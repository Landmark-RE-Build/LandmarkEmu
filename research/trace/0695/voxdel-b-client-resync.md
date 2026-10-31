# Deleted voxels come back: when the client replaces its own cell with the server's (task B)

2026-10-31. Read-only trace (0.695 decompile `re/decomp_all_695`, disassembly of `<0.695 install>\Landmark64.exe`
read with capstone, RTTI `re/out/rtti_695.tsv`, Lua natives `re/out/luabind_695.tsv`, `InputProfile_Default.xml`) and the
server on branch `client-0.695` (HEAD 91c5a566). Nothing was built, run or attached. `[P]` = proven by the cited code,
`[I]` = inferred.

Bug report: voxels deleted with the Selection tool + **Delete** key come back when the player pastes a copied piece of
wall, or after placing voxels next to them.

## 0. Answer in short

1. **The server never deletes them.** Delete (and Ctrl+X Cut) do not use ApplyDiffRequest. They send
   **VoxelConstruction 0xF4 sub 2 ClientSelectionAreaRequest with action 2 (Delete) / 1 (Cut)** after the client has
   already erased the box in its own copy (§6). `Building.HandleSelection` treats every action except 3 and 4 as a
   **Copy** (`BuildingClipboard.cs:273-277`): nothing is erased, and it answers Response 0 plus a ClientCellUpdateResponse
   with **ok = true** for every cell. Because of that ok, the client treats the local erase as confirmed: it writes
   its edited layer 1 to its disk cache and puts its own layer-1 version in its manifest (§3). From then on the client's
   layer 1 holds a different version and different voxels from the server's. [P]
2. **Any CellsUpdate for those cells undoes the delete on the client.** A server CellsUpdate always carries every
   layer (0-2). The client copies each layer in the packet over its own, with no version check, whenever the cell has
   no edit pending (§4). The server's layer 1 still holds the "deleted" voxels, so they come back. These are the
   messages that do it in the reported cases (§7):
   - **Paste:** the server places its own copy and sends `ZoneTerrain.Broadcast(changed)` **to the paster too**,
     after first answering the cells. Every cell the paste changed is replaced with the server's copy.
   - **Placing voxels:** the next ApplyDiffRequest on a cell from the delete has `BaseVersion` = the client's
     post-delete CRC, but the server still has its old version. The server marks the cell **Stale**, does not apply
     it, answers OK anyway and **re-sends its cell** (`UndoLocalEdit(done.Stale)`). The deleted voxels come back, and
     the voxels just placed in that cell disappear.
   - Also: undo/redo, refused edits, the Heal tool, other players' edits, digs, heals and site reverts in range, and
     any **re-request** (a manifest note that changes a layer version, or the zone-in manifest on login, re-zone or
     teleport). A re-request carries the client's held versions, which do not match the server's, so the server sends
     its body.
3. Brush deletes (Remove tool) are not affected. They go through ApplyDiffRequest, which the server applies, and it
   adopts the client's NewVersion so the versions stay equal. [P]

## 1. The client's cell objects

| Thing | Where | Meaning |
|---|---|---|
| `VoxelFarm::CellBlocks` (0x120 bytes, ctor FUN_143fd3b80, vtable 1449a3ab8) | `+0x10 + 4·layer` | **version of layer 0-3** (+0x14 = layer 1 = "permanent version") [P FUN_143fdf3a0, FUN_143fdee80] |
| | `+0x20 + layer` | per-layer flag byte (FUN_143fdf390; FUN_143fdee60 ORs the four) [P] |
| | `+0x28 + 0x18·layer` | compressed layer data [P FUN_143fdf3a0] |
| | vtable +0xb8 = FUN_143fd4580 | compress a working layer with **CompressBlockData FUN_144049a00**. Version = CRC32 of the compressed bytes (0 is changed to 1, 0x7fffffff to 0x7ffffffe). An empty or failed layer gets 0 or 0x7fffffff [P] |
| Cell-block cache (`VoxelCellBlockCacheImpl`, vtables 1449a9370…; reached through core +0x5a90) | lookup FUN_144014f90 / FUN_144015170, wrapper FUN_143f90100 | the client's current CellBlocks per cell [P] |
| **Pending-edit table** | cache `+0x510` (HashListMap, tree root +0x518), lookup FUN_144013a00 | node `{i32 count +0, i64 deadline +8 (= now + 0x32), u8 flag +0x10}`. count++ by FUN_144011830 / FUN_1440117c0 (deadline reset) [P] |
| **"Busy"** | FUN_144016fc0 (wrapper FUN_143f94490) | true while **any** pending node has its flag set [P] |
| Manifest table | cache `+0x150` (entry: set, flag, 4 × u32 layer versions), lookup FUN_143facac0 | replaced whole by the zone-in manifest (HandleCellManifest FUN_144016680 → FUN_143f977e0 clear + FUN_143fac700 fill) [P]. Single entries are set by FUN_144020970, which returns 1 when the version changed [P] |
| Request queue | cache `+0x1f0` (list +0x200, count +0x210) | cells whose manifest entry changed through a manifest note (FUN_144016790) [P] |
| Requested set | cache `+0x450/+0x470` (byte +3 = in flight) | cells asked with ClientCellRequest; HandleCellsUpdate / HandleUpToDateCells clear byte +3 [P FUN_144016a90, FUN_144016c80] |

The local commit of an edit is FUN_144020fa0 (0.589 FUN_143f5d2c0). For a level-2 cell it writes **layer 1**, builds a
new CellBlocks, copies the other layers over, compresses layer 1 through vtable +0xb8 (new CRC version) and asks the
listener at cache +0x30 (vfunc +0x60). When the listener accepts, it calls **FUN_144011830: pending count++** [P].

## 2. What the client's requests carry

- Both requests carry **`Array<CellIdAndPermanentVersion>`** entries `{u64 cell, u32 base, u32 new}`.
  - **base** is the cell's CellBlocks +0x14 (its layer-1 version) when the cell joins the edit, or 0 when the client
    holds no CellBlocks for it (FUN_143161ab0, FUN_143161b30). [P]
  - **new** is CellBlocks +0x14 read again just before sending (FUN_143161c90, called from the flush FUN_143165810
    and from FUN_14315f270), i.e. the CRC of the client's own edited layer 1. It stays 0 when no CellBlocks is found. [P]
- **ApplyDiffRequest** is written by FUN_14315acb0, sent through FUN_14315c980 from the flush FUN_143165810 [P]. The
  server reads the list in `Building.ReadEditVersions` (`Building.cs:432`) and checks base against its own layer-1
  version (`LocalEnvironmentServer.cs:83-87`).
- **ClientSelectionAreaRequest** (sub 2, ctor FUN_14315e0a0, sent by FUN_143165700 → FUN_14315cb20 → writer
  FUN_14315ae20) carries the same list for every cell the box touches [P]. The server skips it as
  "u64 cell, u64 version" (`BuildingClipboard.cs:131-135`). Base and new are there and could be read.

## 3. ClientCellUpdateResponse (0xEC/3): ok / false

- Dispatch (part_0087 `case 3`): reader FUN_1431465e0 → FUN_143f943f0 → cache vtable +8 FUN_144016820 → **FUN_144016840
  HandleCellUpdateResponse(cache, cell, ok)**. When `ok == 0` the dispatcher also shows the packet's string as an alert
  (DAT_145431a10 vfunc +0x18). [P]
- FUN_144016840 [P]:
  1. Finds the pending node. If **!ok** it sets the flag. It then decrements the count. The node is removed only when
     the count is below 1 **and** the flag is clear.
  2. If **ok** and the client holds a CellBlocks for the cell, it writes the client's own layer 1 with **its own
     version** (+0x14) to the disk cache (FUN_143fd16e0 at cache +0x230), then sets **its own manifest entry for
     layer 1 to that version** (FUN_144020970).
- So **ok = true accepts the client's local edit as the truth** (cached across logins, manifest updated). **ok = false**
  keeps the cell flagged ("busy": brush, Copy, Cut and Delete are all dropped while the flag is set, §4) until server
  data for the cell arrives, and shows the error text. The server always sends ok = true
  (`Building.SendCellResponses`, `Building.cs:619-632`) and handles refusals by re-sending the cell instead.

## 4. CellsUpdate (0xEC/5) for a cell the client has edited

Dispatch: reader FUN_1431464f0 → FUN_143157370 → cache vtable +0x18 (thunk 144016a80) → **FUN_144016a90
HandleCellsUpdate(cache, list, bool +0x48, bool +0x49)** [P disassembly 144016a90: r8b/r9b saved and passed on].
For each entry it sets `requested` = the requested-set node exists with byte +3 or bytes 0-2 set, and clears byte +3.
Version 0 (block data) goes to **FUN_144016150(cache, entry, requested, b48, b49)**; anything else goes to FUN_1440164c0
(meshes) [P disassembly 144016bf5-144016c21]. The server writes both bools as false (`TerrainStore.BuildBlocksUpdate`,
`TerrainStore.cs:93-114`).

FUN_144016150 [P]:

- **Not requested** (every server push: Broadcast, Resend):
  - With **pending count > 0** the update is **dropped**. When b48 = 0 (the server's case) the pending node is
    **flagged**, so the client is busy. The count still falls with the per-cell responses, but the flagged node stays
    until the next unrequested CellsUpdate arrives for that cell while the count is ≤ 0.
  - With **count ≤ 0** (or no node) the node is removed. A new CellBlocks is built: **FUN_144020b50** puts every layer
    in the packet into it with the packet's version (FUN_143fdf3a0), copies the layers not in the packet from the
    old block (FUN_143fdee80), writes the packet's layers to the disk cache, and **sets the manifest entry of every
    layer to the packet's version** (FUN_144020970). It is installed with FUN_14401a980. **There is no version
    comparison**: the server's layer 1 replaces the client's whatever the client holds.
- **Requested** (an answer to ClientCellRequest): same merge (FUN_144015ac0 + FUN_144020b50), installed through
  FUN_14401ad70 → FUN_14401a980. This branch does not check the pending count. [P]. FUN_14401a980 drops a pending
  cell only when it is missing from table +0x448 [P]; reading +0x448 as "requested in flight" is [I].
- The server's CellsUpdate always lists layers 0-2, empty ones too (`always: true` in `ZoneTerrain.Resend`,
  `ZoneTerrain.Broadcast`, `TerrainEdits.BuildUpdate`), so **the client's layer 1 is always replaced**. [P]

## 5. ModifiedCellManifestUpdate (0xEC/0xB) and re-requests (0xEC/1)

- Dispatch (`case 0xb`): reader FUN_143146ad0 → FUN_1431572b0. The layer code goes through FUN_143fad330
  (1-4 = set 0, layers 0-3; 5-8 = set 1), then FUN_143f942f0 → FUN_144016760 → **FUN_144016790**. That updates the
  manifest entry (FUN_144020970). If the version changed, the cell goes into the request queue +0x1f0. [P]
- The request builder **FUN_14401a660** (0.589 FUN_143f56980) walks the queue. A cell is requested only when all of
  these hold [P]:
  1. it is in the current "wanted" set (+0x408/+0x428, generation +0x3f8);
  2. it is not already in flight (+0x450 byte +3);
  3. it has **no pending edit** (+0x510);
  4. one of its **held** layer versions (CellBlocks +0x10..+0x1c, zeros when it holds none) differs from the manifest.

  The entry `{u64 cell, 4 × u32 held versions}` goes to the array at +0x430 (ClientCellRequest). **The queue is then
  cleared**, so a note for a pending cell or one not in view causes no request at that point; the manifest still holds
  the new version. [P]
- The server's answer (`TerrainHandler.Answer`, `TerrainHandler.cs:57-127`) is UpToDateCells when every held layer
  version equals the server's. Otherwise it sends the server's body. After a client-only delete the held layer 1 is the
  client's CRC, not the server's version, so **any re-request of the cell brings the server's layer 1 back**. [P]
- The notes the server sends the builder:
  - `Building.cs:818-823`, after an applied edit: version = the server's new version. That is the client's NewVersion
    when it was non-zero (`TerrainEdits.Commit`, `TerrainEdits.cs:1936`), so it causes no request. When NewVersion was
    0, it is the server's counter, so the client re-requests and gets the server's copy.
  - Broadcast notes to players beyond 256 m.
  - Vein `Announce` notes (layer 2; the request compares all four layers, so a layer-2 change also re-fetches layer 1).
  - `PayOwedNotes`.
  - The zone-in manifest (`SendManifest`) on login, re-zone and teleport.

## 6. The Delete key on the client, and what the server does with it

Client path [P]:

| Step | Function |
|---|---|
| `InputProfile_Default.xml:298-301` ActionSet SelectionTool, Delete → `VoxelClipboard.Delete`; Ctrl+X → `VoxelClipboard.Cut` (:625), Ctrl+C → `VoxelClipboard.Copy` (:622) | |
| Lua natives Copy / Cut / Delete | FUN_1435dd1b0 / FUN_1435dd150 / FUN_1435dd210 → tool "Selection" vfunc +0xf8 / +0x100 / +0x108 |
| ToolBrush (vtable 1447d37c0) | FUN_1436e3a50 / FUN_1436e3ee0 / FUN_1436e3f60 → cursor vfunc +0x1c8 / +0x1d0 / +0x1d8 |
| VoxelSelectionCursor | FUN_143793910 / FUN_143793990 / FUN_143793a10 → construction manager (DAT_1454311c8 + 0x32098) |
| Construction manager | FUN_143160f40 / FUN_143161100 / FUN_143161140. Each first checks **busy** (FUN_143f94490) and does nothing while busy, then calls **FUN_143161530(mgr, box, 0 / 1 / 2)** |
| FUN_143161530 | (1) FUN_143161b30: one `{cell, base, 0}` per cell the box touches. (2) Copy / Cut only: FUN_143160530 copies into the clipboard and FUN_143b922a0 fills the request's block list; **Delete leaves it empty**. (3) Cut / Delete: **FUN_1431608c0 erases the box locally** (FUN_143f97df0(core, 1, 1, box) → FUN_142aef6d0) and **FUN_143163a90 marks every box cell pending** (FUN_143f8cd30 → FUN_1440117c0). (4) new = CellBlocks +0x14 for every cell. (5) **FUN_143165700 sends ClientSelectionAreaRequest with action = 0 / 1 / 2** at packet +0x20 (ctor FUN_14315e0a0 sets 0xF4, sub 2, action 0xffffffff) |

Server (`BuildingClipboard.cs`, `HandleSelection` :197) [P]:

- `CopyAction = 0, SaveDesignAction = 3, PasteAction = 4` (:32). Actions **1 and 2 are not handled**:
  - a box ≥ 40 voxels on any axis: "larger than one cell", Response Failed, ok cell responses (:213-227);
  - otherwise the copy permission checks, then the `else` branch (:273-277): Response 0 and **`Copy(session, body,
    request)`**, then ok cell responses (:278).
- `Copy` on a Delete (no blocks, `request.Voxels == null`): **`clipboards.Remove(player)`** (:425-431), then a
  ClipboardData (sub 6) with no materials. A Delete therefore also empties the player's clipboard. That the client
  replaces its own clipboard with this empty one is [I] (0.589 path FUN_1430b7b10 → FUN_143ad8170; the paste cursor
  FUN_1436d6ac0 will not start without materials).
- Nothing is erased, no undo step is recorded, nobody else is told, and **the server's layer-1 version stays the
  same**. On the client the ok responses make the erase permanent there (§3): disk cache, manifest = its CRC.

## 7. Server messages that bring the voxels back on the deleting client

Every row ends in an unrequested or requested CellsUpdate that FUN_144016150 / FUN_144020b50 install over the client's
layer 1 (§4).

| # | Trigger | Server path | Why it reaches the deleter |
|---|---|---|---|
| 1 | **Paste** (Ctrl+V, action 4) touching a cell of the delete | `BuildingClipboard.cs:229-237`: `Paste` → `SendCellResponses(request.Cells)` → `ZoneTerrain.Broadcast(changed, zone)` with **no `except`**. Also the off-grid paste (`BuildingOffGridPaste.cs:131, 359`) | Broadcast (`ZoneTerrain.cs:109-137`) sends a CellsUpdate (all layers) to every player within 256 m, the paster included. The cell responses go first, so the cells are no longer pending and the update is installed [P] |
| 2 | **Placing voxels** (any brush) in a cell of the delete | `LocalEnvironmentServer.cs:83-87`: base (client CRC) ≠ server version → **Stale**, not applied. `Building.cs:786-791` logs "…based on version X (new Y), server has Z; not applied." Then Response **0**, ok responses, **`UndoLocalEdit(session, done.Stale)` → `ZoneTerrain.Resend`** (`Building.cs:811-814`, `ZoneTerrain.cs:73-80`) | Resend is a CellsUpdate with every layer. The deleted voxels return, and **the new voxels in that cell vanish** [P]. Exception: base = 0 accepted by `AcceptsBaseZero` (only after the player's own NewVersion-0 edit) |
| 3 | A refused brush edit (permission, materials, unreadable, Environment Server away) | `Building.cs:714-721, 744-751, 777-784, 792-797` → `UndoLocalEdit` (all cells) | Resend [P] |
| 4 | Undo / redo | `Building.cs:320` `ZoneTerrain.Broadcast(cells, zone)`, no `except` | CellsUpdate to the builder too. Also, the Delete was never recorded, so Ctrl+Z undoes the step before it [P] |
| 5 | Heal tool | `Building.cs:370-373` Resend to the healer | [P] |
| 6 | Anything else that changes the cell | other players' edits (`Building.cs:831`, `except` = the other builder), digs / blasts (`VoxelDestruction.cs:158, 250`, `Abilities.cs:1137`), layer-2 heals (`ZoneTerrain.Regenerate`), site / Spire / outpost reverts, template-instance pushes (`TemplateInstances.cs:171`) | Broadcast within 256 m. Layer 1 rides along because every layer is sent [P] |
| 7 | **Re-request** | A manifest note that changes the client's entry (`SendManifestEntry`: NewVersion-0 edits, notes beyond 256 m, vein `Announce`, `PayOwedNotes`) or the **zone-in manifest** (login, re-zone, teleport) | FUN_14401a660 requests the cell with held layer 1 = client CRC ≠ server, and `TerrainHandler.Answer` sends the server's body [P]. On login the client's cached layer 1 (with the delete) is discarded the same way |

Not a cause: the success path of ApplyDiffRequest. The builder is left out of `Broadcast` (`Building.cs:831`) and the
manifest note carries the client's own NewVersion. Brush edits stay in sync. [P]

## 8. Check against the report

- "Only after selecting voxels and pressing Delete": only Delete and Cut skip ApplyDiffRequest (§6). [P]
- "Copied a section of wall and pasted it: the deleted voxels came back": row 1. The pasted box is next to the wall
  and almost certainly shares 40³-voxel (16 m) cells with the delete. Every cell the paste changes is replaced with the
  server's layer 1, which still holds the deleted voxels. [P paths, I geometry]
- "Placing some voxels after seems to make this happen": row 2. The log should show
  `…: edit of cell … based on version … server has …; not applied.` at Info (`Building.cs:786-791`). It was not in
  today's `server-data/worlds/dev/console.log` (started 05:55 UTC; that session had no Delete). [I until a log shows it]
- The user also had to copy again after the delete: consistent with the Delete emptying the server clipboard (§6). [I]

## 9. Server changes this points to (not made; read-only task)

1. **Handle Delete (action 2) and Cut (action 1) in `HandleSelection`** as live did: erase the box in the server's
   building layer, with the brush rules (claim permission per voxel, materials refunded as Remove does, one undo step
   "Delete" / "Cut", `Broadcast` to everyone except the deleter, `Observe` with the delete effect). Cut stores the
   clipboard first, as Copy does. **Delete must not touch the clipboard.** The exact voxel set the client erases
   (FUN_143f97df0 op 1 / layer argument 1 on the box `min .. max + DAT_144c41158`) still needs tracing so the server
   matches it (lead below).
2. **Read the CellIdAndPermanentVersion list properly** (`u64 cell, u32 base, u32 new`, not `u64 version`). Check
   base the way ApplyDiff does (stale → Resend) and **adopt `new` as the layer version**, as `TerrainEdits.Commit`
   does, so the client's cache and manifest stay valid.
3. **Every refusal of a Delete / Cut** (copy permission, template instance, larger than a cell, Environment Server
   away, stale) must re-send the server's cells after the cell responses (as `UndoLocalEdit` does for brushes), or
   answer ok = false (§3: busy until data arrives, error shown) and then re-send. Otherwise the client keeps a delete
   the server refused.
4. **Unknown actions** must not fall into Copy: log them with `Unanswered` and re-send the cells.
5. Optional, for the user's view: a stale brush edit is answered Response 0 and silently loses its voxels in that cell.
   After items 1-2 this should stop happening for deletes. The stale count is worth exposing in the admin monitor.

## 10. Open leads / what to try next

- What FUN_143f97df0(core, 1, 1, box) → FUN_142aef6d0 (core +0x5a80, vfunc +0x30 of +0x60) erases: only layer 1, or
  layers 0/2 too; solids only or vertices as well; the inclusive box edge (`+ DAT_144c41158`). Next: decompile the
  +0x60 object's vfunc +0x30, or Frida on FUN_1431608c0 in a test copy (not the user's client).
- Whether the local erase's commit (FUN_144020fa0 → listener vfunc +0x60 → FUN_144011830) **also** increments the
  pending count, on top of FUN_143163a90. Two increments against one response would leave the count at 1 after the ok
  and stall for ~50 s. No such stall was reported. Next: identify the cache +0x30 listener (its vtable) and its +0x60.
- Whether the client commits its local paste (FUN_143160230 → FUN_143f8df20) before the server's Broadcast, and
  whether the paste request lists the touched cells as pending (`HandleSelection` assumes so). Next: FUN_143f8df20 and
  FUN_143165810's packet at +0x38.
- The other way cells reach the request builder at zone-in (the "wanted" set +0x408/+0x428 and its generation +0x3f8):
  which function queues cells as they stream into view. Only FUN_144016790 was found writing queue +0x1f0.
- The meaning of table +0x448 in FUN_14401a980 / FUN_14401ad70 (taken as "requested in flight").
- Confirm row 2 in a log: Delete a few voxels, then Add one voxel in the same 16 m cell. Expect the Info line "…not
  applied." and the voxels returning. Confirm row 1 the same way with a paste. Do it on the dev server with the
  TestPlayer2 test client, not the user's.
- Server log levels: today's log had no `base=` Debug lines because every edit in it was refused. For a version trace,
  enable Debug for `Building` (the `edit cell … base=… new=… (server …)` line, `Building.cs:765-767`).

## Function index (0.695)

| Function | What |
|---|---|
| FUN_144016a90 | HandleCellsUpdate (bools +0x48/+0x49 passed through) |
| FUN_144016150 | block-data CellsUpdate: pending → drop + flag; else merge and install |
| FUN_1440164c0 | mesh CellsUpdate (version ≠ 0) |
| FUN_144020b50 | layer merge, disk cache, per-layer manifest update |
| FUN_144016840 | HandleCellUpdateResponse (ok: commit own copy and manifest; !ok: flag) |
| FUN_144016c80 | HandleUpToDateCells (keep own copy) |
| FUN_144016680 | HandleCellManifest (manifest replaced) |
| FUN_144016790 / FUN_144020970 | manifest note / manifest entry set (returns changed) |
| FUN_14401a660 | ClientCellRequest builder (skips pending, sends held versions) |
| FUN_144016fc0 / FUN_143f94490 | busy check (any flagged pending node) |
| FUN_144011830, FUN_1440117c0 (via FUN_143f8cd30) | pending count++ |
| FUN_14401eb80, FUN_144017080, FUN_1440198a0 | pending decrement+flag / remove / flag (vtable methods) |
| FUN_144020fa0 | local commit of an edit into layer 1 (new CRC) |
| FUN_143fd4580, FUN_144049a00 | compress layer → version = CRC32 |
| FUN_143161ab0, FUN_143161b30, FUN_143161c90 | base / new version lists |
| FUN_143161530 | Copy (0) / Cut (1) / Delete (2) |
| FUN_1431608c0, FUN_143163a90 | local erase of the box / mark box cells pending |
| FUN_143165700, FUN_14315e0a0 | ClientSelectionAreaRequest build and send (action at +0x20) |
| FUN_143165810 → FUN_14315c980 → FUN_14315acb0 | ApplyDiffRequest flush (new versions filled) |
| FUN_14315fa80 | brush entry (refused while busy) |
