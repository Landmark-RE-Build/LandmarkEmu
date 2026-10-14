# LaunchPad (LandmarkEmulator.LaunchPad)

A replacement for Landmark's own `LaunchPad.exe`, for LandmarkEmu servers. It is laid out like the original (a native
window showing a web page; the page drives login, patching and launching through a bridge). Here the page is local
(`wwwroot/`, shown with WebView2) and the bridge is C# (`Bridge.cs`).

What it does:

- Players add a server by its **address** only. The server's `/info` supplies the rest: name, login port, in-game web
  pages, news, the game build it needs, and its patches.
- Account creation and login use SRP-6a. The password never leaves the PC. "Remember me" keeps a key that works only for
  that account on that server.
- It installs the 0.695 update from an `.lmpatch` file the player has. Patches only go on top of a verified Steam
  copy of the game, and every change is backed up so it can be undone.
- It points `ClientConfig.ini` at the server and starts `Landmark64.exe` with a fresh session key.

It never downloads the game itself. LaunchPad only patches a complete, unchanged Steam install of Landmark
0.589.1.128895.

## Installing (players)

1. Build it, or take a release: `dotnet publish LandmarkEmulator.LaunchPad -c Release -o publish/LaunchPad`. This makes one
   self-contained `LaunchPad.exe` (about 65 MB, no .NET install needed). It needs the WebView2 runtime, which Windows 10/11
   already have.
2. In the Landmark folder (`steamapps\common\Landmark`), rename the original `LaunchPad.exe` to `LaunchPad.exe.orig` and
   put this one there. Start it from the folder, a desktop shortcut, or as a non-Steam game in Steam: Steam's own Play
   button can no longer start Landmark (it stops at "getting legacy key": the Daybreak key service is gone).
3. It refuses to run anywhere except next to `Landmark64.exe`. Only one copy runs per game folder.

Settings (servers, saved logins, options) live in `%AppData%\LandmarkEmu\launchpad.json`. The game folder's own state is
kept in `<game>\LaunchPadEmu\`:

| Path | What |
|---|---|
| `installed.json` | The installed packs, oldest first: id (SHA-256 of pack.json), name, backup file |
| `packs\<id>.json` | Each installed pack's list (used by undo and Validate) |
| `backups\*.zip` | One backup per pack: `undo/` (the original bytes of each in-place edit), `files/` (every file it replaced, patched or deleted, compressed), `added.txt`, `pack.json` |
| `hashes.json` | CRC32 and SHA-1 per game file, keyed by size and write time (the first full check reads 20 GB, about 4 minutes; later checks take about 1 s) |

## The Steam check

`Data/steam-0589-file-list.csv` is Steam's own manifest of the 0.589 install: depot 297811, manifest 4876286683328705192,
Steam build 1170900, created 2016-06-08. It was decoded from `Steam\depotcache\297811_4876286683328705192.manifest` by
`re/tools/steam_manifest.py`. It holds 33,654 files and 20,078,582,928 bytes, each with a size and SHA-1.

- **The format.** The manifest is a series of sections, each `[magic u32][length u32][protobuf]`:
  - `0x71F617D0`: the payload. Its field 1 repeats per file: 1 = name, 2 = size, 3 = flags (64 = directory), 5 = SHA-1.
  - `0x1F4812BE`: the metadata.
  - `0x1B81B817`: the signature.
- **Empty files.** Steam lists an empty file's SHA-1 as zeros. The launcher uses the real SHA-1 of no bytes instead.
- **Files left out of the check.**
  - `LaunchPad.*`: the old launcher. It updated itself after Steam installed it, and this launcher replaces it.
  - The `.ini` files in the game folder: the player's settings, and `ClientConfig.ini`, which the launcher rewrites.
  - Files an installed pack changed. Their originals were checked before the pack went on, and they are in the backups.
- **What happens on a mismatch.** Anything else missing or changed stops patching. The player is told to use Steam's
  "Verify integrity of game files".

## The update: the only patch LaunchPad installs

LaunchPad installs one kind of patch: an update from the Steam build
(0.589.1.128895) to one of two known builds of 0.695.1.132647 (`Patching.Targets`).

- **Daybreak's final build.** The files of their last update (14 Feb 2017), exactly as their own file list has them.
- **The January 2017 build.** The same list, except for three files from before that last update, held to their exact
  size and CRC32:
  - `Landmark64.exe` 2FF380AE, built 4 Jan 2017;
  - `Locale\en_us_data.dat` FFF22327;
  - `Locale\en_us_data.dir` 6166AEA1.

  On 14 Feb Daybreak recompiled the exe and changed some English text. A compile stamps a new random debug ID into the
  exe, so the February exe can't be rebuilt from the January one; the only surviving copies are the January ones.

A pack passes only when all of these hold:
1. It says it leads to that version from the Steam build.
2. Every file in it is that build's file, by path, size and CRC32. Files of the two builds can't be mixed.
3. It only deletes old Steam files the build doesn't have.
4. With it applied, the player's whole game is that build. This is checked on the player's verified Steam copy before
   anything is changed.

The update is 1,094 files: 583 new and 511 changed. 86 of the changed files kept their size, so they are only found by
CRC32. The list is `LandmarkEmulator.WebAPI/update-0589-to-0695-files.csv`, made by `re/tools/update_files.py`.

## Patch packs (.lmpatch)

A pack is one `.lmpatch` file (`PatchFile.cs`):

```
"LMPATCH1" | u64 index offset | u64 index length | entries, each Brotli-compressed | index
index = Brotli-compressed JSON { "manifest": "<pack.json text>", "entries": [ { name, offset, stored, size, sha256 } ] }
```

Every entry is checked against its SHA-256 as it is read. The pack's identity is the SHA-256 of its manifest text.
Brotli makes packs about a third smaller than the same data in a .zip.

The manifest (`pack.json`, format 2):

```json
{ "format": 2, "name": "Update to 0.695", "version": "0.695.1.132647", "requires": "0.589.1.128895",
  "files": [ { "path": "Resources\\Assets\\Assets_000.pack", "size": 33663672, "crc32": "...", "how": "edit",
               "fromSize": 33107130, "fromCrc32": "..." } ],
  "delete": [ "Some\\Old\\File.txt" ] }
```

Each file says how it is stored (`how`):

| How | Entry | Used for |
|---|---|---|
| `edit` | `edits/<path>.lme` (`InPlaceEdit.cs`) | Files that change in place: the byte ranges that differ at the same position, plus anything added at the end. The asset packs (~98% of each stays where it is). |
| `patch` | `patches/<path>.lmd` (`Delta.cs`) | Files whose contents shift (the exe, text files): copy / add / literal instructions that rebuild the file from its 0.589 version. |
| `whole` | `<path>` | New files, and changed files where neither is smaller. |

`fromSize` / `fromCrc32` name the exact Steam file an edit or patch starts from; `size` / `crc32` are what the file must
be afterwards.

- **Paths.** Paths are relative and use `\` or `/`. `..`, rooted paths, `LaunchPad.exe` and `LaunchPadEmu\` are refused.
- **What gets installed.** Only the verified update to 0.695 (see "The update" above). Anything else is refused before
  a byte changes.
- **Installing.** A pack is installed in this order:
  1. Check the build and the Steam copy, and that the result would be the whole 0.695 build.
  2. Check the pack: whole files against their size and CRC32, edits and patches against the exact Steam file they start
     from.
  3. Write the backup (`backups\<time>-<id>.zip`): the original bytes each edit overwrites (`undo/`), and every file
     replaced, patched or deleted, compressed (`files/`). It is also the journal. While `installing.json` exists, an
     interrupted install is undone from it the next time LaunchPad opens.
  4. Apply: edit files in place (a hard-linked file is edited as a copy, so another install sharing it keeps its
     file), rebuild patched files next to the old ones and swap them in, copy whole files. Each result's size and CRC32
     is checked. A failure part-way restores the backup.
- **Undo.** The undo removes added files and writes the edited ranges' original bytes back (with the old length). It
  then restores the replaced and deleted files. "Back to the Steam game" undoes everything.
- **Validate files.** This checks every file against what it should be: Steam's file, or the update's version of it.
  Files the update deleted must be gone.

Players install the update from **Settings > Patch packs**: choose its `.lmpatch`, or drop it on the window. They see
a preview first: the build it leads to, or exactly why it is refused.

### Making the update

```
LaunchPad.exe --make-update --steam <Steam game folder> --from <0.695 game folder> --out update.lmpatch
LaunchPad.exe --make-update --steam <Steam game folder> --from <zip> [--from <zip> ...] --out update.lmpatch
```

- **`--steam`:** a verified Steam 0.589 copy. It is checked first, and the edits and patches are made against its files.
- **`--from` a 0.695 game folder:** every file that doesn't match Steam's own 0.589 list (not there, or another size or
  SHA-1) goes in.
- **`--from` zips:** the update's files (`update-0589-to-0695-files.csv`), read without unpacking.
- For each changed file the smallest form is stored. The result is checked as a verified update.
- `--delta-test <old> <new>` checks a patch round trip on two files.

`--make-pack <folder> --out <pack.lmpatch>` packs whole files from a folder (`--base` keeps only files that differ from
another folder).

Packs carry game data, so they are never part of this project: `*.lmpatch` is in `.gitignore`.

## Server side (LandmarkEmulator.WebAPI)

`AuthServer.json` → `WebApi`:

| Setting | |
|---|---|
| `ServerName` | Shown in the launcher |
| `WebsiteUrl` | The "Account website" link (empty: none) |
| `RequiredClientBuild` | The build players need. Players on another build see PATCH and install the update themselves |
| `NewsFile` | JSON `[{ "title", "date", "text" }]`, re-read on every request |
| `LoginPort` | The login port players reach. 0 = the auth server's own (`Network.Port`); set it when a router or Docker maps another one |

The account service exposes these to the launcher (`AuthWebAPI.cs`):

- **`GET /info`.** Returns name, websiteUrl, requiredClientBuild, registrationOpen, loginPort, the worlds (name, up,
  players, webPagesUrl) and the news.
- **The SRP endpoints:** `/srp/register`, `/srp/start` and `/srp/finish`.

In Docker (`docker/auth/docker-compose.yml`), `docker/launcher/` is mounted at `/launcher`; `news.json` there is the news.

The `SERVER_NAME`, `WEBSITE_URL`, `REQUIRED_CLIENT_BUILD` and `LAUNCHER_DIR` variables go in `docker/auth/.env`.

### What PLAY does

1. Logs in again for a fresh session key.
2. Writes `ClientConfig.ini`:
   - `Server=<address>:<loginPort>`;
   - `MarketplaceUrl` = the first world's web pages URL. A loopback host is replaced by the server's address.
   - `DirectThreadCount=2`;
3. Starts `Landmark64.exe SessionId=<key> CasSessionId=0 STEAM_ENABLED=0`.

### While the game runs

- LaunchPad minimises when the game starts.
- Closing it while the game runs only hides it: it keeps watching the game.
- When the game closes, LaunchPad comes back to the front, whether the player used Exit Game or the game crashed.
- **Telling a crash from a normal close.** The client's crash handler (`SetUnhandledExceptionFilter`, the
  `wws_crashreport` code) runs `wws_crashreport_uploader.exe`, waits for it, then calls `TerminateProcess(self, -1)`.
  The uploader can also end the game with 0xFF. If the handler fails, Windows ends the game with the exception code.
  So -1, 0xFF or any code from 0x80000000 up shows "Landmark crashed" with the code; any other exit is a normal close.

The account service may be HTTP (release config, Docker) or HTTPS with a self-signed certificate (dev config). The
launcher tries HTTP first and remembers which one works. It doesn't check the certificate, because SRP already proves the
server holds the account's verifier.
