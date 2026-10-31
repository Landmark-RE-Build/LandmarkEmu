using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Player chat (ChatBase 0x13 sub 1 PacketChat; client writer FUN_1432a93e0). Body after the sub: u16, u16 channel,
    /// u64 from guid, u64 to guid, name from {u32, u32, u32, string first, string last}, name to (same), string
    /// message, vec4 position, then trailing fields the server passes through unchanged.
    /// The server fills in the real sender (never trusting the client's) and relays: channel 0 (say) to players
    /// within <see cref="SayRange"/>, everything else to everyone online. Say / shout / emote / party are the zone's;
    /// the other channels go through the social service (<see cref="Social.ISocialService.RouteChat"/>).
    /// </summary>
    public static class Chat
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Wire channel ids = ChatChannels.txt row - 1: 0 Say, 1 Tell, 2 TellEcho, 3 Shout, 4 Emote, 5 Guild, 6 Officers,
        /// 7 System, 8 Party, 9 Public, 10 PlayerPublic (chat rooms), 11 Error, 12 Loot, 13 Combat. The client's chat
        /// commands (FUN_1432ac6b0) send Say, Tell, Shout (/shout /yell), Emote (/emote /em /me), Guild, Officers (/officer),
        /// Party (/party /p /groupsay) and the public / room channels; TellEcho, System, Error, Loot and Combat are lines
        /// only the server or the client itself makes, so a client sending them is dropped.
        /// </summary>
        public const ushort Say = 0, Tell = 1, Shout = 3, Emote = 4, Party = 8, Public = 9;
        private static readonly HashSet<ushort> ServerOnlyChannels = new() { 2, SystemChannelId, 11, 12, 13 };

        /// <summary>
        /// The client's trailing block after the message (writer FUN_1433562c0, reader FUN_143356a90; same in 0.589
        /// FUN_1432a93e0): vec4 position (+0x460), u64 (+0x478), u32 (+0x484), u32 (+0x480), u32 (+0x470), u8 (+0x238),
        /// u8 (+0x488) — 38 bytes — then, on channel 9 only, a u16 public channel id (+0x20) and, on channel 10 only, the
        /// room name string (+0x28).
        /// </summary>
        public const int TrailingFixedLength = 16 + 8 + 4 + 4 + 4 + 1 + 1;

        /// <summary>
        /// The public channels (wire channel 9, ChatChannels.txt row 10 "Public"): a line names its channel by the u16
        /// after the fixed trailing block, and the client turns it into a name with FUN_143b67f50, which knows exactly one:
        /// 0 = "General" (any other id gives no name). The client registers General itself at every login (chat processor
        /// setup FUN_143367e40 → FUN_14335e8e0(9, 0, "General"): numbered slot 1, so "/1" talks in it; the default tab
        /// shows channel 9, and a separate "General" tab follows the ChatGeneralChannelSeperateTabEnabled setting) — the
        /// server sends nothing to "add" it.
        /// </summary>
        public const ushort GeneralChannelId = 0;
        public const string GeneralChannelName = "General";

        /// <summary>
        /// PacketChat kinds (the first u16 after the sub, client FUN_1432b7bc0 case 1): 0 a message; 2 "User '[target]' not
        /// found." (UI.ChatPacketInvalidTarget), 3 "User '[target]' is offline." (Chat.WhisperTargetOffline) — both
        /// about the second name block (+0x188).
        /// </summary>
        private const ushort KindMessage = 0, KindTargetNotFound = 2, KindTargetOffline = 3;

        /// <summary>TellEcho (ChatBase sub 5, reader FUN_1432aa700, handler FUN_1432c0f90): the sender's "To [X]: …" line.</summary>
        private const ushort TellEchoSub = 5;
        private const float SayRange = 100f;
        /// <summary>
        /// /shout and /yell: a message to the zone, within a limited range (2016 wiki, Commands List). The
        /// client has no range constant for it; the server uses a wide radius in the sender's zone.
        /// </summary>
        private const float ShoutRange = 500f;
        private const int MaxMessageLength = 512;

        /// <summary>
        /// A chat message from a client (0x13 sub 1). The sender fields the client wrote are ignored and replaced with the
        /// real sender, then the message goes to its audience: say and emotes within 100 m, shouts within 500 m of the
        /// zone, party (8) to the group, guild (5) to the guild, officers (6) to ranks with JoinOfficerChat, rooms (10) to
        /// the room, other channels to everyone. Over-long or empty messages, server-only channels and muted guild
        /// members' guild lines are dropped.
        /// The zone keeps the range-based channels (say, shout, emote) and party; every other channel is handed to the
        /// social service (<see cref="Social.ISocialService.RouteChat"/>, the PacketChat forward of social-service.md
        /// §5.4), which runs <see cref="Route"/> while it is local.
        /// </summary>
        public static void Handle(WorldSession sender, byte[] body)
        {
            // Flood guard: a burst of 5 lines, then one every 1.5 s.
            if (!sender.ChatLimiter.TryTake())
            {
                if (sender.ChatWarnLimiter.TryTake())
                    System(sender, "You are sending messages too quickly.");
                return;
            }
            try
            {
                var reader = new GamePacketReader(body);
                ushort kind = reader.ReadUShort();
                ushort channel = reader.ReadUShort();
                reader.ReadULong();
                ulong to = reader.ReadULong();
                SkipName(reader);
                string toName = ReadFirstName(reader);
                // Typed rich-text tokens would become markup in every reader's chat window (L54): made harmless.
                string message = Shared.Game.RichTextTokens.Neutralize(reader.ReadString());
                byte[] rest = reader.ReadBytes(reader.BytesRemaining);
                if (string.IsNullOrWhiteSpace(message) || message.Length > MaxMessageLength)
                    return;
                if (ServerOnlyChannels.Contains(channel))
                {
                    log.Debug($"{sender.CharacterName} sent chat on server-only channel {channel}: dropped.");
                    return;
                }
                // A public line must name a channel the clients can show: only General (0) has a name (FUN_143b67f50).
                if (channel == Public && PublicChannelOf(rest) != GeneralChannelId)
                {
                    log.Debug($"{sender.CharacterName} sent chat on unknown public channel {PublicChannelOf(rest)}: dropped.");
                    return;
                }
                // Party lines reach the group on every world through the social service while groups live there and
                // it is reachable (B.9, social-service.md §10); otherwise the zone keeps them to the members it hosts.
                if (!ZoneChannels.Contains(channel)
                    || (channel == Party && Social.SocialServices.Groups is { IsConnected: true }))
                {
                    Social.SocialServices.Service.RouteChat(sender, new Social.ChatLine(kind, channel, to, toName, message, rest));
                    return;
                }
                log.Info($"[chat {channel}] {sender.CharacterName}: {message}");

                byte[] relay = Build(kind, channel, sender, to, message, rest);
                IEnumerable<WorldSession> recipients = channel == Party ? Groups.MembersOf(sender) : PlayerManager.Snapshot();
                // Say and emotes are heard nearby, shouts across a wide radius of the zone, party by the whole group.
                float range = channel == Say || channel == Emote ? SayRange : channel == Shout ? ShoutRange : float.MaxValue;
                foreach (WorldSession session in recipients)
                    if (Social.SocialServices.IgnoreCache.Ignoring(session, sender))
                        continue;
                    else if (range == float.MaxValue || (session.ZoneId == sender.ZoneId
                            && Vector3.Distance(session.Position, sender.Position) <= range))
                        session.EnqueueMessage(ZoneMessageOpcode.ChatBase, relay);
            }
            catch (Exception e)
            {
                log.Debug($"Unreadable chat from {sender.CharacterName}: {e.Message} {BitConverter.ToString(body)}");
            }
        }

        /// <summary>Channels the zone delivers itself (by range, or to the party); the rest go to the social service.</summary>
        private static readonly HashSet<ushort> ZoneChannels = new() { Say, Shout, Emote, Party };

        /// <summary>
        /// The social side of a chat line (<see cref="Social.LocalSocialService.RouteChat"/>): tells to the named player,
        /// guild (5) to the guild, officers (6) to ranks with JoinOfficerChat, rooms (10) to the room, General (public 9)
        /// to the online players who have not left it (<see cref="PublicChannels"/>), other channels to everyone online;
        /// muted guild members' guild lines are dropped and the receivers' ignore lists apply.
        /// </summary>
        internal static void Route(WorldSession sender, Social.ChatLine line)
        {
            ushort channel = line.Channel;
            if (channel == Tell)
            {
                SendTell(sender, line.Kind, line.To, line.ToName, line.Message, line.Rest);
                return;
            }
            Social.IGuildCache guilds = Social.SocialServices.GuildCache;
            if ((channel == Guilds.ChatChannel || channel == Guilds.OfficerChatChannel) && guilds.IsMuted(sender, channel))
            {
                System(sender, $"You are muted in {(channel == Guilds.ChatChannel ? "guild" : "officer")} chat.");
                return;
            }
            if (channel == Public && !PublicChannels.IsMember(sender))
            {
                // Someone who left General talks in it only after joining again [I] (no client text for this case).
                log.Debug($"{sender.CharacterName} spoke in {GeneralChannelName} after leaving it: dropped.");
                return;
            }
            log.Info($"[chat {channel}] {sender.CharacterName}: {line.Message}");

            byte[] relay = Build(line.Kind, channel, sender, line.To, line.Message, line.Rest);
            IEnumerable<WorldSession> recipients = channel == Guilds.ChatChannel ? guilds.MembersOf(sender)
                : channel == Guilds.OfficerChatChannel ? guilds.OfficersOf(sender)
                : channel == ChatRooms.RoomChannel ? ChatRooms.MembersFor(sender, RoomNameOf(line.Rest))
                : channel == Public ? PlayerManager.Snapshot().Where(PublicChannels.IsMember)
                : PlayerManager.Snapshot();
            // These channels reach everyone they are for, wherever they are.
            foreach (WorldSession session in recipients)
                if (!Ignores.Ignoring(session, sender))
                    session.EnqueueMessage(ZoneMessageOpcode.ChatBase, relay);
        }

        /// <summary>
        /// A creature speaks (a Story Node's text): a say line from the creature's guid and name to the players within
        /// <see cref="SayRange"/> of it.
        /// </summary>
        public static void NpcSay(ulong guid, string name, uint zone, Vector3 position, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;
            byte[] tail = new byte[16 + 8 + 4 + 4 + 4 + 1 + 1];
            BitConverter.GetBytes(position.X).CopyTo(tail, 0);
            BitConverter.GetBytes(position.Y).CopyTo(tail, 4);
            BitConverter.GetBytes(position.Z).CopyTo(tail, 8);
            BitConverter.GetBytes(1f).CopyTo(tail, 12);
            BitConverter.GetBytes(uint.MaxValue).CopyTo(tail, 16 + 8 + 8);
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.WriteUShort(1);
            writer.WriteUShort(0);
            writer.WriteUShort(Say);
            writer.Write(guid);
            writer.Write(0ul);
            WriteName(writer, name ?? "");
            WriteName(writer, "");
            writer.Write(message.Length > MaxMessageLength ? message[..MaxMessageLength] : message);
            writer.WriteBytes(tail);
            byte[] packet = output.ToArray();
            foreach (WorldSession session in PlayerManager.InZone(zone))
                if (Vector3.Distance(session.Position, position) <= SayRange)
                    session.EnqueueMessage(ZoneMessageOpcode.ChatBase, packet);
            log.Info($"[npc say] {name}: {message}");
        }

        /// <summary>
        /// A chat line on the client's System channel (7, Chat.SystemFormat): the receiver hands it to the notification
        /// manager (FUN_1432b7bc0 case 7 → *DAT_145352490 (0.589; 0.695 DAT_145431a10) vtable +0x38) instead of a
        /// "says" line.
        /// </summary>
        public static void SystemChannel(WorldSession player, string message)
        {
            byte[] tail = new byte[16 + 8 + 4 + 4 + 4 + 1 + 1];
            BitConverter.GetBytes(1f).CopyTo(tail, 12);
            BitConverter.GetBytes(uint.MaxValue).CopyTo(tail, 16 + 8 + 8);
            player.EnqueueMessage(ZoneMessageOpcode.ChatBase, Build(0, SystemChannelId, null, player.PlayerGuid, message, tail, ""));
        }

        public const ushort SystemChannelId = 7;

        /// <summary>
        /// A system line to one player (e.g. why an action was refused) through the client's own system-message path
        /// (<see cref="SystemMessages"/>: PacketShowSystemMessage with client text 558682 "[*message*]"), so it shows like
        /// the client's own system lines. It used to be a Say line from "Server" ("[Server] says: ..."). Prefer
        /// <see cref="SystemMessages.Show"/> with the client's own text id wherever the client has one for the situation.
        /// </summary>
        public static void System(WorldSession player, string message)
            => SystemMessages.Show(player, PlainMessageText, ("message", message));

        /// <summary>Client text 558682: just "[*message*]".</summary>
        public const uint PlainMessageText = 558682;

        /// <summary>
        /// A tell (/whisper, /tell; channel 1, the client's "Chat.WhisperFormat" "[X] whispers: …"): only the named player
        /// gets it (by guid when the client sent one, else by name), and the sender gets TellEcho (sub 5: {u64 target,
        /// name block, string message, bool}, "Chat.WhisperEchoFormat" "To [X]: …"). A name nobody online has answers
        /// kind 3 "is offline" when such a character exists, else kind 2 "not found". A tell to someone who ignores the
        /// sender is dropped as if delivered (the echo still shows), so ignoring stays invisible.
        /// </summary>
        private static void SendTell(WorldSession sender, ushort kind, ulong to, string toName, string message, byte[] rest)
        {
            string name = (toName ?? "").Trim();
            WorldSession target = (to != 0 ? PlayerManager.FindByGuid(to) : null)
                ?? PlayerManager.Snapshot().FirstOrDefault(s => !s.Disconnected
                    && string.Equals(s.CharacterName, name, StringComparison.OrdinalIgnoreCase));
            if (target == null || target.Disconnected)
            {
                bool exists = name.Length > 0 && DatabaseManager.Instance.CharacterDatabase.FindCharacterByName(name) != null;
                sender.EnqueueMessage(ZoneMessageOpcode.ChatBase,
                    Build(exists ? KindTargetOffline : KindTargetNotFound, Tell, sender, 0, "", rest, name));
                log.Info($"[tell] {sender.CharacterName} -> {name}: {(exists ? "offline" : "not found")}.");
                return;
            }
            if (!Ignores.Ignoring(target, sender))
                target.EnqueueMessage(ZoneMessageOpcode.ChatBase,
                    Build(KindMessage, Tell, sender, target.PlayerGuid, message, rest, target.CharacterName));
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.WriteUShort(TellEchoSub);
            writer.Write(target.PlayerGuid);
            WriteName(writer, target.CharacterName);
            writer.Write(message);
            writer.Write(false);
            sender.EnqueueMessage(ZoneMessageOpcode.ChatBase, output.ToArray());
            log.Info($"[tell] {sender.CharacterName} -> {target.CharacterName}: {message}");
        }

        /// <summary>
        /// Writes a chat packet body: kind, channel, sender guid, target guid, sender name, the target's name (empty for
        /// channel messages), the message and the client's trailing fields (position etc.) passed through.
        /// </summary>
        private static byte[] Build(ushort kind, ushort channel, WorldSession sender, ulong to, string message, byte[] rest,
            string toName = "")
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.WriteUShort(1);
            writer.WriteUShort(kind);
            writer.WriteUShort(channel);
            writer.Write(sender?.PlayerGuid ?? 0ul);
            writer.Write(to);
            WriteName(writer, sender?.CharacterName ?? "Server");
            WriteName(writer, toName ?? "");
            writer.Write(message);
            writer.WriteBytes(rest);
            return output.ToArray();
        }

        /// <summary>
        /// The room a channel-10 message is for: the string after the fixed trailing block (<see cref="TrailingFixedLength"/>;
        /// the second field there is a u64, not a string — read as a string it shifted every later field by 4 bytes and
        /// lost the room name). Null when unreadable.
        /// </summary>
        private static string RoomNameOf(byte[] rest)
        {
            try
            {
                var r = new GamePacketReader(rest);
                r.ReadBytes(TrailingFixedLength);
                return r.ReadString();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The public channel a channel-9 message is for: the u16 after the fixed trailing block (client reader
        /// FUN_143356a90 → +0x20). <see cref="ushort.MaxValue"/> when missing (the client's default +0x20 is -1).
        /// </summary>
        public static ushort PublicChannelOf(byte[] rest)
            => rest != null && rest.Length >= TrailingFixedLength + 2 ? BitConverter.ToUInt16(rest, TrailingFixedLength) : ushort.MaxValue;

        /// <summary>Reads a name block {u32, u32, u32, string first, string last}, returning the first name.</summary>
        private static string ReadFirstName(GamePacketReader reader)
        {
            reader.ReadUInt();
            reader.ReadUInt();
            reader.ReadUInt();
            string first = reader.ReadString();
            reader.ReadString();
            return first;
        }

        /// <summary>
        /// Skips a name block {u32, u32, u32, string first, string last}.
        /// </summary>
        private static void SkipName(GamePacketReader reader)
        {
            reader.ReadUInt();
            reader.ReadUInt();
            reader.ReadUInt();
            reader.ReadString();
            reader.ReadString();
        }

        /// <summary>
        /// Writes a name block {u32 0, u32 0, u32 0, string first, string last ""}.
        /// </summary>
        private static void WriteName(GamePacketWriter writer, string first)
        {
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(first);
            writer.Write("");
        }
    }
}
