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
2. Creates the required local directories and shared memory card directory if they do not already exist.
3. Copies the selected patches into the writable per-game data directory.
4. Generates the required PCSX2 configuration inside the game's local datapath.
5. Launches the bundled PCSX2 runtime using:

```text
-nogui -batch -fullscreen -slowboot -datapath
```

6. PCSX2 reads the game and BIOS directly from the read-only disc while writing configuration, cache, runtime data and memory cards to the local PC.

## Persistence

```text
%LOCALAPPDATA%\PS2Builder\Games\<SERIAL>\
    inis\
    patches\
    cache\
    <other writable PCSX2 runtime data>

%USERPROFILE%\Saved Games\PS2Builder\MemoryCards\
    Mcd001.ps2
    Mcd002.ps2
```

Memory cards are shared between generated discs, reproducing the behavior of using the same pair of physical memory cards across multiple games on the same PS2 console.