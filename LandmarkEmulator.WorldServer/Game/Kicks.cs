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
    /// Removing a player from the server: the kick warning and the kick itself (OPEN-LEADS L172).
    /// <list type="bullet">
    /// <item><b>PacketClientPendingKickFromServer 0x65</b> (wire 65 00; zone dispatcher case 0x2b, reader FUN_142c4a0d0)
    /// [P]: string message (StringFixed&lt;128&gt;, +0x10), u32 seconds (+0xb0); trailing bytes are allowed (strict flag 1).
    /// The handler formats CodeStringMappings "KickedFromServerTimedMessage" with the tokens player (the local player),
    /// seconds (also "TimeRemainingSeconds"), message, and shows it with FUN_142cdd200: sound volume back to full over
    /// 1 s and un-muted (+0x32a00 = 1.0, +0x32a04 = 1000, +0x329f9 = 0), then BaseClient::ShowSystemMessage in red
    /// ("0x990000", FUN_142d56570). <b>The key is missing</b> from the 0.695 client's Resources/CodeStringMappings.txt (and
    /// 0.589's), so the client formats text id 0: the red line comes out empty and the seconds and message are not shown.
    /// So every warning also goes out as a readable system line (client text 558682 "[*message*]", <see cref="Chat.System"/>)
    /// with our wording [I]; setting <c>PendingKickPacket</c> (default true) sends the client's own packet beside it.</item>
    /// <item><b>PacketClientKickedFromServer 0x53</b> (case 0x24, reader FUN_142bb0360) [P]: string message
    /// (StringFixed&lt;128&gt;): ErrorLabelWarning + KickedFromServerMessage (also missing, so the client shows the
    /// message itself) in a message box, "RECEIVED=KickedFromServer" in NetInfo.log. The connection is closed once the
    /// message has gone out (as for a second login of the same character, <see cref="PlayerManager.Enter"/>).</item>
    /// </list>
    /// Live: no source names when live sent 0x65 (the client keeps an idle / admin kick path for it). Here it fronts the
    /// admin kick (dev <c>@Name kick [seconds] [reason]</c>, the admin page) and, once at 30 s or less, the server
    /// shutdown countdown (<see cref="Shutdown"/>).
    /// </summary>
    public static class Kicks
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>Both messages are StringFixed&lt;128&gt; on the client.</summary>
        public const int MaxMessageBytes = 127;

        /// <summary>Warning steps of a timed kick (seconds left), after the first warning.</summary>
        private static readonly int[] Steps = { 300, 120, 60, 30, 10, 5 };

        /// <summary>Kicks given no reason.</summary>
        public const string DefaultReason = "You have been removed from the server by an administrator.";

        /// <summary>PendingKickPacket: send the client's 0x65 warning beside the readable line (true).</summary>
        public static bool SendPendingPacket =>
            LandmarkEmulator.Shared.Configuration.SharedConfiguration.Configuration?.GetValue("PendingKickPacket", true) ?? true;

        private sealed class Pending
        {
            public WorldSession Session;
            public DateTime At;
            public string Reason;
            public string By;
            public int LastStep;
        }

        private static readonly object sync = new();
        private static readonly Dictionary<WorldSession, Pending> pending = new();

        /// <summary>PendingKickFromServer body: string message, u32 seconds.</summary>
        public static byte[] PendingBody(string message, uint seconds)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(Broadcasts.Fit(message, MaxMessageBytes));
            w.Write(seconds);
            return output.ToArray();
        }

        /// <summary>KickedFromServer body: string message.</summary>
        public static byte[] KickedBody(string message)
        {
            var output = new List<byte>();
            new GamePacketWriter(output).Write(Broadcasts.Fit(message, MaxMessageBytes));
            return output.ToArray();
        }

        /// <summary>The client's 0x65 warning alone (when <see cref="SendPendingPacket"/> is on).</summary>
        public static void SendPendingPacketTo(WorldSession session, string message, int seconds)
        {
            if (session == null || session.Disconnected || !SendPendingPacket)
                return;
            session.EnqueueMessage(ZoneMessageOpcode.ClientPendingKickFromServer, PendingBody(message, (uint)Math.Max(0, seconds)));
        }

        /// <summary>A warning: the readable line and the client's 0x65.</summary>
        private static void Warn(WorldSession session, int seconds, string reason)
        {
            Chat.System(session, $"You will be removed from the server in {seconds} second{(seconds == 1 ? "" : "s")}. {reason}");
            SendPendingPacketTo(session, reason, seconds);
        }

        /// <summary>
        /// Kicks a player: at once (<paramref name="seconds"/> 0), or after a countdown with warnings at the start and at
        /// <see cref="Steps"/>. A new kick for the same player replaces the old one.
        /// </summary>
        public static void Kick(WorldSession session, int seconds, string reason, string by = "")
        {
            if (session == null || session.Disconnected)
                return;
            reason = string.IsNullOrWhiteSpace(reason) ? DefaultReason : reason.Trim();
            if (seconds <= 0)
            {
                lock (sync)
                    pending.Remove(session);
                KickNow(session, reason);
                log.Warn($"{session.CharacterName} kicked{(string.IsNullOrEmpty(by) ? "" : $" by {by}")}: {reason}");
                return;
            }
            lock (sync)
                pending[session] = new Pending { Session = session, At = DateTime.UtcNow.AddSeconds(seconds), Reason = reason, By = by ?? "", LastStep = seconds };
            Warn(session, seconds, reason);
            log.Warn($"{session.CharacterName} will be kicked in {seconds} s{(string.IsNullOrEmpty(by) ? "" : $" (by {by})")}: {reason}");
        }

        /// <summary>Cancels a timed kick; true when one was pending.</summary>
        public static bool Cancel(WorldSession session)
        {
            bool had;
            lock (sync)
                had = pending.Remove(session);
            if (had && !session.Disconnected)
                Chat.System(session, "You will not be removed from the server after all.");
            return had;
        }

        /// <summary>
        /// The kick itself: KickedFromServer 0x53 {message}, then the connection is closed 1.5 s later (the message box
        /// must reach the client first).
        /// </summary>
        public static void KickNow(WorldSession session, string message)
        {
            if (session == null || session.Disconnected)
                return;
            session.EnqueueMessage(ZoneMessageOpcode.ClientKickedFromServer, KickedBody(message));
            _ = System.Threading.Tasks.Task.Delay(1500).ContinueWith(_ => { if (!session.Disconnected) session.OnDisconnect(); });
        }

        /// <summary>Server tick: warning steps and due kicks.</summary>
        public static void Update()
        {
            List<(Pending Kick, int Left)> warn = new(), due = new();
            lock (sync)
            {
                if (pending.Count == 0)
                    return;
                DateTime now = DateTime.UtcNow;
                foreach (Pending p in pending.Values.ToList())
                {
                    if (p.Session.Disconnected)
                    {
                        pending.Remove(p.Session);
                        continue;
                    }
                    int left = (int)Math.Ceiling((p.At - now).TotalSeconds);
                    if (left <= 0)
                    {
                        pending.Remove(p.Session);
                        due.Add((p, 0));
                        continue;
                    }
                    int step = Steps.FirstOrDefault(s => s >= left && s < p.LastStep);
                    if (step != 0)
                    {
                        p.LastStep = step;
                        warn.Add((p, left));
                    }
                }
            }
            foreach ((Pending p, int left) in warn)
                Warn(p.Session, left, p.Reason);
            foreach ((Pending p, _) in due)
            {
                KickNow(p.Session, p.Reason);
                log.Warn($"{p.Session.CharacterName} kicked{(string.IsNullOrEmpty(p.By) ? "" : $" by {p.By}")}: {p.Reason}");
            }
        }
    }
}
