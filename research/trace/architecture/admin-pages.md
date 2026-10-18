# Admin monitor pages: the page 28 / 29 replies and the server-side web admin page (agent #17)

Phase B item 7 (BUILD-PATH-0695.md). Client evidence only; addresses are **0.589 / 0.695** (re/decomp_all /
re/decomp_all_695), the 0.695 partner taken from trace/0695/functions.tsv (every function cited here has `code = same`
there unless marked). "Ours" = our design, not live's; "inference" = not proven by code. Nothing was run (no game,
server or Frida). Scratch: re/out/agent_admin/ (f_*.c function dumps, pagestates_589.tsv, pagestrings_589.txt = every
page's draw strings, rstr.py = read strings / constants from the exe).

## Summary

| Question | Answer |
|---|---|
| Can a retail client show pages 28 / 29? | No (L3, unchanged): the paged view flag +0x342a8 is set only by an unreferenced debug body. Both replies are still built to the client's reader so a dev-only Frida hook (§8) can prove them. |
| Page 29 reply | **0x10D sub 2 InstancedGameAdminMonitorPacket** = `u16 0x010D, u32 2, u32 n, n × {string key, string value, u32 indent}`. No request exists: the server must **push** it. Accepted only if read without error and fully consumed, and only while page 29 is current. Printed as `"%*s%s %s"` (2 × indent wide "-" marker, key, value), colour by indent % 6. Same bytes for 0.589 and 0.695 (sub 2 < 0xD, no ClientWire695 shift). |
| Page 28 reply | **0x10A ProceduralDataMonitorPacketUpdateResponse** (layout from agent-L1-L2.md, re-verified with pkt.py), answering **0x109** (bare `u16 0x0109`, no body), which the page sends **every 5 s** on channel 0. New here: the exact header line, the draw filters (an entity list is drawn only if its id is in the Lists table and its entities' spawn total > 0; an entity only if its id is in the ListEntities table and its count > 0), colours, scrolling. |
| Every page 4-33 | 26 page states, 30 page numbers (§3). Server data on **11, 12** (zone, text blob), **24** (gateway, text blob), **28, 29** (zone, binary readers), **30-31** (Storybricks, admin client only), **32** (zone pathing), **33** (Environment Server); 25 / 27 show client copies of server-fed data. Only 28 / 29 have readers in retail (L4). |
| Where the emulator has the data | §5: zone tick / per-system timing (HostedService.cs), players (PlayerManager), packets (OpcodeStats), site games (InstancedGames.cs: rich), gathering / creatures / veins / outposts (partial procedural analogues), terrain layers / heals (TerrainEdits, TerrainLod). Missing: pathing (none), Env Server job counters, gateway per-signature profiler, a ProceduralPlacement spawner. |
| Web admin page | Per world, in the world's existing `Game/WebPages.cs` + `Game/Admin.cs` (`/admin`, Admins config, Showcase token), new read-only `/admin/monitor` + `/api/admin/monitor/<section>`; other services answer an internal `/status` JSON the world page fetches (§6). |
| New open leads | L81 (live page-29 content and push trigger), L82 (page-28 field meanings / live layer-node model), L83 (Storybricks pages on Landmark's server). |

## Existing research used

- trace/architecture/admin-monitor.md — Alt+F, page ids, requests (33 EnvServer 0x1E/0x4AB admin-gated, 32 Pathing
  0x1E/0x4A9, 28 Procedural 0x109), the two admin checks, first reply table. Corrected / extended below.
- trace/0695/architecture-leads.md L3 (no retail path to the paged view; FUN_142d56520 / 0.589 FUN_142cbd160 is the
  only setter, unreferenced) and L4 (no readers for pages 11, 12, 24, 30, 32, 33; only 0x10A and 0x10D sub 2 are read).
- trace/0695/agent-L1-L2.md Task 3 — the whole 0x10A wire layout, the two server-only table schemas
  (ProceduralPlacementEntityListEntities / EntityLists), the graph series read from disassembly
  (re/out/agent_l1/dis_1431685b0.txt), 0.695 partners (all identical). Not re-traced; spot-verified with pkt.py
  (FUN_142b937a0, FUN_142b94430, FUN_142b7ece0, FUN_142b9c300, FUN_142b9c470, FUN_142b85720, FUN_142b8baa0,
  FUN_142b8bc30): matches.
- trace/architecture/operations.md — the user's decision (admin data in a server-side / web page), Environment Server
  page fields (handed to #14), health / watchdog.
- trace/architecture/terrain-service.md §1.1 / §5.2 (#14) — page 33 struct (`ClientEnvironmentServerMetrics`: 19 job
  types Active / Pending, Total, ruin layers) and the proposed 0x1E/0x4AC body; gateway-service.md §1.4 (#13) — the
  gateway profiler record (count, totalNano, totalRecipients, gatewayHeaderBytes, applicationBytes, per signature and
  direction); world-channel-service.md §6.6 (#15) — "a per-world web endpoint for the admin page" kept open.
- trace/families/0109_ProceduralDataMonitor.md, 010A_ProceduralDataMonitor.md, 010D_InstancedGameEndRequest.md,
  trace/FAMILIES-SUMMARY.md, trace/0695/coverage-695.md rows 0x109 / 0x10A / 0x10D sub 2 ("same", not handled /
  not sent) and Command 0x1E 0x4A9 / 0x4AB / 0x4FC ("family handler, sub not handled", InteractionHandler.cs:33).
- trace/0695/OPEN-LEADS.md L2, L3, L4, L48, L69, L74, L76.

### Corrections to existing research

1. **InstancedGameAdminMonitorPacket is sub 2, not 4.** FUN_14343c3d0 / FUN_1434ee920 switch on `sub - 2`
   (f_14343c3d0.c line 188: `uVar6 = local_6a0 - 2`), case 0 builds the packet with `local_838 = 2`. The generated
   files 010D_InstancedGameEndRequest.md ("0x10D, 0x4") and FAMILIES-SUMMARY.md ("✗0x4 InstancedGameAdminMonitorPacket",
   "✗0x6 InstancedGamePropRecipePacket") add the offset twice; coverage-695.md (0x2, PropRecipe 0x4) is right.
2. **Page numbers**: MonitorAssetUsageState has 3 pages (**20-22**, ctor FUN_14315b060 sets id 0x14, count 3);
   StorybricksAiDataState has 2 (**30 summary, 31 selected-character detail**, FUN_14315bf90: 0x1E, count 2;
   draw FUN_14316b340 checks page 0x1E / 0x1F). admin-monitor.md lists only 20 and 30.
3. **Page 28 header** is one line: `"Zone Spawn Count: %d (%d%% of nodes)  F:%d  N:%d  P:%d"` with args +0x20,
   (+0x20 × 100.0 / +0x24), **F = +0x30, N = +0x28, P = +0x2C** (FUN_1431685b0, f_1431685b0.c ~119); the second line is
   `"Zone Node Count: %d"` (+0x24).

## 1. Common mechanics (both builds)

| Item | 0.589 | 0.695 |
|---|---|---|
| Monitor object (BaseClientPerformanceMonitor) | DAT_145353300 | DAT_145432880 |
| Current page (vtable +0x50) | FUN_14317ae20 | — (slot of 0x14472a8c8) |
| Current page state | FUN_14317ae30 | FUN_143225850 |
| Set page (+0x30) / set paged view (+0x40) / toggle (+0x38) | FUN_14317bc10 / FUN_14317bc00 / FUN_14317bcc0 | FUN_143226630 / FUN_143226620 / FUN_1432266e0 |
| Monitor update (+0x18; runs the current page's update only with +0x342a8) | FUN_14317bce0 | FUN_143226700 |
| Page registration (ids, page counts) | FUN_14315a5f0 | FUN_143205010 (same set of 26 states) |
| Draw text call | FUN_142dbc330(font, text, scale, y, ARGB) | same |

Page state vtable slots: +0x20 update (request sender), +0x28 draw. Timers use ms × 0.001 (DAT_144b72d94) against a
threshold: **5.0 s** for page 28 (DAT_1445d50e0), **0.2 s** for pages 32 / 33 (DAT_1445cc500).

## 2. Page 29 — InstancedGameAdminMonitorPacket (0x10D sub 2)

**Dispatch.** Zone family 0x10D → ClientInstancedGameManager::ProcessPacket FUN_14343c3d0 / FUN_1434ee920; switch on
`sub − 2`, case 0. Reader FUN_143427160 / FUN_1434d6be0. Then, only if (a) the reader set no error, (b) **no bytes
are left** (`end − cursor < 1`), (c) the monitor exists and (d) the current page is **0x1D (29)**, the KeyValue array is
copied (FUN_143172020 / FUN_14321ca40) into the global `DAT_14516a648` (count DAT_14516a658, data DAT_14516a650),
which the page keeps drawing until the next accepted packet. No page-29 request exists: the state's update slot is
empty (FUN_143171880 / FUN_14321c2a0 `return`), so the server pushes it.

**Wire (little endian; string = u32 length + bytes, no terminator; a negative length sets the error flag):**

```
u16  <computed>
u32  2             sub
u32  n             KeyValue count
n ×  string key    element +0x00 (IString)
     string value  element +0x18 (IString)
     u32  <computed>
```

**What page 29 prints** (draw FUN_143164bd0 / FUN_14320f5f0):

1. Header `"Instanced Game Information - Server"` in colour table entry 0.
2. Per element: `sprintf("%*s%s %s", indent > 0 ? indent*2 : 0, indent > 0 ? "-" : "", key, value)` — i.e. the "-"
   marker right-aligned in a field 2 × indent wide, then key, one space, value. Colour =
   `DAT_14516a688[indent % 6]`: 0 `0xFFFF7777` (light red), 1 `0xFFFFFF00` (yellow), 2 `0xFF00FF00` (green),
   3 `0xFF00FFFF` (cyan), 4 `0xFF7777FF` (light blue), 5 `0xFFFF77FF` (pink) (static .data, read from the 0.589 exe).
   So key "Players", value "3", indent 1 → ` -Players 3`.
3. Header `"Instanced Game Information - Client"`, then the client's own block built by FUN_1434362a0 / FUN_1434e65d0
   from ClientInstancedGameManager (DAT_145358b90), all indent 0: `State` (%d, mgr +0x28), `DesiredState` (+0x2C),
   `Authoring GameGuid` (%llu, +0x30), `QueuePosition` (+0x80), `QueueSize` (+0x84), `GameEnabled` (u8 +0x8C),
   `Claim Game State: ` (+0x28), `   Desired State: ` (+0x2C), `   Ms2Next State: ` (+0x48).

**Live content: not found yet** (keys, nesting, which games, who received it, how often) → **L81**. The client only
proves the container (an indented key / value tree) and shows live's naming style in its own block.

**Our content (ours, mirrors the client's key style).** One tree per site game known to the zone:

| indent | key | value (from InstancedGames.cs) |
|---|---|---|
| 0 | `Game` | `<guid> <rule set name or custom name>` (Game.Guid / Rules.Name / CustomName) |
| 1 | `State` / `DesiredState` | `6 (Running)` — numeric like the client block, then the name (StateNone…StateRestarting) |
| 1 | `Site` / `Landscape` | ClaimId / Zone |
| 1 | `GameEnabled` | `1` / `0` (!Disabled) |
| 1 | `Players` / `MaxPlayers` / `MinPlayers` | Players.Count, Rules.MaxPlayers / MinPlayers |
| 1 | `QueueSize` | Queue.Count |
| 1 | `Observers` / `Editors` | Observers.Count / Editors.Count |
| 1 | `RoundEnds` / `StateEnds` | seconds left |
| 1 | `TeamScores` | `a / b` |
| 2 | `<character name>` | `team t, K/D/S k/d/s` (+ `respawning` / `arriving`) |

Rules: only admins receive it (Admin.IsAdmin), only while they asked for it (§7), at most every 2 s, and the whole
packet must be consumed exactly (no trailing bytes), or the client drops it.

## 3. Page 28 — ProceduralDataMonitorPacketUpdateResponse (0x10A) and its request (0x109)

**Request.** Page state update FUN_143171e20 / FUN_14321c840 every 5 s: `ProceduralDataMonitorPacketUpdateRequest`
(vtable has only a destructor), sent by the generic tunnel sender FUN_143159870 with channel 0 (Zone): gateway
PacketTunnelPacketFromExternalConnection then **`u16 0x0109` and nothing else**. Not handled by the emulator today
(coverage-695.md; it reaches WorldSession's raw path because ZoneMessageOpcode.cs:567 keeps 0x10900, see :555).

**Dispatch of the reply.** Zone_DispatchPacket case 0x86 (0.589 part_0065:46419 / 0.695 part_0066:15661): ctor
FUN_142bcf5d0 / FUN_142c69030, reader FUN_142bb4ba0 / FUN_142c4e3b0 (returns 1 only with no error and **no bytes
left**), copy FUN_14316d0d0 / FUN_143217af0 → FUN_14315dd60 / FUN_143208780 only if the current page is **0x1C (28)**.

**Wire layout** — agent-L1-L2.md "0x10A … wire layout" (authoritative; verified). In short:

```
u16 <computed>
list layers {u32 LayerId; u32 queued; list biomes {u32 BiomeId; u32 used; u32 unused; f32 value;
             list entityLists {u32 EntityListsId; list entities {u32 EntityListEntitiesId; u32 a; u32 nameTextId;
                               u32 spawnCount; u32 min; u32 max; u32; u32; u8 bool}; u32 nodeCount}};
             u32 available; u32 belowMin; u32 aboveMin; u32 overMax};
u32 <computed>
list EntityListEntities rows {u32 key; ID, ENTITY_LIST_ID, ENTITY_LIST_USE_TYPE_ID, ENTITY_ID, ENTITY_GROUP_ID,
                              MIN_SPAWNS_PER_ZONE, MAX_SPAWNS_PER_ZONE, f32 SPAWN_WEIGHTING, LAYER_TIER_WHITELIST_ID};
list EntityLists rows {u32 key; u32 ID; string NAME; u32 LAYER_TIER_WHITELIST_ID}
```
(every list = `u32 count, count × {u32 key, element}`).

**What page 28 prints** (FUN_1431685b0 / FUN_143212fd0, FUN_1431682f0 / FUN_143212d10, FUN_1431680f0 / FUN_143212b10):

| Line / graph | Text / format | Fields | Colour |
|---|---|---|---|
| help | "Use PageUp and PageDown to scroll through the entity lists!" / "Yellow/Blue signify complete spawning, Red/Purple signify incomplete spawning." | — | — |
| header 1 | `Zone Spawn Count: %d (%d%% of nodes)  F:%d  N:%d  P:%d` | +0x20, +0x20·100/+0x24, F +0x30, N +0x28, P +0x2C | yellow `0xFFFFFF00` |
| header 2 | `Zone Node Count: %d` | +0x24 (send ≠ 0: it is the % divisor) | yellow |
| graph "Layer Queue" (y "nodes") | one history series per layer, named `LayerId %d` | layer `queued` | — |
| per layer graph (y "nodes") | series "Available", "Below Min", "Above Min", "Over Max" | layer +0x38 / +0x3C / +0x40 / +0x44 | — |
| per biome graphs | series `BiomeId %d` on a "%" graph and on a "Spawns" graph; "Used" / "Unused" on a "nodes" graph | biome f32, used, unused | — |
| entity list title | `%d %s\n` = row ID, NAME of the EntityLists row | +0x98 table | cyan `0xFF00FFFF` |
| entity list line | `List Spawn Count: %d, Min/Max (Per Biome): %d / %d, Nodes Remaining: %d` | Σ count, Σ min, Σ max, max(0, list nodeCount − Σ count) | cyan if Σmax ≤ Σcount else purple `0xFFFF55FF` |
| entity title | `%d %s\n` = entity `a`, text of `nameTextId` (ClientStringProvider DAT_145351bf8) | — | yellow |
| entity line | `Spawn Count: %d, Min/Max: %d / %d` | count, min, max | yellow if max ≤ count else red `0xFFFF0000` |

Draw filters (server must respect them or nothing shows): an entity list is drawn only if its key is present in the
**EntityLists** rows (+0x98) and the sum of its entities' spawn counts is non-zero; an entity line only if its key is
present in the **EntityListEntities** rows (+0x68) and its spawn count is non-zero. Scrolling: state +0x18 = first
list shown (PageUp / PageDown), +0x1C counts drawn lists. The `.txt` names at +0x10 / +0x18 are labels only.

Field meanings not proven (N / P / F, entity `a`, the two undrawn u32 and the bool, the biome f32, what "queued",
"node" and "layer" were on live) → **L82** (row data stays L2).

## 4. Every monitor page 4-33: what it shows, and who owns the data

0.589 draw / 0.695 draw. "Client" = the client's own data; for those the web page shows the closest server-side
analogue only where it helps an admin (marked "analogue, ours").

### 4.1 World / zone server (coordinator; channel 0 Zone)

| Page | State, draw | Shows (labels → meaning) | Feed on live | Web admin section |
|---|---|---|---|---|
| 11 | ZoneProfilerStatisticsState, FUN_14316be60 / FUN_143216880 | `ZONE PROFILER DATA` — a server-made **text blob** from PlayClient +0x2F0 (returns 0 in retail → "No zone profiler stats.  Use '/profiler on' to enable.") | `/profiler on` → 0x1E/0x450 AdminCommandPacketIdProfiler (id only, L4) | Zone profiler: per update system count / avg / min / max µs (ours, columns borrowed from page 32) |
| 12 | ZoneStatisticsState, FUN_14316bf40 / FUN_143216960 | `ZONE STATS` — text blob from PlayClient +0x2F8, same fallback text | same command (L4) | Zone stats: players per landscape, tick avg / max ms, fps (ours) |
| 25 | CharacterStatusState, FUN_1431634b0 / FUN_14320ded0 | `CHARACTER STATUS`: the target's flag names (FUN_142e058a0: "Visible" …), `SERVER TIME` block `Offset: %d, interp_delay %f, status updates queued: %d, Ping: %d`, `WEAPON STATUS` (Primary / Secondary Weapon), `Physics Contact Material: %s (%d)`, resources `%s: %d of %d` | client copy of server-sent character data | Player inspector (analogue, ours): resources, position, landscape, latency |
| 27 | PacketMonitorState, FUN_1431667a0 / FUN_1432111c0 | `PACKET MONITOR`: cPacketId, PacketName, Count, Total(b), Avg(b), Worst(b), Rate(b/s), TotalMS, AvgMS, WorstMS; "MODE: Bandwidth / Processing time"; Active (packet within %i s) / Inactive | client | Packet table per opcode (analogue, ours, from OpcodeStats) |
| 28 | ProceduralDataMonitorState, FUN_1431685b0 / FUN_143212fd0 | §3 | **0x109 → 0x10A** (reader in retail) | Procedural data (same layout as §3) |
| 29 | InstancedGameMonitorState, FUN_143164bd0 / FUN_14320f5f0 | §2 | **0x10D sub 2 push** (reader in retail) | Instanced games (same tree) |
| 32 | PathingMonitorState, FUN_143167ad0 / FUN_1432124f0 | `=========Pathing Information - Zone=========`; if no data `    Nothing to display at this time.`; `----------Pathing Summary----------`: NavMeshCells (+0xD10), PendingStitchSections (+0xD14), Characters (+0xD18), DynamicSilhouettes (+0xD1C), AvoidanceObstacles (+0xD20), NavMeshJobsRemaining (+0xD24), NavMeshJobsCompletedThisFrame (+0xD28), NavMeshJobsCompletedLifetime (+0xD2C); `----------Performance Summary----------`: `Zone avg fps` (= 1000 / avg ms), `Zone avg frame ms` (+0x20), `Zone max frame ms` (+0x24), `World Step avg fps / avg frame ms (+0x28) / max frame ms (+0x2C)` (green `0xFF35F600` at ≥ 10 fps, else red `0xFFB11623`); table `-----Name----- -Count- -AvgUs- -MinUs- -MaxUs-` from 0x60-byte entries {name +0x08, count +0x40, avg µs +0x48, min +0x50, max +0x58}, rows with count > 0 | request 0x1E/0x4A9 "Pathing" every 0.2 s (FUN_143171d50 / FUN_14321c770); reply 0x4AA has no reader (L4) | Pathing (same fields; emulator has no pathing → "Nothing to display at this time.") |
| 30-31 | StorybricksAiDataState, FUN_14316b340 / FUN_143215d60 | `STORYBRICKS AI SUMMARY` (30) / `STORYBRICKS AI SELECTED CHARACTER DETAIL` (31): two text blobs (+0x48, +0x60) | 0x1E/0x4FC every interval, **admin client only** (FUN_143171ee0 / FUN_14321c900) | none (L83) |

### 4.2 Gateway (#13)

| Page | State, draw | Shows | Feed | Web admin section |
|---|---|---|---|---|
| 24 | GatewayStatisticsState, FUN_143164670 / FUN_14320f090 | `GATEWAY STATS` — text blob from PlayClient +0x300, else "No gateway profiler stats.  Use '/gatewayprofiler on' to enable." | gateway command `/gatewayprofiler` (opcode 9, L5) + 0x1E/0x4D7 GatewayProfilerRegistration (id only, L48) | Gateway profiler: per (direction, signature) count, totalMs, totalRecipients, gatewayHeaderBytes, applicationBytes; Incoming / Outgoing totals, uniquePacketCount (gateway-service.md §1.4, FUN_143c79940 / FUN_143c7b210 / FUN_143c7b580) |
| 13 | NetworkStatisticsState, FUN_143166390 / FUN_143210db0 | `NETWORK`: Physical / Logical bytes/sec In/Out, Total bytes, ReliablePing / ZonePing, PlayerPositionUpdate and ManagedPositionUpdate outbound rates, Estimated server time / Offset, PendingAssets, simulated latency | client | Connections (analogue, ours): per player bytes in / out, packets / s, ping |

### 4.3 Terrain + Environment Server (#14)

| Page | State, draw | Shows | Feed | Web admin section |
|---|---|---|---|---|
| 33 | EnvironmentServerMonitorState, FUN_143163fe0 / FUN_14320ea00 | `========= Environment Server Information =========`; `----------Job Summary----------` 19 rows `%50s: %10d%10d` (name, Active, Pending) + Total Jobs; `----------Ruin Layer Info----------` `Layer: n  Desired: u  Active: u  Failed: u  Last ID: d`; else "Nothing to display at this time." (terrain-service.md §1.1) | 0x1E/0x4AB every 0.2 s, **admin client only** (FUN_1431717d0 → FUN_143170930); reply 0x4AC no reader (L4) | Environment Server (same fields) |
| 26 | VoxelDataState, FUN_14316b8d0 / FUN_1432162f0 | `VOXEL STATS`: CellId / LOD / Cell Position, Cells / Cell Bytes / Modified Cells / Registered / Pending / Ready / Remote, Contexts / Entities / Meshes / Verts / Colliders, Material Id, Player CellId / GeoLoc | client | Terrain store (analogue, ours): modified cells per layer, pending heals, LOD copies |

### 4.4 Social (#12) and world-channel (#15)

No monitor page draws social or world-channel data (none of the 26 states reads friends, guilds, chat, mail, commerce,
ratings, trades or lobbies). Their web sections are ours (§6.3).

### 4.5 Client-only pages (nothing server-side)

| Page | State (draw 0.589 / 0.695) | Shows |
|---|---|---|
| 4 | TimingDetailStatisticsState (FUN_14316b510 / FUN_143215f30) | TIMING STATISTICS: frame µs, unaccounted, GiveTime per system |
| 5 | TimingThreadsStatisticsState (FUN_14316b8c0 / FUN_1432162e0) | empty body in both builds |
| 6 | RenderStatisticsState (FUN_143169d50 / FUN_143214770) | RENDER STATISTICS: DIP / prims / state changes, actors, particles, tile Lng / Lat / Ecos / LOD |
| 7 | MemoryStatisticsState (FUN_143165290 / FUN_14320fcb0) | MEMORY: device / asset / animation / audio memory |
| 8 | AudioStatisticsState (FUN_143163070 / FUN_14320da90) | AUDIO: voices, driver CPU, sample bytes |
| 9 | AnimationStatisticsState (FUN_1431626b0 / FUN_14320d0d0) | ANIMATION: active nodes, variables |
| 10 | DerivedClassStatisticsState (FUN_143163ea0 / FUN_14320e8c0) | DERIVED STATISTICS: text from PlayClient +0x2E8 ("No Stats From Derived Class") |
| 14 | ControllerStatisticsState (FUN_143163d80 / FUN_14320e7a0) | CONTROLLER |
| 15 | FlashStatisticsState (FUN_143164530 / FUN_14320ef50) | FLASH: count, update / emit µs |
| 16 | AssetsStatisticsState (FUN_143162f20 / FUN_14320d940) | ASSETS: pending assets |
| 17 | ActionEventQueueStatisticsState (FUN_143162590 / FUN_14320cfb0) | ACTION EVENT QUEUE |
| 18-19 | InputControllerStatisticsState (FUN_143164750 / FUN_14320f170) | INPUT: action sets, key states, mouse deltas |
| 20-22 | MonitorAssetUsageState (FUN_143165660 / FUN_143210080) | ASSET USAGE / SUMMARY / UNUSED: textures, render targets, tiles |
| 23 | ContentStatisticsState (FUN_1431637b0 / FUN_14320e1d0) | CONTENT STATISTICS: actors / players drawn and known, textures |

Pages 1-3 are not registered (the array starts with page 4).

## 5. Where the emulator can get each value today (LandmarkEmu/LandmarkEmulator.WorldServer unless said)

| Section / value | Source today | Status |
|---|---|---|
| Per update system timing (page 11 analogue) | HostedService.cs:109-123 `Run(name, update)` wraps each of the 32 world updates (:126-160) — add a Stopwatch per name | missing (one-line hook) |
| Tick avg / max ms, fps (pages 12 / 32 "Zone") | Shared/ThreadManager.cs:14-26 (`lastTick` per loop) | missing (not kept) |
| Last tick, health | Game/Watchdog.cs:37-52 (`lastTickTicks`, health file) | have |
| Players online, per landscape | Game/PlayerManager.cs:302 `Snapshot()`; Network/WorldSession.cs:84 ZoneId, :105 Position; Game/Admin.cs:61-64 `Players()` | have |
| Players reported to login | Game/ServerHeartbeat.cs:24-33 | have |
| Per opcode counts (page 27 analogue) | Network/OpcodeStats.cs:17-41 (first 4 bytes, in / out / unanswered; flushed to opcode_stats.tsv every minute); call sites WorldSession.cs:1033, :1138, :1154 | counts only; add bytes and a live snapshot |
| Per player bytes / ping (page 13 analogue) | gateway layer (LandmarkEmulator.Gateway/Network/GatewaySession.cs:42-122) | missing |
| Gateway profiler (page 24) | gateway-service.md §3.9 spec (#13) | missing (#13 builds it) |
| Site games (page 29) | Game/InstancedGames.cs:135-171 `Game` (State, StateEnds, RoundEnds, TeamScores, Players, Points, Observers, Editors, Queue, Disabled), :175 `gamesByClaim`, :47-48 state constants, :66-99 Rules; InstancedGames.Queue.cs:23 `Queued`; packet helper :1348 `Begin(output, sub)`, send pattern :929 | **have** (needs a read-only snapshot under `sync`) |
| Procedural spawns (page 28) | No ProceduralPlacement spawner (no use of gameData ProceduralPlacementSet*.txt anywhere). Partial analogues: Game/Gathering.cs:91 RegionSize, :109 Node, :164 `regions` (trees / plants per 64 m region by biome, :545 `TerrainMaterials.BiomeAt`), :165 `gathered`; Game/Creatures.cs:572 `byGuid`; Game/Terrain/OreVeins.cs:57 Vein, :442 Update; Game/LootChests.cs:46; Game/Outposts.cs:98 | **partial / missing** |
| Pathing (page 32) | Game/Pathing.cs `Monitor` (creature navigation, TASKS 6.61, 2026-10-17; pathing.md §3: field meanings ours) | **have** on /admin/monitor ("Nothing to display" until the first tile or path) |
| Env Server jobs (page 33) | no job queue; work is inline: Terrain/TerrainEdits.cs:102 stores, :314 Regenerate (heals), Terrain/TerrainLod.cs:51-55 per-layer LOD copies, OreVeins.cs:442 | missing (#14 builds the counters, terrain-service.md §8) |
| Ruin layers (page 33) | Game/Outposts.cs:98 `outposts`, :99 `byCavern`, :193 Choose | partial (count placed; no desired / failed / last id) |
| Social status | Game/Friends.cs, Guilds.cs, Chat.cs, ChatRooms.cs, Mail.cs (in-process today; #12 moves them) | have counts in memory / DB |
| World-channel status | Game/Commerce.cs, Ratings.cs, Contests.cs, Trades.cs, Encounters.cs (#15 moves them) | have |
| Restart schedule / shutdown | Game/RestartSchedule.cs, Game/Shutdown.cs | have |
| Admin identity | WorldServerConfiguration.cs:72-74 `Admins`; Game/Admin.cs:23-31 `IsAdmin`; Store.cs:145-158 `SessionOf` / `TokenOf` (token lives while the player is online); SendSelfToClient.cs:1909 `IsAdminMaybe` (never set) | have |

## 6. Spec: the server-side web admin page

### 6.1 Host and auth

- **Host: the world's own web server** (Game/WebPages.cs; port WebPagesPort, default 19001, one per world container),
  extending the existing `/admin` page (Game/Admin.cs). Reasons: every live monitor page was per zone / per world
  (28 / 29 / 32 / 11 / 12 come from the zone), the world already has the admin check and a running page, and
  world-channel-service.md §6.6 keeps a per-world endpoint for admin. **Not** LandmarkEmulator.WebAPI: it is the
  public account / launcher API in the auth container with the account database and no game state.
- **Auth step 1 (now):** existing admins only — `Admin.IsAdmin(Store.SessionOf(t))` (WorldServer.json / `.env`
  `Admins`: account or character names). Works in the in-game browser (`/admin`) and in a desktop browser while the
  admin is online (the `/admin` command can also print the URL in chat).
- **Auth step 2 (ours, later):** offline login for a desktop browser: the auth WebAPI verifies the account by SRP (the
  existing `/srp/start` + a new `/srp/admin` finish that does **not** call UpdateAccountSessionKey, so it can't log the
  player's game session out) and returns a short-lived ticket `HMAC(ADMIN_TICKET_SECRET, account | expiry)`; the world
  accepts it if the account is in its `Admins`. One secret in `.env`.
- Read-only: monitor endpoints never change game state; actions stay in the existing `/api/admin/run`, logged.

### 6.2 Pages and endpoints

`GET /admin/monitor?t=…` (ES5 page like Admin.Page: the in-game browser is old) with tabs; each tab polls
`GET /api/admin/monitor/<section>?t=…` (JSON, read on the world thread via WebPages.cs:85 OnWorldThread or a lock-protected
snapshot) every `refresh` seconds, only while the tab is open (no push, nothing sent to game clients).

| Section | Tab text (client labels kept) | JSON fields | Refresh | Data owner |
|---|---|---|---|---|
| `zone-profiler` | ZONE PROFILER DATA | systems[] {name, count, avgUs, minUs, maxUs} | 1 s | world / zone |
| `zone-stats` | ZONE STATS | players, perLandscape{id: n}, tickAvgMs, tickMaxMs, fps (1000/avg; red < 10) | 1 s | world / zone |
| `players` | CHARACTER STATUS | per player {name, landscape, position, health / resources "%s: %d of %d", bytesIn/Out, ping} | 2 s | world / zone (+ gateway for bytes) |
| `packets` | PACKET MONITOR | rows {packetId, name, count, totalBytes, avgBytes, worstBytes, ratePerSec, direction}; sort by bandwidth / count | 2 s | world / zone |
| `procedural` | Procedural data | the 0x10A model of §3 as JSON (same builder as the packet, §7) | 5 s (client's interval) | world / zone |
| `instanced-games` | Instanced Game Information - Server | the §2 key / value / indent list (same builder as the packet) | 2 s | world / zone |
| `pathing` | Pathing Information - Zone | the page-32 fields; `hasData = false` today | 1 s | world / zone |
| `gateway` | GATEWAY STATS | incoming / outgoing {signature, name, count, totalMs, recipients, headerBytes, appBytes}, totals, uniquePacketCount | 2 s | gateway (#13) |
| `environment` | Environment Server Information | jobs[19] {name, active, pending}, totalJobs, ruinLayers[] {layer, desired, active, failed, lastId} | 1 s | terrain / Env Server (#14) |
| `social` | (ours) | up, onlinePresence, chatChannels {name, members}, guilds, mailQueued | 5 s | social (#12) |
| `world-channel` | (ours) | up, openOrders, ratingsToday, activeTrades, encounters / lobbies | 5 s | world-channel (#15) |
| `operations` | (ours) | lastTickAge, restartSchedule, nextRestart, shutdownPending, service health per container | 5 s | operations (#16) |

### 6.3 Data from the other Docker services

Each Phase B service already gets an internal HTTP health probe (operations.md spec 3). Extend it with
`GET /status` (JSON, internal network only, never published in compose) returning its section above. The world page
fetches `/status` server-side from the addresses in `.env` (the same addresses the internal transport uses) and
caches each for its refresh interval, so many open admin tabs cost one fetch per interval. A service that doesn't
answer shows "not routable" (the client's ChannelIsRoutable idea). No new internal transport messages are needed for
the admin page (keeps the L48 message list unchanged).

### 6.4 Bandwidth

Nothing reaches game clients except the §7 replies to admins who asked; web polling only while a tab is open; JSON
snapshots built once per interval and shared; procedural at 5 s like the client.

## 7. Spec: the client replies for pages 28 / 29 (zone server)

1. **Opcodes:** give 0x109 / 0x10A wire-form enum values `0x0901` / `0x0A01` (ZoneMessageOpcode.cs:567-568 keep the
   0x1XX00 form only while unhandled, :555) and register a handler for 0x0901 (empty body).
2. **0x109 handler:** if `Admin.IsAdmin(session)` → build and send 0x10A from the procedural snapshot; otherwise
   ignore (count as unanswered). At most one reply per 4 s per session (the client asks every 5 s).
3. **0x10A builder (ours):** until a real ProceduralPlacement spawner exists, map what the zone has, labelled as ours:
   layers = spawn systems (1 trees, 2 plants — Gathering; 3 creatures; 4 ore veins; 5 loot chests), biome = VoxelBiomes
   id (TerrainMaterials.BiomeAt), entity list = the biome's tree / plant / creature set, entity = model / prop with
   count, min 0, max; Zone Node Count = candidate points in loaded regions (must be > 0), Zone Spawn Count = standing
   nodes; N / P / F = 0 until L82 is answered. **Always include matching rows** in both tables (+0x68 / +0x98) for
   every list / entity key sent, or the page draws nothing (§3 filters). nameTextId = the prop's / item's name text id.
   Same bytes for 0.589 and 0.695.
4. **0x10D sub 2 push:** to admins who turned it on (dev command `monitor instanced on|off`, ours, since live's trigger
   is L81) every 2 s while any site game exists; body = §2 tree; via InstancedGames.cs `Begin(output, 2)` and
   `EnqueueMessage(ZoneMessageOpcode.InstancedGameBase, …)` like :929. Not translated by ClientWire695 (sub 2 < 0xD).
5. **Docker / Phase B:** both replies belong to the zone server (Zone channel); the gateway only tunnels them.
6. **Docs:** LandmarkEmu/docs/ entry for both layouts when built (open-source-docs rule).

## 8. Testing later with a dev-only Frida page hook (describe only — not run)

Only on the patched test copy (testclient, re/dlogin.sh direct connect), never the user's install; one test client;
never time out or kill the Frida runner with hooks attached (memory: frida-detach-crash) — undo and detach cleanly.

1. Script (re/tools/run_frida_pid2.py style): find Landmark64.exe's base, rebase the addresses below.
   - 0.695: monitor = `*(void**)0x145432880`; on the main thread (Interceptor.attach on the monitor update
     FUN_143226700, run once in onEnter) call vtable **+0x40** (FUN_143226620) with 1 = paged view on, then **+0x30**
     (FUN_143226630) with 28 or 29.
   - 0.589: monitor = `*(void**)0x145353300`; update FUN_14317bce0; +0x40 = FUN_14317bc00, +0x30 = FUN_14317bc10.
2. Page 28: expect the client to send `09 01` every 5 s (server log / OpcodeStats "in 0901"), the server to answer 0x10A;
   verify **by the client**: hook the reader FUN_142c4e3b0 / FUN_142bb4ba0 (return 1) and the copy FUN_143217af0 /
   FUN_14316d0d0 (called); read the copied fields back from the page state (+0x30 → +0x20 / +0x24 …) and compare
   with what was sent; a screenshot of the page text ("Zone Spawn Count: …", list / entity lines and colours).
3. Page 29: switch to page 29; on the server enable the push; verify the reader FUN_1434d6be0 / FUN_143427160 ran, the
   copy FUN_14321ca40 / FUN_143172020 was called, and `DAT_14516a658` (0.589; 0.695 partner from the copy) equals n;
   screenshot shows the "Server" tree above the client's own block.
4. Negative checks: one trailing byte → no copy (both readers demand full consumption); an entity-list key missing
   from the EntityLists rows → list not drawn.
5. Clean-up: +0x30 with 0 (or +0x40 with 0), detach, close the client (memory: pc-resource-limits).

## 9. Open leads added (trace/0695/OPEN-LEADS.md)

- **L81** live content and push trigger of InstancedGameAdminMonitorPacket (page 29).
- **L82** meanings of the undrawn / unlabelled page-28 fields and live's layer / node / entity-list model.
- **L83** Storybricks pages 30-31 on Landmark's server.

## 10. Hand-offs

- **#3 / #10 / generator (re/tools/trace.py, famsummary.py):** InstancedGame family subs in 010D_InstancedGameEndRequest.md
  and FAMILIES-SUMMARY.md are shifted by +2 (AdminMonitor is 2, PropRecipe 4); coverage-695.md is right.
- **#13 gateway:** expose the profiler counters (gateway-service.md §1.4 / §3.9) and per-connection bytes / ping in its
  `/status` JSON (§6.3).
- **#14 terrain / Env Server:** `/status` with the page-33 struct (19 jobs active / pending, total, ruin layers).
- **#12 social, #15 world-channel:** `/status` with the §6.2 fields.
- **#16 operations:** the health probe grows a `/status`; restart schedule and service health in the `operations` tab.
- **Coordinator:** admin-monitor.md page list (20-22, 30-31) and reply table → point at this file; a ProceduralPlacement
  spawner (from gameData ProceduralPlacementSet*.txt) is a gameplay task that would give page 28 real data (TASKS §7).

## 11. Built (2026-10-16; untested in game)

What landed in LandmarkEmu (world server), following §6 / §7 with these differences:

| Part | Built | Where |
|---|---|---|
| Page 29 push | 0x10D sub 2 exactly as §2; tree as §2's table plus `RuleSet` and `ControlPoints` (ours). Gated by the setting `AdminMonitorPush` (default **off**; retail can't show page 29, L3) instead of a `monitor instanced on` dev command; to admins (`Admin.IsAdmin`) standing on / playing in a site game; every 2 s at most, only when the bytes changed or 10 s passed | Game/AdminMonitor.cs `Update`, Game/InstancedGames.Monitor.cs |
| Page 28 reply | 0x109 handled (enum now wire form `0x0901` / `0x0A01`); admins get 0x10A for their landscape, ≤ 1 per 4 s; not gated by a setting (the request proves page 28 is up). Mapping (ours, L2 / L82 unchanged): layer = PLACEMENT_DATA_GROUP_ID (0 = old tree lists), biome = set BIOME_ID, entity list = placement set (old lists key 100000), entity = set entity (a = entity id, name = PropDefs name text of its prop, max = MAX_SPAWNS), spawn count = nodes standing, node count = nodes generated (≥ 1), layer queued = nodes regrowing, Available = nodes, Above Min / Over Max = standing below / at max, N / P / F = 0; both row tables filled for every key; ≤ 400 entities | Game/AdminMonitor.cs `ProceduralBody`, Gathering.cs `NodeCounts` |
| Wire check | Both bodies written by the server code and read back by a Python model of the client readers (FUN_142bb4ba0 / FUN_143427160 layouts, re-verified with pkt.py: FUN_142b937a0, FUN_142b94430, FUN_142b7ece0, FUN_142b9c300, FUN_142b9c470, FUN_142b85720, FUN_142b8baa0, FUN_142b8bc30): fully consumed, every list / entity key present in its row table | scratch harness (not kept) |
| Web page | `/admin/monitor` + `/api/admin/monitor` (one JSON for all sections, not per-section endpoints; built on the world thread, shared 2 s; page polls every 3 s). Sections: zone stats, zone profiler (per update system, 5 s windows), services (gateway / social / world-channel / commerce link states, in-process — no `/status` fetch from other containers yet, §6.3 open), terrain (edited cells, heals), instanced games, procedural, packet monitor (OpcodeStats live counts + bytes), players | Game/AdminMonitorPage.cs, Game/WebPages.cs `MonitorJson`, HostedService.cs (timing) |
| Not built | Environment Server job counters (#14), gateway profiler (#13), `/status` endpoints of the other containers, offline admin login (§6.1 step 2) | — |
| Generator | re/tools/subnames.py no longer adds a switch's bias twice (the decoder's "values from 0xB" already gives true subs); InstancedGame 0x10D and Template 0xF0 subs regenerated (families 010D / 00F0 and their FAMILIES-SUMMARY lines only); trace.py skips the new `unanswered` rows of opcode_stats.tsv | re/tools |

Test: TESTING-NEEDED.md "Server monitor"; the client pages need the §8 Frida hook.
