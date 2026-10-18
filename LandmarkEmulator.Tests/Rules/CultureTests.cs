using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Shared.GameTable;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// The client's data tables write numbers the invariant way ("2.8"). A server started on a Windows with a comma-decimal
    /// culture (German, Turkish, …) must read them the same: parsed with the machine's culture "2.8" became 28.
    /// </summary>
    public class CultureTests : IDisposable
    {
        private readonly CultureInfo before = CultureInfo.CurrentCulture;
        private readonly string folder = Path.Combine(Path.GetTempPath(), "lmemu-tests-" + Guid.NewGuid().ToString("N"));

        public CultureTests() => Directory.CreateDirectory(folder);

        public void Dispose()
        {
            CultureInfo.CurrentCulture = before;
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }

        /// <summary>A table row type as the game tables have them (ProfileDefinitions: STAND_CAMERA_HEIGHT 2.8 …).</summary>
        public class Row
        {
            public uint Id { get; set; }
            public float Height { get; set; }
            public double Cost { get; set; }
            public int Offset { get; set; }
            public string Name { get; set; }
        }

        [Theory]
        [InlineData("de-DE")]
        [InlineData("tr-TR")]
        [InlineData("fr-FR")]
        [InlineData("en-US")]
        public void GameTables_ReadInvariantNumbers_InAnyCulture(string culture)
        {
            string file = Path.Combine(folder, "Rows.txt");
            File.WriteAllLines(file, new[] { "#*ID^HEIGHT^COST^OFFSET^NAME^", "7^2.8^0.004^-5^stand^", "8^1500.25^12^0^x^" });
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var table = new GameTable<Row>(file, true);
            Row row = table.GetEntry(7);
            Assert.Equal(2.8f, row.Height);
            Assert.Equal(0.004, row.Cost);
            Assert.Equal(-5, row.Offset);
            Assert.Equal(1500.25f, table.GetEntry(8).Height);
        }

        [Theory]
        [InlineData("de-DE")]
        [InlineData("tr-TR")]
        public void DurabilitySets_ReadInvariantNumbers_InAnyCulture(string culture)
        {
            File.WriteAllLines(Path.Combine(folder, "DurabilitySet.txt"), new[]
            {
                "#*ID^DURABILITY_COUNT^LOSS_PER_HP_DMG^LOSS_PER_DEATH^ZERO_COUNT_DESCRIPTION_ID^",
                "2^9834.5^0.009^984.25^575602^",
            });
            PropertyInfo shared = typeof(SharedConfiguration).GetProperty(nameof(SharedConfiguration.Configuration))!;
            object previousConfig = shared.GetValue(null);
            FieldInfo cache = typeof(Durability).GetField("sets", BindingFlags.NonPublic | BindingFlags.Static)!;
            object previousSets = cache.GetValue(null);
            try
            {
                shared.GetSetMethod(true)!.Invoke(null, new object[] { new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string> { ["GameDataPath"] = folder }).Build() });
                cache.SetValue(null, null);
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                var sets = (IDictionary)typeof(Durability).GetProperty("Sets", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
                object set = sets[2u];
                Type t = set.GetType();
                Assert.Equal(9834.5f, (float)t.GetProperty("Count")!.GetValue(set));
                Assert.Equal(0.009f, (float)t.GetProperty("LossPerDamage")!.GetValue(set));
                Assert.Equal(984.25f, (float)t.GetProperty("LossPerDeath")!.GetValue(set));
            }
            finally
            {
                shared.GetSetMethod(true)!.Invoke(null, new[] { previousConfig });
                cache.SetValue(null, previousSets);
            }
        }
    }
}
