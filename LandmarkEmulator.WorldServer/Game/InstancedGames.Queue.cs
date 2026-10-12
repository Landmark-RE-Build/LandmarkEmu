using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Site game queues. A player who asks to join a full game (or one showing its end-of-round scoreboard) waits in the
    /// game's queue. The client shows the wait in its Game Action Queue (GaqInstancedGameQueue, FUN_14332ab20): request
    /// type 5 {i16 value1, i16 queue position (+0x14c, "QueuePosition"), i16 estimated wait in seconds (+0x150,
    /// "EstimatedWaitTime"), name}; position 0 turns its button into "Join Now" (UI.JoinNow), which sends the join
    /// request again. Type 4 takes the entry away (the player left the queue or joined). The walk-on window shows the
    /// queue size (site game info). A queued player whose turn came has the rule set's MaxQueueJoinTime (general setting
    /// 4, ms; 30 s) to join, else they are dropped.
    /// </summary>
    public static partial class InstancedGames
    {
        private const short RequestLeftQueue = 4, RequestQueuePosition = 5;

        private class Queued
        {
            public WorldSession Session;
            /// <summary>When the player was told "Join Now"; null while still waiting.</summary>
            public DateTime? OfferedAt;
        }

        /// <summary>Puts a player who can't join yet in the game's queue (or tells them where they are).</summary>
        private static void Enqueue(Game game, WorldSession session)
        {
            lock (sync)
            {
                foreach (Game g in gamesByClaim.Values)
                    if (g != game)
                        g.Queue.RemoveAll(q => q.Session == session);
                if (game.Queue.All(q => q.Session != session))
                    game.Queue.Add(new Queued { Session = session });
            }
            SendQueuePositions(game);
            Claims.SendGameInfo(game.ClaimId);
            log.Info($"{session.CharacterName} is queued for {Title(game)} on site {game.ClaimId}.");
        }

        /// <summary>Takes a player out of a game's queue (joined, left, or gone); tells them unless they're gone.</summary>
        private static bool Dequeue(WorldSession session, bool tell = true)
        {
            Game was = null;
            lock (sync)
                foreach (Game g in gamesByClaim.Values)
                    if (g.Queue.RemoveAll(q => q.Session == session) > 0)
                        was = g;
            if (was == null)
                return false;
            if (tell)
                SendQueueRequest(session, was, RequestLeftQueue, 0, 0);
            SendQueuePositions(was);
            Claims.SendGameInfo(was.ClaimId);
            return true;
        }

        /// <summary>Everyone in the queue: their place (1 = next), or 0 = "Join Now" once there is room.</summary>
        private static void SendQueuePositions(Game game)
        {
            List<Queued> queue;
            lock (sync)
                queue = game.Queue.ToList();
            int free = Math.Max(0, game.Rules.MaxPlayers - game.Players.Count);
            bool open = game.CanJoin;
            for (int i = 0; i < queue.Count; i++)
            {
                bool turn = open && i < free;
                if (turn)
                    queue[i].OfferedAt ??= DateTime.UtcNow;
                int wait = turn ? 0 : Math.Max(10, (int)(game.State == StateRunning ? (game.RoundEnds - DateTime.UtcNow).TotalSeconds
                    : (game.StateEnds - DateTime.UtcNow).TotalSeconds));
                SendQueueRequest(queue[i].Session, game, RequestQueuePosition, turn ? 0 : i + 1, wait);
            }
        }

        /// <summary>Request sub 8 as the client's HandleGameRequestPacket reads it (see SendMembership).</summary>
        private static void SendQueueRequest(WorldSession session, Game game, short type, int position, int waitSeconds)
        {
            var output = new List<byte>();
            var w = Begin(output, SubRequest);
            w.Write((ushort)type);
            w.Write((ushort)1);
            w.Write((ushort)Math.Clamp(game.Queue.Count, 0, short.MaxValue));
            w.Write((ushort)Math.Clamp(position, 0, short.MaxValue));
            w.Write((ushort)Math.Clamp(waitSeconds, 0, short.MaxValue));
            w.Write(game.Guid);
            w.Write(game.Rules.NameId);
            w.Write(game.CustomName ?? "");
            session.EnqueueMessage(ZoneMessageOpcode.InstancedGameBase, output.ToArray());
        }

        /// <summary>
        /// Server tick (with the sync): a game with room offers it to the head of its queue; offers not taken within
        /// MaxQueueJoinTime are dropped.
        /// </summary>
        private static void TickQueue(Game game, DateTime now)
        {
            List<Queued> expired;
            int timeoutMs = General(game, game.Rules, 4, 30000);
            lock (sync)
            {
                if (game.Queue.Count == 0)
                    return;
                game.Queue.RemoveAll(q => !PlayerManager.Snapshot().Contains(q.Session));
                expired = game.Queue.Where(q => q.OfferedAt is DateTime at && (now - at).TotalMilliseconds > Math.Max(5000, timeoutMs)).ToList();
                game.Queue.RemoveAll(expired.Contains);
            }
            foreach (Queued q in expired)
            {
                SendQueueRequest(q.Session, game, RequestLeftQueue, 0, 0);
                Chat.System(q.Session, $"You didn't join {Title(game)} in time and left its queue.");
            }
            SendQueuePositions(game);
        }

        /// <summary>
        /// May this queued player join now: they are among the first (free places) of the queue. Joining takes them out
        /// of it (the client's queue entry goes away).
        /// </summary>
        private static bool QueueTurn(Game game, WorldSession session)
        {
            int free = Math.Max(0, game.Rules.MaxPlayers - game.Players.Count);
            int index;
            lock (sync)
                index = game.Queue.FindIndex(q => q.Session == session);
            if (index < 0 || index >= free)
                return false;
            Dequeue(session);
            return true;
        }

        /// <summary>Dev "game": the queue, for the game's description.</summary>
        private static string DescribeQueue(Game game)
        {
            lock (sync)
                return game.Queue.Count == 0 ? "" : " queue: " + string.Join(", ", game.Queue.Select(q => q.Session.CharacterName + (q.OfferedAt != null ? " (offered)" : "")));
        }
    }
}
