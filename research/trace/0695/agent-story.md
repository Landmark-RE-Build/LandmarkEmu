# Story tools: spawner weapon Links, Weapon Visuals, the Game Table's "On Game Ended" (agent, 2026-10-17)

Branch `e-story` (worktree `<workspace>\wt-story`), commit 40024eb. Audit rows #22 / #6 / #28 and priority items 3
and 6 of `agent-features-audit.md`; OPEN-LEADS L95 and L21. Built, and `dotnet build` passes. **Not tested in game.**
Marks: **[P]** proven from the client or a developer post, **[I]** inferred.

## 1. NPC weapon draw / sheathe (L95)

### 1.1 Evidence

What was already known: EditableProperties 0.695 row 40 `npc_spawn_weapons_equipped` (577229, drop-down shape) and row
41 `npc_weapon_visuals` (577234; Equipped 577235 / Sheathed 577236); the spawner Links 577074 / 577075 (0.589)
and 577237; the Story Node 577238 / 577239; the wiki Story Node page ("Weapon Visual: Equipped / Sheathed"). The decompile
strings have no "sheath" or "holster".

New traces:

- **AnimationSelection rows that set the network's `NPC` flag** (gameData695 AnimationSelectionVariables, the same in
  0.589's gameData). There are only three, and 352 / 353 are a pair:
  - 352, MESSAGE "WeaponEnd": WeaponIdle 1, Class 1, NPC 1.
  - 353, no message: WeaponIdle 0, Class 0, NPC 1.
  - 502: WeaponIdle 1, NPC 1, OnGround 0.

  The NPC attack selections 356-359, 364 and 781 also set Class 1 with WeaponIdle 1. "WeaponEnd" is a request in
  PlayerAnimationNetwork64.mrn (re/out/anim/strings.txt 0x1fc61e).
- **What WeaponIdle does [P].** I decoded the mrn switch nodes (NodeDef type 0x83, children at +0x20, control input at
  +0x28; layout from trace/systems/social.md). The switch `StandPoses|Idle_Stand_2` (node 10167) reads
  `ControlParameters|WeaponIdle` (node 12) and picks child 10168 `Stand` (0) or 14054 `Weapon1` (1), which is the
  weapon-ready idle. The LedgeGrab switches 2482 / 2490 / 2497 read it too. `ControlParameters|NPC` (node 142) only
  drives the ShrinkRay attack switches. Biped creature actors use this network: for example,
  `Char_Biped_HumanMale_Entities_PCNPC_Qeynos_Heavy_000_Chest.adr` and `Char_Biped_Kobold_Melee_000.adr` both name
  `PlayerAnimationNetwork.mrn`.
- **The same variable comes from a held weapon [P].** `Wield_1HBlade_Qeynos_Qeynos_D_A0_000.adr` has the
  AnimationEnumerations −1452685539 = 1, −775574123 = 1010 and 2090366532 = 1. These are the one-at-a-time hashes
  (strhash.py's StringHashToValue hash) of **WeaponIdle**, Class and Weapon. So drawing a weapon sets WeaponIdle to 1.
- **The client path [P].** AbilityPlayAnim (0x27 sub 0x12) is read by FUN_142e030c0 and handled by FUN_142e1b9d0: entity
  by guid (FUN_142f85390), then FUN_142dfc010, then FUN_143d95d40 `AnimationSelectionManagerBase::PlayAnim`. That
  function sets every "variable" of the selection's AnimationSelectionVariables rows on the actor (vtable +0x18), then
  sends the row's MESSAGE when there is one. So 352 / 353 sent by AbilityPlayAnim put the actor into, or out of, the
  weapon idle.
- **Not proven [I, strong]:** that live's Equipped / Sheathed were exactly 352 / 353. No client code names these
  selections: no FUN_142dfc010 caller has the constant 0x160 / 0x161, and they are not in StringHashToValue, Lua, XML or
  the help pages. They are the only NPC weapon pair, which is the basis for the inference.

### 1.2 Built

| Piece | Where | What it does |
|---|---|---|
| `Creatures.AnimNpcWeaponsEquipped = 352`, `AnimNpcWeaponsSheathed = 353` | Game/Creatures.cs | The evidence above, in the XML doc |
| `Creature.WeaponsEquipped`; `Creatures.StoryWeapons(guid, equipped)` | Creatures.cs | A real change sends one AbilityPlayAnim (40 bytes) to the viewers; no change sends nothing |
| Spawn / new viewer / full data | `SpawnStory(..., weaponsEquipped)`, `SendSpawn`, `SendFull` | An equipped creature gets 352 with its spawn for every new viewer, and once more after its LightweightToFullNpc (see 1.3). Sheathed is the network default, so nothing is sent for it |
| Spawner Links 577074 / 577075 | StoryTools.Execute | Equip / sheathe. They no longer fall to "has no effect yet" |
| Spawner 577237 "...Set this creature's weapon visuals." | spawnerKind + Perform | Uses the chain's Story Node row 41, the way "Set mood" uses the node's mood |
| Story Node 577239 / 577238 | storyNode kind + ActivateNode | The node's Links run; with none, the P5284 fallback sends it to the chain's creature |
| "Perform everything" (577048) | Perform | Also applies weapon visuals, but only when the node's row 41 was saved [I, ours]. This keeps older and unset nodes from drawing weapons on every "everything" |
| Row 40 on spawners, row 41 on Story Nodes | PropSettings (settingsByKind, DropDowns) | Options Equipped 577235 = 352 and Sheathed 577236 = 353. Defaults [I]: row 40 Sheathed, row 41 Equipped |
| Dev `storyweapons <creature hex> <1\|0>` | DevCommands | Calls StoryWeapons directly |
| docs | docs/story-tools.md | New paragraph |

Hazards (mines, fungus) and the Target Dummy keep their own row lists, so they don't get row 40. Every (re)spawn starts
as row 40 says.

### 1.3 Open leads (for L95; nothing here is written off)

1. **The weapon model.** The weapon visuals built here are the stance (WeaponIdle). The PCNPC creature actors are body
   only (`borrowSkeleton="1"`, no attachments), so any weapon model live showed came from the full NPC data's
   attachments (LightweightToFullNpc +0x28, CharacterAttachmentData FUN_142c185c0); we send none. Also unknown: whether
   Sheathed moved the model to a holster bone (the skeleton has BackHolster / R_HipHolster / L_HipHolster). Try next:
   the CharacterAttachmentData reader and its apply (FUN_142e9a050), a slot / holster field in it, and captures of a live
   NPC's full packet.
2. **Timing on a fresh spawn [I].** FUN_142dfc010 needs the actor's network (vtable +0x468). A selection that arrives
   before the model has loaded may be lost. That is why 352 is sent again after the full data. Check in game (test 3
   below).
3. **The option ids.** Row 40 VALUE_DEFAULT 109 and row 41 VALUE_DEFAULT 106 are EditablePropertyValues ids, which are
   not shipped (L18). Row 41's max 2 might mean a third option, but no third text exists. The defaults chosen are [I].
4. **AI coupling.** P6511 ("NPCs that spawn with weapon equipped ... never attack") shows the live server tied the
   weapon state to its AI. Here the AI ignores it, so that bug is not reproduced. Whether creatures drew their weapons
   on entering combat and sheathed them afterwards is not known.
5. **Non-biped creatures** (wolves, deer, chompers, quadbots) may not have WeaponIdle in their networks. They would then
   show nothing, as with moods (L117).

## 2. The Game Table's "On Game Started" / "On Game Ended" (#28, P6517, L21)

### 2.1 Evidence

- **Text 573011 "On Game Ended" [P]**, in both builds. It sits next to **573010 "On Game Started"**, in the 0.589
  story-tool string block (572991 Travel Point, 572997 / 573003 spawner, 573006 / 573007 Spawn Automatically).
  The developer's wording in P6517 (29 Sep 2016) is "the On Game Ended link from the table to a chest". Earlier
  searches for "game ends" missed it because the sentence does not start with "When" / "...". It was not in trace/ and
  not in the code.
- **Owner [P].** "From the table". In 0.695 the only placeable table is 2313 "Game Table" (572907; PropCategoryPropMap
  1926: category 120). 1226 / 1346 / 1347 are in no 0.695 palette.
- "On Game Started" being an event of the same table is **[I, strong]**: a paired text, the same wording, and no source
  mentions it.

### 2.2 Built

- `InstancedGames.GameStartedEvent = 573010` and `GameEndedEvent = 573011` are events of the 2313 kind (StoryTools
  kindsById), so the Links window offers them.
- **StartRound:** after the Adventure round restarts the site's story (`Claims.ApplyMode(play, restart)`), it raises
  "On Game Started" with no triggering character [I].
- **EndRound:** after the result, the final scoreboard and the Victory state, and **before** Play Mode goes back off, it
  raises "On Game Ended" (`RaiseGameEnded`). This way the chain still finds the running props, as in P6517's table →
  chest example. Score and Victory Nodes in the chain do nothing because the round is no longer Running.
- **Triggering character [I, ours]:** the winner. In free-for-all that is the winning player; in a team game it is the
  best scorer of the winning team; a draw has none. So "...Loot this chest." rewards the winner and their group (P6503),
  and an IF Gate testing the "triggering character's game team" reads the winning team.
- Both events run marked as a story chain (`RaiseTableEvent`), so a Victory Node or "Give reward and teleport" inside
  them is deferred to the next tick (EndRound's existing deferral). A round abandoned because everyone left raises
  nothing [I].
- docs/instanced-games.md has a new paragraph.

### 2.3 L21 status

- **Settled:** the table's "On Game Ended" Link is text 573011, an event of Game Table 2313. Its pair 573010 is
  inferred.
- **Placement of Score / Victory Nodes:** already built through the game editor's Game Props tab (the recipe item's
  ScriptPropId, FUN_1434f31e0 [P]; InstancedGames.Rework `ScriptPropRows` / `HandleScriptProp`). No new evidence on
  whether live offered them only through that tab. RuleSetScriptPropDefinition (FUN_143b75220) is still not shipped.
- **Still open:** ownership of 577665 / 577666 / 577668 / 577684-577687. They are on the Score Node by wording; no new
  evidence either way. Also open: the Audio / Group Node models (1742 / 1743), and live's triggering character for "On
  Game Ended" and "On Game Started". Try next: forum threads on the Game Table (Sep / Oct 2016) for "On Game
  Started", and captures.

## 3. Test steps (0.695 test copy <0.695 test copy>, dev world; restarting the dev world with this build is needed)

Dev commands go in `dev_commands.txt` next to the world server, or on the admin page. Prefix a line with `@Name` to
run it for one character. Run `story` to list the running props and creatures with their guids.

**Weapon visuals: spawner Links (577074 / 577075)**
1. On your build site, place a biped creature prop with a weapon look, for example Highfall 2597 / 2598 (Qeynos Heavy
   male), from the Creatures palette or with `prop 2597`. Place a switch or lever next to it.
2. Open Links on the lever: link "When ... turned on" to the creature's "...Have this creature equip their weapon.",
   and "turned off" to "...unequip their weapon.". Set the creature's Reaction to Neutral, so it does not fight.
3. Enter Play Mode and pull the lever. Expect the creature to move from its relaxed stand to the weapon-ready idle.
   Pull it again and expect the relaxed stand. Server log: "Story creature 0x... equips / sheathes its weapons."
4. Without Links: `story` gives the creature guid; then `storyweapons <guid> 1` and `storyweapons <guid> 0`. The log
   says "changed", or "no change" when it was already in that state.

**Spawn With Weapons Equipped (row 40)**

5. Open the creature's Settings. Expect a "Spawn With Weapons Equipped" drop-down (Equipped / Sheathed, default
   Sheathed) after Combat Tactics. Choose Equipped, save, and toggle Play Mode off and on. Expect the creature to appear
   in the weapon idle.
6. Walk out of view range and come back, or have a second client arrive. Expect it to still be in the weapon idle.
   This checks the resend on SendSpawn and SendFull. If a fresh spawn shows the relaxed stand, note it under L95
   open lead 2.

**Story Node Weapon Visuals (row 41, 577237-577239)**

7. Open a Story Node's Settings. Expect "Weapon Visuals" (Equipped / Sheathed, default Equipped) after "Announce Emotes
   in Chat".
8. Link the creature's "When a Luminary clicks this creature..." to the node's "...Activate the weapon visuals on this
   node.", and link the node's "When the weapon visuals on this node activates..." to the creature's "...Set this
   creature's weapon visuals.". In Play Mode, click the creature: it draws its weapon. Set the node to Sheathed and
   click again: it sheathes.
9. Fallback: remove the second Link and click again. The node has no Link, so the creature that triggered it acts it
   out (the P5284 fallback).
10. "Everything": Link the click to "...Activate everything on this node." On a node whose Weapon Visuals was never
    saved, the weapons don't change. After saving one, "everything" applies it before the mood / stance / emote.

**Game Table "On Game Started" / "On Game Ended" (573010 / 573011)**

11. Place a Game Table (2313) and a Common Reward Chest (3003) on the site. Open Links on the table: expect "On Game
    Started" and "On Game Ended" in its events. Link "On Game Ended" to the chest's "...Loot this chest.", and "On Game
    Started" to, for example, a lever's "...Turn on".
12. Join the game (walk on, or `gamejoin`) and `gamestart`. Expect the lever to turn on as the round begins.
13. `gamescore 5`, then `gameend`. Expect the end-of-game scoreboard, and the winner (you) to get the chest's loot
    before the site leaves Play Mode. A draw (a team game with no points) gives no loot. Without the client's Links
    window, add the Link with `storylink <table hex> 573011 <chest hex> 577598`.

## 4. Files

- `wt-story/LandmarkEmulator.WorldServer/Game/Creatures.cs`: AnimNpcWeapons*, StoryWeapons, PlayAnimBody, and the
  spawn / full-data resend.
- `.../Game/StoryTools.cs`: kinds (Story Node, spawner, Game Table), Execute, Perform, Spawn.
- `.../Game/PropSettings.cs`: SpawnWeapons / WeaponVisuals rows, options and defaults.
- `.../Game/InstancedGames.Rework.cs`: GameStartedEvent / GameEndedEvent, RaiseGameEnded, RaiseTableEvent.
- `.../Game/InstancedGames.cs`: the two raise points.
- `.../Game/DevCommands.cs`: `storyweapons`.
- `docs/story-tools.md`, `docs/instanced-games.md`.
- Shared notes were not edited (TASKS / OPEN-LEADS / FEATURES / MODLOG / TESTING-NEEDED). The L95 / L21 updates are in
  §1.3 / §2.3 above.
