# World-channel service (Phase B #4) — spec

Agent #15, 2026-10-15. Static work only: client = re/decomp_all (0.589) and re/decomp_all_695 (0.695); server =
LandmarkEmu (file:line as of today). Nothing was run. Contract: [internal-transport.md](internal-transport.md).
Scratch (re-runnable): re/out/agent_worldsvc/ — `sites.py` + `run_sites.py` (re/tools/channels.py per send site
instead of per class) → `sites_589.tsv`, `sites_695.tsv`, `world_sites_both.tsv` (every World-channel send site, both
builds); `igp_dispatch_589.c` (InGamePurchase S→C dispatcher). `re/out/world_channel_classes.tsv` named in the brief
did not exist; `world_sites_both.tsv` replaces it.

## 1. Summary

- The client sends **109 message classes on channel 1 (World)** that the send-site script resolves (0.589 and 0.695
  identical; world_sites_both.tsv; BasePacket counted once), plus SetLocale, PreviewOrder, PlaceOrder,
  PlaceOrderClientTicket, StoreClientStatistics, SubscriptionProductsRequest and the admin friend packets resolved by
  hand (L7, §3.6).
  Social ones (friends, guilds, ignore, chat membership, social profile) go to #12. The rest — commerce / Player
  Studio, ratings / contests, portraits, locale, trade invitations, encounters / lobbies / mini-game invites, logout to
  character select, the teleport queue, Steam and Lua telemetry — is this service.
- **Several features are split between World and Zone by the client itself**, which shows where live drew the line:
  - **Trade**: Initiate / InviteReply / Cancel on World, but OfferItem / CoinCount / Accept on Zone. A World back end
    brokered the invitation; the zone, which holds inventories, ran the exchange.
  - **Player Studio**: CommerceProductCreate (0x45/0x2F) goes on **Zone** (FUN_1431b7c20 / 0.695 FUN_143262c00,
    channel 0). In 0.695, a design listing is sent as the new TemplateProductCreate (0x30) carrying the design's template
    data (FUN_143262c00 → FUN_14310ad20 / FUN_143106990, channel 0). The zone validated the item and passed it on.
    Everything else about products (account store, review, search, follow, wallet) goes on World.
  - **Portraits**: PortraitDataRequest and AddClientPortraitCrc on World; GeneratePortraitRequest (appearance block)
    and SnapshotRequest on Zone.
  - **Site screenshots** (ModifyScreenshots 0x45/0x49) and StoreBundleContentRequest go on Zone.
- **0.695 Game Finder** (InstancedGameMatchmakingRequest 0x10D/0x19) goes on **Zone** (FUN_1434f2da0 / FUN_1434f2f90),
  but "Game finder should now work between worlds" (patch 14 Oct 2016). So it belongs to a **cross-world matchmaking
  component reached by the zones**, not to the per-world World-channel back end. BUILD-PATH Phase B1 already puts it in
  the shared social tier; this spec defines the zone ↔ matchmaking messages for whoever hosts it (§6.8, Hand-offs).
- **Proposed split** (two containers, one codebase):
  - `worldchannel` — **one per world**. It is the channel-1 endpoint of that world's gateway, and owns the state tied
    to the world's players and zones: trade broker, encounters, claim ratings, logout session, locale, teleport queue,
    telemetry sink.
  - `commerce` — **one for all worlds**. It holds what was account-wide or Daybreak-wide: Player Studio / Marketplace,
    wallet / account, the commerce session, contests, portraits, the Showcase web pages.

  Everything the World channel carries that no module owns yet is **forwarded unchanged to the player's zone**, so the
  move can happen one module at a time with no behaviour change.
- **When it is down** the client only logs it: the gateway's `OnChannelIsRoutable` just writes a NetInfo.log line
  (FUN_142d582a0), and no "store unavailable" text exists (searched all text keys; the only service-down texts are mail
  562281 / build site 562155 / video upload 56853). Requests on channel 1 simply go unanswered (§7).

## 2. Existing research used (coordinator list; nothing here was re-traced)

| File | What it already settled (used as is) |
|---|---|
| LandmarkEmu/docs/commerce.md | 0x45 wire (`u16 0x45, u16 sub`), S→C dispatcher FUN_1431adf70; Player Studio registration (0xFE00 + US / zip), ProductCreate 0x2F layout → 0x30 statuses, peer review 0x3F/0x40/0x42/0x43, account store 0x36/0x38, ProductUpdate / Remove, search 0x3D/0x3E, screenshots 0x49/0x45, IsTemplateValid 0x4C/0x4D, result mail kinds 0x10/0x20, taking a design from the Showcase. |
| LandmarkEmu/docs/showcase.md | The Showcase = CEF browser at ClientConfig MarketplaceUrl; client routes; store / gallery / Player Studio pages; token sign-in via ClientLua.RunCode; web API on :19001; portraits upload (AddClientPortraitCrc + multipart to [Portrait] UploadUrl). |
| LandmarkEmu/docs/encounters.md | EncounterBase 0x2C header and every sub both ways; the entry flow DetailsResponse → ZoneIsReady → RequestEntrance → PlayerEnter / State; 0x7D/0x7E death window; duels. |
| LandmarkEmu/docs/instanced-games.md | Site-game queues (Request type 5 / 4, MaxQueueJoinTime); the zone-side game model the Game Finder sits on. |
| LandmarkEmu/docs/vendors.md | Claim vendors (0x112) and Point-of-Need: zone features that the Showcase web pages also front ("My vendors"); PoN 0x46 type 1 = upkeep, free. |
| trace/families/0045_… | Every InGamePurchase class, sub, builder, reader (C→S 25, S→C 39). |
| 00D0, 0077 / 0078 | PacketClientInGamePurchase (Lua PurchaseBundleWithInGameCurrency, Zone); CommerceSessionRequest builder FUN_142ca25a0 (channel 1); CommerceSessionResponse reader FUN_142bb58f0 + handler (zone dispatcher case 0x35). |
| 003E_TradeInitiate.md | All trade subs both ways, builders and readers. |
| 002C, 008A, 0067, 0068 | Encounter requests; EncounterMatchmaking (1 C→S, 7 S→C, none sent); lobby requests (5 C→S, 2 S→C, none handled). |
| 00A7_Rating.md | Rating subs (AddVote 8, VoteReply 0x12, CanVote 0x18/0x19, contests 0x1D/0x1E, Create / DeleteContestReply). |
| 00C6, 00CA | Leaderboard / ProfileStats requests only (no S→C class): → L59. |
| 0075, 0098 | Portrait CRC; Fotomat subs 1-4 (Generate, DataRequest, PlayerImageData, Snapshot). |
| 005B | PacketSetLocale exists; the generated file marks it S→C (corrected in §3.6). |
| 00F6 / 00F7 | CharacterSelectSessionRequest builders FUN_142ca2540 / FUN_142cbead0 (channel 1, 60 s deadline); response reader FUN_142bb57e0, handler case 0x79 → run state 0x16 (ok) / 0x17. |
| 0104_SocialShare.md | SocialShare 0x104 (provider status / publish) — Zone channel, not World; listed under Hand-offs. |
| trace/FAMILIES.md | Family index and C→S / S→C counts used for the tables in §3. |
| trace/server-only-data.md | Nothing on commerce / ratings; confirms Locale is read from ClientConfig at start (FUN_142c78c20). |
| trace/managers.md | Client managers behind these families: ClientInGamePurchaseManager / StoreManager, ClientLeaderboardManager, FotomatPortraitDataManager / FotomatUpdateHandler, GameCommerce::CasApiHandler / CommerceProcessorHandler / MarketingDataHandler / UramApiHandler, LocaleDefinitionManager. |
| MODLOG.md | Logout → character select flow (2026-10-09); contests (10-11); claim rating (10-11); CanVote fix (10-12 s2cprobe); showcase / vendors / portraits / Player Studio create + review (10-12); unanswered InGamePurchase requests on purpose (protocol.md §14.1); relog keeps the item-definition cache (10-14). |
| trace/0695/coverage-695.md, packets-695.md, ui-trace-695.md, architecture-leads.md (L7 / L8) | 0.695 renumbering (InGamePurchase ≥0x30 +1, TemplateProductCreate 0x30 new), Game Finder layouts (§5a / 5b), L7 resolutions (SetLocale, PlaceOrder…, TemplateProductCreate on Zone), L8 id-only server messages. |

## 3. Client evidence (per family; 0.589 numbering, 0.695 differences noted)

Columns: send site = the function that calls the send template with the channel (0.589 / 0.695), from
re/out/agent_worldsvc/sites_*.tsv, with key sites checked by hand (fn.py). "Owner" = target of the split in §5:
**WC** per-world worldchannel, **COM** shared commerce, **Z** zone (forward), **#12** social.

### 3.1 InGamePurchase 0x45 (u16 sub; 0.695: subs ≥ 0x30 +1, new 0x30 TemplateProductCreate C→S)

| Sub | Class | Ch | Send site 589 / 695 | Client expects back | Owner | Emulator |
|---|---|---|---|---|---|---|
| 0x01 | PreviewOrder&lt;ClientInGamePurchaseOrder&gt; | W | FUN_1431b73c0 / FUN_1432623a0 (L7) | 0x02 PreviewOrderResponse | COM | not handled (real-money store) |
| 0x03 | PlaceOrder&lt;…&gt; / PlaceOrderClientTicket | W | FUN_1431b2170, FUN_143592650 / FUN_14325d150, FUN_143645430 | 0x04 PlaceOrderResponse | COM | not handled |
| – | StoreClientStatistics&lt;…&gt; | W | FUN_1431b6660 / FUN_143261640 | none | COM (sink) | not handled |
| 0x0B | WalletInfoRequest | W | FUN_1431c0c00 / FUN_14326bce0 (also Lua Browser.OnHide FUN_14352fab0) | 0x0C WalletInfoResponse (handler FUN_1431adaf0) | COM | answered InteractionHandler.cs:265-292 |
| 0x0D | ServerStatusRequest | W | FUN_1431c0310 / FUN_14326b3f0 (Lua RequestServerStatus) | 0x0E {bool} → Lua OnReceiveServerStatus (FUN_1431ab880); no UI consumer in 0.589 UI Lua | COM | unanswered |
| 0x0F | StationCashProductsRequest | W | FUN_1431c0480 / FUN_14326b560 | 0x10 | COM | unanswered on purpose (protocol.md §14.1) |
| 0x15 | CountryCodesRequest | W | FUN_1431c0090 / FUN_14326b170 | 0x16 | COM | unanswered |
| 0x17 | SubscriptionProductsRequest | W | FUN_1431c08a0 / FUN_14326b980 (L7) | 0x18 | COM | unanswered |
| 0x1A | AccountInfoRequest | W | FUN_1431bf9a0 / FUN_14326aa80 | 0x1B (FUN_1431a67b0) | COM | Commerce.cs:115-124 |
| 0x1C | StoreBundleContentRequest | **Z** | FUN_1431bfaa0, FUN_1431c05d0 / FUN_14326ab80, FUN_14326b6b0 | 0x1D | Z→COM | unanswered |
| 0x28 | LoyaltyInfoAndStoreRequest | W | FUN_1431b6440, FUN_1431c01a0 / FUN_143261420, FUN_14326b280 | store bundles | COM | unanswered |
| 0x2A | SpiceWebAuthUrlRequest | W | FUN_1431a3f40, FUN_1431b9080 / FUN_14324ee20, FUN_143264160 | 0x2B | COM | unanswered → L62 |
| 0x2F | CommerceProductCreate | **Z** | FUN_1431b7c20 (`FUN_14318a170(…,&local_418,0,1)`), FUN_1431ad850 (held create after IsTemplateValid) / FUN_143262c00, FUN_143258720 | 0x30 CreateResponse {u32 status, string} (FUN_1431a7430 → FUN_14319fc60) | Z validates → COM | Commerce.cs:172-246 (reads Template / Claims) |
| 0x30 (0.695) | CommerceTemplateProductCreate | **Z** | – / FUN_143262c00 (item type 2 = design: product + template copy FUN_143106990), FUN_143258720 | 0x31 (0.695) CreateResponse | Z → COM | not handled; collides with 0.589 0x30 (coverage §5.2) |
| 0x31 | CommerceProductRemove | W | FUN_1431b8620 / FUN_143263700 | none | COM | Commerce.cs:284-300 |
| 0x33 | CommerceProductUpdate | W | FUN_1431b7900, FUN_1431b7a60, FUN_1431c41a0 / FUN_1432628e0, FUN_143262a40, FUN_14326f280 | none | COM | Commerce.cs:307-338 |
| 0x34 / 0x35 | FollowCreator / UnFollowCreator | W | FUN_1431a2390, FUN_1431c55e0 / FUN_14324d270, FUN_1432706c0 | 0x39 AddFollowing / 0x3A RemoveFollowing | COM | unanswered |
| 0x36 | CommerceRequestAccountStore | W | FUN_1431a5080, FUN_1431a74e0 / FUN_14324ff60, FUN_1432523b0 | 0x38 AccountStore (FUN_1431a6b70) | COM | Commerce.cs:256-281 |
| 0x37 | CommerceRecommendations | W | FUN_1431a5ea0 / FUN_143250d70 | not found yet (no Recommendations reply class) | COM | unanswered |
| 0x3D | CommerceSearch (world map search) | W | FUN_1431c31e0 / FUN_14326e2c0 | 0x3E (FUN_1431a8470) — **sites of this landscape** | Z (claims data) | Commerce.cs:73-91 → Claims.Search |
| 0x3F | GetProductForReviewRequest | W | FUN_1431a5c30 / FUN_143250b00 | 0x40 (FUN_1431a7fc0) | COM | Commerce.cs:469-489 |
| 0x42 | SubmitProductReview | W | FUN_1431c53c0 / FUN_1432704a0 | 0x43 {bool} (FUN_1431a8620) | COM | Commerce.cs:545-590 |
| 0x46 | PointOfNeedRequest (type 1 = upkeep) | W | FUN_1431c0200, FUN_1431c0280 / FUN_14326b2e0, FUN_14326b360 | none for upkeep (docs/vendors.md) | Z (claims) | InteractionHandler.cs:268-272 → Claims.PayUpkeep |
| 0x49 | ModifyScreenshots | **Z** | FUN_143531e50, FUN_143531f40, FUN_143533660 / FUN_1435e3b70… | 0x45 (FUN_1431a9a70) | Z | SiteScreenshots.cs:60 |
| 0x4C | IsTemplateValidRequest | W | FUN_1431b7c20 (`FUN_143189fc0(…,1,1)`) / FUN_143262c00 | 0x4D (FUN_1431ad850) | Z (templates) | Commerce.cs:149-161 |

Related, not 0x45:
- **PacketCommerceSessionRequest 0x77** (W, FUN_142ca25a0 / FUN_142d3bac0; only caller ClientInGamePurchaseManager
  vtable 144666448 slot 15 FUN_143596940) → **0x78 CommerceSessionResponse** (reader FUN_142bb58f0: bool ok, then a
  string copied into Crypto::StringSecure&lt;256&gt;; handler FUN_143972530 adds form field "sessionID" to a pending browser
  request and POSTs it; failure logs "Failed to request commerce session."). Owner COM; emulator: not handled. → L62.
- **PacketClientInGamePurchase 0xD0** (Z, FUN_143538f10, Lua PurchaseBundleWithInGameCurrency) — in-game-currency
  bundle buy: zone (currency) → COM. Not handled.
- **ItemReview::PacketOptInAccount 0x10B/0x10** (W, FUN_143539b80) — sink. **WallOfDataMarketplacePacket** ×5
  (W, FUN_1435162f0 … FUN_143517400, Lua Marketplace* events), WallOfDataUIEvent / PlayerKeyboard / VoiceChatEvent (W) —
  telemetry sink (WC). → L63.
- **PacketPlayerStudioRegistered 0xFE** (S→C, reader FUN_142bb0fd0, GameEvents:OnPlayerStudioRegistered): sent at
  world entry today (PlayerManager.cs:95); account-level → COM, triggered by WC on "character entered".

### 3.2 Rating 0xA7 (u8 sub) — ratings and contests

| Sub | Class | Ch | Send site 589 / 695 | Back | Owner | Emulator |
|---|---|---|---|---|---|---|
| 8 | RatingPacketAddVote (Lua Claims.RateClaim FUN_1435329d0: sends twice, "Claims" then "Contests") | W | FUN_1435329d0 / FUN_1435e4870 | 0x12 VoteReply (FUN_142d02880) | "Claims" → WC; "Contests" → COM | Ratings.cs:28-82 |
| 0x18 | CanVoteRequest | W | FUN_142d1ff50, FUN_142d2ad00 / FUN_142dbecb0, FUN_142dc8ec0 | 0x19 CanVoteReply (FUN_142d024b0) | WC | Ratings.cs:90-125 |
| 0x1D | RequestContestsRequest (Lua RequestContests; repeats ~30× when unanswered, MODLOG 10-11) | W | FUN_143539c00 / FUN_1435ea0f0 | 0x1E RequestContestsReply (FUN_142d09110) | COM | Contests.cs:147 |
| – | ContestEntry submit = **Claims 0x41** SubmitContestEntry (Zone) → Claims 0x18 ContestEntryResults | Z | FUN_142d30a60 | 0x18 | Z → COM | Claims.cs:317 → Contests.Submit |

S→C 0x1B CreateContestReply / DeleteContestReply: admin replies, not sent.

### 3.3 Trade 0x3E (u8 sub) — invitation on World, exchange on Zone

| Sub | Class | Ch | Send site 589 / 695 | Owner |
|---|---|---|---|---|
| 1 | TradeInitiatePacket {u64 target} (Lua InitiateTradeByGuid; "TradeYourself" refused locally) | **W** | FUN_142cb1dc0 (`FUN_142bad1e0(…,1,1,0)`) / FUN_142d4b0c0 | WC |
| 3 | TradeInviteReplyPacket {u32 trade, bool} | **W** | FUN_14358fda0 (decline), FUN_14358ff50 / FUN_143642c70, FUN_143642e20 | WC |
| 7 | TradeCancelPacket {u32 trade} | **W** | FUN_14358fef0 (`FUN_14358d790(…,1,1,0)`) / FUN_143642dc0 | WC → Z |
| 6 | TradeAcceptPacket | **Z** | FUN_14358fe80 (`FUN_14358d5f0(…,0,1,0)`) / FUN_143642d50 | Z |
| 8 | TradeOfferItemPacket | **Z** | FUN_14358fd20 / FUN_143642bf0 | Z |
| 9 | TradeCoinCountPacket | **Z** | FUN_143590400 / FUN_1436431e0 | Z |

S→C 2 Invite, 0xF Rejected (after the invite step) come naturally from WC; 4 StartSession, 5 EndSession, 0xA-0xE
from whichever side made the change (the client reads them all in FUN_14358ee50 regardless of channel; channels.md:
incoming channel only matters for 2). Wiki (Trading): direct trade "while they are together in the same location";
text 577061 "That item was recently purchased and may not be traded yet" (Marketplace items).

### 3.4 Encounters 0x2C, encounter matchmaking 0x8A, lobbies 0x67 / 0x68, mini-game invites 0xAD, quizzes 0xAF, requisitions 0x6E

| Class | Ch | Send site 589 / 695 | Owner | Emulator |
|---|---|---|---|---|
| EncounterInvitationResponse 0x67 | W | FUN_143350240 (+ auto in Zone_DispatchPacket) / FUN_1433fc300 | WC | Encounters.cs:192 |
| EncounterDuelInvitationResponse 0x69 | W | FUN_1433501d0 / FUN_1433fc290 | WC | Encounters.cs:201 |
| EncounterParticipantRequestEntrance 0x6C | W | FUN_142cbe6e0 / FUN_142d57a70 | WC | Encounters.cs:180 |
| EncounterParticipantRequestExit 0x6D | W | FUN_142ca26b0 / FUN_142d3bbd0 | WC | Encounters.cs:184 |
| EncounterParticipantCancelPending 0x7C | W | FUN_142c0cfb0 / FUN_142ca6d90 | WC | Encounters.cs:187 |
| EncounterMatchmaking::MatchInvitationResponse 0x8A/0xA | W | FUN_143350a40 / FUN_1433fcb00 | WC (cross-world: §6.8) | not handled → L63 |
| Create / Join / Leave / Start LobbyGame, LeaveLobby 0x67/5,1,2,3,9 | W | FUN_14335c9a0, FUN_14335ced0, FUN_14335d050, FUN_14335d730, FUN_14335cf40 / FUN_143408a60 … | WC | not handled; no UI caller found → L63 |
| PacketInviteAndStartMiniGame Invite 0xAD/1, StartGame /3 | W | FUN_1433509a0, Zone_DispatchPacket / FUN_1433fca60, FUN_142d1ccf0 | WC | not handled → L63 |
| QuizRequest 0xAF/1, QuizResult /3 | W | FUN_142ece330, FUN_142ece2d0 | WC | not handled → L63 |
| GetAllAvailableRequisitionItems 0x6E/1, RequisitionItemsRequest /3 | W (/5 on Z) | FUN_14312b8b0, FUN_143127090… | WC | not handled → L63 |
| CommandPacketRequestPlayIntroEncounter | **Z** | FUN_1435139d0 | Z | – |

The S→C side of encounters (Invitation 0x66, ZoneIsReady 0x6B, PlayerEnter 2, State 0x6A, Details 0x72,
Terminate 0x79, Shutdown 6) is lifecycle → WC; the death window 0x7D / 0x7E is combat → Z (Health.cs).

### 3.5 Portraits (Fotomat 0x98, i16 sub) and 0x75

| Class | Ch | Send site 589 / 695 | Back | Owner | Emulator |
|---|---|---|---|---|---|
| PacketPortraitDataRequest 0x98/2 {u64 char, "Headshot"/"Portrait"} | W | FUN_142ebee10 / FUN_142f5da40 | 0x98/3 PlayerImageData (FUN_142ebab00) | COM | Portraits.cs:51-63 |
| PacketAddClientPortraitCrc 0x75 {u32} | W | FUN_143147490 / FUN_1431f1eb0 | none (then HTTP upload) | COM | Portraits.cs:66-71, WebPages.cs:211-244 |
| PacketGeneratePortraitRequest 0x98/1 (appearance block; Lua RequestPortrait) | **Z** | FUN_142ebf000, FUN_143143170 / FUN_142f5dc30, FUN_1431edb90 | rendered PlayerImageData | Z → COM | logged only |
| PacketSnapshotRequest 0x98/4 | **Z** | FUN_142ca27e0 / FUN_142d3bd00 | – | Z | not handled |

### 3.6 Session-level messages

| Class | Ch | Evidence | Back | Owner | Emulator |
|---|---|---|---|---|---|
| **PacketSetLocale 0x5B** (C→S; ctor FUN_142bcc450: `StringFixed<8>` locale) | W | sent once per world entry in the run loop FUN_142c3b490 (`FUN_142baaf90(…,uVar17,1,1)`, then ClientInitializationDetails 0xA2 on channel 0) / FUN_142cd4e60 (L7). The only use of the vtable is the ctor (decomp_all part_0060:16955), so **no S→C SetLocale exists**: trace/families/005B and coverage-695 "S→C, FUN_142be2e00" are wrong (FUN_142be2e00 is its destructor). | none | WC (keeps it per connection) | MiscHandler.cs:135 (ignored) |
| PacketCharacterSelectSessionRequest 0xF6 (Lua Ui.Logout FUN_143514040) | W | FUN_142ca2540, FUN_142cbead0 (sets +0x324a0 = now + 60 s) / FUN_142d3ba60, FUN_142d57e60 | 0xF7 {bool, string session} (FUN_142bb57e0; case 0x79: ok → state 0x16 → ClientLogout 0x14 on Zone, else 0x17) | WC + login service | CharacterSelectHandler.cs:36-57 (AuthDatabase.UpdateAccountSessionKey, ItemDefinitionManager.NoteRelogin) |
| PacketTeleportToQueuedLocation 0xFC/6 {u32, u32} | W | FUN_1430c8930; FUN_143350e00 (GAQ accept, GaqItemTeleportToZoneRequest vtable 14469e3b8 slot FUN_14332d5b0) / FUN_143173a90, FUN_1433fcec0 | – | WC | not handled (MiscHandler.cs:120 → Travel) → L60 |
| PacketTeleportCancelQueue 0xFC/7 (Lua Teleport.CancelQueue; Dialog.lua:69; GAQ decline FUN_14332f7b0) | W | FUN_1430c6430 / FUN_143171570 | – | WC | not handled |
| (S→C) PacketTeleportPostQueueNotify 0xFC/5 (u8, u32 × 4) | – | reader FUN_1430c2960, handler FUN_1430c7580 → OnPlayerTeleportPostQueueNotify | – | WC | not sent → L60 |
| Other 0xFC subs (RequestLocations, Request, IntraZone, RandomUnderground, NextUnderground, PlayEffect, Managed) | **Z** | sites_589.tsv | – | Z | Travel.cs |
| PlayerUpdatePacketUpdateSteamInfo | W | FUN_142f3c790 (sends when SteamUser()'s id ≠ +0x100) / FUN_142fdc500 | none | WC (sink; account link) | not handled |
| PacketClientLuaMetrics 0x8B | W | FUN_142cc0b10 / FUN_142d59ea0 | none | WC (sink) | not handled |
| ProfileStatsPacketGetLeaderboard 0xCA/6, GetPlayerLeaderboardRow /8 | W | FUN_143257af0, FUN_143257b80 / FUN_1433049a0, FUN_143304a30 | reply ids 7 / 9 have no client class | COM (sink) | not handled → L59 |

### 3.7 0.695 Game Finder (zone channel, cross-world)

InstancedGameMatchmakingRequest 0x10D/0x19 (start FUN_1434f2da0 / stop FUN_1434f2f90, **channel 0**; body u64 flag mask,
u64 player, u8 searching, u8) → InstancedGameMatchmakingUpdate 0x10D/0x1A (u64, u32 Players, ActiveGames, QueuedGames,
InactiveGames, u32 SearchState, u64 flags; handler FUN_1434e9a50) (packets-695.md §5a-b). The counts are a view
of all games the matchmaker knows about, and since 14 Oct 2016 that covers every world. How a found match moves the
player is still open (L31).

### 3.8 World-channel classes not in this topic (for completeness)

#12 social: CommandPacket AddFriend / AddSteamFriend / ConfirmFriend / RemoveFriend / FriendsPosition / Ignore /
ChatChannelOn / Off / AFK / NameChange / NameValidation / RequestPlayerPositions, FriendUpdateExtraInfos, Guild* (18),
PlayerPublicChatJoinRoomRequest, VoiceRequestCustomChannel, Groups::GroupPacketMapPing, AdminCommandPacketFriendsListRequest
/ ForceAdd / RemoveFriend (FUN_1432f9070 / FUN_1433a5e60), and **SocialPacket* / Social*Wall* (14 classes: profile,
likes, wall, challenges, reputation, diplomacy, house rating / furniture score, game top scores)** — not in #12's listed
scope either (see Hand-offs).
Unassigned, recommended **forward to zone** from WC (the emulator handles them in the world process today):
CommandPacketGoHome (FUN_142c0f750), CommandPacketRendererDump (FUN_1432cf3f0), CommandPacketRequestRewardPreviewUpdate
(FUN_142cb1320), AdminCommandPacketGameTime (FUN_143306f10, also sent on Zone), ActivityPacketActivityListRefreshRequest
(also on Zone), generic BasePacket sites, and the 0.589 FUN_142d98fa0 / 0.695 FUN_142e36f70 packet sent on World or
Zone by argument (L7, unresolved).

## 4. Emulator today (one world process; everything below runs there)

| Module | Where | Data | Zone state it touches (and how the split service would reach it) |
|---|---|---|---|
| Commerce / Player Studio | Game/Commerce.cs (handlers :111-166); InteractionHandler.cs:265-292 (wallet, PoN → Claims.PayUpkeep :268-272) | player_studio_product, player_studio_review (CharacterContext.cs:25-26, :852-887; per-world character DB) | Template table (owner check :215-216, designs :410, copy :436-450) → **zone** validates on ProductCreate (it arrives on Zone anyway) and COM keeps a template snapshot; Claims.Get / OwnerOf (:215) → zone; Claims.Search (:80) → zone answers 0x3D; Templates.FindDesign (:153) → zone answers 0x4C; MarketChanged → Templates.SendUpdated (:395-396) and Templates.cs:281 calls Commerce.IsOnMarketplace synchronously → COM pushes **CommerceMarketplaceStatus** and the zone caches it; Claims IsOnGallery → **CommerceGalleryStatus**; Decide → Mail.SendSystemMail (:596) → #12 mail; TakeDesign (:422-460: Unlocks kind 16, template copy, Templates.SendAdded / SendLearned, chat to maker) → **CommerceGrantDesign** to the zone. |
| Site screenshots | Game/SiteScreenshots.cs:60 (via Commerce.Handle :146) | files | stays in zone (0x49 is a Zone message). |
| Ratings | Game/Ratings.cs:28-125; MiscHandler.cs:144-158 | character_unlock kind 11 ClaimVote (Unlocks.cs:31) | Claims.OwnerOf / Get (:55, :61, :109) → **ClaimInfoQuery** to the zone; DailyGoals.LikeGiven, Achievements.LikeGivenOrReceived / LikedTaggedSite (:59-63) → **RatingVoteCounted** event to the zone; Ratings.Total used by Store.cs:251 (gallery). **Moved 2026-10-16 (step 6)**: LandmarkEmulator.WorldChannel/RatingBroker.cs + VoteStore.cs; the zone keeps kind 11 as a read copy (§6.5). |
| Contests | Game/Contests.cs (Vote :84, SendList :147, Submit :172) | Contests/contests.json, entries.json, shots/ (files next to the server) | Submit is reached from Claims.cs:317 (Claims 0x41, Zone) → zone checks the site and forwards **ContestEntrySubmit**; votes need the claim's contest entries (COM-owned). |
| Trades | Game/Trades.cs; InteractionHandler.cs:194 | memory only | Initiate needs Ignores.Refuses (:88) → #12, same landscape (:92) → presence from gateway connection events (zone id); Offer / Coins / Complete use Inventory, coins, DesignItems (:136, :173, :216-270) → stay in **zone**; Forget / Moved (PlayerManager.cs:188, :229; InstancedGames.cs:601) → zone sends **TradeZoneSessionEnd** to WC. |
| Encounters / duels | Game/Encounters.cs; InteractionHandler.cs:163; HostedService.cs:146 (Update tick) | memory only | Challenge distance / same zone (:313-322), Hit / Duelling (:362-383), Update range check (:409-419), death window (:76; Health.cs) → stay in **zone**; lifecycle (invite, details, zone ready, enter, exit, terminate) → WC with **EncounterParticipants** pushed to the zone. Emotes.cs:137, PlayerManager.cs:252 call Leave. |
| Instanced-game queue | Game/InstancedGames.Queue.cs | memory | zone (per-site queue). The 0.695 Game Finder sits on top of it; its cross-world counts are in the social service (§6.8, built 2026-10-16). |
| Portraits | Game/Portraits.cs; MiscHandler.cs:128-133; WebPages.cs:211-244 (upload) | Portraits/<char>_portrait.png / _headshot.png | none (files + CRC map) → COM entirely; GeneratePortraitRequest stays a zone message (logged). |
| Showcase web | Game/WebPages.cs (:29-47 routes, :308-424 API), Game/Store.cs, Game/Admin.cs | port 19001; tokens in memory | Store.Take (bag), Store.Visit (travel), Store.Gallery (claims, Ratings.Total, online), vendors, admin page, template images (Templates.Image :286) — all **zone** → COM's web front calls the player's zone through WC (**WebZoneRequest**), or the per-world parts (gallery, vendors, admin, take, visit) stay on a per-world web endpoint (decision in §6.6). |
| Logout to character select | Network/Handlers/CharacterSelectHandler.cs:36-57 | auth DB session key | AuthDatabase (login service's table) → **ReloginSessionRequest** to the login service; ItemDefinitionManager.NoteRelogin (relog keeps the item-definition cache, MODLOG 10-14) → **NoteRelogin** to the zone. |
| SetLocale, metrics, PC data | MiscHandler.cs:124-135 (empty handlers) | – | none. |
| Teleport (Travel window) | Game/Travel.cs:27-41 (Zone subs) | – | zone; ToQueuedLocation / CancelQueue unhandled (MiscHandler.cs:120). |
| PlayerStudioRegistered | Game/PlayerManager.cs:95 | – | sent at entry; moves to WC on "character entered". |

## 5. Service boundaries

### 5.1 Per world vs shared (evidence)

| Area | Scope | Evidence |
|---|---|---|
| Trade invitations | **per world** (and same landscape) | Trades need both players together (wiki Trading; Trades.cs:92); client sends on the world's gateway. |
| Encounters / duels, lobbies, mini-game invites | per world | participants are in this world's zones; lifecycle messages on World, combat on Zone. |
| Claim ratings ("Claims" votes), CanVote | per world | builds differ per world (wiki Servers and Landscapes); the vote item is a claim id of this world. |
| Teleport queue | per world | queues entry into this world's zones (inference; L60). |
| Logout to character select | per world endpoint, asks the shared login service | session key lives in the login service (auth DB). |
| Locale, Steam id, Lua metrics, WallOfData | per world (sink) | per connection. |
| Player Studio / Marketplace, wallet, account info, commerce session, Spice auth | **shared (all worlds)** | Player Studio is "part of the overall Marketplace" (design/forum/all/2014-03-26-26034-the-blueprint-march-26-april-29.md), sold for Daybreak Cash / real money (wiki Player Studio, Showcase); commerce text "There was an error with your marketplace transaction … you have been charged" (562931); client classes GameCommerce::CasApiHandler / UramApiHandler (Daybreak platform APIs). |
| Contests | **shared** | one Competition tab for everyone (wiki Showcase); players asked for contest entries to "show servers and isles" (design/forum/patches/2014-05-02-34889-hotfix-notes-05-02-2014.md): entries came from several worlds. Weak evidence, consistent with the Showcase being one web site. |
| Portraits | **shared** | an external rendering service (Fotomat; upload to a ClientConfig URL, docs/showcase.md). |
| Game Finder (0.695) | **shared** | patch 14 Oct 2016 "Game finder should now work between worlds" (patchnotes P6551). |
| Leaderboards | shared (sink until L59) | ProfileStats ids only. |

### 5.2 Containers

- **`worldchannel-<world>`** (new project LandmarkEmulator.WorldChannel): channel-1 endpoint of that world's gateway.
  Owns: trade broker, encounters registry, lobbies / mini-game / quiz / requisition / match-invitation sinks, claim
  votes, logout session, locale, teleport queue, telemetry sink. Relays commerce families to `commerce` and forwards
  everything else (including 0x45 subs 0x3D, 0x46, 0x4C) to the player's zone.
- **`commerce`** (one per installation; same project, role `commerce`, or its own project): Player Studio, wallet /
  account / store requests, commerce session, contests, portraits, leaderboards sink, PlayerStudioRegistered, and the
  Showcase web site (port 19001).
- **zone (world server today)** keeps: inventory-side trade, ProductCreate validation (it arrives on Zone), 0x3D / 0x4C /
  0x46 answers, screenshots, duel combat, death window, site-game queues, Game Finder forwarding.

## 6. Spec

### 6.1 Data ownership

| Owner | Store | Tables / files |
|---|---|---|
| worldchannel (per world) | schema `lmemu_wc_<world>` (MariaDB, same server) | `claim_vote` (voter char, claim, at) — moved from character_unlock kind 11; `session_locale` not stored (memory). Trades, encounters, queues: memory only (live lost them on restart too: trade ends with "TradeCanceled"). Built 2026-10-16 (step 6): `claim_vote` (voter_guid, claim_id, voted_at; key voter + claim, index claim). **Deviations**: no EF / migrations project like LandmarkEmulator.Database.Social for one table — VoteStore.cs creates the schema and table with `CREATE ... IF NOT EXISTS` and uses MySqlConnector directly (grant `lmemu\_wc\_%` in docker/db-init); the zone keeps writing kind 11 for every counted like as a **read copy** (Ratings.Total for the Showcase gallery, and the §7 fallback, which must refuse a second like of the same site). |
| commerce (shared) | schema `lmemu_commerce` | `product` (id u32-safe: the client's BundleId must fit u32 and ≠ 0, Commerce.cs:498; world id, owner world + char guid + account, item type, template id in its world, **template snapshot** (name, size, materials, blocks, images) so other worlds can review / take it, name, description, headline, tags, price, status, approvals, rejections, created), `product_review` (product, reviewer world + char, verdict, at), `following` (account, creator), `contest`, `contest_entry` (from Contests/*.json; screenshots in the `commerce-data` volume), `portrait` (files in the volume, keyed world + char), `taken_design` (product, taker world + char) — the zone still records Unlocks kind 16 for its own checks. **Built 2026-10-16 (step 8, untested)**: LandmarkEmulator.CommerceServer/CommerceStore.cs (MySqlConnector + CREATE IF NOT EXISTS, as VoteStore): `commerce_world` (world, **realm**, name text, seen — added), `product` (+ owner name, voxels, mail_pending; snapshot columns snap_*; images not in the snapshot: a copy taken in another realm has none, L124), `product_review`, `taken_design`, `contest`, `contest_entry`, `contest_vote` (added: one row per vote instead of the JSON voter list), `portrait`, `commerce_import` (added: the one-time imports done). `following` **not created** (Follow / UnFollowCreator stay unanswered as in-process; L123). **Realm** (deviation, recorded): the Docker worlds share one character database (lmemu_character), so character guids and template ids are *not* per world as assumed above; every row keeps (world, guid) and each world names its character database in CommerceHello, and a player's own rows match on (realm of the row's world, guid); claims stay per world. |
| zone (unchanged) | per-world character DB | templates, claims, inventory, unlocks (kinds 11 read-only until migrated, 16), mail stays with #12. |

Character guids are per world, so every shared key is (world id, character guid).

### 6.2 Transport (internal-transport.md)

- gateway → WC: `TunnelPacketFromExternalConnection` for channel 1 (0.589 numbering after ClientWire695); connection
  events (entered / left, with zone id, name, client build) give WC its presence table.
- WC → gateway: `TunnelPacketToExternalConnection`, and `…List` 0x16 for fan-out (encounter / queue updates).
- WC → zone: the same `TunnelPacketFromExternalConnection` frame (channel byte = 1) for forwarded messages, so the zone
  handlers need no change; plus `ServicePacket` messages below.
- WC ↔ commerce, zone ↔ commerce (through WC), WC ↔ login: `ServicePacket` (u16 family, u32 sub, body).
- **Ours**: messages without a client name use the client family and a sub ≥ 0x10000 (u32 sub field, so they can never
  clash with a client u8 / u16 sub).

### 6.3 Commerce relay (WC ↔ commerce)

| Name | Dir | Family / sub | Body (ours) |
|---|---|---|---|
| CommerceClientRequest | WC → COM | 0x45 / client sub (also 0x77, 0x98, 0x75, 0xA7 "Contests", 0xCA, 0x10B, 0xFE) | u64 connection, u64 character, u64 account, u32 world, u16 client build, string locale, then the client body as received |
| CommerceClientReply | COM → WC | 0x45 / 0x10000 | u64 connection, bytes = one client packet (0.589 form; the gateway translates for 0.695) |
| CommerceClientReplyList | COM → WC | 0x45 / 0x10001 | u32 n × u64 connection, bytes (→ gateway 0x16) |
| CommerceProductCreate | zone → COM | 0x45 / 0x2F (client name) | CommerceClientRequest header + client body + u32 item type (2 design, 3 claim) + u32 owner check result + template snapshot (design) / claim summary (gallery) |
| CommerceTemplateProductCreate | zone → COM | 0x45 / internal id of 0.695 0x30 (coverage §5.2) | same, from the 0.695 body (product + template data) |
| CommerceMarketplaceStatus | COM → zone (via WC of that world) | 0x45 / 0x10010 | u64 template, u64 owner char, bool on market (replaces Commerce.IsOnMarketplace; the zone sends TemplateBasicData update) |
| CommerceGalleryStatus | COM → zone | 0x45 / 0x10011 | u64 claim, bool listed |
| CommerceGrantDesign / Reply | COM → zone → COM | 0x45 / 0x10012 / 0x10013 | request: u64 taker char, u32 product, template snapshot, string maker; reply: bool ok, string message (the Showcase shows it) |
| CommerceMarketSnapshot | zone → COM at start | 0x45 / 0x10014 | the zone asks for every live product of its world (rebuilds its caches after a restart) |
| SystemMail (to #12) | COM → social | #12's message | owner world + char, kind 0x10 / 0x20, subject = product name (Commerce.cs:596) |
| ContestEntrySubmit / Result | zone → COM → zone | 0xF9 / 0x10020, 0x10021 | world, site, owner, contest key, screenshots; result bool + names → zone sends Claims 0x18 ContestEntryResults |
| ContestVote | WC → COM | 0xA7 / 8 (client name) | relayed AddVote "Contests" + world + claim owner (from ClaimInfoQuery) |
| PortraitUploadAnnounce | — | 0x75 relayed | as CommerceClientRequest |

**Built 2026-10-16 (step 8; agent; untested)** — LandmarkEmulator.Internal/Commerce/CommerceMessages.cs, service
LandmarkEmulator.CommerceServer (CommerceHub, ProductService, ContestService, PortraitService, CommerceStore), zone side
Game/WorldChannel/RemoteCommerceService.cs behind COMMERCE_ADDRESS (off by default; LocalCommerceService unchanged).
**Deviations (ours, recorded):**
- **The zone, not WC, is commerce's client** (as for `social`): the zone already gets every channel-1 commerce request
  (WC hands them on; while WC is away the gateway sends channel 1 to the zone), holds what must be checked first (owner of
  the design / site, the site's three tags, the contest-entry limiter) and the session's account / build, and keeps the
  caches it reads itself. So CommerceClientRequest runs zone → COM; the header is u64 connection, u64 character, u64
  account, u32 world, **string** client build, **string character name** (no locale: SetLocale ends in WC). Replies to a
  player are TunnelPacketToExternalConnection on the zone's link (0.589 numbering; the session translates for 0.695);
  CommerceClientReply 0x10000 / …List 0x10001 are not used. ContestVote is the client's AddVote "Contests" handed on by
  the zone (RatingBroker already passes that category to the zone) with body {string item id, u64 site}.
- Ids added: CommerceHello 0x45/0x10002 (zone → COM: world, realm, name text, which local files exist; answered with
  CommerceMarketSnapshot 0x10014, which therefore runs COM → zone), StudioListRequest / Reply 0x10015 / 0x10016,
  DesignTaken 0x10017, Notice 0x10018, ResultMail 0x10019 (COM → zone; the zone mails through its own social seam, so COM
  needs no link to `social`) / ResultMailSent 0x1001A, AdminRequest / Reply 0x1001B / 0x1001C (psreview, contestphase,
  contests), ImportRequest / File / Done 0x1001D-0x1001F (the worlds' Contests/ and Portraits/ files), PortraitUpload /
  Reply 0x98/0x10030 / 0x10031. CommerceGrantDesign runs **zone → COM → zone** (Request 0x10012 / Reply 0x10013 with the
  snapshot), then DesignTaken: the Showcase pages stay on the world (§6.6 option). CommerceGalleryStatus carries the
  listing time, contest markers and contest names besides "listed". ContestEntrySubmit 0xF9/0x10020 carries the zone's
  checks (allowed, own site, site name) and COM answers ContestEntryResults 0xF9/0x18 itself (no 0x10021).
- WebSignIn / WebZoneRequest (§6.5 / §6.6) not built: the web pages, tokens and their zone actions stay on the world.
- PlayerStudioRegistered 0xFE stays a zone packet at entry (the same {1} for every account; a commerce outage at login
  doesn't change the client's state).
0.695 check (rule 2026-10-15; coverage-695.md): 0x45 "renumbered" (≥0x30 +1, bodies same, 0x30 TemplateProductCreate
new) — **found while checking: Commerce.FromWire / ToWire (1cb2bbd) shifted the subs a second time on top of the session
boundary (ClientWire695), e.g. 0.695 RequestAccountStore 0x37 → 0x36 → 0x35 UnFollowCreator; fixed: the zone sees
0.589 numbers, only the internal 0x1030 maps to 0x2F**; 0xA7 8 / 0x12 / 0x1D / 0x1E, 0xF9 0x41 / 0x18, 0x98 1-4, 0x75,
0xFE "same". Also found: PlayerImageData's i16 sub went out as 4 bytes (GamePacketWriter has no Write(short)); reader
FUN_142ebab00 reads u16 opcode, i16 sub, u64 — fixed in Portraits.Build and the service.

### 6.4 Trade broker (WC ↔ zone)

1. TradeInitiate (W) → WC: target online (presence), same zone id, not ignored (#12 **IgnoreCheck** query, or the
   ignore list cached from #12 events), neither in a trade → **TradeZoneSessionStart** to the zone (0x3E / 0x10030:
   u32 trade id, u64 conn A, u64 char A, u64 conn B, u64 char B), then TradeInvite 0x3E/2 to B.
2. InviteReply (W) → WC: decline → TradeRejected 0x3E/0xF to A and **TradeZoneSessionEnd** (0x3E / 0x10031: u32 trade,
   u8 TradeMessage code); accept → zone **TradeZoneSessionOpen** (0x3E / 0x10032: u32 trade) **before** WC sends
   StartSession 0x3E/4 to both, so an OfferItem on Zone never reaches a zone that doesn't know the trade.
3. Offer / Coins / Accept (Z): unchanged zone code (Trades.cs:129-290); the zone sends UpdateAddItem / CoinCount /
   Accept / Message / EndSession itself.
4. Cancel (W) → WC → TradeZoneSessionEnd (code 1) → the zone ends it and sends EndSession.
5. The zone ends a trade (completed 6, landscape change 1, instance 8 / 9, disconnect 2: Trades.cs:297-340) →
   TradeZoneSessionEnd to WC (frees the pair).

### 6.5 Other WC ↔ zone messages

| Name | Dir | Family / sub | Body |
|---|---|---|---|
| ClaimInfoQuery / Reply | WC → zone → WC | 0xF9 / 0x10040, 0x10041 | u64 claim → u64 claim, u64 owner, u32 zone, string tags, bool exists. Built (step 6): u32 request id first in both, and the reply ends with **bool listed** (in the gallery; the like rule P5630, Commerce.IsOnGallery in the zone until commerce owns it) |
| RatingVoteCounted | WC → zone | 0xA7 / 0x10042 | u64 voter, u64 claim, u64 owner (DailyGoals / Achievements, Ratings.cs:59-63) — the zone also writes its kind-11 read copy |
| RatingVoteRefused (added, step 6) | WC → zone | 0xA7 / 0x10043 | u64 voter, u64 claim, u8 reason 1 own site / 2 not in the gallery (or no such site) / 3 already rated — the zone prints the in-process chat line (Chat.System), as EncounterNotice does for duels |
| RatingClientRequest (added, step 6) | zone → WC | 0xA7 / 0x10044 | u64 connection, u64 character, a whole client RatingBase packet (A7 00, sub, body) — the `ratevote` dev command while a WC is linked; WC serves it as that connection's channel-1 packet (broker off: back to the zone as a tunnelled packet) |

Built 2026-10-16 (step 6; LandmarkEmulator.Internal/WorldChannel/RatingMessages.cs, LandmarkEmulator.WorldChannel/
RatingBroker.cs + VoteStore.cs): ChannelRelay hands 0xA7 AddVote with category "Claims" and CanVoteRequest 0x18 to the
broker; it asks ClaimInfoQuery, decides with Ratings.cs's rules (not your own site, listed in the gallery, one like per
player per site) against `claim_vote` (INSERT IGNORE, so a double click counts once), and sends the client the same
VoteReply 0x12 / CanVoteReply 0x19 bytes Ratings.cs wrote (Zone destination, 0.589 numbering; one reply to the requester,
no broadcast), then RatingVoteCounted or RatingVoteRefused to the zone. AddVote "Contests", RequestContestsRequest 0x1D
and every other Rating sub go on to the zone (contests stay with the in-process commerce code until the `commerce`
container exists, §8 step 8; ContestVote WC → COM of §6.3 is not built). A CanVoteRequest without a numeric site is
answered "may not" at once without asking the zone (as Ratings.cs). Store calls run on one worker thread; requests whose
ClaimInfoReply never comes are dropped after 30 s or when the zone link goes. 0.695 check (rule 2026-10-15): AddVote 8,
VoteReply 0x12, CanVoteRequest 0x18, CanVoteReply 0x19 are all "same" (coverage-695.md §0xA7 rows; gallery-695.md).
| EncounterParticipants | WC → zone | 0x2C / 0x10050 | u32 encounter, string kind, u32 state, n × u64 char (duel rules, Encounters.Duelling) |
| EncounterCreateDuel | zone → WC | 0x2C / 0x10051 | u64 challenger, u64 target (the /duel zone command checked range, Encounters.cs:313-322) |
| EncounterForfeit | zone → WC | 0x2C / 0x10052 | u32 encounter, u64 char, u8 reason (range / death / landscape change) |
| EncounterDuelAnswer (added, step 7) | zone → WC | 0x2C / 0x10053 | u64 char, u64 inviter (0 = any), u8 accept — /duelaccept, /dueldecline are zone commands; the client's own 0x69 reaches WC directly |
| EncounterNotice (added, step 7) | WC → zone | 0x2C / 0x10054 | u64 char, string text — the duel chat lines, printed by the zone (Chat.System); 0.589 has no duel UI text of its own |

Built 2026-10-16 (step 7; LandmarkEmulator.Internal/WorldChannel/EncounterMessages.cs): EncounterParticipants carries
`u32 n` before the characters and a state 0 ended / 1 open / 2 running (2 = every participant entered, the duel rules
apply); EncounterCreateDuel carries, after the spec's two guids, the two connections and names (WC has no presence table
yet, the same gap TradeZoneSessionReady fills); EncounterForfeit reasons 1 left / 2 defeated / 3 range / 4 logout /
5 landscape / 6 declined, encounter 0 with logout = drop the player's open challenge. WC ids start at 0x40000001.
| NoteRelogin | WC → zone | 0xF6 / 0x10060 | u64 account, string session key (ItemDefinitionManager.NoteRelogin) |
| TeleportQueueGrant | WC → zone | 0xFC / 0x10070 | u64 char, u32 zone, u32 location (after ToQueuedLocation; design waits for L60) |
| WebZoneRequest / Reply | COM → WC → zone | 0x45 / 0x10080, 0x10081 | u64 char, string verb (take / visit / gallery / vendor / admin), string JSON args → bool, string JSON |

### 6.6 Showcase web pages

Live served them from Daybreak's store servers (docs/showcase.md), so they belong to `commerce`. The site needs the
signed-in player (the token from Store.SignIn) and acts in that player's zone. COM keeps the token table: WC sends
**WebSignIn** (0x45 / 0x10090: u64 conn, u64 char, u32 world, token) at world entry, and the Lua `ShowURL` push stays a
zone action. Take / Visit / vendors / admin go through WebZoneRequest; the gallery and Player Studio list are COM data
plus a per-world claim list (CommerceGalleryStatus + ClaimInfoQuery). Option kept open: a per-world web endpoint for the
admin page only.

### 6.7 Login service

**ReloginSessionRequest / Reply** (ours, Login service; 0xF6 / 0x10061, 0x10062): u64 connection, u64 account →
u64 connection, bool ok, string session key. WC then sends 0xF7 {ok, key} and NoteRelogin to the zone. After 0xF7 the
client sends ClientLogout 0x14 on Zone and waits ≤ 2 s for the **server to close the connection** (MODLOG 10-09) — the
gateway closes it (Hand-off #13).

### 6.8 Game Finder (0.695) — where it belongs

- Request and update travel on the **zone** channel. So the zone that receives InstancedGameMatchmakingRequest forwards
  it to a **shared matchmaking component**: **MatchmakingRequest** (0x10D / 0x10100: u32 world, u64 char, u64 flag
  mask, bool searching). Each zone publishes its site games: **MatchmakingGames** (0x10D / 0x10101: u32 world, n ×
  {u64 game, u32 zone, u64 flags (FFA 2, Team 4, PvE 8, PvP 0x10, in progress 0x40), u32 players, u32 state}).
  The component answers **MatchmakingUpdate** (counts + SearchState + flags) through the player's world (zone or WC →
  gateway) as client 0x10D/0x1A.
- Hosting: one instance for all worlds, in the shared tier — BUILD-PATH B1 (shared social service), or `commerce` if
  #12 prefers. The 0.589 EncounterMatchmaking 0x8A replies (ListQueues, AddMatchRequest, MatchInvitation, QueueStats)
  belong to the same component; there is no 0.589 UI caller (L63).
- Moving a matched player, especially to another world, is still L31; don't build that step until it is traced.
- **Built 2026-10-16 (agent; untested in the client)** in the shared social service (coordinator's choice of host;
  social-service.md §9): LandmarkEmulator.SocialServer/MatchmakingService.cs, messages
  LandmarkEmulator.Internal/Social/MatchmakingMessages.cs, zone side InstancedGames.Finder.cs + Game/Social
  (IMatchmakingService, RemoteSocialService). Ids as above plus **MatchmakingUpdate** service → zone 0x10D / 0x10102
  (ours): u64 char, u32 players, u32 active, u32 waiting, u32 inactive, u32 SearchState, u64 flags, u32 otherWorld,
  u64 otherGame. The zone, not the service, writes the client's 0x10D / 0x1A, with the writer the local Game Finder
  uses (SendMatchmaking): the 0.695 sub numbering is the zone's (ClientWire695) and the zone holds the match state, so
  it drops answers to searches that are over (matched here meanwhile / not asked to stop). Published game flags carry
  two bits of ours above the client's: 0x1_0000_0000 open (CanJoin), 0x2_0000_0000 someone queued; `state` is the
  client's game state number (FUN_14343ff60). Only with SOCIAL_ADDRESS set; in-process the local Game Finder is
  unchanged. Rules: counts = every world (players searching anywhere; active / waiting / inactive games of the
  searcher's kinds, same rules as the local FinderCounts); a match on the searcher's own world is found by the zone
  (landscape, already-in-it and the queue join are zone checks) and joined as before; a game on another world that
  would match is named in the update and the player is told once with a system line; nobody is moved across worlds
  (L31). Publishing: MatchmakingGames whole per world, on change, at most every 5 s (checked on the 2 s game sync), in
  full after every reconnect together with the zone's searches; the service drops a world's games and searches when
  its link closes or a character leaves the world. While the service is unreachable searches fall back to this
  world's own counts. Test: TESTING-NEEDED.md 2026-10-16 "Game Finder across worlds".

### 6.9 0.695 numbering

Back ends see 0.589 numbering (internal-transport.md). For this service that means: InGamePurchase subs ≥ 0x30 +1 both
ways, translated by the gateway; 0.695 0x30 TemplateProductCreate arrives on **Zone** under the internal id of
coverage §5.2 (the zone forwards it as CommerceTemplateProductCreate); the collisions listed there (UnFollowCreator
0x36 → RequestAccountStore) disappear once the gateway translates.

## 7. Behaviour when down

The client never shows a message for a not-routable channel (FUN_142d582a0 / FUN_142d58300 only log to NetInfo.log),
and no "store / commerce unavailable" text exists in either build's text (text_589.tsv searched for unavailable /
offline / try again). Nothing is invented on top of that.

| Down | What players see (client-faithful) | What the servers do |
|---|---|---|
| worldchannel | trade invites do nothing; walk-on vote does nothing (today: the gateway sends channel 1 to the zone while WC is away, §8 step 2, so the zone's Ratings.cs answers likes from its kind-11 copy; WC copies those likes into claim_vote at its next start); Player Studio lists empty, review popups stop; RequestContests repeats (~30×, MODLOG 10-11); **logout to character select waits** (60 s deadline, outcome → L61); encounter requests ignored. Zone play, chat (Zone channel), building unaffected. | gateway sends ChannelIsRoutable(1, false) and drops channel-1 packets (#13); the zone keeps a TradeZoneSession only while WC confirms it (on WC loss the zone ends open trades with code 1 TradeCanceled, a real client code). On restart WC rebuilds presence from the gateway and the zone re-sends active trades / encounters (TradeZoneSessionEnd for any it can't match). |
| commerce (WC up) | ProductCreate: the zone answers 0x30 with status 0 → the client's default "CommerceCreateProductUnknownError" (FUN_14319fc60 default case); ServerStatusRequest: WC answers 0x0E false (OnReceiveServerStatus(false)); other commerce requests unanswered as today; Showcase page doesn't load (HTTP refused); portraits keep the client's default face. | WC queues nothing (requests are UI-driven and repeated by the client); the zone keeps its last CommerceMarketplaceStatus cache so Designs stay in their category; CommerceMarketSnapshot on reconnect. **Built (step 8)**: status 0 from the zone (checked: 0.589 FUN_14319fc60 / 0.695 FUN_14324ab40 default case), 0x0E false from the **zone** (it is commerce's client here; FUN_1431ab880: one byte → Lua OnReceiveServerStatus), and with the service up the service answers 0x0E **true** [I]; a contest entry gets ContestEntryResults false (the client's own ContestSubmission.Fail) [I: added so the Contest window is not left waiting; L124]; the Showcase page still loads (it is the world's) and its Player Studio list is empty / "Get" says the service is unreachable (our page text); gallery likes and listings work from the zone's last cache. |
| login service | logout to character select fails → 0xF7 false → run state 0x17 (case 0x79) | WC answers false at once rather than letting the 60 s deadline run. |

## 8. Build + test plan (isolated Docker copy; dev servers untouched)

Prerequisite: the gateway service (#13, Phase B2) with channel routing and the internal transport library. Build order
inside this item:

1. **Isolated copy**: compose project `lmemu-wcs-test`, own networks (`lmemu-wcs-test`, `lmemu-wcs-test-web`), image tags
   `landmarkemu-*:wcs-test`, ports offset by +10000 (world 29000/udp, web 29001, login 30042/udp, db 13307), its own
   `server-data-wcs-test/` and a copy of the dev databases made with mariadb-dump (backup first). Never `up.sh` / the
   `landmarkemu` project. Use a test client copy (re/dlogin.sh direct connect) pointed at the test ports.
2. **Passthrough WC** (**done 2026-10-15**: LandmarkEmulator.WorldChannel/ChannelRelay.cs; the zone's internal port is
   ZONE_INTERNAL_PORT 20310 with alias zone-<key>; while WC is down the gateway sends channel 1 to the zone, a
   deviation from live's drop that keeps the zone's channel-1 handlers serving until WC owns them): new LandmarkEmulator.WorldChannel (HostedService pattern like the WorldServer) that forwards
   *every* channel-1 packet to the zone. Gateway routes channel 1 to it. Test: the full regression scenario set must
   pass unchanged (re/test/scenarios, run 10 had 79/79), plus a log check that channel-1 traffic took the new path
   (console.log "Channel: 1" lines).
3. **Sinks + locale + Steam / Lua metrics / WallOfData / opt-in**: handled in WC, nothing to the zone. Test: no
   "unhandled" lines; client unaffected.
4. **Logout session** (+ login message, NoteRelogin): test with slogin.ps1 / logout scenario: character select
   reached, Play back in, relog keeps the item-definition cache (≈2 s, MODLOG 10-14); then stop WC and log out
   (records L61's outcome).
5. **Trade broker** (§6.4): scenarios trade.json, tradecoins, designexport (trade of a Player Design item), ignore.json
   (refused invite); add: cancel from the inviter before reply, partner changes landscape, WC restart mid-trade.
6. **Claim ratings** (claim_vote migration from character_unlock kind 11, one-off script with a backup): ratevote dev
   command, CanVote reply read check (s2cprobe RatingPacketCanVoteReply exact read), daily goal "like" still counted.
   **Built 2026-10-16 (agent; untested)**: RatingBroker.cs / VoteStore.cs in LandmarkEmulator.WorldChannel (messages
   §6.5), zone side Ratings.ClaimInfo / BrokerCounted / BrokerRefused through WorldChannelLink (GatewayLink dispatches
   service families 0xF9 and 0xA7 to it); WorldChannel.json `ClaimRatings` (default true), `Database` (compose:
   `lmemu_wc_<WORLD_KEY>`), `ImportFrom` (compose `WC_IMPORT_FROM`, default lmemu_character). The migration is not a
   one-off script but the service's start: claim_vote empty → copy every kind-11 row (INSERT ... SELECT across the two
   schemas; source only read; back up first as for SOCIAL_IMPORT_FROM); not empty → copy only the kind-11 rows it lacks
   (the likes the zone's fallback counted while WC was away). The `ratevote` dev command goes through WC while one is
   linked (RatingClientRequest). Test: TESTING-NEEDED.md 2026-10-16 "Build-site likes through the world-channel
   container".
7. **Encounters / duels**: duel.json (11/11) with the lifecycle in WC. **Built 2026-10-16 (agent; untested)**:
   LandmarkEmulator.WorldChannel/EncounterBroker.cs owns the registry (ids from 0x40000001, kind, state, participants
   with connection + name, open duel challenges with the one-minute timeout swept by a 5 s timer) and answers 0x67 /
   0x69 / 0x6C / 0x6D / 0x7C of its own encounters (ChannelRelay routes 0x2C to it before the zone relay; anything not
   its own goes on to the zone); the S→C packets are byte-identical to the zone's Encounters.cs. Zone side
   (Encounters.cs): with WorldChannelLink linked, /duel checks range and sends EncounterCreateDuel, /duelaccept sends
   EncounterDuelAnswer, Hit / Update / /duelleave / logout send EncounterForfeit, and the registry is a mirror of the
   EncounterParticipants pushes (so Duelling / Hit / the range tick / IdOf work unchanged); without a link it runs the
   lifecycle itself as before (§7). WorldChannelLink.Detach ends brokered encounters with Terminate + Shutdown;
   EncounterBroker.ZoneLost forgets them. GatewayLink dispatches family 0x2C service packets to WorldChannelLink.
   WorldChannel.json `EncounterBroker` (default true) hands them on unchanged when false. Test: TESTING-NEEDED.md
   2026-10-16 "Duels through the world-channel container".
8. **commerce container**: move Commerce.cs / Contests.cs / Portraits.cs / Store + WebPages; tables to lmemu_commerce
   (migration with world id); scenarios psprobe, psreview, psmail, contestvote, showcase.json (14/14), vendor.json,
   portrait upload test; **two worlds** in the test copy sharing one commerce: product made on world A reviewed and
   taken on world B; contest entry from A voted from B.
   **Built 2026-10-16 (agent; untested)**: LandmarkEmulator.CommerceServer (+ docker/commerce, Dockerfile stage
   `commerce`, compose profile `commerce`, db-init grant lmemu_commerce), messages in §6.3 "Built", zone
   RemoteCommerceService behind COMMERCE_ADDRESS (WorldServer.json CommerceAddress; off by default). Player Studio
   (Commerce.cs rules; the zone keeps Commerce.CheckProduct, the 0x3D / 0x46 / 0x49 / 0x4C zone subs), contests,
   portraits moved; Store / WebPages stay on the world and read their commerce data through ICommerceService
   (IsOnGallery, GalleryListings, ContestNamesOf, ListedDesigns, TakeDesign, PortraitUpload added to the seam). Migration:
   the service's first start copies player_studio_product / player_studio_review / character_unlock kind 16 (same
   product ids; owner account + name and the design snapshot joined from character / template) as COMMERCE_IMPORT_WORLD;
   each world sends its Contests/ and Portraits/ files once at its first link. "Contest entry from A voted from B" has no
   client path today (contest votes come from the site's own walk-on window, so voter and site share a world); the
   shared part tested instead: one contest list, entries from both worlds in it. Test plan: TESTING-NEEDED.md 2026-10-16
   "Player Studio, contests and portraits in the shared commerce container" (COMMERCE=1 up.sh, FakeWorld 92 with the
   commerce link).
9. **Down tests** (§7): stop WC, stop commerce, stop login in turn; check the client's reactions (logs, chatlog, rpc state)
   — rule 4: the client's reaction proves it, not the server log.
10. **0.695 client** against the test copy: InGamePurchase renumbering through the gateway; TemplateProductCreate.
11. Clean up: `docker compose -p lmemu-wcs-test down -v`, remove the wcs-test images and folders; MODLOG + TESTING-NEEDED.

Game Finder (§6.8): counts across worlds built 2026-10-16 in the shared social service; the cross-world match → join
step waits for L31.

## 9. Risks

- **Split requests and ordering**: ProductCreate arrives on Zone while the account store / review requests arrive on
  World; COM must finish a create before answering the client's follow-up RequestAccountStore (the client sends it
  after 0x30, FUN_1431a74e0). Trade: the zone must know a trade before StartSession goes out (§6.4 step 2).
- **Cross-world keys**: character guids and template ids are per world; product ids must stay below 2^32 and non-zero
  (the client's BundleId, Commerce.cs:498; Unlocks kind 16 stores the product id as u32).
- **Template data across worlds**: a design listed on world A must be reviewable and takeable on B → COM keeps a
  snapshot (the 0.695 TemplateProductCreate already sends one; 0.589 needs the zone to attach it).
- **Synchronous calls today** (Templates.cs:281 IsOnMarketplace, Store.Gallery → Ratings.Total, WebPages.OnWorldThread)
  become cached / asynchronous; stale caches after restarts are handled by CommerceMarketSnapshot.
- **Logout depends on WC + login**: a WC restart during logout leaves the client on its 60 s deadline (L61).
- **Bandwidth (rule 8)**: commerce replies are per request; fan-out (queue, encounter, matchmaking counts) uses the 0x16
  list packet and is coalesced to state changes (MatchmakingUpdate only on change, as the client keeps the last values).
- **Unresolved send sites** (L7: FUN_142e36f70, sent on World or Zone by argument) may carry a message this service
  should own; passthrough mode (step 2) keeps it working meanwhile.

## 10. Open leads (added to OPEN-LEADS.md)

- **L59** leaderboards: no UI caller, no client reply class.
- **L60** teleport queue: what creates GaqItemTeleportToZoneRequest; PostQueueNotify fields; when live queued.
- **L61** logout without a 0xF7: the client at its 60 s deadline.
- **L62** commerce session ticket / Spice web auth: trigger and destination.
- **L63** lobbies, mini-game invites, quizzes, requisitions, encounter match invitations, telemetry bodies: no UI caller found.
- Existing: **L31** (Game Finder match → join), **L48** (server-to-server layouts: the internal bodies above are ours),
  **L7** remainder (FUN_142e36f70).

## 11. Hand-offs

- **#12 social**: (a) SocialPacket* / Social*Wall* (14 World classes: profile, likes, wall, challenges, reputation,
  diplomacy, house rating / furniture score, game top scores) are in neither scope list — proposed for #12;
  (b) commerce needs a "send system mail" message (Player Studio result mail kinds 0x10 / 0x20, Commerce.cs:596);
  (c) trade invites need an ignore check (Trades.cs:88); (d) BUILD-PATH Phase B1 lists **Player Studio** and the **Game
  Finder** under the shared social service: this spec puts Player Studio in `commerce` and defines the Game Finder
  messages (§6.8) for whichever shared container hosts it — coordinator to decide; (e) SocialShare 0x104 (Zone channel).
- **#13 gateway**: route channel 1 to `worldchannel-<world>` (passthrough to zone until each module moves); connection
  events must carry zone id and client build; close the connection after ClientLogout 0x14 that follows 0xF7; per-build
  translation of InGamePurchase (≥0x30 +1) including 0.695 0x30 TemplateProductCreate arriving on channel 0; the client
  only logs ChannelIsRoutable (FUN_142d582a0).
- **#16 Environment Server**: contest winners placed beside each spire ("Landmarks of Landmark") need the contest
  results from `commerce` (contest / contest_entry tables).
- **#10 / #3 / coordinator (corrections)**: PacketSetLocale 0x5B is **C→S only** (ctor FUN_142bcc450 StringFixed&lt;8&gt;,
  sent in FUN_142c3b490 on channel 1; FUN_142be2e00 is its destructor) — trace/families/005B and coverage-695 0x5B say
  S→C; coverage-695 lists CommerceProductCreate as sub 0x0 — it is **0x2F** (Commerce.cs:51, FUN_1431b7c20) and it is
  sent on **Zone** (channels-589.tsv misses it: FUN_1431b7c20 / FUN_1431ad850 call FUN_14318a170 with channel 0).
- **Coordinator**: unassigned World classes (§3.8: GoHome, RendererDump, RequestRewardPreviewUpdate, GameTime,
  ActivityListRefresh) — WC forwards them to the zone; confirm.
