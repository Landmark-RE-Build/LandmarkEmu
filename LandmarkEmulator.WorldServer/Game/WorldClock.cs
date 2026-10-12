using System;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The world's day/night clock. GameTimeSync carries a u64 timestamp (seconds, in the double's bits) and a cycle
    /// speed; the client (ClientGameTimeManager, FUN_143cd37b0) takes the scene time of day from the timestamp's
    /// hours/minutes/seconds and runs it <see cref="CycleSpeed"/> times faster than real time until the next sync.
    /// The server keeps one clock for everyone: a day lasts 86400 / CycleSpeed real seconds (~2.6 h) and starts at
    /// <see cref="StartHour"/> when the server starts.
    /// </summary>
    public static class WorldClock
    {
        public const float CycleSpeed = 9.1f;
        public static double StartHour = 9;
        private static readonly DateTime started = DateTime.UtcNow;
        /// <summary>Seconds added to the clock (dev "timeofday").</summary>
        private static double shift;

        /// <summary>Scene seconds since midnight (0 - 86400).</summary>
        public static double SecondsOfDay
        {
            get
            {
                double seconds = StartHour * 3600 + (DateTime.UtcNow - started).TotalSeconds * CycleSpeed + shift;
                return ((seconds % 86400) + 86400) % 86400;
            }
        }

        /// <summary>Jumps the clock to an hour of the day (0-24).</summary>
        public static void SetHour(double hour) => shift += hour * 3600 - SecondsOfDay;

        /// <summary>
        /// The GameTimeSync timestamp to send: the client's own timestamp moved to today's scene time, so dates stay
        /// sensible and only the time of day comes from the world clock.
        /// </summary>
        public static double SyncTime(double clientTime)
        {
            long client = BitConverter.DoubleToInt64Bits(clientTime);
            long day = client - client % 86400;
            return BitConverter.Int64BitsToDouble(day + (long)SecondsOfDay);
        }
    }
}
