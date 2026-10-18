# The admin / performance monitor (Alt+F) and its server pages

Traced 2026-10-15 in the 0.589 client. Status: pages and requests found; the retail page-selection path is still
not found (L3). **2026-10-16:** the page 28 / 29 replies and the web page /admin/monitor are built (untested in game):
full client trace and what was built in **admin-pages.md** (§2, §3, §11); docs in LandmarkEmu/docs/admin.md.

## Opening it

- InputProfile_Default.xml: `TogglePerformance` = **Alt+F** (also present: ToggleStats Ctrl+F4, StatsPrevious Ctrl+F6,
  StatsNext Ctrl+F7, ToggleDebugConsole Alt+~, ToggleVoxelCellStats Alt+B — but none of those names exist in the
  exe or in RefInputProfileActions.txt, so they are inactive leftovers in the retail client).
- The input handler FUN_142c80f20 looks up "Generic"/"TogglePerformance" (FUN_142f012f0) and flips the show flag
  `DAT_145353300 + 0x186` (DAT_145353300 = the BaseClientPerformanceMonitor). No admin check there.
- FUN_142cbd160(…, page): `page < 1` → hide (vtable +0x40 with 0); otherwise show (+0x40 with 1) and select the page
  (+0x30 → FUN_14317bc10). **No static reference to FUN_142cbd160** (only .pdata); how retail reaches it is
  **not found yet** → OPEN-LEADS L3.
- Monitor vtable (RTTI BaseClientPerformanceMonitor 1446621d0): +0x50 FUN_14317ae20 = current page (read by the input
  code for page 0x1C PageUp/PageDown scrolling, FUN in part_0065 line 46424, and page 0x1D in part_0107), +0x60
  FUN_143173b70 draw ("StatsScreenInsetX/Y", "Draw Entities"), FUN_143173540 header ("FPS: %i", "Kb/sec In/Out",
  "Disconnected.").

## The pages (FUN_14315a5f0 registers them: id, page count)

| Id | State | Data source |
|---|---|---|
| 4 | TimingDetailStatisticsState | client |
| 5 | TimingThreadsStatisticsState | client |
| 6 | RenderStatisticsState | client |
| 7 | MemoryStatisticsState | client |
| 8 | AudioStatisticsState | client |
| 9 | AnimationStatisticsState | client |
| 10 | DerivedClassStatisticsState | client |
| 11 | ZoneProfilerStatisticsState | server? (to trace) |
| 12 | ZoneStatisticsState | server? (to trace) |
| 13 | NetworkStatisticsState | client network |
| 14 | ControllerStatisticsState | client |
| 15 | FlashStatisticsState | client UI |
| 16 | AssetsStatisticsState | client |
| 17 | ActionEventQueueStatisticsState | client |
| 18-19 | InputControllerStatisticsState (2 pages) | client |
| 20 | MonitorAssetUsageState (FUN_14315b060) | client |
| 23 | ContentStatisticsState | client |
| 24 | GatewayStatisticsState | gateway profiler ("/gatewayprofiler on", "GATEWAY STATS") — to trace |
| 25 | CharacterStatusState | client / server? |
| 26 | VoxelDataState | client voxel |
| 27 | PacketMonitorState (FUN_14315b980) | client packet stats (packet_monitor_* settings) |
| 28 | ProceduralDataMonitorState (FUN_14315be00) | server: ProceduralDataMonitorPacketUpdateRequest / Response |
| 29 | InstancedGameMonitorState | server: InstancedGameAdminMonitorPacket ("Instanced Game Information - Server") |
| 30 | StorybricksAiDataState (FUN_14315bf90) | server? Storybricks AI |
| 32 | PathingMonitorState (FUN_14315ba50) | server: AdminCommandPacketPathingDataMonitorUpdateRequest |
| 33 | EnvironmentServerMonitorState | server: AdminCommandPacketEnvServerDataMonitorUpdateRequest |

## Requests the server pages send (all on the Zone channel: send templates with channel 0)

| Page | Request | When |
|---|---|---|
| 33 Environment Server | `AdminCommandPacketEnvServerDataMonitorUpdateRequest`: Command 0x1E, sub 0x4AB, string "EnvServer" (FUN_143170930) | only if `(*DAT_145351c48 + 0xB0)()` is true. DAT_145351c48 = the client object (set in FUN_142c4fb20); vtable +0xB0 of both BaseClient and PlayClient = FUN_142a87790 = **return 0** → never sent by the retail client |
| 32 Pathing | `AdminCommandPacketPathingDataMonitorUpdateRequest`: 0x1E sub 0x4A9, string "Pathing" | on a timer while the page updates (part_0091 ~line 14183) |
| 28 Procedural data | `ProceduralDataMonitorPacketUpdateRequest`: opcode 0x109 | on a timer (FUN_143171e20) |

## Two different "admin" checks (traced 2026-10-15)

1. **Admin client** = `(*client + 0xB0)()` (client object DAT_145351c48). In BaseClient and PlayClient that slot is
   FUN_142a87790 = `return 0`: the retail exe is never an admin client. It gates:
   - which Lua script set loads at startup (FUN_142c78c20: `"Admin"` → Resources/Scripts/admin.lua with the debug
     console and 65 extra commands, else `"Empty"`) — retail loads "Empty"; admin.lua ships but never runs;
   - the UI's `IsAdminClient` (Lua binding FUN_14351a5d0 → +0xB0; every widget's GameUtils.IsAdminClient);
   - the Environment Server page request (FUN_143170930);
   - the world map admin features (FUN_142fb4680 / FUN_142fb4a90 "PlaceWaypoint" "WorldMap": they ask the client
     object first and fall back to the player flag only when there is no client object, i.e. never in game).
2. **Admin player** = a bool the server sends in **SendSelfToClient** (SendSelfToClient_Read 142b80420 → player data
   +0x7aea8; the emulator's `SendSelfToClient.IsAdminMaybe`, currently never set → false). Checked as
   "admin client OR admin player" in:
   - admin Command subs ≥ 0x42A (FUN_14330d470, e.g. AdminCommandPacketClientTime 0x1E / 0x42A, "ZoneCommand"):
     only run for an admin;
   - FUN_142e506b0 (generic is-admin, no static caller), FUN_142e4f670 (chat formatting with "[Guild] ");
   - the voice console (FUN_14302b750: `DAT_145370c88 || flag`);
   - voxel paste (FUN_1430671e0 "PasteVoxel": an admin adds a clipboard option from client +0x32098 +0xC);
   - the input handler FUN_142c80f20 (admin player → the "Demo" input action);
   - FUN_142c77390: a non-admin sends CommandPacketHexPermissions (0x1E / 0x34) where an admin doesn't.

So on the unmodified retail client the server **can** make a player an admin (flag in SendSelf), which unlocks the
admin command handling and the in-world admin options above. No path has been found yet that turns on the admin
client in the 0.589 retail exe (Lua admin console, IsAdminClient UI, Environment Server page, admin map): open lead
— check the 0.695 decompile and runtime before concluding (OPEN-LEADS).

## Replies that feed the server pages

| Page | Reply | Gate on the client | Layout |
|---|---|---|---|
| 29 Instanced games (**built 2026-10-16**, pushed to admins with AdminMonitorPush, default off) | `InstancedGameAdminMonitorPacket`: InstancedGames base 0x10D, u32 sub 2 (FUN_14343c3d0 case 0, reader FUN_143427160) | none: shown whenever page 29 is the current page (FUN_143172020 copies it into the page) | `u16 0x10D, u32 2, u32 n, n × {string key, string value, u32 indent}`; drawn by FUN_143164bd0 as `"%*s%s %s"` (indent, key, value) under "Instanced Game Information - Server" |
| 28 Procedural data (**built 2026-10-16**: 0x109 answered for admins; layout and ours-mapping in admin-pages.md §3 / §11) | `ProceduralDataMonitorPacketUpdateResponse`, **opcode 0x10A** (ctor FUN_142bcf5d0; Zone_DispatchPacket slot 0x86, reader FUN_142bb4ba0) | none: copied into the page (FUN_14316d0d0) whenever page 28 (0x1C) is current | u16 0x10A, u32 +0x20, u32 +0x24, list (FUN_142b9c5d0), u32 +0x28 / +0x2C / +0x30, list (FUN_142b9dbb0), list (FUN_142b9dcc0) — element layouts to finish; the class names the server tables **ProceduralPlacementEntityLists.txt** and **ProceduralPlacementEntityListEntities.txt** (not shipped with the client) |
| 32 Pathing | reply to 0x1E / 0x4A9 | not found yet in 0.589: the metrics array is swapped in by FUN_14316f010, which has no static caller; no handler compares subs 0x4A9-0x4AD → OPEN-LEADS L4 | — |
| 33 Environment Server | reply to 0x1E / 0x4AB (the 0.589 request is gated by the admin-client check) | — | not found yet → OPEN-LEADS L4 |

## To do

1. Find how retail selects a page (runtime: Frida on FUN_14317bc10 / FUN_142cbd160 while pressing Alt+F; or a
   server-sent command) — no guessing.
2. For each server page: the reply packet, its reader layout (pkt.py), and how the page draws it
   (ClientEnvironmentServerMetrics::LayerInfo, ClientPathingMetrics::ClientPathMetricEntry, ProceduralDataMonitor
   response, InstancedGameAdminMonitorPacket key/values, zone / gateway profiler stats, Storybricks).
3. Pages 28 / 32 send their requests without either admin gate — confirm at runtime that a normal player
   client sends them while the page is shown.
4. Then: answer each request from the matching service (zone, environment server, pathing, procedural spawner).

## 0.695: every use of the admin-player flag (player +0x7b0d8; scan 2026-10-16, scratchpad adminflag.py)

The flag is read from SendSelfToClient (FUN_142c19bd0 part_0059, last bool before the Bio) and checked as "admin client
OR admin player" (`cVar || DAT_145431a20->+0x7b0d8`) in 37 functions. Since 2026-10-16 the server sets it for the
WorldServer.json "Admins" (WorldSession SendSelf). What it unlocks, with the packet each command sends:

| Client function | Command / feature | Sends (Command 0x1E sub unless noted) |
|---|---|---|
| FUN_1433a7d70 | /goto player \| waypoint ("You must exit your vehicle to use the /goto command.") | 0x42E AdminCommandPacketGotoRequest |
| FUN_1433ac520 | /hideme | 0x458 AdminCommandPacketHideMe |
| FUN_1433b2a20 | /runspeed inc \| dec \| default \| n ("Player run speed set to %.3f") | 0x4D8 AdminCommandPacketRunspeed |
| FUN_1433b3d00, FUN_1433b7350 | /gametime [pause] | 0x429 AdminCommandPacketGameTime |
| FUN_1433b55e0, FUN_1433ba260 | /clienttime advance \| pause \| reverse | 0x42A AdminCommandPacketClientTime |
| FUN_1433a7420, FUN_1433bf9b0 | /target name \| clear ("Admin Target set to %s Guid: %llu") | 0x4B5 AdminPacketVerifyAdminTarget (both ways: the reply prints "Admin Target set to Guid: %llu") |
| FUN_1433b1e50 | /prop id ("Trying to summon prop with prop ID %d"), reset_zone_and_wipe_db_forever_with_no_undo | 0x4EA AdminCommandPacketSpawnProp, 0x4ED AdminCommandPacketDeleteAllProps |
| FUN_1433b6030 | /vehicle id [auto mount] [faction] | 0x49C AdminCommandPacketSpawnVehicle |
| FUN_1433a3170 | /addammo addto | 0x499 AdminCommandPacketAddAmmo |
| FUN_1433a5e60 | /friend forceadd \| remove \| friendpositions \| client | 0x4CF / 0x4D0 ForceAdd/RemoveFriendRequest, 0x40D CommandPacketFriendsPositionRequest |
| FUN_1433b0810 | /profile setrank \| setxp \| select \| list \| group | 0x3EC AdminCommandPacketProfile* |
| FUN_1433acb90 | claim item collect \| uncollect \| vault \| unvault (target guid) | 0x434, 0x48A, 0x4F2, 0x4F3, 0x4FE, ClaimsPacketIdClaimRequestMoveItemToPlayer |
| FUN_1433b02d0 | placed effect command | 0x4EF AdminCommandPacketPlacedEffect |
| FUN_1433b4110 | /setmodel id | 0x4F0 AdminCommandPacketSetModel |
| FUN_1433a5d80 | free-fly hide | 0x4F1 AdminCommandPacketFreeFlyHide |
| FUN_1433b64e0 | weapon set command | 0x4EE AdminCommandPacketWeaponSet |
| FUN_1433a4820 | /clear resource ("That is not a valid resource type.") | client-side |
| FUN_1433af380 | /loc (prints position, POIId, ZoneAreaDefinitionId); /location x y z [heading] moves the player | printing is client-side; the move sends CommandPacketTeleportToLocation 0x1E/0x51 (0x52 with a new heading) through FUN_1433c80a0, which freezes the player until a TeleportToLocation answers (corrected 2026-10-18, agent-l140.md; server: Game/TeleportRequests.cs) |
| FUN_1433a5650 | Demo mode on/off; FUN_142d1a5b0 input "Demo" action | client-side |
| FUN_1433a5710 / FUN_1433a5990 / FUN_1433a5ca0 / FUN_1433b4380 / FUN_1433b6ca0 | radius / fog / foreground scale / shadow quality / near-far | client-side rendering |
| FUN_1433b5430 | teleport player to camera | CommandPacketTeleportToLocation 0x1E/0x51 through FUN_1433c80a0 (not a client-side move: the client waits for the server's TeleportToLocation; corrected 2026-10-18, agent-l140.md; server: Game/TeleportRequests.cs) |
| FUN_1433b3180 | SOEmote webcam | client-side |
| FUN_142d10ad0 | HexPermissions 0x34 is NOT sent by an admin | — |
| FUN_142eee400 | chat "[Guild] " formatting | — |
| FUN_143054920 | world map Admin / MapTeleport / PlaceWaypoint | map teleports: ground-clamped point (FUN_142d0a0d0) → 0x1E/0x51 via FUN_1433c80a0 (needs +0xB0 or +0x7b0d8) |
| FUN_143111480 | PasteVoxel: Character.Templates / PropCount clipboard option | — |
| FUN_1430d1b00 | debug text overlay ("** HANDS OFF **", session list) | — |
| FUN_1433bc350 | /who reply (CommandPacketWhoReply) formatting | — |
| FUN_14335fbb0, FUN_1433c25f0, FUN_142eef440, FUN_1430544f0 | small gates (not named) | — |
