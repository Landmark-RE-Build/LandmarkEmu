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
    /// Chat that reaches players wherever they are (social-service.md §5.1 item 5, §6 step 5): the routed part of the
    /// world server's Game/Chat.cs and its Game/ChatRooms.cs, same client packets (traced there; ChatBase 0x13 "same"
    /// in 0.695: coverage-695.md), now across every world:
    /// <list type="bullet">
    /// <item>Tells (channel 1): to the named player on any world (by guid when the client sent one, else by name); the
    /// sender gets TellEcho (sub 5). Nobody online by that name: kind 3 "is offline" when the character exists, else
    /// kind 2 "not found". A tell to someone who ignores the sender is dropped as if delivered.</item>
    /// <item>Guild (5) / officer (6) chat: the guild's online members on any world (<see cref="GuildService.ChatAudience"/>),
    /// muted members refused.</item>
    /// <item>Chat rooms (10, /join /leave): rooms shared by all worlds, membership kept across a world change (the leave
    /// grace) and dropped when the member goes offline (P5841).</item>
    /// <item>Public channel 9: General (public id 0, the only one the 0.695 client can name, FUN_143b67f50): everyone
    /// online on every world (wiki Chat: General reaches every server and landscape) except the
    /// characters who left it (ChatChannelOff 0x1E/0x23, back with ChatChannelOn 0x22; kept until they go offline, a
    /// world change within the grace keeps it [I]). Lines naming another public id are dropped.</item>
    /// <item>Party (8, /party /p /groupsay): the sender's group on every world (<see cref="GroupService.ChatAudience"/>;
    /// a zone hands party lines on only while it is linked to this service, else it keeps them to the members it hosts).</item>
    /// </list>
    /// Say, shout and emote stay in the zone (range based). Every recipient's ignore list applies. One
    /// frame per world for every line (SocialHub.SendTo, 0x16 lists).
    /// </summary>
    public sealed class ChatService
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private static readonly byte[] ChatBase = { 0x13, 0x00 };

        private const ushort Tell = 1, GuildChat = 5, OfficerChat = 6, PartyChat = 8, PublicChannel = 9, RoomChannel = 10;
        /// <summary>
        /// The fixed part of the client's trailing block (vec4, u64, u32 × 3, u8 × 2; writer FUN_1433562c0); after it the
        /// u16 public channel id (channel 9) or the room name (channel 10). Same as the world server's Chat.TrailingFixedLength.
        /// </summary>
        private const int TrailingFixedLength = 16 + 8 + 4 + 4 + 4 + 1 + 1;
        /// <summary>General: public channel id 0, the client's name for it (FUN_143b67f50(0)).</summary>
        private const ushort GeneralId = 0;
        private const string GeneralName = "General";
        private const ushort KindMessage = 0, KindTargetNotFound = 2, KindTargetOffline = 3;
        private const ushort ChatSub = 1, TellEchoSub = 5, RoomInfo = 0x09, JoinResponse = 0x0B, LeaveResponse = 0x0D;
        private const uint Success = 1, AlreadyInRoom = 8, InvalidPassword = 12, InTooManyRooms = 15, RoomNameTooLong = 17,
            PasswordTooLong = 18, LeaveRoomNotFound = 22, LeaveNotInRoom = 23;
        private const uint KindEntered = 1, KindUsersJoined = 2, KindUserLeft = 5;
        /// <summary>Client constants Chat.PlayerPublicMaxChatRooms 10, Chat.MaxRoomNameCharLength 32, Chat.MaxRoomPasswordCharLength 32.</summary>
        private const int MaxRoomsPerPlayer = 10, MaxNameLength = 32, MaxPasswordLength = 32;

        private sealed class Room
        {
            public string Name;
            public string Password;
            public ulong Owner;
            /// <summary>Member guid → name.</summary>
            public readonly Dictionary<ulong, string> Members = new();
        }

        private readonly SocialHub hub;
        private readonly Dictionary<string, Room> rooms = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Chat rooms that exist now (work loop; the admin page's status).</summary>
        public int RoomCount => rooms.Count;

        /// <summary>Characters (guid) who left General (ChatChannelOff); everyone else online is in it.</summary>
        private readonly HashSet<ulong> leftGeneral = new();

        public ChatService(SocialHub hub) => this.hub = hub;

        /// <summary>A ChatBase request handed on by the zone: a routed line (1) or a room request (0x0A / 0x0C).</summary>
        public void Handle(WorldLink world, ClientRequestForward request)
        {
            OnlineCharacter player = hub.Presence.FindConnected(world, request.CharacterGuid, request.ConnectionId);
            if (player == null)
            {
                log.Warn($"Chat request 0x{request.Sub:X} for {request.CharacterGuid:X} on {world} without a current connection: dropped.");
                return;
            }
            try
            {
                var r = new GamePacketReader(request.Body);
                switch (request.Sub)
                {
                    case SocialMessages.ChatLineSub:
                    {
                        ushort kind = r.ReadUShort(), channel = r.ReadUShort();
                        ulong to = r.ReadULong();
                        string toName = r.ReadString(), message = r.ReadString();
                        byte[] rest = r.ReadBytes(r.BytesRemaining);
                        Route(player, kind, channel, to, toName, message, rest);
                        break;
                    }
                    case SocialMessages.JoinRoomSub:
                    {
                        string name = r.ReadString().Trim();
                        string password = r.BytesRemaining >= 4 ? r.ReadString().Trim() : "";
                        int space = name.IndexOf(' ');
                        if (password.Length == 0 && space > 0)
                            (name, password) = (name[..space], name[(space + 1)..].Trim());
                        Join(player, name, password);
                        break;
                    }
                    case SocialMessages.LeaveRoomSub:
                        Leave(player, r.ReadString().Trim());
                        break;
                }
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                log.Debug($"Unreadable chat request 0x{request.Sub:X} from {player}: {e.Message}");
            }
        }

        /// <summary>
        /// ChatChannelOn (0x22) / ChatChannelOff (0x23) {string channel} handed on by the zone (client: Lua Ui.ChannelOn /
        /// Ui.ChannelOff only): join or leave General. The client has no answer class, so nothing goes back.
        /// </summary>
        public void HandleChannel(WorldLink world, ClientRequestForward request)
        {
            OnlineCharacter player = hub.Presence.FindConnected(world, request.CharacterGuid, request.ConnectionId);
            if (player == null)
                return;
            string name;
            try
            {
                name = new GamePacketReader(request.Body).ReadString().Trim();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                log.Debug($"Unreadable chat channel request 0x{request.Sub:X} from {player}: {e.Message}");
                return;
            }
            if (!string.Equals(name, GeneralName, StringComparison.OrdinalIgnoreCase))
            {
                log.Debug($"{player} asked to switch unknown chat channel '{name}': ignored.");
                return;
            }
            bool on = request.Sub == SocialMessages.ChatChannelOn;
            if (on ? leftGeneral.Remove(player.Guid) : leftGeneral.Add(player.Guid))
                log.Info($"{player} {(on ? "joined" : "left")} {GeneralName}.");
        }

        private void Route(OnlineCharacter sender, ushort kind, ushort channel, ulong to, string toName, string message, byte[] rest)
        {
            if (channel == Tell)
            {
                SendTell(sender, kind, to, toName, message, rest);
                return;
            }
            List<OnlineCharacter> audience;
            if (channel == GuildChat || channel == OfficerChat)
            {
                audience = hub.Guilds.ChatAudience(sender.Guid, channel, out bool muted);
                if (muted)
                {
                    hub.SendTo(sender, ClientPackets.SystemLine($"You are muted in {(channel == GuildChat ? "guild" : "officer")} chat."));
                    return;
                }
            }
            else if (channel == RoomChannel)
                audience = RoomAudience(sender, RoomNameOf(rest));
            else if (channel == PartyChat)
                audience = hub.Groups.ChatAudience(sender.Guid);
            else if (channel == PublicChannel)
            {
                if (PublicChannelOf(rest) != GeneralId || leftGeneral.Contains(sender.Guid))
                {
                    log.Debug($"{sender.Name}: line on public channel {PublicChannelOf(rest)} {(leftGeneral.Contains(sender.Guid) ? "after leaving General" : "(unknown)")}: dropped.");
                    return;
                }
                audience = hub.Presence.Everyone().Where(c => !leftGeneral.Contains(c.Guid)).ToList();
            }
            else
                audience = hub.Presence.Everyone();
            log.Info($"[chat {channel}] {sender.Name}: {message}");
            hub.SendTo(audience.Where(c => !hub.Ignores.Ignoring(c.Guid, sender.Guid)),
                Build(kind, channel, sender.Guid, sender.Name, to, message, rest));
        }

        /// <summary>A tell (channel 1) to one player on any world; TellEcho (sub 5 {u64 target, name block, string, bool}) to the sender.</summary>
        private void SendTell(OnlineCharacter sender, ushort kind, ulong to, string toName, string message, byte[] rest)
        {
            string name = (toName ?? "").Trim();
            OnlineCharacter target = to != 0 ? hub.Presence.Find(to) : null;
            if (target is not { Connected: true })
            {
                SocialCharacterModel row = name.Length > 0 ? hub.Store.FindCharacterByName(name) : null;
                target = row != null ? hub.Presence.Find(row.Guid) : null;
                if (target is not { Connected: true })
                {
                    hub.SendTo(sender, Build(row != null ? KindTargetOffline : KindTargetNotFound, Tell, sender.Guid, sender.Name, 0, "", rest, name));
                    log.Info($"[tell] {sender.Name} -> {name}: {(row != null ? "offline" : "not found")}.");
                    return;
                }
            }
            if (!hub.Ignores.Ignoring(target.Guid, sender.Guid))
                hub.SendTo(target, Build(KindMessage, Tell, sender.Guid, sender.Name, target.Guid, message, rest, target.Name));
            hub.SendTo(sender, Packet(w =>
            {
                w.WriteUShort(TellEchoSub);
                w.Write(target.Guid);
                WriteName(w, target.Name);
                w.Write(message);
                w.Write(false);
            }));
            log.Info($"[tell] {sender.Name} -> {target.Name}: {message}");
        }

        // ---- Rooms ------------------------------------------------------------------------------------------------------

        private List<OnlineCharacter> RoomAudience(OnlineCharacter sender, string name)
            => rooms.TryGetValue(name ?? "", out Room room) && room.Members.ContainsKey(sender.Guid)
                ? room.Members.Keys.Select(hub.Presence.Find).Where(c => c is { Connected: true }).ToList()
                : new List<OnlineCharacter>();

        private void Join(OnlineCharacter player, string name, string password)
        {
            rooms.TryGetValue(name, out Room room);
            uint result = name.Length == 0 || name.Length > MaxNameLength ? RoomNameTooLong
                : password.Length > MaxPasswordLength ? PasswordTooLong
                : room != null && room.Members.ContainsKey(player.Guid) ? AlreadyInRoom
                : rooms.Values.Count(r => r.Members.ContainsKey(player.Guid)) >= MaxRoomsPerPlayer ? InTooManyRooms
                : room != null && room.Password.Length > 0 && room.Password != password ? InvalidPassword
                : Success;
            SendResult(player, JoinResponse, room?.Name ?? name, result);
            if (result != Success)
                return;
            if (room == null)
                rooms[name] = room = new Room { Name = name, Password = password, Owner = player.Guid };
            var others = room.Members.Keys.ToList();
            room.Members[player.Guid] = player.Name;
            hub.SendTo(player, BuildInfo(room.Name, room.Members, KindEntered));
            hub.SendTo(others.Select(hub.Presence.Find).Where(c => c is { Connected: true }),
                BuildInfo(room.Name, new Dictionary<ulong, string> { [player.Guid] = player.Name }, KindUsersJoined));
            log.Info($"{player} joined chat room '{room.Name}' ({room.Members.Count} member(s)).");
        }

        private void Leave(OnlineCharacter player, string name)
        {
            uint result = !rooms.TryGetValue(name, out Room room) ? LeaveRoomNotFound
                : !room.Members.ContainsKey(player.Guid) ? LeaveNotInRoom : Success;
            SendResult(player, LeaveResponse, room?.Name ?? name, result);
            if (result != Success)
                return;
            RemoveMember(room, player.Guid, player.Name);
            log.Info($"{player} left chat room '{room.Name}'.");
        }

        /// <summary>
        /// A member went offline (presence grace over): out of every room, the others see them leave. A world change
        /// (within the grace) keeps the rooms (P5841).
        /// </summary>
        public void WentOffline(ulong character)
        {
            leftGeneral.Remove(character);
            foreach (Room room in rooms.Values.Where(r => r.Members.ContainsKey(character)).ToList())
                RemoveMember(room, character, room.Members[character]);
        }

        private void RemoveMember(Room room, ulong character, string name)
        {
            room.Members.Remove(character);
            if (room.Members.Count == 0)
                rooms.Remove(room.Name);
            else
                hub.SendTo(room.Members.Keys.Select(hub.Presence.Find).Where(c => c is { Connected: true }),
                    BuildInfo(room.Name, new Dictionary<ulong, string> { [character] = name }, KindUserLeft));
        }

        private void SendResult(OnlineCharacter player, ushort sub, string name, uint result)
            => hub.SendTo(player, Packet(w =>
            {
                w.WriteUShort(sub);
                w.Write(name);
                w.Write(result);
            }));

        /// <summary>RoomInfoNotification (0x09): {string room, u32 n × ChatUser {u64 guid, name block}, u32 kind}.</summary>
        private static byte[] BuildInfo(string name, IReadOnlyDictionary<ulong, string> users, uint kind) => Packet(w =>
        {
            w.WriteUShort(RoomInfo);
            w.Write(name);
            w.Write((uint)users.Count);
            foreach ((ulong guid, string user) in users)
            {
                w.Write(guid);
                WriteName(w, user);
            }
            w.Write(kind);
        });

        // ---- Packets ----------------------------------------------------------------------------------------------------

        /// <summary>PacketChat (sub 1): kind, channel, sender guid, target guid, sender name, target name, message, trailing fields.</summary>
        private static byte[] Build(ushort kind, ushort channel, ulong from, string fromName, ulong to, string message, byte[] rest,
            string toName = "") => Packet(w =>
        {
            w.WriteUShort(ChatSub);
            w.WriteUShort(kind);
            w.WriteUShort(channel);
            w.Write(from);
            w.Write(to);
            WriteName(w, fromName);
            WriteName(w, toName ?? "");
            w.Write(message);
            w.WriteBytes(rest);
        });

        /// <summary>
        /// The room a channel-10 line is for: the string after the fixed trailing block (writer FUN_1433562c0; the field
        /// after the position is a u64, not a string).
        /// </summary>
        private static string RoomNameOf(byte[] rest)
        {
            try
            {
                var r = new GamePacketReader(rest);
                r.ReadBytes(TrailingFixedLength);
                return r.ReadString();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The public channel id of a channel-9 line (u16 after the fixed trailing block); ushort.MaxValue when missing.</summary>
        private static ushort PublicChannelOf(byte[] rest)
            => rest != null && rest.Length >= TrailingFixedLength + 2 ? BitConverter.ToUInt16(rest, TrailingFixedLength) : ushort.MaxValue;

        private static void WriteName(GamePacketWriter w, string first)
        {
            w.Write(0u);
            w.Write(0u);
            w.Write(0u);
            w.Write(first ?? "");
            w.Write("");
        }

        private static byte[] Packet(Action<GamePacketWriter> write)
        {
            var output = new List<byte>(ChatBase);
            write(new GamePacketWriter(output));
            return output.ToArray();
        }
    }
}
