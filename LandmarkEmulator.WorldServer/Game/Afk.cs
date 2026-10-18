using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// /afk (wiki "Commands List"; trace/systems/commands.md). The client sends CommandPacketAFKRequest (Command 0x1E sub
    /// 0x3B; 0.695 class vtable 0x14475eff8: {u64 player, flag, string message}) and
    /// shows its own "UI.AFK1" line while its local AFK state is set (+0x2290). The server toggles the player's state and
    /// tells the landscape with PlayerUpdatePacketAFK (0x26 sub 0x57; 0.589 reader FUN_142b8c990: {u64 player, bool afk};
    /// 0.695 only adds the u32 sub-opcode header, which the session writes). Ours: the toggle and who hears it.
    /// </summary>
    public static class Afk
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private static readonly HashSet<WorldSession> away = new();

        /// <summary>The player typed /afk: toggles their state and tells everyone on the landscape.</summary>
        public static void Toggle(WorldSession session)
        {
            bool now;
            lock (away)
            {
                if (!away.Remove(session))
                    away.Add(session);
                now = away.Contains(session);
            }
            Broadcast(session, now);
            log.Info($"{session.CharacterName} is {(now ? "away" : "back")}.");
        }

        /// <summary>A player who is away gets a fresh viewer up to date (PlayerManager introductions).</summary>
        public static void Introduce(WorldSession viewer, WorldSession subject)
        {
            bool isAway;
            lock (away)
                isAway = away.Contains(subject);
            if (isAway)
                viewer.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateAFKWire, Body(subject, true));
        }

        /// <summary>Left the world: no longer away.</summary>
        public static void Forget(WorldSession session)
        {
            lock (away)
                away.Remove(session);
        }

        private static void Broadcast(WorldSession session, bool isAway)
        {
            byte[] body = Body(session, isAway);
            foreach (WorldSession p in PlayerManager.InZone(session.ZoneId))
                if (!p.Disconnected)
                    p.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateAFKWire, body);
        }

        private static byte[] Body(WorldSession session, bool isAway)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(session.PlayerGuid);
            w.Write(isAway);
            return output.ToArray();
        }
    }
}
