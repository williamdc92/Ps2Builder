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
3. Copies the bundled PCSX2 runtime from the read-only disc into the game-specific writable runtime directory if the required runtime is not already cached there.
4. Enables PCSX2 portable mode inside that writable runtime directory.
5. Copies the selected patches into the runtime's writable `patches` directory.
6. Generates `PCSX2.ini` inside the runtime's writable `inis` directory.
7. Launches the local runtime using `-portable -nogui -batch -fullscreen -slowboot`.
8. PCSX2 reads the game and BIOS directly from the read-only disc while writing configuration and cache into the game-specific local runtime. Memory cards remain in the shared save directory.

This per-game writable runtime avoids relying on the newer `-datapath` option and avoids redirecting PCSX2 2.6.x through relative `..` paths. An empty `portable.ini` marker (together with `-portable`) makes PCSX2 use the runtime directory itself as its data root.

## Persistence

```text
%LOCALAPPDATA%\PS2Builder\Games\<SERIAL>\
    Runtime\
        pcsx2-qt.exe
        portable.ini
        inis\
        patches\
        cache\
        <other PCSX2 runtime/data files>

%USERPROFILE%\Saved Games\PS2Builder\MemoryCards\
    Mcd001.ps2
    Mcd002.ps2
```

The PCSX2 runtime is cached automatically inside each game's writable directory. It is not installed system-wide.

Memory cards are shared between generated discs, reproducing the behavior of using the same pair of physical memory cards across multiple games on the same PS2 console.
