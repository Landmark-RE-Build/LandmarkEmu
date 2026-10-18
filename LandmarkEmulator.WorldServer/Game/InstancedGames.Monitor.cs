using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The site games for the admin monitor (trace/architecture/admin-pages.md §2): the client's page 29 "Instanced Game
    /// Information - Server" and the web page /admin/monitor show the same indented key / value tree.
    /// <para>
    /// InstancedGameAdminMonitorPacket = InstancedGames base 0x10D, <b>u32 sub 2</b> (ClientInstancedGameManager::
    /// ProcessPacket FUN_14343c3d0 / 0.695 FUN_1434ee920 switches on sub − 2, case 0; reader FUN_143427160 / FUN_1434d6be0):
    /// <c>u32 n, n × {string key, string value, u32 indent}</c>. Same bytes for 0.589 and 0.695 (sub 2 is below the
    /// 0.695 renumbering, which starts at 0xD). The client takes it only if it was read without error and with no bytes
    /// left, and only while page 29 is the current monitor page; it keeps drawing the last one it took, each line as
    /// <c>"%*s%s %s"</c> (a "-" right-aligned in 2 × indent columns, key, value), coloured by indent % 6.
    /// </para>
    /// <para>
    /// What live put in it is not proven (OPEN-LEADS L81): the keys below are ours, in the style of the client's own
    /// "Client" block on the same page (State, DesiredState, QueueSize, GameEnabled...).
    /// </para>
    /// </summary>
    public static partial class InstancedGames
    {
        /// <summary>InstancedGameAdminMonitorPacket's sub (S→C).</summary>
        public const uint SubAdminMonitor = 2;

        /// <summary>One line of the tree: indent 0 = a game, 1 = its facts, 2 = its players.</summary>
        public sealed record MonitorLine(string Key, string Value, uint Indent);

        private static readonly string[] StateNames =
            { "None", "Configured", "Idle", "Started", "WaitingForReady", "Ready", "Running", "Victory", "Finishing", "Restarting" };

        private static string StateText(uint state) => $"{state} ({(state < StateNames.Length ? StateNames[state] : "?")})";

        /// <summary>
        /// The tree of the game a player is in or standing on (page 29 for an admin), or null when there is none.
        /// </summary>
        public static List<MonitorLine> MonitorLinesFor(WorldSession session)
        {
            EnsureLoaded();
            Game game = GameOf(session) ?? GameAt(session);
            if (game == null)
                return null;
            lock (sync)
                return MonitorLines(game, DateTime.UtcNow);
        }

        /// <summary>Every site game's tree (the web page's "Instanced games" section).</summary>
        public static List<List<MonitorLine>> MonitorAll()
        {
            EnsureLoaded();
            DateTime now = DateTime.UtcNow;
            lock (sync)
                return gamesByClaim.Values.OrderBy(g => g.Zone).ThenBy(g => g.ClaimId).Select(g => MonitorLines(g, now)).ToList();
        }

        /// <summary>Caller holds <see cref="sync"/>.</summary>
        private static List<MonitorLine> MonitorLines(Game g, DateTime now)
        {
            static string Seconds(DateTime at, DateTime now) => at > now ? $"{(int)(at - now).TotalSeconds} s" : "-";
            var lines = new List<MonitorLine>
            {
                new("Game", $"{g.Guid} {Title(g)}", 0),
                new("State", StateText(g.State), 1),
                new("RuleSet", $"{g.Rules.Id} {g.Rules.Name}", 1),
                new("Site", g.ClaimId.ToString(), 1),
                new("Landscape", g.Zone.ToString(), 1),
                new("GameEnabled", g.Disabled ? "0" : "1", 1),
                new("Players", g.Players.Count.ToString(), 1),
                new("MinPlayers", g.Rules.MinPlayers.ToString(), 1),
                new("MaxPlayers", g.Rules.MaxPlayers.ToString(), 1),
                new("QueueSize", g.Queue.Count.ToString(), 1),
                new("Observers", g.Observers.Count.ToString(), 1),
                new("Editors", g.Editors.Count.ToString(), 1),
                new("StateEnds", Seconds(g.StateEnds, now), 1),
                new("RoundEnds", g.State == StateRunning && g.Rules.HasTimeLimit ? Seconds(g.RoundEnds, now) : "-", 1)
            };
            if (g.Rules.TeamCount > 0)
                lines.Add(new("TeamScores", string.Join(" / ", Enumerable.Range(0, g.Rules.TeamCount)
                    .Select(t => $"{TeamName(g, t)} {g.TeamScores.ElementAtOrDefault(t)}")), 1));
            if (g.Points.Count > 0)
                lines.Add(new("ControlPoints", string.Join(", ", g.Points.Select(c => $"{c.Name} {(c.Owner < 0 ? "-" : c.Owner.ToString())}")), 1));
            foreach (Player p in g.Players.OrderBy(p => p.Team).ThenByDescending(p => p.Score))
                lines.Add(new(p.Session.CharacterName ?? "?",
                    $"team {p.Team + 1}, K/D/S {p.Kills}/{p.Deaths}/{p.Score}"
                    + (p.RespawnAt != null ? ", respawning" : "") + (p.Arriving ? ", arriving" : ""), 2));
            return lines;
        }

        /// <summary>The InstancedGameAdminMonitorPacket body after the u16 base (<see cref="SubAdminMonitor"/> first).</summary>
        public static byte[] AdminMonitorBody(List<MonitorLine> lines)
        {
            var output = new List<byte>();
            GamePacketWriter w = Begin(output, SubAdminMonitor);
            w.Write((uint)lines.Count);
            foreach (MonitorLine line in lines)
            {
                w.Write(line.Key ?? "");
                w.Write(line.Value ?? "");
                w.Write(line.Indent);
            }
            return output.ToArray();
        }
    }
}
