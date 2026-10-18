using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// Ore veins, as the live servers made them: the client never spawns veins (its VeinProvider code is server-side
    /// code compiled in; spawning draws from a global random generator), it draws the ore materials the server writes
    /// into the cells it sends. So veins are server state:
    /// - Spawn data: CommonData/Voxel/veinData/ContinentVeinData{zone}.vdt (loader FUN_143faaf90, <see cref="ReadVdt"/>):
    ///   the surface spawn pool (cells by (layer, biome, tier), 447,111 on 406) and the cave pool (590,282), and per
    ///   vein mapping (ResourceVeinGroupVeins id) its vein count and spawn attempts on the surface / in caves (counts are
    ///   about 1063 × DENSITY_ON_SURFACE and 53 × DENSITY_IN_CAVES).
    /// - Spawning: surface veins (FUN_143f187b0) start at the ground height of a random spot in a random pool cell and
    ///   point into the ground (−normal); cave veins (FUN_143f18650 → FUN_143f1a1f0) start on a cave wall, floor or
    ///   ceiling (the .cav air/solid boundary, <see cref="CavePieces"/>) and point into the rock. Walk and ore writing:
    ///   <see cref="VeinGenerator"/>.
    /// The server generates veins region by region (256 m squares) as players come near, from a fixed seed, so they
    /// stay put across restarts; counts per region follow the region's share of the spawn pool. Ore cells are an
    /// in-memory layer under player edits (<see cref="Compose"/>): flag 1 (material only, the client keeps its own
    /// vertex) with the ore's VoxelMaterials ARRAY_INDEX, exactly what the vein code writes. Clients only get a 21-byte
    /// manifest note per ore cell and fetch the cell when they draw it.
    /// Mining an ore voxel gives that ore and marks its vein disturbed; RESPAWN_TIME_DIRTY (1200 s) later the vein
    /// moves to a new spot in its region and the old cells go back to plain ground (dug holes regrow on the same
    /// timer, <see cref="TerrainEdits.Regenerate"/>).
    /// </summary>
    public static class OreVeins
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>World seed of the vein spawner (any value; the same value gives the same veins).</summary>
        public static ulong Seed = 0x4C414E444D41524BUL;

        /// <summary>Region size for generation (dm): 256 m.</summary>
        private const double RegionSize = 2560;
        /// <summary>Players get the veins of their region and the ring around it.</summary>
        private const int RegionRing = 1;
        private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(2), RespawnCheck = TimeSpan.FromMinutes(1);
        /// <summary>Ore voxels per ore item mined (dirt and stone give one item per 4 voxels).</summary>
        public const int VoxelsPerOre = 8;

        /// <summary>
        /// The build-site boxes (global dm) on a landscape that touch a box: the zone's claims, handed in by the
        /// Environment Server (LocalEnvironmentServer) so veins keep out of sites (<see cref="TouchesSite"/>).
        /// </summary>
        public static Func<uint, (double, double, double), (double, double, double), List<(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)>> SitesIn;

        /// <summary>
        /// Other places without ore (landscape, box min / max in global dm → true when the box touches one): the Spire's
        /// platform (patch notes 7 Jun 2016, P5797: no mining nodes around the spires again). Set by the world server;
        /// null = none.
        /// </summary>
        public static Func<uint, (double, double, double), (double, double, double), bool> NoVeins;

        /// <summary>Ore names in the v2 vein descriptions ("v2: metal: copper: common") → this client's raw resource item.</summary>
        private static readonly Dictionary<string, uint> itemByOre = new(StringComparer.OrdinalIgnoreCase)
        {
            ["copper"] = 34, ["tin"] = 35, ["iron"] = 36, ["silver"] = 37, ["gold"] = 39,
            ["marble"] = 47, ["amaranthine"] = 48, ["obsidian"] = 49, ["alabaster"] = 50, ["agate"] = 51,
            ["emerald"] = 56, ["sapphire"] = 57, ["ruby"] = 58, ["diamond"] = 59
        };

        public sealed record Info(uint MappingId, VeinGenerator.Definition Definition, uint Item, int[] Counts, int[] Attempts, double RespawnDirty);

        public sealed class Vein
        {
            public long Id;
            public Info Info;
            public (int X, int Z) Region;
            public bool Cave;
            public List<VeinGenerator.Point> Points;
            public List<ulong> Cells = new();
            public DateTime? DisturbedAt;
            public uint GroupVeinId => Info.MappingId;
            public uint Item => Info.Item;
        }

        private sealed class OreCell
        {
            public Dictionary<int, ushort> Voxels = new();
            public List<long> Veins = new();
            public uint Version = 1;
        }

        private sealed class Zone
        {
            public uint Id;
            public Dictionary<(int, int), List<ulong>> SurfacePool = new(), CavePool = new();
            public long SurfaceTotal, CaveTotal;
            public List<Info> Infos = new();
            public HashSet<(int, int)> Generated = new();
            public Dictionary<(int, int), VeinGenerator.SpawnRandom> RegionRandom = new();
            public Dictionary<ulong, OreCell> Cells = new();
            public Dictionary<long, Vein> Veins = new();
            public long NextVeinId = 1;
        }

        private static readonly object sync = new();
        private static readonly Dictionary<uint, Zone> zones = new();
        private static readonly Dictionary<ushort, uint> itemByMaterial = new();
        private static Dictionary<uint, VeinGenerator.Definition> definitions;
        private static Dictionary<uint, uint> veinOfMapping;
        private static string gameDirectory;
        private static DateTime nextUpdate = DateTime.MinValue, nextRespawn = DateTime.MinValue;

        public static void Load(string gameDirectory, uint continent)
        {
            OreVeins.gameDirectory = gameDirectory;
            try
            {
                definitions = VeinGenerator.LoadDefinitions();
                veinOfMapping = GameDataFiles.Rows("ResourceVeinGroupVeins.txt").ToDictionary(r => r.U(0), r => r.U(1));
                if (ZoneOf(continent) is Zone z)
                    log.Info($"Ore veins: {z.Infos.Count} vein kinds, spawn pools {z.SurfaceTotal} surface / {z.CaveTotal} cave cells, " +
                        $"{z.Infos.Sum(i => i.Counts[0] + i.Counts[1])} veins on the landscape.");
            }
            catch (Exception e)
            {
                log.Warn($"No ore vein data ({e.Message}).");
            }
        }

        /// <summary>The raw resource item an ore material (VoxelMaterials ARRAY_INDEX) gives, or 0.</summary>
        public static uint ItemFor(ushort material) => itemByMaterial.GetValueOrDefault(material);

        /// <summary>
        /// Every ore material known so far with its raw resource item (filled as landscapes' vein data load): a remote
        /// Environment Server sends it to the zone with its version index, so <see cref="ItemFor"/> is answered there
        /// without a round trip.
        /// </summary>
        public static List<(ushort Material, uint Item)> OreItems()
        {
            lock (sync)
                return itemByMaterial.Select(p => (p.Key, p.Value)).ToList();
        }

        private static Zone ZoneOf(uint zone)
        {
            lock (sync)
            {
                if (zones.TryGetValue(zone, out Zone z))
                    return z;
                z = null;
                string path = Path.Combine(gameDirectory ?? "", "CommonData", "Voxel", "veinData", $"ContinentVeinData{zone}.vdt");
                if (definitions != null && File.Exists(path))
                {
                    try
                    {
                        z = ReadVdt(zone, path);
                    }
                    catch (Exception e)
                    {
                        log.Warn($"Vein data for landscape {zone} unreadable ({e.Message}).");
                    }
                }
                zones[zone] = z;
                return z;
            }
        }

        /// <summary>
        /// The .vdt: u32 0, f64, f64, u32; u32 n × layer {u32 id, f64[8] box, u32 m × i32[3] category}; u32 n × (u32 m ×
        /// i32) sets; two maps u32 n × {i32[3] category, i32 count}; two maps u32 n × {i32[3] category, u32 m × u64 sorted
        /// cell ids} (surface pool, cave pool); u32 n × {i32[3], u32 m × i32}; u32 n × vein info {u32 mapping, u32 vein,
        /// u32 group, u32 mapping, u32 m × i32 counts (surface, caves, void), u32 m × i32 attempts, i32[3] category}.
        /// </summary>
        private static Zone ReadVdt(uint zoneId, string path)
        {
            var z = new Zone { Id = zoneId };
            using var r = new BinaryReader(File.OpenRead(path));
            r.ReadUInt32(); r.ReadDouble(); r.ReadDouble(); r.ReadUInt32();
            for (uint n = r.ReadUInt32(); n > 0; n--)
            {
                r.ReadUInt32();
                for (int i = 0; i < 8; i++) r.ReadDouble();
                for (uint m = r.ReadUInt32(); m > 0; m--) { r.ReadInt32(); r.ReadInt32(); r.ReadInt32(); }
            }
            for (uint n = r.ReadUInt32(); n > 0; n--)
                for (uint m = r.ReadUInt32(); m > 0; m--) r.ReadInt32();
            for (int k = 0; k < 2; k++)
                for (uint n = r.ReadUInt32(); n > 0; n--) { r.ReadInt32(); r.ReadInt32(); r.ReadInt32(); r.ReadInt32(); }
            for (int pool = 0; pool < 2; pool++)
            {
                var byRegion = pool == 0 ? z.SurfacePool : z.CavePool;
                for (uint n = r.ReadUInt32(); n > 0; n--)
                {
                    r.ReadInt32(); r.ReadInt32(); r.ReadInt32();
                    for (uint m = r.ReadUInt32(); m > 0; m--)
                    {
                        ulong cell = r.ReadUInt64();
                        (double x, _, double cz) = TerrainStore.CellOrigin(cell);
                        var key = ((int)Math.Floor(x / RegionSize), (int)Math.Floor(cz / RegionSize));
                        if (!byRegion.TryGetValue(key, out var list))
                            byRegion[key] = list = new List<ulong>();
                        list.Add(cell);
                        if (pool == 0) z.SurfaceTotal++; else z.CaveTotal++;
                    }
                }
            }
            for (uint n = r.ReadUInt32(); n > 0; n--)
            {
                r.ReadInt32(); r.ReadInt32(); r.ReadInt32();
                for (uint m = r.ReadUInt32(); m > 0; m--) r.ReadInt32();
            }
            for (uint n = r.ReadUInt32(); n > 0; n--)
            {
                r.ReadUInt32();
                uint veinId = r.ReadUInt32();
                r.ReadUInt32();
                uint mapping = r.ReadUInt32();
                int[] counts = new int[r.ReadUInt32()];
                for (int i = 0; i < counts.Length; i++) counts[i] = r.ReadInt32();
                int[] attempts = new int[r.ReadUInt32()];
                for (int i = 0; i < attempts.Length; i++) attempts[i] = r.ReadInt32();
                r.ReadInt32(); r.ReadInt32(); r.ReadInt32();
                if (!definitions.TryGetValue(veinId, out var def) || counts.Length < 2 || attempts.Length < 2)
                    continue;
                string[] parts = def.Name.Split(':').Select(p => p.Trim()).ToArray();
                if (parts.Length < 3 || !itemByOre.TryGetValue(parts[2], out uint item))
                    continue;
                foreach (ushort material in def.Materials.Where(m => m != 0))
                    itemByMaterial.TryAdd(material, item);
                z.Infos.Add(new Info(mapping, def, item, counts, attempts, 1200));
            }
            return z;
        }

        private static ulong Mix(ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }

        // ---- generation ----------------------------------------------------------------------------------------------

        /// <summary>Generates a region's veins (once). Returns the ore cells created.</summary>
        private static List<ulong> GenerateRegion(Zone z, (int X, int Z) region)
        {
            var changed = new List<ulong>();
            if (!z.Generated.Add(region))
                return changed;
            var random = new VeinGenerator.SpawnRandom(Mix(Seed ^ ((ulong)z.Id << 48) ^ ((ulong)(uint)region.X << 24) ^ (uint)region.Z));
            z.RegionRandom[region] = random;
            int made = 0;
            foreach (Info info in z.Infos)
                for (int pool = 0; pool < 2; pool++)
                {
                    if (info.Counts[pool] <= 0)
                        continue;
                    var cells = (pool == 0 ? z.SurfacePool : z.CavePool).GetValueOrDefault(region);
                    if (cells == null || cells.Count == 0)
                        continue;
                    double expected = info.Counts[pool] * (double)cells.Count / (pool == 0 ? z.SurfaceTotal : z.CaveTotal);
                    int n = (int)expected + (random.Next() < expected - Math.Floor(expected) ? 1 : 0);
                    for (int k = 0; k < n; k++)
                        if (Spawn(z, info, region, pool == 1, random, changed))
                            made++;
                }
            if (made > 0)
                log.Debug($"Veins: region {region} of landscape {z.Id}: {made} vein(s), {changed.Count} cell(s).");
            return changed;
        }

        /// <summary>One vein of a kind in a region (a few attempts, as the vdt's attempt budget allows).</summary>
        private static bool Spawn(Zone z, Info info, (int X, int Z) region, bool cave, VeinGenerator.SpawnRandom random, List<ulong> changed)
        {
            var pool = (cave ? z.CavePool : z.SurfacePool).GetValueOrDefault(region);
            if (pool == null || pool.Count == 0)
                return false;
            int poolIndex = cave ? 1 : 0;
            int tries = Math.Clamp((int)Math.Ceiling(info.Attempts[poolIndex] / (double)Math.Max(1, info.Counts[poolIndex])), 1, 4);
            for (int attempt = 0; attempt < tries; attempt++)
            {
                ulong cell = pool[(int)(random.Next() * pool.Count)];
                var start = cave ? CaveStart(z.Id, cell, random) : SurfaceStart(z.Id, info.Definition, cell, random);
                if (start is not var (position, direction))
                    continue;
                var vein = new Vein
                {
                    Id = z.NextVeinId++,
                    Info = info,
                    Region = region,
                    Cave = cave,
                    Points = VeinGenerator.Walk(info.Definition, position, direction, (uint)(random.Next() * 4294967296.0))
                };
                if (TouchesSite(z.Id, VeinGenerator.CellsOf(info.Definition, vein.Points)))
                    continue;
                Place(z, vein, changed);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Build sites hold no veins: gathering works only on unclaimed land (design/wiki Gathering: no gathering on
        /// build sites), and the client's spawner (FUN_143f187b0) has no site test
        /// of its own, so the live server kept them out. A spot whose cells touch a site is refused like any other
        /// failed attempt.
        /// </summary>
        private static bool TouchesSite(uint zone, IEnumerable<(long X, long Y, long Z)> cells)
        {
            double size = VeinGenerator.CellSize;
            foreach (var c in cells)
            {
                var min = (c.X * size, c.Y * size, c.Z * size);
                var max = (min.Item1 + size, min.Item2 + size, min.Item3 + size);
                if (SitesIn(zone, min, max).Count > 0 || (zone == TerrainStore.HomeContinent && SitesIn(0, min, max).Count > 0))
                    return true;
                if (NoVeins != null && NoVeins(zone, min, max))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// A build site was placed (box in global dm): the veins touching it are taken out (their cells go back to plain
        /// ground for everyone) and spawn again elsewhere in their region, so the region keeps its share of ore. Returns the
        /// changed ore cells (none when no vein moved), which the zone announces (ZoneTerrain.ClearVeinsInSite).
        /// </summary>
        public static List<ulong> ClearSite(uint zone, (double X, double Y, double Z) min, (double X, double Y, double Z) max)
        {
            uint zoneId = zone == 0 ? TerrainStore.HomeContinent : zone;
            if (definitions == null || ZoneOf(zoneId) is not Zone z)
                return new List<ulong>();
            var changed = new List<ulong>();
            int moved = 0;
            double size = VeinGenerator.CellSize;
            lock (sync)
                foreach (Vein vein in z.Veins.Values.ToList())
                {
                    bool inside = vein.Cells.Any(id =>
                    {
                        (double x, double y, double cz) = TerrainStore.CellOrigin(id);
                        return x <= max.X && x + size >= min.X && y <= max.Y && y + size >= min.Y && cz <= max.Z && cz + size >= min.Z;
                    });
                    if (!inside)
                        continue;
                    Remove(z, vein, changed);
                    if (z.RegionRandom.TryGetValue(vein.Region, out var random))
                        Spawn(z, vein.Info, vein.Region, vein.Cave, random, changed);
                    moved++;
                }
            if (moved == 0)
                return new List<ulong>();
            log.Info($"Veins: {moved} vein(s) moved out of a new build site on landscape {zoneId}.");
            return changed.Distinct().ToList();
        }

        /// <summary>Surface vein start: ground height at a random spot of the cell, pointing into the ground.</summary>
        private static ((double, double, double), (double, double, double))? SurfaceStart(uint zone, VeinGenerator.Definition d, ulong cell, VeinGenerator.SpawnRandom random)
        {
            (double cx, double cy, double cz) = TerrainStore.CellOrigin(cell);
            double gx = cx + random.Next() * VeinGenerator.CellSize, gz = cz + random.Next() * VeinGenerator.CellSize;
            var origin = TerrainStore.ZoneOrigin;
            float lx = (float)((gx - origin.X) / 10), lz = (float)((gz - origin.Z) / 10);
            if (TerrainHeights.HeightAt(lx, lz, zone) is not float h)
                return null;
            double gy = h * 10 + origin.Y;
            if (gy < cy || gy > cy + VeinGenerator.CellSize)
                return null;
            gy += (random.Next() * (d.SurfaceOffsetMax - d.SurfaceOffsetMin) + d.SurfaceOffsetMin) * -10.0;
            float hx = TerrainHeights.HeightAt(lx + 2f, lz, zone) ?? h, hz = TerrainHeights.HeightAt(lx, lz + 2f, zone) ?? h;
            var normal = Vector3.Normalize(new Vector3(-(hx - h) / 2f, 1f, -(hz - h) / 2f));
            return ((gx, gy, gz), (-normal.X, -normal.Y, -normal.Z));
        }

        /// <summary>
        /// Cave vein start: a random column of the cell, where cave air meets rock (floor or ceiling, picked at random),
        /// pointing into the rock.
        /// </summary>
        private static ((double, double, double), (double, double, double))? CaveStart(uint zone, ulong cell, VeinGenerator.SpawnRandom random)
        {
            (double cx, double cy, double cz) = TerrainStore.CellOrigin(cell);
            double gx = cx + random.Next() * VeinGenerator.CellSize, gz = cz + random.Next() * VeinGenerator.CellSize;
            double vs = VeinGenerator.VoxelSize;
            var faces = new List<(double Y, double Dir)>();
            bool below = CavePieces.IsAir(gx, cy - vs / 2, gz, zone);
            for (double y = cy + vs / 2; y < cy + VeinGenerator.CellSize; y += vs)
            {
                bool air = CavePieces.IsAir(gx, y, gz, zone);
                if (air && !below)
                    faces.Add((y - vs, -1));      // floor: the rock below the air
                else if (!air && below)
                    faces.Add((y, 1));            // ceiling: the rock above the air
                below = air;
            }
            if (faces.Count == 0)
                return null;
            (double fy, double dir) = faces[(int)(random.Next() * faces.Count)];
            return ((gx, fy, gz), (0, dir, 0));
        }

        /// <summary>Is the generated world solid at a global point (below the ground and not cave air)?</summary>
        private static bool GeneratedSolid(uint zone, double gx, double gy, double gz, Dictionary<(int, int), float?> heights)
        {
            var origin = TerrainStore.ZoneOrigin;
            var key = ((int)Math.Floor(gx / VeinGenerator.VoxelSize), (int)Math.Floor(gz / VeinGenerator.VoxelSize));
            if (!heights.TryGetValue(key, out float? h))
                heights[key] = h = TerrainHeights.HeightAt((float)((gx - origin.X) / 10), (float)((gz - origin.Z) / 10), zone);
            return h is float ground && gy < ground * 10 + origin.Y && !CavePieces.IsAir(gx, gy, gz, zone);
        }

        /// <summary>Writes a vein's ore into its cells (the captured live cells are left alone).</summary>
        private static void Place(Zone z, Vein vein, List<ulong> changed)
        {
            z.Veins[vein.Id] = vein;
            var heights = new Dictionary<(int, int), float?>();
            foreach (var c in VeinGenerator.CellsOf(vein.Info.Definition, vein.Points))
            {
                if (c.X < 0 || c.Y < 0 || c.Z < 0)
                    continue;
                ulong id = TerrainStore.CellId(TerrainEdits.Level, (uint)c.X, (uint)c.Y, (uint)c.Z);
                if (z.Id == TerrainStore.HomeContinent && TerrainStore.Get(id) != null)
                    continue;
                double ox = c.X * VeinGenerator.CellSize, oy = c.Y * VeinGenerator.CellSize, oz = c.Z * VeinGenerator.CellSize;
                var ore = VeinGenerator.ApplyCell(vein.Info.Definition, vein.Points, c, (x, y, zz) => GeneratedSolid(z.Id,
                    ox + (x + 0.5) * VeinGenerator.VoxelSize, oy + (y + 0.5) * VeinGenerator.VoxelSize, oz + (zz + 0.5) * VeinGenerator.VoxelSize, heights));
                if (ore.Count == 0)
                    continue;
                if (!z.Cells.TryGetValue(id, out OreCell cell))
                    z.Cells[id] = cell = new OreCell();
                foreach (var ((x, y, zz), material) in ore)
                    cell.Voxels[TerrainEdits.VoxelIndex(x, y, zz)] = material;
                cell.Veins.Add(vein.Id);
                cell.Version++;
                vein.Cells.Add(id);
                changed.Add(id);
            }
        }

        /// <summary>Takes a vein out and rebuilds its cells from the veins left there.</summary>
        private static void Remove(Zone z, Vein vein, List<ulong> changed)
        {
            z.Veins.Remove(vein.Id);
            var heights = new Dictionary<(int, int), float?>();
            foreach (ulong id in vein.Cells)
            {
                if (!z.Cells.TryGetValue(id, out OreCell cell))
                    continue;
                cell.Veins.Remove(vein.Id);
                cell.Voxels.Clear();
                (_, uint cx, uint cy, uint cz) = TerrainStore.Decode(id);
                double ox = cx * VeinGenerator.CellSize, oy = cy * VeinGenerator.CellSize, oz = cz * VeinGenerator.CellSize;
                foreach (long other in cell.Veins)
                    if (z.Veins.TryGetValue(other, out Vein v))
                        foreach (var ((x, y, zz), material) in VeinGenerator.ApplyCell(v.Info.Definition, v.Points, (cx, cy, cz),
                            (x, y, zz) => GeneratedSolid(z.Id, ox + (x + 0.5) * VeinGenerator.VoxelSize, oy + (y + 0.5) * VeinGenerator.VoxelSize,
                                oz + (zz + 0.5) * VeinGenerator.VoxelSize, heights)))
                            cell.Voxels[TerrainEdits.VoxelIndex(x, y, zz)] = material;
                cell.Version++;
                changed.Add(id);
            }
        }

        // ---- world tick ----------------------------------------------------------------------------------------------

        /// <summary>
        /// World tick: generates the regions around the players (landscape, local position; read as the loop goes), and
        /// moves disturbed veins once their respawn time has passed. Returns the new or changed ore cells per landscape,
        /// which the zone announces to its players (ZoneTerrain.UpdateVeins).
        /// </summary>
        public static IReadOnlyList<(uint Zone, List<ulong> Cells)> Update(IEnumerable<(uint Zone, Vector3 Position)> players)
        {
            DateTime now = DateTime.UtcNow;
            if (now < nextUpdate || definitions == null)
                return Array.Empty<(uint, List<ulong>)>();
            nextUpdate = now + UpdateInterval;
            var announce = new Dictionary<uint, List<ulong>>();
            foreach ((uint playerZone, Vector3 position) in players)
            {
                uint zoneId = playerZone == 0 ? TerrainStore.HomeContinent : playerZone;
                if (ZoneOf(zoneId) is not Zone z)
                    continue;
                (double gx, _, double gz) = TerrainEdits.ToGlobal(position);
                int rx = (int)Math.Floor(gx / RegionSize), rz = (int)Math.Floor(gz / RegionSize);
                lock (sync)
                    for (int dx = -RegionRing; dx <= RegionRing; dx++)
                        for (int dz = -RegionRing; dz <= RegionRing; dz++)
                            if (!z.Generated.Contains((rx + dx, rz + dz)))
                            {
                                if (!announce.TryGetValue(zoneId, out var list))
                                    announce[zoneId] = list = new List<ulong>();
                                list.AddRange(GenerateRegion(z, (rx + dx, rz + dz)));
                            }
            }
            if (now >= nextRespawn)
            {
                nextRespawn = now + RespawnCheck;
                lock (sync)
                    foreach (Zone z in zones.Values.Where(v => v != null))
                        foreach (Vein vein in z.Veins.Values.Where(v => v.DisturbedAt is DateTime t && (now - t).TotalSeconds >= v.Info.RespawnDirty).ToList())
                        {
                            if (!announce.TryGetValue(z.Id, out var list))
                                announce[z.Id] = list = new List<ulong>();
                            Remove(z, vein, list);
                            if (z.RegionRandom.TryGetValue(vein.Region, out var random))
                                Spawn(z, vein.Info, vein.Region, vein.Cave, random, list);
                            log.Debug($"Veins: a disturbed {vein.Info.Definition.Name} vein moved.");
                        }
            }
            return announce.Where(a => a.Value.Count > 0).Select(a => (a.Key, a.Value.Distinct().ToList())).ToList();
        }

        /// <summary>
        /// Generates the vein regions around a position before that player's modified-cell manifest is built, so the
        /// ore cells they will see first are in the manifest itself. Generated only on arrival (the world tick), those
        /// cells came as one ModifiedCellManifestUpdate each, after the client had already loaded the cells from its
        /// mesh cache with the plain terrain's version: every session it found the cached mesh (stored with the vein
        /// layer's version) stale, re-meshed without the ore, then fetched and re-meshed again ("Failed to load mesh
        /// ... due to version mismatch", VoxelDataErrors.log, 2026-10-12). The new cells are still announced to the
        /// other players on the landscape: this returns them, and the zone announces them (ZoneTerrain.SendManifest).
        /// </summary>
        public static List<ulong> EnsureAround(uint zone, Vector3 localPosition)
        {
            uint zoneId = zone == 0 ? TerrainStore.HomeContinent : zone;
            if (definitions == null || ZoneOf(zoneId) is not Zone z)
                return new List<ulong>();
            (double gx, _, double gz) = TerrainEdits.ToGlobal(localPosition);
            int rx = (int)Math.Floor(gx / RegionSize), rz = (int)Math.Floor(gz / RegionSize);
            var made = new List<ulong>();
            lock (sync)
                for (int dx = -RegionRing; dx <= RegionRing; dx++)
                    for (int dz = -RegionRing; dz <= RegionRing; dz++)
                        if (!z.Generated.Contains((rx + dx, rz + dz)))
                            made.AddRange(GenerateRegion(z, (rx + dx, rz + dz)));
            return made.Distinct().ToList();
        }

        // ---- the ore layer for TerrainEdits ----------------------------------------------------------------------------

        /// <summary>Does the vein layer hold this cell (ore now, or ore once: then it is sent empty)?</summary>
        public static bool Known(ulong cellId, uint zone)
        {
            if (ZoneOf(zone == 0 ? TerrainStore.HomeContinent : zone) is not Zone z)
                return false;
            lock (sync)
                return z.Cells.ContainsKey(cellId);
        }

        /// <summary>The vein layer's version of a cell (0 when it holds none).</summary>
        public static uint VersionOf(ulong cellId, uint zone)
        {
            if (ZoneOf(zone == 0 ? TerrainStore.HomeContinent : zone) is not Zone z)
                return 0;
            lock (sync)
                return z.Cells.TryGetValue(cellId, out OreCell cell) ? cell.Version : 0;
        }

        /// <summary>The cells the vein layer holds, with their versions (for the zone-in manifest).</summary>
        public static List<(ulong Cell, uint Version)> Entries(uint zone)
        {
            if (ZoneOf(zone == 0 ? TerrainStore.HomeContinent : zone) is not Zone z)
                return new();
            lock (sync)
                return z.Cells.Select(c => (c.Key, c.Value.Version)).ToList();
        }

        /// <summary>Adds the ore under a cell's edits: voxels the overlay doesn't set get flag 1 + the ore material.</summary>
        public static bool Compose(ulong cellId, uint zone, BlockData blocks)
        {
            if (ZoneOf(zone == 0 ? TerrainStore.HomeContinent : zone) is not Zone z)
                return false;
            bool any = false;
            lock (sync)
            {
                if (!z.Cells.TryGetValue(cellId, out OreCell cell))
                    return false;
                foreach ((int index, ushort material) in cell.Voxels)
                    if (blocks.Flags[index] == 0)
                    {
                        blocks.Flags[index] = 1;
                        blocks.Materials[index] = material;
                        any = true;
                    }
            }
            return any;
        }

        /// <summary>The ore material at a voxel of a cell, or 0.</summary>
        public static ushort OreAt(ulong cellId, int index, uint zone)
        {
            if (ZoneOf(zone == 0 ? TerrainStore.HomeContinent : zone) is not Zone z)
                return 0;
            lock (sync)
                return z.Cells.TryGetValue(cellId, out OreCell cell) ? cell.Voxels.GetValueOrDefault(index) : (ushort)0;
        }

        /// <summary>Ore was mined in these cells: their veins are disturbed (they move on after RESPAWN_TIME_DIRTY).</summary>
        public static void Disturbed(IEnumerable<ulong> cells, uint zone)
        {
            if (ZoneOf(zone == 0 ? TerrainStore.HomeContinent : zone) is not Zone z)
                return;
            DateTime now = DateTime.UtcNow;
            lock (sync)
                foreach (ulong id in cells)
                    if (z.Cells.TryGetValue(id, out OreCell cell))
                        foreach (long v in cell.Veins)
                            if (z.Veins.TryGetValue(v, out Vein vein))
                                vein.DisturbedAt ??= now;
        }

        // ---- detectors -----------------------------------------------------------------------------------------------

        /// <summary>Veins with a point within <paramref name="radius"/> metres of a position (local), nearest point first.</summary>
        public static List<(Vein Vein, Vector3 Nearest, float Distance)> Near(Vector3 position, float radius, uint zone = 0)
        {
            var found = new List<(Vein, Vector3, float)>();
            if (ZoneOf(zone == 0 ? TerrainStore.HomeContinent : zone) is not Zone z)
                return found;
            var origin = TerrainStore.ZoneOrigin;
            lock (sync)
                foreach (Vein vein in z.Veins.Values)
                {
                    Vector3 best = default;
                    float bestDistance = float.MaxValue;
                    foreach (var p in vein.Points)
                    {
                        var local = new Vector3((float)((p.X - origin.X) / 10), (float)((p.Y - origin.Y) / 10), (float)((p.Z - origin.Z) / 10));
                        float d = Vector3.Distance(local, position);
                        if (d < bestDistance)
                        {
                            bestDistance = d;
                            best = local;
                        }
                    }
                    if (bestDistance <= radius)
                        found.Add((vein, best, bestDistance));
                }
            return found.OrderBy(f => f.Item3).ToList();
        }

        /// <summary>The nearest vein of one of <paramref name="items"/> (prospecting gadgets): (item, where, distance), or null.</summary>
        public static (uint Item, Vector3 At, float Distance)? Nearest(Vector3 position, float radius, ISet<uint> items, uint zone = 0)
            => Near(position, radius, zone).Where(v => items.Contains(v.Vein.Item)).Select(v => ((uint, Vector3, float)?)(v.Vein.Item, v.Nearest, v.Distance))
                .FirstOrDefault();
    }
}
