using LandmarkEmulator.Shared.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using NLog;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>One setting the page shows: its configuration key, type, rule, default, .env names, group and help.</summary>
    public sealed class SettingDefinition
    {
        /// <summary>The configuration key: the WorldServer.json name, nested with ":" ("Network:Port"); an environment-only setting by its variable.</summary>
        public string Key { get; init; }
        public Type Type { get; init; }
        /// <summary>The WorldServerConfiguration property path (null for a setting read straight from the configuration).</summary>
        public PropertyInfo[] Path { get; init; }
        public SettingRule Rule { get; init; }
        /// <summary>The code's default as text (null = none).</summary>
        public string Default { get; init; }
        public IReadOnlyList<string> EnvNames { get; init; }
        public string Group { get; init; }
        public string Help { get; init; }
        /// <summary>Order in .env.example (settings with no .env line after those with one).</summary>
        public int Order { get; init; }

        public SettingAccess Access => ServerSettings.LooksSecret(Key) ? SettingAccess.Secret : Rule.Access;
        public bool Editable => Access == SettingAccess.Editable;
    }

    /// <summary>A value set on the settings page: the value (canonical text), who set it and when (UTC).</summary>
    public sealed record SettingOverride(string Value, string By, DateTime At);

    /// <summary>
    /// The admin Settings page's model (/admin/settings, <see cref="ServerSettingsPage"/>): every world setting, grouped
    /// and explained (<see cref="ServerSettingsDocs"/>, <see cref="Rules"/>), and the server owner's changes.
    /// <para><b>Where changes go.</b> The world container can't rewrite the host's .env, so a change made on the page is
    /// stored in an overrides file in the world's data folder (setting SettingsOverridesFile; Docker
    /// /data/settings-overrides.json = server-data/worlds/&lt;WORLD_KEY&gt;/settings-overrides.json). It is the last
    /// configuration source (<see cref="SharedConfiguration.Overrides"/>): code default ← WorldServer.json ← environment
    /// (.env through docker compose) ← command line ← the page. "Reset to .env" removes a setting's override.</para>
    /// <para>The file is read at start-up (only settings the page may change, with valid values; anything else in it is
    /// logged and ignored) and written atomically (a temporary file, then a rename).</para>
    /// </summary>
    public static partial class ServerSettings
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const string DefaultFileName = "settings-overrides.json";
        public const string NoEnvGroup = "Not in .env (WorldServer.json only)";
        public const int MaxChangesPerSave = 300;

        private static IReadOnlyList<SettingDefinition> definitions;
        private static Dictionary<string, SettingDefinition> byKey;

        /// <summary>Every setting: each WorldServerConfiguration leaf property (nested classes flattened) and each raw rule.</summary>
        public static IReadOnlyList<SettingDefinition> Definitions
        {
            get
            {
                if (definitions == null)
                {
                    List<SettingDefinition> built = Build();
                    byKey = built.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);
                    definitions = built;
                }
                return definitions;
            }
        }

        public static SettingDefinition Find(string key)
        {
            _ = Definitions;
            return key != null && byKey.TryGetValue(key, out SettingDefinition d) ? d : null;
        }

        private static bool IsLeaf(Type t) => t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal)
            || typeof(IEnumerable).IsAssignableFrom(t);

        private static List<SettingDefinition> Build()
        {
            var leaves = new List<(string Key, Type Type, PropertyInfo[] Path, string Default)>();
            void Walk(Type type, object defaults, string prefix, PropertyInfo[] path)
            {
                foreach (PropertyInfo p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite && p.CanRead))
                {
                    string key = prefix + p.Name;
                    PropertyInfo[] here = path.Append(p).ToArray();
                    object value = defaults == null ? null : p.GetValue(defaults);
                    if (IsLeaf(p.PropertyType))
                        leaves.Add((key, p.PropertyType, here, value == null ? null : Text(value)));
                    else
                        Walk(p.PropertyType, value, key + ":", here);
                }
            }
            Walk(typeof(WorldServerConfiguration), new WorldServerConfiguration(), "", Array.Empty<PropertyInfo>());
            foreach (SettingRule raw in Rules.Where(r => r.RawType != null))
                if (!leaves.Any(l => l.Key.Equals(raw.Key, StringComparison.OrdinalIgnoreCase)))
                    leaves.Add((raw.Key, raw.RawType, null, raw.RawDefault));

            IReadOnlyDictionary<string, IReadOnlyList<string>> compose = ServerSettingsDocs.Compose;
            IReadOnlyDictionary<string, ServerSettingsDocs.EnvVariable> env = ServerSettingsDocs.Env;
            var order = env.Keys.Select((name, i) => (name, i)).ToDictionary(x => x.name, x => x.i);
            var result = new List<SettingDefinition>();
            foreach (var (key, type, path, def) in leaves)
            {
                SettingRule rule = RuleFor(key) ?? new SettingRule(key, SettingAccess.ReadOnly, SettingApply.Restart)
                {
                    Note = "This setting has no entry in the settings table yet (ServerSettings.Table.cs): shown read-only."
                };
                // The .env variables: the compose line of this key, or of its parents / children (Database's connection
                // strings name DB_PASSWORD), and the rule's hints (volumes).
                var names = new List<string>();
                foreach (var (composeKey, vars) in compose)
                    if (composeKey.Equals(key, StringComparison.OrdinalIgnoreCase)
                        || composeKey.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase)
                        || key.StartsWith(composeKey + ":", StringComparison.OrdinalIgnoreCase))
                        names.AddRange(vars);
                names.AddRange(rule.EnvHints ?? Array.Empty<string>());
                names = names.Distinct().ToList();
                ServerSettingsDocs.EnvVariable first = names.Select(n => env.GetValueOrDefault(n)).FirstOrDefault(v => v != null);
                string help = string.Join(" ", names.Select(n => env.GetValueOrDefault(n)?.Help).Where(h => !string.IsNullOrEmpty(h)).Distinct());
                result.Add(new SettingDefinition
                {
                    Key = key, Type = type, Path = path, Rule = rule, Default = def, EnvNames = names,
                    Group = first?.Section is { Length: > 0 } section ? section : NoEnvGroup,
                    Help = help,
                    Order = first != null ? order[first.Name] : int.MaxValue,
                });
            }
            return result;
        }

        // ---- Values as text -----------------------------------------------------------------------------------------------

        /// <summary>A value as the page and the overrides file write it (invariant numbers, "true" / "false").</summary>
        public static string Text(object value) => value switch
        {
            null => null,
            bool b => b ? "true" : "false",
            float f => f.ToString("R", CultureInfo.InvariantCulture),
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            string s => s,
            IDictionary dictionary => string.Join(",", dictionary.Keys.Cast<object>().Select(k => $"{k}={Text(dictionary[k])}")),
            IEnumerable list => string.Join(", ", list.Cast<object>().Select(Text)),
            IFormattable n => n.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

        public static bool IsBool(Type t) => t == typeof(bool);
        public static bool IsInteger(Type t) => t == typeof(int) || t == typeof(uint) || t == typeof(long) || t == typeof(ulong)
            || t == typeof(short) || t == typeof(ushort) || t == typeof(byte);
        public static bool IsReal(Type t) => t == typeof(float) || t == typeof(double) || t == typeof(decimal);

        private static (double Min, double Max) TypeRange(Type t) => t == typeof(int) ? (int.MinValue, int.MaxValue)
            : t == typeof(uint) ? (0, uint.MaxValue) : t == typeof(short) ? (short.MinValue, short.MaxValue)
            : t == typeof(ushort) ? (0, ushort.MaxValue) : t == typeof(byte) ? (0, 255) : t == typeof(ulong) ? (0, ulong.MaxValue)
            : t == typeof(float) ? (float.MinValue, float.MaxValue) : (double.MinValue, double.MaxValue);

        /// <summary>
        /// Checks a value typed on the page and gives it in the canonical form stored and bound (types, ranges, choices,
        /// formats, length; no control characters). Null error = valid.
        /// </summary>
        public static bool TryNormalise(SettingDefinition def, string input, out string canonical, out string error)
        {
            canonical = null;
            error = null;
            SettingRule rule = def.Rule;
            string value = (input ?? "").Trim();
            if (value.Any(c => char.IsControl(c)))
            {
                error = $"{def.Key}: line breaks and control characters are not allowed.";
                return false;
            }
            if (value.Length > rule.MaxLength)
            {
                error = $"{def.Key}: at most {rule.MaxLength} characters.";
                return false;
            }
            Type t = def.Type;
            if (IsBool(t))
            {
                switch (value.ToLowerInvariant())
                {
                    case "true" or "on" or "1" or "yes": canonical = "true"; return true;
                    case "false" or "off" or "0" or "no": canonical = "false"; return true;
                    default: error = $"{def.Key}: true or false."; return false;
                }
            }
            if (IsInteger(t) || IsReal(t))
            {
                if (!double.TryParse(value, IsInteger(t) ? NumberStyles.Integer : NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                    || !double.IsFinite(d))
                {
                    error = $"{def.Key}: a {(IsInteger(t) ? "whole number" : "number")} is needed (e.g. {(IsInteger(t) ? "10" : "1.5")}).";
                    return false;
                }
                (double lo, double hi) = TypeRange(t);
                lo = Math.Max(lo, rule.Min ?? lo);
                hi = Math.Min(hi, rule.Max ?? hi);
                if (d < lo || d > hi)
                {
                    error = $"{def.Key}: between {Invariant(lo)} and {Invariant(hi)}.";
                    return false;
                }
                if (IsInteger(t))
                    canonical = t == typeof(ulong) ? ulong.Parse(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
                        : ((long)d).ToString(CultureInfo.InvariantCulture);
                else
                    canonical = t == typeof(float) ? ((float)d).ToString("R", CultureInfo.InvariantCulture) : d.ToString("R", CultureInfo.InvariantCulture);
                return true;
            }
            if (t != typeof(string))
            {
                error = $"{def.Key} can't be changed here (its type is {t.Name}).";
                return false;
            }
            if (rule.Choices != null)
            {
                canonical = rule.Choices.FirstOrDefault(c => c.Equals(value, StringComparison.OrdinalIgnoreCase));
                if (canonical == null)
                {
                    error = $"{def.Key}: one of {string.Join(", ", rule.Choices)}.";
                    return false;
                }
                return true;
            }
            if (rule.Format != null && value.Length > 0 && !rule.Format.IsMatch(value))
            {
                error = $"{def.Key}: {rule.FormatHint ?? "not in the expected form"}.";
                return false;
            }
            if (rule.Check?.Invoke(value) is string problem)
            {
                error = $"{def.Key}: {problem}";
                return false;
            }
            canonical = value;
            return true;
        }

        /// <summary>Canonical text → the property's type (for setting a running value).</summary>
        internal static object Convert(Type t, string text)
        {
            if (text == null)
                return t.IsValueType ? Activator.CreateInstance(t) : null;
            if (t == typeof(string))
                return text;
            if (t == typeof(bool))
                return bool.Parse(text.Trim());
            if (t.IsEnum)
                return Enum.Parse(t, text.Trim(), true);
            if (t == typeof(ulong))
                return ulong.Parse(text.Trim(), CultureInfo.InvariantCulture);
            double d = double.Parse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
            return IsInteger(t) ? System.Convert.ChangeType((long)d, t, CultureInfo.InvariantCulture)
                : System.Convert.ChangeType(d, t, CultureInfo.InvariantCulture);
        }

        // ---- The overrides file -------------------------------------------------------------------------------------------

        private static readonly JsonSerializerOptions fileJson = new() { WriteIndented = true };

        private sealed class FileModel
        {
            public string Comment { get; set; }
            public Dictionary<string, FileEntry> Settings { get; set; }
        }

        private sealed class FileEntry
        {
            public string Value { get; set; }
            public string By { get; set; }
            public DateTime At { get; set; }
        }

        /// <summary>
        /// Reads an overrides file: only settings the page may change, with valid values (each problem is listed in
        /// <paramref name="problems"/> and the entry left out). A missing file is no overrides.
        /// </summary>
        public static Dictionary<string, SettingOverride> ReadFile(string path, List<string> problems)
        {
            var result = new Dictionary<string, SettingOverride>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return result;
            FileModel model;
            try
            {
                model = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(path), fileJson);
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
            {
                problems?.Add($"{path} could not be read ({e.Message}); no page settings applied.");
                return result;
            }
            foreach (var (key, entry) in model?.Settings ?? new Dictionary<string, FileEntry>())
            {
                SettingDefinition def = Find(key);
                if (def == null)
                    problems?.Add($"{key}: not a setting; ignored.");
                else if (!def.Editable)
                    problems?.Add($"{key}: can't be set on the page ({def.Access}); ignored.");
                else if (!TryNormalise(def, entry?.Value, out string canonical, out string error))
                    problems?.Add($"{error} Ignored.");
                else
                    result[def.Key] = new SettingOverride(canonical, entry.By ?? "", entry.At);
            }
            return result;
        }

        /// <summary>Writes the overrides atomically: a temporary file beside it, flushed to disk, then renamed over it.</summary>
        public static void WriteFile(string path, IReadOnlyDictionary<string, SettingOverride> entries)
        {
            var model = new FileModel
            {
                Comment = "Written by the admin Settings page (/admin, Server settings). These values win over .env and WorldServer.json; "
                    + "remove an entry (or use \"Reset to .env\" on the page) to go back. Edit only while the world is stopped.",
                Settings = entries.OrderBy(e => e.Key, StringComparer.Ordinal)
                    .ToDictionary(e => e.Key, e => new FileEntry { Value = e.Value.Value, By = e.Value.By, At = e.Value.At }),
            };
            string folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
            string temp = path + ".tmp";
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(model, fileJson));
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }
            File.Move(temp, path, true);
        }

        // ---- Start-up -----------------------------------------------------------------------------------------------------

        /// <summary>The overrides file of a configuration: setting SettingsOverridesFile, else beside the program.</summary>
        public static string OverridesPath(IConfiguration config)
        {
            string path = config?.GetValue("SettingsOverridesFile", "");
            return string.IsNullOrWhiteSpace(path) ? System.IO.Path.Combine(AppContext.BaseDirectory, DefaultFileName) : path.Trim();
        }

        private static string startupPath;
        private static Dictionary<string, SettingOverride> startupSaved;

        /// <summary>
        /// Start-up (WorldServer.Main, through ConfigurationManager.Initialise): the page's saved values, read from the
        /// overrides file of the configuration below them, as the last configuration source.
        /// </summary>
        public static IEnumerable<KeyValuePair<string, string>> StartupOverrides(IConfiguration below)
        {
            startupPath = OverridesPath(below);
            var problems = new List<string>();
            startupSaved = ReadFile(startupPath, problems);
            foreach (string problem in problems)
                log.Warn($"Settings page overrides ({startupPath}): {problem}");
            if (startupSaved.Count > 0)
                log.Info($"Settings page overrides: {startupSaved.Count} from {startupPath} ({string.Join(", ", startupSaved.Keys)}); they win over .env.");
            return startupSaved.Select(e => new KeyValuePair<string, string>(e.Key, e.Value.Value)).ToList();
        }

        /// <summary>The running settings store (null until <see cref="Start"/>).</summary>
        public static SettingsStore Store { get; private set; }

        /// <summary>
        /// After the configuration is built: the store the page uses, and the environment-only settings (read by their
        /// classes from the process environment) set from their saved values.
        /// </summary>
        public static void Start()
        {
            Store = new SettingsStore(startupPath ?? OverridesPath(SharedConfiguration.Configuration),
                SharedConfiguration.Configuration as IConfigurationRoot, SharedConfiguration.Overrides,
                () => ConfigurationManager<WorldServerConfiguration>.Instance.Config, startupSaved);
            foreach (var (key, entry) in startupSaved ?? new Dictionary<string, SettingOverride>())
                if (Find(key) is { Rule.EnvironmentOnly: true } def)
                    def.Rule.Applier?.Invoke(entry.Value);
        }

        /// <summary>The source a provider stands for, as the page names it.</summary>
        internal static string SourceName(IConfigurationProvider provider) => provider switch
        {
            OverridesConfigurationProvider => "page",
            EnvironmentVariablesConfigurationProvider => ".env",
            JsonConfigurationProvider => "WorldServer.json",
            CommandLineConfigurationProvider => "command line",
            _ => provider.GetType().Name
        };
    }

    /// <summary>The page's running state: the saved overrides, what is in effect, and saving / applying changes.</summary>
    public sealed class SettingsStore
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private readonly object sync = new();
        private readonly IConfigurationRoot root;
        private readonly OverridesConfigurationProvider overrides;
        private readonly Func<WorldServerConfiguration> config;
        private Dictionary<string, SettingOverride> saved;

        public string FilePath { get; }

        public SettingsStore(string path, IConfigurationRoot root, OverridesConfigurationProvider overrides,
            Func<WorldServerConfiguration> config, IReadOnlyDictionary<string, SettingOverride> saved)
        {
            FilePath = path;
            this.root = root;
            this.overrides = overrides;
            this.config = config;
            this.saved = new Dictionary<string, SettingOverride>(saved ?? new Dictionary<string, SettingOverride>(), StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyDictionary<string, SettingOverride> Saved
        {
            get { lock (sync) return new Dictionary<string, SettingOverride>(saved, StringComparer.OrdinalIgnoreCase); }
        }

        /// <summary>The value in effect now, as text (a property of the running configuration, or the configuration's value).</summary>
        public string Running(SettingDefinition def)
        {
            if (def.Path != null)
            {
                object value = config();
                foreach (PropertyInfo p in def.Path)
                    value = value == null ? null : p.GetValue(value);
                return ServerSettings.Text(value);
            }
            return root?[def.Key] ?? def.Default;
        }

        /// <summary>The value without the page (the last of WorldServer.json / .env / command line that has it) and where it comes from.</summary>
        public (string Value, string Source) Below(SettingDefinition def)
        {
            if (root != null)
                foreach (IConfigurationProvider provider in root.Providers.Reverse())
                {
                    if (provider is OverridesConfigurationProvider)
                        continue;
                    if (provider.TryGet(def.Key, out string value))
                        return (def.Access == SettingAccess.Secret ? value : Same(def, value), ServerSettings.SourceName(provider));
                    if (provider.GetChildKeys(Enumerable.Empty<string>(), def.Key).Any())
                        return (null, ServerSettings.SourceName(provider));
                }
            return (def.Default, "default");
        }

        /// <summary>What the setting will be after a restart: the page's value, else the one below it.</summary>
        public string AfterRestart(SettingDefinition def)
        {
            lock (sync)
                if (saved.TryGetValue(def.Key, out SettingOverride entry))
                    return entry.Value;
            return Below(def).Value ?? def.Default;
        }

        private static string Same(SettingDefinition def, string value)
            => value != null && ServerSettings.TryNormalise(def, value, out string canonical, out _) ? canonical : value ?? "";

        /// <summary>An editable setting whose saved value differs from the one in effect: it needs a restart.</summary>
        public bool RestartPending(SettingDefinition def)
            => def.Editable && Same(def, AfterRestart(def)) != Same(def, Running(def));

        /// <summary>One setting changed by a save (texts as shown; from / to are the values in effect before / after).</summary>
        public sealed record Change(string Key, string From, string To, SettingApply Apply, bool Reset);

        public sealed record SaveResult(bool Ok, string Message, IReadOnlyList<string> Errors, IReadOnlyList<Change> Changes, Action ApplyNow);

        /// <summary>
        /// Saves page changes: <paramref name="set"/> (key → typed value) and <paramref name="reset"/> (keys going back to
        /// .env). All are checked first; one unknown, read-only or invalid entry refuses the whole save and nothing is
        /// written. Then the overrides file is written (atomically) and each change logged with who made it. The returned
        /// <see cref="SaveResult.ApplyNow"/> puts the "now" / "next login" changes into effect: run it on the world thread.
        /// </summary>
        public SaveResult Save(string admin, IReadOnlyDictionary<string, string> set, IEnumerable<string> reset)
        {
            set ??= new Dictionary<string, string>();
            List<string> resets = (reset ?? Enumerable.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (set.Count + resets.Count == 0)
                return new SaveResult(false, "Nothing to save.", Array.Empty<string>(), Array.Empty<Change>(), null);
            if (set.Count + resets.Count > ServerSettings.MaxChangesPerSave)
                return new SaveResult(false, $"Too many changes at once (at most {ServerSettings.MaxChangesPerSave}).", Array.Empty<string>(),
                    Array.Empty<Change>(), null);
            var errors = new List<string>();
            var values = new Dictionary<SettingDefinition, string>();
            foreach (var (key, value) in set)
            {
                SettingDefinition def = Check(key, errors);
                if (def == null)
                    continue;
                if (resets.Contains(def.Key, StringComparer.OrdinalIgnoreCase))
                    errors.Add($"{def.Key}: set and reset in one save.");
                else if (ServerSettings.TryNormalise(def, value, out string canonical, out string error))
                    values[def] = canonical;
                else
                    errors.Add(error);
            }
            var resetDefs = new List<SettingDefinition>();
            foreach (string key in resets)
                if (Check(key, errors) is SettingDefinition def)
                    resetDefs.Add(def);
            if (errors.Count > 0)
                return new SaveResult(false, "Nothing was saved: " + string.Join(" ", errors), errors, Array.Empty<Change>(), null);

            var changes = new List<Change>();
            lock (sync)
            {
                var next = new Dictionary<string, SettingOverride>(saved, StringComparer.OrdinalIgnoreCase);
                DateTime now = DateTime.UtcNow;
                foreach (var (def, canonical) in values)
                {
                    string before = next.TryGetValue(def.Key, out SettingOverride old) ? old.Value : Below(def).Value ?? def.Default;
                    if (old != null && old.Value == canonical)
                        continue;
                    next[def.Key] = new SettingOverride(canonical, admin ?? "", now);
                    changes.Add(new Change(def.Key, before ?? "", canonical, def.Rule.Apply, false));
                }
                foreach (SettingDefinition def in resetDefs)
                {
                    if (!next.Remove(def.Key, out SettingOverride old))
                        continue;
                    changes.Add(new Change(def.Key, old.Value, Below(def).Value ?? def.Default ?? "", def.Rule.Apply, true));
                }
                if (changes.Count == 0)
                    return new SaveResult(true, "No changes: those are the values already saved.", Array.Empty<string>(), changes, null);
                try
                {
                    ServerSettings.WriteFile(FilePath, next);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    log.Error($"Settings page: {FilePath} could not be written ({e.Message}); nothing changed.");
                    return new SaveResult(false, $"The settings file could not be written ({e.Message}); nothing changed.",
                        new[] { e.Message }, Array.Empty<Change>(), null);
                }
                saved = next;
            }
            foreach (Change c in changes)
                log.Info(c.Reset
                    ? $"Admin {admin} reset {c.Key} to .env ({Shown(c.From)} -> {Shown(c.To)}; {Applies(c.Apply)})."
                    : $"Admin {admin} set {c.Key} from {Shown(c.From)} to {Shown(c.To)} ({Applies(c.Apply)}).");
            List<Change> live = changes.Where(c => c.Apply != SettingApply.Restart).ToList();
            int restart = changes.Count - live.Count;
            string message = $"Saved {changes.Count} change(s)" + (live.Count > 0 ? $"; {live.Count} in effect now" : "")
                + (restart > 0 ? $"; {restart} after a restart of the world" : "") + ".";
            return new SaveResult(true, message, Array.Empty<string>(), changes, live.Count > 0 ? () => Apply(live) : null);
        }

        private static string Shown(string value) => string.IsNullOrEmpty(value) ? "(empty)" : $"\"{value}\"";

        private static string Applies(SettingApply apply) => apply switch
        {
            SettingApply.Now => "in effect now",
            SettingApply.NextLogin => "in effect now, players see it at their next login",
            _ => "after a restart"
        };

        private static SettingDefinition Check(string key, List<string> errors)
        {
            SettingDefinition def = ServerSettings.Find(key);
            if (def == null)
                errors.Add($"Unknown setting \"{(key?.Length > 64 ? key[..64] + "…" : key)}\".");
            else if (def.Access == SettingAccess.Secret)
                errors.Add($"{def.Key} is a secret: change it in .env.");
            else if (!def.Editable)
                errors.Add($"{def.Key} can't be changed here: {def.Rule.Note ?? "change it in .env."}");
            else
                return def;
            return null;
        }

        /// <summary>Puts changes that count now into effect (world thread): the configuration's value, the property, the applier.</summary>
        public void Apply(IEnumerable<Change> changes)
        {
            foreach (Change change in changes)
            {
                SettingDefinition def = ServerSettings.Find(change.Key);
                if (def == null || def.Rule.Apply == SettingApply.Restart)
                    continue;
                string value;
                if (change.Reset)
                {
                    overrides?.Remove(def.Key);
                    value = Below(def).Value ?? def.Default;
                }
                else
                {
                    overrides?.Set(def.Key, change.To);
                    value = change.To;
                }
                try
                {
                    if (def.Path is { Length: 1 } && config() is WorldServerConfiguration running)
                        def.Path[0].SetValue(running, ServerSettings.Convert(def.Type, value));
                    def.Rule.Applier?.Invoke(value);
                }
                catch (Exception e)
                {
                    log.Error($"Settings page: {def.Key} could not be applied now ({e.Message}); it counts after a restart.");
                }
            }
        }
    }
}
