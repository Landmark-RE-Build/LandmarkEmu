using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Game.Terrain;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
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
    /// The full-site slate slab under a new Pioneer site (PioneerSlab "site", the default since 2026-11-01; Pioneer.Slab.cs,
    /// OPEN-LEADS L180 / L260, trace/0695/pioneer-slab-live.md): on every 2016 map view live's slab exactly fills the
    /// floor of the 210 m site box, one level top standing about 2 m proud, a face down to lower ground, a bank up to
    /// higher ground, nothing growing on it, the Starting Location on it 3 m in from the north-west corner.
    /// </summary>
    public class PioneerSiteSlabTests : IDisposable
    {
        private static int nextZone = 9800;
        private readonly uint zone = (uint)System.Threading.Interlocked.Increment(ref nextZone);
        private readonly FakeWorld world = new();
        private const ushort WorkedSlate = 26;
        private const float Ground = 1010f;
        /// <summary>The owner of the sites a test registers with Claims (one per test landscape).</summary>
        private readonly ulong owner;
        private readonly List<ClaimModel> registered = new();
        private readonly List<Props.PlacedProp> placed = new();
        private readonly HashSet<ulong> slabIds = new();
        private bool landscape;

        private static IDictionary Stamped => (IDictionary)Private.Field(typeof(Pioneer), "slabsStamped");
        private static List<ulong> Pending => (List<ulong>)Private.Field(typeof(Pioneer), "slabsPending");
        private static HashSet<ulong> Done => (HashSet<ulong>)Private.Field(typeof(Pioneer), "slabsDone");
        private static HashSet<ulong> Forced => (HashSet<ulong>)Private.Field(typeof(Pioneer), "slabsForced");
        private static object SlabSync => Private.Field(typeof(Pioneer), "slabSync");

        public PioneerSiteSlabTests()
        {
            owner = 0x7100_0000_0001_0000ul + zone;
            if (EnvironmentServer.Instance == null)
                EnvironmentServer.Connect(new LocalEnvironmentServer((z, min, max) => new()));
            TerrainHeights.TestGround = (x, z, z0) => z0 == zone ? Ground : null;
        }

        public void Dispose()
        {
            Pioneer.UpdateSlabs();   // finished stamps of this test (StampSlab queues their cells for the world tick)
            TerrainHeights.TestGround = null;
            CavePieces.TestAirSpans = null;
            var claims = (List<ClaimModel>)Private.Field(typeof(Claims), "claims");
            lock (Private.Field(typeof(Claims), "sync"))
                claims.RemoveAll(c => registered.Contains(c));
            var props = (List<Props.PlacedProp>)Private.Field(typeof(Props), "props");
            lock (Private.Field(typeof(Props), "sync"))
                props.RemoveAll(p => placed.Contains(p));
            if (landscape)
                ((Dictionary<uint, Landscapes.Zone>)Private.Field(typeof(Landscapes), "zones")).Remove(zone);
            lock (SlabSync)
                foreach (ulong id in slabIds)
                {
                    Stamped.Remove(id);
                    Pending.Remove(id);
                    Done.Remove(id);
                    Forced.Remove(id);
                }
            TerrainEdits.Flush();
            TerrainEdits.CompactNow(true);
            world.Dispose();
        }

        /// <summary>A site in Claims' own list (Claims.Get / ClaimAt / CentreOf find it), owned by this test's owner.</summary>
        private ClaimModel Register(ClaimModel claim)
        {
            claim.OwnerGuid = owner;
            lock (Private.Field(typeof(Claims), "sync"))
                ((List<ClaimModel>)Private.Field(typeof(Claims), "claims")).Add(claim);
            registered.Add(claim);
            slabIds.Add(claim.Id);
            return claim;
        }

        /// <summary>This test's landscape as a Pioneer land (StampSlab stamps only there).</summary>
        private void PioneerLand()
        {
            ((Dictionary<uint, Landscapes.Zone>)Private.Field(typeof(Landscapes), "zones"))[zone] = new Landscapes.Zone { Id = zone, Pioneer = true };
            landscape = true;
        }

        /// <summary>A placed Starting Location prop on this landscape (Props.StartingLocationOn finds it in its site).</summary>
        private Props.PlacedProp StartingLocation(Vector3 at)
        {
            var prop = new Props.PlacedProp
            {
                PropId = StoryTools.StartingLocationProp, OwnerGuid = owner, Zone = zone, Placed = true,
                Spawn = new PlayerUpdateAddLightweightProp { Guid = 0x2F00_0000ul + zone * 16 + (ulong)placed.Count, Position = new Vector4(at, 1f) }
            };
            lock (Private.Field(typeof(Props), "sync"))
                ((List<Props.PlacedProp>)Private.Field(typeof(Props), "props")).Add(prop);
            placed.Add(prop);
            return prop;
        }

        /// <summary>A site's slab state as the marker gives it: a "site" slab stamped with this bottom layer.</summary>
        private void StampedAs(ClaimModel site, long bottom)
        {
            slabIds.Add(site.Id);
            lock (SlabSync)
                Stamped[site.Id] = ("site", bottom);
        }

        private static void Lift(ClaimModel site) => Private.Call(typeof(Pioneer), "LiftStartingLocation", site.Id);

        private static Vector3 At(Props.PlacedProp prop) => new(prop.Spawn.Position.X, prop.Spawn.Position.Y, prop.Spawn.Position.Z);

        /// <summary>Writes stored solids (flags 3, centred) into layer 0 at global voxels, as an older stamp left them.</summary>
        private void WriteLayer0(IEnumerable<(long X, long Y, long Z)> voxels, ushort material = WorkedSlate)
        {
            byte c = BlockData.CentreOffset;
            foreach (var cell in voxels.GroupBy(p => TerrainStore.CellId(TerrainEdits.Level, (uint)(p.X / 40), (uint)(p.Y / 40), (uint)(p.Z / 40))))
                TerrainEdits.Write(cell.Key, cell.Select(p => new TerrainEdits.VoxelValue(
                    TerrainEdits.VoxelIndex((int)(p.X % 40), (int)(p.Y % 40), (int)(p.Z % 40)), 3, material, c, c, c)).ToList(),
                    zone, 0, TerrainEdits.PoiLayer);
        }

        /// <summary>Every layer-0 cell this landscape holds.</summary>
        private List<ulong> Layer0Cells() => TerrainEdits.ManifestEntries(zone, TerrainEdits.PoiLayer).Select(e => e.Cell).ToList();

        /// <summary>
        /// A site as Claims.Grant makes it: centred on (x, ground, z) local metres, <paramref name="half"/> dm each way on x
        /// / z (1050 = the 210 m launch site; the box edges fall on half voxels, as a granted Pioneer site's do), 180 m high.
        /// </summary>
        private ClaimModel Site(float x, float z, double half = 1050)
        {
            var g = TerrainEdits.ToGlobal(new Vector3(x, Ground, z));
            return new ClaimModel
            {
                Id = 0x5AB0_0000 + zone, ZoneId = zone, OwnerGuid = 0x7100_0000_0000_0099,
                MinX = g.X - half, MaxX = g.X + half, MinY = g.Y - 900, MaxY = g.Y + 900, MinZ = g.Z - half, MaxZ = g.Z + half
            };
        }

        /// <summary>Every stored layer-0 voxel of the cells (global voxel x / y / z and its value).</summary>
        private IEnumerable<(long X, long Y, long Z, TerrainEdits.VoxelValue V)> Voxels(IEnumerable<ulong> cells)
        {
            byte[] all = Enumerable.Repeat((byte)0xFF, BlockData.Count / 8).ToArray();
            foreach (ulong cell in cells)
            {
                (_, uint cx, uint cy, uint cz) = TerrainStore.Decode(cell);
                foreach (TerrainEdits.VoxelValue v in TerrainEdits.Read(cell, all, zone, TerrainEdits.PoiLayer))
                    if (v.Flags != 0)
                        yield return (cx * 40L + v.Index / 40 % 40, cy * 40L + v.Index % 40, cz * 40L + v.Index / 1600, v);
            }
        }

        /// <summary>The generated ground's top solid sample in a column (samples sit at n × 0.4 m global; solid below the height).</summary>
        private static long GroundSample(float height) => (long)Math.Ceiling((height * 10 + TerrainStore.ZoneOrigin.Y) / TerrainEdits.VoxelSize) - 1;

        private static float LocalX(long gx) => (float)((gx * TerrainEdits.VoxelSize - TerrainStore.ZoneOrigin.X) / 10);

        [Fact]
        public void FlatGround_TheSlabCoversExactlyTheFootprint_InWorkedSlate_NothingOutside()
        {
            ClaimModel site = Site(390f, 780f);
            Pioneer.SlabBox box = Pioneer.SiteSlabOf(site);
            // 525 × 525 sample columns, the faces (half a voxel out) on the box's outline.
            Assert.Equal(525, box.Width);
            Assert.Equal(525, box.Depth);
            Assert.Equal(site.MinX, (box.X0 - 0.5) * TerrainEdits.VoxelSize, 6);
            Assert.Equal(site.MaxX, (box.X0 + box.Width - 0.5) * TerrainEdits.VoxelSize, 6);
            Assert.Equal(site.MinZ, (box.Z0 - 0.5) * TerrainEdits.VoxelSize, 6);
            Assert.Equal(site.MaxZ, (box.Z0 + box.Depth - 0.5) * TerrainEdits.VoxelSize, 6);
            // Its bottom layer is the first air sample above the ground; the top stands 1.8-2.2 m proud.
            Assert.Equal(GroundSample(Ground) + 1, box.Bottom);
            Assert.InRange(box.SurfaceY - Ground, 1.79f, 2.21f);   // 1.8 m when the ground is on a sample, up to 2.2 m

            var watch = System.Diagnostics.Stopwatch.StartNew();
            (List<ulong> cells, string report) = Pioneer.StampSiteSlab(site, WorkedSlate);
            Console.WriteLine($"    site slab (flat): {report}; {watch.ElapsedMilliseconds} ms");

            // Only these cells hold anything on this landscape.
            Assert.Equal(cells.OrderBy(c => c), TerrainEdits.ManifestEntries(zone, TerrainEdits.PoiLayer).Select(e => e.Cell).OrderBy(c => c));
            var solidPerColumn = new int[box.Width * box.Depth];
            long outside = 0, other = 0;
            foreach ((long x, long y, long z, TerrainEdits.VoxelValue v) in Voxels(cells))
            {
                long i = x - box.X0, k = z - box.Z0;
                if (i < 0 || i >= box.Width || k < 0 || k >= box.Depth)
                {
                    outside++;
                    continue;
                }
                // A flat box: every voxel sample + centred vertex, no vertex-only ones; slate from 2 voxels into the ground
                // up to the top, nothing above it on level ground.
                if (v.Flags != 3 || v.Material != WorkedSlate || v.O0 != BlockData.CentreOffset || v.O1 != BlockData.CentreOffset
                    || v.O2 != BlockData.CentreOffset || y < box.Bottom - 2 || y > box.Top)
                    other++;
                else
                    solidPerColumn[k * box.Width + i]++;
            }
            Assert.Equal(0, outside);
            Assert.Equal(0, other);
            Assert.All(solidPerColumn, n => Assert.Equal(Pioneer.SlabLayers + 2, n));
        }

        [Fact]
        public void LowerGround_ColumnsAreFilledDownToTheGround_TheTopStaysLevel()
        {
            // Level at the middle, falling 0.5 m per metre from 5 m east of it: 7.5 m lower at the box's east edge.
            ClaimModel site = Site(0f, 0f, half: 202);
            float middle = (float)(((site.MinX + site.MaxX) / 2 - TerrainStore.ZoneOrigin.X) / 10);
            float Height(float x) => x <= middle + 5 ? Ground : Ground - (x - middle - 5) * 0.5f;
            TerrainHeights.TestGround = (x, z, z0) => z0 == zone ? Height(x) : null;
            Pioneer.SlabBox box = Pioneer.SiteSlabOf(site);
            Assert.Equal(101, box.Width);

            (List<ulong> cells, _) = Pioneer.StampSiteSlab(site, WorkedSlate);
            var stored = Voxels(cells).ToDictionary(v => (v.X, v.Y, v.Z), v => v.V);
            for (long i = 0; i < box.Width; i += 10)
            {
                long gx = box.X0 + i, gz = box.Z0 + 50;
                long ground = GroundSample(Height(LocalX(gx)));
                // Solid slate without a gap from the ground (2 voxels into it) up to the level top; nothing above the top.
                for (long y = Math.Min(box.Bottom, ground + 1) - 2; y <= box.Top; y++)
                    Assert.True(stored.TryGetValue((gx, y, gz), out var v) && v.Flags == 3 && v.Material == WorkedSlate,
                        $"column {i} (ground sample {ground}): no slate at layer {y}");
                Assert.False(stored.ContainsKey((gx, box.Top + 1, gz)));
            }
            // The east edge stands 7 m and more above its ground: a face all the way down, and nothing outside the box.
            long east = box.X0 + box.Width - 1;
            Assert.True(box.Bottom - GroundSample(Height(LocalX(east))) > 15);
            Assert.False(stored.Keys.Any(p => p.X < box.X0 || p.X > east || p.Z < box.Z0 || p.Z >= box.Z0 + box.Depth));
        }

        [Fact]
        public void HigherGround_IsCutAwayAboveTheTop_LeavingABankOnTheOutline()
        {
            // Level at the middle, rising 0.5 m per metre from 5 m east of it: 7.5 m higher at the box's east edge.
            ClaimModel site = Site(0f, 0f, half: 202);
            float middle = (float)(((site.MinX + site.MaxX) / 2 - TerrainStore.ZoneOrigin.X) / 10);
            float Height(float x) => x <= middle + 5 ? Ground : Ground + (x - middle - 5) * 0.5f;
            TerrainHeights.TestGround = (x, z, z0) => z0 == zone ? Height(x) : null;
            Pioneer.SlabBox box = Pioneer.SiteSlabOf(site);

            (List<ulong> cells, _) = Pioneer.StampSiteSlab(site, WorkedSlate);
            var stored = Voxels(cells).ToDictionary(v => (v.X, v.Y, v.Z), v => v.V);
            bool Solid(long x, long y, long z)
                => stored.TryGetValue((x, y, z), out var v) ? (v.Flags & 1) != 0 && v.Material != 0
                    : y <= GroundSample(Height(LocalX(x)));   // not written: the generated ground shows
            int cut = 0;
            for (long i = 0; i < box.Width; i++)
            {
                long gx = box.X0 + i, gz = box.Z0 + 40;
                long ground = GroundSample(Height(LocalX(gx)));
                // The slab's own layers are slate; above the top no ground is left (up to 2 voxels over the generated one).
                for (long y = box.Bottom; y <= box.Top; y++)
                    Assert.True(Solid(gx, y, gz) && stored[(gx, y, gz)].Material == WorkedSlate);
                for (long y = box.Top + 1; y <= ground + 2; y++)
                {
                    Assert.False(Solid(gx, y, gz), $"column {i}: ground left at layer {y} (top {box.Top}, ground {ground})");
                    Assert.Equal((byte)3, stored[(gx, y, gz)].Flags);
                    cut++;
                }
            }
            Assert.True(cut > 100);
            // The ground just outside the east edge is untouched: it stands as a bank above the slab's top.
            long outsideX = box.X0 + box.Width;
            Assert.False(stored.Keys.Any(p => p.X == outsideX));
            Assert.True(GroundSample(Height(LocalX(outsideX))) > box.Top + 10);
        }

        [Fact]
        public void TheStartingLocation_StandsOnTheSlab_3mFromTheNorthWestCorner()
        {
            ClaimModel site = Site(390f, 780f);
            Pioneer.SlabBox box = Pioneer.SiteSlabOf(site);
            var stamped = (IDictionary)Private.Field(typeof(Pioneer), "slabsStamped");
            var pending = (List<ulong>)Private.Field(typeof(Pioneer), "slabsPending");
            object sync = Private.Field(typeof(Pioneer), "slabSync");
            Assert.Null(Pioneer.StartingLocationOnSlab(site));   // no slab: the old rule (10 m in, on the ground)
            try
            {
                // Queued under PioneerSlab "site" (the default): known before the stamp is done.
                Assert.Equal("site", world.Config.PioneerSlab);
                lock (sync)
                    pending.Add(site.Id);
                Vector3? queued = Pioneer.StartingLocationOnSlab(site);
                lock (sync)
                {
                    pending.Remove(site.Id);
                    stamped[site.Id] = ("site", box.Bottom);
                }
                Vector3 at = Pioneer.StartingLocationOnSlab(site) ?? throw new InvalidOperationException("no spot on the slab");
                Assert.Equal((Vector3?)at, queued);
                var origin = TerrainStore.ZoneOrigin;
                // North-west = -x / +z (the client's compass: heading 0 = +z), 3 m in from both edges, on the top surface.
                Assert.Equal((float)((site.MinX - origin.X) / 10) + 3f, at.X, 3);
                Assert.Equal((float)((site.MaxZ - origin.Z) / 10) - 3f, at.Z, 3);
                Assert.Equal(box.SurfaceY, at.Y, 3);
                // ... which is the top of the stamped slab under that spot: its top layer is slate, nothing above it.
                (List<ulong> cells, _) = Pioneer.StampSiteSlab(site, WorkedSlate);
                long gx = (long)Math.Round((at.X * 10 + origin.X) / TerrainEdits.VoxelSize), gz = (long)Math.Round((at.Z * 10 + origin.Z) / TerrainEdits.VoxelSize);
                var column = Voxels(cells.Where(c => TerrainStore.Decode(c).X == gx / 40 && TerrainStore.Decode(c).Z == gz / 40))
                    .Where(v => v.X == gx && v.Z == gz).ToList();
                Assert.Equal(box.Top, column.Where(v => v.V.Material == WorkedSlate).Max(v => v.Y));
                Assert.Equal((float)(((box.Top + 0.5) * TerrainEdits.VoxelSize - origin.Y) / 10), at.Y, 3);
                // A template slab (flush with the ground) keeps the old rule.
                lock (sync)
                    stamped[site.Id] = ("claim3x3.otd", box.Bottom - 5);
                Assert.Null(Pioneer.StartingLocationOnSlab(site));
            }
            finally
            {
                lock (sync)
                {
                    pending.Remove(site.Id);
                    stamped.Remove(site.Id);
                }
            }
        }

        [Fact]
        public void TheMarker_KeepsEachSitesLastLine_AndTheSiteSlabsBottom()
        {
            string folder = Path.Combine(Path.GetTempPath(), "slabmarker-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            FieldInfo folderField = typeof(Pioneer).GetField("slabFolder", BindingFlags.NonPublic | BindingFlags.Static)!;
            object before = folderField.GetValue(null);
            var stamped = (IDictionary)Private.Field(typeof(Pioneer), "slabsStamped");
            var done = (HashSet<ulong>)Private.Field(typeof(Pioneer), "slabsDone");
            ulong a = 0x5AB1_0000 + zone, b = a + 1, c = a + 2;
            try
            {
                // A claim3x3 site queued again and stamped as "site" (pioneerslab again), one stamped long ago, one skipped.
                File.WriteAllLines(Path.Combine(folder, "pioneer_slabs.txt"), new[]
                {
                    $"pending\t{a}\t464", $"stamped\t{a}\t464\tclaim3x3.otd\t100\t2599\t200\t26",
                    $"pending\t{a}\t464", $"stamped\t{a}\t464\tsite\t101\t2604\t201\t26",
                    $"pending\t{b}\t465", $"stamped\t{b}\t465\tclaim1x1.otd\t100\t2599\t200\t26",
                    $"pending\t{c}\t466", $"skipped\t{c}\t466\tsite gone"
                });
                Pioneer.LoadSlabs(folder);
                Assert.Equal("done (site)", Pioneer.SlabState(a));
                Assert.Equal("done (claim1x1.otd)", Pioneer.SlabState(b));
                Assert.Equal("done", Pioneer.SlabState(c));
                Assert.Equal(Pioneer.SlabBox.SurfaceOf(2604), Pioneer.SiteSlabSurface(new ClaimModel { Id = a, ZoneId = 464 }));
                Assert.Null(Pioneer.SiteSlabSurface(new ClaimModel { Id = b, ZoneId = 465 }));
            }
            finally
            {
                folderField.SetValue(null, before);
                foreach (ulong id in new[] { a, b, c })
                {
                    stamped.Remove(id);
                    done.Remove(id);
                }
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void SitePlaced_TakesDecorationOutToo()
        {
            var rolled = (Dictionary<uint, string>)Private.Field(typeof(Gathering), "rolled");
            string generator = (string)typeof(Gathering).GetProperty("Generator", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
            lock (rolled)
                rolled[zone] = generator;
            Type nodeClass = Private.Nested(typeof(Gathering), "Node");
            Type nodeType = Private.Nested(typeof(Gathering), "NodeType");
            object Get(object node, string member)
                => nodeClass.GetField(member) is FieldInfo f ? f.GetValue(node) : nodeClass.GetProperty(member)!.GetValue(node);
            // Two standing trees of the flat test landscape; one is made decoration (a placement set's rock or bush).
            var trees = new List<object>();
            for (int rz = 0; rz < 32 && trees.Count < 2; rz++)
                for (int rx = 0; rx < 32 && trees.Count < 2; rx++)
                    foreach (object node in (IEnumerable)Private.Call(typeof(Gathering), "Region", zone, rx, rz))
                        if (Get(node, "Type").ToString() == "Tree" && (bool)Get(node, "Standing") && trees.Count < 2)
                            trees.Add(node);
            Assert.Equal(2, trees.Count);
            object decor = trees[0];
            nodeClass.GetField("Type")!.SetValue(decor, Enum.Parse(nodeType, "Decor"));
            var empty = (HashSet<ulong>)Private.Field(typeof(Gathering), "empty");
            foreach (object node in trees)
            {
                var spawn = (LandmarkEmulator.WorldServer.Network.Message.Model.PlayerUpdateAddLightweightProp)Get(node, "Spawn");
                var g = TerrainEdits.ToGlobal(new Vector3(spawn.Position.X, spawn.Position.Y, spawn.Position.Z));
                Gathering.SitePlaced(zone, (g.X - 100, g.Y - 100, g.Z - 100), (g.X + 100, g.Y + 900, g.Z + 100));
                Assert.False((bool)Get(node, "Standing"));
            }
            // Decoration takes no respawn pool's free spot (it is never gathered); it comes back with its region's next
            // generation, which keeps it out of the site.
            lock (Private.Field(typeof(Gathering), "sync"))
                Assert.DoesNotContain((ulong)Get(decor, "Key"), empty);
        }

        [Theory]
        [InlineData("site", "site", null)]
        [InlineData(" Site ", "site", null)]
        [InlineData("claim3x3", "claim3x3.otd", "claim3x3.otd")]
        [InlineData("claim1x1", "claim1x1.otd", "claim1x1.otd")]
        [InlineData("off", null, null)]
        [InlineData("claim2x1", null, null)]
        [InlineData(null, null, null)]
        public void TheSetting_SiteIsTheDefault_TheTemplatesStillWork(string setting, string kind, string file)
        {
            Assert.Equal("site", new WorldServerConfiguration().PioneerSlab);
            Assert.Equal(kind, Pioneer.SlabKind(setting));
            Assert.Equal(file, Pioneer.SlabTemplateFile(setting));
        }

        // ---- review 2026-11-01 ------------------------------------------------------------------------------------------

        [Fact]
        public void AReusedClaimId_KeepsNothingOfTheOldSitesSlab()
        {
            // Claims.Load hands ids out again from the highest stored one: a deleted newest site's id, or every id from 2
            // after a wiped claims table, comes back as a new site that must not inherit the old one's marker line (no
            // slab of its own, its Starting Location and arrival at the old slab's height).
            string folder = Path.Combine(Path.GetTempPath(), "slabmarker-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string marker = Path.Combine(folder, "pioneer_slabs.txt");
            FieldInfo folderField = typeof(Pioneer).GetField("slabFolder", BindingFlags.NonPublic | BindingFlags.Static)!;
            FieldInfo nextIdField = typeof(Claims).GetField("nextId", BindingFlags.NonPublic | BindingFlags.Static)!;
            object folderBefore = folderField.GetValue(null), nextBefore = nextIdField.GetValue(null);
            ClaimModel site = Site(390f, 780f);
            ulong id = site.Id;
            slabIds.Add(id);
            string stampedLine = $"stamped\t{id}\t{zone}\tsite\t1\t2604\t2\t26";
            try
            {
                File.WriteAllLines(marker, new[] { $"pending\t{id}\t{zone}", stampedLine });
                Pioneer.LoadSlabs(folder);
                Assert.Equal("done (site)", Pioneer.SlabState(id));

                // Deleted (Claims.Remove): forgotten at once, and after a restart.
                Register(site);
                Private.Call(typeof(Claims), "Remove", new List<ClaimModel> { site }, "test", false);
                Assert.Equal("none", Pioneer.SlabState(id));
                Assert.Null(Pioneer.SiteSlabSurface(site));
                Assert.True(File.ReadAllLines(marker).Last().StartsWith($"removed\t{id}\t{zone}\tsite removed: test"));
                Pioneer.LoadSlabs(folder);
                Assert.Equal("none", Pioneer.SlabState(id));

                // A wiped claims table (nothing removed, the stamped line last): the id is handed out again (Claims.Grant).
                File.AppendAllLines(marker, new[] { stampedLine });
                Pioneer.LoadSlabs(folder);
                Assert.Equal("done (site)", Pioneer.SlabState(id));
                nextIdField.SetValue(null, (long)id - 1);
                var g = TerrainEdits.ToGlobal(new Vector3(0f, Ground, 0f));
                ClaimModel granted = Claims.Grant(owner, "Reused", zone, g.X, g.Y, g.Z);
                registered.Add(granted);
                Assert.Equal(id, granted.Id);
                Assert.Equal("none", Pioneer.SlabState(id));   // QueueSlab takes it (it refuses only pending / done sites)
                Assert.Null(Pioneer.SiteSlabSurface(granted));
                Assert.Null(Pioneer.StartingLocationOnSlab(granted));
                Assert.True(File.ReadAllLines(marker).Last().StartsWith($"removed\t{id}\t{zone}\tid handed out again"));
            }
            finally
            {
                nextIdField.SetValue(null, nextBefore);
                folderField.SetValue(null, folderBefore);
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void ThePendingForce_SurvivesARestart()
        {
            string folder = Path.Combine(Path.GetTempPath(), "slabmarker-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            FieldInfo folderField = typeof(Pioneer).GetField("slabFolder", BindingFlags.NonPublic | BindingFlags.Static)!;
            object folderBefore = folderField.GetValue(null);
            ulong id = 0x5AB3_0000 + zone;
            slabIds.Add(id);
            try
            {
                File.WriteAllLines(Path.Combine(folder, "pioneer_slabs.txt"), new[] { $"pending\t{id}\t{zone}\tforce" });
                // Held: LoadSlabs starts the queue in the background, which waits for the lock.
                lock (SlabSync)
                {
                    Pioneer.LoadSlabs(folder);
                    Assert.Contains(id, Pending);
                    Assert.Contains(id, Forced);
                }
                // The site doesn't exist here: the stamp skips it ("site gone") and drops the force.
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (Pioneer.SlabState(id) != "done" && watch.ElapsedMilliseconds < 10000)
                    System.Threading.Thread.Sleep(20);
                Assert.Equal("done", Pioneer.SlabState(id));
                lock (SlabSync)
                    Assert.DoesNotContain(id, Forced);
            }
            finally
            {
                folderField.SetValue(null, folderBefore);
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void ACavernSite_IsNeitherQueuedNorStamped_AndItsStartingLocationStaysInItsBox()
        {
            ClaimModel surface = Site(390f, 780f);
            // A cavern site (Place step 2): its box centred on a CavernMiddle01 piece 100 m under the baked ground.
            ClaimModel cavern = Site(0f, 0f, half: 202);
            cavern.MinY -= 1000;
            cavern.MaxY -= 1000;
            Assert.True(Pioneer.IsSurfaceSite(surface));
            Assert.False(Pioneer.IsSurfaceSite(cavern));
            Assert.False(Pioneer.QueueSlab(cavern, again: true));

            // A "pending" line of an earlier run for it: no slab surface meanwhile, and its stamp is skipped.
            PioneerLand();
            Register(cavern);
            lock (SlabSync)
                Pending.Add(cavern.Id);
            Assert.Null(Pioneer.SiteSlabSurface(cavern));
            Private.Call(typeof(Pioneer), "StampSlab", cavern.Id);
            Assert.Equal("done", Pioneer.SlabState(cavern.Id));
            Assert.Empty(Layer0Cells());

            // A "stamped" line from before this rule (the slab's height from the surface, above the box): its Starting
            // Location in the cavern is not moved up out of the box (it would no longer be the site's).
            Pioneer.SlabBox box = Pioneer.SiteSlabOf(cavern);
            StampedAs(cavern, box.Bottom);
            float maxY = (float)((cavern.MaxY - TerrainStore.ZoneOrigin.Y) / 10);
            Assert.True(box.SurfaceY > maxY);
            Props.PlacedProp start = StartingLocation(new Vector3(0f, maxY - 1f, 0f));
            Lift(cavern);
            Assert.Equal(maxY - 1f, At(start).Y, 1e-3f);
        }

        [Fact]
        public void TheStartingLocation_GoesOntoTheSlab_FromUnderIt_AndFromGroundTheCutTookAway_NotFromABuild()
        {
            // An old site's Starting Location stood 10 m in from the -x / +z corner on the generated ground, which there
            // rises 4 m over the middle (FindSpot lets a corner rise 6 m): the cut takes that ground away down to the slab.
            ClaimModel site = Register(Site(0f, 0f, half: 202));
            var origin = TerrainStore.ZoneOrigin;
            float minX = (float)((site.MinX - origin.X) / 10), maxZ = (float)((site.MaxZ - origin.Z) / 10);
            TerrainHeights.TestGround = (x, z, z0) => z0 != zone ? null : x < minX + 15f && z > maxZ - 15f ? Ground + 4f : Ground;
            Pioneer.SlabBox box = Pioneer.SiteSlabOf(site);
            StampedAs(site, box.Bottom);
            float top = box.SurfaceY;
            Assert.True(Ground + 4f > top + 1f);

            Props.PlacedProp start = StartingLocation(new Vector3(minX + 10f, Ground + 4f, maxZ - 10f));
            Lift(site);
            Assert.Equal(new Vector3(minX + 10f, top, maxZ - 10f), At(start));   // down onto the slab, x / z kept

            // One on the owner's build 3 m over that ground stays.
            start.Spawn.Position = new Vector4(minX + 10f, Ground + 7f, maxZ - 10f, 1f);
            Lift(site);
            Assert.Equal(Ground + 7f, At(start).Y, 1e-3f);

            // One on the old ground under the slab (the level middle) goes up.
            start.Spawn.Position = new Vector4(1f, Ground, 2f, 1f);
            Lift(site);
            Assert.Equal(new Vector3(1f, top, 2f), At(start));
        }

        [Fact]
        public void AStamp_PutsTheStartingLocationOnTheSlab_AndArrivalsStandOnIt()
        {
            // End to end: StampSlab (the "site" kind) → the world tick (UpdateSlabs: LiftStartingLocation) → the arrivals.
            PioneerLand();
            ClaimModel site = Register(Site(0f, 0f, half: 202));
            Pioneer.SlabBox box = Pioneer.SiteSlabOf(site);
            var origin = TerrainStore.ZoneOrigin;
            float minX = (float)((site.MinX - origin.X) / 10), maxZ = (float)((site.MaxZ - origin.Z) / 10);
            var arrival = new Vector3(1f, Ground + 1f, 2f);
            // No slab yet (not queued): the old rule, 10 m in on the ground, and the arrival as FindPlace gave it.
            Assert.Equal(new Vector3(minX + 10f, Ground, maxZ - 10f), Claims.StartingLocationSpot(site));
            Assert.Equal(arrival, Pioneer.SlabArrival(site, arrival));
            Props.PlacedProp start = StartingLocation(Claims.StartingLocationSpot(site));

            Private.Call(typeof(Pioneer), "StampSlab", site.Id);
            Assert.Equal("done (site)", Pioneer.SlabState(site.Id));
            Assert.NotEmpty(Layer0Cells());
            Pioneer.UpdateSlabs();
            Assert.Equal(new Vector3(minX + 10f, box.SurfaceY, maxZ - 10f), At(start));   // lifted out of the slab onto it

            // A missing one goes on the slab 3 m in; travel / revive's centre and a first login stand over the slab.
            Assert.Equal(Pioneer.StartingLocationOnSlab(site), Claims.StartingLocationSpot(site));
            Assert.Equal(box.SurfaceY + 1f, Claims.CentreOf(owner)?.Position.Y ?? 0f, 1e-3f);
            Assert.Equal(new Vector3(arrival.X, box.SurfaceY + Pioneer.FirstLoginDrop, arrival.Z), Pioneer.SlabArrival(site, arrival));
        }

        [Fact]
        public void ASiteWithBuilding_IsNotStamped_UnlessForced()
        {
            // The slab goes into layer 0 and shows wherever layer 1 has no voxel (block layers merge per voxel): over a
            // build its rooms would fill with slate and the cut would take the ground from under it.
            PioneerLand();
            ClaimModel site = Register(Site(0f, 0f, half: 202));
            Assert.False(Pioneer.HasBuilding(site));
            Pioneer.SlabBox box = Pioneer.SiteSlabOf(site);
            long gx = box.X0 + 30, gy = box.Bottom + 1, gz = box.Z0 + 30;
            ulong cell = TerrainStore.CellId(TerrainEdits.Level, (uint)(gx / 40), (uint)(gy / 40), (uint)(gz / 40));
            byte c = BlockData.CentreOffset;
            TerrainEdits.Write(cell, new List<TerrainEdits.VoxelValue>
                { new(TerrainEdits.VoxelIndex((int)(gx % 40), (int)(gy % 40), (int)(gz % 40)), 3, 7, c, c, c) }, zone, owner, TerrainEdits.BuildLayer);
            Assert.True(Pioneer.HasBuilding(site));
            Assert.False(Pioneer.HasBuilding(Site(100f, 0f, half: 202)));   // a box beside it

            lock (SlabSync)
                Pending.Add(site.Id);
            Private.Call(typeof(Pioneer), "StampSlab", site.Id);
            Assert.Equal("done", Pioneer.SlabState(site.Id));   // skipped
            Assert.Empty(Layer0Cells());

            // "pioneerslab again force": stamped over it.
            lock (SlabSync)
                Forced.Add(site.Id);
            Private.Call(typeof(Pioneer), "StampSlab", site.Id);
            Assert.Equal("done (site)", Pioneer.SlabState(site.Id));
            Assert.NotEmpty(Layer0Cells());
            lock (SlabSync)
                Assert.DoesNotContain(site.Id, Forced);
            TerrainEdits.ClearCell(cell, zone);
        }

        [Fact]
        public void Claim3x3_LinesUpWithTheFootprint_AndARestampClearsTheColumnTheOldRoundingLeftOutside()
        {
            var size = (X: 525u, Y: 5u, Z: 524u);   // claim3x3.otd
            // Grid sites (390 m apart: ToGlobal and HalfSize are exact): the centre voxel is odd at 390 m, even at 780 m.
            foreach (float x in new[] { 390f, 780f })
            {
                ClaimModel grid = Site(x, 780f);
                Pioneer.SlabBox b = Pioneer.SiteSlabOf(grid);
                (long vx, _, long vz) = Pioneer.TemplateCorner(grid, size);
                Assert.Equal(b.X0, vx + 1);                         // template x = 1..525 on the footprint's columns
                Assert.Equal(b.X0 + b.Width - 1, vx + 525);
                Assert.InRange(vz + 1, b.Z0, b.Z0 + b.Depth - 1);   // z = 1..524 inside
                Assert.InRange(vz + 524, b.Z0, b.Z0 + b.Depth - 1);
            }

            // At 780 m the old rounding (half to even) put claim3x3's x = 1 one column out over the -x outline.
            ClaimModel site = Site(780f, 780f);
            Pioneer.SlabBox box = Pioneer.SiteSlabOf(site);
            double v = TerrainEdits.VoxelSize;
            double centre = (site.MinX + site.MaxX) / 2 / v;
            Assert.Equal(box.X0 - 1, (long)Math.Round(centre - (525 + 2) / 2.0) + 1);
            long oldTop = (long)Math.Floor((site.MinY + site.MaxY) / 2 / v);
            // What it left there (its full-width row, 175 rows, 5 layers), and a stored voxel two columns out that stays.
            var leftover = new List<(long X, long Y, long Z)>();
            for (long z = box.Z0 + 175; z < box.Z0 + 350; z++)
                for (long y = oldTop - 4; y <= oldTop; y++)
                    leftover.Add((box.X0 - 1, y, z));
            (long X, long Y, long Z) other = (box.X0 - 2, oldTop, box.Z0 + 200);
            WriteLayer0(leftover.Append(other));

            (List<ulong> cells, string report) = Pioneer.StampSiteSlab(site, WorkedSlate);
            var stored = Voxels(Layer0Cells()).Select(p => (p.X, p.Y, p.Z)).ToHashSet();
            Assert.False(stored.Any(p => p.X == box.X0 - 1));
            Assert.Contains(other, stored);
            Assert.Contains($"{leftover.Count} voxel(s) of an older claim3x3 stamp outside the -x face cleared", report);
            foreach (ulong cell in leftover.Select(p => TerrainStore.CellId(TerrainEdits.Level, (uint)(p.X / 40), (uint)(p.Y / 40), (uint)(p.Z / 40))).Distinct())
                Assert.Contains(cell, cells);   // sent out with the stamp

            // An odd grid column's site: the old stamp was inside, and that column is never touched.
            ClaimModel odd = Site(390f, 780f);
            odd.Id++;
            Pioneer.SlabBox oddBox = Pioneer.SiteSlabOf(odd);
            long oddTop = (long)Math.Floor((odd.MinY + odd.MaxY) / 2 / v);
            WriteLayer0(new[] { (oddBox.X0 - 1, oddTop, oddBox.Z0 + 200) });
            (_, string oddReport) = Pioneer.StampSiteSlab(odd, WorkedSlate);
            Assert.False(oddReport.Contains("claim3x3"));
            Assert.Contains((oddBox.X0 - 1, oddTop, oddBox.Z0 + 200), Voxels(Layer0Cells()).Select(p => (p.X, p.Y, p.Z)));
        }

        [Fact]
        public void OffGridEdges_EveryVoxelWrittenHasItsCentreInsideTheBox_SoTheHealKeepsIt()
        {
            // A placed site's centre isn't snapped (Claims.PlaceConfirm keeps the client's f64): x / z edges 0.08 m under
            // a half voxel, the box top 0.08 m over a sample, and ground rising through the top towards the east edge.
            double v = TerrainEdits.VoxelSize;
            ClaimModel grid = Site(0f, 0f, half: 202);
            double groundGlobal = Ground * 10 + TerrainStore.ZoneOrigin.Y;
            var site = new ClaimModel
            {
                Id = grid.Id, ZoneId = zone, OwnerGuid = grid.OwnerGuid,
                MinX = grid.MinX - 0.8, MaxX = grid.MaxX - 0.8, MinZ = grid.MinZ - 0.8, MaxZ = grid.MaxZ - 0.8,
                MinY = grid.MinY, MaxY = Math.Floor((groundGlobal + 40) / v) * v + 0.8
            };
            float middle = (float)(((site.MinX + site.MaxX) / 2 - TerrainStore.ZoneOrigin.X) / 10);
            float Height(float x) => x <= middle + 5 ? Ground : Ground + (x - middle - 5) * 0.5f;
            TerrainHeights.TestGround = (x, z, z0) => z0 == zone ? Height(x) : null;

            Pioneer.SlabBox box = Pioneer.SiteSlabOf(site);
            Assert.Equal(100, box.Width);   // the 101st column's centre would lie 0.08 m past MaxX
            Assert.Equal(100, box.Depth);
            (List<ulong> cells, _) = Pioneer.StampSiteSlab(site, WorkedSlate);
            long topLayer = (long)Math.Floor(site.MaxY / v) - 1;   // the highest layer whose centre is inside
            long highest = long.MinValue;
            foreach ((long x, long y, long z, _) in Voxels(cells))
            {
                // The heal's own test (TerrainEdits.Regenerate): every voxel's centre in the box.
                Assert.InRange((x + 0.5) * v, site.MinX, site.MaxX);
                Assert.InRange((y + 0.5) * v, site.MinY, site.MaxY);
                Assert.InRange((z + 0.5) * v, site.MinZ, site.MaxZ);
                highest = Math.Max(highest, y);
            }
            Assert.Equal(topLayer, highest);   // the cut reached the box's top
        }

        [Fact]
        public void CaveAir_UnderTheSlabIsNeverFilled_TheSlabsOwnLayersAreWhole()
        {
            ClaimModel site = Site(0f, 0f, half: 202);
            Pioneer.SlabBox box = Pioneer.SiteSlabOf(site);
            double v = TerrainEdits.VoxelSize;
            // A cave whose roof is the ground's top two samples under the first ten columns (the fill's two voxels).
            CavePieces.TestAirSpans = (gx, gz, z0, into) =>
            {
                if (z0 != zone)
                    return false;
                if (gx < (box.X0 + 10) * v - 0.5)
                    into.Add(((box.Bottom - 8) * v, (box.Bottom - 1) * v + 1));
                return true;
            };
            (List<ulong> cells, string report) = Pioneer.StampSiteSlab(site, WorkedSlate);
            var stored = Voxels(cells).ToDictionary(p => (p.X, p.Y, p.Z), p => p.V);
            for (long i = 0; i < 20; i++)
            {
                long gx = box.X0 + i, gz = box.Z0 + 40;
                for (long y = box.Bottom; y <= box.Top; y++)
                    Assert.True(stored.TryGetValue((gx, y, gz), out var s) && s.Material == WorkedSlate, $"column {i}: no slate at {y}");
                for (long y = box.Bottom - 2; y < box.Bottom; y++)
                    Assert.Equal(i >= 10, stored.ContainsKey((gx, y, gz)));
            }
            Assert.Contains($"{10 * box.Depth * 2} on cave air skipped", report);
        }

        [Fact]
        public void FinishedCells_ReachAPlayerStillLoading_AsManifestNotes()
        {
            // A cell changed in layer 0 (as a stamp writes it) after a player's zone-in manifest, before they entered.
            ulong cell = TerrainStore.CellId(TerrainEdits.Level, 3000, 1000, 3000);
            byte c = BlockData.CentreOffset;
            TerrainEdits.Write(cell, new List<TerrainEdits.VoxelValue> { new(TerrainEdits.VoxelIndex(1, 2, 3), 3, WorkedSlate, c, c, c) },
                zone, 0, TerrainEdits.PoiLayer);
            WorldSession Player(ulong guid, bool ready)
            {
                WorldSession s = world.Session(guid, "Loading" + (guid & 0xFF));
                ((CharacterModel)typeof(WorldSession).GetField("currentCharacter", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(s)).ZoneId = zone;
                s.ReadySent = ready;
                ZoneSessions.Add(s);
                return s;
            }
            WorldSession loading = Player(owner + 1, true), entered = Player(owner + 2, true), early = Player(owner + 3, false);
            var players = (List<WorldSession>)Private.Field(typeof(PlayerManager), "players");
            lock (Private.Field(typeof(PlayerManager), "sync"))
                players.Add(entered);
            try
            {
                foreach (WorldSession s in new[] { loading, entered, early })
                    FakeWorld.Outgoing(s);
                ZoneTerrain.NoteLoading(new List<ulong> { cell }, zone);
                var notes = FakeWorld.Outgoing(loading).Where(p => p.Opcode == ZoneMessageOpcode.TerrainCellModifiedCellManifestUpdate).ToList();
                Assert.Single(notes);
                Assert.Equal(cell, BitConverter.ToUInt64(notes[0].Body, 2));
                Assert.Equal(TerrainEdits.PoiLayer + 1, BitConverter.ToUInt32(notes[0].Body, 10));
                Assert.Equal(TerrainEdits.LayerVersion(cell, zone, TerrainEdits.PoiLayer), BitConverter.ToUInt32(notes[0].Body, 14));
                Assert.Empty(FakeWorld.Outgoing(entered));   // Broadcast reaches it
                Assert.Empty(FakeWorld.Outgoing(early));     // its manifest will list the cell
            }
            finally
            {
                lock (Private.Field(typeof(PlayerManager), "sync"))
                    players.Remove(entered);
                foreach (WorldSession s in new[] { loading, entered, early })
                    ZoneSessions.Remove(s);
                TerrainEdits.ClearCell(cell, zone);
            }
        }
    }
}
