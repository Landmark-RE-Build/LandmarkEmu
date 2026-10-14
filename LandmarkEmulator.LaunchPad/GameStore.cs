using System.IO;
using System.IO.Compression;
using System.Text.Json;
using static LandmarkEmulator.LaunchPad.Patching;

namespace LandmarkEmulator.LaunchPad
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
    }

    /// <summary>
    /// What LaunchPad keeps in the game folder, in <c>LaunchPadEmu\</c>: the packs installed (installed.json), their
    /// compressed backups (backups\), their lists (packs\), downloaded server packs waiting to be installed or kept after
    /// an undo so they needn't be downloaded again (cache\), and the hash cache (hashes.json). It lives with the game so a
    /// game folder carries its own history, whichever copy of LaunchPad opens it.
    /// </summary>
    public sealed class GameStore
    {
        public const string FolderName = "LaunchPadEmu";

        public string Game { get; }
        public string Root => Path.Combine(Game, FolderName);
        public string BackupFolder => Path.Combine(Root, "backups");
        public string CacheFolder => Path.Combine(Root, "cache");
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
        }
        private Dictionary<string, HashEntry> hashes = new(StringComparer.OrdinalIgnoreCase);
        private bool hashesChanged;

        private static readonly JsonSerializerOptions json = new() { WriteIndented = true };

        public GameStore(string game)
        {
            Game = Path.GetFullPath(game);
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

        /// <summary>
        /// A game file's CRC32 and SHA-1, from the cache while its size and write time are unchanged (hashing all 20 GB
        /// takes minutes; afterwards only changed files are read). null = missing.
        /// </summary>
        private (long Size, string Crc, string Sha)? Hash(string rel, Action<long>? read)
        {
            var info = new FileInfo(Path.Combine(Game, rel));
            if (!info.Exists)
                return null;
            long ticks = info.LastWriteTimeUtc.Ticks;
            if (hashes.TryGetValue(rel, out HashEntry? e) && e.Size == info.Length && e.Ticks == ticks)
            {
                read?.Invoke(info.Length);
                return (e.Size, e.Crc, e.Sha);
            }
            (string crc, string sha) h;
            using (FileStream s = info.OpenRead())
                h = Hashes(s, read);
            hashes[rel] = new HashEntry { Size = info.Length, Ticks = ticks, Crc = h.crc, Sha = h.sha };
            hashesChanged = true;
            return (info.Length, h.crc, h.sha);
        }

        public Manifest? ManifestOf(InstalledPack p)
        {
            string file = Path.Combine(PacksFolder, p.Id + ".json");
            return File.Exists(file) ? JsonSerializer.Deserialize<Manifest>(File.ReadAllBytes(file))?.Checked() : null;
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
        /// The other half of the update check (<see cref="CheckUpdate"/> checks the pack itself): with the pack's files and
        /// deletions applied, would every file of Daybreak's final build be right? The files the pack doesn't carry must
        /// already match the final build here (the many files 0.695 shares with 0.589). Settings files and the launcher
        /// are left out, as in the Steam check. Returns the files that would still be missing or old.
        /// </summary>
        public List<Problem> CheckComplete(Manifest m, Progress progress, CancellationToken cancel)
        {
            var carried = new HashSet<string>(m.Files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
            CheckUpdate(m, out TargetBuild target);
            var check = target.Files.Where(f => !Mutable(f.Key) && !carried.Contains(f.Key)).ToList();
            long total = check.Sum(f => f.Value.Size), done = 0;
            var left = new List<Problem>();
            try
            {
                foreach (var (rel, expected) in check)
                {
                    cancel.ThrowIfCancellationRequested();
                    progress("verifying", done, total, rel);
                    var h = Hash(rel, n => { done += n; progress("verifying", done, total, rel); });
                    if (h == null)
                    {
                        left.Add(new Problem { Path = rel, Issue = "missing" });
                        done += expected.Size;
                    }
                    else if (h.Value.Size != expected.Size || !h.Value.Crc.Equals(expected.Crc, StringComparison.OrdinalIgnoreCase))
                        left.Add(new Problem { Path = rel, Issue = $"not the {target.Label} version" });
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

        // ---- Installing ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Installs a pack on top of the stack: (1) checks it (whole files against their size and CRC32, edits and patches
        /// against the exact Steam file they start from); nothing is touched if anything is wrong; (2) writes the backup:
        /// the original bytes each in-place edit overwrites, and every file it replaces or deletes, compressed
        /// (backups\*.zip, also the journal of an unfinished install); (3) edits files in place, rebuilds them from patches,
        /// or copies them in whole, checking each result's size and CRC32. A failure part-way puts the game back from the
        /// backup.
        /// </summary>
        public InstalledPack Install(Pack pack, string source, string sourceName, bool filesChecked, Progress progress, CancellationToken cancel)
        {
            if (Game_IsRunning())
                throw new InvalidOperationException("Close Landmark before patching.");
            Manifest m = pack.Manifest;
            if (Stack.Any(p => p.Id == pack.Id))
                throw new InvalidOperationException($"\"{m.Name}\" is already installed.");
            string current = LaunchPad.Game.Build(Game);
            // Only Daybreak's update to the final build, on the Steam build, leaving the whole final build behind.
            List<string> problems = CheckUpdate(m, out TargetBuild build);
            if (problems.Count > 0)
                throw new InvalidOperationException($"\"{m.Name}\" is not a verified update to {FinalBuild}: {string.Join("; ", problems)}. Nothing was changed.");
            if (current != SteamBuild)
                throw new InvalidOperationException($"The update goes on the Steam build {SteamBuild}; this game is {(current.Length > 0 ? current : "an unknown build")}.");
            RequireSteamCopy(progress, cancel);
            List<Problem> left = CheckComplete(m, progress, cancel);
            if (left.Count > 0)
                throw new InvalidOperationException($"\"{m.Name}\" is incomplete: after it, {left.Count} file(s) of the {build.Label} build would still be missing or old (first: {left[0].Path}). Nothing was changed.");

            // 1. The whole pack is checked before anything changes: whole files against their size and CRC32, and for an
            //    edit or patch, the game file it starts from must be the exact Steam file it was made from.
            long total = m.Files.Sum(f => f.Size), done = 0;
            var edits = new Dictionary<string, InPlaceEdit.Edit>(StringComparer.OrdinalIgnoreCase);
            foreach (PackFile f in m.Files)
            {
                cancel.ThrowIfCancellationRequested();
                if (!pack.Has(f.Entry))
                    throw new InvalidDataException($"The pack is missing {f.Entry}. Nothing was changed.");
                progress("checking", done, total, f.Path);
                if (f.How == "whole")
                {
                    if (!filesChecked)
                    {
                        byte[] data = pack.Read(f.Entry);
                        if (data.Length != f.Size || !$"{Delta.Crc(data):X8}".Equals(f.Crc32, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException($"{f.Path} in the pack is damaged or different from its list. Nothing was changed.");
                    }
                }
                else
                {
                    var h = Hash(f.Path, null);
                    if (h == null || h.Value.Size != f.FromSize || !h.Value.Crc.Equals(f.FromCrc32, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"{f.Path} isn't the Steam file this update was made from. Nothing was changed.");
                    if (f.How == "edit")
                    {
                        InPlaceEdit.Edit e = InPlaceEdit.Read(pack.Read(f.Entry));
                        if (e.SizeBefore != f.FromSize || e.SizeAfter != f.Size || !$"{e.CrcAfter:X8}".Equals(f.Crc32, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException($"The edit for {f.Path} doesn't match its list. Nothing was changed.");
                        edits[f.Path] = e;
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

            // 3. The changes: deletions, then each file edited in place, rebuilt from its patch, or copied in whole. Every
            //    result is checked against the size and CRC32 the build must have.
            done = 0;
            try
            {
                foreach (string rel in m.Delete)
                    if (File.Exists(Path.Combine(Game, rel)))
                        File.Delete(Path.Combine(Game, rel));
                foreach (PackFile f in m.Files)
                {
                    cancel.ThrowIfCancellationRequested();
                    progress("installing", done, total, f.Path);
                    string target = Path.Combine(Game, f.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    switch (f.How)
                    {
                        case "edit":
                            EditInPlace(target, edits[f.Path]);
                            break;
                        case "patch":
                        {
                            byte[] old = File.ReadAllBytes(target);
                            using (FileStream to = File.Create(target + ".lpnew"))
                                Delta.Apply(old, pack.Read(f.Entry), to);     // checks the result's size and CRC32
                            File.Move(target + ".lpnew", target, true);
                            break;
                        }
                        default:
                            File.WriteAllBytes(target + ".lpnew", pack.Read(f.Entry));
                            File.Move(target + ".lpnew", target, true);
                            break;
                    }
                    var after = Hash(f.Path, null);
                    if (after == null || after.Value.Size != f.Size || !after.Value.Crc.Equals(f.Crc32, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"{f.Path} came out wrong after patching.");
                    done += f.Size;
                }
                SaveHashes();
            }
            catch (Exception)
            {
                RestoreFrom(backup, null, null);
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
                Source = source, SourceName = sourceName, Build = build.Label
            };
            Stack.Add(record);
            SaveStack();
            File.Delete(MarkerFile);
            return record;
        }

        /// <summary>
        /// Edits a game file in place. A file with more than one name on disk (a hard link, shared with another install) is
        /// edited as a copy and swapped in instead, so the other install keeps its file.
        /// </summary>
        private static void EditInPlace(string target, InPlaceEdit.Edit edit)
        {
            using (var file = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                if (InPlaceEdit.Links(file) <= 1)
                {
                    InPlaceEdit.Apply(edit, file);
                    return;
                }
            }
            File.Copy(target, target + ".lpnew", true);
            using (var copy = new FileStream(target + ".lpnew", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                InPlaceEdit.Apply(edit, copy);
            File.Move(target + ".lpnew", target, true);
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
                return null;
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
                return "An update was interrupted before it changed anything.";
            }
            RestoreFrom(backup, null, null);
            File.Delete(backup);
            File.Delete(MarkerFile);
            return "An update was interrupted part-way, so the game was put back exactly as it was before it.";
        }

        private static bool Game_IsRunning() => LaunchPad.Game.IsRunning();

        // ---- Undoing ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Undoes the newest pack: its files come out (a server's pack is kept in the cache so playing there again needs
        /// no download), then the originals come back from its compressed backup.
        /// </summary>
        public InstalledPack UndoLast(Progress progress, CancellationToken cancel)
        {
            if (Game_IsRunning())
                throw new InvalidOperationException("Close Landmark first.");
            InstalledPack last = Stack.LastOrDefault() ?? throw new InvalidOperationException("No patch to undo.");
            string backup = Path.Combine(BackupFolder, last.Backup);
            if (!File.Exists(backup))
                throw new InvalidOperationException($"The backup of \"{last.Name}\" ({last.Backup}) is missing, so it can't be undone. " +
                    "Steam's \"Verify integrity of game files\" restores the original game.");
            Manifest? m = ManifestOf(last);
            // A server's pack stays downloaded in the cache (its .zip), so playing there again needs no download.
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
            if (zip.GetEntry("added.txt") is ZipArchiveEntry addedEntry)
            {
                using var r = new StreamReader(addedEntry.Open());
                foreach (string rel in r.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (SafeRelative(rel) is not string safe)
                        continue;
                    if (File.Exists(Path.Combine(Game, safe)))
                        File.Delete(Path.Combine(Game, safe));
                    // Folders the pack made, now empty, go too.
                    for (string? dir = Path.GetDirectoryName(Path.Combine(Game, safe));
                         dir != null && dir.Length > Game.Length && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any();
                         dir = Path.GetDirectoryName(dir))
                        Directory.Delete(dir);
                }
            }
            // In-place edits: write the original bytes back and set the old length (safe to repeat, and right whether the
            // edit was finished, half done or not started).
            foreach (ZipArchiveEntry e in zip.Entries.Where(e => e.FullName.StartsWith("undo/") && e.FullName.EndsWith(".lme")))
            {
                string? rel = SafeRelative(e.FullName["undo/".Length..^".lme".Length]);
                if (rel == null || !File.Exists(Path.Combine(Game, rel)))
                    continue;
                progress?.Invoke("restoring", 0, 1, rel);
                InPlaceEdit.Edit undo;
                using (Stream s = e.Open())
                using (var copy = new MemoryStream())
                {
                    s.CopyTo(copy);
                    undo = InPlaceEdit.Read(copy.ToArray());
                }
                using var file = new FileStream(Path.Combine(Game, rel), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                foreach (InPlaceEdit.Range r in undo.Ranges)
                {
                    file.Position = r.Offset;
                    file.Write(r.Bytes);
                }
                file.SetLength(undo.SizeAfter);
                file.Flush(true);
            }
            var files = zip.Entries.Where(e => e.FullName.StartsWith("files/") && !e.FullName.EndsWith("/")).ToList();
            long total = files.Sum(e => e.Length), done = 0;
            foreach (ZipArchiveEntry e in files)
            {
                string? rel = SafeRelative(e.FullName["files/".Length..]);
                if (rel == null)
                    continue;
                progress?.Invoke("restoring", done, total, rel);
                string target = Path.Combine(Game, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                e.ExtractToFile(target + ".lpnew", true);
                File.Move(target + ".lpnew", target, true);
                done += e.Length;
            }
        }

        // ---- Validating -------------------------------------------------------------------------------------------

        /// <summary>
        /// Checks every file is what it should be: Steam's file, or the newest installed pack's version of it (and files a
        /// pack deleted are gone). Like LaunchPad's "Validate Files", but it reports instead of downloading.
        /// </summary>
        public (List<Problem> Problems, int Checked) Validate(Progress progress, CancellationToken cancel)
        {
            // path -> (size, checksum, is SHA-1) ; null = must not exist
            var expected = new Dictionary<string, (long Size, string Sum, bool Sha)?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (rel, f) in SteamList)
                if (!Mutable(rel))
                    expected[rel] = (f.Size, f.Crc, true);
            foreach (InstalledPack p in Stack)
            {
                Manifest m = ManifestOf(p) ?? throw new InvalidOperationException($"The list of \"{p.Name}\" is missing from {FolderName}\\packs.");
                foreach (string rel in m.Delete)
                    expected[rel] = null;
                foreach (PackFile f in m.Files)
                    expected[f.Path] = (f.Size, f.Crc32, false);
            }
            var problems = new List<Problem>();
            long total = expected.Values.Sum(v => v?.Size ?? 0), done = 0;
            try
            {
                foreach (var (rel, want) in expected)
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
                    else if (!(want.Value.Sha ? h.Value.Sha : h.Value.Crc).Equals(want.Value.Sum, StringComparison.OrdinalIgnoreCase))
                        problems.Add(new Problem { Path = rel, Issue = "changed" });
                }
            }
            finally
            {
                SaveHashes();
            }
            return (problems, expected.Count);
        }

        /// <summary>Size of the downloaded-pack cache, and emptying it.</summary>
        public long CacheBytes()
            => Directory.Exists(CacheFolder) ? Directory.EnumerateFiles(CacheFolder, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) : 0;

        public void ClearCache()
        {
            if (Directory.Exists(CacheFolder))
                Directory.Delete(CacheFolder, true);
        }
    }
}
