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
    /// - Spawn data: CommonData/Voxel/veinData/ContinentVeinData{continent}.vdt (loader FUN_143ff34d0, by the landscape's
    ///   continent id: <see cref="VdtPath"/>; read by <see cref="ReadVdt"/>):
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
    /// Ore only ever repaints voxels the client's generated world already has solid (FUN_143fdb110 = 0.589 FUN_143f17220
    /// tests the cell's own generated block: flag bit 0 and material 1..0xFFFE) [P], but on the wire an ore voxel (flag 1
    /// + material) is solid whatever the client generated, so the server must be sure of the ground itself
    /// (<see cref="GeneratedSolid"/>). Where it knows the ground only from the client's surface cache (the Pioneer lands,
    /// <see cref="Zone.CoarseGround"/>), it is sure only under the cache's floor: surface veins are buried there or left
    /// out (<see cref="PioneerSurfaceVeins"/>; the 2026-11-01 lumps, trace/0695/pioneer-veins.md).
    /// </summary>
    public static class OreVeins
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>World seed of the vein spawner (any value; the same value gives the same veins).</summary>
        public static ulong Seed = 0x4C414E444D41524BUL;

        /// <summary>Surface veins where the server knows the ground only from the client's surface cache.</summary>
        public enum CoarseSurfaceVeins
        {
            /// <summary>
            /// Kept, started at the surface cache's floor (<see cref="SurfaceCache.FloorAt"/>) instead of the estimated
            /// ground, so every ore voxel lies in rock the client certainly generates: same kinds, counts and x / z as
            /// the vein data gives. On the plateau that is a few metres under the grass (ore from 1022.8 m down, about
            /// 4.6 m; the ground the client draws 1027.4-1029.5 m). The floor is the lowest of the 3 × 3 neighbouring 16 m
            /// columns, so on slopes and coasts a vein lies about 17-50 m down (13 % of the 440 cache's columns step down one
            /// cell or more beside a neighbour), and on the seabed at 734.8 m. Starting at the column's own bottom would not help: an
            /// 8 m vein started a cell or more above that floor gets no ore at all (<see cref="GeneratedSolid"/> clips there too).
            /// </summary>
            Buried,
            /// <summary>None (cave veins stay).</summary>
            Off,
        }

        /// <summary>
        /// WorldServer.json / EnvServer.json PioneerSurfaceVeins (read at start-up): surface veins on the landscapes whose
        /// ground the server knows only from the surface cache (the Pioneer lands 464-468, whose grids
        /// re/tools/csc_heights.py made; a 2 m bake of their ground would make them classic). Live had surface ore there
        /// (wiki "Area Found: All landscape surfaces", Exploring: "plenty of resources" on the Pioneer Landscape) as flush
        /// patches, which needs the exact ground; Buried is the closest the server can do without it.
        /// </summary>
        public static CoarseSurfaceVeins PioneerSurfaceVeins = CoarseSurfaceVeins.Buried;

        /// <summary>
        /// WorldServer.json / EnvServer.json PioneerSurfaceIron (read at start-up): surface iron (the iron vein of the
        /// surface group, ResourceVeins 247) on the Pioneer lands. Off as live: the 26 Apr 2016 hotfix made iron
        /// "available on the surface of (non-Pioneer) Landscapes" (design/forum 2016-04-26-56999) [P], although the
        /// shipped ContinentVeinData440-444.vdt list it like every surface vein.
        /// </summary>
        public static bool PioneerSurfaceIron;

        /// <summary>PioneerSurfaceVeins text → mode ("off" = Off; anything else, "buried" included, = Buried).</summary>
        public static CoarseSurfaceVeins ParseSurfaceVeins(string value)
            => string.Equals(value?.Trim(), "off", StringComparison.OrdinalIgnoreCase) ? CoarseSurfaceVeins.Off : CoarseSurfaceVeins.Buried;

        /// <summary>Grids up to this spacing (m) are bakes of the client's own ground; coarser ones are estimates.</summary>
        private const float ExactGroundStep = 2f;
        /// <summary>The raw iron item: the surface iron vein's.</summary>
        private const uint IronItem = 36;

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
            /// <summary>
            /// The server's ground here is an estimate from the surface cache (its grid is coarser than a 2 m bake): ore
            /// goes only under <see cref="SurfaceCache.FloorAt"/>.
            /// </summary>
            public bool CoarseGround;
            /// <summary>
            /// The client's surface cache for this coarse-ground land is there: ore is clipped at its floor. Without it
            /// surface veins are left out and cave ore falls back to the estimated ground (<see cref="GeneratedSolid"/>).
            /// </summary>
            public bool HasSurfaceCache;
            /// <summary>A Pioneer land (StaticZoneData IS_NEWBIE_ZONE).</summary>
            public bool Pioneer;
            /// <summary>The vein layer's first cell version here (<see cref="VersionBaseOf"/>).</summary>
            public uint VersionBase;
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

        /// <summary>
        /// The vein file the client loads for a landscape: <c>Voxel\veinData\ContinentVeinData%d.vdt</c> with the
        /// continent id (FUN_143ff34d0 formats the VoxelFarm core's +0x5208, the zone's continent; <see cref="LandscapeContinents"/>).
        /// Same as the zone id on the classic islands; the Pioneer lands 464-468 read 440-444.
        /// </summary>
        public static string VdtPath(string gameDirectory, uint zone)
            => Path.Combine(gameDirectory ?? "", "CommonData", "Voxel", "veinData",
                $"ContinentVeinData{LandscapeContinents.Of(zone, gameDirectory)}.vdt");

        /// <summary>
        /// A landscape's spawn pools as loaded (offline checks and the dev tools): surface / cave pool cells, vein kinds
        /// and veins the vdt gives the landscape; null when it has no vein data.
        /// </summary>
        public static (long SurfaceCells, long CaveCells, int Kinds, int Veins)? PoolsOf(uint zone)
            => ZoneOf(zone == 0 ? TerrainStore.HomeContinent : zone) is Zone z
                ? (z.SurfaceTotal, z.CaveTotal, z.Infos.Count, z.Infos.Sum(i => i.Counts[0] + i.Counts[1]))
                : null;

        private static Zone ZoneOf(uint zone)
        {
            lock (sync)
            {
                if (zones.TryGetValue(zone, out Zone z))
                    return z;
                z = null;
                string path = VdtPath(gameDirectory, zone);
                if (definitions != null && File.Exists(path))
                {
                    try
                    {
                        z = ReadVdt(zone, path);
                        z.CoarseGround = CoarseGroundOf(zone);
                        z.HasSurfaceCache = z.CoarseGround && SurfaceCache.Has(gameDirectory, zone);
                        z.Pioneer = LandscapeContinents.IsPioneer(zone, gameDirectory);
                        z.VersionBase = VersionBaseOf(z);
                        if (z.CoarseGround || z.Pioneer)
                            log.Info($"Ore veins on landscape {zone}: "
                                + (z.CoarseGround ? $"ground known only from the surface cache, surface veins {(PioneerSurfaceVeins == CoarseSurfaceVeins.Off ? "off" : "buried under its floor")} (PioneerSurfaceVeins)" : "ground baked")
                                + (z.Pioneer ? $"; Pioneer land, surface iron {(PioneerSurfaceIron ? "on" : "off")} (PioneerSurfaceIron)" : "") + ".");
                        if (z.CoarseGround && !z.HasSurfaceCache)
                            log.Warn($"Ore veins on landscape {zone}: no surface cache ({Path.GetFileName(SurfaceCache.PathOf(gameDirectory, zone))} "
                                + "missing or unreadable): no surface veins, and cave ore uses the estimated ground.");
                    }
                    catch (Exception e)
                    {
                        log.Warn($"Vein data for landscape {zone} ({Path.GetFileName(path)}) unreadable ({e.Message}).");
                        z = null;
                    }
                }
                zones[zone] = z;
                return z;
            }
        }

        /// <summary>
        /// The vein layout's version base of a landscape (<see cref="VersionBaseOf(Zone)"/>; 0 on the classic islands and
        /// without vein data): TerrainEdits adds it to the dig-layer version of every cell that has an overlay but no ore
        /// (ore cells already carry it).
        /// </summary>
        public static uint DigVersionBase(uint zone)
            => ZoneOf(zone == 0 ? TerrainStore.HomeContinent : zone)?.VersionBase ?? 0;

        /// <summary>
        /// The vein layer's first cell version on a landscape. The client's mesh cache keeps a cell's mesh with its version
        /// and draws it again while the manifest gives the same version (docs/terrain.md "Client mesh cache"); vein cells
        /// are rebuilt from the seed at every start with the same small versions, so a landscape whose vein layout rules
        /// change needs versions no earlier layout gave, or clients keep drawing the old ore (here: the Pioneer lumps of
        /// 2026-11-01 from their cache). 0 = the layout of earlier server versions (every classic island, unchanged);
        /// otherwise a block of 65,536 versions per layout: bit 1 surface veins buried (coarse ground), 2 left out, 4 surface
        /// iron left out, 8 a Pioneer land (any layout since 2026-11-01). Far below the generated cells' 492747177.
        /// The base goes on every dig-layer version of the landscape, not only on cells that still hold ore
        /// (<see cref="DigVersionBase"/>): a cell whose ore the new layout dropped but which keeps a dig overlay would
        /// otherwise report the overlay's version alone, which can equal an old overlay + ore version the client cached.
        /// </summary>
        private static uint VersionBaseOf(Zone z)
        {
            uint layout = 0;
            if (z.CoarseGround)
                layout |= PioneerSurfaceVeins == CoarseSurfaceVeins.Off ? 2u : 1u;
            if (z.Pioneer)
                layout |= 8u | (PioneerSurfaceIron ? 0u : 4u);
            return layout << 16;
        }

        /// <summary>
        /// Is the landscape's ground grid coarser than a 2 m bake of the client's own ground (an estimate from the surface
        /// cache)? False without a grid (no surface veins then anyway: <see cref="SurfaceStart"/> needs a height).
        /// </summary>
        private static bool CoarseGroundOf(uint zone)
        {
            try
            {
                return TerrainHeights.StepOf(zone) is float step && step > ExactGroundStep;
            }
            catch (Exception e)
            {
                log.Warn($"Terrain heights for landscape {zone} not readable for the vein layer ({e.Message}).");
                return false;
            }
        }

        /// <summary>Does this landscape get surface veins of this kind (PioneerSurfaceVeins, PioneerSurfaceIron)?</summary>
        private static bool SurfaceAllowed(Zone z, Info info)
            => !(z.CoarseGround && PioneerSurfaceVeins == CoarseSurfaceVeins.Off)
                && !(z.Pioneer && !PioneerSurfaceIron && info.Item == IronItem);

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
                    if (info.Counts[pool] <= 0 || (pool == 0 && !SurfaceAllowed(z, info)))
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
                var start = cave ? CaveStart(z.Id, cell, random) : SurfaceStart(z, info.Definition, cell, random);
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

        /// <summary>
        /// Surface vein start (0.695 FUN_143fdc6a0 = 0.589 FUN_143f187b0): a random x / z in the pool cell, the ground height
        /// there (the client asks its terrain, VeinProvider+0x38 virtual +0x58), refused unless it lies in the cell's 16 m
        /// span, plus SURFACE_OFFSET (0 for every v2 vein) × −10; pointing into the ground (−normal). Where the server's
        /// ground is only the surface cache's estimate (<see cref="Zone.CoarseGround"/>), the start then goes down to the
        /// cache's floor (PioneerSurfaceVeins Buried), so the vein lies in rock the client certainly has: at the estimate
        /// (1029.0 m on the Pioneer plateau) its top ore stood as a block on the grass wherever the drawn ground is lower
        /// (1027.4-1029.5 m measured; 0.7-0.8 m proud at the 2026-11-01 dig, 2-3 m reported).
        /// </summary>
        private static ((double, double, double), (double, double, double))? SurfaceStart(Zone z, VeinGenerator.Definition d, ulong cell, VeinGenerator.SpawnRandom random)
        {
            uint zone = z.Id;
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
            if (z.CoarseGround)
            {
                if (SurfaceCache.FloorAt(gameDirectory, lx, lz, zone) is not float floor)
                    return null;
                gy = Math.Min(gy, floor * 10.0 + origin.Y);
            }
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

        /// <summary>
        /// Is the client's generated world certainly solid at a global point (a voxel centre)? Below the ground and not cave
        /// air. The ground is the baked one (a 2 m grid of the client's own ground), or where the server only has the surface
        /// cache's estimate (<see cref="Zone.CoarseGround"/>) the cache's floor, under which the client's ground never lies
        /// (<see cref="SurfaceCache.FloorAt"/>): an ore voxel is solid on the client whatever it generated, so a voxel the
        /// server only guesses is rock must stay plain (the client's own vein code repaints only voxels its generated block
        /// has solid, FUN_143fdb110). A coarse-ground land without its surface cache (a trimmed install) uses the estimate
        /// as before, so its cave veins keep their ore (it has no surface veins then: <see cref="SurfaceStart"/>).
        /// </summary>
        private static bool GeneratedSolid(Zone z, double gx, double gy, double gz, Dictionary<(int, int), float?> heights)
        {
            var origin = TerrainStore.ZoneOrigin;
            var key = ((int)Math.Floor(gx / VeinGenerator.VoxelSize), (int)Math.Floor(gz / VeinGenerator.VoxelSize));
            if (!heights.TryGetValue(key, out float? h))
            {
                float lx = (float)((gx - origin.X) / 10), lz = (float)((gz - origin.Z) / 10);
                heights[key] = h = z.HasSurfaceCache ? SurfaceCache.FloorAt(gameDirectory, lx, lz, z.Id) : TerrainHeights.HeightAt(lx, lz, z.Id);
            }
            return h is float ground && gy < ground * 10 + origin.Y && !CavePieces.IsAir(gx, gy, gz, z.Id);
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
                var ore = VeinGenerator.ApplyCell(vein.Info.Definition, vein.Points, c, (x, y, zz) => GeneratedSolid(z,
                    ox + (x + 0.5) * VeinGenerator.VoxelSize, oy + (y + 0.5) * VeinGenerator.VoxelSize, oz + (zz + 0.5) * VeinGenerator.VoxelSize, heights));
                if (ore.Count == 0)
                    continue;
                if (!z.Cells.TryGetValue(id, out OreCell cell))
                    z.Cells[id] = cell = new OreCell { Version = z.VersionBase + 1 };
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
                            (x, y, zz) => GeneratedSolid(z, ox + (x + 0.5) * VeinGenerator.VoxelSize, oy + (y + 0.5) * VeinGenerator.VoxelSize,
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

        /// <summary>
        /// Adds the ore under a cell's edits: voxels the overlay gives no material get flag 1 + the ore material (a
        /// vertex-only voxel of a dug hole's wall keeps its vertex: the ore is the ground there).
        /// </summary>
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
                    if ((blocks.Flags[index] & 1) == 0)
                    {
                        blocks.Flags[index] |= 1;
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
