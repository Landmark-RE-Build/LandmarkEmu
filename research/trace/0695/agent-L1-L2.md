# Leads L1 / L2: procedural mask bitmaps and the procedural entity-list tables (2026-10-15)

## Summary

- **File index (new tool):** `re/tools/fileindex.py` writes `re/out/fileindex_589.tsv` (33,717 loose files, 275 .pack,
  32,591 files inside packs), `re/out/fileindex_695.tsv` (34,257 loose, 280 .pack, 34,900 inside packs) and
  `re/out/fileindex_diff.tsv` (shipped packs under `Resources\` only: 1,162 new, 103 gone, 542 changed in 0.695).
- **L1 — 13 of the 14 MASK bitmaps found.** They are **loose files**, not in the packs, in
  `CommonData\ProceduralPlacementTool\Masks\` of **both** installs (same sizes in both). All are plain 24-bit
  uncompressed BMPs (`BM`, 54-byte header). **`TilingTestZZ.bmp` (set 170) is not found yet** in either install.
  Neither exe references the masks, the folder, `.bmp`, `ProceduralPlacementSets` or its column names (raw scan of
  both exes, ASCII and UTF-16). A text file in the 0.589 CommonData describes `ProceduralPlacementTool` as an offline
  dev tool (`Continent_XXX.ini` / `.bat` under `EQNext\Dev\Runtime\Tools\ProceduralPlacementTool`): masks and sets
  are tool / server input that shipped with the client, not client data.
- **L2 — the two tables are not shipped** (no file with "EntityList" in either index; no `.txt` with the names in
  either install). The client does **not** load them: the `.txt` names appear only in the 0x10A packet
  constructor. But the client **does** contain their full schemas (column names, types, defaults) and the 0x10A
  response **carries both tables' rows** to the admin page 28. Schemas and the whole 0x10A wire layout are below.
  0.589 and 0.695 are identical (every reader, schema and draw function compared with addresses normalised).
- Still not found: `TilingTestZZ.bmp`; the actual **row data** of ProceduralPlacementEntityLists /
  ProceduralPlacementEntityListEntities (only a live server or a server-side archive had them).

## Task 1 — file index

`python re/tools/fileindex.py` (reads only pack directory chunks; ~13 s for both builds).

| Output | Columns |
|---|---|
| `re/out/fileindex_589.tsv`, `re/out/fileindex_695.tsv` | `kind` (loose / pack), `path` (relative to the install; for pack rows the .pack file), `inner` (name inside the pack), `offset`, `length` (loose rows: file size), `crc` (hex) |
| `re/out/fileindex_diff.tsv` | `status` (new / gone / changed), `name`, pack / length / crc in 0.589 and 0.695 |

Totals printed by the run:

| Build | Loose files | Bytes | .pack files | Files in packs | Unique names (Resources packs) |
|---|---|---|---|---|---|
| 0.589 (`<Landmark folder>`) | 33,717 | 20.6 GB | 275 | 32,591 | 32,145 |
| 0.695 (`<0.695 install>`) | 34,257 | 22.5 GB | 280 | 34,900 | 33,204 |

Pack locations: `Resources\Assets` (0.589: 32,145 inner files; 0.695: 33,204) plus the client's runtime mesh caches
`Cache\0` and `Cache\2` (`VoxMesh*.pack`, 446 / 1,696 inner files). The diff uses only the shipped `Resources\`
packs. One cache pack does not parse as a pack directory and is reported and skipped:
`Cache\2\Mesh\419\VoxMeshW_000.pack` (0.589; first chunk header `00911112 000000d5`, a later "next" pointer
9,152,767 fails the bounds check) — a runtime cache, not shipped data.

## Task 2 — L1, the mask bitmaps

### Where they are

`CommonData\ProceduralPlacementTool\Masks\` in both installs (index rows `loose`; read-only header check with
`re/out/agent_l1/bmphdr.py`). Not links: separate files in each install (`Get-Item` LinkType empty; `fsutil hardlink
list` shows one name).

| MASK name (ProceduralPlacementSets.txt) | Rows using it | Size | Image |
|---|---|---|---|
| PPP_Mask_Clumps001_Field02.bmp | 6 (52, 53, 54, 80, ...) | 50,331,704 | 4096×4096 24-bit |
| PPP_Mask_Clumps001_Inner02.bmp | 3 (77–79) | 50,331,704 | 4096×4096 24-bit |
| PPP_Mask_DualForests_Field01.bmp | 6 (157–160, ...) | 3,145,784 | 1024×1024 24-bit |
| PPP_Mask_DualForests_ForestA01.bmp | 3 (133–135) | 3,145,784 | 1024×1024 24-bit |
| PPP_Mask_DualForests_ForestA02.bmp | 3 (154–156) | 3,145,784 | 1024×1024 24-bit |
| PPP_Mask_DualForests_ForestB01.bmp | 9 (145–148, ...) | 3,145,784 | 1024×1024 24-bit |
| PPP_Mask_Forest_Clearing01.bmp | 9 (37–39, 74, ...) | 50,331,704 | 4096×4096 24-bit |
| PPP_Mask_Forest_MainTrees01.bmp | 3 (22–24) | 50,331,704 | 4096×4096 24-bit |
| PPP_Mask_Forest_MainTrees02.bmp | 8 (108–110, 167, ...) | 50,331,704 | 4096×4096 24-bit |
| PPP_Mask_Forest_Spots01.bmp | 3 (142–144) | 50,331,704 | 4096×4096 24-bit |
| PPP_Mask_Forest_Undergrowth01.bmp | 3 (25–27) | 50,331,704 | 4096×4096 24-bit |
| PPP_Mask_Forest_Undergrowth02.bmp | 12 (112–115, ...) | 50,331,704 | 4096×4096 24-bit |
| PPP_Mask_White01.bmp | 15 (121–124, ...) | 3,128 | 32×32 24-bit |
| **TilingTestZZ.bmp** | 1 (set 170: group 17, biome 32, MASK_SCALE 4; linked to set-entities 27 by SetsSetEntities row 763) | **not found yet** | — |

24 rows have an empty MASK. The folder also holds 20 masks no row uses (Clumps001 Field01/Field03/Inner01/Outer01/
Outer02/UniformClusters01/02, Comp01 Center01/Secondary01 (32×32), Continent432 EastHalf/Land/WestHalf (1000×1000),
Forest AroundSpots01/Undergrowth03, GenericNoise01 (+_Inverted, 256×256), Tundra Centipines01/GroveForest01,
TestMask01/02 (512×512)). Script: `re/out/agent_l1/maskcheck.py`. The 0.695 `Resources\ProceduralPlacementSets.txt`
is byte-identical to the 0.589 copy (`diff` after CR strip).

### Who reads them

- **No client loader.** Raw scan of both `Landmark64.exe` (ASCII and UTF-16LE; `re/out/agent_l1/exescan.py`, output
  `exescan.txt`): no hit for `PPP_`, `TilingTest`, `ProceduralPlacementTool`, `PlacementSet`, `PLACEMENT_DATA_GROUP`,
  `MASK_SCALE`, `ENCROACHMENT`, or UTF-16 `bmp`. The only ASCII `bmp` strings are VoxelFarm's
  `VoxelLayerBmpToPropGroups.txt` / `BMP_VALUE` / `VoxelLayerBmpToBiomeDefinition` (a different system, continent
  layer bitmaps). The only `CommonData` path strings are `..\CommonData\Collision`, `..\CommonData\Collision\%s`,
  `../CommonData/`, `CommonData`, `..\CommonData\Voxel` (0.589 exe offsets 0x45c9c28–0x46495b8; same set in 0.695).
- Decompiles: `rg` over `re/decomp_all` and `re/decomp_all_695` for `ProceduralPlacementTool|\Masks|PPP_|TilingTest|
  ProceduralPlacementSet|.bmp` inside string literals: only the two EntityList `.txt` names (L2) match.
  `re/out/strings.tsv`: no `ProceduralPlacementSets` / MASK column entry (the `MASK_TYPE`… strings belong to
  FUN_143f8d190, a voxel material table; `SPAWN_MASK_TGA` to FUN_143f94460).
- `ProceduralPlacementVoxelInterface` (RTTI 0.589 1448cf930, 0.695 14499dd08) is a VoxelFarmWrapper interface: its
  vtable is mostly one stub (143734074) plus 143ec9b70 / 143ed8310 / 143ecbae0 / 143ecbaf0; no mask / file strings.
- Text reference: `CommonData\Voxel\veinData\HowToGenerateVeins.txt` (both installs) explains generating vein data by
  editing `Continent_XXX.ini` under `EQNext\Dev\Runtime\Tools\ProceduralPlacementTool` and running the matching `.bat`
  — i.e. ProceduralPlacementTool is an offline developer tool, and `CommonData\ProceduralPlacementTool\` is its data
  (Masks, CaveMeshes). Combined with "no client loader", the masks are server / tool input.

### Every place searched for TilingTestZZ.bmp (not found yet)

- Both file indexes (every loose file and every pack entry, case-insensitive `tiling`, `tilingtest`, `.bmp` — 302
  `.bmp` per build, all loose, none inside any pack; folders: ProceduralPlacementTool\Masks, Voxel\materials\textures,
  LaunchPad.libs).
- Text search (Grep, case-insensitive `PPP_Mask|ProceduralPlacementTool|TilingTest|ProceduralPlacementSets|
  ProceduralPlacementEntityList`) over every `*.txt, xml, ini, cfg, lua, csv, json` in both installs: hits only
  `Resources\ProceduralPlacementSets.txt`, `CommonData\Voxel\veinData\HowToGenerateVeins.txt`, and the project's own
  `LaunchPadEmu\hashes.json` (0.695).
- Both exes (raw scan above), both decompiles, `re/out/strings.tsv`.
- `testclient\CommonData` is a link into the 0.589 install (covered by the index).
- Not tried yet: other client builds / archives the user may have; the name suggests a developer test mask
  ("TilingTest", set 170 only) that was never shipped.

## Task 3 — L2, ProceduralPlacementEntityLists / ProceduralPlacementEntityListEntities

### Do the files exist?

No. Neither index has any name containing `EntityList` (0.589: 0 rows, 0.695: 0 rows); the text search above found
no table with these names; `testclient/Resources` has only the 4 shipped ProceduralPlacementSet* tables.

### Every function that names them

| 0.589 | 0.695 | What it is |
|---|---|---|
| FUN_142bcf5d0 (part_0060 ~17827) | FUN_142c69030 (part_0061 ~895) | `ProceduralDataMonitorPacketUpdateResponse` ctor: opcode 0x10A at +8, **+0x10 = "ProceduralPlacementEntityListEntities.txt", +0x18 = "ProceduralPlacementEntityLists.txt"** (string pointers), +0x38 HashListMap<int,PropLayerDetails>, +0x68 HashList(Map)<PlacementEntityListEntitiesDefinition>, +0x98 HashList(Map)<PlacementEntityListsDefinition> |
| FUN_14316ce30 / FUN_14316ce40 | FUN_143217850 / FUN_143217860 | return `"PlacementEntityListEntities"` / `"PlacementEntityLists"` (DataAccessInterface vtable slot, table name; no static caller) |
| FUN_143c5f0b0 / FUN_143c611a0 | FUN_143d1df70 / FUN_143d20060 | default ctors of `PlacementEntityListEntitiesDefinition` / `PlacementEntityListsDefinition` (DataManagement::BaseDefinition) |
| FUN_14315bc80 / FUN_14315bd20 | FUN_1432066a0 / FUN_143206740 | copy ctors (used by FUN_14315dd60 to copy a packet into the page state) |
| FUN_143c5fa30, FUN_143c5ff20, FUN_143c603e0 | FUN_143d1e8f0, FUN_143d1ede0, FUN_143d1f2a0 | EntityListEntities column list / column writer / row reader |
| FUN_143c616d0, FUN_143c618f0, FUN_143c61b10 | FUN_143d20590, FUN_143d207b0, FUN_143d209d0 | EntityLists column list / column writer / row reader |

The `.txt` strings have exactly one reference each (strings.tsv: FUN_142bcf5d0 only; exe 0.589 0x45e6900 / 0x45e6930,
0.695 0x46acd00 / 0x46acd30). The packet's +0x10 / +0x18 pointers are only copied (FUN_14315dd60) — no read of them
was found in the page draw functions (FUN_1431685b0, FUN_1431682f0, FUN_1431680f0). **They are labels in the packet,
not a loader.** RTTI (re/out/rtti.tsv, rtti_695.tsv) has no `StandardDefinitionManager<PlacementEntityList…>` (compare
`VoxelLayerBmpToBiomeDefinition`, which has one), so nothing in the client loads these tables from disk; the
definitions exist only so the 0x10A packet can carry rows.

### The table schemas (from the client)

Row readers FUN_143c603e0 / FUN_143c61b10 read columns by name (`(*param_2 + 0x18)(name)`), store ints with
FUN_142a87b60 and the string with FUN_142a8a230; `DAT_1445f8188` = "ID", `DAT_1445f818c` = "NAME". Defaults from the
default ctors.

**ProceduralPlacementEntityListEntities.txt** (`PlacementEntityListEntitiesDefinition`)

| Column | Type | Object offset | Default |
|---|---|---|---|
| ID | int | +0x28 | 0 |
| ENTITY_LIST_ID | int | +0x2C | 0 |
| ENTITY_LIST_USE_TYPE_ID | int | +0x30 | 0 |
| ENTITY_ID | int | +0x34 | 0 |
| ENTITY_GROUP_ID | int | +0x38 | 0 |
| MIN_SPAWNS_PER_ZONE | int | +0x3C | 0 |
| MAX_SPAWNS_PER_ZONE | int | +0x40 | 10000 |
| SPAWN_WEIGHTING | float (read as double, stored float) | +0x44 | 1.0 |
| LAYER_TIER_WHITELIST_ID | int | +0x48 | -1 |

**ProceduralPlacementEntityLists.txt** (`PlacementEntityListsDefinition`)

| Column | Type | Object offset | Default |
|---|---|---|---|
| ID | int | +0x28 | 0 |
| NAME | string | +0x30 (IString) | "" |
| LAYER_TIER_WHITELIST_ID | int | +0x48 | -1 |

Header lines in the client's own format would be `#*ID^ENTITY_LIST_ID^ENTITY_LIST_USE_TYPE_ID^ENTITY_ID^
ENTITY_GROUP_ID^MIN_SPAWNS_PER_ZONE^MAX_SPAWNS_PER_ZONE^SPAWN_WEIGHTING^LAYER_TIER_WHITELIST_ID^` and
`#*ID^NAME^LAYER_TIER_WHITELIST_ID^` (column order = the column-list functions FUN_143c5fa30 / FUN_143c616d0).

### 0x10A ProceduralDataMonitorPacketUpdateResponse — wire layout

Dispatch: Zone_DispatchPacket (142c83660) `case 0x86` (part_0065 ~46419): construct (FUN_142bcf5d0), read
(FUN_142bb4ba0), and if the current monitor page is 0x1C (28) copy into `ProceduralDataMonitorState` (FUN_14316d0d0
→ FUN_14315dd60). All integers little-endian; every list is `u32 count, count × {u32 key, element}`; strings are
`u32 length + bytes` (Stream_ReadString 142cb22d0).

```
u16  <computed>
u32  <computed>
u32  <computed>
list layers  -> +0x38 HashListMap<int,PropLayerDetails>        FUN_142b9c5d0
     u32 <computed>
     PropLayerDetails                                              FUN_142b94430
       u32  <computed>
       list biomes -> +0x08 HashList<BiomeCoverageDetails>         FUN_142b9bba0
            u32 <computed>
            BiomeCoverageDetails                                   FUN_142b7ece0
              u32 <computed>
              u32 <computed>
              f32 <computed>
              list entity lists -> +0x10 HashList<PropEntityListDetails>   FUN_142b9c300
                   u32 <computed>
                   list entities -> +0x00 HashList<PropEntityListEntityDetails>  FUN_142b9c470
                        u32 <computed>
                        u32 <computed>
                        u32 <computed>
                        u32 <computed>
                        u32 <computed>
                        u32 <computed>
                        u32                         <computed>
                        u32                         <computed>
                        u8  <computed>
                   u32 <computed>
       u32  <computed>
       u32  <computed>
       u32  <computed>
       u32  <computed>
u32  <computed>
u32  <computed>
u32  <computed>
list -> +0x68 PlacementEntityListEntitiesDefinition rows       FUN_142b9dbb0 / element FUN_142b8baa0
     u32 <computed>
     ENTITY_GROUP_ID, MIN_SPAWNS_PER_ZONE, MAX_SPAWNS_PER_ZONE, SPAWN_WEIGHTING (f32), LAYER_TIER_WHITELIST_ID
list -> +0x98 PlacementEntityListsDefinition rows              FUN_142b9dcc0 / element FUN_142b8bc30
     u32 <computed>
```

Evidence for the drawn meanings: FUN_1431685b0 (page draw; format strings and lookups) — header format call args
`+0x20, pct, +0x30, +0x28, +0x2C`; layer loop over `+0x48` (list head of +0x38), key `+0x58`; biome loop over layer
`+0x18`, key `+0x50`; entity list lookup of node `+0x48` in the `+0x98` map (count `+0xA0`, buckets `+0xC0`, node key
`+0x60`). The graph values are passed in xmm1 and lost by the decompiler; read from the disassembly
(`re/out/agent_l1/dis_1431685b0.txt`, capstone): 143168b72 `[rsi]` (layer +0), 143168e29 / 143168f79 / 1431690c8 /
143169221 `[r12+0x38/0x3C/0x40/0x44]` (layer node), 1431693ba `movss [r13+8]`, 14316950b `[r13+4]`, 143169662
`[r13]` (biome node, r13 = `[r12+0x18]` at 143169236); series names at 0x14466176c = "Used", 0x144661774 = "%",
0x144661778 = "Spawns". FUN_1431682f0 (entity-list block) and FUN_1431680f0 (entity line; name via
DAT_145351bf8 = ClientStringProvider, created in part_0065 ~38855 by FUN_142f3d630) give the leaf fields. The
meaning of N / P / F, of leaf +0x00 / +0x14 / +0x18 / +0x1C, and of the biome f32 beyond "plotted" is **not proven
yet**.

### 0.695 equivalents (all identical to 0.589 after normalising addresses — `re/out/agent_l1/pairwalk.py`)

| 0.589 | 0.695 |
|---|---|
| FUN_142bb4ba0 reader | FUN_142c4e3b0 |
| FUN_142b937a0 body | FUN_142c2d290 |
| FUN_142b9c5d0 layers list | FUN_142c36150 |
| FUN_142b94430 layer element | FUN_142c2df20 |
| FUN_142b9bba0 biome list | FUN_142c35720 |
| FUN_142b7ece0 biome element | FUN_142c18490 |
| FUN_142b9c300 entity-list list | FUN_142c35e80 |
| FUN_142b9c470 entity list | FUN_142c35ff0 |
| FUN_142b85720 entity tail | FUN_142c1eef0 |
| FUN_142b9dbb0 / FUN_142b8baa0 ListEntities rows | FUN_142c37770 / FUN_142c25270 |
| FUN_142b9dcc0 / FUN_142b8bc30 Lists rows | FUN_142c37880 / FUN_142c25400 (string reader FUN_142d4b5d0 = Stream_ReadString) |
| FUN_142bcf5d0 ctor | FUN_142c69030 |
| FUN_1431685b0 / FUN_1431682f0 / FUN_1431680f0 page draw | FUN_143212fd0 / FUN_143212d10 / FUN_143212b10 (same xmm1 sources at 143213592 … 143214082) |
| schema FUN_143c5fa30 / 143c5ff20 / 143c603e0 / 143c616d0 / 143c618f0 / 143c61b10 | FUN_143d1e8f0 / 143d1ede0 / 143d1f2a0 / 143d20590 / 143d207b0 / 143d209d0 |

Container helpers (clear / rehash, e.g. FUN_142ca6620 vs FUN_142d3f9a0) differ only in Ghidra's jump-table warning
comments.

## Files

- `re/tools/fileindex.py`; `re/out/fileindex_589.tsv`, `re/out/fileindex_695.tsv`, `re/out/fileindex_diff.tsv`
- Scratch (`re/out/agent_l1/`): `maskcheck.py`, `bmphdr.py`, `exescan.py` + `exescan.txt`, `disasm_range.py`,
  `dis_1431685b0.txt`, `pairwalk.py`, function dumps `f_*.c`.
