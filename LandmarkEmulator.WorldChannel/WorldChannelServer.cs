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

namespace LandmarkEmulator.WorldChannel
{
    /// <summary>
    /// A world's world-channel service (trace/architecture/world-channel-service.md; Phase B item 4): the back end of its
    /// gateway's channel 1 (World). First stage: it passes every channel-1 packet on to the world's zone process; the
    /// world-channel areas move into it one by one: sinks (step 3), logout session (step 4), trade invitations
    /// (step 5, TradeBroker), build-site ratings (step 6, RatingBroker, with its own database lmemu_wc_&lt;world&gt;),
    /// encounters (step 7, EncounterBroker).
    /// One container per world (docker/worldchannel).
    /// </summary>
    internal class WorldChannelServer
    {
        #if DEBUG
        private const string Title = "LandmarkEmulator: World Channel (DEBUG)";
        #else
        private const string Title = "LandmarkEmulator: World Channel (RELEASE)";
        #endif

        private static readonly NLog.ILogger log = LogManager.GetCurrentClassLogger();

        static void Main(string[] args)
        {
            Directory.SetCurrentDirectory(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location));
            ConfigurationManager<WorldChannelConfiguration>.Instance.Initialise("WorldChannel.json");

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
