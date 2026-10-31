# Pathing (creature navigation): client trace and our build

> 0.695 rebase (2026-10-26): addresses below are 0.589 unless marked 0.695; their 0.695 partners and changes: trace/0695/address-map-589-695.md, trace/0695/rebase-695.md

TASKS 6.61 (2026-10-17). What the client shows of live's pathing system, and what the emulator built in its place
(LandmarkEmu `Game/Pathing.cs`, `Game/Creatures.cs` `Walk`, `Game/Sight.cs` `ColumnAir`, `CavePieces.AirSpans`).
[P] = proven from the client or a developer post, [I] = ours / inferred.

## 1. What the client has [P]

| What | Where (0.695 unless said) | Notes |
|---|---|---|
| Admin page 32 "Pathing Information - Zone" | PathingMonitorState draw FUN_143167ad0 (0.589) / FUN_1432124f0 (0.695); admin-pages.md §page 32 | Pathing Summary: NavMeshCells (+0xD10), PendingStitchSections (+0xD14), Characters (+0xD18), DynamicSilhouettes (+0xD1C), AvoidanceObstacles (+0xD20), NavMeshJobsRemaining (+0xD24), NavMeshJobsCompletedThisFrame (+0xD28), NavMeshJobsCompletedLifetime (+0xD2C); Performance Summary (zone avg fps / frame ms, World Step avg fps / frame ms) and a Name / Count / AvgUs / MinUs / MaxUs table |
| Page 32 request | 0x1E sub 0x4A9 AdminCommandPacketPathingDataMonitorUpdateRequest, every 0.2 s, built in FUN_143171d50 with the channel string **"Pathing"** (PTR_s_Pathing_145169f90; families/001E_Command.md) | the other admin requests name "ZoneCommand"; a separate "Pathing" channel name suggests a pathing back end [I] |
| Page 32 reply | 0x1E sub 0x4AA PathingDataMonitorUpdateResponse: registry name only, **no reader** (OPEN-LEADS L4) | the page can't be filled in retail; the retail client can't open it either (L3) |
| Other pathing admin commands | registration table part_0107 ~10155-10190: 0x4A2 VisualizePathing, 0x4A3 FreezeNavMeshState, 0x4A4 SaveNavMeshState, 0x4A5 PathMetricsReset, 0x4A6 PathMetricsFlush, 0x4A7 DebugPathQueryRequest (C→S, two points + a byte, built in FUN_1436e3780 from a two-click debug state; channel "ZoneCommand"), 0x4A8 DebugPathQueryResponse | names only beyond 0x4A7's builder; no navmesh data or parameters in the client |
| Storybricks AI debug (pages 30 / 31) | part_0109 ~12635: per character "Pathing: %s", PathingType 4, PathingDraw (bit 3 of +0xE8); client command `sb_pathing` toggles that draw bit (~11685) | a per-creature pathing status string came from the server (admin client only) |
| Developer posts | P0805 (blueprint, late 2014): "NPC pathing ... is ready now (Completed)"; P4225 / P4266 / P4267 (21 Mar 2016): Pathing Nodes for story creatures, "pathing oddities"; P5265 (11 May 2016): spawners placed in the air failed to path; P5781 (7 Jun 2016): "The collision area around props, especially large trees, should now be much smaller, allowing creatures to path past them more easily"; P5924 (30 Jun 2016): outposts no longer spawn NPCs inside walls; P5727: no shots through walls; secondhand: EQ Next pathing data was ~6 GB per island per world (live-server-architecture.md) | props were obstacles on live (P5781) |

Not in the client (searched 2026-10-17: re/decomp_all_695 strings for navmesh / NavMesh / Pathing / Silhouette /
Avoidance / hkai; file indexes have no navmesh files by name): the mesh format, cell size, agent radius / height,
climb, stitching rules, what "silhouettes" and "avoidance obstacles" were made of. → OPEN-LEADS L132.

## 2. What we built [I]

A walkable column grid instead of a polygon mesh, built from data the server already has (no duplicate solidity):

- **Column** 1.2 m (Sight's ruin block). Floors = bottoms of open air ≥ 2.0 m tall, each with its ceiling.
  - Cave kind: `Sight.ColumnAir` = cave air from `CavePieces.AirSpans` (the LOD 0 type runs of every covering piece,
    same voxel rounding as `IsAir`), then each stamped outpost's ruin grid over it (written air carves, written solid
    fills: Sight's own rule).
  - Surface kind: the baked ground height (`TerrainHeights.HeightAt`), open sky.
- **Tile** 16 × 16 columns (19.2 m) per landscape and kind; built lazily on a single BelowNormal background thread;
  ~0.08-0.12 ms and ~2 KB each (offline harness on 406's caves, 2026-10-17: 99 tiles, avg 80 µs, max 0.85 ms); cache
  ≤ 3,072 tiles, LRU. `Pathing.OutpostsChanged` (from `Outposts.TakeArrivals`) drops the tiles under a newly stamped
  outpost; a tile built from older data meanwhile is not kept (generation counter).
- **Move rule**: 8 neighbours, diagonal only if both side columns pass; floor change ≤ 1.25 m; shared headroom ≥ 2 m.
- **Line** (world thread, built tiles only): follow the floor along the straight line (half-column samples); Clear /
  Blocked / Unknown (tiles requested) / Mismatch (start or goal has no floor near it: fail open).
- **Path** (background): A* over (column, floor), ≤ 6,000 expansions, within 96 m of the start and half the way + 24 m
  of the middle; unreachable → path to the node nearest the goal (partial); thinned with Line (24 waypoints ahead).
  Harness: through a synthetic ruin wall's doorway in 112 nodes; around a 25 m wall's end when shorter; a goal on the
  wall top → partial, ending beside the wall; a goal 200 m away → partial after 6,000 nodes (~10-15 ms).
- **Creatures** (`Walk`): cavern / outpost creatures (Cave), surface creatures off build sites (Surface); story creatures
  keep the straight step (player-edited ground: L133). Straight when Line is not Blocked; else follow the path (one
  query in flight per creature, again after the goal moved 2 m / 25 % and 1 s); chasers stand facing an unreachable
  target; Return / Travel Point fail open. Cavern heights come from the grid's floors (outpost floors now count), a
  client CorrectNpcHeights offset ≤ 1.5 m is kept. Packets unchanged (Position 0x7F type-0 bodies).
- **Setting** CreaturePathing (default on), dev `pathing [on|off]`.

## 3. Page 32 numbers (web /admin/monitor) [I]

| Client label | Ours |
|---|---|
| NavMeshCells | tiles cached |
| PendingStitchSections | tiles requested, not built yet |
| Characters | creatures with a path or a path query |
| DynamicSilhouettes, AvoidanceObstacles | 0 (no moving obstacles, no props as obstacles) |
| NavMeshJobsRemaining | jobs queued (tiles + paths) |
| NavMeshJobsCompletedThisFrame | jobs done per second since the last look |
| NavMeshJobsCompletedLifetime | jobs done since start |
| Name / Count / AvgUs / MinUs / MaxUs | "tile build", "path query" |

The zone / World Step performance lines are already on the page as the zone tick (no separate simulation step here).
No 0x4AA is sent: retail has no reader (L4).

## 4. Open

L132 (live's mesh format and parameters, page-32 meanings, 0x4A7 / 0x4A8 debug path query), L133 (player-edited terrain
and props as obstacles). Larger bodies (Ogre, Abomination) use the same 2 m clearance [I].
