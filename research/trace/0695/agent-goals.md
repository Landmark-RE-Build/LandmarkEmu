# Journal goals: every objective counted (TASKS 5.10 / E.16) — agent report, 2026-10-17

Branch `e-goals` (worktree `<workspace>\wt-goals`), commit 3fa5309. Changed file:
`LandmarkEmulator.WorldServer\Game\Achievements.Triggers.cs` only (no hook call sites needed: the crafting hook
`Achievements.Crafted` already exists). `dotnet build LandmarkEmulator.WorldServer/LandmarkEmulator.WorldServer.csproj -v q`
succeeds.

## 1. The current list

`re/out/agent_wiki/objectives.tsv` (10-16) is stale: it predates Achievements.Triggers.cs, the tiers, the tutorial goals
and the meta-map counting, so its 679 "no" marks are mostly wrong now. A new audit replaces it:

- Script: `<workspace>\wt-goals\scratch\goals_audit.py` (scratch, not committed). It parses the capture
  `71-AchievementBase` with the server's own layout (port of `Achievements.Parse`), adds the 19 made dailies
  (DailyGoals.All), the 38 tiers (Achievements.Tiers.cs) and the 3 tutorial goals (Achievements.Tutorial.cs), then
  marks each objective with the code path that counts it. The matchers mirror the C# ones: literal ids in `Progress`
  calls and constants (incl. `cond ? A : B`), the id tables (harvest / biome / UI / place), daily ids (+588),
  `ProgressByText` literals, the trigger texts, kill family / element texts, Like-tag texts, `PlacedNames` (reorder,
  bare name, aliases, plural) for every prop with an item, `"<item> harvested"`, `"<VoxelMaterials name> blocks
  placed / removed"`, `CraftObjectives` (craftable item names, plural), recipe items (type 6 → output "recipe
  unlocked" + item name; type 45 → item name, `RecipeGoalByName`, prop names "recipe unlocked" / "unlocked"), design
  pastes by name, the recipe tracker goals and renamed recipe items (new, §2), and meta goals by capture map or
  "<name> completed" when the child goal is itself fully countable (fixpoint).
- Data: capture + `gameData695` (items.json, Recipes, PropDefs, VoxelMaterials, CraftingStationRecipeMappings) +
  `re/out/text_695.tsv`.
- Full per-objective table (objective, achievement, name, source, subcategory, text, required, counted, how):
  **`trace/0695/agent-goals-objectives.tsv`** (1,269 rows).

**Result: 958 goals, 1,269 objectives, all 1,269 have a counting path** (before this branch: 1,082 — the 186 recipe
tracker goals and "Recipe: Rug" had none; the audit's first pass also flagged 1272 / 9033 / 9034 / the material
block goals, which were script misses: `Game.Achievements.EquipWeapon`, `Progress(s, redo ? RedoBuild : UndoBuild)`,
VoxelMaterials names).

| Subcategory | Goals | Objectives | Counted |
|---|---|---|---|
| Recipe Discoveries | 569 | 569 | 569 |
| Replicator (recipe trackers: Armor, Weapon, Accessory, Outfit, Smelted Metal, Cut Gemstone, …) | 186 | 186 | 186 (new) |
| Decorating | 41 | 131 | 131 |
| Crafting | 15 | 81 | 81 |
| Social | 29 | 63 | 63 |
| Combat | 21 | 61 | 61 |
| Exploration | 14 | 40 | 40 |
| Tiers (Achievements.Tiers.cs) | 38 | 38 | 38 |
| Getting Started | 5 | 33 | 33 |
| Building | 16 | 25 | 25 |
| Tutorial goals 9001-9003 | 3 | 21 | 21 |
| Daily (made + 2 captured) | 21 | 21 | 21 |

| How counted | Objectives |
|---|---|
| prop recipe item name (Recipe Discoveries, `PropRecipeLearned`) | 513 |
| crafted: `"<item> crafted"` 77 + recipe tracker goals 186 | 263 |
| objective id in code (`Progress(session, id)`) | 100 |
| meta goal by capture map | 85 |
| recipe item name (type-6 recipe items, `RecipeLearned`) | 54 |
| `ProgressByText` literal | 48 |
| `"<prop> placed"` (`PlacedNames`) | 36 |
| prop recipe learned (`"<prop> recipe unlocked"`) | 33 |
| Like a tagged site | 26 |
| kills with an element weapon / kill family | 21 / 18 |
| dailies (DailyGoals, id + 588) | 21 |
| creature recipe learned (`"<creature> unlocked"`) | 18 |
| harvest / blocks by material / creature props placed / design pastes / trigger texts | 9 / 8 / 6 / 4 / 4 |
| `RecipeGoalByName` / renamed recipe item | 1 / 1 |

"Counted" means a server event reaches the objective. It does not prove every event is reachable in play (e.g. a
Recipe Discoveries goal needs its recipe item to drop: that is the item-sources audit's job,
trace/0695/item-sources-audit-695.md). Checked here: every recipe tracker goal's item has at least one default or
item-taught recipe at a station or station-less (`scratch/reach.py`: 0 failures).

## 2. What was built

### 2.1 Recipe tracker goals (E.16, OPEN-LEADS L43 first half) — `TrackerGoalsCrafted`
The 186 goals of the Journal's Replicator subcategories are each the `Recipes.TRACKER_ACHIEVEMENT_ID` of the recipe
making the item they are named after (186 / 186 name matches; one objective = the item name, required 1, 0 points, no
rewards, captured state 1). Live rule, patch notes 20 Apr 2015 "Upcoming Achievement/Progression Changes — Recipe
Tracking" (patchnotes.tsv P3248-P3253): a new Journal tab "houses a recipe achievement for each non-free recipe";
"You can complete these achievements by crafting the recipe once, but they do not reward any Founder Score or other
rewards"; the station's "Watch" button tracks it (0.695 CraftingView.as: `ObjectivesController.SetTrackingAchievement`
with the selected recipe's `AchievementId`). Client side: `Recipe.TrackRecipe` (FUN_1435ed290 → FUN_142f3d390) is a
client-only list, no packet.

Implementation: crafted item → the tracker goals of all recipes making it (`TrackerGoalsByItem`, built once from the
Recipes table, only ids that are goal records) → `Progress` on their objective, from `CraftedTriggers` (called by
`Achievements.Crafted`, which Crafting.cs already calls per finished job). [I]: crafting the item with *any* of its
recipes counts — the tracker id sits on one recipe per item (e.g. 1558 Cut Agate → 728) while the same item has other
recipes without one (1309, 1585, 1612, 1895); the goal is named and worded as the item. The other 183 tracker ids of
the table (e.g. 306 on recipe 38 Smelted Copper, the "Change Mod:" recipes, 1467 Roll Wraps) have no record, so the
client has no such goal and nothing is counted. "Breeze Boots" keeps both its crafting goal 2134 (1450 Believe it or
Not...) and its tracker 720.

### 2.2 "Recipe: Rug" (1286, objective 1949) — `RenamedRecipeGoals`
No item is named "Recipe: Rug". The recipe items 3478-3482 have name texts 573630-573634 in item order, except 3480,
now "Recipe: Ornate Rectangular Rug" with the late text 576611; 573632 is "Recipe: Rug". So the goal is 3480's
discovery goal from before the rename [I], and 3480 is the only rug recipe item without a goal of its own. Learning
3480 now also counts every objective of the goal named "Recipe: Rug".

## 3. Rules inferred from wording ([I]) that stay open leads

All of these count today; the rule is the goal's own text / description plus a wiki "?" hint, not a stated rule.
Searched for each: the goal page and its "Other Information" (design/wiki/pages), the goal and objective descriptions
(text_695 574780-574800), patchnotes.tsv, the forum dump (design/forum/all: no goal names found; only general
"secret" / "riddle" posts), client emote words (559123-559192), OPEN-LEADS L45.

| Goal | Objective | Rule in code | Why still open |
|---|---|---|---|
| 547 Ice Cold Brewskies | 951 Brewskies on ice! | a "Keg…" prop placed in a tundra | wiki "Place ??? in a Tundra Biome?"; description "just put that out in the snow! Ice cold brewskies on tap!" |
| 548 Better Than Reality | 952 Reality escaped | any painting placed in a desert | wiki "Place a Desert Painting … ?"; any painting or only "Desert Painting" not settled |
| 546 Gosh It's Hot | 950 Craziness in a volcanic area completed | a fireplace placed in a volcanic biome | wiki "Place a fireplace in a Volcanic Biome?" |
| 551 Hashtag Mutant! | 957 / 958 One is red! / One is black! | a Padded Sofa / Armchair tinted red / black (tint alias families [I]) | wiki "Dye one ??? red and one black?"; whether the two must be different seats is unknown (today the same seat may count both) |
| 552 Tint It Black | 959 / 2095 Door painted red / black | any door prop tinted red / black | red / black alias grouping [I] |
| 489 / 490 / 491 riddle Likes | 768 / 769 / 770 | Like a Western / SciFi / Fantasy tagged site | wiki "Western / SciFi / Fantasy Build Site?" |
| 500 To Me, Gunter! | 855 Express your sorrow in the right temperature | /sob or /cry in a tundra, grouped or not | wiki "/sad … (in a group?)"; the 0.695 client has no /sad word or Sad mood |
| 557 Iron From Ice | 964 Wood of iron burning | a Link to / from a blue Magic Fire emitter | L45 2026-10-17: Telltale GoT "Iron From Ice", ironwood burns blue (strong, not stated) |
| 556 …But Check Out the Bathroom | 963 | Outhouse Door ↔ Chandelier (Intricate) link, either direction | wiki "Link a Chandelier (Intricate) to an Outhouse Door"; description "door should link to a very fancy light" (direction) |
| 544 Makin' Bacon Pancakes! | 939 Painted Wooden Table, 2094 Square Platter | aliases to Table (Small / Large Painted), NovaTech Square Platter | no item of the goal's name |
| 1286 Recipe: Rug | 1949 | item 3480 | §2.2 |
| Replicator trackers | 186 | any recipe of the item | §2.1 |

Not goal objectives (so outside this count): the L45 wiki page names Bowl of Apples, Coiled Hanging Chain, Large
Square Knotted Rope, Dark Desert Succulent (+ With Butterflies), Glowing Jungle (Triple) Pendant Plant, Rusty Sifter —
no goal objective uses them (the goals use "Recipe: Coiled Chain", "Recipe: Large Knotted Rope", "Recipe: Rusty Sifter
(Large / Medium)", "Recipe: Plate of Apples"). L43's second half (what `VISIBILITY_REQUIREMENT_ID` means) is not about
counting and stays open.

Try next for the riddles: goal card images on the wiki pages (File:… thumbs) against prop models; archived 2016 goal
guides on the forum (not in the local dump); a capture of a live character's AchievementInitialize with any of these
complete would at least confirm they were earnable.

## 4. Test steps (0.695 test copy, `re/dlogin.sh`; dev commands from `Game\DevCommands.cs` via `dev_commands.txt`)

Restarting the dev world with this build is needed first (say so when doing it).

1. Recipe tracker goal by crafting: stand at a Replicator (or `station 53`), `give 51 250` (Agate), `craft 1558 1`
   (Cut Agate). Expect in the world log "completed achievement 728" and on the client the Journal's Replicator → Cut
   Stone → Cut Agate goal complete ("Completed achievement" message; AchievementBase sub 8 then sub 4). Before
   crafting, press "Watch" on the Cut Agate recipe: the goal shows in the watch list and leaves it on completion.
   Repeat with another recipe of the same item (e.g. 1895 if the station lists it) on a fresh character to check the
   any-recipe rule. Reset with `achreset 728`.
2. Same path without materials: `objective 1389` (728's objective) completes the goal; proves the client takes the
   record's completion. Startup log line "N recipe tracker goals for M items" appears on the first craft.
3. Breeze Boots: craft them → both 720 (tracker, objective 1381) and 1450 (objective 2134) complete.
4. "Recipe: Rug": `give 3480 1`, use the item from the bag (right-click) → "You have learned a new prop" and goal
   1286 "Recipe: Rug" completes (log "completed achievement 1286"); or `objective 1949`.
5. Regression: Recipe Discoveries for another recipe item (e.g. `give 3479 1`, use → 1287 completes, 1286 does not).
6. Audit rerun after any goal change: `python "<workspace>\wt-goals\scratch\goals_audit.py"` → expects
   "1269 counted, 0 not"; rows in `scratch/goals_audit.tsv`.
