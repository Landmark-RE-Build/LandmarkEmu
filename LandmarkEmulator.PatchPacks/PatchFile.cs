using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LandmarkEmulator.PatchPacks
{
    /// <summary>
    /// The .lmpatch file: one patch pack in one file, smaller than a .zip (each entry is Brotli-compressed, about a third
    /// smaller than zip's Deflate on game files) and checked as it is read (SHA-256 per entry).
    ///
    /// Layout:
    /// <code>
    ///   "LMPATCH1"           8 bytes
    ///   u64 index offset     where the index starts
    ///   u64 index length     its compressed length
    ///   entries              each one Brotli-compressed, back to back
    ///   index                Brotli-compressed UTF-8 JSON:
    ///                        { "manifest": "&lt;pack.json text&gt;",
    ///                          "entries": [ { "name", "offset", "stored", "size", "sha256" }, ... ] }
    /// </code>
    /// The pack's identity is the SHA-256 of the manifest text. Entry names use "/" (e.g. "Resources/Assets/Assets_000.pack",
    /// "edits/....lme", "patches/....lmd"; see <see cref="Patching.PackFile.Entry"/>).
    ///
    /// A pack may come from anyone, so reading one never trusts its numbers (2026-10-18): the file, the index and every
    /// entry have size limits; the index may not unpack past its limit (a "Brotli bomb" stops there); entries must lie
    /// between the header and the index, not overlap and not repeat a name; an entry never unpacks past its declared
    /// size, and must unpack to exactly that size with the SHA-256 the index gives.
    /// </summary>
    public static class PatchFile
    {
        public const string Extension = ".lmpatch";
        private static readonly byte[] Magic = "LMPATCH1"u8.ToArray();
        public const int HeaderSize = 8 + 8 + 8;

        /// <summary>The biggest pack read: the whole update as whole files is about 3 GB, so 32 GB leaves plenty of room.</summary>
        public const long MaxPackSize = 32L << 30;
        /// <summary>The index's limit, packed and unpacked (the real one: about 0.3 MB packed, 2 MB unpacked).</summary>
        public const int MaxIndexSize = 64 << 20;
        /// <summary>The biggest entry (the biggest game file is 268 MB).</summary>
        public const long MaxEntrySize = 1L << 30;
        /// <summary>No pack needs more entries than the game has files.</summary>
        public const int MaxEntries = Patching.MaxManifestFiles;

        public sealed class IndexEntry
        {
            public string Name { get; set; } = "";
            public long Offset { get; set; }
            public long Stored { get; set; }
            public long Size { get; set; }
            public string Sha256 { get; set; } = "";
        }

        private sealed class Index
        {
            public string Manifest { get; set; } = "";
            public List<IndexEntry> Entries { get; set; } = new();
        }

        private static readonly JsonSerializerOptions json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private static byte[] Compress(byte[] data)
        {
            using var output = new MemoryStream();
            // Best compression for the many small entries; a little faster for big ones (Brotli's top level is slow).
            using (var encoder = new BrotliEncoder(quality: data.Length > 8 << 20 ? 9 : 11, window: 24))
            {
                byte[] buffer = new byte[BrotliEncoder.GetMaxCompressedLength(Math.Max(data.Length, 1))];
                if (encoder.Compress(data, buffer, out _, out int written, isFinalBlock: true) != System.Buffers.OperationStatus.Done)
                    throw new IOException("Compression failed.");
                output.Write(buffer, 0, written);
            }
            return output.ToArray();
        }

        /// <summary>
        /// Unpacks into a buffer of exactly the declared size: Brotli stops with an error when the data would go past it,
        /// so a small entry that claims to be small but unpacks to gigabytes never gets further than that.
        /// </summary>
        private static byte[] Decompress(byte[] stored, long size)
        {
            if (size < 0 || size > MaxEntrySize)
                throw new InvalidDataException("The patch file is damaged (an entry is bigger than any game file).");
            byte[] data = new byte[size];
            // TryDecompress fails (false) when the data would need more room than the buffer: it never writes past it.
            if (!BrotliDecoder.TryDecompress(stored, data, out int written) || written != size)
                throw new InvalidDataException("The patch file is damaged (an entry doesn't unpack to its size).");
            return data;
        }

        /// <summary>Unpacks a Brotli stream, refusing to go past <paramref name="limit"/> bytes.</summary>
        private static byte[] DecompressLimited(byte[] stored, int limit)
        {
            using var decoder = new BrotliStream(new MemoryStream(stored), CompressionMode.Decompress);
            using var output = new MemoryStream();
            byte[] buffer = new byte[1 << 16];
            int n;
            while ((n = decoder.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + n > limit)
                    throw new InvalidDataException("The patch file is damaged (its index unpacks past its limit).");
                output.Write(buffer, 0, n);
            }
            return output.ToArray();
        }

        /// <summary>Writes a .lmpatch: add the entries, then <see cref="Finish"/> with the pack.json text.</summary>
        public sealed class Writer : IDisposable
        {
            private readonly FileStream file;
            private readonly List<IndexEntry> entries = new();

            public Writer(string path)
            {
                if (!path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"Patch packs are {Extension} files.");
                file = File.Create(path);
                file.Write(new byte[HeaderSize]);
            }

            public void Add(string name, byte[] data)
            {
                byte[] stored = Compress(data);
                entries.Add(new IndexEntry
                {
                    Name = name.Replace('\\', '/'), Offset = file.Position, Stored = stored.Length, Size = data.Length,
                    Sha256 = Convert.ToHexString(SHA256.HashData(data))
                });
                file.Write(stored);
            }

            public void Finish(string manifest)
            {
                byte[] index = Compress(JsonSerializer.SerializeToUtf8Bytes(new Index { Manifest = manifest, Entries = entries }, json));
                long at = file.Position;
                file.Write(index);
                file.Position = 0;
                file.Write(Magic);
                file.Write(BitConverter.GetBytes(at));
                file.Write(BitConverter.GetBytes((long)index.Length));
                file.Flush(true);
            }

            public void Dispose() => file.Dispose();
        }

        /// <summary>
        /// The manifest and index of an .lmpatch from its first bytes and its index bytes, checked the same way as
        /// <see cref="Reader"/> (for a download: read the header and the index with two range requests, check them, and
        /// only then fetch the rest).
        /// </summary>
        public static (long IndexOffset, long IndexLength) ReadHeader(ReadOnlySpan<byte> header, long fileLength)
        {
            if (header.Length < HeaderSize || !header[..8].SequenceEqual(Magic))
                throw new InvalidDataException($"This isn't a {Extension} patch pack.");
            if (fileLength > MaxPackSize)
                throw new InvalidDataException("The patch file is bigger than any patch pack.");
            long at = BitConverter.ToInt64(header[8..16]), length = BitConverter.ToInt64(header[16..24]);
            if (at < HeaderSize || length <= 0 || length > MaxIndexSize || at > fileLength - length || at + length != fileLength)
                throw new InvalidDataException("The patch file is damaged (index).");
            return (at, length);
        }

        /// <summary>Parses and checks an index (its stored bytes): the manifest text and the entries, in file order.</summary>
        public static (string Manifest, List<IndexEntry> Entries) ParseIndex(byte[] stored, long indexOffset)
        {
            Index index;
            try
            {
                index = JsonSerializer.Deserialize<Index>(DecompressLimited(stored, MaxIndexSize), json)
                    ?? throw new InvalidDataException("The patch file is damaged (index).");
            }
            catch (JsonException)
            {
                throw new InvalidDataException("The patch file is damaged (index).");
            }
            if (index.Manifest == null || index.Entries == null || index.Entries.Count > MaxEntries)
                throw new InvalidDataException("The patch file is damaged (index).");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (IndexEntry e in index.Entries)
            {
                if (e == null || string.IsNullOrEmpty(e.Name) || e.Sha256 == null || e.Sha256.Length != 64 || !e.Sha256.All(Uri.IsHexDigit))
                    throw new InvalidDataException("The patch file is damaged (entry).");
                if (e.Offset < HeaderSize || e.Stored <= 0 || e.Stored > MaxEntrySize + (1 << 20) || e.Offset > indexOffset - e.Stored
                    || e.Size < 0 || e.Size > MaxEntrySize)
                    throw new InvalidDataException($"The patch file is damaged (entry {e.Name} lies outside the pack's data).");
                if (!names.Add(e.Name.Replace('\\', '/')))
                    throw new InvalidDataException($"The patch file is damaged (entry {e.Name} is listed twice).");
            }
            // No two entries may share bytes: each lies after the end of the one before.
            var ordered = index.Entries.OrderBy(e => e.Offset).ToList();
            for (int i = 1; i < ordered.Count; i++)
                if (ordered[i].Offset < ordered[i - 1].Offset + ordered[i - 1].Stored)
                    throw new InvalidDataException($"The patch file is damaged (entries {ordered[i - 1].Name} and {ordered[i].Name} overlap).");
            return (index.Manifest, ordered);
        }

        /// <summary>Reads a .lmpatch: its manifest text and entries (each checked against its SHA-256 when read).</summary>
        public sealed class Reader : IDisposable
        {
            private readonly FileStream file;
            private readonly Dictionary<string, IndexEntry> entries = new(StringComparer.OrdinalIgnoreCase);
            private readonly List<IndexEntry> ordered;
            public string Manifest { get; }
            /// <summary>The pack file's length when it was opened (it is held open, so it can't change underneath).</summary>
            public long Length { get; }

            public Reader(string path)
            {
                if (!path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                    throw new InvalidDataException($"Patch packs are {Extension} files.");
                // Shared for reading only: nobody can change the pack while it is open here.
                file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                try
                {
                    Length = file.Length;
                    byte[] header = new byte[HeaderSize];
                    if (file.Length < HeaderSize)
                        throw new InvalidDataException($"This isn't a {Extension} patch pack.");
                    file.ReadExactly(header);
                    (long at, long length) = ReadHeader(header, file.Length);
                    byte[] stored = new byte[length];
                    file.Position = at;
                    file.ReadExactly(stored);
                    (Manifest, ordered) = ParseIndex(stored, at);
                    foreach (IndexEntry e in ordered)
                        entries[e.Name.Replace('\\', '/')] = e;
                }
                catch
                {
                    file.Dispose();
                    throw;
                }
            }

            public bool Has(string name) => entries.ContainsKey(name.Replace('\\', '/'));

            public IndexEntry? Entry(string name) => entries.TryGetValue(name.Replace('\\', '/'), out IndexEntry? e) ? e : null;

            /// <summary>The entries in file order.</summary>
            public IEnumerable<IndexEntry> Entries => ordered;

            /// <summary>An entry's data, unpacked (never past its declared size) and checked against its SHA-256.</summary>
            public byte[] Read(string name)
            {
                if (!entries.TryGetValue(name.Replace('\\', '/'), out IndexEntry? e))
                    throw new FileNotFoundException($"The patch pack has no {name}.");
                byte[] stored = new byte[e.Stored];
                lock (file)
                {
                    file.Position = e.Offset;
                    file.ReadExactly(stored);
                }
                byte[] data = Decompress(stored, e.Size);
                if (!Convert.ToHexString(SHA256.HashData(data)).Equals(e.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{name} in the patch pack is damaged.");
                return data;
            }

            public void Dispose() => file.Dispose();
        }

        /// <summary>The pack's identity: SHA-256 of its manifest text.</summary>
        public static string IdOf(string manifest) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest)));
    }
}
