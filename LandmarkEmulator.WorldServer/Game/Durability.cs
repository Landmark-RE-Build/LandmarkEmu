using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Configuration;
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
    /// Equipment condition. Weapons and armour name a DurabilitySet.txt row (item definition +0x168): DURABILITY_COUNT
    /// points, LOSS_PER_HP_DMG per hit point of damage taken, LOSS_PER_DEATH per death. Items keep their wear (points
    /// lost); the client shows Condition = (count - wear) / count (FUN_1435585b0) from item +0x148, which the server
    /// sends in the item record and in ItemUpdate. At 0 % the item "is no longer functioning at maximum strength"
    /// (text 575602): its weapon damage is halved here. Gathering tools have no set and never wear.
    ///
    /// Repair stations (CraftingStations type 6, PARAM1 = currency item 1579 Lumen, FLOAT_PARAM1 = 0.004 per point):
    /// RecipeBase 0x21 RepairItemRequest {u64 item guid, u32 currency item, u32 cost} → 0x22 {u32 sub, bool}.
    /// ItemUpdates go out only when the shown percentage changes (a hit wears ~1 point of 9834).
    /// </summary>
    public static class Durability
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const uint RepairItemRequest = 0x21, RepairItemResponse = 0x22;
        /// <summary>Repair stations: Lumen per durability point (CraftingStations 56/57 FLOAT_PARAM1).</summary>
        private const float RepairCostPerPoint = 0.004f;
        private const uint RepairCurrency = 1579;

        private record Set(float Count, float LossPerDamage, float LossPerDeath);

        private static Dictionary<uint, Set> sets;

        private static Dictionary<uint, Set> Sets
        {
            get
            {
                if (sets != null)
                    return sets;
                var map = new Dictionary<uint, Set>();
                try
                {
                    string gameData = SharedConfiguration.Configuration.GetValue("GameDataPath", "gameData");
                    foreach (string line in File.ReadLines(Path.Combine(gameData, "DurabilitySet.txt")).Skip(1))
                    {
                        string[] f = line.Split('^');
                        if (f.Length > 3 && uint.TryParse(f[0], out uint id) && float.TryParse(f[1], out float count)
                            && float.TryParse(f[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float perDamage)
                            && float.TryParse(f[3], out float perDeath))
                            map[id] = new Set(count, perDamage, perDeath);
                    }
                }
                catch (Exception e)
                {
                    log.Error(e, "Could not load DurabilitySet.txt; equipment never wears.");
                }
                return sets = map;
            }
        }

        private static Set SetOf(CharacterItemModel item)
            => Inventory.ItemDefinitionManager.Get(item.ItemDefinitionId) is { DurabilitySet: > 0 } d
               && Sets.TryGetValue(d.DurabilitySet, out Set set) && set.Count > 0 ? set : null;

        /// <summary>Shown condition, 0-100 (100 for items that never wear).</summary>
        public static int Condition(CharacterItemModel item)
        {
            Set set = SetOf(item);
            return set == null ? 100 : (int)((set.Count - Math.Clamp(item.Wear, 0f, set.Count)) / set.Count * 100f);
        }

        /// <summary>Damage multiplier of a weapon: half strength at 0 % condition.</summary>
        public static float Strength(CharacterItemModel item) => item != null && Condition(item) == 0 ? 0.5f : 1f;

        /// <summary>The player took damage (and maybe died): their equipped weapons and armour wear.</summary>
        public static void Damaged(WorldSession session, uint damage, bool died)
        {
            foreach (CharacterItemModel item in session.Inventory.Items.Where(i => i.Container == Inventory.Inventory.EquippedContainer).ToList())
            {
                Set set = SetOf(item);
                if (set == null)
                    continue;
                float loss = damage * set.LossPerDamage + (died ? set.LossPerDeath : 0f);
                if (loss <= 0f || item.Wear >= set.Count)
                    continue;
                int before = Condition(item);
                item.Wear = Math.Min(set.Count, item.Wear + loss);
                int after = Condition(item);
                // At 0 % the client itself says "Some of your gear is broken." (FUN_142e876e0 watches the equipped
                // items' condition, Durability.Ui.BrokenItem.SystemMessage) and the tooltip shows the set's text 575602.
                if (after != before)
                {
                    session.SendItem(item, replace: true);
                    FirstTimeEvents.Trigger(session, FirstTimeEvents.FirstWear);
                }
            }
        }

        /// <summary>
        /// RepairItemRequest {u64 guid, u32 currency, u32 cost}: at a repair station, pay Lumen for the wear and reset it.
        /// The server works out the price itself.
        /// </summary>
        public static void HandleRepair(WorldSession session, byte[] body)
        {
            var reader = new GamePacketReader(body);
            ulong guid = reader.ReadULong();
            CharacterItemModel item = session.Inventory.Items.FirstOrDefault(i => i.Id == guid);
            bool ok = false;
            if (item != null && SetOf(item) != null && item.Wear > 0f && Crafting.OpenStation(session) is 56 or 57)
            {
                uint cost = (uint)Math.Ceiling(item.Wear * RepairCostPerPoint);
                if (cost == 0 || session.TakeItem(RepairCurrency, cost))
                {
                    item.Wear = 0f;
                        session.SendItem(item, replace: true);
                    ok = true;
                    log.Info($"{session.CharacterName} repaired item {item.ItemDefinitionId} for {cost} Lumen.");
                }
            }
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(RepairItemResponse);
            writer.Write(ok);
            session.EnqueueMessage(ZoneMessageOpcode.RecipeBase, output.ToArray());
        }
    }
}
