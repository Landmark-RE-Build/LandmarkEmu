using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.GameTable;
using LandmarkEmulator.Shared.GameTable.Model;
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
    /// Which crafting recipes and props a character knows: every DEFAULT_LEARNED recipe plus what "Recipe: X" items
    /// taught. The client ships no item → recipe table; the link is the item record's own grant (TASKS 5.8 / 4.18,
    /// trace/0695/items-695.md §1.1): <see cref="Inventory.ItemDefinitionManager.ItemDefinition.GrantId"/>, whose kind
    /// is told by <see cref="Inventory.ItemDefinitionManager.ItemDefinition.TypeId"/>:
    /// <list type="bullet">
    /// <item>type 6 (58 items, gear / tool / material recipes): the grant is a Recipes row, e.g. 3981 "Recipe: Flame
    ///   Bound Bastion" → recipe 2391. Using the item learns that recipe (RecipeBase 8 AddRecipe).</item>
    /// <item>type 45 (526 items, props: furniture, lights, creatures, mote and fireworks emitters): the grant is a
    ///   PropDefs row, e.g. 3418 "Recipe: Barrel (Large Iron)" → prop 1608, 4477 "Recipe: Fireworks Emitter (Gold)" →
    ///   prop 2949. Using the item adds the prop to the character's known props (<see cref="Unlocks.PropTaught"/>, sent
    ///   in SendSelf and as KnownProp 0x115 sub 1).</item>
    /// </list>
    /// Until 2026-10-16 the link was made by name ("Recipe: " + the output item's name) on 0.589 text: it missed the 22
    /// fireworks emitter recipes and 7 others (Hanging Lamp, Table, Wall Sconce, NovaTech Chest, Buccaneer's Treasure
    /// Chest, Lamp Post (Tall Combine), Floor Switch…), taught 5 items a station-less type-3 recipe instead of their prop
    /// (Barrel (Large Iron), Brass Trimmed Basin, Mug (Oxidized Brass), Chandelier (Intricate), Oxidized Brass Carved
    /// Horn), and left 13 props free for everyone (trace/0695/wiki-content-audit.md §2.8). Characters who learned a
    /// prop's crafting recipe under the old link keep the prop (<see cref="KnowsProp"/>).
    /// The client only lists recipes whose full record came in SendSelf, so SendSelf carries records for the known
    /// recipes and for those of recipe items in the bag (learnable this session).
    /// Server-built definitions count like the live ones: the twelve "Recipe: Barren …" items of
    /// <see cref="BarrenRecipes"/> (type 45, grants 2445-2459) make those creature props taught, not innate (L44).
    /// </summary>
    public static class RecipeBook
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private static Dictionary<uint, uint[]> recipesByItem;

        /// <summary>Item type (record u15) of items whose grant is a Recipes row (see the class notes).</summary>
        public const uint RecipeGrantType = 6;
        /// <summary>Item type (record u15) of items whose grant is a PropDefs row (see the class notes).</summary>
        public const uint PropGrantType = 45;

        /// <summary>Recipe item definition id → the (non-default) recipes it teaches.</summary>
        private static Dictionary<uint, uint[]> RecipesByItem
        {
            get
            {
                if (recipesByItem != null)
                    return recipesByItem;
                var map = new Dictionary<uint, uint[]>();
                int defaultOnly = 0, missing = 0;
                foreach (var item in Inventory.ItemDefinitionManager.All)
                {
                    if (item.GrantId == 0)
                        continue;
                    if (item.TypeId == RecipeGrantType)
                    {
                        var recipe = GameTableManager.Instance.Recipes.GetEntry(item.GrantId);
                        if (recipe == null)
                            missing++;
                        else if (recipe.DefaultLearned)
                            defaultOnly++;                       // everybody knows it already: the item teaches nothing
                        else
                            map[item.Id] = new[] { recipe.Id };
                    }
                    else if (item.TypeId == PropGrantType)
                    {
                        if (GameTableManager.Instance.PropDefs.GetEntry(item.GrantId) == null)
                            missing++;
                        else
                            propsByItem[item.Id] = item.GrantId;
                    }
                }
                // A prop's crafting recipes (RECIPE_TYPE 2, not default-learned) whose output item places it: taught with
                // the prop, so the prop item stays craftable at its station (the old name link taught exactly these;
                // INFERRED that live tied them to the known prop — no client table links a prop recipe to a prop grant).
                var byProp = new Dictionary<uint, uint[]>();
                foreach (uint prop in propsByItem.Values.Distinct())
                {
                    var items = Inventory.ItemDefinitionManager.ItemsForProp(prop).Select(i => i.Id).ToHashSet();
                    uint[] recipes = GameTableManager.Instance.Recipes.Entries
                        .Where(r => r.RecipeType == PropRecipeType && !r.DefaultLearned && items.Contains(r.ItemDefinitionId))
                        .Select(r => r.Id).ToArray();
                    if (recipes.Length > 0)
                        byProp[prop] = recipes;
                }
                companionRecipes = byProp;
                log.Info($"Recipe items by grant: {map.Count} teach a recipe, {propsByItem.Count} teach a prop "
                    + $"({byProp.Count} of those props also have {byProp.Values.Sum(r => r.Length)} crafting recipe(s)); "
                    + $"{defaultOnly} grant a default recipe, {missing} grant an id this server's tables lack.");
                return recipesByItem = map;
            }
        }

        /// <summary>Granted prop → its non-default prop crafting recipes (see <see cref="RecipesByItem"/>).</summary>
        private static Dictionary<uint, uint[]> companionRecipes = new();

        /// <summary>The prop crafting recipes taught together with a prop (empty when none).</summary>
        private static uint[] CompanionRecipes(uint prop)
        {
            _ = RecipesByItem;
            return companionRecipes.TryGetValue(prop, out uint[] r) ? r : Array.Empty<uint>();
        }

        /// <summary>Recipe type of placeable props (RECIPE_TYPE 2: 576 recipes, 56 known from the start).</summary>
        private const uint PropRecipeType = 2;

        /// <summary>
        /// May the player place this prop from the palette? A prop some "Recipe:" item grants needs to be known: taught
        /// (<see cref="Unlocks.PropTaught"/>), or one of its prop crafting recipes default-learned or learned (characters
        /// who used the item under the old name link learned the recipe, not the prop). Other props: those whose item
        /// has prop recipes need one of them known; props without any prop recipe, or whose recipes no item teaches, are
        /// free to place.
        /// </summary>
        public static bool KnowsProp(WorldSession session, uint propId)
        {
            // Founder / Year Zero / promotional props: only with their entitlement switched on (Entitlements, TASKS 6.59).
            // Their bag items still place them (Props: an item in the bag needs no knowledge).
            if (!Entitlements.PropAllowed(propId))
                return false;
            var items = Inventory.ItemDefinitionManager.ItemsForProp(propId).Select(i => i.Id).ToHashSet();
            var recipes = GameTableManager.Instance.Recipes.Entries
                .Where(r => r.RecipeType == PropRecipeType && items.Contains(r.ItemDefinitionId)).ToList();
            if (TaughtProps.Contains(propId))
                return Unlocks.Has(session, Unlocks.PropTaught, propId)
                    || recipes.Any(r => r.DefaultLearned || session.LearnedRecipes.Contains(r.Id));
            // A recipe no "Recipe:" item teaches could never be learned (e.g. 316 Standing Torch): treat it as known.
            var teachable = TaughtRecipes;
            return recipes.Count == 0
                || recipes.Any(r => r.DefaultLearned || !teachable.Contains(r.Id) || session.LearnedRecipes.Contains(r.Id));
        }

        private static HashSet<uint> taughtRecipes;

        /// <summary>
        /// "Recipe:" items that teach placing a prop (grant type 45: creature recipes such as "Recipe: Earth Elemental" →
        /// prop 2481, furniture, food, lights, doors, mote and fireworks emitters), by item; learned ones are
        /// <see cref="Unlocks.PropTaught"/> rows.
        /// </summary>
        private static readonly Dictionary<uint, uint> propsByItem = new();

        private static HashSet<uint> taughtProps;

        /// <summary>The "Recipe:" items that teach placing a prop.</summary>
        public static IEnumerable<uint> TeachingItemsFor(uint propId)
        {
            _ = RecipesByItem;
            return propsByItem.Where(p => p.Value == propId).Select(p => p.Key).ToList();
        }

        /// <summary>Props only a recipe item can unlock.</summary>
        private static HashSet<uint> TaughtProps
        {
            get
            {
                _ = RecipesByItem;
                return taughtProps ??= propsByItem.Values.ToHashSet();
            }
        }

        /// <summary>Every recipe some recipe item teaches (recipe grants and the prop grants' crafting recipes).</summary>
        private static HashSet<uint> TaughtRecipes => taughtRecipes ??= RecipesByItem.Values.SelectMany(r => r)
            .Concat(companionRecipes.Values.SelectMany(r => r)).ToHashSet();

        public static bool IsRecipeItem(uint itemDefinitionId) => RecipesByItem.ContainsKey(itemDefinitionId);

        /// <summary>
        /// A "Recipe:" item that would teach the player something new (a recipe or a prop), at random; 0 when they know
        /// them all. Used for recipe drops (for example a new lamp recipe found while mining, wiki Recipes).
        /// </summary>
        public static uint RandomUnknownRecipeItem(WorldSession session, Random random)
        {
            var candidates = RecipesByItem.Where(r => r.Value.Any(id => !session.LearnedRecipes.Contains(id))).Select(r => r.Key)
                .Concat(propsByItem.Where(p => !Unlocks.Has(session, Unlocks.PropTaught, p.Value)).Select(p => p.Key))
                // "Recipe: Fireworks Emitter (…)" 4477-4498 are bought for Lumens at the Lumen Station (recipes 5339-5360),
                // not found: kept out of random drops (trace/0695/spec-fireworks.md §5.2 F). The emitter items themselves
                // drop instead (Gathering.RollDrops → Fireworks.RandomEmitterItem).
                .Where(id => !Fireworks.IsEmitterRecipeItem(id))
                .Where(DropsAsRecipe)
                // One armour or one weapon recipe a day (1 Apr 2016, P4438; Pacing): an armour or
                // weapon recipe already found today keeps the rest of that category out until tomorrow.
                .Where(id => Pacing.RecipeAllowedToday(session, CategoryOf(id)))
                .Distinct().ToList();
            if (candidates.Count == 0)
                return 0;
            uint chosen = candidates[random.Next(candidates.Count)];
            // Counted when it drops (goes into the loot bag / bag), not when it is used: P4438 limits what you "get".
            Pacing.NoteRecipeDrop(session, CategoryOf(chosen));
            return chosen;
        }

        private static readonly Dictionary<uint, Pacing.RecipeCategory> categories = new();

        /// <summary>
        /// The daily-limit category of a recipe item (P4438, <see cref="Pacing"/>): what the recipe it teaches makes —
        /// a chest piece (EquipSlotId 8, the 21 "Recipe: … Cuirass / Raiment / Coat …" items 3988-4008, 4023-4029) is
        /// armour, a main-hand item (slot 2) without a harvesting ability is a weapon (3981-3987, 3995-4001, 4118-4122;
        /// the four NovaTech tool recipes 4347-4350 are slot 2 too but harvest, so not weapons). Trinkets (slot 3, 4009-4022),
        /// prop and creature recipes are <see cref="Pacing.RecipeCategory.Other"/> [P for the slots, items_summary.tsv /
        /// Abilities.IsHarvestAbility; I that live's "armor" / "weapon" categories were these].
        /// </summary>
        public static Pacing.RecipeCategory CategoryOf(uint recipeItem)
        {
            lock (categories)
                if (categories.TryGetValue(recipeItem, out Pacing.RecipeCategory known))
                    return known;
            var category = Pacing.RecipeCategory.Other;
            if (RecipesByItem.TryGetValue(recipeItem, out uint[] recipes))
                foreach (uint recipe in recipes)
                {
                    var output = Inventory.ItemDefinitionManager.Get(GameTableManager.Instance.Recipes.GetEntry(recipe)?.ItemDefinitionId ?? 0);
                    if (output?.EquipSlotId == Combat.GearStats.ChestSlot)
                        category = Pacing.RecipeCategory.Armour;
                    else if (output?.EquipSlotId == WeaponSlot && !output.Abilities.Any(a => Abilities.IsHarvestAbility(a.AbilityId)))
                        category = Pacing.RecipeCategory.Weapon;
                }
            lock (categories)
                categories[recipeItem] = category;
            return category;
        }

        /// <summary>EquipSlotId of main-hand items: weapons and tools (Abilities.MainHandSlot).</summary>
        private const uint WeaponSlot = 2;

        /// <summary>
        /// "Recipe: Elite Wisp Monarch / Red Wisp / Elite Volatile Wisp / Black Wisp / Elite Burster Wisp": the wisps were
        /// removed on 26 Apr 2016 (the wisps were taken out because they still would not fly, patchnotes.tsv P4704),
        /// and
        /// these five are the only creature recipe items the 0.695 Lumen Station does not sell (CraftingStationRecipeMappings
        /// station 55 covers the other 520 prop recipe items). Kept in the item list (owned copies still teach), never dropped.
        /// Also the four weapon recipes 3996, 3999, 4000, 4001: never craftable on live — all 13 sibling weapons have recipe
        /// goals in the Feb 2017 live achievement capture, these four (goals 786, 750, 788, 842) don't
        /// (trace/0695/agent-leads2-c.md, OPEN-LEADS L91 / L108) [I strong].
        /// </summary>
        private static readonly HashSet<uint> RetiredRecipeItems = new() { 3931, 3956, 3957, 3958, 3977, 3996, 3999, 4000, 4001 };

        /// <summary>
        /// May a recipe item turn up as a random find (gathering, Lost Chests / Crystals, Reward Chests)? Not when a goal
        /// rewards it: the wiki gives those recipes as goal rewards only ("This recipe is a reward for the Combat Goal
        /// Seismically Snuffed Out", Silent Shadow's Cloak "Recipe is an achievement reward"), the 11 May 2016 notes moved the
        /// replaced recipes back to loot and the Lumen Station (P5327-P5332) and the Lumen Station sells none of
        /// the goal ones (3906, 3990, 4003, 4006-4008, 4023, 4026). Not the retired wisp recipes either.
        /// trace/0695/item-sources-audit-695.md §3.
        /// </summary>
        private static bool DropsAsRecipe(uint itemId) => !Achievements.IsRewardItem(itemId) && !RetiredRecipeItems.Contains(itemId);

        /// <summary>
        /// Would using this item teach the player anything now? Null when it is no teaching item; otherwise false exactly
        /// when <see cref="Learn"/> would refuse it (every recipe known; a prop taught with every crafting recipe of it
        /// known). Used by Crafting to refuse buying an unlock already owned at the Lumen Station (L43).
        /// </summary>
        public static bool? TeachesNew(WorldSession session, uint itemDefinitionId)
        {
            if (RecipesByItem.TryGetValue(itemDefinitionId, out uint[] recipes))
                return recipes.Any(r => !session.LearnedRecipes.Contains(r));
            if (propsByItem.TryGetValue(itemDefinitionId, out uint prop))
                return !Unlocks.Has(session, Unlocks.PropTaught, prop) || FreshCompanions(session, prop).Length > 0;
            return null;
        }

        /// <summary>Is this a prop recipe item (grant type 45, teaches placing a prop)?</summary>
        public static bool IsPropRecipeItem(uint itemDefinitionId)
        {
            _ = RecipesByItem;
            return propsByItem.ContainsKey(itemDefinitionId);
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
            TeachProp(session, item, prop, FreshCompanions(session, prop));
            log.Info($"{session.CharacterName} learned prop {prop} ({recipeName}).");
            return true;
        }

        /// <summary>
        /// Teaches recipes without a recipe item (an entitlement, <see cref="Entitlements"/>): those the character doesn't
        /// know yet are saved and each sent as AddRecipe (the client's "Learned recipe" notice). Returns how many were new.
        /// </summary>
        public static int TeachRecipes(WorldSession session, IEnumerable<uint> recipes)
        {
            uint[] fresh = recipes.Where(r => GameTableManager.Instance.Recipes.GetEntry(r) is { DefaultLearned: false }
                && !session.LearnedRecipes.Contains(r)).Distinct().ToArray();
            if (fresh.Length > 0)
                AddRecipes(session, fresh);
            return fresh.Length;
        }

        /// <summary>RecipeBase sub 0x1F RecipeHideRecipeResponse / 0x20 unhide (handler FUN_142f39870) [P].</summary>
        private const uint HideRecipeSub = 0x1F, UnhideRecipeSub = 0x20;

        private static RecipeEntry[] unlockRecipes;

        /// <summary>The Lumen Station's one-time unlocks: the recipes with a VISIBILITY_REQUIREMENT_ID (581 in 0.695).</summary>
        private static RecipeEntry[] UnlockRecipes => unlockRecipes ??= GameTableManager.Instance.Recipes.Entries
            .Where(r => r.VisibilityRequirementId != 0).ToArray();

        /// <summary>
        /// The Lumen Station unlocks this character already owns, which the client hides (SendSelf hidden list, RecipeBase
        /// 0x1F / 0x20; trace/0695/agent-l43.md). The client never evaluates VISIBILITY_REQUIREMENT_ID itself — it loads
        /// the value and nothing reads it — so live's server decided and sent the list [P layout]. The rule is ours [I:
        /// the requirement read as "doesn't have this yet"]: a recipe item whose recipes / prop are all known
        /// (<see cref="TeachesNew"/> false), or a storage expansion already at its most. An unused copy in the bag does not
        /// hide it (the purchase is refused instead: Crafting.UnlockRefusal), nor does the Starter Set (opening it isn't
        /// recorded). At most 581 ids (~2.3 KB) in SendSelf.
        /// </summary>
        public static HashSet<uint> HiddenUnlocks(WorldSession session)
        {
            var hidden = new HashSet<uint>();
            foreach (RecipeEntry recipe in UnlockRecipes)
            {
                uint item = recipe.ItemDefinitionId;
                bool owned = ItemUses.ExpansionFull(session, item) ?? TeachesNew(session, item) == false;
                if (owned)
                    hidden.Add(recipe.Id);
            }
            return hidden;
        }

        /// <summary>
        /// After something changed what the character owns (a recipe item used, a prop taught, an expansion used): tells
        /// the client which unlocks to hide or show again, one RecipeBase {u32 sub, u32 recipe} each (the same framing as
        /// AddRecipe); the client rebuilds an open crafting window by itself [P]. Nothing before SendSelf.
        /// </summary>
        public static void RefreshHidden(WorldSession session)
        {
            if (session.HiddenRecipes == null)
                return;
            HashSet<uint> now = HiddenUnlocks(session);
            foreach (uint recipe in now.Where(r => !session.HiddenRecipes.Contains(r)))
                SendHide(session, HideRecipeSub, recipe);
            foreach (uint recipe in session.HiddenRecipes.Where(r => !now.Contains(r)))
            {
                SendHide(session, UnhideRecipeSub, recipe);
                // SendSelf left this unlock's record out (bandwidth R1): the client has nothing to list until next login.
                if (session.RecipeRecordsWithheld?.Contains(recipe) == true)
                    log.Warn($"{session.CharacterName}: unlock recipe {recipe} unhidden, but its record was withheld at login; it shows after the next login.");
            }
            session.HiddenRecipes = now;
        }

        private static void SendHide(WorldSession session, uint sub, uint recipe)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(sub);
            writer.Write(recipe);
            session.EnqueueMessage(ZoneMessageOpcode.RecipeBase, output.ToArray());
        }

        /// <summary>The prop's crafting recipes the character doesn't know yet.</summary>
        private static uint[] FreshCompanions(WorldSession session, uint prop)
            => CompanionRecipes(prop).Where(r => !session.LearnedRecipes.Contains(r)).ToArray();

        /// <summary>
        /// Teaches a prop (if not known yet: <see cref="Unlocks.PropTaught"/> + KnownProp AddPropResponse) and the prop's
        /// crafting recipes not known yet (<paramref name="fresh"/>: saved, AddRecipe each), then counts the goals.
        /// </summary>
        private static void TeachProp(WorldSession session, uint recipeItem, uint prop, uint[] fresh)
        {
            if (!Unlocks.Has(session, Unlocks.PropTaught, prop))
            {
                Unlocks.Add(session, Unlocks.PropTaught, prop);
                SendKnownProp(session, prop);
            }
            // AddRecipes ends with RefreshHidden; only without fresh recipes is it needed here (bandwidth audit R9: the
            // second call diffed nothing but re-ran TeachesNew over all 581 unlocks).
            if (fresh.Length > 0)
                AddRecipes(session, fresh);
            else
                RefreshHidden(session);
            // Recipe Discoveries ("Recipe: X"), "<prop item> recipe unlocked" and "<creature> unlocked" goals (TASKS 5.7, 5.10).
            Achievements.PropRecipeLearned(session, recipeItem, prop);
        }

        /// <summary>
        /// Adds recipes to the character's book: kept in memory, saved, and each sent as RecipeBase 8 AddRecipe {u32 recipe,
        /// u32 name text} (the client shows "Learned recipe" with the name of the first recipe's output item).
        /// </summary>
        private static void AddRecipes(WorldSession session, uint[] fresh)
        {
            ulong characterId = session.PlayerGuid;
            foreach (uint recipe in fresh)
                session.LearnedRecipes.Add(recipe);
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
            RefreshHidden(session);
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

        /// <summary>
        /// A prop recipe item (grant type 45): teaches its prop (and the prop's crafting recipes) and is used up. A prop
        /// already known, with every crafting recipe known too, is refused with the client's 568756 "You've already
        /// learned this prop!" (<see cref="ItemUses.Refuse"/>, InventoryPacketShowActivationFailure 0x2D / 0x1E) and the
        /// item is kept.
        /// </summary>
        private static bool LearnProp(WorldSession session, uint itemDefinitionId)
        {
            if (!propsByItem.TryGetValue(itemDefinitionId, out uint prop))
                return false;
            uint[] fresh = FreshCompanions(session, prop);
            if (Unlocks.Has(session, Unlocks.PropTaught, prop) && fresh.Length == 0)
            {
                ItemUses.Refuse(session, ItemUses.PropKnownText);
                return true;
            }
            if (!session.TakeItem(itemDefinitionId, 1))
                return true;
            TeachProp(session, itemDefinitionId, prop, fresh);
            log.Info($"{session.CharacterName} learned prop {prop} from item {itemDefinitionId}"
                + (fresh.Length > 0 ? $" (and recipe(s) {string.Join(", ", fresh)})." : "."));
            return true;
        }

        /// <summary>
        /// Using a recipe item: learn what it teaches (once), use the item up and tell the client
        /// (RecipeBase 8 AddRecipe: u32 recipe id, u32 name text id → "Learned recipe" message). When every recipe it
        /// teaches is known the use is refused (<see cref="ItemUses.Refuse"/>) with the generic 560768 "Error: Failed
        /// item requirement": the client has no "recipe already known" text, and the item is kept.
        /// </summary>
        public static bool Learn(WorldSession session, uint itemDefinitionId)
        {
            if (!RecipesByItem.TryGetValue(itemDefinitionId, out uint[] recipes))
                return LearnProp(session, itemDefinitionId);
            uint[] fresh = recipes.Where(r => !session.LearnedRecipes.Contains(r)).ToArray();
            if (fresh.Length == 0)
            {
                ItemUses.Refuse(session, ItemUses.FailedItemRequirementText);
                return true;
            }
            if (!session.TakeItem(itemDefinitionId, 1))
                return true;
            AddRecipes(session, fresh);
            foreach (uint recipe in fresh)
                Achievements.RecipeLearned(session, itemDefinitionId,
                    GameTableManager.Instance.Recipes.GetEntry(recipe)?.ItemDefinitionId ?? 0);
            log.Info($"{session.CharacterName} learned recipe(s) {string.Join(", ", fresh)} from item {itemDefinitionId}.");
            return true;
        }
    }
}
