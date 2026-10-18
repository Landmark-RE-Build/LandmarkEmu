using System;

namespace LandmarkEmulator.Shared.Game
{
    /// <summary>
    /// A token bucket: up to <see cref="Burst"/> actions at once, refilled at <see cref="PerSecond"/> a second. Used to
    /// cap how fast one player can make the server work (chat lines, packets) without affecting normal play.
    /// Thread-safe.
    /// </summary>
    public sealed class RateLimiter
    {
        public double Burst { get; }
        public double PerSecond { get; }

        private readonly object sync = new();
        private double tokens;
        private long lastTicks;

        public RateLimiter(double burst, double perSecond)
        {
            Burst = burst;
            PerSecond = perSecond;
            tokens = burst;
            lastTicks = Environment.TickCount64;
        }

        /// <summary>Takes one token; false (nothing taken) when the bucket is empty.</summary>
        public bool TryTake()
        {
            lock (sync)
            {
                long now = Environment.TickCount64;
                tokens = Math.Min(Burst, tokens + (now - lastTicks) / 1000.0 * PerSecond);
                lastTicks = now;
                if (tokens < 1)
                    return false;
                tokens -= 1;
                return true;
            }
        }
    }
}
