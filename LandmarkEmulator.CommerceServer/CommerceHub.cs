using LandmarkEmulator.Internal;
using LandmarkEmulator.Internal.Commerce;
using LandmarkEmulator.Internal.Social;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace LandmarkEmulator.CommerceServer
{
    /// <summary>
    /// One world's link to the service. The world's zone process dials in (COMMERCE_ADDRESS) and says which world it is,
    /// and which character database (realm) it uses, with its first message, <see cref="CommerceHello"/>.
    /// </summary>
    public sealed class WorldLink
    {
        public InternalLink Link { get; }
        /// <summary>The world (zone_server id), 0 until its CommerceHello arrived.</summary>
        public uint WorldId { get; set; }
        /// <summary>The world's realm (its character database), "" until its CommerceHello arrived.</summary>
        public string Realm { get; set; } = "";

        public WorldLink(InternalLink link) => Link = link;

        public override string ToString() => WorldId != 0 ? $"world {WorldId} ({Link.Remote})" : Link.Remote?.ToString() ?? "?";
    }

    /// <summary>
    /// The commerce service's core, built like the social service's SocialHub: accepts the worlds' links, runs every
    /// message through one work loop (so the store and the in-memory tables need no locks and one world's messages keep
    /// their order), and answers players through the world that sent the request (<see cref="TunnelToExternal"/>,
    /// addressed by the zone's connection id). The areas: <see cref="ProductService"/> (Player Studio / Marketplace),
    /// <see cref="ContestService"/>, <see cref="PortraitService"/>.
    /// </summary>
    public sealed class CommerceHub : IDisposable
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private readonly InternalListener listener;
        private readonly Channel<Action> work = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions { SingleReader = true });
        private readonly CancellationTokenSource stop = new();
        private readonly List<WorldLink> links = new();
        /// <summary>world → realm, from commerce_world (worlds that linked before, the import's world) and every hello.</summary>
        private Dictionary<uint, string> realms;
        private Task loop;

        public CommerceServerConfiguration Config { get; }
        public CommerceStore Store { get; }
        public ProductService Products { get; }
        public ContestService Contests { get; }
        public PortraitService Portraits { get; }
        /// <summary>Showcase following and its daily digest mail (since 2026-10-18).</summary>
        public FollowService Follows { get; }
        /// <summary>The folder for contest screenshots and portraits (CommerceServer.json DataPath).</summary>
        public string DataPath { get; }

        /// <summary>The port the service listens on (useful with port 0 in tests).</summary>
        public int Port => listener.Port;

        public CommerceHub(CommerceServerConfiguration config, CommerceStore store, IPAddress address, int port)
        {
            Config = config;
            Store = store;
            DataPath = System.IO.Path.GetFullPath(string.IsNullOrWhiteSpace(config.DataPath) ? "Data" : config.DataPath);
            listener = new InternalListener(address, port);
            Products = new ProductService(this);
            Contests = new ContestService(this);
            Portraits = new PortraitService(this);
            Follows = new FollowService(this);
        }

        private Timer statusTimer;
        /// <summary>Once a minute: is the day's following digest due (FollowService.Tick)?</summary>
        private Timer digestTimer;
        private static readonly TimeSpan DigestCheck = TimeSpan.FromMinutes(1);
        private readonly DateTime started = DateTime.UtcNow;

        public void Start()
        {
            realms = Store.Realms();
            loop = Task.Run(WorkLoop);
            statusTimer = new Timer(_ => Post(SendStatus), null, ServiceStatus.Interval, ServiceStatus.Interval);
            digestTimer = new Timer(_ => Post(Follows.Tick), null, DigestCheck, DigestCheck);
            listener.Accepted += OnAccepted;
            listener.Start();
            log.Info($"Commerce service listening on port {Port} (data folder {DataPath}; {realms.Count} world(s) known).");
        }

        /// <summary>Runs <paramref name="action"/> on the work loop (after everything queued before it).</summary>
        public void Post(Action action) => work.Writer.TryWrite(action);

        /// <summary>
        /// Every <see cref="ServiceStatus.Interval"/> (work loop): the linked worlds and their realms to every linked world,
        /// for the worlds' admin monitor page (TASKS B.7).
        /// </summary>
        private void SendStatus()
        {
            DateTime now = DateTime.UtcNow;
            List<WorldLink> worlds = links.ToList();
            ServicePacket status = ServiceStatus.Build("Commerce service", new List<(string, string)>
            {
                ("Up for", $"{(int)(now - started).TotalHours}h {(now - started).Minutes}m"),
                ("Worlds linked", string.Join(", ", worlds.Where(w => w.WorldId != 0).Select(w => $"{w.WorldId} ({w.Realm})"))),
                ("Worlds known", realms.Count.ToString())
            });
            foreach (WorldLink world in worlds)
                world.Link.Send(status);
        }

        private async Task WorkLoop()
        {
            try
            {
                await foreach (Action action in work.Reader.ReadAllAsync(stop.Token))
                {
                    try
                    {
                        action();
                    }
                    catch (Exception e)
                    {
                        // A failing request (e.g. the database briefly away) must not stop the service.
                        log.Error(e, "Commerce work item failed.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void OnAccepted(InternalLink link)
        {
            var world = new WorldLink(link);
            link.Tag = world;
            link.Received += (_, message) => Post(() => OnMessage(world, message));
            link.Closed += (_, error) => Post(() =>
            {
                links.Remove(world);
                log.Info($"Link to {world} closed{(error != null ? ": " + error.Message : ".")}");
            });
            Post(() => links.Add(world));
            link.Start();
            log.Info($"World link from {link.Remote}.");
        }

        // ---- worlds and realms ----------------------------------------------------------------------------------------

        /// <summary>A world's realm (its character database), or "world:&lt;id&gt;" for a world never seen (its own realm).</summary>
        public string RealmOf(uint world) => realms.TryGetValue(world, out string realm) ? realm : $"world:{world}";

        /// <summary>The link of a world, or null.</summary>
        public WorldLink LinkOf(uint world) => links.FirstOrDefault(l => l.WorldId == world);

        /// <summary>Every realm the service knows (worlds that linked before, the import's world), each once.</summary>
        public IEnumerable<string> KnownRealms() => realms.Values.Distinct().ToList();

        /// <summary>The linked worlds of a realm.</summary>
        public IEnumerable<WorldLink> LinksOfRealm(string realm) => links.Where(l => l.WorldId != 0 && l.Realm == realm).ToList();

        // ---- sending --------------------------------------------------------------------------------------------------

        /// <summary>A client packet (opcode bytes + body, the 0.695 client's numbering) for the caller, through its world.</summary>
        public void SendClient(WorldLink world, CommerceCaller caller, byte[] packet)
            => world?.Link.Send(new TunnelToExternal
            {
                Destination = new ExternalDestination(caller.ConnectionId, GatewayChannel.World, ExternalDestination.Reliable),
                Packet = packet
            });

        /// <summary>A service message to a world.</summary>
        public void SendService(WorldLink world, ISocialMessage message) => world?.Link.Send(message.ToPacket());

        /// <summary>A build site's gallery state (listing, contest markers and names) to its world (CommerceGalleryStatus).</summary>
        public void SendGalleryStatus(uint world, ulong claim)
        {
            WorldLink link = LinkOf(world);
            if (link == null)
                return;
            ProductRow listing = Store.Listing(world, claim);
            SendService(link, new CommerceGalleryStatus
            {
                Listed = listing != null,
                Listing = new GalleryListing(claim, listing == null ? 0 : new DateTimeOffset(listing.CreatedAt).ToUnixTimeSeconds(),
                    listing == null ? "" : GalleryTags.ContestTagsOf(listing.Tags), Contests.NamesOf(world, claim))
            });
        }

        // ---- messages -------------------------------------------------------------------------------------------------

        private void OnMessage(WorldLink world, IInternalMessage message)
        {
            if (message is not ServicePacket packet)
            {
                if (message is not Ping)
                    log.Warn($"{world} sent internal opcode {message.Opcode}, which the commerce service doesn't take.");
                return;
            }
            ISocialMessage parsed;
            try
            {
                parsed = CommerceMessages.Parse(packet);
            }
            catch (Exception e) when (e is System.IO.InvalidDataException)
            {
                log.Warn($"Unreadable service packet 0x{packet.Family:X}/0x{packet.Sub:X} from {world}: {e.Message}");
                return;
            }
            if (parsed is CommerceHello hello)
            {
                Hello(world, hello);
                return;
            }
            if (world.WorldId == 0)
            {
                log.Warn($"{world} sent 0x{packet.Family:X}/0x{packet.Sub:X} before its CommerceHello: dropped.");
                return;
            }
            switch (parsed)
            {
                case CommerceClientRequest { Family: CommerceMessages.CommerceFamily } request:
                    Products.Handle(world, request);
                    break;
                case CommerceClientRequest { Family: CommerceMessages.RatingFamily, Sub: CommerceMessages.RequestContestsSub } request:
                    Contests.SendList(world, request.Caller);
                    break;
                case CommerceClientRequest { Family: CommerceMessages.RatingFamily, Sub: CommerceMessages.AddVoteSub } request:
                    Contests.Vote(world, request);
                    break;
                case CommerceClientRequest { Family: CommerceMessages.FotomatFamily } request:
                    Portraits.Request(world, request);
                    break;
                case CommerceClientRequest { Family: CommerceMessages.PortraitCrcFamily } request:
                    Portraits.Announce(world, request);
                    break;
                case CommerceProductCreate create:
                    Products.Create(world, create);
                    break;
                case CommerceProductCreateCheck check:
                    Products.CheckCreate(world, check);
                    break;
                case CommerceGrantDesignRequest grant:
                    SendService(world, Products.Grant(world, grant));
                    break;
                case CommerceDesignTaken taken:
                    Products.Taken(taken);
                    break;
                case CommerceStudioListRequest list:
                    SendService(world, new CommerceStudioListReply { RequestId = list.RequestId, Designs = Products.ListedDesigns() });
                    break;
                case CommerceResultMailSent sent:
                    Store.MailSent(sent.Product);
                    break;
                case CommercePayoutSent sent:
                    Products.PayoutSent(sent.Payout);
                    break;
                case CommerceFollowRequest follow:
                    SendService(world, Follows.Handle(world, follow));
                    break;
                case CommerceDigestMailSent sent:
                    Follows.Sent(sent.Mail);
                    break;
                case CommerceAdminRequest admin:
                    Admin(world, admin);
                    break;
                case CommerceContestEntrySubmit submit:
                    Contests.Submit(world, submit);
                    break;
                case CommercePortraitUpload upload:
                    SendService(world, Portraits.Upload(world, upload));
                    break;
                case CommerceImportFile { What: CommerceMessages.ImportContests } file:
                    Contests.ImportFile(world.WorldId, file);
                    break;
                case CommerceImportFile { What: CommerceMessages.ImportPortraits } file:
                    portraitImports[world.WorldId] = portraitImports.GetValueOrDefault(world.WorldId) + (Portraits.ImportFile(world.WorldId, file) ? 1 : 0);
                    break;
                case CommerceImportDone done:
                    ImportDone(world, done);
                    break;
                default:
                    log.Warn($"{world} sent service packet 0x{packet.Family:X}/0x{packet.Sub:X}, which the commerce service doesn't take.");
                    break;
            }
        }

        /// <summary>Portrait files stored so far per world during its import.</summary>
        private readonly Dictionary<uint, int> portraitImports = new();

        /// <summary>
        /// A world linked: remembered with its realm; it gets the market snapshot (its realm's designs on the market, its
        /// listed sites and contest names), the result mails and Player Studio shares owed to its realm, and — once per world — the request for the
        /// files it kept before the service (contests, portraits).
        /// </summary>
        private void Hello(WorldLink world, CommerceHello hello)
        {
            string realm = string.IsNullOrWhiteSpace(hello.Realm) ? $"world:{hello.WorldId}" : hello.Realm.Trim();
            WorldLink older = links.FirstOrDefault(l => l != world && l.WorldId == hello.WorldId);
            if (older != null)
            {
                // A restarted world links again before the old link noticed it was gone.
                links.Remove(older);
                older.Link.Close();
            }
            world.WorldId = hello.WorldId;
            world.Realm = realm;
            Store.UpsertWorld(hello.WorldId, realm, hello.WorldNameText);
            realms[hello.WorldId] = realm;
            List<ContestRow> contests = Store.Contests();
            List<EntryRow> entries = Store.Entries();
            List<GalleryListing> listings = Store.Listings(hello.WorldId).Select(p => new GalleryListing(p.ItemId,
                new DateTimeOffset(p.CreatedAt).ToUnixTimeSeconds(), GalleryTags.ContestTagsOf(p.Tags), Contests.NamesOf(hello.WorldId, p.ItemId, contests, entries))).ToList();
            var listed = listings.Select(l => l.Claim).ToHashSet();
            List<GalleryListing> unlisted = Contests.SitesWithEntries(hello.WorldId).Where(c => !listed.Contains(c))
                .Select(c => new GalleryListing(c, 0, "", Contests.NamesOf(hello.WorldId, c, contests, entries))).ToList();
            List<ulong> designs = Store.MarketDesigns(realm);
            SendService(world, new CommerceMarketSnapshot { Designs = designs, Listings = listings, Unlisted = unlisted });
            Products.SendPendingMail(world);
            Products.SendPendingPayouts(world);
            Follows.SendPending(world);
            log.Info($"{world} linked (realm {realm}): snapshot with {designs.Count} design(s) on the market, {listings.Count} listed site(s).");
            if ((hello.LocalData & CommerceMessages.ImportContests) != 0 && !Store.IsImported($"contests:{hello.WorldId}"))
                SendService(world, new CommerceImportRequest { What = CommerceMessages.ImportContests });
            if ((hello.LocalData & CommerceMessages.ImportPortraits) != 0 && !Store.IsImported($"portraits:{hello.WorldId}"))
            {
                portraitImports[hello.WorldId] = 0;
                SendService(world, new CommerceImportRequest { What = CommerceMessages.ImportPortraits });
            }
        }

        private void ImportDone(WorldLink world, CommerceImportDone done)
        {
            if (done.What == CommerceMessages.ImportContests)
            {
                string note = Contests.ImportDone(world.WorldId);
                if (note == null)
                    return;
                log.Info($"Contest import ({done.Files} file(s)) — {note}.");
                // The sites in contests now have names to show.
                foreach (ulong claim in Contests.SitesWithEntries(world.WorldId))
                    SendGalleryStatus(world.WorldId, claim);
            }
            else if (done.What == CommerceMessages.ImportPortraits)
            {
                portraitImports.Remove(world.WorldId, out int stored);
                string note = $"world {world.WorldId}: {done.Files} file(s) sent, {stored} portrait image(s) stored";
                Store.MarkImported($"portraits:{world.WorldId}", note);
                log.Info($"Portrait import — {note}.");
            }
        }

        /// <summary>The dev / admin commands (CommerceAdminRequest): psreview, psqueue, contestphase, contests, followdigest.</summary>
        private void Admin(WorldLink world, CommerceAdminRequest request)
        {
            (bool ok, string text) = request.Command switch
            {
                "psreview" => Products.AdminDecide(request.Arguments),
                Internal.Commerce.StaffQueue.Command => Products.StaffQueue(),
                "contestphase" => Contests.SetPhase(request.Arguments),
                "contests" => (true, Contests.Describe()),
                "followdigest" => Follows.AdminRun(world, request.Arguments),
                _ => (false, $"unknown command {request.Command}")
            };
            SendService(world, new CommerceAdminReply { RequestId = request.RequestId, Ok = ok, Text = text });
            log.Info($"{world}: {request.Command} {request.Arguments} → {text}");
        }

        public void Dispose()
        {
            statusTimer?.Dispose();
            digestTimer?.Dispose();
            listener.Dispose();
            Post(() =>
            {
                foreach (WorldLink world in links.ToList())
                    world.Link.Close();
            });
            work.Writer.TryComplete();
            try
            {
                loop?.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
            }
            stop.Cancel();
        }
    }
}
