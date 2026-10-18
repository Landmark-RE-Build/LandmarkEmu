using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.Shared.Configuration
{
    public static class SharedConfiguration
    {
        public static IConfiguration Configuration { get; private set; }

        /// <summary>
        /// The settings-page overrides, the last (winning) source of <see cref="Configuration"/>; null for a server that
        /// has none (<see cref="Initialise(string, Func{IConfiguration, IEnumerable{KeyValuePair{string, string}}})"/>).
        /// </summary>
        public static OverridesConfigurationProvider Overrides { get; private set; }

        public static void Initialise(string file) => Initialise(file, null);

        /// <summary>
        /// Builds the configuration: the JSON file, then environment variables, then the command line, each winning over
        /// the one before. With <paramref name="overrides"/>, its pairs (computed from that configuration, e.g. read from a
        /// file whose path is a setting) are added last, so they win over everything (<see cref="Overrides"/>).
        /// </summary>
        public static void Initialise(string file, Func<IConfiguration, IEnumerable<KeyValuePair<string, string>>> overrides)
        {
            if (Configuration != null)
                return;

            IConfigurationBuilder Base() => new ConfigurationBuilder()
                .AddJsonFile(file, false, true)
                .AddEnvironmentVariables()
                .AddCommandLine(Environment.GetCommandLineArgs().Skip(1).ToArray());

            if (overrides == null)
            {
                Configuration = Base().Build();
                return;
            }
            IConfigurationRoot below = Base().Build();
            var pairs = overrides(below)?.ToList() ?? new List<KeyValuePair<string, string>>();
            (below as IDisposable)?.Dispose();
            Overrides = new OverridesConfigurationProvider(pairs);
            Configuration = Base().Add(new OverridesConfigurationSource(Overrides)).Build();
        }
    }
}
