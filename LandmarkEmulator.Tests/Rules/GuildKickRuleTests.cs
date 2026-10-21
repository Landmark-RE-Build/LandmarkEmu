using LandmarkEmulator.Internal.Social;
using System;
using System.Collections.Generic;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// GuildKickPacket (GuildBase 0x83 sub 0x0C; Internal/Social/GuildKicks.cs): the two bodies the 0.695 client writes —
    /// the roster menu's Guild.Kick (FUN_1435b7ce0) {guild, member guid, ""} and /guildkick or /guildremove Name
    /// (FUN_1433abf90) {guild, 0, name} — and the member lookup both servers share (docs/request-variants.md).
    /// </summary>
    public class GuildKickRuleTests
    {
        private static readonly Dictionary<ulong, string> Roster = new()
        {
            [0x10] = "Alice", [0x20] = "Robin", [0x30] = "Bob",
        };

        /// <summary>The body as the client's GuildKickPacket serialiser lays it out: u64 guild, u64 member, u32 length + name.</summary>
        private static byte[] ClientBody(ulong guild, ulong member, string name)
        {
            var bytes = new List<byte>();
            bytes.AddRange(BitConverter.GetBytes(guild));
            bytes.AddRange(BitConverter.GetBytes(member));
            byte[] text = System.Text.Encoding.UTF8.GetBytes(name);
            bytes.AddRange(BitConverter.GetBytes((uint)text.Length));
            bytes.AddRange(text);
            return bytes.ToArray();
        }

        [Fact]
        public void TheRosterMenuBody_NamesTheMemberByGuid()
        {
            byte[] body = ClientBody(7, 0x30, "");
            Assert.True(GuildKicks.TryRead(body, out GuildKicks.Request request));
            Assert.True(request.ByGuid);
            Assert.Equal(0x30ul, GuildKicks.Resolve(request, Roster));
            Assert.Equal(body, GuildKicks.Write(7, 0x30, ""));
        }

        [Fact]
        public void TheSlashCommandBody_NamesTheMemberByName()
        {
            byte[] body = ClientBody(7, 0, " bob ");
            Assert.True(GuildKicks.TryRead(body, out GuildKicks.Request request));
            Assert.False(request.ByGuid);
            Assert.Equal("bob", request.Name);
            Assert.Equal(0x30ul, GuildKicks.Resolve(request, Roster));
        }

        [Fact]
        public void TheGuid_WinsOverAName_AndAnUnknownGuidFindsNobody()
        {
            Assert.Equal(0x30ul, GuildKicks.Resolve(new GuildKicks.Request(7, 0x30, "Alice"), Roster));
            Assert.Equal(0ul, GuildKicks.Resolve(new GuildKicks.Request(7, 0x99, "Alice"), Roster));
        }

        [Fact]
        public void ANameOutsideTheGuild_OrNoNameAtAll_FindsNobody()
        {
            Assert.Equal(0ul, GuildKicks.Resolve(new GuildKicks.Request(7, 0, "Mallory"), Roster));
            Assert.Equal(0ul, GuildKicks.Resolve(new GuildKicks.Request(7, 0, ""), Roster));
            Assert.Equal(0ul, GuildKicks.Resolve(null, Roster));
        }

        [Fact]
        public void AShortBody_IsUnreadable_ButAMissingName_IsEmpty()
        {
            Assert.False(GuildKicks.TryRead(null, out _));
            Assert.False(GuildKicks.TryRead(new byte[12], out _));
            Assert.True(GuildKicks.TryRead(ClientBody(7, 0x30, "")[..16], out GuildKicks.Request request));
            Assert.Equal("", request.Name);
        }

        [Fact]
        public void TheLogText_SaysHowTheMemberWasNamed()
        {
            Assert.Equal("guid 0x30", new GuildKicks.Request(7, 0x30, "").Describe());
            Assert.Equal("name 'Bob'", new GuildKicks.Request(7, 0, "Bob").Describe());
        }
    }
}
