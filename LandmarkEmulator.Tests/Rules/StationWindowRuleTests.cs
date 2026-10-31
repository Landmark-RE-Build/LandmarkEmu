using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Network;
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
    /// The server closes a station window its player walked away from (Game/Crafting CloseWindowsAway). A Spire's own
    /// Replicators and Repair Station are template props, not placed ones: until 2026-10-21 their position was not found
    /// and every Spire station window was closed half a second after it opened.
    /// </summary>
    public class StationWindowRuleTests
    {
        private const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
        private static ulong nextGuid = 0x7B20_0000_0000_0000;

        /// <summary>A Spire on a test landscape with one station prop at <paramref name="at"/>; returns the prop's guid.</summary>
        private static ulong SpireStation(uint zone, Vector3 at)
        {
            ulong guid = System.Threading.Interlocked.Increment(ref nextGuid);
            Type siteType = typeof(Spire).GetNestedType("Site", BindingFlags.NonPublic)!;
            object site = Activator.CreateInstance(siteType)!;
            siteType.GetField("Zone")!.SetValue(site, zone);
            siteType.GetField("Props")!.SetValue(site, new List<PlayerUpdateAddLightweightProp>
            {
                new() { Guid = guid, Position = new Vector4(at, 1f) }
            });
            var sites = (IDictionary)typeof(Spire).GetField("sites", Static)!.GetValue(null)!;
            sites[zone] = site;
            var definitions = (System.Collections.Concurrent.ConcurrentDictionary<ulong, uint>)typeof(Spire).GetField("propDefinitions", Static)!.GetValue(null)!;
            definitions[guid] = 2894;
            return guid;
        }

        private static void Tick()
        {
            typeof(Crafting).GetField("lastAwayCheck", Static)!.SetValue(null, DateTime.MinValue);
            typeof(Crafting).GetMethod("CloseWindowsAway", Static)!.Invoke(null, null);
        }

        private static bool Closed(WorldSession s)
            => FakeWorld.Outgoing(s).Any(p => p.Opcode == ZoneMessageOpcode.RecipeBase && p.Body.Length >= 9 && BitConverter.ToUInt32(p.Body, 0) == 0x24 && p.Body[8] == 0);

        private static WorldSession At(FakeWorld world, Vector3 at)
        {
            ulong guid = System.Threading.Interlocked.Increment(ref nextGuid);
            WorldSession s = world.Session(guid, "Crafter" + (guid & 0xFFFF));
            var character = (CharacterModel)typeof(WorldSession).GetField("currentCharacter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
            character.PositionX = at.X;
            character.PositionY = at.Y;
            character.PositionZ = at.Z;
            return s;
        }

        [Fact]
        public void SpireStation_StaysOpenNearby_ClosesWhenWalkedAway()
        {
            using var world = new FakeWorld();
            ulong station = SpireStation(9901, new Vector3(100f, 50f, 100f));
            WorldSession s = At(world, new Vector3(103f, 50f, 100f));
            try
            {
                Crafting.StationOpened(s, 53, station);
                Tick();
                Assert.False(Closed(s));                                       // 3 m away: open
                var character = (CharacterModel)typeof(WorldSession).GetField("currentCharacter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
                character.PositionX = 130f;
                Tick();
                Assert.True(Closed(s));                                        // 30 m away: closed
            }
            finally
            {
                Crafting.StationClosed(s);
            }
        }

        /// <summary>
        /// Crafting at a Spire station (player report 2026-10-31: the Replicator "won't take resources or give me what I
        /// crafted"): the craft start looked the station up among placed props only and refused every Spire craft as too far.
        /// It now uses the walk-away close's own station and range.
        /// </summary>
        [Fact]
        public void SpireStation_CraftsNearby_RefusesFarAway()
        {
            using var world = new FakeWorld();
            ulong station = SpireStation(9903, new Vector3(100f, 50f, 100f));
            WorldSession s = At(world, new Vector3(103f, 50f, 100f));
            try
            {
                Crafting.StationOpened(s, 53, station);
                Assert.False(Crafting.StationTooFar(s, station));             // 3 m away: crafts
                var character = (CharacterModel)typeof(WorldSession).GetField("currentCharacter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
                character.PositionX = 130f;
                Assert.True(Crafting.StationTooFar(s, station));              // 30 m away: too far
            }
            finally
            {
                Crafting.StationClosed(s);
            }
        }

        [Fact]
        public void BigStation_OpenedFromFurtherThanEightMetres_Crafts()
        {
            using var world = new FakeWorld();
            ulong station = SpireStation(9904, new Vector3(0f, 0f, 0f));
            WorldSession s = At(world, new Vector3(10f, 0f, 0f));               // clicked 10 m from its pivot
            try
            {
                Crafting.StationOpened(s, 53, station);
                Assert.False(Crafting.StationTooFar(s, station));
            }
            finally
            {
                Crafting.StationClosed(s);
            }
        }

        [Fact]
        public void BigStation_OpenedFromFurtherThanTheLimit_StaysOpen()
        {
            using var world = new FakeWorld();
            ulong station = SpireStation(9902, new Vector3(0f, 0f, 0f));
            WorldSession s = At(world, new Vector3(10f, 0f, 0f));               // clicked 10 m from its pivot
            try
            {
                Crafting.StationOpened(s, 53, station);
                Tick();
                Assert.False(Closed(s));
            }
            finally
            {
                Crafting.StationClosed(s);
            }
        }
    }
}
