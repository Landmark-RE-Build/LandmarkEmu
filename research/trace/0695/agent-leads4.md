# Open leads, fourth pass — "what can become server code now" (2026-10-17)

Read-only pass over every lead still open in OPEN-LEADS.md (45: L2 L8 L10 L16 L19 L22 L23 L24 L25 L41 L42 L43 L46 L47
L48 L62 L79 L81 L82 L85 L86 L87 L90 L92 L93 L97 L99 L100 L109 L110 L111 L112 L113 L119 L120 L127 L128 L129 L130 L131
L132 L133 L163 L164 L165). Each entry, its sweep / pass-2 / pass-3 notes, and the **current server code** were read
first; several entries are behind the code (see §4). Nothing was edited except this file; no game, Frida, Docker, git,
web or outreach.

New search spaces this pass (none used for these leads before):
- the **weapon input chain**: item record u59 (= DynamicInputActionSystems id, cosmetics-695.md §3) → state machine →
  DesignBasedStateProperties → InputActionGroupEntries, for every weapon in gameData/items.json (scratch script, kept in
  the session temp dir only);
- the **`[*AbilityParamN*]` tokens** of 0.695 attack descriptions against AbilityParameterDefinitions rows;
- the **settings-window build order** in 0.695 FUN_142fb71f0 (which list is drawn first);
- a **full reward scan** of the Feb 2017 capture (re/tools/decode_achievements.py with every id 1-3000: 898 goals);
- the 0.589 UI folder (ScriptsBase.bin, UI/Admin, UI/TestMod) and the 0.695 UI folder for `RequestPlayerPositions`;
- the 0.695 file index for firework .fxd names; IgGeneralSetting for 573081.

Marks: **[P]** proven (address / file:line / table row / archive post), **[I]** inferred (reason given).

---

## 1. Verdict table

| Lead | Verdict | One line |
|---|---|---|
| **L133** | **BUILD** (behaviour [P forum], mechanism ours) | Story creatures keep a straight step on build sites (Creatures.NavMode returns null for `c.Story`), so they walk through player walls; live's pathed around walls / props (P5781, 2016 reports). Spec B1: a build-site solidity grid from the Environment Server + prop footprints |
| **L19** | **BUILD** (Combat Node ability rows) | The read-only TextLine row (max 0 / min 0) is proven and already used for row 106; FUN_142fb71f0 draws the drop-down list before the plain rows [P], i.e. "Combat Tactics … Below this … a list of the creature's abilities" (P6157). Spec B2 |
| **L110** | **BUILD (setting, default off)** | Live expired left-over sites when an event island closed (forum 55925 #1, 58077) [P]; ours keeps them. Spec B3, destructive → user's switch |
| **L163** | **BUILD (optional, lowest)** + FOUND [P] | Desperado's input group 78 has **three** casts: LMB 793, RMB 794, **E 795** [P]. The four "never craftable" weapons (3028, 3029, 3038, 3039) are exactly the launch weapons with an E cast [P] → a 3-attack generation that never went live [I strong]. Its attack damage formulas are client rows 703-711 [P]. Spec B4 |
| L46 | CLOSE-BY-DESIGN [P] | No goal of the 898 in the Feb 2017 capture grants 3925 Strip Miner, 3926 Deforester or 4363 Curator (achievement and objective bundles scanned); the "…ium?" / "Teak" capstones 642 / 644 give 80 Lumens only. The server awarding none is right |
| L164 | CLOSE-BY-DESIGN [P neg] | No `RequestPlayerPositions` string in 0.589 UI/ScriptsBase.bin, UI/Admin (DebugAIWindow, DebugStateMachineWindow, TemplateView), UI/TestMod, nor in the 0.695 UI folder. No shipped caller in either build, so the flags / bools never reach a live player |
| L43 | NEEDS-RUNTIME | Server refusal built (Crafting.cs:143 UnlockRefusal). Only the client's reader of recipe def +0x40 is open: T1 in §3 |
| L100 | CLOSE-BY-DESIGN (built) + runtime verify | Spire.cs now stamps the floor on the median ground + `Raise` 0.8 m on the 0.4 m grid (Spire.cs:502-519). The raise is [I]; T2 confirms the client view |
| L129 | CLOSE (built) | Caves.TravelRandom rolls the type-3 beginning locations (Caves.cs:28-34, :414-430), the client's own server-half roll. Entry is stale |
| L130 | CLOSE (built) | Per-landscape crystals (WarpNetwork.Prepare / NodesFor), client confirm cost (CostOf :363-374). Discovery: the client keeps no state [P pass 3]; ours "all open" stays a choice |
| L99 | CLOSE-BY-DESIGN (built) | Type-2 anchors in Pioneer.FindCavern (88fa629). MaxCavernDepth 180 m stays ours [I]; no client value exists |
| L2 / L113 | CLOSE-BY-DESIGN (built) | Gathering.Respawn.cs `ProceduralRespawn = minmax` (default) is Domino's model (forum 55352). The entity-list rows are server-only and absent from the live manifest [P pass 3] |
| L22 | CLOSE (built) | 577089 on reset and 577105 on Edit → Play are both sent; the carrier is the server's choice |
| L23 | CLOSE-BY-DESIGN | Firing rules are server logic only (pass 3); ours are documented in StoryTools (TASKS 2.34) |
| L97 | CLOSE-BY-DESIGN | 573081 "PvP Allowed" is only IgGeneralSetting 62 (setting 11, rule set 8, Adventure) [P]; our PvPAllowed for 2 / 5 / 7 matches IsGameFFA defaults |
| L10 | CLOSE-BY-DESIGN | 24 h + 574500 built; the only number on disk is a player's |
| L16 | CLOSE-BY-DESIGN | 65532 found; event islands are refused to 0.589 clients (event-islands-695.md:133, Landscapes.cs needs the 0.695 .csc), so "client without the files" never happens; the .ocm grids are client rendering data the server never needs |
| L24 | CLOSE-BY-DESIGN | Our ids are built; definitions exist only in live captures, none on disk (re-open if a capture turns up) |
| L25 | CLOSE-BY-DESIGN | The packs hold 26 Firework_*.fxd (bursts, streamers, Rockets_Launcher_50-80m, Rockets_StraightUp_Gold); the 22 emitter composites 12836-12857 are the only composites that use them [I: name match], which is what Fireworks.Launch plays |
| L42 / L92 / L93 / L112 | CLOSE-BY-DESIGN | Built from the wiki; record ids exist only in captures. Re-open path: a capture of a character past 685 |
| L47 | CLOSE-BY-DESIGN (islands) | On the 0.695 islands every manual record is (rot, 4, 76) = the cavern graveyard, built (0fb9cc4). Ids 61 / 75 occur only in the v12 alpha continents 325-384, which the server does not serve |
| L8 / L48 | CLOSE-BY-DESIGN | Server-to-server; the client never parses them (pass 3 A) |
| L62 / L79 / L81 / L82 / L127 / L128 / L132 | CLOSE-BY-DESIGN | As pass 3 A (§1 there): no client reader / sender can show more; ours stand |
| L85 / L86 / L87 / L90 / L109 / L111 / L119 / L120 / L131 / L165 | CLOSE-BY-DESIGN | Server-side numbers / wording / placement with no client source; ours built and documented (details §2) |
| L41 | KEEP-OPEN (no server value) | Function labelling only; next path in §2 |

---

## 2. Per lead (new this pass)

### L163 — Desperado's Deadly Arsenal: three casts, never live [P data, I history]

**Input chain [P]** (gameData/items.json record u59 → 0.695 tables):

| Item | u59 | state machine → state → props | casts (trigger 9 LMB / 10 RMB / 11 E) |
|---|---|---|---|
| 3038 Desperado's Deadly Arsenal | 48 | 45 → 71 → 78 | **793 / 794 / 795**, combo 827 |
| 3029 Katana of Crashing Waves | 39 | 36 → 62 → 69 | 766 / 767 / **768** |
| 3028 Lightbringer's Justice | 38 | 35 → 61 → 68 | 763 / 764 / **765** |
| 3039 Silent Shadow's Condemnation | 49 | 46 → 72 → 79 | 796 / 797 / **798** |
| every other launch weapon (3027, 3030-3037, 3040, 3761-3767, 4089-4107) | — | — | LMB + RMB only |

- The four weapons with an E cast are **exactly** RecipeBook.RetiredRecipeItems' four weapon recipes 3996 / 3999 /
  4000 / 4001 (RecipeBook.cs:231-241, "never craftable on live", L91 / L108). So the three-attack design never reached
  players [I strong: two independent data sets agree].
- Names / descriptions [P text_695]: 573167 Run n' Gun ↔ 573170 (revolver shot), 573168 Get 'em Good ↔ 573171 (shank
  stab), 573169 All or Nothin' ↔ 573172 (dive + burst + shank); status 573184 Dirty Fighting. The content matches
  name for name.
- Slot per name [I strong]: text-id order = cast order (793 / 794 / 795), and the damage rows rise the same way
  (basic 703 smallest, E has four rows).
- **Damage numbers [P rows]:** AbilityParameterDefinitions (StatMultiplier, stat 10, value = stat × P2 + P3,
  ParameterCalculator FUN_1435079e0): 703 = 0.45x + 45, 706 = 0.65x + 65, 704 = 1.5x + 70, 707 = 0.6x + 40,
  710 = 0.75x + 75, 708 = 1.75x + 80, 711 = 0.85x + 85, 709 = 0.7x + 50. The description tokens `[*AbilityParamN*]` tie
  each row to its attack.
- **Correction:** WeaponAttacks.cs:240 says the AbilityParameterDefinitions rows "carry no ability or weapon id, so no row
  can be tied to a weapon", and agent-l138-c2.md §3d says the 40 token texts are 2015 skill texts. Not all are:
  573112-573114 (Katana: Maelstrom attacks, rows 676-681, 686, 687), 573128 (Flame Bound's unshipped "Devouring Flame",
  691 / 693 / 695 / 696) and 573170-573172 (Desperado, 703-711) are 2016 attack texts. All belong to weapons or attacks
  that never went live, so no live kit's numbers change.
- Server today: GenericKit; Kind.Special (E) falls back to the Power attack (WeaponCombat.cs:615, :733, :1345, :1362).

### L19 — Combat Node ability rows: BUILD [P layout and order]

- The 0.695 window builder FUN_142fb71f0 (part_0079) adds rows in this order [P]: the Name row, HelpText, then **every
  drop-down** (list at reply object +0x9dc, FUN_1435be3e0), then **every plain row** (list +0x9d6): checkbox
  (max 1, min 0, step 1, FUN_1435be1e0), **TextLine when max == 0 and min == 0** (FUN_1435becc0(editor, key
  string @+0x20, label @+0)), else slider. So a Combat Tactics drop-down is drawn **above** any text rows, which is
  P6157's "set the desired Combat Tactics … Below this you will see a list of the creature's abilities".
- TextLine shows Name = the record's key string, Value = its label string (spec-reward-chests.md §3.2) [P]. No client
  text "Ability 1" exists (text_695 search), so the caption is a server string like the IF Gate's "If" / "Is".
- Wiki "Combat Node": "show a list of the creature's abilities and designate them Ability 1, Ability 2, etc".

### L46 — titles: CLOSE-BY-DESIGN [P]

decode_achievements.py over every goal: no reward entry (goal or objective bundle) has item 3925, 3926 or 4363; 3927 /
3929 / 3900 each appear once (control). 642 "Obtain …ium?" and 644 "What About Teak?" give one entry each: 80 × item
1579 (Lumens); 645 Best Harvester EVAR gives 180 Lumens + 3927. So by Feb 2017 no goal awarded Strip Miner, Deforester
or Curator. Achievements.cs AnnouncedTitles keeps 3926 → 575534 "for completeness"; it is never reached, which is
correct. Re-open only with a pre-2017 capture.

### L164 — RequestPlayerPositions: CLOSE-BY-DESIGN [P neg]

`grep -a` for "RequestPlayerPositions" and "PlayerPositions": 0 hits in <Landmark folder>\UI (ScriptsBase.bin,
Default, Admin, TestMod) and in <0.695 test copy>\UI. With re/as3 / re/as3_695 / UI Lua (agent chat5) that is every
shipped script of both builds. The native (0.589 FUN_14351e6b0, 0.695 FUN_1435c8060) has no shipped caller; whatever
the bools meant, no live player's client sent the request. Our reply stays as is.

### L110 — event island expiry [P forum]

Forum 55925 #1 (10 Dec 2015): the Halloween island "will close next week. You don't have to do anything, though feel
free to remove your builds early if you wish to use the claim flags or materials"; 58077 (Nov 2016): sites left behind
"will be expired this morning". Ours: Landscapes.LeaveClosedEventIsland moves the player; sites stay. → B3.

### Short notes on the other closes

- **L25:** fileindex_695.tsv has 26 Firework_*.fxd in the packs plus emitter icons; no "Consumable" / "Handheld" firework
  effect name exists, so a consumable can only play an emitter composite, as built.
- **L47:** agent-leads2-d1.md §L47 decoded the record {version, Room, a rot, b kind, c id}; v14 islands are always
  (a, 4, 76). Nothing on the 0.695 islands needs 61 / 75.
- **L87 / L90 / L119 / L120:** wiki pages checked this pass (Reinforced Coat of Crashing Waves, Frenzied Slayer's Battle
  Axe): no numbers. Catapult row 272 is right: the five Activate_01 rows only differ by TypeIndex (272 = 0, 404 = 1, the
  rest none); the network sees the message.
- **L109 / L111 / L131 / L165:** history / placement / cross-world rules with no client source; ours are settings
  (EventIsland, LandmarksOfLandmark placement, Outposts.Rotation OUTPOST_ROTATION_HOURS off). P5830 ("Outposts should no
  longer spawn on top of each other", 16 Jun 2016) shows live placed outposts at run time [P]; already followed
  (audit-outposts-695.md F1).
- **L41 (KEEP-OPEN):** next path: the Ghidra 0.695 project (re/Landmark695) xrefs for the 443 still unlabelled after
  pass 3, and data references in .rdata jump tables; no server change can come from it.

---

## 3. Runtime checks (coordinator; <0.695 test copy>, normal Frida runner, never killed with hooks attached)

- **T1 (L43, the visibility reader):** hook 0x143cb6e00 (RecipeDefinition loader, part_0147; stores VISIBILITY at
  `param_1 + 0x40`, FUN_142b213d0 call); log `param_1` for recipe 2003 (Recipe: Hanging Lantern, value 787). Then a
  MemoryAccessMonitor (read, 4 bytes) on that `+0x40`, and open the Lumen Station (Lua `Recipe.RecipeListSelectStation(55)`
  or the Lumen count). The first reader's return address is the check. Also note by eye whether "Recipe: Hanging
  Lantern" is listed for a character that knows the Hanging Lantern prop. If the client hides nothing, the server
  refusal (built) is the whole rule.
- **T2 (L100, built Spire):** on a non-home landscape stand on the deck, note Y; walk outside the 124 m box, note the
  ground Y. Expected now: plinth on the ground, deck ~18-19 m above, no pit.
- **T3 (after B2):** Combat Node settings unlinked → Name + Combat Tactics only; link it to a Barren Grunt spawner,
  reopen → Combat Tactics, then "Ability 1 … Ability 4" text lines with Slashing Blade 1-3 / Blade Lunge, in Edit Mode
  as well as Play Mode.

---

## 4. OPEN-LEADS entries that are behind the code (bookkeeping for the coordinator)

- L129: the type-3 roll is built (Caves.TravelRandom). L130: per-landscape crystals and the cost fix are built; the
  entry still says "Not built: per-landscape nodes". L99: type-2 anchors built. L100: the Spire fix is in Spire.cs (no
  longer "in progress on local/spire" only). L2 / L113: minmax respawn built and the default. L22: 577105 built.
- L19: the IF Gate labels and the Combat Tactics row are built; only the ability rows remain (B2).

---

## 5. BUILD specs, ranked by player-visible value

### B1 (L133) — creatures see player builds, digging and props [P behaviour, I mechanism]

**Evidence.** P5781 (7 Jun 2016): "The collision area around props, especially large trees, should now be much
smaller, allowing creatures to path past them more easily" — props blocked creatures. Pass 2 (agent-leads2-d2.md L133):
2016 players saw creatures path around their walls and doorways, and break when a floor material changed. The mesh
itself is server-only (L132), so the grid below is ours [I].

**Today.** Creatures.NavMode (Creatures.cs:902-911) returns null for story creatures (`c.Story || c.Deep`), so they step
straight through player walls; cavern creatures use Sight's cave pieces + ruin grids, never player digging
(Pathing.cs:42 note).

**Change.**
1. **EnvServer API** (EnvServer.Core/Terrain/IEnvironmentServer.cs + LocalEnvironmentServer + the remote client):
   `SolidityGrid SolidityOf(uint zone, VoxelBox box)` → the build layer (TerrainEdits.BuildLayer) of every cell meeting
   the box, reduced to 1.2 m blocks (3 × 3 × 3 voxels) exactly like Sight.AddRuin (Sight.cs:67-100): a block is
   *written* when any voxel of it is stored, *solid* when ≥ half of the written ones have material ≠ 0 and ≠
   InteriorAir. Two bitsets; a 64 m-wide, 64 m-tall site box is ~53³ = 150 k blocks = ~37 KB, computed in the service
   (remote-ready: one reply, no voxel bodies on the wire).
2. **Sight.Footprint from a site** (Sight.cs:197-215): `Footprint.FromSite(zone, siteBox, grid)`; Pathing keeps
   `siteFootprints[zone]` next to `ruinsByZone` (Pathing.cs:116, :193). Surface columns (Pathing tile build) apply them
   the way Sight.ColumnAir applies ruins to cave columns: written-solid blocks become rock, written-air blocks become air
   (a dug hole), unwritten blocks keep the generated ground (TerrainHeights).
3. **Props as obstacles:** for every placed prop on the site with a model footprint (ModelFootprints.Of(model):
   Models.txt radius + offset, already loaded), mark the columns within the radius as blocked from the prop's base to
   base + 2 m (Clearance). Skip props a creature may stand on: platform / mover kinds and props whose footprint radius is
   0 (no entry).
4. **Who uses it:** NavMode returns `Mode.Surface` for story creatures on surface sites (and `Mode.Cave` for deep ones /
   cave sites) once the site grid exists; the fail-open rule (LineResult.Mismatch / Unknown → straight step) stays.
5. **When it is built / dropped:** built lazily on the first creature step on a site (background thread, as tiles are);
   dropped (with the Pathing tiles under the site box) when the zone gets an EditResult / WriteResult / ClearResult for a
   cell inside the box, on a prop save / move / delete on the site, and on EnvironmentServer.Regenerate for those cells;
   debounce 2 s so a builder's burst of edits costs one rebuild. A material-only change also drops it (forum 56713,
   pass 2: live broke on a floor-material change — ours simply rebuilds).
6. **Cavern digging (optional second step):** the same SolidityOf over a cavern's outpost box when players dig in a
   Chaos Cavern (cells changed inside `Outposts.InZone(zone)` boxes) → OutpostsChanged-style drop (Pathing.cs:668).
7. **Setting:** reuse `CreaturePathing` (on) plus `SitePathing` (default on for 0.695 worlds; off restores the straight
   step). Bandwidth: none (server-internal; one service call per site rebuild).
8. **Test:** a story Barren Grunt chasing across a walled courtyard with one doorway must use the doorway; a placed
   crate in a corridor must be walked around; deleting the wall must let it walk straight within ~2 s.

### B2 (L19) — Combat Node "Ability N" rows [P layout / order / wording source]

- **File:** WorldServer/Game/PropSettings.cs.
- In `Visible(ulong guid, List<Definition> settings)` (PropSettings.cs:472), when the prop is
  `StoryTools.CombatNodeProp` (2940), append one read-only row per ability of the **linked spawner's creature kind**:
  `new Definition(30000 + n, $"combat_node_ability_{n}", 0, 0, 0f, 0f, 0f, 0f, Label: null, ReadOnly: true)`
  for n = 1 … count.
- **Caption / value:** extend the read-only branch of WriteRecord (PropSettings.cs:578-595), which today writes
  `((int)value).ToString()` as the label: give Definition an optional `Func<ulong, string> Text` (or a per-key
  dictionary filled in Visible) so the label string is the ability name and the key-string slot carries "Ability N"
  (readOnlyCaptions style). The i32 before the record stays `KeyHash(d.Key)`; Handle already refuses ReadOnly rows.
- **Names:** `Combat.CreatureAbilities.Of(kindName)[n-1].Name` (text id → TextManager), e.g. Barren Grunt: "Slashing
  Blade 1", "Slashing Blade 2", "Slashing Blade 3", "Blade Lunge"; a creature with no list but an attack: one row,
  "Ability 1" = 565568 "Basic Attack"; a Target Dummy / harmless kind: no rows. Count = the same rule as
  Creatures.StoryAbilityCount (Creatures.cs:3273-3279), capped at the node's six actions (577213-577216, 577243, 577244),
  so rows and the link actions agree.
- **Edit Mode:** resolve the kind from the linked spawner prop, not the live creature: StoryTools.CombatCreatureOf
  (StoryTools.cs:504-511) needs `site.Props[...].Creature != 0`, which is 0 before Play Mode. Add
  `StoryTools.CombatSpawnerOf(node)` (the 577162 link target) and `Creatures.StoryKindName(spawnerPropId, name)`
  factored out of Creatures.SpawnStory's `SpawnerKind(n, propId)` (Creatures.cs ~3092); ActionsOf (StoryTools.cs:498)
  can use the same, so "perform Ability N" links can be made in Edit Mode too.
- **Gate:** 0.695 sessions only (TextLine and the Combat Node are 0.695).
- **Order:** nothing to do — the client draws the Combat Tactics drop-down first, the text rows below (FUN_142fb71f0).
- **Docs:** docs/story-tools.md Combat Node section; remove "those rows are not sent" from PropSettings.cs:396-397.

### B3 (L110) — expire sites on an event island when it closes [P live behaviour; destructive → setting]

- **Setting:** `EventIslandExpireOnClose` (WorldServer.json; Docker `EVENT_ISLAND_EXPIRE_ON_CLOSE`), **default off**.
- **Where:** Landscapes.Load (Landscapes.cs:286-294) knows the island open this run (`EventIsland`). Persist the last
  open island id (world setting row, e.g. WorldPropSetting-style key "events_island_open"); at start, if it differs and
  the setting is on, run every claim on the previously open island (Claims.BoxesIn(zone) / the claim store) through the
  normal upkeep-expiry path: SiteContents.Pack + SiteUpkeep.MailExpired (SiteUpkeep.cs:76), so the owner gets the
  "Your upkeep has expired!" letter with the contents attached, then remove the claim and revert its cells
  (EnvironmentServer.RevertBox).
- **Why the normal expiry path:** 55925 says players may remove builds early "to use the claim flags or materials",
  i.e. the close itself returns nothing extra; our mailed contents are the existing expiry rule (TASKS / SiteUpkeep),
  so no new loss model is invented [I].
- **Log + TESTING-NEEDED:** list the sites expired; the user decides the default (memory: back up before data changes).

### B4 (L163) — three-attack kits for the never-live weapons (optional, lowest)

- **WeaponCombat.cs:** `Kit(Affinity, Basic, Power, Icon, Attack Special = null)`; where Kind.Special now maps to Power
  (lines 615, 733, 1345, 1362), use `kit.Special ?? kit.Power`. Keep WeaponAttacks rules[Kind.Special] (×2.4, 80 Energy)
  as the cost [I].
- **Desperado 3038 (Affliction, icon 12205):**
  - Basic 793 = 573167 / 573170 Run n' Gun: FirstHit 35 m; damage row 703; on a landed shot 25 % → Dirty Fighting
    (573184) for 4 s; with Dirty Fighting the next shot deals row 706 and Snares 2 s, consuming it.
  - Power 794 = 573168 / 573171 Get 'em Good: Cone 5 m / 60°; row 704; with Dirty Fighting: bleed row 707 × 3 over 3 s.
  - E 795 = 573169 / 573172 All or Nothin': Path forward (8 m [I]) with a Cone of shots (row 710; Dirty Fighting: 711 +
    Snare 2 s) during the dive, then a Cone 4 m shank on landing (row 708; Dirty Fighting: +709 × 3 over 3 s).
  - Damage = stat 10 × P2 + P3 of the row (GearStats stat 10 = the same value the client's tooltip uses, so the
    numbers the description shows and the damage agree) [P formula, I that live used the row as the hit].
- **Katana of Crashing Waves 3029** (now shares Frost Lord's Fury's kit, WeaponCombat.cs:187): its own texts exist —
  names 573078 Tsunami / 573079 Rising Tide / 573080 Crashing Wave, descriptions 573112 (basic "Cut down your foes",
  Maelstrom 573106 stacks, rows 676 / 686 / 687), 573113 (dash + knock-down, 677 / 678), 573114 (Rising Tide by its own
  text: shield 681, 679). Name ↔ slot is [I] (573114 names itself Rising Tide; 573112 names Crashing Wave as another
  attack) — only worth building together with Desperado.
- **Value:** none for normal players (recipes never live, RecipeBook.RetiredRecipeItems); only for admin-granted items.
  Do the WeaponAttacks.cs:240 comment correction regardless.

---

## 6. Hand-offs

- Combat / gear owner: WeaponAttacks.cs:240 and agent-l138-c2.md §3d claim no AbilityParameterDefinitions row ties to a
  weapon; the tokens tie 676-681 / 686 / 687 (Katana), 691 / 693 / 695 / 696 (Flame Bound "Devouring Flame", an unshipped
  third attack of 3030 — 3030's input group has no E cast) and 703-711 (Desperado). No released weapon is affected.
- Graveyards / Spire (L150): B1's site grid reuses Sight's block rule; nothing changes for graveyards.
- OPEN-LEADS bookkeeping: §4.
