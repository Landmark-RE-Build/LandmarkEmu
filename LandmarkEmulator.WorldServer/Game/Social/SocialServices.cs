using NLog;
using System;

namespace LandmarkEmulator.WorldServer.Game.Social
{
    /// <summary>
    /// Where the zone finds the social service and its caches (trace/architecture/social-service.md §6 step 1). Every
    /// caller outside the social code goes through these, never through Friends / Ignores / Guilds / ChatRooms / Mail
    /// directly, so the local implementation can be swapped for the remote one in one place: <see cref="Configure"/>
    /// at start (§6 step 3) picks <see cref="RemoteSocialService"/> when SOCIAL_ADDRESS is set, and otherwise leaves all
    /// five on one <see cref="LocalSocialService"/>, exactly as before (and the remote-only Game Finder and groups
    /// across worlds null).
    /// </summary>
    public static class SocialServices
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private static readonly LocalSocialService local = new();

        /// <summary>Requests and connection events the zone hands to the social service.</summary>
        public static ISocialService Service { get; private set; } = local;

        /// <summary>Ignore checks for what the zone delivers itself (IgnoreListSync once remote).</summary>
        public static IIgnoreCache IgnoreCache { get; private set; } = local;

        /// <summary>Guild membership and names (SocialStateForZone / GuildNameToId once remote).</summary>
        public static IGuildCache GuildCache { get; private set; } = local;

        /// <summary>Who lists a character as a friend (SocialStateForZone once remote).</summary>
        public static IFriendOfCache FriendCache { get; private set; } = local;

        /// <summary>Where an online character is (PresenceQuery once remote).</summary>
        public static IPresenceLookup Presence { get; private set; } = local;

        /// <summary>
        /// The cross-world Game Finder (social-service.md §9): the remote service, or null in-process, where the zone's
        /// own Game Finder counts this world alone as before (InstancedGames.Finder.cs).
        /// </summary>
        public static IMatchmakingService Matchmaking { get; private set; }

        /// <summary>
        /// Groups across worlds (TASKS B.9, social-service.md §10): the remote service, or null in-process, where
        /// <see cref="Game.Groups"/> keeps this world's groups itself exactly as before.
        /// </summary>
        public static IGroupService Groups { get; private set; }

        /// <summary>
        /// Picks the implementation, once at start before players can connect: WorldServer.json SocialAddress, else the
        /// environment variable SOCIAL_ADDRESS ("host:port", e.g. social:20200); empty = local.
        /// </summary>
        public static void Configure(string socialAddress, ulong worldId)
        {
            string address = !string.IsNullOrWhiteSpace(socialAddress) ? socialAddress.Trim()
                : Environment.GetEnvironmentVariable("SOCIAL_ADDRESS")?.Trim();
            if (string.IsNullOrEmpty(address))
            {
                log.Info("Social service: local (SOCIAL_ADDRESS not set).");
                return;
            }
            var remote = new RemoteSocialService(address, (uint)worldId);
            Service = remote;
            IgnoreCache = remote;
            GuildCache = remote;
            FriendCache = remote;
            Presence = remote;
            Matchmaking = remote;
            Groups = remote.Groups;
            remote.Start();
        }

        /// <summary>World thread, every tick: work the remote service's link queued (mail hand-overs, guild goal), and the
        /// deleted-character clean-up.</summary>
        public static void Update()
        {
            (Service as RemoteSocialService)?.Update();
            // Deleted characters leave friends lists and guilds (TASKS 6.42); looks at the database once a minute.
            CharacterDeletion.Update();
        }

        /// <summary>Closes the link to a remote service (at shutdown, after the players have left).</summary>
        public static void Stop() => (Service as IDisposable)?.Dispose();
    }
}
