using System.IO.Compression;
using System.Text.Json;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace PS2Builder;

public sealed record RuntimePackage(string Directory, string Version, string SourceUrl);

public static class RuntimeProvider
{
    static readonly HttpClient Http = Create();
    static HttpClient Create() { var h = new HttpClient(); h.DefaultRequestHeaders.UserAgent.ParseAdd("PS2Builder/0.1"); return h; }

    public static async Task<RuntimePackage> EnsurePcsx2Async()
    {
        var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PS2Builder", "cache", "pcsx2");
        Directory.CreateDirectory(cache);
        var marker = Path.Combine(cache, "runtime.json");
        if (File.Exists(marker))
        {
            var m = JsonSerializer.Deserialize<RuntimeMarker>(await File.ReadAllTextAsync(marker));
            if (m != null && File.Exists(Path.Combine(cache, "pcsx2-qt.exe"))) return new RuntimePackage(cache, m.Version, m.SourceUrl);
        }

        using var resp = await Http.GetAsync("https://api.github.com/repos/PCSX2/pcsx2/releases/latest");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var version = doc.RootElement.GetProperty("tag_name").GetString() ?? "latest";
        var assets = doc.RootElement.GetProperty("assets").EnumerateArray().ToList();
        var asset = assets.FirstOrDefault(a =>
        {
            var n = a.GetProperty("name").GetString() ?? "";
            return n.Contains("windows", StringComparison.OrdinalIgnoreCase) && n.Contains("x64", StringComparison.OrdinalIgnoreCase) && (n.EndsWith(".7z") || n.EndsWith(".zip"));
        });
        if (asset.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException("Non trovo un runtime PCSX2 Windows x64 nella release corrente.");
        var url = asset.GetProperty("browser_download_url").GetString()!;
        var file = Path.Combine(Path.GetTempPath(), "pcsx2-runtime" + Path.GetExtension(url));
        await using (var dst = File.Create(file)) await (await Http.GetStreamAsync(url)).CopyToAsync(dst);
        if (Directory.Exists(cache)) Directory.Delete(cache, true);
        Directory.CreateDirectory(cache);
        if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) ZipFile.ExtractToDirectory(file, cache);
        else
        {
            using var archive = ArchiveFactory.OpenArchive(file);
            archive.WriteToDirectory(cache, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
        }
        FlattenSingleDirectory(cache);
        if (!File.Exists(Path.Combine(cache, "pcsx2-qt.exe"))) throw new InvalidOperationException("Runtime PCSX2 estratto, ma pcsx2-qt.exe non è stato trovato.");
        await File.WriteAllTextAsync(marker, JsonSerializer.Serialize(new RuntimeMarker { Version = version, SourceUrl = url }, new JsonSerializerOptions { WriteIndented = true }));
        return new RuntimePackage(cache, version, url);
    }

    static void FlattenSingleDirectory(string root)
    {
        var files = Directory.GetFiles(root);
        var dirs = Directory.GetDirectories(root);
        if (files.Length == 0 && dirs.Length == 1)
        {
            var inner = dirs[0];
            foreach (var f in Directory.GetFiles(inner)) File.Move(f, Path.Combine(root, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(inner)) Directory.Move(d, Path.Combine(root, Path.GetFileName(d)));
            Directory.Delete(inner, true);
        }
    }
    sealed class RuntimeMarker { public string Version { get; set; } = ""; public string SourceUrl { get; set; } = ""; }
}
