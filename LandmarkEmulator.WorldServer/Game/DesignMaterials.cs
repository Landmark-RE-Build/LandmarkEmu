using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.GameTable;
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
    /// "Get Resources Now" in the Design Details window (0.695; update notes 14 Jul 2016, P6087: when materials are
    /// short for a design, the Design Details window offers to turn Lumens into the missing materials, in the bundle
    /// sizes and at the cost of the Lumen Station;
    /// spec trace/0695/spec-buy-materials.md). TemplateBase 0xF0:
    /// <list type="bullet">
    /// <item>0x1D DataRequest (C→S, Lua Templates.RequestPurchaseTemplateMaterialsData, builder FUN_143113530): u64 guid +
    /// the client's copy of the design, of which only the guid is read.</item>
    /// <item>0x1E DataResponse (S→C, reader FUN_143100050, handler FUN_14310c290 → Character.Templates.PointOfSale):
    /// u64 guid, u32 n ≤ 100 × {recipe, crafts, lumens, item, items crafted, items needed, image set, name text}, u32 total.
    /// The TemplateConfirmationWidget lists "Nx Name" and compares the total with Character.Currencies; it redraws the
    /// cost line only when the currencies change (L57), so the currency update is re-sent after the answer.</item>
    /// <item>0x1F PurchaseMissing (C→S, Lua Templates.PurchaseMissingTemplateMaterials, builder FUN_1431118e0): u64 guid,
    /// u32 n × {recipe, crafts}, u32 total. No answer packet exists: the items and Lumens update.</item>
    /// </list>
    /// Bundles are the Lumen Station's (station 55) recipes: each costs only Lumens (item 1579) and makes one item, so an
    /// item has one bundle size and price; bundles needed = ceil(missing / bundle size).
    /// </summary>
    public static class DesignMaterials
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const byte DataRequestSub = 0x1D, DataResponseSub = 0x1E, PurchaseSub = 0x1F;

        private const uint LumenStation = 55;
        private const int MaxRows = 100;
        private const uint MaxCrafts = 1000;

        /// <summary>
        /// Ours (U1 of the spec): also offer the "Recipe:" item for a design prop the player can't place yet, as the
        /// client's unused getResourcesString lists ("1x Recipe:&lt;prop&gt;"), and the props' recipe components.
        /// </summary>
        private const bool IncludePropNeeds = true;

        private sealed record Bundle(uint Recipe, uint Item, uint Count, uint Lumens);

        private static Dictionary<uint, Bundle> byItem;
        private static Dictionary<uint, Bundle> byRecipe;

        /// <summary>The Lumen Station's bundles: recipes mapped to station 55 whose only component is Lumens.</summary>
        private static void EnsureLoaded()
        {
            if (byItem != null)
                return;
            var items = new Dictionary<uint, Bundle>();
            var recipes = new Dictionary<uint, Bundle>();
            foreach (var mapping in GameTableManager.Instance.CraftingStationRecipeMappings.Entries
                .Where(m => m.CraftingStationId == LumenStation && m.RecipeId != 0))
            {
                var recipe = GameTableManager.Instance.Recipes.GetEntry(mapping.RecipeId);
                var components = GameTableManager.Instance.ComponentsOf(mapping.RecipeId).ToList();
                if (recipe == null || recipe.ItemDefinitionId == 0 || recipe.ItemCount == 0 || components.Count != 1
                    || components[0].ComponentType != 1 || components[0].ComponentId != Inventory.Inventory.CurrencyItem)
                    continue;
                var bundle = new Bundle(recipe.Id, recipe.ItemDefinitionId, recipe.ItemCount, components[0].ComponentCount);
                recipes[recipe.Id] = bundle;
                if (!items.TryGetValue(bundle.Item, out Bundle other)
                    || (double)bundle.Lumens / bundle.Count < (double)other.Lumens / other.Count)
                    items[bundle.Item] = bundle;
            }
            byRecipe = recipes;
            byItem = items;
            log.Info($"Lumen Station: {items.Count} items sold in bundles.");
        }

        /// <summary>Last offer per player (guid → recipe crafts), so a purchase must echo it.</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<WorldSession, (ulong Guid, Dictionary<uint, uint> Crafts)> offers = new();

        /// <summary>0x1D: what the player is missing for a design, in Lumen Station bundles.</summary>
        public static void SendOffer(WorldSession session, ulong guid)
        {
            EnsureLoaded();
            var rows = new List<(Bundle Bundle, uint Crafts, uint Missing)>();
            if (Templates.FindDesign(session.PlayerGuid, guid) is TemplateModel design)
                foreach ((uint item, uint missing) in Shortfall(session, design))
                    if (byItem.TryGetValue(item, out Bundle bundle) && rows.Count < MaxRows)
                        rows.Add((bundle, (missing + bundle.Count - 1) / bundle.Count, missing));
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(DataResponseSub);
            w.Write(guid);
            w.Write((uint)rows.Count);
            uint total = 0;
            foreach ((Bundle bundle, uint crafts, uint missing) in rows)
            {
                var definition = Inventory.ItemDefinitionManager.Get(bundle.Item);
                uint lumens = crafts * bundle.Lumens;
                total += lumens;
                w.Write(bundle.Recipe);
                w.Write(crafts);
                w.Write(lumens);
                w.Write(bundle.Item);
                w.Write(crafts * bundle.Count);
                w.Write(missing);
                w.Write(definition?.ImageSetId ?? 0u);
                w.Write(definition?.NameId ?? 0u);
            }
            w.Write(total);
            session.EnqueueMessage(ZoneMessageOpcode.TemplateBase, output.ToArray());
            offers[session] = (guid, rows.ToDictionary(r => r.Bundle.Recipe, r => r.Crafts));
            // The confirmation's cost line redraws only on a currency change (TemplateConfirmationWidget.as 176-179).
            session.SendCurrencies();
            log.Info($"{session.CharacterName} asked what design 0x{guid:X} is missing: {rows.Count} bundle row(s), {total} Lumens.");
        }

        /// <summary>
        /// What the player lacks, in the client's order (FUN_143115b90): the design's materials (one item per voxel,
        /// BuildMaterials.ItemFor; free materials and 0xFFFC skipped), then its props (prop items held first; a "Recipe:"
        /// item when the player can't place the prop yet; the prop recipe's components), less what the player holds.
        /// </summary>
        private static List<(uint Item, uint Missing)> Shortfall(WorldSession session, TemplateModel design)
        {
            var need = new Dictionary<uint, long>();
            var order = new List<uint>();
            void Add(uint item, long count)
            {
                if (item == 0 || count <= 0)
                    return;
                if (!need.ContainsKey(item))
                    order.Add(item);
                need[item] = need.GetValueOrDefault(item) + count;
            }
            foreach ((ushort material, uint count) in Templates.MaterialsOf(design))
                if (material != 0xFFFC)
                    Add(Terrain.BuildMaterials.ItemFor(material), count);
            if (IncludePropNeeds && design.Id != 0)
                foreach (var kind in Templates.PropsOf(design.Id).GroupBy(p => p.PropId))
                {
                    long remaining = kind.Count() - Inventory.ItemDefinitionManager.ItemsForProp(kind.Key)
                        .Sum(d => (long)session.Inventory.CountOf(d.Id));
                    if (remaining <= 0)
                        continue;
                    if (!RecipeBook.KnowsProp(session, kind.Key))
                        Add(RecipeBook.TeachingItemsFor(kind.Key).FirstOrDefault(i => byItem.ContainsKey(i)), 1);
                    foreach ((uint item, uint count) in Props.RecipeCostOf(kind.Key) ?? new List<(uint, uint)>())
                        Add(item, remaining * count);
                }
            return order.Select(i => (i, need[i] - session.Inventory.CountOf(i)))
                .Where(x => x.Item2 > 0).Select(x => (x.i, (uint)Math.Min(x.Item2, uint.MaxValue))).ToList();
        }

        /// <summary>0x1F: buys the offered bundles: checked against the last offer and the tables, priced by the tables.</summary>
        public static void Purchase(WorldSession session, byte[] body)
        {
            EnsureLoaded();
            var reader = new GamePacketReader(body);
            ulong guid = reader.ReadULong();
            uint n = reader.ReadUInt();
            if (n == 0 || n > MaxRows || !offers.TryRemove(session, out var offer) || offer.Guid != guid)
            {
                log.Warn($"{session.CharacterName}: design material purchase without a matching offer ({n} rows).");
                return;
            }
            var rows = new List<(Bundle Bundle, uint Crafts)>();
            for (uint i = 0; i < n; i++)
            {
                uint recipe = reader.ReadUInt(), crafts = reader.ReadUInt();
                if (!byRecipe.TryGetValue(recipe, out Bundle bundle) || crafts == 0 || crafts > MaxCrafts
                    || !offer.Crafts.TryGetValue(recipe, out uint offered) || offered != crafts)
                {
                    log.Warn($"{session.CharacterName}: design material purchase refused (recipe {recipe} × {crafts} not offered).");
                    return;
                }
                rows.Add((bundle, crafts));
            }
            long cost = rows.Sum(r => (long)r.Bundle.Lumens * r.Crafts);
            if (!session.ChangeCoins(-cost))
            {
                Chat.System(session, $"You need {cost - session.Inventory.CountOf(Inventory.Inventory.CurrencyItem)} more lumens.");
                return;
            }
            foreach ((Bundle bundle, uint crafts) in rows)
                session.GiveItem(bundle.Item, bundle.Count * crafts);
            log.Info($"{session.CharacterName} bought materials for design 0x{guid:X}: {rows.Sum(r => r.Crafts)} bundle(s), {cost} Lumens.");
        }
    }
}
