# Open leads, third pass — group C (client behaviour, packets, story tools, creatures, travel / caves), 2026-10-17

Read-only pass over L21 L22 L23 L28 L33 L35 L36 L41 L51 L54 L57 L58 L84 L85 L94 L96 L97 L99 L104 L107 L117 L118 L129
L130. Each lead started from its OPEN-LEADS.md entry and its pass-2 / sweep text (agent-leads2-a/b/d2.md,
agent-leads-sweep.md, leads-L33-L35-L36.md); only paths not tried before were followed. Sources: re/decomp_all_695
(0.695), re/decomp_all (0.589), <0.695 test copy> (0.695 test copy: Landmark64.exe bytes, Resources, CommonData caves,
read only), gameData695, re/as3 / re/as3_695, re/out text tables, re/out/agent_funcs, design/wiki. Addresses are 0.695
unless marked 0.589. Scratch scripts (not in the repo): scratchpad va.py (read exe .data), cpa.py / cpa2.py / cpa3.py
(.cpa tile records), l41.tsv (call-graph relabels).

**[P]** = proven (address / file / table row / byte read). **[I]** = inferred (reason given).

## 1. Summary

| Lead | Verdict | One line |
|---|---|---|
| L21 | CLOSE-BY-DESIGN | Everything the client can show is built (3010 / 3011 props, 577664-577692 events / actions, EditableProperties row 109, "On Game Ended" 573011). No 0.695 prop uses models 1742 / 1743, so nothing can place an Audio / Group Node. |
| L22 | ADVANCED | The twin text **577105 "The story on this Build Site has been saved!"** (Claim.StorySaveSnapshot.Message) belongs to Edit → Play (P5276). The server sends 577089 on reset but never 577105. The Claims snapshot packets 0xF9/0x13-0x16 are terrain snapshots, not this message. |
| L23 | UNCHANGED | Firing rules are server-side only; nothing in the client decides them. |
| L28 | CLOSE-BY-DESIGN | No 0.695 reader of InspectProxy +0x110 / +0x11c / +0x118 (pass 2 + sweep). Any value is invisible, so 0 / empty is correct. A runtime read would only show our own values. |
| L33 | CLOSE-BY-DESIGN | Built: Claims.Entered, and the reward-chest notice 577634 (RewardChests.cs:66, Claims.cs:419). The client reads no reply. |
| L35 | CLOSE-BY-DESIGN | The refusal-text set is complete (pass 2). Only a live capture could show which carrier live used. |
| L36 | FOUND (existing) | leads-L33-L35-L36.md; built in VoxelDestruction.PlayEffect. |
| L41 | ADVANCED | 141 of the 584 `how = none` functions resolve through 1-3 caller levels. 74 of those are dispatcher noise. **67 usable relabels [I]** in §2. |
| L51 | **FOUND [P]** | A "cave beginning location" is a .cpa tile record with byte 32 set. Its type is byte 38: **1** = surface entrance, **2** = Pioneer isolated cavern, **3** = cavern graveyard (Graveyard01, tile 111). Only type 3 goes into the random-pick table: exactly the **81 Graveyard01 pieces** of each classic island. |
| L54 | FOUND (existing) + mail checked | Mail needs no server escaping. The client HTML-escapes subject and message (FUN_142d08920: & < >), and the mail window does not detokenise. |
| L57 | CLOSE-BY-DESIGN | Settled 2026-10-17 (DesignMaterials re-sends the currencies). |
| L58 | FOUND (existing) | Runtime 2026-10-17: the flag arrives; no change. |
| L84 | FOUND (existing) | Runtime 2026-10-17: the rows show; no change. |
| L85 | ADVANCED [P] | Packet **+0x94 → entity +0x158c is an ActorCompositeEffect id** that the actor plays once loaded (FUN_142eb12d0 ← FUN_142eb6a10). 0 = none, which is what we send. |
| L94 | FOUND (existing) | Runtime 2026-10-17. |
| L96 | FOUND (existing) | Runtime + built (9fd07a0, a2f6ced). |
| L97 | UNCHANGED | No new static path. The interim (PvPAllowed for 2 / 7) agrees with IsGameFFA defaults. |
| L99 | ADVANCED [P data] | Pioneer Underground .cpa: **one type-2 beginning location per isolated cavern** (957 on 440 … 1007 on 442; each in its own connected cavern). Our FindCavern walks all 23,168 piece-104 middles instead. |
| L104 | CLOSE-BY-DESIGN | The personal waypoint never reaches the server (L136). /goto waypoint answers with an error line. |
| L107 | CLOSE-BY-DESIGN | User decision (no vehicle / weapon / ammo data, PlacedEffect is a debug tool). Nothing new in the client. |
| L117 | FOUND (pass 2) | Emote / stance / mood play only on player-network actors. Skipping the send for other models is optional. |
| L118 | FOUND (existing) | 2026-10-16. |
| L129 | ADVANCED [P mechanism, I use] | The server-half GetRandomCaveBeginningLocation ("Player attempting to roll random cave") rolls among the **type-3 cavern-graveyard pieces**, never the current one. It returns the piece's global position and cave id. |
| L130 | **ADVANCED [P]** | Node +0x24 is **not** a discovered flag. It is the node's location key, compared from-vs-to for HubCost. The client has no discovery state; the +0x248 default (11674) has no reader. **Cost bug:** the client shows hub → crystal = 1 Lumen, but the server charges 2. |

---

## 2. Per lead (new this pass)

### L130 — Local Travel nodes, discovery and cost: ADVANCED [P]

**Node record.** Reader FUN_143502390, filled through FUN_143502a10. Layout: +0 u64 id, +8 u32 type, +0xc u32 biome,
+0x10 u64 cell, +0x18/+0x1c/+0x20 f32 x y z, **+0x24 u32**. The +0x24 default is DAT_145262608 = **-2** (exe .data read).

**Who reads +0x24 [P]: one function only.** FUN_143505c90 (0.589 FUN_143453b90) is a node lookup by guid in the manager's
+0x2a0 table; it returns +0x24. It has three callers, all in ControllerLocalTravel (ctor FUN_1430552a0):
- FUN_1430555d0 / FUN_143055ba0 store the current node's +0x24 at controller +0x2ac (default DAT_145229eb8 = -2) and
  its guid at +0x2b0.
- FUN_143055480 is the click on a node. It builds `from = {zone, world, +0x2ac, guid +0x2b0, type}` and
  `to = {zone, world, target +0x24, target guid, type}`, then calls FUN_143504a80.

**FUN_143504a80 is the confirm cost [P]:**

    BaseCost(+0x230)
      + NodeCost(+0x240) if from.type == 2
      + WorldCost(+0x234) if world differs
      + ZoneCost(+0x238) if zone differs
      + HubCost(+0x23c) if from.+0x24 != to.+0x24 or zone / world differs
      + NodeCost if to.type == 2

It then stores the target at manager +0x802a8 and raises `GameEvents:OnOpenTravelConfirmation`. So +0x24 is a
per-node location / layer key. It is not a flag, and nothing else reads it.

The 0.589 `/warpnetwork` debug list (FUN_143452280) adds HubCost when the layer id (+0x228) is not -1. It also
always adds NodeCost. That list is a debug estimate, not the confirm.

**The cost settings [P].** FUN_143505fd0 fills manager +0x230..+0x248 from StringHashToValue by one-at-a-time hash:

| Field | Key (hash) | Default | Result in 0.695 data |
|---|---|---|---|
| BaseCost | 0xbcefa619 | 0 | 0 |
| WorldCost | 0x2e0994b0 | 10 | 0 |
| ZoneCost | 0xdad4f9c4 | 5 | data key has a trailing space, so the default **5** applies |
| HubCost | 0x21057e38 | 2 | **0** |
| NodeCost | 0x239f0d46 | 1 | data key has a trailing space, so the default **1** applies |
| CostItemId | 0xde44021a | 1579 | 1579 |
| +0x248 | 0x57903b24 | 11674 | no name in StringHashToValue |

- I tried 90 candidate names for 0x57903b24 (WarpNetwork./Teleport. × Effect / Composite / Discovery / Arrival /
  Ability variants); none matched.
- The manager +0x248 value has **no reader** in 0x1434f8000-0x143508000 or in any user of DAT_14543a788
  (part_0067 / 0068 / 0082 / 0088 / 0104 / 0113 / 0114).
- So "Leyline discovery fanfare" is only a name the code comments gave it. It is not proven.
- WarpNetwork 0x113 has only subs 2 and 4 (coverage-695.md), so no packet carries a discovered state either.

**Conclusion [P / I].**
- The client keeps no per-player discovery state for warp nodes [P].
- If live had discovery (5 May 2015 notes), the server filtered which nodes it put into WarpNetworkZoneNetwork and
  played any fanfare through a generic composite packet [I].
- Our "every node open" stays a choice; to add discovery, the server would send only the discovered nodes.

**Cost mismatch [P].** In the client's confirm (data as shipped):
- hub → crystal = **1**
- crystal → hub = 1
- crystal → crystal = 2
- + 5 for another zone

WarpNetwork.Warp charges `1 + (target is crystal ? 1 : 0)`, so hub → crystal costs **2**, one more than the window
showed. See §3.

Doc fix: WarpNetwork.cs:42 cites "client FUN_143452280 / FUN_143453b90" for the cost. Those are 0.589 addresses, and
FUN_143453b90 is the +0x24 lookup. The 0.695 cost is FUN_143504a80; the settings are read by FUN_143505fd0.

### L51 / L129 — cave beginning locations and the random roll: FOUND / ADVANCED [P]

**The record fields, in memory and on disk [P].** LoadContinentCaves (FUN_143ffcde0, part_0163 ~27700-27950) walks
0x30-byte in-memory tile records. They map one-to-one onto the 39-byte .cpa records that Caves.cs reads:

| Disk | Memory | Meaning |
|---|---|---|
| @0 u32 | +0 | tile (VoxelCaveTileDefinitions) |
| @4 / 12 / 20 f64 | +8 / +0x10 / +0x18 | x, y, z (global dm) |
| @28 u32 | +0x20 | rotation |
| **@32 u8** | +0x24 | **beginning-location flag**; AddCave FUN_143f813e0 is called when it is set |
| @33 u32 | +0x28 | property group (biome lookup, "Invalid Property Group Id" log) |
| @37 i8 | +0x2c | passed as char to the tile-instance ctor FUN_143fa1d20; -1 on surface entrances, 0 underground |
| **@38 u8** | +0x2d | **beginning-location type** (AddCave's 2nd argument) |

**Counted in every v14 file of the 0.695 test copy [P]:**

| Continents | Flagged pieces | Type |
|---|---|---|
| 391-416, `_Underground` | **81 per island**, all tile **111 Graveyard01** ("IS_GLOBAL_TILE"), rotation 0, a 9 × 9 grid ~700 m apart | **3** |
| 391-416, `_Surface` | 210-479 per island, tiles 48 / 49 / 50 Enterance01-03 (IS_ENTRANCE) | **1** |
| 440-444 (Pioneer), `_Underground` | 950-1007 per land, tiles 102 / 109 / 112 / 113 CavernCorner01-04 | **2** |
| 432, `_Underground` | 61 | 2 |

**The random-pick table [P].** AddCave appends every flagged location to +0xd68 (count +0xd78). Only for **type 3** does
it create a pick node: id from the global counter DAT_14560bd30, type 3, the location; the node goes into the type
list +0x1490 / +0x14b8. GetRandomCaveBeginningLocation (FUN_143f84420):
- logs "Player attempting to roll random cave with no caves loaded!" when nothing is loaded;
- picks a random entry (the game RNG DAT_1455f09f8) whose id differs from `param_4`, the current cave;
- with `param_5`, picks inside that type's list;
- returns the location (24 bytes = the three f64) and the cave id.

So live's random underground roll landed on a **cavern-graveyard piece**. These are the same 81 pieces per island
that carry our warp crystals (WarpNetwork) and cavern graveyards (Graveyards, agent-graveyards.md §1) [P data + code].

**What is still [I]:**
- That live's server called this for TeleportRandomUnderground. The function has no client caller; its log text names
  a player roll.
- Whether one manager held every landscape's caves. The pick ids come from a global counter across all AddCave calls,
  which would make "any zone" (P5927) a single roll over all landscapes.
- Before 30 Jun 2016 the trips stayed on one landscape and repeated caverns (pass 2: 3 reports). That fits 81
  graveyards per landscape.

L47 hand-off: the tile records alone already answer "which records have +0x24 set and type 3". No CavernInstanceData
decode is needed for L51.

### L99 — Pioneer caverns: what remains (server already on 464-468, 0b3247c) [P data, I use]

- Pioneer.FindCavern takes every tile-104 CavernMiddle (23,168 on 440), nearest the centre first.
- The client's own data marks **one beginning location of type 2 per isolated cavern**: 957 on 440.
  - A flood fill over the 64 m grid (cavern-piece tiles 101-117, scratch cpa3.py) puts every flagged piece in its own
    connected cavern; no two share one.
  - Nearest-neighbour spacing: median 344 m, minimum 186 m.
  - All have property group 233, rotation 1, y 250-2746 m (global).
- That fits P4300, "isolated caverns … reserved solely for auto-placing Build Sites" [I]: the type-2 flag is the
  per-cavern site anchor.
- Still open, as before:
  - live's maximum depth (MaxCavernDepth 180 m is ours, TASKS 6.47);
  - one HeightLayerIds_440 for all five lands [I].

### L22 — story reset / save messages: ADVANCED

- [P] CodeStringMappings rows 102 / 103 hold both keys: `Claim.StoryReset.Message^577089` and
  `Claim.StorySaveSnapshot.Message^577105`. Both texts exist in text_589 and text_695.
- [P] P5276 (11 May 2016): "When you enter Play Mode, the current state of all props on your Build Site will be saved."
  So the "saved!" text belongs to Edit → Play [I strong], the way the "reset!" text belongs to the Reset Node.
- [P] Today the server sends only 577089 (StoryTools.cs:222 / 2035 via Emotes.ShowLine). Nothing sends 577105.
- [P neg] The Claims snapshot packets (dispatcher FUN_142dbecb0 cases 0x12-0x15: SnapshotSaveConfirmRequest,
  SaveResponse, RestoreConfirmRequest, RestoreResponse) handle claim terrain snapshots. Their failure branch prints the
  key "Claims.Error.UnableToSaveSnapshot"; their success path raises Lua events
  (OnClaimSaveSnapshotConfirmation / OnClaimRestoreSnapshotConfirmation, the 0.695 SnapshotPanel.as is an empty
  stub). Neither text is printed. So that is not the carrier.
- The carrier stays [I]: ChatPacketFromStringId, as for 577089.

### L54 — mail and other text: checked [P]

- The 0.695 mail window fill FUN_14363a660 runs Subject (FUN_143c83320) and Message (FUN_143c83310) through a length
  cap, then FUN_142d08920. That function replaces `&` → `&amp;`, `<` → `&lt;`, `>` → `&gt;` (strings at 0x1446a7e30 /
  0x144684ba0 …) before the html fields (EmailMessageWindowDocumentClass.as:239-242). The sender (a character name)
  is not escaped.
- Only Chat, NotificationWindow and SpeechBubbleWidget detokenise (grep as3 / as3_695).
- So mail needs no server-side token or HTML neutralising. Chat, guild MOTD and site names are already covered (L54
  entry). The social ChatService routes the line the world already neutralised in Chat.cs:101 [I: from reading
  ChatService.cs:90-96; the world-to-social forward was not traced].

### L85 — LightweightToFullNpc +0x94: ADVANCED [P]

- The apply copies +0x94 to entity +0x158c (part_0067:6192). LightweightToFullPc fills the same field
  (part_0065:20495).
- Its only reader is FUN_142eb12d0 (called from FUN_142eb6a10, the actor post-load step). When the value is non-zero,
  it looks it up in **DAT_145431058 = ActorCompositeEffectDefinitionManager** (created part_0063:27681-27690) and plays
  it through FUN_142eb7e10.
- So +0x94 is a composite effect the creature shows from load (an aura / idle effect). 0 = none, which we send.
- +0x244 → +0x14b0 has only a setter (FUN_142d4cd20). The other part_0180 hits are an FMOD object.
- +0x14b4, +0x4aa8: no game-side reader found in this pass.

### L41 — relabels by deeper callers [I]

Method: for each of the 584 `how = none` rows of re/out/agent_funcs/final.tsv, walk callers (index.pkl) 1-3 levels until
a caller whose label came from `name / direct / caller / callee`; take the majority label.
- 141 resolve.
- 74 of them all reach "PlayerUpdatePacketRemoveFeatherweightObject" through the PlayerUpdate dispatcher. I rejected
  these as noise.

The remaining 67 [I, by caller]:

| Caller label | Functions (0.695) |
|---|---|
| ClientInstancedGameManager | 1434d9330 (d1), 1434d6b10, 1434d7120, 1434d9860 (d2), 1434d77c0, 1434d7f90, 1434d8180, 1434d9a50 (d3) |
| InstancedGame packets / Lua | 1434d5e90 AddOrRemoveRuleRequest, 1434d5fd0 MatchmakingRequest, 1434d8660 TriggerParameter (d1); 1434d62d0 PropRequest, 1434d6620 Lua ResetGameSettings (d2); 1434d8bd0, 1434d8cd0 Lua ResetGameSettings (d3) |
| Template materials (buy missing) | 143100af0 RecipeCraft, 1430fe830 PurchaseTemplateMaterialsDataRequest (d1); 1430fe6d0, 143100790, 143100bc0, 1430ff330 EnvironmentAddOwnershipResponse (d2); 1430fdf80, 1430fe790 (d3) |
| Claims | 142d9fc20, 142da1a50 (LostClaimServerConnection path, d1 / d2); 14348f270 Lua Claims.ActivateCameraCharacter (d1) |
| Props | 142d41020, 142d444a0, 142c15ee0 PropProcessor; 143490550 PaletteMenuIndicator (d3) |
| Player / movement | 142c13350 SetPlayerBio (d1); 142c141c0 ClientWaypointManager::GiveTime (d2); 142fa4250 BC::GiveTime (d3); 142c03170 MeleeStabStart (d2) |
| Templates / areas | 14348f530 ClientTemplateManager; 14319c340 AreaPropertyDefinition (d1) |
| ClientLua | 1439dfd30, 1439e0aa0 (d2); 1439df840, 1439e0070, 1439e01b0, 1439e04a0, 1439e73b0, 1439e5890 (d3); 1439e49c0, 1439e7120 Lua.CreateObject (d3); 1404fac50 Lua.NaN (d2) |
| Engine / third party (no game logic) | 14063cf70, 1405cd690, 1405d9270, 1405e7e20, 1406029a0, 14061bb30, 14062c560, 14062d120 DefaultFileOpener; 144321840, 144321be0, 14434a750, 144379640 EngineFileRequestHandler; 1444032b0 AudioDriverFMOD; 1444c8920, 1444c9e40, 1444e2ba0, 1444e3400 fmod flac; 143803f70 SoeBuffer; 142c6eac0, 142c6eb10 EH funclets |

(d1 / d2 / d3 = caller depth.) 443 functions still have no labelled caller within 3 levels. Next: data references
(vtables Ghidra did not type) and the Ghidra project's xrefs (re/Landmark695).

### Paths tried this pass with no result

- L130: StringHashToValue names for 0x57903b24 (90 guesses); readers of manager +0x248 (above); composite 11674 in
  gameData695 (no composite table there; InteractableCompositeEffects has no 11674 row).
- L22: Claims snapshot S→C handlers (above); the 0.695 SnapshotPanel / ClaimManagementWindow as3.
- L85: readers of +0x14b0, +0x14b4, +0x4aa8 outside the apply.

---

## 3. Server work (evidence-backed)

1. **WarpNetwork cost = the client's confirm (L130) [P].**
   - File / function: `Game/WarpNetwork.cs` → `Warp(session, from, to)`.
   - Today: `uint cost = NodeCost + (target.Type == CrystalType ? NodeCost : 0);`
   - New: `cost = (fromNode?.Type == CrystalType ? 1 : 0) + (target.Type == CrystalType ? 1 : 0)`, plus 5 when the
     target is on another landscape (ZoneCost default), plus 0 for another world (data WorldCost 0) and 0 for HubCost
     (data 0).
   - `fromNode` = the node with id `from` (NodesFor + the hub). If none, treat it as the hub.
   - Evidence: FUN_143504a80 and FUN_143505fd0 with the shipped StringHashToValue rows 675-680.
   - Effect: hub → crystal becomes 1 Lumen, as the Confirm Travel window shows (today 2).
   - Also fix the class-notes addresses (WarpNetwork.cs:42).
2. **WarpNetwork node trailing u32 (L130) [P field, I value].**
   - File: `WarpNetwork.cs:287`. Write the node's layer key (`node.Layer`) instead of `0u`.
   - The client compares it from-vs-to for HubCost (0 in data, so no visible change today). This keeps a future
     HubCost correct.
3. **Underground Adventure via the CaveManager roll (L51 / L129) [P mechanism, I use].**
   - File / function: `Game/Caves.cs` → `TravelRandom`.
   - Option: keep a per-landscape list of the type-3 beginning locations (tile records with @32 ≠ 0 and @38 == 3 =
     the 81 Graveyard01 pieces; Graveyards.Landscape already has them) and roll among them, never the player's current
     one (FUN_143f84420). Arrive at the cavern graveyard's arrival point (Graveyards).
   - Today: a random tile-104 cavern above MinTravelDepth.
   - Needs the user's choice: the graveyard roll is what the client's server code does. P5927 (30 Jun) says trips go to
     "Chaos Caverns in any zone", which today's cavern pick serves. One combination: roll a landscape (P5927), then a
     type-3 location on it (CaveManager).
   - Also document the record fields in the Caves.cs class comment (§2 table): @32 flag, @33 property group, @37 i8,
     @38 type 1 / 2 / 3.
4. **Pioneer cavern sites from the type-2 anchors (L99) [P data, I use].**
   - File / function: `Game/Pioneer.cs` → `FindCavern`.
   - Iterate the flagged type-2 records (one per isolated cavern) instead of every tile 104, so two sites never share a
     cavern and the scan drops from 23,168 to ~957 per land.
   - Site centre: the anchor's cavern. Use the nearest tile-104 middle connected to the anchor (64 m grid), or the
     anchor + half a piece.
   - Keep the depth check (MaxCavernDepth, still [I]).
5. **"The story on this Build Site has been saved!" 577105 on Edit → Play (L22) [P text / key, I trigger and carrier].**
   - File: `Game/StoryTools.cs`.
   - In the owner's Edit → Play path (the caller of `PlayModeChanged(claim, true)` that is not ResetStory), send 577105
     with the same carrier as 577089 (`Emotes.ShowLine(PlayersOn(site), 0, 577105)`).
   - P5276 says the state is saved on entering Play Mode.
6. **Optional (L85).** No change needed: +0x94 = 0 means "no composite". If a creature should glow or carry an aura, put
   its ActorCompositeEffect id in PlayerUpdateLightweightToFullNpc +0x94.
7. **Optional (L117).** Skip sending Story Node emote / stance / mood for actors not on PlayerAnimationNetwork64
   (pass 2 list).

## 4. Runtime tests wanted (coordinator)

1. **L130 cost (no Frida needed).**
   - Steps: at the Spire, open Local Travel. Pick an underground crystal. Read the cost in Confirm Travel (expect 1).
     Confirm, and compare the Lumens taken.
   - Before fix 1: 2 are taken. After: 1.
   - Optional Frida (attach and detach cleanly, never kill with hooks attached): hook **0x143504a80**. Log rdx and r8
     as `{i32 zone, i32 world, i32 key, pad, u64 guid, i32 type}` at +0, +4, +8, +0x10, +0x18. Also hook **0x143504c60**
     and log edx (the cost).
2. **L22.** After fix 5: as owner, switch Edit → Play. The chat window should show "The story on this Build Site has
   been saved!". Then trigger a Reset Node; expect "…has been reset!".
3. **L85 (optional).**
   - Steps: with a dev build, send a LightweightToFullNpc with +0x94 = a known ActorCompositeEffect id. Watch for the
     effect on the creature.
   - Frida: hook **0x142eb12d0** and read [rcx+0x158c] to confirm the value reached the actor.
4. **L54 mail (optional).** Mail yourself the subject `<b>x</b> ~[~b~]~`. Expect both shown literally (client escape
   FUN_142d08920, no detokenise).
5. **L129 / L51.** Nothing on the client: the roll is server-side. After fix 3, check that a trip lands at a cavern
   graveyard and that repeated trips never return the same one twice in a row.

## 5. Hand-offs

- **L47 (cave .cpa sections):** the tile-record byte @32 is the beginning-location flag and @38 its type (1 surface
  entrance / 2 Pioneer isolated cavern / 3 Graveyard01). L51 needs no CavernInstanceData decode. The v12 / v13 files
  (224, 325-389) use other layouts; my parser reads them as garbage.
- **L127 / L113 (Environment Server, procedural):** none.
- **Graveyards (L150):** the 81 type-3 locations per island are exactly the Graveyard01 pieces the cavern graveyards use.
  The client's CaveManager calls them "cave beginning locations".
