# The Pioneer site's slate slab on live (2016) — evidence and what the server builds

Cross-checked 2026-10-31 from four 2016 videos, the wiki and the client; written down 2026-11-01. Leads: OPEN-LEADS
L180 (the slab), L210 (seeing it), L260 (what is still open). Server: `LandmarkEmu/LandmarkEmulator.WorldServer/Game/
Pioneer.Slab.cs` (setting `PioneerSlab` = `site`, the default since 2026-11-01).

Marks: [P] proven (seen / read), [I] inferred, [I strong] inferred from several agreeing sources.

## Sources

| Source | Date | What it shows |
|---|---|---|
| Olgrey, YouTube `mRL8wKF36u4` | 24 Mar 2016 | a new character's Pioneer site from the first login; map view of the site |
| R3KT, YouTube `RAUbrp1aZNg` | spring 2016 | a Pioneer site, map and ground views |
| Verlin EQL, YouTube `YnBmejA5MWE` and `Lj7o-3o93Kg` | spring 2016 | Pioneer sites, slab edges, travel back to the site |
| markeedragon, YouTube `J-YDmIWHMzM` | spring 2016 | a Pioneer site and its Starting Location |
| wiki image `Pioneer-castle.jpg` | 1 Jul 2016 | the desert Pioneer land after the 30 Jun fix: a full square slab, ~1-2 m proud, Starting Location at a corner |
| Update notes 21 Mar 2016 (P4116, P4269) | 21 Mar 2016 | new characters get a site (Pioneer surface first); "teleport arrival point defaults to the lower north west corner of your claim" |
| Hotfix notes 1 Apr 2016 (P4367) | 31 Mar 2016 | a sunk manual site's arrival point no longer goes to the bottom north-west corner but to the terrain surface |
| Update notes 30 Jun 2016 (P5921; design/forum/patches/2016-06-29-57596) | 29/30 Jun 2016 | "Corrected some issues with the slate slab placement on new player claims." |
| Client 0.695 | — | ClaimCursor.ClaimSize and NewbieClaimSize "210 180 210" (StringHashToValue.txt); flora rules (below) |

## What live had

1. **Who got one.** New characters from 21 Mar 2016 got a site on a Pioneer land with a slate slab under it [P videos,
   P4116, P5921].
2. **Size.** On every March-June 2016 map view the grey slab exactly fills the floor of the site box: 210 × 210 m
   (the site is 210 × 180 × 210 m = 525 × 450 × 525 voxels of 0.4 m; client ClaimCursor.ClaimSize / NewbieClaimSize
   "210 180 210"); the site's corner beams stand on the slab's corners [P maps; I strong for the exact size]. A plain
   full square: no plus shape, no empty corners [P]. So not the client's claim3x3.otd (a plus with empty 70 m corners)
   and not claim1x1.otd (70 m).
3. **Height.** One level top plane [P]. Where the ground is lower, a crisp vertical face shows down to the ground, no
   gap under the slab [P frames]. Where the ground was higher, a vertical bank stands exactly on the slab's outline:
   live cut the ground above the slab top inside the footprint [P frames]. After the 30 Jun fix the wiki desert
   screenshot (1 Jul 2016) shows the full square standing about 1-2 m proud of the ground [P image, I for the exact
   number].
4. **Nothing on it.** No grass, trees, rocks or harvestables on the slab; natural ground, bushes and trees start right
   at its edge [P].
5. **Starting Location.** A "Teleport Arrival Point" / Starting Location prop stands on the slab about 3 m in from both
   edges at the north-west corner; "Travel back to your Build Site" lands there, facing into the site [P videos].
   21 Mar notes: "teleport arrival point defaults to the lower north west corner of your claim" [P]; 1 Apr hotfix:
   on manually placed sunk sites it goes to the terrain surface instead of the bottom north-west corner [P].
6. **First login.** The new character drops about 3 m onto the slab's interior, probably near the centre [I videos].
7. **Material.** Cracked grey slate [P look]; Worked Slate (VoxelMaterials ARRAY_INDEX 26) fits [I]; the server's
   `PioneerSlabMaterial` default is 26.

## Client rules that decide how it must be built

- **Grass** is decided only by the surface material (Worked Slate has no flora rows in the materials' flora data), the
  slope and the graphics settings — no area, claim or layer flag (client flora tables, cross-checked 2026-10-31;
  docs/terrain.md "Grass" for the per-triangle scatter) [P]. So the slab has to be
  real solid voxels (material bit set, not vertex-only voxels), and no generated ground may be left above its top
  inside the footprint, or that ground grows grass through it.
- **Samples sit at voxel corners**, a column of generated ground is solid below its height, and a cube without a
  vertex flag is drawn at its centre (docs/terrain.md "How the client draws a dig") [P]. A slab whose voxels carry
  centred vertices is a flat box with its faces half a voxel outside its outermost samples.
- **North.** The client's HUD / map compass (0.695 FUN_14332c6b0, LocalProxiedCharacter::UpdateHudCompass) turns by
  atan2f(v.x, v.z) of the camera vector (FUN_1437f7c8c is atan2f) and sends it to the CompassWidget
  (WorldMapRotateCompass → rotateCompass) [P]: heading 0 is +z, so north = +z. That +x is east (not west) follows
  from the engine's left-handed frame seen from above [I]; the compass art (which way the rose turns) was not checked.
  So north-west is taken as the site's -x / +z corner [I]. Claims.EnsureStartingLocation and Consumables.Direction use
  the same assumption; they are not independent evidence.

## What the server builds (PioneerSlab = site)

- One solid slate square covering exactly the site's footprint (the 525 × 525 samples inside the box; its faces on the
  box's outline), 5 voxels (2 m) thick, one level top: its bottom layer is the first air sample above the generated
  ground at the site's middle, so the top is 1.8-2.2 m above that ground [I from the 1 Jul screenshot].
- Per column (each 0.4 m sample, from the baked heights): solid slate from the slab's bottom down to the generated
  ground (and 2 voxels into it, against the heights' rounding), so the face reaches the ground; air (cut) from the
  slab's top up to 2 voxels above the generated ground where that ground rises above the slab, so hills become
  vertical banks at the outline. Cave air is never filled; nothing outside the footprint is written (except the one
  column outside the -x face that an older claim3x3 stamp reached, reset to the generated ground).
- Starting Location on the slab top, 3 m in from the -x / +z corner; a first login arrives 3 m above the slab's
  middle; trees, plants and decoration in the site are taken out when it is placed.
- Old Pioneer sites keep what they have; an admin stamps one with nothing built on it with the dev command
  `@Owner pioneerslab again` (a site with building in its box is skipped unless `force` is added; cavern sites never
  get one; docs/gameplay.md).

## Open (OPEN-LEADS L260)

Size and height after the 30 Jun fix (exactly 210 m? exactly 2 m?); the exact material (Worked Slate or another
slate); the first-login spawn point; the yellow dashed lines seen on the slab in the videos; north: heading 0 = +z is
proven, but whether +x is east or west depends on whether the CompassWidget's indicator.mcRotate is the needle or the
rose (art not checked).
