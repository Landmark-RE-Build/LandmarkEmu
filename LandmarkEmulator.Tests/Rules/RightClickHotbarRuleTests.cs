using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game.Inventory;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Handlers;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// Right-click on a bag tool (0.695): Lua ClientInventory.ActivateItem → FUN_143457220 sends only TransferItem
    /// {to equipped / any slot, from bag / any slot, guid, -1, no swap} (FUN_142f24db0). The server equips the tool and,
    /// as the 0.589 client did itself (FUN_1433a0b80), puts it on the first free action slot unless a slot already holds
    /// it; the slot is saved and sent (ActionManagerUpdateSlot). The hotbar's own tool use (main-hand slot, swap) and a
    /// drag onto the bar (ActionManager sub 2) keep working as before.
    /// </summary>
    public class RightClickHotbarRuleTests : IDisposable
    {
        private const uint Pick = 41, Axe = 42, Potion = 43, Starter = 44, Flare = 45;
        private const uint MainHand = 2;
        private readonly FakeWorld world = new();
        private static ulong nextGuid = 0x7400_0000;

        public RightClickHotbarRuleTests()
        {
            // The main hand's equipped slot (InventoryDefinition.txt is not loaded in tests): 2 at 2, as FakeWorld's.
            var field = typeof(Inventory)
                .GetField("equipSlotToContainerSlot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            if (field.GetValue(null) == null)
                field.SetValue(null, new Dictionary<uint, uint> { [MainHand] = 2 });
            world.Define(new ItemDefinitionManager.ItemDefinition { Id = Pick, EquipSlotId = MainHand, MaxStack = 1 });
            world.Define(new ItemDefinitionManager.ItemDefinition { Id = Axe, EquipSlotId = MainHand, MaxStack = 1 });
            world.Define(new ItemDefinitionManager.ItemDefinition { Id = Potion, MaxStack = 20 });
            world.Define(new ItemDefinitionManager.ItemDefinition { Id = Starter, EquipSlotId = MainHand, MaxStack = 1 });
            // A held item that stacks (no such 0.695 main-hand item is known; the rule is the client's).
            world.Define(new ItemDefinitionManager.ItemDefinition { Id = Flare, EquipSlotId = MainHand, MaxStack = 10 });
        }

        public void Dispose() => world.Dispose();

        /// <summary>A player whose bars are as SendSelfToClient sent them: every slot empty but the ones given.</summary>
        private WorldSession Player(CharacterItemModel[] items, params (uint Bar, uint Slot, uint Type, ulong Value)[] bars)
        {
            WorldSession s = world.Session(++nextGuid, "Clicker", items);
            var slots = new List<((uint, uint), (uint, ulong))>();
            for (uint bar = 0; bar < 2; bar++)
                for (uint slot = 0; slot < 10; slot++)
                {
                    var set = bars.FirstOrDefault(b => b.Bar == bar && b.Slot == slot);
                    slots.Add(((bar, slot), bars.Any(b => b.Bar == bar && b.Slot == slot) ? (set.Type, set.Value) : (0u, 0ul)));
                }
            s.SetActionSlotsSent(slots);
            FakeWorld.Outgoing(s);
            return s;
        }

        /// <summary>TransferItem as the client writes it: u16 sub 0x16, destination, source (owner 1: no owner guid), guid, quantity, swap.</summary>
        private static InventoryRequest Transfer(uint toContainer, uint toSlot, uint fromContainer, uint fromSlot, ulong guid, bool swap)
        {
            var bytes = new List<byte>(BitConverter.GetBytes(InventoryRequest.RequestTransferItem));
            foreach (uint v in new[] { 1u, toContainer, toSlot, 1u, fromContainer, fromSlot })
                bytes.AddRange(BitConverter.GetBytes(v));
            bytes.AddRange(BitConverter.GetBytes(guid));
            bytes.AddRange(BitConverter.GetBytes(uint.MaxValue));
            bytes.Add((byte)(swap ? 1 : 0));
            var request = new InventoryRequest();
            request.Read(new GamePacketReader(bytes.ToArray()));
            return request;
        }

        /// <summary>The right-click form (FUN_142f24db0 with "in bag" and slot -1).</summary>
        private static InventoryRequest RightClick(ulong guid)
            => Transfer(Inventory.EquippedContainer, uint.MaxValue, Inventory.BagContainer, uint.MaxValue, guid, false);

        private static List<byte[]> SlotUpdates(WorldSession s)
            => FakeWorld.Outgoing(s).Where(p => p.Opcode == ZoneMessageOpcode.ActionManagerUpdateSlotWire).Select(p => p.Body).ToList();

        private static byte[] SlotUpdate(uint bar, uint slot, uint type, ulong value)
            => Wire.Write(new ActionManagerUpdateSlot { BarId = bar, SlotId = slot, Type = type, Value = value });

        [Fact]
        public void TheRightClickForm_IsRecognised_AndNothingElse()
        {
            Assert.True(InventoryHandler.IsRightClickEquip(TransferItemRequest.Read(RightClick(1).Body)));
            // The hotbar's tool use / an armour right-click: main-hand slot named, swap.
            Assert.False(InventoryHandler.IsRightClickEquip(TransferItemRequest.Read(
                Transfer(Inventory.EquippedContainer, 2, Inventory.BagContainer, uint.MaxValue, 1, true).Body)));
            // The unequip half of the same toggle (the item already worn): to the bag.
            Assert.False(InventoryHandler.IsRightClickEquip(TransferItemRequest.Read(
                Transfer(Inventory.BagContainer, uint.MaxValue, Inventory.EquippedContainer, uint.MaxValue, 1, false).Body)));
            // A drag from bag slot 3 onto the main hand.
            Assert.False(InventoryHandler.IsRightClickEquip(TransferItemRequest.Read(
                Transfer(Inventory.EquippedContainer, 2, Inventory.BagContainer, 3, 1, true).Body)));
        }

        [Fact]
        public void RightClickedTool_IsWielded_AndGoesOnTheFirstFreeSlot()
        {
            CharacterItemModel pick = FakeWorld.Item(Pick, slot: 4);
            WorldSession s = Player(new[] { pick, FakeWorld.Item(Potion, 5, slot: 5) },
                (0, 0, WorldSession.ActionSlotItemDefinition, Potion));

            InventoryHandler.HandleInventory(s, RightClick(pick.Id));

            Assert.Equal(Inventory.EquippedContainer, pick.Container);
            Assert.Equal(2u, pick.Slot);
            Assert.Equal((WorldSession.ActionSlotItem, pick.Id), s.ActionSlot(0, 1));
            Assert.Equal(Convert.ToHexString(SlotUpdate(0, 1, WorldSession.ActionSlotItem, pick.Id)), Convert.ToHexString(Assert.Single(SlotUpdates(s))));
        }

        [Fact]
        public void RightClick_WhileHoldingAnotherTool_SwapsIt_AndAddsOnlyTheNewOne()
        {
            CharacterItemModel axe = FakeWorld.Item(Axe, container: Inventory.EquippedContainer, slot: 2);
            CharacterItemModel pick = FakeWorld.Item(Pick, slot: 0);
            WorldSession s = Player(new[] { axe, pick }, (0, 0, WorldSession.ActionSlotItem, axe.Id));

            InventoryHandler.HandleInventory(s, RightClick(pick.Id));

            Assert.Equal(Inventory.EquippedContainer, pick.Container);
            Assert.Equal(Inventory.BagContainer, axe.Container);
            Assert.Equal((WorldSession.ActionSlotItem, axe.Id), s.ActionSlot(0, 0));   // the axe keeps its slot
            Assert.Equal((WorldSession.ActionSlotItem, pick.Id), s.ActionSlot(0, 1));
        }

        [Fact]
        public void ToolAlreadyOnABar_ByGuidOrByItsDefaultDefinitionSlot_IsNotAddedTwice()
        {
            CharacterItemModel pick = FakeWorld.Item(Pick);
            WorldSession s = Player(new[] { pick }, (1, 6, WorldSession.ActionSlotItem, pick.Id));
            InventoryHandler.HandleInventory(s, RightClick(pick.Id));
            Assert.Equal(Inventory.EquippedContainer, pick.Container);
            Assert.Empty(SlotUpdates(s));

            // ActionSlotDefinitions' bar 0 defaults are type 1 (item definition): the client binds them to the item.
            CharacterItemModel starter = FakeWorld.Item(Starter);
            WorldSession t = Player(new[] { starter }, (0, 1, WorldSession.ActionSlotItemDefinition, Starter));
            InventoryHandler.HandleInventory(t, RightClick(starter.Id));
            Assert.Equal(Inventory.EquippedContainer, starter.Container);
            Assert.Empty(SlotUpdates(t));
            Assert.Equal((0u, 0ul), t.ActionSlot(0, 0));
        }

        [Fact]
        public void FullBars_StillWield_ButAddNothing()
        {
            CharacterItemModel pick = FakeWorld.Item(Pick);
            var full = Enumerable.Range(0, 20).Select(i => ((uint)(i / 10), (uint)(i % 10), WorldSession.ActionSlotItemDefinition, (ulong)Potion)).ToArray();
            WorldSession s = Player(new[] { pick, FakeWorld.Item(Potion, 1, slot: 1) }, full);
            InventoryHandler.HandleInventory(s, RightClick(pick.Id));
            Assert.Equal(Inventory.EquippedContainer, pick.Container);
            Assert.Empty(SlotUpdates(s));
        }

        [Fact]
        public void HotbarToolUse_AndARefusedEquip_AddNoSlot()
        {
            // The hotbar's tool use names the main-hand slot and swaps (FUN_14345afe0): the tool is wielded, no slot added.
            CharacterItemModel pick = FakeWorld.Item(Pick);
            WorldSession s = Player(new[] { pick });
            InventoryHandler.HandleInventory(s, Transfer(Inventory.EquippedContainer, 2, Inventory.BagContainer, uint.MaxValue, pick.Id, true));
            Assert.Equal(Inventory.EquippedContainer, pick.Container);
            Assert.Empty(SlotUpdates(s));

            // Something that cannot be worn: nothing moves, nothing goes on the bar.
            CharacterItemModel potion = FakeWorld.Item(Potion, 3);
            WorldSession t = Player(new[] { potion });
            InventoryHandler.HandleInventory(t, RightClick(potion.Id));
            Assert.Equal(Inventory.BagContainer, potion.Container);
            Assert.Empty(SlotUpdates(t));
        }

        [Fact]
        public void DragOntoTheBar_StillSetsTheSlot_AndAResetPutsOurCopyBack()
        {
            CharacterItemModel pick = FakeWorld.Item(Pick);
            WorldSession s = Player(new[] { pick });
            // ActionManager sub 2 {u32 bar, u32 slot, u32 type, u64 value} (Lua Ui.SetSlot).
            var bytes = new List<byte> { ActionManagerRequest.SetSlot };
            foreach (uint v in new[] { 1u, 3u, WorldSession.ActionSlotItem })
                bytes.AddRange(BitConverter.GetBytes(v));
            bytes.AddRange(BitConverter.GetBytes(pick.Id));
            var request = new ActionManagerRequest();
            request.Read(new GamePacketReader(bytes.ToArray()));
            ActionBarHandler.HandleActionManager(s, request);
            Assert.Equal(Convert.ToHexString(SlotUpdate(1, 3, WorldSession.ActionSlotItem, pick.Id)), Convert.ToHexString(Assert.Single(SlotUpdates(s))));
            Assert.Equal((WorldSession.ActionSlotItem, pick.Id), s.ActionSlot(1, 3));

            // Now on a bar: the right-click only wields it.
            InventoryHandler.HandleInventory(s, RightClick(pick.Id));
            Assert.Empty(SlotUpdates(s));

            // DefaultSlot (sub 3): the client shows the default again (none in tests: empty), so does our copy.
            s.ForgetActionSlots(1, 3);
            Assert.Equal((0u, 0ul), s.ActionSlot(1, 3));
        }

        [Fact]
        public void DefinitionSlotOfAStackingItem_DoesNotCount()
        {
            // FUN_14344daa0 shows a type-1 slot as the player's item only when the definition's stack size (+0xe4) is 1;
            // the placing routine (0.589 FUN_1433a0b80) then compares guids only, so a stacking item still gets its slot.
            CharacterItemModel flare = FakeWorld.Item(Flare);
            WorldSession s = Player(new[] { flare }, (0, 1, WorldSession.ActionSlotItemDefinition, Flare));
            InventoryHandler.HandleInventory(s, RightClick(flare.Id));
            Assert.Equal(Inventory.EquippedContainer, flare.Container);
            Assert.Equal((WorldSession.ActionSlotItem, flare.Id), s.ActionSlot(0, 0));
        }
    }
}
