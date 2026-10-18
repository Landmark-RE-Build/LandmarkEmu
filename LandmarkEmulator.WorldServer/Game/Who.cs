using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// /who (TASKS 5.4; OPEN-LEADS L67 a): the 0.695 client has a reply but no request class — no WhoRequest in its RTTI
    /// and no "who" in its own command tables — so live's server added /who as a command of its own; here a world
    /// command (<see cref="DynamicCommands"/>), which the client sends as ExecuteCommand with the rest of the line.
    /// <para>
    /// The answer is CommandPacketWhoReply, Command 0x1E sub 0x02 (0.695 reader FUN_143383a70, entries FUN_143385c40,
    /// handler FUN_1433bc350) [P]: u32 n × WhoData {string name, i8 (+0x40, handed to the Lua hook OnReceiveWho), u32 zone
    /// id (+0x44), u32, u32, u32, u64 guid (+0x58)}, then bool "names only" (+0x40 of the packet). The client prints
    /// "------------------------ Who Results ------------------------" and one "  - name" line per entry; an admin client
    /// also gets ", Zone: [id] name" and the position of entities it has. Ours [I]: who is listed — the players online on
    /// this world whose name contains the typed text (all of them without one), at most <see cref="MaxListed"/>, players
    /// hidden by an admin left out; the i8 and the three u32 are 0 (their meaning isn't in the client).
    /// </para>
    /// </summary>
    public static class Who
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const string CommandName = "who";
        private const ushort WhoReplySub = 0x02;
        private const int MaxListed = 50;

        public static void Run(WorldSession session, string args)
        {
            string filter = (args ?? "").Trim();
            List<WorldSession> found = PlayerManager.Snapshot()
                .Where(p => !p.Disconnected && !AdminCommands.IsHidden(p) && (filter.Length == 0
                    || (p.CharacterName ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(p => p.CharacterName, StringComparer.OrdinalIgnoreCase).Take(MaxListed).ToList();
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.WriteUShort(WhoReplySub);
            w.Write((uint)found.Count);
            foreach (WorldSession p in found)
            {
                w.Write(p.CharacterName ?? "");
                w.Write((byte)0);
                w.Write(p.ZoneId);
                w.Write(0u);
                w.Write(0u);
                w.Write(0u);
                w.Write(p.PlayerGuid);
            }
            w.Write(false);
            session.EnqueueMessage(ZoneMessageOpcode.CommandBase, output.ToArray());
            log.Info($"{session.CharacterName}: /who {filter} → {found.Count} player(s).");
        }
    }
}
