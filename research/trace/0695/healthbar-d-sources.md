# Health bars, task D: live rules for tree / plant health, axe damage and yield; captures of a tree chop

Written 2026-10-31 for the bug report "trees are missing the health bar they had live; larger trees needed more hits,
better axes made more progress / yielded more per hit". Read-only research: no code changed, no game / Frida / Ghidra run.
[P] = proven (client data file:row, decompiled FUN_ address, live capture bytes, dated dev post or patch note).
[I] = inferred. Sister notes (A-C) cover the client side of the bar (HUD indicator 18, requirement 13, the resource
container, which packet moves the bar); this note gives the numbers and the rules.

## 0. Existing research checked first

- design/game-rules.md:13-37 (Mar 2014 "How Stats Work" summary: cast time = 50 / Harvesting Speed; harvest damage =
  HP taken per swing; voxel HP table). Nothing about tree HP.
- trace/systems/gathering.md:20-50 (launch tools from the wiki, "no hard tiers"), trace/0695/TASKS.md 6.12-6.14
  (plants decoration, one wood per biome, axe perks / Harvest Bonus on trees), trace/0695/gear-stats-695.md
  (resource container, SetCharacterResource layout, stat names), docs/unknowns.md:116-144 (item ItemRandom stats,
  tooltips), docs/gameplay.md:207-213 (our trees: 5 hits, 8 for large, 2-4 wood per hit), MODLOG 1058 / 1115 / 1165.
- **New in this note** (not in any earlier file): the client's tree / plant Health rows in Resources.txt, the 15
  "Tree: size: wood" profiles 200-214, the integer health-percent maths (why INITIAL = MAX - 1), the
  ProfileResourceMapping loader, the harvest-damage ability parameters, the live Adventurer's tool stats read
  straight from the capture bytes (speed is 60, not 50), and the 2014 dev rule for tree rewards.

## 1. Tree and plant health: the numbers are in the client data [P]

gameData695/Resources.txt (identical in gameData/ = 0.589; diffed 2026-10-31), RESOURCE_TYPE 1 = Health
(ResourceTypes.txt "Health^1"):

| Row | NAME_ID → text (re/out/text_695.tsv) | INITIAL_VALUE | MAX_VALUE | REGEN_PER_MS | PACKET_BROADCAST_RANGE |
|---|---|---|---|---|---|
| 18 | 560637 "Tree - Very Small" | 350 | 351 | 0 | 30 |
| 16 | 559208 "Tree - Small" | 750 | 751 | 0 | 30 |
| 14 | 559210 "Tree - Medium" | 1500 | 1501 | 0 | 30 |
| 15 | 559209 "Tree - Large" | 2250 | 2251 | 0 | 30 |
| 21 | 562052 "Tree - Very Large" | 3000 | 3001 | 0 | 30 |
| 17 | 559597 "Plant" | 10 | 11 | 0 | 30 |
| 20 | 559597 "Plant" | 20 | 21 | 0 | 30 |
| 110 | 559597 "Plant" | 30 | 31 | 0 | 30 |

- Trees do not regenerate (REGEN 0) [P data]. FLAG_PROFILE_SCOPE 1 on all of them [P data, meaning I].
- Independent confirmation of the Large row: player Ysovuka, forum 25683 post #15, 22 Mar 2014 (design/forum/all/
  2014-03-21-25683-how-stats-work.md:443): an earlier guide "argued that large trees have about 2,250 hit points";
  his blue Gold Axe "had a speed of 93.15 and a Damage of 81.87" → "2,250 hit point of damage in 28 swings" [P post].
- Patch notes: P0473 (26 Mar 2014) "Tree health has been rebalanced to better match tree size" — the five size rows
  [P note, link I]; P4129 (21 Mar 2016, launch) "Tree harvesting remains mostly unchanged" [P note].

### Why INITIAL is MAX - 1 (it makes the bar show on the first swing) [P maths, I intent]

ClientRequirementHealthPercent FUN_143710070 → FUN_143b8c370(entity+17000, type 1): returns
`(current * 100) / max` as an **integer division**, 0 when the entity has no type-1 resource or max < 1 [P]; compared
by FUN_14370e4b0 (operator 1 ==, 2 !=, 3 <, 4 <=, 5 >, 6 >=) [P]. A fresh Large tree is 2250*100/2251 = 99 → 99 < 100,
so Requirements row 5 TargetInjured is already true and row 18 TargetAlive (> 0) true; requirement expression 13's
tree branch then only waits for IsHarvestTarget (row 19, TargetOfPlayer type 9, FUN_143710fe0 compares the local
player's target-manager type for that entity with 9) [P]. A tree at full MAX would read 100 % and hide the bar until
the first hit landed. Designers made every tree / plant row one point "injured" [I], matching P0123 (12 Feb 2014)
"You should now only see the health bars of trees you're currently harvesting." [P note]. Side effect: below 1 %
(e.g. 22 / 2251) the percent truncates to 0 → TargetAlive false → the bar vanishes on the last sliver [P maths].

## 2. Which tree gets which row: profiles 200-214 [P names], mapping [I]

gameData695/ProfileDefinitions.txt names (NAME_ID → text_695): 200-202 "Tree: Very Small: Plain / Burled / Striped
Wood", 203-205 "Tree: Small: …", 206-208 "Tree: Medium: …", 209-211 "Tree: Large: …", 212-214 "Tree: Very Large: …";
90 "Plant - One Hit", 91 "Plant - Two Hit", 296 "Plant - Three Hit"; 39 "Prop", 40 "Non-Destroyable Prop" [P].
RequirementExpressions 20 "Target is Harvestable Tree" = TargetIsProfile200 … 214; 22 "Target is Plant Profile" =
90 / 91 / 296; 34 = plants + trees [P]. (Expressions 8 / 14-16 / 19 still name the 2014 tree profiles 56-58, 87,
100-105, whose ids 0.695 reuses for creatures, e.g. 57 = "COMBAT" model 444 — dead rows [P data, "dead" I].)

- The client looks for a ProfileResourceMappings.txt: FUN_143be1d40 builds the name (PTR_s_ProfileResourceMappings_txt
  part_0143 ~7647) and ProfileDefinitionManager::AddDefinition FUN_143bdecc0 reads records with columns PROFILE_ID,
  PROFILE_RANK, RESOURCE_ID (part_0143 ~5346-5370) → FUN_143cb4990 adds the resource id to the profile's rank record
  (profile +0x1a8 buckets, rank id at +0x140) [P]. **The file is not shipped** (absent from re/out/fileindex_695.tsv and
  both gameData folders) [P], and the live ReferenceData ProfileDefinitions dump
  (Resources/PacketDumps/41-ReferenceDataProfileDefinitions: 218 fixed 102-byte records, e.g. profile 200 at offset
  3884) carries no resource list [P]. So the profile→resource link was server data; the client learned a tree's
  health only from what the server sent for that entity [I, strong].
- Mapping by name [I]: size word → row (Very Small 18, Small 16, Medium 14, Large 15, Very Large 21); wood word = the
  biome's wood at launch (P4628 / P5415, TASKS 6.13). Plants: One / Two / Three Hit → 17 / 20 / 110 (10 / 20 / 30 HP:
  "one hit" = 10 HP suggests a sickle swing did 10 [I]). Plants were not harvested at launch (P4128, 21 Mar 2016:
  "Plants are no longer harvested, and sickles are no longer used for anything") [P note], so plant health only
  matters behind the server's plants toggle.
- Which model is which size is not in any client table (PropBase dump fields: model name, prop id, internal name,
  description, 2 × f32 scale, imageset, 2 × u32, model id; no profile) [P]. Proposal from the 0.695 placement-set tree
  models (ProceduralPlacementSetEntities → 145-PropBase prop → Models.txt name / PATH_BLOCK_RADIUS) [I]:

| Size (row, HP) | Models (name token; radius m) |
|---|---|
| Very Large (21, 3000) | Feerrott Banyans_Large01 (15.6) / Large02 (10.1); JaggedPine Hugeface01 / 02 (sets spawn them at 0.5 scale) |
| Large (15, 2250) | JaggedPine DiamondPine_Large01 / 02 (7), GnarledPine_Large01 / 02 (5 / 6.5); Everfrost Firs_Large01 / 02 (3.3 / 4.1), Grove_Large01 / 02 (4.5 / 5) |
| Medium (14, 1500) | JaggedPine_Medium01 / 02 / MediumBare01, Hollowed_Medium01 / 02; Everfrost Centipine_Medium01 / 02, SpindlePine_Medium01 / 02; Ashfang Paddle_Medium01, Thornleaf_Medium01, MediumDeadTree01; Feerrott Palms_Curvy01 / Tall01; Ashfang Cacti_Tall01-03 |
| Small (16, 750) | Feerrott Banyans_Small01 / 02; GnarledPine_Small01 / 02; Grove_Small01 / 02; Ashfang SmallTree01 / 02, SmallDeadTree01 |
| Very Small (18, 350) | Ashfang Trees_DeadLog01 (and anything the server treats as a log / sapling) |

  The palms and cacti are the least certain (no size word). Today the server uses 8 hits for radius ≥ 4.5 m, else 5
  (Gathering.cs:735-737, LargeTreeFootprint 4.5).

## 3. Axe damage, speed and how a hit's damage was computed

- **Damage per swing = the caster's Harvesting Damage stat** (CalculatedStatDefinitions 12 cStatTypeHarvestingDamage,
  "How much damage is done with your equipped tool") [P data]. Dev Avair, 21 Mar 2014 (forum 25683 #1, wiki Stats):
  "Each point of damage you get represents another 'hit point' taken away from the resource when you strike it. When
  they have no health they 'die'" [P post]. Client data agrees: AbilityParameterDefinitions 111-141 and 144 are all
  `StatMultiplier, USE_CASTER 1, stat 12, ×1, +0` (31 rows, one per harvesting ability [I]) — evaluated by
  ParameterCalculator FUN_1435079e0 as stat × PARAM_2 + PARAM_3 (gear-stats-695.md §1) [P data + P code]. Row 145
  (`StatMultiplier, stat 17 Harvest Bonus, ×0 +2`) sits with them [P data, use I].
- **Live Adventurer's tool stats, from the capture bytes** (Resources/PacketDumps/SendSelf.bin, live login 2017-02-20;
  item 3253 Adventurer's Mining Pick at byte 974, stats at 1006 / 1035 / 1064; item 3259 Adventurer's Logging Axe at
  1207, stats at 1239 / 1268 / 1297; each stat {u32 stat, i32 -1, f32 value, f32 1, u8 1, f32 value, f32 1, f32 1}) [P]:
  **stat 3 Harvesting Size 100, stat 13 Harvesting Speed 60 (0x42700000), stat 12 Harvesting Damage 80 (0x42A00000)**
  on both tools. docs/unknowns.md:117/:142 and Abilities.cs:962-982 say speed 50 — **the live value is 60** (our
  toolStats [3253] / [3259] and DefaultTool use 50; ToolDisplayStats shows 50).
- Speed: swing interval = 50 / Harvesting Speed s (dev 2014) [P post]; the client paces held swings the same way
  (MODLOG 1058: ability +0x68, 50 / stat 13 + base, FUN_142d77f20) [P per MODLOG]. Adventurer's axe: 50 / 60 ≈ 0.83 s.
- Which stats an axe shows: ItemCategoryStatMappings.txt category 34 (Axe, the u47 of every axe in items.json) → stats
  12 and 13 only; category 2 (Pick) and 3 (Sickle) → 12, 3, 13 [P data]. Tier / logging stats exist but were retired
  for axes: stat 26 cStatTypePropHarvestTier "Logging Tier", 52 "Reaping Tier" [P data]; P3108 (16 Apr 2015) "Harvest
  rating has been removed from all axes and trees, meaning all trees can be chopped by all axes" [P note].
- Other axes' numbers are **not on disk**: items.json `stats` is empty for every definition (live stats were rolled /
  sent per item) [P]; the capture's inventory holds no other tool with harvest stats (only the 6 stat records above
  exist in SendSelf.bin) [P]. What we know: P3107 / P3152 (Apr 2015) axes were redistributed to "a slightly lowered
  speed stat, but a significantly higher damage stat … much faster tree harvesting, particularly with higher tier
  axes" [P]; wiki (launch): Whirlwind "chops trees down faster", Goliath "more resources per swing, but swings slower",
  Charmed luck, Hotfoot speed boost after logging [P wiki]; items.json u41_46[1] groups them: Adventurer's pick / axe
  350, Charmed / Hotfoot / Whirlwind axes 351, Goliath axe 352, Logger's / Woodcutter's / Treesplitter's / Lumberjack's
  276 / 277 / 278 / 279 [P values; that it is a server stat-set id is I] — so Charmed, Hotfoot and Whirlwind probably
  shared base stats and differed by their perk [I]. Potion of Furious Harvesting: +25 % Harvesting Damage, 15 min
  (wiki; Consumables.cs:147) [P wiki].

Swings to fell (damage per swing vs INITIAL_VALUE) [I arithmetic on P numbers]:

| Tree | 80 (Adventurer's, live) | 100 (80 + 25 % potion) | time at 0.83 s / swing (80) |
|---|---|---|---|
| Very Small 350 | 5 | 4 | ~4 s |
| Small 750 | 10 | 8 | ~8 s |
| Medium 1500 | 19 | 15 | ~16 s |
| Large 2250 | 29 | 23 | ~24 s |
| Very Large 3000 | 38 | 30 | ~32 s |

(Alpha check: 2250 / 81.87 = 28 swings, the forum's figure.) Our server's 5 / 8 fixed hits are far below these.

## 4. Yield (what a hit gives)

- 2014 rule, dev Avair (quoted in forum 25683 #172, 29 Aug 2014, line 2222): "each % unit of health from the tree will
  grant you a reward set. At certain thresholds in the tree health, you will get a heartwood reward set … Speed has no
  bearing" [P post, alpha]. Player 28 Apr 2014 (#73): a medium tree "always gives heartwood 2x, 1 in the middle and one
  when you cut it down" [P post, alpha]. P0474 (26 Mar 2014): rewards "standardized across tree size to drop at a
  consistent rate regardless of tree size" [P note].
- Later: heartwood / special tree resources removed (P3123 Apr 2015, P3406 May 2015) — trees "still drop the same amount
  they have since the last time we messed with trees" [P note]; launch adds Tree Fiber + Tree Sap to all trees (P4129),
  their drop rate adjusted (P4629, 13 Apr 2016), harvest-bonus items gave too much tree loot (P4389, 1 Apr 2016) [P].
- Launch observations (wiki, 2016): NovaTech Harvestron "you might gather 2 wood when using most axes, but gather 5 wood
  while using this on the same tree"; Goliath Axe "trees yield 4-5 Burled Wood per swing, compared to the Founder's
  Pickaxe which gathers 2-3 per swing" [P wiki].
- So at launch a normal axe gave about 2-3 wood per swing [P wiki]. Whether that count came from the % of health the
  swing removed (the 2014 rule: 80 dmg on a Large tree = 3.5 % → 3 sets; but 80 on a Very Small = 22 % → 22 sets,
  which no launch source shows) or was a flat per-swing roll is **not proven** [I]. A flat 2-3 per swing (more for
  Goliath / Harvestron / Harvest Bonus) fits every launch source; with size-scaled HP that also makes "larger trees
  yield more in total" true, as the player remembers [I].

## 5. Captures of a tree being chopped

- **None on disk.** Checked: LandmarkEmu/LandmarkEmulator.WorldServer/Resources/PacketDumps (145-PropBase,
  39 / 41 ReferenceData, 44 CommandBase, 71 AchievementBase, PropTints.bin, SendSelf.bin, TerrainCellManifest.bin — no
  per-entity resource or hitpoint traffic); LandmarkEmu/NetworkCaptures (only _research/combat-loot-probe: 140
  S_LootPinataBase SpawnLoot rows from a live Courage session "5IewoKPQE2BqKg6Q"; items are ores / gems / Lumens /
  essences — no wood 71 / 72 / 73, so no tree chop in that extract) [P]. The session's raw bins
  (`5IewoKPQE2BqKg6Q_session/…/bins`, export-loot-outcomes.ps1:6) are **not in the repo or the workspace** (came with
  the org import ec67a615, 30 Oct 2026; find to depth 8 found nothing) — they may hold more of that session [P absent].
- The live UpdateCalculatedStats capture used for DefaultCalculatedStats.cs has stats 12 / 13 = 0 (no tool held) [P].
- Forum / wiki: no posted numbers of a launch-era chop beyond the per-swing yields above; the player's screenshot (green
  bar centred over the tree) matches HUD indicator 18 "Prop" (HudIndicators id 12, params 0.75 / 0.75).

## 6. What the server should do (D's part; carriers per sisters A-C)

1. Each harvestable tree node carries a Health resource: Resources.txt row by size (18 / 16 / 14 / 15 / 21), current =
   INITIAL_VALUE, max = MAX_VALUE (no max variable needed); a profile 200 + 3 × size + wood (0 Plain, 1 Burled,
   2 Striped) if the client's tree requirements must see one.
2. Per swing: damage = caster's stat 12 (tool ItemRandom stat 12 + accessories + buffs, e.g. +25 % potion); current
   -= damage; fell at ≤ 0 (replaces Node.Hits / MaxHits). Send the new current only to the chopping player (the bar
   shows only for one's harvest target, requirement 13; PACKET_BROADCAST_RANGE 30 is the table's own range).
3. Adventurer's Pick / Axe Harvesting Speed 60 (live), not 50: toolStats, DefaultTool, ToolDisplayStats, item records.
4. Keep ~2-3 wood per swing for normal axes (Goliath 4-5, Harvestron ~5), Harvest Bonus on top; a partly-chopped tree
   keeps its damage (REGEN 0) until it is felled / respawns.
5. Plants: only behind the plants toggle (live default off, P4128); then rows 17 / 20 / 110 for profiles 90 / 91 / 296.
