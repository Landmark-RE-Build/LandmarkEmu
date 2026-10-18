using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LandmarkEmulator.EnvServer
{
    /// <summary>
    /// The one-time data move of terrain-service.md §7 step 4: the world's TerrainEdits folder (cells in the root for the
    /// home continent and in &lt;zone&gt;/ for the others, Ownership/, heal-queue.log, layers.v2 and the zone's placement
    /// markers) is copied into the service's own folder, unchanged (the formats stay, §4.2), the first time the service
    /// starts with an empty store. A marker (import.done: source, time, files, bytes) keeps it to one run; the source is
    /// only read. The copy goes to a temporary folder and is renamed into place, so a stopped import leaves nothing half
    /// copied.
    /// <para>
    /// The markers are translated into the service's own records too (spec §4.2): outposts695d.txt (the zone's current
    /// outpost marker: cavern, ruin, corner voxel) → ruins.tsv; spire_base*.txt (corner cell) and landmarks_of_landmark.txt
    /// → stamps.tsv. The zone's markers stay where they are and remain the zone's (Game/Outposts.cs, Spire.cs and
    /// LandmarksOfLandmark.cs decide placement from them, lead L127); the service's copies record what it holds.
    /// </para>
    /// </summary>
    public static class DataImport
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const string Marker = "import.done";

        /// <summary>Folders and files of the world's TerrainEdits not copied (research samples, unfinished writes).</summary>
        private static bool Skip(string relative)
            => relative.StartsWith("version-samples", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);

        /// <summary>Runs the import if it is due; returns what happened (for the log).</summary>
        public static string Run(string source, string data)
        {
            string target = Path.Combine(data, "TerrainEdits");
            string marker = Path.Combine(data, Marker);
            if (File.Exists(marker))
                return $"done before ({File.ReadAllText(marker).Trim()})";
            if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source))
                return string.IsNullOrWhiteSpace(source) ? "no source set (ImportFrom), starting from this folder" : $"source {source} not found, starting from this folder";
            if (Directory.Exists(target) && (Directory.EnumerateFiles(target, "*.cell", SearchOption.AllDirectories).Any()
                || Directory.EnumerateFiles(target, LandmarkEmulator.WorldServer.Game.Terrain.CellPack.FileName, SearchOption.AllDirectories).Any()))
            {
                File.WriteAllText(marker, $"skipped {DateTime.UtcNow:u}: {target} already held cells; {source} not copied");
                return $"skipped: {target} already holds cells";
            }

            string temporary = target + ".importing";
            if (Directory.Exists(temporary))
                Directory.Delete(temporary, true);
            int files = 0;
            long bytes = 0;
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(source, file);
                if (Skip(relative))
                    continue;
                string to = Path.Combine(temporary, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Copy(file, to);
                files++;
                bytes += new FileInfo(to).Length;
            }
            if (Directory.Exists(target))
                Directory.Delete(target, true);   // empty of cells (checked above): only an earlier empty start's folders
            Directory.Move(temporary, target);

            string records = ConvertMarkers(target, data);
            string line = $"{DateTime.UtcNow:u} from {source}: {files} files, {bytes / 1024} KB; {records}";
            File.WriteAllText(marker, line + System.Environment.NewLine);
            log.Info($"Copied the world's terrain: {line}.");
            return line;
        }

        /// <summary>The zone's placement markers as the service's records (ruins.tsv, stamps.tsv); existing records are kept.</summary>
        private static string ConvertMarkers(string terrainEdits, string data)
        {
            var notes = new List<string>();
            string ruins = Path.Combine(data, RuinRecords.RuinsFile);
            string outposts = Path.Combine(terrainEdits, "outposts695d.txt");
            if (!File.Exists(ruins) && File.Exists(outposts))
            {
                // outposts695d.txt: cavern \t ruin \t corner x \t y \t z (global voxels), one line per outpost, home continent.
                var lines = new List<string> { RuinRecords.RuinsHeader };
                int id = 0;
                foreach (string row in File.ReadLines(outposts))
                {
                    string[] f = row.Split('\t');
                    if (f.Length < 5)
                        continue;
                    lines.Add(string.Join('\t', RuinRecords.HomeLayer, ++id, f[1], f[0], f[2], f[3], f[4], "imported"));
                }
                // outposts695d_<zone>.txt (the other landscapes' outposts, audit F2): the same first five columns, then the
                // outpost's number on its landscape and its standing spot; "#complete" ends a fully placed landscape.
                int others = 0;
                foreach (string file in Directory.EnumerateFiles(terrainEdits, "outposts695d_*.txt").OrderBy(f => f, StringComparer.Ordinal))
                {
                    string zone = Path.GetFileNameWithoutExtension(file)["outposts695d_".Length..];
                    if (!uint.TryParse(zone, out _))
                        continue;
                    int instance = 0;   // instance ids count per ruin layer (one layer per landscape, RuinRecords)
                    foreach (string row in File.ReadLines(file))
                    {
                        string[] f = row.Split('\t');
                        if (row.StartsWith('#') || f.Length < 5)
                            continue;
                        lines.Add(string.Join('\t', zone, ++instance, f[1], f[0], f[2], f[3], f[4], "imported"));
                        others++;
                    }
                }
                File.WriteAllLines(ruins, lines);
                notes.Add($"{id} ruin(s) from outposts695d.txt" + (others > 0 ? $", {others} from the other landscapes' markers" : ""));
            }
            string stamps = Path.Combine(data, RuinRecords.StampsFile);
            if (!File.Exists(stamps))
            {
                var lines = new List<string> { RuinRecords.StampsHeader };
                foreach (string file in Directory.EnumerateFiles(terrainEdits, "spire_base*.txt"))
                {
                    // spire_base.txt (home continent) / spire_base_<zone>.txt: "voxel x y z [fill none | fill x0 y0 z0 x1 y1 z1]"
                    // corner voxel and the ground filled under the floor (global voxels) since 2026-10-17; before that
                    // "x y z" corner cell (the world reverts and stamps those again: Spire.RevertOldStamp).
                    string name = Path.GetFileNameWithoutExtension(file);
                    string zone = name == "spire_base" ? "home" : name["spire_base_".Length..];
                    string[] c = File.ReadAllText(file).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (c.Length >= 4 && c[0] == "voxel")
                    {
                        lines.Add(string.Join('\t', lines.Count, "base_portal_spire.otd", zone, "voxel", c[1], c[2], c[3], "imported"));
                        if (c.Length == 11 && c[4] == "fill")
                            lines.Add(string.Join('\t', lines.Count, "spire_fill", zone, "voxel", c[5], c[6], c[7], "imported"));
                    }
                    else if (c.Length == 3)
                        lines.Add(string.Join('\t', lines.Count, "base_portal_spire.otd", zone, "cell", c[0], c[1], c[2], "imported"));
                }
                string landmarks = Path.Combine(terrainEdits, "landmarks_of_landmark.txt");
                if (File.Exists(landmarks))
                    foreach (string row in File.ReadLines(landmarks))
                    {
                        // zone \t world \t template \t x \t y \t z \t size x \t y \t z (voxel corner).
                        string[] f = row.Split('\t');
                        if (f.Length >= 6)
                            lines.Add(string.Join('\t', lines.Count, f[2], f[0], "voxel", f[3], f[4], f[5], "imported"));
                    }
                File.WriteAllLines(stamps, lines);
                notes.Add($"{lines.Count - 1} stamp(s) from spire_base*.txt / landmarks_of_landmark.txt");
            }
            return notes.Count == 0 ? "no markers to record" : string.Join(", ", notes);
        }
    }
}
