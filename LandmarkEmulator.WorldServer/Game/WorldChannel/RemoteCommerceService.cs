using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Internal;
using LandmarkEmulator.Internal.Commerce;
using LandmarkEmulator.Internal.Social;
using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldServer.Game.WorldChannel
{
    /// <summary>
    /// The shared commerce service as a remote container (LandmarkEmulator.CommerceServer; world-channel-service.md §8 step
    /// 8), chosen by COMMERCE_ADDRESS (<see cref="WorldChannelServices.Configure"/>). The zone dials the service
    /// (<see cref="InternalDialer"/>, reconnecting by itself) and carries both directions on that one link, as it does for
    /// the social service (messages: LandmarkEmulator.Internal/Commerce/CommerceMessages.cs):
    /// <list type="bullet">
    /// <item>Zone → commerce: <see cref="CommerceHello"/> first on every (re)connect (world, realm = this world's character
    /// database, which Contests / Portraits files it still has); the client's commerce requests handed on with the player's
    /// connection id, guid, account, world, build and name (<see cref="CommerceClientRequest"/>: InGamePurchase 0x45,
    /// RequestContests 0xA7/0x1D, the "Contests" vote 0xA7/8, PortraitDataRequest 0x98/2, AddClientPortraitCrc 0x75);
    /// what needs the zone's data first, checked here then handed on: ProductCreate (<see cref="Commerce.CheckProduct"/>
    /// → <see cref="CommerceProductCreate"/> with the design's snapshot), SubmitContestEntry (site owner, the entry
    /// limiter → <see cref="CommerceContestEntrySubmit"/>); the Showcase pages' requests (listed designs, take a design,
    /// portrait upload, the Following tab: <see cref="CommerceFollowRequest"/>) and the dev tools (psreview, contestphase,
    /// contests, followdigest).</item>
    /// <item>Commerce → zone: <see cref="TunnelToExternal"/> client packets (the 0.695 client's numbering), queued to the session with that connection id; the caches the zone itself reads —
    /// <see cref="CommerceMarketSnapshot"/> (after every hello), <see cref="CommerceMarketplaceStatus"/> (+ the owner's
    /// Designs window update), <see cref="CommerceGalleryStatus"/>; result mails, creators' shares and the daily following
    /// digest mails (<see cref="CommerceDigestMail"/>; all sent through the zone's social seam and confirmed), notices (chat
    /// lines), the replies to the requests above, and the one-time import requests.</item>
    /// </list>
    /// <para>
    /// While the service is unreachable (spec §7; the client has no "store unavailable" text): a ProductCreate is answered
    /// 0x30 with status 0, the client's own "CommerceCreateProductUnknownError" (FUN_14319fc60 default case; same in 0.695
    /// FUN_14324ab40), nothing stored; ServerStatusRequest is answered 0x0E false (Lua OnReceiveServerStatus(false));
    /// a contest entry is answered ContestEntryResults false (the client's ContestSubmission.Fail) [I: an addition to §7's
    /// list, so the Contest window is not left waiting]; every other request stays unanswered (the client asks again);
    /// nothing is taken from anyone. The caches keep their last state, so designs stay in their Designs category and listed
    /// sites stay likeable; the reconnect's snapshot replaces them.
    /// </para>
    /// <para>
    /// The Showcase web pages keep running in this world (spec §6.6, the option kept open there): they act on the player's
    /// zone (bag, travel, vendors, admin page). Their commerce data come from the service: the gallery's listings and
    /// contest names from the caches, Player Studio's list and "Get" by request.
    /// </para>
    /// </summary>
    public sealed class RemoteCommerceService : ICommerceService, IDisposable
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>How long a request waits for the service's answer (the web pages wait 10 s for the world thread too).</summary>
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

        private readonly InternalDialer dialer;
        private readonly string address;
        private readonly uint worldId;
        /// <summary>This world's realm: its character database (CommerceHello; CommerceMessages summary).</summary>
        private readonly string realm;

        private readonly object sync = new();
        private InternalLink link;
        /// <summary>Players by connection id (the id the service addresses them by) and back.</summary>
        private readonly Dictionary<ulong, WorldSession> connections = new();
        private readonly Dictionary<WorldSession, ulong> connectionOf = new();
        /// <summary>Connection ids (ours): unique per process; with the start time in the high bits not reused by a restart.</summary>
        private static long nextConnection = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() << 16;

        // ---- the caches the zone reads (spec §6.3) ---------------------------------------------------------------------
        private readonly object cacheSync = new();
        private HashSet<ulong> marketDesigns = new();
        private Dictionary<ulong, Commerce.Listing> listings = new();
        private Dictionary<ulong, List<string>> contestNames = new();

        /// <summary>Work from the link that touches game state: run on the world thread (<see cref="UpdateWebPages"/>).</summary>
        private readonly ConcurrentQueue<Action> worldQueue = new();

        /// <summary>A request waiting for its answer: run on the world thread with the answer, or with null at the timeout / link loss.</summary>
        private sealed record Pending(DateTime Deadline, Action<ISocialMessage> Done);
        private readonly Dictionary<uint, Pending> pending = new();
        private int nextRequest;

        public RemoteCommerceService(string address, uint worldId)
        {
            this.address = address;
            this.worldId = worldId;
            realm = RealmName();
            dialer = new InternalDialer(address);
            dialer.Connected += OnConnected;
        }

        /// <summary>
        /// The realm: the database named in Database:Character (lmemu_character in the Docker layout, shared by every
        /// world), so the service matches a character's own products, reviews and portraits on every world sharing it.
        /// </summary>
        private static string RealmName()
        {
            string connection = ConfigurationManager<WorldServerConfiguration>.Instance.Config?.Database?.Character?.ConnectionString ?? "";
            Match m = Regex.Match(connection, @"(?:^|;)\s*(?:database|initial catalog)\s*=\s*([^;]+)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        public void Start()
        {
            Task.Run(DigestLoop);
            dialer.Start();
            log.Info($"Commerce: remote at {address} (Player Studio, wallet / account answers, reviews, contests, portraits; realm \"{realm}\").");
        }

        public bool IsConnected
        {
            get
            {
                lock (sync)
                    return link is { IsOpen: true };
            }
        }

        public string LinkState => $"commerce service at {address}, {(IsConnected ? "connected" : "not connected")}";

        public void Dispose()
        {
            digestQueue.Writer.TryComplete();
            dialer.Dispose();
        }

        // ---- the link -------------------------------------------------------------------------------------------------

        private void OnConnected(InternalLink newLink)
        {
            newLink.Received += OnReceived;
            newLink.Closed += (l, error) =>
            {
                lock (sync)
                    if (link == l)
                        link = null;
                // Requests in flight get no answer now: they finish as unreachable on the world thread.
                worldQueue.Enqueue(FailAllPending);
                worldQueue.Enqueue(() => ExpireCreates(linkLost: true));
                log.Warn($"Commerce service link lost{(error != null ? ": " + error.Message : "")}; reconnecting.");
            };
            newLink.Send(new CommerceHello { WorldId = worldId, Realm = realm, WorldNameText = WorldNameText, LocalData = LocalData() }.ToPacket());
            lock (sync)
                link = newLink;
            log.Info("Commerce service connected.");
        }

        private static uint WorldNameText
            => ConfigurationManager<WorldServerConfiguration>.Instance.Config?.WorldNameId is uint id and not 0 ? id : Landscapes.WorldNameText;

        private static string ContestFolder => Path.Combine(AppContext.BaseDirectory, "Contests");
        private static string PortraitFolder => Path.Combine(AppContext.BaseDirectory, "Portraits");

        /// <summary>Which files this world kept before the service (bit 1 Contests/, bit 2 Portraits/), for the one-time import.</summary>
        private static uint LocalData()
        {
            uint data = 0;
            if (File.Exists(Path.Combine(ContestFolder, "contests.json")) || File.Exists(Path.Combine(ContestFolder, "entries.json")))
                data |= CommerceMessages.ImportContests;
            if (Directory.Exists(PortraitFolder) && Directory.EnumerateFiles(PortraitFolder, "*.png").Any())
                data |= CommerceMessages.ImportPortraits;
            return data;
        }

        /// <summary>Sends to the service; false (nothing sent) while it is unreachable.</summary>
        private bool Send(ISocialMessage message)
        {
            lock (sync)
                return link != null && link.Send(message.ToPacket());
        }

        /// <summary>The caller block for a player (a connection id is given at the first request).</summary>
        private CommerceCaller Caller(WorldSession session)
        {
            ulong id;
            lock (sync)
            {
                if (!connectionOf.TryGetValue(session, out id))
                {
                    id = (ulong)Interlocked.Increment(ref nextConnection);
                    connectionOf[session] = id;
                    connections[id] = session;
                }
            }
            return new CommerceCaller(id, session.PlayerGuid, session.Account?.Id ?? 0, worldId, session.ClientBuild ?? "", session.CharacterName ?? "");
        }

        private bool SendRequest(WorldSession session, ushort family, uint sub, byte[] body)
            => Send(new CommerceClientRequest { Family = family, Sub = sub, Caller = Caller(session), Body = body ?? Array.Empty<byte>() });

        /// <summary>Runs on the link's reading task: queueing to sessions and the caches are thread-safe.</summary>
        private void OnReceived(InternalLink from, IInternalMessage message)
        {
            switch (message)
            {
                case TunnelToExternal one:
                    Deliver(one.Destination.ConnectionId, one.Packet);
                    break;
                case TunnelToExternalList many:
                    foreach (ExternalDestination destination in many.Destinations)
                        Deliver(destination.ConnectionId, many.Packet);
                    break;
                case ServicePacket status when ServiceStatuses.TryTake(status):
                    break;
                case ServicePacket packet:
                    OnServicePacket(packet);
                    break;
            }
        }

        /// <summary>A client packet (opcode bytes + body, the 0.695 client's numbering) for one of this world's players.</summary>
        private void Deliver(ulong connectionId, byte[] packet)
        {
            WorldSession session;
            lock (sync)
                connections.TryGetValue(connectionId, out session);
            if (session == null || session.Disconnected)
            {
                log.Debug($"Commerce packet for connection {connectionId:X}, which is gone: dropped.");
                return;
            }
            (ZoneMessageOpcode? opcode, int length) = WorldSession.GetOpcode(packet);
            if (opcode is ZoneMessageOpcode op)
                session.EnqueueMessage(op, packet[length..]);
            else
            {
                log.Warn($"Commerce packet with an unknown opcode for {session.CharacterName}: sent as is.");
                session.SendRaw(packet);
            }
        }

        private void OnServicePacket(ServicePacket packet)
        {
            ISocialMessage message;
            try
            {
                message = CommerceMessages.Parse(packet);
            }
            catch (InvalidDataException e)
            {
                log.Warn($"Unreadable service packet 0x{packet.Family:X}/0x{packet.Sub:X} from the commerce service: {e.Message}");
                return;
            }
            switch (message)
            {
                case CommerceMarketSnapshot snapshot:
                    Snapshot(snapshot);
                    break;
                case CommerceMarketplaceStatus status:
                    lock (cacheSync)
                    {
                        if (status.OnMarket)
                            marketDesigns.Add(status.Template);
                        else
                            marketDesigns.Remove(status.Template);
                    }
                    // The owner's Designs window files the design under Player Studio or back (Commerce.MarketChanged).
                    worldQueue.Enqueue(() =>
                    {
                        if (PlayerManager.FindByGuid(status.Owner) is WorldSession owner)
                            Templates.SendUpdated(owner, status.Template);
                    });
                    break;
                case CommerceGalleryStatus gallery:
                    lock (cacheSync)
                        Apply(gallery.Listing, gallery.Listed);
                    break;
                case CommerceNotice notice:
                    worldQueue.Enqueue(() =>
                    {
                        if (PlayerManager.FindByGuid(notice.Character) is WorldSession player)
                            Chat.System(player, notice.Text);
                    });
                    break;
                case CommerceResultMail mail:
                    // Commerce.Decide's mail, through this world's social seam (the social service when SOCIAL_ADDRESS is set).
                    worldQueue.Enqueue(() =>
                    {
                        Social.SocialServices.Service.SendSystemMail(mail.Owner, "Player Studio", mail.ProductName, "", 0, null, mail.Kind);
                        Send(new CommerceResultMailSent { Product = mail.Product });
                        log.Info($"Player Studio result mail (kind 0x{mail.Kind:X}) for product {mail.Product} '{mail.ProductName}' sent to character {mail.Owner}.");
                    });
                    break;
                case CommercePayout payout:
                    // A creator's share of a Player Studio sale (ProductService.Taken), through this world's social seam.
                    // Off the link's reading task: the de-dup claim is a database write (DeliverPayout).
                    Task.Run(() => DeliverPayout(payout));
                    break;
                case CommerceDigestMail digest:
                    // A follower's daily Showcase following mail (FollowService), as CommercePayout: mailed from here through
                    // the social seam, then confirmed. The name lookup is a database read, so off the link's reading task:
                    // queued to the one digest consumer, which looks the names up in batches (DigestLoop).
                    digestQueue.Writer.TryWrite(digest);
                    break;
                case CommerceProductCreateResult result:
                    // A create's typed result (by create id): settles its listing fee on the world thread (review #4).
                    worldQueue.Enqueue(() => SettleCreate(result));
                    break;
                case CommerceFollowReply reply:
                    Answer(reply.RequestId, reply);
                    break;
                case CommerceImportRequest import:
                    Task.Run(() => SendImport(import.What));
                    break;
                case CommerceGrantDesignReply reply:
                    Answer(reply.RequestId, reply);
                    break;
                case CommerceStudioListReply reply:
                    Answer(reply.RequestId, reply);
                    break;
                case CommerceAdminReply reply:
                    Answer(reply.RequestId, reply);
                    break;
                case CommercePortraitUploadReply reply:
                    Answer(reply.RequestId, reply);
                    break;
                default:
                    log.Debug($"Commerce service sent 0x{packet.Family:X}/0x{packet.Sub:X}: not used by the zone.");
                    break;
            }
        }

        /// <summary>Puts one gallery listing into the caches (call under cacheSync).</summary>
        private void Apply(GalleryListing listing, bool listed)
        {
            if (listed)
                listings[listing.Claim] = new Commerce.Listing(listing.Claim, DateTimeOffset.FromUnixTimeSeconds(listing.ListedAt).UtcDateTime, listing.ContestTags ?? "");
            else
                listings.Remove(listing.Claim);
            if (listing.ContestNames.Count > 0)
                contestNames[listing.Claim] = listing.ContestNames.ToList();
            else
                contestNames.Remove(listing.Claim);
        }

        /// <summary>
        /// The whole market for this world (after every hello): the caches are replaced; designs whose state changed while
        /// the link was down are re-sent to their owners who are online here.
        /// </summary>
        private void Snapshot(CommerceMarketSnapshot snapshot)
        {
            HashSet<ulong> changed;
            lock (cacheSync)
            {
                var designs = snapshot.Designs.ToHashSet();
                changed = designs.Where(d => !marketDesigns.Contains(d)).Concat(marketDesigns.Where(d => !designs.Contains(d))).ToHashSet();
                marketDesigns = designs;
                listings = new Dictionary<ulong, Commerce.Listing>();
                contestNames = new Dictionary<ulong, List<string>>();
                foreach (GalleryListing listing in snapshot.Listings)
                    Apply(listing, true);
                foreach (GalleryListing site in snapshot.Unlisted)
                    Apply(site, false);
            }
            log.Info($"Commerce snapshot: {snapshot.Designs.Count} design(s) on the market, {snapshot.Listings.Count} listed site(s); {changed.Count} design(s) changed.");
            if (changed.Count > 0)
                worldQueue.Enqueue(() =>
                {
                    foreach (WorldSession player in PlayerManager.Snapshot())
                        foreach (ulong design in changed)
                            Templates.SendUpdated(player, design);   // only the design's owner has it: others are skipped
                });
        }

        // ---- requests with an answer ---------------------------------------------------------------------------------

        /// <summary>Registers a request and sends it; false (nothing registered) while the service is unreachable.</summary>
        private bool Ask(Func<uint, ISocialMessage> build, Action<ISocialMessage> done)
        {
            uint id = (uint)Interlocked.Increment(ref nextRequest);
            lock (sync)
                pending[id] = new Pending(DateTime.UtcNow + RequestTimeout, done);
            if (Send(build(id)))
                return true;
            lock (sync)
                pending.Remove(id);
            return false;
        }

        /// <summary>An answer arrived: its completion runs on the world thread.</summary>
        private void Answer(uint requestId, ISocialMessage reply)
        {
            Pending waiting;
            lock (sync)
                pending.Remove(requestId, out waiting);
            if (waiting != null)
                worldQueue.Enqueue(() => waiting.Done(reply));
        }

        /// <summary>World thread: the requests whose answer is overdue finish with none.</summary>
        private void ExpirePending()
        {
            List<Pending> overdue;
            lock (sync)
            {
                if (pending.Count == 0)
                    return;
                DateTime now = DateTime.UtcNow;
                var ids = pending.Where(p => p.Value.Deadline <= now).Select(p => p.Key).ToList();
                overdue = ids.Select(id => pending[id]).ToList();
                foreach (uint id in ids)
                    pending.Remove(id);
            }
            foreach (Pending p in overdue)
                p.Done(null);
        }

        private void FailAllPending()
        {
            List<Pending> all;
            lock (sync)
            {
                all = pending.Values.ToList();
                pending.Clear();
            }
            foreach (Pending p in all)
                p.Done(null);
        }

        /// <summary>A task completed on the world thread, its continuations on the thread pool (web requests await it).</summary>
        private static TaskCompletionSource<T> NewCompletion<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        // ---- ICommerceService: InGamePurchase ------------------------------------------------------------------------

        public void InGamePurchaseRequest(WorldSession session, ushort sub, byte[] body)
        {
            if (sub == Commerce.SubProductCreate)
            {
                CreateProduct(session, body);
                return;
            }
            if (sub == Commerce.SubWalletInfoRequest)
            {
                // The wallet carries this world's Lumens while the listing fee is charged (Commerce.SendWalletInfo), so the
                // zone answers it (until 2026-10-17 the service sent its always-empty wallet).
                Commerce.SendWalletInfo(session);
                return;
            }
            if (sub == Commerce.SubGetProductForReview || sub == Commerce.SubSubmitProductReview)
            {
                // The reviewer rule (live: accounts that bought 1000 SC of Marketplace items) needs this world's records of
                // what the account spent, so the zone answers a player who may not review itself (Commerce.RefuseReview).
                // The rule may read the account's other characters from the database: off the tick, the answer back on
                // the world thread (Commerce.MayReview; review 2026-10-18b #3).
                Commerce.MayReview(session, mayReview =>
                {
                    if (!mayReview)
                        Commerce.RefuseReview(session, sub);
                    else if (!SendRequest(session, CommerceMessages.CommerceFamily, sub, body))
                        log.Debug($"Commerce request 0x{sub:X2} from {session.CharacterName} unanswered: the commerce service is unreachable.");
                });
                return;
            }
            if (SendRequest(session, CommerceMessages.CommerceFamily, sub, body))
                return;
            if (sub == ServerStatusRequest)
            {
                // ServerStatusResponse 0x0E {bool false} (FUN_1431ab880 → Lua OnReceiveServerStatus(false); spec §7).
                session.EnqueueMessage(ZoneMessageOpcode.InGamePurchaseBase, new byte[] { 0x0E, 0x00, 0x00 });
                return;
            }
            log.Debug($"Commerce request 0x{sub:X2} from {session.CharacterName} unanswered: the commerce service is unreachable.");
        }

        private const ushort ServerStatusRequest = 0x0D;

        /// <summary>
        /// ProductCreate (and 0.695's TemplateProductCreate, mapped onto it by Commerce.FromWire): the zone's checks
        /// (<see cref="Commerce.CheckProduct"/>: refusals answered here exactly as in-process), then the product with the
        /// design's snapshot to the service, which checks for a duplicate, stores it and answers 0x30.
        /// </summary>
        private void CreateProduct(WorldSession session, byte[] body)
        {
            Commerce.ProductDraft draft = Commerce.CheckProduct(session, body, out uint refusal);
            if (draft == null)
            {
                Commerce.SendCreateResponse(session, refusal, "");
                return;
            }
            TemplateSnapshot snapshot = null;
            uint voxels = 0;
            if (!draft.Gallery)
            {
                TemplateModel design;
                using (var context = DatabaseManager.Instance.CharacterDatabase.NewContext())
                    design = context.Template.FirstOrDefault(t => t.Id == draft.ItemId);
                if (design != null)
                {
                    snapshot = new TemplateSnapshot(design.Name ?? "", design.Tags ?? "", string.IsNullOrEmpty(design.AuthorName) ? session.CharacterName ?? "" : design.AuthorName,
                        design.SizeX, design.SizeY, design.SizeZ, design.Materials ?? "", design.Blocks ?? Array.Empty<byte>());
                    voxels = (uint)Math.Min(uint.MaxValue, Templates.MaterialCount(design.Materials));
                }
            }
            // The listing fee (designs only) is this world's Lumens: taken here before the product goes to the service, and
            // given back when the service's typed result for this create says it was not stored, or it can't be reached.
            uint fee = draft.Gallery ? 0 : Commerce.ListingFee(draft.Price);
            if (!Commerce.ChargeListingFee(session, fee))
            {
                Commerce.SendCreateResponse(session, Commerce.CouldNotAffordListingFee, "");
                log.Info($"{session.CharacterName} could not pay the {fee}-Lumen listing fee for design {draft.ItemId} ({session.Coins} Lumens).");
                return;
            }
            CommerceCaller caller = Caller(session);
            // Only a create with a fee waits for its result (a gallery listing costs nothing: CreateId 0, no typed result).
            ulong createId = fee > 0 ? (ulong)Interlocked.Increment(ref nextCreate) : 0;
            PendingCreate waiting = null;
            if (createId != 0)
            {
                waiting = new PendingCreate(createId, session, caller, fee, draft.ItemType, draft.ItemId) { Deadline = DateTime.UtcNow + CreateTimeout };
                lock (sync)
                    pendingCreates[createId] = waiting;
            }
            bool sent = Send(new CommerceProductCreate
            {
                Caller = caller, ItemType = draft.ItemType, ItemId = draft.ItemId, Price = draft.Price, Name = draft.Name,
                Description = draft.Description, Tags = draft.Gallery ? Gallery.CleanTags(draft.Tags) : draft.Tags, Headline = draft.Headline,
                Voxels = voxels, Snapshot = snapshot, CreateId = createId
            });
            if (!sent)
            {
                if (waiting != null)
                    lock (sync)
                        pendingCreates.Remove(createId);
                Commerce.RefundListingFee(session, fee, "the commerce service is unreachable");
                // Spec §7: status 0 = the client's "CommerceCreateProductUnknownError" (FUN_14319fc60 default); nothing stored.
                Commerce.SendCreateResponse(session, 0, "");
                log.Info($"{session.CharacterName}'s Player Studio product could not be created: the commerce service is unreachable.");
            }
        }

        /// <summary>How long a create waits for its result (or a check's answer) before the service is asked (again).</summary>
        private static readonly TimeSpan CreateTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Checks (<see cref="CommerceProductCreateCheck"/>) a create may go unanswered on an open link before its fee is given
        /// back anyway: a commerce service built before 2026-10-18 answers neither the create id nor the check, and then
        /// this is the old 30 s timeout refund, only later.
        /// </summary>
        private const int MaxCreateChecks = 3;

        /// <summary>Create ids (ours): unique per process; with the start time in the high bits not reused by a restart.</summary>
        private static long nextCreate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() << 16;

        /// <summary>A ProductCreate handed to the service with a listing fee taken for it, waiting for its result.</summary>
        private sealed record PendingCreate(ulong Id, WorldSession Session, CommerceCaller Caller, uint Fee, uint ItemType, ulong Item)
        {
            /// <summary>When the service is asked (again) for this create's result.</summary>
            public DateTime Deadline { get; set; }
            /// <summary>Checks sent on an open link without an answer.</summary>
            public int Checks { get; set; }
        }

        /// <summary>
        /// Creates with a fee waiting for their result, by create id. Until 2026-10-18 they were queued per player and settled
        /// by the next tunnelled 0x31 (FIFO), refunded after 30 s or at link loss and kept when the player had left: a late or
        /// lost answer listed a design for free, and a refusal for a player who had left kept the fee (review #4). Now the
        /// service's <see cref="CommerceProductCreateResult"/> settles a create by its id; a missing one is asked for
        /// (<see cref="CommerceProductCreateCheck"/>) instead of refunded blindly.
        /// </summary>
        private readonly Dictionary<ulong, PendingCreate> pendingCreates = new();

        /// <summary>
        /// World thread: a create's result. Status 1 (stored) keeps the fee; anything else (0xF duplicate, 0xC, 0 …, or a
        /// check's "nothing stored") gives it back. An unknown id was settled already.
        /// </summary>
        private void SettleCreate(CommerceProductCreateResult result)
        {
            PendingCreate p;
            lock (sync)
                pendingCreates.Remove(result.CreateId, out p);
            if (p == null)
            {
                log.Debug($"Commerce create result {result.CreateId:X} (status 0x{result.Status:X}): no create waiting (settled already).");
                return;
            }
            if (result.Status == 1)
            {
                log.Info($"{p.Caller.Name}'s design {p.Item} stored as Player Studio product {result.Product}; listing fee of {p.Fee} Lumens kept.");
                return;
            }
            RefundCreate(p, $"the commerce service did not store design {p.Item} (status 0x{result.Status:X})");
        }

        /// <summary>
        /// Gives a create's fee back: to the bag while the player is here, else by mail (Lumens attached, from Player Studio),
        /// as a disconnected session's coins would not be saved any more.
        /// </summary>
        private static void RefundCreate(PendingCreate p, string why)
        {
            if (!p.Session.Disconnected)
            {
                Commerce.RefundListingFee(p.Session, p.Fee, why);
                return;
            }
            Social.SocialServices.Service.SendSystemMail(p.Caller.Character, "Player Studio", "Listing fee returned",
                $"Your design could not be submitted to Player Studio. Your listing fee of {p.Fee:N0} Lumens is attached.", p.Fee);
            log.Info($"{p.Caller.Name}'s listing fee of {p.Fee} Lumens mailed back (they had left): {why}.");
        }

        /// <summary>
        /// World thread, every tick: creates whose result is overdue are asked for (<see cref="CommerceProductCreateCheck"/>)
        /// while the link is up — the fee goes back after <see cref="MaxCreateChecks"/> unanswered checks — and wait while it
        /// is down. <paramref name="linkLost"/>: the link just closed; every waiting create is asked for at the next link
        /// (<see cref="OnConnected"/>) rather than refunded, since the service may have stored it.
        /// </summary>
        private void ExpireCreates(bool linkLost = false)
        {
            var check = new List<PendingCreate>();
            var refund = new List<PendingCreate>();
            bool connected = IsConnected;
            lock (sync)
            {
                if (pendingCreates.Count == 0)
                    return;
                DateTime now = DateTime.UtcNow;
                foreach (PendingCreate p in pendingCreates.Values)
                {
                    if (linkLost)
                    {
                        // The answers from the old link are gone; asked again once linked (the counter starts over).
                        p.Deadline = now;
                        p.Checks = 0;
                        continue;
                    }
                    if (!connected || p.Deadline > now)
                        continue;
                    if (p.Checks >= MaxCreateChecks)
                        refund.Add(p);
                    else
                    {
                        p.Checks++;
                        p.Deadline = now + CreateTimeout;
                        check.Add(p);
                    }
                }
                foreach (PendingCreate p in refund)
                    pendingCreates.Remove(p.Id);
            }
            foreach (PendingCreate p in check)
                Send(new CommerceProductCreateCheck { CreateId = p.Id, Caller = p.Caller, ItemType = p.ItemType, ItemId = p.Item });
            foreach (PendingCreate p in refund)
            {
                log.Warn($"{p.Caller.Name}'s listing fee for design {p.Item}: the commerce service answered neither the create nor "
                    + $"{MaxCreateChecks} checks; given back.");
                RefundCreate(p, $"no answer from the commerce service for design {p.Item}");
            }
        }

        public bool IsOnMarketplace(ulong design)
        {
            lock (cacheSync)
                return marketDesigns.Contains(design);
        }

        public bool IsOnGallery(ulong claimId)
        {
            lock (cacheSync)
                return listings.ContainsKey(claimId);
        }

        public IReadOnlyDictionary<ulong, Commerce.Listing> GalleryListings()
        {
            lock (cacheSync)
                return new Dictionary<ulong, Commerce.Listing>(listings);
        }

        public void PlayerStudioRegistered(WorldSession session)
        {
            LocalCommerceService.SendPlayerStudioRegistered(session);
            Caller(session);   // the connection id the service will answer this player by
        }

        public Task<bool> AdminDecide(ulong productId, bool approve)
        {
            var done = NewCompletion<bool>();
            if (!Ask(id => new CommerceAdminRequest { RequestId = id, Command = "psreview", Arguments = $"{productId} {(approve ? "approve" : "reject")}" },
                reply => done.SetResult(reply is CommerceAdminReply { Ok: true })))
                done.SetResult(false);
            return done.Task;
        }

        /// <summary>The service's staff queue (admin command "psqueue", the reply's text as StaffQueue.Encode wrote it).</summary>
        public Task<List<StaffQueueEntry>> StaffQueue()
        {
            var done = NewCompletion<List<StaffQueueEntry>>();
            if (!Ask(id => new CommerceAdminRequest { RequestId = id, Command = Internal.Commerce.StaffQueue.Command },
                reply => done.SetResult(reply is CommerceAdminReply { Ok: true } r ? Internal.Commerce.StaffQueue.Decode(r.Text) : new List<StaffQueueEntry>())))
                done.SetResult(new List<StaffQueueEntry>());
            return done.Task;
        }

        /// <summary>
        /// Buys a listed Player Studio design (Commerce.TakeDesign, split): the zone's own check (Unlocks kind 16), the
        /// service's (listed, not your own, not taken before) with the design's snapshot and the creator's price, then here
        /// the buyer's Lumens (Commerce.StudioPrice; nothing with FreeStore on) and the copy — from this world's character
        /// database when the design is of this realm and still exists (as in-process, screenshots included), else from the
        /// snapshot — and <see cref="CommerceDesignTaken"/> back with the Lumens paid; the service mails the creator their
        /// share (<see cref="CommercePayout"/>).
        /// </summary>
        public Task<(bool Ok, string Message)> TakeDesign(WorldSession session, ulong productId)
        {
            var done = NewCompletion<(bool, string)>();
            if (productId > uint.MaxValue || Unlocks.Has(session, Unlocks.StudioDesign, (uint)productId))
            {
                done.SetResult((false, "You already have this design."));
                return done.Task;
            }
            CommerceCaller caller = Caller(session);
            if (!Ask(id => new CommerceGrantDesignRequest { RequestId = id, Caller = caller, Product = productId },
                reply => done.SetResult(Grant(session, caller, productId, reply as CommerceGrantDesignReply))))
                done.SetResult((false, "Player Studio is not reachable right now. Please try again in a moment."));
            return done.Task;
        }

        /// <summary>World thread: the service's answer to a take → the copy (Commerce.TakeDesign's second half).</summary>
        private (bool, string) Grant(WorldSession session, CommerceCaller caller, ulong productId, CommerceGrantDesignReply reply)
        {
            if (reply == null)
                return (false, "Player Studio is not reachable right now. Please try again in a moment.");
            if (reply.Result != CommerceMessages.GrantOk)
                return (false, reply.Message);
            if (session.Disconnected)
                return (false, "You left the world.");
            if (Unlocks.Has(session, Unlocks.StudioDesign, (uint)productId))
                return (false, "You already have this design.");
            TemplateModel source = null;
            using (var context = DatabaseManager.Instance.CharacterDatabase.NewContext())
                if (reply.SourceRealm == realm)
                    source = context.Template.FirstOrDefault(t => t.Id == reply.SourceTemplate);
            TemplateSnapshot snap = reply.Snapshot;
            if (source == null && snap == null)
                return (false, "That design is no longer available.");
            var copy = source != null
                ? new TemplateModel
                {
                    OwnerGuid = session.PlayerGuid, AuthorName = reply.Maker, Name = source.Name, Tags = source.Tags, SizeX = source.SizeX,
                    SizeY = source.SizeY, SizeZ = source.SizeZ, Materials = source.Materials, Blocks = source.Blocks, CreatedAt = DateTime.UtcNow
                }
                : new TemplateModel
                {
                    OwnerGuid = session.PlayerGuid, AuthorName = reply.Maker, Name = snap.Name, Tags = snap.Tags, SizeX = snap.SizeX,
                    SizeY = snap.SizeY, SizeZ = snap.SizeZ, Materials = snap.Materials, Blocks = snap.Blocks, CreatedAt = DateTime.UtcNow
                };
            if (!Templates.HasRoom(session, copy))
                return (false, "Your design storage is full.");
            // The Lumens go just before the copy is saved, on the world thread (as in-process); a failed save gives them back.
            uint cost = Commerce.StudioPrice(reply.Price);
            if (cost > 0 && !session.ChangeCoins(-cost))
                return (false, $"That design costs {cost:N0} Lumens; you have {session.Coins:N0}.");
            try
            {
                using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
                context.Template.Add(copy);
                context.SaveChanges();
            }
            catch (Exception e)
            {
                if (cost > 0)
                    session.ChangeCoins(cost);
                log.Error($"{session.CharacterName}'s copy of Player Studio design {productId} could not be saved ({e.Message}); Lumens returned.");
                return (false, "The design could not be saved. Nothing was charged; please try again.");
            }
            Unlocks.Add(session, Unlocks.StudioDesign, (uint)productId);
            Commerce.RecordSpend(session, cost);   // the reviewer rule (Commerce.MayReview)
            if (source != null)
                Templates.CopyImages(source.Id, copy.Id);
            Templates.SendAdded(session, copy);
            Templates.SendLearned(session, copy.Id, copy.Name);
            Send(new CommerceDesignTaken { Caller = caller, Product = productId, Copy = copy.Id, DesignName = copy.Name ?? "", Paid = cost });
            log.Info($"{session.CharacterName} took Player Studio design {productId} '{copy.Name}' by {reply.Maker} (copy {copy.Id}, "
                + (source != null ? "from this world's character database" : $"from the snapshot of world {reply.MakerWorld}")
                + (cost > 0 ? $", {cost} Lumens)." : ", free)."));
            return (true, cost > 0 ? $"\"{copy.Name}\" is now in your Designs (Collected), for {cost:N0} Lumens."
                : $"\"{copy.Name}\" is now in your Designs (Collected).");
        }

        public Task<List<Commerce.StudioDesign>> ListedDesigns()
        {
            var done = NewCompletion<List<Commerce.StudioDesign>>();
            if (!Ask(id => new CommerceStudioListRequest { RequestId = id }, reply => done.SetResult(
                    (reply as CommerceStudioListReply)?.Designs.Select(d => new Commerce.StudioDesign(d.Product, d.Name, d.Description, d.Tags,
                        d.Author, d.Voxels, d.Realm == realm ? d.Template : 0, d.Price)).ToList() ?? new List<Commerce.StudioDesign>())))
                done.SetResult(new List<Commerce.StudioDesign>());
            return done.Task;
        }

        // ---- Player Studio creator shares ------------------------------------------------------------------------------

        /// <summary>
        /// Pool thread: one creator share (<see cref="CommercePayout"/>), mailed at most once per payout id. The service
        /// delivers at least once — a share can reach two worlds of the realm (both linking after a service restart) or
        /// come again after its CommercePayoutSent was lost with the link — and every duplicate would be real Lumens
        /// (review 2026-10-18 #1). So the payout id is claimed first in the shared character database (Unlocks kind 32,
        /// an atomic INSERT IGNORE): only the world that adds the row mails; a share already claimed is just confirmed
        /// again. A failed claim (database away) is not confirmed, so the service offers it again at the next link.
        /// </summary>
        private void DeliverPayout(CommercePayout payout)
        {
            if (payout.Payout > uint.MaxValue)
                log.Warn($"Player Studio share {payout.Payout}: id above 32 bits, de-dup key folded (value {(uint)payout.Payout}).");
            uint key = (uint)payout.Payout;
            bool claimed;
            try
            {
                claimed = payout.Share == 0 || payout.Creator == 0
                    || DatabaseManager.Instance.CharacterDatabase.TryClaimUnlock(payout.Creator, Unlocks.StudioPayoutMailed, key);
            }
            catch (Exception e)
            {
                log.Warn($"Player Studio share {payout.Payout} for creator 0x{payout.Creator:X} not mailed now: {e.Message}");
                return;
            }
            if (!claimed)
            {
                log.Info($"Player Studio share {payout.Payout} for creator 0x{payout.Creator:X} was already mailed: confirmed again, not mailed twice.");
                Send(new CommercePayoutSent { Payout = payout.Payout });
                return;
            }
            worldQueue.Enqueue(() =>
            {
                try
                {
                    Commerce.MailCreatorShare(payout.Creator, payout.DesignName, payout.Buyer, payout.Paid, payout.Share);
                }
                catch (Exception e)
                {
                    // The mail did not go: give the claim back so the next offer can mail it.
                    log.Error(e, $"Player Studio share {payout.Payout} for creator 0x{payout.Creator:X} could not be mailed; offered again later.");
                    Task.Run(() =>
                    {
                        try { DatabaseManager.Instance.CharacterDatabase.ReleaseUnlock(payout.Creator, Unlocks.StudioPayoutMailed, key); }
                        catch (Exception release) { log.Error(release, $"Player Studio share {payout.Payout}: claim not released."); }
                    });
                    return;
                }
                Send(new CommercePayoutSent { Payout = payout.Payout });
            });
        }

        // ---- Showcase following (the service's FollowService) ---------------------------------------------------------

        /// <summary>
        /// Digest mails from the service, for the one consumer (<see cref="DigestLoop"/>). Until 2026-10-18 each mail started
        /// its own pool task with a one-row name query; at the digest hour or a world's hello with a backlog that was
        /// thousands of queries at once against the pool the world thread uses (review #10).
        /// </summary>
        private readonly System.Threading.Channels.Channel<CommerceDigestMail> digestQueue =
            System.Threading.Channels.Channel.CreateUnbounded<CommerceDigestMail>(new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true });

        /// <summary>Most digest mails whose recipients are looked up in one query.</summary>
        private const int DigestBatch = 200;

        /// <summary>
        /// The digest consumer (pool thread, one for the service's lifetime): takes the queued mails in batches of up to
        /// <see cref="DigestBatch"/>, looks up all their recipients' names in one query (GetCharacterNames takes a list) and
        /// hands each on (<see cref="DeliverDigest"/>). A failed lookup leaves the batch unconfirmed: the service offers
        /// those mails again at the next link.
        /// </summary>
        private async Task DigestLoop()
        {
            var reader = digestQueue.Reader;
            var batch = new List<CommerceDigestMail>(DigestBatch);
            try
            {
                while (await reader.WaitToReadAsync())
                {
                    batch.Clear();
                    while (batch.Count < DigestBatch && reader.TryRead(out CommerceDigestMail mail))
                        batch.Add(mail);
                    Dictionary<ulong, string> names;
                    try
                    {
                        names = DatabaseManager.Instance.CharacterDatabase.GetCharacterNames(batch.Select(d => d.Recipient).Distinct());
                    }
                    catch (Exception e)
                    {
                        // Not confirmed: the service offers them again at the next link.
                        log.Warn($"{batch.Count} following digest mail(s) not sent now: {e.Message}");
                        continue;
                    }
                    foreach (CommerceDigestMail digest in batch)
                    {
                        try
                        {
                            DeliverDigest(digest, names.GetValueOrDefault(digest.Recipient));
                        }
                        catch (Exception e)
                        {
                            log.Warn($"Following digest mail {digest.Mail} not sent now: {e.Message}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                log.Error(e, "Following digest consumer stopped.");
            }
        }

        /// <summary>
        /// A digest mail for one of this realm's characters, greeted by their current name <paramref name="name"/> (this
        /// world's character database is the realm's; null when the character is gone), queued to the in-process outbox
        /// (ShowcaseFollow.Deliver: same pace, same sender) and confirmed once it went out. A recipient whose character is
        /// gone is confirmed without a mail, so it is not offered again.
        /// </summary>
        private void DeliverDigest(CommerceDigestMail digest, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                log.Info($"Following digest mail {digest.Mail}: character {digest.Recipient} ({digest.RecipientName}) is gone; dropped.");
                Send(new CommerceDigestMailSent { Mail = digest.Mail });
                return;
            }
            ShowcaseFollow.Deliver(digest.Recipient, name, digest.Subject, digest.Body, () =>
            {
                Send(new CommerceDigestMailSent { Mail = digest.Mail });
                log.Info($"Following digest mail {digest.Mail} ({digest.Subject}) sent to {name}.");
            });
        }

        /// <summary>One CommerceFollowRequest and its reply (the list after the change), or null when the service is away.</summary>
        private Task<CommerceFollowReply> AskFollow(WorldSession session, byte action, ulong creator, string text)
        {
            var done = NewCompletion<CommerceFollowReply>();
            CommerceCaller caller = Caller(session);
            if (!Ask(id => new CommerceFollowRequest { RequestId = id, Caller = caller, Action = action, Creator = creator, Text = text ?? "" },
                reply => done.SetResult(reply as CommerceFollowReply)))
                done.SetResult(null);
            return done.Task;
        }

        private const string FollowUnreachable = "Following is not reachable right now. Please try again in a moment.";

        public async Task<ShowcaseFollow.FollowList> FollowList(WorldSession session)
        {
            CommerceFollowReply reply = await AskFollow(session, CommerceFollowRequest.List, 0, "");
            return reply == null
                ? new ShowcaseFollow.FollowList(false, false, new List<(ulong, string)>(), new List<string>(), FollowUnreachable)
                : new ShowcaseFollow.FollowList(true, reply.DigestOn, reply.Creators.ToList(), reply.Tags.ToList());
        }

        /// <summary>A creator typed by name is found in this world's character database (the realm's) first, as in-process.</summary>
        public async Task<(bool Ok, string Message)> Follow(WorldSession session, string creatorName, string tags)
        {
            CommerceFollowReply reply;
            if (!string.IsNullOrWhiteSpace(creatorName))
            {
                (ulong id, string exact, string refusal) = ShowcaseFollow.FindCreator(creatorName);
                if (id == 0)
                    return (false, refusal);
                reply = await AskFollow(session, CommerceFollowRequest.AddCreator, id, exact);
            }
            else
                reply = await AskFollow(session, CommerceFollowRequest.AddTags, 0, tags);
            return reply == null ? (false, FollowUnreachable) : (reply.Ok, reply.Message);
        }

        public async Task<(bool Ok, string Message)> Unfollow(WorldSession session, ulong creator, string tags)
        {
            CommerceFollowReply reply = creator != 0
                ? await AskFollow(session, CommerceFollowRequest.RemoveCreator, creator, "")
                : await AskFollow(session, CommerceFollowRequest.RemoveTags, 0, tags);
            return reply == null ? (false, FollowUnreachable) : (reply.Ok, reply.Message);
        }

        /// <summary>The service's run for this world's realm (admin request "followdigest"): its summary line.</summary>
        public async Task<List<string>> RunFollowDigest(WorldSession requester, bool dryRun, double? hours)
        {
            string arguments = (dryRun ? "dry " : "") + (hours is double h ? h.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
            return new List<string> { await AdminText("followdigest", arguments.Trim()) };
        }

        // ---- contests -------------------------------------------------------------------------------------------------

        public void ContestList(WorldSession session)
        {
            // Unanswered while the service is away: the client asks again by itself (MODLOG 10-11, spec §7).
            SendRequest(session, CommerceMessages.RatingFamily, CommerceMessages.RequestContestsSub, null);
        }

        public void ContestVote(WorldSession session, string itemId, ulong claimId)
        {
            byte[] body = new InternalWriter().Str(itemId ?? "").U64(claimId).ToArray();
            if (!SendRequest(session, CommerceMessages.RatingFamily, CommerceMessages.AddVoteSub, body))
                log.Info($"{session.CharacterName} contest vote for site {claimId}: not counted (the commerce service is unreachable).");
        }

        /// <summary>
        /// Claims 0x41 SubmitContestEntry (Contests.Submit's body: u64 site, string contest, u32 n × (u32 + bytes)
        /// screenshots, u8): the zone's checks — the site exists and is the player's, the entry limiter — then the entry to
        /// the service, which checks the gallery listing and the contest and answers ContestEntryResults itself.
        /// </summary>
        public void ContestSubmit(WorldSession session, byte[] body)
        {
            ulong claimId;
            string contestName;
            var shots = new List<byte[]>();
            try
            {
                var reader = new GamePacketReader(body);
                claimId = reader.ReadULong();
                contestName = reader.ReadString() ?? "";
                try
                {
                    uint n = reader.ReadUInt();
                    for (int i = 0; i < n && i < 8; i++)
                        shots.Add(reader.ReadBytes(reader.ReadUInt()));
                }
                catch (Exception)
                {
                    // Screenshots are optional; a short body just has none.
                }
            }
            catch (Exception)
            {
                return;
            }
            ClaimModel site = Claims.Get(claimId);
            bool allowed = session.ContestLimiter.TryTake();
            bool sent = Send(new CommerceContestEntrySubmit
            {
                Caller = Caller(session), Site = claimId, Contest = contestName, Allowed = allowed,
                OwnSite = site != null && site.OwnerGuid == session.PlayerGuid, SiteName = site?.Name ?? "", Screenshots = shots
            });
            if (!sent)
            {
                // [I] ContestEntryResults false (the client's ContestSubmission.Fail), nothing stored: the service is away.
                var output = new List<byte>();
                var w = new GamePacketWriter(output);
                w.Write((byte)0x18);
                w.Write(false);
                w.Write(site?.Name ?? "");
                w.Write(contestName);
                session.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, output.ToArray());
                log.Info($"{session.CharacterName} entered site {claimId} into \"{contestName}\": refused (the commerce service is unreachable).");
            }
        }

        public List<string> ContestNamesOf(ulong claimId)
        {
            lock (cacheSync)
                return contestNames.TryGetValue(claimId, out List<string> names) ? names.ToList() : new List<string>();
        }

        // ---- portraits ------------------------------------------------------------------------------------------------

        /// <summary>FotomatBase: PortraitDataRequest (sub 2) to the service; GeneratePortraitRequest (sub 1, Zone channel) logged here.</summary>
        public void PortraitRequest(WorldSession session, byte[] data)
        {
            if (data.Length < 2)
                return;
            short sub = BitConverter.ToInt16(data, 0);
            if (sub == Portraits.DataRequestSub && data.Length >= 10)
                SendRequest(session, CommerceMessages.FotomatFamily, CommerceMessages.PortraitDataRequestSub, data[2..]);
            else if (sub == Portraits.GenerateSub)
                log.Debug($"{session.CharacterName} asked for a rendered portrait ({data.Length} bytes of appearance).");
        }

        public void PortraitCrc(WorldSession session, uint crc)
        {
            if (!SendRequest(session, CommerceMessages.PortraitCrcFamily, 0, BitConverter.GetBytes(crc)))
                log.Info($"{session.CharacterName} announced a portrait (CRC {crc:X8}), not kept: the commerce service is unreachable.");
        }

        public Task<(bool Ok, string Message)> PortraitUpload(ulong character, byte[] portrait, byte[] headshot)
        {
            var done = NewCompletion<(bool, string)>();
            if (!Ask(id => new CommercePortraitUpload
            {
                RequestId = id, World = worldId, Character = character, Portrait = portrait ?? Array.Empty<byte>(), Headshot = headshot ?? Array.Empty<byte>()
            }, reply => done.SetResult(reply is CommercePortraitUploadReply r ? (r.Ok, r.Message) : (false, "commerce service unreachable"))))
                done.SetResult((false, "commerce service unreachable"));
            return done.Task;
        }

        // ---- Showcase web pages (stay on this world, spec §6.6) ------------------------------------------------------

        public string ShowcaseUrl => Store.BaseUrl;

        public void ShowcaseSignIn(WorldSession session) => Store.SignIn(session);

        /// <summary>The player left the world: the page token ends, and so does the connection id the service knew them by.</summary>
        public void ShowcaseSignOut(WorldSession session)
        {
            Store.Forget(session);
            lock (sync)
                if (connectionOf.Remove(session, out ulong id))
                    connections.Remove(id);
        }

        public string ShowcaseToken(WorldSession session) => Store.TokenOf(session);

        public void UiEvent(WorldSession session, uint uiEvent) => Store.UiEvent(session, uiEvent);

        public void StartWebPages() => WebPages.Start();

        /// <summary>World thread, every tick: the web requests, the work the service's link queued, overdue requests.</summary>
        public void UpdateWebPages()
        {
            WebPages.Update();
            while (worldQueue.TryDequeue(out Action action))
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    log.Error(e, "Commerce work from the service failed.");
                }
            }
            ExpirePending();
            ExpireCreates();
        }

        public void UpdateSignIns() => Store.Update();

        // ---- test tooling ---------------------------------------------------------------------------------------------

        public void OfferReview(WorldSession session)
            => SendRequest(session, CommerceMessages.CommerceFamily, Commerce.SubGetProductForReview, null);

        public void PortraitStore(ulong character, string kind, byte[] png)
        {
            if (!Ask(id => new CommercePortraitUpload { RequestId = id, World = worldId, Character = character, Trusted = true, Kind = kind ?? "", Portrait = png },
                reply => log.Info($"portraittest: {(reply as CommercePortraitUploadReply)?.Message ?? "no answer"}")))
                log.Info("portraittest: the commerce service is unreachable.");
        }

        public bool PortraitSend(WorldSession session, ulong character, string kind)
        {
            byte[] body = new InternalWriter().U64(character).ToArray();
            var w = new List<byte>(body);
            new GamePacketWriter(w).Write(kind ?? "");
            return SendRequest(session, CommerceMessages.FotomatFamily, CommerceMessages.PortraitDataRequestSub, w.ToArray());
        }

        public Task<string> ContestSetPhase(uint id, string phase) => AdminText("contestphase", $"{id} {phase}");

        public Task<string> ContestDescribe() => AdminText("contests", "");

        private Task<string> AdminText(string command, string arguments)
        {
            var done = NewCompletion<string>();
            if (!Ask(id => new CommerceAdminRequest { RequestId = id, Command = command, Arguments = arguments },
                reply => done.SetResult((reply as CommerceAdminReply)?.Text ?? "no answer from the commerce service")))
                done.SetResult("the commerce service is unreachable");
            return done.Task;
        }

        // ---- the one-time import of this world's files -----------------------------------------------------------------

        /// <summary>
        /// The service asked for the files this world kept before it existed (spec §8 step 8): Contests/contests.json,
        /// entries.json and shots/*.bin, or Portraits/*.png. Read only; sent one message per file, then the count.
        /// </summary>
        private void SendImport(uint what)
        {
            try
            {
                var files = new List<(string Name, string Path)>();
                if (what == CommerceMessages.ImportContests)
                {
                    foreach (string name in new[] { "contests.json", "entries.json" })
                        if (File.Exists(Path.Combine(ContestFolder, name)))
                            files.Add((name, Path.Combine(ContestFolder, name)));
                    string shots = Path.Combine(ContestFolder, "shots");
                    if (Directory.Exists(shots))
                        files.AddRange(Directory.EnumerateFiles(shots, "*.bin").Select(f => ("shots/" + Path.GetFileName(f), f)));
                }
                else if (what == CommerceMessages.ImportPortraits && Directory.Exists(PortraitFolder))
                    files.AddRange(Directory.EnumerateFiles(PortraitFolder, "*.png").Select(f => (Path.GetFileName(f), f)));
                int sent = 0;
                foreach ((string name, string path) in files)
                {
                    byte[] data = File.ReadAllBytes(path);
                    if (data.Length > 8 * 1024 * 1024)
                        continue;
                    if (!Send(new CommerceImportFile { What = what, Name = name, Data = data }))
                        return;   // link lost: the service asks again at the next hello
                    sent++;
                }
                Send(new CommerceImportDone { What = what, Files = (uint)sent });
                log.Info($"Commerce import: {sent} {(what == CommerceMessages.ImportContests ? "contest" : "portrait")} file(s) sent to the service.");
            }
            catch (Exception e)
            {
                log.Error($"Commerce import of kind {what} failed: {e.Message}");
            }
        }
    }
}
