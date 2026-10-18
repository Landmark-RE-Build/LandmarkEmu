using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.WorldServer;
using LandmarkEmulator.WorldServer.Game;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;
using static LandmarkEmulator.Tests.Config.ConfigSources;

namespace LandmarkEmulator.Tests.Config
{
    /// <summary>
    /// The admin Settings page (Game/ServerSettings*.cs): every setting has a rule, the page's values win over .env and
    /// WorldServer.json, secrets never reach the page, only editable settings with valid values are saved (all or
    /// nothing), every POST needs the admin and the anti-forgery token, and the overrides file is written atomically.
    /// </summary>
    public class ServerSettingsTests
    {
        /// <summary>Settings read from the process environment by their classes (ConfigConsistencyTests.RawEnvironment).</summary>
        private static readonly string[] EnvironmentOnly = { "LANDMARK_SEASON", "HAT_DROPS", "ZONE_INTERNAL_PORT" };

        private static IEnumerable<string> CodeKeys => Code.Keys.Where(k => !k.Contains(' ')).Concat(EnvironmentOnly);

        // ---- Metadata -----------------------------------------------------------------------------------------------------

        [Fact]
        public void EveryCodeSetting_HasARule()
        {
            var missing = CodeKeys.Where(k => ServerSettings.RuleFor(k) == null);
            Assert.Empty(missing, "add these to ServerSettings.Table.cs");
        }

        [Fact]
        public void EveryConfigurationProperty_IsOnThePage_WithARule()
        {
            var leaves = ServerSettings.Definitions.Where(d => d.Path != null).ToList();
            Assert.True(leaves.Count > 90, $"{leaves.Count} properties");
            Assert.Empty(leaves.Where(d => ServerSettings.RuleFor(d.Key) == null).Select(d => d.Key));
            foreach (string key in Code.Keys.Where(k => !k.Contains(' ')).Concat(EnvironmentOnly))
                Assert.True(ServerSettings.Definitions.Any(d => d.Key == key || d.Key.StartsWith(key + ":")), $"{key} is not on the page");
        }

        [Fact]
        public void EveryRule_NamesARealSetting()
        {
            var keys = new HashSet<string>(CodeKeys, StringComparer.Ordinal);
            Assert.Empty(ServerSettings.Rules.Select(r => r.Key).Where(k => !keys.Contains(k)), "rules for settings no code reads");
            Assert.Empty(ServerSettings.Rules.GroupBy(r => r.Key).Where(g => g.Count() > 1).Select(g => g.Key), "duplicate rules");
        }

        [Fact]
        public void RawRules_HaveTheCodeDefaults()
        {
            var different = ServerSettings.Rules.Where(r => r.RawType != null && !r.EnvironmentOnly)
                .Where(r => Normalise(r.RawDefault) != Normalise(Code[r.Key].Default))
                .Select(r => $"{r.Key}: table {r.RawDefault}, code {Code[r.Key].Default}");
            Assert.Empty(different);
        }

        [Fact]
        public void EditableSettings_HaveHelp_AndTheirDefaultsAndLiveValuesAreAccepted()
        {
            var problems = new List<string>();
            foreach (SettingDefinition d in ServerSettings.Definitions.Where(d => d.Editable))
            {
                if (string.IsNullOrWhiteSpace(d.Help) && string.IsNullOrWhiteSpace(d.Rule.Note))
                    problems.Add($"{d.Key}: no help text (.env.example) and no note");
                if (d.Default != null && !ServerSettings.TryNormalise(d, d.Default, out _, out string e1))
                    problems.Add($"default refused: {e1}");
                if (d.Rule.Live != null && !ServerSettings.TryNormalise(d, d.Rule.Live, out _, out string e2))
                    problems.Add($"live value refused: {e2}");
                if (d.Type != typeof(string) && !ServerSettings.IsBool(d.Type) && !ServerSettings.IsInteger(d.Type) && !ServerSettings.IsReal(d.Type))
                    problems.Add($"{d.Key}: type {d.Type.Name} can't be edited");
            }
            Assert.Empty(problems);
        }

        [Fact]
        public void GroupsHelpAndEnvNames_ComeFromEnvExampleAndCompose()
        {
            SettingDefinition free = ServerSettings.Find("FreeBuild");
            Assert.Equal("Your world", free.Group);
            Assert.Equal("FREE_BUILD", free.EnvNames.Single());
            Assert.Contains("Build and place props anywhere", free.Help);
            Assert.Equal("false", free.Rule.Live);
            // Several variables under one note share it; a key built from two variables names both.
            Assert.Contains("Scheduled restarts", ServerSettings.Find("RestartTimeZone").Help);
            Assert.Contains("OUTPOST_PRELOAD", ServerSettings.Find("OutpostPreload").Help);
            Assert.Contains("PUBLIC_HOST", ServerSettings.Find("PublicAddress").EnvNames);
            Assert.Contains("WORLD_PORT", ServerSettings.Find("PublicAddress").EnvNames);
            Assert.Equal("Network", ServerSettings.Find("Network:Port").Group);
            Assert.Contains("DB_PASSWORD", ServerSettings.Find("Database:Auth:ConnectionString").EnvNames);
            // Every section of .env.example the world reads is a group.
            Assert.Contains("Your world", ServerSettingsDocs.Env.Values.Select(v => v.Section));
        }

        [Fact]
        public void ReadOnlyAndSecretSettings_AreClassified()
        {
            foreach (string key in new[] { "Network:Port", "PublicAddress", "ZoneServerId", "ClientWorldId", "WebPagesPort", "GameDataPath",
                         "GameDirectory", "SocialAddress", "Admins", "AdminList", "Continent", "SettingsOverridesFile" })
                Assert.Equal(SettingAccess.ReadOnly, ServerSettings.Find(key).Access, key);
            foreach (string key in new[] { "Database:Auth:ConnectionString", "Database:Zone:ConnectionString", "Database:Character:Provider" })
                Assert.Equal(SettingAccess.Secret, ServerSettings.Find(key).Access, key);
            Assert.True(ServerSettings.LooksSecret("Something:Password"));
            Assert.Equal(SettingAccess.Editable, ServerSettings.Find("FreeBuild").Access);
        }

        // ---- Precedence -----------------------------------------------------------------------------------------------------

        private sealed class Harness : IDisposable
        {
            public string Folder { get; } = Path.Combine(Path.GetTempPath(), "lm-settings-" + Guid.NewGuid().ToString("N"));
            public string File => Path.Combine(Folder, "settings-overrides.json");
            public IConfigurationRoot Root { get; }
            public OverridesConfigurationProvider Overrides { get; }
            public WorldServerConfiguration Config { get; }
            public SettingsStore Store { get; }
            private readonly List<string> variables = new();
            public const string Prefix = "LMTEST_SETTINGS_";

            public Harness(string json = "{}", Dictionary<string, string> environment = null, Dictionary<string, string> page = null)
            {
                Directory.CreateDirectory(Folder);
                string jsonFile = Path.Combine(Folder, "WorldServer.json");
                System.IO.File.WriteAllText(jsonFile, json);
                foreach (var (k, v) in environment ?? new Dictionary<string, string>())
                {
                    Environment.SetEnvironmentVariable(Prefix + k, v);
                    variables.Add(Prefix + k);
                }
                var saved = (page ?? new Dictionary<string, string>()).ToDictionary(p => p.Key, p => new SettingOverride(p.Value, "test", DateTime.UtcNow));
                Overrides = new OverridesConfigurationProvider(saved.Select(s => new KeyValuePair<string, string>(s.Key, s.Value.Value)));
                Root = new ConfigurationBuilder().AddJsonFile(jsonFile, false, false).AddEnvironmentVariables(Prefix)
                    .Add(new OverridesConfigurationSource(Overrides)).Build();
                Config = Root.Get<WorldServerConfiguration>() ?? new WorldServerConfiguration();
                Store = new SettingsStore(File, Root, Overrides, () => Config, saved);
            }

            public void Dispose()
            {
                foreach (string v in variables)
                    Environment.SetEnvironmentVariable(v, null);
                (Root as IDisposable)?.Dispose();
                try
                {
                    Directory.Delete(Folder, true);
                }
                catch (IOException)
                {
                }
            }
        }

        [Fact]
        public void PageValues_WinOverEnvironment_WhichWinsOverJson()
        {
            using var h = new Harness("{\"MaxPlayers\": 5, \"MotdSubject\": \"from json\", \"FreeBuild\": false}",
                new() { ["MaxPlayers"] = "7", ["MotdSubject"] = "from env" }, new() { ["MotdSubject"] = "from page" });
            Assert.Equal(7, h.Config.MaxPlayers);
            Assert.Equal("from page", h.Config.MotdSubject);
            Assert.Equal(("7", ".env"), h.Store.Below(ServerSettings.Find("MaxPlayers")));
            Assert.Equal(("from env", ".env"), h.Store.Below(ServerSettings.Find("MotdSubject")));
            Assert.Equal(("false", "WorldServer.json"), h.Store.Below(ServerSettings.Find("FreeBuild")));
            Assert.Equal(("8", "default"), h.Store.Below(ServerSettings.Find("DayCycleSpeed")));
            Assert.Equal("from page", h.Store.Running(ServerSettings.Find("MotdSubject")));
            Assert.Equal("from page", h.Store.AfterRestart(ServerSettings.Find("MotdSubject")));
        }

        [Fact]
        public void Save_AppliesNowSettings_AndKeepsRestartOnesForTheRestart()
        {
            using var h = new Harness();
            var result = h.Store.Save("Tester", new Dictionary<string, string> { ["MaxPlayers"] = "12", ["FreeStore"] = "true" }, null);
            Assert.True(result.Ok, result.Message);
            Assert.Equal(2, result.Changes.Count);
            Assert.NotNull(result.ApplyNow);
            result.ApplyNow();
            Assert.Equal(12, h.Config.MaxPlayers);
            Assert.False(h.Config.FreeStore, "a restart setting stays as it was until the restart");
            Assert.True(h.Store.RestartPending(ServerSettings.Find("FreeStore")));
            Assert.False(h.Store.RestartPending(ServerSettings.Find("MaxPlayers")));
            Assert.Equal("12", h.Root["MaxPlayers"]);
            var problems = new List<string>();
            var reread = ServerSettings.ReadFile(h.File, problems);
            Assert.Empty(problems);
            Assert.Equal("12", reread["MaxPlayers"].Value);
            Assert.Equal("true", reread["FreeStore"].Value);
            Assert.Equal("Tester", reread["FreeStore"].By);
        }

        [Fact]
        public void RawSettings_AreReadThroughTheConfiguration_AfterASave()
        {
            using var h = new Harness();
            var result = h.Store.Save("Tester", new Dictionary<string, string> { ["ShutdownBroadcasts"] = "on" }, null);
            Assert.True(result.Ok, result.Message);
            result.ApplyNow();
            Assert.True(h.Root.GetValue("ShutdownBroadcasts", false));
            Assert.Equal("true", h.Store.Running(ServerSettings.Find("ShutdownBroadcasts")));
        }

        [Fact]
        public void Reset_RemovesTheOverride_AndTheEnvironmentCountsAgain()
        {
            using var h = new Harness("{}", new() { ["LandscapeCapacity"] = "40" });
            var saved = h.Store.Save("Tester", new Dictionary<string, string> { ["LandscapeCapacity"] = "10" }, null);
            saved.ApplyNow();
            Assert.Equal(10, h.Config.LandscapeCapacity);
            var reset = h.Store.Save("Tester", null, new[] { "LandscapeCapacity" });
            Assert.True(reset.Ok, reset.Message);
            Assert.True(reset.Changes.Single().Reset);
            reset.ApplyNow();
            Assert.Equal(40, h.Config.LandscapeCapacity);
            Assert.Equal("40", h.Root["LandscapeCapacity"]);
            Assert.False(ServerSettings.ReadFile(h.File, null).ContainsKey("LandscapeCapacity"));
        }

        [Fact]
        public void UnknownReadOnlyAndSecretNames_AreRefused_AndNothingIsWritten()
        {
            using var h = new Harness();
            foreach (string key in new[] { "NoSuchSetting", "Admins", "AdminList", "WebPagesPort", "Network:Port", "ZoneServerId",
                         "Database:Auth:ConnectionString", "Continent", "SettingsOverridesFile", "StoreLumenPrices" })
            {
                var result = h.Store.Save("Tester", new Dictionary<string, string> { [key] = "1", ["MaxPlayers"] = "3" }, null);
                Assert.False(result.Ok, key);
                Assert.Null(result.ApplyNow);
                Assert.False(File.Exists(h.File), $"{key}: the file was written");
                Assert.Equal(0, h.Config.MaxPlayers);
                var reset = h.Store.Save("Tester", null, new[] { key });
                Assert.False(reset.Ok, "reset " + key);
            }
        }

        [Fact]
        public void BadTypesRangesAndFormats_AreRefused()
        {
            using var h = new Harness();
            var bad = new Dictionary<string, string>
            {
                ["MaxPlayers"] = "abc", ["LandscapeCapacity"] = "-1", ["OutpostRotationShare"] = "2", ["FreeBuild"] = "maybe",
                ["EventIsland"] = "spring", ["StorePrices"] = "lots", ["RestartSchedule"] = "whenever", ["MotdSubject"] = "two\nlines",
                ["ShowcaseDigestHourUtc"] = "24", ["DayCycleSpeed"] = "NaN", ["RestartTimeZone"] = "Mars/Olympus", ["MotdBody"] = new string('x', 5000),
                ["CavernLandscapes"] = "everywhere", ["GriefLevelPoints"] = "1,2,x",
            };
            foreach (var (key, value) in bad)
            {
                var result = h.Store.Save("Tester", new Dictionary<string, string> { [key] = value }, null);
                Assert.False(result.Ok, $"{key}={value} was accepted");
            }
            Assert.False(File.Exists(h.File));
            var good = h.Store.Save("Tester", new Dictionary<string, string>
            {
                ["EventIsland"] = "Winter", ["RestartSchedule"] = "Mon,Sat 03:00", ["StorePrices"] = "900001=500, 2551=1300",
                ["OutpostRotationShare"] = "0.5", ["CavernLandscapes"] = "406,391",
            }, null);
            Assert.True(good.Ok, good.Message);
            Assert.Equal("winter", ServerSettings.ReadFile(h.File, null)["EventIsland"].Value);
        }

        // ---- Secrets ------------------------------------------------------------------------------------------------------

        [Fact]
        public void Secrets_AreNeverInThePage()
        {
            const string secret = "hunter2-do-not-show";
            using var h = new Harness("{\"Database\": {\"Auth\": {\"ConnectionString\": \"server=db;password=" + secret + "\", \"Provider\": \"MySql\"}}}",
                new() { ["Database__Character__ConnectionString"] = "server=db;password=" + secret });
            string json = JsonSerializer.Serialize(ServerSettingsPage.View(h.Store));
            Assert.False(json.Contains(secret), "a password reached the page's data");
            Assert.False(ServerSettingsPage.Page("abc").Contains(secret));
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement auth = doc.RootElement.GetProperty("groups").EnumerateArray().SelectMany(g => g.GetProperty("settings").EnumerateArray())
                .Single(s => s.GetProperty("key").GetString() == "Database:Auth:ConnectionString");
            Assert.True(auth.GetProperty("isSet").GetBoolean());
            Assert.Equal(JsonValueKind.Null, auth.GetProperty("value").ValueKind);
            Assert.Equal(JsonValueKind.Null, auth.GetProperty("below").ValueKind);
        }

        // ---- Requests -----------------------------------------------------------------------------------------------------

        private static Dictionary<string, string> Form(params (string, string)[] fields) => fields.ToDictionary(f => f.Item1, f => f.Item2);

        [Fact]
        public void Requests_NeedAnAdmin()
        {
            using var h = new Harness();
            Assert.Equal(403, ServerSettingsPage.Handle("GET", ServerSettingsPage.PagePath, Form(), null, null, true, "tok", null, h.Store).Status);
            Assert.Equal(403, ServerSettingsPage.Handle("GET", ServerSettingsPage.ApiPath, Form(), null, null, true, "tok", null, h.Store).Status);
            var post = ServerSettingsPage.Handle("POST", ServerSettingsPage.SavePath,
                Form(("csrf", ServerSettingsPage.CsrfToken("tok")), ("set.MaxPlayers", "4")), null, null, true, "tok", null, h.Store);
            Assert.Equal(403, post.Status);
            Assert.False(File.Exists(h.File));
            var page = ServerSettingsPage.Handle("GET", ServerSettingsPage.PagePath, Form(), null, null, true, "tok", "Admin", h.Store);
            Assert.Equal(200, page.Status);
            Assert.Contains(ServerSettingsPage.CsrfToken("tok"), page.Html);
        }

        [Fact]
        public void Posts_NeedTheAntiForgeryToken_AndThisServersOrigin()
        {
            using var h = new Harness();
            string csrf = ServerSettingsPage.CsrfToken("tok");
            (string, string) set = ("set.MaxPlayers", "4");
            Assert.Equal(403, ServerSettingsPage.Handle("POST", ServerSettingsPage.SavePath, Form(set), null, "w:19001", true, "tok", "Admin", h.Store).Status);
            Assert.Equal(403, ServerSettingsPage.Handle("POST", ServerSettingsPage.SavePath, Form(set, ("csrf", (csrf[0] == '0' ? "1" : "0") + csrf[1..])), null, "w:19001", true, "tok", "Admin", h.Store).Status);
            // Another admin's token: its page token does not fit.
            Assert.Equal(403, ServerSettingsPage.Handle("POST", ServerSettingsPage.SavePath, Form(set, ("csrf", ServerSettingsPage.CsrfToken("other"))), null, "w:19001", true, "tok", "Admin", h.Store).Status);
            Assert.Equal(403, ServerSettingsPage.Handle("POST", ServerSettingsPage.SavePath, Form(set, ("csrf", csrf)), "http://evil.example", "w:19001", true, "tok", "Admin", h.Store).Status);
            Assert.Equal(403, ServerSettingsPage.Handle("POST", ServerSettingsPage.RestartPath, Form(("seconds", "60")), null, "w:19001", true, "tok", "Admin", h.Store).Status);
            Assert.Equal(400, ServerSettingsPage.Handle("POST", ServerSettingsPage.SavePath, Form(set, ("csrf", csrf)), null, "w:19001", false, "tok", "Admin", h.Store).Status);
            Assert.False(File.Exists(h.File), "a refused request wrote the file");

            var ok = ServerSettingsPage.Handle("POST", ServerSettingsPage.SavePath, Form(set, ("csrf", csrf)), "http://w:19001", "w:19001", true, "tok", "Admin", h.Store);
            Assert.Equal(200, ok.Status);
            ok.WorldWork();
            Assert.Equal(4, h.Config.MaxPlayers);
        }

        [Fact]
        public void Posts_RefuseReadOnlyNamesAndUnknownFields()
        {
            using var h = new Harness();
            string csrf = ServerSettingsPage.CsrfToken("tok");
            Assert.Equal(400, ServerSettingsPage.Handle("POST", ServerSettingsPage.SavePath, Form(("csrf", csrf), ("set.AdminList", "account:Mallory")),
                null, null, true, "tok", "Admin", h.Store).Status);
            Assert.Equal(400, ServerSettingsPage.Handle("POST", ServerSettingsPage.SavePath, Form(("csrf", csrf), ("set.Database:Auth:ConnectionString", "x")),
                null, null, true, "tok", "Admin", h.Store).Status);
            Assert.Equal(400, ServerSettingsPage.Handle("POST", ServerSettingsPage.SavePath, Form(("csrf", csrf), ("MaxPlayers", "4")),
                null, null, true, "tok", "Admin", h.Store).Status);
            Assert.False(File.Exists(h.File));
            Assert.Equal(0, h.Config.MaxPlayers);
        }

        [Fact]
        public void Csrf_IsTiedToTheAdminToken()
        {
            Assert.True(ServerSettingsPage.CsrfValid("abc", ServerSettingsPage.CsrfToken("abc")));
            Assert.False(ServerSettingsPage.CsrfValid("abc", ServerSettingsPage.CsrfToken("abd")));
            Assert.False(ServerSettingsPage.CsrfValid("abc", ""));
            Assert.False(ServerSettingsPage.CsrfValid("", ServerSettingsPage.CsrfToken("")));
            Assert.True(ServerSettingsPage.OriginAllowed(null, "x:1"));
            Assert.True(ServerSettingsPage.OriginAllowed("http://x:1", "x:1"));
            Assert.False(ServerSettingsPage.OriginAllowed("http://x:2", "x:1"));
            Assert.False(ServerSettingsPage.OriginAllowed("null", "x:1"));
        }

        // ---- The file -----------------------------------------------------------------------------------------------------

        [Fact]
        public void TheFile_IsWrittenAtomically_AndRoundTrips()
        {
            string folder = Path.Combine(Path.GetTempPath(), "lm-settings-" + Guid.NewGuid().ToString("N"), "nested");
            string file = Path.Combine(folder, "settings-overrides.json");
            try
            {
                var at = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
                var first = new Dictionary<string, SettingOverride>
                {
                    ["FreeBuild"] = new("true", "Alice", at), ["MotdBody"] = new("Hello \"world\" ünïcode", "Bob", at),
                };
                ServerSettings.WriteFile(file, first);
                ServerSettings.WriteFile(file, new Dictionary<string, SettingOverride>(first) { ["MaxPlayers"] = new("9", "Alice", at) });
                Assert.False(File.Exists(file + ".tmp"), "the temporary file was left behind");
                var problems = new List<string>();
                var read = ServerSettings.ReadFile(file, problems);
                Assert.Empty(problems);
                Assert.Equal(3, read.Count);
                Assert.Equal("Hello \"world\" ünïcode", read["MotdBody"].Value);
                Assert.Equal("Alice", read["FreeBuild"].By);
                Assert.Equal(at, read["FreeBuild"].At.ToUniversalTime());

                // A hand-edited file: entries the page may not set, or with bad values, are left out (and reported).
                File.WriteAllText(file, "{\"Settings\": {\"FreeBuild\": {\"Value\": \"true\"}, \"AdminList\": {\"Value\": \"account:Mallory\"}, "
                    + "\"Database:Auth:ConnectionString\": {\"Value\": \"x\"}, \"MaxPlayers\": {\"Value\": \"lots\"}, \"Nope\": {\"Value\": \"1\"}}}");
                problems.Clear();
                read = ServerSettings.ReadFile(file, problems);
                Assert.Equal("FreeBuild", read.Keys.Single());
                Assert.Equal(4, problems.Count);
                File.WriteAllText(file, "{ not json");
                problems.Clear();
                Assert.Empty(ServerSettings.ReadFile(file, problems));
                Assert.Single(problems);
            }
            finally
            {
                try
                {
                    Directory.Delete(Path.GetDirectoryName(folder), true);
                }
                catch (IOException)
                {
                }
            }
        }

        [Fact]
        public void StartUp_AppliesOnlyEditableValidValues_FromTheConfiguredFile()
        {
            using var h = new Harness();
            File.WriteAllText(h.File, "{\"Settings\": {\"FreeBuild\": {\"Value\": \"true\"}, \"Admins:0\": {\"Value\": \"account:Mallory\"}, "
                + "\"MaxPlayers\": {\"Value\": \"-5\"}}}");
            IConfigurationRoot below = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
            {
                ["SettingsOverridesFile"] = h.File,
            }).Build();
            Assert.Equal(h.File, ServerSettings.OverridesPath(below));
            var pairs = ServerSettings.StartupOverrides(below).ToList();
            Assert.Equal("FreeBuild", pairs.Single().Key);
            Assert.Equal("true", pairs.Single().Value);
            Assert.Equal(Path.Combine(AppContext.BaseDirectory, ServerSettings.DefaultFileName), ServerSettings.OverridesPath(new ConfigurationBuilder().Build()));
        }
    }
}
