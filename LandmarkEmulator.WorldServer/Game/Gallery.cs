using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.GameTable.Text;
using LandmarkEmulator.WorldServer.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The gallery's rules as the 0.695 client and the launch-era patch notes define them (trace/0695/gallery-695.md):
    /// <list type="bullet">
    /// <item><b>Tags.</b> Build Site Management's three drop-downs ("What Genre Is It?", "What Did You Build?", "What Can
    /// Others Do Here?"; EditPanel.as getDropDownData) are the rows of the client table TagDefinitions.txt with
    /// CATEGORY 1, 2 and 3 (loaded as "MrktBundleTag", FUN_143470750; category 0 rows are the Marketplace bundle tags).
    /// The chosen names go to the server as one space-separated string "Genre Built Activity" (Claims.SetClaimTags →
    /// SetTagString), which the window parses back by position (split on " ", word 0 / 1 / 2 looked up in the lists).
    /// A site may be published or entered into a competition only when all three are set (P4590 / P4591; the window
    /// disables the buttons otherwise), so the gallery lists tagged sites only.</item>
    /// <item><b>"PlayMode" word.</b> A listed site's product tags are rewritten by the client as "&lt;site tags&gt; PlayMode"
    /// while the site is in Play Mode (FUN_142d99b70, on SetClaimTags, on a mode change and on Publish for an existing
    /// listing): that word is how live's gallery filtered by Play Mode status (P5624, 25 May 2016). This server reads
    /// the mode live (<see cref="Claims.IsPlaying"/>) and drops the word from stored tags.</item>
    /// <item><b>Contest markers.</b> "contest_..." words in a listing's tags mark competition entries (Claims.EnterContest
    /// sends the contest tag in ProductUpdate; ListClaimOnGallery / SetClaimTags refuse site tags that contain one:
    /// Claims.Error.TagsAreInvalid). Kept apart from the site's own tags here.</item>
    /// <item><b>Comments</b> on gallery entries (P4139 / P4686): the gallery_comment table, written and read by the
    /// Showcase page (WebPages /api/gallery/comment*).</item>
    /// </list>
    /// </summary>
    public static class Gallery
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>The tag word the client appends to a listing while its site is in Play Mode (FUN_142d99b70).</summary>
        public const string PlayModeWord = "PlayMode";
        /// <summary>The three drop-downs: TagDefinitions.txt CATEGORY values.</summary>
        public const int CategoryGenre = 1, CategoryBuilt = 2, CategoryActivity = 3;

        private static readonly object sync = new();
        /// <summary>Tag names per drop-down (index 0 = genre, 1 = what was built, 2 = what others can do), table order.</summary>
        private static List<string>[] categories;
        /// <summary>Lower-case tag name → drop-down index.</summary>
        private static Dictionary<string, int> categoryOf;

        /// <summary>The drop-down lists from TagDefinitions.txt (ID, NAME_ID, CATEGORY), built once.</summary>
        public static List<string>[] Categories()
        {
            lock (sync)
            {
                if (categories != null)
                    return categories;
                var lists = new[] { new List<string>(), new List<string>(), new List<string>() };
                var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (string[] row in GameDataFiles.Rows("TagDefinitions.txt"))
                {
                    uint category = row.U(2);
                    if (category < CategoryGenre || category > CategoryActivity)
                        continue;
                    string name = (TextManager.Instance.GetTextForId(row.U(1)) ?? "").Trim();
                    if (name.Length == 0 || index.ContainsKey(name))
                        continue;
                    lists[category - 1].Add(name);
                    index[name] = (int)category - 1;
                }
                log.Info($"Gallery tags: {lists[0].Count} genres, {lists[1].Count} build types, {lists[2].Count} activities (TagDefinitions.txt).");
                categoryOf = index;
                categories = lists;
                return categories;
            }
        }

        private static string[] Words(string tags)
            => (tags ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        private static bool IsContestTag(string word) => word.StartsWith(Contests.TagPrefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// A site's three gallery tags from its tag string: the first word of each drop-down list found, in list spelling
        /// ("" where none). Words the lists don't hold (free-form tags of older builds, "PlayMode", contest markers) are
        /// ignored.
        /// </summary>
        public static string[] Parse(string tags)
        {
            List<string>[] lists = Categories();
            var result = new[] { "", "", "" };
            foreach (string word in Words(tags))
                if (categoryOf.TryGetValue(word, out int category) && result[category].Length == 0)
                    result[category] = lists[category].First(n => string.Equals(n, word, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        /// <summary>All three gallery tags set (what Publish to Gallery and Enter Competition require).</summary>
        public static bool IsTagged(string tags) => Parse(tags).All(t => t.Length > 0);

        /// <summary>A tag string without the client's "PlayMode" word and contest markers.</summary>
        public static string CleanTags(string tags)
            => string.Join(' ', Words(tags).Where(w => !string.Equals(w, PlayModeWord, StringComparison.OrdinalIgnoreCase) && !IsContestTag(w)));

        /// <summary>The contest markers ("contest_...") of a listing's tags, space-separated ("" for none).</summary>
        public static string ContestTagsOf(string tags) => string.Join(' ', Words(tags).Where(IsContestTag));

        /// <summary>
        /// <paramref name="tags"/> (markers dropped) with <paramref name="contestTags"/> appended: null = no markers,
        /// a marker already present is not doubled.
        /// </summary>
        public static string WithContestTags(string tags, string contestTags)
        {
            var words = Words(tags).Where(w => !IsContestTag(w)).ToList();
            foreach (string marker in Words(contestTags))
                if (IsContestTag(marker) && !words.Contains(marker, StringComparer.OrdinalIgnoreCase))
                    words.Add(marker);
            return string.Join(' ', words);
        }

        // ---- comments ---------------------------------------------------------------------------------------------

        public record Comment(ulong Id, ulong Site, string Author, ulong AuthorGuid, string Text, long At);

        /// <summary>Longest comment kept (the column is varchar(512)).</summary>
        public const int MaxCommentLength = 500;
        private static readonly TimeSpan CommentInterval = TimeSpan.FromSeconds(10);
        private static readonly Dictionary<ulong, DateTime> lastComment = new();

        private static long Unix(DateTime t) => new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Utc)).ToUnixTimeSeconds();

        /// <summary>A site's comments, oldest first.</summary>
        public static List<Comment> Comments(ulong site)
        {
            using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
            return context.GalleryComment.Where(c => c.ClaimId == site).OrderBy(c => c.Id)
                .Select(c => new { c.Id, c.ClaimId, c.AuthorName, c.AuthorGuid, c.Text, c.CreatedAt }).ToList()
                .Select(c => new Comment(c.Id, c.ClaimId, c.AuthorName, c.AuthorGuid, c.Text, Unix(c.CreatedAt))).ToList();
        }

        /// <summary>How many comments each listed site has (sites without any are left out).</summary>
        public static Dictionary<ulong, int> CommentCounts()
        {
            using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
            return context.GalleryComment.GroupBy(c => c.ClaimId).Select(g => new { g.Key, Count = g.Count() })
                .ToDictionary(g => g.Key, g => g.Count);
        }

        /// <summary>
        /// A comment on a listed site's gallery entry (the page's comments area). One per player per 10 s; plain text,
        /// up to <see cref="MaxCommentLength"/> characters.
        /// </summary>
        public static (bool Ok, string Message) AddComment(WorldSession session, ulong site, string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0)
                return (false, "Write something first.");
            if (text.Length > MaxCommentLength)
                text = text[..MaxCommentLength];
            if (!WorldChannel.WorldChannelServices.Commerce.IsOnGallery(site) || Claims.Get(site) == null)
                return (false, "That build site isn't listed in the gallery.");
            lock (sync)
            {
                if (lastComment.TryGetValue(session.PlayerGuid, out DateTime last) && DateTime.UtcNow - last < CommentInterval)
                    return (false, "One moment between comments.");
                lastComment[session.PlayerGuid] = DateTime.UtcNow;
            }
            using (var context = DatabaseManager.Instance.CharacterDatabase.NewContext())
            {
                context.GalleryComment.Add(new GalleryCommentModel
                {
                    ClaimId = site, AuthorGuid = session.PlayerGuid, AuthorName = session.CharacterName ?? "",
                    Text = text, CreatedAt = DateTime.UtcNow
                });
                context.SaveChanges();
            }
            log.Info($"{session.CharacterName} commented on build site {site}: \"{text}\".");
            return (true, "Comment posted.");
        }

        /// <summary>Removes a comment: its author, the site's owner or an admin may.</summary>
        public static (bool Ok, string Message) DeleteComment(WorldSession session, ulong commentId)
        {
            using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
            GalleryCommentModel comment = context.GalleryComment.FirstOrDefault(c => c.Id == commentId);
            if (comment == null)
                return (false, "That comment is gone.");
            if (comment.AuthorGuid != session.PlayerGuid && Claims.OwnerOf(comment.ClaimId) != session.PlayerGuid && !Admin.IsAdmin(session))
                return (false, "Only the comment's author or the site's owner can remove it.");
            context.GalleryComment.Remove(comment);
            context.SaveChanges();
            log.Info($"{session.CharacterName} removed comment {commentId} from build site {comment.ClaimId}.");
            return (true, "Comment removed.");
        }
    }
}
