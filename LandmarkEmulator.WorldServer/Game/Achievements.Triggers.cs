using LandmarkEmulator.Shared.GameTable;
using LandmarkEmulator.Shared.GameTable.Text;
using LandmarkEmulator.WorldServer.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Goal triggers for objectives no other hook counted (TASKS 5.10; the "no server code counts this text" and "no
    /// prop item of that name" rows of re/out/agent_wiki/objectives.tsv). Sources for every rule: the objective and
    /// goal description texts of the live capture (71-AchievementBase, client text 0.695), the wiki goal pages
    /// (design/wiki/pages, "Other Information") and the item / prop names of the item dump.
    ///
    /// Rules marked INFERRED are not stated by any source in so many words; the evidence is given with each. Rules that
    /// rest only on a wiki "?" hint plus the goal's own description are listed in OPEN-LEADS L45 and can be revised
    /// there. Objectives are counted by id where the text is a riddle (the emote and bathroom goals), by text otherwise
    /// (so the tiers that share a text count too).
    ///
    /// 2026-10-17 (E.16): the 186 recipe tracker goals of the Journal's Replicator subcategories count when their item is
    /// crafted (<see cref="TrackerGoalsCrafted"/>), and "Recipe: Rug" counts for its renamed recipe item
    /// (<see cref="RenamedRecipeGoals"/>). With those, every objective of the 958 goals the server loads (capture,
    /// dailies, tiers, tutorial goals) has a counting path; the per-objective table is trace/0695/agent-goals.md.
    /// </summary>
    public static partial class Achievements
    {
        // ---- Biomes --------------------------------------------------------------------------------------------------

        /// <summary>The surface biome families the goals name (VoxelBiomes ids as in <see cref="biomeObjectives"/>).</summary>
        private enum BiomeFamily { None, Desert, Jungle, Tundra, OldGrowth, Deciduous, Volcanic, Ocean }

        private static BiomeFamily FamilyOf(int biome) => biome switch
        {
            48 => BiomeFamily.Desert,
            49 or 66 or 67 or 68 or 69 or 70 or 71 or 74 => BiomeFamily.Jungle,
            51 or 80 or 81 => BiomeFamily.Tundra,
            50 or 72 or 75 => BiomeFamily.OldGrowth,
            52 or 73 or 76 => BiomeFamily.Deciduous,
            47 => BiomeFamily.Volcanic,
            54 => BiomeFamily.Ocean,
            _ => BiomeFamily.None
        };

        /// <summary>
        /// The biome under the player (one lookup in the landscape's biome bitmap, as <see cref="Moved"/>). Goals about
        /// where a prop was placed or a creature was killed use the player's own position: the hooks pass no other
        /// (INFERRED equivalence; build reach and melee range are a few metres, so the biome is the same but at a border).
        /// </summary>
        private static BiomeFamily BiomeHere(WorldSession session)
            => FamilyOf(Terrain.TerrainMaterials.BiomeAt(session.Position, session.ZoneId));

        /// <summary>I Just Can't Decide 560: "Prop placed in &lt;biome&gt;" (objectives 967-972).</summary>
        private static readonly Dictionary<BiomeFamily, string> PropPlacedIn = new()
        {
            [BiomeFamily.Jungle] = "Prop placed in jungle",
            [BiomeFamily.Desert] = "Prop placed in desert",
            [BiomeFamily.OldGrowth] = "Prop placed in old growth forest",
            [BiomeFamily.Deciduous] = "Prop placed in deciduous forest",
            [BiomeFamily.Volcanic] = "Prop placed in volcanic area",
            [BiomeFamily.Tundra] = "Prop placed in tundra",
        };

        // ---- Names ---------------------------------------------------------------------------------------------------

        private static string Text(uint id) => id == 0 ? "" : TextManager.Instance.GetTextForId(id) ?? "";

        /// <summary>
        /// "X (Y) Z" → "Y X Z": the goal texts put an item name's qualifier first ("Rug (Large Rectangular)" → "Large
        /// Rectangular Rug", "Roast (Sliced)" → "Sliced Roast", "Bread (Sliced) with Cutting Board" → "Sliced Bread with
        /// Cutting Board", "Candle Group (Medium)" → "Medium Candle Group", "Mushroom (Tall Glowing) 1" → "Tall Glowing
        /// Mushroom 1"). Null when the name has no parentheses.
        /// </summary>
        private static string Reordered(string name)
        {
            Match m = Regex.Match(name ?? "", @"^(.*?)\s*\(([^)]*)\)\s*(.*)$");
            if (!m.Success)
                return null;
            return string.Join(" ", new[] { m.Groups[2].Value, m.Groups[1].Value, m.Groups[3].Value }.Where(s => s.Trim().Length > 0)).Trim();
        }

        /// <summary>
        /// Goal names of props whose item names differ in more than word order (checked against every "… placed"
        /// objective of the capture; re/out/agent_wiki/objectives.tsv "no prop item of that name"):
        /// <list type="bullet">
        /// <item>"Painted Wooden Table" (Makin' Bacon Pancakes! 939): the two painted tables, "Table (Small Painted)" 795
        ///   and "Table (Large Painted)" 570 — INFERRED (the set's chairs are "Painted Wooden Chair");</item>
        /// <item>"Square Platter" (2094): "NovaTech Square Platter" 2631, the dump's only square platter — INFERRED
        ///   (the same goal's "Plate of Oranges" is from that 2016 dining set);</item>
        /// <item>"Small / Medium Old Growth Shelf Fungus" (2099 / 2100): "Old Growth Fungus Shelf (Small / Medium)" 405 / 406.</item>
        /// </list>
        /// </summary>
        private static readonly Dictionary<string, string[]> GoalNameAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Table (Small Painted)"] = new[] { "Painted Wooden Table" },
            ["Table (Large Painted)"] = new[] { "Painted Wooden Table" },
            ["NovaTech Square Platter"] = new[] { "Square Platter" },
            ["Old Growth Fungus Shelf (Small)"] = new[] { "Small Old Growth Shelf Fungus" },
            ["Old Growth Fungus Shelf (Medium)"] = new[] { "Medium Old Growth Shelf Fungus" },
        };

        /// <summary>Item names of the items that place a prop.</summary>
        private static List<string> ItemNamesOf(uint propId)
            => Inventory.ItemDefinitionManager.ItemsForProp(propId).Select(i => ItemName(i.Id).Trim())
                .Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>
        /// Every name a "&lt;name&gt; placed" goal may use for a prop: its items' names, the reordered names
        /// (<see cref="Reordered"/>), the names without the parenthesised qualifier ("Fjorden Book (Open)" → "Fjorden
        /// Book", "Mug (Oxidized Brass)" → "Mug"), <see cref="GoalNameAliases"/>, and the plurals of all of those ("Mugs
        /// placed", "Painted Wooden Chairs placed", "Replicators placed"). Checked against the capture: no "… placed"
        /// objective is reached by a prop it doesn't name.
        /// </summary>
        private static HashSet<string> PlacedNames(uint propId)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in ItemNamesOf(propId))
            {
                names.Add(name);
                if (Reordered(name) is string turned)
                    names.Add(turned);
                string bare = Regex.Replace(name, @"\s*\([^)]*\)", "").Trim();
                if (bare.Length > 0)
                    names.Add(bare);
                if (GoalNameAliases.TryGetValue(name, out string[] aliases))
                    names.UnionWith(aliases);
            }
            foreach (string name in names.ToList())
                names.Add(name + "s");
            return names;
        }

        /// <summary>All names of a prop for the creature-faction checks: item names, the PropDefs name and the internal name.</summary>
        private static List<string> AllNamesOf(uint propId)
        {
            var names = ItemNamesOf(propId);
            names.Add(Text(GameTableManager.Instance.PropDefs.GetEntry(propId)?.NameStringId ?? 0));
            names.Add(Props.InternalName(propId) ?? "");
            return names.Where(n => n.Length > 0).ToList();
        }

        private static bool AnyNameContains(IEnumerable<string> names, string word)
            => names.Any(n => n.Contains(word, StringComparison.OrdinalIgnoreCase));

        // ---- Props placed --------------------------------------------------------------------------------------------

        /// <summary>Creature factions of the "&lt;Faction&gt; characters placed" goals (568-573, 5 each).</summary>
        private static readonly string[] CharacterFactions = { "Barren", "Scalewind", "Highfall", "NovaTech", "Ebonguard", "Stoneskin" };

        /// <summary>They Were Here First 567: "&lt;X&gt; monster placed" by the word the creature props carry.</summary>
        private static readonly (string Word, string Objective)[] FirstMonsters =
        {
            ("Slaug", "Slaug monster placed"), ("Abomination", "Abomination monster placed"),
            ("Chomper", "Chomper monster placed"), ("Fung", "Fiery Fungus monster placed"),
        };

        /// <summary>Absence Makes The Heart Grow Fungus 1429: "Old Growth Bump Cluster" (2111) has no " placed" in its text.</summary>
        private const uint OldGrowthBumpClusterProp = 404, PlaceBumpCluster = 2111;

        /// <summary>
        /// The other goals a first placement counts for:
        /// <list type="bullet">
        /// <item>"Props placed" (This One Goes THERE 558, 1000, and its tiers): every prop;</item>
        /// <item>"Prop placed in &lt;biome&gt;" (I Just Can't Decide 560) for the biome the player stands in;</item>
        /// <item>creature props (<see cref="Props.IsSpawner"/>): "&lt;Faction&gt; characters placed" (Bad to the Bone 568,
        ///   Grrr. Arg. 569, Placeth Ye Olde Highfall 570, We Come From the Future 571, They're Not All Evil! 572, Their
        ///   Skin Is Stone 573) and "&lt;Slaug / Abomination / Chomper / Fiery Fungus&gt; monster placed" (They Were Here
        ///   First 567) by the names the prop carries ("Slaug Slasher", "Spawner: Ruins: Abomination (100 Limit)", "Fiery
        ///   Fungus");</item>
        /// <item>Ice Cold Brewskies 547 "Brewskies on ice!": a keg (any prop named "Keg…": Keg (Small Wood), Miner's Keg,
        ///   Keg Tap …) placed in a tundra. INFERRED from the goal's own description "Oh hey, just put that out in the
        ///   snow! Ice cold brewskies on tap!" (the wiki hint only names a tundra and an unknown prop, OPEN-LEADS
        /// L45);</item>
        /// <item>Better Than Reality 548 "Reality escaped": a painting (any prop named "…Painting…") placed in a desert.
        ///   INFERRED from the description "Who needs the reality of sand and cacti when you have a PICTURE instead?"
        ///   (the wiki hint: a Desert Painting in a desert, with a question mark, L45) — any painting counts, not only
        /// "Desert
        ///   Painting";</item>
        /// <item>Gosh It's Hot 546 "Craziness in a volcanic area completed" (950): a fireplace (a prop whose item name is
        ///   "…Fireplace" or "Fireplace (…)": Regal Fireplace 1275; not the Fireplace Screen / Tools) placed in a volcanic
        ///   biome. INFERRED from the description "HOW HOT IS IT?!? Hot enough to do something crazy in a volcanic area"
        ///   and the wiki's hint, a fireplace in a volcanic biome with a question mark (L45; goals audit 2026-10-16).
        /// The text is not a
        ///   meta objective: no goal is named "Craziness in a volcanic area".</item>
        /// </list>
        /// </summary>
        private static void PlacedTriggers(WorldSession session, uint propId)
        {
            ProgressByText(session, "Props placed");
            BiomeFamily biome = BiomeHere(session);
            if (PropPlacedIn.TryGetValue(biome, out string inBiome))
                ProgressByText(session, inBiome);
            if (propId == OldGrowthBumpClusterProp)
                Progress(session, PlaceBumpCluster);
            List<string> names = AllNamesOf(propId);
            if (Props.IsSpawner(propId))
            {
                foreach (string faction in CharacterFactions)
                    if (AnyNameContains(names, faction))
                        ProgressByText(session, faction + " characters placed");
                foreach ((string word, string objective) in FirstMonsters)
                    if (AnyNameContains(names, word))
                        ProgressByText(session, objective);
            }
            if (biome == BiomeFamily.Tundra && names.Any(n => Regex.IsMatch(n, @"\bKeg\b", RegexOptions.IgnoreCase)))
                ProgressByText(session, "Brewskies on ice!");
            if (biome == BiomeFamily.Desert && AnyNameContains(names, "Painting"))
                ProgressByText(session, "Reality escaped");
            if (biome == BiomeFamily.Volcanic && names.Any(n => Regex.IsMatch(n, @"Fireplace(\s*\(|$)", RegexOptions.IgnoreCase)))
                ProgressByText(session, "Craziness in a volcanic area completed");
        }

        // ---- Tints ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// Red and black tints of the prop tint palette (PropTints dump aliases): red = GlobalTint_CherryRed, _Crimson,
        /// _FireBrick, _Maroon; black = GlobalTint_BlackLatex, _Soft Black. INFERRED grouping by the alias names (the
        /// palette has no colour families).
        /// </summary>
        private static bool IsRedTint(string alias)
            => Regex.IsMatch(alias, "red|crimson|firebrick|maroon", RegexOptions.IgnoreCase);

        private static bool IsBlackTint(string alias) => alias.Contains("black", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Colour goals:
        /// <list type="bullet">
        /// <item>Tint It Black 552: "Door painted red" (959) / "Door painted black" (2095) — any door prop (a name with
        ///   "door": Rustic Wood Door, Outhouse Door, Trapdoor …) tinted red / black;</item>
        /// <item>Hashtag Mutant! 551: "One is red!" (957) / "One is black!" (958) — a padded seat (Padded Sofa 845, Padded
        ///   Armchair 912) tinted red / black. INFERRED from the goal's description "These fine Corinthian leather padded
        ///   seats would look great if one was red, and one was black!" (the wiki hint: one unknown prop dyed red and
        /// one black, L45).</item>
        /// </list>
        /// </summary>
        private static void ColourTinted(WorldSession session, uint propId, string alias)
        {
            bool red = IsRedTint(alias), black = IsBlackTint(alias);
            if (!red && !black)
                return;
            List<string> names = ItemNamesOf(propId);
            if (AnyNameContains(names, "door"))
                ProgressByText(session, red ? "Door painted red" : "Door painted black");
            if (names.Any(n => n.StartsWith("Padded", StringComparison.OrdinalIgnoreCase)))
                ProgressByText(session, red ? "One is red!" : "One is black!");
        }

        // ---- Recipes and crafting ------------------------------------------------------------------------------------

        private static Dictionary<string, List<Achievement>> achievementsByName;

        /// <summary>
        /// Recipe items renamed after their Recipe Discoveries goal was made: the item's name now → the goal's (the old
        /// item name). 3480 "Recipe: Ornate Rectangular Rug" (name text 576611, a late text) → 1286 "Recipe: Rug" (objective
        /// 1949 "Recipe: Rug"): the item names of 3478-3482 run 573630-573634 in item order ("Recipe: Roast (Sliced)",
        /// "Recipe: Rug (Large Rectangular)", 573632 "Recipe: Rug", "Recipe: Rustic Bookcase", "Recipe: Scrying Pool
        /// Mote Emitter"), so 573632 was 3480's name before the rename [I]; no item is named "Recipe: Rug" now, and 3480 is
        /// the only rug recipe item without a discovery goal of its own. The goal sits in the discovery list's alphabetical order at
        /// "Rug", between "Row of Thin Books" and "Rug (Large Rectangular)".
        /// </summary>
        private static readonly Dictionary<string, string> RenamedRecipeGoals = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Recipe: Ornate Rectangular Rug"] = "Recipe: Rug",
        };

        /// <summary>
        /// The Recipe Discoveries goal named exactly like a recipe item counts all its objectives — for the records whose
        /// objective text is not the item's name (913 "Recipe: Apples" → 1575 "Recipe unlocked"). Objectives already
        /// counted by their text are complete by then and don't count twice. <paramref name="renamed"/>: the goal of a
        /// renamed item (<see cref="RenamedRecipeGoals"/>), whose objectives all count.
        /// </summary>
        private static void RecipeGoalByName(WorldSession session, string recipeName, bool renamed = false)
        {
            if (string.IsNullOrWhiteSpace(recipeName))
                return;
            if (achievementsByName == null)
            {
                var map = new Dictionary<string, List<Achievement>>(StringComparer.OrdinalIgnoreCase);
                foreach (Achievement a in achievements.Values)
                {
                    string name = Text(a.NameId).Trim();
                    if (!name.StartsWith("Recipe: ", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!map.TryGetValue(name, out var list))
                        map[name] = list = new List<Achievement>();
                    list.Add(a);
                }
                achievementsByName = map;
            }
            // A renamed recipe item: its goal's objective text is the old name, which no text match reaches.
            if (!renamed && RenamedRecipeGoals.TryGetValue(recipeName.Trim(), out string oldName))
                RecipeGoalByName(session, oldName, renamed: true);
            if (!achievementsByName.TryGetValue(recipeName.Trim(), out List<Achievement> goals))
                return;
            foreach (Achievement a in goals)
                foreach (Objective o in a.Objectives)
                    if (renamed || !Text(o.TextId).Trim().Equals(recipeName.Trim(), StringComparison.OrdinalIgnoreCase))
                        Progress(session, o.Id);
        }

        /// <summary>Believe it or Not... 1450, objective 2134 "Breeze Boots" (wiki "Breeze Boots crafted").</summary>
        private const uint BreezeBootsCrafted = 2134;

        /// <summary>
        /// Crafting goals whose objective text is only the item's name. Counted by id, not by text: the same text is also
        /// the objective of the item's Replicator tracker goal (720 "Breeze Boots"), which <see cref="TrackerGoalsCrafted"/>
        /// counts.
        /// </summary>
        private static void CraftedTriggers(WorldSession session, uint itemDefinitionId)
        {
            if (ItemName(itemDefinitionId).Trim().Equals("Breeze Boots", StringComparison.OrdinalIgnoreCase))
                Progress(session, BreezeBootsCrafted);
            TrackerGoalsCrafted(session, itemDefinitionId);
        }

        private static Dictionary<uint, uint[]> trackerGoalsByItem;

        /// <summary>
        /// Crafted item → the recipe tracker goals ("Recipe Achievements") of the recipes that make it: every
        /// Recipes.TRACKER_ACHIEVEMENT_ID that is a goal record. Those are the 186 goals of the Journal's Replicator
        /// subcategories (Armor, Weapon, Accessory, Outfit, Smelted Metal, Cut Gemstone, Textile, Plastic, Lumicite,
        /// Potion, Elemental Essence, Gadget, Extractor, Cut Stone, Boots, Pick, Axe, Cut Wood, Grappling Hook, Explosive):
        /// each is named like the item, has one objective with the item's name, required 1, no reward and 0 points, and
        /// is the tracker id of the recipe making that item (186 / 186 name matches, trace/0695/wiki-content-audit.md
        /// §3.2, OPEN-LEADS L43). The other 183 tracker ids of the table (e.g. 306 on recipe 38 Smelted Copper, 1467 Roll
        /// Wraps) have no record: the client has no such goal, so nothing is counted for them.
        /// </summary>
        private static Dictionary<uint, uint[]> TrackerGoalsByItem
        {
            get
            {
                if (trackerGoalsByItem != null)
                    return trackerGoalsByItem;
                trackerGoalsByItem = GameTableManager.Instance.Recipes.Entries
                    .Where(r => r.TrackerAchievementId != 0 && achievements.ContainsKey(r.TrackerAchievementId))
                    .GroupBy(r => r.ItemDefinitionId)
                    .ToDictionary(g => g.Key, g => g.Select(r => r.TrackerAchievementId).Distinct().ToArray());
                log.Info($"{trackerGoalsByItem.Values.Sum(v => v.Length)} recipe tracker goals for {trackerGoalsByItem.Count} items.");
                return trackerGoalsByItem;
            }
        }

        /// <summary>
        /// The recipe tracker goals of a crafted item (E.16, TASKS 5.10): a new Recipes tab of the Journal held a goal
        /// for every recipe that is not free; crafting the recipe once completes it, without Founder Score or other
        /// rewards, since its job is to track the components while harvesting (patchnotes.tsv P3250-P3251, 20 Apr
        /// 2015); the Replicator's "Watch" button tracks the recipe's goal
        /// (CraftingView.as: ObjectivesController.SetTrackingAchievement with the selected recipe's "AchievementId",
        /// P3253). The tracker id sits on one recipe of the item (e.g. 1558 Cut Agate → 728), while the same item has
        /// other recipes without one (1309 / 1585 / 1612 / 1895: other stations and batch sizes); crafting the item with
        /// any of its recipes counts [I] — the note speaks of crafting the recipe, and the goal (named and worded as
        /// the
        /// item) cannot tell the recipes apart. A completed goal leaves the Watch List like any other (Complete).
        /// </summary>
        private static void TrackerGoalsCrafted(WorldSession session, uint itemDefinitionId)
        {
            if (!TrackerGoalsByItem.TryGetValue(itemDefinitionId, out uint[] goals))
                return;
            foreach (uint goal in goals)
                foreach (Objective o in achievements[goal].Objectives)
                    Progress(session, o.Id);
        }

        // ---- Kills ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// Kill-in-the-right-biome goals (biome under the killer):
        /// <list type="bullet">
        /// <item>Venison? 619 "Venison done right": an Oakmire Stag killed in an old growth forest (wiki);</item>
        /// <item>Reek's Revenge 620 "Revenge taken": a Darkfang Specter killed in a tundra (wiki);</item>
        /// <item>You Don't Belong Here 621 "Fixed a bad match": a Frostbite Giant killed in a volcanic biome (wiki;
        ///   description "Like frost and volcanoes!").</item>
        /// </list>
        /// The creature is recognised by its kind's name (story spawners name their creature: "Oakmire Stag Patroller").
        /// </summary>
        private static void KilledTriggers(WorldSession session, string creatureName)
        {
            BiomeFamily biome = BiomeHere(session);
            if (biome == BiomeFamily.OldGrowth && creatureName.Contains("Oakmire Stag", StringComparison.OrdinalIgnoreCase))
                ProgressByText(session, "Venison done right");
            if (biome == BiomeFamily.Tundra && creatureName.Contains("Darkfang Specter", StringComparison.OrdinalIgnoreCase))
                ProgressByText(session, "Revenge taken");
            if (biome == BiomeFamily.Volcanic && creatureName.Contains("Frostbite Giant", StringComparison.OrdinalIgnoreCase))
                ProgressByText(session, "Fixed a bad match");
        }

        // ---- Likes ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// The riddle Like goals by the site tag they describe (Build Site Management tags 576620-576645). INFERRED from
        /// the goals' own descriptions (the wiki hints name a Western / SciFi / Fantasy build site, with a question
        /// mark, L45):
        /// 489 Howdy, Partner! (768) "…would get Wyatt Earp's trigger finger itchin'" → Western; 490 We Don't Need ...
        /// Roads (769) "…a time-traveling doc would find relaxing" → SciFi; 491 Roll for Initiative! (770) "…where you'd
        /// be most likely to slay a dragon" → Fantasy.
        /// </summary>
        private static readonly Dictionary<string, uint> RiddleLikes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Western"] = 768, ["SciFi"] = 769, ["Fantasy"] = 770,
        };

        // ---- Emotes --------------------------------------------------------------------------------------------------

        /// <summary>Emote AnimationSelection ids (Emotes.cs word table).</summary>
        private const uint EmoteBow = 229, EmoteCry = 231, EmoteFlirt = 233, EmoteGroan = 234, EmoteSob = 240,
            EmoteTalk = 241, EmoteDance = 246, EmoteDrunk = 250, MoodAngry = 219;

        /// <summary>
        /// The 12 secret emote goals 492-503 (So Emote-tional Right Now's children). A player emoted (Emotes.HandleCommand,
        /// "/word"); <paramref name="selection"/> is the emote's AnimationSelection. "Group" = in a group of two or more.
        /// Wiki hints (goal pages, "Other Information") and the goals' descriptions:
        /// <list type="bullet">
        /// <item>771 You Are the Lizard King: /drunk in a desert;</item>
        /// <item>848 Rumble in the Jungle: /angry in a jungle; 849 Get to the Choppa!: /angry in a jungle in a group;</item>
        /// <item>850 Kick Off Your Sunday Shoes: /dance (anywhere); 856 Ice Flow, Nowhere To Go: /dance in a tundra;</item>
        /// <item>851 Weeping Willow: /cry in an old growth forest;</item>
        /// <item>852 Water Cooler: /talk in a group; 857 Well-Mannered: /bow in a group; 854 PUG Troubles: /groan in a group;</item>
        /// <item>853 Casanova: /flirt in a group; 858 All By Myself: /flirt not in a group;</item>
        /// <item>855 To Me, Gunter!: the wiki hint: /sad in a tundra, perhaps in a group. The client has no /sad word
        /// (its emote
        ///   words 559123-559192 have sob and cry): INFERRED /sob or /cry in a tundra, grouped or not (the description
        ///   "…I must emote the sorrow because I am alone" contradicts the wiki's guess of a group).</item>
        /// </list>
        /// </summary>
        public static void Emoted(WorldSession session, uint selection)
        {
            BiomeFamily biome = BiomeHere(session);
            bool grouped = Groups.IsGrouped(session);
            switch (selection)
            {
                case EmoteDrunk:
                    if (biome == BiomeFamily.Desert)
                        Progress(session, 771);
                    break;
                case MoodAngry:
                    if (biome == BiomeFamily.Jungle)
                    {
                        Progress(session, 848);
                        if (grouped)
                            Progress(session, 849);
                    }
                    break;
                case EmoteDance:
                    Progress(session, 850);
                    if (biome == BiomeFamily.Tundra)
                        Progress(session, 856);
                    break;
                case EmoteCry:
                case EmoteSob:
                    if (selection == EmoteCry && biome == BiomeFamily.OldGrowth)
                        Progress(session, 851);
                    if (biome == BiomeFamily.Tundra)
                        Progress(session, 855);
                    break;
                case EmoteTalk:
                    if (grouped)
                        Progress(session, 852);
                    break;
                case EmoteFlirt:
                    Progress(session, grouped ? 853u : 858u);
                    break;
                case EmoteGroan:
                    if (grouped)
                        Progress(session, 854);
                    break;
                case EmoteBow:
                    if (grouped)
                        Progress(session, 857);
                    break;
            }
        }

        // ---- Build sites ---------------------------------------------------------------------------------------------

        /// <summary>
        /// And I Will Call It ...This Land! 511, "Adventure landscape Build Site claimed" (773): a build site placed on a
        /// landscape that is not a Pioneer land (wiki: a claim placed anywhere but the Pioneer lands; description
        /// "Pioneer landscapes are great, but a true Luminary must claim his Build Site out in the wild!"). Hook:
        /// Claims.PlaceConfirm.
        /// </summary>
        public static void ClaimPlaced(WorldSession session, uint zoneId)
        {
            if (Landscapes.Get(zoneId) is { Pioneer: false })
                Progress(session, 773);
        }

        // ---- Equipment -----------------------------------------------------------------------------------------------

        /// <summary>DurabilitySet.txt row of armour (ItemDefinition.DurabilitySet: 2 weapons, 3 armour).</summary>
        private const uint ArmourDurabilitySet = 3;

        /// <summary>
        /// No Pants! 1427, "Clothing malfunction! Better visit a repair station immediately!" (2093): an equipped armour
        /// piece reached 0 % condition (wiki: armour at 0 % condition). Hook: Durability.Damaged, when an
        /// item's shown condition drops to 0.
        /// </summary>
        public static void GearBroken(WorldSession session, uint itemDefinitionId)
        {
            if (Inventory.ItemDefinitionManager.Get(itemDefinitionId)?.DurabilitySet == ArmourDurabilitySet)
                Progress(session, 2093);
        }

        /// <summary>
        /// Physician Boom 637, "Boom knowledge obtained" (1127, 2 times): High Yield Dynamite used (wiki: "Use High Yield
        /// Dynamite"). Hook: Consumables.cs (the dynamite throw).
        /// </summary>
        public static void DynamiteUsed(WorldSession session) => ProgressByText(session, "Boom knowledge obtained");

        // ---- Story-tool Links ----------------------------------------------------------------------------------------

        /// <summary>The Floor Switch: prop 1599 (named "Metal Switch" in PropDefs; "Recipe: Floor Switch" 3435 grants it).</summary>
        private const uint FloorSwitchProp = 1599;
        /// <summary>The "REALLY nice bathroom link": Outhouse Door 1969 and Chandelier (Intricate) 1756.</summary>
        private const uint OuthouseDoorProp = 1969, IntricateChandelierProp = 1756;

        /// <summary>
        /// A Link was made between two placed props (source → target prop ids). Counts:
        /// 960 "Any two props linked" (Welcome to the Machine 553); 961 "Timer Object linked to anything" (554, the Timer
        /// Filter <see cref="StoryTools.TimerProp"/>); 962 "Floor Switch linked to anything" (555); 963 "REALLY nice
        /// bathroom link created" (556; wiki and description: link a Chandelier (Intricate) to an Outhouse Door — either
        /// direction counts, INFERRED); 964 "Wood of iron burning" (Iron From Ice 557, "Link anything to this most special of
        /// magic fire colors"): a Link to or from a blue Magic Fire Mote Emitter (<see cref="BlueMagicFires"/>).
        /// Hook: StoryTools.cs, where a new binding is stored.
        /// </summary>
        public static void Linked(WorldSession session, uint sourceProp, uint targetProp)
        {
            Progress(session, 960);
            if (sourceProp == StoryTools.TimerProp || targetProp == StoryTools.TimerProp)
                Progress(session, 961);
            if (sourceProp == FloorSwitchProp || targetProp == FloorSwitchProp)
                Progress(session, 962);
            if ((sourceProp == OuthouseDoorProp && targetProp == IntricateChandelierProp)
                || (sourceProp == IntricateChandelierProp && targetProp == OuthouseDoorProp))
                Progress(session, 963);
            if (BlueMagicFires.Value.Contains(sourceProp) || BlueMagicFires.Value.Contains(targetProp))
                Progress(session, 964);
        }

        /// <summary>
        /// Iron From Ice's "most special of magic fire colors" (L45, TASKS 6.58; INFERRED, strong): blue. "Iron From Ice"
        /// is the first episode of Telltale's Game of Thrones (House Forrester of Ironrath), and in that setting ironwood
        /// — "wood of iron", the objective's text — burns with a blue flame (A Wiki of Ice and Fire, "Ironwood"). Every
        /// prop placed by an item named "Magic Fire (Blue) …" or "Magic Fire (Small Blue) …" (props 1883 and the small
        /// one; PropDefs 1883 = text 567308).
        /// </summary>
        private static readonly Lazy<HashSet<uint>> BlueMagicFires = new(() =>
        {
            var set = new HashSet<uint> { 1883 };
            foreach (var d in Inventory.ItemDefinitionManager.All)
            {
                string name = Shared.GameTable.Text.TextManager.Instance.GetTextForId(d.NameId) ?? "";
                if (d.PropId != 0 && (name.StartsWith("Magic Fire (Blue)", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("Magic Fire (Small Blue)", StringComparison.OrdinalIgnoreCase)))
                    set.Add(d.PropId);
            }
            return set;
        });
    }
}
