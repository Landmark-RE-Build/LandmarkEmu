using LandmarkEmulator.Shared.Configuration;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The world's player limit for the login queue (TASKS E.8). The queue itself is the login server's
    /// (AuthServer Game/LoginQueue): live's client queues a login at character select — CharacterLoginReply result 8
    /// opens its queue dialog, login tunnel type 3 updates the place, type 4 lets it log in — so the world only reports
    /// its limit with every heartbeat (<see cref="ServerHeartbeat"/>, zone_server.maxPlayers) beside its player count.
    /// The zone's own queue packet, PacketLoginQueueStatus 0xE0 (FUN_143171ae0 → FUN_14368afa0 state 2), belongs to
    /// the travel queue: only TeleportToQueuedLocation / TeleportCancelQueue (TeleportBase 6 / 7, FUN_143173a90 /
    /// FUN_143171570) close that dialog again (<see cref="TravelQueue"/>, L60). Tried first as a world-entry hold on
    /// 2026-10-17: the login waited and continued, but the client's dialog stayed open in the world.
    /// </summary>
    public static class LoginQueue
    {
        /// <summary>A limit set at run time (dev command "maxplayers"), else the configuration's.</summary>
        public static int? Override { get; set; }

        /// <summary>Dev "maxplayers full": the login server queues every login (single-client tests of the queue).</summary>
        public const int Full = -1;

        public static int MaxPlayers => Override ?? ConfigurationManager<WorldServerConfiguration>.Instance.Config?.MaxPlayers ?? 0;

        /// <summary>The limit as the heartbeat reports it: 0 = none, uint.MaxValue = full.</summary>
        public static uint ReportedLimit => MaxPlayers == Full ? uint.MaxValue : MaxPlayers > 0 ? (uint)MaxPlayers : 0u;
    }
}
