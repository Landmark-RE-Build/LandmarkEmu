using LandmarkEmulator.Shared.Configuration;

namespace LandmarkEmulator.EnvServer
{
    /// <summary>
    /// EnvServer.json, overridable by environment variables with the same names (Network__Port, DataPath, ImportFrom ...),
    /// like the other servers. Docker: docker/envserver/docker-compose.yml.
    /// </summary>
    public class EnvServerConfiguration
    {
        /// <summary>
        /// Where the world's zone process reaches the service: Host / Port of its internal TCP listener (internal-transport.md
        /// framing). Default 20500 (Docker: ENVSERVER_ADDRESS=envserver:20500; the spec's 20300 is the gateway's internal
        /// port). Never published outside the Docker network.
        /// </summary>
        public NetworkConfig Network { get; set; } = new() { Host = "0.0.0.0", Port = 20500 };

        /// <summary>
        /// The service's own folder (spec §4.2; Docker: the volume at /data = server-data/worlds/&lt;key&gt;/envserver): its
        /// TerrainEdits/ (cells, Ownership/, heal-queue.log, layers.v2), stamps.tsv, ruins.tsv, ruin-layers.tsv and the
        /// import marker. Relative paths are next to the program.
        /// </summary>
        public string DataPath { get; set; } = "Data";

        /// <summary>
        /// The world's own TerrainEdits folder (Docker: the world's data mounted read-only at /world-data/TerrainEdits),
        /// copied once into <see cref="DataPath"/>/TerrainEdits on the first start (spec §7 step 4; marker import.done).
        /// Empty = no copy (a new world).
        /// </summary>
        public string ImportFrom { get; set; } = "";

        /// <summary>The Landmark install (read-only): cave pieces (.cav / .cpa), vein data (.vdt), templates (.otd).</summary>
        public string GameDirectory { get; set; } = "";

        /// <summary>The server's game-data folder (read-only): VoxelMaterials, ResourceVeinGroupVeins, TerrainHeights*.bin.</summary>
        public string GameDataPath { get; set; } = "gameData";

        /// <summary>The home continent (the world's starting landscape; WorldServer.json Continent).</summary>
        public uint Continent { get; set; } = 406;

        /// <summary>
        /// Run the heal queue (spec §4.3, built and verified in-process 2026-10-15): due heals give destroyed ground back.
        /// Off = heals pause (the persisted queue keeps every entry and catches up, overdue first, when switched on again).
        /// </summary>
        public bool HealQueue { get; set; } = true;

        /// <summary>
        /// Keep the ruin-layer records (spec §4.5; ruins.tsv, ruin-layers.tsv) and report them with the job counters:
        /// every stamp of a ruin template (ruin_*, cavern_ruins_*) is one instance of its landscape's ruin layer. Placement
        /// itself stays with the zone (Game/Outposts.cs) until it moves here (lead L127). Off by default.
        /// </summary>
        public bool RuinLayers { get; set; } = false;

        /// <summary>The "Desired" count of each ruin layer (0 = as many as are stamped).</summary>
        public uint RuinLayerDesired { get; set; } = 0;

        /// <summary>Coarse LOD copies of the edits (WorldServer.json TerrainLodCopies; docs/terrain.md "LOD copies").</summary>
        public bool TerrainLodCopies { get; set; } = true;

        /// <summary>Keep vertex-only voxels the client sends (WorldServer.json KeepVertexOnlyVoxels).</summary>
        public bool KeepVertexOnlyVoxels { get; set; } = true;

        /// <summary>
        /// Surface veins on the lands whose ground is known only from the surface cache (the Pioneer lands): "buried" or
        /// "off" (WorldServer.json PioneerSurfaceVeins; OreVeins.PioneerSurfaceVeins).
        /// </summary>
        public string PioneerSurfaceVeins { get; set; } = "buried";

        /// <summary>Surface iron on the Pioneer lands (WorldServer.json PioneerSurfaceIron; false = live).</summary>
        public bool PioneerSurfaceIron { get; set; } = false;

        /// <summary>Touched every 10 s while the service's loop runs (the Docker health check reads its age); empty = none.</summary>
        public string HealthFile { get; set; } = "";
    }
}
