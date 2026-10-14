using System.IO;
using System.IO.Compression;
using System.IO.Hashing;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LandmarkEmulator.LaunchPad
{
    /// <summary>
    /// Patch packs and the file lists they are checked against. A pack is game files with a pack.json listing them; it
    /// comes from the player (an .lmpatch file) or from the server's patch host (LandmarkEmulator.WebAPI/PatchHost.cs).
    /// The only pack installed is the verified update from the Steam build to Daybreak's final build
    /// (<see cref="CheckUpdate"/>), and only on top of a verified Steam copy of the game (<see cref="SteamList"/>):
    /// LaunchPad is not a game downloader. Installing, undoing and validating is <see cref="GameStore"/>.
    ///
    /// pack.json:
    /// { "format": 1, "name": "...", "version": "0.695.1.132647", "requires": "0.589.1.128895" (optional),
    ///   "description": "...", "files": [ { "path": "Landmark64.exe", "size": 91222528, "crc32": "ED231C3C" }, ... ],
    ///   "delete": [ "path", ... ] (optional) }
    /// A pack is an .lmpatch file (<see cref="PatchFile"/>): its pack.json and the entries (whole files, in-place edits, patches).
    /// </summary>
    public static class Patching
    {
        public sealed class PackFile
        {
            /// <summary>The game file, and what it must be afterwards (size, CRC32).</summary>
            [JsonPropertyName("path")] public string Path { get; set; } = "";
            [JsonPropertyName("size")] public long Size { get; set; }
            [JsonPropertyName("crc32")] public string Crc32 { get; set; } = "";
            /// <summary>
            /// How the pack stores it (format 2): "whole" (the file, at its path in the pack), "edit" (in-place byte ranges,
            /// <see cref="InPlaceEdit"/>, at edits/(path).lme) or "patch" (a rebuild from the old file, <see cref="Delta"/>,
            /// at patches/(path).lmd). Format 1 packs only have whole files.
            /// </summary>
            [JsonPropertyName("how")] public string How { get; set; } = "whole";
            /// <summary>For an edit or patch: the Steam file it starts from (size, CRC32).</summary>
            [JsonPropertyName("fromSize")] public long FromSize { get; set; }
            [JsonPropertyName("fromCrc32")] public string FromCrc32 { get; set; } = "";

            [JsonIgnore] public string Entry => How switch
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

            /// <summary>Checks the format and makes every path a safe relative one (throws on an unsafe path).</summary>
            public Manifest Checked()
            {
                if (Format is not (1 or 2))
                    throw new InvalidDataException($"pack.json format {Format} is newer than this launcher understands.");
                if (Files.Any(f => f.How is not ("whole" or "edit" or "patch") || (Format == 1 && f.How != "whole")))
                    throw new InvalidDataException("pack.json has a file stored in a way this launcher doesn't know.");
                foreach (PackFile f in Files)
                {
                    f.Path = SafeRelative(f.Path) ?? throw new InvalidDataException($"pack.json has an unsafe path: {f.Path}");
                    if (Protected(f.Path))
                        throw new InvalidDataException($"A pack may not change {f.Path}.");
                }
                for (int i = 0; i < Delete.Count; i++)
                {
                    Delete[i] = SafeRelative(Delete[i]) ?? throw new InvalidDataException($"pack.json has an unsafe path: {Delete[i]}");
                    if (Protected(Delete[i]))
                        throw new InvalidDataException($"A pack may not delete {Delete[i]}.");
                }
                return this;
            }
        }

        /// <summary>Progress: phase ("checking", "verifying", "downloading", "backing up", "installing", ...), done, total, file.</summary>
        public delegate void Progress(string phase, long done, long total, string file);

        public sealed class Problem
        {
            public string Path { get; set; } = "";
            public string Issue { get; set; } = "";
        }

        // ---- The file lists (embedded: names, sizes and checksums only) ---------------------------------------------

        /// <summary>The Steam build packs go on: Landmark 0.589.1.128895 (Steam app 297810, depot 297811).</summary>
        public const string SteamBuild = "0.589.1.128895";
        public const string SteamManifestId = "4876286683328705192";

        /// <summary>Daybreak's final build (Feb 2017): packs whose files all match its list are marked official.</summary>
        public const string FinalBuild = "0.695.1.132647";

        private static Dictionary<string, (long Size, string Crc)>? finalList, steamList;

        /// <summary>Daybreak's file list for the final build: path -> size, CRC32.</summary>
        public static Dictionary<string, (long Size, string Crc)> FinalList => finalList ??= LoadList("final-file-list.csv");

        /// <summary>
        /// Steam's own list of the 0.589 install (depot manifest 4876286683328705192, decoded by re/tools/steam_manifest.py):
        /// path -> size, SHA-1. A game folder must match it before anything is patched.
        /// </summary>
        public static Dictionary<string, (long Size, string Crc)> SteamList => steamList ??= LoadList("steam-file-list.csv");

        private static Dictionary<string, (long, string)> LoadList(string resource)
        {
            var list = new Dictionary<string, (long, string)>(StringComparer.OrdinalIgnoreCase);
            using Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(s);
            reader.ReadLine();                                   // header: RelativePath,Size,CRC32|SHA1
            while (reader.ReadLine() is string line)
            {
                string[] f = line.Split(',');
                if (f.Length >= 3 && long.TryParse(f[^2], out long size))
                {
                    string sum = f[^1].Trim().ToUpperInvariant();
                    // Steam lists an empty file's SHA-1 as zeros; the real one is that of no bytes.
                    if (size == 0 && sum.Trim('0').Length == 0)
                        sum = sum.Length == 40 ? EmptySha1 : sum;
                    list[Normalise(string.Join(",", f[..^2]))] = (size, sum);
                }
            }
            return list;
        }

        private const string EmptySha1 = "DA39A3EE5E6B4B0D3255BFEF95601890AFD80709";

        /// <summary>
        /// Steam files LaunchPad itself changes, or that the player's own settings change: the old launcher (replaced by
        /// this one; it also updated itself after Steam installed it) and the .ini settings files next to the game.
        /// They are not checked when verifying the Steam copy.
        /// </summary>
        public static bool Mutable(string rel)
            => rel.StartsWith("LaunchPad.", StringComparison.OrdinalIgnoreCase)
            || (!rel.Contains('\\') && rel.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));

        /// <summary>Files no pack may touch: this launcher and its own folder.</summary>
        public static bool Protected(string rel)
            => rel.Equals("LaunchPad.exe", StringComparison.OrdinalIgnoreCase)
            || rel.StartsWith(GameStore.FolderName + "\\", StringComparison.OrdinalIgnoreCase)
            || rel.Equals(GameStore.FolderName, StringComparison.OrdinalIgnoreCase);

        // ---- Paths and checksums ------------------------------------------------------------------------------------

        /// <summary>A pack path as a safe relative path with "\", or null when it would leave the game folder.</summary>
        public static string? SafeRelative(string path)
        {
            string p = path.Replace('/', '\\').Trim();
            if (p.Length == 0 || Path.IsPathRooted(p) || p.StartsWith("\\") || p.Contains(':'))
                return null;
            string[] parts = p.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Any(part => part == "." || part == ".." || part.Trim().Length == 0))
                return null;
            return string.Join("\\", parts);
        }

        public static string Normalise(string path) => path.Replace('/', '\\').TrimStart('\\');

        /// <summary>CRC32 (zlib / 7-Zip) and SHA-1 of a stream in one read, as hex.</summary>
        public static (string Crc32, string Sha1) Hashes(Stream stream, Action<long>? read = null)
        {
            var crc = new Crc32();
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            byte[] buffer = new byte[1 << 20];
            int n;
            while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                crc.Append(buffer.AsSpan(0, n));
                sha.AppendData(buffer, 0, n);
                read?.Invoke(n);
            }
            return ($"{BitConverter.ToUInt32(crc.GetCurrentHash(), 0):X8}", Convert.ToHexString(sha.GetHashAndReset()));
        }

        public static string Crc32Of(Stream stream, Action<long>? read = null) => Hashes(stream, read).Crc32;

        // ---- Opening a pack ---------------------------------------------------------------------------------------

        /// <summary>A pack: an .lmpatch file (<see cref="PatchFile"/>) with its pack.json and entries.</summary>
        public sealed class Pack : IDisposable
        {
            private readonly PatchFile.Reader file;
            public Manifest Manifest { get; }
            /// <summary>SHA-256 of its pack.json: the pack's identity.</summary>
            public string Id { get; }

            public Pack(string path)
            {
                file = new PatchFile.Reader(path);
                try
                {
                    Id = PatchFile.IdOf(file.Manifest);
                    Manifest = (JsonSerializer.Deserialize<Manifest>(file.Manifest) ?? throw new InvalidDataException("pack.json is empty.")).Checked();
                }
                catch
                {
                    file.Dispose();
                    throw;
                }
            }

            /// <summary>An entry's data (checked against its SHA-256).</summary>
            public byte[] Read(string entry) => file.Read(entry);

            public bool Has(string entry) => file.Has(entry);

            public void Dispose() => file.Dispose();
        }

        /// <summary>What a pack is, before installing it (shown to the player for confirmation).</summary>
        public sealed class PackInfo
        {
            public string Name { get; set; } = "";
            public string Version { get; set; } = "";
            public string Requires { get; set; } = "";
            public string Description { get; set; } = "";
            public int Files { get; set; }
            public long Bytes { get; set; }
            public int Deletes { get; set; }
            /// <summary>It is Daybreak's update to the final build (<see cref="CheckUpdate"/>): the only kind installed.</summary>
            public bool Verified { get; set; }
            /// <summary>The build it leads to (<see cref="Targets"/>), or the closest one when it isn't verified.</summary>
            public string Build { get; set; } = "";
            /// <summary>Why it isn't, for the player.</summary>
            public List<string> Problems { get; set; } = new();
            public string CurrentBuild { get; set; } = "";
            public bool BuildOk { get; set; }
        }

        /// <summary>A build of 0.695 an update may lead to: a name, and the exact file list it must leave.</summary>
        public sealed record TargetBuild(string Id, string Label, Dictionary<string, (long Size, string Crc)> Files);

        private static List<TargetBuild>? targets;

        /// <summary>
        /// The builds the update may lead to, both version <see cref="FinalBuild"/>:
        /// <list type="bullet">
        /// <item>Daybreak's final build: the files of their last update (14 Feb 2017), as their own list has them.</item>
        /// <item>The January 2017 build (accepted 2026-10-15): the same list, but with the exe (built 4 Jan 2017) and the two
        /// English text files from before that last update. Daybreak recompiled the exe on 14 Feb; a compile stamps a new
        /// random debug id into it, so the February exe can't be rebuilt from the January one. The January files are
        /// genuine Daybreak files (trace/launchpad-patching.md); they are held to their exact size and CRC32 here, as every
        /// other file is held to Daybreak's list.</item>
        /// </list>
        /// </summary>
        public static IReadOnlyList<TargetBuild> Targets
        {
            get
            {
                if (targets != null)
                    return targets;
                var january = new Dictionary<string, (long, string)>(FinalList, StringComparer.OrdinalIgnoreCase)
                {
                    ["Landmark64.exe"] = (91222528, "2FF380AE"),
                    ["Locale\\en_us_data.dat"] = (1394253, "FFF22327"),
                    ["Locale\\en_us_data.dir"] = (636370, "6166AEA1")
                };
                return targets = new List<TargetBuild>
                {
                    new("final", "0.695", FinalList),
                    new("january-2017", "0.695", january)
                };
            }
        }

        /// <summary>
        /// The one patch LaunchPad installs, from a player or a server: an update from the Steam build
        /// (<see cref="SteamBuild"/>) to one of the known builds of <see cref="FinalBuild"/> (<see cref="Targets"/>). A pack
        /// passes when it says so (version, requires), every file in it is that build's file (path, size and CRC32), and it
        /// only deletes old Steam files the build doesn't have. No other files, programs or versions get in, and files of
        /// the two builds can't be mixed. (That the result is the whole build is checked against the game folder too:
        /// <see cref="GameStore.CheckComplete"/>.) Returns the reasons it fails (for the closest build); empty = verified.
        /// </summary>
        public static List<string> CheckUpdate(Manifest m) => CheckUpdate(m, out _);

        public static List<string> CheckUpdate(Manifest m, out TargetBuild target)
        {
            List<string>? best = null;
            target = Targets[0];
            foreach (TargetBuild t in Targets)
            {
                List<string> problems = CheckUpdate(m, t);
                if (best == null || problems.Count < best.Count)
                    (best, target) = (problems, t);
                if (problems.Count == 0)
                    break;
            }
            return best!;
        }

        private static List<string> CheckUpdate(Manifest m, TargetBuild t)
        {
            var problems = new List<string>();
            if (m.Version != FinalBuild)
                problems.Add($"it updates to {(m.Version.Length > 0 ? m.Version : "no version")}, not {FinalBuild}");
            if (m.Requires.Length > 0 && m.Requires != SteamBuild)
                problems.Add($"it is for {m.Requires}, not the Steam build {SteamBuild}");
            if (m.Files.Count == 0)
                problems.Add("it has no files");
            var foreign = m.Files.Where(f => !t.Files.TryGetValue(f.Path, out var o) || o.Size != f.Size
                || !o.Crc.Equals(f.Crc32, StringComparison.OrdinalIgnoreCase)).Select(f => f.Path).ToList();
            if (foreign.Count > 0)
                problems.Add($"{foreign.Count} of its files are not files of the {t.Label} build (first: {foreign[0]})");
            var badDeletes = m.Delete.Where(d => t.Files.ContainsKey(d) || !SteamList.ContainsKey(d)).ToList();
            if (badDeletes.Count > 0)
                problems.Add($"it deletes {badDeletes.Count} file(s) that aren't old Steam files (first: {badDeletes[0]})");
            return problems;
        }

        public static PackInfo Describe(Manifest m, string gameFolder)
        {
            string current = Game.IsGameFolder(gameFolder) ? Game.Build(gameFolder) : "";
            List<string> problems = CheckUpdate(m, out TargetBuild target);
            return new PackInfo
            {
                Name = m.Name, Version = m.Version, Requires = m.Requires, Description = m.Description,
                Files = m.Files.Count, Bytes = m.Files.Sum(f => f.Size), Deletes = m.Delete.Count,
                Verified = problems.Count == 0, Problems = problems, Build = target.Label,
                CurrentBuild = current,
                BuildOk = current == SteamBuild
            };
        }

        public static PackInfo Describe(string packPath, string gameFolder)
        {
            using var pack = new Pack(packPath);
            return Describe(pack.Manifest, gameFolder);
        }

        // ---- Making packs (server owners) --------------------------------------------------------------------------

        // ---- The update pack: only the changed parts --------------------------------------------------------------------

        private static List<(string Path, bool New)>? updateFiles;

        /// <summary>
        /// The files the update carries (embedded update-0589-to-0695-files.csv, re/tools/update_files.py): every 0.695 file a
        /// verified Steam copy of 0.589 doesn't already have with the same size and CRC32 (583 new, 511 changed).
        /// </summary>
        public static List<(string Path, bool New)> UpdateFiles
        {
            get
            {
                if (updateFiles != null)
                    return updateFiles;
                var list = new List<(string, bool)>();
                using Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("update-files.csv")!;
                using var reader = new StreamReader(s);
                reader.ReadLine();
                while (reader.ReadLine() is string line)
                {
                    string[] f = line.Split(',');
                    if (f.Length >= 4)
                        list.Add((Normalise(string.Join(",", f[..^3])), f[^1].Trim() == "new"));
                }
                return updateFiles = list;
            }
        }

        /// <summary>
        /// Makes the update pack from a verified Steam copy (<paramref name="steamFolder"/>) and the 0.695 files: a complete
        /// 0.695 game folder (the files that don't match Steam's 0.589 list are taken from it), or zips of the update files
        /// (read without unpacking). Each changed file is stored as the smallest of: an in-place
        /// edit (just the ranges that differ at the same place, plus anything added at the end: the asset packs), a patch
        /// that rebuilds it from the 0.589 file (the exe, text files: their contents shift), or the whole file when neither
        /// is smaller. New files go in whole. Files the 0.695 build doesn't have are listed for deletion. Every file is
        /// checked against the build it leads to. Writes one .lmpatch (<see cref="PatchFile"/>, format 2).
        /// </summary>
        public static (int Edits, int Patches, int Whole, long Bytes) MakeUpdate(string steamFolder, IReadOnlyList<string> from,
            string output, string name, Action<string>? log = null)
        {
            if (!output.EndsWith(PatchFile.Extension, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Patch packs are {PatchFile.Extension} files: give --out a name ending in {PatchFile.Extension}.");
            // Where each 0.695 file comes from: a 0.695 game folder, or zips of the update files (read without unpacking).
            var loaders = new Dictionary<string, Func<byte[]>>(StringComparer.OrdinalIgnoreCase);
            List<(string Path, bool New)> files;
            var opened = new List<ZipArchive>();
            try
            {
                if (from.Count == 1 && Directory.Exists(from[0]))
                {
                    // A 0.695 game folder: the files that don't match Steam's own 0.589 list (not there, or another size or
                    // SHA-1) are the update. Files only a 0.589 install has don't count (they're deleted below).
                    string folder = from[0];
                    files = new List<(string, bool)>();
                    var candidates = Targets[0].Files.Keys.Where(k => !Mutable(k) && !Protected(k)).ToList();
                    int n = 0;
                    foreach (string rel in candidates)
                    {
                        n++;
                        string path = Path.Combine(folder, rel);
                        if (!File.Exists(path))
                            throw new IOException($"The 0.695 folder is missing {rel}: it isn't a complete 0.695 install.");
                        bool isNew = !SteamList.TryGetValue(rel, out var steam);
                        bool changed = isNew || new FileInfo(path).Length != steam.Size;
                        if (!changed)
                        {
                            using FileStream s = File.OpenRead(path);
                            changed = !Hashes(s).Sha1.Equals(steam.Crc, StringComparison.OrdinalIgnoreCase);
                        }
                        if (n % 2000 == 0)
                            log?.Invoke($"  compared {n:N0} of {candidates.Count:N0} files with Steam's 0.589 list");
                        if (!changed)
                            continue;
                        files.Add((rel, isNew));
                        loaders[rel] = () => File.ReadAllBytes(path);
                    }
                    log?.Invoke($"  {files.Count} files differ from 0.589 ({files.Count(f => f.New)} new).");
                }
                else
                {
                    // Zips: every update file (the embedded list), from whichever zip has it.
                    files = UpdateFiles;
                    foreach (string zipPath in from)
                    {
                        ZipArchive zip = ZipFile.OpenRead(zipPath);
                        opened.Add(zip);
                        foreach (ZipArchiveEntry e in zip.Entries)
                            if (!e.FullName.EndsWith("/") && !loaders.ContainsKey(Normalise(e.FullName)))
                            {
                                ZipArchiveEntry entry = e;
                                loaders[Normalise(e.FullName)] = () =>
                                {
                                    using Stream s = entry.Open();
                                    using var copy = new MemoryStream((int)entry.Length);
                                    s.CopyTo(copy);
                                    return copy.ToArray();
                                };
                            }
                    }
                    var missing = files.Where(f => !loaders.ContainsKey(f.Path)).Select(f => f.Path).ToList();
                    if (missing.Count > 0)
                        throw new IOException($"{missing.Count} update file(s) are in none of the zips (first: {missing[0]}).");
                }

                var manifest = new Manifest { Format = 2, Name = name, Version = FinalBuild, Requires = SteamBuild,
                    Description = "Update from the Steam build to 0.695: only the changed parts of each file." };
                int edits = 0, patches = 0, whole = 0;
                using (var pack = new PatchFile.Writer(output))
                {
                    int n = 0;
                    foreach ((string rel, bool isNew) in files)
                    {
                        n++;
                        byte[] now = loaders[rel]();
                        var file = new PackFile { Path = rel, Size = now.Length, Crc32 = $"{Delta.Crc(now):X8}" };
                        string oldPath = Path.Combine(steamFolder, rel);
                        byte[]? body = null;
                        if (!isNew && File.Exists(oldPath))
                        {
                            byte[] old = File.ReadAllBytes(oldPath);
                            InPlaceEdit.Edit e = InPlaceEdit.Create(old, now);
                            byte[] patch = Delta.Create(old, now);
                            // An edit is cheaper to apply (only the ranges are written): take it unless it is much bigger.
                            if (e.Payload <= now.Length / 2 && e.Payload <= patch.Length * 3 / 2)
                            {
                                (file.How, body) = ("edit", InPlaceEdit.Write(e));
                                edits++;
                            }
                            else if (patch.Length < now.Length * 9 / 10)
                            {
                                (file.How, body) = ("patch", patch);
                                patches++;
                            }
                            if (body != null)
                                (file.FromSize, file.FromCrc32) = (old.Length, $"{Delta.Crc(old):X8}");
                        }
                        if (body == null)
                        {
                            (file.How, body) = ("whole", now);
                            whole++;
                        }
                        pack.Add(file.Entry, body);
                        manifest.Files.Add(file);
                        log?.Invoke($"  [{n}/{files.Count}] {file.How,-5} {rel} ({body.Length:N0} of {now.Length:N0} bytes)");
                    }
                    manifest.Delete = SteamList.Keys.Where(k => !Mutable(k) && !Protected(k) && !Targets.Any(t => t.Files.ContainsKey(k)))
                        .OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
                    pack.Finish(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
                }
                return (edits, patches, whole, new FileInfo(output).Length);
            }
            finally
            {
                foreach (ZipArchive z in opened)
                    z.Dispose();
            }
        }

        /// <summary>
        /// Makes a pack from a folder of game files: every file, or with <paramref name="baseFolder"/> only the files that
        /// differ from it (by size and CRC32); with <paramref name="deleteRemoved"/> the files only the base has are listed
        /// for deletion. Writes a .zip (an .lmpatch, for players and for a server's patch folder). Returns the number of files in it.
        /// </summary>
        public static int MakePack(string sourceFolder, string? baseFolder, string output, string name, string version, string requires,
            string description = "", bool deleteRemoved = false, Action<string>? log = null)
        {
            if (!output.EndsWith(PatchFile.Extension, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Patch packs are {PatchFile.Extension} files: give --out a name ending in {PatchFile.Extension}.");
            var manifest = new Manifest { Name = name, Version = version, Requires = requires, Description = description };
            var include = new List<string>();
            string outFull = Path.GetFullPath(output);
            foreach (string file in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(sourceFolder, file);
                if (rel.Equals("pack.json", StringComparison.OrdinalIgnoreCase) || rel.EndsWith(".lpnew", StringComparison.OrdinalIgnoreCase)
                    || Path.GetFullPath(file).StartsWith(outFull, StringComparison.OrdinalIgnoreCase) || Protected(rel)
                    || (baseFolder != null && Mutable(rel)))
                    continue;
                long size = new FileInfo(file).Length;
                string crc;
                using (FileStream s = File.OpenRead(file))
                    crc = Crc32Of(s);
                if (baseFolder != null)
                {
                    string other = Path.Combine(baseFolder, rel);
                    if (File.Exists(other) && new FileInfo(other).Length == size)
                    {
                        using FileStream s = File.OpenRead(other);
                        if (Crc32Of(s) == crc)
                            continue;
                    }
                }
                manifest.Files.Add(new PackFile { Path = rel, Size = size, Crc32 = crc });
                include.Add(rel);
                log?.Invoke($"  + {rel}");
            }
            if (baseFolder != null && deleteRemoved)
                foreach (string file in Directory.EnumerateFiles(baseFolder, "*", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(baseFolder, file);
                    // Only Steam's own files (not logs, caches or settings the base folder picked up).
                    if (!File.Exists(Path.Combine(sourceFolder, rel)) && !Protected(rel) && !Mutable(rel) && SteamList.ContainsKey(rel))
                    {
                        manifest.Delete.Add(rel);
                        log?.Invoke($"  - {rel}");
                    }
                }
            manifest.Format = 2;
            using (var pack = new PatchFile.Writer(output))
            {
                foreach (string rel in include)
                    pack.Add(rel, File.ReadAllBytes(Path.Combine(sourceFolder, rel)));
                pack.Finish(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            }
            return include.Count;
        }
    }
}
