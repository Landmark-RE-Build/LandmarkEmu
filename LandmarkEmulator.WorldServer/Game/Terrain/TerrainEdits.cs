using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
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
        /// Every access happens under <see cref="sync"/>.
        /// </summary>
        private class Overlay
        {
            private BlockData blocks;
            private byte[] packed;

            /// <summary>The cell's voxels, expanded on first use since it was last compacted.</summary>
            public BlockData Blocks
            {
                get
                {
                    if (blocks == null)
                    {
                        blocks = new BlockData();
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

            /// <summary>A cell straight from its saved form (stays compact until used).</summary>
            public static Overlay FromSaved(byte[] saved) => new() { packed = saved };

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

        public static int Count
        {
            get { lock (sync) return stores.Values.Sum(s => s.Count); }
        }

        public static int VoxelIndex(int x, int y, int z) => y + BlockData.Size * (x + BlockData.Size * z);

        /// <summary>Loads saved overlays (one file per cell: packed SerializeBlockData + version).</summary>
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
            log.Info($"Loaded {Count} edited terrain cells on {stores.Count} landscape(s).");
        }

        private static void LoadZone(uint zone, string zoneFolder)
        {
            var overlays = Of(zone);
            foreach (string file in Directory.GetFiles(zoneFolder, "*.cell"))
            {
                try
                {
                    ulong id = Convert.ToUInt64(Path.GetFileNameWithoutExtension(file), 16);
                    byte[] bytes = File.ReadAllBytes(file);
                    var overlay = Overlay.FromSaved(bytes.AsSpan(4).ToArray());
                    overlay.Version = BitConverter.ToUInt32(bytes, 0);
                    // Vertex-only voxels: kept (clients send them with every smooth edit); stripped only when
                    // KeepVertexOnlyVoxels is off.
                    int stripped = 0;
                    for (int i = 0; i < BlockData.Count && !BlockData.KeepVertexOnly; i++)
                        if (overlay.Blocks.Flags[i] == 2)
                        {
                            overlay.Blocks.Flags[i] = 0;
                            stripped++;
                        }
                    // Hole shells (biome cave materials written outside dug spheres) stuck out of the ground where
                    // the height map is below the real surface (rocks); they are no longer written, and old ones go.
                    var shellMaterials = TerrainMaterials.AllCaveMaterials();
                    for (int i = 0; i < BlockData.Count; i++)
                        if ((overlay.Blocks.Flags[i] & 1) != 0 && shellMaterials.Contains(overlay.Blocks.Materials[i]))
                        {
                            overlay.Blocks.Flags[i] = 0;
                            overlay.Blocks.Materials[i] = 0;
                            stripped++;
                        }
                    if (stripped > 0)
                    {
                        overlay.Version++;
                        overlay.Dirty = true;
                        log.Info($"Cell {id:X16}: dropped {stripped} voxels without a material.");
                    }
                    overlay.LastEdit = File.GetLastWriteTimeUtc(file);
                    if (!overlay.Dirty)
                        overlay.Compact();
                    Of(zone)[id] = overlay;
                }
                catch (Exception e)
                {
                    log.Error(e, $"Could not load terrain overlay {file}.");
                }
            }
        }

        /// <summary>A cell idle this long is packed again; at most this many stay expanded (least recently used go first).</summary>
        private static readonly TimeSpan IdleBeforeCompact = TimeSpan.FromSeconds(30);
        private const int MaxExpanded = 64;
        private static DateTime nextCompact;

        /// <summary>
        /// Packs expanded cells that are idle (and saved), and the least recently used ones beyond
        /// <see cref="MaxExpanded"/>; every few seconds from the tick.
        /// </summary>
        private static void CompactIdle()
        {
            DateTime now = DateTime.UtcNow;
            if (now < nextCompact)
                return;
            nextCompact = now.AddSeconds(5);
            CompactNow(false);
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
                int expanded = all.Count(o => o.Expanded);
                long compact = all.Sum(o => (long)o.CompactBytes);
                return $"{all.Count} edited cells: {expanded} expanded (~{expanded * 384 / 1024} MB), " +
                    $"{all.Count - expanded} compact ({compact / 1024} KB)";
            }
        }

        /// <summary>Writes changed overlays to disk (called on the server tick, so bursts of edits coalesce).</summary>
        public static void Flush()
        {
            VoxelOwnership.Flush();
            CompactIdle();
            List<(uint Zone, ulong Id, byte[] Data)> pending = null;
            DateTime now = DateTime.UtcNow;
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
                                (pending ??= new()).Add((zone, id, null));
                                continue;
                            }
                            byte[] data = overlay.Blocks.Save();
                            var bytes = new byte[4 + data.Length];
                            BitConverter.GetBytes(overlay.Version).CopyTo(bytes, 0);
                            data.CopyTo(bytes, 4);
                            (pending ??= new()).Add((zone, id, bytes));
                        }
            if (pending == null || folder == null)
                return;
            foreach ((uint zone, ulong id, byte[] bytes) in pending)
            {
                string zoneFolder = FolderOf(zone);
                string path = Path.Combine(zoneFolder, $"{id:X16}.cell");
                if (bytes == null)
                {
                    File.Delete(path);
                    continue;
                }
                Directory.CreateDirectory(zoneFolder);
                File.WriteAllBytes(path, bytes);
            }
        }

        /// <summary>
        /// How long after its last change dug terrain outside build sites takes to grow back. Landmark wiki
        /// ("Gathering"): "The land automatically regenerates (heals) approximately 20 minutes after destruction";
        /// gathering happens on unclaimed land, build sites keep their changes.
        /// </summary>
        public static TimeSpan RegenerationDelay = TimeSpan.FromMinutes(20);
        private static readonly TimeSpan RegenerationCheck = TimeSpan.FromMinutes(1);
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
        /// World tick (checked once a minute): every edited cell not changed for <see cref="RegenerationDelay"/> drops
        /// its overrides outside build sites, so the generated ground (and its caves) shows again, and players in range
        /// get the cell. <paramref name="claimsIn"/> gives the build-site boxes (global dm) touching a box.
        /// </summary>
        public static void Regenerate(Func<uint, (double, double, double), (double, double, double), List<(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)>> claimsIn)
        {
            DateTime now = DateTime.UtcNow;
            if (now < nextRegeneration)
                return;
            nextRegeneration = now + RegenerationCheck;
            var regrown = new List<(uint Zone, ulong Id)>();
            lock (sync)
                foreach ((uint zone, var overlays) in stores)
                {
                    permanent.TryGetValue(zone, out var keep);
                    foreach ((ulong key, Overlay overlay) in overlays)
                    {
                        // Build-site building (layer 1) is permanent; digging (layer 2) and old layer-0 edits heal.
                        ulong id = CellOf(key);
                        if (LayerOf(key) == BuildLayer || overlay.Dirty || now - overlay.LastEdit < RegenerationDelay
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
            foreach (var zone in regrown.GroupBy(r => r.Zone))
                Broadcast(zone.Select(r => r.Id).ToList(), zone.Key);
            if (regrown.Count > 0)
                log.Info($"Terrain regenerated in {regrown.Count} cell(s).");
        }

        /// <summary>
        /// Sets every voxel whose centre lies within <paramref name="radius"/> (world units) of a global point to air
        /// (material 0) or to <paramref name="material"/>. Returns the cells changed and how many voxels changed.
        /// </summary>
        public static (List<ulong> Cells, int Voxels) Sphere(Vector3 local, double radius, ushort material, uint zone = 0,
            Dictionary<ushort, int> oreMined = null, HashSet<ulong> oreCells = null, uint layer = DigLayer)
        {
            (double gx, double gy, double gz) = ToGlobal(local);
            var cells = new HashSet<ulong>();
            var touched = new Dictionary<ulong, List<int>>();
            int changed = 0;
            double r2 = radius * radius;
            lock (sync)
            {
                for (double x = gx - radius; x <= gx + radius; x += VoxelSize)
                    for (double y = gy - radius; y <= gy + radius; y += VoxelSize)
                        for (double z = gz - radius; z <= gz + radius; z += VoxelSize)
                        {
                            long vx = (long)Math.Floor(x / VoxelSize), vy = (long)Math.Floor(y / VoxelSize),
                                vz = (long)Math.Floor(z / VoxelSize);
                            double cx = (vx + 0.5) * VoxelSize - gx, cy = (vy + 0.5) * VoxelSize - gy,
                                cz = (vz + 0.5) * VoxelSize - gz;
                            if (cx * cx + cy * cy + cz * cz > r2)
                                continue;
                            long cellX = Math.DivRem(vx, BlockData.Size, out long ix),
                                cellY = Math.DivRem(vy, BlockData.Size, out long iy),
                                cellZ = Math.DivRem(vz, BlockData.Size, out long iz);
                            if (ix < 0) { ix += BlockData.Size; cellX--; }
                            if (iy < 0) { iy += BlockData.Size; cellY--; }
                            if (iz < 0) { iz += BlockData.Size; cellZ--; }
                            ulong id = TerrainStore.CellId(Level, (uint)cellX, (uint)cellY, (uint)cellZ);
                            if (!Of(zone).TryGetValue(Key(id, layer), out Overlay overlay))
                                Of(zone)[Key(id, layer)] = overlay = new Overlay();
                            int index = VoxelIndex((int)ix, (int)iy, (int)iz);
                            if (overlay.Blocks.Flags[index] != 0 && overlay.Blocks.Materials[index] == material)
                                continue;
                            // Carving only touches ground: voxels clearly in open air (well above the generated surface)
                            // stay unset, so less is sent. Near the surface everything is carved explicitly: the baked
                            // height grid is coarse, and on slopes and mountains a skipped voxel the client still has as
                            // solid left fragments of the old surface floating over the hole (and hid the mining ring).
                            if (material == 0 && (overlay.Blocks.Flags[index] & 1) == 0 && AboveGround(vx, vy, vz, zone, OpenAirMargin))
                                continue;
                            // Carving an untouched voxel of an ore vein: that ore is what the swing digs out.
                            if (material == 0 && oreMined != null && overlay.Blocks.Flags[index] == 0
                                && OreVeins.OreAt(id, index, zone) is ushort ore and not 0)
                            {
                                oreMined[ore] = oreMined.GetValueOrDefault(ore) + 1;
                                oreCells?.Add(id);
                            }
                            overlay.Blocks.SetSolid(index, material);
                            cells.Add(id);
                            if (!touched.TryGetValue(id, out var list))
                                touched[id] = list = new List<int>();
                            list.Add(index);
                            changed++;
                        }
                SmoothOffsets(gx, gy, gz, radius, cells, zone, layer);
                foreach (ulong id in cells)
                {
                    Of(zone)[Key(id, layer)].Version++;
                    Of(zone)[Key(id, layer)].Dirty = true;
                }
            }
            // Dug ground belongs to nobody (Voxel Vision).
            foreach ((ulong id, List<int> list) in touched)
                VoxelOwnership.Set(zone, id, list, _ => false, 0);
            return (cells.ToList(), changed);
        }

        /// <summary>How far above the baked ground height a voxel must be to count as certainly air, metres.</summary>
        private const float OpenAirMargin = 2f;

        /// <summary>
        /// Is a voxel's centre above the generated ground (baked height grid) by more than <paramref name="margin"/>
        /// metres (default: one voxel), i.e. already air?
        /// </summary>
        private static bool AboveGround(long vx, long vy, long vz, uint zone, float margin = -1f)
        {
            float lx = (float)(((vx + 0.5) * VoxelSize - TerrainStore.ZoneOrigin.X) / 10),
                ly = (float)(((vy + 0.5) * VoxelSize - TerrainStore.ZoneOrigin.Y) / 10),
                lz = (float)(((vz + 0.5) * VoxelSize - TerrainStore.ZoneOrigin.Z) / 10);
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

        /// <summary>Test switch for <see cref="SmoothOffsets"/>: 0 = off, 1.. = offset byte orders (dev "digmode").</summary>
        public static int OffsetMode = 5;

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
        private static void SmoothOffsets(double gx, double gy, double gz, double radius, HashSet<ulong> cells, uint zone = 0,
            uint layer = DigLayer)
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

        /// <summary>Sends one player the server's version of a cell (undoes a refused local edit).</summary>
        public static void Resend(WorldSession session, ulong id)
        {
            uint zone = session.ZoneId;
            // Every layer is sent (empty where the server has nothing), so this undoes the edit even in a new cell.
            byte[] update = BuildUpdate(id, zone, always: true);
            if (update != null)
                session.EnqueueMessage(ZoneMessageOpcode.TerrainCellCellsUpdate, update);
        }

        /// <summary>Would every voxel an edit changes be allowed by <paramref name="mayEdit"/> (global units)?</summary>
        /// <summary>May a player build every voxel an edit changes in this cell (claims checked once per cell)?</summary>
        public static bool MayApply(ulong id, byte[] changed, ulong playerGuid, uint zone)
        {
            (uint level, uint cx, uint cy, uint cz) = TerrainStore.Decode(id);
            if (level != Level)
                return false;
            double ox = cx * CellSize, oy = cy * CellSize, oz = cz * CellSize;
            var check = Claims.BuildCheckIn(playerGuid, zone, (ox, oy, oz), (ox + CellSize, oy + CellSize, oz + CellSize));
            return check == null || MayApply(id, changed, check);
        }

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

        /// <summary>Clears every override in a cell (dev "clearcell": the generated ground shows again) and sends it out.</summary>
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
            Broadcast(new[] { id }, zone);
        }

        /// <summary>
        /// Writes stored voxel values back into a cell's overlay (undo / redo, paste). Solid voxels written become
        /// <paramref name="owner"/>'s and the others nobody's (Voxel Vision); owner 0 leaves ownership alone.
        /// </summary>
        public static void Write(ulong id, List<VoxelValue> values, uint zone = 0, ulong owner = 0, uint layer = BuildLayer)
        {
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

        /// <summary>The landscapes that have edits.</summary>
        internal static List<uint> Zones()
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
        /// New or changed vein cells (layer 2): every player on the landscape gets a 21-byte
        /// ModifiedCellManifestUpdate and their client fetches the cell when it draws it (no voxels pushed).
        /// </summary>
        public static void Announce(List<ulong> cellIds, uint zone, WorldSession except = null)
        {
            var entries = new List<(ulong Id, uint Version)>();
            lock (sync)
                foreach (ulong id in cellIds)
                    entries.Add((id, VersionOf(id, zone, DigLayer)));
            foreach (WorldSession session in PlayerManager.InZone(zone == 0 ? Home : zone))
                if (session != except)
                    foreach ((ulong id, uint version) in entries)
                        TerrainStore.SendManifestEntry(session, id, DigLayer, version, 0);
        }

        /// <summary>Players within this many metres of a changed cell get its voxels straight away.</summary>
        private const float FullUpdateRange = 256f;

        /// <summary>
        /// Sends changed cells out. Nearby players get the voxel data (they can see it); everyone else a 21-byte
        /// ModifiedCellManifestUpdate per layer the server holds, so their client fetches the cell when it comes into
        /// view. The builder is left out (<paramref name="except"/>): their client already holds the edit in layer 1
        /// and updated its own manifest when the edit was confirmed (HandleCellUpdateResponse FUN_143f52b60); data
        /// sent for a cell they are still editing makes it refuse new edits.
        /// </summary>
        public static void Broadcast(IEnumerable<ulong> cellIds, uint zone = 0) => Broadcast(cellIds, null, zone);

        public static void Broadcast(IEnumerable<ulong> cellIds, WorldSession except, uint zone = 0)
        {
            // Nobody else on the landscape (building alone): no cell data to build and compress.
            var viewers = PlayerManager.InZone(zone == 0 ? Home : zone).Where(s => s != except).ToList();
            if (viewers.Count == 0)
                return;
            var changed = new List<(ulong Id, Vector3 Centre, uint[] Versions, byte[] Update)>();
            foreach (ulong id in cellIds)
            {
                byte[] update = BuildUpdate(id, zone, always: true);
                (Vector3 min, Vector3 max) = TerrainStore.LocalBounds(id);
                var versions = new uint[LayerCount];
                lock (sync)
                    for (uint layer = 0; layer < LayerCount; layer++)
                        versions[layer] = VersionOf(id, zone, layer);
                changed.Add((id, (min + max) / 2, versions, update));
            }
            foreach (WorldSession session in viewers)
                foreach ((ulong id, Vector3 centre, uint[] versions, byte[] update) in changed)
                {
                    if (Vector3.Distance(session.Position, centre) <= FullUpdateRange)
                        session.EnqueueMessage(ZoneMessageOpcode.TerrainCellCellsUpdate, update);
                    else
                        for (uint layer = 0; layer < LayerCount; layer++)
                            if (versions[layer] != 0)
                                TerrainStore.SendManifestEntry(session, id, layer, versions[layer], 0);
                }
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
