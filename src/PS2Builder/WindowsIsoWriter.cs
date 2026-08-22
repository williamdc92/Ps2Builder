using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace PS2Builder;

public static class WindowsIsoWriter
{
    // Uses Windows IMAPI2FS; no external ISO authoring program is required.
    public static void WriteUdfIso(string sourceDirectory, string outputIso, string volumeLabel)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("La generazione ISO usa IMAPI2FS ed è supportata su Windows.");
        var t = Type.GetTypeFromProgID("IMAPI2FS.MsftFileSystemImage") ?? throw new InvalidOperationException("IMAPI2FS non disponibile su questo Windows.");
        dynamic fsi = Activator.CreateInstance(t)!;
        fsi.FileSystemsToCreate = 4; // UDF, suitable for files > 4 GiB.
        try { fsi.UDFRevision = 0x0201; } catch { }
        fsi.VolumeName = volumeLabel;
        fsi.Root.AddTree(sourceDirectory, false);
        dynamic result = fsi.CreateResultImage();
        IStream stream = (IStream)result.ImageStream;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputIso))!);
        using var fs = new FileStream(outputIso, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[1024 * 1024];
        var readPtr = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(sizeof(int));
        try
        {
            while (true)
            {
                stream.Read(buffer, buffer.Length, readPtr);
                int read = System.Runtime.InteropServices.Marshal.ReadInt32(readPtr);
                if (read <= 0) break;
                fs.Write(buffer, 0, read);
            }
        }
        finally { System.Runtime.InteropServices.Marshal.FreeCoTaskMem(readPtr); }
    }
}
