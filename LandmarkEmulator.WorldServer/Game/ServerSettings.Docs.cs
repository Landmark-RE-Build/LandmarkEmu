using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// What the settings page says about each setting, read from the project's own files (embedded in this assembly at
    /// build time, see the .csproj) so the page can never drift from them:
    /// <list type="bullet">
    /// <item>.env.example: its sections ("# ---- Your world ----") are the page's groups, and the notes above each
    /// variable are its help text;</item>
    /// <item>docker/world/docker-compose.yml (and docker/gateway/world.override.yml): the world service's environment
    /// says which .env variable feeds which WorldServer.json name (<c>FreeBuild: ${FREE_BUILD:-false}</c>).</item>
    /// </list>
    /// </summary>
    public static class ServerSettingsDocs
    {
        /// <summary>One .env.example variable: its section, its help text, its example value.</summary>
        public sealed record EnvVariable(string Name, string Section, string Help, string Example);

        public const string EnvExampleResource = "Settings.env.example";
        public const string WorldComposeResource = "Settings.world-compose.yml";
        public const string GatewayOverrideResource = "Settings.gateway-override.yml";

        private static IReadOnlyDictionary<string, EnvVariable> env;
        private static IReadOnlyDictionary<string, IReadOnlyList<string>> compose;

        /// <summary>The variables of the embedded .env.example by name.</summary>
        public static IReadOnlyDictionary<string, EnvVariable> Env => env ??= ParseEnvExample(Resource(EnvExampleResource));

        /// <summary>The world service's configuration keys ("FreeBuild", "Network:Port") and the .env variables each reads.</summary>
        public static IReadOnlyDictionary<string, IReadOnlyList<string>> Compose => compose ??= Merge(
            ParseCompose(Resource(WorldComposeResource), "world"), ParseCompose(Resource(GatewayOverrideResource), "world"));

        private static string Resource(string name)
        {
            using Stream stream = typeof(ServerSettingsDocs).Assembly.GetManifestResourceStream(name);
            if (stream == null)
                return "";
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private static readonly Regex Section = new(@"^#\s*-{2,}\s*(.+?)\s*-{2,}\s*$");
        private static readonly Regex Variable = new(@"^#?([A-Z][A-Z0-9_]*)=(.*)$");

        /// <summary>
        /// Parses .env.example. A variable's help is the block of note lines right above it; a variable with no note of
        /// its own right above (several variables under one note) shares the note of the variable before it. A blank line
        /// ends a note. "#NAME=value" lines are commented-out variables (the Advanced section), documented the same way.
        /// </summary>
        public static IReadOnlyDictionary<string, EnvVariable> ParseEnvExample(string text)
        {
            var result = new Dictionary<string, EnvVariable>(StringComparer.Ordinal);
            string section = "";
            var block = new List<string>();
            string lastHelp = "";
            foreach (string raw in (text ?? "").Split('\n'))
            {
                string line = raw.TrimEnd('\r').TrimEnd();
                Match m;
                if (line.Length == 0)
                {
                    block.Clear();
                    lastHelp = "";
                    continue;
                }
                if ((m = Section.Match(line)).Success)
                {
                    section = m.Groups[1].Value;
                    block.Clear();
                    lastHelp = "";
                    continue;
                }
                if ((m = Variable.Match(line)).Success)
                {
                    string help = block.Count > 0 ? string.Join(" ", block) : lastHelp;
                    if (!result.ContainsKey(m.Groups[1].Value))
                        result[m.Groups[1].Value] = new EnvVariable(m.Groups[1].Value, section, help, m.Groups[2].Value.Trim());
                    lastHelp = help;
                    block.Clear();
                    continue;
                }
                if (line.StartsWith('#'))
                    block.Add(line.TrimStart('#').Trim());
            }
            return result;
        }

        private static readonly Regex Reference = new(@"\$\{([A-Za-z_][A-Za-z0-9_]*)");

        /// <summary>
        /// The environment block of one service in a compose file: each key (Docker's "__" written as the configuration's
        /// ":") with the ${VARIABLES} its value reads, in order (none for a fixed value).
        /// </summary>
        public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseCompose(string text, string service)
        {
            var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            bool inService = false, inEnvironment = false;
            foreach (string raw in (text ?? "").Split('\n'))
            {
                string line = raw.TrimEnd('\r').TrimEnd();
                if (Regex.IsMatch(line, @"^  \w[\w-]*:"))
                {
                    inService = line.Trim().TrimEnd(':') == service || line.Trim().StartsWith(service + ":");
                    inEnvironment = false;
                    continue;
                }
                if (!inService)
                    continue;
                if (Regex.IsMatch(line, @"^    \w+:"))
                {
                    inEnvironment = line.Trim() == "environment:";
                    continue;
                }
                if (!inEnvironment || line.TrimStart().StartsWith('#') || line.Trim().Length == 0)
                    continue;
                Match m = Regex.Match(line, @"^\s+([A-Za-z_]\w*):\s*(.*)$");
                if (!m.Success)
                    continue;
                string key = m.Groups[1].Value.Replace("__", ":");
                result[key] = Reference.Matches(m.Groups[2].Value).Select(r => r.Groups[1].Value).Distinct().ToList();
            }
            return result;
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<string>> Merge(params IReadOnlyDictionary<string, IReadOnlyList<string>>[] parts)
        {
            var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in parts)
                foreach (var (key, value) in part)
                    if (!result.ContainsKey(key))
                        result[key] = value;
            return result;
        }
    }
}
