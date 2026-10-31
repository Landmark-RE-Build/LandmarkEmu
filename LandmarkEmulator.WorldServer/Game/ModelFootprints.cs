using NLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Each model's ground footprint from the client's Resources/Models.txt: PATH_BLOCK_RADIUS (metres) and
    /// PATH_BLOCK_OFFSET_X/Y/Z, the area the model blocks for path finding. Used to keep arrivals out of tree trunks.
    /// Also each model's file name (MODEL_FILE_NAME), from which a tree's size is read (Gathering.TreeSize).
    /// </summary>
    public static class ModelFootprints
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        private static readonly Dictionary<uint, (float Radius, Vector3 Offset)> footprints = new();
        private static readonly Dictionary<uint, string> names = new();

        public static void Load(string gameDirectory)
        {
            string path = Path.Combine(gameDirectory ?? "", "Resources", "Models.txt");
            if (!File.Exists(path))
            {
                log.Warn($"No {path}; model footprints unknown.");
                return;
            }
            foreach (string line in File.ReadLines(path))
            {
                string[] f = line.Split('^');
                if (f.Length < 18 || !uint.TryParse(f[0], out uint id))
                    continue;
                float Parse(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
                names[id] = f[1];
                float radius = Parse(f[14]);
                if (radius > 0f)
                    footprints[id] = (radius, new Vector3(Parse(f[15]), Parse(f[16]), Parse(f[17])));
            }
            log.Info($"{footprints.Count} model footprints.");
        }

        /// <summary>The model's blocking radius and its centre offset, or null when the model doesn't block.</summary>
        public static (float Radius, Vector3 Offset)? Of(uint model) =>
            footprints.TryGetValue(model, out var f) ? f : null;

        /// <summary>The model's file name (e.g. Biome_JaggedPine_Trees_DiamondPine_Large01.adr), or null when unknown.</summary>
        public static string NameOf(uint model) => names.GetValueOrDefault(model);
    }
}
