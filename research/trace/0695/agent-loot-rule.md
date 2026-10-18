# L141: when did live make a loot pinata ownerless? Client deep-dive (2026-10-17)

Read-only research. Sources: the 0.695 decompile (`re/decomp_all_695`), `<0.695 test copy>\Landmark64.exe` read as bytes
(capstone disassembly of the LootPinata class range 0x14311e000-0x143128000), the 0.589 decompile (`re/decomp_all`),
gameData695 tables, `re/out/text_695.tsv`, the RTTI / Lua bind lists, the AS3 export, the patch notes, the forum dump and
the wiki dump. It builds on agent-e-leads.md §2 and agent-props2.md §1.

Marks: **[P]** proven (function, table row or file:line), **[I]** inferred (the reason is given).
Function addresses are 0.695 unless marked 0.589.

---

## 0. Answer in short

- **The client cannot drop ownership in place** [P]. The only ownership change it knows is *ownerless → claimed*
  (NotifyOwnerlessLootClaimed 0xFB/4). No packet, timer or state turns an owned pinata into an ownerless one. Every new
  SpawnLoot is launched again from its spawn point. So "ownerless" is a property a pinata has **from the moment it
  spawns**. It is not something that happens to a bag lying on the ground (§3, §4).
- **Ownerless pinatas were built to be seen by several players at the same drop** [P]. For an ownerless pinata the
  client takes the drop arc (launch angle, heading and launch speed) from three server-sent floats in SpawnLoot. For an
  owned pinata it rolls them locally (§1, fields +0x64/+0x68/+0x6c). An ownerless pinata also may not settle on contact
  during its first 11 s (§3). Both rules only matter if every viewer must see the bag land in the same place.
- **There is no group-loot, loot-rule, need/greed, round-robin or kill-tag code in the 0.695 client** [P by search, §5].
  So "ownerless" is not a group-loot mode.
- **The trigger itself is server-side and stated nowhere** [P that it is absent from the client and the sources
  searched]. The strongest-supported rule, with confidence **medium-low** [I], is:
  *loot is spawned ownerless when the server drops it but no player is entitled to it*. The one such drop still alive in
  the 0.695 era is the Fiery Fungus explosion's "destroyed terrain loot" (P4533). Three 2016 patch notes removed the
  other entitlement-less drops (P4349, P4719, P5768). Nothing ever turns an owned bag ownerless.

---

## 1. SpawnLoot (0xFB sub 1): every field and what the client does with it

Reader: FUN_143121ec0 (0.589 FUN_1430775a0, same offsets [P]). Handler: LootPinataManager::HandlePacket FUN_143125240
→ CreateLootPinata FUN_143124c30 → LootPinata ctor FUN_143122a50 (struct copy FUN_143123760 to pinata +0x58).
Offsets are the packet object's. The wire order equals the offset order after `u16 0xFB, i8 1`.

| Packet off | Wire type | Pinata field | What the client does with it |
|---|---|---|---|
| +0x18 | u32 loot id | +0x1C0 (and hash key +0x218) | identity for every sub (2/3/4/5/7) and the HUD indicator key (FUN_14343c620) [P] |
| +0x1C | u32 definition | definition +0xE8 | LootPinata.txt row (model, scale, launch/tumble ranges, animation, FLAG_AUTO_SCOOPABLE); 0 → the manager's default (+0x128), ctor fallback 6 [P] |
| +0x20/24/28 | vec3 position | +0xC0.. | spawn point; launch starts here [P] |
| +0x2C | u32 item | +0x1C4 | the item's own loot model (item def +0x40) if it has one; bag / resource-room check FUN_143124180 (containers 0 and 5) with the count [P] |
| +0x30 | u32 | +0x1C8 | key into the loot-particle definitions (FUN_1431208e0, falls back to key 0) → creation / moving / landing / idle / death composite effects per state [P]; that the key is LootParticles QUALITY_ID is [I] |
| +0x34 | u32 count | +0x1CC | bag / resource-room check [P] |
| +0x38 | f32 | +0x1D0 | **no reader** in the LootPinata code (only the ctor write at 0x143122cef in the disassembled class range) [P] |
| +0x40 | u64 owner | +0x200 and copy +0x70 | visibility filter (shown only if owner = me or none), claim logic, scoop target, drop-arc seeding (§3) [P] |
| +0x48 | string | (ctor only) | model name fallback when the item has no loot model [P] |
| +0x60 | u32 kind | not stored | ==3 → action manager FUN_143455b70(position) (harvest) [P] |
| **+0x64** | **f32 (raw)** | **+0x1F4** | **only if owner == none**: launch-angle fraction, lerp(LAUNCH_ANGLE_MIN, MAX) [P] |
| **+0x68** | **f32 (raw)** | **+0x1F8** | **only if owner == none**: heading fraction (× a constant → yaw of the throw) [P] |
| **+0x6C** | **f32 (raw)** | **+0x1FC** | **only if owner == none**: launch-speed fraction, lerp(LAUNCH_VELOCITY_MIN, MAX) [P] |

Proof for the last three rows: the ctor tail (0x143123311-0x14312341a). `cmp [rdi+0x200], none; jne` → owned: an LCG
`imul 0x7ff8a3ed; add 0x2aa01d31; cvtsi2ss; mulss` (a local random number in [0,1)). Ownerless:
`movss xmm6, [r13+0x50/0x54/0x58]` (the copied packet floats, struct [0x14..0x16] = packet +0x64/+0x68/+0x6c). The
values go to +0x1F4/+0x1F8/+0x1FC [P]. They are used by the launch FUN_1431257a0: definition +0x34/+0x38 (angle) with
+0x1F4, +0x1F8 × constants (rotation), definition +0x2C/+0x30 (velocity) with +0x1FC [P]. The definition offsets match
the LootPinata.txt columns: +0x28 scale (used as scale in FUN_1431270a0), +0x44 animation id (FUN_143126460), +0x48
auto-scoopable (ctor → +0x1D4) [P]. 0.589 is the same (part_0085.c:31199-31220) [P].

**No field is an ownership timer, a group id or a "free for all" flag** [P]. The only fields our server writes as 0 that
the client treats specially are the three arc seeds, and only for ownerless pinatas. Today `SpawnOwnerless` sends 0, 0, 0,
so every ownerless bag flies at the minimum angle (65°), heading 0 and minimum speed (10). The arc is identical on all
viewers but always points the same way. The other zero fields (+0x30 particle key 0, +0x60 kind 0) are plain defaults.

## 2. Every LootPinataBase sub in the 0.695 client

| Sub | Name | Dir | Client code | Effect |
|---|---|---|---|---|
| 1 | SpawnLoot | S→C | FUN_143121ec0 / FUN_143125240 | creates the pinata only if owner = local guid or none (DAT_145453608, no writer → 0) [P] |
| 2 | RemoveLoot | S→C | FUN_143121e00 → FUN_143126f70 | sets the destroy flag +0x1E4; returns 0 if the id is unknown (harmless) [P] |
| 3 | PickupLootResponse | S→C | FUN_143121d20 → FUN_143126f70 | same as 2; the bool is not read [P] |
| 4 | NotifyOwnerlessLootClaimed | S→C | FUN_143121c60 | owner → +0x200 / +0x70, state 3 (fly to the new owner) [P] |
| 5 | PickupLoot | C→S | FUN_143127a10 (state 3 arrival, owner = me) | {u32 id, u64 me} [P] |
| 6 | RequestByItem | — | registry name only (part_0107) | no class, no builder, no handler case (HandlePacket logs "Unable to handle" for anything but 1-4) [P] |
| 7 | RequestClaimOwnerlessLoot | C→S | FUN_143127a10 | {u32 id, u64 none}, every tick while owner = none, distance < 5 m and state ≠ 3 [P] |

Creation: CreateLootPinata FUN_143124c30 has exactly one caller, HandlePacket (call @0x1431256d8) [P]. **The client never
creates or re-owns a pinata on its own.**

## 3. The pinata state machine (FUN_143126d90 and neighbours)

Per frame: FUN_1431270a0 (vtable slot) → FUN_143127a10 (ownership / pickup). The manager sweep FUN_1431251a0 deletes
pinatas whose +0x1E4 is set. Time = FUN_142b4f800 = `_time64` **seconds**. Spawn time is stamped in the ctor (+0x208)
[P].

| State | Meaning | Entered by | Effect played (particle def) |
|---|---|---|---|
| 0 | spawned, waiting | ctor | creation (on leaving) |
| 1 | launched (physics box "LootPinataBox") | FUN_1431270a0 once the terrain at the spot is ready; the launch FUN_1431257a0 runs only on 0→1 | moving |
| 2 | resting (idle anim); for ownerless also "claim sent" | voxel under it solid (FUN_1431270a0); physics contact FUN_1431261f0; **MaxDroppingTime 7 s** after spawn (FUN_143124200, hash 0x97e89377); or the ownerless claim branch | landing → idle |
| 3 | scooping to the owner entity | FUN_143124200 (owner = me, in range, room); or sub 4 | — |
| 4 | decaying | owner set, ≠ me, owner entity unknown to this client (FUN_142f85390 fails) + destroy | death |
| 5 | collected | arrival at the owner; PickupLoot only if owner = me, otherwise only the destroy flag | — |

Ownership-specific rules [P]:
- **Ownerless contact rule** (FUN_1431261f0, 0.589 part_0085.c:33450): a state-1 pinata settles on contact only if
  `owner ≠ none || age ≥ 11 s`. An ownerless pinata keeps bouncing and rolling in its first 11 s. In practice it reaches
  state 2 at about 8 s anyway through MaxDroppingTime 7, or at once when claimed.
- **Claim** (FUN_143127a10): owner = none, local player not knocked out (player +0x250 vfunc +0xB0 = IsKnockedOut,
  abilities-combat.md:205), distance² < DistanceToPickupOwnerlessLoot² (5 m, 0x1d59bdcd), state ≠ 3 → state 2 + sub 7.
  This also fires in state 0/1, so a player standing within 5 m of the drop claims it at once.
- **Ownerless out of range**: FUN_143124200 only settles it (1 → 2). The pickup branch needs owner == me, so it never
  scoops.
- **Owned by me**: state 2 + (VacuumLoot "Generic" ability flags and distance < PickupRadiusManual 30) or
  (FLAG_AUTO_SCOOPABLE and < PickupRadiusAuto 20), with room → state 3 at ScoopStartSpeed 0.05 + ScoopAcceleration 0.01
  per tick. If there is no room and the pinata is within 30 m, it shows `LootPinata.InventoryFullError` once (+0x1E5).
- **Owned by someone else** (only possible after sub 4): it flies to that player's entity. If **this** client's own bag
  is full, FUN_143124180 drops it back to state 1. It then settles and stays on the ground for this viewer until a
  RemoveLoot arrives, because FUN_143124200 never scoops a pinata that is not ours.
- **No client-side lifetime**: nothing reads LootPinata.DecayAge (0x65741807) [P, agent-e-leads §2]. FUN_143124fa0 (age
  = now − spawn) has no caller [P refscan]. The pinata lives until RemoveLoot / PickupLootResponse / arrival.

**No state ever clears +0x200** [P: the only writers of +0x200 / +0x70 are the ctor and the sub-4 handler; the scan of
FUN_14312xxxx for `+ 0x200)` / `+ 0x70)`]. A sub 4 with owner 0 would not make a bag claimable either. It sets state 3,
the copy flies to each viewer's own player and is destroyed on arrival without a PickupLoot [P by the same branch].

## 4. What this implies about the live rule

1. **Ownerless is decided at spawn** [P mechanism / I intent]. The server could only "free" an owned bag by RemoveLoot
   plus a new SpawnLoot with owner 0. That relaunches it from the given point, in a fresh arc, through state 0 [P:
   FUN_1431257a0 runs on every 0→1]. A "the bag becomes free after N seconds" rule (WoW-style) would make old bags jump
   up again on screen. Nothing in the client supports that rule, while the client does carry special handling for
   ownerless pinatas *at spawn*.
2. **It is world loot for whoever comes first** [P mechanism]: shared arc seeds, a 5 m walk-up claim (against 20/30 m
   auto-scoop for your own loot), the first claim wins, and every viewer sees it fly to the claimer.
3. **Personal is the norm** [P sources]: chests give each player within 50 m their own loot (P1089, P1647, wiki Chests:
   "Each person that gets loot from a chest, will get different items"); reward chests give each group member theirs
   (P6503). The client cannot show an owned bag to anyone but its owner, so live never showed a "someone else's bag".
4. **The drops without an entitled player were removed in 2016**: P4349 (1 Apr 2016) "Exiting play mode no longer has a
   chance to drop loot", P4719 (26 Apr 2016) "You should not receive loot if an ally or neutral creature kills an enemy
   creature", P5768 (7 Jun 2016) "Any creatures affected by a Damage Node will NOT drop loot". In each case a death with
   no player killer used to drop loot that a player could get, and the fix was to drop **nothing**, not to make it
   personal [I]. The ownerless path is the client mechanism that let a non-killer pick such loot up. That fits all three
   [I: no source names the path].
5. **What remains in the 0.695 era**: "it continues to also drop destroyed terrain loot if it explodes" (P4533,
   13 Apr 2016), the resources of the voxels a Fiery Fungus blast destroys. That is loot no player harvested [I]. It is
   the best candidate for ownerless chunks / bags.

Confidence: §4.1-4.3 high (client code). §4.4-4.5 medium-low: an inference from the patch-note pattern. No source
states it.

## 5. Group / party loot

- Group packets (RTTI, 0.695): AcceptTeleport, AddGroup, AddMember, AutoGroup, ChangingJob, Disband,
  FindLookingForMoreGroup(+Reply), GetGroupInfo(+Reply), InMinigame, Invite, InviteToGroupContent, Join,
  JoinLookingForMore, Kick, Leave, MapPing, RemoveGroup, RemoveInvitation, RemoveMember, SetGroupDescription,
  SetGroupFlags, SetGroupOwner, SetPrimaryTarget, UpdateMember. **None is about loot** [P]. The Lua Groups / GameSettings
  binds (luabind_695.tsv) are invite, join, leave, kick, disband, owner, auto-group and looking-for-more only [P].
- No string in the decompile and no text id for need / greed / round robin / free for all / master loot / loot rules /
  kill credit / tapped / tagged [P, searched `re/decomp_all_695` literals and text_695.tsv]. No group-loot UI in the
  shipped AS3 widgets (as3_695: only Chat / Crafting mention "loot") [P]. The UI Lua is compiled (UI/ScriptsBase.bin). The
  plain Resources/Scripts only have the chat Loot tab (ChatManager.lua:161) [P].
- There is no EQN / PS2 group-loot code in 0.695 to test with deadcode.py. Nothing was found to test.
- PlayerUpdate LootEvent 0x15 is a visual only: {u64, vec4 from, vec4 to, string effect} → a composite effect trail from
  one point to another (0.589 dispatcher case at part_0065.c:11397, reader FUN_142b8fe70) [P]. The guid is not used
  there, and it has no ownership logic. It is probably the "purple bundles / particle trails among your loot" (P4534) [I].
- HUD: Hud::HudIndicatorDefinitionLootPinata / LootPinataIndicator (FUN_14343c620, FUN_143448a00) shows "%.0fm" to each
  pinata and has no owner branch [P].

## 6. 0.589 compared

The same layout and logic: the reader FUN_1430775a0 has the same offsets; the update FUN_14307d0f0 has the same branches;
the ctor seeds the arc for ownerless only (part_0085.c:31199); the 11 s contact rule is there (part_0085.c:33450) [P].
Nothing changed between the launch build and 0.695.

## 7. Searched without a rule found

- Client: every LootPinata* function (§2-3), RTTI (rtti_695.tsv "loot"), decompile string literals (loot, need, greed,
  round robin, free for all, master loot, kill credit, tagged, tapped, lockout), text_695.tsv (loot, claim, someone else,
  share, group), Resources/Scripts/*.lua, as3_695, luabind_695.tsv, StringHashToValue (LootPinata.* rows 374-382). The client reads DistanceToPickupOwnerlessLoot,
  MaxDroppingTime, PickupRadiusAuto / Manual, ScoopAcceleration, ScoopStartSpeed and ScoopOffset (0x910af91d, the height
  offset of the scoop target in FUN_143127a10). It does not read DecayAge. DefaultLootPinataId was not checked.
- Design: patchnotes.tsv (all "loot" / pick up / vacuum rows), design/forum/all (dev posts with "loot": every hit is
  listed in the scan; none on ownership), forum phrases (steal / someone else's loot / loot on the ground / loot bag /
  shared / personal / ninja), wiki pages Loot, Chests, Exploring, Lost Chests, Lost Crystals, Combat, Gathering.
- **What would settle it:** a live packet capture of a SpawnLoot with owner 0 (none known), or archived video with two
  players and one bag (not in the dumps). No runtime test of our client can reveal live's server rule. A runtime test can
  only confirm our implementation: two clients, `lootfree` near both; check the shared arc, that the first to come
  within 5 m wins, and that the other client sees the bag fly to the claimer.

## 8. Server rule to implement (`Game/LootPinatas.cs`, `Game/Creatures.cs`)

1. **Never convert.** An owned pinata stays personal until it is collected or until BagLinger (120 s) → RemoveLoot. No
   "abandoned bag" re-spawn: the client would relaunch it [§4.1]. Option b of agent-props2.md §1 is not supported.
2. **Ownerless only at spawn, for drops with no entitled player that the server still makes**:
   - Fiery Fungus explosion destroyed-terrain loot (P4533): spawn it with `SpawnOwnerless` to the viewers of the blast.
     Today `Creatures.cs:1767` gives the fungus's explosion bag to `c.Target`. Keep the fungus's own "tree loot on
     death" personal to the player who killed it. Only the terrain part is world loot [I].
   - Deaths with no player killer (ally / neutral creature kills, Damage Node, leaving play mode): **no drop at all**
     (P4719, P5768, P4349). Not ownerless.
   - Chests, reward chests, Lost Crystals, creature kills with a player killer, harvesting: personal, one spawn per
     entitled player (as now).
3. **Send the arc seeds for ownerless spawns**: write three random IEEE floats in [0,1) into the last three fields
   (+0x64 angle, +0x68 heading, +0x6C speed) of the same SpawnLoot sent to every viewer. Today the server writes the
   `uint a, b, c` slots as integer 0. Write `float` (bit pattern) for ownerless spawns. For owned spawns the client
   ignores them.
4. **Clean up the claim**: when the claimer's PickupLoot (sub 5) for a formerly-ownerless pinata is accepted, also send
   RemoveLoot (sub 2) to every other viewer. A viewer whose own bag is full keeps the copy lying on the ground (§3). An
   unknown id is ignored by FUN_143126f70, so this is harmless [P]. Today the pending entry is removed on pickup, so
   `RemoveIfLeft` never reaches those viewers.
5. Keep the existing claim answer: the first valid sub 7 sets the owner and sends sub 4 to all viewers, with no reply to
   repeats (agent-props2.md §1; it matches §2-3 above). Optionally ignore claims from knocked-out players; the client
   already does not send them.
6. Optional: let `SpawnOwnerless` take a pinata definition + item / count, so destroyed-terrain loot can use the resource
   chunk models (PinataFor(item)) instead of the bag (def 5). The contents are still held server-side and given on
   PickupLoot.

Keep OPEN-LEADS L141 open as "rule inferred (world-caused drops only); mechanism proven". Reopen it if a capture or a
dev statement turns up.

---

## Summary

- The client has no way to make a bag ownerless after it spawns (no timer, no packet; sub 4 only sets an owner). A
  pinata is ownerless from birth or never [P].
- Ownerless pinatas take their drop arc from three server floats (SpawnLoot +0x64/+0x68/+0x6C) so that all viewers see
  one shared drop. They resist settling for 11 s and are claimed by the first player within 5 m [P]. These are the
  traits of world loot shared at the drop.
- There is no group-loot, loot-rule or kill-tag code anywhere in 0.695 or 0.589 [P].
- No client field is an ownership timer or group id. The fields the server writes as 0 that matter are the three arc
  seeds (ownerless only). +0x38 has no reader [P].
- The live trigger is not in the client or in any searched source. Strongest-supported rule [I, medium-low]: ownerless
  only for server drops with no entitled player. In 0.695 that is the Fiery Fungus blast's destroyed-terrain loot
  (P4533). Non-player kills drop nothing (P4349 / P4719 / P5768). Never convert an owned bag.
- Server to-dos: send random float seeds for ownerless spawns, send RemoveLoot to the other viewers after the claimer's
  pickup, and route the fungus terrain loot through `SpawnOwnerless`.
