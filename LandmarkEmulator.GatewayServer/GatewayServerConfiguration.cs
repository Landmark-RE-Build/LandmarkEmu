using LandmarkEmulator.Database.Configuration;
using LandmarkEmulator.Shared.Configuration;

namespace LandmarkEmulator.GatewayServer
{
    /// <summary>
    /// GatewayServer.json, overridable by environment variables with the same names (Network__Port, Internal__Port,
    /// Database__Auth__ConnectionString ...), like the other servers.
    /// </summary>
    public class GatewayServerConfiguration
    {
        /// <summary>The client-facing UDP port: the world's game port (WORLD_PORT; the zone no longer opens one).</summary>
        public NetworkConfig Network { get; set; } = new() { Host = "0.0.0.0", Port = 19000 };

        /// <summary>The internal TCP port the zone back ends dial (gateway-service.md §3.11: 20300, never published).</summary>
        public NetworkConfig Internal { get; set; } = new() { Host = "0.0.0.0", Port = 20300 };

        /// <summary>
        /// "host:port" of the login service's internal port (LOGIN_INTERNAL_ADDRESS, e.g. auth:20210; §3.7): ticket checks
        /// and relog sessions go there. Empty: the gateway reads <see cref="Database"/> itself, as the world did.
        /// </summary>
        public string LoginInternalAddress { get; set; } = "";

        /// <summary>
        /// Auth (the single-use server ticket) and Character (the character belongs to the account), only needed without
        /// <see cref="LoginInternalAddress"/>. Internal (no .env line): the Docker gateway always uses
        /// LOGIN_INTERNAL_ADDRESS, so these are for a gateway run outside Docker.
        /// </summary>
        public DatabaseConfig Database { get; set; }

        /// <summary>
        /// The SOE / RC4 key the clients use (today's WorldServer.EncryptionKey). Internal (no .env line): the retail
        /// client's built-in key; any other value and no client can connect.
        /// </summary>
        public string EncryptionKey { get; set; } = "F70IaxuU8C/w7FPXY1ibXw==";

        /// <summary>
        /// The world this gateway serves (WORLD_ID; the routing table's key is (world, channel), gateway-service.md §3.3).
        /// 0: the world of the first back end that registers. Back ends registering for another world are not routed.
        /// </summary>
        public uint WorldId { get; set; }

        /// <summary>
        /// Cross-world travel (TASKS B.10; OPEN-LEADS L114: the 0.695 client never reconnects to change world — it is a
        /// re-zone on the same connection, so live's gateway reached every world's zones): zones of other worlds may
        /// register too, the zones are told which worlds are reachable (WorldsReachable 0x29), and a zone may hand a
        /// connection to another world's zone (HandOver 0x25 with a target world). Logins still attach to
        /// <see cref="WorldId"/>'s zones. Values: "auto" (default) = on as soon as a zone of a second world registers here,
        /// i.e. whenever an owner points more than one world at this gateway (live: one gateway reached every world,
        /// P5734: Lost Crystal trips to other worlds, L114); a gateway with one world behaves exactly as
        /// before. "true" = on; "false" = one world only: other worlds' zones are refused (as before 2026-10-18). Docker:
        /// GATEWAY_MULTI_WORLD.
        /// </summary>
        public string MultiWorld { get; set; } = "auto";

        /// <summary>Seconds a connection waits for its zone to come back after the zone went away (gateway-service.md §3.5).</summary>
        public double HoldSeconds { get; set; } = 120;

        /// <summary>
        /// Seconds without any packet from a client before its connection is closed and its character removed from the
        /// world (a crash, a killed process, Alt+F4, a lost network): the zone hears ConnectionClosed (Timeout) at once.
        /// Default 60 = two of the 0.695 client's 29 s SOE keep-alives (UdpLibrary keepAliveDelay, FUN_143a0a980) and a
        /// little more, so one lost keep-alive is survived; below 30 is raised to 30; 0 = never (only the 120 s
        /// unacknowledged-data rule then). Silence from the zone side (a zone restart, the hold above) is a different
        /// thing and doesn't count: the client keeps sending to the gateway meanwhile. Docker: CLIENT_TIMEOUT_SECONDS (the
        /// same .env line as the world's).
        /// </summary>
        public double ClientTimeoutSeconds { get; set; } = Shared.Network.SocketHeartbeat.DefaultClientTimeoutSeconds;
    }
}
