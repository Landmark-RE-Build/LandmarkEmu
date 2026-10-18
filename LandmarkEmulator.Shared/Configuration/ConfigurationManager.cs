using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;

namespace LandmarkEmulator.Shared.Configuration
{
    public sealed class ConfigurationManager<T> : Singleton<ConfigurationManager<T>>
    {
        public T Config { get; private set; }

        private ConfigurationManager()
        {
        }

        public void Initialise(string file) => Initialise(file, null);

        /// <summary>
        /// Reads <typeparamref name="T"/> from the JSON file, the environment, the command line and (when given) the
        /// overrides computed from those (<see cref="SharedConfiguration.Initialise(string, Func{IConfiguration, IEnumerable{KeyValuePair{string, string}}})"/>).
        /// </summary>
        public void Initialise(string file, Func<IConfiguration, IEnumerable<KeyValuePair<string, string>>> overrides)
        {
            SharedConfiguration.Initialise(file, overrides);
            Config = SharedConfiguration.Configuration.Get<T>();
        }
    }
}
