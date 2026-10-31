# Phase E4: the 0.695 client's own logs from real play on our server (2026-10-26)

Slice: `<0.695 install>\Logs\*` (read only, nothing there written, moved or deleted). Brief:
trace/0695/agent-phaseE3-brief.md. Server code read at HEAD db90a5f (client-0.695). Scratch (grouping script and
outputs): `<user folder>\AppData\Local\Temp\claude\<workspace>\28d489f2-5945-4c82-ba0b-9796b72f5283\scratchpad\e4-clientlogs\`
(`group.py`, `groups.txt` = every distinct message per file with counts per day, `oaat.py` = the client's string hash).

**What the logs cover.** Four client launches (column 4 of each line is the launch id):

| Launch | Day / time (local) | What happened |
|---|---|---|
| 1791999917 | 10-15 04:45-04:48 | one login, Liberation / Pioneer Jungle |
| 1792284043 | 10-18 11:40-12:28 | Player on Liberation, TestPlayer2 nearby, cave re-zones 12:18-12:25 |
| 1792563749 | 10-21 17:22-18:42 | Events world, Halloween Party island (460 / continent 436), 4 relogs |
| 1792568575 | 10-21 18:42-20:45 | same island, relogs, idle kick at 20:45 |

The newest session ends 2026-10-21 20:45; "today's fixes" below = `git log --since=2026-10-21T20:45` (40 commits,
8c0e6a7 … db90a5f). Method: every line split on tabs, numbers / hex / ids normalised, grouped and counted
(`groups.txt`); each message's format string grepped in `re/decomp_all_695` to find the writer; the server and the
shipped data checked for the id or name in question.

---

## 1. Findings (ranked by player impact)

### F1. Design and build-site screenshots never load in the client: `MarketplaceAssets` still points at Daybreak — GAP (a)

- **What the logs show [P]:**
  - TemplateImageCache.log: 335 × `OnHttpResult - failed to download image, result: 404` (writer FUN_143534… in
    part_0109.c:17283).
  - AssetFailure.log: 186 × `Failed to load asset idi2:cacheImage_template_<guid>_0_small.jpg`. There are 29 distinct
    design guids (6004424203191713792 … 818, our server's design ids), plus one `template_0_0` on 10-15 (older build,
    not seen since).
  - Ui.log / GFxWrap.log: 2305 × `ERROR LOADING IMAGE( path="img://ii?id=15000000xx", swf="LandmarkHUD.gfx")`, with
    106 distinct ids from 1500000000 to 1500000105.
- **What the ids are [P]:**
  - ImageDirectory's constructor FUN_1431ba1d0 (part_0090.c:6613) starts a counter at 1500000000.
  - FUN_1431bced0 (part_0090.c:9006) gives a name that is not in the shipped image table the next id from that counter.
  - FUN_143534a20 (TemplateImageCache, "cacheImage_%s.%s") registers each downloaded template image this way. So every
    `img://ii?id=15000000xx` error is a design tile whose download failed.
- **Where the URL comes from [P]:** ClientConfig `[Marketplace] MarketplaceAssets`. It has four readers in the exe:
  - part_0086.c:1625: the Designs window tiles, `template_%llu_%d_small`.
  - part_0113.c:1747 / 8534: Lua `Templates.GetTemplateUrl` (FUN_1435dc0f0) and `Claims.GetClaimUrl` (FUN_1435e5290),
    which feed the SelectedTemplate and ClaimManagementWindow ScreenshotViewer.as.
- **The install [P]:** `<0.695 install>\ClientConfig.ini:70` has
  `MarketplaceAssets=http://assets-cdn.daybreakgames.com`. Diffed against `ClientConfig.ini.orig`, LaunchPad changed
  only `Server=` and `MarketplaceUrl=`.
- **Server [P]:**
  - The server already serves these images:
    - `LandmarkEmulator.WorldServer/Game/WebPages.cs:56`: TemplateImage `/uploads/lm/images/templates[N]/(small|large)/…jpg`.
    - `WebPages.cs:57`: SiteImage.
    - `Game/SiteScreenshots.cs:71`.
  - But LaunchPad never points the client there:
    - `LandmarkEmulator.PatchPacks/ClientConfig.cs:103-127`: `Managed = [Server, Marketplace, Threads]`, and `Apply`
      writes only Server, MarketplaceUrl and DirectThreadCount.
    - `LandmarkEmulator.Tests/PatchPacks/ClientConfigTests.cs:34,65` asserts that the `MarketplaceAssets` line stays
      as it was.
  - Only the docs say to set it by hand: docs/showcase.md:11 and TESTING-NEEDED.md:1389.
  - Not touched since 10-21: `git log --since=2026-10-21 -- ClientConfig.cs LaunchPad/Bridge.cs` is empty.
- **Player impact:** every tile in the Designs window, the Player Studio screenshot viewer and the site screenshots in
  Claim Management stay blank for anyone who starts the game with LaunchPad.
- **Fix (not applied):**
  - Add a fourth managed setting, `MarketplaceAssets`, in `ClientConfig.cs`. Set it in `Apply` to the same address
    as `marketplaceUrl` (`Bridge.MarketplaceUrl`, Bridge.cs:480, already gives the world's WebPagesUrl). The asset
    URLs only add `/uploads/lm/images/...` to it.
  - Update ClientConfigTests:65 to expect the line changed, and docs/launchpad.md "What PLAY does".
  - Optional: a server setting `WebPagesAssetsUrl` for owners who host the images elsewhere.
- **In-game check:** yes. Open the Designs window and a design's screenshot viewer, and Claim Management → screenshots.
  TemplateImageCache.log should then log no 404.

### F2. `Failed to QueueCompositeEffectAtLocation due to missing effect definition for Id #0!` × 1843 — unclear (c), lead L-E4-1

- **Writers [P]:** FUN_143d576a0 and FUN_143d57a70 (part_0150.c:27137 / 27348). They are the generic effect queue,
  with 12 and 6 callers.
- **When it happens [P]:**
  - Steady bursts of 3-12 lines a second, in multiples of 3 (three effects per tick).
  - 10-15 04:48:08-28. 10-18 12:09 and 12:18-12:22 (the cave re-zones). 10-21 17:36:13-38 and 19:16:55-19:17:20.
  - On 10-21 each burst starts about 1 s after a claim walk-on banner closes (GFxWrap 17:36:12 / 19:16:53) and stops
    a few seconds before the next one opens (17:36:42).
  - Not footsteps: the per-minute counts do not line up with the footstep "profile is NULL" lines.
- **What it is not:**
  - Our server's PlayCompositeEffect (0x26/0x0B, Game/HitEffects.cs) only sends fixed non-zero ids [P, every
    HitEffects caller read].
  - The world effect 0x26/0x56 PlayWorldCompositeEffect (dispatcher case 0x55) is never sent [P].
  - RemovePlayer (case 0) plays its effect only when it is non-zero [P, PlayerUpdate_Dispatch FUN_142cdfa60].
- **Candidate [I]:** FUN_1431697f0 plays an entity's `+0x48cc` effect with no zero check. That field is set only from
  RemovePlayer's field when the field is non-zero.
- **Player impact:** unknown. Nothing plays, so a live visual may be missing outside claims.
- **Fix:** none yet. See lead L-E4-1. No in-game check until the caller is known.

### F3. Build tools stay locked 3 s after some edits: "Cell not handled" / "Removing pending cell callback" — unclear (c), lead L-E4-2

- **What the logs show [P]:**
  - PendingCellCursorLock.log: 14 × `Cell not handled: [Cell Id …]`, in three groups (8 / 1 / 5) at 10-21 17:56:12,
    17:56:16 and 17:56:20.
  - VoxelClient.log: 3 × `VoxelClient - Removing pending cell callback for sticking around longer than 3 seconds` at
    the same three seconds.
  - PendingCellCursorLock.log: 178 × `ClientConstructionManager::SetGenerateDiffMaskArea - removed cell from final
    pending list, since VoxelFarm doesn't think it changed` around them.
- **What the client does [P]:**
  - The construction manager (FUN_143165920, part_0087.c:36289) keeps a pending list of the cells its own edit changed.
  - The update loop (part_0087.c:21984) drops the callback after 0xbb9 ms and FUN_14314cfe0 (part_0087.c:17128) logs
    every cell still pending. In other words, the client waited 3 s for cells the server never answered. In play this
    is a 3 s lock on the build cursor after the edit.
- **Server [P]:** `Game/Building.cs:619 SendCellResponses` answers every cell *named in the ApplyDiffRequest* on every
  path: success :813, refused :749, environment server away :781, materials :794, unreadable :719, heal :344-368.
- **Inference [I]:** the cells left pending are ones the client's diff mask counted but the request did not carry
  (neighbour cells?), or ones it waits on for a cell update rather than a response. On the heal path, a cell
  `Env.Has` doesn't hold gets no `Resend` (:371).
- **Today's fixes:** they don't touch this path [P]. `git show 8219f7c b3fccd0 -- Building.cs` changes no
  SendCellResponses / Resend / SendResponse line.
- **Fix:** not yet. See lead L-E4-2. Needs an in-game repro (which tool; the server log for the same edit).

### F4. Character select: one GUI model gets the outfit model with `[RACE][GENDER]` unresolved — unclear, low (c)

- **What the logs show [P]:** AssetFailure.log has 5 × `Char_Biped_[RACE][GENDER]_Entities_Founder_001_Chest.adr` and
  1 × `…Anniversary_Light_000_Chest.adr`. All six fall on 10-21, each at a `WaitForCharacterSelectLoad` (17:22:54,
  18:02:58, 18:43:22, 19:10:06, 19:20:42).
- **Why 10-21 only [P]:** the login server only started sending worn clothing on 10-21 (829c209 imported
  `ItemAppearances.BodyFromEquipped`). It sends the name as the dump has it:
  - LoginHandler.cs:122-130 `WornAttachments`.
  - Shared/Game/Cosmetics.cs:41 builds `Char_Biped_[RACE][GENDER]_…`.
- **What the client does [P]:**
  - The client expands the tokens itself (FUN_143b89e80 part_0141.c:1684-1763).
  - It takes race and gender from the actor's +0x2138 / +0x213c, or from the model definition (FUN_142ebd740). When it
    finds no race it leaves `[RACE]` in the name.
  - The same chest resolves (`Char_Biped_HumanFemale_Entities_Founder_001_Chest.adr`, AttachmentProcessor.log) on the
    character-select actor 4 s later (18:43:26). The failing load is a second model in that scene
    ("GuiModelManager … More than one GUI model in list" at the same second).
- **Player impact:** probably none visible [I].
- **Optional fix:** the login server could expand `[RACE][GENDER]` itself with the character's race and gender (the
  World server path doesn't need it). Check in game whether any character-select view shows a missing chest first.

### F5. OK-NOTE (a, intended): the server's own client-Lua commands log "Unrecognized command"

- **What the logs show [P]:** CommandQueue.log has 103 × `Doing: lmemuN` / `**** Unrecognized command - lmemuN`
  (N 1-53), and 5 × `admin`.
- **Why:** these are ClientLua.RunCode (`Game/ClientLua.cs:60-75`, BindCommandToLua + Ui.ProcessChatCommand) and the
  admin `/admin` bind. The doc comment already says the client logs the line after running the bound code.
- **No change.**
- The client's own Lua binds behave the same way:
  - `marketplacepreload` (×16): UI/Default/InGameMarketplaceBrowser.lua:178, run by Resources/Scripts/events.lua:610.
  - The emotes `cheer`, `stand`, `wave`, `dance`, `kneel` and `emote N` are run by DynamicCommands right after
    ("Execution: Command Executed: …").
- `s hey loser` (×1) really is unrecognized. The 0.695 chat command list in the exe is `say, whisper, tell, t, reply,
  yell, shout, emote, em, me, officer, whochannel, groupsay, partysay, psay, gsay, g` [P, exe string table at
  0x475888f], with no `s`. So it is a client fact, not a server one.

### F6. OK-NOTE: install config still has `DirectThreadCount=10`

- **What the file shows [P]:** ClientConfig.ini:19 has `DirectThreadCount=10`, the same as `.orig`. PLAY writes 2
  unless advanced mode says "Leave" (ClientConfig.cs ThreadsToWrite). So the last PLAY left it alone.
- **Effect on these sessions:** loads were fine. ClientSceneProcessing "Finish SCENE" averaged 124 / 114 ms on 10-21
  (max 7.8 s), against 958 ms (max 24 s) on 10-18.
- **Worth asking the user** whether advanced mode is on. This relates to the allocator-slot project note, and is not a
  server issue.

---

## 2. Checked and fine (one line per message group; (b) = client or install only, also on live)

**Install / client data (b)**

- **VoxelDataErrors.log** 14 version mismatches (10-21): the documented cache-vs-edit case (LandmarkEmu/docs/terrain.md:808-840).
  - 11 in session 1 expect 492747177 (the generated version).
  - 3 at the 18:43 login are the reverse, cells edited the previous run. No new mechanism.
- **VersionedResources.log** 69 × `asset not in pack files: <cell id>`: mesh-cache misses (2316827… cell ids and
  3464256… ids) on the cache path above. 32 × `Unable to load cache metadata`: client cache. (b)
- **CalculatedStat.log** 3578 × `unable to find a stat entry for stat id 4` (FUN_142e5d300, part_0072.c:24948). Stat 4
  is the light radius, read through the manager vtable by ability-parameter calculators (part_0108.c:16928 …). The
  bursts fall in character select (18:54:27-50, after the 18:54:20 logout), at the World Editing menu (17:54:13-18)
  and while loading screens rebuild characters. Those are GUI / preview models with no server stats. The own player
  gets stat 4 (`DefaultCalculatedStats.cs:14`, `WorldSession.SendCalculatedStats` :697). (b) [I for which model]
- **SkillsError.log** 840 = 210 per launch × 4, `No definition was found for profile id` (FUN_143bf2f90), at start-up
  before login. Profile skill tables are empty in the shipped client (FailedSyncLoadAssets: Profile*.txt;
  trace/server-only-data.md "Profile…"). (b)
- **FailedSyncLoadAssets.log** 21 files × 4 launches (`SynchronousGetAssetData failed, local version was not
  current`, FUN_143b2fe00 part_0138.c:32763): ResourceTriggers, IgScriptProp, IgRuleReqSettingMapping,
  CalculatedStatGroupEntries, Profile*, ClaimAccessLevels, Projectiles, GodBeams, Doors, Tints.xml,
  icon_UI_empty_portrait_64. All are server-only or empty in the install: trace/server-only-data.md; none is in
  `…\Resources\`; gameData695/Projectiles.txt is 0 bytes. (b)
- **InstancedGameTriggerDefinition.log** "Failed to load definitions for Ruleset Script Props / required rules":
  follows from IgScriptProp / IgRuleReqSettingMapping above. (b)
- **CalculatedStats.log** "0 groups loaded": follows from CalculatedStatGroupEntries above. (b)
- **Social.log** SocialSettings / SocialMiniGameData / DiplomacyEventTypes not found, per login: server-only
  (server-only-data.md:53, 198, 201). (b)
- **MarketingDataDefinitions.log** "FAILED to load any storePortalCategory / storeBillboardPanel entries",
  `Configuration.txt` missing: the shipped Marketing files have 0 rows. (b)
- **DeferredAssets.txt / main log** "Download mandatory assets failed (MandatoryAssets / SmallUiAssets / UiAssets)":
  ClientConfig `[AssetDelivery] IndirectServerAddress` is the dead sdt-devpatch.station.sony.com. (b, environment)
- **RequirementErrors.log** `VoxelCursorWithinValidClaim`, `ItemIsBuildingTool` at start-up: client requirement table. (b)
- **ActorFailures / AssetFailure:**
  - `Light_CharacterCreate_Yellow/Cyan.adr` (char select).
  - Kobold / Necrozoa / ScorpionBush / DarkElfFemale actors (once per launch, client preload: Preload.log
    "Necrozoa_Ambient.xml …"; no server reference).
  - Biome_Freeport / Everfrost / Indar flora .dme and Voxel_DisplacementMaps_Rocky011.bmp (every world load, biome flora
    sets).
  - ComplexObjectAnimationNetwork6464.mrn (claim cursor flag, once).
  - All assets the install lacks. (b)
- **AssetFailure** `ContinentCavePlacements[436].cpa` (10-21) / `[417]` (10-15): documented, the event-island
  continents have no cave file (docs/terrain.md:468). (b)
- **EquipmentSlotDefinitionManager.log** 3297 × `No slot definition for "ForwardGrip"`, plus Knife / QuickUse:
  hard-coded names in FUN_142ecafd0 (part_0074.c:37683, PS2 weapon parts). EquipmentSlotDefinitions.txt has 8 rows. (b)
- **ActionBarManager.log** 960 = 80 per zone-in, `GetSlot called with illegal bar id 2-9 (allows 0 to 2)`.
  FUN_14344daa0 builds `Character.ActionBars.Bar%d` for 10 bars × 10 slots, but ActionBarDefinitions.txt defines bars
  0-1 only. No server packet sets the bar count. (b)
- **Main log** 116 × `Unable to apply tint alias - id 1381304130 not found` (FUN_143d9e080). 1381304130 is the
  client's hash of `dyetint` [P, oaat.py matches; same hash gives Generic_Red's listed value]. That is the tint alias
  of 632 item fields in gameData695/items.json, decoded from live's CommandBase dump. Live's ArtTints.json capture has
  no `dyetint` alias. So live logged the same. (b)
- **Main log** 601 × 2 `ClientFootstepAnimationEventHandler::Get(Additional)SoundId: player's current profile is
  NULL` (FUN_14347d910 / FUN_14347dca0): footsteps fall back to profile defaults. The lookup is FUN_142ea21d0 (actor
  +0x1474 in DAT_145431a20+400). The windows (10-18 12:01-12:07, 10-21 17:58-18:02) match no burst of re-zones. (c,
  low): lead L-E4-3.
- **Main log** `Unable to find a physics material for voxel material type test / driedgrass`: client material
  table. (b)
- **Main log** `GuiModelManager … More than one GUI model in list`: char select. (b)
- **Main log** `ReadPhysicalDriveInNTWithAdminRights`, `File::Open failure` (LocalConfig, ClientConfig_en_US,
  ActivityUserData, AudioGlobal, ui manifests, Doors, Tints, Marketing Configuration): hardware-id probe and optional
  files. (b)
- **Main log** `Gui script error … attempt to index a nil value (local 'self')`: once per launch, before the login
  connects. (b)
- **PhysXErrors.log / main log** APEX "No NxParameterized::Factory", PxScene lockRead warnings: engine. (b)
- **Audio.log** "Mixer … unknown vcaGroup 'Ambient'": client data. (b)
- **SoeData.log, Graphics.log** (VolumeSelect_Arrow.dds), **LuaErrors.log** `ScriptsBase.bin: size_t size mismatch`:
  client data or install. The UI then runs the loose UI/Default Lua. (b)
- **LuaErrors.log** `TweakMode.gfx … GetProperty on invalid parent window` ×6: client UI. (b)

**Flash UI (b)**

- **FlashErrors.log** 13 × `DSSetDragData: failed to read property list!` (FUN_1430f7b60): a drag passed without a
  PropertyList array. (b)
- **FlashErrors.log** 1 × `DS MarketingData.AutocompletionTags not found`: no producer. The string is absent from
  Landmark64.exe [P, byte search]; the only reader is ClaimManagementWindow EditPanel.as:27. Already n/a in
  trace/UI-AUDIT.md:225. (b)
- **GFxWrap** TypeErrors, all client AS3:
  - `WalkOnTextWindowDocumentClass/handleRemovedFromStageEvent` ×13. `showHandleButton()` calls
    `setPanelState()` = PANEL_STATE_NOPANEL, which nulls `_walkOnTextPanel`, and the handler dereferences it
    unguarded (re/as3_695/ClaimWalkOnTextWindow …:206-226, 844, 1169). Every close after the banner shrinks to its
    handle throws.
  - `InboxDocumentClass/handleListEvent` ×4: INDEX_CHANGE with no renderer.
  - `ContactListWidget/handleRightClick`: a click with no row data.
  - `ItemContainerBase/handleResultsListEvent`: roll-over during populate.
  - `TooltipManager`, `MWindow/handleRemovedFromStage`, `GroupWidget titletop`.
  - All (b).
- **GFxWrap** `Loading "<Widget>.gfx": failed to find asset` / `widgets/<Widget>.gfx`: the loader tries .gfx first. The
  .swf exists (UI/Default/BuildModeWidget.swf …) and the widget runs (the WalkOnText errors above prove it loaded). (b)
- **GFxWrap** `Loading "?t=0.93…" failed` ×16 at ClaimManagementWindow open. ScreenshotViewer.as:150-157 asks for 4
  images and adds `?t=` even when `Claims.GetClaimUrl` returns "" (FUN_142dbcf50: index ≥ the claim's image count).
  With fewer than 4 shots this is normal. (b)
- **GFxWrap** Scale9Grid negative width/height, IME warning, `Sound::load not implemented`. (b)
- **Ui.log** `NotificationQueue::GiveTime - Flashy notification timed out without end callback` ×61
  (FUN_143722560): a toast movie with no end callback within 10 s. Mostly around loading screens. (b/c, low)

**Network and login (b)**

- **Login.log / NetInfo.log / LoadingScreen / main log:** run-state transitions, logins, relogins and
  `DisconnectReasonOtherSideTerminated` at each logout (the immediate close noted in memory). (b)
- **Main log** `WaitForWorldReady Status: InitialZoneDataComplete=0 …` once (10-18 12:25:36): during a cave re-zone
  that finished at 12:25:38. Known re-zone ordering (cave-teleport-hang). Nothing new.
- **NetInfo.log** `BaseClient::IdleKick` ×2 and `Lost connection to the gateway while waiting for a relogin session`
  (20:45). This is the client's own idle timer: FUN_142ca7ca0 reads `System.ClientIdleTimeout` = 1800 from its own
  StringHashToValue.txt:596 [P, hash 0x14842c99]. It fired again during the relogin and the client shut down. (b)
- **Landmark.log** `SetWorldZoneInfo … characterName=[Unknown Name]` and `worldNameId=-1`: FUN_142d52ac0 uses
  "Unknown Name" until the player exists, and -1 = "leave as is". (b)
- **EmbeddedBrowser.log** `OnGetAuthenticatedUrl: Result: 4`: the CAS ticket (auth.daybreakgames.com) fails, and
  FUN_14311cca0 then uses the plain MarketplaceUrl. The server signs the Showcase in its own way. (b, environment)
- **BrowserCef.log:** the Showcase loads 200; `ErrorCode=-3` (aborted by the next navigation) ×5. The empty-token
  `/api/me?t=` calls come from the login preload before the sign-in; the page reloads with the token. Fine.
- **ClientInGamePurchase.log** "no IOTD / bundle groups / categories / BundleTags sent" (FUN_143260610): read by no
  0.695 UI (Game/StoreBundles.cs:22-46, agent-store-entitlements.md). The store itself arrives ("Store: 1, Count: 11"). Fine.

**Logged by design (fine)**

- **ItemDefinitions.log:** about 2960 definitions once per launch.
- **ItemDefinitions.log** "Overwriting existing item definition" ×284: the client re-requests one it holds pending
  (CommandBase 0x49 → `ItemDefinitionManager.SendDefinition` :339, client-driven). Fine.
- **AttachmentProcessor.log** (1 MB): info-level attach / detach. The 200 rebuilds at 18:18 are the Crimson Parlor
  preview (BarberShopWidget loaded 18:18:05). Fine.
- **DynamicCommands.log:** the zone command list (33 entries; 9 more emotes since 10-21) and the world list
  (gatewayprofiler, playdead). Fine.
- **ClientSceneProcessing, CommandQueue, cjhlogfile, LoadedAssets, SyncLoadedAssets, DirectAssets, Preload,
  TemplateImageCache** (beyond F1), **Vivox, VoxelFarm** (ContinentId 436 / 441 / 417 / 406), **ClientMountLog,
  CharacterCustomization, Abilities, CraftingStations, Recipes, ToolDefinitions, Keyword / PropTag / Category /
  InventorySlot definitions, TotalCacheManager:** load counts and info lines only.

---

## 3. New leads

### L-E4-1. Who queues composite effect id 0 (F2)

- **Need:** the caller of FUN_143d576a0 / FUN_143d57a70 that passes id 0 in steady 3-per-tick bursts outside claims,
  and where the id should come from (a server field or client data).
- **Searched:** every server sender of 0x26/0x0B (HitEffects and its 30 callers: fixed non-zero ids); PlayerUpdate
  cases 0, 10, 0x55; the 18 callers of both queue functions (FUN_142e19660 uses 10468; FUN_14303abe0 returns early
  for an unknown id; FUN_1431697f0 reads entity +0x48cc with no zero check); timing correlated against footsteps,
  re-zones and claim banners.
- **Try next:** on a 0.695 test copy (not the user's client), a Frida hook on both functions' entry that logs the
  return address and the first u32 of the effect record when it is 0. Walk out of a claim on the Halloween island.
  The return address names the caller.

### L-E4-2. Cells the build cursor waits for that the server never answers (F3)

- **Need:** which cells the client keeps pending after an edit (FUN_143165920's list) that are not among the
  ApplyDiffRequest's cells, and what the client waits for on them (a 0xEC/3 response, a cell update or a version).
- **Searched:** Building.cs every SendCellResponses path (all request cells answered); the heal path's
  `Env.Has`-only Resend (:371); commits since 10-21.
- **Try next:**
  - Repro with the server's debug log on: the same tool at 17:56 on 10-21. It was three edits 4 s apart; check the
    server log for that minute.
  - Compare the request's cell ids with the "Cell not handled" ids.
  - If they are neighbours, answer them too (0xEC/3 ok plus the cell's current data), as live presumably did.

### L-E4-3. Footstep "current profile is NULL" (low)

- **Need:** which actor's +0x1474 profile id is missing from DAT_145431a20+400 at 10-18 12:01-12:07 and 10-21
  17:58-18:02.
- **Searched:** writers of +0x1474 (ctor default 0 part_0073.c:18040, 1000 at part_0054.c:8010).
- **Try next:** find the packet field that sets +0x1474 for proxied characters (AddPc / full PC reader). Check that the
  server sends the profile id of a profile the viewer's list holds (ReferenceData ProfileDefinitions, dump
  41-ReferenceDataProfileDefinitions).

---

**Summary.** Of about 130 distinct message groups in 62 log files, one is a real server-side gap:

- **F1:** LaunchPad never points `MarketplaceAssets` at the world, so every design tile and site screenshot fails.
  This accounts for 2305 + 335 + 186 of the error lines. The server already serves the images; the fix is one more
  managed ClientConfig line.

Two need a trace before a fix:

- **F2:** 1843 "effect id 0" lines with no known sender.
- **F3:** three 3 s build-cursor locks on 10-21.

The rest is client or install behaviour that live had too, with the reasoning for each in §2. That covers the
calculated-stat 4 lookups on preview models, the server-only tables the client tries to load, the PS2 slot names,
`dyetint` (live's own data), the action bar's 10-bar loop, the AS3 null references and the idle timeout. The server's
own Lua-command lines (F5) are expected.
