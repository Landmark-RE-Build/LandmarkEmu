# LandmarkEmu

A server emulator for **Landmark**, the voxel building MMO that was shut down in 2017, written in C#. It talks to the
Steam client build **0.589.1.128895** (Landmark's 2016 launch build) and aims to recreate the live game: the world you
can dig and build in, build sites, designs, props, crafting, gathering, gear and combat, creatures and caverns,
chat / friends / groups / guilds / mail / trade / vendors, achievements and daily goals, site games and the story
tools. See [docs/gameplay.md](docs/gameplay.md) for what each system does.

The servers run in **Docker**. Players connect with **LaunchPad**, a replacement for the game's own launcher that logs
in to LandmarkEmu servers.

> Everyone needs their own copy of Landmark (the Steam version). This repository contains **no game files**: the server
> copies the data tables it needs from your own install, and LaunchPad works on your own install.

---

## Hosting a server

The servers run with **Docker Compose**: a database, the login server, an account sign-up website and the world
server, each in its own container.

### 1. What you need

- **[Docker](https://docs.docker.com/get-docker/)**: Docker Desktop on Windows or macOS (install it, start it once,
  accept its terms), or Docker Engine with the compose plugin on Linux. About 10 GB free and 16 GB of RAM or more.
- **Landmark** from Steam (build 0.589.1.128895), on the PC running the servers.
- This project: **Code → Download ZIP** (or `git clone`), unpacked anywhere, e.g. `C:\LandmarkEmu`.
- Recommended: **`LandmarkEmu-terrain-heights.zip`** from this project's [Releases](../../releases). Put it in the
  project folder (next to `docker-compose.yml`), still zipped. It holds a ground-height grid for each landscape, which
  the server uses for trees, creature footing and starting build sites. Without it those stay off.

### 2. Settings: `.env`

Copy **`.env.example`** to **`.env`** in the project folder and open it in a text editor. Every setting is explained
in the file; the ones to fill in are:

| Setting | |
|---|---|
| `GAME_DIR` | Your Landmark folder, the one with `Landmark64.exe` (forward slashes, e.g. `C:/Program Files (x86)/Steam/steamapps/common/Landmark`). It is only read: nothing in it is changed. |
| `DB_ROOT_PASSWORD`, `DB_PASSWORD` | Two long random passwords of your choice (letters and digits). Only the containers use them. |
| `PUBLIC_HOST` | The address other players use to reach this PC. Empty: only this PC can play. See [Playing with others](#4-playing-with-others). |
| `ADMIN_ACCOUNT` | Your account name, to be an admin on your world (you create the account in LaunchPad). |

The network settings are all together at the top of the file:

| Setting | Default | What it is |
|---|---|---|
| `PUBLIC_HOST` | *(empty: this PC only)* | The address other players use to reach this PC |
| `LOGIN_PORT` | 20042 (UDP) | The login server |
| `ACCOUNT_PORT` | 5000 (TCP) | Accounts and logins (LaunchPad, the website) |
| `WORLD_PORT` | 19000 (UDP) | The game world |
| `WEB_PAGES_PORT` | 19001 (TCP) | The in-game Showcase, store and Player Studio pages |
| `WEBSITE_PORT` | 8080 (TCP) | The account sign-up website |
| `DB_PORT` | 3307 (TCP) | The database, for tools on this PC only |

### 3. Start it

In a terminal in the project folder:

```
docker compose up -d --build
```

The first time builds the servers and takes several minutes. A `setup` step copies the game's data tables from
`GAME_DIR` into `server-data\gamedata` (and unpacks the terrain heights), then the servers start. The world is ready
about a minute later: `docker compose logs -f world` shows its console (Ctrl+C leaves it).

| Command | |
|---|---|
| `docker compose up -d` | Start the servers, or apply changed settings |
| `docker compose down` | Stop them (all data is kept) |
| `docker compose logs -f world` | Watch the world server's console |
| `docker compose up -d --build` | Rebuild after downloading a new version of this project |

Everything is kept between runs: accounts, characters and builds in the database, and each world's files in
`server-data\worlds\`.

**On Windows, `start.bat` does all of this for you** (optional): on its first run it asks for your Landmark folder,
admin name and address, writes `.env` with random passwords, offers to open the firewall ports, then runs
`docker compose up` and tells you when the world is up. Later: `start.bat`, `start.bat -Stop`, `start.bat -Logs`,
`start.bat -Update`, `start.bat -Setup` (ask the questions again).

### 4. Playing with others

1. **Choose the address** and put it in `.env` as `PUBLIC_HOST`:
   - **Same home network:** this PC's LAN address. `ipconfig` shows it as "IPv4 Address", e.g. `192.168.1.20`.
   - **Friends elsewhere:** the easiest and safest way is a VPN app such as [Tailscale](https://tailscale.com/) or
     [ZeroTier](https://www.zerotier.com/). Everyone joins your network, and you use the address it gives this PC.
     The other way is your public IP address with the ports forwarded on your router (see below).
2. **Run `docker compose up -d` again.**
3. **Open the ports** in the firewall: UDP `LOGIN_PORT` and `WORLD_PORT`, TCP `ACCOUNT_PORT`, `WEB_PAGES_PORT` and
   `WEBSITE_PORT` (`start.bat` offers to do it on Windows).
4. **Router (internet without a VPN only):** forward the same ports to this PC.
5. **Tell players the address.** They add it in LaunchPad.

### 5. Admins

Admins type **`/admin`** in chat to open the admin page: god mode, revive, coins and items, travel, claiming a site,
time of day, creatures, a server shutdown countdown, and any developer command, for themselves, everyone or one player.
Set admins in `.env` (`ADMIN_ACCOUNT`, `ADMIN_2`, `ADMIN_3`: account or character names), then run
`docker compose up -d` again.

More worlds, the account website, backups and every detail: [docs/docker.md](docs/docker.md).

---

## Playing (every player, including the host)

1. **Get LaunchPad:** download `LaunchPad.exe` from this project's [Releases](../../releases).
2. **Put it in your Landmark folder.** Rename the game's own `LaunchPad.exe` to `LaunchPad.exe.orig` first, then copy
   the new one in. LaunchPad only runs from that folder (next to `Landmark64.exe`).
3. **Start it** from the folder, or from a desktop shortcut to it. Steam's own Play button can no longer start Landmark:
   Steam stops at "getting legacy key", because the old Daybreak key service is gone. You can also add `LaunchPad.exe`
   to Steam as a non-Steam game.
4. **Add the server:** in LaunchPad, **Settings → Servers**, type the server's address (just the address, e.g.
   `192.168.1.20`; on the host's own PC, `127.0.0.1`) and save. LaunchPad gets everything else from the server.
5. **Create an account** (Log in ▾ → Create account), or use the server's sign-up website. Your password never leaves
   your PC: logins use SRP.
6. **Press PLAY.**

LaunchPad also checks your game files against Steam's own list (Settings → Game folder), and comes back to the front
when the game closes or crashes. See [docs/launchpad.md](docs/launchpad.md).

---

## Troubleshooting

- **`docker compose` can't connect to Docker:** start Docker Desktop, wait until it says it is running, and try again.
- **"Set GAME_DIR / DB_PASSWORD in .env":** `.env` is missing or that setting is empty (see step 2).
- **"GAME_DIR in .env is not your Landmark folder":** `docker compose logs setup` shows it; fix `GAME_DIR`.
- **The world doesn't come up:** `docker compose logs world` shows the world server's console; the error is usually
  near the end.
- **LaunchPad: "No answer from <address>":** the servers aren't running, the address is wrong, or TCP `ACCOUNT_PORT`
  is blocked (firewall, router).
- **Another PC gets stuck on the loading screen:** `PUBLIC_HOST` is empty or wrong (it must be the address *they*
  reach this PC on), or UDP `WORLD_PORT` is blocked. Run `docker compose up -d` after changing `.env`.
- **The in-game Showcase / store pages are empty:** TCP `WEB_PAGES_PORT` is blocked for that player.
- **Names missing in game:** the Landmark folder had no `Locale\en_us_data.dat`; check `GAME_DIR`, then run
  `docker compose up -d` again.
- The world server's console log and its saved files are in `server-data\worlds\main\`.

---

## What's here

| Folder | |
|---|---|
| `LandmarkEmulator.AuthServer`, `LandmarkEmulator.WorldServer`, `LandmarkEmulator.Shared`, `LandmarkEmulator.Database.*`, `LandmarkEmulator.WebAPI`, `LandmarkEmulator.Gateway` | The servers |
| `LandmarkEmulator.LaunchPad` | LaunchPad, the players' launcher |
| `docker-compose.yml`, `.env.example`, `docker/` | How the servers run in Docker (setup, database, login server, account website, worlds) |
| `docs/` | How the client and server talk (packet layouts), terrain formats, and how each system works |
| `research/` | The reverse-engineering notes: each client message family with its reader / handler functions and field layouts, the client's Lua bindings, classes, data sources and rules, and the system specs the server follows |
| `data/` | Small tables the server needs that were derived from the client |
| `tools/terrain/` | Optional: bakes the ground-height grid from the running client |

The `research/` notes name client functions by address (`FUN_14xxxxxxx`, build 0.589.1.128895) and describe what they
read and do. Decompiled client code itself is not included; load your own copy of `Landmark64.exe` in Ghidra to follow
them. [docs/docker.md](docs/docker.md) explains the Docker setup in detail, including running more than one world.

## Credits and licence

Based on [kirmmin/LandmarkEmu](https://github.com/kirmmin/LandmarkEmu) and the
[PheebeUK](https://github.com/PheebeUK/LandmarkEmu) fork; extended for the 0.589 client. Licensed under the GNU AGPL
v3 (see `LICENSE`).

LandmarkEmu is a fan project. It is not affiliated with or endorsed by Daybreak Game Company; Landmark is a trademark of
its owners. It contains no game files, and everyone needs their own copy of the game.
