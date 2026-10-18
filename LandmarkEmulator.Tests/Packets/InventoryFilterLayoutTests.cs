using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using Xunit;

namespace LandmarkEmulator.Tests.Packets
{
    /// <summary>
    /// Wire layouts of the bag filter packets, built field by field from the 0.695 client's code:
    /// PlayerFilterUpdatePacket (0xDD sub 4) from its writer FUN_142fc3d20, and SendSelfToClient's InventoryFilterManager
    /// from its reader FUN_142c3d470 (docs/protocol.md §14 "Bag filters").
    /// </summary>
    public class InventoryFilterLayoutTests
    {
        /// <summary>FUN_142fc3d20: u32 +0x18 FilterId, string +0x20 FilterName, string +0x38 FilterString, u32 +0x50 Op.</summary>
        [Fact]
        public void PlayerFilterUpdate_IdNameStringOp()
        {
            byte[] expected = new Layout().U32(5).Str("Ores").Str("3,4").U32(PlayerFilterUpdate.AddOrUpdate).ToArray();
            Assert.Equal(expected, Wire.Write(new PlayerFilterUpdate { FilterId = 5, FilterName = "Ores", FilterString = "3,4" }));

            Assert.True(PlayerFilterUpdate.TryParse(expected, out PlayerFilterUpdate read));
            Assert.Equal(5u, read.FilterId);
            Assert.Equal("Ores", read.FilterName);
            Assert.Equal("3,4", read.FilterString);
            Assert.Equal(PlayerFilterUpdate.AddOrUpdate, read.Op);
            Assert.Equal(4, read.FilterNameBytes);
            Assert.Equal(3, read.FilterStringBytes);
        }

        /// <summary>ResetFilters (FUN_142efd4f0) sends op 2 with id 0 and two empty strings [I: DAT_144c40a60 = ""].</summary>
        [Fact]
        public void PlayerFilterUpdate_Reset()
        {
            byte[] reset = new Layout().U32(0).U32(0).U32(0).U32(PlayerFilterUpdate.Reset).ToArray();
            Assert.True(PlayerFilterUpdate.TryParse(reset, out PlayerFilterUpdate read));
            Assert.Equal(PlayerFilterUpdate.Reset, read.Op);
            Assert.Equal("", read.FilterName);
        }

        [Fact]
        public void PlayerFilterUpdate_ShortLongOrTrailingBodies_AreUnreadable()
        {
            byte[] good = new Layout().U32(1).Str("A").Str("1").U32(0).ToArray();
            Assert.False(PlayerFilterUpdate.TryParse(good[..^1], out _));
            Assert.False(PlayerFilterUpdate.TryParse(new Layout().Raw(good).U8(0).ToArray(), out _));
            Assert.False(PlayerFilterUpdate.TryParse(new Layout().U32(1).U32(0xFFFFFFFF).ToArray(), out _));
            Assert.False(PlayerFilterUpdate.TryParse(new byte[0], out _));
            Assert.False(PlayerFilterUpdate.TryParse(null, out _));
        }

        /// <summary>
        /// FUN_142c3d470: the filter list (FUN_142c37990: u32 count, then per filter u32 map key, u32 FilterId, string
        /// FilterName, string FilterString), then u32 NextFilterId (+0x7ae68).
        /// </summary>
        [Fact]
        public void SendSelfInventoryFilterManager_ListThenNextFilterId()
        {
            var manager = new SendSelfToClient.InventoryFilterManager { NextFilterId = 3 };
            manager.Filters.Add(new SendSelfToClient.InventoryFilterManager.PlayerInventoryFilter
            {
                Key = 2, FilterId = 2, FilterName = "Wood", FilterString = "5"
            });
            Assert.Equal(new Layout().U32(1).U32(2).U32(2).Str("Wood").Str("5").U32(3).ToArray(), Wire.Write(manager));
            Assert.Equal(new Layout().U32(0).U32(1).ToArray(), Wire.Write(new SendSelfToClient.InventoryFilterManager()));
        }
    }
}
