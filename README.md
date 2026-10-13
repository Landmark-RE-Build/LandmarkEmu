# LandmarkEmu

A server emulator for **Landmark**, the voxel building MMO that was shut down in 2017, written in C#. It talks to the
Steam client build **0.589.1.128895** (Landmark's 2016 launch build) and aims to recreate the live game: the world you
can dig and build in, build sites, designs, props, crafting, gathering, gear and combat, creatures and caverns,
chat / friends / groups / guilds / mail / trade / vendors, achievements and daily goals, site games and the story
tools. See [docs/gameplay.md](docs/gameplay.md) for what each system does.

> You need your own copy of the Landmark client. This repository contains **no game files**: the setup copies the
> data tables it needs from your install on your own PC.

## Quick start (Windows)

1. Install:
   - the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0);
   - [MariaDB](https://mariadb.org/download/) (or `winget install MariaDB.Server`) — keep "Install as service"
     ticked; note the root password if you set one;
   - Landmark, build 0.589.1.128895.
2. Download this repository (Code → Download ZIP, or `git clone`).
3. Double-click **`start.bat`**.

The first run asks a few questions and sets everything up:

- **Landmark folder** — the folder with `Landmark64.exe` (common Steam folders are found automatically).
- **MariaDB root password** — only used once, to create a database user for the emulator with a random password.
- It builds the servers, copies the game's data tables into `gameData\`, and writes the server configs.
- **Point the game at this server?** — sets `Server=127.0.0.1:20042` in the game's `ClientConfig.ini` (the original is
  kept as `ClientConfig.ini.orig`; copy it back to undo).
- **Account name / password** — your login on this server.

**Terrain heights (recommended):** download `LandmarkEmu-terrain-heights.zip` from this project's
[Releases](../../releases) and put it next to `start.bat` before the first run (or run `start.bat -Setup` after adding
it). It holds a ground-height grid for each landscape, which the server uses for trees, creature footing and ground
checks; without it those stay off. (It was baked from the client with `tools/terrain/`, which you can also run
yourself.)

The answers are saved in `launcher-settings.json` (on your PC only). After that, `start.bat` starts the servers and
logs you straight into the game.

| Command | Does |
|---|---|
| `start.bat` | Start the servers (if not running) and the game |
| `start.bat -NoGame` | Servers only |
| `start.bat -Setup` | Run the first-time setup again (game moved, new account, rebuild) |

Unattended setup: `start.bat -GamePath "D:\Games\Landmark" -Account me -Password secret [-DbRootPassword pw] [-KeepClientConfig]`.

### Playing with others on your network

**Host (the PC that runs the servers):** open `server.ini` next to `start.bat` (made on the first run) and set
`ServerAddress=` to the address the other players reach your PC on — e.g. `ServerAddress=192.168.1.20` (`ipconfig`
shows it as "IPv4 Address"), or a VPN address (Tailscale, ZeroTier). Close the two server windows and run `start.bat`
again. It makes the servers listen on the network, tells players' games to load the world from that address (without
it they stop on the loading screen) and offers to open the ports in Windows Firewall (UDP 20042 and 19000, TCP 5000
and 19001), and writes **`Play LandmarkEmu.bat`** next to `start.bat` with that address built in. Empty
`ServerAddress=` means this PC only (no player file).

**Players on other PCs:** send them the one file **`Play LandmarkEmu.bat`** (Discord, e-mail, a USB stick) — they
don't need this project, .NET, MariaDB or a build, only Landmark. They double-click it: the first run asks for their
Landmark folder and an account name / password, then it points their `ClientConfig.ini` at your server, creates the
account there, logs in and starts Landmark. Later runs just play. Their settings stay on their PC
(`%LOCALAPPDATA%\LandmarkEmu`); running the file with `-Setup` changes the folder or account. If your address changes,
run `start.bat` again and send the new file.

**Passwords never cross the network.** `start.bat` and `Play LandmarkEmu.bat` log in with SRP-6a (the same idea as Blizzard's and
Apple's password logins): the launcher proves it knows the password without sending it, the server keeps only a
verifier it cannot turn back into the password, and the session key comes back encrypted. The launchers save a derived
key in their settings file, never the password itself. The old plain-text login only answers on the server PC.
The game's own traffic after login is Landmark's protocol and is not encrypted (it carries no password), so for
playing over the internet a VPN (Tailscale, ZeroTier) is still the safer choice than opening ports.

### Admin menu

The account `start.bat` logs in with is an admin; add more in `server.ini` (`Admins=name1, name2`, then restart the
servers). Admins type **`/admin`** in chat to open the Admin page in the in-game browser: god mode, revive, coins and
items, travel (Spire, own site, caves, landscapes, to a player), claim a site, play mode, time of day, creatures, a
server shutdown countdown, and a box for any developer command — for themselves, everyone, or a chosen player.

### Troubleshooting

- *MariaDB was not found / the service is missing*: install MariaDB with the "Install as service" option.
- *Could not create the database user*: the MariaDB root password was wrong; run `start.bat -Setup` again.
- *Nothing is listening on UDP 20042 / 19000*: look at the server window for the error (usually the database).
- *Another PC gets stuck on the loading screen*: the host's `server.ini` has no `ServerAddress` (or the servers were
  not restarted after setting it), or UDP 19000 is blocked by a firewall on the host.
- *Play LandmarkEmu.bat: "No answer from the server"*: the host's servers are not running, the address is wrong, or TCP 5000 is
  blocked on the host.
- The server windows show everything that happens; `console.log` next to each server keeps it.

## What's here

| Folder | |
|---|---|
| `LandmarkEmulator.AuthServer`, `LandmarkEmulator.WorldServer`, `LandmarkEmulator.Shared`, `LandmarkEmulator.Database.*` | The servers |
| `docs/` | How the client and server talk (packet layouts), terrain formats, and how each system works |
| `research/` | The reverse-engineering notes: each client message family with its reader / handler functions and field layouts, the client's Lua bindings, classes, data sources and rules, the server's verdict on every client message, and the system specs the server follows |
| `data/` | Small tables the server needs that were derived from the client (weapon animation classes) |
| `tools/terrain/` | Optional: bakes a ground-height grid from the running client (better creature footing) |

The `research/` notes name client functions by address (`FUN_14xxxxxxx`, build 0.589.1.128895) and describe what they
read and do; decompiled client code itself is not included. Load your own copy of `Landmark64.exe` in Ghidra to follow
them.

## Credits and licence

Based on [kirmmin/LandmarkEmu](https://github.com/kirmmin/LandmarkEmu) and the
[PheebeUK](https://github.com/PheebeUK/LandmarkEmu) fork; extended for the 0.589 client. Licensed under the GNU AGPL
v3 (see `LICENSE`). Landmark is a trademark of its owners; this project is not affiliated with them.
