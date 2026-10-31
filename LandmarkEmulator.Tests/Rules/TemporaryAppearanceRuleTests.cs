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
using System.Reflection;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// An admin's /setmodel (Game/TemporaryAppearance.cs) and the per-session cast key seed (OPEN-LEADS L189), from the
    /// 0.695 client's readers: UpdateTemporaryAppearance 0x26 / 9 = {u32 model, u64 character} (FUN_142c2ce10),
    /// RemoveTemporaryAppearance 0x26 / 0xA = {u64 character}, LightweightToFullPc's temporary-appearance u32 right before
    /// the title, cast hash = ((counter &amp; 0xff) ^ seed) * 0x502c3f11 (FUN_142e0f810).
    /// </summary>
    public class TemporaryAppearanceRuleTests : IDisposable
    {
        private const uint Zone = 933;   // a landscape no other test uses
        private static ulong nextGuid = 0x7E00_0000_0000_0000;
        private readonly FakeWorld world = new();
        private readonly List<WorldSession> added = new();

        private static IList Players => (IList)Private.Field(typeof(PlayerManager), "players");

        public void Dispose()
        {
            foreach (WorldSession s in added)
                Players.Remove(s);
            world.Dispose();
        }

        private WorldSession Online()
        {
            ulong guid = System.Threading.Interlocked.Increment(ref nextGuid);
            WorldSession s = world.Session(guid, "Tmp" + (guid & 0xFFFF));
            var character = (CharacterModel)typeof(WorldSession).GetField("currentCharacter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
            character.ZoneId = Zone;
            Players.Add(s);
            added.Add(s);
            return s;
        }

        /// <summary>The S→C header is 26 00 09 00 00 00 (u32 sub, the 0.695 PlayerUpdate shape).</summary>
        [Fact]
        public void Update_OpcodeIsPlayerUpdateSub9()
            => Assert.Equal(new byte[] { 0x26, 0x00, 0x09, 0x00, 0x00, 0x00 },
                WorldSession.GetOpcodeBytes(ZoneMessageOpcode.PlayerUpdateUpdateTemporaryAppearance));

        /// <summary>Reader FUN_142c2ce10: the model (+0x20) comes before the character guid (+0x18) on the wire.</summary>
        [Fact]
        public void Update_ModelThenCharacter()
            => Assert.Equal(new Layout().U32(1234).U64(0x1122334455667788).ToArray(),
                TemporaryAppearance.UpdateBody(0x1122334455667788, 1234));

        /// <summary>/setmodel on yourself: everyone on the landscape (you too) gets the model; /setmodel 0 takes it back.</summary>
        [Fact]
        public void SetModel_ReachesTheLandscape_AndZeroRemovesIt()
        {
            WorldSession admin = Online(), other = Online();
            FakeWorld.Outgoing(admin);
            FakeWorld.Outgoing(other);

            AdminCommands.SetModel(admin, new Layout().U64(0).U32(77).ToArray());
            Assert.Equal(77u, admin.TemporaryAppearanceModel);
            foreach (WorldSession s in new[] { admin, other })
                Assert.Contains(FakeWorld.Outgoing(s), p => p.Opcode == ZoneMessageOpcode.PlayerUpdateUpdateTemporaryAppearance
                    && p.Body.SequenceEqual(TemporaryAppearance.UpdateBody(admin.PlayerGuid, 77)));

            AdminCommands.SetModel(admin, new Layout().U64(0).U32(0).ToArray());
            Assert.Equal(0u, admin.TemporaryAppearanceModel);
            Assert.Contains(FakeWorld.Outgoing(other), p => p.Opcode == ZoneMessageOpcode.PlayerUpdateRemoveTemporaryAppearanceWire
                && p.Body.SequenceEqual(BitConverter.GetBytes(admin.PlayerGuid)));
        }

        /// <summary>The admin target's guid picks the player who changes.</summary>
        [Fact]
        public void SetModel_OnTheAdminTarget()
        {
            WorldSession admin = Online(), target = Online();
            AdminCommands.SetModel(admin, new Layout().U64(target.PlayerGuid).U32(5).ToArray());
            Assert.Equal(5u, target.TemporaryAppearanceModel);
            Assert.Equal(0u, admin.TemporaryAppearanceModel);
        }

        /// <summary>A player who meets the changed one later gets the model in the full character data.</summary>
        [Fact]
        public void FullCharacter_CarriesTheModelBeforeTheTitle()
        {
            byte[] plain = Wire.Write(new PlayerUpdateLightweightToFullPc { Guid = 1, TransientId = 5 });
            byte[] marked = Wire.Write(new PlayerUpdateLightweightToFullPc { Guid = 1, TransientId = 5, TemporaryAppearance = 4321, Title = (9, 9, 9) });
            int at = Enumerable.Range(0, plain.Length).First(i => plain[i] != marked[i]);
            Assert.Equal(4321u, BitConverter.ToUInt32(marked, at));
            Assert.Equal(9u, BitConverter.ToUInt32(marked, at + 4));   // the title follows
        }

        /// <summary>The player's own RemoveTemporaryAppearance ends the look for later arrivals too.</summary>
        [Fact]
        public void PlayerDropsTheLook_ItIsForgotten()
        {
            WorldSession admin = Online();
            TemporaryAppearance.Set(admin, 12);
            TemporaryAppearance.Clear(admin);
            Assert.Equal(0u, admin.TemporaryAppearanceModel);
        }

        /// <summary>FUN_142e0f810's cast hash, and every session drawing a non-zero seed of its own.</summary>
        [Fact]
        public void CastKey_IsTheClientsFormula()
        {
            Assert.Equal(unchecked((uint)((3u ^ 0x5BB9EB0Bu) * 0x502c3f11u)), Abilities.CastKey(0x5BB9EB0B, 0x103));
            Assert.Equal(0u, Abilities.CastKey(0, 0x100));
            Assert.True(Abilities.CastKeyMatches(7, 9, Abilities.CastKey(7, 9)));
            Assert.False(Abilities.CastKeyMatches(7, 9, Abilities.CastKey(8, 9)));
            Assert.NotEqual(0u, Online().CastKeySeed);
        }
    }
}
