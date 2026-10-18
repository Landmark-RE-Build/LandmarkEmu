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
using System.Numerics;
using System.Reflection;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// A re-zone (ClientBeginZoning: long teleport, landscape / Spire travel, cavern trip, travel to a friend) shows its
    /// own GameStateLoading screen, which drops 15-25 s after the zone-in (ClientIsReady) like a login's. The re-zoned
    /// player and the landscape's players meet when that screen's ClientLoading 0x6C {0} arrives, once each way; a viewer
    /// who kept seeing the player through a same-landscape re-zone is not sent it again. Short moves are not held.
    /// </summary>
    public class RezoneIntroductionRuleTests : IDisposable
    {
        private const uint Home = 406, Other = 130;

        private readonly FakeWorld world = new();
        private readonly List<WorldSession> added = new();

        private static IList Players => (IList)Private.Field(typeof(PlayerManager), "players");
        private static IDictionary Holds => (IDictionary)Private.Field(typeof(PlayerManager), "holds");

        public void Dispose()
        {
            foreach (WorldSession s in added)
            {
                Players.Remove(s);
                Holds.Remove(s);
                var shortMoves = (IDictionary)Private.Field(typeof(Teleport), "shortMoves");
                lock (shortMoves)
                    shortMoves.Remove(s);
            }
            world.Dispose();
        }

        /// <summary>A player in the world on <paramref name="zone"/>, its login screen long gone.</summary>
        private WorldSession InWorld(ulong guid, string name, uint zone = Home)
        {
            WorldSession s = world.Session(guid, name);
            SetZone(s, zone);
            s.LoadingScreenDown = true;
            Players.Add(s);
            added.Add(s);
            FakeWorld.Outgoing(s);
            return s;
        }

        /// <summary>The landscape, set without the database save of WorldSession.SetZone.</summary>
        private static void SetZone(WorldSession s, uint zone)
        {
            var character = (LandmarkEmulator.Database.Character.Model.CharacterModel)typeof(WorldSession)
                .GetField("currentCharacter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
            character.ZoneId = zone;
        }

        private static List<ulong> Shown(WorldSession session)
            => FakeWorld.Outgoing(session)
                .Where(p => p.Opcode is ZoneMessageOpcode.PlayerUpdateAddLightweightPcWire or ZoneMessageOpcode.PlayerUpdateAddLightweightPc)
                .Select(p => BitConverter.ToUInt64(p.Body, 0))
                .ToList();

        private static void AssertShown(WorldSession session, params ulong[] guids)
        {
            List<ulong> shown = Shown(session);
            Assert.True(shown.SequenceEqual(guids), $"{session.CharacterName} was shown [{string.Join(", ", shown)}]");
        }

        private static void Introduce(WorldSession viewer, WorldSession subject)
            => Private.Call(typeof(PlayerManager), "Introduce", viewer, subject);

        private static void Started(WorldSession s, bool changedLandscape = false)
            => PlayerManager.RezoneStarted(s, changedLandscape);

        private static int PendingMovement(WorldSession session)
        {
            var pending = (IDictionary)typeof(WorldSession).GetField("pendingMovement", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(session)!;
            lock (pending)
            {
                int n = pending.Count;
                pending.Clear();
                return n;
            }
        }

        private static PlayerUpdateLoading Loading(byte value)
        {
            var m = new PlayerUpdateLoading();
            typeof(InformationalMessage).GetProperty(nameof(InformationalMessage.Body))!.GetSetMethod(true)!
                .Invoke(m, new object[] { new[] { value } });
            return m;
        }

        [Fact]
        public void SameLandscape_RezonedClientSeesTheOthersAtTheDrop_OthersAreNotSentItAgain()
        {
            WorldSession watcher = InWorld(0x7101, "Watcher");
            WorldSession traveller = InWorld(0x7102, "Traveller");

            Started(traveller);
            Assert.True(PlayerManager.IsHeld(traveller));
            Introduce(traveller, watcher);   // what Reintroduce would send at the zone-in
            Assert.Empty(Shown(traveller));

            PlayerManager.ZoneInDone(traveller);   // zone-done flags out (MiscHandler.FinishZoneIn)
            Assert.Empty(Shown(traveller));

            MiscHandler.HandleLoading(traveller, Loading(0));
            Assert.False(PlayerManager.IsHeld(traveller));
            AssertShown(traveller, watcher.PlayerGuid);
            AssertShown(watcher);   // it saw the traveller all along: no second AddLightweightPc
        }

        [Fact]
        public void OtherLandscape_BothSidesMeetOnceAtTheDrop()
        {
            WorldSession local = InWorld(0x7111, "Local", Other);
            WorldSession left = InWorld(0x7112, "LeftBehind", Home);
            WorldSession traveller = InWorld(0x7113, "Traveller", Home);

            Started(traveller, changedLandscape: true);
            SetZone(traveller, Other);
            Introduce(local, traveller);   // what ChangedZone does for the new landscape's players
            Assert.Empty(Shown(local));

            PlayerManager.ZoneInDone(traveller);
            PlayerManager.LoadingScreenLeft(traveller);

            AssertShown(local, traveller.PlayerGuid);
            AssertShown(traveller, local.PlayerGuid);
            AssertShown(left);
        }

        [Fact]
        public void ADropReportBeforeTheZoneIn_IsThePreviousScreens_AndIgnored()
        {
            WorldSession watcher = InWorld(0x7121, "Watcher");
            WorldSession traveller = InWorld(0x7122, "Traveller");

            Started(traveller);
            MiscHandler.HandleLoading(traveller, Loading(1));   // this load's screen coming up: nothing
            MiscHandler.HandleLoading(traveller, Loading(0));   // a {0} before the zone-in: an earlier screen's
            Assert.True(PlayerManager.IsHeld(traveller));
            Assert.Empty(Shown(traveller));

            PlayerManager.ZoneInDone(traveller);
            MiscHandler.HandleLoading(traveller, Loading(0));
            Assert.False(PlayerManager.IsHeld(traveller));
            AssertShown(traveller, watcher.PlayerGuid);
        }

        [Fact]
        public void WhileHeld_NoMovementEitherWay_AndTheArrivalPositionOnlyToThoseWhoSeeIt()
        {
            WorldSession watcher = InWorld(0x7131, "Watcher");
            WorldSession traveller = InWorld(0x7132, "Traveller");
            Started(traveller);
            WorldSession late = InWorld(0x7133, "Late");   // came online during the load: never shown the traveller
            byte[] body = { 0x02, 1, 2, 3 };

            PlayerManager.RelayMovement(traveller, body);
            PlayerManager.RelayMovement(watcher, body);
            Assert.Equal(0, PendingMovement(watcher));
            Assert.Equal(0, PendingMovement(traveller));
            Assert.Equal(1, PendingMovement(late));   // the watcher's own movement: the two see each other

            PlayerManager.ShowPosition(traveller);   // Teleport.FinishRezone
            Assert.Equal(1, PendingMovement(watcher));
            Assert.Equal(0, PendingMovement(late));

            PlayerManager.ZoneInDone(traveller);
            PlayerManager.LoadingScreenLeft(traveller);
            AssertShown(late, traveller.PlayerGuid);
            AssertShown(watcher);
            AssertShown(traveller, watcher.PlayerGuid, late.PlayerGuid);

            PlayerManager.RelayMovement(traveller, body);
            Assert.Equal(1, PendingMovement(watcher));
            Assert.Equal(1, PendingMovement(late));
        }

        [Fact]
        public void TwoReZoning_MeetOnceWhenTheSecondDrops()
        {
            WorldSession first = InWorld(0x7141, "First");
            WorldSession second = InWorld(0x7142, "Second");

            Started(first);
            Started(second);   // its client drops First too, so First no longer counts as seeing... and vice versa

            PlayerManager.ZoneInDone(first);
            PlayerManager.LoadingScreenLeft(first);
            Assert.Empty(Shown(first));
            Assert.Empty(Shown(second));

            PlayerManager.ZoneInDone(second);
            PlayerManager.LoadingScreenLeft(second);
            AssertShown(first, second.PlayerGuid);
            AssertShown(second, first.PlayerGuid);
        }

        [Fact]
        public void AViewerWhoReZones_IsShownTheHeldPlayerAgainAtItsDrop()
        {
            WorldSession traveller = InWorld(0x7151, "Traveller");
            WorldSession viewer = InWorld(0x7152, "Viewer");

            Started(traveller);   // Viewer saw Traveller
            Started(viewer);      // ...until its own re-zone deleted every entity on its client

            PlayerManager.ZoneInDone(viewer);
            PlayerManager.LoadingScreenLeft(viewer);
            Assert.Empty(Shown(viewer));   // Traveller is still held

            PlayerManager.ZoneInDone(traveller);
            PlayerManager.LoadingScreenLeft(traveller);
            AssertShown(viewer, traveller.PlayerGuid);
            AssertShown(traveller, viewer.PlayerGuid);
        }

        [Fact]
        public void AReZoneQueuedBehindTheLast_ContinuesTheHold()
        {
            WorldSession watcher = InWorld(0x7161, "Watcher");
            WorldSession traveller = InWorld(0x7162, "Traveller");

            Started(traveller);
            PlayerManager.ZoneInDone(traveller);
            Started(traveller);   // a retarget / queued re-zone goes out before the first screen dropped
            PlayerManager.LoadingScreenLeft(traveller);   // the first screen's drop report
            Assert.True(PlayerManager.IsHeld(traveller));

            PlayerManager.ZoneInDone(traveller);
            PlayerManager.LoadingScreenLeft(traveller);
            AssertShown(traveller, watcher.PlayerGuid);
            AssertShown(watcher);
        }

        [Fact]
        public void AReZoneDuringTheLoginScreen_WaitsForTheReZonesZoneIn()
        {
            WorldSession online = InWorld(0x7171, "Online");
            WorldSession newcomer = InWorld(0x7172, "Newcomer");
            newcomer.LoadingScreenDown = false;
            var hold = Activator.CreateInstance(Private.Nested(typeof(PlayerManager), "Hold"))!;
            hold.GetType().GetField("ArmedAt")!.SetValue(hold, (DateTime?)DateTime.UtcNow);
            Holds[newcomer] = hold;   // as Enter holds a login

            Started(newcomer);   // e.g. moved to a Starting Location at zone-in
            PlayerManager.LoadingScreenLeft(newcomer);
            Assert.True(PlayerManager.IsHeld(newcomer));

            PlayerManager.ZoneInDone(newcomer);
            PlayerManager.LoadingScreenLeft(newcomer);
            AssertShown(newcomer, online.PlayerGuid);
            AssertShown(online, newcomer.PlayerGuid);   // it never saw the newcomer
        }

        [Fact]
        public void NoDropReport_ShownAfterTheLimits()
        {
            WorldSession watcher = InWorld(0x7181, "Watcher", Other);
            WorldSession armed = InWorld(0x7182, "Armed");
            WorldSession stuck = InWorld(0x7183, "Stuck");
            Started(armed, changedLandscape: true);
            SetZone(armed, Other);
            PlayerManager.ZoneInDone(armed);
            Started(stuck, changedLandscape: true);
            SetZone(stuck, Other);
            DateTime now = DateTime.UtcNow;

            Private.Call(typeof(PlayerManager), "ShowOverdue", now + PlayerManager.LoadingShowLimit - TimeSpan.FromSeconds(1));
            Assert.Empty(Shown(watcher));

            Private.Call(typeof(PlayerManager), "ShowOverdue", now + PlayerManager.LoadingShowLimit + TimeSpan.FromSeconds(1));
            AssertShown(watcher, armed.PlayerGuid);
            Assert.True(PlayerManager.IsHeld(stuck));   // never reached its zone-in: the longer limit

            Private.Call(typeof(PlayerManager), "ShowOverdue", now + PlayerManager.RezoneHoldLimit + TimeSpan.FromSeconds(1));
            AssertShown(watcher, stuck.PlayerGuid);
            Assert.False(PlayerManager.IsHeld(stuck));
        }

        [Fact]
        public void AHeldPlayerWhoTimesOut_IsForgotten()
        {
            WorldSession traveller = InWorld(0x7191, "Traveller");
            Started(traveller);
            Players.Remove(traveller);   // PruneDead → Leave (dead-client timeout)

            Private.Call(typeof(PlayerManager), "ShowOverdue", DateTime.UtcNow);

            Assert.False(PlayerManager.IsHeld(traveller));
        }

        [Fact]
        public void NotInTheWorldYet_NotHeldByTheReZone()
        {
            // An arrival from another world or a re-attach: Enter holds it at its zone-in instead.
            WorldSession arriving = world.Session(0x71A1, "Arriving");
            added.Add(arriving);
            Started(arriving);
            Assert.False(PlayerManager.IsHeld(arriving));
        }

        [Fact]
        public void AShortMove_IsNotHeld()
        {
            WorldSession watcher = InWorld(0x71B1, "Watcher");
            WorldSession hopper = InWorld(0x71B2, "Hopper");
            Vector3 start = hopper.Position;

            Teleport.To(hopper, start + new Vector3(10f, 0f, 0f), snapToGround: false);
            Assert.False(PlayerManager.IsHeld(hopper));
            Assert.True(Teleport.IsShortMovePending(hopper));

            // (A long one goes through Teleport.RezoneNow → PlayerManager.RezoneStarted, the rules above; RezoneNow
            // itself needs the Environment Server for the terrain manifest, which tests don't run.)
            Introduce(watcher, hopper);
            AssertShown(watcher, hopper.PlayerGuid);
        }
    }
}
