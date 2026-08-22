using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace PS2Builder;

public static class GameDatabase
{
    static readonly HttpClient Http = CreateHttp();
    static HttpClient CreateHttp() { var h = new HttpClient(); h.DefaultRequestHeaders.UserAgent.ParseAdd("PS2Builder/0.3"); return h; }

    public static async Task<GameInfo> DetectAsync(string imagePath)
    {
        var disc = await Task.Run(() => Ps2DiscReader.Inspect(imagePath));
        var serial = disc.Serial;
        var info = new GameInfo { Serial = serial ?? "UNKNOWN", Crc = disc.ElfCrc, Title = Path.GetFileNameWithoutExtension(imagePath) };
        if (serial is null) return info;
        await EnrichFromGameIndex(info);
        await EnrichPatches(info);
        return info;
    }

    static async Task EnrichFromGameIndex(GameInfo info)
    {
        try
        {
            var yaml = await Http.GetStringAsync("https://raw.githubusercontent.com/PCSX2/pcsx2/master/bin/resources/GameIndex.yaml");
            var des = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
            var db = des.Deserialize<Dictionary<string, GameDbEntry>>(yaml);
            var pair = db.FirstOrDefault(k => k.Key.Equals(info.Serial, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(pair.Key) && pair.Value != null)
            {
                info.Title = pair.Value.name ?? info.Title;
                info.Region = pair.Value.region ?? info.Region;
            }
        }
        catch { /* offline is allowed */ }
    }

    static async Task EnrichPatches(GameInfo info)
    {
        // PCSX2 patch files are revision-specific (SERIAL_CRC.pnach). If the ELF CRC
        // could not be determined, selecting every SERIAL_* file risks applying a patch
        // made for a different revision of the game. In that case, disable automatic
        // patch discovery rather than guessing.
        if (string.IsNullOrWhiteSpace(info.Crc))
            return;

        try
        {
            var expectedName = $"{info.Serial}_{info.Crc}.pnach";
            using var response = await Http.GetAsync("https://github.com/PCSX2/pcsx2_patches/archive/refs/heads/main.zip");
            response.EnsureSuccessStatusCode();
            using var ms = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            foreach (var e in zip.Entries.Where(e =>
                e.FullName.Contains("/patches/") &&
                e.Name.Equals(expectedName, StringComparison.OrdinalIgnoreCase)))
            {
                using var sr = new StreamReader(e.Open());
                var text = await sr.ReadToEndAsync();
                foreach (var group in Pnach.ParseGroups(text, e.Name)) info.Patches.Add(group);
            }
        }
        catch { /* still buildable offline */ }
    }

    sealed class GameDbEntry { public string? name { get; set; } public string? region { get; set; } }
}

public sealed record DiscInspection(string? Serial, string? ElfCrc);

public static class Ps2DiscReader
{
    static readonly Regex SerialRegex = new(@"([A-Z]{4})[_-](\d{3})\.(\d{2})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static DiscInspection Inspect(string path)
    {
        using var fs = File.OpenRead(path);
        try
        {
            const int sector = 2048;
            fs.Position = 16L * sector;
            var pvd = new byte[sector]; fs.ReadExactly(pvd);
            if (pvd[0] != 1 || Encoding.ASCII.GetString(pvd, 1, 5) != "CD001") throw new InvalidDataException();
            var root = ParseRecord(pvd, 156);
            var cnf = Find(fs, root, "SYSTEM.CNF") ?? throw new InvalidDataException();
            var cnfText = Encoding.ASCII.GetString(ReadExtent(fs, cnf));
            var m = SerialRegex.Match(cnfText);
            if (!m.Success) return new DiscInspection(null, null);
            var serial = $"{m.Groups[1].Value.ToUpperInvariant()}-{m.Groups[2].Value}{m.Groups[3].Value}";
            var elfToken = m.Value.ToUpperInvariant(); // e.g. SLUS_203.12
            var elf = Find(fs, root, elfToken) ?? Find(fs, root, elfToken.Replace('_','-'));
            var crc = elf is null ? null : ElfCrc(ReadExtent(fs, elf)).ToString("X8");
            return new DiscInspection(serial, crc);
        }
        catch
        {
            fs.Position = 0;
            var max = Math.Min(fs.Length, 64L * 1024 * 1024); var buffer = new byte[1024 * 1024]; var tail = ""; long read = 0;
            while (read < max) { int n = fs.Read(buffer,0,(int)Math.Min(buffer.Length,max-read)); if(n<=0) break; var txt=tail+Encoding.ASCII.GetString(buffer,0,n); var m=SerialRegex.Match(txt); if(m.Success) return new DiscInspection($"{m.Groups[1].Value.ToUpperInvariant()}-{m.Groups[2].Value}{m.Groups[3].Value}", null); tail=txt.Length>256?txt[^256..]:txt; read+=n; }
            return new DiscInspection(null, null);
        }
    }

    sealed record IsoEntry(uint Lba, uint Size, bool Directory, string Name);
    static IsoEntry ParseRecord(byte[] b, int o)
    {
        uint lba = BitConverter.ToUInt32(b, o + 2); uint size = BitConverter.ToUInt32(b, o + 10); bool dir = (b[o + 25] & 2) != 0; int n = b[o + 32];
        var name = n == 1 && b[o + 33] <= 1 ? (b[o + 33] == 0 ? "." : "..") : Encoding.ASCII.GetString(b, o + 33, n).Split(';')[0];
        return new IsoEntry(lba,size,dir,name);
    }
    static IsoEntry? Find(FileStream fs, IsoEntry dir, string wanted)
    {
        foreach (var e in List(fs,dir))
        {
            if (e.Name.Equals(wanted,StringComparison.OrdinalIgnoreCase)) return e;
            if (e.Directory && e.Name is not "." and not "..") { var r=Find(fs,e,wanted); if(r!=null) return r; }
        }
        return null;
    }
    static IEnumerable<IsoEntry> List(FileStream fs, IsoEntry dir)
    {
        var bytes=ReadExtent(fs,dir); int o=0;
        while(o<bytes.Length) { int len=bytes[o]; if(len==0){o=((o/2048)+1)*2048; continue;} if(o+len>bytes.Length) yield break; yield return ParseRecord(bytes,o); o+=len; }
    }
    static byte[] ReadExtent(FileStream fs, IsoEntry e) { var b=new byte[checked((int)e.Size)]; fs.Position=(long)e.Lba*2048; fs.ReadExactly(b); return b; }
    static uint ElfCrc(byte[] data) { uint crc=0; int words=data.Length/4; for(int i=0;i<words;i++) crc ^= BitConverter.ToUInt32(data,i*4); return crc; }
}

public static class Pnach
{
    static readonly Regex Header = new(@"^\s*\[(.+?)\]\s*$", RegexOptions.Multiline);
    public static IEnumerable<PatchGroupInfo> ParseGroups(string text, string source)
    {
        var matches = Header.Matches(text);
        for (int i = 0; i < matches.Count; i++)
        {
            var name = matches[i].Groups[1].Value.Trim();
            int start = matches[i].Index + matches[i].Length;
            int end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var body = text[start..end].Trim();
            if (!body.Contains("patch", StringComparison.OrdinalIgnoreCase) && !body.Contains("gsaspectratio", StringComparison.OrdinalIgnoreCase)) continue;
            yield return new PatchGroupInfo { Name = name, SourceFile = source, Body = body, Recommended = name.Contains("No-Interlacing", StringComparison.OrdinalIgnoreCase) || name.Contains("Progressive", StringComparison.OrdinalIgnoreCase) };
        }
    }
}
