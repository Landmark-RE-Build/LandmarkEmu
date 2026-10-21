using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using LandmarkLocal;
using Xunit;

namespace LandmarkEmulator.Tests.Local
{
    public class LocalHostTests
    {
        [Fact]
        public void PortChecker_RequiredPorts_IncludeLoopbackDefaults()
        {
            var settings = new LocalSettings();
            var ports = PortChecker.RequiredPorts(settings);
            Assert.Contains(ports, p => p.Name == "MariaDB" && p.Port == 3307);
            Assert.Contains(ports, p => p.Name == "Auth account API" && p.Port == 5000);
            Assert.Contains(ports, p => p.Name == "Auth login" && p.Protocol == PortProtocol.Udp);
        }

        [Fact]
        public void PortChecker_CanBindFreeEphemeralTcp()
        {
            Assert.True(PortChecker.CanBindTcp(IPAddress.Loopback, 0, out _));
        }

        [Fact]
        public void ConfigWriter_WritesLoopbackAuthAndWorld()
        {
            string root = Path.Combine(Path.GetTempPath(), "landmarklocal-test-" + Path.GetRandomFileName());
            try
            {
                var paths = new LocalPaths(root);
                var settings = new LocalSettings
                {
                    LandmarkFolder = @"C:\Games\Landmark",
                    AdminAccountName = "Alice",
                    DbPassword = "secret",
                    DbRootPassword = "rootsecret",
                };
                ConfigWriter.WriteAll(paths, settings);
                Assert.True(File.Exists(paths.AuthServerJson));
                Assert.True(File.Exists(paths.WorldServerJson));
                string auth = File.ReadAllText(paths.AuthServerJson);
                Assert.Contains("127.0.0.1", auth);
                Assert.Contains("5000", auth);
                Assert.Contains("lmemu_auth", auth);
                string world = File.ReadAllText(paths.WorldServerJson);
                Assert.Contains("account:Alice", world);
                Assert.Contains("lmemu_world_main", world);
                Assert.Contains("560916", world);
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        [Fact]
        public void LocalSettings_AdminEntry_PrefixesAccount()
        {
            var s = new LocalSettings { AdminAccountName = " Robin " };
            Assert.Equal("account:Robin", s.AdminEntry);
        }

        [Fact]
        public void GameDataSetup_HasTerrainBins_FalseWhenEmpty()
        {
            string dir = Path.Combine(Path.GetTempPath(), "landmarklocal-gd-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                Assert.False(GameDataSetup.HasTerrainBins(dir));
                File.WriteAllBytes(Path.Combine(dir, "TerrainHeights406.bin"), new byte[] { 1, 2, 3 });
                Assert.True(GameDataSetup.HasTerrainBins(dir));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void MariaDbHost_BootstrapSql_CreatesLandmarkUserAndDatabases()
        {
            var settings = new LocalSettings { DbPassword = "p", DbRootPassword = "r" };
            string sql = MariaDbHost.BootstrapSql(settings);
            Assert.Contains("lmemu_auth", sql);
            Assert.Contains("lmemu_character", sql);
            Assert.Contains("landmarkemu", sql);
            Assert.Contains(@"lmemu\_world\_%", sql);
        }

        [Fact]
        public void LaunchPadInstaller_RemoteNote_MentionsAccountAndPlay()
        {
            string msg = LaunchPadInstaller.RemoteLaunchPadNote(new LocalSettings { AdminAccountName = "Tess" });
            Assert.Contains("Tess", msg);
            Assert.Contains("Play", msg);
        }

        [Fact]
        public void AccountRules_AcceptsValidNames()
        {
            Assert.True(AccountRules.IsValidAccountName("Alice"));
            Assert.True(AccountRules.IsValidAccountName("a_1"));
            Assert.False(AccountRules.IsValidAccountName("ab"));
            Assert.False(AccountRules.IsValidAccountName("bad name"));
        }

        [Fact]
        public void LocalSettings_RememberLogin_RoundTrips()
        {
            var s = new LocalSettings { AdminAccountName = "Tess" };
            Assert.False(s.HasRememberedLogin());
            s.RememberLogin("Tess", "KEY", "SALT");
            Assert.True(s.HasRememberedLogin());
            s.ForgetLogin();
            Assert.False(s.HasRememberedLogin());
        }

        [Fact]
        public void ProgressThrottle_ReportsChangeOnceThenHeartbeatWithElapsed()
        {
            var lines = new List<string>();
            var statuses = new List<string>();
            IProgress<string> progress = new SyncProgress(lines.Add);
            var started = DateTime.UtcNow - TimeSpan.FromSeconds(65);
            var throttle = new ProgressThrottle(progress, TimeSpan.FromMilliseconds(50), statuses.Add);
            throttle.ReportWaiting("Waiting for the world to register…", started, firstTip: "tip");
            throttle.ReportWaiting("Waiting for the world to register…", started);
            throttle.ReportWaiting("Waiting for the world to register…", started);
            Assert.Equal(2, lines.Count); // message + tip
            Assert.True(statuses[^1].Contains("1:", StringComparison.Ordinal));
            Thread.Sleep(60);
            throttle.ReportWaiting("Waiting for the world to register…", started);
            Assert.Equal(3, lines.Count);
            Assert.True(lines[2].StartsWith("Still ", StringComparison.Ordinal));
            Assert.True(lines[2].Contains("1:", StringComparison.Ordinal));
        }

        sealed class SyncProgress : IProgress<string>
        {
            readonly Action<string> _onReport;
            public SyncProgress(Action<string> onReport) => _onReport = onReport;
            public void Report(string value) => _onReport(value);
        }

        [Fact]
        public async Task ReadinessProbe_WhenNothingListening_IsNotReady()
        {
            var settings = new LocalSettings { AccountPort = 1 }; // almost certainly closed
            ReadinessSnapshot snap = await ReadinessProbe.ProbeAsync(settings);
            Assert.False(snap.InfoOk);
            Assert.NotEqual(HostPhase.Ready, snap.Phase);
        }
    }
}
