using LandmarkEmulator.AuthServer.Command;
using LandmarkEmulator.AuthServer.Network;
using LandmarkEmulator.AuthServer.Network.Message;
using LandmarkEmulator.AuthServer.Zone;
using LandmarkEmulator.Database.Configuration;
using LandmarkEmulator.Shared;
using LandmarkEmulator.Shared.Command;
using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.GameTable;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using Microsoft.Extensions.Hosting;
using NLog;
using System.Threading;
using System.Threading.Tasks;

namespace LandmarkEmulator.AuthServer
{
    public class HostedService : IHostedService
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public Task StartAsync(CancellationToken cancellationToken)
        {
            log.Info("Initialising...");

            DatabaseManager.Instance.Initialise(ConfigurationManager<AuthServerConfiguration>.Instance.Config.Database);
            DatabaseManager.Instance.Migrate(DatabaseType.Auth);
            DatabaseManager.Instance.Migrate(DatabaseType.Character);

            GameTableManager.Instance.Initialise();

            MessageManager.Instance.Initialise();
            AuthMessageManager.Instance.Initialise();
            TunnelDataManager.Instance.Initialise();

            AuthAssetManager.Instance.Initialise();
            // Equipped items' looks for the character list (the world server's item definitions file).
            Shared.Game.ItemAppearances.Load(System.IO.Path.Combine(System.AppContext.BaseDirectory, "Resources", "PacketDumps", "44-CommandBase"));

            ThreadManager.Instance.Initialise(lastTick =>
            {
                NetworkManager<AuthSession>.Instance.Update(lastTick);
                ZoneServerManager.Instance.Update();
            });

            ZoneServerManager.Instance.Initialise();
            NetworkManager<AuthSession>.Instance.Initialise(ConfigurationManager<AuthServerConfiguration>.Instance.Config.Network);

            CommandManager.Instance.Initialise(new AuthCommandHandler());

            log.Info($"Service Started!");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            log.Info($"Ending Service...");

            NetworkManager<AuthSession>.Instance.Shutdown();

            ThreadManager.Instance.Shutdown();

            log.Info($"Service Ended!");
            return Task.CompletedTask;
        }
    }
}
