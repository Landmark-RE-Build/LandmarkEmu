using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Game.Inventory;
using LandmarkEmulator.WorldServer.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// Player trades (Game/Trades.cs) at completion: each item keeps its instance state (wear) on the way, and an offer
    /// that is no longer tradeable when both accept fails the trade.
    /// </summary>
    public class TradeRuleTests : IDisposable
    {
        private const uint Sword = 31, Plank = 32;
        private readonly FakeWorld world = new();
        private static ulong nextGuid = 0xA000;
        private static readonly Type TradeType = Private.Nested(typeof(Trades), "Trade");
        private static readonly Type SideType = Private.Nested(typeof(Trades), "Side");

        public TradeRuleTests()
        {
            world.Define(new ItemDefinitionManager.ItemDefinition { Id = Sword, MaxStack = 1 });
            world.Define(new ItemDefinitionManager.ItemDefinition { Id = Plank, MaxStack = 100 });
        }

        public void Dispose() => world.Dispose();

        private static object Side(WorldSession session, params (ulong Guid, uint Count)[] offer)
        {
            object side = Activator.CreateInstance(SideType, true);
            SideType.GetField("Session")!.SetValue(side, session);
            var items = (Dictionary<ulong, uint>)SideType.GetField("Items")!.GetValue(side);
            foreach (var (guid, count) in offer)
                items[guid] = count;
            return side;
        }

        private static void Complete(object a, object b)
        {
            object trade = Activator.CreateInstance(TradeType, true);
            TradeType.GetField("Id")!.SetValue(trade, 9999);
            TradeType.GetField("A")!.SetValue(trade, a);
            TradeType.GetField("B")!.SetValue(trade, b);
            Private.Call(typeof(Trades), "Complete", trade);
        }

        [Fact]
        public void TradedItem_KeepsItsWear()
        {
            CharacterItemModel sword = FakeWorld.Item(Sword, 1, slot: 1, wear: 250f);
            WorldSession giver = world.Session(++nextGuid, "Giver", FakeWorld.Item(3253), sword);
            WorldSession taker = world.Session(++nextGuid, "Taker", FakeWorld.Item(3253));
            Complete(Side(giver, (sword.Id, 1)), Side(taker));
            Assert.Null(giver.Inventory.Get(sword.Id));
            CharacterItemModel arrived = taker.Inventory.Items.Single(i => i.ItemDefinitionId == Sword);
            Assert.Equal(250f, arrived.Wear);
        }

        /// <summary>
        /// The offer is checked again when both accept (it may be gone, or the bag full). A stack that became trade-locked
        /// after it was offered (a store purchase still in its waiting time dropped onto it: Inventory.MoveCount locks the
        /// stack it joins) must not change hands then, as it may not be offered (Trades.Offer).
        /// </summary>
        [Fact]
        public void OfferThatBecameTradeLocked_FailsTheTrade()
        {
            CharacterItemModel planks = FakeWorld.Item(Plank, 10, slot: 1);
            CharacterItemModel bought = FakeWorld.Item(Plank, 5, slot: 2, tradeableAfter: DateTime.UtcNow.AddHours(20));
            WorldSession giver = world.Session(++nextGuid, "Giver", FakeWorld.Item(3253), planks, bought);
            WorldSession taker = world.Session(++nextGuid, "Taker", FakeWorld.Item(3253));
            object offer = Side(giver, (planks.Id, 10));
            giver.Inventory.MoveCount(bought, Inventory.BagContainer, planks.Slot, 5);   // after the offer
            Assert.True(Inventory.IsTradeLocked(planks));
            Complete(offer, Side(taker));
            Assert.Equal(15u, giver.Inventory.Get(planks.Id)?.Count);
            Assert.DoesNotContain(Plank, taker.Inventory.Items.Select(i => i.ItemDefinitionId));
        }

        [Fact]
        public void OfferThatIsGone_FailsTheTrade()
        {
            CharacterItemModel planks = FakeWorld.Item(Plank, 10, slot: 1);
            WorldSession giver = world.Session(++nextGuid, "Giver", FakeWorld.Item(3253), planks);
            WorldSession taker = world.Session(++nextGuid, "Taker", FakeWorld.Item(3253));
            object offer = Side(giver, (planks.Id, 12));
            Complete(offer, Side(taker));
            Assert.Equal(10u, planks.Count);
            Assert.DoesNotContain(Plank, taker.Inventory.Items.Select(i => i.ItemDefinitionId));
        }
    }
}
