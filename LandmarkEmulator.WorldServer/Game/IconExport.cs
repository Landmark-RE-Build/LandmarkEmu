using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Exports the item / category icons the world's web pages serve (Showcase store, GET /icons/&lt;image set id&gt;.png)
    /// from the owner's own game folder into their game data folder, as <c>&lt;out&gt;/&lt;image set id&gt;.png</c>
    /// (docs/showcase.md "Icons"). Read-only on the game folder; no game file is in the repository.
    ///
    /// Run: <c>dotnet LandmarkEmulator.WorldServer.dll --export-icons &lt;Landmark folder&gt; &lt;out folder&gt; [--size 6]</c>.
    /// Docker runs it as the one-shot <c>icons</c> service after <c>setup</c> (docker-compose.yml), into
    /// server-data/gamedata/icons.
    ///
    /// The chain: image set id → Resources/Images/ImageSetMappings.txt (IMAGE_TYPE 6 = 128 px icon, 5 = 64 px, 4 = 32 px;
    /// the preferred type first, then 6, 5, 4, 7) → Images.txt file name → a loose file under Resources/Images/&lt;folder&gt;/
    /// (nearly all icons) or a file inside Resources/Assets/*.pack (docs/client-reference.md; the pack index is chunks of
    /// {u32 next chunk offset, u32 n, n × {u32 name length, name, u32 offset, u32 length, u32 crc}}, big-endian; the first
    /// pack in name order wins) → a DDS (DXT1 / DXT3 / DXT5, the only formats the icons use) decoded here, or a PNG copied
    /// as is.
    ///
    /// Icons already exported are kept. A stamp file (<see cref="StampName"/>) records the image tables it was made from,
    /// so a later run with the same tables returns at once without reading the packs again.
    /// </summary>
    public static class IconExport
    {
        public const string Argument = "--export-icons";
        public const string StampName = ".icons-export";
        private const int Version = 1;

        /// <summary>The command line (after <see cref="Argument"/>): game folder, out folder, optional --size N. Exit code.</summary>
        public static int Run(string[] args)
        {
            var plain = new List<string>();
            int size = 6;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--size" && i + 1 < args.Length && int.TryParse(args[i + 1], out int s))
                {
                    size = s;
                    i++;
                }
                else
                    plain.Add(args[i]);
            }
            if (plain.Count != 2)
            {
                Console.Error.WriteLine($"usage: dotnet LandmarkEmulator.WorldServer.dll {Argument} <Landmark folder> <out folder> [--size 6]");
                return 2;
            }
            try
            {
                Export(plain[0], plain[1], size, Console.Out);
                return 0;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Console.Error.WriteLine($"icons: {e.Message}");
                return 1;
            }
        }

        /// <summary>Exports every image set's icon not yet in <paramref name="outDir"/>. Returns (written, kept, missing, failed).</summary>
        public static (int Written, int Kept, int Missing, int Failed) Export(string game, string outDir, int size = 6, TextWriter log = null)
        {
            string images = Path.Combine(game, "Resources", "Images");
            string imagesTable = Path.Combine(images, "Images.txt"), mappingsTable = Path.Combine(images, "ImageSetMappings.txt");
            if (!File.Exists(imagesTable) || !File.Exists(mappingsTable))
                throw new IOException($"{game} has no Resources/Images/Images.txt and ImageSetMappings.txt: not a Landmark folder.");

            Directory.CreateDirectory(outDir);
            string stampPath = Path.Combine(outDir, StampName);
            string stamp = $"v{Version} size {size}; {Describe(imagesTable)}; {Describe(mappingsTable)}";
            if (File.Exists(stampPath) && File.ReadAllText(stampPath).Trim() == stamp)
            {
                log?.WriteLine($"icons: up to date in {outDir}.");
                return (0, 0, 0, 0);
            }

            var files = new Dictionary<uint, string>();
            foreach (string[] r in Table(imagesTable))
                if (r.Length > 1 && uint.TryParse(r[0], out uint id))
                    files[id] = r[1];
            var bySet = new Dictionary<uint, Dictionary<uint, uint>>();
            foreach (string[] r in Table(mappingsTable))
                if (r.Length > 2 && uint.TryParse(r[0], out uint set) && uint.TryParse(r[1], out uint type) && uint.TryParse(r[2], out uint image))
                {
                    if (!bySet.TryGetValue(set, out Dictionary<uint, uint> types))
                        bySet[set] = types = new Dictionary<uint, uint>();
                    types[type] = image;
                }

            uint[] order = new[] { (uint)size }.Concat(new uint[] { 6, 5, 4, 7 }.Where(t => t != size)).ToArray();
            var wanted = new SortedDictionary<uint, string>();
            foreach ((uint set, Dictionary<uint, uint> types) in bySet)
                foreach (uint t in order)
                    if (types.TryGetValue(t, out uint image) && files.TryGetValue(image, out string name))
                    {
                        wanted[set] = name.Trim().ToLowerInvariant();
                        break;
                    }

            // Loose files: file name (lower case) -> path. Packs only when an icon is not loose.
            var loose = new Dictionary<string, string>();
            foreach (string path in Directory.EnumerateFiles(images, "*", SearchOption.AllDirectories))
                loose.TryAdd(Path.GetFileName(path).ToLowerInvariant(), path);
            Dictionary<string, (string Pack, long Offset, uint Length)> packed = null;

            int written = 0, kept = 0, missing = 0, failed = 0;
            var handles = new Dictionary<string, FileStream>();
            try
            {
                foreach ((uint set, string name) in wanted)
                {
                    string target = Path.Combine(outDir, $"{set}.png");
                    if (File.Exists(target))
                    {
                        kept++;
                        continue;
                    }
                    byte[] raw;
                    if (loose.TryGetValue(name, out string file))
                        raw = File.ReadAllBytes(file);
                    else
                    {
                        packed ??= IndexPacks(Path.Combine(game, "Resources", "Assets"));
                        if (!packed.TryGetValue(name, out var hit))
                        {
                            missing++;
                            continue;
                        }
                        if (!handles.TryGetValue(hit.Pack, out FileStream fs))
                            handles[hit.Pack] = fs = File.OpenRead(hit.Pack);
                        raw = new byte[hit.Length];
                        fs.Seek(hit.Offset, SeekOrigin.Begin);
                        fs.ReadExactly(raw);
                    }
                    try
                    {
                        byte[] png = ToPng(raw);
                        string part = target + ".part";
                        File.WriteAllBytes(part, png);
                        File.Move(part, target, true);
                        written++;
                    }
                    catch (InvalidDataException e)
                    {
                        if (++failed <= 5)
                            log?.WriteLine($"  {set} {name}: {e.Message}");
                    }
                }
            }
            finally
            {
                foreach (FileStream fs in handles.Values)
                    fs.Dispose();
            }
            File.WriteAllText(stampPath, stamp + "\n");
            log?.WriteLine($"icons: {written} written, {kept} already there, {missing} not in the game files, {failed} undecodable -> {outDir}");
            return (written, kept, missing, failed);
        }

        private static string Describe(string path)
        {
            var info = new FileInfo(path);
            return $"{info.Name} {info.Length} {info.LastWriteTimeUtc.Ticks}";
        }

        private static IEnumerable<string[]> Table(string path)
            => File.ReadLines(path, Encoding.UTF8).Where(l => l.Trim().Length > 0 && !l.StartsWith('#')).Select(l => l.Split('^'));

        /// <summary>Every file in the asset packs: name (lower case) -> (pack, offset, length). Reads only the index chunks.</summary>
        private static Dictionary<string, (string, long, uint)> IndexPacks(string assets)
        {
            var index = new Dictionary<string, (string, long, uint)>();
            if (!Directory.Exists(assets))
                return index;
            byte[] word = new byte[12];
            foreach (string pack in Directory.GetFiles(assets, "*.pack").OrderBy(p => p, StringComparer.Ordinal))
            {
                using var fs = new FileStream(pack, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
                long length = fs.Length, pos = 0;
                var seen = new HashSet<long>();
                while (pos + 8 <= length && seen.Add(pos))
                {
                    fs.Seek(pos, SeekOrigin.Begin);
                    fs.ReadExactly(word, 0, 8);
                    uint next = BinaryPrimitives.ReadUInt32BigEndian(word), n = BinaryPrimitives.ReadUInt32BigEndian(word.AsSpan(4));
                    for (uint i = 0; i < n; i++)
                    {
                        fs.ReadExactly(word, 0, 4);
                        int nameLength = (int)BinaryPrimitives.ReadUInt32BigEndian(word);
                        if (nameLength is < 0 or > 4096)
                            throw new InvalidDataException($"{Path.GetFileName(pack)}: bad index entry at chunk 0x{pos:X}.");
                        byte[] name = new byte[nameLength];
                        fs.ReadExactly(name);
                        fs.ReadExactly(word, 0, 12);
                        uint offset = BinaryPrimitives.ReadUInt32BigEndian(word), size = BinaryPrimitives.ReadUInt32BigEndian(word.AsSpan(4));
                        index.TryAdd(Encoding.Latin1.GetString(name).ToLowerInvariant(), (pack, offset, size));
                    }
                    if (next == 0)
                        break;
                    pos = next;
                }
            }
            return index;
        }

        private static readonly byte[] PngSignature = { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>A PNG of the image: a PNG is kept as it is, a DDS (DXT1 / DXT3 / DXT5, top mip) is decoded.</summary>
        public static byte[] ToPng(byte[] raw)
        {
            if (raw.AsSpan().StartsWith(PngSignature))
                return raw;
            (int width, int height, byte[] rgba) = DecodeDds(raw);
            return EncodePng(width, height, rgba);
        }

        /// <summary>The top mip of a DXT1 / DXT3 / DXT5 DDS as RGBA bytes.</summary>
        public static (int Width, int Height, byte[] Rgba) DecodeDds(byte[] dds)
        {
            if (dds.Length < 128 || dds[0] != 'D' || dds[1] != 'D' || dds[2] != 'S' || dds[3] != ' ')
                throw new InvalidDataException("not a DDS or PNG file");
            int height = BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(12));
            int width = BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(16));
            uint pixelFlags = BinaryPrimitives.ReadUInt32LittleEndian(dds.AsSpan(80));
            string fourCc = Encoding.ASCII.GetString(dds, 84, 4);
            if ((pixelFlags & 0x4) == 0 || fourCc is not ("DXT1" or "DXT3" or "DXT5"))
                throw new InvalidDataException($"unsupported DDS format (flags 0x{pixelFlags:X}, '{fourCc}')");
            if (width is <= 0 or > 8192 || height is <= 0 or > 8192)
                throw new InvalidDataException($"bad DDS size {width}x{height}");
            int blockBytes = fourCc == "DXT1" ? 8 : 16;
            int bw = (width + 3) / 4, bh = (height + 3) / 4;
            if (128 + (long)bw * bh * blockBytes > dds.Length)
                throw new InvalidDataException("DDS data is cut short");

            byte[] rgba = new byte[width * height * 4];
            var colours = new byte[16];
            var alphas = new byte[8];
            int p = 128;
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++, p += blockBytes)
                {
                    int c = blockBytes == 16 ? p + 8 : p;
                    ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(dds.AsSpan(c)), c1 = BinaryPrimitives.ReadUInt16LittleEndian(dds.AsSpan(c + 2));
                    Expand(c0, colours, 0);
                    Expand(c1, colours, 4);
                    bool fourColours = fourCc != "DXT1" || c0 > c1;
                    for (int k = 0; k < 3; k++)
                    {
                        int a = colours[k], b = colours[4 + k];
                        if (fourColours)
                        {
                            colours[8 + k] = (byte)((2 * a + b) / 3);
                            colours[12 + k] = (byte)((a + 2 * b) / 3);
                        }
                        else
                        {
                            colours[8 + k] = (byte)((a + b) / 2);
                            colours[12 + k] = 0;
                        }
                    }
                    colours[3] = colours[7] = colours[11] = 255;
                    colours[15] = (byte)(fourColours ? 255 : 0);
                    uint indices = BinaryPrimitives.ReadUInt32LittleEndian(dds.AsSpan(c + 4));

                    if (fourCc == "DXT5")
                    {
                        alphas[0] = dds[p];
                        alphas[1] = dds[p + 1];
                        if (alphas[0] > alphas[1])
                            for (int k = 1; k < 7; k++)
                                alphas[1 + k] = (byte)(((7 - k) * alphas[0] + k * alphas[1]) / 7);
                        else
                        {
                            for (int k = 1; k < 5; k++)
                                alphas[1 + k] = (byte)(((5 - k) * alphas[0] + k * alphas[1]) / 5);
                            alphas[6] = 0;
                            alphas[7] = 255;
                        }
                    }
                    ulong alphaBits = 0;
                    if (fourCc == "DXT5")
                        for (int k = 0; k < 6; k++)
                            alphaBits |= (ulong)dds[p + 2 + k] << (8 * k);

                    for (int y = 0; y < 4; y++)
                        for (int x = 0; x < 4; x++)
                        {
                            int px = bx * 4 + x, py = by * 4 + y;
                            int i = y * 4 + x;
                            if (px >= width || py >= height)
                                continue;
                            int o = (py * width + px) * 4;
                            int ci = (int)(indices >> (2 * i)) & 3;
                            rgba[o] = colours[ci * 4];
                            rgba[o + 1] = colours[ci * 4 + 1];
                            rgba[o + 2] = colours[ci * 4 + 2];
                            rgba[o + 3] = fourCc switch
                            {
                                "DXT5" => alphas[(int)(alphaBits >> (3 * i)) & 7],
                                "DXT3" => (byte)(((dds[p + i / 2] >> (4 * (i & 1))) & 0xF) * 17),
                                _ => colours[ci * 4 + 3],
                            };
                        }
                }
            return (width, height, rgba);
        }

        /// <summary>RGB565 to 8-bit channels (the top bits repeated into the low ones).</summary>
        private static void Expand(ushort c, byte[] to, int at)
        {
            int r = (c >> 11) & 31, g = (c >> 5) & 63, b = c & 31;
            to[at] = (byte)((r << 3) | (r >> 2));
            to[at + 1] = (byte)((g << 2) | (g >> 4));
            to[at + 2] = (byte)((b << 3) | (b >> 2));
        }

        /// <summary>An 8-bit RGBA PNG; each row takes the filter (none / sub / up / average / Paeth) with the smallest sum.</summary>
        public static byte[] EncodePng(int width, int height, byte[] rgba)
        {
            int stride = width * 4;
            var filtered = new byte[(stride + 1) * height];
            var trial = new byte[stride];
            var best = new byte[stride];
            for (int y = 0; y < height; y++)
            {
                long bestSum = long.MaxValue;
                byte bestType = 0;
                for (byte type = 0; type < 5; type++)
                {
                    long sum = 0;
                    for (int x = 0; x < stride; x++)
                    {
                        int cur = rgba[y * stride + x];
                        int left = x >= 4 ? rgba[y * stride + x - 4] : 0;
                        int up = y > 0 ? rgba[(y - 1) * stride + x] : 0;
                        int upLeft = x >= 4 && y > 0 ? rgba[(y - 1) * stride + x - 4] : 0;
                        int pred = type switch
                        {
                            1 => left,
                            2 => up,
                            3 => (left + up) / 2,
                            4 => Paeth(left, up, upLeft),
                            _ => 0,
                        };
                        byte v = (byte)(cur - pred);
                        trial[x] = v;
                        sum += v < 128 ? v : 256 - v;
                    }
                    if (sum < bestSum)
                    {
                        bestSum = sum;
                        bestType = type;
                        Array.Copy(trial, best, stride);
                    }
                }
                filtered[y * (stride + 1)] = bestType;
                Array.Copy(best, 0, filtered, y * (stride + 1) + 1, stride);
            }

            using var png = new MemoryStream();
            png.Write(PngSignature);
            var header = new byte[13];
            BinaryPrimitives.WriteInt32BigEndian(header, width);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
            header[8] = 8;      // bit depth
            header[9] = 6;      // RGBA
            Chunk(png, "IHDR", header);
            using (var data = new MemoryStream())
            {
                using (var z = new ZLibStream(data, CompressionLevel.SmallestSize, true))
                    z.Write(filtered);
                Chunk(png, "IDAT", data.ToArray());
            }
            Chunk(png, "IEND", Array.Empty<byte>());
            return png.ToArray();
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        private static void Chunk(Stream to, string type, byte[] data)
        {
            Span<byte> word = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
            to.Write(word);
            byte[] typeBytes = Encoding.ASCII.GetBytes(type);
            to.Write(typeBytes);
            to.Write(data);
            uint crc = Crc32(Crc32(0xFFFFFFFF, typeBytes), data) ^ 0xFFFFFFFF;
            BinaryPrimitives.WriteUInt32BigEndian(word, crc);
            to.Write(word);
        }

        private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
        {
            uint c = (uint)n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            return c;
        }).ToArray();

        private static uint Crc32(uint crc, byte[] data)
        {
            foreach (byte b in data)
                crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }
    }
}
