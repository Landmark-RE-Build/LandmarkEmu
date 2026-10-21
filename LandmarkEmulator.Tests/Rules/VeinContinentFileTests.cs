using LandmarkEmulator.WorldServer.Game.Terrain;
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// The client names its per-continent voxel files by the landscape's continent id, not its zone id [P]: the vein
    /// loader FUN_143ff34d0 formats <c>Voxel\veinData\ContinentVeinData%d.vdt</c> with the VoxelFarm core's +0x5208 (the
    /// zone's continent, also used for ContinentOceanMap / ContinentSurfaceCache), the cave loader FUN_143ff1310 names
    /// <c>ContinentCavePlacements[%d]</c> from the core's continent definition. So the Pioneer lands 464-468 read
    /// ContinentVeinData440-444.vdt and ContinentCavePlacements[440-444]; the server opened 464-468 and found no veins
    /// (docs/terrain.md "Ore veins"). A fake install with its own StaticZoneData (landscape ids no real table has, so the
    /// shared caches of the other tests stay untouched) checks both lookups and a vein file actually loading.
    /// </summary>
    public class VeinContinentFileTests : IDisposable
    {
        // Landscape 9464 is generated as continent 9440 (like Pioneer Jungle 464 → 440); 9406 is its own continent (like
        // the classic islands); 9460 → 9436 (like the event islands, which ship no vein file); 9999 has no row.
        private const uint Pioneer = 9464, PioneerContinent = 9440, Island = 9406, EventIsland = 9460, EventContinent = 9436, NoRow = 9999;

        private readonly string install = Path.Combine(Path.GetTempPath(), "lm-veincontinent-" + Guid.NewGuid().ToString("N"));

        public VeinContinentFileTests()
        {
            Directory.CreateDirectory(Path.Combine(install, "Resources"));
            File.WriteAllLines(Path.Combine(install, "Resources", "StaticZoneData.txt"), new[]
            {
                "#*ID^GEOMETRY_ID^LAUNCHED_BY_WORLD^NAME_ID^DESCRIPTION^CONTINENT_ID^DUEL_ENCOUNTER_ID^DESCRIPTION_ID^GEOMETRY_OFFSET_X^GEOMETRY_OFFSET_Y^GEOMETRY_OFFSET_Z^ZONE_EXTENTS_X^ZONE_EXTENTS_Y^ZONE_EXTENTS_Z^IS_NEWBIE_ZONE^",
                $"{Island}^{Island}^1^1^Test Island^{Island}^0^0^2558400^2000^2556450^8000^4000^8000^0^",
                $"{EventIsland}^9456^1^1^Test Event Island^{EventContinent}^0^0^2558400^2000^2556450^8000^4000^8000^0^",
                $"{Pioneer}^9460^1^1^Test Pioneer Land^{PioneerContinent}^0^0^2558400^2000^2556450^8000^4000^8000^1^",
                // A zone row whose id is the Pioneer land's continent but which is generated as another continent (as
                // zones 440-444 are continent 430): the file id is the landscape's continent, never this row.
                $"{PioneerContinent}^9435^1^1^Unrelated^9430^0^0^2546400^2000^2544450^8000^4000^8000^0^",
            });
        }

        public void Dispose()
        {
            try { Directory.Delete(install, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        [Fact]
        public void Continent_IsStaticZoneDataContinentId()
        {
            Assert.Equal(PioneerContinent, LandscapeContinents.Of(Pioneer, install));
            Assert.Equal(Island, LandscapeContinents.Of(Island, install));
            Assert.Equal(EventContinent, LandscapeContinents.Of(EventIsland, install));
            Assert.Equal(9430u, LandscapeContinents.Of(PioneerContinent, install));
            Assert.Equal(NoRow, LandscapeContinents.Of(NoRow, install));
        }

        [Fact]
        public void VeinFile_IsNamedByContinent()
        {
            Assert.Equal("ContinentVeinData9440.vdt", Path.GetFileName(OreVeins.VdtPath(install, Pioneer)));
            Assert.Equal("ContinentVeinData9406.vdt", Path.GetFileName(OreVeins.VdtPath(install, Island)));
            Assert.Equal("ContinentVeinData9436.vdt", Path.GetFileName(OreVeins.VdtPath(install, EventIsland)));
            Assert.Equal(Path.Combine(install, "CommonData", "Voxel", "veinData", "ContinentVeinData9440.vdt"), OreVeins.VdtPath(install, Pioneer));
        }

        [Fact]
        public void CavePlacements_AreNamedByContinent()
        {
            FieldInfo dir = typeof(CavePieces).GetField("gameDirectory", BindingFlags.NonPublic | BindingFlags.Static)!;
            object previous = dir.GetValue(null);
            try
            {
                dir.SetValue(null, install);
                Assert.Equal(Path.Combine(install, "CommonData", "Voxel", "caves", "ContinentCavePlacements[9440]_Underground.cpa"),
                    CavePieces.PlacementPath(Pioneer, "Underground"));
                Assert.Equal("ContinentCavePlacements[9406]_Surface.cpa", Path.GetFileName(CavePieces.PlacementPath(Island, "Surface")));
            }
            finally
            {
                dir.SetValue(null, previous);
            }
        }

        [Fact]
        public void PioneerLand_LoadsItsContinentsVeinFile()
        {
            string veins = Path.Combine(install, "CommonData", "Voxel", "veinData");
            Directory.CreateDirectory(veins);
            // Only the continent's file exists (as the 0.695 install ships ContinentVeinData440.vdt and no 464).
            WriteVdt(Path.Combine(veins, $"ContinentVeinData{PioneerContinent}.vdt"),
                surface: new[] { TerrainStore.CellId(TerrainEdits.Level, 10000, 400, 10000), TerrainStore.CellId(TerrainEdits.Level, 10001, 400, 10000) },
                cave: new[] { TerrainStore.CellId(TerrainEdits.Level, 10000, 300, 10000) });

            FieldInfo dir = typeof(OreVeins).GetField("gameDirectory", BindingFlags.NonPublic | BindingFlags.Static)!;
            FieldInfo defs = typeof(OreVeins).GetField("definitions", BindingFlags.NonPublic | BindingFlags.Static)!;
            var zones = (IDictionary)typeof(OreVeins).GetField("zones", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            object previousDir = dir.GetValue(null), previousDefs = defs.GetValue(null);
            try
            {
                dir.SetValue(null, install);
                defs.SetValue(null, new System.Collections.Generic.Dictionary<uint, VeinGenerator.Definition>());
                var pools = OreVeins.PoolsOf(Pioneer);
                Assert.NotNull(pools);
                Assert.Equal(2, pools.Value.SurfaceCells);
                Assert.Equal(1, pools.Value.CaveCells);
                // The classic-island and event-island ids find no file in this install: no veins, as before.
                Assert.Null(OreVeins.PoolsOf(Island));
                Assert.Null(OreVeins.PoolsOf(EventIsland));
            }
            finally
            {
                lock (typeof(OreVeins).GetField("sync", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!)
                    foreach (uint zone in new[] { Pioneer, Island, EventIsland })
                        zones.Remove(zone);
                dir.SetValue(null, previousDir);
                defs.SetValue(null, previousDefs);
            }
        }

        /// <summary>A minimal .vdt in the layout OreVeins.ReadVdt reads: no layers or vein kinds, one category per pool.</summary>
        private static void WriteVdt(string path, ulong[] surface, ulong[] cave)
        {
            using var w = new BinaryWriter(File.Create(path));
            w.Write(0u); w.Write(0.0); w.Write(0.0); w.Write(0u);
            w.Write(0u);                                // layers
            w.Write(0u);                                // sets
            w.Write(0u); w.Write(0u);                   // two count maps
            foreach (ulong[] pool in new[] { surface, cave })
            {
                w.Write(1u);                            // one category
                w.Write(0); w.Write(0); w.Write(0);
                w.Write((uint)pool.Length);
                foreach (ulong cell in pool)
                    w.Write(cell);
            }
            w.Write(0u);                                // category → set map
            w.Write(0u);                                // vein infos
        }
    }
}
