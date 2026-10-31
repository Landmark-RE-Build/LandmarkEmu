# Trees inside a newly placed build site (Task B, 2026-10-31)

User (2026-10-31): "trees do not disappear inside of a placed claim if they were there before."
Read-only investigation on deployed `f6fb4340` (client-0.695). No code was changed. Marks: [P] proven from a source or
from code, [I] inferred.

## 1. What live did

| Rule | Mark | Source |
|---|---|---|
| Placing a site **removes the trees standing inside it** | [P] | wiki *Build Sites* (launch era), "Location Tips": you can chop the tree first "*or* place the Build Site, which will remove all three [[Prop-Tree]]s" |
| No procedural props spawn on a claim, and ones already there are removed | [P] | P0096 (12 Feb 2014): "Procedural props will no longer spawn on your claim. If they previously did, they will be removed"; P0126 (13 Feb 2014): leftovers can be deleted with the selection volume, "they should no longer respawn ... shouldn't happen on any newly placed claims" |
| No logging inside any site (owner included) | [P] | P0090 (7 Feb 2014) "People can no longer harvest trees on a claim"; P0219 (6 Mar 2014) fixed a bug that let you harvest trees on claims; wiki *Gathering*: "You can not gather any materials found within the boundaries of any build site ... Anywhere you see the blue outline of a box, you can not mine or do any logging"; P6013 (30 Jun 2016) "harvest resources on your Site. (Still not possible.)" |
| Spawn points under a site are not used; the spawn picks another allowed point | [P] | Domino, forum 55352 #18 (19 Jun 2015): "if potential spawn points get 'covered' by claims then yes, trees can no longer spawn there, but they would just look for other spawn points that are still OK to use" |
| The buffer zone keeps its trees | [P] | wiki *Build Sites*: the buffer leaves natural land "allowing space for harvestable materials to generate"; "plenty of room for players to harvest surface ore, minerals, and trees"; Domino #18: the buffer "is still fair game for spawns" |
| Live's test was the spawn point, not the canopy | [I] | forum_raw 47632-2 (Sep 2014): trees "overhanging a claim" came back ~15 min after being deleted; 54587-2 (May 2015): bushes / trees / rocks "close to the edge of your claim" can't be deleted off it. Both alpha era |
| The claim service told the zone to clear them | [I] by name | id table 0.695: `cClaimsPacketIdClearAllProceduralProps` 0xF9/0x49, server-to-server (no client class compiled, coverage-695.md), next to `BlockVeins` 0x5B, the vein twin that our `ClearVeinsInSite` already copies |
| After the site goes, the spot becomes allowed again. Trees return through respawns below min, or at the next restart (restarts re-roll every spawn, Domino #1) | [I] | No source says what happened on deletion. The delete dialog promises the terrain goes back to natural |
| Owners could delete procedural leftovers on their site in 2016 | [I] | P5087 / P5098 (May 2016) prop menu: "Delete (if procedural prop)". This shows edge cases still existed at launch |

So in live, placing a site cleared the trees and plants inside it, and none grew there while it stood. Nobody could chop
inside it. Trees in the buffer stayed and could be chopped.

## 2. Why our trees stay (code, [P])

- **Regions are cached forever.** `Gathering.Region` (Gathering.cs 673-683) generates a 64 m region once into
  `regions` (217). The build-site filter runs only at generation: `Ground` (864-872) for the old lists and `Allowed`
  (852-858) for the sets, both through `Claims.MayEdit(0, ...)`. A site placed later never touches the cached
  `Node[]`, so the nodes inside stay `Standing`.
- **Clients already hold the entity, and nothing tells them to drop it.** `Stream` (595-642) sends a region's
  `AddLightweightProp`s once, when the region enters `sent[session]` (219). `PlayerUpdateRemovePlayer` goes out only
  from `Despawn` (522-530, felling) and from `Stream` when the region leaves the 224 m drop range. Walking away and
  back does not help: the cached node is still standing, so it is sent again.
- **No claim path calls Gathering.** `PlaceConfirm` (Claims.cs 612-661), `Grant` (1946), `Relocate` (1984) and
  `AddServerSite` (2027) call only `ZoneTerrain.ClearVeinsInSite`, the ore precedent (`OreVeins.ClearSite`,
  OreVeins.cs 340-371). `Remove` (1356-1390) has no hook either. Every removal goes through `Remove`: Delete, upkeep
  lapse in `Update`, `Expire`, `Abandon`, `DevLapse` and `CharacterDeleted`. A grep found no `Gathering.*` call
  anywhere in Claims.
- **The code already says so.** The Gathering.Respawn.cs summary, "Known limits" (64-67): "a tree already standing
  under it stays, as before; a site removed after the census adds no candidates until the next restart."
- **Respawns are already kept out.** `Stand` (Gathering.Respawn.cs 463-480) re-checks `Allowed`, so no respawn lands
  in a site. But the min / max pool still counts the trees inside as `Standing`, so it doesn't respawn their share
  elsewhere.
- **A restart hides them, for now.** `Claims.Load` (HostedService.cs 75) runs before `Gathering.Load` (109), so
  regenerated regions skip the site. The census drops those spots from the pools, and `Install` drops their saved "E"
  lines (Gathering.Respawn.cs 320). After a removal, the spots stay out of the pools until the next restart.
- **Chopping inside a site is not refused.** Mining refuses inside any site (`Abilities.Dig`, Abilities.cs
  1121-1129, `Harvest.Fail.NotAllowed`). `Gathering.Chop` / `Reap` / the extractor path (Abilities.cs 347-362) have no
  site check, so anyone can fell a leftover tree on someone else's site. Live did not allow this (P0090, P0219).

Bugs in the current filter:

- **B1 [P]: the filter depends on permissions.** `Claims.MayEdit(0, …)` → `Allows` → `PermissionsFor(claim, 0)` =
  the site's Default Access. A site whose Default Access includes Build 0x10 (for example "Ultimate Power" 0x11F,
  settable through `SetDefaultPermissions`, Claims.cs 1598-1603) passes the filter. Trees then generate after a
  restart, and respawn (`Stand`), inside that site. Coverage must be pure geometry.
- **B2 [P]: set nodes change keys.** With `ProceduralPlacement` on, `siteAllowed` drops a candidate before the
  encroachment test (ProceduralPlacement.cs 381-393). Keys are `128 + index` in the result list (Gathering.cs 771),
  so a site placed or removed shifts the keys and layout of every later node in the region and its margin. Saved "E"
  keys and census `Members` then point at other trees. Dev runs `lists` (gathered_nodes.txt: "Z 406/391/465/460
  lists"), where keys are cell indices and stable: the RNG draws come before the `Ground` check (701-709).
- **B3 [P]: newly stamped landmarks keep their trees.** A Landmark stamped at run time (LandmarksOfLandmark.cs 266,
  389 → `AddServerSite`) keeps the trees on it until a restart, the same as a player site.

## 3. Proposed server change

**One predicate, pure geometry.** Add `Claims.CoversNode(zone, Vector3 baseLocal, float trunkRadius)`:

- It is true when a site on that landscape covers the tree.
- The site's own box is used, with no buffer margin (live kept buffer trees). The test is the trunk circle (base ±
  `ModelFootprints` radius × scale) against the box's x / z, so a trunk straddling the edge counts. A crown that only
  overhangs does not, as in live's alpha-era behaviour [I].
- Vertically, the box's [MinY, MaxY] must overlap [base.y, base.y + TreeReach]. TreeReach is about 40 m, or set per
  `TreeSize` class [I]. This keeps trees under a floated site (box bottom up to 60 m above ground) that don't reach it.
- It never looks at permissions (fixes B1).
- `Allowed` and the respawn check use it in place of `MayEdit(0, ...)`.

**Generation ignores sites; coverage is an overlay.**

- `Ground` and the sets' `siteAllowed` keep sea, slope, the Spire and graveyards, but drop the site test.
- `Region()` sets a new `Node.Covered` from `CoversNode`.
- `Node.Standing` becomes `RegrowsAt <= now && !Covered`. Then `Stream`, `Target`, `Nearby` users, `ClearOfTrees`,
  `NodeCounts`, `DevFell` and the axe all skip covered nodes without further changes.
- `RegrowDue` and the respawn show-lists also check `!Covered`.
- Sets keys become stable (fixes B2). Bump the sets `Generator` tag (for example "...+v2") so sets landscapes
  re-roll once. Lists keys don't change, so dev needs no re-roll.
- Decoration nodes are covered too (P0096: "procedural props"). Their pools are fixed, so no counting is needed.

**Hooks**, called outside `Claims.sync`, with lock order Gathering → Claims only:

- `Gathering.SitePlaced(zone, box)` from `PlaceConfirm`, `Grant`, `AddServerSite` (this covers B3) and `Relocate`
  (new box).
- `Gathering.SiteRemoved(zone, box)` from `Remove` and `Relocate` (old box).
- `GrowToLaunchSize` and `Pioneer.MoveOldSites` run at start-up, before any region exists, so they need no hook.

On **place**:

- Under `sync`, for every cached region overlapping the box, take each node that is not covered and that
  `CoversNode` matches. Set it covered, `ResetDamage`, `ForgetTargets`, and note it if it was standing.
- Pool count (min / max): go over every census key inside the box, cached region or not, using the base positions
  that the census now keeps per key (about 24 B × roughly 10k spots per landscape). For each key not in `empty`,
  decrement `pool.Standing` and queue a respawn when the pool drops below min. This is Domino #18: the spawn moves to
  another allowed point.
- Do not add these keys to `empty`. A standing-but-covered node keeps its "standing" saved state, and that is what
  lets it come back.
- Outside the lock, send one `RemovePlayer` per covered tree to each session whose `sent` has the region, skipping
  sessions with `StreamsOnHold` (re-zone hold rule). Those get it naturally: `Forget` clears `sent`, and the next
  stream skips covered nodes.

On **remove**:

- Re-check each covered node in the box (cached regions plus census keys) against the remaining sites. Ones that are
  now uncovered get `Covered = false`.
- An uncovered node that is not in `empty`, with `pool.Standing < pool.Max`, goes on a "returns" list. If the pool is
  full, it goes into `empty` and becomes a free spot. Spots that were free under the site stay free and can be used by
  respawns again.
- `RespawnUpdate` works through the returns list each second with `Stand`'s player-clearance test, so nobody gets
  stuck in a trunk. Each returning tree gets `pool.Standing++` and one `AddLightweightProp` to the region's holders.
  Because the list runs a tick later, trees arrive after `Delete`'s `RevertBox` broadcast (Claims.cs 1202-1204) and
  the lapse's `SiteContents.Pack`. They then stand on reverted ground, not in a pit or a floor.
- In-place model: an uncovered node shows once its regrow timer has passed.
- This return is ours [I]. It stands in for live's restart re-roll, because our restarts keep the population (the
  user's rule).

**Census.**

- `RunCensus` keeps each key's base position and trunk radius, and computes `covered` from a snapshot of the
  landscape's site boxes (`Claims.On(zone)` is thread-safe).
- `Install` sets `Max` to the uncovered candidates ("today's placed count", the user's rule). The first roll picks
  among uncovered keys only, and covered keys become "E".
- `Standing` = uncovered and not in `empty`.
- Sites placed or removed while a census is running are queued per landscape and replayed in `Install`.

**Persistence.** No new save lines.

- Covered is derived from the claims, which load before Gathering.
- A tree that stood when its site was placed has no "E" line, so it comes back on removal, even after any number of
  restarts.
- A spot that was free under the site keeps its "E" line.
- Lists landscapes already rolled (dev) gain candidates inside sites that existed at roll time. These have no "E"
  line, so they count as standing-but-covered and appear only if that site goes, capped by `pool.Max`.

**Bandwidth.**

- Placing: one `RemovePlayer` (2 + 8 B body) per covered tree, once, to holders only (regions within 160-224 m). A
  210 × 210 m site holds about 30-100 trees (docs/terrain.md: 52-81 per 160 m view on 406, 16-25 on Pioneer), so
  under 1 KB per nearby player.
- Removing: one `AddLightweightProp` (about 250 B) per returning tree, plus the client's own full-data request /
  answer, once: about 8-25 KB per nearby player. That is small next to the reverted terrain cells the same delete
  sends.
- Nothing per tick. The returns list spreads naturally over one-second passes.

**Also (live rules).**

- `Chop` / `Reap` refuse a node inside any site, with `HarvestFail(HarvestNotAllowed)` as mining does. This is a
  guard for a client that kept an entity whose removal it missed (P0090, P0219, wiki *Gathering*).
- Settings, following the "live default + toggle" rule:
  - `SiteClearsTrees`: default true, the live behaviour.
  - `SiteTreesReturn`: `restore` (default [I]) or `pool` (no direct return; freed spots only feed respawns).

**Tests** (LandmarkEmulator.Tests, InternalsVisibleTo). Next to `Rules/TreeFellingTerrainTests.cs`:

1. Generate a region, place a box over it. Check: nodes covered, a RemovePlayer queued for a holder, pool Standing
   down, a respawn queued below min.
2. Remove the box. Check: returns are shown after the clearance check, Standing restored, keys unchanged.
3. A site whose Default Access includes Build 0x10 still covers (B1).
4. Sets region keys are the same with and without a site (B2).

## 4. Open questions / next steps

- **Live's exact coverage geometry** [I]: base point or trunk, and how floated or sunk boxes were tested. Next: look
  in wiki / forum images and 2016 threads for trees under floating sites (`re/out/forum_raw`: "float" + "trees").
- **Live after deletion** [I]: did trees come back at once, through respawns, or at the next restart? Next: search
  forum_raw / design/forum and archive.org for 2015-2016 threads on deleted or moved sites ("deleted my site" +
  "trees").
- **`ClearAllProceduralProps` payload**: server-to-server, so no client reader exists. Its meaning comes from the name
  only. Next: check the 0.589 / 0.695 decompiles for any writer (coverage says none was compiled). Keep it as name
  evidence (OPEN-LEADS).
- **P5087 "Delete (if procedural prop)"**: whether the 0.695 prop menu (native, not in as3_695 / lua_all) offers an
  owner delete for procedural props. Next: grep `decomp_all_695` for the prop radial menu's option builder and a
  procedural flag.
- **Spire**: `Spire.Prepare` runs in `AwaitCensus`, so regions generated before it (arrival `ClearOfTrees`) could keep
  trees on the platform. That belongs to Task A (Spire claims).
