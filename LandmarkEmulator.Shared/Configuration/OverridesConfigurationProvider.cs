using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace LandmarkEmulator.Shared.Configuration
{
    /// <summary>
    /// The last configuration source: values a server owner set on a settings page (the world's admin Settings page,
    /// WorldServer Game/ServerSettings), on top of the JSON file, the environment and the command line. Its values can be
    /// changed while the server runs (<see cref="Set"/>, <see cref="Remove"/>), so code that reads the configuration at use
    /// time (a GetValue call with the setting's name) sees a change at once. Thread-safe: the web threads write, every
    /// thread reads.
    /// </summary>
    public sealed class OverridesConfigurationProvider : ConfigurationProvider
    {
        public OverridesConfigurationProvider(IEnumerable<KeyValuePair<string, string>> initial)
        {
            var data = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in initial ?? Array.Empty<KeyValuePair<string, string>>())
                data[key] = value;
            Data = data;
        }

        /// <summary>Sets (or replaces) one override.</summary>
        public override void Set(string key, string value) => Data[key] = value;

        /// <summary>Removes one override (the value below it counts again).</summary>
        public void Remove(string key) => Data.Remove(key);

        /// <summary>The overrides in effect now, as key / value pairs.</summary>
        public IReadOnlyDictionary<string, string> Snapshot() => new Dictionary<string, string>(Data, StringComparer.OrdinalIgnoreCase);

        /// <summary>Loading is done by the owner of the values; nothing to read.</summary>
        public override void Load()
        {
        }
    }

    /// <summary>Adds an <see cref="OverridesConfigurationProvider"/> instance to a configuration builder.</summary>
    public sealed class OverridesConfigurationSource : IConfigurationSource
    {
        private readonly OverridesConfigurationProvider provider;

        public OverridesConfigurationSource(OverridesConfigurationProvider provider) => this.provider = provider;

        public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
    }
}
