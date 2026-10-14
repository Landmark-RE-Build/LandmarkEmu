using LandmarkEmulator.Database.Configuration;
using LandmarkEmulator.Gateway;
using LandmarkEmulator.Shared;
using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.GameTable;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using Microsoft.Extensions.Hosting;
using NLog;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldServer
{
    public class HostedService : IHostedService
    {
        protected static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public HostedService(Microsoft.Extensions.Hosting.IHostApplicationLifetime lifetime)
        {
            // The shutdown countdown (dev command "shutdown") stops the server through the host.
            Game.Shutdown.StopServer = lifetime.StopApplication;
        }

        /// <summary>
        /// Writes this world's entry in the server list (lmemu_auth.zone_server: id, name text, public address), so a new
        /// world container shows up at character select without editing the database (docs/docker.md).
        /// </summary>
        private static void RegisterInServerList()
        {
            WorldServerConfiguration config = ConfigurationManager<WorldServerConfiguration>.Instance.Config;
            string host = null;
            ushort port = 0;
            string address = config.PublicAddress?.Trim();
            if (!string.IsNullOrEmpty(address))
            {
                int colon = address.LastIndexOf(':');
                if (colon > 0 && ushort.TryParse(address[(colon + 1)..], out ushort p))
                    (host, port) = (address[..colon], p);
                else
                    host = address;
            }
            DatabaseManager.Instance.AuthDatabase.RegisterZoneServer(config.ZoneServerId, config.WorldNameId, host, port).Wait();
            log.Info($"World {config.ZoneServerId} registered in the server list" + (host != null ? $" at {host}:{(port != 0 ? port : config.Network.Port)}." : "."));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            log.Info("Initialising...");

            DatabaseManager.Instance.Initialise(ConfigurationManager<WorldServerConfiguration>.Instance.Config.Database);
            DatabaseManager.Instance.Migrate(DatabaseType.Auth);
            DatabaseManager.Instance.Migrate(DatabaseType.Character);
            RegisterInServerList();

            GameTableManager.Instance.Initialise();
            Game.Inventory.ItemDefinitionManager.Load(System.IO.Path.Combine(System.AppContext.BaseDirectory, "Resources", "PacketDumps", "44-CommandBase"));

            Game.Props.LoadDefinitions(System.IO.Path.Combine(System.AppContext.BaseDirectory, "Resources", "PacketDumps", "145-PropBase"));
            Game.Props.LoadWorld();
            Game.Chests.Load();
            Game.Claims.Load();
            Game.WebPages.Start();
            var worldConfig = ConfigurationManager<WorldServerConfiguration>.Instance.Config;
            if (string.IsNullOrWhiteSpace(worldConfig.GameDirectory) || !System.IO.Directory.Exists(worldConfig.GameDirectory))
                log.Error($"WorldServer.json GameDirectory '{worldConfig.GameDirectory}' is not a Landmark 0.589 install: biomes, caves, "
                    + "models and travel data will be missing (see SETUP.md).");
            Game.Terrain.TerrainMaterials.Load(worldConfig.GameDirectory, worldConfig.Continent);
            Game.Terrain.OreVeins.Load(worldConfig.GameDirectory, worldConfig.Continent);
            Game.Terrain.CavePieces.Init(worldConfig.GameDirectory);
            Game.Terrain.TerrainHeights.Load(worldConfig.Continent);
            Game.Caves.Load(worldConfig.GameDirectory, worldConfig.Continent);
            Game.WarpNetwork.Load(worldConfig.GameDirectory, worldConfig.Continent);
            Game.LootChests.Load(worldConfig.GameDirectory, worldConfig.Continent);
            Game.Achievements.Load();
            Game.ModelFootprints.Load(worldConfig.GameDirectory);
            Game.Landscapes.Load(worldConfig.GameDirectory);
            Game.Gathering.Load(System.IO.Path.Combine(System.AppContext.BaseDirectory, "gathered_nodes.txt"));
            Game.Terrain.BlockData.KeepVertexOnly = ConfigurationManager<WorldServerConfiguration>.Instance.Config.KeepVertexOnlyVoxels;
            Game.Terrain.TerrainLod.Enabled = ConfigurationManager<WorldServerConfiguration>.Instance.Config.TerrainLodCopies;
            Game.Terrain.TerrainEdits.Load(System.IO.Path.Combine(System.AppContext.BaseDirectory, "TerrainEdits"));
            Game.Terrain.TerrainLod.BuildAll();   // coarse copies of the edits, in the background
            Game.Terrain.VoxelTemplate.Folder = System.IO.Path.Combine(worldConfig.GameDirectory ?? "", "CommonData", "Voxel", "templates");
            Game.Spire.Load(worldConfig.GameDirectory, System.IO.Path.Combine(System.AppContext.BaseDirectory, "TerrainEdits"));
            Game.Outposts.Load(System.IO.Path.Combine(System.AppContext.BaseDirectory, "TerrainEdits"));
            // Start-up reads (templates, cave data, stamping) leave a lot of garbage: one compacting collection so the
            // idle server doesn't hold it (measured: ~1.19 GB before, ~0.93 GB after).
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, true, true);

            MessageManager.Instance.Initialise();
            GatewayProvider.Instance.Initialise();
            ZoneMessageManager.Instance.Initialise();

            //AuthAssetManager.Instance.Initialise();

            // Each system runs guarded: an exception in one (a bug, or a hot reload that left a new static field null)
            // is logged — at most once a minute per system, the loop runs ~1000 times a second — instead of ending the
            // world thread and with it the server.
            var lastFailure = new Dictionary<string, DateTime>();
            void Run(string name, Action update)
            {
                try
                {
                    update();
                }
                catch (Exception e)
                {
                    DateTime now = DateTime.UtcNow;
                    if (!lastFailure.TryGetValue(name, out DateTime last) || now - last > TimeSpan.FromMinutes(1))
                    {
                        lastFailure[name] = now;
                        log.Error(e, $"World update of {name} failed.");
                    }
                }
            }

            ThreadManager.Instance.Initialise(lastTick =>
            {
                Run("network", () => NetworkManager<WorldSession>.Instance.Update(lastTick));
                Run("dev commands", () => Game.DevCommands.Update(Game.PlayerManager.Snapshot()));
                Run("crafting", Game.Crafting.Update);
                Run("achievements", Game.Achievements.Update);
                Run("props", Game.Props.Update);
                Run("gathering", Game.Gathering.Update);
                Run("claims", Game.Claims.Update);
                Run("health", Game.Health.Update);
                Run("buffs", Game.Buffs.Update);
                Run("party frames", Game.Groups.Update);
                Run("world editing", Game.WorldEditing.Update);
                Run("creatures", Game.Creatures.Update);
                Run("combat states", Game.Combat.Statuses.Update);
                Run("outposts", Game.Outposts.Update);
                Run("loot chests", Game.LootChests.Update);
                Run("site games", Game.InstancedGames.Update);
                Run("story tools", Game.StoryTools.Update);
                Run("prop settings", Game.PropSettings.Flush);
                Run("deferred re-zones", Game.Teleport.Update);
                Run("web pages", Game.WebPages.Update);
                Run("store", Game.Store.Update);
                Run("encounters", Game.Encounters.Update);
                Run("shutdown", Game.Shutdown.Update);
                Run("players", Game.PlayerManager.Update);
                Run("heartbeat", Game.ServerHeartbeat.Update);
                Run("terrain regeneration", () => Game.Terrain.TerrainEdits.Regenerate(Game.Claims.BoxesIn));
                Run("ore veins", Game.Terrain.OreVeins.Update);
                Run("terrain", Game.Terrain.TerrainEdits.Flush);
                Run("terrain layers (one-time move)", () => Game.Terrain.TerrainEdits.MigrateToLayers(Game.Claims.BoxesIn));
                Run("terrain LOD", Game.Terrain.TerrainLod.Update);
            });

            NetworkManager<WorldSession>.Instance.Initialise(ConfigurationManager<WorldServerConfiguration>.Instance.Config.Network);

            //CommandManager.Instance.Initialise(new AuthCommandHandler());

            log.Info($"Service Started!");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            log.Info($"Ending Service...");
            // The login server hears first, so nobody is sent in while the world closes (it re-reads within 3 s).
            Game.Shutdown.BeginStopping();
            Game.ServerHeartbeat.Stopped();
            // Players see the shutdown notice before the connection drops.
            Game.Shutdown.NotifyStopping();
            System.Threading.Thread.Sleep(1000);
            // Up to a minute: a client still loading (the item definitions alone take ~15 s) only reads its relogin
            // session when it is done, and must still be connected then - if the server closes first, the client sees
            // the drop while starting its relogin and quits (2026-10-14). Ends as soon as everyone has left.
            // At least 5 s even with nobody connected: a client given the world's address just before the login server
            // heard of the shutdown (it re-reads every 3 s) is still on its way.
            Game.Shutdown.SendPlayersToCharacterSelect(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5));

            NetworkManager<WorldSession>.Instance.Shutdown();
            Game.ServerHeartbeat.Stopped();

            ThreadManager.Instance.Shutdown();

            log.Info($"Service Ended!");
            return Task.CompletedTask;
        }
    }
}
