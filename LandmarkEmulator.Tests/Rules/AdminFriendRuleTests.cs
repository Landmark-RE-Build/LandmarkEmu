using LandmarkEmulator.Internal;
using LandmarkEmulator.Internal.Social;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Game.Social;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// Admin /friend forceadd / remove (AdminCommandPacketForceAddFriendRequest 0x1E/0x4CF, ...ForceRemove... 0x4D0;
    /// Internal/Social/AdminFriends.cs): the client body, the rules both carriers share (the world's own friends code and
    /// the social service), the routing to the social service, and that the outcome told to the admin is the real one —
    /// never "removed" when nothing was (harness 2026-10-18: the world removed from its own character_friend table while
    /// the friendship lived in the social service, and still logged "force-removed").
    /// </summary>
    public class AdminFriendRuleTests : IDisposable
    {
        private readonly FakeWorld world = new();

        public void Dispose() => world.Dispose();

        /// <summary>The body the client writes after the u16 sub (FUN_143389ce0): two identities {u32 × 3, string, string}.</summary>
        private static byte[] ClientBody(string target, string source)
        {
            var bytes = new List<byte>();
            var w = new GamePacketWriter(bytes);
            foreach (string name in new[] { target, source })
            {
                w.Write(0u); w.Write(0u); w.Write(0u);
                w.Write(name);
                w.Write("");
            }
            return bytes.ToArray();
        }

        /// <summary>What the session was sent (taken out of its queue once): opcodes, and the bodies as text.</summary>
        private static (List<ZoneMessageOpcode> Opcodes, List<string> Texts) Sent(WorldSession session)
        {
            var sent = FakeWorld.Outgoing(session);
            return (sent.Select(p => p.Opcode).ToList(), sent.Select(p => Encoding.UTF8.GetString(p.Body)).ToList());
        }

        [Fact]
        public void TheClientBody_IsRead_TargetThenSource()
        {
            Assert.True(AdminFriends.TryRead(ClientBody("Bob", "Alice"), out AdminFriends.Request request));
            Assert.Equal("Bob", request.Target);
            Assert.Equal("Alice", request.Source);
            Assert.Equal(ClientBody("Bob", "Alice"), AdminFriends.Write("Bob", "Alice"));
        }

        [Fact]
        public void AShortBody_IsUnreadable()
        {
            byte[] body = ClientBody("Bob", "Alice");
            Assert.False(AdminFriends.TryRead(body[..10], out _));
            Assert.False(AdminFriends.TryRead(null, out _));
        }

        [Fact]
        public void ForceAdd_Rules()
        {
            var none = Array.Empty<ulong>();
            Assert.Equal(AdminFriendOutcome.Added, AdminFriends.DecideAdd(1, 2, none));
            Assert.Equal(AdminFriendOutcome.SourceUnknown, AdminFriends.DecideAdd(null, 2, none));
            Assert.Equal(AdminFriendOutcome.TargetUnknown, AdminFriends.DecideAdd(1, null, none));
            Assert.Equal(AdminFriendOutcome.Self, AdminFriends.DecideAdd(1, 1, none));
            Assert.Equal(AdminFriendOutcome.AlreadyListed, AdminFriends.DecideAdd(1, 2, new ulong[] { 2 }));
            var full = Enumerable.Range(100, AdminFriends.MaxFriends).Select(i => (ulong)i).ToList();
            Assert.Equal(AdminFriendOutcome.ListFull, AdminFriends.DecideAdd(1, 2, full));
        }

        [Fact]
        public void ForceRemove_MatchesTheNameCaseInsensitively_AndSaysWhenItIsNotListed()
        {
            var friends = new[] { (Guid: 2ul, Name: "Bob"), (Guid: 3ul, Name: "Carol") };
            Assert.Equal(AdminFriendOutcome.Removed, AdminFriends.DecideRemove(1, "bob", friends, f => f.Name, out var found));
            Assert.Equal(2ul, found.Guid);
            Assert.Equal(AdminFriendOutcome.NotOnList, AdminFriends.DecideRemove(1, "Dave", friends, f => f.Name, out _));
            Assert.Equal(AdminFriendOutcome.SourceUnknown, AdminFriends.DecideRemove(null, "Bob", friends, f => f.Name, out _));
        }

        [Fact]
        public void TheAdminsLine_SaysWhatHappened()
        {
            Assert.Equal("Removed Bob from Alice's friends list.",
                AdminFriends.Describe(AdminFriends.ForceRemoveSub, AdminFriendOutcome.Removed, "Bob", "Alice", false));
            Assert.Equal("Added Bob to your friends list.",
                AdminFriends.Describe(AdminFriends.ForceAddSub, AdminFriendOutcome.Added, "Bob", "Me", true));
            foreach (AdminFriendOutcome failed in Enum.GetValues<AdminFriendOutcome>()
                .Where(o => o is not AdminFriendOutcome.Added and not AdminFriendOutcome.Removed))
            {
                string line = AdminFriends.Describe(AdminFriends.ForceRemoveSub, failed, "Bob", "Alice", false);
                Assert.False(line.StartsWith("Removed") || line.StartsWith("Added"), $"{failed}: \"{line}\"");
            }
        }

        [Fact]
        public void TheSocialService_TakesBothCommands_AndAnswersWithTheOutcome()
        {
            foreach (ushort sub in new[] { AdminFriends.ForceAddSub, AdminFriends.ForceRemoveSub })
            {
                var forward = new ClientRequestForward { Sub = sub, ConnectionId = 7, CharacterGuid = 9, Body = ClientBody("Bob", "") };
                var parsed = Assert.IsType<ClientRequestForward>(SocialMessages.Parse(forward.ToPacket()));
                Assert.Equal((uint)sub, parsed.Sub);
                Assert.Equal(SocialMessages.CommandFamily, parsed.Family);
                Assert.Equal(forward.Body, parsed.Body);
            }
            var result = new AdminFriendResult
            {
                AdminConnection = 7, AdminGuid = 9, CommandSub = AdminFriends.ForceRemoveSub, Outcome = AdminFriendOutcome.NotOnList,
                Target = "Bob", Source = "Alice", SourceGuid = 5
            };
            var back = Assert.IsType<AdminFriendResult>(SocialMessages.Parse(result.ToPacket()));
            Assert.Equal(AdminFriendOutcome.NotOnList, back.Outcome);
            Assert.Equal(7ul, back.AdminConnection);
            Assert.Equal("Alice", back.Source);
        }

        [Fact]
        public void Remote_ServiceUnreachable_ChangesNothing_AndSaysSo()
        {
            WorldSession admin = world.Session(0x10, "Admin");
            var remote = new RemoteSocialService("127.0.0.1:1", 1);   // never started: no link
            remote.AdminFriend(admin, AdminCommands.ForceRemoveFriendSub, ClientBody("Bob", "Admin"));
            var (opcodes, texts) = Sent(admin);
            Assert.DoesNotContain(ZoneMessageOpcode.FriendBase, opcodes, "no friends-list change");
            Assert.True(texts.Any(t => t.Contains("social service is unavailable")), "the admin is told");
        }

        [Fact]
        public void Local_ADatabaseFailure_IsReported_NotClaimedAsRemoved()
        {
            WorldSession admin = world.Session(0x11, "Admin");
            Friends.HandleAdminForce(admin, AdminCommands.ForceRemoveFriendSub, ClientBody("Bob", "Admin"));
            var (opcodes, texts) = Sent(admin);
            Assert.DoesNotContain(ZoneMessageOpcode.FriendBase, opcodes, "no friends-list change");
            Assert.True(texts.Any(t => t.Contains("could not read or save")), "the admin is told");
            Assert.False(texts.Any(t => t.Contains("Removed")));
        }

        [Fact]
        public void TheAdminCommands_GoThroughTheSocialService()
        {
            // Both subs are admin subs (gated in AdminCommands.Handle) and the service interface carries them.
            Assert.True(AdminCommands.IsAdminSub(AdminCommands.ForceAddFriendSub));
            Assert.True(AdminCommands.IsAdminSub(AdminCommands.ForceRemoveFriendSub));
            Assert.Equal(AdminFriends.ForceAddSub, AdminCommands.ForceAddFriendSub);
            Assert.Equal(AdminFriends.ForceRemoveSub, AdminCommands.ForceRemoveFriendSub);
            Assert.NotNull(typeof(ISocialService).GetMethod(nameof(ISocialService.AdminFriend)));
        }
    }
}
