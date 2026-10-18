using LandmarkEmulator.Database.Social;
using LandmarkEmulator.Database.Social.Model;
using LandmarkEmulator.Internal;
using LandmarkEmulator.Internal.Social;
using LandmarkEmulator.SocialServer;
using LandmarkEmulator.WorldServer.Game;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// Friend requests make mutual friends (user report 2026-10-18: "Marian accepted 2's friend request" and only the asker
    /// got a friend; wiki "Friends List": the list holds people "you either sent friend requests to or from whom you
    /// accepted friend requests"): the rules both carriers share (Internal/Social/FriendAccept.cs), the setting that keeps
    /// one-way lists, and the social service end to end across two worlds. Also RemoveFriendRequest by guid, as the
    /// player right-click menu sends it (Ui.RemoveFriendByGuid).
    /// </summary>
    public class MutualFriendRuleTests
    {
        private const ulong Asker = 0x2, Accepter = 0x5;
        private static readonly ulong[] None = Array.Empty<ulong>();
        private static List<ulong> FullList() => Enumerable.Range(1000, AdminFriends.MaxFriends).Select(i => (ulong)i).ToList();

        [Fact]
        public void Mutual_BothListsGetTheOther()
        {
            FriendAcceptPlan plan = FriendAccept.Decide(true, Asker, Accepter, None, None, false, false);
            Assert.True(plan.AddToAsker);
            Assert.True(plan.AddToAccepter);
            Assert.False(plan.Blocked);
            Assert.True(plan.Changes);
        }

        [Fact]
        public void OneWay_OnlyTheAskersListChanges()
        {
            FriendAcceptPlan plan = FriendAccept.Decide(false, Asker, Accepter, None, None, false, false);
            Assert.True(plan.AddToAsker);
            Assert.False(plan.AddToAccepter);
            Assert.False(plan.AccepterFull, "a one-way accept never looks at the accepter's list");
            Assert.False(FriendAccept.Decide(false, Asker, Accepter, None, FullList(), false, false).AccepterFull);
        }

        [Fact]
        public void EachSidesCap_CountsForItsOwnRowOnly()
        {
            FriendAcceptPlan askerFull = FriendAccept.Decide(true, Asker, Accepter, FullList(), None, false, false);
            Assert.False(askerFull.AddToAsker);
            Assert.True(askerFull.AskerFull);
            Assert.True(askerFull.AddToAccepter, "the accepter still gets the asker");

            FriendAcceptPlan accepterFull = FriendAccept.Decide(true, Asker, Accepter, None, FullList(), false, false);
            Assert.True(accepterFull.AddToAsker);
            Assert.False(accepterFull.AddToAccepter);
            Assert.True(accepterFull.AccepterFull);
        }

        [Fact]
        public void AnIgnoreEitherWay_StopsTheAccept()
        {
            foreach ((bool askerIgnores, bool accepterIgnores) in new[] { (true, false), (false, true), (true, true) })
                foreach (bool mutual in new[] { true, false })
                {
                    FriendAcceptPlan plan = FriendAccept.Decide(mutual, Asker, Accepter, None, None, askerIgnores, accepterIgnores);
                    Assert.True(plan.Blocked);
                    Assert.False(plan.Changes);
                    Assert.False(plan.AskerFull || plan.AccepterFull, "nothing is said");
                }
        }

        [Fact]
        public void AlreadyListed_IsNotAddedAgain_NorCalledFull()
        {
            var full = FullList();
            full[0] = Accepter;
            FriendAcceptPlan plan = FriendAccept.Decide(true, Asker, Accepter, full, new[] { Asker }, false, false);
            Assert.False(plan.Changes);
            Assert.False(plan.AskerFull);
            Assert.False(plan.AccepterFull);
            // An existing one-way row (made before this change): accepting again fills the missing direction.
            FriendAcceptPlan half = FriendAccept.Decide(true, Asker, Accepter, new[] { Accepter }, None, false, false);
            Assert.False(half.AddToAsker);
            Assert.True(half.AddToAccepter);
        }

        [Fact]
        public void TheSetting_DefaultsToLive_InTheWorldAndTheService_AndHasAPageRule()
        {
            Assert.True(new WorldServer.WorldServerConfiguration().FriendRequestsMutual);
            Assert.True(new SocialServerConfiguration().FriendRequestsMutual);
            SettingRule rule = ServerSettings.RuleFor("FriendRequestsMutual");
            Assert.NotNull(rule);
            Assert.Equal(SettingAccess.Editable, rule.Access);
            Assert.Equal("true", rule.Live);
            using (new Support.FakeWorld(c => c.FriendRequestsMutual = false))
                Assert.False(Friends.Mutual);
            using (new Support.FakeWorld())
                Assert.True(Friends.Mutual);
        }

        // ---- RemoveFriendRequest: by name (friends window) and by guid (player menu) ---------------------------------------

        private sealed record Row(ulong Guid, string Name);

        [Fact]
        public void Remove_ByName_OrByGuid_AsTheTwoNativesFillIt()
        {
            var friends = new[] { new Row(7, "Bob"), new Row(9, "Carol") };
            // Ui.RemoveFriend: the typed name; the guid field is never set by that native (anything may be in it).
            Assert.True(FriendRemoveRequest.TryRead(FriendRemoveRequest.Write("carol", 0xDEAD), out var byName));
            Assert.Equal(9ul, byName.Find(friends, f => f.Guid, f => f.Name).Guid);
            // Ui.RemoveFriendByGuid: an empty identity and the guid.
            Assert.True(FriendRemoveRequest.TryRead(FriendRemoveRequest.Write("", 7), out var byGuid));
            Assert.Equal("", byGuid.Name);
            Assert.Equal(7ul, byGuid.Guid);
            Assert.Equal("Bob", byGuid.Find(friends, f => f.Guid, f => f.Name).Name);
            // Not listed / nothing given.
            Assert.True(FriendRemoveRequest.TryRead(FriendRemoveRequest.Write("", 8), out var other));
            Assert.Null(other.Find(friends, f => f.Guid, f => f.Name));
            Assert.True(FriendRemoveRequest.TryRead(FriendRemoveRequest.Write("", 0), out var empty));
            Assert.Null(empty.Find(friends, f => f.Guid, f => f.Name));
        }

        [Fact]
        public void Remove_AnOlderBodyWithoutTheGuid_StillReadsTheName()
        {
            byte[] body = FriendRemoveRequest.Write("Bob", 0);
            Assert.True(FriendRemoveRequest.TryRead(body[..^8], out var request));
            Assert.Equal("Bob", request.Name);
            Assert.Equal(0ul, request.Guid);
            Assert.False(FriendRemoveRequest.TryRead(body[..10], out _));
            Assert.False(FriendRemoveRequest.TryRead(null, out _));
        }

        // ---- The social service end to end: two worlds, one player on each --------------------------------------------------

        private const ulong AskerConnection = 101, AccepterConnection = 202;

        /// <summary>A social hub on a loopback port with an in-memory store, and two world links (world 1: Robin, world 2: Marian).</summary>
        private sealed class TwoWorlds : IDisposable
        {
            public readonly MemoryStore Store = new();
            public readonly SocialHub Hub;
            public readonly WorldEnd World1, World2;

            public TwoWorlds(bool mutual)
            {
                Hub = new SocialHub(Store, IPAddress.Loopback, 0, TimeSpan.Zero);
                Hub.Friends.Mutual = mutual;
                Hub.Start();
                World1 = new WorldEnd(Hub.Port, 1, new CharacterPresence(AskerConnection, Asker, 20, "Robin", 1, 5, "0.695"));
                World2 = new WorldEnd(Hub.Port, 2, new CharacterPresence(AccepterConnection, Accepter, 50, "Marian", 2, 5, "0.695"));
                // Both bootstrapped (each gets its friends list first).
                Assert.NotNull(World1.WaitClient(AskerConnection, 0x4D, ClientPackets.FriendListSub));
                Assert.NotNull(World2.WaitClient(AccepterConnection, 0x4D, ClientPackets.FriendListSub));
            }

            /// <summary>Robin asks Marian by name; Marian's world gets the Friend Invite popup (0x1E/0x13); Marian answers.</summary>
            public void AskAndAnswer(bool accept)
            {
                World1.Forward(SocialMessages.AddFriendRequest, AskerConnection, Asker, new InternalWriter().Str("Marian").U64(0).ToArray());
                Assert.NotNull(World2.WaitClient(AccepterConnection, 0x1E, 0x13));
                byte[] answer = new InternalWriter().U64(Asker).U32(accept ? 1u : 0u).U32(0).U32(0).U32(0).Str("Robin").Str("").ToArray();
                World2.Forward(SocialMessages.ConfirmFriendResponse, AccepterConnection, Accepter, answer);
            }

            public void Dispose()
            {
                World1.Dispose();
                World2.Dispose();
                Hub.Dispose();
            }
        }

        /// <summary>The test's side of one world's link to the service: what reaches each connection, and the service messages.</summary>
        private sealed class WorldEnd : IDisposable
        {
            private readonly InternalLink link;
            private readonly BlockingCollection<(ulong Connection, byte[] Packet)> client = new();
            public readonly BlockingCollection<ISocialMessage> Service = new();
            private readonly List<(ulong Connection, byte[] Packet)> seen = new();

            public WorldEnd(int port, uint worldId, CharacterPresence character)
            {
                var tcp = new TcpClient();
                tcp.Connect(IPAddress.Loopback, port);
                link = new InternalLink(tcp);
                link.Received += (_, m) =>
                {
                    switch (m)
                    {
                        case TunnelToExternal one:
                            client.Add((one.Destination.ConnectionId, one.Packet));
                            break;
                        case TunnelToExternalList many:
                            foreach (ExternalDestination d in many.Destinations)
                                client.Add((d.ConnectionId, many.Packet));
                            break;
                        case ServicePacket packet when SocialMessages.Parse(packet) is ISocialMessage social:
                            Service.Add(social);
                            break;
                    }
                };
                link.Start();
                link.Send(new PresenceSnapshot { WorldId = worldId, Characters = new[] { character } }.ToPacket());
            }

            public void Forward(uint sub, ulong connection, ulong guid, byte[] body)
                => link.Send(new ClientRequestForward { Sub = sub, ConnectionId = connection, CharacterGuid = guid, Body = body }.ToPacket());

            private static bool Is(byte[] p, byte opcode, ushort sub)
                => p.Length >= 3 && p[0] == opcode && p[1] == 0 && (opcode == 0x1E ? p.Length >= 4 && BitConverter.ToUInt16(p, 2) == sub : p[2] == sub);

            /// <summary>The next client packet of this opcode / sub for the connection (10 s), or null.</summary>
            public byte[] WaitClient(ulong connection, byte opcode, ushort sub, Func<byte[], bool> match = null, int ms = 10000)
            {
                DateTime until = DateTime.UtcNow.AddMilliseconds(ms);
                while (DateTime.UtcNow < until)
                    if (client.TryTake(out var item, 50))
                    {
                        seen.Add(item);
                        if (item.Connection == connection && Is(item.Packet, opcode, sub) && (match == null || match(item.Packet)))
                            return item.Packet;
                    }
                return null;
            }

            /// <summary>Everything that arrived for the connection within the wait (for "nothing of this kind came").</summary>
            public List<byte[]> Drain(ulong connection, int ms = 700)
            {
                WaitClient(connection, 0xFF, 0xFF, null, ms);
                var all = seen.Where(s => s.Connection == connection).Select(s => s.Packet).ToList();
                seen.Clear();
                return all;
            }

            public SocialStateForZone WaitState(ulong character, Func<SocialStateForZone, bool> match)
            {
                DateTime until = DateTime.UtcNow.AddSeconds(10);
                while (DateTime.UtcNow < until)
                    if (Service.TryTake(out ISocialMessage m, 50) && m is SocialStateForZone s && s.CharacterGuid == character && match(s))
                        return s;
                return null;
            }

            public void Dispose() => link.Close();
        }

        private static bool Holds(byte[] packet, ulong guid)
        {
            byte[] g = BitConverter.GetBytes(guid);
            for (int i = 0; i + 8 <= packet.Length; i++)
                if (packet.AsSpan(i, 8).SequenceEqual(g))
                    return true;
            return false;
        }

        private static bool IsAccepted(byte[] p) => p.Length > 3 && p[3] == ClientPackets.FriendMessageAccepted;

        [Fact]
        public void Service_Accept_MakesBothFriends_OnTheirOwnWorlds_AtOnce()
        {
            using var worlds = new TwoWorlds(mutual: true);
            worlds.AskAndAnswer(accept: true);

            // The asker (world 1): Marian on the list (online), and "Marian has accepted your friend request."
            Assert.NotNull(worlds.World1.WaitClient(AskerConnection, 0x4D, ClientPackets.FriendAddSub, p => Holds(p, Accepter)));
            Assert.NotNull(worlds.World1.WaitClient(AskerConnection, 0x4D, ClientPackets.FriendMessageSub, IsAccepted));
            // The accepter (world 2): Robin on their list too, at once (the client shows "has been added as a friend").
            byte[] add = worlds.World2.WaitClient(AccepterConnection, 0x4D, ClientPackets.FriendAddSub, p => Holds(p, Asker));
            Assert.NotNull(add);
            Assert.Contains("Robin", System.Text.Encoding.UTF8.GetString(add));

            Assert.Contains(Accepter, worlds.Store.GetFriends(Asker));
            Assert.Contains(Asker, worlds.Store.GetFriends(Accepter));
            // Each zone learns who lists its player now (online / offline notes, friends on the map).
            Assert.NotNull(worlds.World2.WaitState(Accepter, s => s.FriendOf.Contains(Asker)));
            Assert.NotNull(worlds.World1.WaitState(Asker, s => s.FriendOf.Contains(Accepter)));
        }

        [Fact]
        public void Service_OneWaySetting_KeepsTheOldLists()
        {
            using var worlds = new TwoWorlds(mutual: false);
            worlds.AskAndAnswer(accept: true);
            Assert.NotNull(worlds.World1.WaitClient(AskerConnection, 0x4D, ClientPackets.FriendMessageSub, IsAccepted));
            Assert.Contains(Accepter, worlds.Store.GetFriends(Asker));
            Assert.Empty(worlds.Store.GetFriends(Accepter));
            Assert.False(worlds.World2.Drain(AccepterConnection).Any(p => p.Length > 2 && p[0] == 0x4D && p[2] == ClientPackets.FriendAddSub), "no FriendAdd to the accepter");
        }

        [Fact]
        public void Service_AnIgnoredAsker_GetsNoFriend_AndNobodyIsTold()
        {
            using var worlds = new TwoWorlds(mutual: true);
            // Marian ignores Robin (IgnoreRequest 0x1E/0x20 {u64 guid, name block, u8 1}) once the popup is up.
            worlds.World1.Forward(SocialMessages.AddFriendRequest, AskerConnection, Asker, new InternalWriter().Str("Marian").U64(0).ToArray());
            Assert.NotNull(worlds.World2.WaitClient(AccepterConnection, 0x1E, 0x13));
            worlds.World2.Forward(SocialMessages.IgnoreRequest, AccepterConnection, Accepter,
                new InternalWriter().U64(Asker).U32(0).U32(0).U32(0).Str("Robin").Str("").U8(1).ToArray());
            Assert.NotNull(worlds.World2.WaitClient(AccepterConnection, 0x72, ClientPackets.IgnoreAddSub));
            byte[] answer = new InternalWriter().U64(Asker).U32(1).U32(0).U32(0).U32(0).Str("Robin").Str("").ToArray();
            worlds.World2.Forward(SocialMessages.ConfirmFriendResponse, AccepterConnection, Accepter, answer);
            Assert.False(worlds.World1.Drain(AskerConnection, 1000).Any(p => p.Length > 2 && p[0] == 0x4D
                && (p[2] == ClientPackets.FriendAddSub || IsAccepted(p))), "nothing to the asker");
            Assert.Empty(worlds.Store.GetFriends(Asker));
            Assert.Empty(worlds.Store.GetFriends(Accepter));
        }

        [Fact]
        public void Service_ARefusal_ChangesNothing()
        {
            using var worlds = new TwoWorlds(mutual: true);
            worlds.AskAndAnswer(accept: false);
            Assert.NotNull(worlds.World1.WaitClient(AskerConnection, 0x4D, ClientPackets.FriendMessageSub,
                p => p.Length > 3 && p[3] == ClientPackets.FriendMessageDeclined));
            Assert.Empty(worlds.Store.GetFriends(Asker));
            Assert.Empty(worlds.Store.GetFriends(Accepter));
        }

        [Fact]
        public void Service_RemoveByGuid_FromTheMenu_IsOneSided()
        {
            using var worlds = new TwoWorlds(mutual: true);
            worlds.AskAndAnswer(accept: true);
            Assert.NotNull(worlds.World2.WaitClient(AccepterConnection, 0x4D, ClientPackets.FriendAddSub, p => Holds(p, Asker)));

            // Marian right-clicks Robin → Remove Friend (Ui.RemoveFriendByGuid: empty identity + guid).
            worlds.World2.Forward(SocialMessages.RemoveFriendRequest, AccepterConnection, Accepter, FriendRemoveRequest.Write("", Asker));
            byte[] removed = worlds.World2.WaitClient(AccepterConnection, 0x4D, ClientPackets.FriendRemoveSub, p => Holds(p, Asker));
            Assert.NotNull(removed);
            Assert.Empty(worlds.Store.GetFriends(Accepter));
            Assert.Contains(Accepter, worlds.Store.GetFriends(Asker));
            Assert.False(worlds.World1.Drain(AskerConnection).Any(p => p.Length > 2 && p[0] == 0x4D && p[2] == ClientPackets.FriendRemoveSub), "the other list keeps its row");

            // And by name from the friends window (Robin removes Marian).
            worlds.World1.Forward(SocialMessages.RemoveFriendRequest, AskerConnection, Asker, FriendRemoveRequest.Write("marian", 0));
            Assert.NotNull(worlds.World1.WaitClient(AskerConnection, 0x4D, ClientPackets.FriendRemoveSub, p => Holds(p, Accepter)));
            Assert.Empty(worlds.Store.GetFriends(Asker));
        }

        /// <summary>An in-memory social store (friends, ignores, the directory; no guilds, no mail). Called from the hub's work loop and the test.</summary>
        private sealed class MemoryStore : ISocialStore
        {
            private readonly object sync = new();
            private readonly Dictionary<ulong, SocialCharacterModel> characters = new();
            private readonly List<(ulong Character, ulong Friend)> friends = new();
            private readonly Dictionary<ulong, Dictionary<ulong, string>> ignores = new();

            public SocialCharacterModel GetCharacter(ulong guid) { lock (sync) return characters.GetValueOrDefault(guid); }
            public IReadOnlyList<SocialCharacterModel> GetCharacters(IReadOnlyCollection<ulong> guids)
            { lock (sync) return guids.Where(characters.ContainsKey).Select(g => characters[g]).ToList(); }
            public SocialCharacterModel FindCharacterByName(string name)
            {
                lock (sync)
                    return characters.Values.FirstOrDefault(c => c.DeletedAt == null && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            }
            public void UpsertCharacter(ulong guid, ulong accountId, string name, bool deleted = false)
            {
                lock (sync)
                {
                    if (!characters.TryGetValue(guid, out SocialCharacterModel c))
                        characters[guid] = c = new SocialCharacterModel { Guid = guid };
                    c.AccountId = accountId;
                    c.Name = name;
                    if (deleted)
                        c.DeletedAt = DateTime.UtcNow;
                }
            }
            public void RecordLastSeen(ulong guid, ulong world, uint zone, DateTime? lastOnline, uint zoneNameText = 0, uint worldNameText = 0) { }
            public IReadOnlyList<ulong> GetFriends(ulong character) { lock (sync) return friends.Where(f => f.Character == character).Select(f => f.Friend).ToList(); }
            public IReadOnlyList<ulong> GetFriendOf(ulong character) { lock (sync) return friends.Where(f => f.Friend == character).Select(f => f.Character).ToList(); }
            public bool AddFriend(ulong character, ulong friend)
            {
                lock (sync)
                {
                    if (friends.Contains((character, friend)))
                        return false;
                    friends.Add((character, friend));
                    return true;
                }
            }
            public bool RemoveFriend(ulong character, ulong friend) { lock (sync) return friends.Remove((character, friend)); }
            public IReadOnlyDictionary<ulong, string> GetIgnores(ulong character)
            { lock (sync) return ignores.TryGetValue(character, out var list) ? new Dictionary<ulong, string>(list) : new Dictionary<ulong, string>(); }
            public bool AddIgnore(ulong character, ulong ignored, string name)
            {
                lock (sync)
                {
                    if (!ignores.TryGetValue(character, out var list))
                        ignores[character] = list = new Dictionary<ulong, string>();
                    return list.TryAdd(ignored, name);
                }
            }
            public bool RemoveIgnore(ulong character, ulong ignored) { lock (sync) return ignores.TryGetValue(character, out var list) && list.Remove(ignored); }
            public IReadOnlyList<SocialGuildModel> GetGuilds() => Array.Empty<SocialGuildModel>();
            public IReadOnlyList<SocialGuildMemberModel> GetGuildMembers() => Array.Empty<SocialGuildMemberModel>();
            public void SaveGuild(SocialGuildModel guild) { }
            public void DeleteGuild(ulong guildId) { }
            public void SetGuildMember(ulong character, ulong guildId, uint rankId) { }
            public void RemoveGuildMember(ulong character) { }
            public IReadOnlyList<SocialMailModel> GetInbox(ulong recipient, int max) => Array.Empty<SocialMailModel>();
            public SocialMailModel GetMail(ulong id, ulong recipient) => null;
            public int CountInbox(ulong recipient) => 0;
            public bool HasMailToken(ulong token) => false;
            public void AddMail(IReadOnlyCollection<SocialMailModel> mail) { }
            public void MarkMailRead(ulong id) { }
            public bool DeleteMail(ulong id, ulong recipient) => false;
            public int DeleteExpiredMail(ulong recipient, DateTime cutoff) => 0;
            public void SetMailAttachmentsTaken(ulong mailId, IReadOnlyCollection<uint> slots) { }
            public bool HasWaitingMailItem(ulong recipient, uint itemDefinition) => false;
        }
    }
}
