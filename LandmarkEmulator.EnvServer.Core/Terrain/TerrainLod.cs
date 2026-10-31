using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// Coarser copies of player edits. The client draws far terrain from coarser cells (level L+1 = twice the edge,
    /// half the voxel resolution) that it generates itself unless the manifest lists a server copy. The captured live
    /// manifest has layers 0 and 1 at every level from 2 to 10 and layer 2 at levels 2-4; our edits exist only at
    /// level 2, so wherever the client switched to a coarser level (moving, looking further) the original ground came
    /// back: walls appeared only up close, dug terrain floated in the air (2026-10-13). Each edited level-2 cell layer
    /// gets copies up the levels (layers 0/1 to <see cref="MaxLevel"/>, layer 2 to <see cref="MaxDigLevel"/>), built
    /// with a port of VoxelFarm's own octree simplification (<see cref="UpdateBlockLod"/>). Copies are block layers like
    /// the overlays (CellsUpdate version 0), listed in the manifest under their layer with a version that changes
    /// whenever the copy changes, so clients fetch one only when they draw it.
    /// <para>The client never builds coarse block data itself [P]: VoxelFarm::updateBlockLOD (0.695 FUN_142aadbb0) and
    /// updateBlockLODDeprecated (FUN_142aac2b0) have no call, LEA or pointer anywhere in Landmark64.exe (re/tools/refscan.py;
    /// only their exception-table entries), and ClientCellRequest (FUN_14401a660) asks for any level the manifest lists.
    /// So what a client draws past the level-2 ring is exactly what the server sends; live's server ran VoxelFarm, so the
    /// copies follow that function. Where each level is drawn (trace/0695/lod-qef.md): the scene constructor
    /// FUN_143f8aa40 picks clipmap radius preset 1 = {6, 6, 6} cells for levels 2-4 (table 0x14499d2c0, boxes FUN_142b1ced0),
    /// so level 2 reaches about 80-112 m from the camera, level 3 160-224 m, level 4 320-448 m [P].</para>
    /// <para>Built for every landscape at start, then kept up to date in the background: once a layer's edits have
    /// been quiet for <see cref="Quiet"/>, only the copies that read the changed cells are rebuilt and the players there
    /// get a manifest note per copy that came out different (21 bytes). Waiting for quiet keeps fetches away from cells
    /// being edited (a pending fetch makes the client refuse new edits).</para>
    /// </summary>
    public static class TerrainLod
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Serve the copies (WorldServer.json / EnvServer.json TerrainLodCopies, default on: live served them). The first
        /// version (majority vote, centred vertices) drew cubes and blank patches; the second (2026-10-13 to 2026-11-01:
        /// the corner child's state and a mass point, only where the corner child was solid) dropped the vertex of every
        /// coarse cube whose low corner was air, so smooth shapes turned to stairs past ~100 m. This one is VoxelFarm's.
        /// </summary>
        public static bool Enabled;

        /// <summary>
        /// Told which copies of a landscape's layer changed after a refresh (not after the start-up build): the zone sends
        /// its players a manifest note per copy (ZoneTerrain.LodCopiesChanged, through the Environment Server's
        /// LodCopiesChanged). Called on the refresh's background thread.
        /// </summary>
        public static Action<uint, uint, IReadOnlyList<(ulong Cell, uint Version)>> CopiesChanged;

        /// <summary>Coarsest level built for layers 0 and 1 (level 6 = 256 m cells), and for layer 2 (digging).</summary>
        public const uint MaxLevel = 6, MaxDigLevel = 4;
        private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(5);

        /// <summary>
        /// A copy's version comes from its content (<see cref="ContentVersion"/>): VersionTag | (CRC-32 of the copy's
        /// compact form &amp; <see cref="VersionMask"/>). Copies live only in memory and are all rebuilt at every start
        /// (<see cref="BuildAll"/>), and since the VoxelFarm port a copy also reads the cells next to its children
        /// (<see cref="Readers"/>). A version made from the children's versions (their sum | 0x20000000, the corner /
        /// mass-point rule) could therefore come back after a restart for different content: a client that cached the
        /// copy before a neighbour's edit was told "up to date" (TerrainHandler.Answer) and kept the old shape. With the
        /// content's CRC the same number means the same bytes after any restart (the client's own layer versions are a CRC
        /// too, FUN_142ac1800). Range: bit 30 set, bits 29 and 31 clear. Every version of the 0x20000000 rule had bit 29
        /// set (an OR of a second bit would not do: a sum with bits 29 and 30 set gives the same number under both), so
        /// every client fetches each copy once more. 0 ("none") and the client's 0x7fffffff (trace/systems/terrain.md)
        /// are outside the range.
        /// </summary>
        private const uint VersionTag = 0x40000000, VersionMask = 0x1FFFFFFF;

        /// <summary>
        /// The version of a copy whose compact form is <paramref name="saved"/>, replacing a copy at
        /// <paramref name="previous"/> (0 if none). A CRC equal to the previous version (different bytes, about 1 in
        /// 2^29) flips bit 0, so a client holding the old copy still fetches the new one.
        /// </summary>
        internal static uint ContentVersion(byte[] saved, uint previous)
        {
            uint version = VersionTag | (ClientLayerCodec.Crc32(saved, saved.Length) & VersionMask);
            return version == previous ? version ^ 1 : version;
        }

        private class Copy
        {
            public byte[] Blob;      // CellsUpdate layer blob
            public byte[] Saved;     // BlockData.Save form, to rebuild the level above
            public uint Version;
            /// <summary>
            /// No voxels left (kept so the client gets an empty layer that puts the generated ground back). The level
            /// above reads it as missing, as after a restart, where no empty copy is built.
            /// </summary>
            public bool Empty;
        }

        private static readonly object sync = new();
        /// <summary>Per (landscape, layer): coarse cell id → copy.</summary>
        private static readonly Dictionary<(uint Zone, uint Layer), Dictionary<ulong, Copy>> cells = new();
        /// <summary>Per (landscape, layer): the level-2 versions the copies were built from.</summary>
        private static readonly Dictionary<(uint Zone, uint Layer), Dictionary<ulong, uint>> built = new();
        /// <summary>Per (landscape, layer): the level-2 versions at the last check, and when they last moved.</summary>
        private static readonly Dictionary<(uint Zone, uint Layer), (Dictionary<ulong, uint> Versions, DateTime Moved)> lastLook = new();
        private static DateTime nextCheck = DateTime.MinValue;
        private static Task running;

        /// <summary>
        /// <see cref="TerrainEdits.ChangeStamp"/> at the last look, and whether a layer still had changes not built yet
        /// (waiting for <see cref="Quiet"/>, or a refresh in progress). With neither, nothing can have changed and the look
        /// is skipped: until 2026-10-24 every look copied every cell's version (TerrainEdits.Versions) every 5 s.
        /// </summary>
        private static long lookedAtStamp = -1;
        private static bool pending;

        private static uint TopLevel(uint layer) => layer == TerrainEdits.DigLayer ? MaxDigLevel : MaxLevel;

        private static Dictionary<ulong, Copy> Of(uint zone, uint layer)
        {
            if (!cells.TryGetValue((zone, layer), out var store))
                cells[(zone, layer)] = store = new();
            return store;
        }

        public static bool Has(ulong id, uint zone)
        {
            if (!Enabled)
                return false;
            lock (sync)
                for (uint layer = 0; layer < TerrainEdits.LayerCount; layer++)
                    if (Of(zone, layer).ContainsKey(id))
                        return true;
            return false;
        }

        public static uint VersionOf(ulong id, uint zone, uint layer)
        {
            if (!Enabled)
                return 0;
            lock (sync)
                return Of(zone, layer).TryGetValue(id, out Copy copy) ? copy.Version : 0;
        }

        private static readonly Lazy<byte[]> EmptyBlob = new(() => new BlockData().ToLayerBlob());
        private static readonly Lazy<byte[]> EmptySaved = new(() => new BlockData().Save(System.IO.Compression.CompressionLevel.Fastest));

        /// <summary>CellsUpdate for a coarse cell: every layer (empty where there is no copy), or null if none.</summary>
        public static byte[] BuildUpdate(ulong id, uint zone)
        {
            if (!Enabled)
                return null;
            var layers = new List<(uint Layer, uint Version, byte[] Blob)>();
            bool any = false;
            lock (sync)
                for (uint layer = 0; layer < TerrainEdits.LayerCount; layer++)
                    if (Of(zone, layer).TryGetValue(id, out Copy copy))
                    {
                        layers.Add((layer, copy.Version, copy.Blob));
                        any = true;
                    }
                    else
                        layers.Add((layer, 0, EmptyBlob.Value));
            return any ? TerrainStore.BuildBlocksUpdate(id, layers) : null;
        }

        /// <summary>Manifest entries of the coarse copies of one layer on a landscape.</summary>
        public static List<(ulong Cell, uint Version)> Entries(uint zone, uint layer)
        {
            if (!Enabled)
                return new();
            lock (sync)
                return Of(zone, layer).Select(c => (c.Key, c.Value.Version)).ToList();
        }

        public static ulong Parent(ulong id)
        {
            (uint level, uint x, uint y, uint z) = TerrainStore.Decode(id);
            return TerrainStore.CellId(level + 1, x >> 1, y >> 1, z >> 1);
        }

        /// <summary>
        /// The parents whose copy reads cell <paramref name="id"/>: a parent P reads its 2 × 2 × 2 children and the child
        /// cells next to them (cells 2P − 1 .. 2P + 2 per axis, <see cref="Grid(ulong, Func{ulong, BlockData})"/>), so a
        /// cell c is read by the parents of c − 1, c and c + 1 per axis (2 parents per axis, 8 in all). The corner /
        /// mass-point rule read only the children.
        /// </summary>
        private static IEnumerable<ulong> Readers(ulong id)
        {
            (uint level, uint x, uint y, uint z) = TerrainStore.Decode(id);
            var seen = new HashSet<ulong>();
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        long nx = x + dx, ny = y + dy, nz = z + dz;
                        if (nx < 0 || ny < 0 || nz < 0)
                            continue;
                        ulong parent = TerrainStore.CellId(level + 1, (uint)nx >> 1, (uint)ny >> 1, (uint)nz >> 1);
                        if (seen.Add(parent))
                            yield return parent;
                    }
        }

        /// <summary>
        /// Builds the copies of every landscape with edits (server start, in the background). Player building (layer 1)
        /// and digging (layer 2) first, the world stamps (layer 0: outposts, ruins, the Spire; by far the most cells) last.
        /// </summary>
        public static void BuildAll()
        {
            if (!Enabled)
                return;
            running = Task.Run(() =>
            {
                List<uint> zones = TerrainEdits.Zones();
                foreach (uint layer in new[] { TerrainEdits.BuildLayer, TerrainEdits.DigLayer, TerrainEdits.PoiLayer })
                    foreach (uint zone in zones)
                        Refresh(zone, layer, all: true);
            });
        }

        /// <summary>
        /// Threads one refresh builds on (dedicated, below-normal priority, so the thread pool and the zone ticks are not
        /// held up): a quarter of the cores, 1 to 4. The start-up build of a landscape with ~24,000 stamped cells takes
        /// about a minute of CPU (trace/0695/lod-qef.md "Cost").
        /// </summary>
        public static int Workers = Math.Clamp(Environment.ProcessorCount / 4, 1, 4);

        private static bool Same(Dictionary<ulong, uint> a, Dictionary<ulong, uint> b)
            => a.Count == b.Count && a.All(v => b.TryGetValue(v.Key, out uint other) && other == v.Value);

        /// <summary>
        /// World tick: every few seconds, (landscape, layer) pairs whose edits differ from what the copies were built
        /// from and have not moved for <see cref="Quiet"/> get a background refresh.
        /// </summary>
        public static void Update()
        {
            DateTime now = DateTime.UtcNow;
            if (!Enabled || now < nextCheck || running is { IsCompleted: false })
                return;
            nextCheck = now + CheckEvery;
            // No cell version moved since the last look and nothing was left to build: skip the copy of every version.
            long stamp = TerrainEdits.ChangeStamp;
            if (stamp == lookedAtStamp && !pending)
                return;
            lookedAtStamp = stamp;
            bool waiting = false;
            var due = new List<(uint Zone, uint Layer)>();
            foreach (uint zone in TerrainEdits.Zones())
                for (uint layer = 0; layer < TerrainEdits.LayerCount; layer++)
                {
                    Dictionary<ulong, uint> versions = TerrainEdits.Versions(zone, layer);
                    lock (sync)
                    {
                        if (!lastLook.TryGetValue((zone, layer), out var look) || !Same(look.Versions, versions))
                            lastLook[(zone, layer)] = look = (versions, now);
                        var from = built.TryGetValue((zone, layer), out var b) ? b : null;
                        if (from == null ? versions.Count > 0 : !Same(from, versions))
                        {
                            // Changed and not built yet: look again next time even if nothing else moves.
                            waiting = true;
                            if (now - look.Moved >= Quiet)
                                due.Add((zone, layer));
                        }
                    }
                }
            pending = waiting;
            if (due.Count > 0)
                running = Task.Run(() =>
                {
                    foreach ((uint zone, uint layer) in due)
                        Refresh(zone, layer, all: false);
                });
        }

        /// <summary>
        /// Tests: forgets a landscape's copies and builds them again as a server start does (<see cref="BuildAll"/>; the
        /// copies are not saved anywhere).
        /// </summary>
        internal static void RebuildAsAtStart(uint zone)
        {
            lock (sync)
                for (uint layer = 0; layer < TerrainEdits.LayerCount; layer++)
                {
                    cells.Remove((zone, layer));
                    built.Remove((zone, layer));
                    lastLook.Remove((zone, layer));
                }
            foreach (uint layer in new[] { TerrainEdits.BuildLayer, TerrainEdits.DigLayer, TerrainEdits.PoiLayer })
                Refresh(zone, layer, all: true);
        }

        /// <summary>Dev "lodbuild": refreshes a landscape now (changed copies only, players told).</summary>
        public static void RefreshNow(uint zone)
        {
            for (uint layer = 0; layer < TerrainEdits.LayerCount; layer++)
                Refresh(zone, layer, all: false);
        }

        /// <summary>
        /// What one refresh did (log line; tests): parents built, copies that changed, child-level cells read, wall time,
        /// and the time spent in <see cref="UpdateBlockLod"/> summed over the workers.
        /// </summary>
        internal readonly record struct RefreshStats(int Built, int Changed, int Loads, long Milliseconds, long BuildMilliseconds);

        /// <summary>The last refresh's numbers (tests).</summary>
        internal static RefreshStats LastRefresh { get; private set; }

        /// <summary>
        /// Rebuilds the copies that read the level-2 cells of one layer that changed since the last build (all of them
        /// with <paramref name="all"/>) and tells the players on the landscape which copies changed. A copy that comes
        /// out byte-identical keeps its version (no note, no fetch) and does not dirty the level above: a parent is
        /// rebuilt when any cell it reads changed (<see cref="Readers"/>), and most of those only touch it at the edge.
        /// </summary>
        private static void Refresh(uint zone, uint layer, bool all)
        {
            try
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Dictionary<ulong, uint> versions = TerrainEdits.Versions(zone, layer);
                Dictionary<ulong, uint> before;
                lock (sync)
                    before = !all && built.TryGetValue((zone, layer), out var b) ? b : new Dictionary<ulong, uint>();
                var dirty = new HashSet<ulong>();
                foreach ((ulong id, uint version) in versions)
                    if (all || !before.TryGetValue(id, out uint old) || old != version)
                        dirty.UnionWith(Readers(id));
                foreach (ulong id in before.Keys)
                    if (!versions.ContainsKey(id))
                        dirty.UnionWith(Readers(id));

                var changedCopies = new List<(ulong Cell, uint Version)>();
                int builtCount = 0, loads = 0;
                long buildTicks = 0;
                var workers = new List<Worker>();
                for (uint level = 3; level <= TopLevel(layer) && dirty.Count > 0; level++)
                {
                    var next = new HashSet<ulong>();
                    // The level below: the level-2 overlays, or the copies of the previous pass.
                    uint childLevel = level - 1;
                    Func<ulong, bool> exists;
                    Func<ulong, BlockData, bool> fill;
                    if (childLevel == TerrainEdits.Level)
                    {
                        exists = versions.ContainsKey;
                        fill = (id, into) => TerrainEdits.CopyCell(zone, id, layer, into);
                    }
                    else
                    {
                        exists = id =>
                        {
                            lock (sync)
                                return Of(zone, layer).TryGetValue(id, out Copy copy) && !copy.Empty;
                        };
                        fill = (id, into) =>
                        {
                            Copy copy;
                            lock (sync)
                                Of(zone, layer).TryGetValue(id, out copy);
                            if (copy == null || copy.Empty)
                                return false;
                            into.Load(copy.Saved);
                            return true;
                        };
                    }
                    // Morton order keeps the cells around consecutive parents in a worker's cache; each worker takes runs
                    // of ChunkSize parents.
                    ulong[] order = dirty.OrderBy(Morton).ToArray();
                    int threads = Math.Clamp((order.Length + ChunkSize - 1) / ChunkSize, 1, Math.Max(1, Workers));
                    while (workers.Count < threads)
                        workers.Add(new Worker());
                    int nextChunk = -1;
                    void Work(Worker w)
                    {
                        w.Cache.Reset(exists, fill);
                        for (int c; (c = System.Threading.Interlocked.Increment(ref nextChunk)) * ChunkSize < order.Length;)
                            for (int k = c * ChunkSize; k < Math.Min(order.Length, (c + 1) * ChunkSize); k++)
                                BuildCopy(zone, layer, level, order[k], versions, w, next, changedCopies);
                    }
                    if (threads == 1)
                        Work(workers[0]);
                    else
                    {
                        var started = workers.Take(threads).Select(w => new System.Threading.Thread(() =>
                        {
                            try
                            {
                                Work(w);
                            }
                            catch (Exception e)
                            {
                                w.Failure = e;
                            }
                        })
                        { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal, Name = "TerrainLod" }).ToList();
                        started.ForEach(t => t.Start());
                        started.ForEach(t => t.Join());
                        if (workers.Find(w => w.Failure != null) is Worker failed)
                            throw new AggregateException(failed.Failure);
                    }
                    dirty = next;
                }
                foreach (Worker w in workers)
                {
                    builtCount += w.Built;
                    loads += w.Cache.Loads;
                    buildTicks += w.BuildTicks;
                }
                lock (sync)
                    built[(zone, layer)] = versions;
                if (!all)
                    CopiesChanged?.Invoke(zone, layer, changedCopies);
                long buildMs = buildTicks * 1000 / System.Diagnostics.Stopwatch.Frequency;
                LastRefresh = new RefreshStats(builtCount, changedCopies.Count, loads, watch.ElapsedMilliseconds, buildMs);
                if (changedCopies.Count > 0)
                    log.Info($"Terrain LOD, landscape {zone} layer {layer}: {changedCopies.Count} coarse cell(s) updated "
                        + $"({builtCount} built from {loads} cells read, {watch.ElapsedMilliseconds} ms; building {buildMs} ms "
                        + $"on {workers.Count} thread(s)).");
            }
            catch (Exception e)
            {
                log.Error(e, $"Terrain LOD refresh failed for landscape {zone} layer {layer}.");
            }
        }

        /// <summary>Parents a worker takes at a time (consecutive in Morton order).</summary>
        private const int ChunkSize = 32;

        /// <summary>One refresh thread's buffers, kept for the whole refresh.</summary>
        private sealed class Worker
        {
            public readonly Scratch Scratch = new();
            public readonly BlockCache Cache = new();
            /// <summary>The parent block, cleared before each build (only its compact and blob forms are kept).</summary>
            public readonly BlockData Parent = new();
            public int Built;
            public long BuildTicks;
            public Exception Failure;
        }

        /// <summary>
        /// Rebuilds one coarse copy and stores it when it changed: a copy that comes out byte-identical keeps its version
        /// and adds nothing to <paramref name="next"/>. Thread-safe (the store, <paramref name="next"/> and
        /// <paramref name="changedCopies"/> are written under <see cref="sync"/>).
        /// </summary>
        private static void BuildCopy(uint zone, uint layer, uint level, ulong id, Dictionary<ulong, uint> versions, Worker w,
            HashSet<ulong> next, List<(ulong Cell, uint Version)> changedCopies)
        {
            // The captured live store already has its own copy of this cell (home continent): it is the one in the
            // manifest, so ours must not answer for it.
            if (zone == TerrainStore.HomeContinent && TerrainStore.Get(id) != null)
                return;
            uint childLevel = level - 1;
            bool anyChild = false;
            foreach (ulong child in Children(id))
            {
                if (childLevel == TerrainEdits.Level)
                {
                    if (!versions.ContainsKey(child))
                        continue;
                }
                else
                    lock (sync)
                    {
                        if (!Of(zone, layer).TryGetValue(child, out Copy copy) || copy.Empty)
                            continue;
                    }
                anyChild = true;
            }
            BlockData blocks = w.Parent;
            bool hasVoxels = false;
            if (anyChild)
            {
                Array.Clear(blocks.Flags);
                Array.Clear(blocks.Materials);
                Array.Clear(blocks.Offsets);
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                UpdateBlockLod(id, Grid(id, w.Cache), blocks, w.Scratch);
                w.BuildTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
                w.Built++;
                hasVoxels = Array.FindIndex(blocks.Flags, f => f != 0) >= 0;
            }
            byte[] saved = hasVoxels ? blocks.Save(System.IO.Compression.CompressionLevel.Fastest) : EmptySaved.Value;
            Copy old;
            lock (sync)
                Of(zone, layer).TryGetValue(id, out old);
            // Unchanged (a neighbour's edit that did not reach this copy, or nothing there before or now).
            if (old == null ? !hasVoxels : old.Saved.AsSpan().SequenceEqual(saved))
                return;
            byte[] blob = hasVoxels ? blocks.ToLayerBlob() : EmptyBlob.Value;
            lock (sync)
            {
                var store = Of(zone, layer);
                store.TryGetValue(id, out old);
                if (level < TopLevel(layer))
                    next.UnionWith(Readers(id));
                if (!hasVoxels)
                {
                    // Nothing left: an empty layer at a new version puts the generated ground back.
                    old.Blob = blob;
                    old.Saved = saved;
                    old.Empty = true;
                    old.Version = ContentVersion(saved, old.Version);
                    changedCopies.Add((id, old.Version));
                    return;
                }
                // From the content: the same bytes give the same version after any restart (VersionTag).
                uint version = ContentVersion(saved, old?.Version ?? 0);
                store[id] = new Copy { Blob = blob, Saved = saved, Version = version };
                changedCopies.Add((id, version));
            }
        }

        private static IEnumerable<ulong> Children(ulong id)
        {
            (uint level, uint x, uint y, uint z) = TerrainStore.Decode(id);
            for (uint d = 0; d < 8; d++)
                yield return TerrainStore.CellId(level - 1, x * 2 + (d & 1), y * 2 + (d >> 1 & 1), z * 2 + (d >> 2));
        }

        /// <summary>Interleaved bits of a cell's x, y, z (a Z-order curve), for the build order.</summary>
        private static ulong Morton(ulong id)
        {
            (_, uint x, uint y, uint z) = TerrainStore.Decode(id);
            ulong key = 0;
            for (int bit = 0; bit < 20; bit++)
                key |= ((ulong)(x >> bit & 1) << (3 * bit + 2)) | ((ulong)(z >> bit & 1) << (3 * bit + 1)) | ((ulong)(y >> bit & 1) << (3 * bit));
            return key;
        }

        /// <summary>
        /// A worker's child-level cells, the most recently used kept (<see cref="Capacity"/> × 384 KB, about 27 MB per worker
        /// while a refresh runs). The block of the cell dropped is reused for the next one read, so a refresh allocates its
        /// blocks once instead of 384 KB per cell read (a first version without reuse peaked at 9.7 GB on a 24,000-cell
        /// landscape: every block went to the large-object heap). Cells that do not exist cost one lookup and are not kept.
        /// </summary>
        private sealed class BlockCache
        {
            /// <summary>
            /// More than one parent's grid (64 cells): while a grid is gathered, the least recently used cell is never one
            /// that grid already holds, so a reused block is never one still in use.
            /// </summary>
            private const int Capacity = 72;
            private Func<ulong, bool> exists;
            private Func<ulong, BlockData, bool> fill;
            private readonly Dictionary<ulong, LinkedListNode<(ulong Id, BlockData Block)>> map = new();
            private readonly LinkedList<(ulong Id, BlockData Block)> order = new();
            private readonly Stack<BlockData> free = new();
            public int Loads;

            /// <summary>Forgets the cells (their blocks are kept for reuse) and reads from another level.</summary>
            public void Reset(Func<ulong, bool> exists, Func<ulong, BlockData, bool> fill)
            {
                this.exists = exists;
                this.fill = fill;
                foreach ((ulong _, BlockData block) in order)
                    free.Push(block);
                map.Clear();
                order.Clear();
            }

            public BlockData Get(ulong id)
            {
                if (map.TryGetValue(id, out var node))
                {
                    order.Remove(node);
                    order.AddFirst(node);
                    return node.Value.Block;
                }
                if (!exists(id))
                    return null;
                BlockData block;
                if (free.Count > 0)
                    block = free.Pop();
                else if (map.Count >= Capacity)
                {
                    var last = order.Last;
                    map.Remove(last.Value.Id);
                    order.RemoveLast();
                    block = last.Value.Block;
                }
                else
                    block = new BlockData();
                if (!fill(id, block))
                {
                    free.Push(block);
                    return null;
                }
                Loads++;
                map[id] = order.AddFirst((id, block));
                return block;
            }
        }

        /// <summary>
        /// The 4 × 4 × 4 child-level cells around a parent P: cells 2P − 1 .. 2P + 2 per axis, the children at grid
        /// 1 .. 2 (<see cref="GridIndex"/>). Null where a cell does not exist (also below coordinate 0, where the client's
        /// neighbour step FUN_142a75720 gives the invalid id DAT_145eb4710).
        /// </summary>
        private static BlockData[] Grid(ulong parentId, BlockCache source) => Grid(parentId, source.Get);

        /// <summary><see cref="Grid(ulong, BlockCache)"/> from any lookup (null = no such cell).</summary>
        internal static BlockData[] Grid(ulong parentId, Func<ulong, BlockData> cells)
        {
            (uint level, uint px, uint py, uint pz) = TerrainStore.Decode(parentId);
            var grid = new BlockData[64];
            for (int gz = 0; gz < 4; gz++)
                for (int gx = 0; gx < 4; gx++)
                    for (int gy = 0; gy < 4; gy++)
                    {
                        long cx = 2L * px - 1 + gx, cy = 2L * py - 1 + gy, cz = 2L * pz - 1 + gz;
                        if (cx >= 0 && cy >= 0 && cz >= 0)
                            grid[GridIndex(gx, gy, gz)] = cells(TerrainStore.CellId(level - 1, (uint)cx, (uint)cy, (uint)cz));
                    }
            return grid;
        }

        /// <summary>The grid's layout: cell (gx, gy, gz), each 0 .. 3, at (gz · 4 + gx) · 4 + gy (y fastest, like voxels).</summary>
        internal static int GridIndex(int gx, int gy, int gz) => (gz * 4 + gx) * 4 + gy;

        /// <summary>
        /// Single-child form, for tests: <paramref name="parent"/> gets what updateBlockLOD writes from a store that holds
        /// only <paramref name="child"/> (no neighbours).
        /// </summary>
        internal static void Downsample(ulong childId, BlockData child, BlockData parent)
        {
            (_, uint cx, uint cy, uint cz) = TerrainStore.Decode(childId);
            var grid = new BlockData[64];
            grid[GridIndex(1 + (int)(cx & 1), 1 + (int)(cy & 1), 1 + (int)(cz & 1))] = child;
            UpdateBlockLod(Parent(childId), grid, parent, new Scratch());
        }

        // ---------------------------------------------------------------------------------------------------------------
        // VoxelFarm::updateBlockLOD, ported from the 0.695 client (FUN_142aadbb0, re/decomp_all_695/part_0052.c:33270-34375;
        // profiler scope "VoxelFarm::updateBlockLOD", BlockData.cpp line 0x951; 0.589 FUN_142a15480, identical). Its
        // helpers: the cube's dual-contouring quads FUN_142aa7820 (:29551), the thin-material test FUN_142aa9a00 (:30918)
        // with FUN_142aa9830 (:30846), the neighbour step FUN_142a75720 (part_0051.c:176396), the QEF VoxelFarmQef.
        // Notes and the full trace: trace/0695/lod-qef.md.
        // ---------------------------------------------------------------------------------------------------------------

        /// <summary>A child voxel's "material" when its bit 0 is clear, and the parent's when none is set: 0xFFFE [P part_0052.c:33388].</summary>
        private const uint NoMaterial = 0xFFFE;

        /// <summary>
        /// Per worker scratch for <see cref="UpdateBlockLod"/>: the hasVertex mask (acStack_1f4e8, 64,000 bytes) and the
        /// triangle buffers (cube triangles malloc(0x360), the parent's malloc(0x1b00) = 192 triangles), the QEF rows and
        /// the solver's workspace.
        /// </summary>
        internal sealed class Scratch
        {
            public readonly bool[] HasVertex = new bool[BlockData.Count];
            public readonly float[] CubeTris = new float[12 * 9];
            public readonly float[] Tris = new float[192 * 9];
            public readonly double[] A = new double[VoxelFarmQef.MaxRows * 3], B = new double[VoxelFarmQef.MaxRows];
            /// <summary>
            /// The first solve's and the corner re-solve's buffers, two as in the client (auStack_200e8 and auStack_1fae8
            /// in updateBlockLOD's frame, part_0052.c:33454-33455). A 2-row solve (one quad) reads U[2][2] without writing
            /// it (FUN_142aa3140 takes the third diagonal value from U + 0x40), so each solve sees what its own buffer held.
            /// </summary>
            public readonly VoxelFarmQef.Workspace Qef = new(), QefCorner = new();

            /// <summary>
            /// The start of one parent: rows 1 and 2 of both U buffers to 0. The client's buffers are uninitialised stack at
            /// the start of each call; a fixed value makes a parent independent of what the worker built before it (the
            /// thread and chunk split, and the unchanged-copy compare). 0 gives the minimum-norm answer of a 2-row system.
            /// </summary>
            public void BeginParent()
            {
                Array.Clear(Qef.U, 3, 6);
                Array.Clear(QefCorner.U, 3, 6);
            }
        }

        /// <summary>g / 40 and g % 40 for a grid voxel coordinate g in 0 .. 159.</summary>
        private static readonly int[] CellOf40 = Enumerable.Range(0, 160).Select(g => g / 40).ToArray();
        private static readonly int[] LocalOf40 = Enumerable.Range(0, 160).Select(g => g % 40).ToArray();

        /// <summary>
        /// The grid cell holding grid voxel (gx, gy, gz) and the voxel's index in it. Every read stays inside the grid:
        /// children are grid voxels 40 .. 119 and nothing reads more than 2 below or 3 above a child voxel.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static BlockData At(BlockData[] grid, int gx, int gy, int gz, out int index)
        {
            index = (LocalOf40[gz] * BlockData.Size + LocalOf40[gx]) * BlockData.Size + LocalOf40[gy];
            return grid[(CellOf40[gz] * 4 + CellOf40[gx]) * 4 + CellOf40[gy]];
        }

        /// <summary>
        /// A stored material as the LOD rule reads it. [server] Interior empty space (0xFFFC) is air to the client's mesher
        /// but solid to its occluder; it is never stored any more (TerrainEdits.RepairInteriorAir, Write) and is read as
        /// air here so a stray one can never reach a coarse copy as a material. VoxelFarm itself has no such rule.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Material(ushort stored) => stored == VoxelTemplate.InteriorAir ? 0u : stored;

        /// <summary>Material of a voxel with bit 0, else <paramref name="otherwise"/> (also where the cell is missing).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint MaterialAt(BlockData[] grid, int gx, int gy, int gz, uint otherwise)
        {
            BlockData block = At(grid, gx, gy, gz, out int i);
            return block != null && (block.Flags[i] & 1) != 0 ? Material(block.Materials[i]) : otherwise;
        }

        /// <summary>
        /// An offset byte back to voxel units the way the client computes it: byte · (1 / 85) − 1, the reciprocal
        /// computed first (DAT_144c41178 / DAT_144c401b8 at part_0052.c:433, :899; FUN_142aa7820). byte / 85 − 1 can differ
        /// in the last bit.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double OffsetValue(byte b) => b * (1.0 / 85.0) - 1.0;

        /// <summary>
        /// Clamp to [−1, 2], then floor((u + 1) · 85 + 0.5) (cvttsd2si with the sign fix; the value is never negative here).
        /// Same as TerrainEdits.OffsetByte. Byte order x, y, z.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte Quantize(double u)
        {
            if (u < -1.0)
                u = -1.0;
            else if (u > 2.0)
                u = 2.0;
            return (byte)Math.Floor((u - -1.0) * 85.0 + 0.5);
        }

        /// <summary>Edge corners of a cube (from, to), x y z, client table 0x144a60170 (12 × 6 ints) [P bytes read].</summary>
        private static readonly int[] EdgeTable =
        {
            0, 0, 0, 1, 0, 0,   1, 0, 0, 1, 0, 1,   0, 0, 1, 1, 0, 1,   0, 0, 0, 0, 0, 1,
            0, 1, 0, 1, 1, 0,   1, 1, 0, 1, 1, 1,   0, 1, 1, 1, 1, 1,   0, 1, 0, 0, 1, 1,
            0, 0, 0, 0, 1, 0,   1, 0, 0, 1, 1, 0,   1, 0, 1, 1, 1, 1,   0, 0, 1, 0, 1, 1,
        };

        /// <summary><see cref="EdgeTable"/>'s two ends as corner numbers x | y &lt;&lt; 1 | z &lt;&lt; 2.</summary>
        private static readonly int[] EdgeCorners = Enumerable.Range(0, 24)
            .Select(j => EdgeTable[j * 3] | EdgeTable[j * 3 + 1] << 1 | EdgeTable[j * 3 + 2] << 2).ToArray();

        /// <summary>A corner without bit 0 while the cube's default is not known yet (not a material value).</summary>
        private const uint Unset = 0xFFFFFFFF;

        /// <summary>
        /// The other three cubes around each edge (the cube itself is the fourth), x y z, client table 0x144a5ffc0
        /// (12 × 3 × 3 ints) [P bytes read].
        /// </summary>
        private static readonly int[] EdgeCubes =
        {
            0, 0, -1,  0, -1, -1,  0, -1, 0,      1, 0, 0,  1, -1, 0,  0, -1, 0,
            0, -1, 0,  0, -1, 1,  0, 0, 1,        0, -1, 0,  -1, -1, 0,  -1, 0, 0,
            0, 1, 0,  0, 1, -1,  0, 0, -1,        0, 1, 0,  1, 1, 0,  1, 0, 0,
            0, 0, 1,  0, 1, 1,  0, 1, 0,          -1, 0, 0,  -1, 1, 0,  0, 1, 0,
            -1, 0, 0,  -1, 0, -1,  0, 0, -1,      0, 0, -1,  1, 0, -1,  1, 0, 0,
            1, 0, 0,  1, 0, 1,  0, 0, 1,          0, 0, 1,  -1, 0, 1,  -1, 0, 0,
        };

        /// <summary>
        /// VoxelFarm::updateBlockLOD for a whole parent cell: the predicate always true and the box the whole cell, a full
        /// rebuild [I: the client has no call site, so how live's server called it (a dirty predicate and box after edits?)
        /// is not known]. Fills <paramref name="parent"/> (a cleared block) from the children in <paramref name="grid"/>
        /// (child level, 4 × 4 × 4 cells around it, <see cref="Grid(ulong, Func{ulong, BlockData})"/>). Returns false when no
        /// child exists (nothing written).
        /// <para>For each octant whose child exists (order oz, ox, oy) and each of its 20³ parent voxels P (order z, x, y),
        /// the 8 child voxels c = 2P + d (order dz, dx, dy) are visited [P part_0052.c:33631-33895]:</para>
        /// <list type="bullet">
        /// <item>The child at d = 0 gives the starting material (0xFFFE when its bit 0 is clear).</item>
        /// <item>A child with bit 1 (a vertex) adds the quads of its cube's sign-changing edges (<see cref="CubeTriangles"/>)
        /// as two triangles each, fanned from its own vertex, in parent units: v = ((byte · 1/85 − 1) + d) / 2.</item>
        /// <item>The parent's vertex comes from one "best" child while none is chosen: the last child whose code is highest
        /// (bit 0, 2 for a vertex, 6 / 7 for a vertex-only / non-thin child with quads). A solid child with quads is
        /// <i>chosen</i> when its material is thin (<see cref="Thick"/> false) or a child was chosen before; the choice
        /// must pass <see cref="Consistent"/> once one consistent choice was made. A choice sets the parent's material.</item>
        /// <item>So the parent gets a vertex whenever any child has bit 1, and bit 0 whenever the chosen or corner child has
        /// a material: vertex-only parents exist (the corner/mass-point rule wrote a parent only where the corner was solid,
        /// which is what made distant shapes stairs).</item>
        /// <item>The vertex is a QEF of all those triangles' planes (1 to 63 triangles; <see cref="SolveVertex"/>), or the
        /// best child's vertex.</item>
        /// <item>A chosen parent also rewrites the vertices of its 7 lower neighbours from the chosen child's neighbours
        /// (<see cref="WriteNeighbours"/>).</item>
        /// </list>
        /// </summary>
        internal static bool UpdateBlockLod(ulong parentId, BlockData[] grid, BlockData parent, Scratch s)
        {
            // Level 2 has no children to build from (part_0052.c:33492, "if (uVar25 != 2)").
            if (TerrainStore.Decode(parentId).Level == TerrainEdits.Level)
                return false;
            s.BeginParent();
            bool[] hasVertex = s.HasVertex;
            Array.Clear(hasVertex);
            bool any = false;
            for (int oz = 0; oz < 2; oz++)
                for (int ox = 0; ox < 2; ox++)
                    for (int oy = 0; oy < 2; oy++)
                    {
                        BlockData child = grid[GridIndex(1 + ox, 1 + oy, 1 + oz)];
                        if (child == null)
                            continue;
                        any = true;
                        // Grid voxel of the child's voxel 0.
                        int gx0 = 40 * (1 + ox), gy0 = 40 * (1 + oy), gz0 = 40 * (1 + oz);
                        for (int lz = 0; lz < 20; lz++)
                            for (int lx = 0; lx < 20; lx++)
                                for (int ly = 0; ly < 20; ly++)
                                    BuildVoxel(grid, child, parent, s, gx0, gy0, gz0, 20 * ox + lx, 20 * oy + ly, 20 * oz + lz, lx, ly, lz);
                    }
            return any;
        }

        /// <summary>One parent voxel (px, py, pz) of the octant whose child is <paramref name="child"/> (local lx, ly, lz).</summary>
        private static void BuildVoxel(BlockData[] grid, BlockData child, BlockData parent, Scratch s,
            int gx0, int gy0, int gz0, int px, int py, int pz, int lx, int ly, int lz)
        {
            int p = (pz * BlockData.Size + px) * BlockData.Size + py;
            int best = 0;                       // uStack_20b48
            uint material = NoMaterial;         // uStack_20bc4
            bool chosen = false;                // cStack_20bd8
            bool consistentChosen = false;      // cStack_20bd5
            double bestX = 0, bestY = 0, bestZ = 0;     // the best child's vertex (parent units)
            int bestCx = 0, bestCy = 0, bestCz = 0;     // the best child (child-local voxel)
            int vertices = 0;                   // iStack_20bd0: triangles × 3 in s.Tris
            float[] tris = s.Tris;

            for (int dz = 0; dz < 2; dz++)
                for (int dx = 0; dx < 2; dx++)
                    for (int dy = 0; dy < 2; dy++)
                    {
                        int cx = 2 * lx + dx, cy = 2 * ly + dy, cz = 2 * lz + dz;
                        int ci = (cz * BlockData.Size + cx) * BlockData.Size + cy;
                        byte f = child.Flags[ci];
                        uint m = (f & 1) != 0 ? Material(child.Materials[ci]) : NoMaterial;
                        if (dx == 0 && dy == 0 && dz == 0)
                            material = m;
                        int code = f & 1;
                        double vx = 0.5, vy = 0.5, vz = 0.5;
                        if ((f & 2) != 0)
                        {
                            code |= 2;
                            int n = CubeTriangles(grid, child, gx0, gy0, gz0, cx, cy, cz, s.CubeTris);
                            if (n >= 1)
                            {
                                // The child's vertex in parent units from the parent's corner (part_0052.c:433-448).
                                vx = OffsetValue(child.Offsets[ci * 3]) * 0.5 + dx * 0.5;
                                vy = OffsetValue(child.Offsets[ci * 3 + 1]) * 0.5 + dy * 0.5;
                                vz = OffsetValue(child.Offsets[ci * 3 + 2]) * 0.5 + dz * 0.5;
                                AppendFan(tris, ref vertices, s.CubeTris, n, vx, vy, vz, dx, dy, dz);
                                bool candidate;
                                if (m != NoMaterial)
                                {
                                    // A solid child is a candidate when a child was chosen already, else when its material
                                    // is thin (FUN_142aa9a00 returns 0).
                                    candidate = chosen || !Thick(grid, gx0 + cx, gy0 + cy, gz0 + cz, m);
                                    if (!candidate)
                                        code = (f & 1) | 6;
                                }
                                else
                                {
                                    // A vertex-only child after a choice is skipped (LAB_142aae5cc → LAB_142aae827).
                                    if (chosen)
                                        continue;
                                    candidate = false;
                                    code = 6;
                                }
                                if (candidate)
                                {
                                    bool consistent = Consistent(grid, parent, s.HasVertex, gx0 + cx, gy0 + cy, gz0 + cz, px, py, pz);
                                    if (consistentChosen && !consistent)
                                        continue;
                                    if (consistent)
                                        consistentChosen = true;
                                    // LAB_142aae83e / LAB_142aae86e: the choice.
                                    material = m;
                                    chosen = true;
                                    best = (f & 1) | 2;
                                    (bestX, bestY, bestZ) = (vx, vy, vz);
                                    (bestCx, bestCy, bestCz) = (cx, cy, cz);
                                    continue;
                                }
                            }
                        }
                        // Record if nothing is chosen and this one's code is at least the best's (later ties win).
                        if (!chosen && best <= code)
                        {
                            best = code;
                            (bestX, bestY, bestZ) = (vx, vy, vz);
                            (bestCx, bestCy, bestCz) = (cx, cy, cz);
                        }
                    }

            double ux = bestX, uy = bestY, uz = bestZ;
            int rows = vertices / 3;
            if (vertices > 0 && rows < VoxelFarmQef.MaxRows)
                SolveVertex(s, rows, ref ux, ref uy, ref uz);

            s.HasVertex[p] = chosen;
            if (chosen)
                WriteNeighbours(grid, parent, s.HasVertex, consistentChosen, gx0, gy0, gz0, bestCx, bestCy, bestCz, px, py, pz);

            // The voxel's own write (part_0052.c:34307-34375; the predicate is true, so it always happens).
            if (material == NoMaterial)
            {
                parent.Materials[p] = 0;
                parent.Flags[p] &= 0xFE;
            }
            else
            {
                parent.Materials[p] = (ushort)material;
                parent.Flags[p] |= 1;
            }
            if ((best & 2) == 0)
            {
                parent.Offsets[p * 3] = parent.Offsets[p * 3 + 1] = parent.Offsets[p * 3 + 2] = 0;
                parent.Flags[p] &= 0xFD;
            }
            else
            {
                parent.Offsets[p * 3] = Quantize(ux);
                parent.Offsets[p * 3 + 1] = Quantize(uy);
                parent.Offsets[p * 3 + 2] = Quantize(uz);
                parent.Flags[p] |= 2;
            }
        }

        /// <summary>
        /// Appends a child's cube triangles (t0, t1, t2) as the two triangles (v, s(t0), s(t1)) and (v, s(t1), s(t2)),
        /// s(t) = t / 2 + d / 2 in float through double (part_0052.c:449-486). v is the child's own vertex.
        /// </summary>
        private static void AppendFan(float[] tris, ref int vertices, float[] cube, int n, double vx, double vy, double vz, int dx, int dy, int dz)
        {
            float fx = (float)vx, fy = (float)vy, fz = (float)vz;
            double hx = dx * 0.5, hy = dy * 0.5, hz = dz * 0.5;
            for (int t = 0; t < n; t += 3)
            {
                int c0 = t * 3, o = vertices * 3;
                for (int half = 0; half < 2; half++)
                {
                    int a = c0 + half * 3, b = c0 + half * 3 + 3;    // (t0, t1), then (t1, t2)
                    tris[o] = fx;
                    tris[o + 1] = fy;
                    tris[o + 2] = fz;
                    tris[o + 3] = (float)((double)(cube[a] * 0.5f) + hx);
                    tris[o + 4] = (float)((double)(cube[a + 1] * 0.5f) + hy);
                    tris[o + 5] = (float)((double)(cube[a + 2] * 0.5f) + hz);
                    tris[o + 6] = (float)((double)(cube[b] * 0.5f) + hx);
                    tris[o + 7] = (float)((double)(cube[b + 1] * 0.5f) + hy);
                    tris[o + 8] = (float)((double)(cube[b + 2] * 0.5f) + hz);
                    o += 9;
                }
                vertices += 6;
            }
        }

        /// <summary>
        /// FUN_142aa7820 (called with param_8 = 1): the dual-contouring quads around a child cube c's 12 edges, each as one
        /// triangle (V0, V1, V2) of the three other cubes' vertices (cube offset + its vertex, 0.5 without bit 1), in child
        /// units from c. Returns the vertex count (3 per triangle). An edge counts when its two ends' materials differ and
        /// one of them is the cube's default: 0 when one of the cube's 8 voxels inside c's own cell (no wrap) is a non-zero
        /// material, else 0xFFFE; a voxel without bit 0 (or a missing cell) has the default. A quad needs all three cubes'
        /// cells to exist. The winding flips (V0, V2, V1) when the first end is the default.
        /// </summary>
        private static int CubeTriangles(BlockData[] grid, BlockData child, int gx0, int gy0, int gz0, int cx, int cy, int cz, float[] output)
        {
            int gx = gx0 + cx, gy = gy0 + cy, gz = gz0 + cz;
            // The cube's 8 corners, read once (corner k = x | y << 1 | z << 2): the client reads the in-cell ones for the
            // default and each edge's two ends again (24 reads); the values are the same.
            Span<uint> corner = stackalloc uint[8];
            uint def = NoMaterial;
            if (cx < BlockData.Size - 1 && cy < BlockData.Size - 1 && cz < BlockData.Size - 1)
            {
                // The whole cube inside c's own cell (most cubes): read the child directly.
                byte[] flags = child.Flags;
                ushort[] materials = child.Materials;
                for (int k = 0; k < 8; k++)
                {
                    int i = ((cz + (k >> 2)) * BlockData.Size + cx + (k & 1)) * BlockData.Size + cy + (k >> 1 & 1);
                    if ((flags[i] & 1) == 0)
                        corner[k] = Unset;
                    else if ((corner[k] = Material(materials[i])) != 0)
                        def = 0;
                }
            }
            else
                for (int k = 0; k < 8; k++)
                {
                    int dx = k & 1, dy = k >> 1 & 1, dz = k >> 2;
                    BlockData block = At(grid, gx + dx, gy + dy, gz + dz, out int i);
                    if (block == null || (block.Flags[i] & 1) == 0)
                    {
                        corner[k] = Unset;
                        continue;
                    }
                    uint m = Material(block.Materials[i]);
                    corner[k] = m;
                    // The default looks only at corners inside c's own cell (FUN_142aa7820's "< 0x28" tests, no wrap).
                    if (m != 0 && cx + dx < BlockData.Size && cy + dy < BlockData.Size && cz + dz < BlockData.Size)
                        def = 0;
                }
            bool uniform = true;
            for (int k = 0; k < 8; k++)
            {
                if (corner[k] == Unset)
                    corner[k] = def;
                uniform &= corner[k] == corner[0];
            }
            // No edge can change material: no quads (an inside or outside cube).
            if (uniform)
                return 0;

            int count = 0;
            Span<double> v = stackalloc double[9];
            for (int e = 0; e < 12; e++)
            {
                uint ma = corner[EdgeCorners[e * 2]], mb = corner[EdgeCorners[e * 2 + 1]];
                if (ma == mb || (ma != def && mb != def))
                    continue;
                int found = 0;
                for (int j = 0; j < 3; j++)
                {
                    int q = e * 9 + j * 3;
                    int ox = EdgeCubes[q], oy = EdgeCubes[q + 1], oz = EdgeCubes[q + 2];
                    BlockData block = At(grid, gx + ox, gy + oy, gz + oz, out int i);
                    if (block == null)
                        continue;
                    double ux = 0.5, uy = 0.5, uz = 0.5;
                    if ((block.Flags[i] & 2) != 0)
                    {
                        ux = OffsetValue(block.Offsets[i * 3]);
                        uy = OffsetValue(block.Offsets[i * 3 + 1]);
                        uz = OffsetValue(block.Offsets[i * 3 + 2]);
                    }
                    v[j * 3] = ox + ux;
                    v[j * 3 + 1] = oy + uy;
                    v[j * 3 + 2] = oz + uz;
                    found++;
                }
                if (found != 3)
                    continue;
                int o = count * 3;
                int second = ma == def ? 2 : 1, third = ma == def ? 1 : 2;
                output[o] = (float)v[0];
                output[o + 1] = (float)v[1];
                output[o + 2] = (float)v[2];
                output[o + 3] = (float)v[second * 3];
                output[o + 4] = (float)v[second * 3 + 1];
                output[o + 5] = (float)v[second * 3 + 2];
                output[o + 6] = (float)v[third * 3];
                output[o + 7] = (float)v[third * 3 + 1];
                output[o + 8] = (float)v[third * 3 + 2];
                count += 3;
            }
            return count;
        }

        /// <summary>
        /// FUN_142aa9a00 returning 1: some voxel q within ±2 of child voxel (gx, gy, gz) has the material
        /// <paramref name="m"/> (unset counts as 0) and a cube q .. q + 1 without an air / solid change
        /// (<see cref="CubeHasChange"/> false). A material with no such interior cube nearby is "thin", and its child can
        /// be chosen.
        /// </summary>
        private static bool Thick(BlockData[] grid, int gx, int gy, int gz, uint m)
        {
            for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                    for (int dy = -2; dy <= 2; dy++)
                        if (MaterialAt(grid, gx + dx, gy + dy, gz + dz, 0) == m && !CubeHasChange(grid, gx + dx, gy + dy, gz + dz, m))
                            return true;
            return false;
        }

        /// <summary>
        /// FUN_142aa9830 returning 1: one of the cube's 8 voxels has another material and one of the two is 0 (air or
        /// unset).
        /// </summary>
        private static bool CubeHasChange(BlockData[] grid, int gx, int gy, int gz, uint m)
        {
            for (int z = gz; z <= gz + 1; z++)
                for (int x = gx; x <= gx + 1; x++)
                    for (int y = gy; y <= gy + 1; y++)
                    {
                        uint w = MaterialAt(grid, x, y, z, 0);
                        if (w != m && (w == 0 || m == 0))
                            return true;
                    }
            return false;
        }

        /// <summary>
        /// The candidate's check (part_0052.c:33781-33861): for the 7 lower neighbours o ∈ {−1, 0}³ \ 0 inside the cell
        /// (order oz, ox, oy) whose child cell exists, a parent voxel P + o that already has a vertex must have the same
        /// bit 0 as the child voxel c + o.
        /// </summary>
        private static bool Consistent(BlockData[] grid, BlockData parent, bool[] hasVertex, int gx, int gy, int gz, int px, int py, int pz)
        {
            for (int oz = -1; oz <= 0; oz++)
            {
                if (pz + oz < 0)
                    continue;
                for (int ox = -1; ox <= 0; ox++)
                {
                    if (px + ox < 0)
                        continue;
                    for (int oy = -1; oy <= 0; oy++)
                    {
                        if ((ox | oy | oz) == 0 || py + oy < 0)
                            continue;
                        BlockData block = At(grid, gx + ox, gy + oy, gz + oz, out int i);
                        if (block == null)
                            continue;
                        int q = ((pz + oz) * BlockData.Size + px + ox) * BlockData.Size + py + oy;
                        if (hasVertex[q] && (parent.Flags[q] & 1) != (block.Flags[i] & 1))
                            return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// After a choice (part_0052.c:34107-34305): each lower neighbour P + o (o ∈ {−1, 0}³ \ 0, inside the cell) that
        /// has no vertex yet, or every one when the choice was consistent, is marked as having one and gets its vertex
        /// from the chosen child's neighbour n = c + o (its offset u with bit 1, else 0.5): per axis (u + (c and 1)) / 2
        /// for o = 0; for o = −1, (u + 1) / 2 when c is even and 1.0 when it is odd. Only the offset and bit 1 are written.
        /// </summary>
        private static void WriteNeighbours(BlockData[] grid, BlockData parent, bool[] hasVertex, bool consistent,
            int gx0, int gy0, int gz0, int cx, int cy, int cz, int px, int py, int pz)
        {
            for (int oz = -1; oz <= 0; oz++)
            {
                if (pz + oz < 0)
                    continue;
                for (int ox = -1; ox <= 0; ox++)
                {
                    if (px + ox < 0)
                        continue;
                    for (int oy = -1; oy <= 0; oy++)
                    {
                        if ((ox | oy | oz) == 0 || py + oy < 0)
                            continue;
                        int q = ((pz + oz) * BlockData.Size + px + ox) * BlockData.Size + py + oy;
                        if (!consistent && hasVertex[q])
                            continue;
                        hasVertex[q] = true;
                        BlockData block = At(grid, gx0 + cx + ox, gy0 + cy + oy, gz0 + cz + oz, out int i);
                        double ux = 0.5, uy = 0.5, uz = 0.5;
                        if (block != null && (block.Flags[i] & 2) != 0)
                        {
                            ux = OffsetValue(block.Offsets[i * 3]);
                            uy = OffsetValue(block.Offsets[i * 3 + 1]);
                            uz = OffsetValue(block.Offsets[i * 3 + 2]);
                        }
                        parent.Offsets[q * 3] = Quantize(NeighbourAxis(ox, cx, ux));
                        parent.Offsets[q * 3 + 1] = Quantize(NeighbourAxis(oy, cy, uy));
                        parent.Offsets[q * 3 + 2] = Quantize(NeighbourAxis(oz, cz, uz));
                        parent.Flags[q] |= 2;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double NeighbourAxis(int o, int c, double u)
            => o == -1 ? ((c & 1) == 0 ? (u + 1.0) * 0.5 : 1.0) : (u + (c & 1)) * 0.5;

        /// <summary>
        /// The QEF vertex (part_0052.c:33904-34105): one row per triangle, its unit normal (float cross product of
        /// (v1 − v0) and (v2 − v0), normalised when longer than 1e-7) and b = n · (v0 − 0.5) in float; x = the SVD solve;
        /// p = x + 0.5. A component in [−0.7, 0) or (1, 1.3] re-solves about the nearest cube corner (0 or 1 per axis,
        /// b = n · (v0 − corner)), p = corner + x, unclamped; otherwise each component is clamped to [0, 1]. Then to [−1, 2].
        /// </summary>
        internal static void SolveVertex(Scratch s, int rows, ref double px, ref double py, ref double pz)
        {
            float[] t = s.Tris;
            double[] a = s.A, b = s.B;
            for (int r = 0; r < rows; r++)
            {
                int o = r * 9;
                float x0 = t[o], y0 = t[o + 1], z0 = t[o + 2];
                float e1x = t[o + 3] - x0, e1y = t[o + 4] - y0, e1z = t[o + 5] - z0;
                float e2x = t[o + 6] - x0, e2y = t[o + 7] - y0, e2z = t[o + 8] - z0;
                float nx = e2z * e1y - e1z * e2y;
                float ny = e2x * e1z - e2z * e1x;
                float nz = e1x * e2y - e2x * e1y;
                float length = MathF.Sqrt(ny * ny + nx * nx + nz * nz);   // ((ny² + nx²) + nz²) [P asm 0x142aaea44]
                if (length > 1e-7f)
                {
                    float inverse = 1.0f / length;
                    nx = inverse * nx;
                    ny = ny * inverse;
                    nz = nz * inverse;
                }
                a[r * 3] = nx;
                a[r * 3 + 1] = ny;
                a[r * 3 + 2] = nz;
            }
            for (int r = 0; r < rows; r++)
            {
                int o = r * 9;
                b[r] = (float)a[r * 3] * (t[o] - 0.5f) + (float)a[r * 3 + 1] * (t[o + 1] - 0.5f) + (float)a[r * 3 + 2] * (t[o + 2] - 0.5f);
            }
            VoxelFarmQef.Solve(a, b, rows, s.Qef);
            double qx = s.Qef.X[0] + 0.5, qy = s.Qef.X[1] + 0.5, qz = s.Qef.X[2] + 0.5;
            if (NearOutside(qx) || NearOutside(qy) || NearOutside(qz))
            {
                float cx = qx <= 0.5 ? 0.0f : 1.0f, cy = qy <= 0.5 ? 0.0f : 1.0f, cz = qz <= 0.5 ? 0.0f : 1.0f;
                for (int r = 0; r < rows; r++)
                {
                    int o = r * 9;
                    b[r] = (float)a[r * 3] * (t[o] - cx) + (float)a[r * 3 + 1] * (t[o + 1] - cy) + (float)a[r * 3 + 2] * (t[o + 2] - cz);
                }
                VoxelFarmQef.Solve(a, b, rows, s.QefCorner);
                qx = cx + s.QefCorner.X[0];
                qy = cy + s.QefCorner.X[1];
                qz = cz + s.QefCorner.X[2];
            }
            else
            {
                qx = Clamp01(qx);
                qy = Clamp01(qy);
                qz = Clamp01(qz);
            }
            px = ClampWide(qx);
            py = ClampWide(qy);
            pz = ClampWide(qz);
        }

        /// <summary>Just outside the cube: [−0.7, 0) or (1, 1.3] (DAT_144c412e8 = −0.7, DAT_144c41190 = 1.3).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool NearOutside(double p) => (p < 0.0 && -0.7 <= p) || (1.0 < p && p <= 1.3);

        /// <summary>part_0052.c:34066-34081 (a NaN becomes 1, as there).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Clamp01(double p) => p < 0.0 ? 0.0 : p <= 1.0 ? p : 1.0;

        /// <summary>part_0052.c:34082-34097 (a NaN becomes 2, as there).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double ClampWide(double p) => p < -1.0 ? -1.0 : p <= 2.0 ? p : 2.0;
    }
}
