using LandmarkEmulator.AuthServer.Network;
using LandmarkEmulator.AuthServer.Network.Message.Model;
using LandmarkEmulator.Database.Auth.Model;
using LandmarkEmulator.Shared;
using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Network;
using NLog;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

namespace LandmarkEmulator.AuthServer.Zone
{
    public class ZoneServerManager : Singleton<ZoneServerManager>
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>A world is up while its heartbeat (written every 5 s, WorldServer Game.ServerHeartbeat) is this recent.</summary>
        private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(3);

        public ImmutableList<ZoneServer> ZoneServers { get; private set; }

        private DateTime nextCheck;
        private Task<ImmutableList<ZoneServerModel>> pending;

        public void Initialise()
        {
            // AuthServer.json WorldAddress, when set, replaces the stored host (see AuthServerConfiguration.WorldAddress).
            string address = ConfigurationManager<AuthServerConfiguration>.Instance.Config?.WorldAddress?.Trim();
            ZoneServers = DatabaseManager.Instance.AuthDatabase.GetZoneServers()
                .Select(s =>
                {
                    if (!string.IsNullOrEmpty(address))
                        s.Host = address;
                    var server = new ZoneServer(s);
                    Apply(server, s);
                    return server;
                })
                .ToImmutableList();
        }

        /// <summary>Is a world's row (read just now) that of a running world?</summary>
        public static bool IsRunning(ZoneServerModel model) =>
            model?.Heartbeat is DateTime beat && DateTime.UtcNow - beat < HeartbeatTimeout;

        private static bool Apply(ZoneServer server, ZoneServerModel model)
        {
            bool up = IsRunning(model);
            bool changed = up != server.Up;
            server.Up = up;
            server.PlayersOnline = up ? model.PlayersOnline : 0;
            return changed;
        }

        /// <summary>
        /// Login server tick: re-reads the worlds' heartbeats every few seconds (off the tick thread) and, when a world
        /// comes up or goes down, sends its entry again (ServerUpdate) to every logged-in client, so character select
        /// follows it without logging in again.
        /// </summary>
        public void Update()
        {
            if (pending != null)
            {
                if (!pending.IsCompleted)
                    return;
                var task = pending;
                pending = null;
                if (task.IsCompletedSuccessfully)
                    foreach (ZoneServerModel model in task.Result)
                        if (ZoneServers.FirstOrDefault(z => z.Id == model.Id) is ZoneServer server && Apply(server, model))
                        {
                            log.Info($"World {server.Id} ({server.ZoneName}) is {(server.Up ? "up" : "down")}.");
                            var update = new ServerUpdate { Server = server.Build() };
                            foreach (AuthSession session in NetworkManager<AuthSession>.Instance.Sessions().Where(s => s.Account != null))
                                session.EnqueueMessage(update);
                        }
            }
            if (DateTime.UtcNow < nextCheck)
                return;
            nextCheck = DateTime.UtcNow + CheckInterval;
            pending = Task.Run(() => DatabaseManager.Instance.AuthDatabase.GetZoneServers());
        }
    }
}
