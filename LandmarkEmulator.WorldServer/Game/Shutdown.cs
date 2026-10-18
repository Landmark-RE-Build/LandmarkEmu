using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using LandmarkEmulator.Shared.Network.Cryptography;
using LandmarkEmulator.Shared.Game;
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
    /// <para>Once the countdown is at 30 s or less, every player also gets the client's kick warning
    /// PacketClientPendingKickFromServer 0x65 {message, seconds} (<see cref="Kicks"/>; setting PendingKickPacket): it brings
    /// the client's sound back to full volume (FUN_142cdd200) so the last warnings are heard. Its own text is empty on the
    /// retail clients (the CodeStringMappings key is missing), so the 0x5F countdown stays the readable warning. Setting
    /// ShutdownBroadcasts (default false = live) also sends each warning step as a System Broadcast 0x4F
    /// (<see cref="Broadcasts"/>: on-screen text and a chat line).</para>
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
        private static bool stopping;
        /// <summary>The 0x65 kick warning went out for this countdown (once, at 30 s or less).</summary>
        private static bool kickWarned;

        /// <summary>The countdown's last stretch, when the 0x65 kick warning goes out.</summary>
        private const int KickWarningSeconds = 30;

        /// <summary>
        /// A shutdown is counting down or under way: the world tells the login server it is going down (its heartbeat
        /// is cleared, Game.ServerHeartbeat), so Play at character select answers "Game Server not found." instead of
        /// sending players into a world that is about to close.
        /// </summary>
        public static bool InProgress
        {
            get { lock (sync) return at != null || stopping; }
        }

        /// <summary>The host is stopping the server (HostedService.StopAsync).</summary>
        public static void BeginStopping()
        {
            lock (sync)
                stopping = true;
        }

        /// <summary>Starts (or restarts) a countdown.</summary>
        public static void Schedule(int seconds, string text)
        {
            lock (sync)
            {
                at = DateTime.UtcNow.AddSeconds(Math.Max(0, seconds));
                message = text ?? "";
                lastWarning = int.MaxValue;
                kickWarned = false;
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

        /// <summary>
        /// Before the network goes down: every connected client is sent back to character select. A client whose world
        /// connection closes without a relogin session quits ("Lost connection to the gateway while waiting for a
        /// relogin session", client run state 0x15, FUN_142c3b490), even mid-load. So each player in the world gets a
        /// fresh login session key (saved as the account's session key, as a logout does) in
        /// CharacterSelectSessionResponse 0xF7: the client takes it in any run state (Zone_DispatchPacket case 0xF7:
        /// run state 0x16 StartingRelogin), disconnects from the world itself and logs in to the login server again.
        /// Clients still loading are waited for until they are in the world (sent earlier, before the client has the
        /// world, it crashed the client). The connection must stay open until the client leaves: if it drops first, the
        /// client (already in StartingRelogin) falls back to waiting for a session and quits.
        /// </summary>
        public static void SendPlayersToCharacterSelect(TimeSpan wait, TimeSpan minimum)
        {
            // Keeps looking for clients for the whole wait, not just once: someone who pressed Play just before the
            // login server heard of the shutdown still arrives. Only players in the world get the relogin session
            // (PlayerManager: the client has loaded and moved): sent at login, before the client has the world, it
            // crashed the client as it started the relogin (in-client 2026-10-14); clients still loading are waited for.
            // Players in the world first run the client's own Logout (Ui.Logout, the Escape menu's: it tears the world
            // down properly and asks for its relogin session itself, CharacterSelectHandler). A relogin session pushed
            // straight at them skipped that teardown, and their next join re-meshed all the terrain they had (90 s
            // instead of ~10, 2026-10-14). Only a player still here a few seconds later gets the session pushed.
            DateTime start = DateTime.UtcNow, until = start + wait, earliest = start + minimum;
            int sent = 0;
            var askedAt = new Dictionary<WorldSession, DateTime>();
            while (true)
            {
                foreach (WorldSession s in PlayerManager.Snapshot())
                {
                    if (s.Account == null || s.Disconnected)
                        continue;
                    if (!askedAt.ContainsKey(s))
                    {
                        askedAt[s] = DateTime.UtcNow;
                        ClientLua.Run(s, "Ui:Logout");
                        sent++;
                    }
                    else if (DateTime.UtcNow - askedAt[s] > TimeSpan.FromSeconds(8))
                        TurnAway(s);
                }
                bool anyone = Network.ZoneSessions.Connected() > 0;
                DateTime now = DateTime.UtcNow;
                if (now >= until || (!anyone && now >= earliest))
                    break;
                System.Threading.Thread.Sleep(200);
            }
            int left = Network.ZoneSessions.Connected();
            log.Info($"Shutdown: {sent} client(s) sent to character select" + (left > 0 ? $"; {left} still connected, closing anyway." : "."));
        }

        private static readonly HashSet<WorldSession> turnedAway = new();

        /// <summary>
        /// Sends one client back to character select (once): a fresh login session key, saved as the account's session
        /// key (as a logout does), in CharacterSelectSessionResponse. Returns false when it was already sent or the key could not be saved.
        /// </summary>
        public static bool TurnAway(WorldSession s)
        {
            lock (turnedAway)
                if (!turnedAway.Add(s))
                    return false;
            string key = Inventory.ItemDefinitionManager.NewRelogKey();
            try
            {
                DatabaseManager.Instance.AuthDatabase.UpdateAccountSessionKey(s.Account, key).Wait(TimeSpan.FromSeconds(3));
            }
            catch (Exception e)
            {
                log.Warn(e.GetBaseException(), $"Shutdown: session key for account {s.Account.Id} not saved.");
                return false;
            }
            Inventory.ItemDefinitionManager.NoteRelogin(s.Account.Id, key);
            s.EnqueueMessage(new CharacterSelectSessionResponse { Success = true, SessionId = key });
            log.Info($"Shutdown: account {s.Account.Id} sent to character select.");
            return true;
        }

        private static void Broadcast(int seconds, string text)
        {
            bool warnKick = false;
            if (seconds > 0 && seconds <= KickWarningSeconds)
                lock (sync)
                {
                    warnKick = !kickWarned;
                    kickWarned = true;
                }
            if (warnKick)
                foreach (WorldSession s in PlayerManager.Snapshot())
                    Kicks.SendPendingPacketTo(s, string.IsNullOrWhiteSpace(text) ? "The server is shutting down." : text, seconds);
            if (Broadcasts.MirrorShutdownWarnings)
                Broadcasts.ToEveryone((seconds > 0 ? $"The server is shutting down in {Remaining(seconds)}." : "The server is shutting down now.")
                    + (string.IsNullOrWhiteSpace(text) ? "" : " " + text.Trim()));
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write((uint)Math.Max(0, seconds));
            w.Write(text ?? "");
            w.Write(0u);
            byte[] packet = output.ToArray();
            foreach (WorldSession s in PlayerManager.Snapshot())
                s.EnqueueMessage(ZoneMessageOpcode.WorldShutdownNotice, packet);
        }

        /// <summary>"1 hour", "15 minutes", "30 seconds" for the broadcast mirror.</summary>
        private static string Remaining(int seconds) => seconds switch
        {
            >= 3600 when seconds % 3600 == 0 => seconds / 3600 == 1 ? "1 hour" : $"{seconds / 3600} hours",
            >= 60 when seconds % 60 == 0 => seconds / 60 == 1 ? "1 minute" : $"{seconds / 60} minutes",
            >= 120 => $"{(seconds + 30) / 60} minutes",
            1 => "1 second",
            _ => $"{seconds} seconds",
        };
    }
}
