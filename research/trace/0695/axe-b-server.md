# Axe task B (server side): why the axe chops / interacts with anything (the Spire, props)

2026-10-31. Read-only trace (no code changed, no build, no game / Frida). Repo commit 91c5a566 (the one the dev server
runs). Sources: LandmarkEmu/LandmarkEmulator.WorldServer (file:line below), the user's session log
`server-data/worlds/dev/console.log`, re/decomp_all_695 (fn.py), gameData695 tables, re/out/text_695.tsv, the item dump
`Resources/PacketDumps/44-CommandBase` (LZ4-decoded in a scratch script), trace/0695/patchnotes.tsv, earlier notes
healthbar-b-harvest-target.md, healthbar-d-sources.md, healthbar-e-server.md, MODLOG.md. **[P]** proven, **[I]** inferred.

## 0. Short answer

1. **The client decides what an axe may target, from the ability data the server sends.** The axe's CasterInfoReply
   field +0x90 (our `AbilityCasterInfoReply.U90`) is the ability's *target requirement* (a RequirementExpressions id).
   We send 0, and id 0 always passes, so every entity under the cursor (the Spire, placed props, NPCs, players, rocks,
   plants) is a valid axe target: the logging cursor locks onto it and the swing fires. [P, §2]
2. **The server then confirms every logging swing with the chop animation**, whatever was hit: `Abilities.Answer(...,
   swung: true)` sends CastSuccess with AnimationId 254 (axe Enter) and, 350 ms later, 255 (Release), and
   `Emotes.ShowSwing(254)` shows it to the others. The client plays the swing only from that CastSuccess, so this is the
   answer that makes the chop play on the Spire. [P, §1]
3. Server-side holes on top: `Gathering.Target` chops the **nearest tree within 3 m of the aim point** when the cast names
   a non-tree entity, so a swing at a prop / creature / the Spire beside a tree fells that tree; `TargetGuid` falls back
   to `session.CurrentTarget` when the cast names nothing; a wrong-target swing gets no message. [P code, §1.3]
4. "Interact": every completed click on an entity sends SelectPlayer + RequestSetTarget + InteractRequest whatever is
   held (client FUN_142cbed70). The server confirms any target slot and does nothing for InteractRequest on the Spire hub /
   props / trees. That is not axe-specific; the axe-specific part is the chop highlight and swing on everything (1). [P, §3]
5. **Fix (server):** send the axe abilities' target requirement **20 "Target is Harvestable Tree"** (TargetIsProfile
   200-214) and give tree nodes their profile id (200 + 3 × size + wood) in the AddLightweightProp field at +0x1a0;
   then only trees are axe targets on the client. Server-side, only play / broadcast the chop when a tree was really hit,
   drop the nearest-tree fallback when the cast names another entity, and say "It is not possible to harvest that." [§5]

## 1. What the server does with an axe swing today

Path: `Abilities.HandleCast` (Game/Abilities.cs:123-358).

| Step | Code | What happens for a swing at a non-tree |
|---|---|---|
| source item | Abilities.cs:143-145 | the equipped axe lists 1020 / 176 → found [P] |
| combat gear? | Abilities.cs:270, 1192-1195 | no: harvest tools are excluded [P] |
| pace | Abilities.cs:318-322 `TooSoon` | a swing sooner than 50 / speed × 0.85 s is answered `Answer(attempt)` with no animation [P] |
| others see the swing | Abilities.cs:332-333 | `Emotes.ShowSwing(session, 254, swingAt)` **before** the chop is checked [P] |
| chop | Abilities.cs:341-343 → `Gathering.Chop(session, TargetGuid(attempt, session), aimedAt, tool)` | returns false, nothing gathered [P] |
| answer | Abilities.cs:357 `Answer(session, attempt, swung: used && IsHarvestAbility(...))` | **swung = true even when Chop returned false** [P] |
| Answer | Abilities.cs:404-449 | CastSuccess AnimationId = SwingAnimation(1020/176) = 254; +350 ms CastSuccess 255; +0.75 s AbilityEnd [P] |

`Gathering.Chop` (Game/Gathering.cs:320-363) → `Target` (452-472): when the guid is a node (top byte 0x40, GuidBase
Gathering.cs:142) it is looked up; **otherwise the standing tree nearest the aim point within `AimTolerance` 3 m
(horizontal, Gathering.cs:138) is taken**, provided it is within `Reach` 8 m of the player (137). No tree → log "swung at
… with no tree in reach", `WrongTool` (389-405), which returns at once for a non-node guid (391-392): no message. [P]

### 1.1 The user's session (console.log) [P]

Axe casts (1020 / 176) in the log: 2644, all by TestPlayer; every one carried an entity guid at tail +36 (none came without
one, matching MODLOG.md:451 "Swinging only fires with the cursor over a harvestable entity").

| Target (tail +36) | Casts | Where | Server |
|---|---|---|---|
| gathering nodes 0x40… | 2404 | throughout | chopped |
| **the Spire hub 0x53500001CC000001** (Spire.cs:43 GuidBase 0x5350…) | 72 | 07:31:30, line 118017 on | "swung at 53500001CC000001 … with no tree in reach" (118018) → CastSuccess **AnimationId 0xFE = 254** (118022) → **255** for the same cast hash C4-9B-29-39 (118136); later casts in the pace window answered with 0 |
| the user's placed props 0x20001A5 / A3 / A9 (Props.cs:474 guid range; a platform pack, 2005) | 91 / 53 / 24 | 06:45:38-45, line 31330 on | same: "no tree in reach", CastSuccess 254 (31333) |

Every swing also brought an AbilityCollisionReport naming the entity (e.g. 118021 "collision report:
0x53500001CC000001"); MiscHandler.cs:43-44 only logs it. The CasterInfoReply the server sent for 176 at 06:23:12 (line
4499), decoded with re/caster_info.py: MaxRange 6, U68 1, Ua0 0 and **u_90 = 0**. [P]

### 1.2 When the cast names an NPC or terrain [P code]

- A creature: the same path (the axe is not combat gear), Chop finds no node → chop animation, no damage. The client can
  swing at a *hostile* creature even with a proper requirement (FUN_142ea9e60 fallback, §2).
- Terrain / nothing: the client does not fire the logging swing without an entity under the cursor (0 of 2644 casts);
  if a cast came with tail +36 ≤ 0xFFFF, `TargetGuid` (Abilities.cs:865-873) would use `session.CurrentTarget`, which
  after a tree click or the first swing (`HarvestTarget`, Gathering.Health.cs:200-209) is a tree: it would be chopped from
  up to 8 m away. [P code; not seen in this log]

### 1.3 Server holes, independent of the client

1. Chop animation and the others' ShowSwing for swings that hit nothing (Abilities.cs:332-333, 357).
2. The nearest-tree fallback runs although the cast **names** another entity (Gathering.cs:460-464): a swing at a prop,
   a creature or a player standing within 3 m of a tree fells that tree.
3. No feedback for a non-tree target (WrongTool only handles nodes, Gathering.cs:391).

## 2. Why the client lets the axe target anything [P]

- CasterInfoReply reader **FUN_142e016b0** reads, in wire order, … +0x80 MaxRange, +0x84 MinRange, +0x68, two strings,
  +0x6c, **+0x90**, +0x94 … — our writer's `U90` (AbilityMessages.cs:41, written at :108, after ArcDegrees) is +0x90.
- Store **FUN_142e187a0**: reply +0x90 → ability record +0x70 (`puVar4[0x1c]`); +0x80 → record +0x18, +0x84 → +0x1c.
- **FUN_143458f20** (tool-mode set-up, part_0105:15180-15320): for the held item's logging ability (FUN_143b7a3e0) and its
  reaping ability (FUN_143b7a4c0, the one that also gets the arc, SetParam 11) it calls the current action state's
  SetParam(8, **FUN_142e17320** = record +0x70), SetParam(9, record +0x18 + stat 15 Reach), SetParam(10, record +0x1c).
  (The ability-slot types, from the item dump: type 3 primary 1018 / 1036, **type 2 logging 1020 / 176 / 1037**, type 1
  reaping 153 / 155.)
- ActionStateLogging (vtable 1447de218) SetParam int **FUN_143734370**: 8 → state +0xb0; float FUN_143734220: 9 → +0xb4,
  10 → +0xb8.
- Logging cursor handler **FUN_1437327b0** (mouse move 0x200 / 0x1000): takes the entity under the cursor and evaluates
  `ClientRequirementManager vtable +0x10 = FUN_1432f5a00(mgr, state +0xb0, local player, entity)`. **FUN_1432f5a00
  returns true for any id < 1** without looking anything up. If the requirement fails and FUN_142ea9e60(entity) is false,
  the target is dropped (state +0xa8 cleared, cursor state 0); else the in-range flag DAT_1452a9561 is set from the
  distance against +0xb4 / +0xb8. FUN_142ea9e60 = "attackable" (+0x1459 bit 0x20 clear and (+0x1452 bit 2, +0x1457 bit 2,
  or disposition 0 and not +0x1460 bit 4)). Reaping FUN_1437324d0 does the same with its own +0xb0.
- FUN_143734540 (Logging vtable slot 15) returns false only while the logging ability is set and DAT_1452a9561 holds
  [P]; its callers use it to decide whether the tool takes the click [I meaning; matches MODLOG 451].
- The table FUN_1432f5a00 searches (+0x70c0) is the one FUN_1432f5870 uses for HUDIndicatorSettings CLIENT_REQ_ID, i.e.
  **RequirementExpressions ids** (healthbar-b §2) [P same table; I that ids are expression ids, strong].

So with U90 = 0 the requirement always passes: the axe locks onto the Spire, props, NPCs and players alike. `U90` is never
set anywhere in the server (grep) [P].

### 2.1 Which requirement fits

gameData695/RequirementExpressions.txt [P data]:

| id | expression | note |
|---|---|---|
| 8 / 19 | TargetIsTree* (profiles 56-58, 87, 100-105) | 2014 tree profiles; 0.695 reuses 56-58 for creatures ("COMBAT", model 444): stale (healthbar-d §2) |
| 14 / 15 / 16 | "Tier 4 Tree or less" / "Tier 2 Tree or less" / "Basic Tree - Non-teirs" | the per-tier axe sets before P0471 [I] |
| **20** | TargetIsProfile200 … 214 — "Target is Harvestable Tree" | ProfileDefinitions 200-214 = "Tree: Very Small … Very Large: Plain / Burled / Striped Wood" (text_695 562065-562079) |
| 22 | profiles 90 / 91 / 296 — "Target is Plant Profile" | sickle |
| 34 | 22 + 20 | extractor-like "tree or plant" |

ClientRequirementIsProfile **FUN_143710dd0** compares the entity's **+0x1474**; +0x1474 is set by FUN_142ec55e0, which the
AddLightweightProp handler **FUN_142cde1c0** reaches through FUN_142d54040 with packet **+0x1a0** (callers of FUN_142ec55e0:
FUN_142cb1f60, FUN_142cd9980, FUN_142cdd710 featherweight, FUN_142cdfa60 PlayerUpdate, FUN_142d54040) [P]. That is the
unnamed `writer.Write(0u)` right after ControlMode, PlayerUpdateAddLightweightProp.cs:126 (healthbar-e §3) [P]. Today
every node, prop and Spire piece sends profile 0, so requirement 20 would match nothing until trees carry a profile.

## 3. Interacting while holding an axe [P]

- A completed click on an entity runs **FUN_142cbed70**: CommandPacketSelectPlayer 0x1E/0x16, SetCurrentTarget
  (FUN_142ca6f00 → RequestSetTarget) and, when BaseClient +0x32709 is set, InteractRequest 0x1E/9 via FUN_142d4ac90
  (throttled to one a second). Nothing there looks at the held tool.
- The log shows it for every axe click: trees (4620, 8498, …), the user's props (31358 "interacted with 0x20001A5, which
  has no interaction"), the Spire hub (110834, 118070, 121898).
- InteractionHandler.HandleCommand (InteractionHandler.cs:42-98): the Spire hub, props without a use and trees fall
  through to "has no interaction"; stations, chests, the mailbox … open as they would with any item in hand.
- InteractionHandler.HandleTarget (328-381) confirms any RequestSetTarget: a node goes to slot 9 (Gathering.Targeted);
  the Spire hub / props (disposition 2 by default) to slot 8 — e.g. 07:14:11 `06-00 01-00-00-CC-01-00-50-53 08-00-00-00
  01`, the blue ally ring on the Spire. Again not axe-specific.
- Props.SendInteractionInfo (Props.cs:2203-2236) answers the ~1293 InteractionInfoRequests of the session for flagged
  entities (stations, chests, the Spire's usable props, editable props in build mode); it is tool-independent and trees
  are never flagged (no DynamicInteractionFlagging for nodes).

So the server does not "interact" with the Spire because of the axe; what the user sees is the axe's target highlight and
chop on everything (§2) plus the general click-to-select ring.

## 4. What live did

- P0471 (26 Mar 2014): "All trees can now be targeted while an axe is equipped, even trees the equipped axe cannot
  harvest." Targeting was a per-axe client rule (the tiered expressions 14-16), widened to "any tree" [P note; I link].
- P0472: "Attempting to harvest a tree that cannot be harvested by the currently equipped axe results in an error message
  explaining why." = the server's harvest failure (Harvest.Fail.Tier 560563 / Tier.Specific 562581 "[*resource*] requires
  the [*tool*] or better to harvest it.") [P texts]. Gone by 2016: P3108 (16 Apr 2015) removed harvest rating from axes
  and trees, "all trees can be chopped by all axes"; wiki Axe: "As of June 2016, any axe can be used to chop down trees".
- P0916 (7 May 2014): sickle plants "only highlight while inside the targeting cone" — the reaping state's own filter.
- P0726 / P2245: chopping became a one-click toggle (2014), "no longer take more than one click" (Dec 2014).
- So in 2016 the axe targeted **trees only** (plus hostile creatures through FUN_142ea9e60), from its ability data. The
  0.695 tree requirement is 20 (the only one naming the 0.695 tree profiles) [I strong]. Live's server answer to a
  non-tree logging cast is not recorded [open].

## 5. Proposed server change (exact)

**A. The axe's target requirement (the root cause).** Game/Abilities.cs `BuildItemAbilityInfo` (46-95), inside the
`IsHarvestAbility` block: for `LoggingAbilities` (1020, 176) and the extractors' logging-slot ability 1037 set
`reply.U90 = 20`. Rename `U90` → `TargetRequirement` in AbilityMessages.cs:41 with the doc (reply +0x90 → record +0x70 →
SetParam 8 → +0xb0, FUN_1432f5a00; 0 = anything). Optional, only when plants are harvestable (PlantsHarvestable, live off
P4128): `ReapingAbilities` → 22. Constant e.g. `HarvestableTreeRequirement = 20`.

**B. Trees carry their profile.** PlayerUpdateAddLightweightProp.cs:126: make the u32 a `ProfileId` property (default 0,
same bytes). Gathering.Health.cs `SetLife` (122), which both node builders call (Create 876-905, AddSetNodes 746-791):
for trees set `node.Spawn.ProfileId = 200 + 3 × TreeSize(model) + wood`, wood 0 for item 71 Plain, 1 for 72 Burled, 2 for
73 Striped (TreeSize 103 already gives 0-4). Plants (if A's 22 is used): 90 (one hit) — 91 / 296 for the larger kinds
(healthbar-e §5). Decor (rocks, bushes) stays 0. No extra bytes on the wire; the Spire and props keep 0, so they fail 20.
[I: no other client use of a tree profile found; ProfileDefinitions rows 200-214 have no model, abilities or camera]

**C. Server answers only what really happened** (Game/Abilities.cs:330-357):
- Run `Gathering.Chop` first and keep its result; call `Emotes.ShowSwing(session, 254, …)` only when it returned true,
  and answer `Answer(session, attempt, swung: chopped)`, i.e. CastSuccess with AnimationId 0 for a miss (the same
  answer as the TooSoon swings, which the held-button repeat already handles: hundreds in this log). Same for 1037's
  Chop / Reap branch (346-355).
- `Gathering.Target` (Gathering.cs:452-472): use the nearest-tree search only when the cast names no entity
  (`targetGuid == 0`); a named non-node entity → null.
- `Abilities.TargetGuid` (865-873) for logging casts: no `CurrentTarget` fallback unless it is a node whose base is within
  AimTolerance of the aim point.
- Chop's failure branch (Gathering.cs:323-327): for a named non-node entity, `HarvestFail(session, HarvestPrevent)` →
  the client's own centred "It is not possible to harvest that." (560975), on the existing 5 s `lastWrongTool` throttle
  [I: live's text for this case unknown; it is the client's generic one]. 
- Bandwidth: fewer packets (no 255 Release and no ShowSwing to observers on misses).

**D. Nothing to change** in InteractRequest / InteractionInfo / TargetBase for this bug (§3).

**Toggle (owner setting, default live):** e.g. `AxeTargetsTreesOnly` = on → A + B; off → today's U90 0. C is a fix, not a
gameplay option.

**Check in game (user's client, after deploy):** hovering the Spire, a placed prop, a friendly NPC or a rock with the axe
shows no chop highlight and clicking does not swing; a tree still highlights, swings and shows its bar; a hostile creature
may still be swung at (FUN_142ea9e60) and then shows "It is not possible to harvest that." with no wood.

## 6. Open

- FUN_143734540's callers (ActionManager wrapper FUN_14345a9a0): confirm the logging state refuses the click / swing
  when DAT_1452a9561 is 0 (expected: no swing at all on a failing target). Client-side task.
- Whether FUN_142ea9e60 is false for our Spire / prop entities (+0x1452 / +0x1457 bit 2 come from spawn flags we have not
  mapped): if true, requirement 20 would not stop the axe on them. Read the setters of those bits (FUN_142d54040,
  FUN_142ec28c0) or check +0x1452 / +0x1457 on a Spire entity at runtime.
- Live's requirement id for 1020 / 176 and their server answer to a non-tree cast: no captured CasterInfoReply for a tool
  exists (only 221, 1098, 326, 1122 in MiscHandler.cs:491-498). Try any other live capture with ability 0x27/3.
- Whether TargetIsProfile (type 22) is the target-side variant (`RequirementAutoInitializeParam<…,1>`, +0x88 = 1) — it
  must test the entity, not the player; check the factory registration for type 22.
- P0726 one-click toggle: whether live's InteractRequest on a tree (sent on every click) started a server-side harvest
  (PlayerUpdate StartHarvest 0x37 / CastingBar). Not needed for this bug.
