# Store pricing (6.60) and founder entitlements (6.59): agent report, 2026-10-17

Branch **`e-store`** (worktree `<workspace>\wt-store`), commits `349e514` and `e277c32` on top of `client-0.695` e73a744. Not
pushed, not merged. Build: `dotnet build LandmarkEmulator.WorldServer.csproj -v q`, 0 errors, no warnings in the
changed files. Nothing was started, stopped or restarted, and no database was touched.

The user's decisions (2026-10-17) this implements:
- **6.59**: give the Founder's Pack and reward items to everyone for now (also as tester items), behind a server
  setting so they can be turned off later.
- **6.60**: the store matches live. Lumen Station outfits cost Lumens, and props keep their live sources. A server-owner
  toggle makes the store free. Anything live sold for Daybreak Cash (DBC) gets a Lumen price at what Lumens were worth on
  live. Nothing is ever paid with real money.

Evidence marks: **[P]** = proven by client data (gameData695 tables, the client text table `re/out/text_695.tsv`, the
0.695 decompile). **[W]** = wiki, forum or patch note (`trace/0695/patchnotes.tsv` P-numbers). **[I]** = inferred.

---

## 1. What one Daybreak Cash was worth in Lumens: **1.3** (setting `LumensPerDaybreakCash`)

| # | Evidence | Rate it gives |
|---|---|---|
| 1 | **New find. The client has live's Lumen products** (texts 574410-574422, in 0.589 and in 0.695) [P]: "Lumen Box: This hefty box contains **650** Lumens"; "Lumen Crate: **1300** Lumens plus a bonus of 50 (1350)"; "Lumen Chest: **2600** + 150 (2750)"; "Cache of Lumens: **6500** + 500 (7000)"; "Hoard of Lumens: **13000** + 2000 (15000)". No client code reads these ids, so live's store served them. | The base amounts form a 1 : 2 : 4 : 10 : 20 ladder. That is Daybreak Cash's own 500 / 1000 / 2000 / 5000 / 10000 denominations ($5-$100), which gives **1.3 Lumens per DBC** before the volume bonus (up to 1.5 with the Hoard's bonus). The DBC price of each pack is not in the client [I, strong]. |
| 2 | **The Lumen Station's own price grid** [P]: of its 943 Lumen-priced recipes (station 55), 939 cost a multiple of **65 Lumens** (65 ×153, 130 ×226, 260 ×203, 520 ×336, 780 ×20, 1040 ×1). The other four are 20 (Dungeon Builder's Starter Set) and 90 ×3. | 65 = 1.3 × 50 DBC: the station's prices are DBC prices in 50-DBC steps × 1.3. Examples: outfits and the Inventory Expansion at 520 = 400 DBC; Superb chest 260 = 200; Design Expansion 1040 = 800 [I, strong]. |
| 3 | The March 2016 wipe refunds: "When we granted lumen refunds for marketplace items, we granted them based on the amount of Daybreak Cash spent" (Domino, 28 Mar 2016, P4342). "All the Lumens needed (plus an extra bonus)" (Wipe Q&A, P4062) [W]. | One player spent 1,214 DBC (9 × 124 + 2 × 49) and got "approximately 1950 Lumens" (forum 56635 #5 / #7) [W]. That is about 1.6 with the bonus included. At 1.3 the bonus would be about 24 % [I]. |
| 4 | "Lumens will eventually be possible to purchase for Daybreak Cash ... at the moment we are intentionally not selling them" (25 Mar / 1 Apr 2016, P4315 / P4431). Lumens were on sale by July 2016 (14 Jul 2016, P6089: "it will first prompt you to purchase lumens") and in Aug 2016 ("Daybreak Cash is used to buy Lumens", forum 57902) [W]. | Places the Lumen Box price list (row 1) in the launch era. |

**Chosen: 1.3**, the price live sold Lumens at (rows 1 and 2 agree). The refund's ~1.6 included a one-time bonus.
- The setting is configurable (`LUMENS_PER_DBC`).
- Prices are rounded **up to whole 5 Lumens** [I]. This puts live's .99-style prices on the station's 65-Lumen grid:
  499 → 650 and 799 → 1040.

Searched without finding a Lumen-pack DBC price: all 203 forum threads (`design/forum/all`, the same set as
`re/out/forum_raw`), `patchnotes.tsv`, `design/game-rules.md`, `design/patchnotes-after-0589.md`, all 2,340 wiki pages,
and the 0.695 client text. Open lead A below.

## 2. What live sold for Daybreak Cash in 2016, and the Lumen prices set

The 2016 Marketplace (launch to the last update):

| Product | Live evidence | Live price | Store price (Lumens) |
|---|---|---|---|
| Build Site Expansion (server item 900001) | "the price of Base Build Sites has been reduced to **799 DBC**" (Week End Wrap Up, 1 Apr 2016, P4471). Listed as "Base Build Site Expansion" (P4450). Still sold on 14 Jul 2016 (P6083-P6084) [W] | 799 DBC | **1,040** |
| Fantasy Parlor Pack (4419) | "can only be purchased ... for **499 DBC**" (wiki Fantasy Parlor Pack, July 2016). On sale from 30 Jun 2016 (P5910-P5912) [W] | 499 DBC | **650** |
| Six pet figurines: Shadow Wolf, Stag, Cockatrice, Bunny, Beetle, Jackalope (900101-900106) | "There are also six other pets available from the Marketplace" (28 Jul 2016, P6144). Each figurine's wiki page: "Purchase from the marketplace for Daybreak Cash" [W] | **not found** | **650** (placeholder: 499 DBC, the Fantasy Parlor Pack's price [I]). Open lead B |
| Superb / Legendary Reward Chest (4514 / 4515) | "two higher-tier reward chests ... at the lumen station, or also available in the marketplace for DBC" (29 Sep 2016, P6501) [W] | DBC price not found | **260 / 780**: the Lumen Station's own price (recipes 5363 / 5364) [P]. Open lead C |
| Lumens (Lumen Box ... Hoard of Lumens) | Rows 1 and 4 of §1 | about 500-10000 DBC [I] | **not sold**: players earn Lumens. The inventory's "Get more Lumens" button (InventoryDocumentClass.as `OpenMarketplaceTagSearch "Lumen"`) searches the store for "Lumen", and the page then explains how to earn them. The Crimson Parlor's + (BarberShopWidget.as AddLumensButton → `Browser.OpenMarketplace`) opens the store front. |
| Player Studio designs | Creator-priced in DBC (P1149-P1212, Player Studio wiki) [W] | the creator's price | **unchanged**: still taken free on the Player Studio page. Open lead D |

Not sold on live in 2016, so **not in the priced store**:
- The 290 Lumen Station outfits: the Lumen Station, 520 Lumens.
- The 448 discovered props: drops and the Lumen Station.
- Innate props, design items (goal rewards and starter castle), and the Vault / Inventory / Design Expansions (the Lumen
  Station; purchased expanders were "refunded as lumens and you can reacquire the storage from the Lumen Station", P4289).
- The beta Marketplace bundles, orbs and builder's chests (2014-15 only).

**One exception:** the **Build Site Vendor** stays in the store for free. It is this server's working shop (Vendors.cs,
TASKS 6.52, not decided yet). Its prop 1491 is in no palette category, so the store is its only route. It is marked "this
server's vendor feature, not a live product". If 6.52 decides for live fidelity, drop it in
`Store.PricedCatalogue`.

### How the client shows prices (checked before choosing what to send)
- The store's prices are shown on **this server's Showcase page** (the client's embedded browser, `InGameMarketplaceBrowser`,
  docs/showcase.md), not by a client store packet.
- No 0.695 UI movie has a store or bundle window. In `re/as3_695` the only InGamePurchase uses are Player Studio creation
  (PlayerStudioWrapper.as `InGamePurchaseStoreScreen.Product*`, SelectedTemplate) and `MarketingData.AutocompletionTags`.
- The client's StoreBundle / PreviewOrder / PlaceOrder packets (coverage-695.md §0x45: StoreBundleBase 0x5 reader
  FUN_143184280, PlaceOrderResponse 0x4 FUN_143183b10 ...) and the `StoreBundle*` Lua bindings (luabind_695.tsv) stay unsent
  and unused. **No wire layout was added or guessed.**
- The DBC wallet stays empty: WalletInfoResponse 0x0C, Commerce.SendWalletInfo, balance 0.
- Lumens are taken with the server's own currency path: `WorldSession.ChangeCoins`, item 1579, the same as the Lumen
  Station and DesignMaterials. The HUD's Lumen count updates through ClientUpdate CoinCount.

## 3. Entitlements (6.59)

A new `Game/Entitlements.cs` adds three packages. Each is granted **once per character** at world entry, from
`PlayerManager.Enter` after `Admin.Enter`. The grant is recorded as `character_unlock` kind **26**, value = package id.

| Package (`EntitlementPackages`) | Items | Recipes taught | Evidence |
|---|---|---|---|
| `founder` (1) | 435 Settler Flag, 436 Explorer Flag, 437 Trailblazer Flag, 441 Void Vault, 2568 Settler's / 2569 Explorer's / 2570 Trailblazer's Pickaxe, Tech Commander's Gear (900343), Tech Sergeant's Gear (900341) | 271-273 (the flags, Workshop 30), 1713-1715 (the tier pickaxes, Stone Forge 48), 1844-1846 (Change Mod for each tier pickaxe, Stone Forge 48) | Wiki "Founder's Packs": the Trailblazer pack holds every tier [W]. Item records [P]. The tier pickaxes say "An upgraded version of the Founder's Pickaxe" [P]. The Founder's Pickaxe itself (item 434, a component of recipe 1874) has no record in the live item list, so the tier pickaxes stand in for it [I]. None of the recipes is default-learned or taught by an item [P], so only an entitlement could have taught them [I]. Tech gear: server-built records from Cosmetics.cs (tints [I]) |
| `yearzero` (2) | 2483 Year Zero Trophy + one Year Zero Survivor's Outfit (900331-900334, random colour) | none | Wiki "Year Zero Trophy": mailed, outfit "in one of four random colors" [W]. The letter is **the client's own**: sender 568827 "Landmark Team", subject 568828 "Congratulations, You Survived Year Zero!", body 568829 [P]. No client code reads these ids, so live's server sent them |
| `promo` (3) | 406 Iron Portcullis, 1392 Fan Faire 2013 Souvenir Flag, 1394 Fan Faire 2014 Souvenir Flag, 1395 Layers of Landmark | none | Iron Portcullis as a giveaway-only rare (P1138-P1147) [W]. "Awarded to attendees of the ... Fan Faire" (item descriptions) [P] |

- **Delivery** (`EntitlementDelivery`):
  - `mail` (default): letters from "Landmark Team", at most 4 items per letter. That is the compose window's slot count;
    whether the reading window shows more is not proven [I]. The founder package arrives as 3 letters, "Your Founder's
    Pack (1/3)". All five letters: founder 1-3, Year Zero, "Landmark giveaways".
  - `bag`: straight into the bag, as live put Founder's Pack items in a new character's inventory. Whatever doesn't fit
    is mailed.
- **The palette** (`RecipeBook.KnowsProp` → `Entitlements.PropAllowed`):
  - The props of these items (flags 387 / 384 / 386, Void Vault 407, Year Zero Trophy 2043, Iron Portcullis 823, the Fan
    Faire props) stay placeable from the palette only while their package is on.
  - With it off they leave the palette. Bag items still place them (the item path needs no knowledge), so nothing
    already handed out is lost.
  - With the default (on), the palette behaves exactly as before.
- **Per character, not per account.** Live granted an account's entitlements to its characters, and after the wipes to
  the first new character. "Everyone" makes the difference moot for now. Open lead H.

## 4. Settings and defaults

All of these live in `WorldServer.json` (WorldServerConfiguration.cs) and in the env of `docker/world/docker-compose.yml`.
`.env.example`, `WorldServer.example.json`, docs/docker.md, docs/showcase.md and docs/gameplay.md are updated.

| Setting | Env | Default | Meaning |
|---|---|---|---|
| `FreeStore` | `FREE_STORE` | `false` | false = live's priced store (§2); true = the whole old catalogue free |
| `LumensPerDaybreakCash` | `LUMENS_PER_DBC` | `1.3` | DBC → Lumens rate (§1) |
| `StoreLumenPrices` | `StoreLumenPrices__<item id>` | empty | The owner's Lumen price per item (0 = free). Replaces a product's price, or adds any other catalogue item to the priced store |
| `FounderEntitlements` | `FOUNDER_ENTITLEMENTS` | `true` | Grant the packages and keep their props in the palette |
| `EntitlementPackages` | `ENTITLEMENT_PACKAGES` | `founder,yearzero,promo` | Which packages (`all` also works) |
| `EntitlementDelivery` | `ENTITLEMENT_DELIVERY` | `mail` | `mail` or `bag` |

The store catalogue is built once, so changing these needs a world restart.

## 5. Files changed (branch `e-store`)

- `LandmarkEmulator.WorldServer/Game/Entitlements.cs`: **new**, the packages, grant, mail / bag delivery and prop gate.
- `LandmarkEmulator.WorldServer/Game/Store.cs`:
  - `MarketplaceProducts`, `PricedCatalogue`, `LumensFor`, `LumenStationPrice`.
  - `Item` gains `Price` / `PriceNote`.
  - `Take` charges Lumens: the count is cut to bag room and to the Lumens the player has; Lumens are taken before the item
    is given, on the world thread.
  - The old catalogue is now `Everything()`.
- `LandmarkEmulator.WorldServer/Game/WebPages.cs`:
  - `/api/catalogue` adds `price`, `priceNote`.
  - `/api/me` adds `lumens` (read on the world thread) and `freeStore`.
  - `/api/take` returns `lumens`.
- `LandmarkEmulator.WorldServer/Resources/Web/showcase.js`: prices, Buy / Get, the Lumen balance in the header, the
  "how to earn Lumens" note on "Lumen" searches, and a help text for each mode (ES5, `node --check` OK).
- `LandmarkEmulator.WorldServer/Game/RecipeBook.cs`: the `KnowsProp` entitlement gate, and public `TeachRecipes`.
- `LandmarkEmulator.WorldServer/Game/Unlocks.cs`: kind 26 `EntitlementGranted`.
- `LandmarkEmulator.WorldServer/Game/PlayerManager.cs`: calls `Entitlements.Enter`.
- `LandmarkEmulator.WorldServer/Game/Commerce.cs`: class note only.
- `LandmarkEmulator.WorldServer/WorldServerConfiguration.cs`, `WorldServer.example.json`, `docker/world/docker-compose.yml`,
  `.env.example`: the settings.
- `docs/showcase.md`, `docs/gameplay.md`, `docs/docker.md`.

**Merge notes:**
- Unlocks kind **26** is new here. If another branch (e.g. `e-membership`) also adds a kind 26, renumber one of them.
  The value is stored in `character_unlock`.
- No DB migration is needed.

## 6. Not settled: open leads (for trace/0695/OPEN-LEADS.md)

| Lead | Searched so far | Try next |
|---|---|---|
| **A. DBC prices of the Lumen Box ... Hoard of Lumens** (650-13000 Lumens; 1.3 assumes 500-10000 DBC) | forum (203 threads), patchnotes.tsv, wiki, client text and decompile (574410-574422 are referenced by no client code) | archive.org snapshots of `store.landmarkthegame.com` / `www.landmarkthegame.com/store` and the Daybreak store (2016-07 to 2017-02); Reddit r/Landmark; July-Sept 2016 YouTube Marketplace videos |
| **B. Pet figurine DBC prices** (placeholder 499 DBC → 650) | spec-pets.md (lists it as open), wiki figurine pages, forum 57751, patchnotes | same archives; the forum thread "Cosmetic pets" (if outside the 203); stream VODs of 28 Jul 2016 |
| **C. Superb / Legendary Reward Chest DBC prices** (P6501; Lumen Station price used) | forum 57987 / 57902, wiki | same archives; if found, a cross-check of the 1.3 rate (260 / 780 Lumens ↔ 200 / 600 DBC expected) |
| **D. Player Studio designs for Lumens** (live: the creator's DBC price, minimum 25; the creator was paid a share) | Commerce.cs, the Player Studio wiki, forum 2014 threads (listing fee 10 % / minimum 100 SC; "I get 600 sc" of 1000) | decide the creator payout (Lumens by mail?) and the remote-commerce path (`ICommerceService.TakeDesign`). Still free today |
| **E. Founder items with no item record:** Founder's Pickaxe 434, Ring of Bounty, Mastercraft Bracer, Mega Pocket (Inventory / Vault), Noble's / Courtier's Regalia, the title Founder (560080 / 560093), Founder's Harvesting Strike 560082, Founder's Shockwave Pickaxe 561742 | live item list (items.json / items_summary.tsv), text_695 (names only), cosmetics-695.md (Regalia mesh not identified) | Regalia: search the 0.695 packs for a Founder / Regalia mesh (as cosmetics-695.md did for Tech gear). Title: build a "Title: Founder" item and give Titles.cs an explicit title text (560093 is 13 ids from 560080, outside Titles' ±4 search). Ring / Bracer: the 0.589 item list or older captures. Mega Pocket: P4289 says the slots were "added to your inventory/vault size"; the amount is not known |
| **F. Live's "restore missing Founder's Pack items"**: texts 562124-562126 ("Grant Settlers / Explorers / Trailblazers Pack Items If Missing", likely GM labels), 562130, 562156-562158, 567828; Founder's Chest prop 898 (`Theme_FoundersPack_Chests_Founder01`) at the Spires (wiki Founder's Pickaxe) | text_695; the decompile has none of these ids, so they were server-sent | Interaction on prop 898: re-grant missing package items with these texts. Needs a placed chest at the Spires (spire layout) |
| **G. Unproven bits** | | **Year Zero Gift** 568793 / 568802 (a gift-box item, no record; the outfit is given directly). **Alpha Boost Potion** 446 (recipe 527, unmapped; probably alpha-founder, source not found). **Tech Sergeant's Gear (Orange)** 900342 (source not found). Whether the mail reading window shows more than 4 attachments |
| **H. Account-level entitlements** (live: per account, granted to each character / first character after wipes) | | Only matters once the setting is per-account (an auth-DB entitlement list), e.g. if real founders are to be distinguished later |

## 7. In-game test steps (main session, 0.695 test copy <0.695 test copy>, re/dlogin.sh)

Build and deploy the `e-store` branch to the dev world. This **restarts the dev world**. Make sure `.env` has no
`FREE_STORE` / `FOUNDER_ENTITLEMENTS` overrides (defaults apply).

1. **Entitlements, mail (default).**
   - Log in with an existing character. Within a second of world entry: nine "Learned recipe" notices (AddRecipe) and
     5 new letters (mail notification).
   - Open Notifications → Inbox (or a mailbox). Expect "Your Founder's Pack (1/3)" (Settler / Explorer / Trailblazer
     Flag, Void Vault), "(2/3)" (the three tier pickaxes), "(3/3)" (Tech Commander's Gear, Tech Sergeant's Gear), "Congratulations, You
     Survived Year Zero!" (the client's letter text; Year Zero Trophy and one Year Zero Survivor's Outfit), and "Landmark
     giveaways" (Iron Portcullis, both Fan Faire flags, Layers of Landmark). Sender: "Landmark Team".
   - Take the attachments: the items arrive in the bag.
   - World log: `Entitlement 'founder' granted to <name>: 9 item(s) (0 to the bag, 9 by mail), 9 recipe(s) learned.`
2. **Recipes.**
   - At a Stone Forge: Settler's / Explorer's / Trailblazer's Pickaxe and the three Change Mods are listed.
   - At a Workshop: the three founder flags.
3. **Once only.** Relog: no new letters. `dbq.sh "select * from character_unlock where kind=26"` shows rows 1, 2, 3 for
   the character.
4. **Bag delivery.**
   - Set `ENTITLEMENT_DELIVERY=bag` and restart the world.
   - Append `@<Name> clearunlocks 26` to the world's dev_commands.txt, then relog.
   - The items go straight to the bag (overflow is mailed).
5. **Off switch.**
   - Set `FOUNDER_ENTITLEMENTS=false` and restart.
   - Build mode: placing the Void Vault / a founder flag from the palette is refused ("You haven't learned how to place
     that yet.").
   - Placing one from a bag item still works.
   - No letters for a fresh character.
6. **Priced store (default).** Open the Showcase (shopping cart) → Store.
   - Header: "Signed in as <name> · N Lumens".
   - The list: Build Sites › Build Site Expansion **1,040 Lumens**; Cosmetics › Crimson Parlor › Fantasy Parlor Pack
     **650**; Cosmetics › Pets, six figurines **650** each; Decorations › Reward Chests › Superb **260**, Legendary **780**;
     Decorations › Build Site Vendor **Free**.
   - Each item's detail shows the price note.
   - Buy with too few Lumens: the toast "… costs 1,040 Lumens; you have N."
   - `@<Name> coins =5000`, then Buy the Build Site Expansion. Expect: the item in the bag, the HUD Lumen count at 3,960,
     and the header updated. Log: `Store: <name> took 1 × Build Site Expansion (900001) for 1040 Lumens.`
7. **The client's own store links.**
   - Inventory "Get more Lumens": the store opens on a "Lumen" search with the note "Lumens are not for sale on this
     server: earn them …". The Crimson Parlor's + opens the store front.
   - Build Site Management "Get more build sites": the store opens on "Site Expansion" and shows the Build Site Expansion.
   - Crimson Parlor "Unlock": the store shows the Fantasy Parlor Pack.
8. **Free store.**
   - Set `FREE_STORE=true` and restart: the old full catalogue (outfits, decorations, designs), all "Free" with "Get", and
     the note "Everything in the store is free on this server."
   - Optional: `StoreLumenPrices__900001=500` (with `FREE_STORE=false`) shows the expansion at 500 Lumens.

---

**Summary.**
- **Rate:** 1.3 Lumens per Daybreak Cash. This is live's own Lumen price: the client holds live's Lumen Box ... Hoard of
  Lumens texts (650 / 1300 / 2600 / 6500 / 13000 Lumens, a 500-10000 DBC ladder), and 939 of the 943 Lumen Station prices
  sit on the matching 65-Lumen grid. The 2016 wipe refund (~1.6) included a bonus.
- **Priced store (default, `FREE_STORE=false`):** only live's 2016 Marketplace products, all in Lumens:
  - Build Site Expansion: 799 DBC → 1,040.
  - Fantasy Parlor Pack: 499 DBC → 650.
  - Six pets: 650 (placeholder price).
  - Superb / Legendary Reward Chests: 260 / 780 (the Lumen Station's price).
  - Build Site Vendor: free, pending 6.52.
  - Lumens are never sold; the page explains how to earn them.
- **Free store:** `FREE_STORE=true` restores the old free catalogue.
- **Entitlements (default on, `FOUNDER_ENTITLEMENTS=true`):** every character gets three packages once, by mail (or the
  bag):
  - Founder's Pack: flags, Void Vault, tier pickaxes, Tech gear, plus nine recipes.
  - Year Zero: trophy and a random-colour outfit, in the client's own letter.
  - 2014 promotional items.
  - Switching the setting off removes their props from the palette.
- **Open leads:** Lumen-pack, pet and chest DBC prices; Player Studio pricing; founder items with no item record;
  live's restore-from-chest; account-level grants.
