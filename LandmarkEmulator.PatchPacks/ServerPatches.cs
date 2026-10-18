using System.Text.Json;
using static LandmarkEmulator.PatchPacks.Patching;

namespace LandmarkEmulator.PatchPacks
{
    /// <summary>
    /// Where a server's packs come from (LaunchPad: the server's account service, ServerApi.cs; the tests: a fake).
    /// Nothing it returns is trusted: every byte is checked against the embedded lists before it is used.
    /// </summary>
    public interface IPatchSource
    {
        /// <summary>"host:port", for messages.</summary>
        string Display { get; }
        /// <summary>A hosted pack's pack.json, as bytes (GET /patches/{id}/pack.json). At most <paramref name="maxBytes"/>.</summary>
        Task<byte[]> Manifest(string id, long maxBytes, CancellationToken cancel);
        /// <summary>
        /// Downloads a hosted pack's .lmpatch to <paramref name="partFile"/>, continuing a broken-off download where it stopped
        /// (HTTP range). Stops with an error past <paramref name="maxBytes"/> (the size the server advertised).
        /// <paramref name="read"/> gets the bytes on disk so far.
        /// </summary>
        Task Download(string id, string partFile, long maxBytes, Action<long> read, CancellationToken cancel);
    }

    /// <summary>
    /// The update hosted by a server (its /info "patches", LandmarkEmulator.WebAPI/PatchHost.cs), installed only when the
    /// player says so (LaunchPad asks every time, showing the server, the build, the size and that it will be checked;
    /// Settings can switch it off). It can only be the verified update to a known 0.695 build, on a verified Steam copy of
    /// the game: LaunchPad is not a game downloader, and a server owner can't get any other file onto a player's PC.
    ///
    /// The order (each step must pass before the next, and any failure deletes the download and changes nothing):
    /// <list type="number">
    /// <item>The server's list (pack.json) is fetched first and checked against what /info announced (its SHA-256) and
    /// against the embedded lists (<see cref="Patching.CheckUpdate(Manifest)"/>), and the game folder must be a verified
    /// Steam copy the update would complete: before a byte of the pack is downloaded.</item>
    /// <item>The pack is downloaded to LaunchPadEmu\downloads\ (resumed if it broke off), never past the size /info
    /// advertised.</item>
    /// <item>The whole file's SHA-256 must be what /info advertised (finds a swapped or damaged file; it is not what makes
    /// the pack trusted: the server could advertise anything).</item>
    /// <item>Its own list must be the one checked in step 1, and every entry is checked (<see cref="PackCheck"/>):
    /// against the embedded strong list, the trust anchor.</item>
    /// <item>Only then the normal install (<see cref="GameStore.Install"/>: backed up, every file's SHA-256 checked again
    /// before it replaces anything, undoable).</item>
    /// </list>
    /// Nothing downloaded is ever started or opened by anything but this check and the install.
    /// </summary>
    public static class ServerPatches
    {
        /// <summary>A pack in a server's list.</summary>
        public sealed class Listed
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string Version { get; set; } = "";
            public string Requires { get; set; } = "";
            public string Description { get; set; } = "";
            public int Files { get; set; }
            public long Bytes { get; set; }
            public int Delete { get; set; }
            /// <summary>SHA-256 of its pack.json (its identity).</summary>
            public string Sha256 { get; set; } = "";
            /// <summary>The .lmpatch file's size and SHA-256, as the server verified it.</summary>
            public long PackBytes { get; set; }
            public string PackSha256 { get; set; } = "";
        }

        /// <summary>The biggest pack.json accepted (the update's is about 0.3 MB).</summary>
        public const long MaxManifestBytes = 16 << 20;

        public static List<Listed> FromInfo(JsonElement info)
        {
            var list = new List<Listed>();
            if (info.ValueKind == JsonValueKind.Object && info.TryGetProperty("patches", out JsonElement patches) && patches.ValueKind == JsonValueKind.Array)
                foreach (JsonElement p in patches.EnumerateArray().Take(16))
                    if (p.ValueKind == JsonValueKind.Object)
                        list.Add(new Listed
                        {
                            Id = Str(p, "id"), Name = Str(p, "name"), Version = Str(p, "version"), Requires = Str(p, "requires"),
                            Description = Str(p, "description"), Sha256 = Str(p, "sha256").ToUpperInvariant(),
                            PackSha256 = Str(p, "packSha256").ToUpperInvariant(),
                            Files = p.TryGetProperty("files", out var f) && f.TryGetInt32(out int n) ? n : 0,
                            Bytes = p.TryGetProperty("bytes", out var b) && b.TryGetInt64(out long l) ? l : 0,
                            PackBytes = p.TryGetProperty("packBytes", out var pb) && pb.TryGetInt64(out long pl) ? pl : 0,
                            Delete = p.TryGetProperty("delete", out var d) && d.TryGetInt32(out int k) ? k : 0
                        });
            return list;
        }

        private static string Str(JsonElement e, string name)
            => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        /// <summary>
        /// The pack to offer: the first one the server lists, when the game is the Steam build with nothing installed yet.
        /// The only pack LaunchPad installs is the update, so a game that has it (from this server, another one or the
        /// player's own file) needs nothing; nothing is ever undone to make room for a server's pack (until 2026-10-18 a
        /// server's list replaced the player's own update, a 20 GB churn for the same build).
        /// </summary>
        public static Listed? Needed(GameStore store, List<Listed> listed)
            => listed.Count > 0 && store.Stack.Count == 0 && GameFolder.Build(store.Game) == SteamBuild ? listed[0] : null;

        /// <summary>What the player is asked about (nothing has been downloaded yet).</summary>
        public sealed class Offer
        {
            public Listed Listed { get; init; } = new();
            public Manifest Manifest { get; init; } = new();
            public TargetBuild Build { get; init; } = null!;
        }

        private static bool IsSha256(string s) => s.Length == 64 && s.All(Uri.IsHexDigit);

        /// <summary>
        /// Step 1: the server's list for the pack, checked against what /info announced and the embedded lists. Throws with
        /// the reason when the server offers anything but the verified update. Downloads only the list.
        /// </summary>
        public static async Task<Offer> Check(IPatchSource source, Listed listed, CancellationToken cancel)
        {
            if (!IsSha256(listed.Sha256) || !IsSha256(listed.PackSha256) || listed.PackBytes <= PatchFile.HeaderSize
                || listed.PackBytes > PatchFile.MaxPackSize || listed.Id.Length is 0 or > 64 || !listed.Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
                throw new InvalidDataException($"The server's patch list for \"{listed.Name}\" is incomplete (no size or SHA-256), so it can't be checked. Nothing was downloaded.");
            byte[] manifestBytes = await source.Manifest(listed.Id, MaxManifestBytes, cancel);
            if (manifestBytes.Length > MaxManifestBytes)
                throw new InvalidDataException("The server's patch list is far too big. Nothing was downloaded.");
            if (!Sha256Of(manifestBytes).Equals(listed.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The server's list for \"{listed.Name}\" isn't the one it announced (SHA-256 differs). Nothing was downloaded.");
            Manifest m = ParseManifest(manifestBytes);
            List<string> problems = CheckUpdate(m, out TargetBuild build);
            if (problems.Count > 0)
                throw new InvalidOperationException($"This server's patch \"{listed.Name}\" is not a verified update to {FinalBuild}: {string.Join("; ", problems)}. Nothing was downloaded.");
            return new Offer { Listed = listed, Manifest = m, Build = build };
        }

        /// <summary>
        /// Steps 1 to 5 (see the class): checks the list again, the game folder, downloads (or reuses an earlier verified
        /// download), checks the download, installs. <paramref name="checkedId"/>: the pack.json SHA-256 the player was shown
        /// and agreed to; anything else is refused.
        /// </summary>
        public static async Task<InstalledPack> Install(GameStore store, IPatchSource source, string sourceName, Listed listed,
            string checkedId, Progress progress, CancellationToken cancel)
        {
            if (!listed.Sha256.Equals(checkedId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The server's patch changed since you agreed to it. Nothing was downloaded; check it again.");
            Offer offer = await Check(source, listed, cancel);
            await Task.Run(() => store.RequireSteamCopy(progress, cancel), cancel);
            List<Problem> left = await Task.Run(() => store.CheckComplete(offer.Manifest, progress, cancel), cancel);
            if (left.Count > 0)
                throw new InvalidOperationException($"This server's patch \"{listed.Name}\" is incomplete: {left.Count} file(s) of the build would still be missing or old (first: {left[0].Path}). Nothing was downloaded.");

            Directory.CreateDirectory(store.DownloadFolder);
            GameFolder.RequirePlainPath(store.Game, GameStore.FolderName + "\\downloads\\x");
            string file = Path.Combine(store.DownloadFolder, listed.Sha256 + PatchFile.Extension);
            string part = file + ".part";
            try
            {
                if (!File.Exists(file))
                {
                    if (File.Exists(part) && new FileInfo(part).Length > listed.PackBytes)
                        File.Delete(part);
                    await source.Download(listed.Id, part, listed.PackBytes,
                        n => progress("downloading", n, listed.PackBytes, listed.Name), cancel);
                    File.Move(part, file, true);
                }
                await Task.Run(() => CheckDownload(file, listed, offer, progress, cancel), cancel);
            }
            catch (Exception e) when (e is not OperationCanceledException && e is not ServerUnavailable)
            {
                // A download that was checked and failed is never kept (a broken-off one stays as .part, to continue).
                if (e is InvalidDataException or InvalidOperationException)
                {
                    TryDelete(part);
                    TryDelete(file);
                }
                throw;
            }
            using var pack = new Pack(file);
            try
            {
                return await Task.Run(() => store.Install(pack, source.Display, sourceName, progress, cancel), cancel);
            }
            catch (InvalidDataException)
            {
                pack.Dispose();
                TryDelete(file);
                throw;
            }
        }

        /// <summary>The server can't send the pack right now (it is checking it again, or the download broke off).</summary>
        public sealed class ServerUnavailable(string message) : Exception(message);

        /// <summary>Steps 3 and 4: the downloaded file is the one advertised, its list the one checked, every entry genuine.</summary>
        private static void CheckDownload(string file, Listed listed, Offer offer, Progress progress, CancellationToken cancel)
        {
            var info = new FileInfo(file);
            if (info.Length != listed.PackBytes)
                throw new InvalidDataException($"The downloaded patch is {info.Length:N0} bytes, not the {listed.PackBytes:N0} the server announced. It was deleted.");
            string sha;
            using (FileStream s = File.OpenRead(file))
            {
                using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
                byte[] buffer = new byte[1 << 20];
                long done = 0;
                int n;
                while ((n = s.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancel.ThrowIfCancellationRequested();
                    hash.AppendData(buffer, 0, n);
                    done += n;
                    progress("checking", done, info.Length, listed.Name);
                }
                sha = Convert.ToHexString(hash.GetHashAndReset());
            }
            if (!sha.Equals(listed.PackSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded patch isn't the file the server announced (its SHA-256 differs): it was swapped or damaged on the way. It was deleted.");
            using var pack = new Pack(file);
            if (!pack.Id.Equals(listed.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded patch's own list isn't the list that was checked. It was deleted.");
            try
            {
                PackCheck.Verify(pack, null, progress, cancel);
            }
            catch (InvalidDataException e)
            {
                throw new InvalidDataException($"The downloaded patch was refused: {e.Message}. It was deleted.");
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
