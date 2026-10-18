using LandmarkEmulator.Internal.Commerce;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.CommerceServer
{
    /// <summary>
    /// Showcase following and its daily digest mail in the commerce service (OPEN-LEADS L137; the world's in-process code
    /// is Game/ShowcaseFollow.cs, whose class notes have the live sources and the client trace). With COMMERCE_ADDRESS set
    /// the products live here, so the follows and the digest do too, like live's one commerce back end serving every world.
    /// <list type="bullet">
    /// <item><b>The list</b>: creators (table following, shared with the client's FollowCreator 0x35 / UnFollowCreator 0x36,
    /// ProductService.Follow) and tag groups (following_tag), per follower guid and matched across the follower's realm
    /// (the worlds sharing a character database share guids). The Showcase page's Following tab on any world asks through
    /// CommerceFollowRequest → CommerceFollowReply; a creator typed by name was resolved by the world in its own character
    /// database (the realm's). Limits and texts: <see cref="FollowDigest"/>, the same as in-process.</item>
    /// <item><b>The digest</b>: once per UTC day (the hour CommerceServer.json ShowcaseDigestHourUtc, on with
    /// ShowcaseFollowDigest: the same settings as the worlds' in-process digest, Docker SHOWCASE_DIGEST_HOUR_UTC /
    /// SHOWCASE_FOLLOW_DIGEST), one run per realm: the slot "day:yyyy-MM-dd" is recorded per realm first (digest_run,
    /// unique; unfinished until its mails are stored, then a failed run is retried), so a restart the same day does not
    /// mail again. The window runs from the realm's last finished run (the first looks
    /// back 24 hours) to now. One query for the products listed in it (listed_at, set when peer review or an admin lists a
    /// design and when a site is published; created_at for rows listed before that column); nothing new = done. Else one
    /// query for the realm's follow rows that can match (follows of those creators, every tag group), and per follower
    /// one mail (<see cref="FollowDigest.Compose"/>). Which products a follower hears about [I]: every realm's Player
    /// Studio designs (the Showcase's Player Studio page lists them all) and the gallery sites of their own realm's worlds
    /// (a world's gallery shows its own sites); creator follows match only their own realm's products, since a creator id
    /// is a character guid of the follower's realm.</item>
    /// <item><b>The mail</b> (modelled on the Player Studio share, CommercePayout): stored in digest_mail (pending) and sent
    /// as CommerceDigestMail to one linked world of the follower's realm — the world they followed from first — which
    /// mails it through its social seam (SocialServices.Service.SendSystemMail: the social service when SOCIAL_ADDRESS is
    /// set) with the client's Player Studio sender text 563379, greeting the follower by their current name, and answers
    /// CommerceDigestMailSent. Mails for a realm with no world linked wait until one links (<see cref="SendPending"/>); a
    /// mail handed to a world is not offered to another world of the realm for <see cref="InFlight"/> unless that world's
    /// link closed, so two worlds linking together do not both mail it.</item>
    /// </list>
    /// Every call runs on the hub's work loop (no locks), like the other areas.
    /// </summary>
    public sealed class FollowService
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private readonly CommerceHub hub;
        private CommerceStore Store => hub.Store;

        /// <summary>The UTC day whose scheduled runs are done (every known realm checked).</summary>
        private DateTime doneDay = DateTime.MinValue;

        /// <summary>How long a mail handed to a world waits for its confirmation before another world of the realm may take it.</summary>
        private static readonly TimeSpan InFlight = TimeSpan.FromMinutes(10);
        /// <summary>Mails handed to a world and not confirmed yet: mail → (world, when).</summary>
        private readonly Dictionary<ulong, (WorldLink World, DateTime At)> inFlight = new();

        public FollowService(CommerceHub hub) => this.hub = hub;

        /// <summary>The daily mail is on (CommerceServer.json ShowcaseFollowDigest; Docker SHOWCASE_FOLLOW_DIGEST).</summary>
        public bool DigestOn => hub.Config.ShowcaseFollowDigest;

        // ---- the list (the Showcase page's Following tab) --------------------------------------------------------------

        /// <summary>A CommerceFollowRequest: the change, then the follower's list as it is now.</summary>
        public CommerceFollowReply Handle(WorldLink world, CommerceFollowRequest request)
        {
            CommerceCaller caller = request.Caller;
            string realm = world.Realm;
            ulong follower = caller.Character;
            (bool ok, string message) = request.Action switch
            {
                CommerceFollowRequest.List => (true, ""),
                CommerceFollowRequest.AddCreator => AddCreator(world, caller, request.Creator, request.Text),
                CommerceFollowRequest.AddTags => AddTags(world, caller, request.Text),
                CommerceFollowRequest.RemoveCreator => Store.Unfollow(realm, follower, request.Creator)
                    ? (true, "No longer followed.") : (false, "That wasn't followed."),
                CommerceFollowRequest.RemoveTags => Store.UnfollowTags(realm, follower, FollowDigest.NormaliseTags(request.Text))
                    ? (true, "No longer followed.") : (false, "That wasn't followed."),
                _ => (false, "Unknown request.")
            };
            if (request.Action != CommerceFollowRequest.List)
                log.Info($"{caller.Name} (world {caller.World}) following: action {request.Action} {request.Creator} \"{request.Text}\" → {ok} ({message}).");
            return new CommerceFollowReply
            {
                RequestId = request.RequestId, Ok = ok, Message = message, DigestOn = DigestOn,
                Creators = Store.Followings(realm, follower), Tags = Store.TagGroups(realm, follower)
            };
        }

        private (bool, string) AddCreator(WorldLink world, CommerceCaller caller, ulong creator, string name)
        {
            if (creator == 0)
                return (false, "No such creator.");
            if (creator == caller.Character)
                return (false, "You can't follow yourself.");
            name = FollowDigest.Clip((name ?? "").Trim(), 64);
            if (Store.IsFollowing(world.Realm, caller.Character, creator))
            {
                Store.Follow(world.WorldId, caller.Character, creator, name, caller.Name);   // refreshes the name, as in-process
                return (true, $"You already follow {name}.");
            }
            if (Store.Followings(world.Realm, caller.Character).Count >= FollowDigest.MaxCreators)
                return (false, $"You can follow up to {FollowDigest.MaxCreators} creators.");
            Store.Follow(world.WorldId, caller.Character, creator, name, caller.Name);
            return (true, $"You now follow {name}.");
        }

        private (bool, string) AddTags(WorldLink world, CommerceCaller caller, string text)
        {
            string tags = FollowDigest.CheckTags(text, out string refusal);
            if (tags == null)
                return (false, refusal);
            List<string> groups = Store.TagGroups(world.Realm, caller.Character);
            if (groups.Contains(tags))
                return (true, $"You already follow \"{tags}\".");
            if (groups.Count >= FollowDigest.MaxTagGroups)
                return (false, $"You can follow up to {FollowDigest.MaxTagGroups} tag groups.");
            Store.FollowTags(world.WorldId, caller.Character, caller.Name, tags);
            return (true, FollowDigest.FollowedTagsText(tags));
        }

        // ---- the daily digest ------------------------------------------------------------------------------------------

        /// <summary>
        /// Work loop, once a minute (CommerceHub's timer): when the digest is on, past the hour and not done today, the day's
        /// run for every known realm.
        /// </summary>
        public void Tick()
        {
            DateTime now = DateTime.UtcNow;
            if (!DigestOn || doneDay == now.Date || now.Hour < FollowDigest.Hour(hub.Config.ShowcaseDigestHourUtc))
                return;
            string slot = FollowDigest.DaySlot(now);
            bool failed = false;
            foreach (string realm in hub.KnownRealms())
            {
                try
                {
                    Run(realm, slot, null, false);
                }
                catch (Exception e)
                {
                    // One realm failing (e.g. the database briefly away) must not stop the others; it is tried again next
                    // minute: its slot stays unfinished until the mails are stored, and the retry takes it over (review #5).
                    // Until 2026-10-18 this returned here, so the realms listed after a failing one got no digest at all
                    // while it kept failing (a realm whose run always fails starved every realm behind it).
                    log.Error(e, $"Showcase following digest for realm {realm} failed.");
                    failed = true;
                }
            }
            if (!failed)
                doneDay = now.Date;
        }

        /// <summary>
        /// One realm's run (the day's, or a dev run): records <paramref name="slot"/> (an already recorded slot = done,
        /// nothing sent), composes the mails for what was listed since the realm's last run (or the last
        /// <paramref name="hours"/>), stores and sends them. <paramref name="dryRun"/>: compose only. Returns a summary for
        /// the log / the dev command.
        /// </summary>
        public string Run(string realm, string slot, double? hours, bool dryRun)
        {
            DateTime until = DateTime.UtcNow;
            if (Store.HasDigestRun(realm, slot))
                return $"realm {realm}: {slot} already run";
            DateTime? last = Store.LastDigestRun(realm);
            DateTime since = hours is double h && h > 0 ? until - FollowDigest.LookBack(h)
                : last ?? until - FollowDigest.FirstWindow;
            // The run is recorded first, unfinished (CommerceStore.DigestRunPending), and marked finished only once its mails
            // are stored in digest_mail (from where they are delivered until confirmed). Until 2026-10-18 it counted as done
            // when recorded, so a failure in Compose or while storing lost that window's mails (review #5).
            if (!dryRun && !Store.AddDigestRun(realm, slot, until))
                return $"realm {realm}: {slot} already run";
            List<(ulong Recipient, uint World, string Name, FollowDigest.Mail Mail)> letters = Compose(realm, since, until);
            if (!dryRun)
            {
                // All of the run's mails and its "finished" mark in one transaction, then sent: a failure stores none of
                // them, so the retry (which composes them again) can't mail anyone twice.
                List<ulong> ids = Store.AddDigestRunMails(realm, slot, letters
                    .Select(l => (l.World, l.Recipient, l.Name, FollowDigest.Clip(l.Mail.Subject, 128), l.Mail.Body)).ToList());
                for (int i = 0; i < letters.Count; i++)
                {
                    (ulong recipient, uint world, string name, FollowDigest.Mail mail) = letters[i];
                    Send(new CommerceStore.DigestMailRow(ids[i], realm, world, recipient, name, mail.Subject, mail.Body));
                }
            }
            string summary = $"realm {realm} {slot}{(dryRun ? " (dry run)" : "")}: items listed {since:u} .. {until:u}; {letters.Count} mail(s)"
                + (letters.Count > 0 ? ": " + string.Join(", ", letters.Take(10).Select(l => $"{(l.Name.Length > 0 ? l.Name : l.Recipient.ToString())} ({l.Mail.Items})")) : "");
            log.Info("Showcase following digest, " + summary + ".");
            return summary;
        }

        /// <summary>
        /// The realm's mails for the items listed in (<paramref name="since"/>, <paramref name="until"/>]: two queries when
        /// there is something new, one when there is not.
        /// </summary>
        private List<(ulong Recipient, uint World, string Name, FollowDigest.Mail Mail)> Compose(string realm, DateTime since, DateTime until)
        {
            var letters = new List<(ulong, uint, string, FollowDigest.Mail)>();
            // Every realm's designs; this realm's gallery sites. Creator follows match this realm's products only.
            List<FollowDigest.Item> items = Store.ListedBetween(since, until)
                .Where(p => p.ItemType == FollowDigest.ItemDesign || hub.RealmOf(p.World) == realm)
                .Select(p => new FollowDigest.Item(p.Id, p.Owner, p.OwnerName, p.Name, p.ItemType, FollowDigest.Words(p.Tags).ToHashSet(),
                    p.ListedAt, hub.RealmOf(p.World) == realm))
                .ToList();
            if (items.Count == 0)
                return letters;
            List<ulong> creators = items.Where(i => i.CreatorMatch).Select(i => i.Owner).Distinct().ToList();
            List<CommerceStore.FollowRow> rows = Store.FollowRows(realm, creators);
            foreach (IGrouping<ulong, CommerceStore.FollowRow> mine in rows.GroupBy(f => f.Follower))
            {
                FollowDigest.Mail mail = FollowDigest.Compose(mine.Key, mine.Select(f => new FollowDigest.Follow(f.Creator, f.Tags)), items, since);
                if (mail == null)
                    continue;
                // Delivered through the world the follower first followed from (rows are oldest first), named as they last were.
                CommerceStore.FollowRow first = mine.First();
                string name = mine.Select(f => f.FollowerName).LastOrDefault(n => !string.IsNullOrEmpty(n)) ?? "";
                letters.Add((mine.Key, first.World, name, mail));
            }
            return letters;
        }

        // ---- delivery (as CommercePayout) ----------------------------------------------------------------------------

        /// <summary>A mail to one linked world of the follower's realm (the world they followed from first), or it waits.</summary>
        private void Send(CommerceStore.DigestMailRow mail)
        {
            WorldLink target = hub.LinkOf(mail.World);
            if (target == null || target.Realm != mail.Realm)
                target = hub.LinksOfRealm(mail.Realm).FirstOrDefault();
            if (target == null)
            {
                log.Debug($"Following digest mail {mail.Id} for {mail.Follower} waits: no world of realm {mail.Realm} linked.");
                return;
            }
            inFlight[mail.Id] = (target, DateTime.UtcNow);
            hub.SendService(target, new CommerceDigestMail
            {
                Mail = mail.Id, Recipient = mail.Follower, RecipientName = mail.FollowerName, Subject = mail.Subject, Body = mail.Body
            });
        }

        /// <summary>
        /// A world linked: the digest mails still owed to its realm, except those another world of the realm was given
        /// lately and still holds.
        /// </summary>
        public void SendPending(WorldLink world)
        {
            DateTime now = DateTime.UtcNow;
            int sent = 0;
            foreach (CommerceStore.DigestMailRow mail in Store.PendingDigestMails(world.Realm))
            {
                if (inFlight.TryGetValue(mail.Id, out var given) && given.World != world && given.World.Link.IsOpen && now - given.At < InFlight)
                    continue;
                inFlight[mail.Id] = (world, now);
                hub.SendService(world, new CommerceDigestMail
                {
                    Mail = mail.Id, Recipient = mail.Follower, RecipientName = mail.FollowerName, Subject = mail.Subject, Body = mail.Body
                });
                sent++;
            }
            if (sent > 0)
                log.Info($"{world}: {sent} owed following digest mail(s) sent.");
        }

        /// <summary>CommerceDigestMailSent: a world mailed it.</summary>
        public void Sent(ulong mail)
        {
            inFlight.Remove(mail);
            Store.DigestMailSent(mail);
        }

        /// <summary>The dev / admin command "followdigest [dry] [hours]" from a world: a run for that world's realm now.</summary>
        public (bool Ok, string Text) AdminRun(WorldLink world, string arguments)
        {
            string[] a = (arguments ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            bool dry = a.Contains("dry", StringComparer.OrdinalIgnoreCase);
            double? hours = a.Select(x => double.TryParse(x, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                out double v) ? v : (double?)null).FirstOrDefault(v => v != null);
            string summary = Run(world.Realm, FollowDigest.DevSlot(DateTime.UtcNow), hours, dry);
            return (true, "followdigest (commerce service) " + summary + (DigestOn ? "." : ". The daily run is off (SHOWCASE_FOLLOW_DIGEST=false)."));
        }
    }
}
