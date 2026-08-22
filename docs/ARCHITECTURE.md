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
2. Ensures the shared memory card directory exists and creates two already-formatted standard 8 MB PS2 memory-card images when missing. Uniform blank cards left by older builds are reformatted automatically; non-empty cards are preserved.
3. Identifies the bundled PCSX2 runtime by version/source and caches **one shared physical copy per runtime version** under `%LOCALAPPDATA%\PS2Builder\Runtimes\`.
4. Acquires a per-runtime session lock. A shared PCSX2 runtime is intentionally used by only one active PS2 Builder session at a time because PCSX2 2.6.x uses a runtime-level `portable.txt` file.
5. Creates the game's writable data directory below the shared runtime at `UserData\<SERIAL>\`.
6. Writes `portable.txt` with the child path `UserData\<SERIAL>`, avoiding `..` traversal while keeping a single PCSX2 runtime.
7. Migrates and removes obsolete per-game runtime copies created by older PS2 Builder test builds.
8. Copies selected patches into the game's writable `patches` directory.
9. Mirrors the BIOS ROM into the writable game data directory so PCSX2 can create/update `.nvm` and `.mec` sidecars.
10. Generates `PCSX2.ini` with automatic SDL controller mappings, a simultaneous Pad 1 keyboard fallback, fullscreen settings and PS2 Builder-owned exit behavior.
11. Launches the shared PCSX2 runtime using `-portable -nogui -batch -fullscreen -slowboot`.
12. Keeps `PLAY.exe` alive for the complete session. A low-level Windows keyboard hook captures Escape while the PCSX2 render window is active, suppresses it from PCSX2 and opens the PS2 Builder exit overlay instead.

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

Memory cards are shared between generated discs, reproducing the behavior of using the same pair of physical memory cards across multiple games on the same PS2 console. PS2 Builder writes a standard formatted 8 MB card image with 512-byte data pages plus PS2 ECC/spare data, so first-run games do not see an unformatted card.

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

The overlay supports keyboard and mouse input and basic XInput controller navigation. Escape is intercepted before PCSX2 receives it. Pressing Escape a second time or controller B cancels the overlay; Enter/A confirms the selected action. Because PCSX2 pauses on focus loss, opening the overlay also pauses the emulation session.

Double-click fullscreen switching is disabled in PCSX2 configuration, the normal fullscreen hotkey is explicitly unbound, and exclusive fullscreen control is disabled so the PS2 Builder overlay can reliably appear above the game window.


## Default Input Mapping

Pad 1 receives both SDL controller bindings and keyboard bindings. Repeated PCSX2 binding entries allow either input source to drive the same emulated control.

```text
D-pad        Arrow keys
Left stick   W A S D
Right stick  T F G H
Triangle     I
Circle       L
Cross        K
Square       J
L1 / R1      Q / E
L2 / R2      1 / 3
L3 / R3      2 / 4
Select       Backspace
Start        Enter
Analog       Tab
Pressure     Shift
```

Pad 2 is automatically mapped to the second SDL controller.

## Disc and Executable Icons

The selected per-game icon is converted into a multi-resolution Windows ICO and placed at the disc root as hidden/system `game.ico`. `autorun.inf` references this root-level icon for the optical-drive and AutoPlay surfaces.

`PS2Builder.exe` carries a compiled-in multi-resolution PS2 Builder application icon. Since the same published binary is copied into generated discs, `PLAY.exe` carries that stable application icon as well. The Builder does not rewrite executable resources per game.

## Memory Card Formatting

PS2 Builder owns first-run memory-card initialization rather than asking a game or the PCSX2 UI to format the card. It creates standard 8 MB raw `.ps2` images with the PS2 filesystem superblock/FAT/root directory and the 16-byte spare area for every 512-byte page. ECC bytes are generated for each 128-byte data chunk. The layout follows the public-domain mymc implementation. Existing non-empty cards are never reformatted.


## Exit Overlay Input

The exit overlay uses the `SDL3.dll` bundled with the active PCSX2 runtime as its primary controller backend. This keeps overlay navigation aligned with the controller devices recognized by PCSX2 itself. XInput is used as a fallback for Xbox-compatible controllers when SDL cannot be initialized.

Controls:

- D-pad or left stick: choose Continue / Exit game.
- South face button (A / Cross): confirm.
- East face button (B / Circle): return to the game.
- Keyboard Left / Right: choose.
- Enter: confirm.
- Esc: return to the game.
