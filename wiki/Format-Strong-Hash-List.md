# Format: Strong Hash List

The file lists built into LaunchPad and the server (`LandmarkEmulator.PatchPacks/Data/`, embedded in the assembly).
Every file LaunchPad writes into a game folder is checked against them, so they are the trust root of the patch system
(together with `LaunchPad.exe` itself). They hold names, sizes and checksums only: no game data.

Back to [Asset Documentation](Asset-Documentation).

## The files

All are CSV with a header row; paths are relative to the game folder with `\`.

| File | Columns | What |
|---|---|---|
| `landmark-0695-files-sha256.csv` | `RelativePath,Size,CRC32,SHA256` | **The strong list**: the SHA-256 of every genuine file of both known 0.695 builds, one row per path, size and CRC32 (a path that differs between the builds has a row per build) |
| `landmark-final-2017-file-list.csv` | `RelativePath,Size,CRC32` | Daybreak's own file list of the final build (14 Feb 2017) |
| `steam-0589-file-list.csv` | `RelativePath,Size,SHA1` | The Steam build 0.589.1.128895, to verify a Steam copy before anything is installed on it |
| `update-0589-to-0695-files.csv` | `RelativePath,Size,CRC32,Status` | The 1,094 files the update brings: `new` (583) or `changed` (511) |

## Why SHA-256

Daybreak's list has only size and CRC32 per file, and a CRC32 can be forged (four chosen bytes give any CRC32), so a
harmful `Landmark64.exe` could have the right size and CRC32. A SHA-256 cannot be forged that way. The SHA-256 of each
file was taken once from genuine copies whose size **and** CRC32 match Daybreak's list for that build; the hashes are
of the exact bytes (nothing stripped or normalised). A file whose SHA-256 is unknown is refused, never trusted on its
CRC32.

## The two builds

| Build | Files | SHA-256 known |
|---|---|---|
| January 2017 build (Daybreak's list with three earlier files: `Landmark64.exe`, `Locale\en_us_data.dat`, `Locale\en_us_data.dir`) | 34,138 | all |
| Daybreak's final build (February 2017) | 34,138 | all but those three files |

A pack for the final build is refused until genuine copies of its three files are found. LaunchPad has an opt-in
(off by default) to accept those three by size and CRC32 for local pack files only; it never applies to a server's
update.

## Making it

`tools/strong_hash_list.py` in the repository builds the strong list from Daybreak's final list and local sources
(zips of game files or game folders, only read):

```
python tools/strong_hash_list.py <final list csv> <out csv> <source> [<source> ...]
```

Rows found nowhere keep an empty SHA256. Details: `docs/launchpad.md`, "The strong list", in the repository.
