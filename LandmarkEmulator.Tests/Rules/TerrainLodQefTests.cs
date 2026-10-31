using LandmarkEmulator.WorldServer.Game.Terrain;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// The coarse LOD copies after the port of VoxelFarm::updateBlockLOD (EnvServer.Core TerrainLod.UpdateBlockLod, 0.695
    /// FUN_142aadbb0) and its QEF (VoxelFarmQef, FUN_142aa2900 … FUN_142aa5700), 2026-11-01. Before it, Mesh Shapes turned
    /// to stairs past about 100 m (the level 2 → 3 switch): the corner / mass-point rule wrote no vertex for a coarse cube
    /// whose low corner was air (vertex-only or unset), which is every -x, -z and underside face (trace/0695/lod-qef.md).
    /// The parent cases below are worked by hand from the decompile (part_0052.c:33270-34375).
    /// </summary>
    public class TerrainLodQefTests
    {
        private const ushort Sand = 79, Slate = 26;

        // ---------------------------------------------------------------------------------------------------- QEF

        private static double[] Rows(params double[] normals) => normals;

        [Fact]
        public void Qef_ThreeAxisPlanes_GiveTheirCorner()
        {
            var w = new VoxelFarmQef.Workspace();
            double[] a = Rows(1, 0, 0, 0, 1, 0, 0, 0, 1);
            double[] b = { 0.1, -0.2, 0.3 };
            VoxelFarmQef.Solve(a, b, 3, w);
            Assert.Equal(0.1, w.X[0], 1e-15);
            Assert.Equal(-0.2, w.X[1], 1e-15);
            Assert.Equal(0.3, w.X[2], 1e-15);
            // A and b are left as they were (the client solves a copy, and re-solves with the same A).
            Assert.Equal(new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }, a);
        }

        /// <summary>
        /// The decomposition with the chop tolerance near 0 is an exact SVD (1e-12): that proves the ported Householder,
        /// unpack, QR and Givens steps. With the client's 1e-5 (DAT_144c41090) a dropped super-diagonal value leaves
        /// A ≈ U·D·Vᵀ to about 1e-5 of the singular values, as in the client.
        /// </summary>
        [Theory]
        [InlineData(1e-15, 1e-12)]
        [InlineData(VoxelFarmQef.ChopEpsilon, 1e-4)]
        public void Qef_Decomposition_IsOrthonormalAndRebuildsA(double chop, double tolerance)
        {
            var random = new Random(4711);
            var w = new VoxelFarmQef.Workspace();
            foreach (int rows in new[] { 1, 2, 3, 4, 5, 8, 17, 32, 63 })
                for (int trial = 0; trial < 20; trial++)
                {
                    var a = new double[rows * 3];
                    for (int i = 0; i < a.Length; i++)
                        a[i] = random.NextDouble() * 2 - 1;
                    // Every fourth trial rank-deficient: all rows in one plane (z = 0) or along one line.
                    if (trial % 4 == 1)
                        for (int r = 0; r < rows; r++)
                            a[r * 3 + 2] = 0;
                    if (trial % 4 == 2)
                        for (int r = 0; r < rows; r++)
                            (a[r * 3], a[r * 3 + 1], a[r * 3 + 2]) = (a[r * 3] * 0.3, a[r * 3] * -0.4, a[r * 3] * 0.5);
                    VoxelFarmQef.Decompose(a, rows, w, chop);

                    Assert.True(w.D[0] >= w.D[1] && w.D[1] >= w.D[2] && w.D[2] >= 0, $"singular values {w.D[0]} {w.D[1]} {w.D[2]}");
                    for (int i = 0; i < 3; i++)
                        for (int j = 0; j < 3; j++)
                        {
                            double vv = 0;
                            for (int k = 0; k < 3; k++)
                                vv += w.V[k * 3 + i] * w.V[k * 3 + j];
                            Assert.Equal(i == j ? 1.0 : 0.0, vv, 1e-12);
                            // U's columns: orthonormal where they carry a singular value (and for rows ≥ 3 all of them).
                            if (rows >= 3)
                            {
                                double uu = 0;
                                for (int r = 0; r < rows; r++)
                                    uu += w.U[r * 3 + i] * w.U[r * 3 + j];
                                Assert.Equal(i == j ? 1.0 : 0.0, uu, 1e-12);
                            }
                        }
                    for (int r = 0; r < rows; r++)
                        for (int c = 0; c < 3; c++)
                        {
                            double sum = 0;
                            for (int k = 0; k < 3; k++)
                                sum += w.U[r * 3 + k] * w.D[k] * w.V[c * 3 + k];
                            Assert.Equal(a[r * 3 + c], sum, tolerance);
                        }
                }
        }

        /// <summary>Least squares against the normal equations: exact with a near-0 chop, within 1e-4 with the client's.</summary>
        [Theory]
        [InlineData(1e-15, 1e-9)]
        [InlineData(VoxelFarmQef.ChopEpsilon, 1e-4)]
        public void Qef_OverdeterminedSystems_MatchTheNormalEquations(double chop, double tolerance)
        {
            var random = new Random(1234);
            var w = new VoxelFarmQef.Workspace();
            int solved = 0;
            for (int trial = 0; trial < 200; trial++)
            {
                int rows = 3 + trial % 61;
                var a = new double[rows * 3];
                var b = new double[rows];
                for (int r = 0; r < rows; r++)
                {
                    // Unit normals, as updateBlockLOD passes them.
                    double x = random.NextDouble() * 2 - 1, y = random.NextDouble() * 2 - 1, z = random.NextDouble() * 2 - 1;
                    double length = Math.Sqrt(x * x + y * y + z * z);
                    (a[r * 3], a[r * 3 + 1], a[r * 3 + 2]) = (x / length, y / length, z / length);
                    b[r] = random.NextDouble() - 0.5;
                }
                // Normal equations AᵀA x = Aᵀb by Cramer's rule, skipped when a singular value would be cut (< 0.1).
                var m = new double[9];
                var t = new double[3];
                for (int r = 0; r < rows; r++)
                    for (int i = 0; i < 3; i++)
                    {
                        t[i] += a[r * 3 + i] * b[r];
                        for (int j = 0; j < 3; j++)
                            m[i * 3 + j] += a[r * 3 + i] * a[r * 3 + j];
                    }
                VoxelFarmQef.Decompose(a, rows, w, chop);
                if (w.D[2] < 0.2)
                    continue;
                double det = Det(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8]);
                double ex = Det(t[0], m[1], m[2], t[1], m[4], m[5], t[2], m[7], m[8]) / det;
                double ey = Det(m[0], t[0], m[2], m[3], t[1], m[5], m[6], t[2], m[8]) / det;
                double ez = Det(m[0], m[1], t[0], m[3], m[4], t[1], m[6], m[7], t[2]) / det;
                VoxelFarmQef.Solve(a, b, rows, w, chop);
                Assert.Equal(ex, w.X[0], tolerance);
                Assert.Equal(ey, w.X[1], tolerance);
                Assert.Equal(ez, w.X[2], tolerance);
                solved++;
            }
            Assert.True(solved > 150, $"only {solved} well-conditioned systems");
        }

        private static double Det(double a, double b, double c, double d, double e, double f, double g, double h, double i)
            => a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);

        [Fact]
        public void Qef_ParallelPlanes_SolveOnlyAlongTheirNormal()
        {
            // Six floor / ceiling planes at height 0.3 (normals ±y): one singular value, the two others cut to 0, so x and
            // z stay at 0 (the cube centre once updateBlockLOD adds 0.5).
            var w = new VoxelFarmQef.Workspace();
            var a = new double[18];
            var b = new double[6];
            for (int r = 0; r < 6; r++)
            {
                double sign = r % 2 == 0 ? 1 : -1;
                a[r * 3 + 1] = sign;
                b[r] = sign * 0.3;
            }
            VoxelFarmQef.Solve(a, b, 6, w);
            Assert.Equal(0.0, w.X[0], 1e-15);
            Assert.Equal(0.3, w.X[1], 1e-15);
            Assert.Equal(0.0, w.X[2], 1e-15);
            Assert.Equal(Math.Sqrt(6), w.D[0], 1e-12);
            Assert.Equal(0.0, w.D[1]);
            Assert.Equal(0.0, w.D[2]);
        }

        // ---------------------------------------------------------------------------------------------- parent rule

        /// <summary>
        /// A level-3 parent and its 4 × 4 × 4 level-2 grid, written in grid voxels (0 .. 159 per axis; the parent's
        /// children are grid voxels 40 .. 119, parent voxel p ↔ grid voxel 40 + 2p).
        /// </summary>
        private sealed class Scene
        {
            public readonly ulong Parent = TerrainStore.CellId(3, 50000, 40, 50000);
            public readonly BlockData[] Grid = new BlockData[64];

            public void Set(int gx, int gy, int gz, byte flags, ushort material, byte ox = 0, byte oy = 0, byte oz = 0)
            {
                int cell = TerrainLod.GridIndex(gx / 40, gy / 40, gz / 40);
                BlockData block = Grid[cell] ??= new BlockData();
                int i = TerrainEdits.VoxelIndex(gx % 40, gy % 40, gz % 40);
                block.Flags[i] = flags;
                block.Materials[i] = material;
                (block.Offsets[i * 3], block.Offsets[i * 3 + 1], block.Offsets[i * 3 + 2]) = (ox, oy, oz);
            }

            /// <summary>Every grid voxel column (gx, gz) over the whole grid.</summary>
            public void Layer(int gy, byte flags, ushort material, byte ox = 0, byte oy = 0, byte oz = 0)
            {
                for (int gx = 0; gx < 160; gx++)
                    for (int gz = 0; gz < 160; gz++)
                        Set(gx, gy, gz, flags, material, ox, oy, oz);
            }

            public BlockData Build()
            {
                var parent = new BlockData();
                Assert.True(TerrainLod.UpdateBlockLod(Parent, Grid, parent, new TerrainLod.Scratch()));
                return parent;
            }
        }

        /// <summary>An offset byte whose parent value lies mid-step: (111 / 85 − 1) / 2 → 98.5 before the floor.</summary>
        private const byte Surface = 111, Centre = BlockData.CentreOffset;

        private static (byte Flags, ushort Material, byte X, byte Y, byte Z) Voxel(BlockData b, int x, int y, int z)
        {
            int i = TerrainEdits.VoxelIndex(x, y, z);
            return (b.Flags[i], b.Materials[i], b.Offsets[i * 3], b.Offsets[i * 3 + 1], b.Offsets[i * 3 + 2]);
        }

        [Fact]
        public void Floor_AtAnEvenHeight_ParentTakesThePlane()
        {
            // Sand up to grid y 48 (child y 8, parent y 4); the top layer carries the vertices (flags 3).
            var s = new Scene();
            for (int gy = 0; gy < 48; gy++)
                s.Layer(gy, 1, Sand);
            s.Layer(48, 3, Sand, Centre, Surface, Centre);
            BlockData parent = s.Build();

            // Each child is a thick solid (an interior cube within 2 voxels: FUN_142aa9a00 = 1), so none is chosen; the
            // parent keeps the corner child's material and gets the QEF of the 32 fan triangles: a plane, so x and z
            // stay at the cube centre (127 / 128 by rounding) and y = (0.306 + 0) / 2 → byte 98.
            var top = Voxel(parent, 10, 4, 10);
            Assert.Equal((byte)3, top.Flags);
            Assert.Equal(Sand, top.Material);
            Assert.Equal((byte)98, top.Y);
            Assert.InRange(top.X, (byte)127, (byte)128);
            Assert.InRange(top.Z, (byte)127, (byte)128);
            // Below: solid, no vertex (no child has bit 1). Above: nothing.
            Assert.Equal(((byte)1, Sand, (byte)0, (byte)0, (byte)0), Voxel(parent, 10, 3, 10));
            Assert.Equal((byte)0, Voxel(parent, 10, 5, 10).Flags);
        }

        [Fact]
        public void Ceiling_VertexOnlyCornerChild_GivesAVertexOnlyParent()
        {
            // Sand from grid y 49 up; the surface vertices sit in vertex-only voxels at y 48 (the client stores a cube's
            // vertex at its low corner, which is air under a ceiling and on every -x / -z face).
            var s = new Scene();
            for (int gy = 49; gy < 160; gy++)
                s.Layer(gy, 1, Sand);
            s.Layer(48, 2, 0, Centre, Surface, Centre);
            BlockData parent = s.Build();

            // The corner child has no material (0xFFFE), the vertex-only children have quads (code 6, best), the solid
            // children above have none: a vertex-only parent. The corner/mass-point rule wrote nothing here.
            var under = Voxel(parent, 10, 4, 10);
            Assert.Equal((byte)2, under.Flags);
            Assert.Equal((ushort)0, under.Material);
            Assert.Equal((byte)98, under.Y);
            Assert.InRange(under.X, (byte)127, (byte)128);
            Assert.Equal(((byte)1, Sand, (byte)0, (byte)0, (byte)0), Voxel(parent, 10, 5, 10));
            Assert.Equal((byte)0, Voxel(parent, 10, 3, 10).Flags);
        }

        [Fact]
        public void ThinPlate_IsChosen_AndSetsItsNeighboursVertices()
        {
            // One voxel of Slate at grid y 48, nothing else: no Slate cube without an air change within 2 voxels, so the
            // plate is thin and every plate child is chosen (consistently). Then each chosen parent rewrites its 7 lower
            // neighbours from the chosen child's neighbours (part_0052.c:34107-34305): per axis (u + (c & 1)) / 2 where
            // o = 0, and for o = −1: (u + 1) / 2 for an even child, 1.0 for an odd one.
            var s = new Scene();
            s.Layer(48, 3, Slate, Centre, Surface, Centre);
            BlockData parent = s.Build();

            // Parent (10, 4, 10): its own write is the plane (≈128, 98, ≈128); the last parent to rewrite it is (11, 4, 11)
            // with o = (−1, 0, −1) and chosen child (23, 8, 23) (odd x, z): x = z = 1.0 → 170, y = (0.306 + 0) / 2 → 98.
            Assert.Equal(((byte)3, Slate, (byte)170, (byte)98, (byte)170), Voxel(parent, 10, 4, 10));
            // Parent (10, 3, 10) under it has no children with anything (vertex-only after the rewrites): last written by
            // (11, 4, 11) with o = (−1, −1, −1), neighbour child (22, 7, 22) unset (u = 0.5): x = z = 1.0, y = 0.75 → 149.
            Assert.Equal(((byte)2, (ushort)0, (byte)170, (byte)149, (byte)170), Voxel(parent, 10, 3, 10));
            Assert.Equal((byte)0, Voxel(parent, 10, 5, 10).Flags);
        }

        [Fact]
        public void ThinChild_GivesTheParentItsMaterial()
        {
            // The corner child is vertex-only (no material), the child above it a thin Slate plate: the plate child is
            // chosen, so the parent is Slate (solid) where the corner rule would have left nothing.
            var s = new Scene();
            s.Layer(48, 2, 0, Centre, Surface, Centre);
            s.Layer(49, 3, Slate, Centre, Surface, Centre);
            BlockData parent = s.Build();
            var p = Voxel(parent, 10, 4, 10);
            Assert.Equal((byte)3, p.Flags);
            Assert.Equal(Slate, p.Material);
        }

        [Fact]
        public void NoChildren_NothingWritten()
        {
            var s = new Scene();
            var parent = new BlockData();
            Assert.False(TerrainLod.UpdateBlockLod(s.Parent, s.Grid, parent, new TerrainLod.Scratch()));
            Assert.All(parent.Flags, f => Assert.Equal((byte)0, f));
        }

        // ------------------------------------------------------------------------------------------------ real data

        /// <summary>
        /// The level-2 build-layer cells around level-3 parent 301385405E01380E on landscape 464 (the dev world, 2026-11-01,
        /// TestPlayer2's Mesh Shapes cylinder; our own server data, from a copy of the landscape's cells.pack): 8 cells as
        /// {u64 cell id, i32 length, BlockData.Save bytes}.
        /// </summary>
        private static Dictionary<ulong, BlockData> CylinderCells()
        {
            var cells = new Dictionary<ulong, BlockData>();
            using var reader = new BinaryReader(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "TerrainLod", "cylinder-301385405E01380E.cells")));
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                ulong id = reader.ReadUInt64();
                var block = new BlockData();
                block.Load(reader.ReadBytes(reader.ReadInt32()));
                cells[id] = block;
            }
            return cells;
        }

        private const ulong CylinderParent = 0x301385405E01380E;

        [Fact]
        public void MeshShapeCylinder_Level3SurfaceKeepsRealVertices()
        {
            Dictionary<ulong, BlockData> cells = CylinderCells();
            Assert.Equal(8, cells.Count);
            var parent = new BlockData();
            Assert.True(TerrainLod.UpdateBlockLod(CylinderParent, TerrainLod.Grid(CylinderParent, id => cells.GetValueOrDefault(id)),
                parent, new TerrainLod.Scratch()));

            // Level-3 surface cubes inside the cell (a solid / not-solid change among the cube's 8 voxels; solid = bit 0
            // and a material other than 0, as the client's mesher): the client draws a cube without bit 1 at its centre,
            // a 0.8 m step. Count them, and the ones whose low corner is not solid (the faces the old rule lost).
            bool Solid(int x, int y, int z)
            {
                int i = TerrainEdits.VoxelIndex(x, y, z);
                return (parent.Flags[i] & 1) != 0 && parent.Materials[i] != 0;
            }
            int surface = 0, withVertex = 0, airCorner = 0, airCornerWithVertex = 0;
            for (int z = 0; z < 39; z++)
                for (int x = 0; x < 39; x++)
                    for (int y = 0; y < 39; y++)
                    {
                        bool first = Solid(x, y, z), change = false;
                        for (int d = 1; d < 8 && !change; d++)
                            change = Solid(x + (d & 1), y + (d >> 1 & 1), z + (d >> 2)) != first;
                        if (!change)
                            continue;
                        bool vertex = (parent.Flags[TerrainEdits.VoxelIndex(x, y, z)] & 2) != 0;
                        surface++;
                        withVertex += vertex ? 1 : 0;
                        if (!first)
                        {
                            airCorner++;
                            airCornerWithVertex += vertex ? 1 : 0;
                        }
                    }
            // 2026-11-01: 848 surface cubes, 524 of them with an air low corner (the corner/mass-point rule gave those no
            // vertex at all: drawn at their centres, the stairs); now every one has a vertex, 553 parents are vertex-only.
            Assert.Equal(848, surface);
            Assert.Equal(524, airCorner);
            Assert.Equal(surface, withVertex);
            Assert.Equal(airCorner, airCornerWithVertex);
            Assert.Equal(553, parent.Flags.Count(f => f == 2));

            // Regression: the port's own output for this parent (not a client capture: the client never runs this
            // function). Changes only with a deliberate change of the rule.
            using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            hash.AppendData(parent.Flags);
            hash.AppendData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(parent.Materials.AsSpan()));
            hash.AppendData(parent.Offsets);
            Assert.Equal("B112AD595DDCF2FFD56CE0BCA08CD20A", Convert.ToHexString(hash.GetHashAndReset())[..32]);
        }

        /// <summary>
        /// One quad among all 8 children gives a 2-row QEF, whose unpack takes U[2][2] from the buffer without writing it
        /// (FUN_142aa3140). The first solve and the corner re-solve have their own buffers, as in the client, and both are
        /// set at the start of each parent (Scratch.BeginParent): the vertex does not depend on what the worker solved
        /// before. The quad (found by a random search for one whose vertex moves with U[2][2]) lies just outside the cube
        /// at x 1.01-1.27, so it takes the corner re-solve.
        /// </summary>
        [Fact]
        public void SolveVertex_TwoRows_DoesNotDependOnEarlierSolves()
        {
            float[] quad =
            {
                1.01f, 0.05f, 0.36f, 1.27f, 0.70f, 0.24f, 1.15f, 0.78f, 0.70f,
                1.01f, 0.05f, 0.36f, 1.15f, 0.78f, 0.70f, 1.15f, 0.40f, 0.68f,
            };
            (double X, double Y, double Z) Solve(TerrainLod.Scratch s)
            {
                Array.Copy(quad, s.Tris, quad.Length);
                double x = 0.5, y = 0.5, z = 0.5;
                TerrainLod.SolveVertex(s, 2, ref x, ref y, ref z);
                return (x, y, z);
            }
            var fresh = Solve(new TerrainLod.Scratch());
            Assert.True(fresh.X > 1.0, $"x {fresh.X}: not the corner re-solve");

            // A worker that solved before: both buffers hold other values in U[2][2].
            var used = new TerrainLod.Scratch();
            used.Qef.U[8] = used.QefCorner.U[8] = 0.37;
            var stale = Solve(used);
            Assert.True(Math.Abs(stale.X - fresh.X) + Math.Abs(stale.Y - fresh.Y) + Math.Abs(stale.Z - fresh.Z) > 1e-3,
                "U[2][2] did not reach the 2-row solve (the test no longer covers it)");
            used.BeginParent();
            Assert.Equal(fresh, Solve(used));

            // Only the corner buffer stale: the re-solve reads its own buffer, not the first solve's.
            var corner = new TerrainLod.Scratch();
            corner.QefCorner.U[8] = 0.37;
            Assert.NotEqual(fresh, Solve(corner));
            corner.BeginParent();
            Assert.Equal(fresh, Solve(corner));
        }

        // -------------------------------------------------------------------------------------------------- refresh

        [Fact]
        public void Refresh_ReadsNeighbourCells_AndKeepsTheVersionOfAnUnchangedCopy()
        {
            uint zone = 9541;
            bool enabled = TerrainLod.Enabled;
            var notes = new List<(ulong Cell, uint Version)>();
            var hook = TerrainLod.CopiesChanged;
            // Parent P = level-3 (60000, 45, 60000); its children are level-2 x 120000-120001, it also reads x 119999 and
            // 120002. Cell A is a child with a box up to its +x face; cell B is the next cell along x (a child of P + 1).
            ulong a = TerrainStore.CellId(TerrainEdits.Level, 120001, 90, 120000), b = TerrainStore.CellId(TerrainEdits.Level, 120002, 90, 120000);
            ulong p = TerrainLod.Parent(a), next = TerrainLod.Parent(b);
            try
            {
                TerrainLod.Enabled = true;
                TerrainLod.CopiesChanged = (z, l, copies) => { if (z == zone) notes.AddRange(copies); };
                TerrainEdits.Write(a, Block(10, 39), zone);
                TerrainEdits.Write(b, Block(25, 30), zone);
                TerrainLod.RefreshNow(zone);
                uint version = TerrainLod.VersionOf(p, zone, TerrainEdits.BuildLayer);
                Assert.NotEqual(0u, version);
                Assert.Equal(0x40000000u, version & 0xE0000000u);   // the VoxelFarm port's version range (bit 29 clear)
                Assert.True(notes.Exists(n => n.Cell == p), "no note for that copy");
                Assert.True(notes.Exists(n => n.Cell == next), "no note for that copy");

                // More of B far from the shared face (x 20-24 of 40): P is rebuilt (it reads B) but comes out the same, so
                // it keeps its version and is not announced again; B's own parent changed.
                notes.Clear();
                TerrainEdits.Write(b, Block(20, 24), zone);
                TerrainLod.RefreshNow(zone);
                Assert.Equal(version, TerrainLod.VersionOf(p, zone, TerrainEdits.BuildLayer));
                Assert.False(notes.Exists(n => n.Cell == p), "unchanged copy announced");
                Assert.True(notes.Exists(n => n.Cell == next), "no note for that copy");

                // B solid at the face (x 0-5): the edges of A's top row at x 39 that crossed into air at B's x 0 no longer
                // cross, so P's quads there change and so does P (the corner/mass-point rule never looked into B).
                notes.Clear();
                TerrainEdits.Write(b, Block(0, 5), zone);
                TerrainLod.RefreshNow(zone);
                Assert.True(notes.Exists(n => n.Cell == p), "no note for that copy");
                uint afterFace = TerrainLod.VersionOf(p, zone, TerrainEdits.BuildLayer);
                Assert.NotEqual(version, afterFace);

                // A restart rebuilds every copy from scratch (copies are not saved). P's children did not change, only its
                // neighbour B, so a version from the children (the sum) would go back to the first one, for the new
                // content: a client that cached P before the edit would be told "up to date". From the content it stays.
                var before = Copies(zone);
                TerrainLod.RebuildAsAtStart(zone);
                Assert.Equal(afterFace, TerrainLod.VersionOf(p, zone, TerrainEdits.BuildLayer));
                Assert.Equal(before, Copies(zone));

                // A empty: P has no voxels left and is kept as an empty copy at a new version (the client is sent an
                // empty layer). The level-4 parent of P and P + 1 reads P as missing, as after a restart (no empty copy
                // is built there), so every copy left after a restart has the version it had.
                TerrainEdits.ClearCell(a, zone);
                TerrainLod.RefreshNow(zone);
                uint empty = TerrainLod.VersionOf(p, zone, TerrainEdits.BuildLayer);
                Assert.NotEqual(0u, empty);
                Assert.NotEqual(afterFace, empty);
                Assert.Equal(0x40000000u, empty & 0xE0000000u);
                before = Copies(zone);
                Assert.Contains(TerrainLod.Parent(p), before.Keys);
                TerrainLod.RebuildAsAtStart(zone);
                var after = Copies(zone);
                Assert.DoesNotContain(p, after.Keys);
                Assert.Equal(new SortedDictionary<ulong, uint>(before.Where(c => c.Key != p).ToDictionary(c => c.Key, c => c.Value)), after);
            }
            finally
            {
                TerrainLod.Enabled = enabled;
                TerrainLod.CopiesChanged = hook;
                TerrainEdits.ClearCell(a, zone);
                TerrainEdits.ClearCell(b, zone);
                TerrainEdits.Flush();
                TerrainLod.RebuildAsAtStart(zone);
            }
        }

        private static SortedDictionary<ulong, uint> Copies(uint zone)
            => new(TerrainLod.Entries(zone, TerrainEdits.BuildLayer).ToDictionary(c => c.Cell, c => c.Version));

        /// <summary>
        /// The corner / mass-point rule's versions were (sum of the children's versions) | 0x20000000. Build-layer cells
        /// carry the client's CRC as their version (TerrainEdits.Commit), so a sum often has bits 29 and 30 both set, and
        /// sum | 0x40000000 would have been the same number: the client would have kept its blocky copy. Content versions
        /// have bit 29 clear, so they differ from every version of that rule.
        /// </summary>
        [Fact]
        public void ContentVersion_IsOutsideTheCornerRulesRange_AndNeverRepeatsThePrevious()
        {
            var random = new Random(77);
            for (int trial = 0; trial < 2000; trial++)
            {
                var saved = new byte[1 + random.Next(300)];
                random.NextBytes(saved);
                uint version = TerrainLod.ContentVersion(saved, 0);
                Assert.Equal(0x40000000u, version & 0xE0000000u);
                Assert.Equal(version, TerrainLod.ContentVersion((byte[])saved.Clone(), 0));   // the same bytes, the same version
                uint sum = (uint)random.Next() | 0x60000000u | (trial % 2 == 0 ? 0x80000000u : 0);
                Assert.Equal(sum | 0x20000000u, sum | 0x40000000u);   // why another OR bit was not enough
                Assert.NotEqual(sum | 0x20000000u, version);
                // A CRC equal to the copy it replaces (different bytes) still moves the version, inside the range.
                uint moved = TerrainLod.ContentVersion(saved, version);
                Assert.NotEqual(version, moved);
                Assert.Equal(0x40000000u, moved & 0xE0000000u);
            }
        }

        /// <summary>
        /// The output does not depend on the worker split: with 1 worker every parent runs on one Scratch after the ones
        /// before it, with 3 they are spread over three (TerrainLod.Workers, ChunkSize 32).
        /// </summary>
        [Fact]
        public void Refresh_SameCopiesOnOneAndThreeWorkers()
        {
            uint zone = 9542;
            bool enabled = TerrainLod.Enabled;
            int workers = TerrainLod.Workers;
            var written = new List<ulong>();
            try
            {
                TerrainLod.Enabled = true;
                // 40 cells in a row along x, one in each of 40 neighbouring level-3 parents (each reads the cells next to
                // it): 40 copies, and the ~240 dirty parents are 3 or more chunks of 32.
                for (uint k = 0; k < 40; k++)
                {
                    ulong id = TerrainStore.CellId(TerrainEdits.Level, 130001 + 2 * k, 90, 130000);
                    written.Add(id);
                    TerrainEdits.Write(id, k % 2 == 0 ? Block(10, 39) : Block(0, 30), zone);
                }
                TerrainLod.Workers = 1;
                TerrainLod.RebuildAsAtStart(zone);
                var one = Copies(zone);
                Assert.True(one.Count >= 40, $"only {one.Count} copies");
                TerrainLod.Workers = 3;
                TerrainLod.RebuildAsAtStart(zone);
                Assert.Equal(one, Copies(zone));
            }
            finally
            {
                TerrainLod.Workers = workers;
                TerrainLod.Enabled = enabled;
                foreach (ulong id in written)
                    TerrainEdits.ClearCell(id, zone);
                TerrainEdits.Flush();
                TerrainLod.RebuildAsAtStart(zone);
            }
        }

        /// <summary>A sand box x0 .. x1 (y 10-20, z 10-20) with a vertex on its top layer.</summary>
        private static List<TerrainEdits.VoxelValue> Block(int x0, int x1)
        {
            var values = new List<TerrainEdits.VoxelValue>();
            for (int x = x0; x <= x1; x++)
                for (int y = 10; y <= 20; y++)
                    for (int z = 10; z <= 20; z++)
                        values.Add(y == 20
                            ? new(TerrainEdits.VoxelIndex(x, y, z), 3, Sand, Centre, Surface, Centre)
                            : new(TerrainEdits.VoxelIndex(x, y, z), 1, Sand, 0, 0, 0));
            return values;
        }
    }
}
