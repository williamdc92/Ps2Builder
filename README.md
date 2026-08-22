![PS2 Builder Logo](src/PS2Builder/Assets/logo.png)

# PS2 Builder — Standalone PCSX2 Game Packager for Windows

**Turn your own PlayStation 2 game dump into a self-contained Windows game package powered by PCSX2.**

PS2 Builder packages a **user-provided PS2 game dump and PlayStation 2 BIOS** together with the required PCSX2 runtime, configuration, patches, prerequisites and launcher into a single `.iso` file.

The player does **not** need to install PCSX2, configure an emulator, map a controller or manually select the game.

**Mount the generated ISO → double-click `PLAY.exe` → play.**

> Looking for a way to run a PS2 game on Windows through a simple launcher, without manually installing or configuring PCSX2?
> That is exactly what PS2 Builder is designed for.

---

## What PS2 Builder Does

PS2 Builder creates a portable, self-contained PS2 game package for Windows.

You provide:

* your own legally obtained **PlayStation 2 game dump**;
* your own legally obtained **PlayStation 2 BIOS**;
* optional artwork;
* optional game-specific settings and patches.

PS2 Builder generates a structure similar to:

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

The generated disc behaves like a standalone Windows game package while using PCSX2 internally for PlayStation 2 emulation.

PS2 Builder **does not convert PS2 machine code into native Windows code**. Instead, it packages and automates the emulator environment behind a dedicated `PLAY.exe` launcher.

---

## Player Experience

The intended experience on the target PC is:

```text
Mount the generated ISO
        ↓
Open the game disc
        ↓
PLAY.exe
        ↓
PlayStation 2 BIOS boot
        ↓
Game
```

No previous PCSX2 installation is required.

No manual emulator configuration is required.

The target computer only needs a compatible **Windows x64** system capable of running the bundled PCSX2 version.

---

## Why PS2 Builder?

Normally, running a PS2 game on a PC may involve several separate steps:

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
Find compatibility patches
        ↓
Select the game image
        ↓
Launch the emulator
```

PS2 Builder aims to reduce that experience to:

```text
PLAY.exe
    ↓
Game
```

The goal is to make launching an emulated PS2 title feel closer to launching a normal Windows game.

---

# Features

## Self-Contained Game Disc

PS2 Builder generates a single `.iso` containing:

* the game image;
* the user-provided PS2 BIOS;
* the required PCSX2 runtime;
* generated PCSX2 configuration;
* selected compatibility patches;
* required prerequisites;
* game artwork;
* the dedicated `PLAY.exe` launcher.

The original game dump is not modified.

Selected `.pnach` patches are applied by PCSX2 at runtime.

---

## No PCSX2 Installation Required

PS2 Builder can obtain the official PCSX2 runtime during the build process and package the required files with the generated game disc.

The player does not need to manually download, install or configure PCSX2 before launching the game.

On first launch, the bundled PCSX2 runtime is cached under:

```text
%LOCALAPPDATA%\PS2Builder\Runtimes\
```

Games built with the same PCSX2 runtime version can share one physical emulator runtime instead of unnecessarily duplicating it for every title.

---

## Per-Game Configuration

Writable emulator data is stored separately for each game under a structure similar to:

```text
%LOCALAPPDATA%\PS2Builder\Runtimes\<runtime>\UserData\<SERIAL>\
```

This allows each game to maintain isolated configuration and runtime data.

The game image itself remains on the generated read-only disc.

When required by PCSX2, the user-provided BIOS can be mirrored into writable storage so that PCSX2 can create its `.nvm` and `.mec` sidecar files.

---

## Automatic Game Identification

PS2 Builder identifies the PlayStation 2 title from its game data.

Game information can include:

* serial;
* ELF CRC;
* title;
* region;
* revision-specific information.

The ELF CRC is calculated using a method compatible with PCSX2, allowing different revisions of the same game to be distinguished.

Game metadata can also be matched against the PCSX2 `GameIndex.yaml` database.

---

## Automatic PCSX2 Patch Detection

PS2 Builder can search the official PCSX2 patches repository using game identifiers such as:

```text
SERIAL + CRC
```

Compatible patches can then be selected and included with the generated game package.

This allows revision-specific PCSX2 patches to be distributed with the package without modifying the original game dump.

---

## Automatic Graphics Configuration

PS2 Builder supports configurable internal rendering resolutions.

Available profiles can include:

* Automatic
* Native
* 2x Native
* 3x Native
* 4x Native
* 6x Native

In **Automatic** mode, PS2 Builder can choose an internal resolution multiplier based on the target PC's display resolution.

A typical mapping is:

| Desktop Resolution | PS2 Internal Resolution |
| ------------------ | ----------------------: |
| Below ~1080p       |               2x Native |
| ~1080p             |               3x Native |
| ~1440p             |               4x Native |
| ~4K                |               6x Native |

The Windows desktop resolution itself is not changed.

Only the PS2 internal rendering resolution used by PCSX2 is affected.

The rendering profile can also be manually selected during the build process.

---

## Automatic Renderer Selection

PS2 Builder can automatically select an appropriate PCSX2 graphics renderer for the target Windows system.

The player should not need to open PCSX2 graphics settings before launching the game.

---

## Aspect Ratio Configuration

Supported aspect-ratio profiles can include:

* Automatic
* 4:3
* 16:9

This allows the package to ship with the intended display configuration already defined.

---

## Controller Support

PS2 Builder automatically configures standard SDL-compatible controllers.

Typical compatible devices include:

* Xbox controllers;
* DualShock controllers;
* DualSense controllers;
* other standard SDL-compatible gamepads.

The goal is for a player to connect a controller and launch the game without opening the PCSX2 controller configuration screen.

---

## Keyboard Fallback

Pad 1 can also receive a default keyboard configuration.

Typical bindings include:

```text
Arrow Keys → D-pad
WASD       → Left analog stick
TFGH       → Right analog stick
IJKL       → Face buttons
```

Additional nearby keyboard keys can be mapped to:

* L1 / R1;
* L2 / R2;
* Start;
* Select;
* L3 / R3.

Controller and keyboard bindings can coexist.

---

## Shared PlayStation 2 Memory Cards

Memory cards are stored outside the generated read-only ISO.

The default shared location is:

```text
Saved Games\PS2Builder\MemoryCards\
```

PS2 Builder can create standard formatted **8 MB PlayStation 2 memory cards** when no existing card is available.

Existing non-empty memory cards are not overwritten.

Using shared cards allows compatible games to access the same virtual PS2 memory cards, similar to using the same physical memory card across multiple games on a real console.

---

## PS2-Style Exit Experience

`PLAY.exe` can intercept the normal exit action and display a dedicated PS2 Builder exit confirmation overlay instead of exposing the standard PCSX2 pause interface.

The overlay can be controlled using keyboard or controller.

Typical controls:

```text
A / Cross / Enter     → Confirm
B / Circle / Escape   → Cancel
```

The goal is to keep the player inside a game-focused interface instead of exposing emulator controls during normal use.

---

## Fullscreen Launch

Games are started through PCSX2 command-line options designed for direct launching.

Typical options include:

```text
-slowboot
-fullscreen
-nogui
-batch
```

This provides:

* full PlayStation 2 BIOS boot;
* automatic fullscreen startup;
* hidden PCSX2 user interface;
* direct game launch.

---

## Visual C++ Runtime Handling

PCSX2 may require the Microsoft Visual C++ x64 Redistributable on the target computer.

PS2 Builder can include the official installer inside the generated disc:

```text
.ps2builder/
└── prerequisites/
    └── vc_redist.x64.exe
```

If the required runtime is missing, the installer can be launched automatically.

This avoids requiring the player to manually search for and download the dependency.

A Windows UAC prompt may appear during first-time installation.

---

## Custom Game Icons

A custom disc icon can be selected during the build process.

PS2 Builder generates a Windows-compatible:

```text
game.ico
```

for use by Windows Explorer and AutoPlay.

If no custom artwork is selected, a default icon can be used.

Optional artwork integration with **SteamGridDB** is also supported using the user's own API key.

PS2 Builder does not include or distribute shared SteamGridDB API keys.

---

## Windows AutoPlay Integration

The generated `autorun.inf` can use:

```text
shellexecute=PLAY.exe
```

and define a dedicated PS2 Builder shell action for the mounted disc.

Depending on Windows configuration, mounting or inserting the disc may expose an action similar to:

```text
Play <game title>
```

If AutoPlay is disabled through Windows settings or system policy, the player can simply open the mounted disc and double-click:

```text
PLAY.exe
```

Automatic execution is not required.

---

## Native Windows ISO Generation

ISO/UDF creation is handled through Windows **IMAPI2FS**.

This means PS2 Builder does not require separate external ISO-authoring utilities such as:

* `mkisofs`;
* ImgBurn;
* other third-party disc-authoring applications.

---

# Building PS2 Builder

## Requirements

* Windows 10 or Windows 11
* .NET 8 SDK
* x64 environment

Clone the repository:

```powershell
git clone https://github.com/williamdc92/Ps2Builder.git
cd Ps2Builder
```

Restore dependencies:

```powershell
dotnet restore
```

Publish a self-contained Windows x64 build:

```powershell
dotnet publish src/PS2Builder/PS2Builder.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true
```

The self-contained publish build is recommended when generating PS2 game packages.

Prebuilt Windows x64 versions may also be available from the repository's **Releases** section.

---

# Build Workflow

The overall process is:

```text
User-provided PS2 BIOS
        +
User-provided PS2 Game Dump
        ↓
PS2 Builder
        ↓
Game Identification
        ↓
PCSX2 Metadata / Patches
        ↓
Graphics + Input Configuration
        ↓
PCSX2 Runtime Packaging
        ↓
Self-Contained Windows ISO
        ↓
PLAY.exe
        ↓
PlayStation 2 BIOS
        ↓
Game
```

---

# Internet Access

During the build process, PS2 Builder may use Internet access to obtain:

* the official PCSX2 runtime;
* PCSX2 game metadata;
* PCSX2 patches;
* optional SteamGridDB artwork;
* required runtime-related data.

When offline, locally available data can still be used, but resources that have not previously been downloaded or cached cannot be retrieved.

Once the package has been successfully created, the player should not need to manually download or configure PCSX2 before launching the game.

---

# Frequently Asked Questions

## Can PS2 Builder turn a PS2 game into a Windows EXE?

Not literally.

PS2 Builder does not recompile or convert PlayStation 2 machine code into native Windows x64 code.

Instead, it creates a **self-contained Windows game package** containing:

* the PS2 game dump;
* the user-provided PS2 BIOS;
* the PCSX2 runtime;
* emulator configuration;
* optional patches;
* a dedicated `PLAY.exe` launcher.

From the player's perspective, the experience becomes:

```text
PLAY.exe → Game
```

without manually interacting with the emulator interface.

---

## Can I play a PS2 game on Windows without installing PCSX2?

Yes.

The generated PS2 Builder package contains the required PCSX2 runtime.

No previous PCSX2 installation is required on the target machine.

---

## Do I need to configure PCSX2 manually?

Under the normal PS2 Builder workflow, no.

PS2 Builder generates the required emulator configuration and handles common settings such as:

* graphics;
* renderer;
* internal resolution;
* aspect ratio;
* controllers;
* keyboard fallback;
* memory cards;
* fullscreen startup;
* game launching.

---

## Can I make a portable PS2 game for Windows?

Yes.

Creating a portable, self-contained PS2 game package for Windows is one of the main goals of PS2 Builder.

The generated `.iso` contains the files required to launch the game through `PLAY.exe`, while writable emulator data is stored automatically on the Windows machine.

---

## Can I run a PS2 game by double-clicking an EXE?

Yes.

After mounting or opening the generated PS2 Builder ISO, launch:

```text
PLAY.exe
```

PS2 Builder then starts the bundled PCSX2 runtime and launches the packaged game automatically.

---

## Does the player need to see the PCSX2 interface?

Under normal use, no.

The game is started through PCSX2 in a direct-launch configuration designed to keep the standard emulator interface hidden.

---

## Does PS2 Builder include PlayStation 2 games?

No.

This repository does not contain or distribute commercial PlayStation 2 game data.

Users must provide their own game dumps.

---

## Does PS2 Builder include a PlayStation 2 BIOS?

No.

Sony PlayStation 2 BIOS files are not included in this repository.

Users must provide their own legally obtained BIOS dump.

---

## Does PS2 Builder modify the original game image?

No.

The original game dump remains unchanged.

Compatibility patches are supplied separately and applied by PCSX2 at runtime.

---

## Can multiple PS2 Builder games share memory cards?

Yes.

PS2 Builder stores virtual memory cards in a shared location:

```text
Saved Games\PS2Builder\MemoryCards\
```

This allows compatible games to access the same virtual PS2 memory cards.

---

## Can multiple games share the same PCSX2 runtime?

Yes.

Games using the same bundled PCSX2 version can share one cached runtime under:

```text
%LOCALAPPDATA%\PS2Builder\Runtimes\
```

while game-specific configuration remains isolated.

---

## Can I burn the generated ISO to physical media?

Yes.

The generated image can be mounted directly in Windows or written to suitable physical media.

Keep in mind that large PS2 game images combined with the PCSX2 runtime and additional files may exceed the capacity of standard single-layer DVDs.

---

## Is PS2 Builder a PS2 emulator?

No.

PCSX2 performs the actual PlayStation 2 emulation.

PS2 Builder is a **packaging, configuration and launcher system built around PCSX2**.

---

# Common Use Cases

PS2 Builder is designed for users who want to:

* create a **portable PS2 game for Windows**;
* build a **standalone PCSX2 game package**;
* launch a PS2 game through a simple **Windows EXE**;
* run a PS2 game without manually configuring PCSX2;
* package PCSX2 together with a user-provided BIOS and game dump;
* create a per-game PCSX2 environment;
* automatically configure graphics and controllers for a PS2 game;
* distribute a preconfigured PCSX2 environment where legally permitted;
* create a console-like PS2 launching experience on Windows;
* keep the emulator interface hidden during normal gameplay.

---

# What PS2 Builder Is Not

PS2 Builder is not:

* a PS2-to-PC source-code converter;
* a PlayStation 2 decompiler;
* a native PS2 game porting tool;
* a replacement implementation of PCSX2;
* a source of PlayStation 2 BIOS files;
* a source of copyrighted PlayStation 2 game images.

PS2 Builder is a **Windows game packaging and launcher system powered by PCSX2**.

---

# Project Status

PS2 Builder is under active development and testing on Windows.

Its runtime architecture is designed around:

```text
Shared PCSX2 Runtime
        +
Per-Game User Data
        +
Shared Memory Cards
```

The project is intentionally structured so that PCSX2-specific behavior can evolve without changing the basic PS2 Builder workflow:

```text
BIOS + Game Dump
        ↓
PS2 Builder
        ↓
Self-Contained Windows ISO
        ↓
PLAY.exe
        ↓
PlayStation 2 BIOS
        ↓
Game
```

Areas that may require continued compatibility work include:

* Windows IMAPI2FS/UDF behavior;
* PCSX2 configuration changes between releases;
* PCSX2 command-line changes;
* runtime packaging across different Windows installations;
* graphics-driver differences;
* controller compatibility;
* game-specific PCSX2 behavior.

Bug reports and compatibility feedback are welcome.

---

# Legal Notice

This repository does **not** contain or distribute PlayStation 2 games or Sony PlayStation 2 BIOS files.

Users must provide their own legally obtained game and BIOS dumps and are responsible for ensuring that their use complies with applicable laws.

PS2 Builder is not affiliated with or endorsed by Sony Interactive Entertainment.

PlayStation and PlayStation 2 are trademarks of Sony Interactive Entertainment.

PCSX2 is a separate open-source project and is not developed or maintained by PS2 Builder.

---

# PCSX2 Licensing

PCSX2 is distributed under the **GPL-3.0-or-later** license.

When a PCSX2 runtime is included in a generated PS2 Builder package, PS2 Builder can include:

```text
PCSX2_SOURCE.txt
```

containing information about the bundled PCSX2 runtime and its source.

Anyone redistributing generated packages containing PCSX2 is responsible for complying with the GPL and with the licenses of any dependencies bundled with PCSX2.

---

# SteamGridDB

SteamGridDB is a separate service and is not affiliated with PS2 Builder.

Optional SteamGridDB integration requires the user to provide their own API key.

PS2 Builder does not distribute a shared SteamGridDB API key.

---

# Memory Card Implementation

The PlayStation 2 memory-card formatting implementation follows the documented filesystem/ECC behavior of the public-domain **mymc** project by Ross Ridge.

PS2 Builder uses **mymc** as a technical reference for creating correctly formatted PlayStation 2 memory cards, including filesystem structures and ECC/spare data behavior.

---

# Related Projects

PS2 Builder uses **PCSX2** as its PlayStation 2 emulation runtime.

* PCSX2
  https://github.com/PCSX2/pcsx2

* PCSX2 Patches
  https://github.com/PCSX2/pcsx2_patches

* SteamGridDB
  https://www.steamgriddb.com/

---

# Contributing

Bug reports, compatibility reports and pull requests are welcome.

When reporting a game-specific issue, please include as much of the following information as possible:

```text
Game title:
Serial:
CRC:
Region:
Game revision:
Windows version:
CPU:
GPU:
Controller:
PCSX2 runtime version:
PS2 Builder version:
Problem description:
```

Please **do not upload copyrighted game images or Sony BIOS files** to GitHub issues, pull requests or discussions.

---

# License

PS2 Builder is distributed under the license included in this repository.

See [`LICENSE`](LICENSE) for details.

---

# Credits

PS2 Builder is built around and depends on the work of the PCSX2 project and its contributors.

Special thanks to **Ross Ridge**, author of the public-domain **mymc** project, whose documentation of the PlayStation 2 memory-card filesystem and ECC behavior is used as a technical reference by PS2 Builder's memory-card implementation.

Additional thanks to the developers and communities maintaining public PlayStation 2 technical documentation, compatibility information and tooling.

---

# PS2 Builder

**Package your PS2 game. Mount it. Open `PLAY.exe`. Play.**
