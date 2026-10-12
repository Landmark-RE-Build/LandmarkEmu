# System: building — voxels, templates / designs, world editing tools, palette, props, story tools

Status: [~] map level; built parts in docs/terrain.md, docs/gameplay.md, docs/story-tools.md.

## Terrain cells (0xEC; docs/terrain.md) ✔ cell requests / updates / manifests / block damage / veins / timeouts.
✗ C→S ResetAllCells 0x8, ResetTemporaryCells (0x9 family); ✗ S→C ClientCellUpdateResponse 0x3, MaterialChangeInfo
0x6 (check what the client does with them).

## Voxel construction (0xF4) ✔ AddTemporaryBlock, UndoRedo. Server never sends: ApplyDiffRequest 0x1,
ClientSelectionAreaRequest 0x2, ClientOwnershipRequest / ResponseObserved 0x4, OffGridPasteClientRequest 0x5,
ClipboardData 0x6, ZoneUnknownPropList 0x8, ZoneMissingPropList 0x9, EnvOwnershipDataResponse 0x11,
ZoneDisplayNotification 0x15, EnvOwnershipUpToDateResponse 0x16 — read each handler: ownership (who may edit which
voxels), selection areas, off-grid paste, clipboard.

## World editing tools (0x10C) ✗ all C→S: Clear, BoxUpdate 0x1, SphereUpdate 0x2, ShapeUpdate 0x3, SelectionUpdate 0x4,
PreviewSetup 0x5, PreviewUpdate 0x6, FillUpdate 0x7 — never received in play; find which tools send them (selection /
fill / shape tools) and whether 0.589 uses them.

## Templates / designs (0xF0; Game/Templates.cs) ✔ voxel data, instance details, export to item, delete, rename,
save response, update. ✗ C→S ClientListRequest 0x1 (the design list request — we push the list instead: check the
client's own flow); ✗ S→C EnvironmentAddOwnershipResponse 0x11, EnvironmentInstanceDetailsResponse 0x13.

## Palette (0xF5) ✔ AddEntry / DeleteEntry / SlotUpdates; ✗ Create 0x1 / Delete 0x2 (palettes themselves).

## Tools (0x117) ✗ ToolPacketActivate 0x1 (received 115× in play — the tool switch; informational per protocol.md
§14.1), ToolPacketUndoStackChange (S→C). Data: ToolTypes, ToolDefinitions (axis / state), ModalToolBrush*.

## Props (0xED / 0xFD / 0x115) ✔ place, pick up, delete, duplicate, text, tint, name, enable, settings, interaction
state, known props. ✗ PropAccess 0x1, RequestList 0x9, UndoRedoNotification 0x10, PropProcessorStateReplication 0xD
(HudStateManager:OnOpenTextEdit), KnownProp CreatePropItemRequest 0x2. PlayerUpdate prop subs 0x76 / 0x79-0x7D
(template info, prop text display, tint, name, edit properties) ✗. Data: PropDefs, PropCategory*, PropTag*,
PropRecipes (128), PropTintGroupMappings, PlacementPack* ...

## Story tools / object scripting (0x111; Game/StoryTools.cs) ✔ requests; S→C status (check). Instanced games (0x10D):
✔ join / leave / observer / state / scoreboard; ✗ EndRequest 0x10, StartRequest 0xF, PropRecipeRequest 0x5, PropRequest
0xA, PropRemove 0xB, SetConfiguration 0xE; S→C PropState, PropInteraction, PlayerRemoval, TeamMembers,
UpdatePlayerFlags, AdminMonitor, PropRecipe, Event, HUD, GetConfigurationResponse.

## Claims → systems/claims.md. Content management (0x1B / 0x118) ✗ — content packages / sockets (dev tool).

## Live behaviour (wiki, launch 2016 = 0.589 era: Build Mode, Designs, Prop, Prop Palette, Free-to-Use Building Materials, Tint)

Pages: https://landmark.fandom.com/wiki/Build_Mode , /Designs , /Prop , /Prop_Palette , /Free-to-Use_Building_Materials
, /Tint (dump: design/wiki/pages; print with `python re/tools/wiki.py "<title>"`).
- **Build Mode** (Tab or tool icon): one mega-palette with tabs (Designs, Props, Tints, Building Tools + Shapes +
  Materials Tray) or modular pop-out windows; search per tab; "Can Use" / "Can Build" filters. (Mesh Shapes tab came
  late July 2016, after 0.589.)
- **Free-to-use materials:** over 100 terrain textures (dirt, common stone, sand, snow, ice, plus 6 grey slates, 1 grey
  masonry, 1 carved stone) are unlimited (∞ in the tray), never consumed. Every other material must be gathered and
  refined at a Replicator and is consumed from the inventory when building (count shown on its icon); deleting
  returns it. Ctrl+Z undo.
- **Props** come from the palette, not the inventory: the player needs the prop's "recipe" (innate list, or discovered
  while gathering/adventuring — loot shows "recipe discovered" — or bought with Lumens at the Lumen Station) and the
  refined materials; the icon shows how many can be placed with the materials on hand. Placing consumes the materials
  (e.g. a bed 100 wood); **Recycle** returns them all ("no loss"). Right-click menu: Move, Fine Tuning (nudge /
  spin), Duplicate (same scale and tint), Recycle. R held = scale. Only on a site you own or have access to.
  Exceptions placed from the inventory: promotional / event items (Iron Portilicus, Year Zero Trophy …).
  The Replicator itself costs nothing so anyone with a site can place one. Creature props (Story Tools) can be set to
  attack visitors. Props attach by category (ceiling / wall / ground lights).
- **Tints:** pick a tint, click a placed prop; "no" tint removes it; nature props can't be tinted.
- **Designs** (formerly templates; stored server-side only): library (L) sorted by creator / date / voxels; tabs
  Starter Castle (free, for the first Journal goal "Your Home is Your Castle"), Player Studio (bought), Personal,
  Collected (discovered / traded / mailed). Create: selection tool → "+" → up to 4 screenshots → name → Create.
  Placing needs the materials (free terrain textures excepted; clicking sound if lacking; right-click shows the
  material list); props inside a design are placed only if their materials are on hand, else skipped; red preview =
  outside the site. Ctrl+V repeats the last design; Ctrl+Z undoes a placement (materials back).
  **Storage limit** 6,000,000 voxels, +6,000,000 per 1040 Lumens at the Lumen Station, max 48,000,000; Starter Castle
  and bought designs don't count; auto-designs of deleted / expired sites may exceed it (but block new saves).
  **Send to Inventory** turns a design into an item (tradeable, mailable, storable in chests). Copying on someone
  else's site needs the "Get Inspired" (copy) permission.
- **Block ownership:** every voxel placed or altered records its player; Player Studio sales of designs with several
  owners split revenue by share; **Voxel Vision** (site info window) colours blocks by builder.
- **Building tools** (wiki Building Tools / Selection Tool): only usable on own sites or sites where the owner granted
  build access. Add (scales from 1 block; clicking sound when the material is short; with a selection fills the whole
  volume), Delete (natural land or placed material; whole selection), **Heal** (restores the site's original
  landscape *and air* inside the selection or brush — server must keep the original terrain to heal to), Smooth,
  Paint (replace material/texture; needs the new material in inventory), Fill (fills only the empty space inside a
  selection), Line (slopes / beams; Y cuts angles; green = smooth line), Area (sets a trigger area for Linking and
  Triggering, e.g. trapdoor opens when stepped on), Eyedropper (Alt samples a material). C toggles cube / sphere,
  wheel scales, T fine tuning, Esc releases the tool. **Ctrl+Z undo up to 20 steps**, Ctrl+Y redo (props too).
  Ctrl+C / Ctrl+V copy-paste a selection; pasted props appear only if the player has them (materials) in inventory.
- Special voxel types players made: micro voxels, anti voxel, zero-volume voxels (wiki pages Micro-Voxel, Anti Voxel,
  Zero Volume Voxel) — client geometry tricks, the server only stores what the client sends.

## Client constants (trace/client-constants.md)
Prop.MaxScale 3.0; Claims.PropLimit 20000 per site; Props.OnSpawnPropAnimation 801; prop destruction debris
(life 10 s); TextProp on-screen 10 s, font 18; GameScripting UI 10 entries / page, PropNpcDefinitionId 1590;
InstancedGame.ClaimBoundaryBufferSize 5. Template value: VoxelsToSC 100, PropsToSC 1 (Player Studio pricing?).
