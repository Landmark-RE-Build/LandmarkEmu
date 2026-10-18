# Format: LMPatch

The `.lmpatch` patch pack: the one file that carries the update from the Steam build of Landmark (0.589.1.128895) to
0.695.1.132647. Ours. LaunchPad makes, checks and installs it; a server's patch folder checks it the same way before
offering it ([Hosting Patches](Hosting-Patches)). Packs carry game data, so they are never part of the repository.

Back to [Asset Documentation](Asset-Documentation).

## The container

```
"LMPATCH1"           8 bytes
u64 index offset     where the index starts
u64 index length     its compressed length
entries              each Brotli-compressed, back to back
index                Brotli-compressed UTF-8 JSON:
                     { "manifest": "<pack.json text>",
                       "entries": [ { "name", "offset", "stored", "size", "sha256" }, ... ] }
```

- Every entry is checked against its SHA-256 as it is read. The **pack's identity** is the SHA-256 of its manifest
  text.
- Entry names use `/`: whole files at their path, `edits/<path>.lme`, `patches/<path>.lmd`.
- A pack may come from anyone, so reading one trusts none of its numbers: at most 32 GB, an index of at most 64 MB
  packed and unpacked, at most 100,000 entries of at most 1 GB each; entries lie between the header and the index, do
  not overlap or repeat a name, and never unpack past their declared size; no entry may go unused.

## The manifest (`pack.json`, format 2)

```json
{ "format": 2, "name": "Update to 0.695", "version": "0.695.1.132647", "requires": "0.589.1.128895",
  "files": [ { "path": "Resources\\Assets\\Assets_000.pack", "size": 33663672, "crc32": "…", "how": "edit",
               "fromSize": 33107130, "fromCrc32": "…" } ],
  "delete": [ "Some\\Old\\File.txt" ] }
```

| `how` | Entry | Used for |
|---|---|---|
| `edit` | `edits/<path>.lme` | Files that change in place (the asset packs: about 98 % of each stays where it is) |
| `patch` | `patches/<path>.lmd` | Files whose contents shift (the exe, text files) |
| `whole` | `<path>` | New files, and changed files where neither form is smaller |

`fromSize` / `fromCrc32` name the exact Steam file an edit or patch starts from; `size` / `crc32` are what the file
must be afterwards (and its SHA-256 must be the genuine file's, [Format: Strong Hash List](Format-Strong-Hash-List)).

Paths are relative with `\` or `/`. Refused: `..`, `.`, rooted, drive and UNC paths, `:` (also alternate data streams),
Windows device names, wildcard and control characters, names ending in a dot or space, anything named `LaunchPad*` at
the top or under `LaunchPadEmu\`, and a path listed twice or both written and deleted.

## In-place edit (`LME1`)

```
"LME1" | u64 size before | u32 CRC32 before | u64 size after | u32 CRC32 after | u32 n
n × { u64 offset, u32 length, length bytes }
```

Applying writes each range at its offset, then sets the file's length. The same format undoes an edit (the original
bytes of each range and the old length, saved before the file is touched).

## Delta (`LMD1`)

```
"LMD1" | u64 old size | u32 old CRC32 | u64 new size | u32 new CRC32
operations until the new file is complete:
  'C' copy     varint old offset (signed, from the end of the previous copy / add), varint length
  'A' add      the same fields, then length bytes d: new byte = old byte + d (mod 256)
  'L' literal  varint length, then the bytes
```

A delta rebuilds the new file only from the exact old file it was made from, so it is useless without a genuine copy of
the game. The "add" form suits code where addresses moved: its difference bytes are mostly 0 and compress to almost
nothing.

## What a valid update is

A pack installs only when it is the update to one of the two known 0.695 builds: every file is that build's file
(path, size and CRC32 pick it, the SHA-256 must match the genuine file), it carries the whole update (all 1,094 files)
and no settings file, it deletes only old Steam files the build doesn't have, and the player's whole game is that
build afterwards. Installing writes a backup first (it is also the journal for an interrupted install) and can be
undone. The full rules, and what a hostile server owner can and cannot do: `docs/launchpad.md` in the repository.

Code: [PatchPacks](Project-PatchPacks) (`PatchFile.cs`, `InPlaceEdit.cs`, `Delta.cs`, `Patching.cs`, `PackCheck.cs`).
Making a pack: LaunchPad's *Settings > Patch packs > Create update pack*, or
`LaunchPad.exe --make-update --steam <Steam folder> --from <0.695 folder or zips> --out update.lmpatch`.
