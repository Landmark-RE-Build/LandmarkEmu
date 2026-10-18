# Bandwidth audit: client-0.695 work since 2026-10-17 12:00

Date: 2026-10-18. This was a read-only audit: no code edits, no git writes, no game, no Docker.

**Scope.** `git log --since=2026-10-17T12:00` on `client-0.695` (HEAD ef37922, 107 commits). The audit covers every
new send site in those commits. The items the brief named are measured in detail.

**Rule checked.** Every feature should:
- coalesce what it sends;
- send only to players in interest range;
- never resend on every tick;
- send only when something changes.

**Sources.**
- The code at HEAD.
- Two values from the dev logs:
  - the store-bundle packet is 2,694 B;
  - SendSelf recipes are 1,302 records in 161,822 B, about 124 B per record.
- The 0.695 decompile in `re/decomp_all_695`:
  - FUN_14358c410 (the swim compare);
  - FUN_14358d760 (the swim update);
  - FUN_14358cf30 (remote swim);
  - FUN_1435a53e0 (GetStateAtTime).
- The existing traces: agent-platform-collision.md, agent-e3-platform.md, audit-wire-a/d/e.md.

**Tags.** [P] = proven from code, decompile or log. [I] = inferred.

**Fixed costs per packet.** These hold for every byte count below:
- Zone traffic is RC4-encrypted and never compressed (`GameSession.GatewayCompression` is off: "zone data is RC4
  ciphertext so it never shrinks"). Every byte counts at face value.
- Every app message also costs its opcode (2 B), the reliable-channel framing, and its share of the UDP header.

---

## 1. What each feature sends

| Feature (commit) | Packet, size | Frequency | Recipients | Only on change? | Only in range? | Redundant or unbounded sending |
|---|---|---|---|---|---|---|
| **Moving platforms** (e8cbad8, e3da328; Props.Platforms.cs) | PlayerUpdatePosition 0x7F. Moving body about 43 B: 6 header, 9 position, 8 yaw-only rotation, 5–7 velocity, 10 zero fields, 3 state. Plus 2-bit transient id (2 B) and 4×f32 (16 B), so **≈63 B per message**. At rest about 55 B. | While moving: 1 per leg start or stop, plus every 500 ms. At rest: nothing. Newcomer: 2 copies 1 s apart. Measured route (5.4 m leg at 1.6 m/s, 2 s pause): about 16 messages per 10.8 s cycle, so **≈95 B/s per viewer per platform** on average and ≈126 B/s while moving. | Players on the landscape within 120 m and not mid re-zone. | Yes: the segment index, plus the interval while v ≠ 0. | Yes (120 m). | (a) A newcomer's 1 s repeat is still sent when a 500 ms batch has already reached them (one extra message, lines 223–229). (b) A viewer who leaves range mid-leg is dropped with no stop record (lines 207–208): their client keeps v ≠ 0 (see §2). (c) No hysteresis at 120 m: someone walking along the edge gets the newcomer pair again on every re-entry. |
| **Swimmers** (158ff76, ac6ecf8; HeroicMovement.cs) | HeroicMovement 0x105 sub 3, sent raw: 11 B header + 5 B body, or 13 B body when under water (flag 0x40). **16–24 B.** | Relay capped at 10 Hz per swimmer (SwimInterval 100 ms). The client sends one whenever the state differs by **exact float compare** (FUN_14358c410 compares depth and both heights with `==`) and has no rate limit of its own (FUN_14358d760) [P]. So while diving or bobbing, depth changes almost every frame and the 10 Hz cap is what binds: **≈160–240 B/s per swimmer per viewer**. Idle floating sends ≈0. Introduce: one 24 B copy, only when the swimmer is in water. | Players on the landscape within 200 m, one `SendRaw` each (not fanned out through the gateway). | Exact-byte compare only. A depth change of 0.0001 counts as a change. | Yes (200 m). Players mid re-zone are not filtered out (their client has no such entity yet). | Depth is relayed at full float precision. The remote side multiplies it by the character height to place the water line (FUN_14358d760 remote branch: `+0x94 = +0xa8`, then `height × depth`) [P]. Steps of a few centimetres cannot be seen [I]. |
| **Movement effects** (1ffa388, 38f752b; MovementEffects.cs) | CasterInfoReply: +56 B per ClientExecutedEffect (1–2 per boot ability). CastContinue about 30 B at press. CastSuccess + AbilityEnd about 75 B, 1 s after release. Energy update about 20–30 B per second of Immediate Delivery (328). | Per cast or press. Channel ticks are server-side timers; only ability 328 sends anything (Energy). | Self only. | Yes. | n/a | None worth changing. Each Shift release costs ≈75 B on purpose: it is the fix for the stuck-cast case. |
| **Character stats** (1ffa388; CharacterStats.cs) | Login: ClientUpdate UpdateStat with 32 × 13 B + 4 = **420 B**, once. LightweightToFullPc: nested list with 8 × 13 B + 4 = **+104 B per full conversion** (it used to be an empty list). | Login once. Full conversion: whenever a viewer's client asks (FullCharacterDataRequest), i.e. on meeting someone and again after **each of the viewer's re-zones**. | Self; each requesting viewer. | Constants, sent once per conversion. | Client-driven. | None that can be removed: all 8 times are needed (absent means 0, an instant blend). 10 of the 32 login stats are 0 (ids 38, 39, 42–47); 130 B per login could go if the client defaults them to 0 [I]. |
| **Melee targeters** (2fc0c49; WeaponCombat.Targeters.cs) | CasterInfoReply gains 12 × 4 = **48 B per melee ability** (3 abilities per kit, so 144 B per weapon). FindTarget and ClientTargeterEnd travel client → server. | Per CasterInfoRequest (on equip). | Self. | Yes. | n/a | None. Refused FindTargets get no reply. A swing waiting for the sweep is resolved either by the client or by the server's timeout, never both. Accepted hits go through the existing Strike fan-out. |
| **Hidden recipes** (1a5b831; RecipeBook.cs, SendSelfToClient) | SendSelf hidden list: 4 B per owned unlock, **≤ 2,324 B** (581 ids). RecipeBase 0x1F / 0x20: 10 B each. | Login once. 0x1F/0x20 only for a real difference (RefreshHidden diffs against `session.HiddenRecipes`). | Self. | Yes. | n/a | All **581 unlock recipes are DEFAULT_LEARNED** (gameData695 Recipes.txt: 581 of 581) [P]. So **their full records (≈124 B each) are still in SendSelf** even when hidden. A player who owns every unlock pays about 72 KB per login for records the client will never list or start (§3 R1). Also, TeachProp calls RefreshHidden twice (RecipeBook.cs:396, and through AddRecipes at 426): this costs CPU only. |
| **Showcase digest / commerce** (6c2e99a, 90e5f68) | One system mail per follower per day: ≤ 40 items. The client only gets the new-mail notification; the body comes when the mail is opened. 0xD0 purchase result: 12 B plus one chat line. | Daily, at most 20 mails per tick. Commerce ↔ world traffic is internal only (pending digests per realm, de-duplicated by in-flight id). | Recipient only. | Yes. | n/a | None. |
| **Store bundles** (1c91d45, 03c0ef1; StoreBundles.cs) | InGamePurchase sub 5: **one packet** with all products, about 245 B per product. 10 products + membership = **2,694 B (log)** [P]. | Once per world entry (`Send`, force). `Refresh` on a membership change resends only if the bytes differ. | Self. | Yes. Login never double-sends: Membership.Enter only records `told`, and Tell/Refresh compare bytes. | n/a | Not per product: it is one message. Only the Fantasy Parlor Pack row has a visible use (the Parlor's Unlock button); the other 9 rows (≈2.2 KB) are there to match live's store. |
| **Creature weapons** (dce93ea; off by default) | EquipmentSetCharacterEquipmentSlot: guid, slot, model string (~37 B), tint, so about 80–100 B. | One per creature at each viewer's full conversion (again after every re-zone). Equip / Sheath changes go to full viewers. | Viewers holding the creature's full form. | Yes. | Yes (ViewersOf + IsFullFor). | None. A camp of 10 kobolds costs about 1 KB per re-zone per viewer, and only with the setting on. |
| **Evac / GM teleport requests** (6513c82; TeleportRequests.cs) | Answered with TeleportToLocation (47 B + opcode). It is always a short move (`loadingScreen: false`), even when refused (answered in place, because the client is frozen until it gets one). | Per request; 10 s cooldown for non-admins. | Self. On settling, `PlayerManager.ShowPosition` sends one 0x7F to everyone on the landscape (pre-existing). | Yes. | ShowPosition is landscape-wide (pre-existing). | **The settle runs `Teleport.FinishRezone` → `Spire.ReassertCollision`** (Teleport.cs:253 → 317) even though a short move deletes no entity. That is 5 retries × 7 Spire props × ≈63 B ≈ **2.2 KB per short move** within 300 m of the Spire (§3 R2). Translocation Pad hops (Props.Movers.cs:255), in-place respawns (Health.cs:586) and evac all take this path. |
| **Zone control / PropState** (5ae6bdd, 427918a, edf630b) | PropState: a full replace list, on change, de-duplicated per (session, game). PropInteraction: per point, on change, timed by the client's own clock. The HUD (sub 9) is sent whole (title, every point, score bars), roughly 150 B + ~60 B per point + ~50 B per bar [I]. | HUD on every 10 % capture step. With CaptureSeconds = 5 (InstancedGames.cs:119), that is **2 HUDs per second per capturing point**, to every player and observer. Plus one per score tick (10 s). | Game players and observers. | Partly: 10 % steps (this used to be every tick, about 700 per second). | Game members only. | The HUD rate during a capture is the only steady stream left (§3 R6). Subs are native 0.695 again (0x12 / 0x13 / 0x15) after the translator was removed [P, InstancedGames.cs:45]. |
| **Arrival hold: props sent early** (97b5728; MiscHandler.cs:285) | `Props.SendAll`: **every placed prop on the landscape**, about 250 B per AddLightweightProp (+ name, effects, story state, one flagging packet). | Per re-zone arrival that lands ≥1.5 m above the ground over props. | Self. | No double send: `PropsSentEarly` makes Reintroduce skip its own SendAll (PlayerManager.cs:189) [P]. | **No.** Landscape-wide, and sent *before* the zone-done flags. | Total bytes are unchanged from before (the same SendAll as Reintroduce, just earlier). But the whole landscape now shares the reliable channel with the terrain manifest during the 4 s body window the hold is waiting for. A big landscape can push the deck's own props back in the queue. Only props near the arrival need to go first (§3 R5). |
| Spire after every re-zone (650a6ca, 4df69a9) | 7 × AddLightweightProp ≈ 1.8 KB. Then ReassertCollision: 5 × 7 × 0x7F ≈ 2.2 KB. | Every real re-zone. The retries go only to players within 300 m of the Spire. | Self. | Required: ClientBeginZoning deletes the entities [P]. | Spire props: landscape-wide. Retries: 300 m. | The retries at 12 s and 24 s fire after the measured body times (2.7–4.4 s after zone load, and FinishRezone already comes after the Spire's 6 s hold) [I] (§3 R4). |
| Procedural respawn (3c8f9c4) | Node spawn. | Per respawn. | Only sessions that have the node's region streamed. | Yes. | Yes. | None. |
| Map positions 0x25 (9fdb0d9) | ≈40 B per listed player. | Per request. The unmodified client never sends 0x24 (L164 closed: no shipped caller). | Self. | n/a | n/a | None in practice. |
| Claims ClaimInfoUpdate kinds 5 / 6 (936f360) | Small. | On a permission change. | Landscape. | Yes; it replaced whole-record resends. | Landscape-wide. | None. |

---

## 2. Client-side constraints that bound the choices

1. **The platform's collision rebuild is driven by velocity, not by packets** [P, agent-platform-collision.md §2.5].
   - ProxiedProp::Update (FUN_1430ab000) calls FUN_1430ac390 **every frame while the motion state's |v|² ≥ 1e-5**.
   - With v = 0, a rebuild happens only after a 0x7F sets +0x4F00, and at most once per 1000 ms.
   - What this means for sending:
     - The send rate does not change collision correctness. It only changes visual smoothness and drift.
     - Mid-leg updates must never carry v = 0 (the carry would stutter).
     - **Every leg must end with a v = 0 record for every client that saw it moving.** Otherwise that client rebuilds
       the body every frame for as long as it holds the last moving record. Today a viewer who walks past 120 m mid-leg
       is in exactly that state.
2. **Control mode 2 plays buffered 0x7F records through GetStateAtTime** (FUN_1435a53e0: it picks the two records
   around the render time and blends them) [P].
   - Whether it extrapolates along the velocity past the newest record is still open: the tail of the function was not
     read [I].
   - Until that is settled, the 500 ms rate is the safe choice. A slower rate (R7) needs that check.
3. **The swimming client sends on any exact change and has no rate limit** [P, FUN_14358c410 / FUN_14358d760].
   - So the server's relay cap is the only limit on swim traffic.
   - The remote side uses depth × height as the water line and acts on flag 0x80 only from state 1 [P,
     FUN_14358cf30].
4. **ClientBeginZoning deletes every entity except the player**; a plain TeleportToLocation deletes none [P,
   FUN_142d1ccf0 case 8].
   - So anything that re-asserts entities (Spire collision, props) is needed after a real re-zone and wasted after a
     short move.
5. **The client ignores a spawn it already has** (FUN_142f8a800) [P].
   - Duplicate prop sends are harmless to correctness, but they still cost their bytes.

---

## 3. Ranked reductions

Ranked by expected bytes saved and by how certain the saving is. Line numbers are at HEAD ef37922.

### R1. Leave owned Lumen Station unlocks out of SendSelf's recipe records

**Saving.** About 124 B per owned unlock per login: up to **≈72 KB** for a player with all 581, ≈12 KB at 100 owned.
Certainty: high for the bytes; [I] for the client side (check below).

**Change.** `LandmarkEmulator.WorldServer/Network/WorldSession.cs:1080-1084`: compute `HiddenRecipes` first, then
filter the records:

```csharp
HiddenRecipes = Game.RecipeBook.HiddenUnlocks(this);
message.Recipes.RecipeList.AddRange(Game.CraftingStations.BuildRecipeRecords(
    Game.RecipeBook.RecordsFor(this, message.Recipes.Unknown0).Where(r => !HiddenRecipes.Contains(r)).ToList()));
message.Recipes.HiddenRecipeIds.AddRange(HiddenRecipes.OrderBy(r => r));
```

**Keep the hidden id list.** It is 4 B each and still guards the client's own start paths.

**Why it is safe [P / I].**
- The client already gets known ids that have no record: the log shows 1,364 known and 1,302 records, with no
  problem.
- "Hidden" never goes back to "shown" in practice. A prop taught stays taught, and storage expansions only grow.

**Risk.** A real 0x20 unhide later in the session would show nothing until the next login.

**Verify.**
1. Log in on the test copy with owned unlocks.
2. Check that the Lumen Station lists only unowned ones.
3. Check that the client log has no recipe errors.

### R2. No Spire collision retries after a short move

**Saving.** ≈2.2 KB (35 × 0x7F) per short move within 300 m of the Spire:
- each Translocation Pad hop;
- each evac or GM request;
- each in-place respawn.

It also saves the client 35 collision rebuilds. Certainty: high [P: a short move sends no ClientBeginZoning, so the
props and their one-time collision latch are untouched].

**Change.** `LandmarkEmulator.WorldServer/Game/Teleport.cs`:
- line 308: `public static void FinishRezone(WorldSession session, bool rezoned = true)`;
- line 317: `if (rezoned) Spire.ReassertCollision(session);`;
- line 253 (settled short moves): `FinishRezone(session, rezoned: false);`.

### R3. Swimming relay: quantised state, 250 ms cap

**Saving.** From ≤10 Hz × 16–24 B to ≤4 Hz, and only on a visible change: **≈150–240 B/s down to ≲60 B/s per swimmer
per viewer** while diving or surfacing, and nothing while gliding along at a steady depth. Certainty [I] for the
visual result; check the water-line pose of a remote swimmer side by side.

**Change.** `LandmarkEmulator.WorldServer/Game/HeroicMovement.cs`:
- line 68: `SwimInterval = TimeSpan.FromMilliseconds(250)`.
- lines 127–130 (`Swim`): quantise before storing:
  - depth (body[1..4]) to 1/64 (≈3 cm on a 2 m character);
  - the two heights (body[5..12], present when `flags & 0x40`) to 0.05 m;
  - flags unchanged.
- The compare at line 155 then drops the near-identical states the exact client compare lets through.
- line 179: add `&& other.RezoneTarget == null` (a client in the middle of a re-zone has no such entity yet;
  IntroduceSwimmer covers it afterwards).

**Optional.** Collect the swimmers' relays per tick through a `TunnelToExternalList`, as MovementFanout does. This saves
internal-link bytes only (n-1 copies per relay on the world→gateway link), not client bytes.

### R4. Spire collision retries: drop the 12 s and 24 s ones

**Saving.** 2 of 5 retries: ≈0.9 KB per real re-zone arrival near the Spire. Certainty [I]:
- the bodies built 2.7–4.4 s after the zone load (Frida, revisit1 / r71);
- FinishRezone, which starts the retry clock, already runs after the Spire's 6 s hold.

**Change.** `LandmarkEmulator.WorldServer/Game/Spire.cs:799`: `CollisionRetryMs = { 1500, 3000, 6000 }`.

**Re-test** the arrival-from-another-landscape case on a slow load (the 391 case).

### R5. The arrival hold sends only the props near the arrival

**Saving.**
- No byte saving per arrival. It removes the landscape-wide burst *ahead of* the zone-done flags: everything but a
  handful of props moves back behind the flags.
- That protects the 4 s body window the hold depends on (a large landscape could otherwise delay the deck's own
  props).
- It is also the first step towards R8.

Certainty: high for the ordering; [I] that the burst ever exceeded the window.

**Change, in four places.**
1. `LandmarkEmulator.WorldServer/Network/WorldSession.cs:135`: replace `bool PropsSentEarly` with
   `HashSet<ulong> PropsSentEarly`.
2. `LandmarkEmulator.WorldServer/Network/Handlers/MiscHandler.cs:285-286`: send only these, and remember their guids:

   ```csharp
   Game.Props.PlacedInBox(session.ZoneId, at - new Vector3(48f, 64f, 48f), at + new Vector3(48f, 16f, 48f))
   ```

   This needs a `Props.SendSome(session, list)` that wraps `SendSpawns`.
3. `LandmarkEmulator.WorldServer/Game/PlayerManager.cs:189-191`: always run `Props.SendAll(session, except:
   session.PropsSentEarly)` (a new optional parameter at `Props.cs:2317`, filtering on `!except.Contains(guid)`), then
   set `session.PropsSentEarly = null`.
4. This also fixes a latent stale-flag case: if the 4 s action is ever skipped (Teleport.cs:232), today's bool would
   make the next re-zone send no props at all.

### R6. Zone-control HUD in 20 % steps

**Saving.** Halves HUD resends during a capture: 2 Hz → 1 Hz per capturing point. Each HUD is a few hundred bytes to
every player and observer.

**Trade-off.** User experience: the overhead PropInteraction bar stays smooth because it runs on the client's clock.
Only the HUD indicator moves in coarser steps.

**Change.** `LandmarkEmulator.WorldServer/Game/InstancedGames.cs:1402/1404` and `1422/1426`: use `* 5f` instead of
`* 10f`, or make it a setting with a live-like default.

### R7. Platform updates

**Saving.** A few messages per viewer (from a, b, c). Up to ≈50 % of moving-platform traffic, about 126 → 63 B/s per
viewer per platform while moving (from d, if the client check passes).

**Changes in `LandmarkEmulator.WorldServer/Game/Props.Platforms.cs`:**
- **(a) Stop record on leaving range** (client constraint §2.1; costs +1 message, saves client CPU).
  - Lines 207–208: for each `gone` viewer, if `pose.Velocity != Vector3.Zero`, queue `Creatures.MovementBody(pose.At,
    pose.Turn, Vector3.Zero)` before removing it.
  - Skip viewers who have `RezoneTarget` set, because their entity is gone anyway.
- **(b) Hysteresis.**
  - Line 202: admit viewers at ≤ `PlatformViewRange`.
  - Line 207: drop them only beyond `PlatformViewRange + 10f`.
  - This needs a second distance check on `mp.Viewers` keys. It stops repeat newcomer pairs at the 120 m edge.
- **(c) No redundant repeat.**
  - Lines 223–224: `else if (all) { sendTo.Add(v); if (pose.Velocity != Vector3.Zero) mp.Viewers[v] = null; }`.
  - While the platform moves, the 500 ms batch already gives a late spawn its second chance.
  - Saves 1 message (~63 B) per newcomer.
- **(d) [I, verify first] Slower rate on legs with no turn.**
  - Line 93 / 214: use 1000 ms when `stops[k].Turn == stops[k+1].Turn`.
  - Only if FUN_1435a53e0 is shown to extrapolate along +0x1F0 past the newest record. Otherwise the platform would
    move visibly in steps.
  - Do not change collision behaviour: a leg still starts with v ≠ 0 and ends with v = 0.

### R8. (Before the cut-off, but the biggest item.) Interest-range prop streaming

`Props.SendAll` (Props.cs:2317) sends every placed prop on the landscape (≈250 B each) at login and on **every
re-zone**: travel, caves, the Spire, any long teleport. With 1,000 placed props that is ≈250 KB per re-zone.

**Change.** Send the props within the client's prop draw distance, and stream the rest by region as they come into
range, the way trees and plants already do (the per-session region set in Gathering, `sent[session]` in procedural
respawn).

**Out of scope for this window.** It is listed because R5 builds on it, and because the hold now makes the cost
visible.

#### R8 design note (2026-10-18, wt-bandwidth; for the next build)

**How far the client draws props [P].**
- There is no prop-specific draw distance. The far clip is the camera's far plane: FUN_142e5c4f0 sets camera +0x284 to
  min(the renderer's far plane, FUN_1441cb9b0; RenderDistance at +0x1b8).
- RenderDistance is the user's "Render Distance" option ([Rendering] RenderDistance in UserOptions.ini, loaded at
  FUN_143489560). A value ≤ 0 (the default: the test copy has `-1.000000`) becomes 99999 (0x47c34f80),
  i.e. no limit beyond the renderer's own far plane.
- The UI slider (Lua SetRenderDistanceInPercent FUN_1435cfd10 / Get… FUN_1435d0140) maps 0-100 % to 120 + 2.81 × %
  metres: **120 m at 0 %, ~400 m at 99 %, unlimited (99999) at 100 %** [P: constants 120.0, 2.81, 400.0, 99999.0].
- On top of that each model picks a LOD from camera distance (entity +0x2B4, see Spire.ReassertCollision); its
  thresholds live in the model files and are not traced [I: a far LOD may fade props out earlier].
- So a server radius cannot match every client setting. **Proposal: stream props within 400 m** (the slider's last
  bounded value), drop beyond 480 m. A player on "unlimited" sees props pop in at 400 m, which is the same compromise
  trees / plants already make (160 m) and much better than the terrain window. Setting `PropStreamRange` (default
  400; 0 = whole landscape as today, the live-unknown fallback).

**Region scheme (as Gathering.Stream).**
- Index placed props per landscape by 64 m region: `Dictionary<(zone, rx, rz), List<PlacedProp>>`, kept in step in
  Place / Move-settle / Remove / pack expansion (Props.cs places that change `p.Zone` / `Spawn.Position`).
- Per session: `HashSet<long> propRegions` (same key packing as Gathering.Pack). On the 1 s stream tick
  (with `StreamsOnHold` respected): regions whose nearest point is within PropStreamRange and not yet held → SendSpawns
  of their props (one DynamicInteractionFlagging per batch, as now); held regions beyond the drop range → SendRemove
  (effects off + RemovePlayer) and forget.
- A per-tick budget (e.g. 200 props) keeps a dense build area from making one huge burst; nearest regions first.
- Big props: a prop is indexed by its origin, so a 60 m structure on a region edge could pop in late. Index props
  whose bounding radius (PropDefs / collision box) exceeds ~32 m in every region they overlap.

**What changes elsewhere.**
- `Props.SendAll` → `Props.StreamFor(session, reset: true)`: clears the session's region set and sends the regions
  within range of the arrival, nearest first. Login (PlayerManager.Enter) and `Reintroduce` call it; the arrival
  hold (R5) becomes "send the arrival's own region ring now, mark them held" and the rest stream normally.
- Every landscape-wide prop broadcast (Props.cs ~l.156, 424, 822, 1237, 1361, 1395, 1418, 1436, 1520, 1744, 1868,
  1909; Props.PlayModeVisibility 101 / 176) must be filtered to sessions that hold the prop's region: a spawn to
  someone who doesn't hold it is wasted, and an update / name / state for a prop the client has never seen is
  dropped by the client anyway. A helper `Props.ViewersOf(prop)` replaces `PlayerManager.InZone(prop.Zone)` there.
- Placement: a new prop goes to the region's holders only (the placer always holds their own region). A prop moved
  across a region edge: holders of the new region that lacked the old one get a spawn; holders of only the old one
  get a remove.
- Things that look up props without needing the entity on the client keep working (Find, chests, claims, AreaDefinitions
  by guid). Open checks before building: (1) AreaDefinitions.SendAll still sends every mover / linking area
  landscape-wide — does the client need the prop entity for a Linking Area to fire (trace FUN_1431ae4f0 area factory)?
  (2) Story-tool PropState / Links to a prop out of range: the server keeps state, SendStateTo already runs in
  SendSpawns, so a later spawn shows the current state. (3) Moving platforms: their own 120 m viewer set stays, but
  must also require the region held. (4) Spire and outposts are not placed props: unchanged.
- Expected saving: with 1,000 placed props spread over a landscape, a re-zone sends only the ~5-15 % within 400 m
  (≈12-40 KB instead of ≈250 KB), and nothing at all for regions the player never approaches.

### R9. Small items

- **Login character stats.**
  - Leave out the ten 0-valued stats (`CharacterStats.cs:60`, ids 38, 39, 42–47) once the client is shown to default
    them to 0. Saves 130 B per login [I].
- **RecipeBook.TeachProp.**
  - Drop the second `RefreshHidden(session)` at `RecipeBook.cs:396` when `fresh.Length > 0`: AddRecipes (line 426)
    has already run it.
  - Bytes are unchanged (the diff is empty). It saves CPU (581 TeachesNew calls).
- **Store bundles.**
  - No change is recommended (2,694 B once per world entry; Refresh only on a byte change).
  - If an owner wants a lean store, a setting could send just the Fantasy Parlor Pack and membership rows (≈0.5 KB).
  - The default stays live-like, per "live default + toggle".

---

## 4. Already as the rule wants (no change)

- **Movement effects and channels.** Self only, per press. Channel ticks send nothing except the Energy of Immediate
  Delivery, once a second.
- **Melee targeters.** +48 B per melee CasterInfoReply. FindTarget gets no reply when refused.
- **Character stats in LightweightToFullPc.** +104 B per conversion. The minimum for the 8 animation times.
- **Showcase digest.** Daily, bounded (40 items), only to followers. The commerce link de-duplicates in-flight mails
  per realm.
- **Hidden recipes in play.** 10 B per real change only.
- **Creature weapons.** Off by default; sent at full conversion and on equip changes, to full viewers only.
- **Evac and teleport requests.** One 47 B TeleportToLocation per request, with a 10 s cooldown. The refusal is answered
  in place because the client stays frozen without one.
- **Zone control PropState / PropInteraction.** On change, de-duplicated per session.
- **Procedural respawns.** Only to sessions holding the node's region.
- **Map 0x25.** Only on request, and no shipped client caller.

## 5. Noted outside the window (pre-existing, landscape-wide)

- **Movement relays.** `PlayerManager.RelayMovement` / `ShowPosition` (PlayerManager.cs:290–313) fan every player's
  movement out to the whole landscape, with no distance check (HeroicMovement uses 200 m, platforms 120 m).
- **Reintroduce on every re-zone.** It re-introduces every player on the landscape. Each viewer's client then
  re-requests their LightweightToFullPc.
- **Calculated stats.** Each LightweightToFullPc carries all **47 calculated stats (376 B)**. Which of them a remote
  client reads is not traced; a remote-only subset could save most of that per conversion.
