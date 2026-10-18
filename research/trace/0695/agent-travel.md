# Agent report: Game Finder across worlds (L31, features audit #29) and the travel queue (L60)

Branch `e-travel` (worktree `<workspace>\wt-travel`), on top of `client-0.695` bd03023. Commits:

| Commit | What |
|---|---|
| 1c039b1 | Game Finder across worlds: offer and join a match on another world (L31) |
| 22e8868 | Travel queue: per-landscape capacity with the client's own queue (L60) |
| fdc3186 | Travel queue: dev `landscapecap` prints to the log only |

Built: Internal, GatewayServer, SocialServer, WorldServer and the whole solution (`dotnet build LandmarkEmulator.sln`),
0 errors, no new warnings. Nothing was run (no Docker, servers or client).

---

## 1. Evidence (0.695 client)

### 1.1 MatchmakingUpdate 0x10D / 0x1A (L31)

- Reader FUN_1434d7270: u16 → +8, u32 sub → +0x10, **u64 → +0x18**, u32 ×5 → +0x20..+0x30, u64 → +0x38.
- Handler FUN_1434e9a50 reads only +0x20..+0x38 (counts, SearchState, flags). The packet object is a stack object
  of the dispatcher; nothing else sees it. **So the u64 at +0x18 is read by nothing in 0.695**: it cannot have carried a
  world, game or anything the client acted on. We keep sending the player guid there (as the request carries it) [I].
- SearchState 1 ("Match found!"): clears manager +0x402, looks up key 0x565a5445 in the table at
  DAT_145431198+0x2258 and passes the value to FUN_1431f7e90 (DAT_145431358 — a sound play; the key's name is not
  resolved yet), prints "You've been queued for a match! Match details:" + FFA / Teams / PvE / PvP from flag bits
  2 / 4 / 8 / 0x10. **Nothing else** — no join, no travel, no world.
- The only client path into a game the server offers is the game queue entry: S→C Request type 5 (FUN_1434e93e0)
  stores the game guid in the manager's own set (+0x3a0) and builds the Game Action Queue entry from the packet alone
  (no lookup of the game in the world), so an entry for a game of another world is shown like any other; its "Join
  Now" sends Request 6 value 1 with that guid.
- Conclusion: live's client gives no world for a match; the cross-world move must be the server's. Ours: offer the
  other world's game through that queue entry, hand the player over on Join Now.

### 1.2 Travel queue (L60)

- **What sets the queue object's +0x30 / +0x34:** the object is the client teleport manager DAT_145431448.
  FUN_143173750 (Lua Teleport.TeleportToLocation = FUN_1435e7ff0, luabind_695) writes **+0x30 = world, +0x34 = zone**
  and then sends TeleportRequest 0xFC/4 {world, zone} (it also calls FUN_142ca8cc0 when both differ from the current
  world / zone, client +0x378cc / +0x378d0). No other writer. FUN_143173a90 (0xFC/6), FUN_143171570 (0xFC/7) and the
  GAQ accept FUN_1433fcec0 send those two u32 and clear +0x30 (u64). So 6 / 7 carry **the world and zone of the
  player's last TeleportRequest**.
- **PacketTeleportPostQueueNotify 0xFC/5:** reader FUN_14316daa0: u16, i8 sub, u32 +0x18, u32 +0x1c, u32 +0x20,
  u32 +0x24. Handler FUN_1431726c0: text = +0x24 if non-zero, else +0x1c; calls Lua
  GameEvents:OnPlayerTeleportPostQueueNotify(text); when not handled (no handler in UI/Default Lua or ScriptsBase.bin)
  it looks the text up (DAT_145431178 vtable +0x10) and calls **FUN_1433db960 → FUN_1433db970 → FUN_1433d8c20**, the
  constructor of **GaqItemTeleportToZoneRequest** ("GAQTeleportToZone", vtable 144767230 in rtti_695.tsv). +0x18 and
  +0x20 are unread. It does not touch +0x30 / +0x34.
- **The GAQ item's buttons:** vtable 144767230 slot 3 = FUN_1433dc5b0 → FUN_1433fcec0(0) → FUN_143171570 (0xFC/7
  TeleportCancelQueue); slot 4 = FUN_1433da3b0 → FUN_1433fcec0(1) → inline 0xFC/6 TeleportToQueuedLocation {+0x30,
  +0x34}, then FUN_14368acd0 (closes the queue dialog, OnQueueShow(0)). refscan: 0x1433da3b0 ← 0x144767250,
  0x1433dc5b0 ← 0x144767248.
- **The dialog:** 0xE0 → FUN_143171ae0 (dispatcher case 0x67, with DAT_145431448) → FUN_14368afa0(ds, **2**, place,
  isMember, members, non-members, member s/place, non-member s/place); first call runs OnQueueShow(1) (ScriptsBase.bin
  has OnQueueShow). LoginQueueDialog.swf (re/as3): Type "2" → text "Ui.LocationFullQueueMessage" (561206 "This location
  is currently full..."), buttons Cancel → OnCancelTeleportQueueButtonClicked → Teleport.CancelQueue (0xFC/7) and
  "Close" → OnCloseQueueDialogButtonClicked (only closes the window). FUN_14368acd0's callers: 0xFC/6 and /7 senders,
  the login connect (part_0084 ~2382), CharacterSelect.CancelQueue (FUN_1435dab90), the data source ctor. **Nothing
  from the server closes a travel queue dialog.**
- **How live's queue ended:** server → PostQueueNotify → GAQ item → the player's Accept → 0xFC/6 → the server sends
  them; or Decline / Cancel → 0xFC/7. That is the only exit the client has.
- Text for PostQueueNotify: no "place free / ready" text found in text_695 (searched queue / warp / teleport /
  ready / available wording). Candidates 62065 "Warp to [*zone*]?" (in PS2's zone-queue block 62064-62089, "Landscape
  Population", "%0 IS FULL", "CONTINUE PLAYING") and 559442 "Teleport to [*zone*]?" carry a tag the notify can't fill.

---

## 2. What was built

### 2.1 Game Finder across worlds (L31)

- **Internal (ours):** `MatchmakingGame` gains `NameId` / `Name`, sent in an optional trailing names section of
  `MatchmakingGames`; `MatchmakingUpdate` gains optional trailing `OtherZone`, `OtherFlags` (client kind bits only,
  `MatchmakingMessages.ClientKinds`), `OtherNameId`, `OtherName`. `HandOver` and `AttachConnection` gain an optional
  trailing `JoinGame` (u64). All reads tolerate the old shorter form.
- **Social service** (`MatchmakingService`): the other-world match now carries the game's landscape, kind and name.
- **Gateway** (`GatewayClient.BeginHandOver` / `TakeJoinGame`, `ZoneBackEnds`): passes `JoinGame` from HandOver to the
  transfer's AttachConnection, like `ArriveNear`.
- **Zone** (`InstancedGames.Finder.cs`, `CrossWorld.cs`, `GatewayLink`, `WorldSession`):
  - `FinderServiceUpdate`: a named other-world match on a world this player can reach (`CrossWorld.CanReach`: MultiWorld
    gateway lists it, the player came through the gateway) → `OfferRemote`: search ends (here and with the service),
    "Match found!" with the game's kind bits, queue entry (Request type 5, place 0 = Join Now) for that game, a system
    line. Unreachable world → told once, as before.
  - `HandleRemoteOffer` (called before the Request switch in `InstancedGames.Handle`): Request 6 value 1 (or 3) with
    the offered guid → `CrossWorld.JoinGame` → `CrossWorld.Travel(..., joinGame)` → B.10 hand-over; value 0 / type 7 →
    offer dropped (type 4).
  - Offer expiry 30 s (`RemoteOfferTime`, the rule sets' MaxQueueJoinTime default [I]) → type 4 + line.
  - Arrival: `WorldSession.ReattachRezone` → `InstancedGames.ArriveToJoin` (before the friend path): re-zone beside the
    Game Table on its landscape (ClientBeginZoning with this world's id); once `RezoneTarget` clears, `TickArrivals`
    calls `JoinGame` (full game → its queue). Game gone / switched off → line + the landscape's usual arrival.
  - `InstancedGames.Queue.cs`: `SendQueueRequest` split into an overload taking the fields (for the remote game). No
    rule-set code touched (e-admin2's area).
- `CrossWorld.Travel` now returns bool.

### 2.2 Travel queue (L60) — `Game/TravelQueue.cs`

- Setting **`LandscapeCapacity`** (WorldServer.json / Docker `LANDSCAPE_CAPACITY`, default **0 = no limit**; added to
  docker/world/docker-compose.yml and .env.example). Dev **`landscapecap <n> | full | config | text <id>`** (logs the
  lines and held places).
- Travel window TeleportRequest 0xFC/4 for a landscape of this world (`Travel.cs`): `TravelQueue.Request`; full (players
  on it + places offered and not taken ≥ capacity, or others already waiting) → queued, 0xE0 sent.
- 0xE0 to everyone waiting on change and every 10 s: place in that landscape's line (members first [I]), members /
  non-members counts, 60 s per place [I], is member; same layout as the login queue's tunnel type 3 ([P]) plus four u32.
- Once a second: free places go to the head of each line → **PostQueueNotify 0xFC/5** `{0, text, 0, 0}` with text
  `ReadyText` = **565560 "Teleport" [I]**; the place is held 60 s [I].
- `LocalWorldChannelService.TeleportQueueRequest` → `TravelQueue.Handle`: **0xFC/6** with a held place for that zone →
  travel (`Landscapes.TravelTo`, or `CrossWorld.Travel` for another world's id); without one → a fresh request.
  **0xFC/7** → out of the queue / place dropped.
- Players still re-zoning get no status / offer that tick. Disconnected players are dropped.
- Docs: `docs/protocol.md` (TeleportBase subs 5 / 6 / 7, 0xE0 travel queue), `docs/instanced-games.md` (new Game
  Finder section). LoginQueue.cs / Travel.cs / IWorldChannelService comments point at TravelQueue.

---

## 3. Settings

| Setting | Where | Default | Meaning |
|---|---|---|---|
| `LandscapeCapacity` | WorldServer config, Docker `LANDSCAPE_CAPACITY` | 0 | players per landscape; 0 = no travel queue |
| dev `landscapecap` | dev_commands.txt | — | runtime override (`full` queues every arrival), `text <id>` sets the notify text |

Cross-world Game Finder needs no setting: it is on whenever the social service is remote and the other world is on the
same MultiWorld gateway.

---

## 4. Open leads (not settled; nothing written off)

1. **L31 sound key 0x565a5445** played on "Match found!" (table DAT_145431198+0x2258 → FUN_1431f7e90): which sound.
   Try: the table's filler (writers of +0x2258 in part_0061 ~14739), the sound tables in gameData695.
2. **L31 live's own cross-world join**: the client shows none; ours is the queue entry + hand-over [I]. Patch notes 14
   Oct 2016 say only "Game finder should now work between worlds". Still open: whether live moved players without a
   Join Now, and whether live's offer timeout was the target game's MaxQueueJoinTime (ours: 30 s fixed, because the
   other world's rule-set setting isn't published).
3. **L31 double loading screen**: a cross-world joiner arrives beside the table, then `JoinGame` → MoveToSpawn moves them
   to a spawn (another re-zone while ShortTeleportDistance = 0). Could arrive at the chosen spawn directly if the join
   were decided before the re-zone.
4. **L60 PostQueueNotify text id**: live's id not found; 565560 "Teleport" is a stand-in [I]. Test 62065 / 559442 with
   `landscapecap text <id>` to see whether the GAQ fills `[*zone*]`; search the Lua / swf for a GAQTeleportToZone
   display string; fields +0x18 / +0x20 are unread by the handler (their live meaning unknown).
5. **L60 seconds per place, member priority, held-place time**: ours (60 s, members first, 60 s) [I].
6. **L60 capacity of another world's landscape**: travel to another world's landscape is handed over at once; the
   target world doesn't queue arrivals that come through a hand-over. Needs the target's capacity check before the
   hand-over (a gateway / social query) or a queue on arrival.
7. **L60 Landscapes window population**: Locations sends "capacity %" (world) and a per-landscape f32 capacity fixed at
   20 % / 0.2; with a capacity set these could show the real fill — how the client maps the f32 to Low / Medium / High
   / Full (and whether it greys out a full landscape) is not traced yet.
8. **L60 a dialog left open**: if a queued player travels elsewhere (a new TeleportRequest, Spire, death), the client's
   dialog stays open until they press Cancel (the client gives the server no way to close it). Whether live avoided
   that is unknown.

---

## 5. Test steps

### Travel queue (one client, the 0.695 test copy <0.695 test copy>, dev world)
1. Build / restart the dev world from this branch (restarts the dev world).
2. Dev command `landscapecap full` (a line in the world's `dev_commands.txt`, e.g. `server-data/worlds/dev/dev_commands.txt`
   as re/test/up.sh writes it), or `LANDSCAPE_CAPACITY=1` with another player on the target landscape.
3. Open the Landscapes window, pick another landscape of this world. Expect: no travel; the queue dialog "This location
   is currently full…", place 1. Server log: "landscape N full …, queued at 1".
4. Dev command `landscapecap 0`. Within a second: log "a place on landscape N is free; offered"; the Game Action Queue shows a
   "Teleport" item. Accept → the dialog closes, the player travels to that landscape's Spire. Decline instead → dialog
   closes, nothing happens (log "left the travel queue").
5. Again with `full`: press the dialog's Cancel → dialog closes, log "left the travel queue".
6. Check the client's reaction (chatlog / run state), not just the server log.

### Game Finder across worlds (two-world test stack)
1. `GATEWAY=1 re/test/socialtest/up.sh` then `re/test/socialtest/world2.sh` (world 93), both built from this branch,
   social service remote (SOCIAL_ADDRESS set by the stack).
2. On world 93: a site with a Game Table and **a player waiting in its game** (needs a second account / client, or
   In Progress ticked with a round running) — the service only names games with someone in them.
3. On the first world: Game Finder → Search. Expect: "Match found!", chat "You've been queued for a match!…" and our
   line "The game you were matched with is on another world…", plus a GAQ entry for that game with Join Now. Logs:
   world 1 "matched game 0x… on world 93 … offered".
4. Join Now → loading screen, the client is on world 93 (Landscapes window shows that world), beside the Game Table;
   after the re-zone the player is in the game (membership, HUD, scoreboard). Logs: world 1 "takes game …", gateway
   "handed over to world 93", world 93 "arrives from another world for …" then "joined …".
5. Leave Queue on the entry, or wait 30 s → entry removed (type 4), line "You didn't join … in time".
6. Remove the game on world 93 between offer and Join Now → arrival at the landscape's Spire with "The game you were
   matched with has closed."
