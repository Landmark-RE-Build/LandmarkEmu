# System: Claims / build sites (ClaimsBase 0xF9)

Status: [~] traced from the family file (families/00F9_ClaimsBase.md), the client builders and the dispatcher cases
(FUN_142d1ff50); server status read from LandmarkEmulator.WorldServer/Game/Claims.cs (not from coverage.md, which
misses sends written as raw sub bytes).

## What it is

A player plants a Build Site Flag (item 105, right-click → ControllerClaimPlacement), confirms, and owns a plot where
they (and people they permit) may build, place props and set site options (name, welcome text, tags, time of day,
music, play/edit mode for site games, contests, gallery). Live also had upkeep (coins paid to keep the site), claim
groups / attached sites, snapshots (save / restore the site), and ratings.

## Client side

- UI: ClaimManagementWindow (Site Management: name, permissions, upkeep, screenshots, gallery, contest, music, delete
  / recover flag), ClaimConfirmationDialog (placement confirm), ClaimWalkOnTextWindow (walk-on banner: name, welcome,
  rating, join/leave site game, play/edit), UpkeepManagementWindow (deposit upkeep), ClaimUpkeepMessageWindow
  (upkeep warnings), ClaimFeedback, ClaimFiltersWidget, BuildModeHUDWidget / BuildModeTimeWidget / BuildModeCameraWidget,
  ClaimPoN.lua (pay upkeep via Point of Need store).
- Lua natives (luabind "Claims.*"): see families file; client-only ones: camera modes, screenshots (TakeScreenshot,
  MakePrimaryScreenshot, ClearScreenshots), GetClaimUrl, GetOwnedClaimCount, IsClaimListedOnGallery /
  ListClaimOnGallery / DelistClaimOnGallery (→ commerce products, docs/commerce.md), RemoveFromContest.
- Client rules: placement preview and build permission (FUN_142d15900; bypass flag SendSelf +0x287); tags max 128
  chars and "contest_" prefix reserved (FUN_142d33950); props only where the player has build permission (see memory
  "Props only on build sites").
- Data tables: ClaimPermissionDefinitions (6), ClaimPermissionAccessLevelMappings (20), ClaimPermissionStringMappings
  (12), ClaimMusic (4) — none read by the server yet; ClaimAccessLevels.txt is server-only (loader FUN_143b534a0).

## Protocol (u16 0xF9, u8 sub)

Client → server

| Sub | Name | Body (from builder) | Server |
|---|---|---|---|
| 0x19 | RequestAllClaimBasicInfo | — | handled (no answer on purpose: loops) |
| 0x1A | PlaceClaimRequest | f64 x,y,z ... | handled |
| 0x1B | PlaceClaimConfirm | bool | handled |
| 0x1C | ClaimManagmentRequest | — | handled (0x8 + 0xA) |
| 0x1E | RequestEditClaimItemData | u64 claim | handled (0x9) |
| 0x20 | (upkeep data request) | — | handled (0xA) |
| 0x22 | AddUpkeepFundsRequest | u64 claim, u32 coins (FUN_142d18660; client checks coins first, "Claims.InsufficientFunds") | **not handled** |
| 0x24 | CreateGroup | string name, u8 1 (FUN_142d17770) | **not handled** |
| 0x25 | MoveItemToGroup | u64 item, u64 group, u8 1 (FUN_142d28cf0) | **not handled** |
| 0x27 | RequestClaimsByOwner | — | handled (0xF) |
| 0x2A | SetItemName | u64, string, u8 | handled |
| 0x2B | SetItemDescription | u64, string | handled |
| 0x2C | SetTagString | u64, string | handled (kind-4 info update; commit 6f9e9ed) |
| 0x2D / 0x2E | permissions | | handled |
| 0x2F | DeleteClaimItem | u64 | handled (0x30) |
| 0x35 | RequestPropExclusionArea | — | handled (0x11 empty) |
| 0x38 | SnapshotSaveRequest | u64 claim, u32 slot (FUN_142d328f0) | **not handled** |
| 0x39 | SnapshotSaveConfirmResponse | u8 yes, u64 (FUN_142d32b10) | **not handled** |
| 0x3A | SnapshotRestoreRequest | u64 claim, u32 slot (FUN_142d32540) | **not handled** |
| 0x3B | SnapshotRestoreConfirmResponse | u8 yes, u64 (FUN_142d32ab0) | **not handled** |
| 0x3C | SnapshotUpdateName | u64, string, u32 (FUN_142d329e0) | **not handled** |
| 0x3D | SetAsAdminClaim | (admin) | not handled (admin only) |
| 0x3E | SetSupplementalData | u64, u64, string, u32 0xF? (FUN_142d32d50) | **not handled** |
| 0x40 | SetClaimTime | u64, i32, f32, i32, f32, u8 | handled |
| 0x41 | SubmitContestEntry | | handled (Contests) |
| 0x42 | SetMusicOverrideId | u64, u32, u8 | handled |
| 0x43 | SetAdventureStatus | u64, u32, u8 | handled |
| 0x44 | OnClientRateClaim | (built in FUN_142d1f470 together with RatingPacketVoteReply 0xA7/0x12) | **not handled** |
| 0x5E | DebugAddItemClaim | | debug |

Server → client (dispatcher FUN_142d1ff50; case = sub)

| Sub | Name | Layout / client action | Server |
|---|---|---|---|
| 0x1 / 0x2 / 0x3 | Send/Update ClaimBasicInfo (+AOI) | basic info list | 0x1-0x3 never sent (0x4 AOIUpdate used instead) |
| 0x4 | AOIUpdate | bool full, records, removed ids | sent |
| 0x5 | ClaimInfoUpdate | u64, u32 kind, record (kinds: 2 name, 4 tags, 0xb-0xf strings, 0x11 game, 0x13 mode, 0x14 deleted, 0x15 time, 0x16 music) | sent (kinds 4, 0x11, 0x13-0x16) |
| 0x6 / 0x7 | PlaceClaimResponse / Complete | | sent |
| 0x8 / 0x9 / 0xA | Managment / EditItem / UpkeepManagment data | | sent |
| 0xC | AddUpkeepFundsResponse | u64, u32, u32, u32, bool; texts Claim.Upkeep.OnAddFunds.Success / OnAddTimeFor(All)User(s).* | **never sent** |
| 0xD | SendInventoryDetails | | never sent |
| 0xF | SendClaimsByOwner | owner, records | sent |
| 0x10 | ResultResponse | u8, u32 result, string, u64 — "Player or Guild could not be found under the name [%s]" (permissions by name) | **never sent** |
| 0x11 | RecievePropExclusionArea | | sent (empty) |
| 0x12 | LostClaimServerConnection | text Claim.LostClaimServerConnection | never sent (ok) |
| 0x13 / 0x15 | Snapshot Save / Restore ConfirmRequest | u8, u64 claim, u32 slot → confirm dialog | **never sent** |
| 0x14 / 0x16 | Snapshot Save / Restore Response | u8, u32 result, u64 — errors Claims.Error.ShapshotIndexInvalid / RevertClaimUnableToTransferInventory / SnapshotTemplateInvalid / SnapshotIsEmpty | **never sent** |
| 0x18 | ContestEntryResults | ContestSubmission.Fail / Success | sent |
| 0x23 | ClaimRequestMoveItemToPlayer | | never sent |
| 0x30 | DeleteClaimItemReply | errors Claims.Delete.ForeignClaim, AttemptToDeleteClaimWithActiveInstancedGame, AttemptToDeleteRootClaimBeforeAttachments | sent |
| 0x37 | InventoryListDebugResponse | debug | — |
| 0x5F | UpdatePropCategoryCount | u64 claim, list {u32, u32, u32} — prop counts per category (site prop limits) | **never sent** |

## Live behaviour (evidence) and gaps

1. **Upkeep** (0x22 → 0xC; UpkeepManagementWindow, ClaimUpkeepMessageWindow, GameEvents:OnUpkeepWarning, texts
   Claim.Upkeep.*): live charged coins to keep a site; uimap marks DepositUpkeepFunds "unreachable" in 0.589 — confirm
   whether the window can open (decide with the user before charging anything).
2. **Snapshots** (0x38-0x3C ↔ 0x13-0x16): save the site to a slot and restore it, with confirm dialogs and inventory
   transfer (RevertClaimUnableToTransferInventory). Needs: where the UI calls it (find the Lua natives), slot count,
   what a snapshot holds (voxels + props via templates: SnapshotTemplateInvalid).
3. **Claim groups / attached sites** (0x24/0x25; Attached Build Site Flag item 591): grouping sites.
4. **Ratings** (0x44 + RatingBase 0xA7): ClaimWalkOnTextWindow's rate button (Claims.RateClaim marked GAPS).
5. **Prop category counts** (0x5F): per-site prop limits by category shown in the client — find the limits table.
6. **ResultResponse** (0x10): the reply to permission-by-name requests when the name is unknown.
7. **Supplemental data** (0x3E): u64/u64/string — find what UI sends it (FUN_142d34590 / FUN_142d346d0).
8. Data tables to load: ClaimPermissionDefinitions / AccessLevelMappings / StringMappings (permission bits and their
   names), ClaimMusic (site music list).

## Deep pass: placing a site (client code read 2026-10-12)

Flow in the client:
1. Flag right-click → controller state 0x1C (ControllerClaimPlacement), HUD state STATE_CLAIM_PLACEMENT (hudstate.lua
   HudStateClaimPlacement: map hint "Ctrl+Mouse Wheel: adjust height / <Tool.Place>: place").
2. Place key → FUN_142d30400(claims mgr, centre, extents): **client pre-check** FUN_142d28700 on the box:
   - known sites (spatial tree at mgr+0x470+0x50) intersect → 1 → "Claims.InvalidClaimLocationBufferOverlap"
     ("…too close to another build site"), nothing sent;
   - exclusion areas (tree at +0x200, filled by RecievePropExclusionArea 0xF9/0x11) contain it → 3 →
     "Claims.InvalidClaimLocation"; (2 = cave text, from the attach path FUN_142d30790).
   - SendSelf +0x287 (admin bypass) skips the check.
   Otherwise it sends PlaceClaimRequest {f64 centre x,y,z from the box midpoint, then the ground point}, and leaves
   placement (FUN_142fbc270(ctrl, 0) → controller state 0) while it waits.
3. PlaceClaimResponse (sub 6, case in FUN_142d1ff50): result **1** → ClaimDialogs.ClaimConfirmDialog (Result,
   MaxDaysUpkeep, ExpireDate H/M/S) + GameEvents:OnClaimConfirmation. **Any other result** → log "ERROR: Claim request
   error! result=%d", shows the text for the result (FUN_142d29240 → FUN_142d16710), and if the controller is still
   0x1C, **re-enters claim placement** (FUN_142fbc270(ctrl, 1): state 4 → named state "ClaimPlacement").
4. PlaceClaimConfirm → PlaceClaimComplete (sub 7): result **2** → site added (FUN_142d16cf0); other → same error text
   + "ERROR: Claim completion error! result=%d". Either way the controller leaves placement (FUN_142cb5790(ctrl, 1)).

Result codes (FUN_142d16710 → CodeStringMappings text):

| Result | Key | Text |
|---|---|---|
| 1 | (ok, confirm dialog) | |
| 2 | (complete ok) | |
| 3 | Claims.InvalidClaimLocation | The build site location selected is invalid.  Please try again. |
| 4 | Claims.InsufficientFunds | You must deposit additional upkeep to stake a build site. Press U … |
| 6 | Claims.NoClaimItem | You do not have a build site flag! |
| 8 | Claims.MaxNumberClaimsForArea | This maximum number of build sites for this Landscape has been reached. |
| 10 | Claims.MaxNumberOwnedRootClaims | You have reached your maximum number of Build Sites! You can increase this limit in the Build Site Management [U] window. |
| 11 | Claims.MaxNumberOwnedExpansionClaims | attachments maximum |
| 13 | Claims.InvalidLocationForInitiatingClaimAttachment | attachment must touch your site |
| 25 | Claims.InvalidClaimLocationCave | |
| 26 | Claims.InvalidClaimLocationBufferOverlap | …too close to another build site. Please try again. |
| 27 | Claims.InvalidLocationForInitiatingClaimPlacement_Caves | |
| 28 | Claims.UsersCantPlaceInNewbieZone | You can't place a Build Site here. Please travel to a non-Pioneer Landscape… |
| other (0, 5, …) | Claims.DefaultPlacementError | Unable to place build site. |

(Full texts: trace/code-strings.md, group Claims.) Live rules these texts reveal: placing a site needed upkeep
deposited first (result 4); the owned-site limit could be raised in Site Management (paid extra slots, result 10);
landscapes had a site cap (result 8).

**Gap:** the server answers every refusal with result 0 (→ generic "Unable to place build site.") plus its own chat
line. Live sent the specific code: 10 when the player already owns a site, 6 with no flag, 26 for overlap with the
buffer, 28 on Pioneer land, 3 for other bad spots. Also send RecievePropExclusionArea with the real exclusion areas
(spawn areas / Spire surroundings?) so the client's own pre-check matches.

## Reported issue (to check, not fix yet)

**Trace result (2026-10-12, console.log 21:31:18-21:32:07):** TestPlayer2 (who already owns site 20) pressed Place 16
times; each PlaceClaimRequest was refused with result 0. Per step 3 the client then showed "Unable to place build
site." and **put the player straight back into placement mode**, so every press repeated the refusal and the player
seemed stuck. (Our chat line "You already have a Build Site…" went to the chat window, easy to miss.) The open
ClaimManagementWindow re-sent ClaimManagmentRequest (0x1C) at each HUD state change; harmless. The fix (after the
trace): send result 10 (MaxNumberOwnedRootClaims) etc. so the client shows the real reason. Better still, refuse
before entering placement: check what the live client did on flag use when the owner limit is reached.


- 2026-10-12: placing the flag misbehaved while the TestPlayer2 test client was running (user closed it). Notes: one
  site per player (MaxClaimsPerOwner 1, refusal with chat text); overlap with other sites + 40 m buffer refuses
  silently; TestPlayer2's site 20 lies next to TestPlayer's site 9 on landscape 416.

## Client constants (trace/client-constants.md, Resources/StringHashToValue.txt — same file as the real install)

- **Site size `ClaimCursor.ClaimSize` = "210 180 210"** (x y z, local metres; global units are dm, local = global / 10,
  FUN_143ed86d0; voxel 0.4 m) = 525 × 450 × 525 blocks, matching the wiki's launch size. "70 100 70" in
  ToolClaimPlacement (FUN_1435aef40) is only the fallback default when the constant is missing.
  **Gap: our server uses 70 × 100 × 70 m (HalfSize 350 / HalfHeight 500 dm); live = 210 × 180 × 210 m
  (half 1050 / 900 dm).** `ClaimPlacement.PlacementBoundary.SideLength` = 210 too; `NewbieClaimSize` (Pioneer) =
  same. Vertical placement: `TerrainFollowHeightOffsetMin/Max` = -75 / 150 (box offset from the ground while
  following the terrain), `PlayerDepthCursorSnap` 20.
- **Buffer `ClaimsManager.BufferDistanceToNonfriendlyClaim` = 285** (FUN_142d22e00 → claims mgr +0x480/+0x488, clamped
  100-400, default 250), used by the client pre-check against sites of non-friendly owners (friendly = access).
  Whether 285 is a gap between boxes or a centre distance: verify at runtime (Frida on FUN_142d28700 / FUN_142cfd760
  during a placement) before the rebuild. Our server: 40.
- `Claims.PropLimit` = 20000 props per site (UpdatePropCategoryCount 0x5F / ClaimDialogs_PropLimits show counts).
- `Claims.ExpireTimeAlertSeconds` = 86400 (1 day: expiry alert).
- `ClaimsManager.UpkeepTokenItemId` = 34 (the item AddUpkeepFunds checks / pays with).
- `ClaimVendor.CurrencyItemId` = 1579 (Lumens), `ClaimVendor.ListingFeePercent` = 10.
- Site visuals: `InvalidClaimSurfaceBorderRadius` 32, fade 200-500 m, cursor model Char_ComplexObject_ClaimCursorFlag.adr.

## Live behaviour (wiki: design/wiki/pages/Build Sites.wiki, Build Site Management.wiki, Build Site Flag.wiki)

Source: https://landmark.fandom.com/wiki/Build_Sites , /wiki/Build_Site_Management (text describes launch, June 2016 —
the same build as 0.589, exe dated 2016-06-04; some notes describe beta).
- Building zone + **buffer zone** around it (shown red while placing). A new site may not be placed inside another
  site's buffer; two buffers may overlap each other. Nobody can build in buffer zones (natural land is kept for
  harvesting).
- Placement: right-click the flag → topographic map view; mouse moves the box, wheel raises / lowers it (Ctrl+wheel
  zooms), right-drag rotates the view, left click places → confirm window. Placing removes trees in the box.
- **Site slots:** new accounts 1 free root site (alpha/beta players 2); maximum 8; extra slots bought with Daybreak Cash
  from the Site Management window ("Get More Sites") — matches Claims.MaxNumberOwnedRootClaims text. 0.589 still
  uses flags (item 105, crafted at the Replicator for zero materials).
- **Delete** only on the landscape where the site is (Claims.Delete.ForeignClaim). Deleting returns everything
  placed (materials, props) and the flag to the inventory (overflow if full: Claims.DeleteFailedInventoryFull) and
  automatically saves a design of the whole site. "It costs nothing to delete and place again."
- **Upkeep / Active Days** (launch system): start with 7 days; every 2 days of logging in adds 1 day; maximum 90 days
  between logins. Three days before expiry a warning mail; on expiry the site is removed, a design of it is created
  and all resources and props are mailed back. (Before that, beta charged coins as upkeep: UpkeepManagementWindow,
  Claim.Upkeep.OnAddFunds.*, Claims.InsufficientFunds "deposit additional upkeep to stake a build site". 0.589 was
  built 2016-06-04 = the launch build, so Active Days is the system to implement; the coin UI is leftover — confirm
  what the 0.589 Site Management window shows for upkeep.)
- **Permissions** (progressive): No Touching (look only) → Play Around (use props, e.g. the Replicator) → Pillage and
  Plunder (take from chests) → Get Inspired (copy designs = "swaps") → Let's Decorate (move props) → full (build,
  delete). Set for the public, or per named player ("Specific Individuals"). The map shows sites in blue (you have
  friendlier access) or orange (No Access). Compare ClaimPermissionDefinitions / AccessLevelMappings in 0.589.
- Name, welcome message, tags (tags publish the site to the Gallery / Showcase); screenshots per site.
- Attached sites / expansions existed in beta (0.589 has Attached Build Site Flag 591, result codes 11/13) and were
  removed at launch when the base site grew to the old maximum.

## Rebuild status (2026-10-12, commits 76744da .. 3e6bcba)

Done (verified in the test client where noted):
- Launch size 210 × 180 × 210 m; old sites grown (log "Grew 2 site(s)"). Runtime: cursor box 210 × 180 × 210.
- Buffer = client rule (285 m from root-site centres); client pre-check now active (SendAllClaimBasicInfo + parent
  field 0 in records). Runtime: refuses within 285 / 390 m.
- Refusal codes at flag use (InteractionClaim) and placement (10 / 6 / 26 / 28 / 3). Runtime: flag use while owning a
  site → "You have reached your maximum number of Build Sites!", no placement loop (the reported flag bug).
- Delete reply codes (2 / 0x12 foreign landscape / 0x14 site game).
- Upkeep: Active Days (7, +1 per login day, max 90) with the client's letters (extended / expired + contents item).
  Runtime: 7 → 8 and the letter.
- Starting Location placed by default (NW corner); runtime: placed once.
- Site slots shown (PlayerMaxRootClaims 1, MaxRootClaimsAllowed 8) — Buy button opens Marketplace tag "Site Expansion".
- New sites default to Play Around (0x003). Prop use needs Play Around; moving / pick-up needs Let's Decorate; chests
  Pillage & Plunder (already).
- Props: refused in Play Mode (13, runtime-verified text), second game table (8), 50 chests / account (4), 20000 / site.

Still open:
- DONE 2026-10-12: design auto-saved on delete / expiry and materials of the owner's voxels refunded on delete
  (SiteDesigns.cs, from Voxel Vision ownership).
- Overflow inventory tab for items that don't fit (24 h) — Inventory round.
- "Site Expansion" store products (extra slots) — Commerce round; MaxClaimsPerOwner then per player (+ purchases).
- Exclusion areas (RecievePropExclusionArea, result 3): live data unknown (Chaos Cavern entrances: Claim.InvalidLocation
  mail "too close to a Chaos Cavern entrance" → sites near cavern entrances were removed with a design + mail).
- Prop category limits (UpdatePropCategoryCount 0x5F, error 12): per-category maxima were server data; none known.
- Snapshots (0x38-0x3C / 0x13-0x16): no UI in 0.589 (SnapshotPanel is empty) — skip.
- ClaimsBase 0x44 OnClientRateClaim: informational (sent after a vote reply).

## Module 4 rebuild pass (2026-10-13, archive claims-v1)

- Dispatcher FUN_142d1ff50 switches on sub−1 (case 0xf = sub 0x10 ResultResponse, case 0x10 = sub 0x11 prop
  exclusion area, case 0x12 = sub 0x13 snapshot save confirm ...).
- ResultResponse 0x10 {u32 result, string name, u64} (reader FUN_142d012c0): result 0x17 → the client's own chat
  line "Claim Operation Result: ERROR: Player or Guild could not be found under the name [%s]!". Special Access for
  an unknown name now answers this (was a server chat line). Verified with dev grantaccess + Frida.
- Table above is partly stale: 0x5F UpdatePropCategoryCount is sent (chests), 0x10 now sent.

## Site height limits (traced 2026-10-16, TASKS 6.47)
0.589 placement tool: FUN_1435afc20 reads ClaimPlacement.TerrainFollowHeightOffsetMax 150 → DAT_1451b4d00 and ...Min -75
→ DAT_1451b4d04; FUN_1435b0150 clamps the tool's height offset (+0x88, Ctrl+wheel) to them; FUN_1435b14a0 puts the box's
bottom at ground(centre x/z, FUN_1430a8040) + offset − ClaimSize.y × 0.5, so the requested centre = ground + offset ∈
[-75, 150] m. Server: Claims.HeightRefusal (result 3) with 2 m slack against TerrainHeights. 0.695: same constants, code
not re-read. Ultimate Power (0x11F) may switch Play / Edit (P5343): Lua Claims.ToggleAdventureStatus (0.695 FUN_1435e62a0)
sends the current claim's id, no client-side owner test.
