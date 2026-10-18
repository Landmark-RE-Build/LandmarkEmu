using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldServer.Game.WorldChannel
{
    /// <summary>
    /// The commerce back end in the world server's own process: today's code (<see cref="Commerce"/>,
    /// <see cref="Contests"/>, <see cref="Portraits"/>, <see cref="Store"/>, <see cref="WebPages"/>), unchanged. Each
    /// call runs on the caller's thread exactly as the direct call did; the <see cref="Task"/> results are complete when
    /// returned. Used unless COMMERCE_ADDRESS is set (<see cref="WorldChannelServices.Configure"/>).
    /// </summary>
    public class LocalCommerceService : ICommerceService
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        // ---- InGamePurchase -----------------------------------------------------------------------------------------

        public void InGamePurchaseRequest(WorldSession session, ushort sub, byte[] body)
        {
            if (sub == Commerce.SubWalletInfoRequest)
                Commerce.SendWalletInfo(session);
            else
                Commerce.Handle(session, sub, body);
        }

        public bool IsOnMarketplace(ulong design) => Commerce.IsOnMarketplace(design);

        public bool IsOnGallery(ulong claimId) => Commerce.IsOnGallery(claimId);

        public IReadOnlyDictionary<ulong, Commerce.Listing> GalleryListings() => Commerce.Listings();

        public void PlayerStudioRegistered(WorldSession session) => SendPlayerStudioRegistered(session);

        /// <summary>
        /// PacketPlayerStudioRegistered 0xFE {u8 1} (reader FUN_142bb0fd0, GameEvents:OnPlayerStudioRegistered; "same" in
        /// 0.695): every player is a registered member (free; live required signing up on a web page). Sent by the zone at
        /// world entry in both modes: it is the same for every account on this server, so a commerce outage at login does
        /// not change the client's Player Studio state.
        /// </summary>
        public static void SendPlayerStudioRegistered(WorldSession session)
            => session.EnqueueMessage(ZoneMessageOpcode.PlayerStudioRegistered, new byte[] { 1 });

        public Task<bool> AdminDecide(ulong productId, bool approve) => Task.FromResult(Commerce.AdminDecide(productId, approve));

        public Task<List<LandmarkEmulator.Internal.Commerce.StaffQueueEntry>> StaffQueue() => Task.FromResult(Commerce.StaffQueue());

        public Task<(bool Ok, string Message)> TakeDesign(WorldSession session, ulong productId) => Task.FromResult(Commerce.TakeDesign(session, productId));

        public Task<List<Commerce.StudioDesign>> ListedDesigns() => Task.FromResult(Commerce.ListedDesigns());

        // ---- Showcase following ---------------------------------------------------------------------------------------

        public Task<ShowcaseFollow.FollowList> FollowList(WorldSession session)
            => Task.FromResult(ShowcaseFollow.LocalList(session.PlayerGuid));

        public Task<(bool Ok, string Message)> Follow(WorldSession session, string creatorName, string tags)
            => Task.FromResult(!string.IsNullOrWhiteSpace(creatorName)
                ? ShowcaseFollow.AddCreatorByName(session.PlayerGuid, creatorName) : ShowcaseFollow.AddTags(session.PlayerGuid, tags));

        public Task<(bool Ok, string Message)> Unfollow(WorldSession session, ulong creator, string tags)
        {
            bool done = creator != 0 ? ShowcaseFollow.RemoveCreator(session.PlayerGuid, creator) : ShowcaseFollow.RemoveTags(session.PlayerGuid, tags);
            return Task.FromResult((done, done ? "No longer followed." : "That wasn't followed."));
        }

        /// <summary>The in-process run (ShowcaseFollow.RunNow); a dry run also shows the requester their own mail.</summary>
        public async Task<List<string>> RunFollowDigest(WorldSession requester, bool dryRun, double? hours)
        {
            List<ShowcaseFollow.Letter> letters = await ShowcaseFollow.RunNow(hours, dryRun);
            var lines = new List<string>
            {
                $"followdigest{(dryRun ? " (dry run)" : "")}: {letters.Count} mail(s)"
                    + (letters.Count > 0 ? ": " + string.Join(", ", letters.Select(l => $"{l.RecipientName} ({l.Items} items)")) : " (nothing new for anyone following)")
                    + (ShowcaseFollow.DigestOn ? "." : ". The daily run is off (SHOWCASE_FOLLOW_DIGEST=false).")
            };
            if (dryRun && requester != null && letters.FirstOrDefault(l => l.Recipient == requester.PlayerGuid) is { } own)
                lines.Add(own.Subject + ": " + own.Body);
            return lines;
        }

        // ---- contests -----------------------------------------------------------------------------------------------

        public void ContestList(WorldSession session) => Contests.SendList(session);

        /// <summary>
        /// Contest vote (was in Ratings.HandleAddVote): VoteReply 0x12 (reader FUN_142d02880) {u64 voter, string item id,
        /// bool counted, u64 the site's contest vote total}.
        /// </summary>
        public void ContestVote(WorldSession session, string itemId, ulong claimId)
        {
            (bool counted, ulong total) = Contests.Vote(session, claimId);
            var reply = new List<byte>();
            var w = new GamePacketWriter(reply);
            w.Write((byte)0x12);
            w.Write(session.PlayerGuid);
            w.Write(itemId);
            w.Write(counted);
            w.Write(total);
            session.EnqueueMessage(ZoneMessageOpcode.RatingBase, reply.ToArray());
            log.Info($"{session.CharacterName} contest vote for site {claimId}: {(counted ? "counted" : "not counted")} (total {total}).");
        }

        public void ContestSubmit(WorldSession session, byte[] body) => Contests.Submit(session, body);

        public List<string> ContestNamesOf(ulong claimId) => Contests.NamesOf(claimId);

        // ---- portraits ----------------------------------------------------------------------------------------------

        public void PortraitRequest(WorldSession session, byte[] data) => Portraits.Handle(session, data);

        public void PortraitCrc(WorldSession session, uint crc) => Portraits.Announce(session, crc);

        public Task<(bool Ok, string Message)> PortraitUpload(ulong character, byte[] portrait, byte[] headshot)
            => Task.FromResult(Portraits.Upload(character, portrait, headshot));

        // ---- Showcase web pages -------------------------------------------------------------------------------------

        public string ShowcaseUrl => Store.BaseUrl;

        public void ShowcaseSignIn(WorldSession session) => Store.SignIn(session);

        public void ShowcaseSignOut(WorldSession session) => Store.Forget(session);

        public string ShowcaseToken(WorldSession session) => Store.TokenOf(session);

        public void UiEvent(WorldSession session, uint uiEvent) => Store.UiEvent(session, uiEvent);

        public void StartWebPages() => WebPages.Start();

        public void UpdateWebPages() => WebPages.Update();

        public void UpdateSignIns() => Store.Update();

        // ---- test tooling -------------------------------------------------------------------------------------------

        public void OfferReview(WorldSession session) => Commerce.OfferReview(session);

        public void PortraitStore(ulong character, string kind, byte[] png) => Portraits.Store(character, kind, png);

        public bool PortraitSend(WorldSession session, ulong character, string kind) => Portraits.Send(session, character, kind);

        public Task<string> ContestSetPhase(uint id, string phase) => Task.FromResult(Contests.DevSetPhase(id, phase));

        public Task<string> ContestDescribe() => Task.FromResult(Contests.Describe());

        public string LinkState => "in-process";
    }
}
