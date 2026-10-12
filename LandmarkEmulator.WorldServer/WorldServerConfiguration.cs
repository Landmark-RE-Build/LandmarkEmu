using LandmarkEmulator.Database.Configuration;
using LandmarkEmulator.Shared.Configuration;

namespace LandmarkEmulator.WorldServer
{
    public class WorldServerConfiguration
    {
        public NetworkConfig Network { get; set; }
        public DatabaseConfig Database { get; set; }
        public string GameDataPath { get; set; }
        /// <summary>
        /// Lets every player build and place props anywhere, without a claim (the client's claim checks pass when the
        /// SendSelf flag at +0x287 is set). For testing and creative servers; off by default.
        /// </summary>
        public bool FreeBuild { get; set; }

        /// <summary>
        /// The Landmark 0.589 install the server reads world data from (biome maps, cave placements, models; read-only,
        /// game files are never changed). Required: set it in WorldServer.json (see SETUP.md).
        /// </summary>
        public string GameDirectory { get; set; } = "";

        /// <summary>Continent of the starting zone (sent in SendZoneDetails).</summary>
        public uint Continent { get; set; } = 406;

        /// <summary>
        /// Player Studio peer review: approvals that list a product, and rejections that turn it down (the live
        /// server's numbers are not in the client).
        /// </summary>
        public uint PlayerStudioApprovals { get; set; } = 3;
        public uint PlayerStudioRejections { get; set; } = 3;
    }
}
