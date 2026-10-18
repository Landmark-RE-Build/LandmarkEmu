# Build site vendors (player shops) and the merchant window

## Two merchant systems in the 0.589 client

| System | Client side | Server |
|---|---|---|
| **Build site vendor** (player shop) | Prop 1491 "Build Site_Vendor_Object", `ClaimVendorWindow` (native grid with cart), `ClaimVendorHandler.lua`, `LuaClaimVendor` natives, ClaimVendorBase packets 0x112 | `Game/Vendors.cs` (built) |
| **NPC merchant** (coin store) | `MerchantWindow.swf` (buy / sell / buy-back tabs), `MerchantSets.txt` ("Generic Merchant"), CoinStoreBase 0x9E (server opens it with CoinStoreOpen sub 7) | not built: 0.589 has no merchant NPCs to open it |

## Build site vendor

- **Off by default, as live** (setting `VendorShops`, Docker `VENDOR_SHOPS`, default false): live made the Vendor
  Booth a decorative prop on 1 Apr 2016 (P4378: its old, unworking window was removed). Off, the booth has
  no interaction, the Showcase hides **My vendors** and refuses listings, and at start-up any stock left in booths is
  mailed back to the owners ("Your vendor"); the booth stays and can be picked up. `VENDOR_SHOPS=true` turns on the
  working shop described below.
- **Getting one:** the Showcase store, Decorations › Build Site Vendor (free).
- **Placing it:** on your build site.
- **Buying:** visitors click the vendor. The server answers with UpdateInventory (show = true) and the client opens
  its Vendor window with the stock. Buy (cart, Confirm) sends PurchaseItem; the buyer's coins go to the owner by mail
  ("Your vendor", coins attached), whether the owner is online or not.
- **Stocking:** the 0.589 client has the listing natives (`ListItem`, `SetItemPriceByGuid`, `SetItemPriceBySlot`)
  but no screen that calls them, so owners stock their vendors from the Showcase's **My vendors** tab. From there
  they list bag items with a count and a price per item (a 10% listing fee in coins, as the client's own dialog
  says), change prices, and take items back (into the bag, or by mail when it is full). Items that may not change
  hands can't be listed (result Failed, with a chat line): NO_TRADE items, store purchases still in their waiting
  period, chests that still hold items and Player Design items (since 2026-10-18).
- **Picking up:** a vendor that still has stock can't be picked up (PickUpReply error 9,
  `Prop.PickUpErrorVendorInventoryNotEmpty`).
- **Storage:** listings are kept in the `claim_vendor_item` table (migration `ClaimVendorItem`).

### Packets: ClaimVendorBase 0x112 (wire bytes `12 01`)

Every packet in both directions starts with `u16 opcode, u32 sub, u64 vendor guid` (header reader FUN_1432cb500,
dispatcher FUN_1432cd470).

| Sub | Dir | Body |
|---|---|---|
| 1 UpdateInventory | S→C | `bool show` (opens the window via Lua HandlerClaimVendor), `bool include empty rows`, `u32 n` × item. Item = `{u64 listing id, u32 item definition, u32 name text, u32 description text, u32 value (2 when 0), u32 quantity, u32 price, u32 listing fee paid}` (FUN_1432cc620 / FUN_1432cb590 / FUN_1432cd010). |
| 2 SetPrice | C→S | `u32 -1, u64 listing, u32 price` |
| 3 SetPriceResponse | S→C | `u32, u64 listing, u32 price, u32 item, u32 currency item, u32 result` |
| 4 ListItem | C→S | `u32 a, u32 price, u32 b` (ListItem(a, b, price); no 0.589 screen calls it) |
| 5 ListItemResponse | S→C | `u32 item, u32 fee, u32 count, u32 result, u32 currency item` |
| 6 StopObserving | C→S | (window closed) |
| 7 PurchaseItem | C→S | `u64 listing, u32 quantity` |
| 8 PurchaseItemResponse | S→C | `u64 listing, u32 item, u32 result, u32 quantity, u32 price` |

Results: 1 success, 2 not enough coins, 3 no such item, anything else a generic failure (`ClaimVendor.*` strings).

### Client script gaps, patched at run time

0.589's `ClaimVendorHandler.lua` calls a table `ClaimVendorScreen`, but the natives are registered as
`LuaClaimVendor`. Opening a vendor therefore failed with "attempt to index a nil value (global 'ClaimVendorScreen')"
and Buy did nothing. `BuyItem` also only accepts exactly 2 arguments, while the handler passes 3.

After world entry, the server runs client Lua (`Game/ClientFixes.cs` through `ClientLua.RunCode`, see
[showcase.md](showcase.md)). It defines `ClaimVendorScreen` as `LuaClaimVendor` plus the two missing callbacks
(`OnVendorOpened`, `OnVendorClosed`) and a two-argument `BuyItem`. Nothing on disk changes.

### Web API (Showcase, with the player's token)

| Request | |
|---|---|
| `GET /api/myvendors` | `{coins, fee, vendors: [{id, landscape, stock: [{id, info, count, price, fee}]}], bag: [{guid, info, count}]}` (guids as strings) |
| `POST /api/vendor/list` `vendor, item, count, price` | list bag items |
| `POST /api/vendor/price` `listing, price` | change a price |
| `POST /api/vendor/unlist` `listing` | take a listing back |

Checked in a running client with a scripted run (11 checks and a screenshot of the vendor window; the harness is not
part of this repository). Dev commands: `vendoropen <guid hex>` (as a click), `prop 1491` (place one).

## Point-of-Need purchases

The client's "Point of Need" buttons send InGamePurchase 0x47 PointOfNeedRequest (0.589: 0x46):
- `{u32 1, u8, u32 days, u64, u64}` is build site upkeep (Lua `Claims.PayUpkeepPoN`, the ClaimPoN window). Upkeep
  is free here: the server confirms it in chat and re-sends the upkeep data.
- `{u32 2, ...}` is the crafting window's recipe query (`Recipe.SelectRecipe`). Its InfoResponse (0x4C; 0.589: 0x4B) points the
  client at a store URL for the missing ingredients; that is not answered yet.
