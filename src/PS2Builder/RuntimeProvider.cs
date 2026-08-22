using System.IO.Compression;
using System.Text.Json;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace PS2Builder;

public sealed record RuntimePackage(string Directory, string Version, string SourceUrl);

public static class RuntimeProvider
{
    static readonly HttpClient Http = Create();

    static HttpClient Create()
    {
        var h = new HttpClient();
        h.DefaultRequestHeaders.UserAgent.ParseAdd("PS2Builder/0.3");
        return h;
    }

    public static async Task<RuntimePackage> EnsurePcsx2Async()
    {
        var cache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PS2Builder", "cache", "pcsx2");

        Directory.CreateDirectory(cache);
        var marker = Path.Combine(cache, "runtime.json");
        var expectedExe = Path.Combine(cache, "pcsx2-qt.exe");

        if (File.Exists(marker) && File.Exists(expectedExe))
        {
            var m = JsonSerializer.Deserialize<RuntimeMarker>(await File.ReadAllTextAsync(marker));
            if (m != null)
                return new RuntimePackage(cache, m.Version, m.SourceUrl);
        }

        using var resp = await Http.GetAsync("https://api.github.com/repos/PCSX2/pcsx2/releases/latest");
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var version = doc.RootElement.GetProperty("tag_name").GetString() ?? "latest";

        // GitHub releases also contain a Windows x64 *symbols* archive.  The old
        // filter only checked for "windows" + "x64" and could therefore select
        // the symbols package, which naturally does not contain pcsx2-qt.exe.
        var candidates = doc.RootElement
            .GetProperty("assets")
            .EnumerateArray()
            .Select(a => new ReleaseAsset(
                a.GetProperty("name").GetString() ?? string.Empty,
                a.GetProperty("browser_download_url").GetString() ?? string.Empty))
            .Where(a => IsWindowsPortableRuntime(a.Name))
            .OrderByDescending(a => a.Name.EndsWith("-windows-x64-Qt.7z", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(a => a.Name.EndsWith("-windows-x64-Qt.zip", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var asset = candidates.FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Could not find the official PCSX2 Windows x64 Qt portable runtime in the current release.");

        var extension = asset.Name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ? ".7z" : ".zip";
        var tempFile = Path.Combine(Path.GetTempPath(), $"pcsx2-runtime-{Guid.NewGuid():N}{extension}");

        try
        {
            await using (var dst = File.Create(tempFile))
                await (await Http.GetStreamAsync(asset.Url)).CopyToAsync(dst);

            if (Directory.Exists(cache))
                Directory.Delete(cache, true);
            Directory.CreateDirectory(cache);

            if (tempFile.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(tempFile, cache);
            }
            else
            {
                using var archive = ArchiveFactory.OpenArchive(tempFile);
                archive.WriteToDirectory(cache, new ExtractionOptions
                {
                    ExtractFullPath = true,
                    Overwrite = true
                });
            }

            FlattenWrapperDirectories(cache);

            if (!File.Exists(expectedExe))
            {
                var discovered = Directory
                    .EnumerateFiles(cache, "pcsx2-qt.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();

                var detail = discovered == null
                    ? "pcsx2-qt.exe was not present in the extracted package"
                    : $"pcsx2-qt.exe was found at an unexpected location: {Path.GetRelativePath(cache, discovered)}";

                throw new InvalidOperationException(
                    $"PCSX2 runtime extraction failed: {detail}. Selected release asset: {asset.Name}");
            }

            await File.WriteAllTextAsync(marker, JsonSerializer.Serialize(
                new RuntimeMarker
                {
                    Version = version,
                    SourceUrl = asset.Url,
                    AssetName = asset.Name
                },
                new JsonSerializerOptions { WriteIndented = true }));

            return new RuntimePackage(cache, version, asset.Url);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    static bool IsWindowsPortableRuntime(string name)
    {
        if (!(name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
              name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
            return false;

        return name.Contains("windows", StringComparison.OrdinalIgnoreCase)
            && name.Contains("x64", StringComparison.OrdinalIgnoreCase)
            && name.Contains("Qt", StringComparison.OrdinalIgnoreCase)
            && !name.Contains("symbols", StringComparison.OrdinalIgnoreCase)
            && !name.Contains("installer", StringComparison.OrdinalIgnoreCase);
    }

    static void FlattenWrapperDirectories(string root)
    {
        // Official portable releases are normally already flat. This also
        // tolerates archives which wrap the runtime in one or more directories.
        while (!File.Exists(Path.Combine(root, "pcsx2-qt.exe")))
        {
            var files = Directory.GetFiles(root);
            var dirs = Directory.GetDirectories(root);

            if (files.Length != 0 || dirs.Length != 1)
                break;

            var inner = dirs[0];

            foreach (var f in Directory.GetFiles(inner))
                File.Move(f, Path.Combine(root, Path.GetFileName(f)), true);

            foreach (var d in Directory.GetDirectories(inner))
                Directory.Move(d, Path.Combine(root, Path.GetFileName(d)));

            Directory.Delete(inner, true);
        }
    }

    sealed record ReleaseAsset(string Name, string Url);

    sealed class RuntimeMarker
    {
        public string Version { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;
        public string AssetName { get; set; } = string.Empty;
    }
}
