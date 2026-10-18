using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Game.Terrain;
using System;
using System.IO;
using System.Numerics;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// Underground Adventure arrivals on the cavern graveyards (bug 2026-10-18, "teleporting to an underground adventure put
    /// me under the map"): which graveyards a random trip may use (Graveyards.TripProblem), the standable-column rule, the
    /// arrival offset turned with the template like its props, and the fall guard's decision (CavernArrivalGuard).
    /// The numbers are the two logged trips on landscape 406 (graveyard 38: arrival y −603.8 under 1,423 m ground; 24: y
    /// 436.2 under 1,182 m) and what the all-islands check measured (client's own floor 4.0-4.4 m under the arrival, nothing
    /// lower than 5.4 m within 40 m).
    /// </summary>
    public class CavernArrivalTests
    {
        [Fact]
        public void TheGraveyardThatFell_IsLeftOut_ForItsDepthBelowTheGround()
        {
            string problem = Graveyards.TripProblem(-603.8f, 1423f, 4.0f, 5.4f);
            Assert.NotNull(problem);
            Assert.Contains("below the ground", problem);
            Assert.Equal($"more than {Graveyards.TripMaxDepthBelowGround:0} m below the ground", Graveyards.ProblemKind(problem));
        }

        [Fact]
        public void TheGraveyardThatWorked_IsOpen()
            => Assert.Null(Graveyards.TripProblem(436.2f, 1182f, 4.4f, 5.4f));

        [Theory]
        [InlineData(-660f, 300f, 4.4f, 5.4f, "below y")]                 // the old absolute rule stays
        [InlineData(950f, 1000f, 4.4f, 5.4f, "below the ground")]        // 50 m under the ground: not a cavern
        [InlineData(100f, 1000f, 7.5f, 5.4f, "floor 7.5 m under it")]    // a missing plinth would be a long drop
        [InlineData(100f, 1000f, 4.4f, 11f, "fall guard")]               // a legitimate floor the guard would take for a fall
        public void ArrivalsOutsideTheRules_AreLeftOut(float y, float ground, float drop, float pit, string why)
            => Assert.Contains(why, Graveyards.TripProblem(y, ground, drop, pit));

        [Fact]
        public void NoOwnFloor_OrOwnRock_IsLeftOut()
        {
            Assert.Contains("no floor", Graveyards.TripProblem(100f, 1000f, null, 5.4f));
            Assert.Contains("rock", Graveyards.TripProblem(100f, 1000f, 4.4f, 5.4f, inOwnRock: true));
            // Without baked heights the depth rules don't apply; the rest still do.
            Assert.Null(Graveyards.TripProblem(100f, null, 4.4f, 5.4f));
        }

        [Fact]
        public void StandableIndex_IsTheFirstAirOverSolid_With2mOfHeadroom()
        {
            // The plinth column: template solid 1..11, air above (the arrival column of every island graveyard).
            Assert.Equal(12L, Graveyards.StandableIndex(y => y <= 11, 1, 40));
            // A ledge with only 0.8 m of air over it (solid again at 14) is skipped for the next floor up.
            Assert.Equal(20L, Graveyards.StandableIndex(y => y <= 11 || (y >= 14 && y <= 19), 1, 40));
            // Solid all the way: none.
            Assert.Null(Graveyards.StandableIndex(_ => true, 1, 40));
        }

        /// <summary>A template of graveyard_launch's size with no cells or props (TurnPoint only needs the size).</summary>
        private static VoxelTemplate GraveyardSized()
        {
            using var stream = new MemoryStream();
            using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                w.Write((byte)6);
                w.Write(0u);                               // no cells
                w.Write(83u); w.Write(22u); w.Write(83u);   // size
                w.Write(0u);                               // no materials
                w.Write((byte)0);                          // no props
            }
            stream.Position = 0;
            return VoxelTemplate.Load(stream);
        }

        [Fact]
        public void TheArrivalOffset_TurnsWithTheTemplate_LikeItsProps()
        {
            // The centrepiece of graveyard_launch (template metres) and the area's offset (0, ·, 6) from it.
            VoxelTemplate t = GraveyardSized();
            const float metre = 0.4f;
            var centre = new Vector2(16.312744f, 16.493652f);
            for (int turns = 0; turns < 4; turns++)
            {
                (double cx, double cz) = t.TurnPoint(centre.X / metre, centre.Y / metre, turns);
                (double ax, double az) = t.TurnPoint(centre.X / metre, (centre.Y + 6f) / metre, turns);
                Vector3 offset = Vector3.Transform(new Vector3(0f, 0f, 6f), VoxelTemplate.Turn(turns));
                // The arrival = the turned centrepiece + the turned offset = the turned (centrepiece + offset): the same template
                // spot on every graveyard, whatever the placement's turn.
                Assert.Equal((float)(ax - cx) * metre, offset.X, 1e-3f);
                Assert.Equal((float)(az - cz) * metre, offset.Z, 1e-3f);
            }
        }

        [Fact]
        public void FellThrough_IsMoreThan12mBelow_NearTheArrival()
        {
            var at = new Vector3(-4.8f, -603.8f, -1400.4f);
            Assert.False(CavernArrivalGuard.FellThrough(at, at));
            Assert.False(CavernArrivalGuard.FellThrough(at, at - new Vector3(0f, 5.4f, 0f)));     // the stamped box's bottom
            Assert.True(CavernArrivalGuard.FellThrough(at, at - new Vector3(1f, 40f, 2f)));
            Assert.False(CavernArrivalGuard.FellThrough(at, at - new Vector3(80f, 40f, 0f)));    // somewhere else of the cavern
        }

        [Fact]
        public void TheGuard_WaitsForTheLoadingScreen_ThenWatchesItsWindow()
        {
            var at = new Vector3(-4.8f, -603.8f, -1400.4f);
            Vector3 fallen = at - new Vector3(0f, 100f, 0f);
            DateTime armed = new(2026, 10, 18, 1, 23, 9, DateTimeKind.Utc), now = armed.AddSeconds(21);
            // Still loading: the stale reports of the screen never count.
            Assert.Equal(CavernArrivalGuard.Verdict.Wait, CavernArrivalGuard.JudgeFor(at, 406, armed, null, 406, shown: false, fallen, now));
            // Shown: standing is watched, a fall is rescued.
            Assert.Equal(CavernArrivalGuard.Verdict.Keep, CavernArrivalGuard.JudgeFor(at, 406, armed, null, 406, shown: true, at, now));
            Assert.Equal(CavernArrivalGuard.Verdict.Rescue, CavernArrivalGuard.JudgeFor(at, 406, armed, now.AddSeconds(-2), 406, true, fallen, now));
            // Past the window after the drop, on another landscape, or a screen that never dropped: done.
            Assert.Equal(CavernArrivalGuard.Verdict.Done, CavernArrivalGuard.JudgeFor(at, 406, armed, now.AddSeconds(-31), 406, true, fallen, now));
            Assert.Equal(CavernArrivalGuard.Verdict.Done, CavernArrivalGuard.JudgeFor(at, 406, armed, null, 391, true, at, now));
            Assert.Equal(CavernArrivalGuard.Verdict.Done, CavernArrivalGuard.JudgeFor(at, 406, armed, null, 406, false, at, armed.AddMinutes(5)));
        }
    }
}
