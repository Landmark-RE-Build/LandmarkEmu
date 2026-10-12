using LandmarkEmulator.Shared.GameTable.Text;
using LandmarkEmulator.WorldServer.Network;
using NLog;
using System;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Build site upkeep at launch ("Active Days", wiki Build Site Management; client texts Claim.UpkeepMail*): no coins.
    /// A player's sites stay in the world while they log in at least once every N days. N starts at 7 and every day the
    /// player logs in adds a day, up to 90 ("because you logged in today, your Build Site timer has been extended by an
    /// additional day to [UpkeepMax] days ... to a maximum of 90 days", Claim.UpkeepMailMaxAdjusted.Body). When a site
    /// expires its contents are packed (<see cref="SiteContents.Pack"/>) and the owner is mailed the "Your upkeep has
    /// expired!" letter with the Build Site Contents attached (Claim.UpkeepMail.*).
    /// The day count and the last login day are per character (<see cref="Unlocks"/> kinds 13 and 14).
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
            uint today = (uint)(DateTime.UtcNow - Epoch).TotalDays;
            uint last = Unlocks.Values(session.PlayerGuid, Unlocks.UpkeepLastLoginDay).FirstOrDefault();
            if (today == last)
                return;
            Unlocks.Set(session, Unlocks.UpkeepLastLoginDay, today);
            uint days = DaysOf(session.PlayerGuid);
            if (last == 0)
            {
                Unlocks.Set(session, Unlocks.UpkeepDays, days);
                return;
            }
            if (days >= MaxDays)
                return;
            days++;
            Unlocks.Set(session, Unlocks.UpkeepDays, days);
            log.Info($"{session.CharacterName}'s build site timer is now {days} days.");
            if (Claims.Owns(session.PlayerGuid))
                Mail.SendSystemMail(session.PlayerGuid, Text(TextFrom, "Build Site Management"),
                    Text(TextExtendedSubject, "Your Build Site active time has changed!"),
                    Fill(Text(TextExtendedBody, ""), session.CharacterName, days), 0);
        }

        /// <summary>Mails the owner of an expired site the client's "upkeep has expired" letter with the contents item.</summary>
        public static void MailExpired(ulong ownerGuid, string ownerName)
        {
            Mail.SendSystemMail(ownerGuid, Text(TextFrom, "Build Site Management"),
                Text(TextExpiredSubject, "Your upkeep has expired!"),
                Fill(Text(TextExpiredBody, ""), ownerName, DaysOf(ownerGuid)), 0,
                new[] { (SiteContents.ContentsItem, 1u) });
        }

        private static string Text(uint id, string fallback) => TextManager.Instance.GetTextForId(id) ?? fallback;

        /// <summary>Fills the letters' [*player*] / [*Player*] and [*UpkeepMax*] fields.</summary>
        private static string Fill(string text, string player, uint days) => text
            .Replace("[*player*]", player ?? "").Replace("[*Player*]", player ?? "")
            .Replace("[*UpkeepMax*]", days.ToString());
    }
}
