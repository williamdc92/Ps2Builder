using System.Text;
using System.Text.Json;

namespace PS2Builder;

public static class DiscBuilder
{
    public static async Task BuildAsync(BuildSettings s, GameInfo info)
    {
        ValidateBios(s.BiosPath);
        var runtime = await RuntimeProvider.EnsurePcsx2Async();
        var vcRedist = await PrerequisiteProvider.EnsureVcRedistAsync();
        var staging = Path.Combine(Path.GetTempPath(), "PS2Builder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var data = Path.Combine(staging, ".ps2builder");
            var content = Path.Combine(data, "content");
            var firmware = Path.Combine(data, "firmware");
            var runtimeOut = Path.Combine(data, "runtime");
            var patchOut = Path.Combine(data, "patches");
            var prereqOut = Path.Combine(data, "prerequisites");
            Directory.CreateDirectory(content); Directory.CreateDirectory(firmware); Directory.CreateDirectory(runtimeOut); Directory.CreateDirectory(patchOut); Directory.CreateDirectory(prereqOut);

            File.Copy(Environment.ProcessPath ?? throw new InvalidOperationException("Percorso eseguibile non disponibile."), Path.Combine(staging, "PLAY.exe"), true);
            var gameName = "game" + Path.GetExtension(s.GamePath).ToLowerInvariant();
            File.Copy(s.GamePath, Path.Combine(content, gameName), true);
            var biosName = Path.GetFileName(s.BiosPath);
            File.Copy(s.BiosPath, Path.Combine(firmware, biosName), true);
            CopyDirectory(runtime.Directory, runtimeOut);
            File.Copy(vcRedist, Path.Combine(prereqOut, "vc_redist.x64.exe"), true);

            foreach (var group in info.Patches.Where(p => s.EnabledPatchGroups.Contains(p.Name)).GroupBy(p => p.SourceFile))
            {
                var sb = new StringBuilder();
                foreach (var p in group) sb.AppendLine($"[{p.Name}]").AppendLine(p.Body).AppendLine();
                await File.WriteAllTextAsync(Path.Combine(patchOut, group.Key), sb.ToString(), Encoding.UTF8);
            }

            var manifest = new DiscManifest {
                Title = s.DisplayName, Serial = info.Serial, Region = info.Region,
                GameRelativePath = @"content\" + gameName, BiosFileName = biosName,
                Resolution = s.Resolution, Aspect = s.Aspect, EnabledPatchGroups = s.EnabledPatchGroups,
                RuntimeVersion = runtime.Version, RuntimeSource = runtime.SourceUrl
            };
            await File.WriteAllTextAsync(Path.Combine(data, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            await File.WriteAllTextAsync(Path.Combine(data, "PCSX2_SOURCE.txt"), $"PCSX2 runtime: {runtime.Version}\r\nSource: {runtime.SourceUrl}\r\nLicense: GPL-3.0-or-later; see PCSX2 distribution files/resources for notices.\r\n");

            IconFactory.Create(s.CustomIconPath, Path.Combine(data, "game.ico"));
            var autorun = $"[AutoRun]\r\nopen=PLAY.exe\r\naction=Gioca a {SanitizeIni(s.DisplayName)}\r\nicon=.ps2builder\\game.ico\r\nlabel={SanitizeIni(s.DisplayName)}\r\nshell=play\r\nshell\\play=Gioca\r\nshell\\play\\command=PLAY.exe\r\n";
            await File.WriteAllTextAsync(Path.Combine(staging, "autorun.inf"), autorun, Encoding.ASCII);
            File.SetAttributes(data, File.GetAttributes(data) | FileAttributes.Hidden);

            await Task.Run(() => WindowsIsoWriter.WriteUdfIso(staging, s.OutputIso, MakeVolumeLabel(s.DisplayName)));
        }
        finally { try { Directory.Delete(staging, true); } catch { } }
    }

    static void ValidateBios(string path)
    {
        var len = new FileInfo(path).Length;
        if (len < 2 * 1024 * 1024 || len > 16 * 1024 * 1024) throw new InvalidOperationException("Il file BIOS non ha una dimensione plausibile per un dump PS2.");
    }
    static void CopyDirectory(string src, string dst)
    { foreach (var d in Directory.GetDirectories(src, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(d.Replace(src, dst)); foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories)) { var o = f.Replace(src, dst); Directory.CreateDirectory(Path.GetDirectoryName(o)!); File.Copy(f, o, true); } }
    static string SanitizeIni(string s) => s.Replace("\r", " ").Replace("\n", " ").Replace("=", "-").Trim();
    static string MakeVolumeLabel(string s) { var x = new string(s.ToUpperInvariant().Where(c => char.IsLetterOrDigit(c) || c is ' ' or '_' or '-').ToArray()).Trim(); return x.Length > 32 ? x[..32] : (x.Length == 0 ? "PS2_GAME" : x); }
}
