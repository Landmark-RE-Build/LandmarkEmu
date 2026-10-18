# Wire audit, group b (2026-10-17)

Brief: [audit-wire-brief.md](audit-wire-brief.md). Read-only. Every packet in the group that the server writes or reads
was checked field by field against the 0.695 client (`re/decomp_all_695`, `pkt.py` / `fn.py`, writers read by hand
where `pkt.py` drops a call or a field). [P] = proven from the client function named; [I] = inferred.

Group: Claims 0xF9, Prop 0xED, UpdatePropInteractionState 0xFD, ObjectScripting 0x111 (story tools), prop settings
(EditSettingsReply 0xED/0x1C + 0xFD type 2), InstancedGame 0x10D, Template 0xF0, AreaDefinition 0xEA,
DynamicInteractionFlagging (PlayerUpdate 0x6B), Palette 0xF5, CharacterCustomization 0x11C / Barbershop,
ClaimVendor 0x112, Chest 0xFF / reward chests, pets. Also checked in passing because they are the props' own
PlayerUpdates: DisplayPropText 0x79, SetPropTint 0x7A, UpdatePropName 0x7C.

Notes on method:
- Header shapes [P]: Claims / Prop / Template / ObjectScripting / CharacterCustomization = u16 + u8 sub; InstancedGame /
  Chest / ClaimVendor / AreaDefinition = u16 + u32 sub (AreaDefinition gets 2 sub bytes from the enum `0xEA00ss00` and
  the other 2 from `AreaDefinitionHeader.Write`'s u16); Palette = u16 + i16 sub; PlayerUpdate = u16 + u32 sub. Every
  server writer / request model matches its family's width.
- Every PropBase S→C packet must carry the u64 player right after the header: the Prop dispatcher FUN_142fba9f0 only
  enters its switch when 8 more bytes follow the sub [P]. All server PropBase writers do.
- `pkt.py` silently drops some calls (a `FUN_142d4b5d0()` string read with elided args, a bool read before a tail call):
  EditClaimItemData's bool, the 4th string of the attachment record and the Palette update id were confirmed by hand.

## Table

| Packet | Server writer / reader (file:line) | Client reader / writer (0.695) | Verdict | Detail |
|---|---|---|---|---|
| **Claims 0xF9** | | | | |
| 0x01 SendAllClaimBasicInfo S→C | Game/Claims.cs:426 SendBasicInfo | FUN_142da9170, element FUN_142da1800 | OK [P] | u64, n × {3×u64, 3×u32, 6×f32, u32, bool, u32, u32}; trailing bytes rejected (param_4=1 at dispatcher, fine) |
| 0x04 AOIUpdate S→C | Claims.cs:859 BuildAoiUpdate (SendAll 172, placement 606, Grant 1778, AddServerSite 1801, SendDetachedSites 1811, 1881/1966/1988/2106) | FUN_142da0530 → records FUN_142da4150 (FUN_142da1a50), u64 list FUN_142da4850 | OK [P] | bool full, record list, u64 list |
| claim record (subs 4, 7, 9) | Claims.cs:986 WriteRecord | FUN_142da1a50 | OK [P] | id, parent, owner, str, u64, 3 str, u32, 6×f64, 3×u32 masks, {u64,u32} list, guild list, {u64,str,u32,bool} list (FUN_142da2120), u32, u64 admin, bool, u32, u32, list FUN_142da37e0, 5 str, u64, site-game FUN_142d9fc20 (InstancedGames.cs:442 WriteInfo: u64,u32,bool,u32×5,bool,u32,str,u32 list,u32 — matches incl. the 0.695 trailing team-count u32), u64, str, list FUN_142da3dc0, time 4×u32 (i32/f32), u32, u32 mode, list FUN_142da3aa0, u64 adventure flags |
| 0x05 ClaimInfoUpdate S→C | Claims.cs:1238 BuildInfoUpdate | FUN_142da89a0 → FUN_142da2840 | OK [P] | u64, u32 kind, str, u32, 3×u32 (FUN_142da03d0), u64, 4×u32, u64, 5 str, u64, site-game, u64, str, list, time, u32 music, u32 mode, u64 flags |
| 0x06 PlaceClaimResponse S→C | Claims.cs:475 | FUN_142da0f40 | OK [P] | u32, u64, u32, u32, u32, u32, u64 |
| 0x07 PlaceClaimComplete S→C | Claims.cs:845 SendComplete | FUN_142da8ee0 | OK [P] | u32 result + record (refusal sends `new ClaimModel()`; its strings default to "", no null) |
| 0x08 ClaimManagmentData S→C | Claims.cs:623 | FUN_142da07a0, item FUN_142da1fd0 | OK [P] | u64, n × {bool, u64, str, bool, u64, str, u32, u32} |
| 0x09 EditClaimItemData S→C | Claims.cs:832 SendEditData | FUN_142da0a40 | OK [P] | u64, bool (pkt.py misses it; raw read), record |
| 0x0A UpkeepManagmentData S→C | Claims.cs:713 | FUN_142da12f0 | OK [P] | u64 + 11 × u32 |
| 0x0D SendInventoryDetails S→C | Game/ClaimInventory.cs:220 | FUN_142da0d70, list FUN_142da3c70, element FUN_142d9fe40 | OK [P] | u64, u64, n × {u64, u32×5, bool, u32, u64, u32, u32} |
| 0x0F SendClaimsByOwner S→C | Claims.cs:882 | FUN_142da8d00, list FUN_142da3b70, record FUN_142d9f870 | OK [P] | u64 owner, n × {u64, str, u64, 3 str, 6×f64, 6×u32, list, u64, 5 str, u64, list, list} |
| **0x11 RecievePropExclusionArea S→C** | **Claims.cs:387** | FUN_142da0c00 (u32, list FUN_142da4590, list FUN_142da46f0) | **MISMATCH [P]** | server writes `u32 0, u32 0` (8 bytes) = u32 + ONE list count; the client reads u32 + TWO list counts. The second count read runs off the end → error flag → handler FUN_142dd33f0 never called (dispatcher case 0x10). Player impact low: with empty lists the handler would do nothing anyway. |
| 0x12 LostClaimServerConnection S→C | Claims.cs:815 | FUN_142da8e50 (header only) | OK [P] | |
| 0x18 ContestEntryResults S→C | Game/Contests.cs:236, WorldChannel/RemoteCommerceService.cs:784, CommerceServer/ClientPackets.cs:230 | FUN_142da8ad0 → FUN_142da08c0 | OK [P] | bool, str, str |
| 0x30 DeleteClaimItemReply S→C | Claims.cs:1145 | FUN_142da0980 | OK [P] | u64, u32 |
| 0x61 UpdatePropCategoryCount S→C | Game/PropLimits.cs:71 | FUN_142da9090 → FUN_142da11c0 | OK [P] | u64, n × {u32,u32,u32}; sub 0x61 (0.695 number) |
| 0x19 / 0x1C / 0x20 / 0x27 / 0x35 / 0x44 / 0x46 C→S | Claims.cs:249 HandleRequest | writers FUN_142da8080 … | OK [P] | bodies not read (or u64 only) |
| 0x1A PlaceClaimRequest C→S | Claims.cs:277 | FUN_142da6750: hdr, 3×8 (f64 centre), 4×u32 | OK [P] | server reads the 3 f64 |
| 0x1B PlaceClaimConfirm C→S | Claims.cs:280 | FUN_142da6560: u8 | OK [P] | |
| 0x1E RequestEditClaimItemData C→S | Claims.cs:349 | FUN_142da69e0: u64, u8 | OK [P] | |
| 0x2A / 0x2B / 0x2C SetItemName / Description / Tags C→S | Claims.cs:301-324 | FUN_142da7520 / 7320 / 7ad0: u64, str, u8 | OK [P] | |
| 0x2D SetItemBasicPermissions C→S | Claims.cs:325 | FUN_1435e6f40 → FUN_1435e6760: u64, u32, u32, u32, u8 (Lua args 2/3/4, FUN_1435e4180) | OK wire / semantic gap [P] | server reads only the first mask and writes the same mask to all three record slots (WriteRecord 1007): the 2nd and 3rd default-access masks the client sends are dropped. Not a parse error. |
| 0x2E SetItemIndividualPermission C→S | Claims.cs:331 | FUN_1435e68c0: u64, u8, str, u8, u32, u8 | OK [P] | |
| 0x2F DeleteClaimItem C→S | Claims.cs:290 | FUN_142d9ebc0: u64, u8, u8 | OK [P] | |
| 0x40 SetClaimTime C→S | Claims.cs:354 | FUN_142da7120: u64, FUN_142d9f570 (4×u32), u8 | OK [P] | |
| 0x41 SubmitContestEntry C→S | Claims.cs:368 → Contests.cs:187 / CommerceServer | FUN_142da7e70: u64, str, list FUN_142da35a0 of {u32 len, bytes} (FUN_142da2e50), u8 | OK [P] | |
| 0x42 / 0x43 C→S | Claims.cs:363 / 293 | FUN_142d9f290 / FUN_142d9efd0: u64, u32, u8 | OK [P] | |
| 0x45 SetAdventureFlags C→S | Claims.cs:263 | FUN_142da6bd0: u64, u64, u8 | OK [P] | |
| 0x60 DebugAddItemClaim C→S | ClaimInventory.cs:132 | FUN_14338caf0 → FUN_143380850: u32×3, u64, u64, u32, u32, u8 | OK [P] | |
| 0x23 ClaimRequestMoveItemToPlayer C→S | ClaimInventory.cs:177 | FUN_14337e490 → FUN_14338c930: u64×3 | OK [P] | |
| 0x63 PlayerEnteredClaim C→S | Claims.cs:258 | FUN_142da8230: u64 | OK [P] | |
| **Prop 0xED** | | | | |
| 0x02 PropData / refusal S→C | Game/Props.cs:915 BuildPropData, 1064 BuildRefusal (sent 558/564/620/759/1558/2291) | FUN_142fa7b40 (reply base FUN_142fa8940 + body) | OK [P] | base {u64 player, bool ok; if !ok: u32 err, u64}; u64, u32, str, 5×u32, bool, 4×f32, u32, u32, u8, 2 settings lists, str, u32, u32 |
| 0x04 SaveReply S→C | Props.cs:1141, 1216, 1241 | FUN_142faae10 = base + u32 + u64 | OK [P] | success writes `true, 0u, guid`; refusal `false, err, 0ul, 0u, guid` — both consume exactly |
| **0x06 PickUpReply / 0x08 DeleteReply S→C** | **Props.cs:1662-1670** (PickUp) | dispatcher FUN_142fba9f0 case 6 / 8: base FUN_142fa8940 (bool; u32 err + u64 only when false) then ONE u64 prop guid | **MISMATCH [P]** | server always writes `u64 player, bool ok, u32 error, u64 guid`. ok=true: the client reads the u32 0 + 4 guid bytes as the guid → FUN_142fbbb20 (pick-up effect) / FUN_142fba6c0 get a wrong guid. ok=false: the guid is eaten as the base's u64, the final u64 is missing → no error text ever shows (inventory full 3, vendor stock 9, no permission 10, chest soulbound 11, Play Mode 13, reward chest 0xE "PartialLootChest" all silent). |
| 0x0A prop definition list S→C | Network/Handlers/MiscHandler.cs:341 (live dump) | FUN_142faa9b0 | OK [P, earlier parse of the dump] | |
| 0x10 UndoRedoNotification S→C | Props.cs:384 UndoMove | FUN_142fa86c0 | OK [P] | u64 player, u64, 3×4×f32, u32 |
| 0x15 EditPropTextReply S→C | Props.cs:1447 SendText | FUN_142fa8500 | OK [P] | u64 player, u64, str, u32, bool |
| 0x17 AvailableTintsReply S→C | Props.cs:1265 (live dump, player patched at offset 1) | FUN_1435336e0 (raw) | OK [I: captured from live] | |
| 0x1C EditSettingsReply S→C | Game/PropSettings.cs:567 SendSettings + 626 WriteRecord | FUN_142fa85d0: u64 player, u64, bool, list FUN_142c364f0 {u32 key, record FUN_142c15ee0}, list FUN_142c36fe0 {u32 key, record, options FUN_142c391d0 {u32,u32,bool,str}}, str, str | OK [P] | record: str, u32, str, 5×f64, u32 |
| 0x01 Access C→S | Network/Handlers/PropHandler.cs:28 | FUN_142fa9500: u64 prop (+0x18), u64 player claim (+0x20), u32 | OK [P] | first u64 is the prop (part_0079 :33403) |
| 0x03 Save C→S | Props.cs:1103 (read from the end) | FUN_142fa7940: u32, u8, u64, u32, 6 vectors, u8, u64, u8 | OK [P] | |
| 0x05 PickUp / 0x07 Delete C→S | PropHandler.cs:49 | FUN_142faa220: u64, u8 / FUN_142fa9700: u64 | OK [P] | |
| 0x0F Duplicate C→S | PropHandler.cs:61 | FUN_142fa98b0 → FUN_142fa7550: u64 prop, u64, u8, u8 | OK [P] | |
| 0x12 PlacePropById C→S | PropHandler.cs:32 | FUN_142fa9c00: u32 | OK [P] | |
| 0x13 UpdatePropText C→S | PropHandler.cs:65 | FUN_142faa070: u64, str | OK [P] | |
| 0x14 EditPropText C→S | PropHandler.cs:69 | FUN_1435fc680: u64 | OK [P] | |
| 0x16 SetTint C→S | Props.cs:1331 | FUN_143531a80: list<u64> (FUN_1435314a0), u64, u32 | OK [P] | |
| 0x18 AvailableTints C→S | PropHandler.cs:57 | FUN_1435318e0 | OK | no body read |
| **0x19 SetPropName C→S** | **PropHandler.cs:79-81** | FUN_1435fca20: **str name (+0x18), u64 prop (+0x30)** (built part_0113 :25899-25906; 0.589 FUN_14354a200 the same) | **MISMATCH [P]** | server reads `u64 guid, string`: the guid comes from the string's length + first 4 chars, so `Editable()` never finds the prop and renaming a prop never works (on either client build). |
| 0x1A SetPropEnable C→S | PropHandler.cs:77 | FUN_1435fc830: u64, u8 | OK [P] | |
| 0x1B EditSettingsRequest C→S | PropHandler.cs:81 | FUN_142fa9a50: u64 | OK [P] | |
| 0x1D DialogResponse C→S | PropHandler.cs:85 | Lua FUN_1435fbf80 / FUN_1435fc030 | OK [I: per handler comment, not re-read] | u64, bool |
| **Prop PlayerUpdates** | | | | |
| PU 0x79 DisplayPropText S→C | Props.cs:191 WritePropText (sent 158, 181) | dispatcher FUN_142cdfa60 case 0x78 → FUN_142c4be40 → FUN_142c27f40 | OK [P] | u64 question (+0x18), u64 prop (+0x20), str, u32, u32, bool |
| PU 0x7A SetPropTint S→C | Props.cs:1356 / 1520 | case 0x79 → FUN_142c4d540 (exact) | OK [P] | u64, str |
| PU 0x7C UpdatePropName S→C | Props.cs:1943 PropNameUpdate (sent 1418/1490/1922) | case 0x7b → FUN_142c4de60 (exact) | OK [P] | u64, str |
| **UpdatePropInteractionState 0xFD** | | | | |
| S→C (type 2 state, effects, animations) | Game/StoryTools.cs:779 StateBody; Game/PropEffects.cs:216 Body; Game/PropAnimations.cs:55 Send | zone dispatcher case 0x7f (part_0066 :15571) → FUN_142c2e170: u16, u64, u32 type, u32 key, {u32 value, u32 prev, u64, list<u32> (FUN_142c34b60), list<str> (FUN_142c3b970)}, bool | OK [P] | all three bodies match (animations: list of 4 u32 + empty string list) |
| C→S | Game/PropSettings.cs:659 Handle | FUN_1430a7250 → FUN_1430a6d60: u16, u64, u32 type, u32 key, {u32 value, u32, u64, list, list}, u8 | OK [P] | server reads guid, type, key, value |
| **ObjectScripting 0x111** | | | | |
| S→C 1..0xB | Game/StoryTools.cs:2152-2390 (Send 2384) | FUN_142f45700, 43120, 42fc0, 42e40, 42730, 42870 (link FUN_142f42470: u64,u64,u32,u64,u32,u32,u32, 2×4 u32), 42cc0, 42c00, 42d80, 429d0, 42a90 | OK [P] | header u16 + u8 |
| C→S 0xC..0x16 | StoryTools.cs:2152 Handle | FUN_142f45060 … 44460 (header FUN_142f41e70 w2+w1; 0x10 FUN_142f41f40: u64 src, u64 tgt, u32 event, u32 action; 0x15 FUN_142f42050: u64, u32, u32; 0x16 FUN_142f42120: u64 id, u64, u64, u32, u32) | OK [P] | |
| **InstancedGame 0x10D** (u16 + u32 sub; dispatcher part_0107 :39332 switches on sub−2, most cases require exact length) | | | | |
| 0x02 AdminMonitor S→C | Game/InstancedGames.Monitor.cs:94 (sent AdminMonitor.cs:132) | FUN_1434d6be0 | OK [I] | n × {str, str, u32}; contents ours (OPEN-LEADS L81) |
| 0x03 Scoreboard S→C | InstancedGames.cs:2013 BuildScoreboard | FUN_1434d7b40 (teams FUN_1434d8510, players FUN_1434d8420) | OK [P] | |
| 0x04 PropRecipe S→C | InstancedGames.Editor.cs:98 SendRecipe | FUN_1434d7680, item FUN_1434d7f90 | OK [P] | incl. 0.695 trailing u32 ScriptPropId |
| 0x06 State S→C | InstancedGames.cs:1650 | FUN_1434d7c80 | OK [P] | u32, u32, u32 ms, u64 |
| 0x07 Event S→C | InstancedGames.cs:2120 + WriteEventTail 2142; Rework.cs:782 SendPointEvent | FUN_1434d6eb0 | OK [P] | u32,u32,u32,u64,u64,str,str,u32,u32 |
| 0x08 Request S→C | InstancedGames.cs:1631 SendMembership; InstancedGames.Queue.cs:89 | FUN_1434d7990 | OK [P] | 5×i16, u64, u32, str |
| 0x09 HUD S→C | InstancedGames.cs:1883 SendHud | FUN_1434dbe10 → FUN_1434d7120 (entry FUN_1434d8180, bars FUN_1434d8360) | OK [P] | entry incl. 0.695 bool + u64 tail |
| 0x0E GetConfigurationResponse S→C | InstancedGames.Editor.cs:454 | FUN_1434dbb20: 2 × {u32, str, u32, str, settings FUN_1434d9330 {u32 key + FUN_1434d6b10 u32,str,str,str,u32,u32}, str list}, triggers FUN_1434d9600 {u64 key, defs FUN_1434d8700, values FUN_1434d85d0, u32, u64} | OK [P] | |
| 0x12 PropState S→C | InstancedGames.cs:1803 | FUN_1434dc150: bool, list FUN_1434d77c0 {u32,u32,str,u64,u32,u64} | OK [P] | |
| 0x13 PropInteraction S→C | InstancedGames.cs:1852 | FUN_1434d7570 | OK [P] | u64,u64,u32,u32 |
| 0x15 TeamMembers S→C | InstancedGames.cs:2092 | FUN_1434dc380, element FUN_1434d7e10 | OK [P] | |
| 0x16 ObserverRegistration S→C | InstancedGames.cs:1073 Observe | FUN_1434d7400 | OK [P] | u32, u64, u32 |
| 0x1A MatchmakingUpdate S→C | InstancedGames.Finder.cs:607 | FUN_1434d7270 | OK [P] | u64, 5×u32, u64 |
| 0x05 / 0x10 / 0x11 C→S | Editor.cs:203 HandleEditor | FUN_1434daad0 / db7e0 / da430: u64 | OK [P] | |
| 0x08 Request C→S | InstancedGames.cs:897 | FUN_1434db4a0 → FUN_1434d6470: 5×u16, u64, u32, str | OK [P] | |
| 0x0A PropRequest / 0x0B PropRemove C→S | Editor.cs:244 HandleEditorProp | FUN_1434d62d0: u64, 5×u32 / FUN_1434d61b0: u64, u64, 3×u32 | OK [P] | |
| 0x0D GetConfigurationRequest C→S | InstancedGames.Rework.cs:416 | FUN_1434da5e0: u64 | OK [P] | |
| 0x0F SetConfiguration C→S | Editor.cs:563 | FUN_1434d6620: u64, {u32,str}, {u32,str}, settings FUN_1434d8bd0, str list, triggers FUN_1434d8cd0 (FUN_1434d6870), u8 | OK [P] | |
| 0x16 ObserverRegistration C→S | InstancedGames.cs:900 | FUN_1434da930 → FUN_1434d60b0: u32, u64, u32 | OK [P] | |
| 0x18 AddOrRemoveRule C→S | Rework.cs:424 | FUN_1434da290 → FUN_1434d5e90: u64, u32, u8, u64 | OK [P] | |
| 0x19 MatchmakingRequest C→S | Finder.cs:169 | FUN_1434da790 → FUN_1434d5fd0: u64 flags (+0x18), u64 player, u8, u8 | OK [P] | |
| **Template 0xF0** | | | | |
| TemplateBasicData | Game/Templates.cs:494 WriteBasicData | FUN_1430ff530 | OK [P] | 0.695 shape (no PropData list inside; two record lists, bool, str, bool, u32×3, bool, str, u32, bool, u32) |
| 0x02 ListResponse S→C | Templates.cs:409 | (reader by element FUN_1430ff530) | OK [P elements; I list header u32 n … u64 as in 0.589] | |
| 0x03 SaveTemplateResponse S→C | Templates.cs:124, 611 Store | FUN_143102bd0 | OK [P] | bool + BasicData |
| 0x05 VoxelDataResponse S→C | Templates.cs:763 | FUN_1430fff20: u64, BasicData, blocks FUN_143101860, PropData FUN_142fa9070 (element FUN_142fa7fb0 = BuildingClipboard.cs:327 WritePropData), images FUN_1431017a0, bool | OK [P] | |
| 0x07 DeleteResponse S→C | Templates.cs:916 | FUN_1430ffc60: u64, u64, bool, bool | OK [P] | |
| 0x0B RenameError S→C | Templates.cs:951 | FUN_1430ffa70: u32, u64 | OK [P] | |
| 0x0C UpdateTemplateResponse S→C | Templates.cs:447, 951 | FUN_143102c70 | OK [P] | BasicData |
| 0x11 AddOwnershipResponse S→C | Templates.cs:100 | FUN_1430ffb30: u64, u64, bool, u64, str | OK [P] | |
| 0x1E PurchaseTemplateMaterialsDataResponse S→C | Game/DesignMaterials.cs:83 | FUN_143100050: u64, list FUN_1430ff330 (8×u32, max 100), u32 | OK [P] | |
| C→S 1, 4, 6, 0xA, 0x1B, 0x1D, 0x1F | Templates.cs:728 Handle | FUN_143101eb0 (hdr FUN_1430fdeb0 w2+w1, u64) … | OK [P header; I bodies per Templates.cs comments] | |
| **AreaDefinition 0xEA** | | | | |
| 0x01 SetDefinitions S→C | Game/AreaDefinitions.cs:238 + Model/AreaDefinitionMessages.cs Write | part_0098 :1655: FUN_1432f7fd0 header (u16, u32 sub, u64, bool), FUN_1432f8590 list of FUN_1432f76a0; exact | OK [P] | |
| 0x03 UpdateDefinition / 0x04 RemoveDefinition S→C | AreaDefinitions.cs:254 / 265 | case 3: header, record, bool, exact; case 4: header, record, exact | OK [P] | |
| 0x06 RequestResponse S→C | AreaDefinitions.cs:284 | FUN_1432f8e60: header, u64, bool, u32 | OK [P] | |
| 0x08 CreateTriggerAreaResponse S→C | AreaDefinitions.cs:300 | FUN_1432f78d0: header, bool, u32, u64 | OK [P] | |
| record / shape / property | AreaDefinitionMessages.cs | FUN_1432f76a0, shape FUN_1432f7e40, property FUN_1432f7bb0 | OK [P] | |
| C→S 2, 3, 4, 7 | AreaDefinitionMessages.cs:314-380 | FUN_1432f89b0, 8b70, 8800, FUN_14319eb10 | OK [P] | |
| **DynamicInteractionFlagging** PU 0x6B S→C | Network/Message/Model/InteractionMessages.cs:53 (Creatures, LootChests, Outposts, Props, Spire, StoryTools, WarpNetwork) | FUN_142c4beb0: u16, u32 sub, bool, list<u64> | OK [P] | |
| **Palette 0xF5** | | | | |
| 0x05 PaletteUpdate S→C | Game/Palettes.cs:44 Send | part_0076 :11902: u16, i16 sub, FUN_142c25c60 {str, u64 id, list FUN_142c35af0 {u32 key + FUN_142c25d30 u32,u32,u64}, bool, u32}; exact | OK [P] | |
| 0x03 AddEntry / 0x04 DeleteEntry / 0x07 C→S | Palettes.cs:78 | FUN_142f10de0 → FUN_142f10740 (hdr w2+w2, u64, u32, u32, u64) / FUN_142f112d0 (u64, u32) | OK [P] | |
| **CharacterCustomization 0x11C / Barbershop** | | | | |
| 0x04 EquippedAttachmentReply S→C | Game/Barbershop.cs:122 + WriteWorn 155 | FUN_1430a0cc0, element FUN_142c185c0 (4 str, 4×u32, bool) | OK [P] | |
| 0x01 PlayerSet C→S | Barbershop.cs:41 | FUN_1430a0a80 → FUN_1430a0530: hdr (u16+u8), u32, u64, customization FUN_142f59020, u32, u32 | OK [P] | |
| 0x03 EquippedAttachmentRequest C→S | Barbershop.cs:44 | (body unread) | OK | |
| PU 0x1B CustomizationChange S→C | Barbershop.cs:139 | PlayerUpdate group | – | group a |
| **ClaimVendor 0x112** (off by default, VendorShops) | | | | |
| 0x01 / 0x03 / 0x05 / 0x08 S→C | Game/Vendors.cs:150, 211, 232, 295 | FUN_143379450 / FUN_143378720 / FUN_143378520 / FUN_143378620 | OK [P sizes] | 0x05: five u32, the client stores the first at +0x2c [I meaning] |
| 0x02 / 0x04 / 0x06 / 0x07 C→S | Vendors.cs:184 | FUN_143378f00 (u64, u32, u64, u32) / FUN_143378b70 (u64, 3×u32) / FUN_143378d10 (u64, u64, u32) | OK [P] | |
| **Chest 0xFF** | | | | |
| 0x01 EnableWindow S→C | Game/Chests.cs:111 | part_0072 :10484 inline: u16, u32, u64; exact | OK [P] | |
| 0x04 InventoryListResponse S→C | Chests.cs:139 | FUN_142e470c0; exact | OK [P] | u32, u64, u64, u64, u32, u32, bool, u32 len + blob |
| 0x05 / 0x06 S→C | Chests.cs:337 / 355 | FUN_142e46e40 (u64, u64, blob) / FUN_142e46f50 (u64×3); exact | OK [P] | blob item records: inventory group |
| 0x02 / 0x03 C→S | Chests.cs:116 | FUN_142e476f0 → FUN_142e46ca0 (w2 + w4), u64 | OK [P] | |
| **Reward chests** | Game/RewardChests.cs | no packets of their own: settings rows go through 0xED/0x1C (OK), notices through SystemMessages, pick-up refusal 0xE through PickUpReply | see PickUpReply | the 0xE "partial loot chest" refusal is one of the silent errors |
| **Pets** | Game/Pets.cs:244, 463, 486 | PlayerUpdate AddLightweightNpc / RemovePlayer, Inventory ActivateItem, Abilities effects | – | no pet-family packets (0x39 unused by 0.695); the PlayerUpdate / Abilities bodies belong to group a |

## FIX list (ranked by player impact)

1. **Prop 0x06 PickUpReply / 0x08 DeleteReply** — `LandmarkEmulator.WorldServer/Game/Props.cs:1662-1670` (in
   `PickUp`). The reply base (FUN_142fa8940) carries `u32 error, u64` only when `ok` is false, then one `u64` prop guid
   [P: dispatcher FUN_142fba9f0 cases 6 / 8]. Replace

   ```csharp
   writer.Write((byte)(delete ? 8 : 6));
   writer.Write(owner.PlayerGuid);
   writer.Write(ok);
   writer.Write(ok ? 0u : stocked ? 9u : playing ? SiteInPlayMode : !allowed ? NoClaimPermission
       : paidOut ? RewardChests.PartialLootChestError : 0u);
   writer.Write(guid);
   ```
   with
   ```csharp
   writer.Write((byte)(delete ? 8 : 6));
   writer.Write(owner.PlayerGuid);
   writer.Write(ok);
   if (!ok)
   {
       writer.Write(stocked ? 9u : playing ? SiteInPlayMode : !allowed ? NoClaimPermission
           : paidOut ? RewardChests.PartialLootChestError : 0u);
       writer.Write(0ul);
   }
   writer.Write(guid);
   ```
   Effect: refused pick-ups finally show their client text (vendor stock, no permission, Play Mode, reward chest
   already looted), and a successful pick-up gets the right guid (FUN_142fbbb20's pick-up effect at the prop).
   Verify in game: try to pick up a prop on someone else's site → "Prop.PickupErrorNoClaimPermission" text.

2. **Prop 0x19 SetPropName (C→S)** — `LandmarkEmulator.WorldServer/Network/Handlers/PropHandler.cs:79-81`. The client
   writes `string name, u64 prop` [P: FUN_1435fca20 lines 61-62; packet built part_0113 :25899-25906]. Replace
   ```csharp
   case 0x19:
       if (request.Body.Length >= 12)
           Game.Props.SetName(session, reader.ReadULong(), reader.ReadString());
   ```
   with
   ```csharp
   case 0x19:
       if (request.Body.Length >= 12)
       {
           string name = reader.ReadString();
           if (reader.BytesRemaining >= 8)
               Game.Props.SetName(session, reader.ReadULong(), name);
       }
   ```
   (and fix the doc comment at Props.cs:1401 to `{string name, u64 guid}`). Effect: naming a prop (PropDefs
   AllowUserRename rows) works; today it is silently ignored. Same order in 0.589 (FUN_14354a200), so it never worked.

3. **Claims 0x11 RecievePropExclusionArea** — `LandmarkEmulator.WorldServer/Game/Claims.cs:387`. Add the second list
   count:
   ```csharp
   SendSimple(session, 0x11, w => { w.Write(0u); w.Write(0u); w.Write(0u); });
   ```
   (u32, list FUN_142da4590, list FUN_142da46f0 [P]). Effect today: the answer is dropped by the client's error flag;
   with empty lists its handler (FUN_142dd33f0) would do nothing either, so this only makes the reply valid. Update
   the comment at Claims.cs:386 to "{u32, two empty lists}".

Semantic (not a wire mismatch), for the Claims owner:

4. **Claims 0x2D SetItemBasicPermissions** — `Claims.cs:325-329` reads only the first of the three u32 masks the
   client sends (FUN_1435e6760: +0x20, +0x24, +0x28 from Lua args 2-4, FUN_1435e4180) and `WriteRecord`
   (Claims.cs:1007-1008) echoes one mask into all three record slots (+0x2d0/+0x2d4/+0x2d8). If the Site Management
   window sets the three default-access levels separately, the 2nd and 3rd are lost. [P wire; I which audience each
   slot is — the record comment says everyone / friends / guild.] Fix needs a model change (three masks on
   `ClaimModel`); not done here.

Everything else in the group matches the 0.695 client field for field.
