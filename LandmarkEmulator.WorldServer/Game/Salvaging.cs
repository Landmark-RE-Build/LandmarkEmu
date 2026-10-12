using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Shared.GameTable;
using LandmarkEmulator.Shared.GameTable.Model;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using Microsoft.Extensions.Configuration;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Salvaging at a salvaging station (CraftingStations 41 "SalvagingStation", the Magical Monument): recipes whose
    /// components are "any item of a kind" — e.g. recipe 993, any equipment → 276 Lumens. RecipeBase subs (u32):
    /// <list type="bullet">
    /// <item>0x19 SalvagingAddItemRequest {u32 recipe, u32 slot, u64 item} (Lua SalvagingStationAddItem → FUN_142e9e620)
    /// → 0x1A SalvagingAddItemResponse {bool ok, u32 recipe, u32 slot, u64 item} (reader FUN_142e91a90); the client only
    /// fills the slot when the server agrees.</item>
    /// <item>0x18 SalvagingStartRequest {u32 recipe, u32 n × {u64 item, string, string, u32 icon, u32}} (the Salvage
    /// button, FUN_142e9f810) → the usual StartResponse (2), then CompleteResponse (5) after the casting time.</item>
    /// </list>
    /// Component types (RecipeComponents COMPONENT_TYPE, client check FUN_142e9d0e0): 1 item, 2 currency, 4 an item
    /// with that item category, 5 an item with a category in that crafting category group
    /// (CraftingCategoryGroupMapDefinitions.txt), 6/7 accepted by the client as is (7 = keywords; the server asks for a
    /// single, unstackable item).
    /// </summary>
    public static class Salvaging
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const uint SalvageStation = 41;
        public const uint AddItemRequest = 0x19, AddItemResponse = 0x1A, StartRequest = 0x18;

        private static Dictionary<uint, HashSet<uint>> categoryGroups;

        /// <summary>Crafting category group → item categories.</summary>
        private static Dictionary<uint, HashSet<uint>> CategoryGroups
        {
            get
            {
                if (categoryGroups != null)
                    return categoryGroups;
                var groups = new Dictionary<uint, HashSet<uint>>();
                try
                {
                    string gameData = SharedConfiguration.Configuration.GetValue("GameDataPath", "gameData");
                    foreach (string line in File.ReadLines(Path.Combine(gameData, "CraftingCategoryGroupMapDefinitions.txt")))
                    {
                        if (line.Length == 0 || line[0] == '#')
                            continue;
                        string[] f = line.Split('^');
                        if (f.Length >= 3 && uint.TryParse(f[1], out uint category) && uint.TryParse(f[2], out uint group))
                        {
                            if (!groups.TryGetValue(group, out var set))
                                groups[group] = set = new HashSet<uint>();
                            set.Add(category);
                        }
                    }
                }
                catch (Exception e)
                {
                    log.Warn($"Could not read CraftingCategoryGroupMapDefinitions.txt: {e.Message}");
                }
                return categoryGroups = groups;
            }
        }

        /// <summary>The recipe's item slots in the order the client numbers them.</summary>
        private static List<RecipeComponentEntry> Slots(uint recipeId)
            => GameTableManager.Instance.ComponentsOf(recipeId).ToList();

        /// <summary>Is the recipe made at the salvaging station the player has open?</summary>
        private static bool AtStation(WorldSession session, uint recipeId)
            => Crafting.OpenStation(session) == SalvageStation && GameTableManager.Instance.CraftingStationRecipeMappings.Entries
                .Any(m => m.CraftingStationId == SalvageStation && m.RecipeId == recipeId);

        /// <summary>Does this bag item fit a component (the client's rules, FUN_142e9d0e0)?</summary>
        private static bool Fits(RecipeComponentEntry component, CharacterItemModel item)
        {
            var definition = Inventory.ItemDefinitionManager.Get(item.ItemDefinitionId);
            if (definition == null)
                return false;
            return component.ComponentType switch
            {
                1 => item.ItemDefinitionId == component.ComponentId,
                4 => definition.Categories.Contains(component.ComponentId),
                5 => CategoryGroups.TryGetValue(component.ComponentId, out var group) && definition.Categories.Any(group.Contains),
                6 or 7 => definition.MaxStack <= 1,
                _ => false
            };
        }

        private static CharacterItemModel BagItem(WorldSession session, ulong guid)
            => session.Inventory.Get(guid) is { Container: Inventory.Inventory.BagContainer } item ? item : null;

        public static void Handle(WorldSession session, uint sub, byte[] body)
        {
            var reader = new GamePacketReader(body);
            try
            {
                if (sub == AddItemRequest)
                    AddItem(session, reader.ReadUInt(), reader.ReadUInt(), reader.ReadULong());
                else if (sub == StartRequest)
                    Start(session, reader);
            }
            catch (Exception e)
            {
                log.Debug($"Unreadable salvage request 0x{sub:X2} from {session.CharacterName}: {e.Message}");
            }
        }

        private static void AddItem(WorldSession session, uint recipeId, uint slot, ulong guid)
        {
            List<RecipeComponentEntry> slots = Slots(recipeId);
            CharacterItemModel item = BagItem(session, guid);
            bool ok = AtStation(session, recipeId) && slot < slots.Count && item != null && Fits(slots[(int)slot], item);
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(AddItemResponse);
            writer.Write(ok);
            writer.Write(recipeId);
            writer.Write(slot);
            writer.Write(guid);
            session.EnqueueMessage(ZoneMessageOpcode.RecipeBase, output.ToArray());
        }

        private static void Start(WorldSession session, GamePacketReader reader)
        {
            uint recipeId = reader.ReadUInt();
            uint count = reader.ReadUInt();
            var guids = new List<ulong>();
            for (uint i = 0; i < count && i < 16; i++)
            {
                guids.Add(reader.ReadULong());
                reader.ReadString();
                reader.ReadString();
                reader.ReadUInt();
                reader.ReadUInt();
            }
            string error = TryStart(session, recipeId, guids);
            if (error != null)
                log.Info($"{session.CharacterName} could not salvage with recipe {recipeId}: {error}");
            Crafting.SendStartResponse(session, error);
        }

        private static string TryStart(WorldSession session, uint recipeId, List<ulong> guids)
        {
            RecipeEntry recipe = GameTableManager.Instance.Recipes.GetEntry(recipeId);
            if (recipe == null || !AtStation(session, recipeId))
                return "That can't be salvaged here.";
            if (!recipe.DefaultLearned && !session.LearnedRecipes.Contains(recipeId))
                return "You don't know that recipe.";
            List<RecipeComponentEntry> slots = Slots(recipeId);
            if (guids.Count != slots.Count || guids.Distinct().Count() != guids.Count)
                return "Fill every slot.";
            for (int i = 0; i < slots.Count; i++)
            {
                CharacterItemModel item = BagItem(session, guids[i]);
                if (item == null || !Fits(slots[i], item) || item.Count < Math.Max(1u, slots[i].ComponentCount))
                    return "An item doesn't fit its slot.";
            }
            if (!Crafting.Idle(session))
                return "You are already crafting.";
            for (int i = 0; i < slots.Count; i++)
            {
                session.TakeFromStack(guids[i], Math.Max(1u, slots[i].ComponentCount));
            }
            Crafting.Queue(session, recipe, 1);
            log.Info($"{session.CharacterName} salvaged {slots.Count} item(s) with recipe {recipeId}.");
            return null;
        }
    }
}
