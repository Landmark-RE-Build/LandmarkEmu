# FEATURES #47 Graveyards: cavern graveyards, buffer zone, surface graveyards (2026-10-17)

Branch `e-graveyards` (worktree `<workspace>\wt-graveyards`), commit `38e6df8` on top of `d285059`. Not pushed.
`dotnet build LandmarkEmulator.WorldServer/LandmarkEmulator.WorldServer.csproj -v q` succeeds with no new warnings.
Nothing was started or tested in game. The test steps are at the end.

Marks: **[P]** proven from the client, its data or a dev source. **[I]** inferred or ours.

---

## 1. What live did, with evidence

### 1.1 Rules from patch notes, wiki and forum

- **Layers.** P2296 (17 Dec 2014): "If you die on the surface you will return to the hub. If you die below the first
  layer you will revive in one of the graveyards which are scattered around the underground layers." P2718 (5 Feb
  2015): "Addressed an issue where you would always go to the same graveyard on that particular layer, instead of the
  closest one." So Flee goes to the **closest graveyard on the layer the player died on** [P].
- **Surface graveyards existed at launch.**
  - Wiki *Graveyard*: "Graveyards can be found all over the surface of the landscapes or deep within Chaos Caverns",
    "small, red circles on the map".
  - Wiki *Death*: "Flee (revive) at a Graveyard (only while exploring)".
  - Forum 57065 (May 2016) asks for "a [small] no build area much like Surface Graveyards".
  - P6194 (28 Jul 2016): "Graveyards on Pioneer Islands should never be sunk too deeply into the ground".
  - So the server placed them, and Pioneer lands had them too [P].
- **Buffer zone.** Wiki *Graveyard*: "Within the Chaos Caverns, the[y] offer a 'buffer zone' of safety to assure
  roaming monsters will not attack."
- **No build sites on graveyards.**
  - P3540 / P3548 (May 2015): "Graveyards can no longer be claimed ... your claim may be removed".
  - Wiki: "You can not have a Graveyard within the building limits of a build site. Your build site can be close to
    the graveyards, but must remain on the outer boundaries."
  - P3547: graveyard props "can no longer be ... deleted. They also cannot be pasted."
- **Cavern arrival was the graveyard.**
  - Forum 57166 (May 2016): "Porting from my claim to a chaos cavern (graveyard) took on avg of 20 seconds".
  - Forum 56713 (Apr 2016): "Character falls through world sometimes in chaos caverns on initial load into the
    graveyard".
  - Forum 57170: "using FLEE option put me to a different graveyard".
  - Forum 57065: "Some ruins/outposts you respawn on and others you go all the way back to the graveyard".

### 1.2 What the client holds [P]

**Area 23 is the graveyard.** Area property type ids come from each `AreaPropertyData*` class's type function
(vtable slot 4, re/out/rtti_695.tsv):

| Class | Type | Function |
|---|---|---|
| Graveyard | **0x21 = 33** | FUN_143db3540 |
| TerrainPermission | 17 | |
| ProcedurallyPlacedEntities | 18 | |
| Message | 21 | |
| Blank | 19 | |

Area 23 (gameData695 AreaProperties rows 57/62/65/66/72) is the only area with type 33. Its properties:

- 33 Graveyard (no parameters);
- 37 and 36 (server-only; see L-a);
- 18 ProcedurallyPlacedEntities with INT1 = 1 and FLOAT1 = 1.5. UI/Default/Data.xml names its parameter 0
  "Prevent Spawning";
- 17 TerrainPermission, index 5, INT1 = 4, INT3 = 1.

Its shape (GlobalAreaShapes row 20) is shape 1 (sphere) with params 0, 3, 6, **30**. The params are x, y, z and
radius: compare row 6 (pad, r 1.5 at y 1.5) and row 8 (−4.1, 1188.5, 46.69, r 5100).

**How the client uses it.**

- A prop spawn names its static area at base +0x260 (PlayerUpdateAddLightweightProp `StaticAreaId`; 0.695
  FUN_142d54040; movers-695.md §3).
- `ClientAreaTerrainPermission` (vtable 1447d2450, slot 10 = FUN_1436de260) checks its data's INT1 (+0x68; data
  object at +0x60, `AreaPropertyDataTerrainPermission` +8 = INT1, FUN_143db4740). When INT1 is 4, it makes a
  `ClaimPlacementExclusionZoneSphere` for every sphere shape:
  - the centre is the prop's position (vfunc +0x168);
  - the radius is the shape's;
  - it is filed with the claims manager (DAT_145431190, FUN_142db3880).
- The client's site-placement precheck (FUN_142dc6870) looks in its exclusion octree (+0x200). The sphere test
  (FUN_1436ddd80) is: the distance from the prop to the middle of the box is less than the radius plus the box's
  half-diagonal. A hit returns 3, which shows **"Claims.InvalidClaimLocation"** (FUN_142db5960). 1 = buffer overlap,
  2 = cave entrance.
- That this octree is what the map draws as red circles is [I]. The claims manager is called from the map-close code
  (part_0082 26487, near "GameEvents:OnCloseMapDepths"), but the drawing was not traced (L-c).

**Where the cavern graveyards are.**

- Every island's `ContinentCavePlacements[c]_Underground.cpa` (v14, islands 391-416) has 81 Graveyard01 cave pieces
  (VoxelCaveTileDefinitions 111).
- It also has 81 `ManuallyPlacedExportedTemplateData` boxes of 332 × 88 × 332 dm. That is the size of
  `graveyard_launch.otd`: Graveyard Centerpiece 1518, gravestones 2068/2070/2071, glowing mushrooms and 4 Stone
  Gargoyles.
- Measured on all 25 v14 files: each box's minimum lies at exactly (−220, −264, −64) dm from its Graveyard01 piece.
  This holds whatever the box's first field (1-4) is. So the graveyard's middle is (−5.4, ·, +10.2) m from the piece,
  and its floor is about 22 m below the piece's origin (the centrepiece is at template y 3.2 m).
- The older continents' v12/v13 files use other offsets, which this server doesn't use.
- Script: scratchpad gy/cpa.py (parser from ruin-stamping.md §5).

**No client file places the surface graveyards.**

- The `_Surface.cpa` files hold only cave tiles 32-55 / 107. Every island except continent 354 has 0 tiles.
- No gameData695 table has a graveyard position or an area reference. Props have no area column; the server sends
  the area id.
- The prop "Graveyard Origin Marker" 1480 (gravestone model) is server-only.
- There are three graveyard templates:
  - graveyard01.otd: v2, **0 voxel cells**, 43 props, centrepiece 1518.
  - graveyard02.otd: v4, 8 cells, a raised mound.
  - graveyard_launch.otd: the cavern one.
- So live's server placed the surface graveyards from its own data [P for "not in the client"].

**Other related areas** (no client file says which prop carried them; see L-e):

| Area | Shape | Properties |
|---|---|---|
| 25 | sphere r 30 at the prop | TerrainPermission kinds 4 / 2 / 3, Prevent Spawning, 36, 37 |
| 27 | sphere r 360 | Prevent Spawning, 36, 37 |
| 3 | sphere r 90 (hub) | TerrainPermission 0-4, Prevent Spawning, Message HubEnter / HubExit, 36 |
| 4 | box ±10 (hub) | as 3 |
| 12 / 13 | old hub (r 360 / 45) | |

---

## 2. What was built

Files: `Game/Graveyards.cs` (sites, layer rule) and a new `Game/Graveyards.Landscape.cs` (the landscapes'
graveyards). Changes elsewhere:

- `Health.cs`: Flee and evacuation to cavern sites with no terrain snap.
- `Teleport.cs`: `ToZone(..., snapToGround)`.
- `Creatures.cs`: the buffer zone, and no creature spawns inside a graveyard.
- `Gathering.cs`: no trees or plants inside a graveyard.
- `Claims.cs`: placement refusal.
- `DevCommands.cs`, `HostedService.cs`, `PlayerManager.cs`, `PlayerHandler.cs` (full-prop requests).
- `WorldServerConfiguration.cs`, `docker/world/docker-compose.yml`, `.env.example`.
- `docs/gameplay.md`, new section "Graveyards".

### (a) Cavern graveyards and layer respawn

- **Which graveyards.** On each landscape, one per Graveyard01 piece of its Underground .cpa. They are found in the
  background on the first visit (`Warm`), or on first need.
- **Where they stand.**
  - Centrepiece: the cave floor under the graveyard's middle (`Caves.Floor` from 18 m below the piece).
  - Fallback: under the piece itself (the warp crystal's spot).
  - Arrival: 6 m along +z from the centrepiece, on the floor.
- **What is sent.** Only the centrepiece (prop 1518 with static area 23), streamed by 3D distance (160 m in, 220 m
  out). The template's voxels and other props are not stamped yet (L-d).
- **Layer rule.** A player more than **100 m below the ground** is on the cavern layer [I; L-g]. Flee and evacuation
  from there go to the nearest cavern graveyard of that landscape. The death still uses the build site's own points
  first when the player died on a site, as before. The move goes without the client's terrain snap, which would put
  the player on the surface.
- **Fallback.** A landscape with no cavern graveyards falls back to the old list (Spire, own site, ...).

### (b) The buffer zone

- **Radius.** 30 m, the area 23 sphere. That the wiki's buffer is this sphere is [I]: it is the graveyard's only
  volume, and its Prevent Spawning property belongs to the same idea. The 3D sphere is centred on the centrepiece,
  exactly as the client's exclusion sphere.
- **Creatures.**
  - An idle creature never picks a player inside the sphere (`Engage`).
  - A chasing creature drops a target who reaches it; its threat entry is removed and it leashes home (`Valid` in the
    chase).
- **Prevent Spawning** (type 18 [P by name]). No tree or plant (`Gathering.Ground` / `Allowed`) and no creature (cavern
  group spots, surface Fiery Fungus) spawns inside the sphere.
- **Cost.** At most ~64 + 81 distance checks per call, run only for players already within aggro range. No packets.

### (c) Surface graveyards (setting `SurfaceGraveyards`, Docker `SURFACE_GRAVEYARDS`, default true)

**Positions [I].** No client file places them (above), so:

- one per 1 km square of the ±4 km landscape grid;
- in each square, the first of 60 seeded spots on dry land (≥ 1005 m) whose ±13 m footprint is level within 4 m and
  whose slope is ≤ 0.4;
- none within 500 m of the landscape's middle (the Spire);
- only the terrain decides, so they stand in the same places after every restart.

A Python port on the baked grids gives:

| Landscape | Graveyards |
|---|---|
| 406 | 28 |
| 391 | 25 |
| 395 | 36 |
| 417 (Pioneer) | 64 |

The C# seeds differ, so the exact spots differ, but the counts will be about the same.

**Model.** `graveyard01.otd` (props only, so nothing is written to the terrain or the database):

- The centrepiece stands at the spot.
- Each other prop stands on the ground under it, at its height above the template floor (0.4 m) [I].
- Template rotations are kept.
- The centrepiece carries static area 23. The client then refuses build sites there by itself and (inferred) draws
  the red circle.

**Bandwidth.**

- All centrepieces of the player's landscape are sent once, on arrival (≤ 64 small props), so every exclusion sphere
  is known.
- A graveyard's other 42 props stream within 160 m and are removed beyond 220 m.
- Changing landscape removes what was sent.
- The check runs once a second. Nothing is sent per tick.

**Build sites.**

- `Claims.CanPlace` returns 3 (InvalidClaimLocation) when the site's box is within 30 m plus its half-diagonal
  (~204 m from the centre) of a shown surface graveyard, the client's own test.
- A graveyard that an existing build site already overlaps (same test) is hidden while the site stands. It is then not
  shown, not a Flee site, not a buffer zone and does not block sites. This is checked every 30 s per graveyard.
- Live removed such claims instead (P3540/P3548). Ours keeps the player's site [I].
- Prevent Spawning ignores sites, so what grows where never depends on sites coming and going.

**Respawn.** On the surface layer, surface graveyards join the existing sites: placed graveyard/respawn props, Starting
Locations, the Spire and the player's own site. The nearest wins. The arrival point is 6 m along +z from the
centrepiece, 0.5 m above the ground, with the client's ground snap.

### Settings

| Setting | Docker | Default | Effect |
|---|---|---|---|
| `SurfaceGraveyards` (WorldServer.json) | `SURFACE_GRAVEYARDS` | `true` | Surface graveyards on/off. Off: only cavern graveyards (always on) and the Spire. Read at start-up. |

---

## 3. Open leads (for OPEN-LEADS.md; not edited here)

- **L-a. Area property types 36 and 37.** On hub areas 3/4 (36 only), graveyard 23, 25 and 27.
  - *Searched:* rtti_695 `AreaPropertyData*` (no class returns 36/37), the client factory FUN_1431ae4f0 (not
    registered), UI/Default/Data.xml TriggerVolumePropertyTypes (not listed), patch notes, wiki.
  - *Try next:*
    - the 0.589 decompile and Data.xml (re/decomp_all) and older client builds for named types;
    - PlanetSide 2 / EQN shared area code (memory: shared engine code isn't dead) for an area-property enum;
    - forum dev posts on "safe zone" / "no PvP" near graveyards and hubs.
  - Candidate meanings (unproven): creature no-aggro (that would be the buffer) and no-PvP / no-combat.
- **L-b. Surface graveyard positions, count and template.** Server-only on live.
  - *Searched:* all `_Surface.cpa` (caves only), gameData695 (`TerrainObjectDpos.txt` empty, ExportedTemplates /
    VoxelSystemTemplates only poi_test), the templates folder, PropDefs (1480 "Graveyard Origin Marker" server-only),
    server Resources/PacketDumps (no world spawns), the wiki dump (no images), forum all/ (no counts).
  - *Try next:*
    - archived wiki images (Graveyard-example2.jpg, "a graveyard on a map") via archive.org with the browser when
      allowed;
    - forum_raw screenshots or posts with map shots;
    - YouTube / stream archives of the 2016 map;
    - the final 2017 CDN manifest (re/out/final-digest-2017) for server-side placement files;
    - whether live used graveyard01 or graveyard02 on the surface (P6194 "sunk too deeply" fits a voxel template such
      as graveyard02).
- **L-c. The map's red circles.** That the map draws the claims manager's exclusion spheres is inferred.
  - *Try next:* the map's claim overlay drawer, the readers of the exclusion octree (claims manager +0x480+0x200,
    FUN_142d9d0e0 callers), and a test-copy check with "Show Buildable Land" near a surface graveyard. Also check that a
    far graveyard whose centrepiece was sent on arrival shows a circle.
- **L-d. Stamping the cavern graveyard template.** Not stamped: graveyard_launch.otd's 9 voxel cells and its 24 other

> **Correction 2026-10-17 (agent-leads2-d1.md L47):** a ManuallyPlacedExportedTemplateData record is {u32 record version (= the file version 12 / 13 / 14), Room(version), u32 a (1-4, rotation [I]), u32 b (2 or 4: category), u32 c (template id: 76 graveyard on the islands; 61 / 75 on the old continents)}. The "first field 1-4" read earlier is **a**, the second u32. The retail client parses these records but never uses them (FUN_143ff7bc0 has no caller), so the server stamps them.
  props.
  - Needs: the meaning of the box's first field 1-4 (rotation? it doesn't move the box), and template id 76 (L47).
  - Today only the centrepiece stands on the cave floor.
  - *Try next:* the client's ManuallyPlacedExportedTemplateData use (FUN_143fa4190 / FUN_143fa3b70 readers' callers),
    then stamp it as the outposts are (VoxelTemplate.StampAt), with the same voxel-corner rule as ruin-stamping.md §5.
- **L-e. Which props carried areas 25, 27 and 3/4.**
  - Area 25 (r 30, exclusion and edit locks) may be the cave entrances or the underground portal crystals; the
    "InvalidClaimLocationCave" refusal is a separate check (code 2, octree +0x50).
  - Area 3/4 is the hub: our Spire props send no static area, so the client doesn't refuse sites near the Spire by
    itself.
  - *Try next:* the 0.695 Spire spawn handling, crystal / entrance props in the prop dump, runtime capture.
- **L-f. Underground Adventure arrival at the graveyard.** Forums 57166 / 56713 say live trips arrived at the cavern
  graveyard. `Caves.TravelRandom` arrives at the cavern middle (tile 104). Not changed here; a follow-up could send
  trips to `Graveyards` cavern arrival points.
- **L-g. The cavern-layer threshold (100 m below the ground) [I].** The client knows the cave tier the player stands in
  (FUN_142e03940 → FUN_143f34cb0; Local Travel layer). Live's server rule is unknown.
  - *Try next:* trace FUN_143f34cb0's tier source (VoxelContinentToLayerMapping / LayerInfo in the .cpa) and use the
    same tier server-side.
- **L-h. Buffer-zone details.** Live's behaviour when a player attacks from inside the buffer, and whether creatures
  also stopped walking in, is unknown. Ours: creatures never target a player inside, even one who hits them. Also
  unknown: where live put an arriving player (the wiki's "a player at a graveyard after reviving" image). Ours: 6 m
  along +z from the centrepiece, the area's own offset (0, 3, 6).
- **L-i. Claims already on a graveyard.** Live removed them (P3540/P3548). Ours hides the graveyard instead. Decide
  whether to keep that.
- **L-j. TerrainPermission kinds 0-3.** On hubs and area 25 they look like harvest / destroy / edit locks (Data.xml
  names), and the graveyard has only kind 4. The server doesn't enforce kinds 0-3 anywhere. Trace the client class's
  other uses of +0x68.

---

## 4. In-game test steps

**Setup.** This needs a world built from branch `e-graveyards`. That restarts the dev world: say so first, per the
project rule. Use the 0.695 test copy `<0.695 test copy>` with `re/dlogin.sh`. Dev commands are lines appended to
`server-data/worlds/dev/dev_commands.txt`; prefix `@Name` to target one character.

1. **Surface graveyards visible.**
   - Log in on the home landscape. The world log shows `Graveyards: N surface graveyards on landscape 406` (expected
     ~25-35) and, shortly after, `... 81 cavern graveyards on landscape 406`.
   - Run `graveyard`. Chat and log show the counts, "player on the surface layer" and the nearest surface graveyard
     with its distance.
   - Run `graveyard go`. You arrive beside a Graveyard Centerpiece with gravestones and fence posts around it, sitting
     on the ground (not floating or buried by more than ~0.5 m).
   - Expected: no trees inside ~30 m of the centrepiece.
2. **Red circle / claim refusal (client side).**
   - Press M, tick "Show Buildable Land": a small red circle should mark the graveyard (L-c; note what is seen).
   - Try to place a build site flag right at the graveyard. The client should say "The build site location selected is
     invalid. Please try again."
   - Move ~250 m away: placement is allowed again.
   - If the client lets it through, the server answers code 3 (same text). Check the world log for the refusal.
3. **Surface Flee.** Run `hurt 2000` near the graveyard (out of any build site). The death window's Flee/graveyard
   button lists "Graveyard". Click Flee: you respawn 6 m from the centrepiece.
4. **Buffer zone.**
   - Spawn a creature beside you: `kobold`, or `creature <kind>` (`creatures` lists kinds).
   - Step inside ~30 m of the centrepiece: an idle creature ignores you.
   - Get chased from outside, then run into the circle: the chaser gives up and walks home.
   - `graveyard` reports "in a buffer zone: True" there.
5. **Cavern graveyard.**
   - Run `cave` (Underground Adventure), then `graveyard`: "player on the cavern layer" and the nearest cavern
     graveyard.
   - Run `graveyard go`: you land on the cave floor beside a lone Graveyard Centerpiece (not on the surface, not in
     rock).
   - Run `hurt 2000` somewhere in the caverns, then Flee: you respawn at the nearest **cavern** graveyard of that
     landscape, not at the surface Spire.
   - Near it, cavern creatures leave you alone within 30 m.
6. **Setting off.**
   - Set `SURFACE_GRAVEYARDS=false` and restart (dev world restart).
   - Expected: no surface graveyards (`graveyard` shows 0 surface); surface Flee goes to the Spire / respawn points as
     before; cavern graveyards still work.
7. **Regression.**
   - A death on your own build site still uses its Starting Location / Respawn Points.
   - Story creatures on a build site still fight normally. A graveyard is never inside a site, and one a site overlaps
     is hidden.
