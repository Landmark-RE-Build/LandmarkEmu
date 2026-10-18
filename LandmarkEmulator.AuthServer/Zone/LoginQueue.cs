using LandmarkEmulator.AuthServer.Network;
using LandmarkEmulator.AuthServer.Network.Message.Model;
using LandmarkEmulator.AuthServer.Network.Message.Model.TunnelData;
using LandmarkEmulator.AuthServer.Network.Message.Static;
using LandmarkEmulator.Shared.Database;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.AuthServer.Zone
{
    /// <summary>
    /// The login queue (TASKS E.8), as live's login server ran it with the 0.695 client:
    /// <list type="number">
    /// <item>Play on a full world (its heartbeat's maxPlayers, WorldServer "MaxPlayers") is answered with
    /// CharacterLoginReply result 8 CharacterQueued; the client's characterselecthandler.lua (OnCharacterLogin, result 8)
    /// opens CreateLoginQueueDialog.</item>
    /// <item>While it waits, login tunnel type 3 <see cref="QueueStatus"/> keeps its place (and the counts) current; the
    /// client counts the estimate down itself between updates.</item>
    /// <item>When a place frees, tunnel type 4 <see cref="ServerQueue"/>: the client closes the dialog and sends its
    /// CharacterLoginRequest again (FUN_1430b3cb0), which now gets the world's address — the admitted account holds a
    /// pass for <see cref="PassLifetime"/>, so it isn't queued again and later arrivals don't take its place.</item>
    /// </list>
    /// Leaving the queue: closing the client (the session ends). Counts come from the worlds' heartbeats
    /// (ZoneServerManager, every 3 s), so the limit can be passed by the few logins of one heartbeat interval.
    /// <para>Members first (TASKS 3.12): accounts with a membership (trial included; the account's memberType /
    /// memberUntil, read when they join the queue) wait ahead of everyone without one, each group in arrival order — the
    /// client's own "Member priority in queue:" / "Non-Member position in queue:" (62067 / 62068) and its queue data
    /// (IsMember, MembersInQueue, NonMembersInQueue). A member's place counts members only; a non-member's counts every
    /// member plus the non-members ahead [I: the client shows one place].</para>
    /// </summary>
    public static class LoginQueue
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private static readonly TimeSpan StatusEvery = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan PassLifetime = TimeSpan.FromSeconds(60);
        /// <summary>Seconds per place for the client's estimate [I: live's figure is not known].</summary>
        private const int SecondsPerPlace = 60;

        private sealed record Waiting(AuthSession Session, ulong ServerId, ulong CharacterId, DateTime Since, bool IsMember);

        /// <summary>A world's line in admission order: members first, then by arrival.</summary>
        private static IEnumerable<Waiting> InOrder(IEnumerable<Waiting> line) => line.OrderByDescending(w => w.IsMember).ThenBy(w => w.Since);

        /// <summary>
        /// Has the account a membership now? Read from the database (it may have been bought in a world since this login
        /// session began); the session's copy if that fails.
        /// </summary>
        private static bool IsMember(AuthSession session)
        {
            try
            {
                var read = DatabaseManager.Instance.AuthDatabase.GetMembership(session.Account.Id);
                if (read.Wait(TimeSpan.FromSeconds(2)) && read.Result is { } m)
                {
                    session.Account.MemberType = m.Type;
                    session.Account.MemberUntil = m.Until;
                }
            }
            catch (Exception e)
            {
                log.Warn(e, $"Account {session.Account.Id}: membership not read; using the login's copy.");
            }
            return session.Account.MemberTypeAt(DateTime.UtcNow) != 0;
        }

        private static readonly object sync = new();
        private static readonly List<Waiting> waiting = new();
        /// <summary>Admitted accounts: account id → (world, until).</summary>
        private static readonly Dictionary<ulong, (ulong ServerId, DateTime Until)> passes = new();
        private static DateTime lastStatus = DateTime.MinValue;

        private static bool Full(ZoneServer server) => server.MaxPlayers == uint.MaxValue;

        private static int PassesFor(ulong serverId) => passes.Count(p => p.Value.ServerId == serverId && p.Value.Until > DateTime.UtcNow);

        /// <summary>
        /// Play pressed for a running world: true when the login must wait (the reply is sent and the session queued
        /// here); false to log in now (an admitted account's pass is used up).
        /// </summary>
        public static bool Queue(AuthSession session, ZoneServer server, ulong characterId)
        {
            if (server.MaxPlayers == 0 || session.Account == null)
                return false;
            // Read before the lock (a database read): only used when the login has to wait.
            bool member = IsMember(session);
            lock (sync)
            {
                if (passes.TryGetValue(session.Account.Id, out var pass) && pass.ServerId == server.Id && pass.Until > DateTime.UtcNow)
                {
                    passes.Remove(session.Account.Id);
                    return false;
                }
                bool others = waiting.Any(w => w.ServerId == server.Id && w.Session != session);
                if (!Full(server) && !others && server.PlayersOnline + PassesFor(server.Id) < server.MaxPlayers)
                    return false;
                if (!waiting.Any(w => w.Session == session))
                    waiting.Add(new Waiting(session, server.Id, characterId, DateTime.UtcNow, member));
                log.Info($"Account {session.Account.Id}: world {server.Id} full ({server.PlayersOnline} / {(Full(server) ? "full" : server.MaxPlayers.ToString())}), queued at {waiting.Count(w => w.ServerId == server.Id)}.");
            }
            session.EnqueueMessage(new CharacterLoginReply
            {
                CharacterId = characterId,
                ServerId = server.Id,
                Result = CharacterLoginResult.CharacterQueued
            });
            SendStatus();
            return true;
        }

        /// <summary>The player cancelled (tunnel type 5): out of the queue; the others move up at the next status.</summary>
        public static void Cancel(AuthSession session)
        {
            bool removed;
            lock (sync)
                removed = waiting.RemoveAll(w => w.Session == session) > 0;
            if (!removed)
                return;
            log.Info($"Account {session.Account?.Id}: left the login queue.");
            SendStatus();
        }

        /// <summary>Login server tick: drops closed sessions, admits while the worlds have room, refreshes places.</summary>
        public static void Update()
        {
            var admit = new List<Waiting>();
            bool changed = false;
            lock (sync)
            {
                foreach (ulong expired in passes.Where(p => p.Value.Until <= DateTime.UtcNow).Select(p => p.Key).ToList())
                    passes.Remove(expired);
                if (waiting.Count == 0)
                    return;
                changed |= waiting.RemoveAll(w => w.Session.Disconnected || w.Session.Account == null) > 0;
                foreach (var group in waiting.GroupBy(w => w.ServerId).ToList())
                {
                    ZoneServer server = ZoneServerManager.Instance.ZoneServers?.FirstOrDefault(z => z.Id == group.Key);
                    if (server == null || !server.Up)
                        continue;
                    long room = server.MaxPlayers == 0 ? group.Count()
                        : Full(server) ? 0
                        : (long)server.MaxPlayers - server.PlayersOnline - PassesFor(server.Id);
                    foreach (Waiting w in InOrder(group))
                    {
                        if (room-- <= 0)
                            break;
                        admit.Add(w);
                    }
                }
                foreach (Waiting w in admit)
                {
                    waiting.Remove(w);
                    passes[w.Session.Account.Id] = (w.ServerId, DateTime.UtcNow + PassLifetime);
                    changed = true;
                }
            }
            foreach (Waiting w in admit)
            {
                log.Info($"Account {w.Session.Account.Id}: leaves the login queue for world {w.ServerId}.");
                w.Session.EnqueueMessage(new TunnelPacketServerToClient
                {
                    ServerId = w.ServerId,
                    Type = TunnelDataType.ServerQueue,
                    Data = new ServerQueue()
                });
            }
            if (changed || DateTime.UtcNow - lastStatus >= StatusEvery)
                SendStatus();
        }

        private static void SendStatus()
        {
            List<Waiting> snapshot;
            lock (sync)
                snapshot = waiting.ToList();
            lastStatus = DateTime.UtcNow;
            foreach (var group in snapshot.GroupBy(w => w.ServerId))
            {
                List<Waiting> line = InOrder(group).ToList();
                int members = line.Count(w => w.IsMember);
                for (int i = 0; i < line.Count; i++)
                    line[i].Session.EnqueueMessage(new TunnelPacketServerToClient
                    {
                        ServerId = group.Key,
                        Type = TunnelDataType.QueueStatus,
                        Data = new QueueStatus
                        {
                            // Members first in the line, so a place is the index in it either way.
                            Position = i,
                            MembersInQueue = members,
                            NonMembersInQueue = line.Count - members,
                            MemberSecondsPerPlace = SecondsPerPlace,
                            NonMemberSecondsPerPlace = SecondsPerPlace,
                            IsMember = line[i].IsMember
                        }
                    });
            }
        }
    }
}
