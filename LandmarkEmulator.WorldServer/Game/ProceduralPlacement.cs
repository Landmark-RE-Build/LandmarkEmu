using LandmarkEmulator.WorldServer.Game.Terrain;
using NLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The live server's procedural placement rules, as the client ships them (trace/0695/procedural-placement-695.md):
    /// gameData ProceduralPlacementSets (where and how densely), ProceduralPlacementSetEntities (which PropDefs prop,
    /// with a SPAWN_CHANCE weight), ProceduralPlacementSetsSetEntities (set → entity links) and
    /// ProceduralPlacementSetMaterials (the ground materials a set grows on), plus the MASK bitmaps in the install's
    /// CommonData\ProceduralPlacementTool\Masks (read only). The client never loads any of these [P]: they were inputs of
    /// Daybreak's offline ProceduralPlacementTool and of the server's spawner.
    /// <para>
    /// The shipped sets are keyed to the alpha-era biomes (VoxelBiomes 18-31: Desert / Tropical / OldGrowthForest /
    /// Tundra _Small/_Medium/_Large_001, used by the "Alpha_*" world settings) [P]; the launch landscapes use the surface
    /// biomes 47-52 / 54 ("OpenBeta_*" world settings) [P], for which no set ships. The bridge is BIOME_TYPE_ID (same type,
    /// e.g. 50 OldGrowthForestSurface and 22 OldGrowthForest_Large_001 are both type 4) and the shared ground materials
    /// (biome 50's surface is made of the same VoxelMaterials the OldGrowth sets list) [I]: a surface biome uses the sets of
    /// its type's _Large_001 biome. Volcanic (type 6), Deciduous (5: only the test set 170) and Ocean (7) have no sets.
    /// </para>
    /// <para>
    /// Placement is deterministic per landscape and position (a hash of landscape, set and grid cell), so a region is the
    /// same after every restart and its two neighbours agree on what lies near their border.
    /// </para>
    /// </summary>
    public static class ProceduralPlacement
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>What a set entity is to the gathering code: an axe tree, a sickle plant or decoration.</summary>
        public enum EntityKind { Tree, Plant, Decor }

        /// <summary>A ProceduralPlacementSetEntities row with its model (PropDefs prop → Models.txt id).</summary>
        public sealed class SetEntity
        {
            public uint Id, PropId, Model;
            public float Weight;                      // SPAWN_CHANCE (a relative weight within the set) [I]
            public int MaxSpawns;                     // MAX_SPAWNS (10000 on every row; per zone, never reached)
            public Vector3 Offset;                    // OFFSET_X/Y/Z, read as centimetres [I] (OFFSET_Y down to -50)
            public float Heading, Pitch, Roll;        // OFFSET_HEADING/PITCH/ROLL, degrees (all 0 in the data)
            public float Scale;                       // SCALE, times the set's random scale
            public EntityKind Kind;
            public float Footprint;                   // Models.txt PATH_BLOCK_RADIUS (m), 0.5 when none
        }

        /// <summary>A ProceduralPlacementSets row.</summary>
        public sealed class PlacementSet
        {
            public int Id, Group, BiomeId, BiomeType, Encroachment, MaxSpawns;
            public float MinAngle, MaxAngle, MinAltitude, MaxAltitude, MinRespawn, MaxRespawn;
            public Vector3 MinOffset, MaxOffset;
            public float MinHeading, MaxHeading, MinPitch, MaxPitch, MinRoll, MaxRoll, MinScale, MaxScale;
            public float AverageDistance, MinimumDistance;
            public bool OrientToSurface;
            public string MaskName;
            public float MaskScale;
            /// <summary>Ground material (VoxelMaterials ARRAY_INDEX, as the baked height grid reports it) → SOIL_SUITABILITY.</summary>
            public Dictionary<ushort, float> Soil = new();
            public SetEntity[] Entities = Array.Empty<SetEntity>();
            public float TotalWeight;

            /// <summary>A respawn delay between MIN_RESPAWN_MINUTES and MAX_RESPAWN_MINUTES.</summary>
            public TimeSpan Respawn(Random random) =>
                TimeSpan.FromMinutes(MinRespawn + (MaxRespawn - MinRespawn) * random.NextDouble());
        }

        /// <summary>One placed entity: the set and entity it came from and its transform (local metres).</summary>
        public sealed record Placement(PlacementSet Set, SetEntity Entity, Vector3 Position, Quaternion Rotation, float Scale);

        /// <summary>Setting "ProceduralPlacement" (default true): trees on the set biomes come from the sets.</summary>
        public static bool Enabled { get; private set; }
        /// <summary>Setting "ProceduralPlacementDecor" (default false): also place the sets' rocks, bushes and flowers.</summary>
        public static bool Decor { get; private set; }
        /// <summary>Setting "ProceduralPlacementMasks" (default true): apply the MASK bitmaps.</summary>
        public static bool Masks { get; private set; }

        public static bool Loaded => activeSets.Length > 0;
        public static IReadOnlyList<PlacementSet> Sets => activeSets;

        /// <summary>
        /// Placement data groups 17 (set 170: biome 32, mask TilingTestZZ.bmp, never shipped) and 18 (sets 171-172: altitude
        /// ±1,000,000, MAX_SPAWNS 100, respawn 1-5 min, biome 10 "OLD_Tropical") are developer tests [I] and are not used.
        /// </summary>
        private static readonly HashSet<int> TestGroups = new() { 17, 18 };

        /// <summary>Models whose .adr names no collision mesh: players walk through them (MODLOG 2026-10-09).</summary>
        private static readonly HashSet<uint> NoCollision = new() { 344, 350 };

        /// <summary>Global metres = local metres + this (TerrainStore.ZoneOrigin.Y is 20000 dm): the frame of
        /// MIN/MAX_SITE_ALTITUDE, 3000-4000 on every set = local 1000-2000, i.e. above the sea at local ~1002 [I].</summary>
        private static float AltitudeOffset => (float)(TerrainStore.ZoneOrigin.Y * 0.1);

        /// <summary>Half a landscape: mask pixels count from the landscape's corner (local −4000, −4000) [I].</summary>
        private const float MaskOrigin = 4000f;
        /// <summary>Masks are kept at most this many pixels across (box-averaged; 4096² masks would take 16 MB each).</summary>
        private const int MaskMaxSize = 1024;
        /// <summary>How far past a region's edge candidates are evaluated, so encroachment sees its neighbours (m).</summary>
        private const float Margin = 16f;

        private static PlacementSet[] activeSets = Array.Empty<PlacementSet>();
        /// <summary>Surface biome (VoxelBiomes id) → BIOME_TYPE_ID.</summary>
        private static readonly Dictionary<int, int> biomeTypes = new();
        /// <summary>BIOME_TYPE_ID → the sets used for it.</summary>
        private static readonly Dictionary<int, PlacementSet[]> byType = new();
        private static readonly Dictionary<string, Mask> masks = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object maskSync = new();
        private static string maskFolder;

        /// <summary>
        /// Reads the four tables (gameData), VoxelBiomes / VoxelMaterials (biome types, material array indexes) and the
        /// props' models (<see cref="Props.ModelOf"/>, loaded before this). Masks load on first use.
        /// </summary>
        public static void Load(string gameData, string gameDirectory, bool enabled, bool decor, bool useMasks)
        {
            Enabled = enabled;
            Decor = decor;
            Masks = useMasks;
            maskFolder = Path.Combine(gameDirectory ?? "", "CommonData", "ProceduralPlacementTool", "Masks");
            try
            {
                LoadTables(gameData);
            }
            catch (Exception e)
            {
                log.Warn($"Procedural placement sets not read ({e.Message}); the old tree lists are used everywhere.");
                activeSets = Array.Empty<PlacementSet>();
                byType.Clear();
            }
            log.Info($"Procedural placement: {activeSets.Length} sets for biome types "
                + string.Join(", ", byType.Select(t => $"{t.Key} ({t.Value.Length})"))
                + $"; {(Enabled ? "on" : "off")}, decoration {(Decor ? "on" : "off")}, masks {(Masks ? "on" : "off")}.");
            // Read the masks in the background (up to 50 MB each on disk, 1 MB kept), so the first region that needs one
            // doesn't wait for it inside the gathering tick.
            if (Enabled && Masks)
            {
                string[] names = activeSets.Where(s => s.Entities.Any(e => e.Kind == EntityKind.Tree || e.Kind == EntityKind.Decor && Decor))
                    .Select(s => s.MaskName).Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                System.Threading.Tasks.Task.Run(() =>
                {
                    foreach (string name in names)
                        MaskOf(name);
                });
            }
        }

        /// <summary>A table as rows of column name → value (the client's "#*" header line names the columns).</summary>
        private static List<Dictionary<string, string>> Table(string gameData, string name)
        {
            var rows = new List<Dictionary<string, string>>();
            string[] header = null;
            foreach (string line in File.ReadLines(Path.Combine(gameData, name)))
            {
                if (line.StartsWith("#*"))
                {
                    header = line[2..].Split('^');
                    continue;
                }
                if (line.Length == 0 || line[0] == '#' || header == null)
                    continue;
                string[] f = line.Split('^');
                var row = new Dictionary<string, string>();
                for (int i = 0; i < header.Length && i < f.Length; i++)
                    row[header[i]] = f[i];
                rows.Add(row);
            }
            return rows;
        }

        private static float F(Dictionary<string, string> row, string column) =>
            row.TryGetValue(column, out string s) && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;

        private static int I(Dictionary<string, string> row, string column) =>
            row.TryGetValue(column, out string s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

        private static void LoadTables(string gameData)
        {
            var biomeNames = new Dictionary<int, string>();
            foreach (var row in Table(gameData, "VoxelBiomes.txt"))
            {
                biomeTypes[I(row, "ID")] = I(row, "BIOME_TYPE_ID");
                biomeNames[I(row, "ID")] = row.GetValueOrDefault("NAME", "");
            }
            // SetMaterials name VoxelMaterials ids; the height grid's surface codes are their ARRAY_INDEX (biome 50's ground
            // reports 303 / 295 / 304 … = Ancient Forest Clovers Lowland / Grass Midland / Clovers Lowland Dry …) [P].
            var arrayIndex = Table(gameData, "VoxelMaterials.txt")
                .ToDictionary(r => I(r, "ID"), r => (ushort)I(r, "ARRAY_INDEX"));

            var entities = new Dictionary<int, SetEntity>();
            foreach (var row in Table(gameData, "ProceduralPlacementSetEntities.txt"))
            {
                uint prop = (uint)I(row, "PROP_ID");
                uint model = Props.ModelOf(prop);
                if (model == 0 || NoCollision.Contains(model))
                    continue;   // no model for the prop, or one players would walk through
                string file = ModelFile(model);
                entities[I(row, "ID")] = new SetEntity
                {
                    Id = (uint)I(row, "ID"),
                    PropId = prop,
                    Model = model,
                    Weight = Math.Max(0f, F(row, "SPAWN_CHANCE")),
                    MaxSpawns = I(row, "MAX_SPAWNS"),
                    Offset = new Vector3(F(row, "OFFSET_X"), F(row, "OFFSET_Y"), F(row, "OFFSET_Z")) * 0.01f,
                    Heading = F(row, "OFFSET_HEADING"),
                    Pitch = F(row, "OFFSET_PITCH"),
                    Roll = F(row, "OFFSET_ROLL"),
                    Scale = F(row, "SCALE") > 0f ? F(row, "SCALE") : 1f,
                    // Model names: Biome_<biome>_Trees_* are trees (pines, firs, banyans, palms, cacti, dead trees and logs),
                    // Biome_Generic_Plants_* the sickle plants (Amberleaf, Cotton…), everything else (rocks, shrubs,
                    // bushes, flowers, fungal mounds, succulents) decoration [I].
                    Kind = file.Contains("_Trees_", StringComparison.OrdinalIgnoreCase) ? EntityKind.Tree
                        : file.Contains("_Generic_Plants_", StringComparison.OrdinalIgnoreCase) ? EntityKind.Plant
                        : EntityKind.Decor,
                    Footprint = ModelFootprints.Of(model)?.Radius ?? 0.5f
                };
            }

            var links = Table(gameData, "ProceduralPlacementSetsSetEntities.txt")
                .GroupBy(r => I(r, "SETS_ID"))
                .ToDictionary(g => g.Key, g => g.Select(r => I(r, "SET_ENTITIES_ID")).ToList());
            var soil = Table(gameData, "ProceduralPlacementSetMaterials.txt").GroupBy(r => I(r, "SET_ID"))
                .ToDictionary(g => g.Key, g => g.ToList());

            var all = new List<PlacementSet>();
            foreach (var row in Table(gameData, "ProceduralPlacementSets.txt"))
            {
                var set = new PlacementSet
                {
                    Id = I(row, "ID"),
                    Group = I(row, "PLACEMENT_DATA_GROUP_ID"),
                    BiomeId = I(row, "BIOME_ID"),
                    MinAngle = F(row, "MIN_SITE_ANGLE"),
                    MaxAngle = F(row, "MAX_SITE_ANGLE"),
                    MinAltitude = F(row, "MIN_SITE_ALTITUDE"),
                    MaxAltitude = F(row, "MAX_SITE_ALTITUDE"),
                    MaxSpawns = I(row, "MAX_SPAWNS"),
                    MinRespawn = F(row, "MIN_RESPAWN_MINUTES"),
                    MaxRespawn = Math.Max(F(row, "MIN_RESPAWN_MINUTES"), F(row, "MAX_RESPAWN_MINUTES")),
                    MinOffset = new Vector3(F(row, "MIN_OFFSET_X"), F(row, "MIN_OFFSET_Y"), F(row, "MIN_OFFSET_Z")),
                    MaxOffset = new Vector3(F(row, "MAX_OFFSET_X"), F(row, "MAX_OFFSET_Y"), F(row, "MAX_OFFSET_Z")),
                    MinHeading = F(row, "MIN_OFFSET_HEADING"),
                    MaxHeading = F(row, "MAX_OFFSET_HEADING"),
                    MinPitch = F(row, "MIN_OFFSET_PITCH"),
                    MaxPitch = F(row, "MAX_OFFSET_PITCH"),
                    MinRoll = F(row, "MIN_OFFSET_ROLL"),
                    MaxRoll = F(row, "MAX_OFFSET_ROLL"),
                    MinScale = F(row, "MIN_SCALE"),
                    MaxScale = F(row, "MAX_SCALE"),
                    AverageDistance = F(row, "AVERAGE_DISTANCE"),
                    MinimumDistance = F(row, "MINIMUM_DISTANCE"),
                    OrientToSurface = I(row, "ORIENT_TO_SURFACE") != 0,
                    MaskName = row.GetValueOrDefault("MASK", ""),
                    MaskScale = F(row, "MASK_SCALE"),
                    Encroachment = I(row, "ENCROACHMENT_PRIORITY")
                };
                set.BiomeType = biomeTypes.GetValueOrDefault(set.BiomeId);
                if (set.AverageDistance < 1f || set.MinScale <= 0f)
                    continue;
                if (soil.TryGetValue(set.Id, out var materials))
                    foreach (var m in materials)
                        if (arrayIndex.TryGetValue(I(m, "MATERIAL_ID"), out ushort index) && index != 0)
                            set.Soil[index] = F(m, "SOIL_SUITABILITY");
                if (links.TryGetValue(set.Id, out var ids))
                    set.Entities = ids.Where(entities.ContainsKey).Select(id => entities[id]).Where(e => e.Weight > 0f).ToArray();
                set.TotalWeight = set.Entities.Sum(e => e.Weight);
                if (set.Entities.Length > 0)
                    all.Add(set);
            }

            // Per biome type, the sets of its _Large_001 biome (Tropical 31, Desert 20, OldGrowthForest 22, Tundra 26) [I]:
            // the Small / Medium copies differ only in a few distances and respawn times.
            byType.Clear();
            var chosen = new List<PlacementSet>();
            foreach (var type in all.Where(s => !TestGroups.Contains(s.Group) && s.BiomeType != 0).GroupBy(s => s.BiomeType))
            {
                int biome = type.Select(s => s.BiomeId).Distinct()
                    .Where(b => !biomeNames.GetValueOrDefault(b, "").StartsWith("OLD_", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(b => biomeNames.GetValueOrDefault(b, "").Contains("_Large_", StringComparison.OrdinalIgnoreCase))
                    .ThenBy(b => b).FirstOrDefault();
                // Higher ENCROACHMENT_PRIORITY claims its ground first [I].
                PlacementSet[] sets = type.Where(s => s.BiomeId == biome).OrderByDescending(s => s.Encroachment).ThenBy(s => s.Id).ToArray();
                if (sets.Length == 0)
                    continue;
                byType[type.Key] = sets;
                chosen.AddRange(sets);
            }
            activeSets = chosen.ToArray();
        }

        private static Dictionary<uint, string> modelFiles;

        /// <summary>Models.txt MODEL_FILE_NAME of a model id ("" if unknown).</summary>
        private static string ModelFile(uint model)
        {
            if (modelFiles == null)
            {
                modelFiles = new Dictionary<uint, string>();
                try
                {
                    string path = Path.Combine(Landscapes.GameDirectory ?? "", "Resources", "Models.txt");
                    foreach (string line in File.ReadLines(path))
                    {
                        string[] f = line.Split('^');
                        if (f.Length > 1 && uint.TryParse(f[0], out uint id))
                            modelFiles[id] = f[1];
                    }
                }
                catch (Exception e)
                {
                    log.Warn($"Models.txt not read for procedural placement ({e.Message}).");
                }
            }
            return modelFiles.GetValueOrDefault(model, "");
        }

        /// <summary>Does a surface biome take its trees from the sets (placement on and sets for its type)?</summary>
        public static bool Covers(int biome) =>
            Enabled && biomeTypes.TryGetValue(biome, out int type) && byType.ContainsKey(type);

        /// <summary>The BIOME_TYPE_ID of a biome (0 when unknown).</summary>
        public static int TypeOf(int biome) => biomeTypes.GetValueOrDefault(biome);

        /// <summary>
        /// Everything the sets place in one square region (local metres; <paramref name="x0"/>/<paramref name="z0"/> its
        /// corner), in a fixed order. <paramref name="siteAllowed"/> (x, z, ground y) adds the server's own refusals
        /// (build sites, the Spire). Disabled kinds (decoration, sickle plants when <paramref name="plants"/> is false)
        /// are left out and take no room.
        /// <para>
        /// Per set: one candidate in each AVERAGE_DISTANCE grid cell (a jittered grid gives about one entity per
        /// AVERAGE_DISTANCE² m²) [I]; a candidate with a higher-priority neighbour of the same set closer than
        /// MINIMUM_DISTANCE is dropped; then the biome type, MIN/MAX_SITE_ALTITUDE (global metres), MIN/MAX_SITE_ANGLE
        /// (ground slope, degrees), the set's soil materials (SOIL_SUITABILITY as a chance), the MASK (pixel value as a
        /// chance) and room left by the sets placed before it (ENCROACHMENT_PRIORITY order; trunks may not overlap).
        /// The entity is picked by SPAWN_CHANCE weight; heading / pitch / roll / scale / offsets are random within the
        /// set's ranges, plus the entity's own offsets; ORIENT_TO_SURFACE tilts it to the ground.
        /// </para>
        /// </summary>
        public static List<Placement> Region(uint zone, float x0, float z0, float size, bool plants, Func<float, float, float, bool> siteAllowed)
        {
            var result = new List<Placement>();
            if (!Enabled || activeSets.Length == 0)
                return result;
            var room = new List<(float X, float Z, float R)>();
            float ax0 = x0 - Margin, az0 = z0 - Margin, ax1 = x0 + size + Margin, az1 = z0 + size + Margin;
            foreach (PlacementSet set in activeSets)
            {
                float avg = set.AverageDistance;
                int ci0 = (int)MathF.Floor(ax0 / avg), ci1 = (int)MathF.Floor(ax1 / avg);
                int cj0 = (int)MathF.Floor(az0 / avg), cj1 = (int)MathF.Floor(az1 / avg);
                for (int cj = cj0; cj <= cj1; cj++)
                    for (int ci = ci0; ci <= ci1; ci++)
                    {
                        ulong state = Seed(zone, set.Id, ci, cj);
                        (float x, float z, double priority) = Candidate(set, ci, cj, ref state);
                        if (x < ax0 || x >= ax1 || z < az0 || z >= az1)
                            continue;
                        if (biomeTypes.GetValueOrDefault(TerrainMaterials.BiomeAt(new Vector3(x, 0f, z), zone)) != set.BiomeType)
                            continue;
                        if (Dominated(zone, set, ci, cj, x, z, priority))
                            continue;
                        if (Site(zone, set, x, z, ref state) is not (float ground, Vector3 normal))
                            continue;
                        if (Masks && !MaskAllows(set, x, z, ref state))
                            continue;
                        SetEntity entity = Pick(set, ref state);
                        float scale = Lerp(set.MinScale, set.MaxScale, Next(ref state)) * entity.Scale;
                        float heading = Lerp(set.MinHeading, set.MaxHeading, Next(ref state)) + entity.Heading;
                        float pitch = Lerp(set.MinPitch, set.MaxPitch, Next(ref state)) + entity.Pitch;
                        float roll = Lerp(set.MinRoll, set.MaxRoll, Next(ref state)) + entity.Roll;
                        var offset = new Vector3(Lerp(set.MinOffset.X, set.MaxOffset.X, Next(ref state)),
                            Lerp(set.MinOffset.Y, set.MaxOffset.Y, Next(ref state)),
                            Lerp(set.MinOffset.Z, set.MaxOffset.Z, Next(ref state))) + entity.Offset;
                        if (entity.Kind == EntityKind.Decor && !Decor || entity.Kind == EntityKind.Plant && !plants)
                            continue;
                        if (!siteAllowed(x, z, ground))
                            continue;
                        float radius = entity.Footprint * scale;
                        bool crowded = false;
                        foreach (var (ox, oz, or) in room)
                            if ((ox - x) * (ox - x) + (oz - z) * (oz - z) < (or + radius) * (or + radius))
                            {
                                crowded = true;
                                break;
                            }
                        if (crowded)
                            continue;
                        room.Add((x, z, radius));
                        if (x < x0 || x >= x0 + size || z < z0 || z >= z0 + size)
                            continue;   // in the margin: only takes room
                        Quaternion rotation = Quaternion.CreateFromYawPitchRoll(heading * Deg, pitch * Deg, roll * Deg);
                        if (set.OrientToSurface)
                            rotation = Quaternion.Normalize(Tilt(normal) * rotation);
                        // Settled 0.3 m into the ground, as the trees were before (ours): an upright trunk on a slope
                        // otherwise shows a gap on its downhill side.
                        float settle = set.OrientToSurface ? 0f : 0.3f;
                        var position = new Vector3(x + offset.X, ground + offset.Y - settle, z + offset.Z);
                        result.Add(new Placement(set, entity, position, rotation, scale));
                    }
            }
            return result;
        }

        private const float Deg = MathF.PI / 180f;

        private static float Lerp(float a, float b, double t) => a + (b - a) * (float)t;

        /// <summary>The rotation that turns straight up into the ground normal.</summary>
        private static Quaternion Tilt(Vector3 normal)
        {
            Vector3 up = Vector3.UnitY;
            float dot = Math.Clamp(Vector3.Dot(up, normal), -1f, 1f);
            Vector3 axis = Vector3.Cross(up, normal);
            if (axis.LengthSquared() < 1e-8f)
                return Quaternion.Identity;
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.Acos(dot));
        }

        /// <summary>The candidate of a grid cell: a point in the cell and its priority (the same every time).</summary>
        private static (float X, float Z, double Priority) Candidate(PlacementSet set, int ci, int cj, ref ulong state)
        {
            float x = (ci + (float)Next(ref state)) * set.AverageDistance;
            float z = (cj + (float)Next(ref state)) * set.AverageDistance;
            return (x, z, Next(ref state));
        }

        /// <summary>
        /// MINIMUM_DISTANCE: is there a candidate of the same set nearer than that with a higher priority? Neighbours are
        /// compared before any ground check, so the answer doesn't depend on which region asks.
        /// </summary>
        private static bool Dominated(uint zone, PlacementSet set, int ci, int cj, float x, float z, double priority)
        {
            float min = set.MinimumDistance;
            if (min <= 0f)
                return false;
            int reach = (int)MathF.Ceiling(min / set.AverageDistance);
            for (int dj = -reach; dj <= reach; dj++)
                for (int di = -reach; di <= reach; di++)
                {
                    if (di == 0 && dj == 0)
                        continue;
                    ulong state = Seed(zone, set.Id, ci + di, cj + dj);
                    (float nx, float nz, double np) = Candidate(set, ci + di, cj + dj, ref state);
                    if ((nx - x) * (nx - x) + (nz - z) * (nz - z) >= min * min)
                        continue;
                    if (np > priority || np == priority && (dj > 0 || dj == 0 && di > 0))
                        return true;
                }
            return false;
        }

        /// <summary>The set's ground rules at x/z: altitude, slope, soil. Returns the ground height and normal.</summary>
        private static (float Ground, Vector3 Normal)? Site(uint zone, PlacementSet set, float x, float z, ref ulong state)
        {
            if (TerrainHeights.HeightAt(x, z, zone) is not float ground)
                return null;
            float altitude = ground + AltitudeOffset;
            if (altitude < set.MinAltitude || altitude > set.MaxAltitude)
                return null;
            const float d = 0.8f;
            if (TerrainHeights.HeightAt(x - d, z, zone) is not float hx0 || TerrainHeights.HeightAt(x + d, z, zone) is not float hx1
                || TerrainHeights.HeightAt(x, z - d, zone) is not float hz0 || TerrainHeights.HeightAt(x, z + d, zone) is not float hz1)
                return null;
            Vector3 normal = Vector3.Normalize(new Vector3(hx0 - hx1, 2 * d, hz0 - hz1));
            float angle = MathF.Acos(Math.Clamp(normal.Y, -1f, 1f)) / Deg;
            if (angle < set.MinAngle || angle > set.MaxAngle)
                return null;
            double soilRoll = Next(ref state);
            if (set.Soil.Count > 0)
            {
                // 0 = no surface code sampled there: allowed.
                ushort code = TerrainHeights.SurfaceMaterialAt(x, z, zone);
                if (code != 0 && (!set.Soil.TryGetValue(code, out float suitability) || soilRoll >= suitability))
                    return null;
            }
            return (ground, normal);
        }

        /// <summary>A set entity by SPAWN_CHANCE weight.</summary>
        private static SetEntity Pick(PlacementSet set, ref ulong state)
        {
            double roll = Next(ref state) * set.TotalWeight;
            foreach (SetEntity entity in set.Entities)
            {
                roll -= entity.Weight;
                if (roll < 0)
                    return entity;
            }
            return set.Entities[^1];
        }

        // ---- masks ----

        /// <summary>A mask bitmap, box-averaged to at most <see cref="MaskMaxSize"/>² grey values.</summary>
        private sealed class Mask
        {
            public byte[] Values;
            public int Size;          // stored pixels across
            public int SourceSize;    // the bitmap's own width
        }

        /// <summary>
        /// The MASK at x/z as a chance: pixel = (local + 4000 m) × MASK_SCALE, wrapping (the 1024² DualForests masks tile
        /// seamlessly; the 4096² ones cover 16 km at MASK_SCALE 0.25) [I]. No mask, or one not found, allows everything.
        /// </summary>
        private static bool MaskAllows(PlacementSet set, float x, float z, ref ulong state)
        {
            double roll = Next(ref state);
            if (string.IsNullOrEmpty(set.MaskName) || set.MaskScale <= 0f || MaskOf(set.MaskName) is not Mask mask)
                return true;
            int px = (int)MathF.Floor((x + MaskOrigin) * set.MaskScale), pz = (int)MathF.Floor((z + MaskOrigin) * set.MaskScale);
            px = (px % mask.SourceSize + mask.SourceSize) % mask.SourceSize;
            pz = (pz % mask.SourceSize + mask.SourceSize) % mask.SourceSize;
            int sx = (int)((long)px * mask.Size / mask.SourceSize), sz = (int)((long)pz * mask.Size / mask.SourceSize);
            return roll * 255.0 < mask.Values[sz * mask.Size + sx];
        }

        private static Mask MaskOf(string name)
        {
            lock (maskSync)
            {
                if (masks.TryGetValue(name, out Mask cached))
                    return cached;
                Mask mask = null;
                try
                {
                    mask = ReadMask(Path.Combine(maskFolder ?? "", name));
                }
                catch (Exception e)
                {
                    log.Warn($"Procedural placement mask {name} not read ({e.Message}); that set is placed without it.");
                }
                masks[name] = mask;
                return mask;
            }
        }

        /// <summary>Reads a 24/32-bit BMP's red channel (the masks are grey), averaging blocks down to <see cref="MaskMaxSize"/>.
        /// Row 0 of the result is the bitmap's top row.</summary>
        private static Mask ReadMask(string path)
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            var reader = new BinaryReader(file);
            byte[] header = reader.ReadBytes(54);
            int dataOffset = BitConverter.ToInt32(header, 10), width = BitConverter.ToInt32(header, 18), h = BitConverter.ToInt32(header, 22);
            int bytesPerPixel = BitConverter.ToInt16(header, 28) / 8;
            int height = Math.Abs(h);
            if (width <= 0 || height != width || bytesPerPixel < 3)
                throw new InvalidDataException($"unexpected bitmap {width}×{h}, {bytesPerPixel * 8} bpp");
            int factor = Math.Max(1, width / MaskMaxSize), size = width / factor;
            int stride = (width * bytesPerPixel + 3) & ~3;
            var sums = new int[size * size];
            byte[] row = new byte[stride];
            file.Position = dataOffset;
            for (int y = 0; y < height; y++)
            {
                file.ReadExactly(row);
                int top = h > 0 ? height - 1 - y : y;   // bottom-up rows when the height is positive
                int sy = Math.Min(size - 1, top / factor);
                for (int x = 0; x < width; x++)
                    sums[sy * size + Math.Min(size - 1, x / factor)] += row[x * bytesPerPixel + 2];
            }
            var values = new byte[size * size];
            int cells = factor * factor;
            for (int i = 0; i < values.Length; i++)
                values[i] = (byte)(sums[i] / cells);
            log.Info($"Procedural placement mask {Path.GetFileName(path)}: {width}² → {size}².");
            return new Mask { Values = values, Size = size, SourceSize = width };
        }

        // ---- deterministic randomness ----

        private static ulong Mix(ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }

        private static ulong Seed(uint zone, int set, int ci, int cj) =>
            Mix(Mix(Mix(((ulong)zone << 32) | (uint)set) ^ (uint)ci) ^ ((ulong)(uint)cj << 1));

        /// <summary>The next number in [0, 1) of a candidate's own sequence.</summary>
        private static double Next(ref ulong state)
        {
            state = Mix(state);
            return (state >> 11) * (1.0 / (1UL << 53));
        }
    }
}
