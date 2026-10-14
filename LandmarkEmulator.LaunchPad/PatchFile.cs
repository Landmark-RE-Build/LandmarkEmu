using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LandmarkEmulator.LaunchPad
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
    /// </summary>
    public static class PatchFile
    {
        public const string Extension = ".lmpatch";
        private static readonly byte[] Magic = "LMPATCH1"u8.ToArray();
        private const int HeaderSize = 8 + 8 + 8;

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

        private static byte[] Decompress(byte[] stored, long size)
        {
            byte[] data = new byte[size];
            if (!BrotliDecoder.TryDecompress(stored, data, out int written) || written != size)
                throw new InvalidDataException("The patch file is damaged.");
            return data;
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

        /// <summary>Reads a .lmpatch: its manifest text and entries (each checked against its SHA-256 when read).</summary>
        public sealed class Reader : IDisposable
        {
            private readonly FileStream file;
            private readonly Dictionary<string, IndexEntry> entries = new(StringComparer.OrdinalIgnoreCase);
            public string Manifest { get; }

            public Reader(string path)
            {
                if (!path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                    throw new InvalidDataException($"Patch packs are {Extension} files.");
                file = File.OpenRead(path);
                try
                {
                    byte[] header = new byte[HeaderSize];
                    file.ReadExactly(header);
                    if (!header.AsSpan(0, 8).SequenceEqual(Magic))
                        throw new InvalidDataException($"This isn't a {Extension} patch pack.");
                    long at = BitConverter.ToInt64(header, 8), length = BitConverter.ToInt64(header, 16);
                    if (at < HeaderSize || length <= 0 || at + length > file.Length || length > 256 << 20)
                        throw new InvalidDataException("The patch file is damaged (index).");
                    byte[] stored = new byte[length];
                    file.Position = at;
                    file.ReadExactly(stored);
                    using var decoder = new BrotliStream(new MemoryStream(stored), CompressionMode.Decompress);
                    Index index = JsonSerializer.Deserialize<Index>(decoder, json) ?? throw new InvalidDataException("The patch file is damaged (index).");
                    Manifest = index.Manifest;
                    foreach (IndexEntry e in index.Entries)
                    {
                        if (e.Offset < HeaderSize || e.Stored < 0 || e.Offset + e.Stored > at || e.Size < 0)
                            throw new InvalidDataException("The patch file is damaged (entry).");
                        entries[e.Name] = e;
                    }
                }
                catch
                {
                    file.Dispose();
                    throw;
                }
            }

            public bool Has(string name) => entries.ContainsKey(name.Replace('\\', '/'));

            public IEnumerable<IndexEntry> Entries => entries.Values;

            /// <summary>An entry's data, unpacked and checked against its SHA-256.</summary>
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
