using LandmarkEmulator.WorldServer.Game.Terrain;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Line of sight in the Chaos Caverns (audit-outposts-695.md F4): outpost NPCs stop shooting through walls and
    /// ceilings (P5727) and weapon area attacks no longer pass through blocks and wake creatures behind walls (P5726),
    /// 7 Jun 2016.
    /// <para>
    /// What blocks sight underground: cave rock (where no cave piece carves air, <see cref="CavePieces.IsAir"/>) and an
    /// outpost ruin's own walls. Reading the edited terrain cells for every check would expand them (~0.4 MB each), so
    /// each ruin gets a coarse solidity grid when its template is read at start-up (<see cref="AddRuin"/>; one grid per
    /// ruin, shared by its copies): blocks of 3 × 3 × 3 voxels (1.2 m) that the ruin writes, solid when most of its
    /// written voxels are. A segment is marched every 0.6 m, leaving out the first and last metre (the ends stand on
    /// floors). Only for creatures in caverns: on the open surface there is no cave data to test against, so sight is
    /// always clear there. Every landscape with caves (audit F2): a segment is tested against its own landscape's cave
    /// pieces and outposts; a ruin's grid serves its copies on every landscape.
    /// </para>
    /// <para>
    /// The creature navigation (Game/Pathing, TASKS 6.61) walks on the same solidity: <see cref="ColumnAir"/> gives a
    /// column's open air (cave pieces with the ruin grids over them, the rule of <see cref="Solid"/>) from a thread-safe
    /// snapshot of a landscape's ruin grids (<see cref="Footprints"/>). Build sites and dug outposts get grids of the same
    /// kind from the Environment Server (<see cref="Footprint.FromSite"/>, laid over a column by <see cref="ApplyFootprints"/>;
    /// OPEN-LEADS L133); line of sight itself still tests only caves and ruins.
    /// </para>
    /// </summary>
    public static class Sight
    {
        private const int Block = 3;
        private const float Step = 0.6f, EndSkip = 1.2f;

        internal sealed class Grid
        {
            public int NX, NY, NZ;
            public byte[] Written, Solid;     // bit sets, one bit per block
            /// <summary>A block's height in voxels: 3 for the ruins; 1 for the site grids (SolidityGrid.BlockY, doorways).</summary>
            public int BlockY = Block;

            /// <summary>A block's state by block index: −1 not written by the ruin, 0 mostly air, 1 mostly solid.</summary>
            public int State(long bx, long by, long bz)
            {
                long i = bx + (long)NX * (by + (long)NY * bz);
                if ((Written[i >> 3] & (1 << (int)(i & 7))) == 0)
                    return -1;
                return (Solid[i >> 3] & (1 << (int)(i & 7))) != 0 ? 1 : 0;
            }

            public bool TryGet(long x, long y, long z, out bool solid)
            {
                solid = false;
                long bx = x / Block, by = y / Block, bz = z / Block;
                if (x < 0 || y < 0 || z < 0 || bx >= NX || by >= NY || bz >= NZ)
                    return false;
                long i = bx + (long)NX * (by + (long)NY * bz);
                if ((Written[i >> 3] & (1 << (int)(i & 7))) == 0)
                    return false;
                solid = (Solid[i >> 3] & (1 << (int)(i & 7))) != 0;
                return true;
            }
        }

        private static readonly Dictionary<string, Grid> grids = new();
        private static readonly object sync = new();

        /// <summary>Builds a ruin's solidity grid from its decoded template (start-up, while Outposts has it loaded).</summary>
        public static void AddRuin(string ruin, VoxelTemplate template)
        {
            const int size = BlockData.Size;
            int nx = (int)((template.Size.X + Block - 1) / Block), ny = (int)((template.Size.Y + Block - 1) / Block),
                nz = (int)((template.Size.Z + Block - 1) / Block);
            long blocks = (long)nx * ny * nz;
            if (blocks <= 0 || blocks > 64_000_000)
                return;
            var written = new byte[blocks];
            var solid = new byte[blocks];
            foreach (VoxelTemplate.Cell c in template.Cells)
                for (int i = 0; i < size * size * size; i++)
                {
                    if ((c.Changed[i >> 3] & (0x80 >> (i & 7))) == 0 || (c.Blocks.Flags[i] & 1) == 0)
                        continue;
                    // BlockData.Index is y-fastest: i = y + 40 (x + 40 z).
                    int y = i % size, x = i / size % size, z = i / (size * size);
                    long vx = (long)c.X * size + x, vy = (long)c.Y * size + y, vz = (long)c.Z * size + z;
                    long b = vx / Block + (long)nx * (vy / Block + (long)ny * (vz / Block));
                    if (b < 0 || b >= blocks)
                        continue;
                    if (written[b] < 255)
                        written[b]++;
                    ushort m = c.Blocks.Materials[i];
                    if (m != 0 && m != VoxelTemplate.InteriorAir && solid[b] < 255)
                        solid[b]++;
                }
            var grid = new Grid { NX = nx, NY = ny, NZ = nz, Written = new byte[(blocks + 7) / 8], Solid = new byte[(blocks + 7) / 8] };
            for (long b = 0; b < blocks; b++)
            {
                if (written[b] == 0)
                    continue;
                grid.Written[b >> 3] |= (byte)(1 << (int)(b & 7));
                if (solid[b] * 2 >= written[b])
                    grid.Solid[b >> 3] |= (byte)(1 << (int)(b & 7));
            }
            lock (sync)
                grids[ruin] = grid;
        }

        /// <summary>Has this ruin a grid already (the other landscapes' outposts reuse the home continent's)?</summary>
        public static bool HasRuin(string ruin)
        {
            lock (sync)
                return grids.ContainsKey(ruin);
        }

        /// <summary>
        /// Is the straight line from <paramref name="from"/> to <paramref name="to"/> (local metres) free of cave rock and
        /// ruin walls? Always true on a landscape without cave data.
        /// </summary>
        public static bool Clear(uint zone, Vector3 from, Vector3 to)
        {
            if (!CavePieces.HasData(zone))
                return true;
            float length = Vector3.Distance(from, to);
            if (length <= 2 * EndSkip)
                return true;
            // The outposts whose box the segment can touch (a handful at most).
            var near = new List<(Outposts.Outpost O, Grid G)>();
            Vector3 lo = Vector3.Min(from, to), hi = Vector3.Max(from, to);
            lock (sync)
                foreach (Outposts.Outpost o in Outposts.InZone(zone))
                    if (o.Ruin != null && grids.TryGetValue(o.Ruin, out Grid g) && Touches(o, g, lo, hi))
                        near.Add((o, g));
            // Fail open: an end point that itself tests solid means the data doesn't match the world there (a creature
            // can't stand inside rock), so the test can't be trusted on this segment.
            if (Solid(from, near, zone) || Solid(to, near, zone))
                return true;
            Vector3 dir = (to - from) / length;
            for (float t = EndSkip; t < length - EndSkip; t += Step)
                if (Solid(from + dir * t, near, zone))
                    return false;
            return true;
        }

        private static bool Touches(Outposts.Outpost o, Grid g, Vector3 lo, Vector3 hi)
        {
            (Vector3 min, Vector3 max) = Box(o, g);
            return lo.X <= max.X && hi.X >= min.X && lo.Y <= max.Y && hi.Y >= min.Y && lo.Z <= max.Z && hi.Z >= min.Z;
        }

        /// <summary>An outpost's grid box in local metres.</summary>
        private static (Vector3 Min, Vector3 Max) Box(Outposts.Outpost o, Grid g)
        {
            var origin = TerrainStore.ZoneOrigin;
            double v = TerrainEdits.VoxelSize;
            var min = new Vector3((float)((o.Corner.X * v - origin.X) / 10), (float)((o.Corner.Y * v - origin.Y) / 10),
                (float)((o.Corner.Z * v - origin.Z) / 10));
            return (min, min + new Vector3(g.NX, g.NY, g.NZ) * (float)(Block * v / 10));
        }

        /// <summary>Dev check (dev command "sight"): what the test says at one point of a landscape.</summary>
        public static bool SolidAt(Vector3 local, uint zone)
        {
            var near = new List<(Outposts.Outpost O, Grid G)>();
            lock (sync)
                foreach (Outposts.Outpost o in Outposts.InZone(zone))
                    if (o.Ruin != null && grids.TryGetValue(o.Ruin, out Grid g) && Touches(o, g, local, local))
                        near.Add((o, g));
            return Solid(local, near, zone);
        }

        /// <summary>Dev check: what each source says at a point — "grid solid / grid air / not in a ruin", and cave air.</summary>
        public static string Explain(Vector3 local, uint zone)
        {
            var origin = TerrainStore.ZoneOrigin;
            double gx = local.X * 10.0 + origin.X, gy = local.Y * 10.0 + origin.Y, gz = local.Z * 10.0 + origin.Z;
            double v = TerrainEdits.VoxelSize;
            long vx = (long)Math.Floor(gx / v), vy = (long)Math.Floor(gy / v), vz = (long)Math.Floor(gz / v);
            string grid = "not in a ruin";
            lock (sync)
                foreach (Outposts.Outpost o in Outposts.InZone(zone))
                    if (o.Ruin != null && grids.TryGetValue(o.Ruin, out Grid g))
                    {
                        long rx = vx - o.Corner.X, ry = vy - o.Corner.Y, rz = vz - o.Corner.Z;
                        if (rx < 0 || ry < 0 || rz < 0 || rx / Block >= g.NX || ry / Block >= g.NY || rz / Block >= g.NZ)
                            continue;
                        grid = g.TryGet(rx, ry, rz, out bool solid) ? (solid ? $"grid solid ({o.Ruin})" : $"grid air ({o.Ruin})")
                            : $"in {o.Ruin}'s box, not written";
                        break;
                    }
            return $"{grid}, cave air {CavePieces.IsAir(gx, gy, gz, zone)}";
        }

        // ---- Columns for the creature navigation (Game/Pathing, TASKS 6.61) ---------------------------------------------

        /// <summary>
        /// One outpost's ruin grid where it was stamped, for <see cref="ColumnAir"/>: an immutable snapshot, safe to read on
        /// any thread (a ruin's grid never changes once built; the outpost list itself is world-thread only, so the
        /// navigation takes these snapshots there, <see cref="Footprints"/>).
        /// </summary>
        public sealed class Footprint
        {
            internal Grid Grid;
            /// <summary>The template's corner, a global voxel (0.4 m).</summary>
            public (long X, long Y, long Z) Corner;
            /// <summary>Its grid box in local metres.</summary>
            public Vector3 Min, Max;

            /// <summary>
            /// A build site's (or a dug outpost's) edited voxels from the Environment Server (<see cref="SolidityGrid"/>,
            /// IEnvironmentServer.SolidityOf; OPEN-LEADS L133): the same block rule and layout as a ruin's grid (blocks one
            /// voxel tall instead of three), so the navigation applies it the same way (<see cref="ApplyFootprints"/>). Null for
            /// an empty grid.
            /// </summary>
            public static Footprint FromSite(SolidityGrid g)
            {
                if (g == null || g.IsEmpty)
                    return null;
                var grid = new Grid { NX = g.NX, NY = g.NY, NZ = g.NZ, Written = g.Written, Solid = g.Solid, BlockY = SolidityGrid.BlockY };
                var origin = TerrainStore.ZoneOrigin;
                double v = TerrainEdits.VoxelSize;
                var min = new Vector3((float)((g.CornerX * v - origin.X) / 10), (float)((g.CornerY * v - origin.Y) / 10),
                    (float)((g.CornerZ * v - origin.Z) / 10));
                return new Footprint
                {
                    Grid = grid, Corner = (g.CornerX, g.CornerY, g.CornerZ), Min = min,
                    Max = min + new Vector3(g.NX * Block, g.NY * SolidityGrid.BlockY, g.NZ * Block) * (float)(v / 10)
                };
            }
        }

        /// <summary>World thread: the ruin grids of a landscape's outposts, as they stand now.</summary>
        public static Footprint[] Footprints(uint zone)
        {
            var list = new List<Footprint>();
            lock (sync)
                foreach (Outposts.Outpost o in Outposts.InZone(zone))
                    if (o.Ruin != null && grids.TryGetValue(o.Ruin, out Grid g))
                    {
                        (Vector3 min, Vector3 max) = Box(o, g);
                        list.Add(new Footprint { Grid = g, Corner = o.Corner, Min = min, Max = max });
                    }
            return list.ToArray();
        }

        [ThreadStatic] private static List<(double Lo, double Hi)> spansA, spansB;

        /// <summary>
        /// The open air of one vertical column at a local x/z, as this class sees solidity: sorted, separate spans of local
        /// heights (metres, low to high) written to <paramref name="into"/>. Cave air comes from the client's cave pieces
        /// (<see cref="CavePieces.AirSpans"/>); then every ruin that covers the column overrides the blocks it wrote — air
        /// where they are mostly air (interiors, doorways), rock where mostly solid (walls, floors) — exactly the rule of
        /// <see cref="Solid"/>, so sight and walking agree. Heights in a ruin are as coarse as its grid (1.2 m blocks).
        /// Any thread, with a <see cref="Footprints"/> snapshot (null = caves only).
        /// </summary>
        public static void ColumnAir(uint zone, float x, float z, Footprint[] ruins, List<(float Lo, float Hi)> into)
        {
            into.Clear();
            var origin = TerrainStore.ZoneOrigin;
            double gx = x * 10.0 + origin.X, gz = z * 10.0 + origin.Z;
            List<(double Lo, double Hi)> raw = spansA ??= new List<(double, double)>();
            List<(double Lo, double Hi)> air = spansB ??= new List<(double, double)>();
            raw.Clear();
            air.Clear();
            CavePieces.AirSpans(gx, gz, zone, raw);
            raw.Sort();
            foreach ((double lo, double hi) in raw)
                if (air.Count > 0 && lo <= air[^1].Hi)
                    air[^1] = (air[^1].Lo, Math.Max(air[^1].Hi, hi));
                else
                    air.Add((lo, hi));
            if (ruins != null)
                foreach (Footprint f in ruins)
                    ApplyRuns(f, x, z, gx, gz, air);
            foreach ((double lo, double hi) in air)
                into.Add(((float)((lo - origin.Y) / 10), (float)((hi - origin.Y) / 10)));
        }

        /// <summary>
        /// One grid over a column's air (global dm spans): the runs of blocks in the same state, bottom to top — written air
        /// carves, written solid fills, unwritten keeps what is there.
        /// </summary>
        private static void ApplyRuns(Footprint f, float x, float z, double gx, double gz, List<(double Lo, double Hi)> air)
        {
            if (x < f.Min.X || x >= f.Max.X || z < f.Min.Z || z >= f.Max.Z)
                return;
            double v = TerrainEdits.VoxelSize;
            long rx = (long)Math.Floor(gx / v) - f.Corner.X, rz = (long)Math.Floor(gz / v) - f.Corner.Z;
            if (rx < 0 || rz < 0 || rx / Block >= f.Grid.NX || rz / Block >= f.Grid.NZ)
                return;
            long bx = rx / Block, bz = rz / Block;
            for (int by = 0; by < f.Grid.NY;)
            {
                int state = f.Grid.State(bx, by, bz), end = by + 1;
                while (end < f.Grid.NY && f.Grid.State(bx, end, bz) == state)
                    end++;
                if (state >= 0)
                {
                    double lo = (f.Corner.Y + (long)by * f.Grid.BlockY) * v, hi = (f.Corner.Y + (long)end * f.Grid.BlockY) * v;
                    if (state == 0)
                        Union(air, lo, hi);
                    else
                        Subtract(air, lo, hi);
                }
                by = end;
            }
        }

        [ThreadStatic] private static List<(double Lo, double Hi)> spansC;

        /// <summary>
        /// Lays edited-voxel grids (<see cref="Footprint.FromSite"/>: build sites, dug outposts; OPEN-LEADS L133) over a
        /// column's open air given in local metres (sorted, separate spans; +∞ = open sky), by the rule of
        /// <see cref="ColumnAir"/>. Any thread (the grids are immutable snapshots).
        /// </summary>
        public static void ApplyFootprints(Footprint[] grids, float x, float z, List<(float Lo, float Hi)> air)
        {
            if (grids == null || grids.Length == 0)
                return;
            var origin = TerrainStore.ZoneOrigin;
            double gx = x * 10.0 + origin.X, gz = z * 10.0 + origin.Z;
            List<(double Lo, double Hi)> spans = null;
            foreach (Footprint f in grids)
            {
                if (x < f.Min.X || x >= f.Max.X || z < f.Min.Z || z >= f.Max.Z)
                    continue;
                if (spans == null)
                {
                    spans = spansC ??= new List<(double, double)>();
                    spans.Clear();
                    foreach ((float lo, float hi) in air)
                        spans.Add((lo * 10.0 + origin.Y, float.IsPositiveInfinity(hi) ? double.PositiveInfinity : hi * 10.0 + origin.Y));
                }
                ApplyRuns(f, x, z, gx, gz, spans);
            }
            if (spans == null)
                return;
            air.Clear();
            foreach ((double lo, double hi) in spans)
                air.Add(((float)((lo - origin.Y) / 10), double.IsPositiveInfinity(hi) ? float.PositiveInfinity : (float)((hi - origin.Y) / 10)));
        }

        /// <summary>Adds [lo, hi) to sorted, separate spans.</summary>
        private static void Union(List<(double Lo, double Hi)> spans, double lo, double hi)
        {
            int i = 0;
            while (i < spans.Count && spans[i].Hi < lo)
                i++;
            int j = i;
            while (j < spans.Count && spans[j].Lo <= hi)
            {
                lo = Math.Min(lo, spans[j].Lo);
                hi = Math.Max(hi, spans[j].Hi);
                j++;
            }
            spans.RemoveRange(i, j - i);
            spans.Insert(i, (lo, hi));
        }

        /// <summary>Removes [lo, hi) from sorted, separate spans.</summary>
        private static void Subtract(List<(double Lo, double Hi)> spans, double lo, double hi)
        {
            for (int i = 0; i < spans.Count; i++)
            {
                (double a, double b) = spans[i];
                if (b <= lo || a >= hi)
                    continue;
                spans.RemoveAt(i);
                if (b > hi)
                    spans.Insert(i, (hi, b));
                if (a < lo)
                    spans.Insert(i, (a, lo));
                // The pieces left (at most two) are outside [lo, hi): step past them.
                i += (a < lo ? 1 : 0) + (b > hi ? 1 : 0) - 1;
            }
        }

        private static bool Solid(Vector3 local, List<(Outposts.Outpost O, Grid G)> near, uint zone)
        {
            var origin = TerrainStore.ZoneOrigin;
            double gx = local.X * 10.0 + origin.X, gy = local.Y * 10.0 + origin.Y, gz = local.Z * 10.0 + origin.Z;
            double v = TerrainEdits.VoxelSize;
            long vx = (long)Math.Floor(gx / v), vy = (long)Math.Floor(gy / v), vz = (long)Math.Floor(gz / v);
            foreach ((Outposts.Outpost o, Grid g) in near)
                if (g.TryGet(vx - o.Corner.X, vy - o.Corner.Y, vz - o.Corner.Z, out bool solid))
                    return solid;
            return !CavePieces.IsAir(gx, gy, gz, zone);
        }
    }
}
