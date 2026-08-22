![PS2 Builder Logo](src/PS2Builder/Assets/logo.png)

# PS2 Builder — Standalone PCSX2 Game Packager for Windows

**Turn your own PS2 game dump into a self-contained Windows game disc powered by PCSX2.**

PS2 Builder packages a **user-provided PlayStation 2 game dump and PS2 BIOS** together with the required PCSX2 runtime, configuration, patches and launcher into a single `.iso` file.

The player does **not** need to install PCSX2, configure an emulator, select plugins, map a controller or manually browse for the game.

**Mount the generated ISO → double-click `PLAY.exe` → play.**

> Looking for a way to run a PS2 game on Windows through a simple launcher, without manually installing or configuring PCSX2?
> That is exactly what PS2 Builder is designed for.

---

## What PS2 Builder Does

PS2 Builder creates a portable, self-contained PS2 game package for Windows.

You provide:

* your own legally obtained **PS2 game dump**;
* your own legally obtained **PlayStation 2 BIOS**;
* optional artwork and configuration choices.

PS2 Builder generates:

```text
GAME.iso
│
├── PLAY.exe
├── autorun.inf
├── game.ico
│
└── .ps2builder/
    ├── manifest.json
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

The generated disc behaves like a standalone Windows game package while using PCSX2 internally for emulation.

PS2 Builder **does not convert PS2 machine code into a native Windows game**. Instead, it hides the normal emulator setup process behind a dedicated `PLAY.exe` launcher and a preconfigured PCSX2 runtime.

---

## Final Player Experience

```text
Insert / mount the disc
        ↓
Windows shows the game disc
        ↓
PLAY.exe
        ↓
PS2 BIOS boot
        ↓
Game
```

No existing PCSX2 installation is required.

No emulator configuration is required.

The target computer only needs a compatible **Windows x64** system capable of running the bundled PCSX2 version.

---

## Why PS2 Builder?

Normally, running a PS2 game on a PC requires several separate steps:

```text
Install PCSX2
        ↓
Provide a BIOS
        ↓
Configure graphics
        ↓
Configure controllers
        ↓
Configure memory cards
        ↓
Find patches
        ↓
Select the game image
        ↓
Launch the emulator
```

PS2 Builder turns that into:

```text
PLAY.exe
    ↓
Game
```

The goal is to make an emulated PS2 title feel closer to launching a normal Windows game.

---

## Main Features

### Self-Contained Game Disc

PS2 Builder generates a single `.iso` containing:

* the game image;
* the user-provided PS2 BIOS;
* the required PCSX2 runtime;
* generated PCSX2 configuration;
* selected compatibility patches;
* Microsoft Visual C++ runtime installer;
* game artwork;
* the `PLAY.exe` launcher.

The original game dump is never modified.

Selected patches are applied by PCSX2 at runtime.

---

### No PCSX2 Installation Required

PS2 Builder automatically downloads the current official PCSX2 runtime during the build process and packages it with the generated game disc.

The player does not need to download, install or configure PCSX2 separately.

On first launch, the bundled PCSX2 runtime is cached under:

```text
%LOCALAPPDATA%\PS2Builder\Runtimes\
```

Games built with the same PCSX2 runtime version share one physical emulator installation instead of duplicating the complete runtime for every game.

---

### Per-Game Configuration

Writable emulator data is stored separately for each game under:

```text
%LOCALAPPDATA%\PS2Builder\Runtimes\<runtime>\UserData\<SERIAL>\
```

This includes game-specific PCSX2 configuration and cache data.

The game image itself remains on the read-only generated disc.

The BIOS is mirrored into writable game data when required because PCSX2 creates `.nvm` and `.mec` sidecar files next to the selected BIOS.

---

### Automatic Game Identification

PS2 Builder identifies the game through its `SYSTEM.CNF`.

It calculates the ELF CRC using a method compatible with PCSX2, allowing different revisions of the same game to be distinguished.

Game metadata such as title and region can be retrieved using the PCSX2 `GameIndex.yaml` database.

---

### Automatic PCSX2 Patch Detection

PS2 Builder can search the official `PCSX2/pcsx2_patches` repository using:

```text
SERIAL + CRC
```

Available patches can then be selected during the build process and included in the generated game package.

---

### Automatic Graphics Configuration

PS2 Builder supports configurable internal rendering resolutions:

* Automatic
* Native
* 2x Native
* 3x Native (~1080p)
* 4x Native (~1440p)
* 6x Native (~4K)

In **Automatic** mode, the player selects an internal resolution multiplier based on the target PC's desktop resolution.

Current default mapping:

| Desktop Resolution | PS2 Internal Resolution |
| ------------------ | ----------------------: |
| Below ~1080p       |               2x Native |
| ~1080p             |               3x Native |
| ~1440p             |               4x Native |
| ~4K                |               6x Native |

The Windows display resolution itself is not changed. Only the PS2 internal rendering resolution is affected.

The rendering profile can also be manually overridden during the build process.

---

### Automatic Renderer Selection

PS2 Builder automatically selects an appropriate PCSX2 renderer for the target system.

The player does not need to open the PCSX2 graphics settings before launching the game.

---

### Aspect Ratio Configuration

Supported profiles:

* Automatic
* 4:3
* 16:9

---

### Controller Support

The first two standard SDL-compatible controllers are automatically configured.

This includes common devices such as:

* Xbox controllers;
* DualShock controllers;
* DualSense controllers;
* other standard SDL-compatible gamepads.

The player should not need to open PCSX2 controller settings for normal use.

---

### Keyboard Fallback

Pad 1 also receives a default keyboard configuration.

Default bindings include:

* Arrow keys → D-pad
* `WASD` → Left analog stick
* `TFGH` → Right analog stick
* `IJKL` → Face buttons
* nearby keyboard keys → shoulders, triggers, Start, Select and stick clicks

Controller and keyboard bindings coexist.

---

### Shared PS2 Memory Cards

Memory cards are stored under:

```text
Saved Games\PS2Builder\MemoryCards\
```

PS2 Builder creates standard formatted **8 MB PlayStation 2 memory cards** when they do not already exist.

Blank or unformatted cards created by older builds can be repaired automatically.

Existing non-empty memory cards are never overwritten.

Because the cards are shared, compatible games can access the same virtual PS2 memory cards just as they would on a real console.

---

### PS2-Style Exit Experience

`PLAY.exe` captures the Escape key and displays a dedicated PS2 Builder exit confirmation overlay instead of exposing the normal PCSX2 pause interface.

The overlay can be navigated with keyboard or controller.

Typical controls:

```text
Continue → default selection

A / Cross / Enter → confirm
B / Circle / Escape → cancel
```

Controller input is read through the SDL3 runtime bundled with PCSX2, with XInput fallback.

The overlay can be opened and dismissed repeatedly during the same game session.

---

### Fullscreen Launch

Games are started using PCSX2 command-line options including:

```text
-slowboot
-fullscreen
-nogui
-batch
```

This provides:

* full PS2 BIOS boot;
* automatic fullscreen launch;
* hidden PCSX2 interface;
* direct game startup.

Normal PCSX2 fullscreen switching behavior is restricted so the PS2 Builder exit overlay can reliably appear above the game window.

---

### Visual C++ Runtime Handling

PCSX2 may require the Microsoft Visual C++ x64 Redistributable.

PS2 Builder includes the official installer inside the generated ISO:

```text
.ps2builder/
└── prerequisites/
    └── vc_redist.x64.exe
```

If the required runtime is missing, the installer is launched automatically.

No additional download is necessary on the target PC.

A Windows UAC prompt may appear during this first-time installation.

---

### Custom Game Icons

A custom disc icon can be selected during the build process.

PS2 Builder generates a multi-resolution:

```text
game.ico
```

for Windows Explorer and AutoPlay.

If no custom artwork is selected, PS2 Builder generates a default icon.

Optional artwork search through **SteamGridDB** is also supported using the user's own API key.

PS2 Builder does not include or distribute shared SteamGridDB API keys.

---

### Windows AutoPlay Integration

The generated `autorun.inf` uses:

```text
shellexecute=PLAY.exe
```

and defines a dedicated PS2 Builder shell action for the optical drive.

Depending on Windows configuration, mounting or inserting the disc can expose a:

```text
Play <game title>
```

action.

If AutoPlay is disabled through user preferences or system policy, simply open the disc and double-click:

```text
PLAY.exe
```

Zero-click AutoRun execution is not required.

---

### Native Windows ISO Generation

ISO/UDF creation uses Windows **IMAPI2FS**.

No separate disc-authoring utilities such as:

* `mkisofs`;
* ImgBurn;
* external ISO-building software

are required.

---

## Building PS2 Builder

### Requirements

* Windows 10 or Windows 11
* .NET 8 SDK

Clone the repository and run:

```powershell
dotnet restore

dotnet publish src/PS2Builder/PS2Builder.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true
```

Use the self-contained publish build when generating PS2 game ISOs.

The included GitHub Actions workflow also produces a Windows x64 build artifact.

---

## Build Workflow

```text
PS2 BIOS
    +
PS2 Game Dump
    ↓
PS2 Builder
    ↓
Game identification
    ↓
PCSX2 metadata / patches
    ↓
Graphics + input configuration
    ↓
PCSX2 runtime packaging
    ↓
Single self-contained ISO
    ↓
PLAY.exe
    ↓
PS2 BIOS
    ↓
Game
```

---

## Internet Access

During the build process, PS2 Builder may download:

* the PCSX2 runtime;
* PCSX2 game metadata;
* PCSX2 patches;
* optional SteamGridDB artwork;
* other required runtime data.

When offline, local game detection can still work, but components that have not previously been cached cannot be downloaded.

The generated game disc itself is designed so that normal emulator setup and configuration are not required on the target machine.

---

## Frequently Asked Questions

### Can PS2 Builder turn a PS2 game into a Windows EXE?

Not literally.

PS2 Builder does not recompile or convert a PlayStation 2 game into native Windows x64 machine code.

Instead, it creates a **self-contained Windows game package** containing the PS2 game, PCSX2 runtime, configuration and a dedicated `PLAY.exe` launcher.

From the player's perspective, the experience becomes:

```text
PLAY.exe → game
```

without interacting with the emulator interface.

---

### Can I play a PS2 game on Windows without installing PCSX2?

Yes.

The generated PS2 Builder disc contains the required PCSX2 runtime.

No previous PCSX2 installation is required.

---

### Do I need to configure PCSX2 manually?

No for the normal PS2 Builder workflow.

PS2 Builder generates the required emulator configuration and handles graphics, input, fullscreen startup, memory cards and game launching automatically.

---

### Can I make a portable PS2 game for Windows?

That is one of the primary goals of PS2 Builder.

The generated `.iso` contains the game environment required to launch the title through `PLAY.exe`.

Writable data such as saves and PCSX2 configuration is stored on the Windows machine while the original game image remains on the generated disc.

---

### Can I run a PS2 game by double-clicking an EXE?

Yes, after mounting or opening the generated PS2 Builder ISO.

The player launches:

```text
PLAY.exe
```

which starts the bundled PCSX2 runtime and launches the packaged PS2 game automatically.

---

### Does the player ever need to see the PCSX2 interface?

Under normal use, no.

PS2 Builder starts PCSX2 in fullscreen batch mode with its normal GUI hidden.

---

### Does PS2 Builder include PS2 games?

No.

This repository contains no PlayStation 2 game data.

Users must provide their own game dumps.

---

### Does PS2 Builder include a PlayStation 2 BIOS?

No.

Sony BIOS files are not included in this repository.

Users must provide their own legally obtained BIOS dump.

---

### Does PS2 Builder modify the original game ISO?

No.

The original game dump is preserved.

Compatibility patches are applied by PCSX2 at runtime.

---

### Can multiple PS2 Builder games share memory cards?

Yes.

PS2 Builder stores memory cards in a shared location:

```text
Saved Games\PS2Builder\MemoryCards\
```

This allows compatible games to use the same virtual PS2 cards.

---

### Can multiple games share the same PCSX2 installation?

Yes.

Games using the same bundled PCSX2 runtime version share one cached copy under:

```text
%LOCALAPPDATA%\PS2Builder\Runtimes\
```

while game-specific configuration remains isolated.

---

### Can I burn the generated ISO to physical media?

Yes.

The generated image can be mounted directly in Windows or burned to suitable physical media.

Keep in mind that a DVD9 game combined with the bundled PCSX2 runtime may exceed standard DVD5 capacity.

---

## What PS2 Builder Is Not

PS2 Builder is not:

* a PS2-to-PC source-code converter;
* a PS2 decompiler;
* a native PS2 game porting tool;
* a replacement implementation of PCSX2;
* a source of PS2 BIOS files;
* a source of copyrighted PS2 game images.

It is a **Windows packaging and launcher system built around PCSX2**.

---

## Project Status

PS2 Builder currently implements the architecture described above and is under active development and testing for real Windows environments.

The runtime architecture uses:

```text
Shared PCSX2 Runtime
        +
Per-Game User Data
        +
Shared Memory Cards
```

Input is automatically configured for standard controllers and keyboard, and memory cards are created in a formatted state before PCSX2 starts.

Areas that may require further compatibility work include:

* Windows IMAPI2FS/UDF behavior;
* PCSX2 configuration keys across different releases;
* PCSX2 command-line changes;
* runtime packaging across different Windows installations;
* game-specific compatibility behavior.

The project is intentionally structured so PCSX2-specific runtime details can evolve without changing the core workflow:

```text
BIOS + Game Dump
        ↓
PS2 Builder
        ↓
Self-Contained Windows ISO
        ↓
PLAY.exe
        ↓
PS2 BIOS
        ↓
Game
```

---

## Legal and Licensing

This repository does **not** contain PlayStation 2 games or Sony BIOS files.

Users must provide their own legally obtained game and BIOS dumps and are responsible for ensuring that their use complies with applicable laws.

PCSX2 is a separate project distributed under the **GPL-3.0-or-later** license.

PS2 Builder downloads the official PCSX2 runtime and adds:

```text
PCSX2_SOURCE.txt
```

to generated discs containing PCSX2 runtime version and source information.

Anyone publicly redistributing generated images containing PCSX2 is responsible for complying with the GPL and with the licenses of dependencies bundled with PCSX2.

SteamGridDB is a separate service and is not affiliated with PS2 Builder.

The PS2 memory-card formatting implementation follows the documented filesystem/ECC behavior of the public-domain **mymc** project by Ross Ridge.

---

## Search Terms / Related Concepts

PS2 Builder may be useful if you are looking for:

* a **portable PS2 game for Windows**;
* a **standalone PCSX2 game**;
* a **PS2 game launcher for Windows**;
* a way to **run a PS2 game without configuring PCSX2**;
* a **PCSX2 game packager**;
* a **self-contained PS2 emulator package**;
* a way to launch a PS2 game through a simple **Windows EXE**;
* a way to package **PCSX2 + BIOS + game configuration** together;
* a **portable PCSX2 setup per game**;
* a console-like way to launch PS2 games on a PC.

---

## Related Projects

PS2 Builder uses **PCSX2** as its PlayStation 2 emulation runtime.

* PCSX2: https://github.com/PCSX2/pcsx2
* PCSX2 patches: https://github.com/PCSX2/pcsx2_patches
* SteamGridDB: https://www.steamgriddb.com/

---

## Contributing

Bug reports, compatibility reports and pull requests are welcome.

When reporting a game-specific issue, useful information includes:

```text
Game title:
Serial:
CRC:
Region:
Windows version:
GPU:
PCSX2 runtime version:
PS2 Builder version:
Problem description:
```

Please do not upload or attach copyrighted game images or Sony BIOS files to issues.

---

## License

PS2 Builder is distributed under the license included in this repository.

See [`LICENSE`](LICENSE) for details.

---

**PS2 Builder**

*Package your PS2 game. Mount it. Open `PLAY.exe`. Play.*
