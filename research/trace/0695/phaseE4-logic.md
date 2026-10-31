# Phase E, part 4: correctness review of the server code since the last bug hunt (2026-10-26)

Slice: a logic review, not a protocol check, of `git diff 1f2d53bd..db90a5f -- LandmarkEmulator.*` (tests skipped).
1f2d53bd is `git rev-list -1 --before=2026-10-18T23:59 client-0.695`, the base of the 2026-10-18 bug hunts
(trace/0695/bughunt-*.md). That is 92 commits and 136 non-test files. Today's commits (40bb53f..db90a5f, 8219f7c,
b3fccd0, b4ac77d) were read line by line. The rest was read by file, in this order: items and currency, saved data,
and code that runs often. Read-only: nothing in the repository was changed. Scratch is in
`...\scratchpad\e4-logic\` (only full.diff).

Markers: [P] = proven from the server code (file:line). [I] = inferred, mostly about how the client reacts. The
verdicts are DEFECT (a server logic bug) or OK-NOTE.

## 1. Findings (ranked by player impact)

### F1. Build edits that carry NewVersion 0 no longer change the cell's version, so others and later logins never see them (b3fccd0). DEFECT, high

- **Where:** `LandmarkEmulator.EnvServer.Core/Terrain/TerrainEdits.cs:1914`,
  `overlay.Version = clientVersion != 0 ? clientVersion : overlay.Version;`. This came in with org #12 (b3fccd0).
  Before it, the code did `overlay.Version + 1`. The only caller is `LocalEnvironmentServer.ApplyEdit`
  (LocalEnvironmentServer.cs:102). It passes 0 when the edit tail has no versions, and the 0.695 Add tool sends 0
  ("seen on 0.695 Add-tool clicks").
- **What breaks.** The voxels change but the layer version stays the same. Every consumer keyed by version then
  treats the cell as unchanged [P]:
  - **Remote Environment Server (the Docker split).** The world's CellsUpdate body cache matches on versions only
    (`RemoteEnvironmentServer.cs:487`, PayloadCache.TryGet). The Broadcast right after the edit
    (`ZoneTerrain.cs:121`) gets the **old cached body**, so players in range don't see the edit.
  - **A brand-new cell edited only this way stays at version 0.** In that case:
    - the remote version index deletes all-zero entries (`RemoteEnvironmentServer.cs:270`), so `Versions()` is null;
    - `BuildCellsUpdates` then answers null or empty, and the zone-in manifest built from the index leaves the cell out.
  - **Out-of-range viewers and catch-up notes.** Manifest notes are sent only for a non-zero version
    (`ZoneTerrain.cs:135`, `ZoneTerrain.cs:279`), and only a changed version makes a client fetch the cell again.
  - **The builder's own note.** b3fccd0's new manifest note to the builder is skipped when the version is 0
    (`Building.cs:821`).
  - **Coarse LOD copies.** TerrainLod rebuilds only on a version difference (`TerrainLod.cs:187`), so distant
    terrain never shows the edit.
- **Failure scenario.** World running with the envserver split. Player A uses the Add tool on new cells of their
  site, and the client sends NewVersion 0 each time. Player B, standing 20 m away, keeps seeing the old terrain (cache
  hit) [P]. B relogs: the manifest has no entry for those cells, so B's client draws the generated ground [I strong:
  the client only fetches cells listed with a version, docs/terrain.md:98-105, 160-161]. After a restart A sees it the
  same way, unless A's own disk cache still holds the cell. In-process the persistence still happens (the overlay is
  marked dirty and saved), but the manifest carries version 0 and no note goes out, so other clients don't fetch the
  cell [I].
- **Fix.** Never keep the old version when the content changed:
  - when `clientVersion == 0`, give the layer a fresh non-zero version: `overlay.Version + 1`, skipping 0, or better
    a CRC32 of the server's own compressed layer;
  - keep b3fccd0's manifest note, so the builder learns the new number.
  - If the original complaint comes back (the client keeps sending BaseVersion 0 on the next click, so the edit is
    flagged stale), fix it in the stale check instead of freezing the version: remember the last editor and the
    version their NewVersion-0 edit produced, and accept BaseVersion 0 from that player for that cell.
    `LocalEnvironmentServer.cs:83`, the stale check.
- **In-game check:** yes. With the split layout: A uses the Add tool, B watches from 20 m and then from 300 m, B
  relogs, then restart the world. Also confirm lead L-E4-2: whether the client takes a ModifiedCellManifestUpdate
  version as its new base for a cell it is editing.

### F2. World-NPC question answers share id numbers with Story Tools questions and are not tied to the player asked. DEFECT, medium (rewards)

- **Where:**
  - `Game/Npc/WorldNpcs.cs:44` and `:331` (`nextQuestion` starts at 1);
  - `Game/StoryTools.cs:1672-1680` (its own `nextQuestion`, also from 1);
  - `Network/Handlers/PropHandler.cs:108` (WorldNpcs gets first try at every PropPacketDialogResponse);
  - `WorldNpcs.cs:184-215` (`DialogResponse`).
- **What's wrong** [P]:
  - `PendingQuestion` keeps no recipient, and `DialogResponse` accepts anyone not yet in `Answered`.
  - It does not re-check the chain lock (`RequiresNpcTurnedIn`) or the distance.
  - The two counters overlap.
- **Failure scenario.** Player P answers Story Tools question 3 on a Play Mode site. At the same time the Herbalist
  has a live world-NPC question 3, asked of someone else within its 13-17 s window. `WorldNpcs.DialogResponse`
  consumes the answer, so P's story answer is lost: the site's "accepts / denies" Links never fire. P gets "met" and
  "accepted". For a talk-only quest, `QuestRequired ≤ 1`, P also gets an instant `CompleteNpcQuest`: the Herbalist's
  15 Lumens and "turned_in". That skips the Fungus Scout chain lock, for an NPC P never spoke to. The rewards
  themselves can't be duplicated (`CompleteNpcQuest` checks `IsComplete`). The harm is the lost story answer and an
  unearned, out-of-order quest.
- **Fix.**
  - Give both systems one id allocator, or put world-NPC ids in their own range (for example `1UL << 62 | n`).
  - Store the asked player's guid in `PendingQuestion` and require `session.PlayerGuid` to match.
  - In `DialogResponse`, check the chain lock again before offering or completing.
- **In-game check:** no, the code alone shows it. Optionally re-test the Scout and Herbalist dialogs after the fix.

### F3. After a bag placement is settled, a later bag placement's Escape closes the build mode the player is still using (b4ac77d). DEFECT, low-medium (PlaceOpensBuildMode defaults to on)

- **Where:** `Game/BuildModeHolster.cs:145-163`. `ClearPlacing(scheduleClose: false)`, used by the new
  `PlacementSettled`, removes the `placing` mark only. The session stays in `openedByPlacement`, which only
  `CloseAfterPlacement` or the player's own build-mode-off clears [P].
- **Failure scenario.**
  1. A prop from the bag opens build mode, and the first Save settles it.
  2. As b4ac77d intends, build mode stays open and the player keeps building in it for minutes.
  3. They right-click another prop item: `OpenForPlacement` sees `openedByPlacement` and returns early.
  4. They press Escape to cancel it: `PlacementEnded` finds `openedByPlacement`, sets `closeDue`, and 1 s later
     `CloseCode` turns build mode off under them.
  Under the old rule (close at the first Save) this couldn't happen.
- **Fix.** In `PlacementSettled`, once the first Save has kept build mode open on purpose, it is the player's from
  then on: also `openedByPlacement.Remove(session)` and `openPending.Remove(session)`.
- **In-game check:** yes, a quick one.

### F4. Admin entries from the auth database: a failed read is not cached, so every admin check retries the database on the world tick. DEFECT, low-medium

- **Where:** `LandmarkEmulator.Database.Auth/AuthDatabase.cs:331-367` (`GetAdminEntries`). The catch at :355
  returns the old cache but leaves `adminEntriesCachedAt` as it was [P].
- **Callers on the world tick:**
  - `AdminMonitor.cs:120`, every 2 s for every online player;
  - `WorldSession.cs:1300` (SendSelf);
  - every admin command and the web pages.
- **Failure scenario.** The auth database is restarted or unreachable (the project note "restart auth db before
  up.sh"). Every `IsAdmin` call then tries to open a MySQL connection. With N players that is N attempts every 2 s,
  each blocking for up to the connect timeout, and the world tick stalls repeatedly until the database is back. If the
  `admin` table migration is missing on that database, the failure is quick but logs a Warn line per call.
- **Fix.** On failure, set `adminEntriesCachedAt = now` so the next try is a minute later, or refresh on a
  background task and only read the cache on the world tick.
- **In-game check:** no.

### F5. Emotes delayed by the held-item put-away run on a thread-pool thread. DEFECT, low (setting EmotesPutAwayHeldItem defaults to off)

- **Where:** `Game/EmoteHolster.cs:135`. `Then` uses `Task.Delay(PutAwayLead).ContinueWith(play)`. `play` is
  `Emotes.Emote`, `PlayDeadNow` or `PlayAnim` (Emotes.cs:594-624). `Emote` calls `Achievements.Emoted`, which calls
  `Progress`, which can complete goals: `GiveItem` rewards, inventory changes and achievement saves [P].
- **Failure scenario.** The setting is on and a player emotes with a tool in hand. 1.5 s later the emote and its goal
  progress run on a pool thread, alongside the world tick handling that player's inventory: item moves, the hotbar,
  `TakeItem`. Inventory and achievement state are not written for concurrent use. The repository already moved the
  same pattern to the tick for BuildModeHolster and Consumables (review 2026-10-18b #6).
- **Fix.** Run the delayed play on the world tick, as `Abilities.Later` does or as a timed `session.EnqueueEvent`.
- **In-game check:** no.

### F6. A Spire's saved spot is re-checked at every start, and a failed survey moves the Spire. DEFECT, low

- **Where:** `Game/Spire.cs:276-284`. A "merge" marker is kept only if
  `Survey(...) && deckSpread <= MaxDeckSpread`. Otherwise the deck carve is reverted, the marker is renamed
  `.steep-reverted`, and `SpotFor` picks a new spot [P].
- **What triggers it.** `Survey` also returns false when the heights are missing (`TerrainHeights` reads
  `TerrainHeights{zone}.bin` lazily, and heights ship as a separate release asset) or when any of the 81 samples is
  below `DryLand`.
- **Failure scenario.** The world starts without that landscape's heights file, or with a re-baked one.
  - The landscape's Spire is "re-picked": the home continent falls back to `OnGround(0, 0)`, and other landscapes get
    no Spire at all.
  - Its deck carve is reverted.
  - On the next start with heights it may land somewhere else. `SpotFor` skips spots near build sites that exist
    now, so the result can differ from the original spot.
- **Fix.** Re-pick only when `Survey` succeeded and reports too steep. When it can't survey, keep the marker. A
  placed Spire should never move without an explicit admin action.
- **In-game check:** no.

### F7. World-NPC flags are saved by unordered `Task.Run` writes and loaded synchronously. DEFECT, low

- **Where:**
  - `Game/Npc/WorldNpcs.Flags.cs:63`: each `SetFlag` is its own `Task.Run(SaveNpcFlag)`;
  - `SaveNpcFlag` in CharacterDatabase.cs:335 does find-then-insert;
  - `ClearFlags`, the dev reset, deletes synchronously;
  - `LoadCharacter` (WorldSession.cs:718 / 776) replaces the in-memory flags from the database at login and on a
    reattach or transfer [P].
- **Failure scenarios** (rewards stay single: `CompleteNpcQuest` checks `IsComplete`):
  - (a) `npc reset` right after a flag write: the in-flight insert lands after the delete and the flag comes back.
  - (b) A cross-world transfer or relog right after "accepted" / "turned_in": `LoadCharacter` reads before the
    insert lands, so the NPC offers the quest again.
  - (c) Two writes of the same key at once (for example "met" again after a decline and re-ask): both insert, one
    fails on the unique key, and an error is logged.
- **Fix.** Save through the character database's ordered save path, the one `Achievements.Save` uses, instead of
  `Task.Run`. Let `LoadCharacter` keep in-memory flags newer than the database rows.
- **In-game check:** no.

### F8. The world NPCs' fungus seeding uses the dev-spawn guid rule, which reuses live guids. DEFECT, low

- **Where:** `Game/Creatures.cs:3982`, `Guid = ... | (uint)(200 + existing.Length)`, now reached at every start
  and every `npc reload` through `Creatures.SeedNear` (`WorldNpcs.cs:126`) [P].
- **How guids repeat.** Seeded fungi are not story creatures, so they respawn. But story creatures in the same
  region are removed for good when they die, despawn or are reloaded (Creatures.cs:600, :1764, :3579), as are
  creatures hit by `DevKillNear` (:3397). Each removal shrinks `existing.Length`. Story creatures meanwhile take the
  lowest free index (:3531). So `200 + Length` can name an index that is still in use. Above 55 entries the index
  spills into the region-key bits.
- **Failure scenario.**
  1. One region holds a Story Tools creature S at 200, the Scout at 201 and seeded fungi at 202-206.
  2. S dies and is removed: the array is [201, 202-206], length 6.
  3. An admin runs `npc reload`. The Scout is despawned (length 5) and respawned at free index 200 (length 6).
  4. `SeedNear` places a new fungus at 200 + 6 = 206, while the old 206 is alive.
  5. The two creatures share a guid: wrong `Find()`, hits on the wrong creature, broken client entity state.
  - Also, `DevCommands` runs `npc reload` once per target (DevCommands.cs, the "npc" case). With target "all" that
    is N respawns and 5×N more fungi, which never go away.
- **Fix.**
  - In `Place`, pick the first free index 200-255 as `SpawnStory` does (Creatures.cs:3527).
  - Run `npc reload|reset` once, not per target.
- **In-game check:** no.

### F9. The Spire / My Site travel cooldown starts before the travel is known to happen. DEFECT, low (TravelCooldownSeconds defaults to 0)

- **Where:** `Game/Abilities.cs:541`. `StartTravelCooldown` runs before the 187 checks and before 186 finds a
  site. A 186 with no site falls through to "has no destination yet" (Abilities.cs:634) with the cooldown already
  running [P].
- **Related.** `MiscHandler.cs:557` writes the travel cooldown into ability 186's caster info. Its own comment says
  186 is also "the build-mode tool's default ability (asked for every second inside a claim)". If the build tool
  ever casts 186, a non-zero setting would put that tool on cooldown too [I]. See lead L-E4-3.
- **Fix.** Start the cooldown only when the `Later(...)` travel is actually scheduled. Confirm that 186 is never
  cast by the build tool, or give the caster info a cooldown only in the travel context.
- **In-game check:** only if the setting is used.

### F10. Placed-design lookup: every start and every placement bumps versions of build cells nobody is told about (8219f7c). DEFECT, low

- **Where:**
  - `TerrainEdits.cs:2317-2331` (`TouchBuildLayers`) raises the build-layer version of every cell with an overlay
    in an instance's box, both for instances replaced and for instances added;
  - `TemplateInstances.PushAll` (HostedService.cs:129) starts from an empty lookup, so after every world start it
    re-touches every cell of every placed design;
  - `TemplateInstances.Record` pushes with `broadcast: false` [P].
- **Failure scenario.**
  - After each restart, every client re-downloads every cell under every placed design (cost only).
  - The real slip: a design is placed next to the player's earlier build, inside the design's box but outside the
    paste's cells. Those cells get a new version with no note to anyone. The player's next brush edit in such a cell
    arrives with the old BaseVersion and is refused as stale ("not applied", the cell is resent). That happens once
    per such cell.
- **Fix.**
  - Bump only cells whose lookup blob actually changes: compare `BlobFor` before and after, or limit it to cells
    where the placer owns voxels.
  - Send the touched cells that the paste didn't write (a manifest note or a broadcast).
  - At start-up, don't bump at all when the in-memory lookup is merely being rebuilt.
- **In-game check:** yes, a short one: place a Player Studio design beside an existing build, then brush the old
  build next to it.

### F11. OK-NOTE: the Player Studio reviewer "overrule" rule can fire retroactively

- **Where:** `Internal/Commerce/StudioRules.cs` `Overruled`, `Game/Commerce.cs` `OverruledRejections`,
  `CommerceStore.cs:481` [P].
- **What happens.** A rejection counts as overruled when the product is listed with fewer approvals than the
  *current* `PlayerStudioApprovals`.
  - Raising that setting turns every rejecter of a product peer-listed under the old threshold into an "overruled"
    reviewer.
  - With the default allowance of 1 and no time limit, that bans them from reviewing for good.
  - The same happens if a resubmitted product's approvals were reset and staff then list it.
- **Fix if wanted.** Record a staff-listed flag on the product when staff decide, and count only those.

## 2. Checked and fine

- **40bb53f** guild ChangeMemberRank tail: all four writers, including both leadership-transfer writers; self and
  equal ranks are refused before a transfer; `GuildKicks.Resolve` checks by guid or name.
- **cc1c949** `PlacementSpawnPoint` / `SiteHeightAt`: units are local metres
  (`(c.MinY - origin.Y) / 10` matches `ClaimAt`'s `ToGlobal`); `leavesItsSite` works with null sites (props off
  any site compare null to null) and skips unplaced props.
- **e4ef5d8** `SetTime`: owners get `OwnerPermissions` (bit 0x10 included); a claim id that names nothing returns
  quietly; the refusal sends the stored time back.
- **1e2d3dc** `Watchers()` is read under the lock, viewers are deduplicated, and re-zoning watchers are skipped.
- **5cee819**: titles are announced only on completion (Achievements.cs:1234-1236), never again at login.
- **540fa9d**: the endings are added only to a 255-character text with the exact cut ending.
- **d4b1e7a**: defaults only.
- **8380fb5** `HasExtra`: every bag, equipment or overflow item sent by ItemAdd has OwnerType 1 and a container
  below 12 (containers 0/1/2/5/7/8); chest and mail records use `WriteRecord`.
- **6686043**: only the name check changes.
- **8219f7c** `TemplateInstanceLookup`:
  - the voxel index and bit-set order match the documented reader;
  - the signed varint −1 means no weights; the u16 indices stay ≤ 63,999;
  - mapping zone 0 to home agrees with TerrainEdits and VoxelOwnership;
  - no nested locks: `BlobFor` runs before the store lock and ownership has its own;
  - remote resend on reconnect; `GuidOf` is stable across restarts; the 0x12 body length check is right.
- **b3fccd0 paste:**
  - the turned box and `PasteOriginAdjust` net to zero for `PlaceDesign`;
  - the Player Studio box (BuildingClipboard.cs:526-545) matches the voxel map on flipped axes too (voxels at
    b + Dot(m, s) with no +1; the +1 is only for vertices and props).
- **TerrainEdits / VoxelOwnership dirty indexes** (6b4abbc / 5cbb2c2):
  - every Dirty set goes through `MarkDirty`, and a failed flush marks the cell dirty again;
  - `PoiKeysOf`'s count cache is safe because no store ever removes a cell;
  - `RepairInteriorAir` runs after `VoxelOwnership.Load`.
- **PlayerManager stream hold:** ending twice returns null the second time; Leave and an overdue hold release it;
  re-zone resets the pending sends; `RunHeldSends` is skipped during a re-zone.
- **HeroicMovement** (Mover maps: locks, cleanup on leave, `IntroduceTo` resets); **Health** (`knownLife` and
  `revivable` cleaned on leave; `CatchUpLife` once a second); **DeadBodies.Forget**; **Crafting** (`openReach`
  cleaned on every exit path, Spire station positions).
- **Lumens:** Mail and Trades check them at compose, at offer and at the final trade check; feedback mail
  `ToGuid` resolves to a deleted or missing character as null.
- **Commerce:** the overrule count is cached and cleared on staff approval; a purchase with free studio storage.
- **Templates:**
  - `AdoptAccountDesigns`: no character-restore path exists, so the move is safe;
  - `FreeOfStorage` and the storage bool.
- **SiteUpkeep:** Pacific day numbering with the `today <= last` guard.
- **WorldSession:** `TakeItem` on a worn last stack sends the slot unset; `CastKeySeed` is non-zero and per
  session; the off hand follows the main hand.
- **StoryTools** `ReplaceLink`: the old Link is restored on refusal, and its database row is deleted only after the
  new Link is stored.
- **Also checked:** TemporaryAppearance; RightClickMenus; tooltip by chest, inspect or mail; the hotbar
  `bindsDefinition` rule; LandscapeContinents, OreVeins and CavePieces paths; the guild tags (remote state diff,
  in-process create / join / leave / disband).
- **ClientConfig.ini writer:** atomic temp file and move, `.orig` kept once.

## 3. New leads

- **L-E4-1 (F1):**
  - Need: how the 0.695 client treats a manifest entry or ModifiedCellManifestUpdate whose layer version is 0, or
    equal to its cached one.
  - Searched: docs/terrain.md 96-163 and 795-806 (it only asks for cells "it holds no data for, all versions 0").
  - Try next: Frida on the manifest handler (TerrainCell 0x0A reader) and FUN_143f56980 (the request builder) with a
    cell edited by another player.
- **L-E4-2 (F1 fix):**
  - Need: does a ModifiedCellManifestUpdate for a cell the builder is editing update the version that goes out as
    the next ApplyDiff BaseVersion (b3fccd0's assumption), and when does the client really send NewVersion 0?
  - Searched: Building.cs:815-823 (comment only).
  - Try next: tools/brush hooks (org) on FUN_1430b0510 and the manifest-update handler.
- **L-E4-3 (F9):**
  - Need: is ability 186 ever cast (CastAttempt) by the build-mode tool, or only asked for as caster info?
  - Searched: the MiscHandler.cs:540-560 comments.
  - Try next: grep the opcode stats and server logs for "Cast 186" outside travel.

## Summary

Of today's commits, the one that matters is org #12 (b3fccd0), with a TerrainEdits change that the fix aimed at
paste placement doesn't need. Edits sent with NewVersion 0 keep the old version, so every version-keyed path treats
the cell as unchanged:

- the remote body cache;
- the version index, which drops version-0 cells;
- manifest notes;
- LOD copies.

Ordinary Add-tool building can then stay invisible to other players and after relogs (F1, needs the in-game check
L-E4-1/2). Two newer features carry rule or ownership slips:

- world-NPC answers can be taken from Story Tools and from other players (F2);
- after org #13, a settled bag placement's build mode can still be closed by a later Escape (F3).

The rest are robustness issues:

- a failed admin-table read is retried on every check, so the world tick stalls while the auth DB is down (F4);
- a pool-thread emote (F5);
- the Spire can be re-placed when the heights are missing (F6);
- unordered NPC-flag saves (F7);
- duplicate creature guids from seeding (F8);
- the travel cooldown starts too early (F9);
- untold version bumps under placed designs (F10).

Today's protocol commits (guild rank tail, SetTime, site-game watchers, SendSelf fields, upkeep letters, titles, world
names) are logically sound.
