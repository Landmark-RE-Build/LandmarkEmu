using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Game.Terrain;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// Where placed voxels end up (user report 2026-10-21: "after placing voxels they are rendered 1 or 2 blocks offset
    /// from where the preview shows").
    /// <para>
    /// <b>Client-made edits</b> (Add / Remove / Fill / Line / Smooth / Paint / Mesh Shapes: every brush sends its own
    /// edited cell in ApplyDiffRequest, docs/terrain.md "Building diffs"): the blob is built the way the client sends it
    /// (8000-byte changed mask + SerializeBlockData format 2 in the A1B2C3D4 codec), read with the server's own readers
    /// (TerrainStore.Unpack, BlockData.Deserialize), stored with the edit path (TerrainEdits.Prepare / Commit, what
    /// LocalEnvironmentServer.ApplyEdit runs) and read back from the CellsUpdate the server sends (TerrainEdits.BuildUpdate).
    /// Every changed voxel must come back in the same cell at the same index with the same flags, material and vertex:
    /// the server moves nothing a brush places. (The builder is not even sent their own edit back:
    /// Building.Handle / ZoneTerrain.Broadcast leave them out.)
    /// </para>
    /// <para>
    /// <b>Server-made placements</b> (paste / design placement, off-grid paste): the server places its copy itself, so the
    /// result is checked with a copy of the client's mesher rule (ContourEx FUN_142ac4590, docs/terrain.md "How the client
    /// draws a dig": for every edge whose two voxels differ, the vertices of the four cubes around it, a cube's vertex
    /// being its corner voxel + offset or the cube centre) against the copy's own mesh moved by the paste's turn.
    /// </para>
    /// </summary>
    public class BuildPlacementTests : IDisposable
    {
        private static int nextZone = 9300;
        private readonly uint zone = (uint)System.Threading.Interlocked.Increment(ref nextZone);
        private readonly List<ulong> touched = new();
        private readonly bool keepVertexOnly = BlockData.KeepVertexOnly;

        /// <summary>The servers keep the client's vertex-only voxels (KeepVertexOnlyVoxels, default true: HostedService).</summary>
        public BuildPlacementTests() => BlockData.KeepVertexOnly = true;

        public void Dispose()
        {
            foreach (ulong cell in touched.Distinct())
                TerrainEdits.ClearCell(cell, zone);
            BlockData.KeepVertexOnly = keepVertexOnly;
        }

        private static readonly byte[] All = Enumerable.Repeat((byte)0xFF, BlockData.Count / 8).ToArray();

        private static void Mark(byte[] mask, int index) => mask[index >> 3] |= (byte)(0x80 >> (index & 7));

        private static bool Marked(byte[] mask, int index) => (mask[index >> 3] & (0x80 >> (index & 7))) != 0;

        private static byte OffsetByte(double u) => TerrainEdits.OffsetByte(u);

        // ---- client-made edits ---------------------------------------------------------------------------------------

        /// <summary>One brush edit as the client builds it: the cell after the edit and the changed mask.</summary>
        private sealed class ClientEdit
        {
            public readonly BlockData Cell = new();
            public readonly byte[] Changed = new byte[BlockData.Count / 8];

            public void Solid(int x, int y, int z, ushort material, double ux = 0.5, double uy = 0.5, double uz = 0.5)
            {
                int i = BlockData.Index(x, y, z);
                Cell.Flags[i] = 3;
                Cell.Materials[i] = material;
                Cell.Offsets[i * 3] = OffsetByte(ux);
                Cell.Offsets[i * 3 + 1] = OffsetByte(uy);
                Cell.Offsets[i * 3 + 2] = OffsetByte(uz);
                Mark(Changed, i);
            }

            public void SolidNoVertex(int x, int y, int z, ushort material)
            {
                int i = BlockData.Index(x, y, z);
                Cell.Flags[i] = 1;
                Cell.Materials[i] = material;
                Mark(Changed, i);
            }

            public void Air(int x, int y, int z)
            {
                int i = BlockData.Index(x, y, z);
                Cell.Flags[i] = 1;
                Cell.Materials[i] = 0;
                Mark(Changed, i);
            }

            /// <summary>A vertex-only voxel (flag 2): the generated side of a smooth brush's surface.</summary>
            public void VertexOnly(int x, int y, int z, double ux, double uy, double uz)
            {
                int i = BlockData.Index(x, y, z);
                Cell.Flags[i] = 2;
                Cell.Offsets[i * 3] = OffsetByte(ux);
                Cell.Offsets[i * 3 + 1] = OffsetByte(uy);
                Cell.Offsets[i * 3 + 2] = OffsetByte(uz);
                Mark(Changed, i);
            }

            /// <summary>The ApplyDiff cell blob as the client packs it (FUN_143100cd0 → codec around mask + cell).</summary>
            public byte[] Packed() => TerrainStore.Pack(Changed.Concat(Cell.Serialize()).ToArray());
        }

        /// <summary>
        /// Runs an edit through the server (Building.Handle's reading, TerrainEdits.Prepare / Commit) and returns the
        /// cell the server then sends (layer 1 of its CellsUpdate), decoded as the client decodes it.
        /// </summary>
        private BlockData ThroughServer(ulong cell, ClientEdit edit)
        {
            byte[] packed = edit.Packed();
            byte[] raw = TerrainStore.Unpack(packed, 0, packed.Length);
            var changed = new byte[BlockData.Count / 8];
            Array.Copy(raw, 0, changed, 0, changed.Length);
            Assert.True(raw.Length > changed.Length);
            BlockData result = BlockData.Deserialize(raw, changed.Length);
            var delta = new Dictionary<ushort, int>();
            (int[] indices, _) = TerrainEdits.Prepare(cell, changed, result, zone, delta);
            TerrainEdits.Commit(cell, indices, result, zone, owner: 1);
            touched.Add(cell);
            return SentLayer(cell, TerrainEdits.BuildLayer);
        }

        /// <summary>One layer of the CellsUpdate the server builds for a cell (TerrainStore.BuildBlocksUpdate layout).</summary>
        private BlockData SentLayer(ulong cell, uint wanted)
        {
            byte[] update = TerrainEdits.BuildUpdate(cell, zone, always: true);
            using var r = new BinaryReader(new MemoryStream(update));
            r.ReadUInt16();
            Assert.Equal(1u, r.ReadUInt32());
            Assert.Equal(cell, r.ReadUInt64());   // the same cell the edit named
            Assert.Equal(0u, r.ReadUInt32());     // version 0 = block data
            uint layers = r.ReadUInt32();
            for (uint l = 0; l < layers; l++)
            {
                uint layer = r.ReadUInt32();
                r.ReadUInt32();
                r.ReadByte();
                byte[] blob = r.ReadBytes((int)r.ReadUInt32());
                if (layer == wanted)
                    return BlockData.Deserialize(TerrainStore.Unpack(blob, 0, blob.Length));
            }
            throw new InvalidDataException($"layer {wanted} not sent");
        }

        /// <summary>Every changed voxel back in the same place with the same value (flags, material, vertex).</summary>
        private static void AssertSamePlace(ClientEdit edit, BlockData sent)
        {
            int checkedVoxels = 0;
            for (int i = 0; i < BlockData.Count; i++)
            {
                if (!Marked(edit.Changed, i))
                {
                    Assert.Equal(0, sent.Flags[i]);   // nothing appears where the client changed nothing
                    continue;
                }
                byte f = edit.Cell.Flags[i];
                Assert.Equal(f & 3, sent.Flags[i] & 3);
                if ((f & 1) != 0)
                    Assert.Equal(edit.Cell.Materials[i], sent.Materials[i]);
                if ((f & 2) != 0)
                    for (int k = 0; k < 3; k++)
                        Assert.Equal(edit.Cell.Offsets[i * 3 + k], sent.Offsets[i * 3 + k]);
                checkedVoxels++;
            }
            Assert.True(checkedVoxels > 0);
        }

        private static ulong Cell(uint x, uint y, uint z) => TerrainStore.CellId(TerrainEdits.Level, x, y, z);

        [Fact]
        public void AddBox_StaysWhereTheClientPutIt()
        {
            // Add tool, cube brush: solid voxels with centred vertices (flag 0x20 on the wire), up to the cell's edges.
            var edit = new ClientEdit();
            for (int x = 0; x < 4; x++)
                for (int y = 36; y < 40; y++)
                    for (int z = 37; z < 40; z++)
                        edit.Solid(x, y, z, 79);
            ulong cell = Cell(159900, 130, 159800);
            AssertSamePlace(edit, ThroughServer(cell, edit));
        }

        [Fact]
        public void AddSphere_AndLine_KeepTheirVertices()
        {
            // Add tool, sphere brush / Line tool: solid voxels with surface vertices plus vertex-only voxels on the
            // generated side (what keeps a sphere round and a slope smooth).
            var edit = new ClientEdit();
            var rng = new Random(7);
            for (int x = 10; x < 20; x++)
                for (int z = 5; z < 9; z++)
                {
                    int top = 10 + (x - 10) / 2;   // a 1:2 slope
                    for (int y = 8; y <= top; y++)
                        edit.Solid(x, y, z, 26, rng.NextDouble(), 0.2 + (x % 2) * 0.5, rng.NextDouble());
                    edit.VertexOnly(x, top + 1, z, 0.5, rng.NextDouble() * 0.9, 0.5);
                }
            ulong cell = Cell(159901, 129, 159801);
            AssertSamePlace(edit, ThroughServer(cell, edit));
        }

        [Fact]
        public void FillAndMeshShape_LandAtTheirIndices()
        {
            // Fill brush and a Mesh Shape (voxelised by the client): solid voxels some without a vertex, odd offsets
            // (clamped −1..2), the y-fastest index order.
            var edit = new ClientEdit();
            for (int i = 0; i < 300; i++)
            {
                int x = (i * 7) % 40, y = (i * 13) % 40, z = (i * 3) % 40;
                if (i % 3 == 0)
                    edit.SolidNoVertex(x, y, z, 53);
                else
                    edit.Solid(x, y, z, (ushort)(53 + i % 5), -1 + (i % 7) * 0.5, 2 - (i % 4) * 0.7, (i % 9) / 9.0);
            }
            ulong cell = Cell(159902, 131, 159802);
            AssertSamePlace(edit, ThroughServer(cell, edit));
        }

        [Fact]
        public void RemoveSmoothPaint_ChangeOnlyWhatTheClientChanged()
        {
            ulong cell = Cell(159903, 130, 159803);
            var add = new ClientEdit();
            for (int x = 0; x < 6; x++)
                for (int y = 0; y < 6; y++)
                    add.Solid(x, y, 20, 79);
            ThroughServer(cell, add);

            // Remove: carved air (material 0) and the vertices the client moved around the hole.
            var remove = new ClientEdit();
            remove.Air(2, 2, 20);
            remove.Air(3, 2, 20);
            remove.VertexOnly(2, 3, 20, 0.5, 0.1, 0.5);
            BlockData afterRemove = ThroughServer(cell, remove);
            AssertSamePlace(Merge(add, remove), afterRemove);

            // Smooth: only vertices change (solid voxels with new offsets).
            var smooth = new ClientEdit();
            smooth.Solid(5, 5, 20, 79, 0.1, 0.9, 0.5);
            smooth.Solid(5, 4, 20, 79, 0.2, 0.8, 0.5);
            BlockData afterSmooth = ThroughServer(cell, smooth);
            AssertSamePlace(Merge(Merge(add, remove), smooth), afterSmooth);

            // Paint: material only.
            var paint = new ClientEdit();
            paint.Solid(0, 0, 20, 26);
            BlockData afterPaint = ThroughServer(cell, paint);
            AssertSamePlace(Merge(Merge(Merge(add, remove), smooth), paint), afterPaint);
        }

        /// <summary>The layer after <paramref name="later"/> on top of <paramref name="earlier"/> (as the server keeps it).</summary>
        private static ClientEdit Merge(ClientEdit earlier, ClientEdit later)
        {
            var both = new ClientEdit();
            foreach (ClientEdit e in new[] { earlier, later })
                for (int i = 0; i < BlockData.Count; i++)
                    if (Marked(e.Changed, i))
                    {
                        both.Cell.Flags[i] = e.Cell.Flags[i];
                        both.Cell.Materials[i] = e.Cell.Materials[i];
                        for (int k = 0; k < 3; k++)
                            both.Cell.Offsets[i * 3 + k] = e.Cell.Offsets[i * 3 + k];
                        Mark(both.Changed, i);
                    }
            return both;
        }

        [Fact]
        public void HealTool_ClearsExactlyTheMaskedVoxels()
        {
            // The Heal tool sends only the mask (Building.Heal → ClearCells): those voxels, and no others, go back to the
            // generated ground.
            ulong cell = Cell(159904, 130, 159804);
            var add = new ClientEdit();
            for (int x = 10; x < 14; x++)
                add.Solid(x, 10, 10, 79);
            ThroughServer(cell, add);
            var heal = new byte[BlockData.Count / 8];
            Mark(heal, BlockData.Index(11, 10, 10));
            List<TerrainEdits.VoxelValue> cleared = EnvironmentMasks.Cleared(TerrainEdits.Read(cell, heal, zone).Where(v => v.Flags != 0));
            TerrainEdits.Write(cell, cleared, zone);
            BlockData sent = SentLayer(cell, TerrainEdits.BuildLayer);
            for (int x = 10; x < 14; x++)
                Assert.Equal(x == 11 ? 0 : 3, sent.Flags[BlockData.Index(x, 10, 10)]);
        }

        // ---- server-made placements: paste / design placement ---------------------------------------------------------

        /// <summary>A voxel as the mesher reads it: solid, and the vertex of the cube it is the corner of (null = none).</summary>
        private readonly record struct Sample(bool Solid, (double X, double Y, double Z)? Vertex);

        /// <summary>
        /// The vertices the client's quads use around one edge (voxel a → a + axis), each cube's vertex or its centre
        /// (ContourEx FUN_142ac4590, table DAT_144a608f0: the cubes at 0 / −1 on the other two axes).
        /// </summary>
        private static List<(double X, double Y, double Z)> EdgeQuad(Func<long, long, long, Sample> at, long x, long y, long z, int axis)
        {
            var quad = new List<(double, double, double)>();
            for (int a = 0; a > -2; a--)
                for (int b = 0; b > -2; b--)
                {
                    (long cx, long cy, long cz) = axis switch { 0 => (x, y + a, z + b), 1 => (x + a, y, z + b), _ => (x + a, y + b, z) };
                    quad.Add(at(cx, cy, cz).Vertex is { } v ? (cx + v.X, cy + v.Y, cz + v.Z) : (cx + 0.5, cy + 0.5, cz + 0.5));
                }
            return quad;
        }

        private static Sample FromBlock(BlockData block, int index)
            => new((block.Flags[index] & 1) != 0 && block.Materials[index] != 0,
                (block.Flags[index] & 2) != 0
                    ? (block.Offsets[index * 3] / 85.0 - 1, block.Offsets[index * 3 + 1] / 85.0 - 1, block.Offsets[index * 3 + 2] / 85.0 - 1)
                    : null);

        /// <summary>A smooth shape in a clipboard: a sloped wedge with a vertex on every surface cube (the copy of a Line / smoothed build).</summary>
        private static Building.Clipboard Wedge(out uint sx, out uint sy, out uint sz)
        {
            sx = 12; sy = 8; sz = 6;
            var block = new BlockData();
            // Density: solid below the plane y = 1 + 0.45·x + 0.2·z (voxels), inside 1..10 × 1..6 × 1..4.
            double D(int x, int y, int z) => 1 + 0.45 * x + 0.2 * z - y;
            bool In(int x, int y, int z) => x >= 1 && x <= 10 && y >= 1 && z >= 1 && z <= 4 && D(x, y, z) >= 0;
            for (int z = 0; z <= (int)sz; z++)
                for (int x = 0; x <= (int)sx; x++)
                    for (int y = 0; y <= (int)sy; y++)
                    {
                        int i = BlockData.Index(x, y, z);
                        if (In(x, y, z))
                        {
                            block.Flags[i] = 1;
                            block.Materials[i] = 26;
                        }
                    }
            // Every cube with mixed corners gets the mass point of its edge crossings (the client's smooth surface):
            // on solid corners as part of the voxel, on air corners as vertex-only voxels.
            for (int z = 0; z < (int)sz; z++)
                for (int x = 0; x < (int)sx; x++)
                    for (int y = 0; y < (int)sy; y++)
                    {
                        double mx = 0, my = 0, mz = 0;
                        int n = 0;
                        for (int axis = 0; axis < 3; axis++)
                            for (int a = 0; a < 2; a++)
                                for (int b = 0; b < 2; b++)
                                {
                                    (int x0, int y0, int z0) = axis switch { 0 => (0, a, b), 1 => (a, 0, b), _ => (a, b, 0) };
                                    (int x1, int y1, int z1) = axis switch { 0 => (1, a, b), 1 => (a, 1, b), _ => (a, b, 1) };
                                    bool s0 = In(x + x0, y + y0, z + z0), s1 = In(x + x1, y + y1, z + z1);
                                    if (s0 == s1)
                                        continue;
                                    double t = 0.5;
                                    if (axis == 1 && x + x0 >= 1 && x + x0 <= 10 && z + z0 >= 1 && z + z0 <= 4)
                                        t = Math.Clamp(D(x + x0, y + y0, z + z0), 0, 1);   // the slope's own crossing
                                    mx += x0 + (x1 - x0) * t;
                                    my += y0 + (y1 - y0) * t;
                                    mz += z0 + (z1 - z0) * t;
                                    n++;
                                }
                        if (n == 0)
                            continue;
                        int i = BlockData.Index(x, y, z);
                        block.Flags[i] |= 2;
                        block.Offsets[i * 3] = OffsetByte(mx / n);
                        block.Offsets[i * 3 + 1] = OffsetByte(my / n);
                        block.Offsets[i * 3 + 2] = OffsetByte(mz / n);
                    }
            return new Building.Clipboard(block, sx, sy, sz);
        }

        private static readonly int[] Identity = { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
        private static readonly int[] Yaw180 = { -1, 0, 0, 0, 0, 1, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1 };
        private static readonly int[] Yaw90 = { 0, 0, 1, 0, 0, 1, 0, 0, -1, 0, 0, 0, 0, 0, 0, 1 };
        private static readonly int[] MirrorX = { -1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };

        /// <summary>
        /// Pastes the wedge with a turn (base as Building.Paste computes it) and compares the mesh the client draws from the
        /// result with the copy's own mesh moved by the same turn (corner convention: point p → base + M·p). Returns the
        /// largest vertex distance (voxels) and how many solid voxels are not where the turn puts them.
        /// </summary>
        private static (double MaxError, int Misplaced, int Edges) PasteAndCompare(int[] m, long[] origin)
        {
            Building.Clipboard clip = Wedge(out uint sx, out uint sy, out uint sz);
            int[] size = { (int)sx, (int)sy, (int)sz };
            var shift = new int[3];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    shift[i] -= Math.Min(0, m[i * 4 + j] * size[j]);
            long[] b = { origin[0] + shift[0], origin[1] + shift[1], origin[2] + shift[2] };
            var pasted = Building.TurnedVoxels(clip, m, b[0], b[1], b[2]);

            var target = new Dictionary<(long, long, long), Sample>();
            foreach ((ulong cell, List<TerrainEdits.VoxelValue> values) in pasted)
            {
                (_, uint cx, uint cy, uint cz) = TerrainStore.Decode(cell);
                foreach (TerrainEdits.VoxelValue v in values)
                {
                    int y = v.Index % 40, x = v.Index / 40 % 40, z = v.Index / 1600;
                    target[(cx * 40L + x, cy * 40L + y, cz * 40L + z)] = new Sample((v.Flags & 1) != 0 && v.Material != 0,
                        (v.Flags & 2) != 0 ? (v.O0 / 85.0 - 1, v.O1 / 85.0 - 1, v.O2 / 85.0 - 1) : null);
                }
            }
            Sample Target(long x, long y, long z) => target.TryGetValue((x, y, z), out Sample s) ? s : default;
            Sample Source(long x, long y, long z)
                => x >= 0 && y >= 0 && z >= 0 && x <= size[0] && y <= size[1] && z <= size[2] && clip.At((int)x, (int)y, (int)z) is (BlockData blk, int i)
                    ? FromBlock(blk, i) : default;
            (double, double, double) Move(double x, double y, double z)
                => (b[0] + m[0] * x + m[1] * y + m[2] * z, b[1] + m[4] * x + m[5] * y + m[6] * z, b[2] + m[8] * x + m[9] * y + m[10] * z);

            int misplaced = 0, edges = 0;
            double max = 0;
            for (long z = -1; z <= size[2] + 1; z++)
                for (long x = -1; x <= size[0] + 1; x++)
                    for (long y = -1; y <= size[1] + 1; y++)
                    {
                        bool here = Source(x, y, z).Solid;
                        var (tx, ty, tz) = Move(x, y, z);
                        if (here != Target((long)tx, (long)ty, (long)tz).Solid)
                            misplaced++;
                        for (int axis = 0; axis < 3; axis++)
                        {
                            (long nx, long ny, long nz) = axis switch { 0 => (x + 1, y, z), 1 => (x, y + 1, z), _ => (x, y, z + 1) };
                            if (Source(nx, ny, nz).Solid == here)
                                continue;
                            edges++;
                            // The same edge in the paste: its lower end is the moved end with the smaller coordinate.
                            var (ux, uy, uz) = Move(nx, ny, nz);
                            long ex = (long)Math.Min(tx, ux), ey = (long)Math.Min(ty, uy), ez = (long)Math.Min(tz, uz);
                            int taxis = tx != ux ? 0 : ty != uy ? 1 : 2;
                            var expected = EdgeQuad(Source, x, y, z, axis).Select(p => Move(p.X, p.Y, p.Z)).ToList();
                            var got = EdgeQuad(Target, ex, ey, ez, taxis);
                            foreach (var e in expected)
                                max = Math.Max(max, got.Min(g => Math.Sqrt((g.X - e.Item1) * (g.X - e.Item1) + (g.Y - e.Item2) * (g.Y - e.Item2)
                                    + (g.Z - e.Item3) * (g.Z - e.Item3))));
                        }
                    }
            return (max, misplaced, edges);
        }

        private static readonly long[] Origin = { 159900L * 40 + 10, 130L * 40 + 5, 159800L * 40 + 3 };

        [Fact]
        public void Paste_Unturned_MatchesTheCopy()
        {
            (double max, int misplaced, int edges) = PasteAndCompare(Identity, Origin);
            Assert.True(edges > 100);
            Assert.Equal(0, misplaced);
            Assert.True(max < 0.03, $"vertices up to {max:0.###} voxels off");
        }

        [Theory]
        [InlineData("yaw180")]
        [InlineData("yaw90")]
        [InlineData("mirror")]
        public void Paste_Turned_SurfaceTurnsWithIt(string turn)
        {
            int[] m = turn switch { "yaw180" => Yaw180, "yaw90" => Yaw90, _ => MirrorX };
            (double max, int misplaced, int edges) = PasteAndCompare(m, Origin);
            Assert.True(edges > 100);
            Assert.Equal(0, misplaced);
            // Before 2026-10-21 the vertex of each cube stayed with its corner voxel: one voxel off along the flipped axes.
            Assert.True(max < 0.03, $"vertices up to {max:0.###} voxels off");
        }

        [Fact]
        public void OffGridPaste_MatchesTheOnGridPaste()
        {
            // The off-grid paste at a whole-voxel position with a quarter-turn transform must place what the on-grid paste
            // places (clipboard point l lands at origin + l·R; grid point s at base + M·s).
            foreach (int[] m in new[] { Identity, Yaw180, Yaw90, MirrorX })
            {
                Building.Clipboard clip = Wedge(out uint sx, out uint sy, out uint sz);
                int[] size = { (int)sx, (int)sy, (int)sz };
                var shift = new int[3];
                for (int i = 0; i < 3; i++)
                    for (int j = 0; j < 3; j++)
                        shift[i] -= Math.Min(0, m[i * 4 + j] * size[j]);
                long[] b = { Origin[0] + shift[0], Origin[1] + shift[1], Origin[2] + shift[2] };
                var onGrid = Building.TurnedVoxels(clip, m, b[0], b[1], b[2]);
                // R's rows are where the clipboard axes go: row j = column j of M.
                float[] t = { m[0], m[4], m[8], 0, m[1], m[5], m[9], 0, m[2], m[6], m[10], 0, 0, 0, 0, 1 };
                double v = TerrainEdits.VoxelSize;
                var offGrid = Building.OffGridVoxels(clip, t, b[0] * v, b[1] * v, b[2] * v, out _, out _, out _, out _, out _, out _);
                Assert.NotNull(offGrid);
                var solidOn = onGrid.SelectMany(c => c.Value.Where(x => (x.Flags & 1) != 0).Select(x => (c.Key, x))).ToDictionary(e => (e.Key, e.x.Index), e => e.x);
                var solidOff = offGrid.SelectMany(c => c.Value.Select(x => (c.Key, x))).ToDictionary(e => (e.Key, e.x.Index), e => e.x);
                Assert.Equal(solidOn.Count, solidOff.Count);
                foreach (var (key, on) in solidOn)
                {
                    Assert.True(solidOff.TryGetValue(key, out var off), $"turn [{string.Join(",", m)}]: voxel {key} missing");
                    Assert.Equal(on.Material, off.Material);
                    Assert.Equal(on.Flags, off.Flags);
                    Assert.True(Math.Abs(on.O0 - off.O0) <= 1 && Math.Abs(on.O1 - off.O1) <= 1 && Math.Abs(on.O2 - off.O2) <= 1,
                        $"turn [{string.Join(",", m)}]: vertex of {key} {off.O0},{off.O1},{off.O2} vs {on.O0},{on.O1},{on.O2}");
                }
            }
        }
    }
}
