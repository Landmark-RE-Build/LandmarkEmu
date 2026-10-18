using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Internal;
using LandmarkEmulator.Internal.Social;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldServer.Game.Social
{
    /// <summary>
    /// The shared social service as a remote container (LandmarkEmulator.SocialServer; social-service.md §6 steps 3-4),
    /// chosen by SOCIAL_ADDRESS (<see cref="SocialServices.Configure"/>). This world dials the service
    /// (<see cref="InternalDialer"/>, reconnecting by itself) and, as the gateway of its own players until the gateway
    /// service exists, carries both directions on that one link:
    /// <list type="bullet">
    /// <item>World → social, ServicePackets (layouts: LandmarkEmulator.Internal/Social/SocialMessages.cs):
    /// PresenceSnapshot first on every (re)connect (which world this is + everyone in it), CharacterEnteredWorld /
    /// CharacterLeftWorld from <see cref="PlayerManager"/> Enter / Leave, ZoneChanged, and the client's friend / ignore
    /// requests handed on unchanged with the connection id and the zone-verified character guid
    /// (AddFriendRequest 0x1E/0x10, RemoveFriendRequest 0x1E/0x12, IgnoreRequest 0x1E/0x20).</item>
    /// <item>Social → world: TunnelPacketToExternalConnection / ...List (0x16) frames, whose client packets are queued
    /// to the sessions with those connection ids exactly like the world's own (the 0.695 translation is applied per
    /// session on the way out, <see cref="WorldSession.EnqueueMessage(ZoneMessageOpcode, byte[])"/>); IgnoreListSync
    /// (into <see cref="Ignores"/>' per-player table, which every zone-side ignore check reads); SocialStateForZone
    /// (friend-of per online character); PresenceReply.</item>
    /// </list>
    /// Moved so far (§6 step 5): presence, friends, ignore, guilds (GuildBase 0x83 requests handed on whole; the service
    /// answers the client itself; GuildJoined runs the guild goal here; SocialStateForZone carries the guild id / name and
    /// the officer-chat / mute bits), routed chat (tells, guild / officer, public channels and chat rooms: the zone
    /// checks a line and hands it on, ChatBase 0x13 / 1; room requests 0x0A / 0x0C whole, after the zone's bad-word check
    /// of the room name, which needs the game tables), mail (the mailboxes; the zone keeps the bag side — compose
    /// checks, escrow, hand-over of attachments, the reading window's item records: <see cref="RemoteMail"/>), and the
    /// Game Finder's counts across worlds (<see cref="IMatchmakingService"/>: searches and this world's games go to the
    /// service, its MatchmakingUpdate comes back to InstancedGames.FinderServiceUpdate; matching and joining a game on
    /// this world stay in the zone), and groups across worlds (<see cref="RemoteGroups"/>, social-service.md §10: the
    /// client's GroupsBase invite / join / leave / kick / disband / set-owner requests go to the service, this zone
    /// publishes its grouped players' party-frame records and keeps GroupStateForZone for what it does with the members
    /// on this world).
    /// <para>
    /// While the service is unreachable (§5.6): friend / ignore requests are dropped (the client expects no reply to
    /// them; logged), the ignore lists and friend-of lists already cached stay in use, and a player entering meanwhile
    /// is announced in the PresenceSnapshot of the reconnect, which makes the service send the lists then.
    /// </para>
    /// <para>
    /// Character directory bridge: the service has its own directory (social_character, imported once, §5.7). Until
    /// the login service sends CharacterCreated / Renamed / Deleted (hand-off, CharacterDirectoryUpdate), a friend or
    /// ignore request that names a character is preceded by a CharacterDirectoryUpdate "seen" for that character, read
    /// from this world's character database, so characters made after the import are found.
    /// </para>
    /// </summary>
    public sealed class RemoteSocialService : ISocialService, IIgnoreCache, IGuildCache, IFriendOfCache, IPresenceLookup, IMatchmakingService, IDisposable
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>How long <see cref="Find"/> waits for the service's PresenceReply.</summary>
        private static readonly TimeSpan PresenceTimeout = TimeSpan.FromSeconds(1);

        private readonly InternalDialer dialer;
        private readonly string address;
        private readonly uint worldId;

        private readonly object sync = new();
        /// <summary>The link to the service while it is up (set once the PresenceSnapshot is queued on it).</summary>
        private InternalLink link;
        /// <summary>Players in the world by connection id (the id the service addresses them by).</summary>
        private readonly Dictionary<ulong, WorldSession> connections = new();
        private readonly Dictionary<WorldSession, ulong> connectionOf = new();
        /// <summary>SocialStateForZone: who lists each character online here.</summary>
        private readonly Dictionary<ulong, ulong[]> friendOf = new();
        /// <summary>SocialStateForZone: each online character's guild (id, name, flags).</summary>
        private readonly Dictionary<ulong, (ulong Id, string Name, uint Flags)> guildOf = new();
        private readonly Dictionary<uint, TaskCompletionSource<PresenceReply>> presenceQueries = new();
        private int nextQuery;
        /// <summary>The zone's part of mail.</summary>
        private readonly RemoteMail mail;
        /// <summary>Work from the link that changes game state: run on the world thread (<see cref="Update"/>).</summary>
        private readonly System.Collections.Concurrent.ConcurrentQueue<Action> worldQueue = new();
        /// <summary>1 after a (re)connect until the Game Finder has re-sent its games and searches (<see cref="TakeResync"/>).</summary>
        private int matchmakingResync;
        /// <summary>The zone's part of groups across worlds.</summary>
        private readonly RemoteGroups groups;

        /// <summary>Groups across worlds (<see cref="SocialServices.Groups"/>).</summary>
        public IGroupService Groups => groups;

        /// <summary>
        /// Connection ids (ours; the gateway's connection id once it is its own service): unique per process and, with the
        /// start time in the high bits, not reused by a restarted world while the service still remembers the old ones.
        /// </summary>
        private static long nextConnection = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() << 16;

        public RemoteSocialService(string address, uint worldId)
        {
            this.address = address;
            this.worldId = worldId;
            dialer = new InternalDialer(address);
            dialer.Connected += OnConnected;
            mail = new RemoteMail(Send, ConnectionOf, guid => PlayerManager.FindByGuid(guid) as WorldSession);
            groups = new RemoteGroups(Send, ConnectionOf, () => IsConnected, worldQueue.Enqueue, worldId);
        }

        /// <summary>The connection id the service knows a player by, or null (not entered, or the service unreachable).</summary>
        private ulong? ConnectionOf(WorldSession session)
        {
            lock (sync)
                return link != null && connectionOf.TryGetValue(session, out ulong id) ? id : null;
        }

        /// <summary>World thread (HostedService): runs what the link queued.</summary>
        public void Update()
        {
            while (worldQueue.TryDequeue(out Action action))
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    log.Error(e, "Social work from the service failed.");
                }
            }
            mail.Update();
        }

        public void Start()
        {
            dialer.Start();
            log.Info($"Social service: remote at {address} (presence, friends, ignore, guilds, chat, mail, Game Finder, groups).");
        }

        public bool IsConnected
        {
            get
            {
                lock (sync)
                    return link is { IsOpen: true };
            }
        }

        // ---- The link -------------------------------------------------------------------------------------------------

        private void OnConnected(InternalLink newLink)
        {
            newLink.Received += OnReceived;
            newLink.Closed += (l, error) =>
            {
                lock (sync)
                    if (link == l)
                        link = null;
                log.Warn($"Social service link lost{(error != null ? ": " + error.Message : "")}; reconnecting.");
            };
            lock (sync)
            {
                // The first message on every link: which world this is and everyone in it (§5.3, §5.6). Under the lock,
                // so an Entered can't slip between the snapshot and the link becoming current.
                newLink.Send(new PresenceSnapshot
                {
                    WorldId = worldId,
                    Characters = connections.Select(c => PresenceOf(c.Value, c.Key)).ToList()
                }.ToPacket());
                link = newLink;
                log.Info($"Social service connected ({connections.Count} player(s) announced).");
            }
            mail.Reconnected();
            Interlocked.Exchange(ref matchmakingResync, 1);
        }

        private CharacterPresence PresenceOf(WorldSession session, ulong connectionId)
            => new(connectionId, session.PlayerGuid, session.Account?.Id ?? 0, session.CharacterName ?? "", worldId, session.ZoneId,
                session.ClientBuild ?? "", ZoneNameText(session.ZoneId), WorldNameText);

        /// <summary>
        /// This world's name text for the guild roster's Location, now that guildmates are on several worlds: the
        /// server-list name (WorldServer.json WorldNameId), else Landscapes' single-world default (560912).
        /// </summary>
        private static uint WorldNameText
            => Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config?.WorldNameId is uint id and not 0
                ? id : Landscapes.WorldNameText;

        /// <summary>A landscape's name text (StaticZoneData), for the guild roster's Location.</summary>
        private static uint ZoneNameText(uint zone) => Landscapes.Get(zone)?.NameText ?? 0;

        /// <summary>Sends to the service; false (and nothing sent) while it is unreachable.</summary>
        private bool Send(ISocialMessage message)
        {
            lock (sync)
                return link != null && link.Send(message.ToPacket());
        }

        /// <summary>Runs on the link's reading task: queueing to sessions and the caches are thread-safe.</summary>
        private void OnReceived(InternalLink from, IInternalMessage message)
        {
            switch (message)
            {
                case TunnelToExternal one:
                    Deliver(one.Destination.ConnectionId, one.Packet);
                    break;
                case TunnelToExternalList many:
                    foreach (ExternalDestination destination in many.Destinations)
                        Deliver(destination.ConnectionId, many.Packet);
                    break;
                case ServicePacket status when ServiceStatuses.TryTake(status):
                    break;
                case ServicePacket packet:
                    OnServicePacket(packet);
                    break;
                default:
                    log.Warn($"Social service sent internal opcode {message.Opcode}: not handled.");
                    break;
            }
        }

        /// <summary>A client packet (zone opcode bytes + body, the 0.695 client's numbering) for one of this world's players.</summary>
        private void Deliver(ulong connectionId, byte[] packet)
        {
            WorldSession session;
            lock (sync)
                connections.TryGetValue(connectionId, out session);
            if (session == null || session.Disconnected)
            {
                log.Debug($"Social packet for connection {connectionId:X}, which is gone: dropped.");
                return;
            }
            (ZoneMessageOpcode? opcode, int length) = WorldSession.GetOpcode(packet);
            if (opcode is ZoneMessageOpcode op)
                session.EnqueueMessage(op, packet[length..]);
            else
            {
                log.Warn($"Social packet with an unknown opcode for {session.CharacterName}: sent as is.");
                session.SendRaw(packet);
            }
        }

        private void OnServicePacket(ServicePacket packet)
        {
            ISocialMessage message;
            try
            {
                message = SocialMessages.Parse(packet);
            }
            catch (System.IO.InvalidDataException e)
            {
                log.Warn($"Unreadable service packet 0x{packet.Family:X}/0x{packet.Sub:X} from the social service: {e.Message}");
                return;
            }
            switch (message)
            {
                case IgnoreListSync ignores:
                    Ignores.Replace(ignores.CharacterGuid, ignores.Ignored);
                    break;
                case SocialStateForZone state:
                    lock (sync)
                    {
                        friendOf[state.CharacterGuid] = state.FriendOf.ToArray();
                        if (state.GuildId != 0)
                            guildOf[state.CharacterGuid] = (state.GuildId, state.GuildName, state.GuildRankPermissions);
                        else
                            guildOf.Remove(state.CharacterGuid);
                    }
                    break;
                case GuildJoined joined:
                    worldQueue.Enqueue(() =>
                    {
                        if (PlayerManager.FindByGuid(joined.CharacterGuid) is WorldSession member)
                            Achievements.GuildJoined(member);
                    });
                    break;
                case MailComposeResult composed:
                    mail.ComposeAnswered(composed);
                    break;
                case MailConsumeZoneRequest handOver:
                    mail.HandOver(handOver);
                    break;
                case MailReadData read:
                    mail.Read(read);
                    break;
                case MailAttachmentReply reply:
                    mail.QueryAnswered(reply);
                    break;
                case MatchmakingUpdate update:
                    worldQueue.Enqueue(() => InstancedGames.FinderServiceUpdate(update));
                    break;
                case GroupStateForZone groupState:
                    groups.State(groupState);
                    break;
                case PresenceReply reply:
                    TaskCompletionSource<PresenceReply> waiting;
                    lock (sync)
                        presenceQueries.Remove(reply.RequestId, out waiting);
                    waiting?.TrySetResult(reply);
                    break;
                default:
                    log.Warn($"Social service sent service packet 0x{packet.Family:X}/0x{packet.Sub:X}: not handled.");
                    break;
            }
        }

        // ---- ISocialService -------------------------------------------------------------------------------------------

        public void PlayerEntered(WorldSession session)
        {
            ulong id = (ulong)Interlocked.Increment(ref nextConnection);
            lock (sync)
            {
                if (connectionOf.Remove(session, out ulong old))
                    connections.Remove(old);
                connections[id] = session;
                connectionOf[session] = id;
                link?.Send(new CharacterEnteredWorld { Character = PresenceOf(session, id) }.ToPacket());
            }
        }

        public void PlayerLeft(WorldSession session)
        {
            lock (sync)
            {
                if (connectionOf.Remove(session, out ulong id))
                {
                    connections.Remove(id);
                    link?.Send(new CharacterLeftWorld { ConnectionId = id, CharacterGuid = session.PlayerGuid, Reason = CloseReason.Logout }.ToPacket());
                }
                friendOf.Remove(session.PlayerGuid);
                guildOf.Remove(session.PlayerGuid);
                // The group stays with the service (its leave grace keeps it over a world change); this zone forgets it
                // unless the character is still here on a newer session (a relog).
                if (!connections.Values.Any(s => s.PlayerGuid == session.PlayerGuid))
                    groups.Forget(session.PlayerGuid);
            }
            Ignores.Leave(session);
        }

        /// <summary>
        /// Tells the service a character was deleted (CharacterDirectoryUpdate Deleted): it marks the directory row and
        /// takes the character off every friends list and out of its guild (SocialHub, TASKS 6.42).
        /// </summary>
        public bool CharacterDeleted(ulong characterGuid, ulong accountId, string name)
            => Send(new CharacterDirectoryUpdate
            {
                Change = DirectoryChange.Deleted, CharacterGuid = characterGuid, AccountId = accountId, Name = name ?? ""
            });

        public void PlayerChangedZone(WorldSession session)
            => Send(new CharacterZoneChanged
            {
                CharacterGuid = session.PlayerGuid, WorldId = worldId, ZoneId = session.ZoneId,
                ZoneNameText = ZoneNameText(session.ZoneId), WorldNameText = WorldNameText
            });

        public void AddFriend(WorldSession session, byte[] body)
        {
            try
            {
                var reader = new GamePacketReader(body);
                string name = reader.ReadString()?.Trim() ?? "";
                ulong guid = reader.BytesRemaining >= 8 ? reader.ReadULong() : 0;
                AnnounceCharacter(name, guid);
            }
            catch (Exception)
            {
                // The service reads the body itself and logs what it can't.
            }
            Forward(session, SocialMessages.AddFriendRequest, body);
        }

        public void ConfirmFriend(WorldSession session, byte[] body) => Forward(session, SocialMessages.ConfirmFriendResponse, body);
        public void RemoveFriend(WorldSession session, byte[] body) => Forward(session, SocialMessages.RemoveFriendRequest, body);

        public void Ignore(WorldSession session, byte[] body)
        {
            try
            {
                var r = new GamePacketReader(body);
                ulong guid = r.ReadULong();
                r.ReadUInt(); r.ReadUInt(); r.ReadUInt();
                string name = (r.ReadString() ?? "").Trim();
                string last = (r.ReadString() ?? "").Trim();
                AnnounceCharacter(last.Length > 0 ? (name + " " + last).Trim() : name, guid);
            }
            catch (Exception)
            {
            }
            Forward(session, SocialMessages.IgnoreRequest, body);
        }

        /// <summary>
        /// The directory bridge (class summary): tells the service about the character a request names, from this
        /// world's character database, before the request itself.
        /// </summary>
        private void AnnounceCharacter(string name, ulong guid)
        {
            var database = DatabaseManager.Instance.CharacterDatabase;
            CharacterModel target = guid != 0 ? database.GetCharacterById(guid).GetAwaiter().GetResult()
                : name.Length > 0 ? database.FindCharacterByName(name) : null;
            if (target == null)
                return;
            Send(new CharacterDirectoryUpdate
            {
                Change = target.DeleteTime != null ? DirectoryChange.Deleted : DirectoryChange.Seen,
                CharacterGuid = target.Id,
                AccountId = target.AccountId,
                Name = target.Name ?? ""
            });
        }

        /// <summary>Hands a client request to the service; false (logged) when it could not be sent.</summary>
        private bool Forward(WorldSession session, uint sub, byte[] body, ushort family = SocialMessages.CommandFamily)
        {
            ulong id;
            lock (sync)
                if (!connectionOf.TryGetValue(session, out id))
                {
                    log.Debug($"Social request 0x{family:X}/0x{sub:X} from {session.CharacterName}, who has not entered the world: dropped.");
                    return false;
                }
            if (Send(new ClientRequestForward { Family = family, Sub = sub, ConnectionId = id, CharacterGuid = session.PlayerGuid, Body = body }))
                return true;
            log.Warn($"Social service unreachable: 0x{family:X}/0x{sub:X} from {session.CharacterName} dropped.");
            return false;
        }

        /// <summary>
        /// A routed chat line or room request that could not reach the service (§5.6: tells, guild, room and public lines
        /// fail with a system line; the client has no text of its own for a chat outage, L67).
        /// </summary>
        private static void ChatUnavailable(WorldSession session)
            => Chat.System(session, "Chat is unavailable right now; say, shout, emote and party chat still work.");

        /// <summary>A GuildBase request, handed on whole (the service answers the client).</summary>
        public void Guild(WorldSession session, ushort sub, byte[] body) => _ = Forward(session, sub, body, SocialMessages.GuildFamily);

        /// <summary>
        /// A chat-room request, handed on whole; a join with a room name on the game's blacklist is refused here
        /// (JoinRoomResponse 19 "inappropriate", as before) since the service has no game tables.
        /// </summary>
        public void ChatRoom(WorldSession session, ushort sub, byte[] body)
        {
            if (sub == ChatRooms.JoinRequest)
            {
                try
                {
                    string name = new GamePacketReader(body).ReadString().Trim();
                    int space = name.IndexOf(' ');
                    if (ChatRooms.IsBlacklisted(space > 0 ? name[..space] : name))
                    {
                        ChatRooms.SendResult(session, ChatRooms.JoinResponse, name, ChatRooms.RoomNameInappropriate);
                        return;
                    }
                }
                catch (Exception)
                {
                    // The service reads the body itself and logs what it can't.
                }
            }
            if (!Forward(session, sub, body, SocialMessages.ChatFamily))
                ChatUnavailable(session);
        }

        /// <summary>
        /// ChatChannelOn / Off (Command 0x22 / 0x23), handed on whole: the service keeps public channel membership for
        /// every world. The client expects no answer, so a failed forward is only logged (by <see cref="Forward"/>).
        /// </summary>
        public void ChatChannel(WorldSession session, ushort sub, byte[] body) => _ = Forward(session, sub, body);

        /// <summary>A chat line the zone has checked (body: kind, channel, to, to name, message, trailing fields).</summary>
        public void RouteChat(WorldSession sender, ChatLine line)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.WriteUShort(line.Kind);
            w.WriteUShort(line.Channel);
            w.Write(line.To);
            w.Write(line.ToName ?? "");
            w.Write(line.Message ?? "");
            w.WriteBytes(line.Rest ?? Array.Empty<byte>());
            if (!Forward(sender, SocialMessages.ChatLineSub, output.ToArray(), SocialMessages.ChatFamily))
                ChatUnavailable(sender);
        }

        /// <summary>A MailBase request: the zone's part here, the rest handed on (RemoteMail).</summary>
        public void Mail(WorldSession session, MailRequest request)
            => mail.Handle(session, request, (s, sub, body) => Forward(s, sub, body, MailMessages.MailFamily));

        public void SendSystemMail(ulong recipient, string from, string subject, string body, uint coins,
            IEnumerable<(uint Item, uint Count)> items = null, uint kind = 0)
            => mail.SystemMail(recipient, from, subject, body, coins, items, kind);

        public bool HasMailAttachment(ulong recipient, uint itemDefinition) => mail.HasAttachment(recipient, itemDefinition);

        /// <summary>One MailAttachmentQuery per definition (the service answers one at a time); each unanswered one counts as held.</summary>
        public HashSet<uint> MailAttachmentKinds(ulong recipient, IReadOnlyCollection<uint> itemDefinitions)
            => itemDefinitions.Where(d => mail.HasAttachment(recipient, d)).ToHashSet();

        // ---- Zone caches ----------------------------------------------------------------------------------------------

        /// <summary>From IgnoreListSync (the lists live in <see cref="Ignores"/>, as when local).</summary>
        public bool Ignoring(WorldSession listener, WorldSession speaker) => Ignores.Ignoring(listener, speaker);
        public bool Refuses(WorldSession target, WorldSession inviter) => Ignores.Refuses(target, inviter);

        /// <summary>From SocialStateForZone: known for characters online on this world, 0 for anyone else.</summary>
        public ulong GuildIdOf(ulong character)
        {
            lock (sync)
                return guildOf.TryGetValue(character, out var guild) ? guild.Id : 0;
        }

        /// <summary>Among the guilds of players online here (a guild with nobody here online isn't known; 0 then).</summary>
        public ulong GuildIdByName(string name)
        {
            name = name?.Trim() ?? "";
            lock (sync)
                return guildOf.Values.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)).Id;
        }

        public string GuildName(ulong guildId)
        {
            lock (sync)
                return guildOf.Values.FirstOrDefault(g => g.Id == guildId).Name;
        }

        /// <summary>Guild chat while lines go through this process: the guildmates online on this world.</summary>
        public List<WorldSession> MembersOf(WorldSession session)
        {
            ulong guild = GuildIdOf(session.PlayerGuid);
            if (guild == 0)
                return new List<WorldSession>();
            return PlayerManager.Snapshot().Where(p => GuildIdOf(p.PlayerGuid) == guild).ToList();
        }

        public List<WorldSession> OfficersOf(WorldSession session)
        {
            if (!HasFlag(session.PlayerGuid, SocialStateForZone.JoinOfficerChat))
                return new List<WorldSession>();
            return MembersOf(session).Where(p => HasFlag(p.PlayerGuid, SocialStateForZone.JoinOfficerChat)).ToList();
        }

        public bool IsMuted(WorldSession session, ushort channel)
            => HasFlag(session.PlayerGuid, channel == Guilds.ChatChannel ? SocialStateForZone.GuildChatMuted
                : channel == Guilds.OfficerChatChannel ? SocialStateForZone.OfficerChatMuted : 0);

        private bool HasFlag(ulong character, uint flag)
        {
            lock (sync)
                return flag != 0 && guildOf.TryGetValue(character, out var guild) && (guild.Flags & flag) != 0;
        }

        /// <summary>
        /// From SocialStateForZone: known for characters online on this world (pushed on entry and on every change);
        /// empty for anyone else.
        /// </summary>
        public IReadOnlyCollection<ulong> FriendOf(ulong character)
        {
            lock (sync)
                return friendOf.TryGetValue(character, out ulong[] list) ? list : Array.Empty<ulong>();
        }

        /// <summary>
        /// A character on this world is answered at once; anyone else with a PresenceQuery to the service, waiting at most
        /// <see cref="PresenceTimeout"/> (null when offline, unknown, or the service is unreachable). The wait blocks the
        /// caller: nothing on the world tick calls this yet (teleport to friend across worlds will need it).
        /// </summary>
        public SocialPresence Find(ulong character)
        {
            if (PlayerManager.FindByGuid(character) is WorldSession s)
                return new SocialPresence(s.PlayerGuid, s.CharacterName, worldId, s.ZoneId);
            var waiting = new TaskCompletionSource<PresenceReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            uint id;
            lock (sync)
            {
                id = (uint)Interlocked.Increment(ref nextQuery);
                presenceQueries[id] = waiting;
            }
            if (!Send(new PresenceQuery { RequestId = id, CharacterGuid = character }) || !waiting.Task.Wait(PresenceTimeout))
            {
                lock (sync)
                    presenceQueries.Remove(id);
                return null;
            }
            PresenceReply reply = waiting.Task.Result;
            return reply.Online ? new SocialPresence(reply.CharacterGuid, reply.Name, reply.WorldId, reply.ZoneId) : null;
        }

        // ---- Game Finder (IMatchmakingService) -------------------------------------------------------------------------

        public bool Search(ulong character, ulong flags, bool searching)
            => Send(new MatchmakingRequest { WorldId = worldId, CharacterGuid = character, Flags = flags, Searching = searching });

        public bool PublishGames(IReadOnlyList<MatchmakingGame> games)
            => Send(new MatchmakingGames { WorldId = worldId, Games = games });

        public bool TakeResync() => Interlocked.Exchange(ref matchmakingResync, 0) == 1;

        public void Dispose() => dialer.Dispose();
    }
}
