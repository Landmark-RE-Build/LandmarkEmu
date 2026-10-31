# VOXDEL A: what the client does on Delete / Cut / Copy of a voxel selection (0.695, traced 2026-10-31)

Bug report (relayed): deleting voxels with the Selection tool and the Delete key, then copying a wall section and
pasting, brings the deleted voxels back. Placing voxels afterwards also seems to bring them back.

Static trace only (decomp_all_695 + InputProfile_Default.xml). Nothing was attached to or run in the game.
[P] = proven from the cited code or file. [I] = inference.

## 1. Short answer

- Delete erases the voxels **only on the client**. It sends **no ApplyDiffRequest (sub 1)** and no voxel data. It sends
  a `ClientSelectionAreaRequest` (0xF4 sub 2) with **action 2**: the selection box plus every touched cell with its
  layer-1 version before and after the erase. The empty block list means the server has to erase the box itself. [P]
- Cut (Ctrl+X) does the same with **action 1**. It also copies into the local clipboard and sends the copied blocks. [P]
- Our server reads actions 1 and 2 as a **Copy** (`BuildingClipboard.HandleSelection`, else branch) and never erases
  anything. It then answers ok. Each `ClientCellUpdateResponse` makes the client commit its erased cell to its cache
  under its own new layer-1 version. From then on the server and the client disagree on the cell's voxels and on its
  version. [P server code, client handler FUN_144016840]
- The deleted voxels come back the next time the server sends that cell:
  1. A paste. `Paste()` sends every cell in the paste box that the server holds (`byCell.Keys ∪ request.Cells`) to
     every player, the paster included, through `ZoneTerrain.Broadcast`.
  2. The next brush edit in that cell. ApplyDiff's base version (the client's post-delete version) does not match the
     server's version, so `LocalEnvironmentServer.ApplyEdit` marks the cell Stale and `UndoLocalEdit` calls
     `ZoneTerrain.Resend`. The deleted voxels come back and the new voxels in that cell are not applied.
  3. Any re-fetch after a re-zone or login, because the version in the client's cache differs from the server's
     manifest. [I]

  Other players never see the deletion. [P server code]

## 2. Input → native chain [P]

| Step | Delete | Cut | Copy |
|---|---|---|---|
| Key (InputProfile_Default.xml) | ActionSet `SelectionTool`, `Delete` key → `VoxelClipboard.Delete` (l.298-301) | ActionSet `BuildMode`, Ctrl+X → `VoxelClipboard.Cut` (l.625) | `BuildMode`, Ctrl+C → `VoxelClipboard.Copy` (l.622) |
| Lua native (luabind_695.tsv) | FUN_1435dd210 | FUN_1435dd150 | FUN_1435dd1b0 |
| Native → `FUN_143466b90(tools,"Selection")` vtable | +0x108 FUN_1436e3f60 | +0x100 FUN_1436e3ee0 | +0xf8 FUN_1436e3a50 |
| ToolBrush → cursor (+0x20) `VoxelSelectionCursor` vtable (1447e84a8) | +0x1d8 FUN_143793a10 | +0x1d0 FUN_143793990 | +0x1c8 FUN_143793910 |
| ClientConstructionManager (DAT_1454311c8+0x32098) | FUN_143161140 → FUN_143161530(mgr, box, **2**) | FUN_143161100 → (…, **1**) | FUN_143160f40 → (…, **0**) |

- The "Selection" tool is ToolBrush index 8, with cursor FUN_143793230 (VoxelSelectionCursor, 0x590 bytes). It is
  registered in part_0105.c:23329, and its factory is FUN_1436e3a70 case 8. [P]
- The cursor methods work only while the selection is valid: `FUN_143793730` → vtable +0x288 FUN_1436ace30 → +0x1e0
  → FUN_1436b0f90 (area / build-site check). They then get the box (FUN_143793ce0). [P] What FUN_1436b0f90 enforces
  in detail is not traced. It has an admin bypass at DAT_145431a20+0x287. [I]
- FUN_143161140/1100/0f40 do nothing while `FUN_143f94490(core)` is true. That function (FUN_144016fc0) returns true
  when any entry in the pending map (cache impl +0x510) has the conflict flag +0x10 set. This is the "tools locked"
  state. [P]
- No UI calls VoxelClipboard.Delete, Cut or Copy. Only the key bindings reach them (grep of re/ outside the
  decompiles: the only hits are luabind tables). [P]
- The other keys bound to Delete are unrelated to voxels: `Prop` ActionSet → `Prop.DisassembleObject` (l.328), and
  World Edit Mode → `GameEvents:OnWorldEditKeyboardInput_DeleteSelection` (l.444). The second is the admin
  world-editing mode (0x10C); its Lua was not found in the dumps. [P binding / I meaning]

## 3. FUN_143161530 (shared by Copy 0 / Cut 1 / Delete 2), step by step [P]

1. `FUN_143166fb0` validates the box. It must not be empty ("You must first select a valid area…") and must have at
   most 125,000,000 voxels ("Maximum number of blocks to copy…").
2. On the manager (DAT_145454280 == the manager, set in its constructor at part_0087.c:30510), +8/+9 = 0: "not
   generating a diff mask". +0x118 = −1. A normal brush edit (FUN_143164e90) sets +8 = 1 and +0x118 = 3, and only
   then is an ApplyDiff built (FUN_143165920 SetGenerateDiffMaskArea → FUN_1431655e0 → FUN_143165810 → FUN_14315c980).
   The selection path never calls those functions, so **Delete and Cut send no ApplyDiff**.
3. `FUN_143161b30`: for every cell the box touches (FUN_143c18c60), it adds a `CellIdAndPermanentVersion` entry
   {u64 cell, u32 before = CellBlocks+0x14 (the layer-1 "permanent" version, FUN_143f90100), u32 after = 0}.
4. Only when action ≠ 2: `FUN_143160530(mgr, box, 0)` copies the box (clipped to the construction areas,
   FUN_142dbcae0(…, 0x100, …)) into the client clipboard (DAT_145431d68, a new VoxelFarm::CClipboardData).
   `FUN_143f9a670` and `FUN_143b922a0(clipboard, blocks)` then serialize it into the request's block list. For
   **Delete the block list stays empty** (FUN_143fb0e40, never filled).
5. Only when action ≠ 0 (Cut and Delete):
   - `FUN_1431608c0` erases locally. The box → world AABB (FUN_143c18b20: min = origin voxel, max = min +
     (size−1)·voxel; voxel = param·4·0.025 = 4.0 units) with max + 0.5 per axis (DAT_144c41158 = 0.5, read from
     Landmark64.exe). It calls `FUN_143f97df0(core, 1, 1, &aabb, …, 10)`, which goes to the VoxelFarm editor
     FUN_142aef6d0 (core+0x5a80), the same entry point the brush tools use. That op 1 means "remove" is [I], from the
     user's observation and from Cut and Delete sharing it.
   - `FUN_143163a90` marks every cell the box touches as pending: `FUN_143f8cd30(core, cell)` →
     `FUN_1440117c0(cache)`: impl+0x510 map, count += 1, deadline = now + 0x32. In 0.695 the cache is at
     **core+8 → +0x5a90**; in 0.589 it was +0x5a60 (FUN_143ec9370). The server comments still say +0x5a60.
6. For every entry, after = the cell's CellBlocks+0x14 read again **after** the erase. The local version is bumped
   by the erase; that the value changes synchronously is [I], the re-read is [P].
7. `FUN_143165700` builds the request (constructor FUN_14315e0a0) and sends it (FUN_14315cb20 → writer FUN_14315ae20).
   Action is at +0x20. Box: cell at +0x28, origin at +0x30, size at +0x3c. The claim guid at +0xf0 comes from
   FUN_143162230 (the first build site under the box). The cell list is at +0xf8 and the blocks at +0x110 (list at
   +0x118).
8. For Copy only: a UI event (hash 0x42cbb978, the tutorial "UI.Event.Copy" 15).

- **Copy does not mark cells pending**: FUN_143163a90 runs only for action ≠ 0. The server comment "a copy marks the
  cells pending too" (BuildingClipboard.cs:278) is wrong for Copy. Its CellUpdateResponses are harmless, but see §5.
- No client-side undo: Ctrl+Z only sends UndoRedoRequest (sub 3). Undoing a Delete therefore depends on the server
  having recorded it. [P luabind; I live behaviour]

## 4. Wire layout (writer FUN_14315ae20) and every action value [P]

`u8 layer (FF) | u32 action | u64 cell | u32 x,y,z | u32 sx,sy,sz | PropData list | 4×4 i32 matrix | material map
{u32 n × (u16, i32)} | u64 template guid (+0xd0) | string name (+0xd8) | u64 claim guid (+0xf0) | cell list {u32 n ×
(u64 cell, u32 before, u32 after)} (FUN_14315bea0 / FUN_14315aa50) | block list (FUN_143100cd0) | u32 paste mode (+0x148,
default 1) | u8 remove-voxels (+0x14c) | string tags (+0x150) | u8 (+0x168)`.
The server reads each cell entry as 16 bytes and **ignores both versions** (ReadSelection).

There are only four places that build ClientSelectionAreaRequest (calls to FUN_14315e0a0 in part_0087.c):

| action | Built by | UI | Local voxel change? | Marks pending? | Blocks sent |
|---|---|---|---|---|---|
| 0 Copy | FUN_143161530 ← FUN_143160f40 | Ctrl+C | no | **no** | copied box |
| 1 Cut | FUN_143161530 ← FUN_143161100 | Ctrl+X | **erase box** | yes | copied box |
| 2 Delete | FUN_143161530 ← FUN_143161140 | Delete key (Selection tool) | **erase box** | yes | **none** |
| 3 Save design | FUN_143165440 ← FUN_143794ad0 (selection cursor) | design save (name and tags) | no | no (no cell list at all) | copied box |
| 4 Paste / place design | FUN_1431642e0 ← FUN_1436d00b0 (paste cursor); FUN_1431649d0 ← FUN_142dc9f20 (design) | Ctrl+V / place a design | **none locally** (the server places it) | yes | none |

Paste extras: +0x148 = the cursor's mode (1 solids, 2 air, 3 interior air), forced to 1 when +0x14c = 1. +0x14c is
"Remove voxels" (Voxel.SetRemoveVoxels from the TweakMode SWF, `re/as3/TweakMode/.../TweakModeDocumentClass.as:296`).
The placed design is sent with mode 2. Other selection-related operations go through other packets: off-grid paste is
sub 5 (FUN_1431645b0), and Add / Fill / Paint / Smooth / Heal brushes go through ApplyDiff (sub 1). The stamp branch
FUN_14315fa80 → ApplyDiff, and FUN_14315fbc0 → AddTemporaryBlockRequest (sub 0, no local edit). How Fill, Add and
Delete *brushes* use an active selection as their shape is not traced (FUN_14346c770 → selection cursor +0x1e8/+0xf0). [I]

## 5. What the client expects back for a Delete (and Cut)

- **VoxelConstruction Response, sub 7 {u32 request sub = 2, u32 code, bool}** (dispatcher FUN_143162830 case 7). [P]
  - code 0 → FUN_143166f40 refreshes Voxel Vision / ownership (DAT_1454311c8+0x320a0).
  - code ≠ 0 with request sub 2 (or 0xF) → clears the clipboard (FUN_143b92180) and shows the message
    (FUN_143161180). **The local erase is not undone.** Only a CellsUpdate with the server's cell puts the voxels back.
- **TerrainCell 0xEC sub 3 ClientCellUpdateResponse {u64 cell, bool ok, string}** for every cell in the list
  (HandleCellUpdateResponse FUN_144016840). [P]
  - It decrements the +0x510 count.
  - ok = true → the client writes **its own** current cell to the disk cache as layer 1 with its own version
    (+0x14) and updates its manifest (FUN_143fd16e0, FUN_144020970). So the locally erased cell becomes the client's
    committed copy.
  - ok = false → conflict flag set. All build tools stay locked (FUN_144016fc0) until a CellsUpdate for that cell
    arrives.
  - **This handler also runs for cells that were never pending** (for example a Copy's cells), and with ok it commits
    the client's copy there too.
  - With no answer, the request times out after about 50 s (ClientPendingRequestTimeout FUN_143157570).
- **No ClipboardData (sub 6) is needed for a Delete.** The client never touches the clipboard on Delete. Our server
  sends one anyway (Copy() for every action except 3 and 4). The handler FUN_143162750 then **replaces the clipboard's
  props and material map** (+0xf0) with the empty ones from the packet. The voxels stay, because has-voxels is false.
  Our server also drops its own clipboard (`clipboards.Remove`). So after a Delete, Ctrl+V does nothing until the next
  copy. [P client handler + server code; that the paste start check reads +0xf0 is [I], from the 0.589 FUN_1436d6ac0
  note in BuildingClipboard.cs]
- What live sent back to the builder beyond the Response and the per-cell acks is not proven. The likely set is: the
  erase applied on the server with the client's `after` version taken as the cell's new version (as ApplyDiff does
  through `NewVersion`), CellsUpdate (and probably ResponseObserved) to the other players, and an authoritative
  CellsUpdate to the builder only on refusal. [I]

## 6. What our server does today with action 1 / 2 [P, code]

`Building.Handle` routes sub 2 to `HandleSelection`.
- A box ≥ 40 voxels on any axis, with action ∉ {0, 3, 4}: Response(2, Failed), ok acks, a sample saved to
  `Reports/selections/sel-<action>-*.bin`, **no Resend**. The client keeps its erase. Only `sel-0-20261013-231013.bin`
  exists, so no large Cut or Delete has reached the server since 10-13.
- Otherwise: the `MayCopy` (Get Inspired) and `CoversTemplateInstance` checks, then Response(2, 0), `Copy()`, and ok
  acks for every listed cell. Copy() keeps the request blocks: none for a Delete, the cut box for a Cut. **No voxel is
  erased, no undo record is made, nothing is broadcast, and the version is not updated.**
- Then:
  - Paste: `changed = byCell.Keys ∪ request.Cells.Where(Env.Has)` → `ZoneTerrain.Broadcast(changed)` to everyone,
    paster included, with full cells (`always: true`). Every cell in the paste box comes back with the server's
    voxels, including the deleted ones. (BuildingClipboard.cs ~588 / HandleSelection ~229.)
  - Next brush edit in that cell: `LocalEnvironmentServer.ApplyEdit` sees `LayerVersion != BaseVersion` →
    `result.Stale` → not applied, and `UndoLocalEdit` → `ZoneTerrain.Resend` (full cell). This matches "placing
    voxels after makes it happen". The server logs it at INFO: "edit of cell X based on version …, server has …; not
    applied." (Building.cs:786-790). Today's console.log starts at 05:55 and has no copy, delete or stale line, so the
    report's session has rotated out.
  - Re-zone or login: the server's manifest version differs from the client's cached version, so the client
    re-requests the cell and gets the server's copy. [I, from terrain.md §Rules]
- The same gap exists for paste with **Remove voxels** (+0x14c): the server reads `AsDelete` only for tutorial goals
  (`Achievements.Pasted`), so the paste **adds** the clipboard's voxels where the client asked to remove them. This is
  [P server]; what the client shows meanwhile is [I] (no local paste edit, the server's cells win).

## 7. Corrections to existing notes

- trace/systems/building.md:162 calls FUN_1430baa70 (0.695 FUN_143165700) "copy". It is the shared sender for Copy,
  Cut **and** Delete (actions 0/1/2).
- BuildingClipboard.cs:13 documents the action as "0 copy, 4 paste". The full set is 0 copy, 1 cut, 2 delete, 3 save
  design, 4 paste / place design.
- BuildingClipboard.cs:278 says "a copy marks the cells pending too". Only Cut, Delete, Paste and Place mark cells
  pending.
- The VoxelFarm cache in 0.695 is core+8 → **+0x5a90** (0.589: +0x5a60). See Building.cs:616 and the SendCellResponses
  comment.

## 8. Next steps

- Implement actions 1 and 2 on the server, kept with the server notes (see voxdel-c). Erase the box under the same
  rules as a delete brush: build permission per voxel (MayApply, not Get Inspired), material refund, an undo record,
  Broadcast to the others, and the client's `after` version taken as the new layer-1 version. Check `before` against
  the server's version, as ApplyDiff does. On refusal: Response code, ok acks, **and Resend** every listed cell. Send
  no ClipboardData for action 2.
- The exact erase region needs checking. The client's AABB is [origin, origin + (size−1)·4 + 0.5] world units per
  axis, with VoxelFarm op 1 and effect 10. Run a Delete with `cellcompare on` (TerrainEdits.Compare) and compare the
  server result with the client's cell. Also compare the CRC against the `after` version (layer version = CRC32 of the
  level-6 compressed layer).
- Find what FUN_1436b0f90 allows (box partly outside the site) and whether the client erases outside the build area
  (FUN_1431608c0 does not clip, but FUN_143160530 clips for Copy).
- To confirm the mechanism on the server without the game: after a Delete, the next brush click in the same cell
  should log "… not applied" at INFO.
