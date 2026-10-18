using LandmarkEmulator.Shared.Configuration;
using Microsoft.Extensions.Configuration;
using NLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Shared reader for the client's '^'-separated data tables in the GameDataPath folder (gameData/*.txt): the header
    /// row ("#*ID^NAME^…") is skipped, and a missing or unreadable file gives no rows (logged once) instead of throwing,
    /// so a feature that depends on a table just stays off.
    /// </summary>
    public static class GameDataFiles
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>Full path of a table in the game data folder.</summary>
        public static string PathOf(string fileName)
            => Path.Combine(SharedConfiguration.Configuration.GetValue("GameDataPath", "gameData"), fileName);

        /// <summary>Data rows of a table, split on '^' (columns keep their order from the header).</summary>
        public static IEnumerable<string[]> Rows(string fileName)
        {
            string path = PathOf(fileName);
            if (!File.Exists(path))
            {
                log.Warn($"Game data table {fileName} not found at {path}.");
                return Array.Empty<string[]>();
            }
            try
            {
                return File.ReadLines(path).Where(l => l.Length > 0 && !l.StartsWith('#')).Select(l => l.Split('^')).ToList();
            }
            catch (Exception e)
            {
                log.Error(e, $"Could not read game data table {fileName}.");
                return Array.Empty<string[]>();
            }
        }

        /// <summary>Column <paramref name="i"/> as a uint (0 when missing or not a number).</summary>
        public static uint U(this string[] row, int i) => i < row.Length && uint.TryParse(row[i], out uint v) ? v : 0;

        /// <summary>Column <paramref name="i"/> as an int (0 when missing or not a number).</summary>
        public static int I(this string[] row, int i) => i < row.Length && int.TryParse(row[i], out int v) ? v : 0;

        /// <summary>Column <paramref name="i"/> as a float, invariant culture (0 when missing or not a number).</summary>
        public static float F(this string[] row, int i)
            => i < row.Length && float.TryParse(row[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;

        /// <summary>Column <paramref name="i"/> as text ("" when missing).</summary>
        public static string S(this string[] row, int i) => i < row.Length ? row[i] : "";
    }
}
