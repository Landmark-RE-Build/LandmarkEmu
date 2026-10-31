# LaunchPad (LandmarkEmulator.LaunchPad)

A replacement for Landmark's own `LaunchPad.exe`, for LandmarkEmu servers. It is laid out like the original (a native
window showing a web page; the page drives login, patching and launching through a bridge). Here the page is local
(`wwwroot/`, shown with WebView2) and the bridge is C# (`Bridge.cs`).

What it does:

- Players add a server by its **address** only. The server's `/info` supplies the rest: name, login port, in-game web
  pages, news, the game build it needs, and its patches.
- Account creation and login use SRP-6a. The password never leaves the PC. "Remember me" keeps a key that works only for
  that account on that server.
- It installs the 0.695 update from an `.lmpatch` file the player has, or from the server the player plays on (it asks
  first, every time). Patches only go on top of a verified Steam copy of the game, every file is held to the SHA-256 of
  Daybreak's genuine file before it replaces anything, and every change is backed up so it can be undone.
- A player who has a 0.695 copy can make the update pack from it (Settings > Patch packs > Create update pack).
- It points `ClientConfig.ini` at the server and starts `Landmark64.exe` with a fresh session key. Advanced mode shows
  every value it writes there and lets the player choose the asset thread count
  ("Advanced mode" below).

It never downloads the game itself. LaunchPad only patches a complete, unchanged Steam install of Landmark
0.589.1.128895.

## Installing (players)

1. Build it, or take a release: `dotnet publish LandmarkEmulator.LaunchPad -c Release -o publish/LaunchPad`. This makes one
   self-contained `LaunchPad.exe` (about 65 MB, no .NET install needed). It needs the WebView2 runtime, which Windows 10/11
   already have.
2. In the Landmark folder (`steamapps\common\Landmark`), rename the original `LaunchPad.exe` to `LaunchPad.exe.orig` and
   put this one there. Start it from the folder, a desktop shortcut, or as a non-Steam game in Steam: Steam's own Play
   button can no longer start Landmark (it stops at "getting legacy key": the Daybreak key service is gone).
3. It refuses to run anywhere except next to `Landmark64.exe`. Only one copy runs per game folder. (What counts as a
   game folder, its build and whether its game is running: `LandmarkEmulator.PatchPacks/GameFolder.cs`.)

Settings (servers, saved logins, options) live in `%AppData%\LandmarkEmu\launchpad.json`. The game folder's own state is
kept in `<game>\LaunchPadEmu\`:

| Path | What |
|---|---|
| `installed.json` | The installed packs, oldest first: id (SHA-256 of pack.json), name, backup file |
| `packs\<id>.json` | Each installed pack's list (used by undo and Validate) |
| `backups\*.zip` | One backup per pack: `undo/` (the original bytes of each in-place edit), `files/` (every file it replaced, patched or deleted, compressed), `added.txt`, `pack.json` |
| `hashes.json` | CRC32, SHA-1 and SHA-256 per game file, keyed by size and write time (the first full check reads 20 GB, about 4 minutes; later checks take about 1 s) |
| `downloads\<id>.lmpatch` | The update downloaded from a server (kept after an install or undo, so it isn't downloaded again; `.part` while downloading) |
| `staging\` | Files being built (patched, whole, or an edited copy of a hard-linked file) until their SHA-256 is checked and they are swapped in |

## The Steam check

`LandmarkEmulator.PatchPacks/Data/steam-0589-file-list.csv` is Steam's own manifest of the 0.589 install: depot 297811, manifest 4876286683328705192,
Steam build 1170900, created 2016-06-08. It was decoded from `Steam\depotcache\297811_4876286683328705192.manifest` (the
format is below). It holds 33,654 files and 20,078,582,928 bytes, each with a size and SHA-1.

- **The format.** The manifest is a series of sections, each `[magic u32][length u32][protobuf]`:
  - `0x71F617D0`: the payload. Its field 1 repeats per file: 1 = name, 2 = size, 3 = flags (64 = directory), 5 = SHA-1.
  - `0x1F4812BE`: the metadata.
  - `0x1B81B817`: the signature.
- **Empty files.** Steam lists an empty file's SHA-1 as zeros. The launcher uses the real SHA-1 of no bytes instead.
- **Files left out of the check.**
  - `LaunchPad*` (`LaunchPad.exe`, `LaunchPad.ini`, `LaunchPad.libs`): the old launcher. It updated itself after Steam installed it, and this launcher replaces it.
  - The `.ini` files in the game folder: the player's settings, and `ClientConfig.ini`, which the launcher rewrites.
  - Files an installed pack changed. Their originals were checked before the pack went on, and they are in the backups.
- **What happens on a mismatch.** Anything else missing or changed stops patching. The player is told to use Steam's
  "Verify integrity of game files".

## The update: the only patch LaunchPad installs

LaunchPad installs one kind of patch: an update from the Steam build
(0.589.1.128895) to one of two known builds of 0.695.1.132647 (`Patching.Targets`).

- **Daybreak's final build.** The files of their last update (14 Feb 2017), exactly as their own file list has them.
  The exe's embedded build string is `0.699.1.132863` (live captures match); pack manifests still say
  `0.695.1.132647`.
- **The January 2017 build.** The same list, except for three files from before that last update, held to their exact
  size and CRC32:
  - `Landmark64.exe` 2FF380AE, built 4 Jan 2017 (build string `0.695.1.132647`);
  - `Locale\en_us_data.dat` FFF22327;
  - `Locale\en_us_data.dir` 6166AEA1.

  On 14 Feb Daybreak recompiled the exe and changed some English text. A compile stamps a new random debug ID into the
  exe, so the February exe can't be rebuilt from the January one.

A pack passes only when all of these hold (`Patching.CheckUpdate`, `PackCheck`, `GameStore.Install`):
1. It says it leads to that version from the Steam build.
2. Every file in it is that build's file: path, size and CRC32 pick the file, and its bytes must have the SHA-256 of
   Daybreak's genuine file (the strong list, below). Files of the two builds can't be mixed. A file whose genuine SHA-256
   LaunchPad doesn't have is refused, never trusted on its CRC32.
3. It carries the whole update (all 1,094 files) and no settings file; edits and patches start from the Steam file at
   that path.
4. It only deletes old Steam files the build doesn't have.
5. With it applied, the player's whole game is that build. This is checked on the player's verified Steam copy before
   anything is changed (by SHA-256, or by Steam's SHA-1 for files 0.695 kept from 0.589).

## The strong list: what every file is checked against

`LandmarkEmulator.PatchPacks/Data/landmark-0695-files-sha256.csv` (`RelativePath,Size,CRC32,SHA256`) holds the SHA-256
of every genuine file of both builds, one row per path, size and CRC32 (so a path that differs between the builds, like
`Landmark64.exe`, has one row per build). It is built into LaunchPad and the server, not a file next to them. It was made
by `tools/strong_hash_list.py` from local copies, taking a file's SHA-256 only from a copy whose size and CRC32 match
Daybreak's list for that build. The hashes are of the exact bytes: nothing is "normalised" (no debug ids, timestamps or
checksums stripped); LaunchPad only ever copies Daybreak's files, it never rebuilds them.

| | Files | SHA-256 known |
|---|---|---|
| January 2017 build (`0.695.1.132647` in the exe) | 34,138 | all |
| Daybreak's final build (Feb 2017; exe build string `0.699.1.132863`) | 34,138 | all |

Sources (2026-10-18): the two zips of the 0.695 files (1,094 files), and the Steam 0.589 install for the 33,044 files
0.695 kept unchanged. The final build's three differing files (`Landmark64.exe` ED231C3C, `Locale\en_us_data.dat`
10E110DF, `Locale\en_us_data.dir` 7C48670B) were pinned 2026-10-20 from genuine copies (size + CRC32 match Daybreak's
list). Pack manifests still use version `0.695.1.132647` (`Patching.FinalBuild`); `GameFolder.Build` reads the string
embedded in the exe, so a final install shows `0.699.1.132863`. Independent check: a separate SHA-256 list of 106
exe/dll files (2026-10-14) agrees on all 95 files both lists have, including the January `Landmark64.exe` (8B0FC625...).

**The February opt-in** (Settings > Patch packs: "Allow the February 2017 build's three files without a strong
fingerprint (local pack files only)", `AllowFebruaryWeak` in `launchpad.json`, off by default) is a no-op now that the
strong list covers those files (`Patching.WeakAllowed`). It remains in the UI and in tests that deliberately omit the
fingerprints. Before 2026-10-20 it let a local pack write those three files checked by size and CRC32 only; it never
applied to a server's update.

Does the SHA-256 refuse anything the old size + CRC32 check accepted for a genuine copy? No. Both need the exact bytes
of Daybreak's file; the SHA-256 only stops a file made to have the right size and CRC32 with other bytes (forging a
CRC32 takes four chosen bytes).

**Left out of every check** (on the player's side and the server's): only paths no list has (the game's own `Logs\`,
`Cache\`, crash dumps, `ui.log`, `CommandHistory.txt`, `UISettings.xml`, `UserOptions.ini`, `SavedClientTimers.txt`, and
`LaunchPadEmu\`), the `.ini` settings files at the top of the folder (`ClientConfig.ini`, which LaunchPad rewrites,
`Graphics.ini`, `DeepGraphicsWrapper.ini`, `vld.ini`), and anything named `LaunchPad*` at the top (the old launcher
and this one). No pack may write or delete any of those. No program or script (.exe, .dll, .lua, .swf, ...) is ever left
out: every listed file is checked (`EmbeddedListTests.LeftOutFiles_AreNeverCode`).

## What a hostile server owner can and cannot do

Players trust the LaunchPad.exe they run, and through it the lists built into it. Nothing a server sends is trusted for
coming from that server:

- **Can:** refuse to serve the update, serve it slowly, or serve something else. Anything else is refused: the list is
  checked before the download, the download is checked before the install, and a refused download is deleted.
- **Cannot:** get a byte onto the player's disk outside `LaunchPadEmu\downloads\` that isn't a genuine Daybreak file.
  Every file the install writes has the SHA-256 of the genuine file, checked before it replaces anything; no path outside
  the build's file list is created; only old Steam files the build doesn't have are deleted; nothing is written through
  a junction or link (`GameFolder.RequirePlainPath` in `LandmarkEmulator.PatchPacks/GameFolder.cs`); `LaunchPad*` and `LaunchPadEmu\` are off limits; nothing downloaded is ever run or opened (PLAY
  only starts a `Landmark64.exe` that is Steam's 0.589 exe or a known 0.695 build's, by SHA-1 / SHA-256).
- **Cannot:** fill the disk or memory with a "bomb": the download stops at the size `/info` advertised, the pack and
  its index have size limits, and no entry unpacks past its declared size (which must be the build file's size).
- The remaining trust root is LaunchPad.exe itself (and so its built-in lists). **Players must get LaunchPad.exe from
  the project's release, never from a server owner**: a changed LaunchPad could trust anything.
- The connection may be plain HTTP: that changes nothing above, because trust comes from the built-in hashes, not the
  connection. (HTTPS would only hide which files are fetched.)

The update is 1,094 files: 583 new and 511 changed. 86 of the changed files kept their size, so they are only found by
CRC32. The list is `LandmarkEmulator.PatchPacks/Data/update-0589-to-0695-files.csv`, made by comparing the two builds' file
lists (`steam-0589-file-list.csv` and Daybreak's final list, `landmark-final-2017-file-list.csv`).

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

- **Paths.** Paths are relative and use `\` or `/`. Refused: `..` and `.`, rooted, drive and UNC paths, `:` (also
  alternate data streams), Windows device names (`CON`, `NUL`, `COM1`, ... also with an extension), wildcard and control
  characters, names ending in a dot or space, anything named `LaunchPad*` at the top and `LaunchPadEmu\`, and a path
  listed twice or both written and deleted.
- **Reading a pack** (`PatchFile.Reader`): at most 32 GB, an index of at most 64 MB packed and unpacked (it stops
  unpacking there), at most 100,000 entries of at most 1 GB each; every entry lies between the header and the index, no
  two overlap, no name repeats, and an entry never unpacks past its declared size. A whole file's entry must be exactly
  the build file's size; no entry may go unused.
- **What gets installed.** Only the verified update to 0.695 (see "The update" above). Anything else is refused before
  a byte changes.
- **Installing.** A pack is installed in this order:
  1. Check the build and the Steam copy, and that the result would be the whole 0.695 build.
  2. Check the pack: whole files against the genuine file's SHA-256; edits and patches against the exact Steam file
     they start from. An in-place edit's result SHA-256 is worked out first by reading the Steam file with the edit's
     ranges laid over it (nothing written), and must be the genuine file's.
  3. Write the backup (`backups\<time>-<id>.zip`): the original bytes each edit overwrites (`undo/`), and every file
     replaced, patched or deleted, compressed (`files/`). It is also the journal. While `installing.json` exists, an
     interrupted install is undone from it the next time LaunchPad opens.
  4. Apply: edit files in place (a hard-linked file is edited as a copy, so another install sharing it keeps its
     file); rebuild patched files and write whole files in `LaunchPadEmu\staging\` and swap each in only when its
     SHA-256 is the genuine file's. Each result's size, CRC32 and SHA-256 is checked again. A failure part-way restores
     the backup.
- **Undo.** The undo removes added files and writes the edited ranges' original bytes back (with the old length). It
  then restores the replaced and deleted files. "Back to the Steam game" undoes everything.
- **Validate files.** This checks every file against what it should be: Steam's file, or the update's version of it.
  Files the update deleted must be gone.

Players install the update from **Settings > Patch packs**: choose its `.lmpatch`, or drop it on the window. They see
a preview first: the build it leads to, or exactly why it is refused. Or from the server they play on (below).

### Making the update

**In LaunchPad** (Settings > Patch packs > Create update pack), for players who have a 0.695 copy:

1. Choose the 0.695 game folder. "Check the folder" (and Create) first checks every file of the update in it against
   the strong list by SHA-256: it says which build it is, or lists exactly which files are missing, different or can't
   be verified (first 50 and the count), and refuses. Files the game writes as it runs don't matter (see above).
2. Steam copy: "This game folder" (LaunchPad's own folder, when it is the Steam 0.589 build with nothing installed; it
   is verified against Steam's list first), another folder, or none. With one, changed files are stored as edits and
   patches (about 0.7 GB); without one every changed and new file goes in whole (bigger; it still installs on any
   verified Steam copy).
3. Choose where to save the `.lmpatch` (not inside the 0.695 folder). The pack is made off the window's thread with
   progress (STOP cancels; nothing is left behind), then checked again with the same rules a server and every player
   use (`PackCheck.Verify`, replaying every edit and patch on the Steam copy when there is one) before it says done.

The pack holds game files from the player's own copy and installs only on a verified Steam copy of the game.

**On the command line** (the same code, `Patching.MakeUpdate`):

```
LaunchPad.exe --make-update --steam <Steam game folder> --from <0.695 game folder> --out update.lmpatch
LaunchPad.exe --make-update --steam <Steam game folder> --from <zip> [--from <zip> ...] --out update.lmpatch
```

- **`--steam`:** a verified Steam 0.589 copy. It is checked first, and the edits and patches are made against its files.
- **`--from` a 0.695 game folder:** the update's 1,094 files (`update-0589-to-0695-files.csv`) are taken from it. Each
  must have the SHA-256 of one build's genuine file, or nothing is made.
- **`--from` zips:** the update's files (`update-0589-to-0695-files.csv`), read without unpacking.
- For each changed file the smallest form is stored. The result is checked as a verified update.
- `--delta-test <old> <new>` checks a patch round trip on two files.
- `--store check-695 <0.695 folder>`: which build a 0.695 folder is (the check Create update pack runs first).
- `--store verify-pack <pack.lmpatch> [<Steam folder>]`: the check a server and every player run on a pack.

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
| `PatchFolder` | The patch folder (below). Empty = no patches. Docker: `PATCH_DIR`, default `docker/launcher/patches` |
| `PatchVerifySteamDir` | Optional: a Steam 0.589 copy (read only) so the patch check also replays edits and patches. Docker: `PATCH_VERIFY_STEAM_DIR` |

The account service exposes these to the launcher (`AuthWebAPI.cs`):

- **`GET /info`.** Returns name, websiteUrl, requiredClientBuild, registrationOpen, loginPort, the verified patches
  (id, name, version, files, bytes, delete, `sha256` of the pack.json, `packBytes` and `packSha256` of the file, build),
  the worlds (name, up, players, webPagesUrl) and the news.
- **`GET /patches`, `/patches/{id}/pack.json`, `/patches/{id}/pack.lmpatch`** (Range supported): the verified packs only.
- **The SRP endpoints:** `/srp/register`, `/srp/start` and `/srp/finish`.

In Docker (`docker/auth/docker-compose.yml`), `docker/launcher/` is mounted at `/launcher`; `news.json` there is the news.
`docker/launcher/patches/` (`PATCH_DIR`) is mounted writable at `/launcher/patches`.

### The patch folder (`PatchHost.cs`, `PatchLibrary`)

The server owner puts (uploads) the update pack in the patch folder: copy the `.lmpatch` there (any name of letters,
digits, `.`, `_`, `-`; packs are listed in file-name order). There is no upload page: the account service has no admin
area, so the folder is the upload path.

Nothing in the folder is served until it has passed the check every player's LaunchPad runs (`PackCheck.Verify`): the
verified update to a known build; the index sane; every entry unpacked within its declared size with its SHA-256; every
whole file the genuine file by SHA-256; every edit and patch leading to a genuine file's size and CRC32 (whose SHA-256 is
in the list). The server has no 0.589 game files, so it can't replay edits and patches: their results are checked by
each player's LaunchPad on their own Steam copy before anything changes; with `PatchVerifySteamDir` the server replays
them too. The check runs at start-up and whenever the folder changes (a file watcher, and a size / write-time check
every minute). A file still being copied in is checked once it is complete.

- A pack that passes is logged (`Patch packs: <file> PASSED ... pack SHA-256 ...`) and its size, write time and SHA-256
  recorded; `/info` advertises that SHA-256.
- A pack that fails is logged with the reason (`REFUSED and never served: ...`) and moved to `rejected/` with a
  `.reason.txt`.
- A pack whose size or write time changes after it passed is not served (503) until it has passed again. When it is
  sent it is held open read-only, so it can't change while it goes out.

### Installing the update from the server

Settings > Options > "Allow installing the update from servers" (`AllowServerUpdates` in `launchpad.json`, on by
default). When the game is the Steam build with nothing installed and the server lists a verified pack, the button says
UPDATE. The steps are `LandmarkEmulator.PatchPacks/ServerPatches.cs` (`FromInfo` reads `/info`, `Needed` picks the
pack this game needs, `Check` is step 1 below, `Install` runs 1-5 again before installing; LaunchPad's `Bridge.cs` only calls them). Clicking it:

1. Fetches only the pack's list (`pack.json`), checks it is the one `/info` announced (SHA-256) and that it is the
   verified update (the built-in lists). Then it shows the server, the build it leads to, the download size and that
   every file will be checked. Nothing is downloaded until the player clicks "Download and install" (never automatic,
   also not with Auto-play).
2. Checks the game folder is a verified Steam copy the update would complete.
3. Downloads to `LaunchPadEmu\downloads\` (resumed with Range if it broke off), never past the advertised size.
4. Checks the whole file's SHA-256 against `/info` (finds a swapped or damaged file), that its own list is the list
   checked in 1, then every entry (`PackCheck.Verify`).
5. Installs it as above (backed up, every file's SHA-256 checked again before it replaces anything).

Any failure deletes the download, says exactly why, and changes nothing. Undo and "Back to the Steam game" work for it
as for any pack. A game that already has the update (from any source) is never undone to make room for a server's pack.

The `SERVER_NAME`, `WEBSITE_URL` and `REQUIRED_CLIENT_BUILD` settings go in the project's `.env`.

### What PLAY does

1. Checks `Landmark64.exe` is a genuine exe (Steam's 0.589 one, or a known 0.695 build's; from the hash cache, so
   about a second) and refuses to start anything else.
2. Logs in again for a fresh session key.
3. Writes `ClientConfig.ini` (`ClientConfigFile.PointAt` in `LandmarkEmulator.PatchPacks/ClientConfig.cs`):
   - `Server=<address>:<loginPort>` (before the first section);
   - `[Marketplace] MarketplaceUrl` = the first world's web pages URL. A loopback host is replaced by the server's address.
   - `[Marketplace] MarketplaceAssets` = the same URL: the design and build-site screenshots the client downloads come
     from the server's web pages (before 2026-10-26 Steam's Daybreak address stayed and every screenshot was blank).
   - `[AssetDelivery] DirectThreadCount=2`, or in advanced mode the player's choice (below).

   A missing line is added in its own section. The first change keeps the original as `ClientConfig.ini.orig`. The file
   is written beside itself and renamed over the old one, so a hard link to Steam's copy is replaced, never written
   through; bytes outside ASCII are kept as they are.
4. Starts `Landmark64.exe SessionId=<key> CasSessionId=0 STEAM_ENABLED=0`.

### Advanced mode

Settings > Options > **Advanced mode** (off by default). It is kept per game folder in `launchpad.json`:

```json
"ClientConfig": {
  "D:\\Steam\\steamapps\\common\\Landmark": {
    "Advanced": true,
    "AllocatorSlotExpand": "Off",
    "Choices": { "DirectThreadCount": { "Mode": "Custom", "Value": "4" } }
  }
}
```

A folder gets an entry only once the player changes something there. Without an entry, or with `Advanced` false, PLAY
writes the values above exactly as before advanced mode existed; the choices are kept while it is off and count again
when it is switched back on. Advanced mode adds one tab.

**Allocator slot expand** (experimental, Client settings): Off (default) / Auto / On. Not a `ClientConfig.ini` key —
LaunchPad keeps it only in `launchpad.json`. On **0.695 and 0.699** only, PLAY may start `Landmark64.exe` suspended,
read every known rewrite site, and only then move the in-memory per-thread allocator table from 64 to 127 slots
(nothing written to the exe on disk). Auto applies when the PC has 16 or more logical processors. If the PE stamp or
any site byte check fails, LaunchPad shows an alert (e.g. wrong or already-patched build), resumes the process
**unpatched**, and the game still starts. LandmarkLocal has the same setting and the same alert on Play.

**Contour threads** (experimental, Client settings): Off (default) / Auto / On. Raises VoxelFarm contour /
Contouring Job workers from 3 to 5 in memory. **Requires Allocator slot expand Auto or On** — the
UI disables this control otherwise, and turning allocator expand Off clears contour expand to Off. Applied only after
allocator expand **succeeds** on that start; site mismatch shows a separate alert. Sites:
`research/trace/0695/contour-thread-patch-sites.md`.

**Client settings** lists every value PLAY writes, each with what it does, the game's original (from
`ClientConfig.ini.orig`, else Steam's file), LaunchPad's value, the file's current value and what the next PLAY writes:

| Value | Choice | Why |
|---|---|---|
| `Server=` | always set | The chosen server's login address. Without it the game can't reach that server; Steam's value is Daybreak's login server, which is gone. |
| `[Marketplace] MarketplaceUrl=` | always set | The server's in-game web pages (Showcase: store, gallery, following). The world signs the player's Showcase in on its own pages only (`Store.SendSignIn` opens `<WebPagesUrl>/?t=<token>`, and the token works on that world only), so another address shows pages that don't know the player; one that doesn't answer makes the client's browser reload forever, which has frozen the client (`WebPages.cs`). The server owner sets it (`WebPagesUrl`), not the player. |
| `[Marketplace] MarketplaceAssets=` | always set | The same address as `MarketplaceUrl`: where the game downloads design and build-site screenshots (Designs window tiles, Player Studio and site-management screenshot viewers), which the world's web pages serve (`WebPages.cs` TemplateImage / SiteImage). Steam's value is Daybreak's image server, which is gone. |
| `[AssetDelivery] DirectThreadCount=` | **LaunchPad sets it** (2, recommended), **Use my value** (a whole number from 1 to 10), or **Leave the file as it is** (PLAY doesn't touch the line, and adds none) | See below. |

`DirectThreadCount` in the 0.695 client: `CreateAssetSystem` (FUN_142caff80) reads it with the integer reader
FUN_142b21540 (an atoi, FUN_142b213d0; **4** when the line is missing; no clamp) and passes it to
`AssetDeliveryDirect` (FUN_143b30030; with `IndirectEnabled=1`, as shipped, through `AssetDeliveryWrapper`
FUN_143b32550 / FUN_143b32cb0). Its Init (FUN_143b310f0, vtable 0x14491cbc0 slot 1) starts exactly that many
"FileLoader" threads, `for (i = 0; i < count; i++)`. So 0, a negative number or text starts none and nothing loads: 1
is the least that works. The code has no upper limit; each thread takes one of the allocator's 64 per-thread slots
(docs/terrain.md "Slow loading screen: allocator slots"), which is why Steam's 10 stalls the loading screen. LaunchPad
allows 1 to Steam's 10. A custom value that fails the check (launchpad.json edited by hand) is written as 2.

The same slots decide how fast a design's placement preview ("Generating preview... XX.XX%", one thread started at
the click) runs (docs/gameplay.md "Placing a design"). `DirectThreadCount` is the only thread count the game reads from
its files; other pools start one thread per logical processor with no limit (`Resource::Manager Decompression`,
`TaskManagerJob`), so on CPUs with 16 or more logical processors the table can be full even at 2 [I]. LaunchPad cannot
change those without modifying the game; the player-side fixes are in the gameplay doc.

**What PLAY writes, and nothing else.** Only the three lines above change (and, in a file that has no such
section, a `[Marketplace]` or `[AssetDelivery]` header after a blank line). Every other line stays exactly as it was,
including lines LaunchPad doesn't manage. It never writes any other key; in particular never the `[Terrain]
UseCachedMeshes=off` / `CacheMeshes=off` lines an old workspace script added to some players' files (they do nothing:
UserOptions.ini has the same keys, the game's Options window, and those win; docs/terrain.md; a player may delete them
by hand). The only files it writes are `ClientConfig.ini` and, once, `ClientConfig.ini.orig`; its temporary
`ClientConfig.ini.lpnew` is renamed over the file or deleted if the write fails.

The bridge refuses a choice while advanced mode is off (`Bridge.Advanced`); the panel only reads the file. Tests:
`LandmarkEmulator.Tests/PatchPacks/ClientConfigTests.cs` (each choice, advanced mode off, the range, missing lines,
that only the listed lines change and no other file is written, the original, hard links, bytes outside ASCII,
the choices' JSON).

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
