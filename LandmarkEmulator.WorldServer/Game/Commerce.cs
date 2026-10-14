using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The in-game commerce service (InGamePurchaseBase 0x45, u16 sub) behind Player Studio and the in-game store.
    /// Decoded 2026-10-12 from the 0.589 client (docs/commerce.md). The store is free on this server: no real money,
    /// no Station Cash.
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
    /// the claim and the free-listing byte set (FUN_1431b8df0), and is listed at once; Remove from Gallery removes it.</item>
    /// <item>Results reach the owner as mail with kind 0x10 (accepted) / 0x20 (rejected): the client writes the sender
    /// "PlayerStudio.MailUser", the subject and the body (PlayerStudio.AcceptedItem / RejectedItem, item = the mail
    /// subject) itself (FUN_143586ba0).</item>
    /// </list>
    /// </summary>
    public static class Commerce
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const ushort SubAccountInfoRequest = 0x1A, SubAccountInfoResponse = 0x1B, SubProductCreate = 0x2F,
            SubProductCreateResponse = 0x30, SubGetProductForReview = 0x3F, SubReviewProductResponse = 0x40,
            SubSubmitProductReview = 0x42, SubSubmitProductReviewResponse = 0x43, SubProductRemove = 0x31,
            SubProductUpdate = 0x33, SubRequestAccountStore = 0x36, SubAccountStoreResponse = 0x38;

        /// <summary>World map site search: CommerceSearch / CommerceSearchResponse.</summary>
        public const ushort SubCommerceSearch = 0x3D, SubCommerceSearchResponse = 0x3E;
        /// <summary>
        /// IsTemplateValid (0x4C, built in FUN_1431b7c20): before listing a design that contains other designs the client
        /// asks {u64 design, u32 n, n x u64 inner design} and holds its ProductCreate until IsTemplateValidResponse
        /// (0x4D, reader FUN_143183180, handler FUN_1431ad850) {u64 design, bool}: true sends the held ProductCreate,
        /// false fails with 0x16 "TemplateContainsModifiedSubTemplate".
        /// </summary>
        public const ushort SubIsTemplateValid = 0x4C, SubIsTemplateValidResponse = 0x4D;

        /// <summary>
        /// The world map's search bar (Lua Ui.WorldMapClaimTagFilterUpdate → FUN_142cef090 → FUN_1431c31e0):
        /// CommerceSearch {string query, u32 1}. Answered with CommerceSearchResponse (0x3E; reader FUN_143184530,
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
        /// <summary>SubmitProductReview verdict for an approval; every other value rejects.</summary>
        private const uint VerdictApprove = 1;
        /// <summary>ProductItem types: a design, a build site (gallery).</summary>
        public const uint ItemDesign = 2, ItemClaim = 3;
        /// <summary>Lowest price of a design (the client's own check, FUN_1431b7c20).</summary>
        private const uint MinPrice = 25;

        private static WorldServerConfiguration Config =>
            Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config;

        /// <summary>Product statuses (PlayerStudioProductModel.Status).</summary>
        public const byte Pending = 0, Listed = 1, Rejected = 2, Removed = 3;
        /// <summary>CommerceProductCreateResponse codes.</summary>
        private const uint Created = 1, ItemNotFound = 9, TemplateNotOwned = 0xB, NotValid = 0xC, Duplicate = 0xF;

        /// <summary>An InGamePurchase request the older handlers don't take (upkeep, wallet stay in InteractionHandler).</summary>
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
                    log.Info($"Commerce request 0x{sub:X2} from {session.CharacterName}: {BitConverter.ToString(body)}");
                    return false;
            }
        }

        /// <summary>
        /// A Player Studio product: one of the player's designs (stored, waiting for peer review), or one of their build
        /// sites published to the gallery (free, listed at once).
        /// </summary>
        private static void CreateProduct(WorldSession session, byte[] body)
        {
            uint price;
            bool gallery;
            string name, description, tags, headline;
            var items = new List<(ulong Guid, uint Type)>();
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
                SendCreateResponse(session, NotValid, "");
                return;
            }
            uint itemType = gallery ? ItemClaim : ItemDesign;
            var item = items.FirstOrDefault(i => i.Type == itemType);
            if (item.Guid == 0 || name.Length == 0 || (!gallery && price < MinPrice))
            {
                SendCreateResponse(session, NotValid, "");
                return;
            }
            using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
            ulong owner = gallery ? Claims.Get(item.Guid)?.OwnerGuid ?? 0
                : context.Template.Where(t => t.Id == item.Guid).Select(t => t.OwnerGuid).FirstOrDefault();
            if (owner == 0)
            {
                SendCreateResponse(session, ItemNotFound, "");
                return;
            }
            if (owner != session.PlayerGuid)
            {
                SendCreateResponse(session, TemplateNotOwned, "");
                return;
            }
            if (context.PlayerStudioProduct.Any(p => p.TemplateId == item.Guid && p.ItemType == itemType
                && (p.Status == Pending || p.Status == Listed)))
            {
                SendCreateResponse(session, Duplicate, "");
                return;
            }
            var product = new PlayerStudioProductModel
            {
                OwnerGuid = session.PlayerGuid, TemplateId = item.Guid, ItemType = itemType, Name = Clip(name, 128),
                Description = Clip(description, 1024), Headline = Clip(headline, 256), Tags = Clip(tags, 512),
                Price = gallery ? 0 : price, Status = gallery ? Listed : Pending, CreatedAt = DateTime.UtcNow
            };
            context.PlayerStudioProduct.Add(product);
            context.SaveChanges();
            SendCreateResponse(session, Created, "");
            MarketChanged(product);
            log.Info(gallery
                ? $"{session.CharacterName} published build site {item.Guid} to the gallery (product {product.Id} '{product.Name}')."
                : $"{session.CharacterName} submitted Player Studio product {product.Id} '{product.Name}' (design {item.Guid}).");
        }

        /// <summary>
        /// AccountStore 0x38 (reader FUN_143182f10): {u64, bool, u32 n strings (followings), u32 n purchases
        /// (FUN_143184c70), u32 n {string, u64}, u64, u64, u64, u32, u32} (FUN_143181d00), u32 n strings (recent
        /// searches), u32 n bundles (FUN_143182020), u32 n × Product (the player's own: the client's list at manager
        /// +0x236e00). Following, purchases and bundles belong to the store side and are empty. The player's products
        /// waiting for review or listed; rejected and removed ones are left out (the client would take them for live
        /// ones: it only skips Status -1).
        /// </summary>
        private static void SendAccountStore(WorldSession session)
        {
            List<PlayerStudioProductModel> mine;
            string creator = session.CharacterName ?? "";
            using (var context = DatabaseManager.Instance.CharacterDatabase.NewContext())
                mine = context.PlayerStudioProduct.Where(p => p.OwnerGuid == session.PlayerGuid
                    && (p.Status == Pending || p.Status == Listed)).OrderBy(p => p.Id).ToList();
            Send(session, SubAccountStoreResponse, w =>
            {
                w.Write(0ul);
                w.Write(false);
                w.Write(0u);
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

        /// <summary>ProductRemove 0x31 {u64 product}: the owner takes a product off (or a site off the gallery). No answer.</summary>
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
            log.Info($"{session.CharacterName} removed Player Studio product {id} '{product.Name}'.");
        }

        /// <summary>
        /// ProductUpdate 0x33 (writer FUN_143181020): {u64 product, string name, string description, u32 price, string
        /// tags, u8 × 5, u64}. The owner edited a product (or renamed / retagged a published site: SetClaimName and
        /// SetClaimTags update its listing). No answer.
        /// </summary>
        private static void UpdateProduct(WorldSession session, byte[] body)
        {
            ulong id;
            string name, description, tags;
            uint price;
            try
            {
                var r = new GamePacketReader(body);
                id = r.ReadULong();
                name = (r.ReadString() ?? "").Trim();
                description = (r.ReadString() ?? "").Trim();
                price = r.ReadUInt();
                tags = (r.ReadString() ?? "").Trim();
            }
            catch (Exception e)
            {
                log.Warn($"Unreadable product update from {session.CharacterName} ({e.Message}): {BitConverter.ToString(body)}");
                return;
            }
            using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
            PlayerStudioProductModel product = context.PlayerStudioProduct.FirstOrDefault(p => p.Id == id && p.OwnerGuid == session.PlayerGuid);
            if (product == null || product.Status == Removed)
                return;
            if (name.Length > 0)
                product.Name = Clip(name, 128);
            product.Description = Clip(description, 1024);
            product.Tags = Clip(tags, 512);
            if (product.ItemType == ItemDesign && price >= MinPrice)
                product.Price = price;
            context.SaveChanges();
            log.Info($"{session.CharacterName} updated Player Studio product {id} '{product.Name}'.");
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

        /// <summary>A build site published to the gallery (the Showcase gallery lists only these).</summary>
        public static bool IsOnGallery(ulong claimId)
        {
            lock (marketSync)
            {
                LoadMarket();
                return galleryClaims.Contains(claimId);
            }
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

        /// <summary>A listed Player Studio design for the Showcase's Player Studio page.</summary>
        public record StudioDesign(ulong Product, string Name, string Description, string Tags, string Author, uint Voxels,
            ulong Template);

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
                (uint)Templates.MaterialCount(designs[p.TemplateId]), p.TemplateId)).ToList();
        }

        /// <summary>
        /// Takes a listed Player Studio design (the Showcase's Player Studio page; the store is free, as in
        /// <see cref="Store"/>): the buyer gets their own copy of the design, credited to its maker — the Designs window
        /// files it under Collected — once per product. The maker hears about it.
        /// </summary>
        public static (bool Ok, string Message) TakeDesign(WorldSession session, ulong productId)
        {
            TemplateModel copy;
            string maker;
            ulong source;
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
                if (!Templates.HasRoom(session, design))
                    return (false, "Your design storage is full.");
                source = design.Id;
                maker = context.Character.Where(c => c.Id == product.OwnerGuid).Select(c => c.Name).FirstOrDefault() ?? "";
                copy = new TemplateModel
                {
                    OwnerGuid = session.PlayerGuid, AuthorName = maker, Name = design.Name, Tags = design.Tags,
                    SizeX = design.SizeX, SizeY = design.SizeY, SizeZ = design.SizeZ, Materials = design.Materials,
                    Blocks = design.Blocks, CreatedAt = DateTime.UtcNow
                };
                context.Template.Add(copy);
                context.SaveChanges();
            }
            Unlocks.Add(session, Unlocks.StudioDesign, (uint)productId);
            Templates.CopyImages(source, copy.Id);
            Templates.SendAdded(session, copy);
            Templates.SendLearned(session, copy.Id, copy.Name);
            if (PlayerManager.Snapshot().FirstOrDefault(p => p.CharacterName == maker) is WorldSession owner)
                Chat.System(owner, $"{session.CharacterName} took your design \"{copy.Name}\" from Player Studio.");
            log.Info($"{session.CharacterName} took Player Studio design {productId} '{copy.Name}' by {maker} (copy {copy.Id}).");
            return (true, $"\"{copy.Name}\" is now in your Designs (Collected).");
        }

        /// <summary>Test tooling (DevCommands "psoffer"): the reply to GetProductForReview, unasked.</summary>
        public static void OfferReview(WorldSession session) => SendProductForReview(session);

        /// <summary>
        /// The oldest product waiting for review that this player didn't make and hasn't voted on, with the number of
        /// such products. None: an empty product (id 0), which leaves the client's popup off.
        /// </summary>
        private static void SendProductForReview(WorldSession session)
        {
            PlayerStudioProductModel product;
            string creator = "";
            int waiting;
            using (var context = DatabaseManager.Instance.CharacterDatabase.NewContext())
            {
                IQueryable<PlayerStudioProductModel> open = context.PlayerStudioProduct.Where(p => p.Status == Pending
                    && p.OwnerGuid != session.PlayerGuid
                    && !context.PlayerStudioReview.Any(r => r.ProductId == p.Id && r.ReviewerGuid == session.PlayerGuid));
                waiting = open.Count();
                product = open.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).FirstOrDefault();
                if (product != null)
                    creator = context.Character.Where(c => c.Id == product.OwnerGuid).Select(c => c.Name).FirstOrDefault() ?? "";
            }
            Send(session, SubReviewProductResponse, w =>
            {
                WriteProduct(w, product, creator);
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
            bool ok = false;
            PlayerStudioProductModel decided = null;
            using (var context = DatabaseManager.Instance.CharacterDatabase.NewContext())
            {
                PlayerStudioProductModel product = context.PlayerStudioProduct.FirstOrDefault(p => p.Id == productId);
                if (product != null && product.Status == Pending && product.OwnerGuid != session.PlayerGuid
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
                        product.Status = Listed;
                    else if (product.Rejections >= Math.Max(1u, Config.PlayerStudioRejections))
                        product.Status = Rejected;
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
        }

        /// <summary>A product's review is over: the owner's mail (the client writes its text from the kind).</summary>
        private static void Decide(PlayerStudioProductModel product)
        {
            bool listed = product.Status == Listed;
            Mail.SendSystemMail(product.OwnerGuid, "Player Studio", product.Name, "", 0, null,
                listed ? MailAccepted : MailRejected);
            MarketChanged(product);
            log.Info($"Player Studio product {product.Id} '{product.Name}' {(listed ? "accepted" : "rejected")}.");
        }

        /// <summary>
        /// An admin decision (DevCommands "psreview &lt;id&gt; approve|reject"; the live client's admin path sent
        /// "appgamecommerce product approve|reject &lt;id&gt;").
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
                context.SaveChanges();
            }
            Decide(product);
            return true;
        }

        private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

        private static void SendCreateResponse(WorldSession session, uint status, string text)
            => Send(session, SubProductCreateResponse, w => { w.Write(status); w.Write(text ?? ""); });

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
