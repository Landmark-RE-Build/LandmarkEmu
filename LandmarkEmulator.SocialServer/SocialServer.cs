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

namespace LandmarkEmulator.SocialServer
{
    /// <summary>
    /// The shared social service (trace/architecture/social-service.md; Phase B item 1): one process for every world,
    /// holding presence, friends and ignore lists so far (guilds, chat routing and mail follow, §6 step 5). World servers
    /// dial it when their SOCIAL_ADDRESS is set; with it empty they keep running the social code themselves.
    /// Console host like the auth and world servers; in Docker the compose service `social` (docker/social).
    /// </summary>
    internal class SocialServer
    {
        #if DEBUG
        private const string Title = "LandmarkEmulator: Social Server (DEBUG)";
        #else
        private const string Title = "LandmarkEmulator: Social Server (RELEASE)";
        #endif

        private static readonly NLog.ILogger log = LogManager.GetCurrentClassLogger();

        static void Main(string[] args)
        {
            Directory.SetCurrentDirectory(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location));
            ConfigurationManager<SocialServerConfiguration>.Instance.Initialise("SocialServer.json");

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
