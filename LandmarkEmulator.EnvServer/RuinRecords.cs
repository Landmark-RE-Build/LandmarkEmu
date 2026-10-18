using LandmarkEmulator.WorldServer.Game.Terrain;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LandmarkEmulator.EnvServer
{
    /// <summary>
    /// The service's records of what it stamped (terrain-service.md §4.2 / §4.5): stamps.tsv (every exported-template stamp:
    /// id, template, landscape, corner kind, corner, when) always, and — with the RuinLayers switch — the ruin layers of
    /// the client's monitor page ("Ruin Layer Info": Desired / Active / Failed / Last ID per layer, FUN_14320ea00 /
    /// FUN_143163fe0, 16-byte records at +0xC8): ruins.tsv (layer, instance id, template, cavern, corner, when) and
    /// ruin-layers.tsv (layer, desired, last id, failed).
    /// <para>
    /// [I] What a ruin layer is on live is not found yet (L76): here one layer per landscape, every stamp of a ruin template
    /// (ruin_*, cavern_ruins_*; the 0.695 outposts, Game/Outposts.cs) is one instance of it. Placement stays in the zone
    /// (Outposts.ChooseSlots / FillCaverns) until it moves into the service (L127), so Failed stays 0 here — the zone
    /// knows the caverns it could not fill — and an instance the zone reverts is not taken out of ruins.tsv.
    /// </para>
    /// </summary>
    public sealed class RuinRecords
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const string RuinsFile = "ruins.tsv", LayersFile = "ruin-layers.tsv", StampsFile = "stamps.tsv";
        public const string RuinsHeader = "#layer\tinstance\ttemplate\tcavern\tx\ty\tz\twhen";
        public const string StampsHeader = "#stamp\ttemplate\tlandscape\tcorner\tx\ty\tz\twhen";
        /// <summary>The ruin layer of the home continent (= its zone id).</summary>
        public const uint HomeLayer = TerrainStore.HomeContinent;

        private sealed class Layer
        {
            public uint Desired, Active, Failed;
            public int LastId;
        }

        private readonly string folder;
        private readonly bool enabled;
        private readonly uint desired;
        private readonly SortedDictionary<uint, Layer> layers = new();
        private int lastStamp;

        public RuinRecords(string folder, bool enabled, uint desired)
        {
            this.folder = folder;
            this.enabled = enabled;
            this.desired = desired;
            string stamps = Path.Combine(folder, StampsFile);
            if (File.Exists(stamps))
                lastStamp = File.ReadLines(stamps).Count(l => l.Length > 0 && l[0] != '#');
            if (!enabled)
                return;
            string ruins = Path.Combine(folder, RuinsFile);
            if (File.Exists(ruins))
                foreach (string row in File.ReadLines(ruins))
                {
                    string[] f = row.Split('\t');
                    if (row.StartsWith('#') || f.Length < 2 || !uint.TryParse(f[0], out uint layer) || !int.TryParse(f[1], out int id))
                        continue;
                    Layer l = LayerOf(layer);
                    l.Active++;
                    l.LastId = Math.Max(l.LastId, id);
                }
            string saved = Path.Combine(folder, LayersFile);
            if (File.Exists(saved))
                foreach (string row in File.ReadLines(saved))
                {
                    string[] f = row.Split('\t');
                    if (row.StartsWith('#') || f.Length < 4 || !uint.TryParse(f[0], out uint layer))
                        continue;
                    Layer l = LayerOf(layer);
                    if (int.TryParse(f[2], out int last))
                        l.LastId = Math.Max(l.LastId, last);
                    if (uint.TryParse(f[3], out uint failed))
                        l.Failed = failed;
                }
            log.Info($"Ruin layers: {string.Join(", ", layers.Select(p => $"layer {p.Key}: {p.Value.Active} active, last id {p.Value.LastId}"))}.");
        }

        private Layer LayerOf(uint layer)
        {
            if (!layers.TryGetValue(layer, out Layer l))
                layers[layer] = l = new Layer { Desired = desired };
            return l;
        }

        private static bool IsRuin(string template)
        {
            string name = Path.GetFileName(template ?? "");
            return name.StartsWith("ruin_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("cavern_ruins_", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A stamp was made (SpawnExportedTemplateRequest): recorded, and as a ruin instance when it is one.</summary>
        public void Stamped(string template, uint landscape, string corner, long x, long y, long z)
        {
            string when = DateTime.UtcNow.ToString("u");
            try
            {
                string stamps = Path.Combine(folder, StampsFile);
                if (!File.Exists(stamps))
                    File.WriteAllText(stamps, StampsHeader + System.Environment.NewLine);
                File.AppendAllLines(stamps, new[] { string.Join('\t', ++lastStamp, Path.GetFileName(template ?? "(inline)"), landscape, corner, x, y, z, when) });
                if (!enabled || !IsRuin(template))
                    return;
                Layer layer = LayerOf(landscape);
                layer.Active++;
                layer.LastId++;
                string ruins = Path.Combine(folder, RuinsFile);
                if (!File.Exists(ruins))
                    File.WriteAllText(ruins, RuinsHeader + System.Environment.NewLine);
                File.AppendAllLines(ruins, new[] { string.Join('\t', landscape, layer.LastId, Path.GetFileName(template), "", x, y, z, when) });
                SaveLayers();
            }
            catch (IOException e)
            {
                log.Warn($"Could not record the stamp of {template}: {e.Message}");
            }
        }

        private void SaveLayers()
        {
            var lines = new List<string> { "#layer\tdesired\tlast id\tfailed" };
            lines.AddRange(layers.Select(p => string.Join('\t', p.Key, p.Value.Desired, p.Value.LastId, p.Value.Failed)));
            string path = Path.Combine(folder, LayersFile);
            File.WriteAllLines(path + ".tmp", lines);
            File.Move(path + ".tmp", path, overwrite: true);
        }

        /// <summary>The monitor page's "Ruin Layer Info" rows (empty with the switch off).</summary>
        public List<(uint Desired, uint Active, uint Failed, int LastId)> Rows()
            => !enabled ? new() : layers.Values.Select(l => (l.Desired == 0 ? l.Active : l.Desired, l.Active, l.Failed, l.LastId)).ToList();

        public string Summary()
            => !enabled ? "off" : layers.Count == 0 ? "none yet"
                : string.Join("; ", layers.Select(p => $"layer {p.Key}: desired {(p.Value.Desired == 0 ? p.Value.Active : p.Value.Desired)}, "
                    + $"active {p.Value.Active}, failed {p.Value.Failed}, last id {p.Value.LastId}"));
    }
}
