using LandmarkEmulator.Database.Social.Model;
using LandmarkEmulator.Internal.Social;
using LandmarkEmulator.Shared.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.SocialServer
{
    /// <summary>
    /// Friends lists (social-service.md §5.1 item 2; each character's own list, stored per direction; an accepted request
    /// fills both lists, <see cref="Mutual"/>), moved out of the world server's Game/Friends.cs with the same client
    /// behaviour, now across every world:
    /// <list type="bullet">
    /// <item>AddFriendRequest (Command 0x1E/0x10 {string name, u64 guid}; Ui.AddFriend / AddFriendByGuid FUN_14351d8b0 /
    /// FUN_14351da30): "/friend add Name" sends a name, the player menu a guid. Unknown or deleted → FriendMessage 0x0C;
    /// yourself → text 560744 FriendYourself; already listed → 560710 FriendAlreadyOnList; 200 friends → "Your friends
    /// list is full." (the limit is ours, as before). Otherwise the one asked gets ConfirmFriendRequest (0x1E/0x13, the
    /// client's "Friend Invite" popup) on whatever world they are, the asker FriendMessage 5; their ConfirmFriendResponse
    /// (0x1E/0x14) adds the friend (FriendAdd 0x4D/6 + FriendMessage 1; mutual: the asker to theirs too, see
    /// <see cref="HandleResponse"/>) or refuses (2); no answer in 2 minutes → 3; asked
    /// again meanwhile → 8; asked while offline → 3 at once [I]. Same rules as Game/Friends.cs (TASKS 5.4).</item>
    /// <item>RemoveFriendRequest (0x1E/0x12 {identity, u64 guid}; Ui.RemoveFriend by name / Ui.RemoveFriendByGuid from the
    /// player menu, <see cref="FriendRemoveRequest"/>): FriendRemove (0x4D/7) and text 560713 (the
    /// client's own "UI.FriendRemoved" key lookup finds nothing in this client's CodeStringMappings, see
    /// Game/Friends.cs).</item>
    /// <item>The friends list (0x4D/1) with each friend online when the service counts them online on any world.</item>
    /// <item>Admin /friend forceadd / remove (0x1E/0x4CF / 0x4D0, <see cref="HandleAdminForce"/>): the same changes and
    /// client packets without the request round trip, on any character's list; the outcome goes back to the admin's
    /// zone (AdminFriendResult).</item>
    /// </list>
    /// After any change the zone holding the listed character gets a fresh SocialStateForZone (its friend-of list).
    /// </summary>
    public sealed class FriendService
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const int MaxFriends = 200;

        private readonly SocialHub hub;

        /// <summary>How long a request waits for its answer (ours, as group invites).</summary>
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(2);
        /// <summary>Requests waiting for an answer: (asker, asked) → when they lapse (work loop only).</summary>
        private readonly Dictionary<(ulong From, ulong To), (DateTime Lapses, string ToName)> pending = new();

        /// <summary>
        /// An accepted request makes the friendship mutual (live; SocialServer.json FriendRequestsMutual, Docker
        /// FRIEND_REQUESTS_MUTUAL, default true); false = the old one-way lists (only the asker's list changes). Rules in
        /// <see cref="FriendAccept"/>.
        /// </summary>
        public bool Mutual { get; set; } = true;

        public FriendService(SocialHub hub) => this.hub = hub;

        /// <summary>Once a second on the work loop: lapsed requests tell their asker "timed out".</summary>
        public void Tick()
        {
            DateTime now = DateTime.UtcNow;
            foreach (var entry in pending.Where(p => p.Value.Lapses <= now).ToList())
            {
                pending.Remove(entry.Key);
                OnlineCharacter asker = hub.Presence.Find(entry.Key.From);
                if (asker is { Connected: true })
                    hub.SendTo(asker, ClientPackets.FriendMessage(ClientPackets.FriendMessageTimedOut, entry.Key.To, entry.Value.ToName));
            }
        }

        /// <summary>The character's whole friends list (FriendBase sub 1), online flags from presence on every world.</summary>
        public byte[] ListPacket(ulong character)
        {
            IReadOnlyList<ulong> friends = hub.Store.GetFriends(character);
            var rows = hub.Store.GetCharacters(friends.ToList()).ToDictionary(c => c.Guid);
            var list = new List<ClientPackets.Friend>(friends.Count);
            foreach (ulong guid in friends)
                if (rows.TryGetValue(guid, out SocialCharacterModel row))
                    list.Add(ToFriend(row));
            return ClientPackets.FriendList(list);
        }

        private ClientPackets.Friend ToFriend(SocialCharacterModel row)
        {
            OnlineCharacter presence = hub.Presence.Find(row.Guid);
            return new ClientPackets.Friend(row.Guid, presence?.Name ?? row.Name, presence != null, row.LastOnline);
        }

        /// <summary>SocialStateForZone for a character to the world it is connected to (nothing when it isn't).</summary>
        public void PushStateForZone(ulong character)
        {
            OnlineCharacter presence = hub.Presence.Find(character);
            if (presence is not { Connected: true })
                return;
            (ulong guildId, uint guildFlags, string guildName) = hub.Guilds.ZoneState(character);
            hub.SendService(presence.World, new SocialStateForZone
            {
                CharacterGuid = character,
                GuildId = guildId,
                GuildRankPermissions = guildFlags,
                GuildName = guildName,
                FriendOf = hub.Store.GetFriendOf(character).ToList()
            });
        }

        /// <summary>AddFriendRequest, handed on by the zone.</summary>
        public void HandleAdd(WorldLink world, ClientRequestForward request)
        {
            OnlineCharacter player = hub.Presence.FindConnected(world, request.CharacterGuid, request.ConnectionId);
            if (player == null)
            {
                log.Warn($"AddFriendRequest for {request.CharacterGuid:X} on {world} without a current connection: dropped.");
                return;
            }
            string name;
            ulong guid;
            try
            {
                var reader = new GamePacketReader(request.Body);
                name = reader.ReadString()?.Trim() ?? "";
                guid = reader.BytesRemaining >= 8 ? reader.ReadULong() : 0;
            }
            catch (Exception)
            {
                log.Warn($"Unreadable AddFriendRequest from {player}: {BitConverter.ToString(request.Body)}");
                return;
            }

            SocialCharacterModel target = name.Length > 0 ? hub.Store.FindCharacterByName(name)
                : guid != 0 ? hub.Store.GetCharacter(guid) : null;
            if (target?.DeletedAt != null)
                target = null;
            if (target == null)
            {
                hub.SendTo(player, ClientPackets.FriendMessage(ClientPackets.FriendMessageInvalidName, 0, name));
                log.Info($"{player} tried to add a friend named \"{name}\": no such player.");
                return;
            }
            if (target.Guid == player.Guid)
            {
                hub.SendTo(player, ClientPackets.SystemMessage(ClientPackets.FriendYourselfText));
                return;
            }
            IReadOnlyList<ulong> friends = hub.Store.GetFriends(player.Guid);
            if (friends.Contains(target.Guid))
            {
                hub.SendTo(player, ClientPackets.SystemMessage(ClientPackets.FriendAlreadyOnListText, ("player", target.Name)));
                return;
            }
            if (friends.Count >= MaxFriends)
            {
                hub.SendTo(player, ClientPackets.SystemLine("Your friends list is full."));
                return;
            }

            OnlineCharacter other = hub.Presence.Find(target.Guid);
            if (other is not { Connected: true })
            {
                hub.SendTo(player, ClientPackets.FriendMessage(ClientPackets.FriendMessageTimedOut, target.Guid, target.Name));
                log.Info($"{player} asked {target.Name} to be friends: offline, timed out at once.");
                return;
            }
            var key = (player.Guid, target.Guid);
            if (pending.TryGetValue(key, out var waiting) && waiting.Lapses > DateTime.UtcNow)
            {
                hub.SendTo(player, ClientPackets.FriendMessage(ClientPackets.FriendMessageNoSpam, target.Guid, target.Name));
                return;
            }
            pending[key] = (DateTime.UtcNow + RequestTimeout, target.Name);
            hub.SendTo(other, ClientPackets.ConfirmFriendRequest(player.Guid, player.Name));
            hub.SendTo(player, ClientPackets.FriendMessage(ClientPackets.FriendMessageRequested, target.Guid, target.Name));
            log.Info($"{player} asked {target.Name} to be friends.");
        }

        /// <summary>
        /// ConfirmFriendResponse (0x1E/0x14 {u64 asker, accept, string name}) from the one asked, handed on by their zone:
        /// accepted → the asker's list gets them (FriendAdd + FriendMessage 1) and, mutual (<see cref="Mutual"/>, live),
        /// their list gets the asker (FriendAdd with the asker's online state, wherever each of them is); every zone whose
        /// character is newly listed gets its friend-of list again. Caps and ignores: <see cref="FriendAccept"/>.
        /// Refused → FriendMessage 2. An answer to no waiting request is ignored.
        /// </summary>
        public void HandleResponse(WorldLink world, ClientRequestForward request)
        {
            OnlineCharacter player = hub.Presence.FindConnected(world, request.CharacterGuid, request.ConnectionId);
            if (player == null)
                return;
            if (!TryReadResponse(request.Body, out ulong askerGuid, out bool accepted))
            {
                log.Warn($"Unreadable ConfirmFriendResponse from {player}: {BitConverter.ToString(request.Body)}");
                return;
            }
            if (!pending.Remove((askerGuid, player.Guid)))
            {
                log.Info($"{player} answered a friend request from {askerGuid:X} that no longer waits.");
                return;
            }
            OnlineCharacter asker = hub.Presence.Find(askerGuid);
            bool askerOnline = asker is { Connected: true };
            if (!accepted)
            {
                if (askerOnline)
                    hub.SendTo(asker, ClientPackets.FriendMessage(ClientPackets.FriendMessageDeclined, player.Guid, player.Name));
                log.Info($"{player} declined {askerGuid:X}'s friend request.");
                return;
            }
            FriendAcceptPlan plan = FriendAccept.Decide(Mutual, askerGuid, player.Guid, hub.Store.GetFriends(askerGuid).ToList(),
                hub.Store.GetFriends(player.Guid).ToList(), hub.Ignores.Ignoring(askerGuid, player.Guid), hub.Ignores.Ignoring(player.Guid, askerGuid));
            if (plan.Blocked)
            {
                log.Info($"{player} accepted {askerGuid:X}'s friend request, but one of them ignores the other: nothing added.");
                return;
            }
            // The asker's list gets the one who accepted (both modes); their zone learns who lists them now.
            bool toAsker = plan.AddToAsker && hub.Store.AddFriend(askerGuid, player.Guid);
            if (toAsker)
            {
                SocialCharacterModel row = hub.Store.GetCharacter(player.Guid);
                if (askerOnline && row != null)
                    hub.SendTo(asker, ClientPackets.FriendAdd(ToFriend(row)));
                PushStateForZone(player.Guid);
            }
            // Mutual (live): the accepter's list gets the asker, with the asker's online state, wherever the two are.
            bool toAccepter = plan.AddToAccepter && hub.Store.AddFriend(player.Guid, askerGuid);
            if (toAccepter)
            {
                pending.Remove((player.Guid, askerGuid));
                SocialCharacterModel row = hub.Store.GetCharacter(askerGuid);
                if (row != null)
                    hub.SendTo(player, ClientPackets.FriendAdd(ToFriend(row)));
                PushStateForZone(askerGuid);
            }
            if (plan.AskerFull && askerOnline)
                hub.SendTo(asker, ClientPackets.SystemLine("Your friends list is full."));
            if (plan.AccepterFull)
                hub.SendTo(player, ClientPackets.SystemLine("Your friends list is full."));
            if ((toAsker || toAccepter) && askerOnline)
                hub.SendTo(asker, ClientPackets.FriendMessage(ClientPackets.FriendMessageAccepted, player.Guid, player.Name));
            log.Info($"{player} accepted {askerGuid:X}'s friend request ({(Mutual ? "mutual" : "one-way")}: "
                + $"{(toAsker ? "added to the asker's list" : plan.AskerFull ? "the asker's list is full" : "already on the asker's list")}"
                + (Mutual ? $", {(toAccepter ? "the asker added to theirs" : plan.AccepterFull ? "their list is full" : "the asker already on theirs")}" : "")
                + ").");
        }

        /// <summary>
        /// The response body after the u16 sub: u64 asker, the accept flag, the asker's name block {3 × u32, string first,
        /// string last} (the ctor copies the popup's 0xB0-byte name object, read by FUN_142c25b90). The flag's wire width
        /// isn't in the traced ctor (a 4-byte field), so the layout that ends exactly at the body's end decides: u32, else u8.
        /// </summary>
        private static bool TryReadResponse(byte[] body, out ulong asker, out bool accepted)
        {
            asker = 0;
            accepted = false;
            if (body == null || body.Length < 9)
                return false;
            asker = BitConverter.ToUInt64(body, 0);
            foreach (int width in new[] { 4, 1 })
            {
                long o = 8 + width + 12;
                for (int s = 0; s < 2 && o + 4 <= body.Length; s++)
                    o += 4 + BitConverter.ToUInt32(body, (int)o);
                if (o == body.Length)
                {
                    accepted = width == 4 ? BitConverter.ToUInt32(body, 8) != 0 : body[8] != 0;
                    return true;
                }
            }
            accepted = body[8] != 0;
            return true;
        }

        /// <summary>
        /// RemoveFriendRequest (body: identity {3 × u32, string first, string last}, u64 guid): by name from the friends
        /// window / "/friend remove", by guid from the player right-click menu (<see cref="FriendRemoveRequest"/>).
        /// One-sided: only the remover's list loses the row [I].
        /// </summary>
        public void HandleRemove(WorldLink world, ClientRequestForward request)
        {
            OnlineCharacter player = hub.Presence.FindConnected(world, request.CharacterGuid, request.ConnectionId);
            if (player == null)
            {
                log.Warn($"RemoveFriendRequest for {request.CharacterGuid:X} on {world} without a current connection: dropped.");
                return;
            }
            if (!FriendRemoveRequest.TryRead(request.Body, out FriendRemoveRequest remove))
            {
                log.Debug($"{player} removed an unknown friend: {BitConverter.ToString(request.Body)}");
                return;
            }
            SocialCharacterModel friend = remove.Find(hub.Store.GetCharacters(hub.Store.GetFriends(player.Guid).ToList()),
                f => f.Guid, f => f.Name);
            if (friend == null)
            {
                log.Debug($"{player} removed \"{remove.Name}\" ({remove.Guid:X}), who is not on the friends list.");
                return;
            }

            hub.Store.RemoveFriend(player.Guid, friend.Guid);
            hub.SendTo(player, ClientPackets.FriendRemove(friend.Guid));
            hub.SendTo(player, ClientPackets.SystemMessage(ClientPackets.FriendRemovedText, ("player", friend.Name)));
            PushStateForZone(friend.Guid);
            log.Info($"{player} removed {friend.Name} as a friend.");
        }

        /// <summary>
        /// Admin /friend forceadd | remove &lt;target&gt; [source] (AdminCommandPacketForceAddFriendRequest 0x1E/0x4CF /
        /// ...ForceRemoveFriendRequest 0x4D0, rules in <see cref="AdminFriends"/>), handed on by the admin's zone, which
        /// has checked that the sender is an admin (AdminCommands.Handle). The source is any character the directory
        /// knows, online or not (empty = the admin). Force-add puts the target on the source's list without asking
        /// (FriendAdd to the source when connected); force-remove is RemoveFriendRequest on the source's behalf
        /// (FriendRemove + text 560713 to the source when connected). Either way the listed character's zone gets a fresh
        /// SocialStateForZone (its friend-of list: online / offline notes), and the admin's zone gets
        /// <see cref="AdminFriendResult"/> with the outcome.
        /// </summary>
        public void HandleAdminForce(WorldLink world, ClientRequestForward request)
        {
            OnlineCharacter admin = hub.Presence.FindConnected(world, request.CharacterGuid, request.ConnectionId);
            if (admin == null)
            {
                log.Warn($"Admin friend command 0x{request.Sub:X} for {request.CharacterGuid:X} on {world} without a current connection: dropped.");
                return;
            }
            AdminFriendOutcome outcome;
            string targetName = "", sourceName = admin.Name;
            ulong sourceGuid = 0;
            if (!AdminFriends.TryRead(request.Body, out AdminFriends.Request command))
                outcome = AdminFriendOutcome.Unreadable;
            else
            {
                targetName = command.Target;
                if (command.Source.Length > 0)
                    sourceName = command.Source;
                try
                {
                    SocialCharacterModel source = hub.Store.FindCharacterByName(sourceName);
                    if (source != null)
                    {
                        sourceName = source.Name;
                        sourceGuid = source.Guid;
                    }
                    outcome = request.Sub == AdminFriends.ForceAddSub
                        ? AdminAdd(source, ref targetName)
                        : AdminRemove(source, ref targetName);
                }
                catch (Exception e)
                {
                    log.Error(e, $"Admin {admin} /friend {AdminFriends.Verb(request.Sub)} {targetName} [{sourceName}]: the store failed.");
                    outcome = AdminFriendOutcome.Failed;
                }
            }
            log.Info($"Admin {admin} /friend {AdminFriends.Verb(request.Sub)} {targetName} [{sourceName}]: {outcome}.");
            hub.SendService(world, new AdminFriendResult
            {
                AdminConnection = request.ConnectionId, AdminGuid = admin.Guid, CommandSub = request.Sub, Outcome = outcome,
                Target = targetName, Source = sourceName, SourceGuid = sourceGuid
            });
        }

        private AdminFriendOutcome AdminAdd(SocialCharacterModel source, ref string targetName)
        {
            SocialCharacterModel target = hub.Store.FindCharacterByName(targetName);
            if (target != null)
                targetName = target.Name;
            IReadOnlyList<ulong> friends = source != null ? hub.Store.GetFriends(source.Guid) : Array.Empty<ulong>();
            AdminFriendOutcome outcome = AdminFriends.DecideAdd(source?.Guid, target?.Guid, friends);
            if (outcome != AdminFriendOutcome.Added)
                return outcome;
            if (!hub.Store.AddFriend(source.Guid, target.Guid))
                return AdminFriendOutcome.AlreadyListed;
            pending.Remove((source.Guid, target.Guid));
            if (hub.Presence.Find(source.Guid) is { Connected: true } owner)
                hub.SendTo(owner, ClientPackets.FriendAdd(ToFriend(target)));
            PushStateForZone(target.Guid);
            return outcome;
        }

        private AdminFriendOutcome AdminRemove(SocialCharacterModel source, ref string targetName)
        {
            IReadOnlyList<SocialCharacterModel> friends = source != null
                ? hub.Store.GetCharacters(hub.Store.GetFriends(source.Guid).ToList()) : Array.Empty<SocialCharacterModel>();
            AdminFriendOutcome outcome = AdminFriends.DecideRemove(source?.Guid, targetName, friends, f => f.Name,
                out SocialCharacterModel friend);
            if (outcome != AdminFriendOutcome.Removed)
                return outcome;
            targetName = friend.Name;
            if (!hub.Store.RemoveFriend(source.Guid, friend.Guid))
                return AdminFriendOutcome.NotOnList;
            if (hub.Presence.Find(source.Guid) is { Connected: true } owner)
            {
                hub.SendTo(owner, ClientPackets.FriendRemove(friend.Guid));
                hub.SendTo(owner, ClientPackets.SystemMessage(ClientPackets.FriendRemovedText, ("player", friend.Name)));
            }
            PushStateForZone(friend.Guid);
            return outcome;
        }

        /// <summary>
        /// A character was deleted (TASKS 6.42; P4624, 13 Apr 2016: deleted characters leave friend and guild lists):
        /// it leaves every friends list it was on (FriendBase sub 7 Remove to those
        /// owners who are connected, as a manual removal, without the "removed" system line) and its own list is
        /// dropped. Idempotent: a second call finds nothing.
        /// </summary>
        public void CharacterDeleted(ulong character)
        {
            int listed = 0;
            foreach (ulong owner in hub.Store.GetFriendOf(character).ToList())
            {
                if (!hub.Store.RemoveFriend(owner, character))
                    continue;
                listed++;
                if (hub.Presence.Find(owner) is { Connected: true } online)
                    hub.SendTo(online, ClientPackets.FriendRemove(character));
                PushStateForZone(owner);
            }
            int own = 0;
            foreach (ulong friend in hub.Store.GetFriends(character).ToList())
                if (hub.Store.RemoveFriend(character, friend))
                {
                    own++;
                    PushStateForZone(friend);
                }
            if (listed + own > 0)
                log.Info($"Deleted character {character:X}: removed from {listed} friends list(s), its own list of {own} dropped.");
        }

        /// <summary>
        /// The login server's character select (CharacterSelectFriendsQuery, CharacterSelectFriends.cs): each asked
        /// character's friends list, online as the friends list counts it (connected, or in the leave grace) with the world
        /// and landscape they are on. The login server puts them in the character payload's Friends::Friend records, from
        /// which the client counts each world's online friends (FUN_1430c43e0, server list column FriendsOnline).
        /// </summary>
        public CharacterSelectFriendsReply CharacterSelectFriends(CharacterSelectFriendsQuery query)
        {
            var map = new Dictionary<ulong, IReadOnlyList<CharacterSelectFriend>>();
            foreach (ulong character in query.Characters.Distinct())
            {
                IReadOnlyList<ulong> friends = hub.Store.GetFriends(character);
                var rows = hub.Store.GetCharacters(friends.ToList()).Where(c => c.DeletedAt == null).ToDictionary(c => c.Guid);
                var list = new List<CharacterSelectFriend>(friends.Count);
                foreach (ulong guid in friends.Take(CharacterSelectFriendsReply.MaxFriends))
                {
                    if (!rows.TryGetValue(guid, out SocialCharacterModel row))
                        continue;
                    OnlineCharacter presence = hub.Presence.Find(guid);
                    list.Add(new CharacterSelectFriend(guid, presence?.Name ?? row.Name, presence != null,
                        presence?.WorldId ?? 0, presence?.ZoneId ?? 0));
                }
                map[character] = list;
            }
            return new CharacterSelectFriendsReply { RequestId = query.RequestId, Friends = map };
        }
    }
}
