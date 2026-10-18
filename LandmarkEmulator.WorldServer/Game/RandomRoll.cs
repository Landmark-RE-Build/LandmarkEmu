using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// /random (TASKS 5.4; trace/systems/commands.md): CommandPacketRandom, Command 0x1E sub 0x5B, both ways.
    /// <list type="bullet">
    /// <item>Client → server (0.695 command FUN_1433b2xxx, part_0101 ~36300): "/random" = 1-100, "/random N" = 1-N,
    /// "/random A B" = A-B (ordered by the client, refused with "Random.InvalidParams" when the low end isn't above 0).
    /// Body after the u16 sub [P]: u32 start (+0x20), u32 end (+0x24), u64 player (+0x28; the client sends its "no guid"
    /// value), u32 result (+0x30; 1).</item>
    /// <item>Server → clients, the same layout with the roller's guid and the result (reader FUN_1433830a0, handler
    /// FUN_1433bf480 = Command case 0x5B) [P]: the client prints its own "Random.SelfDisplayString" for its own roll and
    /// "Random.OtherDisplayString" for someone else's, filled with Player / StartNum / EndNum / Result.</item>
    /// </list>
    /// Who sees someone else's roll is ours [I]: the players within /say range of the roller (Chat's say range), the way
    /// the roll shows up in chat.
    /// </summary>
    public static class RandomRoll
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const ushort Sub = 0x5B;
        /// <summary>The /say range (metres): who sees another player's roll (ours, as Chat's say lines).</summary>
        private const float SeenWithin = 100f;

        public static void Handle(WorldSession session, byte[] body)
        {
            uint start = 1, end = 100;
            try
            {
                var r = new GamePacketReader(body);
                start = r.ReadUInt();
                end = r.ReadUInt();
            }
            catch (Exception)
            {
                // A short body: the client's defaults.
            }
            if (start == 0)
                start = 1;
            if (end < start)
                (start, end) = (end, start);
            if (end > int.MaxValue - 1)
                end = int.MaxValue - 1;
            uint result = (uint)Random.Shared.Next((int)start, (int)end + 1);

            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.WriteUShort(Sub);
            w.Write(start);
            w.Write(end);
            w.Write(session.PlayerGuid);
            w.Write(result);
            byte[] packet = output.ToArray();

            foreach (WorldSession other in PlayerManager.InZone(session.ZoneId))
                if (other == session || Vector3.Distance(other.Position, session.Position) <= SeenWithin)
                    other.EnqueueMessage(ZoneMessageOpcode.CommandBase, packet);
            log.Info($"{session.CharacterName} rolled {result} ({start}-{end}).");
        }
    }
}
