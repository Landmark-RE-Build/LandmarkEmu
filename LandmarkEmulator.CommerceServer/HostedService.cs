using LandmarkEmulator.Shared.Configuration;
using Microsoft.Extensions.Hosting;
using NLog;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace LandmarkEmulator.CommerceServer
{
    /// <summary>
    /// Start: the database (create lmemu_commerce and its tables; the one-time copy of the in-process Player Studio rows
    /// when the product table is new and ImportFrom is set), then the internal listener the worlds dial. Stop: closes the
    /// links; the worlds keep running, answer what they must while it is away (world-channel-service.md §7) and reconnect
    /// by themselves (their InternalDialer), sending CommerceHello again.
    /// </summary>
    public class HostedService : IHostedService
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>Opening the store: attempts and the pause between them (the database container may still be starting).</summary>
        private const int StoreAttempts = 30;
        private static readonly TimeSpan StoreRetry = TimeSpan.FromSeconds(10);

        private CommerceHub hub;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            log.Info("Initialising...");
            CommerceServerConfiguration config = ConfigurationManager<CommerceServerConfiguration>.Instance.Config;
            if (string.IsNullOrWhiteSpace(config.Database))
                throw new InvalidOperationException("CommerceServer.json: Database (the lmemu_commerce connection string) is not set.");

            CommerceStore store = null;
            for (int attempt = 1; attempt <= StoreAttempts && store == null; attempt++)
            {
                store = CommerceStore.Open(config.Database);
                if (store == null && attempt < StoreAttempts)
                    await Task.Delay(StoreRetry, cancellationToken);
            }
            if (store == null)
                throw new InvalidOperationException($"The commerce database stayed unreachable ({StoreAttempts} attempts).");

            // The one-time copy (spec §8 step 8): the in-process tables in the character database, as world ImportWorldId.
            if (!string.IsNullOrWhiteSpace(config.ImportFrom))
                log.Info($"Player Studio import: {store.ImportProducts(config.ImportFrom, config.ImportWorldId, string.IsNullOrWhiteSpace(config.ImportRealm) ? config.ImportFrom : config.ImportRealm)}.");

            IPAddress address = IPAddress.TryParse(config.Network?.Host, out IPAddress a) ? a : IPAddress.Any;
            int port = config.Network?.Port is ushort p && p != 0 ? p : 20400;
            hub = new CommerceHub(config, store, address, port);
            hub.Start();

            log.Info("Service Started!");
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
