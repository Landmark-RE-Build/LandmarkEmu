# Launch-era patch-note rules vs the server (21 Mar - 4 Jun 2016)

Audit of every launch-era item of trace/0695/patchnotes.tsv (era = launch, 1625 items: they describe the 0.589 client's
game) against the server code (LandmarkEmu, 2026-10-15). Second pass after TASKS 6.1-6.10: those gaps are not
repeated, only extended / answered (end of the Gaps section). Classes:

- **FOLLOWED**: the server code that handles the rule matches it (file / function cited), or the rule is client data
  the server uses as shipped (0.589 tables are the launch build's, exe 4 Jun 2016).
- **NOT FOLLOWED**: server code handles it and differs (difference cited).
- **NOT BUILT**: no server code handles it (files searched cited).
- **SUPERSEDED**: a later launch item changed it (both cited).
- **client-only / no server rule**: UI, visuals, client bugs, known-issue reports, forum chatter and questions,
  developer design talk that did not ship, wipe / migration / store notices.

Item ids can shift when patchnotes.tsv is regenerated (TASKS 6.1-6.10 quote older P3xxx ids); date, thread and text
are quoted with each gap. Scratch: re/out/agent_launch/ (classify.py holds the per-item verdicts, gen.py writes this
file).

## Summary

| Class | Items | of which already in TASKS 6.1-6.10 / 7 |
|---|---|---|
| NOT FOLLOWED | 241 | 168 |
| NOT BUILT | 52 | 3 |
| SUPERSEDED | 15 |  |
| FOLLOWED | 203 |  |
| client-only / no server rule | 1114 |  |
| **Total** | **1625** | |

Rule-bearing items (everything but client-only): 511. New gaps below: 6.12-6.56 (45 task lines).

Per system (the `system` column of patchnotes.tsv):

| System | Items | NOT FOLLOWED | NOT BUILT | SUPERSEDED | FOLLOWED | client-only |
|---|---|---|---|---|---|---|
| combat | 223 | 116 | 8 | 0 | 37 | 62 |
| other | 187 | 26 | 2 | 0 | 8 | 151 |
| ui | 157 | 2 | 2 | 0 | 7 | 146 |
| props | 144 | 9 | 5 | 4 | 16 | 110 |
| storytools | 118 | 14 | 9 | 0 | 30 | 65 |
| gathering | 101 | 10 | 2 | 0 | 13 | 76 |
| logic | 97 | 7 | 1 | 0 | 7 | 82 |
| building | 84 | 2 | 1 | 2 | 9 | 70 |
| progression | 82 | 8 | 0 | 0 | 25 | 49 |
| claims | 80 | 5 | 2 | 7 | 9 | 57 |
| creatures | 56 | 12 | 6 | 1 | 3 | 34 |
| outposts | 50 | 7 | 2 | 0 | 4 | 37 |
| social | 40 | 2 | 4 | 0 | 5 | 29 |
| equipment | 39 | 9 | 0 | 1 | 2 | 27 |
| crafting | 39 | 5 | 1 | 0 | 17 | 16 |
| commerce | 39 | 3 | 4 | 0 | 4 | 28 |
| travel | 36 | 1 | 3 | 0 | 4 | 28 |
| character | 31 | 2 | 0 | 0 | 3 | 26 |
| instancedgames | 13 | 1 | 0 | 0 | 0 | 12 |
| client | 9 | 0 | 0 | 0 | 0 | 9 |

Most important new gaps: 6.49 (a Spire on every landscape), 6.46 (building outside sites accepted), 6.20 / 6.23 /
6.25 (creature allies, outpost respawns, Lost Crystal lockout), 6.16 (no lumens / recipes from combat), 6.38-6.40
(recycle / site delete / designs: the prop economy returns items instead of materials), 6.12 / 6.50 / 6.51 (plant
harvesting, Local Travel and PvP tables still live although launch removed them), 6.42 (character deletion), 6.43
(gallery), 6.56 (Play Mode lost on restart).

## Gaps (ready to copy into TASKS.md section 6)

Item ids are from the current trace/0695/patchnotes.tsv (they differ from the P3xxx ids quoted in TASKS 6.1-6.10,
which come from an earlier generation of the file); date, thread and text are quoted so they can be found again.

### Gathering and loot

- [ ] **6.12 Plants are decoration; sickles gather nothing** (P4128, 21 Mar 2016, "Landmark Update Notes - March 21":
      "Plants are no longer harvested, and sickles are no longer used for anything."): Gathering.cs still spawns
      harvestable plant nodes (PlantKind, PlantDensity 0.04 in Region()) and Gathering.Reap gives plant items to sickle
      swings (Abilities ReapingAbilities). Must: no plant nodes (plants stay client decoration), sickle swings gather
      nothing.
- [ ] **6.13 One wood per biome; Old Growth gives Burled Wood** (P4628, 13 Apr 2016, "Update Notes - April 13":
      "Trees within a biome should now more consistently drop the same type of wood"; P5415, 11 May 2016, "Hotfix Notes
      - 11 May": "All old growth forest trees should now be giving burled wood"): Gathering.Region (~line 552) gives
      Old Growth (biome 50) small trees Plain Wood (71) and only large ones Burled Wood (72). Must: every Old Growth tree
      gives Burled Wood; one wood item per biome.
- [ ] **6.14 Axe perks and Harvest Bonus on trees** (P4131, 21 Mar 2016: "Picks and Axes now have different abilities
      such as increased speed, damage, or resource bonus"; P4389, 1 Apr 2016, "Hotfix notes - April 1": harvest-bonus
      items "granted more tree resources than intended", i.e. they apply to trees): Abilities.cs:247 calls
      `Gathering.Chop(session, guid, aimedAt)` without the tool item, so the Charmed Axe (3260, luck), Goliath Axe
      (3261, yield) and Hotfoot Axe (3263, speed burst) never work; Chop also ignores Harvest Bonus (stat 17), which
      mining applies (Abilities.cs:715). Must: pass `source.ItemDefinitionId` and apply stat 17 to wood.
- [ ] **6.15 No mining or building next to a Portal Spire** (P4425, 1 Apr 2016, "Hotfix notes - April 1": "If you try to
      mine beside the spire you should now get clearer feedback"; client text 560944 "You are near a Portal Spire. You
      must move away before you can mine or build."): Spire.Covers is only used for tree placement (Gathering.Ground) and
      teleports; Abilities.Mine, Building and TerrainEdits.MayApply allow digging and building around the Spire (only
      its stamped base is kept from regrowth, TerrainEdits.MarkPermanent); text 560944 is never sent. Must: refuse
      mining and building inside the Spire's area with text 560944. (TASKS 3.17 "no ore nodes around spires" is the
      later, separate rule.)
- [ ] **6.16 Lumens and recipes from combat; Chaos Cavern monsters drop resources** (P4169, 21 Mar 2016: "Any activity -
      such as harvesting or combat - that gets you loot has a chance of giving you lumens and recipes"; P4166 / P4224
      recipes unlocked "as you fight"; P4353, 1 Apr 2016: "Enemies in Chaos Caverns now drop random resource rewards";
      P4536, 13 Apr 2016: drop rate of rare resources on Chaos Cavern NPCs): Creatures.RollLoot is a fixed per-kind list
      with no lumen or recipe roll; cavern Chompers / Slaugs / Abominations drop essences only (Creatures.CavernKinds),
      outpost garrisons copper + Adventure Essence (SpawnerKind). Must: lumen and recipe chances on kills (paced like
      6.3), random resources (some rare) from cavern monsters. Extends 6.3 / 6.5.
- [ ] **6.17 Essence pacing** (P4776, 26 Apr 2016, "Hotfix notes - 26 April": "You now earn fewer essences (monster
      essence, adventure essence, fire essence, etc.) at the beginning of a play session, but more over longer
      sessions"): essences are flat chances (Gathering.EssenceChance 0.03, creature loot tables). Must: the same session
      curve as lumens (6.3).
- [ ] **6.18 Repair Bot Beacon as rare global loot** (P4835, 26 Apr 2016: "Repair Bot Beacons can also drop rarely as
      global loot"): no loot path gives item 4129 (searched Gathering.RollDrops, Creatures.RollLoot, LootChests,
      Outposts chest / crystal loot). Must: a rare beacon drop wherever loot rolls.

### Creatures and combat

- [ ] **6.19 Cozy Campfire rules** (P4190, 21 Mar 2016: "If you're near the campfire while not in combat, you'll
      immediately be healed to full. (Campfires have no effect during combat.)"; P4539, 13 Apr 2016: "Enduring Warmth
      buff can now be refreshed every minute"; P4702, 26 Apr 2016: not castable "while jumping or falling"):
      Consumables.UpdateCampfires heals 4 % a second within 8 m even in combat, and gives Enduring Warmth (10 min) only
      at full life while no heal-over-time runs; LightCampfire has no airborne check. Must: nothing during combat
      (e.g. Health.HurtWithin as Outposts uses), full heal at once out of combat, Enduring Warmth refreshable once a
      minute, refuse lighting while airborne (check whether the client already blocks it).
- [ ] **6.20 Ally creatures fight enemies; combat before scripted walks** (P4223, 21 Mar 2016: "Ally creatures will
      assist players against enemies. And neutral creatures will simply watch"; P5225, 4 May 2016, "[Feedback] Prop
      Linking", dev: "Combat AI has the highest priority on all creatures"): Creatures.Think only ever targets players
      (allies and neutrals just leave players alone), and in Idle a story walk comes first and "ignores players on the
      way". Must: allies attack enemy creatures near them and enemies attack allies; combat interrupts walks. (Then
      P4719 applies: no loot when an ally or neutral makes the kill, 6.5.)
- [ ] **6.21 Creature AI details** (P4715, 26 Apr 2016: "Creatures who return to their spawn point should now also face
      in the correct direction"; P4696: "Healers will now pursue enemy targets who move out of their range when not busy
      healing"; P4717: "You should now have a few seconds to recover after reviving, before Creatures attack you
      again"): the Return state restores position and health but not the spawn heading; no creature heals; Idle aggro
      takes any living player within 12 m and Health.Revive sets no grace. Must: restore the heading, ignore a revived
      player for a few seconds, healer behaviour when casters heal.
- [ ] **6.22 Elites and very large creatures ignore crowd control** (P5266, 11 May 2016, "Hotfix Notes - 11 May":
      "'Elite' creatures and very large creatures are now immune to stun, root, freeze, snare, and silence (burn) ...
      the attack's effects will still play"): Combat/Statuses.Apply applies every status to any entity; only the
      armour execute skips Elites (Creatures.cs:669). Must: on Elites / bosses (and large kinds) play the effect but
      don't apply the disabling statuses (Freeze, Electrocute, Crush, Condemn, Cripple, Burn's silence).
- [ ] **6.23 Outpost respawn timers** (P5193 / P5195 / P5197, 4 May 2016, "[Feedback] Chaos Caverns + Outposts", dev:
      "every non-elite Outpost creature should have a 8-10 minute respawn. Elites have 15 minute respawn"):
      Creatures.Kill sets `RespawnAt = now + RespawnTime` (3 min) for every creature, outpost garrisons and Elites
      included. Must: 8-10 min (random) for outpost creatures, 15 min for Elites. (6.6 is about world creatures.)
- [ ] **6.24 Elite loot and the hidden chest's spot** (P5628, 25 May 2016, "Update Notes - 25 May": "Lost Crystals, Lost
      Treasure Chests ..., and Outpost Bosses now drop significantly more loot"; P5014, wiki Game Updates 2016:
      "increased drop rate for Recipes"; P4806, 26 Apr 2016: the hidden chest "won't always be in the same place"):
      the Elite drops what any garrison creature drops (SpawnerKind: copper 1-3, Adventure Essence 0-1); the Lost Chest
      is always at one spot per outpost (Outposts.cs:240, the same prop after ChestRespawn). Must: an Elite loot table
      well above the minions' (recipes, essences, resources); the chest moves between several hidden spots when it
      comes back (open lead L12).
- [ ] **6.25 Lost Crystal loot lockout** (P4123, 21 Mar 2016: "you will only get rewards from the first one you find
      within a specific cave network"; P5180 / P5181, 4 May 2016, dev: "The timer is for each Lost Crystal, though, not
      globally" and "when you use the Lost Crystal while it's on Lockout, it should just show the teleport prompt message
      without giving you another reward"): Outposts.CompleteChannels gives loot on every channel; there is no timer.
      Must: a per-player, per-crystal lockout; while locked, no loot, only NextUndergroundOffer. Length unknown (open
      lead L10). TASKS 3.14 has the later, longer timer.
- [ ] **6.26 Group trips underground** (P5188, 4 May 2016, dev: "If someone goes to a new Chaos Cavern via the Travel
      Menu, you'll get a message asking if you want to go with them, but you should be able to decline it"; P5203: the
      group members who use the same crystal go to the same next Chaos Cavern): Caves.TravelRandom and
      Outposts.NextCave pick a cavern per player; groups play no part. Must: offer the trip to group members (accept /
      decline) and send a group using the same crystal to the same cavern.
- [ ] **6.27 No Underground Adventure from Pioneer landscapes in 0.589** (P5158, 4 May 2016: "selecting 'An Underground
      Adventure' from the Travel Menu on any non-Pioneer Landscape"; P4299, 21 Mar: "There are no Chaos Caverns ... on
      Pioneer Landscapes"): Travel.Handle RandomUnderground → Caves.TravelRandom works from any landscape. Must refuse
      it on Pioneer lands for 0.589 (0.695 allows it, TASKS 3.14). Low.
- [ ] **6.28 Target Dummy** (P5275 / P5433, 11 May 2016: "This NPC will stand in one location and intermittently swing
      its sword. Note that it does not do any actual damage, and will not be relevant to any enemy NPCs. The target
      dummy is default learned for all players"; P5445, 13 May: it "can be attacked and 'killed'"): prop 2890 is not a
      spawner to StoryTools.KindOf (its name lacks "Spawner:"), so it stays a static prop; only Achievements.cs:645
      knows it. Must: in Play Mode a killable, harmless, slowly swinging creature that other creatures ignore; check
      it is default-learned in the recipe data.
- [ ] **6.29 Proximity Mines** (P5269, 11 May 2016: "Proximity Mines are treated as creatures on your build site, and
      they have the same faction and respawn settings as other non-pathing creatures like fiery fungi"; P4866, 26 Apr:
      "Proximity mines should once again work"): prop 1220 has no Link kind and no code (StoryTools, Creatures, Props
      searched). Must: a stationary exploding creature like the Fiery Fungus, with faction and respawn settings.
      (TASKS 2.15 is its later terrain damage.)
- [ ] **6.30 Spawner settings: faction, name, duplicate** (P4222, 21 Mar 2016: "Creatures can be set to ally, neutral,
      or enemy faction"; P4883, 26 Apr: "Right-click both creatures and choose 'Open Settings' to change both
      creatures' Faction to Ally"; P4227, 21 Mar: "Right-click a creature to access more options such as the creature
      name and respawn rate"; P4352, 1 Apr: "When duplicating a creature, the name, faction and respawn settings should
      now copy"): PropSettings `settingsByKind["spawner"] = {18, 19, 31}` leaves out EditableProperties row 17
      `prop_npc_spawner_faction_select` (0-2) and Creatures.SpawnStory never reads a faction (enemy until a Link changes
      it); the creature is named after the prop definition (StoryTools.PlayModeChanged `Running.Name`), not the
      spawner's custom name; Props.Duplicate copies only the tint. Must: offer and apply row 17, use the custom name,
      copy name and settings on Duplicate.
- [ ] **6.31 Creatures can't be tinted** (P4354, 1 Apr 2016: "Creatures placed on your build sites can no longer be
      tinted"; P4466 dev reason): Props.SetTint tints any prop the player owns. Must refuse creature spawners (and props
      without tints). Low (the client may already hide it).
- [ ] **6.32 Interaction prompt only where something happens** (P5304, 11 May 2016: "The prompt to interact ... now only
      appears on creatures who have active and available interaction Links on them"; P5305: "While on the Enemy
      Faction, creatures now realize they're too busy killing you to be interacted with"; P5482 / P5492, 25 May):
      Creatures.SendSpawn flags every story creature interactable and StoryTools.ClickCreature runs the click Links
      whatever the faction. Must: flag only creatures with a "clicked" Link that aren't Enemy (re-flag on faction
      change); ignore clicks on enemies.
- [ ] **6.33 Story Node text display** (P4876, 26 Apr 2016: chat bubble, chat channel or on screen; P5297, 11 May: "Text
      Display setting now defaults to Chat Bubble"; P5477, 25 May: "Chat & Chat Bubble"; P5478: "Text Display Duration";
      P5495: own area, shown "for the duration specified on the story node"): StoryTools.Perform always sends a
      creature's text as a Say line (Chat.NpcSay) and ignores the node's Text Display and Duration;
      PropSettings.DropDowns[TextDisplayType] lacks 0.589's text 577170 "In Chat Bubble and Chat". Must: route the text
      by the node's setting (bubble / chat / both / on screen) for its duration and add the "both" option. (TASKS 2.5 is
      the later formatting.)
- [ ] **6.34 Story Node fallback speakers** (P5286-P5288, 11 May 2016: with no outgoing text Link, "the Story Node checks
      whatever triggered the Link initially. If it's a creature, then that creature will say the Text", otherwise "the
      Story Node itself will say the Text" (no bubble); P5284 / P5116 same for animations): text reaches only creatures
      linked with "...Have this creature say the text." (StoryTools.Perform). Must add both fallbacks.
- [ ] **6.35 Story Node text limits** (P4875, 26 Apr 2016: "store up to 512 characters of text"; P5299, 11 May: "text
      can no longer display HTML"): Props.SetText caps every prop's text at 1024 characters and passes markup through.
      Must: 512 for Story Nodes; strip or escape HTML in prop text (first check whether the 0.589 client escapes it).
- [ ] **6.36 Linkable sconces, emissive orbs / cubes, control panels** (P4856, 26 Apr 2016: "All interactive props
      (including emitters and lights) should now function properly with linking and triggering"; P5371, 11 May:
      "Buccaneer wall sconce can now turn on and off"; P5222, 4 May dev: "The Round Control Panel should be Linkable";
      P5227: the emissive cube's "...turn this glow off." Action): StoryTools.namedKinds matches words in the prop name,
      so the Wall / Hanging Sconces (691, 692, 2096, 2204, 2581), the Emissive Orbs and Cubes (2127-2142) and the Control
      Panels (1172, 1176, 1177) get no Link kind. Must: give them their kinds (light, glow, switch) from the client's link
      texts; then sweep every prop name the word list misses.
### Build sites, props and designs

- [ ] **6.37 One Starting Location per site** (P4818, 26 Apr 2016: "Attempting to place a new Starting Location prop if
      you already have one placed on your build site will now automatically pick up the old one and place it on your
      cursor"): Props.BeginPlacement places a second one; Claims.ArrivalOf takes whichever it finds first. Must: move
      the existing one instead.
- [ ] **6.38 Recycle returns the resources** (P5344, 11 May 2016: "When a prop is recycled, you will see what resources
      you got back in the loot window"; trace/systems/building.md "Recycle returns them all"): Props.PickUp with Delete
      (PropBase 0x07) refunds nothing; PickUp (0x05) gives the prop item back (Props.Remove → GiveItem), never the
      recipe materials. Must: Recycle returns the recipe components (Props.RecipeCost) with loot notices; "Send to
      Inventory" stays for props placed from items. First confirm which sub the 0.589 menu's Recycle and Send to
      Inventory send.
- [ ] **6.39 Site delete and foreclosure return props as materials and all resources** (P4870, 26 Apr 2016: "Deleting
      your site (or automatic foreclosure) should now return the props as materials, not as items in your overflow";
      P4133, 21 Mar: on expiry "A Design of the Build Site and all resources will be sent to the Luminary via in-game
      mail"; P4365, 1 Apr: expiry designs "should save their screenshots"): Claims.Delete returns props as items
      (Props.ReturnInBox) and voxel materials (SiteDesigns.Refund); a lapsed site (SiteContents.Pack) packs props as
      items behind the Build Site Contents item and refunds no voxel materials ("the server doesn't know which of them
      the player paid for"); SiteDesigns.Save copies no screenshots. Must: props back as materials in both cases, a
      lapsed site's resources by mail, the site's screenshots on its design.
- [ ] **6.40 Designs build known props from materials** (P4462, 1 Apr 2016, dev: "You cannot 'create' the prop (via
      Build Mode or Template placing) without the recipe" — with it you can; P4569, 13 Apr: designs also use inventory
      props without the recipe): Props.PlaceDesignProp places a design's prop only from a bag item ("a design gives the
      layout, not the props"). Must: a bag item first, else build it from materials when the recipe is known and
      affordable, else list it as skipped (ZoneMissingPropList) or unknown (ZoneUnknownPropList).
- [ ] **6.41 Chests aren't reclaimable** (P4853, 26 Apr 2016: "Chests are intentionally not reclaimable as they may hold
      items inside"): Props.PickUp lets a chest with items be picked up (its contents go with the item, Chests.Move).
      Decide: refuse non-empty chests like live, or keep this as a deliberate convenience. Low.
- [ ] **6.42 Character deletion clean-up** (P4958, 26 Apr 2016: "Deleting a character now automatically creates a
      Design for any build sites that character had in the world"; P4624, 13 Apr: "Deleted characters will now be
      correctly removed from friend and guild lists"; P5412, 11 May: "Names of deleted characters no longer remain in
      the Special Access list"; P5311 guild / officer channels; P5312: "Guild News should now persist the name of
      players who are deleted"): AuthServer LoginHandler.HandleCharacterDeleteRequest only sets DeleteTime and
      OriginalName; the world server leaves the sites placed and the friend rows, guild membership and Special Access
      entries in place (Friends.cs checks DeleteTime only when adding). Must: on delete, save each site as a design and
      remove it, drop the character from friends, guilds (names kept in guild news) and Special Access.
- [ ] **6.43 Gallery: published sites, tags, comments, lists, Play Mode filter, likes** (P4590 / P4591, 13 Apr 2016:
      search by the three tag drop-downs and the site name, untagged sites hidden; P4783, 26 Apr: publish / unpublish
      button; P4139 / P4686: comments on gallery entries; P4670: leaderboards (most-liked) and "rising stars" (recently
      listed); P5624, 25 May: "filter Build Sites by Play Mode status"; P5630, 25 May: "It is only possible to Like a
      build that is listed in the Gallery"; client text 576478 "You must publish your Build Site to the gallery before
      submitting to a competition"): Store.Gallery lists every site ("TODO: only published sites", though
      Commerce.IsOnGallery exists), the page (Resources/Web/showcase.js) searches free text over one vote-ordered list,
      no comments; Ratings.HandleAddVote takes votes for any site; Contests entries don't check publishing. Must follow
      each.
- [ ] **6.44 Ultimate Power may switch Play / Edit Mode** (P5343, 11 May 2016: "Builders with full ('ultimate power!')
      permissions on another person's build site can now change Play/Edit mode"): Claims.SetAdventureStatus accepts
      only the owner (OwnedClaim). Must also accept players with all permissions (0x11F).
- [ ] **6.45 Player Studio designs only on your own site** (P4632, 13 Apr 2016: "error messaging for attempts to place a
      Player Studio Design on a build site you do not own"; P4954, 26 Apr): BuildingClipboard's design paste checks only
      build permission (TerrainEdits.MayApply). Must refuse a bought design on someone else's site, with the client's
      message.
- [ ] **6.46 Build only inside build sites** (P4141, 21 Mar 2016, Build Mode: "you can only actually make use of it on
      a build site where you have access to build"; P5651, 25 May: "It is still possible via a bug to paste rotated block
      selections outside your claim boundaries; needless to say this is unintended"): Claims.BuildCheckIn /
      TerrainEdits.MayApply allow every point outside all sites ("Outside claims: yes"), so build edits, heals and pastes
      (Building ApplyDiff, BuildingClipboard) are accepted in the open world. Must refuse build-layer edits and pastes
      outside a site where the player may build (mining stays allowed).
- [ ] **6.47 Site height limits** (P4135, 21 Mar 2016: sites "can be sunk somewhat into the terrain and can be floated
      somewhat into the air"; P4298: "You cannot place your Build Site far from the surface in the underground"):
      Claims.CanPlace / PlaceConfirm accept any centre height. Must check the centre against the ground (take the
      limits from the client's placement code).
- [ ] **6.48 New-player site fallbacks** (P4116, 21 Mar 2016: Pioneer surface first, "Second priority places build sites
      in an underground cavern on a Pioneer Landscape", then a classic Landscape; P4361, 1 Apr): Pioneer.Place tries
      Pioneer surfaces only, else the character gets no site. Must add the Pioneer-cavern and classic-landscape steps.

### Travel and world

- [ ] **6.49 A Spire on every landscape** (P5647, 25 May 2016: "Central spires are now raised slightly higher out of the
      ground"; P4946, 26 Apr: "the travel menu now allows travel to the nearest portal spire at any time"; P4965:
      "When traveling to a pioneer landscape you should now correctly arrive at the spire"): Spire.cs builds one Spire,
      on the home continent; Spire.TravelTo re-zones there from any other landscape; Landscapes.TravelTo lands other
      landscapes at their centre with no Spire. Must: stamp a Spire (base_portal_spire.otd, Replicator, Repair Station,
      Mailbox, Parlor, crystal) at each landscape's centre, Pioneer lands included, and send "Spire" travel to the
      current landscape's.
- [ ] **6.50 No Local Travel at launch** (P4246, 21 Mar 2016, known issue [Fixed]: "'Local' is still showing as an
      option to travel at the portal spire, but should be removed."): WarpNetwork.cs serves the Spire's Local Travel map
      and 81 underground portal crystals (InteractionHandler: Spire crystal → WarpNetwork.ActivateTerminal). Must switch
      Local Travel and the leyline warps off for 0.589 (FEATURES: "The Leyline Network was removed at launch").
- [ ] **6.51 No PvP at launch** (P4230, 21 Mar 2016: "We have removed PVP game tables. PVP is no longer available.";
      P4302: "There is no PvP."): InstancedGames.cs runs Deathmatch (1346), Team Deathmatch (1347) and Team Zone Control
      (1226) tables in which players hurt each other. Must: PvP tables unavailable in 0.589 (Adventure 2313 stays); the
      29 Sep 2016 rework (2.12) brings games back for 0.695. Needs the user's decision.
- [ ] **6.52 Vendor Booth is decorative** (P4378, 1 Apr 2016: "Vendor Booth had an old, non-functioning UI attached which
      has been removed ... it will be available in Landmark simply as a decorative prop"): Vendors.cs makes "Build
      Site_Vendor_Object" (1491) a working shop (ClaimVendorBase, coins, listings). Decide: off for live fidelity, or
      keep as a documented extra.

### Gear, goals

- [ ] **6.53 Gathering tools wear** (P4524, 13 Apr 2016, "Update Notes - April 13": "For equipped harvesting tools,
      condition is lost when the tool is used to harvest"): Durability.cs "Gathering tools have no set and never wear";
      DurabilitySet rows 1 (9751) and 5 (14751) have no loss per damage or death (tool-like). Must: find which items use
      sets 1 / 5 and the loss per harvest (open lead L11), wear tools on use, repair them like gear.
- [ ] **6.54 One of each accessory** (P4840, 26 Apr 2016: "Multiple copies of the Ring of Bounty can no longer be
      equipped at the same time"): Inventory.Fits / ToggleEquip only check the slot. Must refuse an item whose
      definition is already in the other accessory slot.
- [ ] **6.55 Daily goals count for the group** (P4559, 13 Apr 2016: "Daily Goals should now update harvesting goals for
      everyone who receives harvests, and combat goals for everyone fighting the creature"): Gathering.GiveHarvest
      shares the items, but Achievements.Harvested / DailyGoals.Harvested run for the gatherer only, and Creatures.Kill
      counts the kill for the killer only. Must count harvests for every member who received them and kills for
      everyone who damaged the creature.

### Play Mode

- [ ] **6.56 Play Mode survives restarts** (P4248, 21 Mar 2016, known issue [Fixed]: "On a server reset, pathing
      creatures on build sites will not start pathing again until play mode is turned off and on again"; P4351, 1 Apr:
      "Server reset should no longer make creatures stop pathing"): Claims.cs keeps Play Mode in memory only ("Not
      persisted: sites start in Edit"), so a restart stops every site's story. Must: save the mode per site and restart
      Play Mode (StoryTools.PlayModeChanged) at load.

### Additions and answers for existing items (merge into 6.1-6.10, 5.2)

- 6.5: P4349 (1 Apr, "Exiting play mode no longer has a chance to drop loot") is followed (StoryTools.Stop →
  Creatures.DespawnStory, no loot); P4801 (26 Apr, "Fiery Fungi should no longer update any kill goals") is followed
  (Creatures.ThinkFungus calls no kill counters). Still open: campfire drops (P4594), fungus tree loot (P4533), ally
  kills (P4719, waits for 6.20).
- 6.6: answered for build sites: EditableProperties row 18 `player_npc_spawn_delay_flat` is 0-300 s (default 30),
  which the spawner window offers (P4718, 26 Apr, "Creature respawn time on your Site can now be set up to 5
  minutes"). Outposts: see 6.23.
- 6.10: add P5437 / P5328 (11 May): "The combat achievement H2-Ouch now grants the Reinforced Coat of Crashing Waves
  recipe instead of the Frost Lord's Guard recipe". P4789 (free tutorial designs can't be deleted) is followed
  (Templates.Delete answers "cannot delete" for Starter Castle designs).
- 5.2: P4235 (21 Mar, "The Foundation Museum islands are no longer accessible.") — at launch the Museum was closed;
  Landscapes.cs already leaves the Museum zones out, so for 0.589 the server matches the notes.

## Per-item table

Grouped by the `system` column; within a system by id. Evidence names the server file / function; `→` is the gap.

### combat (223)

| Id | Date | Thread | Class | Evidence | Text |
|---|---|---|---|---|---|
| P4112 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | New characters now select a starter outfit which determines the look of their starter a... |
| P4155 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | While the ability to rearrange the build tool keys may be something we consider in the ... |
| P4169 | 2016-03-21 | Landmark Update Notes - March 21 | NOT BUILT | Creatures.RollLoot is a fixed per-kind list: no lumen or recipe roll on kills (Gathering.RollDrops has them) → 6.16 | Any activity - such as harvesting or combat - that gets you loot has a chance of giving... |
| P4190 | 2016-03-21 | Landmark Update Notes - March 21 | NOT FOLLOWED | Consumables.UpdateCampfires: 4 %/s heal within 8 m even in combat; Enduring Warmth only at full life when no HoT runs → 6.19 | Campfires – for speedy recovery while you’re out fighting, you can place a campfire. If... |
| P4191 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Combat/Statuses.cs + Gear.cs + WeaponCombat.cs implement all 21 affinity properties (chances / sizes ours) | Weapons, armor, and accessories have a “damage type” indicated by a colored symbol on t... |
| P4192 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Combat/Statuses.cs + Gear.cs + WeaponCombat.cs implement all 21 affinity properties (chances / sizes ours) | **[image: [IMG]] Fire** weapons are wild and violent. |
| P4193 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Combat/Statuses.cs + Gear.cs + WeaponCombat.cs implement all 21 affinity properties (chances / sizes ours) | **Erupt:** Successful attacks can deal double damage. |
| P4194 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Combat/Statuses.cs + Gear.cs + WeaponCombat.cs implement all 21 affinity properties (chances / sizes ours) | **Frenzy:** When activated all attacks are temporarily faster. |
| P4195 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Combat/Statuses.cs + Gear.cs + WeaponCombat.cs implement all 21 affinity properties (chances / sizes ours) | **Burn:** Opponents can be set aflame, which temporarily stops their attacks while deal... |
| P4196 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Combat/Statuses.cs + Gear.cs + WeaponCombat.cs implement all 21 affinity properties (chances / sizes ours) | **[image: [IMG]] Water** weapons are elegant and inevitable. |
| P4197 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Combat/Statuses.cs + Gear.cs + WeaponCombat.cs implement all 21 affinity properties (chances / sizes ours) | **Flow:** Successful attacks can temporarily increase maximum energy. |
| P4198 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Combat/Statuses.cs + Gear.cs + WeaponCombat.cs implement all 21 affinity properties (chances / sizes ours) | **Surge:** When activated all stored energy is used to deal bonus damage. |
| P4199 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Combat/Statuses.cs + Gear.cs + WeaponCombat.cs implement all 21 affinity properties (chances / sizes ours) | **Freeze:** Opponents can be frozen, which temporarily stops their attacks and movement. |
| P4200 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Combat/Statuses.cs + Gear.cs + WeaponCombat.cs implement all 21 affinity properties (chances / sizes ours) | **[image: [IMG]] Lightning** weapons are energetic and intense. |
| P4201 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Combat/Statuses.cs + Gear.cs + WeaponCombat.cs implement all 21 affinity properties (chances / sizes ours) | **Energize:** Successful **Power Attacks** can temporarily increase energy regeneration. |
| P4202 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Combat/Statuses.cs + Gear.cs + WeaponCombat.cs implement all 21 affinity properties (chances / sizes ours) | **Supercharge:** When activated all **Power Attacks** temporarily deal more damage and ... |
| P4203 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Combat/Statuses.cs + Gear.cs + WeaponCombat.cs implement all 21 affinity properties (chances / sizes ours) | **Electrocute:** Opponents can be electrocuted, which temporarily stops their attacks a... |
| P4204 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Combat/Statuses.cs + Gear.cs + WeaponCombat.cs implement all 21 affinity properties (chances / sizes ours) | **[image: [IMG]] Earth** weapons are indomitable and overwhelming.. |
| P4205 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Combat/Statuses.cs + Gear.cs + WeaponCombat.cs implement all 21 affinity properties (chances / sizes ours) | **Regrowth:** Successful attacks can restore missing life. |
| P4206 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Combat/Statuses.cs + Gear.cs + WeaponCombat.cs implement all 21 affinity properties (chances / sizes ours) | **Harden:** When activated the next opponents attack deals reduced damage. |
| P4207 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Combat/Statuses.cs + Gear.cs + WeaponCombat.cs implement all 21 affinity properties (chances / sizes ours) | **Crush:** Opponents can be knocked to the ground, which temporarily stops their attack... |
| P4208 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Combat/Statuses.cs + Gear.cs + WeaponCombat.cs implement all 21 affinity properties (chances / sizes ours) | **[image: [IMG]] Affliction** weapons are insidious and persistent. |
| P4209 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Combat/Statuses.cs + Gear.cs + WeaponCombat.cs implement all 21 affinity properties (chances / sizes ours) | **Infect:** Successful attacks can temporarily increase damage. |
| P4210 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Combat/Statuses.cs + Gear.cs + WeaponCombat.cs implement all 21 affinity properties (chances / sizes ours) | **Rot:** Opponents can temporarily suffer from reduced damage and damage over time. |
| P4211 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Combat/Statuses.cs + Gear.cs + WeaponCombat.cs implement all 21 affinity properties (chances / sizes ours) | **Cripple:** Opponents can be crippled, which temporarily slows their movement. |
| P4212 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Combat/Statuses.cs + Gear.cs + WeaponCombat.cs implement all 21 affinity properties (chances / sizes ours) | **[image: [IMG]] Radiance** weapons are dazzling and empowering. |
| P4213 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Combat/Statuses.cs + Gear.cs + WeaponCombat.cs implement all 21 affinity properties (chances / sizes ours) | **Rejuvenate:** Successful attacks can temporarily increase life regeneration. |
| P4214 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Combat/Statuses.cs + Gear.cs + WeaponCombat.cs implement all 21 affinity properties (chances / sizes ours) | **Exalt:** When activated all attacks temporarily deal more damage and all weapon prope... |
| P4215 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Combat/Statuses.cs + Gear.cs + WeaponCombat.cs implement all 21 affinity properties (chances / sizes ours) | **Illuminate:** When activated a shield temporarily intercepts damage from opponents. |
| P4216 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Combat/Statuses.cs + Gear.cs + WeaponCombat.cs implement all 21 affinity properties (chances / sizes ours) | **[image: [IMG]] Shadow** weapons are ravenous and punishing. |
| P4217 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Combat/Statuses.cs + Gear.cs + WeaponCombat.cs implement all 21 affinity properties (chances / sizes ours) | **Siphon:** Some of the damage dealt on successful **Power Attacks** is converted to life. |
| P4218 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Combat/Statuses.cs + Gear.cs + WeaponCombat.cs implement all 21 affinity properties (chances / sizes ours) | **Torment:** Opponents can temporarily take more damage from all attacks. |
| P4219 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Combat/Statuses.cs + Gear.cs + WeaponCombat.cs implement all 21 affinity properties (chances / sizes ours) | **Condemn:** Opponents can be condemned, which temporarily stops their movement. |
| P4247 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | You need to equip starter weapons TWICE in order to update the starter Goal. |
| P4277 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | The ability to search the gallery for key words in the claim name is not yet fully func... |
| P4286 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | [Fixed] Guild information was not being saved, which may have had a number of odd side ... |
| P4302 | 2016-03-21 | Landmark Update Notes - March 21 | NOT FOLLOWED | InstancedGames.cs runs Deathmatch / TDM / Zone Control tables where players hurt each other → 6.51 | There is no PvP. We'll update the notes to make that more clear. Sorry for the confusion. |
| P4340 | 2016-03-25 | Week End Wrap Up - March 25, 2.. | client-only / no server rule | feedback thread / announcement / chatter | Another update - found an issue we really need to fix first (the ability to create temp... |
| P4346 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Projectiles no longer sometimes appear to target the opponent's feet instead of their c... |
| P4347 | 2016-03-31 | Hotfix notes - April 1 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Fixed some weapons that were applying effects to more targets than intended (e.g. earth... |
| P4348 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | NovaTech Pioneer can now be placed |
| P4349 | 2016-03-31 | Hotfix notes - April 1 2016 | FOLLOWED | StoryTools.Stop → Creatures.DespawnStory: no loot when leaving Play Mode (part of 6.5) → 6.5 | Exiting play mode no longer has a chance to drop loot |
| P4350 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Your chosen Starter Weapon should be auto-added to your hotbar when you create a new ch... |
| P4351 | 2016-03-31 | Hotfix notes - April 1 2016 | NOT FOLLOWED | Claims.cs: 'Sites in Play mode ... Not persisted: sites start in Edit' — a restart stops every site's story → 6.56 | Server reset should no longer make creatures stop pathing |
| P4352 | 2016-03-31 | Hotfix notes - April 1 2016 | NOT FOLLOWED | Props.Duplicate copies only the tint → 6.30 | When duplicating a creature, the name, faction and respawn settings should now copy |
| P4353 | 2016-03-31 | Hotfix notes - April 1 2016 | NOT FOLLOWED | Creatures.CavernKinds: cavern Chomper / Slaug / Abomination drop essences only, no resources → 6.16 | Enemies in Chaos Caverns now drop random resource rewards |
| P4354 | 2016-03-31 | Hotfix notes - April 1 2016 | NOT FOLLOWED | Props.SetTint tints any owned prop, spawners included → 6.31 | Creatures placed on your build sites can no longer be tinted |
| P4388 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Pulverizer/extractor type tools and weapons should now look correct on large characters |
| P4440 | 2016-03-31 | Hotfix notes - April 1 2016 | FOLLOWED | client Recipes / PropRecipe tables of the 0.589 build (costs, default-learned flags, Lumen Station list) | The cost of weapon recipes at the Lumen Station has decreased |
| P4499 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | Decorative Weapon Bundle |
| P4524 | 2016-04-12 | Update Notes - April 13, 2016 | NOT FOLLOWED | weapons / armour wear (Durability.Damaged, DurabilitySet) followed; 'Gathering tools have no set and never wear' → 6.53 | Durability has been implemented for weapons, armor, and harvesting tools. These items s... |
| P4525 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | The targeting reticule now displays as a circle |
| P4526 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Underground NPCs should now react faster to your presence |
| P4527 | 2016-04-12 | Update Notes - April 13, 2016 | FOLLOWED | Creatures.Think Chase → Return when the target dies / leash 40 m; Return heals to full | NPCs trained out of position should now reset a short time after they lose aggro |
| P4528 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Various improvements to NPC behaviour |
| P4529 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Improved some of the slaug’s animations and strafing behavior |
| P4530 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Elite Highfall Gladiator should no longer be invisible |
| P4531 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Earth elemental now has a new idle animation so it no longer appears to be just a pile ... |
| P4532 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | NPCs are now smarter at pursuing you around corners |
| P4533 | 2016-04-12 | Update Notes - April 13, 2016 | NOT FOLLOWED | already TASKS 6.5 (no campfire drops; fungus always explodes on a hit, terrain loot only) → 6.5 | Fiery Fungus now drops tree loot on death, with a low chance to occasionally drop rare ... |
| P4534 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Looted recipes now show visibly as purple bundles/particle trails among your loot |
| P4535 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Play Mode is now accessed via a “Play” (or “Pause) button next to the Build Mode button... |
| P4536 | 2016-04-12 | Update Notes - April 13, 2016 | NOT FOLLOWED | Creatures.CavernKinds: cavern Chomper / Slaug / Abomination drop essences only, no resources → 6.16 | Drop rate of rare resources on Chaos Cavern NPCs has been adjusted |
| P4537 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Cozy Campfire tooltip now explains the item’s functionality more clearly |
| P4538 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Cozy Campfire fire effects should now properly persist until the campfire expires |
| P4539 | 2016-04-12 | Update Notes - April 13, 2016 | NOT FOLLOWED | Consumables.UpdateCampfires: 4 %/s heal within 8 m even in combat; Enduring Warmth only at full life when no HoT runs → 6.19 | Enduring Warmth buff can now be refreshed every minute, and will now heal you more reli... |
| P4540 | 2016-04-12 | Update Notes - April 13, 2016 | NOT FOLLOWED | already TASKS 6.1 (ChannelTime 5 s); a hit interrupts (Outposts.Interrupt) followed → 6.1 | Lost Crystals have a new, gloriously glowy interaction effect and animation! You must n... |
| P4557 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Changing cameras on build sites with weapon drawn should no longer result in a potentia... |
| P4588 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Ray gun audio is now on the same audio channel as other weapons |
| P4592 | 2016-04-12 | Update Notes - April 13, 2016 | FOLLOWED | Health.ReviveCost 15 Lumens | Spirit Anchors are no longer an item you need to remember to keep crafting, the “Revive... |
| P4692 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.4 (nearest player in 12 m, no damage table) → 6.4 | Creature aggro has received a significant and much-needed overhaul. This fix includes t... |
| P4693 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | Creatures.Think Chase → Return when the target dies / leash 40 m; Return heals to full | Creatures will now leash back to their home location correctly after making a kill if n... |
| P4694 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.4 (nearest player in 12 m, no damage table) → 6.4 | Creatures will now properly aggro to the highest damage dealer. If they have not been d... |
| P4695 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.4 (nearest player in 12 m, no damage table) → 6.4 | Creatures should no longer run back and forth between two targets without bothering to ... |
| P4696 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT BUILT | no healer behaviour in Creatures.cs → 6.21 | Healers will now pursue enemy targets who move out of their range when not busy healing... |
| P4697 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Various fixes for issues where combat abilities either appeared to be, or actually were... |
| P4698 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Camera should no longer sometimes get stuck in combat state when you equip and unequip ... |
| P4699 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Gunslinger’s Six Shooters no longer triggers additional fx if triggered while running |
| P4700 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Gunslinger’s Six Shooters should be more consistent at spinning your character with the... |
| P4701 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Siphon healing amount should be improved. |
| P4702 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT BUILT | Consumables.LightCampfire has no airborne check (check the client first) → 6.19 | Cozy campfire should no longer be castable while jumping or falling, to reduce the numb... |
| P4703 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Fixed an issue where switching weapons could cause weapons to stop working entirely |
| P4704 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Wisps have been removed as they continued to refuse to fly |
| P4705 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Entering play mode should now cancel flight |
| P4706 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Combat music should now play once a player gets aggro and end when either all creatures... |
| P4707 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Fixed some issues where combat effect icons were not showing up correctly or may have b... |
| P4708 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Barren Grunt should no longer get stuck in backing away and blocking after being hit wi... |
| P4709 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | The Barren Bowman should no longer hold an arrow in his idle animation state |
| P4710 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Highfall Conjuror and Highfall Tempest now have different tints |
| P4711 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Flame Bound Bastion should work more consistently |
| P4712 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Oakmire Stag now activates trigger areas like other creatures |
| P4713 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Ebonguard Attacker now has hands! (He's very happy about this.) |
| P4714 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Ranged attacks by the NovaTech bots are now less overpowered |
| P4715 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | Return restores position and health, not the spawn heading → 6.21 | Creatures who return to their spawn point should now also face in the correct direction |
| P4716 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Corrected an issue where the block effect wasn’t always correctly blocking damage |
| P4717 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT BUILT | Health.Revive sets no grace; Idle aggro takes any living player within 12 m → 6.21 | You should now have a few seconds to recover after reviving, before Creatures attack yo... |
| P4718 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | EditableProperties row 18 player_npc_spawn_delay_flat: default 30, max 300 s (resolves TASKS 6.6 for sites) → 6.6 | Creature respawn time on your Site can now be set up to 5 minutes |
| P4719 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | n/a today: creatures never kill creatures (see 6.20); keep the rule when allies fight → 6.5 | You should not receive loot if an ally or neutral creature kills an enemy creature |
| P4720 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Several equipment sets have been rebalanced: |
| P4721 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Increased the life gained from damage |
| P4728 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Deals more damage |
| P4730 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Has a lower initial energy cost |
| P4731 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Has a higher energy cost over time |
| P4734 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Deals more damage upon activation |
| P4738 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | No longer reduces incoming damage at low life |
| P4747 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Has an increased energy cost |
| P4748 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Grants less maximum energy per stack |
| P4749 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Now does a base amount of damage regardless of the actual energy consumed |
| P4750 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Now only consumes half of stored energy |
| P4751 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Now deals more damage per energy consumed, but less overall damage at high energy reserves |
| P4753 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Deals more damage |
| P4754 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Has a shorter delay after charging ends before other attacks can be used |
| P4760 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Deals less damage |
| P4761 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Now has a slight delay after chaneling ends before other attacks can be used |
| P4763 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Has a higher energy cost over time |
| P4765 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Deals less damage |
| P4767 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Now once per swing hitting a "charged" opponent deals bonus damage in a small area and ... |
| P4768 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Deals more damage |
| P4769 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Has a lower energy cost |
| P4770 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Deals less damage |
| P4834 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | Founder's Pickaxe / founder items: no Founder's Packs on this server (FEATURES: decide) | Founders, rejoice! The Founder’s Pickaxe no longer takes any item damage, ever. |
| P4836 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | The Farseer Resource Sense ability on the Founder’s Pickaxe now has an icon |
| P4848 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Descriptions on some armor items are no longer truncated |
| P4849 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | You should now see a more obvious message alerting you that your gear is damaged, in ad... |
| P4998 | 2016-04-29 | Hotfix notes - 29 April 2016 | client-only / no server rule | client UI / visual / bug fix | You should now be able to swap in and out equipment while in combat (though not while a... |
| P5055 | 2016-05-04 | [Feedback] Story Tools | FOLLOWED | Claims.ArrivalOf + StoryTools Starting Location events (arrive / respawn) | **Starting Location:** Pick the spot that visiting players will start at and respawn at... |
| P5173 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | What weapon/armor combination do you tend to use? |
| P5194 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | Thanks for your feedback :) You mentioned a lot of frustration with the respawn times. |
| P5264 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Molten Rebuke on the Flame Bound Bastion now continues to block but does not counter pr... |
| P5265 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Creatures whose spawners are placed in the air should no longer fail to path and/or attack |
| P5266 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT BUILT | Combat/Statuses.Apply applies every status to any entity; only the armour execute skips Elites (Creatures.cs:669) → 6.22 | “Elite” creatures and very large creatures are now immune to stun, root, freeze, snare,... |
| P5267 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Creatures will no longer make a clapping animation at inappropriate times. |
| P5268 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | creatures have no power attacks or levels in Creatures.cs (nothing to compare) | Creatures should no longer occasionally spawn a level higher than placed, either on bui... |
| P5269 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT BUILT | prop 1220 Proximity Mine: no Link kind (StoryTools.KindOf needs 'Spawner:'), no code in Creatures / Props → 6.29 | Proximity Mines are treated as creatures on your build site, and they have the same fac... |
| P5270 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Luminaries should now look more epic and fancy while reviving each other. |
| P5271 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Ebonguard priestess should now do damage even if she does not move from her spawn location |
| P5272 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Getting hit with electrocution should no longer cause your weapon to stop working |
| P5273 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Secondary channeled abilities should now show a cast bar |
| P5274 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Chompers should no longer occasionally seem invulnerable in caves/ruins |
| P5275 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT BUILT | prop 2890 Target Dummy is a static prop (not a spawner to StoryTools.KindOf); only Achievements.cs:645 knows it → 6.28 | [Updated!] Players can now place a target dummy NPC on their claim to practice combat w... |
| P5339 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Corrected an issue where Extractors don't work on first equip if you had a weapon equip... |
| P5340 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Colonist's Patrol Armor now actually has an appearance and no longer makes your charact... |
| P5406 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Characters who had turned grey should now be back to a normal healthy hue |
| P5416 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | It should no longer be possible for your character animation to get stuck after dying a... |
| P5430 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT BUILT | no HTML stripping in Props.SetText / Chat (check whether the client escapes it) → 6.35 | If you gave broken html to the UI, it would break your ability to receive chat and such... |
| P5436 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT FOLLOWED | already TASKS 6.10 (rewards come from a live capture, Resources/PacketDumps/71-AchievementBase, date unknown) → 6.10 | Several combat achievement armor recipe rewards have been replaced with new armor recip... |
| P5437 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT FOLLOWED | not in TASKS 6.10 yet: H2-Ouch → Reinforced Coat of Crashing Waves (same capture caveat) → 6.10 | The combat achievement H2-Ouch now grants the Reinforced Coat of Crashing Waves recipe ... |
| P5438 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT FOLLOWED | already TASKS 6.10 (rewards come from a live capture, Resources/PacketDumps/71-AchievementBase, date unknown) → 6.10 | The combat achievement Eternal Night now grants the Silent Shadow's Cloak recipe instea... |
| P5439 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT FOLLOWED | already TASKS 6.10 (rewards come from a live capture, Resources/PacketDumps/71-AchievementBase, date unknown) → 6.10 | The combat achievement Praise the Light now grants the Lightbringer's Oath recipe inste... |
| P5440 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT FOLLOWED | already TASKS 6.10 (rewards come from a live capture, Resources/PacketDumps/71-AchievementBase, date unknown) → 6.10 | The combat achievement Assassin's Choice now grants the Desperado's Deadly Poncho recip... |
| P5441 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT FOLLOWED | already TASKS 6.10 (rewards come from a live capture, Resources/PacketDumps/71-AchievementBase, date unknown) → 6.10 | The combat achievement 1.21 ...Something now grants the Overcharged Recon Gear recipe i... |
| P5449 | 2016-05-13 | Week End Wrap Up - 13 May 2016 | client-only / no server rule | feedback thread / announcement / chatter | This week we had fun finding the secret unicorn level in the Unicorn Glue Company - gre... |
| P5474 | 2016-05-23 | [Feedback] Excessively long lo.. | client-only / no server rule | feedback thread / announcement / chatter | (Unfortunately we have been undergoing some DDoS attacks in the past few days that are ... |
| P5550 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | creatures have no power attacks or levels in Creatures.cs (nothing to compare) | NPCs are less likely to use power attacks (such as stuns) |
| P5551 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Melee weapons grant slightly more passive damage resistance to their wielders. |
| P5552 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Damage calculations have been adjusted to better accommodate heavy and medium armor, me... |
| P5553 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | All weapons have undergone a damage and energy cost balance pass: |
| P5554 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Damage Changes |
| P5555 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Increased Wanderer's Blazing Bow damage |
| P5556 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Increased Supercooled Power Cannon damage |
| P5557 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Increased Falchion of the Inner Spark - Basic Attack damage |
| P5558 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Decreased Falchion of the Inner Spark - Power Attack damage |
| P5559 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Increased Gunslinger's Six-Shooter's damage |
| P5560 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Decreased Poison Drenched Daggers damage |
| P5561 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Decreased Wand of Boundless Light damage |
| P5562 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Increased Armament of Unrelenting Vengeance damage |
| P5563 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Decreased Flame Bound Bastion - Basic Attack damage |
| P5564 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Increased Flame Bound Bastion - Power Attack damage |
| P5565 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Increased Staff of Frozen Sorrow damage |
| P5566 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Increased Overcharged Recon Blaster - Basic Attack damage |
| P5567 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Decreased Overcharged Recon Blaster - Power Attack damage |
| P5568 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Decreased Armored Brawler's Blade damage |
| P5569 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Decreased Diabolical Scientist's Raygun damage |
| P5570 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Increased Avenging Champion's Exaltation damage |
| P5571 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Decreased Spear of Piercing Judgement damage |
| P5572 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Energy Cost Changes |
| P5573 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Increased energy cost of Wanderer's Blazing Bow - Power Attack |
| P5574 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Decreased energy cost over time of Supercooled Power Cannon - Power Attack |
| P5575 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Decreased energy cost of Falchion of the Inner Spark - Power Attack |
| P5576 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Decreased energy cost of the first use of Gunslinger's Six-Shooter's - Power Attack |
| P5577 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | - Increased energy cost of the second use of Gunslinger's Six-Shooter's - Power Attack |
| P5578 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Decreased energy cost of Poison Drenched Daggers - Power Attack |
| P5579 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Decreased energy cost of Wand of Boundless Light - Power Attack |
| P5580 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Decreased energy cost over time of Armament of Unrelenting Vengeance - Power Attack |
| P5581 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Decreased energy cost of Staff of Frozen Sorrow - Power Attack |
| P5582 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Decreased energy cost over time of Overcharged Recon Blaster - Power Attack |
| P5583 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Increased energy cost of Armored Brawler's Blade - Power Attack |
| P5584 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Decreased energy cost of Diabolical Scientist's Raygun - Power Attack |
| P5585 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Decreased initial energy cost of Avenging Champion's Exaltation - Power Attack |
| P5586 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Increased energy cost over time of Avenging Champion's Exaltation - Power Attack |
| P5587 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Decreased energy cost of Spear of Piercing Judgement - Power Attack |
| P5588 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Additional Changes |
| P5589 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Wanderer's Blazing Bow - Basic Attack has a slightly longer delay before firing, but a ... |
| P5590 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Wanderer's Blazing Bow - Power Attack has a slightly shorter delay and cooldown |
| P5591 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Falchion of the Inner Spark - Basic Attack now always deals bonus damage on the second ... |
| P5592 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Poison Drenched Daggers - Basic Attack now hits all opponents near you, rather than jus... |
| P5593 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Poison Drenched Daggers - Power Attack now hits opponents in a larger area |
| P5594 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Diabolical Scientist's Raygun - Basic Attack projectiles have a lower arc and hit a lar... |
| P5595 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Overcharged Recon Blaster - Power Attack range of the first shot now matches the range ... |
| P5596 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Diabolical Scientist's Raygun - Power Attack now lets you move slighly faster while cha... |
| P5597 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | We have also added some new weapons! Recipes for these will drop, or can be obtained vi... |
| P5598 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Frenzied Slayer’s Battle-Axe: 2-Handed Fire Axe |
| P5599 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Basic Attack – Fiery Cleave: Swing your axe at all opponents in front of you, gaining F... |
| P5600 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Power Attack – Conflagration: Charge a powerful uppercut that deals damage to all oppon... |
| P5601 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Basic Attack – Fracture: Smash the ground in front of you dealing damage to all opponen... |
| P5602 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Power Attack – Frozen Domain: Charge up frigid energy and release it with a powerful st... |
| P5603 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Electrified Assault Axe: 1-Handed Lightning Axe |
| P5604 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Basic Attack – Overcharge: Charge your axe to damage all opponents in front of you. Whe... |
| P5605 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Power Attack – Rolling Thunder: Execute a series of leaping attacks, dealing greater da... |
| P5606 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Unflinching Embodiment of Might: 1-Handed Earth Club |
| P5607 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Basic Attack – Decimate: Slam your mace down to damage any opponents in front of you. H... |
| P5608 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Power Attack – Obliteration Turn: Spin your mace in a circle hitting all opponents arou... |
| P5609 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Blight-Warden's Pain: Affliction Axe & Shield |
| P5610 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Basic Attack – Enfeeble: Swing your axe to strike at all opponents in front of you. Opp... |
| P5611 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Power Attack – Corrupting Revenge: Channel a blocking stance that prevents all damage f... |
| P5612 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Crusader's Courage: 1-Handed Radiance Sword |
| P5613 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Basic Attack – Sanctify: Swing your sword to damage all opponents in front of you. If y... |
| P5614 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Power Attack – Righteous Swing: Charge a powerful swing that deals damage to all oppone... |
| P5615 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Basic Attack – Cursed Bolt: Fire a cursed arrow that damages the first opponent hit. Da... |
| P5616 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Power Attack – Agonizing Volley: Charge up a massive volley of arrows that deal damage ... |
| P5617 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Staff of the Lightning Caller: Lightning Staff |
| P5618 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Basic Attack – Crackling Orb: Charge and launch an orb of crackling energy that damages... |
| P5619 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Power Attack – Static Bomb: Create an energized explosion at a distant location in fron... |

### other (187)

| Id | Date | Thread | Class | Evidence | Text |
|---|---|---|---|---|---|
| P4138 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Tags for listing your site in the Gallery are now selected from preset drop-downs, rath... |
| P4139 | 2016-03-21 | Landmark Update Notes - March 21 | NOT BUILT | Store.Gallery lists every site ('TODO: only published sites'); free-text search only; no comments, no leaderboard / rising stars, no Play Mode filter → 6.43 | Gallery entries now include a comments area where you can leave a comment about the site! |
| P4142 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Build mode has four tabs, which can also be toggled between using Shift-1, Shift-2, Shi... |
| P4153 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | The building tools are no longer inventory items that need to be placed on a hotkey bar... |
| P4181 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Inventory.DefaultBagSize 40, MaxBagSize 100 | Pre-update Inventory size: 40, max 100 |
| P4182 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Inventory.DefaultBagSize 40, MaxBagSize 100 | New Inventory size: unchanged |
| P4185 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | materials tab capacity per resource from the client InventoryDefinitions (Inventory.ResourceSlots) | Pre- update Collection size: 900,000 |
| P4186 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | materials tab capacity per resource from the client InventoryDefinitions (Inventory.ResourceSlots) | New Collection size: 24,000,000 |
| P4231 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | We have removed Sifters. |
| P4267 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | There are definitely still some pathing oddities to be worked out, we are looking into ... |
| P4278 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | If you receive an error saying "XINPUT_3.dll is missing" you may be missing some Direct... |
| P4294 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | See also: the known issues in the post above this. |
| P4305 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Update on some people missing inventory slots (has also been edited in the original post): |
| P4319 | 2016-03-25 | Week End Wrap Up - March 25, 2.. | client-only / no server rule | feedback thread / announcement / chatter | Hotfix Notes (https://forums.daybreakgames.com/landmark/index.php?threads/hotfix-notes-... |
| P4320 | 2016-03-25 | Week End Wrap Up - March 25, 2.. | client-only / no server rule | feedback thread / announcement / chatter | Wipe Q&A (https://forums.daybreakgames.com/landmark/index.php?threads/landmark-wipe-q-a... |
| P4324 | 2016-03-25 | Week End Wrap Up - March 25, 2.. | client-only / no server rule | feedback thread / announcement / chatter | [image: [IMG]] (https://www.landmarkthegame.com/authenticated/credits-optin)The Communi... |
| P4326 | 2016-03-25 | Week End Wrap Up - March 25, 2.. | client-only / no server rule | feedback thread / announcement / chatter | Finally, a quick call out of some of the new and/or higher priority issues heading into... |
| P4338 | 2016-03-25 | Week End Wrap Up - March 25, 2.. | client-only / no server rule | feedback thread / announcement / chatter | Enjoy your weekend, and see you in Landmark! :) |
| P4339 | 2016-03-25 | Week End Wrap Up - March 25, 2.. | client-only / no server rule | feedback thread / announcement / chatter | We have a ton of bug fixes coming to you this week, and we're currently doing a final t... |
| P4445 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Collision has been removed from small plants |
| P4455 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Fixed numerous typos |
| P4458 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Yes, but I don't think it made it in in time for this update. |
| P4461 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Edit: See JAugustine's response about 2 posts down from this. Edited the notes to clarify. |
| P4467 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | We forgot to include one important piece of information! Which has now been added: |
| P4473 | 2016-04-01 | Week End Wrap Up - April 1, 2016 | client-only / no server rule | feedback thread / announcement / chatter | The latest Week End Wrap Up courtesy of Murren can be found here (https://forums.daybre... |
| P4494 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | Aqueous Mote Bundle |
| P4495 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | Bathroom Bundle |
| P4497 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | Brilliant Mote Bundle |
| P4498 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | Chain and Brace Bundle |
| P4500 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | Fire Mote Bundle |
| P4501 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | Food and Drink Bundle |
| P4502 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | Geyser Mote Bundle |
| P4503 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | Iron Chandelier Bundle |
| P4505 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | Leyline Mote Bundle |
| P4507 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | Living Room Bundle |
| P4508 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | Pillow Bundle (Blue) |
| P4509 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | Pillow Bundle (Green) |
| P4510 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | Purple Fire Mote Bundle |
| P4511 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | Red Fire Mote Bundle |
| P4512 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | Rope Bundle |
| P4513 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | Round Cushion Bundle |
| P4514 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | Shadow Mote Bundle |
| P4515 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | Stemware, Mugs, and Steins Bundle |
| P4516 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | Tech Gadget Bundle |
| P4517 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | Volcanic Mote Bundle |
| P4518 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | Wind and Water Mote Bundle |
| P4520 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | Yellow Fire Mote Bundle |
| P4521 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | This update is scheduled for 7am pacific time on Wednesday April 13th. Expected down ti... |
| P4585 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Lunar Treads are now working once again |
| P4586 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Hotfoot buff now works appropriately with Sprint |
| P4623 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | NovaTech Velocitizer now has the correct icon displayed in the Replicator list |
| P4635 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Corrected multiple typos and grammar errors |
| P4638 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | known-issue report (client bug) | Still working on a number of tools and blending issues. |
| P4645 | 2016-04-15 | Week End Wrap Up - April 15, 2.. | client-only / no server rule | feedback thread / announcement / chatter | This week we had another big update, with a couple new features and a whooole lotta bug... |
| P4646 | 2016-04-15 | Week End Wrap Up - April 15, 2.. | client-only / no server rule | feedback thread / announcement / chatter | We also briefly introduced a new bug where the tool cursor could get stuck, but we got ... |
| P4647 | 2016-04-15 | Week End Wrap Up - April 15, 2.. | client-only / no server rule | feedback thread / announcement / chatter | We have also seen a few reports of another bug that we're having trouble getting repro ... |
| P4652 | 2016-04-15 | Week End Wrap Up - April 15, 2.. | client-only / no server rule | feedback thread / announcement / chatter | Happy weekend, we'll see you in Landmark! |
| P4654 | 2016-04-16 | Loading screen tips being upda.. | client-only / no server rule | feedback thread / announcement / chatter | For reference here are the old outdated ones - at a minimum we'll have updated versions... |
| P4656 | 2016-04-16 | Loading screen tips being upda.. | client-only / no server rule | feedback thread / announcement / chatter | Right-Clicking a tool in your Inventory will send it directly to your hotbar for use. |
| P4661 | 2016-04-16 | Loading screen tips being upda.. | client-only / no server rule | feedback thread / announcement / chatter | Use CTRL+Z to Undo, and CTRL+Y to redo. |
| P4665 | 2016-04-16 | Loading screen tips being upda.. | client-only / no server rule | feedback thread / announcement / chatter | Thanks for all the suggestions! Added many of these. (Though no advertisements for the ... |
| P4671 | 2016-04-21 | Showcase updates | client-only / no server rule | feedback thread / announcement / chatter | There are some additional changes on the game side that will follow with our next game ... |
| P4686 | 2016-04-22 | Week End Wrap Up - April 22, 2.. | NOT BUILT | Store.Gallery lists every site ('TODO: only published sites'); free-text search only; no comments, no leaderboard / rising stars, no Play Mode filter → 6.43 | You can now leave comments on builds in the gallery! |
| P4690 | 2016-04-22 | Week End Wrap Up - April 22, 2.. | client-only / no server rule | feedback thread / announcement / chatter | Hope everyone has a great weekend, and we'll see you in game! :cool: |
| P4722 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Now "condemns" opponents when fully charged |
| P4723 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | No longer "torments" opponents when fully charged |
| P4725 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Now "torments" opponents that are "condemned" |
| P4726 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | No longer "condemns" opponents that are "tormented" |
| P4729 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Can be reactivated faster |
| P4732 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | No longer requires low life to activate |
| P4733 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | No longer requires opponent to be "tormented" to activate |
| P4735 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Now significantly increases siphon strength temporarily upon activation |
| P4736 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Cannot re-activate until siphon buff has expired |
| P4737 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | No longer benefits from siphon |
| P4739 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | No longer "crushes" opponents |
| P4740 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Now makes opponents vulnerable to being "crushed" by "trick shot" for a short time |
| P4742 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Now "crushes" opponents that have been recently hit by "unload" |
| P4743 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Now grants "harden" on second use after landing a hit |
| P4744 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Has increased dash distance |
| P4745 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Fires projectiles that pass through multiple opponents |
| P4755 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Has a longer min and max charge time |
| P4757 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Now freezes targets faster |
| P4758 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Fires projectiles that pass through multiple opponents |
| P4764 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Hits opponents in a much larger area |
| P4766 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Now inflicts "charged" on all opponents hit for a short time |
| P4771 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.7 → 6.7 | Items now take nearly twice as long to reach 0% condition |
| P4772 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.7 → 6.7 | All item repair costs have been decreased |
| P4813 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Grappling hook should no longer fire at any time while build mode is active |
| P4825 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Paint tool should no longer paint through glass |
| P4829 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | The area tool now has a unique cursor |
| P4830 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Tinting now has a unique cursor |
| P4838 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Western Getup (green) is now green. |
| P4847 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | It should no longer be possible to get your Breeze Boots or Lunar Treads stuck on |
| P4911 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | Emotes.cs (FEATURES: emotes ✅) | **Stances** (/stand to end) /dance /kneel /sit /lie /skip /sneak /drunk |
| P4912 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | Emotes.cs (FEATURES: emotes ✅) | **Emotions** (/calm to end) /angry /calm /happy /sad |
| P4913 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | Emotes.cs (FEATURES: emotes ✅) | **Looping action** (/stop to end) /talking |
| P4959 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Fixed many typos |
| P4963 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Escape will now cancel the Placing Dynamite reticle |
| P4966 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Updated the grappling hook icon |
| P4976 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Raw gemstones have been changed to non-blending |
| P4977 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | known-issue report (client bug) | *There are many other known issues in the dgcissuetracker, but these are the most relev... |
| P4981 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | known-issue report (client bug) | Paint tool can still sometimes paint through solid walls if water is behind the wall. O... |
| P4988 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | known-issue report (client bug) | Yes, we are continuing to work on blending changes |
| P4990 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Oops, don't know how that one got lost. Yes, still exists, added it back into the list. |
| P4991 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Consider investing in a grappling hook, which is the intended gameplay. :D The evac was... |
| P5001 | 2016-04-29 | Hotfix notes - 29 April 2016 | client-only / no server rule | client UI / visual / bug fix | (Sorry grey-skinned folks - we did find the cause of this bug but there wasn't time to ... |
| P5005 | 2016-04-30 | Week End Wrap Up - April 30 | client-only / no server rule | feedback thread / announcement / chatter | Luminary and film maker Jinkys has also released a new Landmark video themed after the ... |
| P5009 | 2016-05-01 | wiki: Game Updates 2016 | client-only / no server rule | feedback thread / announcement / chatter | This update was primarily bug fixes (not listed here). |
| P5021 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | Avoid anything so advanced it would be incomprehensible to a brand new player (crazy of... |
| P5023 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | old Arcs starter sets (replaced by the nested arc sets above) |
| P5024 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | old pillars sets (can be replaced by the nested arc sets above) |
| P5026 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | old angles set (doesn't seem heavily used, can be shared in swap meets) |
| P5027 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | old triangles set (as above) |
| P5031 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | Thoughts? Are there any of the old ones that you really DO feel are valuable to everyon... |
| P5036 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | And, created a set of pillars from the arcs. This is what results: |
| P5044 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | odd nested arcs |
| P5045 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | odd nested arches |
| P5046 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | even nested arcs |
| P5047 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | even nested arches |
| P5048 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | angled lines |
| P5063 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | What aspects are confusing or unintuitive? |
| P5065 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | What changes to their functionality would make them easier to use and understand? |
| P5066 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Anything else you want to tell us about them! |
| P5070 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | This is all great feedback. Thanks for taking the time to write this all up. There are ... |
| P5084 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Duplicate |
| P5085 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Send to Inventory |
| P5089 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Deleting is at the very bottom. |
| P5095 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Duplicate |
| P5096 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Send to Inventory |
| P5123 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Activate Automatically (boolean) |
| P5144 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Here's an example of the alternate style, which is what I'm leaning towards atm. |
| P5168 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | Thanks for taking the time to share your thoughts and opinions with us, as always! And ... |
| P5186 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | Thanks for the feedback, Nanny Oggins :) We definitely want to get perspective from all... |
| P5187 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | I wanted to give some follow-up info on these two points in particular. |
| P5190 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | I'm really hoping to get this sort of feedback from this thread, but it's not useful if... |
| P5199 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | Thanks for your feedback :) Some good news about a couple of the things you mentioned: |
| P5200 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | FOLLOWED | OreVeins/VeinGenerator use the 0.589 (4 Jun 2016) vein data: DENSITY_IN_CAVES, counts per pool | Mineral veins will be increased in the underground soon. We heard you all loud and clea... |
| P5204 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | Thanks for the feedback. Two quick notes on this part in particular: |
| P5213 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | Thanks for following up on that so quickly :) Pass along my thanks to you son too! |
| P5217 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | Here are some questions to get you started: |
| P5221 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | Any other feedback you have! |
| P5224 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | There are technical solutions to this problem, but none of them or easy or quick, so it... |
| P5226 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | However, them still fighting you even after changing to Neutral is definitely a bug. I'... |
| P5228 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | I'd agree with that, but want to make sure I understand your opinion. Thanks! |
| P5233 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | I addressed some of this in an earlier post. But there's actually a ton of technical hu... |
| P5235 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | I addressed some of this in an earlier post. But there's actually a ton of technical hu... |
| P5239 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | Pause and Reset |
| P5241 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | Pause and Reset |
| P5250 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | Those terms are often used interchangeably in other products and it's not immediately c... |
| P5422 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | known-issue report (client bug) | IF YOU KNOW OF A BUG THAT ISN'T IN THE ISSUE TRACKER, PLEASE MAKE SURE WE KNOW ABOUT IT... |
| P5426 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | known-issue report (client bug) | Yes, we are continuing to work on blending changes |
| P5435 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Another update that was missed from the original post and has now been added: |
| P5443 | 2016-05-13 | Week End Wrap Up - 13 May 2016 | client-only / no server rule | feedback thread / announcement / chatter | Two things snuck into the update that weren't in the first draft of the notes, so if yo... |
| P5448 | 2016-05-13 | Week End Wrap Up - 13 May 2016 | client-only / no server rule | feedback thread / announcement / chatter | Here's a few of the fun and amazing things we visited in Landmark this week: |
| P5450 | 2016-05-13 | Week End Wrap Up - 13 May 2016 | client-only / no server rule | feedback thread / announcement / chatter | We also enjoyed seeing Dalvasha's Landmark version of "Befallen" in this lovely video, ... |
| P5452 | 2016-05-13 | Week End Wrap Up - 13 May 2016 | client-only / no server rule | feedback thread / announcement / chatter | Have you seen anything fun or interesting in game? Be sure to let us know so we can che... |
| P5456 | 2016-05-20 | Week End Wrap Up - 20 May 2016 | client-only / no server rule | feedback thread / announcement / chatter | This week were kind of up to our necks in bugs, but we did enjoy seeing Taelaa's update... |
| P5457 | 2016-05-20 | Week End Wrap Up - 20 May 2016 | client-only / no server rule | feedback thread / announcement / chatter | [image: [IMG]] *(should we be worried that a new player tutorial includes a guillotine?... |
| P5462 | 2016-05-23 | [Feedback] Excessively long lo.. | client-only / no server rule | feedback thread / announcement / chatter | Hardware specs (CPU, RAM, video card, hard drive brand/type) |
| P5463 | 2016-05-23 | [Feedback] Excessively long lo.. | client-only / no server rule | feedback thread / announcement / chatter | What OS you're using, and if 32 or 64 bit |
| P5464 | 2016-05-23 | [Feedback] Excessively long lo.. | client-only / no server rule | feedback thread / announcement / chatter | What other applications do you usually have running? (Including antivirus) |
| P5468 | 2016-05-23 | [Feedback] Excessively long lo.. | client-only / no server rule | feedback thread / announcement / chatter | We believe we've identified and fixed a number of issues, but as we can't reproduce the... |
| P5471 | 2016-05-23 | [Feedback] Excessively long lo.. | client-only / no server rule | feedback thread / announcement / chatter | Click here (http://goo.gl/forms/kPKZF1XEPDAoutQB3) |
| P5473 | 2016-05-23 | [Feedback] Excessively long lo.. | client-only / no server rule | feedback thread / announcement / chatter | We pushed out a potential fix last night, please let us know if you're seeing any chang... |
| P5648 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Corrected various typos and punctuation issues |
| P5650 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Still working on convincing enemies that it's considered bad form to shoot you through ... |
| P5660 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Frost Lord's Fury |
| P5663 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Crusader's Courage |
| P5664 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Night Herald's Mercy |
| P5665 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | there are many other known issues, but these are currently the highest priority and mos... |
| P5667 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Two quick notes to address common concerns I've seen in here: |
| P5671 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Doh! Updated notes accordingly. We're investigating what went astray! |
| P5673 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | They do need to be open side by side so you can drag from one to the other. |
| P5675 | 2016-05-27 | Week End Wrap Up - 27 May 2016 | client-only / no server rule | feedback thread / announcement / chatter | The big news this week, in case you haven't seen it yet, is the announcement of our lau... |
| P5686 | 2016-06-02 | Hotfix Notes - 2 June 2016 | client-only / no server rule | client UI / visual / bug fix | https://dgcissuetracker.com/browse/LM-782 (https://dgcissuetracker.com/browse/LM-782) |
| P5687 | 2016-06-02 | Hotfix Notes - 2 June 2016 | client-only / no server rule | client UI / visual / bug fix | https://dgcissuetracker.com/browse/LM-924 (https://dgcissuetracker.com/browse/LM-924) |
| P5689 | 2016-06-02 | Hotfix Notes - 2 June 2016 | client-only / no server rule | client UI / visual / bug fix | Seems our ninja fix was a little too ninja, and is still invisible! :confused: We have ... |
| P5690 | 2016-06-02 | Hotfix Notes - 2 June 2016 | client-only / no server rule | client UI / visual / bug fix | We made the gems non-blending as a partial improvement while working on this bigger ble... |
| P5691 | 2016-06-02 | Hotfix Notes - 2 June 2016 | client-only / no server rule | client UI / visual / bug fix | (Edit: and by "now" I mean it's changed internally now, you'll see the change in the fi... |
| P5695 | 2016-06-02 | Hotfix Notes - 2 June 2016 | client-only / no server rule | client UI / visual / bug fix | what is it doing that it shouldn't? |
| P5706 | 2016-06-03 | Week End Wrap Up - 3 June 2016 | client-only / no server rule | feedback thread / announcement / chatter | As you might expect, this week we were super busy getting ready for next week's launch.... |
| P5707 | 2016-06-03 | Week End Wrap Up - 3 June 2016 | client-only / no server rule | feedback thread / announcement / chatter | We also announced the date and time for our launch livestream (https://forums.daybreakg... |
| P5708 | 2016-06-03 | Week End Wrap Up - 3 June 2016 | client-only / no server rule | feedback thread / announcement / chatter | This is covered in the Q&A but just to make sure it's as clear as possible: |
| P5712 | 2016-06-03 | Week End Wrap Up - 3 June 2016 | client-only / no server rule | feedback thread / announcement / chatter | on Thursday, we'll do a livestream, talk about launch and some of the new things in gam... |
| P5716 | 2016-06-03 | Week End Wrap Up - 3 June 2016 | client-only / no server rule | feedback thread / announcement / chatter | no, of course there are no further wipes expected once the game is live! Beta was our p... |
| P5718 | 2016-06-03 | Week End Wrap Up - 3 June 2016 | client-only / no server rule | feedback thread / announcement / chatter | We'll post some update notes next week calling out late-breaking bug fixes and changes ... |
| P5721 | 2016-06-03 | Week End Wrap Up - 3 June 2016 | client-only / no server rule | feedback thread / announcement / chatter | Only Trailblazer level founders get early access. As of noon today the early access wil... |

### ui (157)

| Id | Date | Thread | Class | Evidence | Text |
|---|---|---|---|---|---|
| P4160 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | At the top right of the screen you’ll notice new camera controls, as well as the button... |
| P4161 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | For the Orbit Camera, holding down Ctrl-E allows you to move the orbit point. |
| P4162 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | These camera modes are intended for use while building on a site.Normal third person ca... |
| P4254 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | Several UI buttons sometimes need to be clicked twice in order to work. |
| P4270 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | [Fixed] It's not currently possible to rearrange the building tools order in the new bu... |
| P4303 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | The same two options are available :) This is just a different way of presenting the si... |
| P4318 | 2016-03-25 | Week End Wrap Up - March 25, 2.. | client-only / no server rule | feedback thread / announcement / chatter | Update Notes (https://forums.daybreakgames.com/landmark/index.php?threads/landmark-upda... |
| P4391 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Expanding and minimizing categories in the crafting window could sometimes result in an... |
| P4392 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | When mousing over the crafting window the mouse pointer is now the correct shape |
| P4393 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Background color of Purple, Black, and White textile icons has been corrected |
| P4394 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Free props should now be included when "Can Build" is ticked |
| P4395 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Category names in the prop list now have a blue box around their name |
| P4396 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Play mode buttons in build site header and build UI now sync, so both will change which... |
| P4397 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Map header option now reads "Show Buildable Land" |
| P4398 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Map filter options have been updated to reflect current map features |
| P4399 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Opening and closing the map no longer causes the Boot and Grapple icons to disappear fr... |
| P4400 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | The portal spire crystal now takes you directly to the worlds list. One less mouse click! |
| P4401 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | The Travel menu position is now saved when closed |
| P4402 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | "Portal" is now called "Spire" in the travel menu, and should now always work |
| P4403 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | The Character Sheet window can now be moved |
| P4404 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Right clicking in the friends list to send a ‘tell’ to a ‘friend’ should now put the mo... |
| P4405 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | In the friends list, the teleport to friend icon next to your friend's name now matches... |
| P4406 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Clicking or mousing over the name of a friend in the friends list no longer changes the... |
| P4407 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Site management section on site limits should now correctly show how many possible buil... |
| P4408 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Site Management window no longer allows you to enter a competition without a build site... |
| P4409 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | The site name text box in the Site Management window no longer accepts multiple lines |
| P4410 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Fixed some issues with contest submission tags |
| P4411 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Guild chat log should no longer repeat lines |
| P4412 | 2016-03-31 | Hotfix notes - April 1 2016 | FOLLOWED | Guilds permission ModifyRanks; MemberGameData LastOnline | Officers can now be given access to modify guild ranks |
| P4413 | 2016-03-31 | Hotfix notes - April 1 2016 | FOLLOWED | ItemUses SetVoxelStorage (VoxelStorage.IncreasedSize) | You should now receive chat notification text when design storage is increased |
| P4414 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Cursor position should no longer be off by one character when going to old messages in ... |
| P4415 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Chat window should no longer lose tabs when you use the side arrow to scroll between them |
| P4416 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Chat Window timestamp no longer includes the date, just the time |
| P4417 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Newly created designs should no longer have no icon |
| P4418 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Design/paste settings should now default to 'paste with surrounding empty air" off and ... |
| P4419 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Tweak Mode UI window should no longer remain on-screen after turning Tweak Mode off |
| P4420 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | mail / window texts are the client's (Claim.UpkeepMail*) | The clickable area for the Tweak Mode window should now match the actual window size |
| P4421 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Watch List: you can now click on objectives to view details of the Goal |
| P4422 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Watch List background opacity should now fade when not in focus |
| P4423 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Edit/Play mode UI button should no longer need to be clicked twice to change mode |
| P4424 | 2016-03-31 | Hotfix notes - April 1 2016 | FOLLOWED | Claims ResultPioneer 28 refuses player sites on Pioneer lands | Clarified the error text for trying to place a build site on a Pioneer island |
| P4425 | 2016-03-31 | Hotfix notes - April 1 2016 | NOT BUILT | Spire.Covers only used by Gathering.Ground and Teleport; Abilities.Mine / Building / TerrainEdits.MayApply allow digging and building at the Spire; client text 560944 unused → 6.15 | If you try to mine beside the spire you should now get clearer feedback |
| P4426 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Hotkey icons should no longer duplicate or overlap when creating a new character after ... |
| P4427 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Performance stats (brought up with Alt-F) should no longer overlap game menu |
| P4428 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | If you lock the hotkey bar, it should now stay locked after logout/login |
| P4429 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | The game option setting to remove the boot icon now actually does remove the boot icon |
| P4430 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Clicking on the + beside your Lumen Shard count now opens the Marketplace (although we ... |
| P4523 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Build Mode UI can now be fully collapsed, using the down arrow at the upper right of th... |
| P4598 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Using mousewheel while hovering over chat should now scroll the text |
| P4599 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Corrected messaging when you leave a custom chat channel |
| P4600 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Social media screenshot previews should once again correctly display in the Social window |
| P4601 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Switching back and forth between the Replicator and Lumen Station should no longer caus... |
| P4602 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Items in overflow should no longer incorrectly display "Expires in 0 seconds" in the to... |
| P4603 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Tooltip for boots has been removed from displaying in the main screen (you can still ex... |
| P4604 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Loot tab no longer displays common resources (e.g. stone, dirt, etc) and now remembers ... |
| P4605 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Selecting “Teleport to friend” from the Travel menu no longer toggles the friend window... |
| P4606 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Party window can now be moved and the default position no longer overlaps loot notifica... |
| P4607 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Shortcut key for Designs in the main menu now correctly says “L” |
| P4608 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | The main “Chat” tab now shows all channel, guild, group, and tell chat (they still also... |
| P4609 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Chat Tabs should no longer rearrange order after zoning |
| P4610 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Chat window is no longer available at character select if you hit Enter |
| P4611 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Some UI Windows no longer become inaccessible after logging out and in again immediately |
| P4612 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Zoning with a tweak mode window up no longer causes issues with the toolbar hotkeys |
| P4613 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | It should no longer be possible for the Revive menu to occasionally get stuck on screen |
| P4614 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Description text on inventory item tooltips should now display more reliably |
| P4615 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | UI windows can no longer be clicked through |
| P4616 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Friends list should be collapsed by default on first login |
| P4617 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Tweak mode window now has some more friendly text explaining what it is |
| P4618 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Chat window now has focus when game loads |
| P4619 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Help windows now pop up the first time you perform certain actions (e.g. opening Build ... |
| P4620 | 2016-04-12 | Update Notes - April 13, 2016 | FOLLOWED | Guilds permission ModifyRanks; MemberGameData LastOnline | Guild window now shows members’ last online date, as well as being somewhat prettier lo... |
| P4621 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Friend window should now get focus when it's opened |
| P4639 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | We have seen this happen occasionally but cannot figure out steps to make it happen, wh... |
| P4666 | 2016-04-16 | Loading screen tips being upda.. | client-only / no server rule | feedback thread / announcement / chatter | Unfortunately, these are simply text strings, so any keybinding references will refer t... |
| P4676 | 2016-04-21 | Showcase updates | client-only / no server rule | feedback thread / announcement / chatter | If you could help spread this message to folks in game who may not have read the forums... |
| P4678 | 2016-04-21 | Showcase updates | client-only / no server rule | feedback thread / announcement / chatter | No, I do not see bug reports for either of those on the issue tracker, so if you could ... |
| P4923 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | We made a speculative fix for an issue where performance could degrade during to extend... |
| P4924 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | UI now has more consistent open/close/click sounds |
| P4925 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Build mode UI now displays a tooltip for materials that should indicate what resources ... |
| P4926 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Mousewheel now scrolls the chat window in the direction you’d expect |
| P4927 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Updated links to customer service page to direct to their new locations |
| P4928 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Hotbar and related HUD elements are now centered |
| P4929 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Scrolling through the goals watchlist no longer also changes the third person camera di... |
| P4930 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | When you receive lumens the lumen count displayed in the UI will now immediately update |
| P4931 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | There is no longer a slot for snow in the collection tab. Ice is the resource harvested... |
| P4932 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Ctrl+mousewheel no longer zooms your camera view while trying to adjust height during b... |
| P4933 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Chat should not create a new tab if you try to send a tell to a character who isn’t online |
| P4934 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Guild window ‘last on’ column is now wider |
| P4935 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | ‘Enter competition’ button now correctly updates once you enter a competition |
| P4936 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | ‘Create a guild’ UI window should now be clearer |
| P4937 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Chat window now has additional padding on the right side |
| P4938 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Clicking twice on an entry in the replicator no longer selects the entry below it |
| P4939 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Fixed an issue where screenshots on Designs might not show up |
| P4940 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Creatures should now be using the correct icons in the lumen station |
| P4941 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Tundra berry bush props should now have more colorful berries |
| P4942 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | You can now open mail directly from the notifications window |
| P4943 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Site management UI now better matches the UI of other windows |
| P4944 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Starting Location prop icon has been updated |
| P4945 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Esc menu has been moved into the Landmark menu, which is now what will open when you pr... |
| P4946 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT BUILT | Spire.cs builds one Spire on the home continent; Spire.TravelTo re-zones there from other landscapes → 6.49 | Evac to Safety is no longer available as the travel menu now allows travel to the neare... |
| P4947 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | F key can now trigger ‘on click’ interactions with Creatures |
| P4948 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | The arrow buttons no longer cause the chat tabs to wrap around |
| P4949 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | The default icon for designs that are missing a screenshot has been updated |
| P4950 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | General is no longer a separate chat tab, it’s simply part of the default chat window |
| P4951 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Loading screen should not disappear until loading is fully complete |
| P4952 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Corrected text on site placement UI to reference build sites rather than claims |
| P4953 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Your position in the Lumen station should no longer jump around as you purchase items |
| P4954 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | BuildingClipboard design paste checks only build permission → 6.45 | When you try to place a player studio design you own on a build site that you do NOT ow... |
| P4955 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Categories on the lumen station should now be much more consistent. Prop unlocks should... |
| P4956 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | You can now scroll through the description of an item selected in a crafting station |
| P4961 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Updated the category icons that displayed on the tooltip of many icons. |
| P4974 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Key hints in the UI should now be updated if you have changed your key bindings |
| P5080 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Right now, the Menu options follow this consistent order: |
| P5145 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | (Notice that "Open Settings" is above "Move" instead of below it) |
| P5147 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | No Settings |
| P5160 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | Here are some specific questions to help get you started: |
| P5238 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | Yeah, we definitely want it to be clear what each option does. Do you think the current... |
| P5240 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | Yeah, we definitely want it to be clear what each option does. Do you think the current... |
| P5249 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | I actually think having two options called "Reset" and "Restart" could be pretty confus... |
| P5394 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | More than six buff icons should no longer force the seventh to display behind the healt... |
| P5395 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Corrected a slowness issue when submitting a build site to a competition |
| P5396 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Flee button should now be responsive to clicking anywhere on it |
| P5397 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | Props.PlaceDesignProp uses bag items without the recipe; skipped props listed (BuildingClipboard ZoneMissingPropList) | Props in the Design details window should no longer have a red 'missing knowledge' bord... |
| P5398 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | Templates.Delete only the owner's designs, 'cannot delete' reply | Corrected error messaging when deleting a Design not owned by you |
| P5399 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | Props.PlaceDesignProp uses bag items without the recipe; skipped props listed (BuildingClipboard ZoneMissingPropList) | Acquiring a missing recipe should now also remove it from the Missing Props section of ... |
| P5400 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Mailed Designs that go into overflow inventory should no longer stack incorrectly |
| P5401 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Consumables on hotbar should now correctly show how many copies you have at all times |
| P5402 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Landmark menu should no longer open underneath the watchlist |
| P5403 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Landmark menu options now all have icons |
| P5404 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Various new “first time” informational windows have been added to explain features to n... |
| P5414 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Camera mode should no longer persist between logins |
| P5417 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Corrected messaging when trying to change cameras in play mode |
| P5419 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Loading screen tips no longer mention the evac to safety option since that has been rem... |
| P5460 | 2016-05-23 | [Feedback] Excessively long lo.. | client-only / no server rule | feedback thread / announcement / chatter | We're hoping we can find some common factor(s) that might help us track down why some p... |
| P5467 | 2016-05-23 | [Feedback] Excessively long lo.. | client-only / no server rule | feedback thread / announcement / chatter | Thanks for your help tracking this down! :) |
| P5629 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | The Inventory and Character windows have been combined into one, which is accessed usin... |
| P5630 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | Ratings.HandleAddVote accepts votes for any site → 6.43 | It is only possible to Like a build that is listed in the Gallery, so the Like button h... |
| P5631 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | If you have already liked a Build Site, the tooltip will now correctly tell you so when... |
| P5632 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Right-clicking an item of equipment on your character sheet should now return it to you... |
| P5633 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Filters applied to lumen station no longer reset after each purchase |
| P5634 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | "Enter Competition" button should grey out if you don't have a Genre selected |
| P5635 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | When editing a guild rank name, that field will have focus for your typing by default |
| P5636 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | F to interact key queues up the notification |
| P5637 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Placeholder “F to interact” text should no longer sometimes appear after zoning/logging |
| P5638 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | A pass has been done on Game Settings to remove obsolete or incorrect options |
| P5639 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Chat window fade out should now be more consistent, as follows: |
| P5640 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Continue to bring the window into focus when a new message is received. |
| P5641 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Prevent the "Chat" and "Loot" tab from bringing the window into focus this way. All oth... |
| P5642 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | When the window is brought into focus due to a new message being received, it will lose... |
| P5643 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | When the window loses focus automatically, the chat tabs should continue flashing when ... |
| P5644 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Add an option in the Chat settings "Focus chat window when new message received." Defau... |
| P5646 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Addressed an error where Windows 10 users might get an error about missing XINPUT1_3.dl... |
| P5658 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | We're also continuing to track down some performance issues that some folks are seeing ... |
| P5672 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | It's not immediately obvious from the window, but it CAN be resized, you can make it a ... |
| P5674 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | We wanted to get the combined window out to you as soon as possible to get your feedbac... |
| P5693 | 2016-06-02 | Hotfix Notes - 2 June 2016 | client-only / no server rule | client UI / visual / bug fix | The most helpful information you can give us is: |
| P5717 | 2016-06-03 | Week End Wrap Up - 3 June 2016 | client-only / no server rule | feedback thread / announcement / chatter | and yes, bug reports are still going to be welcome post-launch, and we greatly apprecia... |

### props (144)

| Id | Date | Thread | Class | Evidence | Text |
|---|---|---|---|---|---|
| P4147 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Prop categories have been reorganized and we hope the new categories are clear, but if ... |
| P4149 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | The tint tab is a list of tints that can be applied to the props on your site. Many (th... |
| P4150 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Select a tint and mouse over a prop on your site to preview the new tint; click to conf... |
| P4183 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Vaults: 60, +20 per pocket, max 120 (TASKS 5.3 closed) | Pre- update Vault size: 60, max 100 |
| P4184 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Vaults: 60, +20 per pocket, max 120 (TASKS 5.3 closed) | New Vault size: 60, max 120 |
| P4242 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | Wisps have temporarily forgotten how to fly. While this is slightly embarrassing to the... |
| P4250 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | [Fixed] Some of the lighting props are having trouble lighting. (Intricate chandelier, ... |
| P4261 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | A number of props still have tint issues that we're working on |
| P4283 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | [Not a bug] Missing Trailblazer vault or flags? |
| P4330 | 2016-03-25 | Week End Wrap Up - March 25, 2.. | client-only / no server rule | feedback thread / announcement / chatter | Some outfits are labeled one color but are actually another color. We're correcting the... |
| P4378 | 2016-03-31 | Hotfix notes - April 1 2016 | NOT FOLLOWED | Vendors.cs makes 'Build Site_Vendor_Object' (1491) a working shop → 6.52 | Vendor Booth had an old, non-functioning UI attached which has been removed. This is a ... |
| P4379 | 2016-03-31 | Hotfix notes - April 1 2016 | FOLLOWED | Props.BeginPlacement:350 needs the recipe (RecipeBook.KnowsProp) unless placed from a bag item | You are now required to have the recipe to place any particular prop from raw resources... |
| P4380 | 2016-03-31 | Hotfix notes - April 1 2016 | FOLLOWED | EditableProperties 13 / 14 (loop default off, 1-600 s); TimerResets only on reset actions; PauseAndResetTimer | Timer prop should now correctly allow an NPC to start pathing on the On Complete event |
| P4381 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Logic Gate: some additional options have been fixed/clarified |
| P4382 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Intricate Chandelier now turns on once again |
| P4383 | 2016-03-31 | Hotfix notes - April 1 2016 | SUPERSEDED | prop limit 2500 → 7000 → 100,000 (P5527, 25 May; TASKS 6.2) → 6.2 | [edit to add:] Prop limits on claims have been increased! They were unintentionally too... |
| P4477 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | The list of prop bundles being re-granted: |
| P4478 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | 10 Blue Light Orbs |
| P4479 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | 10 Blue Spotlights |
| P4480 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | 10 Green Light Orbs |
| P4481 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | 10 Green Spotlights |
| P4482 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | 10 Indigo Light Orbs |
| P4483 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | 10 Indigo Spotlights |
| P4484 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | 10 Orange Light Orbs |
| P4485 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | 10 Orange Spotlights |
| P4486 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | 10 Red Light Orbs |
| P4487 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | 10 Red Spotlights |
| P4488 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | 10 Violet Light Orbs |
| P4489 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | 10 Violet Spotlights |
| P4490 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | 10 White Light Orbs |
| P4491 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | 10 White Spotlights |
| P4492 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | 10 Yellow Light Orbs |
| P4493 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | 10 Yellow Spotlights |
| P4496 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | Bedroom Furniture Bundle |
| P4504 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | Kerran Furniture Bundle |
| P4506 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | Lightsource Mote Bundle |
| P4519 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | Wooden Furniture Bundle |
| P4522 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | You can now remove tints from a prop! Use the new “remove tint” icon at the end of the ... |
| P4552 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Prop context menu should no longer get hidden behind Build Mode UI |
| P4567 | 2016-04-12 | Update Notes - April 13, 2016 | SUPERSEDED | prop limit 2500 → 7000 → 100,000 (P5527, 25 May; TASKS 6.2) → 6.2 | Prop Limit has been increased to 7000 per site |
| P4568 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Prop limits now display on the on prop tab of build mode |
| P4569 | 2016-04-12 | Update Notes - April 13, 2016 | FOLLOWED | Props.PlaceDesignProp uses bag items without the recipe; skipped props listed (BuildingClipboard ZoneMissingPropList) | Designs should now use props that exist as items in inventory, even if the character do... |
| P4570 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Prop categories have been rearranged somewhat for clarity |
| P4571 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Script objects are now in a category called “Story Tools” found under “Special Effects”... |
| P4572 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Addressed multiple prop tinting oddities |
| P4573 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Void vault frame should no longer flicker out of visibility |
| P4574 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Red curtains saved in Designs should now place as 100% more red |
| P4575 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Flames on the Buccaneer's Chandelier now appear in the correct location |
| P4576 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Plastic Ceiling Light and Plastic Wall Light now emit a soft white light |
| P4577 | 2016-04-12 | Update Notes - April 13, 2016 | FOLLOWED | client Recipes / PropRecipe tables of the 0.589 build (costs, default-learned flags, Lumen Station list) | Floor switch prop is now available as a default recipe |
| P4578 | 2016-04-12 | Update Notes - April 13, 2016 | FOLLOWED | EditableProperties 15 / 16 dice weights; Logic and Timer filters (StoryTools) | Random gate percentages settings should now work correctly |
| P4579 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Chandelier (Iron) candle flame FX are now correctly positioned over the candles |
| P4580 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | The bouquet of roses prop now tints only the flower part, and no longer defaults to black |
| P4581 | 2016-04-12 | Update Notes - April 13, 2016 | SUPERSEDED | Teleport / Site Arrival Point replaced by the Starting Location (P4818, P5055, 26 Apr - 4 May): Claims.ArrivalOf / EnsureStartingLocation | Teleportation Arrival Point is now called a Site Arrival Point to avoid confusion |
| P4582 | 2016-04-12 | Update Notes - April 13, 2016 | SUPERSEDED | Teleport / Site Arrival Point replaced by the Starting Location (P4818, P5055, 26 Apr - 4 May): Claims.ArrivalOf / EnsureStartingLocation | Site arrival point now has an icon |
| P4583 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | All String Lights props should now place correctly |
| P4622 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Loot should no longer get stuck in props |
| P4651 | 2016-04-15 | Week End Wrap Up - April 15, 2.. | client-only / no server rule | feedback thread / announcement / chatter | We're not expecting to have a game update next week, but we'll be hard at work fixing a... |
| P4741 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Has a slightly shorter "reload" time |
| P4746 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Has a slight delay after second use instead of after third use |
| P4850 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | CraftingStations by station type; Spire has the Replicator and Repair Station (Spire.StationAt) | There are now TWO versions available of the replicator, and the repair station. They ha... |
| P4851 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | There are new props to be found! |
| P4852 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | The Y key is now used for building tool interactions (e.g. gentle smooth, dragging the ... |
| P4853 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | pathing node / trapdoor / pads reclaimable (followed); Props.PickUp lets a chest with items be picked up (Chests.Move) → 6.41 | Several props that previously could not be reclaimed (including translocate pad, trapdo... |
| P4854 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Corrected the icon on the vendor booth prop |
| P4855 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Some nature category props that were missing should now be available. Also includes two... |
| P4856 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | StoryTools.namedKinds word match misses sconces (691, 692, 2096, 2204, 2581), Emissive Orbs/Cubes (2127-2142), Control Panels (1172, 1176, 1177) → 6.36 | All interactive props (including emitters and lights) should now function properly with... |
| P4857 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Clicking on a build tool a second time now toggles it off again |
| P4858 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Gothic Gate (Right) should now position consistently |
| P4859 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Tinting on props with two materials such as the hexagonal grate should now be either en... |
| P4860 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | namedKinds 'Brazier'; books via BookDisplaysText | Books can now be placed |
| P4861 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Linking and triggering descriptions on props should now be clearer and more friendly |
| P4862 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | The wording of the menu options when you right-click a prop have been updated to be mor... |
| P4863 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Sliding props such as the portcullis should now be better at remembering their original... |
| P4864 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | There are no longer multiple untinted globe ornaments |
| P4865 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | Props refuses with NoClaimPermission / PickUp error 10 | If you try to move a prop but don’t have access to, you should now receive an error mes... |
| P4866 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT BUILT | prop 1220 Proximity Mine: no Link kind (StoryTools.KindOf needs 'Spawner:'), no code in Creatures / Props → 6.29 | Proximity mines should once again work |
| P4867 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | Chests.Slots: every recipe prop named 'Chest' is storage | Buccaneer’s Treasure Chest is now a functional storage chest |
| P4868 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Cerise Foil and Iridescent tints should now be more consistent colors |
| P4869 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Props should now face toward you instead of away from you when placed |
| P4870 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | Claims.Delete → Props.ReturnInBox → Remove → GiveItem(prop item); SiteContents.Pack stows props as items → 6.39 | Deleting your site (or automatic foreclosure) should now return the props as materials,... |
| P4871 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Sphere and cube brush sizes are no longer limited to 100, also the scrolling up and dow... |
| P4872 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | scale / collision of story props: client models | Story Tool props can now all be scaled (just for visual differentiation, does not affec... |
| P5037 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | There aren't as many slight size gradients as the old set, BUT, these will fit perfectl... |
| P5072 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Yeah, I agree that the current wording isn't very clear either. What we're hoping to do... |
| P5074 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Yeah, I agree that the current wording isn't very clear either. What we're hoping to do... |
| P5081 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | [All prop-specific options, like "Lock Chest" "Open Settings" etc.] |
| P5086 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Recycle (if player-placed prop) |
| P5087 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Delete (if procedural prop) |
| P5091 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | **Questions for everyone: What changes would you make to this order, keeping in mind th... |
| P5093 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | [All prop-specific options, like "Lock Chest" "Open Settings" etc.] |
| P5097 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Recycle (if player-placed prop) |
| P5098 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Delete (if procedural prop) |
| P5101 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Would you mind explaining the functionality you'd expect from the new prop? I'm not sur... |
| P5139 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | There'd unfortunately still be the occasional Lock Chest and Unlock Chest and (coming s... |
| P5142 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | After playing with this, I'm leaning towards putting prop-specific options above Move i... |
| P5154 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | **Would you still want the current Dice Roll prop in addition to this?** (I'd prefer to... |
| P5185 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | Like I mentioned in an earlier post, we'll be making some changes to chest rewards soon... |
| P5242 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | This is a clever idea, but if I understand it correctly, it still runs into the same pr... |
| P5245 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | This is a clever idea, but if I understand it correctly, it still runs into the same pr... |
| P5348 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Buildable prop count should now be displaying correctly |
| P5362 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | placement cost = PropRecipe components (Props.RecipeCost, client tables) | The placement cost for creature spawners has been changed to better reflect the actual ... |
| P5363 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | spawner essence costs and Lumen Station essence prices are client recipe data; drop rates are ours (no number) | To compensate for this change, and because essence drops already needed a bit of a boos... |
| P5364 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Addressed issues where some players were not able to place the Starting Location prop o... |
| P5365 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Gift props should no longer be missing their patterned textures |
| P5366 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Stone Bench can now be tinted |
| P5367 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Iron Trimmed Mug can now be tinted |
| P5368 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Hexagonal grate texture should no longer shift like flora |
| P5369 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Light source in the plastic lights is now less bright |
| P5370 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Tundra flower model no longer has collision |
| P5371 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT FOLLOWED | StoryTools.namedKinds word match misses sconces (691, 692, 2096, 2204, 2581), Emissive Orbs/Cubes (2127-2142), Control Panels (1172, 1176, 1177) → 6.36 | Buccaneer wall sconce can now turn on and off |
| P5372 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Ring Brace is now more tintable |
| P5373 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Holiday ornaments can now be tinted |
| P5374 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Translocator Pad setup menu should now be displaying correctly |
| P5375 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Novatech bowls, plates, and dividers are now more tintable |
| P5376 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Two new intricate carved props are available: a bench and a throne |
| P5377 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | An additional painting has been added (Deep Mines) |
| P5378 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | When you right-click on a prop outside a build site (for example, in an Outpost) you sh... |
| P5379 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | The Flinger prop’s target now has a target-like appearance |
| P5380 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Ring brace (iron) props should now correctly be placed instead of Ring brace (brass) in... |
| P5381 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | The order of prop settings in the menu has been tidied up |
| P5382 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Collision has been improved on the Volcanic Tree (Large Thorny) |
| P5383 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Drylily plant should now place correctly |
| P5384 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Cross Brace prop is once more available in the prop palette |
| P5385 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Alchemy Table prop saved in Designs should now correctly be replaced by the non-functio... |
| P5522 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Proximity mines and Target Dummies are now found in a subcategory of “Creatures” called... |
| P5523 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Resizing a prop should no longer cause erratic prop movement |
| P5524 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Emitter props should no longer have collision |
| P5525 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Added some punctuation in the Prop Limit Category display list |
| P5526 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | [Updated] Prop limit has been increased: |
| P5527 | 2016-05-24 | Update Notes - 25 May 2016 | NOT BUILT | already TASKS 6.2 (only the 50-chest limit exists, PropLimits.cs) → 6.2 | The global prop limit on a build site has been increased to 100,000 |
| P5528 | 2016-05-24 | Update Notes - 25 May 2016 | NOT BUILT | already TASKS 6.2 (only the 50-chest limit exists, PropLimits.cs) → 6.2 | Story Nodes no longer count towards the global prop limit on a build site |
| P5529 | 2016-05-24 | Update Notes - 25 May 2016 | NOT BUILT | already TASKS 6.2 (only the 50-chest limit exists, PropLimits.cs) → 6.2 | The creature spawner limit on a build site has been increased to 125 |
| P5530 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Brass mug prop is now more tintable |
| P5531 | 2016-05-24 | Update Notes - 25 May 2016 | FOLLOWED | namedKinds 'Brazier'; books via BookDisplaysText | Brazier (mounted gothic) can now be linked |
| P5532 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Confirmed all types of “brace” prop can now be tinted |
| P5533 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Toned down the sound effect volume on translocation pad |
| P5534 | 2016-05-24 | Update Notes - 25 May 2016 | FOLLOWED | StoryTools lock kinds (doors, gates, portcullises, chests) | Doors can now be locked and unlocked |
| P5535 | 2016-05-24 | Update Notes - 25 May 2016 | FOLLOWED | StoryTools lock kinds (doors, gates, portcullises, chests) | Chests can now be locked and unlocked |
| P5536 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Prop listings in the Lumen Station should now be alphabetized within their subcategory |
| P5537 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Corrected some culinary props that were miscategorized as furniture in the Lumen Statio... |
| P5647 | 2016-05-24 | Update Notes - 25 May 2016 | NOT BUILT | Spire.cs builds one Spire on the home continent; Spire.TravelTo re-zones there from other landscapes → 6.49 | Central spires are now raised slightly higher out of the ground, to avoid issues where ... |
| P5661 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Staff of the Lightning Caller |
| P5662 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Blight-Warden's Pain |

### storytools (118)

| Id | Date | Thread | Class | Evidence | Text |
|---|---|---|---|---|---|
| P4546 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Saving your build site information with no text in the story field should no longer cau... |
| P4555 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | The Design tab of the Build UI should have much improved performance when scrolling thr... |
| P4821 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Build site management drop-downs now show all available tags at once, without requiring... |
| P4873 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | We’ve added in a whole lot of new functionality for telling stories with your creatures... |
| P4874 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Note: we do have a few known issues with the story node props that we will need to hotf... |
| P4875 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | Props.SetText caps every prop's text at 1024 → 6.35 | **NPC Text:** It’s happening! You can find the fancy new Story Node prop in the Story T... |
| P4876 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | StoryTools.Perform always sends creature text as Chat.NpcSay; node Text Display / Duration ignored; DropDowns lacks 577170 'In Chat Bubble and Chat' → 6.33 | **Chat Bubbles:** Speaking of creatures talking, they’ve got a whole new way of talking... |
| P4877 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | StoryTools mood / stance / emote (PropSettings drop-downs) | **NPC Animations:** Bonus surprise! You now have complete animation controls for your c... |
| P4878 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | faction actions (StoryTools FactionAlly / Neutral / Enemy); per-kind event / action lists | **Simple Language Linking:** All of the text on prop Links has been updated to make it ... |
| P4879 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | faction actions (StoryTools FactionAlly / Neutral / Enemy); per-kind event / action lists | **Smart Link Visibility:** You’ll notice that occasionally the Events and Actions avail... |
| P4880 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | **Bug-Stomping:** We did a full pass on all of the on Linking props to improve their fu... |
| P4881 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Before we move on, we want to give you a detailed example of just one way you could set... |
| P4882 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Place a few props: 1 Story Node, 1 Metal Switch, 1 Slaug Slasher, 1 Spitting Chomper |
| P4883 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT BUILT | PropSettings settingsByKind['spawner'] = {18,19,31} omits EditableProperties 17 prop_npc_spawner_faction_select; SpawnStory never reads a faction → 6.30 | Right-click both creatures and choose “Open Settings” to change both creatures’ Faction... |
| P4884 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Right-click the Story Node prop and select “Open Settings” |
| P4885 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | In the Settings window, choose a Mood, Stance, and Emote. (I chose Angry, Sneaking, Cheer) |
| P4886 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | StoryTools.Perform always sends creature text as Chat.NpcSay; node Text Display / Duration ignored; DropDowns lacks 577170 'In Chat Bubble and Chat' → 6.33 | In the Settings window, choose where to Display the Text (I chose Above NPC) |
| P4887 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | In the Settings window, type in the text you want your creature to say (I chose “I win!”) |
| P4888 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Close the Settings window. |
| P4889 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Story Nodes are powerful, independent tools that don’t need to be tied to a single spec... |
| P4890 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | For this example, first we want to create a Link that activates some/all of the story c... |
| P4891 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Right-click the Metal Switch prop. Select “Create Link” and choose “When this switch tu... |
| P4892 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Connect that Link to the Story Node prop and choose “…Activate everything on this objec... |
| P4893 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Next, we need to create a Link that lets the Story Node tell specific creatures to perf... |
| P4894 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Right-click the Story Node. Select “Create Link” and choose “When this object’s stance ... |
| P4895 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Connect that Link to the Slaug Slasher and choose “…Set this creature’s stance.” Now wh... |
| P4896 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Create similar Links to the Slaug Slasher for Mood and Emote. |
| P4897 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Right-click the Story Node. Select “Create Link” and choose “When this object’s text ac... |
| P4898 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Connect that Link to the Spitting Chomper and choose “…Have this creature say the text.” |
| P4899 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | We’re all done! Now it’s time to test! Enter Play Mode and click the Metal Switch. |
| P4900 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | creature text goes out as a Say line, not by the node's display setting → 6.33 | The Spitting Chomper should shout its declaration of victory to the world in a fun chat... |
| P4901 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | The Slaug Slasher, jealous of his rival’s victory, should applaud quietly for the Chomp... |
| P4902 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | That’s a super simple example of how you can use Story Nodes to send story commands to ... |
| P4903 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | As a quick demonstration of the Story Nodes in action, here's a video showing a quick s... |
| P4978 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | StoryTools.Perform always sends creature text as Chat.NpcSay; node Text Display / Duration ignored; DropDowns lacks 577170 'In Chat Bubble and Chat' → 6.33 | Story node prop currently defaults to displaying text on screen, despite what the drop-... |
| P4979 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | known-issue report (client bug) | Some elements of linking and triggering setups including story node props may sometimes... |
| P4985 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | Props.Editable: the site owner (all bits) edits others' story props / creatures | Story node props can't currently be edited by anyone other than the actual owner [fixed... |
| P4993 | 2016-04-29 | Hotfix notes - 29 April 2016 | FOLLOWED | Props.Editable: the site owner (all bits) edits others' story props / creatures | Fixed an issue where creatures or story props placed by other people could not be edite... |
| P4995 | 2016-04-29 | Hotfix notes - 29 April 2016 | NOT FOLLOWED | StoryTools.Perform always sends creature text as Chat.NpcSay; node Text Display / Duration ignored; DropDowns lacks 577170 'In Chat Bubble and Chat' → 6.33 | Story node props no longer default to "on screen" display |
| P4999 | 2016-04-29 | Hotfix notes - 29 April 2016 | client-only / no server rule | client UI / visual / bug fix | Corrected an issue where sometimes a story prop or creature name was not correctly dele... |
| P5003 | 2016-04-30 | Week End Wrap Up - April 30 | client-only / no server rule | feedback thread / announcement / chatter | If you didn't already catch up on the details, we highly recommend reading the patch no... |
| P5004 | 2016-04-30 | Week End Wrap Up - April 30 | client-only / no server rule | feedback thread / announcement / chatter | In the Landmark community, we've already been seeing some amazing builds using the new ... |
| P5008 | 2016-04-30 | Week End Wrap Up - April 30 | client-only / no server rule | feedback thread / announcement / chatter | We hope you're having a great weekend in Landmark! What are YOU doing with creatures an... |
| P5010 | 2016-05-01 | wiki: Game Updates 2016 | FOLLOWED | Sign / Book / Scroll DisplaysText (Props.DisplayText) | Story Tools: Props like Books and Scrolls can have text displayed (linked) and related ... |
| P5053 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | I hope you've been having fun playing around with all the new Story Tools at your dispo... |
| P5054 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | As part of our big feedback gathering, I wanted to get your thoughts specifically on th... |
| P5056 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | **Story Node:** Have your creatures talk and perform animations. |
| P5062 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | To that end, this thread will consolidate the feedback specifically related to these 6 ... |
| P5067 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Funny you should mention that :) Internally, we've been looking into a new Damage Node ... |
| P5111 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | We just added Help pages for each of the Story Tools in yesterday's patch. You'll get a... |
| P5112 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | scale / collision of story props: client models | All Story Tool props should have had their collision removed in yesterday's patch :) Pl... |
| P5116 | 2016-05-04 | [Feedback] Story Tools | NOT BUILT | text only reaches creatures linked with SayText (StoryTools.Perform) → 6.34 | We added a ton of fall-back logic to the Story Nodes in yesterday's patch, that essenti... |
| P5117 | 2016-05-04 | [Feedback] Story Tools | NOT BUILT | text only reaches creatures linked with SayText (StoryTools.Perform) → 6.34 | Please play around with the Story Node now that we have all of this backup logic in, an... |
| P5179 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | **Need More Outpost Variety:** Agreed! We definitely want to do another contest to crea... |
| P5208 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | Unless there's a technical limitation, I'd actually want to allow full story props in t... |
| P5216 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | Now that you've had a chance to play around with them, we want to hear what you think! ... |
| P5223 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | Hiding the Emitter pyramids is definitely something we want to do, but it actually has ... |
| P5244 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | I'll save discussion for the damage fields/nodes for the Story Tools thread. It's somet... |
| P5247 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | I'll save discussion for the damage fields/nodes for the Story Tools thread. It's somet... |
| P5261 | 2016-05-06 | Week End Wrap Up - May 6 | client-only / no server rule | feedback thread / announcement / chatter | We also enjoyed Taelaa's new player tutorial, using the story props to explain basic La... |
| P5262 | 2016-05-06 | Week End Wrap Up - May 6 | client-only / no server rule | feedback thread / announcement / chatter | Be sure to let us know about any other fun/cool/clever story builds you come across in ... |
| P5276 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | StoryTools.PlayModeChanged / Stop rebuild state from the built props; ResetStory restarts Play Mode | When you enter Play Mode, the current state of all props on your Build Site will be saved. |
| P5277 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | StoryTools.PlayModeChanged / Stop rebuild state from the built props; ResetStory restarts Play Mode | When you return to Edit Mode, that saved state will be automatically restored. |
| P5278 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | StoryTools.PlayModeChanged / Stop rebuild state from the built props; ResetStory restarts Play Mode | Example: When I enter Play Mode, a door is closed and a torch is lit. A friend visits m... |
| P5279 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | StoryTools.PlayModeChanged / Stop rebuild state from the built props; ResetStory restarts Play Mode | This also affects settings on props like NPC Faction, Timer countdowns, and Logic Filte... |
| P5280 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | StoryTools.PlayModeChanged / Stop rebuild state from the built props; ResetStory restarts Play Mode | Of course, you’ll likely want to take advantage of that cool new Story Saving feature t... |
| P5281 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | StoryTools.PlayModeChanged / Stop rebuild state from the built props; ResetStory restarts Play Mode | This prop has no Events and one Action (when linked TO this prop from another prop): “…... |
| P5282 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | StoryTools.PlayModeChanged / Stop rebuild state from the built props; ResetStory restarts Play Mode | When that Action is triggered, it will restore the saved state of all props and creatur... |
| P5283 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | StoryTools.PlayModeChanged / Stop rebuild state from the built props; ResetStory restarts Play Mode | You can use this for all sorts of things. Like making a timed challenge that resets aft... |
| P5284 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT BUILT | text only reaches creatures linked with SayText (StoryTools.Perform) → 6.34 | Previously, Story Nodes were very rigid in how they required Links to be set up in orde... |
| P5285 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Example: When an Action is triggered to activate Text: |
| P5286 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT BUILT | text only reaches creatures linked with SayText (StoryTools.Perform) → 6.34 | [Current functionality] First, the Story Node looks for an outgoing link to send that T... |
| P5287 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT BUILT | text only reaches creatures linked with SayText (StoryTools.Perform) → 6.34 | [New functionality] If no outgoing Links for the Text exist, then the Story Node checks... |
| P5288 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT BUILT | text only reaches creatures linked with SayText (StoryTools.Perform) → 6.34 | [New functionality] If the no outgoing Links for the text exist AND the prop that trigg... |
| P5289 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT BUILT | text only reaches creatures linked with SayText (StoryTools.Perform) → 6.34 | With these two new backup scenarios in place, you should almost always see text and ani... |
| P5290 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | StoryTools NodeEverythingActivates / PerformEverything | Story Node: New Event “When everything on this node activates…” |
| P5291 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | StoryTools NodeEverythingActivates / PerformEverything | Triggered by the existing Action “…Activate everything on this node.” |
| P5292 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | StoryTools NodeEverythingActivates / PerformEverything | When linked to a creature spawner, connects to a new Action “…Have this creature perfor... |
| P5293 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | StoryTools NodeEverythingActivates / PerformEverything | This should make it much faster to create Links for Story Nodes sending everything to t... |
| P5294 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | client Recipes / PropRecipe tables of the 0.589 build (costs, default-learned flags, Lumen Station list) | Now costs Plain Wood, like other Story Tools. |
| P5295 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | prop rename; StoryTools.Stop; Say channel; any event links to any action (StoryTools.Add) | Now shows their name (which you can edit), to make it easier to organize them. |
| P5296 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | prop rename; StoryTools.Stop; Say channel; any event links to any action (StoryTools.Add) | Now stops sending Text when you switch to Edit Mode. |
| P5297 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT FOLLOWED | StoryTools.Perform always sends creature text as Chat.NpcSay; node Text Display / Duration ignored; DropDowns lacks 577170 'In Chat Bubble and Chat' → 6.33 | Text Display setting now defaults to Chat Bubble (instead of On Screen). |
| P5298 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | prop rename; StoryTools.Stop; Say channel; any event links to any action (StoryTools.Add) | Text sent to the chat window now uses the proper formatting to mimic other chat message... |
| P5299 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT BUILT | no HTML stripping in Props.SetText / Chat (check whether the client escapes it) → 6.35 | Due to potential issues with consistency and security, text can no longer display HTML.... |
| P5300 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | prop rename; StoryTools.Stop; Say channel; any event links to any action (StoryTools.Add) | You can now use Story Node Events (like “When the stance on this node activates…”) to t... |
| P5301 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | New Link Functionality (Optional): “Auto-complete Links when only one action is available” |
| P5302 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | You can find this in your Game Settings window, defaulted to off. |
| P5303 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | If turned on, when you’re creating a Link and open an Action List with only one option ... |
| P5304 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT FOLLOWED | Creatures.SendSpawn flags every story creature interactable; ClickCreature ignores faction → 6.32 | The prompt to interact (and interaction highlight visual) now only appears on creatures... |
| P5305 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT FOLLOWED | Creatures.SendSpawn flags every story creature interactable; ClickCreature ignores faction → 6.32 | While on the Enemy Faction, creatures now realize they’re too busy killing you to be in... |
| P5306 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | scale / collision of story props: client models | All Story Tool props no longer have collision. Run free, builders! |
| P5307 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Darkfang Specter will now correctly trigger Areas that it enters. |
| P5308 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | StoryTools.Spawn fires CreatureSpawns after the creature exists | Creatures are now able to speak/animate immediately after they spawn. This means the “W... |
| P5309 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | StoryTools.Spawn fires CreatureSpawns after the creature exists | On spawn events can now be linked to Story Node props |
| P5314 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Guild list should no longer fail to display a name when a list longer than a full page ... |
| P5442 | 2016-05-13 | Week End Wrap Up - 13 May 2016 | client-only / no server rule | feedback thread / announcement / chatter | We hope you enjoyed this week's update. If you missed reading the update notes, there's... |
| P5447 | 2016-05-13 | Week End Wrap Up - 13 May 2016 | client-only / no server rule | feedback thread / announcement / chatter | We've tried to update the Known Issues list (https://forums.daybreakgames.com/landmark/... |
| P5476 | 2016-05-24 | Update Notes - 25 May 2016 | FOLLOWED | Sign / Book / Scroll DisplaysText (Props.DisplayText) | Book (and scroll) props can now be linked to display text |
| P5477 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | StoryTools.Perform always sends creature text as Chat.NpcSay; node Text Display / Duration ignored; DropDowns lacks 577170 'In Chat Bubble and Chat' → 6.33 | Story Nodes text display options now have an option "Chat & Chat Bubble" to display tex... |
| P5478 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | StoryTools.Perform always sends creature text as Chat.NpcSay; node Text Display / Duration ignored; DropDowns lacks 577170 'In Chat Bubble and Chat' → 6.33 | Story Nodes now have a Text Display Duration setting |
| P5479 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Resolved some conflicts with interact notifications that could cause interact text to n... |
| P5480 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Updated the wording of the display options on linked props so that [V] toggles between ... |
| P5481 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Updated the sort order of linking options displayed on a linkable prop, so they should ... |
| P5482 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | Creatures.SendSpawn flags every story creature interactable; ClickCreature ignores faction → 6.32 | Interaction prompts should no longer be visible on props with no active interactions |
| P5483 | 2016-05-24 | Update Notes - 25 May 2016 | FOLLOWED | EditableProperties 13 / 14 (loop default off, 1-600 s); TimerResets only on reset actions; PauseAndResetTimer | Timer Node should no longer reset by default |
| P5484 | 2016-05-24 | Update Notes - 25 May 2016 | FOLLOWED | EditableProperties 13 / 14 (loop default off, 1-600 s); TimerResets only on reset actions; PauseAndResetTimer | Timer Prop should now have a higher maximum than 2 minutes |
| P5485 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | All prop settings windows should now display ‘saved’ to indicate changes have been saved |
| P5486 | 2016-05-24 | Update Notes - 25 May 2016 | FOLLOWED | story tools are palette props, not tradeable items | Story Tool props are no longer soulbound |
| P5487 | 2016-05-24 | Update Notes - 25 May 2016 | FOLLOWED | story creatures exist only in Play Mode | Creatures can only be interacted with in play mode |
| P5488 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Cleaned up some Text Notification areas: |
| P5489 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Only one interact prompt will ever be on screen at a time, so it’s always clear which p... |
| P5490 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | The interact prompt will not linger on-screen once you’re out of interact range or move... |
| P5491 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | It now has its own dedicated notification area at the bottom-middle of the screen, so i... |
| P5492 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | Creatures.SendSpawn flags every story creature interactable; ClickCreature ignores faction → 6.32 | The interact prompt will now only appear when you have permissions to interact with a p... |
| P5493 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | The text for all of the interact prompts has been cleaned up and standardized. They now... |
| P5494 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | We cleaned up some of the other notifications as well. |
| P5495 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | StoryTools.Perform always sends creature text as Chat.NpcSay; node Text Display / Duration ignored; DropDowns lacks 577170 'In Chat Bubble and Chat' → 6.33 | Text from Story Node has its own dedicated area at the top-middle of the screen. Font i... |

### gathering (101)

| Id | Date | Thread | Class | Evidence | Text |
|---|---|---|---|---|---|
| P4100 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | There’s tons of stuff in this update as well as a good number of pesky bugs we know abo... |
| P4113 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Names that are tied to accounts that haven’t logged in since before our last wipe in 20... |
| P4115 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | **Remember that tradeable marketplace items, and lumens, will be DELETED if you delete ... |
| P4117 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | If you had claims in the world prior to the wipe, you will find that we have created De... |
| P4119 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Refer to the Wipe FAQ (https://forums.daybreakgames.com/landmark/index.php?threads/land... |
| P4125 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Terrain/OreVeins.cs: veins spawn from the client's ContinentVeinData .vdt surface / cave pools (launch data) | With the changes to the world also come changes to harvestable resources! The changes t... |
| P4126 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Terrain/OreVeins.cs: veins spawn from the client's ContinentVeinData .vdt surface / cave pools (launch data) | On the surface you will find: marble, amaranthine, copper, tin, agate, and sapphire |
| P4127 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Terrain/OreVeins.cs: veins spawn from the client's ContinentVeinData .vdt surface / cave pools (launch data) | Underground you will find: alabaster, obsidian, silver, iron, gold, ruby, emerald, and ... |
| P4128 | 2016-03-21 | Landmark Update Notes - March 21 | NOT FOLLOWED | Gathering.cs spawns plant nodes (PlantKind, PlantDensity, Region) and Gathering.Reap gives plant items to sickles → 6.12 | Plants are no longer harvested, and sickles are no longer used for anything.We didn’t f... |
| P4129 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Gathering.Chop: Tree Fiber 30 % per hit, Tree Sap 15 % on felling | Tree harvesting remains mostly unchanged, but all trees now give two additional resourc... |
| P4130 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | free terrain materials vs paid materials: Building.PayFor / BuildMaterials (FEATURES Building) | We’re also made some changes to what resources are free to build with. While terrain ma... |
| P4131 | 2016-03-21 | Landmark Update Notes - March 21 | NOT FOLLOWED | Abilities.cs:247 Gathering.Chop(session, guid, aimedAt) passes no tool: axe perks (Charmed 3260, Goliath 3261, Hotfoot 3263) never apply → 6.14 | Picks and Axes now have different abilities such as increased speed, damage, or resourc... |
| P4132 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Abilities.cs:250-258: extractor swings chop / reap nodes or mine | There is also an exciting new range of harvesting tools powered by Pulverizer technolog... |
| P4146 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | We have also added a few new props for you to collect and there should be more coming o... |
| P4239 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Pulverizers no longer exist in their old form, they are now known as the new harvest ex... |
| P4272 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | There may be several seconds delay when placing a larger template. To be safe, wait for... |
| P4274 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | [Fixed] Sometimes while doing the starter Goals you may complete one and not be offered... |
| P4297 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | sites may include surface caves (no cave rule in Claims.CanPlace) | You have more flexibility when claiming on the surface now, so you can lower your Build... |
| P4313 | 2016-03-24 | Hotfix notes - Friday March 25 | client-only / no server rule | client UI / visual / bug fix | We're hurrying to get the above fixes out to you before the weekend, as we feel these a... |
| P4315 | 2016-03-24 | Hotfix notes - Friday March 25 | client-only / no server rule | client UI / visual / bug fix | We intend that lumens will eventually be possible to purchase for Daybreak Cash from th... |
| P4316 | 2016-03-25 | Week End Wrap Up - March 25, 2.. | client-only / no server rule | feedback thread / announcement / chatter | This was a huge week in Landmark with tons of updates, lots of changes, and lots of new... |
| P4333 | 2016-03-25 | Week End Wrap Up - March 25, 2.. | client-only / no server rule | Founder's Pickaxe / founder items: no Founder's Packs on this server (FEATURES: decide) | Founder's Pickaxe should be better than the starting picks. We'll improve it! |
| P4334 | 2016-03-25 | Week End Wrap Up - March 25, 2.. | client-only / no server rule | feedback thread / announcement / chatter | If you purchased the Dungeon Starter Set before your Goals window actually told you so,... |
| P4341 | 2016-03-28 | Additional lumen re-grants | client-only / no server rule | feedback thread / announcement / chatter | Just an FYI, some people may notice they receive an additional grant of some extra lume... |
| P4345 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Friday morning's hotfix (https://forums.daybreakgames.com/landmark/index.php?threads/se... |
| P4384 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | Founder's Pickaxe / founder items: no Founder's Packs on this server (FEATURES: decide) | Founder's Pickaxe stats have been improved |
| P4431 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | We intend that lumens will eventually be possible to purchase for Daybreak Cash from th... |
| P4434 | 2016-03-31 | Hotfix notes - April 1 2016 | FOLLOWED | goal list, rewards and Luminary score from the live capture (Achievements.cs, DailyGoals.cs) | The lumen reward for goals has been balanced so that the reward for higher tier goals i... |
| P4441 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Once you see how these changes feel (do please try it out first before giving your opin... |
| P4449 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | Founder's Pickaxe / founder items: no Founder's Packs on this server (FEATURES: decide) | People who reported missing base build sites, Founder's Pickaxe or other founder items ... |
| P4452 | 2016-03-31 | Hotfix notes - April 1 2016 | FOLLOWED | Gathering.WrongTool / reach messages go to the player only | "You are too far away to harvest that" message should no longer go to the entire group. |
| P4459 | 2016-03-31 | Hotfix notes - April 1 2016 | NOT FOLLOWED | already TASKS 6.3 (no session pacing anywhere; RollDrops ignores stat 17; plain digging 0.3 % lumens, no recipes) → 6.3 | No, perhaps that example was badly phrased. I'll edit that to be clearer. Just mean to ... |
| P4469 | 2016-04-01 | Week End Wrap Up - April 1, 2016 | client-only / no server rule | feedback thread / announcement / chatter | We're now just over a week past the big update (https://forums.daybreakgames.com/landma... |
| P4551 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Designs that were missing creator names should now show the names once more (exception:... |
| P4554 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | The Design details window now shows how many resources of each type are needed to place... |
| P4587 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Whirlwind buff no longer interferes with gaining resources |
| P4596 | 2016-04-12 | Update Notes - April 13, 2016 | NOT FOLLOWED | already TASKS 6.3 (no session pacing anywhere; RollDrops ignores stat 17; plain digging 0.3 % lumens, no recipes) → 6.3 | You should now find the pacing for loot drops feels a little more naturally spaced out |
| P4627 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Tree harvesting animation should now play consistently when chopping a tree |
| P4628 | 2016-04-12 | Update Notes - April 13, 2016 | NOT FOLLOWED | Gathering.Region (~l.552): Old Growth (biome 50) small trees give Plain Wood 71, only large ones Burled 72 → 6.13 | Trees within a biome should now more consistently drop the same type of wood |
| P4629 | 2016-04-12 | Update Notes - April 13, 2016 | FOLLOWED | fiber / sap rates exist (Gathering.Chop); no number given | Made some adjustments to the drop rate of fiber and sap from trees |
| P4637 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | known-issue report (client bug) | Some updates to Goals are still to come, including more starter templates. |
| P4643 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | We hesitate to just roll back the change because that would break the paint tool again,... |
| P4657 | 2016-04-16 | Loading screen tips being upda.. | FOLLOWED | Abilities.cs:250-258: extractor swings chop / reap nodes or mine | Extractors allow you to quickly dig through dirt and stone, and also allow the harvesti... |
| P4668 | 2016-04-21 | Showcase updates | client-only / no server rule | feedback thread / announcement / chatter | The shopping cart button in your upper left menu now opens the Marketplace (and your "M... |
| P4670 | 2016-04-21 | Showcase updates | NOT BUILT | Store.Gallery lists every site ('TODO: only published sites'); free-text search only; no comments, no leaderboard / rising stars, no Play Mode filter → 6.43 | We have updated the left and right sides of the gallery so they now show leaderboards (... |
| P4683 | 2016-04-22 | Week End Wrap Up - April 22, 2.. | client-only / no server rule | feedback thread / announcement / chatter | The shopping cart button in your upper left menu now opens the Marketplace (and your "M... |
| P4685 | 2016-04-22 | Week End Wrap Up - April 22, 2.. | NOT BUILT | Store.Gallery lists every site ('TODO: only published sites'); free-text search only; no comments, no leaderboard / rising stars, no Play Mode filter → 6.43 | We have updated the left and right sides of the gallery so they now show leaderboards (... |
| P4774 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.3 (no session pacing anywhere; RollDrops ignores stat 17; plain digging 0.3 % lumens, no recipes) → 6.3 | You now earn fewer lumens at the beginning of a play session, but more over longer sess... |
| P4811 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | access masks from the client (Claims PermissionUse / Plunder / Decorate / Build / Copy) | Site Permission ranks have been renamed, and have new descriptions to make them a littl... |
| P4818 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | Props.BeginPlacement places a second Starting Location; ArrivalOf takes the first → 6.37 | Attempting to place a new Starting Location prop if you already have one placed on your... |
| P4831 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Designs in the build mode UI now have more categories, including a separate one for pla... |
| P4842 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Grappling should now work while using an extractor or axe |
| P4846 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.7 → 6.7 | Items are a bit more functional when reaching 0% condition |
| P4960 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Trees in volcanic biomes are now sunk more securely into the ground |
| P4968 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Trying to travel to a build site that is no longer available should now give more helpf... |
| P4970 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | You should no longer hear a ‘failure’ sound on the last stroke of chopping down a tree |
| P4972 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | More collision on flora has been removed |
| P4973 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | More than five buffs can now be displayed |
| P4982 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | known-issue report (client bug) | Some of the new NovaTech props will become more tintable in future updates (notably the... |
| P4996 | 2016-04-29 | Hotfix notes - 29 April 2016 | client-only / no server rule | client UI / visual / bug fix | A bug where the paint tool could still sometimes paint through walls onto something beh... |
| P4997 | 2016-04-29 | Hotfix notes - 29 April 2016 | client-only / no server rule | client UI / visual / bug fix | Building tools should work more reliably when using the freefly camera at long distance... |
| P5016 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | Long ago we granted all new characters a set of "starter templates" that were a series ... |
| P5020 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | Leave anything that's a more specialized use for the community to share in swap meets a... |
| P5025 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | old ovals sets (seems a more specialist use case, better shared in swap meets) |
| P5028 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | old fonts sets (also pretty specialized use better for swap meets, and there are many m... |
| P5029 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | any kind of voxel reactors, these are definitely an advanced concept that is for more a... |
| P5040 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | The columns are not hollow, I just stacked them to save space. Please do visit the site... |
| P5073 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Do you think that'd be more clear? I'm hopeful that having a visual representation of t... |
| P5075 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Do you think that'd be more clear? I'm hopeful that having a visual representation of t... |
| P5079 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Lots of great feedback, Voidlust. Thanks for the mockups and screens as well. I won't a... |
| P5100 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | We really, really want to make it happen and our artists and coders are working togethe... |
| P5110 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Bug 3 is tough to reproduce without more details. Let us know if you're able to identif... |
| P5115 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | In this thread, we're looking for smaller-scale changes and improvements we can do to m... |
| P5118 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | This is definitely a very long-term goal because this sort of system would require a si... |
| P5212 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | And hopefully, as time goes on and we squash more of these bugs, you won't have to be u... |
| P5230 | 2016-05-04 | [Feedback] Prop Linking | FOLLOWED | EditableProperties 13 / 14 (loop default off, 1-600 s); TimerResets only on reset actions; PauseAndResetTimer | In today's patch we did change the wording on the Setting to say "Loop until Paused" in... |
| P5243 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | I'd prefer to focus on solving that core problem through R&D, so we can implement the i... |
| P5246 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | I'd prefer to focus on solving that core problem through R&D, so we can implement the i... |
| P5255 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | I'd like to get more opinions on this before making this change, to see if this is the ... |
| P5256 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | That is exactly what they do :) Thanks for taking the time to weigh in! I'm pretty happ... |
| P5257 | 2016-05-06 | Week End Wrap Up - May 6 | client-only / no server rule | feedback thread / announcement / chatter | This was a quiet week for you as we didn't do any updates, but a busy week for us, as w... |
| P5338 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Corrected some more outfit color issues |
| P5341 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Using extractor should no longer break pick and axe animations |
| P5342 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Addressed some issues that were causing intermittent failure of gem/ore seekers – if yo... |
| P5344 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT FOLLOWED | Props.PickUp: Delete (0x07) refunds nothing; PickUp (0x05) returns the prop item, never materials → 6.38 | When a prop is recycled, you will see what resources you got back in the loot window |
| P5352 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Designs now have more category options which should clearer: Starter Castle, Player Stu... |
| P5356 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Highfall Cobblestone material is once more available in the materials palette, and the ... |
| P5415 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT FOLLOWED | Gathering.Region (~l.552): Old Growth (biome 50) small trees give Plain Wood 71, only large ones Burled 72 → 6.13 | All old growth forest trees should now be giving burled wood. If you find any that are ... |
| P5418 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | There should no longer be floating props (trees, rocks, etc) around surface cave entrances |
| P5446 | 2016-05-13 | Week End Wrap Up - 13 May 2016 | client-only / no server rule | feedback thread / announcement / chatter | Second, as we've been noting in the 'known issues' lists for some weeks now, we're awar... |
| P5455 | 2016-05-20 | Week End Wrap Up - 20 May 2016 | client-only / no server rule | feedback thread / announcement / chatter | In the mean time thanks for all your feedback on these forums, twitter, the issue track... |
| P5459 | 2016-05-20 | Week End Wrap Up - 20 May 2016 | client-only / no server rule | feedback thread / announcement / chatter | See you next week with lots more news and fun stuff - happy weekend, and hope to see yo... |
| P5676 | 2016-05-27 | Week End Wrap Up - 27 May 2016 | client-only / no server rule | feedback thread / announcement / chatter | Read the announcement and more details here (https://forums.daybreakgames.com/landmark/... |
| P5677 | 2016-05-27 | Week End Wrap Up - 27 May 2016 | client-only / no server rule | feedback thread / announcement / chatter | In the coming weeks you'll be seeing more information including an updated launch FAQ a... |
| P5681 | 2016-06-02 | Hotfix Notes - 2 June 2016 | client-only / no server rule | client UI / visual / bug fix | This update should change the behaviour of blending back to how it was before the March... |
| P5688 | 2016-06-02 | Hotfix Notes - 2 June 2016 | client-only / no server rule | client UI / visual / bug fix | We wanted to push this out before launch since it can potentially affect the appearance... |
| P5692 | 2016-06-02 | Hotfix Notes - 2 June 2016 | client-only / no server rule | client UI / visual / bug fix | Thanks for the reports, folks! That's why we hurried this update out before launch, we ... |
| P5700 | 2016-06-02 | Hotfix Notes - 2 June 2016 | client-only / no server rule | client UI / visual / bug fix | The "cracks" in the non-architectural materials was an issue that existed previously, b... |
| P5703 | 2016-06-02 | Hotfix Notes - 2 June 2016 | client-only / no server rule | client UI / visual / bug fix | My understanding is that previous to March, this actually wasn't working correctly. Tha... |
| P5709 | 2016-06-03 | Week End Wrap Up - 3 June 2016 | client-only / no server rule | feedback thread / announcement / chatter | last day to buy founders packs is Monday morning, if you want any of the goodies in the... |
| P5710 | 2016-06-03 | Week End Wrap Up - 3 June 2016 | client-only / no server rule | feedback thread / announcement / chatter | servers coming down Tuesday morning, at which point we'll wipe characters and claims, a... |

### logic (97)

| Id | Date | Thread | Class | Evidence | Text |
|---|---|---|---|---|---|
| P4143 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | This tab shows all the building designs (formerly called templates) that you own, and c... |
| P4145 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | This tab shows all the props you currently know how to make. Recipes for new props can ... |
| P4152 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | This tab contains the original building tools and shaped blocks, as well as the list of... |
| P4154 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | We have also changed the default keybindings for switching between modes and axes. Q = ... |
| P4156 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Tweak Mode applies to templates, props, and build tools and we’ve made some changes to ... |
| P4159 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | The small cog icon at the top right of the build mode UI allows you to access the advan... |
| P4240 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | As noted above, there are a number of known issues that we didn't want to delay the upd... |
| P4241 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | We will be using the third-party fansite dgcissuetracker.com (https://dgcissuetracker.c... |
| P4244 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | [Fixed]Placing templates and pasted selections is not currently defaulting to tweak mod... |
| P4255 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | [Fixed] Props that are free to place are not included in the "can build" filter |
| P4266 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | If you place a creature too close to a pathing node, it sometimes confuses the creature... |
| P4295 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | We'll continue to work on the unresolved known issues above and keep you updated! And w... |
| P4317 | 2016-03-25 | Week End Wrap Up - March 25, 2.. | client-only / no server rule | feedback thread / announcement / chatter | If you haven't had a chance to log in yet, here are some useful links: |
| P4323 | 2016-03-25 | Week End Wrap Up - March 25, 2.. | client-only / no server rule | feedback thread / announcement / chatter | Speaking of launch ... credits list! As you may be aware, founders who purchased the Tr... |
| P4325 | 2016-03-25 | Week End Wrap Up - March 25, 2.. | client-only / no server rule | feedback thread / announcement / chatter | Big huge thanks to everyone who's taken the time to enter details of bugs in the dgciss... |
| P4328 | 2016-03-25 | Week End Wrap Up - March 25, 2.. | client-only / no server rule | feedback thread / announcement / chatter | The Lumen Station sometimes requires two clicks to correctly select the new items you w... |
| P4335 | 2016-03-25 | Week End Wrap Up - March 25, 2.. | client-only / no server rule | feedback thread / announcement / chatter | Sometimes switching between camera modes while building will switch your #1-9 keys to a... |
| P4337 | 2016-03-25 | Week End Wrap Up - March 25, 2.. | client-only / no server rule | feedback thread / announcement / chatter | There are lots of other known issues that we're working on fixing, and you can find mor... |
| P4463 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | The loot tab seems to cause issues if you do a ton of looting without it being open, so... |
| P4474 | 2016-04-01 | Week End Wrap Up - April 1, 2016 | client-only / no server rule | feedback thread / announcement / chatter | Thanks again for the bugs reported in the dgcissuetracker (https://dgcissuetracker.com/... |
| P4542 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Using Ctrl-C to copy selections should no longer also switch your brush cursor to a sphere |
| P4584 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | Mastercraft Bracer / Gem Seeker's Brooch: not in the 0.589 item dump (re/out/item_names.tsv); no code | Gem Seeker's Brooch now detects all types of gem node |
| P4625 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | The bad words filter should now be less zealous about filtering terms like “linking and... |
| P4642 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | If you switch to the selection tool, go find the 1x1x1 selection block and apply anothe... |
| P4649 | 2016-04-15 | Week End Wrap Up - April 15, 2.. | client-only / no server rule | feedback thread / announcement / chatter | This week's community news was fairly quiet - we're guessing you're all busy building a... |
| P4659 | 2016-04-16 | Loading screen tips being upda.. | client-only / no server rule | feedback thread / announcement / chatter | Use the "C" key to switch from a Cube to a Sphere while using the Add / Delete / Heal o... |
| P4752 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Now triggers "surge" when hitting a frozen target (all frozen opponents hit when fully ... |
| P4759 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | No longer triggers "surge" |
| P4817 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | When you select a new material using the eyedropper tool, the materials palette should ... |
| P4822 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Linking and triggering links should no longer sometimes draw to a wrong destination whe... |
| P4823 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Cancelling tweak mode on a trigger area should no longer reset its shape |
| P4824 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Editing a trigger area should no longer cause the area to be duplicated |
| P4837 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Colonist Encounter Suit (Blue & Grey) is now actually blue & grey. |
| P4980 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | known-issue report (client bug) | If you have a filter on your materials palette and use the eyedropper to select a mater... |
| P5006 | 2016-04-30 | Week End Wrap Up - April 30 | client-only / no server rule | feedback thread / announcement / chatter | Found a bug? We are super busy trying to catch them all, but your information is super ... |
| P5013 | 2016-05-01 | wiki: Game Updates 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Weapons and Combat: major changes to the balance of weapons and attacks using them. In ... |
| P5058 | 2016-05-04 | [Feedback] Story Tools | FOLLOWED | EditableProperties 15 / 16 dice weights; Logic and Timer filters (StoryTools) | **Filter (Dice Roll): **Include random outcomes in your Links. |
| P5059 | 2016-05-04 | [Feedback] Story Tools | FOLLOWED | EditableProperties 15 / 16 dice weights; Logic and Timer filters (StoryTools) | **Filter (Logic):** Use basic logic to control outcomes in your Links. |
| P5060 | 2016-05-04 | [Feedback] Story Tools | FOLLOWED | EditableProperties 15 / 16 dice weights; Logic and Timer filters (StoryTools) | **Filter (Timer):** Delay the outcomes of your Links by a period of time. |
| P5068 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Cool idea, and thanks for trying to find a way to link it to existing props :) That sor... |
| P5069 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Can you clarify exactly what you mean here? Does it not have the Links available that y... |
| P5077 | 2016-05-04 | [Feedback] Story Tools | FOLLOWED | StoryTools.Spawn fires CreatureSpawns after the creature exists | The actual issue was that the "...on spawn" Links were being fired the very instant tha... |
| P5082 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Create Link (if available) |
| P5090 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Create Link is near the top to be convenient. |
| P5092 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Create Link (if available) |
| P5099 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Quick note to say that we completely agree on the Logic Filter. We've actually been wor... |
| P5103 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | I've seen a few people talk about the 20-sided die node (definitely a throwback to our ... |
| P5104 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | developer design discussion (not shipped in 0.589) | Links can't be dynamic in the way your describe (disappearing/being created on the fly ... |
| P5105 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | developer design discussion (not shipped in 0.589) | The maximum Links I'd allow on any object is 10, which is the maximum number of Links t... |
| P5106 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | We're unable to require every Link on a prop be created once, and I wouldn't want to ev... |
| P5107 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | developer design discussion (not shipped in 0.589) | With all that in mind, I could see potentially adding a new prop that is a fixed 6-side... |
| P5113 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | developer design discussion (not shipped in 0.589) | Conditionals like that ("when running Link, check IF X is true") are something we'd rea... |
| P5114 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | developer design discussion (not shipped in 0.589) | But it's unfortunately unlikely to happen soon, if at all. I spent a lot of time talkin... |
| P5133 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | developer design discussion (not shipped in 0.589) | While a Goal Node is active, it adds a "dummy" goal onto the Watch List of every player... |
| P5134 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | developer design discussion (not shipped in 0.589) | That means that this "dummy" Goal is actually tied to the Node, not the individual play... |
| P5135 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | developer design discussion (not shipped in 0.589) | When players leave the claim, their Goal entry on the Watch List disappears. The progre... |
| P5137 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | The more I think about it, the more I like the style you prefer (Create Link, then Lock... |
| P5138 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Relative to Create Link, how often do you use the Move/Send to Inventory/Delete command... |
| P5141 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | I also wanted to share some screenshots of the current Link order I'm testing based on ... |
| P5143 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | **Creature** (Notice "Open Settings" is below "Link" and both "Move" options) |
| P5146 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Ideally, I'd like to find a solution that doesn't require adding so many props. I just ... |
| P5148 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | developer design discussion (not shipped in 0.589) | Action: "...Randomly Activate 1 Outgoing Link on this node." |
| P5149 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | developer design discussion (not shipped in 0.589) | Event: "When this Link is randomly chosen..." |
| P5150 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | developer design discussion (not shipped in 0.589) | You could place this node down, create as many (or as few) outgoing links as you wanted... |
| P5151 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | developer design discussion (not shipped in 0.589) | This would let you simulate dice rolls of any size, without bloating the Action/Event L... |
| P5153 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | **Do you think this new Random Node would be useful?** |
| P5180 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | NOT BUILT | Outposts.CompleteChannels gives loot on every channel; no per-player per-crystal lockout → 6.25 | Thanks for all your feedback, Voidlust. I wanted to point out real quickly that Lost Cr... |
| P5195 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | NOT FOLLOWED | Creatures.Kill: RespawnAt = now + RespawnTime (3 min) for every creature, outpost garrisons and Elites included → 6.23 | Currently the respawn times are 8-10 minutes for non-elites and 15 minutes for elites. ... |
| P5209 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | That prop-swap solution is definitely the sort of thing we're looking into. There are j... |
| P5214 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | You Luminaries have made some incredible things with the Prop Linking (Linking & Trigge... |
| P5215 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | Our recent overhaul of the Prop Links system was aimed at making sure that all Prop Lin... |
| P5218 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | Is there any Link text that feels confusing or misleading? |
| P5219 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | Have you found any Links that seem to be bugged (i.e. you tell an emitter to turn on, b... |
| P5220 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | Are there Props that you expected to be able to Link, but you couldn't? (and vice versa) |
| P5222 | 2016-05-04 | [Feedback] Prop Linking | NOT FOLLOWED | StoryTools.namedKinds word match misses sconces (691, 692, 2096, 2204, 2581), Emissive Orbs/Cubes (2127-2142), Control Panels (1172, 1176, 1177) → 6.36 | The Round Control Panel should be Linkable since we did the first big pass on Linking P... |
| P5227 | 2016-05-04 | [Feedback] Prop Linking | NOT FOLLOWED | StoryTools.namedKinds word match misses sconces (691, 692, 2096, 2204, 2581), Emissive Orbs/Cubes (2127-2142), Control Panels (1172, 1176, 1177) → 6.36 | Just to make sure I understand, you're saying that currently sending the "...turn this ... |
| P5231 | 2016-05-04 | [Feedback] Prop Linking | FOLLOWED | EditableProperties 13 / 14 (loop default off, 1-600 s); TimerResets only on reset actions; PauseAndResetTimer | For the links themselves, I think "pause" is a better description of what's happening t... |
| P5232 | 2016-05-04 | [Feedback] Prop Linking | FOLLOWED | EditableProperties 13 / 14 (loop default off, 1-600 s); TimerResets only on reset actions; PauseAndResetTimer | I see what you're saying about "When this timer resets..." It actually triggers when th... |
| P5237 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | Awesome. Definitely let me know if that's still the case for you when you're able to lo... |
| P5248 | 2016-05-04 | [Feedback] Prop Linking | FOLLOWED | EditableProperties 13 / 14 (loop default off, 1-600 s); TimerResets only on reset actions; PauseAndResetTimer | Thanks for the feedback, Zaxzia! Can you clarify what situation requires multiple Links... |
| P5253 | 2016-05-04 | [Feedback] Prop Linking | NOT FOLLOWED | Creatures.SendSpawn flags every story creature interactable; ClickCreature ignores faction → 6.32 | The interaction prompt/highlight would only be hidden if there was no interaction (defa... |
| P5258 | 2016-05-06 | Week End Wrap Up - May 6 | client-only / no server rule | feedback thread / announcement / chatter | This seems like a great opportunity to remind everyone that we don't always know about ... |
| P5347 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | You can now switch to Orbit Cam while in Character Cam first person mode |
| P5350 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Rejoice! You can now resize trigger areas! Go into the area tool and click on the exist... |
| P5353 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Moving the cursor while panning the camera should no longer break links if your cursor ... |
| P5357 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Trigger Areas should no longer sometimes stop being clickable |
| P5358 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Switching cameras should no longer cause you to fall out of the air if you switch from ... |
| P5405 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Added a Help guide to the Landmark menu (also accessible with F12) which contains links... |
| P5421 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Addressed an issue where some zones could sometimes lose all mining resources. If you e... |
| P5423 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | known-issue report (client bug) | There are many other known issues in the dgcissuetracker (https://dgcissuetracker.com/i... |
| P5424 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | known-issue report (client bug) | If you have a filter on your materials palette and use the eyedropper to select a mater... |
| P5431 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Would it be more useful to have a node that's dependent on time of day? Such as an "On ... |
| P5451 | 2016-05-13 | Week End Wrap Up - 13 May 2016 | client-only / no server rule | feedback thread / announcement / chatter | And, if you haven't yet run through Castle Brück (https://forums.daybreakgames.com/land... |
| P5645 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | The Help window, accessed via F12, has been updated and now links to the text of all th... |
| P5668 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Unfortunately, it's going to take a lot of work to make the Linking List a part of the ... |
| P5669 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Now that we've fixed (most) of the bugs that were causing NPCs to spawn inside of terra... |
| P5701 | 2016-06-02 | Hotfix Notes - 2 June 2016 | client-only / no server rule | client UI / visual / bug fix | We are investigating the blending changes further today and will be discussing the best... |

### building (84)

| Id | Date | Thread | Class | Evidence | Text |
|---|---|---|---|---|---|
| P4144 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | New building designs are now created by using the + button on the designs tab. If you c... |
| P4151 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | You can also drag the selection volume around a group of props and then tint them all a... |
| P4157 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Designs and pasted volumes default to starting in Tweak Mode. Everything else defaults ... |
| P4158 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Toggle between Tweak Mode and Freeform Placement Mode with the T key or by using the UI... |
| P4163 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Templates.Starter castle designs; ItemUses 'Architecture Tools' design rewards | The old Starter Home templates (now known as Building Designs) have been removed; and r... |
| P4164 | 2016-03-21 | Landmark Update Notes - March 21 | SUPERSEDED | old starter designs on the Replicator → no longer listed (P4815, 26 Apr); client recipe data | The old starter designs (circles, lines, etc) are no longer granted to new characters b... |
| P4187 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | ItemUses design storage 6M → 48M | Pre- update Template Storage size: 6,000,000, max 24,000,000 |
| P4188 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | ItemUses design storage 6M → 48M | New Template Storage size: 6,000,000, max 48,000,000 |
| P4234 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Props and materials named for Workshop themes now have new names. |
| P4237 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Templates writes the 'counts against storage' flag (IsDesigner) | The Designs management window now has a column that clearly indicates if a design is co... |
| P4238 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Line tool cursor is always a minimum of 2x2 blocks in size, since 1x1 does not work. |
| P4245 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | Placing templates and pasted selections is not listing Mirror as an option; in the mean... |
| P4263 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | Water material has vertical streaks |
| P4268 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | Sometimes the tweak mode window doesn't go away after you finish placing something. |
| P4275 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | [Fixed] Creator names are not properly displaying on templates not owned by you persona... |
| P4276 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | [Fixed] Templates created prior to this update cannot currently be renamed. |
| P4446 | 2016-03-31 | Hotfix notes - April 1 2016 | SUPERSEDED | Teleport / Site Arrival Point replaced by the Starting Location (P4818, P5055, 26 Apr - 4 May): Claims.ArrivalOf / EnsureStartingLocation | Teleport Arrival Point is no longer visible in build site/design screenshots |
| P4447 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Designs created prior to wipe can now be renamed by the owner, although the new name ma... |
| P4453 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Designs created by a third party should now correctly display the name of the character... |
| P4476 | 2016-04-06 | Marketplace purchased prop re-.. | FOLLOWED | Props.PlaceDesignProp uses bag items without the recipe; skipped props listed (BuildingClipboard ZoneMissingPropList) | Related: we're aware there's a bug where placing a template does not automatically take... |
| P4541 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | The Paint tool now paints only the blocks you actually use it on |
| P4543 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | The area tool should no longer start in tweak mode |
| P4544 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Tweak mode window should no longer occasionally go missing after multiple pastes |
| P4545 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Tools should now work better on larger selections, although we continue to iterate and ... |
| P4547 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Smooth Tool can now be applied to selection volumes larger than 100 blocks on an axis |
| P4550 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Designs and paste selections no longer default to placing in tweak mode every time |
| P4553 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Tweak mode window should now appear above the Build Mode UI, rather than underneath |
| P4556 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | You can no longer use fly mode cameras on a build site that’s in Play mode |
| P4558 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | The selection volume tool now automatically stops at the edge of your build site |
| P4632 | 2016-04-12 | Update Notes - April 13, 2016 | NOT FOLLOWED | BuildingClipboard design paste checks only build permission → 6.45 | Added error messaging for attempts to place a Player Studio Design on a build site you ... |
| P4655 | 2016-04-16 | Loading screen tips being upda.. | client-only / no server rule | feedback thread / announcement / chatter | Holding F over the face of a selection volume will allow you to select the opposite face. |
| P4660 | 2016-04-16 | Loading screen tips being upda.. | client-only / no server rule | feedback thread / announcement / chatter | With a Paste volume active, press the "V" key to mirror it. |
| P4663 | 2016-04-16 | Loading screen tips being upda.. | client-only / no server rule | feedback thread / announcement / chatter | Want to save off one of your creations for use somewhere else? Use the Selection Tool a... |
| P4664 | 2016-04-16 | Loading screen tips being upda.. | client-only / no server rule | feedback thread / announcement / chatter | With a Selection Volume active, you can use Cut (CTRL + X) Copy (CTRL + C) and Paste (C... |
| P4809 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | access masks from the client (Claims PermissionUse / Plunder / Decorate / Build / Copy) | Changed ‘customer’ and ‘copy only’ permissions order in build site permissions. ‘Custom... |
| P4826 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Build mode should no longer occasionally allow activation of the hotkey bar when trying... |
| P4828 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Rotate is now the default mode when placing a design or paste selection |
| P4832 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | When placing a design the size of an entire build site, we now auto-position the design... |
| P4833 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Resizing your build tool cursor no longer refreshes build mode UI |
| P4958 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT BUILT | LoginHandler.HandleCharacterDeleteRequest only sets DeleteTime; no world clean-up (sites, friends, guilds, Special Access) → 6.42 | Deleting a character now automatically creates a Design for any build sites that charac... |
| P4984 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | known-issue report (client bug) | The new advanced tutorial "Building Block Basics" cannot be completed yet [fixed in 29/... |
| P4989 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | known-issue report (client bug) | Yes, we are still investigating some issues with the tools, especially line tool and sp... |
| P4994 | 2016-04-29 | Hotfix notes - 29 April 2016 | client-only / no server rule | client UI / visual / bug fix | "Building Block Basics" and the follow up "Modular Thinking" goal should now be complet... |
| P5015 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | A thread for your opinions about starter designs! |
| P5017 | 2016-05-04 | [Feedback] Starter Designs fee.. | FOLLOWED | Templates.Starter castle designs; ItemUses 'Architecture Tools' design rewards | We have stopped giving out all the old Designs to brand new characters, and want to rep... |
| P5018 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | Share Designs that are truly going to be useful tools to just about everyone |
| P5019 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | Avoid filling the Designs list with a huge number of separate entries - combine designs... |
| P5022 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | two Designs with sets of nested arcs odd/even (see pic below, and you can copy them fro... |
| P5030 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | If you already had the old "starter templates" set you'll still have them (now converte... |
| P5034 | 2016-05-04 | [Feedback] Starter Designs fee.. | FOLLOWED | Templates writes the 'counts against storage' flag (IsDesigner) | No. And you have a column in your design management window (L) now that tells you if a ... |
| P5039 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | Let's keep discussions of other topics like tutorial islands or swap meet islands to an... |
| P5043 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | Current list of planned starter templates: |
| P5049 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | columns based on arc templates |
| P5083 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Move (Tweak Mode) |
| P5088 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Everything that's not unique to the prop is always in a consistent block of options tha... |
| P5094 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Move (Tweak Mode) |
| P5108 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Does that seem like a better solution than what we have currently? How useful would it ... |
| P5140 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Thanks for all of the continued feedback here. I'll get a design in for the 6-sided dic... |
| P5345 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Selection volumes should no longer occasionally get “stuck” preventing anything else fr... |
| P5346 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Esc is now shown as an option for build mode tools, to make it clear how to cancel out ... |
| P5354 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Selection Tool should no longer appear to be constantly highlighted after using copy an... |
| P5355 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Improvements to LOD issues that could happen when copying and pasting a selection from ... |
| P5359 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | The starter castle courtyard design is now smaller in size |
| P5360 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Old “starter template” designs have been renamed, and are in the process of being updat... |
| P5361 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | Templates.Starter castle designs; ItemUses 'Architecture Tools' design rewards | If you haven’t already completed “Modular Thinking”, completion of this goal will now r... |
| P5427 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | known-issue report (client bug) | [Updated] Yes, we are still investigating some issues with the tools, although we did g... |
| P5429 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | known-issue report (client bug) | [Updated] Pasted selections that are first rotated in tweak mode do not undo correctly. |
| P5444 | 2016-05-13 | Week End Wrap Up - 13 May 2016 | client-only / no server rule | feedback thread / announcement / chatter | First, meet the newest Character to join the prop palette: |
| P5649 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Addressed an issue that could cause a crash when attempting to place a very large Design |
| P5651 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | Claims.BuildCheckIn / TerrainEdits.MayApply allow every point outside sites ('Outside claims: yes') → 6.46 | It is still possible via a bug to paste rotated block selections outside your claim bou... |
| P5652 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | When blocks are rotated in tweak mode, undo no longer works correctly |
| P5653 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | There are several known issues that are closely tied into the voxel engine, which we ar... |
| P5654 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Blending issues (an architectural material placed beside a terrain material should not ... |
| P5655 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Textures may have streaks or deep lines across them when smoothed |
| P5656 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | The line tool and circle tool no longer cleanly delete flat "cloth" blocks or curved su... |
| P5657 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Flat "cloth" blocks are no longer consistently visible unless placed adjacent to anothe... |
| P5682 | 2016-06-02 | Hotfix Notes - 2 June 2016 | client-only / no server rule | client UI / visual / bug fix | Two blending materials (the vast majority of these are the terrain materials you see ou... |
| P5683 | 2016-06-02 | Hotfix Notes - 2 June 2016 | client-only / no server rule | client UI / visual / bug fix | Two "architectural" materials (the vast majority of the materials you would normally bu... |
| P5684 | 2016-06-02 | Hotfix Notes - 2 June 2016 | client-only / no server rule | client UI / visual / bug fix | An architectural material placed beside a blending material will NOT blend together (th... |
| P5694 | 2016-06-02 | Hotfix Notes - 2 June 2016 | client-only / no server rule | client UI / visual / bug fix | what is the name of the material that seems to be acting wrong, and |
| P5697 | 2016-06-02 | Hotfix Notes - 2 June 2016 | client-only / no server rule | client UI / visual / bug fix | The rule is that materials can be flagged as either architectural, or not. Architectura... |
| P5698 | 2016-06-02 | Hotfix Notes - 2 June 2016 | client-only / no server rule | client UI / visual / bug fix | With the March update some changes to the rules were made (based on feedback from the H... |
| P5702 | 2016-06-02 | Hotfix Notes - 2 June 2016 | client-only / no server rule | client UI / visual / bug fix | These are unfortunately not quick or easy changes to make even if they turn out to be p... |
| P5704 | 2016-06-02 | Hotfix Notes - 2 June 2016 | client-only / no server rule | client UI / visual / bug fix | After extensive discussion and consideration of comments in this thread and elsewhere, ... |

### progression (82)

| Id | Date | Thread | Class | Evidence | Text |
|---|---|---|---|---|---|
| P4118 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | It’s very important to us that we ensure new players have a good experience starting th... |
| P4170 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | We’ve revised the list of Goals in your journal, and there are now more to do, with mor... |
| P4171 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | goal list from the live capture; dailies (DailyGoals.Assign); Achievements.RecipeLearned | Some Goals may be hidden surprises you’ll only find when you do something particular, s... |
| P4172 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | goal list from the live capture; dailies (DailyGoals.Assign); Achievements.RecipeLearned | There are also daily Goals which will be available each day if you want ideas on someth... |
| P4173 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Let us know what you think as you try these out. We’re looking forward to your feedback... |
| P4174 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | goal list, rewards and Luminary score from the live capture (Achievements.cs, DailyGoals.cs) | You now have a Luminary score indicating how many Goals you’ve achieved. You can see th... |
| P4236 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Dark Elf, Kerran, Ogre, Halas, Combine, and Takish Workshop winners should log in to fi... |
| P4252 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | [Fixed] You can only open the detail card for a Goal by clicking the header name, not b... |
| P4273 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | If you had previously hidden your Goals watchlist, you may need to re-open the window (... |
| P4327 | 2016-03-25 | Week End Wrap Up - March 25, 2.. | client-only / no server rule | feedback thread / announcement / chatter | Some Trailblazers reported their title on these forums showing as "founder" instead of ... |
| P4332 | 2016-03-25 | Week End Wrap Up - March 25, 2.. | FOLLOWED | 'That's My Jam!' is not in the goal capture (re/out/achievements.tsv) | "That's My Jam" cannot be completed at the moment. This achievement will be removed. |
| P4371 | 2016-03-31 | Hotfix notes - April 1 2016 | FOLLOWED | 'That's My Jam!' is not in the goal capture (re/out/achievements.tsv) | The goal "That's My Jam" has been removed, as it could not be completed |
| P4372 | 2016-03-31 | Hotfix notes - April 1 2016 | FOLLOWED | goal list, rewards and Luminary score from the live capture (Achievements.cs, DailyGoals.cs) | Weapons should no longer need to be equipped twice to complete the 'item equipped' obje... |
| P4373 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Players who bought the dungeon starter kit before the goal actually told them to, shoul... |
| P4374 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | "Characters in Combat" goal should now be correctly tracked after completing "Harvestin... |
| P4375 | 2016-03-31 | Hotfix notes - April 1 2016 | FOLLOWED | DailyGoals.Moved / Achievements.SiteVisited count arrival by any means | "Build Site Tourist" goal should now update correctly if you visit the sites via the Ga... |
| P4376 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Journal window now changes cursor correctly to indicate it can be moved |
| P4377 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | "Hooked on Build Site Likes" should now correctly register giving or receiving a Like |
| P4448 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Characters who won the "Kerran Workshop" competition now have the title "Architech of t... |
| P4559 | 2016-04-12 | Update Notes - April 13, 2016 | NOT FOLLOWED | Gathering.GiveHarvest shares items but Achievements / DailyGoals.Harvested run for the gatherer only; Creatures.Kill counts for the killer only → 6.55 | Daily Goals should now update harvesting goals for everyone who receives harvests, and ... |
| P4560 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Achievement category numbers should now display correctly in the Journal |
| P4561 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Goal objectives should no longer occasionally disappear from the Goal tracker window |
| P4562 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | The Goal tracking window is no longer cleared when the servers are brought down for an ... |
| P4563 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Goals watchlist now has a title |
| P4564 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Goals watchlist should no longer automatically open if you are not tracking anything |
| P4565 | 2016-04-12 | Update Notes - April 13, 2016 | FOLLOWED | DailyGoals.Moved / Achievements.SiteVisited count arrival by any means | You can now update Goals that require visiting build sites via Gallery teleportation as... |
| P4566 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | It should now be possible to complete the Goal "Build Site Tourist" |
| P4785 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Some images for goal cards have been updated, however this is still a work in progress ... |
| P4786 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | There are now many friendly first time event pop ups to explain things to new players. ... |
| P4787 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | goal list, rewards and Luminary score from the live capture (Achievements.cs, DailyGoals.cs) | Three more advanced tutorial goals have also been added to point at the crafting system... |
| P4788 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | goal list, rewards and Luminary score from the live capture (Achievements.cs, DailyGoals.cs) | The introductory tutorial goals have been revised for additional user-friendliness and ... |
| P4789 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | Templates.Delete: Starter Castle designs 'cannot delete' (resolves that part of 6.10) → 6.10 | Free tutorial designs cannot be deleted, so that the goals requiring them cannot become... |
| P4790 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Various typo corrections and text error fixes |
| P4791 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Goals that have sub-goals should now display the count correctly |
| P4792 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | Achievements.PropPlaced has no Starting Location objective | Placing the Starting Location prop no longer causes some unrelated prop placement goals... |
| P4793 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | goal list, rewards and Luminary score from the live capture (Achievements.cs, DailyGoals.cs) | Journal now displays a Luminary Score rather than a Founder Score |
| P4794 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Corrected an issue where completing an objective on a goal could cause it to complete m... |
| P4795 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | goal list from the live capture; dailies (DailyGoals.Assign); Achievements.RecipeLearned | “Really Ties the Room Together” should now correctly update (as should all other goals ... |
| P4796 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Pioneer landscapes are no longer referred to as islands |
| P4797 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | DailyGoals.Moved / Achievements.SiteVisited count arrival by any means | Build Site Tourist daily goal should now correctly complete |
| P4798 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.10 (rewards come from a live capture, Resources/PacketDumps/71-AchievementBase, date unknown) → 6.10 | Goals now exist for all recipes that can be discovered |
| P4799 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Recipe ingredients can now be tracked via the Journal; tracked ingredients will display... |
| P4800 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Art on the goal card frames should now be prettier and correspond to the rarity of the ... |
| P4801 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | fungus explosion path (Creatures.ThinkFungus) calls no Achievements.Killed / DailyGoals.Killed (part of 6.5) → 6.5 | Fiery Fungi should no longer update any kill goals |
| P4802 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | DailyGoals.Assign: new dailies each day, untouched ones dropped, ones with progress kept | Daily goals should now actually be repeatable |
| P5000 | 2016-04-29 | Hotfix notes - 29 April 2016 | client-only / no server rule | client UI / visual / bug fix | The stray test achievement that was showing up in your journal should now be banished b... |
| P5120 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Goal Title |
| P5121 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Goal Text |
| P5122 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Goal Count (number slider) |
| P5124 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | ...Activate this Goal |
| P5125 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | ...Deactivate this Goal |
| P5126 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | ...Reset this Goal |
| P5127 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | ...Add 1 to the Goal Count |
| P5128 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | ...Remove 1 from the Goal Count |
| P5129 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | When this Goal Activates... |
| P5130 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | When this Goal Deactivates... |
| P5131 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | When this Goal Resets... |
| P5132 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | When this Goal Completes... |
| P5316 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Many Goal card images have been updated in the Journal |
| P5317 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | "You Can Kill Those Things?" can now be completed once again |
| P5318 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | Trades.cs:275 Achievements.Traded | Goals requiring trades should now update |
| P5319 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | "So Emote-ional Right Now" now correctly references “Dancin’ in the Sleet” as a require... |
| P5320 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | “Cry Havoc” now suggests you practice using your weapon before entering play mode |
| P5321 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | DailyGoals.Assign: new dailies each day, untouched ones dropped, ones with progress kept | Repeatable daily achievements should now be repeatable |
| P5322 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Goals that reward Designs should now show their icons as green books when you examine t... |
| P5323 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Completed Goals should now show all the steps you completed, not just the first step |
| P5324 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Selection and paint steps have been reversed in "Building Block Basics" |
| P5325 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | DailyGoals.Moved / Achievements.SiteVisited count arrival by any means | Daily "Build Site Tourist" should now correctly update and finish for characters who ha... |
| P5326 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | DailyGoals.Assign: new dailies each day, untouched ones dropped, ones with progress kept | Daily goals with no progress will be now removed from your journal with each daily rese... |
| P5327 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT FOLLOWED | already TASKS 6.10 (rewards come from a live capture, Resources/PacketDumps/71-AchievementBase, date unknown) → 6.10 | [Updated] Several combat achievement armor recipe rewards have been replaced with new a... |
| P5328 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT FOLLOWED | not in TASKS 6.10 yet: H2-Ouch → Reinforced Coat of Crashing Waves (same capture caveat) → 6.10 | · The combat achievement H2-Ouch now grants the Reinforced Coat of Crashing Waves recip... |
| P5329 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT FOLLOWED | already TASKS 6.10 (rewards come from a live capture, Resources/PacketDumps/71-AchievementBase, date unknown) → 6.10 | · The combat achievement Eternal Night now grants the Silent Shadow's Cloak recipe inst... |
| P5330 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT FOLLOWED | already TASKS 6.10 (rewards come from a live capture, Resources/PacketDumps/71-AchievementBase, date unknown) → 6.10 | · The combat achievement Praise the Light now grants the Lightbringer's Oath recipe ins... |
| P5331 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT FOLLOWED | already TASKS 6.10 (rewards come from a live capture, Resources/PacketDumps/71-AchievementBase, date unknown) → 6.10 | · The combat achievement Assassin's Choice now grants the Desperado's Deadly Poncho rec... |
| P5332 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT FOLLOWED | already TASKS 6.10 (rewards come from a live capture, Resources/PacketDumps/71-AchievementBase, date unknown) → 6.10 | · The combat achievement 1.21 ...Something now grants the Overcharged Recon Gear recipe... |
| P5538 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | All Goal cards in the Journal should now have images |
| P5539 | 2016-05-24 | Update Notes - 25 May 2016 | FOLLOWED | DailyGoals.Assign: new dailies each day, untouched ones dropped, ones with progress kept | Daily goals with no progress are now removed from journal when the day ends (they will ... |
| P5540 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Added more first-time information windows that will pop up the first time you do an act... |
| P5541 | 2016-05-24 | Update Notes - 25 May 2016 | FOLLOWED | goal list, rewards and Luminary score from the live capture (Achievements.cs, DailyGoals.cs) | Tutorial goals have been rearranged somewhat, and updated to use the new Target Dummy f... |
| P5542 | 2016-05-24 | Update Notes - 25 May 2016 | FOLLOWED | goal list, rewards and Luminary score from the live capture (Achievements.cs, DailyGoals.cs) | Tutorial goals no longer require finding a Lost Crystal |
| P5543 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Addressed a performance issue when mining while the goal "Best Harvester EVAR!' is on w... |
| P5544 | 2016-05-24 | Update Notes - 25 May 2016 | FOLLOWED | Templates.Starter castle designs; ItemUses 'Architecture Tools' design rewards | An updated set of Architecture Tools is now rewarded upon completion of Modular Thinking |

### claims (80)

| Id | Date | Thread | Class | Evidence | Text |
|---|---|---|---|---|---|
| P4133 | 2016-03-21 | Landmark Update Notes - March 21 | NOT FOLLOWED | 7 / +1 per 2 login days / 90 (fixed 2026-10-15, forum 53943), design + expiry mail followed (SiteUpkeep, SiteContents.Pack); 'all resources' not returned (no voxel refund, props as items) → 6.39 | Build Site upkeep remains the same as before this update. A brand new Luminary on a bra... |
| P4134 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | pre-wipe carry-over (migration) | Luminaries who played before this update will maintain their previously accrued expiry ... |
| P4135 | 2016-03-21 | Landmark Update Notes - March 21 | NOT FOLLOWED | size 525×450 followed (Claims HalfSize 1050 / HalfHeight 900 dm); Claims.CanPlace / PlaceConfirm accept any height → 6.47 | Build Site size has been standardized, and we are using this update to test the new siz... |
| P4136 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | You should start with as many available Build Sites as you had purchased base claims (e... |
| P4137 | 2016-03-21 | Landmark Update Notes - March 21 | SUPERSEDED | Teleport / Site Arrival Point replaced by the Starting Location (P4818, P5055, 26 Apr - 4 May): Claims.ArrivalOf / EnsureStartingLocation | You can now set the Teleport Arrival Point on your site by moving the arrival point pro... |
| P4140 | 2016-03-21 | Landmark Update Notes - March 21 | NOT BUILT | Store.Gallery lists every site ('TODO: only published sites'); free-text search only; no comments, no leaderboard / rising stars, no Play Mode filter → 6.43 | You can now search for key words in the names of Build SItes in the Gallery (previously... |
| P4189 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Chests.MaxPerSite = 50 (PropLimits.cs) | You can place up to 50 chests on a claim. |
| P4251 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | Build Mode UI should turn off by default when you leave your build site; this isn't cur... |
| P4260 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | When placing a build site, using mousewheel to move it up and down also zooms camera view. |
| P4262 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | Build site time setting display resets in the build settings (although it does remember... |
| P4269 | 2016-03-21 | Landmark Update Notes - March 21 | SUPERSEDED | Teleport / Site Arrival Point replaced by the Starting Location (P4818, P5055, 26 Apr - 4 May): Claims.ArrivalOf / EnsureStartingLocation | [Fixed] The teleport arrival point defaults to the lower north west corner of your clai... |
| P4285 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | [Fixed] Some characters who had purchased claims from the marketplace were not receivin... |
| P4292 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | [NEW] If you have Root Build Site Expansion items and you consume them, the "build site... |
| P4293 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | This is a display issue only, you did actually unlock the additional build site and we'... |
| P4296 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | To clarify the questions about claiming underground: |
| P4298 | 2016-03-21 | Landmark Update Notes - March 21 | NOT FOLLOWED | size 525×450 followed (Claims HalfSize 1050 / HalfHeight 900 dm); Claims.CanPlace / PlaceConfirm accept any height → 6.47 | You cannot place your Build Site far from the surface in the underground. |
| P4301 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Claims.Delete allows deleting the Pioneer site (same landscape) | When you first create your character, you will be auto-placed on a Build Site on a Pion... |
| P4310 | 2016-03-24 | Hotfix notes - Friday March 25 | FOLLOWED | Pioneer.Settle auto-grants a site to new characters | All new characters created after this hotfix is live should correctly get autogranted c... |
| P4362 | 2016-03-31 | Hotfix notes - April 1 2016 | SUPERSEDED | Teleport / Site Arrival Point replaced by the Starting Location (P4818, P5055, 26 Apr - 4 May): Claims.ArrivalOf / EnsureStartingLocation | Teleport Arrival Point would sometimes disappear or reset position build sites; this sh... |
| P4363 | 2016-03-31 | Hotfix notes - April 1 2016 | SUPERSEDED | Teleport / Site Arrival Point replaced by the Starting Location (P4818, P5055, 26 Apr - 4 May): Claims.ArrivalOf / EnsureStartingLocation | When trying to place Teleport Arrival Point, you should no longer get an "unknown" message |
| P4364 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Build settings window now refers to "Game Time" and "Site Time" instead of island time ... |
| P4365 | 2016-03-31 | Hotfix notes - April 1 2016 | NOT FOLLOWED | SiteDesigns.Save copies no site screenshots → 6.39 | Auto-created build sites designs (from upkeep expiry) should save their screenshots fro... |
| P4366 | 2016-03-31 | Hotfix notes - April 1 2016 | SUPERSEDED | Teleport / Site Arrival Point replaced by the Starting Location (P4818, P5055, 26 Apr - 4 May): Claims.ArrivalOf / EnsureStartingLocation | Teleport Arrival Point should only be visible while in build mode, preventing visitors ... |
| P4367 | 2016-03-31 | Hotfix notes - April 1 2016 | SUPERSEDED | Teleport / Site Arrival Point replaced by the Starting Location (P4818, P5055, 26 Apr - 4 May): Claims.ArrivalOf / EnsureStartingLocation | When a build site is placed manually sinking into the terrain, the Teleport Arrival Poi... |
| P4368 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Build tools should function more reliably, even when far away from the player, and shou... |
| P4369 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Yellow and Red Caution Trim are once again available in the material palette |
| P4370 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | The world should no longer occasionally disappear as you move around in build mode |
| P4450 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | real-money store prices / Marketplace UI (the store is free here) | Base Build Site Expansion is no longer listed in the Marketplace twice |
| P4460 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | See the section about the character wipe; refunds will happen again with launch so you'... |
| P4468 | 2016-03-31 | Hotfix notes - April 1 2016 | SUPERSEDED | prop limit 2500 → 7000 → 100,000 (P5527, 25 May; TASKS 6.2) → 6.2 | Prop limits on claims have been increased! They were unintentionally too low. This has ... |
| P4471 | 2016-04-01 | Week End Wrap Up - April 1, 2016 | client-only / no server rule | real-money store prices / Marketplace UI (the store is free here) | You may have noticed that with today's update the price of Base Build Sites has been re... |
| P4549 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Permissions in the Site Management window should now display what levels they’re set to |
| P4640 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | No, this is not a known bug, or at least not one I have seen yet, and I've been doing a... |
| P4662 | 2016-04-16 | Loading screen tips being upda.. | client-only / no server rule | feedback thread / announcement / chatter | You can hide or show other players Build Site Placements in the Game Settings. |
| P4675 | 2016-04-21 | Showcase updates | client-only / no server rule | feedback thread / announcement / chatter | Looks like the slowness issues have been addressed, but currently, whichever one of the... |
| P4810 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | access masks from the client (Claims PermissionUse / Plunder / Decorate / Build / Copy) | Names of permission levels on your build site have been renamed for additional clarity |
| P4816 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | If you delete your build site while you are in flight mode, you no longer remain in fli... |
| P4820 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Build site management drop-down tags have been updated |
| P5011 | 2016-05-01 | wiki: Game Updates 2016 | FOLLOWED | Claims.DefaultAccess = 0x003 Play Around | Build Mode: various changes to Building Tools (too varied to list), changes to Build Si... |
| P5033 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | Will play around and see what arches look like made from the nested arcs. :) |
| P5035 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | I created a set of arches from the nested arcs, which you can also see on my (Domino) b... |
| P5038 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | Finally, also placed the angled lines (now painted slate) on the same build site. Nobod... |
| P5041 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | Both odd and even are on the claim. It's in the gallery under Domino. |
| P5051 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | I have updated the set of starter pillars on the claim with the set from Fredelas. |
| P5176 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | This one brings up a few questions/concerns/requests I've seen a lot in this thread so ... |
| P5182 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | FOLLOWED | sites may include surface caves (no cave rule in Claims.CanPlace) | Lots of good feedback in here, but I wanted to follow-up on this in particular. You sho... |
| P5183 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | Kind of a tangent, but would love to get feedback if the current max/min height variati... |
| P5207 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | Thanks for all the feedback, Zaxzia :) There are a lot of good ideas in there, and I wa... |
| P5210 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | Wow, that sounds like a nasty bug! Definitely let me know if it happens again or if you... |
| P5343 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT FOLLOWED | Claims.SetAdventureStatus accepts only the owner (OwnedClaim) → 6.44 | Builders with full (“ultimate power!”) permissions on another person’s build site can n... |
| P5351 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | You should no longer experience text lag when modifying a build site’s description or w... |
| P5411 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Typing behavior in build site description field should be improved |
| P5412 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT BUILT | LoginHandler.HandleCharacterDeleteRequest only sets DeleteTime; no world clean-up (sites, friends, guilds, Special Access) → 6.42 | Names of deleted characters no longer remain in the Special Access list of build site p... |
| P5420 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | mail / window texts are the client's (Claim.UpkeepMail*) | Updated text on the in-game mail about your build site upkeep |
| P5496 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Cursor position should no longer move if you delete in tweak mode and then undo |
| P5497 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Mirror Mode is once again available in Fine Tuning mode while Pasting |
| P5498 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Clicking the flying camera button in the UI should now work once again |
| P5499 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Resolved some remaining cases where paint tool and smooth tool could pass through shape... |
| P5500 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Camera/play mode toolbar should no longer vanish for non-site owners when play mode is ... |
| P5501 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | "Trigger Area Tool" is now called the “Linking Area Tool” since it’s used for linking |
| P5502 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Free Camera and Orbit Camera modes should now honor your Invert Vertical Look option in... |
| P5503 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Orbit cam can now be zoomed with mousewheel, consistent with other camera modes |
| P5504 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | The position of the smooth tool should no longer occasionally get stuck while dragging ... |
| P5505 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Build HUD icons should no longer occasionally stay in selected state |
| P5506 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Linking areas (trigger areas) in a saved Design should now display more clearly in the ... |
| P5507 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Builders List display (formerly "voxel vision") should once again be working, although ... |
| P5508 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Align to surface mode icon for props has been updated |
| P5509 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | The subtract mode icon for building tools has been updated |
| P5510 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Alt for duplicate should only show as a prop option while in Fine Tuning mode |
| P5511 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Tweak Mode has been renamed to Fine Tuning, to better describe its purpose |
| P5512 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | The advanced options section in the Fine Tuning mode window is now more clearly labeled |
| P5513 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | FOV sliders for the cameras were removed from the build mode settings and are now found... |
| P5514 | 2016-05-24 | Update Notes - 25 May 2016 | FOLLOWED | Claims.DefaultAccess = 0x003 Play Around | Build Site permissions should default to "Play Around" |
| P5515 | 2016-05-24 | Update Notes - 25 May 2016 | FOLLOWED | InteractionHandler: Props.MayUse needs Use (0x01) on sites; creatures (not props) stay usable | "No Touching" site permission level does now, in fact, entirely prevent touching – even... |
| P5516 | 2016-05-24 | Update Notes - 25 May 2016 | FOLLOWED | StoryTools.Add needs Props.CanEdit (owner or Let's Decorate 0x08) | "Play Around" permission no longer allows people to edit prop links |
| P5517 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Resolved an issue where some props would not paste in the correct position if voxels ar... |
| P5518 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | The icon for Select Furthest Face when using Select or Linking Area tool is now correct... |
| P5519 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | mail / window texts are the client's (Claim.UpkeepMail*) | Foreclosure text has been updated in the build site management window and notification ... |
| P5520 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Highfall Plaster Cracked will now blend once again |
| P5521 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Some unfinished test terrain materials had accidentally been added to the Highfall grou... |

### creatures (56)

| Id | Date | Thread | Class | Evidence | Text |
|---|---|---|---|---|---|
| P4148 | 2016-03-21 | Landmark Update Notes - March 21 | SUPERSEDED | creature limit 100 → 125 spawners (P5529, 25 May; TASKS 6.2); 50 chests: PropLimits / Chests.MaxPerSite 50 (followed) → 6.2 | The way we handle prop limits on claims has been changed. You should no longer be limit... |
| P4165 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | As a new Luminary, you start with a selection of props and creatures you can use on you... |
| P4166 | 2016-03-21 | Landmark Update Notes - March 21 | NOT BUILT | creature / prop recipes from fighting: Creatures.RollLoot has no recipe roll → 6.16 | As you travel the world harvesting, earning Goals, or fighting enemies, you’ll unlock r... |
| P4220 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Creatures are now available in your props list, and can be placed on build sites |
| P4221 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | placement cost = PropRecipe components (Props.RecipeCost, client tables) | Creatures require Monster Essence to place; this is obtained by normal activities in La... |
| P4222 | 2016-03-21 | Landmark Update Notes - March 21 | NOT BUILT | PropSettings settingsByKind['spawner'] = {18,19,31} omits EditableProperties 17 prop_npc_spawner_faction_select; SpawnStory never reads a faction → 6.30 | Creatures can be set to ally, neutral, or enemy faction |
| P4223 | 2016-03-21 | Landmark Update Notes - March 21 | NOT FOLLOWED | Creatures.Think: creatures only ever target players; ally (2) / neutral (1) just leave players alone → 6.20 | Claims with creatures on them can be set to Play Mode, meaning the creatures will come ... |
| P4224 | 2016-03-21 | Landmark Update Notes - March 21 | NOT BUILT | creature / prop recipes from fighting: Creatures.RollLoot has no recipe roll → 6.16 | You will start with a set of basic creatures, and will unlock more as you fight, comple... |
| P4225 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | A new set of scripting item props is also available in your props list, including a Tex... |
| P4226 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Creatures can also use the Linking and Triggering system to trigger events on their dea... |
| P4227 | 2016-03-21 | Landmark Update Notes - March 21 | NOT FOLLOWED | respawn settings 18/19 followed; creature name = prop definition name (StoryTools Running.Name), not the spawner's custom name → 6.30 | Right-click a creature to access more options such as the creature name and respawn rate. |
| P4228 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | See creature preview post for more (https://forums.daybreakgames.com/landmark/index.php... |
| P4248 | 2016-03-21 | Landmark Update Notes - March 21 | NOT FOLLOWED | Claims.cs: 'Sites in Play mode ... Not persisted: sites start in Edit' — a restart stops every site's story → 6.56 | [Fixed] On a server reset, pathing creatures on build sites will not start pathing agai... |
| P4249 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | Creatures that chase someone off a build site are not always consistent about returning. |
| P4259 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | NPCs don't always aggro the nearest person on a claim, but may decide to attack anyone ... |
| P4265 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | We do intend to have a text prop that can be hooked up to Creatures on your claims, but... |
| P4280 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | [Fixed] Setup menu not appearing for creatures |
| P4300 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | no caverns are used on Pioneer zones (caverns are the home continent's .cpa) | There are isolated caverns in the underground of the Pioneer Landscapes, but (like the ... |
| P4309 | 2016-03-24 | Hotfix notes - Friday March 25 | client-only / no server rule | client UI / visual / bug fix | The 'setup' option on Creatures will now show up for everyone |
| P4321 | 2016-03-25 | Week End Wrap Up - March 25, 2.. | client-only / no server rule | feedback thread / announcement / chatter | We also did a livestream on Tuesday, which included tons of Q&A for the questions you a... |
| P4331 | 2016-03-25 | Week End Wrap Up - March 25, 2.. | client-only / no server rule | feedback thread / announcement / chatter | Skeleton outfits from the Halloween Party event last year were not re-granted after the... |
| P4387 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Halloween skeleton outfits should now be in your inventory if you had one before the wipe |
| P4438 | 2016-03-31 | Hotfix notes - April 1 2016 | NOT FOLLOWED | already TASKS 6.3 (no session pacing anywhere; RollDrops ignores stat 17; plain digging 0.3 % lumens, no recipes) → 6.3 | Recipes drops for props and spawners have similar safeguards in place, and are also mor... |
| P4456 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Some ruins may be missing monsters and a Lost Crystal. We are investigating this. |
| P4457 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | The Skeleton Suit (at least in some colors) seems to have the incorrect icon. |
| P4466 | 2016-03-31 | Hotfix notes - April 1 2016 | NOT FOLLOWED | Props.SetTint tints any owned prop, spawners included → 6.31 | The color of many of the creatures is an indicator of what type of damage they do. That... |
| P4472 | 2016-04-01 | Week End Wrap Up - April 1, 2016 | client-only / no server rule | feedback thread / announcement / chatter | With today's hotfix you should also find any missing Skeleton Suits are now in your inv... |
| P4594 | 2016-04-12 | Update Notes - April 13, 2016 | NOT FOLLOWED | already TASKS 6.5 (no campfire drops; fungus always explodes on a hit, terrain loot only) → 6.5 | Adjusted drop rates and details for world NPCs, including that NPCs now have a chance t... |
| P4626 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Several icons have been corrected, including the bat colony emitter and skeleton suits |
| P4681 | 2016-04-22 | Week End Wrap Up - April 22, 2.. | client-only / no server rule | feedback thread / announcement / chatter | Here at Landmark HQ we've had a super busy week. We've been a little quiet, but that's ... |
| P4776 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT BUILT | essence drops are flat chances (Gathering.EssenceChance 0.03, creature loot tables); no session curve → 6.17 | You now earn fewer essences (monster essence, adventure essence, fire essence, etc.) at... |
| P4819 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Cancelling placement of a creature by hitting Esc no longer incorrectly counts that unp... |
| P4983 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | known-issue report (client bug) | Creatures may fail to get aggro if they are spawned in the air |
| P4992 | 2016-04-29 | Hotfix notes - 29 April 2016 | client-only / no server rule | client UI / visual / bug fix | Fixed an issue where sometimes creatures on your claim could not be moved after placement |
| P5002 | 2016-04-30 | Week End Wrap Up - April 30 | client-only / no server rule | feedback thread / announcement / chatter | Lots of new stuff this week as you've probably noticed. We had a big huge hotfix on Wed... |
| P5012 | 2016-05-01 | wiki: Game Updates 2016 | FOLLOWED | Outposts.UseCrystal: 'dormant' until the Elite is dead (EliteDied / EliteBack) | Outposts: Elite Monsters were added/must be defeated to activate Lost Crystals |
| P5057 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | **Travel Point:** Have your creatures walk to specific locations |
| P5102 | 2016-05-04 | [Feedback] Story Tools | NOT FOLLOWED | Creatures.SendSpawn flags every story creature interactable; ClickCreature ignores faction → 6.32 | As of yesterday's patch, the interaction highlight/prompt should now only be displayed ... |
| P5136 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Cool. Thanks for the quickl reply! If we did something like this, I'd probably want to ... |
| P5155 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | So, you've braved the dangers of the dark unknown. You've battled foes of all types in ... |
| P5166 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | What bugs have you found on specific Outposts? (Please include the Outpost name, creatu... |
| P5171 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | What contributes most to making it too difficult? The number of monsters, the power lev... |
| P5178 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | NOT FOLLOWED | Creatures.Kill: RespawnAt = now + RespawnTime (3 min) for every creature, outpost garrisons and Elites included → 6.23 | Related to the above, because it's open world Outpost NPCs you kill do have to respawn ... |
| P5191 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | To everyone: please let me know exactly which Outposts or which areas/NPCs of certain O... |
| P5193 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | NOT FOLLOWED | Creatures.Kill: RespawnAt = now + RespawnTime (3 min) for every creature, outpost garrisons and Elites included → 6.23 | Please let me know the name of the Outpost and creature if you run into instantly respa... |
| P5197 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | NOT FOLLOWED | Creatures.Kill: RespawnAt = now + RespawnTime (3 min) for every creature, outpost garrisons and Elites included → 6.23 | Creatures have to respawn eventually because the caves are in the open world (not insta... |
| P5202 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | I hate that too, but it's proven to be an extremely difficult bug to eliminate. Hopeful... |
| P5225 | 2016-05-04 | [Feedback] Prop Linking | NOT FOLLOWED | Creatures.Think Idle: a story WalkTo comes first and 'ignores players on the way' → 6.20 | Yeah, Combat AI has the highest priority on all creatures, so you won't be able to tell... |
| P5252 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | We did that with NPCs in this patch. I'll talk with the engineers and see how viable it... |
| P5410 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Creatures should now appear in screenshots of Designs |
| P5432 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | We have a late addition to the patch notes! Seems a new Creature prop snuck in there wi... |
| P5433 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT BUILT | prop 2890 Target Dummy is a static prop (not a spawner to StoryTools.KindOf); only Achievements.cs:645 knows it → 6.28 | Players can now place a target dummy NPC on their claim to practice combat with. This N... |
| P5445 | 2016-05-13 | Week End Wrap Up - 13 May 2016 | NOT BUILT | prop 2890 Target Dummy is a static prop (not a spawner to StoryTools.KindOf); only Achievements.cs:645 knows it → 6.28 | This fella is intended to appear in the newbie tutorial goal, but he's available to use... |
| P5454 | 2016-05-20 | Week End Wrap Up - 20 May 2016 | client-only / no server rule | feedback thread / announcement / chatter | This post is a little late, because where did the week go and how is it Friday already?... |
| P5628 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | Elite loot = SpawnerKind generic (copper 1-3, Adventure Essence 0-1); crystal / chest loot is ours → 6.24 | Lost Crystals, Lost Treasure Chests (the hidden treasure chests that can be found on ou... |
| P5666 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Constructive balance suggestions will be welcome after folks try them out and see, we a... |

### outposts (50)

| Id | Date | Thread | Class | Evidence | Text |
|---|---|---|---|---|---|
| P4116 | 2016-03-21 | Landmark Update Notes - March 21 | NOT FOLLOWED | Pioneer.Place tries Pioneer surfaces only, else no site → 6.48 | New characters will be automatically granted a Building Site, with priority on a Pionee... |
| P4257 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | Some caverns lack prop decorations |
| P4299 | 2016-03-21 | Landmark Update Notes - March 21 | NOT FOLLOWED | Travel.Handle RandomUnderground → Caves.TravelRandom from any landscape, Pioneer included → 6.27 | There are no Chaos Caverns or other content-focused underground areas on Pioneer Landsc... |
| P4322 | 2016-03-25 | Week End Wrap Up - March 25, 2.. | client-only / no server rule | feedback thread / announcement / chatter | One oft-repeated question that we meant to cover and then totally overlooked during the... |
| P4631 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Stalagmites in the Chaos Caverns should no longer sometimes poke through the floor of a... |
| P4636 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | known-issue report (client bug) | We still have some Chaos Caverns without ruins/lost crystals. Continuing to work on this. |
| P4803 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | You didn’t expect something named “Chaos Caverns” to stay the same forever, did you? A ... |
| P4804 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | **New Enemies: **Many factions have sent excursions down into the Chaos Caverns and hav... |
| P4805 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | **New Outposts: **We’ve done a full pass through the existing Outposts (previously call... |
| P4806 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | every outpost has an Elite and a Lost Chest (followed) but the chest is always at one spot (Outposts.cs:240) → 6.24 | **New Challenges:** Every Outpost will have at least one Elite enemy on it, and a secre... |
| P4807 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | **New Caverns:** The worlds have shifted as a part of this massive Chaos Cavern shakeup... |
| P4808 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.8 (one ruin in the largest caverns only); iron on the surface: client vein data → 6.8 | **Quality-of-Life Updates:** We’ve made several changes to make life a little better fo... |
| P4964 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | There should no longer be caverns so low that their floor is lava (even though this was... |
| P4971 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | one ruin per cavern (Outposts byCavern) | More than one copy of the same outpost should no longer appear in the same cavern |
| P5014 | 2016-05-01 | wiki: Game Updates 2016 | NOT FOLLOWED | Elite loot = SpawnerKind generic (copper 1-3, Adventure Essence 0-1); crystal / chest loot is ours → 6.24 | Increased loot from all lootable aspects of Outposts, increased drop rate for Recipes |
| P5157 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | We've made some huge changes to the Chaos Caverns recently and especially the Outposts ... |
| P5158 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | NOT FOLLOWED | Travel.Handle RandomUnderground → Caves.TravelRandom from any landscape, Pioneer included → 6.27 | As a reminder, you can travel to a Chaos Cavern any time by selecting "An Underground A... |
| P5159 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | Please use this thread for any and all feedback related to Chaos Caverns and Outposts. |
| P5161 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | Did you ever get frustrated and stop having fun while exploring Chaos Caverns? What cau... |
| P5162 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | What are your favorite Outposts to discover and play through? |
| P5163 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | What are your least favorite Outposts to discover and play through? |
| P5164 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | In general, do you feel like you can beat Outposts without other players? |
| P5165 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | In general, do you feel like Outposts still provide a fun challenge when playing with a... |
| P5167 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | What general bugs have you found in Chaos Caverns that are not unique to specific Outpo... |
| P5170 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | Do you have difficulty getting through the cave networks solo, or just the Outposts the... |
| P5172 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | Have you found any Outposts that you could solo? |
| P5174 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | Definitely agree that hidden chests on Outposts really aren't worth the hassle currentl... |
| P5175 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | We're still working out the details, so I don't want to tease too much, but we'll defin... |
| P5177 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | **Progressive difficulty as you proceed through Caverns:** Right now, all of the Chaos ... |
| P5181 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | NOT BUILT | Outposts.CompleteChannels gives loot on every channel; no per-player per-crystal lockout → 6.25 | Right now, when you use the Lost Crystal while it's on Lockout, it should just show the... |
| P5184 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | Thanks for the feedback, Tazha. In the future, if you come across an Outpost that you t... |
| P5188 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | NOT BUILT | Caves.TravelRandom moves only the requester; no group offer → 6.26 | You should never be forced to leave a Chaos Cavern just because someone else is leaving... |
| P5189 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | Wait until you see the new Outpost chests we're working on for the next big update (kno... |
| P5192 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | Thanks for all your feedback, Myhrea :) I'm glad you're enjoying the Outposts. |
| P5198 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | FOLLOWED | OreVeins/VeinGenerator use the 0.589 (4 Jun 2016) vein data: DENSITY_IN_CAVES, counts per pool | We're significantly increasing the mineral veins in Chaos Caverns in (hopefully) the ne... |
| P5201 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | Treasure chests on Outposts will give a lot more loot and be a lot more unique-looking ... |
| P5203 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | NOT FOLLOWED | Outposts.NextCave picks a random outpost per player → 6.26 | That's a great idea! There's actually a technical limitation here currently. I'll spare... |
| P5206 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | We're definitely looking at how we can repurpose the old underground Layer system to wo... |
| P5333 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Props in Outposts (previously named Ruins) should no longer duplicate or shift location... |
| P5334 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Props in Outposts should no longer be placed at incorrect heights |
| P5335 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | All Outposts should now have props as expected |
| P5336 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Snow patches are no longer occasionally found in graveyards in desert caverns |
| P5337 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Several of the above have been recurring issues, so if you see any of these still occur... |
| P5545 | 2016-05-24 | Update Notes - 25 May 2016 | FOLLOWED | OreVeins/VeinGenerator use the 0.589 (4 Jun 2016) vein data: DENSITY_IN_CAVES, counts per pool | Underground mining veins should now be larger and more frequent |
| P5546 | 2016-05-24 | Update Notes - 25 May 2016 | FOLLOWED | Outposts.UseCrystal: 'dormant' until the Elite is dead (EliteDied / EliteBack) | Each Outpost now has an Elite boss, who must be killed in order to activate the Lost Cr... |
| P5547 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Outposts should no longer occasionally have cave stalagmites sticking up through their ... |
| P5548 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Corrected an issue where spawners on Outposts were appearing at incorrect heights |
| P5549 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | We believe we've fixed the issues causing Outposts to sometimes have no props, or no bl... |
| P5696 | 2016-06-02 | Hotfix Notes - 2 June 2016 | client-only / no server rule | client UI / visual / bug fix | Example: Desert Cavern Ceiling, wall, and floor are not currently blending, but used to. |
| P5699 | 2016-06-02 | Hotfix Notes - 2 June 2016 | client-only / no server rule | client UI / visual / bug fix | As far as we can tell, that's now a consistent rule that all materials are following. H... |

### social (40)

| Id | Date | Thread | Class | Evidence | Text |
|---|---|---|---|---|---|
| P4168 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | As you receive loot, you’ll notice it now displays briefly in the upper left of your sc... |
| P4232 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | You should notice improvements to the chat windows; direct tell messages sent to you wi... |
| P4253 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | [Fixed] Text of names in the friends list gets smaller if you click on it or mouse over... |
| P4287 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | [Fixed] "General chat" channel wasn't working for some people. |
| P4311 | 2016-03-24 | Hotfix notes - Friday March 25 | client-only / no server rule | client UI / visual / bug fix | General chat channel will now work for everyone, not just admins or the first person in |
| P4624 | 2016-04-12 | Update Notes - April 13, 2016 | NOT BUILT | LoginHandler.HandleCharacterDeleteRequest only sets DeleteTime; no world clean-up (sites, friends, guilds, Special Access) → 6.42 | Deleted characters will now be correctly removed from friend and guild lists |
| P4648 | 2016-04-15 | Week End Wrap Up - April 15, 2.. | client-only / no server rule | feedback thread / announcement / chatter | If you find some steps to reliably get this to happen, please do let us know! (You can ... |
| P4653 | 2016-04-16 | Loading screen tips being upda.. | client-only / no server rule | feedback thread / announcement / chatter | This weekend I'm updating the helpful tips you see on the loading screen. These need to... |
| P4667 | 2016-04-21 | Showcase updates | client-only / no server rule | feedback thread / announcement / chatter | If you're in game or looking at our website right now you may notice that the Showcase ... |
| P4687 | 2016-04-22 | Week End Wrap Up - April 22, 2.. | client-only / no server rule | feedback thread / announcement / chatter | Unfortunately we did introduce another bug at the same time. We already made a post abo... |
| P4904 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | Friends.HandleAdd takes a name | You can now use /friend [name] to add a character called [name] to your friends list. (... |
| P4905 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | per-world processes (TASKS 7 cross-world services) → 7 | Guild list should now correctly display a new guild member’s location even if they are ... |
| P4906 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | per-world processes (TASKS 7 cross-world services) → 7 | You no longer have to be on the same world to remove a group member from your group |
| P4907 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | PlayerManager.Enter returns early on re-zones, so Friends.Enter runs once per login | You should no longer see the ‘offline/online’ message whenever a friend or guildmate te... |
| P4908 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Chat channels now display in different colors |
| P4909 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Guild window should now be prettier |
| P5032 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | Sounds like the angles should be included - is there a better, newer set out there, or ... |
| P5042 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | Please read the later posts and screenshots. Samples of pre-made columns have been crea... |
| P5109 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | I couldn't reproduce the first two bugs. The first one may have been fixed in yesterday... |
| P5152 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | It seems to me like a pretty elegant solution for using the tech we have to give you lo... |
| P5156 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | What's next for an unstoppable spelunker like yourself? Feedback, of course! |
| P5196 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | Couple follow-up questions for you (like I've posed to others earlier in this thread): |
| P5229 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | Hey Jadam, Can you clarify exactly what you'd like to see here? |
| P5259 | 2016-05-06 | Week End Wrap Up - May 6 | client-only / no server rule | feedback thread / announcement / chatter | If you're looking for other ways to help out, we also posted several new forum threads ... |
| P5310 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | General Chat has its own tab again, and it can be disabled in the Chat Options menu. Ge... |
| P5311 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT BUILT | LoginHandler.HandleCharacterDeleteRequest only sets DeleteTime; no world clean-up (sites, friends, guilds, Special Access) → 6.42 | Officer and Guild Chat channels should no longer remain after deleting and recreating a... |
| P5312 | 2016-05-11 | Hotfix Notes - 11 May 2016 | NOT BUILT | LoginHandler.HandleCharacterDeleteRequest only sets DeleteTime; no world clean-up (sites, friends, guilds, Special Access) → 6.42 | Guild News should now persist the name of players who are deleted |
| P5313 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Chat should now be cleared when a player logs out |
| P5315 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | If you speak in chat using /say the next message should also default to /say |
| P5425 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | known-issue report (client bug) | Paint tool can still sometimes paint through solid walls if held down, and painting a c... |
| P5469 | 2016-05-23 | [Feedback] Excessively long lo.. | client-only / no server rule | feedback thread / announcement / chatter | EDIT - many thanks to all who filled out the survey! Thanks to your help, we've tracked... |
| P5472 | 2016-05-23 | [Feedback] Excessively long lo.. | client-only / no server rule | feedback thread / announcement / chatter | Hi folks, thanks for all the data you entered! Thanks to your help, we've tracked down ... |
| P5620 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | The default image for Player Studio items missing an image has been updated |
| P5621 | 2016-05-24 | Update Notes - 25 May 2016 | FOLLOWED | Ratings.Total: lifetime votes | [Updated - still appear to be some issues with this] Likes on Build Sites no longer dec... |
| P5622 | 2016-05-24 | Update Notes - 25 May 2016 | FOLLOWED | Ratings.Total: lifetime votes | The above should also resolve the issue where Likes count on claims did not always matc... |
| P5623 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Searching by category in Player Studio items has been improved |
| P5624 | 2016-05-24 | Update Notes - 25 May 2016 | NOT BUILT | Store.Gallery lists every site ('TODO: only published sites'); free-text search only; no comments, no leaderboard / rising stars, no Play Mode filter → 6.43 | You can now filter Build Sites by Play Mode status in the Gallery |
| P5625 | 2016-05-24 | Update Notes - 25 May 2016 | FOLLOWED | Commerce.Decide mails MailAccepted / MailRejected (client writes the text) | Player Studio successful approval mail should now correctly contain text |
| P5685 | 2016-06-02 | Hotfix Notes - 2 June 2016 | client-only / no server rule | client UI / visual / bug fix | This should address the issues in bugs like these (thanks for the detailed reports!): |
| P5705 | 2016-06-03 | Week End Wrap Up - 3 June 2016 | client-only / no server rule | feedback thread / announcement / chatter | Feels like it's been a short week, and it actually has, because Monday was a public hol... |

### equipment (39)

| Id | Date | Thread | Class | Evidence | Text |
|---|---|---|---|---|---|
| P4101 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | If you are entering a bug, the MOST useful information you can give us is how to make i... |
| P4114 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Any founder’s pack items and any marketplace purchases you made during beta will be gra... |
| P4141 | 2016-03-21 | Landmark Update Notes - March 21 | NOT FOLLOWED | Claims.BuildCheckIn / TerrainEdits.MayApply allow every point outside sites ('Outside claims: yes') → 6.46 | We have a new Build Mode UI! This is accessed via the menu at the top right, or by pres... |
| P4233 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | no head / hand items in the 0.589 item data | Head and hand slot equipment is no longer available |
| P4264 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | Head and hand slots are still shown in the character window even though there is no equ... |
| P4304 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Shameless plus: We'll be answering this question (and lots of others) on our livestream... |
| P4329 | 2016-03-25 | Week End Wrap Up - March 25, 2.. | SUPERSEDED | Teleport / Site Arrival Point replaced by the Starting Location (P4818, P5055, 26 Apr - 4 May): Claims.ArrivalOf / EnsureStartingLocation | Teleport arrival points are sometimes disappearing from claims. We think this is relate... |
| P4385 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Gunslinger, Wandering Nomad, and Cowboy's Gear outfits should now be the correct colors... |
| P4386 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Gunslinger's Outfit has been renamed to Desperado's Outfit, and Cowboy's Gear is now re... |
| P4389 | 2016-03-31 | Hotfix notes - April 1 2016 | NOT FOLLOWED | Chop ignores Harvest Bonus (stat 17); Mine applies it (Abilities.cs:715) → 6.14 | Fixed an issue where items that granted harvesting bonus (such as the NovaTech Harvestr... |
| P4390 | 2016-03-31 | Hotfix notes - April 1 2016 | NOT FOLLOWED | already TASKS 6.3 (no session pacing anywhere; RollDrops ignores stat 17; plain digging 0.3 % lumens, no recipes) → 6.3 | Fixed an issue where items that granted harvesting bonus (such as the NovaTech Harvestr... |
| P4436 | 2016-03-31 | Hotfix notes - April 1 2016 | FOLLOWED | goal list, rewards and Luminary score from the live capture (Achievements.cs, DailyGoals.cs) | In general during a play session you now get more lumens from every day activity drops ... |
| P4470 | 2016-04-01 | Week End Wrap Up - April 1, 2016 | client-only / no server rule | feedback thread / announcement / chatter | Besides the hotfix, this week included April Fool's Day. Everyone who has logged into L... |
| P4475 | 2016-04-06 | Marketplace purchased prop re-.. | client-only / no server rule | feedback thread / announcement / chatter | Just an FYI, if you previously purchased marketplace bundles of some props (full list b... |
| P4644 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | We just pushed a small fix out this afternoon without requiring down time. If you are a... |
| P4650 | 2016-04-15 | Week End Wrap Up - April 15, 2.. | client-only / no server rule | feedback thread / announcement / chatter | Reminder to all Trailblazers: with launch announced for this spring, time's getting sho... |
| P4658 | 2016-04-16 | Loading screen tips being upda.. | client-only / no server rule | feedback thread / announcement / chatter | Altering the end cursor of the line tool will allow you to make interesting angles and ... |
| P4724 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Allows slightly faster movement during use |
| P4727 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Allows slightly faster movement during use |
| P4756 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Has slower movement during use |
| P4762 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.9 (per-weapon numbers are ours: WeaponCombat.cs) → 6.9 | Has slower movement during use |
| P4812 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Fixed an issue where you might not be able to drag the corners of the selection volume ... |
| P4814 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Toggling in and out of build mode no longer changes your camera mode. However, entering... |
| P4827 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Right-click details should no longer get ‘stuck’ when hovering the mouse over designs |
| P4839 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Wandering Nomad outfit tints should now consistently match their names. |
| P4840 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | Inventory.Fits / ToggleEquip check only the slot → 6.54 | Multiple copies of the Ring of Bounty can no longer be equipped at the same time |
| P5052 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | We're trying to find a balance between "here's a few useful tools to get you started" a... |
| P5064 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | What ordering/naming/cosmetic changes can we do to make them easier to use and understand? |
| P5169 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | Thanks for the feedback! Would you mind answering a few extra questions to help me pinp... |
| P5234 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | We'd like to find technical solutions for the others things you mention, like making th... |
| P5236 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | We'd like to find technical solutions for the others things you mention, like making th... |
| P5251 | 2016-05-04 | [Feedback] Prop Linking | client-only / no server rule | feedback thread / announcement / chatter | It's a technical concern, so we're at the mercy of engineers monitoring server performa... |
| P5263 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Landmark servers are down for a hotfix starting at 3am pacific time, Wednesday May 11th... |
| P5407 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Ambient audio should "mute" during loading screens |
| P5453 | 2016-05-13 | Week End Wrap Up - 13 May 2016 | client-only / no server rule | feedback thread / announcement / chatter | Happy exploring, and we'll see you in Landmark! :) |
| P5458 | 2016-05-20 | Week End Wrap Up - 20 May 2016 | client-only / no server rule | feedback thread / announcement / chatter | To wrap up for this week, how about another reminder that if you're a Trailblazer, chec... |
| P5470 | 2016-05-23 | [Feedback] Excessively long lo.. | client-only / no server rule | feedback thread / announcement / chatter | If you are experiencing the long load times issue, please help us by answering a quick ... |
| P5627 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.3 (no session pacing anywhere; RollDrops ignores stat 17; plain digging 0.3 % lumens, no recipes) → 6.3 | Fixed an issue that caused some players to receive far fewer recipes than intended duri... |
| P5711 | 2016-06-03 | Week End Wrap Up - 3 June 2016 | client-only / no server rule | feedback thread / announcement / chatter | on Wednesday morning, barring unforseen issues, Trailblazers' early access will start; |

### crafting (39)

| Id | Date | Thread | Class | Evidence | Text |
|---|---|---|---|---|---|
| P4167 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | You’ll also earn lumens through regular activities every day, which you can use at the ... |
| P4175 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Crafting.cs: Replicator stations; Lumen Station 55 opened from the UI (Crafting.UiStations) | Crafting is now done at the Replicator, which can be found at each portal spire, or pla... |
| P4176 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Crafting.cs: Replicator stations; Lumen Station 55 opened from the UI (Crafting.UiStations) | The Replicator is where you can use resources to create new equipment, tools, potions, ... |
| P4177 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Crafting.cs: Replicator stations; Lumen Station 55 opened from the UI (Crafting.UiStations) | There is also a Lumen Station at each portal spire, and which can also be accessed from... |
| P4178 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | The Lumen Station also sells many things that were previously available in the Marketpl... |
| P4179 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | You may notice some things you can craft on the Replicator can also be obtained from th... |
| P4180 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | client Recipes / PropRecipe tables of the 0.589 build (costs, default-learned flags, Lumen Station list) | In the case of some items, what you are purchasing at the Lumen Station is the recipe, ... |
| P4229 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | client Recipes / PropRecipe tables of the 0.589 build (costs, default-learned flags, Lumen Station list) | Previous types of crafting station that are no longer used are now available as decorat... |
| P4281 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Pioneer.Settle auto-grants a site to new characters | [Fixed] Some characters were not being auto-granted newbie claims, and there was no cla... |
| P4282 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Build Site Flag item / Replicator recipe (client data, Claims.BuildSiteFlagItem) | If your character was created before this fix went in (3/25), you can now craft a claim... |
| P4284 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | If you're looking for your trailblazer vault or flags, these can be found in the props ... |
| P4289 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | [New - corrected info] If you had a Mega Pocket (part of the Settlers and Trailblazers ... |
| P4307 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | [New - corrected info] If you had a Mega Pocket (part of the Settlers and Trailblazers ... |
| P4314 | 2016-03-24 | Hotfix notes - Friday March 25 | client-only / no server rule | client UI / visual / bug fix | Corrected some incorrect information about inventory slots that was posted in the updat... |
| P4343 | 2016-03-28 | Additional lumen re-grants | client-only / no server rule | feedback thread / announcement / chatter | However, with this update we also adjusted the value of some of the resource bundles no... |
| P4432 | 2016-03-31 | Hotfix notes - April 1 2016 | NOT FOLLOWED | already TASKS 6.3 (no session pacing anywhere; RollDrops ignores stat 17; plain digging 0.3 % lumens, no recipes) → 6.3 | As mentioned previously, lumen and recipe drops have not been working quite as we inten... |
| P4439 | 2016-03-31 | Hotfix notes - April 1 2016 | FOLLOWED | client Recipes / PropRecipe tables of the 0.589 build (costs, default-learned flags, Lumen Station list) | The cost of decorative prop recipes at the Lumen Station has decreased |
| P4444 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Recipes now show up in the loot tab, with purple text |
| P4462 | 2016-03-31 | Hotfix notes - April 1 2016 | NOT FOLLOWED | design part: Props.PlaceDesignProp places a design's props only from bag items → 6.40 | If the prop is already created and in your inventory, you can place it in the world. Yo... |
| P4464 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | This is still being balanced so we can't give you an exact number yet, we're watching t... |
| P4597 | 2016-04-12 | Update Notes - April 13, 2016 | FOLLOWED | client Recipes / PropRecipe tables of the 0.589 build (costs, default-learned flags, Lumen Station list) | Adjusted crafting cost of textile, plastic, and lumicite recipes |
| P4630 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | Mastercraft Bracer / Gem Seeker's Brooch: not in the 0.589 item dump (re/out/item_names.tsv); no code | Lumicite crafted with the Mastercraft Bracer now yields a bonus of 150 |
| P4641 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | This appears to be an unexpected side effect of fixing the paint tool painting through ... |
| P4773 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.3 (no session pacing anywhere; RollDrops ignores stat 17; plain digging 0.3 % lumens, no recipes) → 6.3 | Common materials (dirt, sand, ice, stone) can now also drop lumens and prop recipes |
| P4777 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.3 (no session pacing anywhere; RollDrops ignores stat 17; plain digging 0.3 % lumens, no recipes) → 6.3 | Recipes now drop less often while playing, but are much easier to craft at the lumen st... |
| P4778 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | client Recipes / PropRecipe tables of the 0.589 build (costs, default-learned flags, Lumen Station list) | Boot crafting costs have been adjusted |
| P4815 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | client Recipes / PropRecipe tables of the 0.589 build (costs, default-learned flags, Lumen Station list) | The old set of starter designs is no longer listed on the Replicator for crafting (and ... |
| P4835 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT BUILT | crafting: client recipe data + Consumables 1173 SummonRepairBot; no loot path gives item 4129 (searched Gathering.RollDrops, Creatures.RollLoot, LootChests, Outposts) → 6.18 | A new item, the Repair Bot Beacon, is now available to craft at the replicator! This co... |
| P4843 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | client Recipes / PropRecipe tables of the 0.589 build (costs, default-learned flags, Lumen Station list) | Added 217 new cosmetic outfits which can now be crafted at the Lumen Station |
| P4844 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | client Recipes / PropRecipe tables of the 0.589 build (costs, default-learned flags, Lumen Station list) | Re-ordered the outfits on the Lumen Station alphabetically for ease of use (except the ... |
| P4845 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | client Recipes / PropRecipe tables of the 0.589 build (costs, default-learned flags, Lumen Station list) | At the demand request of NovaTech sales representatives, Extractor recipes are now gain... |
| P5061 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | We're continually refining these tools and hope that you find them easier to use with e... |
| P5119 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | AGAIN, **this is not actively being worked on due to how much work it would be on the c... |
| P5349 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | Props.PlaceDesignProp uses bag items without the recipe; skipped props listed (BuildingClipboard ZoneMissingPropList) | You should now get an error message when you place a Design that has props in you don't... |
| P5409 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | CraftingStations by station type; Spire has the Replicator and Repair Station (Spire.StationAt) | The Spire Hub should now have a Repair Station |
| P5626 | 2016-05-24 | Update Notes - 25 May 2016 | NOT FOLLOWED | already TASKS 6.3 (no session pacing anywhere; RollDrops ignores stat 17; plain digging 0.3 % lumens, no recipes) → 6.3 | Significant adjustments have been made to the recipe drop rate, which should lead to fa... |
| P5659 | 2016-05-24 | Update Notes - 25 May 2016 | FOLLOWED | RecipeBook.RandomUnknownRecipeItem draws from every recipe item | [Updated]These new items missed being added to the lumen station. They will be added in... |
| P5670 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Not intentionally, but I've updated the known issues list to miss the ones that got lef... |
| P5678 | 2016-05-27 | Week End Wrap Up - 27 May 2016 | client-only / no server rule | feedback thread / announcement / chatter | If you're a Trailblazer, don't forget to opt in to get your name in the credits - follo... |

### commerce (39)

| Id | Date | Thread | Class | Evidence | Text |
|---|---|---|---|---|---|
| P4279 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | [Fixed] If you don't receive all the items you should be granted when you create your f... |
| P4288 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | [Fixed] Some characters had previously purchased inventory expanders of some type, and ... |
| P4306 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Some characters had previously purchased inventory expanders of some type, and didn't g... |
| P4336 | 2016-03-25 | Week End Wrap Up - March 25, 2.. | NOT FOLLOWED | already TASKS 6.3 (no session pacing anywhere; RollDrops ignores stat 17; plain digging 0.3 % lumens, no recipes) → 6.3 | There are some issues with lumen & other drops that are not behaving quite as intended ... |
| P4342 | 2016-03-28 | Additional lumen re-grants | client-only / no server rule | feedback thread / announcement / chatter | When we granted lumen refunds for marketplace items, we granted them based on the amoun... |
| P4344 | 2016-03-28 | Additional lumen re-grants | client-only / no server rule | feedback thread / announcement / chatter | This additional grant should compensate for the difference in lumens between what they ... |
| P4433 | 2016-03-31 | Hotfix notes - April 1 2016 | FOLLOWED | goal list, rewards and Luminary score from the live capture (Achievements.cs, DailyGoals.cs) | The lumen reward for tutorial goals has been increased |
| P4435 | 2016-03-31 | Hotfix notes - April 1 2016 | FOLLOWED | goal list, rewards and Luminary score from the live capture (Achievements.cs, DailyGoals.cs) | The lumen reward for completing daily goals has been decreased |
| P4437 | 2016-03-31 | Hotfix notes - April 1 2016 | NOT FOLLOWED | already TASKS 6.3 (no session pacing anywhere; RollDrops ignores stat 17; plain digging 0.3 % lumens, no recipes) → 6.3 | There should be no absolute hard cap on the number of lumens you can earn per day, but ... |
| P4454 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Lumens now have a shiny new icon. |
| P4465 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | I'm not sure what's unclear here. Lumens will be possible to buy. They aren't possible ... |
| P4589 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Marketplace bundles should now correctly display the owner’s name |
| P4590 | 2016-04-12 | Update Notes - April 13, 2016 | NOT BUILT | Store.Gallery lists every site ('TODO: only published sites'); free-text search only; no comments, no leaderboard / rising stars, no Play Mode filter → 6.43 | Gallery can now be searched based on all three of the tag drop-downs, as well as key wo... |
| P4591 | 2016-04-12 | Update Notes - April 13, 2016 | NOT BUILT | Store.Gallery lists every site ('TODO: only published sites'); free-text search only; no comments, no leaderboard / rising stars, no Play Mode filter → 6.43 | Untagged build site should no longer show up in gallery – if you notice any that ARE, p... |
| P4593 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | obsolete Spirit Anchors (migration) | Now-obsolete Spirit Anchors will be moved into your overflow and can be consumed to get... |
| P4595 | 2016-04-12 | Update Notes - April 13, 2016 | FOLLOWED | no daily lumen cap anywhere (grep 'per day' / 'cap') | Fixed a bug that was unintentionally limiting characters to 120 lumens per day |
| P4672 | 2016-04-21 | Showcase updates | client-only / no server rule | feedback thread / announcement / chatter | UPDATE - marketplace/gallery issues performance issues should now be fixed. |
| P4673 | 2016-04-21 | Showcase updates | client-only / no server rule | feedback thread / announcement / chatter | We're currently seeing slow performance when you open either the gallery or the marketp... |
| P4674 | 2016-04-21 | Showcase updates | client-only / no server rule | feedback thread / announcement / chatter | Still working on issues with the Marketplace/Gallery today folks, apologies for the con... |
| P4677 | 2016-04-21 | Showcase updates | client-only / no server rule | feedback thread / announcement / chatter | Also, just to put this discussion to rest: Windows 7 is fine for Landmark and is irrele... |
| P4679 | 2016-04-21 | Showcase updates | client-only / no server rule | feedback thread / announcement / chatter | The marketplace/gallery issue should now be fixed. |
| P4680 | 2016-04-21 | Showcase updates | client-only / no server rule | feedback thread / announcement / chatter | Access the marketplace via the shopping cart icon; access the gallery via your travel m... |
| P4682 | 2016-04-22 | Week End Wrap Up - April 22, 2.. | client-only / no server rule | feedback thread / announcement / chatter | We did push out an update to our marketplace/gallery that fixes some bugs and adds some... |
| P4775 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT FOLLOWED | already TASKS 6.3 (no session pacing anywhere; RollDrops ignores stat 17; plain digging 0.3 % lumens, no recipes) → 6.3 | Decreased early lumen drop rate, but increased drop rate over time |
| P4779 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Gallery now consistently refers to Landscapes rather than Islands |
| P4780 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT BUILT | Store.Gallery lists every site ('TODO: only published sites'); free-text search only; no comments, no leaderboard / rising stars, no Play Mode filter → 6.43 | Gallery search categories have been updated to match the new tags you can set on your B... |
| P4781 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | B key now opens the gallery directly rather than the marketplace |
| P4782 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Improved some performance issues with the gallery or marketplace opening |
| P4783 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT BUILT | Store.Gallery lists every site ('TODO: only published sites'); free-text search only; no comments, no leaderboard / rising stars, no Play Mode filter → 6.43 | There’s now a button in the Site Management window that lets you choose to publish/unpu... |
| P4784 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Updated messaging when submitting a player studio to more clearly indicate why a submis... |
| P5386 | 2016-05-11 | Hotfix Notes - 11 May 2016 | FOLLOWED | Trades.cs:139 / Mail.cs:293 refuse NoTrade items | Attempting to trade or mail an item bought from Marketplace should now correctly give a... |
| P5387 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | real-money store prices / Marketplace UI (the store is free here) | The "Get more build sites" button in site management should now correctly open the purc... |
| P5388 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | There is now a separate menu bar button to open the Gallery directly |
| P5389 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | real-money store prices / Marketplace UI (the store is free here) | Build Site Expansions now have clearer description text explaining what you’re getting ... |
| P5390 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Addressed a Marketplace issue where images would not always display consistently |
| P5391 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | All Player Studio items on the Marketplace should now include detail text to explain wh... |
| P5392 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Search function in the marketplace works once again |
| P5393 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | The default image displayed if no screenshot can be found has been updated |
| P5713 | 2016-06-03 | Week End Wrap Up - 3 June 2016 | client-only / no server rule | feedback thread / announcement / chatter | on Friday the game will be available for purchase and anyone who buys it (or who previo... |

### travel (36)

| Id | Date | Thread | Class | Evidence | Text |
|---|---|---|---|---|---|
| P4098 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | As we prepare Landmark for live launch (expected this spring (https://forums.daybreakga... |
| P4099 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | As of 10am March 21st, Landmark servers are down for this update and may remain down fo... |
| P4102 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Landmark now has a clearer theme and direction for launch to better reflect the creativ... |
| P4103 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Characters are now called Luminaries and the unit of imagination is called the "lumen"!... |
| P4104 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Some other in-game terms are also changing for theme and clarity to new players: |
| P4105 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Islands are now called Landscapes |
| P4106 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Claims are now called Build Sites, and Claim Management is Site Management |
| P4107 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Templates are now called Building Designs, or Designs for short |
| P4108 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Voxels are more commonly known as Blocks |
| P4109 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Underground caverns with ruins are known as the Chaos Caverns |
| P4110 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Achievements are now called Goals |
| P4120 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Claims ResultPioneer 28 refuses player sites on Pioneer lands | The new Pioneer Landscapes are flat, have no cavern networks beneath them, and cannot b... |
| P4121 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Travel menu home / friend / guild (Abilities 185 / 186, teleport to friend) | There is a new Teleport menu accessed directly from the UI. This replaces the need for ... |
| P4122 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Travel: 'An Underground Adventure' → Caves.TravelRandom | The Classic Landscapes have a new type of underground: the Chaos Caverns. These are sho... |
| P4123 | 2016-03-21 | Landmark Update Notes - March 21 | NOT BUILT | Outposts.CompleteChannels gives loot on every channel; no per-player per-crystal lockout → 6.25 | A Lost Crystal should be found in each ruin, but you will only get rewards from the fir... |
| P4124 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | See previous forum post for more details on landscapes and cave changes (https://forums... |
| P4235 | 2016-03-21 | Landmark Update Notes - March 21 | FOLLOWED | Landscapes.cs leaves the Museum zones out (note for TASKS 5.2: not accessible at launch) | The Foundation Museum islands are no longer accessible. |
| P4246 | 2016-03-21 | Landmark Update Notes - March 21 | NOT FOLLOWED | WarpNetwork.cs serves the Spire's Local Travel map and 81 underground portal crystals → 6.50 | [Fixed] "Local" is still showing as an option to travel at the portal spire, but should... |
| P4256 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | Teleporting from another island to a pioneer island doesn't always place you at the spire |
| P4258 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | Consumables on the hotbar don't show how many are in the stack until after you zone. |
| P4271 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | [Fixed] Teleporting back to a teleport arrival point may look bad if you land right on ... |
| P4308 | 2016-03-24 | Hotfix notes - Friday March 25 | client-only / no server rule | client UI / visual / bug fix | We will be doing a quick hotfix on Friday morning at 4am PDT. (Convert to Your Local Ti... |
| P4669 | 2016-04-21 | Showcase updates | client-only / no server rule | feedback thread / announcement / chatter | The Gallery and Competitions areas are now accessed directly via your Travel menu |
| P4684 | 2016-04-22 | Week End Wrap Up - April 22, 2.. | client-only / no server rule | feedback thread / announcement / chatter | The Gallery and Competitions areas are now accessed directly via your Travel menu (or "... |
| P4691 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Wednesday morning's down time (https://forums.daybreakgames.com/landmark/index.php?thre... |
| P4841 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Teleporting with an item in your hand should no longer make the item unusable |
| P4965 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT BUILT | Spire.cs builds one Spire on the home continent; Spire.TravelTo re-zones there from other landscapes → 6.49 | When traveling to a pioneer landscape you should now correctly arrive at the spire |
| P4967 | 2016-04-26 | Hotfix notes - 26 April 2016 | NOT BUILT | Spire.cs builds one Spire on the home continent; Spire.TravelTo re-zones there from other landscapes → 6.49 | You should no longer get a ‘cast failed’ message when attempting to travel to a portal ... |
| P4975 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Corrected some incorrect images of landscape biomes on the portal menu |
| P5211 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | Thanks, Azari. I'll fly around Ancient Crag and see if I can find it. World isn't impor... |
| P5434 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | World and landscape name with /loc coordinates so we can go look at the bug would even ... |
| P5465 | 2016-05-23 | [Feedback] Excessively long lo.. | client-only / no server rule | feedback thread / announcement / chatter | Whether it's consistent between certain zones (if so, which) or only on log in, or ever... |
| P5466 | 2016-05-23 | [Feedback] Excessively long lo.. | client-only / no server rule | feedback thread / announcement / chatter | If there's anything else that seems related to this happening (e.g. only happens on fir... |
| P5475 | 2016-05-24 | Update Notes - 25 May 2016 | client-only / no server rule | client UI / visual / bug fix | Landmark will be down for an update on Wednesday, May 25th starting at 7am pacific time... |
| P5680 | 2016-06-02 | Hotfix Notes - 2 June 2016 | client-only / no server rule | client UI / visual / bug fix | We just pushed out a super stealth ninja hotfix without bringing the servers down. Next... |
| P5720 | 2016-06-03 | Week End Wrap Up - 3 June 2016 | client-only / no server rule | feedback thread / announcement / chatter | We'll see you next week - enjoy your last weekend in beta! Can't wait to see you on the... |

### character (31)

| Id | Date | Thread | Class | Evidence | Text |
|---|---|---|---|---|---|
| P4111 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | New characters now have a size slider to change between small and large humans; |
| P4243 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | known-issue report (client bug) | Sometimes when you create a new character, you will get a message saying “unable to cre... |
| P4355 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | When creating a new character, the new character name field should always be blank |
| P4356 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Fixed some display issues for large characters wielding the sword on character create s... |
| P4357 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Fixed some skin tinting and model issues on character create screen |
| P4358 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Character Create camera now zooms in correctly when weapons change |
| P4359 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | When a character is deleted, the slider bars should now reset, and the character gender... |
| P4360 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Delete button shouldn't be active in character select until the user types in the word ... |
| P4361 | 2016-03-31 | Hotfix notes - April 1 2016 | NOT FOLLOWED | Pioneer.Place tries Pioneer surfaces only, else no site → 6.48 | New characters may now be granted an underground cave build site on the pioneer islands... |
| P4442 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Due to the significant changes in costs and drop rates of items, we have determined tha... |
| P4443 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | This means previous Marketplace purchases will be refunded in lumens, just like the las... |
| P4451 | 2016-03-31 | Hotfix notes - April 1 2016 | client-only / no server rule | client UI / visual / bug fix | Text for "sneak" emote no longer says "you skip" |
| P4548 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Build Settings UI now uses the standard Landmark UI appearance, and can be repositioned |
| P4910 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | Emotes.cs (FEATURES: emotes ✅) | Emote commands have been standardized! The list of emotes follows (and note also that s... |
| P4914 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | Emotes.cs (FEATURES: emotes ✅) | /yell /beg /bow /cheer /beckon /congratulate /cough /cry /flex /flirt /groan /giggle /s... |
| P4915 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | A few missing customization options for female characters (including the loose bun hair... |
| P4916 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | More gradual degrees of skin tint are now available |
| P4917 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Additional hair tints are now available. Pink or blue hair? Yes please! |
| P4918 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Additional eye tints are now available |
| P4919 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | When a character is deleted, the new character no longer incorrectly displays the old c... |
| P4920 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Selecting the fantasy starter outfit at character create no longer loads the sword in u... |
| P4921 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | There should no longer be a moment of lag when switching between outfits on the charact... |
| P4922 | 2016-04-26 | Hotfix notes - 26 April 2016 | FOLLOWED | Pioneer.Settle auto-grants a site to new characters | New characters were not consistently getting an auto-placed build site, this should now... |
| P4986 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | known-issue report (client bug) | Some outfits have changed color again |
| P4987 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | known-issue report (client bug) | Some characters' skin color has changed |
| P5050 | 2016-05-04 | [Feedback] Starter Designs fee.. | client-only / no server rule | feedback thread / announcement / chatter | I haven't received anything ... my in-game character is Domino. |
| P5071 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Thanks for the feedback, Jadam! Can you clarify exactly what's happening for you here. ... |
| P5254 | 2016-05-04 | [Feedback] Prop Linking | NOT FOLLOWED | Creatures.SendSpawn flags every story creature interactable; ClickCreature ignores faction → 6.32 | Basic rule of thumb: the interaction would only be hidden if your character interacting... |
| P5413 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Corrected an error pop-up that could happen when creating characters in some situations |
| P5428 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | known-issue report (client bug) | [Updated] Clicking the Character Fly Camera button does not work (pressing F4 on your k... |
| P5715 | 2016-06-03 | Week End Wrap Up - 3 June 2016 | client-only / no server rule | feedback thread / announcement / chatter | whatever the character on your account is named when we wipe all characters, that name ... |

### instancedgames (13)

| Id | Date | Thread | Class | Evidence | Text |
|---|---|---|---|---|---|
| P4230 | 2016-03-21 | Landmark Update Notes - March 21 | NOT FOLLOWED | InstancedGames.cs runs Deathmatch / TDM / Zone Control tables where players hurt each other → 6.51 | We have removed PVP game tables. PVP is no longer available. |
| P4290 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Steam updates |
| P4291 | 2016-03-21 | Landmark Update Notes - March 21 | client-only / no server rule | client UI / visual / bug fix | Some people who are updating via Steam are reporting a "disk read error" (seems the upd... |
| P4688 | 2016-04-22 | Week End Wrap Up - April 22, 2.. | client-only / no server rule | feedback thread / announcement / chatter | We don't have a firm date yet for the next update, but hope to be able to confirm the t... |
| P4689 | 2016-04-22 | Week End Wrap Up - April 22, 2.. | client-only / no server rule | feedback thread / announcement / chatter | As always, remember you can find all recent posts by dev team members by clicking on th... |
| P4957 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | New loading screens! We are now using screenshots selected from among our previous comp... |
| P5007 | 2016-04-30 | Week End Wrap Up - April 30 | client-only / no server rule | feedback thread / announcement / chatter | As always, remember you can find all recent posts by dev team members by clicking on th... |
| P5205 | 2016-05-04 | [Feedback] Chaos Caverns + Out.. | client-only / no server rule | feedback thread / announcement / chatter | Technical limitations prevent us from having caverns (the big open areas underground) o... |
| P5260 | 2016-05-06 | Week End Wrap Up - May 6 | client-only / no server rule | feedback thread / announcement / chatter | In game this week we all enjoyed watching Verlin & co streaming a Star Wars Trivia game... |
| P5679 | 2016-05-27 | Week End Wrap Up - 27 May 2016 | client-only / no server rule | feedback thread / announcement / chatter | Hope you're as excited as we are - thanks for being a part of our big dev team and work... |
| P5714 | 2016-06-03 | Week End Wrap Up - 3 June 2016 | client-only / no server rule | feedback thread / announcement / chatter | insofar as we have the ability to control it, the intent is for Steam access to share t... |
| P5719 | 2016-06-03 | Week End Wrap Up - 3 June 2016 | client-only / no server rule | feedback thread / announcement / chatter | In case we haven't said it enough, thanks for all the bug reporting and detailed inform... |
| P5722 | 2016-06-03 | Week End Wrap Up - 3 June 2016 | client-only / no server rule | feedback thread / announcement / chatter | If you're not sure which level founder's pack you have, or which account it's on, you s... |

### client (9)

| Id | Date | Thread | Class | Evidence | Text |
|---|---|---|---|---|---|
| P4312 | 2016-03-24 | Hotfix notes - Friday March 25 | client-only / no server rule | client UI / visual / bug fix | Also addressed several issues that could cause random client crashes |
| P4633 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Addressed various client crash issues |
| P4634 | 2016-04-12 | Update Notes - April 13, 2016 | client-only / no server rule | client UI / visual / bug fix | Continued work on performance |
| P4962 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Fixed some crash issues |
| P4969 | 2016-04-26 | Hotfix notes - 26 April 2016 | client-only / no server rule | client UI / visual / bug fix | Loading screen tips have been updated |
| P5076 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Lots of good feedback in here, but I wanted to call out specifically that this issue sh... |
| P5078 | 2016-05-04 | [Feedback] Story Tools | client-only / no server rule | feedback thread / announcement / chatter | Great suggestions here. Just to confirm, you should be able to do #5 and #6 now after y... |
| P5408 | 2016-05-11 | Hotfix Notes - 11 May 2016 | client-only / no server rule | client UI / visual / bug fix | Loading screen screenshots should now change within a play session |
| P5461 | 2016-05-23 | [Feedback] Excessively long lo.. | client-only / no server rule | feedback thread / announcement / chatter | If you are currently experiencing loading times of over 3 minutes on a regular basis, c... |
