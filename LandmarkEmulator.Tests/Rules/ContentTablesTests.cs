using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using System;
using System.IO;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>ContentTables (AGENTS #22): load baked loot JSON; bad override keeps the previous good table.</summary>
    public class ContentTablesTests : IDisposable
    {
        private readonly string baked;
        private readonly string overrideRoot;
        private readonly string prevBaked;
        private readonly string prevOverride;

        public ContentTablesTests()
        {
            prevBaked = ContentTables.BakedRootOverride;
            prevOverride = ContentTables.OverrideRootOverride;
            baked = Path.Combine(Path.GetTempPath(), "content-tables-baked-" + Guid.NewGuid().ToString("N"));
            overrideRoot = Path.Combine(Path.GetTempPath(), "content-tables-override-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(baked, "loot"));
            Directory.CreateDirectory(Path.Combine(overrideRoot, "loot"));
            string repoLoot = Path.Combine(Repo.Root, "LandmarkEmulator.WorldServer", "Resources", "Content", "loot");
            foreach (string file in Directory.EnumerateFiles(repoLoot, "*.json"))
                File.Copy(file, Path.Combine(baked, "loot", Path.GetFileName(file)), overwrite: true);
            ContentTables.BakedRootOverride = baked;
            ContentTables.OverrideRootOverride = overrideRoot;
        }

        public void Dispose()
        {
            ContentTables.BakedRootOverride = prevBaked;
            ContentTables.OverrideRootOverride = prevOverride;
            try { Directory.Delete(baked, true); } catch { /* temp */ }
            try { Directory.Delete(overrideRoot, true); } catch { /* temp */ }
        }

        [Fact]
        public void Init_LoadsBakedCreatureKillKeys()
        {
            ContentTables.Init();
            Assert.NotEmpty(ContentTables.KillLoot("default_humanoid"));
            Assert.NotEmpty(ContentTables.KillLoot("highfall_catapult"));
            Assert.True(ContentTables.KillExtrasTable().CampfireChance > 0);
            Assert.NotEmpty(ContentTables.CavernResourcesTable().Uncommon);
            Assert.NotEmpty(ContentTables.CaveTreasureTable().ResourcePool);
            Assert.NotEmpty(ContentTables.LostChestTable().ResourcePool);
            Assert.True(ContentTables.CrystalLootTable().RecipeChance > 0);
        }

        [Fact]
        public void Reload_BadOverride_KeepsPrevious()
        {
            ContentTables.Init();
            double before = ContentTables.KillExtrasTable().CampfireChance;
            Assert.Equal(0.05, before, 5);

            File.WriteAllText(Path.Combine(overrideRoot, "loot", "creature-kills.json"), "{ not json");
            string err = ContentTables.Reload();
            Assert.False(string.IsNullOrEmpty(err));
            Assert.Equal(before, ContentTables.KillExtrasTable().CampfireChance);
            Assert.NotEmpty(ContentTables.KillLoot("default_humanoid"));
        }

        [Fact]
        public void Reload_GoodOverride_AppliesNextKillTable()
        {
            ContentTables.Init();
            string path = Path.Combine(overrideRoot, "loot", "creature-kills.json");
            string text = File.ReadAllText(Path.Combine(baked, "loot", "creature-kills.json"))
                .Replace("\"campfireChance\": 0.05", "\"campfireChance\": 0.42");
            File.WriteAllText(path, text);
            Assert.Null(ContentTables.Reload());
            Assert.Equal(0.42, ContentTables.KillExtrasTable().CampfireChance, 5);
        }
    }
}
