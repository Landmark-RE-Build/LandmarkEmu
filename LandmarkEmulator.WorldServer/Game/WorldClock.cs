using LandmarkEmulator.Shared.Configuration;
using System;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The world's day/night clock ("Island Time"). GameTimeSync 0x37 carries a u64 timestamp (whole seconds since 1970,
    /// sent in the double's bits) and an f32 cycle speed. The client (ClientGameTimeManager, 0.695 vtable 0x1447220c8,
    /// handler FUN_1431dace0) asks for it every ~11 s (FUN_143d934e0 → FUN_1431dadb0 / FUN_1431dae70) and on every reply
    /// sets its scene time of day from the timestamp's UTC hours / minutes / seconds (SetTime FUN_143d93660, gmtime
    /// FUN_1437efd30) and runs it <see cref="CycleSpeed"/> times faster than real time until the next one (FUN_143d92f90).
    /// The scene time drives the sky (FUN_1431dabd0 → FUN_142e5bf30 with GetSecondsOfDay / 3600) and the client's
    /// GameData.Time data source (FUN_1431daf10, every 500 ms: SceneTime, IslandTime, EarthTime, ServerTime), which the
    /// build-mode time panel shows (BuildModeTimeWidget TimePanel: "Claim Time" = SceneTime, "Island Time" = IslandTime).
    /// <para>
    /// One clock for everyone, continuous across restarts: it starts from real UTC time × <see cref="DefaultCycleSpeed"/>
    /// (so a restart, or another server process of the same world, lands on the same time of day), and admins move it
    /// with /servertime (<see cref="AdminCommands.GameTime"/>: hour, pause, sync, warp) or the dev command timeofday,
    /// which re-sync everyone online. Until 2026-10-18 it restarted at 09:00 on every server start (no source), so a
    /// dev world restarted after each merge never got past the afternoon in a test session and night was never seen.
    /// </para>
    /// </summary>
    public static class WorldClock
    {
        /// <summary>
        /// 8 × real time (live): one day and night take 3 real hours, so game time runs 8 times faster than real time
        /// (wiki Astronomy; sun 04:00-20:00). The client's own default
        /// is the same 8.0 (ClientGameTimeManager constructor FUN_1431da650, double at 0x144722208). Server owners can
        /// change it with the setting DayCycleSpeed (Docker DAY_CYCLE_SPEED); 0 or less means live's 8.
        /// </summary>
        public static float DefaultCycleSpeed { get; } = ConfiguredSpeed();
        /// <summary>Game seconds per real second as sent to the clients: the rate, or 0 while paused.</summary>
        public static float CycleSpeed => Paused ? 0f : rate;
        /// <summary>The running rate (admin /servertime warp); <see cref="DefaultCycleSpeed"/> unless changed.</summary>
        public static float Rate => rate;
        /// <summary>Paused by an admin (/servertime pause): the time of day stands still for everyone.</summary>
        public static bool Paused { get; private set; }
        private static float rate = DefaultCycleSpeed;
        /// <summary>Scene seconds accumulated before <see cref="segmentStart"/> (the rate or pause state changed).</summary>
        private static double elapsed;
        private static DateTime segmentStart;
        /// <summary>Seconds added to the clock (dev "timeofday", /servertime hour).</summary>
        private static double shift;
        private static readonly object sync = new();

        static WorldClock()
        {
            // Anchored to real time: scene seconds = UTC seconds since 1970 × the speed, so the clock is the same after a
            // restart and in every server process [I: live's Island Time ran on; the 09:00 start had no source].
            segmentStart = DateTime.UtcNow;
            elapsed = AnchoredSeconds(segmentStart);
        }

        /// <summary>The time of day an unchanged clock shows at <paramref name="utc"/> (0 - 86400 scene seconds).</summary>
        public static double AnchoredSeconds(DateTime utc)
            => (utc - DateTime.UnixEpoch).TotalSeconds * DefaultCycleSpeed % 86400;

        private static float ConfiguredSpeed()
        {
            float speed = ConfigurationManager<WorldServerConfiguration>.Instance.Config?.DayCycleSpeed ?? 0f;
            return float.IsFinite(speed) && speed > 0f ? speed : 8f;
        }

        /// <summary>Scene seconds since midnight (0 - 86400).</summary>
        public static double SecondsOfDay
        {
            get
            {
                double seconds;
                lock (sync)
                    seconds = elapsed + Running() + shift;
                return ((seconds % 86400) + 86400) % 86400;
            }
        }

        /// <summary>Scene seconds of the current segment (none while paused).</summary>
        private static double Running() => Paused ? 0 : (DateTime.UtcNow - segmentStart).TotalSeconds * rate;

        /// <summary>Closes the current segment so the rate or pause state can change without a jump.</summary>
        private static void Rebase()
        {
            elapsed += Running();
            segmentStart = DateTime.UtcNow;
        }

        /// <summary>Jumps the clock to an hour of the day (0-24).</summary>
        public static void SetHour(double hour)
        {
            lock (sync)
                shift += hour * 3600 - (elapsed + Running() + shift);
        }

        /// <summary>Stops or resumes the time of day.</summary>
        public static void SetPaused(bool paused)
        {
            lock (sync)
            {
                if (Paused == paused)
                    return;
                Rebase();
                Paused = paused;
            }
        }

        /// <summary>Game seconds per real second from now on (0 or less resets to the default).</summary>
        public static void SetRate(float gameSecondsPerRealSecond)
        {
            lock (sync)
            {
                Rebase();
                rate = gameSecondsPerRealSecond > 0f ? gameSecondsPerRealSecond : DefaultCycleSpeed;
            }
        }

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

        /// <summary>The clock as text for logs and dev / admin replies, e.g. "21:07:30, 8× real time".</summary>
        public static string Describe()
        {
            TimeSpan t = TimeSpan.FromSeconds(Math.Floor(SecondsOfDay));
            return $"{t:hh\\:mm\\:ss}, {(Paused ? "paused" : $"{rate:0.##}× real time")}";
        }
    }
}
