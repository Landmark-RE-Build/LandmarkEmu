using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Shared.GameTable.Text;
using Microsoft.Extensions.Configuration;
using NLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// What the ground is made of, from the same data the client generates terrain with (read from the player's own
    /// game install, never modified): the continent biome bitmap (CommonData/Voxel/materials/textures/
    /// Continent_BiomeIds_[continent].bmp, blue channel = biome id, the zone origin at the image centre, 6.4 m per
    /// pixel for an 8 km continent) → VoxelBiomeMaterials → procedural material names → resource item.
    /// </summary>
    public static class TerrainMaterials
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const uint DirtItem = 44, SandItem = 45, StoneItem = 46, IceItem = 76;

        /// <summary>A landscape's biome map (blue channel of Continent_BiomeIds_&lt;id&gt;.bmp).</summary>
        private sealed class BiomeMap
        {
            public byte[] Biomes;
            public int Width, Height;
            public double MetresPerPixel;
        }

        private static string gameDirectory;
        /// <summary>A landscape's width in metres (Landscapes: StaticZoneData extents), or null when unknown.</summary>
        private static Func<uint, float?> landscapeWidth;
        private static readonly object sync = new();
        private static readonly Dictionary<uint, BiomeMap> maps = new();
        private static readonly Dictionary<int, string> biomeNames = new();
        private static readonly Dictionary<int, uint[]> biomeYields = new();

        /// <summary>
        /// The BiomeIds bitmap a landscape is generated with (the world server's Landscapes sets it): its own
        /// Continent_BiomeIds_&lt;zone&gt;.bmp, except the event islands 460-462, which have none and use the template
        /// island's (427 or 437, trace/0695/event-islands-695.md).
        /// </summary>
        public static Func<uint, string> BiomeBitmapOf { get; set; } = zone => $"Continent_BiomeIds_{zone}.bmp";

        /// <summary>Loads the biome map for a continent. Missing files leave every lookup at Dirt.</summary>
        /// <summary>Loads the biome tables and the home continent's map; other landscapes' maps load on first use.</summary>
        /// <param name="landscapeWidth">A landscape's width in metres (the world server passes Landscapes' extents);
        /// asked when a map is first read, 8000 m when it gives none.</param>
        public static void Load(string gameDirectory, uint continent, double continentSize = 8000, Func<uint, float?> landscapeWidth = null)
        {
            TerrainMaterials.gameDirectory = gameDirectory;
            TerrainMaterials.landscapeWidth = landscapeWidth;
            try
            {
                string gameData = SharedConfiguration.Configuration.GetValue("GameDataPath", "gameData");
                LoadBiomeTables(gameData);
            }
            catch (Exception e)
            {
                log.Warn($"Biome tables not read ({e.Message}).");
            }
            BiomeMap home = MapOf(continent);
            if (home != null)
                log.Info($"Biome map {home.Width}×{home.Height} for continent {continent} ({biomeYields.Count} biomes with yields).");
        }

        /// <summary>A landscape's biome map (8000 m across, as StaticZoneData's extents), or null without the bitmap.</summary>
        private static BiomeMap MapOf(uint zone)
        {
            lock (sync)
            {
                if (maps.TryGetValue(zone, out BiomeMap map))
                    return map;
                try
                {
                    string bmp = Path.Combine(gameDirectory ?? "", "CommonData", "Voxel", "materials", "textures",
                        BiomeBitmapOf(zone));
                    map = ReadBmpBlue(bmp);
                    map.MetresPerPixel = (landscapeWidth?.Invoke(zone) ?? 8000f) / map.Width;
                }
                catch (Exception e)
                {
                    log.Warn($"No biome map for landscape {zone} ({e.Message}).");
                    map = null;
                }
                maps[zone] = map;
                return map;
            }
        }

        /// <summary>Biome id at a local position on a landscape (home continent by default; 0 if unknown).</summary>
        public static int BiomeAt(Vector3 local, uint zone = 0)
        {
            BiomeMap m = MapOf(zone == 0 ? TerrainStore.HomeContinent : zone);
            if (m == null)
                return 0;
            int px = (int)Math.Floor(m.Width / 2.0 + local.X / m.MetresPerPixel);
            int pz = (int)Math.Floor(m.Height / 2.0 + local.Z / m.MetresPerPixel);
            if (px < 0 || pz < 0 || px >= m.Width || pz >= m.Height)
                return 0;
            return m.Biomes[pz * m.Width + px];
        }

        public static string BiomeName(int biome) => biomeNames.TryGetValue(biome, out string name) ? name : $"biome {biome}";

        /// <summary>
        /// The resource a swing at this point gives. The surface material mix of the biome decides it (each swing picks
        /// one of the biome's resource kinds); deeper than <see cref="StoneDepth"/> under the generated ground
        /// (<see cref="TerrainHeights"/>, or the swinger's feet without it) it is Stone.
        /// </summary>
        public static uint YieldAt(Vector3 target, Vector3 playerFeet, Random random, uint zone = 0)
        {
            float surface = TerrainHeights.HeightAt(target.X, target.Z, zone) ?? playerFeet.Y;
            if (surface - target.Y > StoneDepth)
                return StoneItem;
            int biome = BiomeAt(target, zone);
            // The ground's own material where the swing lands: the biome's procedural materials whose slope range holds
            // the local slope (the client paints cliffs and flats by slope the same way), one of them at random.
            if (SlopeAt(target, zone) is float up && biomeSurfaces.TryGetValue(biome, out Surface[] surfaces))
            {
                Surface[] fitting = surfaces.Where(sf => up >= sf.AngleMin - 0.02f && up <= sf.AngleMax + 0.02f).ToArray();
                if (fitting.Length > 0)
                    return fitting[random.Next(fitting.Length)].Item;
            }
            if (!biomeYields.TryGetValue(biome, out uint[] yields) || yields.Length == 0)
                return DirtItem;
            return yields[random.Next(yields.Length)];
        }

        /// <summary>A procedural ground material of a biome: the slope range it covers, its resource and voxel material.</summary>
        private sealed record Surface(float AngleMin, float AngleMax, uint Item, ushort Material);

        private static readonly Dictionary<int, Surface[]> biomeSurfaces = new();

        /// <summary>
        /// The up component of the generated ground's normal at a point (1 = flat), from the baked height grid around it;
        /// null without heights there.
        /// </summary>
        public static float? SlopeAt(Vector3 local, uint zone = 0)
        {
            const float d = 0.8f;
            if (TerrainHeights.HeightAt(local.X - d, local.Z, zone) is not float x0 || TerrainHeights.HeightAt(local.X + d, local.Z, zone) is not float x1
                || TerrainHeights.HeightAt(local.X, local.Z - d, zone) is not float z0 || TerrainHeights.HeightAt(local.X, local.Z + d, zone) is not float z1)
                return null;
            var normal = Vector3.Normalize(new Vector3(x0 - x1, 2 * d, z0 - z1));
            return normal.Y;
        }

        private static readonly Dictionary<int, (ushort Floor, ushort Wall)> caveMaterials = new();

        /// <summary>Per biome: resource item → the voxel material (ARRAY_INDEX) of the biome's ground that yields it.</summary>
        private static readonly Dictionary<int, Dictionary<uint, ushort>> biomeWalls = new();

        /// <summary>
        /// The voxel material a dug hole's walls get (TerrainEdits shell): the local biome's own ground material for
        /// what the swing yields (its sand for sand, its rock for stone…), so the hole matches the ground around it.
        /// Null when the biome has no material for that resource.
        /// </summary>
        public static ushort? WallMaterialAt(Vector3 local, uint yieldItem, uint zone = 0)
            => biomeWalls.TryGetValue(BiomeAt(local, zone), out var walls) && walls.TryGetValue(yieldItem, out ushort m) ? m : null;

        /// <summary>
        /// The voxel material (ARRAY_INDEX) the generated ground of the local biome has at a surface slope
        /// <paramref name="up"/> (the normal's up component, 1 = flat): the first of the biome's procedural materials
        /// (VoxelBiomeMaterials order) whose ANGLE_MIN..ANGLE_MAX holds it, as the client paints flats and cliffs by
        /// slope; else the biome's dirt (<see cref="WallMaterialAt"/>), else 297 (the forest dirt the dig shell falls back
        /// to). Used for ground the server adds itself (the Spire's fill), so it matches the ground around it. The baked
        /// heights' surface codes (TerrainHeights.SurfaceMaterialAt) are not used: they are not VoxelMaterials ids.
        /// </summary>
        public static ushort GroundMaterialAt(Vector3 local, float up, uint zone = 0)
        {
            int biome = BiomeAt(local, zone);
            if (biomeSurfaces.TryGetValue(biome, out Surface[] surfaces))
                foreach (Surface s in surfaces)
                    if (s.Material != 0 && up >= s.AngleMin - 0.02f && up <= s.AngleMax + 0.02f)
                        return s.Material;
            return WallMaterialAt(local, DirtItem, zone) ?? 297;
        }

        /// <summary>Every biome's cave floor and wall material.</summary>
        public static HashSet<ushort> AllCaveMaterials()
        {
            var all = new HashSet<ushort>();
            foreach (var (floor, wall) in caveMaterials.Values)
            {
                all.Add(floor);
                all.Add(wall);
            }
            return all;
        }

        /// <summary>The voxel materials of dug-out ground at a point (the biome's cave floor and wall materials).</summary>
        public static (ushort Floor, ushort Wall) CaveMaterialsAt(Vector3 local)
            => caveMaterials.TryGetValue(BiomeAt(local), out var m) ? m : ((ushort)44, (ushort)45);

        /// <summary>Digging this far below the ground surface reaches stone, metres.</summary>
        private const float StoneDepth = 4f;

        /// <summary>
        /// Reads VoxelBiomes, VoxelBiomeMaterials and VoxelProceduralMaterials to map each biome to its procedural material
        /// names.
        /// </summary>
        private static void LoadBiomeTables(string gameData)
        {
            Dictionary<int, string[]> Read(string file) => File.ReadLines(Path.Combine(gameData, file))
                .Where(l => l.Length > 0 && l[0] != '#')
                .Select(l => l.Split('^'))
                .Where(f => f.Length > 2 && int.TryParse(f[0], out _))
                .ToDictionary(f => int.Parse(f[0]));

            var biomeRows = Read("VoxelBiomes.txt");
            foreach ((int id, string[] f) in biomeRows)
            {
                biomeNames[id] = f[1];
                // CAVE_FLOOR_MATERIAL_ID / CAVE_WALL_MATERIAL_ID (columns 13, 14): what dug-out ground is made of.
                if (f.Length > 14 && ushort.TryParse(f[13], out ushort floor) && ushort.TryParse(f[14], out ushort wall))
                    caveMaterials[id] = (floor, wall);
            }
            var procNames = Read("VoxelProceduralMaterials.txt")
                .ToDictionary(p => p.Key, p => TextManager.Instance.GetTextForId(uint.Parse(p.Value[1])) ?? "");
            // Procedural material → its first voxel material, as the ARRAY_INDEX block data uses
            // (VoxelProceduralMaterialMaterials → VoxelMaterials).
            var arrayIndex = Read("VoxelMaterials.txt").ToDictionary(m => m.Key, m => ushort.TryParse(m.Value[1], out ushort a) ? a : (ushort)0);
            var procMaterial = new Dictionary<int, ushort>();
            foreach (string[] f in Read("VoxelProceduralMaterialMaterials.txt").Values)
                if (int.TryParse(f[1], out int proc) && int.TryParse(f[2], out int mat) && !procMaterial.ContainsKey(proc)
                    && arrayIndex.TryGetValue(mat, out ushort index) && index != 0)
                    procMaterial[proc] = index;
            // ANGLE_MIN / ANGLE_MAX (columns 2, 3): the surface slope (normal's up component) a procedural material
            // covers, e.g. OceanCliff 0-0.7 (steep), Ocean Beach 0.7-1 (flat).
            var procAngles = Read("VoxelProceduralMaterials.txt").ToDictionary(p => p.Key, p =>
                (float.TryParse(p.Value[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float lo) ? lo : 0f,
                 float.TryParse(p.Value[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float hi) ? hi : 1f));
            foreach (var group in Read("VoxelBiomeMaterials.txt").Values.GroupBy(f => int.Parse(f[1])))
            {
                var yields = new List<uint>();
                var walls = new Dictionary<uint, ushort>();
                var surfaces = new List<Surface>();
                foreach (string[] f in group)
                {
                    int proc = int.Parse(f[2]);
                    procNames.TryGetValue(proc, out string name);
                    uint item = ItemForMaterialName(name ?? "");
                    yields.Add(item);
                    procMaterial.TryGetValue(proc, out ushort material);
                    if (!walls.ContainsKey(item) && material != 0)
                        walls[item] = material;
                    (float lo, float hi) = procAngles.TryGetValue(proc, out var a) ? a : (0f, 1f);
                    surfaces.Add(new Surface(lo, hi, item, material));
                }
                biomeYields[group.Key] = yields.ToArray();
                biomeWalls[group.Key] = walls;
                biomeSurfaces[group.Key] = surfaces.ToArray();
            }
        }

        /// <summary>Resource item for a voxel material, by its name (only Dirt, Sand, Stone and Ice are gatherable).</summary>
        public static uint ItemForMaterialName(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("sand") || n.Contains("beach"))
                return SandItem;
            if (n.Contains("ice") || n.Contains("snow"))
                return IceItem;
            if (n.Contains("stone") || n.Contains("rock") || n.Contains("granite") || n.Contains("basalt")
                || n.Contains("obsidian") || n.Contains("cliff") || n.Contains("lava") || n.Contains("volcan"))
                return StoneItem;
            return DirtItem;
        }

        /// <summary>Reads the blue channel of a 24/32-bit BMP (bottom-up rows) into a top-down byte array.</summary>
        private static BiomeMap ReadBmpBlue(string path)
        {
            byte[] d = File.ReadAllBytes(path);
            int offset = BitConverter.ToInt32(d, 10);
            int width = BitConverter.ToInt32(d, 18);
            int h = BitConverter.ToInt32(d, 22);
            int bpp = BitConverter.ToInt16(d, 28);
            bool bottomUp = h > 0;
            int height = Math.Abs(h);
            int bytesPerPixel = bpp / 8;
            int stride = (width * bytesPerPixel + 3) & ~3;
            var biomes = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                int row = bottomUp ? height - 1 - y : y;
                for (int x = 0; x < width; x++)
                    biomes[y * width + x] = d[offset + row * stride + x * bytesPerPixel]; // B of BGR
            }
            return new BiomeMap { Biomes = biomes, Width = width, Height = height };
        }
    }
}
