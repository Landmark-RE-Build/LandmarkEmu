# Procedural placement: the shipped sets and the server's tree placement (2026-10-16)

TASKS §7 "Procedural placement". [P] = proven from the client / its data / a developer post; [I] = inferred (ours),
with the reason. Built in `LandmarkEmu/LandmarkEmulator.WorldServer/Game/ProceduralPlacement.cs` + `Game/Gathering.cs`.
Tools (scratchpad, not kept in the repo): table dump (sets → props → models), surface-code histogram, mask statistics,
.adr collision check, a numpy density simulation and a C# harness that ran the built `ProceduralPlacement.Region` over
whole landscapes.

## 1. What existed before this trace

- trace/0695/agent-L1-L2.md: the 13 masks found (`CommonData\ProceduralPlacementTool\Masks`, loose 24-bit BMPs, same
  in both builds); the two server-only tables (ProceduralPlacementEntityLists / EntityListEntities) are not shipped,
  their schemas live in the client only for the 0x10A admin page 28 packet; ProceduralPlacementTool is Daybreak's
  offline tool (`CommonData\Voxel\veinData\HowToGenerateVeins.txt`).
- trace/datatables.md: the four ProceduralPlacementSet* tables have no client loader.
- architecture/admin-pages.md §5 / §7: no spawner used the sets; page 28 reply plan.
- Gathering.cs (before): trees per 64 m region from its own lists (biome 50 Old Growth pines at 0.16 per 8 m spot,
  biome 47 volcanic trees at 0.05, every other biome 5 "fallback" models at 0.03), regrow 30 min.

## 2. The client never loads the sets [P]

- 0.695 decompile `re/decomp_all_695` (238 parts): no hit for `ProceduralPlacementSet`, `AVERAGE_DISTANCE`,
  `SOIL_SUITABILITY`, `SPAWN_CHANCE`, `ENCROACHMENT_PRIORITY`, `SETS_ID`, `SET_ENTITIES_ID`, `PLACEMENT_DATA_GROUP`.
- Raw scan of the 0.695 `Landmark64.exe` (ASCII and UTF-16): none of `AVERAGE_DISTANCE`, `SOIL_SUITABILITY`,
  `SPAWN_CHANCE`, `ENCROACHMENT_PRIORITY`, `PLACEMENT_DATA_GROUP_ID`, `SET_ENTITIES_ID`, `ProceduralPlacementSet`,
  `ORIENT_TO_SURFACE`, `MASK_SCALE`. (0.589: agent-L1-L2.md, same result.)
- The four tables are byte-identical in gameData (0.589) and gameData695.
So these are server / tool rules that shipped with the client. Whatever the server places from them reaches the client
as ordinary props (PlayerUpdate AddLightweightProp), exactly as Gathering's trees always did.

## 3. What the sets contain [P]

- **PROP_ID = PropDefs id.** The PropDefs names fit every row (e.g. 345 "Old Growth Tree (Medium) 1", 333 "Desert
  Cactus (Double)", 808 "Tundra Boulder 1"); read as Models.txt ids they make no sense (character bodies, furniture).
  The prop → model step is the server's prop definition dump (`Resources/PacketDumps/145-PropBase`, `Props.ModelOf`).
- **104 entities, 106 models**, all `Biome_*` props that players can also craft (every PropDefs row has a
  PROP_RECIPE_ID). Kinds by model name:
  - trees (`_Trees_`): Old Growth jagged / gnarled / diamond / hollowed pines (338, 342, 343, 345-349, 376, 384, 453,
    454) — the very models Gathering already used for biome 50; Tundra firs, grove trees, spindle pines, centipines
    (330-335, 556, 557, 658, 659); Jungle banyans and palms (186, 205-209); Desert cacti, paddle / small / thornleaf
    trees, dead trees and a dead log (242-248, 323-325);
  - sickle plants (`Generic_Plants_`): Amberleaf, Cotton, Earthblood, Flax, Jute, Silkfall, Vilebloom (464, 472, 476,
    484, 501, 510, 517);
  - decoration: mossy / snow / jungle / desert rocks and boulders, palmetto bushes, ferns, shrubs, scrub, succulents,
    Bloodtear / Nightbloom plants, tundra shoots, berry bushes, flowers, fungal mounds.
- **108 sets** (31 columns). Each set: one alpha-era biome, a placement data group, MAX_SPAWNS 50,000-150,000,
  respawn 12-18 min (sets 154-155: 18-28; 168: 12-20), slope 0-10 … 0-55°, altitude 3000-4000, AVERAGE /
  MINIMUM_DISTANCE 6/2 … 70/45, ORIENT_TO_SURFACE (rocks, bushes, some trees), a MASK + MASK_SCALE (24 sets none),
  ENCROACHMENT_PRIORITY 0-10. SetEntities: SPAWN_CHANCE 1-27 (a weight), MAX_SPAWNS 10,000 on every row, OFFSET_Y
  0 … -50, SCALE 0.5-2. SetMaterials: 367 rows, SOIL_SUITABILITY 1 on every row.
- **MATERIAL_ID = VoxelMaterials id**, and the baked height grid's surface code is that material's **ARRAY_INDEX**:
  histogram of `TerrainHeights406.bin` codes in biome 50 = 303 / 295 / 304 / 305 / 364 / 299 / 297 … = Ancient Forest
  Clovers Lowland / Grass Midland / Clovers Lowland Dry / Moss Midland / Rock 03 / Grass Lowland / Dirt Lowland
  (VoxelMaterials 423 / 413 / 425 / 426 / 437 / 417 / 415); biome 47 = Volcanic Rock *, 48 = Desert *, 49 = Tropical *,
  52 = Deciduous *, 54 = Open Ocean * (391, 400, 406, 410 checked). So soil can be tested against the real ground.
  (TerrainHeights.SurfaceMaterialAt's comment "not a VoxelMaterials id" is right: it is the ARRAY_INDEX.)
- **The sets are alpha-era.** Their BIOME_IDs are 18-31 (Desert / Tropical / OldGrowthForest / Tundra
  _Small/_Medium/_Large_001, VoxelBiomes BIOME_TYPE_ID 1-4), which only the "Alpha_*" / "Desert" / "Tundra" /
  "OldGrowthForest" world settings use (VoxelWorldSettingBiomes 5, 7-14). Every 0.695 landscape's biome map
  (Continent_BiomeIds_391-421) holds only 47-52 and 54 (the "OpenBeta_*" world settings 18-32 and the single-biome
  33-39). **No set names a launch biome**; the launch-era sets were not shipped (lead L113).
- Placement data groups: Tropical 1 / 7 / 8, Desert 2 / 10 / 11, Old Growth 3 / 12, Tundra 4 / 13 / 14 / 15 / 16; groups
  of one biome are complementary layers, not alternatives (Tundra's ForestA / ForestB / Field masks of groups 4 / 14 /
  15 tile together). Group 17 (set 170, biome 32 Deciduous, mask TilingTestZZ.bmp — never shipped, L1) and group 18
  (171-172: altitude ±1,000,000, MAX_SPAWNS 100, respawn 1-5 min, biome 10 "OLD_Tropical") look like tests.
- Collision: every set tree model's .adr names a .cdt that exists, except 344 GnarledPine_Large01 (set 108; type
  3099813281, no CollisionData — known since MODLOG 2026-10-09). All decoration bushes / flowers / shrubs have none
  (walk-through, like the client's own grass); all rocks have one.
- Masks: binary or grey. The 1024² DualForests masks tile seamlessly (edge difference = interior difference); the
  4096² Forest / Clumps masks do not. Mean coverage: MainTrees01 14.5 %, MainTrees02 22.7 %, Undergrowth01 57 %,
  Undergrowth02 85.5 %, Clearing01 28 %, Spots01 8.5 %, Clumps Field02 7 % (grey), Clumps Inner02 27 % (grey),
  DualForests Field01 34.5 %, ForestA01 22 %, ForestA02 24 %, ForestB01 28 %, White01 100 %.

## 4. How live used them [P / I]

- Developer post (Domino, 19 Jun 2015, forum 55352 "A note on respawn issues in Landmark") [P]: at server start
  "resources that spawn and should respawn (such as trees, plants, and NPCs)" are "randomly populated across an
  island … per layer and per biome … for each thing that can spawn"; there are many more possible locations than
  spawns; each type has a minimum and maximum per island; when a type falls below its minimum another spawns "within
  about 20 minutes" at a random allowed location of that biome, not where it was cut. Overnight restarts reset spawns.
- That matches the server-only tables' schema [P] (EntityListEntities: MIN_SPAWNS_PER_ZONE / MAX_SPAWNS_PER_ZONE /
  SPAWN_WEIGHTING per entity of an entity list, LAYER_TIER_WHITELIST_ID) and page 28 ("Zone Node Count", "Nodes
  Remaining", per layer and biome). Reading [I]: the sets + masks were the ProceduralPlacementTool's input that produced
  the candidate **nodes** (locations); the entity lists said what spawns on those nodes and how many. The 12-18 minute
  respawn of the sets fits "within about 20 minutes".

## 5. What the server does now (ProceduralPlacement.cs, Gathering.cs)

| Rule | Source | How |
|---|---|---|
| Which sets | [I] | Surface biome → VoxelBiomes BIOME_TYPE_ID → the sets of that type's `_Large_001` biome (Tropical 31: sets 24, 27, 39, 76, 79, 82, 85; Desert 20: 54, 88, 91, 94, 97, 101, 104, 107; Old Growth 22: 108, 114, 115, 120, 121, 124, 127, 130, 136; Tundra 26: 133, 139, 142, 145, 148, 151, 154, 157, 160, 163, 167). The Small / Medium copies differ only in a few distances / respawn times. Groups 17 / 18 left out. Bridge evidence: same type and the same ground materials (biome 50's surface is made of the materials the Old Growth sets list). |
| No sets | [P] | Volcanic (47), Deciduous (52; only test set 170) and Ocean (54) keep Gathering's old lists. |
| Spacing | [I] | One candidate per AVERAGE_DISTANCE grid cell (jittered: ≈ one per AVERAGE_DISTANCE² m²); dropped if a same-set neighbour within MINIMUM_DISTANCE has a higher priority (compared before any ground check, so every region agrees). |
| Altitude | [I] | ground + 2000 m (TerrainStore.ZoneOrigin.Y 20000 dm) within MIN/MAX_SITE_ALTITUDE: 3000-4000 = local 1000-2000, i.e. above the sea (~1002 local). |
| Slope | [P columns, I unit] | ground normal angle from vertical, degrees, within MIN/MAX_SITE_ANGLE. |
| Soil | [P] | the baked grid's surface code (VoxelMaterials ARRAY_INDEX) must be one of the set's SetMaterials (SOIL_SUITABILITY as a chance; all 1). Code 0 (not sampled) passes. |
| Mask | [I] | pixel = (local + 4000 m) × MASK_SCALE, wrapping; the value / 255 is the chance. Masks are box-averaged to ≤ 1024² (1 MB each) and read in the background at start. Missing mask = no mask. Setting ProceduralPlacementMasks. |
| Encroachment | [I] | sets run in ENCROACHMENT_PRIORITY order (high first); a candidate whose footprint (Models.txt PATH_BLOCK_RADIUS × scale) overlaps one already placed is dropped. Regions evaluate a 16 m margin so neighbours are seen. |
| Entity | [P weights] | picked by SPAWN_CHANCE; scale = random MIN..MAX_SCALE × entity SCALE; heading / pitch / roll random in the set ranges + entity offsets; position offset random in the set's MIN..MAX_OFFSET (metres) + the entity's OFFSET (read as centimetres [I]: up to -50, which as metres would bury a tree); ORIENT_TO_SURFACE tilts to the ground normal, other entities settle 0.3 m (ours, as before). |
| Kinds | [I] | `_Trees_` models = axe trees (5 hits, 8 when the footprint ≥ 4.5 m); wood by type: Desert Striped (73, wiki Striped Wood), Old Growth Burled (72, P5415), Tropical / Tundra Plain (71). `Generic_Plants_` = sickle plants: only while plants are harvestable (off since 21 Mar 2016, P4128); everything else = decoration, off by default (setting ProceduralPlacementDecor), shown but not gatherable. Disabled kinds take no room. Model 344 (no collision) left out. |
| Respawn | [P range, I place] | a gathered set node returns after a random MIN..MAX_RESPAWN_MINUTES (12-18 on most sets; was 30 min for all trees). Live respawned elsewhere in the biome (§4); here it regrows in place (lead L113). |
| MAX_SPAWNS | [P] | per zone, 50,000-150,000 per set and 10,000 per entity: never reached on an 8 km landscape (the harness's busiest set placed 12,355 on 406), so not enforced. |
| Determinism | ours | a 64-bit hash of (landscape, set, grid cell) drives every random choice: a region is identical after a restart and the old "key ticks" save file keeps working. Set nodes use key indices 128-255 (0-63 old trees, 64-127 old plants). |
| Bandwidth | ours | unchanged streaming: 64 m regions sent within 160 m, dropped beyond 224 m. |

Switch: WorldServer.json / environment `ProceduralPlacement` (default **true**), `ProceduralPlacementDecor` (false),
`ProceduralPlacementMasks` (true). With `ProceduralPlacement: false` the old lists are used everywhere.

## 6. Density check (C# harness over whole landscapes, trees only, masks on)

Per 64 m region of land above the sea, before → after ("before" = a numpy simulation of the old lists with the same
ground / slope / sea filters, claims ignored; "after" = the built C# code run over the landscape, claims ignored):

| Landscape | Biome | Before | After |
|---|---|---|---|
| 406 (home) | 50 Old Growth | 7.5 | 3.7 (sets 115: 12,355, 124: 3,459, 108: 2,624, 114: 1,258, 121: 1,002, 127: 821) |
| 406 | 47 Volcanic | 2.1 | unchanged (old list) |
| 391 | 48 Desert | 1.0 | 2.3 |
| 391 | 49 Tropical | 1.2 | 2.1 |
| 393 | 49 Tropical | 1.3 | 2.3 |
| 393 | 51 Tundra | 0.9 | 1.1 |
| 401 | 50 / 51 with decoration on | — | 5.0 / 3.3 all entities (trees 12,185 + decoration 16,121 on the landscape) |

Within 0.5-2.3× of before on every set biome, so the switch defaults on. Region generation: 0.11-0.18 ms; most
entities in one region: 62 (391, desert cactus clumps), limit 128. Same region twice gives the same placements.
Tropical palms (sets 79 / 85) never place on the launch landscapes: their soil is Tropical Sand Beach, which the
launch maps paint in the Ocean biome (54) instead — a consequence of the alpha-era data.

## 7. Open (OPEN-LEADS)

- L1: TilingTestZZ.bmp (set 170, a test set not used).
- L2: rows of ProceduralPlacementEntityLists / EntityListEntities (per-zone min / max per entity, weights, layer tiers).
- L82: page-28 fields; now that set placements exist, the 0x10A builder can report per-biome set / entity counts.
- **L113 (new)**: the launch-era sets for biomes 47-52; the unit / mapping of MASK_SCALE, the SetEntities offsets,
  AVERAGE_DISTANCE's exact meaning, ENCROACHMENT_PRIORITY and PLACEMENT_DATA_GROUP_ID (the tool's Continent_XXX.ini);
  whether live's launch landscapes had server-placed rocks / bushes; live's respawn-elsewhere with per-zone minimums.
