using LandmarkEmulator.Shared.GameTable;
using LandmarkEmulator.Shared.GameTable.Model;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Crafting stations: which props are stations, and opening their crafting window. The prop → station link is
    /// server data the client doesn't have; it is rebuilt from the client tables: a prop whose name is a station's
    /// name, else the station kind in its model name (sifter, forge).
    /// </summary>
    public static class CraftingStations
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private static Dictionary<uint, uint> stationByProp = new();

        /// <summary>Model name keyword → station, for stations no prop is named after.</summary>
        private static readonly (string Keyword, uint Station)[] modelKeywords =
        {
            ("CraftingStation_Sifter", 28),
            ("CraftingStations_Sifters", 28),
            ("CraftingStation_Forge", 48),
            ("CraftingStations_Forges", 48),
            ("CraftingStation_Workshop", 30),
            ("CraftingStation_Salvager", 41)
        };

        /// <summary>
        /// Props whose name points at the wrong station: the Lumen Station (item 3265) places prop 2565, named "Magical
        /// Monument" like the salvager, but its own description is "ether shard shop" = station 55 (Lumen shop).
        /// </summary>
        private static readonly Dictionary<uint, uint> propOverrides = new() { [2565] = 55 };

        /// <summary>
        /// Maps prop definitions to crafting stations: by name (PropDefs NAME_STRING_ID = CraftingStations NAME), otherwise by
        /// a keyword in the prop's model name (Sifter, Forge, ...). Called once when the prop list is loaded.
        /// </summary>
        public static void Build(IReadOnlyDictionary<uint, string> propModelNames)
        {
            Dictionary<uint, uint> stationByName = GameTableManager.Instance.CraftingStations.Entries
                .GroupBy(s => s.NameId).ToDictionary(g => g.Key, g => g.First().Id);
            var map = new Dictionary<uint, uint>();
            foreach (PropDefEntry prop in GameTableManager.Instance.PropDefs.Entries)
            {
                if (stationByName.TryGetValue(prop.NameStringId, out uint station))
                    map[prop.Id] = station;
                else if (propModelNames.TryGetValue(prop.Id, out string model))
                    foreach ((string keyword, uint keywordStation) in modelKeywords)
                        if (model.Contains(keyword))
                        {
                            map[prop.Id] = keywordStation;
                            break;
                        }
            }
            foreach ((uint prop, uint station) in propOverrides)
                map[prop] = station;
            stationByProp = map;
            log.Info($"{map.Count} props are crafting stations.");
        }

        /// <summary>
        /// Full recipe records for SendSelf (the client's crafting window lists only recipes it has a record for;
        /// it doesn't build them from its own Recipes.txt). Only known recipes that a station can make.
        /// </summary>
        public static List<Network.Message.Model.SendSelfToClient.RecipeData.Recipe> BuildRecipeRecords(
            IEnumerable<uint> knownRecipes, int limit = int.MaxValue)
        {
            var known = new HashSet<uint>(knownRecipes);
            var stationRecipes = GameTableManager.Instance.CraftingStationRecipeMappings.Entries
                .Select(m => m.RecipeId).Where(known.Contains).Distinct().Take(limit);

            var records = new List<Network.Message.Model.SendSelfToClient.RecipeData.Recipe>();
            foreach (uint recipeId in stationRecipes)
            {
                RecipeEntry recipe = GameTableManager.Instance.Recipes.GetEntry(recipeId);
                if (recipe == null)
                    continue;
                var output = Inventory.ItemDefinitionManager.Get(recipe.ItemDefinitionId);
                var record = new Network.Message.Model.SendSelfToClient.RecipeData.Recipe
                {
                    RecipeId = recipe.Id,
                    Unknown1 = recipe.RecipeType,
                    Unknown2 = recipe.SortOrder,
                    Itemid = recipe.ItemDefinitionId,
                    Quantity = recipe.ItemCount,
                    NameId = new Shared.Game.LandmarkText(recipe.OverrideNameId != 0 ? recipe.OverrideNameId : output?.NameId ?? 0),
                    DescriptionId = new Shared.Game.LandmarkText(output?.DescriptionId ?? 0),
                    IconId = output?.ImageSetId ?? 0
                };
                // The window adds one row per entry here (FUN_14355de30 loops record +0x58): the output item's
                // categories, which CraftingCategoryGroupMapDefinitions groups into the window's sections.
                if (output != null)
                    record.Unknown16.AddRange(output.Categories);
                uint index = 0;
                foreach (RecipeComponentEntry component in GameTableManager.Instance.ComponentsOf(recipe.Id))
                {
                    var item = Inventory.ItemDefinitionManager.Get(component.ComponentId);
                    record.Components.Add(new Network.Message.Model.SendSelfToClient.RecipeData.Recipe.RecipeComponent
                    {
                        Index = index++,
                        Unknown1 = component.ComponentType,
                        ItemId = component.ComponentId,
                        Quantity = component.ComponentCount,
                        NameId = new Shared.Game.LandmarkText(component.NameId != 0 ? component.NameId : item?.NameId ?? 0),
                        IconId = component.IconId != 0 ? component.IconId : item?.ImageSetId ?? 0
                    });
                }
                records.Add(record);
            }
            return records;
        }

        /// <summary>A prop that works as this station (the first found), or 0.</summary>
        public static uint PropForStation(uint station) => stationByProp.FirstOrDefault(p => p.Value == station).Key;

        public static uint StationForProp(uint propId) => stationByProp.TryGetValue(propId, out uint station) ? station : 0;

        /// <summary>
        /// Opens the crafting window for a station (RecipeBase 7 CraftWindowEnableResponse, reader FUN_142e91340:
        /// u32 result (0 = open, 5 = private station), u32 station, u64).
        /// </summary>
        public static void OpenWindow(WorldSession session, uint station, ulong propGuid = 0)
        {
            var output = new List<byte>();
            var writer = new Shared.Network.GamePacketWriter(output);
            writer.Write(7u);
            writer.Write(0u);
            writer.Write(station);
            writer.Write(0ul);
            session.EnqueueMessage(ZoneMessageOpcode.RecipeBase, output.ToArray());
            Crafting.StationOpened(session, station, propGuid);
        }
    }
}
