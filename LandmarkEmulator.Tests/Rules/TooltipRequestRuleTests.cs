using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using LandmarkEmulator.WorldServer.Network.Message.Model.Shared;
using System;
using System.Collections.Generic;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// Item tooltip requests (Network/Message/Model/TooltipRequest.cs, docs/request-variants.md): 0x0F's body as the
    /// client's writer FUN_14350b970 lays it out {u32 owner type, u64 owner, u64 item, u32, string, u32 mode} for each of
    /// its senders (bag, Inspect window, chest, upgrade station), and 0x10's mail attachment item record.
    /// </summary>
    public class TooltipRequestRuleTests
    {
        [Fact]
        public void TheBagItem_TypeOne_CarriesItsOwnerAndItem()
        {
            byte[] body = TooltipRequest.WriteInstance(1, 0x100, 0xABC);
            Assert.True(TooltipRequest.TryReadInstance(body, out TooltipRequest r));
            Assert.Equal(1u, r.OwnerType);
            Assert.Equal(0x100ul, r.OwnerGuid);
            Assert.Equal(0xABCul, r.ItemGuid);
            // The old location reader took bytes 12-19 as the item guid: right only for type 1.
            Assert.Equal(0xABCul, BitConverter.ToUInt64(body, 12));
        }

        [Fact]
        public void TheChestItem_TypeThree_NamesTheChestAsOwner()
        {
            Assert.True(TooltipRequest.TryReadInstance(TooltipRequest.WriteInstance(3, 0xC4E57, 0xABC), out TooltipRequest r));
            Assert.Equal(TooltipRequest.ChestOwner, r.OwnerType);
            Assert.Equal(0xC4E57ul, r.OwnerGuid);
            Assert.Equal(0xABCul, r.ItemGuid);
        }

        [Fact]
        public void TheUpgradeStation_SendsModeTwo_AndItsDataSource()
        {
            Assert.True(TooltipRequest.TryReadInstance(TooltipRequest.WriteInstance(1, 0x100, 0xABC, "Crafting.Upgrade.Tooltip", 2), out TooltipRequest r));
            Assert.Equal(2u, r.Mode);
            Assert.Equal(0xABCul, r.ItemGuid);
        }

        [Fact]
        public void AShortBody_IsUnreadable()
        {
            Assert.False(TooltipRequest.TryReadInstance(null, out _));
            Assert.False(TooltipRequest.TryReadInstance(new byte[19], out _));
            // Without the trailing fields it still names owner and item.
            Assert.True(TooltipRequest.TryReadInstance(TooltipRequest.WriteInstance(1, 2, 3)[..20], out TooltipRequest r));
            Assert.Equal(3ul, r.ItemGuid);
        }

        [Fact]
        public void TheMailAttachmentRecord_GivesItsDefinitionAndGuid()
        {
            // The record the mail window got (Mail: InventoryItem.WriteRecord) and sends back whole (FUN_14350baa0).
            var item = new InventoryItem { Guid = 0x5EED, Count = 3, OwnerType = 1, SecondsSinceLastCast = int.MaxValue };
            item.Definition.DefinitionId = 1234;
            var bytes = new List<byte>();
            item.WriteRecord(new GamePacketWriter(bytes));
            Assert.True(TooltipRequest.TryReadFull(bytes.ToArray(), out TooltipRequest r));
            Assert.Equal(1234u, r.DefinitionId);
            Assert.Equal(0x5EEDul, r.ItemGuid);
        }

        [Fact]
        public void ACutRecord_StillNamesTheKind_AnEmptyOneNothing()
        {
            Assert.True(TooltipRequest.TryReadFull(BitConverter.GetBytes(1234u), out TooltipRequest r));
            Assert.Equal(1234u, r.DefinitionId);
            Assert.False(TooltipRequest.TryReadFull(new byte[2], out _));
            Assert.False(TooltipRequest.TryReadFull(BitConverter.GetBytes(0u), out _));
        }
    }
}
