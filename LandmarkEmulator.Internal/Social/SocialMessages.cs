using System;
using System.Collections.Generic;
using System.IO;

namespace LandmarkEmulator.Internal.Social
{
    /// <summary>
    /// Service messages between a world server (zone, and the gateway until the gateway service exists) and the shared
    /// social service: trace/architecture/social-service.md §5.3 / §5.4. Every one travels as a
    /// <see cref="ServicePacket"/> (u16 family, u32 sub, body) on the world's link to `social` (the world dials
    /// SOCIAL_ADDRESS). Client packets from `social` to players travel as <see cref="TunnelToExternal"/> /
    /// <see cref="TunnelToExternalList"/> on the same link (the world is the gateway today, §6 step 4).
    /// <para>
    /// Family and sub: the client's ids where the message is a client request handed on (AddFriendRequest 0x1E/0x10,
    /// RemoveFriendRequest 0x1E/0x12, IgnoreRequest 0x1E/0x20: the client's Command subs, social-service.md §3.1).
    /// Every other message is <b>ours</b> (the client never sees it; live's internal layouts are not found yet, L48): it
    /// takes the family of the client area it belongs to and a sub from 0x10001 up, above every client sub id, so it
    /// can never be mistaken for a client packet. All bodies are ours, little endian, strings u32 length + UTF-8
    /// (<see cref="InternalWriter"/>).
    /// </para>
    /// </summary>
    public static class SocialMessages
    {
        /// <summary>Command (0x1E): friends / ignore requests and the presence messages (ours).</summary>
        public const ushort CommandFamily = 0x1E;
        /// <summary>FriendBase (0x4D): SocialStateForZone (ours).</summary>
        public const ushort FriendFamily = 0x4D;
        /// <summary>IgnoreBase (0x72): IgnoreListSync (ours).</summary>
        public const ushort IgnoreFamily = 0x72;
        /// <summary>GuildBase (0x83): the client's guild requests handed on (u16 sub), GuildJoined (ours).</summary>
        public const ushort GuildFamily = 0x83;
        /// <summary>
        /// ChatBase (0x13): a chat line the zone routes to the service (sub 1 PacketChat; body ours: u16 kind, u16 channel,
        /// u64 to guid, string to name, string message, then the client's trailing fields unchanged — the zone has
        /// checked length / channel and replaces the sender with the verified one), and the chat-room requests handed on
        /// whole (0x0A JoinRoomRequest {string room, string password}, 0x0C LeaveRoomRequest {string room, bool}).
        /// </summary>
        public const ushort ChatFamily = 0x13;
        public const uint ChatLineSub = 1, JoinRoomSub = 0x0A, LeaveRoomSub = 0x0C;

        // Client requests handed on (client sub ids, Command 0x1E).
        public const uint AddFriendRequest = 0x10, RemoveFriendRequest = 0x12, ConfirmFriendResponse = 0x14, IgnoreRequest = 0x20;
        /// <summary>CommandPacketChatChannelOn / Off {string channel}: join / leave a public chat channel (General).</summary>
        public const uint ChatChannelOn = 0x22, ChatChannelOff = 0x23;
        /// <summary>Admin /friend forceadd / remove (AdminCommandPacketForceAddFriendRequest / ...Remove...; AdminFriends.cs).</summary>
        public const uint AdminForceAddFriend = AdminFriends.ForceAddSub, AdminForceRemoveFriend = AdminFriends.ForceRemoveSub;

        // Ours, Command family.
        public const uint PresenceSnapshotSub = 0x10001, CharacterEnteredWorldSub = 0x10002, CharacterLeftWorldSub = 0x10003,
            ZoneChangedSub = 0x10004, CharacterDirectoryUpdateSub = 0x10005, PresenceQuerySub = 0x10006, PresenceReplySub = 0x10007,
            // The login server's character select (CharacterSelectFriends.cs).
            CharacterSelectFriendsQuerySub = 0x10008, CharacterSelectFriendsReplySub = 0x10009,
            // The outcome of an admin force-add / force-remove (AdminFriends.cs).
            AdminFriendResultSub = 0x1000A;
        // Ours, Friend family / Ignore family / Guild family.
        public const uint SocialStateForZoneSub = 0x10001, IgnoreListSyncSub = 0x10001, GuildJoinedSub = 0x10001;

        /// <summary>Wraps a body in a <see cref="ServicePacket"/>.</summary>
        public static ServicePacket ToPacket(this ISocialMessage message)
        {
            var w = new InternalWriter();
            message.Write(w);
            return new ServicePacket { Family = message.Family, Sub = message.Sub, Body = w.ToArray() };
        }

        /// <summary>The social message in a service packet, or null when it isn't one of these.</summary>
        public static ISocialMessage Parse(ServicePacket packet)
        {
            if (packet.Family == MailMessages.MailFamily)
                return MailMessages.Parse(packet);
            if (packet.Family == MatchmakingMessages.InstancedGameFamily)
                return MatchmakingMessages.Parse(packet);
            if (packet.Family == GroupMessages.GroupFamily)
                return GroupMessages.Parse(packet);
            var r = new InternalReader(packet.Body);
            return (packet.Family, packet.Sub) switch
            {
                (CommandFamily, AddFriendRequest or RemoveFriendRequest or ConfirmFriendResponse or IgnoreRequest
                    or ChatChannelOn or ChatChannelOff or AdminForceAddFriend or AdminForceRemoveFriend) => ClientRequestForward.Read(packet.Sub, r),
                (GuildFamily, GuildJoinedSub) => GuildJoined.Read(r),
                (GuildFamily, < 0x10000) => ClientRequestForward.Read(packet.Sub, r, GuildFamily),
                (ChatFamily, ChatLineSub or JoinRoomSub or LeaveRoomSub) => ClientRequestForward.Read(packet.Sub, r, ChatFamily),
                (CommandFamily, PresenceSnapshotSub) => PresenceSnapshot.Read(r),
                (CommandFamily, CharacterEnteredWorldSub) => new CharacterEnteredWorld { Character = CharacterPresence.Read(r) },
                (CommandFamily, CharacterLeftWorldSub) => CharacterLeftWorld.Read(r),
                (CommandFamily, ZoneChangedSub) => CharacterZoneChanged.Read(r),
                (CommandFamily, CharacterDirectoryUpdateSub) => CharacterDirectoryUpdate.Read(r),
                (CommandFamily, PresenceQuerySub) => PresenceQuery.Read(r),
                (CommandFamily, PresenceReplySub) => PresenceReply.Read(r),
                (CommandFamily, CharacterSelectFriendsQuerySub) => CharacterSelectFriendsQuery.Read(r),
                (CommandFamily, CharacterSelectFriendsReplySub) => CharacterSelectFriendsReply.Read(r),
                (CommandFamily, AdminFriendResultSub) => AdminFriendResult.Read(r),
                (FriendFamily, SocialStateForZoneSub) => SocialStateForZone.Read(r),
                (IgnoreFamily, IgnoreListSyncSub) => IgnoreListSync.Read(r),
                _ => null
            };
        }

        internal static void CheckCount(uint n, uint max, string what)
        {
            if (n > max)
                throw new InvalidDataException($"{n} {what} refused.");
        }
    }

    /// <summary>A social service message: its family / sub and its body writer.</summary>
    public interface ISocialMessage
    {
        ushort Family { get; }
        uint Sub { get; }
        void Write(InternalWriter w);
    }

    /// <summary>
    /// A client request the zone hands to the service (social-service.md §5.4 "conn id + sender guid (zone-verified) +
    /// the client body"): AddFriendRequest 0x1E/0x10 {string name, u64 guid}, RemoveFriendRequest 0x1E/0x12 {name block},
    /// IgnoreRequest 0x1E/0x20 {u64 guid, name block, u8 ignore / stop}. Body (ours): u64 connectionId, u64
    /// characterGuid, then the client's body after its sub, unchanged.
    /// </summary>
    public sealed class ClientRequestForward : ISocialMessage
    {
        /// <summary>Command 0x1E (friends / ignore), GuildBase 0x83 (every guild request; sub = the client's u16 sub),
        /// ChatBase 0x13 (lines, room requests), MailBase 0xF8 (inbox, read, delete, take attachments) or GroupsBase 0x2B
        /// (invite, join, leave, kick, disband, set owner; GroupMessages).</summary>
        public ushort Family { get; init; } = SocialMessages.CommandFamily;
        public uint Sub { get; init; }
        public ulong ConnectionId { get; init; }
        public ulong CharacterGuid { get; init; }
        public byte[] Body { get; init; } = Array.Empty<byte>();

        public void Write(InternalWriter w) => w.U64(ConnectionId).U64(CharacterGuid).Bytes(Body);
        public static ClientRequestForward Read(uint sub, InternalReader r, ushort family = SocialMessages.CommandFamily) => new()
        {
            Family = family, Sub = sub, ConnectionId = r.U64(), CharacterGuid = r.U64(), Body = r.Rest()
        };
    }

    /// <summary>
    /// One character in the world (ours; the spec's CharacterEnteredWorld fields, §5.3): u64 connectionId, u64
    /// characterGuid, u64 accountId, string name, u32 worldId (= gateway id while the world is its own gateway),
    /// u32 zoneId (landscape), string clientBuild, u32 zoneNameText, u32 worldNameText (the landscape's and world's name
    /// text ids, for the guild roster's Location "world:zone").
    /// </summary>
    public sealed record CharacterPresence(ulong ConnectionId, ulong CharacterGuid, ulong AccountId, string Name, uint WorldId,
        uint ZoneId, string ClientBuild, uint ZoneNameText = 0, uint WorldNameText = 0)
    {
        public void Write(InternalWriter w) => w.U64(ConnectionId).U64(CharacterGuid).U64(AccountId).Str(Name).U32(WorldId)
            .U32(ZoneId).Str(ClientBuild).U32(ZoneNameText).U32(WorldNameText);
        public static CharacterPresence Read(InternalReader r) => new(r.U64(), r.U64(), r.U64(), r.Str(), r.U32(), r.U32(), r.Str(),
            r.U32(), r.U32());
    }

    /// <summary>
    /// PresenceSnapshot (ours, 0x1E/0x10001, world → social): the first message on every (re)connected link — which
    /// world this link is and every character in it (§5.3 / §5.6). Body: u32 worldId, u32 n, n × CharacterPresence.
    /// The service treats characters of this world it had but that are missing here as having left.
    /// </summary>
    public sealed class PresenceSnapshot : ISocialMessage
    {
        public ushort Family => SocialMessages.CommandFamily;
        public uint Sub => SocialMessages.PresenceSnapshotSub;
        public uint WorldId { get; init; }
        public IReadOnlyList<CharacterPresence> Characters { get; init; } = Array.Empty<CharacterPresence>();

        public void Write(InternalWriter w)
        {
            w.U32(WorldId).U32((uint)Characters.Count);
            foreach (CharacterPresence c in Characters)
                c.Write(w);
        }

        public static PresenceSnapshot Read(InternalReader r)
        {
            uint world = r.U32(), n = r.U32();
            SocialMessages.CheckCount(n, 100_000, "snapshot entries");
            var list = new List<CharacterPresence>((int)n);
            for (uint i = 0; i < n; i++)
                list.Add(CharacterPresence.Read(r));
            return new() { WorldId = world, Characters = list };
        }
    }

    /// <summary>CharacterEnteredWorld (ours, 0x1E/0x10002, world → social): body = one CharacterPresence.</summary>
    public sealed class CharacterEnteredWorld : ISocialMessage
    {
        public ushort Family => SocialMessages.CommandFamily;
        public uint Sub => SocialMessages.CharacterEnteredWorldSub;
        public CharacterPresence Character { get; init; }

        public void Write(InternalWriter w) => Character.Write(w);
    }

    /// <summary>
    /// CharacterLeftWorld (ours, 0x1E/0x10003, world → social): u64 connectionId, u64 characterGuid, u8 reason
    /// (<see cref="CloseReason"/>). Ignored when the connection isn't the character's current one (a relog's old session).
    /// </summary>
    public sealed class CharacterLeftWorld : ISocialMessage
    {
        public ushort Family => SocialMessages.CommandFamily;
        public uint Sub => SocialMessages.CharacterLeftWorldSub;
        public ulong ConnectionId { get; init; }
        public ulong CharacterGuid { get; init; }
        public CloseReason Reason { get; init; }

        public void Write(InternalWriter w) => w.U64(ConnectionId).U64(CharacterGuid).U8((byte)Reason);
        public static CharacterLeftWorld Read(InternalReader r) => new()
        {
            ConnectionId = r.U64(), CharacterGuid = r.U64(), Reason = (CloseReason)r.U8()
        };
    }

    /// <summary>
    /// ZoneChanged (ours, 0x1E/0x10004, zone → social; spec §5.4): u64 characterGuid, u32 worldId, u32 zoneId, u32
    /// zoneNameText, u32 worldNameText — the character is on another landscape (guild LastZone, friend status later).
    /// </summary>
    public sealed class CharacterZoneChanged : ISocialMessage
    {
        public ushort Family => SocialMessages.CommandFamily;
        public uint Sub => SocialMessages.ZoneChangedSub;
        public ulong CharacterGuid { get; init; }
        public uint WorldId { get; init; }
        public uint ZoneId { get; init; }
        public uint ZoneNameText { get; init; }
        public uint WorldNameText { get; init; }

        public void Write(InternalWriter w) => w.U64(CharacterGuid).U32(WorldId).U32(ZoneId).U32(ZoneNameText).U32(WorldNameText);
        public static CharacterZoneChanged Read(InternalReader r) => new()
        {
            CharacterGuid = r.U64(), WorldId = r.U32(), ZoneId = r.U32(), ZoneNameText = r.U32(), WorldNameText = r.U32()
        };
    }

    /// <summary>
    /// GuildJoined (ours, 0x83/0x10001, social → zone; spec §5.4): u64 characterGuid, u64 guildId — the character created
    /// or joined a guild: the zone runs what depends on it there (the "join a guild" goal, Achievements.GuildJoined).
    /// </summary>
    public sealed class GuildJoined : ISocialMessage
    {
        public ushort Family => SocialMessages.GuildFamily;
        public uint Sub => SocialMessages.GuildJoinedSub;
        public ulong CharacterGuid { get; init; }
        public ulong GuildId { get; init; }

        public void Write(InternalWriter w) => w.U64(CharacterGuid).U64(GuildId);
        public static GuildJoined Read(InternalReader r) => new() { CharacterGuid = r.U64(), GuildId = r.U64() };
    }

    /// <summary>What a <see cref="CharacterDirectoryUpdate"/> says happened.</summary>
    public enum DirectoryChange : byte { Seen = 1, Created = 2, Renamed = 3, Deleted = 4 }

    /// <summary>
    /// CharacterDirectoryUpdate (ours, 0x1E/0x10005; the spec's CharacterCreated / Renamed / Deleted in one message,
    /// §5.4): u8 change, u64 characterGuid, u64 accountId, string name. Meant for the login service (create / rename /
    /// delete, a hand-off); until it sends them, a world sends <see cref="DirectoryChange.Seen"/> for the character a
    /// friend / ignore request names, read from its character database, so characters made after the one-time import
    /// are found (the bridge is noted in RemoteSocialService).
    /// </summary>
    public sealed class CharacterDirectoryUpdate : ISocialMessage
    {
        public ushort Family => SocialMessages.CommandFamily;
        public uint Sub => SocialMessages.CharacterDirectoryUpdateSub;
        public DirectoryChange Change { get; init; }
        public ulong CharacterGuid { get; init; }
        public ulong AccountId { get; init; }
        public string Name { get; init; } = "";

        public void Write(InternalWriter w) => w.U8((byte)Change).U64(CharacterGuid).U64(AccountId).Str(Name);
        public static CharacterDirectoryUpdate Read(InternalReader r) => new()
        {
            Change = (DirectoryChange)r.U8(), CharacterGuid = r.U64(), AccountId = r.U64(), Name = r.Str()
        };
    }

    /// <summary>PresenceQuery (ours, 0x1E/0x10006, zone → social; §5.4): u32 requestId, u64 characterGuid.</summary>
    public sealed class PresenceQuery : ISocialMessage
    {
        public ushort Family => SocialMessages.CommandFamily;
        public uint Sub => SocialMessages.PresenceQuerySub;
        public uint RequestId { get; init; }
        public ulong CharacterGuid { get; init; }

        public void Write(InternalWriter w) => w.U32(RequestId).U64(CharacterGuid);
        public static PresenceQuery Read(InternalReader r) => new() { RequestId = r.U32(), CharacterGuid = r.U64() };
    }

    /// <summary>
    /// PresenceReply (ours, 0x1E/0x10007, social → zone): u32 requestId, u64 characterGuid, bool online, string name,
    /// u32 worldId, u32 zoneId (world / zone 0 when offline).
    /// </summary>
    public sealed class PresenceReply : ISocialMessage
    {
        public ushort Family => SocialMessages.CommandFamily;
        public uint Sub => SocialMessages.PresenceReplySub;
        public uint RequestId { get; init; }
        public ulong CharacterGuid { get; init; }
        public bool Online { get; init; }
        public string Name { get; init; } = "";
        public uint WorldId { get; init; }
        public uint ZoneId { get; init; }

        public void Write(InternalWriter w) => w.U32(RequestId).U64(CharacterGuid).Bool(Online).Str(Name).U32(WorldId).U32(ZoneId);
        public static PresenceReply Read(InternalReader r) => new()
        {
            RequestId = r.U32(), CharacterGuid = r.U64(), Online = r.Bool(), Name = r.Str(), WorldId = r.U32(), ZoneId = r.U32()
        };
    }

    /// <summary>
    /// IgnoreListSync (ours, 0x72/0x10001, social → zone; §5.4): the character's whole ignore list for the zone's own
    /// checks (say / shout / emote / party lines, emotes, trade, group and guild invitations, mail). Body: u64
    /// characterGuid, u32 n, n × {u64 ignoredGuid, string name}. Sent on world entry and after every change; the list
    /// is at most 100 entries, so always whole (no deltas).
    /// </summary>
    public sealed class IgnoreListSync : ISocialMessage
    {
        public ushort Family => SocialMessages.IgnoreFamily;
        public uint Sub => SocialMessages.IgnoreListSyncSub;
        public ulong CharacterGuid { get; init; }
        public IReadOnlyList<(ulong Guid, string Name)> Ignored { get; init; } = Array.Empty<(ulong, string)>();

        public void Write(InternalWriter w)
        {
            w.U64(CharacterGuid).U32((uint)Ignored.Count);
            foreach ((ulong guid, string name) in Ignored)
                w.U64(guid).Str(name);
        }

        public static IgnoreListSync Read(InternalReader r)
        {
            ulong character = r.U64();
            uint n = r.U32();
            SocialMessages.CheckCount(n, 10_000, "ignore entries");
            var list = new List<(ulong, string)>((int)n);
            for (uint i = 0; i < n; i++)
                list.Add((r.U64(), r.Str()));
            return new() { CharacterGuid = character, Ignored = list };
        }
    }

    /// <summary>
    /// SocialStateForZone (ours, 0x4D/0x10001, social → zone; §5.4): what the zone needs to know about a character it
    /// hosts. Body: u64 characterGuid, u64 guildId, u32 guildFlags (JoinOfficerChat / GuildChatMuted / OfficerChatMuted:
    /// what the zone's guild chat needs while chat lines still go through the zone), u32 n, n × u64 friendOf (the
    /// characters who list this one as a friend: claim "friends" mask, client claim +0x2d4), string guildName. Sent on
    /// world entry and whenever the friend-of list or the guild membership / rank / mutes change.
    /// </summary>
    public sealed class SocialStateForZone : ISocialMessage
    {
        public ushort Family => SocialMessages.FriendFamily;
        public uint Sub => SocialMessages.SocialStateForZoneSub;
        public ulong CharacterGuid { get; init; }
        public ulong GuildId { get; init; }
        public uint GuildRankPermissions { get; init; }
        public IReadOnlyList<ulong> FriendOf { get; init; } = Array.Empty<ulong>();
        public string GuildName { get; init; } = "";

        /// <summary>GuildRankPermissions bits: the rank has JoinOfficerChat; muted in guild chat (5); muted in officer chat (6).</summary>
        public const uint JoinOfficerChat = 1, GuildChatMuted = 2, OfficerChatMuted = 4;

        public void Write(InternalWriter w)
        {
            w.U64(CharacterGuid).U64(GuildId).U32(GuildRankPermissions).U32((uint)FriendOf.Count);
            foreach (ulong guid in FriendOf)
                w.U64(guid);
            w.Str(GuildName);
        }

        public static SocialStateForZone Read(InternalReader r)
        {
            ulong character = r.U64(), guild = r.U64();
            uint permissions = r.U32(), n = r.U32();
            SocialMessages.CheckCount(n, 1_000_000, "friend-of entries");
            var list = new List<ulong>((int)n);
            for (uint i = 0; i < n; i++)
                list.Add(r.U64());
            return new() { CharacterGuid = character, GuildId = guild, GuildRankPermissions = permissions, FriendOf = list, GuildName = r.Str() };
        }
    }
}
