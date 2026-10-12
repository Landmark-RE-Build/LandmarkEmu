using LandmarkEmulator.Shared.Configuration;
using Microsoft.Extensions.Configuration;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;

namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// What building with a voxel material costs. Voxel data names materials by VoxelMaterials.txt ARRAY_INDEX
    /// (column 1; e.g. 79 = Sand). A material's CURRENCY_ID (column 36: 71 Dirt, 72 Sand, 73 Stone, 68-70 woods,
    /// 74-77 stones, 78-87 metals, 88 Snow) names the resource it is built from; the item for that resource is the raw
    /// one for dirt/sand/stone/snow and the processed one otherwise (cut wood, smelted metal), as in Landmark.
    /// Materials without a currency fall back to ITEM_ID (column 40); with neither they are free.
    /// One item builds <see cref="VoxelsPerItem"/> voxels, the rate mining pays out.
    /// </summary>
    public static class BuildMaterials
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const int VoxelsPerItem = 4;
        private static Dictionary<ushort, uint> itemByMaterial;

        private static readonly Dictionary<uint, uint> itemByCurrency = new()
        {
            [68] = 2000, [69] = 2001, [70] = 2002,
            [71] = 44, [72] = 45, [73] = 46, [88] = 76,
            [74] = 121, [75] = 122, [76] = 123, [77] = 124,
            [78] = 108, [79] = 109, [80] = 110, [81] = 111, [82] = 112,
            [83] = 113, [84] = 114, [85] = 115, [86] = 116, [87] = 117,
            [90] = 58,
        };

        /// <summary>
        /// The item a voxel material is built from (0 = free), loading the table on first use.
        /// </summary>
        public static uint ItemFor(ushort material)
        {
            if (itemByMaterial == null)
                Load();
            return itemByMaterial.TryGetValue(material, out uint item) ? item : 0;
        }

        private static Dictionary<ushort, uint> nameByMaterial = new();

        /// <summary>A voxel material's name (VoxelMaterials NAME_STRING_ID, column 2), e.g. "Shiny Gold"; "" if none.</summary>
        public static string NameOf(ushort material)
        {
            if (itemByMaterial == null)
                Load();
            return nameByMaterial.TryGetValue(material, out uint text)
                ? LandmarkEmulator.Shared.GameTable.Text.TextManager.Instance.GetTextForId(text) ?? ""
                : "";
        }

        /// <summary>
        /// Reads VoxelMaterials.txt: ARRAY_INDEX (column 1) → CURRENCY_ID (column 36) → item, else ITEM_ID (column 40).
        /// </summary>
        private static void Load()
        {
            var map = new Dictionary<ushort, uint>();
            var names = new Dictionary<ushort, uint>();
            try
            {
                string gameData = SharedConfiguration.Configuration.GetValue("GameDataPath", "gameData");
                foreach (string line in File.ReadLines(Path.Combine(gameData, "VoxelMaterials.txt")))
                {
                    if (line.Length == 0 || line[0] == '#')
                        continue;
                    string[] f = line.Split('^');
                    if (f.Length <= 40 || !ushort.TryParse(f[1], out ushort index))
                        continue;
                    if (uint.TryParse(f[2], out uint nameText) && nameText != 0)
                        names[index] = nameText;
                    uint.TryParse(f[36], out uint currency);
                    uint.TryParse(f[40], out uint item);
                    if (currency != 0 && itemByCurrency.TryGetValue(currency, out uint byCurrency))
                        item = byCurrency;
                    if (item != 0)
                        map[index] = item;
                }
            }
            catch (Exception e)
            {
                log.Error(e, "Could not load VoxelMaterials.txt; building is free.");
            }
            log.Info($"Build materials: {map.Count} materials cost items.");
            nameByMaterial = names;
            itemByMaterial = map;
        }
    }
}
