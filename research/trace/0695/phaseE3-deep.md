# Phase E3 (agent deep): changes 2-4 calls below the server-relevant 0.695 roots

2026-10-26. Brief: trace/0695/agent-phaseE3-brief.md. Slice: close the depth-1 limit of the part-2 rebase
(phaseE2-rebase.md "Limits"; re/tools/rebase695.py compared a cited function and its direct callees only). Read-only:
nothing in the server, docs or traces was changed. Scripts (scratch, private):
`<user folder>\AppData\Local\Temp\claude\<workspace>\28d489f2-5945-4c82-ba0b-9796b72f5283\scratchpad\e3-deep\`
(`walk.py` the walk, `sig.py` logic-only diff, `d.py` readable diff, `newconst.py` new compare / case constants,
`rng.py` field-write search; outputs `nodes*.tsv`, `sig*.txt`, `consts.txt`, `newconst.txt`).

## Method

1. **Roots** (21,297): every 0.695 S→C dispatcher case and zone handler row of re/out/agent_coverage_e/sd_695.tsv
   (20,285 rows; their call lists are depth 1 — used instead of trace/families_695, whose case lists still carry 0.589
   addresses, lead L-R1), every C→S builder (inst_695.tsv "stack" rows of the 536 c2s_695.txt classes, 588 rows) and
   every Lua native that sends (luabind_695.tsv "sends", 424). A second run used the 3,332 functions the traces cite
   (rebase695.tsv addr_695) as roots, so depth 3-4 there is exactly "2-3 calls below a cited function".
2. **Walk** the 0.695 call graph (re/out/agent_funcs/index.pkl, direct FUN_ references) to depth 4. Not expanded:
   functions with more than 40 callers (library), callees with more than 120 callers (stream / string / alloc
   primitives; the 4 changed ones were diffed separately — all recompilation), systems "Noise", "UI framework",
   "Graphics", "Third-party", "Settings, audio" (re/out/agent_funcs/final.tsv), and the dispatchers themselves (their
   cases are roots). A third run expanded every other handler over 20 KB as well (7 more functions, all fine).
3. **Pair and filter**: 0.589 partner from trace/0695/functions.tsv; kept `code` new / changed rows whose final.tsv
   kind is not layout-only / same-logic / new-boilerplate.
4. **Already seen**: cited in any trace/**/*.md (except the generated families folders), LandmarkEmu/docs/*.md or a
   server .cs comment, or compared by the rebase (a rebase-cited function or one of its direct callees).
5. **Read**: every remaining function through `sig.py` (rcmp-style normalisation, 0.589 calls renamed to their 0.695
   partners, numbers folded, plain field initialisations dropped → only changed if / loop / call / return lines are
   left), then the raw bodies where anything was left. Two extra passes over **all 757** reached changed functions
   (cited or not): changed numbers on compare / case / return lines with the same shape (`consts.txt`), and compare /
   case constants that exist only in 0.695 (`newconst.txt`).

| Run | reached | changed (logic) | already seen | read here |
|---|---|---|---|---|
| packet roots, depth 4 | 10,045 | 575 | 448 | 127 |
| cited-function roots, depth 3-4 | 12,322 | 86 at depth 3-4 | – | 37 not in the packet run |
| + large handlers expanded | 10,879 | 592 | – | 7 more |
| functions cited only by generated tables (coverage-695.md, address-map, FAMILIES_695) | – | 6 | – | 6 |

## 1. Findings (by player impact)

### F1. 0.695 prop placement: the drop point must be in the same build site as the point where the session opened — the server opens new placements 20 m below the player — GAP (+ small MISMATCH)

**Client [P].** 0.695 adds a same-site test to the placement cursor's validity:
- FUN_142fbd5f0 (0.589 FUN_142f1e540), the per-frame placement update: after the build-permission check passes it
  now compares `FUN_142dbbf20(claims, tool+0x200)` with `FUN_142dbbf20(claims, cursor+0x70)` and, if they differ,
  marks the prop invalid (`prop+0x4b58 = 0`, red highlight FUN_14362e890(...,2)) and returns. 0.589 has no such test.
- FUN_142fc1950 (0.589 FUN_142f22760), the validity check run when a session opens, has the same new test
  (`FUN_142dbbf20(…, tool+0x200)` vs the claim at the prop's position).
- tool+0x200 is the **position of the prop when the session opened**: FUN_142fb19b0 (= 0.589 FUN_142f12b30, same
  code) copies the prop entity's position there, called from FUN_142fc0790 when the edited prop is set (session start,
  FUN_142fb9ba0 line ~259, with no move before it). It is re-recorded only after a valid Save (FUN_142fb3e40, which
  needs +0x4b58 set) or when a server transform update arrives for the edited prop (FUN_142fbb930).
- FUN_142dbbf20 → FUN_142d9c080 → FUN_142d9d660 is a 3-D box test (x, y, z each against the box's min / max), so a
  point above or below a site's box is "no site" (0) [P].

So a placement whose start point is outside the player's site box can never become valid: start site 0 ≠ cursor
site X for every cursor position, and the first valid Save that would re-record the origin never happens.

**Server.** `Props.BeginPlacement` spawns the waiting prop at `owner.Position - (0, PlacementSpawnDepth = 20 m, 0)`
(LandmarkEmulator.WorldServer/Game/Props.cs:536, :608, branch client-0.695 HEAD 8219f7c; commit c617e50 of 2026-10-14, chosen against the 0.589 client,
which had no origin test). Site boxes are ±90 m high around the site centre (Claims.cs:37 HalfHeight 900 dm, MinY
:634 / :1921). A player standing in the bottom 20 m of their site's box — the downhill side of a 210 m site on a
slope, or a deep basement — gets a prop that cannot be put down anywhere (the cursor stays red; FUN_142fb3e40 commits only
while +0x4b58 is set) [I: geometry; the client test itself is P]. Moving an already placed prop (Access) is not affected
(its origin is its real position).

**Small mismatch.** `Props.Save` (Props.cs:1186-1233) accepts a placed prop moved by its owner to any point where
`MayPlaceAt` passes, also into a different site the player may build on; the 0.695 client never sends such a move
(the cursor is invalid outside the start site), so the live server's rule was "stays in its site". Low impact
(only a modified client can do it), but it can move props past a site's prop / chest caps that are counted per site.

**Fix I would make.**
1. BeginPlacement: keep the spawn inside the player's site box. Take the box of `Claims.ClaimAt(owner.ZoneId,
   owner.Position)` (already required by the MayPlaceAt test above it) and spawn at
   `y = max(owner.Y - 20, boxMinY + 1 m)`; if that is less than ~3 m from the player (no room below), spawn above
   (`min(owner.Y + 20, boxMaxY - 1 m)`) — still out of the player's way, still in the same site. Add a helper
   `Claims.SiteBoxBounds(zone, site)` (the min/max are on ClaimModel). Rule test: a player 5 m above a site's floor
   gets a spawn point whose ClaimAt is the player's site.
2. Save: when `wasPlaced`, refuse (same LocationInvalid SaveReply) if `Claims.ClaimAt(prop.Zone, old position) !=
   Claims.ClaimAt(prop.Zone, target)`; document the client rule (FUN_142fbd5f0) in docs/props or gameplay.md
   "Placing props".
3. **In-game check needed:** stand within 20 m of the bottom of your site's box (or dig down ~75 m below the site
   centre), place any prop from the palette — before the fix it should stay red everywhere; after, it places.

### F2. Designs: a cached Design with no cells and no props counts as "not loaded" — OK-NOTE

FUN_143115120 (Lua Templates.Select, 0.589 FUN_14306a840) and FUN_143110480 (Templates.PopulateTooltip, 0.589
FUN_143066210) now use the cached template record only if `record+0x158 > 0` (cell count of the voxel volume at
+0x130) or `record+0x128 > 0` (the PropData array at +0x118, which 0.695 moved from TemplateBasicData into the
VoxelDataResponse, FUN_1431048f0 / FUN_143104790); otherwise they queue a new VoxelDataRequest (0xF0/4) through
FUN_1431136c0 with the new defer flag (1, 1) [P]. Templates.Select also now returns true in that case. Server:
Templates.SendVoxelData (Templates.cs:892-918) always writes the cells and the props, and sends nothing for an
unknown design — matches; an empty design (no blocks, no props) would be asked for again on every select / tooltip
(throttled 25 per burst by FUN_14310b4d0, functions-695.md §11), harmless. The summary comment at Templates.cs:23
("unless they are cached") could add "and hold cells or props (0.695 FUN_143115120)". No change needed.

### F3. Lua natives that send now check the connection first — OK-NOTE

ClientInventory.SetItemsToSlots / SplitStack / MoveOverflow (FUN_1435decb0, FUN_1435def90, FUN_1435df790),
Recipe.RepairStationRepairAll (FUN_1435edc50) and the Teleport natives (FUN_143173520, FUN_1431735b0, FUN_143173640,
FUN_1431738c0 RequestTeleportLocations, FUN_143173920 NextUnderground, FUN_143173980 RandomUnderground) wrap the send
in `if (DAT_145431088 != 0)` (the zone connection) [P]. Same packets, same fields; client-only.

No other function in the walk changed in a way the server sees.

## 2. Checked and fine

Uncited changed functions read (packet run), grouped:
- Claims: FUN_142dc95a0 OnExitClaimBuffer (client state +0x3d8 only); FUN_142dab3f0 / FUN_142dab4e0 site-game struct
  copy / ctor (the known trailing u32, packets-695.md §5); FUN_142db6db0 / FUN_142db6eb0 / FUN_143c16e40 (offsets);
  FUN_1434bc510 / FUN_1434c7360 / FUN_1434c7d50 / FUN_1434c8ef0 / FUN_1434c68f0 packet-id name tables (the known new
  subs: claims 0x45 / 0x46 / 0x63, mail DeveloperMailPlayer, PU SetPlayerBio, Prop NpcDialogResponse).
- PlayerUpdate readers FUN_142c276d0, FUN_142c28a60, FUN_142c2ad20, FUN_142c2af10, FUN_142c2b270, FUN_142c2b3f0,
  FUN_142c2b720, FUN_142c2b7e0, FUN_142c2ae00, FUN_142c28930, FUN_142c2b660: only the known u8 → u32 sub.
- Designs: FUN_143104790 / FUN_143104480 / FUN_1431048f0 / FUN_143104870 (PropData array in the record, F2);
  FUN_14310d4a0 / FUN_14310ec80 / FUN_14310d5d0 / FUN_14310ee00 (new array helpers); FUN_1431bec60 (call renamed);
  FUN_1435db160 PasteInCurrentClaim (defer flag); FUN_143113410 SelectAdminContributors (defer flag);
  FUN_1431167c0 tooltip size text (cast only); FUN_143524890 (array clear inlined).
- Site games: FUN_1434d55e0, FUN_1434ef860, FUN_1434dfa00, FUN_1434ef430, FUN_1434dec30, FUN_143c1b5c0,
  FUN_1434de210, FUN_1434dd720 (settings / trigger containers re-keyed by hash; functions-695.md finding 1),
  FUN_1434da120 (AddOrRemoveRule: local rule value + dirty flag), FUN_1434ea410 / FUN_1434ea770 / FUN_1434ebce0 (HUD
  entry defaults "Untitled", entry size 0x40 → 0x50: the known trailing bool + u64), FUN_1434f0370, FUN_1434e6b30,
  FUN_1434ea350, FUN_1434ea5c0, FUN_1434ea6b0, FUN_1434ddad0, FUN_1434ddc50, FUN_1434ebb10, FUN_1434dcd60,
  FUN_1434f0a60 / FUN_1434f0e80 / FUN_1434f11b0 (End / Observer / Start request builders: no logic change).
- Mail natives FUN_143638580, FUN_1436386e0, FUN_14363d1e0, FUN_14363d240, FUN_14363f0b0, FUN_14363f120 (no logic
  change); FUN_14363f420 (bool text narrow string); FUN_14363a660 (IString recompilation).
- Social / chat: FUN_142ef7600, FUN_142efb400 (guild data publishers: argument passing), FUN_143362410,
  FUN_143371f90 (IString recompilation).
- Props: FUN_142fc00e0, FUN_1435c1600, FUN_1430a8dc0, FUN_1435bceb0 (member destructors), FUN_142fb6ee0 (transform
  copied from doubles), FUN_142c79a60 (cleanup calls).
- Character / customization: FUN_14309c7a0 (0x116 tint data → CharacterCreateOptions rebuild, data-table driven;
  barbershop gating already in functions-695.md §9), FUN_143090540, FUN_143093820, FUN_1430988f0, FUN_143098960,
  FUN_14308f240, FUN_14309e260, FUN_1430bf5e0, FUN_1430c3cf0, FUN_1430bccf0, FUN_143096f80, FUN_143098090,
  FUN_1430c1bd0, FUN_1430bcea0, FUN_143c02540 (render LOD); 0xE3 "cases" are the client run loop FUN_142cd4e60, not
  a packet switch.
- Vehicles / movement: FUN_142f788f0, FUN_14303aff0, FUN_14304a9b0, FUN_14332a8b0, FUN_14302e590, FUN_14302e0e0,
  FUN_14302f9d0, FUN_143034760, FUN_14303d5b0, FUN_14303db30, FUN_143042e50, FUN_143043500, FUN_1430504c0,
  FUN_14302ee90, FUN_143039360, FUN_142c0c7a0, FUN_142cc2eb0, FUN_143041c40, FUN_143041f20, FUN_14302c9f0,
  FUN_14302bd20, FUN_143045c80, FUN_1430516d0, FUN_142ebd9f0, FUN_142eb54b0, FUN_14332c2b0, FUN_143333490,
  FUN_143335de0, FUN_142ecc640 (moved float constants / offsets only); FUN_143d89ba0 / FUN_143d89bd0 (new thread
  check before a physics call).
- Core / other: FUN_142d100d0, FUN_142cd09f0, FUN_14360d7d0 (refining tooltip), FUN_1435646a0 (coin store text),
  FUN_142e5bc40, FUN_142b3eb00, FUN_1433ce560, FUN_1433d20c0, FUN_143b0e230, FUN_1435bb9e0 (critical sections),
  FUN_1431a3480, FUN_1431b2db0 (area property lists), FUN_142ce7600, FUN_1433e4730, FUN_143133360 (settings
  registration), FUN_143d57080, FUN_143451bb0, FUN_14360fef0, FUN_14376daf0, FUN_1434478d0, FUN_143693680.
- Cited-root run, terrain / paste engine (hand-off #2, terrain-695.md, functions-695.md "Paste"): FUN_142a8f470,
  FUN_142ab7820, FUN_142aef300, FUN_142af41c0, FUN_142af9c80, FUN_143160eb0, FUN_1436bc200, FUN_1436cb6d0,
  FUN_143f8aa40, FUN_143f8d560, FUN_143f8e820, FUN_143f90be0, FUN_143f914c0, FUN_143f965d0, FUN_143f96860,
  FUN_143f97f20, FUN_143f9be50, FUN_143f9c1f0, FUN_1430aca90, FUN_143752d40, FUN_143753540, FUN_142dceb50.

Constant passes over all 757 reached changed functions — every hit already recorded: commerce 0x45 subs +1
(FUN_143258f50), mail 0xF8 subs +1 (FUN_1433765a0), claims dispatcher +2 above 0x45 (FUN_142dbecb0 case 0x5e → 0x60),
InstancedGame dispatcher bound 0x15 → 0x19 and +1 shift (FUN_1434ee920), SetSupplementalData index < 0x19
(FUN_142dd1e50 / FUN_142dd1f90, L140 text in phaseE2-rebase.md), PU header writer u32 sub (FUN_142c13350), PU
dispatcher (FUN_142cdfa60), harvest result 9 (FUN_142e18c70), ShowActivationFailure 0x1E (FUN_142f202e0),
WorldEditing PlayEffect 8 (FUN_1430838d0), Game events 10 / 11 and end state 7 (FUN_1434e7900, FUN_1434e2330), team
settings (FUN_1434e7570, FUN_1434ec610), PropState / ObjectScripting (FUN_1434e8ea0, FUN_142f46ed0, FUN_142fee7e0),
voxel-data queue 25 / 0x65 (FUN_14310b4d0), customization slots (FUN_1430a33a0, FUN_1430a37a0), build-tool modes
(FUN_14346d4a0, agent-playmode.md), paste threads (FUN_143f9a150, FUN_143f9c330, FUN_143f9aaf0).

## 3. New leads

### L-D1. Virtual calls below the roots are not walked
- **Need:** the walk follows direct FUN_ references only; manager methods called through a vtable
  (`(**(code **)(*plVar + 0x48))(…)`) from a packet handler, and everything below them, are not reached.
- **Searched:** index.pkl callees (direct references only); re/out/rtti_695.tsv has every 0.695 vtable with its slot
  list, but the object class at a call site is not known statically.
- **Try next:** resolve calls on singleton managers: find the store of `Class::vftable` into the object a `DAT_`
  global points to (constructor + registration), map `DAT_x → class`, then `vtable slot = offset / 8` from
  rtti_695.tsv; re-run walk.py with those edges added and diff the new uncited nodes the same way.

### L-D2. Constants held in data, not code
- **Need:** a tuning value read from .rdata (float limits such as distances / speeds, e.g. the vehicle and placement
  functions above whose only change is the DAT address) can change between builds without any code change; the
  decompile comparison sees only the address.
- **Searched:** the "moved float constant" functions in §2 (vehicles, FUN_142fbd5f0's 0x3f800000 literal is code).
- **Try next:** read the 4-16 bytes at each paired DAT address from both executables (read-only, e.g. with
  re/tools or pefile on copies) for the functions reached in this walk and list the values that differ.

## Summary

The depth-1 limit of the part-2 rebase is closed for the direct call graph: from every 0.695 S→C dispatcher case,
C→S builder and sending Lua native (21,297 roots), and separately from the 3,332 functions the traces cite, every
changed function 2-4 calls down was paired with its 0.589 partner, filtered (layout-only, recompilation, engine / UI
noise) and the 170 not already cited or compared were read. Almost all are struct-offset shifts, the IString
recompilation pattern, moved constants or the already recorded wire changes (PU u32 sub, commerce / mail / claims /
site-game sub shifts, site-game and HUD trailing fields, PropData moved into VoxelDataResponse). One real change
touches the server: the 0.695 client only lets a prop be put down inside the site it was in when the placement opened
(FUN_142fbd5f0 / FUN_142fc1950), and the server opens new placements 20 m below the player, which can be outside the
site box near its floor — fix: spawn inside the site box; also refuse cross-site moves in Props.Save (F1, needs an
in-game check). Two OK-NOTEs (Design cache validity, connection guards). Virtual-call edges and data-held constants
are not covered (L-D1, L-D2).
