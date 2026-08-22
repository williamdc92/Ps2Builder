using System.Diagnostics;
using System.Security.Cryptography;
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
        var sharedSaves = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Saved Games", "PS2Builder", "MemoryCards");
        Directory.CreateDirectory(sharedSaves);
        MemoryCardFactory.EnsureSharedCards(sharedSaves);

        EnsureVisualCppRuntime(data);

        var discRuntime = Path.Combine(data, "runtime");
        var game = Path.Combine(data, m.GameRelativePath);
        if (!Directory.Exists(discRuntime))
            throw new DirectoryNotFoundException("The PCSX2 runtime is missing from the disc.");
        if (!File.Exists(game))
            throw new FileNotFoundException("The game image is missing from the disc.", game);

        var runtimeKey = BuildRuntimeKey(m, discRuntime);
        using var runtimeLock = AcquireRuntimeLock(runtimeKey);

        // A PC only keeps one physical copy of each bundled PCSX2 runtime. All games
        // using the same runtime reuse this directory. Per-game writable PCSX2 data is
        // stored below UserData so PCSX2 2.6.x can reach it with a portable.txt path
        // that never contains fragile ".." components.
        var sharedRuntime = EnsureSharedRuntime(discRuntime, m, runtimeKey);
        var gameDataRoot = Path.Combine(sharedRuntime, "UserData", safeSerial);
        Directory.CreateDirectory(gameDataRoot);

        MigrateLegacyPerGameRuntime(safeSerial, gameDataRoot);
        ConfigurePortableDataRoot(sharedRuntime, safeSerial);

        var iniDir = Path.Combine(gameDataRoot, "inis");
        var patchDir = Path.Combine(gameDataRoot, "patches");
        Directory.CreateDirectory(iniDir);
        Directory.CreateDirectory(patchDir);

        // Selected patch files are copied from the read-only disc into this game's
        // writable PCSX2 data directory. Normalize attributes so later launches can
        // safely replace files copied from optical/read-only media.
        var discPatches = Path.Combine(data, "patches");
        if (Directory.Exists(discPatches))
        {
            foreach (var f in Directory.GetFiles(discPatches, "*.pnach"))
            {
                var destination = Path.Combine(patchDir, Path.GetFileName(f));
                CopyAsWritable(f, destination);
            }
        }

        // PCSX2 creates/updates BIOS sidecars (.nvm/.mec) next to the selected BIOS.
        // Keep the original BIOS on the disc but mirror the small ROM into the writable
        // per-game data directory so those sidecars can persist normally.
        var discBios = Path.Combine(data, "firmware", m.BiosFileName);
        if (!File.Exists(discBios))
            throw new FileNotFoundException("The BIOS file is missing from the disc.", discBios);

        var localBiosDir = Path.Combine(gameDataRoot, "bios");
        Directory.CreateDirectory(localBiosDir);
        var localBios = Path.Combine(localBiosDir, m.BiosFileName);
        CopyAsWritable(discBios, localBios);

        var ini = Path.Combine(iniDir, "PCSX2.ini");
        File.WriteAllText(ini, BuildIni(m, localBiosDir, sharedSaves), new UTF8Encoding(false));
        File.SetAttributes(ini, FileAttributes.Normal);

        var exe = Path.Combine(sharedRuntime, "pcsx2-qt.exe");
        if (!File.Exists(exe))
            throw new FileNotFoundException("The cached PCSX2 runtime is incomplete.", exe);

        var args = $"-portable -nogui -batch -fullscreen -slowboot -- \"{game}\"";
        using var process = Process.Start(new ProcessStartInfo(exe, args)
        {
            WorkingDirectory = sharedRuntime,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("PCSX2 could not be started.");

        // Keep PLAY.exe alive for the whole session. It owns the user-facing escape
        // overlay and ensures the PCSX2 interface never becomes part of normal usage.
        Application.Run(new PlaySessionContext(process, m.Title, exe));
    }

    static string BuildRuntimeKey(DiscManifest manifest, string discRuntime)
    {
        var version = SanitizePathComponent(manifest.RuntimeVersion ?? "bundled", "bundled");
        var runtimeExe = Path.Combine(discRuntime, "pcsx2-qt.exe");
        if (!File.Exists(runtimeExe))
            throw new FileNotFoundException("The bundled PCSX2 runtime does not contain pcsx2-qt.exe.", runtimeExe);

        using var stream = File.OpenRead(runtimeExe);
        var hash = Convert.ToHexString(SHA256.HashData(stream))[..10];
        return $"{version}-{hash}";
    }

    static RuntimeLockLease AcquireRuntimeLock(string runtimeKey)
    {
        var mutexName = $"Local\\PS2Builder.Runtime.{runtimeKey}";
        var mutex = new Mutex(false, mutexName);
        try
        {
            if (!mutex.WaitOne(0))
            {
                mutex.Dispose();
                throw new InvalidOperationException("Another PS2 Builder game using this PCSX2 runtime is already running.");
            }
            return new RuntimeLockLease(mutex);
        }
        catch (AbandonedMutexException)
        {
            return new RuntimeLockLease(mutex);
        }
    }

    static string EnsureSharedRuntime(string discRuntime, DiscManifest manifest, string runtimeKey)
    {
        var runtimeRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PS2Builder", "Runtimes", runtimeKey);
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
            Directory.CreateDirectory(runtimeRoot);

            // Do not delete runtimeRoot here: UserData contains persistent data from all
            // games using this runtime. The runtime key includes version/source identity,
            // so a different runtime naturally receives a different cache directory.
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

        return runtimeRoot;
    }

    static void ConfigurePortableDataRoot(string runtimeRoot, string safeSerial)
    {
        // PCSX2 2.6.x combines AppRoot with the text in portable.txt. Keep the selected
        // data directory below AppRoot so the redirect is a simple child path and never
        // relies on parent traversal. A runtime lock is held for the entire session,
        // because portable.txt is intentionally shared by the one runtime instance.
        var relativeDataRoot = Path.Combine("UserData", safeSerial);
        var portableTxt = Path.Combine(runtimeRoot, "portable.txt");
        File.WriteAllText(portableTxt, relativeDataRoot, new UTF8Encoding(false));
        File.SetAttributes(portableTxt, FileAttributes.Normal);

        // portable.txt is sufficient. Remove a stale portable.ini from older test builds
        // so the cache has one clear portable-mode configuration source.
        var portableIni = Path.Combine(runtimeRoot, "portable.ini");
        if (File.Exists(portableIni))
        {
            File.SetAttributes(portableIni, FileAttributes.Normal);
            File.Delete(portableIni);
        }
    }

    static void MigrateLegacyPerGameRuntime(string safeSerial, string destinationDataRoot)
    {
        var legacyGameRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PS2Builder", "Games", safeSerial);
        var legacyRuntime = Path.Combine(legacyGameRoot, "Runtime");
        if (!Directory.Exists(legacyRuntime))
            return;

        // Previous builds cached a full PCSX2 copy per game. Preserve only directories
        // which can contain user/runtime state, then remove the duplicated emulator.
        var writableDirectories = new[]
        {
            "bios", "inis", "patches", "cache", "sstates", "snaps", "textures",
            "inputprofiles", "logs", "cheats", "covers", "gamesettings", "videos"
        };

        foreach (var name in writableDirectories)
        {
            var source = Path.Combine(legacyRuntime, name);
            if (!Directory.Exists(source))
                continue;
            var destination = Path.Combine(destinationDataRoot, name);
            CopyDirectoryAsWritable(source, destination);
        }

        try
        {
            NormalizeDirectoryAttributes(legacyRuntime);
            Directory.Delete(legacyRuntime, true);
            if (Directory.Exists(legacyGameRoot) && !Directory.EnumerateFileSystemEntries(legacyGameRoot).Any())
                Directory.Delete(legacyGameRoot);
        }
        catch
        {
            // Migration is best-effort. A locked legacy file must not prevent gameplay.
        }
    }

    static void CopyDirectoryAsWritable(string sourceRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);

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
        sb.AppendLine("[UI]")
            .AppendLine("SettingsVersion = 1")
            .AppendLine("ConfirmShutdown = false")
            .AppendLine("StartPaused = false")
            .AppendLine("PauseOnFocusLoss = true")
            .AppendLine("StartFullscreen = true")
            .AppendLine("DoubleClickTogglesFullscreen = false")
            .AppendLine("HideMouseCursor = true")
            .AppendLine("RenderToSeparateWindow = false")
            .AppendLine("HideMainWindowWhenRunning = true")
            .AppendLine("DisableWindowResize = true");
        sb.AppendLine("[Folders]").AppendLine($"Bios = {biosDir}").AppendLine($"MemoryCards = {memcards}");
        sb.AppendLine("[Filenames]").AppendLine($"BIOS = {m.BiosFileName}");
        sb.AppendLine("[EmuCore]").AppendLine("EnablePatches = true")
            .AppendLine($"EnableWideScreenPatches = {ws.ToString().ToLowerInvariant()}")
            .AppendLine($"EnableNoInterlacingPatches = {ni.ToString().ToLowerInvariant()}");
        sb.AppendLine("[EmuCore/GS]").AppendLine("Renderer = -1")
            .AppendLine($"upscale_multiplier = {upscale:0.#}")
            .AppendLine($"AspectRatio = {aspect}")
            .AppendLine("deinterlace_mode = 0")
            .AppendLine("ExclusiveFullscreenControl = 0");
        sb.AppendLine("[MemoryCards]").AppendLine("Slot1_Enable = true")
            .AppendLine("Slot1_Filename = Mcd001.ps2")
            .AppendLine("Slot2_Enable = true")
            .AppendLine("Slot2_Filename = Mcd002.ps2");

        // PCSX2 enumerating a controller is not enough: automatic mapping normally writes
        // a [PadN] table. Preconfigure the first two SDL pads using PCSX2 generic bindings.
        sb.AppendLine("[InputSources]")
            .AppendLine("SDL = true")
            .AppendLine("XInput = false")
            .AppendLine("DInput = false")
            .AppendLine("Keyboard = true")
            .AppendLine("Mouse = true");
        sb.AppendLine("[Pad]")
            .AppendLine("MultitapPort1 = false")
            .AppendLine("MultitapPort2 = false");
        AppendSdlPadMapping(sb, 1, 0, includeKeyboardFallback: true);
        AppendSdlPadMapping(sb, 2, 1, includeKeyboardFallback: false);

        // PLAY.exe owns the normal exit flow. Prevent Escape and fullscreen shortcuts from
        // exposing the PCSX2 UI, and disable the GUI's double-click fullscreen toggle above.
        sb.AppendLine("[Hotkeys]")
            .AppendLine("OpenPauseMenu =")
            .AppendLine("ToggleFullscreen =");

        if (other.Count > 0)
        {
            sb.AppendLine("[Patches]");
            foreach (var p in other) sb.AppendLine($"Enable = {p}");
        }
        return sb.ToString();
    }

    static void AppendSdlPadMapping(StringBuilder sb, int padNumber, int sdlIndex, bool includeKeyboardFallback)
    {
        var d = $"SDL-{sdlIndex}";
        sb.AppendLine($"[Pad{padNumber}]")
            .AppendLine("Type = DualShock2")
            .AppendLine("InvertL = 0")
            .AppendLine("InvertR = 0")
            .AppendLine("Deadzone = 0")
            .AppendLine("AxisScale = 1.33")
            .AppendLine("TriggerDeadzone = 0")
            .AppendLine("TriggerScale = 1")
            .AppendLine("LargeMotorScale = 1")
            .AppendLine("SmallMotorScale = 1")
            .AppendLine("ButtonDeadzone = 0")
            .AppendLine("PressureModifier = 0.5")
            .AppendLine($"Up = {d}/DPadUp")
            .AppendLine($"Right = {d}/DPadRight")
            .AppendLine($"Down = {d}/DPadDown")
            .AppendLine($"Left = {d}/DPadLeft")
            .AppendLine($"Triangle = {d}/FaceNorth")
            .AppendLine($"Circle = {d}/FaceEast")
            .AppendLine($"Cross = {d}/FaceSouth")
            .AppendLine($"Square = {d}/FaceWest")
            .AppendLine($"Select = {d}/Back")
            .AppendLine($"Start = {d}/Start")
            .AppendLine($"L1 = {d}/LeftShoulder")
            .AppendLine($"L2 = {d}/+LeftTrigger")
            .AppendLine($"R1 = {d}/RightShoulder")
            .AppendLine($"R2 = {d}/+RightTrigger")
            .AppendLine($"L3 = {d}/LeftStick")
            .AppendLine($"R3 = {d}/RightStick")
            .AppendLine($"LUp = {d}/-LeftY")
            .AppendLine($"LRight = {d}/+LeftX")
            .AppendLine($"LDown = {d}/+LeftY")
            .AppendLine($"LLeft = {d}/-LeftX")
            .AppendLine($"RUp = {d}/-RightY")
            .AppendLine($"RRight = {d}/+RightX")
            .AppendLine($"RDown = {d}/+RightY")
            .AppendLine($"RLeft = {d}/-RightX")
            .AppendLine($"Analog = {d}/Guide")
            .AppendLine($"LargeMotor = {d}/LargeMotor")
            .AppendLine($"SmallMotor = {d}/SmallMotor");

        if (includeKeyboardFallback)
            AppendKeyboardFallback(sb);
    }

    static void AppendKeyboardFallback(StringBuilder sb)
    {
        // PCSX2 accepts repeated values for the same Pad binding. This keeps SDL as
        // the primary zero-configuration controller path while making the keyboard a
        // simultaneous fallback when no gamepad is connected.
        sb.AppendLine("Up = Keyboard/Up")
            .AppendLine("Right = Keyboard/Right")
            .AppendLine("Down = Keyboard/Down")
            .AppendLine("Left = Keyboard/Left")
            .AppendLine("Triangle = Keyboard/I")
            .AppendLine("Circle = Keyboard/L")
            .AppendLine("Cross = Keyboard/K")
            .AppendLine("Square = Keyboard/J")
            .AppendLine("Select = Keyboard/Backspace")
            .AppendLine("Start = Keyboard/Return")
            .AppendLine("L1 = Keyboard/Q")
            .AppendLine("L2 = Keyboard/1")
            .AppendLine("R1 = Keyboard/E")
            .AppendLine("R2 = Keyboard/3")
            .AppendLine("L3 = Keyboard/2")
            .AppendLine("R3 = Keyboard/4")
            .AppendLine("LUp = Keyboard/W")
            .AppendLine("LRight = Keyboard/D")
            .AppendLine("LDown = Keyboard/S")
            .AppendLine("LLeft = Keyboard/A")
            .AppendLine("RUp = Keyboard/T")
            .AppendLine("RRight = Keyboard/H")
            .AppendLine("RDown = Keyboard/G")
            .AppendLine("RLeft = Keyboard/F")
            .AppendLine("Analog = Keyboard/Tab")
            .AppendLine("Pressure = Keyboard/Shift");
    }

    static float AutoUpscale()
    {
        var b = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        var max = Math.Max(b.Width, b.Height);
        return max >= 3500 ? 6 : max >= 2400 ? 4 : max >= 1800 ? 3 : 2;
    }

    sealed class RuntimeLockLease : IDisposable
    {
        readonly Mutex mutex;
        bool disposed;

        public RuntimeLockLease(Mutex mutex) => this.mutex = mutex;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { mutex.ReleaseMutex(); } catch (ApplicationException) { }
            mutex.Dispose();
        }
    }

    sealed class RuntimeCacheMarker
    {
        public string Version { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
    }
}
