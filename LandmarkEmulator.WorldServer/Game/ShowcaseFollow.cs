using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Internal.Commerce;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.GameTable.Text;
using LandmarkEmulator.WorldServer.Game.WorldChannel;
using Microsoft.EntityFrameworkCore;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Following creators and tags in the Showcase, and the daily mail of what they listed (OPEN-LEADS L137).
    /// <para>
    /// <b>What live did</b> (Update Notes 27 Aug 2014, Showcase and Player Studio enhancements; design/forum/patches/
    /// 2014-08-27-46891, the developers' notes): on the Showcase's My Store tab, Start Following let a player follow a
    /// creator by name or follow tags; separate tags (PVP, Castle) match items with either tag, while a group of tags
    /// (PVP Castles) matches only items with all of them; once a day the server searched what had been listed on the
    /// Marketplace or Gallery and sent an in-game mail about the followed creators and tags, plus an e-mail to players
    /// who had one on file. Still there at launch: the
    /// wiki's Showcase page (2016) says players following their favourite creators, or tag words they care about, see
    /// the newly added items in My Store [W]. No later patch
    /// note, dev post or forum thread in design/ mentions it again (searched: forum/all, forum/patches, re/out/forum_raw,
    /// wiki pages, patchnotes-after-0589.md for follow / following / digest / My Store), so the 2014 rules stand.
    /// </para>
    /// <para>
    /// <b>The client's part</b> [P]: only creators. The Lua natives InGamePurchaseStoreScreen.FollowCreator / UnFollowCreator
    /// send FollowCreator 0x35 / UnFollowCreator 0x36 {u64 creator} (Commerce.Follow), and AccountStore carries the
    /// followings as {u64 id, string name} (the client's following map, manager +0x232c88). The 0.695 client has no tag
    /// following at all: no native (luabind_695.tsv: the InGamePurchaseStoreScreen tag natives ProductAddTag / ProductSetTags
    /// tag one's own product; Browser.OpenMarketplaceWithTags opens a search), no packet (packet_ids_695.tsv, rtti_695:
    /// only PacketInGamePurchaseCommerceFollowCreator / UnFollowCreator / AddFollowing / RemoveFollowing), no text, and the
    /// AccountStore reader has no tag list. So live's tag following lived on its web pages and Daybreak's web back end
    /// [I]; no shipped script calls the follow natives either (the My Store pages were Daybreak's, not shipped). Here both
    /// are on the Showcase page's Following tab (WebPages /api/follows, /api/follow, /api/unfollow; showcase.js), and the
    /// client's FollowCreator packets land in the same list.
    /// </para>
    /// <para>
    /// <b>This server, in-process commerce</b>: the list is the character database's showcase_follow table (one row per
    /// creator or tag group; a group's words lower case and sorted). Once a day (<see cref="WorldServerConfiguration.ShowcaseDigestHourUtc"/>,
    /// UTC), with <see cref="WorldServerConfiguration.ShowcaseFollowDigest"/> on (default, as live), one pass:
    /// the products listed since the last run (player_studio_product status Listed with listedAt — a design accepted by
    /// peer review or an admin, a build site published to the gallery — after the newest showcase_digest_run row; the
    /// first run looks back 24 hours) in one query; nothing new = no further query and no mail. Otherwise the follow rows
    /// for those creators plus every tag group in a second query, and per follower one mail listing the new items from
    /// creators they follow and the items carrying every word of a tag group they follow (their own items left out, each
    /// item once). Players with nothing new get no mail. The scheduled run's slot is unique per UTC day, so worlds sharing the
    /// character database send each day's mail once. Mails go through the social seam
    /// (SocialServices.Service.SendSystemMail) from the world thread, a few per tick. The e-mail copy live sent is not
    /// built (no e-mail here).
    /// </para>
    /// <para>
    /// <b>The mail's text is ours [I]</b>: live composed it on its servers and no copy of one survives in design/ (the
    /// client has no text for it: text_695.tsv has no "follow" string of this kind, and the client's own Player Studio mail
    /// kinds 0x10 / 0x20 / 0x40 / 0x80, FUN_143639de0, are review results only). The sender is the client's own Player
    /// Studio mail sender, text 563379 (PlayerStudio.MailUser, FUN_143639de0) [P for that text]. Limits are ours [I]:
    /// <see cref="MaxCreators"/> creators, <see cref="MaxTagGroups"/> tag groups of up to <see cref="MaxWordsPerGroup"/>
    /// words, <see cref="MaxItemsPerMail"/> items per mail. The rules and the text are shared with the commerce service
    /// (LandmarkEmulator.Internal/Commerce/FollowDigest.cs).
    /// </para>
    /// <para>
    /// <b>Commerce service</b> (COMMERCE_ADDRESS set, since 2026-10-18): the products, the follow lists (creators and tag
    /// groups) and the digest are the service's (LandmarkEmulator.CommerceServer/FollowService.cs), like live's one commerce
    /// back end for every world. The Showcase page's Following tab asks it through the world's commerce link
    /// (RemoteCommerceService: CommerceFollowRequest / Reply; a creator typed by name is found in this world's character
    /// database first); the service runs one digest per realm at the same hour (its own SHOWCASE_FOLLOW_DIGEST /
    /// SHOWCASE_DIGEST_HOUR_UTC) and hands each follower's mail to a world of their realm (CommerceDigestMail), which mails
    /// it from here (<see cref="Deliver"/>, the same outbox and sender) and confirms. This world then runs no digest of its
    /// own, and the showcase_follow rows of its in-process days stay where they are (not copied to the service).
    /// </para>
    /// </summary>
    public static class ShowcaseFollow
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>Most creators one player follows [I: live's limit is not known].</summary>
        public const int MaxCreators = FollowDigest.MaxCreators;
        /// <summary>Most tag groups one player follows [I].</summary>
        public const int MaxTagGroups = FollowDigest.MaxTagGroups;
        /// <summary>Most words in one tag group, and the longest word [I].</summary>
        public const int MaxWordsPerGroup = FollowDigest.MaxWordsPerGroup, MaxWordLength = FollowDigest.MaxWordLength;
        /// <summary>Most items one mail lists; the rest are counted [I] (the mail body holds 4096 characters).</summary>
        public const int MaxItemsPerMail = FollowDigest.MaxItemsPerMail;

        private static WorldServerConfiguration Config =>
            Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config;

        /// <summary>
        /// The follow list and the digest are this world's own: in-process commerce (the products are in this database).
        /// False with COMMERCE_ADDRESS set: the commerce service keeps them (class notes).
        /// </summary>
        public static bool IsLocal => WorldChannelServices.Commerce is LocalCommerceService;

        /// <summary>
        /// The daily mail is switched on here (WorldServer.json ShowcaseFollowDigest, Docker SHOWCASE_FOLLOW_DIGEST); with the
        /// commerce service its own setting counts (the page shows the service's).
        /// </summary>
        public static bool DigestOn => Config?.ShowcaseFollowDigest ?? true;

        /// <summary>The Following tab's data: the creators (id, name when followed) and tag groups followed, oldest first.</summary>
        public sealed record FollowList(bool Available, bool DigestOn, List<(ulong Id, string Name)> Creators, List<string> Tags, string Message = "");

        // ---- the list (in-process) -------------------------------------------------------------------------------------

        /// <summary>The creators a player follows (id, name when followed), oldest first.</summary>
        public static List<(ulong Id, string Name)> Creators(ulong follower)
        {
            using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
            return context.ShowcaseFollow.AsNoTracking().Where(f => f.FollowerGuid == follower && f.CreatorGuid != 0)
                .OrderBy(f => f.Id).Select(f => new { f.CreatorGuid, f.CreatorName }).ToList()
                .Select(f => (f.CreatorGuid, f.CreatorName ?? "")).ToList();
        }

        /// <summary>The tag groups a player follows (normalised words), oldest first.</summary>
        public static List<string> TagGroups(ulong follower)
        {
            using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
            return context.ShowcaseFollow.AsNoTracking().Where(f => f.FollowerGuid == follower && f.CreatorGuid == 0)
                .OrderBy(f => f.Id).Select(f => f.Tags).ToList();
        }

        /// <summary>The in-process Following tab data (<see cref="FollowList"/>).</summary>
        public static FollowList LocalList(ulong follower) => new(true, DigestOn, Creators(follower), TagGroups(follower));

        /// <summary>Follows a creator by character guid (the client's FollowCreator, or the page by name).</summary>
        public static (bool Ok, string Message) AddCreator(ulong follower, ulong creator, string name)
        {
            if (creator == 0)
                return (false, "No such creator.");
            if (creator == follower)
                return (false, "You can't follow yourself.");
            using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
            ShowcaseFollowModel row = context.ShowcaseFollow.FirstOrDefault(f => f.FollowerGuid == follower && f.CreatorGuid == creator && f.Tags == "");
            if (row != null)
            {
                // Already followed: keep the row, refresh the name (as the commerce service's ON DUPLICATE KEY UPDATE).
                if (!string.IsNullOrEmpty(name) && row.CreatorName != name)
                {
                    row.CreatorName = FollowDigest.Clip(name, 64);
                    context.SaveChanges();
                }
                return (true, $"You already follow {name}.");
            }
            if (context.ShowcaseFollow.Count(f => f.FollowerGuid == follower && f.CreatorGuid != 0) >= MaxCreators)
                return (false, $"You can follow up to {MaxCreators} creators.");
            context.ShowcaseFollow.Add(new ShowcaseFollowModel
            {
                FollowerGuid = follower, CreatorGuid = creator, CreatorName = FollowDigest.Clip(name ?? "", 64), Tags = "", FollowedAt = DateTime.UtcNow
            });
            if (!Save(context))
                return (false, "That didn't work; try again.");
            return (true, $"You now follow {name}.");
        }

        /// <summary>
        /// The character a creator's name typed on the page names (live: "type in the name of your favorite creator"), in
        /// this world's character database — the realm's, in both commerce modes. Id 0 with the refusal when there is none.
        /// </summary>
        public static (ulong Id, string Name, string Refusal) FindCreator(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > 64)
                return (0, "", "Type the creator's character name.");
            using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
            var hit = context.Character.AsNoTracking().Where(c => c.Name == name).Select(c => new { c.Id, c.Name }).FirstOrDefault();
            return hit == null ? (0, "", $"There is no character named {name}.") : (hit.Id, hit.Name, null);
        }

        /// <summary>Follows a creator typed by name (live: "type in the name of your favorite creator").</summary>
        public static (bool Ok, string Message) AddCreatorByName(ulong follower, string name)
        {
            (ulong id, string exact, string refusal) = FindCreator(name);
            return id == 0 ? (false, refusal) : AddCreator(follower, id, exact);
        }

        /// <summary>Stops following a creator; false when they were not followed.</summary>
        public static bool RemoveCreator(ulong follower, ulong creator)
        {
            using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
            List<ShowcaseFollowModel> rows = context.ShowcaseFollow.Where(f => f.FollowerGuid == follower && f.CreatorGuid == creator && creator != 0).ToList();
            if (rows.Count == 0)
                return false;
            context.ShowcaseFollow.RemoveRange(rows);
            return Save(context);
        }

        /// <summary>Follows a tag group: every word must be on an item for it to count (live's "PVP Castles").</summary>
        public static (bool Ok, string Message) AddTags(ulong follower, string text)
        {
            string tags = FollowDigest.CheckTags(text, out string refusal);
            if (tags == null)
                return (false, refusal);
            using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
            if (context.ShowcaseFollow.Any(f => f.FollowerGuid == follower && f.CreatorGuid == 0 && f.Tags == tags))
                return (true, $"You already follow \"{tags}\".");
            if (context.ShowcaseFollow.Count(f => f.FollowerGuid == follower && f.CreatorGuid == 0) >= MaxTagGroups)
                return (false, $"You can follow up to {MaxTagGroups} tag groups.");
            context.ShowcaseFollow.Add(new ShowcaseFollowModel
            {
                FollowerGuid = follower, CreatorGuid = 0, CreatorName = "", Tags = tags, FollowedAt = DateTime.UtcNow
            });
            if (!Save(context))
                return (false, "That didn't work; try again.");
            return (true, FollowDigest.FollowedTagsText(tags));
        }

        /// <summary>Stops following a tag group; false when it was not followed.</summary>
        public static bool RemoveTags(ulong follower, string text)
        {
            string tags = FollowDigest.NormaliseTags(text);
            using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
            List<ShowcaseFollowModel> rows = context.ShowcaseFollow.Where(f => f.FollowerGuid == follower && f.CreatorGuid == 0 && f.Tags == tags).ToList();
            if (rows.Count == 0)
                return false;
            context.ShowcaseFollow.RemoveRange(rows);
            return Save(context);
        }

        /// <summary>The words of a tag string: lower case, '#' dropped, each once.</summary>
        public static string[] Words(string tags) => FollowDigest.Words(tags);

        /// <summary>A tag group as stored: its words (each cut to <see cref="MaxWordLength"/>), sorted, one space apart.</summary>
        public static string NormaliseTags(string text) => FollowDigest.NormaliseTags(text);

        private static bool Save(LandmarkEmulator.Database.Character.CharacterContext context)
        {
            try
            {
                context.SaveChanges();
                return true;
            }
            catch (DbUpdateException e)
            {
                log.Warn($"Showcase follow list not saved: {e.InnerException?.Message ?? e.Message}");
                return false;
            }
        }

        // ---- the daily mail --------------------------------------------------------------------------------------------

        /// <summary>
        /// A mail ready to send; <paramref name="Sent"/> runs on the world thread once it went out (the commerce service's
        /// confirmation, <see cref="Deliver"/>).
        /// </summary>
        public sealed record Letter(ulong Recipient, string RecipientName, string Subject, string Body, int Items, Action Sent = null);

        private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(1);
        /// <summary>Mails handed to the social seam per world tick.</summary>
        private const int MailsPerTick = 20;

        private static DateTime lastCheck = DateTime.MinValue;
        /// <summary>The UTC day whose scheduled run this world has done or seen done.</summary>
        private static DateTime doneDay = DateTime.MinValue;
        private static volatile bool running;
        private static bool toldRemote;
        private static readonly ConcurrentQueue<Letter> outbox = new();

        /// <summary>
        /// <see cref="ShowcaseDigestRunModel.Mails"/> of a run that has not finished: its row is written first (so of two worlds
        /// only one runs the day) with this marker, and the mail count replaces it only once every letter has gone out. The
        /// next run's window starts at the newest <i>finished</i> run, and a run left unfinished (the world stopped with
        /// letters in the outbox, or the run failed) is taken over after <see cref="PendingStale"/> and runs its window again.
        /// Until 2026-10-18 the row counted as done as soon as it was written, so a failure after it lost that window's
        /// mails, and a failure before it still marked the day done (review #5). Delivery is at least once: a run taken
        /// over can mail some letters a second time (harmless text).
        /// </summary>
        public const uint RunPending = uint.MaxValue;

        /// <summary>An unfinished run row older than this was left by a world that stopped or failed: the next run takes it over.</summary>
        private static readonly TimeSpan PendingStale = TimeSpan.FromMinutes(15);

        /// <summary>
        /// A mail the commerce service composed (CommerceDigestMail, any thread): queued with the in-process mails, sent from
        /// the world thread at the same pace, <paramref name="sent"/> run after it went out.
        /// </summary>
        public static void Deliver(ulong recipient, string recipientName, string subject, string body, Action sent)
            => outbox.Enqueue(new Letter(recipient, recipientName, subject, FollowDigest.Greeting(recipientName) + body, 0, sent));

        /// <summary>
        /// World thread, every tick: sends the mails a run composed (a few per tick), and once a minute starts the day's run
        /// when it is due (on, in-process, past the hour, not done today). The run's database work is on a pool thread. With
        /// the commerce service the run is the service's (FollowService); its mails arrive through <see cref="Deliver"/>.
        /// </summary>
        public static void Update()
        {
            for (int i = 0; i < MailsPerTick && outbox.TryDequeue(out Letter letter); i++)
            {
                Social.SocialServices.Service.SendSystemMail(letter.Recipient, Sender, letter.Subject, letter.Body, 0);
                letter.Sent?.Invoke();
            }
            DateTime now = DateTime.UtcNow;
            if (now - lastCheck < CheckEvery)
                return;
            lastCheck = now;
            int hour = FollowDigest.Hour(Config?.ShowcaseDigestHourUtc ?? 10u);
            if (!DigestOn || running || doneDay == now.Date || now.Hour < hour)
                return;
            if (!IsLocal)
            {
                if (!toldRemote)
                    log.Info("Showcase following digest: run by the commerce service for this world (COMMERCE_ADDRESS is set; its SHOWCASE_FOLLOW_DIGEST / SHOWCASE_DIGEST_HOUR_UTC count).");
                toldRemote = true;
                doneDay = now.Date;
                return;
            }
            running = true;
            DateTime day = now.Date;
            Task.Run(() =>
            {
                try
                {
                    // Done only when the day's run finished or was found finished; another world's run still going (or a
                    // failure here) is looked at again next minute (review #5: the day was marked done even on failure).
                    if (Run(FollowDigest.DaySlot(day), null, false).Done)
                        doneDay = day;
                }
                catch (Exception e)
                {
                    log.Error(e, "Showcase following digest failed; tried again in a minute.");
                }
                finally
                {
                    running = false;
                }
            });
        }

        /// <summary>
        /// Runs the in-process digest now (DevCommands "followdigest", through LocalCommerceService): <paramref name="hours"/>
        /// = look back that far instead of to the last run; <paramref name="dryRun"/> = compose only (nothing sent, no run
        /// recorded). Pool thread; the mails go out from the world thread.
        /// </summary>
        public static Task<List<Letter>> RunNow(double? hours, bool dryRun)
            => Task.Run(() => Run(FollowDigest.DevSlot(DateTime.UtcNow), hours, dryRun).Letters);

        /// <summary>
        /// One run: records it under <paramref name="slot"/> as unfinished (<see cref="RunPending"/>; unique: a scheduled day
        /// another world has finished returns nothing, one it is still running returns not done), composes the mails for
        /// what was listed since the last finished run (or the last <paramref name="hours"/>) and queues them; the row is
        /// marked finished once the last of them went out (<see cref="Finish"/>). Done: this slot needs no more work here.
        /// </summary>
        private static (List<Letter> Letters, bool Done) Run(string slot, double? hours, bool dryRun)
        {
            DateTime until = DateTime.UtcNow;
            DateTime since;
            ShowcaseDigestRunModel run = null;
            using (var context = DatabaseManager.Instance.CharacterDatabase.NewContext())
            {
                ShowcaseDigestRunModel existing = context.ShowcaseDigestRun.AsNoTracking().FirstOrDefault(r => r.Slot == slot);
                if (existing != null && existing.Mails != RunPending)
                    return (new List<Letter>(), true);
                DateTime? last = context.ShowcaseDigestRun.Where(r => r.Mails != RunPending).Max(r => (DateTime?)r.RanAt);
                since = hours is double h && h > 0 ? until - FollowDigest.LookBack(h)
                    : last is DateTime l ? DateTime.SpecifyKind(l, DateTimeKind.Utc) : until - FollowDigest.FirstWindow;
                if (!dryRun)
                {
                    if (existing == null)
                    {
                        // The run is recorded first: of two worlds starting the same day only one row is stored.
                        run = new ShowcaseDigestRunModel { Slot = slot, RanAt = until, Mails = RunPending };
                        context.ShowcaseDigestRun.Add(run);
                        try
                        {
                            context.SaveChanges();
                        }
                        catch (DbUpdateException)
                        {
                            log.Info($"Showcase following digest {slot}: started by another world just now.");
                            return (new List<Letter>(), false);
                        }
                    }
                    else
                    {
                        // Unfinished: another world is running it, or a world stopped / failed mid-run. Taken over (one world
                        // wins the UPDATE) once it is stale.
                        DateTime staleBefore = until - PendingStale;
                        int taken = context.Database.ExecuteSqlInterpolated(
                            $"UPDATE showcase_digest_run SET ranAt = {until} WHERE id = {existing.Id} AND mails = {RunPending} AND ranAt < {staleBefore}");
                        if (taken != 1)
                            return (new List<Letter>(), false);
                        run = existing;
                        run.RanAt = until;
                        log.Warn($"Showcase following digest {slot}: unfinished run taken over; its window is mailed again from {since:u}.");
                    }
                }
            }
            List<Letter> letters;
            try
            {
                letters = Compose(since, until);
            }
            catch (Exception)
            {
                // Nothing went out: make the row stale at once, so the retry next minute takes it over.
                if (run != null)
                    Release(run.Id);
                throw;
            }
            if (!dryRun)
            {
                if (letters.Count == 0)
                    Finish(run.Id, 0);
                else
                {
                    int left = letters.Count;
                    ulong runId = run.Id;
                    // World thread (ShowcaseFollow.Update): the last letter sent finishes the run.
                    foreach (Letter letter in letters)
                        outbox.Enqueue(letter with { Sent = () => { if (--left == 0) Task.Run(() => Finish(runId, letters.Count)); } });
                }
            }
            log.Info($"Showcase following digest {slot}{(dryRun ? " (dry run)" : "")}: items listed {since:u} .. {until:u}; "
                + $"{letters.Count} mail(s){(letters.Count > 0 ? ": " + string.Join(", ", letters.Take(10).Select(l => $"{l.RecipientName} ({l.Items})")) : "")}.");
            return (letters, true);
        }

        /// <summary>Marks a run finished with its mail count (pool thread). A failure leaves it unfinished: taken over later.</summary>
        private static void Finish(ulong runId, int mails)
        {
            try
            {
                using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
                context.Database.ExecuteSqlInterpolated($"UPDATE showcase_digest_run SET mails = {(uint)mails} WHERE id = {runId}");
            }
            catch (Exception e)
            {
                log.Error(e, $"Showcase following digest run {runId}: not marked finished; it is run again later.");
            }
        }

        /// <summary>Makes an unfinished run row stale at once (its run failed before anything went out).</summary>
        private static void Release(ulong runId)
        {
            try
            {
                using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
                context.Database.ExecuteSqlInterpolated(
                    $"UPDATE showcase_digest_run SET ranAt = {new DateTime(2000, 1, 1)} WHERE id = {runId} AND mails = {RunPending}");
            }
            catch (Exception e)
            {
                log.Warn($"Showcase following digest run {runId}: not released ({e.Message}); it is taken over after {PendingStale.TotalMinutes} minutes.");
            }
        }

        /// <summary>
        /// The mails for the items listed in (<paramref name="since"/>, <paramref name="until"/>]: two queries when there is
        /// something new (items, then the follow rows that can match them), one when there is not.
        /// </summary>
        private static List<Letter> Compose(DateTime since, DateTime until)
        {
            var raw = new List<(ulong Id, ulong Owner, string Name, uint ItemType, string Tags, DateTime Listed)>();
            List<ShowcaseFollowModel> follows;
            using (var context = DatabaseManager.Instance.CharacterDatabase.NewContext())
            {
                raw = context.PlayerStudioProduct.AsNoTracking()
                    .Where(p => p.Status == Commerce.Listed && (p.ListedAt ?? p.CreatedAt) > since && (p.ListedAt ?? p.CreatedAt) <= until)
                    .Select(p => new { p.Id, p.OwnerGuid, p.Name, p.ItemType, p.Tags, Listed = p.ListedAt ?? p.CreatedAt })
                    .ToList()
                    .Select(p => (p.Id, p.OwnerGuid, p.Name ?? "", p.ItemType, p.Tags, p.Listed))
                    .OrderBy(p => p.Listed).ThenBy(p => p.Id).ToList();
                if (raw.Count == 0)
                    return new List<Letter>();
                var owners = raw.Select(i => i.Owner).Distinct().ToList();
                follows = context.ShowcaseFollow.AsNoTracking()
                    .Where(f => f.CreatorGuid == 0 || owners.Contains(f.CreatorGuid)).ToList();
            }
            if (follows.Count == 0)
                return new List<Letter>();
            Dictionary<ulong, string> names = DatabaseManager.Instance.CharacterDatabase.GetCharacterNames(
                raw.Select(i => i.Owner).Concat(follows.Select(f => f.FollowerGuid)).Distinct());
            List<FollowDigest.Item> items = raw.Select(p => new FollowDigest.Item(p.Id, p.Owner, names.GetValueOrDefault(p.Owner, "?"), p.Name,
                p.ItemType, FollowDigest.Words(p.Tags).ToHashSet(), p.Listed)).ToList();
            var letters = new List<Letter>();
            foreach (IGrouping<ulong, ShowcaseFollowModel> mine in follows.GroupBy(f => f.FollowerGuid))
            {
                if (!names.TryGetValue(mine.Key, out string followerName))
                    continue;   // the follower's character is gone
                FollowDigest.Mail mail = FollowDigest.Compose(mine.Key, mine.Select(f => new FollowDigest.Follow(f.CreatorGuid, f.Tags)), items, since);
                if (mail != null)
                    letters.Add(new Letter(mine.Key, followerName, mail.Subject, FollowDigest.Greeting(followerName) + mail.Body, mail.Items));
            }
            return letters;
        }

        /// <summary>The mails' sender: the client's Player Studio mail sender (text 563379).</summary>
        private static string Sender => TextManager.Instance.GetTextForId(FollowDigest.MailUserText) is { Length: > 0 } text ? text : FollowDigest.SenderFallback;
    }
}
