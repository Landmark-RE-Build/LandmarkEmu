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

namespace LandmarkEmulator.GatewayServer
{
    /// <summary>
    /// A world's gateway in its own process (trace/architecture/gateway-service.md, Phase B gateway step 3): the
    /// client-facing UDP port (SOE, RC4, gateway login, 0.695 header translation, flood limits) for one world; the
    /// world's zone process dials its internal port and serves the clients it attaches. One container per world
    /// (docker/gateway); with no gateway container the world runs the gateway itself, as before.
    /// </summary>
    internal class GatewayServer
    {
        #if DEBUG
        private const string Title = "LandmarkEmulator: Gateway Server (DEBUG)";
        #else
        private const string Title = "LandmarkEmulator: Gateway Server (RELEASE)";
        #endif

        private static readonly NLog.ILogger log = LogManager.GetCurrentClassLogger();

        static void Main(string[] args)
        {
            Directory.SetCurrentDirectory(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location));
            ConfigurationManager<GatewayServerConfiguration>.Instance.Initialise("GatewayServer.json");

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
