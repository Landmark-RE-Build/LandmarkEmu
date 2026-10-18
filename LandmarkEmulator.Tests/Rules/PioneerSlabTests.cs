using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Game.Terrain;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// The slate slab under new Pioneer sites (Pioneer.Slab, L180): the template filter (solid voxels only, the material
    /// remapped, the cell section rebuilt so the copy travels inline) and the Starting Location corner.
    /// </summary>
    public class PioneerSlabTests
    {
        /// <summary>A one-cell template: solids at (1,1,1) and (2,1,1) in material 53, template air at (3,1,1), a vertex-only voxel at (4,1,1).</summary>
        private static VoxelTemplate OneCell()
        {
            var blocks = new BlockData();
            var changed = new byte[BlockData.Count / 8];
            void Set(int x, int y, int z, byte flags, ushort material)
            {
                int i = TerrainEdits.VoxelIndex(x, y, z);
                changed[i >> 3] |= (byte)(0x80 >> (i & 7));
                blocks.Flags[i] = flags;
                blocks.Materials[i] = material;
                blocks.Offsets[i * 3] = blocks.Offsets[i * 3 + 1] = blocks.Offsets[i * 3 + 2] = BlockData.CentreOffset;
            }
            Set(1, 1, 1, 3, 53);
            Set(2, 1, 1, 3, 53);
            Set(3, 1, 1, 3, 0);
            Set(4, 1, 1, 2, 0);
            byte[] packed = TerrainStore.Pack(changed.Concat(blocks.Serialize()).ToArray());
            ulong id = TerrainStore.CellId(TerrainEdits.Level, 0, 0, 0);
            using var stream = new MemoryStream();
            using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                w.Write((byte)6);
                w.Write(1u);
                w.Write(id);
                w.Write(id);
                w.Write(4u);
                w.Write(packed.Length);
                w.Write(packed);
                w.Write(5u); w.Write(2u); w.Write(5u);   // size
                w.Write(1u); w.Write((ushort)53); w.Write(2u);   // materials
                w.Write((byte)0);                       // no props
            }
            stream.Position = 0;
            return VoxelTemplate.Load(stream);
        }

        private static (int X, int Y, int Z, byte Flags, ushort Material)[] Voxels(VoxelTemplate t)
            => t.Cells.SelectMany(c => Enumerable.Range(0, BlockData.Count)
                .Where(i => (c.Changed[i >> 3] & (0x80 >> (i & 7))) != 0)
                .Select(i => (c.X * BlockData.Size + i / BlockData.Size % BlockData.Size, c.Y * BlockData.Size + i % BlockData.Size,
                    c.Z * BlockData.Size + i / (BlockData.Size * BlockData.Size), c.Blocks.Flags[i], c.Blocks.Materials[i]))).ToArray();

        [Fact]
        public void SolidOnly_KeepsSolids_RemapsTheMaterial_DropsAirAndVertexOnly_AndTravelsInline()
        {
            VoxelTemplate source = OneCell();
            Assert.Equal(4, Voxels(source).Length);
            VoxelTemplate slab = source.SolidOnly((x, y, z, stored) => stored == 53 ? (ushort)26 : null);
            Assert.Null(slab.Source);
            Assert.Equal(source.Size, slab.Size);
            Assert.Equal(new[] { ((ushort)26, 2u) }, slab.Materials.ToArray());
            // What a remote Environment Server receives (CellsAsOtd → Load) is the filtered copy, not the file.
            VoxelTemplate back = VoxelTemplate.Load(new MemoryStream(slab.CellsAsOtd()));
            Assert.Equal(new[] { (1, 1, 1, (byte)3, (ushort)26), (2, 1, 1, (byte)3, (ushort)26) }, Voxels(back).OrderBy(v => v.X).ToArray());
        }

        [Fact]
        public void SolidOnly_GivesThePositionFromTheCorner_AndNullLeavesAVoxelOut()
        {
            VoxelTemplate slab = OneCell().SolidOnly((x, y, z, stored) => x == 2 ? null : stored);
            Assert.Equal(new[] { (1, 1, 1, (byte)3, (ushort)53) }, Voxels(VoxelTemplate.Load(new MemoryStream(slab.CellsAsOtd()))));
            VoxelTemplate none = OneCell().SolidOnly((_, _, _, _) => null);
            Assert.Empty(none.Cells);
            Assert.Empty(VoxelTemplate.Load(new MemoryStream(none.CellsAsOtd())).Cells);
        }

        /// <summary>
        /// claim3x3 (525 × 5 × 524) centred on a 210 m site: the -x / +z corner (template x small, z large) within 40 m is
        /// cleared; the plus's arms (template x 175..349 over the full depth) are not touched. claim1x1 (175²) never reaches it.
        /// </summary>
        [Theory]
        [InlineData(1, 524, true)]      // the corner itself
        [InlineData(99, 430, true)]     // 39.4 m from the -x edge, 38 m from the +z edge
        [InlineData(101, 430, false)]   // 40.2 m from the -x edge
        [InlineData(99, 424, false)]    // 40.4 m from the +z edge
        [InlineData(175, 524, false)]   // the north arm's first column
        [InlineData(1, 300, false)]     // the west arm
        [InlineData(1, 1, false)]       // another corner
        public void StartingLocationCorner_Claim3x3(int x, int z, bool cleared)
            => Assert.Equal(cleared, Pioneer.InStartingLocationCorner(x, z, (525u, 5u, 524u)));

        [Fact]
        public void StartingLocationCorner_Claim1x1_NeverCleared()
        {
            for (int x = 1; x <= 175; x += 2)
                for (int z = 1; z <= 175; z += 2)
                    Assert.False(Pioneer.InStartingLocationCorner(x, z, (175u, 5u, 175u)));
        }

        [Theory]
        [InlineData("claim3x3", "claim3x3.otd")]
        [InlineData(" Claim1x1 ", "claim1x1.otd")]
        [InlineData("off", null)]
        [InlineData("claim2x1", null)]
        [InlineData(null, null)]
        public void SlabTemplateFile_FromTheSetting(string setting, string file) => Assert.Equal(file, Pioneer.SlabTemplateFile(setting));
    }
}
