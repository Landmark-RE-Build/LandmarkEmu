# E.17 — Play Mode visibility of story tools (L20, audit #1 / #16) and Story Node moods on non-biped creatures (L117, audit #6)

Branch `e-playmode` (worktree `<workspace>\wt-playmode`), commit 377d01c. Build: `dotnet build
LandmarkEmulator.WorldServer/LandmarkEmulator.WorldServer.csproj -v q` — 0 errors. Untested in game (no client / servers
run, as instructed). Marks: [P] proven from client code or data, [I] inferred.

Scratch tools and outputs (all on Z:): `re/out/l117/` — `mrn.py` (Morpheme name table + switch decoder, validated on
the player network against agent-abilities.md node 14514), `dumpnames.py`, `conds.py` (condition records),
`condowner.py` (condition → transition node), `creatures_nets.py` / `creatures_nets.tsv` (creature prop → model → adr →
network), `mrn/*.mrn` (copies read from the test install's packs by fileindex_695 offsets), `names_*.txt`,
`nets_switches.txt`, `player_switches.txt`, `conds_*.txt`. `re/out/adr695/` — the story tools' .adr/.dme/.dma and
decompile extracts (`lw_base_reader.c`, `fun_142d54040.c`, `pu_dispatch.c`).

---

## 1. L20 — how story tools hide in Play Mode

### 1.1 The client has no Play Mode rule for props [P]

- **FUN_142d99b70** (returns "PlayMode" when claim entry +0x378 == 2) has three callers, all building a **map label**
  `"%s %s"` (claim name + "PlayMode") for the map-marker manager (DAT_1454311c8+0x32050, FUN_1432635e0 / FUN_143263d40):
  part_0069:14035 (Claims dispatcher FUN_142dbecb0, ClaimInfoUpdate kind 0x13), part_0113:7315 (FUN_1435e3630),
  part_0113:8883 (FUN_1435e5a20). refscan: `call@0x142dbf5ca, 0x1435e3933, 0x1435e5b61` only.
- **Setting the mode**: ClaimInfoUpdate kind 0x13 calls the claim entry's vtable +0x28 = **FUN_143c17a40**, a bare
  `*(+0x378) = mode` (both claim-entry vtables, 0x1446beaf0 / 0x1446beb30; their other slots return 0). Then it updates
  the map label, `FUN_142db87c0` (AdventureStatus into ClaimDialogs.ClaimEditDialog / WalkOnText data sources) and,
  for the current claim, `FUN_14346d4a0` (Tool.GlobalSettings). Nothing touches props.
- **Every int read of +0x378** in the decompile (grep of all 51 hits, by enclosing function): map label; AdventureStatus
  data sources (FUN_142db8ab0, FUN_142dc7540); camera rules FUN_142dd5540 / FUN_142dd4740 / FUN_1435e4530 /
  FUN_143607950 ("Claim.NoCameraPlayMode"); grapple FUN_142e93540; boot abilities FUN_14370f590; ToggleAdventureStatus
  FUN_1435e62a0; Tool.GlobalSettings FUN_14346d4a0; build-tool pick mask FUN_142f7ce40 (collision query bit 0x2000) and
  FUN_142f7e0e0 (no callers). The rest are unrelated structs. **No prop / render / proxy code reads it.**
- **Counting Node 2901 vs Scoreboard 3000** look the same to the client apart from their mesh: PropDefs rows identical
  bar the name; the live prop list (145-PropBase) records identical (flag 1, +0x58 0, floats 0.5 / 10, image set 21676)
  bar model 1728 / 1736; Models.txt rows identical bar names; the two `.adr` files differ only in mesh / LOD / collision
  file names; the `_Lod0.dma` materials are byte-identical. No hard-coded prop or model id constant (0xb55, 0xbb8,
  0x6c0, 0x6c8) in prop code. No table or Lua native hides props (luabind_695: no prop visibility native).
- So the client cannot tell a hidden tool from a shown one → **the live server did it** [P that the client doesn't;
  the server mechanism itself is I, strong]:
  - DisplayPropText (FUN_142f859b0) shows a **bubble only over an entity it finds** (modes 4 / 5) and its chat line
    falls back from `"[%s] says: %s"` to the bare text when the entity is missing. The wiki Story Node page: the bubble
    "will not be visible in Play Mode, much like the node itself is not visible"; P5288 "Story Nodes cannot display Chat
    Bubbles since they're hidden in Play Mode". An entity that is merely invisible would still carry the bubble, so the
    nodes were **absent** from the client — the server did not send them.
  - Wiki per-prop lines: "This prop is only visible in Edit Mode" (Combat, Counting, Damage, Focus, Reset, Story, Server
    Time Node; Logic, Probability, Random Link, Timer Filter; Respawn Point; Travel Point); Scoreboard "visible in Play
    Mode"; Starting Location "always visible in Edit Mode, but is only visible in Play Mode when the Build Mode UI is
    shown" — a rule only the server can apply (it knows build mode from CommandPacketBuilderMode, 0x1E sub 0x5A).
  - Patch notes P5960 ("Like all Story Tools, this will hide during Play Mode"), P6182 (Scoreboard "permanently
    visible"), P6178 (Respawn Point look "cannot be seen by visitors when play mode is active").

### 1.2 Respawn Point enabled / disabled look (P6178) [P mechanism, I strong for which is "enabled"]

- Model 1739 `Char_SimpleObject_Respawn_Respawn01_SpawnLocation01.adr`: `AnimationNetwork RespawnAnimationNetwork.mrn`,
  anim set `RespawnMarker02`.
- RespawnAnimationNetwork64.mrn (13 nodes): control parameters PlaybackSpeed, absolute_1, WindStrength, WindDirection,
  TypeIndex, ActivationComplete, Pose, State_Dead, **Active**; one switch `Switch1` reading **Active**: [0]
  SimpleObject_Chest_Idle_01, [1] SimpleObject_Chest_Idle_02.
- "ControlParameters|Active" occurs in **no other** 0.695 `*64.mrn`.
- AnimationSelectionVariables 2789 / 2790: selection **848 → Active 1**, **849 → Active 0** (rows new in 0.695).
- Delivery: the lightweight base packet's **+0x280 list** (reader FUN_142c39ea0: u32 n × FUN_142c22da0 {u32 selection,
  bool +4, bool +5}) is played on the entity at spawn by FUN_142d54040 (loop calling FUN_142dfc010 for each; +5 clears
  entity +0x1461 bit 4, +4 is copied to entity +0xa39). 0.589 has the same loop (FUN_142cbaca0). Our writer already sent
  this list's count (0) as the last base u32 before the prop part.
- AbilityPlayAnim (0x27 sub 0x12, FUN_142e1b9d0) also reaches props (FUN_142f85390 searches the same proxy table that
  SpawnProxiedProp FUN_142f8a800 inserts props into), but it **sets the entity's heading** from its "towards" point
  (FUN_1437f7c8c → FUN_142ec4e60 → yaw-only rotation), which would flatten a tilted prop.
- [I, strong] Active 1 (848) is the enabled look, Active 0 (849) disabled. The default value of Active (the look with no
  selection) is not decoded, so both values are sent explicitly.
- Side finding: the prop part's last bool (+0x490 → entity +0x4e9c via FUN_1430abea0, "PlayerUpdatePacketSetEnabled")
  is an interaction / area "enabled" flag read by the area code (part_0117 FUN_1436d9680 …); we send false for every
  prop. Not changed here (movers work with it); noted as a lead below.

### 1.3 What was built

| Piece | Where | What it does |
|---|---|---|
| Hidden set + sight rules | `Game/Props.PlayModeVisibility.cs` (new) | `SightOf(prop)`: Everyone / BuildModeOnly (Starting Location 2655 on a playing site) / Nobody (story tools on a playing site; props being placed are always shown) |
| Hidden set | same | 2940, 2901, 2899, 2898, 2900, 2344, 2659, 2945 (Hour Node [I]), 2325, 2345, 2657, 2367, 2701, 2330, 2976 (IF Gate [I]) |
| Spawn filter | `Props.SendSpawns` | Skips props the viewer must not see — the one funnel for zone entry (`SendAll`), placement broadcasts, moves, re-spawns |
| Mode switch | `Claims.ApplyMode` → `Props.PlayModeVisibilityChanged` | Only on a real switch, after the story started / stopped: Play → RemovePlayer for each hidden tool to every viewer on the landscape (not mid re-zone); Edit → spawns them again (counters then show their Edit value through `SendStateTo`) |
| Build mode | `BuildModeHolster.BuildMode` → `Props.BuildModeVisibilityChanged` | Only when the state changes: playing sites' Starting Locations appear on Tab-in, go on Tab-out |
| Bandwidth | `StoryTools.FlushStates`, `Props.RefreshEditable` | No prop states / interaction infos for props the viewer doesn't have |
| Respawn look | `PlayerUpdateAddLightweightProp.SpawnAnimations` (+0x280 list), `Props.SpawnLook` | Every Respawn Point spawn carries 848 / 849 by its "Enabled" box (row 103) |
| Respawn look change | `StoryTools.SettingChanged` → `Props.RespawnLookChanged` | Upright point: one AbilityPlayAnim (`Creatures.AnimationBody`, shared with creatures) aimed along the prop's own forward so the facing stays [I convention: the creatures' yaw ↔ quaternion convention]; tilted point: remove + spawn |
| Docs | `docs/story-tools.md` "Story tools hidden in Play Mode", "Respawn Point look"; StoryTools comment updated | |

Not hidden (no statement found): Scoreboard 3000 (shown, P6182), Game Table 2313, Score Node 3011, Victory Node 3010,
Linking Area 1590 (model is an invisible cube anyway).

---

## 2. L117 — Story Node mood / stance / emote on creatures

### 2.1 Which network each creature prop uses [P]

Creature props = PropCategory 98 and its sub-categories (PropCategoryGroup), 135 props; model from the live prop list
(the server's `Props.ModelOf`), adr from Models.txt, network from the adr's `<AnimationNetwork fileName>`; the 64-bit
client loads `<name>64.mrn` (FUN_14347b8d0 builds name + "64" + ".mrn").

| Network | Props | Anim sets |
|---|---|---|
| PlayerAnimationNetwork | 111: Barren, Slaug, Scalewind, NovaTech people, Highfall, Ebonguard, Stoneskin (lesser giants), The Outcast, Target Dummy | DarkElfFemale, DarkElfMale, HumanFemale, HumanMale, Kobold, LesserGiant, Skeleton, Slaug |
| NpcAnimationNetwork | 10: Abomination ×4, NovaTech hounds ×4, Darkfang Specter, Oakmire Stag | Abomination, Quadbot, Wolf, Deer |
| GiantAnimationNetwork | 5: Frostbite / Magma / Gloom / Toxic Giant, Powered Mega-Knight | Giant, IronGolem |
| ChomperAnimationNetwork | 4 Chompers | Chomper |
| LargeBipedAnimationNetwork | Earth Elemental | — |
| FieryFungi / SimpleObject / WarMachine | Fiery Fungus / Vine Trap / Highfall Catapult | — |
| none (adr has no network) | Proximity Mine | — |

### 2.2 What the networks contain

Story Node values (AnimationSelectionVariables): moods 218-221 set **Emote_Emotion** 0-3; stances set **Stance_Index**
0, 1, 3, 4, 5, 101, 102, 104; emotes set **Emote_Index** (+ EmoteLoop 0) and send the Morpheme message
**EmoteSingle** (252 EmoteLoop). Every one of them has **STOPS_CURRENT_SELECTION = 1**.

- **Player network** [P]: Emotion switches on Emote_Emotion (Plain, Angry, Happy, Sad, …), Stance switches 14514 /
  14524 (Stand, Kneel, Placeholder, Dance, Sit_Chair, Laydown, PlayDead, AFK_States / Switch1, Drunk, Skip, Skip1,
  Sneak), the emote switches (agent-abilities / PropSettings), and the EmoteSingle request. Condition records confirm the
  walk stances: type 0x273 ranges (100.5, 101.5) … (103.5, 104.5) on Stance_Index. Per-set clip overrides exist only for
  idle / move / react / attack (Kobold, Skeleton, Slaug) plus "AddRetarget" additives (all sets), so creatures of those
  sets use the shared Biped_Core emote clips [I, strong].
- **Npc, Giant, Chomper, LargeBiped (and Pet)** [P for content, I strong for "nothing plays"]: node trees are only idle,
  turn, ouch, stun / knockdown / freeze / electrocute, movement, attack, death, a reaction mixer and **FakeDeath**. No
  switch reads Emote_Index / Stance_Index / Emote_Emotion / EmoteLoop; there is no "EmoteSingle" string in any of them.
  The only conditions on these parameters are `Stance_Index in (5.5, 6.5)` (types 0x273 / 0x263, i.e. value 6 — the
  0.695 play-dead stance, entering / leaving FakeDeath) in all five networks, plus in Giant one `Stance_Index > 0.5`
  (type 0x259, the shape the player network uses for HeroicMovementActive > 0.5) owned, by the node-def table index, by
  the transition `Core|Actions|Idle|ReactionMixer|PartialReactions_PassCore_2` — the partial hit-reaction overlay, not a
  pose [I]. No Story Node stance is 6, so no mood, stance or emote shows on these creatures.
- **FieryFungi (3 parameters), SimpleObject (8), WarMachine (3), Kobold network (not used by any creature prop)**: none
  of the parameters at all.

### 2.3 Server decision

No code change to what is sent: every creature still gets the selections, because STOPS_CURRENT_SELECTION = 1 means the
client stops the creature's current selection even where nothing new plays — skipping them would change behaviour
against live. The limitation and its proof are now in the `PropSettings.DropDowns` doc comment (replacing the old [I]
note).

---

## 3. Open leads (for OPEN-LEADS; not edited here)

- **L20-a** Who saw hidden tools in Play Mode: built as hidden for everyone, owner included (wiki "only visible in Edit
  Mode"); only the Starting Location has the build-mode exception. P6178 says "visitors", so live might have shown them
  to the owner / builders in build mode. Runtime / archive check.
- **L20-b** Game Table 2313, Score Node 3011, Victory Node 3010 in Play Mode: no statement found; left visible.
- **L20-c** Respawn look: that 848 (Active 1) is the *enabled* look is inferred from the name; the Active default value
  is not decoded. If the looks show swapped in game, swap `RespawnLookEnabled` / `RespawnLookDisabled`.
- **L20-d** AbilityPlayAnim on props sets the heading: built with the creatures' quaternion ↔ heading convention
  (spawn rotation (0, sin y/2, 0, cos y/2) ↔ towards (sin y, 0, cos y)); not separately proven for props. Tilted points
  take the re-spawn path; whether removing the entity closes an open prop settings window is untested.
- **L20-e** Prop part bool +0x490 ("enabled", entity +0x4e9c, PlayerUpdatePacketSetEnabled) is sent false for every
  prop; its readers are area / interaction code (part_0117). Worth a trace for movers / Linking Areas.
- **L117-a** Giant `Stance_Index > 0.5` on the reaction-mixer transition: runtime check (Sitting on a Frostbite Giant).
- **L117-b** Player-network creatures with Kobold / Skeleton / Slaug / LesserGiant anim sets: emotes assumed to use the
  shared Biped_Core clips; runtime check.

## 4. Test steps (0.695 test copy, dev world restart needed for the build)

1. On an owned site in Edit Mode place a Counting Node, Scoreboard, Story Node, Logic Filter, Respawn Point (Enabled
   ticked) and note the Starting Location. Press F5 (Play): Counting Node, Story Node, Logic Filter, Respawn Point and
   the Starting Location disappear; the Scoreboard stays and counts. Server log: "Site N: k story tool(s) hidden for
   Play Mode."
2. Still in Play Mode press Tab: the Starting Location appears; Tab again: it goes.
3. A second character arriving on the landscape while the site plays sees none of the hidden tools.
4. Story Node text-only in Play Mode: "In Chat Bubble" shows nothing; "In Chat" prints the text without "[Story Node]
   says:"; "On Screen" works.
5. F5 (Edit): all tools come back; the Counting Node shows its Starting Count; Logic Filter lights off.
6. Respawn Point: untick "Enabled" in its settings — its look changes at once (other idle); tick — back. Rotate a point
   and toggle again: its facing must not change. Relog: the look matches the box.
7. L117: a Story Node (Sitting, Wave, Angry) linked to a Darkfang Specter spawner and to a Frostbite Giant spawner:
   nothing plays (a running action stops); the same node on a Scalewind / Slaug spawner plays all three.
