# In-game commerce and Player Studio (InGamePurchaseBase 0x45)

The 0.589 client carries the whole in-game commerce service of the live game: the Daybreak Cash store, wallets,
gifting, and **Player Studio**, where players sold their designs on the marketplace after a peer review. This server
keeps the store free (no real money, no Station Cash); Player Studio is rebuilt as the client drives it.

Server code: `LandmarkEmulator.WorldServer/Game/Commerce.cs` (Player Studio, account info),
`Network/Handlers/InteractionHandler.cs` (wallet 0x0B → 0x0C, upkeep). Tables: `player_studio_product`,
`player_studio_review`, `mail.kind`.

## Wire format

Every packet is `u16 0x45, u16 sub, body`. The client's response dispatcher is FUN_1431adf70 (a switch on the sub);
requests are written by the commerce manager (`DAT_145351c48 + 0x32050`) through FUN_143189e20 / FUN_14318aba0.

| Sub | Dir | Name | Body | Server |
|---|---|---|---|---|
| 0x0B / 0x0C | C→S / S→C | Wallet info | see InteractionHandler; the reply ends with `u8 zip on file` (manager +0x22bc18) | answered |
| 0x0F | C→S | Station Cash price list | `string currency` | logged, unanswered (store is free) |
| 0x15 | C→S | Country codes | `string locale` | logged, unanswered |
| 0x1A / 0x1B | C→S / S→C | Account info | reply `u32 result (1), string country, string currency, bool, string` (FUN_1431a67b0) | `"US"`, `"USD"`, true |
| 0x2F / 0x30 | C→S / S→C | Product create | see below | built |
| 0x3F / 0x40 | C→S / S→C | Product for review | see below | built |
| 0x41 | S→C | Admin product for review | as 0x40 (FUN_1431a71c0), opens the review window in admin mode | not used |
| 0x42 / 0x43 | C→S / S→C | Submit review | `u64 product, u32 verdict` / `bool ok` | built |
| 0x31, 0x33 | C→S | Product remove / update | | built (Commerce.RemoveProduct / UpdateProduct) |
| 0x34, 0x35, 0x37 | C→S | Follow, recommendations | | open |
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
9 item not found, 0xB not the design's owner, 0xC not valid, 0xF already on the marketplace (text
`CommerceCreateProduct*`). The server stores the product as Pending (status 0).

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
subtype 0/1). The reason drop-downs read `ReviewRejectionTypes` / `ReviewRejectionSubTypes`, which nothing in the
0.589 client fills, so a down vote arrives as 0.

**SubmitProductReviewResponse 0x43** `{bool}` (FUN_1431a8620): fires Lua `GameEvents:OnReviewComplete(ok)`.

Server rules: you never review your own product, and you vote at most once per product. The product is listed after
`PlayerStudioApprovals` approvals, or rejected after `PlayerStudioRejections` rejections (WorldServer.json, default
3 / 3; the live numbers aren't in the client). The dev command `psreview <id> approve|reject` decides a product
directly (the live client's admin path sent `appgamecommerce product approve|reject <id>`).

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

## Tests

`re/test/scenarios/adhoc/psprobe` (create), `psreview` (offer, popup, TemplateData, vote, decision),
`psmail` (the owner's client shows kinds 0x10 / 0x20 as Player Studio mail). `psoffer` is a test-only dev command
that sends the 0x40 reply unasked.
