using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Internal.Commerce;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using Microsoft.EntityFrameworkCore;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The in-game commerce service (InGamePurchaseBase 0x45, u16 sub) behind Player Studio and the in-game store.
    /// Decoded 2026-10-12 from the 0.589 client (docs/commerce.md). No real money and no Station Cash on this server: the
    /// Showcase store charges Lumens, or nothing with FreeStore on (<see cref="Store"/>); the client's wallet carries the
    /// player's Lumens only while the Player Studio listing fee is charged (<see cref="SendWalletInfo"/>), else stays empty.
    /// <list type="bullet">
    /// <item>0x1A AccountInfoRequest → 0x1B AccountInfoResponse {u32 result (1 ok), string country, string currency,
    /// bool, string} (handler FUN_1431a67b0). Player Studio creation needs the account to be in the US with a zip code
    /// on file (FUN_1431b4c10; otherwise status 0x14 CommerceNoZipCode and the client opens the live payout web page).</item>
    /// <item>0x2F CommerceProductCreate (Player Studio; client FUN_1431b7c20, sent by FUN_14318a170; captured): u32 price,
    /// u32, u32, string name, string description, string tags, u32 n × item {u64 design guid, u32 type (2 = design),
    /// u32 quantity, u64, u8, string}, u8 edit, string headline, u32. Answered with 0x30 CommerceProductCreateResponse
    /// {u32 status, string} (FUN_1431a7430 → FUN_14319fc60): 1 created, 9 item not found, 0xB not the design's owner,
    /// 0xF already listed, 0xC not valid (status names CommerceCreateProduct* in the client's text).</item>
    /// <item>Peer review. The client asks by itself (commerce tick FUN_1431a64b0, once per session and again after each
    /// vote): 0x3F GetProductForReviewRequest (no body) → 0x40 ReviewProductResponse (FUN_1431a7fc0, reader
    /// FUN_1431834e0): a Product (FUN_143184930) then u32 products waiting. With a product (id ≠ 0) the client queues
    /// its own Game Action Queue popup, GaqItemMarketplaceItemReviewRequest ("GAQMarketplaceItemReviewsPending" with
    /// the count; no server notification), which opens PlayerStudioItemReview (data source
    /// MarketingData.InGamePurchase.ItemForReview). The vote: 0x42 SubmitProductReview {u64 product, u32 verdict}
    /// (FUN_1431c53c0; 1 approve, 0 reject; 2-7 = rejection reasons, whose lists the 0.589 client never fills) →
    /// 0x43 SubmitProductReviewResponse {bool} (FUN_1431a8620, Lua GameEvents:OnReviewComplete).</item>
    /// <item>The player's own products: 0x36 CommerceRequestAccountStore (no body; at login and after each create) →
    /// 0x38 AccountStore (FUN_1431a6b70, reader FUN_143182f10). The client looks products up there by item to answer
    /// Claims.IsClaimListedOnGallery and to edit or remove them: 0x33 ProductUpdate {u64 product, string name, string
    /// description, u32 price, string tags, u8 × 5, u64} (writer FUN_143181020, sent from the commerce tick) and 0x31
    /// ProductRemove {u64 product} (FUN_1431b8620; only for products with a BundleId), neither answered.</item>
    /// <item>Build sites on the gallery are products too: Build Site Management's Publish to Gallery (Lua
    /// Claims.ListClaimOnGallery, FUN_143533be0; the site needs tags) sends CommerceProductCreate with item type 3 =
    /// the claim and the free-listing byte set (FUN_1431b8df0), and is listed at once; Remove from Gallery removes it.
    /// The gallery rules (only published + tagged sites listed, likes and contest entries only for listed sites, the
    /// "PlayMode" tag word, the contest flag bytes of ProductUpdate) are traced in trace/0695/gallery-695.md and
    /// implemented in <see cref="Gallery"/>, <see cref="Store.Gallery"/>, <see cref="Ratings"/> and
    /// <see cref="Contests"/>.</item>
    /// <item><b>0.695 numbers:</b> the sub numbers in the items above are the 0.589 client's (with its function
    /// addresses). The 0.695 client inserted TemplateProductCreate at sub 0x30 and moved every sub ≥ 0x30 up by one
    /// [P] (trace/0695/packets-695.md "Changed layouts" A4; S→C dispatcher FUN_143258f50 cases 0x31..0x4E vs 0.589
    /// FUN_1431adf70 0x30..0x4D; re/out/packet_ids_695.tsv): ProductCreateResponse 0x31, ProductRemove 0x32,
    /// ProductUpdate 0x34, FollowCreator 0x35, UnFollowCreator 0x36, RequestAccountStore 0x37, AccountStore 0x39,
    /// AddFollowing 0x3A, RemoveFollowing 0x3B, Search 0x3E / 0x3F, GetProductForReview 0x40, ReviewProductResponse 0x41,
    /// SubmitProductReview 0x43 / 0x44, ModifyScreenshotsResponse 0x46, PointOfNeedRequest 0x47, ModifyScreenshots 0x4A,
    /// IsTemplateValid 0x4D / 0x4E. The constants here are those 0.695 numbers, read and written as the client sends
    /// them (the server speaks the 0.695 numbering natively since 2026-10-17, docs/protocol.md §1.6; before that a header
    /// translation at the gateway, ClientWire695, turned them into the 0.589 numbers, and until 2026-10-16 FromWire /
    /// ToWire here shifted them a second time). 0.695's new 0x30 TemplateProductCreate is mapped by
    /// <see cref="FromWire"/> onto 0x2F ProductCreate: its body starts with the same ProductCreate body
    /// (packets-695.md §12), which is all <see cref="CreateProduct"/> reads.</item>
    /// <item>Results reach the owner as mail with kind 0x10 (accepted) / 0x20 (rejected): the client writes the sender
    /// "PlayerStudio.MailUser", the subject and the body (PlayerStudio.AcceptedItem / RejectedItem, item = the mail
    /// subject) itself (FUN_143586ba0).</item>
    /// </list>
    /// </summary>
    public static class Commerce
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        // InGamePurchaseBase subs (u16), 0.695 numbers (class summary; coverage-695.md §0x45, 0.589 number in brackets).
        public const ushort SubAccountInfoRequest = 0x1A, SubAccountInfoResponse = 0x1B, SubProductCreate = 0x2F,
            SubProductCreateResponse = 0x31 /* 0x30 */, SubGetProductForReview = 0x40 /* 0x3F */,
            SubReviewProductResponse = 0x41 /* 0x40 */, SubSubmitProductReview = 0x43 /* 0x42 */,
            SubSubmitProductReviewResponse = 0x44 /* 0x43 */, SubProductRemove = 0x32 /* 0x31 */,
            SubProductUpdate = 0x34 /* 0x33 */, SubRequestAccountStore = 0x37 /* 0x36 */,
            SubAccountStoreResponse = 0x39 /* 0x38 */;

        /// <summary>ServerStatusRequest 0x0D → ServerStatusResponse 0x0E (same numbers in 0.589 and 0.695).</summary>
        public const ushort SubServerStatusRequest = 0x0D, SubServerStatusResponse = 0x0E;

        /// <summary>World map site search: CommerceSearch 0x3E / CommerceSearchResponse 0x3F (0.589: 0x3D / 0x3E).</summary>
        public const ushort SubCommerceSearch = 0x3E, SubCommerceSearchResponse = 0x3F;

        /// <summary>
        /// 0.695's CommerceTemplateProductCreate (sub 0x30, 0.695 only; packets-695.md §12, coverage-695.md §0x45 "new").
        /// </summary>
        public const ushort SubTemplateProductCreate695 = 0x30;

        /// <summary>
        /// An InGamePurchase sub as the client sent it → the number this class handles: the same, except 0.695's
        /// TemplateProductCreate 0x30, mapped onto 0x2F ProductCreate (packets-695.md §12: same body first). The session
        /// parameter is kept for the callers.
        /// </summary>
        public static ushort FromWire(WorldSession session, ushort sub)
            => sub == SubTemplateProductCreate695 ? SubProductCreate : sub;

        /// <summary>
        /// IsTemplateValid (0x4D; 0.589 0x4C, built in FUN_1431b7c20): before listing a design that contains other designs
        /// the client asks {u64 design, u32 n, n x u64 inner design} and holds its ProductCreate until
        /// IsTemplateValidResponse (0x4E; 0.589 0x4D, reader FUN_143183180 / 0.695 FUN_14322dc40, handler FUN_1431ad850)
        /// {u64 design, bool}: true sends the held ProductCreate, false fails with 0x16
        /// "TemplateContainsModifiedSubTemplate".
        /// </summary>
        public const ushort SubIsTemplateValid = 0x4D, SubIsTemplateValidResponse = 0x4E;

        /// <summary>
        /// The world map's search bar (Lua Ui.WorldMapClaimTagFilterUpdate → FUN_142cef090 → FUN_1431c31e0):
        /// CommerceSearch {string query, u32 1}. Answered with CommerceSearchResponse (0x3F; 0.589 0x3E, reader FUN_143184530,
        /// handler FUN_1431a8470 → the map's filter FUN_142ceb420 and WorldMap.ClaimFiltersSearchBar NumResults):
        /// {string query, u32 n × {u32, u32}, u32 count, u32 n × u64 site} — the sites on this landscape whose name or
        /// tags hold every word.
        /// </summary>
        private static void Search(WorldSession session, byte[] body)
        {
            string query = "";
            try { query = new GamePacketReader(body).ReadString() ?? ""; }
            catch (Exception) { }
            if (query.Length > 100)
                query = query[..100];
            List<ulong> sites = Claims.Search(session.ZoneId, query);
            Send(session, SubCommerceSearchResponse, w =>
            {
                w.Write(query);
                w.Write(0u);
                w.Write((uint)sites.Count);
                w.Write((uint)sites.Count);
                foreach (ulong site in sites)
                    w.Write(site);
            });
            log.Info($"{session.CharacterName} searched the map for \"{query}\": {sites.Count} site(s).");
        }

        /// <summary>Mail kinds (MailModel.Kind) for review results.</summary>
        public const uint MailAccepted = 0x10, MailRejected = 0x20;
        /// <summary>Mail kind "CS Review" (0x40 → PlayerStudio.CSReviewItem): a product the peers rejected went to staff.</summary>
        public const uint MailStaffReview = 0x40;
        /// <summary>SubmitProductReview verdict for an approval; every other value rejects.</summary>
        private const uint VerdictApprove = 1;
        /// <summary>ProductItem types: a design, a build site (gallery).</summary>
        public const uint ItemDesign = 2, ItemClaim = 3;
        /// <summary>
        /// Lowest price of any design (the client's own check, FUN_1431b7c20; 0.695 FUN_143262c00: price &lt; 0x19). A
        /// design's own minimum can be higher (<see cref="StudioRules.MinimumPrice"/>: its TemplateValue).
        /// </summary>
        private const uint MinPrice = StudioRules.MinPrice;
        /// <summary>
        /// Highest price of a design: the client's own ceiling, StringHashToValue "PlayerStudio.MaxListingValue^29600^"
        /// (Resources/StringHashToValue.txt, 0.695), which PlayerStudioWindow reads as _maxListingFee and turns the price
        /// field red above (PlayerStudioWindowDocumentClass.as handlePriceTextChange) [P]. The client still sends a higher
        /// price (ProductSetPrice FUN_143263c60 only checks the minimum), so the server refuses it as not valid (0xC) or
        /// keeps the old price on an update [I: live's server-side check is not seen].
        /// </summary>
        public const uint MaxPrice = StudioRules.MaxPrice;

        private static WorldServerConfiguration Config =>
            Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config;

        /// <summary>Product statuses (PlayerStudioProductModel.Status).</summary>
        public const byte Pending = 0, Listed = 1, Rejected = 2, Removed = 3;
        /// <summary>CommerceProductCreateResponse codes.</summary>
        private const uint Created = 1, PriceTooLow = 8, ItemNotFound = 9, TemplateNotOwned = 0xB, NotValid = 0xC, Duplicate = 0xF;
        /// <summary>
        /// CommerceProductCreateResponse 0x10 "CommerceCreateProductCouldNotAffordListingFee" (FUN_14319fc60 case 0x10; text
        /// 563262 "You do not have enough Station Cash to pay the listing fee!"): the client's own refusal when its wallet
        /// is short (FUN_143262c00 → FUN_14324ab40(0x10)), and the server's when the Lumens are gone by the time it charges.
        /// </summary>
        public const uint CouldNotAffordListingFee = 0x10;

        /// <summary>
        /// The InGamePurchase subs the zone answers itself, not the shared commerce back end
        /// (trace/architecture/world-channel-service.md §3.1): the world map search (0x3E, this landscape's sites), site
        /// screenshots (0x4A, sent on Zone) and IsTemplateValid (0x4D, the player's own designs). Upkeep (PointOfNeed 0x47
        /// type 1) stays in InteractionHandler (Claims).
        /// </summary>
        public static bool IsZoneSub(ushort sub)
            => sub is SubCommerceSearch or SiteScreenshots.ModifyRequestSub or SubIsTemplateValid;

        /// <summary>An InGamePurchase request the zone answers (<see cref="IsZoneSub"/>).</summary>
        public static bool HandleZone(WorldSession session, ushort sub, byte[] body)
        {
            switch (sub)
            {
                case SubCommerceSearch:
                    Search(session, body);
                    return true;
                case SiteScreenshots.ModifyRequestSub:
                    SiteScreenshots.HandleModify(session, body);
                    return true;
                case SubIsTemplateValid:
                {
                    // The design must be the player's own; its inner designs are part of it as saved.
                    ulong design = body.Length >= 8 ? BitConverter.ToUInt64(body, 0) : 0;
                    bool valid = design != 0 && Templates.FindDesign(session.PlayerGuid, design) != null;
                    Send(session, SubIsTemplateValidResponse, w =>
                    {
                        w.Write(design);
                        w.Write(valid);
                    });
                    log.Info($"{session.CharacterName} asked whether design {design} may be listed: {valid}.");
                    return true;
                }
                default:
                    return false;
            }
        }

        /// <summary>In-game store (cash shop) wallet: WalletInfoRequest / WalletInfoResponse.</summary>
        public const ushort SubWalletInfoRequest = 0x0B, SubWalletInfoResponse = 0x0C;

        /// <summary>
        /// WalletInfoRequest 0x0B → WalletInfoResponse 0x0C {u32 result (1 ok), wallet {u8, u32 balance, u32, string,
        /// string, u8}} (client FUN_143185620): the player's Lumens while the listing fee is charged
        /// (<see cref="WalletHoldsLumens"/>), else an empty wallet. The zone answers it in both commerce modes: the
        /// Lumens are this world's.
        /// </summary>
        public static void SendWalletInfo(WorldSession session)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            // WalletInfoResponse (0x0C; 0.589 reader FUN_143185620 / handler FUN_1431adaf0; 0.695 handler FUN_143258ad0
            // with the wallet reader FUN_1432300e0, same layout): u32 result (1 = ok: only then is the wallet stored), then
            // u8 (+0x59), u32 (+0x50), u32 (+0x54), string (+0), string (+0x28), u8 (+0x58). The handler stores the first
            // u32 as the BALANCE (FUN_143a3bb70 → wallet +0x58 = manager +0x22bc10, when ≥ 0), the second u32 at +0x5c
            // (FUN_143a3bb90, when > 0), the first string at +8 (FUN_143a3bb80) and the last u8 as "zip code on file"
            // (FUN_143a3bba0 → manager +0x22bc18: with it a US account may create Player Studio products, FUN_1431b4c10);
            // then Lua GameEvents:OnWalletInfo(ok, balance) and, on success, the balance as the Character.Currencies row
            // ItemID -1 "DBC" (FUN_142fd19b0: the inventory's Daybreak Cash counter, InventoryDocumentClass.as
            // DBCurrencyDisplay) [P]. Submitting a Player Studio product compares this balance with the listing fee
            // (FUN_143262c00: "can't afford listing fee! Balance=%d Fee=%d" → status 0x10) [P], so while the fee is charged
            // the balance is the player's Lumens, the currency the fee is in (SendConfiguration) — which the inventory's
            // Daybreak Cash counter then shows as well.
            w.WriteUShort(SubWalletInfoResponse);
            w.Write(1u);
            w.Write((byte)0);
            w.Write(WalletHoldsLumens ? Math.Min(session.Coins, (uint)int.MaxValue) : 0u);
            w.Write(0u);
            w.Write("");
            w.Write("");
            w.Write((byte)1);
            session.EnqueueMessage(ZoneMessageOpcode.InGamePurchaseBase, output.ToArray());
        }

        /// <summary>
        /// Does the client's wallet carry the player's Lumens? Only while the listing fee is charged
        /// (<see cref="ListingFeeCharged"/>): the client checks the fee against the wallet. Otherwise the wallet stays
        /// empty, as before (no Daybreak Cash on this server).
        /// </summary>
        public static bool WalletHoldsLumens => ListingFeeCharged;

        /// <summary>
        /// The player's Lumens changed (WorldSession.SendCurrencies: at world entry and on every change of the coin
        /// stack): the wallet is re-sent unasked while it carries them, so the client's own listing-fee check sees the
        /// current balance. The client asks for the wallet only from its store screens (Lua
        /// InGamePurchaseStoreScreen.RequestWalletInfo FUN_1435ea7e0; the browser's OnHide FUN_143261420), and its
        /// handler takes an unasked reply like any other (no request id) [P]. One ~25-byte packet beside the currency
        /// update.
        /// </summary>
        public static void WalletChanged(WorldSession session)
        {
            if (WalletHoldsLumens)
                SendWalletInfo(session);
        }

        /// <summary>CommerceConfiguration (S→C, no request; the same sub in 0.589 and 0.695).</summary>
        public const ushort SubCommerceConfiguration = 0x2E;

        /// <summary>Is the Player Studio listing fee charged? WorldServer.json PlayerStudioListingFee, never with FreeStore.</summary>
        public static bool ListingFeeCharged => (Config?.PlayerStudioListingFee ?? true) && !Store.IsFree;

        /// <summary>
        /// Live's minimum listing fee, 100 Daybreak Cash (Fairan [DEV], Update Notes 18 Jun 2014,
        /// design/forum/patches/2014-06-18-41434); still 100 DBC in 2016 (Syrah, 20 Oct 2016, in Game Update 14 Oct
        /// 2016, 2016-10-14-58029 — the launch-era value, briefly switched off then while a bug was fixed); the wiki's
        /// one-off fee of one US dollar (Player Studio) is the same 100 at $0.01 a Daybreak Cash [W].
        /// </summary>
        private const uint LiveMinimumFee = 100;
        /// <summary>
        /// Live's fee percent of the price: 10 % of the price the creator set (Update Notes 18 Jun 2014); a player paid
        /// 604 to list a 6,042 SC design (Hotfix Notes 20 Jun 2014) [W]; not seen changed by 2016 [I].
        /// </summary>
        private const uint LivePercent = 10;

        /// <summary>The minimum listing fee in Lumens: live's 100 DBC at the store's rate (<see cref="Store.LumensFor"/>: 130).</summary>
        public static uint MinimumListingFee => ListingFeeCharged ? Store.LumensFor(LiveMinimumFee) : 0;

        /// <summary>
        /// The fee percent as sent, in Lumens per 100 of price: live's 10 % × LumensPerDaybreakCash (13 at 1.3), so the
        /// fee comes out in Lumens while the price stays the creator's (Daybreak Cash) figure. The wire field is a whole
        /// number: another rate is rounded to the nearest whole percent [I].
        /// </summary>
        public static uint ListingFeePercent
        {
            get
            {
                if (!ListingFeeCharged)
                    return 0;
                double rate = Config?.LumensPerDaybreakCash ?? 1.3;
                if (rate <= 0 || double.IsNaN(rate) || double.IsInfinity(rate))
                    rate = 1.3;
                return (uint)Math.Min(1000, Math.Round(LivePercent * rate));
            }
        }

        /// <summary>
        /// The listing fee in Lumens for a design priced <paramref name="price"/>, exactly as the client computes and shows
        /// it from <see cref="SendConfiguration"/>'s values (<see cref="StudioRules.ListingFee"/>); 0 when not charged.
        /// 25 → 130, 1,000 → 130, 2,000 → 260, 29,600 → 3,848 Lumens at the default rate.
        /// </summary>
        public static uint ListingFee(uint price)
            => ListingFeeCharged ? StudioRules.ListingFee(price, MinimumListingFee, ListingFeePercent) : 0;

        /// <summary>
        /// CommerceConfiguration 0x2E (S→C; 0.695 reader FUN_14322db60, handler FUN_143252250, dispatcher FUN_143258f50
        /// case 0x2e; 0.589 FUN_1431830a0 / FUN_1431a7380, same) [P]: {u32 minimum fee → manager +0x230578, u32 fee percent
        /// → +0x23057c, u32 minimum price → +0x230580}. ProductInit (FUN_1432636a0) copies them into each new product
        /// (+0x2c, +0x30, +0x28); ProductAddItem (FUN_143263990) raises the minimum price to the design's TemplateValue;
        /// the submission window's data source MarketingData.CommerceAccount.CreateProduct (FUN_14370d890) shows MinPrice,
        /// MinListingFee, ListingFeePercent and ListingFee = max(minimum fee, percent × 0.01 × price) ("[ Listing Fee: n ]",
        /// "[ Minimum Price: n ]", PlayerStudioWindowDocumentClass.as); ProductSetPrice (FUN_143263c60) refuses a price
        /// below either minimum; ProductCreate (FUN_143262c00) refuses the submission with 0x10 when the wallet balance
        /// is below the fee [P]. Sent at world entry: the minimum price is always live's 25; the fee is live's in Lumens
        /// (<see cref="MinimumListingFee"/>, <see cref="ListingFeePercent"/>), or 0 / 0 when it is not charged (FreeStore,
        /// PlayerStudioListingFee off). Never sent before 2026-10-17: the client then had 0 in all three.
        /// </summary>
        public static void SendConfiguration(WorldSession session)
            => Send(session, SubCommerceConfiguration, w =>
            {
                w.Write(MinimumListingFee);
                w.Write(ListingFeePercent);
                w.Write(StudioRules.MinPrice);
            });

        /// <summary>
        /// Takes a design's listing fee (<see cref="ListingFee"/>) on the world thread when the server accepts the
        /// submission; false (nothing taken) when the player cannot pay it. Live: "a non-refundable listing fee"
        /// (client text 563260), kept even if peer review turns the item down ("the listing fee is kept, even if the item
        /// doesn't get approved for sale initially", a player quoting the 18 Jun 2014 livestream) [W]; only a submission
        /// the server refuses gets it back (<see cref="RefundListingFee"/>).
        /// </summary>
        public static bool ChargeListingFee(WorldSession session, uint fee)
            => fee == 0 || session.ChangeCoins(-(long)fee);

        /// <summary>Gives back a listing fee taken for a submission the server then refused.</summary>
        public static void RefundListingFee(WorldSession session, uint fee, string why)
        {
            if (fee == 0)
                return;
            session.ChangeCoins(fee);
            log.Info($"{session.CharacterName}'s Player Studio listing fee of {fee} Lumens returned: {why}.");
        }

        /// <summary>
        /// An InGamePurchase request for the commerce back end (reached through
        /// <see cref="WorldChannel.ICommerceService.InGamePurchaseRequest"/>; the zone's own subs go to
        /// <see cref="HandleZone"/>, upkeep and the wallet are routed before this).
        /// </summary>
        public static bool Handle(WorldSession session, ushort sub, byte[] body)
        {
            switch (sub)
            {
                case SubAccountInfoRequest:
                    Send(session, SubAccountInfoResponse, w =>
                    {
                        w.Write(1u);          // ok
                        w.Write("US");        // country: Player Studio sellers had to be US accounts
                        w.Write("USD");
                        w.Write(true);        // zip code on file
                        w.Write("");
                    });
                    return true;
                case SubProductCreate:
                    CreateProduct(session, body);
                    return true;
                case SubGetProductForReview:
                    SendProductForReview(session);
                    return true;
                case SubSubmitProductReview:
                    SubmitReview(session, body);
                    return true;
                case SubRequestAccountStore:
                    SendAccountStore(session);
                    return true;
                case SubProductRemove:
                    RemoveProduct(session, body);
                    return true;
                case SubProductUpdate:
                    UpdateProduct(session, body);
                    return true;
                case SubFollowCreator:
                case SubUnFollowCreator:
                    Follow(session, sub == SubFollowCreator, body);
                    return true;
                case SubServerStatusRequest:
                    // ServerStatusResponse 0x0E {bool}: 0.695 handler FUN_143256750 reads u16 opcode, u16 sub and one byte,
                    // refuses anything after it, and calls Lua OnReceiveServerStatus(bool) [P] (0.589 FUN_1431ab880, same).
                    // True = the store service is up, as the commerce service answers (ProductService; false only from a zone
                    // that cannot reach it) [I: what live's bool meant, L124]. Sent by the Lua native
                    // InGamePurchaseStoreScreen.RequestServerStatus, which no shipped script calls (L124); answered so a page
                    // or script that does gets the same reply in both commerce modes. Until 2026-10-18 unanswered in-process.
                    Send(session, SubServerStatusResponse, w => w.Write(true));
                    return true;
                default:
                    log.Info($"Commerce request 0x{sub:X2} from {session.CharacterName}: {BitConverter.ToString(body)}");
                    return false;
            }
        }

        /// <summary>
        /// A ProductCreate the zone has read and checked (<see cref="CheckProduct"/>): item type (2 design, 3 build site),
        /// the item (design template id / claim id), price, the texts as sent (trimmed), and the site for a gallery listing.
        /// </summary>
        public sealed record ProductDraft(uint ItemType, ulong ItemId, uint Price, string Name, string Description, string Tags,
            string Headline, ClaimModel Site)
        {
            public bool Gallery => ItemType == ItemClaim;
        }

        /// <summary>
        /// The zone's half of a ProductCreate (body as in the class summary): readable, an item of the right type, a name,
        /// a design's price 25 … <see cref="MaxPrice"/> (the creator's price, the first u32), the item exists (9) and is the
        /// player's (0xB; a design also their own work, <see cref="Templates.IsSellable"/>), a design's price is at least its
        /// own minimum (8; <see cref="StudioRules.MinimumPrice"/>), a build site has its three gallery tags (0xC). The listing
        /// fee is the callers' (<see cref="ListingFee"/>). Null with the CreateResponse status to send when refused (the refusal is logged as before). Shared by the
        /// in-process <see cref="CreateProduct"/> and the commerce service's client (RemoteCommerceService), which hands on
        /// only what passes here: the templates and claims are the zone's.
        /// </summary>
        public static ProductDraft CheckProduct(WorldSession session, byte[] body, out uint refusal)
        {
            uint price;
            bool gallery;
            string name, description, tags, headline;
            var items = new List<(ulong Guid, uint Type)>();
            refusal = NotValid;
            try
            {
                var r = new GamePacketReader(body);
                price = r.ReadUInt();
                r.ReadUInt(); r.ReadUInt();
                name = (r.ReadString() ?? "").Trim();
                description = (r.ReadString() ?? "").Trim();
                tags = (r.ReadString() ?? "").Trim();
                uint n = r.ReadUInt();
                for (uint i = 0; i < n && i < 16; i++)
                {
                    // ProductItem (writer FUN_143181750): u64 guid, u32 type, u32 quantity, u64, u8, string.
                    ulong guid = r.ReadULong();
                    uint type = r.ReadUInt();
                    r.ReadUInt();
                    r.ReadULong();
                    r.ReadByte();
                    r.ReadString();
                    items.Add((guid, type));
                }
                gallery = r.ReadByte() != 0;
                headline = (r.ReadString() ?? "").Trim();
            }
            catch (Exception e)
            {
                log.Warn($"Unreadable product from {session.CharacterName} ({e.Message}): {BitConverter.ToString(body)}");
                return null;
            }
            uint itemType = gallery ? ItemClaim : ItemDesign;
            var item = items.FirstOrDefault(i => i.Type == itemType);
            if (item.Guid == 0 || name.Length == 0 || (!gallery && (price < MinPrice || price > MaxPrice)))
                return null;
            ClaimModel site = gallery ? Claims.Get(item.Guid) : null;
            TemplateModel design = null;
            ulong owner;
            if (gallery)
                owner = site?.OwnerGuid ?? 0;
            else
            {
                using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
                design = context.Template.FirstOrDefault(t => t.Id == item.Guid);
                owner = design?.OwnerGuid ?? 0;
            }
            if (owner == 0)
            {
                refusal = ItemNotFound;
                return null;
            }
            if (owner != session.PlayerGuid || (design != null && !Templates.IsSellable(design)))
            {
                // A copy taken from Player Studio (or a Player Design item from someone else) is in the player's designs
                // but not their work: the client refuses it itself (ProductAddItem FUN_143263990 needs the own-design flag
                // +0xf4) [P]; the server answers as for a design that isn't theirs (0xB) [I].
                refusal = TemplateNotOwned;
                return null;
            }
            if (design != null && price < StudioRules.MinimumPrice(Templates.MaterialCount(design.Materials)))
            {
                // The design's own minimum (its TemplateValue, 1 % of the voxels): the client raises the price to it
                // (FUN_143263990) and refuses a lower one (FUN_143263c60, FUN_143262c00 → 0xC), so only a modified client
                // gets here; 8 = CommerceCreateProductPriceTooLow, text 563266 "The price of your item is too low.
                // Please set a price above the minimum." [I: the status live used is not seen].
                log.Info($"{session.CharacterName} priced design {item.Guid} at {price}, below its minimum of {StudioRules.MinimumPrice(Templates.MaterialCount(design.Materials))}.");
                refusal = PriceTooLow;
                return null;
            }
            if (gallery && !Gallery.IsTagged(site.Tags))
            {
                // Launch rule (P4590 / P4591, 13 Apr 2016): a site is listed only with its three gallery tags set. The
                // client disables Publish to Gallery until all three drop-downs are chosen (ClaimManagementWindow
                // GALLERY invalidation) and refuses empty tags itself (Claims.Error.TagsAreInvalid), so this only
                // catches a tag string the drop-downs could not have produced.
                log.Info($"{session.CharacterName} tried to publish build site {item.Guid} without its three gallery tags (\"{site.Tags}\").");
                return null;
            }
            refusal = 0;
            return new ProductDraft(itemType, item.Guid, price, name, description, tags, headline, site);
        }

        /// <summary>
        /// A Player Studio product: one of the player's designs (stored, waiting for peer review), or one of their build
        /// sites published to the gallery (free, listed at once).
        /// </summary>
        private static void CreateProduct(WorldSession session, byte[] body)
        {
            ProductDraft draft = CheckProduct(session, body, out uint refusal);
            if (draft == null)
            {
                SendCreateResponse(session, refusal, "");
                return;
            }
            bool gallery = draft.Gallery;
            using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
            if (context.PlayerStudioProduct.Any(p => p.TemplateId == draft.ItemId && p.ItemType == draft.ItemType
                && (p.Status == Pending || p.Status == Listed)))
            {
                SendCreateResponse(session, Duplicate, "");
                return;
            }
            // A gallery listing's tags are the site's three tag words; the client appends "PlayMode" when the site is in
            // Play Mode (FUN_142d99b70) and the server takes that state live instead (Gallery.CleanTags).
            var product = new PlayerStudioProductModel
            {
                OwnerGuid = session.PlayerGuid, TemplateId = draft.ItemId, ItemType = draft.ItemType, Name = Clip(draft.Name, 128),
                Description = Clip(draft.Description, 1024), Headline = Clip(draft.Headline, 256),
                Tags = Clip(gallery ? Gallery.CleanTags(draft.Tags) : draft.Tags, 512),
                Price = gallery ? 0 : draft.Price, Status = gallery ? Listed : Pending, CreatedAt = DateTime.UtcNow,
                ListedAt = gallery ? DateTime.UtcNow : null
            };
            // The listing fee (designs only: a gallery listing is free, FUN_143262c00 skips the fee for it), taken just
            // before the product is stored and given back if that fails.
            uint fee = gallery ? 0 : ListingFee(draft.Price);
            if (!ChargeListingFee(session, fee))
            {
                SendCreateResponse(session, CouldNotAffordListingFee, "");
                log.Info($"{session.CharacterName} could not pay the {fee}-Lumen listing fee for design {draft.ItemId} ({session.Coins} Lumens).");
                return;
            }
            context.PlayerStudioProduct.Add(product);
            try
            {
                context.SaveChanges();
            }
            catch (Exception e)
            {
                RefundListingFee(session, fee, "the product could not be saved");
                log.Error($"{session.CharacterName}'s Player Studio product could not be saved: {e.Message}");
                SendCreateResponse(session, 0, "");
                return;
            }
            SendCreateResponse(session, Created, "");
            MarketChanged(product);
            log.Info(gallery
                ? $"{session.CharacterName} published build site {draft.ItemId} to the gallery (product {product.Id} '{product.Name}')."
                : $"{session.CharacterName} submitted Player Studio product {product.Id} '{product.Name}' (design {draft.ItemId})"
                  + (fee > 0 ? $" for a listing fee of {fee} Lumens." : "."));
        }

        /// <summary>
        /// AccountStore 0x39 (0.589 0x38, reader FUN_143182f10; 0.695 FUN_14322d9d0): {u64, bool, u32 n × {u64 creator id, string name} (followings,
        /// 0.695 FUN_1432333c0), u32 n purchases
        /// (FUN_143184c70), u32 n {string, u64}, u64, u64, u64, u32, u32} (FUN_143181d00), u32 n strings (recent
        /// searches), u32 n bundles (FUN_143182020), u32 n × Product (the player's own: the client's list at manager
        /// +0x236e00). Purchases and bundles belong to the store side and are empty; the followings are the player's
        /// (<see cref="Follow"/>). The player's products
        /// waiting for review or listed; rejected and removed ones are left out (the client would take them for live
        /// ones: it only skips Status -1).
        /// </summary>
        private static void SendAccountStore(WorldSession session)
        {
            string creator = session.CharacterName ?? "";
            ulong owner = session.PlayerGuid;
            // The two reads on a pool thread, the answer on the world thread (review 2026-10-18 #8). Nothing is answered when
            // they fail: the client asks again (as for an unreachable commerce service).
            InBackground<(List<PlayerStudioProductModel> Products, List<(ulong, string)> Followed)>(session, "account store", () =>
            {
                List<PlayerStudioProductModel> products;
                using (var context = DatabaseManager.Instance.CharacterDatabase.NewContext())
                    products = context.PlayerStudioProduct.AsNoTracking().Where(p => p.OwnerGuid == owner
                        && (p.Status == Pending || p.Status == Listed)).OrderBy(p => p.Id).ToList();
                return (products, ShowcaseFollow.Creators(owner));
            }, default, result =>
            {
                if (result.Products != null)
                    SendAccountStore(session, creator, result.Products, result.Followed);
            });
        }

        private static void SendAccountStore(WorldSession session, string creator, List<PlayerStudioProductModel> mine,
            List<(ulong Id, string Name)> followed)
        {
            Send(session, SubAccountStoreResponse, w =>
            {
                w.Write(0ul);
                w.Write(false);
                w.Write((uint)followed.Count);
                foreach ((ulong id, string name) in followed)
                {
                    w.Write(id);
                    w.Write(name ?? "");
                }
                w.Write(0u);
                w.Write(0u);
                w.Write(0ul);
                w.Write(0ul);
                w.Write(0ul);
                w.Write(0u);
                w.Write(0u);
                w.Write(0u);
                w.Write(0u);
                w.Write((uint)mine.Count);
                foreach (PlayerStudioProductModel p in mine)
                    WriteProduct(w, p, creator);
            });
        }

        /// <summary>
        /// Following a Player Studio creator (agent-leads-sweep.md L123) [P layouts]: the Lua natives
        /// InGamePurchaseStoreScreen.FollowCreator / UnFollowCreator (FUN_1435e9df0 / FUN_1435e9e70, luabind_695) send
        /// FollowCreator (0x35, 0.589 0x34; FUN_14324d270, only when the creator is not followed yet) /
        /// UnFollowCreator (0x36, 0.589 0x35; FUN_1432706c0, only when followed) {u64 creator id} on channel 1 (writers
        /// FUN_1432347f0 / FUN_143235af0: FUN_14322b8a0 header, FUN_142c02460 u64 +0x18). The answers:
        /// <list type="bullet">
        /// <item>AddFollowing (0x3A, 0.589 0x39; reader FUN_14322da80, handler part_0093 ~7340): u64 FollowingId, bool
        /// result, string FollowingName — on success the client adds the creator to its following map (manager +0x232c88,
        /// FUN_143af9160), refreshes the store screen (FUN_14370e410) and calls Lua OnCommerceAccountAddFollowing;</item>
        /// <item>RemoveFollowing (0x3B, 0.589 0x3A; reader FUN_14322de50, handler part_0093 ~8005): u64 FollowingId,
        /// bool result — on success the creator leaves the map (FUN_143afb930).</item>
        /// </list>
        /// The creator id is what the store screen passes (no shipped script calls the natives, so its source is not
        /// traced; the character guid of a product's owner is the server's creator id [I]); the name is that character's.
        /// The list goes back to the client in AccountStore (followings: u32 n × {u64 id, string name}, FUN_1432333c0).
        /// In-process the list is kept in the character database (<see cref="ShowcaseFollow"/>, table showcase_follow,
        /// since 2026-10-18; before, in memory until the restart), shared with the Showcase page's Following tab and read by
        /// the daily following digest; the commerce service keeps its own (its following table).
        /// </summary>
        public const ushort SubFollowCreator = 0x35, SubUnFollowCreator = 0x36, SubAddFollowing = 0x3A, SubRemoveFollowing = 0x3B;

        private static void Follow(WorldSession session, bool follow, byte[] body)
        {
            if (body.Length < 8)
                return;
            ulong creator = BitConverter.ToUInt64(body, 0);
            ulong follower = session.PlayerGuid;
            // The database work (2-3 queries) on a pool thread, the answer on the world thread (review 2026-10-18 #8: a
            // client sending these in a loop held up the world tick). A failure answers "not done".
            if (follow)
                InBackground<(bool Ok, string Name)>(session, "follow", () =>
                {
                    string name = creator == 0 ? "" : DatabaseManager.Instance.CharacterDatabase.GetCharacterById(creator).GetAwaiter().GetResult()?.Name ?? "";
                    bool ok = creator != 0 && creator != follower && ShowcaseFollow.AddCreator(follower, creator, name).Ok;
                    return (ok, name);
                }, (false, ""), result =>
                {
                    Send(session, SubAddFollowing, w =>
                    {
                        w.Write(creator);
                        w.Write(result.Ok);
                        w.Write(result.Name);
                    });
                    log.Info($"{session.CharacterName} follows creator {creator} ({result.Name}): {result.Ok}.");
                });
            else
                InBackground(session, "unfollow", () => ShowcaseFollow.RemoveCreator(follower, creator), false, ok =>
                {
                    Send(session, SubRemoveFollowing, w =>
                    {
                        w.Write(creator);
                        w.Write(ok);
                    });
                    log.Info($"{session.CharacterName} stops following creator {creator}: {ok}.");
                });
        }

        /// <summary>Most commerce database jobs one player may have waiting (more are answered with the fallback at once).</summary>
        private const int MaxBackgroundJobs = 8;

        /// <summary>Each player's newest commerce database job and how many are waiting (<see cref="InBackground"/>).</summary>
        private static readonly Dictionary<WorldSession, (Task Last, int Waiting)> backgroundJobs = new();

        /// <summary>
        /// Runs a commerce request's database work on a pool thread and its answer on the world thread, with the result
        /// (WorldSession.EnqueueEvent: run at the session's next flush, in the order queued). One player's jobs run one after
        /// another in arrival order (a follow then an unfollow are stored in that order); <paramref name="fallback"/> is the
        /// result when the work throws, or at once when <see cref="MaxBackgroundJobs"/> are already waiting.
        /// </summary>
        private static void InBackground<T>(WorldSession session, string what, Func<T> work, T fallback, Action<T> answer)
        {
            Task<T> job;
            lock (backgroundJobs)
            {
                backgroundJobs.TryGetValue(session, out var queue);
                if (queue.Waiting >= MaxBackgroundJobs)
                {
                    log.Info($"{session.CharacterName}: {what} refused, {queue.Waiting} commerce requests still waiting.");
                    job = Task.FromResult(fallback);
                }
                else
                {
                    job = (queue.Last ?? Task.CompletedTask).ContinueWith(_ =>
                    {
                        try
                        {
                            return work();
                        }
                        catch (Exception e)
                        {
                            log.Warn($"{session.CharacterName}: {what} failed: {e.Message}");
                            return fallback;
                        }
                    }, TaskScheduler.Default);
                    backgroundJobs[session] = (job, queue.Waiting + 1);
                    job.ContinueWith(_ =>
                    {
                        lock (backgroundJobs)
                            if (backgroundJobs.TryGetValue(session, out var now))
                            {
                                if (now.Waiting <= 1)
                                    backgroundJobs.Remove(session);
                                else
                                    backgroundJobs[session] = (now.Last, now.Waiting - 1);
                            }
                    }, TaskScheduler.Default);
                }
            }
            session.EnqueueEvent(new LandmarkEmulator.Shared.Game.Events.TaskGenericEvent<T>(job, answer));
        }

        /// <summary>ProductRemove 0x32 (0.589 0x31) {u64 product}: the owner takes a product off (or a site off the gallery). No answer.</summary>
        private static void RemoveProduct(WorldSession session, byte[] body)
        {
            if (body.Length < 8)
                return;
            ulong id = BitConverter.ToUInt64(body, 0);
            PlayerStudioProductModel product;
            using (var context = DatabaseManager.Instance.CharacterDatabase.NewContext())
            {
                product = context.PlayerStudioProduct.FirstOrDefault(p => p.Id == id && p.OwnerGuid == session.PlayerGuid);
                if (product == null || product.Status == Removed)
                    return;
                product.Status = Removed;
                context.SaveChanges();
            }
            MarketChanged(product);
            if (product.ItemType == ItemClaim)
                Contests.Withdraw(session, product.TemplateId);   // an unlisted site cannot stay in a competition (text 576478)
            log.Info(product.ItemType == ItemClaim
                ? $"{session.CharacterName} unpublished build site {product.TemplateId} from the gallery (product {id} '{product.Name}')."
                : $"{session.CharacterName} removed Player Studio product {id} '{product.Name}'.");
        }

        /// <summary>
        /// ProductUpdate 0x34 (0.589: 0x33; writer FUN_143181020 = 0.695 FUN_14322ba80, unchanged): {u64 product (+0x18),
        /// string name (+0x38), string description (+0x50), u32 price (+0x68), string tags (+0x70), u8 +0xa2, u8 +0xa0
        /// <b>enter contest</b>, u8 +0xa3, u8 +0xa4, u8 +0xa1 <b>leave contest</b>, u64, u64, string contest id (+0x88),
        /// string (+0x20)}. No answer. Three senders (trace/0695/gallery-695.md §4):
        /// <list type="bullet">
        /// <item>the commerce tick, for products the client retagged: a published site's tags changed (SetClaimTags
        /// FUN_1435e3630), its Play Mode changed (ClaimInfoUpdate kind 0x13 handler, part_0069.c 14035) or Publish to
        /// Gallery was pressed for a site that already has a listing (FUN_1435e5a20) — tags = "&lt;site tags&gt;
        /// PlayMode" / "&lt;site tags&gt; " (FUN_142d99b70), name / description / price copied from the listing;</item>
        /// <item>Claims.EnterContest (FUN_1435e5e70 → FUN_1432628e0): name "", description = the entry's description,
        /// tags = the contest's "contest_..." tag, contest id in the trailing string, enter byte 1; then
        /// ClaimsPacketSubmitContestEntry;</item>
        /// <item>Claims.RemoveFromContest (FUN_1435e6240 → FUN_143262a40): all strings empty, leave byte 1.</item>
        /// </list>
        /// The server keeps the gallery listing's own tags (the "PlayMode" word is dropped: the live state is used), and
        /// keeps contest markers apart: the enter flag records the contest tag and hands the description to
        /// <see cref="Contests"/>, the leave flag withdraws the site's entries.
        /// </summary>
        private static void UpdateProduct(WorldSession session, byte[] body)
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
                log.Warn($"Unreadable product update from {session.CharacterName} ({e.Message}): {BitConverter.ToString(body)}");
                return;
            }
            PlayerStudioProductModel product;
            using (var context = DatabaseManager.Instance.CharacterDatabase.NewContext())
            {
                product = context.PlayerStudioProduct.FirstOrDefault(p => p.Id == id && p.OwnerGuid == session.PlayerGuid);
                if (product == null || product.Status == Removed)
                    return;
                if (enterContest || leaveContest)
                {
                    // Contest markers only: the listing's name, description and tags stay as they are.
                    string contestTag = enterContest && tags.StartsWith(Contests.TagPrefix, StringComparison.OrdinalIgnoreCase) ? tags : "";
                    product.Tags = Clip(Gallery.WithContestTags(product.Tags, leaveContest ? null : contestTag), 512);
                }
                else
                {
                    if (name.Length > 0)
                        product.Name = Clip(name, 128);
                    product.Description = Clip(description, 1024);
                    product.Tags = Clip(product.ItemType == ItemClaim
                        ? Gallery.WithContestTags(Gallery.CleanTags(tags), Gallery.ContestTagsOf(product.Tags)) : tags, 512);
                    // A design's price stays within its own minimum (TemplateValue) and the ceiling. Live charged the fee
                    // difference on a price increase (only the difference was due, Bonus Update Notes 31 Jul 2014); the
                    // 0.695 client has no prompt or wallet check for it (its only fee check is
                    // FUN_143262c00, ProductCreate), so none is charged here (OPEN-LEADS L148).
                    if (product.ItemType == ItemDesign && price <= MaxPrice && price >= StudioRules.MinimumPrice(Templates.MaterialCount(
                            context.Template.Where(t => t.Id == product.TemplateId).Select(t => t.Materials).FirstOrDefault())))
                        product.Price = price;
                }
                context.SaveChanges();
            }
            if (product.ItemType == ItemClaim && enterContest)
                Contests.EntryDescription(product.TemplateId, description);
            else if (product.ItemType == ItemClaim && leaveContest)
                Contests.Withdraw(session, product.TemplateId);
            log.Info($"{session.CharacterName} updated {(product.ItemType == ItemClaim ? "gallery listing" : "Player Studio product")} {id} '{product.Name}'"
                + (enterContest ? $" (entering a contest: \"{tags}\")" : leaveContest ? " (leaving its contests)" : "") + ".");
        }

        private static readonly object marketSync = new();
        private static HashSet<ulong> marketDesigns, galleryClaims;

        /// <summary>Loads which designs are on the marketplace and which sites are on the gallery (once).</summary>
        private static void LoadMarket()
        {
            if (marketDesigns != null)
                return;
            List<PlayerStudioProductModel> live;
            using (var context = DatabaseManager.Instance.CharacterDatabase.NewContext())
                live = context.PlayerStudioProduct.Where(p => p.Status == Pending || p.Status == Listed).ToList();
            galleryClaims = live.Where(p => p.ItemType == ItemClaim && p.Status == Listed).Select(p => p.TemplateId).ToHashSet();
            marketDesigns = live.Where(p => p.ItemType == ItemDesign).Select(p => p.TemplateId).ToHashSet();
        }

        /// <summary>
        /// A design with a product waiting for review or listed: TemplateBasicData IsOnMarketplace, which files it under
        /// the Designs window's Player Studio category.
        /// </summary>
        public static bool IsOnMarketplace(ulong design)
        {
            lock (marketSync)
            {
                LoadMarket();
                return marketDesigns.Contains(design);
            }
        }

        /// <summary>
        /// A build site published to the gallery: the Showcase gallery lists only these (with their tags), only these
        /// can be liked (P5630) or entered into a competition (text 576478), and only these can be visited from the
        /// gallery.
        /// </summary>
        public static bool IsOnGallery(ulong claimId)
        {
            lock (marketSync)
            {
                LoadMarket();
                return galleryClaims.Contains(claimId);
            }
        }

        /// <summary>A build site's gallery listing.</summary>
        public record Listing(ulong Claim, DateTime ListedAt, string ContestTags);

        /// <summary>
        /// Every build site published to the gallery, with when it was listed (the listing's creation; a site taken off
        /// and published again gets a new listing, so "rising stars" = the newest listings, P4670) and the contest tags
        /// its listing carries.
        /// </summary>
        public static Dictionary<ulong, Listing> Listings()
        {
            using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
            var result = new Dictionary<ulong, Listing>();
            foreach (var p in context.PlayerStudioProduct.Where(p => p.Status == Listed && p.ItemType == ItemClaim)
                .Select(p => new { p.TemplateId, p.CreatedAt, p.Tags }))
                result[p.TemplateId] = new Listing(p.TemplateId, DateTime.SpecifyKind(p.CreatedAt, DateTimeKind.Utc), Gallery.ContestTagsOf(p.Tags));
            return result;
        }

        /// <summary>A product was created, decided or removed: the caches, and the owner's design record (its category).</summary>
        private static void MarketChanged(PlayerStudioProductModel product)
        {
            bool live = product.Status == Pending || product.Status == Listed;
            lock (marketSync)
            {
                LoadMarket();
                if (product.ItemType == ItemClaim)
                {
                    if (live && product.Status == Listed) galleryClaims.Add(product.TemplateId);
                    else galleryClaims.Remove(product.TemplateId);
                }
                else if (live)
                    marketDesigns.Add(product.TemplateId);
                else
                    marketDesigns.Remove(product.TemplateId);
            }
            if (product.ItemType == ItemDesign && PlayerManager.FindByGuid(product.OwnerGuid) is WorldSession owner)
                Templates.SendUpdated(owner, product.TemplateId);
        }

        /// <summary>
        /// A listed Player Studio design for the Showcase's Player Studio page; <paramref name="Price"/> is the creator's
        /// price as they set it (Daybreak Cash on live; <see cref="StudioPrice"/> gives the Lumens a buyer pays).
        /// </summary>
        public record StudioDesign(ulong Product, string Name, string Description, string Tags, string Author, uint Voxels,
            ulong Template, uint Price);

        /// <summary>The designs peer review accepted (status Listed), newest first, with their makers' names.</summary>
        public static List<StudioDesign> ListedDesigns()
        {
            using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
            var products = context.PlayerStudioProduct.Where(p => p.Status == Listed && p.ItemType == ItemDesign)
                .OrderByDescending(p => p.CreatedAt).ToList();
            var names = DatabaseManager.Instance.CharacterDatabase.GetCharacterNames(products.Select(p => p.OwnerGuid).Distinct());
            var designs = context.Template.Where(t => products.Select(p => p.TemplateId).Contains(t.Id))
                .Select(t => new { t.Id, t.Materials }).ToDictionary(t => t.Id, t => t.Materials);
            return products.Where(p => designs.ContainsKey(p.TemplateId)).Select(p => new StudioDesign(p.Id, p.Name ?? "",
                p.Description ?? "", p.Tags ?? "", names.GetValueOrDefault(p.OwnerGuid, "?"),
                (uint)Templates.MaterialCount(designs[p.TemplateId]), p.TemplateId, p.Price)).ToList();
        }

        /// <summary>
        /// What a buyer pays for a Player Studio design, in Lumens (L148). On live the buyer paid the creator's price in
        /// Daybreak Cash: the creator sets it in the submission window (PlayerStudioWindow "Set Your Price" →
        /// InGamePurchaseStoreScreen.ProductSetPrice, FUN_143263c60 → commerce manager +0x24), ProductCreate carries it as
        /// its first u32 (0.695 FUN_143262c00 copies +0x24 into the packet) and ProductUpdate after the description [P];
        /// Player Studio designs are priced by their creators in DBC (wiki Player Studio, Showcase; patch notes
        /// P1149-P1212) [W]. This
        /// server has no Daybreak Cash, so the price becomes Lumens at the store's rate (<see cref="Store.LumensFor"/>,
        /// LumensPerDaybreakCash: 25 → 35, 100 → 130, 1000 → 1,300), as every other live DBC price
        /// (trace/0695/agent-store-entitlements.md §1). FreeStore on (<see cref="Store.IsFree"/>): free. The member store
        /// discount (Membership.PriceFor) is not applied: the price is the creator's, the discount is for the server's own
        /// store prices [I].
        /// </summary>
        public static uint StudioPrice(uint creatorPrice) => creatorPrice == 0 || Store.IsFree ? 0 : Store.LumensFor(creatorPrice);

        /// <summary>The creator's share of <paramref name="paid"/> Lumens at <paramref name="percent"/> % (rounded down).</summary>
        public static uint CreatorShare(uint paid, uint percent) => (uint)((ulong)paid * Math.Min(percent, 100u) / 100);

        /// <summary>
        /// Mails a creator their share of a sale: a system mail with the Lumens attached, as a vendor's earnings
        /// (Vendors.Buy); the mail window's Take puts them in the bag. Live paid creators quarterly in real money (paid
        /// out at the end of each quarter, Update Notes 18 Jun 2014) [W]; this server pays at
        /// once, in Lumens [I]. In-process sales and the commerce service's CommercePayout both end here.
        /// </summary>
        public static void MailCreatorShare(ulong creator, string design, string buyer, uint paid, uint share)
        {
            if (share == 0 || creator == 0)
                return;
            Social.SocialServices.Service.SendSystemMail(creator, "Player Studio", $"Sold: {design}",
                $"{buyer} bought your design \"{design}\" from Player Studio for {paid:N0} Lumens. Your share: {share:N0} Lumens, attached.",
                share);
            log.Info($"Player Studio: {share} Lumens mailed to creator 0x{creator:X} for \"{design}\" (bought by {buyer} for {paid}).");
        }

        /// <summary>The line the maker sees when they are online at the sale.</summary>
        public static string SaleNotice(string buyer, string design, uint paid, uint share)
            => paid == 0 ? $"{buyer} took your design \"{design}\" from Player Studio."
                : $"{buyer} bought your design \"{design}\" from Player Studio for {paid:N0} Lumens"
                  + (share > 0 ? $"; your share of {share:N0} Lumens is in your mail." : ".");

        /// <summary>
        /// Buys a listed Player Studio design (the Showcase's Player Studio page) for <see cref="StudioPrice"/> Lumens
        /// (nothing with FreeStore on): the buyer gets their own copy of the design, credited to its maker — the Designs
        /// window files it under Collected — once per product. The Lumens are taken on the world thread just before the copy
        /// is saved (given back if the save fails), as <see cref="Store.Take"/> does; the maker is mailed their share
        /// (<see cref="CreatorShare"/>, WorldServer.json PlayerStudioCreatorSharePercent) and told if online.
        /// </summary>
        public static (bool Ok, string Message) TakeDesign(WorldSession session, ulong productId)
        {
            TemplateModel copy;
            string maker, listing;
            ulong source, makerGuid;
            uint cost;
            using (var context = DatabaseManager.Instance.CharacterDatabase.NewContext())
            {
                PlayerStudioProductModel product = context.PlayerStudioProduct.FirstOrDefault(p => p.Id == productId);
                if (product == null || product.Status != Listed || product.ItemType != ItemDesign)
                    return (false, "That design is not on Player Studio.");
                if (product.OwnerGuid == session.PlayerGuid)
                    return (false, "That is your own design.");
                if (productId > uint.MaxValue || Unlocks.Has(session, Unlocks.StudioDesign, (uint)productId))
                    return (false, "You already have this design.");
                TemplateModel design = context.Template.FirstOrDefault(t => t.Id == product.TemplateId);
                if (design == null)
                    return (false, "That design is no longer available.");
                // A bought design takes no design storage on live (Templates.StudioDesignsFree), so a full storage
                // doesn't stop the purchase then.
                if (!Templates.StudioDesignsFree && !Templates.HasRoom(session, design))
                    return (false, "Your design storage is full.");
                cost = StudioPrice(product.Price);
                listing = string.IsNullOrEmpty(product.Name) ? design.Name ?? "" : product.Name;
                if (cost > 0 && session.Coins < cost)
                    return (false, $"\"{listing}\" costs {cost:N0} Lumens; you have {session.Coins:N0}.");
                source = design.Id;
                makerGuid = product.OwnerGuid;
                maker = context.Character.Where(c => c.Id == product.OwnerGuid).Select(c => c.Name).FirstOrDefault() ?? "";
                copy = new TemplateModel
                {
                    OwnerGuid = session.PlayerGuid, AuthorName = maker, Name = design.Name, Tags = design.Tags,
                    SizeX = design.SizeX, SizeY = design.SizeY, SizeZ = design.SizeZ, Materials = design.Materials,
                    Blocks = design.Blocks, CreatedAt = DateTime.UtcNow
                };
                if (cost > 0 && !session.ChangeCoins(-cost))
                    return (false, $"\"{listing}\" costs {cost:N0} Lumens; you have {session.Coins:N0}.");
                context.Template.Add(copy);
                try
                {
                    context.SaveChanges();
                }
                catch (Exception e)
                {
                    if (cost > 0)
                        session.ChangeCoins(cost);
                    log.Error($"{session.CharacterName}'s copy of Player Studio design {productId} could not be saved ({e.Message}); Lumens returned.");
                    return (false, "The design could not be saved. Nothing was charged; please try again.");
                }
            }
            Unlocks.Add(session, Unlocks.StudioDesign, (uint)productId);
            RecordSpend(session, cost);   // the reviewer rule (MayReview)
            Templates.CopyImages(source, copy.Id);
            Templates.SendAdded(session, copy);
            Templates.SendLearned(session, copy.Id, copy.Name);
            uint share = CreatorShare(cost, Config?.PlayerStudioCreatorSharePercent ?? 40);
            MailCreatorShare(makerGuid, listing, session.CharacterName, cost, share);
            if (PlayerManager.FindByGuid(makerGuid) is WorldSession owner)
                Chat.System(owner, SaleNotice(session.CharacterName, listing, cost, share));
            log.Info($"{session.CharacterName} took Player Studio design {productId} '{copy.Name}' by {maker} (copy {copy.Id})"
                + (cost > 0 ? $" for {cost} Lumens; creator's share {share}." : " (free)."));
            return (true, cost > 0 ? $"\"{copy.Name}\" is now in your Designs (Collected), for {cost:N0} Lumens."
                : $"\"{copy.Name}\" is now in your Designs (Collected).");
        }

        /// <summary>Test tooling (DevCommands "psoffer"): the reply to GetProductForReview, unasked.</summary>
        public static void OfferReview(WorldSession session) => SendProductForReview(session);

        // ---- Live's review rules (trace/0695/audit-wiki-gaps.md #10) ----------------------------------------------------
        // Update Notes 18 Jun 2014 "How Does an Item Get Accepted?" [P developer notes]: reviewers are players who had
        // bought at least 1000 SC on the Marketplace (P1207); nobody is offered more than two reviews a day (P1209); a
        // rejected object goes to a Customer Service queue, where staff confirm or overturn the players' verdict
        // (P1210); the statuses
        // "Peer Review / CS Review / Approved / Rejected" (26 Jun 2014). Wiki Player Studio [W]: reviewers have spent
        // at least 1000 Daybreak Cash; the creator's name is hidden so rivalries can't cause rejections; a rejected
        // item is queued for Daybreak staff instead.
        //
        // Here: the spend rule is the account's Lumens spent on the store and on Player Studio designs (Unlocks kind 33 per
        // character, summed over the account's characters) against PlayerStudioReviewerMinSpend Daybreak Cash at
        // LumensPerDaybreakCash (1000 → 1,300) [I: no Daybreak Cash here; the Lumen price of what live sold for it]; the
        // per-day rule counts this player's reviews in the last 24 hours [I: rolling day]; the review copy carries no
        // creator name; and a product the peers reject stays waiting (status Pending) with its rejections at the threshold:
        // the staff queue, which the peers are no longer offered and the admin page lists (<see cref="StaffQueue"/>,
        // decided with "psreview"). Live's queue state is not a client status (the client only tests -1, WriteProduct), so
        // no new status is sent. The owner is mailed at each step, as live mailed every status change (Update Notes 26 Jun
        // 2014: "Peer Review, CS Review, Approved, Rejected"): kind 0x40 (PlayerStudio.CSReviewItem, text 563484) as soon as
        // the peers' rejections send the product to staff, and kind 0x10 / 0x20 (accepted / rejected) when staff decide —
        // the client's Player Studio mail text (0.695 FUN_143639de0, 0.589 FUN_143586ba0) names all three, and 0x80
        // PeerReviewItem [P: re/decomp_all_695 part_0115.c; docs/commerce.md].
        // (Until 2026-10-18 this comment said the client had no "CS Review" kind; review 2026-10-18b #10.)

        private static uint RejectionsNeeded => Math.Max(1u, Config?.PlayerStudioRejections ?? 3);

        /// <summary>Peer rejections send a product to staff instead of turning it down (PlayerStudioStaffReview).</summary>
        public static bool StaffReviewOn => Config?.PlayerStudioStaffReview ?? true;

        /// <summary>A waiting product the peers rejected, now in the staff queue.</summary>
        public static bool InStaffReview(PlayerStudioProductModel p)
            => p.Status == Pending && StaffReviewOn && p.Rejections >= RejectionsNeeded;

        /// <summary>Lumens the player's account must have spent to review (0 = anyone).</summary>
        public static uint ReviewerMinLumens
            => Config?.PlayerStudioReviewerMinSpend is uint dbc and > 0 ? Store.LumensFor(dbc) : 0;

        /// <summary>
        /// Lumens spent on the store or a Player Studio design (Store.Buy, TakeDesign): added to the character's total
        /// (Unlocks kind 33), which the reviewer rule sums over the account.
        /// </summary>
        public static void RecordSpend(WorldSession session, long lumens)
        {
            if (session == null || lumens <= 0)
                return;
            uint before = Unlocks.Values(session.PlayerGuid, Unlocks.StoreSpent).FirstOrDefault();
            Unlocks.Set(session, Unlocks.StoreSpent, (uint)Math.Min(uint.MaxValue, before + lumens));
        }

        /// <summary>
        /// The Lumens the player's account spent on the store and on Player Studio designs (all its characters), handed to
        /// <paramref name="then"/> on the world thread (at the session's next flush when the database was asked). This
        /// character's own total is the in-memory one (Unlocks kind 33, saved in the background); the other characters'
        /// come from one query off the world thread (<c>CharacterDatabase.SumUnlockValuesOfAccount</c>), kept per account
        /// for <see cref="AltSpendTtl"/> — not loaded into the unlock cache, which holds the characters online. Until
        /// 2026-10-18 this read the account's characters (with their items) and then every alt's whole unlock set
        /// synchronously on the tick, at every login (the store start-up's GetProductForReview) and every vote, and kept
        /// the alts' sets for good, so an alt's spend on another world sharing the character database was never seen
        /// (review 2026-10-18b #3). -1: the database could not be asked.
        /// </summary>
        public static void AccountSpent(WorldSession session, Action<long> then)
        {
            long own = Unlocks.Values(session.PlayerGuid, Unlocks.StoreSpent).FirstOrDefault();
            if (session.Account == null)
            {
                then(own);
                return;
            }
            ulong account = session.Account.Id;
            if (altSpend.TryGetValue(account, out var cached) && DateTime.UtcNow - cached.At < AltSpendTtl)
            {
                then(own + cached.Lumens);
                return;
            }
            ulong character = session.PlayerGuid;
            InBackground(session, "reviewer spend check",
                () => DatabaseManager.Instance.CharacterDatabase.SumUnlockValuesOfAccount(account, Unlocks.StoreSpent, character),
                -1L, alts =>
                {
                    if (alts < 0)
                    {
                        then(-1);
                        return;
                    }
                    altSpend[account] = (alts, DateTime.UtcNow);
                    PruneAltSpend();
                    // This character's total again: a purchase may have been recorded while the query ran.
                    then(Unlocks.Values(session.PlayerGuid, Unlocks.StoreSpent).FirstOrDefault() + alts);
                });
        }

        /// <summary>How long an account's other characters' spend is kept before it is read again.</summary>
        private static readonly TimeSpan AltSpendTtl = TimeSpan.FromMinutes(1);

        /// <summary>Per account: the Lumens its characters other than the one asking spent, and when that was read.</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, (long Lumens, DateTime At)> altSpend = new();

        /// <summary>Drops the expired <see cref="altSpend"/> entries (they are only good for <see cref="AltSpendTtl"/>).</summary>
        private static void PruneAltSpend()
        {
            DateTime now = DateTime.UtcNow;
            foreach (var (account, entry) in altSpend)
                if (now - entry.At >= AltSpendTtl)
                    altSpend.TryRemove(account, out _);
        }

        /// <summary>
        /// May this player peer-review (live: an account that bought at least 1000 SC of Marketplace items, P1207)? Checked
        /// by the world in both commerce modes (RemoteCommerceService holds back the request when not). The answer goes to
        /// <paramref name="then"/> on the world thread: at once when no spend is needed or this character's own spend is
        /// enough, else after the account's other characters were read (<see cref="AccountSpent"/>, off the tick). A
        /// database that can't be asked refuses (no review this time).
        /// </summary>
        public static void MayReview(WorldSession session, Action<bool> then)
        {
            uint needed = ReviewerMinLumens;
            if (needed == 0 || Unlocks.Values(session.PlayerGuid, Unlocks.StoreSpent).FirstOrDefault() >= needed)
            {
                then(true);
                return;
            }
            AccountSpent(session, total => then(total >= needed));
        }

        /// <summary>
        /// The answer to a review request from a player who may not review: an empty product (the client's popup stays
        /// off) for GetProductForReview, false for SubmitProductReview.
        /// </summary>
        public static void RefuseReview(WorldSession session, ushort sub)
        {
            if (sub == SubSubmitProductReview)
                Send(session, SubSubmitProductReviewResponse, w => w.Write(false));
            else
                Send(session, SubReviewProductResponse, w =>
                {
                    WriteProduct(w, null, "");
                    w.Write(0u);
                });
        }

        /// <summary>Reviews this player submitted in the last 24 hours, against PlayerStudioReviewsPerDay (0 = no limit).</summary>
        private static bool ReviewedEnoughToday(Database.Character.CharacterContext context, ulong reviewer)
        {
            uint perDay = Config?.PlayerStudioReviewsPerDay ?? 2;
            if (perDay == 0)
                return false;
            DateTime since = DateTime.UtcNow.AddDays(-1);
            return context.PlayerStudioReview.Count(r => r.ReviewerGuid == reviewer && r.CreatedAt >= since) >= perDay;
        }

        /// <summary>The products waiting for staff (in-process commerce), oldest first.</summary>
        public static List<StaffQueueEntry> StaffQueue()
        {
            if (!StaffReviewOn)
                return new List<StaffQueueEntry>();
            uint needed = RejectionsNeeded;
            using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
            var waiting = context.PlayerStudioProduct.Where(p => p.Status == Pending && p.Rejections >= needed)
                .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).ToList();
            var names = DatabaseManager.Instance.CharacterDatabase.GetCharacterNames(waiting.Select(p => p.OwnerGuid).Distinct());
            // Why the peers rejected each one: the verdict codes kept with the votes (RejectReasons, L185).
            var ids = waiting.Select(p => p.Id).ToList();
            var verdicts = context.PlayerStudioReview.Where(r => ids.Contains(r.ProductId) && r.Verdict != VerdictApprove)
                .Select(r => new { r.ProductId, r.Verdict }).ToList().ToLookup(r => r.ProductId, r => r.Verdict);
            return waiting.Select(p => new StaffQueueEntry(p.Id, p.Name ?? "", p.Description ?? "", p.Tags ?? "",
                names.GetValueOrDefault(p.OwnerGuid, "?"), p.Price, p.Approvals, p.Rejections, p.CreatedAt,
                RejectReasons.Summary(verdicts[p.Id]))).ToList();
        }

        /// <summary>
        /// The oldest product waiting for peer review that this player didn't make and hasn't voted on, with the number of
        /// such products — none for a player who may not review (<see cref="MayReview"/>) or reviewed their share today, and
        /// never one in the staff queue. None: an empty product (id 0), which leaves the client's popup off. The copy is
        /// anonymous: no creator name in its TemplateData (wiki Player Studio: reviewers are not shown who made the
        /// item).
        /// </summary>
        private static void SendProductForReview(WorldSession session)
            => MayReview(session, mayReview => SendProductForReview(session, mayReview));

        /// <summary><see cref="SendProductForReview(WorldSession)"/> once the reviewer rule has answered (world thread).</summary>
        private static void SendProductForReview(WorldSession session, bool mayReview)
        {
            PlayerStudioProductModel product = null;
            int waiting = 0;
            if (mayReview)
            {
                uint needed = StaffReviewOn ? RejectionsNeeded : uint.MaxValue;
                using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
                if (!ReviewedEnoughToday(context, session.PlayerGuid))
                {
                    IQueryable<PlayerStudioProductModel> open = context.PlayerStudioProduct.Where(p => p.Status == Pending
                        && p.Rejections < needed
                        && p.OwnerGuid != session.PlayerGuid
                        && !context.PlayerStudioReview.Any(r => r.ProductId == p.Id && r.ReviewerGuid == session.PlayerGuid));
                    waiting = open.Count();
                    product = open.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).FirstOrDefault();
                }
            }
            Send(session, SubReviewProductResponse, w =>
            {
                WriteProduct(w, product, "");
                w.Write((uint)waiting);
            });
        }

        /// <summary>
        /// A Product (reader FUN_143184930; column names from the client's data sources FUN_143652d10 and
        /// FUN_143647020): u64 ProductId, u64, u32 BundleId, u32 Quantity, u32 Price, u32, u32 Rating (Likes), string
        /// Name, string Description, u64 Created (Unix s), u64 LastPurchased, u32 Status, string SearchTags, u32 n ×
        /// ProductItem (FUN_143184b70: u64 ItemId, u32 ItemType, u32 quantity, u64, bool, string). A design entry's string
        /// is its TemplateData XML (FUN_1434d4590 → FUN_1434c1740), where the review window takes ItemOwner (CreatorName)
        /// and the screenshots. BundleId must not be 0 (the client only sends ProductRemove for a product with one), so
        /// it is the product id; Status is this server's (0 waiting, 1 listed: the client only tests for -1). The two
        /// unnamed fields are read nowhere in 0.589 and are sent as 0.
        /// </summary>
        private static void WriteProduct(GamePacketWriter w, PlayerStudioProductModel p, string creator)
        {
            w.Write(p?.Id ?? 0ul);
            w.Write(0ul);
            w.Write((uint)(p?.Id ?? 0));
            w.Write(p == null ? 0u : 1u);
            w.Write(p?.Price ?? 0u);
            w.Write(0u);
            w.Write(p?.Approvals ?? 0u);
            w.Write(p?.Name ?? "");
            w.Write(p?.Description ?? "");
            w.Write(p == null ? 0ul : (ulong)new DateTimeOffset(DateTime.SpecifyKind(p.CreatedAt, DateTimeKind.Utc)).ToUnixTimeSeconds());
            w.Write(0ul);
            w.Write((uint)(p?.Status ?? 0));
            w.Write(p?.Tags ?? "");
            if (p == null)
            {
                w.Write(0u);
                return;
            }
            w.Write(1u);
            w.Write(p.TemplateId);
            w.Write(p.ItemType);
            w.Write(1u);
            w.Write(0ul);
            w.Write(false);
            w.Write(p.ItemType == ItemDesign ? TemplateDataXml(creator) : "");
        }

        /// <summary>
        /// The design's TemplateData XML as the review window reads it. The reader takes each field as an element or an
        /// attribute depending on its mode (FUN_142b79310), so CreatorName is written both ways.
        /// </summary>
        private static string TemplateDataXml(string creator)
        {
            string name = System.Security.SecurityElement.Escape(creator ?? "");
            return $"<TemplateData CreatorName=\"{name}\"><CreatorName>{name}</CreatorName></TemplateData>";
        }

        /// <summary>
        /// A peer-review vote. One per player and product; never on your own. When the product reaches the approvals
        /// or rejections needed (WorldServerConfiguration.PlayerStudioApprovals / PlayerStudioRejections) it is
        /// listed or rejected and the owner gets the result mail.
        /// </summary>
        private static void SubmitReview(WorldSession session, byte[] body)
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
                Send(session, SubSubmitProductReviewResponse, w => w.Write(false));
                return;
            }
            MayReview(session, mayReview => SubmitReview(session, productId, verdict, mayReview));
        }

        /// <summary><see cref="SubmitReview(WorldSession, byte[])"/> once the reviewer rule has answered (world thread).</summary>
        private static void SubmitReview(WorldSession session, ulong productId, uint verdict, bool mayReview)
        {
            bool ok = false;
            PlayerStudioProductModel decided = null, toStaff = null;
            using (var context = DatabaseManager.Instance.CharacterDatabase.NewContext())
            {
                PlayerStudioProductModel product = context.PlayerStudioProduct.FirstOrDefault(p => p.Id == productId);
                if (mayReview && product != null && product.Status == Pending && !InStaffReview(product)
                    && product.OwnerGuid != session.PlayerGuid && !ReviewedEnoughToday(context, session.PlayerGuid)
                    && !context.PlayerStudioReview.Any(v => v.ProductId == productId && v.ReviewerGuid == session.PlayerGuid))
                {
                    context.PlayerStudioReview.Add(new PlayerStudioReviewModel
                    {
                        ProductId = productId, ReviewerGuid = session.PlayerGuid, Verdict = verdict, CreatedAt = DateTime.UtcNow
                    });
                    if (verdict == VerdictApprove)
                        product.Approvals++;
                    else
                        product.Rejections++;
                    if (product.Approvals >= Math.Max(1u, Config.PlayerStudioApprovals))
                    {
                        product.Status = Listed;
                        product.ListedAt = DateTime.UtcNow;   // the following digest's "newly listed" (ShowcaseFollow)
                    }
                    else if (product.Rejections >= Math.Max(1u, Config.PlayerStudioRejections) && !StaffReviewOn)
                        product.Status = Rejected;
                    else if (InStaffReview(product))
                    {
                        toStaff = product;
                        log.Info($"Player Studio product {product.Id} '{product.Name}' rejected by its peers: waiting for staff (admin page).");
                    }
                    context.SaveChanges();
                    ok = true;
                    if (product.Status != Pending)
                        decided = product;
                }
            }
            Send(session, SubSubmitProductReviewResponse, w => w.Write(ok));
            log.Info($"{session.CharacterName} reviewed Player Studio product {productId}: verdict {verdict}{(ok ? "" : " (refused)")}.");
            if (decided != null)
                Decide(decided);
            else if (toStaff != null)
                // The owner's "CS Review" mail: kind 0x40 → PlayerStudio.CSReviewItem (FUN_143586ba0, text 563484) [P]; live
                // mailed every status change (Update Notes 26 Jun 2014: "Peer Review, CS Review, Approved, Rejected").
                Social.SocialServices.Service.SendSystemMail(toStaff.OwnerGuid, "Player Studio", toStaff.Name, "", 0, null, MailStaffReview);
        }

        /// <summary>A product's review is over: the owner's mail (the client writes its text from the kind).</summary>
        private static void Decide(PlayerStudioProductModel product)
        {
            bool listed = product.Status == Listed;
            Social.SocialServices.Service.SendSystemMail(product.OwnerGuid, "Player Studio", product.Name, "", 0, null,
                listed ? MailAccepted : MailRejected);
            MarketChanged(product);
            log.Info($"Player Studio product {product.Id} '{product.Name}' {(listed ? "accepted" : "rejected")}.");
        }

        /// <summary>
        /// An admin decision (DevCommands "psreview &lt;id&gt; approve|reject", the admin page's staff list; the live client's
        /// admin path sent "appgamecommerce product approve|reject &lt;id&gt;"): any waiting product, in peer review or in the
        /// staff queue.
        /// </summary>
        public static bool AdminDecide(ulong productId, bool approve)
        {
            PlayerStudioProductModel product;
            using (var context = DatabaseManager.Instance.CharacterDatabase.NewContext())
            {
                product = context.PlayerStudioProduct.FirstOrDefault(p => p.Id == productId);
                if (product == null || product.Status != Pending)
                    return false;
                product.Status = approve ? Listed : Rejected;
                if (approve)
                    product.ListedAt = DateTime.UtcNow;
                context.SaveChanges();
            }
            Decide(product);
            return true;
        }

        private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

        /// <summary>
        /// CommerceProductCreateResponse 0x31 (0.589 0x30) {u32 status, string} (FUN_1431a7430 → FUN_14319fc60): 1 created, 9 / 0xB /
        /// 0xC / 0xF refusals; 0 (no case) shows the client's "CommerceCreateProductUnknownError" (the commerce service
        /// unreachable, world-channel-service.md §7).
        /// </summary>
        public static void SendCreateResponse(WorldSession session, uint status, string text)
            => Send(session, SubProductCreateResponse, w => { w.Write(status); w.Write(text ?? ""); });

        /// <summary>Sends an InGamePurchase message; <paramref name="sub"/> is the 0.695 number, written as given.</summary>
        private static void Send(WorldSession session, ushort sub, Action<GamePacketWriter> body)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.WriteUShort(sub);
            body(w);
            session.EnqueueMessage(ZoneMessageOpcode.InGamePurchaseBase, output.ToArray());
        }
    }
}
