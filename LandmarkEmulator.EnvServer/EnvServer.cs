using LandmarkEmulator.Shared.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using System;
using System.IO;
using System.Reflection;

namespace LandmarkEmulator.EnvServer
{
    /// <summary>
    /// The Environment Server of one world (trace/architecture/terrain-service.md; Phase B item 3, build steps 3-6): the
    /// world's voxel back end in its own process — the layered voxel overlays with their versions, build edits, the Heal
    /// tool, undo / paste writes, digs and their persisted heal queue, voxel ownership (Voxel Vision), ore veins, LOD copies,
    /// template stamps (the Spire base, ruins, Landmarks of Landmark) and the ruin-layer records. The client names it in
    /// its monitor page (EnvironmentServerMonitorState FUN_14320ea00 / FUN_143163fe0, job types FUN_143c74ef0) and its
    /// construction replies (VoxelConstructionPacketEnvOwnership* 0xF4/0x16, 0x17).
    /// <para>
    /// The world's zone process dials it when ENVSERVER_ADDRESS is set (Game/Terrain/RemoteEnvironmentServer) and stays the
    /// only thing that talks to the client about terrain (spec §3.1). With ENVSERVER_ADDRESS empty the world runs the same
    /// store code itself (LocalEnvironmentServer), exactly as before. Console host like the other servers; in Docker the
    /// compose service `envserver` (docker/envserver).
    /// </para>
    /// </summary>
    internal class EnvServer
    {
        #if DEBUG
        private const string Title = "LandmarkEmulator: Environment Server (DEBUG)";
        #else
        private const string Title = "LandmarkEmulator: Environment Server (RELEASE)";
        #endif

        private static readonly NLog.ILogger log = LogManager.GetCurrentClassLogger();

        static void Main(string[] args)
        {
            Directory.SetCurrentDirectory(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location));
            ConfigurationManager<EnvServerConfiguration>.Instance.Initialise("EnvServer.json");

            IHostBuilder builder = new HostBuilder()
                .ConfigureLogging(lb =>
                {
                    lb.ClearProviders()
                        .SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace)
                        .AddNLog();
                })
                .ConfigureServices(sc =>
                {
                    sc.AddHostedService<HostedService>();
                })
                .UseWindowsService()
                .UseSystemd();

            if (!WindowsServiceHelpers.IsWindowsService() && !SystemdHelpers.IsSystemdService())
                Console.Title = Title;

            try
            {
                IHost host = builder.Build();
                host.Run();
            }
            catch (Exception e)
            {
                log.Fatal(e);
            }
            NLog.LogManager.Shutdown();
        }
    }
}
