# Client Connection Guide

How to play on a LandmarkEmu server. You need:

- **Landmark in your Steam library**, installed by Steam (it installs build 0.589.1.128895);
- **LaunchPad**, the project's replacement for the game's own launcher (`LaunchPad.exe` from the project's
  **Releases**);
- the **address** of a LandmarkEmu server, from the person who runs it (on your own PC: `127.0.0.1`);
- Windows 10 or 11 (LaunchPad uses WebView2, which they already have).

The servers need the game at build **0.695.1.132647**. LaunchPad installs that update on your Steam copy (step 5).

## 1. Install Landmark from Steam

Install Landmark in Steam as usual and let it finish. If you have changed files in it, run Steam's
**Verify integrity of game files** (Landmark > Properties > Installed Files) first: LaunchPad only works on a complete,
unchanged Steam install.

## 2. Put LaunchPad in your Landmark folder

1. Open your Landmark folder: in Steam, Landmark > Manage > Browse local files. It is usually
   `C:\Program Files (x86)\Steam\steamapps\common\Landmark`, the folder with `Landmark64.exe`.
2. Rename the game's own `LaunchPad.exe` to `LaunchPad.exe.orig`.
3. Copy the new `LaunchPad.exe` into the folder.

LaunchPad only runs from that folder (next to `Landmark64.exe`): anywhere else its button says **MOVE ME**. Only one
copy runs per game folder.

## 3. Start LaunchPad

Start `LaunchPad.exe` from the folder, from a desktop shortcut to it, or add it to Steam as a non-Steam game
(Games > Add a Non-Steam Game). Steam's own Play button for Landmark no longer works: it stops at "getting legacy key",
because the Daybreak key service is gone.

## 4. Add the server

Click the gear (Settings) and open **Servers**. Type the server's **address** (a host name or IP address; add
`:port` only if the owner says the account port isn't 5000) and press **Save server**. The name is optional:
LaunchPad gets the name, news, worlds and everything else from the server.

## 5. Get the 0.695 update

If your game is still the Steam build, the main button says **UPDATE** or **PATCH**:

- **UPDATE**: the server offers the update. Click it, read what LaunchPad shows (the server, the build, the download
  size), then **Download and install**. Nothing is downloaded before you click.
- **PATCH**: the server needs 0.695 but has no update to offer. Install it from an `.lmpatch` file:
  Settings > Patch packs > **Install a patch pack...**, or drop the file on the window.

Either way LaunchPad checks every file against its own list of Daybreak's genuine files before anything changes, and
backs up what it replaces, so it can be undone. Details: [LaunchPad](LaunchPad#the-update-to-0695).

## 6. Create an account

Open the account menu at the top right (**Log in**) and choose **Create account**. Pick an account name (3 to 32
letters, digits or `_`) and a password of at least 6 characters. Some servers also have a sign-up website: the
**Account website** link under the server's name opens it.

Your password never leaves your PC: LaunchPad logs in with SRP, so the server only ever stores a verifier.
**Remember me on this PC** keeps a key that works only for that account on that server.

Each server has its own accounts: an account made on one server doesn't work on another.

## 7. Play

Press **PLAY**. LaunchPad logs in again for a fresh session, points the game at the server and starts it. Create a
character, pick the world and go.

LaunchPad minimises while you play and comes back when the game closes. If the game crashed, it says so (with the exit
code): see [Bug Submission](Bug-Submission).

## Problems

| Message or symptom | What to check |
|---|---|
| **MOVE ME** | LaunchPad isn't in the Landmark folder next to `Landmark64.exe`. |
| "No answer from ..." | The server isn't running, the address is wrong, or its account port (TCP 5000 by default) is blocked. Ask the owner. |
| Stuck on the loading screen | The server's public address isn't set for players on other PCs, or its world port is blocked. Ask the owner (see [Server Installation](Server-Installation#players-on-other-pcs)). |
| The update is refused | LaunchPad says exactly why. Usually a changed or missing game file: run Steam's Verify integrity of game files and try again. |
| The Showcase / store window is empty | The server's web pages port is blocked for you. Ask the owner. |

More about LaunchPad: [LaunchPad](LaunchPad).
