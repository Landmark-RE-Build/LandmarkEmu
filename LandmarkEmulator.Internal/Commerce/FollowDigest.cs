using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LandmarkEmulator.Internal.Commerce
{
    /// <summary>
    /// The Showcase following rules and the daily digest mail's text, shared by the world (in-process commerce,
    /// Game/ShowcaseFollow.cs) and the commerce service (LandmarkEmulator.CommerceServer/FollowService.cs), so both keep
    /// the same lists and write the same mail (docs/showcase.md "Following and the daily mail", OPEN-LEADS L137).
    /// <para>
    /// Live (Update Notes 27 Aug 2014, design/forum/patches/2014-08-27-46891): players followed creators and tags, alone or
    /// grouped ("PVP Castles" = only items with both words), and once a day an in-game mail listed what the followed
    /// creators and tags had newly on the Marketplace or Gallery [W]. The limits and the mail's wording are ours [I]:
    /// live composed the mail on its servers and
    /// no copy survives in design/; the client has no text for it (its Player Studio mail kinds 0x10 / 0x20 / 0x40 /
    /// 0x80, FUN_143639de0, are review results only).
    /// </para>
    /// </summary>
    public static class FollowDigest
    {
        /// <summary>Most creators one player follows [I: live's limit is not known].</summary>
        public const int MaxCreators = 100;
        /// <summary>Most tag groups one player follows [I].</summary>
        public const int MaxTagGroups = 20;
        /// <summary>Most words in one tag group, and the longest word [I].</summary>
        public const int MaxWordsPerGroup = 5, MaxWordLength = 32;
        /// <summary>Most items one mail lists; the rest are counted [I] (the mail body holds 4096 characters).</summary>
        public const int MaxItemsPerMail = 40;
        /// <summary>The first run's look-back, and the window a dev run without a previous run uses.</summary>
        public static readonly TimeSpan FirstWindow = TimeSpan.FromHours(24);
        /// <summary>
        /// The client's Player Studio mail sender, PlayerStudio.MailUser (FUN_143639de0) [P for the text]; the world reads
        /// it from its text table and falls back to <see cref="SenderFallback"/>.
        /// </summary>
        public const uint MailUserText = 563379;
        public const string SenderFallback = "Player Studio";

        /// <summary>Product item types (as player_studio_product.itemType / the service's product.item_type).</summary>
        public const uint ItemDesign = 2, ItemClaim = 3;

        private static readonly char[] Separators = { ' ', '\t', ',', ';', '+', '\r', '\n' };

        /// <summary>The words of a tag string: lower case, '#' dropped, each once.</summary>
        public static string[] Words(string tags)
            => (tags ?? "").Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(w => w.TrimStart('#').ToLowerInvariant()).Where(w => w.Length > 0).Distinct().ToArray();

        /// <summary>A tag group as stored: its words (each cut to <see cref="MaxWordLength"/>), sorted, one space apart.</summary>
        public static string NormaliseTags(string text)
            => string.Join(' ', Words(text).Select(w => Clip(w, MaxWordLength)).Distinct().OrderBy(w => w, StringComparer.Ordinal));

        /// <summary>
        /// Checks a tag group typed by a player: the normalised group, or null with the refusal (empty, too many words).
        /// </summary>
        public static string CheckTags(string text, out string refusal)
        {
            string tags = NormaliseTags(text);
            refusal = null;
            if (tags.Length == 0)
                refusal = "Type one or more tag words.";
            else if (tags.Split(' ').Length > MaxWordsPerGroup)
                refusal = $"A tag group has at most {MaxWordsPerGroup} words.";
            return refusal == null ? tags : null;
        }

        /// <summary>The page's line for a tag group just followed.</summary>
        public static string FollowedTagsText(string tags)
            => tags.Contains(' ') ? $"You now follow items tagged with all of \"{tags}\"." : $"You now follow the tag \"{tags}\".";

        /// <summary>
        /// An item listed in the digest's window. <paramref name="CreatorMatch"/>: whether a creator follow can match its
        /// owner — creator ids are character guids, which only mean the same character within one realm (the commerce
        /// service lists every realm's designs; in-process every item is the world's own).
        /// </summary>
        public sealed record Item(ulong Product, ulong Owner, string OwnerName, string Name, uint ItemType, IReadOnlySet<string> Words,
            DateTime ListedAt, bool CreatorMatch = true);

        /// <summary>One follow row: a creator (guid, <paramref name="Tags"/> empty) or a tag group (<paramref name="Creator"/> 0).</summary>
        public sealed record Follow(ulong Creator, string Tags);

        /// <summary>A follower's mail, without the greeting (<see cref="Greeting"/>; the world that delivers it knows the name).</summary>
        public sealed record Mail(string Subject, string Body, int Items);

        /// <summary>The greeting put before <see cref="Mail.Body"/>.</summary>
        public static string Greeting(string followerName) => $"Dear {followerName},\n\n";

        /// <summary>
        /// One follower's mail, or null when nothing new matches what they follow: the new items from creators they follow,
        /// then per tag group the items carrying every word of it; their own items left out, each item once, at most
        /// <see cref="MaxItemsPerMail"/> listed ("...and n more").
        /// </summary>
        public static Mail Compose(ulong follower, IEnumerable<Follow> follows, IReadOnlyList<Item> items, DateTime since)
        {
            var mine = follows.ToList();
            var seen = new HashSet<ulong>();
            var sections = new List<(string Heading, List<Item> Items)>();
            var creators = mine.Where(f => f.Creator != 0).Select(f => f.Creator).ToHashSet();
            List<Item> fromCreators = items.Where(i => i.CreatorMatch && i.Owner != follower && creators.Contains(i.Owner) && seen.Add(i.Product)).ToList();
            if (fromCreators.Count > 0)
                sections.Add(("From creators you follow:", fromCreators));
            foreach (string group in mine.Where(f => f.Creator == 0).Select(f => f.Tags ?? "").Where(t => t.Length > 0).Distinct())
            {
                string[] words = group.Split(' ');
                List<Item> tagged = items.Where(i => !(i.CreatorMatch && i.Owner == follower) && words.All(i.Words.Contains) && seen.Add(i.Product)).ToList();
                if (tagged.Count > 0)
                    sections.Add((words.Length > 1 ? $"Tagged with all of \"{group}\":" : $"Tagged \"{group}\":", tagged));
            }
            int total = sections.Sum(s => s.Items.Count);
            if (total == 0)
                return null;
            var body = new StringBuilder();
            body.Append("New on the Showcase since ").Append(since.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture))
                .Append(" UTC from the creators and tags you follow:\n");
            int listed = 0;
            foreach ((string heading, List<Item> list) in sections)
            {
                if (listed >= MaxItemsPerMail)
                    break;
                body.Append('\n').Append(heading).Append('\n');
                foreach (Item item in list)
                {
                    if (listed >= MaxItemsPerMail)
                        break;
                    string what = item.ItemType == ItemClaim ? "Gallery build site" : "Player Studio design";
                    string name = item.Name.Length == 0 ? "(unnamed)" : Clip(item.Name, 60);
                    body.Append("- \"").Append(name).Append("\" by ").Append(string.IsNullOrEmpty(item.OwnerName) ? "?" : item.OwnerName)
                        .Append(" (").Append(what).Append(")\n");
                    listed++;
                }
            }
            if (total > listed)
                body.Append("\n...and ").Append(total - listed).Append(" more.\n");
            body.Append("\nOpen the Showcase to see them. You can change who and what you follow on its Following tab.");
            string subject = total == 1 ? "1 new item from those you follow" : $"{total} new items from those you follow";
            return new Mail(subject, body.ToString(), total);
        }

        /// <summary>The scheduled run's slot for a UTC day ("day:2026-10-18"): one run per day (and realm).</summary>
        public static string DaySlot(DateTime utcNow) => "day:" + utcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>A dev run's slot (never the same twice).</summary>
        public static string DevSlot(DateTime utcNow) => "dev:" + utcNow.Ticks.ToString(CultureInfo.InvariantCulture);

        /// <summary>The digest hour from a setting (0-23; above 23 = 23).</summary>
        public static int Hour(uint setting) => (int)Math.Min(23u, setting);

        /// <summary>The look-back of a dev run's "hours" argument, capped at a year.</summary>
        public static TimeSpan LookBack(double hours) => TimeSpan.FromHours(Math.Min(hours, 24 * 366));

        public static string Clip(string s, int max) => (s ?? "").Length <= max ? s ?? "" : s[..max];
    }
}
