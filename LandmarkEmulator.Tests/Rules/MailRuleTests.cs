using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Game.Inventory;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// Mail attachments (Game/Mail.cs): the per-attachment stack cap (MailMaxStack, 9,000 as live, wiki Trading), no-trade
    /// and trade-locked items, Player Design items, the same stack attached twice, and the wear an item keeps on the way.
    /// </summary>
    public class MailRuleTests : IDisposable
    {
        private const uint Ore = 10, Sword = 11, Soulbound = 12;
        private readonly FakeWorld world = new(c => c.MailMaxStack = 9000);

        public MailRuleTests()
        {
            world.Define(new ItemDefinitionManager.ItemDefinition { Id = Ore });
            world.Define(new ItemDefinitionManager.ItemDefinition { Id = Sword, EquipSlotId = 2 });
            world.Define(new ItemDefinitionManager.ItemDefinition { Id = Soulbound, NoTrade = true });
            world.Define(new ItemDefinitionManager.ItemDefinition { Id = DesignItems.PlayerDesignItem });
        }

        public void Dispose() => world.Dispose();

        private static uint Check(WorldSession s, params (ulong Guid, uint Count)[] attached)
        {
            var draft = new Mail.ComposeDraft();
            draft.Attached.AddRange(attached);
            return Mail.CheckBag(s, draft, out _);
        }

        [Fact]
        public void StackAtTheCap_Goes_OverItIsRefused()
        {
            CharacterItemModel ore = FakeWorld.Item(Ore, 20000);
            WorldSession s = world.Session(0x100, "Sender", ore);
            Assert.Equal(Mail.Success, Check(s, (ore.Id, 9000)));
            Assert.Equal(Mail.AttachmentInvalid, Check(s, (ore.Id, 9001)));
        }

        [Fact]
        public void CapZero_MeansNoCap()
        {
            world.Config.MailMaxStack = 0;
            CharacterItemModel ore = FakeWorld.Item(Ore, 20000);
            WorldSession s = world.Session(0x101, "Sender", ore);
            Assert.Equal(Mail.Success, Check(s, (ore.Id, 20000)));
        }

        [Fact]
        public void SameStackTwice_MayNotExceedIt()
        {
            CharacterItemModel ore = FakeWorld.Item(Ore, 100);
            WorldSession s = world.Session(0x102, "Sender", ore);
            Assert.Equal(Mail.Success, Check(s, (ore.Id, 50), (ore.Id, 50)));
            Assert.Equal(Mail.ItemGone, Check(s, (ore.Id, 60), (ore.Id, 50)));
        }

        [Fact]
        public void NoTradeAndTradeLocked_AreRefused()
        {
            CharacterItemModel bound = FakeWorld.Item(Soulbound, 1, slot: 0);
            CharacterItemModel bought = FakeWorld.Item(Ore, 1, slot: 1, tradeableAfter: DateTime.UtcNow.AddHours(5));
            CharacterItemModel waited = FakeWorld.Item(Ore, 1, slot: 2, tradeableAfter: DateTime.UtcNow.AddHours(-1));
            WorldSession s = world.Session(0x103, "Sender", bound, bought, waited);
            Assert.Equal(Mail.NoTradeItem, Check(s, (bound.Id, 1)));
            Assert.Equal(Mail.NoTradeItem, Check(s, (bought.Id, 1)));
            Assert.Equal(Mail.Success, Check(s, (waited.Id, 1)));
        }

        [Fact]
        public void EquippedOrMissingItems_AreGone()
        {
            CharacterItemModel worn = FakeWorld.Item(Sword, 1, Inventory.EquippedContainer, 2);
            WorldSession s = world.Session(0x104, "Sender", FakeWorld.Item(Ore, 1), worn);
            Assert.Equal(Mail.ItemGone, Check(s, (worn.Id, 1)));
            Assert.Equal(Mail.ItemGone, Check(s, (0xDEAD, 1)));
        }

        /// <summary>
        /// A Player Design item carries its design by guid; mail keeps only kinds and counts, so the attach request refuses
        /// it (Mail.Attach). The compose check is the one that decides what leaves the bag and must refuse it too: a
        /// compose naming the item's guid would otherwise mail a design item with no design.
        /// </summary>
        [Fact]
        public void PlayerDesignItem_IsRefusedAtCompose()
        {
            CharacterItemModel design = FakeWorld.Item(DesignItems.PlayerDesignItem, 1);
            WorldSession s = world.Session(0x105, "Sender", design);
            Assert.Equal(Mail.AttachmentInvalid, Check(s, (design.Id, 1)));
        }

        [Fact]
        public void WornItem_KeepsItsWearInTheDraft()
        {
            CharacterItemModel sword = FakeWorld.Item(Sword, 1, wear: 123.5f);
            WorldSession s = world.Session(0x106, "Sender", sword);
            var draft = new Mail.ComposeDraft();
            draft.Attached.Add((sword.Id, 1));
            Assert.Equal(Mail.Success, Mail.CheckBag(s, draft, out var items));
            Assert.Equal(123.5f, Assert.Single(draft.Items).Wear);
            Assert.Equal((Sword, 1u), Assert.Single(items));
        }

        /// <summary>The stored attachment text keeps the wear, in any culture (invariant float).</summary>
        [Fact]
        public void StoredAttachments_RoundTripWear_InAnyCulture()
        {
            Type stored = Private.Nested(typeof(Mail), "Stored");
            var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(stored));
            list.Add(Activator.CreateInstance(stored, 11u, 1u, 1234.5f));
            list.Add(Activator.CreateInstance(stored, 10u, 9000u, 0f));
            var before = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                MethodInfoCall(out string text, list, stored);
                Assert.Equal("11:1:1234.5;10:9000", text);
                var parsed = (System.Collections.IList)Private.Call(typeof(Mail), "ParseAttachments", text);
                Assert.Equal(2, parsed.Count);
                Assert.Equal(1234.5f, (float)stored.GetProperty("Wear")!.GetValue(parsed[0]));
                Assert.Equal(9000u, (uint)stored.GetProperty("Count")!.GetValue(parsed[1]));
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = before;
            }
        }

        private static void MethodInfoCall(out string text, System.Collections.IList list, Type stored)
        {
            var method = typeof(Mail).GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .Single(m => m.Name == "FormatAttachments" && m.GetParameters()[0].ParameterType
                    == typeof(IEnumerable<>).MakeGenericType(stored));
            text = (string)method.Invoke(null, new object[] { list });
        }

        /// <summary>AttachItemToComposeRequest (sub 0x15): over the cap and a Player Design item are answered "invalid".</summary>
        [Theory]
        [InlineData(9000u, 0u)]
        [InlineData(9001u, 5u)]
        public void AttachRequest_AppliesTheCap(uint count, uint expected)
        {
            CharacterItemModel ore = FakeWorld.Item(Ore, 20000, slot: 3);
            WorldSession s = world.Session(0x107, "Sender", ore);
            byte[] body = new Layout().U32(1).U64(0).U32(3).U32(count).U32(Inventory.BagContainer).Bool(false).ToArray();
            Mail.Attach(s, body);
            (ZoneMessageOpcode opcode, byte[] reply) = Assert.Single(FakeWorld.Outgoing(s));
            Assert.Equal(ZoneMessageOpcode.MailBase, opcode);
            // u32 sub 0x16 AttachItemToComposeResponse, u32 result, then the request echoed.
            Assert.Equal(0x16u, BitConverter.ToUInt32(reply, 0));
            Assert.Equal(expected, BitConverter.ToUInt32(reply, 4));
            Assert.Equal(body, reply[8..]);
        }
    }
}
