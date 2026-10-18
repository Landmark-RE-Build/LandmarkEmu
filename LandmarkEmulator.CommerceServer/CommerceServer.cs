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

namespace LandmarkEmulator.CommerceServer
{
    /// <summary>
    /// The shared commerce service (trace/architecture/world-channel-service.md §5.2, §6.1, §6.3; Phase B item 4, build
    /// step 8): one process for every world, holding what live kept account-wide or Daybreak-wide — Player Studio /
    /// Marketplace (products, peer review, the account store, the wallet and account info answers, taking a listed
    /// design), contests (definitions, entries, votes, screenshots) and portraits (Fotomat images and the upload check).
    /// Its data is in its own database, lmemu_commerce. World servers dial it when their COMMERCE_ADDRESS is set; with it
    /// empty they keep running the commerce code themselves (Game/WorldChannel/LocalCommerceService), exactly as before.
    /// Console host like the other servers; in Docker the compose service `commerce` (docker/commerce).
    /// </summary>
    internal class CommerceServer
    {
        #if DEBUG
        private const string Title = "LandmarkEmulator: Commerce Server (DEBUG)";
        #else
        private const string Title = "LandmarkEmulator: Commerce Server (RELEASE)";
        #endif

        private static readonly NLog.ILogger log = LogManager.GetCurrentClassLogger();

        static void Main(string[] args)
        {
            Directory.SetCurrentDirectory(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location));
            ConfigurationManager<CommerceServerConfiguration>.Instance.Initialise("CommerceServer.json");

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
