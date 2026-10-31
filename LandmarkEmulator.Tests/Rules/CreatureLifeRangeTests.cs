using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Game.Terrain;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// A creature's name and health bar only within CreatureLifeRange (player report 2026-10-31: "red health bars for
    /// monsters ... I can see them from far away"; trace/0695/healthbar-range.md). The client draws the bar at any distance
    /// once the entity has a Life record, so the server gives the Life to players within the range and takes it back
    /// (ResourcesBase sub 1, no records) 10 m beyond it.
    /// </summary>
    public class CreatureLifeRangeTests : IDisposable
    {
        private static int nextZone = 9800;
        private readonly uint zone = (uint)System.Threading.Interlocked.Increment(ref nextZone);
        private readonly FakeWorld world = new();
        private static readonly FieldInfo LifeRows = typeof(Creatures).GetField("lifeRows", BindingFlags.NonPublic | BindingFlags.Static)!;
        private readonly object previousRows = LifeRows.GetValue(null);
        private readonly float previousRange = Creatures.LifeRange;

        public CreatureLifeRangeTests()
        {
            TerrainHeights.TestGround = (x, z, z0) => z0 == zone ? 1010f : null;
            // Resources.txt is not shipped with the tests: one Life row stands in for the table's.
            LifeRows.SetValue(null, new List<(uint Row, uint Max)> { (4, 100000) });
            Creatures.LifeRange = 100f;
        }

        public void Dispose()
        {
            Creatures.LifeRange = previousRange;
            LifeRows.SetValue(null, previousRows);
            TerrainHeights.TestGround = null;
            world.Dispose();
        }

        private WorldSession Viewer(ulong guid)
        {
            WorldSession session = world.Session(guid, "Viewer");
            var character = (LandmarkEmulator.Database.Character.Model.CharacterModel)typeof(WorldSession)
                .GetField("currentCharacter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session);
            character.ZoneId = zone;
            (character.PositionX, character.PositionY, character.PositionZ) = (100f, 1010f, 100f);
            return session;
        }

        private static void MoveTo(WorldSession session, float x)
        {
            var character = (LandmarkEmulator.Database.Character.Model.CharacterModel)typeof(WorldSession)
                .GetField("currentCharacter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session);
            character.PositionX = x;
        }

        private static void Sync(WorldSession session, object creature, bool fresh = false)
            => Private.Call(typeof(Creatures), "SyncLife", session, creature, fresh);

        /// <summary>The ResourcesBase packets sent: (sub, guid).</summary>
        private static List<(byte Sub, ulong Guid, int Length)> Resources(WorldSession session)
            => FakeWorld.Outgoing(session).Where(p => p.Opcode == ZoneMessageOpcode.ResourcesBase).Select(p =>
            {
                var reader = new GamePacketReader(p.Body);
                reader.ReadUInt();                     // sequence
                byte sub = reader.ReadByte();
                return (sub, reader.ReadULong(), p.Body.Length);
            }).ToList();

        [Fact]
        public void Life_IsGivenWithinRange_KeptInTheSlack_AndTakenBackBeyondIt()
        {
            WorldSession viewer = Viewer(0x7100_0000_0000_0061);
            Creatures.SpawnNear(viewer, 3f, 0f);
            ulong guid = Creatures.NearestGuid(viewer, 10f);
            Assert.NotEqual(0ul, guid);
            object creature = Private.Call(typeof(Creatures), "Find", guid);
            FakeWorld.Outgoing(viewer);

            Sync(viewer, creature, fresh: true);
            Assert.Equal(new[] { ((byte)2, guid) }, Resources(viewer).Select(r => (r.Sub, r.Guid)));   // SetCharacterResource
            Sync(viewer, creature);
            Assert.Empty(Resources(viewer));                                                          // unchanged: nothing

            MoveTo(viewer, 100f + 3f + 105f);                                                         // 105 m: within the slack
            Sync(viewer, creature);
            Assert.Empty(Resources(viewer));

            MoveTo(viewer, 100f + 3f + 111f);                                                         // 111 m: taken back
            Sync(viewer, creature);
            var cleared = Resources(viewer).Single();
            Assert.Equal(((byte)1, guid), (cleared.Sub, cleared.Guid));                               // SetCharacterResources
            Assert.Equal(4 + 1 + 8 + 4, cleared.Length);                                              // with no records
            Sync(viewer, creature);
            Assert.Empty(Resources(viewer));

            MoveTo(viewer, 100f + 3f + 99f);                                                          // back within 100 m
            Sync(viewer, creature);
            Assert.Equal(new[] { ((byte)2, guid) }, Resources(viewer).Select(r => (r.Sub, r.Guid)));
        }

        [Fact]
        public void ARangeBelowTheClientsLockDistance_CountsAs50m()
        {
            // The client drops a locked target without Life as dead within its 50 m lock range (FUN_14332d590).
            Creatures.LifeRange = 25f;
            WorldSession viewer = Viewer(0x7100_0000_0000_0063);
            Creatures.SpawnNear(viewer, 3f, 0f);
            ulong guid = Creatures.NearestGuid(viewer, 10f);
            object creature = Private.Call(typeof(Creatures), "Find", guid);
            MoveTo(viewer, 100f + 3f + 45f);
            FakeWorld.Outgoing(viewer);
            Sync(viewer, creature, fresh: true);
            Assert.Equal(new[] { ((byte)2, guid) }, Resources(viewer).Select(r => (r.Sub, r.Guid)));
        }

        [Fact]
        public void ResourceSequences_StayBehindTheClientsClock()
        {
            // A sub 2 whose sequence is ahead of the client's synced clock (server ms - ~150) is held a frame (FUN_14361cd30).
            uint sequence = (uint)Private.Call(typeof(CharacterResources), "NextSequence");
            uint clock = (uint)LandmarkEmulator.WorldServer.Game.ServerClock.Milliseconds - 150u;
            Assert.True(unchecked((int)(sequence - clock)) < 0);
        }

        [Fact]
        public void AFarCreature_GetsNoLifeAtSpawn_UnlessTheRangeIsOff()
        {
            WorldSession viewer = Viewer(0x7100_0000_0000_0062);
            Creatures.SpawnNear(viewer, 3f, 0f);
            ulong guid = Creatures.NearestGuid(viewer, 10f);
            object creature = Private.Call(typeof(Creatures), "Find", guid);
            MoveTo(viewer, 100f + 3f + 150f);
            FakeWorld.Outgoing(viewer);

            Sync(viewer, creature, fresh: true);
            Assert.Empty(Resources(viewer));

            Creatures.LifeRange = 0f;                                                                 // off: as far as streamed
            Sync(viewer, creature, fresh: true);
            Assert.Equal(new[] { ((byte)2, guid) }, Resources(viewer).Select(r => (r.Sub, r.Guid)));
        }
    }
}
