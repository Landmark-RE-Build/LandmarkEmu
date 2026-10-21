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
    /// The loading-screen stream hold (PlayerManager "Stream hold", setting StreamHoldUntilLoaded): while a login's or a
    /// re-zone's loading screen is up, only what the arrival stands on goes out (the placed props under it, the Spire's
    /// deck); the other placed props, the Spire's stations, the warp crystals and the ticked streams (creatures, trees,
    /// chests, outposts: <see cref="WorldSession.StreamsOnHold"/>) wait for the client's ClientLoading {0}, or for the
    /// limit. It works around the 0.695 client's 64 allocator slots on 16+ thread CPUs (docs/terrain.md "Allocator slots").
    /// </summary>
    public class StreamHoldRuleTests : IDisposable
    {
        /// <summary>A landscape id no real data uses: its Spire and crystals are set up here.</summary>
        private const uint Zone = 9301;

        private const ulong NearProp = 0x7E01, FarProp = 0x7E02, DeckProp = 0x7E10, StationProp = 0x7E11, Crystal = 0x7E20;

        private readonly FakeWorld world;
        private readonly List<WorldSession> added = new();
        private readonly List<Props.PlacedProp> placed = new();

        private static IList Players => (IList)Private.Field(typeof(PlayerManager), "players");
        private static IDictionary Holds => (IDictionary)Private.Field(typeof(PlayerManager), "holds");
        private static object PropsSync => Private.Field(typeof(Props), "sync");

        private static readonly FieldInfo ChestsField = typeof(Chests).GetField("chestProps", BindingFlags.NonPublic | BindingFlags.Static)!;
        private static readonly FieldInfo CreaturesField = typeof(StoryTools).GetField("creatureProps", BindingFlags.NonPublic | BindingFlags.Static)!;
        private static IDictionary Kinds => (IDictionary)Private.Field(typeof(StoryTools), "kindCache");
        private readonly object previousChests, previousCreatures;
        private readonly bool addedKind;

        public StreamHoldRuleTests()
        {
            world = new FakeWorld();
            // A prop spawn asks whether the prop is a chest, a creature prop or a Link tool, from game tables that tests
            // don't load: none of them (restored on Dispose).
            previousChests = ChestsField.GetValue(null);
            ChestsField.SetValue(null, previousChests ?? new Dictionary<uint, uint>());
            previousCreatures = CreaturesField.GetValue(null);
            CreaturesField.SetValue(null, previousCreatures ?? new HashSet<uint>());
            lock (Kinds)
                if (!Kinds.Contains(1u))
                {
                    Kinds[1u] = null;   // prop 1: no Link kind
                    addedKind = true;
                }
            Place(NearProp, new Vector3(5f, -3f, 4f));     // under the arrival (within 48 m sideways, 64 m down)
            Place(FarProp, new Vector3(220f, 0f, 30f));    // within the 400 m stream range, not under the arrival
            SetUpSpire();
            SetUpCrystal();
        }

        public void Dispose()
        {
            foreach (WorldSession s in added)
            {
                Players.Remove(s);
                Holds.Remove(s);
                Props.Forget(s);
            }
            lock (PropsSync)
                foreach (Props.PlacedProp p in placed)
                {
                    Private.Call(typeof(Props), "UnindexLocked", p);
                    ((IList)Private.Field(typeof(Props), "props")).Remove(p);
                    ((IDictionary)Private.Field(typeof(Props), "byGuid")).Remove(p.Spawn.Guid);
                }
            ((IDictionary)Private.Field(typeof(Spire), "sites")).Remove(Zone);
            ((IDictionary)Private.Field(typeof(WarpNetwork), "underground")).Remove(Zone);
            ((IDictionary)Private.Field(typeof(WarpNetwork), "crystals")).Remove(Zone);
            ChestsField.SetValue(null, previousChests);
            CreaturesField.SetValue(null, previousCreatures);
            if (addedKind)
                lock (Kinds)
                    Kinds.Remove(1u);
            world.Dispose();
        }

        // ---- set-up ------------------------------------------------------------------------------------------------

        private void Place(ulong guid, Vector3 at)
        {
            var p = new Props.PlacedProp
            {
                PropId = 1,
                Spawn = new PlayerUpdateAddLightweightProp { Guid = guid, TransientId = (uint)guid, Position = new Vector4(at, 1f) },
                Zone = Zone,
                Placed = true
            };
            lock (PropsSync)
            {
                ((IList)Private.Field(typeof(Props), "props")).Add(p);
                ((IDictionary)Private.Field(typeof(Props), "byGuid"))[guid] = p;
                Private.Call(typeof(Props), "IndexLocked", p);
            }
            placed.Add(p);
        }

        /// <summary>A Spire on the test landscape, 300 m away: its Hub (deck, model 596) and a station.</summary>
        private static void SetUpSpire()
        {
            Type siteType = Private.Nested(typeof(Spire), "Site");
            object site = Activator.CreateInstance(siteType)!;
            siteType.GetField("Zone")!.SetValue(site, Zone);
            siteType.GetField("Position")!.SetValue(site, new Vector3(300f, 0f, 0f));
            siteType.GetField("Props")!.SetValue(site, new List<PlayerUpdateAddLightweightProp>
            {
                new() { Guid = DeckProp, TransientId = 0x7E10, ModelId = 596, Position = new Vector4(300f, 0f, 0f, 1f) },
                new() { Guid = StationProp, TransientId = 0x7E11, ModelId = 4242, Position = new Vector4(305f, 0f, 0f, 1f) },
            });
            ((IDictionary)Private.Field(typeof(Spire), "sites"))[Zone] = site;
        }

        /// <summary>One underground warp crystal on the test landscape (its prop already built).</summary>
        private static void SetUpCrystal()
        {
            Type nodeType = Private.Nested(typeof(WarpNetwork), "Node");
            object node = Activator.CreateInstance(nodeType)!;
            nodeType.GetField("Id")!.SetValue(node, Crystal);
            var nodes = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(nodeType))!;
            nodes.Add(node);
            ((IDictionary)Private.Field(typeof(WarpNetwork), "underground"))[Zone] = nodes;
            ((IDictionary)Private.Field(typeof(WarpNetwork), "crystals"))[Zone] =
                new List<PlayerUpdateAddLightweightProp> { new() { Guid = Crystal, TransientId = 0x7E20 } };
        }

        private WorldSession Player(ulong guid, string name, bool loading)
        {
            WorldSession s = world.Session(guid, name);
            var character = (LandmarkEmulator.Database.Character.Model.CharacterModel)typeof(WorldSession)
                .GetField("currentCharacter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
            character.ZoneId = Zone;
            s.LoadingScreenDown = !loading;
            Players.Add(s);
            added.Add(s);
            return s;
        }

        /// <summary>What Enter does for the world objects of a login: the hold, then the props / Spire / crystals.</summary>
        private static void EnterWorld(WorldSession s)
        {
            object hold = Private.Call(typeof(PlayerManager), "BeginEntryHold", s, DateTime.UtcNow);
            MethodInfo send = typeof(PlayerManager).GetMethod("SendWorldObjects", BindingFlags.NonPublic | BindingFlags.Static)!;
            send.Invoke(null, new[] { s, hold, true, false });
        }

        private static List<ulong> PropsSent(WorldSession s)
            => FakeWorld.Outgoing(s)
                .Where(p => p.Opcode == ZoneMessageOpcode.PlayerUpdateAddLightweightPropWire)
                .Select(p => BitConverter.ToUInt64(p.Body, 0))
                .ToList();

        /// <summary>The props (any order) the session was sent since the last look, each once.</summary>
        private static void AssertSent(WorldSession s, params ulong[] guids)
        {
            List<ulong> sent = PropsSent(s);
            Assert.True(sent.OrderBy(g => g).SequenceEqual(guids.OrderBy(g => g)),
                $"sent [{string.Join(", ", sent.Select(g => $"0x{g:X}"))}]");
        }

        private static PlayerUpdateLoading Loading(byte value)
        {
            var m = new PlayerUpdateLoading();
            typeof(InformationalMessage).GetProperty(nameof(InformationalMessage.Body))!.GetSetMethod(true)!
                .Invoke(m, new object[] { new[] { value } });
            return m;
        }

        private static void ShowOverdue(DateTime at) => Private.Call(typeof(PlayerManager), "ShowOverdue", at);

        // ---- rules -------------------------------------------------------------------------------------------------

        [Fact]
        public void Login_OnlyTheArrivalAndTheDeck_UntilTheScreenDrops_ThenTheRestOnce()
        {
            WorldSession s = Player(0x7E81, "Loader", loading: true);

            EnterWorld(s);

            AssertSent(s, NearProp, DeckProp);
            Assert.True(s.StreamsOnHold);   // creatures, trees, chests, outposts wait (their ticks ask this)
            Assert.False(Props.StreamStarted(s));

            MiscHandler.HandleLoading(s, Loading(1));   // a screen coming up: nothing
            Assert.Empty(PropsSent(s));

            MiscHandler.HandleLoading(s, Loading(0));

            AssertSent(s, FarProp, StationProp, Crystal);   // nothing twice
            Assert.False(s.StreamsOnHold);
            Assert.True(Props.StreamStarted(s));
            Assert.False(PlayerManager.IsHeld(s));
        }

        [Fact]
        public void Login_NoDropReport_ReleasedAtTheLimit()
        {
            WorldSession s = Player(0x7E82, "NeverDrops", loading: true);
            EnterWorld(s);
            FakeWorld.Outgoing(s);
            DateTime now = DateTime.UtcNow;

            ShowOverdue(now + PlayerManager.StreamHoldLimit - TimeSpan.FromSeconds(2));
            Assert.Empty(PropsSent(s));
            Assert.True(s.StreamsOnHold);

            ShowOverdue(now + PlayerManager.StreamHoldLimit + TimeSpan.FromSeconds(1));
            AssertSent(s, FarProp, StationProp, Crystal);
            Assert.False(s.StreamsOnHold);

            MiscHandler.HandleLoading(s, Loading(0));   // a late drop sends nothing again
            Assert.Empty(PropsSent(s));
        }

        [Fact]
        public void TheLimitSetting_IsHonoured_AndClamped()
        {
            world.Config.StreamHoldLimitSeconds = 30;
            Assert.Equal(TimeSpan.FromSeconds(30), PlayerManager.StreamHoldLimit);
            world.Config.StreamHoldLimitSeconds = 1;
            Assert.Equal(TimeSpan.FromSeconds(10), PlayerManager.StreamHoldLimit);
            world.Config.StreamHoldLimitSeconds = 100000;
            Assert.Equal(TimeSpan.FromSeconds(600), PlayerManager.StreamHoldLimit);
        }

        [Fact]
        public void SettingOff_EverythingAtZoneIn_AsBefore()
        {
            world.Config.StreamHoldUntilLoaded = false;
            WorldSession s = Player(0x7E83, "Classic", loading: true);

            EnterWorld(s);

            AssertSent(s, NearProp, FarProp, DeckProp, StationProp, Crystal);
            Assert.False(s.StreamsOnHold);
            Assert.True(PlayerManager.IsHeld(s));   // the introductions still wait for the drop (unchanged)
        }

        [Fact]
        public void ReZone_HeldUntilItsOwnDrop_NotTheScreenBefore()
        {
            WorldSession s = Player(0x7E84, "Traveller", loading: false);
            PlayerManager.RezoneStarted(s, changedLandscape: false);
            Assert.True(s.StreamsOnHold);

            MiscHandler.HandleLoading(s, Loading(0));   // the screen before this re-zone's zone-in: ignored
            Assert.True(s.StreamsOnHold);

            PlayerManager.Reintroduce(s);   // the zone-in (MiscHandler.FinishZoneIn): only the arrival and the deck
            AssertSent(s, NearProp, DeckProp);
            Assert.True(s.StreamsOnHold);
            Assert.Equal(Spire.SpirePart.Deck, PlayerManager.SpirePartSent(s));   // FinishRezone re-asserts the deck only

            MiscHandler.HandleLoading(s, Loading(0));
            AssertSent(s, FarProp, StationProp, Crystal);
            Assert.False(s.StreamsOnHold);
            Assert.Equal(Spire.SpirePart.All, PlayerManager.SpirePartSent(s));
        }

        [Fact]
        public void ReZone_TheLimitRunsFromTheZoneIn()
        {
            WorldSession s = Player(0x7E85, "SlowLoad", loading: false);
            PlayerManager.RezoneStarted(s, changedLandscape: false);
            DateTime started = DateTime.UtcNow;

            ShowOverdue(started + PlayerManager.StreamHoldLimit + TimeSpan.FromSeconds(1));
            Assert.True(s.StreamsOnHold);   // no zone-in yet: the client is still loading the re-zone

            PlayerManager.Reintroduce(s);
            FakeWorld.Outgoing(s);
            DateTime zoneIn = DateTime.UtcNow;
            ShowOverdue(zoneIn + PlayerManager.StreamHoldLimit - TimeSpan.FromSeconds(2));
            Assert.True(s.StreamsOnHold);

            ShowOverdue(zoneIn + PlayerManager.StreamHoldLimit + TimeSpan.FromSeconds(1));
            Assert.False(s.StreamsOnHold);
            AssertSent(s, FarProp, StationProp, Crystal);
        }

        [Fact]
        public void ReZone_NeverZonedIn_ReleasedWithTheReZoneLimit()
        {
            WorldSession s = Player(0x7E86, "Lost", loading: false);
            PlayerManager.RezoneStarted(s, changedLandscape: false);

            ShowOverdue(DateTime.UtcNow + PlayerManager.RezoneHoldLimit + TimeSpan.FromSeconds(1));

            Assert.False(s.StreamsOnHold);
            Assert.False(PlayerManager.IsHeld(s));
            Assert.Empty(PropsSent(s));   // nothing was owed: its Reintroduce, when it comes, sends everything
        }

        [Fact]
        public void AReZoneDuringTheLoginHold_WaitsForTheReZonesDrop()
        {
            WorldSession s = Player(0x7E87, "MovedAtLogin", loading: true);
            EnterWorld(s);
            FakeWorld.Outgoing(s);

            PlayerManager.RezoneStarted(s, changedLandscape: false);   // e.g. put on a Starting Location at zone-in
            s.RezoneInFlight = true;
            MiscHandler.HandleLoading(s, Loading(0));   // the login screen's drop: belongs to the screen before
            Assert.True(s.StreamsOnHold);
            Assert.Empty(PropsSent(s));

            Props.Forget(s);   // the re-zone's ClientIsReady: its client has no prop any more
            PlayerManager.Reintroduce(s);
            s.RezoneInFlight = false;   // Teleport.FinishRezone
            AssertSent(s, NearProp, DeckProp);

            MiscHandler.HandleLoading(s, Loading(0));
            AssertSent(s, FarProp, StationProp, Crystal);
            Assert.False(s.StreamsOnHold);
        }

        [Fact]
        public void ALimitDuringAReZoneInFlight_SendsNothingToTheLoadingClient()
        {
            WorldSession s = Player(0x7E88, "Busy", loading: true);
            EnterWorld(s);
            FakeWorld.Outgoing(s);
            s.RezoneInFlight = true;   // a re-zone went out without RezoneStarted (not in the world list then)

            ShowOverdue(DateTime.UtcNow + PlayerManager.StreamHoldLimit + TimeSpan.FromSeconds(1));

            Assert.False(s.LoadingHoldsStreams);
            Assert.Empty(PropsSent(s));   // its Reintroduce sends them
        }

        [Fact]
        public void LeavingDuringTheHold_EndsIt()
        {
            WorldSession s = Player(0x7E89, "Gone", loading: true);
            EnterWorld(s);
            Players.Remove(s);   // PruneDead → Leave (dead-client timeout), or a logout

            ShowOverdue(DateTime.UtcNow);

            Assert.False(PlayerManager.IsHeld(s));
            Assert.False(s.LoadingHoldsStreams);
        }

        [Fact]
        public void ArrivingOnTheSpire_GetsAllItsPropsEarly()
        {
            // Covers() needs a templated Spire (a corner): emulate one by asking for the whole Spire as Enter would there.
            WorldSession s = Player(0x7E8A, "OnDeck", loading: true);
            Spire.Enter(s, Spire.SpirePart.All);
            AssertSent(s, DeckProp, StationProp);
            Spire.Enter(s, Spire.SpirePart.Deck);
            AssertSent(s, DeckProp);
            Spire.Enter(s, Spire.SpirePart.Rest);
            AssertSent(s, StationProp);
        }
    }
}
