using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Game.Terrain;
using LandmarkEmulator.WorldServer.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// FreeBuild makes voxel materials free (player report 2026-10-31: "some materials get placed then revert ... I get a
    /// message saying I don't have the materials but I'm in free build mode"). The client let a material whose item the
    /// player did not hold be placed; the server refused it (InsufficientMaterials) and the voxels reverted. Free
    /// (ITEM_ID 0) materials worked. With FreeBuild the server neither checks, charges nor gives back materials.
    /// </summary>
    public class FreeBuildMaterialsTests : IDisposable
    {
        /// <summary>VoxelMaterials ARRAY_INDEX 19 "Raw Ruby", ITEM_ID 1750 (gameData695).</summary>
        private const ushort RawRuby = 19;
        private const uint RawRubyItem = 1750;
        private static readonly FieldInfo ItemByMaterial =
            typeof(BuildMaterials).GetField("itemByMaterial", BindingFlags.NonPublic | BindingFlags.Static)!;
        private readonly object previousTable = ItemByMaterial.GetValue(null);

        public FreeBuildMaterialsTests()
            => ItemByMaterial.SetValue(null, new Dictionary<ushort, uint> { [RawRuby] = RawRubyItem });

        public void Dispose() => ItemByMaterial.SetValue(null, previousTable);

        private static bool PayFor(WorldSession session, int voxels)
            => (bool)Private.Call(typeof(Building), "PayFor", session, new Dictionary<ushort, int> { [RawRuby] = voxels });

        [Fact]
        public void FreeBuild_BuildsAMaterialNotHeld_AndNothingIsChargedOrGivenBack()
        {
            using var world = new FakeWorld(c => c.FreeBuild = true);
            WorldSession builder = world.Session(0x7100_0000_0000_0051, "Builder");

            Assert.Null(Building.Budget(builder));              // the Environment Server checks nothing
            Assert.True(PayFor(builder, 500));                  // placed
            Assert.Equal(0u, builder.Inventory.CountOf(RawRubyItem));
            Assert.True(PayFor(builder, -500));                 // taken down again: no free Raw Ruby
            Assert.Equal(0u, builder.Inventory.CountOf(RawRubyItem));
        }

        [Fact]
        public void ThePlayersLoginSnapshot_Decides_NotTheLiveSetting()
        {
            using var world = new FakeWorld(c => c.FreeBuild = true);
            WorldSession builder = world.Session(0x7100_0000_0000_0053, "Builder");
            // Logged in while FreeBuild was off: their client still checks materials itself, so the server does too.
            typeof(WorldSession).GetProperty(nameof(WorldSession.FreeBuildAtLogin))!.SetValue(builder, false);
            Assert.NotNull(Building.Budget(builder));
            Assert.False(PayFor(builder, 10));
        }

        [Fact]
        public void UnderFreeBuild_ADeletedSiteRefundsNoItems()
        {
            using (new FakeWorld(c => c.FreeBuild = true))
                Assert.Empty(SiteDesigns.RefundItems(new Dictionary<ushort, uint> { [RawRuby] = 5000 }));
            using (new FakeWorld())
            {
                var refund = SiteDesigns.RefundItems(new Dictionary<ushort, uint> { [RawRuby] = 5000 }).Single();
                Assert.Equal(RawRubyItem, refund.Item);
                Assert.Equal((uint)(5000 / BuildMaterials.VoxelsPerItem), refund.Count);
            }
        }

        [Fact]
        public void WithoutFreeBuild_AMaterialNotHeld_IsRefused()
        {
            using var world = new FakeWorld();
            WorldSession builder = world.Session(0x7100_0000_0000_0052, "Builder");

            MaterialBudget budget = Building.Budget(builder);
            Assert.NotNull(budget);
            Assert.False(budget.Covers(new Dictionary<ushort, int> { [RawRuby] = 10 }, out uint missing));
            Assert.Equal(RawRubyItem, missing);
            Assert.False(PayFor(builder, 10));
        }
    }
}
