using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Game.Inventory;
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
    /// The acting player's own screen and an observer's screen agree (audit 2026-10-21, docs/observer-sync.md): what a
    /// player is or does reaches the others now, later, after a re-zone and when they walk into range.
    /// </summary>
    public class ObserverSyncRuleTests : IDisposable
    {
        private const uint Zone = 931;   // a landscape no other test uses
        private static ulong nextGuid = 0x7D00_0000_0000_0000;
        private readonly FakeWorld world = new();
        private readonly List<WorldSession> added = new();

        private static IList Players => (IList)Private.Field(typeof(PlayerManager), "players");

        public void Dispose()
        {
            foreach (WorldSession s in added)
            {
                Players.Remove(s);
                HeroicMovement.Forget(s);
                Health.Forget(s);
                Afk.Forget(s);
            }
            world.Dispose();
        }

        /// <summary>A player on <see cref="Zone"/> at (x, 0, 0), in the world when <paramref name="online"/>.</summary>
        private WorldSession At(float x, bool online = true, params CharacterItemModel[] items)
        {
            ulong guid = System.Threading.Interlocked.Increment(ref nextGuid);
            WorldSession s = world.Session(guid, "Obs" + (guid & 0xFFFF), items);
            var character = (CharacterModel)typeof(WorldSession).GetField("currentCharacter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
            character.PositionX = x;
            character.ZoneId = Zone;
            s.LoadingScreenDown = true;
            if (online)
                Players.Add(s);
            added.Add(s);
            return s;
        }

        private static void MoveTo(WorldSession s, float x)
            => typeof(WorldSession).GetField("lastPosition", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(s, (System.Numerics.Vector3?)new System.Numerics.Vector3(x, 0f, 0f));

        // ---- LightweightToFullPc: title and AFK -------------------------------------------------------------------

        /// <summary>The character blob of a LightweightToFullPc (after its bool and u32 length).</summary>
        private static byte[] Blob(PlayerUpdateLightweightToFullPc message)
        {
            byte[] data = Wire.Write(message);
            int length = BitConverter.ToInt32(data, 1);
            return data.AsSpan(5, length).ToArray();
        }

        /// <summary>
        /// The title sits where the client reads it (blob +0x118, three u32, applied by FUN_142ec5330) and the AFK flag
        /// at +0x174 (FUN_142ec2c10): 12 + 4 × 4 + 4 + 4 + 4 bytes after the title's start. Both are applied
        /// unconditionally when the character turns full, so zeros there wiped what the introduction had sent.
        /// </summary>
        [Fact]
        public void FullCharacter_CarriesTitleAndAfkWhereTheClientReadsThem()
        {
            byte[] plain = Blob(new PlayerUpdateLightweightToFullPc { Guid = 1, TransientId = 5 });
            byte[] marked = Blob(new PlayerUpdateLightweightToFullPc { Guid = 1, TransientId = 5, Title = (0x11223344, 2, 0x55667788), Afk = true });
            Assert.Equal(plain.Length, marked.Length);
            List<int> differ = Enumerable.Range(0, plain.Length).Where(i => plain[i] != marked[i]).ToList();
            int title = differ.First();
            Assert.Equal(0x11223344u, BitConverter.ToUInt32(marked, title));
            Assert.Equal(2u, BitConverter.ToUInt32(marked, title + 4));
            Assert.Equal(0x55667788u, BitConverter.ToUInt32(marked, title + 8));
            int afk = title + 12 + 16 + 4 + 4 + 4;
            Assert.Equal(1, marked[afk]);
            Assert.Equal(afk, differ.Last());
        }

        /// <summary>The AFK state the full character is built from (WorldSession.BuildLightweightToFullPc) follows /afk.</summary>
        [Fact]
        public void AfkState_IsWhatTheFullCharacterCarries()
        {
            WorldSession player = At(0f, online: false);
            Assert.False(Afk.IsAway(player));
            var away = (HashSet<WorldSession>)Private.Field(typeof(Afk), "away");
            lock (away)
                away.Add(player);
            Assert.True(Afk.IsAway(player));
        }

        // ---- Worn items used up ------------------------------------------------------------------------------------

        /// <summary>A worn item with a model that is used up is unset for the player and the landscape.</summary>
        [Fact]
        public void WornItemUsedUp_UnsetsTheSlotForEveryone()
        {
            world.Define(new ItemDefinitionManager.ItemDefinition { Id = 990001, EquipSlotId = 2, ModelFile = "Wield_Test.adr" });
            CharacterItemModel held = FakeWorld.Item(990001, 1, Inventory.EquippedContainer, 2);
            WorldSession player = At(0f, true, held, FakeWorld.Item(3253));
            WorldSession other = At(500f);
            FakeWorld.Outgoing(player);
            FakeWorld.Outgoing(other);
            Assert.True(player.TakeItem(990001, 1));
            Assert.Contains(FakeWorld.Outgoing(player), p => IsUnset(p.Opcode));
            Assert.Contains(FakeWorld.Outgoing(other), p => IsUnset(p.Opcode));
        }

        private static bool IsUnset(ZoneMessageOpcode opcode)
            => opcode is ZoneMessageOpcode.EquipmentUnsetCharacterEquipmentSlotWire;

        /// <summary>Using up a bag stack of the same item changes nothing others see.</summary>
        [Fact]
        public void BagStackUsedUp_SendsNoEquipmentChange()
        {
            world.Define(new ItemDefinitionManager.ItemDefinition { Id = 990002, EquipSlotId = 2, ModelFile = "Wield_Test.adr" });
            WorldSession player = At(0f, true, FakeWorld.Item(990002, 1, Inventory.EquippedContainer, 2), FakeWorld.Item(990002, 1));
            WorldSession other = At(5f);
            FakeWorld.Outgoing(other);
            Assert.True(player.TakeItem(990002, 1));
            Assert.False(FakeWorld.Outgoing(other).Any(p => IsUnset(p.Opcode)));
        }

        // ---- Heroic movement, grapple, swimming ----------------------------------------------------------------------

        /// <summary>The HeroicMovement packets (sub, subject guid, body) queued for a session.</summary>
        private static List<(byte Sub, ulong Subject, byte[] Body)> Heroic(WorldSession viewer)
            => FakeWorld.Outgoing(viewer).Where(p => p.Opcode == ZoneMessageOpcode.HeroicMovement)
                .Select(p => (p.Body[0], BitConverter.ToUInt64(p.Body, 1), p.Body.AsSpan(9).ToArray())).ToList();

        /// <summary>A HeroicMovement packet from <paramref name="sender"/>'s client: {u8 sub, u64 own guid, body}.</summary>
        private static void Client(WorldSession sender, byte sub, params byte[] body)
        {
            byte[] wire = new byte[9 + body.Length];
            wire[0] = sub;
            BitConverter.GetBytes(sender.PlayerGuid).CopyTo(wire, 1);
            body.CopyTo(wire, 9);
            HeroicMovement.Handle(sender, Wire.Read<HeroicMovementRequest>(wire).Message);
        }

        /// <summary>
        /// Heroic movement on reaches the viewers in range; a viewer who comes into range while it is on is shown it;
        /// off reaches every viewer shown it on, even out of range by then.
        /// </summary>
        [Fact]
        public void HeroicMovement_FollowsViewersInAndOutOfRange()
        {
            WorldSession runner = At(0f), near = At(50f), far = At(1000f);
            Client(runner, HeroicMovement.SetHeroicMovement, 1);
            Assert.Equal(new[] { (byte)1 }, Heroic(near).Single().Body);
            Assert.Empty(Heroic(far));

            MoveTo(far, 100f);                      // walks into range while the runner is still in heroic movement
            HeroicMovement.CatchUp(runner);
            var caught = Heroic(far).Single();
            Assert.Equal((HeroicMovement.SetHeroicMovement, runner.PlayerGuid), (caught.Sub, caught.Subject));
            Assert.Equal(1, caught.Body[0]);
            HeroicMovement.CatchUp(runner);         // shown once only
            Assert.Empty(Heroic(far));

            MoveTo(near, 5000f);                    // walks out of range, then the runner stops
            Client(runner, HeroicMovement.SetHeroicMovement, 0);
            Assert.Equal(0, Heroic(near).Single().Body[0]);
            Assert.Equal(0, Heroic(far).Single().Body[0]);
        }

        /// <summary>A rope's terminate reaches every viewer shown the hook, also one who has left the range since.</summary>
        [Fact]
        public void GrappleTerminate_ReachesEveryViewerShownTheHook()
        {
            WorldSession grappler = At(0f), viewer = At(150f), stranger = At(900f);
            Client(grappler, HeroicMovement.FireGrapplingHook, new byte[41]);
            Assert.Single(Heroic(viewer));
            Assert.Empty(Heroic(stranger));
            MoveTo(viewer, 400f);
            Client(grappler, HeroicMovement.TerminateGrappleHookEffects);
            Assert.Equal(HeroicMovement.TerminateGrappleHookEffects, Heroic(viewer).Single().Sub);
            Assert.Empty(Heroic(stranger));
        }

        private static byte[] SwimBody(byte flags, float depth)
        {
            byte[] body = new byte[5];
            body[0] = flags;
            BitConverter.GetBytes(depth).CopyTo(body, 1);
            return body;
        }

        /// <summary>
        /// A swimmer met by a viewer who comes into range is shown swimming; leaving the water reaches a viewer that holds
        /// the swim state even when it is out of range by then (it saw a swimmer on dry land before).
        /// </summary>
        [Fact]
        public void Swimming_CatchesUpAndEndsForViewersOutOfRange()
        {

            WorldSession swimmer = At(0f), viewer = At(500f);
            Client(swimmer, HeroicMovement.SwimmingUpdate, SwimBody(0, 0.5f));
            Assert.Empty(Heroic(viewer));

            MoveTo(viewer, 50f);
            HeroicMovement.CatchUp(swimmer);
            var swim = Heroic(viewer).Single();
            Assert.Equal(HeroicMovement.SwimmingUpdate, swim.Sub);
            Assert.Equal(0.5f, BitConverter.ToSingle(swim.Body, 1));

            MoveTo(viewer, 900f);
            var movers = (IDictionary)Private.Field(typeof(HeroicMovement), "movers");
            object mover = movers[swimmer]!;
            mover.GetType().GetField("LastSentAt")!.SetValue(mover, DateTime.MinValue);
            Client(swimmer, HeroicMovement.SwimmingUpdate, SwimBody(0, 0f));
            var dry = Heroic(viewer).Single();
            Assert.Equal(0f, BitConverter.ToSingle(dry.Body, 1));
        }

        // ---- Life and revive points -------------------------------------------------------------------------------

        /// <summary>The Life records (resource id 4) queued for a viewer about a subject: their current values.</summary>
        private static List<uint> LifeOf(WorldSession viewer, WorldSession subject)
            => FakeWorld.Outgoing(viewer)
                .Where(p => p.Opcode == ZoneMessageOpcode.ResourcesBase && p.Body.Length > 21
                    && BitConverter.ToUInt64(p.Body, 5) == subject.PlayerGuid && BitConverter.ToUInt32(p.Body, 13) == 4)
                .Select(p => BitConverter.ToUInt32(p.Body, 25)).ToList();

        /// <summary>A viewer who comes within the Life range gets the player's current Life once when its copy is stale.</summary>
        [Fact]
        public void Life_CatchesUpWhenAViewerComesNear()
        {
            WorldSession hurt = At(0f), viewer = At(200f);
            Health.IntroduceDead(viewer, hurt);
            Health.Damage(hurt, 300);
            Assert.Empty(LifeOf(viewer, hurt));     // 200 m away: no Life changes
            MoveTo(viewer, 10f);
            Health.CatchUpLife(new[] { hurt, viewer });
            Assert.True(LifeOf(viewer, hurt).SequenceEqual(new[] { 600u }));
            Health.CatchUpLife(new[] { hurt, viewer });
            Assert.Empty(LifeOf(viewer, hurt));     // current now: nothing more
        }

        /// <summary>The RevivePoints records (resource id 29) queued for a viewer about a subject: their values.</summary>
        private static List<uint> RevivePointsOf(WorldSession viewer, WorldSession subject)
            => FakeWorld.Outgoing(viewer)
                .Where(p => p.Opcode == ZoneMessageOpcode.ResourcesBase && p.Body.Length > 21
                    && BitConverter.ToUInt64(p.Body, 5) == subject.PlayerGuid && BitConverter.ToUInt32(p.Body, 13) == Health.RevivePointsId)
                .Select(p => BitConverter.ToUInt32(p.Body, 25)).ToList();

        /// <summary>
        /// A player whose death gave the landscape RevivePoints has them set back to 0 there when alive again, so the
        /// others' clients stop offering "Revive Player"; a revive with nothing to undo sends nothing.
        /// </summary>
        [Fact]
        public void Revive_ClearsTheRevivePointsTheDeathGaveTheLandscape()
        {
            WorldSession player = At(0f), other = At(700f);
            var revivable = (HashSet<WorldSession>)Private.Field(typeof(Health), "revivable");
            lock (Private.Field(typeof(Health), "sync"))
                revivable.Add(player);
            Health.Revive(player);
            Assert.True(RevivePointsOf(other, player).SequenceEqual(new[] { 0u }));
            Health.Revive(player);
            Assert.Empty(RevivePointsOf(other, player));
        }
    }
}
