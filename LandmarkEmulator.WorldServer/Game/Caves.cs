using LandmarkEmulator.WorldServer.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Underground caves. The client generates the caves itself from its own data
    /// (CommonData/Voxel/caves/ContinentCavePlacements[continent]_Underground.cpa places the .cav pieces), so the
    /// server only needs to know where they are to send players there ("An Underground Adventure" in Travel).
    /// .cpa layout: u32 version (14), u32 n, then n × 39-byte records {u32 piece, f64 x, f64 y, f64 z (global
    /// decimetres, the piece's corner), u32 rotation (0-3), u8, u32, u8, u8}; more data follows (not needed here).
    /// Pieces sit on a 64 m grid; piece 104 is an open cavern (verified in game), so its centre, a little above the
    /// corner, is a safe arrival point: the player drops onto the cavern floor.
    /// </summary>
    public static class Caves
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private const uint CavernPiece = 104;
        private const int RecordSize = 39;
        /// <summary>
        /// The .cpa position is the piece's origin, the cavern's x/z centre (CavePieces); 10 m above it is open air in
        /// every cavern piece (CavernMiddle01 is air from about −76 to +75 m around it).
        /// </summary>
        private static readonly Vector3 ArrivalOffset = new(0f, 10f, 0f);

        private static readonly List<Vector3> caverns = new();
        /// <summary>Deepest cavern entry (local metres) a random Underground Adventure picks (see TravelRandom).</summary>
        private const float MinTravelDepth = -650f;
        /// <summary>
        /// The caverns with their cave biome (VoxelBiomes: 41 AncientForestCavern, 45 CrystalCavern, 62 DeciduousCavern,
        /// 63 VolcanicCavern …), from the placement's property group (VoxelLayerPropertyGroups → biome, tier): the Chaos
        /// Caverns' monsters live here (Creatures).
        /// </summary>
        public static IReadOnlyList<(Vector3 At, uint Biome)> Caverns => cavernBiomes;
        private static readonly List<(Vector3 At, uint Biome)> cavernBiomes = new();
        private static readonly Random random = new();

        /// <summary>Reads the continent's underground placements and keeps the cavern pieces (local metres).</summary>
        public static void Load(string gameDirectory, uint continent)
        {
            string path = Path.Combine(gameDirectory, "CommonData", "Voxel", "caves", $"ContinentCavePlacements[{continent}]_Underground.cpa");
            try
            {
                var groups = new Dictionary<uint, uint>();
                string groupFile = Path.Combine(gameDirectory, "Resources", "VoxelLayerPropertyGroups.txt");
                if (File.Exists(groupFile))
                    foreach (string line in File.ReadLines(groupFile))
                    {
                        string[] f = line.Split('^');
                        if (f.Length >= 3 && uint.TryParse(f[0], out uint id) && uint.TryParse(f[1], out uint biome))
                            groups[id] = biome;
                    }
                byte[] d = File.ReadAllBytes(path);
                int n = BitConverter.ToInt32(d, 4);
                var origin = Terrain.TerrainStore.ZoneOrigin;
                for (int i = 0, o = 8; i < n && o + RecordSize <= d.Length; i++, o += RecordSize)
                {
                    if (BitConverter.ToUInt32(d, o) != CavernPiece)
                        continue;
                    double x = BitConverter.ToDouble(d, o + 4), y = BitConverter.ToDouble(d, o + 12), z = BitConverter.ToDouble(d, o + 20);
                    var at = new Vector3((float)((x - origin.X) / 10), (float)((y - origin.Y) / 10), (float)((z - origin.Z) / 10))
                        + ArrivalOffset;
                    caverns.Add(at);
                    cavernBiomes.Add((at, groups.GetValueOrDefault(BitConverter.ToUInt32(d, o + 33))));
                }
                log.Info($"Caves: {caverns.Count} caverns under continent {continent}.");
            }
            catch (Exception e)
            {
                log.Warn($"No cave placements ({e.Message}); underground travel is off.");
            }
        }

        /// <summary>Travel → An Underground Adventure: a random cavern, keeping the exact height (no ground snap).</summary>
        /// <summary>Switch for Travel → An Underground Adventure.</summary>
        public static bool Enabled = true;

        /// <summary>The cavern nearest a position (local metres), or null when none were loaded.</summary>
        public static Vector3? Nearest(Vector3 position)
            => caverns.Count == 0 ? null : caverns.OrderBy(c => Vector3.DistanceSquared(c, position)).First();

        /// <summary>A random cavern arrival point, or null when no placements were loaded.</summary>
        public static Vector3? RandomCavern() => caverns.Count == 0 ? null : Floor(caverns[random.Next(caverns.Count)]);

        /// <summary>
        /// The cavern floor below a point in the cavern's air (the client's own cave shapes, CavePieces): stepping
        /// down a voxel at a time until rock, then 1 m above it. Unchanged when the caves aren't known.
        /// </summary>
        /// <summary>
        /// The cave floor at (x, z) near a height: from a point in the air, down to the rock below (at most
        /// <paramref name="reach"/> m); from inside rock, up to the first air. Null when there is no floor within reach
        /// (a wall, a drop) — a creature doesn't step there.
        /// </summary>
        public static Vector3? FloorNear(Vector3 near, float reach = 6f)
        {
            double step = Terrain.TerrainEdits.VoxelSize;
            (double gx, double gy, double gz) = Terrain.TerrainEdits.ToGlobal(near + new Vector3(0f, 1f, 0f));
            if (Terrain.CavePieces.IsAir(gx, gy, gz))
            {
                for (double y = gy; y > gy - reach * 10; y -= step)
                    if (!Terrain.CavePieces.IsAir(gx, y, gz))
                        return near with { Y = near.Y + 1f - (float)((gy - y) / 10) + 0.4f };
                return null;
            }
            for (double y = gy; y < gy + reach * 10; y += step)
                if (Terrain.CavePieces.IsAir(gx, y, gz))
                    return near with { Y = near.Y + 1f + (float)((y - gy) / 10) };
            return null;
        }

        public static Vector3 Floor(Vector3 inAir)
        {
            (double gx, double gy, double gz) = Terrain.TerrainEdits.ToGlobal(inAir);
            if (!Terrain.CavePieces.IsAir(gx, gy, gz))
                return inAir;
            for (double y = gy; y > gy - 2000; y -= Terrain.TerrainEdits.VoxelSize)
                if (!Terrain.CavePieces.IsAir(gx, y, gz))
                    return inAir with { Y = inAir.Y - (float)((gy - y) / 10) + 1f };
            return inAir;
        }

        /// <summary>
        /// Developer check (dev "cavecheck"): for every cavern entry point, whether <see cref="Floor"/> finds a floor —
        /// starting in rock, or no ground within 200 m below, leaves the arrival in the air or in rock.
        /// </summary>
        public static string CheckFloors()
        {
            int inRock = 0, noFloor = 0, ok = 0;
            var bad = new List<string>();
            foreach (Vector3 c in caverns)
            {
                (double gx, double gy, double gz) = Terrain.TerrainEdits.ToGlobal(c);
                if (!Terrain.CavePieces.IsAir(gx, gy, gz)) { inRock++; if (bad.Count < 8) bad.Add($"rock {c}"); continue; }
                Vector3 f = Floor(c);
                if (f == c) { noFloor++; if (bad.Count < 8) bad.Add($"air {c}"); }
                else ok++;
            }
            return $"{caverns.Count} caverns: {ok} floor found, {inRock} start in rock, {noFloor} no floor within 200 m; " + string.Join("; ", bad);
        }

        public static void TravelRandom(WorldSession session)
        {
            if (caverns.Count == 0 || !Enabled)
            {
                Chat.System(session, "Underground adventures aren't open on this server yet.");
                return;
            }
            // Random trips use the caverns down to MinTravelDepth: deeper ones (the .cpa has them to about y -1000) were
            // still loading on the client 35 s after arrival and the player then fell through (y -673 -> -1305,
            // y -865 -> -1882; the audit of 2026-10-14). Entries down to y -640 landed on the floor every time.
            var reachable = caverns.Where(c => c.Y >= MinTravelDepth).ToList();
            Vector3 at = Floor(reachable.Count > 0 ? reachable[random.Next(reachable.Count)] : caverns[random.Next(caverns.Count)]);
            // The caverns are the home continent's: from another landscape the trip re-zones there.
            if (session.OnHomeContinent)
                Teleport.To(session, at, snapToGround: false);
            else
                Teleport.ToZone(session, at, Terrain.TerrainStore.HomeContinent);
            log.Info($"{session.CharacterName} goes underground at {at}.");
            Achievements.Progress(session, Achievements.UndergroundAdventure);
        }
    }
}
