using LandmarkEmulator.WorldServer.Game.Terrain;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// The client's own version of a layer the server wrote (player report 2026-10-31: a sphere Remove left a
    /// straight-edged section standing). The server stamps layers it writes itself with a counter; the client re-stamps the
    /// same content with CRC32 of its compressed copy (0 when empty), and its next edit there was refused as stale. A base
    /// that names the server's content is now the same copy (TerrainEdits.HoldsClientCopy).
    /// </summary>
    public class ClientLayerVersionTests : IDisposable
    {
        private static int nextZone = 9900;
        private readonly uint zone = (uint)System.Threading.Interlocked.Increment(ref nextZone);
        private readonly ulong cell = TerrainStore.CellId(TerrainEdits.Level, 310, 20, 310);

        public void Dispose() => TerrainEdits.ClearCell(cell, zone);

        private static TerrainEdits.VoxelValue Solid(int index) => new(index, 1, 7, 0x80, 0x80, 0x80);
        private static TerrainEdits.VoxelValue Air(int index) => new(index, 0, 0, 0, 0, 0);

        private uint Version => TerrainEdits.LayerVersion(cell, zone, TerrainEdits.BuildLayer);

        [Fact]
        public void Crc32_IsTheStandardOne()
            => Assert.Equal(0xCBF43926u, ClientLayerCodec.Crc32(Encoding.ASCII.GetBytes("123456789"), 9));

        [Fact]
        public void TheCodec_GivesTheClientsVersions_OfLoggedLayers()
        {
            // Five layer-1 contents from console_47.log, each named by the version the client gave it.
            string folder = Path.Combine(AppContext.BaseDirectory, "Data", "ClientLayerVersions");
            string[] files = Directory.GetFiles(folder, "vec_*.raw");
            Assert.Equal(5, files.Length);
            if (!ClientLayerCodec.Available)
            {
                Console.WriteLine("    classic zlib (zlib1.dll / libz.so.1) not found: client layer versions not checked");
                return;
            }
            foreach (string file in files)
            {
                uint expected = Convert.ToUInt32(Path.GetFileNameWithoutExtension(file).Split('_')[2], 16);
                Assert.Equal(expected, ClientLayerCodec.Version(File.ReadAllBytes(file)));
            }
        }

        [Fact]
        public void AServerWrittenLayerLeftEmpty_AcceptsTheClientsBaseZero()
        {
            TerrainEdits.Write(cell, new List<TerrainEdits.VoxelValue> { Solid(10) }, zone);
            TerrainEdits.Write(cell, new List<TerrainEdits.VoxelValue> { Air(10) }, zone);   // healed / undone: empty again
            Assert.NotEqual(0u, Version);                                                      // the server's counter
            Assert.True(TerrainEdits.HoldsClientCopy(cell, zone, 0));
        }

        [Fact]
        public void ANonEmptyLayer_RefusesBaseZero_AndAWrongVersion()
        {
            TerrainEdits.Write(cell, new List<TerrainEdits.VoxelValue> { Solid(10) }, zone);
            Assert.False(TerrainEdits.HoldsClientCopy(cell, zone, 0));
            Assert.False(TerrainEdits.HoldsClientCopy(cell, zone, 0x1234_5678));
        }

        [Fact]
        public void TheClientsVersionOfTheServersContent_IsTheSameCopy()
        {
            if (!ClientLayerCodec.Available)
                return;
            TerrainEdits.Write(cell, new List<TerrainEdits.VoxelValue> { Solid(10), Solid(11) }, zone);
            uint clientVersion = ClientLayerCodec.Version(LayerBytes())!.Value;
            Assert.NotEqual(Version, clientVersion);                          // the server's number differs
            Assert.True(TerrainEdits.HoldsClientCopy(cell, zone, clientVersion));
            // Someone else changes the cell: the client's copy is old now.
            TerrainEdits.Write(cell, new List<TerrainEdits.VoxelValue> { Solid(12) }, zone);
            Assert.False(TerrainEdits.HoldsClientCopy(cell, zone, clientVersion));
        }

        /// <summary>The building layer's raw bytes as the server holds them (what it packs into a CellsUpdate).</summary>
        private byte[] LayerBytes()
        {
            var values = TerrainEdits.Read(cell, Enumerable.Repeat((byte)0xFF, BlockData.Count / 8).ToArray(), zone);
            var blocks = new BlockData();
            foreach (TerrainEdits.VoxelValue v in values)
            {
                blocks.Flags[v.Index] = v.Flags;
                blocks.Materials[v.Index] = v.Material;
                blocks.Offsets[v.Index * 3] = v.O0;
                blocks.Offsets[v.Index * 3 + 1] = v.O1;
                blocks.Offsets[v.Index * 3 + 2] = v.O2;
            }
            return blocks.Serialize();
        }
    }
}
