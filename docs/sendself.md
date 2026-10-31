# SendSelfToClient (login character data)

The client parses SendSelfToClient (opcode 0x0F) straight into its player object, `ClientPlayer`
(`SendSelfToClient_Read` @ 142b80420; the object is `*(DAT_1453524a0)` = `*(game player +0xf18)`). Each block below is
named by the client code that uses the same offset afterwards: the packet family the dispatcher routes to the manager
at that offset (base opcode → case through the byte table at 0x142c874b4), Lua bindings that read it, or UI data
sources built from it. The generated field map is `research/tables/fieldmap_SendSelfToClient_Read.md` (layout:
`research/tables/sendself_layout.txt`).

| Player offset | Block (reader) | Identified as | Evidence |
|---|---|---|---|
| 0x18 | string | the server sends the account name; live sent the character's name ("Freldam") | no reader found yet (Lua Ui.SetChatLanguage / GetChatLanguage, FUN_1435d1d70 / FUN_1435d1f80, call player vtable slots +0x18 / +0x20, not this field; phaseE3-wire-a.md F2) |
| 0x38 | bit flags (ReadUInt2BitLength…) | flags | |
| 0x40 | FUN_142b8c3c0 (0.695 FUN_142c25b90): 3 × u32, 2 strings | character identity: the name block (Gender, UnusedA4, Honor, first, last) | model `CharacterIdentity`, see below |
| 0x750f0 | FUN_142b7ef60 | character model / appearance (customisations, race, gender) | same block LightweightToFullPc copies for other players |
| 0x72a38 / 0x72a40 | u32 / FUN_142b9b7a0 | coins (Lua GetPlayerCoinCount) / following list | |
| 0x7a9d0 | FUN_142b9d1b0 (`ClassProgression`) | class progression | class name in reader |
| 0x753f0 / 0x753f8 | u32 / FUN_142c35860 (0.695) | `CastKeySeed` / `AbilityCooldowns` (see "Remaining fields named 2026-10-18") | FUN_142e0f810 "send cast to zone"; ability cooldown lookups FUN_142fcd170 |
| 0x75630 / 0x75638 | FUN_142ba5eb0 | | read by FUN_142d71070, FUN_142df5410 |
| 0x268b0 | FUN_142b81c10 (0.695 FUN_142c1b3d0) | **quests** (`HashListMap<int,ClientQuestData,256,8>`, see "Quests, palettes and missions" below) | QuestBase 0x34 handler FUN_142f2cd80 works on it (the old "notifications" reading was wrong) |
| 0x38658 | FUN_142b7f660 | achievements (AchievementData records, `Model/Shared/Achievement.cs`; sent empty, the list comes as AchievementInitialize) | manager handles AchievementBase 0x92 (FUN_142d92750) |
| 0x7aa00 | FUN_142b82050 (0.695 FUN_142c1b810) | crafting: known recipes, cooldowns, **hidden recipes**, recipe records (see below) | manager handles RecipeBase 0x42 (FUN_142e9acd0); CancelRecipe, Refining*, Repair* bindings |
| 0x7adf8 | int list | **known props** (manager at 0x7adf0) | CreatePropItem, prop dispatcher |
| 0x7ae30 | int list | **customisation knowledge** (barbershop unlocks; manager at 0x7ae28) | 0x11C sub 2 AddKnowledge adds to this set |
| 0x7ae48 | int list | customisation knowledge, type 4 (second set of the manager at 0x7ae28) | AddKnowledgeResponse type 4 adds here, type 3 to 0x7ae30 (FUN_1435d7a50) |
| 0x59a00 | `u32 n` x PacketMountInfo (FUN_142c23fe0), copied by FUN_142f0fd70 | mounts (`HashListMap<int, MountClientInfo>`; `Model/Shared/Mount.cs`) | sent empty, as live (no mounts in Landmark) |
| 0x75088 / 0x75090 | bool / int list | **first-time events**: manager enabled / events already seen (`ClientFirstTimeEventManager`) | FirstTimeEventBase 0x6D, see below |
| 0x595b0 | FUN_142b81a70 | titles | manager handles PlayerTitleBase 0x94 (FUN_142e89bd0) |
| 0x75d68 | FUN_142b9b8e0 (0.695 FUN_142c35460) | currency discounts `HashListMap<int,float,8>` (`CurrencyDiscounts`) | manager handles CurrencyBase 0xE5; prices × (1 − discount), FUN_142f1d000 |
| 0x75e00 | FUN_142ba3f60 (0.695 FUN_142c3dae0) | definition variables (`DefinitionVariables`, see below) | read by the item / combat code through FUN_143c2caa0 etc. |
| 0x75e70 | FUN_142b9cf30 (0.695 FUN_142c36ad0) | equipment records `HashListMap<int,CharacterEquipmentRecord>` (`EquipmentRecords`) | manager handles EquipmentBase 0xCB (0.695 FUN_142e42da0) |
| 0x75f18 | FUN_142b9d820 (0.695 FUN_142c373e0) | experience records `HashListMap<int,ExperienceRecord>` (`ExperienceRecords`) | manager handles ExperienceBase 0xBE (0.695 FUN_142ed77a0) |
| 0x76938 | FUN_142b87860 (0.695 FUN_142c21030) | item timer manager (`ItemTimers`) | manager handles ItemsBase 0xE6 (SetItemTimerManager FUN_142f02070) |
| *(0x753d8) | FUN_142c47af0 (0.695) | inventory: 12 containers, then per item the ClientItem record and one bool (ClientItem +0x161; `Model/Shared/InventoryItem.cs`) | ClientInventory |
| 0x771a0..0x771c0 | FUN_142b95c60 (0.695 FUN_142c2f750) | skill point manager (`SkillPoints`): earned, spent, progress, rate tier value | data source Character.PlayerData (SpentClassPointsTotal, AvailableClassPoints); SkillBase 0xBC sub 6 |
| 0x7ae60 | FUN_142ba38f0 (0.695 FUN_142c3d470) | inventory filters (`InventoryFilters`) | Lua AddFilter / RemoveFilter / ResetFilters; data source Character.InventoryFilters |
| 0x2b0 | FUN_142b9b410 (0.695 FUN_142c34f90) | `HashListMap<int,int>` {u32 key, u32 value} (`IntMap2b0`) | type from the ClientPcData destructor; never read by the client (see below) |
| (none) | u32 list after the first-time events | read and dropped in 0.589 (each id goes to an empty function, FUN_142f31eb0) | |
| 0x75670 | stats list | `HashList<CharacterStat,133>` | |
| 0x75ac0 | u32 list | signature phases (`HashList<StrongType<int,SignaturePhaseId>>`) | |
| 0x114, 0x118, 0x130, 0x148 | u32, 2 × `Array<int>`, `Array<bool>` | `CurrentAdventureId` and its three arrays: the block PlayerUpdate 0x36 SetCurrentAdventure rewrites | dispatcher FUN_142cdfa60 case 0x35, reader FUN_142c2b4b0 |
| 0x753f8 | FUN_142b9bce0 | ability cooldowns (`HashList<Abilities::CooldownTracker,64>`) | |
| 0x75638 | FUN_142ba5eb0 | active effects (`HashListMap<__int64,Abilities::ClientEffectData>`) | |
| 0x75bf0 | FUN_142b7d220 (0.695 FUN_142c169d0) | acquire timers (`AcquireTimers::ClientAcquireTimerManager` at 0x75be8; `AcquireTimers`) | constructor vtable; AcquireTimersBase 0xDE |
| 0x75dd0 | FUN_142ba3b90 (0.695 FUN_142c3d710) | currency rate tier, 5 × u32 (`CurrencyRateTier`) | CurrencyBase 0xE5 sub 2 SetCurrencyRateTier reads the same block; never read |
| 0x75e00, 0x75e30 | FUN_142ba3f60, FUN_142ba3d80 (0.695 FUN_142c3dae0, FUN_142c3d900) | definition variables and int sets (`DefinitionFilters::ClientDefinitionVariableManager` at 0x75df0) | constructor vtable |
| 0x75fc0 | FUN_142ba42c0 (0.695 FUN_142c3de40) | experience rate tier, 5 × u32 (`ExperienceRateTier`), experience manager + 0xa8 | ExperienceBase 0xBE sub 0 SetExperienceRateTier reads the same block; never read |
| 0x76920 | FUN_142b98950 | grief state: i32 grief points, i32 damage points, i32 grief level (0.695 FUN_142c324d0) | names from the client's grief dump (FUN_142e43250); written from `Game/Grief.cs` |
| 0x76a58 | FUN_142ba4800 | class loadouts (object at 0x76a50 owns the "Character.ClassLoadouts" data source) | FUN_142e67cf0 |
| 0x76db8 | FUN_142b98880 (5 lists) | missions (`Missions::ClientMissionInstanceManager`) | constructor vtable |
| 0x771d0 | FUN_142ba2610 | skill instances (`Skills::ClientSkillInstanceManager` at 0x77198) | constructor vtable |
| 0x1d0 | FUN_142d4fa00 per entry (0.695) | last played profile per profile type (`LastPlayedProfileByType`) | ClientRequirementCheckLastPlayedProfileOfTypeRank (FUN_14370f8c0) |
| 0x24490 | FUN_142f75760, FUN_142f756f0 (0.695) | `ClientVehicleManager`: owned-vehicle slot (+0x00) and occupied-vehicle slot (+0x178) | "ClientVehicleManager::Update" FUN_142f79dc0; VehicleOwnerPacket / VehicleOccupyPacket fill the same slots |



Every block now has a client class or owner. The class names come from the `ClientPcData` constructor (FUN_142f27f70)
and destructor (FUN_142f296c0), which set each member's vtable. The server sends the blocks it has no data for
empty, which is what a new character has.

## Remaining fields named 2026-10-18 (0.695 reader FUN_142c19bd0)

The last numbered fields of the main record, named from the client code that uses each offset. [P] = proven from the
client, [I] = inference. Values are the live capture's (Resources/PacketDumps/SendSelf.bin), checked by
`SendSelfCaptureTests.LiveCapture_NamedTailAndProfileFields_MatchTheModelDefaults`.

| Field (was) | Client offset / users | Live | Server |
|---|---|---|---|
| `LastPlayedProfileByType` (Unknown30) {ProfileType, ProfileId} | player +0x1d0 HashListMap<int,int> of the profile manager (+0x168); the reader empties it after the profile records fill it (FUN_142d2fc30), FUN_142fd33a0 sets it on a profile switch, ClientRequirementCheckLastPlayedProfileOfTypeRank (FUN_14370f8c0) reads it [P] | empty | empty (sent the current profile until 2026-10-18) |
| `AbilityCooldowns` (Unknown31) {Key, CooldownGroup, ElapsedMs} | +0x753f8 HashList<Abilities::CooldownTracker,64> (FUN_142c35860 / FUN_142c1e470): key and record +0 = the ability's cooldown group (record +0x7c), start = timeGetTime − ElapsedMs; FUN_142fcd170 / FUN_142fcab90 / FUN_142fcb2f0, AbilityPacketClearAllCooldowns [P] | empty | empty |
| `CastKeySeed` (Unknown32) | +0x753f0: FUN_142e0f810 builds ((cast counter +0x753f4 & 0xff) ^ seed) × 0x502c3f11 for each cast request: the CastAttempt's cast hash (+0x20, the counter at +0x28), which every answer echoes as the cast's identity [P] | 0x5BB9EB0B | a random non-zero seed per session (WorldSession.CastKeySeed, L189 closed 2026-10-26); a hash that doesn't match is logged (Debug) and answered as usual |
| `CurrentAdventureId` (Unknown52) | +0x114: only PlayerUpdate 0x36 SetCurrentAdventure (FUN_142cdfa60 case 0x35, reader FUN_142c2b4b0: u64 player, u32 adventure, list<u32> → +0x118, list<u32> → +0x130, list<bool> → +0x148) writes it, then calls the quest manager's (player +0x268b0) hook FUN_142f2dd50, an empty function in 0.695 and 0.589; nothing reads the values [P]. An EQN adventure (quest) system with no Landmark use (L190 closed 2026-10-26) | 0 | 0 |
| `OwnedVehicleGuid` (Tail24490Id) | ClientVehicleManager +0x00 (player +0x24490): the slot VehicleOwnerPacket (0xBF 1, FUN_142f783c0) fills; Update FUN_142f79dc0 looks it up [P] | 0 | 0 |
| `Unused244a8` (Tail24490Value) | manager +0x18: written only (FUN_142f71860, FUN_142f73830, FUN_142f75760, FUN_142f783c0) [P] | 0 | 0 |
| `OccupiedVehicleGuid` (Tail24490B0) | manager +0x178: the slot VehicleOccupyPacket (0xBF 2, FUN_142f77eb0) fills; FUN_142f74040 / FUN_143618940 return that vehicle instead of the player [P] | 0 | 0 |
| `Unused24618` (Tail24490B1) | manager +0x188: written only (FUN_142f756f0, FUN_142f77eb0, FUN_142f73830) [P] | 0 | 0 |
| `Unused24620` (Tail24490B2) | manager +0x190: written only (FUN_142f756f0, FUN_142f77eb0) [P] | 0 | 0 |
| `OccupiedVehicleSeat` (Tail24490B3) | manager +0x194: −1 = in no vehicle (constructor FUN_142f71860, reset FUN_142f73830, leave FUN_142f73940); FUN_142f77eb0 tears the occupancy down when it changes [P]; "seat" [I] | 0xFFFFFFFF | 0xFFFFFFFF (sent 0 until 2026-10-18) |

`ReadCapture` now reads the capture with the 0.695 layout (`Read`): the old capture-only tail (six bytes, a string and
34 "unread" bytes) was the vehicle slots, the admin flag, the inventory filters and the bio read out of step.

## Manager blocks (named 2026-10-18 from the 0.695 readers)

The blocks between the titles and the bio, model classes in `SendSelfToClient.cs`. Field names come from the client's
own dumps, data sources and logs (cited in the model's comments); "never read" means the reader (and the matching
packet) are the only code that touches the offset, checked with displacement scans of the 0.695 exe.

| Model | Client object (reader) | Fields |
|---|---|---|
| `IntMap2b0Entry` | ClientPcData +0x2b0 `HashListMap<int,int>` (FUN_142c34f90) | Key, Value — never read; sent empty, live empty |
| `ProgressTimer` | seconds timer (FUN_142c251e0) | ExpireSec, Progress, LastUpdateTime (u64): "expireSec / progress / progressPcnt" |
| `ProgressTimerMsec` | millisecond timer (FUN_142c25130) | ExpireMsec, Progress, LastUpdateMsec (u64), LastUpdateTime (u64) |
| `AcquireTimerManager` | `AcquireTimers::ClientAcquireTimerManager` | ProfileTimers {Key, timer, ProfileId}, VehicleTimers {KeyVehicleId, KeyFactionId (u8), timer, VehicleId, FactionId (u8)}, Flags160 (u8, never read; live 0x80) |
| `CurrencyDiscount` | currency manager map (FUN_142c35460) | CurrencyType, Discount (f32: price × (1 − Discount)) |
| `RateTier` | currency / experience rate tier (FUN_142c3d710, FUN_142c3de40) | TierValueMin, TierMappedValue, NameId, DescriptionId, IconId (the SkillPointRateTier shape, SkillPointRates.txt); never read |
| `DefinitionVariable` | `HashListMap<DefinitionVariableKey, DefinitionVariable>` (FUN_142c3dae0) | VariableId, DefinitionId (u64), Base (f32), Addend (f32): the client uses Base + Addend instead of the definition's own value |
| `DefinitionInts` | `HashListMap<DefinitionVariableKey, HashListMap<int,int,4>>` (FUN_142c3d900) | VariableId, DefinitionId, Ints {Key, Value} |
| `CharacterEquipmentRecord` | `Equipment::CharacterEquipmentRecord` (FUN_142c187f0) | Key (profile), ProfileId, String08, String48, Slots: `EquipmentSlotRecord` {Key, EquipSlotId, ItemGuid, String30, String70}; the four strings have no reader found yet |
| `ExperienceRecord` | `Experience::ExperienceRecord` (FUN_142c1fa30) | Key, RecordId, Xp, Slot08 (never read), Level ("JobLevel", 1 when missing), Slot10 (never read) |
| `ItemTimerManager` | item timer manager (FUN_142c21030) | ItemLockTimer (ms), ItemLineTimers {Key, ms timer, ItemLineId}, ItemTrialLockTimer, ItemLineTrialTimers {Key, timer, ItemLineId, Type}, ListenerFlags (u8: bit 0x40 mutes the change listener; live 0x80) |
| `SkillPointManager` | skill point block of `Skills::ClientSkillInstanceManager` (FUN_142c2f750) | EarnedSkillPoints, SpentSkillPoints (u64), SkillPointProgress (f64), Slot771b8 (u64, no reader found; live 1487581831), SkillPointRateTierValue |
| `InventoryFilterManager` | `InventoryFilters::ClientInventoryFilterManager` (FUN_142c3d470) | Filters `PlayerInventoryFilter` {Key, FilterId, FilterName, FilterString}, NextFilterId (live 1); filled from the character's saved bag filters (protocol.md §14 "Bag filters"), NextFilterId one past the highest id |

Changed on 2026-10-18: `InventoryFilters.NextFilterId` 0 → 1 (live value and the client's default; with 0 the first
filter got id 0, which RemoveFilter ignores); the equipment record lost a second record list that neither client reads
(never on the wire, the list was empty). The live capture (`Resources/PacketDumps/SendSelf.bin`, 2017-02-20) is the
0.695 layout end to end: `Read` consumes it to the last byte and `Write` gives it back byte for byte
(Tests/Packets/SendSelfCaptureTests.cs). `ReadCapture`'s "0.589-era tail" (six bytes, filters, string, 34 bytes) is a
misreading of the 0.695 tail of the same 53 bytes.

## Crafting block (recipe manager, player +0x7aa00)

The 0.695 reader FUN_142c1b810 fills the recipe manager in this order (`SendSelfToClient.RecipeData`):

| Order | Manager offset | Wire | Content |
|---|---|---|---|
| 1 | +0x08 | `u32 n × u32` | Known recipe ids (`KnownRecipeIds`; a set; cleared first). The server sends the default-learned ones and those learned from recipe items. |
| 2 | +0x38 | FUN_142c35bd0 | Cooldowns: `Crafting::PlayerRecipeData` {u32 key, u32 recipe, u64 start} (model `PlayerRecipeData`). Live sent one; the server sends none (no recipe has a COOLDOWN_TIME). |
| 3 | +0x20 | `u32 n × u32` | **Hidden recipe ids** (a set; cleared first, so every SendSelf replaces it). The crafting window leaves them out and the client's own start paths refuse them. Changed in play by RecipeBase 0x1F / 0x20 (protocol.md §12). |
| 4 | +0x78 | `u32 n × record` | Recipe records: the known recipes that are on a crafting station, plus the recipes of recipe items in the bag (so learning one shows it at once). Layout in protocol.md §12. |

The server fills the hidden list with the Lumen Station unlocks the character already owns (`RecipeBook.HiddenUnlocks`;
OPEN-LEADS L43): of the 581 recipes with a `VISIBILITY_REQUIREMENT_ID`, those whose "Recipe:" item teaches nothing new
(its recipes or prop are known) and the storage expansions already at their most. The client loads
`VISIBILITY_REQUIREMENT_ID` but nothing reads it (every reader of the recipe definitions was checked), so live's server
must have made this list; the rule is ours. An unused copy in the bag does not hide an unlock (buying a second is
refused instead), nor does the Dungeon Builder's Starter Set (opening it is not recorded). At most 581 ids (~2.3 KB).

## Quests, palettes and missions (named 2026-10-18)

All three are sent empty, as live; the models (`SendSelfToClient.QuestData`, `PlayerPalette`, `MissionInstanceManager`)
name every field from the 0.695 client code that reads it, and fields only the reader, constructor and copy touch are
named by offset (`Slot18`, ...) with "never read [P]".

* **Quests** (player +0x268b0, reader FUN_142c1b3d0). Each quest record (FUN_142c2e280) becomes a ClientQuestData
  (FUN_142f28c90 resolves its +0x4 / +0x8 / +0xc text ids into strings). Only the QuestBase 0x34 handler
  (FUN_142f2cd80) and the quest manager methods read quests: the id, the name (notices "QuestStarted", "QuestCompleted",
  "QuestFailed", "QuestAbandoned", the objective notices), `Silent` (+0x24: no notices at all), `LastUpdate` (+0x28:
  objective packets set it, the highest goes to the helper when the selected quest ends) and the objectives. Objectives
  are ObjectiveData records (same names as `Model/Shared/Objective.cs`): status 1 / 2 / 3 (activated, complete, failed),
  count, total and the event bar are used; +0x10 / +0x11 / +0x18 / +0x20 / +0x2238 / +0x223c of the quest and the
  objective's sort order / flags / hint text are never read by the quest code. Landmark has no quest UI (the Lua
  QuestHandler the notices call is not in the 0.695 UI).
* **Palettes** (player +0x753e8, reader FUN_142c3fc60; the same record is PaletteUpdate 0xF5 sub 5). Name (radial menu
  list), id, entries {slot, type, value} (type 1 Design.Props, 2 Voxel.Materials, 3 Voxel.Shapes, 4
  Character.Templates; the icon and name come from that data source row, FUN_142f131e0), `ReadOnly` (+0x54) and
  `MenuSlot` (+0x50, the radial position FUN_142f13a70 re-packs and reports with SlotUpdates sub 7). The client sends no
  AddEntry / Delete / DeleteEntry for a read-only palette (FUN_142f14850 / FUN_142f14a10 / FUN_142f14aa0).
* **Missions** (player +0x76db8, reader FUN_142c32400). The client's mission dump (FUN_142f0b8c0 / FUN_143c50980) names
  the instance fields: key {missionId, missionInstanceId, factionId}, priority (+0x54; with the definition's priority
  bracket it orders the lists), zoneId, regionId, location x / y / z. The component maps' key adds the mission
  component id (+0x18). The manager's change callbacks are empty, so nothing else reads them; +0x48 .. +0x50, the
  location's w and the component key's +0x1c are never read.

## First-time events (help pages)

`ClientFirstTimeEventManager` (+0x75088) shows the one-off help pages (FirstTimeEvents.txt ids, client
HelpPages/*_FTE.html). FirstTimeEventBase 0x6D, u8 sub:

| Sub | Direction | Body | Client code |
|---|---|---|---|
| 1 TriggerRequest | client → server | u32 event | FUN_143580bf0: sent by Ui.TriggerFirstTimeEvent (ClientTimerDefinitions.txt timers) when enabled and not seen |
| 2 StatePacket | server → client | u32 event, bool clear | FUN_1435802c0: clear=false marks seen, true forgets (event -1 = all) |
| 3 ClearRequest | client → server | u32 event (-1 = all) | debug command |
| 4 EnablePacket | client → server | bool | debug command |
| 5 ScriptPacket | server → client | string function, string params, u32 event, bool clear | FUN_143580670: runs the Lua function with the comma-separated params, then marks as sub 2 |

The server opens the page with ScriptPacket `ShowHelpWindow` / `Small,<page>,<position>` (Scripts/global.lua), on the
UI events the windows report, when a pick or axe is equipped and when gear first wears (Game/FirstTimeEvents.cs).

## Records named from the 0.695 readers (2026-10-18)

Every field of these records is named from the 0.695 reader and the client code that uses the offset it is stored
at; a field the client stores but never reads is named by its offset (`UnusedXX`). The model comments list the
functions. Live values: Resources/PacketDumps/SendSelf.bin (character "Freldam", profile 46).

**Name block** (`CharacterIdentity`, FUN_142c25b90; also in chat, friends, guild, mail, trade): `u32 Gender (+0xa0),
u32 UnusedA4, u32 Honor (+0xa8), string first, string last`. The only consumer turns the block into a T4 text
parameter (FUN_142d8a970); the T4 JSON log names the two attributes it passes on (GENDER, HONOR). Live 0 / 0 / 0.

**Profile** (FUN_142c1ac40 → a 200-byte record in the player's profile hash, player +0x190): ProfileId, NameId,
ProfileType, the item classes the profile may use (`HashList<ProfileItemClassData>` {key = item class, item class,
relevance rating}; only the key is read), FactionId (an i8), NoJump / NoStrafe / CrouchAtAnySpeed (bits 0x08 / 0x04 /
0x10 of +0x00, read by LocalProxiedCharacter's CanJump / strafe input / CanCrouch), StandCameraHeight,
CrouchCameraHeight (read: slot 164), LineOfSightHeight, UiModelCameraId, CapsuleRadius, Mass (read: set on the
physics), CharSize / Feet / Armor (read: the Footstep.txt sound rows). The stand / line-of-sight / capsule / UI-camera
values and 17 more (`Unused…`) are stored and never read (the client takes most profile values from its own
ProfileDefinitions by id). Behaviour change 2026-10-18: the server
sent the floats in table-column order (line of sight 2.75 in the crouch-camera slot the client reads, where live had
1.4) and CHAR_SIZE / ARMOR / FEET one slot late (the footsteps used size 1 and the armor value as feet); it now sends
them in the client's order, as live did.

**Collection** (FUN_142c18f50 → player +0x75220; ClientUpdate 0x29 subs 7 / 9 carry the same data): CollectionId,
NameId, Unused140, Unused144, IconId, IconTintId, StartMode (0 started at once, 1 on the first collected entry), the
reward bundle, then `u32 n × {u32 key, entry}` with entry `{EntryId, Unused20, CollectionId, NameId, ImageSetId,
TintId, MessageId, bool Collected}` (FUN_142c190d0). The model lacked the entry list (a sent collection would have
shifted the rest of SendSelf); none is sent, as live.

**Active effects** (`Buff`, `Abilities::ClientEffectData`, FUN_142c19230 → player +0x75638; 161 bytes): KeyGuid,
KeyId, EffectId (the key), GroupId, EffectType (type-name hash), AnimationId, VisualEffectId, VisualEffectDuration,
CasterGuid, TargetGuid, StartTime (server ms), AddedTime, Unused50, Unused54, Unused80 (4 × u32), TypeParameters
(4 × f32), IconDuration, Unused90 (4 × u32), NameId, DescriptionId, IconId, Duration, EnemyCaster, StatId, StatValue,
GoodType, StackCount, Unused00. Names from the buff bar data source Character.AbilityEffects, the combat log and the
in-play builders (EffectInitialized / EffectStart). The model had an extra u64 (169 bytes); none is sent, as live.

**Acquaintances** (FUN_142c2e960 → player +0x74aa0): CharacterGuid, Name, Unused20 (stored, never read), SortKey,
Online.

**Class loadouts** (FUN_142c3e380 → player +0x76a58): `u32 n × {u32 LoadoutId (the map key the weapon swap looks the
active loadout up by), LoadoutRecord}`.
