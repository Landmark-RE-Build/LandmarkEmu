# Voxel delete comes back: server audit (Task C, 2026-10-31)

Bug report: selected voxels removed with the Delete key come back after a copy and paste of the wall nearby, or after
placing voxels. Read-only audit of the server (client-0.695 HEAD 91c5a566). No code changed, nothing built, game not
touched. Markers: [P] = proven (file:line or FUN_), [I] = inferred.

## 1. Root cause in one line

The selection tool's **Delete (and Cut)** is a ClientSelectionAreaRequest (0xF4 sub 2) with **action 2 (Cut 1)**. The
server has no branch for actions 1 and 2: they fall into the **Copy** branch, so **nothing is erased on the server**.
The client has already erased its own copy and gets "ok" for every cell, so from then on the player's cell and the
server's cell differ. Every later path that sends the server's copy of that cell to the builder puts the deleted
voxels back: the paste broadcast, the stale-edit resend after placing voxels, a relog or refetch, undo, heal, digs, and
other players' edits.

## 2. What the client sends on Delete

- Delete key → `VoxelClipboard.Delete` (0.695 `InputProfile_Default.xml:299-300`) → Lua native FUN_1435dd210
  (`re/out/luabind_695.tsv:746`) → the "Selection" tool's vtable +0x108 [P]. That reaches VoxelSelectionCursor vtable
  slot 59, FUN_143793a10 (`rtti_695.tsv:6970`; slots 57/58/59 = FUN_143793910 / 990 / a10) [I strong: the tool → cursor hop
  is not traced].
- FUN_143793910 → FUN_143160f40 → FUN_143161530(…, **0**) = Copy; FUN_143793990 → FUN_143161100 → (…, **1**) = Cut;
  FUN_143793a10 → FUN_143161140 → (…, **2**) = Delete (part_0087.c:32570 / 32650 / 32665) [P].
- In FUN_143161530 (part_0087.c:32864) [P]:
  - FUN_143161b30 builds the CellIdAndPermanentVersion list: u64 cell, u32 at +8 = the cell's version *before* the change
    (FUN_143f90100(...)+0x14).
  - Action ≠ 2: the voxels are copied into the request (FUN_143f9a670 + FUN_143b922a0). On Delete the block list stays
    empty.
  - Action ≠ 0: **local erase** FUN_1431608c0 → FUN_143f97df0(VoxelFarm, 1, 1, box), box = area bounds
    (FUN_143c18b20) with +DAT_144c41158 (= **0.5**, read from Landmark64.exe .rdata) on the max corner. Then
    FUN_143163a90 marks each cell pending (FUN_143f8cd30).
  - The loop then writes u32 at +0xc = the cell's version *after* the erase (lookup +0x14). So the request carries
    **(base, new)** per cell, the same as ApplyDiff.
  - Then FUN_143165700 sets action = the argument and sends.
- Paste = action 4 (FUN_1431642e0, FUN_1431649d0); Save Design = action 3 (FUN_143165440) [P].
- Saved copy sample `server-data/worlds/dev/Reports/selections/sel-0-20261013-231013.bin`: 4 cells, each with
  base == new (ec50a4c2/ec50a4c2 …), which fits "copy changes nothing" [P].
- On the "ok" reply, the client's HandleCellUpdateResponse (0.695 FUN_144016840) writes the block's own version
  (+0x14) to its cache and manifest. So the builder now holds the post-delete version [P for the handler; I that it
  is the same object as FUN_143f90100's].

## 3. What the server does with actions 1 / 2 (every line)

`LandmarkEmulator.WorldServer/Game/BuildingClipboard.cs`, HandleSelection:

| Step | Line | Effect on Delete (2) / Cut (1) |
|---|---|---|
| ReadSelection reads the cell list | 128-133 | Keeps only the u64 cell id. **base/new versions skipped** [P] |
| Box ≥ 40 voxels on any axis | 213-228 | Refused with Failed (`sel-2-*.bin` sample saved) and cells answered. **No resend**, so the client keeps its local erase [P] |
| Not paste | 229 | skipped |
| MayCopy (Get Inspired) | 241-247 | Wrong permission for a delete (it should be Build). Refusal = 0xE, **no resend** [P] |
| CoversTemplateInstance | 251-257 | Refusal 0x1A, **no resend** [P] |
| Not Save Design | 258 | skipped |
| **else → Copy** | 273-277 | Response(2, 0) = success, then Copy() [P] |
| Copy() | 421-457 | Delete has no blocks, so request.Voxels == null → **clipboards.Remove(player)** (430-431). It sends ClipboardData with the props in the box, 0 materials, 0 blocks, "has voxels" 0 → [I] the client's paste clipboard is replaced by an empty one. Cut: the clipboard is set from the cut blocks (as copy). **Neither erases anything** [P] |
| Cell answers | 278 | ClientCellUpdateResponse ok for every listed cell, so the client commits its local erase [P] |

Never done for 1/2: no write to layer 1, no material refund (PayFor), no ownership clear, no undo record (Remember),
no Broadcast to others, no ResponseObserved, no version update. Other players never see the delete [P].

## 4. Where the stored cell can differ from the builder's

### (a) Selection Delete / Cut

- D1. Actions 1 and 2 are never applied (section 3) [P].
- D2. The server's layer-1 version stays the old one, V_s. The client holds the post-delete version V_d (section 2) [P/I].
- D3. Refusals for 1/2 (box ≥ 40, Get Inspired, template instance) send no resend, so the client keeps its own erase [P].
- D4. Related: a paste with **Remove voxels / Paste as Delete** (`AsDelete`, read at BuildingClipboard.cs:155 and
  BuildingOffGridPaste.cs:84) is ignored by TurnedVoxels and OffGridVoxels. That paste **adds** the clipboard's solid
  voxels instead of removing them (comment at BuildingClipboard.cs:89-96, 722-724) [P]. The server's broadcast is what
  the client draws there, so the result is wrong for everyone, not a desync [I].

### (b) Brush Remove (ApplyDiff with removals): stored correctly

- Parse (Building.cs:687-711): the mask plus the client's cell. A removed voxel is either flags 0 (no override) or
  flag 1 + material 0 (air, flag bit 0x40). Deserialize keeps both (BlockData.cs ~170-200) [P].
- Permission: MayApply checks every changed voxel, removals included (Building.cs:744-752; TerrainEdits.MayApply
  1790-1805) [P].
- Prepare (TerrainEdits.cs:1855-1880): refund −1 for each solid overlay voxel replaced. Commit (1906-1955) writes
  **every** changed index whatever its value, air included. Ownership: VoxelOwnership.Set with "solid" false, so the
  removed voxels belong to nobody [P].
- Version: the client's NewVersion is adopted (1932-1936). NewVersion 0 gives a server counter plus lastZeroEdit [P].
- Builder gets nothing back except a manifest note (Building.cs:818-823) [P].
- Divergence only on the refusal and stale paths, and those resend the server's copy, so they converge but lose the
  edit:
  - stale, Building.cs:786-790 and 814;
  - permission, 744-752;
  - materials, 791-797;
  - unreadable, 713-722;
  - environment server away, 775-785.
- KeepVertexOnlyVoxels=false would drop flag-2 voxels (cosmetic; the default is on) [P].
- Side finding: the vein-aware MaterialDelta(id, changed, result) (TerrainEdits.cs:1748-1785) has **no caller**.
  ApplyEdit uses Prepare, which lacks the vein rule, so an edit whose mask covers unchanged vein ore is charged for that
  ore again [P].

## 5. Every path that sends the server's copy back to the builder

Each of these restores the deleted voxels in that cell.

1. **Stale ApplyDiff** → `UndoLocalEdit(session, done.Stale)` Building.cs:814 → 402-406 → `ZoneTerrain.Resend`
   ZoneTerrain.cs:73-80 (all layers, always). The stale test is LocalEnvironmentServer.cs:83-88.
2. Refused or unreadable ApplyDiff → UndoLocalEdit (Building.cs:720, 750, 782, 795).
3. Heal: resend of every healed cell (Building.cs:370-372), plus its refusals (344-346, 356-357).
4. Undo / redo: `ZoneTerrain.Broadcast(edit.Cells, zone)` without an except, so the builder is included (Building.cs:320).
   The delete was never put in the history, so Ctrl+Z after a delete undoes the edit before it and sends those cells.
5. **On-grid paste**: `changed = byCell.Keys ∪ request.Cells the server holds` (BuildingClipboard.cs:590) →
   `ZoneTerrain.Broadcast(changed, session.ZoneId)` with no except (236). The paster is a viewer (ZoneTerrain.cs:116) and
   within 256 m gets the full CellsUpdate (121, 131-132), built from TerrainEdits.BuildUpdate (2301-2340), whose layer 1
   still holds the deleted voxels. Also PlaceDesign (344).
6. Off-grid paste: the same, at BuildingOffGridPaste.cs:243 and 131.
7. The manifest note to the builder after an edit (Building.cs:818-823). If the version differs from the client's →
   [I] the client fetches the cell → TerrainHandler.Answer (TerrainHandler.cs:66-90, 120) sends the server's copy.
8. Any ClientCellRequest whose held layer-1 version ≠ the server's: relog, cache eviction, coming back into range
   (TerrainHandler.cs:70-90) [P server; I client trigger].
9. Broadcasts with no except that include the builder:
   - digs (Abilities.cs:1137);
   - the layer-2 heal tick (ZoneTerrain.cs:207-215);
   - VoxelDestruction (158, 250);
   - TemplateInstances.cs:171;
   - claim and Spire reverts;
   - another player's edit in the same cell (Building.cs:831 leaves out only *that* builder).

## 6. The exact lines in the two reported sequences

**Delete → copy → paste.**
1. The Delete goes to BuildingClipboard.cs:273-277 (Copy). Nothing is erased.
2. The Copy (action 0) stores the client's blocks, which already lack the deleted voxels. Fine.
3. The Paste writes the clipboard's solid voxels (WriteCells → TerrainEdits.Write, Version++).
4. Then **BuildingClipboard.cs:590 + 236** send every written or listed cell to everyone, the paster included.
5. That CellsUpdate's layer 1 is the server's copy, which still has the deleted voxels, so they reappear [P server;
   I client replaces its layer from the update, docs/terrain.md "A CellsUpdate must carry every layer"].
6. After this the paster's client holds the server's version again, so the delete is lost for good.

**Delete → place voxels.**
1. The ApplyDiff's base version for the cell is the client's post-delete V_d. The server still has V_s.
2. **LocalEnvironmentServer.cs:83-88** marks the cell stale: not applied, not charged. It is logged at Info as
   "edit of cell … based on version …; not applied." (Building.cs:786-790).
3. Response 0 still goes out (812), so the player sees no error.
4. **Building.cs:814 → ZoneTerrain.cs:77-79** sends the server's cell. The deleted voxels come back, and the voxels just
   placed in that cell vanish [P server; I base = V_d].
5. If the Add tool sends base 0 and AcceptsBaseZero (TerrainEdits.cs:1894-1900) lets it in, the edit applies with no
   resend. The deleted voxels then come back later through path 5, 7 or 8 [P].

## 7. How to confirm from the logs (no game needed)

- A delete shows up as Copy's Debug line "<name> copied X×Y×Z (n props, materials )." with **empty materials**
  (BuildingClipboard.cs:455). Nothing else is logged.
- A later placement logs "…; not applied." at Info.
- The dev world's `console.log` for the current run (from 05:55) has no copy, paste or delete lines. The repro ran
  before this container start, so there is no runtime evidence yet.

## 8. Server changes proposed (not made)

1. Add Cut (1) and Delete (2) branches to HandleSelection.
   - Read the per-cell (base u32 @+8, new u32 @+12) in ReadSelection.
   - Per cell, check **build** permission over the box (MayApply on the box mask) and the stale base like ApplyEdit.
   - Erase in layer 1 the same voxels and values as the client's FUN_143f97df0(vf, 1, 1, [min, max+0.5]).
   - Do it as one Env operation with before/after values, then:
     - refund (PayFor negative delta) and clear ownership;
     - Remember "Delete" / "Cut";
     - set each layer's version to the client's new value (as Commit's clientVersion).
   - Then answer, without sending the builder the cell:
     - Response 0, SendCellResponses, and the manifest note;
     - `Broadcast(cells, except: session)` and Observe(ObservedDelete).
   - Cut also does today's Copy part. Delete must **not** send ClipboardData or clear the clipboard.
2. On every refusal of 1/2, Resend the listed cells (as UndoLocalEdit), because the client has already erased.
3. Allow boxes ≥ 40 for 1/2 (the erase is a box, there are no blocks to read), or at least resend.
4. Paste with Remove voxels (AsDelete): write air where the clipboard is solid, using the client's CClipboardData rule
   FUN_142a7eeb0, on both paste paths.
5. ApplyEdit: use the vein-aware material delta.
6. Delete key in Play Mode / props in the box: check whether live's Delete also removed the props in the box (see open
   questions).

Edits made before the fix live only in the players' caches. They will reappear on the next fetch (an account reset is
planned anyway).

## 9. Open questions (what to try next)

- What FUN_142aef6d0 mode 1 writes on layer 1: air (flag 1, material 0, carving generated ground too) or cleared
  overrides (flag 0)? Also the box units (+0.5 voxel or +0.5 dm) and whether the max corner is inclusive. Try next:
  core+0x60 vtable +0x30 in the decompile, or Frida on FUN_143f97df0 with a known box (test copy only).
- Does the block version (+0x14) change straight after the local erase (new ≠ base in the request)? Log the request's
  versions once branch 1/2 exists, or capture an action-2 body (raise LargerThanCell's sample to every action-1/2 request).
- Does the client also delete props inside the box on Delete or Cut? FUN_143793ce0 / FUN_1436ea360 in the cursor path
  are not read yet.
- Does the client restore its own erase on a non-zero Response for request type 2 (FUN_1430b6560 path)?
- Does the client fetch a cell when a ModifiedCellManifestUpdate names a version other than its own, for a cell it
  edits (lead L-E4-2)?
- Is the hop from the Selection tool's +0x108 to VoxelSelectionCursor slot 59 direct? (Find the class whose vtable
  +0x108 calls the cursor's +0x1d8.)
