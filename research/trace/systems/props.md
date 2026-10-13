# System: props (module 3 of REBUILD-PLAN) — traced 2026-10-13

Archive: tag `props-v1`, files in `archive/props-v1/` (Props, PropSettings, PropEffects, PropAnimations, PropLimits,
RightClickMenus, StoryTools).

## PropBase 0xED, S→C (client dispatcher FUN_142f1b980)

Header on every message: u16 opcode, u8 sub, u64 player guid (FUN_142b94540). The dispatcher handles these subs:

| Sub | Name | Reader → handler | What the client does | Server |
|---|---|---|---|---|
| 2 | AccessReply / PropData | FUN_142f08e10 → FUN_142f1ab30 | opens the placement / edit gizmo | ✔ |
| 4 | SaveReply | FUN_142f0c0e0 → FUN_142f1c7b0 | ok / "can not be placed there" | ✔ |
| 6 | PickUpReply | FUN_142f09c10 | errors 3/9/6/7/10/11/13 (texts in the dispatcher) | ✔ |
| 8 | DeleteReply | FUN_142f09c10 → FUN_142f1b650 | | ✔ |
| 0xA | ListReply {Array<PropData 0x68>} | FUN_142f0bc80 → FUN_142f1b7e0 | fills Lua `Design.Props` (Model, ImageSetId, CategoryId, PlaceMode, ModelId) | ✗ — no 0.589 UI reads Design.Props; asked by RequestList (sub 9, FUN_142f21130 / FUN_143305690, a vtable command) |
| 0xB | VolumeReply {Array<TemplateCommon::PropData>} | FUN_142f0be10 → FUN_143ad8170 | sets the PASTE CLIPBOARD's props | ✗ (design props, below) |
| 0x10 | UndoRedoNotification {u64 prop, vec4 pos, vec4 rot, vec4 scale, u32} | FUN_142f09990 → FUN_142f1c880 | moves the prop (and an open gizmo); u32 goes to the tool observers | ✔ 2026-10-13 (prop-move undo) |
| 0x11 | MultiPropPlacementPreview {u64, Array<PropData>} | FUN_142f096a0 → FUN_143066850 | (only when the u64 is the player's current site +0x32508) empties the clipboard voxels, loads the props, flag +0x12f = props only, switches to the PasteVoxel tool | ✗ |
| 0x15 | EditPropTextReply | FUN_142f097d0 | HudStateManager:OnOpenTextEdit | ✔ |
| 0x17 | AvailableTintsReply | FUN_143481840 | tint palette | ✔ |
| 0x1C | EditSettingsReply | FUN_142f098a0 | settings window | ✔ |

0xD PropProcessorStateReplication {u64 (+0x20/+0x24)} is read by the prop tool itself (FUN_142f1c510 → FUN_142f1a9c0);
the client sends 0xC PropProcessorStateNotification {u32 state, u64, u32} as its tool state changes (101× in play).
Not built: what the server mirrored back is unknown.

## PropBase 0xED, C→S

| Sub | Name | Server |
|---|---|---|
| 1 | Access {u64 prop, u64, u32} | ✔ (opens an edit session; records the start for undo) |
| 3 | Save {…, u64 prop, u32, 6 × vec4 (pos, rot, scale, old pos, old rot, old scale), u8, u64, u8} | ✔; moves of a placed prop → one undo step per edit session |
| 5 / 7 | PickUp / Delete {u64} | ✔ |
| 9 | RequestList | ✗ (no UI) |
| 0xC | ProcessorStateNotification | informational |
| 0xF | Duplicate {u64 prop, u64 site (+0x348 of the current site), u8, u8} (Lua DuplicateObject) | ✔ — a new placement session of the same prop with the original's tint |
| 0x12 | PlacePropById {u32} | ✔ |
| 0x13 / 0x14 | text set / ask | ✔ |
| 0x16 / 0x18 | tint set / list | ✔ |
| 0x19 / 0x1A / 0x1B | name / enable / settings | ✔ |

## Undo of prop moves (built 2026-10-13)

The build undo history (Building, 20 steps, Tool.UndoStack names) takes "Move Prop" steps: Access opens an edit
session with the prop's transform; every changed Save of that session updates the same step's "after"; undo / redo
put the prop at before / after (if the player may still rearrange it there), send 0x10 to the player, and the move
reaches others once it settles (Props.Update). Verified with dev `propmove` + `undo` / `redo` and Frida on
FUN_142f1c880 (re/test/scenarios/adhoc/propundo.*).

## Props in copies and designs (built 2026-10-13)

See building.md "Props in copies and designs" for PropData. The client gathers no props itself; the server's data
carries them:
- Copy (selection request, copy action): live answered ClipboardData {PropData list …} (FUN_1430b7b10 also loads the
  list into the paste clipboard via FUN_143ad8170) and/or VolumeReply 0xB.
- Save design: TemplateBasicData's PropData list (PropCount in the Designs window).
- Paste / place design (action 4, FUN_1430b96a0 / FUN_1430b9d50): the request carries that PropData list back; the
  server places each prop the player has (an item of it in the bag), lists the rest in VoxelConstruction 9
  ZoneMissingPropList ("Construction.SkippedProps", "%d : %s") and unknown ones in 8 ZoneUnknownPropList
  ("Construction.MissingPropKnowledge").
- PropData position is metres from the paste origin; rotation quaternion and scale per prop; +0x0C prop definition.

Server (2026-10-13): Copy gathers the placed props inside the selection box (Props.InBox; box = origin voxel corner
+ (size+1) voxels) into the clipboard and ClipboardData's PropData list; saving a design stores them beside it
(TemplateProps/<id>.json) and TemplateBasicData lists them. Paste / place design places each prop the player has an
item of (taken from the bag), at base + M·p (+1 voxel per flipped axis), turned with the paste
(quaternion of M composed after the prop's own); the rest go to ZoneMissingPropList (sub 9). The undo step picks the
placed props up again (items back); redo places them again. Verified with dev giveprop / copyhere / pasteat / undo
and Frida on FUN_1430b8590 (re/test/scenarios/adhoc/designprops.*): 1 of 2 placed, client listed "1 x 572907".
Not checked: turned pastes (the client's quaternion handedness), the design-save path through the real UI.

## Text props (built 2026-10-13)

- PlayerUpdate 0x79 DisplayPropText (dispatcher PlayerUpdate_Dispatch switches on sub−1, case 0x78; reader
  FUN_142b8e6c0 reads a ONE-BYTE sub, so the opcode enum is 0x260079 → wire 26 00 79): {u64 prop, string text,
  u32 mode, u32 seconds}. Handler FUN_142ee6ce0: 1 text window (HudStateManager:OnOpenTextDisplay), 2 on screen
  (FFF6AA), 3 chat "[prop name] says: …", 4 bubble over the prop, 5 bubble + chat.
- Links: Rustic Sign 2939 / Fjorden Book 2228, 2229 / Gold Trimmed Scroll 2639 have the action "...Have this sign /
  book / scroll display the text." (577157 / 577149 / 577150) and settings 25 Text Location (drop-down: 576481 In
  Chat Bubble = 4 default, 576480 On Screen = 2, 576482 In Chat = 3) and 37 Text Duration (1-20 s, default 5).
  Server: StoryTools kinds + PropSettings + Props.DisplayText (players within 60 m). Verified with dev propshowtext
  + Frida (modes 4, 2, 3 reached the handler; bubble seen over the sign).
- TextEditWidget's On Screen / Window radio goes to Lua Prop.SetTextPropData(guid, text, mode), but the native
  (FUN_143549450) sends only {u64 guid, string text} (0x13): the 0.589 client never sends the mode.
- Open: what sends mode 1 (window) — likely clicking a text prop to read it; not built (no evidence of the trigger).

## Prop names (built 2026-10-13)

- PlayerUpdate 0x7C UpdatePropName {u64 prop, string name} (one-byte sub, enum 0x26007C; reader FUN_142bb4650,
  dispatcher case 0x7b → FUN_142cb7000 / FUN_142e26420): sent to the landscape on rename (PropBase 0x19) and after
  the spawn of a prop with a custom name. Verified with dev propname + Frida.
- 0x7B UpdatePropText: no case in the 0.589 dispatcher (dropped). 0x7D UpdateEditProperties {u64, 2 records}
  refreshes only the prop the receiver is editing (FUN_142be7b20 compares with the prop tool's current prop);
  settings already update through 0xFD — not built.
- PlayerUpdate_Dispatch reads a ONE-BYTE sub for every message; enum values 0x26xx00 put an extra 00 on the wire.
  All PlayerUpdate sends now use 3-byte values (checked 2026-10-13).
