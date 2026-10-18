using LandmarkEmulator.Internal.Commerce;
using LandmarkEmulator.Shared.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.CommerceServer
{
    /// <summary>
    /// Player Studio / Marketplace in the commerce service: the InGamePurchaseBase 0x45 requests the in-process
    /// Game/Commerce.cs answered (docs/commerce.md; trace/architecture/world-channel-service.md §3.1), with its rules and
    /// its packets, now for every world at once.
    /// <list type="bullet">
    /// <item>0x0B WalletInfoRequest → 0x0C (an empty wallet; the zones answer it themselves since 2026-10-17); 0x1A AccountInfoRequest → 0x1B (US, zip code
    /// on file: Player Studio creation allowed, FUN_1431b4c10); 0x0D ServerStatusRequest → 0x0E true [I] (the zone
    /// answers false while the service is unreachable, spec §7; in-process true as well since 2026-10-18).</item>
    /// <item>0x2F ProductCreate, checked by the zone first (<see cref="CommerceProductCreate"/>) → duplicate check (0xF),
    /// stored (designs waiting for peer review, build sites listed at once), 0x31 status 1.</item>
    /// <item>0x37 RequestAccountStore → 0x39 the player's own products (waiting or listed) from every world of the
    /// player's realm.</item>
    /// <item>0x32 ProductRemove, 0x34 ProductUpdate (with the contest enter / leave bytes, trace/0695/gallery-695.md §4) —
    /// owner only, no answer.</item>
    /// <item>0x40 GetProductForReview → 0x41 the oldest product waiting that the player didn't make or vote on (any
    /// world); 0x43 SubmitProductReview → 0x44; at PlayerStudioApprovals / PlayerStudioRejections the product is listed /
    /// rejected and the owner gets the result mail (kind 0x10 / 0x20) through a world of the owner's realm
    /// (<see cref="CommerceResultMail"/>, waiting in the table while none is linked).</item>
    /// <item>The Showcase's Player Studio page (listed designs, taking one) and the dev / admin decision ("psreview").</item>
    /// </list>
    /// Every other 0x45 request (store bundles, Station Cash, country codes, follow …) stays unanswered as in-process
    /// (protocol.md §14.1: the real-money store is not run). Sub numbers are the 0.695 client's (ClientPackets; 0.589 + 1
    /// from 0x30).
    /// </summary>
    public sealed class ProductService
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private readonly CommerceHub hub;
        private CommerceStore Store => hub.Store;

        /// <summary>CommerceProductCreateResponse codes (FUN_14319fc60).</summary>
        private const uint Created = 1, Duplicate = 0xF;
        /// <summary>SubmitProductReview verdict for an approval; every other value rejects.</summary>
        private const uint VerdictApprove = 1;
        /// <summary>Highest price of a design: the client's StringHashToValue PlayerStudio.MaxListingValue (StudioRules.MaxPrice).</summary>
        private const uint MaxPrice = StudioRules.MaxPrice;
        /// <summary>Mail kinds for review results (the client writes the text from the kind, FUN_143586ba0).</summary>
        public const uint MailAccepted = 0x10, MailRejected = 0x20;
        /// <summary>
        /// The "CS Review" mail kind (FUN_143586ba0: 0x40 → PlayerStudio.CSReviewItem, text 563484) [P]: a product the
        /// peers rejected went to staff (live mailed each status change, Update Notes 26 Jun 2014).
        /// </summary>
        public const uint MailStaffReview = 0x40;

        /// <summary>The result mail's kind for a product's state: listed, rejected, or (still waiting) in the staff queue.</summary>
        private static uint MailKindOf(ProductRow product) => product.Status == CommerceStore.Listed ? MailAccepted
            : product.Status == CommerceStore.Pending ? MailStaffReview : MailRejected;

        public ProductService(CommerceHub hub) => this.hub = hub;

        /// <summary>A client InGamePurchase request handed on by a zone.</summary>
        public void Handle(WorldLink world, CommerceClientRequest request)
        {
            CommerceCaller caller = request.Caller;
            switch (request.Sub)
            {
                case ClientPackets.SubWalletInfoRequest:
                    // Since 2026-10-17 the zones answer the wallet themselves (it carries their Lumens while the Player
                    // Studio listing fee is charged, Game/Commerce.SendWalletInfo); kept for a zone that hands it on.
                    hub.SendClient(world, caller, ClientPackets.WalletInfo());
                    break;
                case ClientPackets.SubServerStatusRequest:
                    hub.SendClient(world, caller, ClientPackets.ServerStatus(true));
                    break;
                case ClientPackets.SubAccountInfoRequest:
                    hub.SendClient(world, caller, ClientPackets.AccountInfo());
                    break;
                case ClientPackets.SubRequestAccountStore:
                    hub.SendClient(world, caller, ClientPackets.AccountStore(Store.OwnLive(world.Realm, caller.Character), caller.Name ?? "",
                        Store.Followings(world.Realm, caller.Character)));
                    break;
                case ClientPackets.SubFollowCreator:
                case ClientPackets.SubUnFollowCreator:
                    Follow(world, caller, request.Sub == ClientPackets.SubFollowCreator, request.Body);
                    break;
                case ClientPackets.SubProductRemove:
                    Remove(world, caller, request.Body);
                    break;
                case ClientPackets.SubProductUpdate:
                    Update(world, caller, request.Body);
                    break;
                case ClientPackets.SubGetProductForReview:
                    OfferReview(world, caller);
                    break;
                case ClientPackets.SubSubmitProductReview:
                    Review(world, caller, request.Body);
                    break;
                default:
                    log.Info($"Commerce request 0x{request.Sub:X2} from {caller.Name} (world {caller.World}): {BitConverter.ToString(request.Body)} (unanswered, as in-process).");
                    break;
            }
        }

        /// <summary>
        /// FollowCreator 0x35 / UnFollowCreator 0x36 {u64 creator id} → AddFollowing 0x3A {u64, bool, string name} /
        /// RemoveFollowing 0x3B {u64, bool} (Commerce.Follow has the client trace, agent-leads-sweep.md L123). Kept per
        /// follower guid and world (the realm's worlds share it); the name is the creator's from their newest product
        /// on the realm [I: the creator id is a product owner's character guid]. The same list as the Showcase page's
        /// Following tab and the daily digest (<see cref="FollowService"/>; the in-process code's limit of
        /// FollowDigest.MaxCreators applies here too since 2026-10-18).
        /// </summary>
        private void Follow(WorldLink world, CommerceCaller caller, bool follow, byte[] body)
        {
            if (body.Length < 8)
                return;
            ulong creator = BitConverter.ToUInt64(body, 0);
            if (follow)
            {
                bool ok = creator != 0 && creator != caller.Character
                    && (Store.IsFollowing(world.Realm, caller.Character, creator)
                        || Store.Followings(world.Realm, caller.Character).Count < FollowDigest.MaxCreators);
                string name = ok ? Store.CreatorName(world.Realm, creator) : "";
                if (ok)
                    Store.Follow(world.WorldId, caller.Character, creator, name, caller.Name);
                hub.SendClient(world, caller, ClientPackets.AddFollowing(creator, ok, name));
                log.Info($"{caller.Name} (world {caller.World}) follows creator {creator} ({name}): {ok}.");
            }
            else
            {
                bool ok = Store.Unfollow(world.Realm, caller.Character, creator);
                hub.SendClient(world, caller, ClientPackets.RemoveFollowing(creator, ok));
                log.Info($"{caller.Name} (world {caller.World}) stops following creator {creator}: {ok}.");
            }
        }

        private static string Clip(string s, int max) => (s ?? "").Length <= max ? s ?? "" : s[..max];

        /// <summary>
        /// A product the zone checked (Commerce.CreateProduct after its owner / tag / price checks): refused when the item
        /// already has a live product, else stored and answered with status 1.
        /// </summary>
        public void Create(WorldLink world, CommerceProductCreate create)
        {
            CommerceCaller caller = create.Caller;
            bool gallery = create.ItemType == CommerceStore.ItemClaim;
            if (create.ItemId == 0 || create.Name.Length == 0
                || (!gallery && (create.Price < StudioRules.MinimumPrice(create.Voxels) || create.Price > MaxPrice))
                || (create.ItemType != CommerceStore.ItemClaim && create.ItemType != CommerceStore.ItemDesign))
            {
                // The zone checks these first; a create that slips through gets the in-process answer (0xC not valid).
                AnswerCreate(world, create, 0xC, 0);
                return;
            }
            if (Store.HasLive(caller.World, world.Realm, create.ItemType, create.ItemId))
            {
                AnswerCreate(world, create, Duplicate, 0);
                return;
            }
            var product = new ProductRow
            {
                WorldId = caller.World, OwnerGuid = caller.Character, OwnerAccount = caller.Account, OwnerName = Clip(caller.Name, 64),
                ItemType = create.ItemType, ItemId = create.ItemId, Name = Clip(create.Name, 128), Description = Clip(create.Description, 1024),
                Headline = Clip(create.Headline, 256), Tags = Clip(create.Tags, 512), Price = gallery ? 0 : create.Price,
                Status = gallery ? CommerceStore.Listed : CommerceStore.Pending, CreatedAt = DateTime.UtcNow, Voxels = create.Voxels
            };
            // A site is listed at once: the following digest's "newly listed" (FollowService), as in-process (Commerce.CreateProduct).
            if (gallery)
                product.ListedAt = product.CreatedAt;
            product.Id = Store.InsertProduct(product, gallery ? null : create.Snapshot);
            if (product.Id == 0 || product.Id > uint.MaxValue)
            {
                // The client's BundleId is a u32 and must not be 0 (Commerce.WriteProduct); never reached with
                // AUTO_INCREMENT from 1, kept as a guard.
                log.Error($"Product id {product.Id} does not fit the client's BundleId: product left waiting, client told \"unknown error\".");
                AnswerCreate(world, create, 0, 0);
                return;
            }
            AnswerCreate(world, create, Created, product.Id);
            MarketChanged(product);
            log.Info(gallery
                ? $"{caller.Name} (world {caller.World}) published build site {create.ItemId} to the gallery (product {product.Id} '{product.Name}')."
                : $"{caller.Name} (world {caller.World}) submitted Player Studio product {product.Id} '{product.Name}' (design {create.ItemId}).");
        }

        /// <summary>
        /// A create's outcome: the client's CreateResponse 0x31 (tunnelled), and for a zone that sent a create id the typed
        /// <see cref="CommerceProductCreateResult"/>, which settles the listing fee it took (review 2026-10-18 #4). The
        /// result is also kept a while for <see cref="CheckCreate"/>.
        /// </summary>
        private void AnswerCreate(WorldLink world, CommerceProductCreate create, uint status, ulong product)
        {
            hub.SendClient(world, create.Caller, ClientPackets.CreateResponse(status));
            if (create.CreateId == 0)
                return;
            DateTime now = DateTime.UtcNow;
            if (createResults.Count > 1000)
                foreach (var old in createResults.Where(c => now - c.Value.At > CreateResultKept).Select(c => c.Key).ToList())
                    createResults.Remove(old);
            createResults[(create.Caller.World, create.CreateId)] = (status, product, now);
            hub.SendService(world, new CommerceProductCreateResult { CreateId = create.CreateId, Status = status, Product = product });
        }

        /// <summary>How long a create's result is kept for a zone's <see cref="CommerceProductCreateCheck"/>.</summary>
        private static readonly TimeSpan CreateResultKept = TimeSpan.FromHours(1);

        /// <summary>Recent create results: (world, create id) → status, product, when.</summary>
        private readonly Dictionary<(uint World, ulong CreateId), (uint Status, ulong Product, DateTime At)> createResults = new();

        /// <summary>
        /// A zone missed a create's result (timeout, link lost): the result kept for that id, else what the store holds —
        /// 1 with the product when the caller has a live product for the item (it was stored), 0 when not (nothing stored;
        /// the zone gives the fee back).
        /// </summary>
        public void CheckCreate(WorldLink world, CommerceProductCreateCheck check)
        {
            uint status;
            ulong product;
            if (createResults.TryGetValue((check.Caller.World, check.CreateId), out var known))
                (status, product) = (known.Status, known.Product);
            else
            {
                product = Store.LiveProductOf(check.Caller.World, world.Realm, check.ItemType, check.ItemId, check.Caller.Character);
                status = product != 0 ? Created : 0u;
            }
            log.Info($"{check.Caller.Name} (world {check.Caller.World}): create {check.CreateId:X} for item {check.ItemId} checked → status {status} (product {product}).");
            hub.SendService(world, new CommerceProductCreateResult { CreateId = check.CreateId, Status = status, Product = product });
        }

        /// <summary>The caller's own product, or null (owner = the guid on any world of the caller's realm).</summary>
        private ProductRow Own(WorldLink world, CommerceCaller caller, ulong id)
        {
            ProductRow p = Store.GetProduct(id);
            return p != null && p.OwnerGuid == caller.Character && hub.RealmOf(p.WorldId) == world.Realm ? p : null;
        }

        /// <summary>ProductRemove 0x32 {u64 product}: the owner takes a product off (or a site off the gallery). No answer.</summary>
        private void Remove(WorldLink world, CommerceCaller caller, byte[] body)
        {
            if (body.Length < 8)
                return;
            ulong id = BitConverter.ToUInt64(body, 0);
            ProductRow product = Own(world, caller, id);
            if (product == null || product.Status == CommerceStore.Removed)
                return;
            product.Status = CommerceStore.Removed;
            Store.SaveProduct(product);
            if (product.ItemType == CommerceStore.ItemClaim)
                hub.Contests.Withdraw(product.WorldId, product.ItemId, caller, sendStatus: false);   // text 576478
            MarketChanged(product);
            log.Info(product.ItemType == CommerceStore.ItemClaim
                ? $"{caller.Name} unpublished build site {product.ItemId} (world {product.WorldId}) from the gallery (product {id} '{product.Name}')."
                : $"{caller.Name} removed Player Studio product {id} '{product.Name}'.");
        }

        /// <summary>
        /// ProductUpdate 0x34 (0.589 0x33, writer FUN_143181020 = 0.695 FUN_14322ba80): u64 product, string name, string description,
        /// u32 price, string tags, u8 +0xa2, u8 +0xa0 enter contest, u8 +0xa3, u8 +0xa4, u8 +0xa1 leave contest, …
        /// (Commerce.UpdateProduct). Contest markers only touch the tags' "contest_..." words and hand the entry's
        /// description to the contests; a plain update edits the listing.
        /// </summary>
        private void Update(WorldLink world, CommerceCaller caller, byte[] body)
        {
            ulong id;
            string name, description, tags;
            uint price;
            bool enterContest = false, leaveContest = false;
            try
            {
                var r = new GamePacketReader(body);
                id = r.ReadULong();
                name = (r.ReadString() ?? "").Trim();
                description = (r.ReadString() ?? "").Trim();
                price = r.ReadUInt();
                tags = (r.ReadString() ?? "").Trim();
                try
                {
                    r.ReadByte();                        // +0xa2
                    enterContest = r.ReadByte() != 0;    // +0xa0: Claims.EnterContest
                    r.ReadByte();                        // +0xa3
                    r.ReadByte();                        // +0xa4
                    leaveContest = r.ReadByte() != 0;    // +0xa1: Claims.RemoveFromContest
                }
                catch (Exception)
                {
                    // A shorter body than the client's writer produces: a plain update.
                }
            }
            catch (Exception e)
            {
                log.Warn($"Unreadable product update from {caller.Name} ({e.Message}): {BitConverter.ToString(body)}");
                return;
            }
            ProductRow product = Own(world, caller, id);
            if (product == null || product.Status == CommerceStore.Removed)
                return;
            if (enterContest || leaveContest)
            {
                // Contest markers only: the listing's name, description and tags stay as they are.
                string contestTag = enterContest && tags.StartsWith(GalleryTags.ContestPrefix, StringComparison.OrdinalIgnoreCase) ? tags : "";
                product.Tags = Clip(GalleryTags.WithContestTags(product.Tags, leaveContest ? null : contestTag), 512);
            }
            else
            {
                if (name.Length > 0)
                    product.Name = Clip(name, 128);
                product.Description = Clip(description, 1024);
                product.Tags = Clip(product.ItemType == CommerceStore.ItemClaim
                    ? GalleryTags.WithContestTags(GalleryTags.CleanTags(tags), GalleryTags.ContestTagsOf(product.Tags)) : tags, 512);
                // Within the design's own minimum (its TemplateValue, from the voxels the zone sent at create;
                // StudioRules.MinimumPrice) and the ceiling, as in-process (Commerce.UpdateProduct).
                if (product.ItemType == CommerceStore.ItemDesign && price >= StudioRules.MinimumPrice(product.Voxels) && price <= MaxPrice)
                    product.Price = price;
            }
            Store.SaveProduct(product);
            if (product.ItemType == CommerceStore.ItemClaim && enterContest)
                hub.Contests.EntryDescription(product.WorldId, product.ItemId, description);
            else if (product.ItemType == CommerceStore.ItemClaim && leaveContest)
                hub.Contests.Withdraw(product.WorldId, product.ItemId, caller, sendStatus: false);
            if (product.ItemType == CommerceStore.ItemClaim)
                hub.SendGalleryStatus(product.WorldId, product.ItemId);
            log.Info($"{caller.Name} updated {(product.ItemType == CommerceStore.ItemClaim ? "gallery listing" : "Player Studio product")} {id} '{product.Name}'"
                + (enterContest ? $" (entering a contest: \"{tags}\")" : leaveContest ? " (leaving its contests)" : "") + ".");
        }

        /// <summary>
        /// The oldest product waiting for review that this player didn't make and hasn't voted on, with the number of such
        /// products (Commerce.SendProductForReview); none = the empty product (id 0), which leaves the client's popup off.
        /// </summary>
        public void OfferReview(WorldLink world, CommerceCaller caller)
        {
            // Live's rules (Commerce.cs "Live's review rules"): the zone already held back players who may not review (the
            // spend rule); here the per-day rule (P1209), no product in the staff queue (P1210), and an anonymous copy
            // (wiki Player Studio: reviewers are not shown who made the item).
            (ProductRow product, int waiting) = ReviewedEnoughToday(world, caller) ? (null, 0)
                : Store.NextForReview(world.Realm, caller.Character, StaffAt);
            hub.SendClient(world, caller, ClientPackets.ReviewProduct(product, "", waiting));
        }

        /// <summary>Rejections that send a waiting product to the staff queue; never reached while the queue is off.</summary>
        private uint StaffAt => hub.Config.PlayerStudioStaffReview ? Math.Max(1u, hub.Config.PlayerStudioRejections) : uint.MaxValue;

        /// <summary>The player reviewed PlayerStudioReviewsPerDay products in the last 24 hours (0 = no limit) [I: rolling day].</summary>
        private bool ReviewedEnoughToday(WorldLink world, CommerceCaller caller)
            => hub.Config.PlayerStudioReviewsPerDay > 0
                && Store.ReviewsSince(world.Realm, caller.Character, DateTime.UtcNow.AddDays(-1)) >= hub.Config.PlayerStudioReviewsPerDay;

        /// <summary>The staff queue for the worlds' admin pages (admin command "psqueue"), every realm.</summary>
        public (bool Ok, string Text) StaffQueue()
        {
            if (!hub.Config.PlayerStudioStaffReview)
                return (true, "");
            uint staffAt = Math.Max(1u, hub.Config.PlayerStudioRejections);
            List<ProductRow> waiting = Store.StaffQueue(staffAt);
            // Why the peers rejected each one: the verdict codes kept with the votes (RejectReasons, L185).
            ILookup<ulong, uint> verdicts = Store.StaffQueueVerdicts(staffAt);
            return (true, Internal.Commerce.StaffQueue.Encode(waiting.Select(p => new StaffQueueEntry(p.Id, p.Name, p.Description, p.Tags,
                p.OwnerName.Length > 0 ? p.OwnerName : "?", p.Price, p.Approvals, p.Rejections, p.CreatedAt,
                RejectReasons.Summary(verdicts[p.Id])))));
        }

        /// <summary>
        /// A peer-review vote (Commerce.SubmitReview): one per player and product, never on your own; at the approvals or
        /// rejections needed the product is listed or rejected and the owner gets the result mail.
        /// </summary>
        private void Review(WorldLink world, CommerceCaller caller, byte[] body)
        {
            ulong productId;
            uint verdict;
            try
            {
                var r = new GamePacketReader(body);
                productId = r.ReadULong();
                verdict = r.ReadUInt();
            }
            catch (Exception)
            {
                hub.SendClient(world, caller, ClientPackets.ReviewDone(false));
                return;
            }
            bool ok = false;
            ProductRow decided = null, toStaff = null;
            ProductRow product = Store.GetProduct(productId);
            bool own = product != null && product.OwnerGuid == caller.Character && hub.RealmOf(product.WorldId) == world.Realm;
            if (product != null && product.Status == CommerceStore.Pending && product.Rejections < StaffAt && !own
                && !ReviewedEnoughToday(world, caller) && !Store.HasReviewed(productId, world.Realm, caller.Character))
            {
                Store.AddReview(productId, caller.World, caller.Character, verdict);
                if (verdict == VerdictApprove)
                    product.Approvals++;
                else
                    product.Rejections++;
                if (product.Approvals >= Math.Max(1u, hub.Config.PlayerStudioApprovals))
                {
                    product.Status = CommerceStore.Listed;
                    product.ListedAt = DateTime.UtcNow;   // the following digest's "newly listed" (FollowService)
                }
                else if (product.Rejections >= Math.Max(1u, hub.Config.PlayerStudioRejections) && !hub.Config.PlayerStudioStaffReview)
                    product.Status = CommerceStore.Rejected;
                else if (product.Rejections >= StaffAt)
                {
                    product.MailPending = true;   // the owner's "CS Review" mail (MailStaffReview)
                    toStaff = product;
                    log.Info($"Player Studio product {product.Id} '{product.Name}' rejected by its peers: waiting for staff (psqueue).");
                }
                if (product.Status != CommerceStore.Pending)
                    product.MailPending = true;
                Store.SaveProduct(product);
                ok = true;
                if (product.Status != CommerceStore.Pending)
                    decided = product;
            }
            hub.SendClient(world, caller, ClientPackets.ReviewDone(ok));
            log.Info($"{caller.Name} (world {caller.World}) reviewed Player Studio product {productId}: verdict {verdict}{(ok ? "" : " (refused)")}.");
            if (decided != null)
                Decided(decided);
            else if (toStaff != null)
                SendResultMail(toStaff);
        }

        /// <summary>A product's review is over: the owner's result mail (owed until a world of the realm sends it), the caches.</summary>
        private void Decided(ProductRow product)
        {
            bool listed = product.Status == CommerceStore.Listed;
            SendResultMail(product);
            MarketChanged(product);
            log.Info($"Player Studio product {product.Id} '{product.Name}' {(listed ? "accepted" : "rejected")}.");
        }

        /// <summary>
        /// The result mail through one linked world of the owner's realm (its own world first); the flag stays set until that
        /// world confirms (<see cref="CommerceResultMailSent"/>), and <see cref="SendPendingMail"/> retries at every link.
        /// </summary>
        private void SendResultMail(ProductRow product)
        {
            string realm = hub.RealmOf(product.WorldId);
            WorldLink target = hub.LinkOf(product.WorldId) ?? hub.LinksOfRealm(realm).FirstOrDefault();
            if (target == null)
            {
                log.Info($"Result mail for product {product.Id} waits: no world of realm {realm} linked.");
                return;
            }
            hub.SendService(target, new CommerceResultMail
            {
                Product = product.Id, Owner = product.OwnerGuid, ProductName = product.Name,
                Kind = MailKindOf(product)
            });
        }

        /// <summary>A world linked: the result mails still owed to owners of its realm.</summary>
        public void SendPendingMail(WorldLink world)
        {
            foreach (ProductRow product in Store.PendingMail(world.Realm))
                hub.SendService(world, new CommerceResultMail
                {
                    Product = product.Id, Owner = product.OwnerGuid, ProductName = product.Name,
                    Kind = MailKindOf(product)
                });
        }

        /// <summary>
        /// A product was created, decided or removed: designs → CommerceMarketplaceStatus to every world of the realm (the
        /// Designs window's Player Studio category); build sites → CommerceGalleryStatus to the site's world.
        /// </summary>
        public void MarketChanged(ProductRow product)
        {
            bool live = product.Status == CommerceStore.Pending || product.Status == CommerceStore.Listed;
            if (product.ItemType == CommerceStore.ItemClaim)
            {
                hub.SendGalleryStatus(product.WorldId, product.ItemId);
                return;
            }
            var status = new CommerceMarketplaceStatus { Template = product.ItemId, Owner = product.OwnerGuid, OnMarket = live };
            foreach (WorldLink world in hub.LinksOfRealm(hub.RealmOf(product.WorldId)))
                hub.SendService(world, status);
        }

        /// <summary>
        /// An admin decision on a waiting product, in peer review or in the staff queue ("psreview &lt;id&gt; approve|reject",
        /// the worlds' admin page; live: "appgamecommerce product approve|reject").
        /// </summary>
        public (bool Ok, string Text) AdminDecide(string arguments)
        {
            string[] a = (arguments ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (a.Length < 1 || !ulong.TryParse(a[0], out ulong id))
                return (false, "psreview <product> approve|reject");
            ProductRow product = Store.GetProduct(id);
            if (product == null || product.Status != CommerceStore.Pending)
                return (false, $"product {id} is not waiting for review");
            product.Status = a.Length > 1 && a[1] == "approve" ? CommerceStore.Listed : CommerceStore.Rejected;
            if (product.Status == CommerceStore.Listed)
                product.ListedAt = DateTime.UtcNow;   // the following digest's "newly listed" (FollowService)
            product.MailPending = true;
            Store.SaveProduct(product);
            Decided(product);
            return (true, $"product {id} {(product.Status == CommerceStore.Listed ? "approved" : "rejected")}");
        }

        /// <summary>The designs peer review accepted, newest first (the Showcase's Player Studio page; Commerce.ListedDesigns).</summary>
        public List<StudioListEntry> ListedDesigns()
            => Store.ListedDesigns().Select(p => new StudioListEntry(p.Id, p.Name, p.Description, p.Tags, p.OwnerName.Length > 0 ? p.OwnerName : "?",
                p.Voxels, p.ItemId, p.WorldId, hub.RealmOf(p.WorldId), p.Price)).ToList();

        /// <summary>
        /// A listed design the Showcase's Player Studio page took (Commerce.TakeDesign, the service's half): listed, not the
        /// taker's own, not taken before by that player; the reply carries the design's snapshot and where it came from.
        /// </summary>
        public CommerceGrantDesignReply Grant(WorldLink world, CommerceGrantDesignRequest request)
        {
            CommerceCaller caller = request.Caller;
            ProductRow product = Store.GetProduct(request.Product);
            CommerceGrantDesignReply Refuse(byte result, string message)
                => new() { RequestId = request.RequestId, Result = result, Message = message, Product = request.Product };
            if (product == null || product.Status != CommerceStore.Listed || product.ItemType != CommerceStore.ItemDesign)
                return Refuse(CommerceMessages.GrantNotListed, "That design is not on Player Studio.");
            string sourceRealm = hub.RealmOf(product.WorldId);
            if (product.OwnerGuid == caller.Character && sourceRealm == world.Realm)
                return Refuse(CommerceMessages.GrantOwnDesign, "That is your own design.");
            if (request.Product > uint.MaxValue || Store.HasTaken(request.Product, world.Realm, caller.Character))
                return Refuse(CommerceMessages.GrantAlreadyHave, "You already have this design.");
            TemplateSnapshot snapshot = Store.GetSnapshot(product.Id);
            if (snapshot == null && sourceRealm != world.Realm)
                return Refuse(CommerceMessages.GrantGone, "That design is no longer available.");
            return new CommerceGrantDesignReply
            {
                RequestId = request.RequestId, Result = CommerceMessages.GrantOk, Product = product.Id, Maker = product.OwnerName,
                MakerWorld = product.WorldId, MakerGuid = product.OwnerGuid, SourceTemplate = product.ItemId, SourceRealm = sourceRealm,
                Snapshot = snapshot, Price = product.Price
            };
        }

        /// <summary>
        /// The zone made the copy (after taking the buyer's Lumens, Commerce.StudioPrice): recorded; the creator is owed
        /// their share of what was paid (CommerceServer.json PlayerStudioCreatorSharePercent, live's 60/40 split with the
        /// creator's 40 [W]; a studio_payout row, mailed through a world of the creator's realm, <see cref="SendPayout"/>);
        /// and the maker hears about it on whichever world of their realm they play.
        /// </summary>
        public void Taken(CommerceDesignTaken taken)
        {
            CommerceCaller caller = taken.Caller;
            Store.AddTaken(taken.Product, caller.World, caller.Character, taken.Copy);
            ProductRow product = Store.GetProduct(taken.Product);
            log.Info($"{caller.Name} (world {caller.World}) took Player Studio design {taken.Product} '{taken.DesignName}' by {product?.OwnerName} "
                + $"(copy {taken.Copy}, {(taken.Paid > 0 ? $"{taken.Paid} Lumens" : "free")}).");
            if (product == null)
                return;
            string listing = string.IsNullOrEmpty(product.Name) ? taken.DesignName : product.Name;
            uint share = (uint)((ulong)taken.Paid * Math.Min(hub.Config.PlayerStudioCreatorSharePercent, 100u) / 100);
            if (share > 0)
            {
                var payout = new PayoutRow
                {
                    Product = product.Id, TakerWorld = caller.World, TakerGuid = caller.Character, Buyer = Clip(caller.Name ?? "", 64),
                    CreatorWorld = product.WorldId, CreatorGuid = product.OwnerGuid, DesignName = Clip(listing ?? "", 128), Paid = taken.Paid,
                    Share = share
                };
                payout.Id = Store.AddPayout(payout);
                SendPayout(payout);
            }
            string text = taken.Paid == 0 ? $"{caller.Name} took your design \"{listing}\" from Player Studio."
                : $"{caller.Name} bought your design \"{listing}\" from Player Studio for {taken.Paid:N0} Lumens"
                  + (share > 0 ? $"; your share of {share:N0} Lumens is in your mail." : ".");
            var notice = new CommerceNotice { Character = product.OwnerGuid, Text = text };
            foreach (WorldLink world in hub.LinksOfRealm(hub.RealmOf(product.WorldId)))
                hub.SendService(world, notice);
        }

        /// <summary>
        /// A creator's share through one linked world of their realm (their own world first), as the result mails; the row
        /// stays pending until that world confirms (CommercePayoutSent), and <see cref="SendPendingPayouts"/> retries at
        /// every link.
        /// </summary>
        private void SendPayout(PayoutRow payout)
        {
            string realm = hub.RealmOf(payout.CreatorWorld);
            WorldLink target = hub.LinkOf(payout.CreatorWorld) ?? hub.LinksOfRealm(realm).FirstOrDefault();
            if (target == null)
            {
                log.Info($"Player Studio share {payout.Id} ({payout.Share} Lumens for creator {payout.CreatorGuid}) waits: no world of realm {realm} linked.");
                return;
            }
            payoutsInFlight[payout.Id] = (target, DateTime.UtcNow);
            hub.SendService(target, PayoutMessage(payout));
        }

        /// <summary>How long a share handed to a world waits for its confirmation before another world of the realm may take it.</summary>
        private static readonly TimeSpan PayoutInFlight = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Shares handed to a world and not confirmed yet: payout → (world, when). As FollowService's digest mails: every
        /// world of a realm links again after a service restart, and without this each one was given every pending share
        /// and mailed it — real Lumens twice (review 2026-10-18 #1). The world side also claims the payout id before
        /// mailing (RemoteCommerceService.DeliverPayout), which covers a confirmation lost with the link.
        /// </summary>
        private readonly Dictionary<ulong, (WorldLink World, DateTime At)> payoutsInFlight = new();

        /// <summary>
        /// A world linked: the creators' shares still owed to its realm, except those another world of the realm was given
        /// lately and still holds on an open link.
        /// </summary>
        public void SendPendingPayouts(WorldLink world)
        {
            DateTime now = DateTime.UtcNow;
            foreach (PayoutRow payout in Store.PendingPayouts(world.Realm))
            {
                if (payoutsInFlight.TryGetValue(payout.Id, out var given) && given.World != world && given.World.Link.IsOpen
                    && now - given.At < PayoutInFlight)
                    continue;
                payoutsInFlight[payout.Id] = (world, now);
                hub.SendService(world, PayoutMessage(payout));
            }
        }

        /// <summary>CommercePayoutSent: a world mailed the share (or found it already mailed).</summary>
        public void PayoutSent(ulong payout)
        {
            payoutsInFlight.Remove(payout);
            Store.PayoutSent(payout);
        }

        private static CommercePayout PayoutMessage(PayoutRow p) => new()
        {
            Payout = p.Id, Creator = p.CreatorGuid, DesignName = p.DesignName, Buyer = p.Buyer, Paid = p.Paid, Share = p.Share
        };
    }
}
