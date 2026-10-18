using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// A character's chat bubble colours and size (TASKS E.14, trace/0695/agent-chat.md §2). Both ways the packet is
    /// CommandPacketSetChatBubbleColor, Command 0x1E sub 0x15: {u32 foreground, u32 background, u32 size, u64 guid}.
    /// <list type="bullet">
    /// <item>C→S (Zone channel): Lua Ui.SetChatColorFgBgSz(fg, bg, size) FUN_1435d2a20 → FUN_142d4c740 sends it with the
    /// player's own guid and writes the three values straight into the local character entity (+0x219c fg, +0x21a0 bg,
    /// +0x21a4 size); writer FUN_142c13c00. No shipped script calls that native (only the Lua binding table points at
    /// it), so the unmodified 0.695 client never sends this today.</item>
    /// <item>S→C: the Command dispatcher FUN_1433baf00 case 0x15 → FUN_1433bbef0 reads the same layout (FUN_1433833e0)
    /// and FUN_142d5d290 writes the values into the entity with that guid. Characters loaded later get them in
    /// LightweightToFullPc (2nd-4th u32 of the character blob).</item>
    /// <item>Where the client draws them is not found yet: the overhead speech bubble (SpeechBubbleWidget,
    /// "Character.ActiveChatBubbles" rows from FUN_142ece660) carries position and text only, and no reader of the entity
    /// fields shows in the decompile (open lead, agent-chat.md).</item>
    /// </list>
    /// The server never trusts the guid the client wrote: the colours are the sender's. They are saved on the character
    /// and sent to the other players on the landscape only when they change (one 22-byte packet each).
    /// </summary>
    public static class ChatBubbleColor
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const ushort Sub = 0x15;
        /// <summary>The client's defaults for a new character entity (FUN_142e88840 → FUN_143b94310 / FUN_143b941a0, 1).</summary>
        public const uint DefaultForeground = 0xFFFFFF, DefaultBackground = 0xD4E2F0, DefaultSize = 1;

        /// <summary>The player set new bubble colours: saved, and shown to everyone else on the landscape.</summary>
        public static void Handle(WorldSession session, byte[] body)
        {
            uint foreground, background, size;
            try
            {
                var reader = new GamePacketReader(body);
                foreground = reader.ReadUInt();
                background = reader.ReadUInt();
                size = reader.ReadUInt();
                reader.ReadULong();          // the client's own guid (DAT_145430c58): ignored, the sender is the session
            }
            catch (Exception e)
            {
                log.Debug($"Unreadable chat bubble colour from {session.CharacterName}: {e.Message} {BitConverter.ToString(body)}");
                return;
            }
            if (session.ChatBubble == (foreground, background, size))
                return;
            session.SetChatBubble(foreground, background, size);
            log.Info($"{session.CharacterName} set chat bubble colours fg 0x{foreground:X6}, bg 0x{background:X6}, size {size}.");
            // The sender's client already applied them to its own entity (FUN_142d4c740).
            byte[] packet = Build(session.PlayerGuid, foreground, background, size);
            foreach (WorldSession other in PlayerManager.InZone(session.ZoneId))
                if (other != session)
                    other.EnqueueMessage(ZoneMessageOpcode.CommandBase, packet);
        }

        /// <summary>
        /// At ClientIsReady (the player's entity exists): the player's own saved colours, which the client keeps only on the
        /// entity and so forgets between logins. Nothing is sent while they are the client's defaults.
        /// </summary>
        public static void SendOwn(WorldSession session)
        {
            (uint foreground, uint background, uint size) = session.ChatBubble;
            if ((foreground, background, size) != (DefaultForeground, DefaultBackground, DefaultSize))
                session.EnqueueMessage(ZoneMessageOpcode.CommandBase, Build(session.PlayerGuid, foreground, background, size));
        }

        /// <summary>CommandPacketSetChatBubbleColor body after the family: u16 sub, u32 fg, u32 bg, u32 size, u64 guid.</summary>
        private static byte[] Build(ulong guid, uint foreground, uint background, uint size)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.WriteUShort(Sub);
            writer.Write(foreground);
            writer.Write(background);
            writer.Write(size);
            writer.Write(guid);
            return output.ToArray();
        }
    }
}
