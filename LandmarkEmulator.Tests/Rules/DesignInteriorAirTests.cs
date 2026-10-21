using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Game.Terrain;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// "Whenever you stand inside of any of the player studio designs ... nothing outside is visible" (user, 2026-10-21):
    /// the terrain around a placed design was culled while its flora still drew.
    /// <para>
    /// A copy marks the air its build encloses as material 0xFFFC (interior empty space; every castle design is mostly
    /// that). The server's paste wrote it raw into the build layer. The client's mesher draws 0xFFFC as air (ContourEx
    /// FUN_142ac4590), but its occluder builder (FUN_142ae77d0 / FUN_142ae8610) counts any voxel with the material bit
    /// and a material ≠ 0 whose library entry is opaque, and entry 0xFFFC is a copy of entry 0 (FUN_143ff41f0,
    /// TRANSPARENT_TYPE_ID 0). So the design's rooms and doorways became occlusion quads. The client's own paste
    /// (FUN_142a7eeb0) never writes 0xFFFC: air in the Paste Air / Paste Interior Air modes, nothing otherwise.
    /// </para>
    /// The invariant checked throughout: every voxel the server stores is solid to the occluder builder exactly when it is
    /// solid to the mesher.
    /// </summary>
    public class DesignInteriorAirTests : IDisposable
    {
        private static int nextZone = 9400;
        private readonly uint zone = (uint)System.Threading.Interlocked.Increment(ref nextZone);
        private readonly List<ulong> touched = new();

        public void Dispose()
        {
            foreach (ulong cell in touched.Distinct())
                TerrainEdits.ClearCell(cell, zone);
        }

        private const ushort Stone = 7;
        private const ushort InteriorAir = VoxelTemplate.InteriorAir;

        /// <summary>Client occluder builder FUN_142ae77d0: material bit, material ≠ 0, opaque entry (0xFFFC's is).</summary>
        private static bool OccluderSolid(byte flags, ushort material) => (flags & 1) != 0 && material != 0;

        /// <summary>Client mesher ContourEx FUN_142ac4590: material bit and a material other than 0 and 0xFFFC.</summary>
        private static bool MesherSolid(byte flags, ushort material) => (flags & 1) != 0 && material != 0 && material != InteriorAir;

        /// <summary>
        /// A small castle room as a copy holds it: grid 0..12 × 0..10 × 0..12, stone shell (floor, walls, roof) one voxel
        /// in from the box, exterior air (material 0) around it, the inside and a doorway as interior empty space, and one
        /// interior-air voxel on the box's first slice (never written by the client's paste).
        /// </summary>
        private static Building.Clipboard Room(out int shell, out int inside, out int firstSlice)
        {
            const int sx = 12, sy = 10, sz = 12;
            var block = new BlockData();
            shell = inside = firstSlice = 0;
            for (int z = 0; z <= sz; z++)
                for (int x = 0; x <= sx; x++)
                    for (int y = 0; y <= sy; y++)
                    {
                        int i = BlockData.Index(x, y, z);
                        bool inBox = x >= 1 && x <= sx - 1 && y >= 1 && y <= sy - 1 && z >= 1 && z <= sz - 1;
                        bool onShell = inBox && (x == 1 || x == sx - 1 || y == 1 || y == sy - 1 || z == 1 || z == sz - 1);
                        bool door = z == 1 && x >= 5 && x <= 7 && y >= 2 && y <= 6;
                        block.Flags[i] = 3;
                        block.Offsets[i * 3] = block.Offsets[i * 3 + 1] = block.Offsets[i * 3 + 2] = BlockData.CentreOffset;
                        if (onShell && !door)
                        {
                            block.Materials[i] = Stone;
                            shell++;
                        }
                        else if (inBox)
                        {
                            block.Materials[i] = InteriorAir;
                            inside++;
                        }
                        else
                            block.Materials[i] = 0;
                    }
            block.Materials[BlockData.Index(0, 5, 6)] = InteriorAir;
            firstSlice = 1;
            return new Building.Clipboard(block, sx, sy, sz);
        }

        private static readonly int[] Identity = { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
        private static readonly int[] Yaw90 = { 0, 0, 1, 0, 0, 1, 0, 0, -1, 0, 0, 0, 0, 0, 0, 1 };
        private static readonly long[] Origin = { 159900L * 40 + 10, 130L * 40 + 5, 159800L * 40 + 3 };

        private static List<TerrainEdits.VoxelValue> Values(Dictionary<ulong, List<TerrainEdits.VoxelValue>> byCell)
            => byCell.Values.SelectMany(v => v).ToList();

        private static Dictionary<ulong, List<TerrainEdits.VoxelValue>> PasteOnGrid(Building.Clipboard clip, int[] m, uint mode)
        {
            int[] size = { (int)clip.SizeX, (int)clip.SizeY, (int)clip.SizeZ };
            var shift = new int[3];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    shift[i] -= Math.Min(0, m[i * 4 + j] * size[j]);
            return Building.TurnedVoxels(clip, m, Origin[0] + shift[0], Origin[1] + shift[1], Origin[2] + shift[2], mode);
        }

        private static Dictionary<ulong, List<TerrainEdits.VoxelValue>> PasteOffGrid(Building.Clipboard clip, uint mode)
        {
            double v = TerrainEdits.VoxelSize;
            float[] t = { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
            var byCell = Building.OffGridVoxels(clip, t, Origin[0] * v, Origin[1] * v, Origin[2] * v, out _, out _, out _, out _, out _, out _, mode);
            Assert.NotNull(byCell);
            return byCell;
        }

        [Theory]
        [InlineData("ongrid")]
        [InlineData("ongrid-turned")]
        [InlineData("offgrid")]
        public void DefaultPaste_WritesTheShellOnly_NoInteriorAir(string path)
        {
            Building.Clipboard clip = Room(out int shell, out _, out _);
            var values = Values(path switch
            {
                "ongrid" => PasteOnGrid(clip, Identity, Building.PasteSolidsMode),
                "ongrid-turned" => PasteOnGrid(clip, Yaw90, Building.PasteSolidsMode),
                _ => PasteOffGrid(clip, Building.PasteSolidsMode)
            });
            Assert.False(values.Any(v => v.Material == InteriorAir));
            Assert.False(values.Any(v => (v.Flags & 1) != 0 && v.Material == 0));
            Assert.Equal(shell, values.Count(v => (v.Flags & 1) != 0 && v.Material == Stone));
            Assert.All(values, v => Assert.Equal(MesherSolid(v.Flags, v.Material), OccluderSolid(v.Flags, v.Material)));
        }

        [Theory]
        [InlineData(2u, false)]
        [InlineData(3u, false)]
        [InlineData(3u, true)]
        public void AirModes_WriteInteriorAirAsAir_ExceptOnTheFirstSlice(uint mode, bool offGrid)
        {
            Building.Clipboard clip = Room(out int shell, out int inside, out _);
            var values = Values(offGrid ? PasteOffGrid(clip, mode) : PasteOnGrid(clip, Identity, mode));
            Assert.False(values.Any(v => v.Material == InteriorAir));
            Assert.Equal(shell, values.Count(v => (v.Flags & 1) != 0 && v.Material == Stone));
            // The room's inside and doorway carve (material 0); the first-slice voxel and the exterior air are not written.
            Assert.Equal(inside, values.Count(v => (v.Flags & 1) != 0 && v.Material == 0));
            Assert.All(values, v => Assert.Equal(MesherSolid(v.Flags, v.Material), OccluderSolid(v.Flags, v.Material)));
        }

        [Fact]
        public void PastedMaterial_FollowsTheClientPasteTable()
        {
            Assert.Equal((ushort)Stone, Building.PastedMaterial(3, Stone, 1, false));
            Assert.Null(Building.PastedMaterial(3, InteriorAir, 1, false));
            Assert.Null(Building.PastedMaterial(3, InteriorAir, 0, false));
            Assert.Null(Building.PastedMaterial(3, InteriorAir, 4, false));
            Assert.Equal((ushort)0, Building.PastedMaterial(3, InteriorAir, 2, false));
            Assert.Equal((ushort)0, Building.PastedMaterial(1, InteriorAir, 3, false));
            Assert.Null(Building.PastedMaterial(3, InteriorAir, 3, true));
            Assert.Null(Building.PastedMaterial(2, InteriorAir, 3, false));   // vertex only: no material to write
            Assert.Null(Building.PastedMaterial(3, 0, 2, false));             // surrounding air: not applied yet
        }

        private ulong Cell(uint dx = 0)
        {
            ulong cell = TerrainStore.CellId(TerrainEdits.Level, 159900 + dx, 130, 159800);
            touched.Add(cell);
            return cell;
        }

        [Fact]
        public void Write_StoresInteriorAirAsAir()
        {
            ulong cell = Cell();
            int a = TerrainEdits.VoxelIndex(3, 4, 5), b = TerrainEdits.VoxelIndex(3, 5, 5);
            TerrainEdits.Write(cell, new List<TerrainEdits.VoxelValue>
            {
                new(a, 1, InteriorAir, BlockData.CentreOffset, BlockData.CentreOffset, BlockData.CentreOffset),
                new(b, 3, Stone, 100, 110, 120)
            }, zone, owner: 77);
            var mask = new byte[BlockData.Count / 8];
            mask[a >> 3] |= (byte)(0x80 >> (a & 7));
            mask[b >> 3] |= (byte)(0x80 >> (b & 7));
            var read = TerrainEdits.Read(cell, mask, zone).ToDictionary(v => v.Index);
            Assert.Equal(1, read[a].Flags);
            Assert.Equal(0, read[a].Material);
            Assert.Equal(Stone, read[b].Material);
            Assert.Equal(3, read[b].Flags);
        }

        [Fact]
        public void StoredInteriorAir_IsRepairedAsAir_AtANewVersion()
        {
            // A design placed before the fix: its build-layer cell holds raw 0xFFFC (ApplyDiff stores what it is given).
            ulong cell = Cell(1);
            var result = new BlockData();
            var changed = new byte[BlockData.Count / 8];
            var interior = new List<int>();
            for (int y = 2; y < 8; y++)
            {
                int i = TerrainEdits.VoxelIndex(10, y, 10);
                result.Flags[i] = 3;
                result.Materials[i] = y < 4 ? Stone : InteriorAir;
                result.Offsets[i * 3] = result.Offsets[i * 3 + 1] = result.Offsets[i * 3 + 2] = BlockData.CentreOffset;
                changed[i >> 3] |= (byte)(0x80 >> (i & 7));
                if (y >= 4)
                    interior.Add(i);
            }
            Assert.True(TerrainEdits.ApplyDiff(cell, changed, result, null, out _, zone, 0, TerrainEdits.BuildLayer));
            uint before = TerrainEdits.LayerVersion(cell, zone, TerrainEdits.BuildLayer);

            Assert.Equal(1, TerrainEdits.RepairInteriorAir(zone));

            var read = TerrainEdits.Read(cell, changed, zone).ToDictionary(v => v.Index);
            Assert.All(interior, i => Assert.Equal((byte)3, read[i].Flags));
            Assert.All(interior, i => Assert.Equal((ushort)0, read[i].Material));
            Assert.Equal(2, read.Values.Count(v => v.Material == Stone));
            Assert.All(read.Values, v => Assert.Equal(MesherSolid(v.Flags, v.Material), OccluderSolid(v.Flags, v.Material)));
            Assert.NotEqual(before, TerrainEdits.LayerVersion(cell, zone, TerrainEdits.BuildLayer));
            // Nothing left to repair.
            Assert.Equal(0, TerrainEdits.RepairInteriorAir(zone));
        }

        [Fact]
        public void CoarseCopies_NeverCarryInteriorAir()
        {
            // The coarse copy takes each parent voxel from the child voxel at its corner: a 0xFFFC there becomes air.
            ulong child = TerrainStore.CellId(TerrainEdits.Level, 159900, 130, 159800);
            var cell = new BlockData();
            int stone = TerrainEdits.VoxelIndex(0, 0, 0), interior = TerrainEdits.VoxelIndex(2, 0, 0);
            cell.SetSolid(stone, Stone);
            cell.SetSolid(interior, InteriorAir);
            var parent = new BlockData();
            TerrainLod.Downsample(child, cell, parent);
            int ps = TerrainEdits.VoxelIndex(0, 0, 0), pi = TerrainEdits.VoxelIndex(1, 0, 0);
            Assert.Equal(Stone, parent.Materials[ps]);
            Assert.Equal(0, parent.Materials[pi]);
            Assert.NotEqual(0, parent.Flags[pi] & 1);
            Assert.DoesNotContain(InteriorAir, parent.Materials);
        }
    }
}
