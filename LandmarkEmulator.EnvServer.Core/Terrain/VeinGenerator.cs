using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// The ore vein algorithm of Landmark's shared VoxelFarm VeinProvider (compiled into the client but run by the live
    /// servers: the client never spawns veins, it draws the ore the server writes into the cells). Ported from the
    /// client's code:
    /// - <see cref="Walk"/> (FUN_143f168b0): a vein instance {definition, start, direction, seed} → VEIN_LENGTH points,
    ///   one voxel (4 dm) apart, wandering every STRAIGHT_SEGMENT_STEPS steps (WANDER_DISTANCE/RADIUS/RATE, verticality
    ///   range, NEVER_RISE, AVOID_SURFACE in the top 20 voxels, WANDER_SWOOPILY), widths from VEIN_WIDTH,
    ///   VARIABLE_VEIN_WIDTH and END_TAPER_STEPS; each point keeps its own seed (the walk state − 7).
    /// - <see cref="CellsOf"/> (FUN_143f17f90): the level-2 cells a vein touches.
    /// - <see cref="ApplyCell"/> (FUN_143f17220): ore into one 40³ cell. Around each point a cube of
    ///   ±(W + W%2 + 2)/2 voxels; the per-point LCG advances twice per voxel; inside when the squared distance is below
    ///   (EDGE_UNCERTAINTY·(r·8 − 4) + width·2)² dm; MATERIAL_PATTERN 0 = MATERIAL1, 1 = by position along the vein,
    ///   2 = by distance from the centre (running PCT sums). Ore only repaints solid voxels.
    /// Materials are VoxelMaterials ARRAY_INDEX values (179 Raw Copper, 181 Raw Iron…), as block format 2 stores them.
    /// The wander azimuth is the one uncertain detail (shape only).
    /// </summary>
    public static class VeinGenerator
    {
        public const double VoxelSize = 4.0, CellSize = 160.0;
        private const double Inv32 = 1.0 / 4294967296.0;

        /// <summary>A ResourceVeins row (the columns the algorithm reads).</summary>
        public sealed class Definition
        {
            public uint Id;
            public string Name;
            public double DensitySurface, DensityCave, SurfaceOffsetMin, SurfaceOffsetMax;
            public int Width, Length, Pattern, Taper, Straight;
            public double WanderDistance, WanderRadius, WanderRate, VariableWidth, VerticalMin, VerticalMax, Edge;
            public bool AvoidSurface, NeverRise, Swoopy;
            public ushort[] Materials = new ushort[3];
            public double[] Percents = new double[3];
        }

        public readonly record struct Point(double X, double Y, double Z, double Width, uint Seed);

        /// <summary>The per-vein / per-point random generator (the terrain code's 32-bit LCG).</summary>
        public static uint Lcg(uint s) => unchecked(s * 0x7FF8A3EDu + 0x2AA01D31u);

        /// <summary>The spawn generator (FUN_143f19960): 64-bit LCG, doubles in [0, 1). Seeded by the server.</summary>
        public sealed class SpawnRandom
        {
            private ulong state;
            public SpawnRandom(ulong seed) => state = seed;
            public double Next()
            {
                state = unchecked(state * 0x27BB2EE687B0B0FDUL + 0xB504F32DUL);
                return state * (1.0 / 18446744073709551616.0);
            }
        }

        /// <summary>ResourceVeins rows by id, read by column name.</summary>
        public static Dictionary<uint, Definition> LoadDefinitions()
        {
            var result = new Dictionary<uint, Definition>();
            string path = GameDataFiles.PathOf("ResourceVeins.txt");
            if (!File.Exists(path))
                return result;
            string[] header = null;
            foreach (string line in File.ReadLines(path))
            {
                string[] f = line.Split('^');
                if (line.StartsWith("#*"))
                {
                    header = f.Select(h => h.TrimStart('#', '*')).ToArray();
                    continue;
                }
                if (header == null || f.Length < 2 || !uint.TryParse(f[0], out uint id))
                    continue;
                var row = new Dictionary<string, string>();
                for (int i = 0; i < header.Length && i < f.Length; i++)
                    row[header[i]] = f[i];
                double N(string k) => row.TryGetValue(k, out string v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0;
                var def = new Definition
                {
                    Id = id,
                    Name = row.GetValueOrDefault("DESCRIPTION", ""),
                    DensitySurface = N("DENSITY_ON_SURFACE"),
                    DensityCave = N("DENSITY_IN_CAVES"),
                    SurfaceOffsetMin = N("SURFACE_OFFSET_MIN"),
                    SurfaceOffsetMax = N("SURFACE_OFFSET_MAX"),
                    Width = (int)N("VEIN_WIDTH"),
                    Length = (int)N("VEIN_LENGTH"),
                    WanderDistance = N("WANDER_DISTANCE"),
                    WanderRadius = N("WANDER_RADIUS"),
                    WanderRate = N("WANDER_RATE"),
                    Pattern = (int)N("MATERIAL_PATTERN"),
                    VariableWidth = N("VARIABLE_VEIN_WIDTH"),
                    AvoidSurface = N("AVOID_SURFACE") != 0,
                    VerticalMin = N("WANDER_MIN_VERTICALITY_PCT"),
                    VerticalMax = N("WANDER_MAX_VERTICALITY_PCT"),
                    Edge = N("EDGE_UNCERTAINTY"),
                    Taper = (int)N("END_TAPER_STEPS"),
                    Straight = (int)N("STRAIGHT_SEGMENT_STEPS"),
                    NeverRise = N("NEVER_RISE") != 0,
                    Swoopy = N("WANDER_SWOOPILY") != 0
                };
                for (int k = 0; k < 3; k++)
                {
                    def.Materials[k] = (ushort)N($"MATERIAL{k + 1}_ID");
                    def.Percents[k] = N($"MATERIAL{k + 1}_PCT");
                }
                result[id] = def;
            }
            return result;
        }

        private static (double X, double Y, double Z) Norm((double X, double Y, double Z) v)
        {
            double l = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
            return l > 0 ? (v.X / l, v.Y / l, v.Z / l) : v;
        }

        /// <summary>Rotation taking unit a onto unit b (used by WANDER_SWOOPILY).</summary>
        private static double[,] RotationBetween((double X, double Y, double Z) a, (double X, double Y, double Z) b)
        {
            double c = a.X * b.X + a.Y * b.Y + a.Z * b.Z;
            (double vx, double vy, double vz) = (a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
            if (c < -0.999999)
                return new double[,] { { -1, 0, 0 }, { 0, -1, 0 }, { 0, 0, -1 } };
            double k = 1.0 / (1.0 + c);
            return new double[,]
            {
                { vx * vx * k + c, vx * vy * k - vz, vx * vz * k + vy },
                { vy * vx * k + vz, vy * vy * k + c, vy * vz * k - vx },
                { vz * vx * k - vy, vz * vy * k + vx, vz * vz * k + c }
            };
        }

        /// <summary>FUN_143f168b0: the vein's points (global dm), one voxel step apart.</summary>
        public static List<Point> Walk(Definition d, (double X, double Y, double Z) start, (double X, double Y, double Z) direction, uint seed)
        {
            double vs = VoxelSize;
            double dist = vs * d.WanderDistance, rad = vs * d.WanderRadius, rate = vs * d.WanderRate;
            double lo = Math.Abs(dist - rad), hi = dist + rad;
            (double X, double Y, double Z) p = start;
            double y0 = start.Y;
            var dir = Norm(direction);
            var w = dir;
            double length = hi;
            uint state = seed;
            var points = new List<Point>(Math.Max(0, d.Length));
            for (int i = 0; i < d.Length; i++)
            {
                uint pointSeed = unchecked(state - 7);
                if (d.Straight == 0 || i % d.Straight == 0)
                {
                    double r = d.Straight > 0 ? rate * d.Straight : rate;
                    state = Lcg(Lcg(state));
                    double t = state * Inv32;
                    double c0 = d.VerticalMin * 0.02 - 1.0, c1 = d.VerticalMax * 0.02 - 1.0;
                    double theta = Math.Acos(Math.Clamp((c1 - c0) * t + c0, -1.0, 1.0));
                    double phi = 2.0 * Math.PI * t;
                    (double rx, double ry, double rz) = (Math.Cos(phi) * Math.Sin(theta), Math.Cos(theta), Math.Sin(phi) * Math.Sin(theta));
                    w = Norm((w.X + rx * r, w.Y + ry * r, w.Z + rz * r));
                    (double sx, double sy, double sz) = (dir.X * dist + w.X * rad, dir.Y * dist + w.Y * rad, dir.Z * dist + w.Z * rad);
                    length = Math.Sqrt(sx * sx + sy * sy + sz * sz);
                    var next = Norm((sx, sy, sz));
                    if (d.Swoopy)
                    {
                        double[,] m = RotationBetween(dir, next);
                        w = (m[0, 0] * w.X + m[0, 1] * w.Y + m[0, 2] * w.Z, m[1, 0] * w.X + m[1, 1] * w.Y + m[1, 2] * w.Z,
                            m[2, 0] * w.X + m[2, 1] * w.Y + m[2, 2] * w.Z);
                    }
                    if (d.NeverRise && next.Y > 0)
                    {
                        next = (next.X, -next.Y, next.Z);
                        w = (w.X, -w.Y, w.Z);
                    }
                    if (d.AvoidSurface && next.Y > 0)
                    {
                        double depth = (y0 - p.Y) / vs;
                        if (depth < 20.0)
                        {
                            double f = 1.0 - (20.0 - depth) * 0.05;
                            next = Norm((next.X, next.Y * f, next.Z));
                            w = Norm((w.X, w.Y * f, w.Z));
                        }
                    }
                    dir = next;
                }
                double width;
                if (d.VariableWidth > 0)
                    width = ((hi != lo ? (length - lo) / (hi - lo) : 1.0) * d.VariableWidth + (1.0 - d.VariableWidth)) * (d.Width - 1) + 1.0;
                else
                    width = d.Width;
                if (d.Taper > 0)
                {
                    if (i < d.Taper)
                        width *= (i + 1) / (double)(d.Taper + 1);
                    int rem = d.Length - i;
                    if (rem - 1 < d.Taper)
                        width *= rem / (double)(d.Taper + 1);
                }
                points.Add(new Point(p.X, p.Y, p.Z, width, pointSeed));
                p = (p.X + dir.X * vs, p.Y + dir.Y * vs, p.Z + dir.Z * vs);
            }
            return points;
        }

        /// <summary>FUN_143f17f90: level-2 cells (x, y, z cell coordinates) a vein's points touch.</summary>
        public static HashSet<(long X, long Y, long Z)> CellsOf(Definition d, List<Point> points)
        {
            double half = ((d.Width + d.Width % 2 + 2) / 2) * VoxelSize;
            var cells = new HashSet<(long, long, long)>();
            foreach (Point p in points)
                for (int dx = -1; dx <= 1; dx += 2)
                    for (int dy = -1; dy <= 1; dy += 2)
                        for (int dz = -1; dz <= 1; dz += 2)
                            cells.Add(((long)((p.X + dx * half) / CellSize), (long)((p.Y + dy * half) / CellSize), (long)((p.Z + dz * half) / CellSize)));
            return cells;
        }

        /// <summary>
        /// FUN_143f17220 for one level-2 cell: voxel index (x + 40·(z + 40·y) order is the caller's) → ore material.
        /// <paramref name="solid"/>(x, y, z) says whether the voxel under the ore is solid (ore never changes solidity).
        /// </summary>
        public static Dictionary<(int X, int Y, int Z), ushort> ApplyCell(Definition d, List<Point> points, (long X, long Y, long Z) cell,
            Func<int, int, int, bool> solid, Dictionary<(int X, int Y, int Z), ushort> output = null)
        {
            output ??= new Dictionary<(int, int, int), ushort>();
            double cx0 = cell.X * CellSize, cy0 = cell.Y * CellSize, cz0 = cell.Z * CellSize;
            int half = (d.Width + d.Width % 2 + 2) / 2;
            int n = points.Count;
            var mine = new Dictionary<(int, int, int), ushort>();
            for (int pi = 0; pi < n; pi++)
            {
                Point p = points[pi];
                double mx = cx0 + 80, my = cy0 + 80, mz = cz0 + 80;
                double reach = (half + 30) * VoxelSize;
                if ((p.X - mx) * (p.X - mx) + (p.Y - my) * (p.Y - my) + (p.Z - mz) * (p.Z - mz) > reach * reach)
                    continue;
                int ix0 = (int)Math.Floor((p.X - cx0) / VoxelSize), iy0 = (int)Math.Floor((p.Y - cy0) / VoxelSize),
                    iz0 = (int)Math.Floor((p.Z - cz0) / VoxelSize);
                uint rng = p.Seed;
                double baseRadius = p.Width * 0.5 * VoxelSize;
                for (int y = iy0 - half; y < iy0 + half; y++)
                    for (int z = iz0 - half; z < iz0 + half; z++)
                        for (int x = ix0 - half; x < ix0 + half; x++)
                        {
                            uint u = Lcg(rng);
                            rng = Lcg(u);
                            if (x < 0 || x >= 40 || y < 0 || y >= 40 || z < 0 || z >= 40)
                                continue;
                            double d2 = ((x - ix0) * (x - ix0) + (y - iy0) * (y - iy0) + (z - iz0) * (z - iz0)) * VoxelSize * VoxelSize;
                            double jitter = d.Edge > 0 ? d.Edge * (u * Inv32 * 2 * VoxelSize - VoxelSize) : 0.0;
                            double r2 = (jitter + baseRadius) * (jitter + baseRadius);
                            if (!(d2 < r2))
                                continue;
                            var key = (x, y, z);
                            ushort material;
                            if (d.Pattern < 1)
                            {
                                if (mine.ContainsKey(key))
                                    continue;
                                material = d.Materials[0];
                            }
                            else
                            {
                                double t = d.Pattern == 1 ? (n > 1 ? pi / (double)(n - 1) : 0)
                                    : d.Pattern == 2 ? 1.0 - Math.Clamp(d2 / r2, 0.0, 1.0) : 0.0;
                                material = 0;
                                double cumulative = 0;
                                for (int k = 0; k < 3; k++)
                                {
                                    cumulative += d.Percents[k] * 0.01;
                                    if (t <= cumulative)
                                    {
                                        material = d.Materials[k];
                                        break;
                                    }
                                }
                                if (mine.TryGetValue(key, out ushort old))
                                {
                                    if (d.Pattern == 1 || old == material)
                                        continue;
                                    bool wins = false;
                                    for (int k = 2; k >= 0; k--)
                                    {
                                        if (d.Materials[k] == material) { wins = true; break; }
                                        if (d.Materials[k] == old) break;
                                    }
                                    if (!wins)
                                        continue;
                                }
                            }
                            if (material == 0 || material == 0xFFFF || !solid(x, y, z))
                                continue;
                            mine[key] = material;
                        }
            }
            foreach (var (key, material) in mine)
                output[key] = material;
            return output;
        }
    }
}
