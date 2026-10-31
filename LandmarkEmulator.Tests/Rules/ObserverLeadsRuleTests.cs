using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Internal.Commerce;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Game.Social;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// Phase E part 2 (2026-10-26, trace/0695/phaseE2-social.md): the observer-sync leads OL-1 (guild tag), OL-2 (a dead
    /// player's body), OL-3 (no buffed stats in the full character), OL-4 (template previews keep their setup) and the
    /// Player Studio overrule rule (L185).
    /// </summary>
    public class ObserverLeadsRuleTests : IDisposable
    {
        private const uint Zone = 932;   // a landscape no other test uses
        private static ulong nextGuid = 0x7E00_0000_0000_0000;
        private readonly FakeWorld world = new();
        private readonly List<WorldSession> added = new();
        private readonly IGuildCache previousGuilds = SocialServices.GuildCache;

        private static IList Players => (IList)Private.Field(typeof(PlayerManager), "players");

        public void Dispose()
        {
            foreach (WorldSession s in added)
            {
                Players.Remove(s);
                DeadBodies.Forget(s);
                WorldEditing.Forget(s);
                Health.Forget(s);
            }
            SetGuildCache(previousGuilds);
            world.Dispose();
        }

        private WorldSession At(float x, bool online = true)
        {
            ulong guid = System.Threading.Interlocked.Increment(ref nextGuid);
            WorldSession s = world.Session(guid, "Lead" + (guid & 0xFFFF));
            var character = (CharacterModel)typeof(WorldSession).GetField("currentCharacter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
            character.PositionX = x;
            character.ZoneId = Zone;
            s.LoadingScreenDown = true;
            if (online)
                Players.Add(s);
            added.Add(s);
            return s;
        }

        private static void SetGuildCache(IGuildCache cache)
            => typeof(SocialServices).GetProperty(nameof(SocialServices.GuildCache))!.SetValue(null, cache);

        /// <summary>A guild cache with one guild for the given members.</summary>
        private sealed class OneGuild : IGuildCache
        {
            public readonly HashSet<ulong> Members = new();
            public const ulong Id = 77;
            public string Name = "Builders of Note";
            public ulong GuildIdOf(ulong character) => Members.Contains(character) ? Id : 0;
            public ulong GuildIdByName(string name) => name == Name ? Id : 0;
            public string GuildName(ulong guildId) => guildId == Id ? Name : null;
            public List<WorldSession> MembersOf(WorldSession session) => new();
            public List<WorldSession> OfficersOf(WorldSession session) => new();
            public bool IsMuted(WorldSession session, ushort channel) => false;
        }

        // ---- OL-1 guild tag --------------------------------------------------------------------------------------

        /// <summary>0xDF PlayerUpdatePacketGuildTag after the opcode: u64 character, string (reader FUN_142c4c0a0).</summary>
        [Fact]
        public void GuildTagPacket_IsGuidThenString()
        {
            byte[] body = GuildTags.Body(0x1122334455667788, "Abc");
            Assert.Equal(8 + 4 + 3, body.Length);
            Assert.Equal(0x1122334455667788ul, BitConverter.ToUInt64(body, 0));
            Assert.Equal(3u, BitConverter.ToUInt32(body, 8));
            Assert.Equal("Abc", Encoding.UTF8.GetString(body, 12, 3));
        }

        /// <summary>The full character's third string (+0xf8, FUN_142ec2610) carries the tag; the first two stay empty.</summary>
        [Fact]
        public void FullCharacter_CarriesTheGuildTagAsItsThirdString()
        {
            byte[] plain = Blob(new PlayerUpdateLightweightToFullPc { Guid = 1, TransientId = 5 });
            byte[] tagged = Blob(new PlayerUpdateLightweightToFullPc { Guid = 1, TransientId = 5, GuildTag = "Abc" });
            Assert.Equal(plain.Length + 3, tagged.Length);
            int at = Enumerable.Range(0, plain.Length).First(i => plain[i] != tagged[i]);
            // The third string's length: the two empty strings (two zero u32) end right before it.
            Assert.Equal(3u, BitConverter.ToUInt32(tagged, at));
            Assert.Equal("Abc", Encoding.UTF8.GetString(tagged, at + 4, 3));
            Assert.Equal(0u, BitConverter.ToUInt32(tagged, at - 4));
            Assert.Equal(0u, BitConverter.ToUInt32(tagged, at - 8));
        }

        private static byte[] Blob(PlayerUpdateLightweightToFullPc message)
        {
            byte[] data = Wire.Write(message);
            int length = BitConverter.ToInt32(data, 1);
            return data.AsSpan(5, length).ToArray();
        }

        /// <summary>A guild change sends the new tag to everyone on the player's landscape, the player included.</summary>
        [Fact]
        public void GuildChange_SendsTheTagToTheLandscapeAndThePlayer()
        {
            var guilds = new OneGuild();
            SetGuildCache(guilds);
            WorldSession member = At(0f), near = At(30f), far = At(1500f);
            guilds.Members.Add(member.PlayerGuid);
            Assert.Equal("Builders of Note", GuildTags.TagOf(member.PlayerGuid));
            Assert.Equal("", GuildTags.TagOf(near.PlayerGuid));
            foreach (WorldSession s in new[] { member, near, far })
                FakeWorld.Outgoing(s);
            GuildTags.Changed(member.PlayerGuid);
            foreach (WorldSession s in new[] { member, near, far })
            {
                var tags = FakeWorld.Outgoing(s).Where(p => p.Opcode == ZoneMessageOpcode.UpdateGuildTag).ToList();
                Assert.Single(tags);
                Assert.Equal(GuildTags.Body(member.PlayerGuid, "Builders of Note"), tags[0].Body);
            }
            guilds.Members.Clear();
            GuildTags.Changed(member.PlayerGuid);
            Assert.Equal(GuildTags.Body(member.PlayerGuid, ""),
                FakeWorld.Outgoing(near).Single(p => p.Opcode == ZoneMessageOpcode.UpdateGuildTag).Body);
        }

        // ---- OL-2 dead bodies ------------------------------------------------------------------------------------

        /// <summary>UpdateCharacterStateDelta bodies queued for a viewer about a subject: (set, clear).</summary>
        private static List<(ulong Set, ulong Clear)> Deltas(WorldSession viewer, WorldSession subject)
            => FakeWorld.Outgoing(viewer)
                .Where(p => p.Opcode == ZoneMessageOpcode.PlayerUpdateUpdateCharacterStateDeltaWire && BitConverter.ToUInt64(p.Body, 0) == subject.PlayerGuid)
                .Select(p => (BitConverter.ToUInt64(p.Body, 16), BitConverter.ToUInt64(p.Body, 24))).ToList();

        /// <summary>
        /// A death sets entity state bit 7 on the others' copies (the client's State_Dead), never on the dead player's own;
        /// a newcomer is told too; alive again, everyone told gets the bit cleared.
        /// </summary>
        [Fact]
        public void Death_LaysTheBodyDownOnOthers_AndRevivalLiftsIt()
        {
            WorldSession dead = At(0f), viewer = At(500f), later = At(40f, online: false);
            FakeWorld.Outgoing(dead);
            FakeWorld.Outgoing(viewer);
            DeadBodies.Died(dead);
            Assert.True(Deltas(viewer, dead).SequenceEqual(new[] { (DeadBodies.KnockedOutBit, 0ul) }));
            Assert.Empty(Deltas(dead, dead));
            DeadBodies.Introduce(later, dead);
            Assert.True(Deltas(later, dead).SequenceEqual(new[] { (DeadBodies.KnockedOutBit, 0ul) }));
            Assert.Equal(2, DeadBodies.ToldCount(dead.PlayerGuid));
            DeadBodies.Revived(dead);
            Assert.True(Deltas(viewer, dead).SequenceEqual(new[] { (0ul, DeadBodies.KnockedOutBit) }));
            Assert.True(Deltas(later, dead).SequenceEqual(new[] { (0ul, DeadBodies.KnockedOutBit) }));
            DeadBodies.Revived(dead);   // nothing left to undo
            Assert.Empty(Deltas(viewer, dead));
        }

        /// <summary>DEAD_BODIES_VISIBLE=false sends nothing.</summary>
        [Fact]
        public void DeadBodies_Off_SendsNothing()
        {
            world.Config.DeadBodiesVisible = false;
            WorldSession dead = At(0f), viewer = At(20f);
            FakeWorld.Outgoing(viewer);
            DeadBodies.Died(dead);
            DeadBodies.Introduce(viewer, dead);
            DeadBodies.Revived(dead);
            Assert.Empty(Deltas(viewer, dead));
        }

        // ---- OL-3 the full character's stats ---------------------------------------------------------------------

        /// <summary>Others get the stats without temporary buffs: a light-radius buff stays off the snapshot.</summary>
        [Fact]
        public void FullCharacterStats_LeaveTemporaryBuffsOut()
        {
            WorldSession player = At(0f);
            Buffs.AddStat(player, "Test Moonlight", 4, 25f, 0f, TimeSpan.FromMinutes(5), announce: false);
            Assert.Equal(25f, Abilities.CalculatedStats(player).Single(s => s.StatId == 4).Value);
            Assert.Equal(0f, Abilities.CalculatedStatsForOthers(player).Single(s => s.StatId == 4).Value);
            Assert.Equal(Abilities.CalculatedStats(player).Count(), Abilities.CalculatedStatsForOthers(player).Count());
        }

        // ---- OL-4 template previews ------------------------------------------------------------------------------

        /// <summary>A 0x10C packet: u16 0x010C, u32 sub, u32 id, the body, ending with the builder's guid.</summary>
        private static byte[] Cursor(uint sub, ulong guid, int bodyBefore)
        {
            var bytes = new List<byte> { 0x0C, 0x01 };
            bytes.AddRange(BitConverter.GetBytes(sub));
            bytes.AddRange(BitConverter.GetBytes(7u));
            switch (sub)
            {
                case 5:      // PreviewSetup: template records, the guid found by value
                    bytes.AddRange(new byte[bodyBefore]);
                    break;
                case 6:      // PreviewUpdate {vec4, 4 × vec4, bool, string, u64}
                    bytes.AddRange(new byte[16 + 64 + 1]);
                    bytes.AddRange(BitConverter.GetBytes(0u));
                    break;
                case 1:      // Box {2 × vec4, string, u64}
                    bytes.AddRange(new byte[32]);
                    bytes.AddRange(BitConverter.GetBytes(0u));
                    break;
            }
            bytes.AddRange(BitConverter.GetBytes(guid));
            if (sub == 5)
                bytes.AddRange(new byte[] { 1, 2, 3 });   // the byte array after the guid
            return bytes.ToArray();
        }

        private static List<uint> CursorSubs(WorldSession viewer)
        {
            var raw = (ConcurrentQueue<byte[]>)typeof(WorldSession).GetField("rawPackets", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(viewer);
            var subs = new List<uint>();
            while (raw.TryDequeue(out byte[] p))
                if (p.Length > 6 && p[0] == 0x0C && p[1] == 0x01)
                    subs.Add(BitConverter.ToUInt32(p, 2));
            return subs;
        }

        private static void ResetPacing(WorldSession builder)
        {
            var editors = (IDictionary)Private.Field(typeof(WorldEditing), "editors");
            object editor = editors[builder];
            editor.GetType().GetField("LastSentAt")!.SetValue(editor, DateTime.MinValue);
            editor.GetType().GetField("LastViewCheck")!.SetValue(editor, DateTime.MinValue);
        }

        /// <summary>
        /// An update that replaced its setup within the send interval still has the setup sent first; a viewer who comes
        /// near while the preview is dragged gets the setup before the update; another cursor kind ends the preview.
        /// </summary>
        [Fact]
        public void TemplatePreview_SetupGoesBeforeItsUpdates()
        {
            WorldSession builder = At(0f), viewer = At(50f), newcomer = At(1000f);
            WorldEditing.Received(builder, Cursor(5, builder.PlayerGuid, 40));
            WorldEditing.Received(builder, Cursor(6, builder.PlayerGuid, 0));
            WorldEditing.Update();
            Assert.True(CursorSubs(viewer).SequenceEqual(new[] { 5u, 6u }));
            Assert.Empty(CursorSubs(newcomer));

            typeof(WorldSession).GetField("lastPosition", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(newcomer, (System.Numerics.Vector3?)new System.Numerics.Vector3(60f, 0f, 0f));
            ResetPacing(builder);
            WorldEditing.Update();
            Assert.True(CursorSubs(newcomer).SequenceEqual(new[] { 5u, 6u }));
            Assert.Empty(CursorSubs(viewer));

            // A box cursor ends the preview: a later newcomer gets only the box.
            WorldSession third = At(1000f);
            WorldEditing.Received(builder, Cursor(1, builder.PlayerGuid, 0));
            ResetPacing(builder);
            WorldEditing.Update();
            typeof(WorldSession).GetField("lastPosition", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(third, (System.Numerics.Vector3?)new System.Numerics.Vector3(70f, 0f, 0f));
            ResetPacing(builder);
            WorldEditing.Update();
            Assert.True(CursorSubs(third).SequenceEqual(new[] { 1u }));
        }

        // ---- L185 overruled reviewers ----------------------------------------------------------------------------

        /// <summary>A rejection counts as overruled when the product is listed below the peers' approval threshold.</summary>
        [Fact]
        public void OverruledRejections_HoldTheReviewerBack()
        {
            Assert.True(StudioRules.Overruled(verdict: 2, listed: true, approvals: 0, approvalsNeeded: 3));
            Assert.False(StudioRules.Overruled(verdict: StudioRules.VerdictApprove, listed: true, approvals: 0, approvalsNeeded: 3));
            Assert.False(StudioRules.Overruled(verdict: 2, listed: true, approvals: 3, approvalsNeeded: 3));   // the peers listed it
            Assert.False(StudioRules.Overruled(verdict: 2, listed: false, approvals: 0, approvalsNeeded: 3));
            Assert.True(StudioRules.MayStillReview(0, 1));
            Assert.False(StudioRules.MayStillReview(1, 1));
            Assert.True(StudioRules.MayStillReview(1, 2));
            Assert.True(StudioRules.MayStillReview(50, 0));      // 0 = never held back
        }
    }
}
