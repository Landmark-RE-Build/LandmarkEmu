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
        /// Keep the vertex-only voxels (surface position, no material) that clients send with build edits: they keep
        /// edited surfaces round when the server sends the cell back. Off until confirmed safe in the client (server-
        /// written ones once froze its mesher). Dev "vertexonly on|off" switches it while running.
        /// </summary>
        public bool KeepVertexOnlyVoxels { get; set; }

        /// <summary>
        /// Serve coarse (LOD) copies of player edits (Game/Terrain/TerrainLod), as live did: without them edits vanish
        /// when the client draws the area at a coarser level (from a distance). On by default.
        /// </summary>
        public bool TerrainLodCopies { get; set; } = true;

        /// <summary>
        /// The Landmark 0.589 install the server reads world data from (biome maps, cave placements, models; read-only,
        /// game files are never changed). Required: set it in WorldServer.json (see SETUP.md).
        /// </summary>
        public string GameDirectory { get; set; } = "";

        /// <summary>This world's row in the login database's zone_server table (its heartbeat for character select).</summary>
        public ulong ZoneServerId { get; set; } = 1;

        /// <summary>Continent of the starting zone (sent in SendZoneDetails).</summary>
        public uint Continent { get; set; } = 406;

        /// <summary>
        /// Player Studio peer review: approvals that list a product, and rejections that turn it down (the live
        /// server's numbers are not in the client).
        /// </summary>
        public uint PlayerStudioApprovals { get; set; } = 3;

        /// <summary>
        /// Message of the day, shown once per login (PacketMOTD 0x5A): the client prints "subject\nbody" as a system
        /// message and adds a row to its UI.MessageOfTheDay data source. Nothing is sent when both are empty.
        /// </summary>
        public string MotdSubject { get; set; } = "Welcome to Landmark";

        /// <summary>
        /// Admins: account names or character names allowed the in-game admin menu (/admin, <see cref="Game.Admin"/>).
        /// </summary>
        public System.Collections.Generic.List<string> Admins { get; set; } = new();
        public string MotdBody { get; set; } = "This server is LandmarkEmu, a fan-made recreation. Have fun building!";
        public uint PlayerStudioRejections { get; set; } = 3;
    }
}
