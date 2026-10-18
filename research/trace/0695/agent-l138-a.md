# L138 part A — server-only tables shipped in the client folder (0.695)

Agent report, 2026-10-17. Read-only research; no code changed. Marks: [P] proven (source given), [I] inferred (reason
given). Data: `gameData695/*.txt`; the files are byte-identical to the 0.589 copies (md5) except Requirements.txt [P].
No loader: the names below occur neither in decomp_all / decomp_all_695 nor as raw strings in
`<0.695 test copy>\Landmark64.exe` (grep -a: 0 hits for WeaponSetDefinitions, RequirementSets, RequirementGroups,
LoadoutSlots, LoadoutEquipSlots, LoadoutConstants, LoadoutTabCountMax, ProfileTypes, VoxelTiers, DamageTables; the
"SlotDefinition" hits are only Action/Equipment/CharacterCustomization/InventorySlotDefinition names) [P].

---

## 1. SlotDefinition (167 rows: ID, ICON_ID, REQUIREMENT_ID, CAPACITY)

**Meaning**
- ICON_ID is an image-set id, the same space as the item records' imageSetId: 118 Dirt, 119 Sand, 126 Stone,
  189 Plain Wood, 110 Smelted Copper (gameData/items.json) [P]. A contact sheet of the icons shows metal bars, soil/stone
  chunks, 10 gems, 3 wood rounds and 8 plants [P, icons/*.png].
- Row blocks [P from data]: 4-5 (cap 10, icons 110/113); 6-16 (cap 1, no icon, req 21-27); 17-43 (27 resource slots:
  10 bar icons, 7 soil/stone, 10 gems); 44-46 (wood); 47-55 (plants); 63-86 and 87-113 (the same icon order repeated
  = further tiers or states of the same resources); 114-171 mostly no icon. REQUIREMENT_ID runs with the slot id
  (slot n → n+13 in 17-43 and 63-113), range 20-220.
- REQUIREMENT_ID does **not** resolve in 0.695 data: Requirements.txt only has ids 3-81, and those rows are unrelated
  tests (slot 17 with a copper-bar icon → req 30 TargetIsProfile202 = profile 202 "Tree: …") [P]. It is not an item id
  either: the item whose id equals the req has a different icon (slot 32 req 44 shows icon 125, but item 44 Dirt
  uses 118; the 118 slot has req 29) [P items.json].
- CAPACITY 100000 per resource matches no Landmark collection size: 900,000 before and 24,000,000 after 21 Mar 2016
  (patch notes "Inventory/Storage Changes", forum/patches/2016-03-21-56259) [P].
- So [I]: a superseded early resource-collection ("slot per resource") definition with requirement ids from an older
  server Requirements numbering. Its 0.695 replacement is **InventoryDefinition.txt type 5/6** (client-loaded by
  InventorySlotManager FUN_143c03c70, which logs under the name "InventorySlotDefinitions.txt"; SLOT_ID = item id,
  SLOT_VALUE 24,000,000) [P].

**Live use:** none in the 0.695 era [I: the capacity and the requirement ids contradict the patch notes and
Requirements.txt]. No link to harvesting / tools / claims was found: harvest target tests live in Requirements
20-43 and RequirementExpressions 8/14-16/19/20/22/34 (the tree/plant profiles 90, 91, 200-214, 296), not here [P].

**Our server:** the resource bag reads InventoryDefinition type 5 (`Game/Inventory/Inventory.cs:53`, `:147-161`).
That matches P4186 (24,000,000) [P]. Harvesting rules: `Game/Gathering.cs:288` (Chop), `:323` (Reap). **No change.**

## 2. RequirementGroups (43) / RequirementSets (22)

**Meaning** [P structure]: RequirementSets rows = (set, group, REQUIRED); RequirementGroups rows = (group, requirement).
Sets 1-11 and 13-19 each hold one group with REQUIRED=1. Set 12 holds groups 12, 13, 14, 15, all REQUIRED=0.
- [I] combine rule (SOE engine shape): every REQUIRED=1 group must pass, and at least one REQUIRED=0 group must pass.
  Inside a group the operator is not provable from the data. Group 11 = {14,15,16,17,18}, read as client ids, is
  TreeSmall, TreeVerySmall, ItemIsBuildingTool, IsHUB, Alive, which makes sense neither as AND nor as OR. Group 3
  contains 7 = AlwaysFails. This looks like test data.
- The group requirement ids include 1, 2, 8, 9 and 33, which are **absent** from the client Requirements.txt [P]. [I]
  The client file is a filtered export of the server's Requirements table, and the server had more rows.
- 0.695's client composes requirements with **RequirementExpressions.txt** instead: boolean strings over DESIGN_NAME,
  loaded by FUN_1432ef380 ("%sRequirementExpressions.txt"); Requirements.txt is loaded right after it ("%sRequirements.txt")
  [P].

**Who references set ids** [P column scan of all 0.695 tables]:
- The REQ_SET_ID / CLIENT_REQ_SET_ID columns are in ItemLines, AbilityLines, LoadoutSlots, LoadoutTabs, Implants,
  ImplantSlots, Missions, Skills, SkillLines, SkillSets, SkillCategories and VehicleLoadout*. In every one of them the
  value is 0 or the table is empty.
- The client parses them: ItemLineRequirement::Load FUN_143cb25d0, the Skill*Requirement loaders in part_0147, and
  EffectDefinition XML "requirementSetId" FUN_143ce3fa0 / FUN_143ce49a0.
- Only non-zero candidate: GlobalAreas rows 3 and 27 have REQUIREMENT_SOURCE_TYPE 1 and REQUIREMENT_SOURCE_ID 4. Open
  question below.
- AbilitySets row 1 uses REQUIREMENT_ID 2, a server-only requirement id.

**Live use** [I]: server-side gate for item lines, ability lines, loadout slots and skills. The shipped rows are
engine test data.

**Our server:** there is no generic requirement evaluator. Each rule is coded where it applies (for example
`Game/ItemUses.cs:93` and `:219`, the "Failed item requirement" refusal, and `Game/Creatures.cs:1965`, client
requirement 21). **No change.** If a generic evaluator is ever built, implement Requirements + RequirementExpressions
(the client's semantics), not Sets/Groups.

## 3. LoadoutSlots / LoadoutEquipSlots / LoadoutConstants

**Meaning** [P data]:
- LoadoutSlots describes one loadout, id 17, with slots 1, 3, 5-14:
  - slot 1: item line 11, AUTO_EQUIP, REQUIRED, CAN_CUSTOMIZE, VISIBLE.
  - slot 3: item line 2, REQUIRED, CAN_CUSTOMIZE, VISIBLE.
  - the other slots have no flags. Their item lines are 0, 1, 3033, 14, 110, 7, and ItemLines.txt / ItemLineMembers.txt
    are empty.
- EQUIP_SLOT_ID is set only on slot 10 → 8 (Chest, "Armor" 575726) and slot 12 → 12 (Head). LoadoutEquipSlots repeats
  this mapping.
- LoadoutConstants: LoadoutTabCountMax 3 and VehicleLoadoutTabCountMax 3 (PS2-style loadout tabs; LoadoutTabs.txt is
  empty).

**Not Landmark's loadout** [P]:
- Live sent loadout id 1, profile 46, "Default Loadout", ActiveWeaponSet 0, 4 + 5 ability ids and 16 equipment u64s.
  This is the captured LoadoutsBase body in `Network/Handlers/MiscHandler.cs:262`.
- No 0.695 item uses equip slot 11 or 12. Item equip slots in the dump are 0, 1, 2, 3, 4, 5, 8 [P items.json].

**Client loadout record** [P FUN_142f06ef0, the Character.ClassLoadouts data source]: +0x20 LoadoutId, +0x24 ProfileId,
+0x28 name, +0x40 ActiveWeaponSet, ActiveAbility1-4, PassiveAbility1-5, EquipmentSlot1-15.
- The Loadouts 0xBD C→S packets are not handled by our server (coverage-695.md §0xBD). They are sent only from the Lua
  bindings: SetActiveLoadout, SetActiveWeaponSet, SwapWeaponSet ("No active loadout! Weapon swap failed.",
  FUN_1435f05b0 → FUN_142f08430), CreateLoadout / CopyLoadout, DeleteLoadout, SetName, SetAbility (luabind_695.tsv).
- No shipped Lua (`<0.695 test copy>\Resources\Scripts`), AS3 UI (re/as3_695) or input table calls them [P grep].

**Live use** [I]: a server-side template for an EQN/PS2 class loadout. Landmark's equip rules come from the
client-loaded EquipmentSlotDefinitions plus InventoryDefinition type 2.

**Our server vs these rules:**
- Equip slots come from InventoryDefinition type 2: `Inventory.cs:468-482` (EquippedSlotFor), `:751-766` (Fits),
  `:775-790` (ToggleEquip). That matches the client data. There is no divergence that matters for loadout 17, because
  no item uses its slots 11 or 12.
- Divergence, cosmetic: SendSelf `Loadout` keeps the record fields as Unknown0-6 (`SendSelfToClient.cs:1535-1600`);
  `WorldSession.cs:977` sets the profile id.
- **Side finding to verify, outside L138:** the doc comment at `Inventory.cs:473` says "chest → 9". The code takes the
  lowest SORT_ORDER, and for equip slot 8 InventoryDefinition has row 328 (slot 1, SORT 4, PARAM2 1) and row 325
  (slot 9, SORT 5). So chest resolves to container slot 1, the outfit's slot. Check the client's use of
  PARAM2 / SORT_ORDER before changing anything.

## 4. WeaponSetDefinitions (3 rows)

**Meaning** [P data]: item (type, subtype) pair → abilities 1-4 + requirement.
- set 1 = 26/3 + 27/20 → abilities 5, 6, 7
- set 2 = 26/3 + 26/16 → abilities 3, 4, 8, 2
- set 3 = 26/3 + 26/3 → abilities 6, 7

Requirement is 0 in all three sets.
- Only ProfileDefinitions 2 (Rogue) and 3 (Cleric) use WEAPON_SET_ID_01-05: profile 2 → sets 1, 3; profile 3 → sets
  2, 3 [P]. Landmark player profiles (45-56: Firiona, Human, Founders …) have none [P].
- Abilities 1-8 are the engine sample abilities: AbilityLines 1-2 / AbilityLineMembers 1-6, AbilitySets set 1 [P].
- 26 is the item category "Weapon" (items.json u47 = [26] on weapons) [P]. The subtypes 3 / 16 / 20 are not mapped to
  any 0.695 item field [open].

**Live use** [I]: a dual-wield ability-set selector for EQN prototype class profiles (ActiveWeaponSet 0/1 in the
loadout; SwapWeaponSet flips it). It is unreachable for Landmark characters on 0.695 because their profiles have no
weapon sets. The only entry point is the /weaponset admin command (L107).

**Our server:** WeaponCombat takes the held weapon's kit (`Game/Combat/WeaponCombat.cs:12-25`), which matches
Landmark's single-weapon combat. `ProfileDefinitionEntry.cs:50-54` loads WeaponSetId01-05 but nothing reads them.
/weaponset is logged as not built (`Game/AdminCommands.cs:584-595`). **No gameplay change.**

## 5. ProfileTypes (13 rows)

**Meaning** [P text_695]: NAME_ID 557644-557654 = Warrior, Female Wizard, Rogue, Cleric, Ranger, Monk, Necromancer,
Ninja, Elementalist, Beastlord, Shadowknight (EQN classes); rows 12 and 13 have no name.
- ProfileDefinitions.PROFILE_TYPE uses 0 (136 rows), 1 (12, test combat profiles), 2 (1), 3 (23, the player body
  profiles such as Human 46 and Founders), 13 (46, combat NPCs 298-327 …) [P].

So [I] in Landmark the type is a grouping number whose class names are stale, and the client has no "ProfileType"
string [P].

**Our server:** sends ProfileDefinitions.PROFILE_TYPE in the SendSelf profile record and in the type → profile pairs
(`Network/WorldSession.cs:961`, `:976`). This already matches live's use. **No change.**

## 6. VoxelTiers (9 rows)

**Meaning:** id, name, underground biome bitmap, cave density.
- Prototype tiers: TestTier1/2.bmp; 10-14 "The Undergrowth / Crust / Caverns / Deadlands / Core" with
  Continent_Underground_BiomeIds_0000[2-6].bmp, density 0.8 [P].
- VoxelContinentToTierMapping maps continents 224, 234 and 323 to them. Those are not 0.695 continents (re/out/
  continents.tsv) [P].
- Superseded by **VoxelContinentLayers**, which has the same UNDERGROUND_BIOME_MAP and CAVE_DENSITY_PCT columns plus
  random encounters and teleport image. The client loads underground biomes through
  VoxelContinentLayerDefinitionManager (FUN_143ff5070, called from FUN_143ffa7c0) [P; terrain-695.md §row parsers].
- Layers arrived with the 17 Dec 2014 update ("true layers are being introduced") [P
  forum/patches/2014-12-15-51046].

**Live use** [I]: the 2014 tier prototype; dead by launch.

**Our server:** caves come from the .cpa / .cav files and the continent layers. **No change.**

## 7. DamageTables (2 rows)

**Meaning:** columns ID, DAMAGE_CATEGORY, DAMAGE_AMOUNT, DAMAGE_PER_SECOND. Row 1 is category 1 → 8; row 2 is
category 2 → 10; DPS is 0 in both.
- The header has no `*` key marker, unlike the other exports [P].
- No table column references it [P scan], and the client has no damage-category string [P grep].

[I] Probably environmental damage, such as falling or drowning. Fall damage was turned off in 2014 (11-25-14 hotfix
thread, 12-4-14 notes "Barbed Armor will no longer react to fall damage") [P]. `Game/Landscapes.cs:364` notes that
0.589 has no fall damage.

**Our server:** no environmental damage. **No change** until a 0.695 trigger is found.

---

## Proposed server changes
1. **Docs / naming (low)**: rename the SendSelf `Loadout` fields (`SendSelfToClient.cs:1535-1600`) to LoadoutId,
   ProfileId, Name, ActiveWeaponSet, ActiveAbilities[4], PassiveAbilities[5] and EquipmentSlots[16].
   Evidence: FUN_142f06ef0 and the live capture `MiscHandler.cs:262`.
2. **Record in tables-unread-695.md §A**: SlotDefinition, RequirementSets/Groups, LoadoutSlots/EquipSlots/Constants,
   WeaponSetDefinitions, ProfileTypes, VoxelTiers and DamageTables are superseded or test data with no 0.695 effect.
   Evidence: the sections above. No loaders to add.
3. **Verify (side finding)**: the chest container slot in `Inventory.EquippedSlotFor` (`Inventory.cs:473-482`). The
   comment and the code disagree: slot 9 in the comment vs slot 1 by SORT_ORDER.
4. Optional, after L107: if /weaponset or Loadouts 0xBD are built, use ProfileDefinitions WEAPON_SET_ID_* →
   WeaponSetDefinitions for ActiveWeaponSet. Reply with the result codes in client-result-tables.md §Loadout*.

## Open questions (try next)
- **GlobalAreas REQUIREMENT_SOURCE_TYPE 1 / SOURCE_ID 4** (areas 3 and 27): is it requirement set 4, requirement 4 or
  expression 4? Read the GlobalAreas row parser that FUN_143d2c880 registers and its enter/exit check.
- **SlotDefinition requirement numbering**: look for an old-format Requirements or item dump in archive/, re/out/
  forum_raw or 2013-14 alpha patch notes about the resource collection (per-resource slots, cap 100,000).
- **Weapon subtypes 3 / 16 / 20**: match against items.json u41_46 / u48_53 and the client ItemClassDefinitions
  reader. Our server sends ReferenceData ItemClassDefinitions empty (`WorldSession.cs:507`).
- **ClientRequirementLocalPlayerHasItemInActiveLoadout** (requirement 60 PlayerHasFlightBootsEquipped, item 1594):
  does the client read the loadout's EquipmentSlot list or the equipped container? No expression references it today.
  Read its vftable Evaluate (constructors at part_0095.c:2725 and part_0097.c:2744).
- **DamageTables category ids**: look for a 0.695 caller using damage category 1/2 (the creature or environment damage
  path); search the decompile for the constants 8 / 10 near a fall-velocity or water check.
