using System.Diagnostics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The server's network clock, shared by every client through Synchronization.
    /// </summary>
    public static class ServerClock
    {
        private static readonly Stopwatch clock = Stopwatch.StartNew();

        /// <summary>
        /// Milliseconds since the server started. Clients stamp movement with the low 32 bits, so this wraps after
        /// ~49 days of uptime.
        /// </summary>
        public static ulong Milliseconds => (ulong)clock.ElapsedMilliseconds;
    }
}
