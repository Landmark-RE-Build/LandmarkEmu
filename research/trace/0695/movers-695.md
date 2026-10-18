# Movers and placement packs — 0.695 trace (TASKS E.1)

Phase E data-table audit, 2026-10-17. Proof marks: [P] read in the client / data, [I] inferred, [?] open.

## 1. What they are

Patch notes 27 Aug 2014 "Movers!" (P1529-P1538) and later fixes; wiki Category:Movers:
- **Translocation Pad** (item 1290 → prop 1196, a spawner): two pads; walking onto one teleports you to the other;
  "right click one of the Pads and select Reposition"; one-way via the Setup window (P, 2014-10 notes).
- **Spring-loaded Flinger** (item 1285 → prop 1200, spawner): flinger + a target "cube"; "when you activate the flinger,
  it'll propel you towards the target"; Setup changes the Height (angle); the target must stay on your claim.
- **Spring-loaded Launcher** (prop 1328, spawner → 1233): "launch the user in the air, at a speed and angle that you
  specify" (Setup).
- **Magical Platform** (item 1289 → prop 1207, spawner): platform + two end docks; the platform moves between the docks
  (any direction; it takes the docks' rotation); "should no longer slide off" fix.
- Also: Glass Platform (1423 → 1421 + 2 × dock 1422), Three dock moving platform (1288), Dark Elf Teleporter (1213 →
  2 × 1206), Test Portal pack (1178, internal).

## 2. Placement packs (server data) [P]

- PropDefs `PLACEMENT_PACK_ID` (col 10) names the pack of a spawner prop: 1178→1, 1196→2, 1200→3, 1213→4, 1207→5,
  1288→6, 1328→7, 1423→8.
- PlacementPackProps.txt rows {pack, prop, PROP_INDEX, OFFSET_X, OFFSET_Z, SCALE_OVERRIDE, HEADING_DEGREES}: e.g. pack 2
  = pad 1194 at (0, 0) and pad 1194 at (0, 5); pack 5 = platform 1193 (index 0) + dock 1192 (index 1) at (−5, 0) + dock
  1192 (index 2) at (5, 0) heading 180.
- PlacementPacks.txt {MIN / MID / MAX_SEPARATION_DISTANCE}: pads 0 / 78 / 298, others 0 / 80 / 46430.
- The client's ClientPlacementPackPropDefinition reads only ID, PROP_NPC_ID and PLACEMENT_PACK_ID (0.695 FUN_143536050);
  its manager (DAT_14543b4a8) maps a part's prop to its pack only for the known-prop lookups (FUN_143535a60 from
  FUN_142f03b90 / FUN_142f03de0). Offsets, scale, heading and PlacementPacks.txt have **no client reader** → the server
  places the parts and enforces the separation [P for "no reader", I for the server's role].
- The live prop dump (Resources/PacketDumps/145-PropBase) gives the spawners an empty model and a third u32 of 2
  ("[Spawner]"); parts have 0, docks 1 [P].

## 3. How a part becomes a mover in the client [P]

Lightweight prop spawn (PlayerUpdate 0x62, handler FUN_142cde1c0 → FUN_142d54040, 0.695):
- **+0x260 u32 static area id**: looked up in the static area manager (GlobalAreas.txt / AreaProperties.txt /
  GlobalAreaShapes.txt, identical in 0.589 and 0.695); when found a ClientAreaData is made for the prop and its area
  properties are created by type (factory FUN_1431ae4f0).
- **+0x268 u64 area guid** (entity +0x240; also the Area Tool's tie, area-definitions-695.md §4.2).
- **+0x270 u64 group key**: handed to each area property by type (FUN_142d54040 lines with `param_3 + 0x270`):
  Teleporter 0x17 / 0x26 (vfunc 200, plus the prop guid from +0x18), ChainThrustPad 0x18 and Thrower 0x20 (+0x80),
  SlidingFriction 0x1a (+0x68 with the prop guid at +0x70). The flinger finds its target among the props whose
  entity +0x340 equals that key (FUN_142f85170 / FUN_142f85230, hash on +0x340) [P]; that entity +0x340 comes from the
  same spawn field [I].
- **+0x340 list** (reader FUN_142ba1d20 → FUN_142b9df30 → FUN_142b945d0): the prop's editable property values, up to
  6 typed lists of {u32 key, {u32, u32, u64, u32 list, string list}}; FUN_1430ab830 stores them in the actor (+0x4c00).
  Missing keys fall back to defaults (the teleporter's enable check defaults to 1, FUN_1436db8b0) [P]. The key for
  enable_teleportation is 0xa632764b — not Jenkins lookup2 of the name [?] (L134).

Area property types the client implements (registration list FUN_1431ae4f0): 6 Minimap, 7 Physics, 8 Timer, 10
Interaction, 11 Death, 12 AntiGravThrust, 17 TerrainPermission, 20 Claim, 22 ClaimRestrictionBuffer, **23 Teleporter**,
**24 ChainThrustPad**, **26 SlidingFriction**, **29 MovingObjectParamEditProxy**, 30 InstancedGame, **31
FlingerParamEditProxy**, **32 Thrower**, **38 TeleporterClaimVisitation**. Types 15, 18, 21, 27, 28, 33, 34, 36, 37 have
no client class → server-side properties [I].

Mover areas (AreaProperties.txt):
| area | properties | shape (GlobalAreaShapes) | part [I] |
|---|---|---|---|
| 6 | 24 ChainThrustPad (edit: launch speed, trajectory height, pitch) | box ±1.8, y 0.75-2.25 | Flinger 1188 |
| 7 | 23 Teleporter (ints 10625 / 10624 / 10476, float 0.5; edit enable_teleportation) | sphere r 1.5 at y 1.5 | Translocation Pad 1194 |
| 8 | 24 ChainThrustPad (floats 20, 60; no edits) | box as 6 | older launcher |
| 14 | 26 SlidingFriction (1, 1, 1) + 28 (server; edit platform speed / pause) | box ±1.8, y 0.25-0.75 | Magical Platform 1193 |
| 15 | 27 (server; −1) + 29 MovingObjectParamEditProxy (edit speed / pause) | box 1 × 0.5 × 3.6 | dock 1192 |
| 19 | 31 FlingerParamEditProxy (edit trajectory height) | sphere r 1 | Flinger Target 1191 |
| 20 | 32 Thrower (edit launch speed, pitch) | box as 6 | Launcher 1233 |
| 26 | 38 TeleporterClaimVisitation (int 10624; edit enable_teleportation) | sphere as 7 | (pad variant) |

## 4. Behaviour in the client [P]

- **Teleporter** (ClientAreaTeleporter / …ClaimVisitation share FUN_1436db8b0 enter, FUN_1436dbac0 exit,
  FUN_1436dbb40 update): on enter, if the prop's enable_teleportation (default 1) is on and the player isn't busy,
  a "*Bamf!*" PhysicsTeleportCallback (FUN_1436db150, state machine FUN_1436db650) runs: TeleportPlayEffectRequest
  **0xFC sub 9** {u64, u32 effect, u32 duration} (FUN_1431736e0), waits, then **TeleportManagedRequest 0xFC sub 10**
  {u64 guid (from the area), u8 0, u32 1, u32 0, u32 −1, u64, u64} (FUN_143173480). The server moves the player.
  Neither sub is handled by the server today (Travel.cs default branch).
- **Thrower** (type 32, FUN_1436dc4a0 → FUN_1436dbf00): finds the prop of the area (entity area guid == area guid),
  reads its pitch and speed (vfuncs 0xd8 / 0xd0) and heading, and sets the local player's velocity — all client-side.
- **ChainThrustPad** (type 24, FUN_1436da050 → FUN_1436d9c90): among the props of the group key, the one with this
  area's guid is the source and any other is the target; with a target FUN_1436d9720 computes the arc to it (flinger),
  else FUN_1436d9ab0 launches by the settings — client-side physics.
- **Rails** (PlayerUpdate 0x2A-0x2C MoveOnRail …, ProxiedCharacterRailMovementController FUN_1434f3330) use a global
  predefined rail table — not the platforms [P for the table; I that platforms don't use it].

## 5. What the server must do

1. Placing a spawner prop (PLACEMENT_PACK_ID ≠ 0): place its PlacementPackProps parts at the offsets (rotated by the
   placement heading), not the spawner; all parts share a pack group id (persisted). Picking up / undo / destroy of any
   part takes the whole pack (one item back).
2. Every part's spawn: +0x260 area id (table above), +0x268 a unique area guid, +0x270 the group id.
3. Moving a part (Reposition = the normal move): keep the parts within MIN..MAX separation of each other and on the
   owner's claim (patch notes: the flinger's end point can't leave the claim; pads can't reach a far claim).
4. 0xFC / 10 ManagedRequest from a pad: teleport the player to the pack's other pad (above it). 0xFC / 9: show the
   effect to players nearby [I].
5. Magical Platform: server-side movement of the platform between the docks at moving_platform_speed with
   moving_platform_pause_delay (types 27 / 28 have no client class) [I]; how live moved the prop on the wire is open
   (L135).
6. Settings in the spawn's +0x340 list for non-default values (needs L134's key).
