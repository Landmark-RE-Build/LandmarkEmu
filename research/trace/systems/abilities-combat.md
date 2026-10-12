# System: abilities, combat, weapons, effects (Abilities 0x27, Combat 0x24, PlayerUpdate effect subs)

Status: [~] started — findings recorded as they come; full pass pending.

## Ability data (what the live server had)

- AbilityDefinitions.txt + AbilityEventMappingDefinitions.txt: server-only (loader FUN_143ae2670, schema types
  "Ability" / "AbilityEventMapping"). Columns of the ability definition (schema FUN_143bef660): CAST_REQUIREMENT_ID
  COST_RESOURCE_TYPE_ID COST_RESOURCE_AMOUNT DURABILITY_COST CAN_CAST_WITHOUT_RESOURCES COOLDOWN NAME_ID ICON_ID
  DESCRIPTION_ID SKIP_OFFLINE_TIME HIDE_FROM_CLIENT IS_HARVESTING IS_DEFAULT_LEARNED ITEM_COST_COUNT ITEM_COST_ID
  TRIGGERS_COOLDOWN NOT_IN_COMBAT CRIT_EFFECTS_ID ROOT_MOVEMENT_ALLOWANCE ALLOW_WHILE_GRAPPLED APPLY_HASTE TOGGLEABLE
  IGNORE_DEFENDING DEFENSE_ENERGY_PARAM_ID NO_COUNTER_ATTACK STAGGER_RECOVERY CAN_INTERUPT_FOR_ABILITY
  CAN_CAST_DURING_RECOVERY USE_RT_MTN_FRM_PREV_ABILITY MELEE_ASSIST_START_TIME/END_TIME/SPEED/TARGET_RANGE/FIELD_OF_VIEW
  OVERRIDE_COOLDOWN_ID (+ base columns: id, cast/recovery timing, animation, range — to read from the base loader).
  Event mapping: ABILITY_ID EVENT_ID (schema FUN_143befd00).
- AbilityEx.txt (client, newer system) holds only a "Test" row: ABILITY_EX columns (expire, first/next/reuse delay,
  resource type/threshold/first cost/cost per ms, requirement sets, run on client/server flags, scopes, next ability,
  distance/radius, 14 params, 4 strings).
- AbilityParameterDefinitions.txt (746 rows, client; FUN_143ae3c50 ParameterDefinitionManager): parameter formulas
  (TYPE_NAME e.g. AbilityParamTwoStatAndCharged, Constant; USE_CASTER; PARAM_1-6; BOOL_PARAM_1-2) — the damage /
  effect formulas abilities reference by parameter id. **Not read by the server.**
- AbilityTellDefinitions.txt (telegraphs: ability, heading/distance offset, start, duration, composite effect),
  AbilityLines / AbilityLineMembers / AbilitySets / AbilityClasses / AbilityEffectTypeSettings (small).

## Effects

- ActorCompositeEffectDefinitions.xml (client): 1610 composite effects, id → name + parts (PARTICLE / LIGHT / SOUND /
  FX ...), list in ../composite_effects.tsv. Combat ones: PFX_Combat_Misc_EQNPrototype_Impact_Hit_Physical_Directional_
  Combo1 10043 / Combo2 12117 / Combo3 11844 / HeavyAttack 12119; Arrow Combo1-3 12159-12161, HeavyAttack 12162;
  GreatBow 12218-12220, 12244; Ice 12163/12164; Lightning 12216/12217; weapon trails (10042, 11352, 11669, 11987,
  12118 heavy); per-weapon ambient (PFX_Weapon_Fire_Sword 11386, Ice_Staff 11387, Divine_Mace 11332 ...); surface
  impacts pfx_Combat_Weapon_Impact_Grass/Dirt/Ice/Leaves/Sand/Snow/Stone 12744-12750.
- PlayerUpdate effect messages (trace/playerupdate_subs.md): 0xB PlayCompositeEffect {u64 entity, u32 effect, u32,
  u32, bool, bool, bool} (reader FUN_142bb2f10), 0xC StopCompositeEffect, 0x19 TriggerEffectPackage, 0x1D-0x1F effect
  tag composite effects, 0x56 PlayWorldCompositeEffect, 0x78 DoCombatNotification, 0x2 Knockback. Server sends none.
- Client tables not read by the server: TriggerFXMap (313: animation trigger → fx), TriggerFxTag (307), ClientEffects,
  ScreenEffects, FireModeEffectGroups, InteractableCompositeEffects/Sets, MaterialTypeTriggerDefinitions.xml.

## Weapons (done before the trace; see MODLOG)

Animation class per wielded model (.adr enumerations → gameData/WeaponAnimationClasses.txt), Enter/Release
selections per class and AttackIndex (WeaponAttacks.AttackAnimation). Open: hit effects (PlayCompositeEffect with the
impact ids above), trails / ambient per weapon, combat text (0x78), knockback (0x2), the right-click stop reported
2026-10-12 (no casts reached the server: client-side refusal — Energy? cast pending?).

## Combo state machines (client-side)

DesignBased* tables (StateMachineMap 146, StateDefinition 136, StatePropertiesDefinition 132 {ACTION_TYPE, PARAM_1/2,
START_DELAY, STATE_DURATION}, StateToStateTransitionMap 147, TransitionRuleDefinition 127 {TRANSITION_TYPE_ID,
PARAM_1, START_DELAY, DURATION}, TransitionGroupRuleMap 121, TransitionGroupDefinition 107; managers
DesignBasedStateMachineDefinitionManager FUN_143b0cc30 / ClientDesignBasedStateMachineManagerManager): a data-driven
state machine. Rule 1 {type 1, param 16, 0.25, 1.4} matches InputActionEntryComboMap 16 → 74 (0.25 / 1.4), and state
actions' params (9, 11, 19 ...) look like input action group ids — i.e. the weapon combo chains: states switch the
active input group, so the client itself picks the next cast id (760 → 816 ...). To confirm: which entity / item
selects a state machine id, and what ACTION_TYPE / TRANSITION_TYPE values mean (read the state machine code).

## Abilities protocol (0x27, u16 0x27 + u8 sub; dispatcher FUN_142d7cae0; families/0027_Abilities.md)

Client → server
| Sub | Message | Server |
|---|---|---|
| 0x1 | CastAttempt | handled (Abilities.HandleCast) |
| 0x2 | CasterInfoRequest | handled (captured + built replies) |
| 0xE | **FinishChargedCast** (charge fraction = held time / charge time, builder FUN_142d767b0) | **not handled** — charged attacks (likely the right-click power attacks) wait for the server; prime suspect for "right click stops working" |
| 0xF | **CancelCast** {ability} (FUN_142d71590) | **not handled** |
| 0x13 | DefaultInfoRequest | handled |
| 0x16 | EffectUpdatesRequest | handled (Buffs.Resend) |
| 0x17 | DelayedTargetInfo | handled |
| 0x18 | CollisionReport | handled |
| 0x1E | RequestCancelEffect | handled |
| 0x23 / 0x24 | ClientTargeterEnd / FindTarget | **not handled** (ground-target / aimed abilities) |

Server → client (handler functions; what they show)
| Sub | Message | Client does | Sent? |
|---|---|---|---|
| 0x3 | CasterInfoReply | ability definition for the client | yes |
| 0x4 | AbilityStart | starts an ability on an entity (AttackAngle) — other players' casts | no |
| 0x5 | AbilityEnd | ends it | yes |
| 0x6 | CastFail | "receive CastFail" + Harvest_CastFailMessage | no |
| 0x7 | CastSuccess | confirms a cast (+ animation id) | yes |
| 0x8 | CastContinue | continue a charged / channelled cast | no |
| 0x9-0xB | TargeterStart / Update / End | targeting shapes | no |
| 0xC | EffectStart | effect + animation direction (Morpheme variable) | no |
| 0xD | EffectEnd | **harvest results**: "Complete success", "Partial success: %f", Harvest_Fail_Tier / _Prevent / _Multiple / _NotAllowed / _IsPulverizer / _Tier_Specific | no |
| 0x10 / 0x11 | AbilityStartFailed / StartCanceled | GameEvents:OnAbilityCastResult | no |
| 0x12 | PlayAnim | animation selection (+ Translation) | yes |
| 0x14 | DefaultInfoReply | | yes |
| 0x19 | PlayVfx | visual effect | no |
| 0x1A | UpdateEffect | | no |
| 0x1B / 0x1F / 0x22 | Lower / Clear current cooldown, ClearAllCooldowns | cooldown bars | no |
| 0x1C | EffectInitialized | | no |
| 0x21 / (ShapeEnd) | ShapeStart / ShapeEnd | ability shapes (areas) | no |

To read next: the client's cast state (pending cast, charged state +0x2c4/+0x2c8, what ends it), CastContinue /
CastFail layouts (both {u64 caster, u64 cast guid, u32 ability, u32 ...}), how other players' casts are shown
(AbilityStart vs PlayAnim), Combat 0x24 (auto attack, single attack target), 0xBA family (melee stab / projectiles).

## Combat 0x24 and Weapon 0xBA (engine weapon system — not used by 0.589)

- Combat 0x24: C2S AutoAttackTarget 0x2, AutoAttackOff 0x3, SingleAttackTarget 0x4; S2C AttackTargetDamage 0x5,
  AttackerMissed 0x6, TargetDodged 0x7, AttackProcessed 0x8, EnableBossDisplay 0xA, TargetBlocked 0xB, Parried 0xC,
  BlockBroken 0xE. None ever received in play.
- 0xBA (MeleeSlash/Stab, WeaponFire*, fire groups, ammo / reload / chamber, fire modes, lock-on, guided weapons,
  remote weapon add/remove/reset, projectile launch / detonate hints, ProjectileSpawnNpc, QuickUse): the shared
  firearm system (PlanetSide-style). 0.589: Weapons.txt is header-only, weapons are item abilities driven by
  InputActionGroupEntries; none of these packets was ever received.
- Kept for completeness; the server does not need them unless a 0.589 item uses a Weapons.txt entry (none do).
  Exceptions worth checking: EnableBossDisplay 0xA (boss health bar), Blocked / Parried / Dodged texts (could the
  ability system show these?), ProjectileSpawnNpc.

## Deep notes (2026-10-12)

- CastSuccess 0x27/0x7 (handler FUN_142d7a370; our model AbilityCastSuccess): u64 caster (+0x20), u64 cast guid
  (+0x28), u32 cast hash (+0x30), u32 ability (+0x38), u32 animation (+0x3C: played via FUN_142d5dfa0 when the cast is
  not the caster's current one or the hash differs), u32 U40 (+0x40: when non-zero the actor's vtable +0x4A8 method
  FUN_142e199a0 runs with it — an effect / event id on the caster; same family of calls as composite effects), u32 U44
  (+0x44: actor vtable +0x250 FUN_142df2e00(actor, 1, U44) — queues something on the actor, +0x4420 list), bool force
  (+0x48: treat as the current cast). We always send U40 = U44 = 0.
- PlayCompositeEffect PU 0xB (PlayerUpdate_Dispatch case 10, reader FUN_142bb2f10): u64 entity, u32 effect id, u32 a,
  u32 b (both × DAT_144b72d94 → seconds: delay / duration), bool, bool, bool. Entity kind 2 (location) → plays at a
  world position (FUN_142c77970), else on the actor (FUN_142c77770), optional attach (FUN_142df3400).
- Charged casts (FUN_142d767b0): only for CasterInfo kind hash 0xF7150354 (-0x8eafcac); min / max charge times are
  CasterInfo floats (+0xB8 / +0xB4 of the entry), charge-scaled params +0xCC / +0xD0; on release the client sends
  FinishChargedCast {ability, aim data}. Our weapon combos answer with the instant kind 0xDFB05E5F, so the client never
  charges them; real charged abilities would need CastContinue / CastSuccess after FinishChargedCast.

## Client constants (trace/client-constants.md)
Revive: ReviveToken item 1579 (Lumens) × ReviveTokenCount 15 to revive in place (respawnhandler.lua: "x15 Here",
enabled when the bag holds ≥ 15 Lumens; "Flee" = nearest safe point, shown if Respawn.ShowGraveyardRespawn; hub
button hidden). Character.Revive.AbilityId 1170, Abilities.ReviveInteractionAbilityId 246 (reviving another player).
Abilities: StaggerRecovery 1.5 s, CancelCastAnimId 347, TargetSearchDistance 30, PcTargetRadius 2, active-time anims
509 / 510. Combat: personal damage text fade 1.5 s. Grapple.Cooldown 10000 ms. Flinch (unused).

## Live behaviour (wiki, launch 2016: Combat, Weapons, Monsters (PvE), Gear and Equipment, Stats, Affinity, Maximum Affinity Effect, Outposts, Chaos Caverns, PvE, Armor, Caves, Enemy Factions)
- **No classes**: one "Adventurer"; the equipped weapon + armour define the combat style (melee, ranged, spells).
  Twitch / movement based (no tab-target hotbar). **PvP removed before launch.**
- **Weapons** live in the bag and are equipped from the hotbar (number key / click). Three attacks: **Primary
  (left mouse), Secondary (right mouse), Special (E)**; Alt over a weapon in the inventory shows all three. Every
  weapon has an affinity. Innate ranged: Gunslinger's Six-Shooters, Supercooled Power Cannon, Wand of Boundless
  Light, Wanderer's Blazing Bow; innate melee: Falchion of the Inner Spark, Poison Soaked Daggers, Armament of
  Unrelenting Vengeance. Non-innate (recipe drop / Lumens): Frost Lord's Fury, Staff of Frozen Sorrow, Overcharged
  Recon Blaster, Staff of the Lightning Caller, Diabolical Scientist's Raygun, Flame Bound Bastion, Frenzied Slayer's
  Battle Axe, Electrified Assault Axe, Armored Brawler's Blade, Crusader's Courage, Spear of Piercing Judgement,
  Blight Warden's Pain, Unflinching Embodiment of Might, Avenging Champion's Exaltation, Night Herald's Mercy.
  May 25 2016 update rebalanced energy costs vs damage (0.589 is right after — check its ability data).
- **Affinities**: Fire, Water, Lightning, Earth, Affliction, Radiance, Shadow (+ older Primal / Physical). Items of
  the same affinity stack: armour / accessories add conditional effects to that affinity's weapon (e.g. Fire:
  Erupt, Frenzy, Burn; full set → Erupt summons a blazing cyclone, DoT) = "Maximum Affinity Effect". Armour
  "weight" trades protection for speed. Appearance slot for outfits. Armour can't be swapped in combat.
- Start gear: Grappling Hook (Q fire, Space release), Adventurer's Pick and Logging Axe, basic armour and a weapon.
- **Monsters**: launch = only Fiery Fungus ("boom shroom", explodes when approached, drops rock / dirt / sometimes
  loot) on the surface and in surface caves; everything else in **Chaos Caverns** (reached only via the Spire travel
  menu "Adventure"): Chomper (stationary plant, vomit stream + chomp, 360°), Slaug (hides in crevices, strong
  knock-back), Abomination (slithers, gas, 1- and 3-projectile spit, tongue lash, retreats), names / colours vary by
  biome (e.g. "Parched Slaug" in deserts). Each makes a distinct sound on approach. Enemy factions (Barren, Ebonguard,
  Highfall, NovaTech, Stoneskin, Scalewind) exist as Creature Props for story tools.
- **Outposts** (player-built, contest winners, in the largest caverns; name + date shown on approach): monsters + 1
  **Elite Monster**, 1 **Lost Chest** (dig it out; more loot than common chests), 1 **Lost Crystal** (activates only
  after the Elite dies; best loot; then offers travel to a new cavern). Surface caves: common chests (resources).
  Anyone within 50 m when a chest is opened gets loot; party members share loot / harvests (200 m).
- **Death**: revive here (15 Lumens) or go to a graveyard / safe point ("Flee"); gear takes durability damage →
  repair at a Repair-o-Matic (Lumens) or with a Repair Bot Beacon item.
- Stats (beta formulas, may have changed): base run speed 10 units/s, +1 per Run Speed point; Harvest Bonus: 1 % per
  point extra resource (cap 100); Harvest cast time = 50 / Harvesting Speed s; Harvest Size ≈ +1 voxel of sphere volume
  per point; Light Radius / 10 = spotlight radius.

## Reported issue resolved: right-click stops working (2026-10-12)
Measured on the test client (re/testcopy/hook_resources.js, probe_power.js — the client's own cast check, vtable +0x110
of the action manager at game+0x32128, and cast +0xa0, as Lua AbilityStart uses them):
- The client regenerates Energy itself (resource type 2: ~20 per second from Resources.txt; REGEN_DURING_CAST /
  ACTIVE are 0 but casts end, so it keeps regenerating).
- Power attacks cost 60 (our rule, WeaponAttacks). With the cost unknown to the client (CasterInfo IgnoreCostPool,
  cost 0) it kept casting at low Energy and the server answered an empty success → "nothing happens / no animation".
- Fix (commit after 3e6bcba): weapon CasterInfo carries CostResource 2 / CostAmount; the client now refuses below the
  cost (check result 3 = AbilityCastError.NoMana) and every sent attack is paid. Holding right-click therefore fires
  about every 3 s once Energy is spent — live's design (power attacks cost Energy); the live costs themselves are
  server data (May 25 2016 rebalance), ours are from the wiki weapon pages (55-74).

## Weapon kits and affinity states (implemented 2026-10-12, Game/Combat/)

- Per-weapon attacks: text pairs "Basic Attack: X" / "Power Attack: Y" + descriptions (573028 … 576436; a few
  descriptions are out of sequence: Hateseeker 574882 / Nightfall 574883, Agonizing Volley 576222, Ooze Bolt 573071,
  Decimate 573202 / Obliteration Turn 573203, Overcharge 576436; Terminal Voltage, Rolling Thunder, Sludge Storm have
  none). Weapon affinity = item description id (574424 Fire, 574437 Water, 574438 Lightning, 574439 Earth, 574440
  Affliction, 574441 Shadow, 574442 Radiance). Kits matched by affinity + weapon type in the description text.
- Properties (wiki weapon pages, "<Affinity> weapons … possess the following properties"): Fire Erupt / Frenzy / Burn;
  Water Flow / Surge / Freeze; Lightning Supercharge / Energize / Electrocute; Earth Harden / Regrowth / Crush;
  Affliction Infect / Rot / Cripple; Shadow Siphon / Torment / Condemn; Radiance Illuminate / Exalt / Rejuvenate.
  Name texts 574468-574494 (Freeze 565471). Icons: attack 12205-12217 (icon_abilities_<aff>), buff 12206-12218 (_buff).
- Armour MAE texts 575654-575681 (order of item names 575605-575632 → items 1466 … 2260); accessory texts 565622 …
  565743, 574932.
- Client caster-info cache (FUN_142d7a7d0) overwrites an entry on every CasterInfoReply → per-weapon names work for the
  shared cast ids when re-sent on weapon change.
- Open: charged casts (kind 0xF7150354, FinishChargedCast); location composite effects for lingering areas (PlayCompositeEffect
  target type 2 layout); real durations / numbers (server data).
