using LandmarkEmulator.Database.Configuration;
using LandmarkEmulator.Shared.Configuration;

namespace LandmarkEmulator.SocialServer
{
    /// <summary>
    /// SocialServer.json, overridable by environment variables with the same names (Database__Social__ConnectionString,
    /// Network__Port, ImportFrom ...), like the other servers.
    /// </summary>
    public class SocialServerConfiguration
    {
        /// <summary>
        /// Where the world servers (and later the gateways and zones) reach the service: Host / Port of its internal TCP
        /// listener (internal-transport.md framing). Default 20200 (social-service.md §6 step 2: SOCIAL_ADDRESS=social:20200).
        /// Never published outside the Docker network.
        /// </summary>
        public NetworkConfig Network { get; set; } = new() { Host = "0.0.0.0", Port = 20200 };

        /// <summary>Only <see cref="DatabaseConfig.Social"/> is used: the service's own database, lmemu_social.</summary>
        public DatabaseConfig Database { get; set; }

        /// <summary>
        /// The database to copy today's friends / ignore lists and character names from on the first start
        /// (social-service.md §5.7, Database.Social/SocialImport.cs), normally lmemu_character on the same server.
        /// Empty = no copy. The copy runs only while the social tables are empty.
        /// </summary>
        public string ImportFrom { get; set; } = "";

        /// <summary>
        /// Seconds a character who left a world still counts as online before friends are told it went offline
        /// (social-service.md §5.5, ours: 30 s), so a change of world (left on one, entered on another) shows nothing.
        /// </summary>
        public double PresenceGraceSeconds { get; set; } = 30;
    }
}
