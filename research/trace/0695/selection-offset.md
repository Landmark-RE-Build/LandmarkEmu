# Selection box offsets: voxels, props, copy grid (0.695)

Workflow wf_a6205152-f54 (2026-11-01): the 0.695 client traced, constants read from Landmark64.exe. Built on branch
`local/selection-props` (worktree re/wt-selprops), commit d0b4b6dd. Server code: LandmarkEmu
`Game/BuildingSelectionBox.cs`, `BuildingClipboard.cs`, `BuildingDelete.cs`, `BuildingOffGridPaste.cs`, `Props.cs`;
tests `LandmarkEmulator.Tests/Rules/SelectionOffsetTests.cs`; docs/terrain.md "The selection box".

Earlier traces this builds on: voxdel-a-client-delete.md (the Delete / Cut chain), voxdel-b-client-resync.md,
voxdel-c-server.md, voxdel-d-sources.md (help page, P2954, P0036), phaseE2-building.md L199 (paste origin).

## Notation

ClientSelectionAreaRequest (0xF4 sub 2) box: level-2 cell, origin X / Y / Z (voxels in the cell), size SizeX / Y / Z.
Per axis a: **G_a = cell_a · 40 + origin_a** (global grid point), **S_a = size_a**, **v = 4 dm** (one voxel).
A grid point g sits at g·v; its block is drawn over [(g − ½)v, (g + ½)v] (the mesher samples materials at grid points
and a lone solid voxel is the cube between the centres of the eight cubes around it, docs/terrain.md "How the client
draws a dig").

## Findings [P]

1. **The visible box** is [(G − ½)v, (G + S − ½)v] on every axis: its faces sit on block faces and it holds exactly the
   grid points G .. G + S − 1.
   - FUN_143794700, FUN_1436eec30: the selection cursor's box drawing / dragging.
   - FUN_143c1a8a0: a dragged box (min, max, world units) → the request: origin = floor((min + 0.75v) / v + 0.5),
     size = floor((max − min) / v + 0.75) (in the decompile the 0.75v term is voxel · DAT_144c411c0 · DAT_1446f40a0,
     added to min and to max − min, and the divisor DAT_144c41178 / voxel; constant values as the workflow read them
     from the exe). The visible box round-trips through it (SelectionOffsetTests.VisibleBox_FacesSitOnBlockFaces).
2. **Delete / Cut voxels (server)**: Building.DeleteMasks already erased exactly G .. G + S − 1 per axis. Correct, kept,
   together with the resend of every listed cell.
3. **The client's own local erase** (Cut and Delete, action ≠ 0): FUN_1431608c0 → FUN_143c18b20 gives the AABB
   [G·v, (G + S − 1)·v] (+ DAT_144c41158 = 0.5 dm on the max, voxdel-a) → FUN_143f97df0(core, 1, 1, box, …, 10) →
   VoxelFarm op 1; the stamp (FUN_1440084e0) covers [(G − ½)v, (G + S − 1.375)v] in grid-point terms: **grid points
   G .. G + S − 2**, one layer short of the visible box on each + face (and nothing at all for S = 1 on that axis).
   The server must not copy this: it erases the box the player saw, and its resend replaces the client's short erase.
   Written down as a model (not proven by the test) in
   SelectionOffsetTests.ClientsOwnErase_IsOneLayerShortOnThePlusFaces_SoTheServerMustNotCopyIt.
4. **Copy voxels** (Copy, Cut, Save Design): the client's own copy (FUN_143160530 → FUN_143c18800: origin − 1 on every
   axis, size + 1), an (S + 1)-point grid from G − 1: **clipboard grid point 0 = global grid point G − 1**, at (G − 1)·v;
   clipboard grid points 1 .. S are the visible box's voxels.
5. **Props (server, before the fix)**:
   - Delete took props from [G·v, (G + S)·v] (half a voxel high: the − half-layer of the box left out, half a voxel past
     the + faces taken);
   - Cut, Copy and Save Design (SelectionBox / PropsInSelection), MayCopy (Get Inspired) and CoversTemplateInstance used
     [G·v, (G + S + 1)·v]: up to 1.5 voxels past the + faces;
   - copied prop offsets were measured from G·v (Props.InBox) instead of clipboard grid point 0 = (G − 1)·v: pasted
     props landed one voxel low and towards −x / −z;
   - the on-grid paste (PlaceProps) added one voxel on every axis the turn flips: props there were two voxels off;
   - the off-grid paste maps clipboard grid point 0 to its origin (OffGridVoxels: l → origin + l·v·R), which is right
     once offsets are measured from grid point 0.
6. **Brush / Mesh Shapes edits** are stored exactly as sent (BuildPlacementTests); no server change. The Building.cs
   brush log's "min−aim" subtracted the cell-local aim from a global minimum (meaningless); now min − (aim cell · 40 +
   aim).

## Found while building (2026-11-01)

- **The props' turn on an on-grid paste was the transpose.** The org's #12 (b3fccd0a, 2026-10-25) changed the voxel
  map and the props' position map from row·s to Σ s_j·row_j (the client's paste preview FUN_14315feb0, Frida 10-24) but
  left the props' quaternion built from the matrix's columns (the old map). For a quarter turn a pasted prop faced the
  opposite quarter turn (a half turn and the mirrors are their own transposes). Now built from the rows
  (Building.PasteTurn), as the off-grid paste already did. That the client applies a prop's quaternion the way
  System.Numerics does is [I] (the convention used everywhere in Props).
- **Placed Player Studio design boxes** are recorded as [lowest grid point · v, (highest + 1) · v] (Paste →
  TemplateInstances.Record), half a voxel + of their blocks; with the visible selection box a design whose highest grid
  point is G − 1 still counts as overlapping (kept: the safe side for copy protection).

## What the server does now

| What | Box / grid | Code |
|---|---|---|
| Delete / Cut voxels | G .. G + S − 1 | DeleteMasks (unchanged) |
| Props taken (Delete, Cut, Copy, Save Design) | pivot in [(G − ½)v, (G + S − ½)v], each axis [min − 1 cm, max − 1 cm) | Building.VisibleBoxLocal, HoldsPivot; Props.StandingIn (InBox and DisassembleInBox) |
| Get Inspired, placed-design check | the visible box | MayCopy, CoversTemplateInstance |
| Copied prop offsets | from (G − 1)·v | Building.ClipboardGridZero, Props.InBox |
| On-grid paste, props | base + M·q (q = offset in voxels), turn from the rows | Building.PastedPropAt, PasteTurn |
| Off-grid paste, props | origin + offset·R | Building.OffGridPropAt |

**Tie rule [I, ours]** for a pivot exactly on a face: half-open per axis with 1 cm tolerance: a prop standing on the
box's floor (on the block under the selection) is taken, one standing on its top is left; the side faces likewise, so
two boxes sharing a face never both take a prop. Live's own rule is not known (OPEN-LEADS L250).

**Diagnostics (log only)**: the request's per-cell layer-1 versions before / after are parsed
(SelectionRequest.CellVersions); after a Delete / Cut each listed cell is logged at Debug with whether
TerrainEdits.HoldsClientCopy(cell, zone, after) holds (in-process Environment Server only). "Not the server's content"
is expected where the client's erase stopped short (+ faces) or wrote air the server leaves out (open air). It also
shows whether the client's "after" is re-read after the erase synchronously (voxdel-a step 6, [I]).

**Old data**: clipboards and designs saved before 2026-11-01 keep the old offsets; no
migration, as all accounts are being reset (2026-10-17). (Props paste one voxel short along each clipboard axis: one
voxel low when unturned.)

## Not verified yet

- In game: a Delete / Cut / Copy + paste with props on the floor, on the top, at the + faces, for an unturned and a
  quarter-turned paste; whether the client's paste preview shows the props where the server now places them (the
  preview's own origin for PropData offsets was not traced).
- The client's erase model (finding 3) is from the decompile; one `cellcompare on` / Debug run of the new
  HoldsClientCopy lines after a Delete would confirm it in the client's own data.
- Live's Delete-key erase (as short as the client's?) and the Mesh Shapes half-voxel shift: OPEN-LEADS L262.
