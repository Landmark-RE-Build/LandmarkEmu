# LandmarkEmulator.PatchPacks

**Library**, used by [LaunchPad](Project-LaunchPad) and by the server's patch folder ([WebAPI](Project-WebAPI)
`PatchHost.cs`). Patch packs, the checks every pack must pass, and the file lists built into the program. The same
code runs on both sides, so a pack the server offers has already passed the check every player's LaunchPad repeats.

Back to [Code Documentation](Code-Documentation).

## Main files

| Path | What |
|---|---|
| `PatchFile.cs` | The `.lmpatch` container: writer and reader with its limits (sizes, entry counts, no overlaps, no entry unpacks past its declared size) |
| `InPlaceEdit.cs` | `LME1` in-place edits (the changed byte ranges of a file that keeps its layout, such as the asset packs) |
| `Delta.cs` | `LMD1` binary deltas (copy / add / literal operations against the Steam file) |
| `Patching.cs` | The known builds (`Targets`), the update check (`CheckUpdate`), making the update (`MakeUpdate`), path rules |
| `PackCheck.cs` | The whole check of a pack, entry by entry (`Verify`) |
| `GameFolder.cs` | Is a folder a Landmark install, which build, is the game running |
| `GameStore.cs` | Installing (backup and journal first), undoing, validating a game folder |
| `ServerPatches.cs` | Where a server's packs come from (nothing it returns is trusted) |
| `Data/landmark-0695-files-sha256.csv` | The strong list: the SHA-256 of every genuine file of the two known 0.695 builds |
| `Data/landmark-final-2017-file-list.csv` | Daybreak's file list of the final build (path, size, CRC32) |
| `Data/steam-0589-file-list.csv` | The Steam 0.589 build (path, size, SHA-1) |
| `Data/update-0589-to-0695-files.csv` | The 1,094 files the update brings (new or changed) |

The CSV files are embedded in the assembly; they hold names, sizes and checksums only, no game data.

Formats: [Format: LMPatch](Format-LMPatch), [Format: Strong Hash List](Format-Strong-Hash-List). The rules (what a
pack may and may not do, what a hostile server cannot do) are in `docs/launchpad.md` in the repository; the tests are
in `LandmarkEmulator.Tests/PatchPacks`.
