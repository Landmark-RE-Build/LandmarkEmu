# Trees and grass toward the horizon (2026-11-01)

**Report (user, 2026-11-01).** "I also don't like how the grass and trees don't load in the horizon." Screenshot on
Pioneer land 464 in the patched test copy, settings as the real install: OverallQuality 3, FloraQuality 3,
TerrainQuality 3, ModelQuality 3, RenderDistance -1, DrawFlora on. Trees stop at a line some 100-200 m out, the far
plain is bare, grass ends earlier.

**Answer.**
- **Trees: server-caused, fixed.** The server sent trees only in the 64 m regions whose nearest edge was within a fixed
  160 m (dropped beyond 224 m), our own first choice from 2026-10 (MODLOG). The client draws trees much further.
  Now: setting `TreeStreamRange` (default 400 m, drop 480 m), nearest first with a per-tick budget beyond 160 m
  (branch local/horizon, `Game/Gathering.Stream.cs`).
- **Grass: the client's own limit, same as live, no change.** The client draws flora only within a fixed 64 m of the
  camera; nothing the server sends can extend it. Documented in LandmarkEmu/docs/terrain.md "Grass (flora)".

Two trace agents (workflow, 2026-11-01) did the client and server side; their raw tables and tools are in the session
scratchpad (`tree_lods2.txt`, `adr_trees/`, `hz/adr/`, `hz/treecount/`). Evidence marks: [P] proven from the client /
code / data, [I] inferred.

## 1. How far the 0.695 client draws trees

| Claim | Mark | Where |
|---|---|---|
| Models.txt has no LOD or draw-distance column (ID, MODEL_FILE_NAME, ..., CAMERA_DISTANCE is a UI camera value, PATH_BLOCK_*). Every LOD and the cull distance come from the model's `.adr` `<Lods>` block. | P | gameData695/Models.txt header; adr parser FUN_143e0d9a0 (decomp_all_695 part_0154:24252-24320) |
| A trailing `<Lod distance=X/>` with no fileName is the cull distance; when the last `<Lod>` has a mesh the model is never culled. Distances are stored at +0x29c+4i (FUN_143da5d10, forced ≥ previous + 1.0); hi[i] = dist[i+1] at +0x2c4, re-enter = dist[i] − 0.1 × gap at +0x2b0 (FUN_143da6430). | P | part_0152:9227-9247; FUN_143d9c8a0 (LOD count +0x298) |
| LOD metric = \|actor − view\| × (view +0x1f8 × 0.5) × actor +0x378 (1.0 by default) / max(mean scale × model +0x4e8 (adr scale), 0.5) × ActorManager +0x6e0a5c. FUN_1430e7710 picks the LOD from the thresholds; past the last LOD with no file, +0x2B4 = count and FUN_143cffe20 neither calls vfunc 400 nor the render submit FUN_143dbcb40. | P | FUN_143cffe20 (part_0148:39013-39019), FUN_143d0b3d0 (part_0149:7034-7116), ctor FUN_143d043d0 part_0149:1733, ActorManager vfunc 0x1d0 FUN_1430e7820 |
| ModelQuality → ActorManager multiplier: 1 → 1.4, 2 → 1.2, 3 / other → 1.0. GraphicsQuality writes ActorManager +0x6ce680 (0.02 / 0.01 / 0.005); no reader found yet. | P (reader: not found yet) | FUN_142d53730 @0x142d538ef-0x142d53963; setter FUN_1430f5d30; settings map FUN_143489560 (+0xf3c, +0xf54, +0xf58, +0xf5c) |
| View factor +0x1f8 × 0.5 = tan(hfov/2): the projection builder writes right − left = 2 tan(hfov/2); with aspect ≥ 1 the FOV value is treated as diagonal. Third-person FOV CONTROLLER_2_FOV = 103.2 (first person 73.1, default 85) → about 1.10 on 16:9 (first person about 0.65). | I (view object matched by structure) | FUN_142e5c380 → FUN_142d213b0 → FUN_142d214a0; FUN_142e536e0 0x142e53799-0x142e537de; CONTROLLER_%d_FOV part_0114:4580; testcopy UserOptions.ini |
| Render Distance ≤ 0 (default -1) → 99999 (0x47c34f80); far plane = min(sky FadeRange Far, RenderDistance): Sky_Tropical 6000, Desert 4500, JaggedPine 7000, Everfrost 3900, Deciduous 7000, Lavastorm 6500 m. | P | FUN_143489560 part_0106 ~16606-16615; FUN_142e5c4f0; FUN_1441cb9b0 (+0x3870), FUN_1441d1f10; Resources/SKY/*.xml |
| Fog is light: Sky_Tropical haze 1075 m, fog 1000-3600 m; JaggedPine / Deciduous daytime density 0.0002/m (about 90 % transmittance at 500 m). | P | Sky_Tropical.xml:44, 98-99; Sky_JaggedPine.xml:95-110 |
| adr Base updateRadius 150 (actor +0x450, clamped to 1500) gates animation, effects and self-light only, not drawing. No actor-count cap found (ActorManager containers 4000 / 5000 / 1000 look like hash bucket counts). | I | FUN_143e0bac0, FUN_143d03cf0, readers FUN_143cfd520, FUN_142e99950; ctor FUN_1430e2250 |

**Cull distances (raw adr X, metres) of the trees the server places** [P, `tree_lods2.txt`, 129 tree adrs]:

| Old tree lists (dev default) | X | Placement sets | X |
|---|---|---|---|
| 351 Birch_Medium02 | 440 | Jungle Palms_Curvy01 / Tall01 | 390 / 380 |
| 352 Birch_Medium03 | 571 | Banyans_Large01 / 02 | 2500 |
| 1467-1469 Oaks_Medium01-03 | 800 / 735 / 770 | Banyans_Small01 / 02 | never culled |
| 338 / 342 / 343 JaggedPine | 866 / 926 / 806 | Desert cacti / trees | 172-204 / 900-1500 |
| 345 / 346 DiamondPine | 2480 / 1700 | Tundra firs, centipines, groves | 950-1651 (SpindlePine never) |
| 347 GnarledPine_Large | 1200 | | |
| 348 / 349 GnarledPine_Small01 / 02 | 262 / 308 | | |
| 225 / 226 / 1370 / 1371 Lavastorm | 580 / 985 / 905 / 518 | | |

Effective cull ≈ X × scale / view factor: in the third-person camera about X × scale / 1.1 [I]. So the client draws the
birches and oaks on 464 out to about 400-800 m; the 160-250 m tree line was not a client limit.

**Same on live?** The client limits are the client's; live's server range is **not known yet** (no capture, no
developer post: grep of design/forum, design/wiki, re/out/forum_raw for tree view distance / pop-in found only the 27 Aug
2014 note "The Portal Spire should now have a near infinite view distance" and a 2014 thread comparing Render distance
200 / 4000 / 99999). 2016 footage (Verlin "Beginner's Corner - 3 Build Sites", 2016-06-07, YnBmejA5MWE; frames
scratchpad vid-pioneer/f51.jpg, hz/vf2/v83.jpg) shows yellow birches running to the horizon with no tree line, among
neighbouring sites' buildings (≥ 285 m away by the site buffer, docs/gameplay.md:399-400); the smallest visible ones
are about 250-430 m off if the vertical FOV is 60-74° [I].

## 2. How far the client draws grass

| Claim | Mark | Where |
|---|---|---|
| FloraManager ctor sets the radius +0x2250 = 64.0f. The flora command-list job draws a batch / instance only if its squared distance to the camera ≤ radius², fading over [FADEOUT_START, max(FADEOUT_END, radius)]. FADEOUT is 0/0 in 174 of 177 VoxelFloras rows → fade over 0-64 m (the shape is in the shader, not traced). | P | FUN_14373d210 (+0x44a×8 = 0x42800000); FUN_14373f550 → FUN_1437a7c90 (part_0119:25737); VoxelFloras.txt; flora def copy part_0106 ~30230 |
| The radius changes only by `/flora Radius n` (FUN_1433a5710 → FUN_14349f530, call @0x1433a5934), gated to admin clients (PlayClient vfunc 0xb0) or the admin flag +0x7b0d8. FloraManager's setter FUN_143740190 is unreferenced; no packet or Lua binding reaches it. | P | refscan; trace/0695/client-commands-for-slots.md §2.5 |
| FloraQuality = density only: +0x23bc = 0 / 0.25 / 0.5 / 1.0 for q < 1 / 1 / 2 / ≥ 3 (per-triangle acceptance threshold). Run-time SetFloraQuality → FUN_143d70840(q − 1) → the terrain system's vfunc 0x90 (voxel-mode target not resolved yet). | P (target: not found yet) | FUN_14349af30 part_0106:28325-28335; FUN_1436c7e90; FUN_142d53730 @0x142d53820 |
| Flora only on cell levels 2-3: VoxelEntity::CreateRenderMeshesForGeometry calls the placer only when level < 4 (level = cell id >> 60). Rule: three vertices share one material, slope within limits, density test; seed = hash(triangle centre, flora id). | P | FUN_1436c17a0 part_0117 ~7700; level arg @0x1436c05e3 = shr r14,0x3c @0x1436c0279 |
| Clipmap fixed at preset 1 {6,6,6} (ctor +0x5a88 = 1; setter FUN_143f9a6d0 unreferenced). Simulated rings per axis: level 2 to ~88-104 m (≥ ~80), level 3 ~168-216, level 4 ~330-440, level 5 ~585-700 m. So visible grass sits on level-2 cells; level-3 flora lies beyond the 64 m cut. | P table / I rings | table 0x14499d2c0; FUN_143f8aa40; FUN_142b1ced0 (simulation scratchpad clip.c); phaseE2-building.md L200 |
| Server cells don't change visible grass: TerrainLod copies at level ≥ 4 can't carry flora, level-3 flora is beyond 64 m, edited level-2 cells get flora by the same per-triangle rule. The server sends no flora data. | I | follows from the rows above; docs/terrain.md "Coarse levels" |

So grass ending ~64 m out, before the trees, is the unmodified client on every setting, as on live.

## 3. The server before the fix

- `Gathering.cs` (client-0.695 814df329): RegionSize 64, ViewRange 160, DropRange 224 (nearest-edge distance,
  horizontal); trees certain within 160 m, some to ~250 m at region corners, none beyond [P, Gathering.cs:130-134,
  637-684, 1001-1006]. MODLOG.md:461-462 "View 160 m, drop 224 m" — our choice, not live's.
- Streamed every 1 s per player, regions in grid order (not nearest first), no budget; nothing during StreamsOnHold
  (loading screen, arrival, re-zone), then the whole set in one burst [P].
- Dev world: ProceduralPlacement off, so 464 grows birch / oak 351/352/1467-1469 at 3 % per 8 m cell (1.88 trees per
  land region), not live's palms and banyans (known gap L113) [P, console.log:56].

## 4. Built (branch local/horizon, worktree re/wt-horizon)

- `Game/Gathering.Stream.cs` (the stream moved out of Gathering.cs): `StreamRange` = setting `TreeStreamRange`
  (default 400, clamped 64-1000; dev `treestream <m>` overrides until the restart); drop at range + 80 m (as placed
  props: 400 → 480). Regions within 160 m (`ImmediateRange`) go at once, as the whole set did; beyond, nearest first,
  at most 32 regions per tick (`FarRegionsPerTick`) and only while the tick has sent fewer than 200 nodes
  (`StreamBudget`, the 160 m nodes included). The step itself is `StreamRegions` (testable without baked heights).
- Why 400 [I]: covers the 2016 footage (≥ 300-450 m), equals PropStreamRange 400 (the Render Distance slider's last
  bounded step, 120 + 2.81 × 99 %, FUN_1435cfd10), so trees and props end at the same line.
- Settings everywhere: WorldServerConfiguration, ServerSettings.Table (Now), WorldServer.example.json,
  docker/world/docker-compose.yml, .env.example, docs/docker.md, docs/admin.md, wiki (tools/wiki_reference.py; live
  value "not known (2016 videos: trees to at least 300-450 m)").
- Tests: LandmarkEmulator.Tests/Rules/TreeStreamRangeTests.cs (8): default / clamp / override; first tick = whole
  160 m + nearest far regions; later ticks fill outward to exactly the range; the budget counts the 160 m ring but
  never holds it back; 160 = the old one-tick stream; moving 300 m drops exactly the regions beyond 480 m with their
  nodes' removals; underground drops all; another landscape's regions are forgotten without removals. Full suite 2,136
  passed.

## 5. Bandwidth (measured with the server's own generator, scratchpad hz/treecount400)

296 B per AddLightweightProp (290 B body + 6 B opcode; zone traffic is uncompressed; PropBase BulkCreate 0xED/0x0E has
no client reader, so there is no cheaper spawn) [P]; 16 B per RemovePlayer. Means over 1,852 (464) / 1,653 (406)
dry-land player spots, per login or re-zone (a re-zone deletes every entity, so it is resent):

| Mode / landscape | 160 m (before) | 400 m (now) | 400 m p90 | Arrives in (1 s ticks) | Walking KB/km 160 → 400 (KB/s at 8 m/s) |
|---|---|---|---|---|---|
| Placement off (dev), 464 | 58 trees / 16.8 KB | 271 / 78.2 KB | 298 / 86.1 KB | 4 (first tick 118 nodes, 34 KB) | 54 → 121 (0.43 → 0.97) |
| Placement off (dev), 406 | 183 / 53.0 KB | 827 / 238.9 KB | 1,210 / 349.8 KB | 5.1 mean, 7 max (first 214, 62 KB) | 155 → 344 (1.24 → 2.75) |
| Placement on, 464 jungle | 483 / 139.5 KB | 2,242 / 648.0 KB | 2,932 / 847.5 KB | 9.8 mean, 16 max | 447 → 1,012 (3.57 → 8.10) |
| Placement on, 406 | 107 / 30.9 KB | 488 / 141.2 KB | 906 / 261.9 KB | 4.4 mean, 7 max | 98 → 217 (0.79 → 1.74) |

At 1,000 m (the cap): 464 1,456 trees / 421 KB, 406 4,163 / 1,203 KB (placement off). The drop margin (224 vs 240 at
160 m) changes nothing on straight travel. Generation 0.02 ms per region (lists) / 0.4-0.9 ms (sets); the 32-region
cap bounds a tick's generation to ~30 ms with sets.

**Not done (saving measured, needs the [I] view factor):** skipping trees the client would cull anyway (horizontal
distance > X × scale / factor). With factor 1.0: 406 827 → 718 at 400 m (−13 %, mostly GnarledPine_Small 262 / 308 m);
464 271 → 267; placement-on jungle 2,242 → 1,650 (−26 %, palms 380 / 390 m). The server does not read `.adr` files
(no pack reader), and a first-person camera (factor ~0.65) draws further than the third-person one, so a filter at 1.0
or 1.1 would cut trees a first-person player sees. Lead L266.

## 6. Open (OPEN-LEADS L266)

- Live's server tree range: not known yet (no capture). Try: more 2016 footage with a known distance (site buffer 285 m,
  Spire 90 m area) and a traced in-game FOV.
- The view factor's object (+0x1f8) is matched by structure only; the in-game vertical FOV for the footage distances.
- The client-cull filter (above): needs a server-side `.adr` `<Lods>` reader (pack index) and the camera factor range.
- GraphicsQuality's ActorManager +0x6ce680 reader; the voxel-mode target of SetFloraQuality (vfunc 0x90); the flora
  fade shape (shader).
- Whether the nearest-50 full-entity promotion (FUN_142ee8540, 50 / 10 per frame / 300 m [I]) covers props: if not,
  far trees would add FullCharacterDataRequest traffic (dev opcode_stats: 14,514 AddLightweightProp vs 7,487 requests
  suggests a cap).
- Client frame-rate cost of ~800-2,200 tree actors at 400 m (needs an in-game check, TESTING-NEEDED).
