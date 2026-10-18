using LandmarkEmulator.Shared.Configuration;
using Microsoft.Extensions.Hosting;
using NLog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldChannel
{
    /// <summary>Start: the links to the gateway and the zone. Stop: closes them (the gateway then routes channel 1 to the zone).</summary>
    public class HostedService : IHostedService
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        private Timer report, sweep;

        /// <summary>Opening the vote store: attempts and the pause between them (the database container may still be starting).</summary>
        private const int StoreAttempts = 30;
        private static readonly TimeSpan StoreRetry = TimeSpan.FromSeconds(10);

        public Task StartAsync(CancellationToken cancellationToken)
        {
            log.Info("Initialising...");
            WorldChannelConfiguration config = ConfigurationManager<WorldChannelConfiguration>.Instance.Config;
            if (string.IsNullOrWhiteSpace(config.GatewayInternalAddress) || string.IsNullOrWhiteSpace(config.ZoneInternalAddress))
                throw new InvalidOperationException("WorldChannel.json: GatewayInternalAddress and ZoneInternalAddress are needed.");
            if (!string.IsNullOrWhiteSpace(config.LoginInternalAddress))
                Logout.Login = new Internal.Login.LoginClient(config.LoginInternalAddress, text => log.Info(text));
            ChannelRelay.Start(config);
            // Build-site ratings (world-channel-service.md §6.1, §8 step 6): this service's own store, filled once from the
            // zone's kind-11 rows (and topped up with the likes the zone counted while this service was away). Opened in
            // the background, retried while the database container is still starting; until it is open (and the import
            // done) the likes are handed on to the zone as before.
            if (config.ClaimRatings && !string.IsNullOrWhiteSpace(config.Database))
                Task.Run(async () =>
                {
                    for (int attempt = 1; attempt <= StoreAttempts; attempt++)
                    {
                        VoteStore store = VoteStore.Open(config.Database);
                        if (store != null)
                        {
                            store.Import(config.ImportFrom);
                            RatingBroker.Store = store;
                            log.Info("Claim ratings: answered by the world channel.");
                            return;
                        }
                        await Task.Delay(StoreRetry);
                    }
                    log.Warn($"Claim ratings: the world-channel database stayed unreachable ({StoreAttempts} attempts); the likes stay with the zone.");
                });
            else if (config.ClaimRatings)
                log.Info("Claim ratings: no world-channel database (Database); the likes are handed on to the zone.");
            report = new Timer(_ =>
            {
                (long relayed, long dropped) = ChannelRelay.Counts;
                (int encounters, int challenges) = EncounterBroker.Count;
                log.Info($"Channel 1: {relayed} packet(s) handed on, {dropped} dropped; {TradeBroker.Count} brokered trade(s), {encounters} encounter(s), {challenges} open duel challenge(s).");
            }, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
            // Stale duel challenges expire (the zone's own tick did this in-process; world-channel-service.md §6.5).
            sweep = new Timer(_ => EncounterBroker.Sweep(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            log.Info("Service Started!");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            log.Info("Ending Service...");
            report?.Dispose();
            sweep?.Dispose();
            ChannelRelay.Stop();
            Logout.Login?.Dispose();
            log.Info("Service Ended!");
            return Task.CompletedTask;
        }
    }
}
