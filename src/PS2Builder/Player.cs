using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PS2Builder;

public static class Player
{
    public static void Run(string discRoot)
    {
        var data = Path.Combine(discRoot, ".ps2builder");
        var manifestPath = Path.Combine(data, "manifest.json");
        var m = JsonSerializer.Deserialize<DiscManifest>(File.ReadAllText(manifestPath)) ?? throw new InvalidOperationException("The disc manifest is invalid.");
        var safeSerial = string.Concat(m.Serial.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_'));
        var localRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PS2Builder", "Games", safeSerial);
        var sharedSaves = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games", "PS2Builder", "MemoryCards");
        Directory.CreateDirectory(localRoot); Directory.CreateDirectory(sharedSaves);
        Directory.CreateDirectory(Path.Combine(localRoot, "inis")); Directory.CreateDirectory(Path.Combine(localRoot, "patches"));

        // Selected patch files are copied from the read-only disc into PCSX2's writable data path.
        // Files originating from optical media may carry the ReadOnly attribute. Always normalize
        // the local copy so subsequent launches can update/replace it without elevation.
        var discPatches = Path.Combine(data, "patches");
        if (Directory.Exists(discPatches))
        {
            foreach (var f in Directory.GetFiles(discPatches, "*.pnach"))
            {
                var destination = Path.Combine(localRoot, "patches", Path.GetFileName(f));
                CopyAsWritable(f, destination);
            }
        }

        var biosDir = Path.Combine(data, "firmware");
        var ini = Path.Combine(localRoot, "inis", "PCSX2.ini");
        File.WriteAllText(ini, BuildIni(m, biosDir, sharedSaves));

        EnsureVisualCppRuntime(data);

        var exe = Path.Combine(data, m.Pcsx2ExeRelativePath);
        var game = Path.Combine(data, m.GameRelativePath);
        if (!File.Exists(exe)) throw new FileNotFoundException("The PCSX2 runtime is missing from the disc.", exe);
        if (!File.Exists(game)) throw new FileNotFoundException("The game image is missing from the disc.", game);

        var args = $"-nogui -batch -fullscreen -slowboot -datapath \"{localRoot}\" -- \"{game}\"";
        Process.Start(new ProcessStartInfo(exe, args) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false });
    }

    static void CopyAsWritable(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        if (File.Exists(destination))
        {
            // A previous version of PS2 Builder may have copied the ReadOnly attribute
            // from the mounted ISO/DVD. Clear it before replacing the file.
            var attributes = File.GetAttributes(destination);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(destination, attributes & ~FileAttributes.ReadOnly);
        }

        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
            input.CopyTo(output);

        // Do not propagate read-only/optical-media attributes to the writable local copy.
        File.SetAttributes(destination, FileAttributes.Normal);
    }

    static void EnsureVisualCppRuntime(string data)
    {
        var msvcp = Path.Combine(Environment.SystemDirectory, "MSVCP140.dll");
        var vcruntime = Path.Combine(Environment.SystemDirectory, "VCRUNTIME140.dll");
        if (File.Exists(msvcp) && File.Exists(vcruntime)) return;
        var setup = Path.Combine(data, "prerequisites", "vc_redist.x64.exe");
        if (!File.Exists(setup)) throw new InvalidOperationException("The required Microsoft Visual C++ Runtime is not installed and the offline installer is missing from the disc.");
        using var p = Process.Start(new ProcessStartInfo(setup, "/install /quiet /norestart") { UseShellExecute = true, Verb = "runas" });
        p?.WaitForExit();
        if (!File.Exists(msvcp) || !File.Exists(vcruntime)) throw new InvalidOperationException("The Microsoft Visual C++ Runtime required by PCSX2 could not be installed.");
    }

    static string BuildIni(DiscManifest m, string biosDir, string memcards)
    {
        float upscale = m.Resolution switch {
            ResolutionProfile.Native => 1, ResolutionProfile.X2 => 2, ResolutionProfile.X3 => 3,
            ResolutionProfile.X4 => 4, ResolutionProfile.X6 => 6, _ => AutoUpscale()
        };
        var aspect = m.Aspect switch { AspectProfile.Original4x3 => "4:3", AspectProfile.Widescreen16x9 => "16:9", _ => "Auto 4:3/3:2" };
        bool ws = m.EnabledPatchGroups.Any(x => x.Equals("Widescreen 16:9", StringComparison.OrdinalIgnoreCase));
        bool ni = m.EnabledPatchGroups.Any(x => x.Equals("No-Interlacing", StringComparison.OrdinalIgnoreCase));
        var other = m.EnabledPatchGroups.Where(x => !x.Equals("Widescreen 16:9", StringComparison.OrdinalIgnoreCase) && !x.Equals("No-Interlacing", StringComparison.OrdinalIgnoreCase)).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("[UI]").AppendLine("SettingsVersion = 1").AppendLine("StartFullscreen = true");
        sb.AppendLine("[Folders]").AppendLine($"Bios = {biosDir}").AppendLine($"MemoryCards = {memcards}");
        sb.AppendLine("[Filenames]").AppendLine($"BIOS = {m.BiosFileName}");
        sb.AppendLine("[EmuCore]").AppendLine("EnablePatches = true").AppendLine($"EnableWideScreenPatches = {ws.ToString().ToLowerInvariant()}").AppendLine($"EnableNoInterlacingPatches = {ni.ToString().ToLowerInvariant()}");
        sb.AppendLine("[EmuCore/GS]").AppendLine("Renderer = -1").AppendLine($"upscale_multiplier = {upscale:0.#}").AppendLine($"AspectRatio = {aspect}").AppendLine("deinterlace_mode = 0");
        sb.AppendLine("[MemoryCards]").AppendLine("Slot1_Enable = true").AppendLine("Slot1_Filename = Mcd001.ps2").AppendLine("Slot2_Enable = true").AppendLine("Slot2_Filename = Mcd002.ps2");
        sb.AppendLine("[InputSources]").AppendLine("SDL = true").AppendLine("XInput = true").AppendLine("Keyboard = true");
        if (other.Count > 0) { sb.AppendLine("[Patches]"); foreach (var p in other) sb.AppendLine($"Enable = {p}"); }
        return sb.ToString();
    }

    static float AutoUpscale()
    {
        var b = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0,0,1920,1080);
        var max = Math.Max(b.Width, b.Height);
        return max >= 3500 ? 6 : max >= 2400 ? 4 : max >= 1800 ? 3 : 2;
    }
}
