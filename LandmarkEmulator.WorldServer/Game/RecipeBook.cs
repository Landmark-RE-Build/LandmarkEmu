using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.GameTable;
using LandmarkEmulator.Shared.GameTable.Text;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Which crafting recipes a character knows: every DEFAULT_LEARNED recipe plus those learned from "Recipe: X"
    /// items. The client ships no item → recipe table; a recipe item is named "Recipe: " + the name of the item the
    /// recipe makes (e.g. 3416 "Recipe: Wood Stool" → recipe 686, which makes 992 "Wood Stool"), so the link is made
    /// by name. The client only lists recipes whose full record came in SendSelf, so SendSelf carries records for the
    /// known recipes and for those of recipe items in the bag (learnable this session).
    /// </summary>
    public static class RecipeBook
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private static Dictionary<uint, uint[]> recipesByItem;

        /// <summary>"Recipe:" items whose output item is named differently (only unambiguous ones).</summary>
        private static readonly Dictionary<string, string> OutputAliases = new()
        {
            ["Brazier (Standing)"] = "Standing Brazier",
            ["Hanging Lantern"] = "Ornate Hanging Lantern",
            ["Painting: The Withered Land"] = "Painting: Withered Lands",
        };

        /// <summary>Recipe item definition id → the (non-default) recipes it teaches.</summary>
        private static Dictionary<uint, uint[]> RecipesByItem
        {
            get
            {
                if (recipesByItem != null)
                    return recipesByItem;
                string Name(uint nameId) => TextManager.Instance.GetTextForId(nameId) ?? "";
                var itemsByName = new Dictionary<string, List<uint>>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in Inventory.ItemDefinitionManager.All)
                {
                    string name = Name(item.NameId).Trim();
                    if (name.Length == 0)
                        continue;
                    if (!itemsByName.TryGetValue(name, out var ids))
                        itemsByName[name] = ids = new List<uint>();
                    ids.Add(item.Id);
                }
                var byOutput = GameTableManager.Instance.Recipes.Entries.Where(r => !r.DefaultLearned)
                    .GroupBy(r => r.ItemDefinitionId).ToDictionary(g => g.Key, g => g.Select(r => r.Id).ToArray());
                var map = new Dictionary<uint, uint[]>();
                foreach (var item in Inventory.ItemDefinitionManager.All)
                {
                    string name = Name(item.NameId).Trim();
                    if (!name.StartsWith("Recipe: ", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string output = name.Substring(8).Trim();
                    if (!itemsByName.TryGetValue(OutputAliases.GetValueOrDefault(output, output), out var outputs))
                        continue;
                    uint[] recipes = outputs.Where(byOutput.ContainsKey).SelectMany(o => byOutput[o]).Distinct().ToArray();
                    if (recipes.Length > 0)
                        map[item.Id] = recipes;
                    else if (outputs.Select(o => Inventory.ItemDefinitionManager.Get(o)?.PropId ?? 0).FirstOrDefault(p => p != 0) is uint prop and not 0)
                        propsByItem[item.Id] = prop;   // the prop has no crafting recipe in this client (creature, furniture,
                                                       // emitter recipes…): the item teaches placing it
                }
                log.Info($"Recipe items: {map.Count} teach {map.Values.Sum(r => r.Length)} recipe(s), {propsByItem.Count} teach a prop.");
                return recipesByItem = map;
            }
        }

        /// <summary>Recipe type of placeable props (RECIPE_TYPE 2: 576 recipes, 56 known from the start).</summary>
        private const uint PropRecipeType = 2;

        /// <summary>
        /// May the player place this prop from the palette? Props whose item has prop recipes need one of them known
        /// (default-learned or learned from a "Recipe:" item); props without any prop recipe, or whose recipes no item
        /// teaches, are free to place.
        /// </summary>
        public static bool KnowsProp(WorldSession session, uint propId)
        {
            var items = Inventory.ItemDefinitionManager.ItemsForProp(propId).Select(i => i.Id).ToHashSet();
            var recipes = GameTableManager.Instance.Recipes.Entries
                .Where(r => r.RecipeType == PropRecipeType && items.Contains(r.ItemDefinitionId)).ToList();
            // A recipe no "Recipe:" item teaches could never be learned (e.g. 316 Standing Torch): treat it as known.
            if (TaughtProps.Contains(propId))
                return Unlocks.Has(session, Unlocks.PropTaught, propId);
            var teachable = TaughtRecipes;
            return recipes.Count == 0
                || recipes.Any(r => r.DefaultLearned || !teachable.Contains(r.Id) || session.LearnedRecipes.Contains(r.Id));
        }

        private static HashSet<uint> taughtRecipes;

        /// <summary>
        /// "Recipe:" items that teach placing a prop which has no crafting recipe (creature recipes such as "Recipe: Earth
        /// Elemental" → prop 2481, and furniture, food, lights, doors, mote emitters), by item; learned ones are <see cref="Unlocks.PropTaught"/> rows.
        /// </summary>
        private static readonly Dictionary<uint, uint> propsByItem = new();

        private static HashSet<uint> taughtProps;

        /// <summary>Props only a recipe item can unlock.</summary>
        private static HashSet<uint> TaughtProps
        {
            get
            {
                _ = RecipesByItem;
                return taughtProps ??= propsByItem.Values.ToHashSet();
            }
        }

        /// <summary>Every recipe some recipe item teaches.</summary>
        private static HashSet<uint> TaughtRecipes => taughtRecipes ??= RecipesByItem.Values.SelectMany(r => r).ToHashSet();

        public static bool IsRecipeItem(uint itemDefinitionId) => RecipesByItem.ContainsKey(itemDefinitionId);

        /// <summary>
        /// A "Recipe:" item that would teach the player something new (a recipe or a prop), at random; 0 when they know
        /// them all. Used for recipe drops ("you might discover a new type of lamp recipe while mining", wiki Recipes).
        /// </summary>
        public static uint RandomUnknownRecipeItem(WorldSession session, Random random)
        {
            var candidates = RecipesByItem.Where(r => r.Value.Any(id => !session.LearnedRecipes.Contains(id))).Select(r => r.Key)
                .Concat(propsByItem.Where(p => !Unlocks.Has(session, Unlocks.PropTaught, p.Value)).Select(p => p.Key))
                .Distinct().ToList();
            return candidates.Count == 0 ? 0 : candidates[random.Next(candidates.Count)];
        }

        /// <summary>Does using the item teach something (recipes or a creature prop)?</summary>
        public static bool Teaches(uint itemDefinitionId) => RecipesByItem.ContainsKey(itemDefinitionId) || propsByItem.ContainsKey(itemDefinitionId);

        /// <summary>
        /// Recipes a character has learned (from recipe items), from the database.
        /// </summary>
        public static HashSet<uint> LoadLearned(ulong characterId)
            => DatabaseManager.Instance.CharacterDatabase.GetLearnedRecipes(characterId).ToHashSet();

        /// <summary>Recipes whose records SendSelf should carry: known ones and those of recipe items held.</summary>
        public static IEnumerable<uint> RecordsFor(WorldSession session, IEnumerable<uint> known)
        {
            var ids = new HashSet<uint>(known);
            foreach (CharacterItemModel item in session.Inventory.Items)
                if (RecipesByItem.TryGetValue(item.ItemDefinitionId, out uint[] recipes))
                    ids.UnionWith(recipes);
            return ids;
        }

        /// <summary>A creature recipe item: unlocks placing its prop (once) and is used up.</summary>
        /// <summary>
        /// Teaches the prop a "Recipe:" item (named <paramref name="recipeName"/>) would teach, without that item (e.g. a
        /// starter set). False when there is no such recipe or it is already known.
        /// </summary>
        public static bool TeachPropOf(WorldSession session, string recipeName)
        {
            _ = RecipesByItem;
            uint item = ItemUses.ItemNamed(recipeName);
            if (item == 0 || !propsByItem.TryGetValue(item, out uint prop) || Unlocks.Has(session, Unlocks.PropTaught, prop))
                return false;
            Unlocks.Add(session, Unlocks.PropTaught, prop);
            SendKnownProp(session, prop);
            log.Info($"{session.CharacterName} learned prop {prop} ({recipeName}).");
            return true;
        }

        /// <summary>
        /// KnownProp 0x115 sub 1 AddPropResponse (reader FUN_142e64760, handler FUN_142e65310): u32 prop id (added to the
        /// client's known props, +0x7adf8), u32 name text: above 0 the client shows its own notice "You have learned a
        /// new prop" (Learned.Prop) with that name — the prop's own name here.
        /// </summary>
        private static void SendKnownProp(WorldSession session, uint prop)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(prop);
            writer.Write(GameTableManager.Instance.PropDefs.GetEntry(prop)?.NameStringId ?? 0u);
            session.EnqueueMessage(ZoneMessageOpcode.KnownPropAddPropResponse, output.ToArray());
        }

        private static bool LearnProp(WorldSession session, uint itemDefinitionId)
        {
            if (!propsByItem.TryGetValue(itemDefinitionId, out uint prop))
                return false;
            if (Unlocks.Has(session, Unlocks.PropTaught, prop))
            {
                Chat.System(session, "You already know that recipe.");
                return true;
            }
            if (!session.TakeItem(itemDefinitionId, 1))
                return true;
            Unlocks.Add(session, Unlocks.PropTaught, prop);
            SendKnownProp(session, prop);
            log.Info($"{session.CharacterName} learned prop {prop} from item {itemDefinitionId}.");
            return true;
        }

        /// <summary>
        /// Using a recipe item: learn what it teaches (once), use the item up and tell the client
        /// (RecipeBase 8 AddRecipe: u32 recipe id, u32 name text id → "Learned recipe" message).
        /// </summary>
        public static bool Learn(WorldSession session, uint itemDefinitionId)
        {
            if (!RecipesByItem.TryGetValue(itemDefinitionId, out uint[] recipes))
                return LearnProp(session, itemDefinitionId);
            uint[] fresh = recipes.Where(r => !session.LearnedRecipes.Contains(r)).ToArray();
            if (fresh.Length == 0)
            {
                Chat.System(session, "You already know that recipe.");
                return true;
            }
            if (!session.TakeItem(itemDefinitionId, 1))
                return true;
            ulong characterId = session.PlayerGuid;
            foreach (uint recipe in fresh)
            {
                session.LearnedRecipes.Add(recipe);
                Achievements.RecipeLearned(session, itemDefinitionId,
                    GameTableManager.Instance.Recipes.GetEntry(recipe)?.ItemDefinitionId ?? 0);
            }
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                foreach (uint recipe in fresh)
                    c.CharacterRecipe.Add(new CharacterRecipeModel { CharacterId = characterId, RecipeId = recipe });
            });
            uint nameId = Inventory.ItemDefinitionManager.Get(
                GameTableManager.Instance.Recipes.GetEntry(fresh[0])?.ItemDefinitionId ?? 0)?.NameId ?? 0;
            foreach (uint recipe in fresh)
            {
                var output = new List<byte>();
                var writer = new GamePacketWriter(output);
                writer.Write(8u);
                writer.Write(recipe);
                writer.Write(nameId);
                session.EnqueueMessage(ZoneMessageOpcode.RecipeBase, output.ToArray());
            }
            log.Info($"{session.CharacterName} learned recipe(s) {string.Join(", ", fresh)} from item {itemDefinitionId}.");
            return true;
        }
    }
}
