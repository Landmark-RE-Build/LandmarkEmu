using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using Microsoft.Extensions.Configuration;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Operator broadcasts: BaseBroadcastPacket 0x4F (wire 4F 00; OPEN-LEADS L171), the client's "System Broadcast" line.
    /// <para>Client side (0.695) [P]: zone dispatcher → handler FUN_142cd9130, reader FUN_142c17410:
    /// <list type="bullet">
    /// <item>i16 kind (packet +0x10): 0 = the message alone; 1 = "System" (default format), 2 = zone, 3 = world.</item>
    /// <item>sender name block (FUN_142c25b90 → +0x18): u32, u32, u32 (+0xb8 / +0xbc / +0xc0), string first name (+0x18),
    /// string last name (+0x48); FUN_143b87120 joins them into the full name (+0x78). Either part non-empty makes it a
    /// player broadcast.</item>
    /// <item>string message (StringFixed&lt;256&gt;, +0xc8); u32 text id (+0x1e8), used only when the message is empty
    /// (FUN_142d8b190 looks the text up).</item>
    /// </list>
    /// The handler first asks CodeStringMappings (DAT_145431178 = ClientStringProvider over ./Resources/CodeStringMappings.txt,
    /// FUN_142fdd3a0) for "SystemBroadcastFormat" / "PlayerBroadcastFormat" (kind 1), "System/PlayerZoneBroadcastFormat"
    /// (kind 2) or "System/PlayerWorldBroadcastFormat" (kind 3). None of these six keys is in the 0.695 client's
    /// Resources/CodeStringMappings.txt (2,600 rows, checked in the 0.695 test copy) nor in 0.589's, so the client always
    /// takes its built-in fallback: kind 0 prints the message as it is, otherwise "System Broadcast: %s" or, with a
    /// sender, "Broadcast from %s: %s" — kinds 1-3 look the same on the retail clients. The line goes to
    /// FUN_142d557f0(client, text, 0, 1): Lua EQNScreenText:DisplayText (white on-screen text), the chat window
    /// (vtable +0x28 of client +0x326f0) and FUN_1433fe6d0.</para>
    /// <para>Live use: the client has no sender (AdminCommandPacketBroadcast* exist only as id-table names) and no source
    /// mentions an in-game broadcast, so the server sends it only when an operator asks: the dev command
    /// <c>broadcast &lt;text&gt;</c> and the admin page's Broadcast button. Shutdown warnings stay the client's own
    /// WorldShutdownNotice 0x5F countdown (the line players quoted on the forum); setting <c>ShutdownBroadcasts</c>
    /// (default false = live) also mirrors each warning step as a System Broadcast.</para>
    /// </summary>
    public static class Broadcasts
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>BaseBroadcastPacket kinds (the handler's format choice).</summary>
        public const short Plain = 0, SystemKind = 1, ZoneKind = 2, WorldKind = 3;

        /// <summary>The client keeps the message in a StringFixed&lt;256&gt;; longer text is cut there.</summary>
        public const int MaxLength = 255;

        /// <summary>ShutdownBroadcasts: each shutdown / restart warning step is also sent as a System Broadcast (false = live).</summary>
        public static bool MirrorShutdownWarnings =>
            LandmarkEmulator.Shared.Configuration.SharedConfiguration.Configuration?.GetValue("ShutdownBroadcasts", false) ?? false;

        /// <summary>
        /// The packet body after the opcode: i16 kind, sender name block {u32, u32, u32, string first, string last}, string
        /// message, u32 text id (0: the message is the text).
        /// </summary>
        public static byte[] Build(string message, short kind = SystemKind, string sender = "", uint textId = 0)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write((ushort)kind);      // i16 on the wire (Write(short) would widen to 4 bytes)
            w.Write(0u);
            w.Write(0u);
            w.Write(0u);
            w.Write(sender ?? "");
            w.Write("");
            w.Write(Fit(message, MaxLength));
            w.Write(textId);
            return output.ToArray();
        }

        /// <summary>Cuts text to at most <paramref name="maxBytes"/> UTF-8 bytes without splitting a character.</summary>
        public static string Fit(string text, int maxBytes)
        {
            text ??= "";
            if (System.Text.Encoding.UTF8.GetByteCount(text) <= maxBytes)
                return text;
            int length = text.Length;
            while (length > 0 && System.Text.Encoding.UTF8.GetByteCount(text.AsSpan(0, length)) > maxBytes)
                length--;
            if (length > 0 && char.IsHighSurrogate(text[length - 1]))
                length--;
            return text[..length];
        }

        /// <summary>Sends one broadcast to these players (one body built once).</summary>
        public static int Send(IEnumerable<WorldSession> players, string message, short kind = SystemKind, string sender = "")
        {
            byte[] body = Build(message, kind, sender);
            int sent = 0;
            foreach (WorldSession s in players.Where(p => p != null && !p.Disconnected))
            {
                s.EnqueueMessage(ZoneMessageOpcode.BroadcastBase, body);
                sent++;
            }
            log.Info($"Broadcast ({kind}{(string.IsNullOrEmpty(sender) ? "" : $", from {sender}")}) to {sent} player(s): {message}");
            return sent;
        }

        /// <summary>A System Broadcast ("System Broadcast: …") to everyone in the world.</summary>
        public static int ToEveryone(string message) => Send(PlayerManager.Snapshot(), message);
    }
}
