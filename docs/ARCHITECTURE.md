# Architecture

## Builder Mode

The application runs in **Builder Mode** when it does not find `.ps2builder/manifest.json` next to the executable.

1. Reads the PS2 game dump.
2. Extracts the game serial from `SYSTEM.CNF`.
3. Locates the boot ELF inside the ISO9660 filesystem and calculates the XOR-based CRC used by PCSX2.
4. Queries the PCSX2 GameIndex and patch database.
5. Downloads or retrieves the cached Windows x64 PCSX2 runtime.
6. Creates a staging directory.
7. Copies itself into the staging directory as `PLAY.exe`.
8. Adds the game dump, BIOS, PCSX2 runtime, selected patches and generated manifest.
9. Generates `autorun.inf` and the disc icon.
10. Produces the final UDF image through Windows IMAPI2FS.

## Player Mode

The same binary runs in **Player Mode** when it finds `.ps2builder/manifest.json` next to the executable.

1. Reads the disc manifest.
2. Ensures the shared memory card directory exists.
3. Identifies the bundled PCSX2 runtime by version/source and caches **one shared physical copy per runtime version** under `%LOCALAPPDATA%\PS2Builder\Runtimes\`.
4. Acquires a per-runtime session lock. A shared PCSX2 runtime is intentionally used by only one active PS2 Builder session at a time because PCSX2 2.6.x uses a runtime-level `portable.txt` file.
5. Creates the game's writable data directory below the shared runtime at `UserData\<SERIAL>\`.
6. Writes `portable.txt` with the child path `UserData\<SERIAL>`, avoiding `..` traversal while keeping a single PCSX2 runtime.
7. Migrates and removes obsolete per-game runtime copies created by older PS2 Builder test builds.
8. Copies selected patches into the game's writable `patches` directory.
9. Mirrors the BIOS ROM into the writable game data directory so PCSX2 can create/update `.nvm` and `.mec` sidecars.
10. Generates `PCSX2.ini` with automatic controller mappings, fullscreen settings and PS2 Builder-owned exit behavior.
11. Launches the shared PCSX2 runtime using `-portable -nogui -batch -fullscreen -slowboot`.
12. Keeps `PLAY.exe` alive for the complete session. Escape opens the PS2 Builder exit overlay instead of the PCSX2 pause UI.

## Shared Runtime and Persistence

```text
%LOCALAPPDATA%\PS2Builder\Runtimes\<PCSX2-VERSION>-<RUNTIME-ID>\
    pcsx2-qt.exe
    <PCSX2 runtime files>            # one physical copy shared by games
    portable.txt                     # points at the currently active game data
    UserData\
        SLUS-20370\
            inis\
            patches\
            bios\
            cache\
            <other writable PCSX2 data>
        SCES-xxxxx\
            ...

%USERPROFILE%\Saved Games\PS2Builder\MemoryCards\
    Mcd001.ps2
    Mcd002.ps2
```

Twenty games using the same bundled PCSX2 version still use one PCSX2 runtime copy. Only the relatively small writable game data is separated per serial.

Memory cards are shared between generated discs, reproducing the behavior of using the same pair of physical memory cards across multiple games on the same PS2 console.

## Session UI Isolation

PCSX2 is treated as an implementation detail rather than part of the user interface.

Generated configuration sets:

```ini
[UI]
StartFullscreen = true
DoubleClickTogglesFullscreen = false
HideMainWindowWhenRunning = true
PauseOnFocusLoss = true

[Hotkeys]
OpenPauseMenu =
ToggleFullscreen =
```

Normal behavior is therefore:

```text
PLAY.exe
   ↓
PS2 boot / game
   ↓
ESC
   ↓
PS2 Builder overlay
   ├── Continue   (default)
   └── Exit
```

The overlay supports keyboard and mouse input and basic XInput controller navigation. Pressing Escape or controller B cancels the overlay; Enter/A confirms the selected action. Because PCSX2 pauses on focus loss, opening the overlay also pauses the emulation session.

Double-click fullscreen switching is disabled in PCSX2 configuration, the normal fullscreen hotkey is explicitly unbound, and exclusive fullscreen control is disabled so the PS2 Builder overlay can reliably appear above the game window.
