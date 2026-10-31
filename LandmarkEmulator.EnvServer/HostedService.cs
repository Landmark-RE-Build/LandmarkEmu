using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.WorldServer.Game.Terrain;
using Microsoft.Extensions.Hosting;
using NLog;
using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace LandmarkEmulator.EnvServer
{
    /// <summary>
    /// Start: the read-only inputs (cave pieces, ground heights, vein data, templates folder), the one-time copy of the
    /// world's TerrainEdits (<see cref="DataImport"/>), the store (overlays, ownership, heal queue), the LOD copies in the
    /// background, then the internal listener the zone dials (<see cref="EnvHub"/>). Stop: the hub's loop ends and the store
    /// is written to disk; the zone keeps running, answers from its cache and refuses edits while the service is away
    /// (terrain-service.md §6), and reconnects by itself (its InternalDialer).
    /// </summary>
    public class HostedService : IHostedService
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private EnvHub hub;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            log.Info("Initialising...");
            EnvServerConfiguration config = ConfigurationManager<EnvServerConfiguration>.Instance.Config;
            string data = Path.GetFullPath(string.IsNullOrWhiteSpace(config.DataPath) ? "Data" : config.DataPath);
            Directory.CreateDirectory(data);
            if (string.IsNullOrWhiteSpace(config.GameDirectory) || !Directory.Exists(config.GameDirectory))
                log.Error($"EnvServer.json GameDirectory '{config.GameDirectory}' is not a Landmark install: caves, ore veins and "
                    + "templates will be missing.");

            // The same switches the world server sets for its in-process store (WorldServer.json), plus crash-safe writes.
            TerrainLod.Enabled = config.TerrainLodCopies;
            BlockData.KeepVertexOnly = config.KeepVertexOnlyVoxels;
            OreVeins.PioneerSurfaceVeins = OreVeins.ParseSurfaceVeins(config.PioneerSurfaceVeins);
            OreVeins.PioneerSurfaceIron = config.PioneerSurfaceIron;
            TerrainEdits.AtomicWrites = true;
            VoxelTemplate.Folder = Path.Combine(config.GameDirectory ?? "", "CommonData", "Voxel", "templates");
            CavePieces.Init(config.GameDirectory);
            TerrainHeights.Load(config.Continent);

            // Step 4 of the spec's build plan: the world's TerrainEdits copied once into this service's own folder.
            log.Info($"Data import: {DataImport.Run(config.ImportFrom, data)}");

            IPAddress address = IPAddress.TryParse(config.Network?.Host, out IPAddress a) ? a : IPAddress.Any;
            int port = config.Network?.Port is ushort p && p != 0 ? p : 20500;
            hub = new EnvHub(config, data, address, port);
            hub.Start();

            log.Info("Service Started!");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            log.Info("Ending Service...");
            hub?.Dispose();
            log.Info("Service Ended!");
            return Task.CompletedTask;
        }
    }
}
