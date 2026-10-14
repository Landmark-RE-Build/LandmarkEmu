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
        /// edited surfaces smooth (line slopes, spheres) when the server sends the cell back; dropping them gave
        /// saw-toothed lines and stair-stepped fills after a relog (2026-10-14). The client stores and meshes them
        /// itself (its own cache, Cache/2/Voxel/416, held 67,662 of them in 45 of the player's cells). The server never
        /// makes such voxels itself. Dev "vertexonly on|off" switches it while running.
        /// </summary>
        public bool KeepVertexOnlyVoxels { get; set; } = true;

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

        /// <summary>
        /// This world's name in the server list: a client text id (0 = keep the stored one). The client has the live
        /// server names: 560911 Courage, 560912 Liberation, 560913 Satisfaction (EU), 560914 Understanding (EU),
        /// 560915 Rebellion, 560916 Serenity, 560917 Determination, 560918 Confidence, 560919 Adventure, 560920 Gratitude,
        /// 560921 Prosperity, 560922 Success, 560923 Triumph, 560924 Victory, 560925 Opportunity, 560926 Inspiration;
        /// 62147 is "Dev Server".
        /// </summary>
        public uint WorldNameId { get; set; }

        /// <summary>
        /// "host:port" game clients use to reach this world, written to its server-list entry at start (empty = keep the
        /// stored one). Each world container sets its own (docs/docker.md).
        /// </summary>
        public string PublicAddress { get; set; } = "";

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
