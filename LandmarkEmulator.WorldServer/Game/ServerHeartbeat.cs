using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Shared.Database;
using NLog;
using System;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Tells the login server this world is running: every few seconds the world stamps its zone_server row (heartbeat
    /// time and players online), and clears the heartbeat when it stops. The login server lists the world as up while
    /// the stamp is fresh, and as down (the server entry's status bit 1, "IsActive" = 0 at character select) otherwise.
    /// </summary>
    public static class ServerHeartbeat
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
        private static DateTime next;
        private static Task pending;

        private static ulong ZoneServerId => ConfigurationManager<WorldServerConfiguration>.Instance.Config.ZoneServerId;

        /// <summary>World tick.</summary>
        public static void Update()
        {
            DateTime now = DateTime.UtcNow;
            if (now < next || pending is { IsCompleted: false })
                return;
            next = now + Interval;
            uint players = (uint)PlayerManager.Snapshot().Count;
            // During a shutdown countdown the world is reported down already (no new players join a closing world).
            bool running = !Shutdown.InProgress;
            pending = DatabaseManager.Instance.AuthDatabase.UpdateZoneServerHeartbeat(ZoneServerId, running, players)
                .ContinueWith(t => log.Warn(t.Exception?.GetBaseException(), "World heartbeat not saved."),
                    TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>The world is stopping: listed as down straight away (not after the heartbeat goes stale).</summary>
        public static void Stopped()
        {
            try
            {
                DatabaseManager.Instance.AuthDatabase.UpdateZoneServerHeartbeat(ZoneServerId, false, 0).Wait(TimeSpan.FromSeconds(3));
            }
            catch (Exception e)
            {
                log.Warn(e.GetBaseException(), "World heartbeat not cleared.");
            }
        }
    }
}
