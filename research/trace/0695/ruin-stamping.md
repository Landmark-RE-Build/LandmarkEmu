# Ruin stamping in the Chaos Caverns: material 0xFFFC, layer compositing, placement (traced 2026-10-15)

Question: the server stamps the 35 `ruin_*.otd` into caverns (Outposts.Load → VoxelTemplate.Stamp → TerrainEdits.ApplyDiff
on PoiLayer) and in game the cavern's own rock / ice shows through the ruin. What is voxel material 65532 (lead L16),
how do the client's layers combine with the generated cave, what is wrong on the server, and where should a ruin go
(lead L47)?

All addresses are 0.695 (`re/decomp_all_695`). **Proven** = read in the client code or measured in the files;
**inferred** = the best reading of the evidence, not proven. Scratch scripts (numpy): the session scratchpad
`ruinvox.py` / `ruincmp.py` / `checkcells.py` (template voxel classes, 0.589 vs 0.695, saved-cell check) and `cpa.py` /
`cpa4.py` (.cpa sections). Their logic is described below, so they can be rebuilt under `re/out/agent_terrain/`.

## Summary

1. **The main bug, proven on the dev world's own files:** at every start `TerrainEdits.LoadZone` removes every overlay
   voxel whose material is a `VoxelBiomes` CAVE_FLOOR / CAVE_WALL_MATERIAL_ID (44, 45, 218-231). Those are VoxelMaterials
   **IDs**, but block data stores **ARRAY_INDEX**. ARRAY_INDEX 218-231 are burled-wood planks, rough cloths,
   **230 Worn Solucite and 231 Worn Iron**, and 44 / 45 are Chaos Cavern Bottom / Wall. 22 of the 35 ruins use these
   materials (816,293 solid voxels; hadder_bunker loses 93,823 of its 234,390). Once a removed voxel has flag 0, the
   client's generated cave (rock or ice) shows in its place, which is the "cave rock clips through the ruin" seen in
   game. The dev world's cells were stamped at 19:12 and stripped by the restart at 19:21: `checkcells.py` found
   exactly the template's 231 / 219 / 230 voxel counts at flag 0 in the saved cells (§4). **The same code also wipes
   player building in those materials on every restart**, in every layer.
2. **0xFFFC = "interior empty space"** (proven). When a selection is copied, the client marks air that is enclosed by
   the build with material 0xFFFC (FUN_143f9a150 → FUN_143f9c330). The paste turns it into **air** in the "Paste Air"
   (mode 2) and "Paste Interior Air" (mode 3) modes. In the default (solids only) and Remove-Voxels modes it means
   "leave alone". Material 0 in a template is the surrounding (exterior) air. Only mode 2 writes it as air.
3. **Layers composite as the server assumed** (proven). The order is height-map terrain → CaveVoxelLayer → server block
   layers (0→3 merged, a later layer wins) → paste preview. A voxel with the material bit overwrites the material
   underneath, and material 0 is air, so it carves cave rock. A vertex-only voxel moves only the surface vertex and
   keeps whatever is underneath, solid or air. The mesher (ContourEx) treats 0xFFFC as empty, so the server's raw
   0xFFFC voxels draw as air. 0xFFFC is still not what live wrote, though: the paste wrote material 0.
4. **Placement.** Live had precomputed **ruin slots**: the `.cpa` CavernInstanceData "rooms" (proven layout). On
   continent 406 Underground there are 81 caverns and 4,030 slots. Each slot is 100 m tall, and the footprints are
   exactly the ruin footprints: 40×40, 70×70, 70×140, 140×70, 70×220, 220×70 and 220×220 m. A slot's floor is
   8.0-8.4 m above the cavern's box bottom. Slots are voxel-aligned, not cell-aligned. Every ruin template has the
   same structure: a 0.4 m vertex-only border at x/y/z = 0, then an explicit box of Size voxels (air, 0xFFFC or solid),
   with its lowest solid voxels in the first content row. So live put a ruin into a slot whose box matches the
   template and carved the whole box (inferred). The server's corners are 2-9 m off the slot floors and are rounded to
   16 m cells (§5).
5. Secondary issues: ore-vein voxels (layer 2) win over the ruin (layer 0) in the client merge, and the server
   generates cave veins without knowing about stamps. 0xFFFC kept raw counts as solid in server code and in the
   client's smoothing utility.

---

## 1. What 0xFFFC is

### 1.1 Where it is written: the interior-air pass (proven)
- `FUN_143f9a150(impl, clipboard)` (wrapper FUN_143f9a670) sweeps the clipboard along the three axes and calls
  `FUN_143f9c330` per axis order. Along each line it collects the non-solid voxels (no material bit, or material 0)
  that lie between solid voxels, and keeps a bitmask across the passes. When a voxel is enclosed again, it sets the
  material bit and **material = -4 (0xFFFC)** (`local_a0[0] |= 1; local_9e = -4;` then clipboard vtable +0x20 write,
  part_0161 ~21807).
- The "solid" test of that pass is the lambda FUN_143f9cc80 (`std::function<bool(Voxel&)>`, RTTI 14499eaa0):
  material bit set and material in 1..0xFFFC, or 0xFFFF. An already-marked 0xFFFC therefore counts as "filled" for
  the later passes.
- So 0xFFFC = **enclosed (interior) air of a build**, as opposed to material 0 = the exterior air that surrounds it in
  the copied box.

### 1.2 How the paste uses it: VoxelFarm CClipboardData paste FUN_142a7eeb0 (proven)
Signature: `(writer, clipboard, worldLookup, cell, x0, y0, z0, offset*, size*, mode, asDelete, dirtyCells)`. Per
clipboard voxel (flags `f`, material `m`):

| Template voxel | default (mode 0/1) | mode 2 "Paste Air" | mode 3 "Paste Interior Air" | mode 4 |
|---|---|---|---|---|
| solid m | write m | write m | write m | write m |
| air (m = 0) | material dropped (vertex kept only next to real solid, FUN_142a7ea50) | **write air** | dropped | write air |
| 0xFFFC | dropped | **write air** (material 0, zero extra data) | **write air** | dropped |
| no material (unset) | nothing | air **only if** the world lookup (`param_3`) says the destination is set | nothing | nothing |
| vertex bit | vertex written (sentinel material 0xFFFE) | same | same | same |

- `asDelete` (`param_11`, the "Remove voxels" option) forces every written material to 0.
- A 0xFFFC on the first slice of the box (x, y or z index 0) is never written.
- The "real solid" test used everywhere is `(f & 1) && ((m + 4) & 0xFFFB) != 0`, i.e. m ∉ {0, 0xFFFC}.
- The mode names come from the Fine Tuning menu FUN_1436b14b0. "PasteAir" is shown Selected when the paste mode
  (vtable +0x218) == **2**, "PasteInteriorAir" when it == **3**, and "RemoveVoxels" is a separate flag. The texts
  are 562542 "Paste air with blocks?", 571841 "Paste air with interior blocks?", 576288-576291 "Paste with
  Surrounding / Interior Empty Space", "Paste as Delete".
- Callers: FUN_142aed380 (paste into the clipmap view; it passes the view as world lookup) via VoxelFarmWrapper
  FUN_143f96760 / FUN_143f967f0. FUN_14400c770 is the client paste preview: **mode 2, no world lookup** (FUN_143f99120,
  FUN_143f9aef0).
- The design material-cost list skips 0xFFFC (FUN_143115670: `uVar2 != 0xfffc` before the ItemId / CountRequired
  rows). It is not a real material.

### 1.3 Other readers of 0xFFFC (proven)
- **Mesher** ContourEx FUN_142ac4590 (VoxelFarm contour.cpp "ContourEx--points"): a voxel's material counts only if
  the flag has bit 0 and `m != 0xfffc`. Otherwise it is 0, i.e. empty. **0xFFFC renders as air.**
- SmoothVoxelDataWithContourData FUN_144003140 (VoxelFarmSmoothingUtil.cpp) treats a voxel as empty only when
  `(m - 1) > 0xfffc`, i.e. m ∈ {0, 0xFFFE, 0xFFFF}. **0xFFFC and 0xFFFD count as solid there.**
- The paste-preview layer write FUN_14400d390 treats 0 and 0xFFFD (-3) as empty, and 0xFFFE as "offset only".

Sentinels found: 0xFFFC interior air, 0xFFFD (empty for the preview layer), 0xFFFE vertex-only write, 0xFFFF solid
for the interior pass.

## 2. How the client combines layers with the generated cave (proven)

1. **Generator layer order.** `VoxelFarmCore::Init` FUN_143ff9e30 is called from the VoxelFarmImpl constructor
   FUN_143fee1e0. It adds the height-map terrain (created by vtable +0x50, stored at `[0x9cb]`), then
   `VoxelFarm::CaveVoxelLayer` (FUN_14407db80) to the generator at +0x4e50 (FUN_142abba40). The later
   `VoxelFarmWrapper` init FUN_143f94580 adds `ContourOptionalBlockData` (+0x5a78, the server block layers) and then
   `PreviewGenerationLayer` to the same generator, and +0x4f78 in a new group. `CGenerator::generate` FUN_142aba6d0
   calls each layer's slot 1 (`getContourData`) in that order, so **later layers overwrite earlier ones: terrain,
   then caves, then server layers.**
2. **Layers 0-3 → composite slot 4.** CellBlocks (vtable 1449a3ab8): `EnsureCellBlocks(4)` FUN_143fd4760 →
   FUN_143fd4940. It decompresses layer 0 into slot 4, then merges layers 1, 2, 3 with FUN_1440498d0. Per voxel of the
   upper layer: flags OR'd in; **material bit → its material replaces the lower one (raw, 0 included)**; offset bit →
   its offset replaces the lower one. A higher layer therefore wins: **dig layer 2 (and the ore veins composed into
   it) overrides POI layer 0.**
3. **Composite over the generated cell.** `CBlockData::getContourData` FUN_142ab1d40 collects the 3×3×3 neighbour
   blocks and calls `VoxelFarm::getBlockContourData` FUN_142aa8830, which writes into the 44³ padded contour grid that
   terrain and caves already filled:
   - material bit → `contour.material = block.material` (raw) and flag bit 0 set. **Material 0 carves anything the
     terrain or cave layer made solid. 0xFFFC also ends up empty after ContourEx.**
   - offset bit → the voxel's vertex is replaced (blended with the existing vertex through FUN_142aa6030 /
     FUN_142aa6930 / FUN_142aa6290 when the base already has one). The material is not touched.
   - neither bit → the generated terrain or cave stays.
4. **What vertex-only means:** it changes only the position of the surface vertex in that voxel. The voxel stays
   solid or air as the lower layers / generator made it. In a ruin template the vertex-only voxels are exactly the
   x = 0, y = 0, z = 0 border faces (none inside, §3). The server writes them like the paste does.

Inferred link: that `ContourOptionalBlockData`'s block source (its +0x18 interface, slot 0) returns CellBlocks slot 4.
The interface implementation was not traced. Digging and building, which travel the same path, are known to carve and
add in game.

## 3. The ruin templates (proven, measured)

`ruinvox.py` / `ruincmp.py` decode every cell of each template (changed mask + format 2) into a grid:

- **The changed mask equals the occupancy mask** in every cell. No voxel is "changed but unset".
- Set box = voxels 0..Size inclusive. **Index 0 on x, y and z is vertex-only.** Indices 1..Size are explicit. Example,
  frozenteleporter (175,250,175): the x = 0 face is all vertex-only, the x = 175 face is air. Interior vertex-only
  voxels: 0. The voxels outside the box (cell padding up to 200 / 280) are unset.
- Inside the box (0.695): **0 % unset** in 31 of 35 ruins. ceijay_fallenpridekeep has 8.2 % (0 % in 0.589),
  butchywuff 1.6 %, koda 0.8 %, ahnzul_ancestralshrine 0.3 %. Above the build the box is 77-98 % air (material 0),
  plus the 0xFFFC rooms and the solids.
- **Lowest solid = voxel row 1 (0.4 m) in all 35.** The lowest prop is 0-10 m above the corner.
- Every template is 250 voxels (100 m) tall. Footprints are 100 / 175 / 350 / 550 voxels = 40 / 70 / 140 / 220 m.
- 0.589 → 0.695: the unset holes inside the box were filled with air (e.g. ashlyn 3.8 % → 0, portavar 3.3 % → 0,
  ahnzul_ancestralshrine 4.1 % → 0.3 %). This matches the 13 Apr 2016 note "Stalagmites in the Chaos Caverns should
  no longer sometimes poke through the floor of a ruin" (trace/architecture/terrain-service.md §1.1). Inferred: live
  carved the **whole box** (mode-2 semantics), and the full air envelope is what removes cave features from it.
- Materials (template material list): no template lists 0xFFFC or any value ≥ 0xFFF0 as a material. The ones that
  matter for bug §4.1: 230 (Worn Solucite), 231 (Worn Iron), 219, 221, 222, 224-229.

## 4. Server code checked against the client

### 4.1 BUG (proven, the cause in game): LoadZone strips ruin and build materials
`LandmarkEmulator.EnvServer.Core/Terrain/TerrainEdits.cs`, `LoadZone` (~lines 179-188):
```
var shellMaterials = TerrainMaterials.AllCaveMaterials();
... if ((Flags[i] & 1) != 0 && shellMaterials.Contains(Materials[i])) { Flags[i] = 0; Materials[i] = 0; }
```
- `AllCaveMaterials` (TerrainMaterials.cs:160) returns VoxelBiomes columns 13 / 14: {44, 45, 218-231}. These are
  VoxelMaterials **IDs** (ID 230 → ARRAY_INDEX 158 "Horizontal Grain Burled Wood Planks"). Block data holds
  **ARRAY_INDEX** values, so the code strips ARRAY_INDEX 218 Light Horizontal Grain Burled Wood Planks, 219 Dark
  Horizontal Grain Burled Wood Planks, 220-225 burled woods, 226-229 rough cloths, **230 Worn Solucite, 231 Worn
  Iron**, and 44 / 45 Chaos Cavern Bottom / Wall.
- TerrainMaterials.Load runs before EnvironmentServer.Load (HostedService.cs:79 vs :97), so the strip runs on every
  start, in every layer (POI, build, dig).
- Ruins affected (template material list): 22 of 35, 816,293 voxels. For example hadder_bunker 231 ×79,447 +
  219 ×14,376, shattari_magrail 231 ×81,079, ashlyn_coliseumraid 230 ×71,647, woodstein 230 ×53,792 + 231 ×64,419.
- **Proof on the dev world** (`server-data/worlds/dev/TerrainEdits`, marker `outposts695.txt` written 19:12, cells
  rewritten 19:21):

| Ruin, template cell | Template | Saved cell |
|---|---|---|
| frozenteleporter (2,1,2) → `2026FC1099026F67.cell` | 646 voxels of 231 | 646 voxels flag 0, 0 of 231 |
| hadder_bunker (2,1,3) → `20270180630270DB.cell` | 2,802 of 231 + 1,280 of 219 = 4,082 | 4,082 flag 0 |
| woodstein (2,2,2) → `2027087047027015.cell` | 6,481 of 230 + 6,787 of 231 = 13,268 | 13,268 flag 0 |

  All other voxels of those cells are present (0xFFFC kept raw: 41,045 / 40,523 / 21,108).
- Effect: the stripped voxels fall back to the generated world. Where the ruin's iron / solucite / wood stood inside
  what was cave rock (its floor, its foundations, walls against the cavern wall), the cave's rock / ice comes back in
  the ruin's shape. Where it stood in open cavern air, the ruin has holes.

### 4.2 ApplyDiff / overlay vs the client (checked)
- `VoxelTemplate.Load` / `BlockData.Deserialize`: correct format-2 decode. Flags 0x40 / 0x80 / 0x20 handled.
  Vertex-only kept as flag 2 when `KeepVertexOnly` (set at HostedService.cs:95, before stamping).
- `Stamp` → `ApplyDiff(..., PoiLayer)` copies exactly the masked voxels: flags, raw material, offsets. That is the
  client merge's semantics: air carves, unset leaves the cave alone, the vertex-only border moves vertices. **Nothing in
  ApplyDiff itself leaves cave rock inside the box.**
- Difference from live: 0xFFFC is stored raw. It draws as air (ContourEx). Live's paste wrote material 0, and raw
  0xFFFC is treated as solid by the client's smoothing utility (FUN_144003140) and by server code that tests
  `material != 0`: `ApplyDiff` / `Commit` ownership (TerrainEdits.cs:909, :977), `Prepare` material delta (:930),
  `BuildingClipboard.cs:172` (a player copying ruin space would be charged for "material 65532"), and TerrainLod
  `Downsample` (copies it raw into the coarse levels).
- Ore veins: `OreVeins.Compose` (OreVeins.cs:554) puts vein ore into layer 2 wherever the dig layer is unset, and the
  client merges layer 2 over layer 0 (FUN_1440498d0 order). `OreVeins.GeneratedSolid` / `CaveStart` (:372-395) know
  only the height map and `CavePieces.IsAir`, not stamped POI air. Cave veins start at cavern floors / ceilings and
  run into the rock, so a vein in a ruin's box shows as ore inside the ruin (inferred risk; not measured on the dev
  world).
- Order at start-up (HostedService.cs:97-101): `BuildLodCopies` runs before `Outposts.Load` stamps. The coarse copies
  of new ruin cells only come from the background refresh after 20 s of quiet. That is fine once it has run.

## 5. Placement: live's ruin slots in the .cpa (lead L47, proven layout)

> **Correction 2026-10-17 (agent-leads2-d1.md L47):** a ManuallyPlacedExportedTemplateData record is {u32 record version (= the file version 12 / 13 / 14), Room(version), u32 a (1-4, rotation [I]), u32 b (2 or 4: category), u32 c (template id: 76 graveyard on the islands; 61 / 75 on the old continents)}. The "first field 1-4" read earlier is **a**, the second u32. The retail client parses these records but never uses them (FUN_143ff7bc0 has no caller), so the server stamps them.

Parser FUN_143fa2e20 (CavePlacementAsset; constructor FUN_143fa4590 gives the array offsets). File layout:

```
u32 <computed>
v≥3:  u64
3≤v≤12: u32 n × u64; u32 n × u64; (v>3) u32 n × u64        (cell-id arrays → classifications built at load)
v>4:  u32; u32 n × LayerInfo {i32 ×4, (v>8) i32 ×2}          (FUN_143fa3930)
v>5:  u32
v>6:  u32 n × CavernInstanceData {u32 ×4, Box, u32 m × Room}  (FUN_143fa37e0)
v>7:  u32 n × CaveTileObjectGaps {u32, u32 m × Room}          (FUN_143fa3720)
v>10: u32 n × ManuallyPlacedExportedTemplateData {u32 v2, Room(v2), u32, u32, u32}  (FUN_143fa4190 / FUN_143fa3b70)
v≥13: cell classifications (FUN_143fa40b0)
Room (FUN_143fa3590) = (v<10: 1 byte | v≥10: u32 k × {i32 ×3}), Box;  Box (FUN_14315c3a0) = f64 min x,y,z,w + max x,y,z,w (global dm)
```

`ContinentCavePlacements[406]_Underground.cpa` (v14, 3,194 tiles), parsed to the end of the template section:
- LayerInfo `(3, 0, 0, 3194, 0, 81)` and `(2, 0, 3194, 0, 0, 0)`. Inferred: kind, layer, first tile, tile count,
  first cavern, cavern count. This is the per-layer record behind the Environment Server's "Ruin Layer Info" page
  (Desired / Active / Failed).
- **81 caverns.** Header e.g. (7, 9, 9, 7); box e.g. 653×179×640 m. **Rooms = ruin slots, 4,030 in all**, each with
  the point (2, 0, 12):

| Slot (x × z, m, height 100 m) | Count | Fits |
|---|---|---|
| 40 × 40 | 2,422 | diamondfox_ebonglowtower (100×250×100) |
| 70 × 70 | 642 | the 175×250×175 ruins |
| 70 × 140 / 140 × 70 | 303 / 314 | 175×250×350 (unrotated / turned 90°) |
| 70 × 220 / 220 × 70 | 154 / 158 | 175×250×550 |
| 220 × 220 | 37 | the 550×250×550 ruins |

  Slot boxes overlap: they are candidates on an 8 m grid. Slot floor = cavern box bottom + 8.0 m (2,622) / 8.4 m
  (1,228). Every slot is voxel-aligned (y mod 4 dm = 0) and **not cell-aligned** (x mod 160 dm = 32 / 64 / 112 …).
- **81 manually placed templates**, one per cavern, ids `(1..4, 4, 76)`. Box 33.2 × 8.8 × 33.2 m = 83×22×83 voxels =
  the size of `graveyard_launch.otd` (25 props: chests, glowing crystals). The 81 CaveTileObjectGaps hold one room
  each with the same box. Inferred: the cavern's arrival / portal-crystal platform, which matches the server's "81
  underground portal crystals". The first field (1-4) is probably the rotation. Template 76 is in no client table.
- **No client table or code names a ruin_*.otd.** Live's Environment Server chose the ruins and filled these slots.
  The slot sizes are what ties the templates to the caverns.
- **Box vs template corner:** the manual box is exactly Size × 0.4 m (83 voxels) and the slot boxes are exactly the
  ruin Sizes. So box min ↔ the template's first content voxel (index 1); the vertex-only border (index 0) lies 0.4 m
  below and outside the box (inferred; ±1 voxel unproven).

Server placement compared with the slots (`cpa4.py`): every ruin corner in `outposts695.txt` falls inside a .cpa
cavern (the same global dm frame as the tile records). The nearest slot floors are **−8.8 m to +4.4 m** from the
server's corners (corner y = floor((floor − 1 m) / 16 m) cells). x / z are rounded to 16 m cells and ignore the slot
shapes. For example ahnzul_market (220×220 m) sits in cavern 1 (397×320 m), whose largest nearby slot is 70×70.
Large ruins in small caverns cut 100 m-tall boxes through the cavern walls.

## 6. Fix list for the server

1. **TerrainEdits.cs `LoadZone`: remove the "hole shells" cleanup** (lines ~179-188), or limit it to `DigLayer` cells
   behind a one-time marker, and translate the VoxelBiomes IDs to ARRAY_INDEX
   (`VoxelMaterials.txt` col 0 → col 1) before comparing. Never touch `PoiLayer` or `BuildLayer`. Also check whether
   players' Worn Iron / Worn Solucite / burled-wood / cloth builds were lost in earlier restarts (MODLOG note).
   `TerrainMaterials.CaveMaterialsAt` is unused and returns IDs, not ARRAY_INDEX. Fix it or delete it.
2. **Re-stamp the ruins** once (1) is in. The dev world's ruin cells are already stripped on disk. Revert each box
   (`RevertBox` over the saved corner + Size) and stamp again: for example a new marker name, or drop the
   `outposts695.txt` lines so `Outposts.Load` re-stamps. Say that it restarts the dev world.
3. **VoxelTemplate.Stamp: write 0xFFFC as air**, as live's paste did (FUN_142a7eeb0 mode 2/3). For each masked voxel
   with flag bit 0 and material 0xFFFC, set material 0 and keep its flags and offsets. Material-0 air stays air, solid
   stays solid, vertex-only stays as is. That is mode 2 without the world lookup, the same as the client preview
   FUN_14400c770. The overlay then never holds 65532, so ownership, material deltas, clipboard costs, LOD copies and
   the client smoothing utility all see air.
4. **Placement from the .cpa slots** (Outposts.cs `Load` / `Choose`; parse in CavePieces.cs or a new class):
   - parse CavernInstanceData + rooms (layout §5) for the home continent's `_Underground.cpa`;
   - for each ruin, choose a free slot whose x / z size equals `template.Size × 0.4 m` (or the swapped size, see 6),
     one ruin per cavern first ("each Chaos Cavern has at least one ruin"), no overlap with other chosen slots or the
     cavern's CaveTileObjectGaps box;
   - corner (global dm) = slot box min, minus one voxel on x / y / z for the vertex-only border (inferred, §5). Props:
     corner + prop position, as now (`CornerLocal` must then use the voxel corner, not `Corner × CellSize`);
   - save the voxel corner in the marker, not a cell.
5. **Voxel-precise stamping:** add `Stamp(long voxelX, long voxelY, long voxelZ, zone)` to
   `VoxelTemplate` / `IEnvironmentServer.StampTemplate`. Each template voxel (cell c, index i) maps to the global voxel
   `corner + c·40 + (x, y, z)`, and the destination cell and index come from it (index order y + 40·(x + 40·z)). Build
   one changed mask + BlockData per destination cell, then `ApplyDiff(..., PoiLayer)` per cell. `MarkPermanent` must
   take the destination cells.
6. **Rotation** (both orientations of slots exist in nearly equal numbers): either stamp only into slots of the
   template's own orientation, or rotate by 90° (swap x / z voxels with mirrored offsets, turn props: position and
   quaternion).
7. **Ore veins vs stamps:** after stamping, remove the OreVeins cell voxels inside each ruin box, and make
   `OreVeins.GeneratedSolid` / `CaveStart` skip stamped boxes, so layer-2 ore cannot sit inside a ruin.
8. Keep the vertex-only border and the explicit air box: they are what live shipped (the 0.695 re-export filled the
   box with air). After (4) the box lies inside cavern air by design, so the carve only clears stalagmites and floor
   bumps.

## 7. For OPEN-LEADS (not edited here)
- **L16**, material 65532: **resolved**. It is interior air (§1). The .csc / .ocm parts of L16 stay open.
- **L47**: section layout through ManuallyPlacedExportedTemplateData resolved for v11-v14 (§5). Still open: the
  meaning of the cavern header (7, 9, 9, 7), the room points (2, 0, 12), LayerInfo fields, which template id 76 is
  (graveyard_launch by size, unproven), and the classification section (v≥13, FUN_143fa40b0).
- New: whether `ContourOptionalBlockData`'s block source is CellBlocks slot 4 (§2, inferred), and the ±1 voxel of the
  slot ↔ template corner (§5).
