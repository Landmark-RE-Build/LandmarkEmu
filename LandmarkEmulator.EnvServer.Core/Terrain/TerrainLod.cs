using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// Coarser copies of player edits. The client draws far terrain from coarser cells (level L+1 = twice the edge,
    /// half the voxel resolution) that it generates itself unless the manifest lists a server copy. The captured live
    /// manifest has layers 0 and 1 at every level from 2 to 10 and layer 2 at levels 2-4; our edits exist only at
    /// level 2, so wherever the client switched to a coarser level (moving, looking further) the original ground came
    /// back: walls appeared only up close, dug terrain floated in the air (2026-10-13). Each edited level-2 cell layer
    /// gets copies up the levels (layers 0/1 to <see cref="MaxLevel"/>, layer 2 to <see cref="MaxDigLevel"/>), made
    /// like VoxelFarm's octree simplification (see <see cref="Downsample"/>). Copies are block layers like the
    /// overlays (CellsUpdate version 0), listed in the manifest under their layer with a version that changes
    /// whenever a child changes, so clients fetch one only when they draw it.
    /// <para>Built for every landscape at start, then kept up to date in the background: once a layer's edits have
    /// been quiet for <see cref="Quiet"/>, only the copies above the changed cells are rebuilt and the players there
    /// get a manifest note per changed copy (21 bytes). Waiting for quiet keeps fetches away from cells being edited
    /// (a pending fetch makes the client refuse new edits).</para>
    /// </summary>
    public static class TerrainLod
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Serve the copies (WorldServer.json TerrainLodCopies). The first version (majority vote, centred vertices)
        /// drew cubes and blank patches and was switched off; this one follows VoxelFarm's rules.
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

        /// <summary>Copies of this downsample get versions in their own range (cached older copies differ).</summary>
        private const uint VersionTag = 0x20000000;

        private class Copy
        {
            public byte[] Blob;      // CellsUpdate layer blob
            public byte[] Saved;     // BlockData.Save form, to rebuild the level above
            public uint Version;
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

        private static IEnumerable<ulong> Children(ulong id)
        {
            (uint level, uint x, uint y, uint z) = TerrainStore.Decode(id);
            for (uint d = 0; d < 8; d++)
                yield return TerrainStore.CellId(level - 1, x * 2 + (d & 1), y * 2 + (d >> 1 & 1), z * 2 + (d >> 2));
        }

        /// <summary>Builds the copies of every landscape with edits (server start, in the background).</summary>
        public static void BuildAll()
        {
            if (!Enabled)
                return;
            running = Task.Run(() =>
            {
                foreach (uint zone in TerrainEdits.Zones())
                    for (uint layer = 0; layer < TerrainEdits.LayerCount; layer++)
                        Refresh(zone, layer, all: true);
            });
        }

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
                        if ((from == null ? versions.Count > 0 : !Same(from, versions)) && now - look.Moved >= Quiet)
                            due.Add((zone, layer));
                    }
                }
            if (due.Count > 0)
                running = Task.Run(() =>
                {
                    foreach ((uint zone, uint layer) in due)
                        Refresh(zone, layer, all: false);
                });
        }

        /// <summary>Dev "lodbuild": refreshes a landscape now (changed copies only, players told).</summary>
        public static void RefreshNow(uint zone)
        {
            for (uint layer = 0; layer < TerrainEdits.LayerCount; layer++)
                Refresh(zone, layer, all: false);
        }

        /// <summary>
        /// Rebuilds the copies above the level-2 cells of one layer that changed since the last build (all of them
        /// with <paramref name="all"/>) and tells the players on the landscape which copies changed.
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
                        dirty.Add(Parent(id));
                foreach (ulong id in before.Keys)
                    if (!versions.ContainsKey(id))
                        dirty.Add(Parent(id));

                var changedCopies = new List<(ulong Cell, uint Version)>();
                for (uint level = 3; level <= TopLevel(layer) && dirty.Count > 0; level++)
                {
                    var next = new HashSet<ulong>();
                    foreach (ulong id in dirty)
                    {
                        if (level < TopLevel(layer))
                            next.Add(Parent(id));
                        // The captured live store already has its own copy of this cell (home continent): it is the
                        // one in the manifest, so ours must not answer for it.
                        if (zone == TerrainStore.HomeContinent && TerrainStore.Get(id) != null)
                            continue;
                        var blocks = new BlockData();
                        uint version = 0;
                        bool any = false;
                        foreach (ulong child in Children(id))
                        {
                            if (level == 3)
                            {
                                if (!versions.TryGetValue(child, out uint childVersion))
                                    continue;
                                TerrainEdits.WithCell(zone, child, layer, cell => Downsample(child, cell, blocks));
                                version += childVersion;
                                any = true;
                            }
                            else
                            {
                                Copy copy;
                                lock (sync)
                                    Of(zone, layer).TryGetValue(child, out copy);
                                if (copy == null)
                                    continue;
                                var cell = new BlockData();
                                cell.Load(copy.Saved);
                                Downsample(child, cell, blocks);
                                version += copy.Version;
                                any = true;
                            }
                        }
                        bool hasVoxels = false;
                        for (int i = 0; i < BlockData.Count && !hasVoxels && any; i++)
                            hasVoxels = blocks.Flags[i] != 0;
                        lock (sync)
                        {
                            var store = Of(zone, layer);
                            store.TryGetValue(id, out Copy old);
                            if (!hasVoxels)
                            {
                                if (old == null)
                                    continue;
                                // Nothing left: an empty layer at a new version puts the generated ground back.
                                old.Blob = new BlockData().ToLayerBlob();
                                old.Saved = new BlockData().Save(System.IO.Compression.CompressionLevel.Fastest);
                                old.Version++;
                                changedCopies.Add((id, old.Version));
                                continue;
                            }
                            // Never reuse a version a client may hold (the sum of children can repeat).
                            version |= VersionTag;
                            if (old != null && version <= old.Version)
                                version = old.Version + 1;
                            store[id] = new Copy
                            {
                                Blob = blocks.ToLayerBlob(),
                                Saved = blocks.Save(System.IO.Compression.CompressionLevel.Fastest),
                                Version = version
                            };
                            changedCopies.Add((id, version));
                        }
                    }
                    dirty = next;
                }
                lock (sync)
                    built[(zone, layer)] = versions;
                if (!all)
                    CopiesChanged?.Invoke(zone, layer, changedCopies);
                if (changedCopies.Count > 0)
                    log.Info($"Terrain LOD, landscape {zone} layer {layer}: {changedCopies.Count} coarse cell(s) updated ({watch.ElapsedMilliseconds} ms).");
            }
            catch (Exception e)
            {
                log.Error(e, $"Terrain LOD refresh failed for landscape {zone} layer {layer}.");
            }
        }

        /// <summary>
        /// Folds a child cell's voxels into the octant of its parent they cover, the way VoxelFarm's octree
        /// simplification does it (VoxelFarm::updateBlockLOD, FUN_142a15480, compiled into the client):
        /// <list type="bullet">
        /// <item>A parent grid point is the child grid point at the same place, so a parent voxel takes its solid/air
        /// state and material from the child at its corner (offset 0,0,0), set only where that child is set.</item>
        /// <item>Its surface vertex comes from the surface inside its 2×2×2 children: VoxelFarm solves a QEF over the
        /// children's triangles; on edited terrain that lands on the mass point of the children's vertices, which is
        /// what is used here (no vertex inside → centred).</item>
        /// </list>
        /// The earlier majority vote with centred vertices drew cubes and punched holes (2026-10-13).
        /// </summary>
        internal static void Downsample(ulong childId, BlockData child, BlockData parent)
        {
            (_, uint cx, uint cy, uint cz) = TerrainStore.Decode(childId);
            int ox = (int)(cx & 1) * 20, oy = (int)(cy & 1) * 20, oz = (int)(cz & 1) * 20;
            for (int pz = 0; pz < 20; pz++)
                for (int px = 0; px < 20; px++)
                    for (int py = 0; py < 20; py++)
                    {
                        int corner = TerrainEdits.VoxelIndex(px * 2, py * 2, pz * 2);
                        if ((child.Flags[corner] & 1) == 0)
                            continue;
                        // Mass point of the children's vertices, in parent-voxel units from the parent's corner.
                        double sx = 0, sy = 0, sz = 0;
                        int n = 0;
                        for (int d = 0; d < 8; d++)
                        {
                            int dx = d & 1, dy = d >> 1 & 1, dz = d >> 2;
                            int i = TerrainEdits.VoxelIndex(px * 2 + dx, py * 2 + dy, pz * 2 + dz);
                            if ((child.Flags[i] & 2) == 0)
                                continue;
                            sx += (dx + OffsetValue(child.Offsets[i * 3])) / 2;
                            sy += (dy + OffsetValue(child.Offsets[i * 3 + 1])) / 2;
                            sz += (dz + OffsetValue(child.Offsets[i * 3 + 2])) / 2;
                            n++;
                        }
                        int index = TerrainEdits.VoxelIndex(ox + px, oy + py, oz + pz);
                        // Interior empty space is air to the client's mesher but solid to its occluder builder: never copied
                        // as a material (TerrainEdits.RepairInteriorAir).
                        parent.SetSolid(index, child.Materials[corner] == VoxelTemplate.InteriorAir ? (ushort)0 : child.Materials[corner]);
                        if (n > 0)
                        {
                            parent.Offsets[index * 3] = TerrainEdits.OffsetByte(sx / n);
                            parent.Offsets[index * 3 + 1] = TerrainEdits.OffsetByte(sy / n);
                            parent.Offsets[index * 3 + 2] = TerrainEdits.OffsetByte(sz / n);
                        }
                    }
        }

        /// <summary>An offset byte back to voxel units (inverse of TerrainEdits.OffsetByte).</summary>
        private static double OffsetValue(byte b) => b / 85.0 - 1;
    }
}
