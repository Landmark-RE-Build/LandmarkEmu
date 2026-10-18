# Admin players and the client's admin commands

The retail client has a set of admin slash commands that only run when the server marks the player as an admin:
SendSelfToClient carries an admin-player bool (`IsAdminMaybe` in the server's SendSelf model, player +0x7b0d8 in the
0.695 client), and the client checks "admin client OR admin player" before each command. The server sets it for the
entries in WorldServer.json `Admins` and `AdminList` (Docker: `ADMIN_ACCOUNT`, `ADMIN_2`, `ADMIN_3` in `.env` →
`Admins__0..2`, and `ADMINS`, comma separated → `AdminList`; see docker.md). An admin also gets the `/admin` web menu
(`Game/Admin.cs`).

Each entry names an account or a character:

| Entry | Who is an admin |
|---|---|
| `account:Alice` | Every character of the account with username `Alice`. Character names are not looked at. |
| `character:AliceTheBuilder` | The character `AliceTheBuilder`, whichever account owns it. Account names are not looked at. |
| `Alice` (bare, the old form) | The account `Alice` or the character `Alice`, unless the name is contested (below). |

The prefixes ignore case (`Account:` works too), and so do the names. Use the prefixed forms: account usernames are
unique among accounts and character names among characters, so nobody can take a prefixed entry away.

Bare entries still work so older configs don't break, but account names and character names are both chosen by
players. A bare entry is **contested** when it names the player's account while a character of another account has the
same name, or the player's character while another account has that username (someone took the name in the other
namespace). A contested entry makes nobody an admin — anyone could otherwise become admin by registering the admin's
character name as an account — and the world logs an error with the prefixed form to use instead. An uncontested bare
entry logs the same advice once at Info level.

Example `.env`:

```
ADMIN_ACCOUNT=account:Alice
ADMIN_2=character:AliceTheBuilder
ADMINS=account:Bob,character:Carol
```

The commands arrive as subs of the Command family (opcode 0x1E, u16 sub) and are handled in
`Game/AdminCommands.cs`, dispatched from `Network/Handlers/InteractionHandler.cs`. Every sub is gated again with
`Admin.IsAdmin`: a non-admin's packet (only a modified client can send one) is refused and logged. The exact wire
layouts, with the client functions they come from, are in `research/trace/0695/admin-commands-695.md`.

| Command (client) | Sub | What the server does |
|---|---|---|
| `/goto name` (`/goto player name`, `/warp`) | 0x42E | Teleports the admin beside an online player (partial names accepted), landscape change included |
| `/goto waypoint [name]` | 0x430 | Logged only: the server does not keep player waypoints yet |
| `/target name` (`/gm target`) | 0x4B5 | Looks the character up (online or not) and replies with the same sub `{u64 guid, string name}`; the client sets its "admin target", which `/goto`, `/profile` ... then use. `/target clear` is client-side |
| `/hideme [on\|off]` | 0x458 | Hides the admin from every other player (removed from their view, no movement fan-out, not introduced to arrivals) until `/hideme off` |
| `/run n` / `inc n` / `dec n` / `default` | 0x4D8 | Sets the admin's movement-speed stat (cStatTypeMovementSpeed, default 12) to the metres/second the client sent and re-sends the calculated stats. `default` (and any n ≤ 0) arrives as 0.0 and removes the override; inc / dec are floored at 1.0 by the client |
| `/servertime`, `/zonetime`: `hour h`, `pause`, `sync`, `warp n` | 0x429 | Moves, pauses / resumes or re-rates the world clock (`Game/WorldClock.cs`) and re-syncs everyone online |
| `/clienttime set\|hour\|pause\|sync\|warp\|advance\|reverse` | 0x42A | Echoes the packet to the sender: the client applies it to its own clock (admin-gated receive handler, 7 modes: 1 set h m s, 2 hour f, 3 pause toggle, 4 sync = rate 8.0, 5 warp rate, 6 / 7 advance / reverse f hours). Unknown modes and NaN values are refused; after `sync` the server also sends the world clock (GameTimeSync). Any change lasts until the client's next time sync |
| Admin monitor requests: PathingDataMonitorUpdate, ZonePlayerCount, StorybricksRequestDiagnostics | 0x4A9, 0x4CC, 0x4FC | Logged (once a minute per admin and kind). The retail client never sends them, and none of their replies has a reader, so none is sent (`Game/AdminCommands.Monitor.cs`) |
| DebugPathQueryRequest (build tool "PathFindVisualize": two clicks) | 0x4A7 | Runs the server's creature pathing (`Game/Pathing.cs`) between the two points and answers in chat: the straight-line verdict, reached / partial / none, waypoints, walked length, nodes. The tool has no palette entry or key in retail; dev command `pathquery dx dy dz` sends the same request |
| `/prop id` | 0x4EA | Places a free prop at the transform the client computed (ground under the crosshair, facing the camera), owned by the admin, saved and shown to the landscape |
| `/prop reset_zone_and_wipe_db_forever_with_no_undo` | 0x4ED | Refused; logged with who tried |
| `/friend forceadd target [source]`, `/friend remove target [source]` | 0x4CF, 0x4D0 | Adds (without a request) / removes the target on the source's friends list (the admin's when no source; any character, online or not). Done where the friends lists live: the social service when `SOCIAL_ADDRESS` is set, else the world itself. The source sees the same list change as a normal add / remove; the admin gets one line with the real outcome (added, removed, not on the list, already listed, no such character, list full, service unavailable) |
| `/vehicle`, `/setmodel`, `/ammo`, `/weaponset`, `/placedeffect`, free-fly hide, `/profile ...` | 0x49C, 0x4F0, 0x499, 0x4EE, 0x4EF, 0x4F1, 0x3EC | Decoded and logged with their fields; the matching systems (vehicles, model override, ammo, weapon sets, placed effects, job profiles) are not built |
| `/location x y z [heading]`, teleport-to-camera, the world map's admin teleport | 0x51 (0x52 with a new heading) | The client freezes the player until a TeleportToLocation answers; the server moves an admin to the exact point (`Game/TeleportRequests.cs`, which also serves the Escape menu's Evac to Safety for everyone) |

Client-side admin extras that need no server reply: `/loc` (printing the position), `/clear resource`, Demo mode, the
render tweaks (radius, fog, foreground scale, shadow quality, near/far).

Logs: every admin command is written at Info level with the admin's name and the decoded fields (`Admin X /goto ...`);
refused ones at Warn.

## Broadcasts and kicks (/admin menu, dev commands)

- **System Broadcast** (`broadcast <text>`; the admin menu's Broadcast box sends it to everyone): BaseBroadcastPacket
  0x4F `{i16 kind, name block {u32, u32, u32, string, string}, string message (≤ 255 bytes), u32 text id}` (client reader
  FUN_142c17410, handler FUN_142cd9130). The client shows "System Broadcast: <text>" as on-screen text and a chat line;
  `broadcast from <Name> <text>` shows "Broadcast from <Name>: <text>", `broadcast raw <text>` the text alone. The
  client's format keys (SystemBroadcastFormat …) are not in its CodeStringMappings.txt, so it always uses these
  built-in fallbacks (`Game/Broadcasts.cs`). Live had no in-game sender for it; `ShutdownBroadcasts`
  (`SHUTDOWN_BROADCASTS`, default false) also mirrors every shutdown / restart warning as a System Broadcast.
- **Kick** (`@Name kick [seconds] [reason]`, `@Name kick cancel`; the menu's Moderation row for the selected player): a
  countdown with warnings at the start and at 300 / 120 / 60 / 30 / 10 / 5 s, then PacketClientKickedFromServer 0x53
  `{string}` (a message box) and the connection closes 1.5 s later (`Game/Kicks.cs`). Each warning is a readable chat
  line plus the client's PacketClientPendingKickFromServer 0x65 `{string message, u32 seconds}` (reader FUN_142c4a0d0;
  setting `PendingKickPacket` / `PENDING_KICK_PACKET`, default true): it turns the client's sound back to full, but its
  own text key (KickedFromServerTimedMessage) is missing from the retail clients, so its red line is empty. A bare
  `kick` without `@Name` is refused. The shutdown countdown sends the 0x65 once at 30 s or less.

## Server monitor (/admin/monitor) and the client's monitor pages 28 / 29

The live client has an admin performance monitor (Alt+F) with pages fed by the server: 11 zone profiler, 12 zone
stats, 24 gateway, 28 procedural data, 29 instanced games, 32 pathing, 33 Environment Server. The retail client can't
select a page (nothing in 0.589 or 0.695 calls the page setter; research/trace/0695/OPEN-LEADS.md L3), and only
pages 28 and 29 have readers for a server reply (L4). So the data is shown on a web page of the world instead, and the
two replies the client can read are still sent the way the client reads them (`Game/AdminMonitor.cs`; client trace in
`research/trace/architecture/admin-pages.md`).

**Web page.** `/admin/monitor` on the world's web server (WorldServer.json `WebPagesPort`, default 19001),
opened from the **Server monitor** button of the `/admin` menu. Admins only (the admin web login of `/admin`, see
"Admin web login" below). Read-only; it polls `/api/admin/monitor` (JSON) every 3 s while open; the snapshot is built on the world thread and shared for 2 s
whatever the number of open pages; nothing is fetched from outside the world's web server. Sections:

| Section | Shows | Source |
|---|---|---|
| ZONE STATS | players, live creatures and placed props per landscape; tick average / maximum ms and ticks per second over the last 5 s | PlayerManager, `Creatures.AliveByZone`, `Props.PlacedByZone`, `AdminMonitor.RecordTick` |
| ZONE PROFILER | every world update system: count, avg / min / max µs, total ms in the last 5 s window (the columns of the client's pathing page) | `HostedService` `Run` → `AdminMonitor.RecordSystem` |
| Services | gateway (in-process or separate, connected), social (in-process or remote, connected), world-channel service linked, commerce implementation | `GatewayLink`, `SocialServices`, `WorldChannelLink`, `WorldChannelServices` |
| Terrain | edited cells held (expanded / compact), cells healed since start, last heal | `EnvironmentServer.Instance.MemoryReport`, `ZoneTerrain.HealedCells` |
| Instanced Game Information - Server | every site game as the page-29 tree (below) | `InstancedGames.MonitorAll` |
| Procedural data | placement sets on / loaded, respawn model; per landscape with players: nodes standing / candidate spots / minimum / respawns queued per placement set, types below their minimum | `Gathering.NodeCounts` |
| PACKET MONITOR | zone packets by their first 4 bytes, in / out: count, total / average / worst bytes, rate between two polls | `OpcodeStats.Snapshot` (live counts since start) |
| Players | name, landscape, position, admin | PlayerManager |

**Page 29 (InstancedGameAdminMonitorPacket).** `u16 0x10D, u32 sub 2, u32 n, n × {string key, string value, u32 indent}`
(same bytes for 0.589 and 0.695). The client has no request for it, so the server pushes it: only with the setting
**`AdminMonitorPush`** = true (WorldServer.json or the environment; **default off**, since a retail client can't show
page 29), only to admins standing on or playing in a site game, at most every 2 s and only when the tree changed (or
every 10 s). Tree (keys ours, live's content is L81): indent 0 `Game <guid> <name>`; indent 1 `State`, `RuleSet`,
`Site`, `Landscape`, `GameEnabled`, `Players`, `MinPlayers`, `MaxPlayers`, `QueueSize`, `Observers`, `Editors`,
`StateEnds`, `RoundEnds`, `TeamScores`, `ControlPoints`; indent 2 one line per player (`team t, K/D/S k/d/s`).

**Page 28 (ProceduralDataMonitorPacketUpdateResponse 0x10A).** The client sends 0x109 (bare opcode) every 5 s while
page 28 is shown; an admin gets 0x10A for their landscape (at most one per 4 s), anyone else nothing. Layout in
`AdminMonitor.ProceduralBody` and admin-pages.md §3. Live's id tables (ProceduralPlacementEntityLists /
...ListEntities.txt) were server-only, so the contents are ours: layer = placement data group, biome = the set's biome,
entity list = placement set, entity = set entity (its prop's name), spawn count = nodes standing, node count = candidate
spots; with the min / max respawn model (docs/gameplay.md "Respawn model") an entity's min / max and the rows'
`MIN_SPAWNS_PER_ZONE` / `MAX_SPAWNS_PER_ZONE` are its pool's minimum / maximum on the landscape, a layer's "queued" is the
respawns waiting, and Below Min / Above Min / Over Max sum the standing nodes of entities under their minimum / between /
at their maximum (with `inplace`: min 0, max `MAX_SPAWNS`, queued = nodes regrowing); the client only draws lists and
entities whose ids are in the two row tables, so every id sent gets a row.

## Admin web login

The admin pages (`/admin`, `/admin/monitor`, `/admin/settings`) and their requests (`/api/admin/...`) are signed in with
a cookie, not with the URL (`Game/AdminSessions.cs`, `WebPages.AdminRequest`):

1. The in-game `/admin` command opens `/admin?t=<the admin's Showcase token>` in the game's browser.
2. The world checks that the token is an online admin's, starts an admin web session (64 random hex digits) and answers
   `302 Location: /admin` with `Set-Cookie: lm_admin_<web port>=<session>; Path=/; Max-Age=3600; HttpOnly;
   SameSite=Strict`. The token is used once and is gone from the address bar and the browser's history; the web log
   writes request lines with `t=…` instead of the token.
3. Every admin page and request after that is checked by the cookie only; a URL token is not accepted there. A session
   lasts 1 hour from the sign-in and only while its admin is in the world and still in the admin list (it names their
   Showcase token, which ends when they leave). Typing `/admin` again signs in afresh. The cookie name carries the web
   port, so two worlds on one host keep their logins apart.
4. Every admin POST (the menu's commands, Player Studio staff review, the Settings page's save and restart) also needs
   the page's anti-forgery token: an HMAC of the cookie's session under a key made at each start, written into the page
   (`var csrf=...`), and, when the browser sends an `Origin` header, this server's own origin. A request without them is
   refused (403) and logged.

The game's browser must keep cookies for the admin pages to work (a page opened without the cookie says to type
`/admin` again).

## Settings page (/admin/settings)

Every world setting on one page, grouped as in `.env.example`, with toggles, number boxes and drop-downs instead of
editing `.env` (`Game/ServerSettings.cs`, `ServerSettings.Table.cs`, `ServerSettings.Docs.cs`, `ServerSettingsPage.cs`).

**Opening it.** In game, as an admin: type `/admin`, then press **Server settings**. (Directly: `/admin/settings` on the
world's web server once signed in, see "Admin web login".)

**What each row shows.** The WorldServer.json name and the `.env` name(s); the value (for a setting that needs a
restart: the saved value, with "restart needed (running: ...)" while the world still runs the old one); where it comes
from (`default`, `WorldServer.json`, `.env`, `command line` or `page`, with who set it and when); the code default; the
value that was live Landmark's behaviour (`live: ...`, marked "differs" when the setting is not on it; "not known" has no
chip); when a change counts (`now`, `next login`, `restart`); the help text and notes. The groups, help texts and `.env`
names are read from `.env.example` and `docker/world/docker-compose.yml`, embedded in the world's program when it is
built, so the page always says what those files say. A search box filters by name, `.env` name or text; groups fold
(remembered by the browser); changed but unsaved rows are marked yellow and counted beside **Save changes**.

**Where changes are kept, and precedence.** The world container can't rewrite the host's `.env`, so a saved change goes
to an overrides file in the world's data folder: setting `SettingsOverridesFile` (Docker:
`/data/settings-overrides.json` = `server-data/worlds/<WORLD_KEY>/settings-overrides.json`; outside Docker, empty =
`settings-overrides.json` beside the program). It is the last configuration source:

    code default  <  WorldServer.json  <  environment (.env through docker compose)  <  command line  <  the Settings page

**Reset to .env** on a row removes its override, so `.env` (or the JSON, or the default) counts again. The file is JSON
(`{"Settings": {"FreeBuild": {"Value": "true", "By": "Alice", "At": "..."}}}`), written atomically (a temporary file,
flushed, then renamed over the old one). At start-up only settings the page may change, with valid values, are taken
from it; anything else in it is logged and ignored. Edit it by hand only while the world is stopped.

**Saving.** **Save changes** sends only the changed rows. The server checks every one (known name, editable, type,
range, choices, format, at most 1,000 characters, no line breaks) and refuses the whole save if one is wrong, so nothing
is half saved. Each change is logged: `Admin <name> set <setting> from "<old>" to "<new>" (in effect now)`. A save body
over 64 KB is refused, not cut.

**When a change counts** (each "now" / "next login" was checked against the code that reads it):

| When | Settings |
|---|---|
| now | MaxPlayers, LandscapeCapacity, ClientTimeoutSeconds (the in-process gateway; a separate gateway reads CLIENT_TIMEOUT_SECONDS from .env at its start), RestartSchedule / RestartTimeZone / RestartWarningSeconds / RestartMessage (the next restart is planned again at once), ShutdownBroadcasts, PendingKickPacket, TargetNotOnlinePacket, MovementEffects, MeleeTargeters, BlockStanceVisible, SafeEjectReset, PropStreamRange, CreatureWeapons (creatures sent from then on), PioneerSlab / PioneerSlabMaterial (new sites), LANDMARK_SEASON, HAT_DROPS, the Player Studio settings (in-process commerce; with `COMMERCE_ADDRESS` the commerce service's own values count), TrialKeys, TrialKeyDays, BuildMenuPropName, ChestAccountCap, FriendTeleportCooldownSeconds, StoryPosePersists, PlaceOpensBuildMode, ShowcaseFollowDigest, ShowcaseDigestHourUtc, FinderReturnHome, CavernTripsOtherWorlds, MemberMonthlyLumens, MemberStoreDiscountPercent, StoreTradeWaitHours, OverflowExpiryHours, MailMaxStack, ItemAutoResupply, the Grief settings |
| next login | MotdSubject, MotdBody, FreeBuild (the server checks it at once; the client gets it at login), LiveCharacterStats, FounderEntitlements, EntitlementPackages, EntitlementDelivery |
| restart | WorldNameId, ClientWorldNameText, WatchdogSeconds, CraftingWindowCloseAway, PlayerTeleportRequests, AdminMonitorPush, LandmarksOfLandmarkWorld, CavernLandscapes, OutpostPreload, OutpostRotationHours / Share, CreaturePathing, SitePathing, SurfaceGraveyards, CavernGraveyardStamps, the Procedural settings, EventIsland, EventIslandExpireOnClose, MoveOldPioneerSites, FreeStore, LumensPerDaybreakCash, StorePrices, MembershipPriceLumens, MembershipDays (the store's catalogue is built once), VendorShops, DayCycleSpeed, ShortTeleportDistance, ShortTeleportWaitScreen, ItemDefinitionChunkSize, EnvServerStartupWaitSeconds |

A dev command that switched a setting until the restart (`pathing`, `propstream`, `meleetargeters` ...) still wins over
the page's value until then.

**Read-only (shown, changed in `.env` only).** Network, ports and addresses (`Network`, `PublicAddress`, `WebPages*`,
`SocialAddress`, `CommerceAddress`, `EnvServerAddress`, `GatewayInternalAddress`, `ZONE_INTERNAL_PORT`): they must
match the Docker port mappings and the other containers. Ids (`ZoneServerId`, `ClientWorldId`). Folders and files
(`GameDataPath`, `GameDirectory`, `HealthFile`, `SettingsOverridesFile`). `Continent`, `TerrainLodCopies`,
`KeepVertexOnlyVoxels`: one `.env` line feeds the world and the Environment Server, and an override on the world alone
would make them disagree. The admin list (`Admins`, `AdminList`): an admin who could change it on the page could make
anyone an admin. `StoreLumenPrices` (the JSON dictionary form; edit `STORE_PRICES`).

**Secrets.** The database connections (they carry `DB_PASSWORD`) are never sent to the page, only "set" / "not set";
any setting whose name ends in ConnectionString / Password / Secret / Token is treated the same way.

**Other containers.** `.env` lines the world doesn't read (login server, website, social / commerce / gateway
services, LaunchPad) are listed at the bottom by name and help only: change them in `.env`.

**Restart world.** The box at the top lists the saved settings waiting for a restart. **Restart world** (with a confirm
dialog) starts the usual shutdown countdown (default 60 s, with a message): players get the warnings, are sent to
character select and saved, and the world stops cleanly. In Docker the world's `restart: unless-stopped` policy starts
it again; outside a container the page says that something must start it again. Logged as `Admin <name> restarted the
world from the settings page`. Refused while a shutdown is already counting down.
