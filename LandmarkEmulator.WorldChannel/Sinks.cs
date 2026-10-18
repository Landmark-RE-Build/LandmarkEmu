using LandmarkEmulator.Internal;
using NLog;
using System.Collections.Concurrent;
using System.Text;

namespace LandmarkEmulator.WorldChannel
{
    /// <summary>
    /// Channel-1 messages that end at the world channel (world-channel-service.md §3.6, §8 step 3): nothing answers them
    /// and the zone has no use for them, so they are taken here and not handed on. Packets arrive as the 0.695 client
    /// sent them (docs/protocol.md §1.6: PlayerUpdate with its u32 sub).
    /// <list type="bullet">
    /// <item>PacketSetLocale 0x5B (StringFixed&lt;8&gt;, once per world entry; no S→C SetLocale exists): kept per
    /// connection.</item>
    /// <item>PlayerUpdatePacketUpdateSteamInfo 0x26/0x68 (the client's Steam id; no account link here).</item>
    /// <item>PacketClientLuaMetrics 0x8B (client telemetry).</item>
    /// <item>ProfileStats 0xCA subs 6 GetLeaderboard / 8 GetPlayerLeaderboardRow: the replies (7 / 9) have no client
    /// class (L59), so live's answer can't be built.</item>
    /// </list>
    /// </summary>
    public static class Sinks
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>Each connection's locale (SetLocale), for the areas that will move here and need it.</summary>
        public static readonly ConcurrentDictionary<ulong, string> Locales = new();

        /// <summary>True when the packet ends here.</summary>
        public static bool TryTake(TunnelFromExternal packet)
        {
            byte[] p = packet.Packet;
            if (p.Length < 2)
                return false;
            switch ((p[0], p[1]))
            {
                case (0x5B, 0x00):
                    string locale = DecodeLocale(p);
                    Locales[packet.ConnectionId] = locale;
                    log.Debug($"Connection {packet.ConnectionId:X}: locale {locale}.");
                    return true;
                case (0x26, 0x00) when p.Length >= 6 && System.BitConverter.ToUInt32(p, 2) == 0x68:
                case (0x8B, 0x00):
                    return true;
                case (0xCA, 0x00) when p.Length >= 3 && (p[2] == 6 || p[2] == 8):
                    log.Debug($"Connection {packet.ConnectionId:X}: leaderboard request 0xCA/{p[2]} (no reply class, L59).");
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The locale text after the opcode: printable characters only (the client's StringFixed&lt;8&gt;).</summary>
        private static string DecodeLocale(byte[] p)
        {
            var text = new StringBuilder();
            for (int i = 2; i < p.Length && text.Length < 16; i++)
                if (p[i] >= 0x20 && p[i] < 0x7F)
                    text.Append((char)p[i]);
            return text.ToString();
        }
    }
}
