using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Game.Terrain;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// The voxels a design's VoxelDataResponse (TemplateBase 0xF0 / 5) carries: the template's cell section only, counted
    /// from (0, 0, 0). The client caches them per design (FUN_1431136c0 asks again only while the record's block count
    /// +0x158 is 0) and builds its paste clipboard and the "Generating preview..." mesh from them on its own
    /// (FUN_143111480 → FUN_143b92dc0; VoxelPasteCursor::GetVoxelPreviewMesh FUN_1436cdf00 → PreviewGenerationThread
    /// FUN_143f99120; docs/gameplay.md "Placing a design"). A Starter Castle .otd is mostly screenshots (Modular Castle
    /// Window: 218,929 bytes, of which 3,283 are cell blobs), so the reply must stop at the cell section.
    /// </summary>
    public class DesignVoxelDataTests
    {
        private static byte[] PackedCell(int voxel, ushort material)
        {
            var blocks = new BlockData();
            var changed = new byte[BlockData.Count / 8];
            changed[voxel >> 3] |= (byte)(0x80 >> (voxel & 7));
            blocks.Flags[voxel] = 3;
            blocks.Materials[voxel] = material;
            blocks.Offsets[voxel * 3] = blocks.Offsets[voxel * 3 + 1] = blocks.Offsets[voxel * 3 + 2] = BlockData.CentreOffset;
            return TerrainStore.Pack(changed.Concat(blocks.Serialize()).ToArray());
        }

        /// <summary>A cell section (u32 n × {u64 cell, u64 cell, u32 type 4, u32 length, blob}) at the given cells.</summary>
        private static byte[] CellSection(params (uint X, uint Y, uint Z)[] cells)
        {
            using var stream = new MemoryStream();
            using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                w.Write((uint)cells.Length);
                foreach ((uint x, uint y, uint z) in cells)
                {
                    ulong id = TerrainStore.CellId(TerrainEdits.Level, x, y, z);
                    byte[] packed = PackedCell(TerrainEdits.VoxelIndex(1, 1, 1), 53);
                    w.Write(id);
                    w.Write(id);
                    w.Write(4u);
                    w.Write(packed.Length);
                    w.Write(packed);
                }
            }
            return stream.ToArray();
        }

        [Fact]
        public void TemplateCells_StopBeforeTheScreenshots_AndNeedNoRebase()
        {
            byte[] cells = CellSection((0, 0, 0), (0, 1, 0));
            byte[] jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }.Concat(new byte[20000]).ToArray();
            using var stream = new MemoryStream();
            using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                w.Write((byte)5);
                w.Write(cells);
                w.Write(8u); w.Write(41u); w.Write(40u);       // size (the Modular Castle Window's)
                w.Write(1u); w.Write((ushort)53); w.Write(2u);  // materials
                w.Write((byte)0);                              // no props
                w.Write(2u);                                   // the trailer: screenshots, as in the castle files
                w.Write(jpeg.Length); w.Write(jpeg);
                w.Write(jpeg.Length); w.Write(jpeg);
            }
            stream.Position = 0;
            VoxelTemplate template = VoxelTemplate.Load(stream);

            Assert.Equal(cells, template.RawCells);                  // the block list sent is the cell section, no more
            Assert.True(template.RawCells.Length < jpeg.Length);
            Assert.Same(template.RawCells, Templates.Rebased(template.RawCells));   // already 0-based: sent as stored
        }

        [Fact]
        public void Rebased_CountsCellsFromTheLowestOne_AndKeepsTheSize()
        {
            byte[] world = CellSection((159874, 185, 159790), (159875, 185, 159790), (159874, 186, 159791));
            byte[] rebased = Templates.Rebased(world);

            Assert.Equal(world.Length, rebased.Length);
            var ids = Enumerable.Range(0, 3).Select(i =>
            {
                int at = 4 + i * (24 + BitConverter.ToInt32(world, 4 + 20));
                (_, uint x, uint y, uint z) = TerrainStore.Decode(BitConverter.ToUInt64(rebased, at));
                Assert.Equal(BitConverter.ToUInt64(rebased, at), BitConverter.ToUInt64(rebased, at + 8));
                return (x, y, z);
            }).ToArray();
            Assert.Equal(new[] { (0u, 0u, 0u), (1u, 0u, 0u), (0u, 1u, 1u) }, ids);
        }
    }
}
