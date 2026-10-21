using LandmarkEmulator.Shared.GameTable.Text;
using LandmarkEmulator.WorldServer.Network;
using NLog;
using System;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Build site upkeep at launch ("Active Days", wiki Build Site Management; client texts Claim.UpkeepMail*): no coins.
    /// A player's sites stay in the world while they log in at least once every N days. N starts at 7 and every two unique
    /// days the player logs in add a day, up to 90: the 17 Mar 2015 "Upcoming Claim Upkeep Changes" post (forum thread
    /// 53943: one extra day for every two days logged in; six login days make three), which the
    /// launch notes (21 Mar 2016, P4133) keep (upkeep unchanged, linking that post) and the wiki
    /// repeats. On the login day that adds one the owner gets "because you logged in today, your Build Site timer has
    /// been extended by an additional day to [UpkeepMax] days" (Claim.UpkeepMailMaxAdjusted). Three days before an absent
    /// owner's sites expire they are mailed a reminder (wiki: a warning message three days before upkeep runs out;
    /// patch notes 7 Aug 2014: an upkeep reminder 3 days before a claim expires; <see cref="MailWarning"/>). When a
    /// site expires its contents are packed
    /// (<see cref="SiteContents.Pack"/>) and the owner is mailed the "Your upkeep has expired!" letter with the Build
    /// Site Contents attached (Claim.UpkeepMail.*).
    /// Per character (<see cref="Unlocks"/>): 13 the timer in days, 14 the last login day, 17 the unique login days
    /// counted, 18 the absence (last login day) already warned about. A login day runs midnight to midnight Pacific time,
    /// as live (thread 53943; setting UpkeepTimeZone, <see cref="DayZone"/>).
    /// </summary>
    public static class SiteUpkeep
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>Days a new player's sites last without a login; each login day adds one, up to <see cref="MaxDays"/>.</summary>
        public const uint StartDays = 7, MaxDays = 90;

        /// <summary>Client texts (CodeStringMappings): sender "Build Site Management", and the two letters.</summary>
        private const uint TextFrom = 562148, TextExpiredSubject = 562149, TextExpiredBody = 562150,
            TextExtendedSubject = 564967, TextExtendedBody = 564968;

        private static readonly DateTime Epoch = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// The upkeep day of a moment: days since 2000-01-01 by the calendar of <paramref name="zone"/>. Live counted the
        /// login days by Pacific time: "each day (determined as 12:00:00AM to 11:59:59 PM Pacific Time) you log in will be
        /// tracked" (developer, Upcoming Claim Upkeep Changes, 17 Mar 2015, thread 53943); setting UpkeepTimeZone.
        /// </summary>
        internal static uint DayNumber(DateTime utc, TimeZoneInfo zone)
            => (uint)(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone).Date - Epoch.Date).TotalDays;

        private static string zoneName;
        private static TimeZoneInfo zone = TimeZoneInfo.Utc;

        /// <summary>The time zone of the upkeep day (setting UpkeepTimeZone, live America/Los_Angeles; unknown or empty = UTC).</summary>
        internal static TimeZoneInfo DayZone
        {
            get
            {
                string name = Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config?.UpkeepTimeZone ?? "";
                if (name == zoneName)
                    return zone;
                TimeZoneInfo found = TimeZoneInfo.Utc;
                if (name.Trim().Length > 0)
                    try
                    {
                        found = TimeZoneInfo.FindSystemTimeZoneById(name.Trim());
                    }
                    catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
                    {
                        log.Warn($"Upkeep: time zone \"{name}\" not found; counting login days by UTC.");
                    }
                (zoneName, zone) = (name, found);
                return found;
            }
        }

        /// <summary>The days a character's sites last without a login (offline characters too).</summary>
        public static uint DaysOf(ulong characterId)
        {
            uint days = Unlocks.ValuesOfAnyone(characterId, Unlocks.UpkeepDays).FirstOrDefault();
            return days == 0 ? StartDays : Math.Min(days, MaxDays);
        }

        /// <summary>
        /// World entry: the first entry of a new day adds a day to the timer (not on the character's very first day)
        /// and, when the player owns a site, mails the "timer extended" letter as live did.
        /// </summary>
        public static void Enter(WorldSession session)
        {
            uint today = DayNumber(DateTime.UtcNow, DayZone);
            uint last = Unlocks.Values(session.PlayerGuid, Unlocks.UpkeepLastLoginDay).FirstOrDefault();
            // Not a new day (or a day already counted: a day numbered by another time zone before a settings change).
            if (today <= last)
                return;
            Unlocks.Set(session, Unlocks.UpkeepLastLoginDay, today);
            if (Unlocks.Values(session.PlayerGuid, Unlocks.UpkeepWarned).Count > 0)
                Unlocks.Set(session, Unlocks.UpkeepWarned, 0);
            uint before = DaysOf(session.PlayerGuid);
            uint count = Unlocks.Values(session.PlayerGuid, Unlocks.UpkeepLoginDays).FirstOrDefault();
            if (count == 0 && last != 0)
                count = Math.Max(1, (before - StartDays) * 2);   // counted under the old one-day-per-login rule: keep the timer
            count++;
            Unlocks.Set(session, Unlocks.UpkeepLoginDays, count);
            uint days = Math.Max(before, Math.Min(MaxDays, StartDays + count / 2));
            Unlocks.Set(session, Unlocks.UpkeepDays, days);
            if (last == 0 || days == before)
                return;
            log.Info($"{session.CharacterName}'s build site timer is now {days} days.");
            if (Claims.Owns(session.PlayerGuid))
                Social.SocialServices.Service.SendSystemMail(session.PlayerGuid, Text(TextFrom, "Build Site Management"),
                    Text(TextExtendedSubject, "Your Build Site active time has changed!"),
                    Fill(Text(TextExtendedBody, ""), session.CharacterName, days), 0);
        }

        /// <summary>Mails the owner of an expired site the client's "upkeep has expired" letter with the contents item.</summary>
        public static void MailExpired(ulong ownerGuid, string ownerName)
        {
            Social.SocialServices.Service.SendSystemMail(ownerGuid, Text(TextFrom, "Build Site Management"),
                Text(TextExpiredSubject, "Your upkeep has expired!"),
                Fill(Text(TextExpiredBody, ""), ownerName, DaysOf(ownerGuid)), 0,
                new[] { (SiteContents.ContentsItem, 1u) });
        }

        /// <summary>
        /// The letter for a site expired because its event island closed (<see cref="Landscapes.ExpireClosedIsland"/>),
        /// carrying the Build Site Contents item like <see cref="MailExpired"/>. Live's letter for this is not in the client
        /// (its only expiry text, 562150, speaks of "your extended absence") and no post quotes one, so the wording is
        /// ours [I]: the facts are the developers' (forum 58077, 21 Nov 2016: "the Halloween Party Island is no longer open
        /// on the Events world ... Any build sites that were left behind on the halloween island will be expired"), the
        /// layout and the Design / attachment sentences follow 562150.
        /// </summary>
        public static void MailIslandClosed(ulong ownerGuid, string ownerName, string islandName)
        {
            Social.SocialServices.Service.SendSystemMail(ownerGuid, Text(TextFrom, "Build Site Management"),
                $"The {islandName} has closed",
                $"Dear {ownerName},<br><br>The {islandName} is no longer open on the Events world, and the Build Sites "
                + "left behind on it have been expired. But don't worry! A Design of your Build Site has been saved, and you "
                + "will find it in your Designs list.<br><br>Right-click the attachment below to retrieve the resources and "
                + "props from your Build Site, and you will be able to re-place your build from the saved Design on any "
                + "Build Site you claim.<br><br>Thank you,<br>Build Site Management", 0,
                new[] { (SiteContents.ContentsItem, 1u) });
        }

        /// <summary>How long before expiry the reminder goes out.</summary>
        public static readonly TimeSpan WarningTime = TimeSpan.FromDays(3);

        /// <summary>
        /// The reminder three days before an absent owner's sites expire, once per absence (<paramref name="lastSeen"/>
        /// = the day they were last in the world). Live's wording isn't in the client (no Claim.UpkeepMail text for it;
        /// L87), so the letter follows the client's other upkeep letters.
        /// </summary>
        public static void MailWarning(ulong ownerGuid, string ownerName, DateTime lastSeen)
        {
            uint absence = (uint)(lastSeen.Date - Epoch).TotalDays;
            if (Unlocks.ValuesOfAnyone(ownerGuid, Unlocks.UpkeepWarned).Contains(absence))
                return;
            Unlocks.Add(ownerGuid, Unlocks.UpkeepWarned, absence);
            Social.SocialServices.Service.SendSystemMail(ownerGuid, Text(TextFrom, "Build Site Management"),
                "Your Build Site will expire soon!",
                $"Dear {ownerName},<br><br>Your Build Site will expire in less than 3 days. Simply log in to Landmark before "
                + "then to refresh your Build Site timer and keep your Build Site in the world.", 0);
            log.Info($"Mailed 0x{ownerGuid:X} ({ownerName}) the upkeep reminder.");
        }

        private static string Text(uint id, string fallback) => TextManager.Instance.GetTextForId(id) ?? fallback;

        /// <summary>Fills the letters' [*player*] / [*Player*] and [*UpkeepMax*] fields.</summary>
        private static string Fill(string text, string player, uint days) => text
            .Replace("[*player*]", player ?? "").Replace("[*Player*]", player ?? "")
            .Replace("[*UpkeepMax*]", days.ToString());
    }
}
