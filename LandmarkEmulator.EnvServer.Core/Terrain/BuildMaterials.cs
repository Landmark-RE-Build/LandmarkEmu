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
    /// (column 1; e.g. 79 = Sand). The cost is the material's ITEM_ID (column 40), exactly what the client's Materials
    /// tray uses (FUN_1436f4180: material record +0x28 → +0x90; "IsFree" and the ∞ symbol when it is 0, otherwise
    /// "Quantity" = how many of that item the player holds). Read from the running client for all 712 materials
    /// (re/test/scenarios/adhoc/matcost.js, 2026-10-13): every one equals ITEM_ID. CURRENCY_ID (column 36) is not the
    /// cost: charging by it made 137 materials disagree with the tray (∞ terrain textures and Halloween Black charged
    /// Dirt / Sand / Stone / Snow; others charged a different item than the tray counts, e.g. Cut Marble for Marble,
    /// Smelted Tin for Smelted Iron). ITEM_ID 0 = free-to-use (the wiki's
    /// "Free-to-Use Building Materials": most Terrain textures, infinity symbol in the tray).
    /// One item builds <see cref="VoxelsPerItem"/> voxel: the client's own pre-check (FUN_1435fd7c0) refuses an edit when
    /// the items held are fewer than the brush's voxel count (cursor vtable +0xa0, FUN_1435fbed0: popcount of the brush's
    /// 40³ cell masks, no division), and the material list it sends with each edit (ApplyDiffRequest +0x90) carries
    /// the voxel count. The earlier 4 voxels per item had no source and let the server charge a quarter of what the
    /// client counts (2026-10-14).
    /// </summary>
    public static class BuildMaterials
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const int VoxelsPerItem = 1;
        private static Dictionary<ushort, uint> itemByMaterial;

        /// <summary>
        /// The item a voxel material is built from (0 = free), loading the table on first use.
        /// </summary>
        public static uint ItemFor(ushort material)
        {
            if (itemByMaterial == null)
                Load();
            return itemByMaterial.TryGetValue(material, out uint item) ? item : 0;
        }

        /// <summary>
        /// Every item some voxel material costs (the items a material budget lists, <see cref="MaterialBudget"/>). Lives
        /// with the Environment Server library since the remote split (terrain-service.md §5.2): the service checks an
        /// edit's cost against the zone's budget before it writes anything.
        /// </summary>
        public static IReadOnlyCollection<uint> Items
        {
            get
            {
                if (itemByMaterial == null)
                    Load();
                return items ??= new HashSet<uint>(itemByMaterial.Values);
            }
        }

        private static HashSet<uint> items;

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
        /// Reads VoxelMaterials.txt: ARRAY_INDEX (column 1) → ITEM_ID (column 40).
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
                    uint.TryParse(f[40], out uint item);
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
