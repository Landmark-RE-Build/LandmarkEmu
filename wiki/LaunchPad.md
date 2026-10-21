# LaunchPad

LaunchPad is LandmarkEmu's replacement for Landmark's own launcher. Like the original, it is a window showing a web
page; here the page is built into the exe and talks to LaunchPad's own code. It:

- keeps a list of servers, added by **address** only (the server supplies its name, news, worlds, the game build it
  needs and its update);
- creates accounts and logs in with **SRP-6a**: the password never leaves your PC;
- installs the **0.695 update** on your Steam copy, from a file you have or from the server (it asks first), checking
  every file against the SHA-256 of Daybreak's genuine file, and keeps a backup so it can be undone;
- can **make the update pack** from a 0.695 copy you already have;
- **validates** your game files, starts the game pointed at the server, and comes back when the game closes or
  crashes.

It never downloads the game itself, and it only patches a complete, unchanged Steam install of Landmark
(0.589.1.128895). Setting it up: [Client Connection Guide](Client-Connection-Guide).

## Getting LaunchPad

Download `LaunchPad.exe` from the project's **Releases**. It is one self-contained file (no .NET install needed) and
needs the WebView2 runtime, which Windows 10 and 11 already have. **Only use the release from the project**, never a
copy from a server owner: the lists of genuine files LaunchPad checks against are built into it, and a changed
LaunchPad could trust anything.

To build it yourself (Windows): `dotnet publish LandmarkEmulator.LaunchPad -c Release -o publish/LaunchPad`
([Server Building on Windows](Server-Building-Windows)).

It runs only from the Landmark folder (next to `Landmark64.exe`, with the original renamed to `LaunchPad.exe.orig`),
and only one copy runs per game folder.

## The main button

| Button | Meaning |
|---|---|
| **MOVE ME** | LaunchPad isn't in a Landmark folder |
| **ADD SERVER** | No server yet: open Settings > Servers |
| **UPDATE** | The server offers the 0.695 update and your game doesn't have it |
| **PATCH** | The server needs another build and has no update to offer: install a pack yourself |
| **LOG IN** | Log in to the selected server |
| **PLAY** | Start the game |
| **STOP** | Cancel a long job (a check, an install, making a pack) |

**Auto-play** (next to the button, or Settings > Options) starts the game as soon as LaunchPad opens. It never starts
a download.

## Settings

| Tab | What's there |
|---|---|
| **Servers** | Add, rename or remove servers. The address is a host name or IP, with `:port` if the server's account port isn't 5000 |
| **Patch packs** | Install a pack, undo the last one, go back to the Steam game, the list of installed packs, the February-build option, and **Create update pack** |
| **Validate files** | Check every game file against what it should be |
| **Game folder** | The folder LaunchPad works on, its build, and **Verify the Steam copy** |
| **Options** | Auto-play, **Allow installing the update from servers**, the game language |
| **About** | What LaunchPad is, and a note on passwords |

Settings are kept in `%AppData%\LandmarkEmu\launchpad.json`. What LaunchPad has installed into a game folder is
recorded in that folder's `LaunchPadEmu\` subfolder (installed packs, their file lists, backups, a cache of file
hashes, downloads in progress).

## Accounts and logging in

- **Create account** (the account menu, top right): an account name of 3 to 32 letters, digits or `_`, and a password
  of at least 6 characters. LaunchPad works out the SRP salt and verifier on your PC and sends only those.
- **Log in** proves you know the password without sending it, and the server proves it holds your account's
  verifier.
- **Remember me on this PC** keeps a key that works only for that account on that server.
- Accounts belong to one server. Server owners can close sign-ups ([Account Creation](Account-Creation)).

## The update to 0.695

LaunchPad installs exactly one kind of patch: the update from the Steam build (0.589.1.128895) to **0.695.1.132647**
(pack version; the February exe's own build string is **0.699.1.132863**). It knows two genuine builds:

- **the January 2017 build** (`0.695.1.132647` in the exe): fully verified;
- **Daybreak's final build (14 February 2017)** (`0.699.1.132863` in the exe): the same files except `Landmark64.exe`
  and the English text (`Locale\en_us_data.dat` / `.dir`). Fully verified (SHA-256 of those three pinned 2026-10-20).

A pack is only installed when all of this holds:

1. It is the update from the Steam build to one of those builds, with all of its 1,094 files and no settings files.
2. Every file in it has the SHA-256 of Daybreak's genuine file. Files of the two builds can't be mixed.
3. Your game folder is a verified Steam copy that the pack completes to the whole build.

Before anything changes you see a preview: the build the pack leads to, or exactly why it is refused. The install
backs up every file it replaces or edits, checks each new file again before it swaps it in, and undoes itself if it
fails part-way (also after a crash or power cut: the next start finishes the undo).

### From a pack file

Settings > Patch packs > **Install a patch pack...**, or drop the `.lmpatch` file on the window.

### From the server

When the server offers the update (its owner put it in the server's patch folder, see
[Hosting Patches](Hosting-Patches)) and your game is the Steam build, the button says **UPDATE**. Clicking it:

1. fetches only the pack's file list, checks it is the pack the server announced and that it is the verified update,
   and shows you the server, the build, the download size and that every file will be checked;
2. downloads nothing until you click **Download and install** (never automatic, also not with Auto-play);
3. downloads into `LaunchPadEmu\downloads\` (a broken-off download resumes), never past the size the server announced;
4. checks the whole file, then every file in it, then installs it as above.

Any failure deletes the download, says why, and changes nothing. A server can refuse to offer the update or offer it
slowly, but it can't get anything onto your disk that isn't a genuine Daybreak file. To turn this off: Settings >
Options, untick **Allow installing the update from servers** (on by default).

### Create update pack

If you have a 0.695 copy of Landmark, LaunchPad can make the update pack from it, for yourself or for your server
(Settings > Patch packs > **Create update pack**):

1. **Your 0.695 game folder**: choose it. **Check the folder** tells you which build it is, or lists exactly which
   files are missing, different or can't be verified, and refuses.
2. **Steam copy**: *This game folder* (LaunchPad's own folder, when it is the unchanged Steam build), *Another
   folder...*, or *No Steam copy: every file whole*. With a Steam copy, changed files are stored as edits and patches
   (a much smaller pack, about 0.7 GB); without one every changed file goes in whole (bigger, but it still installs on
   any verified Steam copy).
3. **Save the pack as**: where to write the `.lmpatch` (not inside the 0.695 folder), then **Create update pack**.

The pack is checked again with the same rules every player and server use before LaunchPad says it's done. It holds
game files from your own copy and installs only on a verified Steam copy, so it is no use to anyone who doesn't own the
game.

The same is available on the command line:

```
LaunchPad.exe --make-update --steam <Steam game folder> --from <0.695 game folder> --out update.lmpatch
LaunchPad.exe --store check-695 <0.695 folder>
LaunchPad.exe --store verify-pack <pack.lmpatch> [<Steam folder>]
```

### The February build

Settings > Patch packs still has **Allow the February 2017 build's three files without a strong fingerprint (local pack
files only)** for older LaunchPads. With the current strong list those files are fully verified and the option has no
effect. It never applied to an update from a server.

### Undo, back to Steam, and validating

- **Undo the last patch**: puts back every file the last pack replaced, edited or deleted, and removes what it added.
- **Back to the Steam game**: undoes every installed pack.
- **Validate files** (its own tab): checks every game file against Steam's original or the installed update's version
  of it, and reports problems. It changes nothing.
- **Verify the Steam copy** (Game folder tab): checks the folder against Steam's own file list of the 0.589 install.
  The first full check reads about 20 GB (a few minutes); later checks use a cache and take about a second. If files
  are missing or changed, use Steam's Verify integrity of game files.

Files the game writes while it runs (its logs, cache, `UserOptions.ini`, ...), the `.ini` settings files at the top of
the folder and LaunchPad's own files are left out of every check. No program or script file is ever left out.

## What PLAY does

1. Checks `Landmark64.exe` is a genuine exe (Steam's 0.589 one or a known 0.695 build's) and refuses to start
   anything else.
2. Logs in again for a fresh session key.
3. Writes `ClientConfig.ini` so the game connects to the server's login port and opens the server's in-game web pages.
4. Starts `Landmark64.exe` with the session key.

While the game runs, LaunchPad is minimised; closing its window only hides it. When the game closes it comes back to
the front, and tells a crash (the game's crash handler's exit codes) from a normal exit.

## For developers

How it works inside (the pack format, the checks, the server side): `docs/launchpad.md` in the repository, and the
[Code Documentation](Code-Documentation).
