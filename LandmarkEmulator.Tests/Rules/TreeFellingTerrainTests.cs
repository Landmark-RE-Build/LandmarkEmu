using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Game.Terrain;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// A felled tree is an entity change only (player report 2026-10-21, "every login after I felled a tree is slow";
    /// docs/terrain.md "Trees and plants"): <see cref="Gathering.Chop"/> takes the tree away with
    /// PlayerUpdate RemovePlayer and frees its spot, but writes no voxel, queues no heal, adds no cell to the zone-in
    /// manifest and sends no terrain packet. So the login next to a felled tree carries exactly the terrain data of the
    /// login before it, and one AddLightweightProp less for that 64 m region.
    /// </summary>
    public class TreeFellingTerrainTests : IDisposable
    {
        private static int nextZone = 9400;
        private readonly uint zone = (uint)System.Threading.Interlocked.Increment(ref nextZone);
        private readonly FakeWorld world = new();

        public TreeFellingTerrainTests()
        {
            // Flat ground at 1010 m (above the 1002 m sea line) for this test's landscape only.
            TerrainHeights.TestGround = (x, z, z0) => z0 == zone ? 1010f : null;
            // Under the min / max respawn model a landscape's nodes wait for its first population (NodesReady): mark it
            // rolled for today's generator, as a restart with a saved gathered_nodes.txt does.
            var rolled = (Dictionary<uint, string>)Private.Field(typeof(Gathering), "rolled");
            string generator = (string)typeof(Gathering).GetProperty("Generator", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
            lock (rolled)
                rolled[zone] = generator;
            // The main hand's equipped slot (InventoryDefinition.txt is not loaded in tests): 2 at 2, as FakeWorld's. The
            // swing wears the held tool (Durability.ToolUsed).
            var slots = typeof(LandmarkEmulator.WorldServer.Game.Inventory.Inventory)
                .GetField("equipSlotToContainerSlot", BindingFlags.NonPublic | BindingFlags.Static)!;
            if (slots.GetValue(null) == null)
                slots.SetValue(null, new Dictionary<uint, uint> { [2] = 2 });
        }

        public void Dispose()
        {
            TerrainHeights.TestGround = null;
            world.Dispose();
        }

        private static readonly Type NodeClass = Private.Nested(typeof(Gathering), "Node");

        private static T Get<T>(object node, string member)
        {
            FieldInfo field = NodeClass.GetField(member);
            return (T)(field != null ? field.GetValue(node) : NodeClass.GetProperty(member)!.GetValue(node));
        }

        /// <summary>The first standing tree the generator puts on this landscape, with its region.</summary>
        private (object Node, int Rx, int Rz) FirstTree()
        {
            for (int rz = 0; rz < 32; rz++)
                for (int rx = 0; rx < 32; rx++)
                    foreach (object node in (IEnumerable)Private.Call(typeof(Gathering), "Region", zone, rx, rz))
                        if (Get<object>(node, "Type").ToString() == "Tree" && Get<bool>(node, "Standing"))
                            return (node, rx, rz);
            throw new InvalidOperationException("No tree generated on the flat test ground.");
        }

        /// <summary>Everything a client's terrain engine is told at zone-in or later, for this landscape.</summary>
        private string TerrainState()
        {
            var parts = new List<string> { Convert.ToBase64String(TerrainStore.BuildManifest(zone)) };
            for (uint layer = 0; layer < TerrainEdits.LayerCount; layer++)
            {
                parts.Add(string.Join(",", TerrainEdits.ManifestEntries(zone, layer).Select(e => $"{e.Cell:X}:{e.Version}")));
                parts.Add(string.Join(",", TerrainLod.Entries(zone, layer).Select(e => $"{e.Cell:X}:{e.Version}")));
            }
            parts.Add(string.Join(",", OreVeins.Entries(zone).Select(e => $"{e.Cell:X}:{e.Version}")));
            return string.Join("|", parts);
        }

        [Fact]
        public void FellingATree_ChangesNoTerrainAndSendsNoTerrainPacket()
        {
            (object tree, int rx, int rz) = FirstTree();
            var spawn = Get<PlayerUpdateAddLightweightProp>(tree, "Spawn");
            var at = new Vector3(spawn.Position.X, spawn.Position.Y, spawn.Position.Z);
            var session = world.Session(0x7100_0000_0000_0001, "Feller");
            var character = (LandmarkEmulator.Database.Character.Model.CharacterModel)typeof(LandmarkEmulator.WorldServer.Network.WorldSession)
                .GetField("currentCharacter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session);
            character.ZoneId = zone;
            (character.PositionX, character.PositionY, character.PositionZ) = (at.X + 2f, at.Y, at.Z);
            string before = TerrainState();
            FakeWorld.Outgoing(session);

            // Until it falls: 5 swings under TreeHealth=hits, its health / the axe's damage under TreeHealth=health.
            for (int i = 0; i < 100 && Get<bool>(tree, "Standing"); i++)
                Assert.True(Gathering.Chop(session, Get<ulong>(tree, "Guid"), at));

            Assert.False(Get<bool>(tree, "Standing"));
            Assert.Equal(before, TerrainState());
            List<string> sent = FakeWorld.Outgoing(session).Select(p => p.Opcode.ToString()).ToList();
            Assert.Empty(sent.Where(op => op.StartsWith("TerrainCell", StringComparison.Ordinal)
                || op.StartsWith("VoxelConstruction", StringComparison.Ordinal)));
            // The region's next zone-in list is the same nodes minus the felled one.
            var standing = ((IEnumerable)Private.Call(typeof(Gathering), "Region", zone, rx, rz)).Cast<object>()
                .Where(n => Get<bool>(n, "Standing")).Select(n => Get<ulong>(n, "Guid")).ToList();
            Assert.DoesNotContain(Get<ulong>(tree, "Guid"), standing);
        }
    }
}
