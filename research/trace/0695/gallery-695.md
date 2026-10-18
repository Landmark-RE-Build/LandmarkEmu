# Gallery at 0.695: publishing, tags, likes, comments, Play Mode filter, contests (TASKS 6.43)

Traced 2026-10-16 from the 0.695 client (re/decomp_all_695, re/as3_695, <0.695 test copy> UI Lua, gameData695) and the
launch-era patch notes (trace/0695/patchnotes.tsv). [P] = proven in the client, [I] = inferred. Built in
LandmarkEmu (Game/Gallery.cs, Commerce.cs, Store.cs, Ratings.cs, Contests.cs, WebPages.cs, Resources/Web/showcase.js,
migration 20261027090000_GalleryComment); user docs in LandmarkEmu/docs/showcase.md and docs/commerce.md.

## 1. What live did (patch notes, dev posts, wiki)

| Row | Date | Rule |
|---|---|---|
| P4138 | 21 Mar 2016 | tags chosen from preset drop-downs, not free-form |
| P4139 / P4686 | 21 Mar / 22 Apr | comments area on gallery entries |
| P4140 / P4277 | 21 Mar | search by key words in the site name (previously tags only) |
| P4590 | 13 Apr | search by all three tag drop-downs + key words in the site name; every site had to be re-listed |
| P4591 | 13 Apr | untagged sites no longer show in the gallery |
| P4669 / P4680 / P4781 | 21-26 Apr | Gallery opened from the Travel menu / B key (Browser.OpenGallery) |
| P4670 / P4685 | 21-22 Apr | left / right sides of the gallery: leaderboards (most-liked) and "rising stars" (recently listed) |
| P4780 | 26 Apr | gallery search categories = the tags you can set on your Build Site |
| P4783 | 26 Apr | Site Management button to publish / unpublish any time |
| P5624 | 25 May | filter Build Sites by Play Mode status |
| P5630 | 25 May | only a build listed in the Gallery can be Liked; Like button removed from unlisted sites' banners |
| P5791 | 7 Jun | likes on banners and in the gallery consistent, never decay |
| P0752 (2014) | | emptying all tags = unpublishing; changing tags keeps votes |
| text 576478 | | Claims.Error.CannotEnterCompetition "You must publish your Build Site to the gallery before submitting to a competition." |
| wiki Gallery | | publish = name, up to 4 screenshots, tags (tags the only required step); "search the gallery for Taelaa" (P5898: owner names were searchable) |

## 2. The client side [P]

### 2.1 Build Site Management (re/as3_695/ClaimManagementWindow)
- `ClaimManagementWindowDocumentClass.as` 232-258 (GALLERY invalidation): `Claims.IsClaimListedOnGallery(guid) == "1"` →
  shows **Remove from Gallery**; else **Publish to Gallery**, enabled only when `epanel.Tag1/2/3 != ""`, otherwise the
  tooltip button "You need to tell us more about your Site before we can list it on the Gallery." The same three-tag
  test gates **Enter Competition** (195-231; tooltip UI.ContestTagTooltip 566907 "You must tag your Build Site before
  entering a contest.").
- Buttons (388-394): Publish → `EditPanel.listClaimOnGallery()` = `Claims.ListClaimOnGallery(guid, imageCount)`;
  Remove → `Claims.DelistClaimOnGallery(guid)`; Enter Competition → `GameEvents:ContestWindowOpen(guid)`; Leave →
  `Claims.RemoveFromContest(guid)`.
- `controls/EditPanel.as` 314-418 `getDropDownData`: the three drop-downs ("What Genre Is It?", "What Did You Build?",
  "What Can Others Do Here?") are filled from data source `GameData.ClaimDialogs.ClaimManagmentDialog.ClaimTags`
  (columns GUID, Name, Category 1/2/3, Id, NameId); the current tags come from `Claims.GetAllClaimTags(guid)` split on
  " ", word 0/1/2 matched against list 1/2/3 by label. A change sends `Claims.SetClaimTags(guid, "Tag1 Tag2 Tag3",
  imageCount)` (515-516). The 0.589 EditPanel has the same drop-downs (re/as3 Category1Dropdown).
- The tag table: `TagDefinitions.txt` (ID, NAME_ID, CATEGORY), loaded as "MrktBundleTag" (part_0105.c 34847,
  FUN_143470750). gameData695: 534 rows CATEGORY 0 (Marketplace bundle / autocompletion tags), **9 genre** (602-610:
  Aquatic, Fantasy, Holiday 576927 (0.695 only), Modern, Scary, SciFi, Steampunk, Underground, Western), **11 built**
  (611-621: City, Dungeon, Fortress, Home, Guildhall, Inn, Restaurant, Sculpture, Shop, Theater, Other), **7 activities**
  (544-600: In-Progress, Combat, Socialize, Story, Trade, Tutorial, Puzzle). Names are single words, which is why the
  client can split the tag string on spaces.

### 2.2 Lua natives (re/out/luabind_695.tsv; part_0113.c)
| Native | 0.695 | What it does |
|---|---|---|
| Claims.SetClaimTags | FUN_1435e3630 (7195) | refuses tags containing "contest_"; sends ClaimsPacketSetTagString; if the site has a listing (cache lookup FUN_143250b60 ≠ none) copies the listing (FUN_1432635e0, free byte 1), retags it `"%s %s"` = site tags + FUN_142d99b70 (7315) and queues a ProductUpdate (FUN_143263f70 → modified list +0x237a28, drained by FUN_143258eb0 / FUN_14326f280 on the commerce tick) |
| Claims.GetAllClaimTags | FUN_1435e3af0 | the claim entry's tag string (+0x400 / len +0x408) |
| Claims.ListClaimOnGallery | FUN_1435e5a20 (8826) | tags empty or containing "contest_" → Claims.Error.TagsAreInvalid (562613) locally; no listing → FUN_143263ed0(name +0x18, description +0x30, claim id, **item type 3**, price 0, tags +0x400, imageCount) → CommerceProductCreate 0x2F (free byte set); listing exists → the same retag + ProductUpdate as SetClaimTags |
| Claims.DelistClaimOnGallery | FUN_1435e5d20 (8960) | listing lookup → FUN_143263700 (part_0093.c 18897): "Removing Product=%lld from the store", drops it from the local product list (FUN_14326a8f0 on +0x236e00) and sends **ProductRemove 0x45 / 0x32** {u64 product}. No reply needed: IsClaimListedOnGallery is "0" at once |
| Claims.IsClaimListedOnGallery | FUN_1435e5de0 (8993) | "1" when the account-store product list (+0x236e00, status +0xf0 ≠ -1) holds a product whose item is the claim (FUN_143250b60 / FUN_143afa950) — i.e. what the server sent in **AccountStore** (0x39 in 0.695) plus local adds / removes |
| Claims.EnterContest | FUN_1435e5e70 (9020) | args (guid, contest id, "contest_..." tag, description, bool); not a contest_ tag → return; **no listing for the claim → message box "Oops!" + Claims.Error.CannotEnterCompetition 576478** (FUN_1432628e0 returns 0); else FUN_1432628e0 sends **ProductUpdate** {product, name "", description = entry description, price, tags = contest tag, byte +0xa0 = 1, contest id in the trailing string} and then FUN_142dce740 → ClaimsPacketSubmitContestEntry |
| Claims.RemoveFromContest | FUN_1435e6240 (9168) | FUN_143262a40: ProductUpdate with every string empty and byte +0xa1 = 1; nothing else is sent |
| Claims.RateClaim | FUN_1435e4870 | RatingPacketAddVote ("Claims" then "Contests"), unchanged from 0.589 (coverage-695 0xA7 same) |
| Browser.OpenGallery | FUN_1435e12b0 | `MarketplaceUrl#!/gallery` in the InGameMarketplaceBrowser, title "Gallery" |
| Browser.OpenGalleryWithTags | FUN_1435e0db0 (5368) | the marketplace search route `/#!/marketplace/search/limit=50&filterEquals=Purchasable:1&SearchTags=<tags>` (same string as OpenMarketplaceWithTags), title "Marketplace" |

- **FUN_142d99b70** (part_0068.c 21679): returns `"PlayMode"` when the claim's mode (+0x378) is 2, else "". It is
  appended to a listing's tags on SetClaimTags, on ListClaimOnGallery for an existing listing, and in the
  **ClaimInfoUpdate handler** (part_0069.c 14035: a mode update for a listed site retags the product). So live's
  "filter Build Sites by Play Mode status" (P5624) read a **PlayMode tag word** on the listing. The server takes the
  mode live instead and strips the word (Gallery.CleanTags).

### 2.3 ProductUpdate wire (0x45 sub 0x33, 0.695 **0x34**; writer FUN_143181020 = 0.695 FUN_14322ba80, "same")
`u64 product (+0x18), string name (+0x38), string description (+0x50), u32 price (+0x68), string tags (+0x70), u8 +0xa2,
u8 +0xa0 enter contest, u8 +0xa3, u8 +0xa4, u8 +0xa1 leave contest, u64 +0xa8, u64 +0xb0, string contest id (+0x88),
string (+0x20)`. The server reads the five bytes now (Commerce.UpdateProduct); the earlier doc only knew "u8 × 5, u64".

### 2.4 0.695 sub numbering (packets-695.md §4; now handled)
The 0.695 client inserted TemplateProductCreate at 0x30 and shifted every InGamePurchase sub ≥ 0x30 by +1. The server's
constants stay 0.589; `Commerce.FromWire` (InteractionHandler.HandleInGamePurchase) and `Commerce.ToWire` (Commerce.Send,
SiteScreenshots' ModifyScreenshotsResponse) translate for `WorldSession.Speaks695`. 0.695's 0x30 lands on 0x2F
ProductCreate, whose body it starts with (packets-695.md §12). Before this the 0.695 client's ProductRemove (0x32),
ProductUpdate (0x34), RequestAccountStore (0x37), Search (0x3E) etc. fell into the "unhandled" default, and the
server's AccountStore (0x38) / CreateResponse (0x30) replies were read as other messages.

### 2.5 Likes and the walk-on banner
- WalkOnText data source (part_0069.c 19421 ff.): `CanVote` = "0" while the claim's tag string is empty (+0x408 == 0,
  line 19884), else "2" when the CanVoteReply flag (+0x368) is set, "1" otherwise (TimeCanVote from +0x370).
  `WalkOnTextWindowDocumentClass.as` 806-815: CanVote 0 → the red button "Build Site cannot be liked currently."
  (562227); 2 → Like (562225); 1 → the timer button "You've already Liked this Build Site." / "Vote again in" (562226).
  So the 0.695 client itself hides Like on untagged (= unpublished, P0752) sites [P]; the server refuses the vote and
  answers CanVote "may not" for any site without a listing (Ratings) [P: P5630].
- RatingBase AddVote 0x8 / CanVoteRequest 0x18 / CanVoteReply 0x19 are unchanged in 0.695 (coverage-695 0xA7).

### 2.6 Contest state on the client
`InContest` (ClaimItemInfo data source, part_0069.c 8600) = claim entry +0x3c0 ≠ 0, which the server's claim record
leaves empty (Claims.cs 935 "contest data is empty"), so the window never shows Leave Competition today → OPEN-LEADS L104.

## 3. The web page (ours, [I] for the layout)
Live's gallery was a Daybreak web page in the embedded browser (store*.landmarkthegame.com), not in the client; its
layout is known only from the notes (search box, three tag drop-downs, Play Mode filter, left / right lists). The
server's page (Resources/Web/showcase.js `#!/gallery`) follows the notes: one `/api/gallery` list of published + tagged
sites; three drop-downs (`/api/gallery/tags` from TagDefinitions.txt) + name / owner words from the top search box (all
must match); a Play Mode filter (any / Play / Edit); side lists "Most liked" (leaderboard) and "Rising stars" (newest
listings, = the listing's CreatedAt; a site unpublished and published again is new again); each entry: primary
screenshot, name, Play / Edit badge, owner (online dot), landscape, listed-ago, competitions, tags, welcome message,
likes, comments count → comments box (list, post, remove own / on your site / admin), Visit. `SearchTags=` words from
the client's OpenGalleryWithTags pick the drop-downs when they are tag names.

## 4. Server rules (built)
| Rule | Where |
|---|---|
| Listed = Player Studio product ItemType 3, Status Listed; only those appear, only with all three tags (Gallery.IsTagged); untagged hidden | Commerce.Listings, Store.Gallery |
| Publish needs the three tags (NotValid otherwise); listing tags = the site's tags, "PlayMode" dropped | Commerce.CreateProduct |
| Unpublish = ProductRemove → Status Removed, contest entries withdrawn | Commerce.RemoveProduct |
| ProductUpdate: plain → name / description / tags (PlayMode dropped, contest markers kept); enter byte → contest marker added + entry description kept for SubmitContestEntry; leave byte → markers dropped, entries withdrawn | Commerce.UpdateProduct, Contests.EntryDescription / Withdraw |
| Likes only for listed sites (AddVote refused with a chat line; CanVoteReply may = false) | Ratings |
| Contest entry only for a listed site (text 576478); description from the ProductUpdate | Contests.Submit |
| Visit only listed sites | Store.Visit |
| Comments: gallery_comment table; 500 chars, one per 10 s per player; remove by author / site owner / admin | Gallery, WebPages |
| 0.695 InGamePurchase sub shift both ways | Commerce.FromWire / ToWire |
| Goal 1307 "Like a gallery site" now counts every counted Like (only listed sites can be liked) | Achievements.LikedSite |

## 5. Not verified in game yet
Untested on the client (coordinator tests): Publish / Remove flow on 0.695 (sub translation), the page, likes refused on
unlisted sites, EnterContest's ProductUpdate parse. re/test/scenarios/showcase.json's gallery step expects TestPlayer2's
site in the list: it must be published first (tags + Publish) or the scenario updated.
