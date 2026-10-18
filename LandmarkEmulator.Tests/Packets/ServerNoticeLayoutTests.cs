using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using System;
using System.Collections.Generic;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using Xunit;

namespace LandmarkEmulator.Tests.Packets
{
    /// <summary>
    /// Byte layouts of the server's notices to the player (OPEN-LEADS L171-L177), built from the 0.695 client's readers
    /// (comments in Game/Broadcasts.cs, Kicks.cs, Guilds.cs ...), not from the writers.
    /// </summary>
    public class ServerNoticeLayoutTests
    {
        /// <summary>BaseBroadcastPacket 0x4F is the u16 base on the wire (4F 00), no sub.</summary>
        [Fact]
        public void Broadcast_OpcodeIs4F00()
            => Assert.Equal(new byte[] { 0x4F, 0x00 }, WorldSession.GetOpcodeBytes(ZoneMessageOpcode.BroadcastBase));

        /// <summary>
        /// Reader FUN_142c17410: i16 kind, name block {u32, u32, u32, string first, string last} (FUN_142c25b90), string
        /// message (StringFixed&lt;256&gt;), u32 text id.
        /// </summary>
        [Fact]
        public void Broadcast_KindNameMessageTextId()
            => Assert.Equal(new Layout().I16(1).U32(0).U32(0).U32(0).Str("").Str("").Str("Restart soon").U32(0).ToArray(),
                Broadcasts.Build("Restart soon"));

        [Fact]
        public void Broadcast_FromAPlayer_PutsTheNameInTheFirstNameField()
            => Assert.Equal(new Layout().I16(0).U32(0).U32(0).U32(0).Str("Ann").Str("").Str("hi").U32(7).ToArray(),
                Broadcasts.Build("hi", Broadcasts.Plain, "Ann", 7));

        /// <summary>The message fits the client's StringFixed&lt;256&gt; (255 bytes), cut on a character boundary.</summary>
        [Fact]
        public void Broadcast_LongMessage_IsCutTo255Bytes()
        {
            string text = new string('é', 200);   // 400 UTF-8 bytes
            string fitted = Broadcasts.Fit(text, Broadcasts.MaxLength);
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(fitted) <= 255);
            Assert.Equal(127, fitted.Length);
        }

        /// <summary>PacketClientPendingKickFromServer 0x65 (wire 65 00), reader FUN_142c4a0d0: string message, u32 seconds.</summary>
        [Fact]
        public void PendingKick_MessageThenSeconds()
        {
            Assert.Equal(new byte[] { 0x65, 0x00 }, WorldSession.GetOpcodeBytes(ZoneMessageOpcode.ClientPendingKickFromServer));
            Assert.Equal(new Layout().Str("Be nice").U32(30).ToArray(), Kicks.PendingBody("Be nice", 30));
        }

        /// <summary>PacketClientKickedFromServer 0x53 (wire 53 00), reader FUN_142bb0360: string (StringFixed&lt;128&gt;).</summary>
        [Fact]
        public void Kicked_MessageOnly_CutTo127Bytes()
        {
            Assert.Equal(new byte[] { 0x53, 0x00 }, WorldSession.GetOpcodeBytes(ZoneMessageOpcode.ClientKickedFromServer));
            Assert.Equal(new Layout().Str("Bye").ToArray(), Kicks.KickedBody("Bye"));
            Assert.Equal(4 + 127, Kicks.KickedBody(new string('x', 300)).Length);
        }

        /// <summary>GuildDataFull 0x83/0x12 with no guild (reader FUN_142ee4820): u16 sub, u32 0 guilds, u32 0 names.</summary>
        [Fact]
        public void GuildDataFull_Empty()
            => Assert.Equal(new Layout().U16(0x12).U32(0).U32(0).ToArray(), Guilds.EmptyDataFull());

        /// <summary>GuildRemoveLogUpdate 0x83/0x1B (reader FUN_142ee5580): u16 sub, u32 n × u32 row id, u64 guild.</summary>
        [Fact]
        public void GuildRemoveLog_IdsThenGuild()
            => Assert.Equal(new Layout().U16(0x1B).U32(2).U32(4).U32(5).U64(0x77).ToArray(), Guilds.BuildRemoveLog(0x77, new uint[] { 4, 5 }));

        /// <summary>
        /// GuildInvitations 0x83/0x14 (reader FUN_142ee5040; invitation FUN_142ee4ee0): u16 sub, u32 n, invitation {u64 id,
        /// u64 guild, string guild name, string inviter id, string invitee id, u64 time, string, u32 type 1, u32}, names
        /// u32 n × {u64 guid, string name}.
        /// </summary>
        [Fact]
        public void GuildInvitations_OneInvite()
        {
            var at = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
            long unix = new DateTimeOffset(at).ToUnixTimeSeconds();
            var open = new List<(ulong GuildId, string GuildName, (ulong Id, ulong Guild, string From, ulong FromGuid, DateTime At) Invite)>
                { (9, "Builders", (3ul, 9ul, "Ann", 100ul, at)) };
            byte[] body = Guilds.BuildInvitations(open, 200, "Bob");
            byte[] expected = new Layout().U16(0x14).U32(1)
                .U64(3).U64(9).Str("Builders").Str("100").Str("200").U64((ulong)unix).Str("200").U32(1).U32(0)
                .U32(2).U64(100).Str("Ann").U64(200).Str("Bob").ToArray();
            Assert.Equal(expected, body);
        }

        [Fact]
        public void GuildInvitations_None_IsCountsOnly()
            => Assert.Equal(new Layout().U16(0x14).U32(0).U32(0).ToArray(),
                Guilds.BuildInvitations(new List<(ulong, string, (ulong, ulong, string, ulong, DateTime))>(), 200, "Bob"));

        /// <summary>
        /// PacketTargetClientNotOnline 0x1A (wire 1A 00), reader FUN_142c4af20 (consumed exactly): u64, string name
        /// (StringFixed&lt;32&gt;, cut to 31 bytes).
        /// </summary>
        [Fact]
        public void TargetNotOnline_GuidThenName()
        {
            Assert.Equal(new byte[] { 0x1A, 0x00 }, WorldSession.GetOpcodeBytes(ZoneMessageOpcode.TargetClientNotOnline));
            Assert.Equal(new Layout().U64(0x1234).Str("Bob").ToArray(), TargetNotOnline.Body(0x1234, "Bob"));
            Assert.Equal(8 + 4 + 31, TargetNotOnline.Body(1, new string('n', 40)).Length);
        }

        /// <summary>
        /// InstancedGamePlayerRemovalPacket 0x10D/0x14 (wire 0D 01; 0.695 case 0x12, reader FUN_1434dbfb0, consumed exactly):
        /// u32 sub 0x14, u64 game = 12 bytes after the opcode.
        /// </summary>
        [Fact]
        public void InstancedGamePlayerRemoval_SubThenGame()
        {
            Assert.Equal(new byte[] { 0x0D, 0x01 }, WorldSession.GetOpcodeBytes(ZoneMessageOpcode.InstancedGameBase));
            Assert.Equal(new Layout().U32(0x14).U64(0xABC).ToArray(), InstancedGames.RemovalBody(0xABC));
        }

        /// <summary>
        /// RecipePacketZoneCraftingWindow 0x42/0x24 (reader FUN_142f300e0): u32 sub 0x24, u32 (+0x18), bool (+0x1c; false
        /// closes the window).
        /// </summary>
        [Fact]
        public void ZoneCraftingWindow_SubStationBool()
        {
            Assert.Equal(new byte[] { 0x42, 0x00 }, WorldSession.GetOpcodeBytes(ZoneMessageOpcode.RecipeBase));
            Assert.Equal(new Layout().U32(0x24).U32(12).Bool(false).ToArray(), Crafting.CraftingWindowBody(12, false));
        }
    }
}
