# Audit: server-owner settings vs live defaults (2026-10-18)

Read-only audit. The rule it checks, from the user on 2026-10-18: "I'd like it to be togglable … by admins/server owners
but I'd also like it to be how it was on live". Every gameplay setting defaults to live's behaviour and stays a toggle.
Extras that go beyond live default to off.

Sources read:
- `LandmarkEmulator.WorldServer/WorldServerConfiguration.cs` and `WorldServer.example.json`.
- Every `Environment.GetEnvironmentVariable` and `IConfiguration.GetValue` call in the solution.
- The root `docker-compose.yml`, `docker/{world,auth,commerce,envserver,gateway,social,worldchannel}/docker-compose.yml` and `docker/gateway/world.override.yml`.
- `.env.example`, `docker/worlds/dev.env` and `docker/worlds/extra-world.env.example`.
- The other servers' `*Configuration.cs` and `WebApiConfig.cs`.
- The live evidence: `trace/0695/patchnotes.tsv`, `agent-membership.md`, `agent-store-entitlements.md`, `audit-outposts-695.md`, `procedural-placement-695.md`, MODLOG and code comments.

Verdicts:
- **OK**: the default equals live, or the setting is an extra beyond live and defaults to off.
- **FLIP**: the default is not live. The live value is given.
- **NO-LIVE**: there is no live equivalent (infrastructure, ports, test aids, content).
- **UNKNOWN**: live's value is not known. Keep the current default.

"—" means the setting is not present in that place. "comp" means the default in the compose file. ".env.ex" means the value in `.env.example`.

## 1. World server (WorldServerConfiguration + env vars read directly)

| Setting (config key / env var) | Code | comp | .env.ex | Controls | Live (source) | Verdict |
|---|---|---|---|---|---|---|
| FreeBuild / — | false | — | — | Build and place props anywhere, with no claim | Claims were required. Props only where you have build permission (project rule "props only on build sites") | OK (extra, off) |
| KeepVertexOnlyVoxels / — | true | — | — | Keep the client's vertex-only voxels | The client caches and meshes them itself (code comment, Cache/2/Voxel/416) | OK |
| TerrainLodCopies / — | true | — | — | Coarse LOD copies of edits | Live served them, otherwise edits vanish at a distance (code comment) | OK |
| GameDirectory, GameDataPath / GAME_DIR, GAME_DATA_DIR (volumes) | "" / "gameData" | /game, /gamedata | GAME_DIR set | Install and data paths | — | NO-LIVE |
| ZoneServerId / WORLD_ID | 1 | 1 | 1 | Server-list row | — | NO-LIVE |
| WorldNameId / WORLD_NAME_ID | 0 (keep the stored one) | **62147 "Dev Server"** | **560916 Serenity** | Name in the server list | Live names 560911-560926 | NO-LIVE. **Inconsistent** (§4 #1) |
| ClientWorldId / CLIENT_WORLD_ID | 0 (= 2) | 0 | — | World id told to the client | 2 = the live 406 capture | OK |
| ClientWorldNameText / CLIENT_WORLD_NAME_TEXT | 0 (= 560912 Liberation) | 0 | — | World name in game (Landscapes window, zone details) | On live the in-game name was the world's own name. Here it stays "Liberation" whatever WORLD_NAME_ID says | FLIP (logic): 0 should follow WorldNameId when that is a live name (§4 #9) |
| PublicAddress / PUBLIC_HOST + WORLD_PORT | "" | 127.0.0.1:19000 | PUBLIC_HOST empty | Server-list address | — | NO-LIVE |
| Network__Port / WORLD_PORT | (json 19000) | 19000 | 19000 | UDP port | — | NO-LIVE |
| WebPagesPort, WebPagesHost, WebPagesUrl / WEB_PAGES_PORT | 19001, loopback, derived | 19001, 0.0.0.0, http://host:19001 | 19001 | Showcase web pages | — | NO-LIVE |
| Continent / — (EnvServer: ENVSERVER_CONTINENT) | 406 | — | — | Starting landscape | 406 = the live capture's | OK |
| LandmarksOfLandmarkWorld / LANDMARKS_WORLD | auto | auto | auto | Landmarks of Landmark set by world | A different set per world (forum 3 Aug 2016) | OK. dev.env sets off |
| CavernLandscapes / CAVERN_LANDSCAPES | all | all | all | Which islands' caverns take trips | P5927, P5734 (any zone, other worlds) | OK. dev.env sets home |
| OutpostPreload / OUTPOST_PRELOAD | false | false | false | Stamp unvisited islands up front | Performance aid | NO-LIVE |
| OutpostRotationHours / OUTPOST_ROTATION_HOURS | 0 (never) | 0 | 0 | Outposts change cavern | "Outposts exist in the open world and change regularly" (client FTE help, audit-outposts-695.md F8). The period is unknown (L76) | UNKNOWN. Live did rotate, so 0 is known to be not live; it needs a number (§3) |
| OutpostRotationShare / OUTPOST_ROTATION_SHARE | 0.25 | 0.25 | 0.25 | Share of outposts per rotation | Unknown | UNKNOWN |
| CreaturePathing / CREATURE_PATHING | true | true | true | Creatures path around rock and walls | Live creatures navigated | OK |
| SitePathing / SITE_PATHING | true | true | true | Story creatures path around player builds | P5781, forum 56713 | OK |
| SurfaceGraveyards / SURFACE_GRAVEYARDS | true | true | true | Surface Flee graveyards (map red circles) | The client's map has them. Their spots are inferred | OK |
| CavernGraveyardStamps / CAVERN_GRAVEYARD_STAMPS | true | true | true | Cavern graveyard templates | The client leaves the placements to the server | OK |
| EventIsland / EVENT_ISLAND | auto | auto | auto | Events-world island by date | 2016 calendar (event-islands-695.md, P6532) | OK. Stale doc comments (§4 #6) |
| EventIslandExpireOnClose / EVENT_ISLAND_EXPIRE_ON_CLOSE | true | true | true | Expire sites on the island that closed | Forum 58077, 21 Nov 2016 | OK (it deletes builds; dev.env does not override it, see §5) |
| LANDMARK_SEASON (direct env, RareDrops.cs) | auto | auto | auto | Seasonal outfit drops | P6504-P6507, P6565-P6569 | OK |
| HAT_DROPS (direct env, RareDrops.cs) | season | season | season | Ninja / Samurai unlocker drops | P6505, P6565 | OK |
| ProceduralRespawn / PROCEDURAL_RESPAWN | minmax | minmax | minmax | Tree respawn model | Forum 55352 (min / max per type, respawn elsewhere) | OK |
| ProceduralRespawnMinShare / PROCEDURAL_RESPAWN_MIN_SHARE | 0.67 | 0.67 | 0.67 | Minimum as a share of the maximum | The post's 1000 / 1500, which the developer said were made up | UNKNOWN |
| ProceduralPlacement / — | false | — | — | Placement-set trees | Live used procedural placement from rules the client doesn't carry. The shipped sets are alpha-era (L113) | UNKNOWN. The doc says otherwise (§4 #7) |
| ProceduralPlacementDecor / — | false | — | — | Decoration from the sets | Unknown whether live had server-placed rocks (L113) | UNKNOWN |
| ProceduralPlacementMasks / — | true | — | — | Placement masks | Part of the sets' input [I] | OK |
| PlayerStudioApprovals / PlayerStudioRejections / — (world, in-process) | 3 / 3 | — | — | Peer-review thresholds | Not in the archive (P1207-P1210 give the reviewer rules only) | UNKNOWN. Can't be set by env on the world (§4 #10) |
| PlayerStudioCreatorSharePercent / PLAYER_STUDIO_CREATOR_SHARE | 40 | 40 | 40 | Creator's share | 60/40 split, creator 40 (Update Notes 18 Jun 2014, forum 41434) | OK |
| PlayerStudioListingFee / PLAYER_STUDIO_LISTING_FEE | true | true | true | Listing fee of 10 % (minimum 100 DBC) | 18 Jun 2014, 14 Oct 2016, text 563260 | OK |
| MotdSubject / MotdBody / — | "Welcome to Landmark" / fan-server text | — | — | Message of the day | Content | NO-LIVE. Missing from compose (§6) |
| Admins / ADMIN_ACCOUNT, ADMIN_2, ADMIN_3 | [] | 3 slots | 3 slots | Admin menu | — | NO-LIVE |
| MaxPlayers / MAX_PLAYERS | 0 | 0 | **—** | World cap and login queue | Live queued logins (E.8 trace). Cap unknown | UNKNOWN. Missing from .env.example |
| LandscapeCapacity / LANDSCAPE_CAPACITY | 0 | 0 | 0 | Per-landscape cap and travel queue | Live had it ("This location is currently full"). Number unknown | UNKNOWN |
| FreeStore / FREE_STORE | false | false | false | Free store | Live store = 2016 Marketplace (agent-store-entitlements.md) | OK (extra, off) |
| LumensPerDaybreakCash / LUMENS_PER_DBC | 1.3 | 1.3 | 1.3 | DBC to Lumens rate | Lumen Box 650 for 500 DBC (texts 574410-574422) | OK |
| StoreLumenPrices / StoreLumenPrices__<id> | {} | — | — | Owner price overrides | — | NO-LIVE |
| FounderEntitlements / FOUNDER_ENTITLEMENTS | **true** | true | true | Founder / Year Zero / promo items for everyone | Live gave them only to buyers, survivors and winners (P0247 "Founder's Pack items … purchased"; Wiki "Year Zero Trophy") | **FLIP → false**. Conflicts with the 2026-10-17 decision "for now … turned off later" (§2) |
| EntitlementPackages / ENTITLEMENT_PACKAGES | founder,yearzero,promo | same | same | Which packages | Follows FounderEntitlements | OK |
| EntitlementDelivery / ENTITLEMENT_DELIVERY | mail | mail | mail | Mail or bag | Mixed: Founder's Pack items went into a new character's bag; Year Zero gifts were mailed (agent-store-entitlements.md §2, Wiki) | FLIP (partial): founder → bag, yearzero → mail. Needs a per-package default |
| RestartSchedule / RESTART_SCHEDULE | **""** | "" | "" | Scheduled restarts | "every Monday and Saturday morning at 3am Pacific" from 25 Jun 2016 (P5902, P5906) | **FLIP → "Mon,Sat 03:00"** (operational; see §2) |
| RestartTimeZone / RESTART_TIME_ZONE | America/Los_Angeles | same | same | Schedule time zone | Pacific (P5902) | OK |
| RestartWarningSeconds / — | 3600 | — | — | Warning lead time | Not in the archive | UNKNOWN. Missing from compose |
| RestartMessage / — | "Scheduled server restart." | — | — | Warning text | Content | NO-LIVE. Missing from compose |
| HealthFile / — | "" | /tmp/world-alive | — | Healthcheck file | — | NO-LIVE |
| WatchdogSeconds / WATCHDOG_SECONDS | **0** | **180** | **180** | Hung-world self-restart | — | NO-LIVE. Inconsistent (§4 #2) |
| SocialAddress / SOCIAL_ADDRESS (also read directly) | "" | "" | "" | Shared social service | Live was split (architecture) | NO-LIVE |
| CommerceAddress / COMMERCE_ADDRESS (also read directly) | "" | "" | "" | Shared commerce service | as above | NO-LIVE |
| EnvServerAddress / ENVSERVER_ADDRESS (also read directly) | "" | "" | "" | Environment Server | as above | NO-LIVE |
| EnvServerStartupWaitSeconds / — | 120 | — | — | Wait for the EnvServer index | — | NO-LIVE |
| GatewayInternalAddress / GATEWAY_INTERNAL_ADDRESS (also read directly) | "" | override: gateway-<key>:20300 | — | Separate gateway | — | NO-LIVE |
| ZONE_INTERNAL_PORT (direct env) | unset | override: 20310 | — | Zone port for the world channel | — | NO-LIVE |
| MembershipPriceLumens / MEMBERSHIP_PRICE_LUMENS | 1950 | 1950 | 1950 | Buy membership with Lumens | Live: membership meant only owning the game; All Access gave no Landmark benefits (agent-membership.md §2). Buyable membership is the user's 10-17 design | NO-LIVE (user design) |
| MembershipDays / MEMBERSHIP_DAYS | 30 | 30 | 30 | Days per purchase | All Access was monthly [I] | NO-LIVE (user design) |
| TrialKeys / TRIAL_KEYS | true | true | true | Trial key letter | P6417, 29 Aug 2016 | OK |
| TrialKeyDays / TRIAL_KEY_DAYS | 10 | 10 | 10 | Trial length | "single-use, 10-day trial key" (P6417) | OK |
| VendorShops / VENDOR_SHOPS | false | false | false | Working vendor booths | Decorative since 1 Apr 2016 (P4378) | OK |
| FinderReturnHome / FINDER_RETURN_HOME | false | false | false | Return after a cross-world game | No source (P1735 covers queueing only) | UNKNOWN (keep; as an extra it is off) |
| MemberMonthlyLumens / MEMBER_MONTHLY_LUMENS | **650** | 650 | 650 | Monthly Lumen stipend | No live Landmark member benefits: "does not have any of the normal All Access benefits" (wiki Daybreak Cash; agent-membership.md §2, §7 "none is live") | **FLIP → 0** (extra beyond live). Conflicts with the 10-17 decision (§2) |
| MemberStoreDiscountPercent / MEMBER_STORE_DISCOUNT | **10** | 10 | 10 | Member store discount | as above | **FLIP → 0** (extra beyond live). Conflicts with the 10-17 decision (§2) |
| GriefAwards / GRIEF_AWARDS | true | true | true | Friendly fire earns grief | The client ships the grief warnings (texts 56075-56079) and team-kill rule sets | OK |
| GriefDamagePerPoint, GriefLevelPoints, GriefDecayPerHour, GriefSuspensionMinutes / GRIEF_* | 100, "10,20,30,40,60", 30, 60 | same | same | Grief numbers | Unknown (L144) | UNKNOWN |
| ShortTeleportDistance / — | 0 (every teleport re-zones) | — | — | Short moves without re-zoning | The client re-centres only beyond 80 m (FUN_143152c40), so live sent plain moves for short hops. Our 0 works around a client hang (L116) | Diverges by necessity: keep 0 until L116 is traced, then the live value ≈ 80 |
| ShortTeleportWaitScreen / — | false | — | — | Wait-screen byte on short moves | Unknown (L116) | UNKNOWN |
| AdminMonitorPush / — | false | — | — | Push admin page 29 | Admin tool | NO-LIVE |
| LANDMARK_NOSTREAM (direct env) | unset | — | — | Turns off terrain streaming (test aid) | — | NO-LIVE |

## 2. FLIP list (default is not live)

| # | Setting | Current → live | Source | Note |
|---|---|---|---|---|
| F1 | RestartSchedule / RESTART_SCHEDULE | "" → **"Mon,Sat 03:00"** (America/Los_Angeles) | P5902 (24 Jun 2016, "Server restarts Saturdays & Mondays"), P5906 | Operational, not gameplay. Live from 25 Jun 2016 (earlier 5 a.m. three nights a week, P3643). The compose restart policy already brings the world back. The user decides whether the "gameplay" rule covers it |
| F2 | FounderEntitlements / FOUNDER_ENTITLEMENTS | true → **false** | P0247 (Founder's Pack items are purchases); Wiki "Year Zero Trophy" (survivors only); agent-store-entitlements.md | Conflicts with the user decision of 2026-10-17 (memory store-membership-decisions: "for now (also as tester items) … turned off later"). Needs the user's confirmation |
| F3 | MemberMonthlyLumens / MEMBER_MONTHLY_LUMENS | 650 → **0** | agent-membership.md §2 (wiki Daybreak Cash: Landmark "does not have any of the normal All Access benefits"; player 2015 "Landmark is not part of All Access"), §7 "none is live" | An extra beyond live. Conflicts with the user decision of 2026-10-17 ("monthly stipend") |
| F4 | MemberStoreDiscountPercent / MEMBER_STORE_DISCOUNT | 10 → **0** | as F3 | An extra beyond live. Conflicts with the user decision of 2026-10-17 ("store discounts") |
| F5 | EntitlementDelivery / ENTITLEMENT_DELIVERY | mail (all packages) → **founder: bag, yearzero: mail** (promo: unknown) | agent-store-entitlements.md (live put Founder's Pack items in a new character's inventory; Wiki: Year Zero mailed) | Partial: needs a per-package default, or "auto" |
| F6 | ClientWorldNameText / CLIENT_WORLD_NAME_TEXT | 0 = always 560912 "Liberation" → **the world's own name (WorldNameId when it is a live name 560911-560926)** | Landscapes.cs:53-57. On live every world showed its own name | Fidelity and logic fix rather than a number |
| F7 | Gateway MultiWorld / GATEWAY_MULTI_WORLD | false → **true** when more than one world runs | P5734 ("Lost Crystals can now teleport you to other worlds"); OPEN-LEADS L114 (one gateway reached every world) | Only matters for owners running several worlds. A single world is unaffected |

Known not-live but not flippable yet:
- OutpostRotationHours = 0. Live outposts "change regularly" (FTE 2, F8), but no period is known (L76). It needs a number before it can default on.
- ShortTeleportDistance = 0. It works around a client hang (L116).
- MaxPlayers and LandscapeCapacity = 0. Live had both caps, but the numbers are unknown.

## 3. Other servers' settings

| Setting / env | Code | comp | .env.ex | Controls | Live | Verdict |
|---|---|---|---|---|---|---|
| Auth CreationRules.StrictName / — | false | — | — | RPG-style names only | No archive rule found (P0356: names not unique in 2014 alpha) | UNKNOWN. Not settable in Docker |
| Auth WorldAddress | "" | "" | — | Force one world address | — | NO-LIVE |
| Auth Internal (20210) | 0.0.0.0:20210 | expose 20210 | — | Login internal port | — | NO-LIVE |
| WebApi RegistrationOpen / REGISTRATION_OPEN | true | true | true | Sign-ups | — | NO-LIVE |
| WebApi RegistrationsPerAddressPerHour / REGISTRATIONS_PER_ADDRESS_PER_HOUR | **10** | **3** | **3** | Sign-up limit per address | — | NO-LIVE. Inconsistent (§4 #5) |
| WebApi RegistrationsPerHour / REGISTRATIONS_PER_HOUR | 60 | 60 | 60 | Global sign-up limit | — | NO-LIVE |
| WebApi ServerName, WebsiteUrl, RequiredClientBuild, NewsFile / SERVER_NAME, WEBSITE_URL, REQUIRED_CLIENT_BUILD, LAUNCHER_DIR | LandmarkEmu, "", "", "" | same, derived, "", /launcher/news.json | set | LaunchPad info | — | NO-LIVE |
| WebApi LoginPort, PatchFolder / — | 0, "" | — | — | Launcher login port and patch packs | — | NO-LIVE |
| LANDMARKEMU_SERVER_PATCHES (LaunchPad, direct env) | off | — | — | Client-side server patches | — | NO-LIVE |
| Commerce PlayerStudioApprovals / Rejections / PLAYER_STUDIO_APPROVALS / _REJECTIONS | 3 / 3 | 3 / 3 | — | Peer review (commerce service) | Unknown | UNKNOWN |
| Commerce PlayerStudioCreatorSharePercent / PLAYER_STUDIO_CREATOR_SHARE | 40 | 40 | 40 | Creator share | 60/40 (18 Jun 2014) | OK |
| Commerce ImportFrom / ImportWorldId / ImportRealm / COMMERCE_IMPORT_* | "", 1, "" | lmemu_character, 1, lmemu_character | comment only | One-time copy | — | NO-LIVE |
| EnvServer HealQueue / ENVSERVER_HEAL_QUEUE | true | true | — | Destroyed ground heals | Live healed | OK |
| EnvServer RuinLayers, RuinLayerDesired / ENVSERVER_RUIN_LAYERS, _DESIRED | false, 0 | false, 0 | — | Ruin-layer records only | Live's ruin layers (L76, L127), records only | NO-LIVE (bookkeeping) |
| EnvServer Continent / ENVSERVER_CONTINENT | 406 | 406 | — | Home continent | must equal the world's Continent | NO-LIVE (§4 #13) |
| EnvServer TerrainLodCopies, KeepVertexOnlyVoxels / — | true, true | — | — | as the world's | as the world's | OK (§4 #12) |
| EnvServer ImportFrom, DataPath / ENVSERVER_IMPORT_FROM, ENVSERVER_DATA_DIR | "", Data | /world-data/TerrainEdits, /data | — | Import, store | — | NO-LIVE |
| Gateway HoldSeconds / GATEWAY_HOLD_SECONDS | 120 | 120 | — | Hold a connection while its zone is away | — | NO-LIVE |
| Gateway MultiWorld / GATEWAY_MULTI_WORLD | false | false | — | Cross-world travel | Live: on (F7) | FLIP (conditional) |
| Gateway WorldId / WORLD_ID; LoginInternalAddress / LOGIN_INTERNAL_ADDRESS; EncryptionKey | 0, "", client key | 1, landmarkemu-login:20210 | — | Routing | — | NO-LIVE |
| Social PresenceGraceSeconds / SOCIAL_PRESENCE_GRACE_SECONDS | 30 | 30 | — | Offline-notice delay on a world change | Ours (social-service.md §5.5) | NO-LIVE |
| Social ImportFrom / SOCIAL_IMPORT_FROM | "" | lmemu_character | comment only | One-time copy | — | NO-LIVE |
| WorldChannel TradeBroker / WC_TRADE_BROKER | true | true | — | Trade invitations in the world channel | Live architecture | NO-LIVE (OK) |
| WorldChannel EncounterBroker / — | true | **—** | — | Duel lifecycle in the world channel | Live architecture | NO-LIVE. Missing from compose |
| WorldChannel ClaimRatings / WC_CLAIM_RATINGS | true | true | — | Site likes in the world channel | Live architecture | NO-LIVE |
| WorldChannel ImportFrom / WC_IMPORT_FROM | "" | lmemu_character | — | Likes copy | — | NO-LIVE |
| Infrastructure-only compose vars: NETWORK_NAME, WEB_SUBNET_PREFIX, DB_PORT, LOGIN_PORT, ACCOUNT_PORT, WEBSITE_PORT, *_IMAGE, WORLD_KEY, WORLD_DATA_DIR | | | partly | | — | NO-LIVE |

## 4. Inconsistencies (code vs compose vs .env.example vs docs)

1. **WorldNameId.** The code defaults to 0 (keep the stored name). docker/world compose defaults to 62147 "Dev Server". .env.example sets 560916 Serenity. A second world started from docker/world without WORLD_NAME_ID lists as "Dev Server". With LANDMARKS_WORLD=auto, 62147 gives Courage's landmarks and 560916 gives Serenity's.
2. **WatchdogSeconds.** The code defaults to 0 (never). Compose and .env.example both use 180. A non-Docker install has no watchdog. Infrastructure only.
3. **HealthFile and PublicAddress.** The code defaults to "", while compose sets /tmp/world-alive and 127.0.0.1:19000. This is intended for Docker and harmless.
4. **WebPagesHost.** The code uses loopback by default. Compose sets 0.0.0.0. Intended.
5. **RegistrationsPerAddressPerHour.** WebApiConfig defaults to **10**. Auth compose and .env.example use **3**. A non-Docker auth server allows 10 per address.
6. **EventIsland documentation is stale.** The XML doc in WorldServerConfiguration.cs (lines 151-153) says "Default off until the islands' heights are baked", but the value is "auto". docker/world compose line 81 says "Off until the islands' heights are baked". .env.example correctly says "baked 2026-10-16. Default auto = live".
7. **ProceduralPlacement documentation is stale.** `trace/0695/procedural-placement-695.md` §5 and §6 say the switch defaults to **true** ("so the switch defaults on"). The code (Gathering.cs:237) defaults to **false**, and its comment gives the reason (alpha-era sets, L113).
8. **dev.env sets WEB_PORT=19001.** That name is not read: compose reads WEB_PAGES_PORT. It is harmless only because 19001 is also the default.
9. **ClientWorldNameText vs WorldNameId.** The in-game world name is always "Liberation" (560912) unless CLIENT_WORLD_NAME_TEXT is set. With .env.example's WORLD_NAME_ID=560916, the server list says Serenity and the Landscapes window says Liberation. CLIENT_WORLD_NAME_TEXT is not in .env.example. This is F6.
10. **PlayerStudioApprovals / Rejections.** The world reads its own keys (3 / 3) for in-process commerce, but docker/world compose does not forward them. PLAYER_STUDIO_APPROVALS and PLAYER_STUDIO_REJECTIONS exist only in commerce compose. A Docker owner without the commerce service cannot change them from .env. PLAYER_STUDIO_CREATOR_SHARE is forwarded to both (consistent: 40).
11. **WorldServer.example.json** lists only 13 of about 75 world keys. The values it lists match the code.
12. **TerrainLodCopies and KeepVertexOnlyVoxels** exist in both WorldServer and EnvServer configs. Neither compose file forwards them. With ENVSERVER_ADDRESS set, the EnvServer's own copy governs. An owner who changes WorldServer.json only would see no effect.
13. **Continent and ENVSERVER_CONTINENT.** The world's Continent has no env var (406 from code or json). The EnvServer's is ENVSERVER_CONTINENT (406). They must match but are set in two places.
14. **Season windows differ, on purpose.** LANDMARK_SEASON's Halloween runs 28 Sep - 5 Dec (drops: P6504 to P6565). EVENT_ISLAND's Halloween island runs 3 Oct - 20 Nov (island: P6532, forum 58077). These are different live sources, not a bug. They are listed here so nobody "fixes" them into one.

## 5. dev.env overrides (docker/worlds/dev.env)

| Key | dev value | Default | Note |
|---|---|---|---|
| WORLD_NAME_ID | 62147 | 62147 (comp) | — |
| LANDMARKS_WORLD | off | auto | Held back until the user decides (TESTING-NEEDED 10-16) |
| CAVERN_LANDSCAPES | home | all | Held back until verified (TESTING-NEEDED 10-16) |
| CAVERN_GRAVEYARD_STAMPS | true | true | Redundant (same as the default) |
| PROCEDURAL_RESPAWN | minmax | minmax | Redundant |
| WEB_PORT | 19001 | — | Not read (§4 #8) |
| DB_PASSWORD, GAME_DIR, GAME_DATA_DIR, PUBLIC_HOST, WORLD_DATA_DIR, ADMIN_* | (local) | | dev.env is git-ignored (.gitignore:351) |

Not overridden on dev, though these features write data. The project rule "data-writing features default off on dev" applies to them:
- **EVENT_ISLAND_EXPIRE_ON_CLOSE=true** expires sites on an island change.
- **TRIAL_KEYS=true** writes mail and account rows on first login. agent-membership.md §7.4 asked for TRIAL_KEYS=false on dev until tested.
- **FOUNDER_ENTITLEMENTS=true** sends mail.

They are on dev by default. If they have already been verified on the test stack, they are fine. Otherwise they need dev.env lines.

## 6. Settings a server owner needs that compose or .env.example lack

Docker compose forwards only the variables listed under `environment:`. A key that appears only in code cannot be set from `.env` without editing a compose file.

**In code, not in any compose file:**
- **MotdSubject / MotdBody**: every server will want its own MOTD.
- **FreeBuild**: creative servers.
- RestartWarningSeconds and RestartMessage.
- StoreLumenPrices__<id>.
- PlayerStudioApprovals / Rejections (world, in-process).
- **Auth CreationRules.StrictName**.
- Admins beyond three (only Admins__0-2 are mapped).
- Continent.
- ProceduralPlacement, ProceduralPlacementDecor and ProceduralPlacementMasks.
- KeepVertexOnlyVoxels and TerrainLodCopies.
- ShortTeleportDistance and ShortTeleportWaitScreen.
- AdminMonitorPush and EnvServerStartupWaitSeconds.
- WorldChannel EncounterBroker.
- WebApi LoginPort and PatchFolder.

**In compose, not in .env.example:**
- **MAX_PLAYERS**: the login queue cap, which an owner needs.
- **CLIENT_WORLD_NAME_TEXT** and CLIENT_WORLD_ID (§4 #9).
- PLAYER_STUDIO_APPROVALS and PLAYER_STUDIO_REJECTIONS.
- GATEWAY_MULTI_WORLD and GATEWAY_HOLD_SECONDS.
- WC_TRADE_BROKER, WC_CLAIM_RATINGS and WC_IMPORT_FROM.
- SOCIAL_PRESENCE_GRACE_SECONDS.
- ENVSERVER_HEAL_QUEUE, ENVSERVER_CONTINENT, ENVSERVER_RUIN_LAYERS, ENVSERVER_RUIN_LAYER_DESIRED, ENVSERVER_IMPORT_FROM and ENVSERVER_DATA_DIR.
- COMMERCE_IMPORT_FROM, COMMERCE_IMPORT_WORLD and COMMERCE_IMPORT_REALM. These, and SOCIAL_IMPORT_FROM, are mentioned only in comments.
- WORLD_DATA_DIR, GAME_DATA_DIR, LAUNCHER_DIR, NETWORK_NAME and WEB_SUBNET_PREFIX.
