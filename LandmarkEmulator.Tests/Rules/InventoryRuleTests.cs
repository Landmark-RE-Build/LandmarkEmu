using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Game.Inventory;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// Inventory rules: overflow expiry (OverflowExpiryHours, 24 = live, wiki Inventory), the instance state items keep
    /// (wear, a store purchase's waiting time) and how it decides stacking, and equipment condition (Durability).
    /// </summary>
    public class InventoryRuleTests : IDisposable
    {
        private const uint Plank = 20, Pick = 21;
        private readonly FakeWorld world = new(c => c.OverflowExpiryHours = 24);
        private static ulong nextGuid = 0x8800;

        public InventoryRuleTests()
        {
            world.Define(new ItemDefinitionManager.ItemDefinition { Id = Plank, MaxStack = 100 });
            world.Define(new ItemDefinitionManager.ItemDefinition { Id = Pick, MaxStack = 1, DurabilitySet = 1 });
        }

        public void Dispose() => world.Dispose();

        private WorldSession Player(params CharacterItemModel[] items) => world.Session(++nextGuid, "Player", items);

        private static CharacterItemModel Overflow(uint definition, uint count, uint slot, DateTime? expires)
        {
            CharacterItemModel item = FakeWorld.Item(definition, count, Inventory.OverflowContainer, slot);
            item.ExpiresAt = expires;
            return item;
        }

        // ---- Overflow expiry ----------------------------------------------------------------------------------------

        [Fact]
        public void AtLogin_ExpiredOverflowGoes_TheRestStaysOrGetsATime()
        {
            CharacterItemModel expired = Overflow(Plank, 5, 0, DateTime.UtcNow.AddMinutes(-1));
            CharacterItemModel waiting = Overflow(Plank, 5, 1, DateTime.UtcNow.AddHours(3));
            CharacterItemModel untimed = Overflow(Plank, 5, 2, null);
            WorldSession s = Player(FakeWorld.Item(Pick), expired, waiting, untimed);
            Assert.Null(s.Inventory.Get(expired.Id));
            Assert.NotNull(s.Inventory.Get(waiting.Id));
            Assert.InRange(untimed.ExpiresAt!.Value, DateTime.UtcNow.AddHours(23.9), DateTime.UtcNow.AddHours(24.1));
        }

        [Fact]
        public void ExpiryOff_KeepsEverything()
        {
            world.Config.OverflowExpiryHours = 0;
            CharacterItemModel expired = Overflow(Plank, 5, 0, DateTime.UtcNow.AddMinutes(-1));
            WorldSession s = Player(FakeWorld.Item(Pick), expired);
            Assert.NotNull(s.Inventory.Get(expired.Id));
            Assert.Empty(s.Inventory.ExpireOverflow());
        }

        [Fact]
        public void FullBag_SpillsIntoOverflow_WithItsTime_ArrivalsWithinAMinuteShareAStack()
        {
            WorldSession s = Player(FakeWorld.Item(Pick));
            uint bag = s.Inventory.BagSize;
            s.Inventory.Add(Pick, bag);                   // fills the bag (one slot taken already: one spills)
            var spilled = s.Inventory.Items.Where(i => i.Container == Inventory.OverflowContainer).ToList();
            Assert.Single(spilled);
            Assert.InRange(spilled[0].ExpiresAt!.Value, DateTime.UtcNow.AddHours(23.9), DateTime.UtcNow.AddHours(24.1));
            s.Inventory.Add(Plank, 10);
            s.Inventory.Add(Plank, 10);
            var planks = s.Inventory.Items.Where(i => i.Container == Inventory.OverflowContainer && i.ItemDefinitionId == Plank).ToList();
            Assert.Equal(20u, Assert.Single(planks).Count);   // the same minute: one stack, one countdown (review 2026-10-18b #14)
        }

        [Fact]
        public void Overflow_OlderStack_IsNotToppedUp()
        {
            // A stack that arrived more than SameArrivalWindow ago keeps its own (earlier) end; the new items get theirs.
            CharacterItemModel older = Overflow(Plank, 5, 0, DateTime.UtcNow.AddHours(24) - Inventory.SameArrivalWindow - TimeSpan.FromSeconds(30));
            WorldSession s = Player(FakeWorld.Item(Pick), older);
            s.Inventory.Add(Pick, s.Inventory.BagSize);
            s.Inventory.Add(Plank, 10);
            var planks = s.Inventory.Items.Where(i => i.Container == Inventory.OverflowContainer && i.ItemDefinitionId == Plank)
                .OrderBy(i => i.Slot).ToList();
            Assert.Equal(2, planks.Count);
            Assert.Equal(5u, planks[0].Count);
            Assert.True(planks[1].ExpiresAt > older.ExpiresAt);
        }

        [Fact]
        public void Overflow_ManyArrivals_DoNotFillTheTab()
        {
            // Looting one item at a time with a full bag: arrivals in the same minute stack instead of taking a slot each.
            WorldSession s = Player(FakeWorld.Item(Pick));
            s.Inventory.Add(Pick, s.Inventory.BagSize);
            for (int i = 0; i < 600; i++)
                s.Inventory.Add(Plank, 1);
            var planks = s.Inventory.Items.Where(i => i.Container == Inventory.OverflowContainer && i.ItemDefinitionId == Plank).ToList();
            Assert.Equal(600u, (uint)planks.Sum(p => p.Count));
            Assert.Equal(6, planks.Count);   // MaxStack 100
        }

        [Fact]
        public void ExpiryOff_OverflowStacksTopUp_AndHaveNoTime()
        {
            world.Config.OverflowExpiryHours = 0;
            WorldSession s = Player(FakeWorld.Item(Pick));
            s.Inventory.Add(Pick, s.Inventory.BagSize);
            s.Inventory.Add(Plank, 10);
            s.Inventory.Add(Plank, 10);
            var planks = s.Inventory.Items.Where(i => i.Container == Inventory.OverflowContainer && i.ItemDefinitionId == Plank).ToList();
            Assert.Equal(20u, Assert.Single(planks).Count);
            Assert.Null(planks[0].ExpiresAt);
        }

        [Fact]
        public void Retrieved_OverflowItem_LosesItsTime()
        {
            CharacterItemModel waiting = Overflow(Plank, 5, 0, DateTime.UtcNow.AddHours(3));
            WorldSession s = Player(FakeWorld.Item(Pick), waiting);
            s.Inventory.AbsorbOverflow();
            Assert.Equal(Inventory.BagContainer, waiting.Container);
            Assert.Null(waiting.ExpiresAt);
        }

        [Fact]
        public void ItemRecord_CountsDownTheOverflowTime()
        {
            CharacterItemModel soon = Overflow(Plank, 5, 0, DateTime.UtcNow.AddSeconds(90));
            CharacterItemModel past = Overflow(Plank, 5, 1, DateTime.UtcNow.AddSeconds(-30));
            CharacterItemModel bag = FakeWorld.Item(Plank, 5, Inventory.BagContainer, 3);
            bag.ExpiresAt = DateTime.UtcNow.AddHours(1);
            Assert.InRange(Inventory.ToPacket(soon).TimedDeleteSeconds, 89u, 91u);
            Assert.Equal(0u, Inventory.ToPacket(past).TimedDeleteSeconds);
            Assert.Equal(0u, Inventory.ToPacket(bag).TimedDeleteSeconds);
        }

        // ---- Instance state and stacking ---------------------------------------------------------------------------

        [Fact]
        public void WornOrWaitingItems_NeverJoinAPlainStack_NorTheReverse()
        {
            CharacterItemModel plain = FakeWorld.Item(Plank, 10, slot: 1);
            WorldSession s = Player(FakeWorld.Item(Pick), plain);
            s.Inventory.Add(Plank, 5, new ItemState(Wear: 3f));
            s.Inventory.Add(Plank, 5, new ItemState(TradeableAfter: DateTime.UtcNow.AddHours(1)));
            Assert.Equal(10u, plain.Count);
            Assert.Equal(3, s.Inventory.Items.Count(i => i.ItemDefinitionId == Plank));
            s.Inventory.Add(Plank, 5);
            Assert.Equal(15u, plain.Count);
        }

        [Fact]
        public void ExpiredWait_IsPlainAgain()
        {
            Assert.True(new ItemState(0f, DateTime.UtcNow.AddSeconds(-1)).IsPlain);
            Assert.False(new ItemState(0f, DateTime.UtcNow.AddMinutes(1)).IsPlain);
            Assert.False(new ItemState(0.5f).IsPlain);
            Assert.True(default(ItemState).IsPlain);
        }

        [Fact]
        public void StateOf_CarriesWearAndWait()
        {
            DateTime after = DateTime.UtcNow.AddHours(2);
            CharacterItemModel item = FakeWorld.Item(Pick, 1, wear: 42f, tradeableAfter: after);
            Assert.Equal(new ItemState(42f, after), Inventory.StateOf(item));
        }

        [Fact]
        public void SplitPart_KeepsTheStacksWaitingTime()
        {
            DateTime after = DateTime.UtcNow.AddHours(2);
            CharacterItemModel stack = FakeWorld.Item(Plank, 10, slot: 1, tradeableAfter: after);
            WorldSession s = Player(FakeWorld.Item(Pick), stack);
            var changed = s.Inventory.MoveCount(stack, Inventory.BagContainer, 5, 4);
            CharacterItemModel part = changed.Select(c => c.Item).Single(i => i != stack);
            Assert.Equal(4u, part.Count);
            Assert.Equal(after, part.TradeableAfter);
        }

        [Fact]
        public void WaitingItemsJoiningAStack_LockItUntilTheLaterTime()
        {
            DateTime after = DateTime.UtcNow.AddHours(2);
            CharacterItemModel target = FakeWorld.Item(Plank, 10, slot: 1);
            CharacterItemModel bought = FakeWorld.Item(Plank, 3, slot: 2, tradeableAfter: after);
            WorldSession s = Player(FakeWorld.Item(Pick), target, bought);
            s.Inventory.MoveCount(bought, Inventory.BagContainer, 1, 3);
            Assert.Equal(13u, target.Count);
            Assert.Equal(after, target.TradeableAfter);
        }

        // ---- Durability ------------------------------------------------------------------------------------------------

        private static void SetDurabilitySets(params (uint Id, float Count, float PerDamage, float PerDeath)[] rows)
        {
            Type set = Private.Nested(typeof(Durability), "Set");
            var map = (IDictionary)Activator.CreateInstance(typeof(System.Collections.Generic.Dictionary<,>).MakeGenericType(typeof(uint), set));
            foreach (var r in rows)
                map[r.Id] = Activator.CreateInstance(set, r.Count, r.PerDamage, r.PerDeath);
            typeof(Durability).GetField("sets", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, map);
        }

        [Theory]
        [InlineData(0f, 100)]
        [InlineData(1f, 99)]        // the client truncates too: (count - wear) / count × 100, FUN_1435585b0
        [InlineData(5000f, 48)]
        [InlineData(9700f, 0)]      // 0.52 % shows as 0 %: broken
        [InlineData(9751f, 0)]
        [InlineData(20000f, 0)]     // clamped
        [InlineData(-5f, 100)]      // clamped
        public void Condition_IsTheClientsPercentage(float wear, int expected)
        {
            SetDurabilitySets((1, 9751f, 0f, 0f));
            try
            {
                Assert.Equal(expected, Durability.Condition(FakeWorld.Item(Pick, 1, wear: wear)));
                Assert.Equal(expected == 0 ? 0.5f : 1f, Durability.Strength(FakeWorld.Item(Pick, 1, wear: wear)));
            }
            finally
            {
                SetDurabilitySets();
            }
        }

        [Fact]
        public void ItemWithoutASet_NeverWears()
        {
            SetDurabilitySets((1, 9751f, 0f, 0f));
            try
            {
                Assert.Equal(100, Durability.Condition(FakeWorld.Item(Plank, 1, wear: 50f)));
            }
            finally
            {
                SetDurabilitySets();
            }
        }

        /// <summary>Damage wears equipped gear by LOSS_PER_HP_DMG; the client hears only when the shown percentage moves.</summary>
        [Fact]
        public void Damage_WearsEquippedGear_AndTellsTheClientOnPercentSteps()
        {
            const uint Armour = 22;
            world.Define(new ItemDefinitionManager.ItemDefinition { Id = Armour, MaxStack = 1, DurabilitySet = 2, EquipSlotId = 8 });
            SetDurabilitySets((2, 9834f, 0.009f, 984f));
            try
            {
                CharacterItemModel chest = FakeWorld.Item(Armour, 1, Inventory.EquippedContainer, 3);
                WorldSession s = Player(FakeWorld.Item(Pick), chest);
                FakeWorld.Outgoing(s);
                Durability.Damaged(s, 100, died: false);           // 0.9 points: 100 % to 99 %, shown
                Assert.Equal(0.9f, chest.Wear, 0.0001f);
                Assert.Equal(99, Durability.Condition(chest));
                Assert.Contains(ZoneMessageOpcode.ClientUpdateItemUpdate, FakeWorld.Outgoing(s).Select(p => p.Opcode));
                Durability.Damaged(s, 100, died: false);           // 1.8 points: still 99 %, nothing sent
                Assert.Equal(99, Durability.Condition(chest));
                Assert.Empty(FakeWorld.Outgoing(s));
                Durability.Damaged(s, 0, died: true);              // a death: 984 points, 10 %
                Assert.Equal(985.8f, chest.Wear, 0.01f);
                Assert.Equal(89, Durability.Condition(chest));
                Assert.NotEmpty(FakeWorld.Outgoing(s));
            }
            finally
            {
                SetDurabilitySets();
            }
        }
    }
}
