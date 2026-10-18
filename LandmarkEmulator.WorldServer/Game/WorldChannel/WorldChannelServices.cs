using NLog;
using System;

namespace LandmarkEmulator.WorldServer.Game.WorldChannel
{
    /// <summary>
    /// The world-channel back ends the zone code calls (trace/architecture/world-channel-service.md, build step 1): the
    /// per-world <see cref="IWorldChannelService"/> and the shared <see cref="ICommerceService"/>. Both are the
    /// in-process implementations until the services run in their own containers; then a network client takes their
    /// place here and no caller changes. Commerce (build step 8): <see cref="Configure"/> picks
    /// <see cref="RemoteCommerceService"/> when COMMERCE_ADDRESS is set.
    /// </summary>
    public static class WorldChannelServices
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>Trade broker, encounter lifecycle, claim votes, character-select session, locale, teleport queue, telemetry.</summary>
        public static IWorldChannelService WorldChannel { get; set; } = new LocalWorldChannelService();

        /// <summary>Player Studio / Marketplace, wallet, account store, reviews, contests, portraits, Showcase pages.</summary>
        public static ICommerceService Commerce { get; set; } = new LocalCommerceService();

        /// <summary>
        /// Picks the commerce implementation, once at start before players can connect: WorldServer.json CommerceAddress,
        /// else the environment variable COMMERCE_ADDRESS ("host:port", e.g. commerce:20400); empty = in-process, exactly
        /// as before.
        /// </summary>
        public static void Configure(string commerceAddress, ulong worldId)
        {
            string address = !string.IsNullOrWhiteSpace(commerceAddress) ? commerceAddress.Trim()
                : Environment.GetEnvironmentVariable("COMMERCE_ADDRESS")?.Trim();
            if (string.IsNullOrEmpty(address))
            {
                log.Info("Commerce: in-process (COMMERCE_ADDRESS not set).");
                return;
            }
            var remote = new RemoteCommerceService(address, (uint)worldId);
            Commerce = remote;
            remote.Start();
        }

        /// <summary>Closes the link to a remote commerce service (at shutdown, after the players have left).</summary>
        public static void Stop() => (Commerce as IDisposable)?.Dispose();
    }
}
