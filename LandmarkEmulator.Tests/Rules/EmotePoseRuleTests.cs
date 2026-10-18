using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// Stances and "stand" (Game/Emotes.cs): they go out as PlayerUpdate 0x65 RequestAnimationSelection with the clear-sit
    /// bool, so a client /sit ends on the sitting client too (its movement then stops telling everyone "sitting"); the
    /// stand reaches everyone who saw the stance; a stance reaches players who meet the player later. Bug 2026-10-18: after
    /// "/sit, /stand" an observer saw the player sitting while the player's own client showed them standing.
    /// </summary>
    public class EmotePoseRuleTests
    {
        private const uint Stand = 223, Kneel = 224, Dance = 246, Wave = 217, Calm = 218;
        private static ulong nextGuid = 0x7A00_0000_0000_0000;

        private static WorldSession At(FakeWorld world, float x, float z = 0f, uint zone = 406)
        {
            ulong guid = System.Threading.Interlocked.Increment(ref nextGuid);
            WorldSession s = world.Session(guid, "Pose" + (guid & 0xFFFF));
            var character = (CharacterModel)typeof(WorldSession).GetField("currentCharacter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
            character.PositionX = x;
            character.PositionZ = z;
            character.ZoneId = zone;
            return s;
        }

        /// <summary>The selections each viewer got as RequestAnimationSelection for this subject (other packets dropped).</summary>
        private static uint[] Poses(WorldSession viewer, WorldSession subject)
            => FakeWorld.Outgoing(viewer)
                .Where(p => p.Opcode == ZoneMessageOpcode.PlayerUpdateRequestAnimationSelectionWire
                    && System.BitConverter.ToUInt64(p.Body, 0) == subject.PlayerGuid)
                .Select(p => System.BitConverter.ToUInt32(p.Body, 8)).ToArray();

        [Fact]
        public void RequestAnimationSelection_Layout()
        {
            // 0.695 reader FUN_142c2ae00: header (u16 base, u32 sub), u64 +0x18, u32 +0x20, bool +0x24, bool +0x25.
            Assert.Equal(Wire.Hex("26 00 65 00 00 00"), WorldSession.GetOpcodeBytes(ZoneMessageOpcode.PlayerUpdateRequestAnimationSelectionWire));
            Assert.Equal(Wire.Hex("88 77 66 55 44 33 22 11 DF 00 00 00 01 00"), Emotes.SelectionBody(0x1122334455667788, Stand));
            Assert.Equal(Wire.Hex("01 00 00 00 00 00 00 00 50 03 00 00 00 01"), Emotes.SelectionBody(1, 848, clearSit: false, seated: true));
        }

        [Theory]
        [InlineData(Stand, true)]
        [InlineData(Kneel, true)]
        [InlineData(Dance, true)]
        [InlineData(Emotes.PlayDead, true)]
        [InlineData(Wave, false)]
        [InlineData(Calm, false)]
        public void StancesAndStand_ClearTheClientSit_EmotesAndMoodsDoNot(uint selection, bool clears)
            => Assert.Equal(clears, Emotes.ClearsSit(selection));

        /// <summary>The sitting bit is the one the client's writer takes from +0x1461 bit 0x4, and each change forces a full body.</summary>
        [Fact]
        public void SittingBit_IsReadFromTheMovementWord_AndForcesAFullBody()
        {
            Assert.Equal(0x2000000u, PositionUpdate.SittingState);
            Assert.NotEqual(0u, PositionUpdate.SittingState & 0x201f810u);
            // Type 1 body: u8 type, u32 time, u8 version, then the 2-bit-length uint (0x2000001 << 2 | 3 extra bytes).
            byte[] body = { 0x01, 0, 0, 0, 0, 1, 0x07, 0x00, 0x00, 0x08 };
            Assert.True(PositionUpdate.TryReadState(body, out uint state));
            Assert.Equal(0x2000001u, state);
        }

        /// <summary>
        /// /kneel then /stand with a player near (10 m) and one far (200 m) away: the near one sees both; the far one, once
        /// they walk within range, is shown the kneel once, and then gets the stand although they walked away again.
        /// </summary>
        [Fact]
        public void Stance_ReachesNearbyAndLateComers_Stand_ReachesEveryoneWhoSawIt()
        {
            using var world = new FakeWorld();
            WorldSession subject = At(world, 0f), near = At(world, 10f), far = At(world, 200f), elsewhere = At(world, 5f, zone: 407);
            var zone = new[] { subject, near, far };

            Assert.Equal(1, Emotes.ShowPose(subject, Kneel, zone));
            Assert.Equal(new[] { Kneel }, Poses(subject, subject));
            Assert.Equal(new[] { Kneel }, Poses(near, subject));
            Assert.Empty(Poses(far, subject));
            Assert.Empty(Poses(elsewhere, subject));
            Assert.Equal(Kneel, Emotes.StanceOf(subject.PlayerGuid));

            // The far player walks within range: shown once, not on every movement packet.
            Emotes.Moved(far, new System.Numerics.Vector3(60f, 0f, 0f));
            Emotes.Moved(far, new System.Numerics.Vector3(61f, 0f, 0f));
            Assert.Equal(new[] { Kneel }, Poses(far, subject));
            // Someone on another landscape at the same coordinates is not.
            Emotes.Moved(elsewhere, new System.Numerics.Vector3(5f, 0f, 0f));
            Assert.Empty(Poses(elsewhere, subject));

            // /stand: the far player (back at 200 m) saw the kneel, so they get the stand too.
            Assert.Equal(2, Emotes.ShowPose(subject, Stand, zone));
            Assert.Equal(new[] { Stand }, Poses(subject, subject));
            Assert.Equal(new[] { Stand }, Poses(near, subject));
            Assert.Equal(new[] { Stand }, Poses(far, subject));
            Assert.Equal(0u, Emotes.StanceOf(subject.PlayerGuid));
        }

        /// <summary>A plain /stand (ending a client /sit, which the server never saw as a stance) goes to the player and those in range.</summary>
        [Fact]
        public void StandAfterAClientSit_ReachesThePlayerAndThoseInRange()
        {
            using var world = new FakeWorld();
            WorldSession subject = At(world, 0f), near = At(world, 30f), far = At(world, 300f);
            Assert.Equal(1, Emotes.ShowPose(subject, Stand, new[] { subject, near, far }));
            Assert.Equal(new[] { Stand }, Poses(subject, subject));
            Assert.Equal(new[] { Stand }, Poses(near, subject));
            Assert.Empty(Poses(far, subject));
        }

        /// <summary>A player who meets someone in a stance (full character data) is shown it; after the stand, nothing.</summary>
        [Fact]
        public void Newcomer_IsShownTheCurrentStance_OnlyWhileItLasts()
        {
            using var world = new FakeWorld();
            WorldSession subject = At(world, 0f), first = At(world, 500f), second = At(world, 500f);
            Emotes.ShowPose(subject, Emotes.PlayDead, new[] { subject });
            FakeWorld.Outgoing(subject);

            Emotes.IntroducePose(first, subject);
            Assert.Equal(new[] { Emotes.PlayDead }, Poses(first, subject));
            Emotes.IntroducePose(subject, subject);
            Assert.Empty(Poses(subject, subject));

            // The newcomer was told, so the stand reaches them even at 500 m.
            Emotes.ShowPose(subject, Stand, new[] { subject, first, second });
            Assert.Equal(new[] { Stand }, Poses(first, subject));
            Assert.Empty(Poses(second, subject));

            Emotes.IntroducePose(second, subject);
            Assert.Empty(Poses(second, subject));
        }

        /// <summary>Walking out of a stance: newcomers are no longer shown it while the stand waits for the player to stop.</summary>
        [Fact]
        public void WalkingOut_StopsShowingTheStanceToNewcomers()
        {
            using var world = new FakeWorld();
            WorldSession subject = At(world, 0f), newcomer = At(world, 20f);
            Emotes.ShowPose(subject, Dance, new[] { subject });
            Emotes.Moved(subject, new System.Numerics.Vector3(2f, 0f, 0f));
            Assert.Equal(0u, Emotes.StanceOf(subject.PlayerGuid));
            Emotes.IntroducePose(newcomer, subject);
            Emotes.Moved(newcomer, new System.Numerics.Vector3(20f, 0f, 0f));
            Assert.Empty(Poses(newcomer, subject));
            Emotes.Forget(subject);
        }

        /// <summary>Leaving the world drops the player's stance and their place in everyone else's.</summary>
        [Fact]
        public void Forget_DropsTheStance()
        {
            using var world = new FakeWorld();
            WorldSession subject = At(world, 0f), viewer = At(world, 5f);
            Emotes.ShowPose(subject, Kneel, new[] { subject, viewer });
            Emotes.Forget(subject);
            Assert.Equal(0u, Emotes.StanceOf(subject.PlayerGuid));
            FakeWorld.Outgoing(viewer);
            Emotes.IntroducePose(viewer, subject);
            Assert.Empty(Poses(viewer, subject));
        }
    }
}
