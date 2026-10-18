# 0.695 terrain data: what changed and what the server must do (traced 2026-10-15)

Sources: the two installs (read-only), `update-0589-to-0695-files.csv`, `re/tools/tablediff.py`, the decompiles
(`re/decomp_all` 0.589, `re/decomp_all_695` 0.695), `trace/0695/functions.tsv`. Scratch scripts and raw outputs:
`re/out/agent_terrain/` (csc.py, cscmatch.py, ocm.py, otddiff.py, otdvox.py, otdsum.py, otdhist.py, x64dis.py,
text695.py; otddiff.txt, otdhist.txt). All 0.695 addresses unless marked 0.589.

## Summary

1. **No terrain protocol change.** Every terrain packet reader / writer and VoxelCellBlockCache handler the server
   talks to is byte-for-byte "same" in 0.695 (table in §5). The server's cell manifest / CellsUpdate / ApplyDiff code
   needs nothing new for 0.695.
2. **The 3 new .ocm + 3 new .csc are the event islands' client-side precomputed data.** File numbers are continent ids
   (the client formats them with engine +0x5208, §2.1). Continents 436 / 437 / 438 = zones 460
   **HalloweenPartyZone_460** ("Halloween Party Zone"), 461 **ChristmasPartyZone_461** ("Winter Holiday Party Zone"),
   462 **IsleOfJudgementZone_462** ("IsleOfJudgement Zone") in StaticZoneData (rows unchanged since 0.589). They are
   built on the developers' "Museum" template island: their ocean coastline grid is identical to continents 424 / 427 /
   429, the Halloween surface cache matches continents 427 / 429 (deciduous) at 99.99 %, Winter and Judgement match each
   other (99.98 %); VoxelContinentWorld puts 436 in world setting 43 "Museum Qeynos" and 437 / 438 in 48 "Museum
   Tundra"; Continent_BiomeIds_437.bmp (already in 0.589) is the template island painted Tundra. The islands ship **no**
   Shape / HeightLayer bitmaps of their own, no cave placements, no vein data and no underground layer.
3. **The one new continent layer is not a holiday island**: VoxelContinentLayers 496 `391_Layer2` + mapping 1125
   (continent 391 = Arid Thicket) + 3 VoxelLayerBmpToPropGroups rows pointing at the **tier-2** property groups of the
   same cave biomes (234 JungleCavern t2, 228 DesertCavern t2, 258 Bedrock t2), TELEPORT_IMAGE_ID 10229 (all other
   layers 10228). A second, deeper underground layer for one island. No cave placement data for it exists (§3).
4. **The 32 changed .otd are all `ruin_*` cavern ruins** (Environment Server ruin layers), re-exported: same size and
   cell set, each shifted inside its box (props move by one constant offset), the air / material-65532 envelope
   redone, and **spawner props added (1,121 → 1,750)**, mostly "Spawner: Ruins: Chests (1 Limit)". The builds' solid
   voxels are unchanged in 20 of 32. The 5 "new" .otd are 2 Landmarks-of-Landmark winners that were **0-byte files in
   0.589** (now with content under new names), a re-saved copy of the Sairyss mirror, `lm_eleven_tree_fixed` and a
   player design (`dalinsia's_befallen_dungeon…`). Every prop definition they use exists in the 0.589 PropDefs.
5. **Mesh Shapes need nothing from the server.** The 30 new VoxelShapes rows are client meshes (.dme in the packs,
   no SERVER_FILE_NAME, RESOURCE_COST 0). Mesh.SelectMesh / PlaceMesh only switch the client to its StampBrush tool
   and select the palette entry locally; the shape id is never sent. The edit arrives as the usual ApplyDiffRequest
   (writer unchanged). ToolPacketActivate carries the StampBrush tool definition, as in 0.589.
6. **Server changes the evidence supports** (§6): gate the event islands to 0.695 clients; give Landscapes /
   SendZoneDetails a way to name bitmaps for continents that have none of their own (which ones live sent is an open
   lead, L13); per-continent server data (heights, biome map, caves, veins) for 436-438; read templates from the 0.695
   install; spawners in ruin templates once ruins are placed. Unresolved items: OPEN-LEADS L13-L17.

## 1. Every new / changed terrain file and row

### 1.1 CommonData/Voxel files (update-0589-to-0695-files.csv)

| File | Status | What it is |
|---|---|---|
| ocean/ContinentOceanMap436/437/438.ocm (2,642,580 B each) | new | Ocean map, version 3, of continents 436 / 437 / 438 (Halloween, Winter Holiday, Isle of Judgement) |
| surfaceCache/ContinentSurfaceCache436/437/438.csc (670,505 B each) | new | Surface cache of the same continents |
| templates/ruin_*.otd (32) | changed | Cavern ruins, re-exported with spawners (§4) |
| templates/landmarks_of_landmark_2016_winner_-_landmark_competition__stepford_embassy.otd | new | Was `…_-_stepford_embassy.otd`, **0 bytes** in 0.589 (gone from 0.695) |
| templates/landmarks_of_landmark_2016_winner_-_s.a.g.7__hangars.otd | new | Was `…_-_sag7.otd`, **0 bytes** in 0.589 (gone from 0.695) |
| templates/sairyss_summoning_mirror.otd | new | Same 44 cells (identical blobs), size and 51 props as the 0.589 `landmarks_of_landmark_2016_winner_-_sairyss_summoning_mirror.otd` |
| templates/lm_eleven_tree_fixed.otd | new | 186×248×164 voxels, 57 cells, 32 props (no `lm_eleven_tree` existed before) |
| templates/dalinsia's_befallen_dungeon_re-imagined!_2016_04_29_23_19z.otd | new | 525×450×525, 759 cells, 990 props; a player design export (name carries a 2016-04-29 timestamp) |

Nothing else under CommonData/Voxel changed: no .cpa (caves), .vdt (veins), .cav, .bmp, VoxelWorldSettings.xml or
project.dat (the CSV lists none; `diff` of both folders gave only the files above).

### 1.2 Resources tables (`python re/tools/tablediff.py <Table>`)

| Table | Change | Meaning |
|---|---|---|
| VoxelContinentLayers | + `496^391_Layer2^Continent_Underground_BiomeIds_00019.bmp^1^83^10229` | 2nd underground layer of continent 391 (§3) |
| VoxelContinentToLayerMapping | + `1125^391^496` | continent 391 → layer 496 (391 already maps to 364 `391_Layer1`, row 1119) |
| VoxelLayerBmpToPropGroups | + `1610^496^250^234`, `1611^496^125^228`, `1612^496^20^258` | layer 496 bitmap values 250 / 125 / 20 → property groups 234 / 228 / 258 |
| VoxelBiomes | 6 rows (35 JungleCavern, 36 DesertCavern, 41 AncientForestCavern, 45 CrystalCavern, 62 DeciduousCavern, 63 VolcanicCavern) | only AUDIO_ENV_SUBMERGED and AUDIO_ENV_SURFACE: `Env_Underwater` / `Env_Ext_AboveWater` → `Env_Int_Underground` (client audio) |
| VoxelMaterials | row 6 (ARRAY_INDEX 6, text 558287 "Biome_Jaggedpine_Clover01") MAX_LEVEL 20 → 0; row 430 (ARRAY_INDEX 357, "Vertical Grain Striped Wood Planks", ITEM_ID 2002) INCLUDE_IN_TRAY 1 → 0 | client rendering / tray; ARRAY_INDEX → ITEM_ID (all the server reads, BuildMaterials.cs) unchanged |
| VoxelShapes | +30 rows 179-208 | Mesh Shapes (§5) |
| MeshCategory (new, 4 rows), MeshCategoryGroup (new, empty), MeshCategoryMeshMap (new, 14 rows) | new | Mesh Shapes tab categories (§5) |

Unchanged and relevant: StaticZoneData, VoxelContinentWorld, VoxelWorldSettings, VoxelWorldSettingBiomes,
VoxelLayerPropertyGroups, ExportedTemplates, VoxelSystemTemplates (tablediff prints nothing for them).

## 2. The event islands: continents 436, 437, 438

### 2.1 File numbers are continent ids

- 0.695 FUN_143ff3730 builds `Voxel\ocean\ContinentOceanMap%d.ocm`; the disassembly (`x64dis.py`, 0x143ff376f
  `mov r8d, [rcx+0x5208]`, then `call 0x142b24220` at 0x143ff37b7) shows the `%d` is the engine's int at +0x5208
  (Ghidra dropped the vararg). FUN_143ff3fc0 does the same for `ContinentSurfaceCache%d.csc` (+0x5208, `21000` in
  the decompile). +0x5208 is set together with +0x5aa8 by FUN_143f9aa30 / FUN_143f9aad0 / FUN_143f94580.
- The set of files matches StaticZoneData CONTINENT_ID, not GEOMETRY_ID: files exist for 424-430, 432-434, 436-438,
  440-444; there is no 431 (a geometry id of zone 437, never a continent id), and geometry ids 436-438 belong to zones
  441-443 "Antonica_1_0" etc. (continent 430's 4000 m grid, text 571844…), which are not islands.
- StaticZoneData (both builds): `460^456^1^572668^HalloweenPartyZone_460^436…`, `461^457^1^572672^ChristmasPartyZone_461^437…`,
  `462^458^1^572679^IsleOfJudgementZone_462^438…`, all with origin 2558400 / 2000 / 2556450 and extents 8000 × 4000 ×
  8000 (same as the islands 391-416). Text (0.695 en_us): 572668 "Halloween Party Zone", 572672 "Winter Holiday
  Party Zone", 572679 "IsleOfJudgement Zone".

### 2.2 What the files contain

**.csc** (reader FUN_14407b070 → FUN_14407b810, 0.589 FUN_143fb73c0, same): `u8 version 1, u32 x, u32 y, u32 z, u32 w,
u32 h`, then 9 LOD grids of `w × h` u16, each LOD halving (w+1+(x&1))/2 and x/2. Every csc: origin (159650, 170,
159528), 501 × 501 at LOD 0 (≈ 8 km at 16 m per texel). The meaning of the u16 (low byte 0-~65, high byte multiples of
4) is not decoded (lead L16).

**.ocm** (reader FUN_14407c470, VoxelFarm::VoxelOceanHeightAsset; 0.589 FUN_143fb87c0, same): `u32 version`, `u32 a,
u32 b`, `u32 n + n × u16` (grid 1), if v > 1 `u32 c, u32 d, u32 n + n × u16` (grid 2) + `u64`, if v > 2 four lists
`u32 n + n × (u32, u32)` (FUN_14407c370). 436-438: v3, 512 × 512 grid 1, 1024 × 1024 grid 2, lists 698 / 570 / 810 / 558.

**Matching** (`cscmatch.py`, `ocm.py`):
- ocm grid 1 (md5 12d2432d) and the four list sizes are identical for 436, 437, 438 and the template islands 424, 427,
  429; grid 2 differs per continent. Continents 424-429 and 432 share one Continent_Shape bitmap (md5 e75d937c6e) and
  one HeightLayerIds bitmap (c5db546028).
- csc LOD 0: 436 = 427 and 429 on 99.99 % of texels (low byte 100 %); 437 = 438 on 99.98 %; 437 vs 424 86.7 %.
  427 and 429 share BiomeIds bitmap ee5ba3f32e ("DeciduousForestSurface 48.4 %").
- Continent_BiomeIds_437.bmp (in 0.589 too): Ocean 49.4 %, TundraSurface 48.4 %, TundraSurfacePlain 2 %, Path 0.3 % —
  the exact split of the template island 424 (OldGrowth) and 427 (Deciduous), with Tundra.
- VoxelContinentWorld: `254^436^43`, `255^437^48`, `256^438^48` → VoxelWorldSettings 43 "Museum Qeynos" (also 427), 48
  "Museum Tundra" (also 439, 441). VoxelWorldSettingBiomes 43 = biomes 52, 54, 73, 76; 48 = 51, 80, 81, 54.

So: the Halloween island = the template island with the Qeynos (deciduous) biome set; the Winter Holiday island and the
Isle of Judgement = the template island with the Tundra set. Which bitmap file names live put in SendZoneDetails for
436 and 438 (no Continent_*_436/438.bmp exist) is not proven — lead L13.

### 2.3 What the islands do not have

No ContinentCavePlacements[436-438]_*.cpa, no ContinentVeinData436-438.vdt, no Continent_Shape / HeightLayerIds /
BiomeIds for 436 and 438 (re/out/fileindex_695.tsv, `find CommonData/Voxel -iname "*43[678]*"`), and no
VoxelContinentToLayerMapping row for 436-438 (no underground layer). The islands' own content (Halloween props, any
voxel structures) is not in client terrain files: no template with a seasonal name was added (§1.1).

## 3. The new layer 496 `391_Layer2`

- Continent 391 is zone 391 "Arid Thicket" (text 571452; Tropical 50 % / Desert 49 %, re/out/continents.tsv). Its
  first layer 364 maps bitmap 250 → group 233 (biome 35 JungleCavern, tier 1), 125 → 227 (36 DesertCavern, tier 1),
  20 → 257 (65 Bedrock, tier 1). Layer 496 maps the same values to 234 / 228 / 258 = the same biomes at **tier 2**
  (VoxelLayerPropertyGroups `ID^BIOME_ID^TIER`). Those tier-2 groups existed in 0.589 but no layer used them.
- Row parsers: VoxelContinentLayerDefinition FUN_144047f00 (vtable slot 0; UNDERGROUND_BIOME_MAP +0x28, CAVE_DENSITY_PCT +0x40, TELEPORT_IMAGE_ID +0x48), mapping
  FUN_14407ae40 (CONTINENT_ID +0xc, LAYER_ID +0x10).
- ContinentCavePlacements[391]_Underground.cpa (unchanged since 0.589) holds 3,268 pieces with property groups 227
  (1,851) and 233 (1,417) only; `_Surface` holds 2,936. No piece uses 234 / 228 / 258. The client only builds
  `ContinentCavePlacements[%d].cpa`, `…_Surface.cpa`, `…_Underground.cpa` (FUN_143ff1310 area, part_0163 ~19231-19506),
  so there is no per-layer placement file. How the client uses a second layer row for one continent, and where tier-2
  caves would come from, is open (lead L14). The warp network files nodes per layer = cave-tile tier
  (WarpNetwork.cs notes: FUN_142e03940 → FUN_143f34cb0), so a tier-2 layer would be a second underground travel level.

## 4. The 32 changed .otd (all cavern ruins)

Format unchanged: the prop-list reader 0.589 FUN_143470510 / 0.695 FUN_1435225f0 differs only in struct offsets of
the in-memory template (+0x90/+0xa0 → +0x118/+0x128); all files are still version 6 with type-4 cells. After the props
each file carries a trailer that starts `u32 8, u32 length` followed by a JPEG (observed in the bytes, not traced in the reader; a preview image; e.g. ruin_raenin_lostmission 0.589 and 0.695
both start `08000000 c7260100 ffd8ffe0…JFIF`), which the server never reads.

Per template (`otddiff.txt`, `otdsum.py`, `otdhist.txt`):
- **Cells:** same cell ids and template size in 31 of 32; ruin_basikle_handofbrog grew 175×250×350 → 175×250×550
  (315 → 490 cells). Cells with a different blob: 0 (raenin_lostmission) to 1,310 of 1,372 (ashlyn_coliseumraid).
- **Shift:** in each file most props moved by one constant offset (e.g. hadder_bunker +0.8 / 0 / +3.2 m for 45 of 51
  props, ansgar_darkarrowskyway −15.6 / 0 / +0.4 m for all 1,057, basikle_handofbrog +57.2 m in z), i.e. the ruin was
  re-saved at a different corner. Prop ids (u64) are all new.
- **Voxels:** solid voxels per material identical in 20 of 32 (e.g. hadder_bunker 234,390 both); the differences are
  in air (material 0), material 65532 (0xFFFC, meaning not traced: lead L16) and vertex-only voxels — the carve
  envelope around the build. Real material edits in 12: ahnzul_market +969,744 solid, butchywuff_shrineofthelotus
  +39,853 of material 293, endru_genesisstone +33,257 of 359, durwin_bridgetonowhere +22,365 of 62, maxximus_trappedsoul
  +10,760 of 64, shattari_hunterslodge, ahnzul_ancestralshrine (+1 material in its list), small ones in
  ansgar_darkarrowskyway, osmotar_grimstonehall, poem_crag, poem_lifesong.
- **Props / spawners:** spawner props 1,121 → 1,750 over the 32 (only raenin_lostmission and
  woodstein_clockworkcalamity unchanged). Mostly "Spawner: Ruins: Chests (1 Limit)" (prop 2731), plus creature and boss
  spawners (Fungus 1909, Darkfang Specter Patroller 2874, Oakmire Stag Patroller 2876, Highfall BOSS 2716, Barren BOSS
  2706, Scalewind BOSS, Ebonguard BOSS, NovaTech, Slaug, Abomination…). Every prop definition used is in the 0.589
  PropDefs (checked by id), so the 0.589 client knows them too.
- **Who places them:** no client table names a `ruin_*` file (grep of Resources/*.txt; ExportedTemplates /
  VoxelSystemTemplates unchanged); the client's Environment Server monitor page prints "Ruin Layer Info"
  (FUN_14320ea00, 0.589 FUN_143163fe0) — the live Environment Server placed ruins, server-side (BUILD-PATH Phase B 5).

## 5. Mesh Shapes (VoxelShapes 179-208, MeshCategory*)

- **Rows:** `179^577553^0^1^5^5^Theme_Meshshapes_Meshshapes_cone_low.dme^^21741^0^0^0^2` … 208 pyramid_tallest:
  RESOURCE_COST 0, SCALABLE 1, SCALABLE_INCREMENT_PER 5, MIN_ROTATION_DEG 5, FILE_NAME a `Theme_Meshshapes_*.dme`
  (30 found in the 0.695 packs, 0 in 0.589; re/out/fileindex_*.tsv), **SERVER_FILE_NAME empty** (the 0.589 rock shapes
  name a `.vfm` there), IMAGESET 21741-21770, SHAPE_TYPE 2 (same type as the 0.589 rock / magma meshes). Names (0.695
  text): Cone 1-5, cogs, tubes, cylinders, dice-style polyhedra ("Polyhedron (10 side)" …), prisms, Pyramid 1-4.
- **Categories:** MeshCategory 3 Cones (577586), 4 Cylinders (577585), 5 Polyhedrons (577583), 7 Other (576638);
  MeshCategoryMeshMap lists 14 shapes (180-182 Cones; 189-190 Cylinders; 205-208 Polyhedrons; 184-188 Other).
  The other 16 new shapes (179, 183, 191-204) are in no category. MeshCategoryGroup is empty.
- **Client path:** Lua Mesh.SelectMesh FUN_143607fe0 and Mesh.PlaceMesh FUN_143608060 (both new, identical): if no
  tool is active, FUN_143464780(tool manager, "StampBrush") → FUN_143468840 activates the tool by name hash, which
  sends ToolPacketActivate (0x117 sub 1) with the **tool definition** from FUN_143bfc1c0 (part_0105 ~28701); then
  FUN_1434cfa10(palette, id, 7) → PaletteCollectionMeshes vtable +0x20 FUN_14374e540 → FUN_14374a700 (same as the
  Shapes palette) → FUN_14346a510 stores the selected id at tool manager +0x80 (0.589 FUN_1433bc4c0, same). No packet
  carries the shape id. PaletteCollectionMeshes fills itself (FUN_14374e110, new) from a client manager
  (DAT_145437390), no send found on that path.
- **Edit:** the result is a normal ApplyDiffRequest — writer FUN_14315acb0 = 0.589 FUN_1430b0510 (same), cell list
  FUN_143100cd0 = FUN_1430b1d30 (same); its tail (Building.cs ReadEditVersions) has no shape field. The voxelizing
  machinery (VoxelMeshModelManager, DynamicVoxelMeshGenerator, VoxelMeshCursor) is in both builds' RTTI; 0.695 adds
  only VoxelFarm::Internal::PasteThread / PreviewGenerationThread.
- **Server:** nothing to send or store. Charging is per voxel material from the cells (BuildMaterials.cs), as for
  other placements; RESOURCE_COST 0 matches that (the 0.589 rock shapes' RESOURCE_COST values are not used by the
  server today either).

## 6. Terrain protocol: unchanged (functions.tsv, status "same")

| 0.589 | 0.695 | What |
|---|---|---|
| FUN_14309bbd0 | FUN_1431464f0 | CellsUpdate reader |
| FUN_14309c0c0 / FUN_14309c1b0 | FUN_1431469e0 / FUN_143146ad0 | ModifiedCellManifest / ManifestUpdate readers |
| FUN_14309bd90 | FUN_1431466b0 | DetectVeins reader |
| FUN_1430a5030 | FUN_14314fb60 | ClientCellRequest entries |
| FUN_1430aca10 | FUN_143157570 | ClientPendingRequestTimeout |
| FUN_143f529a0 / 52b60 / 52db0 / 52fa0 | FUN_144016680 / 16840 / 16a90 / 16c80 | HandleCellManifest / CellUpdateResponse / CellsUpdate / UpToDateCells |
| FUN_143f52470, FUN_143f5ce70, FUN_143f56980 | FUN_144016150, FUN_144020b50, FUN_14401a660 | block store, layer merge, request builder |
| FUN_1430b0510, FUN_1430b1d30, FUN_1430b7070, FUN_1430b7610 | FUN_14315acb0, FUN_143100cd0, FUN_143161c90, FUN_143162230 | ApplyDiffRequest writer and parts |
| FUN_1430b11e0 | FUN_14315b8a0 | ResponseObserved reader |
| FUN_142ac1800, FUN_143f85d50, FUN_143f86db0 | FUN_142b5b060, FUN_144049a00, FUN_14404aa60 | layer CRC32, CompressBlockData, SerializeBlockData |
| FUN_143fc3e00 | FUN_144087ab0 | heightmap sampler (TerrainHeights bake) |
| FUN_143c20a80 | FUN_143cdf940 | continent definition parser (ContinentId, BiomeMap, ShapeMap, HeightLayerMap, OceanHeight, DistanceField…) |
| FUN_143f2f840 / FUN_143f300d0 / FUN_143f3c220 | FUN_143ff3730 / FUN_143ff3fc0 / FUN_144000110 | ocean map / surface cache load, asset request (0x144000110 "changed" in the tsv but identical after normalising addresses) |

## 7. What the server must change (only what the evidence shows)

1. **Protocol:** nothing for terrain (§6).
2. **Event islands only for 0.695 clients.** Zones 460-462 exist in both builds' StaticZoneData, but only 0.695 ships
   ContinentOceanMap / ContinentSurfaceCache 436-438. What a 0.589 client does when they are missing is not traced
   (lead L16), so gate them by client build (memory: version strategy).
3. **Landscapes / SendZoneDetails** (Game/Landscapes.cs): today it lists zones 391-421 only and needs
   `Continent_HeightLayerIds_<continent>.bmp`; it names the three bitmaps after the continent. For 436-438 the
   continent id must be 436-438 (it selects the .ocm/.csc) while the bitmap names must point at existing files —
   candidates by the evidence: Shape / HeightLayerIds of the template island (424-429 share them), BiomeIds 427 or
   429 for 436 and 437 for 437 / 438. Unproven which live sent: lead L13. The rest of the continent definition
   (world setting, OceanHeight …) as for the other islands; VoxelContinentWorld already maps the world settings
   client-side (whether the client reads that table or the packet: lead L15).
4. **Per-continent server data for 436-438:** TerrainHeights (only gameData/TerrainHeights406.bin is baked; the bake
   uses a running client, LandmarkEmu/tools/terrain), TerrainMaterials (needs a BiomeIds bitmap; only 437 exists),
   no caves (no .cpa: Caves / "Underground Adventure" must skip them), no ore-vein cells (no .vdt: OreVeins must not
   assume one). Also an "Events" world (TASKS 3.19).
   → 2026-10-16: 3 and 4 built (names [I], .csc decoded, heights = bake test pending): [event-islands-695.md](event-islands-695.md).
5. **GameDirectory:** the server reads templates and terrain files from `WorldServer.json GameDirectory` (HostedService
   ~88, documented as the 0.589 install). For terrain data the 0.695 install is a superset of 0.589 except the two
   0-byte templates it replaced (§1.1), so pointing it at 0.695 serves both clients and gives the newer ruins and the
   real Landmarks-of-Landmark winners (TASKS 3.20).
6. **Ruins** (when the Environment Server is built): use the 0.695 ruin templates and turn their "Spawner: Ruins: …"
   props into server spawns (chests, creatures, bosses) instead of placing them as visible props.
7. **Layer 496:** nothing until L14 shows how the client uses it.
8. **Mesh Shapes, VoxelBiomes, VoxelMaterials changes:** nothing (§1.2, §5).

## Open leads raised here

L13 bitmap names for 436-438 in SendZoneDetails · L14 layer 496 (two layers for one continent, tier-2 caves) ·
L15 how the client picks a continent's VoxelWorldSettings · L16 .csc / .ocm value meaning and the client without them
(also material 65532 in ruin templates) · L17 PaletteCollectionMeshes data source (DAT_145437390). See OPEN-LEADS.md.
