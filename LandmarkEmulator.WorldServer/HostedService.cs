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
            // Ticket hardening: this world accepts only tickets the login server issued for it (or local test tickets).
            Gateway.Network.GatewaySession.TicketWorld = (uint)ConfigurationManager<WorldServerConfiguration>.Instance.Config.ZoneServerId;
            DatabaseManager.Instance.Migrate(DatabaseType.Auth);
            DatabaseManager.Instance.Migrate(DatabaseType.Character);
            RegisterInServerList();
            // Local social code, or the shared social service when SOCIAL_ADDRESS is set (Game/Social/SocialServices).
            Game.Social.SocialServices.Configure(ConfigurationManager<WorldServerConfiguration>.Instance.Config.SocialAddress,
                ConfigurationManager<WorldServerConfiguration>.Instance.Config.ZoneServerId);
            // In-process commerce, or the shared commerce service when COMMERCE_ADDRESS is set (Game/WorldChannel/WorldChannelServices).
            Game.WorldChannel.WorldChannelServices.Configure(ConfigurationManager<WorldServerConfiguration>.Instance.Config.CommerceAddress,
                ConfigurationManager<WorldServerConfiguration>.Instance.Config.ZoneServerId);

            GameTableManager.Instance.Initialise();
            Game.Inventory.ItemDefinitionManager.Load(System.IO.Path.Combine(System.AppContext.BaseDirectory, "Resources", "PacketDumps", "44-CommandBase"));

            Game.Props.LoadDefinitions(System.IO.Path.Combine(System.AppContext.BaseDirectory, "Resources", "PacketDumps", "145-PropBase"));
            Game.Props.LoadWorld();
            Game.Chests.Load();
            Game.Claims.Load();
            Game.WorldChannel.WorldChannelServices.Commerce.StartWebPages();
            var worldConfig = ConfigurationManager<WorldServerConfiguration>.Instance.Config;
            if (string.IsNullOrWhiteSpace(worldConfig.GameDirectory) || !System.IO.Directory.Exists(worldConfig.GameDirectory))
                log.Error($"WorldServer.json GameDirectory '{worldConfig.GameDirectory}' is not a Landmark 0.695 install: biomes, caves, "
                    + "models and travel data will be missing (set GAME_DIR in .env with Docker, see README.md and docs/docker.md).");
            Game.Watchdog.Start(worldConfig.HealthFile, worldConfig.WatchdogSeconds);
            // A client silent this long (a crash, a killed process, a lost network) is disconnected and leaves the world
            // (the in-process gateway; a separate gateway applies its own copy of the setting).
            SocketHeartbeat.DefaultTimeoutSeconds = worldConfig.ClientTimeoutSeconds;
            Game.RestartSchedule.Configure(worldConfig.RestartSchedule, worldConfig.RestartTimeZone,
                worldConfig.RestartWarningSeconds, worldConfig.RestartMessage);
            Game.Terrain.TerrainMaterials.Load(worldConfig.GameDirectory, worldConfig.Continent,
                landscapeWidth: zone => Game.Landscapes.Get(zone)?.Extents.X);
            // The Environment Server (terrain / voxel back end: overlays, edits, heals, veins, ownership, LOD copies,
            // stamps): in this process (it sees the zone's build sites through Claims.BoxesIn), or the `envserver`
            // container when ENVSERVER_ADDRESS is set (sent the sites and Spire platforms as boxes); the zone side talks to
            // it only through IEnvironmentServer (trace/architecture/terrain-service.md).
            Game.Terrain.EnvironmentServer.Configure(worldConfig.EnvServerAddress, worldConfig.ZoneServerId, Game.Claims.BoxesIn,
                Game.Claims.AllBoxes, Game.Spire.PlatformBoxes, TimeSpan.FromSeconds(worldConfig.EnvServerStartupWaitSeconds));
            Game.Terrain.EnvironmentServer.Instance.LoadVeins(worldConfig.GameDirectory, worldConfig.Continent);
            Game.Terrain.CavePieces.Init(worldConfig.GameDirectory);
            Game.Terrain.TerrainHeights.Load(worldConfig.Continent);
            Game.Caves.Load(worldConfig.GameDirectory, worldConfig.Continent);
            Game.WarpNetwork.Load(worldConfig.GameDirectory, worldConfig.Continent);
            Game.LootChests.Load(worldConfig.GameDirectory, worldConfig.Continent);
            // Vendor shops off (as live): stock left in booths goes back to the owners by mail.
            Game.Vendors.StartUp();
            Game.Graveyards.Load(worldConfig.GameDirectory, worldConfig.SurfaceGraveyards, worldConfig.CavernGraveyardStamps);
            Game.Achievements.Load();
            Game.ModelFootprints.Load(worldConfig.GameDirectory);
            Game.Landscapes.Load(worldConfig.GameDirectory);
            // The other landscapes' Chaos Caverns (CavernLandscapes; audit-outposts-695.md F2), after the home continent's.
            Game.Caves.LoadLandscapes(worldConfig.GameDirectory, worldConfig.CavernLandscapes);
            Game.Gathering.Load(System.IO.Path.Combine(System.AppContext.BaseDirectory, "gathered_nodes.txt"));
            Game.Terrain.BlockData.KeepVertexOnly = ConfigurationManager<WorldServerConfiguration>.Instance.Config.KeepVertexOnlyVoxels;
            Game.Terrain.TerrainLod.Enabled = ConfigurationManager<WorldServerConfiguration>.Instance.Config.TerrainLodCopies;
            Game.Terrain.EnvironmentServer.Instance.Load(System.IO.Path.Combine(System.AppContext.BaseDirectory, "TerrainEdits"));
            Game.Terrain.EnvironmentServer.Instance.BuildLodCopies();   // coarse copies of the edits, in the background
            Game.Terrain.VoxelTemplate.Folder = System.IO.Path.Combine(worldConfig.GameDirectory ?? "", "CommonData", "Voxel", "templates");
            Game.LandmarksOfLandmark.Load(System.IO.Path.Combine(System.AppContext.BaseDirectory, "TerrainEdits"));
            Game.Spire.Load(worldConfig.GameDirectory, System.IO.Path.Combine(System.AppContext.BaseDirectory, "TerrainEdits"));
            // Tunable content tables (loot / rates) before creatures drop bags; host overrides under /data/content.
            Game.ContentTables.Init();
            // World NPCs after the Spire (homes are Spire-relative offsets) and achievements / terrain heights.
            Game.Npc.WorldNpcs.Init();
            // Sites still on the old Pioneer lands 417-421 move to their replacements 464-468 (after the Spires: a new spot
            // keeps clear of the land's Spire; MoveOldPioneerSites, idempotent).
            Game.Pioneer.MoveOldSites();
            Game.Outposts.Load(System.IO.Path.Combine(System.AppContext.BaseDirectory, "TerrainEdits"));
            // The cavern graveyards stamped in earlier runs: their cells marked permanent again before the first heal pass
            // (the stamping itself runs in the background on a landscape's first visit).
            Game.Graveyards.LoadStamps(System.IO.Path.Combine(System.AppContext.BaseDirectory, "TerrainEdits"));
            // The slate slabs under new Pioneer sites (L180): which are done, and the ones a restart interrupted.
            Game.Pioneer.LoadSlabs(System.IO.Path.Combine(System.AppContext.BaseDirectory, "TerrainEdits"));
            // The home continent's underground portal crystals onto their cavern graveyards' stamped ground (L130).
            Game.WarpNetwork.SettleOnGraveyards();
            // The /membership command (TASKS 3.12), registered before anyone zones in.
            Game.Membership.Initialise();
            // The zone, world and gateway slash-command lists must not share a name (OPEN-LEADS L74); after every
            // DynamicCommands.Register (Membership's /membership is the last).
            Game.DynamicCommands.CheckUniqueNames();
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
            // Each system's time goes to the admin monitor (/admin/monitor "Update systems", Game/AdminMonitor).
            void Run(string name, Action update)
            {
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                try
                {
                    update();
                    Game.AdminMonitor.RecordSystem(name, System.Diagnostics.Stopwatch.GetTimestamp() - started);
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
                long tickStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                Run("watchdog", Game.Watchdog.Update);
                Run("network", () =>
                {
                    if (GatewayLink.Enabled)
                        GatewayLink.Update();
                    else
                        NetworkManager<ClientSession>.Instance.Update(lastTick);
                });
                Run("dev commands", () => Game.DevCommands.Update(Game.PlayerManager.Snapshot()));
                Run("crafting", Game.Crafting.Update);
                Run("achievements", Game.Achievements.Update);
                Run("props", Game.Props.Update);
                Run("build mode", Game.BuildModeHolster.Update);   // closes a cancelled bag placement's build mode once due
                Run("gathering", Game.Gathering.Update);
                Run("claims", Game.Claims.Update);
                // The sites left on an event island that closed at this start (EventIslandExpireOnClose; once).
                Run("event island expiry", Game.Landscapes.UpdateEventExpiry);
                // The old Pioneer lands' site move, when start-up couldn't reach the Environment Server (once).
                Run("old pioneer sites", Game.Pioneer.UpdateOldSites);
                Run("pioneer slabs", Game.Pioneer.UpdateSlabs);
                Run("health", Game.Health.Update);
                Run("buffs", Game.Buffs.Update);
                Run("pets", Game.Pets.Update);
                Run("party frames", Game.Groups.Update);
                Run("world editing", Game.WorldEditing.Update);
                Run("heroic movement", Game.HeroicMovement.Update);
                Run("creatures", Game.Creatures.Update);
                Run("world npcs", Game.Npc.WorldNpcs.Update);
                Run("site pathing", Game.Pathing.UpdateSites);
                Run("combat states", Game.Combat.Statuses.Update);
                Run("outposts", Game.Outposts.Update);
                Run("warp network", Game.WarpNetwork.Update);
                Run("landmarks of landmark", Game.LandmarksOfLandmark.Update);
                Run("loot chests", Game.LootChests.Update);
                Run("graveyards", Game.Graveyards.Update);
                Run("site games", Game.InstancedGames.Update);
                Run("grief", Game.Grief.Update);
                Run("story tools", Game.StoryTools.Update);
                Run("prop settings", Game.PropSettings.Flush);
                Run("deferred re-zones", Game.Teleport.Update);
                Run("movement effects", Game.MovementEffects.Update);
                Run("ability timers", Game.Abilities.Update);
                Run("travel queue", Game.TravelQueue.Update);
                Run("web pages", Game.WorldChannel.WorldChannelServices.Commerce.UpdateWebPages);
                Run("store", Game.WorldChannel.WorldChannelServices.Commerce.UpdateSignIns);
                Run("showcase following", Game.ShowcaseFollow.Update);
                Run("encounters", Game.WorldChannel.WorldChannelServices.WorldChannel.Update);
                Run("restart schedule", Game.RestartSchedule.Update);
                Run("shutdown", Game.Shutdown.Update);
                Run("kicks", Game.Kicks.Update);
                Run("social", Game.Social.SocialServices.Update);
                Run("players", Game.PlayerManager.Update);
                Run("overflow expiry", Game.OverflowExpiry.Update);
                Run("heartbeat", Game.ServerHeartbeat.Update);
                Run("terrain regeneration", Game.Terrain.ZoneTerrain.Regenerate);
                Run("ore veins", Game.Terrain.ZoneTerrain.UpdateVeins);
                Run("terrain", () => Game.Terrain.EnvironmentServer.Instance.Flush());
                Run("terrain layers (one-time move)", () => Game.Terrain.EnvironmentServer.Instance.MigrateToLayers());
                Run("terrain LOD", () => Game.Terrain.EnvironmentServer.Instance.UpdateLodCopies());
                Run("environment server link", Game.Terrain.EnvironmentServer.Update);
                Run("admin monitor", Game.AdminMonitor.Update);
                Run("admin path queries", Game.AdminCommands.Update);
                Run("membership", Game.Membership.Update);
                Game.AdminMonitor.RecordTick(System.Diagnostics.Stopwatch.GetTimestamp() - tickStarted);
            });

            // The gateway in its own process (GATEWAY_INTERNAL_ADDRESS, gateway-service.md), or in this one on the world port.
            string gateway = ConfigurationManager<WorldServerConfiguration>.Instance.Config.GatewayInternalAddress;
            if (string.IsNullOrWhiteSpace(gateway))
                gateway = Environment.GetEnvironmentVariable("GATEWAY_INTERNAL_ADDRESS");
            if (GatewayLink.Start(gateway, (uint)ConfigurationManager<WorldServerConfiguration>.Instance.Config.ZoneServerId))
            {
                // The world-channel service (channel 1) hands its packets on through the zone's own internal port.
                if (int.TryParse(Environment.GetEnvironmentVariable("ZONE_INTERNAL_PORT"), out int zonePort))
                    GatewayLink.ListenForServices(zonePort);
            }
            else
                NetworkManager<ClientSession>.Instance.Initialise(ConfigurationManager<WorldServerConfiguration>.Instance.Config.Network);

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

            if (GatewayLink.Enabled)
                GatewayLink.Stop();
            else
                NetworkManager<ClientSession>.Instance.Shutdown();
            Game.ServerHeartbeat.Stopped();
            Game.Social.SocialServices.Stop();
            Game.WorldChannel.WorldChannelServices.Stop();
            Game.Terrain.EnvironmentServer.Stop();

            ThreadManager.Instance.Shutdown();
            // The gathering state is saved at most every 10 s: write the last changes (felled trees, free spots).
            Game.Gathering.Flush();

            log.Info($"Service Ended!");
            return Task.CompletedTask;
        }
    }
}
