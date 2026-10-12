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
    /// Server shutdown warnings (client SystemShutDownDialog). WorldShutdownNotice 0x5F (reader FUN_142b8b650, handler
    /// FUN_142c4d020): u32 seconds left, string message, u32. The client builds the text itself:
    /// "ServerShutdownMessageWithTimeRemaining" with hours / minutes / seconds above 5 s, "ServerImmediateShutdownMessage"
    /// at 5 s or less, and shows the message too.
    /// The dev command "shutdown &lt;seconds&gt; [message]" counts down with warnings at the usual steps (1 h … 10 s),
    /// sends the final notice and stops the server cleanly; "shutdown cancel" stops the countdown. A server stopped any
    /// other way (Ctrl+C) still sends the immediate notice first.
    /// </summary>
    public static class Shutdown
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        private static readonly int[] Warnings = { 3600, 1800, 900, 600, 300, 120, 60, 30, 10, 5 };

        /// <summary>Set by the host: asks the application to stop (HostedService.StopAsync runs).</summary>
        public static Action StopServer { get; set; }

        private static readonly object sync = new();
        private static DateTime? at;
        private static string message = "";
        private static int lastWarning = int.MaxValue;
        private static bool notified;

        /// <summary>Starts (or restarts) a countdown.</summary>
        public static void Schedule(int seconds, string text)
        {
            lock (sync)
            {
                at = DateTime.UtcNow.AddSeconds(Math.Max(0, seconds));
                message = text ?? "";
                lastWarning = int.MaxValue;
            }
            Broadcast(seconds, text ?? "");
            lock (sync)
                lastWarning = seconds;
            log.Warn($"Server shutdown in {seconds} s: {text}");
        }

        public static void Cancel()
        {
            lock (sync)
                at = null;
            foreach (WorldSession s in PlayerManager.Snapshot())
                Chat.System(s, "The server shutdown has been cancelled.");
            log.Warn("Server shutdown cancelled.");
        }

        /// <summary>Server tick: the next warning step, then the stop.</summary>
        public static void Update()
        {
            int left;
            string text;
            lock (sync)
            {
                if (at == null)
                    return;
                left = (int)Math.Ceiling((at.Value - DateTime.UtcNow).TotalSeconds);
                text = message;
            }
            if (left <= 0)
            {
                lock (sync)
                    at = null;
                NotifyStopping();
                log.Warn("Server shutdown: stopping now.");
                StopServer?.Invoke();
                return;
            }
            int step = Warnings.FirstOrDefault(w => w >= left && w < lastWarning);
            if (step != 0 && left <= step)
            {
                lock (sync)
                    lastWarning = step;
                Broadcast(left, text);
            }
        }

        /// <summary>The immediate notice (once), before the network goes down.</summary>
        public static void NotifyStopping()
        {
            lock (sync)
            {
                if (notified)
                    return;
                notified = true;
            }
            Broadcast(0, message);
        }

        private static void Broadcast(int seconds, string text)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write((uint)Math.Max(0, seconds));
            w.Write(text ?? "");
            w.Write(0u);
            byte[] packet = output.ToArray();
            foreach (WorldSession s in PlayerManager.Snapshot())
                s.EnqueueMessage(ZoneMessageOpcode.WorldShutdownNotice, packet);
        }
    }
}
