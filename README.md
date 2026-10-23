# LandmarkEmu

A server emulator for **Landmark**, the voxel building MMO by Daybreak Game Company that shut down in February 2017,
written in C#. The goal is to bring back the Landmark server as it was at shutdown, for the last client build,
**0.695.1.132647**: the world you dig and build in, build sites, designs, props, crafting, gathering, gear and combat,
creatures and the Chaos Caverns, chat / friends / groups / guilds / mail / trade, goals, site games, the story tools
and the Showcase. Older 0.589 clients are refused.

Everything was worked out from the retail client itself: how it reads each packet, what it expects back, and what
the live game's patch notes and wiki say it did. The [docs](docs/README.md) explain the formats and how each system
works; see the project wiki for guides.

> **You need your own copy of Landmark** (the Steam version). This repository contains **no game files**. The server
> copies the data tables it needs from your own install, and LaunchPad only updates your own install from 0.589 to
> 0.695 with Daybreak's genuine files.

---

## Project status

**What works.** Almost every system of the live game is built: logging in and character creation, the world and its
islands, digging and building with every tool, build sites (placing, permissions, upkeep, the new-player Pioneer
lands), designs and the Player Studio, props and their settings, crafting stations and the Lumen Station, gathering
and ore veins, weapons, abilities, armour and accessory effects, creatures, outposts and graveyards in the Chaos
Caverns, travel (the Spire, the Landscapes window, Teleport to Friend, Lost Crystals), chat channels, friends, groups,
guilds, mail, trade and storage chests, goals and daily goals, the story tools and Play Mode, site games and the Game
Finder, the Showcase store and gallery, membership and the trial key, admin commands, scheduled restarts and the login
queue.

**What is still open.**

- Many features are built but have not been checked in the game yet, or only with one player. The feature list in
  `research/trace/0695/FEATURES.md` marks each one.
- Some numbers lived only on Daybreak's servers (combat values, loot rates, creature stats, which weapon each creature
  held). Where no source survives, the server uses its own values, and the setting or the docs say so.
- The live game split its server into several services (gateway, world channel, social, commerce, Environment Server).
  They are built and can each run in their own container, but they are opt-in and still being tested. Splitting one
  world over several machines (one process per landscape) is not built.
- A few client systems have no server side yet: the NPC merchant window, rendered character portraits, royalty shares
  for designs with several contributors.

Every gameplay setting defaults to how live Landmark behaved, and server owners can change each one (`.env`).

---

## Quick start — play locally on your PC (Windows)

New to LandmarkEmu and just want a hit of pure Landmark goodness? This is the simplest path: one app runs the servers and starts the game.

### What you need

1. **Landmark** on this PC (Remember, if you're using Steam you can't use Steam's Play button)
2. The **LandmarkLocal** zip from this project’s [Releases](/releases)  
   (self-contained — no separate .NET install)

### Play in five steps

1. Unpack the zip anywhere (e.g. `C:\LandmarkLocal`).
2. Double-click **`LandmarkLocal.exe`**.
3. Confirm your **Landmark folder** (the one with `Landmark64.exe` — usually found automatically for Steam installs), then choose an
   **account name** and **password**. That account is both your normal login and **admin** on this local world.
4. Click **Start** and wait for **Ready** (the first time can take more than several minutes: it downloads a local database and
   terrain data, plus the world server has do a bunch of stuff the first time it runs).
5. Click **Play**. Landmark should start. You do not need to open LaunchPad for this path.

Later visits: open LandmarkLocal → **Start** → **Ready** → **Play** (Your password is remembered).  
To shut servers down: **Stop**.  

More detail and troubleshooting (fresh PCs, Smart App Control, DirectX): [docs/landmark-local.md](docs/landmark-local.md).

---

## Playing on someone else’s server (LaunchPad)

Use this when a friend hosts a server (or you use Docker hosting below) and you join over the network.

1. **Get LaunchPad:** download `LaunchPad.exe` from this project's [Releases](../../releases) (always from here,
   never from a server owner).
2. **Put it in your Landmark folder** (`steamapps\common\Landmark`, next to `Landmark64.exe`). Rename the game's own
   `LaunchPad.exe` to `LaunchPad.exe.orig` first. LaunchPad only runs from that folder.
3. **Start it** from the folder, a desktop shortcut, or as a non-Steam game in Steam.
4. **Add the server:** Settings → Servers, type the server's address and save.
   LaunchPad gets everything else from the server.
5. **Update the game to 0.695.** The Steam copy is 0.589. LaunchPad installs the 0.695 update from an `.lmpatch` file
   you have (Settings → Patch packs), or from the server you play on: it shows **UPDATE** and asks first, every time.
   Details: [docs/launchpad.md](docs/launchpad.md).
6. **Create an account** (Log in ▾ → Create account), or use the server's sign-up website. Your password never leaves
   your PC: logins use SRP.
7. **Press PLAY.**

---

## Hosting a server for others (Docker)

**Multi-user / internet:** the servers run in **Docker**. Each part of the server is its own container:

| Service | |
|---|---|
| `setup` | Copies the data tables from your Landmark folder into `server-data/gamedata`, then exits |
| `icons` | Exports the item icons the in-game store pages show from your Landmark folder into `server-data/gamedata/icons`, then exits |
| `db` | MariaDB: accounts, characters, and each world's own database |
| `auth` | The login server and the account service LaunchPad talks to (it also serves the 0.695 update) |
| `web` | The account sign-up website |
| `world` | The world server (a "server" in the game's list) |
| `social`, `commerce`, `envserver` | Optional: friends / guilds / chat / mail, Player Studio / contests, and the terrain store, each in its own container and shared by all your worlds (`COMPOSE_PROFILES` in `.env`) |

A world can also run its gateway and world channel in their own containers, and more worlds can join the same
database. All of that is in [docs/docker.md](docs/docker.md).

### 1. What you need

- **[Docker](https://docs.docker.com/get-docker/)**: Docker Desktop on Windows or macOS, or Docker Engine with the
  compose plugin on Linux. About 10 GB free disk and 16 GB of RAM or more.
- **Landmark** from Steam on the PC running the servers, updated to 0.695 (LaunchPad does that, see above).
- This project: **Code → Download ZIP** (or `git clone`), unpacked anywhere, e.g. `C:\LandmarkEmu`.
- Recommended: **`LandmarkEmu-terrain-heights.zip`** from [Releases](../../releases), put in the project folder (next
  to `docker-compose.yml`), still zipped. It holds a ground-height grid for each landscape, which the server uses for
  trees, creature footing and new players' build sites. Without it those stay off.

### 2. Settings: one `.env` file

Copy **`.env.example`** to **`.env`** and open it in a text editor. Every setting is explained in the file. Fill in:

| Setting | |
|---|---|
| `GAME_DIR` | Your Landmark folder, the one with `Landmark64.exe` (forward slashes, e.g. `C:/Program Files (x86)/Steam/steamapps/common/Landmark`). It is mounted read-only: nothing in it is changed. |
| `DB_ROOT_PASSWORD`, `DB_PASSWORD` | Two long random passwords of your choice (letters and digits). Only the containers use them. |
| `PUBLIC_HOST` | The address other players use to reach this PC. Empty: only this PC can play. |
| `ADMIN_ACCOUNT` | `account:` and your account name (e.g. `account:Alice`) to be an admin on your world. |

### 3. Start it

**With `start.bat` (Windows, optional):** on its first run it asks for your Landmark folder, your account name and the
address, writes `.env` with random passwords, offers to open the firewall ports, runs `docker compose up` and tells you
when the world is up. Later: `start.bat`, `start.bat -Stop`, `start.bat -Logs`, `start.bat -Update` (after downloading a
new version), `start.bat -Setup` (ask the questions again). `start.ps1` is the same script for PowerShell.

**By hand**, in a terminal in the project folder:

```
docker compose up -d --build
```

The first run builds the servers and takes several minutes; the world is ready a minute or two after that.

| Command | |
|---|---|
| `docker compose up -d` | Start the servers, or apply changed settings |
| `docker compose down` | Stop them (all data is kept) |
| `docker compose logs -f world` | Watch the world server's console |
| `docker compose up -d --build` | Rebuild after downloading a new version of this project |

Accounts, characters and builds are kept in the database volume, and each world's files in `server-data/worlds/`.

### 4. Ports

| Setting | Default | What it is |
|---|---|---|
| `LOGIN_PORT` | 20042 (UDP) | The login server |
| `ACCOUNT_PORT` | 5000 (TCP) | Accounts, logins and the update (LaunchPad, the website) |
| `WORLD_PORT` | 19000 (UDP) | The game world |
| `WEB_PAGES_PORT` | 19001 (TCP) | The in-game Showcase, store and Player Studio pages |
| `WEBSITE_PORT` | 8080 (TCP) | The account sign-up website |
| `DB_PORT` | 3307 (TCP) | The database, for tools on this PC only (never open it to others) |

### 5. Playing with others

1. Put the address they reach this PC on in `.env` as `PUBLIC_HOST`: this PC's LAN address for players on your home
   network (`ipconfig` shows it as "IPv4 Address"), or for friends elsewhere a VPN address
   ([Tailscale](https://tailscale.com/), [ZeroTier](https://www.zerotier.com/)) or your public address with the ports
   forwarded on your router.
2. Run `docker compose up -d` again.
3. Open the ports in the firewall: UDP `LOGIN_PORT` and `WORLD_PORT`, TCP `ACCOUNT_PORT`, `WEB_PAGES_PORT` and
   `WEBSITE_PORT` (`start.bat` offers to do it).
4. Players add that address in LaunchPad.

### 6. Admins and the update

- Admins type **`/admin`** in chat for the admin page: god mode, revive, coins and items, travel, time of day,
  creatures, broadcasts, kicks, a shutdown countdown and any developer command. Set them in `.env` (`ADMIN_ACCOUNT`,
  `ADMIN_2`, `ADMIN_3`, `ADMINS`), see [docs/admin.md](docs/admin.md).
- To offer players the 0.695 update, put the update pack (`.lmpatch`) in `docker/launcher/patches`. The server checks
  it with the same rules every LaunchPad uses before it serves it (refused packs go to `rejected/` with the reason).
  A player who has a 0.695 copy can make the pack in LaunchPad (Settings → Patch packs → Create update pack).

### Troubleshooting

- **`docker compose` can't connect to Docker:** start Docker Desktop, wait until it says it is running, try again.
- **"Set GAME_DIR / DB_PASSWORD in .env":** `.env` is missing or that setting is empty.
- **"GAME_DIR in .env is not your Landmark folder":** `docker compose logs setup` shows it; fix `GAME_DIR`.
- **The world doesn't come up:** `docker compose logs world` shows its console; the error is usually near the end.
- **LaunchPad: "No answer from <address>":** the servers aren't running, the address is wrong, or TCP `ACCOUNT_PORT`
  is blocked.
- **Another PC gets stuck on the loading screen:** `PUBLIC_HOST` is empty or wrong (it must be the address *they* reach
  this PC on), or UDP `WORLD_PORT` is blocked. Run `docker compose up -d` after changing `.env`.
- **The in-game Showcase / store pages are empty:** TCP `WEB_PAGES_PORT` is blocked for that player.
- **Names missing in game:** the Landmark folder had no `Locale\en_us_data.dat`; check `GAME_DIR`.
- The world's console log (`console.log`, the five runs before it as `console.0.log` .. `console.4.log`) and its saved
  files are in `server-data/worlds/main/`.

---

## For developers

Needs the **.NET 10 SDK**. LaunchPad is a Windows program (WebView2); everything else also builds on Linux.

```
dotnet build LandmarkEmulator.sln
dotnet test LandmarkEmulator.sln
```

| Project | |
|---|---|
| `LandmarkEmulator.AuthServer` | Login server: login, server list, character list and creation, the login queue |
| `LandmarkEmulator.WebAPI` | The account service inside the auth server: SRP sign-up and login, `/info` for LaunchPad, the patch folder |
| `LandmarkEmulator.WorldServer` | The world (zone) server: every game system |
| `LandmarkEmulator.Gateway` | The client-facing gateway: SOE UDP session, RC4, gateway login, client build check |
| `LandmarkEmulator.GatewayServer` | The gateway in its own process, routing each client channel to its back end |
| `LandmarkEmulator.WorldChannel` | The world-channel service: trade invitations, duels, build-site likes |
| `LandmarkEmulator.SocialServer` | The shared social service: presence, friends, ignore lists, groups, guilds, chat, mail |
| `LandmarkEmulator.CommerceServer` | The shared commerce service: Player Studio, contests, portraits, Showcase following |
| `LandmarkEmulator.EnvServer` | The Environment Server in its own process (the terrain store of one world) |
| `LandmarkEmulator.EnvServer.Core` | The terrain store itself: edits, heals, ore veins, ownership, coarse copies, stamps |
| `LandmarkEmulator.Internal` | The messages the server processes exchange with each other |
| `LandmarkEmulator.Shared` | Code all servers share: networking, cryptography, configuration, game tables |
| `LandmarkEmulator.Database` | Database plumbing shared by the database projects |
| `LandmarkEmulator.Database.Auth` | Accounts, server list and login tickets (`lmemu_auth`) |
| `LandmarkEmulator.Database.Character` | Characters and world data (`lmemu_character`, `lmemu_world_<key>`) |
| `LandmarkEmulator.Database.Social` | The social service's database (`lmemu_social`) |
| `LandmarkEmulator.LaunchPad` | LaunchPad, the players' launcher |
| `LandmarkEmulator.PatchPacks` | The 0.695 update: pack format, the built-in file lists, checks and install (LaunchPad and the server) |
| `LandmarkEmulator.Parser` | Reads packet captures of the live game and decodes them with the server's models |
| `LandmarkEmulator.Tests` | Unit tests: packet layouts against live captures, game rules, patch pack security |

Other folders: `docker/` and `docker-compose.yml` (the containers), `data/` (small tables derived from the client),
`tools/` (the terrain height bake and the strong hash list), `docs/` (below) and, in the published repository,
`research/` (the reverse-engineering notes: each client message family with its reader and handler functions and
field layouts, the client's Lua bindings, classes and data sources, and the specs the server follows). The notes name
client functions by address (`FUN_14xxxxxxx` in `Landmark64.exe`, build 0.695.1.132647, older notes 0.589.1.128895);
no decompiled client code is included, so load your own copy in Ghidra to follow them.

### Documentation

| Document | What it covers |
|---|---|
| [docs/README.md](docs/README.md) | Index of the docs and their conventions |
| [docs/docker.md](docs/docker.md) | Running the servers in Docker: every setting, more worlds, the optional services, backups |
| [docs/launchpad.md](docs/launchpad.md) | LaunchPad, the 0.695 update and its checks, the server's patch folder |
| [docs/protocol.md](docs/protocol.md) | The network protocol and the layout of every packet family the server handles |
| [docs/gameplay.md](docs/gameplay.md) | How each server system works |
| [docs/terrain.md](docs/terrain.md) | The VoxelFarm terrain: formats, edits, heals, veins, stamps, the Environment Server |
| [docs/sendself.md](docs/sendself.md) | The login character record (SendSelfToClient) field by field |
| [docs/client-reference.md](docs/client-reference.md) | Where things are in the client, the client data the server reads, client logs |
| [docs/admin.md](docs/admin.md) | Admin players, the client's admin commands, broadcasts, kicks, the server monitor |
| [docs/commerce.md](docs/commerce.md) | Player Studio and in-game commerce (InGamePurchase 0x45) |
| [docs/showcase.md](docs/showcase.md) | The Showcase window: store, gallery, following, sign-in, web API, likes |
| [docs/membership.md](docs/membership.md) | Membership and the trial key |
| [docs/vendors.md](docs/vendors.md) | Build site vendors, the NPC merchant window, Point-of-Need purchases |
| [docs/instanced-games.md](docs/instanced-games.md) | Site games, the game editor and the Game Finder |
| [docs/story-tools.md](docs/story-tools.md) | Story tools: Links, Play Mode, the prop settings window |
| [docs/encounters.md](docs/encounters.md) | Encounters (EncounterBase 0x2C): duels and the death window |
| [docs/unknowns.md](docs/unknowns.md) | How the once-unknown packet fields were named, and what is still open |

---

## Credits and licence

Based on [kirmmin/LandmarkEmu](https://github.com/kirmmin/LandmarkEmu) and the
[PheebeUK](https://github.com/PheebeUK/LandmarkEmu) fork; extended for the 0.589 and 0.695 clients. Licensed under the
GNU Affero General Public License v3 (see [LICENSE](LICENSE)).

**Legal note.** LandmarkEmu is a fan project. It is not affiliated with or endorsed by Daybreak Game Company; Landmark
is a trademark of its owners. This repository contains no game files, no extracted assets and no decompiled code.
Everyone needs their own copy of the game.
