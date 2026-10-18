# LandmarkEmulator.LaunchPad

**Windows program** (`LaunchPad.exe`, `net10.0-windows`, WPF + WebView2). The player's launcher for LandmarkEmu
servers, a replacement for the game's own launcher. How to use it: [LaunchPad](LaunchPad) and
[Client Connection Guide](Client-Connection-Guide).

Back to [Code Documentation](Code-Documentation).

## What it does

- Players add a server by its address only; everything else (name, login port, in-game web pages, news, the build it
  needs, its patches) comes from the server's `/info`.
- Creates accounts and logs in with SRP-6a: the password never leaves the PC (`SrpClient.cs`: the RFC 5054 2048-bit
  group, SHA-256, PBKDF2 for the private key).
- Installs the 0.695 update from an `.lmpatch` pack the player has or from the server (it asks first, every time), only
  on top of a verified Steam copy, every file held to the SHA-256 of Daybreak's genuine file, with a backup so it can be
  undone. Players with a 0.695 copy can make the pack themselves.
- Points `ClientConfig.ini` at the server and starts `Landmark64.exe` with a fresh session key; shows the player as
  "Playing Landmark" in Steam while it runs.

## Main files

| Path | What |
|---|---|
| `App.xaml.cs` | Start-up, single instance per game folder, the command-line modes (`--make-update`, `--make-pack`, `--store …`, `--delta-test`) |
| `MainWindow.xaml(.cs)` | The frameless window with the launcher page in WebView2 |
| `wwwroot/` | The launcher page (`index.html`, `launchpad.js`, `launchpad.css`) |
| `Bridge.cs` | The page's native side: JSON requests `{ id, method, args }`, answers and events (login, patching, launching, progress) |
| `ServerApi.cs` | The server's web API: `/info`, `/srp/*`, the patches |
| `SrpClient.cs` | SRP-6a, the player's side |
| `Game.cs` | Finding the install, its build, pointing it at a server, starting it |
| `Settings.cs` | `launchpad.json`: the servers added and the options |
| `SteamPresence.cs` | The Steam "Playing" state |

All the patch-pack work is in [PatchPacks](Project-PatchPacks). Formats: [Format: LMPatch](Format-LMPatch),
[Format: Strong Hash List](Format-Strong-Hash-List); the full description is `docs/launchpad.md` in the repository.

Players should get `LaunchPad.exe` from the project's releases only: the hashes built into it are what every file is
checked against.
