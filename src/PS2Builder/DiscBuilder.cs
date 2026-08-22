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

            File.Copy(Environment.ProcessPath ?? throw new InvalidOperationException("The current executable path is not available."), Path.Combine(staging, "PLAY.exe"), true);
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

            // Keep the shell icon in the root of the optical volume. Explorer/AutoPlay
            // resolve root-level .ico files more consistently than icons stored inside a
            // hidden data directory. The file itself is hidden from normal Explorer views.
            var rootIcon = Path.Combine(staging, "game.ico");
            IconFactory.Create(s.CustomIconPath, rootIcon);
            // Use a project-specific shell verb rather than the generic "play" verb.
            // Windows uses this verb as the optical drive's default double-click action,
            // while action+shellexecute provide the AutoPlay entry shown when the disc is
            // inserted or mounted. Do not set UseAutoPlay=1: on modern Windows that can
            // suppress the application action from the AutoPlay chooser.
            var displayName = SanitizeIni(s.DisplayName);
            var autorun =
                "[AutoRun]\r\n" +
                "shellexecute=PLAY.exe\r\n" +
                $"action=Play {displayName}\r\n" +
                "icon=game.ico,0\r\n" +
                "defaulticon=game.ico,0\r\n" +
                $"label={displayName}\r\n" +
                "shell=ps2builderplay\r\n" +
                $"shell\\ps2builderplay=Play {displayName}\r\n" +
                "shell\\ps2builderplay\\command=PLAY.exe\r\n";
            var autorunPath = Path.Combine(staging, "autorun.inf");
            await File.WriteAllTextAsync(autorunPath, autorun, Encoding.ASCII);
            File.SetAttributes(rootIcon, File.GetAttributes(rootIcon) | FileAttributes.Hidden | FileAttributes.System);
            File.SetAttributes(autorunPath, File.GetAttributes(autorunPath) | FileAttributes.Hidden | FileAttributes.System);
            File.SetAttributes(data, File.GetAttributes(data) | FileAttributes.Hidden);

            await Task.Run(() => WindowsIsoWriter.WriteUdfIso(staging, s.OutputIso, MakeVolumeLabel(s.DisplayName)));
        }
        finally { try { Directory.Delete(staging, true); } catch { } }
    }

    static void ValidateBios(string path)
    {
        var len = new FileInfo(path).Length;
        if (len < 2 * 1024 * 1024 || len > 16 * 1024 * 1024) throw new InvalidOperationException("The selected BIOS file does not have a plausible size for a PS2 BIOS dump.");
    }
    static void CopyDirectory(string src, string dst)
    { foreach (var d in Directory.GetDirectories(src, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(d.Replace(src, dst)); foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories)) { var o = f.Replace(src, dst); Directory.CreateDirectory(Path.GetDirectoryName(o)!); File.Copy(f, o, true); } }
    static string SanitizeIni(string s) => s.Replace("\r", " ").Replace("\n", " ").Replace("=", "-").Trim();
    static string MakeVolumeLabel(string s) { var x = new string(s.ToUpperInvariant().Where(c => char.IsLetterOrDigit(c) || c is ' ' or '_' or '-').ToArray()).Trim(); return x.Length > 32 ? x[..32] : (x.Length == 0 ? "PS2_GAME" : x); }
}
