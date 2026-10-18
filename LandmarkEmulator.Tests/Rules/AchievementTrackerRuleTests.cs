using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// The goal tracker behind the crafting window's Watch button and the Journal's watch (user report 2026-10-18, "the
    /// Watch button in the craft menus does not seem to work"; Achievements.WatchableGoal has the client trace):
    /// TrackerUpdate (0x92 sub 0x0C, reader FUN_142e28e60) tracks / untracks, the set comes back in TrackerInitialize
    /// (sub 0x0B), a completed goal leaves it (client FUN_142e2e390), and a recipe names only a goal that exists.
    /// </summary>
    public class AchievementTrackerRuleTests : IDisposable
    {
        // Ids far from the capture's (≤ ~2300) so nothing else in the process knows them.
        private const uint Goal = 990_728, OtherGoal = 990_720, NoSuchGoal = 990_306;
        private const ulong Player = 0x7700_0000_0000_0001;

        private readonly FakeWorld world = new();
        private readonly Dictionary<uint, Achievements.Achievement> achievements =
            (Dictionary<uint, Achievements.Achievement>)Private.Field(typeof(Achievements), "achievements");

        public AchievementTrackerRuleTests()
        {
            achievements[Goal] = new Achievements.Achievement { Id = Goal };
            achievements[OtherGoal] = new Achievements.Achievement { Id = OtherGoal };
        }

        public void Dispose()
        {
            achievements.Remove(Goal);
            achievements.Remove(OtherGoal);
            // The lazily built text index may have been built from the test goals: let the next user rebuild it.
            typeof(Achievements).GetField("objectivesByText", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(null, null);
            world.Dispose();
        }

        /// <summary>The client's TrackerUpdate body after the sub byte: u32 0 (no records), u32 n, n × u32, u32 removed [P].</summary>
        private static AchievementRequest TrackerUpdate(bool removed, params uint[] ids)
        {
            var bytes = new MemoryStream();
            void U32(uint v) => bytes.Write(BitConverter.GetBytes(v));
            bytes.WriteByte(Achievements.TrackerUpdateSub);
            U32(0);
            U32((uint)ids.Length);
            foreach (uint id in ids)
                U32(id);
            U32(removed ? 1u : 0u);
            var request = new AchievementRequest();
            request.Read(new GamePacketReader(bytes.ToArray()));
            return request;
        }

        private WorldSession NewSession(ulong guid)
        {
            WorldSession session = world.Session(guid, "Watcher");
            Unlocks.Clear(session, Unlocks.TrackedAchievement);
            return session;
        }

        /// <summary>The kept set, sorted, as "id,id".</summary>
        private static string Tracked(WorldSession session)
            => string.Join(",", Unlocks.Values(session.PlayerGuid, Unlocks.TrackedAchievement).OrderBy(x => x));

        private static string Ids(params uint[] ids) => string.Join(",", ids.OrderBy(x => x));

        [Fact]
        public void Watch_TracksTheGoal_AndWatchAgainUntracksIt()
        {
            WorldSession session = NewSession(Player);
            Achievements.HandleRequest(session, TrackerUpdate(false, Goal));
            Assert.Equal(Ids(Goal), Tracked(session));

            Achievements.HandleRequest(session, TrackerUpdate(false, OtherGoal));
            Assert.Equal(Ids(Goal, OtherGoal), Tracked(session));

            Achievements.HandleRequest(session, TrackerUpdate(true, Goal));
            Assert.Equal(Ids(OtherGoal), Tracked(session));
        }

        /// <summary>The kept set is what TrackerInitialize (sent at zone-in) carries: sub 0x0B, u32 n, n × u32.</summary>
        [Fact]
        public void TrackedGoals_AreSentBackInTrackerInitialize()
        {
            WorldSession session = NewSession(Player + 1);
            Achievements.HandleRequest(session, TrackerUpdate(false, Goal));
            FakeWorld.Outgoing(session);

            Private.Call(typeof(Achievements), "SendTracker", session);
            var (opcode, body) = Assert.Single(FakeWorld.Outgoing(session));
            Assert.Equal(ZoneMessageOpcode.AchievementBase, opcode);
            var reader = new GamePacketReader(body);
            Assert.Equal(Achievements.TrackerSub, reader.ReadByte());
            Assert.Equal(1u, reader.ReadUInt());
            Assert.Equal(Goal, reader.ReadUInt());
            Assert.Equal(0u, reader.BytesRemaining);
        }

        /// <summary>A goal the client can't hold (no record) is not kept: nothing could show it.</summary>
        [Fact]
        public void UnknownGoal_IsNotKept()
        {
            WorldSession session = NewSession(Player + 2);
            Achievements.HandleRequest(session, TrackerUpdate(false, NoSuchGoal, Goal));
            Assert.Equal(Ids(Goal), Tracked(session));
        }

        /// <summary>The client drops a completed goal from its tracked set (FUN_142e2e390); the server's copy follows.</summary>
        [Fact]
        public void CompletedGoal_LeavesTheTrackedSet()
        {
            WorldSession session = NewSession(Player + 3);
            Achievements.HandleRequest(session, TrackerUpdate(false, Goal, OtherGoal));

            Private.Call(typeof(Achievements), "Complete", session, achievements[Goal], new List<CharacterObjectiveModel>());

            Assert.Equal(Ids(OtherGoal), Tracked(session));
            Assert.True(FakeWorld.Outgoing(session).Any(p => p.Opcode == ZoneMessageOpcode.AchievementBase
                && p.Body.Length == 5 && p.Body[0] == Achievements.CompleteSub && BitConverter.ToUInt32(p.Body, 1) == Goal), "AchievementComplete sent");
        }

        /// <summary>
        /// Recipe record +0x40 (the Watch button's AchievementId): the tracker id when its goal exists, else 0 so the
        /// button is disabled (177 station recipes' tracker ids have no goal record).
        /// </summary>
        [Fact]
        public void RecipeRecord_NamesOnlyAnExistingGoal()
        {
            Assert.Equal(Goal, Achievements.WatchableGoal(Goal));
            Assert.Equal(0u, Achievements.WatchableGoal(NoSuchGoal));
            Assert.Equal(0u, Achievements.WatchableGoal(0));
        }
    }
}
