using LandmarkEmulator.Tests.Support;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using static LandmarkEmulator.Tests.Config.ConfigSources;

namespace LandmarkEmulator.Tests.Config
{
    /// <summary>
    /// Every world setting the code reads is documented with the same default in WorldServer.example.json, the world's
    /// Docker service (docker/world/docker-compose.yml) and .env.example, and nothing there is a setting no code reads.
    /// A Docker default that differs from the code's means a Docker world and a plain-JSON world behave differently
    /// out of the box; a compose key no code reads is a switch that does nothing.
    /// </summary>
    public class ConfigConsistencyTests
    {
        /// <summary>Settings that are deployment details, not behaviour: the Docker value is fixed by the container layout.</summary>
        private static readonly Dictionary<string, string> DockerFixed = new()
        {
            ["GameDataPath"] = "the container mounts the game data at /gamedata",
            ["GameDirectory"] = "the container mounts the game folder at /game",
            ["HealthFile"] = "the healthcheck reads /tmp/world-alive",
            ["WebPagesHost"] = "must listen on 0.0.0.0 inside a container",
            ["WebPagesUrl"] = "built from PUBLIC_HOST and WEB_PAGES_PORT",
            ["PublicAddress"] = "built from PUBLIC_HOST and WORLD_PORT",
            ["SettingsOverridesFile"] = "the settings page's file is kept in the world's /data volume",
        };

        /// <summary>Settings deliberately not offered in Docker, with the reason.</summary>
        private static readonly Dictionary<string, string> NotInDocker = new()
        {
            ["Network"] = "structured; Docker sets Network__Port",
            ["Database"] = "structured; Docker sets the connection strings",
            ["StoreLumenPrices"] = "the JSON dictionary form of STORE_PRICES",
        };

        private static IEnumerable<CodeSetting> Settings => Code.Values.Where(s => !s.Key.Contains(' '));

        [Fact]
        public void Sources_AreParsed()
        {
            Assert.True(Code.Count > 80, $"{Code.Count} code settings");
            Assert.True(Compose.Count > 80, $"{Compose.Count} compose lines");
            Assert.True(EnvExample.Count > 80, $"{EnvExample.Count} .env.example variables");
            Assert.True(Json.Count > 80, $"{Json.Count} JSON keys");
            Assert.Contains("GatewayInternalAddress", GatewayOverride.Keys);
        }

        [Fact]
        public void CodeReadsEachSettingWithOneDefault()
        {
            var conflicts = Code.Keys.Where(k => k.Contains(" (")).Select(k => $"{k}: default {Code[k].Default} vs "
                + Code[k.Split(' ')[0]].Default);
            Assert.Empty(conflicts, "the same key is read with different defaults in different places");
        }

        [Fact]
        public void EverySetting_IsInTheExampleJson()
        {
            var missing = Settings.Where(s => !Json.ContainsKey(s.Key)).Select(s => $"{s.Key} (default {Show(s.Default)}, {s.Where})");
            Assert.Empty(missing);
        }

        [Fact]
        public void ExampleJson_HasTheCodeDefaults()
        {
            var different = Settings.Where(s => Json.ContainsKey(s.Key) && !Structured.Contains(s.Key) && s.Key != "GameDirectory")
                .Where(s => Normalise(Text(Json[s.Key])) != Normalise(s.Default))
                .Select(s => $"{s.Key}: json {Show(Text(Json[s.Key]))}, code {Show(s.Default)} ({s.Where})");
            Assert.Empty(different);
        }

        [Fact]
        public void ExampleJson_HasNoUnreadKeys()
        {
            var unread = Json.Keys.Where(k => !Code.ContainsKey(k)).Select(k => k);
            Assert.Empty(unread);
        }

        [Fact]
        public void EverySetting_IsInTheWorldCompose()
        {
            var missing = Settings.Where(s => !NotInDocker.ContainsKey(s.Key))
                .Where(s => !Compose.Keys.Concat(GatewayOverride.Keys).Any(k => k == s.Key || k.StartsWith(s.Key + "__")))
                .Select(s => $"{s.Key} (default {Show(s.Default)}, {s.Where})");
            Assert.Empty(missing);
        }

        [Fact]
        public void WorldCompose_DefaultsAreTheCodeDefaults()
        {
            var different = new List<string>();
            foreach (ComposeLine line in Compose.Values)
            {
                string key = line.Key.Split("__")[0];
                if (!Code.TryGetValue(key, out CodeSetting setting) || DockerFixed.ContainsKey(key) || Structured.Contains(key))
                    continue;
                string docker = line.Variable == null ? line.Default : line.Default;
                if (Normalise(docker) != Normalise(setting.Default))
                    different.Add($"{line.Key}: compose {line.Raw} → {Show(docker)}, code {Show(setting.Default)} ({setting.Where})");
            }
            Assert.Empty(different);
        }

        /// <summary>Environment variables the world reads itself (not through the configuration binder).</summary>
        private static readonly HashSet<string> RawEnvironment = new() { "LANDMARK_SEASON", "HAT_DROPS", "ZONE_INTERNAL_PORT" };

        [Fact]
        public void WorldCompose_SetsNothingTheCodeIgnores()
        {
            var unread = Compose.Values.Concat(GatewayOverride.Values).Where(l => !Code.ContainsKey(l.Key.Split("__")[0]) && !RawEnvironment.Contains(l.Key)
                    && !Structured.Contains(l.Key.Split("__")[0]))
                .Select(l => $"{l.Key}: {l.Raw}");
            Assert.Empty(unread);
        }

        [Fact]
        public void RawEnvironmentVariables_AreReadByTheWorld()
        {
            string source = string.Join("\n", Directory.EnumerateFiles(Repo.PathOf("LandmarkEmulator.WorldServer"), "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));
            var unread = RawEnvironment.Where(v => !source.Contains($"GetEnvironmentVariable(\"{v}\")"));
            Assert.Empty(unread);
        }

        [Fact]
        public void WorldCompose_VariablesAreInEnvExample_WithTheSameDefault()
        {
            var problems = new List<string>();
            foreach (ComposeLine line in Compose.Values.Where(l => l.Variable != null))
            {
                if (!EnvExample.TryGetValue(line.Variable, out string example))
                {
                    problems.Add($"{line.Variable} ({line.Key}) is not in .env.example (compose default {Show(line.Default)})");
                    continue;
                }
                // An empty .env line means "the compose default" only with ":-" (empty counts as unset).
                if (example.Length == 0 && line.DefaultIfEmpty)
                    continue;
                if (Normalise(example) != Normalise(line.Default))
                    problems.Add($"{line.Variable}: .env.example {Show(example)}, compose default {Show(line.Default)} ({line.Key})");
            }
            Assert.Empty(problems);
        }

        /// <summary>Every variable .env.example documents is used by some compose file of the project.</summary>
        [Fact]
        public void EnvExample_HasNoUnusedVariables()
        {
            string composeText = string.Join("\n", Directory.EnumerateFiles(Repo.PathOf("docker"), "*.yml", SearchOption.AllDirectories)
                .Append(Repo.PathOf("docker-compose.yml")).Select(File.ReadAllText));
            // Read by docker compose itself, or by scripts beside the compose files.
            var compose = new HashSet<string> { "COMPOSE_PROFILES" };
            string scripts = string.Join("\n", Directory.EnumerateFiles(Repo.PathOf("docker"), "*.sh", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(Repo.Root, "*.ps1")).Concat(Directory.EnumerateFiles(Repo.Root, "*.bat")).Select(File.ReadAllText));
            var unused = EnvExample.Keys.Where(v => !compose.Contains(v) && !Regex.IsMatch(composeText, @"\$\{" + v + @"\b")
                && !scripts.Contains(v));
            Assert.Empty(unused);
        }

        private static string Show(string value) => value.Length == 0 ? "(empty)" : value;
    }
}
