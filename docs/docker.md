# Running LandmarkEmu in Docker

Everything runs from the project's `docker-compose.yml`, with the settings in one file, `.env`:

```
copy .env.example .env        (then edit .env: GAME_DIR, the two passwords, PUBLIC_HOST)
docker compose up -d --build
```

`start.bat` (or `start.ps1`) is an optional Windows shortcut for the same thing: it fills in `.env` from a few
questions (random passwords included), offers to open the firewall ports, runs `docker compose up` for the project and
every extra world in `docker/worlds/`, and waits until the account service lists a world as up. Options: `-Stop`,
`-Update` (rebuild), `-Logs` (the world's console), `-Setup` (ask the questions again).

## The services

The project, matching how live Landmark was organised. The default start runs `setup`, `icons`, `db`, `auth`, `web` and one
`world`; that world then runs its gateway, social, commerce and terrain code in its own process. Live ran those as
separate services, and each can run in its own container here (opt-in, still being tested): the shared `social`,
`commerce` and `envserver` services of this project (compose profiles), and per world a gateway and a world channel
(below).

| Service | |
|---|---|
| `setup` | Copies the data tables the servers read from your Landmark folder (`GAME_DIR`, mounted read-only) into `server-data/gamedata`, plus `data/` and the terrain heights if `LandmarkEmu-terrain-heights.zip` is in the project folder (`docker/setup.sh`). Runs on every start, then exits; the servers wait for it. |
| `icons` | Exports the item icons of the world's web pages (Showcase store) from your Landmark folder (read-only) into `server-data/gamedata/icons`, with the world server's own exporter (`--export-icons`, docs/showcase.md "Icons"). The first start takes about 2 minutes; later starts keep what is there and return at once while the game files are unchanged. Nothing waits for it, and a failed export only leaves the pages without icons (`docker compose logs icons`). |
| `db` | MariaDB: the accounts and server list (`lmemu_auth`), the shared characters (`lmemu_character`), and each world's own database (`lmemu_world_<key>`) |
| `auth` | The login server and the account service LaunchPad and the website talk to |
| `web` | The account sign-up website (nginx; see below) |
| `social` | Optional (compose profile `social`): presence, friends, ignore lists, guilds, chat routing and mail shared by every world; see "Social service" below |
| `commerce` | Optional (compose profile `commerce`): Player Studio, contests and portraits shared by every world; see "Commerce service" below |
| `envserver` | Optional (compose profile `envserver`): the first world's terrain store (Environment Server) in its own container; see "Environment Server" below |
| `world` | The first world: a "server" in the game's list, such as Serenity. Like on live, every world has the whole set of landscapes, and its build sites, props, chests, vendors, prop links and terrain are its own. Characters are shared, so one character can play on every world. |

The service definitions are in `docker/auth`, `docker/world`, `docker/social`, `docker/commerce` and
`docker/envserver` (one `docker-compose.yml` each); the project file puts them together (`extends`). `docker/gateway`
and `docker/worldchannel` are run per world beside it (below). Without a `Database:Zone` setting a world server keeps
everything in the character database (the older way of running it directly on Windows).

## Settings (.env)

`.env.example` explains each one. Gameplay settings default to how live Landmark behaved wherever a source says so
(the note names it), and each is a toggle for server owners. In short:

| Settings | |
|---|---|
| `PUBLIC_HOST`, `LOGIN_PORT`, `ACCOUNT_PORT`, `WORLD_PORT`, `WEB_PAGES_PORT`, `WEBSITE_PORT`, `DB_PORT` | The network: the address players reach this PC on, and every port (the same number inside the containers, except `ACCOUNT_PORT`, which maps to the auth container's 5000) |
| `GAME_DIR` | Your Landmark folder |
| `GAME_DATA_DIR` | The server's copy of the client data tables (mounted read-only as `/gamedata`). It must be the same build as the players' client: the client's `Resources\*.txt` tables plus `Locale\en_us_data.dat`, with the server's own files (terrain heights, item dumps). For 0.695 clients use the 0.695 tables: 57 of them changed since 0.589 (Parlor prices, props, recipes, game rules, text). |
| `DB_ROOT_PASSWORD`, `DB_PASSWORD` | The database passwords, used only between the containers. Set them before the first start: the database keeps the ones it was created with. |
| `WORLD_KEY`, `WORLD_ID`, `WORLD_NAME_ID`, `ADMIN_ACCOUNT`, `ADMIN_2`, `ADMIN_3`, `ADMINS` | The first world: its database and folder name, server-list id and name (default 560916 Serenity), and admins (`account:<name>` or `character:<name>`, docs/admin.md; `ADMINS`: any number more, comma separated) |
| `CLIENT_WORLD_NAME_TEXT`, `CLIENT_WORLD_ID` | The world's name and id in game. `0` (default) = as live: the name follows `WORLD_NAME_ID` when that is a live world name (560911-560926), else Liberation; the id is 2. Only worlds sharing one gateway need their own id |
| `MAX_PLAYERS`, `LANDSCAPE_CAPACITY` | Players per world (more wait in the login queue, members first) and per landscape (more wait in the travel queue); 0 = no limit (live had both queues, their numbers are not known) |
| `MOTD_SUBJECT`, `MOTD_BODY` | Message of the day, once per login (both empty = none) |
| `RESTART_SCHEDULE`, `RESTART_TIME_ZONE`, `RESTART_WARNING_SECONDS`, `RESTART_MESSAGE`, `WATCHDOG_SECONDS` | Scheduled restarts (empty = none; live: `Mon,Sat 03:00` Pacific), the warnings' lead time and text, and the hung-world restart (180 s; 0 = never) |
| `CLIENT_TIMEOUT_SECONDS` | A player whose game client sends nothing for this long (a crash, a killed process, Alt+F4, power or network loss) is disconnected and removed from the world at once, so the others stop seeing them (`60` = two of the 0.695 client's 29 s keep-alives; below 30 counts as 30; `0` = never, only the 120 s unacknowledged-data rule). Read by the world and the gateway container alike; a new login of the same character drops the old session at once whatever this says |
| `SHUTDOWN_BROADCASTS`, `PENDING_KICK_PACKET` | Shutdown / restart warnings also as a System Broadcast (false = live); the client's kick-warning packet before an admin kick and in a shutdown's last 30 s (true) (docs/admin.md) |
| `TARGET_NOT_ONLINE_PACKET` | "[player] is not online at this." (offline friend for Teleport to Friend, a trade / inspect target that left) as the client's packet 0x1A (`true`; its line names the reader, a client quirk) or a system line with the target's name (`false`, default) |
| `CRAFTING_WINDOW_CLOSE_AWAY` | A crafting / repair station's window closes when its player walks more than 8 m away or the station is gone (`true`, default; the client never closes it by distance itself) |
| `FREE_BUILD`, `HOME_CONTINENT`, `TERRAIN_LOD_COPIES`, `KEEP_VERTEX_ONLY_VOXELS` | Build anywhere without a site (`false` = live); the home continent (406), LOD copies of edits and vertex-only voxels (`true`). The last three are read by the world and the Environment Server alike, so the two always agree; with `ENVSERVER_ADDRESS` set the service's values are the ones that count |
| `MOVEMENT_EFFECTS` | Shift's sprint and the boot abilities (double jump, leaps, glide, Neon Blitzing charge) carry the movement effects the client runs; sprint, Immediate Delivery and the Breeze glide last while their key is held, and the boots cost Energy and have cooldowns (2014 developer values) (`true` = live; without them those keys do nothing) (docs/gameplay.md "Abilities") |
| `SAFE_EJECT_RESET` | A launch by a flinger, launcher or thrust pad (the client's SafeEject report) ends when the player lands: the server clears the client's "launched" state bit 26 so the next launch works the same (`true` = live); `false` only logs it (docs/gameplay.md "Teleports") |
| `DEAD_BODIES_VISIBLE` | A dead player's body lies down on the other clients: the server sets the character's "knocked out" state (entity state bit 7) on their copies at the death and for newcomers, and clears it when the player is alive again (`true` = live; the client has no other death pose for others); `false` sends nothing (docs/observer-sync.md) |
| `BLOCK_STANCE_VISIBLE` | A blocking stance (Molten Rebuke, Corrupting Revenge, a creature's shield) is shown to the other clients so their melee sweeps stop on the blocker with the "blocked" effect (`true` = live); the server decides the damage either way (docs/gameplay.md "Weapon combat") |
| `MELEE_TARGETERS` | Melee swings run as the client's Sweep: the client reports whom its swing passes through and the server checks each against its own cone (`true` = live); `false` = the server alone decides every swing (docs/gameplay.md "Weapon combat") |
| `PROP_STREAM_RANGE` | Placed props are sent to each player only within this many metres (64 m squares, nearest first, at zone-in and as they move) and taken away 80 m further out; default `400` (the client's Render Distance slider at its last bounded step); `0` = every placed prop of the landscape at every zone-in, as before 2026-10-18 (docs/gameplay.md "Props and crafting") |
| `STREAM_HOLD_UNTIL_LOADED`, `STREAM_HOLD_LIMIT_SECONDS` | While a player's loading screen is up (login or re-zone), creatures, trees / gathering nodes, chests, outposts, warp crystals, the Spire's non-deck props and the placed props away from the arrival wait until the client reports the screen down, at most `90` s (10-600); `true` by default. Works around the 0.695 client's 64 allocator slots on 16+ thread CPUs ("Avg contour" loading crawl); only the order changes. `false` = everything at zone-in, as before 2026-10-21 (docs/terrain.md "Allocator slots") |
| `LIVE_CHARACTER_STATS` | The character stats live sent: the player's movement tuning at login and other players' acceleration times (`true` = live) |
| `PLAYER_TELEPORT_REQUESTS` | The Escape menu's Evac to Safety moves players (onto the ground where they stand, or just off the build site they stand on; `true` = live); `false` answers players' requests in place (admins' teleports still work) (docs/gameplay.md "Teleports") |
| `ITEM_DEFINITION_CHUNK_SIZE` | Item definitions per packet when a player enters the world (default `100`; `0` = the whole ~2,960-item list in one packet, as the live dump). The client takes each packet in one go on its main thread, so one big packet froze its window ("Not Responding" at 16 %); the client ends up with the same items either way (docs/protocol.md §5) |
| `SHORT_TELEPORT_DISTANCE` | Same-landscape teleports up to this many metres move the player without a loading screen; `60` (default, since 2026-10-18: the hangs came from a zero facing vector, now fixed) stays inside the client's 80 m terrain window; `0` = every teleport re-zones (docs/terrain.md "Long teleports"). `SHORT_TELEPORT_WAIT_SCREEN=true` shows the client's brief "Waiting for Teleport..." screen on those moves (`false` while measured). Translocation Pad hops always move in place |
| `ADMIN_MONITOR_PUSH`, `ENVSERVER_STARTUP_WAIT_SECONDS` | Push the instanced-games admin monitor page to admins while a site game runs (`false` = only when asked), and how long start-up waits for the Environment Server (`120` s) |
| `DEV_CLIENT_CODE_ON_OTHERS` | The dev commands that run code or raw packets inside a game client (`lua`, `luacode`, `luas`, `send`; the admin page and `dev_commands.txt`) reach only the admin who ran them and other admins, and may not call the game's functions that open the player's desktop web browser (`false`, default). `true` lifts both checks: an admin can then run code in any player's game client, which can open web pages on that player's PC; use it only on a test server whose players agreed. Every use is logged with caller and target. |
| `MOVE_OLD_PIONEER_SITES` | `true` (default): build sites still on the old Pioneer lands 417-421 move at start-up to the replacement land of the same biome (464-468), a Design of the old build saved; `false` leaves them, unreachable (docs/gameplay.md "Pioneer lands") |
| `PIONEER_SLAB`, `PIONEER_SLAB_MATERIAL` | The slate slab under each new Pioneer site (live, 30 Jun 2016 notes): `claim3x3` (default; the client's 210 m plus-shaped 2 m slab template), `claim1x1` (its 70 m middle) or `off`; its material as a VoxelMaterials ARRAY_INDEX (`26` = Worked Slate; `0` = the template's stored 53) (docs/gameplay.md "Pioneer lands") |
| `EVENT_ISLAND`, `EVENT_ISLAND_EXPIRE_ON_CLOSE` | The island on the teleport window's Events world (`auto` = live's 2016 dates; `halloween`, `winter`, `judgement`, `off`), and whether the sites left on an island that closed expire at the next start (`true` = live; it deletes builds, so back up first) (docs/terrain.md "The Events world") |
| `LANDMARK_SEASON`, `HAT_DROPS` | Seasonal outfit drops (`auto` = live's 2016 calendar; `halloween`, `winter`, `off`) and the Ninja Mask / Samurai Helmet unlockers (`season` = live, only in the Halloween season; `always`, `off`) (docs/gameplay.md "Rare and seasonal drops") |
| `SPIRE_AREA` | The 90 m area round each Spire: no build sites, mining, harvesting or trees in it, as live (`true`); `false` = only the platform (docs/gameplay.md "Claims / build sites") |
| `SURFACE_GRAVEYARDS`, `CAVERN_GRAVEYARD_STAMPS` | Surface graveyards (the map's red circles; their spots are ours) and the Chaos Caverns' graveyards stamped into the terrain once per landscape (both `true`) (docs/gameplay.md "Graveyards") |
| `CAVERN_ARRIVAL_RESCUE` | A player who falls through the world in the first 30 s after an Underground Adventure is put back on the graveyard, the second time on the landscape's shallowest one (`true`; ours) (docs/gameplay.md "Underground Adventure") |
| `GRIEF_AWARDS`, `GRIEF_DAMAGE_PER_POINT`, `GRIEF_LEVEL_POINTS`, `GRIEF_DECAY_PER_HOUR`, `GRIEF_SUSPENSION_MINUTES` | Friendly fire in team site games earns grief (`true` = live; `false` = teammates can't hurt each other) and its numbers, which are ours (100 damage a point; levels at 10,20,30,40,60; 30 points an hour wear off; 60 minutes' suspension) (docs/gameplay.md "Grief") |
| `MEMBERSHIP_PRICE_LUMENS`, `MEMBERSHIP_DAYS`, `TRIAL_KEYS`, `TRIAL_KEY_DAYS` | Membership for Lumens (1950 for 30 days; 0 = not for sale) and the trial key every account is mailed once (`true`, 10 days) (docs/membership.md) |
| `VENDOR_SHOPS` | Player shops on the Vendor Booth (`false` = live: decorative since 1 Apr 2016) (docs/vendors.md) |
| `BUILD_MENU_PROP_NAME` | The prop's name over it while its build-mode right-click menu is open, for that player only (`true`; `false` = no label) (docs/gameplay.md "Right-click menus") |
| `UPKEEP_TIME_ZONE` | The time zone whose calendar days count as build site upkeep login days (`America/Los_Angeles` = live, developers 17 Mar 2015; `UTC` = days by UTC) |
| `CHEST_ACCOUNT_CAP` | Storage chests one account may have placed over all its sites (`0` = none, as live: only 50 per build site; 50 = the wiki's "per account") |
| `TRAVEL_COOLDOWN_SECONDS` | Seconds between two Travel menu trips to the Spire, and between two to the own build site (`0` = live 2016: the Stone of Recall these replaced lost its cooldown on 5 May 2015; 1800 / 3600 = the 2014 alpha's); the client refuses a repeat itself (docs/gameplay.md "Travel") |
| `FRIEND_TELEPORT_COOLDOWN_SECONDS` | Seconds between two Teleport to Friend trips (`0` = live since 5 May 2015; 1800 = the 2014 alpha's 30 minutes); the client refuses a repeat itself with its own cooldown text (docs/gameplay.md "Travel") |
| `FRIEND_REQUESTS_MUTUAL` | An accepted friend request puts each player on the other's friends list (`true` = live); `false` = one-way lists (only the asker gets a friend). Read by the world and by the social service (docs/gameplay.md, Social) |
| `STORY_POSE_PERSISTS` | Story creatures keep the mood / stance a Story Node gave them, saved with the spawner (`true` = live from 7 Jun 2016) (docs/story-tools.md) |
| `DESIGNS_FOLLOW_ACCOUNT` | The designs of a deleted character (and of its build sites) go to the account's next character at its world entry (`true` = live: Wipe Q&A 14 Mar 2016, designs are tied to the account) |
| `PLAYER_STUDIO_DESIGNS_FREE_STORAGE` | Designs bought from Player Studio, and a creator's designs on it, take no design storage (`true` = live: wiki Designs, developers' hotfix notes 17 Oct 2014) |
| `PLAYER_STUDIO_REVIEWER_MIN_SPEND`, `PLAYER_STUDIO_REVIEWS_PER_DAY`, `PLAYER_STUDIO_STAFF_REVIEW` | Player Studio peer review as live: reviewers spent 1000 Daybreak Cash (× `LUMENS_PER_DBC` in Lumens) on the store and designs (0 = anyone), 2 reviews a day (0 = no limit), peer-rejected designs wait for staff on /admin (`true`) (docs/commerce.md) |
| `PLACE_OPENS_BUILD_MODE` | Placing a prop from the bag opens Build Mode on its Props page; cancel before first Save closes it, a successful place leaves it open (`true` = this server's default, asked for by the user; `false` = live, which never opened it) (docs/gameplay.md "Props") |
| `YELL_IS_EMOTE` | `/yell` plays the yell emote ("You yell loudly.", `true` = live: the client's own emote word and lines, the 2016 command list); `false` = `/yell` is the client's shout alias (docs/protocol.md "Emotes") |
| `EMOTES_PUT_AWAY_HELD_ITEM` | Emotes, stances, /playdead and /sit put the held tool or weapon away and bring it back when the player walks off or types /stand (`false` = live, the default: emotes show only with empty hands; `true` = put away for the player, after the game's put-away clip) (docs/protocol.md "Emotes") |
| `DAY_CYCLE_SPEED` | Day/night clock speed, game seconds per real second (`8` = live: a day lasts 3 real hours); the clock follows real UTC time, so it carries on across restarts (docs/gameplay.md "World") |
| `SHOWCASE_FOLLOW_DIGEST`, `SHOWCASE_DIGEST_HOUR_UTC` | The Showcase Following tab's daily mail (`true` = live) and its hour (UTC, default 10); read by whichever side runs the digest, the world or the commerce service (docs/showcase.md "Following and the daily mail") |
| `FINDER_RETURN_HOME`, `CAVERN_TRIPS_OTHER_WORLDS` | With several worlds on one gateway: bring a player the Game Finder took to another world back afterwards (`false`), and let Chaos Cavern trips land on other worlds (`true` = live) |
| `PROCEDURAL_PLACEMENT`, `PROCEDURAL_PLACEMENT_DECOR`, `PROCEDURAL_PLACEMENT_MASKS` | Trees from the client's placement sets (`false` by default: the sets are alpha-era, live's launch placement is not in the client), their rocks / bushes (`false`), their masks (`true`) (docs/gameplay.md) |
| `STORE_PRICES` | Owner store prices in Lumens, `item id=Lumens` pairs (e.g. `900001=500,2551=1300`; 0 = free); empty = live's prices only (docs/showcase.md) |
| `MAIL_MAX_STACK` | Most of one item a mail attachment may carry (`9000` = live, wiki Trading; `0` = no cap) |
| `LUMEN_TRADING` | Lumens may be offered in trades and sent or asked for (cash on delivery) by mail (`false` = live: Lumens could not be traded, Wipe Q&A 14 Mar 2016) |
| `ITEM_AUTO_RESUPPLY` | Items keep their auto-resupply switch (Inventory 0x2D sub 0x0D, saved per item and sent back in the item record; `true` = live; `false` = ignored, always off) (docs/protocol.md "InventoryBase 0x2D") |
| `OVERFLOW_EXPIRY_HOURS` | Hours items wait in the overflow tab before they are deleted (`24` = live, wiki Inventory; the client shows the countdown; `0` = kept) |
| `STORE_TRADE_WAIT_HOURS` | Hours a Lumen store purchase can't be traded, mailed or put in a chest (live's "fraud timer"; default 12, the most a developer post of 26 Mar 2014 gives; the item tooltip counts it down; `0` = off) |
| `MEMBER_MONTHLY_LUMENS`, `MEMBER_STORE_DISCOUNT` | Member perks beyond queue priority: `0` = live (none); e.g. 650 / 10 (docs/membership.md) |
| `STRICT_NAMES`, `CHARACTER_SELECT_FRIENDS` | RPG-style character names only (`false` by default); the character list carries each character's friends list, as live (`true`; who is online comes from the social service when `SOCIAL_ADDRESS` is set) |
| `CREATURE_PATHING`, `SITE_PATHING`, `CREATURE_WEAPONS` | Creatures walk around cave rock and ruin walls (`true`), story creatures around player-built walls on build sites (`true`), and creatures hold a weapon in their hand (`true`: a best guess, the weapon type from each creature's icon, the model ours) (see "Everyday commands" below) |
| `GAME_DATA_DIR`, `WORLD_DATA_DIR`, `ENVSERVER_DATA_DIR`, `LAUNCHER_DIR` | Advanced: other folders for the game data copy, a world's saved files, the Environment Server's files and LaunchPad's news file |
| `NETWORK_NAME`, `WEB_SUBNET_PREFIX` | Advanced: the Docker network's name and the website network's address range, if they clash with others on the PC |
| `WORLD_IMAGE`, `AUTH_IMAGE`, `SOCIAL_IMAGE`, `GATEWAY_IMAGE`, `WORLDCHANNEL_IMAGE`, `COMMERCE_IMAGE`, `ENVSERVER_IMAGE` | Advanced (not in `.env.example`): another image tag for that service's build, so a second build can stand beside the running one |
| `SERVER_NAME`, `WEBSITE_URL`, `REQUIRED_CLIENT_BUILD` | What LaunchPad shows (docs/launchpad.md). News: `docker/launcher/news.json`. |
| `PATCH_DIR`, `PATCH_VERIFY_STEAM_DIR` | The patch folder players' LaunchPad downloads the 0.695 update from (default `docker/launcher/patches`, mounted at `/launcher/patches`; drop the `.lmpatch` in it). Every pack is checked with LaunchPad's own rules before it is served; refused packs go to `rejected/` with a `.reason.txt` and the reason in `docker compose logs auth`. Optional `PATCH_VERIFY_STEAM_DIR`: an unchanged Steam 0.589 copy (read-only; not your `GAME_DIR` once that is updated to 0.695) so the check also replays every edit and patch (docs/launchpad.md "Server side") |
| `REGISTRATION_OPEN`, `REGISTRATIONS_PER_ADDRESS_PER_HOUR`, `REGISTRATIONS_PER_HOUR` | Sign-ups |
| `COMPOSE_PROFILES`, `SOCIAL_ADDRESS`, `SOCIAL_IMPORT_FROM`, `SOCIAL_PRESENCE_GRACE_SECONDS` | The optional social service (below) |
| `GATEWAY_HOLD_SECONDS`, `GATEWAY_MULTI_WORLD`, `LOGIN_INTERNAL_ADDRESS`, `WC_TRADE_BROKER`, `WC_ENCOUNTER_BROKER`, `WC_CLAIM_RATINGS`, `WC_IMPORT_FROM` | The optional gateway and world-channel containers (below); `GATEWAY_MULTI_WORLD=auto` (default) turns travel between worlds on when a second world uses the gateway |
| `COMMERCE_ADDRESS`, `COMMERCE_IMPORT_FROM`, `COMMERCE_IMPORT_WORLD`, `COMMERCE_IMPORT_REALM`, `PLAYER_STUDIO_APPROVALS`, `PLAYER_STUDIO_REJECTIONS` | The optional commerce service (below) |
| `ENVSERVER_ADDRESS`, `ENVSERVER_IMPORT_FROM`, `ENVSERVER_HEAL_QUEUE`, `ENVSERVER_RUIN_LAYERS`, `ENVSERVER_RUIN_LAYER_DESIRED` | The optional Environment Server (below) |
| `PLAYER_STUDIO_LISTING_FEE` | Player Studio listing fee on submitting a design (default `true` = live's 10 % of the price, at least 100 DBC, in Lumens at `LUMENS_PER_DBC`: 13 %, at least 130 Lumens); `false` = none; never with `FREE_STORE`. World setting (the zone charges it in both commerce modes) |
| `PLAYER_STUDIO_CREATOR_SHARE` | Percent of a Player Studio design's Lumen price mailed to its creator (default 40: live's 60/40 split). Used by the world (in-process commerce) and by the commerce service; the price itself is the creator's × `LUMENS_PER_DBC`, free with `FREE_STORE` |

After changing `.env`, run `docker compose up -d` again: only the containers whose settings changed are recreated.

Most world settings can also be changed in game on the admin **Settings page** (`/admin` → Server settings). Those
changes are kept in the world's data folder, `server-data/worlds/<WORLD_KEY>/settings-overrides.json` (the world's
`SettingsOverridesFile`, `/data/settings-overrides.json` in the container), and **win over `.env`**: code default <
WorldServer.json < `.env` < the page. The page shows where each value comes from; its "Reset to .env" removes an
override; deleting the file (world stopped) removes them all. Network, ports, ids, folders, passwords and the admin
list stay `.env`-only (docs/admin.md "Settings page").

**Tunable content** (loot / drop tables, AGENTS #22) lives beside that file under
`server-data/worlds/<WORLD_KEY>/content/` → `/data/content/` (same world volume). Baked defaults ship in the image as
`Resources/Content/` (see `Resources/Content/README.md`). On first start the world seeds empty `content/loot/*.json`
from those defaults without overwriting host edits. Edit on the host, then `loot reload` or `content reload` in a
dev/admin console — no world restart. A bad file keeps the previous good table.

## More worlds

1. Copy `docker/worlds/extra-world.env.example` to e.g. `docker/worlds/liberation.env` and set:
   - `WORLD_KEY=liberation`, which gives the database `lmemu_world_liberation` and the folder `server-data/worlds/liberation`.
   - `WORLD_ID=2`, unique and never reused.
   - `WORLD_NAME_ID=560912`, which shows as "Liberation" in the server list and, as on live, in game (the client's server
     names are listed in `.env.example`).
   - `WORLD_PORT=19010` and `WEB_PAGES_PORT=19011`: free ports of its own.
2. With the project running, start it:
   ```
   docker compose --env-file .env --env-file docker/worlds/liberation.env -p landmarkemu-world-liberation -f docker/world/docker-compose.yml up -d
   ```
   It takes the address, passwords and game folder from `.env`. `start.bat` starts and stops every world in
   `docker/worlds` along with the project. Open the new ports in the firewall too.

On start a world writes its own entry in the server list: id, name and `PUBLIC_HOST:WORLD_PORT`. The auth server picks it up within a few seconds; players see it after logging in again.

## Social service (optional, being built)

The shared social service (`LandmarkEmulator.SocialServer`, compose service `social`, `docker/social/docker-compose.yml`)
holds who is online, friends lists, ignore lists, guilds, chat routing and mail for every world at once, as on live
Landmark: a friend on another world shows as online, adding or ignoring someone works across worlds, changing world
shows no offline / online line to friends, a guild's roster and chat span the worlds, tells and chat rooms reach
players on other worlds, and a mail lands in the same inbox whichever world its recipient plays on
(research/trace/architecture/social-service.md §6). Groups span the worlds too (§10): invite, join, kick, leader and party
chat work between players on different worlds, a group survives a world change, and each world keeps what needs
positions (party-frame distances, the cave group trip, shared loot) to the members on that world. Say, shout and emote
stay in each world (they are by range). Mail is split as live had it: the service keeps the mailboxes, the world keeps the bag side (it checks and
holds a compose's items and coins until the service has stored it, and hands attachments over when they are taken).

It is off unless both are set in `.env`:

```
COMPOSE_PROFILES=social
SOCIAL_ADDRESS=social:20200
```

`COMPOSE_PROFILES` starts the container; `SOCIAL_ADDRESS` makes every world (also extra worlds, which read the same
`.env`) use it. With `SOCIAL_ADDRESS` empty a world keeps its own friends, ignore lists, guilds and mail exactly as
before. The login server uses `SOCIAL_ADDRESS` too: the character list carries each character's friends list
(`CHARACTER_SELECT_FRIENDS`, default true) with who is online on which world, asked of the service; without it the
lists come from the character database with every friend offline. The
service listens on port 20200 inside the Docker network only; nothing is published.

**Its database** is `lmemu_social` on the same MariaDB. A database created before this service needs one grant (new
installs get it from `docker/db-init`):

```
docker compose exec db mariadb -uroot -p<DB_ROOT_PASSWORD> -e "GRANT ALL PRIVILEGES ON lmemu_social.* TO 'landmarkemu'@'%';"
```

**One-time copy of the existing data** (`Database.Social/SocialImport.cs`). On its first start the service creates its
tables, then copies from `lmemu_character` (or the database named by `SOCIAL_IMPORT_FROM`; set it empty to skip):
character names, accounts, last world / landscape / online time → `social_character`; `character_friend` → `friend`;
`character_ignore` → `ignore_entry`; `guild` / `guild_member` → `social_guild` / `social_guild_member`; `mail` →
`social_mail` (same ids) with each mail's attachments split into `social_mail_attachment` rows. Each part runs only
while its own tables are empty (the characters / friends / ignore part while all three are empty), in one
transaction, reads the old tables without changing them, and logs the row counts of both sides
(`docker compose logs social`). **Back up first** (`mariadb-dump --single-transaction --databases lmemu_character`).
To copy a part again, empty its tables and restart `social`. While worlds use the service they no longer change the
old friend, ignore, guild or mail tables; those stay as they are. Characters made after the copy are added when they enter a world or are named in a friend / ignore
request.

**When it is down**, worlds keep running: friend, ignore, guild and room requests and routed chat lines are dropped
(logged), ignore checks use the lists the world already had, a mail being written is refused with the client's own
"Mail service is unavailable" (nothing leaves the bag), and mail the game sends (vendor sales, site upkeep) waits in
the world until the service is back. Then each world sends everyone it holds and the players get their lists again. Friends see someone offline 30 s after they leave (`SOCIAL_PRESENCE_GRACE_SECONDS`).

## Gateway in its own container (optional, being built)

A world's gateway — the client-facing UDP port with the SOE session, encryption, the gateway login and the flood
limits (packets pass unchanged: protocol.md §1.6) — can run as its own container (`LandmarkEmulator.GatewayServer`,
`docker/gateway/docker-compose.yml`), as live Landmark split it (research/trace/architecture/gateway-service.md). The
world then opens no UDP port: it dials the gateway on the internal network (`gateway-<WORLD_KEY>:20300`, never
published) and serves the clients the gateway attaches. Start both for one world, here an extra world with its own
file `docker/worlds/liberation.env` (see "More worlds"):

```
docker compose --env-file .env --env-file docker/worlds/liberation.env -p landmarkemu-gateway-liberation -f docker/gateway/docker-compose.yml up -d --build
docker compose --env-file .env --env-file docker/worlds/liberation.env -p landmarkemu-world-liberation -f docker/world/docker-compose.yml -f docker/gateway/world.override.yml up -d
```

Players notice nothing (same address and port). If the world stops without shutting down (a crash, `docker kill`) the
gateway keeps its players connected for up to `GATEWAY_HOLD_SECONDS` (120): when the world is back they get a short
loading screen and carry on where they stand; after that they are sent back to character select. The gateway checks
logins through the login service (the auth container's internal port 20210), so it needs no database access. A normal world shutdown or restart
still logs its players out first. Restarting the gateway disconnects its players; the world saves them at once. A
client that dies (crash, killed process, lost network) is noticed by the gateway, not the world: no packet for
`CLIENT_TIMEOUT_SECONDS` (60) closes the connection and the world removes the character at once. Silence from the world
side (the hold above) is a different thing: the client keeps sending to the gateway while it waits, so held players are
never timed out by it.
Without the gateway container the world runs the gateway itself, as before.

Routing is filled only from registrations (2026-10-16, research/trace/architecture/gateway-service.md §3.3): every back end that dials the gateway
says which world it serves (`WORLD_ID`, the gateway's `WorldId`), which landscapes (none = all) and which client
channels it takes. Channel 0 goes to the zone process holding the player's landscape; channels 1-3 to a service
registered for that channel (today the world channel takes channel 1), else to the zone. When a back end goes away the
gateway tells the affected clients `ChannelIsRoutable 0` for the channels left without one (they only log it, in
`Logs/NetInfo.log`) and holds their packets; `ChannelIsRoutable 1` follows when one registers again. A player who
logs in while no zone serves their landscape gets `ConnectionIsNotRoutable` and waits (up to `GATEWAY_HOLD_SECONDS`)
for one to register.

### Travel between worlds (one gateway for several worlds)

Live players picked another world in the teleport window and simply arrived there after a loading screen: the 0.695
client never reconnects to change world, it re-zones on the same connection with the new world id
(research/trace/0695/OPEN-LEADS.md L114). To get that, run one gateway for several worlds:

- the gateway (`GATEWAY_MULTI_WORLD=auto`, the default, turns travel between worlds on as soon as a second world's zone
  registers; `true` forces it on, `false` refuses other worlds; its own `WORLD_ID` is the world players log in to);
- every world behind it dialing that same gateway: an override file of your own beside `docker/gateway/world.override.yml`
  that sets the world's `GatewayInternalAddress` to `gateway-<that gateway's WORLD_KEY>:20300` (the shipped override
  uses the world's own key), each world with its own `WORLD_ID`, `WORLD_KEY` and data folder;
- each world with its own `CLIENT_WORLD_ID` (the id the game client is told; never 3, which is the Events world). Its
  name in game follows its `WORLD_NAME_ID` when that is a live world name (as on live); `CLIENT_WORLD_NAME_TEXT` (a
  client text id, e.g. 560911 Courage) sets another.

The gateway tells every world which others are there; their teleport windows list them with the same landscapes. When a
player picks a landscape of another world, their world saves them (the next login goes there too), lets go of them,
and the gateway hands the connection to the other world, which loads them and brings them in at that landscape's
arrival point (the Spire). Friends, guildmates and the group see no logout in between when the social service runs.
With the defaults (one world per gateway, `CLIENT_WORLD_ID` 2) nothing changes.

Chaos Cavern trips use the same hand-over (`CAVERN_TRIPS_OTHER_WORLDS`, true by default as live: the 7 Jun 2016 notes
let Lost Crystals send players to other worlds and biomes): a Lost Crystal's trip and Travel → An Underground Adventure treat this world
and every other world on the gateway as equally likely; on another world the player arrives in one of its caverns (see
gameplay.md, "Chaos Caverns on every landscape").

## World channel in its own container (optional, being built)

With the gateway in its own container, a world's channel 1 (the "World" channel: friends, guilds, trade invitations,
Player Studio and other world-wide requests) can have its own back end, `LandmarkEmulator.WorldChannel`
(`docker/worldchannel/docker-compose.yml`, research/trace/architecture/world-channel-service.md). It answers trade
invitations (`WC_TRADE_BROKER`), duels (`WC_ENCOUNTER_BROKER`, docs/encounters.md) and build-site likes
(`WC_CLAIM_RATINGS`, below) itself and hands every other channel-1 packet on to the world's zone process (its internal
port 20310, opened by `docker/gateway/world.override.yml`). Without it, or while it is down, the gateway sends channel
1 to the world directly.

```
docker compose --env-file .env --env-file docker/worlds/liberation.env -p landmarkemu-worldchannel-liberation -f docker/worldchannel/docker-compose.yml up -d --build
```

Build-site likes (2026-10-16, world-channel-service.md §8 step 6) are kept by the world channel in its own database,
`lmemu_wc_<WORLD_KEY>` (table `claim_vote`), which it creates on its first start; on that start it copies the likes
counted so far (`lmemu_character.character_unlock` kind 11; `WC_IMPORT_FROM`, empty = no copy), and on every later
start the likes the world counted itself while the world channel was down. An existing database volume needs the grant
once: `` GRANT ALL PRIVILEGES ON `lmemu\_wc\_%`.* TO 'landmarkemu'@'%'; `` (new volumes get it from `docker/db-init`).
`WC_CLAIM_RATINGS=false`, or no reachable database, leaves the likes to the world as before.

## Commerce service (optional, being built)

The shared commerce service (`LandmarkEmulator.CommerceServer`, compose service `commerce`,
`docker/commerce/docker-compose.yml`; research/trace/architecture/world-channel-service.md §8 step 8) holds what live kept
account-wide: **Player Studio** (designs for sale, peer review, the player's own products, the gallery listings of
build sites), **contests** (the Competition list, entries, votes, screenshots) and **portraits** — for every world at
once. A design submitted on one world can be reviewed and taken from the Showcase on another; contests have one list
for everyone. The Showcase web pages themselves stay on each world (they act on the player there: bag, travel,
vendors), and read their Player Studio and gallery data from the service. Details: docs/commerce.md "Where it runs".

It is off unless both are set in `.env`:

```
COMPOSE_PROFILES=commerce          (or social,commerce for both services)
COMMERCE_ADDRESS=commerce:20400
```

With `COMMERCE_ADDRESS` empty every world keeps its own Player Studio, contests and portraits exactly as before. The
service listens on port 20400 inside the Docker network only; its files (contest screenshots, portraits) are in the
volume `commerce-data`.

**Its database** is `lmemu_commerce` on the same MariaDB, created by the service. A database created before this
service needs one grant (new installs get it from `docker/db-init`):

```
docker compose exec db mariadb -uroot -p<DB_ROOT_PASSWORD> -e "GRANT ALL PRIVILEGES ON lmemu_commerce.* TO 'landmarkemu'@'%';"
```

**One-time copy of the existing data.** On its first start, while its product table is empty, the service copies from
`lmemu_character` (`COMMERCE_IMPORT_FROM`; empty = skip): `player_studio_product` → `product` (same ids, the owner's
account and name, and each design's snapshot from `template`), `player_studio_review` → `product_review`, the taken
designs (`character_unlock` kind 16) → `taken_design`, all as world `COMMERCE_IMPORT_WORLD` (default 1: the world the
in-process code ran on, whose build sites the gallery listings name). Each world then sends its own `Contests/` and
`Portraits/` files once, the first time it links (`commerce_import` records what was done). The sources are only read.
**Back up first** (`mariadb-dump --single-transaction --databases lmemu_character`, and the worlds' `Contests` /
`Portraits` folders). While worlds use the service they no longer change the old tables or files; switching a world
back copies nothing back.

**Showcase following** (the Following tab and the daily digest mail) moves to the service too: it keeps the follow lists
and runs the digest once a day per realm, reading the same `SHOWCASE_FOLLOW_DIGEST` / `SHOWCASE_DIGEST_HOUR_UTC` from
`.env` as the worlds do in-process; the mails go out through a world of the follower's realm (docs/commerce.md
"Following and the digest"). The follow lists a world kept in-process (`showcase_follow`) are not copied.

**When it is down**, worlds keep running: a Player Studio submit gets the client's own "unknown error" (nothing is
stored), the store's status request answers "down", a contest entry is refused, other Player Studio / contest / portrait
requests go unanswered (the client asks again), designs keep their Designs-window category and listed sites stay
likeable from the world's last copy, and the world reconnects by itself.

## Environment Server (optional, being built)

The Environment Server (`LandmarkEmulator.EnvServer`, compose service `envserver`, `docker/envserver/docker-compose.yml`;
research/trace/architecture/terrain-service.md §7) is one world's terrain store in its own container: build edits, the Heal tool,
undo and paste writes, digs and their heal queue, Voxel Vision ownership, ore veins, coarse (LOD) copies and the
Spire / ruin / Landmarks of Landmark stamps. The world still talks to the game client and keeps only a version index and
a cache of cell data; how the split works: docs/terrain.md "Where it runs".

It is off unless both are set in `.env`:

```
COMPOSE_PROFILES=envserver          (or social,commerce,envserver)
ENVSERVER_ADDRESS=envserver:20500
```

With `ENVSERVER_ADDRESS` empty the world keeps its terrain itself exactly as before. The service listens on port 20500
inside the Docker network only (aliases `envserver` and `envserver-<WORLD_KEY>`); the world waits up to two minutes for
it at start-up (`EnvServerStartupWaitSeconds`). Its health check reads a file its loop touches every 10 seconds.

**Its data** is `server-data/worlds/<WORLD_KEY>/envserver` (`ENVSERVER_DATA_DIR`): `TerrainEdits/` (cells, `Ownership/`,
`heal-queue.log`, `layers.v2`), `stamps.tsv`, `ruins.tsv` / `ruin-layers.tsv` and the marker `import.done`.

**Switching a world over (one-time copy).** Stop the world first (`docker compose stop world`), **back up** its
`server-data/worlds/<WORLD_KEY>/TerrainEdits` folder, set the two lines above, then `docker compose up -d --build`. On
its first start with an empty store the service copies the world's `TerrainEdits` folder (mounted read-only at
`/world-data`) into its own folder, unchanged (`ENVSERVER_IMPORT_FROM`; empty = start from nothing), and records the
world's placement markers in `stamps.tsv` / `ruins.tsv`; `import.done` keeps it to once. From then on the world's own
`TerrainEdits` folder holds only its placement markers. **Going back** to in-process terrain copies nothing back by
itself: stop both, copy `envserver/TerrainEdits/` over the world's `TerrainEdits/`, then clear `ENVSERVER_ADDRESS`.

Settings (`.env`, all optional): `ENVSERVER_HEAL_QUEUE=false` pauses heals (the queue keeps every entry and catches up
when switched on again), `ENVSERVER_RUIN_LAYERS=true` keeps the ruin-layer records shown on `/admin/monitor`
(`ENVSERVER_RUIN_LAYER_DESIRED`). The home continent, LOD copies and vertex-only voxels come from the same `.env`
lines as the world's (`HOME_CONTINENT` 406, `TERRAIN_LOD_COPIES`, `KEEP_VERTEX_ONLY_VOXELS`), so the two can't
disagree; with the service on, its values are the ones that count (it stores the edits and builds the copies). The
older `ENVSERVER_CONTINENT` is no longer read. A second world runs `docker/envserver/docker-compose.yml`
with its own world file (its `WORLD_KEY`) and sets `ENVSERVER_ADDRESS=envserver-<key>:20500`.

**When it is down**, the world keeps running and players stay connected: cells come from the world's cache or are
reported up to date (and fetched when it is back), building is refused with the client's "Connection to the Build Site
Server is unavailable" message, digs do nothing, heals and veins wait; the world reconnects by itself.

## Account website

The auth stack also runs a sign-up page (service `web`, nginx) on `WEBSITE_PORT`, default 8080. Players open `http://<server address>:8080`, choose a name and password, and then sign in with LaunchPad.

How it is kept safe:

- **The password never leaves the player's browser.** The page derives the SRP salt and verifier itself (`docker/web/site/srp.js`, the same maths as `Shared/Network/Cryptography/Srp.cs`, checked against .NET) and sends only those. The server stores only the verifier.
- **No database access.** The web container is only on the `landmarkemu-web` network, together with the auth server:
  - It can reach the auth server's account API and nothing else. It can't even resolve the database's or the worlds' names.
  - It has no passwords.
  - It runs read-only, as a non-root user.
  - The database port is published on 127.0.0.1 only.
- **One route.** Only `POST /api/register` is passed on (to `/srp/register`). Every other `/api/` path returns 404, and other methods return 403.
- **Spam limits, in layers:**
  - nginx: 5 sign-ups a minute per address (burst 3), 60 a minute in total, and 20 connections per address.
  - Auth server: `REGISTRATIONS_PER_ADDRESS_PER_HOUR` (default 3) and `REGISTRATIONS_PER_HOUR` in total (default 60). These limits apply to launcher sign-ups as well.
  - `REGISTRATION_OPEN=false` closes sign-ups.
  - The same rules apply on the server PC itself: the local `POST /create` route (user name and password in the request body, never in the address) checks the account name (3-32 letters, digits or underscores), the 6-character password minimum, `REGISTRATION_OPEN` and both limits. Only the login server console's `account create` (the owner's own tool) works while sign-ups are closed; it still checks the name and password rules.
  - The page also has a hidden field that bots fill in, and a 2-second minimum before submitting.
- **The real player address.** nginx replaces `X-Forwarded-For` with the address it sees, so a client can't fake one. The auth server trusts that header only from the website's fixed address, 172.30.77.10 (`WebApi:TrustedProxy`).
- **Security headers:** a strict Content-Security-Policy, no framing, nosniff and no-referrer.

For a public server, put HTTPS in front of port 8080, for example with Caddy or a Cloudflare tunnel. The password is never sent even over plain HTTP, but HTTPS stops anyone on the way from changing the page. Behind such a proxy, set nginx's `real_ip_header` / `set_real_ip_from` so the limits still see the player's address.

## Players on other PCs

- Set `PUBLIC_HOST` in `.env` to the address they reach this PC on: LAN IP, VPN IP (Tailscale / ZeroTier) or host
  name, then run `docker compose up -d`.
- Open the ports: UDP `LOGIN_PORT` and `WORLD_PORT`, TCP `ACCOUNT_PORT`, `WEB_PAGES_PORT` and `WEBSITE_PORT` (and each
  extra world's own two ports).

## Everyday commands

| | |
|---|---|
| `docker compose up -d` | Start (or apply changed settings) |
| `docker compose down` | Stop; all data stays |
| `docker compose logs -f world` | The world's console (`auth` for the login server) |
| `docker compose up -d --build` | Rebuild after updating the code |
| `docker compose restart world` | Restart the world |

Where the data lives:
- The `landmarkemu_db` Docker volume holds every database.
- `server-data/gamedata` holds the data the servers read (made by `setup`, its `icons/` by `icons`; safe to delete, it is
  made again).
- `server-data/worlds/<WORLD_KEY>` holds each world's saved files (`WORLD_DATA_DIR` in a world's settings names
  another folder):
  - the terrain edits, screenshots, contests, reports, felled trees and gathered nodes;
  - the world's `console.log` (this run; the five runs before it are kept as `console.0.log`, the newest, ..
    `console.4.log`), and `dev_commands.txt`: a line appended there runs as a dev command, e.g.
    `shutdown 60 Restarting`.
- Scheduled restarts: `RESTART_SCHEDULE` in `.env` (e.g. `Mon,Sat 03:00`, live's schedule, with `RESTART_TIME_ZONE`
  `America/Los_Angeles`). Players get the in-game warnings from an hour before, are sent to character select at the
  end, and the world container starts again by itself.
- Landmarks of Landmark: `LANDMARKS_WORLD` in `.env` picks whose contest-winning builds stand beside the Spires
  (`auto` = the live world named by `WORLD_NAME_ID`, else Courage; or Courage, Determination, Liberation, Rebellion,
  Serenity, `off`). They are stamped into the terrain once per landscape (docs/terrain.md "Landmarks of Landmark").
- Chaos Caverns on every island: `CAVERN_LANDSCAPES` in `.env` (`all` by default, `home` for the home continent only, or
  zone ids such as `406,391`) picks the landscapes Travel → An Underground Adventure and the Lost Crystals send players
  to. Each other landscape's outposts are stamped once, in the background, the first time someone is there (about 4
  minutes and ~110 MB of terrain files per landscape, up to ~1 GB of memory while it runs); `OUTPOST_PRELOAD=true`
  stamps the unvisited ones too, one after another (docs/gameplay.md "Chaos Caverns on every landscape").
- Outposts changing caverns: `OUTPOST_ROTATION_HOURS` (0 = never, the default) and `OUTPOST_ROTATION_SHARE` (0.25):
  every so many hours per landscape that share of its outposts gets another ruin in its own cavern, stamped in the
  background; outposts players are near are left for the next time (docs/gameplay.md "Outposts change caverns"). It
  writes terrain edits: back up `server-data` before switching it on.
- Creature pathing: `CREATURE_PATHING` (`true` by default) lets cavern and outpost creatures walk around cave rock and ruin
  walls; `false` = they step straight at their target, as before (docs/gameplay.md "Creature pathing"). It uses one
  low-priority background thread and at most ~10 MB of cached walkable tiles. `SITE_PATHING` (`true` by default, needs
  `CREATURE_PATHING`) lets story creatures on build sites walk around player walls, dug holes and placed trees / rocks
  (a grid per site from the Environment Server, rebuilt 2 s after the last edit there; at most ~3.4 MB for a fully built
  site, 16 sites held); `false` = they step straight, as before.
- Creature weapons: `CREATURE_WEAPONS` (`true` by default) puts a weapon model in the hand of every humanoid creature
  (Barren, Scalewind, Highfall, NovaTech, Ebonguard, Stoneskin and the Target Dummy). It is a best guess: the weapon type
  comes from each creature's icon in the client, the exact model is ours (docs/gameplay.md "Creature weapons"); `false`
  = none. One small packet per creature per player, once.
- Tree health: `TREE_HEALTH` (`health` by default = live's: trees have health by size, each axe swing takes the axe's
  damage and the player chopping sees the tree's health bar; `hits` = every tree falls after 5 swings, 8 for large ones).
- Tree respawns: `PROCEDURAL_RESPAWN` (`minmax` by default = live's model: a minimum and maximum per kind of tree and
  landscape, a kind below its minimum grows back at another random free spot after its respawn minutes; `inplace` = a cut
  tree grows back where it stood) and `PROCEDURAL_RESPAWN_MIN_SHARE` (0.67: the minimum as a share of the maximum)
  (docs/gameplay.md "Respawn model"). The free spots are saved in `gathered_nodes.txt`; the first visit to a landscape
  after a start counts its spots on a background thread (about 1 s with the old tree lists, 7-15 s with the placement
  sets); only on a landscape's very first population do its trees wait for that.
- Store prices: `FREE_STORE` (`false` by default = live's 2016 Marketplace items for Lumens, at `LUMENS_PER_DBC` Lumens
  per live Daybreak Cash; `true` = everything free) (docs/showcase.md "Pages").
- Founder's Pack / Year Zero / promotional items: `FOUNDER_ENTITLEMENTS` (`true` by default: every character gets them
  once), `ENTITLEMENT_PACKAGES`, `ENTITLEMENT_DELIVERY` (`auto` = as live: Founder's Pack into the bag, Year Zero and
  promo by mail; or `mail`, `bag`, or per package) (docs/gameplay.md "Founder's Pack and other account items").

Back up the `server-data` folder and the database (`docker exec landmarkemu-db-1 mariadb-dump -uroot -p --all-databases`).
`docker compose down -v` deletes the database volume; the `server-data` folder stays.

The database is published on `127.0.0.1:DB_PORT` (3307) for tools like HeidiSQL (user `landmarkemu`, `DB_PASSWORD`). If
Docker Desktop's forward of that port closes connections at the handshake (seen on one Windows PC), run the client
inside the container instead: `docker exec -it landmarkemu-db-1 mariadb -ulandmarkemu -p`.

## Moving from servers run directly on Windows

Earlier versions ran the servers directly on Windows with MariaDB installed. To move such a server to Docker:

1. **Back up.** Dump `lmemu_auth` and `lmemu_character` with `mariadb-dump --single-transaction --databases ...`.
   Archive the world server's folders (`TerrainEdits`, `SiteImages`, `Contests`, `Reports`, `Portraits`,
   `TemplateImages`, `TemplateProps`) and its `felled_trees.txt` and `gathered_nodes.txt`.
2. **Import both dumps into the Docker database, as bytes.** They contain binary data.
   - Windows MariaDB writes table names in lower case, but on Linux EF needs `__EFMigrationsHistory`. Rename it in the
     dumps.
   - Check first that both sides have the same migrations.
3. **Copy the world's own rows into `lmemu_world_<key>`,** after its first start (which creates the schema): `claim`,
   `claim_permission`, `claim_vendor_item`, `world_prop`, `world_prop_binding`, `world_prop_setting`, `world_area`,
   `chest_item` and `site_template_instance`.
   The old servers kept them in the character database; a Docker world reads them from its own.
4. **Unpack the world files into the world's data folder** (`server-data/worlds/<WORLD_KEY>`), stop the old
   servers, and run `docker compose up -d`.

## How the world split works (code)

- `CharacterDatabase.SeparateWorld` is true when `Database:Zone` is set.
- World tables (`claim`, `claim_permission`, `claim_vendor_item`, `world_prop`, `world_prop_binding`, `world_prop_setting`, `world_area`, `chest_item`, `site_template_instance`) are read and written through `NewWorldContext()` / `SaveWorld()`.
  - Callers: `Claims`, `ClaimInventory`, `Chests`, `Props`, `Vendors`, `PropSettings`, `StoryTools`, `AreaDefinitions`, `TemplateInstances`, `Landscapes.EventExpiry`.
  - Everything else uses the shared database.
  - Both databases are created by the same migrations; each uses its own part.
- IDs that servers pick themselves are allocated per world, so two running worlds never collide:
  - Item guids: world n uses n × 2^28 up to the next.
  - Guild ids: world n × 2^20.
- `WorldServer.json`:
  - `WorldNameId` and `PublicAddress` give the server-list entry (`AuthDatabase.RegisterZoneServer` at start).
  - `WebPagesPort` and `WebPagesUrl` give the in-game web pages.
- The auth server (`ZoneServerManager.Update`) adds new worlds and address changes while it runs.

Not yet shared live between running worlds: Player Studio is cached per world unless the commerce service is on, and
so are friends, ignore lists, guilds and mail unless the social service is on (above). A change on one world reaches
another when that world restarts. The social service (`Game/Social/RemoteSocialService.cs` and `RemoteMail.cs` in the
world, `LandmarkEmulator.SocialServer`) is the cross-world path for those; its internal messages are in
`LandmarkEmulator.Internal/Social/SocialMessages.cs` and `MailMessages.cs`. The commerce service
(`Game/WorldChannel/RemoteCommerceService.cs` in the world, `LandmarkEmulator.CommerceServer`) is the one for Player
Studio, contests and portraits; its messages are in `LandmarkEmulator.Internal/Commerce/CommerceMessages.cs`.
