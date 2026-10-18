# Hosting Patches

Players need build 0.695.1.132647; Steam installs 0.589. Your server can **offer the update** to your players'
LaunchPad, so they don't need the update file themselves. This is optional: players can also install it from an
`.lmpatch` file they have ([LaunchPad](LaunchPad#the-update-to-0695)).

The update pack carries Daybreak's game files, so it is never part of this project. You make it yourself from your
own 0.695 copy, and it only installs on a verified Steam copy of the game.

## 1. Make the update pack

In LaunchPad, on a PC with a 0.695 Landmark folder: Settings > Patch packs > **Create update pack**
([LaunchPad](LaunchPad#create-update-pack)). With a Steam copy to compare against, the pack is about 0.7 GB.

## 2. Put it in the patch folder

The patch folder is `docker/launcher/patches/` in the project folder (or the folder `PATCH_DIR` in `.env` names).
Copy the `.lmpatch` file into it. The file name may use letters, digits, `.`, `_` and `-`; packs are listed in file
name order. There is no upload page: the folder is the upload path.

Nothing is restarted: the auth server notices the new file (a file watcher, and a size and time check every minute)
and checks it once it is completely copied.

## 3. The check

Before a pack is offered to anyone, the server checks it with **the same rules every player's LaunchPad uses**:

- it must be the verified update from the Steam build to a known 0.695 build;
- the pack's index must be sane, and every entry must unpack within its declared size to its declared SHA-256;
- every whole file must be Daybreak's genuine file by SHA-256, and every edit or patch must lead to a genuine file's
  size and CRC32.

The server has no 0.589 game files of its own, so by default it checks edits and patches by their headers; each
player's LaunchPad replays them on their own Steam copy before anything changes. To replay them on the server too, set
`PATCH_VERIFY_STEAM_DIR` in `.env` to a Steam 0.589 copy of Landmark (mounted read-only).

The result is in the auth server's log (`docker compose logs auth`):

| Log line | Meaning |
|---|---|
| `Patch packs: <file> PASSED: the verified update to <build>, <n> files, pack SHA-256 <hash>. ...` | The pack is offered. Its SHA-256 is announced to LaunchPad, which checks the download against it |
| `Patch packs: <file> REFUSED and never served: <reason>` | The pack is never offered. It is moved to `rejected/` in the patch folder, with a `.reason.txt` next to it |
| `Patch packs: <file> can't be read yet (...); checking it again once it is complete.` | The file is still being copied |

A pack whose size or time changes after it passed is not served until it has passed again, and while it is being sent
it is held open read-only, so it can't change on the way.

## 4. What players see

When a player's game is the Steam build with nothing installed and your server offers a verified pack, LaunchPad's
button says **UPDATE**. LaunchPad shows your server, the build, the download size and that every file will be checked,
and downloads nothing until the player clicks **Download and install**. Players can switch this off (Settings >
Options > Allow installing the update from servers).

## Related settings

| Setting (`.env`) | |
|---|---|
| `PATCH_DIR` | The patch folder (empty = `docker/launcher/patches`) |
| `PATCH_VERIFY_STEAM_DIR` | Optional Steam 0.589 copy for replaying edits and patches (it can be the same folder as `GAME_DIR` only if that folder is still the Steam build) |
| `REQUIRED_CLIENT_BUILD` | The build players need (empty = any). A player on another build sees **PATCH** when you offer no update |
| `SERVER_NAME`, `WEBSITE_URL` | What LaunchPad shows for your server |

The news LaunchPad shows comes from `docker/launcher/news.json` (a list of `{ "title", "date", "text" }`, read again on
every request).

## Trust

Players trust the LaunchPad they downloaded from the project, and the lists of genuine files built into it, not your
server. A server can refuse to offer the update, offer it slowly, or offer something else, and anything else is
refused and deleted on the player's side. Never hand out your own `LaunchPad.exe`: players should always get it from
the project's Releases.
