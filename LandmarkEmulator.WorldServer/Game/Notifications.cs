using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System.Collections.Generic;
using System.Threading;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The client's Game Action Queue notifications (the invitation popups with Accept / Decline). Base 0x01 (bytes
    /// 01 00 = u16 1; the enum's 0x100; Zone_DispatchPacket case 0), read by GameActionQueue::HandleNotificationPacket
    /// (FUN_143336df0): u32
    /// packet type (only 1 = ListNotifications is accepted), u32 n, n × Data (reader FUN_143329330, 0x198 bytes):
    /// u64 id, u32 type, u32, u64 from, PlayerName from {u32, u32, u32, string first, string last}, u64 to,
    /// PlayerName to, u64 subject (+0x180), u64, u32. FUN_143337930 turns each into a queue entry by type:
    /// 6 = guild invitation (GaqItemGuildInvite "GAQGuildInvite"; subject = guild id, looked up in the player's guild
    /// manager for its name), 2 / 3 / 4 / 7 / 9 other kinds. Accepting a guild invitation sends GuildBase
    /// InviteReply (sub 5), which Guilds.Reply handles.
    /// </summary>
    public static class Notifications
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const uint GuildInvite = 6;
        private static long nextId = 1;

        /// <summary>Shows the guild invitation popup to the invited player.</summary>
        public static void SendGuildInvite(WorldSession target, ulong inviterGuid, string inviterName, ulong guildId)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(1u);                         // ListNotifications
            w.Write(1u);
            ulong id = (ulong)Interlocked.Increment(ref nextId);
            w.Write(id);
            w.Write(GuildInvite);
            w.Write(0u);
            w.Write(inviterGuid);
            WriteName(w, inviterName);
            w.Write(target.PlayerGuid);
            WriteName(w, target.CharacterName ?? "");
            w.Write(guildId);
            w.Write(0ul);
            w.Write(0u);
            target.EnqueueMessage(ZoneMessageOpcode.Notifications, output.ToArray());
            log.Info($"Notification {id} (guild invite to {guildId}) sent to {target.CharacterName}.");
        }

        private static void WriteName(GamePacketWriter w, string name)
        {
            w.Write(0u);
            w.Write(0u);
            w.Write(0u);
            w.Write(name ?? "");
            w.Write("");
        }
    }
}
