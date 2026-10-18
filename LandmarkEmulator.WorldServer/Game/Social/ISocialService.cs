using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Game.Social
{
    /// <summary>
    /// The shared social service as the zone (this world server) sees it: friends, ignore lists, guilds, the chat
    /// channels that are not range-based (tells, guild, officer, public, rooms), chat rooms and mail
    /// (trace/architecture/social-service.md §5.1). Live ran these in one service shared by every world: friends, guild
    /// and ignore requests on the client's World channel, chat lines and all mail requests on the Zone channel and
    /// handed on by the zone (channels-589.tsv, architecture-leads.md L7 / L8).
    /// <para>
    /// Today <see cref="LocalSocialService"/> runs the existing code (Friends, Ignores, Guilds, Chat, ChatRooms, Mail)
    /// in this process; a later RemoteSocialService forwards the same calls to the `social` container
    /// (social-service.md §6 steps 2-3). Every method maps to one message of the spec's §5.3 / §5.4 so the remote
    /// version needs no new seam: requests carry the player's session (the remote version takes its connection id and
    /// zone-verified character guid from it) and the client body unchanged; answers go to the client from the service.
    /// </para>
    /// The zone's own read-only questions (does X ignore Y, which guild is X in, who lists X as a friend, where is X)
    /// are the caches in <see cref="SocialServices"/> (<see cref="IIgnoreCache"/>, <see cref="IGuildCache"/>,
    /// <see cref="IFriendOfCache"/>, <see cref="IPresenceLookup"/>), fed by IgnoreListSync / SocialStateForZone /
    /// PresenceQuery once the service is remote.
    /// </summary>
    public interface ISocialService
    {
        /// <summary>
        /// The player entered the world (the gateway's CharacterEnteredWorld event, spec §5.3 / §5.5): their friends list
        /// (FriendBase 0x4D sub 1), ignore list (IgnoreBase 0x72 sub 1), guild data + ranks (GuildBase 0x83 subs 0x13 /
        /// 0x0E) and a pending guild invitation; online notes to the friends and guildmates who see them.
        /// </summary>
        void PlayerEntered(WorldSession session);

        /// <summary>
        /// The player left the world (CharacterLeftWorld): out of every chat room (RoomInfoNotification kind 5 to the
        /// others), offline notes to friends (FriendBase sub 3) and guildmates (GuildBase 0x18), ignore list dropped.
        /// </summary>
        void PlayerLeft(WorldSession session);

        /// <summary>
        /// The player is on another landscape now (the spec's ZoneChanged, §5.4: presence location for guild LastZone and
        /// friend status). Nothing to do while the service is local.
        /// </summary>
        void PlayerChangedZone(WorldSession session);

        /// <summary>
        /// A character was deleted (TASKS 6.42, <see cref="CharacterDeletion"/>): it leaves every friends list and its
        /// guild (P4624, P5311). Local: done here; remote: the service is told (CharacterDirectoryUpdate Deleted) and does
        /// it. <paramref name="name"/> is the name it had. False when it could not be done now (service unreachable):
        /// the caller tries again later. Idempotent.
        /// </summary>
        bool CharacterDeleted(ulong characterGuid, ulong accountId, string name);

        /// <summary>AddFriendRequest (Command 0x1E sub 0x10 {string name, u64 guid}).</summary>
        void AddFriend(WorldSession session, byte[] body);

        /// <summary>ConfirmFriendResponse (Command 0x1E sub 0x14 {u64 asker, accept, string name}): the answer to a request.</summary>
        void ConfirmFriend(WorldSession session, byte[] body);

        /// <summary>RemoveFriendRequest (Command 0x1E sub 0x12 {identity, u64 guid}: by name or, from the player menu, by guid).</summary>
        void RemoveFriend(WorldSession session, byte[] body);

        /// <summary>
        /// Admin /friend forceadd | remove (Command 0x1E sub 0x4CF / 0x4D0 {identity target, identity source};
        /// LandmarkEmulator.Internal.Social.AdminFriends). The caller has checked the admin flag. Done where the friends
        /// lists live (in-process, or the social service), the same changes and client packets as a normal add / remove;
        /// the outcome is logged and told to the admin (<see cref="AdminCommands.ReportFriendResult"/>) — by the remote
        /// service when its answer arrives.
        /// </summary>
        void AdminFriend(WorldSession admin, ushort sub, byte[] body);

        /// <summary>IgnoreRequest (Command 0x1E sub 0x20 {u64 guid, name block, u8 ignore / stop}).</summary>
        void Ignore(WorldSession session, byte[] body);

        /// <summary>A GuildBase 0x83 request (u16 sub, body after it); also the dev command that sends one.</summary>
        void Guild(WorldSession session, ushort sub, byte[] body);

        /// <summary>ChatBase 0x13 JoinRoomRequest (0x0A) / LeaveRoomRequest (0x0C).</summary>
        void ChatRoom(WorldSession session, ushort sub, byte[] body);

        /// <summary>
        /// Command 0x1E ChatChannelOn (0x22) / ChatChannelOff (0x23) {string channel}: join or leave a public channel
        /// (General). No answer to the client (<see cref="PublicChannels"/>).
        /// </summary>
        void ChatChannel(WorldSession session, ushort sub, byte[] body);

        /// <summary>
        /// A chat line on a channel the service routes (the PacketChat forward, spec §5.4): tell (1), guild (5),
        /// officer (6), public (9), player rooms (10) and any other channel that isn't range-based. The zone has already
        /// read the line, applied the sender's flood limit and dropped server-only channels; say / shout / emote / party
        /// stay with the zone (<see cref="Chat.Handle"/>). The service fills in the real sender, applies guild mutes and
        /// the receivers' ignore lists, and delivers.
        /// </summary>
        void RouteChat(WorldSession sender, ChatLine line);

        /// <summary>A MailBase 0xF8 request (inbox, compose, read, delete, attach, take attachments).</summary>
        void Mail(WorldSession session, MailRequest request);

        /// <summary>
        /// A mail from the game rather than a player (the spec's System mail, cMailPacketIdSystem 0xF8 / 0x16): coins
        /// (0 = none) and items attached; a kind (MailModel.Kind) makes the client write its own sender / subject / body
        /// for that kind. The recipient is told at once when online.
        /// </summary>
        void SendSystemMail(ulong recipient, string from, string subject, string body, uint coins,
            IEnumerable<(uint Item, uint Count)> items = null, uint kind = 0);

        /// <summary>Does the character have a letter still holding an item of this definition (it counts as owned)?</summary>
        bool HasMailAttachment(ulong recipient, uint itemDefinition);

        /// <summary>
        /// Which of these item definitions the character has on a letter still holding it — <see cref="HasMailAttachment"/>
        /// for several at once (the Founder's Chest, review 2026-10-18b #5). Blocking (a database read, or the social
        /// service's answers): call it off the world thread. A definition that can't be checked counts as held.
        /// </summary>
        HashSet<uint> MailAttachmentKinds(ulong recipient, IReadOnlyCollection<uint> itemDefinitions);
    }

    /// <summary>
    /// A PacketChat line as the zone read it (ChatBase 0x13 sub 1, client writer FUN_1432a93e0 / 0.695 FUN_1433562c0):
    /// kind, channel, the target's guid and first name (tells), the message, and the client's trailing block (vec4
    /// position, u64, u32 × 3, u8 × 2, then the u16 public channel id on channel 9 or the room name on channel 10;
    /// <see cref="Chat.TrailingFixedLength"/>) passed through unchanged. The client's own sender fields are
    /// not kept: the service always uses the real sender.
    /// </summary>
    public sealed record ChatLine(ushort Kind, ushort Channel, ulong To, string ToName, string Message, byte[] Rest);
}
