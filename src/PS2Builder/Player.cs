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
        var m = JsonSerializer.Deserialize<DiscManifest>(File.ReadAllText(manifestPath))
            ?? throw new InvalidOperationException("The disc manifest is invalid.");

        var safeSerial = SanitizePathComponent(m.Serial, "UNKNOWN");
        var localRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PS2Builder", "Games", safeSerial);
        var sharedSaves = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Saved Games", "PS2Builder", "MemoryCards");

        Directory.CreateDirectory(localRoot);
        Directory.CreateDirectory(sharedSaves);

        EnsureVisualCppRuntime(data);

        var discRuntime = Path.Combine(data, "runtime");
        var game = Path.Combine(data, m.GameRelativePath);
        if (!Directory.Exists(discRuntime))
            throw new DirectoryNotFoundException("The PCSX2 runtime is missing from the disc.");
        if (!File.Exists(game))
            throw new FileNotFoundException("The game image is missing from the disc.", game);

        // Keep a writable PCSX2 runtime per game. This deliberately avoids redirecting
        // portable mode through a relative path containing '..', which PCSX2 2.6.x can
        // pass to Win32 directory creation without canonicalizing first.
        //
        // An empty portable.ini next to pcsx2-qt.exe is enough to enable portable mode.
        // With no portable.txt present, PCSX2 uses the executable directory itself as
        // DataRoot, so inis/patches/cache are all guaranteed to be writable.
        var localRuntime = EnsureLocalRuntime(discRuntime, m, localRoot);
        var iniDir = Path.Combine(localRuntime, "inis");
        var patchDir = Path.Combine(localRuntime, "patches");
        Directory.CreateDirectory(iniDir);
        Directory.CreateDirectory(patchDir);

        // Selected patch files are copied from the read-only disc into PCSX2's writable
        // portable data directory. Normalize attributes so subsequent launches can replace them.
        var discPatches = Path.Combine(data, "patches");
        if (Directory.Exists(discPatches))
        {
            foreach (var f in Directory.GetFiles(discPatches, "*.pnach"))
            {
                var destination = Path.Combine(patchDir, Path.GetFileName(f));
                CopyAsWritable(f, destination);
            }
        }

        var biosDir = Path.Combine(data, "firmware");
        var ini = Path.Combine(iniDir, "PCSX2.ini");
        File.WriteAllText(ini, BuildIni(m, biosDir, sharedSaves), new UTF8Encoding(false));
        File.SetAttributes(ini, FileAttributes.Normal);

        var exe = Path.Combine(localRuntime, "pcsx2-qt.exe");
        if (!File.Exists(exe))
            throw new FileNotFoundException("The cached PCSX2 runtime is incomplete.", exe);

        var args = $"-portable -nogui -batch -fullscreen -slowboot -- \"{game}\"";
        Process.Start(new ProcessStartInfo(exe, args)
        {
            WorkingDirectory = localRuntime,
            UseShellExecute = false
        });
    }

    static string EnsureLocalRuntime(string discRuntime, DiscManifest manifest, string localGameRoot)
    {
        var runtimeRoot = Path.Combine(localGameRoot, "Runtime");
        var marker = Path.Combine(runtimeRoot, ".ps2builder-runtime.json");
        var exe = Path.Combine(runtimeRoot, "pcsx2-qt.exe");

        var markerMatches = false;
        if (File.Exists(marker) && File.Exists(exe))
        {
            try
            {
                var cached = JsonSerializer.Deserialize<RuntimeCacheMarker>(File.ReadAllText(marker));
                markerMatches = cached != null &&
                    string.Equals(cached.Version, manifest.RuntimeVersion ?? "bundled", StringComparison.Ordinal) &&
                    string.Equals(cached.Source, manifest.RuntimeSource, StringComparison.Ordinal);
            }
            catch
            {
                markerMatches = false;
            }
        }

        if (!markerMatches)
        {
            if (Directory.Exists(runtimeRoot))
            {
                NormalizeDirectoryAttributes(runtimeRoot);
                Directory.Delete(runtimeRoot, true);
            }

            Directory.CreateDirectory(runtimeRoot);
            CopyDirectoryAsWritable(discRuntime, runtimeRoot);

            if (!File.Exists(exe))
                throw new InvalidOperationException("The bundled PCSX2 runtime does not contain pcsx2-qt.exe.");

            File.WriteAllText(marker, JsonSerializer.Serialize(
                new RuntimeCacheMarker
                {
                    Version = manifest.RuntimeVersion ?? "bundled",
                    Source = manifest.RuntimeSource
                },
                new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.SetAttributes(marker, FileAttributes.Normal);
        }

        // portable.ini is only a marker file. An empty file makes PCSX2 use AppRoot as
        // its data root. Remove portable.txt because older PS2 Builder test builds may
        // have left a relative redirected path there, and portable.txt takes effect too.
        var portableTxt = Path.Combine(runtimeRoot, "portable.txt");
        if (File.Exists(portableTxt))
        {
            File.SetAttributes(portableTxt, FileAttributes.Normal);
            File.Delete(portableTxt);
        }

        var portableIni = Path.Combine(runtimeRoot, "portable.ini");
        if (File.Exists(portableIni))
            File.SetAttributes(portableIni, FileAttributes.Normal);
        File.WriteAllText(portableIni, string.Empty, new UTF8Encoding(false));
        File.SetAttributes(portableIni, FileAttributes.Normal);

        return runtimeRoot;
    }

    static void CopyDirectoryAsWritable(string sourceRoot, string destinationRoot)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, directory);
            var destination = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(destination);
            try { File.SetAttributes(destination, FileAttributes.Directory); } catch { }
        }

        foreach (var source in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, source);
            var destination = Path.Combine(destinationRoot, relative);
            CopyAsWritable(source, destination);
        }
    }

    static void NormalizeDirectoryAttributes(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
        }

        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(x => x.Length))
        {
            try { File.SetAttributes(directory, FileAttributes.Directory); } catch { }
        }

        try { File.SetAttributes(root, FileAttributes.Directory); } catch { }
    }

    static string SanitizePathComponent(string value, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(result) ? fallback : result;
    }

    static void CopyAsWritable(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        if (File.Exists(destination))
        {
            var attributes = File.GetAttributes(destination);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(destination, attributes & ~FileAttributes.ReadOnly);
        }

        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
            input.CopyTo(output);

        File.SetAttributes(destination, FileAttributes.Normal);
    }

    static void EnsureVisualCppRuntime(string data)
    {
        var msvcp = Path.Combine(Environment.SystemDirectory, "MSVCP140.dll");
        var vcruntime = Path.Combine(Environment.SystemDirectory, "VCRUNTIME140.dll");
        if (File.Exists(msvcp) && File.Exists(vcruntime)) return;
        var setup = Path.Combine(data, "prerequisites", "vc_redist.x64.exe");
        if (!File.Exists(setup))
            throw new InvalidOperationException("The required Microsoft Visual C++ Runtime is not installed and the offline installer is missing from the disc.");
        using var p = Process.Start(new ProcessStartInfo(setup, "/install /quiet /norestart")
        {
            UseShellExecute = true,
            Verb = "runas"
        });
        p?.WaitForExit();
        if (!File.Exists(msvcp) || !File.Exists(vcruntime))
            throw new InvalidOperationException("The Microsoft Visual C++ Runtime required by PCSX2 could not be installed.");
    }

    static string BuildIni(DiscManifest m, string biosDir, string memcards)
    {
        float upscale = m.Resolution switch
        {
            ResolutionProfile.Native => 1,
            ResolutionProfile.X2 => 2,
            ResolutionProfile.X3 => 3,
            ResolutionProfile.X4 => 4,
            ResolutionProfile.X6 => 6,
            _ => AutoUpscale()
        };
        var aspect = m.Aspect switch
        {
            AspectProfile.Original4x3 => "4:3",
            AspectProfile.Widescreen16x9 => "16:9",
            _ => "Auto 4:3/3:2"
        };
        bool ws = m.EnabledPatchGroups.Any(x => x.Equals("Widescreen 16:9", StringComparison.OrdinalIgnoreCase));
        bool ni = m.EnabledPatchGroups.Any(x => x.Equals("No-Interlacing", StringComparison.OrdinalIgnoreCase));
        var other = m.EnabledPatchGroups.Where(x =>
            !x.Equals("Widescreen 16:9", StringComparison.OrdinalIgnoreCase) &&
            !x.Equals("No-Interlacing", StringComparison.OrdinalIgnoreCase)).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("[UI]").AppendLine("SettingsVersion = 1").AppendLine("StartFullscreen = true");
        sb.AppendLine("[Folders]").AppendLine($"Bios = {biosDir}").AppendLine($"MemoryCards = {memcards}");
        sb.AppendLine("[Filenames]").AppendLine($"BIOS = {m.BiosFileName}");
        sb.AppendLine("[EmuCore]").AppendLine("EnablePatches = true")
            .AppendLine($"EnableWideScreenPatches = {ws.ToString().ToLowerInvariant()}")
            .AppendLine($"EnableNoInterlacingPatches = {ni.ToString().ToLowerInvariant()}");
        sb.AppendLine("[EmuCore/GS]").AppendLine("Renderer = -1")
            .AppendLine($"upscale_multiplier = {upscale:0.#}")
            .AppendLine($"AspectRatio = {aspect}")
            .AppendLine("deinterlace_mode = 0");
        sb.AppendLine("[MemoryCards]").AppendLine("Slot1_Enable = true")
            .AppendLine("Slot1_Filename = Mcd001.ps2")
            .AppendLine("Slot2_Enable = true")
            .AppendLine("Slot2_Filename = Mcd002.ps2");
        sb.AppendLine("[InputSources]").AppendLine("SDL = true").AppendLine("XInput = true").AppendLine("Keyboard = true");
        if (other.Count > 0)
        {
            sb.AppendLine("[Patches]");
            foreach (var p in other) sb.AppendLine($"Enable = {p}");
        }
        return sb.ToString();
    }

    static float AutoUpscale()
    {
        var b = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        var max = Math.Max(b.Width, b.Height);
        return max >= 3500 ? 6 : max >= 2400 ? 4 : max >= 1800 ? 3 : 2;
    }

    sealed class RuntimeCacheMarker
    {
        public string Version { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
    }
}
