using LandmarkEmulator.PatchPacks;
using LandmarkEmulator.WebAPI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Xunit;
using static LandmarkEmulator.Tests.PatchPacks.PatchFixture;

namespace LandmarkEmulator.Tests.PatchPacks
{
    /// <summary>
    /// The patch-pack rules shared by LaunchPad and the server's patch host (LandmarkEmulator.PatchPacks): a genuine pack
    /// passes everywhere; anything a hostile pack maker or server owner could try (a file with the right size and CRC32
    /// but other bytes, paths out of the game folder, extra or mixed files, bombs, a pack swapped after the server checked
    /// it, a download that isn't what /info said) is refused before a byte of the game changes. Synthetic files only.
    /// </summary>
    public sealed class PatchSecurityTests : IDisposable
    {
        private readonly PatchFixture fx = new();
        private readonly List<string> log = new();

        public void Dispose() => fx.Dispose();

        private static Patching.Manifest ManifestOf(string pack)
        {
            using var p = new Patching.Pack(pack);
            return p.Manifest;
        }

        private static string Hash(string file) => Sha256(File.ReadAllBytes(file));

        private PatchLibrary Library(string folder)
        {
            var library = new PatchLibrary(folder, null, log.Add);
            library.Scan();
            return library;
        }

        // ---- a genuine pack ---------------------------------------------------------------------------------------

        [Fact]
        public void GenuinePack_PassesTheServerAndInstallsAndUndoes()
        {
            string pack = fx.MakeGenuine();
            using (var p = new Patching.Pack(pack))
            {
                Assert.Empty(Patching.CheckUpdate(p.Manifest));
                Assert.Contains(p.Manifest.Files, f => f.How == "edit");
                Assert.Contains(p.Manifest.Files, f => f.How == "patch");
                PackCheck.Result r = PackCheck.Verify(p, fx.Steam);          // replayed on the Steam copy
                Assert.True(r.Replayed >= 2, $"replayed {r.Replayed}");
                Assert.Equal(0, PackCheck.Verify(p, null).Replayed);         // the server without a Steam copy
            }

            // The server: verified, listed with the file's SHA-256, served.
            string folder = Path.Combine(fx.Root, "server-patches");
            Directory.CreateDirectory(folder);
            File.Copy(pack, Path.Combine(folder, "01-update.lmpatch"));
            using PatchLibrary library = Library(folder);
            PatchLibrary.Pack served = Assert.Single(library.Packs);
            Assert.Equal(Hash(pack), served.FileSha256);
            using (FileStream s = library.OpenForServing("01-update", out _))
                Assert.NotNull(s);

            // The player: installed on a verified Steam copy; every file is then the 0.695 file; undo gives Steam back.
            string game = fx.SteamCopy();
            var store = new GameStore(game);
            using (var p = new Patching.Pack(pack))
                store.Install(p, "", "", Quiet, CancellationToken.None);
            foreach (var (rel, data) in fx.NewFiles)
                Assert.Equal(Sha256(data), Hash(Path.Combine(game, rel)));
            Assert.False(File.Exists(Path.Combine(game, Gone)));
            Assert.Empty(store.Validate(Quiet, CancellationToken.None).Problems);
            Assert.Null(store.CheckExe());
            store.UndoLast(Quiet, CancellationToken.None);
            foreach (var (rel, data) in fx.SteamFiles)
                Assert.Equal(Sha256(data), Hash(Path.Combine(game, rel)));
            Assert.Empty(new GameStore(game).VerifySteam(Quiet, CancellationToken.None));
        }

        [Fact]
        public void PackWithoutSteamCopy_IsAllWholeFiles_AndInstalls()
        {
            string pack = fx.MakeGenuine(withSteam: false);
            Assert.True(ManifestOf(pack).Files.All(f => f.How == "whole"));
            string game = fx.SteamCopy();
            using var p = new Patching.Pack(pack);
            new GameStore(game).Install(p, "", "", Quiet, CancellationToken.None);
            Assert.Equal(Sha256(fx.NewFiles[Exe]), Hash(Path.Combine(game, Exe)));
        }

        [Fact]
        public void MakingFromAChangedFolder_IsRefused_AndLeavesNothing()
        {
            // The player's "0.695 folder" has an exe with the right size and CRC32 but other bytes.
            File.WriteAllBytes(Path.Combine(fx.New, Exe), ForgeCrc(Evil(fx.NewFiles[Exe]), Crc(fx.NewFiles[Exe])));
            Patching.FolderCheck check = Patching.CheckFolder(fx.New);
            Assert.Null(check.Build);
            Assert.Contains(check.Problems, p => p.Path == Exe && p.Issue.Contains("SHA-256"));
            string output = Path.Combine(fx.Out, "bad.lmpatch");
            Assert.Throws<InvalidDataException>(() => Patching.MakeUpdate(fx.Steam, new[] { fx.New }, output, "x"));
            Assert.False(File.Exists(output));
            Assert.Empty(Directory.GetFiles(fx.Out));
        }

        [Fact]
        public void FolderCheck_NamesTheBuild()
        {
            Assert.Equal("final", Patching.CheckFolder(fx.New).Build?.Id);
            Assert.Equal("january-2017", Patching.CheckFolder(fx.January).Build?.Id);
            // Files the game writes as it runs (logs, caches, settings) are in no list and don't matter.
            File.WriteAllText(Path.Combine(fx.New, "ui.log"), "log");
            File.WriteAllText(Path.Combine(fx.New, "UserOptions.ini"), "x");
            Directory.CreateDirectory(Path.Combine(fx.New, "Logs"));
            File.WriteAllText(Path.Combine(fx.New, "Logs", "a.txt"), "x");
            File.WriteAllText(Path.Combine(fx.New, "ClientConfig.ini"), "Server=elsewhere");
            Assert.Equal("final", Patching.CheckFolder(fx.New).Build?.Id);
        }

        // ---- a forged file: right size and CRC32, wrong bytes ------------------------------------------------------

        private static byte[] Evil(byte[] genuine)
        {
            byte[] evil = (byte[])genuine.Clone();
            Encoding.ASCII.GetBytes("HARMFUL CODE HERE").CopyTo(evil, 200);
            return evil;
        }

        /// <summary>The genuine pack's manifest, with the exe stored whole as <paramref name="exe"/>.</summary>
        private string PackWithExe(byte[] exe, string name = "forged.lmpatch")
        {
            string genuine = fx.MakeGenuine();
            Patching.Manifest m = ManifestOf(genuine);
            var entries = new Dictionary<string, byte[]>();
            using (var p = new Patching.Pack(genuine))
                foreach (Patching.PackFile f in m.Files)
                    entries[f.Entry] = p.Read(f.Entry);
            Patching.PackFile e = m.Files.Single(f => f.Path == Exe);
            entries.Remove(e.Entry);
            (e.How, e.FromSize, e.FromCrc32, e.Sha256) = ("whole", 0, "", "");
            entries[e.Entry] = exe;
            return WritePack(Path.Combine(fx.Out, name), m, entries);
        }

        [Fact]
        public void CrcForgedExe_PassesTheOldCheck_ButIsRefusedEverywhere()
        {
            byte[] genuine = fx.NewFiles[Exe];
            byte[] forged = ForgeCrc(Evil(genuine), Crc(genuine));
            Assert.Equal(genuine.Length, forged.Length);
            Assert.Equal(Crc(genuine), Crc(forged));
            Assert.NotEqual(Sha256(genuine), Sha256(forged));
            string pack = PackWithExe(forged);

            // The list alone looks right (size + CRC32: all the old check looked at) ...
            Assert.Empty(Patching.CheckUpdate(ManifestOf(pack)));
            // ... but the entry is not the genuine file.
            using (var p = new Patching.Pack(pack))
                Assert.Contains("not the genuine file", Assert.Throws<InvalidDataException>(() => PackCheck.Verify(p, null)).Message);

            // The server refuses it and moves it aside; it is never served.
            string folder = Path.Combine(fx.Root, "server-patches");
            Directory.CreateDirectory(folder);
            File.Copy(pack, Path.Combine(folder, "evil.lmpatch"));
            using (PatchLibrary library = Library(folder))
            {
                Assert.Empty(library.Packs);
                Assert.Null(library.OpenForServing("evil", out _));
                Assert.True(File.Exists(Path.Combine(folder, "rejected", "evil.lmpatch")));
                Assert.True(File.Exists(Path.Combine(folder, "rejected", "evil.lmpatch.reason.txt")));
                Assert.Contains(log, l => l.Contains("REFUSED"));
            }

            // The player: refused before anything changes.
            string game = fx.SteamCopy();
            using (var p = new Patching.Pack(pack))
                Assert.Throws<InvalidDataException>(() => new GameStore(game).Install(p, "", "", Quiet, CancellationToken.None));
            Assert.Equal(Sha256(fx.SteamFiles[Exe]), Hash(Path.Combine(game, Exe)));
            Assert.Empty(new GameStore(game).Stack);
            Assert.False(Directory.Exists(Path.Combine(game, GameStore.FolderName, "backups"))
                && Directory.GetFiles(Path.Combine(game, GameStore.FolderName, "backups")).Length > 0);
        }

        [Fact]
        public void CrcForgedExe_IsNotStartedByPlay()
        {
            string game = fx.SteamCopy();
            Assert.Null(new GameStore(game).CheckExe());
            File.WriteAllBytes(Path.Combine(game, Exe), ForgeCrc(Evil(fx.NewFiles[Exe]), Crc(fx.NewFiles[Exe])));
            Assert.NotNull(new GameStore(game).CheckExe());
        }

        [Fact]
        public void InPlaceEdit_WithAForgedResult_NeverWrites()
        {
            // An edit that turns the Steam asset into bytes with the 0.695 file's size and CRC32 but other content.
            byte[] genuine = fx.NewFiles[Asset];
            byte[] forged = ForgeCrc(Evil(genuine), Crc(genuine));
            string good = fx.MakeGenuine();
            Patching.Manifest m = ManifestOf(good);
            var entries = new Dictionary<string, byte[]>();
            using (var p = new Patching.Pack(good))
                foreach (Patching.PackFile f in m.Files)
                    entries[f.Entry] = p.Read(f.Entry);
            Patching.PackFile asset = m.Files.Single(f => f.Path == Asset);
            Assert.Equal("edit", asset.How);
            entries[asset.Entry] = InPlaceEdit.Write(InPlaceEdit.Create(fx.SteamFiles[Asset], forged));
            string pack = WritePack(Path.Combine(fx.Out, "forged-edit.lmpatch"), m, entries);

            string game = fx.SteamCopy();
            string before = Hash(Path.Combine(game, Asset));
            using (var p = new Patching.Pack(pack))
            {
                // The server can't tell without a Steam copy (the header is right) ...
                PackCheck.Verify(p, null);
                // ... with one it can, and LaunchPad always can: it works the result out before writing.
                Assert.Throws<InvalidDataException>(() => PackCheck.Verify(p, fx.Steam));
                var e = Assert.Throws<InvalidDataException>(() => new GameStore(game).Install(p, "", "", Quiet, CancellationToken.None));
                Assert.Contains("doesn't make the genuine file", e.Message);
            }
            Assert.Equal(before, Hash(Path.Combine(game, Asset)));
            Assert.Equal(new FileInfo(Path.Combine(game, Asset)).Length, fx.SteamFiles[Asset].Length);
        }

        [Fact]
        public void EditResultHash_IsWorkedOutWithoutWriting()
        {
            byte[] old = fx.SteamFiles[Asset], now = fx.NewFiles[Asset];
            InPlaceEdit.Edit e = InPlaceEdit.Create(old, now);
            using var s = new MemoryStream(old, writable: false);
            Assert.Equal(Sha256(now), InPlaceEdit.ResultSha256(e, s));
            // A shrinking edit too.
            byte[] shorter = now[..40000];
            Assert.Equal(Sha256(shorter), InPlaceEdit.ResultSha256(InPlaceEdit.Create(old, shorter), new MemoryStream(old, false)));
        }

        // ---- paths and lists ------------------------------------------------------------------------------------

        [Theory]
        [InlineData(@"..\Landmark64.exe")]
        [InlineData(@"Resources\..\..\evil.dll")]
        [InlineData(@"C:\Windows\evil.dll")]
        [InlineData(@"\Windows\evil.dll")]
        [InlineData(@"\\server\share\evil.dll")]
        [InlineData("Landmark64.exe:hidden")]
        [InlineData("CON")]
        [InlineData(@"Resources\nul.txt")]
        [InlineData(@"Resources\COM1.dll")]
        [InlineData("LaunchPad.exe")]
        [InlineData(@"LaunchPad.libs\libcef.dll")]
        [InlineData(@"LaunchPadEmu\installed.json")]
        [InlineData("launchpademu/packs/x.json")]
        [InlineData("Landmark64.exe.")]
        [InlineData("Landmark64.exe ")]
        [InlineData("a*b.dll")]
        [InlineData(@"Resources\.\x.dll")]
        public void UnsafePaths_AreRefused(string path)
        {
            var write = new Patching.Manifest { Format = 2, Files = { new Patching.PackFile { Path = path, Size = 1, Crc32 = "00000000" } } };
            Assert.Throws<InvalidDataException>(() => write.Checked());
            var delete = new Patching.Manifest { Format = 2, Delete = { path } };
            Assert.Throws<InvalidDataException>(() => delete.Checked());
        }

        [Fact]
        public void SafePaths_AreKept()
        {
            Assert.Equal(@"Resources\Assets\Assets_000.pack", Patching.SafeRelative("Resources/Assets/Assets_000.pack"));
            Assert.Equal("Landmark64.exe", Patching.SafeRelative("Landmark64.exe"));
        }

        private Patching.Manifest Genuine() => ManifestOf(fx.MakeGenuine());

        [Fact]
        public void ExtraFile_IsRefused()
        {
            Patching.Manifest m = Genuine();
            m.Files.Add(new Patching.PackFile { Path = "dinput8.dll", Size = 100, Crc32 = "12345678" });
            Assert.Contains(Patching.CheckUpdate(m), p => p.Contains("not files of the"));
        }

        [Fact]
        public void DeletingAFileTheBuildHasOrANonSteamFile_IsRefused()
        {
            Patching.Manifest m = Genuine();
            m.Delete.Add(Same);                          // 0.695 has it
            Assert.Contains(Patching.CheckUpdate(m), p => p.Contains("deletes"));
            m = Genuine();
            m.Delete.Add(@"Docs\Readme.txt");            // not a Steam file at all
            Assert.Contains(Patching.CheckUpdate(m), p => p.Contains("deletes"));
            m = Genuine();
            m.Delete.Add("ClientConfig.ini");            // a settings file
            Assert.Contains(Patching.CheckUpdate(m), p => p.Contains("deletes"));
        }

        [Fact]
        public void DuplicatePaths_AreRefused()
        {
            Patching.Manifest m = Genuine();
            m.Files.Add(new Patching.PackFile { Path = m.Files[0].Path.ToUpperInvariant(), Size = m.Files[0].Size, Crc32 = m.Files[0].Crc32 });
            Assert.Throws<InvalidDataException>(() => m.Checked());
            m = Genuine();
            m.Delete.Add(m.Files[0].Path);
            Assert.Throws<InvalidDataException>(() => m.Checked());
        }

        [Fact]
        public void MixingTheTwoBuilds_IsRefused()
        {
            // The final build's exe with the January build's text file: each file is genuine, the mix is not a build.
            string pack = fx.MakeGenuine(withSteam: false);
            Patching.Manifest m = ManifestOf(pack);
            Patching.PackFile text = m.Files.Single(f => f.Path == Text);
            (text.Size, text.Crc32, text.Sha256) = (fx.JanuaryFiles[Text].Length, Crc(fx.JanuaryFiles[Text]), "");
            Assert.NotEmpty(Patching.CheckUpdate(m));
            // Each build on its own is fine.
            Assert.Empty(Patching.CheckUpdate(ManifestOf(fx.MakeGenuine(false, "jan.lmpatch", fx.January))));
        }

        [Fact]
        public void SettingsFiles_AreNotCarried()
        {
            Patching.Manifest m = Genuine();
            m.Files.Add(new Patching.PackFile { Path = "ClientConfig.ini", Size = fx.NewFiles["ClientConfig.ini"].Length, Crc32 = Crc(fx.NewFiles["ClientConfig.ini"]) });
            Assert.Contains(Patching.CheckUpdate(m), p => p.Contains("settings files"));
        }

        [Fact]
        public void IncompleteUpdate_IsRefused()
        {
            Patching.Manifest m = Genuine();
            m.Files.RemoveAll(f => f.Path == Added);
            Assert.Contains(Patching.CheckUpdate(m), p => p.Contains("incomplete"));
        }

        [Fact]
        public void AFileWithNoStrongHash_IsRefused()
        {
            // As the real final build's exe today: in Daybreak's list, but no genuine copy's SHA-256 is known.
            Patching.Manifest m = ManifestOf(fx.MakeGenuine(withSteam: false));
            using var noExeHash = new PatchFixture(path => path != Exe);       // the same made-up files, no SHA-256 for the exe
            Assert.Contains(Patching.CheckUpdate(m), p => p.Contains("can't be verified"));
            // Nor can such a pack be made.
            Assert.Throws<InvalidDataException>(() => noExeHash.MakeGenuine(withSteam: false));
        }

        [Fact]
        public void AClaimedSha256_MustBeTheGenuineOne()
        {
            Patching.Manifest m = Genuine();
            m.Files[0].Sha256 = new string('A', 64);
            Assert.Contains(Patching.CheckUpdate(m), p => p.Contains("claim a SHA-256"));
        }

        // ---- damaged and hostile pack files -------------------------------------------------------------------------

        private string ManifestText() => JsonSerializer.Serialize(Genuine());

        [Fact]
        public void BrotliBomb_StopsAtTheDeclaredSize()
        {
            byte[] zeros = new byte[16 << 20];
            byte[] stored = Brotli(zeros);
            Assert.True(stored.Length < 64 * 1024);
            string pack = WriteRaw(Path.Combine(fx.Out, "bomb.lmpatch"), ManifestText(),
                new[] { new RawEntry("bomb", stored, 1000, Sha256(zeros[..1000])) });
            using var reader = new PatchFile.Reader(pack);
            Assert.Throws<InvalidDataException>(() => reader.Read("bomb"));
        }

        [Fact]
        public void EntryClaimingAHugeSize_IsRefusedUnread()
        {
            string pack = WriteRaw(Path.Combine(fx.Out, "huge.lmpatch"), ManifestText(),
                new[] { new RawEntry("x", Brotli(new byte[10]), 8L << 30, new string('0', 64)) });
            Assert.Throws<InvalidDataException>(() => new PatchFile.Reader(pack));
        }

        [Fact]
        public void WholeEntry_LargerThanItsFile_IsRefusedByTheIndexCheck()
        {
            Patching.Manifest m = Genuine();
            Patching.PackFile f = m.Files.Single(x => x.Path == Added);
            byte[] bigger = fx.NewFiles[Added].Concat(new byte[100]).ToArray();
            var entries = m.Files.Where(x => x.Path != Added).ToDictionary(x => x.Entry, x => new byte[1]);
            entries[f.Entry] = bigger;
            string pack = WritePack(Path.Combine(fx.Out, "bigger.lmpatch"), m, entries);
            using var p = new Patching.Pack(pack);
            Assert.Contains(PackCheck.CheckIndex(p, out _), x => x.Contains("unpacks to"));
        }

        [Fact]
        public void IndexBomb_IsRefused()
        {
            byte[] index = Brotli(new byte[(64 << 20) + 1000]);
            string pack = WriteRaw(Path.Combine(fx.Out, "indexbomb.lmpatch"), "", Array.Empty<RawEntry>(), index);
            Assert.Throws<InvalidDataException>(() => new PatchFile.Reader(pack));
        }

        [Fact]
        public void DuplicateEntries_AreRefused()
        {
            byte[] data = Encoding.ASCII.GetBytes("hello");
            string pack = WriteRaw(Path.Combine(fx.Out, "dup.lmpatch"), ManifestText(), new[]
            {
                new RawEntry("a.txt", Brotli(data), data.Length, Sha256(data)),
                new RawEntry("A.TXT", Brotli(data), data.Length, Sha256(data))
            });
            Assert.Contains("twice", Assert.Throws<InvalidDataException>(() => new PatchFile.Reader(pack)).Message);
        }

        [Fact]
        public void OverlappingAndOutsideEntries_AreRefused()
        {
            byte[] data = Encoding.ASCII.GetBytes("hello world, hello world");
            byte[] stored = Brotli(data);
            string overlap = WriteRaw(Path.Combine(fx.Out, "overlap.lmpatch"), ManifestText(), new[]
            {
                new RawEntry("a", stored, data.Length, Sha256(data)),
                new RawEntry("b", stored, data.Length, Sha256(data)),
                new RawEntry("c", stored, data.Length, Sha256(data), Offset: 25)
            });
            Assert.Contains("overlap", Assert.Throws<InvalidDataException>(() => new PatchFile.Reader(overlap)).Message);
            string outside = WriteRaw(Path.Combine(fx.Out, "outside.lmpatch"), ManifestText(), new[]
            {
                new RawEntry("a", stored, data.Length, Sha256(data), Offset: 1 << 20)
            });
            Assert.Throws<InvalidDataException>(() => new PatchFile.Reader(outside));
        }

        [Fact]
        public void UnusedEntries_AreRefused()
        {
            string genuine = fx.MakeGenuine();
            Patching.Manifest m = ManifestOf(genuine);
            var entries = new Dictionary<string, byte[]>();
            using (var p = new Patching.Pack(genuine))
                foreach (Patching.PackFile f in m.Files)
                    entries[f.Entry] = p.Read(f.Entry);
            entries["extra/payload.exe"] = Encoding.ASCII.GetBytes("MZ");
            string pack = WritePack(Path.Combine(fx.Out, "extra.lmpatch"), m, entries);
            using var p2 = new Patching.Pack(pack);
            Assert.Contains(PackCheck.CheckIndex(p2, out _), x => x.Contains("doesn't use"));
        }

        [Fact]
        public void DeltaThatCopiesForever_StopsAtTheNewSize()
        {
            // Header: old 4 bytes, new size 10; then a copy op of the whole old file 1000 times.
            byte[] old = Encoding.ASCII.GetBytes("abcd");
            var d = new MemoryStream();
            var w = new BinaryWriter(d);
            w.Write("LMD1"u8.ToArray());
            w.Write((ulong)old.Length);
            w.Write(System.IO.Hashing.Crc32.HashToUInt32(old));
            w.Write((ulong)10);
            w.Write(0u);
            for (int i = 0; i < 1000; i++)
            {
                w.Write((byte)'C');
                w.Write((byte)(i == 0 ? 0 : 7));   // zigzag(-4): back to the start
                w.Write((byte)4);
            }
            var output = new MemoryStream();
            Assert.Throws<InvalidDataException>(() => Delta.Apply(old, d.ToArray(), output));
            Assert.True(output.Length <= 10, $"wrote {output.Length}");
        }

        // ---- the server: a pack changed after it was checked ---------------------------------------------------------

        [Fact]
        public void PackTamperedAfterTheServerChecked_IsNotServed()
        {
            string folder = Path.Combine(fx.Root, "server-patches");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "update.lmpatch");
            File.Copy(fx.MakeGenuine(), file);
            using PatchLibrary library = Library(folder);
            Assert.Single(library.Packs);

            // The owner (or anyone with the folder) swaps bytes in it: same size, new write time.
            byte[] bytes = File.ReadAllBytes(file);
            bytes[30] ^= 0xFF;
            File.WriteAllBytes(file, bytes);
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(1));
            Assert.Null(library.OpenForServing("update", out _));
            Assert.Empty(library.Packs);
            // Checked again: refused, moved aside.
            library.Scan();
            Assert.Empty(library.Packs);
            Assert.False(File.Exists(file));
        }

        [Fact]
        public void PackWithABadName_IsRefused()
        {
            string folder = Path.Combine(fx.Root, "server-patches");
            Directory.CreateDirectory(folder);
            File.Copy(fx.MakeGenuine(), Path.Combine(folder, "bad name!.lmpatch"));
            using PatchLibrary library = Library(folder);
            Assert.Empty(library.Packs);
        }

        // ---- the player: downloading from a server ------------------------------------------------------------------

        [Fact]
        public void ServerDownload_Genuine_IsCheckedThenInstalled()
        {
            string pack = fx.MakeGenuine();
            var source = new FakeSource(pack);
            ServerPatches.Listed listed = source.Listed();
            string game = fx.SteamCopy();
            var store = new GameStore(game);
            Assert.Same(listed, ServerPatches.Needed(store, new List<ServerPatches.Listed> { listed }));
            InstalledPack installed = ServerPatches.Install(store, source, "Test", listed, listed.Sha256, Quiet, CancellationToken.None).Result;
            Assert.Equal(source.Display, installed.Source);
            Assert.Equal(1, source.Downloads);
            Assert.Equal(Sha256(fx.NewFiles[Exe]), Hash(Path.Combine(game, Exe)));
            // Nothing more needed; undo works for a server's pack too, and the download is kept for later.
            Assert.Null(ServerPatches.Needed(store, new List<ServerPatches.Listed> { listed }));
            store.UndoLast(Quiet, CancellationToken.None);
            Assert.Equal(Sha256(fx.SteamFiles[Exe]), Hash(Path.Combine(game, Exe)));
            Assert.True(File.Exists(Path.Combine(store.DownloadFolder, listed.Sha256 + PatchFile.Extension)));
        }

        [Fact]
        public void ServerDownload_ThatIsntTheAdvertisedFile_IsRefusedAndDeleted()
        {
            string pack = fx.MakeGenuine();
            var source = new FakeSource(pack);
            ServerPatches.Listed listed = source.Listed(packSha256: new string('B', 64));
            string game = fx.SteamCopy();
            var store = new GameStore(game);
            var e = Assert.Throws<AggregateException>(() =>
                ServerPatches.Install(store, source, "Test", listed, listed.Sha256, Quiet, CancellationToken.None).Wait());
            Assert.True(e.InnerException is InvalidDataException, e.InnerException?.ToString());
            Assert.Empty(Directory.Exists(store.DownloadFolder) ? Directory.GetFiles(store.DownloadFolder) : Array.Empty<string>());
            Assert.Empty(store.Stack);
            Assert.Equal(Sha256(fx.SteamFiles[Exe]), Hash(Path.Combine(game, Exe)));
        }

        [Fact]
        public void ServerDownload_ListIsCheckedBeforeTheBody()
        {
            // A server whose pack.json is the forged-exe pack's list with a wrong exe (another CRC32): refused unread.
            string pack = fx.MakeGenuine();
            Patching.Manifest m = ManifestOf(pack);
            m.Files.Single(f => f.Path == Exe).Crc32 = "DEADBEEF";
            byte[] manifest = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(m));
            var source = new FakeSource(pack, manifest);
            ServerPatches.Listed listed = source.Listed();
            string game = fx.SteamCopy();
            var e = Assert.Throws<AggregateException>(() =>
                ServerPatches.Install(new GameStore(game), source, "Test", listed, listed.Sha256, Quiet, CancellationToken.None).Wait());
            Assert.Contains("Nothing was downloaded", e.InnerException.Message);
            Assert.Equal(1, source.Manifests);
            Assert.Equal(0, source.Downloads);

            // Also: a list that isn't the one /info announced.
            var other = new FakeSource(pack);
            ServerPatches.Listed wrong = other.Listed();
            wrong.Sha256 = new string('C', 64);
            Assert.Throws<AggregateException>(() =>
                ServerPatches.Check(other, wrong, CancellationToken.None).Wait());
            Assert.Equal(0, other.Downloads);
        }

        [Fact]
        public void ServerDownload_OfAForgedPack_IsRefusedAfterTheDownload_AndDeleted()
        {
            byte[] forged = ForgeCrc(Evil(fx.NewFiles[Exe]), Crc(fx.NewFiles[Exe]));
            string pack = PackWithExe(forged);
            var source = new FakeSource(pack);
            ServerPatches.Listed listed = source.Listed();
            string game = fx.SteamCopy();
            var store = new GameStore(game);
            var e = Assert.Throws<AggregateException>(() =>
                ServerPatches.Install(store, source, "Test", listed, listed.Sha256, Quiet, CancellationToken.None).Wait());
            Assert.Contains("refused", e.InnerException.Message);
            Assert.Empty(Directory.GetFiles(store.DownloadFolder));
            Assert.Equal(Sha256(fx.SteamFiles[Exe]), Hash(Path.Combine(game, Exe)));
        }

        [Fact]
        public void ServerDownload_NeedsTheAgreedList()
        {
            var source = new FakeSource(fx.MakeGenuine());
            ServerPatches.Listed listed = source.Listed();
            Assert.Throws<AggregateException>(() =>
                ServerPatches.Install(new GameStore(fx.SteamCopy()), source, "Test", listed, new string('D', 64), Quiet, CancellationToken.None).Wait());
            Assert.Equal(0, source.Manifests);
            Assert.Equal(0, source.Downloads);
        }

        // ---- junctions inside the game folder -----------------------------------------------------------------------

        [Fact]
        public void Junctions_InsideTheGameFolder_AreRefused()
        {
            string game = fx.SteamCopy();
            string elsewhere = Path.Combine(fx.Root, "elsewhere");
            Directory.CreateDirectory(elsewhere);
            string link = Path.Combine(game, "New");
            if (OperatingSystem.IsWindows())
            {
                var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{elsewhere}\"")
                    { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });
                p.WaitForExit();
            }
            else
                Directory.CreateSymbolicLink(link, elsewhere);
            Assert.True((new DirectoryInfo(link).Attributes & FileAttributes.ReparsePoint) != 0, "the junction was made");
            Assert.Throws<InvalidOperationException>(() => GameFolder.RequirePlainPath(game, Added));
            using var pack = new Patching.Pack(fx.MakeGenuine());
            Assert.Throws<InvalidOperationException>(() => new GameStore(game).Install(pack, "", "", Quiet, CancellationToken.None));
            Assert.Empty(Directory.GetFiles(elsewhere));
            Directory.Delete(link);
        }
    }
}
