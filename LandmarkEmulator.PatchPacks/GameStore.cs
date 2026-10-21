using System.IO.Compression;
using System.Text.Json;
using static LandmarkEmulator.PatchPacks.Patching;

namespace LandmarkEmulator.PatchPacks
{
    /// <summary>A pack in the game folder's stack (oldest first): what it was, where it came from, where its backup is.</summary>
    public sealed class InstalledPack
    {
        /// <summary>SHA-256 of its pack.json.</summary>
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public bool Official { get; set; }
        public DateTime InstalledAt { get; set; }
        public int Files { get; set; }
        public int Deletes { get; set; }
        /// <summary>The compressed backup of every file it replaced or deleted (in <see cref="GameStore.BackupFolder"/>).</summary>
        public string Backup { get; set; } = "";
        public long BackupBytes { get; set; }
        /// <summary>"" = a pack the player installed; otherwise the server it came from ("host:port").</summary>
        public string Source { get; set; } = "";
        public string SourceName { get; set; } = "";
        /// <summary>The build it installed (a Patching.Targets label).</summary>
        public string Build { get; set; } = "";
        /// <summary>The build's id (Patching.Targets: "final", "january-2017"); empty for packs installed before 2026-10-18.</summary>
        public string Target { get; set; } = "";
        /// <summary>
        /// Files it wrote that were only checked by size and CRC32: the final build's three files without a SHA-256, with the
        /// player's February opt-in (Patching.WeakAllowed). Empty for every fully verified pack.
        /// </summary>
        public List<string> WeakFiles { get; set; } = new();
    }

    /// <summary>
    /// What LaunchPad keeps in the game folder, in <c>LaunchPadEmu\</c>: the packs installed (installed.json), their
    /// compressed backups (backups\), their lists (packs\), packs downloaded from servers (downloads\: kept after an
    /// install or undo so they needn't be downloaded again), the files being built before they are swapped in (staging\),
    /// and the hash cache (hashes.json). It lives with the game so a game folder carries its own history, whichever copy
    /// of LaunchPad opens it.
    ///
    /// Every file this writes into the game is first held to the SHA-256 of the genuine file (Patching.Targets) or, for the
    /// originals it puts back, comes from the backup of the player's own verified Steam copy. Nothing is written through a
    /// junction or symbolic link inside the game folder (<see cref="GameFolder.RequirePlainPath"/>).
    /// </summary>
    public sealed class GameStore
    {
        public const string FolderName = "LaunchPadEmu";

        public string Game { get; }
        public string Root => Path.Combine(Game, FolderName);
        public string BackupFolder => Path.Combine(Root, "backups");
        /// <summary>Packs downloaded from servers (<see cref="ServerPatches"/>).</summary>
        public string DownloadFolder => Path.Combine(Root, "downloads");
        /// <summary>Where files are built and checked before they replace the game's (same drive, so the swap is a rename).</summary>
        public string StagingFolder => Path.Combine(Root, "staging");
        /// <summary>The download cache of LaunchPads before 2026-10-18 (cleared with the downloads).</summary>
        private string OldCacheFolder => Path.Combine(Root, "cache");
        private string PacksFolder => Path.Combine(Root, "packs");
        private string StackFile => Path.Combine(Root, "installed.json");
        private string HashFile => Path.Combine(Root, "hashes.json");

        public List<InstalledPack> Stack { get; private set; } = new();

        private sealed class HashEntry
        {
            public long Size { get; set; }
            public long Ticks { get; set; }
            public string Crc { get; set; } = "";
            public string Sha { get; set; } = "";
            /// <summary>SHA-256 (since 2026-10-18; older entries get it the first time it is needed).</summary>
            public string Sha256 { get; set; } = "";
        }
        private Dictionary<string, HashEntry> hashes = new(StringComparer.OrdinalIgnoreCase);
        private bool hashesChanged;

        private static readonly JsonSerializerOptions json = new() { WriteIndented = true };

        /// <summary>Is the game in this folder running? (Replaceable for tests.)</summary>
        public Func<bool> GameRunning { get; set; }

        public GameStore(string game)
        {
            Game = Path.GetFullPath(game);
            GameRunning = () => GameFolder.IsRunning(Game);
            try
            {
                if (File.Exists(StackFile))
                    Stack = JsonSerializer.Deserialize<List<InstalledPack>>(File.ReadAllText(StackFile)) ?? new();
                if (File.Exists(HashFile))
                    hashes = new(JsonSerializer.Deserialize<Dictionary<string, HashEntry>>(File.ReadAllText(HashFile)) ?? new(),
                        StringComparer.OrdinalIgnoreCase);
            }
            catch (JsonException)
            {
                // A damaged hash cache is only slower; a damaged stack is kept aside and reported by Validate.
                if (File.Exists(StackFile))
                    File.Copy(StackFile, StackFile + ".bad", true);
            }
        }

        private void SaveStack()
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(StackFile + ".tmp", JsonSerializer.Serialize(Stack, json));
            File.Move(StackFile + ".tmp", StackFile, true);
        }

        private void SaveHashes()
        {
            if (!hashesChanged)
                return;
            Directory.CreateDirectory(Root);
            File.WriteAllText(HashFile + ".tmp", JsonSerializer.Serialize(hashes));
            File.Move(HashFile + ".tmp", HashFile, true);
            hashesChanged = false;
        }

        /// <summary>A game file's size, CRC32, SHA-1 and SHA-256 ("" when not asked for and not cached).</summary>
        public readonly record struct FileHash(long Size, string Crc, string Sha, string Sha256);

        /// <summary>
        /// A game file's CRC32 and SHA-1 (and SHA-256 when <paramref name="sha256"/>), from the cache while its size and
        /// write time are unchanged (hashing all 20 GB takes minutes; afterwards only changed files are read). All three are
        /// worked out in the same read. null = missing.
        /// </summary>
        private FileHash? Hash(string rel, Action<long>? read, bool sha256 = false)
        {
            var info = new FileInfo(Path.Combine(Game, rel));
            if (!info.Exists)
                return null;
            long ticks = info.LastWriteTimeUtc.Ticks;
            if (hashes.TryGetValue(rel, out HashEntry? e) && e.Size == info.Length && e.Ticks == ticks && (!sha256 || e.Sha256.Length > 0))
            {
                read?.Invoke(info.Length);
                return new FileHash(e.Size, e.Crc, e.Sha, e.Sha256);
            }
            (string crc, string sha, string sha2) h;
            using (FileStream s = info.OpenRead())
                h = AllHashes(s, read);
            hashes[rel] = new HashEntry { Size = info.Length, Ticks = ticks, Crc = h.crc, Sha = h.sha, Sha256 = h.sha2 };
            hashesChanged = true;
            return new FileHash(info.Length, h.crc, h.sha, h.sha2);
        }

        /// <summary>
        /// Is the file on disk the genuine file a build wants? By its SHA-256 when the build has one; a file 0.695 kept from
        /// 0.589 may instead match by Steam's own SHA-1 (it is then Steam's file, with the build's size and CRC32).
        /// </summary>
        private static bool Genuine(string rel, BuildFile want, FileHash h, bool weak = false)
        {
            if (h.Size != want.Size || !h.Crc.Equals(want.Crc, StringComparison.OrdinalIgnoreCase))
                return false;
            if (weak && want.Sha256.Length == 0)
                return true;                       // the player's February opt-in: size and CRC32 only (Patching.WeakAllowed)
            if (want.Sha256.Length > 0)
                return Same(h.Sha256, want.Sha256);
            return SteamList.TryGetValue(rel, out var steam) && steam.Size == want.Size && Same(h.Sha, steam.Crc);
        }

        public Manifest? ManifestOf(InstalledPack p)
        {
            string file = Path.Combine(PacksFolder, p.Id + ".json");
            return File.Exists(file) ? JsonSerializer.Deserialize<Manifest>(File.ReadAllBytes(file))?.Checked() : null;
        }

        /// <summary>The build an installed pack led to.</summary>
        private TargetBuild BuildOf(InstalledPack p, Manifest m)
        {
            if (Target(p.Target) is TargetBuild known)
                return known;
            CheckUpdate(m, out TargetBuild target);
            return target;
        }

        /// <summary>Paths the installed packs replaced, added or deleted (their Steam originals are in the backups).</summary>
        private HashSet<string> Touched()
        {
            var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (InstalledPack p in Stack)
                if (ManifestOf(p) is Manifest m)
                {
                    touched.UnionWith(m.Files.Select(f => f.Path));
                    touched.UnionWith(m.Delete);
                }
            return touched;
        }

        // ---- Verifying the Steam copy -----------------------------------------------------------------------------

        /// <summary>
        /// Checks the folder is a complete, unchanged Steam install of Landmark 0.589 (Steam's own sizes and SHA-1s): every
        /// file but the settings files and the old launcher, and the files installed packs replaced (their originals were
        /// checked before, and are in the backups). Packs only go on a folder that passes, so LaunchPad never stands in for
        /// owning the game: it only patches.
        /// </summary>
        public List<Problem> VerifySteam(Progress progress, CancellationToken cancel)
        {
            var touched = Touched();
            var check = SteamList.Where(f => !Mutable(f.Key) && !touched.Contains(f.Key)).ToList();
            long total = check.Sum(f => f.Value.Size), done = 0;
            var problems = new List<Problem>();
            try
            {
                foreach (var (rel, expected) in check)
                {
                    cancel.ThrowIfCancellationRequested();
                    progress("verifying", done, total, rel);
                    var h = Hash(rel, n => { done += n; progress("verifying", done, total, rel); });
                    if (h == null)
                    {
                        problems.Add(new Problem { Path = rel, Issue = "missing" });
                        done += expected.Size;
                    }
                    else if (h.Value.Size != expected.Size)
                        problems.Add(new Problem { Path = rel, Issue = $"wrong size ({h.Value.Size:N0}, Steam's is {expected.Size:N0})" });
                    else if (!h.Value.Sha.Equals(expected.Crc, StringComparison.OrdinalIgnoreCase))
                        problems.Add(new Problem { Path = rel, Issue = "changed (SHA-1 differs from Steam's)" });
                }
            }
            finally
            {
                SaveHashes();
            }
            return problems;
        }

        /// <summary>
        /// The other half of the update check (<see cref="CheckUpdate(Manifest)"/> checks the pack itself): with the pack's
        /// files and deletions applied, would every file of the build be right? The files the pack doesn't carry must
        /// already be the build's genuine files here (the many files 0.695 shares with 0.589: by SHA-256, or by Steam's
        /// SHA-1). Settings files and the launcher are left out, as in the Steam check. Returns the files that would still
        /// be missing or old.
        /// </summary>
        public List<Problem> CheckComplete(Manifest m, Progress progress, CancellationToken cancel, bool allowFebruary = false)
        {
            var carried = new HashSet<string>(m.Files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
            CheckUpdate(m, out TargetBuild target, allowFebruary);
            var check = target.Files.Where(f => !Mutable(f.Key) && !carried.Contains(f.Key)).ToList();
            long total = check.Sum(f => f.Value.Size), done = 0;
            var left = new List<Problem>();
            try
            {
                foreach (var (rel, expected) in check)
                {
                    cancel.ThrowIfCancellationRequested();
                    progress("verifying", done, total, rel);
                    var h = Hash(rel, n => { done += n; progress("verifying", done, total, rel); }, sha256: expected.Sha256.Length > 0);
                    if (h == null)
                    {
                        left.Add(new Problem { Path = rel, Issue = "missing" });
                        done += expected.Size;
                    }
                    else if (!Genuine(rel, expected, h.Value))
                        left.Add(new Problem { Path = rel, Issue = $"not the {target.Label}'s version" });
                }
            }
            finally
            {
                SaveHashes();
            }
            return left;
        }

        /// <summary>Throws unless <see cref="VerifySteam"/> passes.</summary>
        public void RequireSteamCopy(Progress progress, CancellationToken cancel)
        {
            List<Problem> problems = VerifySteam(progress, cancel);
            if (problems.Count > 0)
                throw new InvalidOperationException(
                    $"Patches only go on a complete Steam copy of Landmark {SteamBuild}, and {problems.Count} file(s) here are missing or changed " +
                    $"(first: {problems[0].Path}, {problems[0].Issue}). In Steam: Landmark > Properties > Installed Files > " +
                    "Verify integrity of game files, then try again.");
        }

        /// <summary>
        /// Is the game's Landmark64.exe a genuine one: Steam's 0.589 exe, or the exe of a known 0.695 build? null = yes. With
        /// the player's February opt-in, the final build's exe an installed pack wrote on its size and CRC32 counts too.
        /// </summary>
        public string? CheckExe(bool allowFebruary = false)
        {
            var h = Hash("Landmark64.exe", null, sha256: true);
            SaveHashes();
            if (h == null)
                return "Landmark64.exe is missing.";
            if (SteamList.TryGetValue("Landmark64.exe", out var steam) && steam.Size == h.Value.Size && Same(h.Value.Sha, steam.Crc))
                return null;
            bool weak = allowFebruary && Stack.Any(p => p.WeakFiles.Contains("Landmark64.exe", StringComparer.OrdinalIgnoreCase));
            if (Targets.Any(t => t.Files.TryGetValue("Landmark64.exe", out BuildFile want)
                && Genuine("Landmark64.exe", want, h.Value, weak && WeakAllowed(t, "Landmark64.exe", true))))
                return null;
            // The exe matches a known build by size and CRC32 but this LaunchPad has no SHA-256 for it (Daybreak's final
            // February 2017 build, "0.699", until its fingerprints are added): the install is fine, LaunchPad is just
            // older than that build's fingerprints, so it must not tell the player to put the Steam game back.
            if (Targets.FirstOrDefault(t => t.Files.TryGetValue("Landmark64.exe", out BuildFile want) && want.Sha256.Length == 0
                    && want.Size == h.Value.Size && want.Crc.Equals(h.Value.Crc, StringComparison.OrdinalIgnoreCase)) is TargetBuild known)
                return $"Landmark64.exe looks like the exe of {known.Label} (it matches Daybreak's file list), but this LaunchPad " +
                    "can't verify that build yet, so it won't start it. Your game files are probably fine: a newer LaunchPad that " +
                    "knows this build's fingerprints will start it. Don't restore the Steam game for this.";
            return "Landmark64.exe is not a genuine Daybreak exe (neither Steam's 0.589 exe nor the exe of a known 0.695 build). " +
                "LaunchPad won't start it. Settings > Patch packs > Back to the Steam game, or Steam's \"Verify integrity of " +
                "game files\", puts the genuine one back.";
        }

        // ---- Installing ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Installs a pack on top of the stack: (1) checks it (the list against the strong list, every whole file's SHA-256,
        /// every edit's result SHA-256 worked out from the player's Steam file without writing, every patch's header);
        /// nothing is touched if anything is wrong; (2) writes the backup: the original bytes each in-place edit overwrites,
        /// and every file it replaces or deletes, compressed (backups\*.zip, also the journal of an unfinished install);
        /// (3) edits files in place, or builds them in staging\ (patches, whole files, edits of hard-linked files) and only
        /// swaps them in once their SHA-256 is the genuine file's; every result is checked again (size, CRC32, SHA-256).
        /// A failure part-way puts the game back from the backup.
        /// </summary>
        /// <param name="allowFebruary">
        /// The player's February opt-in (Patching.WeakAllowed): only for a pack the player chose themselves, never for a
        /// server's (ServerPatches never passes it).
        /// </param>
        public InstalledPack Install(Pack pack, string source, string sourceName, Progress progress, CancellationToken cancel,
            bool allowFebruary = false)
        {
            if (GameRunning())
                throw new InvalidOperationException("Close Landmark before patching.");
            Manifest m = pack.Manifest;
            if (Stack.Any(p => p.Id == pack.Id))
                throw new InvalidOperationException($"\"{m.Name}\" is already installed.");
            if (Stack.Count > 0)
                throw new InvalidOperationException("The update is already installed. Undo it first (Settings > Patch packs).");
            string current = GameFolder.Build(Game);
            // Only the update to a known 0.695 build, on the Steam build, leaving the whole build behind.
            List<string> problems = PackCheck.CheckIndex(pack, out TargetBuild build, allowFebruary);
            var weak = new HashSet<string>(WeakFiles(m, build, allowFebruary), StringComparer.OrdinalIgnoreCase);
            bool ShaOk(string path, BuildFile want, string sha) => weak.Contains(path) || Same(sha, want.Sha256);
            if (problems.Count > 0)
                throw new InvalidOperationException($"\"{m.Name}\" is not a verified update to {FinalBuild}: {string.Join("; ", problems)}. Nothing was changed.");
            if (current != SteamBuild)
                throw new InvalidOperationException($"The update goes on the Steam build {SteamBuild}; this game is {(current.Length > 0 ? current : "an unknown build")}.");
            foreach (string rel in m.Files.Select(f => f.Path).Concat(m.Delete))
                GameFolder.RequirePlainPath(Game, rel);
            GameFolder.RequirePlainPath(Game, FolderName + "\\staging\\x");
            RequireSteamCopy(progress, cancel);
            List<Problem> left = CheckComplete(m, progress, cancel, allowFebruary);
            if (left.Count > 0)
                throw new InvalidOperationException($"\"{m.Name}\" is incomplete: after it, {left.Count} file(s) of the {build.Label} would still be missing or old (first: {left[0].Path}). Nothing was changed.");

            // 1. The whole pack is checked before anything changes: every whole file against the genuine file's SHA-256;
            //    for an edit or patch, the game file it starts from must be the exact Steam file it was made from, and an
            //    edit's result SHA-256 is worked out from that file with the edit laid over it (nothing written).
            long total = m.Files.Sum(f => f.Size), done = 0;
            var edits = new Dictionary<string, InPlaceEdit.Edit>(StringComparer.OrdinalIgnoreCase);
            foreach (PackFile f in m.Files)
            {
                cancel.ThrowIfCancellationRequested();
                progress("checking", done, total, f.Path);
                BuildFile want = build.Files[f.Path];
                if (f.How == "whole")
                {
                    byte[] data = pack.Read(f.Entry);
                    if (data.Length != want.Size || !$"{Delta.Crc(data):X8}".Equals(want.Crc, StringComparison.OrdinalIgnoreCase)
                        || !ShaOk(f.Path, want, Sha256Of(data)))
                        throw new InvalidDataException($"{f.Path} in the pack is not the genuine file (its SHA-256 differs). Nothing was changed.");
                }
                else
                {
                    var h = Hash(f.Path, null);
                    if (h == null || h.Value.Size != f.FromSize || !h.Value.Crc.Equals(f.FromCrc32, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"{f.Path} isn't the Steam file this update was made from. Nothing was changed.");
                    if (f.How == "edit")
                    {
                        InPlaceEdit.Edit e = InPlaceEdit.Read(pack.Read(f.Entry));
                        if (e.SizeBefore != f.FromSize || e.SizeAfter != want.Size || !$"{e.CrcAfter:X8}".Equals(want.Crc, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException($"The edit for {f.Path} doesn't match its list. Nothing was changed.");
                        using (FileStream original = File.OpenRead(Path.Combine(Game, f.Path)))
                            if (!ShaOk(f.Path, want, InPlaceEdit.ResultSha256(e, original)))
                                throw new InvalidDataException($"The edit for {f.Path} doesn't make the genuine file (its result's SHA-256 " +
                                    "differs). Nothing was changed.");
                        edits[f.Path] = e;
                    }
                    else
                    {
                        var (oldSize, _, newSize, newCrc) = Delta.ReadHeader(pack.Read(f.Entry));
                        if (oldSize != f.FromSize || newSize != want.Size || !$"{newCrc:X8}".Equals(want.Crc, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException($"The patch for {f.Path} doesn't match its list. Nothing was changed.");
                    }
                }
                done += f.Size;
            }
            SaveHashes();

            // 2. The backup, written before anything changes: for an in-place edit just the original bytes of the ranges it
            //    overwrites and the old length (undo/), for a replaced or deleted file the whole original, compressed
            //    (files/), and the files the pack adds (added.txt). It doubles as the journal: while the marker
            //    (installing.json) exists, an interrupted install is undone from it the next time LaunchPad opens.
            Directory.CreateDirectory(BackupFolder);
            string backupName = $"{DateTime.Now:yyyyMMdd-HHmmss}-{pack.Id[..8]}.zip";
            string backup = Path.Combine(BackupFolder, backupName);
            var added = new List<string>();
            done = 0;
            using (ZipArchive zip = ZipFile.Open(backup + ".tmp", ZipArchiveMode.Create))
            {
                foreach (PackFile f in m.Files)
                {
                    cancel.ThrowIfCancellationRequested();
                    string target = Path.Combine(Game, f.Path);
                    if (!File.Exists(target))
                    {
                        added.Add(f.Path);
                        continue;
                    }
                    progress("backing up", done, total, f.Path);
                    if (edits.TryGetValue(f.Path, out InPlaceEdit.Edit? e))
                    {
                        using FileStream original = File.OpenRead(target);
                        byte[] undo = InPlaceEdit.Write(InPlaceEdit.Undo(e, original));
                        using Stream to = zip.CreateEntry("undo/" + f.Path.Replace('\\', '/') + ".lme", CompressionLevel.Optimal).Open();
                        to.Write(undo);
                    }
                    else
                    {
                        using Stream to = zip.CreateEntry("files/" + f.Path.Replace('\\', '/'), CompressionLevel.Optimal).Open();
                        using FileStream from = File.OpenRead(target);
                        from.CopyTo(to, 1 << 20);
                    }
                    done += f.Size;
                }
                foreach (string rel in m.Delete)
                {
                    string target = Path.Combine(Game, rel);
                    if (!File.Exists(target))
                        continue;
                    using Stream to = zip.CreateEntry("files/" + rel.Replace('\\', '/'), CompressionLevel.Optimal).Open();
                    using FileStream from = File.OpenRead(target);
                    from.CopyTo(to, 1 << 20);
                }
                using (var w = new StreamWriter(zip.CreateEntry("added.txt").Open()))
                    w.Write(string.Join("\n", added));
                using (var w = new StreamWriter(zip.CreateEntry("pack.json").Open()))
                    w.Write(JsonSerializer.Serialize(m, json));
            }
            File.Move(backup + ".tmp", backup, true);
            File.WriteAllText(MarkerFile, JsonSerializer.Serialize(new { backup = backupName, pack = m.Name }));

            // 3. The changes: deletions, then each file edited in place, or built in staging\ and swapped in once its SHA-256
            //    is the genuine file's. Every result is checked again against the size, CRC32 and SHA-256 the build must have.
            done = 0;
            try
            {
                Directory.CreateDirectory(StagingFolder);
                foreach (string rel in m.Delete)
                    if (File.Exists(Path.Combine(Game, rel)))
                        File.Delete(Path.Combine(Game, rel));
                int n = 0;
                foreach (PackFile f in m.Files)
                {
                    cancel.ThrowIfCancellationRequested();
                    progress("installing", done, total, f.Path);
                    BuildFile want = build.Files[f.Path];
                    string target = Path.Combine(Game, f.Path);
                    string staged = Path.Combine(StagingFolder, $"{++n}.tmp");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    switch (f.How)
                    {
                        case "edit":
                            EditInPlace(target, staged, edits[f.Path], weak.Contains(f.Path) ? null : want.Sha256);
                            break;
                        case "patch":
                        {
                            byte[] old = File.ReadAllBytes(target);
                            string sha;
                            using (FileStream to = File.Create(staged))
                            using (var hashed = new HashingStream(to))
                            {
                                Delta.Apply(old, pack.Read(f.Entry), hashed);     // checks the result's size and CRC32
                                sha = hashed.Sha256();
                                to.Flush(true);
                            }
                            if (!ShaOk(f.Path, want, sha))
                                throw new InvalidDataException($"{f.Path} came out wrong after patching (SHA-256 differs from the genuine file).");
                            File.Move(staged, target, true);
                            break;
                        }
                        default:
                        {
                            byte[] data = pack.Read(f.Entry);
                            if (!ShaOk(f.Path, want, Sha256Of(data)) || !$"{Delta.Crc(data):X8}".Equals(want.Crc, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidDataException($"{f.Path} in the pack is not the genuine file (its SHA-256 differs).");
                            using (FileStream to = File.Create(staged))
                            {
                                to.Write(data);
                                to.Flush(true);
                            }
                            File.Move(staged, target, true);
                            break;
                        }
                    }
                    var after = Hash(f.Path, null, sha256: true);
                    if (after == null || !Genuine(f.Path, want, after.Value, weak.Contains(f.Path)))
                        throw new InvalidDataException($"{f.Path} came out wrong after patching.");
                    done += f.Size;
                }
                SaveHashes();
                ClearStaging();
            }
            catch (Exception failure)
            {
                try
                {
                    RestoreFrom(backup, null, null);
                }
                catch (Exception restore)
                {
                    // Keep the backup and the marker: opening LaunchPad tries the recovery again.
                    throw new InvalidOperationException($"{failure.Message} Putting the game back also failed: {restore.Message}", failure);
                }
                File.Delete(backup);
                File.Delete(MarkerFile);
                throw;
            }

            Directory.CreateDirectory(PacksFolder);
            File.WriteAllText(Path.Combine(PacksFolder, pack.Id + ".json"), JsonSerializer.Serialize(m, json));
            var record = new InstalledPack
            {
                Id = pack.Id, Name = m.Name, Version = m.Version, Official = true, InstalledAt = DateTime.Now,
                Files = m.Files.Count, Deletes = m.Delete.Count, Backup = backupName, BackupBytes = new FileInfo(backup).Length,
                Source = source, SourceName = sourceName, Build = build.Label, Target = build.Id, WeakFiles = weak.ToList()
            };
            Stack.Add(record);
            SaveStack();
            File.Delete(MarkerFile);
            return record;
        }

        /// <summary>
        /// Edits a game file in place, then checks its SHA-256 (already worked out before anything was written; see
        /// Install step 1). A file with more than one name on disk (a hard link, shared with another install) is edited as
        /// a copy in staging\ and swapped in instead, so the other install keeps its file.
        /// </summary>
        private static void EditInPlace(string target, string staged, InPlaceEdit.Edit edit, string? wantSha256)
        {
            using (var file = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                if (InPlaceEdit.Links(file) <= 1)
                {
                    InPlaceEdit.Apply(edit, file);
                    file.Position = 0;
                    if (wantSha256 != null && !Same(Sha256Of(file), wantSha256))
                        throw new InvalidDataException($"{Path.GetFileName(target)} came out wrong after patching (SHA-256 differs from the genuine file).");
                    return;
                }
            }
            File.Copy(target, staged, true);
            using (var copy = new FileStream(staged, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                InPlaceEdit.Apply(edit, copy);
                copy.Position = 0;
                if (wantSha256 != null && !Same(Sha256Of(copy), wantSha256))
                    throw new InvalidDataException($"{Path.GetFileName(target)} came out wrong after patching (SHA-256 differs from the genuine file).");
            }
            File.Move(staged, target, true);
        }

        private void ClearStaging()
        {
            if (Directory.Exists(StagingFolder))
                Directory.Delete(StagingFolder, true);
        }

        /// <summary>Set while an install is changing files (its backup is the journal): see <see cref="RecoverInterrupted"/>.</summary>
        private string MarkerFile => Path.Combine(Root, "installing.json");

        /// <summary>
        /// An install that was interrupted (LaunchPad or the PC stopped part-way) is undone from its backup, so the game is
        /// exactly as before it. Returns a note for the player, or null when there was nothing to do.
        /// </summary>
        public string? RecoverInterrupted()
        {
            if (!File.Exists(MarkerFile))
            {
                ClearStaging();
                return null;
            }
            string? name = null;
            try
            {
                name = JsonDocument.Parse(File.ReadAllText(MarkerFile)).RootElement.GetProperty("backup").GetString();
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or IOException)
            {
            }
            string? backup = name != null ? Path.Combine(BackupFolder, Path.GetFileName(name)) : null;
            if (backup == null || !File.Exists(backup))
            {
                File.Delete(MarkerFile);
                ClearStaging();
                return "An update was interrupted before it changed anything.";
            }
            try
            {
                RestoreFrom(backup, null, null);
            }
            catch (Exception e)
            {
                // The backup and the marker stay, so nothing is lost; the player is told what to do.
                return $"An update was interrupted part-way, and putting the game back failed: {e.Message}";
            }
            File.Delete(backup);
            File.Delete(MarkerFile);
            return "An update was interrupted part-way, so the game was put back exactly as it was before it.";
        }

        // ---- Undoing ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Undoes the newest pack: its files come out, then the originals come back from its compressed backup. (A server's
        /// pack stays in downloads\, so installing it again needs no download.)
        /// </summary>
        public InstalledPack UndoLast(Progress progress, CancellationToken cancel)
        {
            if (GameRunning())
                throw new InvalidOperationException("Close Landmark first.");
            InstalledPack last = Stack.LastOrDefault() ?? throw new InvalidOperationException("No patch to undo.");
            string backup = Path.Combine(BackupFolder, last.Backup);
            if (!File.Exists(backup))
                throw new InvalidOperationException($"The backup of \"{last.Name}\" ({last.Backup}) is missing, so it can't be undone. " +
                    "Steam's \"Verify integrity of game files\" restores the original game.");
            Manifest? m = ManifestOf(last);
            RestoreFrom(backup, m, null, progress);
            File.Delete(backup);
            File.Delete(Path.Combine(PacksFolder, last.Id + ".json"));
            Stack.RemoveAt(Stack.Count - 1);
            SaveStack();
            return last;
        }

        /// <summary>
        /// Puts the game back from a backup: removes the files the pack added, writes the original bytes of in-place edits
        /// back, and restores replaced or deleted files. First moves the pack's own files to <paramref name="keepIn"/> if given.
        /// </summary>
        private void RestoreFrom(string backup, Manifest? m, string? keepIn, Progress? progress = null)
        {
            if (keepIn != null && m != null)
            {
                foreach (PackFile f in m.Files)
                {
                    string target = Path.Combine(Game, f.Path);
                    if (File.Exists(target) && new FileInfo(target).Length == f.Size)
                    {
                        string to = Path.Combine(keepIn, f.Path);
                        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                        File.Move(target, to, true);
                    }
                }
            }
            using ZipArchive zip = ZipFile.OpenRead(backup);
            string[] added = [];
            if (zip.GetEntry("added.txt") is ZipArchiveEntry addedEntry)
            {
                using var r = new StreamReader(addedEntry.Open());
                added = r.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            }
            // The files being built (staging\) are never part of the game. An install by a LaunchPad before 2026-10-18
            // built them next to their files (<file>.lpnew, possibly a whole asset pack); the backup lists every one of
            // those, so remove them all (found by re/test/test copies.sh test 2: Assets_027.pack.lpnew was left behind).
            ClearStaging();
            foreach (string listed in added.Concat(zip.Entries.Select(e => e.FullName switch
                     {
                         var n when n.StartsWith("undo/") && n.EndsWith(".lme") => n["undo/".Length..^".lme".Length],
                         var n when n.StartsWith("files/") && !n.EndsWith("/") => n["files/".Length..],
                         _ => ""
                     })))
                if (SafeRelative(listed) is string safe && File.Exists(Path.Combine(Game, safe) + ".lpnew"))
                    File.Delete(Path.Combine(Game, safe) + ".lpnew");
            foreach (string rel in added)
            {
                if (SafeRelative(rel) is not string safe || Protected(safe))
                    continue;
                GameFolder.RequirePlainPath(Game, safe);
                if (File.Exists(Path.Combine(Game, safe)))
                    File.Delete(Path.Combine(Game, safe));
                // Folders the pack made, now empty, go too.
                for (string? dir = Path.GetDirectoryName(Path.Combine(Game, safe));
                     dir != null && dir.Length > Game.Length && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any();
                     dir = Path.GetDirectoryName(dir))
                    Directory.Delete(dir);
            }
            // In-place edits: write the original bytes back and set the old length (safe to repeat, and right whether the
            // edit was finished, half done or not started). Each restored file must then have its original CRC32; any that
            // doesn't is reported at the end instead of the undo claiming success.
            var notRestored = new List<string>();
            foreach (ZipArchiveEntry e in zip.Entries.Where(e => e.FullName.StartsWith("undo/") && e.FullName.EndsWith(".lme")))
            {
                string? rel = SafeRelative(e.FullName["undo/".Length..^".lme".Length]);
                if (rel == null || Protected(rel) || !File.Exists(Path.Combine(Game, rel)))
                    continue;
                GameFolder.RequirePlainPath(Game, rel);
                progress?.Invoke("restoring", 0, 1, rel);
                InPlaceEdit.Edit undo;
                using (Stream s = e.Open())
                using (var copy = new MemoryStream())
                {
                    s.CopyTo(copy);
                    undo = InPlaceEdit.Read(copy.ToArray());
                }
                using (var file = new FileStream(Path.Combine(Game, rel), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    foreach (InPlaceEdit.Range r in undo.Ranges)
                    {
                        file.Position = r.Offset;
                        file.Write(r.Bytes);
                    }
                    file.SetLength(undo.SizeAfter);
                    file.Flush(true);
                    file.Position = 0;
                    if (!Patching.Crc32Of(file).Equals($"{undo.CrcAfter:X8}", StringComparison.OrdinalIgnoreCase))
                        notRestored.Add(rel);
                }
            }
            if (notRestored.Count > 0)
                throw new InvalidDataException($"{notRestored.Count} file(s) could not be put back exactly from the backup (first: " +
                    $"{notRestored[0]}). Steam's \"Verify integrity of game files\" restores the original game files.");
            var files = zip.Entries.Where(e => e.FullName.StartsWith("files/") && !e.FullName.EndsWith("/")).ToList();
            long total = files.Sum(e => e.Length), done = 0;
            Directory.CreateDirectory(StagingFolder);
            foreach (ZipArchiveEntry e in files)
            {
                string? rel = SafeRelative(e.FullName["files/".Length..]);
                if (rel == null || Protected(rel))
                    continue;
                GameFolder.RequirePlainPath(Game, rel);
                progress?.Invoke("restoring", done, total, rel);
                string target = Path.Combine(Game, rel);
                string staged = Path.Combine(StagingFolder, "restore.tmp");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                e.ExtractToFile(staged, true);
                File.Move(staged, target, true);
                done += e.Length;
            }
            ClearStaging();
        }

        // ---- Validating -------------------------------------------------------------------------------------------

        /// <summary>
        /// Checks every file is what it should be: Steam's file (SHA-1), or the installed update's genuine version of it
        /// (SHA-256), and files the update deleted are gone. Like LaunchPad's "Validate Files", but it reports instead of
        /// downloading.
        /// </summary>
        public (List<Problem> Problems, int Checked) Validate(Progress progress, CancellationToken cancel)
        {
            // path -> what it must be; null = must not exist
            var steam = new Dictionary<string, (long Size, string Sha1)?>(StringComparer.OrdinalIgnoreCase);
            var packed = new Dictionary<string, BuildFile>(StringComparer.OrdinalIgnoreCase);
            var weak = new HashSet<string>(Stack.SelectMany(p => p.WeakFiles), StringComparer.OrdinalIgnoreCase);
            foreach (var (rel, f) in SteamList)
                if (!Mutable(rel))
                    steam[rel] = (f.Size, f.Crc);
            foreach (InstalledPack p in Stack)
            {
                Manifest m = ManifestOf(p) ?? throw new InvalidOperationException($"The list of \"{p.Name}\" is missing from {FolderName}\\packs.");
                TargetBuild build = BuildOf(p, m);
                foreach (string rel in m.Delete)
                {
                    steam[rel] = null;
                    packed.Remove(rel);
                }
                foreach (PackFile f in m.Files)
                {
                    steam.Remove(f.Path);
                    packed[f.Path] = build.Files.TryGetValue(f.Path, out BuildFile b) ? b : new BuildFile(f.Size, f.Crc32, "");
                }
            }
            var problems = new List<Problem>();
            long total = steam.Values.Sum(v => v?.Size ?? 0) + packed.Values.Sum(v => v.Size), done = 0;
            try
            {
                foreach (var (rel, want) in steam)
                {
                    cancel.ThrowIfCancellationRequested();
                    progress("validating", done, total, rel);
                    var h = Hash(rel, n => { done += n; progress("validating", done, total, rel); });
                    if (want == null)
                    {
                        if (h != null)
                            problems.Add(new Problem { Path = rel, Issue = "should have been removed by a patch" });
                        continue;
                    }
                    if (h == null)
                    {
                        problems.Add(new Problem { Path = rel, Issue = "missing" });
                        done += want.Value.Size;
                    }
                    else if (h.Value.Size != want.Value.Size)
                        problems.Add(new Problem { Path = rel, Issue = $"wrong size ({h.Value.Size:N0}, expected {want.Value.Size:N0})" });
                    else if (!h.Value.Sha.Equals(want.Value.Sha1, StringComparison.OrdinalIgnoreCase))
                        problems.Add(new Problem { Path = rel, Issue = "changed" });
                }
                foreach (var (rel, want) in packed)
                {
                    cancel.ThrowIfCancellationRequested();
                    progress("validating", done, total, rel);
                    var h = Hash(rel, n => { done += n; progress("validating", done, total, rel); }, sha256: want.Sha256.Length > 0);
                    if (h == null)
                    {
                        problems.Add(new Problem { Path = rel, Issue = "missing" });
                        done += want.Size;
                    }
                    else if (h.Value.Size != want.Size)
                        problems.Add(new Problem { Path = rel, Issue = $"wrong size ({h.Value.Size:N0}, expected {want.Size:N0})" });
                    else if (!Genuine(rel, want, h.Value, weak.Contains(rel)))
                        problems.Add(new Problem { Path = rel, Issue = want.Sha256.Length > 0 || weak.Contains(rel) ? "changed" : "can't be verified (no SHA-256 of the genuine file)" });
                }
            }
            finally
            {
                SaveHashes();
            }
            return (problems, steam.Count + packed.Count);
        }

        /// <summary>Size of the downloaded packs, and emptying them.</summary>
        public long CacheBytes()
            => new[] { DownloadFolder, OldCacheFolder }.Where(Directory.Exists)
                .Sum(d => Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length));

        public void ClearCache()
        {
            foreach (string folder in new[] { DownloadFolder, OldCacheFolder })
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
        }
    }
}
