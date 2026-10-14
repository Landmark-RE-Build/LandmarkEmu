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

## Hosting a server (Windows)

### 1. What you need

- **Windows 10 or 11**, about 10 GB free for Docker and the server's data, and 16 GB of RAM or more.
- **[Docker Desktop](https://www.docker.com/products/docker-desktop/)**: install it, start it once, accept its terms.
- **Landmark** from Steam (build 0.589.1.128895).
- This project: **Code → Download ZIP** (or `git clone`), unpacked anywhere, e.g. `C:\LandmarkEmu`.
- Recommended: **`LandmarkEmu-terrain-heights.zip`** from this project's [Releases](../../releases). Put it next to
  `start.bat` before the first run. It holds a ground-height grid for each landscape, which the server uses for trees,
  creature footing and starting build sites. Without it those stay off.

### 2. First start

Double-click **`start.bat`**. The first run asks three things:

1. **Your Landmark folder**, the one with `Landmark64.exe` (e.g. `C:\Program Files (x86)\Steam\steamapps\common\Landmark`).
   The game's data tables are copied from it into `gameData\`. Nothing in your game folder is changed.
2. **Your account name**, to be an admin on your world. You create the account itself in LaunchPad (below); leave it
   empty for no admin.
3. **The address players use to reach this PC.** Leave it empty if only you will play on this PC; see
   [Playing with others](#4-playing-with-others) otherwise.

Then it builds the servers (the first time takes several minutes) and starts them. When it says
**"LandmarkEmu is running"**, the server is ready.

Later, `start.bat` just starts the servers again. Everything is kept between runs: accounts, characters, builds.

| Command | |
|---|---|
| `start.bat` | Start the servers |
| `start.bat -Stop` | Stop them (all data is kept) |
| `start.bat -Logs` | Watch the world server's console (Ctrl+C to leave) |
| `start.bat -Update` | Rebuild the servers after downloading a new version of this project |
| `start.bat -Setup` | Ask the first-run questions again |

### 3. The settings: `network.env`

All the network settings are in **one file, `network.env`**, next to `start.bat`. Edit it with Notepad, save it, and
run `start.bat` again.

| Setting | Default | What it is |
|---|---|---|
| `PUBLIC_HOST` | *(empty: this PC only)* | The address other players use to reach this PC |
| `LOGIN_PORT` | 20042 (UDP) | The login server |
| `ACCOUNT_PORT` | 5000 (TCP) | Accounts and logins (LaunchPad, the website) |
| `WORLD_PORT` | 19000 (UDP) | The game world |
| `WEB_PAGES_PORT` | 19001 (TCP) | The in-game Showcase, store and Player Studio pages |
| `WEBSITE_PORT` | 8080 (TCP) | The account sign-up website |
| `DB_PORT` | 3307 (TCP) | The database, for tools on this PC only |

The other settings files (`docker\auth\.env` with the database passwords and folders, and `docker\worlds\main.env` for
your world's name and admins) are made by the first run, and you rarely need to touch them.

### 4. Playing with others

1. **Choose the address** and put it in `network.env` as `PUBLIC_HOST`:
   - **Same home network:** this PC's LAN address. `ipconfig` shows it as "IPv4 Address", e.g. `192.168.1.20`.
   - **Friends elsewhere:** the easiest and safest way is a VPN app such as [Tailscale](https://tailscale.com/) or
     [ZeroTier](https://www.zerotier.com/). Everyone joins your network, and you use the address it gives this PC.
     The other way is your public IP address with the ports forwarded on your router (see below).
2. **Run `start.bat` again.** It offers to open the ports in Windows Firewall (Windows asks for permission).
3. **Router (internet without a VPN only):** forward UDP `LOGIN_PORT` and `WORLD_PORT`, and TCP `ACCOUNT_PORT`,
   `WEB_PAGES_PORT` and `WEBSITE_PORT`, to this PC.
4. **Tell players the address.** They add it in LaunchPad.

### 5. Admins

Admins type **`/admin`** in chat to open the admin page: god mode, revive, coins and items, travel, claiming a site,
time of day, creatures, a server shutdown countdown, and any developer command, for themselves, everyone or one player.
Set admins in `docker\worlds\main.env` (`ADMIN_ACCOUNT`, `ADMIN_2`, `ADMIN_3`: account or character names), then run
`start.bat` again.

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

- **"Docker Desktop is not installed / did not start":** start Docker Desktop by hand, wait until it says it is running,
  then run `start.bat` again.
- **"No world is up after 4 minutes":** run `start.bat -Logs` to see the world server's console; the error is usually
  near the end.
- **LaunchPad: "No answer from <address>":** the servers aren't running, the address is wrong, or TCP `ACCOUNT_PORT`
  is blocked (firewall, router).
- **Another PC gets stuck on the loading screen:** `PUBLIC_HOST` is empty or wrong (it must be the address *they*
  reach this PC on), or UDP `WORLD_PORT` is blocked. Run `start.bat` after changing `network.env`.
- **The in-game Showcase / store pages are empty:** TCP `WEB_PAGES_PORT` is blocked for that player.
- **Names missing in game:** the Landmark folder had no `Locale\en_us_data.dat`; check the game folder, then run
  `start.bat -Setup`.
- The world server's console log and its saved files are in `server-data\worlds\main\`.

---

## What's here

| Folder | |
|---|---|
| `LandmarkEmulator.AuthServer`, `LandmarkEmulator.WorldServer`, `LandmarkEmulator.Shared`, `LandmarkEmulator.Database.*`, `LandmarkEmulator.WebAPI`, `LandmarkEmulator.Gateway` | The servers |
| `LandmarkEmulator.LaunchPad` | LaunchPad, the players' launcher |
| `docker/` | How the servers run in Docker (database, login server, account website, worlds) |
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
