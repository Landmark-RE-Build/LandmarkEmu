using LandmarkEmulator.Gateway;
using LandmarkEmulator.Shared;
using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using Microsoft.Extensions.Hosting;
using NLog;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace LandmarkEmulator.GatewayServer
{
    /// <summary>
    /// Start: the databases the login check reads, the internal port the zone dials, then the client UDP port and one
    /// tick loop (the SOE sessions and what the zone link queued for them). The world's migrations stay with the world.
    /// </summary>
    public class HostedService : IHostedService
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public Task StartAsync(CancellationToken cancellationToken)
        {
            log.Info("Initialising...");
            GatewayServerConfiguration config = ConfigurationManager<GatewayServerConfiguration>.Instance.Config;
            string login = !string.IsNullOrWhiteSpace(config.LoginInternalAddress) ? config.LoginInternalAddress
                : Environment.GetEnvironmentVariable("LOGIN_INTERNAL_ADDRESS");
            if (!string.IsNullOrWhiteSpace(login))
            {
                GatewayClient.Login = new Internal.Login.LoginClient(login, text => log.Info(text));
                log.Info($"Login checks through the login service at {login}.");
            }
            else if (config.Database?.Auth == null || config.Database.Character == null)
                throw new InvalidOperationException("GatewayServer.json: set LoginInternalAddress, or Database:Auth and Database:Character for the login check.");
            else
                DatabaseManager.Instance.Initialise(config.Database);
            GatewayClient.HoldLimit = TimeSpan.FromSeconds(System.Math.Max(0, config.HoldSeconds));
            SocketHeartbeat.DefaultTimeoutSeconds = config.ClientTimeoutSeconds;
            log.Info(SocketHeartbeat.DefaultTimeoutSeconds > 0
                ? $"A silent client is disconnected after {SocketHeartbeat.DefaultTimeoutSeconds:0} s."
                : "Silent clients are never timed out (CLIENT_TIMEOUT_SECONDS=0).");

            MessageManager.Instance.Initialise();
            GatewayProvider.Instance.Initialise();

            IPAddress internalAddress = IPAddress.TryParse(config.Internal?.Host, out IPAddress a) ? a : IPAddress.Any;
            ZoneBackEnds.Mode = ZoneBackEnds.ParseMultiWorld(config.MultiWorld);
            log.Info($"Multi-world: {ZoneBackEnds.Mode}.");
            ZoneBackEnds.Start(internalAddress, config.Internal?.Port is ushort p && p != 0 ? p : 20300, config.WorldId);

            ThreadManager.Instance.Initialise(lastTick =>
            {
                try
                {
                    NetworkManager<GatewayClient>.Instance.Update(lastTick);
                    ZoneBackEnds.SendStatusWhenDue();
                }
                catch (Exception e)
                {
                    log.Error(e, "Gateway tick failed.");
                }
            });
            NetworkManager<GatewayClient>.Instance.Initialise(config.Network);

            log.Info("Service Started!");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            log.Info("Ending Service...");
            NetworkManager<GatewayClient>.Instance.Shutdown();
            ZoneBackEnds.Stop();
            GatewayClient.Login?.Dispose();
            ThreadManager.Instance.Shutdown();
            log.Info("Service Ended!");
            return Task.CompletedTask;
        }
    }
}
