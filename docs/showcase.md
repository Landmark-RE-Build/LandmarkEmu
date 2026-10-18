# Showcase: store and gallery

The HUD's **Showcase** button opens the `InGameMarketplaceBrowser` window (its title bar reads "Showcase"). The
window is an embedded Chromium browser (CEF, Chrome 33, `SoeBrowser/1.0`) that shows the URL in the client's
`ClientConfig.ini`. LaunchPad writes `MarketplaceUrl` there on PLAY (the server's first world's web pages address,
`WebPagesUrl`; docs/launchpad.md "What PLAY does"); by hand it looks like this:

```ini
[Marketplace]
MarketplaceUrl=http://127.0.0.1:19001
MarketplaceAssets=http://127.0.0.1:19001
```

The client adds its own routes to that address:

| Opened by | URL |
|---|---|
| Login preload (`Browser.Create`, view "marketplace") | `MarketplaceUrl#!/gallery` |
| `Browser.OpenMarketplace` | `MarketplaceUrl#!/marketplace` |
| `Browser.OpenGallery` | `MarketplaceUrl#!/gallery` |
| `Browser.OpenMarketplaceWithTags` | `MarketplaceUrl/#!/marketplace/search/limit=50&filterEquals=Purchasable:1&SearchTags=<tags>` |

The official pages lived on Daybreak's store servers (`store*.landmarkthegame.com`), which also delivered what
was bought. The client data has no catalogue (`Resources/Marketing/*.txt` are empty), so the world server now
serves these pages itself (`Game/WebPages.cs`, `Game/Store.cs`, page files in
`LandmarkEmulator.WorldServer/Resources/Web/`). With the shared commerce service (`COMMERCE_ADDRESS`) the pages stay on
each world; their Player Studio list, "Buy", gallery listings and contest names come from the service
(docs/commerce.md "Where it runs").

## Pages

- **Store** (`#!/marketplace`). What it sells is the world's `FreeStore` setting (`.env` `FREE_STORE`, TASKS 6.60):
  - **`false` (default): live's store.** Only what the live Marketplace sold in 2016, priced in **Lumens** (players
    earn Lumens; nothing is paid with real money and the client's Daybreak Cash wallet holds no Daybreak Cash — it shows
    the Lumens while the Player Studio listing fee is charged, docs/commerce.md):

    | Item | Live price | Lumens (1.3 per DBC) |
    |---|---|---|
    | Build Site Expansion | 799 DBC (1 Apr 2016) | 1,040 |
    | Fantasy Parlor Pack | 499 DBC (wiki, July 2016) | 650 |
    | Six pet figurines (Shadow Wolf, Stag, Cockatrice, Bunny, Beetle, Jackalope) | not known: 499 DBC placeholder | 650 |
    | Superb / Legendary Reward Chest | DBC price not known | the Lumen Station's 260 / 780 |
    | Build Site Vendor | not a live product (this server's shop, TASKS 6.52) | free |

    A Daybreak Cash price becomes Lumens at `LumensPerDaybreakCash` (`LUMENS_PER_DBC`, default 1.3: live's Lumen Box
    sold 650 Lumens per 500 DBC, and 939 of the 943 Lumen Station prices sit on that 65-Lumen grid), rounded up to whole
    5 Lumens. Everything else (outfits, props, designs) keeps its live route: the Lumen Station, exploring, drops,
    goals. The owner can set any catalogue item's price in Lumens (`.env` `STORE_PRICES=900001=500,2551=1300`, or
    WorldServer.json `StorePrices` / `StoreLumenPrices`, env `StoreLumenPrices__900001=500`; 0 = free), which also
    adds it to the store. The client's "Get more Lumens" button
    searches the store for "Lumen": the page then explains how to earn them.
  - **`true`: everything free**, as before 2026-10-17. Sections:
    - Outfits: equipment slot 1 cosmetics.
    - Decorations: prop items in the builder's prop categories (`PropCategory`, `PropCategoryPropMap`,
      `PropCategoryGroup`; uncategorised props such as game markers and crafting stations are left out).
    - Designs: design items with a client template.

  Tools, weapons, armour and accessories (slot 3 combat trinkets) are not sold: they are crafted. Each item has an
  icon, a name, a description and its price, plus a **Buy** / **Get** button; decorations can be taken 1/5/10/25 at a
  time (the count is cut to the bag's room and to the Lumens the player has).
  An item bought for Lumens can't be traded, mailed or put in a chest for `STORE_TRADE_WAIT_HOURS` (default 12; 0 =
  off): live's "fraud timer" (TransferItemResponse 0x12; the client's texts 577061, a recent purchase can't be traded
  yet, and 562759, nor put in a chest). The length is the most a developer post of 26 Mar 2014 gives (12 hours; the
  wiki says 12-24 hours). The item's tooltip counts it down,
  "Tradable in %TIME" (562375): the tooltip reply's FraudTimer (item record +0x120, Unix seconds when it becomes
  tradeable; read by the item tooltip and the crafting Upgrade panel).
  Kept on the item (`character_item.tradeableAfter`), and on a stack split; a stack it joins waits as long.
- **Gallery** (`#!/gallery`): every build site, best rated first, with owner, landscape, votes and an online dot.
  **Visit** travels there (the site's Starting Location, else its centre), as the Landscapes window would.
- **Following** (`#!/following`, ours; live: My Store → "Start Following", Update Notes 27 Aug 2014): follow creators
  by character name and tags (one word, or several words in one entry = only items carrying all of them, live's
  "PVP Castles"); each Gallery site and Player Studio design has a **Follow** link by its maker. Once a day
  (`SHOWCASE_FOLLOW_DIGEST`, default true = live; at `SHOWCASE_DIGEST_HOUR_UTC`, default 10) the server mails each
  follower what those creators listed, and what was listed with those tags, on Player Studio (designs accepted by
  review) and the Gallery (published build sites) since the last mail; nobody gets a mail when nothing new matches.
  See **Following and the daily mail** below.
- Search box: filters both pages; the client's tag search (`SearchTags=`) fills it in. The client joins the words
  with `+` (`Fantasy+Parlor+Pack`), which the page reads as spaces.

### The client's store products and the Crimson Parlor's Unlock (`Game/StoreBundles.cs`, TASKS 4.9)

The client keeps its own copy of the store's products ("store bundles"). Live's store server sent them; the Crimson
Parlor reads them. A Parlor option or tint that is not unlocked by default and not yet known is **not listed at all**
unless the client's store has a product for its unlocking item (the Fantasy Parlor Pack, item 4419). With one, the
Parlor lists it, and picking it shows the **Unlock** button with "You have selected an option that requires "Fantasy
Parlor Pack". Click to purchase this from the Marketplace." and the price. The button opens the Showcase on a search
for the pack's name, which lists the pack at its store price.

So at world entry the server sends the client one store (id 1) holding every product live's 2016 Marketplace sold that
this store sells now — the Build Site Expansion, the Fantasy Parlor Pack, the six pet figurines, the Superb and
Legendary Reward Chests — plus the store's membership row. Each product has the item's own name and description texts,
its icon, one of the item, and this store's Lumen price for that player: members' and trials' discount
(`MEMBER_STORE_DISCOUNT`) included, 0 with `FREE_STORE` (which only sends the products the free store lists); membership
is never discounted, as on the page. The owner's extra priced items and the free catalogue are not sent: the client
only looks products up for the Parlor, and the packet stays small (about 250 bytes a product, 2.7 KB in all).

When a player's membership starts or ends the store is sent again with the new prices (only if they changed); the
client replaces its whole store each time. Buying still happens on the Showcase page (no 0.695 window shows store
bundles: the client's store screens, its "Display store bundles containing ..." Inspect hooks and the bundle preview
natives are called by no 0.695 script or UI movie); using the pack from the bag unlocks the options and tints
(`Game/Unlockers.cs`), and the Unlock button goes away. The Parlor refuses a locked look that the character has not
unlocked (`Game/Barbershop.cs`). The packet layout is in docs/protocol.md (InGamePurchase).

**The client's own buy request** (since 2026-10-18): the Lua native `InGamePurchaseStoreScreen.PurchaseBundleWithInGameCurrency`
(0.695 FUN_1435e9400; bound by no shipped script or movie) sends **PacketClientInGamePurchase 0xD0** `{u32 Lua
argument 1, u32 Lua argument 3, u32 0}` on the Zone channel (writer FUN_1435ebe10) [P layout]. The server
(`StoreBundles.HandlePurchase`) takes whichever field is one of the player's bundle ids (above: 1,000,000 + item id;
999,999 = membership) as the bundle and the other as the quantity (1-100, else 1) [I: the arguments' meaning is not
traced], and buys it exactly as the Showcase page does (`Store.Buy`: the same Lumen price for that player, free with
`FREE_STORE`, Lumens taken before the item is given), all or nothing. It answers **PlayerUpdate 0x4B
cPlayerInGamePurchaseResult** `{u32, u32, u32}` — the two request fields and a result code from the client's own
purchase check CanPurchaseStoreBundle (FUN_143248df0: 1 ok, 3 not for sale, 4 bad quantity, 7 not enough currency; 0 =
bag full / too fast / membership refused [I]). The client reads that packet (FUN_142c4b300) and does nothing with it
[P], so the player is told with a chat line in the client's own in-game purchase texts (568731 "You have sucessfully
purchased …", 568732 "… do not have enough Lumens.", 568733 "… no longer for sale.", 568734 "… unknown reason.", the
claim vendor's texts) and sees the item and the Lumens counter change.

The page is plain ES5 with `XMLHttpRequest` and flexbox: the client's browser is Chrome 33 (no `fetch`, arrow
functions or CSS grid).

## Help page and help links

- `#!/help` is a help page for this server (Showcase tab "Help").
- The server's ClientSettings packet (0xE9: three URLs, u32, bool) used to carry PlanetSide 2's help and shop
  addresses (a leftover capture). It now points at `#!/help` and `#!/marketplace` on this server.
- The Escape menu's Help (Lua `Ui.OpenSupportUrl`) opens ClientConfig `PetitionUri` in the system's own web browser.
  Set it to `http://<server>:19001/#!/help` to land on that page.

## Who is buying: sign-in

The page only talks to the web server, so the server tells each player's browser who they are:

1. At world entry the server makes a random token for the session (`Store.SignIn`).
2. 10 s later (the browser only exists after the client's login preload) the server runs, in that client,
   `SoeGui.GetWindow('InGameMarketplaceBrowser'):ShowURL('<WebPagesUrl>/?t=<token>#!/marketplace')`. This goes
   through `ClientLua.RunCode` (below).
3. The page stores the token in `sessionStorage` (one per client process, so two clients on one PC stay apart)
   and sends it with every request. When it calls `/api/me` with it, the server marks the page as signed in.
   Opening the Showcase (UI event 7) before that re-sends the sign-in.
4. The client's own later navigations reuse the same browser, so the token stays. It dies with the session.

## Web API (`http://<WebPagesHost>:19001`)

| Request | Reply |
|---|---|
| `GET /` (any path without extension) | the Showcase page |
| `GET /web/showcase.js`, `/web/showcase.css` | page script and style |
| `GET /icons/<image set id>.png` | item icon (see below) |
| `GET /api/me?t=` | `{signedIn, name, lumens, freeStore}` |
| `GET /api/catalogue` | `[{id, name, description, icon, section, group, price, priceNote}]` (price in Lumens, 0 = free) |
| `GET /api/gallery` | `[{id, name, owner, landscape, description, rating, online}]` |
| `POST /api/take` `t, item, count` | `{ok, message, lumens}`: item added to the player's bag for its price (max 100, bag space and Lumens permitting, one take per 0.4 s) |
| `GET /api/studio` | `[{id, name, description, tags, author, voxels, image, price, creatorPrice}]`: listed Player Studio designs; `price` = Lumens the buyer pays (the creator's price × `LUMENS_PER_DBC`, 0 with `FREE_STORE`), `creatorPrice` = the price as the creator set it (docs/commerce.md "Buying a listed design") |
| `POST /api/studio/take` `t, product` | `{ok, message, lumens}`: buys the design for its price (a copy in the player's Designs, Collected; once per product; the creator is mailed `PLAYER_STUDIO_CREATOR_SHARE` % of it) |
| `POST /api/visit` `t, site` | `{ok, message}`: travel to the build site |
| `GET /api/follows?t=` | `{available, digest, creators: [{id, name}], tags: [string], maxCreators, maxTags, maxWords}`: what the player follows (ids as strings) |
| `POST /api/follow` `t, creator` or `t, tags` | `{ok, message}`: follow a creator by character name, or a tag group |
| `POST /api/unfollow` `t, creator` (id) or `t, tags` | `{ok, message}` |
| `GET /uploads/lm/images/templates[N]/(small\|large)/<24-digit guid>.jpg` | design screenshots (`MarketplaceAssets`) |

Work that changes the game runs on the world thread (`WebPages.Update` drains a queue each tick).

Settings in `WorldServer.json`:
- `WebPagesHost`: listen address (default loopback only).
- `WebPagesUrl`: the address players' browsers use (default `http://127.0.0.1:19001`; Docker:
  `http://<PUBLIC_HOST>:<WEB_PAGES_PORT>`); it is what LaunchPad writes as their `MarketplaceUrl`.

## Following and the daily mail (`Game/ShowcaseFollow.cs`, OPEN-LEADS L137)

What live did (Update Notes 27 Aug 2014): players followed creators (typed by name on My Store, "Start Following") and
tags, alone or grouped ("PVP" or "Castle" = either; "PVP Castle" = only items with both), and once a day the game
looked for everything newly accepted on the Marketplace or the Gallery and sent each follower an in-game mail about
their tags and creators (plus an e-mail, not built here). The 2016 wiki (Showcase, My Store) still describes it.

The 0.695 client only knows creator following (FollowCreator 0x35 / UnFollowCreator 0x36 → AddFollowing 0x3A /
RemoveFollowing 0x3B; the list in AccountStore 0x39; no shipped script calls the natives). Tag following has no native,
packet or text in the client: it was on live's web pages. Both live in the character database table `showcase_follow`
(one row per creator or tag group) and are edited on the Following tab; the client's packets use the same rows.

The run (once a day, per UTC day; `showcase_digest_run` records each run and makes the day's run happen once for all
worlds sharing the character database):
1. One query: products with status Listed whose `listedAt` (or `createdAt` for rows from before 2026-10-18) is after the
   last run (the first run looks back 24 hours). Nothing new: done, no other query, no mail.
2. One query: the follow rows for those items' creators plus every tag group.
3. Per follower, one mail: the new items from creators they follow, then per tag group the new items whose tags hold
   every word of the group; each item once, their own items left out, at most 40 listed ("...and n more"). Sender: the
   client's Player Studio mail sender (text 563379). The wording is ours (no copy of live's mail survives).
4. The mails go out from the world thread, 20 per tick, through the social seam.
5. The run's row is written first as unfinished (`mails` = 4294967295) and gets its mail count only when its last mail
   went out; the next run's window starts at the last finished run. A run left unfinished (the world stopped with mails
   still queued, or the run failed) is taken over after 15 minutes (at once after a failure in this world) and its
   window mailed again, so a mail can arrive twice but none is lost; the day counts as done only when its run finished
   (since 2026-10-18).

Limits (ours): 100 creators, 20 tag groups of up to 5 words (32 characters each). Dev command: `followdigest [dry]
[hours]` runs it now (`dry`: compose and show only; `hours`: look back that far instead of to the last run). The rules
and the mail's text are shared with the commerce service (`LandmarkEmulator.Internal/Commerce/FollowDigest.cs`).

**With the shared commerce service** (`COMMERCE_ADDRESS` set; since 2026-10-18) the follows and the digest are the
service's, like the products (`LandmarkEmulator.CommerceServer/FollowService.cs`; docs/commerce.md "Following and the
digest"): the Following tab works the same, through the world's commerce link (a creator typed by name is looked up in
the world's character database first); the service keeps the lists in `lmemu_commerce` (`following`, `following_tag`),
dates listings by `product.listed_at`, and runs one digest per realm and UTC day at the service's own
`SHOWCASE_DIGEST_HOUR_UTC` with its own `SHOWCASE_FOLLOW_DIGEST`. Each mail goes to a world of the follower's realm,
which sends it from this outbox (same sender and pace) and confirms; mails for a realm with no world up wait until one
links. `followdigest` asks the service for a run for the world's realm (summary only). The `showcase_follow` rows a world
kept in-process are not copied to the service.

## Icons

Item icons are the client's image sets: `ImageSetMappings.txt` (IMAGE_TYPE 6 = 128 px, 5 = 64 px, 4 = 32 px) →
`Images.txt` file name → a loose `.dds` under `Resources/Images/<folder>/` (a few are in the asset packs). They are
exported from the server owner's own game folder into the game data folder as `icons/<image set id>.png`; the web
server serves `GET /icons/<id>.png` from there and answers 404 for a missing one, so the pages show no icons without
the folder. No game file is in this repository.

The exporter is part of the world server (`LandmarkEmulator.WorldServer/Game/IconExport.cs`): it reads the two image
tables and the image files (and, for the few icons not loose, only the index of `Resources/Assets/*.pack`), decodes the
DDS files (DXT1 / DXT3 / DXT5, the formats the icons use; a PNG is copied as is) and writes PNGs. It never writes to the
game folder. Icons already exported are kept, and a stamp file (`icons/.icons-export`) skips the whole run while the
image tables are unchanged.

- **Docker:** the one-shot `icons` service does it on every start (docker-compose.yml; the first start about 2 minutes,
  then at once). Nothing waits for it; `docker compose logs icons` shows the counts.
- **Without Docker:** `dotnet LandmarkEmulator.WorldServer.dll --export-icons "<Landmark folder>" "<GameDataPath>/icons"`
  (optional `--size 5` prefers the 64 px icons). With the 0.695 client it writes about 5,050 icons; about 65 image
  sets name files the game does not have.

## Running Lua in the client: `ClientLua.RunCode`

The server's existing Lua channel is the first-time-event Script packet (FirstTimeEventBase sub 5 → client
FUN_142e3fdf0). It calls one named function with string arguments separated by commas, and:
- A **Lua-defined** table function (`Table.Method`) gets exactly the arguments and no `self`. So
  `InGameMarketplaceBrowser.ShowURL(url)` failed with "attempt to index a string value (local 'self')".
- A **native** table function gets the table as its first argument. `Ui.DoCommand` saw `(table, "Ui")` and ignored
  it, because it reads argument 1.
- A name without a dot is a global function with just the arguments.

`RunCode(session, code)` combines two calls:
1. The global `BindCommandToLua("lmemu", code)` binds the console command `lmemu` to the code. Console commands
   are lower case.
2. `Ui.ProcessChatCommand("/lmemu")` runs it. This native reads the last argument, so the table in front doesn't
   matter. The client also logs "Unrecognized command - lmemu" afterwards.

Code containing commas is sent as `load("...")()` with each comma written as `\44`. The dev command
`luacode <code>` runs code in a player's client. The winner banner fix
([instanced-games.md](instanced-games.md)) uses the same path.

## Likes (build-site ratings)

The walk-on window's like button (Lua `Claims.RateClaim`) sends **AddVote** (RatingBase 0xA7, sub 8) twice on the
World channel: category "Claims", then "Contests", each with the site id as a decimal string. Body: string category,
string item id, string, string, f32 score (1.0), u64. The contest walk-on also asks **CanVoteRequest** (sub 0x18: four
strings, the first numeric one is the site, and a u64).

Answers: **VoteReply** (sub 0x12) {u64 voter, string item id as sent, bool counted, u64 the site's like total} and
**CanVoteReply** (sub 0x19) {u64 site, string the site key as sent, bool may vote, u64 0}. Rules: one like per player
per site, never your own site, and only sites listed in the gallery (patch notes, 25 May 2016). A refused like also
gets a system chat line ("You can't rate your own build site." / "Only build sites listed in the gallery can be liked."
/ "You have already rated this build site."). A counted like advances the "like" daily goal and the like achievements
of the voter and, when online, of the owner.

Where it runs: in-process, `Game/Ratings.cs` keeps the likes as character_unlock rows of kind 11. With the world-channel
container (`docker/worldchannel`, since 2026-10-16) `LandmarkEmulator.WorldChannel/RatingBroker.cs` answers the
"Claims" votes and CanVoteRequest itself, with the same bytes, from its own database `lmemu_wc_<world>` (table
`claim_vote`); it asks the zone about the site (ClaimInfoQuery 0xF9/0x10040 → reply 0x10041: owner, landscape, tags,
exists, listed) and tells it the outcome (RatingVoteCounted 0xA7/0x10042: goals, achievements and the zone's kind-11
copy; RatingVoteRefused 0xA7/0x10043: the chat line). "Contests" votes and the contest list stay in the zone. While the
container is down the zone answers from its kind-11 copy; the container copies those likes in at its next start (and
all kind-11 likes on its first start).

## Tests

Checked against a running world with a scripted client run (the harness is not part of this repository): token
sign-in; the page, me and catalogue requests; a free outfit for the right player; a wrong token and an unknown item
refused; the gallery; Visit.

## Portraits and headshots

The official portraits came from a rendering service (Fotomat). The 0.589 client can render its own instead:
its ScreenshotManager captures a portrait and a headshot of the local character. It then:
1. sends the server the CRC-32 of portrait.png (AddClientPortraitCrc 0x75 `{u32}`);
2. posts both images to ClientConfig `[Portrait] UploadUrl` (multipart, boundary `AaBb432101234bBaA`: imageType
   `portrait`, characterId, thumbnailFile `headshot.png`, imageFile `portrait.png`).

The server side is `Game/Portraits.cs`:
- **Uploads** are taken at `POST /portrait/upload`, so set `UploadUrl=http://<server>:19001/portrait/upload`.
  An upload is only kept if its CRC is the one that character announced over its own game session, so nobody can
  overwrite someone else's portrait. Images are stored as `Portraits/<character>_portrait.png` and `_headshot.png`
  next to the server.
- **Requests:** clients ask with FotomatBase 0x98 sub 2 PortraitDataRequest `{u64 character, string "Headshot" |
  "Portrait"}` (sent for party members and other players). The server answers with sub 3 PlayerImageData
  `{i16 sub 3, u64 character, string kind, bool, appearance block (empty), u32 length + PNG}` (reader FUN_142ebab00)
  when it has an image. (Until 2026-10-16 the sub went out as 4 bytes, which shifted every field after it.)
- **With the shared commerce service** (`COMMERCE_ADDRESS`, docs/commerce.md "Where it runs"): the checksums,
  the images (`Portraits/<world>_<character>_<kind>.png` in the service's data volume, table `portrait`) and the
  answers are the service's; the world's web page still takes the upload and hands it on. A character's portrait is
  found on every world sharing its character database. Each world's existing `Portraits/` files are sent to the
  service once, when it first links.

Limits found in 0.589:
- No window of this client shows those images: the Flash helper `BindPlayerPortrait` (`imgps://<guid>`) is never
  called, and the Party window has no faces.
- What makes the client capture and upload its own portrait is not traced yet (the capture and post functions are
  only reached through function pointers).

Tests: a script outside this repository posts an upload the way the client does. Dev commands: `portraitcrc <crc>`
and `portraittest <character> <kind> <icon id>`.
