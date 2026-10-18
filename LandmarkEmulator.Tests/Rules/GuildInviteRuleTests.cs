using LandmarkEmulator.Internal.Social;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// GuildInvitePacket (GuildBase 0x83 sub 4; Internal/Social/GuildInvites.cs): the two bodies the client's Guild.Invite
    /// (FUN_1435b66e0) writes — the Guild window's {guild, 0, typed name} and the right-click menu's {current guild,
    /// target guid, ""} — and the lookup both servers share: by guid when set, else by name (bug 2026-10-18: the menu's
    /// invite was looked up by its empty name and answered "Guild invite failed.  Player not found.").
    /// </summary>
    public class GuildInviteRuleTests
    {
        private sealed record Character(ulong Guid, string Name);

        private static readonly Character[] Characters =
        {
            new(0x10, "Alice"), new(0x20, "Robin"), new(0x30, "Bob"),
        };

        private static Character ByGuid(ulong guid) => Characters.FirstOrDefault(c => c.Guid == guid);

        private static Character ByName(string name)
            => Characters.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>The body as the client's GuildInvitePacket serialiser lays it out: u64 guild, u64 invitee, u32 length + name.</summary>
        private static byte[] ClientBody(ulong guild, ulong invitee, string name)
        {
            var bytes = new List<byte>();
            bytes.AddRange(BitConverter.GetBytes(guild));
            bytes.AddRange(BitConverter.GetBytes(invitee));
            byte[] text = System.Text.Encoding.UTF8.GetBytes(name);
            bytes.AddRange(BitConverter.GetBytes((uint)text.Length));
            bytes.AddRange(text);
            return bytes.ToArray();
        }

        [Fact]
        public void TheRadialMenuBody_CarriesTheGuid_AndNoName()
        {
            byte[] body = ClientBody(7, 0x30, "");
            Assert.Equal(20, body.Length);
            Assert.True(GuildInvites.TryRead(body, out GuildInvites.Request request));
            Assert.Equal(7ul, request.GuildId);
            Assert.Equal(0x30ul, request.InviteeGuid);
            Assert.Equal("", request.Name);
            Assert.True(request.ByGuid);
            Assert.Equal(body, GuildInvites.Write(7, 0x30, ""));
        }

        [Fact]
        public void TheGuildWindowBody_CarriesTheName_AndGuidZero()
        {
            byte[] body = ClientBody(7, 0, "Bob");
            Assert.True(GuildInvites.TryRead(body, out GuildInvites.Request request));
            Assert.Equal(7ul, request.GuildId);
            Assert.Equal(0ul, request.InviteeGuid);
            Assert.Equal("Bob", request.Name);
            Assert.False(request.ByGuid);
            Assert.Equal(body, GuildInvites.Write(7, 0, "Bob"));
        }

        [Fact]
        public void TheTypedName_IsTrimmed()
        {
            Assert.True(GuildInvites.TryRead(ClientBody(7, 0, "  Bob "), out GuildInvites.Request request));
            Assert.Equal("Bob", request.Name);
        }

        [Fact]
        public void AShortBody_IsUnreadable_ButAMissingName_IsEmpty()
        {
            Assert.False(GuildInvites.TryRead(null, out _));
            Assert.False(GuildInvites.TryRead(new byte[12], out _));
            Assert.False(GuildInvites.TryRead(ClientBody(7, 0, "Bob")[..22], out _));
            Assert.True(GuildInvites.TryRead(ClientBody(7, 0x30, "")[..16], out GuildInvites.Request request));
            Assert.Equal(0x30ul, request.InviteeGuid);
            Assert.Equal("", request.Name);
        }

        [Fact]
        public void TheGuid_FindsTheInvitee_WithAnEmptyName()
        {
            var request = new GuildInvites.Request(7, 0x30, "");
            Assert.Equal("Bob", GuildInvites.Resolve(request, ByGuid, ByName)?.Name);
        }

        [Fact]
        public void TheGuid_WinsOverTheName()
        {
            var request = new GuildInvites.Request(7, 0x30, "Alice");
            Assert.Equal("Bob", GuildInvites.Resolve(request, ByGuid, ByName)?.Name);
        }

        [Fact]
        public void TheName_FindsTheInvitee_WhenTheGuidIsZero()
        {
            var request = new GuildInvites.Request(7, 0, "bob");
            Assert.Equal(0x30ul, GuildInvites.Resolve(request, ByGuid, ByName)?.Guid);
        }

        [Fact]
        public void NothingIsLookedUp_ForNoGuidAndNoName()
        {
            bool asked = false;
            var request = new GuildInvites.Request(7, 0, "");
            Assert.Null(GuildInvites.Resolve(request, g => { asked = true; return ByGuid(g); }, n => { asked = true; return ByName(n); }));
            Assert.False(asked);
            Assert.Null(GuildInvites.Resolve<Character>(null, ByGuid, ByName));
        }

        [Fact]
        public void AnUnknownGuid_IsNotFound_AndDoesNotFallBackToTheName()
        {
            var request = new GuildInvites.Request(7, 0x99, "Alice");
            Assert.Null(GuildInvites.Resolve(request, ByGuid, ByName));
        }

        [Fact]
        public void TheNotification_NamesTheKnownCharacter_ElseTheTypedName()
        {
            Assert.Equal("Bob", GuildInvites.ShownName(new GuildInvites.Request(7, 0x30, ""), "Bob"));
            Assert.Equal("Bobby", GuildInvites.ShownName(new GuildInvites.Request(7, 0, "Bobby"), null));
            Assert.Equal("", GuildInvites.ShownName(new GuildInvites.Request(7, 0x99, ""), null));
            Assert.Equal("", GuildInvites.ShownName(null, null));
        }

        [Fact]
        public void TheLogText_SaysHowTheInviteeWasNamed()
        {
            Assert.Equal("guid 0x30", new GuildInvites.Request(7, 0x30, "").Describe());
            Assert.Equal("name 'Bob'", new GuildInvites.Request(7, 0, "Bob").Describe());
        }
    }
}
