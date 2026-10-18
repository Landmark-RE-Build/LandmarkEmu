using LandmarkEmulator.Database.Social;
using LandmarkEmulator.Shared.Configuration;
using Microsoft.Extensions.Hosting;
using NLog;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace LandmarkEmulator.SocialServer
{
    /// <summary>
    /// Start: the database (create / migrate lmemu_social, then the one-time import of §5.7 when configured and the
    /// tables are empty), then the internal listener the worlds dial. Stop: closes the links; the worlds keep running
    /// and reconnect by themselves (their InternalDialer), sending PresenceSnapshot.
    /// </summary>
    public class HostedService : IHostedService
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private SocialHub hub;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            log.Info("Initialising...");
            SocialServerConfiguration config = ConfigurationManager<SocialServerConfiguration>.Instance.Config;
            if (config.Database?.Social == null || string.IsNullOrWhiteSpace(config.Database.Social.ConnectionString))
                throw new InvalidOperationException("SocialServer.json: Database:Social:ConnectionString is not set.");

            var database = new SocialDatabase(config.Database);
            database.Migrate();
            SocialImport.RunIfEmpty(database, config.ImportFrom);
            SocialImport.RunGuildsIfEmpty(database, config.ImportFrom);
            SocialImport.RunMailIfEmpty(database, config.ImportFrom);

            IPAddress address = IPAddress.TryParse(config.Network?.Host, out IPAddress a) ? a : IPAddress.Any;
            int port = config.Network?.Port is ushort p && p != 0 ? p : 20200;
            hub = new SocialHub(database, address, port, TimeSpan.FromSeconds(Math.Max(0, config.PresenceGraceSeconds)));
            hub.Start();

            log.Info("Service Started!");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            log.Info("Ending Service...");
            hub?.Dispose();
            log.Info("Service Ended!");
            return Task.CompletedTask;
        }
    }
}
