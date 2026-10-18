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

namespace LandmarkEmulator.WorldServer
{
    internal class WorldServer
    {
        #if DEBUG
        private const string Title = "LandmarkEmulator: Zone Server (DEBUG)";
        #else
        private const string Title = "LandmarkEmulator: Zone Server (RELEASE)";
        #endif

        private static readonly NLog.ILogger log = LogManager.GetCurrentClassLogger();

        public const string EncryptionKey = "F70IaxuU8C/w7FPXY1ibXw==";

        public static double StartTime = 0;

        static void Main(string[] args)
        {
            // One-shot tool mode (Docker's "icons" service, docs/showcase.md "Icons"): export the item icons, then exit.
            if (args.Length > 0 && args[0] == Game.IconExport.Argument)
                Environment.Exit(Game.IconExport.Run(args[1..]));

            StartTime = (DateTime.Now.Ticks - DateTime.UnixEpoch.Ticks) / 1000;
            Directory.SetCurrentDirectory(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location));
            // WorldServer.json, the environment (.env through docker compose), the command line, and last the values saved on
            // the admin Settings page (Game/ServerSettings: the overrides file in the world's data folder).
            ConfigurationManager<WorldServerConfiguration>.Instance.Initialise("WorldServer.json", Game.ServerSettings.StartupOverrides);
            Game.ServerSettings.Start();

            IHostBuilder builder = new HostBuilder()
                .ConfigureLogging(lb =>
                {
                    // only applicable to logging done through host
                    // other logging is still done directly though NLog
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
            // Hosting has stopped and HostedService.StopAsync has saved and closed everything; worker threads that are
            // still running (not background threads) would otherwise keep the process and its window open after
            // "Service Ended!", holding the build output locked (seen with the "shutdown" dev command, 2026-10-14).
            NLog.LogManager.Shutdown();
            Environment.Exit(0);
        }

        public static double GetServerTime()
        {
            double delta = GetCurrentServerTime() - StartTime;
            return GetCurrentServerTime() + delta;
        }

        public static double GetCurrentServerTime()
        {
            return (DateTime.Now.Ticks - DateTime.UnixEpoch.Ticks) / 1000;
        }
    }
}
