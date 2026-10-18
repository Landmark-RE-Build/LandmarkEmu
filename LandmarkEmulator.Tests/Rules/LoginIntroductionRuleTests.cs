using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Handlers;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// A login is shown to the others, and they to it, when its loading screen drops (ClientLoading 0x6C {0}), not at
    /// ClientFinishedLoading 15-25 s earlier (bug 2026-10-18: one client saw the other, the other did not).
    /// </summary>
    public class LoginIntroductionRuleTests : IDisposable
    {
        private readonly FakeWorld world = new();
        private readonly List<WorldSession> added = new();

        private static IList Players => (IList)Private.Field(typeof(PlayerManager), "players");
        private static IDictionary Loading => (IDictionary)Private.Field(typeof(PlayerManager), "loading");

        public void Dispose()
        {
            foreach (WorldSession s in added)
            {
                Players.Remove(s);
                Loading.Remove(s);
            }
            world.Dispose();
        }

        /// <summary>A player in the world; <paramref name="loading"/>: its login loading screen is still up (as Enter holds it).</summary>
        private WorldSession InWorld(ulong guid, string name, bool loading)
        {
            WorldSession s = world.Session(guid, name);
            s.LoadingScreenDown = !loading;
            Players.Add(s);
            if (loading)
                Loading[s] = DateTime.UtcNow;
            added.Add(s);
            return s;
        }

        /// <summary>The guids of the characters this session was shown (AddLightweightPc), taken out of its queue.</summary>
        private static List<ulong> Shown(WorldSession session)
            => FakeWorld.Outgoing(session)
                .Where(p => p.Opcode is ZoneMessageOpcode.PlayerUpdateAddLightweightPcWire or ZoneMessageOpcode.PlayerUpdateAddLightweightPc)
                .Select(p => BitConverter.ToUInt64(p.Body, 0))
                .ToList();

        /// <summary>The session was shown exactly these characters (and nothing else) since the last check.</summary>
        private static void AssertShown(WorldSession session, params ulong[] guids)
        {
            List<ulong> shown = Shown(session);
            Assert.True(shown.SequenceEqual(guids), $"{session.CharacterName} was shown [{string.Join(", ", shown)}]");
        }

        private static void Introduce(WorldSession viewer, WorldSession subject)
            => Private.Call(typeof(PlayerManager), "Introduce", viewer, subject);

        private static int PendingMovement(WorldSession session)
            => ((IDictionary)typeof(WorldSession).GetField("pendingMovement", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(session)).Count;

        [Theory]
        [InlineData(new byte[] { 0 }, true)]
        [InlineData(new byte[] { 1 }, false)]
        [InlineData(new byte[0], true)]
        public void ClientLoading_ZeroMeansTheScreenDropped(byte[] body, bool left)
            => Assert.Equal(left, PlayerUpdateLoading.LeftLoading(body));

        [Fact]
        public void WhileLoading_NobodyIsShownEitherWay()
        {
            WorldSession online = InWorld(0x7001, "Online", loading: false);
            WorldSession newcomer = InWorld(0x7002, "Newcomer", loading: true);

            Introduce(online, newcomer);
            Introduce(newcomer, online);

            Assert.Empty(Shown(online));
            Assert.Empty(Shown(newcomer));
        }

        [Fact]
        public void ScreenDrop_ShowsBothSidesOnce()
        {
            WorldSession online = InWorld(0x7011, "Online", loading: false);
            WorldSession newcomer = InWorld(0x7012, "Newcomer", loading: true);

            PlayerManager.LoadingScreenLeft(newcomer);

            AssertShown(online, newcomer.PlayerGuid);
            AssertShown(newcomer, online.PlayerGuid);
            Assert.False(Loading.Contains(newcomer));

            // A second drop report (a later re-zone's screen) shows nothing again.
            PlayerManager.LoadingScreenLeft(newcomer);
            Assert.Empty(Shown(online));
            Assert.Empty(Shown(newcomer));
        }

        [Fact]
        public void TwoLoading_MeetWhenTheSecondDrops()
        {
            WorldSession first = InWorld(0x7021, "First", loading: true);
            WorldSession second = InWorld(0x7022, "Second", loading: true);

            PlayerManager.LoadingScreenLeft(first);
            Assert.Empty(Shown(first));
            Assert.Empty(Shown(second));

            PlayerManager.LoadingScreenLeft(second);
            AssertShown(first, second.PlayerGuid);
            AssertShown(second, first.PlayerGuid);
        }

        [Fact]
        public void HandleLoading_OnlyTheFirstDropOfALoginShows()
        {
            WorldSession online = InWorld(0x7031, "Online", loading: false);
            WorldSession newcomer = InWorld(0x7032, "Newcomer", loading: true);

            MiscHandler.HandleLoading(newcomer, Message(1));   // the loading state starting: nothing
            Assert.Empty(Shown(online));
            Assert.False(newcomer.LoadingScreenDown);

            MiscHandler.HandleLoading(newcomer, Message(0));
            Assert.True(newcomer.LoadingScreenDown);
            AssertShown(online, newcomer.PlayerGuid);
            AssertShown(newcomer, online.PlayerGuid);
        }

        [Fact]
        public void Movement_IsNotRelayedToOrFromAPlayerStillLoading()
        {
            WorldSession online = InWorld(0x7041, "Online", loading: false);
            WorldSession newcomer = InWorld(0x7042, "Newcomer", loading: true);
            byte[] body = { 0x02, 1, 2, 3 };

            PlayerManager.RelayMovement(online, body);
            PlayerManager.RelayMovement(newcomer, body);
            Assert.Equal(0, PendingMovement(newcomer));
            Assert.Equal(0, PendingMovement(online));

            PlayerManager.LoadingScreenLeft(newcomer);
            PlayerManager.RelayMovement(online, body);
            PlayerManager.RelayMovement(newcomer, body);
            Assert.Equal(1, PendingMovement(newcomer));
            Assert.Equal(1, PendingMovement(online));
        }

        [Fact]
        public void NoDropReport_ShownAfterTheLimit()
        {
            WorldSession online = InWorld(0x7051, "Online", loading: false);
            WorldSession newcomer = InWorld(0x7052, "Newcomer", loading: true);
            DateTime entered = (DateTime)Loading[newcomer]!;

            Private.Call(typeof(PlayerManager), "ShowOverdue", entered + PlayerManager.LoadingShowLimit - TimeSpan.FromSeconds(1));
            Assert.Empty(Shown(online));

            Private.Call(typeof(PlayerManager), "ShowOverdue", entered + PlayerManager.LoadingShowLimit + TimeSpan.FromSeconds(1));
            AssertShown(online, newcomer.PlayerGuid);
            AssertShown(newcomer, online.PlayerGuid);
        }

        [Fact]
        public void HeldPlayerWhoLeft_IsForgotten()
        {
            WorldSession newcomer = InWorld(0x7061, "Newcomer", loading: true);
            Players.Remove(newcomer);

            Private.Call(typeof(PlayerManager), "ShowOverdue", DateTime.UtcNow);

            Assert.False(Loading.Contains(newcomer));
        }

        private static PlayerUpdateLoading Message(byte value)
        {
            var m = new PlayerUpdateLoading();
            typeof(InformationalMessage).GetProperty(nameof(InformationalMessage.Body))!.GetSetMethod(true)!
                .Invoke(m, new object[] { new[] { value } });
            return m;
        }
    }
}
