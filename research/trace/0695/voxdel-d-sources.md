# Voxel delete bug, task D: live rules and data for the selection Delete key

Bug report (2026-10-31): "I deleted some voxels (selection + Delete key), then copied a section of the wall and pasted it,
and the paste restored the voxels I had just deleted. Placing some voxels afterwards also seems to bring them back."
This file covers the archived sources (patch notes, wiki, forum, game rules), the client's own data (help pages, string
table, input profile, tool tables, Lua natives, AS3 UI) and a short client-path check that ties the Delete key to its
packet. Every claim is marked [P] (proven: file:line / FUN_) or [I] (inferred). No code changed. Read-only session.

## 1. What the Delete key is in the 0.695 client [P]

| Key | Input binding (InputProfile_Default.xml) | Lua native (luabind_695.tsv) | Request it sends |
|---|---|---|---|
| Delete (Selection tool active) | ActionSet `SelectionTool` (string 560579), Action `Delete`, string 563921 "Selection Tool: Delete Selection", `luatriggercommand="VoxelClipboard.Delete"` (line 299-300) | `VoxelClipboard.Delete` = FUN_1435dd210 | ClientSelectionAreaRequest, **action 2** |
| Ctrl+X | `BuildMode` Action `Cut`, 563946 (line 625) | `VoxelClipboard.Cut` = FUN_1435dd150 | ClientSelectionAreaRequest, **action 1** |
| Ctrl+C | `BuildMode` Action `Copy`, 563945 (line 622) | `VoxelClipboard.Copy` = FUN_1435dd1b0 | ClientSelectionAreaRequest, **action 0** |
| Ctrl+V | top ActionSet `Paste`, 563909 (line 81) | `VoxelClipboard.Paste` = FUN_1435dd040 | activates the PasteVoxel tool (ToolType 9); the paste itself is action 4 (other writer, FUN_1430b96a0 per BuildingClipboard.cs:11) |
| Ctrl+Z / Ctrl+Y | `Undo` 563907 / `Redo` 563908 (lines 75-80) | FUN_1435dcfe0 / FUN_1435dd010 | ClientUndoRedoRequest |
| Delete (prop active) | ActionSet `Prop`, `Delete`, 563930 "Prop Inspect Window: Disassemble Prop", `Prop.DisassembleObject` (line 328) | - | prop disassemble path (not voxels) |
| Comma | `SelectionTool` `Lock` -> `Tool.SetSelectionLocked` (line 302) | FUN_1436064e0 | none (client state) |

The same bindings are in the 0.589 profile (lp-test/delta/old/InputProfile_Default.xml:292-294, 616-619) [P].

Client call chain for the Delete key [P unless marked]:
1. FUN_1435dd210 finds the tool named "Selection" (FUN_143466b90) and calls ToolBrush vtable +0x108.
   Copy calls +0xf8, Cut +0x100. The "Selection" tool is registered in FUN_14346b7b0(.., "Selection", FUN_1436e3a70(8), 8)
   (part_0105.c:23329); case 8 of FUN_1436e3a70 wraps a VoxelSelectionCursor (FUN_143793230) in a ToolBrush (FUN_1436e3330).
2. ToolBrush (vtable 1447d37c0) +0x108 = FUN_1436e3f60, which forwards to the inner cursor's slot +0x1d8. Cut +0x100 = FUN_1436e3ee0
   forwards to +0x1d0; Copy +0xf8 = FUN_1436e3a50 forwards to +0x1c8.
3. VoxelSelectionCursor (vtable 1447e84a8): +0x1d8 = FUN_143793a10 (Delete), +0x1d0 = FUN_143793990 (Cut), +0x1c8 = FUN_143793910
   (Copy). Each one checks the box (FUN_143793730, a cursor vtable +0x288 test on the box, [I] "may edit here" / red cursor), builds the
   selection box and calls FUN_143161140 / FUN_143161100 / FUN_143160f40 on the net object (DAT_1454311c8+0x32098).
4. FUN_143161140 -> FUN_143161530(.., 2); FUN_143161100 -> (.., 1); FUN_143160f40 -> (.., 0). Each one first checks
   FUN_143f94490(engine) (-> FUN_144016fc0 on engine+0x5a90) and does nothing if it returns non-zero ([I] the engine busy /
   pending-cells list; trace/systems/building.md:103). These three are FUN_143161530's only callers (fn.py -x).
5. FUN_143161530(net, box, action):
   - collects every cell the box touches with its permanent version (FUN_143161b30, Array<CellIdAndPermanentVersion>; the
     version for each comes from FUN_143f90100(engine, cell)+0x14);
   - **action != 2** (copy, cut): fills the clipboard (FUN_143160530; if that fails nothing is sent) and FUN_143b922a0 writes it into
     the request's clipboard part. **For Delete (2) the request's clipboard part stays empty** [P code path; I: so the body has no blocks];
   - **action != 0** (cut, delete): FUN_1431608c0 -> FUN_143f97df0(engine, 1, 1, box) -> FUN_142aef6d0, a timed local VoxelFarm edit
     on the box ([I strong] the client erases the box in its own terrain at once), then FUN_143163a90 calls FUN_143f8cd30 ->
     FUN_1440117c0(engine+0x5a90, cell) for each cell ([I] marks them pending until the server answers);
   - FUN_143165700 builds VoxelConstructionPacketClientSelectionAreaRequest (ctor FUN_14315e0a0, vftable checked) with the action at
     its +0x20 field and sends it (FUN_14315cb20);
   - action 0 only: a tutorial UIEventTriggered (lookup 0x42cbb978).

So the user's guess is right [P for the request; I strong for the local erase]: the Delete key erases the box on the client at once
and asks the server to do the same through ClientSelectionAreaRequest action 2. Nothing else sends this delete. The Delete tool
applied to a selection is a different path: a brush edit (ApplyDiff sub 1).

## 2. What the emulator does with actions 1 and 2 [P]

LandmarkEmulator.WorldServer/Game/BuildingClipboard.cs (HEAD 91c5a566):
- :32 only `CopyAction = 0, SaveDesignAction = 3, PasteAction = 4` exist; 1 (Cut) and 2 (Delete) have no name.
- :213 a Cut/Delete box larger than one cell is refused (`Failed`, cells released). The client has already erased it locally, so the
  client and server go out of step and no message says so.
- :241 Cut/Delete are checked against **MayCopy** (Get Inspired / copy permission), not build permission.
- :273-277 the `else` branch runs `Copy(session, body, request)` for actions 1 and 2.
- :421-450 `Copy()`: the clipboard is taken from the request's voxels (empty for a Delete) plus the props in the box; with no materials
  and no props the clipboard is **removed** (:431). It always sends a ClipboardData reply (:438) back to the client. **No voxel is
  removed from the server's terrain, no prop is removed, nothing is refunded, nothing is broadcast, no undo entry is made.**
- Building.cs:619-631 `SendCellResponses` sends only TerrainCellClientCellUpdateResponse (cell, true, "") per cell: it releases the
  pending cells and carries no cell data, so the client's local erase stays on screen.
- :229-237 Paste: the server places its own clipboard and calls `ZoneTerrain.Broadcast(changed)`; ZoneTerrain.cs:109-121 builds full
  cell updates from the **server's** store (Env.BuildCellsUpdates, always: true) for every viewer, the paster included.

So the chain behind the report [I strong, matches all three of the user's observations]:
1. Delete key: client erases locally; server keeps the voxels (action 2 is treated as a copy).
2. Copy the wall, then paste: the server writes the pasted cells and broadcasts them from **its** store. Those cells still hold the
   "deleted" voxels, so they come back on the paster's screen (and every other player never saw them go).
3. Placing voxels afterwards: ApplyDiff applies only the voxels marked changed onto the server's (undeleted) cell. Anything that later
   re-sends that cell (a refused-cell re-send, a broadcast, a re-zone, a cell request after a version change) shows the deleted voxels
   again. Which re-send it is for "placing" is still open (open questions below).
- Same fault for Ctrl+X: a cut erases locally but the server keeps the voxels and the props, and refunds nothing.
- Side effect: after a Delete, the ClipboardData reply (empty, or props only) may replace the client's paste clipboard ([I], see the
  ClipboardData reader FUN_1430b7b10 -> FUN_143ad8170 named at BuildingClipboard.cs:435).
- Why the audits missed it: luabind_695.tsv lists no "sends packet" for VoxelClipboard.Copy / Cut / Delete (the send is 4 vtable hops
  down). trace/UI-AUDIT.md:74-75 covers only Undo / Redo, and the only saved oversized sample is action 0.

## 3. Live rules from the client's own data [P]

Client help page `<0.695 install>\HelpPages\Tools_Large.html` (lines 21-43, "Selection tool"):
- "ANY other tool (aside from Line tool) can be applied to a selection volume." (26)
- CTRL+X cuts and CTRL+C copies everything within the selection (27-28).
- "Neither cut nor copy will grab naturally occuring terrain, only things that were placed." (30)
- "Both cut and copy affect props as well as blocks." (31). "Cut props are refunded." (33)
- "Cut or Copied selections can be returned to the world by pressing CTRL+V to paste." (35)
- "You can use DELETE or apply the Delete tool to the Selection volume." DELETE "will remove both props and voxels". "The
  Delete tool will only remove voxels." "Resources for all deleted items are returned to you." (37-41)

String table re/out/text_695.tsv:
- 563921 "Selection Tool: Delete Selection" (the keybinding name). 563922 "Selection Tool: Fill Selection" has **no binding**
  in the 0.695 or 0.589 profiles and no decompile reference (grep): an orphan name, [I] a feature that was dropped.
- 563945 Copy, 563946 Cut, 563909 Paste, 563907 Undo, 563908 Redo, 563930 "Prop Inspect Window: Disassemble Prop".
- 574231 Selection palette tooltip "Change large sections of blocks at once"; 574261 Remove tool "Remove blocks".
- 560844 tip "When you select an area and then CTRL+C to copy it, that object is now on your pasteboard."
- 572781 "Press [*keybind*] to Lock your Selection Box", 572782 "Lock Selection"; 565580 "Hold [*keyBind*]: Furthest Face".
- 565550 "Hold [*keyBind*]: Delete" is the Alternate Remove option (Y) of the Add tool (brush 7) and of the **paste cursor**
  (brush 10, mapping 86) and Fill Stamp (18). It is not part of the Selection tool.
- 564595 "Game related props cannot be copied or added to a Design. Please remove any from your selection and try again."
- Client Construction.* keys used (decompile grep): InsufficentMaterials, InsufficentProps, InsufficentPermissions,
  ExceedsMaxTotalTemplateVoxelsCount, OnlyClaimOwnerMayPlaceOrCopyTemplateInstances, MissingPropKnowledge, SkippedProps,
  GeneralError. There is no selection-size or delete-specific message.

Tool tables (<0.695 install>\Resources):
- ToolTypes.txt: 2 Selection (559612, keybinding 22, palette tooltip 574231); 6 SubtractiveBrush "Remove Tool"; 9 PasteVoxel
  "Paste Cursor"; 17 FillStampBrush "Fill Brush".
- ModalToolBrushDefinitions.txt: 14 Selection = brush type 8 (the same type 8 as FUN_1436e3a70 case 8).
- ModalToolOptionMappingDefinitions.txt: the Selection brush (14) shows options 20 "(Alternate) Furthest Face", 38 "Selection Locked"
  (SelectionTool Lock -> Tool.SetSelectionLocked) and 35 "Tweak Mode". **Delete / Cut / Copy are not modal-bar options**; they
  exist only as key bindings.
- AS3 UI: only BuildModeHudWidget.as calls VoxelClipboard (Undo / Redo buttons; as3_695 lines 236-239, as3 lines 225-228).
  No Lua or AS3 UI calls VoxelClipboard.Delete / Cut / Copy (grep re/as3, re/as3_695, re/out/lua_all.txt, testclient Scripts).

## 4. Live rules from patch notes, wiki and forum

Delete key on a selection (server-side effects):
- P2954 (26 Mar 2015) [P text]: a selection with props + the Delete **key** turns all those props back into resources; the Delete
  **tool** deletes only voxels and leaves the props.
- Forum, Fredelas, 9 Dec 2015 (design/forum/all/2015-12-08-55919-…md:232, :251): "Selecting nearby voxels and pressing the Delete
  key will deconstruct props anchored within that selection (and return their resources), but it also destroys voxels." Shown
  workaround: copy, Delete key, then paste back without props. Copy, Delete key, paste was a normal live workflow, so it has to work
  ([I]).
- Wiki Designs.wiki:121-122: Selection + Delete Tool removes all blocks and leaves the props; then the Delete key removes the props.
- P0126 (13 Feb 2014): stray props on a claim can be removed with the selection volume + Delete key "(they should no longer respawn)".
- P0036 (4 Feb 2014): fixed losing props when using "cut" or "delete" with a selection volume (props must be refunded, not lost).
- [I strong] The props are server entities and are refunded, so live's server carried out the Delete key itself. The client sends
  only the box and the cell versions (section 1). On live the zone forwarded ClientSelectionAreaRequest 0x2 to the voxel /
  Environment Server as ZoneSelectionAreaRequest 0xF (trace/architecture/terrain-service.md:32; agent-leads2-d2.md L125). The
  server-side VoxelConstruction block also has DeleteClaimArea 0xE and PasteClaimArea 0xD (layouts still open).

Delete key vs Delete tool (different code paths on live):
- 26 Mar 2015 notes (forum 2015-03-26-54066…md:121): "Deleting voxels using the Delete tool should affects their neighbours less
  (using the Delete key acts differently and will still affect neighbouring voxels as before)."
- 28 Jul 2016, dev Terrogaunt (2016-07-27-57751…md:809, P6209): "the delete tool applied to the selection volume" gave bad
  smoothing data. The workaround given on Landmark Live (Fredelas :493, Gromatici :767): select, fill, then the Delete **key**, which
  "resets the grid". P6426 (29 Aug 2016) repeats it: heal or add, then the delete KEY (not volume).
- [I] So the Delete key was a box erase computed by the server (clean empty voxels, neighbours handled by the server's own rule). It
  was not the client's brush cell. A rebuild has to compute the erase server-side from the box (as our paste does) rather than wait
  for client cell data. The request carries none (section 1).

Refunds, undo, permissions, limits:
- Wiki Delete Tool.wiki "Uses": deleting player-built objects returns their materials to the inventory; deleting natural landscape
  gives nothing back. Help page: "Resources for all deleted items are returned to you." Emulator rule today: 1 item per voxel, refunded
  on removal (docs/gameplay.md:292). A selection delete should refund in the same way [I].
- P1052 (22 May 2014): refunds (e.g. undo) go to overflow when the bag is full.
- Undo: wiki Controls.wiki:102-106 / Building Tools.wiki:179-185, Ctrl+Z / Ctrl+Y up to 20 steps, "All tools, all building actions".
  P1645 / P1680 (Sep 2014): copying a selection no longer breaks the undo / redo stack. P5496 (24 May 2016): delete in tweak mode then
  undo. P1716: deleting movers works with undo. [I] A selection Delete was an undoable step (the voxels back, and the props as live
  did; not stated anywhere yet).
- Permissions: P2530 (17 Dec 2014) "Copy Only" lets visitors copy but not "do anything else to it, including delete". P4809 (26 Apr
  2016) reorders Customer / Copy Only. So a Delete (and a Cut's removal) needs **build** permission, not Get Inspired. Today's
  server checks MayCopy for both (section 2).
- Bounds: P0940 (7 May 2014) a selection past the allowed bounds turns red. P4558 (12 Apr 2016) the selection tool stops at the edge
  of the build site. The client tests the box before sending (FUN_143793730, [I] permission box).
- Size: no archived limit for a selection delete. P4545 (12 Apr 2016) "Tools should now work better on larger selections", P4547 Smooth
  works on selections over 100 blocks a side. [I] Live accepted multi-cell deletes. Our one-cell limit for actions 1/2 (:213) is
  ours, not live's.
- Natural terrain: help page, cut/copy do not take natural terrain. The Delete key / tool do remove it (wiki Delete Tool, Selection Tool
  table "remove all material").

Bugs on live with deleted voxels coming back (similar symptoms, different causes):
- P0672 (16 Apr 2014): "user placed voxels would sometimes re-appear at a distance if they were deleted or removed with undo": a
  LOD (far-cell) copy not being updated. Worth checking after the main fix: the server's lower-LOD cells after a selection delete.
- P5355 (11 May 2016): LOD issues when copying and pasting a selection from one side of a build site to the other.
- P2853 (6 Mar 2015): paste with "remove voxels" (paste as delete) did not remove voxels. Fixed "after the 5am downtime", so a
  server-side fix [I]. P5755 / P5802 / P5853 (Jun 2016): paste-as-delete with rotated shapes and with Designs.
- P5429 / P5652 / P5762 / P5809 (May-Jun 2016): undo of rotated pastes.
- No archived note describes deleted voxels coming back after a copy or paste. Searched: patchnotes.tsv (6575 rows), design/wiki (2337
  pages), design/forum/all (203 threads), re/out/forum_raw (9 raw pages with "delete key", all already in design/forum). On live
  the server applied the delete, so this would not have happened [I].

## 5. What a live-matching handler needs (for the fix task; nothing built here)

[I, from sections 1-4] ClientSelectionAreaRequest action 2 (Delete) on the server should do this:
- check **build** permission for every voxel in the box (as ApplyDiff does) and refuse with InsufficentPermissions otherwise;
- erase every voxel in the box (placed **and** natural), computed on the server from the box;
- disassemble every prop anchored in the box and refund its resources (P2954, help page);
- refund the placed voxels' materials (help page; 1 item per voxel rule);
- record one undo step (20-deep stack);
- answer the cells and broadcast the changed cells to the other players. Whether the builder also gets the cells back is an open
  question below;
- not touch or replace the player's clipboard, and send no ClipboardData.

Action 1 (Cut) = copy (props in the clipboard, ClipboardData reply as now) **plus** the same erase / prop removal (props refunded,
help page) / refund / undo as Delete. The Cut / Delete limit of one cell (:213) is not live's.

## 6. Open questions (with what to try next)

1. Does the client really erase locally for actions 1/2? FUN_1431608c0 -> FUN_143f97df0(engine,1,1,box) -> FUN_142aef6d0 -> VoxelFarm
   editor vtable +0x30, op 1. Next: print the editor vtable +0x30 target and the op table (FUN_142aef6d0 maps op 1/2/4 to stat slots)
   to prove op 1 = erase; and how FUN_142aef6d0's caller passes shape 10 (local_90 = 10 in FUN_1431608c0).
2. What FUN_1440117c0(engine+0x5a90, cell) does (pending mark vs version bump). If it bumps the client's cell version, a later server
   answer or manifest with an older version may make the client re-request the cell. That would be the "placing voxels after
   brings them back" path. Next: fn.py FUN_1440117c0 and FUN_144016fc0 (the same object; the busy gate).
3. Which server message makes "placing some voxels after" restore them: a refused-cell re-send in Building.cs ApplyDiff, a version
   mismatch in TerrainHandler cell requests or manifests, or LOD. Next: read Building.cs ApplyDiff's re-send and versions, and the
   Response sub 7 / TerrainCellClientCellUpdateResponse readers for cell versions; reproduce on the dev server with server logs
   only (no Frida while the user plays).
4. What the client does with the ClipboardData reply after a Delete (empty / props only): does it replace the paste clipboard so a
   following Ctrl+V pastes nothing? Next: FUN_1430b7b10 -> FUN_143ad8170.
5. What the client does when a Delete / Cut is answered `Failed` (oversized, :213) after its local erase: any rollback or re-request?
   Next: the Response sub 7 handler FUN_1430b7bf0 case 7 for result 1 with sub 2.
6. Undo of a selection Delete on live: are props restored as props or as resources, and is it one undo step? No archive states it.
   Next: wiki history / Wayback for "Selection Tool" (2015-2016 revisions), Landmark Live videos (Twitch VOD 80299884 near 38 min is
   cited, not archived here), the client's ToolPacketUndoStackChange reader (RTTI 1447788c8).
7. The server-side live messages DeleteClaimArea 0xE / PasteClaimArea 0xD / ZoneSelectionArea 0xF: layouts still open (L125). They
   may show which fields live's Env server used for a delete. Next: their readers in the VoxelConstruction registry (part_0107.c).
8. "Selection Tool: Fill Selection" (563922): an orphan string, also in text_589.tsv with no binding there. Patch notes have no
   "fill selection". Was there ever a Fill-selection key (pre-0.589 builds, beta)? Next: older client builds' InputProfile, and
   Wayback copies of the 2014 "Controls" wiki page.
