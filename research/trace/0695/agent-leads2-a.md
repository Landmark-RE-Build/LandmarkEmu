# Open leads, second pass — group A (wire fields), 2026-10-17

Leads: L9, L28, L31, L32, L35, L37, L38, L53, L79, L80, L85, L126, L156, L160.
Read-only pass. Marks: **[P]** proven (function address / file:line / table row), **[I]** inferred (reason given).
Sources: re/decomp_all_695 (0.695), re/decomp_all (0.589), <0.695 test copy>/Landmark64.exe (read with the sweep's
scratch rd.py / re/tools/refscan.py), gameData695, the shipped UI / Lua, server code under LandmarkEmu/.
Captures in the repo: only `LandmarkEmulator.WorldServer/Resources/PacketDumps/` (PropBase, ReferenceData 39 / 41,
CommandBase, AchievementBase, SendSelf, PropTints, TerrainCellManifest) and the four CasterInfoReply bodies in
MiscHandler.cs `capturedCasterInfo`. None of them carries 0xF4/0x1B, 0x2D/0x1E, 0x10D, 0xEC/6 or 0xEE.

## Summary

| Lead | Verdict | One line |
|---|---|---|
| L9 | **FOUND** | The client's own packet printer FUN_143a57510 names all six strings: Session, Fingerprint, Locale, ThirdPartyAuthTicket, ThirdPartyUserId, ThirdPartyId |
| L28 | ADVANCED | Radial entry found: RadialMenuButtons row 75 (menu 3 = player, button 7, text 563898 "Inspect", Lua `Ui.InspectPlayer`). +0x110 / +0x11c sit at the same offsets in 0.589, and no 0.695 reader turned up |
| L31 | **FOUND** (client side) | The client has no other way to move a matched player: the update's u64 is never read, "Match found" only plays a sound, and the S→C Request knows types 4/5/6. JoinTeam has no answer packet. The server already does both |
| L32 | **FOUND** | The last unnamed fields: the settings value's u32 +0x50/+0x54 and the per-block string list are only copied back on Apply. **PropState's new u64 = the free-for-all controller's player guid.** PropState / PropInteraction drive the "InstancedGameObject" overhead indicator, which we never send |
| L35 | ADVANCED | No client code holds any refusal text id as a constant. The 0.695-new unmapped refusal texts are exactly 577364, 577416 and 577705-577707, all already built. List of server-only CodeStringMappings keys added |
| L37 | **FOUND** (built) | FUN_142dc9660 is the only reader of entry +0x15c in the claims and InstancedGame code. The team count is already sent (InstancedGames.cs:445-456) |
| L38 | FOUND (closed earlier) | Confirmed: the 0xED changes it listed are built (RewardChests.cs PickUpReply 0xE, PropSettings f64) |
| L53 | ADVANCED | **re/tools/packetids.py drops every namespaced id name** (`\w+` doesn't match `::`), which loses 135 rows in 0.589. The 0.695 registry FUN_1434c7940 gives 1,397 rows; 23 of the 108 non-PlayerUpdate L53 classes now have a registry sub |
| L79 | ADVANCED | Trailing u64: when it is non-zero, each listed cell is handed with it to FUN_14401ecf0 (sentinel DAT_14560bdc0 has no writer, so it is 0). The u16 is stored at VoxelClientBlockEvent +0x70 with no reader. Send u16 0, u64 0 |
| L80 | ADVANCED | The only keyed lookup of the manifest (FUN_144014800) copies set and versions, never the flag. The other use is a whole-entry copy into the terrain ResetMemento. The client never seems to read the flag; 0 is safe |
| L85 | ADVANCED + **server change** | The apply copies several fields unconditionally whose client defaults are not 0. We overwrite chat colours (0xFFFFFF / 0xD4E2F0 / 1) and the expected speed (8.0f) with 0 |
| L126 | ADVANCED | Server-only text keys found: 1,906 CodeStringMappings keys appear in no client code, Lua, UI or AS3. The parameter-free ones (231) are the candidates for a bare text-id packet, e.g. Claim.SetTime.Error 577525 and Harvest.Fail.* |
| L156 | **FOUND** (except +0x4c) | +0x58 = homing target entity guid (re-resolved every frame), +0x64 = fixed homing point (wins over the guid), +0x60 = seconds for the turn rate to fade to 0. +0x4c (→ projectile +0x3c) has no reader |
| L160 | ADVANCED | ClientExecutedEffectData decoded: 14 u32, type hashes for the seven effect classes, and a 3-phase curve of AbilityParameterDefinitions ids + durations. The client gates casts on the effects. The 326 capture carries none |

## Server changes to make (from this pass)

1. **LightweightToFullNpc (L85)**: `Network/Message/Model/PlayerUpdateLightweightToFullNpc.cs` (and the same base
   fields in `PlayerUpdateLightweightToFullProp.cs`). Write the client constructor's defaults instead of 0 where the
   0.695 apply FUN_142d532f0 copies a field unconditionally [P]:
   - +0x1c = **0x00FFFFFF**, +0x20 = **0x00D4E2F0**, +0x24 = **1**. These go to entity +0x219c / +0x21a0 / +0x21a4,
     the chat-bubble foreground, background and size. Ctor FUN_142e88840 (part_0073:18181-18185) sets them through
     FUN_143b94310 = 0xffffff and FUN_143b941a0 = 0xd4e2f0. CommandPacketSetChatBubbleColor sets the same fields
     (part_0067:1442).
   - +0xb4 = **0x41000000 (8.0f)** → entity +0x740. The ctor default is 8.0. FUN_142ccd610 returns it × 1.0 (× 0.25
     with flag +0x145a & 8) as "Expected speed" (debug print FUN_142d0cc70), and the movement code FUN_142e69e50
     (part_0072:35434) scales a velocity by it. Today we send 0 [P]. Visible effect not tested [I]: creature animation
     or prediction speed.
   - Props only (`...ToFullProp.cs` writes 0 everywhere):
     - +0xb8 → actor +0x6b8 (default 100);
     - +0x248 (default 2);
     - +0x250 / +0x254 should be 0xFFFFFFFF. 0 differs from the "unset" DAT_1451f31c8 = 0xffffffff, so it overwrites
       entity +0x4144 / +0x4148, whose ctor value is DAT_145209114 = 0xffffffff.
   - Fields where 0 already equals the default (+0x140 / +0x258 guid: DAT_145441368 is BSS 0) need nothing.
2. **InstancedGame PropState / PropInteraction (L32)**: new writers in `Game/InstancedGames*.cs` for zone-control
   control points (prop 1224). The client puts the "InstancedGameObject" overhead indicator on those props (HudIndicators
   row 22, code type 27; FUN_143444620 picks 0x1b when the prop is in the PropState map).
   - **PropState** 0x10D / u32 sub **0x12** (0.589: 0x11). Reader FUN_1434dc150 / entry FUN_1434d77c0, handler
     FUN_1434e8ea0 [P]. Wire:
     - `bool clearAll` (+0x18: empties the map first);
     - `u32 n`, n × entry:
       - `u32 a`, `u32 b` (not read by the handler; send 0);
       - `str label` (replaced by the handler);
       - `u64 prop guid` (+0x20, the key);
       - `i32 controller` (+0x28): -1 = "Not Controlled" (or text InstancedGames.DefaultControlLabel), 0 = a
         free-for-all player, n ≥ 1 = team n, whose name comes from setting InstancedGames_TeamName<n> or
         InstancedGame.Teams.DefaultName_<n> (FUN_1434e7570);
       - **`u64 player` (+0x30, new in 0.695)**: with controller 0 the label becomes that player's name, looked up in
         manager +0x2c0 (the team-member map).
     - The indicator colour comes from FUN_1434f2b50: controller 0 AF7FAF, 1 0034F8, 2 FF0000, 3 D4AB1F, 4 00CC00,
       other FFFFFF.
   - **PropInteraction** 0x10D / sub **0x13** (0.589: 0x12). Reader FUN_1434d7570, handler FUN_1434e8d70 [P]. Wire:
     `u64 player (+0x18)`, `u64 prop (+0x20)`, `u32 start (+0x28)`, `u32 duration ms (+0x2c; ≤ 0 = cancel)`. This is
     the capture progress bar over the prop (FUN_1434f2c10: elapsed on the client's synced clock / duration, coloured
     by the player's team via manager +0x2c0).
   - Send PropState on round start, on capture (with the taker) and to joiners; send PropInteraction while a player
     stands on a point. [I: when live sent them. The client shows nothing for control points without them.]
3. **Auth LoginRequest names (L9)**: `LandmarkEmulator.AuthServer/Network/Message/Model/LoginRequest.cs` (and the
   Gateway copy). The remark "names … not proven" can go: FUN_143a57510 prints Session (+0x10), Fingerprint (+0x50),
   Locale (+0x90), ThirdPartyAuthTicket (+0xb8), ThirdPartyUserId (+0xf8), ThirdPartyId (+0x138) [P]. Optionally rename
   `SystemFingerPrint` → `Fingerprint`.
4. **Projectiles (L156)**: `Game/Projectiles.cs:25-28, 73-78`. Update the comments: +0x58 = homing target guid [P],
   +0x60 = homing fade seconds [P], +0x64 = homing point [P]. Behaviour is unchanged while +0x44 = 0 (no homing). To
   add homing shots, send +0x44 > 0 (turn rate, rad/s [I]), +0x58 = target guid, +0x64 = 0,0,0 and +0x70 = start time.
5. **Tooling, not server**: in re/tools/packetids.py, change the name pattern to `[\w:]+` and add the 0.695 registry
   (FUN_1434c7940 in part_0107) (L53). The scratch output is
   `scratchpad/l2a/packet_ids_695_full.tsv` (1,397 rows; scratchpad of this session).

---

## L9 — LoginRequest extra bytes: FOUND

- Already fixed 2026-10-15: the reader reads six strings (AuthServer LoginRequest.cs).
- **New [P]:** FUN_143a57510 (part_0135:4990) is PacketLoginRequest's field printer. It prints "Session" +0x10,
  "Fingerprint" +0x50, "Locale" +0x90, "ThirdPartyAuthTicket" +0xb8, "ThirdPartyUserId" +0xf8 and "ThirdPartyId" +0x138,
  the same offsets the writer FUN_143a54110 emits in order. So the names in our model are now proven; "SystemFingerPrint"
  is "Fingerprint".
- The credential values come from the credentials object (BaseClient `param_1[2]`, vtable +0x28) in FUN_143a5bcd0. The
  third string is the locale StringFixed<5>. Only Session is used by the server. Nothing left open.

## L28 — Inspect blob fields: ADVANCED

- **Radial entry [P]:** gameData695 RadialMenuButtons.txt row `75^3^7^563898^0^Ui.InspectPlayer` (menu type 3 = the
  player menu, the same menu as Add Friend 15, Trade 19, Group Invite 21 and Guild Invite 57; button slot 7; text
  563898 "Inspect", text_695). So "RadialMenuButtons +2" in packets-695 §2 is row 75, slot 7.
- **0.589 vs 0.695 [P]:** 0.589's parser FUN_142fe9cb0 (part_0082) uses the same offsets:
  - profile list at +0x114 (values > 1 add to +0x118);
  - u32 +0x110;
  - u32 +0x11c at the end.
  0.695 added LuminaryScore, NumberOfClaims, claim guids and bio between them. So +0x110 / +0x11c and the profile list
  are the older (2014-15) InspectProxy fields, not a 0.695 addition.
- **Consumers checked [P]:**
  - the InspectProxy vtables 144701380 / 144701468: no read of +0x110 / +0x11c / +0x118 in 0x143080000-0x143090000
    (scratch fgrep);
  - the proxy is stored at BaseClient +0x32e50. Its users are the dispatcher (part_0066:14919-14933), the tick
    FUN_14308ad10, StopInspecting (part_0067:10667-10695), and ControllerInspect FUN_143053c70 (reads proxy +0x3f8 and
    +0x30 for the camera target only);
  - the data source FUN_14308b5c0 (fields listed in packets-695 §2).
  None reads the three fields.
- **Server:** unchanged. Game/Inspect.cs sends 0 / empty, which is safe.
- **Next:** the profile list copies the actor's profile array from actor +0x1308 (FUN_142de67a0). Check whether the
  rank-sum +0x118 reaches any UI through ProfileDefinitions (ReferenceData 41 capture). A runtime read of proxy +0x110
  after a StartInspect would settle +0x110 / +0x11c.

## L31 — Game Finder: FOUND (client side)

- **u64 at +0x18 [P]:** the handler FUN_1434e9a50 reads only +0x20..+0x38, and the packet object dies after it. Already
  stated in InstancedGames.Finder.cs:21-31; confirmed by a re-read.
- **"Match found!" (state 1) [P]:**
  - it clears manager +0x402;
  - it looks key 0x565a5445 up in DAT_145431198 +0x2258 and passes the value to FUN_1431f7e90(DAT_145431358, …), a
    hash-table play by id: a sound / notification [I: the table at DAT_145431350 +0x70 keyed by id];
  - it prints chat type 7 with the kind bits (2 FFA, 4 Teams, 8 PvE, 0x10 PvP).
  Nothing moves the player.
- **What moves a matched player [P]:** only the existing InstancedGame Request S→C types: 4 left queue, 5 queue
  position (GAQ entry with Join Now), 6 membership. The S→C Request handler FUN_1434e93e0 knows no others. The
  server's Finder already queues the player and offers Join Now (InstancedGames.Finder.cs, InstancedGames.cs:917-940).
- **JoinTeam (Request type 9) [P]:** no answer type exists. TeamMembers (sub 0x15) and the scoreboard carry the result,
  as built (InstancedGames.Rework.cs:456-497).
- **Remaining (server design, not a wire question):** carrying a cross-world search to the other world (B.10 notes).
  The client side holds nothing more for this lead.

## L32 — Game Table rework fields: FOUND

Already proven and built before this pass (re-checked in code):
- AddOrRemoveRule u64 = the trigger;
- per-trigger u32 / u64 = rule type / trigger;
- recipe item u32 = ScriptPropId;
- HUD bool + u64 = team entry / player guid (InstancedGames.cs:1622-1659);
- Event tail = target name, source name, object text id, pointCount (InstancedGames.cs:2016-2034).

New:
- **Settings value +0x50 / +0x54 and the per-block string list are echoed only [P].**
  - The value reader FUN_1434d6b10 is {u32 +0, str +8, str +0x20, str +0x38, u32 +0x50, u32 +0x54}.
  - The UI builder FUN_1434f39a0 uses the value's key, SettingKey, SettingValue and SettingDescription, plus the
    IgGeneralSetting row's +0x48 / +0x4c / +0x50 / +0x54 / +0x58. It never reads the value's +0x50 / +0x54.
  - Apply (part_0107 ~30216) copies the whole block back into SetConfigurationRequest: FUN_1434dfc80, string list via
    FUN_142cabab0, value writer FUN_1434d5d60 writing +0x50 / +0x54.
  - Nothing in 0x1434c0000-0x143520000 reads manager +0x158 / +0x160 / +0x168 (the block's string list) apart from
    that copy.
  - So 0 / empty is correct; the server may ignore them on SetConfiguration.
- **PropState entry's new u64 = the controlling player's guid [P]** (handler FUN_1434e8ea0, part_0107 ~35140):
  - with controller +0x28 == 0 it looks +0x30 up in manager +0x2c0 and copies that player's name into the label;
  - -1 gives "Not Controlled";
  - n ≥ 1 gives the team name (FUN_1434e7570).
- **What PropState / PropInteraction drive [P]:**
  - FUN_1434ec6b0 (is the prop in the PropState map) is called from the overhead-indicator code FUN_143444620 (picks
    indicator type 0x1b = HudIndicators row 22 "InstancedGameObject", code type 27), FUN_143449e00 (fills label +
    colour via FUN_1434f2b50 and progress via FUN_1434f2c10), and FUN_143711b30 (a filter: prop is / is not a game
    object);
  - the PropInteraction handler FUN_1434e8d70 fills the progress map (+0x370);
  - coverage-695.md:2478-2479 shows both as "not sent". Server change in the summary (#2).
- **Remaining:** what PropState entry +0 / +4 are (the handler never reads them; [I] state / type, send 0). Check in
  game that the indicator appears over prop 1224 once PropState is sent.

## L35 — ShowActivationFailure texts: ADVANCED

- Built already: ItemUses.Refuse with the leads-L33-L35-L36.md table (ItemUses.cs:65-125, Claims.cs:698-706,
  RecipeBook.cs:360, Pets.cs:173).
- **New [P]:**
  - no 0.695 client code holds any of the refusal ids as a constant. A grep for 0x8cf54 / 0x8cf88 / 0x8d0a9-0x8d0ab /
    0x8cff6 finds nothing, so they can only arrive from the server;
  - a scan of every 0.695 text that is new since 0.589, looks like a refusal ("already", "cannot", "maximum" …) and has
    no CodeStringMappings key finds exactly: 577364, 577416, 577705, 577706, 577707 (all used by ItemUses), plus
    **577526** "You cannot change the Time Settings on this Build Site." That one duplicates 577525 =
    `Claim.SetTime.Error` and is a claim refusal, not an item one;
  - so the set of new item-use refusal texts in the server is complete.
- **Server-only keys [P literal scan, I meaning]:**
  - 2,233 of 2,599 CodeStringMappings keys are not literal strings in the 0.695 decompile. 1,906 are also absent from
    UI/, Resources/Scripts (incl. ScriptsBase.bin) and re/as3_695;
  - after dropping the UI.* / Ui.* labels, 231 of them take no parameters. These are texts the live server resolved and
    sent by id;
  - item-use relevant ones: Item.ErrorFailedItemRequirement 560768, Item.ErrorBadItem 560760, Item.ErrorFail 560759,
    Item.ErrorHas*Timer 560764-560767, Customization.OptionKnown 577178, Customization.TintKnown 577179,
    LootPinata.InventoryFullError 568125, Prop.PickupError* (560968, 562839, 571433, 571822, 574232);
  - caveat [I]: some keys may be built at run time (e.g. "Item.%s"), so the list may hold false positives.
- **Next:** none from the client. Only a capture shows live's choice between 0x1E and a chat / system line.

## L37 — Site-game +0x15c: FOUND (built)

- [P] The only read of `+ 0x15c` on that entry in 0x142d90000-0x142df0000 is FUN_142dc9660 (part_0069:20756,
  `< 2` → no Switch Team). The other hits are unrelated constructors.
- In 0x1434c0000-0x143520000 the hits are writes of 0x14 into other structs.
- The server sends the game's team count (InstancedGames.cs:445-456). Closed.

## L38 — not-compared classes: FOUND (closed 2026-10-15)

- Its closing note stands.
- Checked that the three 0.695 differences it found are in the server:
  - PickUpReply error 0xE (RewardChests.cs:27, 61, 110);
  - EditSettingsReply f64 records (PropSettings, coverage-695.md:204);
  - FromZoneToClient list elements stay empty (coverage-695.md:205).

## L53 — class ↔ sub for unreferenced classes: ADVANCED

- **Tool bug [P]:**
  - re/tools/packetids.py matches `"(\w+)"`, so registry calls whose name has a namespace (`Abilities::cAbility…`,
    `Items::cItem…`, `PacketInGamePurchase::cId…`, `VehicleBasePacket::c…`, `Skills::…`, `Currency::…`) are dropped;
  - in 0.589 (part_0106) 135 of the 1,681 FUN_143419ce0 calls have `::` names; packet_ids.tsv has 1,175 rows;
  - the 0.695 registry is FUN_1434c7940 (part_0107, 1,687 calls). With `[\w:]+` it yields **1,397 rows**
    (217 namespaced). The other 281 calls are top-level ids with `&DAT_144682b0c` names.
- **New class → sub [P registry row; name match exact unless marked]:**
  - Abilities::AbilityPacketAbilityLanding 0x27/0x1D;
  - CommandPacketDebrisLaunchPacket 0x1E/0x35; CommandPacketInteractionList 0x1E/0xB;
    CommandPacketInteractionVault 0x1E/0x50;
  - Currency::CurrencyPacketSetCurrencyDiscount 0xE5/1;
  - DesignBasedStateMachinePacketHandleTransitionAction 0x11A/1;
  - Items::ItemPacketSetItemLockTimer 0xE6/2, SetItemLineTimers 0xE6/3, SetItemLineTrialTimers 0xE6/5;
  - PacketInGamePurchaseStoreBundleCategoryGroups 0x45/6, StoreBundleCategories 0x45/7, StoreBundleGroups 0x45/0xA,
    EnableMarketplace 0x45/0x19;
  - PacketPlayerRewardCurrency 0x26/0x4A, PlayerInitiateNameChange 0x26/0x5F, PlayerNameChangeResult 0x26/0x60;
  - Skills::SkillPacketLoadSkillDefinitionManager 0xBC/5, SetSkillPointManager 0xBC/6;
  - VehiclePacketLoadVehicleDefinitionManager 0xBF/0x15, VehicleExpirationPacket 0xBF/0x22;
  - [I, fuzzy name]: HudMessagePacket ↔ cPlayerUpdatePacketHudMessage 0x26/0x33; PropReplyPacket ↔
    cAdminCommandPacketIdSpawnPropReply 0x1E/0x4EB.
- **Why the vtables are missing [P sample]:** for Grief::GriefPacketReset / SetGriefLevel, Groups::GroupPacketRemoveGroup,
  EncounterPacketEncounterShutdown, PacketClientServerShuttingDown, TargetPacketRemoveAllTargets and HudMessagePacket,
  the only RTTI slot is the destructor (e.g. FUN_142ee1630). It just resets to BasePacket::vftable and is never called
  directly. These are header-only classes whose stack copies the optimiser reduced to the header constants, so a
  dispatcher case shows only `{u16 base, sub}` (as Claims 0x12 LostClaimServerConnection did).
- **Correction to coverage-695 §11 [P]:** PacketInGamePurchaseWalletBalanceUpdate is referenced
  (part_0093:12361 `PacketInGamePurchaseWalletBalanceUpdate::vftable`), so it does not belong in the list.
- **Remaining (85 classes):** Grief, Groups, Encounter*, ClientActivityLaunch*, Gaq*, UramApi*, QuickChat*, Social*,
  Target, Raids, Gateway. Their families are not in either registry. Next: the header-constant cases of each family
  dispatcher, matched by the class's handler / string (e.g. the Grief dispatcher, part_0073 ~27160, sub 3 = Reset).

## L79 — MaterialChangeInfo 0xEC/6: ADVANCED

- **Trailing u64 [P]:** FUN_143f941b0 (part_0161:15539) acts only when `u64 != DAT_14560bdc0`. It then calls
  FUN_14401ecf0(cache +0x5a90, cell, +0x5210, &u64) for each listed cell; FUN_14401ecf0 creates a job through
  FUN_14408d780 (part_0164:12386 / 12405).
  - DAT_14560bdc0 is in .data BSS and nothing in the decompile writes it, so the "none" value is **0**.
  - With 0, only the block-event path FUN_1436bb0e0 runs (map +0x98, VoxelClientBlockEvent, RTTI 1447c4428).
- **u16 [P]:** written once at VoxelClientBlockEvent +0x70 (FUN_143667630).
  - No read of `+ 0x70` in 0x143660000-0x14366c000 except an unrelated struct.
  - The class's only vtable slots are 143666770 (dtor) and 142b4d2f0.
  - So the u16 is not consumed in that code.
- **Server:** we never send 0xEC/6, and nothing needs it. If it is ever used for a block change: u16 0; u64 0 to skip
  the per-cell job, or a non-zero transaction id to have each cell re-handled [I: refresh].
- **Next:** what FUN_14401ecf0's job (FUN_14408a770) does with the u64 (a cell re-request keyed by transaction?);
  readers of event +0x70 through the block-event owner (ZoneProcessingComplete / CellBlockDamage handlers outside the
  scanned range).

## L80 — Manifest flag: ADVANCED

- [P] HandleCellManifest FUN_144016680 clears impl +0x150 and fills it with FUN_143fac700:
  - entry +0 set, +4 flag, +8..+0x14 versions, +0x28 cell;
  - the HashList buckets are at +0x178, the count at +0x158.
- [P] The only keyed lookup of that map (same hash, `+0x178` / `+0x158 - 1`) in 0x144000000-0x144040000 is
  FUN_144014800 (part_0164:3525). It copies entry +0 (set) and +8..+0x17 (versions) and never touches +4.
- [P] The only other user, FUN_1440155c0, copies whole entries into a `VoxelFarm::Internal::ResetMementoImpl` (+0x1e0)
  through FUN_143f92c20 (refscan: single call at 0x143f92d1a). That is a snapshot for terrain reset; the memento's
  copy-assigns FUN_144010750 / FUN_1440107a0 copy entries wholesale.
- A byte-read scan (`(char *)(x + 4)`) of 0x143f80000-0x144040000 found two hits, neither on a manifest entry.
- So the flag is stored and copied but never read in the cache code [P within that range]. The live 1 / 0 values
  (capture) don't change client behaviour; ours (0) is safe.
- **Next:** a Frida read watch on one entry's +4 during a zone load, to rule out readers outside the range. Do it only
  on the test copy, per the harness rules.

## L85 — LightweightToFullNpc creature values: ADVANCED + server change

- [P] FUN_142d532f0 (0.695 apply) copies these unconditionally (part_0067 ~6180-6200):
  - +0x1c → +0x219c, +0x20 → +0x21a0, +0x24 → +0x21a4;
  - +0xb4 → +0x740, +0xe4 → +0x1498;
  - +0x22c → +0x4140, +0x238 → +0x149c, +0x23c → +0x14a0, +0x230 → +0x2198;
  - +0x94 → +0x158c, +0x244 → +0x14b0, +0x268 → +0x4aa8, +0x254 → +0x4148, +0x240 → +0x14b4;
  - +0x140 → +0x4050, +0x258 → +0x7d8;
  - +0x90 → +0x147c (already handled).
- [P] Defaults that differ from 0 (ProxiedCharacter ctor FUN_142e88840; scratch ctor scan):
  - +0x219c = 0xFFFFFF, +0x21a0 = 0xD4E2F0, +0x21a4 = 1 (chat-bubble colours, the PC model already names them:
    PlayerUpdateLightweightToFullPc.cs:33-37);
  - **+0x740 = 0x41000000 (8.0f)**;
  - +0x4144 / +0x4148 = 0xffffffff;
  - +0x147c = 2.
  The others are 0, or the BSS guid DAT_145441368 = 0.
- [P] +0x740 is the "Expected speed": FUN_142ccd610, debug print FUN_142d0cc70, used as a velocity scale in
  FUN_142e69e50. The 0xFFFFFF / 0xD4E2F0 pair is also the default line colour pair the chat-print path DAT_145431a10
  vtable +0x28 uses (part_0066:1221, part_0076:20285).
- **Server change:** summary #1. Live's per-creature values still need a capture (strings, attachments, resource
  setup).
- **Next:** check in game that a creature's bubble or chat line is readable once the defaults are sent; name +0x158c /
  +0x14b0 / +0x4140 from their readers.

## L126 — ZoneDisplayNotification texts: ADVANCED

- From the morning sweep: 562155 is Claim.LostClaimServerConnection, now sent with header-only 0xF9/0x12 (L142).
- **New [P literal scan]:** the server-only CodeStringMappings keys (L35 section). The parameter-free ones near the
  Environment / Construction / Claims services are the likely ZoneDisplayNotification texts [I: the packet carries a
  bare text id and the VoxelConstruction family belongs to the Environment Server]:
  - Claim.SetTime.Error 577525 (and its unmapped twin 577526);
  - Claim.StoryReset.Message 577089, Claim.StorySaveSnapshot.Message 577105;
  - Harvest.Fail.InClaim 562568, .NotAllowed 576183, .Prevent 560975, .Tier 560563, .Multiple 560976;
    Harvest.CastFailMessage 566166;
  - Prop.PickupErrorClaimNotInEditMode 574232, Prop.PickupErrorNoClaimPermission 571433;
  - Claims.DeleteFailedInventoryFull 560973;
  - InstancedGame.NoEntryWhileActive 564152, InstancedGame.ModifyErrorGameRunning 564516.
  None of these keys is referenced by client code, Lua or AS3. Texts with `[*…*]` (Construction.MissingProps 561618,
  Template.TooManyOfMaterialType 562806) can't travel as a bare id.
- **Server option [I]:** use ZoneDisplayNotification (bool set → system message) for those refusals where the server
  now prints a chat line (e.g. Claims.SetTime for a non-owner silently returns: Claims.cs:965-969).
- **Next:** a capture. Otherwise the bool's two client paths are already proven (system message vs. chat window).

## L156 — Projectile Create fields: FOUND (except +0x4c)

Arguments traced from FUN_14318fbe0 (part_0088) into the init FUN_1436d6830 and the base FUN_143c9fa70 (part_0146:33760):

| Wire | Projectile | Meaning [P] |
|---|---|---|
| +0x18 u32 | +0x10 | projectile id (when equal to DAT_145601e88 the client numbers it itself) |
| +0x48 f32 | +0x48 | max range (recomputed by FUN_1436d5f00 with the definition's +0x28 when gravity > threshold) |
| +0x4c vec3 | +0x3c..+0x44 | **no reader found** (projectile code 0x1436d5000-0x1436d8000 and 0x143c9e000-0x143ca2000) |
| +0x44 f32 | +0xa8 | homing turn rate; FUN_143c9fca0 turns the velocity toward +0x90 by at most rate × dt while +0x90 ≠ 0 and now > +0xb8 |
| **+0x58 u64** | +0x28 | **homing target entity**: FUN_1436d6f50 resolves it each frame (FUN_142f85390) to its bounds centre + model offset into +0x90; reset to none when the entity is gone |
| **+0x60 f32** | +0xb0, +0xac = +0x44 / it | **homing fade time (s)**: the turn rate drops by +0xac per second, reaching 0 after this many seconds; values < DAT_144c410c0 = 0.001 leave it constant |
| **+0x64 vec3** | +0x9c..+0xa4 | **fixed homing point**: when not 0,0,0 (sentinel DAT_145483028, BSS, no writer) FUN_1436d6f50 uses it instead of the entity |
| +0x70 u64 | +0xb8 | homing start time |
| +0x38 u64 | +0x20 | shooter (start bone, FUN_1436d5c40) |

- FUN_1436d7570 (end check) treats "no target guid and no homing point" as the plain-flight case.
- Impact effect: unchanged ([I strong], morning note).
- **Server:** comments only (summary #4).
- **Next:** +0x4c readers through ClientProjectile's vtable (RTTI not named; factory hash 0xd1064f33); a probe varying
  +0x4c.

## L160 — ClientExecutedEffectData for sprint / boot / glide: ADVANCED

- **Layout [P]:** CasterInfoReply reader FUN_142e016b0 reads the list at +0xf8 with FUN_142e04680. Each element
  (FUN_142e03a00) is 14 u32 at +0x00..+0x34.
- **Type ids [P]** (factory registration part_0070:25208-25250 → creators FUN_142dfd020 / dfd290 / dfd500 / dfc660 /
  dfc8d0 / dfcb40 / dfcdb0):
  - +4 = the effect type: 0x9055911d Glide, 0x1c3c381c ChanneledGlide, 0x80d81da0 Jump, 0x7f2bc64f DoubleJump,
    0x7faad620 Sprint, 0xf1fae31f ChanneledSprint, 0xea3d9ce0 Fly;
  - FUN_142e255e0 swaps Sprint → ChanneledSprint and Glide → ChanneledGlide in modes 2-3.
- **Parameters [P]** (FUN_14354b670, called by every effect's start, e.g. Sprint FUN_14354c1f0):
  - three phases, read as floats and cast to int:
    - +0x0c / +0x10 = start / end AbilityParameterDefinitions ids, +0x14 = duration (s);
    - +0x18 / +0x1c, +0x20;
    - +0x24 / +0x28, +0x2c;
  - each id is evaluated by the ParameterCalculator FUN_1435079e0; the curve is linear per phase;
  - +0x00, +0x08, +0x30, +0x34 are not read there.
- **Sprint effect [P]:** FUN_14354bbc0 sets entity +0x4cd0 (`plVar1 + 0x99a`) = the current speed and calls
  FUN_142ec3230(entity, 1), if entity vtable +0x428(0) allows it.
- **Cast gate [P]:** FUN_142e0f4f0 refuses a cast unless every listed effect's precondition holds: Glide → vt +0x2d0,
  Jump → +0x2b8, DoubleJump → +0x2c8, Sprint → +0x428(0), Fly → FUN_142e934b0. So adding effects can block casts that
  work today.
- **Capture [P, provenance unknown]:** the `[326]` body in MiscHandler.cs `capturedCasterInfo` decodes (re/caster_info.py)
  with list_f8 = 0. So does every captured reply (221, 1098, 326, 1122), but the 326 body has the Lightstone's fields
  (MiscHandler.cs:473), so it says little about live's sprint.
- **Server:** no change. No client table names which AbilityParameterDefinitions rows (314 Constant, 380 StatMultiplier
  … 747 rows, no names) belong to movement, so any values would be ours.
- **Next:**
  - in game: whether Shift (326) / Breeze Boots glide feel right without effects;
  - if not, build one Sprint effect {type 0x7faad620; phase 1 Constant rows for start / end speed; durations} from
    Constant rows (e.g. the 1.25 / 1.5 multipliers 16 / 36 / 38) and test.

## Scratch artefacts (session scratchpad, not in the repo)
- `l2a/packet_ids_695_full.tsv` — full 0.695 packet id registry incl. namespaced names (1,397 rows).
- `l2a/server_keys.txt`, `l2a/server_only_keys.txt` — CodeStringMappings keys absent from the client code (2,233) and
  also from Lua / UI / AS3 (1,906).
- `l2a/fgrep.py` — function-scoped regex grep over the decompile (byte-exact line numbers).
