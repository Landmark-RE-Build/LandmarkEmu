# Mesh Shapes tab (TASKS 3.1): what the 0.695 client sends and what the server does (traced 2026-10-16)

Sources: the 0.695 decompile (`re/decomp_all_695`, addresses below are 0.695; 0.589 pairs from trace/0695/functions.tsv),
the client tables (gameData695 / <0.695 test copy>\Resources: VoxelShapes, MeshCategory*, ToolTypes, ModalToolBrush*),
the 0.695 exe itself (vtable slots read from Landmark64.exe, scratch `vtable.py`), and the earlier traces
terrain-695.md §5, ui-trace-695.md (MeshesWindow 223), functions-695.md §11. **[P]** = proven from the client /
table, **[I]** = inferred.

## 1. Summary

1. **Nothing new on the wire.** Selecting a mesh shape activates the client's StampBrush (Add) tool, which sends the
   existing ToolPacketActivate (0x117 sub 1) [P]; the shape id stays in the client [P]; the edit arrives as the
   unchanged ApplyDiffRequest (0xF4 sub 1) with the voxels the client already computed from the mesh [P]. The server's
   brush path (Building.cs: build-site check per changed voxel, material charge / refund, overlay save, broadcast to
   players in range, ResponseObserved effect for others) applies unchanged to mesh shapes.
2. **The server now reads the tool switch** (new Game/Building.Tools.cs): u8 by-item flag, u32 item id, u32 ToolTypes
   id (layout §3, [P]). It keeps each player's active tool and uses it to pick the effect other players see for an
   edit (ResponseObserved: Add / Mesh Shapes / paste → Stamp placement, Fill Brush → FillStamp no-destroy placement,
   Remove → Delete, Heal, Smooth, Paint), instead of guessing every sculpting edit from the voxel diff (§5).
3. **The Mesh Shapes palette is client-only** (lead L17 answered, §4): PaletteCollectionMeshes fills itself from the
   VoxelClient's shape list, which is the client's own VoxelShapes.txt rows [P]. No unlock / ownership data exists.
4. **Fill "Smoothing Enabled"** (29 Aug 2016) is a client option: Tool.ToggleCursorSmoothing FUN_1436066a0 flips the
   global DAT_14525564c and refreshes the active tool (FUN_14346f3e0, no packet) [P].

## 2. Tables (client-only; nothing to serve)

| Table | Rows | Use |
|---|---|---|
| VoxelShapes 179-208 | 30 Mesh Shapes: cones, cogs, tubes, cylinders, dice-style polyhedra, prisms, pyramids; FILE_NAME `Theme_Meshshapes_*.dme`, SERVER_FILE_NAME empty, RESOURCE_COST 0, SHAPE_TYPE 2 | the shape meshes the client voxelizes (VoxelShapeDefinition::ShapeModelDefinition loads the .dme, FUN_143c8fc80) |
| MeshCategory (4) | 3 Cones 577586, 4 Cylinders 577585, 5 Polyhedrons 577583, 7 Other 576638 | tab categories (CategoryDefinitionManager FUN_1434d3350, "%d MeshCategoryMeshMapDefinitions loaded") |
| MeshCategoryMeshMap (14) | 180-182 → 3; 189-190 → 4; 205-208 → 5; 184-188 → 7 | category → shape; the other 16 shapes are uncategorised (search only) |
| MeshCategoryGroup | empty | — |
| ToolTypes 10 StampBrush "Add Tool", 17 FillStampBrush "Fill Brush" | NAME hashed at +0x50 (parser FUN_143cbcf70) | the ids ToolPacketActivate carries (§3) |
| ModalToolBrushDefinitions 7 / 20 StampBrush BRUSH_TYPE 4, 18 FillStampBrush 11 | — | the brush types the ResponseObserved handler maps to (§5) |

## 3. Client path and the packet [P]

- **Lua:** `Mesh.SelectMesh` FUN_143607fe0 and `Mesh.PlaceMesh` FUN_143608060 (identical): if the tool manager
  (DAT_145438108) has no active tool (+0x68 → FUN_143466b50 null), `FUN_143464780(mgr, "StampBrush")`; then
  `FUN_1434cfa10(palette DAT_1454380f8, shapeId, 7)` selects the entry in palette collection 7 (Meshes) → vtable +0x20
  FUN_14374e540 → FUN_14374a700 (the Shapes collection's selector) → FUN_14346a510 stores the shape id at tool manager
  +0x80 (0.589 FUN_1433bc4c0, same). `Mesh.SelectCategory` FUN_143607f90, `SearchPalette` FUN_1436081a0,
  `FilterMeshesByAvailability` FUN_143608100 (→ FUN_1434cfb80(palette, 1, 0, flag)) only filter the client palette.
- **Tool activation by name:** FUN_143464780 hashes the name (SOE string hash: `h = (h + c) * 0x401; h ^= h >> 6`
  per char, final `(h*9 >> 11 ^ h*9) * 0x8001`) → FUN_143468840(mgr, hash, 0). That function (part_0105 ~28616-28705)
  fires `GameEvents:OnToolChanged`, re-selects the palette entry, and when a tool is now active (+0x68 ≠ 0) looks the
  ToolTypes definition up by hash (`FUN_143bfc1c0(ToolDefinitionManager +0x321a8 of DAT_1454311c8, hash)`: linked list
  at +0x18, match on +0x50) and builds ToolPacketActivate with `+0x18 = 0, +0x1c = 0, +0x20 = def vtable+0x10`.
  ToolTypeDefinition vtable 0x1449547c8 (RTTI) slot 2 = FUN_143cbce80 = `return *(u32*)(this + 8)` = the ID column.
  So the last field is the **ToolTypes ID**: 10 for StampBrush. Leaving build mode (tool 0) sends nothing.
- **Item activation form:** ClientInventory.ActivateItem FUN_143457220 (part_0105 13742-13749): `+0x18 = 1, +0x1c =
  item definition +8 (item id), +0x20 = 0`.
- **Serialization:** FUN_143452ac0 (0.589 FUN_1433a5960, same) writes the tunnel byte `channel << 5 | 6`, then
  FUN_143452550: base header FUN_143452480 = `u16 opcode (+8)`, `u8 sub (+0x10)`; then `u8 (+0x18)`, `u32 (+0x1c)`,
  `u32 (+0x20)`. Wire (what the zone sees): **`17 01 | 01 | u8 byItem | u32 itemId | u32 toolTypeId`** (12 bytes).
  The family has no reply (coverage-695.md 0x117: sub 1 C→S only; UndoStackChange is S→C).
- **The edit:** the stamp cursor's ApplyDiffRequest writer FUN_14315acb0 = 0.589 FUN_1430b0510 (same); the request
  kind is set at +0x118 in 0.695 (3 for sculpting brushes, FUN_143164e90; 1 / 2 for box / fill placements,
  FUN_143165f30) and written in the same place as 0.589's +0xe0 (Building.ReadEditVersions reads it). No shape id,
  mesh name or tool id is in the request.

## 4. Lead L17 answered: where PaletteCollectionMeshes gets its entries [P]

`PaletteCollectionMeshes` fill FUN_14374e110 walks `FUN_14349c7a0(DAT_145437390)` / `FUN_14349e9a0(…, id)` = first /
next entry of the VoxelClientImpl's shape array (+0x2390 `SoeUtil::Array<VoxelShapeDefinition>`, entries 0xe0 bytes,
count +0x23a0), creating a MeshPaletteEntry (0x170 bytes, FUN_1437ac3e0) per entry and remembering the entry with
+0x48 (SHAPE_TYPE) == 3 as the default (+0x65). That array is filled by `VoxelFarmCore::InitVoxelStructures`
FUN_14349d440 (part_0106 30286-30337) from the **VoxelShapes definition manager** (`param_2 + 0x868` list, else the
log line "VoxelShapesManager is not available"): per row it copies ID (+8), +0xc, NAME (+0x10), +0x28 … SHAPE_TYPE
(+0x48) and starts the .dme load (FUN_143c8fc80). The manager is loaded from `VoxelShapes.txt` (part_0162 10456,
`FUN_143fbc480(…, "VoxelShapes", "VoxelShapes.txt")`). The entry writer FUN_1437ac720 exposes `ImageSetId`,
`EntryType "Shape"`, the name and `Enabled` (true unless the admin-only level gate DAT_145465a20 is on) — nothing from
the server. So the palette is the client's own table; "Can Use" filtering is the client comparing its material counts.

## 5. Server: what was built (Game/Building.Tools.cs, Building.cs)

- `Building.HandleTool` (ToolBase 0x117) parses sub 1 as above and stores `(ToolType, item)` per player guid
  (`Building.ActiveTool(session)`). Other subs are logged as unanswered. The dead early-return for `17 01 01` in
  WorldSession.FromClient's unknown-opcode path was left as is (ToolBase 0x1701 is in the enum, so the packet already
  dispatched to the empty stub; the stub is gone).
- `ObservedOperation(session, kind, record, out stamp)`: kinds 1 / 2 pass through; otherwise the active tool decides
  (StampBrush / FillStampBrush / PasteVoxel → 0 placement, SubtractiveBrush → 3, HealingBrush → 4, Smoothing → 5,
  PaintBrush → 6), falling back to the voxel-diff heuristic when no tool is known. `stamp` (the packet's trailing
  bool) is false only for FillStampBrush: the client's handler FUN_143166bd0 maps operations 0-2 to brush 11 FillStamp,
  or 4 Stamp when the bool is set, and FUN_143086570 picks `WorldEditingEffect.ShapePlacement_%d` vs
  `ShapePlacementNoDestroy_%d` by the same bool [P]; that the Fill Brush is the no-destroy one follows from its brush
  type 11 being the bool-false branch [P] and its behaviour (fills empty space only) [I].
- `SendObserved(viewers, operation, min, max, stamp = true)`: the bool is now a parameter (DevCommands `observed`
  keeps true).
- Bandwidth: one 12-byte packet per tool switch in, nothing out; the edit path and its coalesced broadcasts are
  unchanged.

## 6. What was not changed, and why

- No palette / unlock data for mesh shapes (§4) and no material charge changes: cost is per voxel material from the
  cells (BuildMaterials), RESOURCE_COST 0 agrees.
- The Fill tool with mesh shapes (11 Aug 2016) and "Smoothing Enabled" are client-side (§1).
- 0.589 clients have no Mesh Shapes tab (no rows, no window); the tool switch parse applies to both builds (the packet
  is "same", coverage-695.md).

## Open points

- Verify in the client (TESTING-NEEDED.md): a second player should see the Fill Brush's effect as the no-destroy one
  and the Add / mesh placement as the Stamp one; log line "switched build tool to StampBrush (10)" when a mesh shape
  is picked with no tool active.
- Which bool the live server sent for a paste (PasteVoxel) is not traced; Stamp (true) is used [I].
