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
    /// The off hand (OPEN-LEADS L192 (i), L95; WorldSession.BuildSecondarySlot, CreatureWeapons): a main-hand item's second
    /// model (the shield of a sword-and-shield item, a bow's quiver, the left gun / dagger of a pair) goes to the client in
    /// slot 11 "Off Hand" (SecondaryWeaponSlotName), because the client's in-world slot handler attaches only the model in
    /// the packet [P, FUN_142e42da0]; it follows the main hand on and off. Creatures: CreatureWeapons.Messages (the
    /// org's #15, from a live capture: bow creatures a quiver on outer 2 / attach 11, no shields; CreatureWeaponsTests).
    /// </summary>
    public class OffHandRuleTests : IDisposable
    {
        private readonly FakeWorld world = new();

        public void Dispose() => world.Dispose();

        private const uint Bastion = 990301, PlainSword = 990302;

        private void DefineItems()
        {
            world.Define(new ItemDefinitionManager.ItemDefinition
            {
                Id = Bastion, EquipSlotId = 2, ModelFile = "Wield_Blade1H_Epic_Fire_Fire_000.adr",
                SecondaryModelFile = "Wield_Shield_Epic_Fire_Fire_000.adr", SecondaryTintAlias = "Shield_Test_01"
            });
            world.Define(new ItemDefinitionManager.ItemDefinition { Id = PlainSword, EquipSlotId = 2, ModelFile = "Wield_Test.adr" });
        }

        [Fact]
        public void SecondarySlot_CarriesTheSecondModel_WithNoItemGuid()
        {
            DefineItems();
            CharacterItemModel held = FakeWorld.Item(Bastion, 1, Inventory.EquippedContainer, 2);
            WorldSession player = world.Session(0x7E00_0000_0000_0001, "OffHand1", held);
            var off = player.BuildSecondarySlot(held);
            Assert.NotNull(off);
            Assert.Equal(Inventory.OffHandSlot, off.EquipSlotId);
            Assert.Equal("Wield_Shield_Epic_Fire_Fire_000.adr", off.ModelName);
            Assert.Equal("Shield_Test_01", off.TintAlias);
            // Not the item's guid: the client would re-slot the item record into the off hand (FUN_142ea49e0).
            Assert.Equal(0ul, off.ItemGuid);
        }

        [Fact]
        public void ItemWithoutSecondModel_HasNoOffHand()
        {
            DefineItems();
            CharacterItemModel held = FakeWorld.Item(PlainSword, 1, Inventory.EquippedContainer, 2);
            WorldSession player = world.Session(0x7E00_0000_0000_0002, "OffHand2", held);
            Assert.Null(player.BuildSecondarySlot(held));
            Assert.False(player.EquipmentSlotPackets().Any(p => p.EquipSlotId == Inventory.OffHandSlot));
        }

        [Fact]
        public void EquipmentSlotPackets_PutTheOffHandAfterTheMainHand()
        {
            DefineItems();
            CharacterItemModel held = FakeWorld.Item(Bastion, 1, Inventory.EquippedContainer, 2);
            WorldSession player = world.Session(0x7E00_0000_0000_0003, "OffHand3", held);
            var packets = player.EquipmentSlotPackets();
            int main = packets.FindIndex(p => p.EquipSlotId == Inventory.MainHandSlot);
            int off = packets.FindIndex(p => p.EquipSlotId == Inventory.OffHandSlot);
            Assert.True(main >= 0 && off == main + 1, $"main {main}, off {off}");
        }

        /// <summary>Taking the item in sends the off hand; swapping to an item without one empties it.</summary>
        [Fact]
        public void MainHandChange_SetsAndEmptiesTheOffHand()
        {
            DefineItems();
            CharacterItemModel bastion = FakeWorld.Item(Bastion, 1, Inventory.EquippedContainer, 2);
            CharacterItemModel sword = FakeWorld.Item(PlainSword, 1, Inventory.BagContainer, 0);
            WorldSession player = world.Session(0x7E00_0000_0000_0004, "OffHand4", bastion, sword);
            var none = new Dictionary<uint, CharacterItemModel>();

            // Taken in: the shield goes out with it.
            var set = Assert.IsType<WorldServer.Network.Message.Model.EquipmentSetCharacterEquipmentSlot>(
                player.OffHandChange(none, player.VisibleEquipment()));
            Assert.Equal(Inventory.OffHandSlot, set.EquipSlotId);
            Assert.Equal(0ul, set.ItemGuid);
            // The same item still in hand: nothing.
            Assert.Null(player.OffHandChange(player.VisibleEquipment(), player.VisibleEquipment()));

            // Swapped for a sword without one: the off hand is emptied.
            var before = player.VisibleEquipment();
            bastion.Container = Inventory.BagContainer;
            bastion.Slot = 1;
            sword.Container = Inventory.EquippedContainer;
            sword.Slot = 2;
            var unset = Assert.IsType<WorldServer.Network.Message.Model.EquipmentUnsetCharacterEquipmentSlot>(
                player.OffHandChange(before, player.VisibleEquipment()));
            Assert.Equal(Inventory.OffHandSlot, unset.EquipSlotId);

            // The sword put away: it had no off hand, nothing to empty.
            before = player.VisibleEquipment();
            sword.Container = Inventory.BagContainer;
            Assert.Null(player.OffHandChange(before, player.VisibleEquipment()));
        }
    }
}
