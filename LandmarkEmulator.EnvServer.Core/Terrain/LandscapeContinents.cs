using NLog;
using System;
using System.Collections.Generic;
using System.IO;

namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// The continent a landscape is generated as: StaticZoneData CONTINENT_ID (column 5) of the landscape's zone row.
    /// The client names its per-continent voxel files by this id, not by the zone id [P]:
    /// - <c>Voxel\veinData\ContinentVeinData%d.vdt</c> (FUN_143ff34d0), <c>Voxel\ocean\ContinentOceanMap%d.ocm</c>
    ///   (FUN_143ff3730) and <c>Voxel\surfaceCache\ContinentSurfaceCache%d.csc</c> (FUN_143ff3fc0) all format the
    ///   VoxelFarm core's +0x5208, which FUN_143f9aa30 / FUN_143f9aad0 / FUN_143f94580 set from the zone's continent
    ///   definition (SendZoneDetails ContinentDefinition.ContinentId) and VoxelFarmCore::InitVoxelStructureContinent
    ///   (FUN_143ffa7c0) matches against VoxelContinentWorld CONTINENT_ID;
    /// - <c>ContinentCavePlacements[%d]_*.cpa</c> (FUN_143ff1310) takes the id from the core's continent definition
    ///   (+0x5218, its vtable +0x10, the same getter FUN_143f9aa30 stores into +0x5208).
    /// The classic islands 391-416 are their own continent; the Pioneer lands 464-468 are continents 440-444 and the
    /// event islands 460-462 continents 436-438 (for those no vein or cave file ships). The shipped vein files agree:
    /// ContinentVeinData388.vdt exists while no zone 388 does (zone 387 is continent 388), and 440-444 exist while
    /// zones 440-444 are all continent 430.
    /// Read from the install's own Resources/StaticZoneData.txt (as Landscapes does), else the game data folder's copy;
    /// a landscape without a row is taken as its own continent.
    /// </summary>
    public static class LandscapeContinents
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private static readonly object sync = new();
        private static readonly Dictionary<string, Dictionary<uint, uint>> tables = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The continent id the client generates landscape <paramref name="zone"/> as (the zone id itself when unknown).</summary>
        public static uint Of(uint zone, string gameDirectory)
            => Table(gameDirectory).TryGetValue(zone, out uint continent) && continent != 0 ? continent : zone;

        /// <summary>
        /// Is <paramref name="zone"/> a Pioneer land: StaticZoneData IS_NEWBIE_ZONE (column 14) = 1, set for exactly 464-468
        /// and the abandoned 463 [P] (Landscapes.NewbieZone reads the same column for SendZoneDetails)?
        /// </summary>
        public static bool IsPioneer(uint zone, string gameDirectory)
        {
            Table(gameDirectory);
            lock (sync)
                return newbie.TryGetValue(gameDirectory ?? "", out var set) && set.Contains(zone);
        }

        private static readonly Dictionary<string, HashSet<uint>> newbie = new(StringComparer.OrdinalIgnoreCase);

        private static Dictionary<uint, uint> Table(string gameDirectory)
        {
            string key = gameDirectory ?? "";
            lock (sync)
            {
                if (tables.TryGetValue(key, out var table))
                    return table;
                table = new Dictionary<uint, uint>();
                var pioneer = new HashSet<uint>();
                newbie[key] = pioneer;
                string path = Path.Combine(key, "Resources", "StaticZoneData.txt");
                IEnumerable<string[]> rows;
                try
                {
                    rows = File.Exists(path) ? ReadRows(path) : GameDataFiles.Rows("StaticZoneData.txt");
                }
                catch (Exception e)
                {
                    log.Warn($"StaticZoneData not read ({e.Message}); landscapes are taken as their own continent.");
                    rows = Array.Empty<string[]>();
                }
                foreach (string[] f in rows)
                    if (f.Length > 5 && uint.TryParse(f[0], out uint id) && uint.TryParse(f[5], out uint continent))
                    {
                        table[id] = continent;
                        if (f.Length > 14 && f[14].Trim() == "1")
                            pioneer.Add(id);
                    }
                tables[key] = table;
                return table;
            }
        }

        private static List<string[]> ReadRows(string path)
        {
            var rows = new List<string[]>();
            foreach (string line in File.ReadLines(path))
                if (line.Length > 0 && !line.StartsWith('#'))
                    rows.Add(line.Split('^'));
            return rows;
        }
    }
}
