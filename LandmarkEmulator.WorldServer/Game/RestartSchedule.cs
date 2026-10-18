using NLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Scheduled restarts, as live ran them: from 25 Jun 2016 a short restart on Monday and Saturday mornings at 3 a.m.
    /// Pacific (dev post, 24 Jun 2016; earlier a nightly 5 a.m. bounce). At the warning time before a restart
    /// this starts the usual <see cref="Shutdown"/> countdown (warnings, players sent to character select, clean stop);
    /// the container's restart policy (unless-stopped) brings the world back. Off unless WorldServer.json / .env sets
    /// RestartSchedule, e.g. "Mon,Sat 03:00" with RestartTimeZone "America/Los_Angeles" (trace/architecture/operations.md).
    /// </summary>
    public static class RestartSchedule
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private static List<(DayOfWeek Day, TimeSpan Time)> slots;
        private static TimeZoneInfo zone;
        private static DateTime? next;          // UTC
        private static int warningSeconds;
        private static string message;

        /// <summary>Reads the schedule; an empty or unreadable one switches restarts off.</summary>
        public static void Configure(string schedule, string timeZone, int warning, string text)
        {
            slots = Parse(schedule);
            warningSeconds = Math.Max(0, warning);
            message = string.IsNullOrWhiteSpace(text) ? "Scheduled server restart." : text;
            zone = TimeZoneInfo.Utc;
            if (!string.IsNullOrWhiteSpace(timeZone))
            {
                try
                {
                    zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
                }
                catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
                {
                    log.Warn($"Restart schedule: time zone \"{timeZone}\" not found; using UTC.");
                }
            }
            next = slots.Count > 0 ? NextAfter(DateTime.UtcNow) : null;
            if (next != null)
                log.Info($"Scheduled restarts: {schedule} ({zone.Id}); next at {next:yyyy-MM-dd HH:mm} UTC.");
            else if (!string.IsNullOrWhiteSpace(schedule))
                log.Warn($"Restart schedule \"{schedule}\" not understood (expected e.g. \"Mon,Sat 03:00\"); restarts are off.");
        }

        /// <summary>Server tick: starts the countdown when the warning time before the next restart has come.</summary>
        public static void Update()
        {
            if (next is not DateTime at || Shutdown.InProgress)
                return;
            int left = (int)Math.Ceiling((at - DateTime.UtcNow).TotalSeconds);
            if (left > warningSeconds)
                return;
            next = NextAfter(at.AddMinutes(1));
            log.Warn($"Scheduled restart in {Math.Max(0, left)} s.");
            Shutdown.Schedule(Math.Max(0, left), message);
        }

        /// <summary>"Mon,Sat 03:00" → (Monday, 3:00), (Saturday, 3:00). Days: three-letter English names or "Daily".</summary>
        internal static List<(DayOfWeek, TimeSpan)> Parse(string schedule)
        {
            var result = new List<(DayOfWeek, TimeSpan)>();
            string[] parts = (schedule ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !TimeSpan.TryParseExact(parts[1], @"hh\:mm", CultureInfo.InvariantCulture, out TimeSpan time))
                return result;
            IEnumerable<string> days = parts[0].Equals("Daily", StringComparison.OrdinalIgnoreCase)
                ? Enum.GetNames<DayOfWeek>().Select(d => d[..3])
                : parts[0].Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (string day in days)
            {
                DayOfWeek? match = Enum.GetValues<DayOfWeek>()
                    .Cast<DayOfWeek?>()
                    .FirstOrDefault(d => d.ToString()!.StartsWith(day.Trim(), StringComparison.OrdinalIgnoreCase) && day.Trim().Length >= 3);
                if (match is not DayOfWeek d)
                    return new List<(DayOfWeek, TimeSpan)>();
                result.Add((d, time));
            }
            return result;
        }

        /// <summary>The first scheduled time after <paramref name="afterUtc"/>, in UTC (local times in the schedule's zone).</summary>
        private static DateTime? NextAfter(DateTime afterUtc)
        {
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(afterUtc, zone);
            for (int days = 0; days <= 7; days++)
            {
                DateTime date = local.Date.AddDays(days);
                foreach ((DayOfWeek day, TimeSpan time) in slots.OrderBy(s => s.Time))
                {
                    if (date.DayOfWeek != day)
                        continue;
                    DateTime candidate = DateTime.SpecifyKind(date + time, DateTimeKind.Unspecified);
                    if (zone.IsInvalidTime(candidate))
                        candidate = candidate.AddHours(1);      // a clock change skipped this time
                    DateTime utc = TimeZoneInfo.ConvertTimeToUtc(candidate, zone);
                    if (utc > afterUtc)
                        return utc;
                }
            }
            return null;
        }
    }
}
