# SendSelfToClient (login character data)

The client parses SendSelfToClient (opcode 0x0F) straight into its player object, `ClientPlayer`
(`SendSelfToClient_Read` @ 142b80420; the object is `*(DAT_1453524a0)` = `*(game player +0xf18)`). Each block below is
named by the client code that uses the same offset afterwards: the packet family the dispatcher routes to the manager
at that offset (base opcode → case through the byte table at 0x142c874b4), Lua bindings that read it, or UI data
sources built from it. Tools: `re/tools/pkt.py SendSelfToClient_Read` (layout), `re/tools/fieldmap.py`.

| Player offset | Block (reader) | Identified as | Evidence |
|---|---|---|---|
| 0x18 | string | chat language | Lua SetChatLanguage reads it |
| 0x38 | bit flags (ReadUInt2BitLength…) | flags | |
| 0x40 | FUN_142b8c3c0: 3 × u32, 2 strings | character identity (names) | model `Identity` |
| 0x750f0 | FUN_142b7ef60 | character model / appearance (customisations, race, gender) | same block LightweightToFullPc copies for other players |
| 0x72a38 / 0x72a40 | u32 / FUN_142b9b7a0 | coins (Lua GetPlayerCoinCount) / following list | |
| 0x7a9d0 | FUN_142b9d1b0 (`ClassProgression`) | class progression | class name in reader |
| 0x753f0 / 0x753f8 | | ability-related (FUN_142d7cae0 = ability packets) | |
| 0x75630 / 0x75638 | FUN_142ba5eb0 | | read by FUN_142d71070, FUN_142df5410 |
| 0x268b0 | FUN_142b81c10 | notifications / message panel data | PlayerUpdate dispatcher, "NotificationHandler:setMessagePanelData" |
| 0x38658 | FUN_142b7f660 | achievements | manager handles AchievementBase 0x92 (FUN_142d92750) |
| 0x7aa00 | FUN_142b82050 | crafting: known recipes, stations | manager handles RecipeBase 0x42 (FUN_142e9acd0); CancelRecipe, Refining*, Repair* bindings |
| 0x7adf8 | int list | **known props** (manager at 0x7adf0) | CreatePropItem, prop dispatcher |
| 0x7ae30 | int list | **customisation knowledge** (barbershop unlocks; manager at 0x7ae28) | 0x11C sub 2 AddKnowledge adds to this set |
| 0x7ae48 | int list | customisation knowledge, type 4 (second set of the manager at 0x7ae28) | AddKnowledgeResponse type 4 adds here, type 3 to 0x7ae30 (FUN_1435d7a50) |
| 0x75088 / 0x75090 | bool / int list | **first-time events**: manager enabled / events already seen (`ClientFirstTimeEventManager`) | FirstTimeEventBase 0x6D, see below |
| 0x595b0 | FUN_142b81a70 | titles | manager handles PlayerTitleBase 0x94 (FUN_142e89bd0) |
| 0x75d68 | FUN_142b9b8e0 | currencies | manager handles CurrencyBase 0xE5 (FUN_142db1fb0) |
| 0x75e00 | FUN_142ba3f60 | weapons / projectiles (used by many combat functions) | |
| 0x75e70 | FUN_142b9cf30 | equipment | manager handles EquipmentBase 0xCB (FUN_142da4a00) |
| 0x75f18 | FUN_142b9d820 | experience | manager handles ExperienceBase 0xBE (FUN_142e38a70) |
| 0x76938 | FUN_142b87860 | items | manager handles ItemsBase 0xE6 (FUN_142e62ed0) |
| 0x771a0 / 0x771a8 | FUN_142b95c60 | skill / class points (SpentClassPointsTotal), in the skill instance manager | data source Character.PlayerData |
| 0x7ae60 | FUN_142ba38f0 | inventory filters | Lua AddFilter / RemoveFilter / ResetFilters |
| 0x2b0 | FUN_142b9b410 | `HashListMap<int,int>` {u32 key, u32 value} | type from the ClientPcData destructor; no reader found yet |
| (none) | u32 list after the first-time events | read and dropped in 0.589 (each id goes to an empty function, FUN_142f31eb0) | |
| 0x75670 | stats list | `HashList<CharacterStat,133>` | |
| 0x75ac0 | u32 list | signature phases (`HashList<StrongType<int,SignaturePhaseId>>`) | |
| 0x114, 0x118, 0x130, 0x148 | u32, 2 × `Array<int>`, `Array<bool>` | plain ClientPcData fields | |
| 0x753f8 | FUN_142b9bce0 | ability cooldowns (`HashList<Abilities::CooldownTracker,64>`) | |
| 0x75638 | FUN_142ba5eb0 | active effects (`HashListMap<__int64,Abilities::ClientEffectData>`) | |
| 0x75bf0 | FUN_142b7d220 | acquire timers (`AcquireTimers::ClientAcquireTimerManager` at 0x75be8) | constructor vtable |
| 0x75dd0 | FUN_142ba3b90 | u32, (3 × u32), u32, (3 × u32): plain fields | |
| 0x75e00, 0x75e30 | FUN_142ba3f60, FUN_142ba3d80 | definition variables (`DefinitionFilters::ClientDefinitionVariableManager` at 0x75df0) | constructor vtable |
| 0x75fc0 | FUN_142ba42c0 | part of the experience manager (0x75f18 + 0xa8), same shape as 0x75dd0 | |
| 0x76920 | FUN_142b98950 | 3 × u32 | |
| 0x76a58 | FUN_142ba4800 | class loadouts (object at 0x76a50 owns the "Character.ClassLoadouts" data source) | FUN_142e67cf0 |
| 0x76db8 | FUN_142b98880 (5 lists) | missions (`Missions::ClientMissionInstanceManager`) | constructor vtable |
| 0x771d0 | FUN_142ba2610 | skill instances (`Skills::ClientSkillInstanceManager` at 0x77198) | constructor vtable |



Every block now has a client class or owner. The class names come from the `ClientPcData` constructor (FUN_142f27f70)
and destructor (FUN_142f296c0), which set each member's vtable. The server sends the blocks it has no data for
empty, which is what a new character has.

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
