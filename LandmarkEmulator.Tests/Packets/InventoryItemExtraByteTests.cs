using System.Collections.Generic;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model.Shared;
using Xunit;

namespace LandmarkEmulator.Tests.Packets
{
    /// <summary>
    /// The byte after each SendSelf inventory record is read only for player-owned items in containers 0-11
    /// (FUN_142c98950 → FUN_142c987c0), so it is written only for those (trace/0695/phaseE3-wire-a.md F1).
    /// </summary>
    public class InventoryItemExtraByteTests
    {
        private static int Length(uint owner, uint container)
        {
            var bytes = new List<byte>();
            new InventoryItem { OwnerType = owner, ContainerId = container }.Write(new GamePacketWriter(bytes));
            return bytes.Count;
        }

        [Fact]
        public void ExtraByte_OnlyForPlayerItemsBelowContainer12()
        {
            int player = Length(1, 0);
            Assert.Equal(player, Length(1, 11));
            Assert.Equal(player - 1, Length(1, 12));
            Assert.Equal(player - 1, Length(2, 0));
            Assert.True(new InventoryItem { OwnerType = 1, ContainerId = 8 }.HasExtra);
        }
    }
}
