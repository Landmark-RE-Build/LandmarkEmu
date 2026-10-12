using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Display names of calculated stats (CalculatedStatDefinitions.txt: ID, PARENT_STAT_ID, NAME, …, NAME_ID column 11),
    /// e.g. 3 Harvesting Area, 12 Harvesting Damage, 13 Harvest Speed. Item tooltips carry the name text id with each stat.
    /// </summary>
    public static class StatNames
    {
        private static Dictionary<uint, uint> names;

        public static uint NameText(uint stat)
        {
            names ??= GameDataFiles.Rows("CalculatedStatDefinitions.txt").Where(r => r.Length > 11 && uint.TryParse(r[0], out _))
                .GroupBy(r => uint.Parse(r[0])).ToDictionary(g => g.Key, g => uint.TryParse(g.First()[11], out uint n) ? n : 0u);
            return names.GetValueOrDefault(stat);
        }
    }
}
