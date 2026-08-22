using System.Buffers.Binary;
using System.Text;

namespace PS2Builder;

/// <summary>
/// Creates standard formatted 8 MB PS2 memory-card images. The filesystem layout
/// follows the public-domain mymc implementation, including the 528-byte raw page
/// layout (512 bytes data + ECC/spare) expected by PCSX2 .ps2 file cards.
/// Existing non-empty cards are never reformatted.
/// </summary>
internal static class MemoryCardFactory
{
    const int PageSize = 512;
    const int SpareSize = 16;
    const int RawPageSize = PageSize + SpareSize;
    const int PagesPerCluster = 2;
    const int ClusterSize = PageSize * PagesPerCluster;
    const int PagesPerEraseBlock = 16;
    const int PagesPerCard = 16384;
    const int ClustersPerCard = PagesPerCard / PagesPerCluster;
    const int DataImageSize = PageSize * PagesPerCard;
    const int RawImageSize = RawPageSize * PagesPerCard;

    const int FirstIfcCluster = 8;
    const int FatClusterCount = 32;
    const int FirstFatCluster = FirstIfcCluster + 1;
    const int AllocatableClusterOffset = FirstFatCluster + FatClusterCount; // 41
    const int GoodBlock1 = 1023;
    const int GoodBlock2 = 1022;
    const int ClustersPerEraseBlock = PagesPerEraseBlock / PagesPerCluster; // 8
    const int AllocatableClusterEnd = GoodBlock2 * ClustersPerEraseBlock - AllocatableClusterOffset; // 8135

    const uint FatChainEnd = 0xFFFFFFFF;
    const uint FatFree = 0x7FFFFFFF;

    const ushort DF_READ = 0x0001;
    const ushort DF_WRITE = 0x0002;
    const ushort DF_EXECUTE = 0x0004;
    const ushort DF_DIR = 0x0020;
    const ushort DF_0400 = 0x0400;
    const ushort DF_HIDDEN = 0x2000;
    const ushort DF_EXISTS = 0x8000;

    static readonly byte[] Magic = Encoding.ASCII.GetBytes("Sony PS2 Memory Card Format ");
    static readonly byte[] ColumnParityMasks = BuildColumnParityMasks();
    static readonly byte[] ParityTable = BuildParityTable();

    public static void EnsureSharedCards(string directory)
    {
        Directory.CreateDirectory(directory);
        EnsureCard(Path.Combine(directory, "Mcd001.ps2"));
        EnsureCard(Path.Combine(directory, "Mcd002.ps2"));
    }

    static void EnsureCard(string path)
    {
        if (!File.Exists(path))
        {
            CreateFormattedCard(path);
            return;
        }

        var length = new FileInfo(path).Length;
        if (HasFormattedMagic(path) && length == RawImageSize)
            return;

        // Preserve an older valid no-ECC image by converting it to PCSX2's normal raw
        // .ps2 layout rather than destroying any saves it may contain.
        if (HasFormattedMagic(path) && length == DataImageSize)
        {
            ConvertNoEccCardToRaw(path);
            return;
        }

        // Older PS2 Builder builds allowed PCSX2 to create an erased/unformatted card.
        // Replacing a completely uniform 0xFF or 0x00 conventional image is safe; any
        // unknown non-empty image is deliberately left untouched.
        if (IsBlankUnformattedImage(path))
            CreateFormattedCard(path);
    }

    static bool HasFormattedMagic(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < Magic.Length)
                return false;
            Span<byte> header = stackalloc byte[28];
            if (fs.Read(header) != header.Length)
                return false;
            return header.SequenceEqual(Magic);
        }
        catch
        {
            return false;
        }
    }

    static bool IsBlankUnformattedImage(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length == 0)
                return true;
            if (fs.Length != DataImageSize && fs.Length != RawImageSize)
                return false;

            var first = fs.ReadByte();
            if (first is not (0x00 or 0xFF))
                return false;

            var expected = (byte)first;
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (var i = 0; i < read; i++)
                {
                    if (buffer[i] != expected)
                        return false;
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    static void CreateFormattedCard(string path)
    {
        var data = new byte[DataImageSize];
        using (var ms = new MemoryStream(data, writable: true))
        {
            WriteSuperblock(ms);
            WriteIndirectFat(ms);
            WriteFat(ms);
            WriteRootDirectory(ms);
        }

        WriteRawCardAtomically(path, data);
    }

    static void ConvertNoEccCardToRaw(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length != DataImageSize)
            return;
        WriteRawCardAtomically(path, data);
    }

    static void WriteRawCardAtomically(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var spare = new byte[SpareSize];
                var erasedRawPage = Enumerable.Repeat((byte)0xFF, RawPageSize).ToArray();
                var erasedBackupStartPage = GoodBlock2 * PagesPerEraseBlock;
                var erasedBackupEndPage = erasedBackupStartPage + PagesPerEraseBlock;

                for (var page = 0; page < PagesPerCard; page++)
                {
                    if (page >= erasedBackupStartPage && page < erasedBackupEndPage)
                    {
                        fs.Write(erasedRawPage);
                        continue;
                    }

                    var pageData = data.AsSpan(page * PageSize, PageSize);
                    fs.Write(pageData);
                    BuildSpare(pageData, spare);
                    fs.Write(spare);
                }
                fs.Flush(true);
            }

            if (File.Exists(path))
            {
                try { File.SetAttributes(path, FileAttributes.Normal); } catch { }
                File.Delete(path);
            }
            File.Move(temp, path);
            File.SetAttributes(path, FileAttributes.Normal);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    static void BuildSpare(ReadOnlySpan<byte> page, Span<byte> spare)
    {
        spare.Clear();
        for (var chunk = 0; chunk < 4; chunk++)
        {
            var ecc = CalculateEcc(page.Slice(chunk * 128, 128));
            spare[chunk * 3] = ecc.Item1;
            spare[chunk * 3 + 1] = ecc.Item2;
            spare[chunk * 3 + 2] = ecc.Item3;
        }
        // The final four spare bytes remain zero, matching mymc's standard format.
    }

    static (byte, byte, byte) CalculateEcc(ReadOnlySpan<byte> data)
    {
        if (data.Length != 128)
            throw new ArgumentException("PS2 memory-card ECC operates on 128-byte chunks.", nameof(data));

        var columnParity = 0x77;
        var lineParity0 = 0x7F;
        var lineParity1 = 0x7F;

        for (var i = 0; i < data.Length; i++)
        {
            var b = data[i];
            columnParity ^= ColumnParityMasks[b];
            if (ParityTable[b] != 0)
            {
                lineParity0 ^= ~i;
                lineParity1 ^= i;
            }
        }

        return ((byte)columnParity, (byte)(lineParity0 & 0x7F), (byte)lineParity1);
    }

    static byte[] BuildParityTable()
    {
        var table = new byte[256];
        for (var i = 0; i < table.Length; i++)
        {
            var v = i;
            v ^= v >> 1;
            v ^= v >> 2;
            v ^= v >> 4;
            table[i] = (byte)(v & 1);
        }
        return table;
    }

    static byte[] BuildColumnParityMasks()
    {
        var parity = BuildParityTable();
        byte[] masks = [0x55, 0x33, 0x0F, 0x00, 0xAA, 0xCC, 0xF0];
        var table = new byte[256];

        for (var b = 0; b < 256; b++)
        {
            var mask = 0;
            for (var i = 0; i < masks.Length; i++)
                mask |= parity[b & masks[i]] << i;
            table[b] = (byte)mask;
        }
        return table;
    }

    static void WriteSuperblock(Stream fs)
    {
        var page = new byte[PageSize];
        Magic.CopyTo(page, 0);
        Encoding.ASCII.GetBytes("1.2.0.0").CopyTo(page, 0x1C);

        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(0x28, 2), PageSize);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(0x2A, 2), PagesPerCluster);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(0x2C, 2), PagesPerEraseBlock);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(0x2E, 2), 0xFF00);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(0x30, 4), ClustersPerCard);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(0x34, 4), AllocatableClusterOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(0x38, 4), AllocatableClusterEnd);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(0x3C, 4), 0); // root directory FAT cluster
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(0x40, 4), GoodBlock1);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(0x44, 4), GoodBlock2);

        // The superblock IFC list is zero-filled by default; a standard 8 MB card
        // uses only its first entry.
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(0x50, 4), FirstIfcCluster);
        for (var i = 0; i < 32; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(0xD0 + i * 4, 4), 0xFFFFFFFF);

        page[0x150] = 2;
        page[0x151] = 0x2B;

        fs.Position = 0;
        fs.Write(page);
    }

    static void WriteIndirectFat(Stream fs)
    {
        var cluster = Enumerable.Repeat((byte)0xFF, ClusterSize).ToArray();
        for (var i = 0; i < FatClusterCount; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(cluster.AsSpan(i * 4, 4), (uint)(FirstFatCluster + i));
        WritePhysicalCluster(fs, FirstIfcCluster, cluster);
    }

    static void WriteFat(Stream fs)
    {
        for (var fatCluster = 0; fatCluster < FatClusterCount; fatCluster++)
        {
            var cluster = new byte[ClusterSize];
            for (var entry = 0; entry < ClusterSize / 4; entry++)
            {
                var index = fatCluster * (ClusterSize / 4) + entry;
                uint value = index switch
                {
                    0 => FatChainEnd,
                    < AllocatableClusterEnd => FatFree,
                    _ => FatChainEnd
                };
                BinaryPrimitives.WriteUInt32LittleEndian(cluster.AsSpan(entry * 4, 4), value);
            }
            WritePhysicalCluster(fs, FirstFatCluster + fatCluster, cluster);
        }
    }

    static void WriteRootDirectory(Stream fs)
    {
        var cluster = new byte[ClusterSize];
        var now = GetPs2Time();

        var rootMode = (ushort)(DF_READ | DF_WRITE | DF_EXECUTE | DF_DIR | DF_0400 | DF_EXISTS);
        WriteDirEntry(cluster.AsSpan(0, 512), rootMode, 2, now, 0, 0, now, ".");

        var parentMode = (ushort)(DF_WRITE | DF_EXECUTE | DF_DIR | DF_0400 | DF_HIDDEN | DF_EXISTS);
        WriteDirEntry(cluster.AsSpan(512, 512), parentMode, 0, now, 0, 0, now, "..");

        WritePhysicalCluster(fs, AllocatableClusterOffset, cluster);
    }

    static void WriteDirEntry(
        Span<byte> entry,
        ushort mode,
        uint length,
        byte[] created,
        uint cluster,
        uint parentEntry,
        byte[] modified,
        string name)
    {
        entry.Clear();
        BinaryPrimitives.WriteUInt16LittleEndian(entry.Slice(0x00, 2), mode);
        BinaryPrimitives.WriteUInt16LittleEndian(entry.Slice(0x02, 2), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(0x04, 4), length);
        created.CopyTo(entry.Slice(0x08, 8));
        BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(0x10, 4), cluster);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(0x14, 4), parentEntry);
        modified.CopyTo(entry.Slice(0x18, 8));
        BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(0x20, 4), 0);

        var nameBytes = Encoding.ASCII.GetBytes(name);
        nameBytes.AsSpan(0, Math.Min(nameBytes.Length, 447)).CopyTo(entry.Slice(0x40, 448));
    }

    static byte[] GetPs2Time()
    {
        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9));
        var bytes = new byte[8];
        bytes[0] = 0;
        bytes[1] = (byte)now.Second;
        bytes[2] = (byte)now.Minute;
        bytes[3] = (byte)now.Hour;
        bytes[4] = (byte)now.Day;
        bytes[5] = (byte)now.Month;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6, 2), (ushort)now.Year);
        return bytes;
    }

    static void WritePhysicalCluster(Stream fs, int clusterNumber, byte[] data)
    {
        if (data.Length != ClusterSize)
            throw new ArgumentException("Invalid PS2 memory-card cluster size.", nameof(data));
        fs.Position = (long)clusterNumber * ClusterSize;
        fs.Write(data);
    }
}
