using LandmarkEmulator.WorldServer.Game.Terrain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// The shape a pick swing leaves (TerrainEdits.Sphere): the voxels a dig writes, meshed the way the 0.695 client meshes
    /// them, against the ideal hole (the ground minus the sphere).
    /// <para>
    /// <see cref="ClientView"/> is a small copy of the client's own pipeline (docs/terrain.md "How the client draws a
    /// dig"): the generated ground's samples sit at the voxel corners (ContinentMapHeightmapTerrain::getContourDataRegular
    /// FUN_1440873e0: sample j at cell corner + j × 0.4 m, solid up to the height), every cube [j, j+1]³ with a sign change
    /// gets the mass point of its edge crossings as its vertex (SmoothContourData FUN_144086430), the block layers
    /// replace material and vertex per voxel (FUN_1440498d0, getBlockContourData FUN_142aa8830), and the mesher
    /// (ContourEx FUN_142ac4590) joins, for every edge whose two samples differ, the vertices of the four cubes around it
    /// (cube + offset, 0.5 when the voxel has none). The seam blend the client adds where block voxels meet generated
    /// ones (FUN_142aa6030 → QEF) is left out: it only moves rim vertices towards the generated planes.
    /// </para>
    /// </summary>
    public class DigShapeTests : IDisposable
    {
        private const double VS = TerrainEdits.VoxelSize;
        private static int nextZone = 9100;
        private readonly uint zone = (uint)System.Threading.Interlocked.Increment(ref nextZone);
        private readonly List<ulong> touched = new();

        public void Dispose()
        {
            foreach (ulong cell in touched.Distinct())
                TerrainEdits.ClearCell(cell, zone);
            TerrainHeights.TestGround = null;
        }

        /// <summary>A ground of the test's own: local height from local x / z (metres).</summary>
        private void Ground(Func<float, float, float> height)
            => TerrainHeights.TestGround = (x, z, z0) => z0 == zone ? height(x, z) : null;

        private static double GlobalGroundY(Func<float, float, float> height, double gx, double gz)
            => height((float)((gx - TerrainStore.ZoneOrigin.X) / 10), (float)((gz - TerrainStore.ZoneOrigin.Z) / 10)) * 10
               + TerrainStore.ZoneOrigin.Y;

        /// <summary>A point in global voxel units (doubles: a float holds a global voxel coordinate only to half a voxel).</summary>
        private readonly record struct P(double X, double Y, double Z);

        /// <summary>What the client composites and meshes around a dig (see the class remarks).</summary>
        private sealed class ClientView
        {
            private readonly uint zone;
            private readonly Func<double, double, double> groundY;   // global dm x, z → global dm y
            private readonly Dictionary<(ulong, uint), TerrainEdits.VoxelValue[]> cells = new();
            private static readonly byte[] all = Enumerable.Repeat((byte)0xFF, BlockData.Count / 8).ToArray();

            public ClientView(uint zone, Func<double, double, double> groundY)
            {
                this.zone = zone;
                this.groundY = groundY;
            }

            private TerrainEdits.VoxelValue Voxel(long vx, long vy, long vz, uint layer)
            {
                long cx = (long)Math.Floor(vx / 40.0), cy = (long)Math.Floor(vy / 40.0), cz = (long)Math.Floor(vz / 40.0);
                ulong id = TerrainStore.CellId(TerrainEdits.Level, (uint)cx, (uint)cy, (uint)cz);
                if (!cells.TryGetValue((id, layer), out var values))
                    cells[(id, layer)] = values = TerrainEdits.Read(id, all, zone, layer).ToArray();
                return values[TerrainEdits.VoxelIndex((int)(vx - cx * 40), (int)(vy - cy * 40), (int)(vz - cz * 40))];
            }

            /// <summary>Generated density at a sample: height − y (dm), solid at 0 or more.</summary>
            public double Density(long vx, long vy, long vz) => groundY(vx * VS, vz * VS) - vy * VS;

            public bool Solid(long vx, long vy, long vz)
            {
                for (uint layer = TerrainEdits.DigLayer + 1; layer-- > 0;)
                {
                    TerrainEdits.VoxelValue v = Voxel(vx, vy, vz, layer);
                    if ((v.Flags & 1) != 0)
                        return v.Material != 0;
                }
                return Density(vx, vy, vz) >= 0;
            }

            /// <summary>The cube's vertex in voxel units (global), and whether anything gave it one (else the centre).</summary>
            public (P Position, bool Placed, bool FromBlocks) Vertex(long vx, long vy, long vz)
            {
                for (uint layer = TerrainEdits.DigLayer + 1; layer-- > 0;)
                {
                    TerrainEdits.VoxelValue v = Voxel(vx, vy, vz, layer);
                    if ((v.Flags & 2) != 0)
                        return (new P(vx + v.O0 / 85.0 - 1, vy + v.O1 / 85.0 - 1, vz + v.O2 / 85.0 - 1), true, true);
                }
                // The generator's vertex (SmoothContourData): mass point of the linear crossings on the cube's 12 edges.
                double sx = 0, sy = 0, sz = 0;
                int n = 0;
                for (int axis = 0; axis < 3; axis++)
                    for (int a = 0; a < 2; a++)
                        for (int b = 0; b < 2; b++)
                        {
                            (int x0, int y0, int z0) = axis switch { 0 => (0, a, b), 1 => (a, 0, b), _ => (a, b, 0) };
                            (int x1, int y1, int z1) = axis switch { 0 => (1, a, b), 1 => (a, 1, b), _ => (a, b, 1) };
                            double d0 = Density(vx + x0, vy + y0, vz + z0), d1 = Density(vx + x1, vy + y1, vz + z1);
                            if (d0 * d1 > 0)
                                continue;
                            double t = Math.Abs(d0 - d1) < 1e-9 ? 0 : d0 / (d0 - d1);
                            sx += x0 + (x1 - x0) * t;
                            sy += y0 + (y1 - y0) * t;
                            sz += z0 + (z1 - z0) * t;
                            n++;
                        }
                if (n == 0)
                    return (new P(vx + 0.5, vy + 0.5, vz + 0.5), false, false);
                return (new P(vx + sx / n, vy + sy / n, vz + sz / n), true, false);
            }
        }

        /// <summary>The mesh around a dig: every cube vertex the client's quads use within <paramref name="reach"/> voxels.</summary>
        private static List<(long X, long Y, long Z, P Position, bool Placed, bool FromBlocks)> MeshVertices(ClientView view,
            (double X, double Y, double Z) centre, double reach)
        {
            var cubes = new Dictionary<(long, long, long), (P, bool, bool)>();
            long x0 = (long)Math.Floor((centre.X / VS) - reach), x1 = (long)Math.Ceiling((centre.X / VS) + reach);
            long y0 = (long)Math.Floor((centre.Y / VS) - reach), y1 = (long)Math.Ceiling((centre.Y / VS) + reach);
            long z0 = (long)Math.Floor((centre.Z / VS) - reach), z1 = (long)Math.Ceiling((centre.Z / VS) + reach);
            for (long x = x0; x <= x1; x++)
                for (long y = y0; y <= y1; y++)
                    for (long z = z0; z <= z1; z++)
                    {
                        bool here = view.Solid(x, y, z);
                        for (int axis = 0; axis < 3; axis++)
                        {
                            (long nx, long ny, long nz) = axis switch { 0 => (x + 1, y, z), 1 => (x, y + 1, z), _ => (x, y, z + 1) };
                            if (view.Solid(nx, ny, nz) == here)
                                continue;
                            for (int a = 0; a > -2; a--)
                                for (int b = 0; b > -2; b--)
                                {
                                    (long cx, long cy, long cz) = axis switch { 0 => (x, y + a, z + b), 1 => (x + a, y, z + b), _ => (x + a, y + b, z) };
                                    if (!cubes.ContainsKey((cx, cy, cz)))
                                        cubes[(cx, cy, cz)] = view.Vertex(cx, cy, cz);
                                }
                        }
                    }
            return cubes.Select(c => (c.Key.Item1, c.Key.Item2, c.Key.Item3, c.Value.Item1, c.Value.Item2, c.Value.Item3)).ToList();
        }

        /// <summary>How far (voxels) a point lies from the ideal surface: the ground minus every dug sphere.</summary>
        private static double SurfaceError(P voxelPosition, Func<double, double, double> groundY,
            IEnumerable<((double X, double Y, double Z) C, double R)> spheres)
        {
            double x = voxelPosition.X * VS, y = voxelPosition.Y * VS, z = voxelPosition.Z * VS;
            double f = groundY(x, z) - y;
            foreach (var (c, r) in spheres)
                f = Math.Min(f, Math.Sqrt((x - c.X) * (x - c.X) + (y - c.Y) * (y - c.Y) + (z - c.Z) * (z - c.Z)) - r);
            return Math.Abs(f) / VS;
        }

        /// <summary>
        /// The mesh around a dig: cube vertices the quads use (<see cref="Vertices"/>), those with no vertex at all (drawn at
        /// the cube centre: a stair step), the error against the ideal surface over the hole (vertices within the swing
        /// radius + 1.5 voxels of a swing centre; voxels), how many of those are off by more than 0.3 voxel, how many
        /// vertices the dig layer gave, the dig layer's voxels and what the touched cells cost to send (CellsUpdate bytes).
        /// </summary>
        private sealed record Shape(int Vertices, int Unplaced, double MeanError, double MaxError, int Off, int Written,
            int LayerVoxels, int SolidAdded, int Bytes);

        /// <summary>Digs the swings (each healing after <paramref name="heal"/>) and measures the mesh around them.</summary>
        private Shape Dig(Func<float, float, float> height, params (Vector3 Local, double Radius)[] swings)
            => Measure(height, swings.Select(s => (s.Local, s.Radius)).ToList(), dig: true);

        private Shape Measure(Func<float, float, float> height, List<(Vector3 Local, double Radius)> swings, bool dig,
            TimeSpan? heal = null)
        {
            Ground(height);
            double GroundY(double gx, double gz) => GlobalGroundY(height, gx, gz);
            var spheres = new List<((double X, double Y, double Z), double)>();
            foreach (var (local, radius) in swings)
            {
                if (dig)
                {
                    (List<ulong> cells, _) = TerrainEdits.Sphere(local, radius, 0, zone, healDelay: heal);
                    touched.AddRange(cells);
                }
                spheres.Add((TerrainEdits.ToGlobal(local), radius));
            }
            var view = new ClientView(zone, GroundY);
            var centre = spheres[0].Item1;
            double reach = swings.Max(s => s.Radius) / VS + 4 + swings.Count * 2;
            var vertices = MeshVertices(view, centre, reach);
            bool OverHole(P v) => spheres.Any(h => Math.Sqrt(Math.Pow(v.X * VS - h.Item1.X, 2) + Math.Pow(v.Y * VS - h.Item1.Y, 2)
                + Math.Pow(v.Z * VS - h.Item1.Z, 2)) <= h.Item2 + 1.5 * VS);
            var hole = vertices.Where(v => OverHole(v.Position)).ToList();
            var errors = hole.Select(v => SurfaceError(v.Position, GroundY, spheres)).ToList();
            // The dig layer: its voxels, any solid material it holds (a dig adds no rock), and the bytes sent.
            var all = Enumerable.Repeat((byte)0xFF, BlockData.Count / 8).ToArray();
            int voxels = 0, solids = 0, bytes = 0;
            foreach (ulong cell in touched.Distinct())
            {
                foreach (TerrainEdits.VoxelValue v in TerrainEdits.Read(cell, all, zone, TerrainEdits.DigLayer))
                {
                    if (v.Flags != 0)
                        voxels++;
                    if ((v.Flags & 1) != 0 && v.Material != 0)
                        solids++;
                }
                bytes += TerrainEdits.BuildUpdate(cell, zone)?.Length ?? 0;
            }
            var shape = new Shape(vertices.Count, vertices.Count(v => !v.Placed), errors.Count == 0 ? 0 : errors.Average(),
                errors.Count == 0 ? 0 : errors.Max(), errors.Count(e => e > 0.3), vertices.Count(v => v.FromBlocks), voxels, solids, bytes);
            Console.WriteLine($"  dig shape (mode {TerrainEdits.OffsetMode}): {shape}");
            if (Environment.GetEnvironmentVariable("DIGSHAPE_DUMP") == "1")
                foreach (var (v, e) in hole.Zip(errors).Where(p => p.Second > 0.12))
                    Console.WriteLine($"    off {e:0.00}: cube {v.X},{v.Y},{v.Z} at {v.Position.Y - GroundY(v.Position.X * VS, v.Position.Z * VS) / VS:0.00} "
                        + $"voxels from the ground, from the dig layer: {v.FromBlocks}");
            return shape;
        }

        private static float Flat(float x, float z) => 999.87f;

        private static readonly Vector3 Surface = new(10.3f, 999.87f, 20.7f);

        /// <summary>A pick swing (radius 1 m) at the surface of flat ground: no stair steps, no rock added.</summary>
        [Fact]
        public void PickSwing_OnFlatGround_IsRound()
        {
            Shape shape = Dig(Flat, (Surface, 10));
            Assert.True(shape.Written > 20, $"the hole's vertices come from the dig ({shape.Written})");
            Assert.Equal(0, shape.Unplaced);
            Assert.Equal(0, shape.SolidAdded);
            Assert.True(shape.MaxError < 0.3, $"every vertex within 0.3 voxel of the ideal hole (max {shape.MaxError:0.00})");
            Assert.True(shape.MeanError < 0.05, $"mean error {shape.MeanError:0.000} voxel");
        }

        /// <summary>Held-button mining: three swings, each 0.7 radius further down, carve one smooth shaft.</summary>
        [Fact]
        public void HeldSwings_OverlappingHoles_StaySmooth()
        {
            Shape shape = Dig(Flat, (Surface, 10), (new Vector3(10.3f, 999.17f, 20.7f), 10), (new Vector3(10.6f, 998.47f, 20.9f), 10));
            Assert.Equal(0, shape.Unplaced);
            Assert.Equal(0, shape.SolidAdded);
            Assert.True(shape.MaxError < 0.35, $"max {shape.MaxError:0.00}");
            Assert.True(shape.MeanError < 0.05, $"mean error {shape.MeanError:0.000} voxel");
        }

        /// <summary>A wide tool (Goliath pick, 1.5 m) on a slope.</summary>
        [Fact]
        public void WideSwing_OnASlope_IsRound()
        {
            static float Slope(float x, float z) => 999.6f + 0.35f * (x - 10f) - 0.2f * (z - 20f);
            Shape shape = Dig(Slope, (new Vector3(10.3f, Slope(10.3f, 20.7f), 20.7f), 15));
            Assert.Equal(0, shape.Unplaced);
            Assert.True(shape.MaxError < 0.35, $"max {shape.MaxError:0.00}");
            Assert.True(shape.MeanError < 0.07, $"mean error {shape.MeanError:0.000} voxel");
        }

        /// <summary>
        /// Before / after on the same swing: the old solid shell (digmode 5: centre-tested voxels, a guessed-material shell
        /// only from 0.6 m under the ground) left cubes with no vertex (stair steps) and vertices half a voxel or more off;
        /// the client-surface dig leaves none, and costs about the same to send.
        /// </summary>
        [Fact]
        public void ClientSurface_BeatsTheOldShell()
        {
            int mode = TerrainEdits.OffsetMode;
            Shape old;
            try
            {
                TerrainEdits.OffsetMode = 5;
                old = Dig(Flat, (Surface, 10));
            }
            finally
            {
                TerrainEdits.OffsetMode = mode;
            }
            foreach (ulong cell in touched.Distinct())
                TerrainEdits.ClearCell(cell, zone);
            Shape now = Dig(Flat, (new Vector3(Surface.X, Surface.Y, Surface.Z), 10));
            Assert.True(old.Unplaced > 0 && old.MaxError > 1, $"the old shell steps ({old})");
            Assert.True(old.SolidAdded > 0, "the old shell wrote guessed rock");
            Assert.Equal(0, now.Unplaced);
            Assert.True(now.MaxError < old.MaxError / 3, $"{now.MaxError:0.00} vs {old.MaxError:0.00}");
            Assert.True(now.Bytes < old.Bytes * 1.5, $"bandwidth {now.Bytes} vs {old.Bytes} bytes");
        }

        /// <summary>The walls keep the ground's own material: outside the sphere the dig writes vertices only.</summary>
        [Fact]
        public void Walls_AreVertexOnly_InsideIsAirWithoutCentreVertices()
        {
            Dig(Flat, (Surface, 10));
            var g = TerrainEdits.ToGlobal(Surface);
            var all = Enumerable.Repeat((byte)0xFF, BlockData.Count / 8).ToArray();
            int vertexOnly = 0, air = 0;
            foreach (ulong cell in touched.Distinct())
            {
                (_, uint cx, uint cy, uint cz) = TerrainStore.Decode(cell);
                foreach (TerrainEdits.VoxelValue v in TerrainEdits.Read(cell, all, zone, TerrainEdits.DigLayer))
                {
                    if (v.Flags == 0)
                        continue;
                    int ix = v.Index / BlockData.Size % BlockData.Size, iy = v.Index % BlockData.Size, iz = v.Index / (BlockData.Size * BlockData.Size);
                    double x = (cx * 40 + ix) * VS - g.X, y = (cy * 40 + iy) * VS - g.Y, z = (cz * 40 + iz) * VS - g.Z;
                    bool inside = x * x + y * y + z * z <= 100;
                    if ((v.Flags & 1) != 0)
                    {
                        Assert.True(inside, "only voxels whose corner is inside the sphere are carved");
                        Assert.Equal((ushort)0, v.Material);
                        air++;
                    }
                    else
                    {
                        Assert.Equal((byte)2, v.Flags);
                        Assert.False(inside, "a vertex-only voxel lies on the ground side");
                        vertexOnly++;
                    }
                    if ((v.Flags & 2) != 0)
                        Assert.False(v.O0 == BlockData.CentreOffset && v.O1 == BlockData.CentreOffset && v.O2 == BlockData.CentreOffset,
                            "no centre vertices (they are the steps)");
                }
            }
            Assert.True(air > 30 && vertexOnly > 10, $"{air} air, {vertexOnly} vertex-only");
        }

        /// <summary>
        /// Heals: the first of two overlapping holes heals (layer 2 back to the ground there), the second stays open and is
        /// still round where the ground came back (its vertices are redone against the holes that are left).
        /// </summary>
        [Fact]
        public void Heal_OfOneOverlappingHole_LeavesTheOtherSmooth()
        {
            TimeSpan delay = TerrainEdits.RegenerationDelay;
            try
            {
                var second = new Vector3(10.9f, 999.6f, 20.7f);
                Measure(Flat, new() { (Surface, 10) }, dig: true, heal: TimeSpan.Zero);
                Measure(Flat, new() { (second, 10) }, dig: true, heal: TimeSpan.FromHours(1));
                TerrainEdits.SetRegenerationDelay(TerrainEdits.RegenerationDelay);   // look for due heals now
                var regrown = TerrainEdits.Regenerate((_, _, _) => new());
                Assert.Contains(regrown, r => r.Zone == zone);
                Shape left = Measure(Flat, new() { (second, 10) }, dig: false);
                Assert.Equal(0, left.Unplaced);
                Assert.True(left.MaxError < 0.35, $"the open hole after the heal: max {left.MaxError:0.00}");
                Assert.Equal(0, left.SolidAdded);
            }
            finally
            {
                TerrainEdits.RegenerationDelay = delay;
            }
        }

        /// <summary>Dynamite (4 m): the same surface at blast size, in well under a second.</summary>
        [Fact]
        public void DynamiteBlast_IsRound_AndQuick()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Shape shape = Dig(Flat, (Surface, 40));
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"{clock.Elapsed.TotalMilliseconds:0} ms with the mesh check");
            Assert.Equal(0, shape.Unplaced);
            Assert.Equal(0, shape.SolidAdded);
            Assert.True(shape.MaxError < 0.3, $"max {shape.MaxError:0.00}");
        }

        /// <summary>Where destruction is refused (a site's edge), the dig writes nothing: no air, no vertex.</summary>
        [Fact]
        public void RefusedVoxels_GetNothing()
        {
            Ground(Flat);
            var g = TerrainEdits.ToGlobal(Surface);
            (List<ulong> cells, _) = TerrainEdits.Sphere(Surface, 10, 0, zone, allowed: (x, y, z) => x < g.X);
            touched.AddRange(cells);
            var all = Enumerable.Repeat((byte)0xFF, BlockData.Count / 8).ToArray();
            int written = 0;
            foreach (ulong cell in touched.Distinct())
            {
                (_, uint cx, _, _) = TerrainStore.Decode(cell);
                foreach (TerrainEdits.VoxelValue v in TerrainEdits.Read(cell, all, zone, TerrainEdits.DigLayer).Where(v => v.Flags != 0))
                {
                    int ix = v.Index / BlockData.Size % BlockData.Size;
                    Assert.True((cx * 40 + ix + 0.5) * VS < g.X, "nothing where the filter refuses");
                    written++;
                }
            }
            Assert.True(written > 20, $"the allowed half is dug ({written})");
        }

        /// <summary>A heal takes everything a dig wrote: carved air and the vertex-only walls.</summary>
        [Fact]
        public void Heal_ClearsTheWholeDig()
        {
            Measure(Flat, new() { (Surface, 10) }, dig: true, heal: TimeSpan.Zero);
            TerrainEdits.SetRegenerationDelay(TerrainEdits.RegenerationDelay);
            TerrainEdits.Regenerate((_, _, _) => new());
            Shape healed = Measure(Flat, new() { (Surface, 10) }, dig: false);
            Assert.Equal(0, healed.LayerVoxels);
        }
    }
}
