using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// Pending heals of destroyed ground (trace/architecture/terrain-service.md §4.3; job type 4 Cell Temporary
    /// Modifications). A developer described live's model (27 Jul 2016, trace/live-server-architecture.md): destruction
    /// lays a volume of empty space on the destruction layer and queues a heal request for a set time; a new destroy
    /// does not cancel an earlier pending heal. So every dig / mining swing / explosion adds
    /// one entry {its volume, the cells it touched, due = now + the heal delay}; when an entry is due, the destruction
    /// layer's voxels inside its volume go back to the generated ground — except voxels a still-pending later volume
    /// also covers (that destruction heals at its own time; not for <see cref="IgnoresLater"/> entries) and, for entries
    /// written before 2026-10-16 (no <see cref="HealsInSites"/> flag), voxels inside build sites. Since 0.695's Play Mode
    /// destruction (TASKS 2.1 / 2.6) build sites can hold destruction-layer voxels (dynamite, Linking Area destroy); they
    /// are temporary like everywhere else (P6006, P6037) and heal.
    /// How live bounded a heal exactly (volume, cells, box) is not known (L76); the volume is what the dev text says.
    /// <para>
    /// The queue survives restarts: entries are appended to heal-queue.log beside the cell files (record "A" = entry,
    /// "D" = healed) and the file is compacted on load. Destruction-layer voxels from before the queue existed (no entry
    /// covers their cell) get a whole-cell entry due at their file time plus the delay, which is what the old
    /// "untouched for 20 minutes" rule gave them.
    /// </para>
    /// </summary>
    public static class HealQueue
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Entry flags (heal-queue.log field 10; lines written before 2026-10-16 have none = 0).
        /// <para><see cref="HealsInSites"/>: the destruction heals inside build sites too. Every destruction-layer voxel is
        /// temporary (terrain-service.md §4.3; dynamite holes on Play Mode sites heal after a short while, P6006, the
        /// same way as anywhere else, P6037), so entries written from now on carry
        /// it; older entries keep the old safety net (site voxels left alone).</para>
        /// <para><see cref="IgnoresLater"/>: the heal also takes voxels a later, still pending destruction covers. A
        /// developer tested the Linking Area destroy with a 10 s timer: destroying again before the time ran out did
        /// not stop the first heal (P6205) — the first request heals the area at its own time. Used
        /// for Linking Area destruction; digs keep the "later digs keep their voxels" rule (§4.3).</para>
        /// </summary>
        public const byte HealsInSites = 1, IgnoresLater = 2;

        /// <summary>
        /// One pending heal: a sphere (global dm centre and radius); with <see cref="HalfHeight"/> at 0 or more a vertical
        /// cylinder (radius across x / z, half height up and down: the Linking Area shape, StoryTools.InArea); or, with
        /// <see cref="Radius"/> below 0, the whole of its cells. <see cref="Flags"/>: <see cref="HealsInSites"/>,
        /// <see cref="IgnoresLater"/>.
        /// </summary>
        public sealed record Entry(ulong Id, uint Zone, long DueMs, double X, double Y, double Z, double Radius, ulong[] Cells,
            double HalfHeight = -1, byte Flags = 0)
        {
            public bool WholeCells => Radius < 0;

            /// <summary>Does the volume hold a point (global dm)? The margin takes in the smoothed rim around a dig.</summary>
            public bool Holds(double x, double y, double z, ulong cell, double margin)
            {
                if (WholeCells)
                    return Array.IndexOf(Cells, cell) >= 0;
                double dx = x - X, dy = y - Y, dz = z - Z, r = Radius + margin;
                if (HalfHeight >= 0)
                    return dx * dx + dz * dz <= r * r && Math.Abs(dy) <= HalfHeight + margin;
                return dx * dx + dy * dy + dz * dz <= r * r;
            }

            public bool Has(byte flag) => (Flags & flag) != 0;
        }

        private static readonly object sync = new();
        private static readonly List<Entry> pending = new();
        private static ulong nextId = 1;
        private static string path;

        public static int Count
        {
            get
            {
                lock (sync)
                    return pending.Count;
            }
        }

        /// <summary>Loads the queue from <paramref name="folder"/>/heal-queue.log and rewrites it with what is still pending.</summary>
        public static void Load(string folder)
        {
            path = Path.Combine(folder, "heal-queue.log");
            var entries = new Dictionary<ulong, Entry>();
            if (File.Exists(path))
            {
                foreach (string line in File.ReadLines(path))
                {
                    string[] f = line.Split('\t');
                    try
                    {
                        if (f[0] == "A" && f.Length >= 9)
                        {
                            // Fields 9 / 10 (half height, flags) came 2026-10-16; older lines are spheres without flags.
                            var e = new Entry(ulong.Parse(f[1]), uint.Parse(f[2]), long.Parse(f[3]), double.Parse(f[4], Inv),
                                double.Parse(f[5], Inv), double.Parse(f[6], Inv), double.Parse(f[7], Inv),
                                f[8].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(c => Convert.ToUInt64(c, 16)).ToArray(),
                                f.Length >= 10 ? double.Parse(f[9], Inv) : -1, f.Length >= 11 ? byte.Parse(f[10], Inv) : (byte)0);
                            entries[e.Id] = e;
                        }
                        else if (f[0] == "D" && f.Length >= 2)
                            entries.Remove(ulong.Parse(f[1]));
                    }
                    catch (Exception)
                    {
                        log.Warn($"heal-queue.log: unreadable line \"{line}\" skipped.");
                    }
                }
            }
            lock (sync)
            {
                pending.Clear();
                pending.AddRange(entries.Values.OrderBy(e => e.DueMs));
                nextId = pending.Count > 0 ? pending.Max(e => e.Id) + 1 : 1;
                Rewrite();
            }
            log.Info($"Heal queue: {pending.Count} pending heal(s).");
        }

        private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        private static string Format(Entry e)
            => string.Join('\t', "A", e.Id, e.Zone, e.DueMs, e.X.ToString("R", Inv), e.Y.ToString("R", Inv), e.Z.ToString("R", Inv),
                e.Radius.ToString("R", Inv), string.Join(',', e.Cells.Select(c => c.ToString("X"))), e.HalfHeight.ToString("R", Inv),
                e.Flags.ToString(Inv));

        /// <summary>Writes the pending entries to a fresh file (atomic: temp file, then rename).</summary>
        private static void Rewrite()
        {
            if (path == null)
                return;
            string temp = path + ".tmp";
            File.WriteAllLines(temp, pending.Select(Format));
            File.Move(temp, path, overwrite: true);
        }

        private static void Append(string line)
        {
            if (path == null)
                return;
            try
            {
                File.AppendAllText(path, line + "\n");
            }
            catch (IOException e)
            {
                log.Warn($"heal-queue.log not written: {e.Message}");
            }
        }

        /// <summary>
        /// A destruction: its sphere (global dm) and cells heal together after <paramref name="delay"/>, inside build sites
        /// too (<see cref="HealsInSites"/>).
        /// </summary>
        public static void Add(uint zone, (double X, double Y, double Z) centre, double radius, IEnumerable<ulong> cells, TimeSpan delay)
            => Add(zone, centre, radius, cells, DateTime.UtcNow + delay, -1, HealsInSites);

        /// <summary>
        /// A destruction of a vertical cylinder (global dm centre, radius across x / z, half height up and down): the
        /// Linking Area's "...Destroy Voxels in this area." (577548). <paramref name="flags"/> as <see cref="Entry.Flags"/>.
        /// </summary>
        public static void AddCylinder(uint zone, (double X, double Y, double Z) centre, double radius, double halfHeight,
            IEnumerable<ulong> cells, TimeSpan delay, byte flags)
            => Add(zone, centre, radius, cells, DateTime.UtcNow + delay, Math.Max(0, halfHeight), flags);

        /// <summary>Whole cells healing at <paramref name="due"/> (destruction from before the queue).</summary>
        public static void AddWholeCells(uint zone, IEnumerable<ulong> cells, DateTime due)
            => Add(zone, (0, 0, 0), -1, cells, due, -1, 0);

        private static void Add(uint zone, (double X, double Y, double Z) centre, double radius, IEnumerable<ulong> cells, DateTime due,
            double halfHeight, byte flags)
        {
            ulong[] list = cells.Distinct().ToArray();
            if (list.Length == 0)
                return;
            Entry entry;
            lock (sync)
            {
                entry = new Entry(nextId++, zone, new DateTimeOffset(due).ToUnixTimeMilliseconds(), centre.X, centre.Y, centre.Z, radius, list,
                    halfHeight, flags);
                int at = pending.FindIndex(e => e.DueMs > entry.DueMs);
                pending.Insert(at < 0 ? pending.Count : at, entry);
                Append(Format(entry));
            }
        }

        /// <summary>Is any pending entry holding this cell (whole-cell adoption of old destruction checks this)?</summary>
        public static bool Covers(uint zone, ulong cell)
        {
            lock (sync)
                return pending.Any(e => e.Zone == zone && Array.IndexOf(e.Cells, cell) >= 0);
        }

        /// <summary>The entries due now, taken off the queue (oldest first); <see cref="Done"/> records them healed.</summary>
        public static List<Entry> TakeDue()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lock (sync)
            {
                int n = pending.FindIndex(e => e.DueMs > now);
                if (n < 0)
                    n = pending.Count;
                List<Entry> due = pending.GetRange(0, n);
                pending.RemoveRange(0, n);
                return due;
            }
        }

        /// <summary>The still-pending entries touching a cell (their destruction heals later).</summary>
        public static List<Entry> PendingIn(uint zone, ulong cell)
        {
            lock (sync)
                return pending.Where(e => e.Zone == zone && Array.IndexOf(e.Cells, cell) >= 0).ToList();
        }

        /// <summary>
        /// The still-open dug spheres (pending sphere entries, global dm) that reach within <paramref name="reach"/> of a
        /// sphere's surface: what a dig's surface vertices are computed against when holes overlap (TerrainEdits.SurfaceVertices).
        /// </summary>
        public static List<(double X, double Y, double Z, double Radius)> SpheresNear(uint zone, (double X, double Y, double Z) centre,
            double radius, double reach)
        {
            lock (sync)
                return pending.Where(e => e.Zone == zone && !e.WholeCells && e.HalfHeight < 0
                        && Math.Sqrt((e.X - centre.X) * (e.X - centre.X) + (e.Y - centre.Y) * (e.Y - centre.Y) + (e.Z - centre.Z) * (e.Z - centre.Z))
                            <= e.Radius + radius + reach)
                    .Select(e => (e.X, e.Y, e.Z, e.Radius)).ToList();
        }

        /// <summary>The entries were healed: recorded, and the file compacted now and then.</summary>
        public static void Done(IEnumerable<Entry> healed)
        {
            lock (sync)
            {
                foreach (Entry e in healed)
                    Append("D\t" + e.Id);
                if (++doneBatches % 60 == 0)
                    Rewrite();
            }
        }

        private static int doneBatches;
    }
}
