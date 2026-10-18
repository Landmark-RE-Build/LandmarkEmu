namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// The zone's connection to its Environment Server (trace/architecture/terrain-service.md): one per world. By default
    /// the in-process <see cref="LocalEnvironmentServer"/>; with ENVSERVER_ADDRESS set (WorldServer.json EnvServerAddress)
    /// the <see cref="RemoteEnvironmentServer"/> to the `envserver` container (spec §7 steps 3-6). Callers see the same
    /// interface either way.
    /// </summary>
    public static class EnvironmentServer
    {
        private static readonly NLog.ILogger log = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>The world's Environment Server (set once at start-up by <see cref="Connect"/>).</summary>
        public static IEnvironmentServer Instance { get; private set; }

        /// <summary>True when the terrain is the remote service's (this world's TerrainEdits folder holds only markers).</summary>
        public static bool IsRemote => Instance is RemoteEnvironmentServer;

        /// <summary>Uses <paramref name="server"/> from now on and routes its pushes to the zone (<see cref="ZoneTerrain"/>).</summary>
        public static void Connect(IEnvironmentServer server)
        {
            server.LodCopiesChanged += ZoneTerrain.LodCopiesChanged;
            Instance = server;
        }

        /// <summary>
        /// Picks the implementation, once at start before anything reads terrain: WorldServer.json EnvServerAddress, else
        /// the environment variable ENVSERVER_ADDRESS ("host:port", e.g. envserver:20500); empty = in-process, exactly as
        /// before (<paramref name="sitesIn"/> = Claims.BoxesIn). The remote one is sent the build sites
        /// (<paramref name="allSites"/>) and Spire platforms (<paramref name="noVeinBoxes"/>) as boxes, and start-up waits
        /// up to <paramref name="wait"/> for its version index (the Spire / outpost stamps need it).
        /// </summary>
        public static void Configure(string address, ulong worldId,
            System.Func<uint, (double, double, double), (double, double, double),
                System.Collections.Generic.List<(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)>> sitesIn,
            System.Func<System.Collections.Generic.IEnumerable<(uint Zone, double[] Box)>> allSites,
            System.Func<System.Collections.Generic.IEnumerable<(uint Zone, double[] Box)>> noVeinBoxes,
            System.TimeSpan wait)
        {
            address = !string.IsNullOrWhiteSpace(address) ? address.Trim() : System.Environment.GetEnvironmentVariable("ENVSERVER_ADDRESS")?.Trim();
            if (string.IsNullOrEmpty(address))
            {
                log.Info("Environment Server: in-process (ENVSERVER_ADDRESS not set).");
                Connect(new LocalEnvironmentServer(sitesIn));
                return;
            }
            var remote = new RemoteEnvironmentServer(address, (uint)worldId, allSites, noVeinBoxes);
            Connect(remote);
            remote.Start(wait);
        }

        /// <summary>World tick: a remote service's pushes that reach players and its reconnect work (nothing in-process).</summary>
        public static void Update() => (Instance as RemoteEnvironmentServer)?.Update();

        /// <summary>Closes the link to a remote service (at shutdown, after the players have left).</summary>
        public static void Stop() => (Instance as System.IDisposable)?.Dispose();
    }
}
