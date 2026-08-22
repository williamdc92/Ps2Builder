# PS2 Builder

PS2 Builder creates a **self-contained Windows game disc** from a PS2 BIOS and game dump provided by the user.

The output is a single `.iso` file. When the ISO is mounted or burned to physical media, the end user simply opens **PLAY.exe** and the game launches directly using the PCSX2 runtime bundled inside the disc.

No previous PCSX2 installation is required, and no emulator configuration is needed.

## Final User Experience

```text
Insert / mount the disc
        ↓
Windows shows the disc and the "Play <title>" action
        ↓
PLAY.exe
        ↓
Full PS2 BIOS boot
        ↓
Game
```

The target PC only needs to run a Windows x64 version compatible with the bundled PCSX2 runtime.

## Features Already Implemented

- Single executable architecture: `PS2Builder.exe` becomes `PLAY.exe` when copied into the generated ISO.
- Game identification through `SYSTEM.CNF`.
- ELF CRC calculation compatible with the method used by PCSX2, allowing different game revisions to be distinguished.
- Automatic title and region lookup using the official PCSX2 `GameIndex.yaml`.
- Patch lookup from the official `PCSX2/pcsx2_patches` repository using `SERIAL + CRC`.
- Patch selection during the build process.
- Configurable internal rendering resolution: Automatic, Native, 2x Native, 3x Native (~1080p), 4x Native (~1440p), 6x Native (~4K).
- In Automatic mode, the player selects the internal resolution multiplier according to the target PC's desktop resolution.
- Aspect ratio selection: Automatic, 4:3, 16:9.
- Automatic PCSX2 renderer selection.
- Full PS2 BIOS boot using `-slowboot`.
- Fullscreen launch using `-fullscreen`.
- PCSX2 GUI hidden using `-nogui`.
- Batch mode using `-batch`.
- PCSX2 runtime automatically downloaded from the current official GitHub release and bundled inside the generated ISO.
- On first PLAY, the bundled PCSX2 runtime is copied into a versioned local runtime cache under `%LOCALAPPDATA%\PS2Builder\Runtime\`. This is an internal cache, not a system-wide installation, and it is reused by discs containing the same runtime version.
- Official Microsoft Visual C++ x64 Redistributable bundled as an offline prerequisite. It is launched automatically only when the required runtime is missing from the target PC.
- Writable emulator data is separated from the read-only disc through a local portable-mode data directory.
- Shared memory cards stored under `Saved Games\PS2Builder\MemoryCards\`. Memory cards are created or initialized by PCSX2 when they do not already exist.
- Per-game configuration and cache stored under `%LOCALAPPDATA%\PS2Builder\Games\<SERIAL>\`.
- Customizable disc icon.
- Default PS2 Builder icon automatically generated when no custom icon is selected.
- Optional icon search through SteamGridDB using the user's own API key.
- `autorun.inf` used for disc name, disc icon and AutoPlay action. Automatic AutoRun execution is not required.
- ISO/UDF image generation through **Windows IMAPI2FS**, without requiring `mkisofs`, ImgBurn or external disc-authoring software.

## Build

Requirements:

- Windows 10/11
- .NET 8 SDK

```powershell
dotnet restore

dotnet publish src/PS2Builder/PS2Builder.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true
```

Use the self-contained `publish` build when generating game ISOs.

The included GitHub Actions workflow automatically produces the Windows x64 build artifact.

## Generated ISO Structure

```text
GAME.iso

├── PLAY.exe
├── autorun.inf
└── .ps2builder/          (hidden)
    ├── manifest.json
    ├── game.ico
    ├── PCSX2_SOURCE.txt
    │
    ├── content/
    │   └── game.iso
    │
    ├── firmware/
    │   └── <user-provided BIOS>
    │
    ├── patches/
    │   └── <selected .pnach files>
    │
    ├── prerequisites/
    │   └── vc_redist.x64.exe
    │
    └── runtime/
        └── pcsx2-qt.exe + dependencies
```

The original game dump is never modified. Any selected patches are applied at runtime by PCSX2.

## Automatic Resolution

The current default mapping is:

| Desktop Resolution | Internal Rendering Resolution |
| --- | --- |
| Below ~1080p | 2x Native |
| ~1080p | 3x Native |
| ~1440p | 4x Native |
| ~4K | 6x Native |

The rendering profile can be manually overridden in PS2 Builder.

The display itself remains at the Windows desktop/fullscreen resolution. The multiplier only affects the internal PS2 rendering resolution.

## Important Notes

- If the target PC does not have the Visual C++ Runtime required by PCSX2, the first launch may trigger a Windows UAC prompt to install the bundled official Microsoft redistributable. No download or manual configuration is required.
- AutoPlay may be disabled by the user or through Windows system policies. In that case, the user only needs to open the disc and double-click `PLAY.exe`.
- A DVD9 game combined with the PCSX2 runtime may exceed the capacity of a standard DVD5. The generated ISO can still be mounted directly or burned to higher-capacity media.
- SteamGridDB integration is optional and requires the user's personal API key. PS2 Builder does not include or distribute shared API keys.
- During the build process, PS2 Builder may download updated data from PCSX2 and GitHub. When offline, local game detection still works, but metadata, patches or runtime components that are not already cached cannot be downloaded.

## Legal and Licensing

This repository does not contain PS2 games or Sony BIOS files.

Users must provide their own legally obtained game and BIOS dumps and are responsible for ensuring that their use complies with applicable laws.

PCSX2 is a separate project distributed under the GPL-3.0-or-later license.

PS2 Builder downloads the official PCSX2 runtime and adds a `PCSX2_SOURCE.txt` file to generated discs containing the runtime version and source information.

Anyone publicly redistributing generated ISOs that contain PCSX2 must comply with the GPL and with the licenses of the dependencies bundled with PCSX2.

SteamGridDB is a separate service and is not affiliated with this project.

## Project Status

PS2 Builder currently implements the complete architecture described above and is intended to be compiled and tested on a real Windows environment.

The components most likely to require adjustments after initial testing are:

- Windows IMAPI2FS/UDF integration.
- PCSX2 configuration keys and command-line behavior for specific runtime releases.
- Compatibility handling for future PCSX2 release changes.
- Runtime packaging behavior across different Windows systems.

The project is designed so that these runtime-specific details can be updated without changing the core workflow:

```text
BIOS + Game Dump
        ↓
PS2 Builder
        ↓
Single self-contained ISO
        ↓
PLAY.exe
        ↓
PS2 BIOS
        ↓
Game
```
