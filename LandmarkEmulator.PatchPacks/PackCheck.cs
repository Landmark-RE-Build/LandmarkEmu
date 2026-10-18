using static LandmarkEmulator.PatchPacks.Patching;

namespace LandmarkEmulator.PatchPacks
{
    /// <summary>
    /// The whole check of a pack, entry by entry, the same on the server (PatchHost verifies every pack a server owner
    /// drops in its folder before serving it) and in LaunchPad (a made pack is checked before it is reported done; a
    /// downloaded one before it is installed; <see cref="GameStore.Install"/> repeats the per-entry checks with the
    /// player's own files as it installs).
    ///
    /// <list type="number">
    /// <item>The list: <see cref="Patching.CheckUpdate(Manifest)"/> (the verified update to a known build, every file's
    /// SHA-256 known).</item>
    /// <item>The index (<see cref="CheckIndex"/>): every file has its entry, no entry is unused, every entry's declared size
    /// is what that file needs (a whole file's is the build's size exactly).</item>
    /// <item>Every entry: unpacked (never past its declared size) with the SHA-256 the index gives; a whole file's SHA-256
    /// must be the genuine file's; an edit or patch must start from the Steam file's size and end at the build file's
    /// size and CRC32.</item>
    /// <item>With a verified Steam copy (<c>steamFolder</c>): every edit and patch is replayed on the Steam file (read
    /// only), and the result's SHA-256 must be the genuine file's. Without one, an edit or patch can't be replayed (the
    /// server has no game files): its result is then checked by each player's LaunchPad on their own Steam copy before
    /// any file changes.</item>
    /// </list>
    /// </summary>
    public static class PackCheck
    {
        /// <summary>What a passed check found.</summary>
        public sealed class Result
        {
            public TargetBuild Build { get; init; } = null!;
            public int Entries { get; init; }
            public long Bytes { get; init; }
            /// <summary>Edits and patches whose results were rebuilt and matched (0 without a Steam copy).</summary>
            public int Replayed { get; init; }
            /// <summary>Edits and patches that could only be checked by their headers (no Steam copy to replay them on).</summary>
            public int NotReplayed { get; init; }
        }

        /// <summary>The most an edit or patch entry may unpack to: the file it makes, plus room for the format's overhead.</summary>
        public static long MaxDeltaSize(PackFile f) => f.Size + Math.Max(f.Size / 8, 1 << 20);

        /// <summary>
        /// Checks the pack's index against its list without unpacking anything. Returns the problems (empty = fine) and the
        /// build the list leads to.
        /// </summary>
        public static List<string> CheckIndex(Pack pack, out TargetBuild target, bool allowFebruary = false)
        {
            var problems = CheckUpdate(pack.Manifest, out target, allowFebruary);
            if (problems.Count > 0)
                return problems;
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PackFile f in pack.Manifest.Files)
            {
                PatchFile.IndexEntry? e = pack.Entry(f.Entry);
                if (e == null)
                {
                    problems.Add($"the pack is missing {f.Entry}");
                    continue;
                }
                used.Add(e.Name.Replace('\\', '/'));
                if (f.How == "whole" && e.Size != f.Size)
                    problems.Add($"{f.Entry} unpacks to {e.Size:N0} bytes, but {f.Path} is {f.Size:N0}");
                else if (f.How != "whole" && (e.Size < 28 || e.Size > MaxDeltaSize(f)))
                    problems.Add($"{f.Entry} claims {e.Size:N0} bytes, more than an edit or patch of {f.Path} can need");
            }
            var unused = pack.Entries.Where(e => !used.Contains(e.Name.Replace('\\', '/'))).Select(e => e.Name).ToList();
            if (unused.Count > 0)
                problems.Add($"the pack has {unused.Count} entr{(unused.Count == 1 ? "y" : "ies")} its list doesn't use (first: {unused[0]})");
            return problems;
        }

        /// <summary>
        /// The whole check (see the class). Throws <see cref="InvalidDataException"/> with the reason when anything is wrong;
        /// nothing is written anywhere. <paramref name="steamFolder"/>: a Steam 0.589 copy to replay edits and patches on
        /// (each base file is held to Steam's SHA-1 first), or null.
        /// </summary>
        /// <param name="allowFebruary">
        /// The player's February opt-in (<see cref="Patching.WeakAllowed"/>): only LaunchPad passes it, and only for a pack the
        /// player chose or made themselves. The server and server downloads never do.
        /// </param>
        public static Result Verify(Pack pack, string? steamFolder, Progress? progress = null, CancellationToken cancel = default,
            bool allowFebruary = false)
        {
            List<string> problems = CheckIndex(pack, out TargetBuild target, allowFebruary);
            if (problems.Count > 0)
                throw new InvalidDataException($"not the verified update to {FinalBuild}: {string.Join("; ", problems)}");
            Manifest m = pack.Manifest;
            long total = m.Files.Sum(f => f.Size), done = 0;
            int replayed = 0, notReplayed = 0;
            foreach (PackFile f in m.Files)
            {
                cancel.ThrowIfCancellationRequested();
                progress?.Invoke("checking", done, total, f.Path);
                BuildFile want = target.Files[f.Path];
                bool weak = WeakAllowed(target, f.Path, allowFebruary);
                byte[] data = pack.Read(f.Entry);          // unpacked within its declared size, SHA-256 per the index
                switch (f.How)
                {
                    case "whole":
                        if (data.Length != want.Size || !$"{Delta.Crc(data):X8}".Equals(want.Crc, StringComparison.OrdinalIgnoreCase)
                            || (!weak && !Same(Sha256Of(data), want.Sha256)))
                            throw new InvalidDataException($"{f.Path} in the pack is not the genuine file (its SHA-256 differs from the {target.Label}'s)");
                        break;
                    case "edit":
                    {
                        InPlaceEdit.Edit e = InPlaceEdit.Read(data);
                        InPlaceEdit.CheckOrdered(e);
                        if (e.SizeBefore != f.FromSize || !$"{e.CrcBefore:X8}".Equals(f.FromCrc32, StringComparison.OrdinalIgnoreCase)
                            || e.SizeAfter != want.Size || !$"{e.CrcAfter:X8}".Equals(want.Crc, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException($"the edit for {f.Path} doesn't match its list");
                        if (SteamBase(steamFolder, f) is string basePath)
                        {
                            using FileStream s = File.OpenRead(basePath);
                            if (!weak && !Same(InPlaceEdit.ResultSha256(e, s), want.Sha256))
                                throw new InvalidDataException($"the edit for {f.Path} doesn't make the genuine file (SHA-256 differs)");
                            replayed++;
                        }
                        else
                            notReplayed++;
                        break;
                    }
                    default:
                    {
                        var (oldSize, oldCrc, newSize, newCrc) = Delta.ReadHeader(data);
                        if (oldSize != f.FromSize || !$"{oldCrc:X8}".Equals(f.FromCrc32, StringComparison.OrdinalIgnoreCase)
                            || newSize != want.Size || !$"{newCrc:X8}".Equals(want.Crc, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException($"the patch for {f.Path} doesn't match its list");
                        if (SteamBase(steamFolder, f) is string basePath)
                        {
                            using var sha = new HashingStream();
                            Delta.Apply(File.ReadAllBytes(basePath), data, sha);
                            if (sha.Length != want.Size || (!weak && !Same(sha.Sha256(), want.Sha256)))
                                throw new InvalidDataException($"the patch for {f.Path} doesn't make the genuine file (SHA-256 differs)");
                            replayed++;
                        }
                        else
                            notReplayed++;
                        break;
                    }
                }
                done += f.Size;
            }
            return new Result { Build = target, Entries = m.Files.Count, Bytes = total, Replayed = replayed, NotReplayed = notReplayed };
        }

        /// <summary>The Steam file an edit or patch starts from, when there is a Steam copy and it is Steam's file (SHA-1).</summary>
        private static string? SteamBase(string? steamFolder, PackFile f)
        {
            if (steamFolder == null)
                return null;
            string path = Path.Combine(steamFolder, f.Path);
            if (!SteamList.TryGetValue(f.Path, out var steam) || !File.Exists(path) || new FileInfo(path).Length != steam.Size)
                throw new InvalidDataException($"the Steam copy given to replay the update on has no Steam version of {f.Path}");
            using FileStream s = File.OpenRead(path);
            if (!Same(Hashes(s).Sha1, steam.Crc))
                throw new InvalidDataException($"the Steam copy given to replay the update on has a changed {f.Path} (SHA-1 differs from Steam's)");
            return path;
        }
    }

    /// <summary>A write-only stream that only counts and hashes what is written (SHA-256), e.g. a patch's result.</summary>
    public sealed class HashingStream : Stream
    {
        private readonly System.Security.Cryptography.IncrementalHash sha =
            System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        private readonly Stream? inner;
        private long length;

        /// <summary>Hashes what is written; with <paramref name="inner"/>, also passes it on there.</summary>
        public HashingStream(Stream? inner = null) => this.inner = inner;

        public string Sha256() => Convert.ToHexString(sha.GetHashAndReset());

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => length;
        public override long Position { get => length; set => throw new NotSupportedException(); }
        public override void Flush() => inner?.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            sha.AppendData(buffer);
            length += buffer.Length;
            inner?.Write(buffer);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                sha.Dispose();
            base.Dispose(disposing);
        }
    }
}
