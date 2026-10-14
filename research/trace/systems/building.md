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

## World editing tools (0x10C) ✔ relayed to nearby players (Game/WorldEditing.cs, 2026-10-12): Clear, BoxUpdate 0x1, SphereUpdate 0x2, ShapeUpdate 0x3, SelectionUpdate 0x4,
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

## ApplyDiffRequest in full (traced 2026-10-13, decoded from live edits)
Writer FUN_1430b0510 (ClientConstructionManager DAT_1453747d8, request at +0x38, sent by FUN_1430bab80):
u8 sub 1, u64 +0x20, u64 +0x28, u64 +0x30 (all 0 seen), cell list (u32 n, n × {u64 key, u64 cell, u32 type,
u32 length + blob, [u32 for types 2/3]}), u64 +0x70 the cell the tool aimed at, i32×3 +0x78 brush voxel position,
i32×3 +0x84 brush size, materials +0x90 (u32 n, n × {u16 material, u32 count}), u64 +0xc0 build site
(FUN_1430b7610), versions +0xc8 (u32 n, n × {u64 cell, u32 base layer-1 version, u32 new layer-1 version}),
u32 +0xe0, f32×3 +0xe4, u32 n × {u32, vec3, vec3} +0xf0, u8 +0x108.
- The new version is the client's CRC32 of its compressed layer 1; the server adopts it (client and server agree,
  caches valid across logins); a base version ≠ the server's means a stale edit → not applied, cell re-sent.
- Response (sub 7) {u32 request type, u32 result, bool}: result 0 → FUN_1430bc0d0 (site HUD refresh); non-zero →
  message (FUN_1430b6560). The client does not wait on it.
- Edits are gated by the engine busy list (terrain.md); an undo (VoxelClipboard.Undo) leaves no tool selected.

## ResponseObserved (0xF4 sub 0xA), traced 2026-10-13

- Reader FUN_1430b11e0: u16 opcode, u8 sub, u32 operation (+0x18), FUN_1430b2280 two double×4 (+0x20 min, +0x40 max),
  u32×3 (+0x60), bool (+0x6c).
- Handler FUN_1430bbd60: operation → brush type for the effect: 0/1/2 → 11 FillStamp (4 Stamp if bool), 3 → 2
  Subtractive, 4 → 1 Healing, 5 → 0 Smoothing, 6 → 5 Paint (brush types: gameData/ModalToolBrushDefinitions.txt).
  Bounds − scene origin (FUN_143ece1c0) × 0.1 (DAT_144b72e90) → metres. Op 1 or a non-cubic box → box effect
  FUN_142fe6bf0 with direction (0,1,0); otherwise shape effect FUN_142fe6610 ("WorldEditingEffect.Shape<Kind>_%d").
- The 3 u32 are the placement's direction (FUN_142fdfff0 uses them as an axis); the bool selects ShapePlacement
  (replacing) over ShapePlacementNoDestroy.
- The client's own edits play the same effects locally (brush apply FUN_1436cff90, FUN_14362aaa0/ac30), so only other
  players need the packet.
- ApplyDiffRequest +0xe0 = operation kind set in DAT_1453747d8+0x118 (request object is that global + 0x38):
  1/2 FUN_1430bb0e0 (box/stamp placement; 2 with an aimed cell), 3 FUN_1430ba210 (sculpting brushes, mode from brush
  +0x144), 4 FUN_1430ba4d0, 5 FUN_1430bae50, 6 FUN_1430b9560, 7 FUN_1430b6bd0, 8 FUN_1430ba330 (part_0113 caller),
  9 FUN_1430b5660. The brush itself isn't sent.
- Server: Building.Observe after every applied edit (and Heal) → players within 100 m except the builder; op from
  the kind (1/2) or from the before/after values. Verified 2026-10-13 with dev `observed <op> [m]` + Frida
  (re/test/scenarios/adhoc/observed.*): ops 0–6 → effects 4,4(box),4,2,1,0,5 with the sent bounds.

## Response results and undo (traced 2026-10-13)

- Response (0xF4 sub 7) `{u32 request type, u32 result, bool}`: result 0 → FUN_1430bc0d0 (refreshes the open Voxel
  Vision ownership view); else FUN_1430b6560 messages: 0xC InsufficentMaterials, 0xD InsufficentProps, 0xE
  InsufficentPermissions, 0x11 ExceedsMaxTotalTemplateVoxelsCount (+ Voxel Storage Expanders offer), 0x1A
  OnlyClaimOwnerMayPlaceOrCopyTemplateInstances, other GeneralError. Types 0xF and 2 with an error also call
  FUN_1430a7330 → FUN_143ad63b0. ResponseObserved also ends in FUN_1430bc0d0.
- ClientUndoRedoRequest (sub 3) `{u32 0 undo (FUN_1430bc070) / 1 redo (FUN_1430ba020)}`: the client changes nothing
  itself. Server now answers Response(3, 0) on success (was silent), then UndoStackChange.
- UndoStackChange (0x117 sub 2, reader FUN_1433b4760, handler FUN_1433ba050): u32 undo, u32 redo (stored at manager
  +0xa0/+0xa4), list<string> → Tool.UndoStack. Server matches. It doesn't deselect the tool.

## Designs (TemplateBase 0xF0), audit 2026-10-13

- Client dispatcher FUN_1430624b0 switches on sub−2: 2 ListResponse, 3 SaveTemplateResponse, 5 VoxelDataResponse,
  7 DeleteTemplateResponse, 0xB rename response, 0xC UpdateTemplateResponse, 0x11 AddOwnershipResponse, 0x13
  InstanceDetailsResponse. (trace/families/00F0_Template.md's sub numbers are 2 too high; server numbers are right.)
- Storage limit is VOXELS, not designs: Lua Character.Template.Limits {CurrentVoxels = manager +0x150, MaxVoxels =
  FUN_143061750}. MaxVoxels = min(Templates.MaxTotalStoredVoxelCount 6,000,000 (hash 0x771cc5d4) + Σ expander items
  (item def +0x238 × count, items from player +0x753d8 list 10), Templates.MaxVoxelExpandable 48,000,000).
  CurrentVoxels sums the material counts of designs with IsDesignerTemplate = 0, IsOnMarketplace = 0, not a contest
  winner and flag +0xf8 (CountsAgainstStorageLimit). Server had "100 designs per 6M" → now voxels
  (Templates.MaxVoxels / UsedVoxels; save over the limit → Response(2, 0x11)). Open: confirm the client's expander
  list (+0x753d8 kind 10) agrees with the server's DesignStorage unlocks.
- Not built: template INSTANCES. Ownership grids carry a u16/i64 instance list (written empty by the server); the
  client asks GetInstanceDetailsRequest (0x12 {u64 instance, u64 DAT_145372ff8}) for each and expects 0x13
  {instance data (FUN_143058570: 3 × u64, ?, u32, u64, string, bool), TemplateBasicData (FUN_143058180), u64 list}.
  Placing a design would create an instance (who placed which design where); 0x1A guards copying them.

## Props in copies and designs (traced 2026-10-13) — to build in the Props module

- PropData (TemplateCommon::PropData, 0xC8 bytes; reader FUN_142f09280, writer FUN_1430afea0), wire order:
  u64 +0x00 (guid), u32 +0x08 (name/model id → preview +0x2bc), u32 +0x0C prop definition id (FUN_143accdc0 on
  DAT_145351c48+0x31f30: +0x38 model, +0x60 scale), f32×3 +0x10 position offset in metres from the paste origin,
  f32×4 +0x1C rotation quaternion, f32×3 +0x2C scale, list n×16 (+0x50), u64 +0x38, u64 +0x40, u32 +0x48,
  list n×20 (+0x68), u32 +0x80 tint (FUN_143bd9fc0 on DAT_14535bed8), string +0x88, string +0xA0, u32×3 +0xB8.
  Field use from the paste preview FUN_1435d6050 (VoxelPasteCursor::GetVoxelPreviewMesh FUN_1436d8c70).
- The client never gathers props itself: copy (FUN_1430baa70) and save design (FUN_1430ba7b0) send none. Placing a
  design (FUN_1430b9d50, action 4) and pasting (FUN_1430b96a0, action 4) send the PropData list the SERVER gave it
  (design TemplateBasicData / ClipboardData). So live collected the placed props inside the box on copy / save,
  sent them back in the data, and placed them on paste — props the player lacks are listed in ZoneMissingPropList
  (sub 9 {Array<int> name ids, Array<int> counts} → chat "Construction.SkippedProps" + "%d : %s"), props they don't
  know in ZoneUnknownPropList (sub 8 {map int → {u32 name id, u32 count}} → "Construction.MissingPropKnowledge").
- Server today: props in the box are ignored on copy / save / paste (PropData lists are empty). Carried to module 3.

## Template instances — what they are (2026-10-13, to build)
- ClientTemplateManager keeps, per build site, the template INSTANCES placed on it (designs placed as a unit);
  PopulateClaimInstanceDataToOwnershipDS (FUN_1434892a0 / FUN_14348add0) fills Voxel Vision's data from them and
  asks GetInstanceDetailsRequest (0xF0 sub 0x12) for instances whose template it doesn't know. Ownership grid v4
  carries an optional i64 list (bool at +0xc, FUN_14307f3a0) and a u16 list (FUN_14307f2b0) before the voxel
  indices — the server writes both empty. Next: find the packet that fills the per-site instance map (+0x28) and
  the 0x13 reply's use, then record an instance per design placement.
- Correction: the instances are PURCHASED designs (Lua VoxelOwnership.PurchasedTemplate*; FUN_1434892a0 lists the
  site's purchased templates for Voxel Vision). Build with Player Studio purchases (module 12).

## Fill / Line / Smooth shapes (checked 2026-10-14)
- Reports: Fill sphere stays blocky, 2-voxel lines break into pieces, smoothing stays stepped — same before and after a
  relog. Server-side check (dev `cellcompare on`, Building.CompareWithClient → TerrainEdits.Compare) with sphere edits
  in the real client: after every edit the only differences were voxels the server holds that the sent cell lacks
  (client 0 / server 2-3: earlier edits) — an ApplyDiff cell carries the edit, not the whole layer, and the server keeps
  all of it (vertex-only voxels included since e187acc). The shapes are the 0.589 client's own; the official forum had
  "[Feedback] Fill Cursor - Sphere application" (AugurDev, 3 Aug 2016, after this build).
- Server-side causes fixed the same day: vertex-only voxels were dropped (e187acc); build cost 1 item per voxel
  (717df47); tray counts are the client's own (ITEM_ID); material tray listeners are lost on relog (client bug, see below).
- Material tray after a relog (client bug, traced): MaterialPaletteEntry registers its cost item with the player
  inventory (FUN_1436f3ed0 → FUN_142e7e970, table +0x258/+0x278, notifier FUN_142e83480) only when the Materials
  collection is built (FUN_1433bc830, once per game session from FUN_142c195f0); a relog replaces the inventory, so
  counts stop updating until a full restart. No packet or Lua binding rebuilds collection 2 (re-checked 2026-10-14:
  FUN_1434215f0(manager, n) repopulates a collection; packet handlers call it for collections 1, 3, 4 and 5, only
  FUN_1433bc830 for 2 and 6).
- Hotbar hidden after a relog from build mode: fixed (87429ea) — server runs HudStateManager:OnDisableBuildMode once
  per login (testclient/Resources/Scripts/hudstate.lua).
