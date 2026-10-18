# In-game commerce and Player Studio (InGamePurchaseBase 0x45)

The 0.589 client carries the whole in-game commerce service of the live game: the Daybreak Cash store, wallets,
gifting, and **Player Studio**, where players sold their designs on the marketplace after a peer review. This server
has no real money and no Station Cash: the store and Player Studio designs cost Lumens (or nothing with `FREE_STORE`);
Player Studio is rebuilt as the client drives it.

Server code: `LandmarkEmulator.WorldServer/Game/Commerce.cs` (Player Studio, account info),
`Network/Handlers/InteractionHandler.cs` (wallet 0x0B → 0x0C, upkeep). Tables: `player_studio_product`,
`player_studio_review`, `mail.kind`. With the shared commerce service (`COMMERCE_ADDRESS`), the same rules and packets
run in `LandmarkEmulator.CommerceServer` and its own database `lmemu_commerce` — see **Where it runs** below.

**0.695 numbering.** The 0.695 client inserted TemplateProductCreate at sub 0x30 and moved every sub ≥ 0x30 up by one
(research/trace/0695/coverage-695.md §0x45: 33 renumbered, bodies "same"). This page gives the 0.589 numbers; the server
speaks the 0.695 ones natively (protocol.md §1.6: every sub ≥ 0x30 here is one higher on the wire, e.g. AccountStore
0x39, Search 0x3E / 0x3F), and `Commerce.FromWire` maps 0.695's 0x30 TemplateProductCreate onto 0x2F ProductCreate
(same body first, research/trace/0695/packets-695.md §12). (Until 2026-10-16 the commerce code shifted the subs a second time; until
2026-10-17 a header translation at the gateway turned 0.695 numbers into these.)

## Wire format

Every packet is `u16 0x45, u16 sub, body`. The client's response dispatcher is FUN_1431adf70 (a switch on the sub);
requests are written by the commerce manager (`DAT_145351c48 + 0x32050`) through FUN_143189e20 / FUN_14318aba0.

| Sub | Dir | Name | Body | Server |
|---|---|---|---|---|
| 0x0B / 0x0C | C→S / S→C | Wallet info | `u32 result, u8, u32 balance, u32, string, string, u8 zip on file` (see **Listing fee**) | answered by the zone: the Lumens while the listing fee is charged, else 0; also sent unasked when the Lumens change |
| 0x2E | S→C | CommerceConfiguration | `u32 minimum fee, u32 fee percent, u32 minimum price` (see **Listing fee**) | sent at world entry |
| 0x0F | C→S | Station Cash price list | `string currency` | logged, unanswered (store is free) |
| 0x15 | C→S | Country codes | `string locale` | logged, unanswered |
| 0x1A / 0x1B | C→S / S→C | Account info | reply `u32 result (1), string country, string currency, bool, string` (FUN_1431a67b0) | `"US"`, `"USD"`, true |
| 0x2F / 0x30 | C→S / S→C | Product create | see below | built |
| 0x3F / 0x40 | C→S / S→C | Product for review | see below | built |
| 0x41 | S→C | Admin product for review | as 0x40 (FUN_1431a71c0), opens the review window in admin mode | not used |
| 0x42 / 0x43 | C→S / S→C | Submit review | `u64 product, u32 verdict` / `bool ok` | built |
| 0x31, 0x33 | C→S | Product remove / update | | built (Commerce.RemoveProduct / UpdateProduct) |
| 0x34, 0x35 (0.695: 0x35, 0x36) / 0x39, 0x3A (0.695: 0x3A, 0x3B) | C→S / S→C | Follow / unfollow a creator | `u64 creator` / `u64 creator, bool ok, string name` (AddFollowing), `u64 creator, bool ok` (RemoveFollowing) | built: kept in the character database (`showcase_follow`, shared with the Showcase page's Following tab and read by the daily following mail: docs/showcase.md **Following**); with the commerce service in its `following` table (**Following and the digest** below) |
| 0x37 (0.695: 0x38) | C→S | Recommendations | | unanswered: the client has no reply class for it and no shipped caller (OPEN-LEADS L123) |
| 0x0D / 0x0E | C→S / S→C | Server status | — / `bool` (0.695 FUN_143256750: exactly one byte, then Lua `OnReceiveServerStatus(bool)`) | answered `true` (in-process since 2026-10-18, as the commerce service; no shipped script asks, L124) |
| 0x36 / 0x38 | C→S / S→C | The player's own products (account store) | | built (Commerce.SendAccountStore) |
| 0x3D / 0x3E | C→S / S→C | Search (world map search bar) | `{string query, u32}` / `{string query, u32 n × {u32, u32}, u32 count, u32 n × u64 site}` (FUN_143184530) | built (Claims.Search) |
| 0x49 / 0x45 | C→S / S→C | Site screenshots | `{u32 action, u32 index, u64 site, …}` / `{u64 site, u64, bool}` | built (Game/SiteScreenshots.cs) |
| 0x4C / 0x4D | C→S / S→C | Is template valid | `{u64 design, u32 n × u64 inner design}` / `{u64 design, bool}`: the client holds the ProductCreate of a design containing other designs until true (FUN_1431ad850); false = 0x16 TemplateContainsModifiedSubTemplate | built (the player's own design is valid) |

## Player Studio registration

Creating a product needs the player to be Player Studio registered (`PacketPlayerStudioRegistered`, opcode 0xFE00,
`{u8 1}`, sent at world entry) and the account to be a US account with a zip code on file (FUN_1431b4c10). Otherwise
the client shows status 0x14 `CommerceNoZipCode` and opens the live payout web page. The AccountInfo reply ("US") and
the wallet's zip flag cover this.

## Creating a product (0x2F → 0x30)

The Designs window's Submit button runs Lua `InGamePurchaseStoreScreen.ProductInit`, `ProductAddItem(guid, 2)`,
`ProductSetName`, `ProductSetDescription`, `ProductSetPrice`, `ProductSetTags`, then `ProductCreate`. The client
checks the form first (FUN_1431b7c20): price ≥ 25, name, description and tags not empty, registered.

**CommerceProductCreate 0x2F** (writer FUN_1431b7c20, captured):

| Field | Type |
|---|---|
| price | u32 |
| (unused) | u32, u32 |
| name, description, tags | string × 3 |
| items | u32 n × ProductItem |
| edit | u8 |
| headline | string |
| | u32 |

**ProductItem** (writer FUN_143181750, reader FUN_143184b70): `u64 guid, u32 type (2 = design), u32 quantity, u64,
u8, string`. In a product sent to the client, a design's string is its TemplateData XML (below).

**CommerceProductCreateResponse 0x30** `{u32 status, string}` (FUN_1431a7430 → FUN_14319fc60): 1 created,
8 price too low (below the design's own minimum), 9 item not found, 0xB not the design's owner (or not their own work:
a design taken from Player Studio), 0xC not valid, 0xF already on the marketplace, 0x10 can't afford the listing fee
(text `CommerceCreateProduct*`, CodeStringMappings.txt → text ids 563262-563364). Other codes the client knows and the
server never sends: 0x11 a contributor is not registered for Player Studio (563336), 0x12 contains a Player Studio
design priced higher (563364), 0x13 invalid material, 0x14 no zip code, 0x16 modified sub-design, 0x17 blocks from an
unknown contributor (571819). The server stores the product as Pending (status 0).

## Peer review (0x3F → 0x40, 0x42 → 0x43)

The client asks for a product to review **by itself**: its commerce tick (FUN_1431a64b0) sends 0x3F once per
session, and again after each vote (SubmitProductReview resets the request time).

**ReviewProductResponse 0x40** (handler FUN_1431a7fc0, reader FUN_1431834e0): `Product, u32 products waiting`.

**Product** (reader FUN_143184930; the review window's columns from FUN_143647020):

| Field | Type | Shown as |
|---|---|---|
| id | u64 | ItemGUID (0 = no product) |
| | u64 | |
| | u32, u32 | |
| price | u32 | |
| | u32 | |
| likes | u32 | Likes |
| name | string | Name |
| description | string | ItemDescription |
| created | u64 (Unix s) | CreatedOn |
| | u64, u32 | |
| tags | string | |
| items | u32 n × ProductItem | the design entry's TemplateData gives ItemOwner and the screenshots |

The unnamed fields aren't read anywhere in 0.589 and are sent as 0.

With a product (id ≠ 0), the client queues **its own** Game Action Queue popup, `GaqItemMarketplaceItemReviewRequest`
(type 9, FUN_14332e440, text `GAQMarketplaceItemReviewsPending` with the waiting count). The server sends no
notification for it. Accepting opens `PlayerStudioItemReview`, which reads the data source
`MarketingData.InGamePurchase.ItemForReview`.

**TemplateData XML** (rapidxml; parser FUN_1434d4590 → FUN_1434c1740, root `TemplateData`): VoxelCount, Props,
Materials, Contributors, Showcase, CreatorName, LargeImageURLs/LargeImageUrl, SmallImageURLs/SmallImageUrl, ImageURLs,
SubTemplates, SubTemplatesMetaData. The reader takes fields as elements or attributes depending on its mode, so the
server writes CreatorName both ways: `<TemplateData CreatorName="X"><CreatorName>X</CreatorName></TemplateData>`.
Missing fields only set the reader's error flag; the window still uses what was found.

**SubmitProductReview 0x42** `{u64 product, u32 verdict}` (FUN_1431c53c0, from Lua `ReviewProduct(vote, type,
subtype)`, FUN_143539a40): 1 = approve (thumbs up); 0 = reject; 2-7 = reject for a reason (rejection type 1-3 ×
subtype 0/1: (1,0) 2, (1,1) 3, (2,0) 4, (2,1) 5, (3,0) 6, (3,1) 7; 0.695 FUN_1435e9f30). The reason drop-downs read
`ReviewRejectionTypes` / `ReviewRejectionSubTypes`, which nothing in the client fills (0.589 and 0.695), so a down vote
usually arrives as 0 (or 2 with the default indices). The code is kept with the vote (`PlayerStudioReview.Verdict`,
the service's `product_review.verdict`), and the staff queue shows it as the reason: type 1 "Offensive", 2 "Copyright
(name)", 3 "Copyright (object or description)" — the three grounds of the 18 Jun 2014 notes, names [I] since the
client has none — with the subtype as "option 1 / 2" (meaning unknown), e.g. "Rejected for: Offensive, option 1 x2;
no reason x1" (`Internal.Commerce.RejectReasons`).

**SubmitProductReviewResponse 0x43** `{bool}` (FUN_1431a8620): fires Lua `GameEvents:OnReviewComplete(ok)`.

Server rules: you never review your own product, and you vote at most once per product. The product is listed after
`PlayerStudioApprovals` approvals, or rejected after `PlayerStudioRejections` rejections (WorldServer.json, default
3 / 3; with the commerce service CommerceServer.json / `PLAYER_STUDIO_APPROVALS`, `PLAYER_STUDIO_REJECTIONS`; the live
numbers aren't in the client). The dev command `psreview <id> approve|reject` decides a product
directly (the live client's admin path sent `appgamecommerce product approve|reject <id>`).

Live's review rules (Update Notes 18 Jun 2014 "How Does an Item Get Accepted?", P1207-P1210; wiki Player Studio):

| Rule | Live | Here | Setting (`.env`) |
|---|---|---|---|
| Who reviews | players who had bought at least 1000 SC of Marketplace items | accounts that spent at least 1000 × `LUMENS_PER_DBC` Lumens (1,300) on the store and on Player Studio designs (all the account's characters; `character_unlock` kind 33 per character, dev `storespent [n]`). The world checks it in both commerce modes and answers a player who may not review itself (empty product / `false`); the account's other characters are summed by one query off the world tick, kept a minute per account | `PLAYER_STUDIO_REVIEWER_MIN_SPEND` (1000; 0 = anyone) |
| How many | at most two review chances a day | reviews in the last 24 hours | `PLAYER_STUDIO_REVIEWS_PER_DAY` (2; 0 = no limit; world and commerce service) |
| Anonymous | the creator's name is not shown to reviewers (wiki) | the review copy's TemplateData has an empty CreatorName | — |
| Rejected | a rejected item went to a Customer Service queue, where staff upheld or overrode the players' verdict | the product stays waiting with its rejections at the threshold: no longer offered to peers, listed on the admin page (/admin → "Player Studio: staff review", with the peers' reasons; Approve / Reject = `psreview`); the owner gets the "CS Review" mail (kind 0x40), then the result mail when staff decide | `PLAYER_STUDIO_STAFF_REVIEW` (true; false = rejected at once) |

Not built: live's reviewer opt-out and the extra review chances it announced but never described, and
taking review away from reviewers whose rejections staff overturn (wiki); the "Peer Review" mail (kind 0x80) on
submission.

## Result mail

The owner gets a mail whose **kind** (MailData u32 at +0x1d0, `mail.kind`) tells the client to write the text itself
(FUN_143586ba0):

| Kind | Text (client locale) |
|---|---|
| 0x10 | `PlayerStudio.AcceptedItem` |
| 0x20 | `PlayerStudio.RejectedItem` |
| 0x40 | `PlayerStudio.CSReviewItem` |
| 0x80 | `PlayerStudio.PeerReviewItem` |

Sender `PlayerStudio.MailUser`, subject `PlayerStudio.MailSubject`; the body's `item` token is the mail's subject,
so the server puts the product name there. Other kind bits (1, 2, 4, 8, 0x100, 0x800, 0x1000, 0x2000) are other
system mails (returned mail, follow manager, …).

## The creator's price

The creator sets a price in the submission window (PlayerStudioWindow "Set Your Price", only digits) and the client
keeps it in its commerce manager (+0x24) through Lua `InGamePurchaseStoreScreen.ProductSetPrice` (0.695 FUN_143263c60,
which refuses a price below the manager's minimum). `FUN_143262c00` (0.589 FUN_1431b7c20) refuses the form when the
price is below 25 (0x19) or below the minimum, and copies the price into **ProductCreate's first u32**; ProductUpdate
carries a new price after the description (My Store edits). Both are **[P]**. The price was in Station Cash /
Daybreak Cash on live: the creator named the item, set a price, wrote a description and entered tags (Update Notes
18 Jun 2014), and any price over 25 SC was allowed (wiki Player Studio).

Server rules: the design's own minimum (at least 25, see **Minimum price per design**) ≤ price ≤ **29,600**
(`LandmarkEmulator.Internal/Commerce/StudioRules.cs`, shared by the world and the commerce service). The ceiling is the client's own: StringHashToValue
`PlayerStudio.MaxListingValue^29600^`, which the submission window reads and turns the price red above (the client still
sends it, so the server refuses it: create → 0xC, update → the old price stays) [P value, I server check].

### Listing fee (`PLAYER_STUDIO_LISTING_FEE`, default on)

Live charged a fee for every design submitted: 10 % of the set price, at least 100 SC (a developer, Update Notes 18 Jun
2014); still a 100 DBC fee in October 2016 (Game Update 14 Oct 2016, which paused it briefly while a pricing bug was
fixed); the wiki's one-dollar fee is the same 100 Daybreak Cash. It was "non-refundable" (client text 563260)
and kept when peer review turned the design down (a player quoting the launch livestream) [W]. The client has the whole
rule [P]:

- **CommerceConfiguration 0x2E** `{u32 minimum fee, u32 fee percent, u32 minimum price}` (S→C; 0.695 reader
  FUN_14322db60, handler FUN_143252250 → manager +0x230578 / +0x23057c / +0x230580; 0.589 FUN_1431830a0 / FUN_1431a7380).
  ProductInit (FUN_1432636a0) copies the three into each new product.
- The submission window (data source `MarketingData.CommerceAccount.CreateProduct`, FUN_14370d890) shows
  `ListingFee = max(minimum fee, percent × 0.01 × price)` ("[ Listing Fee: n ]") and the minimum price.
- ProductCreate (FUN_143262c00) refuses the submit with status 0x10 when the **wallet balance** (WalletInfoResponse
  0x0C's first u32 → manager +0x22bc10) is below the fee. A gallery listing (free-listing byte) skips the fee.
- The wallet reply (0.695 handler FUN_143258ad0, reader FUN_1432300e0): `u32 result` (1 = ok), `u8`, **`u32 balance`**,
  `u32`, `string`, `string`, `u8 zip code on file`. The balance also becomes the Character.Currencies row ItemID -1
  "DBC" (FUN_142fd19b0), which the inventory shows as its Daybreak Cash counter.

The server (Game/Commerce.cs) runs it in **Lumens**:

- 0x2E at world entry: minimum fee = 100 DBC × `LUMENS_PER_DBC` (`Store.LumensFor`: **130 Lumens**), fee percent =
  10 × `LUMENS_PER_DBC` rounded to a whole percent (**13**), minimum price **25** (the price stays the creator's
  Daybreak Cash figure, as the buyer's Lumen price is computed from it). Fee = 130 Lumens up to a price of 1,000, then
  13 % (2,000 → 260; 29,600 → 3,848). With the fee off or `FREE_STORE`: `{0, 0, 25}`.
- The wallet carries the player's Lumens while the fee is charged (0 otherwise), answered by the zone in both commerce
  modes and re-sent unasked whenever the Lumens change (WorldSession.SendCurrencies), so the client's check is current.
  Side effect: the inventory's Daybreak Cash counter shows the Lumen count too.
- ProductCreate: after the zone's checks the fee (computed exactly as the client does, `StudioRules.ListingFee`) is
  taken; short → 0x10. It is given back only if the server then refuses the listing (save failure; with the commerce
  service: its typed result for that create (CommerceProductCreateResult, by create id) says not stored — 0xF duplicate,
  0xC, 0 …). A result that does not come (30 s, or the link lost) is asked for (CommerceProductCreateCheck: the
  service's recent result for the id, else whether the player has a live product for the item); after 3 unanswered
  checks on an open link (a service built before 2026-10-18) the fee is given back. A player who left by then gets it
  by mail (Lumens attached). Peer review rejection keeps it, as live. A world restart while a create waits keeps the
  fee (the waiting creates are not saved).
- Not built: live's fee difference on a later price increase (a raised price asked for the extra fee: Bonus Update
  Notes 31 Jul 2014). The 0.695 client has no prompt or wallet check for ProductUpdate (FUN_143262c00 is
  its only fee check), so no fee is taken on updates (OPEN-LEADS L148).

### Minimum price per design (TemplateValue)

ProductAddItem (FUN_143263990) raises the product's minimum price to the design's own value at template **+0x110**
when it is higher than the configured minimum, and the price with it; ProductSetPrice (FUN_143263c60) refuses a lower
price [P]. +0x110 is TemplateBasicData's **TemplateValue** (the u32 after the author string; 0.695 reader FUN_1430ff530,
data source column "TemplateValue", part_0086.c:12279). The server sends **1 % of the design's voxels, rounded down**
(`StudioRules.TemplateValue`; 0 for the game's own designs) [I]: players reported a minimum of 1 % of the voxel count
(a 604,200-voxel design had to cost at least 6,042 SC: Hotfix Notes 20 Jun 2014 thread) and a minimum set by the
voxel count in 2016 (Game Update 14 Oct 2016 thread). The server checks the same minimum on create (status 8) and update (old price kept), in both
commerce modes (the service uses the voxel count the zone sent at create). Open (L148): the client's tooltip says "The
minimum price is determined by block count and resource rarity." (text 563163); the rarity part is not found yet. And
live's rule that the minimum is at least the price of the dearest sub-design (Hotfix Notes 20 Jun 2014) needs template
instances inside designs, which the server does not keep.

Only the player's own work can be listed: the client refuses a design whose own-design flag (+0xf4) is false, i.e. one
taken from Player Studio (the same hotfix notes disabled the Player Studio button on bought designs) [P]; the server refuses those and the game's own designs with 0xB [I].

### Contributors (not built)

Live split a design's royalties between the players whose voxels it holds (by voxel share, players in the Hotfix
Notes 20 Jun 2014 thread; the TemplateData XML has a `Contributors` element; statuses 0x11 / 0x17 refuse
a design with an unregistered or unknown contributor). The server stores a design as its block list and material
counts only (TemplateModel; Templates.Save): the world's per-voxel owners (EnvServer VoxelOwnership, Voxel Vision) are
not copied into the design when it is saved, so no contributor list exists to split by. The whole share goes to the
product's owner. To build it: record the selection box's ownership census (EnvServer OwnershipCensus / OwnedBy) with
the design at save time, send it as TemplateData `Contributors`, and split the share by it (L148).

## Buying a listed design (Showcase → Player Studio)

The live marketplace sold accepted designs for the creator's Daybreak Cash price. This server has no Daybreak Cash, so
the Showcase page (`Resources/Web`, `Game/WebPages.cs`) has a **Player Studio** tab listing every accepted design product
with its price in **Lumens** (`GET /api/studio`: `price` = what the buyer pays, `creatorPrice` = the price as set);
**Buy** (`POST /api/studio/take`, `Commerce.TakeDesign`; dev command `pstake <product>`) takes the Lumens and gives the
player a copy of the design:

- **Price** = the creator's price × `LumensPerDaybreakCash` (`LUMENS_PER_DBC`, 1.3), rounded up to whole 5 Lumens
  (`Store.LumensFor`, the rate every live DBC price in the store uses): 25 → 35, 100 → 130, 1,000 → 1,300.
  `FreeStore` (`FREE_STORE=true`) makes every design free ("Get"). No member discount (the price is the creator's).
  The Lumens are taken on the world thread just before the copy is saved, and given back if the save fails.
- **The creator's share**: `PlayerStudioCreatorSharePercent` (`PLAYER_STUDIO_CREATOR_SHARE`, default **40**) of what the
  buyer paid, rounded down, mailed at once as a system mail from "Player Studio" ("Sold: <listing>") with the Lumens
  attached; the creator also gets a chat line if online. Live: a 60/40 split of each sale's net revenue (a developer,
  Update Notes 18 Jun 2014), which players read as Daybreak 60 / creator 40, paid quarterly in real money [W]. Live split royalties between all contributors of a design by voxel ownership; here the whole share
  goes to the product's owner (**Contributors** above).

- the copy belongs to the taker and keeps the maker's name as its author, so the Designs window files it under
  **Collected** (TemplateBasicData's "own design" flag is false when the author isn't the holder);
- one copy per product per character (unlock kind 16), not your own products, design storage limit applies;
- the client prints its own "Learned.Template" notice (EnvironmentAddOwnershipResponse), and the maker, if online,
  gets a line saying who took it.

Not built: live placed bought designs as **template instances** so Voxel Vision credits their maker
(research/trace/systems/crafting-meta.md); a taken copy is placed like any other design.

## Where it runs: world vs the shared commerce service

Player Studio, the wallet / account answers, peer review, contests and portraits were account-wide or Daybreak-wide on
live (research/trace/architecture/world-channel-service.md §5.1: Player Studio was part of the one Marketplace, one
Competition tab, an external portrait renderer), so they belong to **one service for all worlds**, `commerce`
(build step 8, 2026-10-16). It is **off by default**: without `COMMERCE_ADDRESS` every world runs the code above in its
own process exactly as before (`Game/WorldChannel/LocalCommerceService.cs`). With it (`docker/commerce`, docs/docker.md
"Commerce service") the world's `RemoteCommerceService` hands the requests to `LandmarkEmulator.CommerceServer`:

| Part | In-process (default) | With the commerce service |
|---|---|---|
| Account 0x1A, account store 0x36, remove 0x31, update 0x33, review 0x3F / 0x42 | Commerce.cs | ProductService.cs, same packets (0.695 numbers on the wire: 0x37, 0x32, 0x34, 0x40 / 0x43) |
| Wallet 0x0B → 0x0C, CommerceConfiguration 0x2E, the listing fee | Commerce.cs | still the zone's: the wallet carries this world's Lumens and the fee is taken from them; settled by the service's CommerceProductCreateResult for that create id (kept on 1, given back otherwise); a missing result is asked for with CommerceProductCreateCheck (after a timeout or at the next link) rather than refunded blindly (see **Listing fee**) |
| ServerStatusRequest 0x0D | 0x0E true (since 2026-10-18) | 0x0E true from the service [I: the bool read as "store service up"]; 0x0E false from the world while it is unreachable (spec §7; Lua OnReceiveServerStatus) |
| ProductCreate 0x2F (0.695 0x30) | Commerce.CreateProduct | the world checks (Commerce.CheckProduct: item, owner, price, three gallery tags) and answers its refusals; the service checks for a duplicate (0xF), stores, answers 1. A design is sent with its **snapshot** (name, tags, size, materials, blocks) so another world can take it |
| Designs window category (TemplateBasicData IsOnMarketplace) | Commerce.IsOnMarketplace | the world's cache: CommerceMarketSnapshot at every link, CommerceMarketplaceStatus on every change (the owner's design is re-sent, Templates.SendUpdated) |
| Gallery listings (likes, contest entries, the Showcase gallery, Visit) | Commerce.IsOnGallery / Listings | the world's cache: CommerceGalleryStatus (listing, contest markers and names) |
| Result mail 0x10 / 0x20 | Commerce.Decide | the service sends CommerceResultMail to a world of the owner's realm, which mails it through its social seam; owed mails wait in the service's table until such a world links |
| Contests: list 0xA7/0x1D, "Contests" votes, entries 0xF9/0x41 → 0x18, Leave Competition | Contests.cs (Contests/*.json) | ContestService.cs (tables contest, contest_entry, contest_vote; screenshots in the service's data volume); the world checks the site's owner and the entry limiter first |
| Portraits 0x98/2 → 3, AddClientPortraitCrc 0x75, uploads | Portraits.cs (Portraits/*.png) | PortraitService.cs (table portrait + files); the world's web page hands uploads on |
| Showcase web pages, store catalogue, gallery comments, vendors, Visit, admin page | WebPages.cs / Store.cs | still the world's (spec §6.6 option: they act on the player's zone); Player Studio's list and "Buy" ask the service; a design taken from another realm is built from its snapshot |
| Player Studio sale: price, the buyer's Lumens, the creator's share | Commerce.TakeDesign (WorldServer.json `PlayerStudioCreatorSharePercent`) | the service's grant reply carries the creator's price; the buyer's world charges `Commerce.StudioPrice` (its own `FreeStore` / `LumensPerDaybreakCash`) and reports the Lumens paid (CommerceDesignTaken); the service computes the share (CommerceServer.json `PlayerStudioCreatorSharePercent`), records it in `studio_payout` and sends CommercePayout to a world of the creator's realm, which mails it and confirms (CommercePayoutSent); owed shares wait until such a world links. Mailed once: a share handed to a world is not offered to another world of the realm for 10 minutes while that world's link is open, and the world claims the payout id in the shared character database before mailing (`character_unlock` kind 32, INSERT IGNORE), so a share offered again (two worlds linking after a service restart, a confirmation lost with the link) is only confirmed |
| PlayerStudioRegistered 0xFE | world at entry | world at entry (same for every account; a commerce outage at login doesn't change it) |
| Showcase following (Following tab, FollowCreator 0x35 / UnFollowCreator 0x36) and the daily digest mail | ShowcaseFollow.cs (`showcase_follow`, `showcase_digest_run`, `player_studio_product.listedAt`; the world's `SHOWCASE_FOLLOW_DIGEST` / `SHOWCASE_DIGEST_HOUR_UTC`) | FollowService.cs (`following`, `following_tag`, `digest_run`, `digest_mail`, `product.listed_at`; the service's `SHOWCASE_FOLLOW_DIGEST` / `SHOWCASE_DIGEST_HOUR_UTC`); the page asks through the world (CommerceFollowRequest / Reply); each mail goes to a world of the follower's realm (CommerceDigestMail → CommerceDigestMailSent), see **Following and the digest** |
| In-game currency purchase PacketClientInGamePurchase 0xD0 → PlayerUpdate 0x4B | StoreBundles.HandlePurchase (Lumens, Store.Buy) | the same, in the world: the store and the Lumens are the world's (docs/showcase.md "The client's store products") |

**Keys.** Every row carries the world id and character guid (spec §6.1). A world also names its **realm** — the
character database it uses (all Docker worlds share `lmemu_character`) — so a player's own products, reviews, taken
designs, contest votes and portraits are found on every world of that realm, while build sites (claims) are per world.
Product ids stay the client's u32 BundleId (never 0); imported products keep their ids.

**One-time import.** On its first start, while its product table is empty, the service copies
`player_studio_product` (with the owner's account / name and the design's snapshot from `template`),
`player_studio_review` and the taken designs (`character_unlock` kind 16) from `COMMERCE_IMPORT_FROM` as world
`COMMERCE_IMPORT_WORLD`. Each world sends its `Contests/` (contests.json, entries.json, shots) and `Portraits/` files
once, the first time it links. Sources are only read; switching a world back to in-process does not copy anything back.

**When it is down** (spec §7): ProductCreate gets status 0 (the client's own "CommerceCreateProductUnknownError"),
ServerStatusRequest false, a contest entry ContestEntryResults false [I], everything else stays unanswered (the client
asks again); nothing is taken from anyone, and the world's caches keep their last state until the reconnect's snapshot.

Messages: `LandmarkEmulator.Internal/Commerce/CommerceMessages.cs` (ours; research/trace/architecture/internal-messages.md §2).

### Following and the digest (with the commerce service, since 2026-10-18)

Live's Showcase following (Update Notes 27 Aug 2014: follow creators and tags, and a daily in-game mail with what they
listed; docs/showcase.md **Following and the daily mail**) belonged to the one
commerce back end every world used, so with `COMMERCE_ADDRESS` set it runs in the service
(`LandmarkEmulator.CommerceServer/FollowService.cs`); without it nothing changes (the world's `Game/ShowcaseFollow.cs`).
Both use the same limits, tag rules and mail text (`LandmarkEmulator.Internal/Commerce/FollowDigest.cs`).

- **Storage** (`lmemu_commerce`, made at start like the other tables): creators in `following` (the client's
  FollowCreator list too; column `follower_name` added), tag groups in `following_tag` (follower world + guid + name,
  the group's words lower case and sorted). Matched across the follower's realm, like their products.
- **Newly listed**: `product.listed_at` (column added at start when missing) is set when peer review or an admin
  (`psreview`) lists a design and when a site is published; rows listed before it are dated by `created_at`. The
  one-time import copies the world's `player_studio_product.listedAt` when the source has it.
- **The run**: once per UTC day after `ShowcaseDigestHourUtc` (CommerceServer.json; Docker `SHOWCASE_DIGEST_HOUR_UTC`,
  default 10) with `ShowcaseFollowDigest` on (`SHOWCASE_FOLLOW_DIGEST`, default true = live), one run per realm. The
  slot `day:yyyy-MM-dd` is recorded in `digest_run` (unique per realm) before anything is sent, so a restart the same day
  mails nothing twice. The row is unfinished (`mails` = 4294967295) until the run's mails are stored in `digest_mail`;
  a run that fails before that is taken over and run again the next minute (since 2026-10-18). Window: the realm's last
  finished run (24 hours for the first) to now. One query for the products listed
  in it; nothing new = done. Otherwise one query for the realm's follow rows that can match (those creators' followers,
  every tag group) and one mail per follower. A follower hears about every realm's Player Studio designs (the Showcase's
  Player Studio page lists them all) and their own realm's gallery sites; creator follows match their own realm's
  products only (a creator id is a character guid of that realm) [I].
- **The mail** (as the creator's share, CommercePayout): stored in `digest_mail` (pending) and sent as
  **CommerceDigestMail** (0x45/0x10025: u64 mail, u64 recipient, string name, string subject, string body) to one linked
  world of the follower's realm (the world they followed from first). That world greets them by their current name,
  queues it on the in-process outbox (20 mails per tick, sender = client text 563379 "Player Studio", through the social
  seam) and answers **CommerceDigestMailSent** (0x10026) once it went out (also for a character that is gone). Mails
  for a realm with no world linked wait and are sent at its next link; a mail handed to one world is not offered to
  another world of the realm for 10 minutes unless the first world's link closed.
- **The page** (WebPages `/api/follows`, `/api/follow`, `/api/unfollow`) asks through the world: **CommerceFollowRequest**
  (0x10023: u32 request, caller, u8 action 0 list / 1 add creator / 2 add tags / 3 remove creator / 4 remove tags, u64
  creator, string text) → **CommerceFollowReply** (0x10024: u32 request, bool ok, string message, bool digest on, the
  creators {u64, string}, the tag groups). A creator typed by name is found in the world's character database (the
  realm's) before the request. While the service is unreachable the tab says so and nothing changes.
- **Dev**: `followdigest [dry] [hours]` on such a world sends the admin request "followdigest" and shows the service's
  summary (a run for the world's realm; `dry` composes only).

## Tests

Checked in a running client with scripted runs (the test harness is not part of this repository): create, the review
offer and popup, TemplateData, the vote and decision, and the owner's client showing kinds 0x10 / 0x20 as Player Studio
mail. Unit tests: `LandmarkEmulator.Tests` (`StaffQueueTests`, `ReviewSpendRuleTests`). `psoffer` is a test-only dev
command that sends the 0x40 reply unasked.
