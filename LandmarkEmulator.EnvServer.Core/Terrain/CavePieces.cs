using NLog;
using System;
using System.Collections.Generic;
using System.IO;

namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// The client's caves, so the server knows where the generated world is cave air (read-only, from the install).
    ///
    /// Pieces: CommonData/Voxel/caves/*.cav (client VoxelFarm::CaveTileAsset, load FUN_1444343d0 → parser FUN_144433800),
    /// little-endian: u32 version 1; f64 voxel size (4 dm at LOD 0); f64[8] box (min x,y,z,w, max x,y,z,w; dm from the
    /// piece origin); u32 nLod, then per LOD {u32 version 3, i32 min[4], i32 max[4] (inclusive voxel index box),
    /// u32 n + n bytes type RLE, u32 nx·ny·nz, u32 n + n × u32 column offsets (n = nx·nz), u32 n + n bytes offset RLE,
    /// u32 3·nx·ny·nz, u32 n + n × u32 offset column offsets}; u32 nGap × room-gap records. A column (x, z) is
    /// (x − minx) + nx·(z − minz) and runs up y from miny; type runs are (u8 count, u8 type): 0 = carve to air,
    /// 1..N = repaint an already solid voxel with the tile's cave material (never adds rock). Cave LOD 0 = cell level 2.
    ///
    /// Placement: ContinentCavePlacements[continent]_Underground.cpa / _Surface.cpa: u32 version, u32 n, n × 39 bytes
    /// {u32 tile (VoxelCaveTileDefinitions ID → .cav), f64 x, y, z (global dm, the piece's origin = its x/z centre),
    /// u32 rotation 0-3, u8, u32 property group, u8, u8} (FUN_143edfd60). Rotation turns x/z only: the source index
    /// of world-relative index (jx, jz) is rot 0 (jx, jz), 1 (−jz, jx), 2 (−jx, −jz), 3 (jz, −jx); j = round(Δ / 4 dm).
    ///
    /// The cave layer runs after the terrain generator (CaveVoxelLayer, FUN_143fba640): a voxel is air when any piece
    /// covering it has type 0 there; nothing else changes air vs rock (DRAW_OUTER_SHELL is 0 for every tile).
    /// </summary>
    public static class CavePieces
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private sealed class Piece
        {
            public double VoxelSize;
            public double[] Box;                  // min x,y,z, max x,y,z (dm, from the origin, unrotated)
            public int MinX, MinY, MinZ, Nx, Ny, Nz;
            public byte[] Runs;                   // LOD 0 type RLE
            public uint[] Columns;                // LOD 0 column start offsets into Runs

            /// <summary>Type at a source voxel index (0 = air), or -1 outside the piece.</summary>
            public int TypeAt(int x, int y, int z)
            {
                int cx = x - MinX, cy = y - MinY, cz = z - MinZ;
                if (cx < 0 || cy < 0 || cz < 0 || cx >= Nx || cy >= Ny || cz >= Nz)
                    return -1;
                int column = cx + Nx * cz;
                int at = (int)Columns[column], end = column + 1 < Columns.Length ? (int)Columns[column + 1] : Runs.Length;
                for (; at + 1 < end; at += 2)
                {
                    cy -= Runs[at];
                    if (cy < 0)
                        return Runs[at + 1];
                }
                return -1;
            }
        }

        private readonly record struct Placed(Piece Piece, double X, double Y, double Z, int Rotation,
            double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ);

        /// <summary>Bucket size for the spatial index (dm): pieces sit on a 64 m grid.</summary>
        private const double Bucket = 640;

        private static readonly object sync = new();
        private static readonly Dictionary<uint, Dictionary<(int, int), List<Placed>>> zones = new();
        private static readonly Dictionary<uint, Piece> pieces = new();
        private static Dictionary<uint, string> tileFiles;
        private static string gameDirectory;

        public static void Init(string gameDirectory) => CavePieces.gameDirectory = gameDirectory;

        /// <summary>Does this landscape have cave placements (so <see cref="IsAir"/> is meaningful)?</summary>
        public static bool HasData(uint zone) => ZoneIndex(zone == 0 ? TerrainStore.HomeContinent : zone) != null;

        /// <summary>
        /// Is this global point (dm) cave air in the client's generated world? False where no cave piece carves it
        /// (or the landscape has no cave data).
        /// </summary>
        public static bool IsAir(double gx, double gy, double gz, uint zone = 0)
        {
            var index = ZoneIndex(zone == 0 ? TerrainStore.HomeContinent : zone);
            if (index == null || !index.TryGetValue(((int)Math.Floor(gx / Bucket), (int)Math.Floor(gz / Bucket)), out var list))
                return false;
            foreach (Placed p in list)
            {
                if (gx < p.MinX || gx > p.MaxX || gy < p.MinY || gy > p.MaxY || gz < p.MinZ || gz > p.MaxZ)
                    continue;
                double v = p.Piece.VoxelSize;
                int jx = (int)Math.Floor((gx - p.X) / v + 0.5), jy = (int)Math.Floor((gy - p.Y) / v + 0.5),
                    jz = (int)Math.Floor((gz - p.Z) / v + 0.5);
                (int sx, int sz) = p.Rotation switch { 1 => (-jz, jx), 2 => (-jx, -jz), 3 => (jz, -jx), _ => (jx, jz) };
                if (p.Piece.TypeAt(sx, jy, sz) == 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Tests only: cave air of their own (global x, z dm, landscape, the list to append spans to; true = handled, the
        /// placements are not read), so the Pioneer slab's cave rule can be checked against a known cave
        /// (Tests/Rules/PioneerSiteSlabTests.cs). Never set by the servers.
        /// </summary>
        internal static Func<double, double, uint, List<(double Lo, double Hi)>, bool> TestAirSpans;

        /// <summary>
        /// Every stretch of cave air in the vertical column at a global x/z (dm) of a landscape, appended to
        /// <paramref name="into"/> as (low, high) global heights in dm: one span per air run of each piece covering the column
        /// (unsorted, and spans of different pieces may overlap — a point is air when any piece carves it, as in
        /// <see cref="IsAir"/>, whose voxel rounding and piece box it follows). Reads the LOD 0 type runs of the column once
        /// instead of testing voxel by voxel, so a whole column costs about as much as a few <see cref="IsAir"/> calls. Used
        /// by the creature navigation (Game/Pathing via Sight.ColumnAir). Thread-safe (pieces are read-only once loaded).
        /// </summary>
        public static void AirSpans(double gx, double gz, uint zone, List<(double Lo, double Hi)> into)
        {
            if (TestAirSpans?.Invoke(gx, gz, zone, into) == true)
                return;
            var index = ZoneIndex(zone == 0 ? TerrainStore.HomeContinent : zone);
            if (index == null || !index.TryGetValue(((int)Math.Floor(gx / Bucket), (int)Math.Floor(gz / Bucket)), out var list))
                return;
            foreach (Placed p in list)
            {
                if (gx < p.MinX || gx > p.MaxX || gz < p.MinZ || gz > p.MaxZ)
                    continue;
                Piece piece = p.Piece;
                double v = piece.VoxelSize;
                int jx = (int)Math.Floor((gx - p.X) / v + 0.5), jz = (int)Math.Floor((gz - p.Z) / v + 0.5);
                (int sx, int sz) = p.Rotation switch { 1 => (-jz, jx), 2 => (-jx, -jz), 3 => (jz, -jx), _ => (jx, jz) };
                int cx = sx - piece.MinX, cz = sz - piece.MinZ;
                if (cx < 0 || cz < 0 || cx >= piece.Nx || cz >= piece.Nz)
                    continue;
                int column = cx + piece.Nx * cz;
                int at = (int)piece.Columns[column], end = column + 1 < piece.Columns.Length ? (int)piece.Columns[column + 1] : piece.Runs.Length;
                // Runs go up y from MinY: (count, type), type 0 = carve to air (see Piece.TypeAt). Source voxel j covers the
                // heights that round to it: p.Y + (j − 0.5) v up to p.Y + (j + 0.5) v.
                for (int y = 0; at + 1 < end && y < piece.Ny; at += 2)
                {
                    int n = Math.Min((int)piece.Runs[at], piece.Ny - y);
                    if (piece.Runs[at + 1] == 0 && n > 0)
                    {
                        double lo = Math.Max(p.Y + (piece.MinY + y - 0.5) * v, p.MinY);
                        double hi = Math.Min(p.Y + (piece.MinY + y + n - 0.5) * v, p.MaxY);
                        if (hi > lo)
                            into.Add((lo, hi));
                    }
                    y += piece.Runs[at];
                }
            }
        }

        /// <summary>A landscape's placed pieces, bucketed by 64 m x/z squares; loaded on first use, null without data.</summary>
        private static Dictionary<(int, int), List<Placed>> ZoneIndex(uint zone)
        {
            lock (sync)
            {
                if (zones.TryGetValue(zone, out var index))
                    return index;
                index = null;
                try
                {
                    index = Build(zone);
                }
                catch (Exception e)
                {
                    log.Debug($"No cave pieces for landscape {zone} ({e.Message}).");
                }
                zones[zone] = index;
                return index;
            }
        }

        /// <summary>
        /// A landscape's placement file (kind "Underground" or "Surface"), named by the landscape's continent as the
        /// client's loader FUN_143ff1310 names it (the core's continent definition, <see cref="LandscapeContinents"/>):
        /// the classic islands their own id, the Pioneer lands 464-468 continents 440-444.
        /// </summary>
        public static string PlacementPath(uint zone, string kind)
            => Path.Combine(gameDirectory ?? "", "CommonData", "Voxel", "caves",
                $"ContinentCavePlacements[{LandscapeContinents.Of(zone == 0 ? TerrainStore.HomeContinent : zone, gameDirectory)}]_{kind}.cpa");

        private static Dictionary<(int, int), List<Placed>> Build(uint zone)
        {
            string folder = Path.Combine(gameDirectory ?? "", "CommonData", "Voxel", "caves");
            tileFiles ??= TileFiles();
            var index = new Dictionary<(int, int), List<Placed>>();
            int count = 0;
            foreach (string kind in new[] { "Underground", "Surface" })
            {
                string path = PlacementPath(zone, kind);
                if (!File.Exists(path))
                    continue;
                using var reader = new BinaryReader(File.OpenRead(path));
                reader.ReadUInt32();
                uint n = reader.ReadUInt32();
                for (uint i = 0; i < n; i++)
                {
                    uint tile = reader.ReadUInt32();
                    double x = reader.ReadDouble(), y = reader.ReadDouble(), z = reader.ReadDouble();
                    int rotation = (int)reader.ReadUInt32() & 3;
                    reader.ReadBytes(7);
                    if (PieceFor(folder, tile) is not Piece piece)
                        continue;
                    double[] b = piece.Box;
                    (double x0, double x1, double z0, double z1) = rotation switch
                    {
                        1 => (b[2], b[5], -b[3], -b[0]),
                        2 => (-b[3], -b[0], -b[5], -b[2]),
                        3 => (-b[5], -b[2], b[0], b[3]),
                        _ => (b[0], b[3], b[2], b[5])
                    };
                    var placed = new Placed(piece, x, y, z, rotation, x + x0, y + b[1], z + z0, x + x1, y + b[4], z + z1);
                    for (int bx = (int)Math.Floor(placed.MinX / Bucket); bx <= (int)Math.Floor(placed.MaxX / Bucket); bx++)
                        for (int bz = (int)Math.Floor(placed.MinZ / Bucket); bz <= (int)Math.Floor(placed.MaxZ / Bucket); bz++)
                        {
                            if (!index.TryGetValue((bx, bz), out var list))
                                index[(bx, bz)] = list = new List<Placed>();
                            list.Add(placed);
                        }
                    count++;
                }
            }
            if (count == 0)
                return null;
            log.Info($"Cave pieces on landscape {zone}: {count} placed, {pieces.Count} piece shapes loaded.");
            return index;
        }

        /// <summary>VoxelCaveTileDefinitions: tile id → .cav file name.</summary>
        private static Dictionary<uint, string> TileFiles()
        {
            var map = new Dictionary<uint, string>();
            foreach (string[] r in GameDataFiles.Rows("VoxelCaveTileDefinitions.txt"))
                map[r.U(0)] = r[1];
            return map;
        }

        private static Piece PieceFor(string folder, uint tile)
        {
            if (pieces.TryGetValue(tile, out Piece piece))
                return piece;
            piece = null;
            if (tileFiles.TryGetValue(tile, out string file) && File.Exists(Path.Combine(folder, file)))
                piece = ReadPiece(Path.Combine(folder, file));
            pieces[tile] = piece;
            return piece;
        }

        /// <summary>Reads a .cav's header and its LOD 0 type data (the rest isn't needed for air vs rock).</summary>
        private static Piece ReadPiece(string path)
        {
            using var reader = new BinaryReader(File.OpenRead(path));
            reader.ReadUInt32();
            var piece = new Piece { VoxelSize = reader.ReadDouble() };
            var box = new double[8];
            for (int i = 0; i < 8; i++)
                box[i] = reader.ReadDouble();
            piece.Box = new[] { box[0], box[1], box[2], box[4], box[5], box[6] };
            if (reader.ReadUInt32() == 0 || reader.ReadUInt32() < 3)
                throw new InvalidDataException($"{path}: unsupported LOD layout");
            int[] min = { reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32() };
            int[] max = { reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32() };
            piece.MinX = min[0]; piece.MinY = min[1]; piece.MinZ = min[2];
            piece.Nx = max[0] - min[0] + 1; piece.Ny = max[1] - min[1] + 1; piece.Nz = max[2] - min[2] + 1;
            piece.Runs = reader.ReadBytes(reader.ReadInt32());
            reader.ReadUInt32();
            int columns = reader.ReadInt32();
            piece.Columns = new uint[columns];
            for (int i = 0; i < columns; i++)
                piece.Columns[i] = reader.ReadUInt32();
            return piece;
        }
    }
}
