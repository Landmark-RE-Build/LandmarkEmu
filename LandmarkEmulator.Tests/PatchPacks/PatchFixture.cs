using LandmarkEmulator.PatchPacks;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LandmarkEmulator.Tests.PatchPacks
{
    /// <summary>
    /// A made-up game for the patch tests: a "Steam 0.589" folder and a "0.695" folder of small synthetic files (never
    /// real game bytes), and the lists the checks use for it (Patching.UseLists), so the tests run anywhere, offline. The
    /// 0.695 build changes an asset pack in place (an edit), shifts a text file (a patch), changes the exe, adds a file
    /// and deletes one; a "January" variant has another exe and text file, as the real January 2017 build does.
    /// </summary>
    public sealed class PatchFixture : IDisposable
    {
        public string Root { get; }
        public string Steam => Path.Combine(Root, "steam");
        public string New => Path.Combine(Root, "new695");
        public string January => Path.Combine(Root, "january695");
        public string Out => Path.Combine(Root, "out");
        private readonly IDisposable lists;

        public const string Exe = "Landmark64.exe";
        public const string Asset = @"Resources\Assets\Assets_000.pack";
        public const string Text = @"Resources\Data\Rules.txt";
        public const string Same = @"Resources\Same.bin";
        public const string Gone = @"Old\Gone.txt";
        public const string Added = @"New\Added.dat";

        public Dictionary<string, byte[]> SteamFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, byte[]> NewFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, byte[]> JanuaryFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <param name="strongFor">Which 0.695 files get a SHA-256 in the strong list (null = all).</param>
        public PatchFixture(Func<string, bool> strongFor = null)
        {
            Root = Path.Combine(Path.GetTempPath(), "lmemu-test copies-" + Guid.NewGuid().ToString("N")[..10]);
            var random = new Random(1234);
            byte[] Bytes(int n) { var b = new byte[n]; random.NextBytes(b); return b; }
            byte[] ExeOf(string build, int seed)
            {
                var r = new Random(seed);
                var b = new byte[8192];
                r.NextBytes(b);
                Encoding.ASCII.GetBytes("MZ fake Landmark " + build + " ").CopyTo(b, 0);
                return b;
            }
            string RulesText(int from, bool extra)
                => string.Join("\n", Enumerable.Range(from, 600).Select(i => $"rule {i} value {i * 7 % 13}")) + (extra ? "\nnew rule" : "");

            byte[] asset = Bytes(64 * 1024);
            SteamFiles[Exe] = ExeOf(Patching.SteamBuild, 1);
            SteamFiles[Asset] = asset;
            SteamFiles[Text] = Encoding.ASCII.GetBytes(RulesText(0, false));
            SteamFiles[Same] = Bytes(5000);
            SteamFiles[Gone] = Encoding.ASCII.GetBytes("deleted in 0.695");
            SteamFiles["ClientConfig.ini"] = Encoding.ASCII.GetBytes("Server=127.0.0.1:20042\n");

            byte[] asset695 = (byte[])asset.Clone();
            for (int i = 1000; i < 1040; i++)
                asset695[i] ^= 0x5A;
            asset695 = asset695.Concat(Bytes(3000)).ToArray();
            NewFiles[Exe] = ExeOf(Patching.FinalBuild, 2);
            NewFiles[Asset] = asset695;
            NewFiles[Text] = Encoding.ASCII.GetBytes("header line\n" + RulesText(0, true));
            NewFiles[Same] = SteamFiles[Same];
            NewFiles[Added] = Bytes(2000);
            NewFiles["ClientConfig.ini"] = SteamFiles["ClientConfig.ini"];

            foreach (var (k, v) in NewFiles)
                JanuaryFiles[k] = v;
            JanuaryFiles[Exe] = ExeOf(Patching.FinalBuild, 3);
            JanuaryFiles[Text] = Encoding.ASCII.GetBytes("january header\n" + RulesText(0, true));

            WriteFolder(Steam, SteamFiles);
            WriteFolder(New, NewFiles);
            WriteFolder(January, JanuaryFiles);
            Directory.CreateDirectory(Out);

            var final = NewFiles.ToDictionary(f => f.Key, f => ((long)f.Value.Length, Crc(f.Value)), StringComparer.OrdinalIgnoreCase);
            var january = new Dictionary<string, (long, string)>(StringComparer.OrdinalIgnoreCase)
            {
                [Exe] = (JanuaryFiles[Exe].Length, Crc(JanuaryFiles[Exe])),
                [Text] = (JanuaryFiles[Text].Length, Crc(JanuaryFiles[Text]))
            };
            var strong = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (k, v) in NewFiles.Concat(JanuaryFiles.Where(j => january.ContainsKey(j.Key))))
                strong[Patching.StrongKey(k, v.Length, Crc(v))] = strongFor == null || strongFor(k) ? Sha256(v) : "";
            lists = Patching.UseLists(new Patching.Lists
            {
                Final = final,
                January = january,
                Steam = SteamFiles.ToDictionary(f => f.Key, f => ((long)f.Value.Length, Convert.ToHexString(SHA1.HashData(f.Value))), StringComparer.OrdinalIgnoreCase),
                Strong = strong,
                UpdateFiles = new List<(string, bool)> { (Exe, false), (Asset, false), (Text, false), (Added, true) }
            });
        }

        public static void WriteFolder(string folder, Dictionary<string, byte[]> files)
        {
            foreach (var (rel, data) in files)
            {
                string path = Path.Combine(folder, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, data);
            }
        }

        public static string Crc(byte[] data) => $"{System.IO.Hashing.Crc32.HashToUInt32(data):X8}";
        public static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

        /// <summary>A fresh copy of the Steam folder to install on.</summary>
        public string SteamCopy(string name = "game")
        {
            string to = Path.Combine(Root, name);
            WriteFolder(to, SteamFiles);
            return to;
        }

        /// <summary>The genuine update pack, made the way LaunchPad makes it (with or without a Steam copy).</summary>
        public string MakeGenuine(bool withSteam = true, string name = "update.lmpatch", string from = null, bool allowFebruary = false)
        {
            string output = Path.Combine(Out, name);
            Patching.MakeUpdate(withSteam ? Steam : null, new[] { from ?? New }, output, "Test update", allowFebruary: allowFebruary);
            return output;
        }

        /// <summary>
        /// Changes the last 4 bytes of <paramref name="data"/> so its CRC32 (zlib) becomes <paramref name="crc"/>: the
        /// classic CRC forge (a CRC is linear, so 4 chosen bytes reach any value). Size stays the same.
        /// </summary>
        public static byte[] ForgeCrc(byte[] data, string crc)
        {
            uint target = Convert.ToUInt32(crc, 16);
            uint[] table = new uint[256];
            var rev = new byte[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                table[i] = c;
                rev[c >> 24] = (byte)i;
            }
            byte[] result = (byte[])data.Clone();
            // The register after everything but the last 4 bytes.
            uint reg = ~System.IO.Hashing.Crc32.HashToUInt32(result.AsSpan(0, result.Length - 4));
            uint want = ~target;
            for (int k = 0; k < 4; k++)
            {
                byte j = rev[want >> 24];
                want = ((want ^ table[j]) << 8) | j;
            }
            BitConverter.GetBytes(want ^ reg).CopyTo(result, result.Length - 4);
            if (Crc(result) != crc.ToUpperInvariant())
                throw new InvalidOperationException("CRC forge failed");
            return result;
        }

        /// <summary>Writes a pack from a manifest and entries with LaunchPad's own writer.</summary>
        public static string WritePack(string path, Patching.Manifest m, Dictionary<string, byte[]> entries)
        {
            using (var w = new PatchFile.Writer(path))
            {
                foreach (var (name, data) in entries)
                    w.Add(name, data);
                w.Finish(JsonSerializer.Serialize(m));
            }
            return path;
        }

        /// <summary>An index entry as written raw (any numbers, for damaged / hostile packs).</summary>
        public sealed record RawEntry(string Name, byte[] Stored, long Size, string Sha256, long? Offset = null);

        /// <summary>Writes an .lmpatch by hand, with whatever index the test wants.</summary>
        public static string WriteRaw(string path, string manifest, IEnumerable<RawEntry> entries, byte[] storedIndex = null)
        {
            using var file = File.Create(path);
            file.Write(new byte[24]);
            var index = new List<object>();
            foreach (RawEntry e in entries)
            {
                long at = e.Offset ?? file.Position;
                if (e.Offset == null)
                    file.Write(e.Stored);
                index.Add(new { name = e.Name, offset = at, stored = e.Stored.LongLength, size = e.Size, sha256 = e.Sha256 });
            }
            storedIndex ??= Brotli(JsonSerializer.SerializeToUtf8Bytes(new { manifest, entries = index }));
            long indexAt = file.Position;
            file.Write(storedIndex);
            file.Position = 0;
            file.Write(Encoding.ASCII.GetBytes("LMPATCH1"));
            file.Write(BitConverter.GetBytes(indexAt));
            file.Write(BitConverter.GetBytes((long)storedIndex.Length));
            return path;
        }

        public static byte[] Brotli(byte[] data)
        {
            using var output = new MemoryStream();
            using (var b = new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: true))
                b.Write(data);
            return output.ToArray();
        }

        public static Patching.Progress Quiet => (_, _, _, _) => { };

        public void Dispose()
        {
            lists.Dispose();
            try { Directory.Delete(Root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>A server for <see cref="ServerPatches"/>: serves a local pack, counts what was asked for.</summary>
    public sealed class FakeSource : IPatchSource
    {
        private readonly string pack;
        private readonly byte[] manifest;
        public int Manifests, Downloads;
        public string Display => "test-server:5000";

        public FakeSource(string pack, byte[] manifest = null)
        {
            this.pack = pack;
            using var r = new PatchFile.Reader(pack);
            this.manifest = manifest ?? Encoding.UTF8.GetBytes(r.Manifest);
        }

        public byte[] ManifestBytes => manifest;

        public Task<byte[]> Manifest(string id, long maxBytes, CancellationToken cancel)
        {
            Manifests++;
            return Task.FromResult(manifest);
        }

        public Task Download(string id, string partFile, long maxBytes, Action<long> read, CancellationToken cancel)
        {
            Downloads++;
            byte[] data = File.ReadAllBytes(pack);
            if (data.Length > maxBytes)
                throw new InvalidDataException("more than announced");
            File.WriteAllBytes(partFile, data);
            read(data.Length);
            return Task.CompletedTask;
        }

        /// <summary>What /info would list for the pack (as PatchHost describes a verified pack).</summary>
        public ServerPatches.Listed Listed(string packSha256 = null)
        {
            byte[] file = File.ReadAllBytes(pack);
            return new ServerPatches.Listed
            {
                Id = "update", Name = "Test update", Version = Patching.FinalBuild, Sha256 = PatchFixture.Sha256(manifest),
                PackBytes = file.Length, PackSha256 = packSha256 ?? PatchFixture.Sha256(file)
            };
        }
    }
}
