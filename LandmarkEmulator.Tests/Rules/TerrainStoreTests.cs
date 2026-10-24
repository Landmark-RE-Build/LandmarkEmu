using LandmarkEmulator.WorldServer.Game.Terrain;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// The terrain store's own formats (EnvServer.Core): the client's block data as the server decodes it, and the cell
    /// pack every edited cell is saved in.
    /// </summary>
    public class TerrainStoreTests
    {
        /// <summary>Format-2 block data (BlockData.Deserialize's layout): mask, flags, palette switch, materials, offsets.</summary>
        private static byte[] Block(int materialCount, int materialBytes)
        {
            using var stream = new MemoryStream();
            using var w = new BinaryWriter(stream);
            w.Write(2u);
            w.Write(new byte[BlockData.Count / 8]);   // no voxel changed
            w.Write(0);                               // flags
            w.Write(false);                           // no palette
            w.Write(materialCount);
            w.Write(new byte[materialBytes]);
            w.Write(0);                               // offsets
            w.Flush();
            return stream.ToArray();
        }

        [Fact]
        public void BlockData_ReadsAnEmptyBlock()
        {
            BlockData blocks = BlockData.Deserialize(Block(0, 0));
            Assert.True(Array.TrueForAll(blocks.Flags, f => f == 0));
        }

        /// <summary>
        /// A client blob naming far more materials than it holds is refused before anything is allocated (it allocated
        /// ~4 GB for a count near 2^31, bughunt-services #4).
        /// </summary>
        [Theory]
        [InlineData(int.MaxValue)]
        [InlineData(-1)]
        [InlineData(1000)]
        public void BlockData_RefusesAMaterialCountBeyondTheData(int count)
            => Assert.Throws<InvalidDataException>(() => BlockData.Deserialize(Block(count, 16)));

        /// <summary>Records appended to a cell pack load back; the latest record of a key wins; a tombstone drops it.</summary>
        [Fact]
        public void CellPack_AppendsLoadBack()
        {
            string folder = Path.Combine(Path.GetTempPath(), "lmemu-cellpack-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(folder);
                DateTime at = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
                using (var pack = new CellPack(folder))
                {
                    pack.Append(new List<(ulong, uint, byte[], DateTime)> { (1, 1, new byte[] { 1, 2, 3 }, at), (2, 1, new byte[] { 4 }, at) });
                    pack.Append(new List<(ulong, uint, byte[], DateTime)> { (1, 2, new byte[] { 9, 9 }, at), (2, 2, null, at) });
                }
                using (var pack = new CellPack(folder))
                {
                    Dictionary<ulong, CellPack.Entry> entries = pack.Load();
                    Assert.Equal(1, entries.Count);
                    Assert.Equal(2u, entries[1].Version);
                    Assert.Equal(2, pack.Read(entries[1]).Length);
                    Assert.Equal((byte)9, pack.Read(entries[1])[0]);
                }
            }
            finally
            {
                try { Directory.Delete(folder, true); } catch (IOException) { }
            }
        }

        /// <summary>
        /// Flush indexes dirty overlays instead of scanning the whole store: a write raises DirtyCount, one Flush clears
        /// it, and a second idle Flush leaves it empty (idle world ticks must not walk every stamped cell).
        /// </summary>
        [Fact]
        public void TerrainEdits_Flush_ClearsDirtyIndexWithoutRescanning()
        {
            uint zone = 9501;
            ulong cell = TerrainStore.CellId(TerrainEdits.Level, 160000, 130, 160000);
            int index = TerrainEdits.VoxelIndex(1, 1, 1);
            try
            {
                TerrainEdits.Write(cell, new List<TerrainEdits.VoxelValue>
                {
                    new(index, 3, 7, BlockData.CentreOffset, BlockData.CentreOffset, BlockData.CentreOffset)
                }, zone);
                Assert.True(TerrainEdits.DirtyCount >= 1);

                TerrainEdits.Flush();
                Assert.Equal(0, TerrainEdits.DirtyCount);

                TerrainEdits.Flush();
                Assert.Equal(0, TerrainEdits.DirtyCount);
            }
            finally
            {
                TerrainEdits.ClearCell(cell, zone);
                TerrainEdits.Flush();
            }
        }

        /// <summary>Ownership Flush uses the same dirty index: Set dirties, Flush clears, idle Flush stays clean.</summary>
        [Fact]
        public void VoxelOwnership_Flush_ClearsDirtyIndex()
        {
            string folder = Path.Combine(Path.GetTempPath(), "lmemu-ownership-dirty-" + Guid.NewGuid().ToString("N"));
            uint zone = 9502;
            ulong cell = TerrainStore.CellId(TerrainEdits.Level, 160010, 130, 160010);
            try
            {
                Directory.CreateDirectory(folder);
                // Sets the ownership save folder (and loads any empty tree) so Flush can clear the dirty index.
                TerrainEdits.Load(folder);
                VoxelOwnership.Set(zone, cell, new[] { 0, 1, 2 }, _ => true, owner: 42);
                Assert.True(VoxelOwnership.DirtyCount >= 1);

                TerrainEdits.Flush();
                Assert.Equal(0, VoxelOwnership.DirtyCount);

                TerrainEdits.Flush();
                Assert.Equal(0, VoxelOwnership.DirtyCount);
            }
            finally
            {
                VoxelOwnership.ClearCell(zone, cell);
                try { Directory.Delete(folder, true); } catch (IOException) { }
            }
        }
    }
}
