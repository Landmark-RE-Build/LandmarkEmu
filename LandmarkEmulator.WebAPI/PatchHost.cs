using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LandmarkEmulator.WebAPI
{
    /// <summary>
    /// Serves the server owner's patch pack to LandmarkEmu LaunchPad (LandmarkEmulator.LaunchPad/ServerPatches.cs). Players'
    /// LaunchPad has this switched off for now (ServerPatches.Enabled, 2026-10-15); the host stays so it can come back.
    ///
    /// The owner puts the pack, as LaunchPad.exe --make-update makes it, in <see cref="Configuration.WebApiConfig.PatchFolder"/>:
    ///   patches\01-update-0695.lmpatch
    /// Packs apply in file-name order. The only pack served is the verified update from the Steam build to a known build
    /// of 0.695 (<see cref="NotTheUpdate"/>; players' LaunchPad checks it again in full). The launcher only patches a
    /// verified Steam copy of the game and only downloads the update, never the game itself.
    ///
    ///   GET /patches                          the packs in order: id, name, version, requires, files, bytes, packBytes, sha256
    ///   GET /patches/{id}/pack.json           a pack's list (the launcher checks it against the sha256 above before downloading)
    ///   GET /patches/{id}/pack.lmpatch        the pack; ranges supported (a broken-off download continues)
    /// </summary>
    public static class PatchHost
    {
        public sealed class PackFile
        {
            [JsonPropertyName("path")] public string Path { get; set; } = "";
            [JsonPropertyName("size")] public long Size { get; set; }
            [JsonPropertyName("crc32")] public string Crc32 { get; set; } = "";
            [JsonPropertyName("how")] public string How { get; set; } = "whole";

            /// <summary>Its entry in the .lmpatch (LaunchPad Patching.PackFile.Entry).</summary>
            public string Entry => How switch
            {
                "edit" => "edits/" + Path.Replace('\\', '/') + ".lme",
                "patch" => "patches/" + Path.Replace('\\', '/') + ".lmd",
                _ => Path.Replace('\\', '/')
            };
        }

        public sealed class Manifest
        {
            [JsonPropertyName("format")] public int Format { get; set; } = 1;
            [JsonPropertyName("name")] public string Name { get; set; } = "";
            [JsonPropertyName("version")] public string Version { get; set; } = "";
            [JsonPropertyName("requires")] public string Requires { get; set; } = "";
            [JsonPropertyName("description")] public string Description { get; set; } = "";
            [JsonPropertyName("files")] public List<PackFile> Files { get; set; } = new();
            [JsonPropertyName("delete")] public List<string> Delete { get; set; } = new();
        }

        /// <summary>A pack as loaded from its .lmpatch (re-read when the file changes).</summary>
        public sealed class Pack
        {
            public string Id { get; init; } = "";
            public string FilePath { get; init; } = "";
            public long FileBytes { get; init; }
            public Manifest Manifest { get; init; } = new();
            /// <summary>Its pack.json text as stored in the .lmpatch (served as is; the launcher checks its SHA-256).</summary>
            public byte[] ManifestBytes { get; init; } = Array.Empty<byte>();
            public string Sha256 { get; init; } = "";
            public DateTime Written { get; init; }
        }

        private static readonly Regex packId = new("^[A-Za-z0-9._-]{1,64}$");

        /// <summary>The build the update must lead to (Daybreak's final build).</summary>
        public const string FinalBuild = "0.695.1.132647";

        private static Dictionary<string, (long Size, string Crc)>? finalList;

        /// <summary>Daybreak's final-build file list (embedded): path ("\", any case) -> size, CRC32.</summary>
        private static Dictionary<string, (long Size, string Crc)> FinalList
        {
            get
            {
                if (finalList != null)
                    return finalList;
                var list = new Dictionary<string, (long, string)>(StringComparer.OrdinalIgnoreCase);
                using Stream s = typeof(PatchHost).Assembly.GetManifestResourceStream("final-file-list.csv")!;
                using var reader = new StreamReader(s);
                reader.ReadLine();
                while (reader.ReadLine() is string line)
                {
                    string[] f = line.Split(',');
                    if (f.Length >= 3 && long.TryParse(f[^2], out long size))
                        list[string.Join(",", f[..^2]).Replace('/', '\\')] = (size, f[^1].Trim().ToUpperInvariant());
                }
                return finalList = list;
            }
        }

        private static List<string>? updateFiles;

        /// <summary>
        /// The files the update must carry (embedded update-0589-to-0695-files.csv): every final-build file a verified
        /// Steam copy of 0.589 doesn't already have with the same size and CRC32 (583 new, 511 changed).
        /// </summary>
        private static List<string> UpdateFiles
        {
            get
            {
                if (updateFiles != null)
                    return updateFiles;
                var list = new List<string>();
                using Stream s = typeof(PatchHost).Assembly.GetManifestResourceStream("update-files.csv")!;
                using var reader = new StreamReader(s);
                reader.ReadLine();
                while (reader.ReadLine() is string line)
                {
                    string[] f = line.Split(',');
                    if (f.Length >= 4)
                        list.Add(string.Join(",", f[..^3]));
                }
                return updateFiles = list;
            }
        }

        /// <summary>
        /// The second accepted build of 0.695 (LaunchPad Patching.Targets): Daybreak's final list, but with the January 2017
        /// exe and English text files from before Daybreak's last update (14 Feb 2017).
        /// </summary>
        private static readonly Dictionary<string, (long Size, string Crc)> JanuaryFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Landmark64.exe"] = (91222528, "2FF380AE"),
            ["Locale\\en_us_data.dat"] = (1394253, "FFF22327"),
            ["Locale\\en_us_data.dir"] = (636370, "6166AEA1")
        };

        /// <summary>
        /// Why a pack isn't an update to one known build of 0.695, or null when it is (checked against each build). Players'
        /// LaunchPad only installs that update (LaunchPad Patching.CheckUpdate, which also checks deletions against the
        /// Steam build and that the result is the whole build), so the host refuses anything else up front: the owner sees
        /// why in the server log instead of their players.
        /// </summary>
        private static string? NotTheUpdate(Manifest m)
        {
            string? final = NotTheUpdate(m, FinalList.TryGetValue);
            if (final == null)
                return null;
            string? january = NotTheUpdate(m, (string path, out (long Size, string Crc) entry) =>
                JanuaryFiles.TryGetValue(path, out entry) || FinalList.TryGetValue(path, out entry));
            if (january == null)
                return null;
            // Report the build the pack is closer to: one carrying the January exe is meant to be the January build.
            bool meantJanuary = m.Files.Any(f => f.Path.Equals("Landmark64.exe", StringComparison.OrdinalIgnoreCase)
                && f.Crc32.Equals(JanuaryFiles["Landmark64.exe"].Crc, StringComparison.OrdinalIgnoreCase));
            return meantJanuary ? "(January 2017 build) " + january : "(final build) " + final;
        }

        private delegate bool Lookup(string path, out (long Size, string Crc) entry);

        private static string? NotTheUpdate(Manifest m, Lookup build)
        {
            if (m.Version != FinalBuild)
                return $"version is \"{m.Version}\", not {FinalBuild}";
            if (m.Files.Count == 0)
                return "no files";
            PackFile? foreign = m.Files.FirstOrDefault(f => !build(f.Path.Replace('/', '\\'), out var o) || o.Size != f.Size
                || !o.Crc.Equals(f.Crc32, StringComparison.OrdinalIgnoreCase));
            if (foreign != null)
                return $"{foreign.Path} is not a file of a known 0.695 build (size or CRC32 differs, or it isn't in Daybreak's list)";
            var carried = new HashSet<string>(m.Files.Select(f => f.Path.Replace('/', '\\')), StringComparer.OrdinalIgnoreCase);
            List<string> missing = UpdateFiles.Where(f => !carried.Contains(f)).ToList();
            if (missing.Count > 0)
                return $"incomplete: {missing.Count} of the update's {UpdateFiles.Count} files are missing (first: {missing[0]})";
            string? kept = m.Delete.FirstOrDefault(d => build(d.Replace('/', '\\'), out _));
            return kept != null ? $"it deletes {kept}, which the build has" : null;
        }

        private static readonly Dictionary<string, Pack> cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object cacheLock = new();

        private static string? Safe(string path)
        {
            string p = path.Replace('/', '\\').Trim();
            if (p.Length == 0 || System.IO.Path.IsPathRooted(p) || p.StartsWith('\\') || p.Contains(':'))
                return null;
            string[] parts = p.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            return parts.Any(x => x == "." || x == ".." || x.Trim().Length == 0) ? null : string.Join("\\", parts);
        }

        /// <summary>The packs (.lmpatch) in <paramref name="folder"/>, in order. A pack that isn't the update is left out (logged once).</summary>
        public static List<Pack> Packs(string folder)
        {
            var packs = new List<Pack>();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return packs;
            folder = System.IO.Path.GetFullPath(folder);
            foreach (string packPath in Directory.EnumerateFiles(folder, "*.lmpatch").OrderBy(f => System.IO.Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
            {
                string id = System.IO.Path.GetFileNameWithoutExtension(packPath);
                if (!packId.IsMatch(id))
                    continue;
                var info = new FileInfo(packPath);
                lock (cacheLock)
                {
                    if (cache.TryGetValue(id, out Pack? known) && known.Written == info.LastWriteTimeUtc && known.FilePath == packPath)
                    {
                        packs.Add(known);
                        continue;
                    }
                }
                try
                {
                    (string manifestText, Dictionary<string, long> entries) = ReadIndex(packPath);
                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(manifestText);
                    Manifest m = JsonSerializer.Deserialize<Manifest>(bytes) ?? throw new InvalidDataException("empty pack.json");
                    if (NotTheUpdate(m) is string why)
                        throw new InvalidDataException($"not a verified update to {FinalBuild}: {why}");
                    // Every listed file has its entry (LaunchPad checks each entry's SHA-256 and each result's CRC32).
                    foreach (PackFile f in m.Files)
                    {
                        if (Safe(f.Path) == null)
                            throw new InvalidDataException($"unsafe path {f.Path}");
                        if (!entries.ContainsKey(f.Entry))
                            throw new InvalidDataException($"{f.Entry} is missing from the pack");
                    }
                    var pack = new Pack
                    {
                        Id = id, FilePath = packPath, FileBytes = info.Length, Manifest = m, ManifestBytes = bytes,
                        Written = info.LastWriteTimeUtc, Sha256 = Convert.ToHexString(SHA256.HashData(bytes))
                    };
                    lock (cacheLock)
                        cache[id] = pack;
                    packs.Add(pack);
                }
                catch (Exception e) when (e is IOException or JsonException or InvalidDataException)
                {
                    lock (cacheLock)
                        if (!cache.ContainsKey("!" + id))
                        {
                            cache["!" + id] = new Pack();
                            Console.WriteLine($"Patch pack {id} is left out: {e.Message}");
                        }
                    // Stop here: later packs build on this one.
                    break;
                }
            }
            return packs;
        }

        /// <summary>What /info and /patches list for a pack.</summary>
        public static object Describe(Pack p) => new
        {
            id = p.Id, name = p.Manifest.Name, version = p.Manifest.Version, requires = p.Manifest.Requires,
            description = p.Manifest.Description, files = p.Manifest.Files.Count, bytes = p.Manifest.Files.Sum(f => f.Size),
            packBytes = p.FileBytes, delete = p.Manifest.Delete.Count, sha256 = p.Sha256
        };

        public static void Map(WebApplication app, Func<string> folder)
        {
            app.MapGet("/patches", () => Results.Ok(Packs(folder()).Select(Describe)));

            app.MapGet("/patches/{id}/pack.json", (string id) =>
            {
                Pack? pack = Packs(folder()).FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                return pack == null ? Results.NotFound() : Results.Bytes(pack.ManifestBytes, "application/json");
            });

            app.MapGet("/patches/{id}/pack.lmpatch", (string id) =>
            {
                Pack? pack = Packs(folder()).FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                return pack == null ? Results.NotFound()
                    : Results.File(pack.FilePath, "application/octet-stream", enableRangeProcessing: true);
            });
        }

        /// <summary>
        /// The manifest text and entry sizes of an .lmpatch (LaunchPad PatchFile.cs): "LMPATCH1", u64 index offset,
        /// u64 index length, then the Brotli-compressed JSON index { manifest, entries: [{ name, offset, stored, size, sha256 }] }.
        /// </summary>
        private static (string Manifest, Dictionary<string, long> Entries) ReadIndex(string path)
        {
            using FileStream file = File.OpenRead(path);
            byte[] header = new byte[24];
            file.ReadExactly(header);
            if (System.Text.Encoding.ASCII.GetString(header, 0, 8) != "LMPATCH1")
                throw new InvalidDataException("not an .lmpatch file");
            long at = BitConverter.ToInt64(header, 8), length = BitConverter.ToInt64(header, 16);
            if (at < 24 || length <= 0 || at + length > file.Length || length > 256 << 20)
                throw new InvalidDataException("damaged index");
            byte[] stored = new byte[length];
            file.Position = at;
            file.ReadExactly(stored);
            using var decoder = new System.IO.Compression.BrotliStream(new MemoryStream(stored), System.IO.Compression.CompressionMode.Decompress);
            using JsonDocument index = JsonDocument.Parse(decoder);
            string manifest = index.RootElement.GetProperty("manifest").GetString() ?? "";
            var entries = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonElement e in index.RootElement.GetProperty("entries").EnumerateArray())
                entries[e.GetProperty("name").GetString() ?? ""] = e.GetProperty("size").GetInt64();
            return (manifest, entries);
        }
    }
}
