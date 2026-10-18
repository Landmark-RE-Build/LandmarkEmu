# Event islands (continents 436-438) and the "Events" world (TASKS 0.T2, 3.19) — 2026-10-16

Starts from terrain-695.md §2 (file numbers = continent ids, the islands are the developers' "Museum" template
island) and OPEN-LEADS L13 / L15 / L16. Marks: **[P]** proven from the client or its data, **[I]** inferred,
**[W]** forum / wiki, **ours** = the server's choice where the client and sources say nothing.

## 1. Result

| Item | State |
|---|---|
| Zones 460 / 461 / 462 → continents 436 / 437 / 438 in SendZoneDetails | built (Game/Landscapes.cs), compiled, **untested in game** |
| Bitmap names | Shape_424 + HeightLayerIds_424 for all three; BiomeIds_427 (460) / BiomeIds_437 (461, 462) — **[I]**, strong (§2); proof = a 0.695 bake matching the shipped .csc (§4, TESTING-NEEDED) |
| .csc surface cache format | decoded (§3): per 16 m column, the range of 16 m cells holding the ground **[P layout, I meaning, 95.5-98 % reproduced]** |
| Server heights from the .csc | **not exact** (a 16 m range, not a height: median error 1.6 m, p90 4.4 m, max 40 m); not used. Heights = the usual client bake (§4), not done yet (needs the 0.695 client) |
| Materials under the pick | the template biome bitmap (TerrainMaterials.BiomeBitmapOf) |
| Caves / ore veins | none: no ContinentCavePlacements[460-462] / ContinentVeinData460-462 exist (and none for 436-438) **[P]**; the server's lookups are by zone id and find nothing |
| "Events" world in the teleport menu | built: second world, id 3 (ours), name text 567016 "Events" [I], holds the island chosen at start-up (EventIsland setting, default auto = live's 2016 dates) |

## 2. Which bitmaps the islands are generated with

The client builds the continent from the three bitmaps SendZoneDetails names (FUN_143cdf940 keys BiomeMap /
ShapeMap / HeightLayerMap, L13) and the continent id (selects ContinentOceanMap / ContinentSurfaceCache, engine
+0x5208, terrain-695.md §2.1). The surface cache the client ships is what live's generator produced for the continent,
so a candidate set of bitmaps is right when the terrain it generates reproduces that cache.

Bitmap md5s (0.695 install, `md5sum`): Shape e75d937c6e = 424, 425, 426, 427, 428, 429, 432; HeightLayerIds c5db546028 =
the same seven. BiomeIds: 424 880ad0aeca (OldGrowth 50 / Ocean 54 / 72 / 75), 425 = 426 2e38b86df6 (49 / 54 / 71 / 74),
427 = 429 ee5ba3f32e (Deciduous 52 / 54 / 73 / 76), 428 e4bfc77da7 (77 only), 432 c7e6881c61 (78 only), 437 9626f71724
(Tundra 51 / 54 / 80 / 81) — 424, 425, 427 and 437 have the same layout (48.3 % land, 49.4 % ocean, 2.0 %, 0.3 %).

LOD 0 of the surface caches compared texel by texel (`cscall.py`, low = surface cell, §3):

| Cache | Best matches (whole u16 / low field) |
|---|---|
| 436 | 427 and 429: 99.994 % / 99.998 %; 432 97.1 / 98.7; 425, 426 95.7 / 97.8; 424 94.6 / 97.5; 437 / 438 83.6 / 92.1 |
| 437 | 438: 99.976 % / 99.988 %; 425 / 426 86.2 / 93.6; 424 86.7 / 93.5; 427 / 429 / 436 83.6 / 92.1 |

- Same bitmaps → same cache up to a noise floor: 425 / 426 99.994 % (low 99.997 %), 427 / 429 99.998 % (low 100 %).
  Same Shape and HeightLayerIds with different biome sets differ: 424 / 425 95.0 % (low 97.6 %), 424 / 427 94.6 (97.5),
  425 / 427 95.7 (97.8). The cache follows the three bitmaps, and the biome bitmap changes the heights (biomes carry
  their own height layers, VoxelWorldSettings.xml).
- 436 = 427 / 429 at that noise floor (99.994 %): **Halloween = the template Shape + HeightLayerIds with BiomeIds ee5ba3f32e (427 or 429,
  byte-identical)** [I]. VoxelContinentWorld 436 → 43 "Museum Qeynos", the setting of 427 [P table].
- 437 = 438 and unlike every other cache by ≥ 6 % (low field): the biome bitmap is one no other continent uses. The
  only template-layout bitmap not used by another continent is Continent_BiomeIds_437.bmp, Tundra, and
  VoxelContinentWorld puts 437 / 438 in 48 "Museum Tundra" (biomes 51, 80, 81, 54) [P table]: **Winter Holiday and
  Isle of Judgement = the template Shape + HeightLayerIds with BiomeIds_437** [I].
- The names the server sends: `Continent_Shape_424.bmp`, `Continent_HeightLayerIds_424.bmp`, and
  `Continent_BiomeIds_427.bmp` (460) / `Continent_BiomeIds_437.bmp` (461, 462). Any of 424-429 / 432 gives the same
  Shape / HeightLayerIds bytes; 429 the same biome bytes as 427. Whether live sent these exact file names, or files the
  0.695 install never received, is not provable without a capture; that the bytes are right is checked by the bake
  test (§4).

Unchanged for the islands: the rest of the zone details keep the islands' pattern (OpenBetaGeometry_<zone>,
OpenBetaContinent_<continent>, 6.4 m bitmap scale, origin 2558400 / 2000 / 2556450 and extents 8000 × 4000 × 8000 from
StaticZoneData). Every baked island 391-421 was generated with the 406 capture's Sky fields (Sky2 5, Sky5
"OpenBeta_Do_NOT_ChangeThisText_16") and still matches its own cache (§3), so none of those fields is per-continent
for the terrain; the world setting follows the continent id (supports L15: VoxelContinentWorld, not the packet).
StaticZoneData GEOMETRY_ID is 456 / 457 / 458 for the event zones but equals the zone id for every island, so which
SendZoneDetails field carried it on live is open (L13).

## 3. ContinentSurfaceCache .csc decoded

Reader FUN_14407b070 → FUN_14407b810 (0.695; 0.589 FUN_143fb73c0, same) [P layout, terrain-695.md §2.2]:
`u8 version 1, u32 x, u32 y, u32 z, u32 w, u32 h`, 9 LODs of `w × h` u16, row = z, each LOD halving x / y / z with
`w' = (w + 1 + (x & 1)) / 2`. Meaning, fitted on the 28 baked landscapes (gameData695/TerrainHeights391-421.bin, 409 has
none) [I, measured]:

- Texel (r, c) of LOD k is the column of global cell (x + c, z + r) with cells of 16 · 2^k m: global metres = cell × 16
  at LOD 0. Islands: x 159650 → local x 16c − 4000; z 159528 → local z 16r − 4002; y 170 → local 720 m (417 has y 184).
- `u16 = low | count << 10` (bits 8-9 always 0): the ground lies in the cells `y + low … y + low + count`. Neither
  field is a height.
- Rule that reproduces the cache from the bake: `low = floor((min of the 2 m bake over the texel's square widened by
  one sample − (y·16 − 2000) − 1) / 16)`: 406 97.09 %, all 28 between 95.54 % (397) and 97.97 % (421). The misses are
  the bake's 2 m spacing missing the 0.4 m voxel extremes. LOD k+1 from LOD k (min / max over 2 × 2, halved): 96-97 %
  (`csclod.py`), so each LOD is computed from the terrain, not merged.
- Height from the cache alone (texel range midpoint, bilinear to 2 m): error mean +0.0-0.2 m, sd 2.6-3.5 m, p50 1.6-1.8
  m, p90 4.2-5.3 m, max 23-42 m (406, 391, 410, 417). Too coarse for trees, digging and the site height check, so
  **no grid is made from it**.

Tool: `LandmarkEmu/tools/terrain/csc.py` (info / fit / pick). L16 is answered for the .csc; the .ocm grids and lists
stay open there.

## 4. Ground heights for 460-462: the bake, and the test that proves the bitmap names

The server's heights come from the client's own sampler (ContinentMapHeightmapTerrain vtable +0x58: 0.589
FUN_143fc3e00, 0.695 FUN_144087ab0, "same" in functions.tsv, identical first 48 bytes) through
LandmarkEmu/tools/terrain/bake_all.py; the hook now picks the address by the exe's PE time stamp (0.695 = 0x586d8017).
The bake samples whatever the client generated from the names the server sent, so the same run settles L13:

1. Bake zone 460 (dev command `landscape 460` works while the island is closed) →
   `gameData695/TerrainHeights460.bin` (named by **zone** id: TerrainHeights.cs looks grids up by zone).
2. `csc.py pick 460 436 427 424 437`: right names → ~96-98 % against 436 (and 427, which is the same) and the bake
   sides with 436 on the texels where 436 and 424 / 437 differ. Wrong biome (e.g. the client fell back to something
   else) → it sides with another cache or matches nothing.
3. Same for 461 (`pick 461 437 438 424 436`) and 462.

Exact steps for the coordinator: TESTING-NEEDED.md "Event islands". Where the grids go: next to the other bakes in
the server's game-data folder (GAME_DATA_DIR = <workspace>\gameData695; gameData for the frozen 0.589 branch).

## 5. Server data on the islands

- **Heights:** none until baked; without a grid the server does what it does on 409 (arrival from 1800 m above the
  centre, no height check for sites, no trees, no Spire). Grid name by zone id (above).
- **Materials:** TerrainMaterials reads the biome bitmap through `BiomeBitmapOf(zone)` (set by Landscapes): 460 →
  Continent_BiomeIds_427.bmp, 461 / 462 → Continent_BiomeIds_437.bmp; biome yields come from VoxelBiomeMaterials as for
  every landscape.
- **Caves:** CavePieces / Caves / LootChests / WarpNetwork / Outposts read ContinentCavePlacements[<zone or home>]: no
  file for 460-462 (none for 436-438 either) [P: fileindex_695.tsv, terrain-695.md §2.3] → no caves, no cave chests, no
  Underground Adventure there; no VoxelContinentToLayerMapping row for 436-438 [P].
- **Ore veins:** OreVeins.ZoneOf(460-462) finds no ContinentVeinData<zone>.vdt → no veins [P: no .vdt for 436-438].
- **Spire:** Spire.Create stamps one on any landscape with ground heights; after the bake the islands get one like every
  island. Whether live's event islands had a Spire is not known (L110).

## 6. The "Events" world

Sources [W]: forum 57978 (23 Sep 2016) "open up the Halloween Party Island again ... available for anyone to build on
who would like to stake a claim there"; 57987 (29 Sep, patch notes) "a seasonally themed island where everyone is free to
place a build site"; 58006 (3 Oct, Domino) "You can access the Halloween Party Island via the teleport menu, on a new
world called "Events""; #3 (9 Oct, player) logging out on the island → "you can't log back in directly onto it - must
select one of the regular "worlds" (servers)"; 58077 (21 Nov) "As of this morning's bounce the Halloween Party Island is
no longer open on the Events world, and instead the Winter Holiday Island is up ... at least till early January. Any
build sites that were left behind on the halloween island will be expired this morning."; 58108 (17 Dec) up "until we
return from the holiday break". 2015 beta: the Halloween island was on the "Museum" world (55778). No source mentions the
Isle of Judgement being opened.

Built (Game/Landscapes.cs):
- **Setting** `EventIsland` (WorldServer.json; Docker `EVENT_ISLAND`, docker/world/docker-compose.yml): `auto` (default:
  Halloween 3 Oct - 20 Nov, Winter 21 Nov - 10 Jan, UTC, every year; the 10 Jan end is ours, as RareDrops' winter),
  `halloween`, `winter`, `judgement`, a zone id, or `off`. Read once at start-up ("switched at a restart") [W].
- **Locations** (TeleportBase sub 3, reader FUN_1430c4150): a second world location (FUN_1430c2ad0 layout, the same as
  the main world's) `{3, 567016, 20, 1 zone}` after the main world, only for 0.695 clients and only while an island is
  open. TeleportRequest {world, zone} → Landscapes.TravelTo(zone) as for every landscape (refused for 0.589 clients).
- **World id on the island:** SendZoneDetails Unknown8 / Unknown9 (2 / 560912 "Liberation" in the 406 capture) and
  ClientBeginZoning +0xa8 (the client's current world +0x378cc, which the window highlights) are 3 / 567016 there. The
  0.589 dispatcher (part_0065 ~44895) treats a different world exactly like a different zone name (zone change), so
  this adds no new client path [I].
- **Site lists:** ClaimsBase sub 8 (Site Management) writes each site's world (`Landscapes.WorldOf`) so a site on the
  island shows under Events.
- **Login on a closed island:** the character starts at the home Spire (`LeaveClosedEventIsland`, ours; live could not
  log in there at all, 58006 #3).
- **Not in the main world's lists:** `Landscapes.Zones` (used by Pioneer for new players' sites) holds only the main
  world's landscapes; `Landscapes.Get` also answers the open island (site placement, achievements, chat locations).
- **Not built (L110):** expiring the sites left on an island when it closes (live did, 58077); they stay, unreachable
  from the menu, until the island opens again or their owner deletes them.

## 7. Files

Game/Landscapes.cs (event islands, Events world, zone details, login move), WorldServerConfiguration.cs (EventIsland),
EnvServer.Core Terrain/TerrainMaterials.cs (BiomeBitmapOf), Teleport.cs (world id in ClientBeginZoning), Claims.cs (site
world), DevCommands.cs (`landscape` reaches closed islands), Network/WorldSession.cs (login hook),
docker/world/docker-compose.yml (EVENT_ISLAND), tools/terrain (hook build detection, csc.py, README). Scratch scripts of
this trace: cscfit.py, cscfit2.py, cscpredict.py, cscrule.py, cscall.py, csclod.py, zd.py (session scratchpad).

## 8. Open leads

L13 (names: test pending; GEOMETRY_ID 456-458 field) · L15 (supporting evidence added) · L16 (.csc answered) · L109
Events world id / name text · L110 event island content and rules (site limit, Spire, dev builds, expiry at the switch).
