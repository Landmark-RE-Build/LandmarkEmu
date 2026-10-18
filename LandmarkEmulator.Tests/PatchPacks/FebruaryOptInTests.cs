using LandmarkEmulator.PatchPacks;
using LandmarkEmulator.WebAPI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Xunit;
using static LandmarkEmulator.Tests.PatchPacks.PatchFixture;

namespace LandmarkEmulator.Tests.PatchPacks
{
    /// <summary>
    /// The player's February opt-in (LaunchPad Settings: "Allow the February 2017 build's three files without a strong
    /// fingerprint", Patching.WeakAllowed): the made-up game's exe stands for the real final build's exe, which the strong
    /// list has no SHA-256 for. Off: refused. On, for a pack the player chose: installed on size + CRC32 with a warning,
    /// for those files only. A server's pack: always refused, setting or not.
    /// </summary>
    public sealed class FebruaryOptInTests : IDisposable
    {
        // The final build's exe without a SHA-256, as for the real final build today.
        private readonly PatchFixture fx = new(path => path != Exe);

        public void Dispose() => fx.Dispose();

        private static Patching.Manifest ManifestOf(string pack)
        {
            using var p = new Patching.Pack(pack);
            return p.Manifest;
        }

        [Fact]
        public void Off_IsRefused()
        {
            Assert.Throws<InvalidDataException>(() => fx.MakeGenuine());
            string pack = fx.MakeGenuine(allowFebruary: true);
            Assert.Contains(Patching.CheckUpdate(ManifestOf(pack)), p => p.Contains("can't be verified"));
            Assert.False(Patching.Describe(pack, fx.Steam).Verified);
            string game = fx.SteamCopy();
            using var p2 = new Patching.Pack(pack);
            Assert.Throws<InvalidOperationException>(() => new GameStore(game).Install(p2, "", "", Quiet, CancellationToken.None));
            Assert.Equal(Sha256(fx.SteamFiles[Exe]), Sha256(File.ReadAllBytes(Path.Combine(game, Exe))));
        }

        [Fact]
        public void OnForALocalPack_InstallsWithAWarning_ForThoseFilesOnly()
        {
            Patching.FolderCheck folder = Patching.CheckFolder(fx.New, allowFebruary: true);
            Assert.Equal("final", folder.Build?.Id);
            Assert.Equal(new[] { Exe }, folder.Weak.ToArray());
            string pack = fx.MakeGenuine(allowFebruary: true);
            Patching.PackInfo info = Patching.Describe(pack, fx.Steam, allowFebruary: true);
            Assert.True(info.Verified, string.Join("; ", info.Problems));
            Assert.Equal(new[] { Exe }, info.Weak.ToArray());
            using (var p = new Patching.Pack(pack))
                PackCheck.Verify(p, fx.Steam, allowFebruary: true);

            string game = fx.SteamCopy();
            var store = new GameStore(game);
            InstalledPack installed;
            using (var p = new Patching.Pack(pack))
                installed = store.Install(p, "", "", Quiet, CancellationToken.None, allowFebruary: true);
            Assert.Equal(new[] { Exe }, installed.WeakFiles.ToArray());
            Assert.Equal(Sha256(fx.NewFiles[Exe]), Sha256(File.ReadAllBytes(Path.Combine(game, Exe))));
            Assert.Empty(store.Validate(Quiet, CancellationToken.None).Problems);
            Assert.Null(store.CheckExe(allowFebruary: true));
            Assert.NotNull(store.CheckExe(allowFebruary: false));       // switched off later: not started
            store.UndoLast(Quiet, CancellationToken.None);
        }

        [Fact]
        public void On_StillHoldsEveryOtherFileToItsSha256()
        {
            // A forged asset (right size and CRC32) in a pack that also has the exe: refused even with the setting on.
            string good = fx.MakeGenuine(allowFebruary: true);
            Patching.Manifest m = ManifestOf(good);
            var entries = new Dictionary<string, byte[]>();
            using (var p = new Patching.Pack(good))
                foreach (Patching.PackFile f in m.Files)
                    entries[f.Entry] = p.Read(f.Entry);
            byte[] genuine = fx.NewFiles[Asset];
            byte[] evil = (byte[])genuine.Clone();
            Encoding.ASCII.GetBytes("HARMFUL").CopyTo(evil, 100);
            byte[] forged = ForgeCrc(evil, Crc(genuine));
            Patching.PackFile asset = m.Files.Single(f => f.Path == Asset);
            entries[asset.Entry] = InPlaceEdit.Write(InPlaceEdit.Create(fx.SteamFiles[Asset], forged));
            string pack = WritePack(Path.Combine(fx.Out, "forged.lmpatch"), m, entries);
            string game = fx.SteamCopy();
            using var p2 = new Patching.Pack(pack);
            Assert.Throws<InvalidDataException>(() => new GameStore(game).Install(p2, "", "", Quiet, CancellationToken.None, allowFebruary: true));
            Assert.Equal(Sha256(fx.SteamFiles[Asset]), Sha256(File.ReadAllBytes(Path.Combine(game, Asset))));
        }

        [Fact]
        public void On_NeverCoversAnotherPath()
        {
            // A file outside the three February paths without a SHA-256: refused even with the setting on.
            using var noTextHash = new PatchFixture(path => path != Text);
            Assert.Throws<InvalidDataException>(() => noTextHash.MakeGenuine(allowFebruary: true));
            Assert.Null(Patching.CheckFolder(noTextHash.New, allowFebruary: true).Build);
        }

        [Fact]
        public void AServersPack_IsAlwaysRefused()
        {
            string pack = fx.MakeGenuine(allowFebruary: true);
            // The player's LaunchPad: the download path never uses the setting.
            var source = new FakeSource(pack);
            ServerPatches.Listed listed = source.Listed();
            var e = Assert.Throws<AggregateException>(() =>
                ServerPatches.Install(new GameStore(fx.SteamCopy()), source, "Test", listed, listed.Sha256, Quiet, CancellationToken.None).Wait());
            Assert.Contains("can't be verified", e.InnerException.Message);
            Assert.Equal(0, source.Downloads);
            // The server: never served.
            string folder = Path.Combine(fx.Root, "server-patches");
            Directory.CreateDirectory(folder);
            File.Copy(pack, Path.Combine(folder, "february.lmpatch"));
            using var library = new PatchLibrary(folder, null, _ => { });
            library.Scan();
            Assert.Empty(library.Packs);
        }
    }
}
