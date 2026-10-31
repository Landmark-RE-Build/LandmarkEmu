using NLog;
using System;
using System.Collections.Generic;
using System.IO;

namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// The client's surface cache, read for the one thing it guarantees: how low the client's generated ground can lie.
    ///
    /// File: <c>CommonData\Voxel\surfaceCache\ContinentSurfaceCache%d.csc</c> by the landscape's continent (the client's
    /// FUN_143ff3fc0 formats the VoxelFarm core's +0x5208, the id the vein and ocean files use: <see cref="LandscapeContinents"/>);
    /// reader FUN_14407b070 → FUN_14407b810 (0.695) [P]: <c>u8 version 1, u32 x, u32 y, u32 z, u32 w, u32 h</c>, then 9 LOD
    /// grids of w × h u16, rows along z (only LOD 0 is read here). Texel (row r, column c) of LOD 0 is the 16 m column at
    /// global cell (x + c, z + r); its value is <c>low | count &lt;&lt; 10</c>: the ground of the column lies in the 16 m cells
    /// y + low … y + low + count (docs/terrain.md "Surface cache", fitted against the 2 m bakes [P]).
    ///
    /// <see cref="FloorAt"/> is the bottom of the lowest of those cells over the 3 × 3 columns around a spot, less
    /// <see cref="Margin"/>. Checked 2026-11-01 against the 2 m bakes of the client's own ground on 31 landscapes (391-420
    /// without 392 / 409, the event islands 460-462; 16 M samples each, trace/0695/pioneer-veins.md): the 3 × 3 cell bottom
    /// lies under the baked ground everywhere except 63 samples (403, 418, 420; at most 0.82 m above it) — none within
    /// <see cref="Margin"/>. So a voxel under <see cref="FloorAt"/> is solid in the client's generated world (unless a cave
    /// carves it). The server needs it where its own ground is only an estimate from this cache (the Pioneer lands 464-468:
    /// their terrain is not built by the heightmap sampler the 2 m bake hooks, re/tools/csc_heights.py).
    /// </summary>
    public static class SurfaceCache
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Metres kept under the cache's lowest cell bottom: the largest miss measured (0.82 m on 420) plus the 2 m bake's
        /// own error between its samples (p99 0.38 m on 406), rounded up to whole voxels (3 × 0.4 m).
        /// </summary>
        public const float Margin = 1.2f;

        private sealed class Grid
        {
            public int W, H;
            public double X0, Z0;      // local metres of texel (0, 0)'s low corner
            public float[] Floor;      // local metres, 3 × 3 minimum less Margin
        }

        private static readonly object sync = new();
        private static readonly Dictionary<(string, uint), Grid> grids = new();

        /// <summary>The cache file the client loads for a landscape (by its continent).</summary>
        public static string PathOf(string gameDirectory, uint zone)
            => Path.Combine(gameDirectory ?? "", "CommonData", "Voxel", "surfaceCache",
                $"ContinentSurfaceCache{LandscapeContinents.Of(zone, gameDirectory)}.csc");

        /// <summary>Is there a surface cache for this landscape (loads it)?</summary>
        public static bool Has(string gameDirectory, uint zone) => GridOf(gameDirectory, zone) != null;

        /// <summary>
        /// The height (local metres) the client's generated ground is never below at a local x / z: the bottom of the
        /// lowest cell the cache gives the 3 × 3 columns around it, less <see cref="Margin"/>. Null outside the cache or
        /// without one.
        /// </summary>
        public static float? FloorAt(string gameDirectory, float x, float z, uint zone)
        {
            Grid g = GridOf(gameDirectory, zone);
            if (g == null)
                return null;
            int c = (int)Math.Floor((x - g.X0) / 16.0), r = (int)Math.Floor((z - g.Z0) / 16.0);
            if (c < 0 || r < 0 || c >= g.W || r >= g.H)
                return null;
            return g.Floor[r * g.W + c];
        }

        private static Grid GridOf(string gameDirectory, uint zone)
        {
            var key = (gameDirectory ?? "", zone);
            lock (sync)
            {
                if (grids.TryGetValue(key, out Grid grid))
                    return grid;
                grid = null;
                string path = PathOf(gameDirectory, zone);
                try
                {
                    if (File.Exists(path))
                        grid = Read(path);
                }
                catch (Exception e)
                {
                    log.Warn($"Surface cache {Path.GetFileName(path)} unreadable ({e.Message}).");
                    grid = null;
                }
                if (grid != null)
                    log.Info($"Surface cache: {grid.W}×{grid.H} columns of 16 m for landscape {zone} ({Path.GetFileName(path)}).");
                grids[key] = grid;
                return grid;
            }
        }

        private static Grid Read(string path)
        {
            using var r = new BinaryReader(File.OpenRead(path));
            r.ReadByte();
            uint x = r.ReadUInt32(), y = r.ReadUInt32(), z = r.ReadUInt32();
            int w = (int)r.ReadUInt32(), h = (int)r.ReadUInt32();
            if (w <= 0 || h <= 0 || w > 4096 || h > 4096)
                throw new InvalidDataException($"size {w} × {h}");
            var origin = TerrainStore.ZoneOrigin;   // dm
            double oy = origin.Y / 10;
            var bottom = new float[w * h];
            for (int i = 0; i < w * h; i++)
            {
                ushort v = r.ReadUInt16();
                bottom[i] = (float)((y + (v & 1023)) * 16.0 - oy);
            }
            var floor = new float[w * h];
            for (int row = 0; row < h; row++)
                for (int col = 0; col < w; col++)
                {
                    float min = float.MaxValue;
                    for (int dr = -1; dr <= 1; dr++)
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            int rr = Math.Clamp(row + dr, 0, h - 1), cc = Math.Clamp(col + dc, 0, w - 1);
                            min = Math.Min(min, bottom[rr * w + cc]);
                        }
                    floor[row * w + col] = min - Margin;
                }
            return new Grid { W = w, H = h, X0 = x * 16.0 - origin.X / 10, Z0 = z * 16.0 - origin.Z / 10, Floor = floor };
        }
    }
}
