# Gear stats: max life and damage from armour (TASKS 5.1, client 0.695)

Traced 2026-10-16 from the 0.695 decompile (`re/decomp_all_695`, `DECOMP=decomp_all_695 python re/tools/fn.py`),
the 0.695 data (`gameData695`), the live item records (`gameData/items.json`, `re/out/items_by_category.txt`), the
UI (`re/as3/CharacterEquipment`, unchanged in 0.695) and the wiki / forum dumps.
[P] = proven from client code, client data or a live capture. [I] = inferred (reasoned, or our number).

## 1. Where the client shows max life and damage

| What the player sees | Data path | Tag |
|---|---|---|
| HUD life bar (CombatHud) and character sheet "Health" line | `LocalPlayer.Resources.Health` row: `CurrentValue`, `Minimum` 0, `Maximum`, written by `LocalProxiedCharacter::OnResourceChanged` FUN_143329090 (part_0099 ~2302). Health entry: Current = FUN_142ccbb60 → FUN_143b8c2b0(entity+17000, type 1); Maximum = FUN_142cd0230 → FUN_143b8c310(entity+17000, type 1). The same call feeds the HUD (FUN_142e80670(type, current, max)). | [P] |
| Character sheet stat list | `CharacterEquipmentDocumentClass.getStatsDataProvider` = rows of `GameData.Character.StatsList` + `mergeLocalResources`: Health / Armor / Energy / Stamina = the resource row's `Maximum` (re/as3/CharacterEquipment/scripts/CharacterEquipmentDocumentClass.as:283-327). CharacterEquipment.swf is not in the 0.589→0.695 changed-file list (re/tools/as3export695.py), so 0.695 uses the same script. | [P] |
| `Character.StatsList` rows (calculated stats) | FUN_142e5e460 / FUN_142e5e100 add a row per calculated stat only when the stat definition's HIDE_FROM_UI (+0x48) is 0 and its CATEGORY_GROUP_INDEX_ID (+0x4c) is a StatCategoryDefinitions row; Name = NAME_ID (+0x40), Value via FUN_143ba9b60 (DISPLAY_MULTIPLIER +0x60, PRECISION_ID +0x58), SortOrder +0x44, ShowAsPercentage +0x50, Description +0x5c. **Every row of 0.695 CalculatedStatDefinitions has HIDE_FROM_UI 1**, so this list is always empty: the sheet shows only the merged resource maxima. | [P] |
| Target frame / nameplate bar of another entity | ClientRequirementTargetMaxHealth FUN_143711bb0 → FUN_143b8c310(target+17000, type 1) — the target's Life resource. | [P] |
| Attack numbers (ability tooltips, client-side parameters) | ParameterCalculator FUN_1435079e0 (AbilityParameterDefinitions, manager game+0x32220): type 0xFADE58C / 0x570B0B99 = calculated stat (local player stats at entity+0x7e8, FUN_142ccb400) × PARAM_2 + PARAM_3; type 0x6BABD7D4 = resource current (FUN_143b8c2b0) or max (FUN_143b8c310) × P2 + P3. 296 of the 380 StatMultiplier rows read stat 10 (Damage), 7 read stat 1 (Life), 16 ResourceMultiplier rows read resource type 1 / 11. | [P] |

### Resource maximum (the number that matters)

- FUN_143b8c310 finds the resource of that type in the entity's container (bucket `+0x28 + (type & 7) * 8`, record
  type at +0xe8) and calls FUN_143217a00: walk the record's variable bucket at +0x60 for variable id
  **0x2C7CD6E6**; found → `(int)(f32 +8 + f32 +4)`; not found → the resource definition's MAX_VALUE
  (FUN_143c77750(...)+0x4c). [P]
- 0x2C7CD6E6 = MAX_VALUE: FUN_143c77590 (part_0146 ~2999) builds a missing variable from the definition — 0x51AD9D,
  **0x2C7CD6E6 → def +4 (MAX_VALUE, Resources loader part_0146 ~3667 stores MAX_VALUE at +4)**, 0xD1B5E30D → +8
  (REGEN_PER_MS), 0xC4403E66 → +0xc, … A default entry is FUN_143c771f0 `{+0 id, +4 base, +8 modifier = 0}`. [P]
- So **value = base + modifier**, and the server can raise a player's maximum life by sending the variable.

### SetCharacterResource record (ResourcesBase 0xC4 sub 2), with variables

Header `u32 sequence, u8 sub, u64 guid` (handler FUN_14361cd30; the sequence goes through the shared sequenced-packet
check, part_0067 case 0x9E — keep one increasing counter per receiving client). Record reader FUN_142c18870:

```
FUN_142c2ec50:  u32 resource id, u32 resource type,
  FUN_142c3c070: u32 variable count, count × { u32 key, FUN_142c2ed20: u32 variable id, f32 base, f32 modifier }
FUN_142c18a60:  10 × u32 (+0x70 = current value first)
u32 <computed>
```
[P]. Applying a Set: FUN_143550610 → FUN_14354f240 copies the record over the existing one and **clears the variable
list first** (FUN_142d32d30, then copies each variable) [P] — a Set without the variable resets the maximum to
Resources.txt's value. Update (sub 3) reads FUN_14361bb40 and applies with FUN_143550820 (not used by the server).

### ClientUpdate Hitpoints (0x29 sub 1)

Reader FUN_142c1c7c0 `{u32 current, u32 max}`; the dispatcher (part_0065 ~13326) only builds a combat-text entry
(FUN_1432707e0 with `current * 100 / max`) — it does not move the bar (as docs/protocol.md §4.4 says). [P]

### UpdateCalculatedStats (0x29 sub 0x2C)

Dispatcher part_0065 ~14426 → reader FUN_142c49070 `{u64 guid, list {u32 stat, f32 value}}` → entity FUN_142f85390 →
FUN_142e5d990(entity+0x7e8): each listed stat is upserted on its own (FUN_142e5de80) — **a partial list only changes
the listed stats** — then side effects: 3 harvest radius (FUN_14345a8d0), 4 light radius, 5 run speed (entity+0x740),
0x1D fly, 0x25 → entity+0x4a74; FUN_142e5e460 (StatsList row) and FUN_143512a10 when HIDE_FROM_TOOLTIP (+0x49) is 0.
None of the stats drives the life bar. [P]

## 2. The data

- **CalculatedStatDefinitions** (loader FUN_143baad50: ID +8, PARENT +0xc, BASE +0x10, MAX +0x14, MIN +0x18, CALC +0x1c,
  STACKING +0x20, SEND_TO_OTHERS +0x24, NAME +0x28, NAME_ID +0x40, SORT +0x44, HIDE_FROM_UI +0x48, HIDE_FROM_TOOLTIP
  +0x49, CATEGORY +0x4c, SHOW_AS_PERCENTAGE +0x50, PERCENT_OF_WHAT +0x54, PRECISION +0x58, DESCRIPTION +0x5c,
  DISPLAY_MULTIPLIER +0x60). Stat 1 cStatTypeHealthBonus, NAME_ID 558897 "Life" — **the same text as the Life
  resource (Resources.txt row 4)**; stat 10 cStatTypeDamage, NAME_ID changed 50081 "Offense" (0.589) → 577652
  "Damage % Multiplier" (0.695). All rows SEND_TO_OTHERS 0, HIDE_FROM_UI 1, HIDE_FROM_TOOLTIP 1. [P]
- **Resources.txt** row 4: type 1 Health, NAME_ID 558897 "Life", INITIAL / MAX_VALUE 900, REGEN_PER_MS 0.05,
  REGEN_DELAY_MS 10000, PACKET_BROADCAST_RANGE 30. [P] (The broadcast range is a server column; reading it as "send
  life changes to players within 30 m" is [I].)
- **ItemCategoryStatMappings.txt**: 8 rows, only harvesting stats (12, 3, 13) for categories 2, 3, 34 (tools). No armour
  rows. **CharacterStatSetMappings.txt** is empty; **CharacterStatDefinitions.txt** has ids 1-47 with no names (the
  UpdateStat / movement stats, not life). [P]
- **Live item records**: `stats` is empty for all 2,885 definitions (gameData/items.json). An armour's stat values were
  never in its definition: they were server-side (the tooltip's stat list came from ItemTooltipReply). [P]
- **Armour weight = item category** [P]: 286 "Light Armor", 285 "Medium Armor", 157 "Heavy Armor" (live
  ReferenceData ItemCategoryDefinitions dump → re/out/items_by_category.txt). The 28 chest armours (slot 8) carry
  exactly one each and match the wiki's categories, also where the model name disagrees (Frost Lord's Guard = medium,
  model `Qeynos_Heavy_003`; Lightbringer's Oath = light, model `Qeynos_Medium_002`). Light: 1466, 1491, 1532, 1537,
  1538, 2252, 2254, 2256, 2260; medium: 1495, 1530, 1531, 1536, 1540, 1541, 2251, 2253, 2255, 2259; heavy: 1533, 1534,
  1535, 1539, 1942, 2249, 2250, 2257, 2258.
- The same records hold two more weight-linked fields after quality (`u41_46[1]` 364 / 365 / 366 and `u41_46[3]`
  374 / 375 / 371 for light / medium / heavy). Meaning unknown → open lead below. [P that they correlate]
- **Live UpdateCalculatedStats capture** (DefaultCalculatedStats.cs; MiscHandler.cs comment): stat 1 = 1800,
  stat 10 = 70, stat 22 = 6, stat 24 = 160. The capture's era and gear are unknown. [P value / I meaning]

## 3. Design sources (rules, no numbers)

- Wiki *Armor Weight*: light = small max-life bonus, large damage bonus; medium = balanced; heavy = large life, small
  damage. *Armor*: weight is a trade-off; you cannot swap armour in combat. *Affinity*: affinity changes the bonuses
  (already the Maximum Affinity Effects, Combat/Gear.cs).
- Update notes 25 May 2016 (forum 57170): "Damage calculations have been adjusted to better accommodate heavy and
  medium armor, meaning all weapons now deal slightly more damage when wearing those armor types."
- Forum, 26 Apr 2016 (56999, a player): crafted armour "all add bonus life and damage in various amounts", no armour stat.
- No source gives the launch numbers (2014 placeholder sets — Soldier chest +200 health — are pre-launch).

## 4. Server implementation (2026-10-16)

| Piece | Where | Tag |
|---|---|---|
| Weight from the equipped chest item's category (286 / 285 / 157) | Game/Combat/GearStats.cs `WeightOf`, `Worn` | [P] |
| Max life = 900 + light 200 / medium 550 / heavy 900 (1100 / 1450 / 1800); damage × 1.30 / 1.20 / 1.10; none = 900 / ×1. Bonus halved at 0 % armour condition (as weapons, Durability.Strength) | GearStats `bonuses`, `MaxHealth`, `DamageFactor` | [I] ours; heavy 1800 = the capture's stat 1 |
| Life record carries the MAX_VALUE variable {0x2C7CD6E6, 0x2C7CD6E6, f32 900, f32 max−900} whenever max ≠ 900 (a Set without it resets to 900 = unequipped) | CharacterResources.SendFor(`maximum`), Health.SendLife | [P] layout |
| HUD bar, character sheet Health, current kept as the same share on equip / unequip (full stays full) | Health.Max, MaxChanged, SendHitpoints | [P] path / [I] share rule |
| Viewers: Life (with max) to players within 30 m on every change, and on meeting when not the default full 900 | Health.SendHitpoints, IntroduceDead | [I] (PACKET_BROADCAST_RANGE 30) |
| Hitpoints {current, max} with the real max | Health.SendHitpoints | [P] |
| Calculated stats: 1 = max life, 10 = round(70 × factor) — full list at entry, two-entry list on change | Abilities.CalculatedStats, GearStats.Refresh | [P] mechanism / [I] stat 1 = total life |
| Server weapon damage = 70 × armour factor × weapon condition (all player hits scale from it) | Abilities.DamageOf | [I] numbers |
| Trigger: equip / unequip (WeaponCombat.CheckWeapon from WorldSession.SendEquipmentChanges), each hit taken (armour wearing to 0 %), world entry (GearStats.Remember) | GearStats.Refresh | — |

Bandwidth: unarmoured players send exactly what they did before (no variable). Armour adds 16 bytes per Life update;
changes go only on change; viewers only within 30 m.

## 5. Open leads

1. The launch life / damage numbers per weight (server-side; ours now). Retry: more forum / Reddit / video archives
   (character sheet "Health" screenshots with known armour), the wiki images `*_-_Info.jpg` (armour tooltips).
2. Stat 1 "Life": total max life or a bonus? The capture's 1800 vs Resources.txt 900 fits either a heavy-armour player
   (total) or 900 + 1800. Ours sends the total.
3. Item fields `u41_46[1]` (364/365/366) and `u41_46[3]` (374/375/371) follow the weight: find their client offsets
   (definition +0x15c / +0x164, item reader FUN_142b800a0's 0.695 twin) and users.
4. Armour tooltips: the live ItemTooltipReply stat list probably showed Life / Damage lines for armour (as tools show
   Harvesting stats); Inventory.cs fills only tool stats today.
5. Callers outside this change still use the 900 base (`Health.MaxHealth`) and should use `Health.Max(session)`:
   Groups.cs:583 (party frame max), Consumables.cs:38 ("already at full health" — refuses healing potions between 900
   and an armoured max) and :403-404 (campfire), InstancedGames.cs:1269 / :1352, Buffs.cs:375, StoryTools.cs:639.
6. Repairing broken armour restores the bonus only at the next equip / hit taken / weapon swing (GearStats.Refresh is
   not called from the repair path, Durability.cs).
