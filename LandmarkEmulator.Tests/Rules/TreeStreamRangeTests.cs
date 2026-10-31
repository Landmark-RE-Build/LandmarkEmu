using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Game.Terrain;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// The tree / plant stream range (Gathering.Stream.cs, setting TreeStreamRange; player report 2026-11-01: trees end
    /// 100-200 m out and the far plain is bare; trace/0695/horizon.md). The client draws trees out to their models' own
    /// cull distances (440-2,500 m for ours), so the server's range is the tree line: 400 m by default (dropped at 480 m),
    /// the 160 m around the player at once and the rest nearest first, at most 32 regions a tick while the tick has sent
    /// fewer than 200 nodes; regions not generated yet beyond 160 m also wait while all players together have spent the
    /// second's 25 ms of region generation (review 2026-11-01: the per-player caps alone let world-thread generation grow
    /// with the number of players arriving).
    /// Runs the stream step itself (<c>StreamRegions</c>) on flat test ground; the gates before it (stream hold, census,
    /// baked heights) are the old ones, covered by StreamHoldRuleTests. The shared generation time is unlimited here unless
    /// a test sets it (only Update refills it, and Update does nothing without the home landscape's heights).
    /// </summary>
    public class TreeStreamRangeTests : IDisposable
    {
        private const float RegionSize = 64f;
        private static int nextZone = 9700;
        private readonly uint zone = (uint)System.Threading.Interlocked.Increment(ref nextZone);
        private readonly FakeWorld world = new();
        private readonly int budget = Gathering.StreamBudget, regionsPerTick = Gathering.FarRegionsPerTick;
        private readonly TimeSpan generationPerSecond = Gathering.RegionGenerationPerSecond;
        private readonly long previousGenerationLeft;
        private readonly DateTime previousRefill;

        /// <summary>A spot well inside the test ground (regions 12-20 around it).</summary>
        private static readonly Vector3 Spot = new(1000f, 1012f, 1000f);

        private static object Sync => Private.Field(typeof(Gathering), "sync");
        private static readonly FieldInfo OverrideField = typeof(Gathering).GetField("streamRangeOverride", BindingFlags.NonPublic | BindingFlags.Static)!;
        private static readonly FieldInfo GenerationLeftField = typeof(Gathering).GetField("generationLeft", BindingFlags.NonPublic | BindingFlags.Static)!;
        private static readonly FieldInfo RefillField = typeof(Gathering).GetField("nextGenerationRefill", BindingFlags.NonPublic | BindingFlags.Static)!;

        public TreeStreamRangeTests()
        {
            TerrainHeights.TestGround = (x, z, z0) => z0 == zone ? 1010f : null;
            var rolled = (Dictionary<uint, string>)Private.Field(typeof(Gathering), "rolled");
            string generator = (string)typeof(Gathering).GetProperty("Generator", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
            lock (rolled)
                rolled[zone] = generator;
            // The players' shared generation time: unlimited and never refilled, whatever ran before (restored on Dispose).
            lock (Sync)
            {
                previousGenerationLeft = (long)GenerationLeftField.GetValue(null)!;
                previousRefill = (DateTime)RefillField.GetValue(null)!;
                GenerationLeftField.SetValue(null, long.MaxValue);
                RefillField.SetValue(null, default(DateTime));
            }
        }

        public void Dispose()
        {
            OverrideField.SetValue(null, null);
            Gathering.StreamBudget = budget;
            Gathering.FarRegionsPerTick = regionsPerTick;
            Gathering.RegionGenerationPerSecond = generationPerSecond;
            lock (Sync)
            {
                GenerationLeftField.SetValue(null, previousGenerationLeft);
                RefillField.SetValue(null, previousRefill);
            }
            TerrainHeights.TestGround = null;
            world.Dispose();
        }

        /// <summary>Stopwatch ticks of the second's shared generation time still unspent.</summary>
        private static long GenerationLeft
        {
            get { lock (Sync) return (long)GenerationLeftField.GetValue(null)!; }
            set { lock (Sync) GenerationLeftField.SetValue(null, value); }
        }

        private static void Refill(DateTime now) => Private.Call(typeof(Gathering), "RefillRegionGeneration", now);

        // ---- helpers -----------------------------------------------------------------------------------------------

        private sealed class Tick
        {
            public readonly List<PlayerUpdateAddLightweightProp> Spawns = new();
            public readonly List<ulong> Removals = new();
            public List<long> Added = new();
        }

        /// <summary>One stream step for a player at <paramref name="at"/> holding <paramref name="have"/>.</summary>
        private Tick Step(HashSet<long> have, Vector3 at, float range, bool hide = false, bool show = true)
        {
            var tick = new Tick();
            lock (Sync)
            {
                var before = have.ToHashSet();
                Private.Call(typeof(Gathering), "StreamRegions", zone, at, range, hide, show, have, tick.Spawns, tick.Removals);
                tick.Added = have.Where(k => !before.Contains(k)).ToList();
            }
            return tick;
        }

        private long Key(int rx, int rz) => (long)Private.Call(typeof(Gathering), "Pack", zone, rx, rz);

        private static (uint Zone, int Rx, int Rz) Parts(long key)
        {
            var t = (ValueTuple<uint, int, int>)Private.Call(typeof(Gathering), "Unpack", key);
            return (t.Item1, t.Item2, t.Item3);
        }

        private static float Distance(long key, Vector3 p)
        {
            (_, int rx, int rz) = Parts(key);
            float cx = Math.Clamp(p.X, rx * RegionSize, (rx + 1) * RegionSize);
            float cz = Math.Clamp(p.Z, rz * RegionSize, (rz + 1) * RegionSize);
            return MathF.Sqrt((p.X - cx) * (p.X - cx) + (p.Z - cz) * (p.Z - cz));
        }

        /// <summary>Every region key whose nearest edge is within <paramref name="range"/> of a point.</summary>
        private HashSet<long> Within(Vector3 p, float range)
        {
            var keys = new HashSet<long>();
            for (int rz = (int)MathF.Floor((p.Z - range) / RegionSize); rz <= (int)MathF.Floor((p.Z + range) / RegionSize); rz++)
                for (int rx = (int)MathF.Floor((p.X - range) / RegionSize); rx <= (int)MathF.Floor((p.X + range) / RegionSize); rx++)
                {
                    long key = Key(rx, rz);
                    if (Distance(key, p) <= range)
                        keys.Add(key);
                }
            return keys;
        }

        private static readonly Type NodeClass = Private.Nested(typeof(Gathering), "Node");

        /// <summary>The guids of a region's standing nodes (what a spawn or a drop of it carries).</summary>
        private List<ulong> Standing(long key)
        {
            (_, int rx, int rz) = Parts(key);
            lock (Sync)
                return ((IEnumerable)Private.Call(typeof(Gathering), "Region", zone, rx, rz)).Cast<object>()
                    .Where(n => (bool)NodeClass.GetProperty("Standing")!.GetValue(n)!)
                    .Select(n => (ulong)NodeClass.GetProperty("Guid")!.GetValue(n)!).ToList();
        }

        /// <summary>Steps until nothing more comes (at most 50 ticks); returns the ticks that added something.</summary>
        private List<Tick> Fill(HashSet<long> have, Vector3 at, float range)
        {
            var ticks = new List<Tick>();
            for (int i = 0; i < 50; i++)
            {
                Tick t = Step(have, at, range);
                if (t.Added.Count == 0)
                    return ticks;
                ticks.Add(t);
            }
            throw new InvalidOperationException("The stream never filled the range.");
        }

        // ---- the setting ---------------------------------------------------------------------------------------------

        [Fact]
        public void Range_Defaults400_TakesTheSetting_AndIsClamped()
        {
            Assert.Equal(400f, Gathering.DefaultStreamRange);
            Assert.Equal(400f, world.Config.TreeStreamRange);
            Assert.Equal(400f, Gathering.StreamRange);
            world.Config.TreeStreamRange = 160f;
            Assert.Equal(160f, Gathering.StreamRange);
            world.Config.TreeStreamRange = 10f;
            Assert.Equal(64f, Gathering.StreamRange);
            world.Config.TreeStreamRange = 5000f;
            Assert.Equal(1000f, Gathering.StreamRange);
            world.Config.TreeStreamRange = float.NaN;
            Assert.Equal(400f, Gathering.StreamRange);
            Gathering.StreamRange = 250f;   // dev "treestream 250" wins until the restart
            Assert.Equal(250f, Gathering.StreamRange);
        }

        // ---- sending -----------------------------------------------------------------------------------------------

        [Fact]
        public void FirstTick_TheWhole160m_AtOnce_ThenOnlyTheNearestFarRegions()
        {
            var have = new HashSet<long>();
            Tick first = Step(have, Spot, 400f);

            HashSet<long> near = Within(Spot, Gathering.ImmediateRange);
            Assert.True(near.IsSubsetOf(have));   // everything a player stands among comes in one go, as the whole set did
            List<long> far = first.Added.Where(k => !near.Contains(k)).ToList();
            Assert.InRange(far.Count, 1, Gathering.FarRegionsPerTick);
            // Nearest first: every far region taken is at least as near as every far region in range still to come.
            float furthestTaken = far.Max(k => Distance(k, Spot));
            float nearestLeft = Within(Spot, 400f).Where(k => !have.Contains(k)).Min(k => Distance(k, Spot));
            Assert.True(furthestTaken <= nearestLeft, $"took {furthestTaken:0.0} m before {nearestLeft:0.0} m");
            // What went out is exactly the regions' standing nodes.
            Assert.True(first.Spawns.Select(s => s.Guid).OrderBy(g => g).SequenceEqual(have.SelectMany(Standing).OrderBy(g => g)));
            Assert.Empty(first.Removals);
        }

        [Fact]
        public void LaterTicks_FillTheRangeOutward_AndNothingBeyondIt()
        {
            var have = new HashSet<long>();
            List<Tick> ticks = Fill(have, Spot, 400f);

            Assert.Equal(Within(Spot, 400f).OrderBy(k => k), have.OrderBy(k => k));   // all of it, nothing further
            Assert.True(ticks.Count > 1, "the far ring should take more than one tick");
            for (int i = 1; i < ticks.Count; i++)
            {
                Assert.True(ticks[i].Added.Count <= Gathering.FarRegionsPerTick);
                Assert.True(ticks[i - 1].Added.Max(k => Distance(k, Spot)) <= ticks[i].Added.Min(k => Distance(k, Spot)),
                    $"tick {i} went back inward");
            }
            int sent = ticks.Sum(t => t.Spawns.Count);
            Assert.Equal(have.Sum(k => Standing(k).Count), sent);   // each node once
            Assert.All(ticks.SelectMany(t => t.Spawns), s => Assert.True(
                Distance(Key((int)MathF.Floor(s.Position.X / RegionSize), (int)MathF.Floor(s.Position.Z / RegionSize)), Spot) <= 400f));
        }

        [Fact]
        public void TheNodeBudget_BoundsTheFarRing_NotThe160m()
        {
            var have = new HashSet<long>();
            Gathering.StreamBudget = 1;   // the 160 m ring's own nodes count: nothing further this tick
            Tick first = Step(have, Spot, 400f);
            Assert.Equal(Within(Spot, Gathering.ImmediateRange).OrderBy(k => k), have.OrderBy(k => k));
            Assert.True(first.Spawns.Count > 1, "the 160 m ring goes whole, over the budget");

            Gathering.StreamBudget = 3;
            Tick t = Step(have, Spot, 400f);
            int nodes = t.Spawns.Count;
            Assert.True(nodes >= 3 || t.Added.Count == Gathering.FarRegionsPerTick, $"{nodes} node(s) in {t.Added.Count} region(s)");
            // Whole regions: it stops at the first region that reaches the budget (that one may go over), so without its
            // biggest region the tick stays under the budget.
            int withoutBiggest = nodes - t.Added.Max(k => Standing(k).Count);
            Assert.True(withoutBiggest < 3, $"{withoutBiggest} node(s) without the biggest region");
        }

        [Fact]
        public void Range160_IsTheOldStream()
        {
            var have = new HashSet<long>();
            List<Tick> ticks = Fill(have, Spot, 160f);
            Assert.Single(ticks);   // all at once, as before 2026-11-01
            Assert.Equal(Within(Spot, 160f).OrderBy(k => k), have.OrderBy(k => k));
        }

        // ---- the players' shared generation time -------------------------------------------------------------------

        [Fact]
        public void SharedGeneration_OnePlayerSpendsTheSecond_TheNextGetsOnlyIts160m_UntilTheRefill()
        {
            Gathering.StreamBudget = int.MaxValue;   // only the generation time and the 32-region cap hold the far ring back
            HashSet<long> near = Within(Spot, Gathering.ImmediateRange);
            foreach (long key in near)
                Standing(key);   // the 160 m ring at Spot is generated already (someone stood there)
            GenerationLeft = 1;   // one Stopwatch tick left this second: the first region generated spends it

            var a = new HashSet<long>();
            Tick first = Step(a, Spot, 400f);
            Assert.True(near.IsSubsetOf(a));
            List<long> far = first.Added.Where(k => !near.Contains(k)).ToList();
            Assert.Single(far);   // the nearest far region, which spent the rest of the second; then it waits
            Assert.Equal(Within(Spot, 400f).Where(k => !near.Contains(k)).Min(k => Distance(k, Spot)), Distance(far[0], Spot));
            Assert.True(GenerationLeft <= 0);

            // A second player arriving elsewhere (nothing generated there) the same second: its own 160 m ring whole, as
            // before 2026-11-01 (spending more), and nothing beyond it.
            var other = new Vector3(5000f, 1012f, 5000f);
            var b = new HashSet<long>();
            long before = GenerationLeft;
            Tick arrival = Step(b, other, 400f);
            Assert.Equal(Within(other, Gathering.ImmediateRange).OrderBy(k => k), b.OrderBy(k => k));
            Assert.NotEmpty(arrival.Added);
            Assert.True(GenerationLeft < before, "the 160 m ring's new regions spend it too");
            Assert.Empty(Step(a, Spot, 400f).Added);   // still spent: the first player waits as well

            // The next second (Update refills it): both far rings go on, nearest first, under the per-player cap.
            Refill(DateTime.UtcNow);
            Assert.True(GenerationLeft > 0);
            Tick next = Step(b, other, 400f);
            Assert.InRange(next.Added.Count, 1, Gathering.FarRegionsPerTick);
            Assert.All(next.Added, k => Assert.True(Distance(k, other) > Gathering.ImmediateRange));
            float nearestLeft = Within(other, 400f).Where(k => !b.Contains(k)).Select(k => Distance(k, other)).DefaultIfEmpty(float.MaxValue).Min();
            Assert.True(next.Added.Max(k => Distance(k, other)) <= nearestLeft, "nearest first");
        }

        [Fact]
        public void SharedGeneration_RegionsAlreadyGenerated_NeitherWaitNorSpend()
        {
            var a = new HashSet<long>();
            Fill(a, Spot, 400f);   // the first player generated the whole range
            GenerationLeft = 0;   // and this second's generation is spent

            var b = new HashSet<long>();
            List<Tick> ticks = Fill(b, Spot, 400f);   // a second player at the same spot gets it all from the cache

            Assert.Equal(a.OrderBy(k => k), b.OrderBy(k => k));
            Assert.True(ticks.Count > 1, "the per-player caps still spread it over ticks");
            Assert.Equal(0, GenerationLeft);
        }

        [Fact]
        public void SharedGeneration_Is25msASecond_RefilledOnceASecond_WithoutCarryingDebt()
        {
            Assert.Equal(TimeSpan.FromMilliseconds(25), Gathering.RegionGenerationPerSecond);
            long full = (long)(0.025 * Stopwatch.Frequency);
            var t0 = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

            Refill(t0);
            Assert.Equal(full, GenerationLeft);
            GenerationLeft = -3 * full;   // 160 m rings arriving together overspent it (they never wait)
            Refill(t0.AddMilliseconds(999));
            Assert.Equal(-3 * full, GenerationLeft);   // not before the second is up
            Refill(t0.AddSeconds(1));
            Assert.Equal(full, GenerationLeft);   // a new second starts full: the overspend is not a debt
            GenerationLeft = 0;
            Refill(t0.AddHours(-1));   // the clock set back: refilled at once, not an hour later
            Assert.Equal(full, GenerationLeft);
        }

        [Fact]
        public void Update_RefillsTheSharedGeneration_BeforeThePlayersTicks()
        {
            string source = Repo.Read("LandmarkEmulator.WorldServer", "Game", "Gathering.cs");
            int update = source.IndexOf("public static void Update()", StringComparison.Ordinal);
            int refill = source.IndexOf("RefillRegionGeneration(now);", update, StringComparison.Ordinal);
            int players = source.IndexOf("foreach (WorldSession session in PlayerManager.Snapshot())", update, StringComparison.Ordinal);
            Assert.True(update >= 0 && refill > update && refill < players, $"Update {update}, refill {refill}, players {players}");
        }

        // ---- dropping ------------------------------------------------------------------------------------------------

        [Fact]
        public void MovingAway_DropsOnlyRegionsBeyondTheRangePlus80m()
        {
            var have = new HashSet<long>();
            Fill(have, Spot, 400f);
            HashSet<long> held = have.ToHashSet();
            Vector3 moved = Spot + new Vector3(300f, 0f, 0f);

            Tick t = Step(have, moved, 400f);

            List<long> dropped = held.Where(k => !have.Contains(k)).ToList();
            Assert.NotEmpty(dropped);
            Assert.All(dropped, k => Assert.True(Distance(k, moved) > 480f));
            Assert.All(held.Where(have.Contains), k => Assert.True(Distance(k, moved) <= 480f));
            Assert.True(t.Removals.OrderBy(g => g).SequenceEqual(dropped.SelectMany(Standing).OrderBy(g => g)));
            // The regions it walked towards come in, nearest first, from the new spot.
            Assert.NotEmpty(t.Added);
            Assert.All(t.Added, k => Assert.True(Distance(k, moved) <= 400f));
        }

        [Fact]
        public void DeepUnderground_DropsEverything_AndSendsNothing()
        {
            var have = new HashSet<long>();
            Fill(have, Spot, 400f);
            int nodes = have.Sum(k => Standing(k).Count);

            Tick t = Step(have, Spot, 400f, hide: true, show: false);

            Assert.Empty(have);
            Assert.Equal(nodes, t.Removals.Count);
            Assert.Empty(t.Spawns);
        }

        [Fact]
        public void AnotherLandscapesRegions_AreForgotten_WithoutRemovals()
        {
            long elsewhere = (long)Private.Call(typeof(Gathering), "Pack", zone + 1000u, 15, 15);
            var have = new HashSet<long> { elsewhere };

            Tick t = Step(have, Spot, 160f);

            Assert.DoesNotContain(elsewhere, have);
            Assert.Empty(t.Removals);   // the client lost those entities with its re-zone
        }
    }
}
