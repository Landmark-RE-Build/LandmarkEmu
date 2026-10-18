# System: character — stats, resources, skills, experience, profiles, loadouts, titles, customization, currency

Status: [~] map level.

## Stats / resources
✔ ClientUpdate UpdateCalculatedStats 0x2C, UpdateStat 0x6, Hitpoints 0x1, CombatResourceEvent (PlayerUpdate 0x67).
✗ ClientUpdate Mana 0xD (energy bar via CombatGroupHandler:SetPower), UpdateAttributes 0x2E, DamageInfo 0x1E,
ModifyMovementSpeed / TurnRate / StrafeSpeed 0x21-0x23; PlayerUpdate UpdateStat 0x53.
Data (client): CalculatedStatDefinitions, CharacterStatDefinitions, CharacterStatSetMappings, AttributeDefinitions,
AttributeEntryDefinitions (1736, **not read**), ResourceTypes / Resources / ResourceConstants, StatCategoryDefinitions,
ItemCategoryStatMappings. Server-only: CalculatedStatGroupEntries, ProfileCalculatedStatGroups, ResourceTriggers /
ResourceTriggerMaps (CONDITION_TYPE / VALUE / REQUIREMENT → TRIGGER_ABILITY / NOTIFICATION), Profile* (rank stats:
FUN_143bf3360 — movement speeds, sprint, jump, fall damage, attributes, damage, crit ...).

## Skills (0xBC; SkillAdmin in 0x01) ✔ UpdateClassPoints 0x1, SetSkillPointManager 0x5. ✗ SelectSkillSet, SpendClassPoints,
SelectSkill, GetSkillPointManager (C→S); LoadSkillDefinitionManager 0x4, AddSkill 0x6, ReportSkillGrant 0x7 (S→C). (0.695:
LoadSkillDefinitionManager = sub 5, SetSkillPointManager = 6 — FUN_142f55e50; agent-l138-c2.md.)
SkillDefinitionManager loads 10 tables (FUN_143b310e0..322e0). 0.589 skill UI: check reachability.

## Experience (0xBE) ✔ rate tier, ranks; ✗ SetExperience 0x1.
## Profiles (ProfileDefinitions 47 columns, ProfileStatAttributeMap; ClientUpdate Delete / Activate profile, ability
set APLs; Command SetProfile 0xF ✗, ProfileDefineSlot 0x1D ✗): Landmark uses one profile — check.
## Loadouts (0xBD) ✗ create / delete / select / set ability / name / weapon set; UpdateLoadout (S→C).
## Titles (0x94) ✔ add / update all; ✗ PlayerTitleRequestSelect 0x4 (choosing a title!).
## Customization (0x11C) ✔ PlayerSet; ✗ AddKnowledgeResponse 0x2. PlayerUpdate CustomizationChange 0x1B /
CustomizationData 0x34 ✗. Data: CharacterCustomizationSlots, CharCustomizationSlotCost, CharCustOptionMult ...
## Currency ✔ PlayerCurrencyChange; ✗ PlayerRewardCurrency (PU 0x4A), CurrencyPacketSetCurrencyRateTier (0xE5),
KeyCodeRedemptionNotification (0x74), loyalty points (ClientUpdate 0x2B).
## Factions (0xC2) ✗ LoadFactionDefinitionManager; PlayerUpdate SetFactionId / SetBattleRank ✗.
## Name change: Command NameChangeRequest ✔, PlayerUpdate InitiateNameChange 0x5F / NameChangeResult 0x60 ✗,
RenamePlayer 0xE ✗.

## Live behaviour (wiki, launch 2016: Character Creation, Crimson Parlor, User Interface, Controls)
- Creation: sex, size (Small / Large human — breadth, both fit a 9 × 5 voxel opening), 4 face presets per sex+size,
  skin tones, hair (more for female), 4 facial hair (male), hair colour, eye colour; starting kit style chosen at
  creation: fantasy (sword), sci-fi (water gun), Western (dual pistols). Server: choose manually or automatic;
  every character starts with a site on a Pioneer Landscape.
- **Crimson Parlor** (all surface Spires; placeable): F / click → customisation view; each changed feature costs
  Lumens (more features = more cost): face, skin, hair, hair colour, facial hair, eyes, sex, size. Fantasy Parlor
  Pack (late June 2016, after 0.589) adds fantasy features.
- HUD: Options (Landmark logo), Marketplace (cart), Showcase, Inventory, Notifications "!" (friend requests, mail),
  Friends, Social, Travel crystal; Lumen count with "+" (buy); Goals list; chat; hotbar (1-9, Ctrl+1-9 switches
  hotbars); health + energy bars (screen edge flickers red on heavy damage). On a site with access: first person,
  levitate / fly (Build Mode), reverse camera, fly-through camera, Play / Pause (story tools play mode), Build
  Mode. Site info drop-down (owner, title, welcome) on any site. Map M: "Show Claimable Land" (red = taken sites,
  small red = cave openings), orange flags = no access, blue = friendlier; Spire icon.
- Keys: Shift sprint, Q grapple / Space release, . walk, O screenshot, U Site Management, L designs, J Journal,
  B Showcase, N guild, I inventory + character, Alt tool tips, Alt+F performance, Alt+S script window.

## Customization knowledge (traced 2026-10-13)
- CharacterCustomization 0x11C sub 2 AddKnowledgeResponse {Array<int> ids, u32 type} (reader FUN_1435d77f0, handler
  FUN_1435d7a50 on player +0x7ae28): type 3 adds ids to set +8, type 4 to set +0x20. SendSelfToClient carries the
  same two int arrays (+0x7ae30 / +0x7ae48, SendSelfToClient_Read).
- The barbershop offers an option when its record's innate flag (+0x30) is set or the knowledge set holds it
  (FUN_142fffxxx near part_0083:5956). So non-innate hairstyles / faces / tints need knowledge — live unlocked them
  (store bundles). Open: which options are non-innate and which items grant them; the server sends both arrays empty.
- Locked options (CharacterCreateOptions.txt DEFAULT_UNLOCKED 0): 8 rows, all slot 4 Facial Hair — the "_06" variant
  of facial hairs 00-03 for HumanMale (88-91) and HumanLargeMale (147, 154, 161, 168), children of the base beards.
  No item in 0.589 data unlocks them (no beard / customization items); left locked, as a new 0.589 character had them.
