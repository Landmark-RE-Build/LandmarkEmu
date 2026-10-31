# Highlight task B: what the server sends for interaction info, and why the highlight stays one colour

2026-10-31. Read-only trace. No code was changed, nothing was built, and the game, Frida and Ghidra were not used.
Server repo at the deployed commit f6fb4340 (branch client-0.695).

Sources:
- re/decomp_all_695 (fn.py, pkt.py, grep);
- re/out/rtti_695.tsv;
- a capstone disassembly of `<0.695 install>\Landmark64.exe`, used for two call sites and to read the colour,
  timer and bounds constants;
- server-data/worlds/dev/console.log (this session: 185 replies, 6 flaggings);
- MODLOG.md:760, 1172-1176 (0.589 era);
- design/forum (patch notes).

**[P]** = proven (FUN_ address or file:line). **[I]** = inferred.

User report: "the highlighted colour only shows one colour; it won't change to yellow when you are close enough to
interact with it."

## 0. Answer in short

- **The reply bytes are correct for the reader [P].** Every field lands where the server means it. The colour rule only
  reads the type, the outline byte and the range, and the server sends type 3, outline 1 and range 6 for every usable
  entity. On its own, the hover outline should therefore turn gold within a 6 m box gap.
- **What the server never sends: an interaction-sphere radius (reply +0x20, we send −1) [P].**
  - The handler then never sets the entity's sphere radius (+0x411c).
  - The client's fallback to the zone default (10) applies **only to player characters** (FUN_142ea4230 tests +0x1452
    bit 2).
  - So every prop and NPC we flag keeps a **radius-0 proximity sphere**.
  - ProcessAutomaticInteractions looks for candidates with a radius-0 query at the player's position. Its sphere test
    is strict (`d² < (r1 + r2)²`), so **a radius-0 entity can never be a proximity candidate**.
  - Result: none of our interactables lights up as you walk near it. There is no "near" outline, no red-to-gold change
    as you approach, no "interact" hint and no working F key.
  - The only outline left is the one on the entity under the camera cursor. That one is red unless you are within the
    6 m box gap while aiming at it.
  - Live had the proximity highlight. The 10 Sep 2014 update notes say: "The prop will now highlight appropriately when
    you're near it." (design/forum/all/2014-09-09-47931-update-notes-9-10-14.md:88). The same thread has players
    complaining about the outline on interactive props showing all the time (:275).
- **Fix (server, 0 extra bytes):**
  - Send **SphereRadius > Range** for every usable entity: SphereRadius 10 m (the client's own zone default, live
    ClientGameSettings +0x14 = 10) and Range 6 m. Both are server settings, and the proximity outline is a toggle that
    defaults to on (live).
  - Send the reply **before** the DynamicInteractionFlagging, so the proximity trackable that the flagging creates
    already has the radius (§5, open question 2).
  - Keep −1 for edit-only (build-mode) entries.
- **Live values are not on disk [I].** No live InteractionInfoReply capture exists (Resources/PacketDumps holds 8
  files, none is 0x26/0x6A). The numbers above are the client's own defaults and our station range.
- If the user really sees red while aiming at a station from right next to it, that is not explained by the server
  bytes. See §6 for the client-side candidates (task A) and the in-game check.

## 1. Who sends what (server) [P]

There is **one** InteractionInfoReply writer: `Props.SendInteractionInfo`
(LandmarkEmulator.WorldServer/Game/Props.cs:2204-2237). It is reached from:
- `InteractionHandler.HandleInteractionInfoRequest` (Network/Handlers/InteractionHandler.cs:27-31), the client's request
  (PU 0x69);
- `Props.RefreshEditable` (Props.cs:2244-2252), on a build-mode toggle.

`usable` (Props.cs:2209-2214) covers:
- crafting stations (placed and Spire);
- chests, vendors, the Spire's usable props (Replicator, Repair Station, Mailbox, Barber, Portal Crystal, Resupply
  Chest: Spire.cs:994);
- mailboxes, parlors, vaults, readables;
- warp crystals, outposts, LootChests;
- story-tool switches and click-linked props;
- RewardChests;
- `Creatures.IsInteractable` (story creatures and world NPCs).

| case | Type (+8) | 4 × u32 (+0xc..+0x18) | +0x1c | SphereRadius (+0x20) | Range (+0x24) | Outline (+0x28) |
|---|---|---|---|---|---|---|
| usable | 3 | 10, 0, 10, 0 | 0 | **−1** | 6.0 (`StationRange`, Props.cs:2325) | 1 |
| edit-only in build mode | 3 | 0, 0, 0, 0 | 0 | −1 | 6.0 | 0 |
| anything else | 0 | 0, 0, 0, 0 | 0 | −1 | −1 | 0 |

Writer: InteractionMessages.cs:130-139. It writes the cursor pair twice; +0x1c is written as 0.

DynamicInteractionFlagging senders (all with `Interactable = true`):
- Props.cs:1567 (sign became readable) and :2150 (SendSpawns: usable, readable, mailbox 489, vault, or `MayRearrange`);
- Spire.cs:860;
- Creatures.cs:2302 and :3923;
- LootChests.cs:181, Outposts.cs:707, WarpNetwork.cs:467, StoryTools.cs:1982/1994, Npc/WorldNpcs.cs:144.

None of them sends a reply up front. Each one waits for the client's request.

## 2. The bytes in this session's console.log [P]

All 185 replies are single-entry and 45 bytes long (body after the opcode).

| guid | count | decoded |
|---|---|---|
| 0x53500001CC00000{2..7} (Spire props, zone 0x1CC) | 164 | type 3, cursors 10/0/10/0, +0x1c 0, sphere −1.0 (`00-00-80-BF`), range 6.0 (`00-00-C0-40`), outline 01 |
| 0x535000018700000{2,4..7} (Spire, zone 0x187) | 19 | same |
| 0x00000002000001D3 (a placed prop, flagged because the viewer may rearrange it) | 2 | type 0, cursors 0, sphere −1, range −1, outline 0 |

Requests come every ~5 s per entity, which matches the client timers in §3e. Flaggings seen:
- the 6 Spire props of each Spire;
- 81 warp/outpost guids 0x57500001870003E8.. (none was ever asked about in this log);
- the placed prop.

## 3. The client side the server feeds (0.695) [P]

### 3a. Reader and handler

Reader: entry FUN_142c202b0 (0x30-byte element). Handler: FUN_142f85860 (called from FUN_142d54c10 case 0x6a). The
disassembly at 142f8590c-142f85976 shows:

| entry | goes to | effect |
|---|---|---|
| +0x08 type | FUN_142ec2b00 → entity **+0x4a58** (4 = "pending" only overwrites 1) and +0x4a68 = now | getter FUN_142ea07e0 (asks again while it is 1) |
| +0x0c..+0x18 | FUN_142ec5740: when type is 2 or 3, sets +0x1457 bit 2 and copies the four ints to +0x2188 / +0x218c / +0x2190 / +0x2194; otherwise it writes −1 × 4 | **cursor ids**, not colours: FUN_1433d5620 shows +0x2188 in reach / +0x218c out of reach for type 3, and +0x2190 / +0x2194 for other types |
| +0x1c | entity +0x4a5c | the hint text id: FUN_1433d1c50 uses it for "<binding> … interact with …"; 0 = the default text |
| +0x20 | if ≥ 0: FUN_142ec2b40(entity, **xmm1 = +0x20**) → +0x411c, then rebuilds the sphere (+0x4120 centre, +0x4130 radius) | proximity sphere (§3c) |
| +0x24 | if ≥ 0: entity **+0x4118** | interaction range (§3b) |
| +0x28 | entity **+0x4a60** | outline allowed |

The model comments still name the 0.589 offsets: +0x4a28 for the type, +0x4a30 for the outline. In 0.695 they are
+0x4a58 and +0x4a60.

### 3b. The colour and the reach test

The update FUN_1433d2a40 (called from FUN_142cd6830) runs every frame:
- target = processor +0x28 if set;
- otherwise hover +0x18 when `(state(+0x18) == 2 || state(+0x20) != 2) && state(+0x18) != 0`;
- otherwise proximity +0x20;
- then FUN_1433d19b0(target, on = 1). That call recomputes the colour every frame.

FUN_1433d19b0 (disassembly 1433d1a4a-1433d1ab7):
- no outline when the type is 1 or 4, or when +0x4a60 == 0;
- **gold**: mode 1 = RGBA (2, 1, 0, 1) at 1447bf0b0, when local player (+0x282c8) != 0 **and FUN_142ea9b60(entity,
  player)** and type == 3. This branch also shows the "interact" hint (FUN_1433d5310);
- **red**: mode 2 = (2, 0, 0, 1) at 1447bf0a0 otherwise. Exception: a non-PC with +0xa20 bit 44 gets no outline at all
  when it is out of reach;
- outline slot 3 via FUN_142ec5ce0(entity, 3, processor+0x280 ProxiedObjectOutlineEffect);
- the other modes: 3 = (0.5, 2, 0.5), 4 = (0, 2, 0), 5 = (2, 2, 0) (true yellow). No interaction path uses them.

FUN_142ea9b60(entity, player) is the reach test:
- it takes the box gap between [pos + actor bounds] of the two (actor bounds: Actor vfunc +0x120 = FUN_143d01e50,
  default [−1, 1]³ = local space);
- the test is gap² ≤ max(entity +0x4118, player +0x4118)²;
- when both are 0 it uses the zone default int game +0x326d0 (ClientGameSettings +0x14, live 10).

The same test drives two other things:
- the hover cursor (FUN_1433d5620);
- F: FUN_1433d2940 returns 2 only when it passes, and FUN_1433d2890 / FUN_1433d5520 send InteractRequest only on 2.

Entity +0x4118 sources:
- the spawn base field **+0x240** (FUN_142d54040 part_0067.c:6875, raw; for props FUN_142cde1c0 part_0065.c:16398-16403
  uses ≤ 0 → zone default 10);
- the reply. There are no other writers besides the constructors (0).
- The server writes that spawn field as "draw distance (0 = default)" (PlayerUpdateAddLightweightProp.cs:141,
  PlayerUpdateAddLightweightNpc.cs:73). It is really the entity's interaction range f32 (pkt.py FUN_142c26790: u32 +0x240
  right after the 3 × f64 origin at +0x228).

### 3c. Proximity candidates (why nothing lights up as you walk near)

- The DynamicInteractionFlagging handler FUN_142f85770 does two things:
  - it sets entity +0x1461 bit 0x40;
  - it registers the entity once in the proximity octree game +0x32030, as a PlayerSphereTrackable
    (FUN_142e93f50 → FUN_14359a3b0; FUN_142ec7ce0 is true for flagged entities).
- The trackable's sphere comes from vfunc +8 FUN_14359a510 → **FUN_142ea4230**. That returns the entity's sphere, whose
  radius is +0x411c, **with the zone default only `if (radius == 0 && (entity +0x1452 & 2))`**.
  - +0x1452 bit 2 is the constructor flag (FUN_142e88840 param_6) that marks player characters [I name].
  - The same code exists in 0.589 (FUN_142e05dc0).
- The octree insert FUN_143d831c0 copies that sphere.
- ProcessAutomaticInteractions FUN_1433d4070 queries with **{player position, radius 0}** (`local_388 = 0`, read as
  param_2[4] by FUN_1433ce560).
- FUN_1433ce820 keeps an entry only if `d² < (r_entry + r_query)²`. With both radii 0 this is never true.
- So **props and NPCs flagged by us never become candidates**. Processor +0x20 stays empty, and only the entity under
  the camera cursor (+0x18, FUN_1433d5620 from FUN_142d0f610) gets an outline.
- With candidates present, FUN_1433d4070 makes +0x20 the best-ranked flagged entity in reach with type 3. Failing that,
  it takes the best flagged one out of reach. So as you approach, the outline goes red → gold, and F interacts without
  aiming.

### 3d. The 0.589 history

- MODLOG 1173/1174 (0.589, 10-11) says gold was "verified on the Spire Mailbox" and that the outline was "fed by
  ProcessAutomaticInteractions".
- The second point does not fit the radius-0 sphere above. With a camera-locked cursor, the hover target is whatever the
  crosshair is on, which looks like proximity [I].
- The 10-11 request "outline only on hover" (commit 0cf02d20) was made before the 10-18 rule "live default + toggle".

### 3e. Refresh

FUN_142eb1950 refreshes the interaction info using these constants (from the exe):

| constant | address | effect |
|---|---|---|
| after **5000 ms** (0x1388) | 145208b58 | asks again while the player is within **50 m** (145208b54) |
| after **10000 ms** (0x2710) | 145208b5c | type falls back to 1 |

A refresh (FUN_142f88e70 → type 4) does not overwrite type 3, so the outline does not flicker.

## 4. Field-by-field verdict

| field | server | client wants | verdict |
|---|---|---|---|
| type | 3 / 0 | 3 = interactable (gold allowed), 0 = none | OK |
| cursors | 10 in reach, 0 out | ids from Cursors.txt (10 cursor_interaction_generic; 0 arrow) | OK (pair 2 only matters for type 2) |
| +0x1c | 0 | the hint text id; 0 → default "interact with" | OK; live verbs unknown |
| **+0x20 SphereRadius** | **−1** | ≥ 0 sets the proximity sphere; props have no fallback | **missing: no proximity highlight / hint / F** |
| +0x24 Range | 6 | reach for gold, cursor and F | OK; the gold zone is 6 m of box gap (the spawn default would be 10) |
| +0x28 Outline | 1 usable / 0 | must be 1 for any outline | OK |
| spawn +0x240 | 0 ("draw distance") | interaction range f32; props → 10, NPCs → 0 | harmless, misnamed |

## 5. Proposed change per entity kind (not edited)

All of these go through `Props.SendInteractionInfo` (Props.cs:2219-2233). The cost is 0 bytes, because the field is
already sent.

1. **Every `usable` kind:**
   - **kinds**: placed and Spire crafting stations, chests, vaults, vendors, mailboxes (placed 489 and Spire), the
     parlor / barber, readable signs, warp and portal crystals, outpost crystal and chest, LootChests, RewardChests,
     story switches / click-linked props, interactable story creatures and world NPCs (NPCs are non-PC
     ProxiedCharacters, so they have no sphere fallback either);
   - set `SphereRadius = InteractionSphere`;
   - set **InteractionSphere = 10 m**. That is the zone default the client gives PCs (FUN_142ea4230 ← ClientGameSettings
     +0x14, live 10) [I value];
   - keep **Range = 6 m** (`StationRange`).
   - Expected:
     - inside 10 m of the prop's centre, the nearest one is outlined **red**;
     - within a 6 m box gap it turns **gold (2, 1, 0)** and the interact hint appears;
     - F sends InteractRequest.
   - The sphere must stay larger than the range plus the prop's half-size, or the outline would be gold from the
     moment it appears. That would again be a single colour.
2. **Edit-only (build-mode) entries:** keep SphereRadius −1 and Outline 0. With a sphere, F and ProcessAutomaticInteractions
   would pick fences and decorations (they are type 3 and in reach) and send InteractRequest for them.
3. **Type-0 entries:** unchanged.
4. **Order:** send the reply for usable props **right before** their DynamicInteractionFlagging (Spire.Enter Spire.cs:860-863,
   Props.SendSpawns :2150-2177, Outposts :707, LootChests :181, WarpNetwork :467, WorldNpcs :144, Creatures :2302-2304 / :3923,
   StoryTools :1982).
   - FUN_142f85860 does not check the flag, so it applies to an unflagged entity.
   - The trackable created by the flagging then already has the radius.
   - This is only needed if the octree does not re-read the sphere of a registered trackable (open question 2).
   - Cost: one ~45 B entry per prop, once per spawn.
5. **Settings:**
   - `INTERACTION_PROXIMITY_HIGHLIGHT` (default on = live; off = sphere −1, the hover-only outline the user asked for on
     10-11);
   - `INTERACTION_SPHERE` = 10;
   - `INTERACTION_RANGE` = 6.
6. **Docs and comments:**
   - docs/protocol.md:366 "4 × u32 colour" → cursor ids;
   - "+0x1c u32" → hint text id;
   - InteractionMessages.cs SphereRadius: the fallback is PC-only;
   - type / outline offsets are +0x4a58 / +0x4a60 in 0.695;
   - the spawn "draw distance" at base +0x240 → interaction range (entity +0x4118).

## 6. If red persists while aiming at a station from close by (for task A)

The server bytes do not explain that case. Client-side candidates [P mechanism]:
- FUN_142ec5ce0 shows only the **lowest set outline slot**. A fixed colour on a lower slot hides slot 3. The slots are:
  - 0: target lock FUN_14332b710 and PC hover FUN_143201830;
  - 1: tool states, ActionStateLogging / DualTool slot 39 (mode 1 fixed);
  - 2: FUN_143650cf0;
  - 3: interaction;
  - 4: PropProcessor selection.
- FUN_1433d3760 also skips slot 3 for the prop that the PropProcessor (FUN_142cd1c30, +0x58) holds.

To check in game (not now): read entity +0x4118 / +0x4a58 / +0x4a60, the result of FUN_142ea9b60 and effect +0x110
while standing at the Replicator. Then repeat after change 1.
