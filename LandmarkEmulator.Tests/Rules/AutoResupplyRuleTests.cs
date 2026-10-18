using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game.Inventory;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Handlers;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using LandmarkEmulator.WorldServer.Network.Message.Model.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// The item auto-resupply switch: Inventory 0x2D sub 0x0D InventoryPacketToggleAutoResupply {u64 item guid, bool on}
    /// (client sender FUN_142f261b0, writer FUN_142f18aa0) is kept per item (setting ItemAutoResupply, default on = live,
    /// whose item table had an auto_resupply column) and sent back in the item record (ClientItem +0xdc). The client's
    /// definition check (FUN_142f1d360) is applied on the server too; no answer is sent.
    /// </summary>
    public class AutoResupplyRuleTests : IDisposable
    {
        private const uint Potion = 30, Cap = 31;
        private readonly FakeWorld world = new(c => c.ItemAutoResupply = true);
        private static ulong nextGuid = 0x9900;

        public AutoResupplyRuleTests()
        {
            FakeWorld.EnsureMessageTable();
            world.Define(new ItemDefinitionManager.ItemDefinition { Id = Potion, MaxStack = 20, AutoResupplyAllowed = true });
            world.Define(new ItemDefinitionManager.ItemDefinition { Id = Cap, MaxStack = 1 });
        }

        public void Dispose() => world.Dispose();

        private WorldSession Player(params CharacterItemModel[] items) => world.Session(++nextGuid, "Player", items);

        /// <summary>The request as the client writes it: u16 sub 0x0D, u64 item guid, u8 on (after the u16 family).</summary>
        private static InventoryRequest Toggle(ulong guid, bool on)
        {
            var bytes = new List<byte>(BitConverter.GetBytes(InventoryRequest.ToggleAutoResupply));
            bytes.AddRange(BitConverter.GetBytes(guid));
            bytes.Add((byte)(on ? 1 : 0));
            var request = new InventoryRequest();
            request.Read(new GamePacketReader(bytes.ToArray()));
            return request;
        }

        [Fact]
        public void Toggle_IsKept_SentInTheItemRecord_AndNotAnswered()
        {
            CharacterItemModel potions = FakeWorld.Item(Potion, 5);
            WorldSession s = Player(potions);
            FakeWorld.Outgoing(s);

            InventoryHandler.HandleInventory(s, Toggle(potions.Id, true));
            Assert.True(potions.AutoResupply);
            Assert.Empty(FakeWorld.Outgoing(s));   // the client set its own copy first and reads no reply

            // The record byte after the u32 at +0xd8 (ClientItem +0xdc) is the switch.
            byte[] record = Wire.Write(Inventory.ToPacket(potions));
            var back = new InventoryItem();
            back.Read(new GamePacketReader(record));
            Assert.True(back.AutoResupply);

            InventoryHandler.HandleInventory(s, Toggle(potions.Id, false));
            Assert.False(potions.AutoResupply);
            Assert.False(Inventory.ToPacket(potions).AutoResupply);
        }

        [Fact]
        public void Toggle_ForADefinitionTheClientWouldRefuse_OrAnotherPlayersItem_IsIgnored()
        {
            CharacterItemModel cap = FakeWorld.Item(Cap);
            WorldSession s = Player(cap, FakeWorld.Item(Potion, 2, slot: 1));
            InventoryHandler.HandleInventory(s, Toggle(cap.Id, true));
            Assert.False(cap.AutoResupply);

            CharacterItemModel theirs = FakeWorld.Item(Potion, 3);
            Player(theirs);
            InventoryHandler.HandleInventory(s, Toggle(theirs.Id, true));
            Assert.False(theirs.AutoResupply);
        }

        [Fact]
        public void SettingOff_IgnoresTheToggle_AndSendsOff()
        {
            CharacterItemModel potions = FakeWorld.Item(Potion, 5);
            potions.AutoResupply = true;   // saved while the setting was on
            WorldSession s = Player(potions);
            world.Config.ItemAutoResupply = false;
            Assert.False(Inventory.ToPacket(potions).AutoResupply);
            InventoryHandler.HandleInventory(s, Toggle(potions.Id, false));
            Assert.True(potions.AutoResupply);   // untouched, comes back when the setting is on again
        }

        [Fact]
        public void SplitPart_KeepsTheSwitch_ButTheSwitchNeverBlocksStacking()
        {
            CharacterItemModel potions = FakeWorld.Item(Potion, 10);
            WorldSession s = Player(potions);
            Assert.True(s.Inventory.SetAutoResupply(potions, true));
            Assert.False(s.Inventory.SetAutoResupply(potions, true));   // no change, nothing saved

            s.Inventory.MoveCount(potions, Inventory.BagContainer, 5, 4);
            CharacterItemModel part = s.Inventory.Items.Single(i => i.ItemDefinitionId == Potion && i.Id != potions.Id);
            Assert.True(part.AutoResupply);
            Assert.Equal(6u, potions.Count);

            // The client's stack test ignores +0xdc: plain potions still top up the switched-on stack.
            s.Inventory.Add(Potion, 3);
            Assert.Equal(13L, s.Inventory.Items.Where(i => i.ItemDefinitionId == Potion).Sum(i => (long)i.Count));
            Assert.Equal(2, s.Inventory.Items.Count(i => i.ItemDefinitionId == Potion));
            Assert.True(new ItemState(AutoResupply: true).IsPlain);
        }

        /// <summary>
        /// The live definitions (Resources/PacketDumps/44-CommandBase, 2,885 items) against the client's check
        /// FUN_142f1d360: none passes, so a stock client never sends the toggle (the live SendSelf capture carries no
        /// definition-variable overrides that could change +0xfc / the stack size). Only 5 Cloth Cap has +0x28 / +0xfc /
        /// +0x100 set, and it stacks to 1.
        /// </summary>
        [Fact]
        public void NoLiveDefinition_PassesTheClientsCheck()
        {
            byte[] data = System.IO.File.ReadAllBytes(System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "PacketDumps", "44-CommandBase"));
            uint count = BitConverter.ToUInt32(data, 8);
            int offset = 12, allowed = 0;
            for (uint i = 0; i < count; i++)
            {
                ushort compressed = BitConverter.ToUInt16(data, offset), size = BitConverter.ToUInt16(data, offset + 2);
                byte[] record = LandmarkEmulator.Shared.Network.Lz4Block.Decode(data.AsSpan(offset + 4, compressed), size);
                offset += 4 + compressed;
                if (Private.Call<ItemDefinitionManager.ItemDefinition>(typeof(ItemDefinitionManager), "Parse", record).AutoResupplyAllowed)
                    allowed++;
            }
            Assert.Equal(2885u, count);
            Assert.Equal(0, allowed);
        }

        [Fact]
        public void HandedOverItem_BringsItsSwitch()
        {
            WorldSession s = Player(FakeWorld.Item(Cap));
            var added = s.Inventory.Add(Potion, 2, new ItemState(Wear: 1f, AutoResupply: true));
            Assert.True(Assert.Single(added).Item.AutoResupply);
            Assert.True(Inventory.StateOf(added[0].Item).AutoResupply);
        }
    }
}
