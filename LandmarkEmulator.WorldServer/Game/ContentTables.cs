using LandmarkEmulator.Shared.Configuration;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Tunable content tables (AGENTS.md #22 / conventions 2026-10-28): baked defaults under
    /// <c>Resources/Content/</c>, live overrides under the world data folder <c>content/</c>
    /// (<c>server-data/worlds/&lt;WORLD_KEY&gt;/content/</c> → container <c>/data/content/</c>).
    /// Precedence: baked &lt; host override. <see cref="Reload"/> swaps atomically; a bad or missing
    /// override keeps the previous good table. Seed copies baked loot JSON into an empty override
    /// <c>loot/</c> once without overwriting host edits.
    /// </summary>
    public static class ContentTables
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private static readonly JsonSerializerOptions jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static readonly object gate = new();
        private static Snapshot current = Snapshot.Empty;

        /// <summary>Test hook: when set, <see cref="BakedRoot"/> / <see cref="OverrideRoot"/> return these paths.</summary>
        internal static string BakedRootOverride, OverrideRootOverride;

        /// <summary>One rolled loot line: item definition id and inclusive count range.</summary>
        public readonly record struct LootRange(uint Item, uint Min, uint Max);

        /// <summary>Optional chance roll added after resource pool picks (chests).</summary>
        public readonly record struct ChanceLoot(uint Item, double Chance, uint Min, uint Max);

        public sealed class KillExtras
        {
            public uint CampfireItem { get; init; } = 3894;
            public double CampfireChance { get; init; } = 0.05;
            public double LumenChance { get; init; } = 0.15;
            public uint LumenMin { get; init; } = 1;
            public uint LumenMax { get; init; } = 3;
            public double RecipeChance { get; init; } = 0.01;
            public double FungusRareChance { get; init; } = 0.03;
        }

        public sealed class CavernResources
        {
            public uint[] Uncommon { get; init; } = { 36, 37, 39, 49, 50 };
            public uint[] Rare { get; init; } = { 56, 58, 59 };
            public double RareChance { get; init; } = 0.08;
            public uint UncommonMin { get; init; } = 1;
            public uint UncommonMax { get; init; } = 3;
        }

        public sealed class ChestTable
        {
            public uint[] ResourcePool { get; init; } = Array.Empty<uint>();
            public int ResourceRolls { get; init; } = 3;
            public uint ShallowMin { get; init; } = 4;
            public uint ShallowMax { get; init; } = 10;
            public uint DeepMin { get; init; } = 6;
            public uint DeepMax { get; init; } = 15;
            public uint ResourceMin { get; init; }
            public uint ResourceMax { get; init; }
            public double RecipeChance { get; init; }
            public double DynamiteChance { get; init; }
            public uint DynamiteItem { get; init; } = 511;
            public ChanceLoot[] Extras { get; init; } = Array.Empty<ChanceLoot>();
        }

        public sealed class CrystalTable
        {
            public double RecipeChance { get; init; } = 0.6;
            public double SecondRecipeChance { get; init; } = 0.3;
            public double CampfireChance { get; init; } = 0.5;
            public uint CampfireItem { get; init; } = 3894;
            public uint[] AffinityEssences { get; init; } = { 1587, 1588, 1589, 1590, 1591, 1592, 1593 };
            public uint AffinityMin { get; init; } = 1;
            public uint AffinityMax { get; init; } = 3;
            public uint AdventureMin { get; init; } = 2;
            public uint AdventureMax { get; init; } = 4;
            public uint LumenMin { get; init; } = 2;
            public uint LumenMax { get; init; } = 5;
        }

        private sealed class Snapshot
        {
            public static Snapshot Empty { get; } = new()
            {
                KillLoot = new Dictionary<string, LootRange[]>(StringComparer.OrdinalIgnoreCase),
                KillExtras = new KillExtras(),
                Cavern = new CavernResources(),
                CaveTreasure = new ChestTable(),
                LostChest = new ChestTable(),
                Crystal = new CrystalTable()
            };

            public Dictionary<string, LootRange[]> KillLoot { get; init; }
            public KillExtras KillExtras { get; init; }
            public CavernResources Cavern { get; init; }
            public ChestTable CaveTreasure { get; init; }
            public ChestTable LostChest { get; init; }
            public CrystalTable Crystal { get; init; }
        }

        private sealed class CreatureKillsFile
        {
            public Dictionary<string, LootEntry[]> Kinds { get; set; }
            public KillExtrasDto KillExtras { get; set; }
        }

        private sealed class LootEntry
        {
            public uint Item { get; set; }
            public uint Min { get; set; }
            public uint Max { get; set; }
        }

        private sealed class KillExtrasDto
        {
            public uint CampfireItem { get; set; } = 3894;
            public double CampfireChance { get; set; } = 0.05;
            public double LumenChance { get; set; } = 0.15;
            public uint LumenMin { get; set; } = 1;
            public uint LumenMax { get; set; } = 3;
            public double RecipeChance { get; set; } = 0.01;
            public double FungusRareChance { get; set; } = 0.03;
        }

        private sealed class CavernResourcesFile
        {
            public uint[] Uncommon { get; set; }
            public uint[] Rare { get; set; }
            public double RareChance { get; set; } = 0.08;
            public uint UncommonMin { get; set; } = 1;
            public uint UncommonMax { get; set; } = 3;
        }

        private sealed class ChestFile
        {
            public uint[] ResourcePool { get; set; }
            public int ResourceRolls { get; set; } = 3;
            public uint ShallowMin { get; set; }
            public uint ShallowMax { get; set; }
            public uint DeepMin { get; set; }
            public uint DeepMax { get; set; }
            public uint ResourceMin { get; set; }
            public uint ResourceMax { get; set; }
            public double RecipeChance { get; set; }
            public double DynamiteChance { get; set; }
            public uint DynamiteItem { get; set; } = 511;
            public ChanceEntry[] Extras { get; set; }
        }

        private sealed class ChanceEntry
        {
            public uint Item { get; set; }
            public double Chance { get; set; }
            public uint Min { get; set; } = 1;
            public uint Max { get; set; } = 1;
        }

        private sealed class CrystalFile
        {
            public double RecipeChance { get; set; } = 0.6;
            public double SecondRecipeChance { get; set; } = 0.3;
            public double CampfireChance { get; set; } = 0.5;
            public uint CampfireItem { get; set; } = 3894;
            public uint[] AffinityEssences { get; set; }
            public uint AffinityMin { get; set; } = 1;
            public uint AffinityMax { get; set; } = 3;
            public uint AdventureMin { get; set; } = 2;
            public uint AdventureMax { get; set; } = 4;
            public uint LumenMin { get; set; } = 2;
            public uint LumenMax { get; set; } = 5;
        }

        /// <summary>Baked defaults shipped with the world program.</summary>
        public static string BakedRoot()
            => BakedRootOverride ?? Path.Combine(AppContext.BaseDirectory, "Resources", "Content");

        /// <summary>
        /// Host override root: <c>content</c> beside <see cref="ServerSettings"/> overrides file when that path is set
        /// (Docker: <c>/data/content</c>), else <c>content</c> beside the program.
        /// </summary>
        public static string OverrideRoot()
        {
            if (OverrideRootOverride != null)
                return OverrideRootOverride;
            try
            {
                string overridesFile = ServerSettings.OverridesPath(SharedConfiguration.Configuration);
                string dir = Path.GetDirectoryName(Path.GetFullPath(overridesFile));
                if (!string.IsNullOrEmpty(dir))
                    return Path.Combine(dir, "content");
            }
            catch
            {
                // SharedConfiguration not initialised yet (unit tests use OverrideRootOverride).
            }
            return Path.Combine(AppContext.BaseDirectory, "content");
        }

        /// <summary>Load baked + override tables at world start (seeds empty host loot/ once).</summary>
        public static void Init()
        {
            SeedOverrideLootIfEmpty();
            if (!TryBuild(out Snapshot next, out string error))
            {
                log.Error($"ContentTables: initial load failed ({error}); using empty fallback until a good reload.");
                lock (gate)
                    current = Snapshot.Empty;
                return;
            }
            lock (gate)
                current = next;
            log.Info($"ContentTables: loaded (baked {BakedRoot()}, override {OverrideRoot()}).");
        }

        /// <summary>
        /// Reload without world restart. On any file error, keeps the previous good snapshot and returns the error.
        /// </summary>
        public static string Reload()
        {
            if (!TryBuild(out Snapshot next, out string error))
            {
                log.Warn($"ContentTables: reload rejected — {error} (kept previous tables).");
                return error;
            }
            lock (gate)
                current = next;
            log.Info("ContentTables: reloaded.");
            return null;
        }

        public static IReadOnlyList<LootRange> KillLoot(string key)
        {
            Snapshot s = Read();
            if (string.IsNullOrEmpty(key) || !s.KillLoot.TryGetValue(key, out LootRange[] rows))
                return Array.Empty<LootRange>();
            return rows;
        }

        public static KillExtras KillExtrasTable() => Read().KillExtras;
        public static CavernResources CavernResourcesTable() => Read().Cavern;
        public static ChestTable CaveTreasureTable() => Read().CaveTreasure;
        public static ChestTable LostChestTable() => Read().LostChest;
        public static CrystalTable CrystalLootTable() => Read().Crystal;

        /// <summary>Roll one cavern resource from the live table.</summary>
        public static (uint Item, uint Count) RollCavernResource(Random random)
        {
            CavernResources t = CavernResourcesTable();
            if (random.NextDouble() < t.RareChance && t.Rare is { Length: > 0 })
                return (t.Rare[random.Next(t.Rare.Length)], 1u);
            if (t.Uncommon is not { Length: > 0 })
                return (36, 1u);
            uint min = Math.Min(t.UncommonMin, t.UncommonMax);
            uint max = Math.Max(t.UncommonMin, t.UncommonMax);
            return (t.Uncommon[random.Next(t.Uncommon.Length)], (uint)random.Next((int)min, (int)max + 1));
        }

        /// <summary>Append chance extras that pass their roll.</summary>
        public static void AddChanceExtras(List<(uint, uint)> loot, ChanceLoot[] extras, Random random)
        {
            if (extras == null)
                return;
            foreach (ChanceLoot e in extras)
            {
                if (e.Chance <= 0 || random.NextDouble() >= e.Chance)
                    continue;
                uint min = Math.Min(e.Min, e.Max);
                uint max = Math.Max(e.Min, e.Max);
                uint count = (uint)random.Next((int)min, (int)max + 1);
                if (count > 0)
                    loot.Add((e.Item, count));
            }
        }

        private static Snapshot Read()
        {
            lock (gate)
                return current;
        }

        private static void SeedOverrideLootIfEmpty()
        {
            string hostLoot = Path.Combine(OverrideRoot(), "loot");
            string bakedLoot = BakedLootDir();
            try
            {
                if (bakedLoot == null)
                {
                    log.Warn($"ContentTables: no baked loot dir under {BakedRoot()} (expected loot/).");
                    return;
                }
                Directory.CreateDirectory(hostLoot);
                foreach (string src in Directory.EnumerateFiles(bakedLoot, "*.json"))
                {
                    string dest = Path.Combine(hostLoot, Path.GetFileName(src));
                    if (!File.Exists(dest))
                        File.Copy(src, dest);
                }
            }
            catch (Exception e)
            {
                log.Warn(e, $"ContentTables: could not seed override loot at {hostLoot}.");
            }
        }

        /// <summary>Baked <c>loot/</c> directory (lowercase; Linux image is case-sensitive).</summary>
        private static string BakedLootDir()
        {
            string exact = Path.Combine(BakedRoot(), "loot");
            if (Directory.Exists(exact))
                return exact;
            // Dev machines / older images may still have Loot/ from before the lowercase rename.
            if (Directory.Exists(BakedRoot()))
                foreach (string dir in Directory.EnumerateDirectories(BakedRoot()))
                    if (string.Equals(Path.GetFileName(dir), "loot", StringComparison.OrdinalIgnoreCase))
                        return dir;
            return null;
        }

        private static bool TryBuild(out Snapshot next, out string error)
        {
            next = null;
            error = null;
            try
            {
                CreatureKillsFile kills = ReadMergedJson<CreatureKillsFile>("loot/creature-kills.json");
                CavernResourcesFile cavern = ReadMergedJson<CavernResourcesFile>("loot/cavern-resources.json");
                ChestFile treasure = ReadMergedJson<ChestFile>("loot/cavern-treasure.json");
                ChestFile lost = ReadMergedJson<ChestFile>("loot/outpost-lost-chest.json");
                CrystalFile crystal = ReadMergedJson<CrystalFile>("loot/lost-crystal.json");
                if (kills?.Kinds == null)
                {
                    error = "creature-kills.json missing kinds";
                    return false;
                }
                var killLoot = new Dictionary<string, LootRange[]>(StringComparer.OrdinalIgnoreCase);
                foreach ((string key, LootEntry[] rows) in kills.Kinds)
                    killLoot[key] = (rows ?? Array.Empty<LootEntry>())
                        .Select(r => new LootRange(r.Item, r.Min, r.Max)).ToArray();
                KillExtrasDto x = kills.KillExtras ?? new KillExtrasDto();
                next = new Snapshot
                {
                    KillLoot = killLoot,
                    KillExtras = new KillExtras
                    {
                        CampfireItem = x.CampfireItem,
                        CampfireChance = x.CampfireChance,
                        LumenChance = x.LumenChance,
                        LumenMin = x.LumenMin,
                        LumenMax = x.LumenMax,
                        RecipeChance = x.RecipeChance,
                        FungusRareChance = x.FungusRareChance
                    },
                    Cavern = ToCavern(cavern),
                    CaveTreasure = ToChest(treasure, shallowDefault: true),
                    LostChest = ToChest(lost, shallowDefault: false),
                    Crystal = ToCrystal(crystal)
                };
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        /// <summary>Override file if present, else baked; missing both throws.</summary>
        private static T ReadMergedJson<T>(string relative)
        {
            // relative is always "loot/<file>.json" with lowercase loot/ (Linux case-sensitive).
            string overridePath = Path.Combine(OverrideRoot(), relative.Replace('/', Path.DirectorySeparatorChar));
            string bakedPath = ResolveBakedFile(relative);
            string path = File.Exists(overridePath) ? overridePath : bakedPath;
            if (path == null || !File.Exists(path))
                throw new FileNotFoundException($"content file not found: {relative}", overridePath);
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), jsonOptions)
                ?? throw new InvalidDataException($"empty JSON: {path}");
        }

        private static string ResolveBakedFile(string relative)
        {
            string exact = Path.Combine(BakedRoot(), relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(exact))
                return exact;
            string file = Path.GetFileName(relative);
            string loot = BakedLootDir();
            if (loot == null)
                return exact;
            string candidate = Path.Combine(loot, file);
            return File.Exists(candidate) ? candidate : exact;
        }

        private static CavernResources ToCavern(CavernResourcesFile f)
        {
            f ??= new CavernResourcesFile();
            return new CavernResources
            {
                Uncommon = f.Uncommon is { Length: > 0 } ? f.Uncommon : new uint[] { 36, 37, 39, 49, 50 },
                Rare = f.Rare is { Length: > 0 } ? f.Rare : new uint[] { 56, 58, 59 },
                RareChance = f.RareChance,
                UncommonMin = f.UncommonMin,
                UncommonMax = f.UncommonMax
            };
        }

        private static ChestTable ToChest(ChestFile f, bool shallowDefault)
        {
            f ??= new ChestFile();
            return new ChestTable
            {
                ResourcePool = f.ResourcePool ?? Array.Empty<uint>(),
                ResourceRolls = f.ResourceRolls > 0 ? f.ResourceRolls : 3,
                ShallowMin = f.ShallowMin != 0 ? f.ShallowMin : (shallowDefault ? 4u : 0u),
                ShallowMax = f.ShallowMax != 0 ? f.ShallowMax : (shallowDefault ? 10u : 0u),
                DeepMin = f.DeepMin != 0 ? f.DeepMin : (shallowDefault ? 6u : 0u),
                DeepMax = f.DeepMax != 0 ? f.DeepMax : (shallowDefault ? 15u : 0u),
                ResourceMin = f.ResourceMin,
                ResourceMax = f.ResourceMax,
                RecipeChance = f.RecipeChance,
                DynamiteChance = f.DynamiteChance,
                DynamiteItem = f.DynamiteItem != 0 ? f.DynamiteItem : 511u,
                Extras = (f.Extras ?? Array.Empty<ChanceEntry>())
                    .Select(e => new ChanceLoot(e.Item, e.Chance, e.Min, e.Max)).ToArray()
            };
        }

        private static CrystalTable ToCrystal(CrystalFile f)
        {
            f ??= new CrystalFile();
            return new CrystalTable
            {
                RecipeChance = f.RecipeChance,
                SecondRecipeChance = f.SecondRecipeChance,
                CampfireChance = f.CampfireChance,
                CampfireItem = f.CampfireItem != 0 ? f.CampfireItem : 3894u,
                AffinityEssences = f.AffinityEssences is { Length: > 0 }
                    ? f.AffinityEssences
                    : new uint[] { 1587, 1588, 1589, 1590, 1591, 1592, 1593 },
                AffinityMin = f.AffinityMin,
                AffinityMax = f.AffinityMax,
                AdventureMin = f.AdventureMin,
                AdventureMax = f.AdventureMax,
                LumenMin = f.LumenMin,
                LumenMax = f.LumenMax
            };
        }
    }
}
