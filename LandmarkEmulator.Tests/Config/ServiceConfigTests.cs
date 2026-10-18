using LandmarkEmulator.Tests.Support;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;
using static LandmarkEmulator.Tests.Config.ConfigSources;

namespace LandmarkEmulator.Tests.Config
{
    /// <summary>
    /// The services' settings (commerce, social, Environment Server, world channel, gateway): every key their Docker service
    /// sets is a setting the service reads, its Docker default is the code's default (deployment wiring aside: addresses,
    /// paths, databases, ids), every setting is in the service's example JSON with the code's default, and the .env
    /// variables they use are in .env.example with the same default.
    /// </summary>
    public class ServiceConfigTests
    {
        public sealed record Service(string Name, Type Config, string Json, string Compose, string ComposeService)
        {
            public override string ToString() => Name;
        }

        public static IEnumerable<object[]> Services => new[]
        {
            new Service("commerce", typeof(LandmarkEmulator.CommerceServer.CommerceServerConfiguration), "LandmarkEmulator.CommerceServer/CommerceServer.example.json", "docker/commerce/docker-compose.yml", "commerce"),
            new Service("social", typeof(LandmarkEmulator.SocialServer.SocialServerConfiguration), "LandmarkEmulator.SocialServer/SocialServer.example.json", "docker/social/docker-compose.yml", "social"),
            new Service("envserver", typeof(LandmarkEmulator.EnvServer.EnvServerConfiguration), "LandmarkEmulator.EnvServer/EnvServer.example.json", "docker/envserver/docker-compose.yml", "envserver"),
            new Service("worldchannel", typeof(LandmarkEmulator.WorldChannel.WorldChannelConfiguration), "LandmarkEmulator.WorldChannel/WorldChannel.example.json", "docker/worldchannel/docker-compose.yml", "worldchannel"),
            new Service("gateway", typeof(LandmarkEmulator.GatewayServer.GatewayServerConfiguration), "LandmarkEmulator.GatewayServer/GatewayServer.example.json", "docker/gateway/docker-compose.yml", "gateway"),
        }.Select(s => new object[] { s });

        /// <summary>Deployment wiring: the container's value differs from a plain run's by design.</summary>
        private static readonly HashSet<string> Wiring = new()
        {
            "Network", "Internal", "Database", "DataPath", "GameDirectory", "GameDataPath", "HealthFile", "ImportFrom", "ImportRealm",
            "ImportWorldId", "LoginInternalAddress", "GatewayInternalAddress", "ZoneInternalAddress", "WorldId", "EncryptionKey",
        };

        private static Dictionary<string, (string Default, Type Type)> Settings(Service s)
        {
            object defaults = Activator.CreateInstance(s.Config);
            return s.Config.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite)
                .ToDictionary(p => p.Name, p => (Text(p.GetValue(defaults)), p.PropertyType));
        }

        private static Dictionary<string, ComposeLine> ComposeOf(Service s) => ReadCompose(Repo.PathOf(s.Compose.Split('/')), s.ComposeService);

        [Theory]
        [MemberData(nameof(Services))]
        public void Compose_SetsOnlySettingsTheServiceReads(Service s)
        {
            var settings = Settings(s);
            var compose = ComposeOf(s);
            Assert.NotEmpty(compose, $"{s.Compose}: no environment parsed for '{s.ComposeService}'");
            var unread = compose.Keys.Where(k => !settings.ContainsKey(k.Split("__")[0])).Select(k => $"{k}: {compose[k].Raw}");
            Assert.Empty(unread);
        }

        [Theory]
        [MemberData(nameof(Services))]
        public void Compose_DefaultsAreTheCodeDefaults(Service s)
        {
            var settings = Settings(s);
            var different = ComposeOf(s).Values
                .Where(l => settings.ContainsKey(l.Key) && !Wiring.Contains(l.Key))
                .Where(l => Normalise(l.Default) != Normalise(settings[l.Key].Default))
                .Select(l => $"{l.Key}: compose {l.Raw}, code {settings[l.Key].Default}");
            Assert.Empty(different);
        }

        [Theory]
        [MemberData(nameof(Services))]
        public void ExampleJson_ListsEverySetting_WithTheCodeDefault(Service s)
        {
            var settings = Settings(s);
            var json = ReadJson(Repo.PathOf(s.Json.Split('/')));
            var problems = new List<string>();
            foreach ((string key, (string value, Type _)) in settings)
            {
                if (!json.TryGetValue(key, out var element))
                    problems.Add($"{key} (default {value}) is not in {s.Json}");
                else if (!Wiring.Contains(key) && element.ValueKind != System.Text.Json.JsonValueKind.Object
                    && Normalise(Text(element)) != Normalise(value))
                    problems.Add($"{key}: json {Text(element)}, code {value}");
            }
            problems.AddRange(json.Keys.Where(k => !settings.ContainsKey(k)).Select(k => $"{k} in {s.Json} is not a setting"));
            Assert.Empty(problems);
        }

        [Theory]
        [MemberData(nameof(Services))]
        public void ComposeVariables_AreInEnvExample_WithTheSameDefault(Service s)
        {
            var problems = new List<string>();
            foreach (ComposeLine line in ComposeOf(s).Values.Where(l => l.Variable != null))
            {
                if (!EnvExample.TryGetValue(line.Variable, out string example))
                    problems.Add($"{line.Variable} ({line.Key}) is not in .env.example");
                else if (!(example.Length == 0 && line.DefaultIfEmpty) && Normalise(example) != Normalise(line.Default))
                    problems.Add($"{line.Variable}: .env.example '{example}', compose default '{line.Default}'");
            }
            Assert.Empty(problems);
        }
    }
}
