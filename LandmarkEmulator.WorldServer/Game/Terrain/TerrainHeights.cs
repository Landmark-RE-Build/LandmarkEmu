using LandmarkEmulator.Shared.Configuration;
using Microsoft.Extensions.Configuration;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.IO.Compression;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// Ground height and surface material of the generated (unedited) terrain, from a grid sampled out of the player's
    /// own client: the client's heightmap terrain function (ContinentMapHeightmapTerrain vtable +0x58, FUN_143fc3e00)
    /// gives exactly the ground a character stands on, so re/run_heightbake.py samples it on a 2 m grid (read-only, no
    /// game file is touched) and re/pack_heights.py writes TerrainHeights[continent].bin into the game data folder.
    /// File: "LMH1", u32 size, f32 step, f32 minX, f32 minZ, f32 baseY, f32 unit, then deflate(u16 heights, 0xFFFF =
    /// none) and deflate(u16 surface codes the sampler reports), row-major with z as the row.
    /// </summary>
    public static class TerrainHeights
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private const ushort None = ushort.MaxValue;
        /// <summary>A landscape's grid is dropped after this long unused (the home continent's is kept).</summary>
        private static readonly TimeSpan Idle = TimeSpan.FromMinutes(10);

        /// <summary>One landscape's grid (about 32 MB in memory).</summary>
        private sealed class Grid
        {
            public ushort[] Heights, Materials;
            public int Size;
            public float Step, MinX, MinZ, BaseY, Unit;
            public DateTime LastUsed;
        }

        private static readonly object sync = new();
        private static readonly Dictionary<uint, Grid> grids = new();
        private static readonly HashSet<uint> missing = new();
        private static uint Home => TerrainStore.HomeContinent;

        /// <summary>True when the home continent's grid is loaded.</summary>
        public static bool Loaded => Has(Home);

        /// <summary>True when a landscape has a baked grid (loads it on first use).</summary>
        public static bool Has(uint zone) => GridOf(zone) != null;

        /// <summary>Is there a baked grid for a landscape on disk (without loading it)?</summary>
        public static bool Available(uint zone)
        {
            lock (sync)
                if (grids.ContainsKey(zone))
                    return true;
            return File.Exists(Path.Combine(SharedConfiguration.Configuration.GetValue("GameDataPath", "gameData"),
                $"TerrainHeights{zone}.bin"));
        }

        /// <summary>
        /// Loads the home continent's grid at startup (gameData/TerrainHeights{zone}.bin, made by tools/terrain).
        /// Other landscapes load on first use and are dropped after <see cref="Idle"/>.
        /// </summary>
        public static void Load(uint continent)
        {
            if (GridOf(continent) == null)
                log.Warn($"No terrain heights for {continent}; trees and ground checks are off there.");
        }

        private static Grid GridOf(uint zone)
        {
            lock (sync)
            {
                if (grids.TryGetValue(zone, out Grid grid))
                {
                    grid.LastUsed = DateTime.UtcNow;
                    return grid;
                }
                if (missing.Contains(zone))
                    return null;
                string gameData = SharedConfiguration.Configuration.GetValue("GameDataPath", "gameData");
                string path = Path.Combine(gameData, $"TerrainHeights{zone}.bin");
                try
                {
                    using var reader = new BinaryReader(File.OpenRead(path));
                    if (new string(reader.ReadChars(4)) != "LMH1")
                        throw new InvalidDataException("not a height grid");
                    grid = new Grid
                    {
                        Size = reader.ReadInt32(),
                        Step = reader.ReadSingle(),
                        MinX = reader.ReadSingle(),
                        MinZ = reader.ReadSingle(),
                        BaseY = reader.ReadSingle(),
                        Unit = reader.ReadSingle(),
                        LastUsed = DateTime.UtcNow
                    };
                    grid.Heights = ReadBlock(reader, grid.Size * grid.Size);
                    grid.Materials = ReadBlock(reader, grid.Size * grid.Size);
                    grids[zone] = grid;
                    log.Info($"Terrain heights: {grid.Size}² samples every {grid.Step} m for landscape {zone}.");
                    return grid;
                }
                catch (Exception e)
                {
                    missing.Add(zone);
                    log.Debug($"No terrain heights for {zone} ({e.Message}).");
                    return null;
                }
            }
        }

        /// <summary>Drops landscape grids nobody has used for a while (called once a minute).</summary>
        public static void Trim()
        {
            lock (sync)
                foreach (uint zone in grids.Where(g => g.Key != Home && DateTime.UtcNow - g.Value.LastUsed > Idle)
                    .Select(g => g.Key).ToList())
                {
                    grids.Remove(zone);
                    log.Info($"Terrain heights for landscape {zone} unloaded (idle).");
                }
        }

        /// <summary>Ground height at a local x/z (bilinear between samples), or null outside the grid or without one.</summary>
        public static float? HeightAt(float x, float z, uint zone = 0)
        {
            Grid g = GridOf(zone == 0 ? Home : zone);
            if (g == null)
                return null;
            float fx = (x - g.MinX) / g.Step, fz = (z - g.MinZ) / g.Step;
            int ix = (int)MathF.Floor(fx), iz = (int)MathF.Floor(fz);
            if (ix < 0 || iz < 0 || ix + 1 >= g.Size || iz + 1 >= g.Size)
                return null;
            ushort[] h = g.Heights;
            ushort h00 = h[iz * g.Size + ix], h10 = h[iz * g.Size + ix + 1];
            ushort h01 = h[(iz + 1) * g.Size + ix], h11 = h[(iz + 1) * g.Size + ix + 1];
            if (h00 == None || h10 == None || h01 == None || h11 == None)
                return null;
            float tx = fx - ix, tz = fz - iz;
            float top = h00 + (h10 - h00) * tx, bottom = h01 + (h11 - h01) * tx;
            return g.BaseY + (top + (bottom - top) * tz) * g.Unit;
        }

        /// <summary>The surface code the sampler reports nearest to x/z (0 if unknown; not a VoxelMaterials id, meaning undecoded).</summary>
        public static ushort SurfaceMaterialAt(float x, float z, uint zone = 0)
        {
            Grid g = GridOf(zone == 0 ? Home : zone);
            if (g == null)
                return 0;
            int ix = (int)MathF.Round((x - g.MinX) / g.Step), iz = (int)MathF.Round((z - g.MinZ) / g.Step);
            if (ix < 0 || iz < 0 || ix >= g.Size || iz >= g.Size)
                return 0;
            return g.Materials[iz * g.Size + ix];
        }

        /// <summary>Ground slope at x/z as the rise over <paramref name="distance"/> metres (steepest of x and z).</summary>
        public static float Slope(float x, float z, float distance = 2f, uint zone = 0)
        {
            float? c = HeightAt(x, z, zone), dx = HeightAt(x + distance, z, zone), dz = HeightAt(x, z + distance, zone);
            if (c == null || dx == null || dz == null)
                return float.MaxValue;
            return MathF.Max(MathF.Abs(dx.Value - c.Value), MathF.Abs(dz.Value - c.Value)) / distance;
        }

        /// <summary>
        /// How far a point is below the generated ground at its x/z, in metres (negative above it; 0 when unknown).
        /// Used to stop streaming surface objects (trees, plants, creatures) to players deep in caves.
        /// </summary>
        public static float DepthBelowGround(Vector3 p, uint zone = 0) => HeightAt(p.X, p.Z, zone) is float ground ? ground - p.Y : 0f;

        public static Vector3? OnGround(float x, float z, uint zone = 0) => HeightAt(x, z, zone) is float y ? new Vector3(x, y, z) : null;

        /// <summary>
        /// Reads one deflate-compressed block of u16 heights.
        /// </summary>
        private static ushort[] ReadBlock(BinaryReader reader, int count)
        {
            int length = reader.ReadInt32();
            byte[] packed = reader.ReadBytes(length);
            using var inflate = new DeflateStream(new MemoryStream(packed), CompressionMode.Decompress);
            var values = new ushort[count];
            byte[] raw = new byte[count * 2];
            inflate.ReadExactly(raw);
            Buffer.BlockCopy(raw, 0, values, 0, raw.Length);
            return values;
        }
    }
}
