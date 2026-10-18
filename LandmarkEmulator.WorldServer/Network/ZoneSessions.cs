using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Network
{
    /// <summary>
    /// Every zone session of this process, from the moment the gateway attached its connection until it closed —
    /// whether the gateway runs in this process (<see cref="ClientSession"/>) or as its own (<see cref="GatewayLink"/>).
    /// PlayerManager lists the players in the world; this lists connections, including those still loading.
    /// </summary>
    public static class ZoneSessions
    {
        private static readonly object sync = new();
        private static readonly HashSet<WorldSession> sessions = new();

        public static void Add(WorldSession session)
        {
            lock (sync)
                sessions.Add(session);
        }

        public static void Remove(WorldSession session)
        {
            lock (sync)
                sessions.Remove(session);
        }

        /// <summary>The sessions now (a copy).</summary>
        public static List<WorldSession> All()
        {
            lock (sync)
                return sessions.ToList();
        }

        /// <summary>
        /// Another open session of the same character — the relog that is replacing <paramref name="session"/> (a fast
        /// relog or a crash relog attaches the new connection before the old one has left) — or null. Its inventory and
        /// wallet were loaded from the database at its login, so anything still owed to the old session (a craft finished
        /// at logout, a mail escrow given back) belongs there: given to the old session's in-memory inventory it would be
        /// overwritten by the new session's next save (absolute stack counts) or land in a slot the new one also fills
        /// (review 2026-10-18b #4). Not in PlayerManager's list yet while <c>Enter</c> → <c>Leave(old)</c> runs, hence
        /// looked up here.
        /// </summary>
        public static WorldSession ReplacementOf(WorldSession session)
        {
            if (session == null || session.PlayerGuid == 0)
                return null;
            lock (sync)
                return sessions.FirstOrDefault(s => s != session && s.PlayerGuid == session.PlayerGuid && !s.Disconnected
                    && s.Inventory != null);
        }

        /// <summary>Connections still open.</summary>
        public static int Connected()
        {
            lock (sync)
                return sessions.Count(s => !s.Disconnected);
        }
    }
}
