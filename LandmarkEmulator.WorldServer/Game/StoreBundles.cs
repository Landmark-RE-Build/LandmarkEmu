using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The client's own Marketplace product records (TASKS 4.9): the store bundles live's store server sent the client.
    /// Every product live's Marketplace sold that this server's Showcase store sells (<see cref="Store.Catalogue"/>,
    /// <see cref="Store.IsMarketplaceProduct"/>: the Build Site Expansion, the Fantasy Parlor Pack, the six pet
    /// figurines, the Superb / Legendary Reward Chests) plus the store's membership row goes to the client as one store
    /// (id 1), each product priced for that player. Buying still happens on the Showcase page (<see cref="Store"/>): no
    /// 0.695 UI movie has a store or bundle window. The client's own buy request, PacketClientInGamePurchase 0xD0 (Lua
    /// PurchaseBundleWithInGameCurrency, bound by no shipped script), is answered too (<see cref="HandlePurchase"/>, since
    /// 2026-10-18): the same purchase, for Lumens.
    /// Docs: docs/showcase.md "The client's store products", docs/protocol.md (InGamePurchase); trace: trace/0695/leads-L39.md
    /// and the notes below.
    /// <para>
    /// <b>Who reads the client's store</b> [P, 0.695 decompile; every caller of the store lookups FUN_1435800f0 /
    /// FUN_143c2c370 / the item → bundle map (store manager +0xd98) walked to its root in the 0.695 exe]:
    /// <list type="bullet">
    /// <item>The Crimson Parlor (the one shipped UI): the option-list builder FUN_1430a33a0 lists a locked row (not
    /// DEFAULT_UNLOCKED, not known) when the item → bundle map holds its UNLOCKING_ITEM_ID; the slot data source
    /// FUN_1430a37a0 then sets PurchaseRequired = 1, MarketName = the text of the bundle's name id (+0x17c) and MarketCost
    /// = the price FUN_143c2c370 gives (base price +0x18c unless a sale applies) of the bundle in store 1
    /// (FUN_1435800f0(mgr+0x460, {1, bundle})). The tint list FUN_1430a4720 / FUN_1430a3210 does the same for the tints
    /// of ArtTintGroupUnlock groups (item 4419). BarberShopWidget.as 293-298 shows the Unlock button with "You have
    /// selected an option that requires "MarketName". Click to purchase this from the Marketplace."; the button calls Lua
    /// OpenMarketplaceTagSearch(MarketName) (global.lua 619) → InGameMarketplaceBrowser:OpenTagSearch → Browser.OpenGalleryWithTags
    /// (FUN_1435e0db0): the Showcase's store search, which lists the product.</item>
    /// <item>Nothing else that ships reads it. The item → bundle map is also read by the Lua natives
    /// InGamePurchaseStoreScreen.DisplayStoreBundlesContainingPlayerInspectGameItem (FUN_1435e8b00: Lua event
    /// OnStorePlayerInspectPreview) and ...ExpiredRentalGameItem (FUN_1435e8bc0: OnStoreBundleRentalExpired), which no 0.695
    /// Lua script, compiled script (ScriptsBase.bin) or UI movie calls or handles, and by FUN_142e45f60 (an item's store
    /// flags at +0x1a9 / +0x1aa from FUN_143c2c530 and bundle +0x1e4), which nothing calls (no call / jump, no pointer
    /// in the 0.695 exe; also uncalled in 0.589 as FUN_142da7f90). The store's data sources (MarketingData.InGamePurchase
    /// StoreBundles ..., row filler FUN_143703740, columns FUN_1436fcd40), its search engine and the EquipScreen /
    /// CanPurchaseStoreBundle natives are bound by no 0.695 movie or script (only MarketingData.AutocompletionTags is).
    /// The fields those unused readers name are filled where the name says what they hold (description, icon,
    /// item quantity); the rest stay 0.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Prices</b>: the Showcase's Lumen price for this player (Membership.PriceFor: members' and trials' discount, set
    /// by the owner; FREE_STORE gives 0), as the base price (+0x18c). The client's own price rule (FUN_143c2c370 /
    /// FUN_143c2c460) only lowers it for an item of the day (+0x1e8 / +0x1ec member-only, sale price +0x1dc) or a
    /// scheduled sale (+0x1d3 / discount type +0x1c4 with +0x278 &gt; 0); the column MemberSalePrice (+0x290) is shown by no
    /// UI. So the player's own price goes in the base price, and the store is sent again when it changes: a membership
    /// starting or ending (<see cref="Refresh"/> from Membership). Membership itself is never discounted
    /// (Membership.Purchase charges the full price), as on the page.
    /// </para>
    /// <para>
    /// <b>What the client does with the packet</b> [P]: InGamePurchase 0x45 sub 5 StoreBundleBase (dispatcher FUN_143258f50
    /// case 5 → FUN_143257d60; header reader FUN_14322ed40: u16 opcode, u16 sub, u32 type, u32 store id). Type 1 =
    /// PacketInGamePurchaseStoreBundles: the store with that id is found or created (FUN_14357aad0 → FUN_1435818b0), its
    /// bundle list emptied and read again (FUN_14322c6f0 → FUN_1432330d0, which removes every old bundle first), and the
    /// packet must be consumed exactly or it is dropped ("Failed to serialize PacketInGamePurchaseStoreBundles packet.");
    /// then ClientInGamePurchase.log gets "Added/Updated new store successfully - Store: 1, Count: n" and FUN_1432625d0 →
    /// FUN_1435851b0 rebuilds the item → bundle map from scratch (FUN_143586d30 clears it; FUN_143584f70 →
    /// FUN_1435793e0 adds the bundle's item ids: all of them, or only the first while the bundle has fewer than 2). So a
    /// resend replaces the store; nothing piles up. Type 2 adds one bundle through AddStoreBundle FUN_143579260, only to a
    /// store that already exists (DAT_14526873c = 1), so type 1 is what is sent. A bundle whose type-1 item the client has
    /// no definition for is remembered as unavailable (FUN_143584f70, set +0x3e0); every product here is an item the
    /// server defines for the client.
    /// </para>
    /// <para>
    /// <b>Wire</b> (u16 sub 5, then): u32 type 1, u32 store 1, store {u32 (+8, the store id) [I], u32 (+0xc), u32
    /// (+0x10), string (+0x20), string (+0x38)}, u32 n bundles, n × {u32 bundle id (+0xfe8, the hash key),
    /// AppStoreBundleDefinition (FUN_14322cae0)}. The bundle: base StoreBundleDefinition FUN_14322d330 {u32 bundle id
    /// +0x178 (column BundleId), u32 name text +0x17c (Name), u32 description text +0x180 (Description), u32 +0x184,
    /// string +0x20 (ImageSetId: the column parses it as a number, FUN_142b213d0), string +0x38 (ImageTintValue /
    /// AvailableTintGroupId, parsed the same way), bool +0x1a1 (IsTintable), string +0x50, u32 +0x188 (CurrencyId), u32
    /// price +0x18c (CurrencyPrice), u32 +0x190 (AltCurrencyId), +0x194 (AltCurrencyPrice), +0x198, u64 +0x8, u64 +0x10,
    /// u32 +0x19c, bool +0x1a0, u32 n items × {u32 quantity +8 (the item list's "Quantity", FUN_143700ce0), u32 +0xc,
    /// u64 item id +0x10, u32 type +0x18 (1 = item: FUN_143584f70 looks type-1 ids up in the item definitions), string
    /// +0x20, string +0x38} (FUN_1432337c0 / FUN_14322d0a0), u32 n × {u32, u32} (FUN_143233f30)}, then FUN_14322fa80 {u32
    /// store id +0x1a8 (StoreId; AddStoreBundle matches it to the store), u32 category +0x1ac (CategoryId), bool on sale
    /// +0x1d3, 8 × u32 +0x1b0..+0x1cc (+0x1b0 terms text, +0x1b4 MaxPerOrder, +0x1c4 DiscountType, +0x1c8
    /// DiscountValue), bool +0x1d0, +0x1d1 (IsNewItem), +0x1d2 (IsViewOnly)}, then FUN_14322cae0's own {6 × u32
    /// +0x1d8..+0x1ec (+0x1dc sale price, +0x1e0 IsPrimaryItemDisplayBundle / IsMemberOnly, +0x1e4, +0x1e8
    /// IsItemOfTheDay, +0x1ec IsMemberOnlyItemOfTheDay), string +0x1f0 (OverrideAppearance), u32 +0x208
    /// (OverrideCameraId), u32 +0x278 (HasSaleSchedule when &gt; 0), u32 +0x28c, +0x290 (MemberSalePrice), +0x294, u64
    /// +0x210, string +0x218 (PlayerStudioDisplayName), u32 +0x298, +0x29c, +0x2a0, bool +0x2a4, u64 +0x2b0, u32 +0x2a8,
    /// bool +0x3f4}. A non-zero u64 +0x210 or a non-empty string +0x218 marks a Player Studio product, which the client
    /// may drop ("Ignoring bundle ... because Player Studio is not allowed", FUN_143579260 / FUN_1432625d0), so both stay
    /// empty. Column names: the row filler FUN_143703740 with FUN_1436fcd40.
    /// </para>
    /// <para>Bandwidth: one packet of ~250 bytes per product (about 2.7 KB for live's 2016 Marketplace and membership) at world entry, and
    /// again only when this player's prices change (the same bytes are never sent twice in a row).</para>
    /// </summary>
    public static class StoreBundles
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>InGamePurchase StoreBundleBase sub and its type for a whole store (PacketInGamePurchaseStoreBundles).</summary>
        public const ushort SubStoreBundleBase = 5;
        public const uint TypeStoreBundles = 1;

        /// <summary>
        /// The store the client looks products up in: DAT_14526873c (AddStoreBundle) and the literal 1 of the Parlor's
        /// lookups (FUN_1430a37a0 / FUN_1430a4720) [P].
        /// </summary>
        public const uint ClientStoreId = 1;

        /// <summary>Item list entry type of a game item (FUN_143584f70 tests type 1 against the item definitions) [P].</summary>
        private const uint ItemEntryType = 1;

        /// <summary>Our bundle ids: this base + the item id (live's ids are not on disk; any non-zero id works).</summary>
        public const uint BundleIdBase = 1_000_000;

        /// <summary>The membership row's bundle id (below every item bundle id, so it can't meet one).</summary>
        public const uint MembershipBundleId = BundleIdBase - 1;

        /// <summary>
        /// Client text 55472 "MEMBERSHIP" (the membership screen's title): the membership row's name [I: live sold
        /// membership through its own subscription products, not a store bundle, so no bundle name exists for it].
        /// </summary>
        private const uint MembershipNameText = 55472;

        /// <summary>The store packet each client was last sent, so a refresh with the same prices sends nothing.</summary>
        private static readonly ConditionalWeakTable<WorldSession, byte[]> sent = new();

        /// <summary>One product as the client gets it.</summary>
        private readonly record struct Bundle(uint Id, uint Item, uint Name, uint Description, uint Icon, uint Price);

        /// <summary>
        /// World entry: the client's store 1 with every product (<see cref="Products"/>). Nothing is sent when the store
        /// sells none of them.
        /// </summary>
        public static void Send(WorldSession session) => Push(session, force: true);

        /// <summary>
        /// The player's prices may have changed (membership started or ended): the store again, if it differs from what the
        /// client has.
        /// </summary>
        public static void Refresh(WorldSession session) => Push(session, force: false);

        private static void Push(WorldSession session, bool force)
        {
            if (session == null || session.Disconnected)
                return;
            List<Bundle> bundles = Products(session);
            if (bundles.Count == 0)
            {
                sent.Remove(session);
                return;
            }
            byte[] packet = Build(bundles);
            lock (sent)
            {
                if (!force && sent.TryGetValue(session, out byte[] last) && last.AsSpan().SequenceEqual(packet))
                    return;
                sent.AddOrUpdate(session, packet);
            }
            session.EnqueueMessage(ZoneMessageOpcode.InGamePurchaseBase, packet);
            log.Info($"Store bundles to {session.CharacterName} ({packet.Length} bytes): "
                + string.Join(", ", bundles.Select(b => $"{(b.Item == 0 ? "membership" : b.Item.ToString())} at {b.Price}")) + " Lumens.");
        }

        /// <summary>
        /// The products the client gets: the membership row (when the store lists it; full price, as Membership.Purchase
        /// charges) and every live Marketplace product the Showcase store sells now, at this player's price, in the
        /// store's order. Items without a client name are left out (the Parlor shows the name).
        /// </summary>
        private static List<Bundle> Products(WorldSession session)
        {
            var bundles = new List<Bundle>();
            foreach (Store.Item product in Store.Catalogue())
            {
                if (product.Id == Store.MembershipStoreId)
                {
                    bundles.Add(new Bundle(MembershipBundleId, 0, MembershipNameText, 0, 0, product.Price));
                    continue;
                }
                if (!Store.IsMarketplaceProduct(product.Id))
                    continue;
                var definition = Inventory.ItemDefinitionManager.Get(product.Id);
                if (definition == null || definition.NameId == 0)
                    continue;
                bundles.Add(new Bundle(BundleIdBase + product.Id, product.Id, definition.NameId, definition.DescriptionId,
                    definition.ImageSetId, Membership.PriceFor(session, product.Price)));
            }
            return bundles;
        }

        // ---- buying a bundle with in-game currency (PacketClientInGamePurchase 0xD0) ----------------------------------------

        /// <summary>
        /// cPlayerInGamePurchaseResult codes: the client's own purchase check CanPurchaseStoreBundle (0.695 FUN_143248df0,
        /// called by the Lua native CanPurchaseStoreBundle FUN_1435ea290 and the store rows' filler FUN_143703740) returns 1
        /// = can buy, 3 = the bundle (store 1) or its item is unknown / not for sale, 4 = quantity below 1 or above the
        /// item's stack, 7 = not enough currency (alt-currency price +0x194 × quantity against the player's currency of id
        /// +0x190, else the Daybreak Cash price against the wallet), 0xF = the store or player not ready [P codes]. 0 =
        /// another failure (bag full, too fast, membership refused): no client code is seen for these [I].
        /// </summary>
        public const uint PurchaseOk = 1, PurchaseUnavailable = 3, PurchaseBadQuantity = 4, PurchaseCannotAfford = 7, PurchaseFailed = 0;

        /// <summary>
        /// The client's in-game currency purchase texts (CodeStringMappings ClaimVendor.PurchaseSuccess /
        /// PurchaseFailedInsufficientFunds / PurchaseFailedNoSuchItem / PurchaseFailedGeneric, shown by the claim vendor's
        /// HandlePurchaseResponse FUN_14337aaf0 for its results 1 / 2 / 3 / other) [P texts]; the only purchase-result texts
        /// for in-game currency the 0.695 client has, so the in-game store's purchase uses them too [I].
        /// </summary>
        private const uint TextPurchased = 568731, TextNotEnough = 568732, TextNotForSale = 568733, TextFailed = 568734;

        /// <summary>
        /// PacketClientInGamePurchase 0xD0 {u32 Lua argument 1, u32 Lua argument 3, u32 0} (Network/Message/Model
        /// InteractionMessages.ClientInGamePurchase; native InGamePurchaseStoreScreen.PurchaseBundleWithInGameCurrency,
        /// 0.695 FUN_1435e9400) [P layout]: buys a bundle of the client's store (<see cref="Send"/>: store 1, our bundle ids)
        /// for Lumens through the Showcase store's own purchase (<see cref="Store.Buy"/>: the same item, the same price for
        /// this player — Membership.PriceFor, nothing with FREE_STORE —, the Lumens taken before the item is given; the
        /// membership row runs Membership.Purchase), as a Showcase purchase does. All or nothing: the whole quantity or no
        /// purchase, as the client's own check (FUN_143248df0) compares price × quantity with the currency.
        /// <para>
        /// <b>Which field is which [I]</b>: no shipped script calls the native, so its arguments' meaning is not traced. The
        /// store natives around it take (store id, bundle id, …) (EquipStoreBundle FUN_1435e8d40 → "storeId: %d,
        /// storeBundleId: %d"; CanPurchaseStoreBundle uses argument 2 as the bundle), yet argument 2 is the one this native
        /// leaves out. So the server takes as the bundle whichever of the two fields is one of this player's bundle ids
        /// (the first field first), and the other field as the quantity when it is 1 … <see cref="Store.MaxCount"/>, else 1
        /// (a store id 1 reads as quantity 1).
        /// </para>
        /// <para>
        /// <b>The reply</b>: PlayerUpdate 0x4B cPlayerInGamePurchaseResult (ZoneMessageOpcode.PlayerUpdateInGamePurchaseResultWire),
        /// whose reader (0.695 dispatcher FUN_142cdfa60 case 0x4a → FUN_142c4b300 → FUN_142c25490) takes u16 base, u32 sub
        /// and exactly three u32 (+0x18, +0x1c, +0x20), and does nothing else with them: the client parses the result and
        /// drops it [P]. Their meaning is not traced; the request's own shape (two values and a 0 at +0x18) suggests the
        /// answer echoes the two and fills the third with the result, which is what is sent: first field, second field,
        /// result code (<see cref="PurchaseOk"/> …) [I]. The player sees the purchase through what the client does show:
        /// the item arriving in the bag, the Lumens counter (PacketPlayerCurrencyChange) and a chat line with the client's
        /// own purchase text (<see cref="TextPurchased"/> …).
        /// </para>
        /// </summary>
        public static void HandlePurchase(WorldSession session, uint first, uint second, uint third)
        {
            if (session == null || session.Disconnected)
                return;
            List<Bundle> bundles = Products(session);
            uint bundleId = bundles.Any(b => b.Id == first) || !bundles.Any(b => b.Id == second) ? first : second;
            uint other = bundleId == first ? second : first;
            uint quantity = other >= 1 && other <= Store.MaxCount ? other : 1;
            Bundle? bundle = bundles.Where(b => b.Id == bundleId).Select(b => (Bundle?)b).FirstOrDefault();
            uint code;
            string line;
            if (bundle == null)
            {
                code = PurchaseUnavailable;
                line = Text(TextNotForSale, "Your purchase has failed because that item is no longer for sale.");
                log.Info($"In-game purchase from {session.CharacterName}: no bundle {first} / {second} in their store (third field {third}).");
            }
            else
            {
                uint item = bundle.Value.Item == 0 ? Store.MembershipStoreId : bundle.Value.Item;
                Store.Purchase purchase = Store.Buy(session, item, item == Store.MembershipStoreId ? 1 : quantity, partial: false);
                (code, line) = purchase.Result switch
                {
                    Store.PurchaseResult.Ok => (PurchaseOk, Text(TextPurchased, "You have sucessfully purchased #count([*item*])!")
                        .Replace("#count([*item*])", purchase.Count > 1 ? $"{purchase.Count} {purchase.ItemName}" : purchase.ItemName)),
                    Store.PurchaseResult.CannotAfford => (PurchaseCannotAfford,
                        Text(TextNotEnough, "Your purchase has failed because you do not have enough [*currencyName*].").Replace("[*currencyName*]", "Lumens")),
                    Store.PurchaseResult.NotInStore => (PurchaseUnavailable,
                        Text(TextNotForSale, "Your purchase has failed because that item is no longer for sale.")),
                    _ => (PurchaseFailed, Text(TextFailed, "Your purchase has failed for an unknown reason.") + $" ({purchase.Message})")
                };
                log.Info($"In-game purchase from {session.CharacterName}: bundle {bundleId} (item {item}) × {quantity}: {purchase.Result}"
                    + (purchase.Cost > 0 ? $", {purchase.Cost} Lumens." : "."));
            }
            var body = new List<byte>(12);
            var w = new GamePacketWriter(body);
            w.Write(first);
            w.Write(second);
            w.Write(code);
            session.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateInGamePurchaseResultWire, body.ToArray());
            Chat.System(session, line);
        }

        private static string Text(uint id, string fallback)
            => Shared.GameTable.Text.TextManager.Instance.GetTextForId(id) is { Length: > 0 } text ? text : fallback;

        /// <summary>The InGamePurchase body (sub 5 type 1, store 1) holding these bundles.</summary>
        private static byte[] Build(List<Bundle> bundles)
        {
            var output = new List<byte>(64 + bundles.Count * 220);
            var w = new GamePacketWriter(output);
            w.WriteUShort(SubStoreBundleBase);
            w.Write(TypeStoreBundles);
            w.Write(ClientStoreId);
            // Store (FUN_14322c6f0).
            w.Write(ClientStoreId);   // +0x08 (the store id before the read; overwritten by this field) [I]
            w.Write(0u);              // +0x0c [I]
            w.Write(0u);              // +0x10 [I]
            w.Write("");              // +0x20 [I]
            w.Write("");              // +0x38 [I]
            w.Write((uint)bundles.Count);
            foreach (Bundle bundle in bundles)
            {
                w.Write(bundle.Id);   // hash key (+0xfe8), FUN_1432330d0
                WriteBundle(w, bundle);
            }
            return output.ToArray();
        }

        /// <summary>One AppStoreBundleDefinition (FUN_14322cae0, field order and offsets in the class notes).</summary>
        private static void WriteBundle(GamePacketWriter w, Bundle b)
        {
            // StoreBundleDefinition, FUN_14322d330.
            w.Write(b.Id);            // +0x178 bundle id
            w.Write(b.Name);          // +0x17c name text: the Parlor's MarketName (FUN_1430a37a0)
            w.Write(b.Description);   // +0x180 description text (column Description)
            w.Write(0u);              // +0x184 [I]
            w.Write(b.Icon == 0 ? "" : b.Icon.ToString(System.Globalization.CultureInfo.InvariantCulture)); // +0x20 image set id (column ImageSetId)
            w.Write("");              // +0x38 tint (ImageTintValue / AvailableTintGroupId): none
            w.Write(false);           // +0x1a1 IsTintable: no
            w.Write("");              // +0x50 [I]
            w.Write(0u);              // +0x188 CurrencyId [I: no UI reads it; Lumens have no store currency id traced]
            w.Write(b.Price);         // +0x18c base price: MarketCost (FUN_143c2c370)
            w.Write(0u);              // +0x190 AltCurrencyId [I]
            w.Write(0u);              // +0x194 AltCurrencyPrice [I]
            w.Write(0u);              // +0x198 [I]
            w.Write(0ul);             // +0x08 [I]
            w.Write(0ul);             // +0x10 [I]
            w.Write(0u);              // +0x19c [I]
            w.Write(false);           // +0x1a0 [I]
            if (b.Item == 0)
                w.Write(0u);          // items: none (membership)
            else
            {
                w.Write(1u);          // items (FUN_1432337c0)
                w.Write(1u);          //   +0x08 quantity: the Showcase gives one per purchase
                w.Write(0u);          //   +0x0c [I]
                w.Write((ulong)b.Item); // +0x10 item id: the item → bundle map key (FUN_1435793e0)
                w.Write(ItemEntryType); // +0x18 type 1 = item
                w.Write("");          //   +0x20 [I]
                w.Write("");          //   +0x38 [I]
            }
            w.Write(0u);              // u32 n × {u32, u32} (FUN_143233f30) [I]
            // FUN_14322fa80.
            w.Write(ClientStoreId);   // +0x1a8 store id
            w.Write(0u);              // +0x1ac category: none (the client has no store categories)
            w.Write(false);           // +0x1d3 on sale: no
            for (int i = 0; i < 8; i++)
                w.Write(0u);          // +0x1b0 .. +0x1cc: no terms, MaxPerOrder, discount
            w.Write(false);           // +0x1d0 [I]
            w.Write(false);           // +0x1d1 IsNewItem
            w.Write(false);           // +0x1d2 IsViewOnly
            // AppStoreBundleDefinition, FUN_14322cae0.
            for (int i = 0; i < 6; i++)
                w.Write(0u);          // +0x1d8 .. +0x1ec: no sale price, not an item of the day
            w.Write("");              // +0x1f0 OverrideAppearance: none
            w.Write(0u);              // +0x208 OverrideCameraId: none
            w.Write(0u);              // +0x278 sale schedule: none
            w.Write(0u);              // +0x28c [I]
            w.Write(0u);              // +0x290 MemberSalePrice [I: shown by no UI; the player's price is the base price]
            w.Write(0u);              // +0x294 [I]
            w.Write(0ul);             // +0x210 Player Studio creator: none
            w.Write("");              // +0x218 Player Studio name: none
            w.Write(0u);              // +0x298 [I]
            w.Write(0u);              // +0x29c [I]
            w.Write(0u);              // +0x2a0 [I]
            w.Write(false);           // +0x2a4 [I]
            w.Write(0ul);             // +0x2b0 [I]
            w.Write(0u);              // +0x2a8 [I]
            w.Write(false);           // +0x3f4 [I]
        }
    }
}
