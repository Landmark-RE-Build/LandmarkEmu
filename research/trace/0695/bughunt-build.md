# Bug hunt: build sites, props, story tools, building (2026-10-18)

Branch `local/bugbuild` (worktree `wt-bugbuild`, from client-0.695 9280c50). The bugs were found by reading the code
and tracing each path. Each fix is its own commit. The whole solution builds with 0 errors and the same 32 warnings as
before (none in the files these commits touch).

Areas read: Props.cs and all of its partials (including the new Props.Stream region streaming, Movers, Platforms,
PlayModeVisibility and ClaimItems), Claims.cs (load, permissions, delete, expiry, relocate), SiteUpkeep, SiteContents,
ClaimInventory, Pioneer, AreaDefinitions, StoryTools (request handler, links, tick), PropSettings, RightClickMenus,
Building.cs, BuildingClipboard, ZoneTerrain.MayApply, TerrainEdits.RevertBox, LocalEnvironmentServer.WriteCells,
SiteDesigns, Gallery, WarpNetwork, PlayerManager enter/leave and the Teleport re-zone hooks.

## Fixed, ranked by severity

| # | Severity | Commit | Bug | Failure scenario |
|---|---|---|---|---|
| B1 | High (item duplication) | 6f22567 | Site delete or lapse refunds a mover pack once for each of its parts | Every pack part carries PackSpawner, and ReturnOf refunds the spawner's recipe for it. `ReturnInBox` (delete: refund straight into the bag) and `StowInBox` (lapse: mailed) refunded every part. A Magical Platform came back 3×, a Translocation Pad pair 2×. Placing movers, then deleting and re-placing the site (sites are free slots), made materials without limit. Fix: the whole pack goes on its first part (`RemoveOtherParts`) and is refunded once, as a pick-up is. Also fixed a latent bug: `returned?.AddRange(Remove(...))` skipped the removal itself when no list was passed. |
| B2 | High (permission bypass / theft) | 0869a7c | "Let's Decorate" could Send to Inventory, Recycle or Delete the owner's props, into the visitor's bag | `PickUp` and the prop menu used the decorate bit (0x08). The client's own rank texts [P] (ClaimPermissionStringMappings): Let's Decorate 576196/576943 "don't want to give full control … can help you decorate by moving props"; Ultimate Power 576197/576944 "can do anything … Anything!". Fix: `Props.MayRemove` / `CanRemove` = the owner, or PermissionBuild (Ultimate Power) on the site. Moving a prop still needs only Let's Decorate. A non-owner's pick-up still goes to their own bag, which is live behaviour (P3596). The prop menu drops Delete, Send to Inventory and Recycle unless the player may remove the prop. docs/protocol.md §11 updated. |
| B3 | Medium-high (permission bypass) | ebd1eeb | A removed site's Special Access stayed, and a new site could inherit it | `Claims.Remove` deleted the claim row but not its `special` entries or its `claim_permission` rows (that table has no foreign key). `Load` restarts ids at the highest stored id, so after the newest site is removed and the server restarts, the next site gets the same id and the old site's grants with it (Ultimate Power on a stranger's site). Fix: Remove clears them (in memory and in the DB). Load skips and deletes orphaned rows. |
| B4 | Medium (props out of sync) | 33a6eb2 | When someone other than the owner moves a prop, the owner never sees the new position | `Props.Update` treated the owner as "the mover" whose client already has the move. When a visitor with Let's Decorate moved the prop (or undid a move), the owner's client was skipped and kept the old place until a re-zone. The visitor got a needless remove + spawn under the gizmo. Fix: `PlacedProp.MovedBy` is set in `Save` / `UndoMove`, and only that player is skipped. |
| B5 | Medium (permission bypass) | 81e8c3b | Area Tool update checked only the first shape | `AreaDefinitions.Update` checked build permission at the first shape's centre only. A record holds up to 16 shapes, and every shape is a Linking Area trigger box (`BoxesOf` → StoryTools). Extra shapes could therefore sit over another player's site, and Links (teleport, damage, destroy voxels, dialogs) fired on that site's visitors. Fix: every shape's centre must be on a site where the player may build. A shape is at most 256 m and sites are at least 390 m apart centre to centre, so a shape centred on a permitted site cannot reach another site. |
| B6 | Medium (unbounded per-session state) | 1142c99 | `Claims.pending` kept WorldSession objects forever | `Claims.Forget` was never called. A disconnect between PlaceRequest and PlaceClaimConfirm left the whole session in the dictionary. Fix: called from `PlayerManager.Leave` and `ForgetNeverEntered`. |
| B7 | Low-medium (leak / stuck state) | 1c25543 | A disconnect during a prop placement left the unplaced prop and its replacement state behind | The unplaced prop stayed in memory for good, and `HasPendingPlacement` stayed true for that character (build mode opened by a bag placement never closed; the next re-zone waited 1 s). A Starting Location left with ReplacingBy / AwaitingPlacement set reopened the placement session on the cursor the next time that client asked for the prop's full data. Fix: `Props.LeftWorld` (from Leave) removes the unplaced props (they are not paid for until the first Save), clears the replacement and drops the edit session. It is skipped when a newer session of the same character is online (re-login kick). |
| B8 | Low (lost undo step) | db3848b | An undo or redo refused for materials lost its step | `UndoRedo` moves the step to the other stack before writing. When WriteCells refused for materials (nothing written), the step stayed moved: the next Ctrl+Z skipped it, and it could only be "redone" as a no-op. Fix: the step is put back, as the unreachable-server case already did. The undo stack is re-sent. |
| B9 | Low (needs a crafted client) | 740bd29 | Copy and Save Design checked Get Inspired only over the listed cells, but take props from the server-side box | Props for the clipboard or design come from `Props.InBox(SelectionBox)`. A request whose cell list was empty or too short copied a foreign site's props (with names, tints and sign text). Fix: `MayCopy` also checks the selection box. |
| B10 | Low (data loss, admin-only data) | adf8de4 | A lapsed site dropped its unplaced claim-inventory items | A delete returns them (`ReturnUnplaced`). A lapse (upkeep, character deletion, island closing) dropped them with the row. Fix: `ClaimInventory.TakeUnplaced` adds them to the lapse resource letters (the mail carries no tint). |

## Checked, no bug found

- **Props.Stream region streaming.** I traced placement (Show), settled moves and pack mates (Reconcile), pick-up / stow
  (Hide plus the tick's sweep of gone props), the arrival hold (SendEarly, then StreamIn skips what was sent early;
  Reconcile drops and re-adds early props consistently), re-zone (Forget in RezoneNow and ClientIsReady), drop
  hysteresis, the budget and pack union indexing. Every prop broadcast goes through ViewersOf. I found no prop that is
  never sent, no removal that is never sent, and no split pack.
- **Lock order.** Props.sync is never held while PlayerManager.sync is taken (ViewersOf / Hide / Update call
  PlayerManager outside the prop lock). StreamIn under PlayerManager.sync is a one-way order.
- **Moving platforms.** Viewers are pruned through `streaming` / InZone, the route always has ≥ 2 stops, and the
  PoseAt cycle is > 0.
- **Revert after a site expires.** RevertBox and the claim box are both in global decimetres, and props are converted
  to local metres /10 the same way in Pack and Delete. The units are consistent.
- **Paste, design placement and Player Studio ownership checks, links (CanEdit on both ends), and the instanced-game
  editor (CanEdit).**

## Not fixed (leads for the coordinator)

1. **World DB writes can land out of order.** `CharacterDatabase.Save` runs each change set on its own context
   concurrently. For example, a prop `Persist` (Any → Add) followed quickly by another Persist can double-insert and
   fail, losing the later state. A `Remove` that commits before an in-flight insert brings the prop back after a
   restart. PropSettings.Flush against PropRemoved can leave orphan setting rows, which a reused prop guid would
   inherit (nextGuid restarts at the highest stored prop id). Fixing this properly needs a serial world-save queue,
   which changes threading for every SaveWorld caller, so I left it out of a bug-hunt commit.
2. **Claim id reuse also carries other per-site data keyed by id**: gallery comments, contest tags held elsewhere,
   `Unlocks.SiteContents` (the lapse key), and site screenshots. A persistent high-water mark for claim ids would close
   all of these at once. B3 closes only the permission part.
3. **Props placed by other players stay behind on a deleted or lapsed site.** `ReturnInBox` and `StowInBox` take only
   the owner's props. A builder's props remain on unclaimed land (their owner can still pick them up). Live's rule is
   not found yet.
4. **Old Pioneer site move.** `Claims.Relocate` leaves the old site's props on the unloaded zones 417-421. This is
   documented as intended (a design is saved), but those props are unreachable and never refunded. Site designs
   (`SiteDesigns.Save`) also carry no props.
5. **The warp request is not checked against where the player stands.** `WarpNetwork.Warp` treats an unknown `from` as
   the hub, so a crafted client can warp from anywhere for the base cost. Live's rule (must the player be at a node?)
   is not traced yet.
6. **No server clamp on a prop Save's scale.** PropDefs MIN_SCALE / MAX_SCALE are 0|0 for most props, so the client
   gizmo's limits are the only limit. A crafted Save can make huge props. The live limit is not traced.
7. **No exception isolation in StoryTools.Update.** An exception in one site's Fire stops the whole tick (including
   pending resets) for every later site on every tick, until the cause goes away.
