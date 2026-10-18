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
    /// The Lumen store (Game/Store.cs): live's Daybreak Cash prices in Lumens (×1.3, up to whole 5), member discounts,
    /// whole-or-nothing versus partial purchases, and the trade wait on bought items (StoreTradeWaitHours, live's "fraud
    /// timer").
    /// </summary>
    public class StoreRuleTests : IDisposable
    {
        private const uint Priced = 900001, Free = 900002, Stackable = 900003;
        private readonly FakeWorld world = new(c =>
        {
            c.StoreTradeWaitHours = 24;
            c.FreeStore = false;
            c.MembershipPriceLumens = 0;
        });
        private readonly FieldInfo catalogue = typeof(Store).GetField("catalogue", BindingFlags.NonPublic | BindingFlags.Static)!;
        private readonly object previousCatalogue;
        private static ulong nextGuid = 0x7700;

        public StoreRuleTests()
        {
            previousCatalogue = catalogue.GetValue(null);
            catalogue.SetValue(null, new List<Store.Item>
            {
                new(Priced, "Lamp", "", 0, "Decorations", "Lamps", 650, "test"),
                new(Free, "Hat", "", 0, "Outfits", "Hats"),
                new(Stackable, "Fireworks", "", 0, "Decorations", "Fireworks", 100, "test"),
            });
            world.Define(new ItemDefinitionManager.ItemDefinition { Id = Priced, MaxStack = 1 });
            world.Define(new ItemDefinitionManager.ItemDefinition { Id = Free, MaxStack = 1 });
            world.Define(new ItemDefinitionManager.ItemDefinition { Id = Stackable, MaxStack = 10 });
            world.Define(new ItemDefinitionManager.ItemDefinition { Id = Inventory.CurrencyItem, MaxStack = uint.MaxValue });
        }

        public void Dispose()
        {
            catalogue.SetValue(null, previousCatalogue);
            world.Dispose();
        }

        private WorldSession Player(uint lumens)
            => world.Session(++nextGuid, "Buyer", FakeWorld.Item(3253, 1, slot: 0),
                FakeWorld.Item(Inventory.CurrencyItem, lumens, Inventory.CurrencyContainer, 0));

        [Theory]
        [InlineData(499u, 650u)]     // Fantasy Parlor Pack / pets: 648.7 → 650
        [InlineData(799u, 1040u)]    // Build Site Expansion: 1038.7 → 1040
        [InlineData(100u, 130u)]
        [InlineData(0u, 0u)]
        [InlineData(1u, 5u)]
        public void DaybreakCash_ToLumens(uint dbc, uint lumens)
            => Assert.Equal(lumens, Store.LumensFor(dbc));

        [Fact]
        public void PricedPurchase_TakesLumens_AndWaitsToBeTradeable()
        {
            WorldSession s = Player(1000);
            Store.Purchase result = Store.Buy(s, Priced, 1, partial: false);
            Assert.Equal(Store.PurchaseResult.Ok, result.Result);
            Assert.Equal(650L, result.Cost);
            Assert.Equal(350u, s.Coins);
            CharacterItemModel lamp = s.Inventory.Items.Single(i => i.ItemDefinitionId == Priced);
            Assert.True(Inventory.IsTradeLocked(lamp), "a bought item waits StoreTradeWaitHours");
            Assert.InRange(lamp.TradeableAfter!.Value, DateTime.UtcNow.AddHours(23.9), DateTime.UtcNow.AddHours(24.1));
        }

        [Fact]
        public void FreeItem_IsTradeableAtOnce()
        {
            WorldSession s = Player(0);
            Store.Purchase result = Store.Buy(s, Free, 1, partial: false);
            Assert.Equal(Store.PurchaseResult.Ok, result.Result);
            Assert.False(Inventory.IsTradeLocked(s.Inventory.Items.Single(i => i.ItemDefinitionId == Free)));
        }

        [Fact]
        public void TradeWaitZero_MeansTradeableAtOnce()
        {
            world.Config.StoreTradeWaitHours = 0;
            WorldSession s = Player(1000);
            Assert.Equal(Store.PurchaseResult.Ok, Store.Buy(s, Priced, 1, partial: false).Result);
            Assert.False(Inventory.IsTradeLocked(s.Inventory.Items.Single(i => i.ItemDefinitionId == Priced)));
        }

        [Fact]
        public void WholeOrNothing_RefusesWhatItCannotPay()
        {
            WorldSession s = Player(1000);
            Store.Purchase result = Store.Buy(s, Priced, 2, partial: false);
            Assert.Equal(Store.PurchaseResult.CannotAfford, result.Result);
            Assert.Equal(1000u, s.Coins);
            Assert.DoesNotContain(Priced, s.Inventory.Items.Select(i => i.ItemDefinitionId));
        }

        [Fact]
        public void Partial_BuysWhatTheLumensPayFor()
        {
            WorldSession s = Player(1000);
            Store.Purchase result = Store.Buy(s, Priced, 2, partial: true);
            Assert.Equal(Store.PurchaseResult.Ok, result.Result);
            Assert.Equal(1u, result.Count);
            Assert.Equal(350u, s.Coins);
        }

        [Fact]
        public void SecondPurchaseAtOnce_IsTooFast()
        {
            WorldSession s = Player(5000);
            Assert.Equal(Store.PurchaseResult.Ok, Store.Buy(s, Priced, 1, partial: false).Result);
            Assert.Equal(Store.PurchaseResult.TooFast, Store.Buy(s, Priced, 1, partial: false).Result);
        }

        [Fact]
        public void NotInTheStore_IsRefused()
        {
            WorldSession s = Player(5000);
            Assert.Equal(Store.PurchaseResult.NotInStore, Store.Buy(s, 1234567, 1, partial: false).Result);
        }

        /// <summary>
        /// A purchase that waits gets stacks of its own (a trade-locked stack never takes plain items, nor the reverse), so
        /// only free bag slots count as room for it.
        /// </summary>
        [Fact]
        public void WaitingPurchase_DoesNotJoinAPlainStack()
        {
            CharacterItemModel plain = FakeWorld.Item(Stackable, 3, slot: 1);
            WorldSession s = world.Session(++nextGuid, "Buyer", FakeWorld.Item(3253, 1, slot: 0), plain,
                FakeWorld.Item(Inventory.CurrencyItem, 1000, Inventory.CurrencyContainer, 0));
            Assert.Equal(Store.PurchaseResult.Ok, Store.Buy(s, Stackable, 2, partial: false).Result);
            Assert.Equal(3u, plain.Count);
            CharacterItemModel bought = s.Inventory.Items.Single(i => i.ItemDefinitionId == Stackable && i != plain);
            Assert.Equal(2u, bought.Count);
            Assert.True(Inventory.IsTradeLocked(bought));
        }

        /// <summary>Members and trials pay the discounted price, rounded up to whole 5 Lumens, never under 5.</summary>
        [Theory]
        [InlineData(650u, 10u, 2, 585u)]
        [InlineData(650u, 10u, 1, 585u)]     // a trial counts as a member here
        [InlineData(5u, 50u, 2, 5u)]         // never below 5
        [InlineData(999u, 33u, 2, 670u)]     // 669.33 → 670
        [InlineData(650u, 100u, 2, 5u)]      // 100 % off still costs the minimum
        [InlineData(650u, 150u, 2, 5u)]      // over 100 is capped at 100
        [InlineData(0u, 10u, 2, 0u)]         // free stays free
        public void PriceFor_Member(uint price, uint discount, byte memberType, uint expected)
        {
            world.Config.MemberStoreDiscountPercent = discount;
            var account = new LandmarkEmulator.Database.Auth.Model.AccountModel { MemberType = memberType, MemberUntil = DateTime.UtcNow.AddDays(3) };
            WorldSession member = world.Session(++nextGuid, "Member", account, FakeWorld.Item(3253));
            Assert.Equal(expected, Membership.PriceFor(member, price));
        }

        [Fact]
        public void PriceFor_LapsedMember_PaysFullPrice()
        {
            world.Config.MemberStoreDiscountPercent = 10;
            var account = new LandmarkEmulator.Database.Auth.Model.AccountModel { MemberType = 2, MemberUntil = DateTime.UtcNow.AddDays(-1) };
            Assert.Equal(650u, Membership.PriceFor(world.Session(++nextGuid, "Lapsed", account, FakeWorld.Item(3253)), 650));
        }

        [Theory]
        [InlineData(650u, 0u, 650u)]     // no discount (live)
        [InlineData(650u, 10u, 650u)]    // not a member: full price
        public void PriceFor_NonMember_PaysFullPrice(uint price, uint discount, uint expected)
        {
            world.Config.MemberStoreDiscountPercent = discount;
            Assert.Equal(expected, Membership.PriceFor(Player(0), price));
        }
    }
}
