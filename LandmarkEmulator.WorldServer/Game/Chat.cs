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
    /// within <see cref="SayRange"/>, everything else to everyone online.
    /// </summary>
    public static class Chat
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const ushort Say = 0, Tell = 1, Party = 8;

        /// <summary>
        /// PacketChat kinds (the first u16 after the sub, client FUN_1432b7bc0 case 1): 0 a message; 2 "User '[target]' not
        /// found." (UI.ChatPacketInvalidTarget), 3 "User '[target]' is offline." (Chat.WhisperTargetOffline) — both
        /// about the second name block (+0x188).
        /// </summary>
        private const ushort KindMessage = 0, KindTargetNotFound = 2, KindTargetOffline = 3;

        /// <summary>TellEcho (ChatBase sub 5, reader FUN_1432aa700, handler FUN_1432c0f90): the sender's "To [X]: …" line.</summary>
        private const ushort TellEchoSub = 5;
        private const float SayRange = 100f;
        private const int MaxMessageLength = 512;

        /// <summary>
        /// A chat message from a client (0x13 sub 1). The sender fields the client wrote are ignored and replaced with the
        /// real sender, then the message goes to its audience: say within 100 m, party (8) to the group, guild (5) to the
        /// guild, other channels to everyone. Over-long or empty messages are dropped.
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
                string message = reader.ReadString();
                byte[] rest = reader.ReadBytes(reader.BytesRemaining);
                if (string.IsNullOrWhiteSpace(message) || message.Length > MaxMessageLength)
                    return;
                if (channel == Tell)
                {
                    SendTell(sender, kind, to, toName, message, rest);
                    return;
                }
                log.Info($"[chat {channel}] {sender.CharacterName}: {message}");

                byte[] relay = Build(kind, channel, sender, to, message, rest);
                IEnumerable<WorldSession> recipients = channel == Party ? Groups.MembersOf(sender)
                    : channel == Guilds.ChatChannel ? Guilds.MembersOf(sender)
                    : channel == ChatRooms.RoomChannel ? ChatRooms.MembersFor(sender, RoomNameOf(rest))
                    : PlayerManager.Snapshot();
                foreach (WorldSession session in recipients)
                    if (Ignores.Ignoring(session, sender))
                        continue;
                    else if (channel != Say || (session.ZoneId == sender.ZoneId
                            && Vector3.Distance(session.Position, sender.Position) <= SayRange))
                        session.EnqueueMessage(ZoneMessageOpcode.ChatBase, relay);
            }
            catch (Exception e)
            {
                log.Debug($"Unreadable chat from {sender.CharacterName}: {e.Message} {BitConverter.ToString(body)}");
            }
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
        /// manager (FUN_1432b7bc0 case 7 → *DAT_145352490 vtable +0x38) instead of a "says" line.
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
        /// The room a channel-10 message is for: the last field of the client's trailing block (writer FUN_1432a93e0:
        /// vec4 position, string, u32, u32, u32, u8, u8, then the room name for channel 10). Null when unreadable.
        /// </summary>
        private static string RoomNameOf(byte[] rest)
        {
            try
            {
                var r = new GamePacketReader(rest);
                r.ReadBytes(16);
                r.ReadString();
                r.ReadUInt();
                r.ReadUInt();
                r.ReadUInt();
                r.ReadByte();
                r.ReadByte();
                return r.ReadString();
            }
            catch
            {
                return null;
            }
        }

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
