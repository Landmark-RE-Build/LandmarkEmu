using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.WorldServer.Game.Terrain;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// Ore never stands in the client's air (report 2026-11-01, trace/0695/pioneer-veins.md). The client's vein code only
    /// repaints voxels its generated ground has solid (FUN_143fdb110) [P], but an ore voxel the server sends is solid
    /// whatever the client generated. On the Pioneer lands the server's ground is only an estimate from the surface cache
    /// (re/tools/csc_heights.py: a flat cell at low 18 = "somewhere in 1024-1040 m", written as 1029.0 m) while the client
    /// draws 1027.4-1029.5 m there, so veins started on the estimate stood as blocks on the grass. Now, where the ground is
    /// only that estimate (a grid coarser than the 2 m bake), ore goes only under the cache's floor (the lowest the
    /// client's ground can lie, less 1.2 m), surface veins start there (PioneerSurfaceVeins buried) or are left out (off),
    /// surface iron is left out on Pioneer lands as live (PioneerSurfaceIron), and the vein layer's versions there move to
    /// a block no older layout used (clients re-mesh instead of drawing their cached lumps). A classic island (2 m bake of
    /// the client's own ground) keeps its veins exactly as before: started on the ground, ore flush with it.
    /// A fake install (own StaticZoneData, surface cache, vein files; landscape ids no real table has) and the client's
    /// own vein rows (ResourceVeins 233 copper, 236 amaranthine, 237 agate, 247 surface iron).
    /// </summary>
    public class PioneerVeinTests : IDisposable
    {
        // Landscape 9564 is a Pioneer land generated as continent 9540 (like 464 → 440); 9506 a classic island.
        private const uint Pioneer = 9564, PioneerContinent = 9540, Island = 9506;
        /// <summary>A classic island with a real (header-read) 2 m heights file and no TestStep / TestGround.</summary>
        private const uint BakedIsland = 9507;
        /// <summary>The surface cache's flat Pioneer cell: low 18 over the cache's y 171 = the 16 m cell 1024-1040 m.</summary>
        private const ushort PlateauTexel = 18;
        private const float CellBottom = 1024f;
        /// <summary>What re/tools/csc_heights.py writes for that cell: bottom + 5 m.</summary>
        private const float Estimate = 1029f;
        /// <summary>The lowest ground the client was seen to draw on the plateau (464, 2026-11-01 log).</summary>
        private const float LowestDrawnGround = 1027.4f;
        private const float IslandGround = 1010f;

        private static readonly (double X, double Y, double Z) Origin = TerrainStore.ZoneOrigin;
        // One 256 m vein region (global dm 25584640.. / 25564160..), local x 64..320 m, z -34..222 m.
        private const uint CellX0 = 159904, CellZ0 = 159776;
        private static readonly Vector3 InRegion = new(192, 1029, 94);

        private readonly string install = Path.Combine(Path.GetTempPath(), "lm-pioneerveins-" + Guid.NewGuid().ToString("N"));
        private readonly object previousDir, previousDefs, previousConfig;
        private static readonly PropertyInfo Shared = typeof(SharedConfiguration).GetProperty(nameof(SharedConfiguration.Configuration))!;
        private readonly List<(ulong Cell, uint Zone)> edited = new();
        private readonly OreVeins.CoarseSurfaceVeins previousMode = OreVeins.PioneerSurfaceVeins;
        private readonly bool previousIron = OreVeins.PioneerSurfaceIron;
        private readonly Func<uint, (double, double, double), (double, double, double), List<(double, double, double, double, double, double)>> previousSites = OreVeins.SitesIn;
        private readonly Func<uint, (double, double, double), (double, double, double), bool> previousNoVeins = OreVeins.NoVeins;

        private static FieldInfo Static(string name) => typeof(OreVeins).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!;

        public PioneerVeinTests()
        {
            Directory.CreateDirectory(Path.Combine(install, "Resources"));
            File.WriteAllLines(Path.Combine(install, "Resources", "StaticZoneData.txt"), new[]
            {
                "#*ID^GEOMETRY_ID^LAUNCHED_BY_WORLD^NAME_ID^DESCRIPTION^CONTINENT_ID^DUEL_ENCOUNTER_ID^DESCRIPTION_ID^GEOMETRY_OFFSET_X^GEOMETRY_OFFSET_Y^GEOMETRY_OFFSET_Z^ZONE_EXTENTS_X^ZONE_EXTENTS_Y^ZONE_EXTENTS_Z^IS_NEWBIE_ZONE^",
                $"{Island}^{Island}^1^1^Test Island^{Island}^0^0^2558400^2000^2556450^8000^4000^8000^0^",
                $"{Pioneer}^9560^1^1^Test Pioneer Land^{PioneerContinent}^0^0^2558400^2000^2556450^8000^4000^8000^1^",
            });
            WriteSurfaceCache(PioneerContinent, (r, c) => PlateauTexel);
            string veins = Path.Combine(install, "CommonData", "Voxel", "veinData");
            Directory.CreateDirectory(veins);
            WriteVdt(Path.Combine(veins, $"ContinentVeinData{PioneerContinent}.vdt"), PoolCells(189));   // 1024-1040 m
            WriteVdt(Path.Combine(veins, $"ContinentVeinData{Island}.vdt"), PoolCells(188));              // 1008-1024 m
            WriteVdt(Path.Combine(veins, $"ContinentVeinData{BakedIsland}.vdt"), PoolCells(188));
            // The game data folder (GameDataPath): only BakedIsland has a heights file (4 × 4 samples, 2 m apart).
            string gameData = Path.Combine(install, "gameData");
            Directory.CreateDirectory(gameData);
            WriteHeights(Path.Combine(gameData, $"TerrainHeights{BakedIsland}.bin"), 4, 2f);
            previousConfig = Shared.GetValue(null);
            Shared.GetSetMethod(true)!.Invoke(null, new object[] { new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string> { ["GameDataPath"] = gameData }).Build() });

            previousDir = Static("gameDirectory").GetValue(null);
            previousDefs = Static("definitions").GetValue(null);
            Static("gameDirectory").SetValue(null, install);
            Static("definitions").SetValue(null, Definitions());
            OreVeins.SitesIn = (zone, min, max) => new List<(double, double, double, double, double, double)>();
            OreVeins.NoVeins = null;
            // The Pioneer land's grid as csc_heights.py makes it (16 m, flat cells at the estimate); the island's a 2 m bake.
            TerrainHeights.TestGround = (x, z, zone) => zone == Pioneer ? Estimate : zone == Island ? IslandGround : null;
            TerrainHeights.TestStep = zone => zone == Pioneer ? 16f : zone == Island ? 2f : null;
            Forget();
        }

        public void Dispose()
        {
            foreach ((ulong cell, uint zone) in edited)
                TerrainEdits.ClearCell(cell, zone);
            Forget();
            Shared.GetSetMethod(true)!.Invoke(null, new[] { previousConfig });
            TerrainHeights.TestGround = null;
            TerrainHeights.TestStep = null;
            Static("gameDirectory").SetValue(null, previousDir);
            Static("definitions").SetValue(null, previousDefs);
            OreVeins.SitesIn = previousSites;
            OreVeins.NoVeins = previousNoVeins;
            OreVeins.PioneerSurfaceVeins = previousMode;
            OreVeins.PioneerSurfaceIron = previousIron;
            try { Directory.Delete(install, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        /// <summary>Drops the test landscapes' vein layers (they are built with the settings of their first use).</summary>
        private static void Forget()
        {
            var zones = (IDictionary)Static("zones").GetValue(null)!;
            lock (Static("sync").GetValue(null)!)
                foreach (uint zone in new[] { Pioneer, Island, BakedIsland })
                    zones.Remove(zone);
        }

        [Fact]
        public void FlatPioneerLand_NoOreAboveTheGround()
        {
            OreVeins.PioneerSurfaceVeins = OreVeins.CoarseSurfaceVeins.Buried;
            OreVeins.PioneerSurfaceIron = false;
            OreVeins.EnsureAround(Pioneer, InRegion);

            var veins = Veins(Pioneer);
            Assert.True(veins.Count >= 10, $"{veins.Count} surface veins");
            foreach (string ore in new[] { "copper", "amaranthine", "agate" })
                Assert.True(veins.Any(v => v.Info.Definition.Name.Contains(ore)), $"no {ore} vein");
            var voxels = OreVoxels(Pioneer);
            Assert.True(voxels.Count > 500, $"{voxels.Count} ore voxels");
            // No ore where the client may draw air: every ore voxel's top under the lowest ground the cache allows (1024 m),
            // so under any ground the client can draw there (seen: 1027.4-1029.5 m).
            float top = voxels.Max(v => v.Top);
            Assert.True(top <= CellBottom, $"ore up to {top:F2} m: above the lowest possible ground {CellBottom} m");
            Assert.True(top < LowestDrawnGround - 4f, $"ore up to {top:F2} m");
            // Started at the cache's floor (1024 − 1.2 m), not at the estimate (1029 m), and none lost: every vein has ore.
            foreach (var v in veins)
            {
                double startY = Local(v.Points[0].Y);
                Assert.Equal(CellBottom - SurfaceCache.Margin, startY, 0.01);
                Assert.True(v.Cells.Count > 0, $"{v.Info.Definition.Name} vein without ore");
            }
            // Every vein kind keeps its whole shape: only the first point's upper half lies above the floor.
            foreach (var v in veins.Where(v => v.Info.Definition.Name.Contains("copper")))
                Assert.True(VoxelsOf(Pioneer, v) > 100, $"copper vein with {VoxelsOf(Pioneer, v)} voxels");
        }

        [Fact]
        public void FlatPioneerLand_SurfaceIronOffAsLive_AndSwitchable()
        {
            OreVeins.PioneerSurfaceVeins = OreVeins.CoarseSurfaceVeins.Buried;
            OreVeins.PioneerSurfaceIron = false;
            OreVeins.EnsureAround(Pioneer, InRegion);
            Assert.False(Veins(Pioneer).Any(v => v.Item == 36), "surface iron on a Pioneer land (26 Apr 2016: non-Pioneer only)");

            Forget();
            OreVeins.PioneerSurfaceIron = true;
            OreVeins.EnsureAround(Pioneer, InRegion);
            Assert.True(Veins(Pioneer).Any(v => v.Item == 36), "PioneerSurfaceIron true gives the vdt's surface iron");
            Assert.True(OreVoxels(Pioneer).Max(v => v.Top) <= CellBottom);
        }

        [Fact]
        public void FlatPioneerLand_SurfaceVeinsOff()
        {
            OreVeins.PioneerSurfaceVeins = OreVeins.CoarseSurfaceVeins.Off;
            OreVeins.EnsureAround(Pioneer, InRegion);
            Assert.Empty(Veins(Pioneer));
            Assert.Empty(OreVeins.Entries(Pioneer));
        }

        [Fact]
        public void PioneerLand_VeinCellsGetVersionsNoEarlierLayoutGave()
        {
            // The client's mesh cache draws a cell's stored mesh again while the manifest gives the version it was stored
            // with; the old layout's vein cells had versions 2, 3… (rebuilt the same at every start), so the new layout
            // must not reuse them, or a client keeps showing its cached lumps.
            OreVeins.PioneerSurfaceVeins = OreVeins.CoarseSurfaceVeins.Buried;
            OreVeins.PioneerSurfaceIron = false;
            OreVeins.EnsureAround(Pioneer, InRegion);
            var buried = OreVeins.Entries(Pioneer);
            Assert.NotEmpty(buried);
            Assert.True(buried.All(e => e.Version > 65536 && e.Version < 492747177u), "Pioneer vein cells keep small versions");

            Forget();
            OreVeins.PioneerSurfaceIron = true;
            OreVeins.EnsureAround(Pioneer, InRegion);
            var withIron = OreVeins.Entries(Pioneer);
            Assert.True(withIron.Max(e => e.Version) < buried.Min(e => e.Version) || buried.Max(e => e.Version) < withIron.Min(e => e.Version),
                "two Pioneer layouts share vein cell versions");
        }

        [Fact]
        public void ClassicIsland_VeinsUnchanged()
        {
            OreVeins.PioneerSurfaceVeins = OreVeins.CoarseSurfaceVeins.Off;   // Pioneer-only rules: no effect here
            OreVeins.PioneerSurfaceIron = false;
            OreVeins.EnsureAround(Island, InRegion);

            var veins = Veins(Island);
            Assert.True(veins.Count >= 10, $"{veins.Count} surface veins");
            Assert.True(veins.Any(v => v.Item == 36), "surface iron belongs on the classic islands");
            // Started on the ground (SURFACE_OFFSET 0), ore up to it and not above: the top layer flush with the grass.
            foreach (var v in veins)
                Assert.Equal(IslandGround, Local(v.Points[0].Y), 0.001);
            var voxels = OreVoxels(Island);
            Assert.True(voxels.All(v => v.Top <= IslandGround + 0.001), "ore above the ground on a classic island");
            Assert.Equal(IslandGround, voxels.Max(v => v.Top), 0.001);
            // Versions as before (the vein layer's own 1 + one per write).
            Assert.True(OreVeins.Entries(Island).All(e => e.Version >= 2 && e.Version < 100), "classic vein cell versions changed");
        }

        [Fact]
        public void SurfaceCacheFloor_IsTheLowestCellBottomAroundLessTheMargin()
        {
            // One seabed texel (low 0: the cell 736-752 m) in the plateau: its 3 × 3 neighbours take its bottom.
            WriteSurfaceCache(PioneerContinent, (r, c) => (ushort)(r == 250 && c == 250 ? 0 : PlateauTexel | (r == 100 ? 2 << 10 : 0)));
            Assert.Equal(CellBottom - SurfaceCache.Margin, SurfaceCache.FloorAt(install, -200, -200, Pioneer) ?? float.NaN, 0.001f);
            // Texel (250, 250) covers local x 16·250 − 4000 = 0..16 m, z −2..14 m.
            float seabed = 736f - SurfaceCache.Margin;
            Assert.Equal(seabed, SurfaceCache.FloorAt(install, 8, 6, Pioneer) ?? float.NaN, 0.001f);
            Assert.Equal(seabed, SurfaceCache.FloorAt(install, 20, 20, Pioneer) ?? float.NaN, 0.001f);
            Assert.Equal(CellBottom - SurfaceCache.Margin, SurfaceCache.FloorAt(install, 40, 6, Pioneer) ?? float.NaN, 0.001f);
            // A sloped texel's count widens the band upwards only: the floor stays the bottom cell's.
            Assert.Equal(CellBottom - SurfaceCache.Margin, SurfaceCache.FloorAt(install, 100, 16 * 100 - 4002 + 8, Pioneer) ?? float.NaN, 0.001f);
            Assert.Null(SurfaceCache.FloorAt(install, 9000, 0, Pioneer));
            Assert.Null(SurfaceCache.FloorAt(install, 0, 0, Island));   // no cache for the island's continent
        }

        [Fact]
        public void PioneerLand_WithoutSurfaceCache_KeepsCaveOreOnTheEstimate_AndHasNoSurfaceVeins()
        {
            // A trimmed install: the heights grid makes the land coarse ground, but its ContinentSurfaceCache is gone.
            // Before: FloorAt was null for every voxel, so even cave veins got no ore. Now: no surface veins (no floor to
            // bury them under), and cave ore is tested against the estimated ground as before 2026-11-01.
            File.Delete(SurfaceCache.PathOf(install, Pioneer));
            OreVeins.PioneerSurfaceVeins = OreVeins.CoarseSurfaceVeins.Buried;
            OreVeins.EnsureAround(Pioneer, InRegion);
            Assert.Empty(Veins(Pioneer));
            object zone = ZoneObject(Pioneer);
            Assert.True(Field<bool>(zone, "CoarseGround"));
            Assert.False(Field<bool>(zone, "HasSurfaceCache"));
            // 1025 m: under the estimate (1029 m), above where the cache's floor would be (1022.8 m).
            Assert.True(GeneratedSolid(zone, InRegion.X, 1025f, InRegion.Z), "cave ore lost without the surface cache");
            Assert.False(GeneratedSolid(zone, InRegion.X, 1030f, InRegion.Z));
        }

        [Fact]
        public void PioneerLand_WithSurfaceCache_ClipsOreAtTheFloor()
        {
            Assert.NotNull(OreVeins.PoolsOf(Pioneer));
            object zone = ZoneObject(Pioneer);
            Assert.True(Field<bool>(zone, "HasSurfaceCache"));
            Assert.False(GeneratedSolid(zone, InRegion.X, 1025f, InRegion.Z), "ore above the cache's floor");
            Assert.True(GeneratedSolid(zone, InRegion.X, 1022f, InRegion.Z));
        }

        [Fact]
        public void OpeningAVeinLayer_ReadsOnlyTheHeightsHeader()
        {
            // The Environment Server opens the vein layer of every landscape with stored edits at start-up (Reconcile →
            // ManifestEntries → OreVeins.Entries) and never trims height grids; deciding coarse ground used to load the
            // whole grid (64 MB for a 4000² bake) and keep it for the service's life.
            Assert.NotNull(OreVeins.PoolsOf(BakedIsland));
            Assert.False(TerrainHeights.IsLoaded(BakedIsland), "the vein layer loaded the whole heights grid");
            Assert.False(Field<bool>(ZoneObject(BakedIsland), "CoarseGround"));
            Assert.Equal(2f, TerrainHeights.StepOf(BakedIsland) ?? float.NaN);
            Assert.False(TerrainHeights.IsLoaded(BakedIsland));
            // The grid itself still loads when the ground is needed, with the same step.
            Assert.Equal(1010f, TerrainHeights.HeightAt(-997f, -997f, BakedIsland) ?? float.NaN, 0.01f);
            Assert.True(TerrainHeights.IsLoaded(BakedIsland));
            Assert.Equal(2f, TerrainHeights.StepOf(BakedIsland) ?? float.NaN);
            Assert.Null(TerrainHeights.StepOf(9508));   // no file
        }

        [Fact]
        public void PioneerLand_DugCellsWithoutOre_GetTheVersionBaseToo()
        {
            // Under the new layout the old lumps' cells (1024-1040 m) hold no ore. A dug one keeps its overlay, and its
            // version alone (E) could equal an old E' + 2..5 the client cached (heals bump E in small steps), so the
            // client would keep drawing the old lump. Every dig-layer version of the land carries the base instead.
            OreVeins.PioneerSurfaceVeins = OreVeins.CoarseSurfaceVeins.Buried;
            OreVeins.PioneerSurfaceIron = false;
            OreVeins.EnsureAround(Pioneer, InRegion);
            uint versionBase = OreVeins.DigVersionBase(Pioneer);
            Assert.Equal((8u | 4u | 1u) << 16, versionBase);
            Assert.Equal(0u, OreVeins.DigVersionBase(Island));

            ulong plain = TerrainStore.CellId(TerrainEdits.Level, CellX0 + 100, 189, CellZ0 + 100);   // no ore
            Assert.False(OreVeins.Known(plain, Pioneer));
            Assert.Equal(0u, TerrainEdits.LayerVersion(plain, Pioneer, TerrainEdits.DigLayer));      // nothing: still 0
            Dig(plain, Pioneer, 3);
            Assert.Equal(3u + versionBase, TerrainEdits.LayerVersion(plain, Pioneer, TerrainEdits.DigLayer));
            Assert.Equal(3u + versionBase, Manifest(Pioneer, plain));

            // A dug ore cell: overlay + the ore cell's version (which holds the base once already).
            (ulong ore, uint oreVersion) = OreVeins.Entries(Pioneer).First();
            Assert.True(oreVersion > versionBase && oreVersion < versionBase + 100, $"ore cell version {oreVersion}");
            Dig(ore, Pioneer, 3);
            Assert.Equal(3u + oreVersion, TerrainEdits.LayerVersion(ore, Pioneer, TerrainEdits.DigLayer));
            Assert.Equal(3u + oreVersion, Manifest(Pioneer, ore));
            Assert.Equal(TerrainEdits.LayerVersions(ore, Pioneer)[TerrainEdits.DigLayer], Manifest(Pioneer, ore));

            // Other layers and the classic islands as before.
            Assert.Equal(0u, TerrainEdits.LayerVersion(plain, Pioneer, TerrainEdits.BuildLayer));
            ulong island = TerrainStore.CellId(TerrainEdits.Level, CellX0 + 100, 188, CellZ0 + 100);
            Dig(island, Island, 3);
            Assert.Equal(3u, TerrainEdits.LayerVersion(island, Island, TerrainEdits.DigLayer));
            Assert.Equal(3u, Manifest(Island, island));
        }

        // ---- helpers -------------------------------------------------------------------------------------------------

        private static object ZoneObject(uint zone)
        {
            var zones = (IDictionary)Static("zones").GetValue(null)!;
            lock (Static("sync").GetValue(null)!)
                return zones[zone] ?? throw new InvalidOperationException($"no vein layer for {zone}");
        }

        private static T Field<T>(object zone, string name) => (T)zone.GetType().GetField(name)!.GetValue(zone)!;

        /// <summary>OreVeins.GeneratedSolid at a local point.</summary>
        private static bool GeneratedSolid(object zone, float x, float y, float z)
            => (bool)typeof(OreVeins).GetMethod("GeneratedSolid", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[]
            {
                zone, Origin.X + x * 10.0, Origin.Y + y * 10.0, Origin.Z + z * 10.0, new Dictionary<(int, int), float?>()
            })!;

        /// <summary>A one-voxel dig-layer edit whose layer version the client gives as <paramref name="version"/>.</summary>
        private void Dig(ulong cell, uint zone, uint version)
        {
            var result = new BlockData();
            result.SetSolid(100, 7);
            TerrainEdits.Commit(cell, new[] { 100 }, result, zone, owner: 1, layer: TerrainEdits.DigLayer, clientVersion: version);
            edited.Add((cell, zone));
        }

        private static uint Manifest(uint zone, ulong cell)
            => TerrainEdits.ManifestEntries(zone, TerrainEdits.DigLayer).Single(e => e.Cell == cell).Version;

        /// <summary>A TerrainHeights file ("LMH1" header, then deflate u16 heights and surface codes), flat at 1010 m.</summary>
        private static void WriteHeights(string path, int size, float step)
        {
            using var w = new BinaryWriter(File.Create(path));
            w.Write("LMH1".ToCharArray());
            w.Write(size); w.Write(step); w.Write(-1000f); w.Write(-1000f); w.Write(0f); w.Write(0.1f);
            for (int block = 0; block < 2; block++)
            {
                var raw = new byte[size * size * 2];
                for (int i = 0; i < size * size; i++)
                    BitConverter.GetBytes((ushort)(block == 0 ? 10100 : 1)).CopyTo(raw, i * 2);
                using var packed = new MemoryStream();
                using (var deflate = new System.IO.Compression.DeflateStream(packed, System.IO.Compression.CompressionLevel.Fastest, true))
                    deflate.Write(raw);
                w.Write((int)packed.Length);
                w.Write(packed.ToArray());
            }
        }


        private static double Local(double globalY) => (globalY - Origin.Y) / 10;

        private static List<OreVeins.Vein> Veins(uint zone)
            => OreVeins.Near(InRegion, 10000, zone).Select(n => n.Vein).Where(v => !v.Cave).ToList();

        /// <summary>Every ore voxel of a landscape: its top face (local m).</summary>
        private static List<(ulong Cell, int Index, float Top)> OreVoxels(uint zone)
        {
            var list = new List<(ulong, int, float)>();
            foreach ((ulong cell, _) in OreVeins.Entries(zone))
            {
                var blocks = new BlockData();
                OreVeins.Compose(cell, zone, blocks);
                (_, _, uint cy, _) = TerrainStore.Decode(cell);
                for (int i = 0; i < BlockData.Count; i++)
                    if ((blocks.Flags[i] & 1) != 0 && blocks.Materials[i] != 0)
                    {
                        int y = i % BlockData.Size;
                        list.Add((cell, i, (float)Local(cy * VeinGenerator.CellSize + (y + 1) * VeinGenerator.VoxelSize)));
                    }
            }
            return list;
        }

        private static int VoxelsOf(uint zone, OreVeins.Vein vein)
            => vein.Cells.Sum(cell => Enumerable.Range(0, BlockData.Count).Count(i => OreVeins.OreAt(cell, i, zone) != 0));

        private static ulong[] PoolCells(uint y)
        {
            var cells = new List<ulong>();
            for (uint x = 0; x < 16; x++)
                for (uint z = 0; z < 16; z++)
                    cells.Add(TerrainStore.CellId(TerrainEdits.Level, CellX0 + x, y, CellZ0 + z));
            return cells.OrderBy(c => c).ToArray();
        }

        /// <summary>The client's vein rows the test uses (ResourceVeins 233, 236, 237, 247; the columns VeinGenerator reads).</summary>
        private static Dictionary<uint, VeinGenerator.Definition> Definitions()
        {
            VeinGenerator.Definition Row(uint id, string name, int width, int length, double distance, double radius, double rate,
                ushort material, double variable, double vmin, double vmax, double edge, int taper, int straight, bool swoopy)
            {
                var d = new VeinGenerator.Definition
                {
                    Id = id, Name = name, Width = width, Length = length, WanderDistance = distance, WanderRadius = radius,
                    WanderRate = rate, VariableWidth = variable, VerticalMin = vmin, VerticalMax = vmax, Edge = edge, Taper = taper,
                    Straight = straight, Swoopy = swoopy, DensitySurface = 1
                };
                d.Materials[0] = material;
                d.Percents[0] = 100;
                return d;
            }
            return new Dictionary<uint, VeinGenerator.Definition>
            {
                [233] = Row(233, "v2: metal: copper: common", 5, 20, 0.05, 0.01, 0.01, 179, 0.5, 10, 90, 0.5, 0, 0, true),
                [236] = Row(236, "v2: stone: amaranthine: uncommon", 7, 18, 7, 3, 1.2, 53, 0.2, 35, 65, 0, 2, 10, false),
                [237] = Row(237, "v2: gem: agate: common", 8, 6, 7, 2, 1.2, 31, 0.1, 5, 15, 0.1, 2, 0, false),
                [247] = Row(247, "v2: metal: iron: common", 5, 20, 0.05, 0.01, 0.01, 181, 0.5, 10, 90, 0.5, 2, 0, true),
            };
        }

        /// <summary>A surface cache in the client's layout (LOD 0 only): the islands' header, 501 × 501 texels.</summary>
        private void WriteSurfaceCache(uint continent, Func<int, int, ushort> texel)
        {
            string dir = Path.Combine(install, "CommonData", "Voxel", "surfaceCache");
            Directory.CreateDirectory(dir);
            using var w = new BinaryWriter(File.Create(Path.Combine(dir, $"ContinentSurfaceCache{continent}.csc")));
            w.Write((byte)1); w.Write(159650u); w.Write(171u); w.Write(159528u); w.Write(501u); w.Write(501u);
            for (int r = 0; r < 501; r++)
                for (int c = 0; c < 501; c++)
                    w.Write(texel(r, c));
        }

        /// <summary>A .vdt in the layout OreVeins.ReadVdt reads: one surface category, the four vein kinds, no cave pool.</summary>
        private static void WriteVdt(string path, ulong[] surface)
        {
            using var w = new BinaryWriter(File.Create(path));
            w.Write(0u); w.Write(0.0); w.Write(0.0); w.Write(0u);
            w.Write(0u);                                // layers
            w.Write(0u);                                // sets
            w.Write(0u); w.Write(0u);                   // two count maps
            foreach (ulong[] pool in new[] { surface, Array.Empty<ulong>() })
            {
                w.Write(1u);
                w.Write(0); w.Write(0); w.Write(0);
                w.Write((uint)pool.Length);
                foreach (ulong cell in pool)
                    w.Write(cell);
            }
            w.Write(0u);                                // category → set map
            uint[] kinds = { 233, 236, 237, 247 };
            w.Write((uint)kinds.Length);
            for (int i = 0; i < kinds.Length; i++)
            {
                w.Write(0u); w.Write(kinds[i]); w.Write(11u); w.Write(500u + (uint)i);
                w.Write(3u); w.Write(4); w.Write(0); w.Write(0);         // counts: surface, caves, void
                w.Write(2u); w.Write(40); w.Write(0);                   // attempts
                w.Write(0); w.Write(0); w.Write(0);
            }
        }
    }
}
