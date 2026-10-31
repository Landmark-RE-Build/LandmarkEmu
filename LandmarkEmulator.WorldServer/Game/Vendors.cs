using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.GameTable.Text;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Build site vendors (player shops): the prop "Build Site_Vendor_Object" (PropDefs <see cref="VendorProp"/>),
    /// placed on a build site, sells what its owner listed, for coins. The 0.589 client has the buyer's side (the
    /// ClaimVendorWindow opened by ClaimVendorHandler:Show, items in ClaimVendorScreen.ItemListEntries, Buy →
    /// ClaimVendorScreen.BuyItem) and the natives for listing (ClaimVendorScreen.ListItem / SetItemPriceByGuid /
    /// SetItemPriceBySlot, with "How much would you like to list this item for? Note: a 10% listing fee will be
    /// applied."), but no screen calls the listing natives, so owners stock their vendors from the Showcase page
    /// ("My vendors", see <see cref="WebPages"/>).
    ///
    /// <para>ClaimVendorBase 0x112 (wire bytes 12 01). Both directions: u16 opcode, u32 sub, u64 vendor guid
    /// (header reader FUN_1432cb500), then per sub (client dispatcher FUN_1432cd470):</para>
    /// <list type="bullet">
    /// <item>S→C 1 UpdateInventory (reader FUN_1432cc620, handler FUN_1432cd730): bool show (opens the window through
    /// Lua HandlerClaimVendor), bool include empty rows, u32 n × item {u64 listing id, u32 item definition, u32 name
    /// text, u32 description text, u32 (shown value, 2 when 0), u32 quantity, u32 price, u32 listing fee paid}
    /// (FUN_1432cb590, rows FUN_1432cd010).</item>
    /// <item>C→S 2 SetPrice {u32 -1, u64 listing, u32 price} → S→C 3 SetPriceResponse {u32, u64 listing, u32 price,
    /// u32 item, u32 currency item, u32 result}.</item>
    /// <item>C→S 4 ListItem {u32 bag slot, u32 price, u32 count} (TestGrid.lua, see ListItemSub) → S→C 5 ListItemResponse {u32 item, u32 fee, u32 count, u32 result,
    /// u32 currency item}.</item>
    /// <item>C→S 6 StopObserving (window closed).</item>
    /// <item>C→S 7 PurchaseItem {u64 listing, u32 quantity} → S→C 8 PurchaseItemResponse {u64 listing, u32 item,
    /// u32 result, u32 quantity, u32 price}.</item>
    /// </list>
    /// Results: 1 success, 2 not enough coins, 3 no such item, else a generic failure (ClaimVendor.* strings). A
    /// vendor that still has stock can't be picked up (PickUpReply error 9, "Prop.PickUpErrorVendorInventoryNotEmpty").
    /// Earnings reach the owner by mail (coins attached), whether online or not.
    /// <para>
    /// Off by default (<see cref="Enabled"/>, setting VendorShops): live made the booth decorative on 1 Apr 2016 (P4378,
    /// the Vendor Booth's old, broken window was removed and the booth kept as decoration).
    /// Off, the booth has no interaction, the Showcase hides My vendors and refuses listings, and <see cref="StartUp"/>
    /// mails any stock left in booths back to the owners.
    /// </para>
    /// </summary>
    public static class Vendors
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const uint VendorProp = 1491;
        public const uint UpdateInventorySub = 1, SetPriceSub = 2, SetPriceResponseSub = 3, ListItemSub = 4,
            ListItemResponseSub = 5, StopObservingSub = 6, PurchaseSub = 7, PurchaseResponseSub = 8;
        public const uint Success = 1, NotEnoughCoins = 2, NoSuchItem = 3, Failed = 4;
        /// <summary>Listing fee: a tenth of the listed value, rounded up.</summary>
        public const double FeeRate = 0.10;
        public const uint MaxPrice = 10_000_000;

        private static readonly object sync = new();
        private static List<ClaimVendorItemModel> listings;
        /// <summary>Who has which vendor's window open (they get its updates).</summary>
        private static readonly Dictionary<ulong, HashSet<WorldSession>> observers = new();

        private static List<ClaimVendorItemModel> Listings()
        {
            if (listings == null)
            {
                using var context = DatabaseManager.Instance.CharacterDatabase.NewWorldContext();
                listings = context.ClaimVendorItem.ToList();
                log.Info($"Loaded {listings.Count} vendor listing(s).");
            }
            return listings;
        }

        /// <summary>Are vendor shops on (setting VendorShops; off = live's decorative booth)?</summary>
        public static bool Enabled => Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config?.VendorShops == true;

        public static bool IsVendor(ulong guid) => Enabled && Props.Find(guid) is { } prop && prop.PropId == VendorProp;

        /// <summary>
        /// Start-up: with shops off, every listing still holding items is mailed back to its owner (the booth stays as a
        /// decorative prop and can be picked up).
        /// </summary>
        public static void StartUp()
        {
            if (Enabled)
                return;
            List<ClaimVendorItemModel> stocked;
            lock (sync)
                stocked = Listings().Where(l => l.Count > 0).ToList();
            foreach (ClaimVendorItemModel listing in stocked)
            {
                uint count = listing.Count;
                lock (sync)
                    listing.Count = 0;
                Save(listing);
                Social.SocialServices.Service.SendSystemMail(listing.OwnerGuid, "Your vendor", $"Returned: {count} × {ItemName(listing.ItemDefinitionId)}",
                    "Vendor shops are off on this server, so the items you listed on your Vendor Booth come back by mail.", 0,
                    new[] { (listing.ItemDefinitionId, count) });
            }
            if (stocked.Count > 0)
                log.Info($"Vendor shops are off: {stocked.Count} listing(s) mailed back to their owners.");
        }

        public static bool HasStock(ulong vendor)
        {
            lock (sync)
                return Listings().Any(l => l.VendorId == vendor && l.Count > 0);
        }

        /// <summary>The listings of one vendor (copies).</summary>
        public static List<ClaimVendorItemModel> StockOf(ulong vendor)
        {
            lock (sync)
                return Listings().Where(l => l.VendorId == vendor && l.Count > 0).Select(Copy).ToList();
        }

        private static ClaimVendorItemModel Copy(ClaimVendorItemModel l) => new()
        {
            Id = l.Id, VendorId = l.VendorId, OwnerGuid = l.OwnerGuid, ItemDefinitionId = l.ItemDefinitionId,
            Count = l.Count, Price = l.Price, FeePaid = l.FeePaid, ListedAt = l.ListedAt
        };

        /// <summary>A player clicked a vendor: its window opens with the stock.</summary>
        public static void Open(WorldSession session, ulong vendor)
        {
            lock (sync)
            {
                if (!observers.TryGetValue(vendor, out HashSet<WorldSession> watching))
                    observers[vendor] = watching = new HashSet<WorldSession>();
                watching.Add(session);
            }
            SendInventory(session, vendor, show: true);
            if (Props.Find(vendor)?.OwnerGuid == session.PlayerGuid && !HasStock(vendor))
                Chat.System(session, "Your vendor is empty: stock it from the Showcase (My vendors tab).");
        }

        private static byte[] Build(uint sub, ulong vendor, Action<GamePacketWriter> body)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(sub);
            w.Write(vendor);
            body(w);
            return output.ToArray();
        }

        private static void SendInventory(WorldSession session, ulong vendor, bool show)
        {
            List<ClaimVendorItemModel> stock = StockOf(vendor);
            session.EnqueueMessage(ZoneMessageOpcode.ClaimVendorBase, Build(UpdateInventorySub, vendor, w =>
            {
                w.Write(show);
                w.Write(false);
                w.Write((uint)stock.Count);
                foreach (ClaimVendorItemModel l in stock)
                {
                    var d = Inventory.ItemDefinitionManager.Get(l.ItemDefinitionId);
                    w.Write(l.Id);
                    w.Write(l.ItemDefinitionId);
                    w.Write(d?.NameId ?? 0u);
                    w.Write(d?.DescriptionId ?? 0u);
                    w.Write(d?.Quality ?? 0u);
                    w.Write(l.Count);
                    w.Write(l.Price);
                    w.Write(l.FeePaid);
                }
            }));
        }

        /// <summary>The vendor's stock changed: everyone with its window open gets the new list (without reopening).</summary>
        private static void Refresh(ulong vendor)
        {
            List<WorldSession> watching;
            lock (sync)
                watching = observers.TryGetValue(vendor, out var set) ? set.Where(s => !s.Disconnected).ToList() : new();
            foreach (WorldSession s in watching)
                SendInventory(s, vendor, show: false);
        }

        /// <summary>ClaimVendorBase from the client (u32 sub, u64 vendor, body).</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.ClaimVendorBase)]
        public static void Handle(WorldSession session, ClaimVendorRequest request)
        {
            if (!Enabled || request.Body.Length < 8)
                return;
            var reader = new GamePacketReader(request.Body);
            ulong vendor = reader.ReadULong();
            switch (request.SubOpcode)
            {
                case StopObservingSub:
                    lock (sync)
                        if (observers.TryGetValue(vendor, out var set))
                            set.Remove(session);
                    break;
                case PurchaseSub when reader.BytesRemaining >= 12:
                {
                    ulong listing = reader.ReadULong();
                    uint quantity = reader.ReadUInt();
                    Buy(session, vendor, listing, quantity);
                    break;
                }
                case SetPriceSub when reader.BytesRemaining >= 16:
                {
                    // {u32 slot, u64 listing, u32 price}: SetItemPriceByGuid (FUN_1435ef300 → FUN_14337ba70) sends slot -1
                    // and the listing; SetItemPriceBySlot (FUN_1435ef390 → FUN_14337bae0) the slot and listing 0. No 0.589
                    // or 0.695 window (TestGrid.lua included) calls BySlot, and the vendor list's rows carry no slot
                    // (FUN_143379e40: listing guid, Name, Description, ItemID, Icon, Quantity, Price, ListingFeePaid), so a
                    // slot-only request finds no listing (result 3) [P callers; the slot's meaning was server-side].
                    reader.ReadUInt();
                    ulong listing = reader.ReadULong();
                    uint price = reader.ReadUInt();
                    (uint result, ClaimVendorItemModel l) = SetPrice(session, listing, price);
                    session.EnqueueMessage(ZoneMessageOpcode.ClaimVendorBase, Build(SetPriceResponseSub, vendor, w =>
                    {
                        w.Write(0u);
                        w.Write(listing);
                        w.Write(price);
                        w.Write(l?.ItemDefinitionId ?? 0u);
                        w.Write(Inventory.Inventory.CurrencyItem);
                        w.Write(result);
                    }));
                    break;
                }
                case ListItemSub when reader.BytesRemaining >= 12:
                {
                    // ClaimVendorScreen.ListItem(a, b, price) (0.589 FUN_14353ced0 → FUN_1432ce850; 0.695 LuaClaimVendor
                    // FUN_1435eeeb0 → FUN_14337b640 {vendor, a, price, b}): a = the bag slot, b = the count [P: the 0.695
                    // install's developer test window UI/TestMod/TestGrid.lua drops an inventory item on the vendor grid
                    // with "LuaClaimVendor:ListItem( data.Slot, data.Quantity, 0 )"; price 0 opens the client's "How much
                    // would you like to list this item for?" dialog, which calls ListItem again with the price].
                    uint a = reader.ReadUInt(), price = reader.ReadUInt(), b = reader.ReadUInt();
                    log.Info($"{session.CharacterName}: vendor ListItem 0x{vendor:X} ({a}, {b}) at {price}.");
                    CharacterItemModel item = session.Inventory.At(Inventory.Inventory.BagContainer, a);
                    (uint result, uint fee, uint count) = item == null
                        ? (NoSuchItem, 0u, 0u)
                        : List(session, vendor, item.Id, b == 0 ? item.Count : b, price);
                    session.EnqueueMessage(ZoneMessageOpcode.ClaimVendorBase, Build(ListItemResponseSub, vendor, w =>
                    {
                        w.Write(item?.ItemDefinitionId ?? 0u);
                        w.Write(fee);
                        w.Write(count);
                        w.Write(result);
                        w.Write(Inventory.Inventory.CurrencyItem);
                    }));
                    break;
                }
                default:
                    log.Debug($"{session.CharacterName}: vendor sub {request.SubOpcode}: {BitConverter.ToString(request.Body)}");
                    break;
            }
        }

        /// <summary>A visitor buys from a vendor (in-game window): coins out, item in, the owner paid by mail.</summary>
        public static uint Buy(WorldSession session, ulong vendor, ulong listingId, uint quantity)
        {
            uint result;
            ClaimVendorItemModel listing;
            uint total = 0;
            lock (sync)
            {
                listing = Listings().FirstOrDefault(l => l.Id == listingId && l.VendorId == vendor);
                quantity = Math.Max(1, quantity);
                if (listing == null || listing.Count < quantity)
                    result = NoSuchItem;
                else if (listing.OwnerGuid == session.PlayerGuid)
                    result = Failed;
                else if ((total = (uint)Math.Min(uint.MaxValue, (ulong)listing.Price * quantity)) > session.Coins)
                    result = NotEnoughCoins;
                else if (session.Inventory.SpaceFor(listing.ItemDefinitionId) < quantity)
                    result = Failed;
                else
                {
                    result = Success;
                    listing.Count -= quantity;
                }
            }
            if (result == Success)
            {
                if (total > 0 && !session.ChangeCoins(-(long)total))
                {
                    lock (sync)
                        listing.Count += quantity;
                    result = NotEnoughCoins;
                }
                else
                {
                    session.GiveItem(listing.ItemDefinitionId, quantity);
                    Save(listing);
                    string name = ItemName(listing.ItemDefinitionId);
                    Social.SocialServices.Service.SendSystemMail(listing.OwnerGuid, "Your vendor", $"Sold: {quantity} × {name}",
                        $"{session.CharacterName} bought {quantity} × {name} from your vendor for {total} coins.", total);
                    log.Info($"Vendor 0x{vendor:X}: {session.CharacterName} bought {quantity} × {name} for {total} coins.");
                    Refresh(vendor);
                }
            }
            if (result == Failed && listing?.OwnerGuid == session.PlayerGuid)
                Chat.System(session, "That's your own vendor: take items back from the Showcase (My vendors).");
            else if (result == Failed)
                Chat.System(session, "Your bag has no room for that.");
            session.EnqueueMessage(ZoneMessageOpcode.ClaimVendorBase, Build(PurchaseResponseSub, vendor, w =>
            {
                w.Write(listingId);
                w.Write(listing?.ItemDefinitionId ?? 0u);
                w.Write(result);
                w.Write(quantity);
                w.Write(total);
            }));
            return result;
        }

        /// <summary>
        /// The owner puts items from their bag on a vendor: the items leave the bag, the 10% fee is paid in coins.
        /// Returns (result, fee, count).
        /// </summary>
        public static (uint Result, uint Fee, uint Count) List(WorldSession session, ulong vendor, ulong itemGuid, uint count, uint price)
        {
            if (!Enabled || Props.Find(vendor) is not { } prop || prop.PropId != VendorProp || prop.OwnerGuid != session.PlayerGuid)
                return (Failed, 0, 0);
            CharacterItemModel item = session.Inventory.Get(itemGuid);
            if (item == null || item.Container != Inventory.Inventory.BagContainer || count == 0 || count > item.Count
                || item.ItemDefinitionId == Inventory.Inventory.CurrencyItem)
                return (NoSuchItem, 0, 0);
            // What may not change hands may not be sold either: a NO_TRADE item (Lumen-bought, founder, soulbound), a
            // store purchase still in its waiting period (Trades / Mail / Chests refuse both), a chest still holding items
            // (its contents are keyed to its guid, which a listing doesn't keep) and a Player Design item (its design is
            // keyed to the item's guid too). Before, all four could be listed and bought: the first two passed to any
            // buyer, the last two arrived empty and their contents / design were lost.
            if (Inventory.ItemDefinitionManager.Get(item.ItemDefinitionId) is { NoTrade: true } || Inventory.Inventory.IsTradeLocked(item)
                || Chests.HasItems(item.Id) || item.ItemDefinitionId == DesignItems.PlayerDesignItem)
            {
                Chat.System(session, "That item can't be sold on a vendor.");
                return (Failed, 0, 0);
            }
            price = Math.Min(price, MaxPrice);
            // In doubles: price × count × 10 % can pass 2^32 (10,000,000 × 500 = 500,000,000 fine, × 5,000 is not), and an
            // out-of-range double cast to uint is unspecified, which let a huge listing through for a tiny fee.
            double exactFee = Math.Ceiling((double)price * count * FeeRate);
            if (exactFee > session.Coins)
                return (NotEnoughCoins, (uint)Math.Min(exactFee, uint.MaxValue), count);
            uint fee = (uint)exactFee;
            uint definition = item.ItemDefinitionId;
            if (session.TakeFromStack(itemGuid, count) == null)
                return (NoSuchItem, 0, 0);
            if (fee > 0)
                session.ChangeCoins(-(long)fee);
            var listing = new ClaimVendorItemModel
            {
                Id = Inventory.Inventory.NewItemGuid(), VendorId = vendor, OwnerGuid = session.PlayerGuid,
                ItemDefinitionId = definition, Count = count, Price = price, FeePaid = fee, ListedAt = DateTime.UtcNow
            };
            lock (sync)
                Listings().Add(listing);
            Save(listing);
            log.Info($"Vendor 0x{vendor:X}: {session.CharacterName} listed {count} × {ItemName(definition)} at {price} (fee {fee}).");
            Refresh(vendor);
            return (Success, fee, count);
        }

        /// <summary>The owner changes a listing's price (no new fee).</summary>
        public static (uint Result, ClaimVendorItemModel Listing) SetPrice(WorldSession session, ulong listingId, uint price)
        {
            ClaimVendorItemModel listing;
            lock (sync)
            {
                listing = Listings().FirstOrDefault(l => l.Id == listingId);
                if (listing == null || listing.OwnerGuid != session.PlayerGuid)
                    return (NoSuchItem, listing);
                listing.Price = Math.Min(price, MaxPrice);
            }
            Save(listing);
            Refresh(listing.VendorId);
            return (Success, listing);
        }

        /// <summary>The owner takes a listing back: into the bag, or by mail when the bag is full.</summary>
        public static (bool Ok, string Message) Unlist(WorldSession session, ulong listingId)
        {
            ClaimVendorItemModel listing;
            lock (sync)
            {
                listing = Listings().FirstOrDefault(l => l.Id == listingId);
                if (listing == null || listing.OwnerGuid != session.PlayerGuid || listing.Count == 0)
                    return (false, "That listing is gone.");
            }
            uint count = listing.Count;
            string name = ItemName(listing.ItemDefinitionId);
            lock (sync)
                listing.Count = 0;
            Save(listing);
            if (session.Inventory.SpaceFor(listing.ItemDefinitionId) >= count)
                session.GiveItem(listing.ItemDefinitionId, count);
            else
                Social.SocialServices.Service.SendSystemMail(session.PlayerGuid, "Your vendor", $"Returned: {count} × {name}",
                    "Your bag was full, so the items you took off your vendor come by mail.", 0,
                    new[] { (listing.ItemDefinitionId, count) });
            Refresh(listing.VendorId);
            log.Info($"Vendor 0x{listing.VendorId:X}: {session.CharacterName} took back {count} × {name}.");
            return (true, $"{count} × {name} taken back.");
        }

        /// <summary>The player's vendors (placed, on any landscape) and their stock.</summary>
        public static List<(ulong Vendor, uint Zone, List<ClaimVendorItemModel> Stock)> VendorsOf(WorldSession session)
            => Props.OwnedBy(session.PlayerGuid).Where(p => p.PropId == VendorProp && p.Placed)
                .Select(p => (p.Spawn.Guid, p.Zone, StockOf(p.Spawn.Guid))).ToList();

        private static void Save(ClaimVendorItemModel listing)
        {
            ClaimVendorItemModel copy = Copy(listing);
            DatabaseManager.Instance.CharacterDatabase.SaveWorld(c =>
            {
                if (copy.Count == 0)
                {
                    c.ClaimVendorItem.Remove(new ClaimVendorItemModel { Id = copy.Id });
                    return;
                }
                if (c.ClaimVendorItem.Any(l => l.Id == copy.Id))
                    c.ClaimVendorItem.Update(copy);
                else
                    c.ClaimVendorItem.Add(copy);
            });
            if (copy.Count == 0)
                lock (sync)
                    listings?.RemoveAll(l => l.Id == copy.Id);
        }

        public static string ItemName(uint definition)
        {
            uint text = Inventory.ItemDefinitionManager.Get(definition)?.NameId ?? 0;
            string name = text == 0 ? "" : TextManager.Instance.GetTextForId(text);
            return string.IsNullOrEmpty(name) ? $"item {definition}" : name;
        }

        /// <summary>The player left the world.</summary>
        public static void Forget(WorldSession session)
        {
            lock (sync)
                foreach (var set in observers.Values)
                    set.Remove(session);
        }
    }
}
