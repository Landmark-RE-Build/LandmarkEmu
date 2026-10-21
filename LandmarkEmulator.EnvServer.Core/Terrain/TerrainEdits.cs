using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// Player changes to the terrain, kept as voxel overlays per level-2 cell (16 m, 40³ voxels of 0.4 m). Clients
    /// composite the overlay over the terrain they generate themselves and mesh it, so the server never needs the
    /// whole world: only what players changed. A voxel with material 0 is air (mined away).
    /// <para>Voxel index order (verified in game): y fastest, then x, then z.</para>
    /// </summary>
    public static class TerrainEdits
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const uint Level = 2;
        /// <summary>Cell edge and voxel edge in world units (dm).</summary>
        public const double CellSize = 160, VoxelSize = CellSize / BlockData.Size;
        /// <summary>
        /// Block layers, as the client names them (FUN_143bb4ef0) and as the live manifest used them: 0 "Cell Poi" -
        /// server world stamps (Spire, outposts); 1 "Cell Permanent" - player building on build sites (the client
        /// commits its own edits of a level-2 cell into layer level - 1 = 1, FUN_143f5d2c0, and caches them there);
        /// 2 "Cell Timed Persistent" - digging outside build sites, which heals (with the ore veins under it);
        /// 3 "Cell Transient" - unused. A CellsUpdate carries every layer of a cell; a layer it leaves out keeps the
        /// client's old copy, so all three are always sent (empty where the server has nothing).
        /// </summary>
        public const uint PoiLayer = 0, BuildLayer = 1, DigLayer = 2;
        public const uint LayerCount = 3;

        /// <summary>
        /// The store key of a cell's layer: the cell id with the layer in bits 56-57 (unused by level-2+ cell ids:
        /// x needs 18 bits, ending at bit 53; TerrainStore.Decode masks them out). Layer 0 keys equal the cell id, so
        /// files saved before layers load as layer 0.
        /// </summary>
        public static ulong Key(ulong cell, uint layer) => cell | ((ulong)layer << 56);
        public static ulong CellOf(ulong key) => key & ~(3UL << 56);
        public static uint LayerOf(ulong key) => (uint)(key >> 56) & 3;

        /// <summary>
        /// One edited cell. Expanded, a cell is 384 KB (flags, u16 materials, 3-byte offsets for 40³ voxels); most are
        /// nearly empty and compress to a few KB. So a cell is kept compact (<see cref="BlockData.Save"/>'s zlib form)
        /// and expanded only when touched (<see cref="Blocks"/>); <see cref="CompactIdle"/> packs cells again once they
        /// have been idle for <see cref="IdleBeforeCompact"/>, and keeps at most <see cref="MaxExpanded"/> expanded.
        /// A saved cell idle for <see cref="IdleBeforeSpill"/> also drops its compact bytes (<see cref="Spill"/>): its
        /// file has them, and the next use reads them back. Every access happens under <see cref="sync"/>.
        /// </summary>
        private class Overlay
        {
            private BlockData blocks;
            private byte[] packed;
            /// <summary>Out of memory (<see cref="Spill"/>): the voxels are read back from <see cref="Pack"/> at <see cref="Stored"/>.</summary>
            private bool spilled;

            /// <summary>The cell's voxels, expanded on first use since it was last compacted (or spilled).</summary>
            public BlockData Blocks
            {
                get
                {
                    if (blocks == null)
                    {
                        blocks = new BlockData();
                        if (packed == null && spilled)
                            packed = ReadSpilled();
                        spilled = false;
                        if (packed != null)
                            blocks.Load(packed);
                        packed = null;
                    }
                    LastUse = Environment.TickCount64;   // cheap: read per voxel in the edit loops
                    return blocks;
                }
            }

            public bool Expanded => blocks != null;
            /// <summary>When the cell was last touched (Environment.TickCount64, ms).</summary>
            public long LastUse { get; private set; }

            /// <summary>Back to the compact form (Fastest zlib; the files keep the Optimal one).</summary>
            public void Compact()
            {
                if (blocks == null)
                    return;
                packed = blocks.Save(System.IO.Compression.CompressionLevel.Fastest);
                blocks = null;
            }

            /// <summary>A cell straight from its saved form (stays compact until used; idle from now).</summary>
            public static Overlay FromSaved(byte[] saved) => new() { packed = saved, LastUse = Environment.TickCount64 };

            /// <summary>
            /// Its landscape's cell pack holds what memory holds as of the last save (<see cref="Pack"/> at
            /// <see cref="Stored"/>): set when loaded and when <see cref="Flush"/> has appended it; cleared when the cell was
            /// dropped. Spilling also waits <see cref="SavedBeforeSpill"/> after the last save, so an append still in
            /// progress is never relied on.
            /// </summary>
            public bool OnDisk { get; set; }
            public CellPack Pack { get; set; }
            public CellPack.Entry Stored { get; set; }

            /// <summary>A cell as its pack holds it, not read yet (read on first use).</summary>
            public static Overlay FromPack(CellPack pack, CellPack.Entry entry) => new()
            {
                Pack = pack, Stored = entry, spilled = true, OnDisk = true, Version = entry.Version, LastEdit = entry.Saved,
                LastUse = Environment.TickCount64
            };

            /// <summary>Drops the compact bytes of a saved cell; its pack record holds the same voxels.</summary>
            public void Spill()
            {
                if (blocks != null || packed == null || Dirty || !OnDisk || Pack == null)
                    return;
                packed = null;
                spilled = true;
            }

            /// <summary>The voxels of a spilled cell from its pack record (an unreadable record reads as an empty cell).</summary>
            private byte[] ReadSpilled()
            {
                try
                {
                    return Pack.Read(Stored);
                }
                catch (Exception e) when (e is IOException or ObjectDisposedException)
                {
                    log.Warn($"Spilled terrain cell at {Stored.Offset} unreadable ({e.Message}): read as empty.");
                    return null;
                }
            }

            /// <summary>
            /// The compact (saved) form without expanding the cell: the in-memory copy, or a spilled cell's pack record
            /// (read, not kept); null when expanded (read <see cref="Blocks"/> instead) or unreadable. Under the store's lock.
            /// </summary>
            public byte[] SavedBytes() => blocks != null ? null : packed ?? (spilled ? ReadSpilled() : null);

            public bool Spilled => spilled;
            public int CompactBytes => packed?.Length ?? 0;
            public uint Version { get; set; }
            public bool Dirty { get; set; }
            /// <summary>When the cell last changed (set when it is saved; at load, the file's time).</summary>
            public DateTime LastEdit { get; set; } = DateTime.UtcNow;
        }

        private static readonly object sync = new();
        /// <summary>
        /// Overlays per landscape (StaticZoneData zone id). The home continent's are saved in the TerrainEdits folder
        /// itself (as before landscapes), other landscapes' in TerrainEdits/&lt;zone&gt;/.
        /// </summary>
        private static readonly Dictionary<uint, Dictionary<ulong, Overlay>> stores = new();
        private static string folder;

        private static uint Home => TerrainStore.HomeContinent;

        /// <summary>A landscape's overlays (0 = the home continent). Callers hold <see cref="sync"/>.</summary>
        private static Dictionary<ulong, Overlay> Of(uint zone)
        {
            zone = zone == 0 ? Home : zone;
            if (!stores.TryGetValue(zone, out var store))
                stores[zone] = store = new Dictionary<ulong, Overlay>();
            return store;
        }

        private static string FolderOf(uint zone) => zone == Home ? folder : Path.Combine(folder, zone.ToString());

        /// <summary>Each landscape's cell pack (<see cref="CellPack"/>: TerrainEdits/cells.pack, TerrainEdits/&lt;zone&gt;/cells.pack).</summary>
        private static readonly Dictionary<uint, CellPack> packs = new();

        private static CellPack PackOf(uint zone)
        {
            zone = zone == 0 ? Home : zone;
            lock (packs)
            {
                if (!packs.TryGetValue(zone, out CellPack pack))
                    packs[zone] = pack = new CellPack(FolderOf(zone));
                return pack;
            }
        }

        public static int Count
        {
            get { lock (sync) return stores.Values.Sum(s => s.Count); }
        }

        public static int VoxelIndex(int x, int y, int z) => y + BlockData.Size * (x + BlockData.Size * z);

        /// <summary>Loads saved overlays (per landscape one <see cref="CellPack"/>; older one-file-per-cell folders are packed once).</summary>
        public static void Load(string directory)
        {
            folder = directory;
            Directory.CreateDirectory(folder);
            var zones = new List<(uint Zone, string Folder)> { (Home, folder) };
            foreach (string sub in Directory.GetDirectories(folder))
                if (uint.TryParse(Path.GetFileName(sub), out uint z))
                    zones.Add((z, sub));
            foreach ((uint zone, string zoneFolder) in zones)
                LoadZone(zone, zoneFolder);
            VoxelOwnership.Load(folder);
            RepairInteriorAir();
            log.Info($"Loaded {Count} edited terrain cells on {stores.Count} landscape(s).");
            HealQueue.Load(folder);
            AdoptOldDestruction();
        }

        /// <summary>
        /// Placed designs and pastes stored before 2026-10-21 hold the copy's interior empty space as a raw material 0xFFFC
        /// (<see cref="VoxelTemplate.InteriorAir"/>) in the build layer. The client draws it as air but builds occlusion quads
        /// from it (Building.PastedMaterial has the trace), so standing inside such a design hid every terrain cell around
        /// it. At every start the build layer's cells are checked (flags and materials only, no full expansion) and each
        /// such voxel becomes air (material 0, what it looked like), at a new cell version so clients fetch the cell again
        /// and the coarse copies (TerrainLod, built after the load) follow; the voxels lose their owner (air has none).
        /// Only the build layer: stamps (layer 0) always wrote it as air and digs (layer 2) never hold it. Returns the cells
        /// repaired (<paramref name="onlyZone"/>: one landscape, for tests).
        /// </summary>
        internal static int RepairInteriorAir(uint? onlyZone = null)
        {
            var flags = new byte[BlockData.Count];
            var materials = new ushort[BlockData.Count];
            var repaired = new List<(uint Zone, ulong Cell, List<int> Indices)>();
            lock (sync)
                foreach ((uint zone, var overlays) in stores)
                    foreach ((ulong key, Overlay overlay) in overlays)
                    {
                        if (LayerOf(key) != BuildLayer || (onlyZone is uint only && zone != only))
                            continue;
                        byte[] saved = overlay.SavedBytes();
                        if (saved == null ? !HasInteriorAir(overlay.Blocks.Flags, overlay.Blocks.Materials)
                            : !BlockData.LoadSolidity(saved, flags, materials) || !HasInteriorAir(flags, materials))
                            continue;
                        BlockData blocks = overlay.Blocks;
                        var indices = new List<int>();
                        for (int i = 0; i < BlockData.Count; i++)
                            if ((blocks.Flags[i] & 1) != 0 && blocks.Materials[i] == VoxelTemplate.InteriorAir)
                            {
                                blocks.Materials[i] = 0;
                                indices.Add(i);
                            }
                        overlay.Version++;
                        overlay.Dirty = true;
                        repaired.Add((zone, CellOf(key), indices));
                    }
            foreach ((uint zone, ulong cell, List<int> indices) in repaired)
                VoxelOwnership.Set(zone, cell, indices, _ => false, 0);
            if (repaired.Count > 0)
                log.Info($"Terrain store: interior empty space (material 0xFFFC) of placed designs written as air in {repaired.Count} cell(s) "
                    + $"({repaired.Sum(r => r.Indices.Count)} voxels).");
            return repaired.Count;
        }

        private static bool HasInteriorAir(byte[] flags, ushort[] materials)
        {
            for (int i = 0; i < BlockData.Count; i++)
                if ((flags[i] & 1) != 0 && materials[i] == VoxelTemplate.InteriorAir)
                    return true;
            return false;
        }

        /// <summary>
        /// Destruction-layer cells no pending heal covers (written before the heal queue existed) heal whole, at their file
        /// time plus the delay — what the old rule gave them (<see cref="HealQueue"/>).
        /// </summary>
        private static void AdoptOldDestruction()
        {
            int adopted = 0;
            lock (sync)
                foreach ((uint zone, var overlays) in stores)
                    foreach ((ulong key, Overlay overlay) in overlays)
                        if (LayerOf(key) == DigLayer && !HealQueue.Covers(zone, CellOf(key)))
                        {
                            HealQueue.AddWholeCells(zone, new[] { CellOf(key) }, overlay.LastEdit + RegenerationDelay);
                            adopted++;
                        }
            if (adopted > 0)
                log.Info($"Heal queue: {adopted} older destruction cell(s) added.");
        }

        private static void LoadZone(uint zone, string zoneFolder)
        {
            CellPack pack = PackOf(zone);
            if (!CellPack.ExistsIn(zoneFolder) && Directory.EnumerateFiles(zoneFolder, "*.cell").Any())
                PackCellFiles(zone, zoneFolder, pack);
            var overlays = Of(zone);
            Dictionary<ulong, CellPack.Entry> entries;
            try
            {
                entries = pack.Load();
            }
            catch (Exception e)
            {
                log.Error(e, $"Could not load the cell pack of landscape {zone} ({zoneFolder}).");
                return;
            }
            foreach ((ulong id, CellPack.Entry entry) in entries)
            {
                var overlay = Overlay.FromPack(pack, entry);
                overlays[id] = overlay;
                if (BlockData.KeepVertexOnly || LayerOf(id) == DigLayer)
                    continue;
                // Vertex-only voxels: kept (clients send them with every smooth edit); stripped only when
                // KeepVertexOnlyVoxels is off. The dig layer's are the server's own (hole walls, SurfaceVertices): kept.
                int stripped = 0;
                for (int i = 0; i < BlockData.Count; i++)
                    if (overlay.Blocks.Flags[i] == 2)
                    {
                        overlay.Blocks.Flags[i] = 0;
                        stripped++;
                    }
                if (stripped > 0)
                {
                    overlay.Version++;
                    overlay.Dirty = true;
                    log.Info($"Cell {id:X16}: dropped {stripped} voxels without a material.");
                }
                else
                    overlay.Compact();
            }
        }

        /// <summary>
        /// One-time move of a landscape's cell files (&lt;key&gt;.cell: u32 version + packed cell) into its new cell pack,
        /// with each file's write time as the save time. The files are left where they are (the pack is read from now on;
        /// they can be deleted once the pack is known to be good).
        /// </summary>
        private static void PackCellFiles(uint zone, string zoneFolder, CellPack pack)
        {
            var started = DateTime.UtcNow;
            var batch = new List<(ulong Key, uint Version, byte[] Data, DateTime Saved)>();
            int packed = 0;
            foreach (string file in Directory.GetFiles(zoneFolder, "*.cell"))
            {
                try
                {
                    ulong id = Convert.ToUInt64(Path.GetFileNameWithoutExtension(file), 16);
                    byte[] bytes = File.ReadAllBytes(file);
                    batch.Add((id, BitConverter.ToUInt32(bytes, 0), bytes.AsSpan(4).ToArray(), File.GetLastWriteTimeUtc(file)));
                }
                catch (Exception e)
                {
                    log.Error(e, $"Could not read terrain overlay {file}; it stays out of the pack.");
                }
                if (batch.Count >= 2000)
                {
                    pack.Append(batch);
                    packed += batch.Count;
                    batch.Clear();
                }
            }
            if (batch.Count > 0)
                pack.Append(batch);
            packed += batch.Count;
            log.Info($"Landscape {zone}: {packed} cell file(s) packed into {CellPack.FileName} in {(DateTime.UtcNow - started).TotalSeconds:0} s " +
                $"(the .cell files are kept, no longer read).");
        }

        /// <summary>A cell idle this long is packed again; at most this many stay expanded (least recently used go first).</summary>
        private static readonly TimeSpan IdleBeforeCompact = TimeSpan.FromSeconds(30);
        private const int MaxExpanded = 64;
        private static DateTime nextCompact;

        /// <summary>
        /// A saved, compact cell untouched this long leaves memory (<see cref="Overlay.Spill"/>). Every landscape a
        /// player visits keeps its Chaos Cavern outposts as edited cells (about 45,000 cells / 470 MB compact for the
        /// home continent and one island, F2 test 2026-10-17), so without this the store grew by ~240 MB per landscape
        /// visited and never shrank. At most <see cref="MaxSpillPerPass"/> cells per pass (no file access: a spill only
        /// drops bytes; <see cref="Overlay.OnDisk"/> says the file has them).
        /// </summary>
        private static readonly TimeSpan IdleBeforeSpill = TimeSpan.FromMinutes(10);
        /// <summary>A cell saved this recently isn't spilled: its file write (after the lock in Flush) may still be going on.</summary>
        private static readonly TimeSpan SavedBeforeSpill = TimeSpan.FromMinutes(1);
        private const int MaxSpillPerPass = 4000;

        /// <summary>
        /// Packs expanded cells that are idle (and saved), and the least recently used ones beyond
        /// <see cref="MaxExpanded"/>; spills saved cells idle for <see cref="IdleBeforeSpill"/>; every few seconds from
        /// the tick.
        /// </summary>
        private static void CompactIdle()
        {
            DateTime now = DateTime.UtcNow;
            if (now < nextCompact)
                return;
            nextCompact = now.AddSeconds(5);
            CompactNow(false);
            SpillIdle(now);
        }

        /// <summary>Spills saved cells idle for <see cref="IdleBeforeSpill"/> (only with a store folder: the files hold them).</summary>
        private static void SpillIdle(DateTime now)
        {
            if (folder == null)
                return;
            long tick = Environment.TickCount64;
            int spilled = 0;
            lock (sync)
                foreach ((uint zone, var overlays) in stores)
                    foreach ((ulong id, Overlay o) in overlays)
                    {
                        if (o.Expanded || o.Dirty || o.Spilled || !o.OnDisk || o.CompactBytes == 0
                            || tick - o.LastUse < (long)IdleBeforeSpill.TotalMilliseconds || now - o.LastEdit < SavedBeforeSpill)
                            continue;
                        o.Spill();
                        if (++spilled >= MaxSpillPerPass)
                            goto done;
                    }
            done:
            if (spilled > 0)
                log.Debug($"Terrain store: {spilled} idle cell(s) spilled to their files.");
        }

        /// <summary>
        /// Packs the saved, expanded cells that have been idle (or, with <paramref name="all"/>, every saved one), and
        /// the least recently used beyond <see cref="MaxExpanded"/>.
        /// </summary>
        public static void CompactNow(bool all)
        {
            long now = Environment.TickCount64;
            lock (sync)
            {
                var expanded = stores.Values.SelectMany(s => s.Values).Where(o => o.Expanded && !o.Dirty)
                    .OrderBy(o => o.LastUse).ToList();
                int over = stores.Values.Sum(s => s.Values.Count(o => o.Expanded)) - MaxExpanded;
                foreach (Overlay o in expanded)
                    if (all || over-- > 0 || now - o.LastUse >= (long)IdleBeforeCompact.TotalMilliseconds)
                        o.Compact();
            }
        }

        /// <summary>Dev: cells held, how many expanded, and the compact bytes.</summary>
        public static string MemoryReport()
        {
            lock (sync)
            {
                var all = stores.Values.SelectMany(s => s.Values).ToList();
                int expanded = all.Count(o => o.Expanded), spilled = all.Count(o => o.Spilled);
                long compact = all.Sum(o => (long)o.CompactBytes);
                return $"{all.Count} edited cells: {expanded} expanded (~{expanded * 384 / 1024} MB), " +
                    $"{all.Count - expanded - spilled} compact ({compact / 1024} KB), {spilled} on disk only";
            }
        }

        /// <summary>
        /// Kept for the Environment Server's settings (it sets it on). Since the cell packs (<see cref="CellPack"/>) cells
        /// are appended and flushed whole, and a record cut by a crash is dropped at load, in both processes.
        /// </summary>
        public static bool AtomicWrites;

        /// <summary>After a failed save: the next attempt (the dirty cells stay dirty until then), and the next error logged.</summary>
        private static DateTime flushRetryAt, nextFlushWarning;
        private static readonly TimeSpan FlushRetry = TimeSpan.FromSeconds(5);

        /// <summary>Writes changed overlays to disk (called on the server tick, so bursts of edits coalesce).</summary>
        public static void Flush()
        {
            VoxelOwnership.Flush();
            CompactIdle();
            List<(uint Zone, ulong Id, uint Version, byte[] Data)> pending = null;
            DateTime now = DateTime.UtcNow;
            // A save failed a moment ago (disk full): wait a little before compressing and writing every dirty cell again.
            if (now < flushRetryAt && folder != null)
                return;
            lock (sync)
                foreach ((uint zone, var overlays) in stores)
                    foreach ((ulong id, Overlay overlay) in Of(zone))
                        if (overlay.Dirty)
                        {
                            overlay.Dirty = false;
                            overlay.LastEdit = now;
                            if (Array.TrueForAll(overlay.Blocks.Flags, f => f == 0))
                            {
                                // Nothing left (regenerated, healed): no file, so the next start doesn't list the cell.
                                (pending ??= new()).Add((zone, id, overlay.Version, null));
                                continue;
                            }
                            (pending ??= new()).Add((zone, id, overlay.Version, overlay.Blocks.Save()));
                        }
            if (pending == null || folder == null)
                return;
            // One append per landscape (Props of a save burst coalesce on the tick); a dropped cell gets a tombstone.
            foreach (var group in pending.GroupBy(p => p.Zone))
            {
                CellPack pack = PackOf(group.Key);
                var records = group.Select(p => (p.Id, p.Version, p.Data, now)).ToList();
                CellPack.Entry[] stored;
                try
                {
                    stored = pack.Append(records);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ObjectDisposedException)
                {
                    // The cells were marked clean above, before the write. Until 2026-10-18 a failed append (disk full: the
                    // dev PC's disk filled twice) left them clean: never written again unless edited again, and after ten
                    // idle minutes SpillIdle dropped their bytes (OnDisk still pointed at the older record), so the edits
                    // were lost from memory as well as from disk. Now they are marked dirty again and the next Flush tries
                    // again (CellPack.Append cuts a half-written append off, so the file stays readable).
                    lock (sync)
                    {
                        var overlays = Of(group.Key);
                        foreach (var record in records)
                            if (overlays.TryGetValue(record.Id, out Overlay overlay))
                                overlay.Dirty = true;
                    }
                    flushRetryAt = now + FlushRetry;
                    if (now >= nextFlushWarning)
                    {
                        nextFlushWarning = now.AddMinutes(1);
                        log.Error($"Terrain store: {records.Count} cell(s) of landscape {group.Key} could not be saved ({e.Message}); "
                            + "they stay in memory and are saved again on the next flush.");
                    }
                    continue;
                }
                lock (sync)
                {
                    var overlays = Of(group.Key);
                    for (int i = 0; i < records.Count; i++)
                        if (overlays.TryGetValue(records[i].Id, out Overlay overlay))
                        {
                            overlay.OnDisk = records[i].Data != null;
                            overlay.Pack = pack;
                            overlay.Stored = stored[i];
                        }
                }
            }
        }

        /// <summary>
        /// How long after its last change dug terrain outside build sites takes to grow back. Landmark wiki
        /// ("Gathering"): destroyed land heals by itself after about 20 minutes;
        /// gathering happens on unclaimed land, build sites keep their changes.
        /// </summary>
        public static TimeSpan RegenerationDelay = TimeSpan.FromMinutes(20);
        /// <summary>How often due heals are looked for (only the queue's head is read).</summary>
        private static readonly TimeSpan RegenerationCheck = TimeSpan.FromSeconds(5);
        /// <summary>A heal also takes the smoothed rim a dig leaves around its sphere (SmoothOffsets: within 1.5 voxels).</summary>
        private const double HealMargin = 1.5 * VoxelSize;
        private static DateTime nextRegeneration = DateTime.MinValue;

        /// <summary>Dev "regendelay": changes the delay and checks again right away.</summary>
        public static void SetRegenerationDelay(TimeSpan delay)
        {
            RegenerationDelay = delay;
            nextRegeneration = DateTime.MinValue;
        }

        /// <summary>Cells that never regenerate (the Spire's stamped base), per landscape.</summary>
        private static readonly Dictionary<uint, HashSet<ulong>> permanent = new();

        /// <summary>Marks cells as part of the world (server-built structures): regeneration leaves them alone.</summary>
        public static void MarkPermanent(IEnumerable<ulong> cells, uint zone)
        {
            lock (sync)
            {
                if (!permanent.TryGetValue(zone, out var set))
                    permanent[zone] = set = new HashSet<ulong>();
                set.UnionWith(cells);
            }
        }

        /// <summary>
        /// World tick (checked every few seconds): destroyed ground heals by the <see cref="HealQueue"/> — each destruction
        /// at its own due time, only its own volume, never inside build sites. Older layer-0 edits that aren't part of
        /// the world (stamps are marked permanent) still heal when untouched for <see cref="RegenerationDelay"/>, as
        /// before the layers. Returns the cells that grew back per landscape; the zone sends them to the players in range
        /// (ZoneTerrain.Regenerate). <paramref name="claimsIn"/> gives the build-site boxes (global dm) touching a box.
        /// </summary>
        public static IReadOnlyList<(uint Zone, List<ulong> Cells)> Regenerate(Func<uint, (double, double, double), (double, double, double), List<(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)>> claimsIn)
        {
            DateTime now = DateTime.UtcNow;
            if (now < nextRegeneration)
                return Array.Empty<(uint, List<ulong>)>();
            nextRegeneration = now + RegenerationCheck;
            var regrown = new List<(uint Zone, ulong Id)>();
            List<HealQueue.Entry> due = HealQueue.TakeDue();
            if (due.Count > 0)
                lock (sync)
                {
                    foreach (HealQueue.Entry entry in due)
                        foreach (ulong cell in entry.Cells)
                            if (HealCell(entry, cell, claimsIn))
                                regrown.Add((entry.Zone, cell));
                    foreach ((uint zone, ulong cell) in RefreshOpenHoles(due, claimsIn))
                        regrown.Add((zone, cell));
                }
            HealQueue.Done(due);
            lock (sync)
                foreach ((uint zone, var overlays) in stores)
                {
                    permanent.TryGetValue(zone, out var keep);
                    foreach ((ulong key, Overlay overlay) in overlays)
                    {
                        // Build-site building (layer 1) is permanent; digging (layer 2) heals by the queue (above); old
                        // layer-0 edits heal when left alone.
                        ulong id = CellOf(key);
                        if (LayerOf(key) != PoiLayer || overlay.Dirty || now - overlay.LastEdit < RegenerationDelay
                            || keep?.Contains(id) == true)
                            continue;
                        (_, uint cx, uint cy, uint cz) = TerrainStore.Decode(id);
                        double ox = cx * CellSize, oy = cy * CellSize, oz = cz * CellSize;
                        var sites = claimsIn(zone, (ox, oy, oz), (ox + CellSize, oy + CellSize, oz + CellSize));
                        bool wasExpanded = overlay.Expanded;
                        BlockData cell = overlay.Blocks;
                        bool any = false;
                        for (int iz = 0; iz < BlockData.Size; iz++)
                            for (int iy = 0; iy < BlockData.Size; iy++)
                                for (int ix = 0; ix < BlockData.Size; ix++)
                                {
                                    int index = VoxelIndex(ix, iy, iz);
                                    if (cell.Flags[index] == 0)
                                        continue;
                                    double x = ox + (ix + 0.5) * VoxelSize, y = oy + (iy + 0.5) * VoxelSize, z = oz + (iz + 0.5) * VoxelSize;
                                    if (sites.Exists(s => x >= s.MinX && x <= s.MaxX && y >= s.MinY && y <= s.MaxY && z >= s.MinZ && z <= s.MaxZ))
                                        continue;
                                    cell.Flags[index] = 0;
                                    cell.Materials[index] = 0;
                                    any = true;
                                }
                        if (!any)
                        {
                            // Only build-site voxels left: nothing to regrow until it changes again.
                            overlay.LastEdit = now;
                            if (!wasExpanded)
                                overlay.Compact();
                            continue;
                        }
                        overlay.Version++;
                        overlay.Dirty = true;
                        regrown.Add((zone, id));
                    }
                }
            if (regrown.Count > 0)
                log.Info($"Terrain regenerated in {regrown.Count} cell(s)" + (due.Count > 0 ? $" ({due.Count} heal(s) due)." : "."));
            return regrown.Distinct().GroupBy(r => r.Zone).Select(zone => (zone.Key, zone.Select(r => r.Id).ToList())).ToList();
        }

        /// <summary>
        /// One cell of a due heal: the destruction layer's voxels inside the entry's volume go back to what lies under them
        /// (the generated ground and, on build sites, the owner's building in layer 1), except those a still-pending
        /// destruction holds and, for old entries, those inside build sites (<see cref="HealQueue.Entry.Flags"/>). True
        /// when anything changed.
        /// </summary>
        private static bool HealCell(HealQueue.Entry entry, ulong cell, Func<uint, (double, double, double), (double, double, double), List<(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)>> claimsIn)
        {
            if (!Of(entry.Zone).TryGetValue(Key(cell, DigLayer), out Overlay overlay))
                return false;
            if (permanent.TryGetValue(entry.Zone, out var keep) && keep.Contains(cell))
                return false;
            (_, uint cx, uint cy, uint cz) = TerrainStore.Decode(cell);
            double ox = cx * CellSize, oy = cy * CellSize, oz = cz * CellSize;
            var sites = claimsIn(entry.Zone, (ox, oy, oz), (ox + CellSize, oy + CellSize, oz + CellSize));
            bool wasExpanded = overlay.Expanded;
            BlockData blocks = overlay.Blocks;
            List<HealQueue.Entry> later = HealQueue.PendingIn(entry.Zone, cell);
            bool any = false;
            for (int iz = 0; iz < BlockData.Size; iz++)
                for (int iy = 0; iy < BlockData.Size; iy++)
                    for (int ix = 0; ix < BlockData.Size; ix++)
                    {
                        int index = VoxelIndex(ix, iy, iz);
                        if (blocks.Flags[index] == 0)
                            continue;
                        double x = ox + (ix + 0.5) * VoxelSize, y = oy + (iy + 0.5) * VoxelSize, z = oz + (iz + 0.5) * VoxelSize;
                        // Site voxels: only entries from before site destruction existed leave them (HealQueue.HealsInSites);
                        // later destructions keep theirs unless this entry heals regardless (HealQueue.IgnoresLater, P6205).
                        if (!entry.Holds(x, y, z, cell, HealMargin)
                            || (!entry.Has(HealQueue.HealsInSites)
                                && sites.Exists(s => x >= s.MinX && x <= s.MaxX && y >= s.MinY && y <= s.MaxY && z >= s.MinZ && z <= s.MaxZ))
                            || (!entry.Has(HealQueue.IgnoresLater)
                                && later.Exists(e => KeepsFor(e, blocks.Flags[index], ox + ix * VoxelSize, oy + iy * VoxelSize, oz + iz * VoxelSize, cell))))
                            continue;
                        blocks.Flags[index] = 0;
                        blocks.Materials[index] = 0;
                        any = true;
                    }
            if (!any)
            {
                if (!wasExpanded)
                    overlay.Compact();
                return false;
            }
            overlay.Version++;
            overlay.Dirty = true;
            return true;
        }

        /// <summary>
        /// Does a still-pending destruction keep a voxel when an earlier one heals (global dm corner of the voxel)? A carved
        /// or filled voxel: when that destruction's sphere holds it (its corner, as the dig tests it; its centre too in the
        /// old dig modes). A vertex: when a corner of its cube lies in that sphere, so the open hole's surface is redone
        /// there (<see cref="RefreshOpenHoles"/>); a vertex of the healed hole anywhere else would bend the ground that
        /// came back. Cylinders and whole cells keep everything within the dig margin, as before.
        /// </summary>
        private static bool KeepsFor(HealQueue.Entry later, byte flags, double x, double y, double z, ulong cell)
        {
            double half = VoxelSize / 2;
            if (later.WholeCells || later.HalfHeight >= 0)
                return later.Holds(x + half, y + half, z + half, cell, HealMargin);
            if ((flags & 1) != 0)
                return later.Holds(x, y, z, cell, 0) || OffsetMode < SurfaceMode && later.Holds(x + half, y + half, z + half, cell, 0);
            for (int d = 0; d < 8; d++)
                if (later.Holds(x + (d & 1) * VoxelSize, y + (d >> 1 & 1) * VoxelSize, z + (d >> 2) * VoxelSize, cell, 0))
                    return true;
            return false;
        }

        /// <summary>
        /// Sets every voxel within <paramref name="radius"/> (world units) of a global point to air (material 0) or to
        /// <paramref name="material"/>, then gives the new surface its vertices (<see cref="SurfaceVertices"/>). Returns the
        /// cells changed and how many voxels changed.
        /// <para>
        /// Where a voxel is: the client samples its generated ground at the voxel's CORNER (cell corner + index × 0.4 m,
        /// ContinentMapHeightmapTerrain::getContourDataRegular FUN_1440873e0) and puts the cube [i, i+1]³'s surface vertex
        /// at i + offset (ContourEx FUN_142ac4590), so a voxel is inside the sphere when its corner is (<see cref="OffsetMode"/>
        /// <see cref="SurfaceMode"/>, the default). The old modes tested the voxel's centre, half a voxel off the frame the
        /// vertices are drawn in (docs/terrain.md "How the client draws a dig").
        /// </para>
        /// <para>
        /// Temporary destruction on build sites (0.695 Play Mode; TASKS 2.1 / 2.6):
        /// <paramref name="allowed"/> (global dm voxel centre → may this voxel be destroyed?) keeps the sphere out of
        /// places where destruction is not allowed — sites in Edit Mode, sites whose Play Mode Rules block it, Linking Areas
        /// set to Blocked — so a blast at a site's edge stops at the boundary. <paramref name="carveBuilds"/> also carves
        /// building voxels (layer 1) that stand above the generated ground (the open-air skip below would leave them),
        /// without touching their ownership: the destruction lies on top in layer 2 (an empty-space volume on the top
        /// layer, dev 28 Jul 2016, P6204) and the owner's voxels come back when it heals.
        /// <paramref name="healDelay"/>: the effect's own heal time (dynamite 100 s, P6037); default
        /// <see cref="RegenerationDelay"/>.
        /// </para>
        /// </summary>
        public static (List<ulong> Cells, int Voxels) Sphere(Vector3 local, double radius, ushort material, uint zone = 0,
            Dictionary<ushort, int> oreMined = null, HashSet<ulong> oreCells = null, uint layer = DigLayer,
            Func<double, double, double, bool> allowed = null, bool carveBuilds = false, TimeSpan? healDelay = null)
        {
            (double gx, double gy, double gz) = ToGlobal(local);
            var cells = new HashSet<ulong>();
            var touched = new Dictionary<ulong, List<int>>();
            int changed = 0;
            double r2 = radius * radius;
            bool corners = OffsetMode >= SurfaceMode;
            // The point of a voxel the sphere tests: its corner (the client's sample), or its centre in the old modes.
            double at = corners ? 0 : 0.5;
            long x0 = (long)Math.Floor((gx - radius) / VoxelSize) - 1, x1 = (long)Math.Floor((gx + radius) / VoxelSize) + 1;
            long y0 = (long)Math.Floor((gy - radius) / VoxelSize) - 1, y1 = (long)Math.Floor((gy + radius) / VoxelSize) + 1;
            long z0 = (long)Math.Floor((gz - radius) / VoxelSize) - 1, z1 = (long)Math.Floor((gz + radius) / VoxelSize) + 1;
            lock (sync)
            {
                for (long vx = x0; vx <= x1; vx++)
                    for (long vy = y0; vy <= y1; vy++)
                        for (long vz = z0; vz <= z1; vz++)
                        {
                            double cx = (vx + at) * VoxelSize - gx, cy = (vy + at) * VoxelSize - gy,
                                cz = (vz + at) * VoxelSize - gz;
                            if (cx * cx + cy * cy + cz * cz > r2)
                                continue;
                            // Site rules (and every other permission test) look at the voxel's centre, as for building.
                            if (allowed != null && !allowed((vx + 0.5) * VoxelSize, (vy + 0.5) * VoxelSize, (vz + 0.5) * VoxelSize))
                                continue;
                            long cellX = Math.DivRem(vx, BlockData.Size, out long ix),
                                cellY = Math.DivRem(vy, BlockData.Size, out long iy),
                                cellZ = Math.DivRem(vz, BlockData.Size, out long iz);
                            if (ix < 0) { ix += BlockData.Size; cellX--; }
                            if (iy < 0) { iy += BlockData.Size; cellY--; }
                            if (iz < 0) { iz += BlockData.Size; cellZ--; }
                            ulong id = TerrainStore.CellId(Level, (uint)cellX, (uint)cellY, (uint)cellZ);
                            int index = VoxelIndex((int)ix, (int)iy, (int)iz);
                            // Building under the destruction (only on build sites: layer 1 holds nothing elsewhere).
                            bool building = carveBuilds && layer == DigLayer && BuildSolid(zone, id, index);
                            if (!Of(zone).TryGetValue(Key(id, layer), out Overlay overlay))
                                Of(zone)[Key(id, layer)] = overlay = new Overlay();
                            // Already this material (a vertex-only voxel, flag 2, is still the generated ground).
                            if ((overlay.Blocks.Flags[index] & 1) != 0 && overlay.Blocks.Materials[index] == material)
                                continue;
                            // Carving only touches ground: voxels clearly in open air (well above the generated surface)
                            // stay unset, so less is sent. Near the surface everything is carved explicitly: the baked
                            // height grid is coarse, and on slopes and mountains a skipped voxel the client still has as
                            // solid left fragments of the old surface floating over the hole (and hid the mining ring).
                            // A building voxel standing in the open air is carved all the same (site destruction).
                            if (material == 0 && !building && (overlay.Blocks.Flags[index] & 1) == 0
                                && AboveGround(vx, vy, vz, zone, OpenAirMargin, at))
                                continue;
                            // Carving an untouched voxel of an ore vein: that ore is what the swing digs out.
                            if (material == 0 && oreMined != null && (overlay.Blocks.Flags[index] & 1) == 0
                                && OreVeins.OreAt(id, index, zone) is ushort ore and not 0)
                            {
                                oreMined[ore] = oreMined.GetValueOrDefault(ore) + 1;
                                oreCells?.Add(id);
                            }
                            if (corners && material == 0)
                            {
                                // Air without a vertex of its own: SurfaceVertices gives one to every voxel whose cube
                                // the new surface runs through (a centre vertex here would be a 0.4 m step).
                                overlay.Blocks.Flags[index] = 1;
                                overlay.Blocks.Materials[index] = 0;
                            }
                            else
                                overlay.Blocks.SetSolid(index, material);
                            cells.Add(id);
                            changed++;
                            // The owner keeps their building voxels (Voxel Vision): only the ground is nobody's.
                            if (building)
                                continue;
                            if (!touched.TryGetValue(id, out var list))
                                touched[id] = list = new List<int>();
                            list.Add(index);
                        }
                if (corners)
                {
                    // The other holes still open around this one (pending heals) shape the surface too.
                    var holes = layer == DigLayer ? HealQueue.SpheresNear(zone, (gx, gy, gz), radius, 2 * VoxelSize)
                        : new List<(double X, double Y, double Z, double Radius)>();
                    holes.Add((gx, gy, gz, radius));
                    SurfaceVertices((gx, gy, gz), radius, holes, cells, zone, layer, allowed);
                }
                else
                    SmoothOffsets(gx, gy, gz, radius, cells, zone, layer, allowed);
                foreach (ulong id in cells)
                {
                    Of(zone)[Key(id, layer)].Version++;
                    Of(zone)[Key(id, layer)].Dirty = true;
                }
            }
            // Destruction heals at its own time, never postponed by later digs (HealQueue).
            if (layer == DigLayer && cells.Count > 0)
                HealQueue.Add(zone, (gx, gy, gz), radius, cells, healDelay ?? RegenerationDelay);
            // Dug ground belongs to nobody (Voxel Vision).
            foreach ((ulong id, List<int> list) in touched)
                VoxelOwnership.Set(zone, id, list, _ => false, 0);
            return (cells.ToList(), changed);
        }

        /// <summary>How far above the baked ground height a voxel must be to count as certainly air, metres.</summary>
        private const float OpenAirMargin = 2f;

        /// <summary>
        /// Is a voxel's point (<paramref name="at"/> 0.5 = its centre, 0 = its corner, the client's sample) above the
        /// generated ground (baked height grid) by more than <paramref name="margin"/> metres (default: one voxel), i.e.
        /// already air?
        /// </summary>
        private static bool AboveGround(long vx, long vy, long vz, uint zone, float margin = -1f, double at = 0.5)
        {
            float lx = (float)(((vx + at) * VoxelSize - TerrainStore.ZoneOrigin.X) / 10),
                ly = (float)(((vy + at) * VoxelSize - TerrainStore.ZoneOrigin.Y) / 10),
                lz = (float)(((vz + at) * VoxelSize - TerrainStore.ZoneOrigin.Z) / 10);
            if (margin < 0)
                margin = (float)(VoxelSize / 10);
            return TerrainHeights.HeightAt(lx, lz, zone) is float ground && ly > ground + margin;
        }

        /// <summary>
        /// Test (dev "ball"): a solid ball of <paramref name="material"/> fully described by the overlay: solid voxels
        /// inside, explicit air outside, and every voxel within 1.2 voxels of the surface given a vertex on it.
        /// </summary>
        public static List<ulong> Ball(Vector3 local, double radius, ushort material, uint zone = 0, uint layer = PoiLayer)
        {
            (double gx, double gy, double gz) = ToGlobal(local);
            var cells = new HashSet<ulong>();
            double reach = radius + VoxelSize * 1.5;
            lock (sync)
            {
                for (long vx = (long)Math.Floor((gx - reach) / VoxelSize); vx <= (long)Math.Floor((gx + reach) / VoxelSize); vx++)
                    for (long vy = (long)Math.Floor((gy - reach) / VoxelSize); vy <= (long)Math.Floor((gy + reach) / VoxelSize); vy++)
                        for (long vz = (long)Math.Floor((gz - reach) / VoxelSize); vz <= (long)Math.Floor((gz + reach) / VoxelSize); vz++)
                        {
                            double cx = (vx + 0.5) * VoxelSize - gx, cy = (vy + 0.5) * VoxelSize - gy, cz = (vz + 0.5) * VoxelSize - gz;
                            double d = Math.Sqrt(cx * cx + cy * cy + cz * cz);
                            bool inside = d <= radius, near = Math.Abs(d - radius) <= VoxelSize * 1.2;
                            if (!inside && !near)
                                continue;
                            long cellX = Math.DivRem(vx, BlockData.Size, out long ix), cellY = Math.DivRem(vy, BlockData.Size, out long iy),
                                cellZ = Math.DivRem(vz, BlockData.Size, out long iz);
                            if (ix < 0) { ix += BlockData.Size; cellX--; }
                            if (iy < 0) { iy += BlockData.Size; cellY--; }
                            if (iz < 0) { iz += BlockData.Size; cellZ--; }
                            ulong id = TerrainStore.CellId(Level, (uint)cellX, (uint)cellY, (uint)cellZ);
                            if (!Of(zone).TryGetValue(Key(id, layer), out Overlay overlay))
                                Of(zone)[Key(id, layer)] = overlay = new Overlay();
                            int index = VoxelIndex((int)ix, (int)iy, (int)iz);
                            overlay.Blocks.SetSolid(index, inside ? material : (ushort)0);
                            if (near && d > 1e-6)
                            {
                                double k = radius / d;
                                overlay.Blocks.Offsets[index * 3] = OffsetByte((gx + cx * k) / VoxelSize - vx);
                                overlay.Blocks.Offsets[index * 3 + 1] = OffsetByte((gy + cy * k) / VoxelSize - vy);
                                overlay.Blocks.Offsets[index * 3 + 2] = OffsetByte((gz + cz * k) / VoxelSize - vz);
                            }
                            cells.Add(id);
                        }
                foreach (ulong id in cells)
                {
                    Of(zone)[Key(id, layer)].Version++;
                    Of(zone)[Key(id, layer)].Dirty = true;
                }
            }
            return cells.ToList();
        }

        /// <summary>
        /// How deep below the generated surface the smoothing shell may add solid voxels (metres) on a landscape
        /// without cave data; with it the shell goes anywhere the caves leave rock.
        /// </summary>
        private const float ShellDepth = 4f;

        private static bool CavesKnown(uint zone) => CavePieces.HasData(zone);

        /// <summary>
        /// Hole rims (dev "rimmode"): 1 = rim voxels whose sphere point lies above the ground get their vertex on the
        /// ground surface (default); 0 = they are left alone (centre vertices: notched, blocky rims). Compared side by
        /// side on a flat beach (2026-10-11, the holes told apart by a marker hole): 1 gave the cleaner rim.
        /// </summary>
        public static int RimMode = 1;

        /// <summary>
        /// How a dig's surface is written (dev "digmode"): <see cref="SurfaceMode"/> (8, the default) = the client's own
        /// surface (<see cref="SurfaceVertices"/>); 5 = the old solid shell of guessed material (<see cref="SmoothOffsets"/>,
        /// 6 / 7 its variants, 1-4 its offset byte orders); 0 = no vertices (plain voxel steps). Kept for side-by-side checks.
        /// </summary>
        public static int OffsetMode = SurfaceMode;

        /// <summary><see cref="OffsetMode"/> of the client-surface dig (corner samples, vertex-only walls).</summary>
        public const int SurfaceMode = 8;

        /// <summary>
        /// A voxel's surface-vertex coordinate (voxel units from its corner, -1..2) as stored: round((u + 1) × 85)
        /// (client FUN_142a0d650 constants: clamp -1..2, × 85, + 0.5). 0.5 (centre) ≈ 128.
        /// </summary>
        public static byte OffsetByte(double u) => (byte)Math.Floor((Math.Clamp(u, -1, 2) + 1) * 85 + 0.5);
        /// <summary>
        /// Material of the solid voxels that line a dug hole (so its walls are round, not the generated cubes): set by
        /// the digger from what the dig yields (VoxelMaterials ARRAY_INDEX; default Ancient Forest Dirt Lowland).
        /// </summary>
        public static ushort ShellMaterial = 297;

        /// <summary>
        /// Gives the air voxels on a carved sphere's boundary (inside, with a 6-neighbour outside) a surface offset at
        /// the nearest point of the sphere, so the client's mesher draws a round hole instead of 0.4 m steps.
        /// </summary>
        /// <remarks>
        /// Voxels <paramref name="allowed"/> refuses (global dm centre) are left alone, as in <see cref="Sphere"/>. No solid
        /// shell voxel is put where the build layer (1) has anything: on a build site the shell would otherwise cover the
        /// owner's own material or fill the owner's own dug space with ground until the heal (layer 2 lies on top).
        /// </remarks>
        private static void SmoothOffsets(double gx, double gy, double gz, double radius, HashSet<ulong> cells, uint zone = 0,
            uint layer = DigLayer, Func<double, double, double, bool> allowed = null)
        {
            if (OffsetMode == 0)
                return;
            double r2 = radius * radius;
            bool Inside(long vx, long vy, long vz)
            {
                double cx = (vx + 0.5) * VoxelSize - gx, cy = (vy + 0.5) * VoxelSize - gy, cz = (vz + 0.5) * VoxelSize - gz;
                return cx * cx + cy * cy + cz * cz <= r2;
            }
            double reach = radius + VoxelSize * 1.5;
            long x0 = (long)Math.Floor((gx - reach) / VoxelSize), x1 = (long)Math.Floor((gx + reach) / VoxelSize);
            long y0 = (long)Math.Floor((gy - reach) / VoxelSize), y1 = (long)Math.Floor((gy + reach) / VoxelSize);
            long z0 = (long)Math.Floor((gz - reach) / VoxelSize), z1 = (long)Math.Floor((gz + reach) / VoxelSize);
            bool shell = OffsetMode >= 5;
            bool NearSurface(long vx, long vy, long vz)
            {
                double cx = (vx + 0.5) * VoxelSize - gx, cy = (vy + 0.5) * VoxelSize - gy, cz = (vz + 0.5) * VoxelSize - gz;
                return Math.Abs(Math.Sqrt(cx * cx + cy * cy + cz * cz) - radius) <= VoxelSize * 1.2;
            }
            for (long vx = x0; vx <= x1; vx++)
                for (long vy = y0; vy <= y1; vy++)
                    for (long vz = z0; vz <= z1; vz++)
                    {
                        bool inside = Inside(vx, vy, vz);
                        bool boundary = NearSurface(vx, vy, vz);
                        if (!boundary || (!inside && !shell) || (inside && OffsetMode == 6))
                            continue;
                        if (allowed != null && !allowed((vx + 0.5) * VoxelSize, (vy + 0.5) * VoxelSize, (vz + 0.5) * VoxelSize))
                            continue;
                        double cx = (vx + 0.5) * VoxelSize - gx, cy = (vy + 0.5) * VoxelSize - gy, cz = (vz + 0.5) * VoxelSize - gz;
                        double length = Math.Sqrt(cx * cx + cy * cy + cz * cz);
                        if (length < 1e-6)
                            continue;
                        double k = radius / length;
                        // Surface point relative to the voxel's corner, in voxel units (0..1).
                        double ux = (gx + cx * k) / VoxelSize - vx, uy = (gy + cy * k) / VoxelSize - vy, uz = (gz + cz * k) / VoxelSize - vz;
                        // Rim: a sphere point at or above the generated ground would stick up out of the grass. Skipping such
                        // voxels (RimMode 0) left them with centre vertices: a notched rim. RimMode 1 (default) puts their
                        // vertex on the ground surface (same x/z, the ground's height) so the wall meets the terrain cleanly.
                        {
                            float sx = (float)((gx + cx * k - TerrainStore.ZoneOrigin.X) / 10), sy = (float)((gy + cy * k - TerrainStore.ZoneOrigin.Y) / 10),
                                sz = (float)((gz + cz * k - TerrainStore.ZoneOrigin.Z) / 10);
                            if (TerrainHeights.HeightAt(sx, sz, zone) is float surfaceY && sy > surfaceY - 0.25f)
                            {
                                if (RimMode == 0)
                                    continue;
                                double groundGlobalY = (surfaceY - 0.25f) * 10 + TerrainStore.ZoneOrigin.Y;
                                uy = groundGlobalY / VoxelSize - vy;
                                // A vertex more than a voxel outside its own voxel can't be drawn; such voxels are left alone.
                                if (uy < -1 || uy > 2)
                                    continue;
                            }
                        }
                        byte B(double u) => OffsetByte(u);
                        int order = OffsetMode >= 5 ? 1 : OffsetMode;
                        (byte a, byte b, byte c) = order switch
                        {
                            2 => (B(uy), B(ux), B(uz)),
                            3 => (B(uz), B(uy), B(ux)),
                            4 => (B(ux), B(uz), B(uy)),
                            _ => (B(ux), B(uy), B(uz)),
                        };
                        long cellX = Math.DivRem(vx, BlockData.Size, out long ix), cellY = Math.DivRem(vy, BlockData.Size, out long iy),
                            cellZ = Math.DivRem(vz, BlockData.Size, out long iz);
                        if (ix < 0) { ix += BlockData.Size; cellX--; }
                        if (iy < 0) { iy += BlockData.Size; cellY--; }
                        if (iz < 0) { iz += BlockData.Size; cellZ--; }
                        ulong id = TerrainStore.CellId(Level, (uint)cellX, (uint)cellY, (uint)cellZ);
                        if (!Of(zone).TryGetValue(Key(id, layer), out Overlay overlay))
                        {
                            if (inside)
                                continue;
                            Of(zone)[Key(id, layer)] = overlay = new Overlay();
                        }
                        int index = VoxelIndex((int)ix, (int)iy, (int)iz);
                        if (!inside)
                        {
                            float lx = (float)(((vx + 0.5) * VoxelSize - TerrainStore.ZoneOrigin.X) / 10),
                                ly = (float)(((vy + 0.5) * VoxelSize - TerrainStore.ZoneOrigin.Y) / 10),
                                lz = (float)(((vz + 0.5) * VoxelSize - TerrainStore.ZoneOrigin.Z) / 10);
                            // Only where the generated ground is solid: below the surface and not in the client's
                            // caves (CavePieces). Without the cave check a shell filled cave air with new voxels
                            // around every swing underground (2026-10-11).
                            if (TerrainHeights.HeightAt(lx, lz, zone) is not float ground || ly > ground - 0.6f
                                || (ly < ground - ShellDepth && !CavesKnown(zone))
                                || CavePieces.IsAir((vx + 0.5) * VoxelSize, (vy + 0.5) * VoxelSize, (vz + 0.5) * VoxelSize, zone))
                                continue;
                            // Solid shell: keep an edited voxel's material; generated ground gets either a guessed
                            // material (mode 5/6) or only a vertex (mode 7: flag 2, the client keeps its material).
                            if ((overlay.Blocks.Flags[index] & 1) != 0 && overlay.Blocks.Materials[index] == 0)
                                continue;
                            // The owner's building or digging (layer 1) under it: no ground shell over it.
                            if (layer == DigLayer && BuildHas(zone, id, index))
                                continue;
                            if ((overlay.Blocks.Flags[index] & 1) == 0)
                            {
                                {
                                    // Ground the hole's surface runs through: the biome's cave floor material where
                                    // the surface faces up (voxel below the centre), its wall material elsewhere.
                                    overlay.Blocks.SetSolid(index, ShellMaterial);
                                }
                            }
                        }
                        else if ((overlay.Blocks.Flags[index] & 1) == 0)
                            continue;
                        if ((overlay.Blocks.Flags[index] & 2) != 0 && !(overlay.Blocks.Offsets[index * 3] == BlockData.CentreOffset
                            && overlay.Blocks.Offsets[index * 3 + 1] == BlockData.CentreOffset && overlay.Blocks.Offsets[index * 3 + 2] == BlockData.CentreOffset))
                        {
                            // Overlapping holes: keep the vertex nearer the voxel centre (the union's nearer surface).
                            double Old(int k) => overlay.Blocks.Offsets[index * 3 + k] / 85.0 - 1 - 0.5;
                            double oldDistance = Old(0) * Old(0) + Old(1) * Old(1) + Old(2) * Old(2);
                            double newDistance = (ux - 0.5) * (ux - 0.5) + (uy - 0.5) * (uy - 0.5) + (uz - 0.5) * (uz - 0.5);
                            if (oldDistance < newDistance)
                                continue;
                        }
                        overlay.Blocks.Flags[index] |= 2;
                        overlay.Blocks.Offsets[index * 3] = a;
                        overlay.Blocks.Offsets[index * 3 + 1] = b;
                        overlay.Blocks.Offsets[index * 3 + 2] = c;
                        cells.Add(id);
                    }
        }

        /// <summary>
        /// The surface of a dig the way the client draws the unmodified world (docs/terrain.md "How the client draws a
        /// dig"): every cube [i, i+1]³ that has a corner inside the dug sphere and solid / air corners both gets, at its
        /// corner voxel i, the mass point of the crossings on its 12 edges as its vertex — what the client's own generator
        /// writes for its ground (ContinentMapHeightmapTerrain::SmoothContourData FUN_144086430: linear crossing of the
        /// density on each edge whose ends differ, averaged, flag 2 + offset byte). The mesher (ContourEx FUN_142ac4590)
        /// joins the vertices of the four cubes around every edge whose two voxels differ, so a cube left without one
        /// is drawn at its centre: the 0.4 m stair steps players saw in pick holes (2026-10-18).
        /// <para>
        /// The density along an edge is the ground minus every open hole: min(ground height − y, distance to each sphere
        /// centre − its radius) (dm; the baked height grid for the ground, cave air as air), so overlapping swings make
        /// one smooth shaft and the rim meets the ground. Corners whose state comes from the block layers (carved air,
        /// building, stamps) take the holes' distance only; where the estimate disagrees with the corner's state the
        /// crossing falls in the middle of the edge.
        /// </para>
        /// <para>
        /// Voxels on the solid side get a <b>vertex-only</b> voxel (flag 2, no material): the client keeps its own
        /// generated material there and only moves the vertex (getBlockContourData FUN_142aa8830; layers merge per bit,
        /// FUN_1440498d0), as its own smooth brushes (spheres, lines, fills) send them (docs/terrain.md "Vertex-only voxels").
        /// So no rock is ever added and the hole's walls keep the ground's own materials. Cubes the sphere cleared
        /// completely lose a vertex an earlier swing gave them (less to send). Voxels <paramref name="allowed"/> refuses
        /// and voxels where the build layer has anything are left alone.
        /// </para>
        /// Callers hold <see cref="sync"/>. Returns the vertices written.
        /// </summary>
        private static int SurfaceVertices((double X, double Y, double Z) centre, double radius,
            List<(double X, double Y, double Z, double Radius)> holes, HashSet<ulong> cells, uint zone, uint layer,
            Func<double, double, double, bool> allowed)
        {
            // Cubes with a corner that can be inside the sphere, and their corner samples (one more along each axis).
            long x0 = (long)Math.Floor((centre.X - radius) / VoxelSize) - 1, x1 = (long)Math.Floor((centre.X + radius) / VoxelSize);
            long y0 = (long)Math.Floor((centre.Y - radius) / VoxelSize) - 1, y1 = (long)Math.Floor((centre.Y + radius) / VoxelSize);
            long z0 = (long)Math.Floor((centre.Z - radius) / VoxelSize) - 1, z1 = (long)Math.Floor((centre.Z + radius) / VoxelSize);
            int nx = (int)(x1 - x0 + 2), ny = (int)(y1 - y0 + 2), nz = (int)(z1 - z0 + 2);
            var solid = new bool[nx * ny * nz];
            var density = new double[nx * ny * nz];
            var inside = new bool[nx * ny * nz];
            // A corner whose state the density estimate did not explain (its crossing goes mid-edge, with no plane).
            var guessed = new bool[nx * ny * nz];
            int At(int i, int j, int k) => i + nx * (j + ny * k);

            var overlays = new Dictionary<ulong, Overlay>();
            Overlay Get(ulong id, uint l)
            {
                ulong key = Key(id, l);
                if (!overlays.TryGetValue(key, out Overlay o))
                {
                    Of(zone).TryGetValue(key, out o);
                    overlays[key] = o;
                }
                return o;
            }
            var ground = new Dictionary<(long, long), double>();
            double GroundY(long sx, long sz)
            {
                if (!ground.TryGetValue((sx, sz), out double y))
                {
                    float lx = (float)((sx * VoxelSize - TerrainStore.ZoneOrigin.X) / 10), lz = (float)((sz * VoxelSize - TerrainStore.ZoneOrigin.Z) / 10);
                    ground[(sx, sz)] = y = TerrainHeights.HeightAt(lx, lz, zone) is float h ? h * 10.0 + TerrainStore.ZoneOrigin.Y : double.NaN;
                }
                return y;
            }

            for (int k = 0; k < nz; k++)
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        long sx = x0 + i, sy = y0 + j, sz = z0 + k;
                        double px = sx * VoxelSize, py = sy * VoxelSize, pz = sz * VoxelSize;
                        (ulong id, int index) = Locate(sx, sy, sz);
                        // The block layers as the client merges them: the highest layer with a material decides.
                        bool? set = null;
                        for (uint l = LayerCount; l-- > 0;)
                            if (Get(id, l) is Overlay o && (o.Blocks.Flags[index] & 1) != 0)
                            {
                                ushort m = o.Blocks.Materials[index];
                                set = m != 0 && m != VoxelTemplate.InteriorAir;
                                break;
                            }
                        double hole = double.PositiveInfinity;
                        foreach (var h in holes)
                            hole = Math.Min(hole, Math.Sqrt((px - h.X) * (px - h.X) + (py - h.Y) * (py - h.Y) + (pz - h.Z) * (pz - h.Z)) - h.Radius);
                        double own = Math.Sqrt((px - centre.X) * (px - centre.X) + (py - centre.Y) * (py - centre.Y) + (pz - centre.Z) * (pz - centre.Z));
                        int a = At(i, j, k);
                        inside[a] = own <= radius;
                        bool isSolid;
                        double value;
                        if (set is bool blockSolid)
                        {
                            isSolid = blockSolid;
                            value = hole;
                        }
                        else
                        {
                            // The generated world: solid up to the ground (the client's sample rule), except cave air.
                            double groundY = GroundY(sx, sz);
                            double g = double.IsNaN(groundY) ? double.PositiveInfinity : groundY - py;
                            bool cave = g >= 0 && CavePieces.IsAir(px, py, pz, zone);
                            isSolid = g >= 0 && !cave;
                            value = Math.Min(hole, cave ? -VoxelSize / 2 : g);
                        }
                        if (isSolid && value <= 0 || !isSolid && value > 0)
                        {
                            value = isSolid ? VoxelSize / 2 : -VoxelSize / 2;
                            guessed[a] = true;
                        }
                        solid[a] = isSolid;
                        density[a] = value;
                    }

            var crossings = new Vector3[12];
            var planePoints = new Vector3[12];
            var planeNormals = new Vector3[12];
            int written = 0;
            for (int k = 0; k < nz - 1; k++)
                for (int j = 0; j < ny - 1; j++)
                    for (int i = 0; i < nx - 1; i++)
                    {
                        bool touches = false;
                        int solids = 0;
                        for (int d = 0; d < 8; d++)
                        {
                            int c = At(i + (d & 1), j + (d >> 1 & 1), k + (d >> 2));
                            touches |= inside[c];
                            if (solid[c])
                                solids++;
                        }
                        if (!touches)
                            continue;
                        long sx = x0 + i, sy = y0 + j, sz = z0 + k;
                        if (allowed != null && !allowed((sx + 0.5) * VoxelSize, (sy + 0.5) * VoxelSize, (sz + 0.5) * VoxelSize))
                            continue;
                        (ulong id, int index) = Locate(sx, sy, sz);
                        // The owner's building or digging (layer 1) at this voxel keeps its own vertex.
                        if (layer == DigLayer && BuildHas(zone, id, index))
                            continue;
                        Of(zone).TryGetValue(Key(id, layer), out Overlay overlay);
                        if (solids == 0 || solids == 8)
                        {
                            // No surface in this cube any more: a vertex an earlier swing left here is not needed.
                            if (overlay != null && (overlay.Blocks.Flags[index] & 2) != 0)
                            {
                                overlay.Blocks.Flags[index] &= 1;
                                cells.Add(id);
                            }
                            continue;
                        }
                        // The crossings: linear in the density on each edge whose corners differ (the generator's rule).
                        int n = 0, planes = 0;
                        bool trusted = true;
                        for (int d = 0; d < 8; d++)
                            for (int bit = 1; bit <= 4; bit <<= 1)
                            {
                                if ((d & bit) != 0)
                                    continue;
                                int e = d | bit;
                                int ca = At(i + (d & 1), j + (d >> 1 & 1), k + (d >> 2)), cb = At(i + (e & 1), j + (e >> 1 & 1), k + (e >> 2));
                                if (solid[ca] == solid[cb])
                                    continue;
                                double t = Math.Clamp(density[ca] / (density[ca] - density[cb]), 0, 1);
                                var q = new Vector3((float)((d & 1) + ((e & 1) - (d & 1)) * t), (float)((d >> 1 & 1) + ((e >> 1 & 1) - (d >> 1 & 1)) * t),
                                    (float)((d >> 2) + ((e >> 2) - (d >> 2)) * t));
                                crossings[n++] = q;
                                trusted &= !guessed[ca] && !guessed[cb];
                                if (Field((sx + (double)q.X) * VoxelSize, (sy + (double)q.Y) * VoxelSize, (sz + (double)q.Z) * VoxelSize, holes, zone) is { } at
                                    && at.Gradient.LengthSquared() > 1e-12)
                                {
                                    planePoints[planes] = q;
                                    planeNormals[planes++] = Vector3.Normalize(at.Gradient);
                                }
                            }
                        long ox = sx, oy = sy, oz = sz;
                        Vector3 u = PlaceVertex(crossings, n, planePoints, planeNormals, trusted ? planes : 0,
                            c => Field((ox + (double)c.X) * VoxelSize, (oy + (double)c.Y) * VoxelSize, (oz + (double)c.Z) * VoxelSize, holes, zone));
                        byte bx = OffsetByte(u.X), by = OffsetByte(u.Y), bz = OffsetByte(u.Z);
                        if (overlay == null)
                            Of(zone)[Key(id, layer)] = overlay = new Overlay();
                        BlockData blocks = overlay.Blocks;
                        if ((blocks.Flags[index] & 2) != 0 && blocks.Offsets[index * 3] == bx && blocks.Offsets[index * 3 + 1] == by
                            && blocks.Offsets[index * 3 + 2] == bz)
                            continue;
                        blocks.Flags[index] |= 2;
                        blocks.Offsets[index * 3] = bx;
                        blocks.Offsets[index * 3 + 1] = by;
                        blocks.Offsets[index * 3 + 2] = bz;
                        cells.Add(id);
                        written++;
                    }
            return written;
        }

        /// <summary>
        /// A cube's vertex (cube-local voxel units) from its edge crossings. The mass point of the crossings is what the
        /// client's generator uses (SmoothContourData); on the small spheres of a pick (2.5 voxels) it sits a third of a
        /// voxel inside the curve. So, where the density is known (<paramref name="planes"/> &gt; 0), the vertex is moved
        /// onto the surface: first onto the tangent planes at the crossings (their least-squares point, reached from the
        /// mass point by averaged projections, as VoxelFarm's own QEF places vertices where block data meets the generated
        /// ground: getBlockContourData FUN_142aa8830 → FUN_142aa2900 … FUN_142aa5700), which gives a rim its edge where a
        /// wall meets the ground; if that point is not on the surface (thin tips, near-parallel planes), the mass point
        /// projected onto the nearest surface along the density's gradient; the mass point itself if neither is nearer.
        /// Kept within half a voxel of the cube.
        /// </summary>
        private static Vector3 PlaceVertex(Vector3[] crossings, int n, Vector3[] points, Vector3[] normals, int planes,
            Func<Vector3, (double Value, Vector3 Gradient)?> field)
        {
            var low = new Vector3(-0.5f);
            var high = new Vector3(1.5f);
            Vector3 mass = Vector3.Zero;
            for (int c = 0; c < n; c++)
                mass += crossings[c];
            mass /= n;
            if (planes == 0)
                return mass;
            // Distance to the surface (voxels) by the density and its gradient.
            double Off(Vector3 v) => field(v) is { } f && f.Gradient.Length() > 1e-6
                ? Math.Abs(f.Value) / VoxelSize / f.Gradient.Length() : double.PositiveInfinity;

            Vector3 planar = mass;
            for (int iteration = 0; iteration < 32; iteration++)
            {
                Vector3 step = Vector3.Zero;
                for (int p = 0; p < planes; p++)
                    step += normals[p] * Vector3.Dot(normals[p], planar - points[p]);
                planar -= step / planes;
            }
            planar = Vector3.Clamp(planar, low, high);
            if (Off(planar) <= 0.05)
                return planar;

            Vector3 projected = mass;
            for (int iteration = 0; iteration < 4; iteration++)
            {
                if (field(projected) is not { } f || f.Gradient.LengthSquared() < 1e-9)
                    break;
                projected -= f.Gradient * (float)(f.Value / VoxelSize / f.Gradient.LengthSquared());
            }
            projected = Vector3.Clamp(projected, low, high);
            Vector3 best = mass;
            double bestOff = Off(mass);
            foreach (Vector3 candidate in new[] { planar, projected })
                if (Off(candidate) < bestOff)
                {
                    best = candidate;
                    bestOff = Off(candidate);
                }
            return best;
        }

        /// <summary>
        /// The ground-minus-holes density at a global point (dm, positive in the ground) and the gradient of the term that
        /// makes it there: a hole's sphere (distance to its centre − its radius; radial, unit length) or the generated
        /// ground (height − y; (∂h/∂x, −1, ∂h/∂z) from the baked heights). Null when nothing is known there.
        /// </summary>
        private static (double Value, Vector3 Gradient)? Field(double x, double y, double z, List<(double X, double Y, double Z, double Radius)> holes, uint zone)
        {
            double best = double.PositiveInfinity;
            Vector3 gradient = Vector3.Zero;
            foreach (var h in holes)
            {
                double dx = x - h.X, dy = y - h.Y, dz = z - h.Z, length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (length - h.Radius < best && length > 1e-6)
                {
                    best = length - h.Radius;
                    gradient = new Vector3((float)(dx / length), (float)(dy / length), (float)(dz / length));
                }
            }
            float lx = (float)((x - TerrainStore.ZoneOrigin.X) / 10), lz = (float)((z - TerrainStore.ZoneOrigin.Z) / 10);
            if (TerrainHeights.HeightAt(lx, lz, zone) is float h0 && h0 * 10.0 + TerrainStore.ZoneOrigin.Y - y < best)
            {
                const float d = 0.2f;
                if (TerrainHeights.HeightAt(lx + d, lz, zone) is float hx1 && TerrainHeights.HeightAt(lx - d, lz, zone) is float hx0
                    && TerrainHeights.HeightAt(lx, lz + d, zone) is float hz1 && TerrainHeights.HeightAt(lx, lz - d, zone) is float hz0)
                {
                    best = h0 * 10.0 + TerrainStore.ZoneOrigin.Y - y;
                    gradient = new Vector3((hx1 - hx0) / (2 * d), -1f, (hz1 - hz0) / (2 * d));
                }
            }
            return double.IsPositiveInfinity(best) ? null : (best, gradient);
        }

        /// <summary>A global voxel's level-2 cell and its index there.</summary>
        private static (ulong Cell, int Index) Locate(long vx, long vy, long vz)
        {
            long cellX = Math.DivRem(vx, BlockData.Size, out long ix), cellY = Math.DivRem(vy, BlockData.Size, out long iy),
                cellZ = Math.DivRem(vz, BlockData.Size, out long iz);
            if (ix < 0) { ix += BlockData.Size; cellX--; }
            if (iy < 0) { iy += BlockData.Size; cellY--; }
            if (iz < 0) { iz += BlockData.Size; cellZ--; }
            return (TerrainStore.CellId(Level, (uint)cellX, (uint)cellY, (uint)cellZ), VoxelIndex((int)ix, (int)iy, (int)iz));
        }

        /// <summary>
        /// After heals: the holes still open next to a healed one get their surface again (<see cref="SurfaceVertices"/>
        /// against the holes that are left), so where the ground came back their walls close cleanly instead of keeping
        /// vertices that leaned on the healed hole. Outside build sites only. Callers hold <see cref="sync"/>. Returns the
        /// cells changed.
        /// </summary>
        private static List<(uint Zone, ulong Cell)> RefreshOpenHoles(List<HealQueue.Entry> healed,
            Func<uint, (double, double, double), (double, double, double), List<(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)>> claimsIn)
        {
            var changed = new List<(uint, ulong)>();
            if (OffsetMode < SurfaceMode)
                return changed;
            var done = new HashSet<(uint, double, double, double, double)>();
            foreach (HealQueue.Entry entry in healed)
            {
                if (entry.WholeCells || entry.HalfHeight >= 0)
                    continue;
                foreach (var open in HealQueue.SpheresNear(entry.Zone, (entry.X, entry.Y, entry.Z), entry.Radius, 2 * VoxelSize))
                {
                    if (!done.Add((entry.Zone, open.X, open.Y, open.Z, open.Radius)))
                        continue;
                    double reach = open.Radius + 2 * VoxelSize;
                    var sites = claimsIn(entry.Zone, (open.X - reach, open.Y - reach, open.Z - reach), (open.X + reach, open.Y + reach, open.Z + reach));
                    Func<double, double, double, bool> allowed = sites.Count == 0 ? null
                        : (x, y, z) => !sites.Exists(s => x >= s.MinX && x <= s.MaxX && y >= s.MinY && y <= s.MaxY && z >= s.MinZ && z <= s.MaxZ);
                    var cells = new HashSet<ulong>();
                    SurfaceVertices((open.X, open.Y, open.Z), open.Radius,
                        HealQueue.SpheresNear(entry.Zone, (open.X, open.Y, open.Z), open.Radius, 2 * VoxelSize), cells, entry.Zone, DigLayer, allowed);
                    foreach (ulong id in cells)
                    {
                        Overlay overlay = Of(entry.Zone)[Key(id, DigLayer)];
                        overlay.Version++;
                        overlay.Dirty = true;
                        changed.Add((entry.Zone, id));
                    }
                }
            }
            return changed;
        }

        /// <summary>
        /// Returns the ground inside a global box (world units) to its natural state: every overlay voxel whose centre
        /// lies in the box is dropped, so the client shows its own generated terrain there again. Returns the cells
        /// that changed (to broadcast).
        /// </summary>
        public static List<ulong> RevertBox(double minX, double minY, double minZ, double maxX, double maxY, double maxZ, uint zone = 0)
        {
            var changed = new List<ulong>();
            var reverted = new Dictionary<ulong, List<int>>();
            lock (sync)
            {
                foreach ((ulong key, Overlay overlay) in Of(zone))
                {
                    ulong id = CellOf(key);
                    (_, uint cx, uint cy, uint cz) = TerrainStore.Decode(id);
                    double ox = cx * CellSize, oy = cy * CellSize, oz = cz * CellSize;
                    if (ox > maxX || oy > maxY || oz > maxZ || ox + CellSize < minX || oy + CellSize < minY || oz + CellSize < minZ)
                        continue;
                    bool any = false;
                    for (int iz = 0; iz < BlockData.Size; iz++)
                    {
                        double z = oz + (iz + 0.5) * VoxelSize;
                        if (z < minZ || z > maxZ)
                            continue;
                        for (int ix = 0; ix < BlockData.Size; ix++)
                        {
                            double x = ox + (ix + 0.5) * VoxelSize;
                            if (x < minX || x > maxX)
                                continue;
                            for (int iy = 0; iy < BlockData.Size; iy++)
                            {
                                double y = oy + (iy + 0.5) * VoxelSize;
                                int index = VoxelIndex(ix, iy, iz);
                                if (y < minY || y > maxY || overlay.Blocks.Flags[index] == 0)
                                    continue;
                                overlay.Blocks.Flags[index] = 0;
                                overlay.Blocks.Materials[index] = 0;
                                if (!reverted.TryGetValue(id, out var list))
                                    reverted[id] = list = new List<int>();
                                list.Add(index);
                                any = true;
                            }
                        }
                    }
                    if (!any)
                        continue;
                    overlay.Version++;
                    overlay.Dirty = true;
                    if (!changed.Contains(id))
                        changed.Add(id);
                }
            }
            // Natural ground again: nobody's (Voxel Vision).
            foreach ((ulong id, List<int> list) in reverted)
                VoxelOwnership.Set(zone, id, list, _ => false, 0);
            return changed;
        }

        // ---- temporary destruction on build sites (0.695 Play Mode; TASKS 2.1 / 2.6) ------------------------------------

        /// <summary>Does the build layer (1) hold a solid voxel with a material here? Callers hold <see cref="sync"/>.</summary>
        private static bool BuildSolid(uint zone, ulong id, int index)
            => Of(zone).TryGetValue(Key(id, BuildLayer), out Overlay build)
               && (build.Blocks.Flags[index] & 1) != 0 && build.Blocks.Materials[index] != 0;

        /// <summary>Does the build layer (1) set this voxel at all (solid, carved or a vertex)? Callers hold <see cref="sync"/>.</summary>
        private static bool BuildHas(uint zone, ulong id, int index)
            => Of(zone).TryGetValue(Key(id, BuildLayer), out Overlay build) && build.Blocks.Flags[index] != 0;

        /// <summary>
        /// How long a Linking Area's "...Destroy Voxels in this area." lasts: 10000 seconds, a number the developer
        /// said was picked without a reason (28 Jul 2016, P6203).
        /// </summary>
        public static readonly TimeSpan AreaDestructionHeal = TimeSpan.FromSeconds(10000);

        /// <summary>
        /// Visits the voxels whose centres lie in a vertical cylinder (global dm centre, radius across x / z, half height up
        /// and down) — the Linking Area's shape (StoryTools.InArea) — as (cell, index, global centre). Callers hold
        /// <see cref="sync"/>.
        /// </summary>
        private static void ForCylinder((double X, double Y, double Z) c, double radius, double halfHeight,
            Action<ulong, int, double, double, double> visit)
        {
            long x0 = (long)Math.Floor((c.X - radius) / VoxelSize), x1 = (long)Math.Floor((c.X + radius) / VoxelSize);
            long y0 = (long)Math.Floor((c.Y - halfHeight) / VoxelSize), y1 = (long)Math.Floor((c.Y + halfHeight) / VoxelSize);
            long z0 = (long)Math.Floor((c.Z - radius) / VoxelSize), z1 = (long)Math.Floor((c.Z + radius) / VoxelSize);
            double r2 = radius * radius;
            for (long vx = x0; vx <= x1; vx++)
                for (long vz = z0; vz <= z1; vz++)
                {
                    double x = (vx + 0.5) * VoxelSize, z = (vz + 0.5) * VoxelSize;
                    if ((x - c.X) * (x - c.X) + (z - c.Z) * (z - c.Z) > r2)
                        continue;
                    for (long vy = y0; vy <= y1; vy++)
                    {
                        double y = (vy + 0.5) * VoxelSize;
                        if (Math.Abs(y - c.Y) > halfHeight)
                            continue;
                        long cellX = Math.DivRem(vx, BlockData.Size, out long ix), cellY = Math.DivRem(vy, BlockData.Size, out long iy),
                            cellZ = Math.DivRem(vz, BlockData.Size, out long iz);
                        if (ix < 0) { ix += BlockData.Size; cellX--; }
                        if (iy < 0) { iy += BlockData.Size; cellY--; }
                        if (iz < 0) { iz += BlockData.Size; cellZ--; }
                        visit(TerrainStore.CellId(Level, (uint)cellX, (uint)cellY, (uint)cellZ), VoxelIndex((int)ix, (int)iy, (int)iz), x, y, z);
                    }
                }
        }

        /// <summary>
        /// The Linking Area's "...Destroy Voxels in this area." (577548; P6185): the building voxels (layer 1, solid) inside
        /// the area's cylinder are covered by air in the destruction layer (2), the generated ground is left alone, and one
        /// pending heal is queued for the whole area (<see cref="AreaDestructionHeal"/>; it heals inside the site and does
        /// not wait for later destructions: <see cref="HealQueue.IgnoresLater"/>, P6205). The owner's layer 1 is never
        /// written, so their build comes back exactly when the destruction heals (the developer's model: a destroy adds
        /// an empty-space volume on the top layer only, and a heal removes that volume again, P6204).
        /// <para>
        /// <b>Inferred</b> (TASKS 2.6 open question): only player-set voxels are destroyed — the developer's note that
        /// the heal covers only player-set voxels and does not bring back the terrain that was there before (P6201) is
        /// read as: the area action works on what the builder placed, so an area that reaches into the ground does not
        /// dig a pit under the
        /// site. Live may have carved the natural ground too. <paramref name="allowed"/> (global dm) keeps voxels where
        /// destruction is not allowed (row 102 / Play Mode Rules) out.
        /// </para>
        /// Returns the cells changed and the voxels destroyed.
        /// </summary>
        public static (List<ulong> Cells, int Voxels) DestroyArea(uint zone, (double X, double Y, double Z) centre, double radius,
            double halfHeight, Func<double, double, double, bool> allowed = null)
        {
            var cells = new HashSet<ulong>();
            int changed = 0;
            lock (sync)
            {
                ForCylinder(centre, radius, halfHeight, (id, index, x, y, z) =>
                {
                    if (!BuildSolid(zone, id, index) || (allowed != null && !allowed(x, y, z)))
                        return;
                    if (!Of(zone).TryGetValue(Key(id, DigLayer), out Overlay overlay))
                        Of(zone)[Key(id, DigLayer)] = overlay = new Overlay();
                    if ((overlay.Blocks.Flags[index] & 1) != 0 && overlay.Blocks.Materials[index] == 0)
                        return;   // already destroyed
                    overlay.Blocks.SetSolid(index, 0);
                    cells.Add(id);
                    changed++;
                });
                foreach (ulong id in cells)
                {
                    Of(zone)[Key(id, DigLayer)].Version++;
                    Of(zone)[Key(id, DigLayer)].Dirty = true;
                }
            }
            if (cells.Count > 0)
                HealQueue.AddCylinder(zone, centre, radius, halfHeight, cells, AreaDestructionHeal,
                    HealQueue.HealsInSites | HealQueue.IgnoresLater);
            return (cells.ToList(), changed);
        }

        /// <summary>
        /// The Linking Area's "...Heal Voxels in this area." (577549; P6185): every destruction-layer (2) voxel inside the
        /// area's cylinder is dropped, so what lies under it shows again — the owner's building, and the ground where the
        /// owner had not dug (the heal only removes the empty-space volume and touches no other layer, so it never puts
        /// terrain back into a build site, P6204; it is for player-set voxels only, P6201).
        /// The owner's own layer 1 (including space they dug) is not touched. Pending heals stay queued (a heal cancels
        /// nothing, P6205); when due they find nothing left or heal a later destruction early, as on live.
        /// Ore veins (composed under layer 2) are cleared from sites when they are placed. Returns the cells changed.
        /// </summary>
        public static (List<ulong> Cells, int Voxels) HealArea(uint zone, (double X, double Y, double Z) centre, double radius, double halfHeight)
        {
            var cells = new HashSet<ulong>();
            int changed = 0;
            lock (sync)
            {
                var store = Of(zone);
                ForCylinder(centre, radius, halfHeight, (id, index, x, y, z) =>
                {
                    if (!store.TryGetValue(Key(id, DigLayer), out Overlay overlay) || overlay.Blocks.Flags[index] == 0)
                        return;
                    overlay.Blocks.Flags[index] = 0;
                    overlay.Blocks.Materials[index] = 0;
                    cells.Add(id);
                    changed++;
                });
                foreach (ulong id in cells)
                {
                    store[Key(id, DigLayer)].Version++;
                    store[Key(id, DigLayer)].Dirty = true;
                }
            }
            return (cells.ToList(), changed);
        }

        /// <summary>Test: replaces a cell's overlay with saved block data (dev "poitest").</summary>
        public static void SetOverlay(ulong id, byte[] saved, uint zone = 0)
        {
            lock (sync)
            {
                if (!Of(zone).TryGetValue(Key(id, PoiLayer), out Overlay overlay))
                    Of(zone)[Key(id, PoiLayer)] = overlay = new Overlay();
                overlay.Blocks.Load(saved);
                overlay.Version++;
                overlay.Dirty = true;
            }
        }

        /// <summary>
        /// Net solid voxels per material an edit would add (positive) or take away (negative), against the server's
        /// overlay. Voxels the overlay doesn't hold (the generated ground) count as nothing.
        /// </summary>
        public static Dictionary<ushort, int> MaterialDelta(ulong id, byte[] changed, BlockData result, uint zone = 0,
            uint layer = BuildLayer)
        {
            var delta = new Dictionary<ushort, int>();
            void Add(ushort material, int n)
            {
                if (material != 0)
                    delta[material] = delta.GetValueOrDefault(material) + n;
            }
            // Ore veins lie under the edits (as in BuildUpdate): the client's result carries the vein ore it was sent,
            // so a vein voxel the overlay doesn't set was already there. Without this, editing near a vein charged
            // for its ore (Raw Marble → "needs Cut Marble", 2026-10-13). Removing vein ore with build tools refunds
            // nothing: it was never paid for (ore is gathered with a pick, outside build sites).
            // Most cells have no vein: skip the 384 KB scratch cell for them (big brushes touch 100+ cells).
            BlockData veins = OreVeins.Known(id, zone) ? new BlockData() : null;
            bool hasVeins = veins != null && OreVeins.Compose(id, zone, veins);
            lock (sync)
            {
                Of(zone).TryGetValue(Key(id, layer), out Overlay overlay);
                for (int i = 0; i < BlockData.Count; i++)
                {
                    if ((changed[i >> 3] & (1 << (7 - (i & 7)))) == 0)
                        continue;
                    if ((result.Flags[i] & 1) != 0)
                        Add(result.Materials[i], 1);
                    if (overlay != null && overlay.Blocks.Flags[i] != 0)
                    {
                        if ((overlay.Blocks.Flags[i] & 1) != 0)
                            Add(overlay.Blocks.Materials[i], -1);
                    }
                    else if (hasVeins && veins.Flags[i] != 0 && (result.Flags[i] & 1) != 0
                        && result.Materials[i] == veins.Materials[i])
                        Add(veins.Materials[i], -1);   // ore left as it was: no charge (and no refund when it goes)
                }
            }
            return delta;
        }

        /// <summary>
        /// Would every voxel an edit changes be allowed by <paramref name="mayEdit"/> (global units)? The zone's
        /// permission check (ZoneTerrain.MayApply) runs it with the player's claim permissions.
        /// </summary>
        public static bool MayApply(ulong id, byte[] changed, Func<double, double, double, bool> mayEdit)
        {
            (uint level, uint cx, uint cy, uint cz) = TerrainStore.Decode(id);
            if (level != Level)
                return false;
            double ox = cx * CellSize, oy = cy * CellSize, oz = cz * CellSize;
            for (int i = 0; i < BlockData.Count; i++)
            {
                if ((changed[i >> 3] & (1 << (7 - (i & 7)))) == 0)
                    continue;
                int y = i % BlockData.Size, x = i / BlockData.Size % BlockData.Size, z = i / (BlockData.Size * BlockData.Size);
                if (!mayEdit(ox + (x + 0.5) * VoxelSize, oy + (y + 0.5) * VoxelSize, oz + (z + 0.5) * VoxelSize))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// A player's build edit (ApplyDiffRequest): every voxel set in <paramref name="changed"/> (8000-byte mask, MSB
        /// first) takes its value from <paramref name="result"/>. Refused (false) when any changed voxel lies where
        /// <paramref name="mayEdit"/> says no (global units).
        /// </summary>
        public static bool ApplyDiff(ulong id, byte[] changed, BlockData result, Func<double, double, double, bool> mayEdit, out int voxels, uint zone = 0,
            ulong owner = 0, uint layer = BuildLayer)
        {
            voxels = 0;
            (uint level, uint cx, uint cy, uint cz) = TerrainStore.Decode(id);
            if (level != Level)
                return false;
            double ox = cx * CellSize, oy = cy * CellSize, oz = cz * CellSize;
            List<int> indices = ChangedIndices(changed);
            // Index order: y fastest, then x, then z (VoxelIndex). Null mayEdit: already checked (Building).
            if (mayEdit != null)
                foreach (int i in indices)
                {
                    int y = i % BlockData.Size, x = i / BlockData.Size % BlockData.Size, z = i / (BlockData.Size * BlockData.Size);
                    if (!mayEdit(ox + (x + 0.5) * VoxelSize, oy + (y + 0.5) * VoxelSize, oz + (z + 0.5) * VoxelSize))
                        return false;
                }
            lock (sync)
            {
                if (!Of(zone).TryGetValue(Key(id, layer), out Overlay overlay))
                    Of(zone)[Key(id, layer)] = overlay = new Overlay();
                foreach (int i in indices)
                {
                    overlay.Blocks.Flags[i] = result.Flags[i];
                    overlay.Blocks.Materials[i] = result.Materials[i];
                    overlay.Blocks.Offsets[i * 3] = result.Offsets[i * 3];
                    overlay.Blocks.Offsets[i * 3 + 1] = result.Offsets[i * 3 + 1];
                    overlay.Blocks.Offsets[i * 3 + 2] = result.Offsets[i * 3 + 2];
                }
                overlay.Version++;
                overlay.Dirty = true;
            }
            // The builder owns what the edit left solid; what it emptied or healed is nobody's (Voxel Vision).
            VoxelOwnership.Set(zone, id, indices, i => (result.Flags[i] & 1) != 0 && result.Materials[i] != 0, owner);
            voxels = indices.Count;
            return true;
        }

        /// <summary>
        /// First half of a build edit, one read-only pass per cell: the voxels the edit changes, their current values
        /// in the layer (the undo record's "before") and the material difference it would make (solid voxels of the
        /// result minus solid voxels it replaces). Nothing is written, so a refused edit costs nothing more.
        /// </summary>
        public static (int[] Indices, List<VoxelValue> Before) Prepare(ulong id, byte[] changed, BlockData result, uint zone,
            Dictionary<ushort, int> delta, uint layer = BuildLayer)
        {
            int[] indices = ChangedIndices(changed).ToArray();
            var before = new List<VoxelValue>(indices.Length);
            lock (sync)
            {
                Of(zone).TryGetValue(Key(id, layer), out Overlay overlay);
                BlockData blocks = overlay?.Blocks;
                foreach (int i in indices)
                {
                    if ((result.Flags[i] & 1) != 0 && result.Materials[i] != 0)
                        delta[result.Materials[i]] = delta.GetValueOrDefault(result.Materials[i]) + 1;
                    if (blocks == null)
                    {
                        before.Add(new VoxelValue(i, 0, 0, 0, 0, 0));
                        continue;
                    }
                    byte flags = blocks.Flags[i];
                    ushort material = blocks.Materials[i];
                    if ((flags & 1) != 0 && material != 0)
                        delta[material] = delta.GetValueOrDefault(material) - 1;
                    before.Add(new VoxelValue(i, flags, material, blocks.Offsets[i * 3], blocks.Offsets[i * 3 + 1], blocks.Offsets[i * 3 + 2]));
                }
            }
            return (indices, before);
        }

        /// <summary>
        /// Second half of a build edit: writes the result's values at the prepared voxels into the layer (one pass) and
        /// returns them as the undo record's "after". Solid voxels become <paramref name="owner"/>'s, the rest nobody's.
        /// </summary>
        public static List<VoxelValue> Commit(ulong id, int[] indices, BlockData result, uint zone, ulong owner, uint layer = BuildLayer,
            uint clientVersion = 0)
        {
            var after = new List<VoxelValue>(indices.Length);
            lock (sync)
            {
                if (!Of(zone).TryGetValue(Key(id, layer), out Overlay overlay))
                    Of(zone)[Key(id, layer)] = overlay = new Overlay();
                BlockData blocks = overlay.Blocks;
                foreach (int i in indices)
                {
                    byte flags = result.Flags[i], o0 = result.Offsets[i * 3], o1 = result.Offsets[i * 3 + 1], o2 = result.Offsets[i * 3 + 2];
                    ushort material = result.Materials[i];
                    blocks.Flags[i] = flags;
                    blocks.Materials[i] = material;
                    blocks.Offsets[i * 3] = o0;
                    blocks.Offsets[i * 3 + 1] = o1;
                    blocks.Offsets[i * 3 + 2] = o2;
                    after.Add(new VoxelValue(i, flags, material, o0, o1, o2));
                }
                // The client sends the version it gave the layer after this edit (CRC32 of its compressed layer,
                // CompressBlockData); adopting it keeps the server's and the client's versions equal, so its cached copy
                // stays valid (no re-download) and the next edit's base version can be checked.
                overlay.Version = clientVersion != 0 ? clientVersion : overlay.Version + 1;
                overlay.Dirty = true;
            }
            VoxelOwnership.Set(zone, id, indices, i => (result.Flags[i] & 1) != 0 && result.Materials[i] != 0, owner);
            return after;
        }

        /// <summary>The voxel indices set in an 8000-byte changed mask (MSB first), skipping empty bytes.</summary>
        /// <summary>
        /// Diagnostic: how the server's copy of a layer differs from the cell an edit sent (voxels with other flags /
        /// material / vertex offsets, and how many lie outside the changed mask). The sent cell holds the edit's voxels,
        /// not the whole layer, so voxels only the server has (earlier edits) are expected; a voxel the client sent that
        /// the server lacks or holds differently would mean a relog or another player sees something else.
        /// </summary>
        /// <summary>Per (client flags, server flags) pair of voxels that differ outside the changed mask (diagnostic).</summary>
        public static readonly Dictionary<(int Client, int Server), int> LastOutside = new();

        public static (int Flags, int Materials, int Offsets, int OutsideMask) Compare(ulong id, uint zone, BlockData client,
            byte[] changed, uint layer = BuildLayer)
        {
            int f = 0, m = 0, o = 0, outside = 0;
            lock (sync)
            {
                Of(zone).TryGetValue(Key(id, layer), out Overlay overlay);
                BlockData server = overlay?.Blocks;
                for (int i = 0; i < BlockData.Count; i++)
                {
                    byte cf = client.Flags[i], sf = server?.Flags[i] ?? 0;
                    bool diff = false;
                    if ((cf & 3) != (sf & 3)) { f++; diff = true; }
                    else
                    {
                        if ((cf & 1) != 0 && client.Materials[i] != server.Materials[i]) { m++; diff = true; }
                        if ((cf & 2) != 0 && (client.Offsets[i * 3] != server.Offsets[i * 3] || client.Offsets[i * 3 + 1] != server.Offsets[i * 3 + 1]
                            || client.Offsets[i * 3 + 2] != server.Offsets[i * 3 + 2])) { o++; diff = true; }
                    }
                    if (diff && (changed[i >> 3] & (1 << (7 - (i & 7)))) == 0)
                    {
                        outside++;
                        lock (LastOutside)
                            LastOutside[(cf & 3, sf & 3)] = LastOutside.GetValueOrDefault((cf & 3, sf & 3)) + 1;
                    }
                }
            }
            return (f, m, o, outside);
        }

        public static List<int> ChangedIndices(byte[] changed)
        {
            var indices = new List<int>(4096);
            for (int b = 0; b < changed.Length; b++)
            {
                byte bits = changed[b];
                if (bits == 0)
                    continue;
                for (int k = 0; k < 8; k++)
                    if ((bits & (0x80 >> k)) != 0)
                        indices.Add(b * 8 + k);
            }
            return indices;
        }

        /// <summary>One overlay voxel's stored value (sparse undo/redo records).</summary>
        public readonly record struct VoxelValue(int Index, byte Flags, ushort Material, byte O0, byte O1, byte O2);

        /// <summary>
        /// The overlay's current values of the voxels set in <paramref name="changed"/> (all zero where the cell has no
        /// overlay: zero = no override, the generated ground shows).
        /// </summary>
        public static List<VoxelValue> Read(ulong id, byte[] changed, uint zone = 0, uint layer = BuildLayer)
        {
            List<int> indices = ChangedIndices(changed);
            var values = new List<VoxelValue>(indices.Count);
            lock (sync)
            {
                Of(zone).TryGetValue(Key(id, layer), out Overlay overlay);
                foreach (int i in indices)
                {
                    values.Add(overlay == null ? new VoxelValue(i, 0, 0, 0, 0, 0)
                        : new VoxelValue(i, overlay.Blocks.Flags[i], overlay.Blocks.Materials[i],
                            overlay.Blocks.Offsets[i * 3], overlay.Blocks.Offsets[i * 3 + 1], overlay.Blocks.Offsets[i * 3 + 2]));
                }
            }
            return values;
        }

        /// <summary>Net solid voxels per material that writing <paramref name="values"/> would add (+) or remove (−).</summary>
        public static Dictionary<ushort, int> MaterialDelta(ulong id, List<VoxelValue> values, uint zone = 0, uint layer = BuildLayer)
        {
            var delta = new Dictionary<ushort, int>();
            lock (sync)
            {
                Of(zone).TryGetValue(Key(id, layer), out Overlay overlay);
                foreach (VoxelValue v in values)
                {
                    if ((v.Flags & 1) != 0 && v.Material != 0)
                        delta[v.Material] = delta.GetValueOrDefault(v.Material) + 1;
                    if (overlay != null && (overlay.Blocks.Flags[v.Index] & 1) != 0 && overlay.Blocks.Materials[v.Index] != 0)
                        delta[overlay.Blocks.Materials[v.Index]] = delta.GetValueOrDefault(overlay.Blocks.Materials[v.Index]) - 1;
                }
            }
            return delta;
        }

        /// <summary>
        /// Clears every override in a cell (dev "clearcell": the generated ground shows again); the zone sends it out.
        /// </summary>
        public static void ClearCell(ulong id, uint zone)
        {
            lock (sync)
                for (uint layer = 0; layer < LayerCount; layer++)
                {
                    if (!Of(zone).TryGetValue(Key(id, layer), out Overlay overlay))
                        continue;
                    Array.Clear(overlay.Blocks.Flags);
                    Array.Clear(overlay.Blocks.Materials);
                    Array.Clear(overlay.Blocks.Offsets);
                    overlay.Version++;
                    overlay.Dirty = true;
                }
            VoxelOwnership.ClearCell(zone, id);
        }

        /// <summary>
        /// Writes stored voxel values back into a cell's overlay (undo / redo, paste). Solid voxels written become
        /// <paramref name="owner"/>'s and the others nobody's (Voxel Vision); owner 0 leaves ownership alone. Interior
        /// empty space (0xFFFC) is never stored as a material: a value carrying it is written as air (the paste paths
        /// already resolve it, Building.PastedMaterial; this keeps any other writer from storing it: <see cref="RepairInteriorAir"/>).
        /// </summary>
        public static void Write(ulong id, List<VoxelValue> values, uint zone = 0, ulong owner = 0, uint layer = BuildLayer)
        {
            if (values.Exists(v => (v.Flags & 1) != 0 && v.Material == VoxelTemplate.InteriorAir))
                values = values.Select(v => (v.Flags & 1) != 0 && v.Material == VoxelTemplate.InteriorAir ? v with { Material = 0 } : v).ToList();
            if (owner != 0)
            {
                var solid = values.Where(v => (v.Flags & 1) != 0 && v.Material != 0).Select(v => v.Index).ToHashSet();
                VoxelOwnership.Set(zone, id, values.Select(v => v.Index), solid.Contains, owner);
            }
            lock (sync)
            {
                if (!Of(zone).TryGetValue(Key(id, layer), out Overlay overlay))
                    Of(zone)[Key(id, layer)] = overlay = new Overlay();
                foreach (VoxelValue v in values)
                {
                    overlay.Blocks.Flags[v.Index] = v.Flags;
                    overlay.Blocks.Materials[v.Index] = v.Material;
                    overlay.Blocks.Offsets[v.Index * 3] = v.O0;
                    overlay.Blocks.Offsets[v.Index * 3 + 1] = v.O1;
                    overlay.Blocks.Offsets[v.Index * 3 + 2] = v.O2;
                }
                overlay.Version++;
                overlay.Dirty = true;
            }
        }

        /// <summary>
        /// Developer report (dev "cellstats"): what each stored layer of the cell at a local point holds — solid voxels
        /// per material, carving (air) voxels, vertex-only voxels.
        /// </summary>
        public static string Describe(Vector3 local, uint zone)
        {
            ulong cell = CellAt(local);
            var text = new System.Text.StringBuilder($"cell {cell:X16}:");
            lock (sync)
                for (uint layer = 0; layer < LayerCount; layer++)
                {
                    if (!Of(zone).TryGetValue(Key(cell, layer), out Overlay overlay))
                        continue;
                    BlockData b = overlay.Blocks;
                    var solids = new Dictionary<ushort, int>();
                    int air = 0, vertexOnly = 0;
                    for (int i = 0; i < BlockData.Count; i++)
                    {
                        if (b.Flags[i] == 0) continue;
                        if ((b.Flags[i] & 1) == 0) { vertexOnly++; continue; }
                        if (b.Materials[i] == 0) air++;
                        else solids[b.Materials[i]] = solids.GetValueOrDefault(b.Materials[i]) + 1;
                    }
                    text.Append($" layer {layer}: air {air}, vertex-only {vertexOnly}, solid "
                        + string.Join(", ", solids.OrderByDescending(x => x.Value).Take(6).Select(x => $"{x.Key}×{x.Value}")) + ";");
                }
            return text.ToString();
        }

        /// <summary>The level-2 cell containing a local point.</summary>
        public static ulong CellAt(Vector3 local)
        {
            (double x, double y, double z) = ToGlobal(local);
            return TerrainStore.CellId(Level, (uint)Math.Floor(x / CellSize), (uint)Math.Floor(y / CellSize),
                (uint)Math.Floor(z / CellSize));
        }

        /// <summary>
        /// Client local metres → global decimetres (local × 10 + zone origin).
        /// </summary>
        public static (double X, double Y, double Z) ToGlobal(Vector3 local)
            => (local.X * 10 + TerrainStore.ZoneOrigin.X, local.Y * 10 + TerrainStore.ZoneOrigin.Y,
                local.Z * 10 + TerrainStore.ZoneOrigin.Z);

        /// <summary>
        /// Does the server hold anything for this cell (an edited layer, ore veins, or a coarse copy)?
        /// </summary>
        public static bool Has(ulong cellId, uint zone = 0)
        {
            lock (sync)
                for (uint layer = 0; layer < LayerCount; layer++)
                    if (Of(zone).ContainsKey(Key(cellId, layer)))
                        return true;
            return OreVeins.Known(cellId, zone) || TerrainLod.Has(cellId, zone == 0 ? Home : zone);
        }

        /// <summary>Does the store hold a voxel layer of this cell (not ore veins or coarse copies)? (SolidityGrid.)</summary>
        public static bool HasLayers(ulong cellId, uint zone)
        {
            lock (sync)
                for (uint layer = 0; layer < LayerCount; layer++)
                    if (Of(zone).ContainsKey(Key(cellId, layer)))
                        return true;
            return false;
        }

        /// <summary>
        /// The composite solidity of a cell's stored layers for the creature navigation (<see cref="SolidityGrid"/>, OPEN-LEADS
        /// L133): <paramref name="state"/> (64,000, BlockData order) gets 0 = no layer writes the voxel (the generated ground
        /// shows), 1 = air (material 0 / InteriorAir), 2 = solid, layers 0, 1, 2 in that order with a later layer's written
        /// voxel over an earlier one's (what the client draws). False when no layer is stored. Reads without expanding: an
        /// expanded layer is read in place under the store's lock; a compact or spilled one has its saved bytes taken under
        /// the lock and only its flags and materials decompressed after it (into <paramref name="flags"/> /
        /// <paramref name="materials"/>), so a whole site read neither holds the lock long nor keeps 384 KB per cell.
        /// </summary>
        public static bool Solidity(ulong cellId, uint zone, byte[] state, byte[] flags, ushort[] materials)
        {
            Array.Clear(state);
            bool any = false;
            for (uint layer = 0; layer < LayerCount; layer++)
            {
                byte[] saved = null;
                lock (sync)
                {
                    if (!Of(zone).TryGetValue(Key(cellId, layer), out Overlay overlay))
                        continue;
                    any = true;
                    if (overlay.Expanded)
                    {
                        Compose(overlay.Blocks.Flags, overlay.Blocks.Materials, state);
                        continue;
                    }
                    saved = overlay.SavedBytes();
                }
                if (saved == null || !BlockData.LoadSolidity(saved, flags, materials))
                    continue;
                Compose(flags, materials, state);
            }
            return any;
        }

        private static void Compose(byte[] flags, ushort[] materials, byte[] state)
        {
            for (int i = 0; i < BlockData.Count; i++)
                if ((flags[i] & 1) != 0)
                    state[i] = materials[i] is 0 or VoxelTemplate.InteriorAir ? (byte)1 : (byte)2;
        }

        /// <summary>The landscapes that have edits.</summary>
        public static List<uint> Zones()
        {
            lock (sync)
                return stores.Where(z => z.Value.Count > 0).Select(z => z.Key).ToList();
        }

        /// <summary>Every edited cell of one layer of a landscape with its version (cheap: nothing is expanded).</summary>
        internal static Dictionary<ulong, uint> Versions(uint zone, uint layer)
        {
            lock (sync)
                return Of(zone).Where(c => LayerOf(c.Key) == layer).ToDictionary(c => CellOf(c.Key), c => c.Value.Version);
        }

        /// <summary>Reads one edited cell layer's voxels under the store lock (packed again afterwards if it was).</summary>
        internal static void WithCell(uint zone, ulong id, uint layer, Action<BlockData> read)
        {
            lock (sync)
            {
                if (!Of(zone).TryGetValue(Key(id, layer), out Overlay overlay))
                    return;
                bool wasExpanded = overlay.Expanded;
                read(overlay.Blocks);
                if (!wasExpanded)
                    overlay.Compact();
            }
        }

        /// <summary>One number for "what the server would send for this cell now" (all layers; for de-duplication).</summary>
        public static uint CurrentVersion(ulong cellId, uint zone)
        {
            uint sum = 0;
            for (uint layer = 0; layer < LayerCount; layer++)
                sum += LayerVersion(cellId, zone, layer);
            return sum;
        }

        /// <summary>The version a CellsUpdate carries for one layer of a cell (0 = the server has nothing there).</summary>
        public static uint LayerVersion(ulong cellId, uint zone, uint layer)
        {
            if (TerrainStore.Decode(cellId).Level > Level)
                return TerrainLod.VersionOf(cellId, zone == 0 ? Home : zone, layer);
            lock (sync)
                return VersionOf(cellId, zone, layer);
        }

        private static int versionSamples;

        /// <summary>
        /// Research (2026-10-13): saves a few cells whose layer-1 version the client holds differently from ours, with
        /// the client's number, to TerrainEdits/version-samples, to test whether the client's version (CRC32 of the
        /// layer compressed by CompressBlockData, FUN_143f85d50) can be reproduced from the server's data.
        /// </summary>
        public static void SampleVersion(ulong cellId, uint zone, uint[] held)
        {
            if (folder == null || versionSamples >= 20 || held[BuildLayer] == 0)
                return;
            byte[] raw;
            lock (sync)
            {
                if (!Of(zone).TryGetValue(Key(cellId, BuildLayer), out Overlay overlay) || overlay.Version == held[BuildLayer])
                    return;
                raw = overlay.Blocks.Serialize();
            }
            versionSamples++;
            string dir = Path.Combine(folder, "version-samples");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, $"{cellId:X16}_{held[BuildLayer]}.raw"), raw);
        }

        /// <summary>
        /// Every layer's version of a level-2 cell (edit overlay, plus the vein layer's for layer 2), read together under
        /// the store lock: what the zone sends in manifest notes for a changed cell (ZoneTerrain.Broadcast / Announce).
        /// </summary>
        public static uint[] LayerVersions(ulong cellId, uint zone)
        {
            var versions = new uint[LayerCount];
            lock (sync)
                for (uint layer = 0; layer < LayerCount; layer++)
                    versions[layer] = VersionOf(cellId, zone, layer);
            return versions;
        }

        /// <summary>Edit overlay version, plus the vein layer's for layer 2 (both only grow).</summary>
        private static uint VersionOf(ulong cellId, uint zone, uint layer)
            => (Of(zone).TryGetValue(Key(cellId, layer), out Overlay overlay) ? overlay.Version : 0)
                + (layer == DigLayer ? OreVeins.VersionOf(cellId, zone) : 0);

        /// <summary>
        /// CellsUpdate (block data, version 0) for a cell: every layer, with its own version, empty where the server
        /// has nothing (a layer left out of the packet keeps the client's old copy, FUN_143f5ce70 → FUN_143f1af90).
        /// Null when the server has nothing at all for the cell, unless <paramref name="always"/>.
        /// </summary>
        public static byte[] BuildUpdate(ulong cellId, uint zone = 0, bool always = false)
        {
            if (TerrainStore.Decode(cellId).Level > Level)
                return TerrainLod.BuildUpdate(cellId, zone == 0 ? Home : zone);
            var layers = new List<(uint Layer, uint Version, byte[] Blob)>();
            bool any = false;
            lock (sync)
            {
                for (uint layer = 0; layer < LayerCount; layer++)
                {
                    Of(zone).TryGetValue(Key(cellId, layer), out Overlay overlay);
                    bool ore = layer == DigLayer && OreVeins.Known(cellId, zone);
                    BlockData blocks = overlay?.Blocks;
                    if (ore)
                    {
                        // Ore veins lie under the digging: voxels the dig layer doesn't set get the vein's ore.
                        var composed = new BlockData();
                        if (blocks != null)
                        {
                            Array.Copy(blocks.Flags, composed.Flags, BlockData.Count);
                            Array.Copy(blocks.Materials, composed.Materials, BlockData.Count);
                            Array.Copy(blocks.Offsets, composed.Offsets, BlockData.Count * 3);
                        }
                        OreVeins.Compose(cellId, zone, composed);
                        blocks = composed;
                    }
                    if (blocks != null)
                        any = true;
                    layers.Add((layer, VersionOf(cellId, zone, layer), (blocks ?? Empty).ToLayerBlob()));
                }
            }
            return any || always ? TerrainStore.BuildBlocksUpdate(cellId, layers) : null;
        }

        private static readonly BlockData Empty = new();

        /// <summary>Manifest entries (cell, layer version) of one layer, for the zone-in modified-cell manifest.</summary>
        public static List<(ulong Cell, uint Version)> ManifestEntries(uint zone, uint layer)
        {
            var entries = new Dictionary<ulong, uint>();
            lock (sync)
            {
                foreach ((ulong key, Overlay overlay) in Of(zone))
                    if (LayerOf(key) == layer)
                        entries[CellOf(key)] = overlay.Version;
                if (layer == DigLayer)
                    foreach ((ulong id, uint version) in OreVeins.Entries(zone))
                        entries[id] = entries.GetValueOrDefault(id) + version;
            }
            foreach ((ulong id, uint version) in TerrainLod.Entries(zone == 0 ? Home : zone, layer))
                entries[id] = version;
            return entries.Select(e => (e.Key, e.Value)).ToList();
        }

        /// <summary>
        /// One-time move to layers (2026-10-13): before, every edit was one layer-0 overlay. Voxels inside build sites
        /// are building and move to layer 1 (the layer the client keeps its own edits in); everything else stays in
        /// layer 0 and heals as before. Stamped (permanent) cells stay layer 0. A marker file keeps it to one run.
        /// </summary>
        private static bool migrationChecked;

        public static void MigrateToLayers(Func<uint, (double, double, double), (double, double, double),
            List<(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)>> claimsIn)
        {
            if (folder == null || migrationChecked)
                return;
            migrationChecked = true;
            string marker = Path.Combine(folder, "layers.v2");
            if (File.Exists(marker))
                return;
            int moved = 0, cellsMoved = 0;
            lock (sync)
                foreach ((uint zone, var overlays) in stores)
                {
                    permanent.TryGetValue(zone, out var keep);
                    foreach ((ulong key, Overlay overlay) in overlays.ToList())
                    {
                        if (LayerOf(key) != PoiLayer || keep?.Contains(key) == true)
                            continue;
                        (_, uint cx, uint cy, uint cz) = TerrainStore.Decode(key);
                        double ox = cx * CellSize, oy = cy * CellSize, oz = cz * CellSize;
                        var sites = claimsIn(zone, (ox, oy, oz), (ox + CellSize, oy + CellSize, oz + CellSize));
                        if (sites.Count == 0)
                            continue;
                        bool wasExpanded = overlay.Expanded;
                        BlockData from = overlay.Blocks;
                        Overlay target = null;
                        for (int iz = 0; iz < BlockData.Size; iz++)
                            for (int ix = 0; ix < BlockData.Size; ix++)
                                for (int iy = 0; iy < BlockData.Size; iy++)
                                {
                                    int i = VoxelIndex(ix, iy, iz);
                                    if (from.Flags[i] == 0)
                                        continue;
                                    double x = ox + (ix + 0.5) * VoxelSize, y = oy + (iy + 0.5) * VoxelSize, z = oz + (iz + 0.5) * VoxelSize;
                                    if (!sites.Exists(b => x >= b.MinX && x <= b.MaxX && y >= b.MinY && y <= b.MaxY && z >= b.MinZ && z <= b.MaxZ))
                                        continue;
                                    if (target == null && !overlays.TryGetValue(Key(key, BuildLayer), out target))
                                        overlays[Key(key, BuildLayer)] = target = new Overlay();
                                    target.Blocks.Flags[i] = from.Flags[i];
                                    target.Blocks.Materials[i] = from.Materials[i];
                                    target.Blocks.Offsets[i * 3] = from.Offsets[i * 3];
                                    target.Blocks.Offsets[i * 3 + 1] = from.Offsets[i * 3 + 1];
                                    target.Blocks.Offsets[i * 3 + 2] = from.Offsets[i * 3 + 2];
                                    from.Flags[i] = 0;
                                    from.Materials[i] = 0;
                                    moved++;
                                }
                        if (target == null)
                        {
                            if (!wasExpanded)
                                overlay.Compact();
                            continue;
                        }
                        cellsMoved++;
                        target.Version = overlay.Version + 1;
                        target.Dirty = true;
                        overlay.Version++;
                        overlay.Dirty = true;
                    }
                }
            File.WriteAllText(marker, "Build-site voxels moved from layer 0 to layer 1.");
            log.Info($"Terrain layers: moved {moved} build-site voxels in {cellsMoved} cell(s) to layer 1.");
        }
    }
}
