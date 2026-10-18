using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LandmarkEmulator.Tests.Config
{
    /// <summary>
    /// The world server's settings as each place states them: the code (WorldServerConfiguration's property defaults and
    /// the direct <c>Configuration.GetValue("Key", default)</c> reads), WorldServer.example.json, the world service's
    /// environment in docker/world/docker-compose.yml (<c>Key: ${VAR:-default}</c>) and .env.example (<c>VAR=value</c>).
    /// </summary>
    public static class ConfigSources
    {
        /// <summary>A setting the code reads, with its default as text ("" for null).</summary>
        public sealed record CodeSetting(string Key, string Default, Type Type, string Where);

        /// <summary>One line of the world service's environment: key, the ${VAR} it reads (null = a fixed value), default.</summary>
        public sealed record ComposeLine(string Key, string Variable, string Default, bool DefaultIfEmpty, string Raw);

        private static Dictionary<string, CodeSetting> code;
        private static Dictionary<string, ComposeLine> compose;
        private static Dictionary<string, string> envExample;
        private static Dictionary<string, JsonElement> json;

        /// <summary>Nested objects / lists with their own shape (checked by the services that read them).</summary>
        public static readonly HashSet<string> Structured = new() { "Network", "Database", "Admins", "StoreLumenPrices" };

        public static Dictionary<string, CodeSetting> Code => code ??= ReadCode();
        public static Dictionary<string, ComposeLine> Compose => compose ??= ReadCompose(Repo.PathOf("docker", "world", "docker-compose.yml"), "world");
        /// <summary>What docker/gateway/world.override.yml adds to the world service when its gateway runs on its own.</summary>
        public static Dictionary<string, ComposeLine> GatewayOverride => gatewayOverride ??= ReadCompose(Repo.PathOf("docker", "gateway", "world.override.yml"), "world");
        private static Dictionary<string, ComposeLine> gatewayOverride;
        public static Dictionary<string, string> EnvExample => envExample ??= ReadEnv(Repo.PathOf(".env.example"));
        public static Dictionary<string, JsonElement> Json => json ??= ReadJson(Repo.PathOf("LandmarkEmulator.WorldServer", "WorldServer.example.json"));

        private static Dictionary<string, CodeSetting> ReadCode()
        {
            var settings = new Dictionary<string, CodeSetting>(StringComparer.Ordinal);
            var defaults = new WorldServerConfiguration();
            foreach (PropertyInfo p in typeof(WorldServerConfiguration).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite))
                settings[p.Name] = new CodeSetting(p.Name, Text(p.GetValue(defaults)), p.PropertyType, "WorldServerConfiguration");

            // Direct reads: Configuration.GetValue("Key", default) / config?.GetValue<T>("Key").
            var read = new Regex(@"(?:Configuration|config)\??\s*\.GetValue(?:<(\w+)>)?\(""([A-Z]\w+)""(?:,\s*([^)]+?))?\)");
            foreach (string file in Directory.EnumerateFiles(Repo.PathOf("LandmarkEmulator.WorldServer"), "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(Repo.PathOf("LandmarkEmulator.EnvServer.Core"), "*.cs", SearchOption.AllDirectories))
                .Concat(Directory.EnumerateFiles(Repo.PathOf("LandmarkEmulator.Shared"), "*.cs", SearchOption.AllDirectories)))
            {
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                    continue;
                foreach (Match m in read.Matches(File.ReadAllText(file)))
                {
                    string key = m.Groups[2].Value;
                    string literal = m.Groups[3].Success ? m.Groups[3].Value.Trim() : "";
                    string value = literal.Trim('"');
                    if (Regex.IsMatch(value, @"^-?[0-9.]+[fdmFDM]?$"))
                        value = value.TrimEnd('f', 'd', 'm', 'F', 'D', 'M');
                    Type type = literal.StartsWith('"') || m.Groups[1].Value == "string" || literal.Length == 0 ? typeof(string)
                        : literal is "true" or "false" ? typeof(bool) : typeof(double);
                    string where = Path.GetRelativePath(Repo.Root, file).Replace('\\', '/');
                    // A property default of null with a GetValue default (GameDataPath): the read's default is what applies.
                    if (!settings.TryGetValue(key, out CodeSetting existing) || existing.Default == "")
                        settings[key] = new CodeSetting(key, value, existing?.Type ?? type, where);
                    else if (Normalise(existing.Default) != Normalise(value))
                        settings[key + " (" + where + ")"] = new CodeSetting(key, value, type, where);
                }
            }
            return settings;
        }

        public static Dictionary<string, ComposeLine> ReadCompose(string path, string service)
        {
            var lines = new Dictionary<string, ComposeLine>(StringComparer.Ordinal);
            bool inService = false, inEnvironment = false;
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.TrimEnd();
                if (Regex.IsMatch(line, @"^  \w[\w-]*:$"))
                {
                    inService = line.Trim() == service + ":";
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
                Match m = Regex.Match(line, @"^\s+([A-Za-z_][\w]*):\s*(.*)$");
                if (!m.Success)
                    continue;
                string key = m.Groups[1].Value, value = m.Groups[2].Value.Trim().Trim('"');
                Match v = Regex.Match(value, @"^\$\{(\w+)(:?-)([^}]*)\}$");
                lines[key] = v.Success
                    ? new ComposeLine(key, v.Groups[1].Value, v.Groups[3].Value, v.Groups[2].Value == ":-", value)
                    : new ComposeLine(key, null, value, false, value);
            }
            return lines;
        }

        /// <summary>.env.example: VAR=value lines, and "#VAR=value" lines (documented but commented out).</summary>
        public static Dictionary<string, string> ReadEnv(string path)
        {
            var env = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string raw in File.ReadLines(path))
            {
                Match m = Regex.Match(raw.Trim(), @"^#?\s?([A-Z][A-Z0-9_]*)=(.*)$");
                if (m.Success && !env.ContainsKey(m.Groups[1].Value))
                    env[m.Groups[1].Value] = m.Groups[2].Value.Trim();
            }
            return env;
        }

        public static Dictionary<string, JsonElement> ReadJson(string path)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.EnumerateObject().Where(p => !p.Name.StartsWith("__"))
                .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
        }

        public static string Text(object value) => value switch
        {
            null => "",
            bool b => b ? "true" : "false",
            float f => f.ToString("R", CultureInfo.InvariantCulture),
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            IFormattable n => n.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

        public static string Text(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "",
            _ => value.GetRawText()
        };

        /// <summary>Compares settings as the binder would read them: case-insensitive bools, numbers by value.</summary>
        public static string Normalise(string value)
        {
            value = (value ?? "").Trim();
            if (bool.TryParse(value, out bool b))
                return b ? "true" : "false";
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                return d.ToString("R", CultureInfo.InvariantCulture);
            return value;
        }
    }
}
