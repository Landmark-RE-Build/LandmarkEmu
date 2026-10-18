using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.WorldServer.Game.Terrain;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Creature navigation (TASKS 6.61; trace/architecture/pathing.md): where a creature can walk, so chasing, going home
    /// and wandering go around cave rock and outpost walls instead of through them.
    /// <para>
    /// <b>What live had [P]</b>: a pathing system in the zone server with navigation meshes built from the voxel terrain as
    /// background jobs — the client's admin monitor page 32 "Pathing Information - Zone" (PathingMonitorState
    /// FUN_143167ad0 / 0.695 FUN_1432124f0) shows NavMeshCells, PendingStitchSections, Characters, DynamicSilhouettes,
    /// AvoidanceObstacles and NavMeshJobsRemaining / CompletedThisFrame / CompletedLifetime, and the client registers the
    /// admin commands VisualizePathing 0x4A2, Freeze / SaveNavMeshState 0x4A3 / 0x4A4, PathMetricsReset / Flush 0x4A5 /
    /// 0x4A6, DebugPathQuery 0x4A7 / 0x4A8 and PathingDataMonitorUpdate 0x4A9 / 0x4AA (0.695 registration table, part_0107).
    /// NPC pathing was ready (blueprint, late 2014, P0805); props, large trees most of all, got a much smaller
    /// collision area so creatures path past them more easily (7 Jun 2016, P5781). The mesh
    /// format and its parameters were server-only: nothing of it is in the client (no navmesh files by name, no reader
    /// for 0x4AA, OPEN-LEADS L4 / L132).
    /// </para>
    /// <para>
    /// <b>Ours [I]</b> — a walkable column grid, not a polygon mesh:
    /// </para>
    /// <list type="bullet">
    /// <item><b>Columns</b> of <see cref="Cell"/> (1.2 m, the size of Sight's ruin blocks) on a fixed lattice per
    /// landscape. A column keeps every <i>floor</i> in it: the bottom of a stretch of open air at least
    /// <see cref="Clearance"/> tall, with that stretch's ceiling. Two kinds (<see cref="Mode"/>): <b>Cave</b> columns from
    /// Sight's solidity (<see cref="Sight.ColumnAir"/>: the client's cave pieces with the outposts' ruin grids over them —
    /// the same data line of sight uses, nothing duplicated); <b>Surface</b> columns from the generated ground height
    /// (<see cref="TerrainHeights"/>), one floor each with open sky.</item>
    /// <item><b>Tiles</b> of 16 × 16 columns (19.2 m) per landscape and kind, built lazily on first need by one background
    /// thread and cached (at most <see cref="MaxTiles"/>, least recently used dropped first; a few KB each). The tiles under
    /// an outpost are dropped when it is stamped (<see cref="OutpostsChanged"/>). Edited terrain is not read per column
    /// (that would expand edited cells or ask the Environment Server for every tile): the grid is the generated world plus
    /// the stamped ruins, plus — on build sites and in dug outposts only — one coarse solidity grid per site from the
    /// Environment Server and the placed props' footprints (<see cref="SiteReady"/>, OPEN-LEADS L133).</item>
    /// <item><b>Moves</b>: from one column to a neighbour (8 ways; diagonals only when both side columns pass too, so no
    /// corner is cut) onto a floor at most <see cref="MaxClimb"/> higher or lower with <see cref="Clearance"/> of shared
    /// headroom.</item>
    /// <item><b>Straight first</b> (the cheap common case): <see cref="Line"/> follows the floor along the straight line on
    /// built tiles (a walking version of Sight.Clear: Sight tests the eye line, this the ground under the feet, so a drop
    /// or a ledge blocks too). Only a blocked line costs a path.</item>
    /// <item><b>Paths</b>: A* over (column, floor) on the background thread (<see cref="RequestPath"/>), at most
    /// <see cref="MaxNodes"/> nodes in a box of at most <see cref="MaxSearchRadius"/> around the two ends; an unreachable goal
    /// gives the path to the nearest point reached (partial). Waypoints are thinned with <see cref="Line"/>. The world tick
    /// never waits: it only reads finished tiles and finished paths, and a creature without them moves as before.</item>
    /// <item><b>Fail open</b>: where the data contradicts the world (the creature or its goal stands where the grid says
    /// rock, like Sight's end-point rule) or nothing is built yet, the caller steps straight, as before 6.61.</item>
    /// </list>
    /// Setting <c>CreaturePathing</c> (WorldServer.json / Docker CREATURE_PATHING, default on; dev "pathing off" for a
    /// comparison). The admin monitor's page-32 numbers come from <see cref="Monitor"/> (meanings ours [I]).
    /// </summary>
    public static class Pathing
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>Column width (m): Sight's ruin block, so a ruin's walls and doorways line up with whole columns.</summary>
        public const float Cell = 1.2f;
        /// <summary>Columns per tile side.</summary>
        private const int TileColumns = 16;
        /// <summary>Open air a floor needs above it (m, ours): the height of a kobold / slaug; larger bodies are not modelled.</summary>
        public const float Clearance = 2.0f;
        /// <summary>Most rise or drop between neighbouring columns (m, ours): one ruin block (stairs) or a 46° slope.</summary>
        public const float MaxClimb = 1.25f;
        /// <summary>How far from a creature's height its own floor may be (client height corrections, ruin block rounding).</summary>
        private const float StartReach = 2.0f;
        /// <summary>A goal's floor: the highest one at most this far above it ...</summary>
        private const float GoalAbove = 1.0f;
        /// <summary>... and at most this far below (a player jumping or flying over its floor).</summary>
        private const float GoalBelow = 12f;
        /// <summary>Most tiles cached (all landscapes; ~2-4 KB each).</summary>
        private const int MaxTiles = 3072;
        /// <summary>Most A* nodes one path query expands.</summary>
        private const int MaxNodes = 6000;
        /// <summary>A path query searches at most this far (m) from the middle of its two ends.</summary>
        private const float MaxSearchRadius = 96f;
        /// <summary>Most jobs waiting for the background thread; further path requests are refused (the creature waits).</summary>
        private const int MaxQueue = 512;
        /// <summary>Path thinning looks at most this many waypoints ahead.</summary>
        private const int SmoothLookahead = 24;

        /// <summary>Which solidity a creature walks on: cave rock and ruins, or the generated surface.</summary>
        public enum Mode : byte { Cave = 0, Surface = 1 }

        /// <summary>
        /// A straight walk: <see cref="LineResult.Clear"/>; <see cref="LineResult.Blocked"/> (a wall, ledge or drop on the
        /// way); <see cref="LineResult.Unknown"/> (tiles not built yet, now requested); <see cref="LineResult.Mismatch"/>
        /// (the start or the goal has no floor near it in the data: fail open).
        /// </summary>
        public enum LineResult { Clear, Blocked, Unknown, Mismatch }

        /// <summary>The floor under a step: not built yet, no floor within a climb (wall / drop), or found.</summary>
        public enum Probe { Unknown, None, Found }

        private sealed class Tile
        {
            /// <summary>Per column (x + 16 z) the first index into Floor / Ceiling; one more entry at the end.</summary>
            public int[] Start;
            /// <summary>Floors and their ceilings in decimetres (local), each column's low to high.</summary>
            public short[] Floor, Ceiling;
            public long LastUsed;
        }

        private static readonly ConcurrentDictionary<long, Tile> tiles = new();
        private static readonly ConcurrentDictionary<long, byte> pendingTiles = new();
        private static readonly ConcurrentQueue<object> jobs = new();
        private static readonly AutoResetEvent signal = new(false);
        /// <summary>Each landscape's outpost ruin snapshot (set on the world thread, read by the worker).</summary>
        private static readonly ConcurrentDictionary<uint, Sight.Footprint[]> ruinsByZone = new();
        private static Thread worker;
        private static readonly object startLock = new();
        /// <summary>Bumped when tiles are dropped: a tile built meanwhile from older data is not kept.</summary>
        private static int generation;
        private static int queued;

        private static bool? enabled;

        /// <summary>Setting CreaturePathing (default on); the dev command "pathing on|off" switches it until the restart.</summary>
        public static bool Enabled
        {
            get => enabled ??= ConfigurationManager<WorldServerConfiguration>.Instance.Config?.CreaturePathing ?? true;
            set => enabled = value;
        }

        private static uint Home => TerrainStore.HomeContinent;
        private static uint Norm(uint zone) => zone == 0 ? Home : zone;

        // ---- statistics (admin monitor page 32 analogue) -------------------------------------------------------------

        private static long tileBuilds, tileTicks, tileMax, tileMin = long.MaxValue;
        private static long pathQueries, pathTicks, pathMax, pathMin = long.MaxValue, pathNodes;
        private static long pathsReached, pathsPartial, pathsNone, pathsRefused;
        private static long completedLifetime;
        private static long lastCompleted, lastMonitorAt;

        // ---- keys and columns ---------------------------------------------------------------------------------------

        private static int ColumnOf(float v) => (int)MathF.Floor(v / Cell);
        private static float CentreOf(int column) => (column + 0.5f) * Cell;

        private static long Key(uint zone, Mode mode, int tx, int tz)
            => ((long)(zone & 0x7FFF) << 49) | ((long)mode << 48) | ((long)(tx & 0xFFFFFF) << 24) | (long)(tz & 0xFFFFFF);

        private static (uint Zone, Mode Mode, int Tx, int Tz) Unkey(long key)
            => ((uint)((ulong)key >> 49), (Mode)((key >> 48) & 1), ((int)((key >> 24) & 0xFFFFFF) << 8) >> 8, ((int)(key & 0xFFFFFF) << 8) >> 8);

        /// <summary>
        /// The tile holding a column. World thread (<paramref name="build"/> false): only a built tile; a missing one is
        /// requested and false returned. Worker (<paramref name="build"/> true): built on the spot when missing.
        /// </summary>
        private static bool Get(uint zone, Mode mode, int ix, int iz, bool build, out Tile tile, out int column)
        {
            int tx = ix >> 4, tz = iz >> 4;
            column = (ix & (TileColumns - 1)) + TileColumns * (iz & (TileColumns - 1));
            long key = Key(zone, mode, tx, tz);
            if (tiles.TryGetValue(key, out tile))
            {
                tile.LastUsed = Environment.TickCount64;
                return true;
            }
            if (!build)
            {
                RequestTile(key, zone);
                return false;
            }
            int gen = Volatile.Read(ref generation);
            tile = BuildTile(zone, mode, tx, tz);
            if (Volatile.Read(ref generation) == gen)
                tiles[key] = tile;
            pendingTiles.TryRemove(key, out _);
            return true;
        }

        private static void RequestTile(long key, uint zone)
        {
            if (Volatile.Read(ref queued) >= MaxQueue || !pendingTiles.TryAdd(key, 0))
                return;
            EnsureRuins(zone);
            Enqueue(key);
        }

        /// <summary>World thread: the landscape's outpost snapshot exists before any job for it runs.</summary>
        private static void EnsureRuins(uint zone)
        {
            if (!ruinsByZone.ContainsKey(zone))
                ruinsByZone[zone] = Sight.Footprints(zone);
        }

        [ThreadStatic] private static List<(float Lo, float Hi)> columnAir;

        /// <summary>
        /// Worker: one tile's columns (cave: Sight's air spans; surface: the generated ground height), with the edited-voxel
        /// grids and prop obstacles of the build sites (and dug outposts, caves only) that cover the tile laid over them
        /// (<see cref="SiteLayer"/>, OPEN-LEADS L133).
        /// </summary>
        private static Tile BuildTile(uint zone, Mode mode, int tx, int tz)
        {
            long started = Stopwatch.GetTimestamp();
            var start = new int[TileColumns * TileColumns + 1];
            var floors = new List<short>();
            var ceilings = new List<short>();
            List<(float Lo, float Hi)> air = columnAir ??= new List<(float, float)>();
            Sight.Footprint[] ruins = mode == Mode.Cave ? ruinsByZone.GetValueOrDefault(zone) : null;
            // The site layers meeting this tile (a handful at most), their grids as one list for Sight.
            float size = TileColumns * Cell, x0 = tx * size, z0 = tz * size;
            SiteLayer[] layers = siteLayers.GetValueOrDefault(zone)?.Where(l => (mode == Mode.Cave || !l.CaveOnly)
                && l.Min.X < x0 + size && l.Max.X > x0 && l.Min.Z < z0 + size && l.Max.Z > z0).ToArray();
            if (layers is { Length: 0 })
                layers = null;
            Sight.Footprint[] grids = layers?.Where(l => l.Grid != null).Select(l => l.Grid).ToArray();
            for (int cz = 0; cz < TileColumns; cz++)
                for (int cx = 0; cx < TileColumns; cx++)
                {
                    start[cx + TileColumns * cz] = floors.Count;
                    int ix = tx * TileColumns + cx, iz = tz * TileColumns + cz;
                    float x = CentreOf(ix), z = CentreOf(iz);
                    if (mode == Mode.Surface)
                    {
                        if (TerrainHeights.HeightAt(x, z, zone) is not float h)
                            continue;
                        if (layers == null)
                        {
                            floors.Add(Dm(h));
                            ceilings.Add(short.MaxValue);
                            continue;
                        }
                        // On a site: the generated ground (solid below, open sky above) under the site's edits.
                        air.Clear();
                        air.Add((h, float.PositiveInfinity));
                    }
                    else
                        Sight.ColumnAir(zone, x, z, ruins, air);
                    if (layers != null)
                    {
                        Sight.ApplyFootprints(grids, x, z, air);
                        foreach (SiteLayer l in layers)
                            if (l.Obstacles != null && l.Obstacles.TryGetValue(ColumnKey(ix, iz), out List<(float Lo, float Hi)> blocked))
                                foreach ((float lo, float hi) in blocked)
                                    SubtractSpan(air, lo, hi);
                    }
                    foreach ((float lo, float hi) in air)
                        if (hi - lo >= Clearance && floors.Count - start[cx + TileColumns * cz] < 255)
                        {
                            floors.Add(Dm(lo));
                            ceilings.Add(float.IsPositiveInfinity(hi) ? short.MaxValue : Dm(hi));
                        }
                }
            start[^1] = floors.Count;
            long elapsed = Stopwatch.GetTimestamp() - started;
            Interlocked.Increment(ref tileBuilds);
            Interlocked.Add(ref tileTicks, elapsed);
            Max(ref tileMax, elapsed);
            Min(ref tileMin, elapsed);
            return new Tile { Start = start, Floor = floors.ToArray(), Ceiling = ceilings.ToArray(), LastUsed = Environment.TickCount64 };
        }

        private static short Dm(float metres) => (short)Math.Clamp(MathF.Round(metres * 10f), -32767f, 32767f);
        private static float FloorOf(Tile t, int column, int k) => t.Floor[t.Start[column] + k] / 10f;
        private static float CeilingOf(Tile t, int column, int k) => t.Ceiling[t.Start[column] + k] / 10f;
        private static int Count(Tile t, int column) => t.Start[column + 1] - t.Start[column];

        /// <summary>The floor nearest a height within <paramref name="reach"/>, or −1.</summary>
        private static int NearestFloor(Tile t, int column, float y, float reach)
        {
            int best = -1;
            float bestD = reach;
            for (int k = 0; k < Count(t, column); k++)
            {
                float d = MathF.Abs(FloorOf(t, column, k) - y);
                if (d <= bestD)
                {
                    best = k;
                    bestD = d;
                }
            }
            return best;
        }

        /// <summary>A floor one can step onto from a floor at <paramref name="y"/> under <paramref name="ceiling"/>, or −1.</summary>
        private static int PassableFloor(Tile t, int column, float y, float ceiling)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int k = 0; k < Count(t, column); k++)
            {
                float f = FloorOf(t, column, k), d = MathF.Abs(f - y);
                if (d > MaxClimb || MathF.Min(ceiling, CeilingOf(t, column, k)) - MathF.Max(y, f) < Clearance)
                    continue;
                if (d < bestD)
                {
                    best = k;
                    bestD = d;
                }
            }
            return best;
        }

        /// <summary>A goal's floor: the highest at most <see cref="GoalAbove"/> above it and <see cref="GoalBelow"/> below, or −1.</summary>
        private static int GoalFloor(Tile t, int column, float y)
        {
            int best = -1;
            for (int k = 0; k < Count(t, column); k++)
            {
                float f = FloorOf(t, column, k);
                if (f <= y + GoalAbove && f >= y - GoalBelow)
                    best = k;
            }
            return best;
        }

        // ---- straight lines and steps (world thread) ------------------------------------------------------------------

        /// <summary>
        /// World thread: can a creature walk the straight line from <paramref name="from"/> (where it stands) to
        /// <paramref name="to"/> on the floors of built tiles? Missing tiles are requested (<see cref="LineResult.Unknown"/>).
        /// </summary>
        public static LineResult Line(uint zone, Mode mode, Vector3 from, Vector3 to)
            => LineCore(Norm(zone), mode, from, to, build: false);

        private static LineResult LineCore(uint zone, Mode mode, Vector3 from, Vector3 to, bool build)
        {
            int cx = ColumnOf(from.X), cz = ColumnOf(from.Z);
            if (!Get(zone, mode, cx, cz, build, out Tile t, out int column))
                return LineResult.Unknown;
            int k = NearestFloor(t, column, from.Y, StartReach);
            if (k < 0)
                return LineResult.Mismatch;
            float y = FloorOf(t, column, k), ceiling = CeilingOf(t, column, k);
            if (!Get(zone, mode, ColumnOf(to.X), ColumnOf(to.Z), build, out Tile gt, out int gcolumn))
                return LineResult.Unknown;
            int gk = GoalFloor(gt, gcolumn, to.Y);
            if (gk < 0)
                return LineResult.Mismatch;
            float goalY = FloorOf(gt, gcolumn, gk);
            float dx = to.X - from.X, dz = to.Z - from.Z;
            int steps = (int)MathF.Ceiling(MathF.Sqrt(dx * dx + dz * dz) / (Cell * 0.5f));
            for (int s = 1; s <= steps; s++)
            {
                float f = (float)s / steps;
                int nx = ColumnOf(from.X + dx * f), nz = ColumnOf(from.Z + dz * f);
                if (nx == cx && nz == cz)
                    continue;
                if (nx != cx && nz != cz)
                {
                    // Through a corner: both side columns must pass as well.
                    foreach ((int sx, int sz) in new[] { (nx, cz), (cx, nz) })
                    {
                        if (!Get(zone, mode, sx, sz, build, out Tile st, out int scolumn))
                            return LineResult.Unknown;
                        if (PassableFloor(st, scolumn, y, ceiling) < 0)
                            return LineResult.Blocked;
                    }
                }
                if (!Get(zone, mode, nx, nz, build, out t, out column))
                    return LineResult.Unknown;
                k = PassableFloor(t, column, y, ceiling);
                if (k < 0)
                    return LineResult.Blocked;
                y = FloorOf(t, column, k);
                ceiling = CeilingOf(t, column, k);
                cx = nx;
                cz = nz;
            }
            return MathF.Abs(y - goalY) <= MaxClimb ? LineResult.Clear : LineResult.Blocked;
        }

        /// <summary>
        /// World thread: the floor a step from <paramref name="from"/> to <paramref name="next"/> lands on. <paramref name="here"/>
        /// is the floor under <paramref name="from"/> (to keep a client's height correction as an offset), <paramref name="there"/>
        /// the one under <paramref name="next"/>. <see cref="Probe.Unknown"/> when a tile isn't built or the creature isn't
        /// near a floor of the data (the caller falls back to its old floor search).
        /// </summary>
        public static Probe Step(uint zone, Mode mode, Vector3 from, Vector3 next, out float here, out float there)
        {
            zone = Norm(zone);
            here = there = from.Y;
            int cx = ColumnOf(from.X), cz = ColumnOf(from.Z), nx = ColumnOf(next.X), nz = ColumnOf(next.Z);
            if (!Get(zone, mode, cx, cz, false, out Tile t, out int column))
                return Probe.Unknown;
            int k = NearestFloor(t, column, from.Y, StartReach);
            if (k < 0)
                return Probe.Unknown;
            here = there = FloorOf(t, column, k);
            float ceiling = CeilingOf(t, column, k);
            if (nx == cx && nz == cz)
                return Probe.Found;
            if (nx != cx && nz != cz)
                foreach ((int sx, int sz) in new[] { (nx, cz), (cx, nz) })
                {
                    if (!Get(zone, mode, sx, sz, false, out Tile st, out int scolumn))
                        return Probe.Unknown;
                    if (PassableFloor(st, scolumn, here, ceiling) < 0)
                        return Probe.None;
                }
            if (!Get(zone, mode, nx, nz, false, out Tile nt, out int ncolumn))
                return Probe.Unknown;
            int nk = PassableFloor(nt, ncolumn, here, ceiling);
            if (nk < 0)
                return Probe.None;
            there = FloorOf(nt, ncolumn, nk);
            return Probe.Found;
        }

        // ---- path queries -------------------------------------------------------------------------------------------

        /// <summary>A path query; filled by the background thread, read by the world thread once <see cref="Done"/>.</summary>
        public sealed class PathRequest
        {
            public uint Zone;
            public Mode Mode;
            public Vector3 From, To;
            public DateTime Created;
            public volatile bool Done;
            /// <summary>Waypoints after the start (floor heights); empty or null = no way forward found.</summary>
            public List<Vector3> Points;
            /// <summary>The goal itself was reached; false = the path ends at the nearest point found (partial).</summary>
            public bool Reached;
            public int Nodes;
        }

        /// <summary>World thread: queues a path query (null when the queue is full: try again later).</summary>
        public static PathRequest RequestPath(uint zone, Mode mode, Vector3 from, Vector3 to)
        {
            zone = Norm(zone);
            if (Volatile.Read(ref queued) >= MaxQueue)
            {
                Interlocked.Increment(ref pathsRefused);
                return null;
            }
            EnsureRuins(zone);
            var request = new PathRequest { Zone = zone, Mode = mode, From = from, To = to, Created = DateTime.UtcNow };
            Enqueue(request);
            return request;
        }

        private static void Enqueue(object job)
        {
            if (worker == null)
                lock (startLock)
                    if (worker == null)
                    {
                        worker = new Thread(Run) { IsBackground = true, Name = "Pathing", Priority = ThreadPriority.BelowNormal };
                        worker.Start();
                    }
            Interlocked.Increment(ref queued);
            jobs.Enqueue(job);
            signal.Set();
        }

        private static DateTime lastFailure;

        /// <summary>The background thread: tile builds and path queries in order, then the cache trimmed.</summary>
        private static void Run()
        {
            while (true)
            {
                signal.WaitOne(1000);
                while (jobs.TryDequeue(out object job))
                {
                    Interlocked.Decrement(ref queued);
                    try
                    {
                        if (job is long key)
                        {
                            (uint zone, Mode mode, int tx, int tz) = Unkey(key);
                            Get(zone, mode, tx * TileColumns, tz * TileColumns, true, out _, out _);
                        }
                        else if (job is PathRequest request)
                            Solve(request);
                    }
                    catch (Exception e)
                    {
                        if (DateTime.UtcNow - lastFailure > TimeSpan.FromMinutes(1))
                        {
                            lastFailure = DateTime.UtcNow;
                            log.Error(e, "Pathing job failed.");
                        }
                        if (job is PathRequest failed)
                            failed.Done = true;
                        if (job is long failedKey)
                            pendingTiles.TryRemove(failedKey, out _);
                    }
                    Interlocked.Increment(ref completedLifetime);
                }
                Trim();
            }
        }

        /// <summary>Drops the least recently used tenth of the tiles once over <see cref="MaxTiles"/>.</summary>
        private static void Trim()
        {
            if (tiles.Count <= MaxTiles)
                return;
            foreach (long key in tiles.OrderBy(p => p.Value.LastUsed).Take(tiles.Count - MaxTiles * 9 / 10).Select(p => p.Key).ToList())
                tiles.TryRemove(key, out _);
        }

        private struct Node
        {
            public int Ix, Iz;
            public float Y, Ceiling, G;
            public int Parent;
            public bool Closed;
        }

        private static float Octile(int dx, int dz)
        {
            dx = Math.Abs(dx);
            dz = Math.Abs(dz);
            return (Math.Max(dx, dz) + 0.41421356f * Math.Min(dx, dz)) * Cell;
        }

        private static readonly (int Dx, int Dz)[] Directions = { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) };

        private static void Solve(PathRequest r)
        {
            long started = Stopwatch.GetTimestamp();
            try
            {
                r.Points = Search(r, out r.Reached, out r.Nodes);
            }
            finally
            {
                long elapsed = Stopwatch.GetTimestamp() - started;
                Interlocked.Increment(ref pathQueries);
                Interlocked.Add(ref pathTicks, elapsed);
                Interlocked.Add(ref pathNodes, r.Nodes);
                Max(ref pathMax, elapsed);
                Min(ref pathMin, elapsed);
                if (r.Points == null || r.Points.Count == 0)
                    Interlocked.Increment(ref pathsNone);
                else if (r.Reached)
                    Interlocked.Increment(ref pathsReached);
                else
                    Interlocked.Increment(ref pathsPartial);
                r.Done = true;
            }
        }

        /// <summary>
        /// A* over (column, floor): from the requester's floor (or, standing in a wall per the data, the nearest neighbour
        /// column's) towards the goal's floor; when the goal can't be reached within the budget, to the node nearest it.
        /// </summary>
        private static List<Vector3> Search(PathRequest r, out bool reached, out int expanded)
        {
            reached = false;
            expanded = 0;
            uint zone = r.Zone;
            Mode mode = r.Mode;
            int sx = ColumnOf(r.From.X), sz = ColumnOf(r.From.Z), gx = ColumnOf(r.To.X), gz = ColumnOf(r.To.Z);
            float startY = 0f, startCeiling = 0f;
            int startK = 0;
            bool found = false;
            foreach ((int dx, int dz) in new[] { (0, 0) }.Concat(Directions))
            {
                Get(zone, mode, sx + dx, sz + dz, true, out Tile t, out int column);
                int k = NearestFloor(t, column, r.From.Y, StartReach);
                if (k < 0)
                    continue;
                sx += dx;
                sz += dz;
                startK = k;
                startY = FloorOf(t, column, k);
                startCeiling = CeilingOf(t, column, k);
                found = true;
                break;
            }
            if (!found)
                return null;
            Get(zone, mode, gx, gz, true, out Tile goalTile, out int goalColumn);
            int goalK = GoalFloor(goalTile, goalColumn, r.To.Y);
            float goalY = goalK >= 0 ? FloorOf(goalTile, goalColumn, goalK) : r.To.Y;

            // The search box: within MaxSearchRadius of the start, and within half the way plus 24 m of the middle of the two
            // ends (a near goal is searched near the straight line; a goal too far away gets a partial path towards it).
            int startReach = (int)(MaxSearchRadius / Cell), midReach = (int)((Octile(gx - sx, gz - sz) / 2f + 24f) / Cell);
            int mx = (sx + gx) / 2, mz = (sz + gz) / 2;
            var nodes = new List<Node> { new() { Ix = sx, Iz = sz, Y = startY, Ceiling = startCeiling, Parent = -1 } };
            var index = new Dictionary<long, int> { [NodeId(sx, sz, startK)] = 0 };
            var open = new PriorityQueue<int, float>();
            open.Enqueue(0, Octile(gx - sx, gz - sz));
            int best = 0;
            float bestH = float.MaxValue;
            while (open.TryDequeue(out int i, out _))
            {
                Node n = nodes[i];
                if (n.Closed)
                    continue;
                n.Closed = true;
                nodes[i] = n;
                if (++expanded > MaxNodes)
                    break;
                float h = Octile(gx - n.Ix, gz - n.Iz) + MathF.Abs(n.Y - goalY);
                if (h < bestH)
                {
                    best = i;
                    bestH = h;
                }
                if (n.Ix == gx && n.Iz == gz && goalK >= 0 && MathF.Abs(n.Y - goalY) <= MaxClimb)
                {
                    reached = true;
                    best = i;
                    break;
                }
                foreach ((int dx, int dz) in Directions)
                {
                    int nx = n.Ix + dx, nz = n.Iz + dz;
                    if (Math.Abs(nx - sx) > startReach || Math.Abs(nz - sz) > startReach
                        || Math.Abs(nx - mx) > midReach || Math.Abs(nz - mz) > midReach)
                        continue;
                    Get(zone, mode, nx, nz, true, out Tile nt, out int ncolumn);
                    int nk = PassableFloor(nt, ncolumn, n.Y, n.Ceiling);
                    if (nk < 0)
                        continue;
                    bool diagonal = dx != 0 && dz != 0;
                    if (diagonal)
                    {
                        Get(zone, mode, nx, n.Iz, true, out Tile at, out int acolumn);
                        Get(zone, mode, n.Ix, nz, true, out Tile bt, out int bcolumn);
                        if (PassableFloor(at, acolumn, n.Y, n.Ceiling) < 0 || PassableFloor(bt, bcolumn, n.Y, n.Ceiling) < 0)
                            continue;
                    }
                    float ny = FloorOf(nt, ncolumn, nk);
                    float g = n.G + (diagonal ? 1.41421356f : 1f) * Cell + MathF.Abs(ny - n.Y) * 0.5f;
                    long id = NodeId(nx, nz, nk);
                    if (index.TryGetValue(id, out int j))
                    {
                        Node old = nodes[j];
                        if (old.Closed || old.G <= g)
                            continue;
                        old.G = g;
                        old.Parent = i;
                        nodes[j] = old;
                    }
                    else
                    {
                        j = nodes.Count;
                        nodes.Add(new Node { Ix = nx, Iz = nz, Y = ny, Ceiling = CeilingOf(nt, ncolumn, nk), G = g, Parent = i });
                        index[id] = j;
                    }
                    open.Enqueue(j, g + Octile(gx - nx, gz - nz));
                }
            }
            if (best == 0)
                return new List<Vector3>();
            var cells = new List<Vector3>();
            for (int i = best; i != -1; i = nodes[i].Parent)
                cells.Add(new Vector3(CentreOf(nodes[i].Ix), nodes[i].Y, CentreOf(nodes[i].Iz)));
            cells.Reverse();
            return Thin(zone, mode, r.From, cells);
        }

        private static long NodeId(int ix, int iz, int k) => ((long)(ix & 0xFFFFFF) << 32) | ((long)(iz & 0xFFFFFF) << 8) | (long)(k & 0xFF);

        /// <summary>
        /// Keeps only the waypoints a straight walk can't skip: from each point, the farthest of the next
        /// <see cref="SmoothLookahead"/> column centres in a clear line. The start column itself is left out.
        /// </summary>
        private static List<Vector3> Thin(uint zone, Mode mode, Vector3 from, List<Vector3> cells)
        {
            var points = new List<Vector3>();
            Vector3 at = from;
            int i = 0;
            while (i < cells.Count - 1)
            {
                int next = i + 1;
                for (int j = Math.Min(cells.Count - 1, i + SmoothLookahead); j > i + 1; j--)
                    if (LineCore(zone, mode, at, cells[j], build: true) == LineResult.Clear)
                    {
                        next = j;
                        break;
                    }
                points.Add(cells[next]);
                at = cells[next];
                i = next;
            }
            return points;
        }

        // ---- build sites and dug outposts (OPEN-LEADS L133, trace/0695/agent-leads4.md B1) ----------------------------
        //
        // Live creatures walked around what players built: props, large trees most of all, got a much smaller collision
        // area so creatures path past them more easily (7 Jun 2016, P5781 [P]: props
        // blocked them), and 2016 players saw NPCs walk along the outer walls to the next pathing node, found a 45°
        // doorway had to be as wide as a straight one, and saw pathing stop after repainting the floor under a path
        // (forum 56713, agent-leads2-d2.md L133 [P behaviour]). How live's server mesh did it is not
        // in the client (L132), so the mechanism is ours [I]:
        //  - a site's edited voxels come from the Environment Server as a coarse solidity grid (IEnvironmentServer.SolidityOf:
        //    1.2 m columns × 0.4 m rows, Sight.AddRuin's rule) and are laid over the walkable columns the way the outposts' ruin grids are
        //    laid over the caves (Sight.ApplyFootprints): written solid = rock, written air = a dug hole, unwritten = the
        //    generated ground;
        //  - placed props with a path-block radius (Models.txt PATH_BLOCK_RADIUS / OFFSET, ModelFootprints) block the
        //    columns within that radius (× the prop's scale) from the prop's base to Clearance above it, so a creature can't
        //    stand in or step onto them; creature spawners, Linking Areas, game markers and moving platforms / docks don't
        //    block;
        //  - built lazily: on the first step of a story creature on the site (in the background; until it is there the
        //    creature steps straight, as before), then again 2 s after the last voxel or prop change on the site (one rebuild
        //    for a builder's burst of edits; a material-only change rebuilds too: live broke there, ours just rebuilds); a site
        //    no story creature walked on for 10 minutes is forgotten;
        //  - optional cheap extra: a Chaos Cavern outpost players dig into gets the same grid of its box (the digging layer
        //    over the stamped ruin), for the cavern creatures (caves only), forgotten 30 minutes after its last change (the
        //    holes heal after 20).
        // No network traffic: all server-internal (one Environment Server call per rebuild, nothing to the clients).
        // Setting SitePathing (WorldServer.json / Docker SITE_PATHING, default on); off = story creatures step straight.

        /// <summary>Most site / outpost grids kept (least recently used dropped first; a fully built site is ~3.4 MB, most far less).</summary>
        private const int MaxSites = 16;
        /// <summary>A prop's blocking radius is capped here (m): Models.txt's largest trees are ~16 m.</summary>
        private const float MaxObstacleRadius = 16f;
        private static readonly TimeSpan SiteDebounce = TimeSpan.FromSeconds(2), SiteIdle = TimeSpan.FromMinutes(10),
            OutpostIdle = TimeSpan.FromMinutes(30), SiteRetry = TimeSpan.FromSeconds(10), PropCheckInterval = TimeSpan.FromSeconds(1),
            SiteUpdateInterval = TimeSpan.FromMilliseconds(250);
        /// <summary>Outpost entries' ids (never a claim id: those are database row ids).</summary>
        private const ulong OutpostBit = 1UL << 63;

        private static bool? sitesEnabled;

        /// <summary>Setting SitePathing (default on = live); the dev command "sitepathing on|off" switches it until the restart.</summary>
        public static bool SitesEnabled
        {
            get => sitesEnabled ??= ConfigurationManager<WorldServerConfiguration>.Instance.Config?.SitePathing ?? true;
            set
            {
                sitesEnabled = value;
                if (!value)
                    lock (siteLock)
                        foreach (SiteNav nav in siteNavs.Values.ToList())
                            RemoveSite(nav);
            }
        }

        /// <summary>A placed prop that blocks creature paths: footprint centre (local), radius (m, scaled) and base height.</summary>
        public readonly record struct Obstacle(ulong Guid, Vector3 Centre, float Radius, float Base);

        /// <summary>One site's (or dug outpost's) state. Fields under <see cref="siteLock"/>.</summary>
        private sealed class SiteNav
        {
            public (uint Zone, ulong Id) Key;
            /// <summary>The claim's own landscape id (the stores' and Props' key); <see cref="Key"/> holds the normalised one.</summary>
            public uint StoreZone;
            public bool Outpost, HasBox;
            public VoxelBox Box;
            public Vector3 Min, Max;
            public Sight.Footprint Grid;
            public List<Obstacle> Obstacles = new();
            public Dictionary<long, List<(float Lo, float Hi)>> ObstacleMap;
            public long PropSignature;
            /// <summary>Ready: a grid was built once (it is laid over the tiles). Building: a build is in flight.</summary>
            public volatile bool Ready;
            public bool Building, VoxelsDirty, PropsDirty, Removed;
            public DateTime ChangedAt = DateTime.MinValue, PropsChangedAt = DateTime.MinValue, LastUsed, RetryAt, NextPropCheck;
            public string LastReport = "not built yet";
        }

        /// <summary>What the tile builds read: one immutable layer per ready site (replaced whole on every change).</summary>
        private sealed class SiteLayer
        {
            public Sight.Footprint Grid;
            public Dictionary<long, List<(float Lo, float Hi)>> Obstacles;
            public Vector3 Min, Max;
            public bool CaveOnly;
        }

        private static readonly Dictionary<(uint Zone, ulong Id), SiteNav> siteNavs = new();
        private static readonly object siteLock = new();
        private static readonly ConcurrentDictionary<uint, SiteLayer[]> siteLayers = new();
        private static int siteBuildsInFlight;
        private static long siteBuilds, siteBuildTicks, siteBuildFailures;
        private static DateTime nextSiteUpdate, lastSiteFailureLog;

        private static long ColumnKey(int ix, int iz) => ((long)ix << 32) ^ (uint)iz;

        /// <summary>
        /// World thread (Creatures.NavMode, every step of a story creature): is the site's grid built, so the creature may
        /// path on it? The first ask registers the site (built in the background by <see cref="UpdateSites"/>); until it is
        /// ready the answer is false and the creature steps straight, as before.
        /// </summary>
        public static bool SiteReady(uint zone, ulong claimId)
        {
            if (!Enabled || !SitesEnabled || claimId == 0)
                return false;
            var key = (Norm(zone), claimId);
            lock (siteLock)
            {
                if (siteNavs.TryGetValue(key, out SiteNav nav))
                {
                    nav.LastUsed = DateTime.UtcNow;
                    return nav.Ready;
                }
                MakeRoom();
                siteNavs[key] = new SiteNav { Key = key, StoreZone = zone, LastUsed = DateTime.UtcNow, VoxelsDirty = true };
                log.Info($"Site pathing: site {claimId} on landscape {key.Item1} registered (first story creature step); grid follows.");
                return false;
            }
        }

        /// <summary>Under siteLock: drops the least recently used entry when <see cref="MaxSites"/> are held.</summary>
        private static void MakeRoom()
        {
            if (siteNavs.Count < MaxSites)
                return;
            SiteNav oldest = siteNavs.Values.OrderBy(n => n.Outpost ? n.ChangedAt : n.LastUsed).First();
            RemoveSite(oldest);
        }

        /// <summary>Under siteLock: forgets an entry, its layer and the tiles under it.</summary>
        private static void RemoveSite(SiteNav nav)
        {
            siteNavs.Remove(nav.Key);
            nav.Removed = true;
            if (nav.Ready)
            {
                nav.Ready = false;
                Publish(nav.Key.Zone, nav);
            }
        }

        /// <summary>
        /// World thread (ZoneTerrain.Broadcast: every edit, undo, paste, heal tool, dig, blast, Linking Area destroy / heal,
        /// site revert, terrain heal and another zone's edits pass there): the sites and dug outposts whose box meets one of
        /// the cells are due a rebuild <see cref="SiteDebounce"/> after the last change.
        /// </summary>
        public static void CellsChanged(uint zone, IReadOnlyCollection<ulong> cells)
        {
            if (cells.Count == 0)
                return;
            zone = Norm(zone);
            lock (siteLock)
            {
                if (siteNavs.Count == 0)
                    return;
                DateTime now = DateTime.UtcNow;
                foreach (SiteNav nav in siteNavs.Values)
                {
                    if (nav.Key.Zone != zone || !nav.HasBox)
                        continue;
                    foreach (ulong cell in cells)
                    {
                        (Vector3 min, Vector3 max) = TerrainStore.LocalBounds(cell);
                        if (min.X > nav.Max.X || max.X < nav.Min.X || min.Y > nav.Max.Y || max.Y < nav.Min.Y || min.Z > nav.Max.Z || max.Z < nav.Min.Z)
                            continue;
                        nav.VoxelsDirty = true;
                        nav.ChangedAt = now;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// World thread (Abilities.Mine, after its broadcast): digging (or dynamite) in a Chaos Cavern outpost's box gives the
        /// outpost a grid of its own, so its creatures see the holes (the optional step of spec B1; caves only).
        /// </summary>
        public static void Dug(uint zone, IReadOnlyCollection<ulong> cells)
        {
            if (!Enabled || !SitesEnabled || cells.Count == 0 || !CavePieces.HasData(zone))
                return;
            uint key = Norm(zone);
            EnsureRuins(key);
            Sight.Footprint[] ruins = ruinsByZone.GetValueOrDefault(key);
            if (ruins == null || ruins.Length == 0)
                return;
            DateTime now = DateTime.UtcNow;
            var origin = TerrainStore.ZoneOrigin;
            lock (siteLock)
                foreach (Sight.Footprint f in ruins)
                {
                    bool meets = false;
                    foreach (ulong cell in cells)
                    {
                        (Vector3 min, Vector3 max) = TerrainStore.LocalBounds(cell);
                        if (min.X <= f.Max.X && max.X >= f.Min.X && min.Y <= f.Max.Y && max.Y >= f.Min.Y && min.Z <= f.Max.Z && max.Z >= f.Min.Z)
                        {
                            meets = true;
                            break;
                        }
                    }
                    if (!meets)
                        continue;
                    ulong id = OutpostBit | ((ulong)(f.Corner.X & 0xFFFFF) << 40) | ((ulong)(f.Corner.Y & 0xFFFFF) << 20) | (ulong)(f.Corner.Z & 0xFFFFF);
                    if (!siteNavs.TryGetValue((key, id), out SiteNav nav))
                    {
                        MakeRoom();
                        siteNavs[(key, id)] = nav = new SiteNav
                        {
                            Key = (key, id), StoreZone = zone, Outpost = true, HasBox = true, Min = f.Min, Max = f.Max,
                            Box = new VoxelBox(f.Min.X * 10.0 + origin.X, f.Min.Y * 10.0 + origin.Y, f.Min.Z * 10.0 + origin.Z,
                                f.Max.X * 10.0 + origin.X, f.Max.Y * 10.0 + origin.Y, f.Max.Z * 10.0 + origin.Z),
                            LastUsed = now
                        };
                        log.Info($"Site pathing: outpost at voxel {f.Corner} on landscape {key} dug into; its grid follows.");
                    }
                    nav.VoxelsDirty = true;
                    nav.ChangedAt = now;
                }
        }

        /// <summary>
        /// World tick: resolves new sites' boxes, notices prop changes (once a second per site), starts the debounced
        /// rebuilds (one at a time, on a pool thread: the Environment Server call may wait for a remote service) and forgets
        /// idle entries.
        /// </summary>
        public static void UpdateSites()
        {
            DateTime now = DateTime.UtcNow;
            if (now < nextSiteUpdate)
                return;
            nextSiteUpdate = now + SiteUpdateInterval;
            List<SiteNav> navs;
            lock (siteLock)
            {
                if (siteNavs.Count == 0)
                    return;
                navs = siteNavs.Values.ToList();
            }
            foreach (SiteNav nav in navs)
            {
                // The site box and the props (Claims / Props locks: taken outside siteLock).
                LandmarkEmulator.Database.Character.Model.ClaimModel claim = null;
                if (!nav.Outpost && !nav.HasBox && (claim = Claims.Get(nav.Key.Id)) == null)
                {
                    lock (siteLock)
                        RemoveSite(nav);
                    continue;
                }
                List<Obstacle> obstacles = null;
                if (!nav.Outpost && now >= nav.NextPropCheck)
                {
                    nav.NextPropCheck = now + PropCheckInterval;
                    Vector3 min = nav.HasBox ? nav.Min : Local(claim.MinX, claim.MinY, claim.MinZ),
                        max = nav.HasBox ? nav.Max : Local(claim.MaxX, claim.MaxY, claim.MaxZ);
                    obstacles = Props.PathObstaclesIn(nav.StoreZone, min, max);
                }
                lock (siteLock)
                {
                    if (nav.Removed)
                        continue;
                    if (claim != null)
                    {
                        nav.Box = new VoxelBox(claim.MinX, claim.MinY, claim.MinZ, claim.MaxX, claim.MaxY, claim.MaxZ);
                        nav.Min = Local(claim.MinX, claim.MinY, claim.MinZ);
                        nav.Max = Local(claim.MaxX, claim.MaxY, claim.MaxZ);
                        nav.HasBox = true;
                    }
                    if (nav.Outpost ? now - nav.ChangedAt > OutpostIdle : now - nav.LastUsed > SiteIdle)
                    {
                        log.Info($"Site pathing: {Name(nav)} forgotten (idle).");
                        RemoveSite(nav);
                        continue;
                    }
                    if (obstacles != null)
                    {
                        long signature = Signature(obstacles);
                        if (signature != nav.PropSignature)
                        {
                            nav.PropSignature = signature;
                            nav.Obstacles = obstacles;
                            nav.PropsDirty = true;
                            nav.PropsChangedAt = now;
                        }
                    }
                    // A voxel change: the Environment Server is asked again (the props come along).
                    if (nav.VoxelsDirty && !nav.Building && now >= nav.RetryAt && Volatile.Read(ref siteBuildsInFlight) == 0
                        && (!nav.Ready || now - nav.ChangedAt >= SiteDebounce))
                    {
                        StartBuild(nav, now);
                        continue;
                    }
                    // Props only: the obstacles are laid again over the grid already held (no service call).
                    if (nav.PropsDirty && nav.Ready && !nav.Building && !nav.VoxelsDirty && now - nav.PropsChangedAt >= SiteDebounce)
                    {
                        nav.PropsDirty = false;
                        nav.ObstacleMap = ObstacleMap(nav.Obstacles);
                        nav.LastReport = $"{nav.Obstacles.Count} prop obstacle(s) laid again at {now:HH:mm:ss} (props changed)";
                        log.Info($"Site pathing: {Name(nav)}: {nav.LastReport}.");
                        Publish(nav.Key.Zone, nav);
                    }
                }
            }
        }

        private static string Name(SiteNav nav) => nav.Outpost ? $"outpost {nav.Key.Id & ~OutpostBit:X} on {nav.Key.Zone}" : $"site {nav.Key.Id} on {nav.Key.Zone}";

        private static Vector3 Local(double gx, double gy, double gz)
        {
            var origin = TerrainStore.ZoneOrigin;
            return new Vector3((float)((gx - origin.X) / 10), (float)((gy - origin.Y) / 10), (float)((gz - origin.Z) / 10));
        }

        private static long Signature(List<Obstacle> obstacles)
        {
            long h = obstacles.Count;
            foreach (Obstacle o in obstacles)
                h = h * 31 + HashCode.Combine(o.Guid, MathF.Round(o.Centre.X * 10), MathF.Round(o.Centre.Z * 10), MathF.Round(o.Base * 10),
                    MathF.Round(o.Radius * 10));
            return h;
        }

        /// <summary>Under siteLock, world thread: the Environment Server call on a pool thread; the result is published there.</summary>
        private static void StartBuild(SiteNav nav, DateTime now)
        {
            nav.Building = true;
            nav.VoxelsDirty = false;
            nav.PropsDirty = false;
            bool first = !nav.Ready;
            DateTime changed = nav.ChangedAt;
            VoxelBox box = nav.Box;
            uint zone = nav.StoreZone;
            List<Obstacle> obstacles = nav.Obstacles;
            Interlocked.Increment(ref siteBuildsInFlight);
            System.Threading.Tasks.Task.Run(() =>
            {
                long started = Stopwatch.GetTimestamp();
                SolidityGrid grid = null;
                try
                {
                    grid = EnvironmentServer.Instance?.SolidityOf(zone, box);
                }
                catch (Exception e)
                {
                    log.Error(e, $"Site pathing: {Name(nav)}: solidity grid failed.");
                }
                long elapsed = Stopwatch.GetTimestamp() - started;
                Dictionary<long, List<(float Lo, float Hi)>> map = grid != null ? ObstacleMap(obstacles) : null;
                lock (siteLock)
                {
                    nav.Building = false;
                    if (grid == null)
                    {
                        // Service away (or failed): keep what is laid now, ask again later.
                        nav.VoxelsDirty = true;
                        nav.RetryAt = DateTime.UtcNow + SiteRetry;
                        Interlocked.Increment(ref siteBuildFailures);
                        if (DateTime.UtcNow - lastSiteFailureLog > TimeSpan.FromMinutes(1))
                        {
                            lastSiteFailureLog = DateTime.UtcNow;
                            log.Warn($"Site pathing: {Name(nav)}: no solidity grid from the Environment Server; trying again in {SiteRetry.TotalSeconds:0} s.");
                        }
                    }
                    else if (!nav.Removed)
                    {
                        nav.Grid = Sight.Footprint.FromSite(grid);
                        nav.ObstacleMap = map;
                        if (!ReferenceEquals(obstacles, nav.Obstacles))
                            nav.PropsDirty = true;   // props changed while it was built: laid again after the debounce
                        nav.Ready = true;
                        Interlocked.Increment(ref siteBuilds);
                        Interlocked.Add(ref siteBuildTicks, elapsed);
                        nav.LastReport = $"grid {grid.NX}×{grid.NY}×{grid.NZ} blocks from {grid.Cells} stored cell(s): {grid.WrittenBlocks} written, "
                            + $"{grid.SolidBlocks} solid; {obstacles.Count} prop obstacle(s); built in {elapsed * 1000.0 / Stopwatch.Frequency:0} ms "
                            + $"at {DateTime.UtcNow:HH:mm:ss} ({(first ? "first build" : $"rebuild {(DateTime.UtcNow - changed).TotalSeconds:0.0} s after the last change")})";
                        log.Info($"Site pathing: {Name(nav)}: {nav.LastReport}.");
                        Publish(nav.Key.Zone, nav);
                    }
                }
                Interlocked.Decrement(ref siteBuildsInFlight);
            });
        }

        /// <summary>
        /// The columns each obstacle blocks: those whose centre lies within its radius of the footprint centre (its own column
        /// always), from its base to <see cref="Clearance"/> above it.
        /// </summary>
        private static Dictionary<long, List<(float Lo, float Hi)>> ObstacleMap(List<Obstacle> obstacles)
        {
            if (obstacles == null || obstacles.Count == 0)
                return null;
            var map = new Dictionary<long, List<(float Lo, float Hi)>>();
            void Add(int ix, int iz, float lo, float hi)
            {
                long key = ColumnKey(ix, iz);
                if (!map.TryGetValue(key, out var list))
                    map[key] = list = new List<(float, float)>();
                list.Add((lo, hi));
            }
            foreach (Obstacle o in obstacles)
            {
                float r = Math.Min(o.Radius, MaxObstacleRadius), lo = o.Base, hi = o.Base + Clearance;
                int cx = ColumnOf(o.Centre.X), cz = ColumnOf(o.Centre.Z), reach = (int)MathF.Ceiling(r / Cell);
                Add(cx, cz, lo, hi);
                for (int ix = cx - reach; ix <= cx + reach; ix++)
                    for (int iz = cz - reach; iz <= cz + reach; iz++)
                    {
                        if (ix == cx && iz == cz)
                            continue;
                        float dx = CentreOf(ix) - o.Centre.X, dz = CentreOf(iz) - o.Centre.Z;
                        if (dx * dx + dz * dz <= r * r)
                            Add(ix, iz, lo, hi);
                    }
            }
            return map;
        }

        /// <summary>
        /// Under siteLock: the landscape's layer list again from its ready entries, and the tiles under the changed one
        /// dropped (built again on next need, with the new layer).
        /// </summary>
        private static void Publish(uint zone, SiteNav changed)
        {
            siteLayers[zone] = siteNavs.Values.Where(n => n.Key.Zone == zone && n.Ready && n.HasBox)
                .Select(n => new SiteLayer { Grid = n.Grid, Obstacles = n.ObstacleMap, Min = n.Min, Max = n.Max, CaveOnly = n.Outpost })
                .ToArray();
            Interlocked.Increment(ref generation);
            float size = TileColumns * Cell;
            for (int tx = (int)MathF.Floor(changed.Min.X / size); tx <= (int)MathF.Floor(changed.Max.X / size); tx++)
                for (int tz = (int)MathF.Floor(changed.Min.Z / size); tz <= (int)MathF.Floor(changed.Max.Z / size); tz++)
                {
                    tiles.TryRemove(Key(zone, Mode.Cave, tx, tz), out _);
                    tiles.TryRemove(Key(zone, Mode.Surface, tx, tz), out _);
                }
        }

        /// <summary>Removes [lo, hi) from sorted, separate spans (local metres).</summary>
        private static void SubtractSpan(List<(float Lo, float Hi)> spans, float lo, float hi)
        {
            for (int i = 0; i < spans.Count; i++)
            {
                (float a, float b) = spans[i];
                if (b <= lo || a >= hi)
                    continue;
                spans.RemoveAt(i);
                if (b > hi)
                    spans.Insert(i, (hi, b));
                if (a < lo)
                    spans.Insert(i, (a, lo));
                i += (a < lo ? 1 : 0) + (b > hi ? 1 : 0) - 1;
            }
        }

        /// <summary>The admin monitor's site-grid numbers (ours): grids held / ready, builds, failures, average build time.</summary>
        private static object SiteMonitor()
        {
            int held, ready;
            lock (siteLock)
            {
                held = siteNavs.Count;
                ready = siteNavs.Values.Count(n => n.Ready);
            }
            long builds = Interlocked.Read(ref siteBuilds);
            return new
            {
                enabled = SitesEnabled,
                held,
                ready,
                builds,
                failures = Interlocked.Read(ref siteBuildFailures),
                avgMs = builds > 0 ? Math.Round(Interlocked.Read(ref siteBuildTicks) * 1000.0 / Stopwatch.Frequency / builds, 1) : 0
            };
        }

        /// <summary>Dev "sitepathing" / "pathing": one line per site or dug outpost held.</summary>
        public static List<string> ExplainSites()
        {
            lock (siteLock)
                return siteNavs.Values.Select(n => $"{Name(n)}: {(n.Ready ? "ready" : "not ready")}{(n.Building ? ", building" : "")}"
                    + $"{(n.VoxelsDirty ? ", voxels changed" : "")}{(n.PropsDirty ? ", props changed" : "")}; {n.LastReport}").ToList();
        }

        /// <summary>Dev: the state of one site for a creature line.</summary>
        public static string SiteState(uint zone, ulong claimId)
        {
            lock (siteLock)
                return siteNavs.TryGetValue((Norm(zone), claimId), out SiteNav n)
                    ? $"site {claimId} grid {(n.Ready ? "ready" : "not ready")}{(n.Building ? " (building)" : "")}"
                    : $"site {claimId} not registered";
        }

        // ---- outposts, monitor, dev -----------------------------------------------------------------------------------

        /// <summary>
        /// World thread (Outposts, when an outpost goes live or leaves its cavern for another ruin): the landscape's ruin
        /// snapshot is taken again and the cave tiles under the outposts that are new since the last one, or gone, are
        /// dropped (built again on next need).
        /// </summary>
        public static void OutpostsChanged(uint zone)
        {
            zone = Norm(zone);
            Sight.Footprint[] now = Sight.Footprints(zone);
            Sight.Footprint[] before = ruinsByZone.GetValueOrDefault(zone) ?? Array.Empty<Sight.Footprint>();
            ruinsByZone[zone] = now;
            var known = before.Select(f => f.Corner).ToHashSet();
            var still = now.Select(f => f.Corner).ToHashSet();
            float size = TileColumns * Cell;
            Interlocked.Increment(ref generation);
            foreach (Sight.Footprint f in now.Where(f => !known.Contains(f.Corner)).Concat(before.Where(f => !still.Contains(f.Corner))))
                for (int tx = (int)MathF.Floor(f.Min.X / size); tx <= (int)MathF.Floor(f.Max.X / size); tx++)
                    for (int tz = (int)MathF.Floor(f.Min.Z / size); tz <= (int)MathF.Floor(f.Max.Z / size); tz++)
                        tiles.TryRemove(Key(zone, Mode.Cave, tx, tz), out _);
        }

        /// <summary>
        /// The page-32 numbers (admin monitor, /admin/monitor "Pathing Information - Zone"). Field meanings are ours [I]:
        /// NavMeshCells = tiles cached; PendingStitchSections = tiles waiting to be built; Characters = creatures with a path
        /// or a query (<paramref name="characters"/>); DynamicSilhouettes / AvoidanceObstacles = 0 (no moving obstacles);
        /// NavMeshJobsRemaining = jobs queued; NavMeshJobsCompletedThisFrame = jobs done per second since the last look;
        /// NavMeshJobsCompletedLifetime = all jobs done. Plus our timing table (tile builds, path queries).
        /// </summary>
        public static object Monitor(int characters)
        {
            long now = Stopwatch.GetTimestamp(), done = Interlocked.Read(ref completedLifetime);
            double seconds = lastMonitorAt == 0 ? 0 : (now - lastMonitorAt) / (double)Stopwatch.Frequency;
            double perSecond = seconds > 0 ? (done - lastCompleted) / seconds : 0;
            lastMonitorAt = now;
            lastCompleted = done;
            double Us(long ticks) => Math.Round(ticks * 1_000_000.0 / Stopwatch.Frequency, 1);
            long tb = Interlocked.Read(ref tileBuilds), pq = Interlocked.Read(ref pathQueries);
            long bytes = tiles.Values.Sum(t => (long)t.Start.Length * 4 + t.Floor.Length * 4L + 48);
            return new
            {
                enabled = Enabled,
                hasData = tb > 0 || pq > 0,
                navMeshCells = tiles.Count,
                pendingStitchSections = pendingTiles.Count,
                characters,
                dynamicSilhouettes = 0,
                avoidanceObstacles = 0,
                navMeshJobsRemaining = Math.Max(0, Volatile.Read(ref queued)),
                navMeshJobsCompletedThisFrame = Math.Round(perSecond, 1),
                navMeshJobsCompletedLifetime = done,
                tileKb = bytes / 1024,
                maxTiles = MaxTiles,
                sites = SiteMonitor(),
                paths = new
                {
                    queries = pq,
                    reached = Interlocked.Read(ref pathsReached),
                    partial = Interlocked.Read(ref pathsPartial),
                    none = Interlocked.Read(ref pathsNone),
                    refused = Interlocked.Read(ref pathsRefused),
                    avgNodes = pq > 0 ? Interlocked.Read(ref pathNodes) / pq : 0
                },
                timings = new[]
                {
                    new { name = "tile build", count = tb, avgUs = tb > 0 ? Us(Interlocked.Read(ref tileTicks) / tb) : 0,
                        minUs = tb > 0 ? Us(Interlocked.Read(ref tileMin)) : 0, maxUs = Us(Interlocked.Read(ref tileMax)) },
                    new { name = "path query", count = pq, avgUs = pq > 0 ? Us(Interlocked.Read(ref pathTicks) / pq) : 0,
                        minUs = pq > 0 ? Us(Interlocked.Read(ref pathMin)) : 0, maxUs = Us(Interlocked.Read(ref pathMax)) }
                }
            };
        }

        /// <summary>Dev command "pathing": the floors of the column at a point, and the cache state.</summary>
        public static string Explain(uint zone, Mode mode, Vector3 at)
        {
            zone = Norm(zone);
            int ix = ColumnOf(at.X), iz = ColumnOf(at.Z);
            string column;
            if (!Get(zone, mode, ix, iz, false, out Tile t, out int c))
                column = "tile not built yet (requested)";
            else
            {
                var floors = Enumerable.Range(0, Count(t, c)).Select(k => $"{FloorOf(t, c, k):0.0}..{(t.Ceiling[t.Start[c] + k] == short.MaxValue ? "sky" : CeilingOf(t, c, k).ToString("0.0"))}");
                column = $"floors (floor..ceiling m) [{string.Join(", ", floors)}]";
            }
            return $"{mode} column ({ix}, {iz}) on landscape {zone}: {column}; {tiles.Count} tiles cached, {pendingTiles.Count} pending, "
                + $"{Math.Max(0, Volatile.Read(ref queued))} jobs queued, {Interlocked.Read(ref pathQueries)} paths "
                + $"({Interlocked.Read(ref pathsReached)} reached, {Interlocked.Read(ref pathsPartial)} partial, {Interlocked.Read(ref pathsNone)} none); "
                + $"pathing {(Enabled ? "on" : "off")}";
        }

        private static void Max(ref long target, long value)
        {
            long seen;
            while (value > (seen = Interlocked.Read(ref target)) && Interlocked.CompareExchange(ref target, value, seen) != seen)
            {
            }
        }

        private static void Min(ref long target, long value)
        {
            long seen;
            while (value < (seen = Interlocked.Read(ref target)) && Interlocked.CompareExchange(ref target, value, seen) != seen)
            {
            }
        }
    }
}
