using NLog;
using System;
using System.IO;
using System.Threading;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Keeps a hung world from staying up (trace/architecture/operations.md, health checks). Every server tick stamps the
    /// time and, every few seconds, touches <see cref="WorldServerConfiguration.HealthFile"/>, which the container's
    /// healthcheck reads (Docker shows the world as unhealthy when it goes stale). Docker Compose doesn't restart an
    /// unhealthy container, so a background thread ends the process when no tick has run for
    /// <see cref="WorldServerConfiguration.WatchdogSeconds"/>; the restart policy (unless-stopped) starts the world again.
    /// Live's zones crashed and came back on their own too ("zone crash" notes, P0042 / P4612).
    /// </summary>
    public static class Watchdog
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        private static long lastTickTicks;
        private static DateTime lastFileWrite;
        private static string file;
        private static int limitSeconds;
        private static Thread thread;

        public static void Start(string healthFile, int watchdogSeconds)
        {
            file = string.IsNullOrWhiteSpace(healthFile) ? null : healthFile;
            limitSeconds = Math.Max(0, watchdogSeconds);
            if (limitSeconds == 0 || thread != null)
                return;
            thread = new Thread(Watch) { IsBackground = true, Name = "Watchdog" };
            thread.Start();
            log.Info($"Watchdog on: the world restarts after {limitSeconds} s without a tick.");
        }

        /// <summary>Server tick.</summary>
        public static void Update()
        {
            DateTime now = DateTime.UtcNow;
            Interlocked.Exchange(ref lastTickTicks, now.Ticks);
            if (file == null || now - lastFileWrite < TimeSpan.FromSeconds(5))
                return;
            lastFileWrite = now;
            try
            {
                File.WriteAllText(file, now.ToString("O"));
            }
            catch (IOException e)
            {
                log.Warn($"Health file {file} not written: {e.Message}");
            }
        }

        private static void Watch()
        {
            while (true)
            {
                Thread.Sleep(5000);
                long last = Interlocked.Read(ref lastTickTicks);
                // Not armed until the first tick (start-up loads can take minutes), nor while the server is stopping, nor
                // while a debugger is attached (a breakpoint stops the tick; WatchdogSeconds defaults on since 2026-10-18).
                if (last == 0 || Shutdown.InProgress || System.Diagnostics.Debugger.IsAttached)
                    continue;
                double stalled = (DateTime.UtcNow - new DateTime(last, DateTimeKind.Utc)).TotalSeconds;
                if (stalled < limitSeconds)
                    continue;
                log.Fatal($"Watchdog: no server tick for {stalled:F0} s; ending the process so the world restarts.");
                LogManager.Flush(TimeSpan.FromSeconds(5));
                Environment.Exit(3);
            }
        }
    }
}
