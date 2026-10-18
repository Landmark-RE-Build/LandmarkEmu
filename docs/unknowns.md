# Unknown values: how the fields were named

**Status (2026-10-18).** No `Unknown*` identifier is left in the server's code: every packet model field, record and
list that was once called `Unknown<n>` has a name taken from the client code that reads it, or, where nothing in the
client reads it, a name by its slot (`Unused<offset>`, `Slot<offset>`, `Field<offset>`) marked "never read [P]" in the
model's comments. This page is the record of how that was done, family by family, with the old and new names, and of
the values that changed on the way. What is still open:

- slots whose client reader was not found (ClientBeginZoning `SlotA0` / `SlotB4`, AreaDefinition / AreaShapeDefinition
  slots, the equipment record's strings, a few achievement / objective / reward-bundle slots; listed in each section);
- values only live's servers had (other landscapes' zone details, the server list's per-world claim count);
- the bool after each SendSelf item record (live 1 on two items) and `CastKeySeed`, which the server does not check.

The original triage follows. Per-character data copied from the live player is wrong for our characters and gets
replaced; global settings captured from the live server are correct as they are and only need naming. The packet
registry and field maps are in `research/tables/` (`packet_ids.tsv`, `coverage.md`, `fieldmap_SendSelfToClient_Read.md`).

## Captured bodies sent as-is

| Message | Status | Notes |
|---|---|---|
| ResourcesBase sub 3 / sub 1 | **removed** | named the live character (guid 0x4B561BAE72200691); client applies only to that entity (FUN_143568b00/FUN_143568e40). CharacterResources sends the real pools (sub 2). |
| EquipmentBase sub 1 | **removed** | live character's guid; FUN_142da4b10 applies only to the local player. |
| PlayerUpdate EffectTagCompositeEffectsEnable | **fixed** | was 4-byte opcode + 3 captured bytes, dropped by the reader (FUN_142bb2520: one bool). Now `{true}`. |
| ArtData TintDefinitionManagerData | **added**, every field named (2026-10-18) | was never sent; the client builds tint pickers and character-create/barbershop options on it. Field names: see "Login and auth" below. |
| ExperienceBase sub 2 SetExperienceRanks | unused | Experience.txt is empty and no UI reads experience; harmless. |
| SkillBase (693 B) | correct as captured | skill lines at level 0/1, the same for any new character; replace when skills exist. |
| LoadoutsBase (220 B) | correct as captured | "Default Loadout"; no character-specific values. |
| ClientSettings (110 B) | unused | help/shop URLs (planetside2.com) are stored at DAT_145351c50 but no reachable binding reads them; help uses local HelpPages, the store uses ClientConfig MarketplaceUrl. |
| ZoneCoordinateInfo 0x12 (80 B) | traced, matters | 3 × f64 zone origin in global decimetres (25584000, 20000, 25564500), 3 × f32 zone size (80000, 40000, 80000), 3 × u32 (0), local bounds −4000/−2000/−4000 … 4000/2000/4000 (f32 vec4 × 2). The server's own position → cell conversion (TerrainStore.ZoneOrigin) uses the same origin, so cells, edits and veins line up on every landscape (verified with the cell-request and vein probes). Whether the live game gave other landscapes other origins is unknown; ours are consistent. |
| InitializationParameters 0x9F ("live") | traced, unused | FUN_142c42db0 copies the string into a local that nothing reads, then only sets the image path. |
| WorldDisplayInfo 0x59 (u32 0) | traced, harmless | Stored at game +0x32500, read only as the "Player GUID" field of the client's crash-report metadata. |
| QuickChat 0x46 sub 1 SendData (empty list) | traced, matches live | The server supplies the quick-chat phrase list; the live server sent this same empty list. Quick-chat key bindings are shared-engine leftovers. |
| ActivityManager 0x8F (5 B), AreaDefinitionSetDefinitions 0xEA/1 | traced | AreaDefinition: header {u32 sub, u64, bool} + area list (u32 0) is read exactly and sets the "area definitions loaded" flag the re-zone wait needs. ActivityManager: an empty list as live. |
| "LoadWelcomeScreen" 0x60 (17 B) | **removed** | 0x60 is outside the dispatcher's table (DAT_142c874b4 → default case) and none of the fallback listeners (content manager, warp network, defaults) accept it (a Frida probe of the dispatcher returned 0). No longer sent (Network/Handlers/MiscHandler.cs). |
| Inventory 0x2D sub 0x18 (bool 1) | traced, matters | GrapplingHookEquipped: true turns on the client's grappling hook (FUN_142e21a00). |
| ClientUpdate MovementVersion (u8 1) | as live | |

## Packet model fields (2026-10-18)

Every field once called `Unknown*` in the world server's packet models is named now (the models owned elsewhere
aside: SendSelfToClient, Achievement, Objective, RewardBundle, InventoryItem, Mount). Names come from the client's own
strings where it has them (ContinentDefinition's loaders, the item printer, "WaitForZoneReadyPacket", RTTI
ClientRequirementIsGM, the AreaDefinition / AreaShapeDefinition table loaders); the others are named by what reads them
([I] in the model comments). ClientGameSettings, SendZoneDetails, ClientBeginZoning and the continent block are in
protocol.md §3.0. Still open:

| Field | What is known | Not found yet |
|---|---|---|
| ClientBeginZoning +0xa0 (f32, `SlotA0`) | ActorManager vtable +0x90 → +0x6ce678 = max(v, 1500), 1500 = constructor default | the caller of the getter (vtable +0x88, FUN_1430eca00): searched every function naming the ActorManager (game +0x34098 / DAT_1454311d8) |
| ClientBeginZoning +0xb4 (bool, `SlotB4`) | ActorManager vtable +0x78 → +0x6ce68c; cleared when an encounter ends (FUN_142ca6d90, FUN_142ca8b50, FUN_142d06480) | the caller of the getter (vtable +0x80, FUN_1430ef390); same search |
| AreaDefinition +0x35 / +0x38, AreaShapeDefinition +0x5c | only the readers, writers, constructors and copies touch them in the area code | a reader outside the scanned ranges (offsets too common for a whole-client search) |
| SendZoneDetails / ContinentDefinition for landscapes other than 406 | the live values are 406's capture | ContinentVersion, MaxClaims, OceanHeight, TeleportImageId, cave density … of the other landscapes (no capture, no client table) |
| ItemUpdate +0x28 `RentalDuration` | with the rental expiration it marks a rental (never stacked) | the name: the item printer does not print +0xd8 |

Values changed with the names: ClientBeginZoning's movement version and TeleportToLocation +0x40 are now 1 (were 0;
the client copies the byte into its movement bodies, and other clients drop non-full updates of another version),
ClientBeginZoning sends the world name text (was 0: "Unnamed World" until the zone details), SendZoneDetails sends
IS_NEWBIE_ZONE of the landscape (the Pioneer lands: true), AddLightweightPc sends the player's current profile id
(was 0).

## Opcode encoding

* PlayerUpdate (0x26) subs are u32 on the 0.695 wire (one byte in 0.589; protocol.md §1.6); ClientUpdate (0x29) and
  Command (0x1E) subs are u16.
* Bases above 0xFF are u16 little-endian on the wire: the server enum spells them as the wire bytes (KnownProp 0x115 →
  0x1501). DebugDraw (0x100, wire "00 01") cannot be expressed (leading zero byte dropped); unused.
* Fixed: SetPropTint, EffectTagCompositeEffectsEnable (4-byte → 3-byte); KnownProp, Tool, Interaction,
  DesignBasedStateMachine bases; CombatResourceEvent 0x67; CharacterCustomization 0x11C.

## SendSelfToClient

See sendself.md. Named this round: KnownPropIds (was Unknown40), CustomizationKnowledge (was Unknown41).

2026-10-18, the last numbered fields (sendself.md "Remaining fields named 2026-10-18"): LastPlayedProfileByType (Unknown30), AbilityCooldowns (Unknown31), CastKeySeed (Unknown32), CurrentAdventureId (Unknown52) and the vehicle slots OwnedVehicleGuid / Unused244a8 / OccupiedVehicleGuid / Unused24618 / Unused24620 / OccupiedVehicleSeat (Tail24490*); the capture-only Unknown69-74 / Unknown77 are gone (ReadCapture reads the 0.695 layout). Values changed: OccupiedVehicleSeat 0 -> 0xFFFFFFFF and LastPlayedProfileByType (current profile) -> empty, both as live. No Unknown* left in SendSelfToClient.cs.

2026-10-18 (0.695 readers; sendself.md "Records named from the 0.695 readers"): no Unknown* left in CharacterIdentity
(3: Gender, UnusedA4, Honor), Profile (33), Buff (26, and the layout lost a stray u64), Collection (7, and the entry
list it lacked), the recipe block (KnownRecipeIds, Cooldowns = `PlayerRecipeData` (was StructUnknown142C1B810:
RecipeKey, RecipeId, CooldownStart), Recipe RecipeType / SortOrder / IconTintId / CastingTime / Unused38 / StackSize /
CooldownTotal / Categories / MaxQueue, component ComponentType / IconTintId / DescriptionId), Loadout (LoadoutId,
Record) and Acquaintance (Unused20). Values changed: the profile floats and CHAR_SIZE / FEET / ARMOR now go in the
client's order (live's); the recipe records carry CASTING_TIME, STACK_SIZE and COOLDOWN_TIME, and components their
DESCRIPTION_ID, as live did.

Every block now has a client owner (ClientPcData constructor/destructor vtables). Renamed: CustomizationKnowledge4
(Unknown42), FirstTimeEventsSeen (Unknown46), IgnoredIds47 (dropped by 0.589), SignaturePhases (Unknown51),
IntArray118/IntArray130/BoolArray148 (Unknown53-55), AcquireTimers (Unknown56); the rest are commented with their
client class. The plain field at +0x114 is `CurrentAdventureId` (2026-10-18: written only by PlayerUpdate 0x36 SetCurrentAdventure; sendself.md "Remaining fields named 2026-10-18"). +0x75dd0 (checked 2026-10-12): the
reader FUN_142ba3b90 writes +0x75dd0..+0x75de3 only, and nothing else reads those offsets (only the constructor clears
them), so the block is unused. It stops before +0x75de8, a pointer FUN_142f33fd0 fills lazily on the same object, so
there is no overlap.

2026-10-18, the UnknownStruct* classes named from the 0.695 readers (sendself.md "Manager blocks"): the int map at
+0x2b0 (`IntMap2b0`, never read — exe scans of ClientPcData's methods and of every player-global load), acquire timers,
currency discounts, the currency / experience rate tiers (+0x75dd0 is the currency rate tier: CurrencyBase 0xE5 sub 2
fills the same block; still never read), definition variables and int sets, equipment / experience records, the item
timer manager, the skill point manager and the inventory filters. Named by slot because no reader was found: the
acquire timer byte (+0x75d48, `Flags160`), experience record +8 / +0x10, skill point +0x771b8, and the equipment
record / slot strings (`String08`, `String48`, `String30`, `String70`; open lead). Values: NextFilterId 0 → 1 (live).
The live capture turned out to be the 0.695 layout to the last byte, including its tail.

Result: SendFirstTimeEvents was false, so the client's first-time help pages never ran. Now implemented
(Game/FirstTimeEvents.cs, FirstTimeEventBase 0x6D), verified in the client: the Inventory help page opens once,
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
| +0x120 | optional rental block {RentalExpirationTime, RentalUpsellsSent, MarketplaceBundleId} (names from the client's item print FUN_143b793d0) | not seen | not sent |
| +0xd4 | ConsumedCount (item print; clamped to the count, summed on stack merges) | 0 | 0 |
| +0xd8 | u32: > 0 blocks stacking like a rental (FUN_143b789b0 / FUN_143b78c10); no other reader | 0 | 0 |
| +0xdc | AutoResupply (client toggles it and sends Inventory 0x2D sub 0x0D, FUN_142f261b0; only the uncalled getter FUN_142f21ff0 reads it) | false | the saved switch (character_item / chest_item autoResupply) |
| +0x100 | u64 the inventory copy FUN_143b78d70 drops: never read | 0 | 0 |
| +0x148 | Wear (float bits) → the row's "Condition" (FUN_142f258f0) | 0 | the saved wear |
| +0x14c | Flags: 0x80 stacks of one, 0x40 no overflow warning (FUN_142f22d40 → FUN_142f1e580) | 0 | 0 |
| +0x130 | metadata: present byte, type, type 1 = ItemInstanceMetaCharacaterGuid {u64 guid, u32, u32} | absent | absent (the model now reads the body by type, as the client) |
| ItemRandom +0x4..+0x10 | +0x4 must match to stack (FUN_143b77f90); +0x8/+0xc/+0x10 never read | 0 | 0 |
| after the record (SendSelf list only) | bool → ClientItem +0x161 via vtable +0x38 (FUN_142e46670); no reader found | 1 on Lightstone 231 and Cozy Campfire 3894, 0 on the other 22 | 0 (open lead) |

Item tooltips come from the server: hovering sends Inventory 0x0F/0x10/0x11 tooltip requests and the client shows
Inventory 0x12 ItemTooltipReply (reader FUN_142e784e0; record FUN_142e78e10; filled into Tooltips::ItemTooltipInfo by
vtable slot 7, FUN_1436a3a80). Record: 5 u32 (name, description, image, 0, definition id), three int lists, a list,
the definition blob, …, a stat list at +0xc0. Found with a Frida hook on the tooltip reader and fixed:

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

## Goals: achievement, objective and reward bundle records (named 2026-10-18)

Every field of `Model/Shared/Achievement.cs`, `Objective.cs` and `RewardBundle.cs` is named from the 0.695 readers
(AchievementData FUN_142c161b0, ObjectiveData FUN_142c22ee0, ClientRewardBundle FUN_142c2f040) and the code that reads
the stored offsets: the Journal row data source FUN_1435553a0 ("QualityId", "TrophyId", "Points", "SubcategoryGuid",
"BackgroundImageId", "ForegroundImageId", "VisibilityType", "ActiveToday", "CanWatchAfterCompletion", "Dependency",
"RequiredAchievements", "ObjectivesItems", "Rewards"), the objective rows FUN_143556ff0, the sorters, the goal tracker
(FUN_14355bc20 "IsCollapsed"), AchievementAdd / Complete (hint texts, the Steam achievement, the completion composite
effect) and BaseClient::HandleRewardPacket (FUN_1431e2670: coins, experience). Fields only the reader, copy and writer
touch are named by slot (`Slot2270`, ...) with "never read [P]" and their live values. Both captures (71-AchievementBase,
SendSelf.bin with 865 achievements) parse to the last byte with the renamed models.

Values changed:

* Reward bundles: icon and name text default to 0xFFFFFFFF ("use the first entry's", getters FUN_143b85620 /
  FUN_143b856c0), as on all 1,946 empty live bundles; the encounter details' three empty bundles now use the model
  (they sent 0).
* The captured goal records carry the live character's guid (0x4B561BAE72200691) as the reward bundles' recipient
  (+0x20f0) on the 143 bundles with rewards; Achievements.WriteRecord now writes the receiving player's guid there.
* Achievement visibility type defaults to 4 (the client's constructor default and every live record).

No reader found yet (named by slot, live values in the models): achievement +0x226c, +0x2270, +0x2288, +0x228c, +0x2290
(live: the previous Getting Started goal), +0x22a2, +0x22a4, the IconData at +0x2250; objective +0x215c, +0x2174 and its
+0xc hint text (quests resolve it, achievements do not); reward bundle +0x78, +0x20a8, +0x20d0, +0x20d8, +0x20b0 ..
+0x20bc, +0x20e8, the recipient guid and the bundle id (+0x20ac, one number per live reward tier).

## Mounts (PacketMountInfo, named 2026-10-18)

`Model/Shared/Mount.cs` (reader FUN_142c23fe0): +0x0 mount id (manager key; Lua Mount.Spawn's auto-mount compares it,
FUN_142f0fce0) and +0x28 the mount actor's guid are read; +0x4, +0x8, +0xc, +0x10 string and +0x30 are only copied.
Landmark had no mounts; the list is sent empty, as live.

## Quests, palettes, missions (SendSelfToClient, named 2026-10-18)

Every field of `QuestData.Quest`, `QuestData.QuestObjective`, `PlayerPalette`, `PlayerPalette.PlayerPaletteEntry`,
`MissionInstanceManager.MissionInstance`, `MissionInstanceKey` and `MissionComponentDataKey` is named from the 0.695
code (sendself.md, "Quests, palettes and missions"). No values changed: the lists are sent empty, as live; the wire
layouts are the same (mission location is now a Vector4 of the same four 4-byte slots).

Never read [P] (named by offset): quest +0x10, +0x11, +0x18 (a guid by its default [I]), +0x20, +0x2238, +0x223c and
its reward bundle; quest +0x8 / +0xc are resolved into strings nothing reads (named description / hint after the
ObjectiveData layout [I]); objective +0x215c, +0x2174 (and, in quests, sort order, flags, hint text, description and
reward); mission instance +0x48, +0x4c, +0x50 and the location's w; component key +0x1c.

Found on the way (other models): the quest objective "hint text" note in `Objective.cs` (+0xc, "the quest objective
wrapper resolves it", FUN_142f28c90) is about the quest's own +0xc: FUN_142f28c90 is the ClientQuestData constructor.

## Switches Ghidra left undecoded (decoded 2026-10-12)

Five packet functions had "Could not recover jumptable". A script (not in this repository) read their jump tables from
the exe with capstone and listed each case's target and the functions it calls; every case leads to an ordinary,
decompiled function. Only the sub-opcode → handler mapping was missing:

| Function | System | Sub-opcodes handled | Notes |
|---|---|---|---|
| FUN_143310250 | Encounters (0x2C; i16 sub after the u16 base, header reader FUN_142b7dda0) | 2, 3, 4, 5, 6, 0x66, 0x6a, 0x6b, 0x72 (EncounterDetailsResponse, ctor FUN_1432eb8d0), 0x78, 0x79, 0x82 | Reached from the game's 0x2C handler FUN_142c428b0 (PlayClient vtable +0x2b0), which first takes 0x7D (death window, FUN_142cbca40) and 0x7E (`RespawnHandler:Hide`) itself. No encounter content in 0.589. |
| FUN_14326a960 | Mini-games | 0xd, 0xe, 0x12, 0x15, 0x25, 0x29, 0x2a, 0x33 | Shared-engine system; no 0.589 UI. |
| FUN_14330d470 | Admin commands (AdminCommandPacketClientTime, …) | 0-6 | Inline handlers. |
| FUN_142eb5a20 | Skill admin | 0-0xe → FUN_142eb5d50, 6510, 6370, 5c30, 7100, 6ca0, 6fe0, 5f90, 60b0, 5e20, 6230, 68f0, 6af0, 6e50, 6760 | Admin tooling for skill lines. |
| FUN_1431bb890 | (not a switch) | | A virtual tail call `jmp [rax+0x28]` after a list-node removal in the purchase code; Ghidra took it for a jump table. |

## Login and auth (named 2026-10-18)

Every field that was called `Unknown*` in the login server's models and the shared login-side models, traced in the
0.695 client and renamed. The wire layouts are unchanged except where the client proved them wrong (marked). Tests:
LandmarkEmulator.Tests/Packets/LoginModelTests.cs.

**Tunnel type 10 ArtData (TintDefinitionManagerData)**, reader FUN_1430af1e0 → FUN_143cd0780, which replaces the
client's TintDefinitionManager lists. Names from the client's own builder of the same lists from local tables
(ToolTintDefinitionManager FUN_143cd0ef0) and the lookups FUN_143ccf6f0 / FUN_143ccf800. Hash = the client's string
hash (signed shifts), "upper" = of the upper-cased string. The bytes sent are identical before and after (SHA-256 checked
in the tests); Resources/ArtTints.json was rewritten with the new keys, one record per line, colours as floats.

| List (manager offset) | Was | Now |
|---|---|---|
| 1 (+0x878) | Unknown0 {Unknown0, Unknown1} | AliasNames {AliasHash, AliasName} |
| 2 (+0x18e8, reverse map +0x2958) | Unknown1 {Unknown0, Unknown1} | AliasIds {AliasId = ArtTints.txt ID, AliasHash} |
| 3 (+0x39c8) | ArtTintOverrideGroups {Unknown0-3} | OverrideGroups {Key = group id, ArtSourceActorId, GroupId, Entries {ArtTintTypeId, ArtTintId}} |
| 4 (+0x39f8) | Unknown3 {Unknown0-4} | ActorTintTypes {Key = (model hash shifted 32) + shader hash, ArtTintTypeId, ActorModelHash (ACTOR_FILE_NAME as .dme), ShaderNameHash, ArtSourceActorId} |
| 5 (+0x230) | Unknown4 {Unknown0} | SemanticNames (the "Default" alias's SEMANTIC_NAMEs) |
| 6 (+0x550) | Unknown5 {Unknown0} | EditTypes (RGB, SCALAR, TEXTURE, ENABLE, VECTOR2) |
| 7 (+0x8, alias table) | ArtTintGroups {Id, AliasName, SemanticGroup, ArtTints, Unknown3, Unknown4} | Aliases {Key, AliasName, **Slot** (was misnamed SemanticGroup), Semantics, AliasHash (+0x268), SlotHash (+0x26c)} |
| 7, per semantic (FUN_142c2ff10) | {SemanticName, Unknown1-3, EditType, Unknown4, R, G, B, A} | {**SemanticGroup** (was misnamed SemanticName), SemanticGroupHash (upper), SemanticNameHash, EditTypeHash (upper), **StringParam** (was misnamed EditType: "" or a texture such as white.dds), StringParamHash (upper), Values f32 × 4 (FLOAT_1..4)} |
| 8 (+0x3a28) | Unknown7 {Unknown0, Unknown1} | PlayerTintOptions {ArtTintTypeId, ArtTintId}: ArtTintTypeTints rows of types with PLAYER_TINT_OPTION = 1 (633) |

The override groups' ArtSourceActorId is 0 in the live capture (the 0.695 table has 384 / 484 / 425 / ...); the lookup
FUN_143ccf800 does not read it, so the captured value is kept. Fixed in passing: the model's override-group reader read
its third field twice.

**Tunnel type 9 ClaimData** (`ServerClaimBasicData`, reader FUN_1430addc0, list at login state +0x20 +0x260). Read only
in two places: the server list row builder FUN_1430c3840 counts records whose u32 +0x18 is the row's server ("Claims",
shown in the Teleport Spire's world list), and a character delete drops records whose u64 +0x08 is that character
(FUN_1430c2b20). Named ServerId and OwnerCharacterId [P]; the others are stored and never read [P] and are named by
slot (Field10, Field1C, Field20, Flag24, Field70, Field74) or by analogy with the zone claim record [I] (ClaimId +0x00,
Name +0x28, Min / Max +0x40 / +0x58). The model's Read used to add empty records without reading them. Still sent empty
(every world shows 0 claims): see the open lead below.

**Tunnel type 7** (was `Unknown7` / "presumed a MOTD"): LoginMessage {Text}. The client reads the string and hands it to
an empty function (FUN_1430b2d50 → FUN_142ce7050 `ret`, 0.589 the same) [P], so the server stopped sending its welcome
line.

| Model | Was | Now | Evidence |
|---|---|---|---|
| CharacterCreateRequest | Unknown0 | PayloadLength | FUN_143a5aac0 sends {u64 server, Array<u8> payload} (u32 count + bytes, FUN_142da2e50) [P] |
| CharacterCreateRequest | Unknown1 | SelectedGender | login state +0x50, Lua CharacterSelect.SetGender / GetGender (FUN_1435d9e30 / FUN_1435d9e80) [P] |
| CharacterCreateRequest | Unknown2 (u32) | MaterialOverride (**string**) | FUN_142f59020 writes a string there (CharacterCustomizationData +0x20); always "" in create, so the old u32 read only matched an empty string [P] |
| CharacterCreateRequest9 (0.589) | Unknown0 | PayloadLength | same builder in 0.589 (the 0.589 / 0.695 function match: "same") [P] |
| CharacterLoginRequest | Unknown0 | PayloadLength | FUN_1430b3cb0 / FUN_143a5b9b0 / writer FUN_143a53e90 [P] |
| CharacterLoginReply.ServerInfo | Unknown0 | ClientExitUrl | stored at game +0x325a8 (FUN_1430ad6e0 → FUN_1430b4ad0); opened in the player's browser when the game closes (FUN_142d56760 → ShellExecuteA) [P]; name from the client's "ClientExitUrl" [I]. Kept "" |
| SessionReply (SOE) | Unknown0 | ProtocolVersion | UdpLibrary FUN_142b69770 type 2 stores it at connection +0x204 (as type 1 does the request's version); not read further [P]. Echoes the request (3) |
| CharacterModelInfo (CharacterCustomizationData) | Unknown1 | MaterialOverride | +0x20 string; set only by FUN_142ec5b90, which applies it as a material to the character and matching attachments (FUN_143d0b620) [P path, I meaning]. Sent "" |
| TerrainPlanCellEntry | Unknown0 | Version | the CellsUpdate cell version (protocol.md §8) |
| TerrainCel2Packer.CellRecord (Parser) | Unknown0 | Version | CellsUpdate cell entry +0x08 (VoxelFarm::CellIdVersionFlagsAndData, FUN_143146320; packet reader FUN_1431464f0); HandleCellsUpdate FUN_144016a90 takes 0 as block data (FUN_144016150), else geometry (FUN_1440164c0) [P] |
| TerrainCel2Packer.CellRecord (Parser) | Flags | FirstLayerId | the first layer's u32 layer id (FUN_143146cf0 +0x00) [P]; the "inter-segment tag" was each later layer's id. The walk now takes 9 bytes for an empty layer (was 15, which only lined up on the packet's last layer: the 6 tail bytes are the transaction list count and two bools, FUN_143148b80 / +0x48 / +0x49), reads the cell count as u32 and accepts cells with no layer |
| CaptureEnrichment.TryReadSendSelfSpawn (Parser) | comments Unknown0 / Unknown1 / Unknown4 | SizePrefix / TransientId / Rotation | the SendSelfToClient model's names (ServerTime for the old LastLoginTime) |
| SendSelfToClient (owned elsewhere) | Unknown30 | `LastPlayedProfileByType` (proposed as `ProfileTypeEntries`; named so with the other SendSelf fields, sendself.md) | the list after CurrentProfile: u32 n × {u32 ProfileType, u32 ProfileId} → FUN_142d4fa00 into `HashList<ProfileTypeEntry,8>` at `ProfileManager<ClientPcProfile>` (player +0x168, ctor FUN_142fc6d00) + 0x68; FUN_142fcaa60 adds the same pair (profile +0x14 type, +0x04 id) for types above 1 [P]. The server's (ProfileType, Id) order is right. WorldSession's one use renames with the property |

CharacterSelectInfoReply (2026-10-18; payload reader FUN_1430ad7c0, copies traced in the handler's disassembly
1430b4ebe-1430b4eec, data source row FUN_1430bb290):

| Model | Old | New | Evidence |
|---|---|---|---|
| CharacterPayload | UnknownQ (u64 +0x1d0) | ServerId | → summary +0x30 → column ServerId; CharacterSelection.lua SetServer [P]. Now the character's last world |
| CharacterPayload | Unknown0 (i8 +0x18) | Unused18 | copied to summary +0x20, never read [P] |
| CharacterPayload | Unknown1 (+0x1c) | Unused1C | not copied [P] (kept 1) |
| CharacterPayload | HeadId (+0x20) | Unused20 | not copied [P]; the head comes from the model block |
| CharacterPayload | Unknown7 (+0x34) | Unused34 | summary +0x4c, never read [P] |
| CharacterPayload | Unknown8 (u64 +0x58) | Unused58 | summary +0x50, never read [P] |
| CharacterPayload | Array142C34C30 (+0x38) | Unused38 | not copied [P] |
| CharacterAttachment | SemanticTint / Unknown3 | TintAlias / DecalAlias | +0xc0 / +0x120 strings, ProxiedCharacter::SetTintAndDecalAlias [P] |
| CharacterAttachment | Unknown4 / Unknown5 | TintId / CompositeEffectId | +0x180 / +0x184 into the actor attach FUN_143b95320 [P path] |
| CharacterAttachment | Unknown7 | Value18C | not 0 → added to the character's id set +0x380 (FUN_142e90e10) [P]; meaning open |
| CharacterAttachment | Unknown8 | Flag190 | passed through to FUN_143b95320 [P]; meaning open |
| Friend | (not modelled) | Key, Name, Text40, Text64, CharacterGuid, Online, Status | FUN_1430ad9e0; only Online and Status are read [P] |
| CharacterSelectInfoReply9 payload | Unknown5 / 6 / 7 / 9 | Race / ProfileId / Unused58 / ServerId | by analogy with protocol 10 [I: no LoginUdp_9 reader] |

Open lead: the server list's per-world "Claims" count (tunnel type 9) needs the account's sites on every world; the
login server does not read world claim data yet.
