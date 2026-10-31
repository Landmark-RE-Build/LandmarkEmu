# Spire build-site exclusion (2026-10-31, Task A)

User report (2026-10-31): players can claim the Spire area. Research only: no code changed. Server state: client-0.695,
deployed f6fb4340. [P] = proven from the client, data or sources. [I] = inferred.

## 0. Short answer

- The client already has the Spire's area: **GlobalAreas 3**. It is a **sphere of radius 90 m centred on the prop that
  carries it** (GlobalAreaShapes row 1). It has the claim-exclusion permission (TerrainPermission kind 4), edit locks
  (kinds 0-3), "Prevent Spawning", and the message strings **HubEnter / HubExit** [P data]. It matches the 6 Oct 2014
  hotfix word for word (P1829-P1832, section 4) [I that area 3 is the area those notes mean].
- No client file says which prop carries it. Props have no area column; the template's prop records hold none; the
  live PropBase dump holds none. Live's server sent the id in the spawn, as it did for graveyards and movers [P].
- Our Spire props send **no StaticAreaId**, and `Claims.CanPlace` has **no Spire check** [P]. So neither the client
  nor the server refuses a site on the Spire. That is the bug.
- Fix: send StaticAreaId 3 on the Hub prop (PropDefs 1871, model 596), and add the same test to `Claims.CanPlace`.
  - The client then refuses the spot by itself: the cursor shows disallowed, the place key shows
    "Claims.InvalidClaimLocation", and no PlaceClaimRequest is sent [P chain; I that it fires for this prop, untested].
  - The server check is still needed: an admin flag skips the client check, and a crafted request skips it too.
- **Exact client rule** for the launch site (210 × 180 × 210 m). Let d = site centre − Hub pivot, in metres. The site
  is refused when **|dx| < 195 and |dy| < 180 and |dz| < 195 and |d| < 263.64** [P formula]. In practice:
  - along an axis, the site's centre may come within 195 m of the Hub;
  - so the site's edge may come within 90 m of the Hub pivot, about 43 m outside the platform's ±47.19 m footprint.

## 1. Client data [P]

Areas with TerrainPermission kind 4 (AreaProperties type 17, INT_PARAM1 = 4):

| Area | Shape (GlobalAreaShapes) | Other properties | Notes |
|---|---|---|---|
| **3** | row 1: type 1 sphere, params 0, 0, 0, **90** (offset 0, radius 90) | rows 1-4 + 10: kinds 2, 3, 1, 0, 4 (INT3 = 1); row 11: type 18 INT1 1, FLOAT1 1.5 (Prevent Spawning); row 13: type 21 "HubEnter" / "HubExit"; row 59: type 36 | GlobalAreas row: REQUIREMENT_SOURCE_TYPE 1, REQUIREMENT_SOURCE_ID 4 (also area 27; meaning open, L138) |
| 4 | row 2: type 2 **box** −10…10 on every axis | rows 5-9: kinds 2, 3, 1, 0, 4; row 12: "HubEnter" / "HubExit"; row 60: 36 | a box makes **no** exclusion sphere (see §2); the second hub area (a crystal or ley-line hub?) [I] |
| 10 | row 8: sphere at (−4.1, 1188.5, 46.69), r 5100 | kinds 2, 3, 1, 0, 4 | areas 10-13 go together with area 11's "DEI Restriction Message" — some special landscape, not the Spire [I] |
| 12 | row 10: sphere r 360 at the prop | kinds 2, 1, 0, 4; Prevent Spawning | same family |
| 13 | row 11: sphere at (−4.1, 1188.5, 46.69), r 45 | kinds 2, 1, 0, 4; Prevent Spawning | same family |
| 23 | row 20: sphere (0, 3, 6), r 30 | graveyard (33), 18, 36, 37 | in use: Graveyards.Landscape.cs |
| 25 | row 22: sphere r 30 | kinds 4, 2, 3; 18, 36, 37 | carrier unknown (agent-graveyards L-e) |

- Area 27 (sphere r 360; Prevent Spawning, 36, 37; the same requirement source as area 3) has no TerrainPermission,
  so it can't exclude sites. It might be a no-spawn ring around the Spire [I, lead S4].
- Types 18, 21, 36 and 37 have no client class (factory FUN_1431ae4f0). They are the server's to act on [P].

**Which prop carries area 3.**
- base_portal_spire.otd (v6, 320 cells, 310 × 170 × 310 voxels) has 7 props:
  - 1871 Portal Spire (Hub), model 596, at template (60.07, 21.24, 63.92);
  - 575 Portal Crystal (59.55, 19.71, 64.55);
  - 2942 The Crimson Parlor, 2894 Replicator, 2895 Repair Station, 489 Mailbox, 884 Resupply Chest.
- None of the template's prop records holds an area:
  - the u64, u64 and u32 after the scale are 0 on every prop;
  - the six non-Hub props have one 20-byte list-2 entry {u32 4, u32 1, u64, u32}, with no area id in it (the Hub has none);
  - the file ends with a JPEG thumbnail.
  Script: scratchpad spire/otdx.py.
- PropDefs.txt has no area column.
- The live PropBase dump (Resources/PacketDumps/145-PropBase) holds, per prop: model name, id, internal name,
  description, f32 × 2, image set, a category (0/1/4/11), u32 (0/1/2), model. No area [P]. Script: scratchpad spire/pb.py.
- So **live's server sent it** [P that it must be sent; I which prop]. The Hub is the natural carrier: it is the
  Spire itself, its pivot is the respawn and arrival reference (`Spire.Site.Position`), and the area says
  HubEnter / HubExit. The crystal stands 1.7 m from the Hub's pivot, so the choice moves the sphere by 1.7 m at most.

**Today:** `Spire.Prop()` / `BuildProps` (Spire.cs ~1001-1040) set no StaticAreaId and no AreaGuid on any Spire prop.
The fallback without a template (two models at `site.Position`) sets none either [P].

## 2. How the client uses it [P, 0.695 addresses]

1. **Spawn.** PlayerUpdate 0x62 → FUN_142d54040. A non-zero +0x260 StaticAreaId is looked up in the static-area table
   (game +0x37910). Then the area manager ClientAreaDataManagerClient (DAT_1454319d0, vtable 14471cb48) creates the
   area through slot 5, FUN_143d67420 → FUN_143d68c50. That call makes a ClientAreaData of kind 4 (the static area,
   ctor FUN_143d91df0) under guid = spawn +0x268 (AreaGuid).
   - There is **no duplicate-guid check**: areas are chained in a 128-bucket hash by guid.
   - Every shape gets the area guid as its parent (+0x188).
   - Properties are made by type, and shapes from the area definition.
2. **Exclusion sphere.** ClientAreaTerrainPermission (vtable 1447d2450) slot 10 is FUN_1436de260. It runs only when
   INT1 (+0x68) == 4 and the area's kind (vfunc 0xe0) == 4. For **each shape of type 1 (sphere)** it builds a
   `ClaimPlacementExclusionZoneSphere<float>` (vtable 1447d2580):
   - the centre is the prop/area position (vfunc 0x168);
   - the radius is the shape's +0x280;
   - the shape's offset (+0x270…+0x27c) is read into locals and **never used**, so the centre is always the prop itself;
   - box shapes produce nothing.
   The sphere is filed through FUN_142db3880 → FUN_142db3600 into the claims manager's (+0x480)+0x200 octree. Its
   stored box is the centre ± r (slot 7 FUN_1436ddb90 → FUN_1436dd1a0, w = 0).
   - FUN_1436de260 is the only place in the decompile that builds a sphere. The class has no other subclass; the base
     ClaimPlacementExclusionZone is abstract.
   - [I] When slot 10 runs (area init or position set) was not traced. It needs the area's position, and the
     graveyards depend on the same step (also untested in game).
3. **Check.** FUN_142dc6870(mgr+0x480, site box):
   - the octree query FUN_142d9d0e0 collects the spheres whose stored box overlaps the site box, strictly on x, y and z
     (the w lane is masked out);
   - **only the head of that list** (the last one pushed) gets slot 9, FUN_1436ddd80:
     `|box middle − centre| < radius + |max − min| / 2` (half the box diagonal);
   - a hit returns 3.
4. **Results.**
   - FUN_142dce0e0 (the place key): result 3 shows "Claims.InvalidClaimLocation" ("The build site location selected is
     invalid. Please try again.") and sends **no PlaceClaimRequest**. 1 is BufferOverlap and 2 is Cave.
     SendSelf +0x287 (DAT_145431a20, admin) skips the check.
   - FUN_143663cd0 (the claim cursor, every frame): a non-zero result sets cursor state 5 (disallowed;
     ClaimCursor.DisallowedTintColor .75 0 0 1 [I that state 5 uses that tint]).
   - StringHashToValue also has ClaimCursor.PlacementExclusionZone* textures and a tint (0.60 0.86 0.84 .4). The
     client may draw the zones during placement [I, drawer not traced].
5. **RecievePropExclusionArea** (0xF9 sub 0x11) does **not** fill this octree. Its handler FUN_142dd33f0 adds or removes
   entries in another manager (FUN_142ccbaa0; mode 1/2 add, 3 remove). The note in trace/systems/claims.md (step 2,
   "filled by RecievePropExclusionArea") is therefore wrong for 0.695 [P]. A static area on a prop is the **only** way
   to make the client refuse a site spot.
6. **Other client effects of area 3.** Area 3 also carries TerrainPermission kinds 0-3 (INT3 = 1 = forbid; INT2 is
   the priority at +0x6c). FUN_1436af020(tool, kind) collects the kind-N permissions of the areas overlapping a tool
   box (the area is found by the shape's parent guid +0x188 through FUN_143d6aa40), and the highest priority wins. It
   is read for:
   - **kind 2** by FUN_1436acd20: a forbid returns "not allowed" before the claim-permission test FUN_142db4b50
     (mask 0x100);
   - **kind 3** by FUN_14378cf50 (gated by DAT_14548ef68 and tool mode 3).
   So inside the 90 m sphere the client itself refuses whatever those tool checks gate [P read; I which tools]. This is
   live behaviour (P1831 "You can no longer mine / harvest in this area"), but the server's current no-edit rule is
   only 10 m around the platform box (Spire.NoEditMargin, ours).

## 3. The exact rule

Inputs:
- The launch site box is 210 × 180 × 210 m (ClaimCursor.ClaimSize "210 180 210"), centred on the requested point;
  half extents (105, 90, 105).
- Half the box diagonal = ½·√(210² + 180² + 210²) = **173.64 m**.
- Area 3: r = 90 m, at the Hub's pivot (`Spire.Site.Position` = the 1871 prop's position).

Client refusal, as the code above runs it. Let d = site centre − Hub pivot (local metres). Refused (code 3) iff **all**
of these hold:
- |dx| < 105 + 90 = **195**, |dy| < 90 + 90 = **180**, |dz| < **195** (the octree box overlap), and
- √(dx² + dy² + dz²) < 90 + 173.64 = **263.64** (the sphere test).

What that means on level ground:
- On an axis the nearest allowed centre is 195 m away: the site's edge sits 90 m from the pivot, about 42.8 m outside
  the platform's edge.
- In the corner band (|dx| and |dz| both between about 177.4 and 195 m) the 263.64 m sphere decides.
- The test is 3D: a site floated very high or sunk very low next to the Spire could pass. In practice |dy| < 180
  always holds there (the height offset is limited to −75…+150 m).

The server must use **both** terms. A sphere-only test (|d| < 263.64) would refuse spots the client allows (for example
200 m due east). The player would see a valid cursor and then get "invalid location" from the server.

## 4. Live rule [P sources]

- **3 Oct 2014 hotfix** (forum 48918):
  - P1821: known issue "being able to claim and mine around spire hubs";
  - P1822: "trees growing on hubs";
  - P1828: "until we can fix the claimable spires bug ... please do not claim the land on a spire hub. If you do ...
    your claim WILL be deleted".
- **6 Oct 2014 hotfix** (forum 48991):
  - P1829: "addressed a few issues in regards to the area surrounding the spire";
  - P1830: "That area will now properly suppress procedural props";
  - P1831: "You can no longer mine / harvest in this area";
  - P1832: "You can no longer place a claim in this area";
  - P1833: a site placed over a Spire → /reportclaim, CS removes it.
- These three rules are area 3's three property groups: Prevent Spawning (18), TerrainPermission kinds 0-3 (edit and
  harvest locks), and kind 4 (claim exclusion) [I-strong].
- 5 May 2015 (P3467): "space reserved for [Landmarks of Landmark] beside each spire". 14 Mar 2016 (P4081) and
  5 Aug 2016 (P6383): they were placed by the spires. Those are staff sites (P6379: landmark sites are edited only with
  dev powers), so they don't go through the player rule.
- 1 Apr 2016 (P4425): "If you try to mine beside the spire you should now get clearer feedback" — client text 560944
  "You are near a Portal Spire. You must move away before you can mine or build."
- Wiki "Build Sites" (launch era): "many players opt to place Build Sites next to a main (near-center) Spire". So
  sites could stand close by, which fits a 90 m area (edges about 43 m from the platform), not a large ring.
- The wiki pages "Spire", "Hub" and "Claims" and the forum archives (design/forum/all, re/out/forum_raw) give **no
  distance figure**. The figure comes from the client data above.

## 5. Server: what to add (proposal; nothing changed)

1. **Spire.cs.**
   - In `BuildProps`, give the Hub spawn (PropId 1871, index 1 = `GuidOn(zone)`; in the fallback, the SpiresModel 596
     prop) `StaticAreaId = 3`.
   - Give it a **unique AreaGuid** (a tag | the prop guid, like `Props.Movers.MoverAreaTag`), because areas are hashed
     by guid without a duplicate check and the kind-N queries find areas by guid [P hash; I that guid 0 would collide
     with the graveyards' guid-0 areas].
   - Set `Name = " "`: a spawn with an area guid and an empty name gets the floating label "area" (FUN_142ec18c0, as
     Props.Movers notes).
   - The Area Tool can't open it: it only edits areas with exactly one box shape, and area 3 is a sphere [P,
     area-definitions-695.md §4.3].
   - Bandwidth: one u32 + one u64 on a spawn that is already sent. The Hub goes on every arrival, and also early during
     the stream hold (it is the deck, `IsDeck`). So every client knows the exclusion before it can place a flag. Nothing
     per tick.
2. **`Spire.BlocksSite(zone, x, y, z, halfSize, halfHeight)`** (global dm in, like `Graveyards.BlocksSite`):
   - use `Ensure(zone)`: CanPlace isn't terrain code, and the player is on that landscape;
   - centre = `site.Position`; radius `SpireExclusionRadius = 90f` (GlobalAreaShapes row 1);
   - refuse when the box term **and** the sphere term of §3 both hold.
   - `Claims.CanPlace`: after `IsFree`, `if (Spire.BlocksSite(...)) return ResultInvalidLocation;` (3, the client's
     own code and text). PlaceConfirm re-runs CanPlace, so a confirm can't slip through.
3. **Shared helper.** Put the client test in one place (for example `ExclusionSphere.Blocks(centre, r, siteMiddle,
   half)`) and use it from Graveyards too. `Graveyards.BlocksSite` today has **only the sphere term**, so for a 30 m
   graveyard it refuses centres the client allows: the client refuses only when |dx| < 135, |dy| < 120, |dz| < 135
   *and* |d| < 203.64; a centre 150 m due east is refused by our server and allowed by the client [P from both codes].
4. **Pioneer.FindSpot** (server-placed Pioneer sites; Grant and Relocate) checks only the platform box ± 105 m. Use
   the same test. With Spacing 390 m and the Spire on the middle cell nothing changes. It matters only when SpotFor
   moved the Spire off (0, 0).
5. **Landmarks of Landmark** (`Claims.AddServerSite`) skip CanPlace, which is right: live staff placed them beside the
   Spire.
6. **Sites already over a Spire.** Live deleted them (P1828 / P1833). Accounts will be reset, so: log them at start-up,
   with a setting to remove them (live) or keep them (ours).
7. **Setting.** Live default plus a toggle (project rule "live default + toggle"): for example `SpireClaimExclusion`
   (default on), which controls both the area on the Hub and the server check.
8. **Same area, optional, live rules (P1830 / P1831):**
   - Trees and plants: suppress them in the 90 m sphere (Gathering.Allowed today uses `Spire.Covers`, the platform box
     ±49.19 m).
   - Mining and building: refuse them in the sphere (`Spire.MayEdit`, today 10 m around the platform box).
   - Once the area is sent, the client already applies kinds 2/3 in the sphere, so the server should match.
   - Both are gameplay changes near the Spire: behind settings, default live.

## 6. Open leads (next steps)

- **S1 — in-game proof.** On a patched test copy (memory: test 0.695 only; delete the copy afterwards), send area 3 on
  the Hub. Then: flag right-click near the Spire → the cursor is disallowed within the §3 box; the place key shows
  "Claims.InvalidClaimLocation"; 196 m due east is allowed. Also check that "Show Buildable Land" draws the zone
  (agent-graveyards L-c) and what kind 2/3 forbid does to the mining and harvest tools by the Spire. Prove it from the
  client's own reaction (chat text, cursor), not from server logs.
- **S2 — when ClientAreaTerrainPermission slot 10 runs** (area init vs position set): trace the area vfuncs 0xc0 / 0x178
  that FUN_142d54040 and FUN_143d67420 call. Also check whether the claims manager removes the sphere when the entity
  is deleted at a re-zone (duplicate spheres would be harmless).
- **S3 — kinds 0-3.** Which tools FUN_1436acd20 (kind 2) and FUN_14378cf50 (kind 3) belong to: vtable owners of those
  slots. Also kinds 0/1 (likely server-only harvest / destroy) and the Data.xml names (Forbid Harvesting / Destruction
  / Editing, Priority).
- **S4 — area 27** (r 360, Prevent Spawning, 36, 37, the same requirement source as area 3): is it a second Spire ring
  (no procedural spawns, or the reserve for the Landmarks of Landmark)? And area 4's carrier (box ±10 m, hub messages).
  Try the 0.589 tables / prop dumps and the requirement-source meaning (L138).
- **S5 — GlobalAreas REQUIREMENT_SOURCE_TYPE 1 / ID 4** on area 3: does it gate the area's effect per player? The
  exclusion code does not read it [P: FUN_1436de260 checks only +0x68 and the shape type], so the claim rule is
  unaffected.
- **S6 — which prop** carried area 3 on live (Hub vs crystal). It changes the centre by about 1.7 m. No client source
  found yet. Searched: the template, PropDefs, PropBase dump, wiki, forum.

## Adjacent (Task B, not studied here)

- Wiki "Build Sites", Location Tips: you can chop a tree before placing a site "or place the Build Site, which will remove
  all three Prop-Trees" — live removed trees inside a newly placed site.
- `Claims.PlaceConfirm` today calls only `Terrain.ZoneTerrain.ClearVeinsInSite` and nothing for Gathering nodes [P,
  Claims.cs ~640].
