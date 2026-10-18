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
    /// (text 575602): its weapon damage is halved here.
    ///
    /// Gathering tools (TASKS 6.53, P4524 13 Apr 2016: an equipped harvesting tool loses condition with each harvest):
    /// in the live item dump exactly 12 tools name a set (checked over all 2,885 records,
    /// 2026-10-16, definition u41_46[4]): set 1 (9751 points) the eight crafted tier tools — Farseer 3257, Goliath 3255,
    /// Charmed 3254, Whirlwind 3256 picks and Hotfoot 3263, Goliath 3261, Charmed 3260, Whirlwind 3262 axes — and set 5
    /// (14751) the four NovaTech tools 3702-3705. Every other tool (the Adventurer's starters 3253 / 3259, Hewer's,
    /// Miner's...) has set 0 and never wears. Sets 1 / 5 have LOSS_PER_HP_DMG = LOSS_PER_DEATH = 0, so damage and
    /// death never wear a tool (<see cref="Damaged"/> skips them by the numbers); the harvest loss is not a client
    /// column at all (the client's DurabilitySetDefinition reader reads only DURABILITY_COUNT / LOSS_PER_HP_DMG /
    /// LOSS_PER_DEATH, 0.695 part_0146 DurabilitySetDefinition), so it was a server value: see
    /// <see cref="ToolLossPerHarvest"/> (open lead L11).
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
                        // Invariant for every column (as LOSS_PER_HP_DMG already was): with the machine's culture a
                        // comma-decimal Windows would read "984.5" as 9845.
                        var invariant = System.Globalization.CultureInfo.InvariantCulture;
                        var number = System.Globalization.NumberStyles.Float;
                        if (f.Length > 3 && uint.TryParse(f[0], out uint id) && float.TryParse(f[1], number, invariant, out float count)
                            && float.TryParse(f[2], number, invariant, out float perDamage)
                            && float.TryParse(f[3], number, invariant, out float perDeath))
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
                    // Saved at each shown step (at most 100 a life): wear is part of the item row and travels with it.
                    session.Inventory.SaveWear(item);
                    FirstTimeEvents.Trigger(session, FirstTimeEvents.FirstWear);
                }
                if (after == 0 && before > 0) Achievements.GearBroken(session, item.ItemDefinitionId);   // No Pants! (TASKS 5.10)
            }
        }

        /// <summary>
        /// Condition points a gathering tool loses per harvesting swing (a mining swing that collects, every axe hit on a
        /// tree, every reaped plant). INFERRED: the live value was server data (not in any client table or the item
        /// definition; open lead L11). One point per swing gives the set-1 tools 9,750 swings and the NovaTech tools
        /// 14,750 swings from 100 % to 0 % (about 2.5 / 3.7 hours of non-stop harvesting at the tools' swing rates) and
        /// a full repair of 39 / 59 Lumen at <see cref="RepairCostPerPoint"/> — the same order as the weapons and armour
        /// sets, which lose 10 % per death. The 26 Apr 2016 hotfix (P4771: items take almost twice as long to reach 0 %
        /// condition) predates the 0.589 / 0.695 tables, so the counts above are already the slower ones.
        /// </summary>
        public const float ToolLossPerHarvest = 1f;

        /// <summary>Equipped-container slot of the main hand (EquipmentSlotDefinitions 2 "Main Hand").</summary>
        private const uint MainHandEquipSlot = 2;

        /// <summary>
        /// The held gathering tool was used to harvest once: it loses <see cref="ToolLossPerHarvest"/> condition when
        /// it names a durability set (the 12 tier tools above; starter tools never wear). Like <see cref="Damaged"/>,
        /// an ItemUpdate goes out only when the shown percentage changes, and the client itself raises "Some of your
        /// gear is broken." at 0 % (FUN_142e876e0). Hooks: Gathering.Chop / Gathering.Reap / Gathering.RollDrops
        /// (mining swings; Abilities.Dig calls RollDrops once per collecting swing).
        /// </summary>
        public static void ToolUsed(WorldSession session)
        {
            if (session?.Inventory == null || Inventory.Inventory.EquippedSlotFor(MainHandEquipSlot) is not uint hand)
                return;
            CharacterItemModel tool = session.Inventory.At(Inventory.Inventory.EquippedContainer, hand);
            Set set = tool == null ? null : SetOf(tool);
            if (set == null || tool.Wear >= set.Count)
                return;
            int before = Condition(tool);
            tool.Wear = Math.Min(set.Count, tool.Wear + ToolLossPerHarvest);
            int after = Condition(tool);
            if (after != before)
            {
                session.SendItem(tool, replace: true);
                session.Inventory.SaveWear(tool);
                FirstTimeEvents.Trigger(session, FirstTimeEvents.FirstWear);
                if (after == 0)
                    log.Info($"{session.CharacterName}'s tool {tool.ItemDefinitionId} broke (0 % condition).");
            }
        }

        /// <summary>
        /// Yield multiplier of the held gathering tool: half at 0 % condition, like a broken weapon's damage
        /// (<see cref="Strength"/>). Live: an item at 0 % condition breaks and works only in a limited way (P4524) and,
        /// filed under gathering, items at 0 % work a little better than before (P4846, 26 Apr 2016); the tooltip text
        /// 575602 "no longer functioning at maximum strength". What the limit meant for a tool is not in the client:
        /// halving the yield is INFERRED (mirrors the weapon rule).
        /// Applied by Gathering.Chop; mining (Abilities.Dig) still needs the same factor on its yield.
        /// </summary>
        public static float ToolStrength(WorldSession session)
        {
            if (session?.Inventory == null || Inventory.Inventory.EquippedSlotFor(MainHandEquipSlot) is not uint hand)
                return 1f;
            CharacterItemModel tool = session.Inventory.At(Inventory.Inventory.EquippedContainer, hand);
            return tool != null && SetOf(tool) != null ? Strength(tool) : 1f;
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
                    session.Inventory.SaveWear(item);
                    ok = true;
                    Combat.GearStats.Refresh(session);   // repaired armour gets its full bonus back at once
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
