using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Game.Combat;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// Geometry the server decides with: the arrival facing (Teleport), the melee sweep's cone check, the block stance's arc,
    /// build-site boxes (global decimetres), prop-stream regions and the moving platforms' motion.
    /// </summary>
    public class GeometryRuleTests
    {
        // ---- Teleport.FacingVector ----------------------------------------------------------------------------------

        public static IEnumerable<object[]> Facings()
        {
            yield return new object[] { "identity", Quaternion.Identity };
            yield return new object[] { "zero", new Quaternion(0, 0, 0, 0) };
            yield return new object[] { "NaN", new Quaternion(float.NaN, 0, 0, 1) };
            yield return new object[] { "infinite", new Quaternion(float.PositiveInfinity, 0, 0, 1) };
            yield return new object[] { "pitched up 90", Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2) };
            yield return new object[] { "pitched down 90", Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2) };
            yield return new object[] { "not normalised", new Quaternion(0, 5, 0, 5) };
            yield return new object[] { "tiny", new Quaternion(1e-30f, 0, 0, 1e-30f) };
            var random = new Random(7);
            for (int i = 0; i < 40; i++)
                yield return new object[] { $"random {i}", new Quaternion((float)random.NextDouble() * 2 - 1, (float)random.NextDouble() * 2 - 1,
                    (float)random.NextDouble() * 2 - 1, (float)random.NextDouble() * 2 - 1) };
        }

        /// <summary>
        /// ClientUpdateTeleportToLocation's +0x30 facing: a zero vector made the client's visibility boxes NaN and hung
        /// it (Teleport.cs). Always a flat unit vector, whatever the rotation.
        /// </summary>
        [Theory]
        [MemberData(nameof(Facings))]
        public void FacingVector_IsAFlatUnitVector(string name, Quaternion facing)
        {
            Vector4 v = Teleport.FacingVector(facing);
            Assert.True(float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) && float.IsFinite(v.W), $"{name}: {v}");
            Assert.Equal(0f, v.Y);
            Assert.Equal(0f, v.W);
            Assert.Equal(1f, MathF.Sqrt(v.X * v.X + v.Z * v.Z), 1e-4f);
        }

        [Fact]
        public void FacingVector_FollowsTheYaw()
        {
            Vector4 v = Teleport.FacingVector(Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2));
            Assert.Equal(1f, v.X, 1e-5f);
            Assert.Equal(0f, v.Z, 1e-5f);
            Assert.Equal(new Vector4(0, 0, 1, 0), Teleport.FacingVector(Quaternion.Identity));
        }

        // ---- Melee sweep cone (Creatures.InSweepShape, used for every client FindTarget) ------------------------------

        private static readonly Vector3[] North = { Vector3.UnitZ };

        [Theory]
        [InlineData(0f, 4f, true)]       // dead ahead, inside the range
        [InlineData(0f, 5f, true)]       // on the range
        [InlineData(0f, 5.1f, false)]    // past it
        [InlineData(0f, -4f, false)]     // behind
        [InlineData(1f, -1f, true)]      // behind but closer than 1.5 m: any direction
        [InlineData(4.3f, 2.4f, true)]   // 60.8° off: inside a 61° half-arc
        [InlineData(4.4f, 2.3f, false)]  // 62.4° off: outside
        public void Sweep_ConeAndRange(float x, float z, bool expected)
            => Assert.Equal(expected, Creatures.InSweepShape(Vector3.Zero, new Vector3(x, 0f, z), North, 5f, 61f));

        [Fact]
        public void Sweep_HeightLimit_Is4mPlusAThirdOfTheRange()
        {
            Assert.True(Creatures.InSweepShape(Vector3.Zero, new Vector3(0, 5.5f, 3), North, 5f, 60f));    // 4 + 1.5
            Assert.False(Creatures.InSweepShape(Vector3.Zero, new Vector3(0, 5.6f, 3), North, 5f, 60f));
            Assert.False(Creatures.InSweepShape(Vector3.Zero, new Vector3(0, -5.6f, 3), North, 5f, 60f));
        }

        [Fact]
        public void Sweep_EitherFacingCounts()
        {
            var both = new[] { Vector3.UnitZ, Vector3.UnitX };   // the swing's facing and the player's facing now
            Assert.True(Creatures.InSweepShape(Vector3.Zero, new Vector3(4, 0, 0), both, 5f, 30f));
            Assert.False(Creatures.InSweepShape(Vector3.Zero, new Vector3(4, 0, 0), North, 5f, 30f));
        }

        // ---- Block stance -----------------------------------------------------------------------------------------------

        /// <summary>
        /// The client's Block targeter uses the effect's full arc (cos(arc × 0.5°)); the server's own head-on test
        /// (WeaponCombat.Incoming) is dot &gt; 0.2. The arc sent must be that same test.
        /// </summary>
        [Fact]
        public void PlayerBlockArc_IsTheServersHeadOnTest()
        {
            Assert.Equal(0.2f, MathF.Cos(BlockStance.PlayerArc * 0.5f * MathF.PI / 180f), 0.003f);
            Assert.Equal(360f, BlockStance.CreatureArc);
            Assert.Equal(1UL << 45, BlockStance.BlockingBit);
        }

        // ---- Build-site boxes ---------------------------------------------------------------------------------------------

        private static readonly (double X, double Y, double Z) Origin = LandmarkEmulator.WorldServer.Game.Terrain.TerrainStore.ZoneOrigin;

        private static ClaimModel Site(ulong id, uint zone, Vector3 minLocal, Vector3 maxLocal) => new()
        {
            Id = id, ZoneId = zone, OwnerGuid = 0x42,
            MinX = minLocal.X * 10 + Origin.X, MinY = minLocal.Y * 10 + Origin.Y, MinZ = minLocal.Z * 10 + Origin.Z,
            MaxX = maxLocal.X * 10 + Origin.X, MaxY = maxLocal.Y * 10 + Origin.Y, MaxZ = maxLocal.Z * 10 + Origin.Z,
        };

        private static T WithSites<T>(Func<T> test, params ClaimModel[] sites)
        {
            var list = (List<ClaimModel>)Private.Field(typeof(Claims), "claims");
            List<ClaimModel> saved;
            lock (Private.Field(typeof(Claims), "sync"))
            {
                saved = list.ToList();
                list.Clear();
                list.AddRange(sites);
            }
            try
            {
                return test();
            }
            finally
            {
                lock (Private.Field(typeof(Claims), "sync"))
                {
                    list.Clear();
                    list.AddRange(saved);
                }
            }
        }

        [Fact]
        public void ClaimAt_UsesTheBoxInGlobalDecimetres()
        {
            ClaimModel site = Site(77, 406, new Vector3(100, 0, 100), new Vector3(164, 64, 164));
            WithSites(() =>
            {
                Assert.Equal(77ul, Claims.ClaimAt(406, new Vector3(100, 0, 100)));
                Assert.Equal(77ul, Claims.ClaimAt(406, new Vector3(164, 64, 164)));
                Assert.Equal(77ul, Claims.ClaimAt(406, new Vector3(130, 10, 130)));
                Assert.Null(Claims.ClaimAt(406, new Vector3(99.9f, 10, 130)));
                Assert.Null(Claims.ClaimAt(406, new Vector3(130, 10, 164.1f)));
                Assert.Null(Claims.ClaimAt(391, new Vector3(130, 10, 130)));    // another landscape
                return 0;
            }, site);
        }

        [Fact]
        public void Near_GrowsTheBoxByTheMarginInMetres()
        {
            ClaimModel site = Site(78, 406, new Vector3(100, 0, 100), new Vector3(164, 64, 164));
            WithSites(() =>
            {
                Assert.True(Private.Call<bool>(typeof(Claims), "Near", 406u, new Vector3(96.1f, 10, 130), 78ul, 4f));
                Assert.False(Private.Call<bool>(typeof(Claims), "Near", 406u, new Vector3(95.9f, 10, 130), 78ul, 4f));
                Assert.False(Private.Call<bool>(typeof(Claims), "Near", 406u, new Vector3(130, 10, 130), 79ul, 4f));
                return 0;
            }, site);
        }

        [Fact]
        public void SiteBox_CentreAndHalfDiagonal_InLocalMetres()
        {
            ClaimModel site = Site(79, 406, new Vector3(100, 0, 100), new Vector3(164, 64, 164));
            WithSites(() =>
            {
                var box = Claims.SiteBoxAt(406, new Vector3(120, 5, 120));
                Assert.NotNull(box);
                Assert.Equal(132f, box.Value.Centre.X, 0.01f);
                Assert.Equal(32f, box.Value.Centre.Y, 0.01f);
                Assert.Equal(132f, box.Value.Centre.Z, 0.01f);
                Assert.Equal(MathF.Sqrt(3) * 64f / 2f, box.Value.HalfDiagonal, 0.01f);
                return 0;
            }, site);
        }

        // ---- Prop streaming regions (Props.Stream) ------------------------------------------------------------------------

        [Theory]
        [InlineData(406u, 0, 0)]
        [InlineData(406u, -1, -1)]
        [InlineData(406u, 32767, -32768)]
        [InlineData(uint.MaxValue, -5, 7)]
        [InlineData(0x80000001u, 123, -456)]
        public void StreamKey_RoundTrips(uint zone, int rx, int rz)
        {
            long key = Private.Call<long>(typeof(Props), "StreamKey", zone, rx, rz);
            object parts = Private.Call(typeof(Props), "StreamKeyParts", key);
            var tuple = ((uint, int, int))parts;
            Assert.Equal((zone, rx, rz), tuple);
        }

        [Fact]
        public void StreamKeys_AreDistinctPerRegionAndLandscape()
        {
            var keys = new HashSet<long>();
            foreach (uint zone in new uint[] { 391, 406 })
                for (int rx = -3; rx <= 3; rx++)
                    for (int rz = -3; rz <= 3; rz++)
                        Assert.True(keys.Add(Private.Call<long>(typeof(Props), "StreamKey", zone, rx, rz)), $"{zone} {rx} {rz}");
        }

        [Fact]
        public void RegionDistance_IsZeroInside_AndToTheNearestEdge()
        {
            Assert.Equal(0f, Private.Call<float>(typeof(Props), "StreamRegionDistance", 0, 0, new Vector3(10, 99, 10)));
            Assert.Equal(6f, Private.Call<float>(typeof(Props), "StreamRegionDistance", 1, 0, new Vector3(58, 0, 10)));
            Assert.Equal(5f, Private.Call<float>(typeof(Props), "StreamRegionDistance", -1, -1, new Vector3(3, 0, 4)), 1e-4f);
        }

        [Fact]
        public void RegionsWithin_TakesEveryRegionInRange_AndNoneBeyond()
        {
            var at = new Vector3(1000, 0, -700);
            const float range = 400f;
            var found = ((IEnumerable)Private.Call(typeof(Props), "RegionsWithin", 406u, at, range)).Cast<(long Key, float Distance)>().ToList();
            Assert.All(found, r => Assert.True(r.Distance <= range));
            var keys = found.Select(r => r.Key).ToHashSet();
            // Every region whose nearest point is within range is there (brute force over a wider square).
            for (int rx = -30; rx <= 30; rx++)
                for (int rz = -30; rz <= 30; rz++)
                {
                    int x = (int)MathF.Floor(at.X / 64f) + rx, z = (int)MathF.Floor(at.Z / 64f) + rz;
                    float d = Private.Call<float>(typeof(Props), "StreamRegionDistance", x, z, at);
                    long key = Private.Call<long>(typeof(Props), "StreamKey", 406u, x, z);
                    Assert.Equal(d <= range, keys.Contains(key));
                }
        }

        // ---- Moving platforms: motion is a pure function of the clock -------------------------------------------------------

        private static readonly Type StopType = Private.Nested(typeof(Props), "PlatformStop");

        private static object Stops(params (Vector3 At, float Pause, float Speed)[] stops)
        {
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(StopType));
            foreach (var s in stops)
                list.Add(Activator.CreateInstance(StopType, s.At, Quaternion.Identity, s.Pause, s.Speed));
            return list;
        }

        private static (Vector3 At, Vector3 Velocity, int Segment) Pose(object stops, double clock)
        {
            object pose = Private.Call(typeof(Props), "PoseAt", stops, clock);
            Type t = pose.GetType();
            return ((Vector3)t.GetProperty("At")!.GetValue(pose), (Vector3)t.GetProperty("Velocity")!.GetValue(pose),
                (int)t.GetProperty("Segment")!.GetValue(pose));
        }

        [Fact]
        public void Platform_RestsTravelsAndReturns()
        {
            // Two docks 16 m apart, 2 s pause, 1.6 m/s (the defaults: 160 × 0.01): 2 + 10 + 2 + 10 = a 24 s cycle.
            object stops = Stops((new Vector3(0, 0, 0), 2f, 1.6f), (new Vector3(16, 0, 0), 2f, 1.6f));
            var rest = Pose(stops, 1.0);
            Assert.Equal((Vector3.Zero, Vector3.Zero, 0), rest);
            var mid = Pose(stops, 7.0);                       // 5 s into the 10 s leg
            Assert.Equal(8f, mid.At.X, 1e-3f);
            Assert.Equal(1.6f, mid.Velocity.Length(), 1e-4f);
            Assert.Equal(1, mid.Segment);
            var there = Pose(stops, 13.0);
            Assert.Equal(16f, there.At.X, 1e-3f);
            Assert.Equal(2, there.Segment);
            var back = Pose(stops, 19.0);
            Assert.Equal(8f, back.At.X, 1e-3f);
            Assert.Equal(-1.6f, back.Velocity.X, 1e-4f);
            Assert.Equal(3, back.Segment);
            Assert.Equal(Pose(stops, 1.0 + 24 * 1000), rest);    // periodic
        }

        [Fact]
        public void Platform_IsContinuous()
        {
            object stops = Stops((new Vector3(0, 0, 0), 2f, 1.6f), (new Vector3(16, 4, 0), 0f, 3f), (new Vector3(0, 8, 12), 1f, 0.5f));
            Vector3 last = Pose(stops, 0).At;
            for (double t = 0.01; t < 200; t += 0.01)
            {
                Vector3 now = Pose(stops, t).At;
                Assert.True(Vector3.Distance(now, last) < 0.05f, $"jump at {t:0.00}: {last} → {now}");
                last = now;
            }
        }
    }
}
