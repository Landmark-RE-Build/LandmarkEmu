# 0.695 data tables the server doesn't read — classification (Phase E, AUDIT-0695.md §2)

Source: trace/0695/datatables-695.md (431 tables, 274 with data, server reads 87, **188 with data unread**; 67 of those
already described in trace/systems/*.md or trace/0695/*.md). This file classifies the other 121 and anything found on
the way. Proof marks: [P] read, [I] inferred, [?] open.

## A. Server data shipped in the client folder (no client loader at all) — what live's server used

These have **no loader string** in either client build, so only the live server read them. Each must be explained.

| table | rows | what it is | status |
|---|---|---|---|
| PlacementPacks / PlacementPackProps | 8 / 19 | mover packs: parts, offsets, separation | **built** (E.1, 22dd72f; movers-695.md) |
| VoxelResourceGeology | 84 | older vein geology (same fields as ResourceVeins.txt, which the server uses) | covered by ResourceVeins [I] |
| SlotDefinition | 167 | slot {icon, REQUIREMENT_ID, CAPACITY}; requirements are target-profile tests (20 TargetIsTreeVerySmallJungle, 30 TargetIsProfile202 …), capacities 1 / 10 / 100000 | [?] L138 |
| RequirementGroups / RequirementSets | 43 / 22 | how Requirements combine into sets (REQUIRED flag) — the server side of requirement checks | [?] L138 |
| LoadoutSlots / LoadoutEquipSlots / LoadoutConstants | 12 / 14 / 2 | loadout 17's slots (item line, auto-equip, required, tint / decal flags) → equip slots; "LoadoutTabCountMax 3" | [?] L138 (compare with the server's own equip-slot rules) |
| WeaponSetDefinitions | 3 | item type / subtype pairs → abilities 1-4 + requirement | [?] L138 (which abilities a weapon pairing grants) |
| ProfileTypes | 13 | profile type names (557644 …) | [?] L138 |
| VoxelTiers | 9 | underground tier maps (TestTier1.bmp, Continent_Underground_BiomeIds_0000n.bmp) and cave density | [?] L138 (the server's caves come from the .cpa / .cav) |
| DeployableTypes | 3 | ids only | no content |
| DamageTables | 2 | damage category → amount (8, 10) | [?] L138 |
| PropTintGroups | 1 | the one tint group (no requirement) | covered: the tint palette capture (PropTints.bin) |

**Result 2026-10-17 (agent-l138-a.md):** none of the §A tables feeds a 0.695 Landmark system — SlotDefinition (old
per-resource collection, superseded by InventoryDefinition type 5), RequirementSets / Groups (test rows; 0.695 uses
RequirementExpressions), Loadout* (loadout 17 is an EQN template; live sent loadout 1 / profile 46), WeaponSetDefinitions
(test profiles 2 / 3 only), ProfileTypes (EQN class names), VoxelTiers (2014 prototype, replaced by VoxelContinentLayers),
DamageTables (no reference). Side finding fixed: chest armour now equips into the Armor slot 9, not the appearance slot 1
(ea93a7a). §C combat tables: agent-l138-c1.md — ProjectileDefinitions LIVE (0xEE built, L156 / L157), Fire* parked (no
weapon data on 0.695), MerchantSets not live [I] (L159), HeroicMovement client-only, KeywordDefinitions live display (L158). §C skills / stats / content tables: agent-l138-c2.md — Skills live but test data
(captured body kept), Attributes not live on 0.695, AbilityParameterDefinitions / StatCategories / ContentPacks client-only,
PropRecipes covered, CatalystMappings unreachable (no station type 3), ProfileSkillSet / VehicleArmor / FacilityZoneMap not loaded
by 0.695; leads L160-L162.

## B. Client-loaded, client-side only (no server rule) [I from the loader's manager and the columns]

- Rendering / materials / art: AutoDetectDevIDs, VoxelShaderParameters, MaterialCategoryMatMap, MaterialCategory(+Group),
  MaterialTypes, VoxelMaterialFloras, VoxelMaterialRemaps, VoxelFloras, VoxelMatComponents(+Flora),
  VoxelMaterialsMatComponents, VoxelMeshProceduralMaterial(Materials)s, VoxelPeriodicTileSets, VoxelCornerTileSets,
  VoxelTransparentTypes, VoxelWaters, VoxelCategories(+Group), VoxelTierBmpToBiomes, VoxelHeightLayers, ArtTint* ,
  AlexTestTintSemanticTables, SelfLight, ItemSelfLightMap, LootParticles, FirstPersonArms, HeadTypes, Footstep,
  TriggerSet (FX start / stop triggers), PrebuiltPalettes (client palette presets), PiecewiseLinearCurves (manager at
  client +0x32118; read by the quick-turn movement curve, "StatId.QuickTurnCurveId" FUN_14327ccb0, and FUN_142f66920
  with a "Strength" stat — client movement / physics) [P].
- Character creation looks: CharacterCreate*, CharCreateGearSet(+Mapping), GenderDefinitions.
- Input / tools / UI: InputStrings, RefInputProfileActions, ModalTool*, BrushType, ToolStateTypes, ToolAxisTypes,
  CursorStyles, VoxelCursorShape, Reticles, UiColors, HUDIndicatorSettings, OnScreenHints, RefDisplayCategoryTypes,
  ItemUiOptions, CraftingCategoryGroupDefinitions, AchievementCategories / Subcategories / Quality (goal window
  grouping), GameActionQueues / NotificationEntries / CombatNotifications (popup and toast styles), DevTalk, README-0695.
- Animation / client state: AnimationSelectionRuntimeVariable(Enum)s, CharacterPersistState*, LootPinataAnimation(Set),
  DesignBased* (client weapon-combo state machines, trace/systems/abilities-combat.md).
- Terrain generation inputs (the client generates base terrain): VoxelMiningResources, VoxelContinentToTierMapping,
  VoxelCaveRoomGap*, BiomeTypes, ResourceVeinGroups (client vein groups; the server's veins use ResourceVeinGroupVeins).
- Movers' areas (client builds them from the spawn's static area id): GlobalAreas, GlobalAreaShapes, AreaProperties —
  now **used** by the server's mover spawns (E.1).

## C. Client-loaded, EQN / PS2 systems — not written off (TASKS E.2 rule)

ProjectileDefinitions (ranged projectiles: speed / gravity — the server resolves hits without travel time [I]),
MerchantSets ("Generic Merchant"), FireModes / FireGroups
/ FireModeEffectGroups, Skills / SkillLines / SkillSets / SkillCategories / ProfileSkillSet(s), VehicleArmorMappings,
FacilityZoneMap, CraftingStationCatalystMappings (Forge 48 → catalyst 33; "catalyst" appears in Landmark only as
flavour text, 15 Dec 2014), ItemGroups / ItemGroupMappings, StatCategoryDefinitions, CharacterStatDefinitions,
AttributeDefinitions / AttributeEntryDefinitions, AbilityParameterDefinitions (client tooltip numbers,
gear-stats-695.md), KeywordDefinitions, HeroicMovement (systems file), ContentPacks, PropRecipes (systems/building.md).
Each needs the E.2 proof (0.695 caller, data, design source) before it counts as unused; open in L138.
