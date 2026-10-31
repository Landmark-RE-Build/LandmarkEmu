using LandmarkEmulator.PatchPacks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace LandmarkEmulator.Tests.PatchPacks
{
    /// <summary>
    /// LaunchPad's ClientConfig.ini rules (LandmarkEmulator.PatchPacks/ClientConfig.cs, docs/launchpad.md "Advanced mode"):
    /// what PLAY writes for each advanced-mode choice, that it writes nothing else (no other line, never the dead
    /// [Terrain] lines, no other file), the original it keeps, and how it writes the file.
    /// </summary>
    public sealed class ClientConfigTests : IDisposable
    {
        /// <summary>A short file in the shape of Steam's ClientConfig.ini (keys before the first section, comments, CRLF).</summary>
        private const string Steam =
            "World=\r\n" +
            "Server=old.example:20042\r\n" +
            "\r\n" +
            "[Libraries]\r\n" +
            "#For Deep\r\n" +
            "GraphicsDataPath=GraphicsData\r\n" +
            "\r\n" +
            "[AssetDelivery]\r\n" +
            "IndirectServerAddress=http://127.0.0.1:7000/assets\r\n" +
            "DirectThreadCount=10\r\n" +
            "DirectEnabled=1\r\n" +
            "\r\n" +
            "[Marketplace]\r\n" +
            "MarketplaceUrl=https://store.example\r\n" +
            "MarketplaceAssets=http://assets.example\r\n";

        private const string Login = "play.example:20042";
        private const string Pages = "http://play.example:19001";

        private readonly string game = Path.Combine(Path.GetTempPath(), "lmemu-config-" + Guid.NewGuid().ToString("N")[..10]);
        private string Config => Path.Combine(game, ClientConfigFile.FileName);
        private string Original => Path.Combine(game, ClientConfigFile.OriginalName);

        public ClientConfigTests() => Directory.CreateDirectory(game);

        public void Dispose()
        {
            try { Directory.Delete(game, true); } catch (IOException) { }
        }

        private static ClientConfigOptions Choose(ConfigChoice mode, string value = "", bool advanced = true)
            => new() { Advanced = advanced, Choices = { [ClientConfigFile.ThreadsKey] = new SettingChoice { Mode = mode, Value = value } } };

        private static string Threads(string text) => ClientConfigFile.Value(text, "AssetDelivery", "DirectThreadCount");

        // ---- what PLAY writes

        [Fact]
        public void NoOptions_WritesTheServerItsPagesAndTwoThreads_KeepingEverythingElse()
        {
            string text = ClientConfigFile.Apply(Steam, Login, Pages, null, out bool changed);
            Assert.True(changed);
            Assert.Equal(Login, ClientConfigFile.Value(text, "", "Server"));
            Assert.Equal(Pages, ClientConfigFile.Value(text, "Marketplace", "MarketplaceUrl"));
            Assert.Equal("2", Threads(text));
            Assert.Equal(Pages, ClientConfigFile.Value(text, "Marketplace", "MarketplaceAssets"));
            Assert.Equal(Steam.Replace("old.example:20042", Login).Replace("https://store.example", Pages).Replace("http://assets.example", Pages)
                .Replace("DirectThreadCount=10", "DirectThreadCount=2"), text);
        }

        [Fact]
        public void AdvancedModeOff_IgnoresTheChoices()
        {
            foreach (ConfigChoice mode in Enum.GetValues<ConfigChoice>())
                Assert.Equal("2", Threads(ClientConfigFile.Apply(Steam, Login, Pages, Choose(mode, "6", advanced: false), out _)));
        }

        [Fact]
        public void Recommended_WritesTwo()
            => Assert.Equal("2", Threads(ClientConfigFile.Apply(Steam, Login, Pages, Choose(ConfigChoice.Recommended, "6"), out _)));

        [Theory]
        [InlineData("6", "6")]
        [InlineData(" 1 ", "1")]
        [InlineData("10", "10")]
        [InlineData("04", "4")]
        // A value that fails the check (launchpad.json edited by hand) falls back to the recommended 2.
        [InlineData("0", "2")]
        [InlineData("11", "2")]
        [InlineData("-3", "2")]
        [InlineData("four", "2")]
        [InlineData("", "2")]
        public void Custom_WritesTheValue_WhenValid(string value, string written)
            => Assert.Equal(written, Threads(ClientConfigFile.Apply(Steam, Login, Pages, Choose(ConfigChoice.Custom, value), out _)));

        [Fact]
        public void Leave_DoesNotTouchTheLine_NorAddOne()
        {
            string text = ClientConfigFile.Apply(Steam, Login, Pages, Choose(ConfigChoice.Leave), out _);
            Assert.Equal("10", Threads(text));
            string without = Steam.Replace("DirectThreadCount=10\r\n", "");
            text = ClientConfigFile.Apply(without, Login, Pages, Choose(ConfigChoice.Leave), out _);
            Assert.Null(Threads(text));
            // Server= and MarketplaceUrl= are written whatever the choice.
            Assert.Equal(Login, ClientConfigFile.Value(text, "", "Server"));
            Assert.Equal(Pages, ClientConfigFile.Value(text, "Marketplace", "MarketplaceUrl"));
        }

        [Fact]
        public void MissingLines_GoIntoTheirSections()
        {
            string text = ClientConfigFile.Apply("[Libraries]\r\nGraphicsDataPath=GraphicsData\r\n", Login, Pages, null, out _);
            Assert.Equal("Server=" + Login + "\r\n[Libraries]\r\nGraphicsDataPath=GraphicsData\r\n\r\n[Marketplace]\r\nMarketplaceAssets=" + Pages +
                "\r\nMarketplaceUrl=" + Pages + "\r\n\r\n[AssetDelivery]\r\nDirectThreadCount=2\r\n", text);
            // An existing section gets the line right below its header; keys are matched without regard to case.
            text = ClientConfigFile.Apply("[assetdelivery]\r\ndirectthreadcount=7\r\n[Marketplace]\r\n", Login, Pages, null, out _);
            Assert.Equal("Server=" + Login + "\r\n[assetdelivery]\r\nDirectThreadCount=2\r\n[Marketplace]\r\nMarketplaceAssets=" + Pages
                + "\r\nMarketplaceUrl=" + Pages + "\r\n", text);
            // An empty file.
            text = ClientConfigFile.Apply("", Login, Pages, null, out _);
            Assert.Equal(Login, ClientConfigFile.Value(text, "", "Server"));
            Assert.Equal(Pages, ClientConfigFile.Value(text, "Marketplace", "MarketplaceAssets"));
            Assert.Equal("2", Threads(text));
        }

        [Fact]
        public void AKeyInAnotherSection_IsNotTaken()
        {
            // IndirectServerAddress= is not Server=, and a DirectThreadCount in another section is not the game's.
            string text = ClientConfigFile.Apply(Steam + "[Other]\r\nDirectThreadCount=9\r\n", Login, Pages, null, out _);
            Assert.Equal("http://127.0.0.1:7000/assets", ClientConfigFile.Value(text, "AssetDelivery", "IndirectServerAddress"));
            Assert.Equal("9", ClientConfigFile.Value(text, "Other", "DirectThreadCount"));
            Assert.Equal("2", Threads(text));
        }

        [Fact]
        public void NothingToChange_LeavesTheTextAlone()
        {
            string once = ClientConfigFile.Apply(Steam, Login, Pages, null, out _);
            Assert.Equal(once, ClientConfigFile.Apply(once, Login, Pages, null, out bool changed));
            Assert.False(changed);
        }

        [Fact]
        public void PointAt_KeepsTheOriginalOnce_AndLeavesNoTemporaryFile()
        {
            File.WriteAllText(Config, Steam);
            Assert.True(ClientConfigFile.PointAt(game, Login, Pages, null));
            Assert.Equal(Steam, File.ReadAllText(Original));
            Assert.Equal("2", Threads(File.ReadAllText(Config)));
            Assert.True(ClientConfigFile.PointAt(game, "other.example:20042", Pages, Choose(ConfigChoice.Custom, "3")));
            Assert.Equal(Steam, File.ReadAllText(Original));
            Assert.Equal("3", Threads(File.ReadAllText(Config)));
            Assert.False(ClientConfigFile.PointAt(game, "other.example:20042", Pages, Choose(ConfigChoice.Custom, "3")));
            Assert.Equal(new[] { ClientConfigFile.FileName, ClientConfigFile.OriginalName },
                Directory.GetFiles(game).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        }

        [Fact]
        public void PointAt_ReplacesAHardLink_InsteadOfWritingThroughIt()
        {
            // The test mirror's game files are hard links to the Steam install: a change must never reach the other name.
            File.WriteAllText(Config, Steam);
            string twin = Path.Combine(game, "twin.ini");
            try
            {
                if (!CreateHardLink(twin, Config, IntPtr.Zero))
                    return;   // not NTFS: nothing to check
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
            {
                return;   // not Windows
            }
            ClientConfigFile.PointAt(game, Login, Pages, null);
            Assert.Equal(Steam, File.ReadAllText(twin));
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(string newName, string existing, IntPtr security);

        [Fact]
        public void Original_ComesFromTheOrigFile_ElseSteamsValue()
        {
            Assert.Equal(("10", "Steam's ClientConfig.ini"), ClientConfigFile.Original(game, ClientConfigFile.Threads));
            File.WriteAllText(Original, Steam.Replace("DirectThreadCount=10", "DirectThreadCount=8"));
            Assert.Equal(("8", ClientConfigFile.OriginalName), ClientConfigFile.Original(game, ClientConfigFile.Threads));
            File.WriteAllText(Original, "[AssetDelivery]\r\n");
            Assert.Equal(((string)null, ClientConfigFile.OriginalName), ClientConfigFile.Original(game, ClientConfigFile.Threads));
        }

        [Theory]
        [InlineData("1", true)]
        [InlineData("2", true)]
        [InlineData("10", true)]
        [InlineData("0", false)]
        [InlineData("11", false)]
        [InlineData("-1", false)]
        [InlineData("2.5", false)]
        [InlineData("1e1", false)]
        [InlineData("99999999999", false)]
        public void ThreadCount_IsAWholeNumberFromOneToTen(string value, bool ok)
            => Assert.Equal(ok, ClientConfigFile.Threads.Validate(value) == null);

        [Fact]
        public void ServerAndPages_AreNotEditable()
        {
            Assert.False(ClientConfigFile.Server.Editable);
            Assert.False(ClientConfigFile.Marketplace.Editable);
            Assert.NotNull(ClientConfigFile.Server.Validate("x:1"));
            Assert.Equal(new[] { "Server", "MarketplaceUrl", "MarketplaceAssets", "DirectThreadCount" }, ClientConfigFile.Managed.Select(m => m.Key).ToArray());
        }

        [Fact]
        public void Choices_RoundTripThroughJson_WithTheModeAsText()
        {
            string json = JsonSerializer.Serialize(Choose(ConfigChoice.Leave, "4"));
            Assert.Contains("\"Leave\"", json);
            ClientConfigOptions back = JsonSerializer.Deserialize<ClientConfigOptions>(json);
            Assert.True(back.Advanced);
            Assert.Equal(ConfigChoice.Leave, back.Effective(ClientConfigFile.ThreadsKey).Mode);
            Assert.Equal(ConfigChoice.Recommended, new ClientConfigOptions().Effective(ClientConfigFile.ThreadsKey).Mode);
        }

        [Fact]
        public void AllocatorSlotExpand_DefaultsOff_AndRoundTripsAsText()
        {
            Assert.Equal(AllocatorSlotExpand.Off, new ClientConfigOptions().AllocatorSlotExpand);
            var options = new ClientConfigOptions { Advanced = true, AllocatorSlotExpand = AllocatorSlotExpand.Auto };
            string json = JsonSerializer.Serialize(options);
            Assert.Contains("\"Auto\"", json);
            ClientConfigOptions back = JsonSerializer.Deserialize<ClientConfigOptions>(json);
            Assert.Equal(AllocatorSlotExpand.Auto, back.AllocatorSlotExpand);
        }

        [Fact]
        public void ContourThreadExpand_DefaultsOff_AndRoundTripsAsText()
        {
            Assert.Equal(ContourThreadExpand.Off, new ClientConfigOptions().ContourThreadExpand);
            var options = new ClientConfigOptions
            {
                Advanced = true,
                AllocatorSlotExpand = AllocatorSlotExpand.On,
                ContourThreadExpand = ContourThreadExpand.Auto,
            };
            string json = JsonSerializer.Serialize(options);
            Assert.Contains("\"Auto\"", json);
            ClientConfigOptions back = JsonSerializer.Deserialize<ClientConfigOptions>(json);
            Assert.Equal(ContourThreadExpand.Auto, back.ContourThreadExpand);
        }

        // ---- PLAY writes nothing beyond the listed values

        /// <summary>The [Terrain] lines an old workspace script added to players' files (they do nothing).</summary>
        private const string Dead = "\r\n[Terrain]\r\nUseCachedMeshes=off\r\nCacheMeshes=off\r\n";

        public static IEnumerable<object[]> Files() => new[]
        {
            new object[] { Steam }, new object[] { "" }, new object[] { "[Libraries]\r\nGraphicsDataPath=GraphicsData\r\n" },
            new object[] { Steam + Dead }, new object[] { Steam.Replace("\r\n", "\n") }, new object[] { "World=\r\nServer=x:1" }
        };

        private static IEnumerable<ClientConfigOptions> EveryChoice()
        {
            yield return null;
            foreach (bool advanced in new[] { false, true })
            {
                yield return Choose(ConfigChoice.Recommended, "", advanced);
                yield return Choose(ConfigChoice.Custom, "7", advanced);
                yield return Choose(ConfigChoice.Custom, "0", advanced);
                yield return Choose(ConfigChoice.Leave, "", advanced);
            }
        }

        private static string[] LinesOf(string text) => text.Replace("\r\n", "\n").Split('\n');

        [Theory]
        [MemberData(nameof(Files))]
        public void Play_ChangesOnlyTheListedLines(string file)
        {
            var listed = new[] { "Server=", "MarketplaceUrl=", "MarketplaceAssets=", "DirectThreadCount=" };
            foreach (ClientConfigOptions options in EveryChoice())
            {
                string text = ClientConfigFile.Apply(file, Login, Pages, options, out _);
                List<string> before = LinesOf(file).ToList(), after = LinesOf(text).ToList();
                // Every line of the file is still there, in order, except the listed keys' old values.
                List<string> kept = after.Where(l => !listed.Any(k => l.StartsWith(k, StringComparison.Ordinal))).ToList();
                List<string> others = before.Where(l => !listed.Any(k => l.StartsWith(k, StringComparison.Ordinal))).ToList();
                foreach (string line in others)
                {
                    int at = kept.IndexOf(line);
                    Assert.True(at >= 0, $"lost: {line}");
                    kept.RemoveAt(at);
                }
                // What is new: only the headers of the two sections the lines belong in, and blank lines before them.
                Assert.All(kept, l => Assert.Contains(l, new[] { "", "[Marketplace]", "[AssetDelivery]" }));
                Assert.All(after.Where(l => listed.Any(k => l.StartsWith(k, StringComparison.Ordinal))),
                    l => Assert.Contains(l, new[] { "Server=" + Login, "MarketplaceUrl=" + Pages, "MarketplaceAssets=" + Pages, "DirectThreadCount=2",
                        "DirectThreadCount=7", "DirectThreadCount=10" }));
                // Never the dead [Terrain] lines: their count is what the file had.
                Assert.Equal(LinesOf(file).Count(l => l.Contains("CachedMeshes") || l.Contains("CacheMeshes")),
                    LinesOf(text).Count(l => l.Contains("CachedMeshes") || l.Contains("CacheMeshes")));
                Assert.Equal(LinesOf(file).Count(l => l == "[Terrain]"), LinesOf(text).Count(l => l == "[Terrain]"));
            }
        }

        [Fact]
        public void PointAt_WritesNoOtherFile()
        {
            // No ClientConfig.ini yet: only the file itself (nothing to keep as the original).
            ClientConfigFile.PointAt(game, Login, Pages, Choose(ConfigChoice.Custom, "5"));
            Assert.Equal(new[] { ClientConfigFile.FileName }, Directory.GetFileSystemEntries(game).Select(Path.GetFileName).ToArray());
            // With one: the file and, once, the .orig; no backups, no LaunchPadEmu folder, no temporary file.
            File.WriteAllText(Config, Steam + Dead);
            foreach (ClientConfigOptions options in EveryChoice())
                ClientConfigFile.PointAt(game, Login, Pages, options);
            Assert.Equal(new[] { ClientConfigFile.FileName, ClientConfigFile.OriginalName },
                Directory.GetFileSystemEntries(game).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray());
            Assert.Equal(Steam + Dead, File.ReadAllText(Original));
        }

        [Fact]
        public void Bytes_OutsideAscii_SurviveARewrite()
        {
            // The game's files are one byte per character (a code page): PLAY's rewrite keeps such bytes as they are.
            byte[] bytes = System.Text.Encoding.Latin1.GetBytes(Steam + "[Extra]\r\nName=Café\r\n");
            File.WriteAllBytes(Config, bytes);
            ClientConfigFile.PointAt(game, Login, Pages, null);
            Assert.Contains((byte)0xE9, File.ReadAllBytes(Config));
        }
    }
}
