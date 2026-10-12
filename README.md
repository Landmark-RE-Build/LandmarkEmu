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

The generated configs listen on this PC only (`127.0.0.1`). To let others on your network join, change `Host` to
`0.0.0.0` under `Network` in `LandmarkEmulator.AuthServer\bin\Release\net10.0\AuthServer.json` and
`LandmarkEmulator.WorldServer\bin\Release\net10.0\WorldServer.json`, set the world server's address in the auth database
(`lmemu_auth.zone_server`, `host` column) to your PC's address, allow UDP 20042 and 19000 through the firewall, and have
each player point their `ClientConfig.ini` at `Server=<your address>:20042`. Accounts for them are created with the
`account create <name> <password>` command in the AuthServer window.

### Troubleshooting

- *MariaDB was not found / the service is missing*: install MariaDB with the "Install as service" option.
- *Could not create the database user*: the MariaDB root password was wrong; run `start.bat -Setup` again.
- *Nothing is listening on UDP 20042 / 19000*: look at the server window for the error (usually the database).
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
