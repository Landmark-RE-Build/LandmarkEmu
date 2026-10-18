using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.GameTable;
using LandmarkEmulator.Shared.GameTable.Text;
using LandmarkEmulator.WorldServer.Game.WorldChannel;
using LandmarkEmulator.WorldServer.Network;
using Microsoft.Extensions.Configuration;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The Showcase (HUD "Showcase" button = the InGameMarketplaceBrowser window, title "Showcase"): the client's
    /// embedded Chromium browser shows ClientConfig [Marketplace] MarketplaceUrl, opened at "#!/marketplace" or
    /// "#!/gallery" (Browser.OpenMarketplace / OpenGallery, FUN_143530290 / FUN_143530100) or at
    /// "/#!/marketplace/search/limit=50&amp;filterEquals=Purchasable:1&amp;SearchTags=..." (OpenMarketplaceWithTags). The
    /// official pages lived on Daybreak's store servers, which also delivered what was bought; the client data has no
    /// catalogue (Resources/Marketing tables are empty). Here the world server serves those pages itself
    /// (<see cref="WebPages"/>). What the store sells and for what is WorldServer.json <c>FreeStore</c> (TASKS 6.60,
    /// trace/0695/agent-store-entitlements.md): off (default) = live's 2016 Marketplace products for Lumens
    /// (<see cref="MarketplaceProducts"/>), on = the whole catalogue free, as before 2026-10-17. Nothing is ever paid with
    /// real money: the client's own Daybreak Cash wallet holds no Daybreak Cash (Commerce.SendWalletInfo: the Lumens while
    /// the Player Studio listing fee is charged, else empty) and every price is in
    /// Lumens (item 1579, <see cref="Inventory.Inventory.CurrencyItem"/>), shown and charged by these pages, not by a
    /// client store packet (the InGamePurchase order packets are never sent; coverage-695.md §0x45). The client does get
    /// its own copy of the products (<see cref="StoreBundles"/>: live's Marketplace products this store sells and the
    /// membership row, at this store's price for the player), because the Crimson Parlor only lists the Fantasy Parlor
    /// Pack's options with an Unlock button when the client's store has the Pack.
    ///
    /// <para>Who is buying: the page only reaches the web server, so the server gives the player's browser a sign-in
    /// URL with a per-session token: client Lua SoeGui.GetWindow('InGameMarketplaceBrowser'):ShowURL(url) through
    /// <see cref="ClientLua.RunCode"/> (ShowURL also queues the URL when the browser doesn't exist yet). The browser only exists after the
    /// client's login preload (Browser.Create, which loads MarketplaceUrl#!/gallery), so the sign-in goes out
    /// <see cref="SignInDelay"/> after world entry, and again when the player opens the Showcase (UI event 7) while
    /// the page hasn't confirmed it yet (/api/me with the token). The page keeps the token in its sessionStorage (one
    /// per client process, so two clients on one PC stay apart) and sends it with every request; the client's own
    /// navigations reuse the same browser and keep it. A token dies with the session.</para>
    ///
    /// <para>Catalogue: cosmetic outfits (equipment slot 1), decorations (prop items in the builder's prop categories
    /// PropCategory / PropCategoryPropMap / PropCategoryGroup; uncategorised props such as game markers and crafting
    /// stations stay out) and designs (design items with a client template). Tools, weapons, armour and accessories
    /// (slot 3: combat trinkets) stay out too: they are crafted. Icons are the client's
    /// image sets exported by re/tools/export_icons.py into GameDataPath/icons.</para>
    /// </summary>
    public static class Store
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// A store item: <paramref name="Price"/> in Lumens (0 = free) and where that price comes from
        /// (<paramref name="PriceNote"/>, shown on the page).
        /// </summary>
        public record Item(uint Id, string Name, string Description, uint Icon, string Section, string Group, uint Price = 0,
            string PriceNote = "");
        /// <summary>
        /// A gallery entry: the site, its owner (and whether they are online), landscape, welcome message, likes
        /// (<c>Rating</c>), its three gallery tags (genre, what was built, what others can do), whether it is in Play
        /// Mode now, when it was listed (Unix seconds), its primary screenshot (web path or ""), how many comments it
        /// has and the competitions it is entered in.
        /// </summary>
        public record Site(ulong Id, string Name, string Owner, string Landscape, string Description, ulong Rating, bool Online,
            string[] Tags, bool PlayMode, long ListedAt, string Image, int Comments, List<string> Contests);

        private static readonly object sync = new();
        private static readonly Dictionary<string, WorldSession> sessions = new();
        private static readonly Dictionary<WorldSession, string> tokens = new();
        private static readonly Dictionary<WorldSession, DateTime> lastGrant = new();
        /// <summary>Sign-ins waiting to be sent (due time), and players whose page confirmed its token.</summary>
        private static readonly Dictionary<WorldSession, DateTime> pendingSignIn = new();
        private static readonly HashSet<WorldSession> confirmed = new();
        private static readonly TimeSpan SignInDelay = TimeSpan.FromSeconds(10);
        public const uint ShowcaseOpenedEvent = 7;
        private static List<Item> catalogue;

        /// <summary>Most of one item a single request may take, and the pause between takes.</summary>
        public const uint MaxCount = 100;
        private static readonly TimeSpan GrantInterval = TimeSpan.FromMilliseconds(400);

        /// <summary>
        /// The address players' browsers reach the web pages at (WorldServer.json "WebPagesUrl"; unset or empty =
        /// http://127.0.0.1:WebPagesPort, this PC only).
        /// </summary>
        public static string BaseUrl
            => (SharedConfiguration.Configuration?.GetValue<string>("WebPagesUrl") is string url && !string.IsNullOrWhiteSpace(url)
                ? url.Trim() : $"http://127.0.0.1:{WebPages.Port}").TrimEnd('/');

        /// <summary>World entry: a fresh token; the sign-in URL follows once the client's browser exists.</summary>
        public static void SignIn(WorldSession session)
        {
            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            lock (sync)
            {
                if (tokens.Remove(session, out string old))
                    sessions.Remove(old);
                tokens[session] = token;
                sessions[token] = session;
                confirmed.Remove(session);
                pendingSignIn[session] = DateTime.UtcNow + SignInDelay;
            }
        }

        /// <summary>World thread: sends the sign-ins that are due.</summary>
        public static void Update()
        {
            List<WorldSession> due;
            lock (sync)
            {
                if (pendingSignIn.Count == 0)
                    return;
                DateTime now = DateTime.UtcNow;
                due = pendingSignIn.Where(p => p.Value <= now).Select(p => p.Key).ToList();
                foreach (WorldSession session in due)
                    pendingSignIn.Remove(session);
            }
            foreach (WorldSession session in due)
                SendSignIn(session);
        }

        /// <summary>The player opened the Showcase (UI event 7): sign the page in now if it hasn't confirmed yet.</summary>
        public static void UiEvent(WorldSession session, uint uiEvent)
        {
            if (uiEvent != ShowcaseOpenedEvent)
                return;
            lock (sync)
            {
                if (confirmed.Contains(session) || !tokens.ContainsKey(session))
                    return;
                pendingSignIn.Remove(session);
            }
            SendSignIn(session);
        }

        private static void SendSignIn(WorldSession session)
        {
            string token = TokenOf(session);
            if (token == null || session.Disconnected)
                return;
            ClientLua.RunCode(session, $"SoeGui.GetWindow('InGameMarketplaceBrowser'):ShowURL('{BaseUrl}/?t={token}#!/marketplace')");
            ClientFixes.Apply(session);
        }

        /// <summary>The page loaded with this player's token (/api/me): no more sign-ins needed.</summary>
        public static void Confirm(WorldSession session)
        {
            lock (sync)
            {
                if (tokens.ContainsKey(session) && confirmed.Add(session))
                    log.Info($"Store: {session.CharacterName}'s Showcase page is signed in.");
            }
        }

        /// <summary>The player left the world: the token stops working.</summary>
        public static void Forget(WorldSession session)
        {
            lock (sync)
            {
                if (tokens.Remove(session, out string token))
                    sessions.Remove(token);
                lastGrant.Remove(session);
                pendingSignIn.Remove(session);
                confirmed.Remove(session);
            }
        }

        /// <summary>The online player a token belongs to, or null.</summary>
        public static WorldSession SessionOf(string token)
        {
            if (string.IsNullOrEmpty(token))
                return null;
            lock (sync)
                return sessions.TryGetValue(token, out WorldSession session) && !session.Disconnected ? session : null;
        }

        /// <summary>The token of an online player (tests: the harness reads it from the log instead).</summary>
        public static string TokenOf(WorldSession session)
        {
            lock (sync)
                return tokens.GetValueOrDefault(session);
        }

        private static readonly string[] SectionOrder = { "Build Sites", "Outfits", "Cosmetics", "Decorations", "Designs" };

        private static List<Item> everything;

        private static WorldServerConfiguration Config =>
            Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config;

        /// <summary>Is the whole catalogue free (WorldServer.json FreeStore)?</summary>
        public static bool IsFree => Config?.FreeStore ?? false;

        /// <summary>
        /// The store's items (built once): the whole catalogue, free, with FreeStore on; else live's Marketplace products
        /// and the owner's priced extras (<see cref="PricedCatalogue"/>).
        /// </summary>
        /// <summary>
        /// The store's membership row (no item: buying runs <see cref="Membership.Purchase"/>, which takes the Lumens
        /// itself). Listed in the free store too: membership always costs Lumens (user decision 2026-10-17).
        /// </summary>
        public const uint MembershipStoreId = 900600;

        public static List<Item> Catalogue()
        {
            if (catalogue != null)
                return catalogue;
            catalogue = IsFree ? Everything() : PricedCatalogue();
            (uint memberPrice, uint memberDays) = Membership.Offer;
            if (memberPrice > 0 && memberDays > 0)
                catalogue.Insert(0, new Item(MembershipStoreId, $"Membership ({memberDays} days)",
                    $"Member benefits: priority in the login queue, Lumens every month, and lower store prices. Bought with "
                    + "Lumens, never real money; a membership bought while you are one adds its days.", 0, "Membership",
                    "Membership", memberPrice, "membership price set by the server"));
            log.Info($"Store catalogue ({(IsFree ? "free" : "live prices in Lumens")}): {catalogue.Count} items "
                + $"({string.Join(", ", catalogue.GroupBy(i => i.Section).Select(g => $"{g.Count()} {g.Key}"))}).");
            return catalogue;
        }

        /// <summary>
        /// A product live's Marketplace sold in 2016 (launch to the last update): the item, its Daybreak Cash price (0 =
        /// priced from the Lumen Station's own recipe instead), the store section / group, and the evidence.
        /// </summary>
        private sealed record Product(uint Item, uint DaybreakCash, string Section, string Group, string Source);

        /// <summary>
        /// Live's 2016 Marketplace (trace/0695/agent-store-entitlements.md §2). Lumens themselves (the Lumen Box … Hoard of
        /// Lumens, client texts 574410-574422) are not sold: players earn them. Player Studio designs are on their own page.
        /// Everything else in the old free catalogue (the 290 Lumen Station outfits, the 448 discovered props, innate
        /// props, designs) live never sold on the 2016 Marketplace: they keep their live routes.
        /// </summary>
        private static readonly Product[] MarketplaceProducts =
        {
            // Base Build Sites cut to 799 DBC (Week End Wrap Up 1 Apr 2016, P4471) [W]; the
            // product is the Base Build Site Expansion (P4450), still sold on 14 Jul 2016 (P6083-P6084).
            new(Inventory.ItemDefinitionBuilder.BuildSiteExpansion, 799, "Build Sites", "Build Site Expansion", "799 Daybreak Cash on live (1 Apr 2016)"),
            // sold only for 499 DBC (wiki Fantasy Parlor Pack, July 2016; sold from 30 Jun 2016, P5910) [W].
            new(Unlockers.FantasyParlorPack, 499, "Cosmetics", "Crimson Parlor", "499 Daybreak Cash on live (2016)"),
            // six more pets on the Marketplace (28 Jul 2016, P6144) [W]. Their price is in no source searched
            // (open lead): 499 DBC, the Fantasy Parlor Pack's, is a placeholder [I].
            new(Inventory.ItemDefinitionBuilder.ShadowWolfFigurine, 499, "Cosmetics", "Pets", "live price not known: placeholder"),
            new(Inventory.ItemDefinitionBuilder.StagFigurine, 499, "Cosmetics", "Pets", "live price not known: placeholder"),
            new(Inventory.ItemDefinitionBuilder.CockatriceFigurine, 499, "Cosmetics", "Pets", "live price not known: placeholder"),
            new(Inventory.ItemDefinitionBuilder.BunnyFigurine, 499, "Cosmetics", "Pets", "live price not known: placeholder"),
            new(Inventory.ItemDefinitionBuilder.BeetleFigurine, 499, "Cosmetics", "Pets", "live price not known: placeholder"),
            new(Inventory.ItemDefinitionBuilder.JackalopeFigurine, 499, "Cosmetics", "Pets", "live price not known: placeholder"),
            // two higher reward chest tiers at the Lumen Station, also sold on the Marketplace for DBC
            // (29 Sep 2016, P6501) [W]: their DBC price is not known, so the Lumen Station's own price (recipes 5363 / 5364:
            // 260 / 780 Lumens) [P].
            new(SuperbRewardChest, 0, "Decorations", "Reward Chests", "the Lumen Station's price"),
            new(LegendaryRewardChest, 0, "Decorations", "Reward Chests", "the Lumen Station's price"),
        };

        /// <summary>
        /// Is this item one of live's 2016 Marketplace products (<see cref="MarketplaceProducts"/>)? The client's own store
        /// records hold these (<see cref="StoreBundles"/>), not the owner's extras or the free catalogue.
        /// </summary>
        public static bool IsMarketplaceProduct(uint itemId) => MarketplaceProducts.Any(p => p.Item == itemId);

        /// <summary>Reward Chest items (Superb, Legendary; RewardChests class notes) [P].</summary>
        public const uint SuperbRewardChest = 4514, LegendaryRewardChest = 4515;

        /// <summary>The store group of the Build Site Vendor prop item.</summary>
        private const string VendorGroup = "Build Site Vendor";

        /// <summary>The Lumen Station (crafting station 55): every recipe priced in Lumens only.</summary>
        private const uint LumenStation = 55;

        /// <summary>
        /// A Daybreak Cash price in Lumens: × WorldServer.json LumensPerDaybreakCash (1.3), rounded up to whole 5 Lumens —
        /// the rounding [I] puts 499 / 799 DBC on the Lumen Station's own 65-Lumen grid (650 / 1040 Lumens).
        /// </summary>
        public static uint LumensFor(uint daybreakCash)
        {
            double rate = Config?.LumensPerDaybreakCash ?? 1.3;
            if (rate <= 0 || double.IsNaN(rate) || double.IsInfinity(rate))
                rate = 1.3;
            return (uint)Math.Min(uint.MaxValue, Math.Ceiling(daybreakCash * rate / 5.0 - 1e-9) * 5);
        }

        /// <summary>
        /// What the Lumen Station charges for one of this item: a station-55 recipe making it whose only component is
        /// Lumens (as DesignMaterials reads the station); 0 when there is none.
        /// </summary>
        private static uint LumenStationPrice(uint itemId)
        {
            foreach (var mapping in GameTableManager.Instance.CraftingStationRecipeMappings.Entries
                .Where(m => m.CraftingStationId == LumenStation && m.RecipeId != 0))
            {
                var recipe = GameTableManager.Instance.Recipes.GetEntry(mapping.RecipeId);
                if (recipe == null || recipe.ItemDefinitionId != itemId || recipe.ItemCount == 0)
                    continue;
                var components = GameTableManager.Instance.ComponentsOf(recipe.Id).ToList();
                if (components.Count == 1 && components[0].ComponentType == 1
                    && components[0].ComponentId == Inventory.Inventory.CurrencyItem)
                    return (uint)Math.Ceiling((double)components[0].ComponentCount / recipe.ItemCount);
            }
            return 0;
        }

        /// <summary>
        /// The priced store: <see cref="MarketplaceProducts"/> at their converted prices, then any other catalogue item the
        /// owner priced in StorePrices ("id=Lumens,...", Docker STORE_PRICES) or StoreLumenPrices (the dictionary form, which
        /// wins for the same id). An owner's price replaces a product's.
        /// </summary>
        private static List<Item> PricedCatalogue()
        {
            string Text(uint id) => id == 0 ? "" : TextManager.Instance.GetTextForId(id) ?? "";
            var overrides = new Dictionary<uint, uint>();
            foreach (string pair in (Config?.StorePrices ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] kv = pair.Split('=', 2, StringSplitOptions.TrimEntries);
                if (kv.Length == 2 && uint.TryParse(kv[0], out uint item) && uint.TryParse(kv[1], out uint lumens))
                    overrides[item] = lumens;
                else
                    log.Warn($"Store: StorePrices entry '{pair}' is not \"item id=Lumens\"; ignored.");
            }
            foreach (var (key, value) in Config?.StoreLumenPrices ?? new Dictionary<string, uint>())
                if (uint.TryParse(key, out uint id))
                    overrides[id] = value;
            var items = new List<Item>();
            foreach (Product product in MarketplaceProducts)
            {
                var d = Inventory.ItemDefinitionManager.Get(product.Item);
                string name = d == null ? "" : Text(d.NameId).Trim();
                if (name.Length == 0)
                {
                    log.Warn($"Store: Marketplace product {product.Item} has no item record or name; left out.");
                    continue;
                }
                uint price;
                string note;
                if (overrides.TryGetValue(product.Item, out uint set))
                    (price, note) = (set, "set by this server");
                else if (product.DaybreakCash > 0)
                    (price, note) = (LumensFor(product.DaybreakCash), product.Source);
                else if ((price = LumenStationPrice(product.Item)) > 0)
                    note = product.Source;
                else
                {
                    log.Warn($"Store: no price for Marketplace product {product.Item} ({name}); left out.");
                    continue;
                }
                items.Add(new Item(d.Id, name, Plain(Text(d.DescriptionId)), d.ImageSetId, product.Section, product.Group, price, note));
            }
            var listed = items.Select(i => i.Id).ToHashSet();
            foreach (Item extra in Everything().Where(i => overrides.ContainsKey(i.Id) && !listed.Contains(i.Id)))
                items.Add(extra with { Price = overrides[extra.Id], PriceNote = "set by this server" });
            // The Build Site Vendor is this server's working shop (Vendors; live's Vendor Booth was decorative, P4378) and
            // its prop is in no palette category, so the store stays its only route: free, until TASKS 6.52 is decided.
            listed = items.Select(i => i.Id).ToHashSet();
            foreach (Item vendor in Everything().Where(i => i.Group == VendorGroup && !listed.Contains(i.Id)))
                items.Add(vendor with { PriceNote = "this server's vendor feature, not a live product" });
            return items.OrderBy(i => Array.IndexOf(SectionOrder, i.Section)).ThenBy(i => i.Group).ThenBy(i => i.Name).ToList();
        }

        /// <summary>The whole catalogue (built once), every item free: the store with FreeStore on.</summary>
        private static List<Item> Everything()
        {
            if (everything != null)
                return everything;
            var items = new List<Item>();
            string Text(uint id) => id == 0 ? "" : TextManager.Instance.GetTextForId(id) ?? "";

            // Prop categories: top-level name for each prop (a child category reads "Parent: Child").
            var categoryName = GameDataFiles.Rows("PropCategory.txt").ToDictionary(r => r.U(0), r => Text(r.U(1)));
            var parentOf = new Dictionary<uint, uint>();
            foreach (string[] r in GameDataFiles.Rows("PropCategoryGroup.txt"))
                parentOf.TryAdd(r.U(1), r.U(0));
            var propCategory = new Dictionary<uint, string>();
            foreach (string[] r in GameDataFiles.Rows("PropCategoryPropMap.txt"))
            {
                uint category = r.U(1), prop = r.U(2);
                string name = categoryName.GetValueOrDefault(category, "");
                if (parentOf.TryGetValue(category, out uint parent) && categoryName.TryGetValue(parent, out string parentName)
                    && parentName.Length > 0)
                    name = name.Length > 0 ? $"{parentName}: {name}" : parentName;
                if (name.Length > 0)
                    propCategory.TryAdd(prop, name);
            }

            foreach (var d in Inventory.ItemDefinitionManager.All)
            {
                string name = Text(d.NameId).Trim();
                if (name.Length == 0 || name.StartsWith("<", StringComparison.Ordinal) || name.Contains("TEST", StringComparison.Ordinal))
                    continue;
                string section, group;
                if (d.EquipSlotId == 1)
                    (section, group) = ("Outfits", "Outfits");
                else if (Pets.IsFigurine(d.Id) && d.Id != Inventory.ItemDefinitionBuilder.LopEarBunnyFigurine)
                    // Six figurines were Marketplace items (28 Jul 2016, trace/0695/spec-pets.md 5.6); the Lop Ear Bunny
                    // only dropped from Outpost elites (P6143).
                    (section, group) = ("Cosmetics", "Pets");
                else if (d.Id == Unlockers.FantasyParlorPack)
                    // The Fantasy Parlor Pack was sold on the Marketplace (29 Jun 2016, P5910-P5912); the parlor's Unlock
                    // button searches the Marketplace for it (BarberShopWidget.as UnlockButton). The hats were drops.
                    (section, group) = ("Cosmetics", "Crimson Parlor");
                else if (d.PropId == Vendors.VendorProp)
                    (section, group) = ("Decorations", VendorGroup);
                else if (d.PropId != 0 && d.EquipSlotId == 0 && propCategory.TryGetValue(d.PropId, out string category))
                    (section, group) = ("Decorations", category);
                else if (d.EquipSlotId == 0 && d.Abilities.Count == 0 && ItemUses.IsDesign(d.Id))
                    (section, group) = ("Designs", "Designs");
                else
                    continue;
                items.Add(new Item(d.Id, name, Plain(Text(d.DescriptionId)), d.ImageSetId, section, group));
            }
            return everything = items.GroupBy(i => (i.Section, i.Name)).Select(g => g.OrderBy(i => i.Id).First())
                .OrderBy(i => Array.IndexOf(SectionOrder, i.Section)).ThenBy(i => i.Group).ThenBy(i => i.Name).ToList();
        }

        /// <summary>Client text without its markup (&lt;font color=...&gt;, &lt;br&gt;).</summary>
        private static string Plain(string text)
            => System.Text.RegularExpressions.Regex.Replace(
                System.Text.RegularExpressions.Regex.Replace(text ?? "", "<br */?>", " "), "<[^>]*>", "").Trim();

        /// <summary>
        /// Gives a catalogue item, for its price in Lumens (free items for nothing). Runs on the world thread. The count is
        /// cut to the bag's room and to what the player's Lumens pay for; the Lumens are taken before the item is given
        /// (both on this thread, so nothing in between can spend them). Returns a short message for the page; the player
        /// also sees the item arrive in their bag and the Lumen count drop.
        /// </summary>
        public static (bool Ok, string Message) Take(WorldSession session, uint itemId, uint count)
        {
            Purchase purchase = Buy(session, itemId, count, partial: true);
            return (purchase.Result == PurchaseResult.Ok, purchase.Message);
        }

        /// <summary>How a store purchase ended (<see cref="Buy"/>).</summary>
        public enum PurchaseResult { Ok, NotInStore, TooFast, BagFull, CannotAfford, Refused }

        /// <summary>A store purchase's outcome: the result, the page's message, the item's name, how many and what it cost.</summary>
        public sealed record Purchase(PurchaseResult Result, string Message, string ItemName = "", uint Count = 0, long Cost = 0);

        /// <summary>
        /// <see cref="Take"/>'s purchase, with the reason when it fails. <paramref name="partial"/> (the Showcase page): the
        /// count is cut to the bag's room and to what the player's Lumens pay for; without it (the client's in-game
        /// purchase, StoreBundles.HandlePurchase) the whole count is bought or nothing (the client's own check, CanPurchase
        /// FUN_143248df0, compares price × quantity with the currency it holds). World thread.
        /// </summary>
        public static Purchase Buy(WorldSession session, uint itemId, uint count, bool partial)
        {
            Item item = Catalogue().FirstOrDefault(i => i.Id == itemId);
            if (item == null)
                return new(PurchaseResult.NotInStore, "That item isn't in the store.");
            if (itemId == MembershipStoreId)
            {
                (uint price, uint days) = Membership.Offer;
                // Membership.Purchase refuses for the Lumens once the offer and the account are there; otherwise "Refused".
                bool tooPoor = price > 0 && days > 0 && session.Account != null && session.Coins < price;
                (bool bought, string text) = Membership.Purchase(session);
                return new(bought ? PurchaseResult.Ok : tooPoor ? PurchaseResult.CannotAfford : PurchaseResult.Refused, text, item.Name, 1,
                    bought ? price : 0);
            }
            // Members and trials pay less (Membership.PriceFor, MemberStoreDiscountPercent).
            if (item.Price > 0)
                item = item with { Price = Membership.PriceFor(session, item.Price) };
            count = Math.Clamp(count, 1, MaxCount);
            lock (sync)
            {
                if (lastGrant.TryGetValue(session, out DateTime last) && DateTime.UtcNow - last < GrantInterval)
                    return new(PurchaseResult.TooFast, "One moment...", item.Name);
                lastGrant[session] = DateTime.UtcNow;
            }
            // A purchase that will wait to become tradeable gets stacks of its own (Inventory.Add), so only free slots count.
            uint waitHours = item.Price > 0 ? ConfigurationManager<WorldServerConfiguration>.Instance.Config.StoreTradeWaitHours : 0;
            long space = session.Inventory.SpaceFor(itemId, plain: waitHours == 0);
            if (space <= 0 || (!partial && space < count))
                return new(PurchaseResult.BagFull, "Your bag is full.", item.Name);
            count = (uint)Math.Min(count, space);
            long cost = 0;
            if (item.Price > 0)
            {
                uint lumens = session.Coins;
                if (!partial && (long)item.Price * count > lumens)
                    return new(PurchaseResult.CannotAfford, $"{count} × {item.Name} cost {(long)item.Price * count:N0} Lumens; you have {lumens:N0}.", item.Name);
                count = (uint)Math.Min(count, lumens / item.Price);
                if (count == 0)
                    return new(PurchaseResult.CannotAfford, $"{item.Name} costs {item.Price:N0} Lumens; you have {lumens:N0}.", item.Name);
                cost = (long)item.Price * count;
                if (!session.ChangeCoins(-cost))
                    return new(PurchaseResult.CannotAfford, $"{item.Name} costs {item.Price:N0} Lumens; you have {session.Coins:N0}.", item.Name);
            }
            // A purchase waits STORE_TRADE_WAIT_HOURS before it can change hands (live's "fraud timer", audit-wiki-gaps #7).
            if (cost > 0 && waitHours > 0)
                session.GiveItem(itemId, count, new Inventory.ItemState(TradeableAfter: DateTime.UtcNow.AddHours(waitHours)));
            else
                session.GiveItem(itemId, count);
            Commerce.RecordSpend(session, cost);   // the Player Studio reviewer rule (Commerce.MayReview)
            log.Info($"Store: {session.CharacterName} took {count} × {item.Name} ({itemId})"
                + (cost > 0 ? $" for {cost} Lumens." : " (free)."));
            string what = count == 1 ? $"{item.Name} is in your bag" : $"{count} × {item.Name} are in your bag";
            return new(PurchaseResult.Ok, cost > 0 ? $"{what} ({cost:N0} Lumens)." : $"{what}.", item.Name, count, cost);
        }

        /// <summary>
        /// The gallery: the build sites their owners published (Build Site Management › Publish to Gallery = a free
        /// Player Studio listing of item type 3, <see cref="Commerce"/>) and tagged with all three drop-downs
        /// (<see cref="Game.Gallery.IsTagged"/>; untagged sites are hidden, P4591), most liked first. The page builds
        /// its leaderboard (most liked), rising stars (most recently listed), tag / name search and Play Mode filter from
        /// this one list (trace/0695/gallery-695.md §6).
        /// </summary>
        public static List<Site> Gallery()
        {
            var online = PlayerManager.Snapshot().Select(s => s.PlayerGuid).ToHashSet();
            // The listings and contest names are the commerce back end's (in-process, or the commerce service's caches).
            IReadOnlyDictionary<ulong, Commerce.Listing> listings = WorldChannelServices.Commerce.GalleryListings();
            var published = Claims.All().Where(c => listings.ContainsKey(c.Id) && Game.Gallery.IsTagged(c.Tags)).ToList();
            var names = DatabaseManager.Instance.CharacterDatabase.GetCharacterNames(published.Select(c => c.OwnerGuid).Distinct());
            Dictionary<ulong, int> comments = Game.Gallery.CommentCounts();
            return published.Select(c => new Site(c.Id, c.Name ?? "", names.GetValueOrDefault(c.OwnerGuid, "?"),
                    LandscapeName(c.ZoneId), c.Description ?? "", WorldChannelServices.WorldChannel.ClaimVoteTotal(c.Id), online.Contains(c.OwnerGuid),
                    Game.Gallery.Parse(c.Tags), Claims.IsPlaying(c.Id), new DateTimeOffset(listings[c.Id].ListedAt).ToUnixTimeSeconds(),
                    SiteScreenshots.HasImage(c.Id, 0, "small") ? SiteScreenshots.ImagePath(c.Id, 0, "small") : "",
                    comments.GetValueOrDefault(c.Id), WorldChannelServices.Commerce.ContestNamesOf(c.Id)))
                .OrderByDescending(s => s.Rating).ThenByDescending(s => s.ListedAt).ThenBy(s => s.Name).ToList();
        }

        private static string LandscapeName(uint zone)
        {
            uint text = Landscapes.Get(zone)?.NameText ?? 0;
            string name = text == 0 ? "" : TextManager.Instance.GetTextForId(text);
            return string.IsNullOrEmpty(name) ? $"Landscape {zone}" : name;
        }

        /// <summary>
        /// The gallery's Visit: travel to a build site (its Starting Location, else its centre). World thread. Only a
        /// listed site can be reached this way (the Travel menu's "Teleport to any Build Site in the Gallery",
        /// TeleportWidget.as).
        /// </summary>
        public static (bool Ok, string Message) Visit(WorldSession session, ulong siteId)
        {
            ClaimModel site = Claims.Get(siteId);
            if (site == null || Claims.ArrivalOf(site.OwnerGuid) is not (Vector3 arrival, uint zone, ulong claimId) || claimId != siteId)
                return (false, "That build site is gone.");
            if (!WorldChannelServices.Commerce.IsOnGallery(siteId))
                return (false, "That build site isn't listed in the gallery.");
            StoryTools.ExpectArrival(session, siteId, respawn: false);
            Vector3 ground = Terrain.TerrainHeights.OnGround(arrival.X, arrival.Z, zone) ?? arrival;
            Teleport.ToZone(session, ground + new Vector3(0f, 1f, 0f), zone, Claims.ArrivalFacingOf(site.OwnerGuid));
            Achievements.TravelledToSite(session, siteId);   // Getting Around: visit another player's site (1308)
            log.Info($"Store: {session.CharacterName} visits build site {siteId} '{site.Name}'.");
            return (true, $"Travelling to {(string.IsNullOrEmpty(site.Name) ? "the build site" : site.Name)}...");
        }
    }
}
