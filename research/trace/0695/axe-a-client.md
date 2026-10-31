# Axe task A: what the 0.695 client lets an axe target, and why it swings at the Spire

2026-10-31. Read-only trace. No code was changed and nothing was built. The game, Frida and Ghidra were not used.
Sources:
- re/decomp_all_695 (fn.py, pkt.py, grep);
- re/out/rtti_695.tsv;
- a capstone disassembly of `<0.695 install>\Landmark64.exe` (scratch script; it was used only to read the
  third argument of FUN_142ec5ce0);
- gameData695 (RequirementExpressions, Requirements, ProfileDefinitions, Cursors, items.json);
- Resources/PacketDumps/41-ReferenceDataProfileDefinitions (live);
- server-data/worlds/dev/console.log.

**[P]** = proven (a FUN_ address plus what it does, or file:line). **[I]** = inferred.

Existing research used and not repeated here:
- trace/0695/healthbar-b-harvest-target.md: slot 9, SetCurrentTarget FUN_142d4e890, CheckTargets.
- healthbar-e-server.md §3: AddLightweightProp +0x1a0 is the profile, read by FUN_142d54040.
- MODLOG.md:762 (0.589): "ActionStateLogging's target hook returns the picked entity".
- MODLOG.md:1123: "the client lets any tool target any node".
- OPEN-LEADS L241 (tree size) and L244 (who put trees in slot 9).

## 0. Answer in short

- **Root cause [P].** For an axe, the client decides what the axe may outline and swing at with one value: the
  ability's **target requirement**. That value is the RequirementExpressions id in **CasterInfoReply +0x90**. Our
  server writes 0 there (`AbilityCasterInfoReply.U90`, never set in `Abilities.BuildItemAbilityInfo`). The client's
  requirement check returns **true for any id < 1** (FUN_1432f5a00).
  - So every prop under the cursor counts as a valid axe target: the Spire, stations, placed props and trees.
  - Each one gets the axe cursor (Cursors.txt 1 `cursor_interaction_axe.cur`) and the tool outline, and a click
    swings at it.
  - A click also swings at any non-hostile NPC.
- **Live's way [P mechanism, I values].** Send:
  - the axe abilities' target requirement **20** ("Target is Harvestable Tree" = `TargetIsProfile200 || … ||
    TargetIsProfile214`);
  - for each tree, a **profile id 200-214** ("Tree: Very Small … Very Large: Plain / Burled / Striped Wood") in
    AddLightweightProp +0x1a0. Today this is 0 for every prop.

  The client then shows the axe cursor and outline, and swings, only on trees. Everything else keeps profile 0, fails
  requirement 20 and gets the plain arrow and no swing. **Both changes must ship together.** With 20 and profile 0,
  the axe cannot target trees at all.
- **Interact (F, click) is not a tool matter [P].**
  - The tool states do not block interactions. Only the Combat state does (action-state slot 3).
  - The F key and the interaction cursor work from the server's DynamicInteractionFlagging and InteractionInfoReply
    alone.
  - Any completed left click on any entity sends SelectPlayer, RequestSetTarget and (when client +0x32709 is set)
    InteractRequest, with or without a tool. That is the "interacted with 0x53500001CC000001, which has no
    interaction" line. It also happens bare-handed, and live sent the same.
- **Neutral disposition and slot 9 do not change the axe or F on other entities [P].**
  - The axe checks read the requirement, the "attackable" test (disposition **0** only) and the falling-tree table.
    They never read disposition 1 or 2, or target slots.
  - F reads only the interaction flags and type.
  - Disposition 1 is sent only for nodes (Gathering.cs:919).
  - Never send trees as disposition 0: the swing check refuses "attackable" entities.

## 1. The axe's action state: ActionStateLogging (vtable 1447de218)

Tool mode [P]:
- The tool mode comes from the main-hand item's ability types (FUN_143457da0 → FUN_143455020 on CasterInfo
  +0x8c):
  - **type 3 only → Mining (1)**;
  - **type 2 → Logging (2)**;
  - **type 1 → Reaping (3)**;
  - **types 3 + 2 → DualTool (4)**.
- FUN_143b7a400 / 3e0 / 4c0 read the item's ability of type 3 / 2 / 1.
- The live item dump (gameData695/items.json) [P data]:
  - axes 3259-3263: **1020 type 2**;
  - crafted axes 1925-1928: **176 type 2**;
  - sickles: 153-157 type 1;
  - picks: type 3;
  - **Settler's / Explorer's / Trailblazer's Pickaxe (2568-2570): 169 type 3 + 173 type 2** → DualTool;
  - NovaTech 3702-3705: 1036 type 3 + 1037 type 2 → DualTool.

Parameters, set by FUN_143458f20 (ClientActionManager vtable +0x50) for the held item [P]:
- SetParam 2/3/4 = the type 3 / 2 / 1 ability ids.
- Then, from the type-2 ability (and again from the type-1 ability, if the item has one):
  - SetParam **8** = FUN_142e17320(abilityMgr, id) = ability record **+0x70**;
  - float 9 = record +0x18 + stat 0xF (max range);
  - float 10 = record +0x1c (min range);
  - float 11 = arc.
- ActionStateLogging SetParam FUN_143734370 stores: **3 → +0x30 (ability), 8 → +0xb0 (target requirement)**.
- The float setter FUN_143734220 stores: 9 → +0xb4 (max range), 10 → +0xb8 (min range).

Record +0x70 is CasterInfoReply **+0x90** [P]:
- The ability store FUN_142e187a0 copies `record[0x1c] = reply +0x90`.
- The 0.695 reply reader FUN_142e016b0 (pkt.py) reads +0x90 right after the arc (+0x6c). That is our `U90`
  (AbilityMessages.cs:108).
- The user's session shows 176's reply with U90 = 0: console.log:4499, the u32 after `00-00-C0-40` max range 6.0,
  u68 1, two empty strings and arc 0 [P].

The id is a **RequirementExpressions** id [P]:
- FUN_1432f5a00 (ClientRequirementManager vtable +0x10, via `DAT_145431a00`) looks it up in the table at +0x70c0.
- FUN_1432ef380 fills that table from RequirementExpressions.txt (vfunc +0x18 = FUN_1432cd6c0 on the +8
  sub-object → +0x70b8 + 8).
- **`if ((int)id < 1) return 1;`**
- The context is {self = local player, target = entity}.

### 1a. Cursor, outline and reticle: FUN_1437327b0 (slot 39), mouse events 0x200 / 0x1000, mode DAT_1452a9558 == 2 [P]

1. The entity under the cursor is taken only if it is a **ProxiedProp**: entity vfunc +0x108 returns `this` for
   ProxiedProp (FUN_1430a8d90) and 0 for ProxiedCharacter / ProxiedObject (FUN_142e9dd30).
2. It is valid when `Requirement(+0xb0, local player, prop) || FUN_142ea9e60(prop)`.
   - FUN_142ea9e60 is "attackable": !(+0x1459 & 0x20) && ((+0x1452 & 2) || (+0x1457 & 2) || (disposition
     +0x147c == 0 && !(+0x1460 & 4))).
3. Valid:
   - **outline**: FUN_142ec5ce0(prop, slot 1, state+0xa0). The third argument is from the disassembly at
     14373298d–14373299e. It is applied only when no client claim contains the prop: ClaimsManager octree count
     FUN_142dbc050 < 1 (client +0x314f0 +0x96fc8, ctor FUN_142dab830).
   - +0xa8 = the prop's guid.
   - **DAT_1452a955c = 1** inside [min, max] range, 2 outside.
4. Invalid: outline cleared (third argument 0), +0xa8 cleared, DAT_1452a955c = 0.
5. The cursor manager FUN_14306c660 uses the hovered entity's interaction cursor (InteractionInfoReply) first.
   Otherwise it uses the action state's value (FUN_1434556c0 → slot 17 FUN_1434556b0 returns DAT_1452a955c).
   - Cursors.txt: **1 = cursor_interaction_axe.cur**, 2 = arrow.cur, 0 = arrow.cur.
   - So the "axe can act here" cursor and outline appear on **every prop** while U90 = 0.

### 1b. The swing: FUN_143730e50(state, ability, entity, point, 0) [P]

It is reached two ways:
- **Select** (Generic "Select" = Mouse_0, InputProfile_Default.xml:54) in slot 12 FUN_1437334f0 →
  FUN_143735030. That function finds the entity by guid in the ProxiedCharacter table (props **and** NPCs,
  FUN_142f85390).
- The held-button repeat in slot 7 → FUN_143735470, on the same entity (+0x48), while the player stays put.

A swing needs all of these:
1. ClientActionManager +0x38 = **FUN_14345b450**: `Requirement(record(ability)+0x70, local player, entity)`, the
   same id as above.
2. **!FUN_142ea9e60(entity)**: an attackable (hostile) entity is refused.
3. Entity vfunc +0x410:
   - ProxiedProp FUN_1430aaa40: not in the PropDestruction table (client +0x320a8, `HashListMap<guid,
     PropDestructionEntity>`, ctor FUN_1431684f0), i.e. not already falling;
   - ProxiedCharacter FUN_142eaa8a0: always 1.
4. The distance to the point is within [+0xb8, +0xb4].

Then the state sets +0x80 = the entity guid and casts through ClientActionManager +0x80. The guid is the one the
cast tail carries at +36 (Abilities.TargetGuid).

**With U90 = 0, steps 1-4 pass for the Spire (disposition 2, not falling, in reach). The client swings and sends
CastAttempt 176.**
- console.log:118018-118473 (07:31:30) and 121871-122386: "swung at 53500001CC000001 … with no tree in reach".
- 07:14 / 07:19 (log 110826-113418): cast **173**, the Pickaxe's chop, at the same Spire prop [P].

### 1c. DualTool (Pickaxe 169 + 173, NovaTech 1036 + 1037): vtable 1447de560, mode DAT_1452a9558 == 5 [P]

- FUN_1437324d0 runs the same test with the Logging sub-state's +0xb0 (state +0x40).
- Slot 26 FUN_143734570 returns true (keep the pick ring) only when the prop is **not** a chop target: not attackable
  and the requirement fails.
- With U90 = 0 every prop under the cursor turns the dual tool into an axe and switches the ring off.
- SetParam FUN_143734320 forwards to both sub-states.

### 1d. Reaping (sickle)

- ActionStateReaping stores SetParam 4 → +0x30 and **8 → +0xd0** (FUN_1437343b0) [P].
- No reader of +0xd0 was found among its own vtable functions. It has no single-target outline (MODLOG.md:762) [open].
- Plants are off on our server (`PlantsHarvestable` const false).

## 2. The data the requirement uses

RequirementExpressions (0.695 and identical in 0.589) [P data]:

| id | expression | profiles | exist in 0.695 ProfileDefinitions? |
|---|---|---|---|
| 8, 19 | TargetIsTreeLarge … TargetIsTreeLargeOldGrowth | 56, 57, 58, 87, 100-105 | 87 and 100-105 missing; 56-58 are "COMBAT" (name 50084, player models) → **stale** |
| 14 / 15 / 16 | "Tier 4 / Tier 2 tree or less", "Basic tree" | subsets of the above | stale (pre-launch tiered axes) [I] |
| **20** | "Target is Harvestable Tree" | **200-214** | yes: "Tree: Very Small: Plain Wood" … "Tree: Very Large: Striped Wood" (text 562065-562079) |
| 22 | "Target is Plant Profile" | 90, 91, 296 | yes: "Plant - One / Two / Three Hit" |
| 34 | "Harvestable Tree or Plant" | 90, 91, 296, 200-214 | yes |

- Profile **39 = "Prop"** (Requirements 48 TargetNotPropProfile = profile ≠ 39).
- The live ReferenceDataProfileDefinitions capture that our server sends at login (WorldSession.cs:736) contains rows
  200-214, 90 and 39. For example {200, 562065} is at offset 3888 and {214, 562079} at 5316 [P]. So live's server
  knew these profiles.

Profile order [P data]: id = **200 + 3 × size + wood**, where:
- size 0 = Very Small, 1 = Small, 2 = Medium, 3 = Large, 4 = Very Large. This is the same order as
  Gathering.Health.cs TreeLife / TreeSize.
- wood 0 = Plain (item 71), 1 = Burled (72), 2 = Striped (73).

TargetIsProfile [P]:
- Registered as `RequirementAutoInitializeParam<ClientRequirementIsProfile,1>` (oaat 0xd42f996c, FUN_14329b830),
  i.e. it reads the **target**.
- Evaluate FUN_143710dd0: `target +0x1474 == PARAM_1` (operator 1) or `!=` (operator 2).

Where +0x1474 is written [P] (FUN_142ec55e0 callers):
- **FUN_142d54040**: lightweight base, `packet +0x1a0`, for AddLightweightProp FUN_142cde1c0, AddLightweightNpc
  FUN_142cddaa0 and FUN_142cde8b0;
- **AddFeatherweightObject FUN_142cdd710** (`+0x70`), which also hard-codes **disposition 1** and collidable;
- FUN_142cdfa60 (other spawns), FUN_142cb1f60 and FUN_142cd9980.

LightweightToFull (FUN_142d532f0) writes disposition (+0x90 → +0x147c) but **not** the profile. So the profile must
be in the lightweight spawn, which every node (re)spawn already sends (`node.Spawn`).

Our writer: `PlayerUpdateAddLightweightProp.cs:126 writer.Write(0u);` (just after ControlMode) = +0x1a0, always 0 [P].

Other readers of +0x1474 [I, low risk]:
- the profile-row lookups FUN_142e9e610 / FUN_142e9eab0 / FUN_142ea0b80 / FUN_142ea2bd0 / FUN_142ea21d0 /
  FUN_142e9e8b0, used by the local player's movement controller (FUN_143008ee0, FUN_143010260, FUN_14300dda0) and
  footsteps (FUN_14347d910);
- the HUD indicator FUN_143449e00;
- requirement 13 (plant branch 90/91/296 only) and 21 (props fail NotProp anyway).

No HUDIndicatorSettings row tests 200-214, so the tree bar rules (healthbar-b) do not change [P data].
FUN_142ec55e0 also sets +0x1453 bit 0x40 when the profile equals a config value (hash 0xF4595910). That config's
name and default are unknown; presumably 0 = off [I].

## 3. Interaction while a tool is held [P]

| Path | Code | Depends on |
|---|---|---|
| Blocks interactions? | ActionState slot 3 (+0x18) through FUN_143457850: **0** for Default, Logging, Mining, Reaping, DualTool and Harvesting (FUN_143457760); **1 only for Combat** (FUN_143731b30) | the action state |
| Left-click select | FUN_142d0f610 event 0x40 → FUN_142cbed70 (only if not in Combat): CommandPacketSelectPlayer 0x1E/0x16 → FUN_142ca6f00 (SetCurrentTarget → TargetBase RequestSetTarget) → if client +0x32709: FUN_142d4ac90 → **CommandPacketInteractRequest 0x1E/0x09** (at most 1 per second) | any entity; no interaction flag needed |
| Interact key F (Generic "Interact") | InteractionProcessor (vtable 144765898, +8 slot 0 FUN_1433d39c0) → FUN_1433d5520: InteractRequest only when FUN_1433d2890 == 2 | the entity flagged (+0x1461 bit 0x40 / +0xa20 bit 40, from DynamicInteractionFlagging) and interaction type 3 (FUN_142ea07e0 = +0x4a28, from InteractionInfoReply) in reach (FUN_1433d2940) |
| Proximity auto-interact | FUN_1433d4070 "BaseClient::ProcessAutomaticInteractions" | the same flags; cleared while the state blocks (Combat) |
| Hover cursor | FUN_14306c660: the interaction cursor (CursorInReach) first, otherwise the tool state's cursor | InteractionInfoReply |

So with an axe:
- stations, chests and the Spire's usable props still show the interaction cursor and open on F or a click. That is
  correct and live-like.
- "Interacted with … which has no interaction" for the Spire base (…CC000001) is the generic click. The server
  answers nothing, which is correct.
- The only tool-specific bug is §1: the axe cursor, outline and swing on non-trees.

## 4. Effect of the 2026-10-31 changes (neutral nodes, slot 9) [P]

- **On other entities: none.** Disposition 1 is written only in Gathering.SendFull (Gathering.cs:919). Every other
  prop keeps the default 2. Slot 9 is pushed or confirmed only for nodes (Gathering.HarvestTarget,
  InteractionHandler).
- **On trees:**
  - SetCurrentTarget picks slot 9 instead of 8 (healthbar-b).
  - Trees leave the friendly tab-cycle list (FUN_1431e5de0 lists disposition 2).
  - FUN_142ea9e60 stays false (1 ≠ 0), so the swing check (§1b-2) still passes.
  - Nothing in ActionStateLogging, FUN_143730e50, the cursor manager or the InteractionProcessor reads target slots
    (ClientTargetManager client +0x32b20) or disposition 1 / 2.
- **Do not use disposition 0 for trees.** Hostile means "attackable": the outline would show (§1a-2), but
  FUN_143730e50 refuses the swing, and SetCurrentTarget would use slot 3.

## 5. What the server should send (as live) — change points, not edited

1. **CasterInfoReply +0x90 (`U90`) = 20** in `Abilities.BuildItemAbilityInfo` for the axe abilities (1020, 176) and
   for the dual tools' type-2 abilities (**173**, **1037**). [P mechanism; I that live used 20: it is the only
   tree expression whose profiles exist in 0.589 / 0.695 / live ReferenceData. No live tool CasterInfo is on disk;
   re/caster1018.hex is our own server's output: MaxRange 30, animation 245.]
   - Sickles (153-157, type 1) would take **22**: stored by Reaping +0xd0, effect untraced.
   - 34 is the tree-or-plant variant, for a tool that should do both.
2. **AddLightweightProp +0x1a0 = profile** for every tree node: `200 + 3 × TreeSize(model) + wood(Yield 71/72/73)`,
   set once on `node.Spawn` (Gathering Generate / AddSetNodes / Stand) so Stream, RegrowDue and respawn all carry it.
   - Plants, if they are ever enabled: 90 / 91 / 296.
   - Every other prop stays 0, so requirement 20 fails for it: no axe cursor, no outline, no swing.
   - Cost: 0 bytes (the field is already sent).
3. Keep disposition 1 and the slot-9 handling as they are. AddFeatherweightObject, the client's own node-like spawn,
   also hard-codes disposition 1.
4. Server-side guard [I, ours]:
   - Answer a logging cast whose guid is not a standing tree node with **Refuse (CastFail)** instead of
     `Answer(swung: true)`. Today the swinger sees the swing animation (CastSuccess AnimationId 254) at whatever
     they clicked.
   - Gathering.Target's "nearest tree within 3 m of the aim point" fallback for a non-node guid can chop a tree
     next to the Spire. Keep it only for casts that carry no guid.
5. Side finding [P]: **173 is in no ability set** (Abilities.cs:876-888 list 169 but not 173).
   - So the Pickaxe's chop (DualTool logging) gets the non-harvest CasterInfo: MaxRange 30, CastTime 0.75.
   - It also does nothing on the server: the casts at 07:14 were answered without a chop.
   - It belongs in LoggingAbilities (or a dual-tool set) with the tree requirement.

Expected client result after 1 + 2 [P chain, to verify in game]:
- Hovering the Spire with an axe: arrow cursor, no outline, a click sends no CastAttempt (SelectPlayer /
  RequestSetTarget / InteractRequest still go).
- Hovering a tree: `cursor_interaction_axe.cur` and the outline in reach; a click swings and the cast tail carries the
  node guid.
- Trees inside a client claim get no outline but can still be swung at (FUN_142dbc050 gates only the outline).
