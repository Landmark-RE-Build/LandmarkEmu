# Outposts / Lost Crystals / Underground Adventure / Catapult vs the server (TASKS 3.13, 3.14, 3.28)

Audit of the post-0.589 patch notes (7 Jun - 11 Aug 2016, plus later outpost drops) about outposts, Chaos Caverns, Lost
Crystals, Underground Adventure, elites, loot lockouts and the Highfall catapult, plus the wiki and the client's own help
pages, against the server code (LandmarkEmu, 2026-10-15). Extends trace/0695/launch-rules-audit.md (21 Mar - 4 Jun 2016):
launch rows settled there (6.1 channel / combat block, 6.22 elite CC immunity, 6.23 outpost respawn 8-10 / 15 min, 6.24
elite loot, 6.25 launch lockout, 6.26 group trips, 6.27 Pioneer refusal) are not repeated, only referenced.
Read-only research; no code changed.

Classes: **FOLLOWED** (code matches, cited) · **PARTLY** · **NOT FOLLOWED** (code differs) · **NOT BUILT** (no code) ·
**SUPERSEDED** · **CLIENT-ONLY** · **UNKNOWN** (data missing). Fix ids F1-F10 refer to the fix list below.

## New client facts found for this audit

1. **The 0.695 outposts are the `ruin_*.otd` templates, not `cavern_ruins_*.otd`.** The 35 `ruin_*.otd` in the 0.695
   install each hold one (two in felgon_tomb, hanna_pausaniaseye) **Lost Crystal prop 2654**, 1-6 **"Spawner: Ruins: <faction>
   BOSS (1 Limit)"** props, the faction spawners (Patroller / Melee / Ranged / Caster / Hound / Human, "100 Limit"), cave
   monster spawners (Fungus 1909, Chomper 1904, Slaug 1893, Abomination 1899, Darkfang Specter 2874, Oakmire Stag 2876)
   and **1-55 "Spawner: Ruins: Chests (1 Limit)" (2731)** spots each (`LANDMARK_DIR='<0.695 test copy>' python
   re/tools/otd.py "ruin_*"`). In 0.589 the same files had 0-12 chest spots and 11 had none (`python re/tools/otd.py
   "ruin_*"`) — that is the 30 Jun 2016 "exactly one Treasure Chest ... new and dastardly clever spawn locations" change
   (P5925 / P5926). The `cavern_ruins_*.otd` the server stamps (Outposts.cs:43-56, version 2, the 2014-15 team-up ruins)
   hold **no** Lost Crystal, no spawners and no chest spots. This answers open lead **L12** (where the Lost Chest hides):
   one of the ruin's 2731 spots ("1 Limit" = one chest alive at a time).
2. **Client help pages** (same bytes in 0.589 and 0.695): HelpPages/ChaosCavern_1Enter_FTE.html, _2Outpost_FTE.html,
   _3Crystal_FTE.html, for FirstTimeEvents.txt 36 ChaosCavern, 37 Outpost, 38 LostCrystal. Rules they state: "Every Chaos
   Cavern has at least one Outpost"; "Every Outpost has at least one Elite enemy"; "Killing an Elite activates the Lost
   Crystal temporarily" / "for a limited time"; "An inactive Lost Crystal looks dormant. An active Lost Crystal is glowing
   and projects a tall beam of light"; "Lost Crystals can be embedded inside of large gems or power sources. You may need
   to dig it out"; the chest "can be hidden in obscure nooks, or camouflaged by the decor"; "Outposts exist in the open
   world and change regularly"; "Enemies will return to the Outpost soon"; "You can only bond with a Lost Crystal once in
   each Chaos Cavern"; after bonding "you can use it to teleport to another random Chaos Cavern".
3. **Caves exist on every non-Pioneer island.** ContinentCavePlacements[c]_Underground.cpa exists for islands 391,
   393-408, 410-416 (cavern piece 104 counts 499-860 each; 406 = 520) and for none of the Pioneer lands 417-421 or the
   event islands 436-438. The server loads only continent 406 (HostedService.cs:88 → Caves.Load).
4. **The Lost-Crystal offer packet is unchanged**: 0.695 FUN_143171ee0 = 0.589 FUN_1430c6da0
   (PacketTeleportNextUndergroundOffer {u16?, u8, u64}, dialog Teleport.LeylineTeleportDialogTitle /
   TeleportToNextUndergroundCave). The destination (world / landscape / biome) is purely the server's choice on the reply.
5. **Catapult data**: prop 2941 "Highfall Catapult" (identical PropDefs row in both builds); models 1134 / 1145
   Char_WarMachine_Catapult_Fantasy01, projectile model 1137 FX_Projectiles_Rocks_CatapultRock_Firey; composite effects
   12825 PFX_Combat_Fire_Catapult_Release_01, 12826 PFX_Combat_Fire_Catapult_Explosion_Impact_01, 12827 / 12828 impact
   ambients (ActorCompositeEffectDefinitions.xml, both builds); animations Idle_01, Activate_01 (fire), Ouch_L01 / R01,
   Death_01 — **no walk or turn animation**. No ability rows anywhere in the tables (NPC abilities were server data).
   Wiki "Highfall Catapult": Elite, Fire Essence affinity, "Launches burning rocks in a straight line at a target around
   60 blocks away", "Rocks cause an AoE upon landing, dealing damage and knocking players down".
6. **Crystal state effect candidate**: InteractableCompositeEffectSets 125 "Ruin Artifact", rows 270 (index 0, OFF, no
   effect) / 271 (index 1, ON, composite 11673 — the same composite Outposts.cs plays on the channelling player as
   "PFX_Teleport_LeylineCrystal_Cave"). A two-state prop effect is what the help page describes (dormant / glowing); which
   prop uses set 125 is not proven (see Unknowns).

## Per-item table

| Note | Date | Rule | Status | Evidence | Fix |
|---|---|---|---|---|---|
| P5727 | 2016-06-07 | NPCs in Outposts no longer shoot you through walls, ceilings | NOT FOLLOWED | No line-of-sight test anywhere: Abominations spit at up to SpitRange 16 m (Creatures.cs:101, 422-452) and every creature aggroes at 12 m 3D through rock (Creatures.cs:366-367, Dist :192). Garrison Ranged / Caster spawners get the default Behaviour.Melee (SpawnerKind, Creatures.cs:1426-1427), so they don't shoot at all | F4 |
| P5726 | 2016-06-07 | Weapon AoE no longer passes through blocks and pulls creatures behind walls | NOT FOLLOWED | WeaponCombat.cs:581-582, 625, 790 use Creatures.InRadius with no wall test | F4 |
| P5728 | 2016-06-07 | Highfall catapult: one facing only, attacks only distant enemies, can't hit close ones | NOT BUILT | Prop 2941 placed as a creature prop → StoryTools.Spawn (StoryTools.cs:731) → Creatures.SpawnStory → SpawnerKind → a 550 hp / 50 dmg **melee walker** named "Highfall Catapult" (Creatures.cs:1407-1427, 1430-1462); spawn ignores the prop's rotation (no Yaw set); no minimum range | F6 |
| P5732 | 2016-06-07 | First-time pop-ups explain Chaos Caverns and Lost Crystals (also in Help F12) | NOT BUILT | FirstTimeEvents.cs:43-47 has no 36 / 37 / 38 and no pages for them; nothing triggers them. Pages exist in both clients (fact 2). F12 help list is client-side | F7 |
| P5733 | 2016-06-07 | Reduced severity of some creature AoE abilities in Outposts | CLIENT-ONLY / UNKNOWN | Server creatures have no AoE abilities except the Fiery Fungus blast (BlastRadius 5 m, Creatures.cs:101); live values unknown | — |
| P5734 | 2016-06-07 | Lost Crystals teleport you to other worlds and biomes each time | NOT FOLLOWED | Outposts.NextCave (Outposts.cs:510-522) picks another outpost on the home continent only; Caves are loaded for one continent (HostedService.cs:88). Biomes vary only among 406's cave biomes. "Other worlds": the server has one world (Landscapes.cs WorldId 2) | F2 |
| P5826 | 2016-06-15 | Elite names positioned above their heads | CLIENT-ONLY | name placement is client rendering (boss display already sent, Creatures.cs Boss) | — |
| P5828 | 2016-06-15 | Killing the Elite reliably activates the Lost Crystal | FOLLOWED | Creatures.Kill → Outposts.EliteDied when `c.Boss && c.Cavern >= 0` (Creatures.cs:820-821); every death path goes through Kill (:705, :796, :1336, :1497) | — |
| P5829 | 2016-06-15 | Lost Crystal loot lockout "significantly increased"; only affects looting a crystal in the same Chaos Cavern again | NOT BUILT | Outposts.CompleteChannels gives loot on every channel (Outposts.cs:466-497); no timer (launch gap 6.25). Client page: "once in each Chaos Cavern". Length unknown (L10) | F3 |
| P5830 | 2016-06-15 | Outposts don't spawn on top of each other; chests don't float | FOLLOWED (caveat) | One ruin per cavern, caverns ≥ 400 m apart (Outposts.Choose, :193-220); chest put on the cave floor −0.35 m (:239-240). Caveat: floor search uses the client cave pieces only, not the stamped ruin voxels | F1 |
| P5831 | 2016-06-15 | Teleporting from a Lost Crystal above ground sets you down at ground level on arrival | FOLLOWED | NextCave arrives at Caves.Floor(cavern) + 1 m (Outposts.cs:520; Floor = 1 m above rock, Caves.cs:117-126), independent of where the crystal stood | — |
| P5847 / P5848 | 2016-06-15 | Known issue: repeated Underground Adventure lands you in a recently looted cavern | SUPERSEDED (P5927) | Still relevant once F3 exists: TravelRandom / NextCave should prefer caverns the player is not locked out of | F3 |
| P5923 | 2016-06-30 | Update pass through all Outpost populations | NOT FOLLOWED | Every outpost gets the same synthetic 7-creature garrison of one random faction (Outposts.GarrisonFor :269-277, Garrisons :59-67); the 0.695 ruin templates carry the real populations (fact 1) | F1 |
| P5924 | 2016-06-30 | Outposts no longer spawn NPCs inside walls and other terrain | NOT FOLLOWED | Garrison positions are random 4-26 m around the cavern floor (Creatures.cs:241-273) checked with Caves.FloorNear, which tests only CavePieces.IsAir (EnvServer.Core/Terrain/CavePieces.cs:78-95) — the stamped ruin's walls are invisible to it | F1, F4 |
| P5925 | 2016-06-30 | Every Outpost has exactly one Treasure Chest; especially well guarded on small Outposts | PARTLY | One Lost Treasure Chest (2930) per outpost: FOLLOWED (Outposts.cs:240). Guarding: NOT FOLLOWED — fixed offset (9, 2, 7) from the cavern middle, unrelated to the garrison | F1 |
| P5926 | 2016-06-30 | New and clever Treasure Chest spawn locations | NOT FOLLOWED | One fixed spot per outpost (Outposts.cs:239), the same after ChestRespawn (:308-313, :411); the 0.695 templates hold 1-55 chest spots each (fact 1) | F1 |
| P5927 | 2016-06-30 | "Underground Adventure" sends you to Chaos Caverns in any zone, like the Lost Crystal | NOT FOLLOWED | Caves.TravelRandom (Caves.cs:147-166) picks a cavern of continent 406 only | F2 |
| P5928 | 2016-06-30 | Underground Adventure works on Pioneer Landscapes, sending you to a cavern on a non-Pioneer Landscape | FOLLOWED | Travel.Handle 0x0B (Travel.cs:41-43) → TravelRandom from any zone, re-zoning to 406 (non-Pioneer) (Caves.cs:160-163). Supersedes launch gap 6.27 for the 0.695 server; the data agrees (no Pioneer .cpa) | (F2 keeps it) |
| P5929 | 2016-06-30 | NPCs have no collision after they die | FOLLOWED | Creatures.Kill removes the creature from every viewer at once (Creatures.cs:813-814); no corpse entity remains | — |
| P6092 | 2016-07-14 | Underground Adventure from Pioneer works and takes you to a different non-Pioneer landscape | FOLLOWED | as P5928 | (F2 keeps it) |
| P6143 | 2016-07-28 | Outpost elites have a rare chance to drop the Lop Ear Bunny pet | NOT BUILT | covered by TASKS 3.10 / spec-pets.md:219 (hook in Creatures.Kill for `c.Boss && c.Cavern >= 0`) | F10 |
| P6387 | 2016-08-11 | Killing an elite no longer unlocks the Lost Crystal permanently | PARTLY | Re-locked when the Elite respawns (Creatures.Respawn → Outposts.EliteBack, Creatures.cs:847-848). But (a) dead creatures only respawn while someone streams their cavern (Update's active list, Creatures.cs:316-321; Think :333-343), so an unvisited outpost stays unlocked; (b) the crystal's "light" is a one-shot HitEffect 10621 (Outposts.cs:285, :343-344), never turned off; client: "activates ... for a limited time", inactive = "looks dormant" | F5 |
| P6505 | 2016-09-29 | Hat unlockers drop from outposts | NOT BUILT | covered by TASKS 4.10 (items 4521-4523) | F10 |
| P6565 / P6567 / P6569 | 2016-12-06/17 | Halloween items stop dropping; holiday sweaters and Colonist's Flight Suits drop from outposts | NOT BUILT | items-695.md:136-137; seasonal | F10 |
| wiki Lost Crystals | 2016 | Not in combat; channel several seconds; a hit cancels | FOLLOWED | Outposts.UseCrystal (:436-454), Interrupt from Health.cs:136-137 (launch 6.1) | — |
| FTE 2 / wiki Outposts | 2016 | Every Chaos Cavern has at least one Outpost | NOT FOLLOWED | 23 outposts on 406 for 520 cavern pieces (Choose picks the 23 roomiest ≥ 400 m apart). What live counted as one "Chaos Cavern" (cave network) is in the .cpa sections the server doesn't read (L47) | F1, F8 |
| FTE 2 | 2016 | Outposts exist in the open world and change regularly | NOT FOLLOWED / UNKNOWN | Placement is fixed for ever (TerrainEdits/outposts.txt, Outposts.cs:113-188); live rotated ruin layers on the Environment Server, period unknown (L76) | F8 |
| FTE 2 / wiki Elite Monsters | 2016 | Every Outpost has at least one Elite; killing an Elite activates the crystal | FOLLOWED (count) | One BOSS per garrison (GarrisonFor slot 4), doubled health (Creatures.cs:256-264). Templates give 1+ BOSS types per ruin (e.g. butchywuff: Ebonguard + Barren) | F1 |
| FTE 3 | 2016 | Crystals sit in places of power, sometimes embedded in gems: dig it out | NOT FOLLOWED | Crystal at a fixed offset (−6, 2, −4) on the cave floor (Outposts.cs:241-242); templates place 2654 inside the build | F1 |
| FTE 3 | 2016 | Bond with a Lost Crystal once per Chaos Cavern, then teleport to another random cavern | PARTLY | Offer after every channel: FOLLOWED (Outposts.cs:493, 500-507); "once": NOT BUILT (P5829) | F3 |
| wiki Highfall Catapult | 2016 | Elite; burning rocks in a straight line at ~60 blocks; AoE + knockdown on landing | NOT BUILT | as P5728 | F6 |
| (server hygiene) | — | NextUndergroundReply only answers an offer | NOT FOLLOWED | Travel.cs:44-47 calls Outposts.NextCave for any 0xFC/0x0D, without checking the echoed u64 or that an offer was made: any client can warp to a random outpost at will | F9 |

Counts: 32 rows — FOLLOWED 8 (one with a caveat), PARTLY 3, NOT FOLLOWED 11, NOT BUILT 7, SUPERSEDED 1, CLIENT-ONLY 2.

## Fix list (prioritised)

**F1 (high) — Build outposts from the 0.695 `ruin_*.otd` templates and their spawner props.**
Fixes P5923, P5924, P5925, P5926, FTE crystal / elite rows; closes L12.
- `Game/Outposts.cs` `Ruins` (:43-56): replace the 23 `cavern_ruins_*` with the 35 `ruin_*` names; read them from the 0.695
  install (WorldServer.json GameDirectory → 0.695, as terrain-695.md §7.5 already recommends; 0.589's copies lack most chest
  spots).
- `BuildProps` (:231-243): split the template props. Send only decor props. Never send spawner props (definition name
  starts "Spawner:": 2702-2731, 1893, 1899, 1904, 1909, 2874, 2876). Prop 2654 → `o.Crystals` (a list: two in felgon_tomb
  and hanna_pausaniaseye) at the template position. Prop 2731 → `o.ChestSpots`; place the Lost Chest (2930) at a random
  spot, and pick a new spot each time it comes back (`Update` :308-313).
- `GarrisonFor` (:269-277): yield each creature spawner at its template position and its own prop id. Drop the synthetic
  `Garrisons` table. BOSS spawners are "(1 Limit)": spawn one per BOSS prop definition, at a random one of its spots, as
  `Boss`. "(100 Limit)": one creature per spawner prop.
- `Creatures.CavernGroup` (:241-273): use the spawner position as the creature's home. Search the floor only within about
  1 m, and test the stamped ruin cells as well as the cave pieces (see F4's solid test). The Fungus, Chomper, Slaug and
  Abomination spawners map to the existing cave kinds (`CavernKinds` / `FieryFungus` / `Abomination`).
- `Outposts.Choose` (:193-220): require open space for the template's footprint (175-550 voxels square). Keep one ruin per
  cavern.
- Migration: the dev world's `TerrainEdits/outposts.txt` and the stamped `cavern_ruins` cells are permanent
  (`MarkPermanent`). Use a new marker name, and reset or un-stamp the old cells (back up server-data first). The dev world
  restarts for this.

**F2 (high) — Underground Adventure and Lost-Crystal trips to every landscape.** Fixes P5734 and P5927, and keeps
P5928 / P6092. **Built 2026-10-16 (agent, untested in game):** all of the list below, plus treasure chests and line of
sight per landscape; outposts stamped lazily per landscape in the background (docs/gameplay.md "Chaos Caverns on every
landscape", TASKS 3.14, TESTING-NEEDED); trip rule ours (L129), portal crystals off-home L130, "other worlds" L131.
- `Game/Caves.cs` `Load` (:45-78) and `HostedService.cs:88`: load `ContinentCavePlacements[c]_Underground.cpa` for every
  `Landscapes.Zones` entry that has one: islands 391, 393-408, 410-416; none for the Pioneer lands 417-421 or the event
  islands. Store `(Zone, At, Biome)`.
- `Floor` / `FloorNear` (:100-126): take the zone and pass it to `CavePieces.IsAir(..., zone)` (already zone-aware).
- `TravelRandom` (:147-166): pick a random non-Pioneer zone that has caves, then a cavern in it (keep `MinTravelDepth`).
  Use `Teleport.ToZone` when the zone differs. From Pioneer and event islands this still lands on a non-Pioneer landscape.
- `Outposts.NextCave` (:510-522): pick from outposts in all zones, skipping the player's locked-out caverns (F3).
- Make zone-aware everything that assumes `OnHomeContinent` / `TerrainStore.HomeContinent`:
  - `Outposts.Load` / `Stream` (:167, :328) and `Creatures` cavern streaming (:897-909);
  - `Creatures.Stream` (:863), which returns early when `!TerrainHeights.Has(zone)` and must not skip cavern groups;
  - `Creatures` `Zone = Home` (:221, :252).
- Place a landscape's outposts on its first visit, to keep memory flat. "Other worlds" needs several world servers:
  out of scope while the server has one world.

**F3 (high) — Lost Crystal loot lockout** (P5829 + launch 6.25 / P5180 / P5181; client page "once in each Chaos Cavern").
- Per character, keyed by (zone, cavern index). Persist it with the character (e.g. `Unlocks` or a character table) so
  it survives relog and restart.
- `Outposts.CompleteChannels` (:466-497): when the key is locked, give no loot, no fanfare and no DailyGoals credit;
  send only `SendNextCaveOffer` ("just show the teleport prompt"). Otherwise give the loot and lock until
  `now + LockoutTime`.
- `LockoutTime` is unknown (L10, "significantly increased" in June). Use a named constant with a comment. A 24 h
  placeholder is only a player's guess from the 4 May thread.
- `Caves.TravelRandom` / `Outposts.NextCave`: prefer caverns that are not locked (P5847).

**F4 (medium) — Line of sight for creatures and area attacks** (P5727, P5726, P5924).
- Add `Caves.Clear(zone, from, to)`: march the segment in voxel steps and fail on rock (`!CavePieces.IsAir`) or on a
  solid stamped / edited cell (EnvironmentServer overlay).
- `Creatures.cs`:
  - Idle aggro (:366-367) and chase: a creature without sight doesn't aggro and its ranged attack doesn't fire;
  - Abomination spit (:422-452): require sight;
  - `SpawnerKind` (:1417-1427): give "Ranged" / "Caster" spawners `Behaviour.Ranged`, so garrison archers and casters
    shoot as live did (with sight).
- `Combat/WeaponCombat.cs` (:581-582, :625, :790): filter `InRadius` hits by `Clear(aimPoint or player, creature)`.

**F5 (medium) — The crystal is active only for a limited time** (P6387; client pages).
- Give `Outposts.Outpost` an `ActiveUntil`.
- `EliteDied` (:280-288): set `ActiveUntil` to the Elite's respawn time (15 min per launch 6.23).
- `Outposts.Update`: expire the window and tick the dead outpost Elites even when nobody streams the cavern (or let
  `Creatures` respawn outpost bosses by time alone).
- `EliteBack` (:291-295): switch the crystal back to dormant for every viewer.
- Show the state as a two-state prop effect: `PropEffects` UpdatePropInteractionState with set 125 "Ruin Artifact"
  (ON row 271 / OFF row 270), instead of the one-shot `HitEffects.PlayOn(..., 10621)` (:285, :343-344). Verify in the
  0.695 test client that set 125 is the Lost Crystal's (see Unknowns). Otherwise at least stop re-sending 10621 after
  `EliteBack`.

**F6 (medium) — Highfall Catapult (TASKS 3.13).**
- `Creatures.cs`: add `Behaviour.Siege` for prop 2941 (or a name containing "Catapult") in `SpawnStory` / `SpawnerKind`.
- Never move, never turn: its yaw is the placed prop's rotation. Pass the rotation from `StoryTools.Spawn` (:731) into
  `SpawnStory`, which today sets no `Yaw`.
- Target only a player inside a forward cone, between `MinRange` and `MaxRange` (~60 blocks per the wiki), with line of
  sight (F4).
- Fire with animation Activate_01 and composite 12825 on the catapult. After the flight time, play 12826 at the target
  point it aimed at (a straight shot, not homing). Damage and knock down every player within an AoE radius there.
- Elite status (wiki) so 6.22's crowd-control immunity applies.
- Ability id, damage, AoE radius, minimum range and flight speed are unknown: name them as constants.

**F7 (low) — Chaos Cavern / Outpost / Lost Crystal first-time help** (P5732).
- `Game/FirstTimeEvents.cs` (:43-47 and `pages`): add `ChaosCavern = 36`, `Outpost = 37`, `LostCrystal = 38`, with
  pages `ChaosCavern_1Enter_FTE`, `ChaosCavern_2Outpost_FTE` and `ChaosCavern_3Crystal_FTE`. Use Small / CenterR, as
  for the other events, since live's choice is unknown.
- Trigger 36 when a player arrives in a cavern: after `Caves.TravelRandom` or `NextCave`, once the re-zone ends (memory:
  hold packets until the re-zone finishes).
- Trigger 37 in `Outposts.Stream` where the name is shown (:353-354).
- Trigger 38 on the first crystal click, or when `EliteDied` happens in view.

**F8 (low) — Outposts change regularly; one per cave network** (FTE 2).
- Re-pick caverns and ruins on a schedule, at a restart: un-stamp the old ones and stamp new ones.
- Place at least one outpost per cave network. That needs the .cpa CavernInstanceData / LayerInfo (L47) and the ruin-layer
  rules (L76). The period is unknown.

**F9 (low, hygiene) — Accept NextUndergroundReply only for an offer.**
- `Outposts.SendNextCaveOffer` (:500-507): remember a per-session token, unique per offer instead of the constant 0x4C43.
- `Travel.cs:44-47`: call `NextCave` only when the body's u64 matches a pending offer. Clear it on use or on logout
  (`Outposts.Forget`).

**F10 (low) — Outpost drops added after June.** These are tracked elsewhere: Lop Ear Bunny (TASKS 3.10, spec-pets.md:219),
hat unlockers (4.10), Dec 2016 sweaters and flight suits (items-695.md:136-137). Hook them all into `Creatures.Kill` for
outpost bosses, the Lost Chest (`Outposts.OpenChest`) and the crystal loot (`CompleteChannels`).

## Unknowns (data missing)

- **Lockout length** after the 15 Jun 2016 increase (L10). The key is also open: per crystal (dev, 4 May) or per Chaos
  Cavern (client page, 15 Jun note). Two ruins have two crystals, so the choice matters there. F3 uses the cavern, following
  the later sources.
- **Crystal active window**: "temporarily" / "for a limited time", no number. F5 ties it to the Elite's respawn.
- **Which prop uses InteractableCompositeEffectSet 125 "Ruin Artifact"**. Check: does the 0.695 client show 11673 on a
  2654 prop when the server sends set 125 / row 1?
- **How live placed ruins**: which ruin goes in which cavern, how many per landscape, and how often they changed
  (Environment Server ruin layers, L47 / L76).
- **Chest spot rule**: a random 2731 spot each respawn (assumed), and the chest respawn time (20 min is ours,
  Outposts.cs:76).
- **"(1 Limit)" meaning**: one alive per prop definition per ruin is assumed. It decides how many Elites a ruin with
  several BOSS spots has.
- **Catapult**: ability id, damage, AoE radius, minimum range, projectile speed, knockdown status id. There is no table
  data; the wiki gives ~60 blocks only.
- **Outpost creature AoE severity** (P5733): there are no live values.
- **Cave networks**: what counts as one "Chaos Cavern" for "every Chaos Cavern has at least one Outpost" and for the
  lockout (L47).
