using System.IO.Compression;
using System.IO.Hashing;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LandmarkEmulator.PatchPacks
{
    /// <summary>
    /// Patch packs and the file lists they are checked against. A pack is game files with a pack.json listing them; it
    /// comes from the player (an .lmpatch file) or from a server's patch host (LandmarkEmulator.WebAPI/PatchHost.cs).
    /// The only pack installed is the verified update from the Steam build to a known build of 0.695
    /// (<see cref="CheckUpdate(Manifest)"/>), and only on top of a verified Steam copy of the game (<see cref="SteamList"/>):
    /// LaunchPad is not a game downloader. Installing, undoing and validating is <see cref="GameStore"/>; checking a whole
    /// pack (every entry) is <see cref="PackCheck"/>. The same code runs in LaunchPad and on the server.
    ///
    /// pack.json:
    /// { "format": 2, "name": "...", "version": "0.695.1.132647", "requires": "0.589.1.128895",
    ///   "description": "...", "files": [ { "path": "Landmark64.exe", "size": 91222528, "crc32": "2FF380AE",
    ///   "sha256": "...", "how": "patch", "fromSize": 90288128, "fromCrc32": "..." }, ... ], "delete": [ "path", ... ] }
    /// A pack is an .lmpatch file (<see cref="PatchFile"/>): its pack.json and the entries (whole files, in-place edits, patches).
    ///
    /// Trust: a file is only ever trusted on its SHA-256 from the embedded list of genuine files
    /// (Data/landmark-0695-files-sha256.csv, tools/strong_hash_list.py), or, for a file 0.695 kept from 0.589, on Steam's
    /// own SHA-1. Size and CRC32 only pick which file is meant: a CRC32 is easy to forge (until 2026-10-18 it was the
    /// whole check, so a server owner could have sent a harmful Landmark64.exe with the right size and CRC32).
    /// </summary>
    public static class Patching
    {
        public sealed class PackFile
        {
            /// <summary>The game file, and what it must be afterwards (size, CRC32; its SHA-256 comes from the embedded list).</summary>
            [JsonPropertyName("path")] public string Path { get; set; } = "";
            [JsonPropertyName("size")] public long Size { get; set; }
            [JsonPropertyName("crc32")] public string Crc32 { get; set; } = "";
            /// <summary>
            /// Its SHA-256 (packs made since 2026-10-18; for information). Never trusted on its own: when present it must equal
            /// the embedded list's, which is what every check uses.
            /// </summary>
            [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
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

            /// <summary>
            /// Checks the format and makes every path a safe relative one (throws on an unsafe path, or a path listed twice:
            /// a pack that writes one file twice, or writes and deletes it, could make its checked result differ from what
            /// ends up on disk).
            /// </summary>
            public Manifest Checked()
            {
                if (Format is not (1 or 2))
                    throw new InvalidDataException($"pack.json format {Format} is newer than this launcher understands.");
                if (Files == null || Delete == null)
                    throw new InvalidDataException("pack.json is damaged (no file list).");
                if (Files.Count > MaxManifestFiles || Delete.Count > MaxManifestFiles)
                    throw new InvalidDataException("pack.json lists more files than any build of the game has.");
                if (Files.Any(f => f == null || f.How is not ("whole" or "edit" or "patch") || (Format == 1 && f.How != "whole")))
                    throw new InvalidDataException("pack.json has a file stored in a way this launcher doesn't know.");
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (PackFile f in Files)
                {
                    f.Path = SafeRelative(f.Path) ?? throw new InvalidDataException($"pack.json has an unsafe path: {f.Path}");
                    if (Protected(f.Path))
                        throw new InvalidDataException($"A pack may not change {f.Path}.");
                    if (!seen.Add(f.Path))
                        throw new InvalidDataException($"pack.json lists {f.Path} twice.");
                    f.Crc32 ??= "";
                    f.Sha256 ??= "";
                    f.FromCrc32 ??= "";
                }
                for (int i = 0; i < Delete.Count; i++)
                {
                    Delete[i] = SafeRelative(Delete[i] ?? "") ?? throw new InvalidDataException($"pack.json has an unsafe path: {Delete[i]}");
                    if (Protected(Delete[i]))
                        throw new InvalidDataException($"A pack may not delete {Delete[i]}.");
                    if (!seen.Add(Delete[i]))
                        throw new InvalidDataException($"pack.json lists {Delete[i]} twice (or both writes and deletes it).");
                }
                Name ??= "";
                Version ??= "";
                Requires ??= "";
                Description ??= "";
                return this;
            }
        }

        /// <summary>No build of the game has more files than this (34,138 in 0.695): a longer list is refused unread.</summary>
        public const int MaxManifestFiles = 100_000;

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

        /// <summary>The version of both known 0.695 builds (<see cref="Targets"/>).</summary>
        public const string FinalBuild = "0.695.1.132647";

        /// <summary>
        /// The lists everything is checked against. Loaded once from the embedded resources; the tests swap in their own
        /// small ones (<see cref="UseLists"/>) so they never need game files.
        /// </summary>
        internal sealed class Lists
        {
            /// <summary>Daybreak's list of the final build: path -> size, CRC32.</summary>
            public Dictionary<string, (long Size, string Crc)> Final { get; init; } = new(StringComparer.OrdinalIgnoreCase);
            /// <summary>The January 2017 build's files that differ from the final list: path -> size, CRC32.</summary>
            public Dictionary<string, (long Size, string Crc)> January { get; init; } = new(StringComparer.OrdinalIgnoreCase);
            /// <summary>Steam's list of the 0.589 install: path -> size, SHA-1.</summary>
            public Dictionary<string, (long Size, string Crc)> Steam { get; init; } = new(StringComparer.OrdinalIgnoreCase);
            /// <summary>The genuine files' SHA-256: "path|size|CRC32" (path in lower case) -> SHA-256 ("" = no copy was found).</summary>
            public Dictionary<string, string> Strong { get; init; } = new(StringComparer.Ordinal);
            /// <summary>The files the update carries: every 0.695 file a verified Steam copy doesn't already have.</summary>
            public List<(string Path, bool New)> UpdateFiles { get; init; } = new();
        }

        private static Lists? lists;
        private static List<TargetBuild>? targets;

        private static Lists L => lists ??= LoadEmbedded();

        /// <summary>For the tests: use these lists until the result is disposed.</summary>
        internal static IDisposable UseLists(Lists test)
        {
            Lists? before = lists;
            lists = test;
            targets = null;
            return new Restore(() => { lists = before; targets = null; });
        }

        private sealed class Restore(Action undo) : IDisposable
        {
            public void Dispose() => undo();
        }

        internal static string StrongKey(string path, long size, string crc) => $"{Normalise(path).ToLowerInvariant()}|{size}|{crc.ToUpperInvariant()}";

        private static Lists LoadEmbedded()
        {
            var updateFiles = new List<(string, bool)>();
            foreach (string[] f in ReadCsv("update-files.csv", 4))
                updateFiles.Add((Normalise(string.Join(",", f[..^3])), f[^1].Trim() == "new"));
            var strong = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string[] f in ReadCsv("strong-file-list.csv", 4))
                if (long.TryParse(f[^3], out long size))
                    strong[StrongKey(string.Join(",", f[..^3]), size, f[^2].Trim())] = f[^1].Trim().ToUpperInvariant();
            return new Lists
            {
                Final = LoadList("final-file-list.csv"),
                Steam = LoadList("steam-file-list.csv"),
                // The January 2017 build (accepted 2026-10-15): see Targets.
                January = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["Landmark64.exe"] = (91222528, "2FF380AE"),
                    ["Locale\\en_us_data.dat"] = (1394253, "FFF22327"),
                    ["Locale\\en_us_data.dir"] = (636370, "6166AEA1")
                },
                Strong = strong,
                UpdateFiles = updateFiles
            };
        }

        private static IEnumerable<string[]> ReadCsv(string resource, int minColumns)
        {
            using Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"The embedded list {resource} is missing from this build.");
            using var reader = new StreamReader(s);
            reader.ReadLine();                                   // the header
            while (reader.ReadLine() is string line)
            {
                string[] f = line.Split(',');
                if (f.Length >= minColumns)
                    yield return f;
            }
        }

        /// <summary>Daybreak's file list for the final build: path -> size, CRC32.</summary>
        public static Dictionary<string, (long Size, string Crc)> FinalList => L.Final;

        /// <summary>
        /// Steam's own list of the 0.589 install (depot manifest 4876286683328705192, decoded by re/tools/steam_manifest.py):
        /// path -> size, SHA-1 (the field is called Crc for history's sake; it holds the SHA-1). A game folder must match
        /// it before anything is patched.
        /// </summary>
        public static Dictionary<string, (long Size, string Crc)> SteamList => L.Steam;

        private static Dictionary<string, (long, string)> LoadList(string resource)
        {
            var list = new Dictionary<string, (long, string)>(StringComparer.OrdinalIgnoreCase);
            foreach (string[] f in ReadCsv(resource, 3))
                if (long.TryParse(f[^2], out long size))
                {
                    string sum = f[^1].Trim().ToUpperInvariant();
                    // Steam lists an empty file's SHA-1 as zeros; the real one is that of no bytes.
                    if (size == 0 && sum.Trim('0').Length == 0)
                        sum = sum.Length == 40 ? EmptySha1 : sum;
                    list[Normalise(string.Join(",", f[..^2]))] = (size, sum);
                }
            return list;
        }

        private const string EmptySha1 = "DA39A3EE5E6B4B0D3255BFEF95601890AFD80709";

        /// <summary>
        /// Steam files LaunchPad itself changes, or that the player's own settings change: the old launcher (replaced by
        /// this one; it also updated itself after Steam installed it) and the .ini settings files next to the game.
        /// They are not checked when verifying the Steam copy, and no pack may carry them.
        /// </summary>
        public static bool Mutable(string rel)
            => rel.StartsWith("LaunchPad", StringComparison.OrdinalIgnoreCase)
            || (!rel.Contains('\\') && rel.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Files no pack may touch: this launcher and anything of the old one (LaunchPad.exe, LaunchPad.libs\, ...: every
        /// name at the top that starts with "LaunchPad"), and LaunchPad's own folder (LaunchPadEmu\).
        /// </summary>
        public static bool Protected(string rel)
            => rel.Split('\\')[0].StartsWith("LaunchPad", StringComparison.OrdinalIgnoreCase);

        // ---- Paths and checksums ------------------------------------------------------------------------------------

        private static readonly HashSet<string> DeviceNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$", "CLOCK$",
            "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
            "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³"
        };

        /// <summary>
        /// A pack path as a safe relative path with "\", or null when it could leave the game folder or name something
        /// other than a plain file: rooted or drive paths, "." / "..", ':' (also alternate data streams "file:stream"),
        /// Windows device names (CON, NUL, COM1, ... also with an extension), wildcard and control characters, and names
        /// ending in a dot or space (Windows drops those, so "a.exe." would be written as "a.exe").
        /// </summary>
        public static string? SafeRelative(string path)
        {
            if (path == null)
                return null;
            string p = path.Replace('/', '\\');
            if (p.Length == 0 || p.Length > 400 || p != p.Trim() || Path.IsPathRooted(p) || p.StartsWith('\\') || p.Contains(':'))
                return null;
            if (p.Any(c => c < 0x20 || c is '*' or '?' or '"' or '<' or '>' or '|'))
                return null;
            string[] parts = p.Split('\\');
            foreach (string part in parts)
            {
                if (part.Length == 0 || part == "." || part == ".." || part.EndsWith('.') || part.EndsWith(' ') || part.StartsWith(' '))
                    return null;
                string stem = part.Split('.')[0];
                if (DeviceNames.Contains(stem))
                    return null;
            }
            return string.Join("\\", parts);
        }

        public static string Normalise(string path) => path.Replace('/', '\\').TrimStart('\\');

        /// <summary>CRC32 (zlib / 7-Zip), SHA-1 and SHA-256 of a stream in one read, as hex.</summary>
        public static (string Crc32, string Sha1, string Sha256) AllHashes(Stream stream, Action<long>? read = null)
        {
            var crc = new Crc32();
            using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[1 << 20];
            int n;
            while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                crc.Append(buffer.AsSpan(0, n));
                sha1.AppendData(buffer, 0, n);
                sha256.AppendData(buffer, 0, n);
                read?.Invoke(n);
            }
            return ($"{BitConverter.ToUInt32(crc.GetCurrentHash(), 0):X8}", Convert.ToHexString(sha1.GetHashAndReset()),
                Convert.ToHexString(sha256.GetHashAndReset()));
        }

        /// <summary>CRC32 and SHA-1 of a stream in one read, as hex.</summary>
        public static (string Crc32, string Sha1) Hashes(Stream stream, Action<long>? read = null)
        {
            var (crc, sha1, _) = AllHashes(stream, read);
            return (crc, sha1);
        }

        public static string Crc32Of(Stream stream, Action<long>? read = null) => Hashes(stream, read).Crc32;

        public static string Sha256Of(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

        public static string Sha256Of(Stream stream) => Convert.ToHexString(SHA256.HashData(stream));

        public static bool Same(string a, string b) => a.Length > 0 && a.Equals(b, StringComparison.OrdinalIgnoreCase);

        // ---- Opening a pack ---------------------------------------------------------------------------------------

        /// <summary>A pack: an .lmpatch file (<see cref="PatchFile"/>) with its pack.json and entries.</summary>
        public sealed class Pack : IDisposable
        {
            private readonly PatchFile.Reader file;
            public Manifest Manifest { get; }
            /// <summary>SHA-256 of its pack.json: the pack's identity.</summary>
            public string Id { get; }
            public string FilePath { get; }

            public Pack(string path)
            {
                FilePath = path;
                file = new PatchFile.Reader(path);
                try
                {
                    Id = PatchFile.IdOf(file.Manifest);
                    Manifest = ParseManifest(file.Manifest);
                }
                catch
                {
                    file.Dispose();
                    throw;
                }
            }

            /// <summary>An entry's data (checked against its SHA-256; never more than its declared size).</summary>
            public byte[] Read(string entry) => file.Read(entry);

            public bool Has(string entry) => file.Has(entry);

            /// <summary>The pack's entries, as its index lists them.</summary>
            public IEnumerable<PatchFile.IndexEntry> Entries => file.Entries;

            public PatchFile.IndexEntry? Entry(string name) => file.Entry(name);

            public void Dispose() => file.Dispose();
        }

        /// <summary>A pack.json text as a checked manifest (throws when it is damaged or unsafe).</summary>
        public static Manifest ParseManifest(string text)
        {
            try
            {
                return (JsonSerializer.Deserialize<Manifest>(text) ?? throw new InvalidDataException("pack.json is empty.")).Checked();
            }
            catch (JsonException e)
            {
                throw new InvalidDataException($"pack.json is damaged: {e.Message}");
            }
        }

        public static Manifest ParseManifest(byte[] utf8) => ParseManifest(System.Text.Encoding.UTF8.GetString(utf8));

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
            /// <summary>It is the update to a known 0.695 build (<see cref="CheckUpdate(Manifest)"/>): the only kind installed.</summary>
            public bool Verified { get; set; }
            /// <summary>The build it leads to (<see cref="Targets"/>), or the closest one when it isn't verified.</summary>
            public string Build { get; set; } = "";
            /// <summary>Why it isn't, for the player.</summary>
            public List<string> Problems { get; set; } = new();
            /// <summary>Files it would write that are only checked by size and CRC32 (the player's February opt-in).</summary>
            public List<string> Weak { get; set; } = new();
            public string CurrentBuild { get; set; } = "";
            public bool BuildOk { get; set; }
        }

        /// <summary>One file of a build: what it must be (size, CRC32) and its SHA-256 ("" = LaunchPad has no copy's SHA-256).</summary>
        public readonly record struct BuildFile(long Size, string Crc, string Sha256);

        /// <summary>A build of 0.695 an update may lead to: a name, and the exact file list it must leave.</summary>
        public sealed record TargetBuild(string Id, string Label, Dictionary<string, BuildFile> Files)
        {
            /// <summary>Files of this build no local copy was found of: the build can't be verified while it has any.</summary>
            public IEnumerable<string> Unverifiable => Files.Where(f => f.Value.Sha256.Length == 0 && !Mutable(f.Key)).Select(f => f.Key);
        }

        /// <summary>
        /// The builds the update may lead to, both version <see cref="FinalBuild"/>:
        /// <list type="bullet">
        /// <item>Daybreak's final build: the files of their last update (14 Feb 2017), as their own list has them.</item>
        /// <item>The January 2017 build (accepted 2026-10-15): the same list, but with the exe (built 4 Jan 2017) and the two
        /// English text files from before that last update. Daybreak recompiled the exe on 14 Feb; a compile stamps a new
        /// random debug id into it, so the February exe can't be rebuilt from the January one. The January files are
        /// genuine Daybreak files (trace/launchpad-patching.md).</item>
        /// </list>
        /// Each file carries its SHA-256 from the embedded strong list; a file with none can't be verified, so a pack that
        /// carries it is refused. (Both builds are fully covered as of 2026-10-20.)
        /// </summary>
        public static IReadOnlyList<TargetBuild> Targets
        {
            get
            {
                if (targets != null)
                    return targets;
                Lists l = L;
                Dictionary<string, BuildFile> Build(Dictionary<string, (long Size, string Crc)> files)
                {
                    var build = new Dictionary<string, BuildFile>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (path, (size, crc)) in files)
                        build[path] = new BuildFile(size, crc, l.Strong.TryGetValue(StrongKey(path, size, crc), out string? sha) ? sha : "");
                    return build;
                }
                var january = new Dictionary<string, (long, string)>(l.Final, StringComparer.OrdinalIgnoreCase);
                foreach (var (path, file) in l.January)
                    january[path] = file;
                return targets = new List<TargetBuild>
                {
                    // Labels keep the 0.695 pack version; the Feb exe's own build string is 0.699.1.132863 (GameFolder.Build).
                    new("final", "0.695 / 0.699 (Daybreak's final build, Feb 2017)", Build(l.Final)),
                    new("january-2017", "0.695 (January 2017 build)", Build(january))
                };
            }
        }

        /// <summary>
        /// The final (Feb 2017) build's files that differ from the January build: Landmark64.exe and the English text.
        /// Their SHA-256s are in the strong list (2026-10-20); <see cref="WeakAllowed"/> is kept for older LaunchPads /
        /// tests that still omit those fingerprints.
        /// </summary>
        public static readonly string[] FebruaryOnlyPaths = { "Landmark64.exe", "Locale\\en_us_data.dat", "Locale\\en_us_data.dir" };

        /// <summary>
        /// May this file be accepted on its size and CRC32 alone? Only with the player's own opt-in
        /// (<paramref name="allowFebruary"/>: LaunchPad's setting "Allow the February 2017 build's three files without a
        /// strong fingerprint", off by default, for packs the player chose themselves; never for a server's pack, never
        /// on the server), only for the final build's three <see cref="FebruaryOnlyPaths"/>, and only while the strong list
        /// has no SHA-256 for them: once it does (a LaunchPad update), they are fully verified like every other file and
        /// this never applies again.
        /// </summary>
        public static bool WeakAllowed(TargetBuild t, string path, bool allowFebruary)
            => allowFebruary && t.Id == "final" && FebruaryOnlyPaths.Contains(path, StringComparer.OrdinalIgnoreCase)
            && t.Files.TryGetValue(path, out BuildFile f) && f.Sha256.Length == 0;

        /// <summary>The files of a pack that would be accepted on size and CRC32 alone (<see cref="WeakAllowed"/>).</summary>
        public static List<string> WeakFiles(Manifest m, TargetBuild t, bool allowFebruary)
            => m.Files.Where(f => WeakAllowed(t, f.Path, allowFebruary)).Select(f => f.Path).ToList();

        /// <summary>A target build by its id ("final", "january-2017"), or null.</summary>
        public static TargetBuild? Target(string id) => Targets.FirstOrDefault(t => t.Id == id);

        /// <summary>
        /// The one patch LaunchPad installs, from a player or a server: an update from the Steam build
        /// (<see cref="SteamBuild"/>) to one of the known builds of <see cref="FinalBuild"/> (<see cref="Targets"/>). This
        /// checks the list (pack.json) alone, so it runs before anything is read or downloaded. A pack passes when:
        /// <list type="number">
        /// <item>it says so (version, requires);</item>
        /// <item>every file in it is that build's file by path, size and CRC32, and LaunchPad holds that file's SHA-256 (which
        /// every byte is checked against later; a file no genuine copy's SHA-256 is known of is refused, never trusted on
        /// its CRC32);</item>
        /// <item>it carries every file of the update (no half updates) and no settings file;</item>
        /// <item>an edit or patch starts from the Steam file at that path (its size);</item>
        /// <item>it only deletes old Steam files the build doesn't have.</item>
        /// </list>
        /// No other files, programs or versions get in, and files of the two builds can't be mixed. (That the result is
        /// the whole build is checked against the game folder too: <see cref="GameStore.CheckComplete"/>; every entry is
        /// checked by <see cref="PackCheck"/> and again as it is installed.) Returns the reasons it fails (for the closest
        /// build); empty = verified.
        /// </summary>
        public static List<string> CheckUpdate(Manifest m) => CheckUpdate(m, out _);

        /// <param name="allowFebruary">The player's opt-in for the final build's three files without a SHA-256 (<see cref="WeakAllowed"/>).</param>
        public static List<string> CheckUpdate(Manifest m, out TargetBuild target, bool allowFebruary = false)
        {
            List<string>? best = null;
            target = Targets[0];
            foreach (TargetBuild t in Targets)
            {
                List<string> problems = CheckUpdate(m, t, allowFebruary);
                if (best == null || problems.Count < best.Count)
                    (best, target) = (problems, t);
                if (problems.Count == 0)
                    break;
            }
            return best!;
        }

        private static bool IsHex(string s, int length) => s.Length == length && s.All(Uri.IsHexDigit);

        public static List<string> CheckUpdate(Manifest m, TargetBuild t, bool allowFebruary = false)
        {
            var problems = new List<string>();
            if (m.Version != FinalBuild)
                problems.Add($"it updates to {(m.Version.Length > 0 ? m.Version : "no version")}, not {FinalBuild}");
            if (m.Requires.Length > 0 && m.Requires != SteamBuild)
                problems.Add($"it is for {m.Requires}, not the Steam build {SteamBuild}");
            if (m.Files.Count == 0)
                problems.Add("it has no files");
            var foreign = m.Files.Where(f => !t.Files.TryGetValue(f.Path, out BuildFile o) || o.Size != f.Size
                || !o.Crc.Equals(f.Crc32, StringComparison.OrdinalIgnoreCase)).Select(f => f.Path).ToList();
            if (foreign.Count > 0)
                problems.Add($"{foreign.Count} of its files are not files of the {t.Label} (first: {foreign[0]})");
            var unknown = m.Files.Where(f => t.Files.TryGetValue(f.Path, out BuildFile o) && o.Size == f.Size
                && o.Crc.Equals(f.Crc32, StringComparison.OrdinalIgnoreCase) && o.Sha256.Length == 0
                && !WeakAllowed(t, f.Path, allowFebruary)).Select(f => f.Path).ToList();
            if (unknown.Count > 0)
                problems.Add($"{unknown.Count} of its files can't be verified: LaunchPad has no SHA-256 of the {t.Label}'s " +
                    $"version (first: {unknown[0]}), and a CRC32 alone is not trusted");
            var claimed = m.Files.Where(f => f.Sha256.Length > 0 && t.Files.TryGetValue(f.Path, out BuildFile o) && o.Sha256.Length > 0
                && !f.Sha256.Equals(o.Sha256, StringComparison.OrdinalIgnoreCase)).Select(f => f.Path).ToList();
            if (claimed.Count > 0)
                problems.Add($"{claimed.Count} of its files claim a SHA-256 that isn't the genuine file's (first: {claimed[0]})");
            var settings = m.Files.Where(f => Mutable(f.Path)).Select(f => f.Path).ToList();
            if (settings.Count > 0)
                problems.Add($"it carries settings files, which aren't part of the update (first: {settings[0]})");
            var badBase = m.Files.Where(f => f.How != "whole" && (!SteamList.TryGetValue(f.Path, out var steam) || steam.Size != f.FromSize
                || Mutable(f.Path) || !IsHex(f.FromCrc32, 8))).Select(f => f.Path).ToList();
            if (badBase.Count > 0)
                problems.Add($"{badBase.Count} of its edits or patches don't start from the Steam file at that path (first: {badBase[0]})");
            var carried = new HashSet<string>(m.Files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
            var missing = L.UpdateFiles.Where(u => !Mutable(u.Path) && !carried.Contains(u.Path)).Select(u => u.Path).ToList();
            if (missing.Count > 0)
                problems.Add($"it is incomplete: {missing.Count} of the update's {L.UpdateFiles.Count} files are missing (first: {missing[0]})");
            var badDeletes = m.Delete.Where(d => t.Files.ContainsKey(d) || !SteamList.ContainsKey(d) || Mutable(d)).ToList();
            if (badDeletes.Count > 0)
                problems.Add($"it deletes {badDeletes.Count} file(s) that aren't old Steam files (first: {badDeletes[0]})");
            return problems;
        }

        public static PackInfo Describe(Manifest m, string gameFolder, bool allowFebruary = false)
        {
            string current = GameFolder.IsGameFolder(gameFolder) ? GameFolder.Build(gameFolder) : "";
            List<string> problems = CheckUpdate(m, out TargetBuild target, allowFebruary);
            return new PackInfo
            {
                Name = m.Name, Version = m.Version, Requires = m.Requires, Description = m.Description,
                Files = m.Files.Count, Bytes = m.Files.Sum(f => f.Size), Deletes = m.Delete.Count,
                Verified = problems.Count == 0, Problems = problems, Build = target.Label,
                CurrentBuild = current,
                BuildOk = current == SteamBuild,
                Weak = problems.Count == 0 ? WeakFiles(m, target, allowFebruary) : new()
            };
        }

        public static PackInfo Describe(string packPath, string gameFolder, bool allowFebruary = false)
        {
            using var pack = new Pack(packPath);
            PackInfo info = Describe(pack.Manifest, gameFolder, allowFebruary);
            if (info.Verified)
            {
                // The pack's own index too (cheap: no entry is unpacked here; every entry is checked as it is installed).
                List<string> index = PackCheck.CheckIndex(pack, out _, allowFebruary);
                info.Problems.AddRange(index);
                info.Verified = index.Count == 0;
            }
            return info;
        }

        // ---- Checking a 0.695 game folder (for making the update) -------------------------------------------------------

        /// <summary>What a 0.695 game folder is, file by file against the strong list.</summary>
        public sealed class FolderCheck
        {
            /// <summary>The build every update file matches (by SHA-256), or null.</summary>
            public TargetBuild? Build { get; set; }
            /// <summary>For the closest build: the files that are missing, different, or can't be verified.</summary>
            public List<Problem> Problems { get; set; } = new();
            public string Closest { get; set; } = "";
            public int Checked { get; set; }
            /// <summary>Each checked file's SHA-256 (so making the pack needn't read it twice to know it is genuine).</summary>
            public Dictionary<string, string> Sha256 { get; } = new(StringComparer.OrdinalIgnoreCase);
            /// <summary>Files only checked by size and CRC32 (the player's February opt-in).</summary>
            public List<string> Weak { get; } = new();
        }

        /// <summary>
        /// Checks the files of the update in a 0.695 game folder (the ones a pack carries: <see cref="Lists.UpdateFiles"/>)
        /// against the SHA-256 of each known build. Says which build it is, or exactly which files are missing, changed or
        /// can't be verified (for the build it is closest to). Only reads the folder.
        /// </summary>
        public static FolderCheck CheckFolder(string folder, Progress? progress = null, CancellationToken cancel = default,
            bool allowFebruary = false)
        {
            var result = new FolderCheck();
            var files = L.UpdateFiles.Where(u => !Mutable(u.Path) && !Protected(u.Path)).Select(u => u.Path).ToList();
            var have = new Dictionary<string, (long Size, string Crc, string Sha)?>(StringComparer.OrdinalIgnoreCase);
            long total = 0, done = 0;
            foreach (string rel in files)
            {
                var info = new FileInfo(Path.Combine(folder, rel));
                total += info.Exists ? info.Length : 0;
            }
            foreach (string rel in files)
            {
                cancel.ThrowIfCancellationRequested();
                progress?.Invoke("verifying", done, total, rel);
                var info = new FileInfo(Path.Combine(folder, rel));
                if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    have[rel] = null;
                    continue;
                }
                using FileStream s = info.OpenRead();
                var (crc, _, sha) = AllHashes(s, n => { done += n; progress?.Invoke("verifying", done, total, rel); });
                have[rel] = (info.Length, crc, sha);
                result.Sha256[rel] = sha;
            }
            result.Checked = files.Count;
            List<Problem>? best = null;
            foreach (TargetBuild t in Targets)
            {
                var problems = new List<Problem>();
                foreach (string rel in files)
                {
                    if (!t.Files.TryGetValue(rel, out BuildFile want))
                        continue;
                    var h = have[rel];
                    if (h == null)
                        problems.Add(new Problem { Path = rel, Issue = "missing" });
                    else if (h.Value.Size != want.Size || !h.Value.Crc.Equals(want.Crc, StringComparison.OrdinalIgnoreCase))
                        problems.Add(new Problem { Path = rel, Issue = $"not the {t.Label}'s version" });
                    else if (want.Sha256.Length == 0 && !WeakAllowed(t, rel, allowFebruary))
                        problems.Add(new Problem { Path = rel, Issue = $"the {t.Label}'s version, but LaunchPad has no SHA-256 of it, so it can't be verified" });
                    else if (want.Sha256.Length > 0 && !h.Value.Sha.Equals(want.Sha256, StringComparison.OrdinalIgnoreCase))
                        problems.Add(new Problem { Path = rel, Issue = "changed: right size and CRC32 but not the genuine file (SHA-256 differs)" });
                }
                if (problems.Count == 0)
                {
                    result.Build = t;
                    result.Problems.Clear();
                    result.Closest = t.Label;
                    result.Weak.AddRange(files.Where(rel => WeakAllowed(t, rel, allowFebruary)));
                    return result;
                }
                if (best == null || problems.Count < best.Count)
                {
                    best = problems;
                    result.Closest = t.Label;
                }
            }
            result.Problems = best ?? new List<Problem>();
            return result;
        }

        // ---- The update pack: only the changed parts --------------------------------------------------------------------

        /// <summary>
        /// The files the update carries (embedded update-0589-to-0695-files.csv, re/tools/update_files.py): every 0.695 file a
        /// verified Steam copy of 0.589 doesn't already have with the same size and CRC32 (583 new, 511 changed).
        /// </summary>
        public static List<(string Path, bool New)> UpdateFiles => L.UpdateFiles;

        /// <summary>
        /// Makes the update pack from the 0.695 files: a 0.695 game folder (the update's files are taken from it), or zips of
        /// the update files (read without unpacking). With a verified Steam copy (<paramref name="steamFolder"/>) each changed
        /// file is stored as the smallest of: an in-place edit (just the ranges that differ at the same place, plus anything
        /// added at the end: the asset packs), a patch that rebuilds it from the 0.589 file (the exe, text files: their
        /// contents shift), or the whole file when neither is smaller; without one (null) every file goes in whole (bigger,
        /// and it still installs on any verified Steam copy). New files go in whole. Files the 0.695 build doesn't have are
        /// listed for deletion. Every file read is held to the SHA-256 of the build it leads to before it goes in, so a
        /// pack only ever carries genuine files. Writes one .lmpatch (<see cref="PatchFile"/>, format 2); a failed or stopped
        /// run leaves nothing behind.
        /// </summary>
        public static (int Edits, int Patches, int Whole, long Bytes) MakeUpdate(string? steamFolder, IReadOnlyList<string> from,
            string output, string name, Action<string>? log = null, Progress? progress = null, CancellationToken cancel = default,
            bool allowFebruary = false)
        {
            if (!output.EndsWith(PatchFile.Extension, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Patch packs are {PatchFile.Extension} files: give --out a name ending in {PatchFile.Extension}.");
            // Where each 0.695 file comes from: a 0.695 game folder, or zips of the update files (read without unpacking).
            var loaders = new Dictionary<string, Func<byte[]>>(StringComparer.OrdinalIgnoreCase);
            var files = UpdateFiles.Where(f => !Mutable(f.Path) && !Protected(f.Path)).ToList();
            var opened = new List<ZipArchive>();
            string partial = output[..^PatchFile.Extension.Length] + ".partial" + PatchFile.Extension;
            try
            {
                if (from.Count == 1 && Directory.Exists(from[0]))
                {
                    // A 0.695 game folder: the update's files (the 0.695 files a verified Steam copy doesn't have).
                    string folder = from[0];
                    foreach (var (rel, _) in files)
                    {
                        string path = Path.Combine(folder, rel);
                        if (!File.Exists(path))
                            throw new IOException($"The 0.695 folder is missing {rel}: it isn't a complete 0.695 install.");
                        loaders[rel] = () => File.ReadAllBytes(path);
                    }
                }
                else
                {
                    // Zips: every update file (the embedded list), from whichever zip has it.
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
                                    if (entry.Length > PatchFile.MaxEntrySize)
                                        throw new IOException($"{entry.FullName} in the zip is bigger than any game file.");
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
                    Description = steamFolder != null
                        ? "Update from the Steam build to 0.695: only the changed parts of each file."
                        : "Update from the Steam build to 0.695: the changed and new files, whole." };
                int edits = 0, patches = 0, whole = 0;
                // Which build this is: the one every file read so far matches (decided by the first file that differs).
                List<TargetBuild> candidates = Targets.ToList();
                long total = files.Sum(f => FinalList.TryGetValue(f.Path, out var o) ? o.Size : 0), done = 0;
                using (var pack = new PatchFile.Writer(partial))
                {
                    int n = 0;
                    foreach ((string rel, bool isNew) in files)
                    {
                        cancel.ThrowIfCancellationRequested();
                        n++;
                        progress?.Invoke("making", done, total, rel);
                        byte[] now = loaders[rel]();
                        string sha = Sha256Of(now), crc = $"{Delta.Crc(now):X8}";
                        // Only genuine files go in: the file must be one build's file by its SHA-256.
                        candidates = candidates.Where(t => t.Files.TryGetValue(rel, out BuildFile want) && want.Size == now.Length
                            && (Same(want.Sha256, sha) || WeakAllowed(t, rel, allowFebruary))
                            && want.Crc.Equals(crc, StringComparison.OrdinalIgnoreCase)).ToList();
                        if (candidates.Count == 0)
                            throw new InvalidDataException($"{rel} is not a genuine file of a known 0.695 build (its SHA-256 isn't in " +
                                "LaunchPad's list), so it can't go in the update.");
                        // (A file taken on size and CRC32 alone gets no SHA-256 in the list: none is known to compare with.)
                        var file = new PackFile { Path = rel, Size = now.Length, Crc32 = crc,
                            Sha256 = candidates.Any(t => WeakAllowed(t, rel, allowFebruary)) ? "" : sha };
                        string? oldPath = steamFolder != null ? Path.Combine(steamFolder, rel) : null;
                        byte[]? body = null;
                        if (!isNew && oldPath != null && File.Exists(oldPath))
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
                        done += now.Length;
                        log?.Invoke($"  [{n}/{files.Count}] {file.How,-5} {rel} ({body.Length:N0} of {now.Length:N0} bytes)");
                    }
                    manifest.Delete = SteamList.Keys.Where(k => !Mutable(k) && !Protected(k) && !Targets.Any(t => t.Files.ContainsKey(k)))
                        .OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
                    pack.Finish(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
                }
                File.Move(partial, output, true);
                return (edits, patches, whole, new FileInfo(output).Length);
            }
            catch
            {
                try { File.Delete(partial); } catch (IOException) { }
                throw;
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
        /// for deletion. Writes an .lmpatch. Returns the number of files in it. (A developer tool: players' LaunchPad only
        /// installs a pack that is the verified update, whatever this puts in.)
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
                string crc, sha;
                using (FileStream s = File.OpenRead(file))
                    (crc, _, sha) = AllHashes(s);
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
                manifest.Files.Add(new PackFile { Path = rel, Size = size, Crc32 = crc, Sha256 = sha });
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
