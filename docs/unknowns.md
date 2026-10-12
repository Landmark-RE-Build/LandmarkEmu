# Unknown values: status

Triage of everything the server sends that was copied from a live capture or left unnamed. Per-character data copied
from the live player is wrong for our characters and gets replaced; global settings captured from the live server are
correct as they are and only need naming. Tools: re/tools/packetids.py (client packet registry), coverage.py, pkt.py,
fieldmap.py.

## Captured bodies sent as-is

| Message | Status | Notes |
|---|---|---|
| ResourcesBase sub 3 / sub 1 | **removed** | named the live character (guid 0x4B561BAE72200691); client applies only to that entity (FUN_143568b00/FUN_143568e40). CharacterResources sends the real pools (sub 2). |
| EquipmentBase sub 1 | **removed** | live character's guid; FUN_142da4b10 applies only to the local player. |
| PlayerUpdate EffectTagCompositeEffectsEnable | **fixed** | was 4-byte opcode + 3 captured bytes, dropped by the reader (FUN_142bb2520: one bool). Now `{true}`. |
| ArtData TintDefinitionManagerData | **added** | was never sent; the client builds tint pickers and character-create/barbershop options on it. |
| ExperienceBase sub 2 SetExperienceRanks | unused | Experience.txt is empty and no UI reads experience; harmless. |
| SkillBase (693 B) | correct as captured | skill lines at level 0/1, the same for any new character; replace when skills exist. |
| LoadoutsBase (220 B) | correct as captured | "Default Loadout"; no character-specific values. |
| ClientSettings (110 B) | unused | help/shop URLs (planetside2.com) are stored at DAT_145351c50 but no reachable binding reads them; help uses local HelpPages, the store uses ClientConfig MarketplaceUrl. |
| ClientGameSettings, all fields (re-checked 2026-10-12) | traced | Dispatcher case 0x3b: +0x10 → game +0x326cc, +0x18 → +0x326d4, +0x20: written, never read. +0x14 = 10 → +0x326d0 default interaction range. bool +0x1c = 0 → +0x326d8: an optional movement check (FUN_142dfab30), off live. +0x24 = 0 → DAT_1452104c4 and +0x28 = 1 → DAT_145210558: two lookup switches. At 0 the client looks the id up in the player's own list; at 1 (the second one, live) it uses the list's single shared entry (FUN_142dc0950, FUN_143654570). +0x2c → DAT_14551ce34: written only. +0x30 = 12.0 / +0x34 = 110.0 → +0x326dc / +0x326e0 (getters FUN_142c33cb0 / FUN_142c33c90): the character controller's impact threshold and scale (FUN_142dd0960; impacts above it start the collision report). All values are the live server's. |
| ZoneCoordinateInfo 0x12 (80 B) | traced, matters | 3 × f64 zone origin in global decimetres (25584000, 20000, 25564500), 3 × f32 zone size (80000, 40000, 80000), 3 × u32 (0), local bounds −4000/−2000/−4000 … 4000/2000/4000 (f32 vec4 × 2). The server's own position → cell conversion (TerrainStore.ZoneOrigin) uses the same origin, so cells, edits and veins line up on every landscape (verified with the cell-request and vein probes). Whether the live game gave other landscapes other origins is unknown; ours are consistent. |
| InitializationParameters 0x9F ("live") | traced, unused | FUN_142c42db0 copies the string into a local that nothing reads, then only sets the image path. |
| WorldDisplayInfo 0x59 (u32 0) | traced, harmless | Stored at game +0x32500, read only as the "Player GUID" field of the client's crash-report metadata. |
| QuickChat 0x46 sub 1 SendData (empty list) | traced, matches live | The server supplies the quick-chat phrase list; the live server sent this same empty list. Quick-chat key bindings are shared-engine leftovers. |
| ActivityManager 0x8F (5 B), AreaDefinitionSetDefinitions 0xEA/1 | traced | AreaDefinition: header {u32 sub, u64, bool} + area list (u32 0) is read exactly and sets the "area definitions loaded" flag the re-zone wait needs. ActivityManager: an empty list as live. |
| "LoadWelcomeScreen" 0x60 (17 B) | **ignored by 0.589** | 0x60 is outside the dispatcher's table (DAT_142c874b4 → default case) and none of the fallback listeners (content manager, warp network, defaults) accept it (probe secdispatch.js: returns 0). To be removed. |
| Inventory 0x2D sub 0x18 (bool 1) | traced, matters | GrapplingHookEquipped: true turns on the client's grappling hook (FUN_142e21a00). |
| ClientUpdate MovementVersion (u8 1) | as live | |

## Opcode encoding

* PlayerUpdate (0x26) subs are one byte; ClientUpdate (0x29) and Command (0x1E) subs are u16.
* Bases above 0xFF are u16 little-endian on the wire: the server enum spells them as the wire bytes (KnownProp 0x115 →
  0x1501). DebugDraw (0x100, wire "00 01") cannot be expressed (leading zero byte dropped); unused.
* Fixed: SetPropTint, EffectTagCompositeEffectsEnable (4-byte → 3-byte); KnownProp, Tool, Interaction,
  DesignBasedStateMachine bases; CombatResourceEvent 0x67; CharacterCustomization 0x11C.

## SendSelfToClient

See sendself.md. Named this round: KnownPropIds (was Unknown40), CustomizationKnowledge (was Unknown41).

Every block now has a client owner (ClientPcData constructor/destructor vtables). Renamed: CustomizationKnowledge4
(Unknown42), FirstTimeEventsSeen (Unknown46), IgnoredIds47 (dropped by 0.589), SignaturePhases (Unknown51),
IntArray118/IntArray130/BoolArray148 (Unknown53-55), AcquireTimers (Unknown56); the rest are commented with their
client class. Still unknown in purpose: the int→int map at +0x2b0 and the plain field at +0x114 (offsets too common to
search; we send them empty / 0). +0x75dd0 (checked 2026-10-12): the reader FUN_142ba3b90 writes +0x75dd0..+0x75de3
only, and nothing else reads those offsets (only the constructor clears them), so the block is unused. It stops before
+0x75de8, a pointer FUN_142f33fd0 fills lazily on the same object, so there is no overlap.

Result: SendFirstTimeEvents was false, so the client's first-time help pages never ran. Now implemented
(Game/FirstTimeEvents.cs, FirstTimeEventBase 0x6D), verified in the test client: the Inventory help page opens once,
and the seen set survives a relog.

## Item records (InventoryItem)

Reader FUN_142b87110 fills a `ClientItem`. The captured live login packet (parsed with the server's model, all items
read exactly) shows what the live server sent; compared field by field with the item definitions:

| ClientItem | Field | Live value | Server now |
|---|---|---|---|
| +0x8 / +0xc | definition id / tint | id / 0 | same |
| +0x10 | ItemRandom block: quality, 4 × u32, stats (ItemRandom::StatInfo), catalysts (CatalystRandomInfo) | quality = the definition's quality (every item) | quality from the definition |
| +0x28 stats | {stat, text -1, f32 value, 1, true, value, 1, 1} | Adventurer's Pick/Axe: 3 = 100, 13 = 50, 12 = 80; armour 18-21; weapons 1, 10, 22 | gathering tools: area = dig radius × 10, the tool's speed, damage 80 |
| +0x68 | second ItemRandom block | empty except one weapon | empty |
| +0xc0 | f32 cost multiplier | RefItemQuality.txt COST_MULTIPLIER of the quality (2, 3, 4, 5) | same |
| +0x128 | optional {u64, string, string} | Player Design item: {design id, "1 - EW Section", "Zhang"} | design id, name, author (DesignItems.ItemBlock) |
| +0x120 | optional {u64, u32, u32} | not seen | not sent |
| +0xd4, +0xd8, +0xdc, +0x100, +0x14c | | 0 on every item | 0 |

Item tooltips come from the server: hovering sends Inventory 0x0F/0x10/0x11 tooltip requests and the client shows
Inventory 0x12 ItemTooltipReply (reader FUN_142e784e0; record FUN_142e78e10; filled into Tooltips::ItemTooltipInfo by
vtable slot 7, FUN_1436a3a80). Record: 5 u32 (name, description, image, 0, definition id), three int lists, a list,
the definition blob, …, a stat list at +0xc0. Found with a hook (re/test/scenarios/adhoc/tiphook.js) and fixed:

* The third int list (tooltip +0x1b0) holds the categories the "Categories" line names and the display category that
  decides which stat lines show (FUN_1436a5ac0). We sent it empty ("There are no categories"); it now carries the
  definition's categories (the pick shows "Pick" and its icon).
* Stat entries (FUN_142e78ba0, printed by FUN_143466d50): {u32 stat, u32 name text, f32 value, f32, f32 base value};
  value != base shows in the upgraded colour. Name texts from CalculatedStatDefinitions.txt NAME_ID (Game/StatNames.cs).
  Gathering tools now show Harvesting Damage / Speed / Size (Adventurer's Pick: 80 / 50 / 100, as live).

The item record's own ItemRandom stat list (above) is separate; the tooltip doesn't read it.

Testing aid: ClientLua.RunWithArgs / dev `luas <Table.Method> args...` calls any client Lua function with string
arguments through the first-time event Script packet, e.g. `luas TooltipManager.ShowRollOverTooltip Inventory 3 <item
guid> 0 0 700 300 50 50` opens an item tooltip without the mouse.

## Switches Ghidra left undecoded (decoded 2026-10-12)

Five packet functions had "Could not recover jumptable". `re/tools/decodeswitch.py` reads their jump tables from the
exe with capstone and lists each case's target and the functions it calls; every case leads to an ordinary,
decompiled function. Only the sub-opcode → handler mapping was missing:

| Function | System | Sub-opcodes handled | Notes |
|---|---|---|---|
| FUN_143310250 | Encounters (0x2C; i16 sub after the u16 base, header reader FUN_142b7dda0) | 2, 3, 4, 5, 6, 0x66, 0x6a, 0x6b, 0x72 (EncounterDetailsResponse, ctor FUN_1432eb8d0), 0x78, 0x79, 0x82 | Reached from the game's 0x2C handler FUN_142c428b0 (PlayClient vtable +0x2b0), which first takes 0x7D (death window, FUN_142cbca40) and 0x7E (`RespawnHandler:Hide`) itself. No encounter content in 0.589. |
| FUN_14326a960 | Mini-games | 0xd, 0xe, 0x12, 0x15, 0x25, 0x29, 0x2a, 0x33 | Shared-engine system; no 0.589 UI. |
| FUN_14330d470 | Admin commands (AdminCommandPacketClientTime, …) | 0-6 | Inline handlers. |
| FUN_142eb5a20 | Skill admin | 0-0xe → FUN_142eb5d50, 6510, 6370, 5c30, 7100, 6ca0, 6fe0, 5f90, 60b0, 5e20, 6230, 68f0, 6af0, 6e50, 6760 | Admin tooling for skill lines. |
| FUN_1431bb890 | (not a switch) | | A virtual tail call `jmp [rax+0x28]` after a list-node removal in the purchase code; Ghidra took it for a jump table. |
