using LandmarkEmulator.WorldServer.Network;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldServer.Game.WorldChannel
{
    /// <summary>
    /// The shared commerce back end (`commerce`, one for all worlds; trace/architecture/world-channel-service.md §5.2):
    /// what was account-wide or Daybreak-wide on live — Player Studio / Marketplace, the wallet and account store,
    /// peer reviews, contests, portraits (Fotomat) and the Showcase web pages.
    ///
    /// Build step 1 (2026-10-15): an interface split inside the world server, no behaviour change. Today's code is the
    /// in-process implementation (<see cref="LocalCommerceService"/>); a later step relays these calls over the
    /// network (spec §6.3). Zone code reaches commerce only through this interface; the commerce code itself still
    /// calls the zone directly (templates, claims, inventory, mail), which the spec's later steps turn into messages.
    /// <para>
    /// Build step 8 (2026-10-16): with COMMERCE_ADDRESS set the world uses <see cref="RemoteCommerceService"/>, a client
    /// of the shared `commerce` container (LandmarkEmulator.CommerceServer; messages
    /// LandmarkEmulator.Internal/Commerce/CommerceMessages.cs); without it nothing changes. The calls are made on the world
    /// thread unless a summary says otherwise; the <see cref="Task"/> results complete later when the service answers
    /// (immediately in-process). The Showcase web pages stay on the world (spec §6.6 option: they act on the player's
    /// zone — bag, travel, vendors, admin); their commerce data come through here.
    /// </para>
    /// </summary>
    public interface ICommerceService
    {
        // ---- InGamePurchase 0x45 (§3.1) -----------------------------------------------------------------------------

        /// <summary>
        /// An InGamePurchase request for commerce: wallet (0x0B), account info (0x1A), Player Studio products (0x2F,
        /// 0x31, 0x33), peer review (0x3F, 0x42), account store (0x36). The zone's own subs are not sent here
        /// (<see cref="Commerce.IsZoneSub"/>).
        /// </summary>
        void InGamePurchaseRequest(WorldSession session, ushort sub, byte[] body);

        /// <summary>
        /// A design with a product waiting for review or listed (TemplateBasicData IsOnMarketplace; spec
        /// CommerceMarketplaceStatus, cached by the zone once split).
        /// </summary>
        bool IsOnMarketplace(ulong design);

        /// <summary>
        /// A build site of this world published to the gallery (spec CommerceGalleryStatus, cached by the zone once split):
        /// only these are listed, liked (P5630), entered into competitions (text 576478) or visited from the gallery.
        /// </summary>
        bool IsOnGallery(ulong claimId);

        /// <summary>This world's gallery listings (Store.Gallery): site → when listed, contest markers. Any thread.</summary>
        IReadOnlyDictionary<ulong, Commerce.Listing> GalleryListings();

        /// <summary>World entry: PacketPlayerStudioRegistered 0xFE (every player is a registered member).</summary>
        void PlayerStudioRegistered(WorldSession session);

        /// <summary>An admin decision on a waiting product (live: "appgamecommerce product approve|reject").</summary>
        Task<bool> AdminDecide(ulong productId, bool approve);

        /// <summary>
        /// The Player Studio products the peers rejected, waiting for staff (live's "Customer Service queue", P1210): the
        /// admin page's list (Commerce.StaffQueue in-process; the service's "psqueue" admin command).
        /// </summary>
        Task<List<LandmarkEmulator.Internal.Commerce.StaffQueueEntry>> StaffQueue();

        /// <summary>Takes a listed Player Studio design (the Showcase's Player Studio page).</summary>
        Task<(bool Ok, string Message)> TakeDesign(WorldSession session, ulong productId);

        /// <summary>The designs peer review accepted, newest first (the Showcase's Player Studio page).</summary>
        Task<List<Commerce.StudioDesign>> ListedDesigns();

        // ---- Showcase following (OPEN-LEADS L137; Game/ShowcaseFollow.cs) -------------------------------------------

        /// <summary>The Following tab: the creators and tag groups this player follows, and whether the daily mail is on. Any thread.</summary>
        Task<ShowcaseFollow.FollowList> FollowList(WorldSession session);

        /// <summary>Follows a creator typed by name (<paramref name="creatorName"/>), else a tag group (<paramref name="tags"/>). Any thread.</summary>
        Task<(bool Ok, string Message)> Follow(WorldSession session, string creatorName, string tags);

        /// <summary>Stops following a creator (<paramref name="creator"/> ≠ 0), else a tag group (<paramref name="tags"/>). Any thread.</summary>
        Task<(bool Ok, string Message)> Unfollow(WorldSession session, ulong creator, string tags);

        /// <summary>
        /// DevCommands "followdigest [dry] [hours]": the following digest now (in-process: this world's; with the commerce
        /// service: the service's run for this world's realm). The lines to show <paramref name="requester"/>. Any thread.
        /// </summary>
        Task<List<string>> RunFollowDigest(WorldSession requester, bool dryRun, double? hours);

        // ---- contests (RatingBase 0x1D / AddVote "Contests", Claims 0x41; §3.2) ------------------------------------

        /// <summary>RatingBase 0x1D RequestContestsRequest → 0x1E RequestContestsReply.</summary>
        void ContestList(WorldSession session);

        /// <summary>
        /// A contest vote (AddVote "Contests", relayed by the world channel; spec ContestVote): counted, and answered with
        /// VoteReply 0x12 {voter, item id as sent, counted, the site's contest total} (Ratings.HandleAddVote's reply).
        /// </summary>
        void ContestVote(WorldSession session, string itemId, ulong claimId);

        /// <summary>Claims 0x41 SubmitContestEntry (spec ContestEntrySubmit, from the zone).</summary>
        void ContestSubmit(WorldSession session, byte[] body);

        /// <summary>The names of the contests a site of this world is entered in (its gallery entry). Any thread.</summary>
        List<string> ContestNamesOf(ulong claimId);

        // ---- portraits (Fotomat 0x98, 0x75; §3.5) ---------------------------------------------------------------------

        /// <summary>FotomatBase from the client (PortraitDataRequest; GeneratePortraitRequest is logged).</summary>
        void PortraitRequest(WorldSession session, byte[] data);

        /// <summary>AddClientPortraitCrc 0x75: the checksum of the portrait about to be uploaded.</summary>
        void PortraitCrc(WorldSession session, uint crc);

        /// <summary>
        /// The client's own portrait upload (web pages /portrait/upload): accepted when the portrait's CRC-32 is what that
        /// character announced. Web thread.
        /// </summary>
        Task<(bool Ok, string Message)> PortraitUpload(ulong character, byte[] portrait, byte[] headshot);

        // ---- Showcase web pages (§6.6) -------------------------------------------------------------------------------

        /// <summary>The address players' browsers reach the Showcase pages at.</summary>
        string ShowcaseUrl { get; }

        /// <summary>World entry: a fresh page token (spec WebSignIn); the sign-in URL follows once the browser exists.</summary>
        void ShowcaseSignIn(WorldSession session);

        /// <summary>The player left the world: the token stops working.</summary>
        void ShowcaseSignOut(WorldSession session);

        /// <summary>The page token of an online player, or null.</summary>
        string ShowcaseToken(WorldSession session);

        /// <summary>A UI event the client reported (7 = the Showcase was opened).</summary>
        void UiEvent(WorldSession session, uint uiEvent);

        /// <summary>Starts the web server (server start).</summary>
        void StartWebPages();

        /// <summary>World thread: runs the web requests waiting for the world thread.</summary>
        void UpdateWebPages();

        /// <summary>World thread: sends the page sign-ins that are due.</summary>
        void UpdateSignIns();

        // ---- test tooling (DevCommands) -----------------------------------------------------------------------------

        /// <summary>DevCommands "psoffer": the reply to GetProductForReview, unasked.</summary>
        void OfferReview(WorldSession session);

        /// <summary>DevCommands "portraittest": stores an image as a character's portrait / headshot.</summary>
        void PortraitStore(ulong character, string kind, byte[] png);

        /// <summary>DevCommands "portraittest": sends a stored image as PlayerImageData.</summary>
        bool PortraitSend(WorldSession session, ulong character, string kind);

        /// <summary>DevCommands "contestphase".</summary>
        Task<string> ContestSetPhase(uint id, string phase);

        /// <summary>DevCommands "contests": the contests and their entries as text.</summary>
        Task<string> ContestDescribe();

        /// <summary>Where commerce runs, for the admin monitor ("in-process", or the service's address and link state).</summary>
        string LinkState { get; }
    }
}
