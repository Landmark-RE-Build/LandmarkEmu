using NLog;
using System;
using System.Threading;

namespace LandmarkEmulator.Shared.Command
{
    public class CommandManager : Singleton<CommandManager>
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private Thread commandThread;
        private readonly ManualResetEventSlim waitHandle = new();

        private volatile CancellationTokenSource cancellationToken;

        private ICommandHandler commandHandler;

        public void Initialise(ICommandHandler handler)
        {
            commandHandler = handler;

            // Docker/non-interactive hosts have redirected/closed stdin. ReadLine() returns
            // null immediately there, which would otherwise spin on ">> " at ~100% CPU.
            if (Console.IsInputRedirected)
            {
                log.Info("Console input is redirected; interactive command thread disabled.");
                return;
            }

            InitialiseCommandThread();
        }

        private void InitialiseCommandThread()
        {
            cancellationToken = new CancellationTokenSource();

            commandThread = new Thread(CommandThread);
            commandThread.Start();

            // wait for command thread to start before continuing
            waitHandle.Wait();
        }

        private void CommandThread()
        {
            log.Info("Started command thread.");
            waitHandle.Set();

            while (!cancellationToken.IsCancellationRequested)
            {
                Console.Write(">> ");
                string line = Console.ReadLine();
                if (line == null)
                {
                    log.Info("Console input closed; stopping command thread.");
                    break;
                }

                commandHandler?.Invoke(line);
            }

            log.Info("Stopped command thread.");
        }
    }
}
