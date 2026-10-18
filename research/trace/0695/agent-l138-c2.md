# L138 part C2: skill, stat, item and content tables on 0.695

Agent report, 2026-10-17. Scope: OPEN-LEADS L138, tables-unread-695.md §C. Client 0.695 (`re/decomp_all_695`,
`<0.695 test copy>`), data `gameData695`, UI `re/as3` (0.589, unchanged movies) + `re/as3_695` (the 29 new / changed
movies), UI Lua `<0.695 test copy>\UI\Default\*.lua` + `UI\ScriptsBase.bin`, shipped `Resources\Scripts`.
Proof marks: [P] proven (address, row, file:line, source); [I] inferred (reason given).

## Summary

| Table group | Verdict | What the server must do |
|---|---|---|
| Skills / SkillLines / SkillSets / SkillCategories / ProfileSkillSets | **Client loads them. The live server sent the definitions (test data); no UI uses them.** | Nothing new. Keep the captured LoadSkillDefinitionManager (WorldSession.cs:512). Skill packets stay parked. |
| ProfileSkillSet.txt (no "s") | **Not loaded by the 0.695 client** (PS2 server data) | nothing |
| ItemGroups / ItemGroupMappings | **No 0.695 path reaches the client side.** Server-side item bundles: group 6 is the starter tools. | Optional: tool resupply from group 6 (see §2), lead L138-C2-b |
| StatCategoryDefinitions | **Client-only, live code**, but the result is empty | nothing |
| CharacterStatDefinitions | **Client loads it, but the reader is never called.** It is server data (SEND_TO_REMOTE_CLIENT). | Open: what remote players need (lead L138-C2-c) |
| AttributeDefinitions / AttributeEntryDefinitions | **Not live on 0.695.** This was the 2015 progression system. | nothing (do not send UpdateAttributes) |
| AbilityParameterDefinitions | **Client-only, live code.** Tooltip tokens are not used by 0.695 weapon texts. Client-executed movement effects use it. | Stat 10 / life already correct. Open: executed-effect lists in CasterInfoReply (lead L138-C2-a) |
| ContentPacks | **Client-only** (asset loader gating, env "MAIN") | nothing |
| PropRecipes | **Live**, but it is only an id list. The server already reads the linked tables. | nothing |
| CraftingStationCatalystMappings | **Not reachable on 0.695** | nothing |
| VehicleArmorMappings | **Not loaded by the 0.695 client.** Vehicles.txt is empty (PS2). | nothing |
| FacilityZoneMap | **Not loaded by the 0.695 client.** All Facility* tables are empty (PS2). | nothing |

---

## 1. Skills / SkillLines / SkillSets / SkillCategories / ProfileSkillSet(s)

**Verdict: the client loads them, and the live server sent the skill definitions at login. No 0.695 UI shows skills,
and the player's profiles have no skill set.** Landmark had no skill trees.

Evidence:
- Loader FUN_143bf4c90 (part_0143 ~20490) loads SkillPointRates, SkillSets, SkillCategories, SkillLines, Skills,
  ProfileSkillSets, FactionSkillSets, VehicleSkillSets, Leadership and LeadershipSkillSets into
  Skills::SkillDefinitionManager. [P] **ProfileSkillSet.txt (singular, 20 rows of PS2-style 2000 / 4000 sets) has no
  loader string in Landmark64.exe** (byte search). [P]
- The data is a stub. There are 4 skills, all in skill set 1 / category 1 / line 1. Two of them are named 557636
  "Test String". Their points are 5 / 0 / 10 / 15 and they grant abilities 3-6. Skill set 1 is also named "Test
  String". ProfileSkillSets maps only profiles 19, 20 and 28-33 to set 1. Those are EQN / NPC profiles: 557645
  "Female Wizard", 557644 "Warrior", 557688 "Kobold Archer" … [P text_695].
- The player profiles are 46 and 291 (CharacterCreatePresetDefinitions PROFILE_ID). [P] At start-up the client
  cross-links every profile to its skill set (FUN_143bf7140, called from FUN_142cacd40 → FUN_143bf2f90). It logs "No
  definition was found for profile id" for every profile without one. `<0.695 test copy>\Logs\SkillsError.log` has
  106,068 such lines, including 516 for profile 46 and 516 for profile 291, and none for 19 / 20. [P]
- **The live server sent this data.** The server's login packet `SkillBase` (WorldSession.cs:512) is a live capture
  (docs/unknowns.md "SkillBase (693 B) … correct as captured"). Its first byte is sub 5. The 0.695 dispatcher
  FUN_142f55e50 routes case 5 to FUN_143bf3460, which is Skills::SkillPacketLoadSkillDefinitionManager. [P]
  - The payload decodes to the same 4 skills: name 557636, set / category / line 1, points 5 / 0 / 10 / 15.
  - It also carries 1 skill line, 1 category, set 1 "557636", and the 8 profile → set rows 19, 20, 28-33. [P]
  - Each skill also has one extra column with a running total (15, 30). [I]
  - Note: trace/families/00BC_Skills.md and character.md call sub 4 "LoadSkillDefinitionManager" and sub 5
    "SetSkillPointManager". The 0.695 dispatcher has LoadSkillDefinitionManager = 5, SetSkillPointManager = 6
    (FUN_142f56c40), AddSkill = 7 (FUN_142f56020), ReportSkillGrant = 8 (FUN_142f56450), UpdateClassPoints = 10. [P]
    The emulator's opcode enum (ZoneMessageOpcode.cs:451-461) already matches this.
- UI consumers: none on 0.695.
  - The Skills.* and ProfilePoint.* Lua natives (luabind_695: SelectSkill, SelectSkillSet, RequestSkillPointUpdate,
    the *SkillSetDataSource* selectors, ProfilePoint.SelectClass / Spend) have no caller. The search covered re/as3,
    re/as3_695, UI/Default/*.lua, Resources/Scripts and the compiled UI/ScriptsBase.bin ("Skills." / "ProfilePoint."
    strings: 0 hits). [P]
  - ProfilePoint.SelectClass (FUN_1435af420) and UpdateClassPoints (FUN_142f56d10) fill the "SelectedClass" data
    source through FUN_142dd3d00 (Unlocked, SpentClassPoints, MaxClassPoints, Bonuses; UI/Default/Data.xml:729).
    No movie or Lua reads SelectedClass / ClassPoints. [P]
  - The only AS3 reader of `Skills.SkillPointDataSource` is the PS2 HUD's cert-point counter
    (as3/Hud/scripts/resources/ResourceView.as:53). Its window `PS2HUD` (UI/Default/PS2Hud.xml) is listed only in
    manifest.xml. No Lua, no ScriptsBase.bin string and no exe string opens it ("PS2HUD" absent from the decompile). [P]
- Design: the wiki *Crafting and Gathering Guide* says Landmark has no avatar levels and no skill trees. [P]

Server: **no change.** Keep sending the captured LoadSkillDefinitionManager, because it matches the client files and
live sent it. AddSkill, ReportSkillGrant, SetSkillPointManager and UpdateClassPoints have no visible effect on 0.695.
Leave them unsent. The C2S skill packets stay "parked" (verdicts.tsv).

## 2. ItemGroups / ItemGroupMappings

**Verdict: no 0.695 path reaches the client side. The rows are the live server's item bundles. One is the starter
tool set.**

Evidence:
- Loaded by the item definition manager DAT_145431a50 (FUN_143404ce0 → FUN_143405750 ItemGroups, FUN_143405320
  ItemGroupMappings), with a bucket map keyed by id % 10 at +0x30. [P]
- **The only reader of that map in the exe is FUN_1433cc9c0** (grep of every `DAT_145431a50 + 0x30 … % 10` access). [P]
  - It checks that the group exists, then sends InventoryPacketEquipItemGroup (0x2D sub 10).
  - Its only caller is the Lua native EquipScreen.EquipItemGroup (FUN_1435b5360; refscan: call@0x1435b5395).
  - Nothing in the UI / Lua calls that native (verdicts.tsv: unreachable; ScriptsBase.bin 0 hits). [P]
- Ui.SetTooltipItemGroup (FUN_1435c7000) is a stub that returns false. [P] The PS2 HUD declares
  `BaseClient.ItemGroups` / `BaseClient.ItemGroup.Items` data sources (DataSourceNames.as:96-98). Nothing uses them,
  and the exe does not register them. [P]
- CharacterCreatePresetDefinitions.ITEM_GROUP_ID is 0 in all 4 presets. ItemGroupPromos is empty and has no loader
  string. [P]
- Data meaning:
  - Group 6 = {3253 Adventurer's Mining Pick, 3259 Adventurer's Logging Axe}, the server's starter tools
    (Inventory.cs:60-61).
  - Group 7 is named 562800 "Retrieving Missing Tools." / 562801 "Recover any missing Building Tools". Its items are
    493, 498-503 and 1624, which are **not in the 0.695 item list** (items.json): they are 2014 building tools.
  - Group 9 is three chest armours (1495, 1535, 1537).
  - Groups 1-5 and 8 are also item ids that no longer exist. [P]
  - Orphan texts 562688 "Retrieving your missing Building Tools." and 562716 "Retrieving your missing Adventurer's
    Tools." appear in no client table. [P]
- Design: 31 Mar 2015 hotfix notes say missing tools can be obtained from the chest at spires. 12 May 2014 says lost
  Building Tools come from the Resupply Chest (design/forum/patches). [P] The 2016 wiki *Spire* page lists only a
  Founder's Chest, for founders' uncraftable tools. [P]

Server: nothing is required. The EquipItemGroup handler is unreachable.
- **Optional, open lead L138-C2-b:** a "retrieve missing Adventurer's Tools" grant (ItemGroupMappings group 6, text
  562716) for players who have lost their pick or axe.
- Try next: find whether a 0.695 spire prop / interaction (Interaction list, PropDefs at spires) or a GAQ /
  notification used text 562716. Then decide the trigger (the spire chest vs. on login).

## 3. Stats

### 3a. StatCategoryDefinitions — client-only (live code, empty result)
- The 1 row is General 1000 "Stats.General".
- FUN_142e5e460 / FUN_142e5e100 add a Character.StatsList row only when a calculated stat has HIDE_FROM_UI 0 and its
  category is in this table. Every 0.695 CalculatedStatDefinitions row has HIDE_FROM_UI 1, so the list is always empty
  (gear-stats-695.md §1). [P]
- Server: nothing. The sheet's numbers come from resource maxima, which the server already sends.

### 3b. CharacterStatDefinitions — the client never reads it; it is server data
- The 47 rows have no names. SEND_TO_REMOTE_CLIENT = 1 for ids 21, 22 and 24-29. [P]
- Loaded by FUN_143bb18b0 into `HashListMap<int,CharacterStatDefinition,133>` (DAT_145431a38). [P]
  - Its only reader is FUN_143c67450. That function walks an entity's stat list and collects the stats flagged +0x10
    (SEND_TO_REMOTE_CLIENT).
  - **refscan: FUN_143c67450 has no call, LEA or pointer in Landmark64.exe**, only EH table entries. [P]
  - The other uses are the destructor (FUN_143bb1360). [P]
- These are *character* stats: the `List<CharacterStat>` of ClientUpdate UpdateStat (0.695 case 5, reader
  FUN_142c492a0), ids up to 0x84. They are a different id space from CalculatedStatDefinitions. [P]
- So SEND_TO_REMOTE_CLIENT tells the server which of a player's character stats to copy to other players.
- Server today: PlayerUpdateLightweightToFullPc sends an empty PlayerUpdateUpdateStat list for remote players (:51-53).
- **Open lead L138-C2-c.** What character stats 21, 22 and 24-29 are, and whether a viewer's client needs them for
  another player.
  - Try next: decode the stat list in the SendSelf capture (SendSelfToClient Stats, docs/sendself.md +0x75670) and the
    rank-stat ids in FUN_143bf3360 (ProfileDefinitions).
  - Then trace what FUN_142fd48b0 does with each id for a remote entity.

### 3c. AttributeDefinitions / AttributeEntryDefinitions — not live on 0.695 (the 2015 progression system)
- There are 7 real attributes: 3 Health, 4 Armor, 5 Offense, 6 Armor Steal, 7 Critical Strike, 8 Effect Power,
  9 Energy (texts 569779 / 569944-569949). Each entry is a threshold of 1 point → +1 to a calculated stat (BONUS_TYPE 1):
  Health → stat 1, Armor → 9, Offense → 10, Armor Steal → 44, Crit → 2, Effect Power → 39, Energy → 24. MAXIMUM is
  966 / 483 / 149 / 23 / 39 / 39 / 34. [P rows]
- Design: on 20 Apr 2015 the devs said progression achievements reward permanent character stats. The "Layer N
  Adventurer" goals grant Health, Offense and Armor, and secondary goals grant Critical Strike and Armor Steal
  (forum/all/2015-04-20-54344). [P]
- Client paths:
  - ClientUpdate UpdateAttributes (dispatcher part_0065 ~14445 → FUN_143470000) fills the data source
    `Character.Attributes` (FUN_142d62f60 part_0105:34413). No movie, Lua or ScriptsBase.bin reads it. [P]
  - The only UI use is the Journal reward list: reward Type "3" → tooltip type 11 →
    ClientInventory.RequestDetailsForAttributeByDefId (JournalDetailsClass.as:583-585,
    ToolTipWidgetDocumentClass.as:278/413 → FUN_1435deb50, AttributeDefinitionManager game+0x32190). [P]
- **The live achievement capture (Feb 2017, 898 goals) has only reward entries of type 1 (items): 183 of them, and no
  other reward type. There are no "Layer N Adventurer" goals** (parse of 71-AchievementBase with the server's
  RewardBundle layout, Achievements.cs:263-281). [P] So by the 0.695 era no goal granted attributes. The March 2016
  wipe and combat rework replaced the 2015 progression. [I, timing]
- Server: **nothing**. Do not send UpdateAttributes. The gear model in GearStats.cs (stat 1 / stat 10) replaces it.

### 3d. AbilityParameterDefinitions — client-only, live code
- ParameterCalculator FUN_1435079e0 (manager game+0x32220) computes stat × PARAM_2 + PARAM_3, or resource × PARAM_2 +
  PARAM_3 (gear-stats-695.md §1). [P] It has two kinds of users:
  1. **Text tokens.** FUN_142d891a0 replaces `[*AbilityParamN*]` in "Global.Text.%d" strings (three callers in
     part_0068). There are 40 client texts with tokens [P text_695]. **None of them is the description of a 0.695
     weapon attack the server sends.** The server's 45 kit description ids (WeaponCombat.cs `new Attack(name, desc …)`)
     share no id with the 40. [P] No item description or table uses the 40 either. [P] They are 2015 skill texts
     ("Offense scaling", 564673-573172) plus templates ("Stat_Name", "AbilityParam###").
     So **no tooltip number needs to match server damage** for the 0.695 kits. Our stat 10 = 70 × armour factor still
     keeps the formula right if a token text is ever shown. [I]
  2. **Client-executed effects.** The effects ClientExecutedEffectSprint / Jump / DoubleJump / Glide / ChanneledGlide /
     ChanneledSprint / Fly (factory part_0070 16668-17100) build their speed curves from parameter ids
     (FUN_14354b670 ← FUN_14354bd50 / bf30 / c1f0). [P] The effect list arrives in
     **Abilities CasterInfoReply** (`List<ClientExecutedEffectData>` at reply +0x1f·8, ctor FUN_142e076d0). [P]
     The server writes `0u // executed effects` (AbilityMessages.cs:105). [P] The 326 heroic-movement reply and the
     boot replies are built without effects (MiscHandler.cs:466-500). The only live captures (lightstone 221, 1098)
     have no effects.
- **Open lead L138-C2-a.** Whether live CasterInfoReplies for sprint (326), the boot abilities (320-328, 432, 542,
  807-813, 1207-1214) or glide / fly carried ClientExecutedEffectData that referenced AbilityParameterDefinitions rows.
  - Try next:
    - decode the 0.695 CasterInfoReply effect-list reader (twin of 0.589 FUN_142d63690) and the factory type hashes;
    - find which AbilityParameterDefinitions TYPE_NAME rows look like movement curves (speeds / durations rather than
      ×stat 10);
    - check in game whether Dash Soles / Boots of Bounding / Breeze Boots move as the wiki describes without effects
      (the DesignBased state machines may drive them instead, BootAbilities.cs).

## 4. ContentPacks — client-only

- ContentPacks::ContentPackManager is created at start-up (FUN_143bb53f0 from part_0066:8901) whenever the client
  connects to a server (game +0x31e0b, set at part_0066:7651). [P]
- Its environment name is the exe constant **"MAIN"** (DAT_1446b3ea0, read from Landmark64.exe), so the
  MAIN_UNLOCKED column applies. Packs 1, 2, 98, 99 and 106 are unlocked. [P] Parser FUN_143bb6560 builds the column
  name with "%s_UNLOCKED". [P]
- The manager is handed to the **AssetSystemDataLoader** (param_1[0x63c0], ctor FUN_143d5f570) after
  MandatoryAssets.txt. No gameData695 table has a content-pack column. [P]
- So it gates client assets and data by build environment. It has no server rule. The ContentManagement::Content*
  packets are a different system (Content.* natives, verdicts: unreachable). [I]
- Server: nothing. (InitializationParameters "live" does not feed it; that string is unread, docs/unknowns.md.)

## 5. PropRecipes — live, but only an id list

- PropRecipes.txt has a single column (ID, 128 rows). The client defines Crafting::PropRecipeDefinition (file getter
  FUN_143cb6580). [P]
- All 113 PROP_RECIPE_IDs in PropDefs and all 124 recipe ids in PropRecipeComponentMaps exist in PropRecipes. 15 rows
  are unused by PropDefs and 4 have no components. [P, cross-check]
- The server already gets the cost from PropDefs.PROP_RECIPE_ID → PropRecipeComponentMaps → RecipeComponents
  (Props.cs:1984-2013, Chests.cs:83).
- Server: nothing. There is no data in PropRecipes.txt beyond the key.

## 6. CraftingStationCatalystMappings (Forge 48 → catalyst 33) — not reachable on 0.695

- The client defines `HashListMap<int, CraftingStationCatalystMappingDefinition>` (part_0142 ~9554-9794), and the file
  name is in the exe. [P]
- Catalysts appear only in the item **upgrade** view: Recipe.UpgradeStation* natives, AS3
  CraftingWidget/…/upgrading/*. In 0.695, CraftingWindowWidget.as offers the "Upgrade" tab only when the station's
  type is 3 (UPGRADING). [P]
- **No 0.695 CraftingStations row has type 3.** The types are 1 (sifter, workshop, forge 48, replicator, 54, ether
  shard shop), 5 (salvager) and 6 (two repair stations). [P] Recipe.AddCatalyst / RemoveCatalyst have no UI caller
  (as3 + as3_695 grep). [P]
- InventoryPacketCatalystDefinitions (S2C) is not sent, and no catalyst items exist. Catalyst 33 is not an item id.
  [P items.json]
- Server: nothing.

## 7. VehicleArmorMappings / FacilityZoneMap — not loaded by the 0.695 client (PS2)

- Neither file name is in Landmark64.exe or in the decompile (byte search returns nothing). [P]
- VehicleArmorMappings maps vehicle 6 → armour info 1-6, but Vehicles.txt and every other Vehicle* table are empty.
  [P] The vehicle packets are unreachable (verdicts.tsv).
- FacilityZoneMap maps facilities 17 and 180000 to zoom images. All Facility* tables are empty, and the similarly
  named FacilityZoomMap.txt is empty too. [P]
- They are PlanetSide 2 server data with nothing in Landmark to apply to.
- Server: nothing.

## New open leads (for OPEN-LEADS.md, under L138)

- **L138-C2-a:** ClientExecutedEffectData in CasterInfoReply (sprint / boot / glide abilities) and its
  AbilityParameterDefinitions rows (§3d).
- **L138-C2-b:** the retrieve-missing-tools grant (ItemGroup 6 / text 562716): trigger and place (§2).
- **L138-C2-c:** CharacterStatDefinitions SEND_TO_REMOTE_CLIENT stats 21, 22 and 24-29: what they are and whether
  remote players need them in PlayerUpdate UpdateStat (§3b).
- **Doc fix:** trace/families/00BC_Skills.md and trace/systems/character.md use 0.589-generator sub numbers for 0xBC.
  On 0.695, LoadSkillDefinitionManager is sub 5 (FUN_142f55e50), and the server sends it as captured.
