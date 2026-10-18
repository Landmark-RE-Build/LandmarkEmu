using LandmarkEmulator.PatchPacks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace LandmarkEmulator.WebAPI
{
    /// <summary>
    /// The server's patch folder (<see cref="Configuration.WebApiConfig.PatchFolder"/>): the server owner drops (uploads)
    /// the update pack there, as LaunchPad makes it (Settings > Patch packs > Create update pack, or LaunchPad.exe
    /// --make-update), and players' LaunchPad offers it to them (LandmarkEmulator.PatchPacks/ServerPatches.cs).
    ///
    /// Nothing in the folder is served until it has passed the same check every player's LaunchPad runs
    /// (LandmarkEmulator.PatchPacks: Patching.CheckUpdate and PackCheck.Verify): it must be the verified update from the
    /// Steam build to a known 0.695 build; every entry is unpacked (never past its declared size) and checked; every whole
    /// file's SHA-256 must be the genuine file's (the embedded strong list); every edit and patch must lead to a genuine
    /// file's size and CRC32, whose SHA-256 is in the list. The server has no 0.589 game files, so it can't replay edits
    /// and patches on them; each player's LaunchPad does that on their own Steam copy before anything changes. With
    /// <see cref="Configuration.WebApiConfig.PatchVerifySteamDir"/> (a Steam 0.589 copy, read only) the server replays them
    /// too and so checks every result's SHA-256 itself.
    ///
    /// The check runs on start-up and whenever the folder changes (a file watcher, and a size / write-time check every
    /// minute). For each pack passed it keeps the file's size, write time and SHA-256: /info advertises that SHA-256, and
    /// a pack whose size or write time changed afterwards is not served until it has passed again. A pack that fails is
    /// logged with the reason and moved to <c>rejected\</c> (with a .reason.txt), never served.
    ///
    ///   GET /patches                          the verified packs in file-name order (as in /info "patches")
    ///   GET /patches/{id}/pack.json           a pack's list (LaunchPad checks it against the sha256 above, then against its
    ///                                         own lists, before it downloads the pack)
    ///   GET /patches/{id}/pack.lmpatch        the pack; ranges supported (a broken-off download continues)
    /// {id} is the file name without .lmpatch. HTTP is fine for all of it: LaunchPad trusts the embedded lists, not the
    /// server or the connection.
    /// </summary>
    public sealed class PatchLibrary : IDisposable
    {
        /// <summary>A pack that passed the check, as it was when it passed.</summary>
        public sealed class Pack
        {
            public string Id { get; init; } = "";
            public string FilePath { get; init; } = "";
            public long FileBytes { get; init; }
            public DateTime Written { get; init; }
            /// <summary>SHA-256 of the whole .lmpatch file as checked (advertised in /info: packSha256).</summary>
            public string FileSha256 { get; init; } = "";
            public Patching.Manifest Manifest { get; init; } = new();
            /// <summary>Its pack.json text as stored in the .lmpatch (served as is; LaunchPad checks its SHA-256).</summary>
            public byte[] ManifestBytes { get; init; } = Array.Empty<byte>();
            /// <summary>SHA-256 of the pack.json text: the pack's identity (sha256 in /info).</summary>
            public string Sha256 { get; init; } = "";
            public string Build { get; init; } = "";
            public int Replayed { get; init; }
            public int NotReplayed { get; init; }
        }

        private static readonly Regex packId = new("^[A-Za-z0-9._-]{1,64}$");

        public string Folder { get; }
        private readonly string? steamDir;
        private readonly Action<string> log;
        private readonly object gate = new();
        private readonly Dictionary<string, Pack> verified = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Files that couldn't be read yet (still being copied in): retried on the next scan.</summary>
        private readonly HashSet<string> waiting = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(string File, string Reason, DateTime When)> refused = new();
        private FileSystemWatcher? watcher;
        private Timer? timer;
        private int scanning;
        private bool disposed;

        /// <param name="folder">The patch folder; empty = no patches.</param>
        /// <param name="steamDir">A Steam 0.589 copy to replay edits and patches on (read only); empty or not a game folder = none.</param>
        public PatchLibrary(string folder, string? steamDir, Action<string> log)
        {
            Folder = string.IsNullOrWhiteSpace(folder) ? "" : Path.GetFullPath(folder);
            this.log = log;
            if (!string.IsNullOrWhiteSpace(steamDir))
            {
                if (GameFolder.IsGameFolder(steamDir) && GameFolder.Build(steamDir) == Patching.SteamBuild)
                    this.steamDir = Path.GetFullPath(steamDir);
                else if (File.Exists(Path.Combine(steamDir, "Landmark64.exe")))
                    log($"Patch packs: {steamDir} is not a Steam {Patching.SteamBuild} copy of Landmark, so edits and patches are checked by their headers only.");
            }
        }

        /// <summary>The first check (in the background) and the watcher. Nothing is served before a pack has passed.</summary>
        public void Start()
        {
            if (Folder.Length == 0)
                return;
            Directory.CreateDirectory(Folder);
            log($"Patch packs: checking {Folder}{(steamDir != null ? $" (replaying edits and patches on {steamDir})" : "")}.");
            try
            {
                watcher = new FileSystemWatcher(Folder, "*" + PatchFile.Extension)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                    IncludeSubdirectories = false
                };
                FileSystemEventHandler changed = (_, _) => ScanSoon();
                watcher.Created += changed;
                watcher.Changed += changed;
                watcher.Deleted += changed;
                watcher.Renamed += (_, _) => ScanSoon();
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception e) when (e is IOException or ArgumentException or PlatformNotSupportedException)
            {
                // Some mounted folders can't be watched: the timer below still finds every change within a minute.
                log($"Patch packs: can't watch {Folder} ({e.Message}); checking it every minute instead.");
            }
            // The first scan now, then one a minute (size / write-time check; a changed pack is checked again).
            timer = new Timer(_ => Scan(), null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        }

        /// <summary>A change was seen: scan a few seconds later (a file being copied in raises many events).</summary>
        private void ScanSoon() => timer?.Change(TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1));

        /// <summary>Checks every pack in the folder that is new or changed since it was checked. One scan at a time.</summary>
        public void Scan()
        {
            if (Folder.Length == 0 || disposed || Interlocked.Exchange(ref scanning, 1) == 1)
                return;
            try
            {
                if (!Directory.Exists(Folder))
                    return;
                var files = Directory.EnumerateFiles(Folder, "*" + PatchFile.Extension).Select(Path.GetFullPath).ToList();
                lock (gate)
                    foreach (string gone in verified.Keys.Where(k => !files.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
                    {
                        verified.Remove(gone);
                        log($"Patch packs: {Path.GetFileName(gone)} was removed; no longer offered.");
                    }
                foreach (string file in files.OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                {
                    if (disposed)
                        return;
                    var info = new FileInfo(file);
                    lock (gate)
                        if (verified.TryGetValue(file, out Pack? known) && known.FileBytes == info.Length && known.Written == info.LastWriteTimeUtc)
                            continue;
                    lock (gate)
                        verified.Remove(file);
                    Check(file);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log($"Patch packs: can't read {Folder}: {e.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref scanning, 0);
            }
        }

        /// <summary>Checks one pack (see the class) and keeps it, or refuses it (logged, moved to rejected\).</summary>
        private void Check(string file)
        {
            string name = Path.GetFileName(file);
            string id = Path.GetFileNameWithoutExtension(file);
            if (!packId.IsMatch(id))
            {
                Refuse(file, "its file name may only use letters, digits, '.', '_' and '-' (at most 64)");
                return;
            }
            try
            {
                var info = new FileInfo(file);
                // Open it shared for reading only: while it is open nobody can write to it, so what is checked is what
                // gets recorded. A file still being copied in can't be opened that way yet: it is checked on a later scan.
                using var pack = new Patching.Pack(file);
                info.Refresh();
                string fileSha;
                using (var s = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                    fileSha = Convert.ToHexString(SHA256.HashData(s));
                bool wasWaiting;
                lock (gate)
                    wasWaiting = waiting.Remove(file);
                log($"Patch packs: checking {name} ({info.Length:N0} bytes){(wasWaiting ? " now that it is complete" : "")}...");
                PackCheck.Result result = PackCheck.Verify(pack, steamDir);
                byte[] manifestBytes = System.Text.Encoding.UTF8.GetBytes(PackManifestText(pack));
                var passed = new Pack
                {
                    Id = id, FilePath = file, FileBytes = info.Length, Written = info.LastWriteTimeUtc, FileSha256 = fileSha,
                    Manifest = pack.Manifest, ManifestBytes = manifestBytes, Sha256 = pack.Id, Build = result.Build.Label,
                    Replayed = result.Replayed, NotReplayed = result.NotReplayed
                };
                lock (gate)
                {
                    verified[file] = passed;
                    refused.RemoveAll(r => r.File.Equals(name, StringComparison.OrdinalIgnoreCase));
                }
                log($"Patch packs: {name} PASSED: the verified update to {result.Build.Label}, {result.Entries} files, " +
                    $"pack SHA-256 {fileSha}. " + (result.NotReplayed > 0
                        ? $"{result.NotReplayed} edits/patches checked by their headers (no Steam copy here to replay them on; each player's LaunchPad replays them before installing)."
                        : $"{result.Replayed} edits/patches replayed and matched."));
            }
            catch (Exception e) when (e is InvalidDataException or JsonException or InvalidOperationException)
            {
                Refuse(file, e.Message);
            }
            catch (IOException e)
            {
                lock (gate)
                    if (waiting.Add(file))
                        log($"Patch packs: {name} can't be read yet ({e.Message}); checking it again once it is complete.");
            }
        }

        /// <summary>The pack.json text exactly as stored (its SHA-256 is the pack's id).</summary>
        private static string PackManifestText(Patching.Pack pack)
        {
            using var reader = new PatchFile.Reader(pack.FilePath);
            return reader.Manifest;
        }

        private void Refuse(string file, string reason)
        {
            string name = Path.GetFileName(file);
            log($"Patch packs: {name} REFUSED and never served: {reason}");
            lock (gate)
            {
                refused.RemoveAll(r => r.File.Equals(name, StringComparison.OrdinalIgnoreCase));
                refused.Add((name, reason, DateTime.UtcNow));
                waiting.Remove(file);
            }
            try
            {
                string rejected = Path.Combine(Folder, "rejected");
                Directory.CreateDirectory(rejected);
                string to = Path.Combine(rejected, name);
                if (File.Exists(to))
                    to = Path.Combine(rejected, $"{Path.GetFileNameWithoutExtension(name)}-{DateTime.UtcNow:yyyyMMdd-HHmmss}{PatchFile.Extension}");
                File.Move(file, to);
                File.WriteAllText(to + ".reason.txt", $"{DateTime.UtcNow:u} refused: {reason}{Environment.NewLine}");
                log($"Patch packs: moved {name} to {to}.");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A read-only folder: it stays where it is, still never served (it isn't in the verified list).
                log($"Patch packs: couldn't move {name} to rejected\\ ({e.Message}); it stays there, not served.");
            }
        }

        /// <summary>
        /// The verified packs, in file-name order, that are still exactly as checked (size and write time; one that changed
        /// is checked again soon and left out until it passes).
        /// </summary>
        public List<Pack> Packs
        {
            get
            {
                List<Pack> all;
                lock (gate)
                    all = verified.Values.OrderBy(p => Path.GetFileName(p.FilePath), StringComparer.OrdinalIgnoreCase).ToList();
                return all.Where(Unchanged).ToList();
            }
        }

        /// <summary>The packs refused since start-up (file, reason), newest last.</summary>
        public List<(string File, string Reason, DateTime When)> Refused
        {
            get { lock (gate) return refused.ToList(); }
        }

        private bool Unchanged(Pack p)
        {
            var info = new FileInfo(p.FilePath);
            if (info.Exists && info.Length == p.FileBytes && info.LastWriteTimeUtc == p.Written)
                return true;
            lock (gate)
                verified.Remove(p.FilePath);
            log($"Patch packs: {Path.GetFileName(p.FilePath)} changed after it was checked; not served until it passes again.");
            ScanSoon();
            return false;
        }

        public Pack? Find(string id) => Packs.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Opens a verified pack for sending: the file is opened first (shared for reading only, so it can't be written while
        /// it is sent) and only served if it is still exactly the file that was checked. null = not served (unknown, or it
        /// changed: then it is checked again).
        /// </summary>
        public FileStream? OpenForServing(string id, out Pack? pack)
        {
            pack = null;
            Pack? p;
            lock (gate)
                p = verified.Values.FirstOrDefault(v => v.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (p == null)
                return null;
            FileStream s;
            try
            {
                s = new FileStream(p.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (IOException)
            {
                return null;
            }
            if (!Unchanged(p) || s.Length != p.FileBytes)
            {
                s.Dispose();
                return null;
            }
            pack = p;
            return s;
        }

        /// <summary>What /info and /patches list for a pack.</summary>
        public static object Describe(Pack p) => new
        {
            id = p.Id, name = p.Manifest.Name, version = p.Manifest.Version, requires = p.Manifest.Requires,
            description = p.Manifest.Description, files = p.Manifest.Files.Count, bytes = p.Manifest.Files.Sum(f => f.Size),
            packBytes = p.FileBytes, packSha256 = p.FileSha256, delete = p.Manifest.Delete.Count, sha256 = p.Sha256, build = p.Build
        };

        public void Dispose()
        {
            disposed = true;
            watcher?.Dispose();
            timer?.Dispose();
        }
    }

    /// <summary>The patch endpoints (see <see cref="PatchLibrary"/>).</summary>
    public static class PatchHost
    {
        public static void Map(WebApplication app, Func<PatchLibrary> library)
        {
            app.MapGet("/patches", () => Results.Ok(library().Packs.Select(PatchLibrary.Describe)));

            app.MapGet("/patches/{id}/pack.json", (string id) =>
            {
                PatchLibrary.Pack? pack = library().Find(id);
                return pack == null ? Unavailable(library(), id) : Results.Bytes(pack.ManifestBytes, "application/json");
            });

            app.MapGet("/patches/{id}/pack.lmpatch", (string id) =>
            {
                FileStream? stream = library().OpenForServing(id, out _);
                return stream == null ? Unavailable(library(), id)
                    : Results.Stream(stream, "application/octet-stream", enableRangeProcessing: true);
            });
        }

        /// <summary>404 for a pack the folder doesn't have; 503 while a pack that was verified is being checked again.</summary>
        private static IResult Unavailable(PatchLibrary library, string id)
            => File.Exists(Path.Combine(library.Folder, id + PatchFile.Extension)) && library.Folder.Length > 0
                && Regex.IsMatch(id, "^[A-Za-z0-9._-]{1,64}$")
                ? Results.StatusCode(503) : Results.NotFound();
    }
}
