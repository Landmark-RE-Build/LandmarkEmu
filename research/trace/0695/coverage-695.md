# 0.695 packet coverage: every family and sub-opcode, client 0.589 → 0.695, versus the emulator

Agent #10, 2026-10-15. Input: trace/0695/packets-695.md (agent #3: new classes, changed layouts; not re-traced here,
cited as "packets-695 §n" or "§A…" / "§B…"; a bare "§n" is a section of this file), trace/0695/functions-695.md (agent #5, Breaks B1-B9), trace/0695/storytools-695.md (agent #6),
trace/architecture/channels*.md. Everything below is static: client = re/decomp_all (0.589) and re/decomp_all_695
(0.695) plus read-only disassembly of both Landmark64.exe; server = LandmarkEmu/LandmarkEmulator.WorldServer source
(file:line as of today). Nothing was run against a client or server.

Scratch (re/out/agent_coverage/, all scripts re-runnable): `extract.py` (every packet-class construction with its
header constants: the base at the stack slot vtable+8, the sub at vtable+0x10, nearest assignment) → inst_589/695.tsv;
`ctorcalls.py` (readers of constructor-built packets); `sd.py` (port of re/tools/subdispatch.py to both builds: zone
dispatcher jump table → each family's sub-switch, decoded from the exe; PlayClient vtable slots resolved for the
`call [rax+N]` cases) → sd_589/695.tsv; `rcmp.py` (per class: the 0.589 reader / send template mapped through
trace/0695/functions.tsv to its 0.695 partner, closure depth 2 compared with agent #3's normalisation) → rcmp.tsv;
`hdronly.py` (separates "only the PlayerUpdate u8→u32 sub read/write differs" from real body changes);
`casecmp.py` (every family dispatcher split at its case labels and compared case by case, renumbering detected by the
best shift) → casecmp.tsv; `rdrcmp.py` / `laycmp.sh` (pkt.py layouts of the readers each changed case calls, header
stripped) → rdrcmp.tsv; `zonecmp.py` (families without a sub switch: zone-case closure depth 3) → zonecmp.txt;
`pair.py` (one explicit dispatcher pair, used for the Prop dispatcher reached through a vtable);
`srvmodels.py` / `srvsubs.py` / `srvsends.py` (server: enum decode, [ZoneMessage] models, handlers, case labels,
sub written first into every EnqueueMessage payload, helper calls `X(session, sub, …)`) → srv_*.tsv;
`build_table.py` → table.md (§10), `famtable.py` → famtable.md (§9).

## 1. Summary

**Scope.** 153 zone families (the 0.589 and 0.695 zone dispatchers FUN_142c83660 / FUN_142d1ccf0 have the same 153
non-default opcodes out of 284 table entries) plus the Login, Gateway and RemoteAssetDelivery protocols. The table
(§10) has **1,318 rows** (one per client packet class with a wire header, plus PlayerUpdate sub-opcodes known only from
the id table, the dispatcher or the server enum) in 173 sections; 88 further id-table names without any client class are listed under their family;
the Command (0x1E, 353 ids) and Admin (0x1F, 361 ids) id tables are identical in both builds (diff of
re/out/agent_packets/packetids_589/695.txt) and are not listed name by name; their 145 compiled classes are in the
table (137 same, the rest inline, see §10).

| 0.695 status | Rows | Emulator implements (handled / sent) | Not implemented |
|---|---|---|---|
| same (reader / writer closure equal) | 1,046 | 353 | 693 |
| same (inline reader; family dispatcher cases equal) | 57 | 10 | 47 |
| **header changed** (PlayerUpdate 0x26: sub u8 → u32) | 125 | **32** | 93 |
| **renumbered** (Claims ≥0x45 +2, Mail ≥4 +1, InGamePurchase ≥0x30 +1, InstancedGame ≥0xD +1) | 46 | **33** | 13 |
| **renumbered + body changed** | 3 | **2** | 1 |
| **body changed** | 20 | **16** | 4 |
| **new in 0.695** | 19 | 1 (SetPlayerBio stub) | 18 |
| value domain only (same layout, new value the client understands) | 2 | 1 | 1 |
| removed in 0.695 | 0 (no packet class in re/out/rtti_gone_695.txt) | – | – |

Direction: 579 C→S, 732 S→C, 1 both (SetPlayerBio), 6 PlayerUpdate id rows without an S→C case (C→S or unused).

**What a 0.695 client breaks on today: 83 implemented rows** — 32 PlayerUpdate rows (31 sub-opcodes; header), 35
renumbered messages (InGamePurchase 15, Mail 13, InstancedGame 6 of which GetConfigurationResponse and
SetConfiguration also changed body, Claims 1; §5.2), 16 body changes (SendSelfToClient, DisplayPropText, 3 Template,
4 Claims, 4 InstancedGame, EditSettingsReply, PlayerSet, and PropFromZoneToClient — wire-compatible as the server sends
it today) — plus the 0.695-only sub numbers that land on a different 0.589 meaning in the server (§5.2 "collisions").

**Header widths.** Every family header writer the 0.695 client uses is code-identical to its 0.589 partner except
the PlayerUpdate one (re/out/agent_packets/hdrw.txt: 243 send templates; FUN_142c13350 vs 0.589 FUN_142b79bb0
changed, 13 templates), and every S→C family dispatcher reads the same sub width except PlayerUpdate (FUN_142cdfa60
`*(int *)`, 0.589 FUN_142c45fa0 `(char)`). The other changed / new header-writer entries are body writers
(PlayerSet FUN_1430a0530, InstancedGame PropRequest FUN_1434d62d0, SetConfiguration FUN_1434d6620, AddOrRemoveRule
FUN_1434d5e90, Matchmaking FUN_1434d5fd0).

**Sub numbering.** Only the four families above renumber; verified three ways that agree: the id tables
(packetids diff: Claims 31 and Mail 20 entries moved, plus PlayerUpdate 0x80 and Prop 0x1D new), the per-class header
constants (cmp.py: 74 classes differ, all in those families or new), and the dispatcher case lists (casecmp: a shift
is found only for 0x45, 0xF8, 0xF9 (last case 0x5E→0x60) and 0x10D; 0x26 gains case 0x7F, 0x2D gains 0x1E, 0x10C gains
8, 0xF0's handler gains 0x1E). No family lost a sub.

## 2. Method notes and limits

- "same" = the 0.589 reader / send template and its callees to depth 2 are identical after normalising addresses,
  names and variables (functions.tsv pairs, agent #3's closure_check normalisation). Where funcdiff mis-paired the
  many near-identical PlayerUpdate wrapper readers ("unpaired"), the pkt.py layouts were compared directly
  (laycmp.sh; all equal after the header).
- 176 packet classes in the RTTI of both builds (re/out/rtti*.tsv; 68 of them PlayerUpdate) are **never referenced by vtable** in either
  decompile (e.g. PlayerUpdatePacketDisplayPropText, the Encounter* replies, ClientActivityLaunch*, Vehicle*): the
  optimiser dropped the vtable store, so their header constants are not compiled. PlayerUpdate ones are covered by
  id-table rows + the per-case comparison (§3); the rest belong to families whose dispatchers are case-for-case equal
  (famtable.md), so their 0.695 status is "same", but the class ↔ sub mapping for them is not proven from client
  constants → **L53** (§7; list in §11).
- Emulator columns are from a static scan (handlers / models / case labels / first payload write / helper calls /
  runtime opcode_stats recorded in trace/families "SENT"). "not sent / not handled" is "not found by the scan"; the
  rows that matter for 0.695 (§5) were read by hand.
- Zone-level closure "DIFF" in famtable.md includes UI / logic code below the readers (depth 3); it is not a wire
  verdict by itself — the per-case and per-reader results are.

## 3. L38 closed: classes whose readers are inline in dispatchers

Every class in L38 was compared, by reader closure where a reader exists (ctor-site readers found by ctorcalls.py) or
by the family dispatcher case (casecmp.py) where it is inline:

| L38 item | Result | Evidence |
|---|---|---|
| PacketChat 0x13/1 | same | readers 1432aed20 / 1432a9b80 → 14335bc40 / 143356a90 |
| ClientUpdate ItemUpdate 0x29/3, UpdateCalculatedStats 0x29/0x2C | same | 142b832a0 → 142c1ca60; UpdateCalculatedStats_Read 142baf500 = FUN_142c49070 (layout equal) |
| Groups 0x2B replies (FindLookingForMoreGroupReply, GetGroupInfoReply, AddMember...) | same | 24 dispatcher cases same (FUN_142d4c840 / FUN_142deaa10); readers same |
| Encounter 0x2C (DetailsResponse, EncounterComplete) | same | 18 cases same (FUN_143d929a0 / 143d92330 → FUN_143e54970 / 143e54300); zone closure same |
| Inventory GrapplingHookEquipped 0x2D/0x18 | same | dispatcher FUN_142e81590 / FUN_142f202e0 case 0x18 same; only 0x1D changed (control flow before the new 0x1E) |
| ReferenceData 0x2F/1, /3 | same | readers 142bad7d0 / 143ac1da0 → 142c47350 / 143b7db80; zone closure same |
| Crafting 0x42 (BaseRecipe, SalvagingAddItemResponse) | same | 19 cases same, case 5 differs only in a named-function label (Stream_ReadString); 142e91a90 → 142f30630 same |
| InGamePurchase store bundles / search results / review response | same (renumbered) | all 40 cases same after the +1 shift ≥0x30 |
| QuickChat 0x46, SetLocale 0x5B, Ignore 0x72, Activity 0x8F, InitializationParameters 0x9F, Experience 0xBE, Resources 0xC4, Equipment 0xCB, 0xDD speech packs / PC data, Chest 0xFF, ToolBase 0x117, PlayerTitle 0x94 | same | zonecmp.txt depth-3 closures equal (0x46, 0x5B via 142be2e00 → 142c7c980, 0x72, 0x8F, 0x94, 0x9F, 0xBE, 0xC4, 0xCB, 0xDD, 0xFF, 0x117) |
| Achievement / Tracker initialize 0x92 | same | 11 cases same (FUN_142d92750 / FUN_142e30720) |
| Skills 0xBC/0xA | same | 8 cases same |
| PlayerUpdateManagedPosition 0xC7 | same | own family; send builder 142c0bb80 → 142ca5960 differs only in an assert line / struct offset |
| Hud radial notification 0xDC | same wire | zone closure differs only in the radial UI (143494590 → 143544280) |
| Prop 0xED FromZoneToClient / EditSettingsReply / AccessReply | **changed (lists)** | Prop dispatcher 0.589 FUN_142f1b980 / 0.695 FUN_142fba9f0 (reached through a vtable; pair.py): cases same except 6 (PickUpReply: new error 0xE) and 0x1C (EditSettingsReply: f64 records, option labels — storytools-695 §1.4). FromZoneToClient top level equal; its two list elements carry the settings record (f64). AccessReply = sub 2 case, same |
| VoxelConstruction 0xF4 (1, 2, 4, 6, 7, 0x16) | same | 7 cases same, 1 same-wire, case 0xA differs only in a mistyped decompiler argument (reader 1430b11e0 → 14315b8a0 same); terrain-695 §6 agrees |
| PaletteUpdate 0xF5 | same | zone closure same |
| Mail ReadMessageResponse | same (renumbered 9 → 0xA) | reader 1432c77e0 → 143374610 same |
| Teleport 0xFC | same | 10 cases same |
| ChestEnableWindow 0xFF | same | 142da90f0 → 142e470c0 same |
| Login / Gateway external packets | same | all readers / writers same (rcmp); the four inline Login replies (ServerListReply, ServerUpdate, CharacterCreateReply, ForcedDisconnect: 1439999d0 / 143999ac0 / 143999bc0 / 143999cf0 / 143998db0 / 143999780 …) closure-equal; the LoginRequest delta is a value, not a layout (L9) |
| PlayerUpdate inline cases (all 0x26 S→C subs) | header only, except 0x1B, 0x79 | per-case + per-reader: 59 readers equal after the header, 18 identical, the rest checked by hand (RemovePlayer_Read 142b90ef0 / FUN_142c2a960; RemoveNotifications 142bb3270 vs FUN_142c4cae0 → FUN_142c2a7c0: {i32 n; n × u64, u32, u32}; LightweightToFullNpc / Vehicle body readers 142b8fc70 = FUN_142c295e0; AddLightweightPc_Read / LightweightToFullPc_Read / Movement_ReadBody: header only; MobMovement case 0x71 reader FUN_142c29ab0 in both). InteractionInfoReply 0x6A, DynamicInteractionFlagging 0x6B and SetEnabled 0x73 (S→C) are read by the proxied-packet handler 0.589 FUN_142cbb870 / 0.695 FUN_142d54c10: header only |

## 4. Findings not in packets-695.md

1. **ChatPacketDebugChat is sub 3**, not 2 (class constants in both builds: FUN_1432b7bc0 / FUN_143364980; sd case 2 =
   sub 3). Its trailing bool (§B8) stands.
2. **CharacterCustomization PlayerSet 0x11C/1 (C→S) has two trailing u32** (agent #5 B5, verified): 0.589 template
   FUN_142ffd830 writes u32, u64, customization data (FUN_142eba3f0); 0.695 writer FUN_1430a0530 writes the same plus
   u32 (+0xb0, new base-actor id) and u32 (+0xb4). The emulator parses it (Barbershop.cs:37-51).
3. **Abilities EffectEnd 0x27/0xD: new result 9** (agent #5 B7, verified): reader same (142d64420 → 142e02440);
   handler FUN_142e18c70 adds `case 9: "ClaimDestruction.Fail.NotAllowed"` (0.589 FUN_142d7aca0 knew 2-8). Value only.
4. **Prop PickUpReply 0xED/6: new error 0xE** `Prop.PropPickupErrorPartialLootC…` (Prop dispatcher FUN_142fba9f0
   case 6; reader unchanged). Value only.
5. **Game Table settings key** (agent #5 B1, verified): the 0.695 key is a hash of the setting name: FUN_143c1c060 /
   FUN_142dc6b80 hash "InstancedGames_IsGameFFA" / "InstancedGames_AllowSwitch" with `h = (c + h) * 0x401; h ^= h >> 6`
   per char (signed char, arithmetic shift on int), then `h *= 9; h ^= h >> 11; h *= 0x8001` (Jenkins one-at-a-time with
   signed shifts). The server must send that u32 key in GetConfigurationResponse (§B7) for 0.695.
6. **InstancedGame Event types 10 / 11 read pointCount at +0x6c** (agent #5 B9): part of §B6's new Event body.
7. **PropBase 0x0A ListReply exists with the same reader in both builds** (0.589 FUN_142f0bc80 = 0.695 FUN_142faa9b0,
   Prop dispatcher case 0xA identical). MiscHandler.cs:283-287 sends the captured 0.695 prop list
   (Resources/PacketDumps/145-PropBase) to 0.695 clients only, on the stated ground that 0.589 has no reader for it —
   not supported by the client code → **L52**.
8. **AchievementAdd 0x92 sub 2** (coordinator request, agent #9's finding, verified): S→C, Achievement dispatcher
   0.589 FUN_142d92750 / 0.695 FUN_142e30720 case 2 → reader 0.589 FUN_142d8c370 / 0.695 FUN_142e2a340 (code same) →
   handler 0.589 FUN_142d8f4a0 / 0.695 FUN_142e2d470 (changed, logic only). Wire: u16 0x92, u8 2, then **one
   achievement record** read by FUN_142c161b0 (0.695; the same record reader the Initialize list FUN_142e29f60 uses
   per element, so the layout is the Initialize element: docs/protocol.md:739-740). Emulator: not sent —
   Achievements.cs:38-39 has subs 3, 4, 6, 7, 8, 0xB, 0xC (+0xF at :837), no 2. No 0.695 change needed for it.
9. **PlayerUpdate is header-only everywhere else**, including all the classes in agent #3's "not compared" set and the
   ones without compiled vtables (§3).
10. The **proxied-packet handler** (0.589 FUN_142cbb870 / 0.695 FUN_142d54c10) is a second S→C entry for PlayerUpdate
    0x6A / 0x6B / 0x73, Abilities, Achievements, WorldEditing, Prop, Weapon packets wrapped for another entity
    (cmp_sent's "BasePacket 0x1E/0x20" row is this function, not a packet).

## 5. Version-gating plan (one per-build opcode / serializer layer)

Build detection exists: Gateway LoginRequest.ClientBuild (LandmarkEmulator.Gateway/Network/Message/Model/
LoginRequest.cs:19) → WorldSession.ClientBuild; ZoneOpcodeTranslator.Uses589Opcodes (Network/Message/
ZoneOpcodeTranslator.cs:13) = `StartsWith("0.589.")`, used today only at MiscHandler.cs:284 and WorldSession.cs:674.
Everything below hangs off one `ClientVersion` on the session (TASKS §0 "Decide how the server tells builds apart").

### 5.1 Header layer (session boundary, both directions)
- **PlayerUpdate 0x26, sub u32 for 0.695.** Outbound: SendPacket/GetOpcodeBytes (WorldSession.cs:917-940) emit the
  enum's bytes; for 0.695, after bytes `26 00 ss` insert `00 00 00` (and the 4-byte enum values 0x2600ss00 become
  `26 00 ss 00 00 00`). Inbound: GetOpcode (WorldSession.cs:950-972) matches 2/3/4-byte prefixes; for 0.695, when
  `data[0..1] == 26 00`, read the u32 sub and rebuild the 3-byte form before lookup, so every PlayerUpdate model keeps
  its 0.589 shape. Raw checks on PlayerUpdate bytes: WorldSession.cs:1129 (`26 00 6C`, still matches), PlayerUpdate
  raw requests (PlayerUpdateRequests.cs:14-30, PlayerUpdateRawRequest). Implemented PlayerUpdate subs affected (32):
  S→C 0x1, 0x3, 0xB, 0x1C, 0x1F, 0x39, 0x42, 0x43, 0x44, 0x49, 0x58, 0x59, 0x5B, 0x62, 0x63, 0x67, 0x6A, 0x6B, 0x79
  (body too), 0x7A, 0x7C; C→S 0xA, 0x18, 0x1A, 0x3B, 0x3D, 0x5E, 0x69, 0x6C, 0x71, 0x73, 0x77; plus 0x80 (new; today a
  stub). Commented-out sends (MiscHandler.cs:235-237: 0x51, ClientUpdate 0x6 / 0x2C) would need it too if re-enabled.
- **No other family needs a header change** (§1).

### 5.2 Sub-number translation tables (per family, both directions)
One table per family: `(insert point, delta)` for 0.589 subs plus the 0.695-only subs mapped to new internal ids, so
the handlers keep 0.589 numbering internally.

| Family | 0.589 subs moved | 0.695-only subs | Server sites using the numbers |
|---|---|---|---|
| Claims 0xF9 (u8) | ≥0x45 → +2 | 0x45 SetAdventureFlags, 0x46 StartClaimPlacement, 0x63 PlayerEnteredClaim (C→S) | PropLimits.cs:28 (0x5F → 0x61). Collision: a 0.695 0x45 / 0x46 today reaches ClaimsHandler.cs:14 → Claims.cs:220 switch as 0.589 0x45 ClaimMoveItemToPlayerOnZone / 0x46 ResetTemporaryTerrainDamage (both unhandled there) |
| Mail 0xF8 (u32 in the emulator model) | ≥4 → +1 | 4 DeveloperMailPlayer (id only, no class) | Mail.cs:28-32 (every constant but ComposeRequest 1) |
| InGamePurchase 0x45 (u16) | ≥0x30 → +1 | 0x30 TemplateProductCreate (C→S) | Commerce.cs:51-64, SiteScreenshots.cs:31, InteractionHandler.cs:268 (0x46 Point of Need → 0x47); InteractionHandler.cs:274 (0x0B, unchanged). Collision: 0.695 C→S 0x30 is 0.589 ProductCreateResponse in Commerce.cs:51 |
| InstancedGame 0x10D (u32) | ≥0xD → +1 | 0xD GetConfigurationRequest, 0x18 AddOrRemoveRule, 0x19 MatchmakingRequest (C→S), 0x1A MatchmakingUpdate (S→C) | InstancedGames.cs:35-36 (TeamMembers 0x14→0x15, ObserverRegistration 0x15→0x16), InstancedGames.Editor.cs:35-36 (EnableGame 0xF→0x10, DisableGame 0x10→0x11), :308 (GetConfiguration 0xD→0xE, SetConfiguration 0xE→0xF). Collisions: 0.695 C→S 0xD = 0.589 response number; 0.695 0xF SetConfiguration hits 0.589 EnableGame (§A5) |
| Prop 0xED (u8) | none | 0x1D DialogResponse (C→S) | – |
| PlayerUpdate 0x26 | none | 0x80 SetPlayerBio (both directions) | MiscHandler.cs:510-516, enum 0x26008000 (ZoneMessageOpcode.cs:210) |
| Template 0xF0 (u8) | none | 0x1D, 0x1F (C→S), 0x1E (S→C) | – |
| Inventory 0x2D (i16) | none | 0x1E ShowActivationFailure (S→C) | – |
| WorldEditing 0x10C (u32) | none | 8 PlayEffect (S→C) | – |
| CharacterCustomization 0x11C (u8) | none | 3 EquippedAttachmentRequest (C→S), 4 Reply (S→C) | Barbershop.cs:24 (PlayerSetSub 1) |
| Inspect 0x91 (u8) | none | 3 RequestInspectPlayer (C→S) | – |

**Collisions today (0.695 client against the 0.589-numbered server)**, from the table:
- Inbound, processed as a *different handled* message: InGamePurchase 0.695 UnFollowCreator 0x36 → server
  RequestAccountStore (Commerce.cs:134); InstancedGame 0.695 SetConfiguration 0xF → server EnableGame and 0.695
  StartRequest 0x10 → server **DisableGame** (InstancedGames.Editor.cs:35-36, :203-217); 0.695 RequestJoinTeam (sub 8,
  type 9) → server Request handler without type 9 (InstancedGames.cs:752-789).
- Outbound, read by the 0.695 client as a *different* message: Mail NewMailInboxNotification 0xE → 0.695
  **ClearInboxResponse**; InstancedGame TeamMembers 0x14 → 0.695 **PlayerRemoval**. The other renumbered sends
  (InGamePurchase 0x30, 0x38, 0x3E, 0x40, 0x45, 0x4D; Mail 4, 7, 9, 0xB, 0x10, 0x13, 0x15; Claims 0x5F; InstancedGame
  0xD) land on numbers the 0.695 dispatcher has no S→C case for and are dropped.

### 5.3 Per-build serializers (bodies)
Writers (S→C) — add the 0.695 shape behind the session's version:
- SendSelfToClient: + trailing str bio (Network/Message/Model/SendSelfToClient.cs:16, end of Write ~2461-2465).
- PlayerUpdate 0x79 DisplayPropText: + u64, + u32, + bool (Props.cs:76-124; §B2, L30).
- TemplateBasicData without the PropData list; VoxelDataResponse with the PropData list after the cell list
  (Templates.cs:268 WriteBasicData, used by subs 2, 3, 5, 0xC: Templates.cs:83, 219, 249, 421, 545; §B4).
- Claim record: site-game trailing u32 + trailing u64 adventure flags (Claims.cs:761 WriteRecord → subs 4, 7, 9:
  Claims.cs:616, 627, 638); ClaimInfoUpdate: trailing u64 + kind 0x17 (Claims.cs:983 BuildInfoUpdate; §B5).
- InstancedGame: PropRecipe item + u32 (InstancedGames.Editor.cs:96), HUD entry + bool + u64 (InstancedGames.cs:1406),
  Event: str, str, u32, u32 after the two u64 (InstancedGames.cs:1171, 1557), GetConfigurationResponse new shape with
  hashed keys (InstancedGames.Editor.cs:388-452; §B7 + §4.5 hash).
- Prop EditSettingsReply 0xED/0x1C: f64 record values + option label strings (PropSettings.cs:185-219;
  storytools-695 §1.4, TASKS 2.18). PropFromZoneToClient 0xED/2: no change while its lists stay empty (Props.cs:574).
Readers (C→S):
- PlayerSet 0x11C/1: + 2 × u32 (Barbershop.cs:43-51).
- InstancedGame PropRequest 0xA: + trailing u32 (InstancedGames.Editor.cs:237); SetConfiguration (0.695 0xF): new
  shapes (InstancedGames.Editor.cs:193).
- SetPlayerBio 0x80: str bio (Network/Message/Model/PlayerUpdateSetPlayerBio.cs:12, today reads nothing).

### 5.4 Value domains (no layout change)
EffectEnd result 9 for blocked voxel destruction (Buffs.cs:235 / Gathering.cs:335 send EffectEnd), PickUpReply error
0xE (not sent today), ClaimInfoUpdate kind 0x17, AddBinding result 4 (storytools-695 §1.8).

### 5.5 Implement (0.695-only messages): see Gaps (§6).

## 6. Gaps (TASKS §1-style; the coordinator merges)

- [ ] **C.1 Per-build PlayerUpdate header** (u32 sub) in WorldSession.GetOpcode / GetOpcodeBytes for 0.695; 32
      implemented sub-opcodes (§5.1). Source: coverage-695 §5.1, packets-695 §A1.
- [ ] **C.2 Sub translation tables** for Claims, Mail, InGamePurchase, InstancedGame, both directions, with the four
      0.695-only collisions routed to new ids (§5.2). 35 implemented messages.
- [ ] **C.3 Per-build body serializers** for SendSelfToClient, DisplayPropText, TemplateBasicData / VoxelDataResponse,
      claim record + ClaimInfoUpdate, InstancedGame PropRecipe / HUD / Event / GetConfigurationResponse (hashed keys),
      EditSettingsReply (§5.3).
- [ ] **C.4 Per-build parsers**: PlayerSet (+2 u32), InstancedGame PropRequest (+u32), SetConfiguration (0.695 shape),
      SetPlayerBio (read the string).
- [ ] **C.5 New 0.695 messages** (each is in TASKS §1 already; this is the packet list): 0x26/0x80 SetPlayerBio (store,
      SendSelfToClient), 0x91/3 RequestInspectPlayer → StartInspect 0x91/1, 0xF0/0x1D-0x1F design materials,
      0xED/0x1D DialogResponse, 0x10D/0xD GetConfigurationRequest, /8 type 9 JoinTeam, /0x18 AddOrRemoveRule, /0x19
      MatchmakingRequest → /0x1A MatchmakingUpdate, 0xF9/0x45 SetAdventureFlags, /0x46 StartClaimPlacement, /0x63
      PlayerEnteredClaim, 0x11C/3 → /4 EquippedAttachmentRequest / Reply, 0x2D/0x1E ShowActivationFailure,
      0x10C/8 WorldEditingPlayEffect, 0x45/0x30 TemplateProductCreate.
- [ ] **C.6 Value domains**: EffectEnd result 9 when Play Mode Rules block destruction; PickUpReply 0xE (partial loot).
- [ ] **C.7 AchievementAdd 0x92/2** (one achievement record) for goal tiers (agent #9 / wiki audit) — both builds.
- [ ] **C.8 Re-check the 0.695-only prop list send** (MiscHandler.cs:283-287) against L52.

## 7. Open leads (added to OPEN-LEADS.md)

- **L38** closed (in place): §3.
- **L52** PropBase 0x0A ListReply: both builds have the same reader (FUN_142f0bc80 / FUN_142faa9b0); the server sends
  the captured 0.695 dump only to 0.695 on the ground that 0.589 has no reader. Need: the dump's body against the
  reader's layout (header reader FUN_142fa8940: bool, u32, u64; element reader FUN_142fa7e30), and a 0.589 test.
- **L53** 176 RTTI packet classes with no vtable reference in either decompile: class ↔ sub not proven from client
  constants outside PlayerUpdate. Their families' dispatchers are case-for-case equal, so nothing changes for 0.695;
  mapping each to its case needs the reader called in each case (sd_*.tsv calls) matched to the class by its dtor /
  RTTI COL.

## 8. Hand-offs

- **#3 (packets):** ChatPacketDebugChat is sub 3 (§B8 says 2). PlayerSet 0x11C/1 C→S gained two trailing u32 (§4.2;
  writer FUN_1430a0530, not in packets-695.md). PropPickUpReply 0xED/6 new error 0xE. InteractionInfoReply /
  DynamicInteractionFlagging / SetEnabled (S→C) are read by the proxied-packet handler FUN_142d54c10, not the
  PlayerUpdate dispatcher.
- **#5 (functions):** B1 (hash: signed-char, arithmetic-shift Jenkins variant, §4.5), B5, B7, B9 verified at packet
  level; B2-B4, B6, B8 are logic, not layouts (not re-checked here).
- **#6 (story tools):** PropFromZoneToClient (0xED/2) list elements carry the settings record (f64 in 0.695): matters
  only if the server starts filling those lists (Props.cs:574-575 writes them empty).
- **#9 (wiki):** AchievementAdd 0x92/2 verified (§4.8): S→C, one achievement record, reader unchanged, never sent.
- **Coordinator:** L52 (0.695-only prop list send at MiscHandler.cs:283-287), L53 (classes without compiled vtables);
  the inbound / outbound collisions in §5.2 are the first things a 0.695 test will hit after the PlayerUpdate header.

## 9. Family overview (zone dispatcher)

Per zone opcode: the family handler with a sub switch (decoded from the exe), the per-case comparison, and the
depth-3 closure of the zone case (UI logic included, so "DIFF" there is not a wire verdict). Families reached through
a client vtable without a decoded switch (0x1E Command, 0xED Prop, 0x11C CharacterCustomization...) are covered at
class level in §10 (Prop also by pair.py, §3).

Every "changed" case was resolved by hand: PlayerUpdate 0x26 — header only except 0x1B / 0x79 (§3); Abilities 0x27
cases 9 / 0xA / 0x19 / 0x21 and ClientUpdate 0x29 cases 0x14 / 0x23 / 0x26 / 0x2B differ only in global-data addresses
or named-function labels (reader UpdateCalculatedStats_Read = FUN_142c49070); Crafting 0x42 case 5 and WorldEditing
0x10C cases 1 / 7 only in the Stream_ReadString label, 0x10C case 4 in a global address (case 8 = new PlayEffect);
Inventory 0x2D case 0x1D in control flow before the new 0x1E; VoxelConstruction 0xF4 case 0xA in a mistyped decompiler
argument; ObjectScripting 0x111 case 4 is the AddBinding result now an int (storytools-695 §1.8, layout same); Chat 0x13
case 3 = DebugChat (§B8); Claims 0xF9 case 4 = AOIUpdate (§B5); InstancedGame 0x10D cases = §A5 / §B6 / §B7. Case
values are in each switch's own domain (PlayerUpdate: sub − 1; Chat: sub).

| Opcode | Family | Sub-switch handler 589 / 695 | Cases (per-case result) | Zone-level closure (depth 3) |
|---|---|---|---|---|
| 0x1 | Skills | FUN_143337930 / FUN_1433e4730 | 7 same | same |
| 0x3 | Preference | FUN_142be76b0 / FUN_142c812b0; FUN_143d92330 / FUN_143e54300; FUN_143d929a0 / FUN_143e54970 | 28 same | same |
| 0xF | PacketSendSelfToClient | – | no sub switch decoded | DIFF |
| 0x11 |  | – | no sub switch decoded | same |
| 0x12 | PacketClientZoneCoordinateInfo | – | no sub switch decoded | DIFF |
| 0x13 | Chat | FUN_1432b7bc0 / FUN_143364980 | 1 changed, 12 same, 1 same-wire | DIFF |
| 0x1A | PacketTargetClientNotOnline | – | no sub switch decoded | same |
| 0x1E | Command | – | no sub switch decoded | same |
| 0x23 | PacketClientBeginZoning | – | no sub switch decoded | same |
| 0x24 | Combat | – | no sub switch decoded | same |
| 0x26 | PlayerUpdate | FUN_142c45fa0 / FUN_142cdfa60 | 87 changed, 1 only695, 16 same, 5 same-wire | same |
| 0x27 | Abilities | FUN_142d7cae0 / FUN_142e1aab0 | 4 changed, 20 same | DIFF |
| 0x29 | ClientUpdate | FUN_142c3fec0 / FUN_142cd9980 | 4 changed, 39 same | DIFF |
| 0x2A | MiniGamePayload | – | no sub switch decoded | DIFF |
| 0x2B | Groups | FUN_142d4c840 / FUN_142deaa10 | 24 same | same |
| 0x2C | EncounterInvitationResponse | FUN_143d92330 / FUN_143e54300; FUN_143d929a0 / FUN_143e54970 | 18 same | same |
| 0x2D | Inventory | FUN_142e81590 / FUN_142f202e0 | 1 changed, 1 only695, 8 same | DIFF |
| 0x2E | PacketSendZoneDetails | – | no sub switch decoded | same |
| 0x2F | ReferenceData | – | no sub switch decoded | same |
| 0x30 | ObjectiveClientComplete | FUN_14326b630 / FUN_1433184e0 | 9 same | same |
| 0x32 | SetWaypoint | FUN_143346ad0 / FUN_1433f2b90 | 17 same | same |
| 0x34 | QuestReply | FUN_142e8e1e0 / FUN_142f2cd80 | 13 same | same |
| 0x35 | Reward | – | no sub switch decoded | same |
| 0x37 | PacketGameTimeSync | – | no sub switch decoded | same |
| 0x38 | PacketDelayWrapped | – | no sub switch decoded | DIFF |
| 0x3D |  | – | no sub switch decoded | same |
| 0x3E | TradeInitiate | FUN_14358ee50 / FUN_143641d20 | 9 same | DIFF |
| 0x41 |  | – | no sub switch decoded | DIFF |
| 0x42 | Crafting | FUN_142e9acd0 / FUN_142f39870 | 1 changed, 19 same | DIFF |
| 0x45 | PacketInGamePurchasePreviewOrderGameCommerce | FUN_1431adf70 / FUN_143258f50 | 40 same | DIFF |
| 0x46 | QuickChatSendData | – | no sub switch decoded | same |
| 0x48 | LiveGamerPostItem | FUN_142be76b0 / FUN_142c812b0; FUN_143d92330 / FUN_143e54300; FUN_143d929a0 / FUN_143e54970 | 28 same | same |
| 0x4B | AddAcquaintance | – | no sub switch decoded | same |
| 0x4C | PacketClientServerShuttingDown | – | no sub switch decoded | same |
| 0x4D | FriendUpdateExtraInfos | FUN_143493360 / FUN_143543050 | 10 same | same |
| 0x4F |  | – | no sub switch decoded | same |
| 0x53 | PacketClientKickedFromServer | – | no sub switch decoded | same |
| 0x54 | PacketUpdateClientSessionData | – | no sub switch decoded | same |
| 0x59 | PacketWorldDisplayInfo | – | no sub switch decoded | same |
| 0x5A | PacketMOTD | – | no sub switch decoded | DIFF |
| 0x5C | PacketSetClientArea | – | no sub switch decoded | same |
| 0x5F | PacketWorldShutdownNotice | – | no sub switch decoded | same |
| 0x64 | PacketClientExitLaunchUrl | – | no sub switch decoded | same |
| 0x65 | PacketClientPendingKickFromServer | – | no sub switch decoded | same |
| 0x66 | Activity | FUN_142be76b0 / FUN_142c812b0; FUN_143d92330 / FUN_143e54300; FUN_143d929a0 / FUN_143e54970 | 28 same | same |
| 0x67 | JoinLobbyGame | FUN_142c43130 / FUN_142cdcbf0 | 9 same | same |
| 0x68 | LobbyGameDefinition | – | no sub switch decoded | same |
| 0x69 | PacketShowSystemMessage | – | no sub switch decoded | same |
| 0x6A | PacketPOIChangeMessage | – | no sub switch decoded | DIFF |
| 0x6D | FirstTimeEventTriggerRequest | – | no sub switch decoded | same |
| 0x6E | GetAllAvailableRequisitionItemsRequest | – | no sub switch decoded | same |
| 0x72 | IgnoreList | – | no sub switch decoded | same |
| 0x74 | KeyCodeRedemptionNotification | – | no sub switch decoded | same |
| 0x78 | PacketCommerceSessionResponse | – | no sub switch decoded | same |
| 0x7A |  | – | no sub switch decoded | same |
| 0x7F | PlayerUpdate | FUN_142c09c80 / FUN_142ca3a60 | 6 same | DIFF |
| 0x83 | GuildCreate | FUN_142e539f0 / FUN_142ef2780 | 17 same | DIFF |
| 0x84 | GuildDisband | FUN_142e539f0 / FUN_142ef2780 | 17 same | DIFF |
| 0x8A | EncounterMatchmaking | – | no sub switch decoded | same |
| 0x8D | RepeatingActivityClearRequest | – | no sub switch decoded | same |
| 0x8E | PacketClientGameSettings | – | no sub switch decoded | same |
| 0x8F | ActivityJoinError | – | no sub switch decoded | same |
| 0x91 | StopInspect | – | no sub switch decoded | DIFF |
| 0x92 | AchievementTrackerUpdate | FUN_142d92750 / FUN_142e30720 | 11 same | same |
| 0x94 | PlayerTitleRequestSelect | – | no sub switch decoded | same |
| 0x97 | BeginJobCustimzation | – | no sub switch decoded | same |
| 0x98 | PacketGeneratePortraitRequest | – | no sub switch decoded | same |
| 0x9A | ActionManager | – | no sub switch decoded | DIFF |
| 0x9B |  | FUN_142ec9700 / FUN_142f68330 | 8 same | same |
| 0x9C | Social | FUN_142ec9700 / FUN_142f68330 | 8 same | same |
| 0x9E | CoinStoreItemDefinitionsRequest | FUN_1434b8110 / FUN_143567bd0 | 10 same | DIFF |
| 0x9F | PacketInitializationParameters | – | no sub switch decoded | same |
| 0xA0 | Activity | – | no sub switch decoded | same |
| 0xA1 | PacketMountRequest | FUN_142e70630 / FUN_142f0f1c0 | 6 same | same |
| 0xA4 | PacketClientNotifyCoinSpinAvailable | – | no sub switch decoded | same |
| 0xA5 | PacketClientAreaTimer | – | no sub switch decoded | same |
| 0xA7 | Rating | – | no sub switch decoded | DIFF |
| 0xA8 | ClientActivityLaunch | FUN_14365b550 / FUN_1437168e0 | 12 same | same |
| 0xAA | PacketClientFlashTimer | – | no sub switch decoded | same |
| 0xAD | PacketInviteAndStartMiniGameInvite | – | no sub switch decoded | same |
| 0xAF | QuizRequest | – | no sub switch decoded | same |
| 0xB3 | PacketClientMembershipVipInfo | – | no sub switch decoded | DIFF |
| 0xB5 | Target | – | no sub switch decoded | same |
| 0xB6 |  | – | no sub switch decoded | same |
| 0xB7 | GuideStone | – | no sub switch decoded | same |
| 0xB8 | Raids | FUN_142d4fd80 / FUN_142dedf50 | 14 same | same |
| 0xB9 | VoiceLeaveChannel | – | no sub switch decoded | same |
| 0xBA | MeleeStabFinish | FUN_142c4dba0 / FUN_142ce7600 | 1 changed, 10 same | same |
| 0xBB | PunkBuster | – | no sub switch decoded | same |
| 0xBC | Skills | FUN_142eb7220 / FUN_142f55e50 | 8 same | same |
| 0xBD | Loadouts | FUN_142e687e0 / FUN_142f07370 | 7 same | same |
| 0xBE | Experience | – | no sub switch decoded | same |
| 0xBF | Vehicle | FUN_142ed5770 / FUN_142f743a0 | 27 same, 4 same-wire | same |
| 0xC0 | Grief | – | no sub switch decoded | same |
| 0xC2 | Factions | – | no sub switch decoded | same |
| 0xC3 | PacketSynchronization | – | no sub switch decoded | same |
| 0xC4 | Resources | – | no sub switch decoded | same |
| 0xC6 | Leaderboard | – | no sub switch decoded | same |
| 0xC9 | PlayerUpdate | – | no sub switch decoded | same |
| 0xCB | Equipment | – | no sub switch decoded | same |
| 0xCC | DefinitionFilters | – | no sub switch decoded | same |
| 0xCF | ThrustPadPlayerEntered | – | no sub switch decoded | same |
| 0xD1 | Missions | FUN_142e6c300 / FUN_142f0ae90 | 9 same | same |
| 0xD2 |  | – | no sub switch decoded | same |
| 0xD5 | DeployableActionResponse | – | no sub switch decoded | same |
| 0xDC | HudPackets | – | no sub switch decoded | DIFF |
| 0xDD | SetSpeechPack | – | no sub switch decoded | same |
| 0xDE | AcquireTimers | – | no sub switch decoded | same |
| 0xDF | PlayerUpdate | – | no sub switch decoded | same |
| 0xE0 | PacketLoginQueueStatus | – | no sub switch decoded | same |
| 0xE1 | PacketServerPopulationInfo | – | no sub switch decoded | same |
| 0xE3 | PlayerUpdateVehicleCollision | – | no sub switch decoded | DIFF |
| 0xE4 | PlayerUpdate | – | no sub switch decoded | same |
| 0xE5 | Currency | – | no sub switch decoded | same |
| 0xE6 | Items | FUN_142e62ed0 / FUN_142f01a60 | 7 same | same |
| 0xE7 | PlayerUpdate | – | no sub switch decoded | same |
| 0xE8 | PlayerUpdate | – | no sub switch decoded | same |
| 0xE9 | PacketClientSettings | – | no sub switch decoded | same |
| 0xEA | AreaDefinitionTransferShape | – | no sub switch decoded | same |
| 0xEC | TerrainCell | – | no sub switch decoded | DIFF |
| 0xED | Prop | – | no sub switch decoded | same |
| 0xEE | Projectiles | – | no sub switch decoded | same |
| 0xEF | PlacedEffectDebug | – | no sub switch decoded | same |
| 0xF0 | Template | FUN_1430624b0 / (new FUN_14310bbf0) | 1 unpaired handler | DIFF |
| 0xF2 | PacketZoneProximityFacialAnim | – | no sub switch decoded | same |
| 0xF4 | VoxelConstruction | FUN_1430b7bf0 / FUN_143162830 | 1 changed, 7 same, 1 same-wire | DIFF |
| 0xF5 | Palette | – | no sub switch decoded | same |
| 0xF7 | PacketCharacterSelectSessionResponse | – | no sub switch decoded | same |
| 0xF8 | Mail | FUN_1432c9770 / FUN_1433765a0 | 1 same, 8 same-wire | DIFF |
| 0xF9 | Claims | FUN_142d1ff50 / FUN_142dbecb0 | 1 changed, 17 same, 6 same-wire | same |
| 0xFA |  | – | no sub switch decoded | same |
| 0xFB | LootPinata | – | no sub switch decoded | same |
| 0xFC | PacketTeleportRequestLocations | FUN_142be76b0 / FUN_142c812b0 | 10 same | DIFF |
| 0xFD | PropUpdateInteractionState | – | no sub switch decoded | same |
| 0xFE | PacketPlayerStudioRegistered | FUN_142be76b0 / FUN_142c812b0; FUN_143d92330 / FUN_143e54300; FUN_143d929a0 / FUN_143e54970 | 28 same | same |
| 0xFF | Chest | – | no sub switch decoded | same |
| 0x100 | DebugDraw | – | no sub switch decoded | same |
| 0x103 | FogOfWarZoneDataReply | – | no sub switch decoded | same |
| 0x104 | SocialShare | – | no sub switch decoded | same |
| 0x105 | HeroicMovement | – | no sub switch decoded | same |
| 0x10A | ProceduralDataMonitor | – | no sub switch decoded | same |
| 0x10C | WorldEditingClear | FUN_142fe3ae0 / FUN_1430838d0; FUN_1433b9530 / FUN_143467570 | 5 changed, 1 only695, 11 same | DIFF |
| 0x10D | InstancedGameEndRequest | FUN_14343c3d0 / FUN_1434ee920 | 4 changed, 1 only589, 2 only695, 4 same, 5 same-wire | DIFF |
| 0x10E |  | – | no sub switch decoded | same |
| 0x111 | ObjectScripting | FUN_142ea87d0 / FUN_142f47400 | 1 changed, 9 same, 1 same-wire | DIFF |
| 0x112 | PacketClaimVendorSetPriceRequest | – | no sub switch decoded | DIFF |
| 0x115 | KnownProp | – | no sub switch decoded | DIFF |
| 0x116 | ArtData | – | no sub switch decoded | DIFF |
| 0x117 | Tool | – | no sub switch decoded | same |
| 0x119 | DebugAnimation | – | no sub switch decoded | DIFF |
| 0x11A |  | – | no sub switch decoded | same |
| 0x11B | Interaction | – | no sub switch decoded | same |
| 0x11C | CharacterCustomization | – | no sub switch decoded | same |

## 10. Full table, per family

Columns: sub-opcode 0.589 → 0.695 ("–" = not set by a compiled constant; see §2), direction, client class, the 0.589 / 0.695 reader (S→C) or send template / builder (C→S) compared, 0.695 status with the evidence, the emulator's handling in 0.589 numbering (file:line), the change needed for 0.695. "(id table)" rows: PlayerUpdate sub-opcodes named only in the client's id table (FUN_143419ce0 / FUN_1434c7940); "(sub 0x.., no class name)": PlayerUpdate sub-opcodes known only from the dispatcher case or the server enum. Families are headed by opcode and the name of their trace/families file. "§A…/§B…" in notes = packets-695.md.

### 0x1 Notifications / Skills admin (0x0001)

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | C→S | Notifications::ListNotificationsPacket | FUN_14332a300 FUN_143329cd0 / FUN_1433d7110 FUN_1433d6ae0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:153) | none |
| 0x2 → 0x2 | C→S | Notifications::AddNotificationPacket | FUN_143329b20 / FUN_1433d6930 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:153) | none |
| 0x3 → 0x3 | C→S | Notifications::UpdateNotificationPacket | FUN_143329e80 / FUN_1433d6c90 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:153) | none |

### 0x3 Preference

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | BasePreferencePacket | FUN_142bb4ef0 / FUN_142c4e700 | same | not sent | none |
| 0x1 → 0x1 | C→S | PreferencePacketClientXml | FUN_142bacea0 FUN_142be4a60 / FUN_142c46a20 FUN_142c7e650 | same | not handled | none |

### 0xD PacketClientFinishedLoading

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | C→S | PacketClientFinishedLoading | FUN_142ba98a0 / FUN_142c43420 | same | handled (Network/Handlers/MiscHandler.cs:295) | none |

### 0xF PacketSendSelfToClient

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | PacketSendSelfToClient | FUN_142bb1050 / FUN_142c4abc0 | body: trailing str bio (FUN_142c19bd0 vs SendSelfToClient_Read 142b80420; TASKS §0) | sent (Network/Message/Model/SendSelfToClient.cs:15) | per-build body |

### 0x12 PacketClientZoneCoordinateInfo

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | PacketClientZoneCoordinateInfo | FUN_142b8a3b0 / FUN_142c23b80 | same | sent (Network/Handlers/MiscHandler.cs:257) | none |

### 0x13 Chat

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | S→C | PacketChat | FUN_1432aed20 FUN_1432a9b80 / FUN_14335bc40 FUN_143356a90 | same | sent (Game/Chat.cs:148; Game/Chat.cs:161; Game/Chat.cs:194) | none |
| 0x2 → 0x2 | S→C | ChatPacketEnterArea | FUN_1432ab430 / FUN_143358340 | same | not sent | none |
| 0x3 → 0x3 | S→C | ChatPacketDebugChat | FUN_1432a9800 / FUN_1433566e0 | body: trailing bool (FUN_1433566e0 vs 1432a9800; §B8 — sub is 3, not 2) | not sent | (not implemented; 0.695 body differs) |
| 0x4 → 0x4 | S→C | ChatPacketFromStringId | FUN_1432a9950 / FUN_143356860 | same | sent (Game/Emotes.cs:188) | none |
| 0x5 → 0x5 | S→C | TellEchoPacket | FUN_1432aa700 / FUN_143357610 | same | sent (Game/Chat.cs:209) | none |
| 0x7 → 0x7 | S→C | PlayerPublicChatCreateRoomResponsePacket | FUN_1432a9e90 / FUN_143356da0 | same | not sent | none |
| 0x8 → 0x8 | S→C | PlayerPublicChatRoomInviteNotificationPacket | FUN_1432aa3c0 / FUN_1433572d0 | same | not sent | none |
| 0x9 → 0x9 | S→C | PlayerPublicChatRoomInfoNotificationPacket | FUN_1432aa2e0 / FUN_1433571f0 | same | sent (Game/ChatRooms.cs:111) | none |
| 0xa → 0xa | C→S | PlayerPublicChatJoinRoomRequestPacket | FUN_1432aaf70 / FUN_143357e80 | same | handled (Network/Handlers/ChatHandler.cs:22) | none |
| 0xb → 0xb | S→C | PlayerPublicChatJoinRoomResponsePacket | FUN_1432aa140 / FUN_143357050 | same | sent (Game/ChatRooms.cs:105) | none |
| 0xc → 0xc | C→S | PlayerPublicChatLeaveRoomRequestPacket | FUN_1432ab120 / FUN_143358030 | same | handled (Network/Handlers/ChatHandler.cs:22) | none |
| 0xd → 0xd | S→C | PlayerPublicChatLeaveRoomResponsePacket | FUN_1432aa210 / FUN_143357120 | same | sent (Game/ChatRooms.cs:135) | none |
| 0xf → 0xf | S→C | PlayerPublicChatDestroyRoomResponsePacket | FUN_1432a9f60 / FUN_143356e70 | same | not sent | none |
| 0x11 → 0x11 | S→C | PlayerPublicChatTransferOwnershipResponsePacket | FUN_1432aa4a0 / FUN_1433573b0 | same | sent (Game/ChatRooms.cs:34 RoomNameTooLong) | none |
| 0x13 → 0x13 | S→C | PlayerPublicChatInviteUserResponsePacket | FUN_1432aa030 / FUN_143356f40 | same | sent (Game/ChatRooms.cs:34 RoomNameInappropriate) | none |
| 0x14 → 0x14 | S→C | PlayerSendChatHistoryPacket | FUN_1432aa5a0 / FUN_1433574b0 | same | not sent | none |

### 0x14 PacketClientLogout

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x3 → 0x3 | C→S | PacketClientLogout | FUN_142ba9fd0 / FUN_142c43b50 | same | family handler, sub not handled (Network/Handlers/CharacterSelectHandler.cs:25) | none |

### 0x1A PacketTargetClientNotOnline

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | PacketTargetClientNotOnline | FUN_142bb13b0 / FUN_142c4af20 | same | not sent | none |

### 0x1B ContentManagement

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | S→C | PacketMonitorState | FUN_14315be00 / FUN_143206820 | same | not sent | none |

### 0x1E Command

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x2 → 0x2 | S→C | CommandPacketWhoReply | FUN_1432d6c80 / FUN_143383a70 | same | sent (runtime opcode_stats (trace/families; agent_packets/sent_s2c.tsv)) | none |
| 0x3 → 0x3 | S→C | CommandPacketShowDialog | FUN_1432d6700 / FUN_1433834f0 | same | sent (runtime opcode_stats (trace/families; agent_packets/sent_s2c.tsv)) | none |
| 0x4 → 0x4 | C→S | CommandPacketEndDialog | FUN_143274340 / FUN_1433211f0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x6 → 0x6 | C→S | PacketPlayerPlaySpeech | FUN_1432d8720 FUN_14314e4c0 / FUN_143385510 FUN_1431f8ee0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x7 → 0x7 | C→S | PacketDialogResponse | FUN_1432744e0 / FUN_143321390 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x8 → 0x8 | S→C | CommandPacketPlaySoundAtLocation | FUN_1432d60f0 / FUN_143382ee0 | same | sent (runtime opcode_stats (trace/families; agent_packets/sent_s2c.tsv)) | none |
| 0x9 → 0x9 | C→S | CommandPacketInteractRequest | FUN_142ba8340 / FUN_142c41ec0 | same | handled (Network/Handlers/InteractionHandler.cs:38) | none |
| 0xa → 0xa | C→S | CommandPacketInteractCancel | FUN_142ba81a0 / FUN_142c41d20 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0xc → 0xc | C→S | CommandPacketInteractionSelect | FUN_143322c70 / FUN_1433cfa50 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0xe → 0xe | S→C | CommandPacketStartFlashGame | FUN_1432e5b20 / FUN_143392910 | same | not sent | none |
| 0xf → 0xf | C→S | CommandPacketSetProfile | FUN_142ba8a30 / FUN_142c425b0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x10 → 0x10 | C→S | CommandPacketAddFriendRequest | FUN_142ba7e40 / FUN_142c419c0 | same | handled (Network/Handlers/InteractionHandler.cs:87) | none |
| 0x11 → 0x11 | C→S | CommandPacketAddSteamFriendRequest | FUN_142f3b7c0 / FUN_142fdb530 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x12 → 0x12 | C→S | CommandPacketRemoveFriendRequest | FUN_143523120 / FUN_1435d4aa0 | same | handled (Network/Handlers/InteractionHandler.cs:90) | none |
| 0x13 → 0x13 | S→C | CommandPacketConfirmFriendRequest | FUN_1432d55d0 / FUN_1433823c0 | same | not sent | none |
| 0x14 → 0x14 | C→S | CommandPacketConfirmFriendResponse | FUN_14332a070 / FUN_1433d6e80 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x15 → 0x15 | C→S | CommandPacketSetChatBubbleColor | FUN_142ba8890 FUN_1432d65f0 / FUN_142c42410 FUN_1433833e0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x16 → 0x16 | C→S | CommandPacketSelectPlayer | FUN_142ba86e0 / FUN_142c42260 | same | handled (Network/Handlers/InteractionHandler.cs:93) | none |
| 0x18 → 0x18 | C→S | CommandPacketFriendsPositionRequest | FUN_1432d0b60 FUN_1432e0440 / FUN_14337d950 FUN_14338d230 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x19 → 0x19 | S→C | CommandPacketMoveAndInteract | FUN_1432d5f60 / FUN_143382d50 | same | not sent | none |
| 0x1a → 0x1a | C→S | CommandPacketQuestAbandon | FUN_143512790 / FUN_1435c3d70 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x1c → 0x1c | C→S | CommandPacketActivateProfileFailed | FUN_142ba7ca0 / FUN_142c41820 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x1d → 0x1d | C→S | CommandPacketProfileDefineSlot | FUN_1432e07e0 / FUN_14338d5d0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x1e → 0x1e | S→C | CommandPacketPlayDialogEffect | FUN_1432d6030 / FUN_143382e20 | same | not sent | none |
| 0x20 → 0x20 | C→S | CommandPacketIgnoreRequest | FUN_1432e05e0 / FUN_14338d3d0 | same | handled (Network/Handlers/InteractionHandler.cs:101) | none |
| 0x21 → 0x21 | C→S | CommandPacketSetActiveVehicleGuid | FUN_142f25a50 FUN_1432d6530 / FUN_142fc4d90 FUN_143383320 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x22 → 0x22 | C→S | CommandPacketChatChannelOn | FUN_143522bc0 / FUN_1435d4540 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x23 → 0x23 | C→S | CommandPacketChatChannelOff | FUN_143522a10 / FUN_1435d4390 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x24 → 0x24 | C→S | CommandPacketRequestPlayerPositions | FUN_143523470 / FUN_1435d4df0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x25 → 0x25 | S→C | CommandPacketRequestPlayerPositionsReply | FUN_1432d63c0 / FUN_1433831b0 | same | not sent | none |
| 0x27 → 0x27 | C→S | CommandPacketRequestRewardPreviewUpdate | FUN_142ba84f0 / FUN_142c42070 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x28 → 0x28 | S→C | CommandPacketRequestRewardPreviewUpdateReply | FUN_1432e58a0 / FUN_143392690 | same | not sent | none |
| 0x29 → 0x29 | S→C | CommandPacketPlaySoundIdOnTarget | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |
| 0x2a → 0x2a | C→S | CommandPacketRequestPlayIntroEncounter | FUN_1435232d0 / FUN_1435d4c50 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x2b → 0x2b | C→S | CommandPacketSpotPlayer | FUN_142ba8c10 / FUN_142c42790 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x2c → 0x2c | S→C | CommandPacketSpotPlayerReply | FUN_1432d6a80 / FUN_143383870 | same | not sent | none |
| 0x2d → 0x2d | C→S | CommandPacketSpotPrimaryTarget | FUN_142ba8e00 / FUN_142c42980 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x2e → 0x2e | C→S | CommandPacketInteractionString | FUN_1432d5bc0 FUN_143322e60 / FUN_1433829b0 FUN_1433cfc40 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x30 → 0x30 | S→C | CommandPacketFindReply | FUN_1432d57c0 / FUN_1433825b0 | same | sent (runtime opcode_stats (trace/families; agent_packets/sent_s2c.tsv)) | none |
| 0x33 → 0x33 | C→S | CommandPacketChargeCollision | FUN_143275cd0 / FUN_143322b80 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x34 → 0x34 | C→S | CommandPacketHexPermissions | FUN_142bac1a0 / FUN_142c45d20 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x37 → 0x37 | C→S | HelpRequestPacket | FUN_142bac740 FUN_1432d7be0 / FUN_142c462c0 FUN_1433849d0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x38 → 0x38 | C→S | HelpOfferPacket | FUN_142bac540 FUN_1432d7b00 / FUN_142c460c0 FUN_1433848f0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x39 → 0x39 | C→S | CommandPacketRedeployRequest | FUN_143385940 / FUN_143432810 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x3a → 0x3a | S→C | PacketPlayersInRadius | FUN_1432d88e0 / FUN_1433856d0 | same | not sent | none |
| 0x3b → 0x3b | C→S | CommandPacketAFKRequest | FUN_1432e0050 / FUN_14338ce40 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x3f → 0x3f | C→S | CommandPacketRendererDump | FUN_1432cf050 / FUN_14337be40 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x40 → 0x40 | C→S | CommandPacketNameChangeRequest | FUN_143522d70 / FUN_1435d46f0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x41 → 0x41 | C→S | CommandPacketNameValidationRequest | FUN_143522f60 / FUN_1435d48e0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x42 → 0x42 | C→S | CommandPacketGoHome | FUN_142ba7ff0 / FUN_142c41b70 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x43 → 0x43 | C→S | ClientCommandPacket | FUN_1432d0790 FUN_1432dfea0 / FUN_14337d580 FUN_14338cc90 | same | handled (Network/Handlers/InteractionHandler.cs:109) | none |
| 0x44 → 0x44 | S→C | CommandListPacket | FUN_1432d5410 / FUN_143382200 | same | sent (Game/Emotes.cs:105) | none |
| 0x45 → 0x45 | S→C | CommandPacketAddWorldCommand | FUN_1432e4df0 / FUN_143391be0 | same | not sent | none |
| 0x46 → 0x46 | S→C | CommandPacketAddZoneCommand | FUN_1432e4ef0 / FUN_143391ce0 | same | not sent | none |
| 0x47 → 0x47 | C→S | CommandPacketExecuteCommand | FUN_1432e0250 / FUN_14338d040 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x49 → 0x49 | C→S | CommandPacketItemDefinitionRequest | FUN_143355980 / FUN_143401a40 | same | handled (Network/Handlers/InteractionHandler.cs:112) | none |
| 0x4a → 0x4a | S→C | CommandPacketItemDefinitionReply | FUN_1432d5de0 / FUN_143382bd0 | same | not sent | none |
| 0x4b → 0x4b | S→C | CommandPacketItemDefinitions | FUN_1432d5ea0 / FUN_143382c90 | same | not sent | none |
| 0x4c → 0x4c | C→S | CommandPacketInteractionItem | FUN_143322ac0 / FUN_1433cf8a0 | same | handled (Network/Handlers/InteractionHandler.cs:117) | none |
| 0x4f → 0x4f | S→C | CommandPacketInteractionTool | FUN_1432d5c90 / FUN_143382a80 | same | not sent | none |
| 0x51 → 0x51 | C→S | CommandPacketTeleportToLocation | FUN_1432e5c90 FUN_1432e0d40 / FUN_143392a80 FUN_14338db30 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x52 → 0x52 | C→S | CommandPacketTeleportToLocationEx | FUN_1432e0f50 / FUN_14338dd40 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x53 → 0x53 | S→C | CommandPacketInteractionRunClientLua | FUN_1432d5aa0 / FUN_143382890 | same | sent (Game/ClientLua.cs:30) | none |
| 0x5a → 0x5a | C→S | CommandPacketBuilderMode | FUN_1433b4900 / FUN_143462900 | same | handled (Network/Handlers/InteractionHandler.cs:95) | none |
| 0x5b → 0x5b | C→S | CommandPacketRandom | FUN_1432e0980 FUN_1432d62b0 / FUN_14338d770 FUN_1433830a0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x3ea → 0x3ea | C→S | PacketAddItem | FUN_1432e2ad0 / FUN_14338f8c0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x3eb → 0x3eb | C→S | PacketDeleteItem | FUN_1432e3210 / FUN_143390000 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x3ec → 0x3ec | C→S | AdminCommandPacketProfileAdd | FUN_1432dde90 / FUN_14338ac80 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x3ec → 0x3ec | C→S | AdminCommandPacketProfileDelete | FUN_1432de0d0 / FUN_14338aec0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x3ec → 0x3ec | C→S | AdminCommandPacketProfileListAll | FUN_1432de310 / FUN_14338b100 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x3ec → 0x3ec | C→S | AdminCommandPacketProfileSelect | FUN_1432de500 / FUN_14338b2f0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x3ec → 0x3ec | C→S | AdminCommandPacketProfileSetRank | FUN_1432de740 / FUN_14338b530 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x3ec → 0x3ec | C→S | AdminCommandPacketProfileSetXp | FUN_1432de8e0 / FUN_14338b6d0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x3ec → 0x3ec | C→S | AdminCommandPacketProfileShowSelected | FUN_1432dea80 / FUN_14338b870 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x3ec → 0x3ec | C→S | AdminCommandPacketSetProfileGroup | FUN_1432df1e0 / FUN_14338bfd0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x3f6 → 0x3f6 | C→S | CommandPacketTeleportManagedToLocation | FUN_1432e0b20 / FUN_14338d910 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x40d → 0x40d | S→C | CommandPacketUITest | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |
| 0x41f → 0x41f | S→C | AdminCommandPacketAddCurrency | FUN_1432ed790 / FUN_14339a580 | same | not sent | none |
| 0x420 → 0x420 | S→C | AdminCommandPacketSetCurrency | FUN_1432edbe0 / FUN_14339a9d0 | same | not sent | none |
| 0x421 → 0x421 | S→C | AdminCommandPacketClearCurrency | FUN_1432ed800 / FUN_14339a5f0 | same | not sent | none |
| 0x423 → 0x423 | S→C | AdminCommandPacketListCurrencyRequest | FUN_1432eda70 / FUN_14339a860 | same | not sent | none |
| 0x429 → 0x429 | C→S | AdminCommandPacketGameTime | FUN_1432dd430 / FUN_14338a220 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x42a → 0x42a | C→S | AdminCommandPacketClientTime | FUN_1432dca00 FUN_1432d50f0 / FUN_1433897f0 FUN_143381ee0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x42e → 0x42e | C→S | AdminCommandPacketGotoRequest | FUN_1432dd5d0 / FUN_14338a3c0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x430 → 0x430 | S→C | AdminCommandPacketGotoWaypointRequest | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |
| 0x433 → 0x433 | C→S | PacketDeleteItemById | FUN_1432e3410 / FUN_143390200 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x434 → 0x434 | C→S | PacketGetItemList | FUN_1432e37a0 / FUN_143390590 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x445 → 0x445 | S→C | AdminCommandPacketZoneFlags | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |
| 0x458 → 0x458 | C→S | AdminCommandPacketHideMe | FUN_1432dd940 / FUN_14338a730 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x48a → 0x48a | C→S | PacketGetItemInfo | FUN_1432e35b0 / FUN_1433903a0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x499 → 0x499 | C→S | AdminCommandPacketAddAmmo | FUN_1432dc510 / FUN_143389300 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x49a → 0x49a | S→C | Skills::BaseSkillAdminPacket | FUN_142eab1f0 / FUN_142f49e20 | same | not sent | none |
| 0x49a → 0x49a | C→S | Skills::SkillAdminPacketAddSkillPoints | FUN_142eacd90 FUN_142eab1f0 / FUN_142f4b9c0 FUN_142f49e20 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x49a → 0x49a | C→S | Skills::SkillAdminPacketEcho | FUN_142eacf30 FUN_142eab1f0 / FUN_142f4bb60 FUN_142f49e20 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x49a → 0x49a | C→S | Skills::SkillAdminPacketGrantAllSkillLines | FUN_142ead0d0 FUN_142eab1f0 / FUN_142f4bd00 FUN_142f49e20 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x49a → 0x49a | C→S | Skills::SkillAdminPacketGrantSkill | FUN_142ead2b0 FUN_142eab1f0 / FUN_142f4bee0 FUN_142f49e20 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x49a → 0x49a | C→S | Skills::SkillAdminPacketGrantSkillLine | FUN_142ead450 FUN_142eab1f0 / FUN_142f4c080 FUN_142f49e20 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x49a → 0x49a | C→S | Skills::SkillAdminPacketGrantSkillSet | FUN_142ead5f0 FUN_142eab1f0 / FUN_142f4c220 FUN_142f49e20 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x49a → 0x49a | C→S | Skills::SkillAdminPacketListSkillPoints | FUN_142ead790 FUN_142eab1f0 / FUN_142f4c3c0 FUN_142f49e20 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x49a → 0x49a | C→S | Skills::SkillAdminPacketListSkills | FUN_142ead940 FUN_142eab1f0 / FUN_142f4c570 FUN_142f49e20 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x49a → 0x49a | C→S | Skills::SkillAdminPacketResetAllSkills | FUN_142eadaf0 FUN_142eab1f0 / FUN_142f4c720 FUN_142f49e20 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x49a → 0x49a | C→S | Skills::SkillAdminPacketResetSkill | FUN_142eadcd0 FUN_142eab1f0 / FUN_142f4c900 FUN_142f49e20 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x49a → 0x49a | C→S | Skills::SkillAdminPacketResetSkillLine | FUN_142eade70 FUN_142eab1f0 / FUN_142f4caa0 FUN_142f49e20 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x49a → 0x49a | C→S | Skills::SkillAdminPacketResetSkillPoints | FUN_142eae010 FUN_142eab1f0 / FUN_142f4cc40 FUN_142f49e20 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x49a → 0x49a | C→S | Skills::SkillAdminPacketResetSkillSet | FUN_142eae1f0 FUN_142eab1f0 / FUN_142f4ce20 FUN_142f49e20 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x49a → 0x49a | C→S | Skills::SkillAdminPacketSetSkillPointRateTierValue | FUN_142eae390 FUN_142eab1f0 / FUN_142f4cfc0 FUN_142f49e20 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x49a → 0x49a | C→S | Skills::SkillAdminPacketSpendSkillPoints | FUN_142eab1f0 / FUN_142f49e20 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x49c → 0x49c | C→S | AdminCommandPacketSpawnVehicle | – / – | same: builder: struct offsets only | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4a7 → 0x4a7 | C→S | AdminCommandPacketDebugPathQueryRequest | FUN_1436e2f30 / FUN_14379adb0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4a9 → 0x4a9 | C→S | AdminCommandPacketPathingDataMonitorUpdateRequest | FUN_143159be0 / FUN_143204600 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4ab → 0x4ab | C→S | AdminCommandPacketEnvServerDataMonitorUpdateRequest | FUN_143159a40 / FUN_143204460 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4b5 → 0x4b5 | C→S | AdminPacketVerifyAdminTarget | FUN_1432e4bd0 FUN_1432df990 / FUN_1433919c0 FUN_14338c780 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4cc → 0x4cc | C→S | AdminCommandPacketZonePlayerCount | FUN_1431722b0 / FUN_14321ccd0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4cf → 0x4cf | C→S | AdminCommandPacketForceAddFriendRequest | FUN_1432dcd40 / FUN_143389b30 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4d0 → 0x4d0 | C→S | AdminCommandPacketForceRemoveFriendRequest | FUN_1432dcef0 / FUN_143389ce0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4d1 → 0x4d1 | S→C | AdminCommandPacketFriendsListRequest | FUN_1432ed900 / FUN_14339a6f0 | same | not sent | none |
| 0x4d8 → 0x4d8 | C→S | AdminCommandPacketRunspeed | FUN_1432e4b20 FUN_1432dec70 / FUN_143391910 FUN_14338ba60 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4da → 0x4da | C→S | Grief::GriefAdminPacketResetGrief | FUN_142e41e70 / FUN_142ee0c00 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4da → 0x4da | C→S | Grief::GriefAdminPacketSetDamageLevel | FUN_142e42010 / FUN_142ee0da0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4da → 0x4da | C→S | Grief::GriefAdminPacketSetGriefLevel | FUN_142e421f0 / FUN_142ee0f80 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4de → 0x4de | C→S | AcquireTimers::AcquireTimerAdminPacketClearAcquireTimers | FUN_142d96600 / FUN_142e345d0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4de → 0x4de | C→S | AcquireTimers::AcquireTimerAdminPacketClearProfileAcquireTimers | FUN_142d967e0 / FUN_142e347b0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4de → 0x4de | C→S | AcquireTimers::AcquireTimerAdminPacketClearVehicleAcquireTimers | FUN_142d969c0 / FUN_142e34990 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4de → 0x4de | C→S | AcquireTimers::AcquireTimerAdminPacketListAcquireTimers | FUN_142d96ee0 / FUN_142e34eb0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4e7 → 0x4e7 | C→S | Items::ItemAdminPacketExpireItemLineTrialTimers | FUN_142e5ff40 / FUN_142efead0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4e7 → 0x4e7 | C→S | Items::ItemAdminPacketListItemLineTimers | FUN_142e600e0 / FUN_142efec70 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4e8 → 0x4e8 | C→S | Currency::CurrencyAdminPacketListCurrencyDiscounts | – / – | same: builder closure same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4e8 → 0x4e8 | C→S | Currency::CurrencyAdminPacketRequestSetCurrencyDiscount | FUN_142db10d0 / FUN_142e4f0a0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4ea → 0x4ea | S→C | AdminCommandPacketSpawnProp | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |
| 0x4ed → 0x4ed | C→S | AdminCommandPacketDeleteAllProps | FUN_1432dcba0 / FUN_143389990 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4ee → 0x4ee | C→S | AdminCommandPacketWeaponSet | FUN_1432df7d0 / FUN_14338c5c0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4ef → 0x4ef | C→S | AdminCommandPacketPlacedEffect | FUN_1432ddcd0 / FUN_14338aac0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4f0 → 0x4f0 | C→S | AdminCommandPacketSetModel | FUN_1432deff0 / FUN_14338bde0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4f1 → 0x4f1 | C→S | AdminCommandPacketFreeFlyHide | FUN_1432dd0a0 / FUN_143389e90 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4f2 → 0x4f2 | C→S | PacketVaultItemById | FUN_1432e42d0 / FUN_1433910c0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4f3 → 0x4f3 | C→S | PacketRandomItem | FUN_1432e3f30 / FUN_143390d20 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4f4 → 0x4f4 | C→S | PacketCollectionItemById | FUN_1432e3010 / FUN_14338fe00 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4fa → 0x4fa | C→S | PacketVaultCollectionItemById | FUN_1432e40d0 / FUN_143390ec0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4fc → 0x4fc | C→S | AdminCommandPacketStorybricksRequestDiagnostics | FUN_143159680 / FUN_1432040a0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4fd → 0x4fd | C→S | AdminCommandPacketStorybricksRequestDataErrors | FUN_14347a0d0 / FUN_14352bf70 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x4fe → 0x4fe | S→C | PacketWipeInventory | FUN_1432d1a60 / FUN_14337e850 | same | not sent | none |
| 0x4ff → 0x4ff | C→S | PacketOverflowItemById | FUN_1432e3b40 / FUN_143390930 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x500 → 0x500 | C→S | PacketBuybackItemById | FUN_1432e2c70 / FUN_14338fa60 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:33) | none |
| 0x507 → 0x507 | S→C | PacketItemFind | FUN_1432ef6c0 / FUN_14339c4b0 | same | not sent | none |

### 0x23 PacketClientBeginZoning

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | PacketClientBeginZoning | FUN_142b89b40 / FUN_142c23310 | same | sent (Network/Message/Model/ClientBeginZoning.cs:11) | none |

### 0x24 Combat

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x2 → 0x2 | C→S | CombatPacketAutoAttackTarget | FUN_14331c700 / FUN_1433c94f0 | same | not handled | none |
| 0x3 → 0x3 | C→S | CombatPacketAutoAttackOff | FUN_14331c560 / FUN_1433c9350 | same | not handled | none |
| 0x4 → 0x4 | C→S | CombatPacketSingleAttackTarget | FUN_14331c8b0 / FUN_1433c96a0 | same | not handled | none |
| 0x5 → 0x5 | S→C | CombatPacketAttackTargetDamage | FUN_14331be10 / FUN_1433c8c00 | same | not sent | none |
| 0x6 → 0x6 | S→C | CombatPacketAttackAttackerMissed | FUN_14331ba70 / FUN_1433c8860 | same | not sent | none |
| 0x7 → 0x7 | S→C | CombatPacketAttackTargetDodged | FUN_14331bf20 / FUN_1433c8d10 | same | not sent | none |
| 0x8 → 0x8 | S→C | CombatPacketAttackProcessed | FUN_14331bb30 / FUN_1433c8920 | same | not sent | none |
| 0xa → 0xa | S→C | CombatPacketEnableBossDisplay | FUN_14331c160 / FUN_1433c8f50 | same | sent (Game/Creatures.cs:1064) | none |
| 0xb → 0xb | S→C | CombatPacketAttackTargetBlocked | FUN_14331bd50 / FUN_1433c8b40 | same | not sent | none |
| 0xc → 0xc | S→C | CombatPacketAttackTargetParried | FUN_14331bfe0 / FUN_1433c8dd0 | same | not sent | none |
| 0xe → 0xe | S→C | CombatPacketBlockBroken | FUN_14331c0a0 / FUN_1433c8e90 | same | not sent | none |

### 0x26 PlayerUpdate

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → 0x80 | C→S (S→C read, ignored) | PlayerUpdatePacketSetPlayerBio | – / – | new | stub (reads nothing, answers 10 bytes) (Network/Handlers/MiscHandler.cs:510) | implement (0.695 only) |
| 0x1 → 0x1 | S→C | PlayerUpdatePacketRemovePlayer | FUN_142bb3650 / FUN_142c4cd60 | header: RemovePlayer_Read 142b90ef0 vs FUN_142c2a960: header only | sent (Network/Message/Model/PlayerUpdateRemovePlayer.cs:10) | u32 sub-opcode for 0.695 |
| 0x1 → 0x1 | S→C | PlayerUpdatePacketRemovePlayerGracefully | FUN_142bb36c0 / FUN_142c4cdd0 | header: unpaired | sent (Network/Message/Model/PlayerUpdateRemovePlayer.cs:10) | u32 sub-opcode for 0.695 |
| 0x2 → 0x2 | S→C | cPlayerUpdatePacketIdKnockback (id table) | – / – | header: dispatcher case 0x1 changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x3 → 0x3 | S→C | PlayerUpdatePacketUpdateHitpoints | FUN_142bb4450 / FUN_142c4dc60 | header | sent (Game/Creatures.cs:1179) | u32 sub-opcode for 0.695 |
| 0x4 → 0x4 | S→C | cPlayerUpdatePacketIdSetAnimation (id table) | – / – | header: dispatcher case 0x3 same; readers: inline | not sent | (not implemented; u32 sub in 0.695) |
| 0x5 → 0x5 | S→C | cPlayerUpdatePacketIdAddNotifications (id table) | – / – | header: dispatcher case 0x4 changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x6 → 0x6 | S→C | cPlayerUpdatePacketIdRemoveNotifications (id table) | – / – | header: dispatcher case 0x5 changed; checked by hand (coverage-695.md §3) | not sent | (not implemented; u32 sub in 0.695) |
| 0x7 → 0x7 | S→C | cPlayerUpdatePacketIdNpcRelevance (id table) | – / – | header: dispatcher case 0x6 changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x8 → 0x8 | S→C | PlayerUpdatePacketUpdateScale | FUN_142bb4770 / FUN_142c4df80 | header | not sent | (not implemented; u32 sub in 0.695) |
| 0x9 → 0x9 | S→C | PlayerUpdatePacketUpdateTemporaryAppearance | FUN_142bb4970 / FUN_142c4e180 FUN_142eca100 | header | not sent | (not implemented; u32 sub in 0.695) |
| 0xa → 0xa | C→S | PlayerUpdatePacketRemoveTemporaryAppearance | FUN_142bb3730 FUN_1432e46b0 / FUN_142c4ce40 FUN_1433914a0 | header | handled (Network/Handlers/PlayerHandler.cs:46) | u32 sub-opcode for 0.695 |
| 0xb → 0xb | S→C | cPlayerUpdatePacketIdPlayCompositeEffect (id table) | – / – | header: dispatcher case 0xa changed; readers: header-only | sent (Game/HitEffects.cs:106; Game/HitEffects.cs:131; Game/HitEffects.cs:95) | u32 sub-opcode for 0.695 |
| 0xc → 0xc | S→C | PlayerUpdatePacketStopCompositeEffect | FUN_142bb4220 / FUN_142c4da30 | header | not sent | (not implemented; u32 sub in 0.695) |
| 0xd → 0xd | S→C | cPlayerUpdatePacketIdSetLookAt (id table) | – / – | header: dispatcher case 0xc changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0xe → 0xe | S→C | cPlayerUpdatePacketIdRenamePlayer (id table) | – / – | header: dispatcher case 0xd changed; readers: same | not sent | (not implemented; u32 sub in 0.695) |
| 0xf → 0xf | S→C | cPlayerUpdatePacketIdUpdateCharacterState (id table) | – / – | header: dispatcher case 0xe changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x10 → 0x10 | S→C | cPlayerUpdatePacketIdQueueAnimation (id table) | – / – | header: dispatcher case 0xf same; readers: inline | not sent | (not implemented; u32 sub in 0.695) |
| 0x11 → 0x11 | S→C | PlayerUpdatePacketExpectedSpeed | FUN_142bb25d0 / FUN_142c4c030 | header | not sent | (not implemented; u32 sub in 0.695) |
| 0x12 → 0x12 | S→C | cPlayerUpdatePacketIdScriptedAnimation (id table) | – / – | header: dispatcher case 0x11 same; readers: inline | not sent | (not implemented; u32 sub in 0.695) |
| 0x13 → 0x13 | S→C | cPlayerUpdatePacketIdThoughtBubble (id table) | – / – | header: dispatcher case 0x12 changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x14 → 0x14 | S→C | cPlayerUpdatePacketIdSetDisposition (id table) | – / – | header: dispatcher case 0x13 changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x15 → 0x15 | S→C | cPlayerUpdatePacketLootEvent (id table) | – / – | header: dispatcher case 0x14 changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x16 → 0x16 | S→C | PlayerUpdatePacketSlotCompositeEffectOverride | FUN_142bb61d0 / FUN_142c4f9d0 | header | not sent | (not implemented; u32 sub in 0.695) |
| 0x17 → 0x17 | S→C | cPlayerUpdatePacketIdFreeze (id table) | – / – | header: dispatcher case 0x16 changed; readers: same | not sent | (not implemented; u32 sub in 0.695) |
| 0x18 → 0x18 | C→S | PlayerUpdatePacketRequestStripEffect | FUN_142baccc0 / FUN_142c46840 | header | handled (Network/Handlers/PlayerHandler.cs:26) | u32 sub-opcode for 0.695 |
| 0x19 → 0x19 | S→C | cPlayerUpdatePacketIdEffectPackage (id table) | – / – | header: dispatcher case 0x18 changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x1a → 0x1a | C→S | PlayerUpdatePacketPreferredLanguages | – / – | header: only the PlayerUpdate header writer differs | handled (Network/Handlers/PlayerHandler.cs:34) | u32 sub-opcode for 0.695 |
| 0x1b → 0x1b | S→C | PlayerUpdatePacketCustomizationChange | FUN_142bb1f10 / FUN_142c4b980 | body: bool moved before customization data, + u32 n × CharacterAttachmentData (FUN_142c27ad0 vs 142b8e270; §B3) | not sent | (not implemented; 0.695 body differs) |
| 0x1c → 0x1c | S→C | PlayerUpdatePacketPlayerTitle | FUN_142bb2ff0 / FUN_142c4c860 | header: pkt layouts equal after the header | sent (Game/PlayerManager.cs:133; Game/Titles.cs:151) | u32 sub-opcode for 0.695 |
| 0x1d → 0x1d | S→C | (sub 0x1d, no class name) | – / – | header: dispatcher case 0x1c changed; no id-table name | not sent | (not implemented; u32 sub in 0.695) |
| 0x1e → 0x1e | S→C | (sub 0x1e, no class name) | – / – | header: dispatcher case 0x1d changed; no id-table name | not sent | (not implemented; u32 sub in 0.695) |
| 0x1f → 0x1f | S→C | (sub 0x1f, no class name) | – / – | header: dispatcher case 0x1e changed; no id-table name | sent (Network/Handlers/MiscHandler.cs:233) | u32 sub-opcode for 0.695 |
| 0x20 → 0x20 | S→C | cPlayerUpdatePacketSetSpawnAnimation (id table) | – / – | header: dispatcher case 0x1f same; readers: inline | not sent | (not implemented; u32 sub in 0.695) |
| 0x21 → 0x21 | S→C | cPlayerUpdatePacketIdCustomizeNpc (id table) | – / – | header: dispatcher case 0x20 changed; readers: same | not sent | (not implemented; u32 sub in 0.695) |
| 0x22 → 0x22 | S→C | PlayerUpdatePacketSetSpawnerActivationEffect | – / – | header: pkt layouts equal after the header | not sent | (not implemented; u32 sub in 0.695) |
| 0x23 → 0x23 | S→C | cPlayerUpdatePacketIdSetComboState (id table) | – / – | header: dispatcher case 0x22 changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x24 → 0x24 | S→C | cPlayerUpdatePacketIdSetSurpriseState (id table) | – / – | header: dispatcher case 0x23 changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x25 → 0x25 | S→C | cPlayerUpdatePacketIdRemoveNpcCustomization (id table) | – / – | header: dispatcher case 0x24 changed; readers: same | not sent | (not implemented; u32 sub in 0.695) |
| 0x26 → 0x26 | S→C | cPlayerUpdatePacketIdReplaceBaseModel (id table) | – / – | header: dispatcher case 0x25 changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x27 → 0x27 | S→C | cPlayerUpdatePacketIdSetCollidable (id table) | – / – | header: dispatcher case 0x26 changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x28 → 0x28 | S→C | cPlayerUpdatePacketUpdateOwner (id table) | – / – | header: dispatcher case 0x27 changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x29 → 0x29 | S→C | cPlayerUpdatePacketUpdateTintAlias (id table) | – / – | header: dispatcher case 0x28 changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x2a → 0x2a | S→C | cPlayerUpdatePacketIdMoveOnRail (id table) | – / – | header: dispatcher case 0x29 changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x2b → 0x2b | S→C | cPlayerUpdatePacketIdClearMovementRail (id table) | – / – | header: dispatcher case 0x2a changed; readers: same | not sent | (not implemented; u32 sub in 0.695) |
| 0x2c → 0x2c | S→C | cPlayerUpdatePacketIdMoveOnRelativeRail (id table) | – / – | header: dispatcher case 0x2b changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x2d → 0x2d | S→C | cPlayerUpdatePacketIdDestroyed (id table) | – / – | header: dispatcher case 0x2c changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x2e → 0x2e | S→C | cPlayerUpdatePacketIdSeekTarget (id table) | – / – | header: dispatcher case 0x2d changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x2f → 0x2f | S→C | cPlayerUpdatePacketIdSeekTargetUpdate (id table) | – / – | header: dispatcher case 0x2e changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x30 → 0x30 | S→C | cPlayerUpdatePacketIdUpdateActiveWieldType (id table) | – / – | header: dispatcher case 0x2f same; readers: inline | not sent | (not implemented; u32 sub in 0.695) |
| 0x31 → 0x31 | S→C | PlayerUpdateLaunchProjectilePacket | FUN_142bb5ae0 / FUN_142c4f2f0 | header | not sent | (not implemented; u32 sub in 0.695) |
| 0x32 → 0x32 | S→C | (sub 0x32, no class name) | – / – | header: dispatcher case 0x31 same; no id-table name | not sent | (not implemented; u32 sub in 0.695) |
| 0x33 → 0x33 | S→C | cPlayerUpdatePacketHudMessage (id table) | – / – | header: dispatcher case 0x32 changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x34 → 0x34 | S→C | PlayerUpdatePacketCustomizationData | FUN_142bb1f80 / FUN_142c4b9f0 | header: BODY:142bb1f80->142c4b9f0 | not sent | (not implemented; u32 sub in 0.695) |
| 0x35 → 0x35 | S→C | cPlayerUpdatePacketIdMemberStatus (id table) | – / – | header: dispatcher case 0x34 changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x36 → 0x36 | S→C | cPlayerUpdatePacketIdSetCurrentAdventure (id table) | – / – | header: dispatcher case 0x35 changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x37 → 0x37 | S→C | PlayerUpdatePacketStartHarvest | FUN_142bb4010 / FUN_142c4d820 | header | not sent | (not implemented; u32 sub in 0.695) |
| 0x38 → 0x38 | S→C | PlayerUpdatePacketStopHarvest | FUN_142bb4290 / FUN_142c4daa0 | header | not sent | (not implemented; u32 sub in 0.695) |
| 0x39 → 0x39 | S→C | PlayerUpdatePacketKnockedOut | FUN_142bb2990 FUN_142b8efc0 / FUN_142c4c2f0 FUN_142c28930 | header | sent (Game/Health.cs:150) | u32 sub-opcode for 0.695 |
| 0x3a → 0x3a | S→C | PlayerUpdatePacketKnockedOutDamageReport | FUN_142bb2a00 / FUN_142c4c360 | header: pkt layouts equal after the header | not sent | (not implemented; u32 sub in 0.695) |
| 0x3b → 0x3b | C→S | PlayerUpdatePacketRespawn | FUN_143385ea0 / FUN_143432d70 | header | handled (Network/Handlers/PlayerHandler.cs:18; Network/Message/Model/PlayerUpdateRespawn.cs:10) | u32 sub-opcode for 0.695 |
| 0x3c → 0x3c | S→C | cPlayerUpdatePacketIdRespawnReply (id table) | – / – | header: dispatcher case 0x3b changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x3d → 0x3d | C→S | PlayerUpdatePacketReadyToReviveResponse | FUN_143385cb0 / FUN_143432b80 | header | handled (Network/Handlers/PlayerHandler.cs:67; Network/Message/Model/PlayerUpdateReadyToReviveResponse.cs:11) | u32 sub-opcode for 0.695 |
| 0x3e → 0x3e | S→C | cPlayerUpdatePacketIdActivateProfile (id table) | – / – | header: dispatcher case 0x3d changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x3f → 0x3f | S→C | cPlayerUpdatePacketIdSetSpotted (id table) | – / – | header: dispatcher case 0x3e changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x40 → 0x40 | S→C | cPlayerUpdatePacketIdJet (id table) | – / – | header: dispatcher case 0x3f changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x41 → 0x41 | S→C | cPlayerUpdatePacketIdTurbo (id table) | – / – | header: dispatcher case 0x40 changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x42 → 0x42 | S→C | cPlayerUpdatePacketIdStartRevive (id table) | – / – | header: dispatcher case 0x41 changed; readers: header-only | sent (Game/Health.cs:306) | u32 sub-opcode for 0.695 |
| 0x43 → 0x43 | S→C | cPlayerUpdatePacketIdStopRevive (id table) | – / – | header: dispatcher case 0x42 changed; readers: header-only | sent (Game/Health.cs:353) | u32 sub-opcode for 0.695 |
| 0x44 → 0x44 | S→C | cPlayerUpdatePacketIdReadyToRevive (id table) | – / – | header: dispatcher case 0x43 changed; readers: header-only | sent (Game/Health.cs:365) | u32 sub-opcode for 0.695 |
| 0x45 → 0x45 | S→C | cPlayerUpdatePacketIdSetFaction (id table) | – / – | header: dispatcher case 0x44 changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x46 → 0x46 | S→C | cPlayerUpdatePacketIdSetBattleRank (id table) | – / – | header: dispatcher case 0x45 changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x47 → 0x47 | S→C | cPlayerUpdatePacketIdStartHeal (id table) | – / – | header: dispatcher case 0x46 changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x48 → 0x48 | S→C | cPlayerUpdatePacketIdStopHeal (id table) | – / – | header: dispatcher case 0x47 changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x49 → 0x49 | S→C | PacketPlayerCurrencyChange | FUN_142bb0d50 / FUN_142c4a8c0 | header: BODY:142bb0d50->142c4a8c0 | sent (Network/WorldSession.cs:491) | u32 sub-opcode for 0.695 |
| 0x4a → 0x4a | S→C | cPlayerRewardCurrency (id table) | – / – | header: dispatcher case 0x49 changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x4b → 0x4b | S→C | cPlayerInGamePurchaseResult (id table) | – / – | header: dispatcher case 0x4a changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x4c → 0x4c | C→S | PlayerUpdatePacketSafeEject | FUN_142de7090 / FUN_142e85070 | header | not handled | (not implemented; u32 sub in 0.695) |
| 0x4d → 0x4d | S→C | PlayerUpdatePacketManagedObject | FUN_142b8ffa0 / FUN_142c29910 | header | not sent | (not implemented; u32 sub in 0.695) |
| 0x4e → 0x4e | S→C | cPlayerUpdatePacketIdMaterialTypeOverride (id table) | – / – | header: dispatcher case 0x4d changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x4f → 0x4f | S→C | cPlayerUpdateDebrisLaunchPacket (id table) | – / – | header: dispatcher case 0x4e changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x50 → 0x50 | S→C | PlayerUpdatePacketHideCorpse | FUN_142bb2740 / FUN_142c4c1a0 | header: pkt layouts equal after the header | not sent | (not implemented; u32 sub in 0.695) |
| 0x51 → 0x51 | S→C | (sub 0x51, no class name) | – / – | header: dispatcher case 0x50 changed; no id-table name | not sent | (not implemented; u32 sub in 0.695) |
| 0x52 → 0x52 | S→C | (sub 0x52, no class name) | – / – | header: dispatcher case 0x51 changed; no id-table name | not sent | (not implemented; u32 sub in 0.695) |
| 0x53 → 0x53 | S→C | PlayerUpdatePacketUpdateStat | FUN_142b93270 / FUN_142c2cd60 | header | not sent | (not implemented; u32 sub in 0.695) |
| 0x54 → 0x54 | S→C | cPlayerUpdatePacketIdAnimationRequest (id table) | – / – | header: dispatcher case 0x53 changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x55 → 0x55 | S→C | cPlayerUpdateNonPriorityCharacters (id table) | – / – | header: dispatcher case 0x54 changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x56 → 0x56 | S→C | cPlayerUpdatePlayWorldCompositeEffect (id table) | – / – | header: dispatcher case 0x55 changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x57 → 0x57 | S→C | cPlayerUpdatePacketIdAFK (id table) | – / – | header: dispatcher case 0x56 changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x58 → 0x58 | S→C | cPlayerUpdatePacketIdAddLightweightPc (id table) | – / – | header: dispatcher case 0x57 changed; readers: inline | sent (Network/Message/Model/PlayerUpdateAddLightweightPc.cs:13) | u32 sub-opcode for 0.695 |
| 0x59 → 0x59 | S→C | PlayerUpdatePacketAddLightweightNpc | FUN_142b8cfc0 / FUN_142c26790 | header | sent (Network/Message/Model/PlayerUpdateAddLightweightNpc.cs:11) | u32 sub-opcode for 0.695 |
| 0x5a → 0x5a | S→C | cPlayerUpdatePacketIdAddLightweightVehicle (id table) | – / – | header: dispatcher case 0x59 same; readers: inline | not sent | (not implemented; u32 sub in 0.695) |
| 0x5b → 0x5b | S→C | PlayerUpdatePacketLightweightToFullPc | – / FUN_142c294b0 | header: LightweightToFullPc_Read 142b8fb40 vs FUN_142c294b0: header only; FullPcData_Read same | sent (Network/Message/Model/PlayerUpdateLightweightToFullPc.cs:18) | u32 sub-opcode for 0.695 |
| 0x5c → 0x5c | S→C | PlayerUpdatePacketLightweightToFullNpc | FUN_142b8f230 / FUN_142c4c3d0 | header: pkt layouts equal after the header | not sent | (not implemented; u32 sub in 0.695) |
| 0x5d → 0x5d | S→C | (sub 0x5d, no class name) | – / – | header: dispatcher case 0x5c changed; no id-table name | not sent | (not implemented; u32 sub in 0.695) |
| 0x5e → 0x5e | C→S | PlayerUpdatePacketFullCharacterDataRequest | FUN_142de6ee0 / FUN_142e84ec0 | header | handled (Network/Handlers/PlayerHandler.cs:76; Network/Message/Model/PlayerUpdateFullCharacterDataRequest.cs:10) | u32 sub-opcode for 0.695 |
| 0x5f → 0x5f | S→C | cPlayerInitiateNameChange (id table) | – / – | header: dispatcher case 0x5e changed; readers: same | not sent | (not implemented; u32 sub in 0.695) |
| 0x60 → 0x60 | S→C | cPlayerNameChangeResult (id table) | – / – | header: dispatcher case 0x5f changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x61 → 0x61 | C→S / unused | cPlayerNameValidationResult (id table) | – / – | header: no S→C dispatcher case (C→S or unused) | not handled | (not implemented; u32 sub in 0.695) |
| 0x62 → 0x62 | S→C | cPlayerUpdatePacketIdAddLightweightProp (id table) | – / – | header: dispatcher case 0x61 same; readers: inline | sent (Network/Message/Model/PlayerUpdateAddLightweightProp.cs:13) | u32 sub-opcode for 0.695 |
| 0x63 → 0x63 | S→C | cPlayerUpdatePacketIdLightweightToFullProp (id table) | – / – | header: dispatcher case 0x62 same; readers: inline | sent (Network/Message/Model/PlayerUpdateLightweightToFullProp.cs:12) | u32 sub-opcode for 0.695 |
| 0x64 → 0x64 | S→C | cPacketIdPlayerUpdateSourceGeometryOffset (id table) | – / – | header: dispatcher case 0x63 changed; readers: header-only | not sent | (not implemented; u32 sub in 0.695) |
| 0x65 → 0x65 | S→C | PlayerUpdatePacketRequestAnimationSelection | FUN_142bb3940 / FUN_142c4d050 | header: pkt layouts equal after the header | not sent | (not implemented; u32 sub in 0.695) |
| 0x66 → 0x66 | S→C | NpcCharacterPersistStateUpdatePacket | FUN_142bb0000 / FUN_142c49b70 | header | not sent | (not implemented; u32 sub in 0.695) |
| 0x67 → 0x67 | S→C | PlayerUpdatePacketCombatResourceEvent | FUN_142bb1ea0 / FUN_142c4b910 | header | sent (Game/Creatures.cs:1170) | u32 sub-opcode for 0.695 |
| 0x68 → 0x68 | C→S | PlayerUpdatePacketUpdateSteamInfo | FUN_142f3b970 / FUN_142fdb6e0 | header | not handled | (not implemented; u32 sub in 0.695) |
| 0x69 → 0x69 | C→S / unused | cPlayerUpdatePacketInteractionInfoRequest (id table) | – / – | header: no S→C dispatcher case (C→S or unused) | handled (Network/Handlers/InteractionHandler.cs:23; Network/Message/Model/InteractionMessages.cs:72) | u32 sub-opcode for 0.695 |
| 0x6a → 0x6a | S→C | cPlayerUpdatePacketInteractionInfoReply (id table) | – / – | header: reached through the proxied-packet handler 0.589 FUN_142cbb870 / 0.695 FUN_142d54c10; reader FUN_142bb27b0 / FUN_142c285e0: equal after the header | sent (Network/Message/Model/InteractionMessages.cs:88) | u32 sub-opcode for 0.695 |
| 0x6b → 0x6b | S→C | (sub 0x6b, no class name) | – / – | header: proxied-packet handler 0.589 FUN_142cbb870 / 0.695 FUN_142d54c10, reader FUN_142bb2440 / FUN_142c4beb0: equal after the header | sent (Network/Message/Model/InteractionMessages.cs:53) | u32 sub-opcode for 0.695 |
| 0x6c → 0x6c | C→S | PlayerUpdatePacketClientLoading | FUN_1435f8c30 / FUN_1436ab9b0 | header | handled (Network/Handlers/MiscHandler.cs:159) | u32 sub-opcode for 0.695 |
| 0x6d → 0x6d | C→S / unused | cPlayerUpdatePacketIdAddProxiedObject (id table) | – / – | header: no S→C dispatcher case (C→S or unused) | not handled | (not implemented; u32 sub in 0.695) |
| 0x6f → 0x6f | C→S / unused | cPlayerUpdatePacketIdDeploy (id table) | – / – | header: no S→C dispatcher case (C→S or unused) | not handled | (not implemented; u32 sub in 0.695) |
| 0x70 → 0x70 | C→S / unused | cPlayerUpdatePacketIdSetNpcGuildDecal (id table) | – / – | header: no S→C dispatcher case (C→S or unused) | not handled | (not implemented; u32 sub in 0.695) |
| 0x71 → 0x71 | C→S | PlayerUpdatePacketBindingCustomization | FUN_142eee6c0 / FUN_142f8d3b0 | header | handled (Network/Handlers/MiscHandler.cs:50; Network/Message/Model/PlayerUpdateBindingCustomization.cs:10) | u32 sub-opcode for 0.695 |
| 0x72 → 0x72 | S→C | PlayerUpdatePacketMobMovement | – / – | header: pkt layouts equal after the header | not sent | (not implemented; u32 sub in 0.695) |
| 0x73 → 0x73 | C→S | PlayerUpdatePacketSetEnabled | FUN_142b91bf0 FUN_143002130 / FUN_142c2b660 FUN_1430a7060 | header | handled (Network/Handlers/PlayerHandler.cs:55) | u32 sub-opcode for 0.695 |
| 0x74 → 0x74 | S→C | cPlayerUpdatePacketIdAddFeatherweightObject (id table) | – / – | header: dispatcher case 0x73 changed; readers: inline | not sent | (not implemented; u32 sub in 0.695) |
| 0x75 → 0x75 | S→C | PlayerUpdatePacketRemoveFeatherweightObject | FUN_142bb31b0 / FUN_142c4ca20 | header: pkt layouts equal after the header | not sent | (not implemented; u32 sub in 0.695) |
| 0x76 → 0x76 | S→C | (sub 0x76, no class name) | – / – | header: dispatcher case 0x75 changed; no id-table name | not sent | (not implemented; u32 sub in 0.695) |
| 0x77 → 0x77 | C→S | PlayerUpdatePacketEmoteStringById | FUN_1432e4860 / FUN_143391650 | header | handled (Network/Handlers/PlayerHandler.cs:59) | u32 sub-opcode for 0.695 |
| 0x78 → 0x78 | S→C | cPlayerUpdatePacketDoCombatNotification (id table) | – / – | header: dispatcher case 0x77 changed; readers: same | not sent | (not implemented; u32 sub in 0.695) |
| 0x79 → 0x79 | S→C | cPlayerUpdatePacketDisplayPropText (id table) | – / – | body: +u64 (+0x20), +u32 (+0x48), +bool (+0x4c) (FUN_142c27f40 vs 142b8e6c0; packets-695 §B2) | sent (Game/Props.cs:124; Game/Props.cs:96) | per-build body |
| 0x7a → 0x7a | S→C | cPlayerUpdatePacketIdSetPropTint (id table) | – / – | header: dispatcher case 0x79 changed; readers: same | sent (Game/Props.cs:1086; Game/Props.cs:938) | u32 sub-opcode for 0.695 |
| 0x7b → 0x7b | C→S / unused | cPlayerUpdatePacketIdUpdatePropText (id table) | – / – | header: no S→C dispatcher case (C→S or unused) | not handled | (not implemented; u32 sub in 0.695) |
| 0x7c → 0x7c | S→C | cPlayerUpdatePacketIdUpdatePropName (id table) | – / – | header: dispatcher case 0x7b changed; readers: same | sent (Game/Props.cs:1332; Game/Props.cs:994) | u32 sub-opcode for 0.695 |
| 0x7d → 0x7d | S→C | cPlayerUpdatePacketUpdateEditProperties (id table) | – / – | header: dispatcher case 0x7c changed; checked by hand (coverage-695.md §3) | not sent | (not implemented; u32 sub in 0.695) |
| 0x7e → 0x7e | S→C | cPlayerUpdatePacketDebugAILineOfSight (id table) | – / – | header: dispatcher case 0x7d changed; readers: same | not sent | (not implemented; u32 sub in 0.695) |
| 0x7f → 0x7f | S→C | (sub 0x7f, no class name) | – / – | header: dispatcher case 0x7e changed; no id-table name | not sent | (not implemented; u32 sub in 0.695) |

Id-table entries with no client class (no reader / writer compiled; server-to-server or unused): cPlayerUpdatePacketSetPlayerBio –→0x80

### 0x27 Abilities

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | Abilities::BacktimedAbilityPacket | FUN_142d65900 / FUN_142e03920 | same | not sent | none |
| 0x0 → 0x0 | S→C | Abilities::BaseAbilityPacket | FUN_142d79170 FUN_142bae390 / FUN_142e17140 FUN_142c47f00 | same | not sent | none |
| 0x1 → 0x1 | S→C | Abilities::AbilityPacketCastAttempt | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |
| 0x2 → 0x2 | C→S | Abilities::AbilityPacketCasterInfoRequest | – / – | same: builder closure same | handled (Network/Handlers/MiscHandler.cs:441; Network/Message/Model/AbilityCasterInfoRequest.cs:10) | none |
| 0x3 → 0x3 | S→C | Abilities::AbilityPacketCasterInfoReply | FUN_142d63690 / FUN_142e016b0 | same | sent (Network/Handlers/MiscHandler.cs:372; Network/Handlers/MiscHandler.cs:462; Network/Handlers/MiscHandler.cs:490) | none |
| 0x4 → 0x4 | S→C | Abilities::AbilityPacketAbilityStart | FUN_142d62de0 / FUN_142e00e00 | same | not sent | none |
| 0x5 → 0x5 | S→C | Abilities::AbilityPacketAbilityEnd | FUN_142d62d00 / FUN_142e00d20 | same | sent (Game/Abilities.cs:364) | none |
| 0x6 → 0x6 | S→C | Abilities::AbilityPacketCastFail | FUN_142d63390 / FUN_142e013b0 | same | sent (Network/Message/Model/AbilityMessages.cs:259) | none |
| 0x7 → 0x7 | S→C | Abilities::AbilityPacketCastSuccess | FUN_142d63510 / FUN_142e01530 | same | sent (Network/Message/Model/AbilityMessages.cs:288) | none |
| 0x8 → 0x8 | S→C | Abilities::AbilityPacketCastContinue | FUN_142d63190 / FUN_142e011b0 | same | not sent | none |
| 0x9 → 0x9 | S→C | Abilities::AbilityPacketTargeterStart | FUN_142d65540 / FUN_142e03560 | same | not sent | none |
| 0xa → 0xa | S→C | Abilities::AbilityPacketTargeterUpdate | FUN_142d656d0 / FUN_142e036f0 | same | not sent | none |
| 0xb → 0xb | S→C | Abilities::AbilityPacketTargeterEnd | FUN_142d65440 / FUN_142e03460 | same | not sent | none |
| 0xc → 0xc | S→C | Abilities::AbilityPacketEffectStart | FUN_142d64890 / FUN_142e028b0 | same | sent (Game/Buffs.cs:200) | none |
| 0xd → 0xd | S→C | Abilities::AbilityPacketEffectEnd | FUN_142d64420 / FUN_142e02440 | value: reader same; handler FUN_142e18c70 (0.589 142d7aca0) accepts new result 9 = ClaimDestruction.Fail.NotAllowed (Play Mode Rules voxel block) | sent (Game/Buffs.cs:235; Game/Gathering.cs:302; Game/Gathering.cs:335) | new value only (server may send it) |
| 0xe → 0xe | C→S | Abilities::AbilityPacketFinishChargedCast | FUN_142d66f90 / FUN_142e04fb0 | same | handled (Network/Handlers/MiscHandler.cs:427; Network/Message/Model/AbilityMessages.cs:211) | none |
| 0xf → 0xf | C→S | Abilities::AbilityPacketCancelCast | FUN_142d66890 / FUN_142e048b0 | same | handled (Network/Handlers/MiscHandler.cs:434; Network/Message/Model/AbilityMessages.cs:242) | none |
| 0x10 → 0x10 | S→C | Abilities::AbilityPacketAbilityStartFailed | FUN_142d63050 / FUN_142e01070 | same | not sent | none |
| 0x11 → 0x11 | S→C | Abilities::AbilityPacketAbilityStartCanceled | FUN_142d62f90 / FUN_142e00fb0 | same | not sent | none |
| 0x12 → 0x12 | S→C | Abilities::AbilityPacketPlayAnim | FUN_142d650a0 / FUN_142e030c0 | same | sent (Game/Creatures.cs:1208; Game/Emotes.cs:265) | none |
| 0x13 → 0x13 | C→S | Abilities::AbilityPacketDefaultInfoRequest | FUN_142d66df0 / FUN_142e04e10 | same | handled (Network/Handlers/MiscHandler.cs:519; Network/Message/Model/AbilityDefaultInfoRequest.cs:9) | none |
| 0x14 → 0x14 | S→C | Abilities::AbilityPacketIdDefaultInfoReply | FUN_142d67990 / FUN_142e059b0 | same | sent (Network/Handlers/MiscHandler.cs:522) | none |
| 0x16 → 0x16 | C→S | Abilities::AbilityPacketEffectUpdatesRequest | FUN_142f258b0 / FUN_142fc4bf0 | same | handled (Network/Handlers/MiscHandler.cs:56; Network/Message/Model/PlayerUpdateBindingCustomization.cs:23) | none |
| 0x17 → 0x17 | C→S | Abilities::AbilityPacketDelayedTargetInfo | FUN_1433a55c0 / FUN_143452720 | same | handled (Network/Handlers/MiscHandler.cs:66; Network/Message/Model/PlayerUpdateBindingCustomization.cs:50) | none |
| 0x18 → 0x18 | C→S | Abilities::AbilityPacketCollisionReport | FUN_142dc9660 / FUN_142e67590 | same | handled (Network/Handlers/MiscHandler.cs:37; Network/Message/Model/AbilityCollisionReport.cs:14) | none |
| 0x19 → 0x19 | S→C | Abilities::AbilityPacketPlayVfx | FUN_142d67b20 / FUN_142e05b40 | same | not sent | none |
| 0x1a → 0x1a | S→C | Abilities::AbilityPacketUpdateEffect | FUN_142d65800 / FUN_142e03820 | same | not sent | none |
| 0x1b → 0x1b | S→C | Abilities::AbilityPacketLowerCurrentCooldown | FUN_142d64fe0 / FUN_142e03000 | same | not sent | none |
| 0x1c → 0x1c | S→C | Abilities::AbilityPacketEffectInitialized | FUN_142d64620 / FUN_142e02640 | same | sent (Game/Buffs.cs:153) | none |
| 0x1e → 0x1e | C→S | Abilities::AbilityPacketRequestCancelEffect | FUN_142d67130 / FUN_142e05150 | same | handled (Network/Handlers/MiscHandler.cs:62; Network/Message/Model/PlayerUpdateBindingCustomization.cs:33) | none |
| 0x1f → 0x1f | S→C | Abilities::AbilityPacketClearCurrentCooldown | FUN_142d67720 / FUN_142e05740 | same | not sent | none |
| 0x21 → 0x21 | S→C | Abilities::AbilityPacketShapeStart | FUN_142d65310 / FUN_142e03330 | same | not sent | none |
| 0x22 → 0x22 | S→C | Abilities::AbilityPacketShapeEnd | FUN_142d65210 / FUN_142e03230 | same | not sent | none |
| 0x23 → 0x23 | C→S | Abilities::AbilityPacketClientTargeterEnd | FUN_142d64340 FUN_14349ca20 / FUN_142e02360 FUN_14354c520 | same | not handled | none |
| 0x24 → 0x24 | C→S | Abilities::AbilityPacketClientTargeterFindTarget | FUN_14349cc60 / FUN_14354c760 | same | not handled | none |

### 0x29 ClientUpdate

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | S→C | ClientUpdatePacketHitpoints | FUN_142b83000 / FUN_142c1c7c0 | same | sent (Game/Health.cs:239; Game/Health.cs:522; Game/Health.cs:559) | none |
| 0x2 → 0x2 | S→C | ClientUpdatePacketItemAdd | FUN_142b830c0 / FUN_142c1c880 | same | sent (Network/Message/Model/ClientUpdateItemAdd.cs:12) | none |
| 0x3 → 0x3 | S→C | ClientUpdatePacketItemUpdate | FUN_142b832a0 / FUN_142c1ca60 | same | sent (Network/Message/Model/ClientUpdateItemUpdate.cs:12) | none |
| 0x4 → 0x4 | S→C | ClientUpdatePacketItemDelete | FUN_142bb5350 / FUN_142c4eb60 | same | sent (Network/Message/Model/ClientUpdateItemDelete.cs:9) | none |
| 0x5 → 0x5 | S→C | ClientUpdatePacketItemDeleteFailed | FUN_142bb5410 / FUN_142c4ec20 | same | not sent | none |
| 0x6 → 0x6 | S→C | ClientUpdatePacketUpdateStat | FUN_142baf730 / FUN_142c492a0 | same | sent (runtime opcode_stats (trace/families; agent_packets/sent_s2c.tsv)) | none |
| 0x7 → 0x7 | S→C | ClientUpdatePacketCollectionStart | FUN_142b82c60 / FUN_142c1c420 | same | not sent | none |
| 0x8 → 0x8 | S→C | ClientUpdatePacketCollectionRemove | FUN_142baead0 / FUN_142c48640 | same | not sent | none |
| 0x9 → 0x9 | S→C | ClientUpdatePacketCollectionAddEntry | FUN_142b82950 / FUN_142c1c110 | same | not sent | none |
| 0xa → 0xa | S→C | ClientUpdatePacketCollectionRemoveEntry | FUN_142b82ba0 / FUN_142c1c360 | same | not sent | none |
| 0xb → 0xb | S→C | ClientUpdatePacketUpdateLocation | FUN_142b840a0 / FUN_142c1d860 | same | not sent | none |
| 0xc → 0xc | S→C | ClientUpdatePacketTeleportToLocation | FUN_142b83d70 / FUN_142c1d530 | same | sent (Game/Teleport.cs:77) | none |
| 0xd → 0xd | S→C | ClientUpdatePacketMana | FUN_142b834d0 / FUN_142c1cc90 | same | not sent | none |
| 0xf → 0xf | S→C | ClientUpdatePacketAddProfileAbilitySetApl | FUN_142b82800 / FUN_142c1bfc0 | same | not sent | none |
| 0x10 → 0x10 | S→C | ClientUpdatePacketAddEffectTag | FUN_142b82710 / FUN_142c1bed0 | same | not sent | none |
| 0x11 → 0x11 | S→C | ClientUpdatePacketRemoveEffectTag | FUN_142b83a20 / FUN_142c1d1e0 | same | not sent | none |
| 0x13 → 0x13 | S→C | ClientUpdatePacketCoinCount | FUN_142bae9b0 / FUN_142c48520 | same | sent (Network/WorldSession.cs:510) | none |
| 0x14 → 0x14 | S→C | ClientUpdatePacketDeleteProfile | FUN_142baec60 / FUN_142c487d0 | same | not sent | none |
| 0x15 → 0x15 | S→C | ClientUpdatePacketActivateProfile | FUN_142b82450 / FUN_142c1bc10 | same | not sent | none |
| 0x16 → 0x16 | S→C | ClientUpdatePacketAddAbility | FUN_142b82600 / FUN_142c1bdc0 | same | not sent | none |
| 0x17 → 0x17 | S→C | ClientUpdatePacketNotifyPlayer | FUN_142baf020 / FUN_142c48b90 | same | not sent | none |
| 0x18 → 0x18 | S→C | ClientUpdatePacketUpdateProfileAbilitySetApl | FUN_142b84340 / FUN_142c1db00 | same | not sent | none |
| 0x1b → 0x1b | S→C | ClientUpdatePacketDoneSendingPreloadCharacters | FUN_142baed10 / FUN_142c48880 | same | sent (Network/Message/Model/ClientUpdateDoneSendingPreloadCharacters.cs:10) | none |
| 0x1d → 0x1d | S→C | ClientUpdatePacketPhaseChange | FUN_142baf120 / FUN_142c48c90 | same | not sent | none |
| 0x1e → 0x1e | S→C | ClientUpdatePacketDamageInfo | FUN_142b82d20 / FUN_142c1c4e0 | same | not sent | none |
| 0x1f → 0x1f | S→C | ClientUpdatePacketZonePopulation | FUN_142baf8a0 / FUN_142c49410 | same | sent (Network/Message/Model/ClientUpdateZonePopulation.cs:10) | none |
| 0x20 → 0x20 | S→C | ClientUpdatePacketRespawnLocations | FUN_142b83ae0 / FUN_142c1d2a0 | same | sent (Network/Message/Model/ClientUpdateRespawnLocations.cs:15) | none |
| 0x21 → 0x21 | S→C | ClientUpdatePacketModifyMovementSpeed | FUN_142b83650 / FUN_142c1ce10 | same | not sent | none |
| 0x22 → 0x22 | S→C | ClientUpdatePacketModifyTurnRate | FUN_142b837d0 / FUN_142c1cf90 | same | not sent | none |
| 0x23 → 0x23 | S→C | ClientUpdatePacketModifyStrafeSpeed | FUN_142b83710 / FUN_142c1ced0 | same | not sent | none |
| 0x24 → 0x24 | S→C | ClientUpdatePacketUpdateManagedLocation | FUN_142b841c0 / FUN_142c1d980 | same | not sent | none |
| 0x25 → 0x25 | S→C | ClientUpdatePacketScreenEffect | FUN_142b83bd0 / FUN_142c1d390 | same | not sent | none |
| 0x26 → 0x26 | S→C | ClientUpdatePacketMovementVersion | FUN_142baef80 / FUN_142c48af0 | same | sent (Network/Handlers/MiscHandler.cs:259) | none |
| 0x27 → 0x27 | S→C | ClientUpdatePacketManagedMovementVersion | FUN_142b83590 / FUN_142c1cd50 | same | not sent | none |
| 0x28 → 0x28 | S→C | ClientUpdatePacketUpdateWeaponAddClips | FUN_142f25160 / FUN_142fc4420 | same | not sent | none |
| 0x2b → 0x2b | S→C | ClientLoyaltyPointsUpdatePacket | FUN_142bb5230 / FUN_142c4ea40 | same | not sent | none |
| 0x2c → 0x2c | S→C | ClientUpdatePacketUpdateCalculatedStats | FUN_142dbf9c0 FUN_142b83ff0 / FUN_142c49070 FUN_142c1d7b0 | same: UpdateCalculatedStats_Read 142baf500 = FUN_142c49070 | sent (Network/Message/Model/ClientUpdateUpdateCalculatedStats.cs:11) | none |
| 0x2e → 0x2e | S→C | ClientUpdatePacketUpdateAttributes | FUN_142baf330 / FUN_142c48ea0 | same | not sent | none |

Id-table entries with no client class (no reader / writer compiled; server-to-server or unused): cClientUpdatePacketIdCollectionItem 0x2d→0x2d; cClientUpdatePacketIdContainerItem 0x2a→0x2a; cClientUpdatePacketIdRemoveActionBars 0x19→0x19; cClientUpdatePacketIdUpdateActionBarSlot 0x1a→0x1a; cClientUpdatePacketIdUpdateProfileExperience 0xe→0xe; cClientUpdatePacketIdUpdateProfileRank 0x12→0x12; cClientUpdatePacketIdVaultItem 0x29→0x29

### 0x2A MiniGamePayload

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | MiniGameGameOverPacket | FUN_1432619a0 / FUN_14330e850 | same | not sent | none |
| 0x1 → 0x1 | C→S | BaseMiniGamePacket | FUN_142bac000 FUN_1432617d0 / FUN_142c45b80 FUN_14330e680 | same | not handled | none |
| 0x1 → 0x1 | S→C | MiniGameGameEndScorePacket | FUN_1432617d0 / FUN_14330e680 | same | not sent | none |
| 0x1 → 0x1 | S→C | MiniGameGroupInfoPacket | FUN_1432617d0 / FUN_14330e680 | same | not sent | none |
| 0x1 → 0x1 | S→C | MiniGameKnockOutPacket | FUN_1432617d0 / FUN_14330e680 | same | not sent | none |
| 0x1 → 0x1 | S→C | MiniGameMessagePacket | FUN_1432617d0 / FUN_14330e680 | same | not sent | none |
| 0x1 → 0x1 | S→C | MiniGameStarsEarnedPacket | FUN_1432617d0 / FUN_14330e680 | same | not sent | none |
| 0xc → 0xc | C→S | MiniGamePayloadPacket | FUN_143263860 FUN_143263150 / FUN_143310710 FUN_143310000 | same | not handled | none |
| 0xe → 0xe | S→C | MiniGameInfoPacket | FUN_1432617d0 / FUN_14330e680 | same | not sent | none |
| 0x14 → 0x14 | S→C | MiniGameRewardStatusPacket | FUN_143263980 / FUN_143310830 | same | not sent | none |

### 0x2B Groups

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | C→S | Groups::GroupPacketAcceptTeleport | FUN_142d429b0 / FUN_142de0c10 | same | handled (Game/Groups.cs:100) | none |
| 0x1 → 0x1 | C→S | Groups::GroupPacketAutoGroup | FUN_142d42bb0 FUN_142d48ed0 / FUN_142de0e10 FUN_142de7130 | same | handled (Game/Groups.cs:100) | none |
| 0x1 → 0x1 | C→S | Groups::GroupPacketDisband | FUN_142d42f40 / FUN_142de11a0 | same | handled (Game/Groups.cs:100) | none |
| 0x1 → 0x1 | C→S | Groups::GroupPacketFindLookingForMoreGroup | FUN_142d43120 / FUN_142de1380 | same | handled (Game/Groups.cs:100) | none |
| 0x1 → 0x1 | S→C | Groups::GroupPacketFindLookingForMoreGroupReply | FUN_142d457c0 / FUN_142de3a20 | same | sent (Game/Groups.cs:29 InviteSub) | none |
| 0x1 → 0x1 | C→S | Groups::GroupPacketGetGroupInfo | FUN_142d43300 / FUN_142de1560 | same | handled (Game/Groups.cs:100) | none |
| 0x1 → 0x1 | S→C | Groups::GroupPacketGetGroupInfoReply | FUN_142d45970 / FUN_142de3bd0 | same | sent (Game/Groups.cs:29 InviteSub) | none |
| 0x1 → 0x1 | C→S | Groups::GroupPacketInvite | FUN_142d45ad0 FUN_142d434a0 / FUN_142de3d30 FUN_142de1700 | same | handled (Game/Groups.cs:100) | none |
| 0x1 → 0x1 | C→S | Groups::GroupPacketJoin | FUN_142d45bf0 FUN_142d43640 / FUN_142de3e50 FUN_142de18a0 | same | handled (Game/Groups.cs:100) | none |
| 0x1 → 0x1 | C→S | Groups::GroupPacketJoinLookingForMore | FUN_142d437e0 / FUN_142de1a40 | same | handled (Game/Groups.cs:100) | none |
| 0x1 → 0x1 | C→S | Groups::GroupPacketKick | FUN_142d43980 / FUN_142de1be0 | same | handled (Game/Groups.cs:100) | none |
| 0x1 → 0x1 | C→S | Groups::GroupPacketLeave | FUN_142d43b20 / FUN_142de1d80 | same | handled (Game/Groups.cs:100) | none |
| 0x1 → 0x1 | C→S | Groups::GroupPacketSetGroupDescription | FUN_142d460c0 FUN_142d43d00 / FUN_142de4320 FUN_142de1f60 | same | handled (Game/Groups.cs:100) | none |
| 0x1 → 0x1 | C→S | Groups::GroupPacketSetGroupFlags | FUN_142d43ef0 / FUN_142de2150 | same | handled (Game/Groups.cs:100) | none |
| 0x1 → 0x1 | C→S | Groups::GroupPacketSetGroupOwner | FUN_142d46230 FUN_142d44090 / FUN_142de4490 FUN_142de22f0 | same | handled (Game/Groups.cs:100) | none |
| 0x2 → 0x2 | S→C | Groups::GroupPacketAddMember | FUN_142d45590 / FUN_142de37f0 | same | sent (Game/Groups.cs:29 JoinSub) | none |
| 0x2 → 0x2 | C→S | Groups::GroupPacketChangingJob | FUN_142d42d50 / FUN_142de0fb0 | same | handled (Game/Groups.cs:103) | none |
| 0x2 → 0x2 | S→C | Groups::GroupPacketInviteToGroupContent | FUN_142d3f560 / FUN_142ddd7c0 | same | sent (Game/Groups.cs:29 JoinSub) | none |
| 0x2 → 0x2 | C→S | Groups::GroupPacketMapPing | FUN_142d45e60 FUN_142bac3a0 / FUN_142de40c0 FUN_142c45f20 | same | handled (Game/Groups.cs:103) | none |
| 0x2 → 0x2 | S→C | Groups::GroupPacketRemoveInvitation | FUN_142d45fc0 / FUN_142de4220 | same | sent (Game/Groups.cs:29 JoinSub) | none |

### 0x2C EncounterInvitationResponse

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | BaseEncounterPacket | FUN_142b7dda0 / FUN_142c17550 | same | sent (Game/Encounters.cs:72) | none |
| 0x3 → 0x3 | S→C | EncounterPacketEncounterComplete | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | sent (Game/Encounters.cs:28 SubComplete) | none |
| 0x67 → 0x67 | C→S | EncounterInvitationResponsePacket | FUN_142ba8ff0 / FUN_142c42b70 | same | handled (Game/Encounters.cs:192) | none |
| 0x69 → 0x69 | C→S | EncounterDuelInvitationResponsePacket | FUN_14333d260 / FUN_1433e9320 | same | handled (Game/Encounters.cs:201) | none |
| 0x6c → 0x6c | C→S | EncounterParticipantRequestEntrancePacket | FUN_142ba9380 / FUN_142c42f00 | same | handled (Game/Encounters.cs:180) | none |
| 0x6d → 0x6d | C→S | EncounterParticipantRequestExitPacket | FUN_142ba9530 / FUN_142c430b0 | same | handled (Game/Encounters.cs:184) | none |
| 0x72 → 0x72 | S→C | EncounterDetailsResponsePacket | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | sent (Game/Encounters.cs:158) | none |
| 0x7c → 0x7c | C→S | EncounterParticipantCancelPendingEncounterPacket | FUN_142ba91e0 / FUN_142c42d60 | same | handled (Game/Encounters.cs:187) | none |

### 0x2D Inventory

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → 0x1e | S→C | InventoryPacketShowActivationFailure | – / – | new: S→C u16 0x2D, i16 0x1E, u32 text id (packets-695 §10) | not sent | implement (0.695 only) |
| 0x2 → 0x2 | C→S | InventoryPacketEquippedRemove | FUN_14331f4b0 / FUN_1433cc2a0 | same | handled (Network/Handlers/InventoryHandler.cs:98) | none |
| 0x3 → 0x3 | C→S | InventoryPacketEquipByGuid | FUN_14331f130 / FUN_1433cbf20 | same | handled (Network/Handlers/InventoryHandler.cs:93) | none |
| 0x6 → 0x6 | C→S | InventoryPacketItemActionBarAssign | FUN_143523970 / FUN_1435d52f0 | same | family handler, sub not handled (Network/Handlers/InventoryHandler.cs:22) | none |
| 0x8 → 0x8 | C→S | InventoryPacketEquipByItemRecord | FUN_142f25c40 / FUN_142fc4f80 | same | family handler, sub not handled (Network/Handlers/InventoryHandler.cs:22) | none |
| 0xa → 0xa | C→S | InventoryPacketEquipItemGroup | – / – | same: builder closure same | family handler, sub not handled (Network/Handlers/InventoryHandler.cs:22) | none |
| 0xb → 0xb | C→S | InventoryPacketRequestDeleteItem | FUN_142e79930 / FUN_142f18560 | same | handled (Network/Handlers/InventoryHandler.cs:62) | none |
| 0xc → 0xc | C→S | InventoryPacketRequestSplitStack | FUN_14352e3d0 / FUN_1435e00f0 | same | family handler, sub not handled (Network/Handlers/InventoryHandler.cs:22) | none |
| 0xd → 0xd | C→S | InventoryPacketToggleAutoResupply | FUN_142e79e70 / FUN_142f18aa0 | same | family handler, sub not handled (Network/Handlers/InventoryHandler.cs:22) | none |
| 0xe → 0xe | C→S | InventoryPacketRearrangeItemSlots | FUN_14352e000 / FUN_1435dfd20 | same | handled (Network/Handlers/InventoryHandler.cs:76) | none |
| 0xf → 0xf | C→S | InventoryPacketItemInstanceTooltipRequest | FUN_14345a6f0 / FUN_14350c7f0 | same | handled (Network/Handlers/InventoryHandler.cs:44) | none |
| 0x10 → 0x10 | C→S | InventoryPacketItemInstanceFullTooltipRequest | FUN_14345a540 / FUN_14350c640 | same | handled (Network/Handlers/InventoryHandler.cs:179; Network/Handlers/InventoryHandler.cs:45) | none |
| 0x11 → 0x11 | C→S | InventoryPacketItemDefinitionTooltipRequest | FUN_14345a3a0 / FUN_14350c4a0 | same | handled (Network/Handlers/InventoryHandler.cs:176; Network/Handlers/InventoryHandler.cs:46) | none |
| 0x12 → 0x12 | S→C | InventoryPacketItemTooltipReply | FUN_142e784e0 / FUN_142f17070 | same | sent (Network/Message/Model/InventoryTooltipReply.cs:14 (InventoryHandler.cs:206)) | none |
| 0x13 → 0x13 | C→S | InventoryPacketRequestSalvageItem | FUN_142e79ad0 / FUN_142f18700 | same | family handler, sub not handled (Network/Handlers/InventoryHandler.cs:22) | none |
| 0x14 → 0x14 | S→C | InventoryPacketReplySalvageItem | FUN_142e78740 / FUN_142f172d0 | same | not sent | none |
| 0x15 → 0x15 | C→S | InventoryPacketActivateItem | FUN_1433a57b0 / FUN_143452910 | same | handled (Network/Handlers/InventoryHandler.cs:103) | none |
| 0x16 → 0x16 | C→S | InventoryPacketRequestTransferItem | FUN_142e79cc0 / FUN_142f188f0 | same | handled (Network/Handlers/InventoryHandler.cs:27) | none |
| 0x17 → 0x17 | S→C | InventoryPacketSetSize | FUN_142e788b0 / FUN_142f17440 | same | sent (Game/ItemUses.cs:62 SetSizeSub) | none |
| 0x19 → 0x19 | S→C | InventoryPacketTransferItemResponse | FUN_142e78a30 / FUN_142f17650 | same | sent (Network/Handlers/InventoryHandler.cs:42) | none |
| 0x1a → 0x1a | S→C | InventoryPacketLootNotification | FUN_142e78620 / FUN_142f171b0 | same | sent (Game/LootPinatas.cs:149) | none |
| 0x1b → 0x1b | S→C | InventoryPacketCatalystDefinitions | FUN_142e78390 / FUN_142f16f20 | same | not sent | none |
| 0x1c → 0x1c | C→S | InventoryPacketRequestOverflowAbsorb | FUN_14352e1f0 / FUN_1435dff10 | same | handled (Network/Handlers/InventoryHandler.cs:49) | none |
| 0x1d → 0x1d | S→C | InventoryPacketSetVoxelStorage | FUN_142e78970 / FUN_142f17500 | same | sent (Game/ItemUses.cs:150) | none |

### 0x2E PacketSendZoneDetails

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | PacketSendZoneDetails | FUN_142b8aee0 / FUN_142c246b0 | same | sent (Network/Message/Model/SendZoneDetails.cs:12; Network/Handlers/MiscHandler.cs:248) | none |

### 0x2F ReferenceData

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | S→C | ReferenceDataPacketItemClassDefinitions | FUN_142bad7d0 FUN_143ac1da0 / FUN_142c47350 FUN_143b7db80 | same | sent (Network/Message/Model/ReferenceDataItemClassDefinitions.cs:10; Network/WorldSession.cs:422) | none |
| 0x2 → 0x2 | S→C | ReferenceDataPacketItemCategoryDefinitions | FUN_142bad7d0 FUN_143ac1da0 / FUN_142c47350 FUN_143b7db80 | same | sent (Network/Message/Model/ReferenceDataItemCategoryDefinitions.cs:10; Network/WorldSession.cs:425) | none |

### 0x30 ObjectiveClientComplete

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | S→C | ObjectiveActivatePacket | FUN_143261f30 / FUN_14330ede0 | same | not sent | none |
| 0x2 → 0x2 | S→C | ObjectiveUpdatePacket | FUN_143262520 / FUN_14330f3d0 | same | not sent | none |
| 0x3 → 0x3 | S→C | ObjectiveCompletePacket | FUN_143262110 / FUN_14330efc0 | same | not sent | none |
| 0x4 → 0x4 | S→C | ObjectiveFailPacket | FUN_143263ef0 / FUN_143310da0 | same | not sent | none |
| 0x5 → 0x5 | S→C | ObjectiveUnhidePacket | FUN_143264130 / FUN_143310fe0 | same | not sent | none |
| 0x6 → 0x6 | S→C | ObjectiveLookAtPacket | FUN_143262280 / FUN_14330f130 | same | not sent | none |
| 0x7 → 0x7 | C→S | ObjectiveClientCompletePacket | FUN_1436f6a50 / FUN_1437ae930 | same | not handled | none |
| 0x8 → 0x8 | S→C | ObjectiveClientClearPacket | FUN_143263d20 / FUN_143310bd0 | same | not sent | none |
| 0x9 → 0x9 | S→C | ObjectiveClientCompleteFailedPacket | FUN_143263dd0 / FUN_143310c80 | same | not sent | none |
| 0xa → 0xa | S→C | ObjectiveUIEventPacket | FUN_143264010 / FUN_143310ec0 | same | not sent | none |

### 0x32 SetWaypoint

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | S→C | TaskAddPacket | FUN_14333c6d0 / FUN_1433e8790 | same | not sent | none |
| 0x2 → 0x2 | S→C | TaskUpdatePacket | FUN_14333e900 / FUN_1433ea9c0 | same | not sent | none |
| 0x3 → 0x3 | S→C | TaskCompletePacket | FUN_14333e7a0 / FUN_1433ea860 | same | not sent | none |
| 0x4 → 0x4 | S→C | TaskFailPacket | FUN_14333e850 / FUN_1433ea910 | same | not sent | none |
| 0x6 → 0x6 | C→S | SelectTaskRequest | FUN_14333dd90 / FUN_1433e9e50 | same | family handler, sub not handled (Network/Handlers/MiscHandler.cs:72) | none |
| 0x7 → 0x7 | S→C | ExecuteScriptPacket | FUN_14333e250 / FUN_1433ea310 | same | not sent | none |
| 0x8 → 0x8 | S→C | LoadingScreenPacket | FUN_14333e4a0 / FUN_1433ea560 | same | not sent | none |
| 0x9 → 0x9 | S→C | StartTimerPacket | FUN_14333e680 / FUN_1433ea740 | same | not sent | none |
| 0xb → 0xb | C→S | SelectQuestPacket | FUN_142e8b5d0 / FUN_142f2a170 | same | family handler, sub not handled (Network/Handlers/MiscHandler.cs:72) | none |
| 0xc → 0xc | C→S | SelectedQuestLockedPacket | FUN_142e8b7b0 / FUN_142f2a350 | same | family handler, sub not handled (Network/Handlers/MiscHandler.cs:72) | none |
| 0xd → 0xd | S→C | ObjectiveTargetUpdatePacket | FUN_14333c490 / FUN_1433e8550 | same | not sent | none |
| 0xe → 0xe | S→C | UiMessagePacket | FUN_14333e9d0 / FUN_1433eaa90 | same | not sent | none |
| 0xf → 0xf | S→C | CinematicStartLookAtPacket | FUN_14333c060 / FUN_1433e8120 | same | not sent | none |
| 0x10 → 0x10 | S→C | WeaponHitFeedbackPacket | FUN_14333c990 / FUN_1433e8a50 | same | not sent | none |
| 0x11 → 0x11 | S→C | HeadShotFeedbackPacket | FUN_14333c260 / FUN_1433e8320 | same | not sent | none |
| 0x12 → 0x12 | C→S | SetWaypointPacket | FUN_14333df80 / FUN_1433ea040 | same | family handler, sub not handled (Network/Handlers/MiscHandler.cs:72) | none |
| 0x13 → 0x13 | S→C | WaypointCooldownPacket | FUN_14333ea90 / FUN_1433eab50 | same | not sent | none |
| 0x14 → 0x14 | S→C | ZoneWaypointPacket | FUN_14333ebc0 / FUN_1433eac80 | same | not sent | none |
| 0x15 → 0x15 | S→C | InteractStartPacket | FUN_14333c320 / FUN_1433e83e0 | same | not sent | none |
| 0x17 → 0x17 | C→S | UIEventTriggeredPacket | FUN_1430b24e0 / FUN_14315c600 | same | handled (Network/Handlers/MiscHandler.cs:75) | none |

### 0x34 QuestReply

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | S→C | QuestInfoPacket | FUN_142e8ac40 / FUN_142f297e0 | same | not sent | none |
| 0x2 → 0x2 | C→S | QuestReplyPacket | FUN_14333dbf0 / FUN_1433e9cb0 | same | not handled | none |
| 0x3 → 0x3 | S→C | QuestAddPacket | FUN_142e8a8f0 / FUN_142f29490 | same | not sent | none |
| 0x4 → 0x4 | S→C | QuestCompletePacket | FUN_142e8bbc0 / FUN_142f2a760 | same | not sent | none |
| 0x5 → 0x5 | S→C | QuestFailedPacket | FUN_142e8bce0 / FUN_142f2a880 | same | not sent | none |
| 0x6 → 0x6 | S→C | QuestAbandonedPacket | FUN_142e8a830 / FUN_142f293d0 | same | not sent | none |
| 0x7 → 0x7 | S→C | QuestObjectiveAddedPacket | FUN_142e8be70 / FUN_142f2aa10 | same | not sent | none |
| 0x8 → 0x8 | S→C | QuestObjectiveActivatedPacket | FUN_142e8ae90 / FUN_142f29a30 | same | not sent | none |
| 0x9 → 0x9 | S→C | QuestObjectiveUpdatePacket | FUN_142e8b240 / FUN_142f29de0 | same | not sent | none |
| 0xa → 0xa | S→C | QuestObjectiveCompletePacket | FUN_142e8b040 / FUN_142f29be0 | same | not sent | none |
| 0xb → 0xb | S→C | QuestObjectiveFailedPacket | FUN_142e8b180 / FUN_142f29d20 | same | not sent | none |
| 0xc → 0xc | S→C | CompletedQuestCountUpdatePacket | FUN_142e8ba30 / FUN_142f2a5d0 | same | not sent | none |
| 0xd → 0xd | S→C | QuestEndPacket | FUN_142e8aa40 / FUN_142f295e0 | same | not sent | none |
| 0xe → 0xe | C→S | QuestEndReplyPacket | FUN_14333da10 / FUN_1433e9ad0 | same | not handled | none |
| 0xf → 0xf | S→C | QuestSelectedPacket | FUN_142e8c0a0 / FUN_142f2ac40 | same | not sent | none |

### 0x35 Reward

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | RewardBasePacket | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |
| 0x1 → 0x1 | S→C | RewardBundlePacket | FUN_143136da0 / FUN_1431e1f80 | same | sent (runtime opcode_stats (trace/families; agent_packets/sent_s2c.tsv)) | none |

### 0x37 PacketGameTimeSync

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | C→S | PacketGameTimeSync | FUN_14312f170 FUN_14312f260 / FUN_1431da350 FUN_1431da440 | same | handled (Network/Handlers/MiscHandler.cs:171) | none |

### 0x38 PacketDelayWrapped

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | PacketDelayWrappedPacket | FUN_142b8a550 / FUN_142c23d20 | same | not sent | none |

### 0x3E TradeInitiate

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | C→S | TradeInitiatePacket | FUN_142bad1e0 / FUN_142c46d60 | same | handled (Game/Trades.cs:69) | none |
| 0x2 → 0x2 | S→C | TradeInvitePacket | FUN_14358ccb0 / FUN_14363fb80 | same | sent (Game/Trades.cs:101) | none |
| 0x3 → 0x3 | C→S | TradeInviteReplyPacket | FUN_14358db10 / FUN_1436409e0 | same | handled (Game/Trades.cs:70) | none |
| 0x4 → 0x4 | S→C | TradeStartSessionPacket | FUN_14358ce80 / FUN_14363fd50 | same | sent (Game/Trades.cs:124) | none |
| 0x5 → 0x5 | S→C | TradeEndSessionPacket | FUN_14358df00 / FUN_143640dd0 | same | sent (Game/Trades.cs:310; Game/Trades.cs:340) | none |
| 0x6 → 0x6 | C→S | TradeAcceptPacket | FUN_14358d5f0 / FUN_1436404c0 | same | handled (Game/Trades.cs:71) | none |
| 0x7 → 0x7 | C→S | TradeCancelPacket | FUN_14358d790 / FUN_143640660 | same | handled (Game/Trades.cs:72) | none |
| 0x8 → 0x8 | C→S | TradeOfferItemPacket | FUN_14358dcb0 / FUN_143640b80 | same | handled (Game/Trades.cs:73) | none |
| 0x9 → 0x9 | C→S | TradeCoinCountPacket | FUN_14358d970 / FUN_143640840 | same | handled (Game/Trades.cs:74) | none |
| 0xa → 0xa | S→C | TradeUpdateCoinCountPacket | FUN_14358d170 / FUN_143640040 | same | sent (Game/Trades.cs:193) | none |
| 0xb → 0xb | S→C | TradeUpdateAddItemPacket | FUN_14358d030 / FUN_14363ff00 | same | sent (Game/Trades.cs:162) | none |
| 0xc → 0xc | S→C | TradeUpdateItemCountPacket | FUN_14358d280 / FUN_143640150 | same | sent (Game/Trades.cs:153) | none |
| 0xd → 0xd | S→C | TradeUpdateAcceptPacket | FUN_14358cf70 / FUN_14363fe40 | same | sent (Game/Trades.cs:209) | none |
| 0xe → 0xe | S→C | TradeMessagePacket | FUN_14358e020 / FUN_143640ef0 | same | sent (Game/Trades.cs:182; Game/Trades.cs:226; Game/Trades.cs:236) | none |
| 0xf → 0xf | S→C | TradeRejectedPacket | FUN_14358e0e0 / FUN_143640fb0 | same | sent (Game/Trades.cs:120) | none |

### 0x42 Crafting

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | C→S | Crafting::RecipeStartRequestPacket | FUN_142e94500 / FUN_142f330a0 | same | handled (Network/Handlers/CraftingHandler.cs:26) | none |
| 0x3 → 0x3 | C→S | Crafting::RecipeCancelRequestPacket | FUN_142e92f30 / FUN_142f31ad0 | same | handled (Network/Handlers/CraftingHandler.cs:40) | none |
| 0x6 → 0x6 | C→S | Crafting::RecipeCraftWindowEnableRequestPacket | FUN_142e930d0 / FUN_142f31c70 | same | handled (Network/Handlers/CraftingHandler.cs:46) | none |
| 0x7 → 0x7 | S→C | Crafting::RecipeCraftWindowEnableResponsePacket | FUN_142e91340 / FUN_142f2fee0 | same | not sent | none |
| 0x8 → 0x8 | S→C | Crafting::RecipeAddRecipeResponsePacket | FUN_142e91100 / FUN_142f2fca0 | same | not sent | none |
| 0xa → 0xa | C→S | Crafting::RecipeUpgradeRequestPacket | FUN_142e946a0 / FUN_142f33240 | same | family handler, sub not handled (Network/Handlers/CraftingHandler.cs:20) | none |
| 0xb → 0xb | S→C | Crafting::RecipeUpgradeResponsePacket | FUN_142e91ce0 / FUN_142f30880 | same | not sent | none |
| 0xc → 0xc | C→S | Crafting::RecipeRemoveUpgradeRequestPacket | FUN_142e93e20 / FUN_142f329c0 | same | family handler, sub not handled (Network/Handlers/CraftingHandler.cs:20) | none |
| 0xd → 0xd | S→C | Crafting::RecipeRemoveUpgradeResponsePacket | FUN_142e91950 / FUN_142f304f0 | same | not sent | none |
| 0xe → 0xe | C→S | Crafting::RecipeUpgradeStatRequestPacket | FUN_142e94850 / FUN_142f333f0 | same | family handler, sub not handled (Network/Handlers/CraftingHandler.cs:20) | none |
| 0x10 → 0x10 | S→C | Crafting::RecipeRefiningProcessResponsePacket | FUN_142e91680 / FUN_142f30220 | same | not sent | none |
| 0x11 → 0x11 | C→S | Crafting::RecipeRefiningRemoveProcessRequestPacket | FUN_142e93c40 / FUN_142f327e0 | same | family handler, sub not handled (Network/Handlers/CraftingHandler.cs:20) | none |
| 0x12 → 0x12 | C→S | Crafting::RecipeRefiningAddFuelRequestPacket | FUN_142e934a0 / FUN_142f32040 | same | family handler, sub not handled (Network/Handlers/CraftingHandler.cs:20) | none |
| 0x13 → 0x13 | S→C | Crafting::RecipeRefiningStateResponsePacket | FUN_142e91780 / FUN_142f30320 | same | not sent | none |
| 0x14 → 0x14 | C→S | Crafting::RecipeRefiningCommandPacket | FUN_142e93880 / FUN_142f32420 | same | family handler, sub not handled (Network/Handlers/CraftingHandler.cs:20) | none |
| 0x16 → 0x16 | C→S | Crafting::RecipeRefiningApplyAdditiveRequestPacket | FUN_142e93690 / FUN_142f32230 | same | family handler, sub not handled (Network/Handlers/CraftingHandler.cs:20) | none |
| 0x17 → 0x17 | C→S | Crafting::RecipeRefiningRemoveAdditiveRequestPacket | FUN_142e93a60 / FUN_142f32600 | same | family handler, sub not handled (Network/Handlers/CraftingHandler.cs:20) | none |
| 0x18 → 0x18 | C→S | Crafting::RecipeSalvagingStartRequestPacket | FUN_142e94310 / FUN_142f32eb0 | same | handled (Network/Handlers/CraftingHandler.cs:53) | none |
| 0x19 → 0x19 | C→S | Crafting::RecipeSalvagingAddItemRequest | FUN_142e94170 / FUN_142f32d10 | same | handled (Network/Handlers/CraftingHandler.cs:52) | none |
| 0x1a → 0x1a | S→C | Crafting::RecipeSalvagingAddItemResponse | FUN_142e91a90 / FUN_142f30630 | same | sent (Game/Salvaging.cs:129) | none |
| 0x1c → 0x1c | C→S | Crafting::RecipecComponentImageSetIdsRequestPacket | FUN_1434a1340 / FUN_143550e40 | same | handled (Network/Handlers/CraftingHandler.cs:29) | none |
| 0x1d → 0x1d | S→C | Crafting::RecipecComponentImageSetIdsResponsePacket | FUN_142e91e20 / FUN_142f309c0 | same | not sent | none |
| 0x1e → 0x1e | S→C | Crafting::PlayerRecipeDataUpdatePacket | FUN_142e91040 / FUN_142f2fbe0 | same | not sent | none |
| 0x1f → 0x1f | S→C | Crafting::RecipeHideRecipeResponsePacket | FUN_142e914b0 / FUN_142f30050 | same | not sent | none |
| 0x20 → 0x20 | S→C | Crafting::RecipeUnhideRecipeResponsePacket | FUN_142e91c50 / FUN_142f307f0 | same | not sent | none |
| 0x21 → 0x21 | C→S | Crafting::RecipeRepairItemRequestPacket | FUN_142e93fd0 / FUN_142f32b70 | same | handled (Network/Handlers/CraftingHandler.cs:49) | none |
| 0x22 → 0x22 | S→C | Crafting::RecipeRepairItemResponsePacket | FUN_142e91a00 / FUN_142f305a0 | same | sent (Game/Durability.cs:129) | none |
| 0x23 → 0x23 | C→S | Crafting::RecipePacketPlayerOpenCraftingWindow | FUN_142e932c0 / FUN_142f31e60 | same | handled (Network/Handlers/CraftingHandler.cs:43) | none |
| 0x24 → 0x24 | S→C | Crafting::RecipePacketZoneCraftingWindow | FUN_142e91540 / FUN_142f300e0 | same | not sent | none |

### 0x45 InGamePurchase

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → 0x30 | C→S | PacketInGamePurchaseCommerceTemplateProductCreate | – / – | new | not handled; the number 0x30 is the 0.589 meaning on the server (Game/Commerce.cs:51 SubProductCreateResponse) | implement (0.695 only) |
| 0x0 → 0x0 | C→S | PacketInGamePurchaseCommerceProductCreate | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | handled (Network/Handlers/InteractionHandler.cs:265) | none |
| 0x0 → 0x0 | S→C | PacketInGamePurchaseGiftNotification | FUN_14318d020 / FUN_143237d80 | same | sent (Game/Commerce.cs:632) | none |
| 0x0 → 0x0 | S→C | PacketInGamePurchaseNudgeOfferNotification | FUN_143183980 / FUN_14322e440 | same | sent (Game/Commerce.cs:632) | none |
| 0x2 → 0x2 | S→C | PacketInGamePurchasePreviewOrderResponse | FUN_143183ef0 / FUN_14322e9b0 | same | not sent | none |
| 0x4 → 0x4 | S→C | PacketInGamePurchasePlaceOrderResponse | FUN_143183b10 / FUN_14322e5d0 | same | not sent | none |
| 0x5 → 0x5 | S→C | PacketInGamePurchaseStoreBundleBase | FUN_143184280 / FUN_14322ed40 | same | not sent | none |
| 0xb → 0xb | C→S | PacketInGamePurchaseWalletInfoRequest | FUN_14318bbd0 / FUN_143236930 | same | handled (Network/Handlers/InteractionHandler.cs:274) | none |
| 0xc → 0xc | S→C | PacketInGamePurchaseWalletInfoResponse | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | sent (Game/Commerce.cs:204; Game/Commerce.cs:211; Network/Handlers/InteractionHandler.cs:292) | none |
| 0xd → 0xd | C→S | PacketInGamePurchaseServerStatusRequest | FUN_14318b430 / FUN_143236190 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:265) | none |
| 0xe → 0xe | S→C | PacketInGamePurchaseServerStatusResponse | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |
| 0xf → 0xf | C→S | PacketInGamePurchaseStationCashProductsRequest | FUN_14318b5d0 / FUN_143236330 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:265) | none |
| 0x10 → 0x10 | S→C | PacketInGamePurchaseStationCashProductsResponse | FUN_14318d950 / FUN_1432386b0 | same | not sent | none |
| 0x14 → 0x14 | S→C | PacketInGamePurchaseStateCodesResponse | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |
| 0x15 → 0x15 | C→S | PacketInGamePurchaseCountryCodesRequest | FUN_14318af40 / FUN_143235ca0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:265) | none |
| 0x16 → 0x16 | S→C | PacketInGamePurchaseCountryCodesResponse | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |
| 0x17 → 0x17 | C→S | PacketInGamePurchaseSubscriptionProductsRequest | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:265) | none |
| 0x18 → 0x18 | S→C | PacketInGamePurchaseSubscriptionProductsResponse | FUN_14318e1f0 / FUN_143238f50 | same | not sent | none |
| 0x1a → 0x1a | C→S | PacketInGamePurchaseAccountInfoRequest | FUN_143189ac0 / FUN_143234640 | same | handled (Game/Commerce.cs:115) | none |
| 0x1b → 0x1b | S→C | PacketInGamePurchaseAccountInfoResponse | FUN_143182cf0 / FUN_14322d7b0 | same | sent (Game/Commerce.cs:116) | none |
| 0x1c → 0x1c | C→S | PacketInGamePurchaseStoreBundleContentRequest | FUN_14318b780 / FUN_1432364e0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:265) | none |
| 0x20 → 0x20 | S→C | PacketInGamePurchaseDisplayMannequinStoreBundles | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |
| 0x22 → 0x22 | S→C | PacketInGamePurchaseStoreEnablePaymentSources | FUN_143184690 / FUN_14322f150 | same | not sent | none |
| 0x27 → 0x27 | S→C | PacketInGamePurchaseActiveSchedules | FUN_14318e530 / FUN_143239290 | same | not sent | none |
| 0x28 → 0x28 | C→S | PacketInGamePurchaseLoyaltyInfoAndStoreRequest | FUN_14318b0f0 / FUN_143235e50 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:265) | none |
| 0x2a → 0x2a | C→S | PacketInGamePurchaseSpiceWebAuthUrlRequest | FUN_14318bd70 / FUN_143236ad0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:265) | none |
| 0x2b → 0x2b | S→C | PacketInGamePurchaseSpiceWebAuthUrlResponse | FUN_14318e860 / FUN_1432395c0 | same | not sent | none |
| 0x2c → 0x2c | S→C | PacketInGamePurchaseBundlePriceUpdate | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |
| 0x2e → 0x2e | S→C | PacketInGamePurchaseCommerceConfiguration | FUN_1431830a0 / FUN_14322db60 | same | not sent | none |
| 0x30 → 0x31 | S→C | PacketInGamePurchaseCommerceProductCreateResponse | FUN_14318c7a0 / FUN_143237500 | renumbered | sent (Game/Commerce.cs:624) | use sub 0x31 for 0.695 |
| 0x31 → 0x32 | C→S | PacketInGamePurchaseCommerceProductRemove | FUN_14318a320 / FUN_143234ea0 | renumbered | handled (Game/Commerce.cs:137) | use sub 0x32 for 0.695 |
| 0x33 → 0x34 | C→S | PacketInGamePurchaseCommerceProductUpdate | FUN_14318a4d0 / FUN_143235050 | renumbered | handled (Game/Commerce.cs:140) | use sub 0x34 for 0.695 |
| 0x34 → 0x35 | C→S | PacketInGamePurchaseCommerceFollowCreator | FUN_143189c70 / FUN_1432347f0 | renumbered | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:265) | (not implemented; 0.695 number 0x35) |
| 0x35 → 0x36 | C→S | PacketInGamePurchaseCommerceUnFollowCreator | FUN_14318ad90 / FUN_143235af0 | renumbered | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:265) | (not implemented; 0.695 number 0x36) |
| 0x36 → 0x37 | C→S | PacketInGamePurchaseCommerceRequestAccountStore | FUN_14318a810 / FUN_143235390 | renumbered | handled (Game/Commerce.cs:134) | use sub 0x37 for 0.695 |
| 0x37 → 0x38 | C→S | PacketInGamePurchaseCommerceRecommendations | FUN_14318a670 / FUN_1432351f0 | renumbered | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:265) | (not implemented; 0.695 number 0x38) |
| 0x38 → 0x39 | S→C | PacketInGamePurchaseCommerceAccountStore | FUN_143182f10 / FUN_14322d9d0 | renumbered | sent (Game/Commerce.cs:263) | use sub 0x39 for 0.695 |
| 0x39 → 0x3a | S→C | PacketInGamePurchaseCommerceAddFollowing | FUN_143182fc0 / FUN_14322da80 | renumbered | not sent | (not implemented; 0.695 number 0x3a) |
| 0x3a → 0x3b | S→C | PacketInGamePurchaseCommerceRemoveFollowing | FUN_143183390 / FUN_14322de50 | renumbered | not sent | (not implemented; 0.695 number 0x3b) |
| 0x3c → 0x3d | S→C | PacketInGamePurchaseCommerceProductUpdated | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |
| 0x3d → 0x3e | C→S | PacketInGamePurchaseCommerceSearch | FUN_14318a9b0 / FUN_143235530 | renumbered | handled (Game/Commerce.cs:143) | use sub 0x3e for 0.695 |
| 0x3e → 0x3f | S→C | PacketInGamePurchaseStoreBundleSearchResults | FUN_143184530 / FUN_14322eff0 | renumbered | sent (Game/Commerce.cs:81) | use sub 0x3f for 0.695 |
| 0x3f → 0x40 | C→S | PacketInGamePurchaseCommerceGetProductForReviewRequest | FUN_143189e20 / FUN_1432349a0 | renumbered | handled (Game/Commerce.cs:128) | use sub 0x40 for 0.695 |
| 0x40 → 0x41 | S→C | PacketInGamePurchaseCommerceReviewProductResponse | FUN_1431834e0 / FUN_14322dfa0 | renumbered | sent (Game/Commerce.cs:484) | use sub 0x41 for 0.695 |
| 0x41 → 0x42 | S→C | AdminCommandPacketInGamePurchaseGetAdminReviewsReply | FUN_143181f00 / FUN_14322c9c0 | renumbered | not sent | (not implemented; 0.695 number 0x42) |
| 0x42 → 0x43 | C→S | PacketInGamePurchaseCommerceSubmitProductReview | – / – | renumbered: builder differs only in the sub constant 0x42→0x43 | handled (Game/Commerce.cs:131) | use sub 0x43 for 0.695 |
| 0x45 → 0x46 | S→C | PacketInGamePurchaseModifyScreenshotsResponse | FUN_143183890 / FUN_14322e350 | renumbered | sent (Game/SiteScreenshots.cs:102) | use sub 0x46 for 0.695 |
| 0x46 → 0x47 | C→S | PacketInGamePurchasePointOfNeedRequestPacket | FUN_14318b290 / FUN_143235ff0 | renumbered | handled (Network/Handlers/InteractionHandler.cs:268) | use sub 0x47 for 0.695 |
| 0x47 → 0x48 | S→C | PacketInGamePurchasePointOfNeedPromptRequestPacket | FUN_1431aa810 FUN_143183d90 / FUN_1432556e0 FUN_14322e850 | renumbered | not sent | (not implemented; 0.695 number 0x48) |
| 0x49 → 0x4a | C→S | PacketInGamePurchaseModifyScreenshots | FUN_1435352b0 / FUN_1435e7280 | renumbered | handled (Game/Commerce.cs:146) | use sub 0x4a for 0.695 |
| 0x4b → 0x4c | S→C | PacketInGamePurchasePointOfNeedInfoResponse | FUN_143183c50 / FUN_14322e710 | renumbered | not sent | (not implemented; 0.695 number 0x4c) |
| 0x4c → 0x4d | C→S | PacketInGamePurchaseCommerceIsTemplateValidRequest | FUN_143189fc0 / FUN_143234b40 | renumbered | handled (Game/Commerce.cs:149) | use sub 0x4d for 0.695 |
| 0x4d → 0x4e | S→C | PacketInGamePurchaseCommerceIsTemplateValidResponse | FUN_143183180 / FUN_14322dc40 | renumbered | sent (Game/Commerce.cs:154) | use sub 0x4e for 0.695 |

### 0x46 QuickChatSendData

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | QuickChatSendChatPacketBase | FUN_143290db0 / FUN_14333dc20 | same | sent (Network/Handlers/MiscHandler.cs:221) | none |

### 0x47 Report

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | C→S | ReportPacketReportPlayer | FUN_142babcb0 / FUN_142c45830 | same | not handled | none |

### 0x48 LiveGamerPostItem

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | C→S | LiveGamerPostItemPacket | FUN_143511660 / FUN_1435c2c40 | same | not handled | none |

### 0x4B AddAcquaintance

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | S→C | AddAcquaintancePacket | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |
| 0x2 → 0x2 | S→C | RemoveAcquaintancePacket | FUN_142f269c0 / FUN_142fc5eb0 | same | not sent | none |
| 0x3 → 0x3 | S→C | AcquaintanceOnlinePacket | FUN_142f24f70 / FUN_142fc4230 | same | not sent | none |

### 0x4D FriendUpdateExtraInfos

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | FriendMessagePacket | FUN_14348c3a0 / FUN_14353c090 | same | sent (Game/Friends.cs:86) | none |
| 0x1 → 0x1 | S→C | FriendListPacket | FUN_14348d770 / FUN_14353d460 | same | sent (Game/Friends.cs:55) | none |
| 0x2 → 0x2 | S→C | FriendOnlinePacket | FUN_14348c520 / FUN_14353c210 | same | sent (Game/Friends.cs:59) | none |
| 0x3 → 0x3 | S→C | FriendOfflinePacket | FUN_14348d8a0 / FUN_14353d590 | same | sent (Game/Friends.cs:67) | none |
| 0x4 → 0x4 | S→C | FriendUpdateProfileInfoPacket | FUN_14348c990 / FUN_14353c680 | same | not sent | none |
| 0x5 → 0x5 | S→C | FriendUpdatePositionsPacket | FUN_14348db70 / FUN_14353d860 | same | not sent | none |
| 0x6 → 0x6 | S→C | FriendAddPacket | FUN_14348d6b0 / FUN_14353d3a0 | same | sent (Game/Friends.cs:30 AddSub) | none |
| 0x7 → 0x7 | S→C | FriendRemovePacket | FUN_14348d9d0 / FUN_14353d6c0 | same | sent (Game/Friends.cs:156) | none |
| 0x9 → 0x9 | S→C | FriendStatusPacket | FUN_14348c850 / FUN_14353c540 | same | not sent | none |
| 0xa → 0xa | S→C | FriendRenamePacket | FUN_14348c6a0 / FUN_14353c390 | same | not sent | none |
| 0xb → 0xb | C→S | FriendUpdateExtraInfos | FUN_14348d3b0 / FUN_14353d0a0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:131) | none |

### 0x4F 0x4F

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | BaseBroadcastPacket | FUN_142b7dc60 / FUN_142c17410 | same | not sent | none |

### 0x53 PacketClientKickedFromServer

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | PacketClientKickedFromServer | FUN_142bb0360 / FUN_142c49ed0 | same | sent (Game/PlayerManager.cs:55) | none |

### 0x54 PacketUpdateClientSessionData

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | PacketUpdateClientSessionData | FUN_142b8b520 / FUN_142c24cf0 | same | not sent | none |

### 0x55 BugSubmission

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | C→S | BugSubmissionPacketAddBug | FUN_14306de40 / FUN_143118760 | same | family handler, sub not handled (Game/Reports.cs:27) | none |

### 0x56 ClaimReportSubmission

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | C→S | ClaimReportSubmissionPacketAddReport | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | family handler, sub not handled (Game/Reports.cs:31) | none |

### 0x59 PacketWorldDisplayInfo

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | PacketWorldDisplayInfo | FUN_142bb1520 / FUN_142c4b090 | same | sent (Network/Handlers/MiscHandler.cs:258) | none |

### 0x5A PacketMOTD

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | PacketMOTD | FUN_142bb0af0 / FUN_142c4a660 | same | sent (Game/PlayerManager.cs:120) | none |

### 0x5B PacketSetLocale

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | PacketSetLocale | FUN_142be2e00 / FUN_142c7c980 | same | not sent | none |

### 0x5C PacketSetClientArea

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | PacketSetClientArea | FUN_142b8b160 / FUN_142c24930 | same | not sent | none |

### 0x5F PacketWorldShutdownNotice

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | PacketWorldShutdownNotice | FUN_142b8b650 / FUN_142c24e20 | same | sent (Game/Shutdown.cs:207) | none |

### 0x62 MiniGameFlashCommandAdmin

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1a → 0x1a | C→S | BaseMiniGameAdminPacket | FUN_143262e00 / FUN_14330fcb0 | same | not handled | none |
| 0x2f → 0x2f | C→S | MiniGameFlashCommandAdminPacket | FUN_143262fa0 / FUN_14330fe50 | same | not handled | none |

### 0x63 PacketKeepAlive

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | C→S | PacketKeepAlive | FUN_142baaab0 / FUN_142c44630 | same | handled (Network/Handlers/MiscHandler.cs:43) | none |

### 0x64 PacketClientExitLaunchUrl

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | PacketClientExitLaunchUrl | FUN_142bb0150 / FUN_142c49cc0 | same | not sent | none |

### 0x65 PacketClientPendingKickFromServer

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | PacketClientPendingKickFromServer | FUN_142bb0560 / FUN_142c4a0d0 | same | not sent | none |

### 0x66 Activity

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | PacketMembershipActivation | FUN_142bb0c50 / FUN_142c4a7c0 | same | not sent | none |

### 0x67 JoinLobbyGame

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | C→S | JoinLobbyGamePacket | FUN_142b87990 FUN_14335bca0 / FUN_142c21160 FUN_143407d60 | same | not handled | none |
| 0x2 → 0x2 | C→S | LeaveLobbyGamePacket | FUN_142b87ac0 FUN_14335be40 / FUN_142c21290 FUN_143407f00 | same | not handled | none |
| 0x3 → 0x3 | C→S | StartLobbyGamePacket | FUN_142b95ec0 FUN_14335c1c0 / FUN_142c2f9b0 FUN_143408280 | same | not handled | none |
| 0x5 → 0x5 | C→S | CreateLobbyGamePacket | FUN_14335b970 / FUN_143407a30 | same | not handled | none |
| 0x9 → 0x9 | C→S | LeaveLobbyPacket | FUN_14335bfe0 / FUN_1434080a0 | same | not handled | none |
| 0xb → 0xb | S→C | LobbyErrorMessagePacket | FUN_142bad710 / FUN_142c47290 | same | not sent | none |
| 0xc → 0xc | S→C | ShowLobbyUiPacket | FUN_142b95ad0 / FUN_142c2f5c0 | same | not sent | none |

### 0x68 LobbyGameDefinition

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | LobbyGameDefinitionPacketDefinitionsResponse | FUN_142b88540 / FUN_142c21d10 | same | not sent | none |

### 0x69 PacketShowSystemMessage

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | PacketShowSystemMessage | FUN_142b8b220 / FUN_142c249f0 | same | sent (Game/SystemMessages.cs:40) | none |

### 0x6A PacketPOIChangeMessage

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | PacketPOIChangeMessage | FUN_142b8a940 / FUN_142c24110 | same | not sent | none |

### 0x6B PacketClientMetrics

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | C→S | PacketClientMetrics | FUN_142baa380 / FUN_142c43f00 | same | handled (Network/Handlers/MiscHandler.cs:124) | none |

### 0x6D FirstTimeEventTriggerRequest

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | FirstTimeEventScriptPacket | FUN_14357f000 / FUN_143632800 | same | sent (runtime opcode_stats (trace/families; agent_packets/sent_s2c.tsv)) | none |
| – → – | S→C | FirstTimeEventStatePacket | FUN_14357f100 / FUN_143632900 | same | sent (runtime opcode_stats (trace/families; agent_packets/sent_s2c.tsv)) | none |
| 0x1 → 0x1 | C→S | FirstTimeEventTriggerRequest | FUN_14357f660 / FUN_143632e60 | same | handled (Game/FirstTimeEvents.cs:144) | none |
| 0x3 → 0x3 | C→S | FirstTimeEventClearRequest | FUN_14357f2a0 / FUN_143632aa0 | same | handled (Game/FirstTimeEvents.cs:147) | none |
| 0x4 → 0x4 | C→S | FirstTimeEventEnablePacket | – / – | same: builder closure same | handled (Game/FirstTimeEvents.cs:162) | none |

### 0x6E GetAllAvailableRequisitionItemsRequest

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | C→S | GetAllAvailableRequisitionItemsRequestPacket | FUN_1431242e0 / FUN_1431cf350 | same | not handled | none |
| 0x2 → 0x2 | S→C | GetAllAvailableRequisitionItemsResponsePacket | FUN_143124900 / FUN_1431cf970 | same | not sent | none |
| 0x3 → 0x3 | C→S | RequisitionItemsRequestPacket | FUN_143124670 / FUN_1431cf6e0 | same | not handled | none |
| 0x4 → 0x4 | S→C | RequisitionItemsResponsePacket | FUN_143123bf0 / FUN_1431cec50 | same | not sent | none |
| 0x5 → 0x5 | C→S | RequisitionItemsItemDefinitionsRequest | FUN_143124480 / FUN_1431cf4f0 | same | not handled | none |
| 0x6 → 0x6 | S→C | RequisitionItemsItemDefinitionsResponse | FUN_143123ad0 / FUN_1431ceb30 | same | not sent | none |

### 0x6F PacketClientLog

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | C→S | PacketClientLog | FUN_142ba9de0 / FUN_142c43960 | same | handled (Network/Handlers/MiscHandler.cs:25) | none |

### 0x72 IgnoreList

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x2 → 0x2 | S→C | IgnoreAddPacket | FUN_14348dca0 / FUN_14353d990 | same | sent (Game/Ignores.cs:145) | none |

### 0x74 KeyCodeRedemptionNotification

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | S→C | KeyCodeRedemptionNotificationPacket | FUN_142f35ea0 / FUN_142fd5c10 | same | not sent | none |

### 0x75 PacketAddClientPortraitCrc

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | C→S | PacketAddClientPortraitCrc | FUN_143140720 / FUN_1431eb140 | same | handled (Network/Handlers/MiscHandler.cs:132) | none |

### 0x77 PacketCommerceSessionRequest

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | C→S | PacketCommerceSessionRequest | FUN_142baa560 / FUN_142c440e0 | same | not handled | none |

### 0x78 PacketCommerceSessionResponse

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | PacketCommerceSessionResponse | FUN_142bb58f0 / FUN_142c4f100 | same | not sent | none |

### 0x7C PacketZoneSafeTeleportRequest

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | C→S | PacketZoneSafeTeleportRequest | FUN_142bab310 / FUN_142c44e90 | same | handled (Network/Handlers/MiscHandler.cs:32) | none |

### 0x7F PlayerUpdatePosition

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | PlayerUpdatePacketUpdatePositionZoneToClient | – / FUN_142c4dd40 | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | sent (Network/Message/Model/PlayerUpdatePosition.cs:10) | none |

### 0x83 GuildCreate

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | C→S | GuildCreatePacket | FUN_1432e1230 / FUN_14338e020 | same | handled (Game/Guilds.cs:318) | none |
| 0x2 → 0x2 | C→S | GuildDisbandPacket | FUN_142e499b0 FUN_1432e15b0 / FUN_142ee8740 FUN_14338e3a0 | same | handled (Game/Guilds.cs:344) | none |
| 0x3 → 0x3 | C→S | GuildRenamePacket | FUN_1432e2760 FUN_142e46990 / FUN_14338f550 FUN_142ee5720 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:167) | none |
| 0x4 → 0x4 | C→S | GuildInvitePacket | FUN_1432e1760 / FUN_14338e550 | same | handled (Game/Guilds.cs:321) | none |
| 0x5 → 0x5 | C→S | GuildInviteReplyPacket | FUN_1432e1920 / FUN_14338e710 | same | handled (Game/Guilds.cs:326) | none |
| 0x6 → 0x6 | C→S | GuildInviteRevokePacket | FUN_1432e1b10 / FUN_14338e900 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:167) | none |
| 0x7 → 0x7 | C→S | GuildPetitionPacket | FUN_1432e2050 / FUN_14338ee40 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:167) | none |
| 0x8 → 0x8 | C→S | GuildPetitionReplyPacket | FUN_1432e2210 / FUN_14338f000 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:167) | none |
| 0x9 → 0x9 | C→S | GuildPetitionRevokePacket | FUN_1432e2400 / FUN_14338f1f0 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:167) | none |
| 0xa → 0xa | C→S | GuildChangeMemberRankPacket | FUN_142e457c0 FUN_142e4d360 / FUN_142ee4550 FUN_142eec0f0 | same | handled (Game/Guilds.cs:341) | none |
| 0xb → 0xb | C→S | GuildMotdUpdatePacket | FUN_1432e1e80 FUN_142e46610 / FUN_14338ec70 FUN_142ee53a0 | same | handled (Game/Guilds.cs:330) | none |
| 0xc → 0xc | C→S | GuildKickPacket | FUN_1432e1cc0 / FUN_14338eab0 | same | handled (Game/Guilds.cs:337) | none |
| 0xd → 0xd | C→S | GuildQuitPacket | FUN_1432e25b0 / FUN_14338f3a0 | same | handled (Game/Guilds.cs:334) | none |
| 0xe → 0xe | C→S | GuildUpdateRankPacket | FUN_142e49210 FUN_142e46c20 / FUN_142ee7fa0 FUN_142ee59b0 | same | handled (Game/Guilds.cs:347) | none |
| 0xf → 0xf | C→S | GuildDescriptionUpdatePacket | FUN_1432e13e0 FUN_142e45c60 / FUN_14338e1d0 FUN_142ee49f0 | same | handled (Game/Guilds.cs:350) | none |
| 0x10 → 0x10 | C→S | GuildRequestMutePlayerPacket | FUN_142e4ced0 / FUN_142eebc60 | same | handled (Game/Guilds.cs:356) | none |
| 0x11 → 0x11 | C→S | GuildUpdateAppDataFieldPacket | FUN_1432e2920 / FUN_14338f710 | same | handled (Game/Guilds.cs:353) | none |
| 0x12 → 0x12 | S→C | GuildDataFullPacket | FUN_142e45a90 / FUN_142ee4820 | same | not sent | none |
| 0x13 → 0x13 | S→C | GuildDataPacket | FUN_142e49860 / FUN_142ee85f0 | same | sent (Game/Guilds.cs:43 DataSub) | none |
| 0x14 → 0x14 | S→C | GuildInvitationsPacket | FUN_142e462b0 / FUN_142ee5040 | same | not sent | none |
| 0x15 → 0x15 | S→C | GuildAddMemberPacket | FUN_142e49570 / FUN_142ee8300 | same | sent (Game/Guilds.cs:43 AddMemberSub) | none |
| 0x16 → 0x16 | S→C | GuildRemoveMemberPacket | FUN_142e468c0 / FUN_142ee5650 | same | sent (Game/Guilds.cs:43 RemoveMemberSub) | none |
| 0x17 → 0x17 | S→C | GuildUpdateInvitationPacket | FUN_142e46b40 / FUN_142ee58d0 | same | sent (Game/Guilds.cs:43 UpdateInvitationSub) | none |
| 0x18 → 0x18 | S→C | GuildMemberOnlineStatusPacket | FUN_142e46520 / FUN_142ee52b0 | same | sent (Game/Guilds.cs:43 OnlineStatusSub) | none |
| 0x19 → 0x19 | S→C | GuildNotificationPacket | FUN_142e46710 / FUN_142ee54a0 | same | sent (Game/Guilds.cs:1113 NotificationSub) | none |
| 0x1a → 0x1a | S→C | GuildAddLogUpdatePacket | FUN_142e454f0 / FUN_142ee4280 | same | not sent | none |
| 0x1b → 0x1b | S→C | GuildRemoveLogUpdatePacket | FUN_142e467f0 / FUN_142ee5580 | same | not sent | none |
| 0x1c → 0x1c | S→C | GuildUpdateAppDataPacket | FUN_142e46a50 / FUN_142ee57e0 | same | not sent | none |

### 0x8A EncounterMatchmaking

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | EncounterMatchmaking::RemoveParticipantFromQueueResponsePacket | FUN_1432d8b80 / FUN_143385970 | same | not sent | none |
| 0x2 → 0x2 | S→C | EncounterMatchmaking::ListQueuesResponsePacket | FUN_1432d7ef0 / FUN_143384ce0 | same | not sent | none |
| 0x4 → 0x4 | S→C | EncounterMatchmaking::AddMatchRequestResponsePacket | FUN_1432e67e0 / FUN_1433935d0 | same | not sent | none |
| 0x5 → 0x5 | S→C | EncounterMatchmaking::ClearMatchRequestPacket | FUN_1432e6930 / FUN_143393720 | same | not sent | none |
| 0x9 → 0x9 | S→C | EncounterMatchmaking::MatchInvitationRequestPacket | FUN_1432d8010 / FUN_143384e00 | same | not sent | none |
| 0xa → 0xa | C→S | EncounterMatchmaking::MatchInvitationResponsePacket | FUN_14333d810 / FUN_1433e98d0 | same | not handled | none |
| 0xc → 0xc | S→C | EncounterMatchmaking::SelectQueueForUserPacket | FUN_1432e6e90 / FUN_143393c80 | same | not sent | none |
| 0xe → 0xe | S→C | EncounterMatchmaking::QueueStatsResponsePacket | FUN_1432d8ac0 / FUN_1433858b0 | same | not sent | none |

### 0x8B PacketClientLuaMetrics

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | C→S | PacketClientLuaMetrics | FUN_142baa1a0 / FUN_142c43d20 | same | not handled | none |

### 0x8D RepeatingActivityClearRequest

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | S→C | RepeatingActivityAddPacket | FUN_143134310 / FUN_1431df4f0 | same | not sent | none |
| 0x2 → 0x2 | S→C | RepeatingActivityStatePacket | FUN_143133680 / FUN_1431de860 | same | not sent | none |
| 0x3 → 0x3 | S→C | RepeatingActivityConsecutiveMsgPacket | FUN_143133520 / FUN_1431de700 | same | not sent | none |
| 0x4 → 0x4 | C→S | RepeatingActivityClearRequest | FUN_143133b00 / FUN_1431dece0 | same | not handled | none |
| 0x5 → 0x5 | C→S | RepeatingActivitySetConsecutivePacket | FUN_143133e90 / FUN_1431df070 | same | not handled | none |
| 0x6 → 0x6 | C→S | RepeatingActivityAddCountPacket | FUN_143133960 / FUN_1431deb40 | same | not handled | none |
| 0x7 → 0x7 | S→C | RepeatingActivityAddCountResultPacket | FUN_143133440 / FUN_1431de620 | same | not sent | none |
| 0x8 → 0x8 | C→S | RepeatingActivitySetCountPacket | FUN_143134030 / FUN_1431df210 | same | not handled | none |
| 0x9 → 0x9 | C→S | RepeatingActivityGetCountRemainingPacket | FUN_143133cf0 / FUN_1431deed0 | same | not handled | none |
| 0xa → 0xa | S→C | RepeatingActivityCountRemainingInfoPacket | FUN_143133440 / FUN_1431de620 | same | not sent | none |

### 0x8E PacketClientGameSettings

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | PacketClientGameSettings | FUN_142b89ef0 / FUN_142c236c0 | same | sent (Network/Message/Model/ClientGameSettings.cs:11; Network/WorldSession.cs:421) | none |

### 0x8F ActivityJoinError

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | S→C | ActivityProfileListPacket | FUN_142c57d80 / FUN_142cf15f0 | same | not sent | none |

### 0x91 Inspect

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → 0x3 | C→S | RequestInspectPlayerPacket | – / – | new | not handled | implement (0.695 only) |
| 0x0 → 0x0 | S→C | BaseInspectPacket | FUN_142bb4e60 / FUN_142c4e670 | same | not sent | none |
| 0x1 → 0x1 | S→C | StartInspectPacket | FUN_142b95dd0 / FUN_142c2f8c0 | same | not sent | none |
| 0x2 → 0x2 | C→S | StopInspectPacket | FUN_142bad040 / FUN_142c46bc0 | same | not handled | none |

### 0x92 AchievementTrackerUpdate

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x2 → 0x2 | S→C | AchievementAddPacket | FUN_142d8c370 / FUN_142e2a340 | same | not sent | none |
| 0x3 → 0x3 | S→C | AchievementInitializePacket | FUN_142d8c550 / FUN_142e2a520 | same | sent (Game/Achievements.cs:38 InitializeSub) | none |
| 0x4 → 0x4 | S→C | AchievementCompletePacket | FUN_142d8c430 / FUN_142e2a400 | same | sent (Game/Achievements.cs:38 CompleteSub) | none |
| 0x5 → 0x5 | S→C | AchievementObjectiveAddedPacket | FUN_142d8c680 / FUN_142e2a650 | same | not sent | none |
| 0x6 → 0x6 | S→C | AchievementObjectiveActivatedPacket | FUN_142d8a950 / FUN_142e28920 | same | sent (Game/Achievements.cs:38 ActivatedSub) | none |
| 0x7 → 0x7 | S→C | AchievementObjectiveUpdatePacket | FUN_142d8ac90 / FUN_142e28c60 | same | sent (Game/Achievements.cs:38 UpdateSub) | none |
| 0x8 → 0x8 | S→C | AchievementObjectiveCompletePacket | FUN_142d8ab50 / FUN_142e28b20 | same | sent (Game/Achievements.cs:38 ObjectiveCompleteSub) | none |
| 0xc → 0xc | C→S | AchievementTrackerUpdatePacket | FUN_142d8ae90 / FUN_142e28e60 | same | handled (Game/Achievements.cs:340) | none |
| 0xe → 0xe | S→C | AchievementSecretCompletePacket | FUN_142d8c840 / FUN_142e2a810 | same | not sent | none |
| 0xf → 0xf | S→C | AchievementGlobalAnnouncePacket | FUN_142d8a7b0 / FUN_142e28780 | same | sent (Game/Achievements.cs:837 GlobalAnnounceSub) | none |

### 0x94 PlayerTitleRequestSelect

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | PlayerTitleAddPacket | FUN_142e89070 / FUN_142f27d20 | same | sent (runtime opcode_stats (trace/families; agent_packets/sent_s2c.tsv)) | none |
| 0x3 → 0x3 | S→C | PlayerTitleUpdateAllPacket | FUN_142e88c10 / FUN_142f278c0 | same | sent (runtime opcode_stats (trace/families; agent_packets/sent_s2c.tsv)) | none |
| 0x4 → 0x4 | C→S | PlayerTitleRequestSelectPacket | FUN_142e88df0 / FUN_142f27aa0 | same | handled (Game/Titles.cs:37 RequestSelectSub) | none |

### 0x97 BeginJobCustimzation

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | S→C | BeginJobCustimzationPacket | FUN_142b7ec10 / FUN_142c183c0 | same | not sent | none |

### 0x98 PacketGeneratePortraitRequest

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | C→S | PacketGeneratePortraitRequest | FUN_142ebb060 / FUN_142f59c90 | same | family handler, sub not handled (Network/Handlers/MiscHandler.cs:128) | none |
| 0x2 → 0x2 | C→S | PacketPortraitDataRequest | FUN_142ebb210 / FUN_142f59e40 | same | family handler, sub not handled (Network/Handlers/MiscHandler.cs:128) | none |
| 0x3 → 0x3 | S→C | PacketPlayerImageData | FUN_142ebab00 / FUN_142f59730 | same | sent (Game/Portraits.cs:77) | none |
| 0x4 → 0x4 | C→S | PacketSnapshotRequest | FUN_142bacae0 / FUN_142c46660 | same | family handler, sub not handled (Network/Handlers/MiscHandler.cs:128) | none |

### 0x9A ActionManager

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | ActionManagerPacketUpdateSlot | FUN_142b7d3a0 / FUN_142c16b50 | same | not sent | none |
| 0x1 → 0x1 | C→S | ActionManagerPacketRequestClearSlot | FUN_14339dc00 / FUN_14344b380 | same | handled (Network/Handlers/ActionBarHandler.cs:26) | none |
| 0x2 → 0x2 | C→S | ActionManagerPacketRequestSetSlot | FUN_14339dda0 / FUN_14344b520 | same | handled (Network/Handlers/ActionBarHandler.cs:45) | none |
| 0x3 → 0x3 | C→S | ActionManagerPacketDefaultSlot | FUN_14339da60 / FUN_14344b1e0 | same | handled (Network/Handlers/ActionBarHandler.cs:35) | none |
| 0x4 → 0x4 | C→S | ActionManagerPacketDefaultAllSlots | FUN_14339d8c0 / FUN_14344b040 | same | handled (Network/Handlers/ActionBarHandler.cs:40) | none |

### 0x9C Social

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | S→C | SocialPacketProfileInfo | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |
| 0x3 → 0x3 | C→S | SocialPacketUpdateLike | FUN_142ec4530 / FUN_142f63160 | same | not handled | none |
| 0x4 → 0x4 | C→S | SocialPacketReputationUpdate | FUN_142ec3c40 / FUN_142f62870 | same | not handled | none |
| 0x5 → 0x5 | C→S | SocialPacketRequestProfile | – / – | same: builder closure same | not handled | none |
| 0x6 → 0x6 | S→C | SocialPacketHighScoreUpdate | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |
| 0x7 → 0x7 | C→S | SocialPacketRequestGameTopScores | FUN_142ec3fc0 / FUN_142f62bf0 | same | not handled | none |
| 0x8 → 0x8 | C→S | SocialPacketRequestFriendsGameTopScores | FUN_142ec3de0 / FUN_142f62a10 | same | not handled | none |
| 0x9 → 0x9 | C→S | SocialPacketRequestGamesPlayedCount | FUN_142ec41a0 / FUN_142f62dd0 | same | not handled | none |
| 0xd → 0xd | S→C | SocialPacketNotification | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |
| 0xe → 0xe | C→S | SocialPacketIssueChallenge | FUN_142ec3a50 / FUN_142f62680 | same | not handled | none |
| 0xf → 0xf | C→S | SocialPacketChallengeResponse | FUN_142ec3270 / FUN_142f61ea0 | same | not handled | none |
| 0x13 → 0x13 | C→S | SocialPacketDiplomacyEvent | FUN_142ec3460 / FUN_142f62090 | same | not handled | none |
| 0x15 → 0x15 | C→S | SocialPacketHouseRating | FUN_142ec3850 / FUN_142f62480 | same | not handled | none |
| 0x18 → 0x18 | C→S | SocialPacketHouseFurnitureScore | FUN_142ec3650 / FUN_142f62280 | same | not handled | none |
| 0x19 → 0x19 | S→C | SocialPacketReputationStatsUpdated | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |
| 0x1a → 0x1a | S→C | SocialPacketChallengeUpdated | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |

### 0x9E CoinStoreItemDefinitionsRequest

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x2 → 0x2 | C→S | CoinStoreItemDefinitionsRequestPacket | FUN_1434b15e0 / FUN_1435610e0 | same | not handled | none |
| 0x3 → 0x3 | S→C | CoinStoreItemDefinitionsResponsePacket | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |
| 0x4 → 0x4 | S→C | CoinStoreSellToClientRequestPacket | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |
| 0x5 → 0x5 | C→S | CoinStoreBuyFromClientRequestPacket | FUN_1434b13e0 / FUN_143560ee0 | same | not handled | none |
| 0x6 → 0x6 | S→C | CoinStoreTransactionCompletePacket | FUN_1434b0120 / FUN_14355fc20 | same | not sent | none |
| 0x7 → 0x7 | S→C | CoinStoreOpenPacket | FUN_1434afe00 / FUN_14355f900 | same | not sent | none |
| 0x8 → 0x8 | C→S | CoinStoreItemDynamicListUpdateRequestPacket | FUN_1434b1790 / FUN_143561290 | same | not handled | none |
| 0xc → 0xc | C→S | CoinStoreBuyBackRequestPacket | FUN_1434b1200 / FUN_143560d00 | same | not handled | none |
| 0xd → 0xd | S→C | CoinStoreBuyBackResponsePacket | FUN_1434afaa0 / FUN_14355f5a0 | same | not sent | none |
| 0xe → 0xe | C→S | CoinStoreSellToClientAndGiftRequestPacket | FUN_1434b1930 / FUN_143561430 | same | not handled | none |
| 0x11 → 0x11 | S→C | CoinStoreReceiveGiftItemPacket | FUN_1434b0030 / FUN_14355fb30 | same | not sent | none |
| 0x12 → 0x12 | S→C | CoinStoreGiftTransactionCompletePacket | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |

### 0x9F PacketInitializationParameters

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | PacketInitializationParameters | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | sent (Network/Message/Model/InitializationParameters.cs:9; Network/WorldSession.cs:420) | none |

### 0xA0 Activity

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | C→S | ActivityPacketActivityListRefreshRequest | FUN_142ba7ac0 / FUN_142c41640 | same | not handled | none |
| 0x1 → 0x1 | C→S | ActivityPacketJoinActivityRequest | FUN_142d9a440 / FUN_142e38410 | same | not handled | none |
| 0x1 → 0x1 | S→C | ActivityPacketListOfActivities | FUN_142d993f0 / FUN_142e373c0 | same | sent (runtime opcode_stats (trace/families; agent_packets/sent_s2c.tsv)) | none |
| 0x1 → 0x1 | S→C | ActivityPacketUpdateActivityFeaturedStatus | FUN_142d9a7d0 / FUN_142e387a0 | same | sent (runtime opcode_stats (trace/families; agent_packets/sent_s2c.tsv)) | none |
| 0x2 → 0x2 | S→C | ScheduledActivityPacketListOfActivities | FUN_142d99cf0 / FUN_142e37cc0 | same | not sent | none |

### 0xA1 PacketMountRequest

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | MountBasePacket | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |
| 0x1 → 0x1 | C→S | PacketMountRequest | FUN_142baac50 / FUN_142c447d0 | same | not handled | none |
| 0x2 → 0x2 | S→C | PacketMountResponse | FUN_142e6f120 / FUN_142f0dcb0 | same | not sent | none |
| 0x3 → 0x3 | C→S | PacketDismountRequest | FUN_142baa730 / FUN_142c442b0 | same | not handled | none |
| 0x4 → 0x4 | S→C | PacketDismountResponse | FUN_142e6ef90 / FUN_142f0db20 | same | not sent | none |
| 0x5 → 0x5 | S→C | PacketMountList | FUN_142e6f960 / FUN_142f0e4f0 | same | not sent | none |
| 0x6 → 0x6 | C→S | PacketMountSpawn | FUN_14353d800 / FUN_1435ef7e0 | same | not handled | none |
| 0x8 → 0x8 | C→S | PacketMountSpawnByItemDefinitionId | FUN_142e6f6b0 / FUN_142f0e240 | same | not handled | none |
| 0xa → 0xa | C→S | PacketSeatChangeRequest | FUN_142fa4b80 / FUN_1430449b0 | same | not handled | none |
| 0xb → 0xb | S→C | PacketSeatChangeResponse | FUN_142e6f2b0 / FUN_142f0de40 | same | not sent | none |
| 0xc → 0xc | S→C | PacketSeatSwapRequest | FUN_142e6f430 / FUN_142f0dfc0 | same | not sent | none |
| 0xd → 0xd | C→S | PacketSeatSwapResponse | FUN_14333d630 / FUN_1433e96f0 | same | not handled | none |

### 0xA2 PacketClientInitializationDetails

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | C→S | PacketClientInitializationDetails | FUN_142ba9a70 / FUN_142c435f0 | same | handled (Network/Handlers/MiscHandler.cs:165) | none |

### 0xA4 PacketClientNotifyCoinSpinAvailable

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | PacketClientNotifyCoinSpinAvailable | FUN_142bb04b0 / FUN_142c4a020 | same | not sent | none |

### 0xA5 PacketClientAreaTimer

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | C→S | PacketClientAreaTimer | FUN_142b89a80 FUN_143621690 / FUN_142c23250 FUN_1436dc6e0 | same | not handled | none |

### 0xA7 Rating

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | RatingPacketDeleteContestReply | FUN_142d026d0 / FUN_142da2520 | same | sent (runtime opcode_stats (trace/families; agent_packets/sent_s2c.tsv)) | none |
| 0x8 → 0x8 | C→S | RatingPacketAddVote | – / – | same: builder closure same | family handler, sub not handled (Network/Handlers/MiscHandler.cs:144) | none |
| 0x12 → 0x12 | S→C | RatingPacketVoteReply | FUN_142d02880 / FUN_142da26d0 | same | sent (Game/Ratings.cs:49; Game/Ratings.cs:73) | none |
| 0x18 → 0x18 | C→S | RatingPacketCanVoteRequest | FUN_142d07f50 / FUN_142da83e0 | same | family handler, sub not handled (Network/Handlers/MiscHandler.cs:144) | none |
| 0x19 → 0x19 | S→C | RatingPacketCanVoteReply | FUN_142d024b0 / FUN_142da2300 | same | sent (Game/Ratings.cs:122) | none |
| 0x1b → 0x1b | S→C | RatingPacketCreateContestReply | FUN_142d025d0 / FUN_142da2420 | same | not sent | none |
| 0x1d → 0x1d | C→S | RatingPacketRequestContestsRequest | FUN_14353a490 / FUN_1435ec460 | same | family handler, sub not handled (Network/Handlers/MiscHandler.cs:144) | none |
| 0x1e → 0x1e | S→C | RatingPacketRequestContestsReply | FUN_142d09110 / FUN_142da95a0 | same | sent (Game/Contests.cs:168) | none |

### 0xA8 ClientActivityLaunch

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0xd → 0xd | S→C | ClientActivityLaunchPacketKickMemberEvent | FUN_1436586d0 / FUN_143713a60 | same | not sent | none |
| 0x11 → 0x11 | S→C | ClientActivityLaunchPacketInviteResponse | FUN_143657080 / FUN_143712410 | same | not sent | none |
| 0x12 → 0x12 | S→C | ClientActivityLaunchPacketMemberReady | FUN_143657420 / FUN_1437127b0 | same | not sent | none |
| 0x13 → 0x13 | S→C | ClientActivityLaunchPacketMemberQuit | FUN_143657310 / FUN_1437126a0 | same | not sent | none |
| 0x14 → 0x14 | S→C | ClientActivityLaunchPacketInviteMemberRequest | FUN_143656f00 / FUN_143712290 | same | not sent | none |
| 0x15 → 0x15 | S→C | ClientActivityLaunchPacketKickMemberRequest | FUN_143657190 / FUN_143712520 | same | not sent | none |
| 0x16 → 0x16 | S→C | ClientActivityLaunchPacketOwnerLaunchRequest | FUN_143657530 / FUN_1437128c0 | same | not sent | none |
| 0x17 → 0x17 | S→C | ClientActivityLaunchPacketOwnerMatchmakingRequest | FUN_143657640 / FUN_1437129d0 | same | not sent | none |

### 0xAA PacketClientFlashTimer

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | PacketClientFlashTimer | FUN_142bb0230 / FUN_142c49da0 | same | not sent | none |

### 0xAD PacketInviteAndStartMiniGameInvite

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | PacketInviteAndStartMiniGamePacket | FUN_142b8a660 / FUN_142c23e30 | same | not sent | none |
| 0x1 → 0x1 | C→S | PacketInviteAndStartMiniGameInvitePacket | FUN_14333d450 / FUN_1433e9510 | same | not handled | none |
| 0x3 → 0x3 | C→S | PacketInviteAndStartMiniGameStartGamePacket | FUN_142baa910 / FUN_142c44490 | same | not handled | none |

### 0xAE PlayerUpdate

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | C→S | PlayerUpdatePacketFlourish | FUN_142babad0 / FUN_142c45650 | same | not handled | none |

### 0xAF QuizRequest

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | C→S | QuizRequestPacket | FUN_142ec2b20 / FUN_142f61750 | same | not handled | none |
| 0x2 → 0x2 | S→C | QuizDefinitionPacket | FUN_143120f00 / FUN_1431cbf60 | same | not sent | none |
| 0x3 → 0x3 | C→S | QuizResultPacket | FUN_142ec2d00 / FUN_142f61930 | same | not handled | none |

### 0xB3 PacketClientMembershipVipInfo

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | PacketClientMembershipVipInfo | FUN_142bb0440 / FUN_142c49fb0 | same | not sent | none |

### 0xB5 Target

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | C→S | TargetPacketRequestAddTarget | FUN_143139090 / FUN_1431e4270 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:206) | none |
| 0x2 → 0x2 | C→S | TargetPacketRequestSetTarget | FUN_143139600 / FUN_1431e47e0 | same | handled (Network/Handlers/InteractionHandler.cs:214) | none |
| 0x3 → 0x3 | C→S | TargetPacketRequestRemoveTarget | FUN_143139420 / FUN_1431e4600 | same | handled (Network/Handlers/InteractionHandler.cs:225) | none |
| 0x4 → 0x4 | C→S | TargetPacketRequestRemoveAllTargets | FUN_143139280 / FUN_1431e4460 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:206) | none |
| 0x5 → 0x5 | S→C | TargetPacketAddTarget | FUN_143138bb0 / FUN_1431e3d90 | same | not sent | none |
| 0x6 → 0x6 | S→C | TargetPacketSetTarget | FUN_143138df0 / FUN_1431e3fd0 | same | sent (Network/Handlers/InteractionHandler.cs:235) | none |
| 0x7 → 0x7 | S→C | TargetPacketRemoveTarget | FUN_143138d30 / FUN_1431e3f10 | same | sent (runtime opcode_stats (trace/families; agent_packets/sent_s2c.tsv)) | none |

### 0xB7 GuideStone

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x2 → 0x2 | S→C | GuideStonePacketAddInfo | FUN_14328ed70 / FUN_14333bbe0 | same | not sent | none |

### 0xB8 Raids

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | C→S | Raids::RaidPacketDisband | FUN_142d44230 / FUN_142de2490 | same | not handled | none |
| 0x1 → 0x1 | C→S | Raids::RaidPacketEcho | FUN_142d445b0 / FUN_142de2810 | same | not handled | none |
| 0x1 → 0x1 | C→S | Raids::RaidPacketEcho2 | FUN_142d44410 / FUN_142de2670 | same | not handled | none |
| 0x1 → 0x1 | C→S | Raids::RaidPacketKickGroup | FUN_142d466e0 FUN_142d44760 / FUN_142de4940 FUN_142de29c0 | same | not handled | none |
| 0x1 → 0x1 | C→S | Raids::RaidPacketKickMember | FUN_142d467a0 FUN_142d44900 / FUN_142de4a00 FUN_142de2b60 | same | not handled | none |
| 0x1 → 0x1 | C→S | Raids::RaidPacketLeave | FUN_142d44b00 / FUN_142de2d60 | same | not handled | none |
| 0x1 → 0x1 | C→S | Raids::RaidPacketSetGroupOwner | FUN_142d40450 FUN_142d44ce0 / FUN_142dde6b0 FUN_142de2f40 | same | not handled | none |
| 0x1 → 0x1 | C→S | Raids::RaidPacketSetMemberGroup | FUN_142d44e80 FUN_142d40550 / FUN_142de30e0 FUN_142dde7b0 | same | not handled | none |
| 0x1 → 0x1 | C→S | Raids::RaidPacketSetRaidOwner | FUN_142d45020 / FUN_142de3280 | same | not handled | none |
| 0x1 → 0x1 | C→S | Raids::RaidPacketSwapMembers | FUN_142d46b10 FUN_142d45260 / FUN_142de4d70 FUN_142de34c0 | same | not handled | none |
| 0x2 → 0x2 | S→C | Raids::RaidPacketAddGroup | FUN_142d46430 / FUN_142de4690 | same | not sent | none |
| 0x2 → 0x2 | S→C | Raids::RaidPacketAddRaid | FUN_142d3ff90 / FUN_142dde1f0 | same | not sent | none |
| 0x2 → 0x2 | S→C | Raids::RaidPacketRemoveRaid | FUN_142d46990 / FUN_142de4bf0 | same | not sent | none |

### 0xB9 VoiceLeaveChannel

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | S→C | VoiceJoinChannelPacket | FUN_1430249f0 / FUN_1430cada0 | same | not sent | none |
| 0x6 → 0x6 | C→S | VoiceRequestCustomChannel | FUN_143025040 / FUN_1430cb3f0 | same | not handled | none |

### 0xBA MeleeStabFinish

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | C→S | WeaponGuidedUpdate | FUN_142b96cb0 FUN_143623db0 / FUN_142c307a0 FUN_1436dedf0 | same | not handled | none |
| 0x1 → 0x1 | S→C | WeaponFireStateUpdate | FUN_143561930 / FUN_143615130 | same | not sent | none |
| 0x2 → 0x2 | S→C | WeaponFirePacket | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |
| 0x3 → 0x3 | S→C | WeaponFireNoProjectilePacket | FUN_142f86700 / FUN_143026530 | same | not sent | none |
| 0x5 → 0x5 | S→C | WeaponReloadRequest | FUN_142f86700 / FUN_143026530 | same | not sent | none |
| 0x7 → 0x7 | C→S | WeaponReloadInterruptPacket | FUN_143562c30 / FUN_143616430 | same | not handled | none |
| 0x9 → 0x9 | S→C | WeaponAddAmmoPacket | FUN_142b96900 / FUN_142c303f0 | same | not sent | none |
| 0xa → 0xa | S→C | WeaponSetAmmoPacket | FUN_142b970e0 / FUN_142c30bd0 | same | not sent | none |
| 0xb → 0xb | C→S | WeaponSwitchFireModeRequest | FUN_143562e20 / FUN_143616620 | same | not handled | none |
| 0xc → 0xc | C→S | WeaponLockOnUpdateGuidPacket | FUN_143562760 / FUN_143615f60 | same | not handled | none |
| 0xd → 0xd | C→S | WeaponLockOnUpdateLocationPacket | FUN_143562980 / FUN_143616180 | same | not handled | none |
| 0xe → 0xe | C→S | MeleeSlashPacket | FUN_143275e80 / FUN_143322d30 | same | not handled | none |
| 0xf → 0xf | C→S | MeleeStabStartPacket | FUN_1432816c0 / FUN_14332e530 | same | not handled | none |
| 0x10 → 0x10 | C→S | MeleeStabFinishPacket | FUN_1432814d0 / FUN_14332e340 | same | not handled | none |
| 0x11 → 0x11 | S→C | WeaponStatUpdatePacket | FUN_142bb6960 / FUN_142c50160 | same | not sent | none |
| 0x13 → 0x13 | S→C | WeaponAddFireGroupPacket | FUN_142b96a00 / FUN_142c304f0 | same | not sent | none |
| 0x14 → 0x14 | S→C | WeaponRemoveFireGroupPacket | FUN_142bb6710 / FUN_142c4ff10 | same | not sent | none |
| 0x16 → 0x16 | S→C | BaseRemoteWeaponPacket | FUN_142bb4ff0 / FUN_142c4e800 | same | not sent | none |
| 0x16 → 0x16 | S→C | BaseRemoteWeaponUpdatePacket | FUN_1434f7c80 / FUN_1435a7720 | same | not sent | none |
| 0x16 → 0x16 | S→C | RemoteMeleeSlashPacket | FUN_1434f90a0 / FUN_1435a8b40 | same | not sent | none |
| 0x16 → 0x16 | S→C | RemoteMeleeStabFinishPacket | FUN_1434f91b0 / FUN_1435a8c50 | same | not sent | none |
| 0x16 → 0x16 | S→C | RemoteMeleeStabStartPacket | FUN_1434f92c0 / FUN_1435a8d60 | same | not sent | none |
| 0x16 → 0x16 | S→C | RemoteProjectileDetonateHintPacket | FUN_1434f7f10 / FUN_1435a79b0 | same | not sent | none |
| 0x16 → 0x16 | S→C | RemoteProjectileLaunchHintPacket | FUN_1434f7fe0 / FUN_1435a7a80 | same | not sent | none |
| 0x16 → 0x16 | S→C | RemoteProjectileLaunchPacket | FUN_1434f7c80 / FUN_1435a7720 | same | not sent | none |
| 0x16 → 0x16 | S→C | RemoteWeaponAddFireGroupPacket | FUN_1434f7c80 / FUN_1435a7720 | same | not sent | none |
| 0x16 → 0x16 | S→C | RemoteWeaponAddPacket | FUN_1434f8200 / FUN_1435a7ca0 | same | not sent | none |
| 0x16 → 0x16 | S→C | RemoteWeaponEmptyPacket | FUN_1434f9660 / FUN_1435a9100 | same | not sent | none |
| 0x16 → 0x16 | S→C | RemoteWeaponFireStateUpdate | FUN_1434f8320 / FUN_1435a7dc0 | same | not sent | none |
| 0x16 → 0x16 | S→C | RemoteWeaponRemoveFireGroupPacket | FUN_1434f7c80 / FUN_1435a7720 | same | not sent | none |
| 0x16 → 0x16 | S→C | RemoteWeaponRemovePacket | FUN_1434f9910 / FUN_1435a93b0 | same | not sent | none |
| 0x16 → 0x16 | S→C | RemoteWeaponResetPacket | FUN_1434f8500 / FUN_1435a7fa0 | same | not sent | none |
| 0x16 → 0x16 | S→C | RemoteWeaponStatUpdatePacket | FUN_1434f7c80 / FUN_1435a7720 | same | not sent | none |
| 0x16 → 0x16 | S→C | RemoteWeaponSwitchFireModePacket | FUN_1434f7c80 / FUN_1435a7720 | same | not sent | none |
| 0x17 → 0x17 | C→S | WeaponChamberRoundPacket | FUN_143562570 / FUN_143615d70 | same | not handled | none |
| 0x18 → 0x18 | C→S | QuickUsePacket | FUN_1432818b0 / FUN_14332e720 | same | not handled | none |
| 0x1a → 0x1a | C→S | WeaponChamberInterruptPacket | FUN_143562380 / FUN_143615b80 | same | not handled | none |
| 0x1c → 0x1c | C→S | DestroyNpcProjectilePacket | FUN_1434ed5d0 / FUN_14359d070 | same | not handled | none |
| 0x1d → 0x1d | C→S | WeaponToggleEffectsPacket | FUN_142bb6a30 FUN_143281a90 / FUN_142c50230 FUN_14332e900 | same | not handled | none |
| 0x1e → 0x1e | S→C | WeaponResetPacket | FUN_142bb6810 / FUN_142c50010 | same | not sent | none |
| 0x1f → 0x1f | S→C | ProjectileSpawnNpcPacket | FUN_1434ed2d0 / FUN_14359cd70 | same | not sent | none |
| 0x20 → 0x20 | S→C | WeaponFireRejectedPacket | FUN_142b96ae0 / FUN_142c305d0 | same | not sent | none |
| 0x21 → 0x21 | C→S | MultiWeaponPacket | FUN_142d57410 / FUN_142df5480 | same | not handled | none |
| 0x22 → 0x22 | S→C | WeaponFireHintPacket | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |

### 0xBB PunkBuster

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | C→S | PunkBusterPacket | FUN_143590e40 / FUN_143643c20 | same | not handled | none |

### 0xBC Skills

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | C→S | Skills::SkillPacketEcho | FUN_142eae6d0 / FUN_142f4d300 | same | not handled | none |
| 0x1 → 0x1 | C→S | Skills::SkillPacketGetSkillPointManager | FUN_142eae870 / FUN_142f4d4a0 | same | not handled | none |
| 0x1 → 0x1 | C→S | Skills::SkillPacketSelectSkill | – / – | same: builder closure same | not handled | none |
| 0x1 → 0x1 | C→S | Skills::SkillPacketSelectSkillSet | FUN_142eaebb0 / FUN_142f4d7e0 | same | not handled | none |
| 0x1 → 0x1 | C→S | Skills::SkillPacketSpendClassPoints | FUN_1434ffbe0 / FUN_1435af680 | same | not handled | none |
| 0x1 → 0x1 | S→C | Skills::SkillPacketUpdateClassPoints | FUN_142eb80e0 / FUN_142f56d10 | same | sent (runtime opcode_stats (trace/families; agent_packets/sent_s2c.tsv)) | none |

### 0xBD Loadouts

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | C→S | Loadouts::LoadoutPacketCreateLoadout | FUN_142e66d70 / FUN_142f05900 | same | not handled | none |
| 0x1 → 0x1 | C→S | Loadouts::LoadoutPacketDeleteLoadout | FUN_142e66f60 / FUN_142f05af0 | same | not handled | none |
| 0x1 → 0x1 | C→S | Loadouts::LoadoutPacketSelectLoadout | FUN_142e67140 / FUN_142f05cd0 | same | not handled | none |
| 0x1 → 0x1 | C→S | Loadouts::LoadoutPacketSelectWeaponSet | FUN_142e67320 / FUN_142f05eb0 | same | not handled | none |
| 0x1 → 0x1 | C→S | Loadouts::LoadoutPacketSetAbility | FUN_142e674c0 / FUN_142f06050 | same | not handled | none |
| 0x1 → 0x1 | C→S | Loadouts::LoadoutPacketSetCustomName | FUN_142e67b30 FUN_142e67660 / FUN_142f066c0 FUN_142f061f0 | same | not handled | none |
| 0x2 → 0x2 | S→C | Loadouts::LoadoutPacketUpdateLoadout | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |

### 0xBE Experience

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | Experience::ExperiencePacketSetExperienceRateTier | FUN_142e365a0 / FUN_142ed5240 | same | sent (Network/Handlers/MiscHandler.cs:255) | none |
| 0x1 → 0x1 | S→C | Experience::ExperiencePacketSetExperience | FUN_142e35af0 / FUN_142ed4790 | same | not sent | none |
| 0x2 → 0x2 | S→C | Experience::ExperiencePacketSetExperienceRanks | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |

### 0xBF Vehicle

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | VehicleBasePacket | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |
| 0x1 → 0x1 | S→C | VehicleOwnerPacket | FUN_142ed32a0 FUN_142ecfcd0 / FUN_142f71ed0 FUN_142f6e900 | same | not sent | none |
| 0x2 → 0x2 | S→C | VehicleOccupyPacket | FUN_142ecfaa0 / FUN_142f6e6d0 | same | not sent | none |
| 0x3 → 0x3 | C→S | VehicleStateDataPacket | FUN_142f87e60 FUN_142ed0410 / FUN_143027c90 FUN_142f6f040 | same | not handled | none |
| 0x4 → 0x4 | S→C | VehicleStateDamagePacket | FUN_142ed02f0 / FUN_142f6ef20 | same | not sent | none |
| 0x6 → 0x6 | C→S | VehiclePacketSpawnVehicle | FUN_142bad390 / FUN_142c46f10 | same | not handled | none |
| 0x7 → 0x7 | S→C | VehicleTintPacket | FUN_142ed2620 / FUN_142f71250 | same | not sent | none |
| 0x8 → 0x8 | S→C | VehiclePacketLoadVehicleTerminalDefinitionManager | FUN_143b4b180 / FUN_143c061c0 | same | not sent | none |
| 0x9 → 0x9 | C→S | VehicleActiveWeaponPacket | FUN_142ed0110 FUN_142ed0de0 / FUN_142f6ed40 FUN_142f6fa10 | same | not handled | none |
| 0xa → 0xa | S→C | VehicleStatPacket | FUN_142ed0110 / FUN_142f6ed40 | same | not sent | none |
| 0xb → 0xb | S→C | VehicleDamageInfoPacket | FUN_142ecf2d0 / FUN_142f6df00 | same | not sent | none |
| 0xc → 0xc | S→C | VehicleStatUpdatePacket | FUN_142ed0200 / FUN_142f6ee30 | same | not sent | none |
| 0xd → 0xd | S→C | VehiclePacketUpdateWeapon | FUN_142ecfeb0 / FUN_142f6eae0 | same | not sent | none |
| 0xe → 0xe | S→C | VehicleRemovedFromQueuePacket | FUN_142ed1970 / FUN_142f705a0 | same | not sent | none |
| 0xf → 0xf | S→C | VehicleUpdateQueuePositionPacket | FUN_142ed1b30 / FUN_142f70760 | same | not sent | none |
| 0x10 → 0x10 | S→C | VehiclePadDestroyNotifyPacket | FUN_142ed18e0 / FUN_142f70510 | same | not sent | none |
| 0x11 → 0x11 | C→S | VehicleSetAutoDrivePacket | FUN_142ed1a00 FUN_142f878c0 / FUN_142f70630 FUN_1430276f0 | same | not handled | none |
| 0x12 → 0x12 | S→C | VehicleLockOnStatePacket | FUN_142ecf980 / FUN_142f6e5b0 | same | not sent | none |
| 0x13 → 0x13 | S→C | VehicleTrackingStatePacket | FUN_142ed05b0 / FUN_142f6f1e0 | same | not sent | none |
| 0x14 → 0x14 | S→C | VehicleCounterMeasureStatePacket | FUN_142ecf210 / FUN_142f6de40 | same | not sent | none |
| 0x16 → 0x16 | S→C | VehicleAcquireStatePacket | FUN_142ecef40 / FUN_142f6db70 | same | not sent | none |
| 0x17 → 0x17 | C→S | VehiclePacketDismissVehicle | FUN_142ed13a0 / FUN_142f6ffd0 | same | not handled | none |
| 0x18 → 0x18 | C→S | VehicleAutoMountPacket | FUN_142ed1c50 FUN_143276070 / FUN_142f70880 FUN_143322f20 | same | not handled | none |
| 0x1a → 0x1a | C→S | VehicleDeployPacket | FUN_142f87a70 FUN_142ecf460 / FUN_1430278a0 FUN_142f6e090 | same | not handled | none |
| 0x1b → 0x1b | C→S | VehicleHeadLightPacket | FUN_142ecf740 FUN_142f87c60 / FUN_142f6e370 FUN_143027a90 | same | not handled | none |
| 0x1c → 0x1c | C→S | VehicleAccessTypePacket | FUN_142ecee80 / FUN_142f6dab0 | same | not handled | none |
| 0x1d → 0x1d | C→S | VehicleKickPlayerPacket | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not handled | none |
| 0x1e → 0x1e | S→C | VehicleHealthUpdateOwner | FUN_142ecf830 / FUN_142f6e460 | same | not sent | none |
| 0x21 → 0x21 | S→C | VehicleNoAccessPacket | FUN_142ed2210 / FUN_142f70e40 | same | not sent | none |
| 0x23 → 0x23 | S→C | VehicleGroupPacket | FUN_142ecf640 / FUN_142f6e270 | same | not sent | none |
| 0x24 → 0x24 | C→S | VehicleDeployResponsePacket | FUN_142ed1eb0 FUN_142ed7360 / FUN_142f70ae0 FUN_142f75f90 | same | not handled | none |
| 0x25 → 0x25 | C→S | VehicleExitPointsPacket | FUN_142f87710 / FUN_143027540 | same | not handled | none |
| 0x26 → 0x26 | S→C | VehicleControllerLogOutPacket | FUN_142ed1d10 / FUN_142f70940 | same | not sent | none |

### 0xC2 Factions

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | S→C | Factions::FactionPacketLoadFactionDefinitionManager | FUN_14336db80 FUN_143b4a040 / FUN_143419c40 FUN_143c05080 | same | not sent | none |

### 0xC3 PacketSynchronization

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | C→S | PacketSynchronization | FUN_142bab170 / FUN_142c44cf0 FUN_142c24b00 | same | handled (Network/Handlers/MiscHandler.cs:318) | none |

### 0xC6 Leaderboard

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | C→S | Leaderboard::LeaderboardPacketAdminFillLeaderboard | FUN_143256fb0 / FUN_143303e60 | same | not handled | none |
| 0x2 → 0x2 | C→S | Leaderboard::LeaderboardPacketGetPlayerStats | FUN_143257150 / FUN_143304000 | same | not handled | none |
| 0x4 → 0x4 | C→S | Leaderboard::LeaderboardPacketAdminClearPlayerStats | FUN_143256e10 / FUN_143303cc0 | same | not handled | none |

### 0xC7 PlayerUpdateManagedPosition

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | C→S | PlayerUpdateManagedPosition | – / – | same: own family 0xC7 (not PlayerUpdate); send builder 142c0bb80→142ca5960 differs in assert line / struct offset only | handled (Network/Handlers/MiscHandler.cs:95) | none |

### 0xC9 PlayerUpdate

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | C→S | PlayerUpdatePacketUpdateVehicleWeapon | FUN_142b934a0 FUN_142f87570 / FUN_142c2cf90 FUN_1430273a0 | same | not handled | none |

### 0xCA ProfileStats

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x6 → 0x6 | C→S | ProfileStatsPacketGetLeaderboard | FUN_143257300 / FUN_1433041b0 | same | not handled | none |
| 0x8 → 0x8 | C→S | ProfileStatsPacketGetPlayerLeaderboardRow | FUN_1432574a0 / FUN_143304350 | same | not handled | none |

Id-table entries with no client class (no reader / writer compiled; server-to-server or unused): cProfileStatsPacketIdGetPlayerProfileStats 0x0→0x0; cProfileStatsPacketIdGetZoneCharacterStats 0xa→0xa; cProfileStatsPacketIdLeaderboard 0x7→0x7; cProfileStatsPacketIdPlayerLeaderboardRow 0x9→0x9; cProfileStatsPacketIdPlayerProfileStats 0x2→0x2; cProfileStatsPacketIdZoneCharacterStats 0xb→0xb; cProfileStatsPacketIdZonePlayerProfileStats 0x3→0x3

### 0xCB Equipment

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | Equipment::EquipmentPacketSetCharacterEquipment | FUN_142da3ac0 / FUN_142e41a90 | same | not sent | none |
| 0x0 → 0x0 | S→C | Equipment::EquipmentPacketSetCharacterEquipmentSlot | FUN_142da3b70 / FUN_142e41b40 | same | not sent | none |
| 0x0 → 0x0 | S→C | Equipment::EquipmentPacketSetCharacterEquipmentSlots | FUN_142da3ce0 / FUN_142e41cb0 | same | not sent | none |

### 0xCD PacketGetRespawnLocations

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | C→S | PacketGetRespawnLocations | FUN_143385ae0 / FUN_1434329b0 | same | not handled | none |

### 0xCE WallOfDataPlayerKeyboard

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x2 → 0x2 | C→S | WallOfDataPlayerKeyboardPacket | FUN_142babe50 / FUN_142c459d0 | same | not handled | none |
| 0x4 → 0x4 | C→S | WallOfDataUIEventPacket | FUN_1435237b0 / FUN_1435d5130 | same | not handled | none |
| 0x6 → 0x6 | C→S | WallOfDataVoiceChatEventPacket | FUN_142f26020 / FUN_142fc5510 | same | not handled | none |
| 0x8 → 0x8 | C→S | WallOfDataMarketplacePacket | FUN_143523610 / FUN_1435d4f90 | same | not handled | none |

### 0xCF ThrustPadPlayerEntered

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x3 → 0x3 | C→S | ThrustPadPlayerEnteredPacket | FUN_142de7270 / FUN_142e85250 | same | not handled | none |

### 0xD0 PacketClientInGamePurchase

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | C→S | PacketClientInGamePurchase | FUN_14353a150 / FUN_1435ec120 | same | not handled | none |

### 0xD1 Missions

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | Missions::ClientMissionPacketSetMissionManager | FUN_142e6de90 / FUN_142f0ca20 | same | not sent | none |
| 0x0 → 0x0 | S→C | Missions::MissionPacketAddActiveMission | FUN_142e6a910 / FUN_142f094a0 | same | not sent | none |
| 0x0 → 0x0 | S→C | Missions::MissionPacketAddAvailableMission | FUN_142e6aaa0 / FUN_142f09630 | same | not sent | none |
| 0x0 → 0x0 | S→C | Missions::MissionPacketAddGlobalAvailableMission | FUN_142e6df50 / FUN_142f0cae0 | same | not sent | none |
| 0x0 → 0x0 | S→C | Missions::MissionPacketRemoveActiveMission | FUN_142e6acf0 / FUN_142f09880 | same | not sent | none |
| 0x0 → 0x0 | S→C | Missions::MissionPacketRemoveGlobalAvailableMission | FUN_142e6adb0 / FUN_142f09940 | same | not sent | none |
| 0x0 → 0x0 | S→C | Missions::MissionPacketReportCompletedMission | FUN_142e6ae70 / FUN_142f09a00 | same | not sent | none |
| 0x0 → 0x0 | S→C | Missions::MissionPacketSetMissionInstanceManager | FUN_142e6af30 / FUN_142f09ac0 | same | not sent | none |
| 0x1 → 0x1 | C→S | Missions::MissionPacketConquerZone | FUN_142e6a550 / FUN_142f090e0 | same | not handled | none |
| 0x1 → 0x1 | C→S | Missions::MissionPacketListMissions | FUN_142e6ac30 / FUN_142f097c0 | same | not handled | none |

### 0xD6 PacketSecurity

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | PacketSecurity | FUN_142be2d00 / FUN_142c7c880 | same | not sent | none |

### 0xDC HudPackets

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | C→S | HudPackets::PacketRadialMenuRequest | FUN_1432e3d40 / FUN_143390b30 | same | family handler, sub not handled (Network/Handlers/MiscHandler.cs:107) | none |
| 0x2 → 0x2 | S→C | HudPackets::PacketRadialMenuResponse | FUN_143670ee0 / FUN_14372cdf0 | same | sent (Game/RightClickMenus.cs:81) | none |

### 0xDD SetSpeechPack

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | C→S | SetSpeechPackPacket | FUN_142f26500 / FUN_142fc59f0 | same | family handler, sub not handled (Network/Handlers/MiscHandler.cs:162) | none |
| 0x2 → 0x2 | C→S | GetSpeechPackListPacket | FUN_142f261c0 / FUN_142fc56b0 | same | family handler, sub not handled (Network/Handlers/MiscHandler.cs:162) | none |
| 0x4 → 0x4 | S→C | PlayerFilterUpdatePacket | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |

### 0xDE AcquireTimers

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | AcquireTimers::AcquireTimerPacketSetAcquireTimerManager | FUN_142d96fa0 / FUN_142e34f70 | same | not sent | none |

### 0xDF PlayerUpdate

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | PlayerUpdatePacketGuildTag | FUN_142bb2640 / FUN_142c4c0a0 | same | not sent | none |

### 0xE0 PacketLoginQueueStatus

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | PacketLoginQueueStatus | FUN_1430c2590 / FUN_14316d6d0 | same | not sent | none |

### 0xE1 PacketServerPopulationInfo

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | PacketServerPopulationInfo | FUN_142bb1190 / FUN_142c4ad00 | same | not sent | none |

### 0xE3 PlayerUpdateVehicleCollision

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | C→S | PlayerUpdateVehicleCollision | FUN_142bb4ac0 FUN_1433d1200 / FUN_142c4e2d0 FUN_14347f2f0 | same | not handled | none |

### 0xE4 PlayerUpdate

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | PlayerUpdatePacketStop | FUN_142bb4160 / FUN_142c4d970 | same | not sent | none |

### 0xE5 Currency

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | Currency::CurrencyPacketSetCurrencyRateTier | FUN_142db13e0 / FUN_142e4f3b0 | same | not sent | none |

### 0xE6 Items

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | C→S | Items::ItemPacketRequestTrialItemLine | FUN_142e607c0 FUN_142e60430 / FUN_142eff350 FUN_142efefc0 | same | not handled | none |
| 0x0 → 0x0 | S→C | Items::ItemPacketSetItemTimerManager | FUN_142e60a70 / FUN_142eff600 | same | not sent | none |
| 0x0 → 0x0 | S→C | Items::ItemPacketSetItemTrialLockTimer | FUN_142e60be0 / FUN_142eff770 | same | not sent | none |
| 0x1 → 0x1 | C→S | Items::ItemPacketAddItemLineTimer | FUN_142e60290 / FUN_142efee20 | same | not handled | none |

### 0xE7 PlayerUpdate

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | C→S | PlayerUpdatePacketAttachObject | FUN_142bb1dd0 FUN_142ee1020 / FUN_142c4b840 FUN_142f7fc80 | same | not handled | none |

### 0xE8 PlayerUpdate

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | C→S | PlayerUpdatePacketDetachObject | FUN_142bb2330 FUN_142ee11d0 / FUN_142c4bda0 FUN_142f7fe30 | same | not handled | none |

### 0xE9 PacketClientSettings

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | PacketClientSettings | FUN_142bb06d0 / FUN_142c4a240 | same | sent (Network/Handlers/MiscHandler.cs:271) | none |

### 0xEA AreaDefinitionTransferShape

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | S→C | AreaDefinitionSetDefinitionsPacket | FUN_14324b120 / FUN_1432f7fd0 | same | sent (Network/Handlers/MiscHandler.cs:205; Network/Handlers/MiscHandler.cs:278) | none |
| 0x2 → 0x2 | C→S | AreaDefinitionTransferShapePacket | FUN_14324bb00 / FUN_1432f89b0 | same | not handled | none |
| 0x3 → 0x3 | C→S | AreaDefinitionUpdateDefinitionPacket | FUN_14324bcc0 / FUN_1432f8b70 | same | not handled | none |
| 0x4 → 0x4 | C→S | AreaDefinitionBasePacket | FUN_14324b950 FUN_14324bcc0 / FUN_1432f8800 FUN_1432f8b70 | same | not handled | none |
| 0x4 → 0x4 | C→S | AreaDefinitionRemoveDefinitionPacket | FUN_14324b950 / FUN_1432f8800 | same | not handled | none |
| 0x6 → 0x6 | S→C | AreaDefinitionRequestResponsePacket | FUN_14324bfb0 / FUN_1432f8e60 | same | not sent | none |
| 0x7 → 0x7 | C→S | AreaDefinitionCreateTriggerAreaRequestPacket | FUN_1430f3a70 / FUN_14319eb10 | same | not handled | none |
| 0x7 → 0x7 | S→C | BaseAreaDefinitionPacket | FUN_14324c130 FUN_14324b120 / FUN_1432f8fe0 FUN_1432f7fd0 | same | not sent | none |
| 0x8 → 0x8 | S→C | AreaDefinitionCreateTriggerAreaResponsePacket | FUN_14324aa20 / FUN_1432f78d0 | same | not sent | none |

### 0xEC TerrainCell

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | S→C | BaseTerrainClientCellPacket | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |
| 0x1 → 0x1 | C→S | TerrainCellPacketClientCellRequest | FUN_14309e870 / FUN_143149190 | same | handled (Network/Handlers/TerrainHandler.cs:20; Network/Message/Model/TerrainCellClientCellRequest.cs:10) | none |
| 0x2 → 0x2 | C→S | TerrainCellPacketClientPendingRequestTimeout | FUN_14309ea20 / FUN_143149340 | same | handled (Network/Handlers/TerrainHandler.cs:31; Network/Message/Model/TerrainCellPendingRequestTimeout.cs:11) | none |
| 0x3 → 0x3 | S→C | TerrainCellPacketClientCellUpdateResponse | FUN_14309bcc0 / FUN_1431465e0 | same | sent (Game/Building.cs:539) | none |
| 0x4 → 0x4 | S→C | TerrainCellPacketUpToDateCells | FUN_14309f380 / FUN_143149ca0 | same | sent (Network/Message/Model/TerrainCellUpToDateCells.cs:11) | none |
| 0x5 → 0x5 | S→C | TerrainCellPacketCellsUpdate | FUN_14309bbd0 / FUN_1431464f0 | same | sent (Game/DevCommands.cs:203; Game/DevCommands.cs:249; Game/Terrain/TerrainEdits.cs:1302) | none |
| 0x6 → 0x6 | S→C | TerrainCellPacketMaterialChangeInfo | FUN_14309bfd0 / FUN_1431468f0 | same | not sent | none |
| 0x7 → 0x7 | S→C | TerrainCellPacketCellBlockDamage | FUN_14309bb00 / FUN_143146420 | same | sent (Game/DevCommands.cs:202; Game/DevCommands.cs:224) | none |
| 0x8 → 0x8 | C→S | TerrainCellPacketResetAllCells | FUN_14309ebd0 / FUN_1431494f0 | same | not handled | none |
| 0x9 → 0x9 | C→S | TerrainCellPacketResetTemporaryCells | FUN_14309ed80 / FUN_1431496a0 | same | not handled | none |
| 0xa → 0xa | S→C | TerrainCellPacketModifiedCellManifest | FUN_14309c0c0 / FUN_1431469e0 | same | sent (Game/Terrain/TerrainStore.cs:371; Network/WorldSession.cs:1019) | none |
| 0xb → 0xb | S→C | TerrainCellPacketModifiedCellManifestUpdate | FUN_14309c1b0 / FUN_143146ad0 | same | sent (Game/Terrain/TerrainStore.cs:347; Game/Terrain/TerrainStore.cs:361) | none |
| 0x10 → 0x10 | S→C | TerrainCellPacketDetectVeins | FUN_14309bd90 / FUN_1431466b0 | same | sent (Game/Terrain/VeinOverlay.cs:70) | none |

Id-table entries with no client class (no reader / writer compiled; server-to-server or unused): cTerrainCellPacketIdAdminEnableMaterialVeins 0xe→0xe; cTerrainCellPacketIdAdminResetMaterialVeins 0xf→0xf; cTerrainCellPacketIdAdminSetBotValues 0xd→0xd; cTerrainCellPacketIdEndDetectVeins 0x11→0x11; cTerrainCellPacketIdNone 0x0→0x0; cTerrainCellPacketIdZoneProcessingComplete 0xc→0xc

### 0xED Prop

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → 0x1d | C→S | PropPacketDialogResponse | – / – | new | not handled (family handler default branch) (Network/Handlers/PropHandler.cs:22) | implement (0.695 only) |
| 0x0 → 0x0 | S→C | PropBasePacket | FUN_142bb4c10 / FUN_142c4e420 | same | sent (Game/Props.cs:1124; Game/Props.cs:1164) | none |
| 0x1 → 0x1 | C→S | PropAccessPacket | FUN_142f0a7d0 / FUN_142fa9500 | same | handled (Network/Handlers/PropHandler.cs:28) | none |
| 0x2 → 0x2 | S→C | PropAccessReplyPacket | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | sent (Game/Props.cs:1673; Game/Props.cs:362; Game/Props.cs:368) | none |
| 0x2 → 0x2 | S→C | PropPacketFromZoneToClient | FUN_142f08e10 FUN_142f098a0 / FUN_142fa7b40 FUN_142fa85d0 | body: top level equal (FUN_142fa7b40 vs 142f08e10, i32→u32 only); the elements of its two lists (FUN_142c364f0 / FUN_142c36fe0) carry the settings record, f64 in 0.695 (FUN_142c15ee0 vs 142b7c740; storytools-695 §1.4). Server writes both lists empty (Props.cs:574-575), so wire-compatible as sent today | sent (Game/Props.cs:1673; Game/Props.cs:362; Game/Props.cs:368) | per-build body |
| 0x3 → 0x3 | C→S | PropSavePacket | FUN_142f0b880 / FUN_142faa5b0 | same | handled (Network/Handlers/PropHandler.cs:46) | none |
| 0x4 → 0x4 | S→C | PropSaveReplyPacket | FUN_142f0c0e0 / FUN_142faae10 | same | sent (Game/Props.cs:759; Game/Props.cs:828; Game/Props.cs:848) | none |
| 0x5 → 0x5 | C→S | PropPickUpPacket | FUN_142f0b4f0 / FUN_142faa220 | same | handled (Network/Handlers/PropHandler.cs:49) | none |
| 0x6 → 0x6 | S→C | PropPickUpReplyPacket | FUN_142f09c10 / FUN_142fa8940 | value: reader same; new error 0xE Prop.PropPickupErrorPartialLootC... (Prop dispatcher FUN_142fba9f0 case 6) | not sent | new value only (server may send it) |
| 0x7 → 0x7 | C→S | PropDeletePacket | FUN_142f0a9d0 / FUN_142fa9700 | same | handled (Network/Handlers/PropHandler.cs:50; Network/Handlers/PropHandler.cs:52) | none |
| 0x8 → 0x8 | S→C | PropDeleteReplyPacket | FUN_142f09c10 / FUN_142fa8940 | same | not sent | none |
| 0x9 → 0x9 | C→S | PropPacketRequestList | FUN_142f0b0b0 / FUN_142fa9de0 | same | family handler, sub not handled (Network/Handlers/PropHandler.cs:22) | none |
| 0xa → 0xa | S→C | PropPacketListReply | FUN_142f0bc80 / FUN_142faa9b0 | same | sent (runtime opcode_stats (trace/families; agent_packets/sent_s2c.tsv)) | none |
| 0xb → 0xb | S→C | PropPacketVolumeReply | FUN_142f0be10 / FUN_142faab40 | same | sent (runtime opcode_stats (trace/families; agent_packets/sent_s2c.tsv)) | none |
| 0xc → 0xc | C→S | PropProcessorStateNotificationPacket | FUN_142f0b6e0 / FUN_142faa410 | same | handled (Network/Handlers/PropHandler.cs:86) | none |
| 0xd → 0xd | S→C | PropProcessorStateReplicationPacket | FUN_142f0bfd0 / FUN_142faad00 | same | not sent | none |
| 0xf → 0xf | C→S | PropDuplicatePacket | FUN_142f0ab80 / FUN_142fa98b0 | same | handled (Network/Handlers/PropHandler.cs:61) | none |
| 0x10 → 0x10 | S→C | PropPacketUndoRedoNotification | FUN_142f09990 / FUN_142fa86c0 | same | sent (Game/Props.cs:211) | none |
| 0x12 → 0x12 | C→S | PropPacketPlacePropById | FUN_142f0aed0 / FUN_142fa9c00 | same | handled (Network/Handlers/PropHandler.cs:32) | none |
| 0x13 → 0x13 | C→S | PropPacketUpdatePropTextRequest | FUN_142f0b340 / FUN_142faa070 | same | handled (Network/Handlers/PropHandler.cs:65) | none |
| 0x14 → 0x14 | C→S | PropPacketEditPropTextRequest | FUN_143549e60 / FUN_1435fc680 | same | handled (Network/Handlers/PropHandler.cs:69) | none |
| 0x15 → 0x15 | S→C | PropPacketEditPropTextReply | FUN_142f097d0 / FUN_142fa8500 | same | sent (Game/Props.cs:1038) | none |
| 0x16 → 0x16 | C→S | PropPacketSetTintRequest | FUN_14347fbe0 / FUN_143531a80 | same | handled (Network/Handlers/PropHandler.cs:54) | none |
| 0x17 → 0x17 | S→C | PropPacketAvailableTintsPacketReply | FUN_142f09c10 / FUN_142fa8940 | same | not sent | none |
| 0x18 → 0x18 | C→S | PropPacketAvailableTintsPacketRequest | FUN_14347fa40 / FUN_1435318e0 | same | handled (Network/Handlers/PropHandler.cs:57) | none |
| 0x19 → 0x19 | C→S | PropPacketSetPropName | FUN_14354a200 / FUN_1435fca20 | same | handled (Network/Handlers/PropHandler.cs:73) | none |
| 0x1a → 0x1a | C→S | PropPacketSetPropEnable | FUN_14354a010 / FUN_1435fc830 | same | handled (Network/Handlers/PropHandler.cs:77) | none |
| 0x1b → 0x1b | C→S | PropPacketEditSettingsRequest | FUN_142f0ad20 / FUN_142fa9a50 | same | handled (Network/Handlers/PropHandler.cs:81) | none |
| 0x1c → 0x1c | S→C | PropPacketEditSettingsReply | – / – | body: Prop dispatcher case 0x1C changed (FUN_142fba9f0 vs 142f1b980); f64 records + option strings (storytools-695 §1.4, §2) | sent (Game/PropSettings.cs:219) | per-build body |

Id-table entries with no client class (no reader / writer compiled; server-to-server or unused): cPropPacketIdBulkCreate 0xe→0xe; cPropPacketIdMultiPropPlacementPreview 0x11→0x11; cPropPacketIdNpcDialogResponse –→0x1d

### 0xEE Projectiles

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | Projectiles::ProjectilePacketDebug | FUN_1430e2060 / FUN_14318d160 | same | not sent | none |
| 0x1 → 0x1 | S→C | Projectiles::ProjectilePacketCreate | FUN_1430e1d10 / FUN_14318ce10 | same | not sent | none |
| 0x2 → 0x2 | S→C | Projectiles::ProjectilePacketUpdate | FUN_1430e2380 / FUN_14318d480 | same | not sent | none |
| 0x3 → 0x3 | S→C | Projectiles::ProjectilePacketDelete | FUN_1430e2220 / FUN_14318d320 | same | not sent | none |

### 0xEF PlacedEffectDebug

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | S→C | PlacedEffectDebugPacket | FUN_142eed420 / FUN_142f8c110 | same | not sent | none |
| 0x2 → 0x2 | S→C | PlacedEffectCreatePacket | FUN_142eecf60 / FUN_142f8bc50 | same | not sent | none |
| 0x3 → 0x3 | S→C | PlacedEffectDestroyPacket | FUN_142eed1a0 / FUN_142f8be90 | same | not sent | none |

### 0xF0 Template

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → 0x1f | C→S | TemplatePacketPurchaseMissingTemplateMaterials | – / – | new | not handled (family handler default branch) (Network/Handlers/InteractionHandler.cs:185) | implement (0.695 only) |
| – → 0x1d | C→S | TemplatePacketPurchaseTemplateMaterialsDataRequest | – / – | new | not handled (family handler default branch) (Network/Handlers/InteractionHandler.cs:185) | implement (0.695 only) |
| – → 0x1e | S→C | TemplatePacketPurchaseTemplateMaterialsDataResponse | – / – | new | not sent | implement (0.695 only) |
| 0x1 → 0x1 | C→S | TemplatePacketClientListRequest | FUN_143059f60 / FUN_143101eb0 | same | handled (Game/Templates.cs:497) | none |
| 0x3 → 0x3 | S→C | TemplatePacketSaveTemplateResponse | FUN_14305a870 / FUN_143102bd0 | body: TemplateBasicData without PropData list (FUN_1430ff530 vs 143058180; §B4) | sent (Game/Templates.cs:421; Game/Templates.cs:83) | per-build body |
| 0x4 → 0x4 | C→S | TemplatePacketClientVoxelDataRequest | FUN_14305a2f0 / FUN_143102240 | same | handled (Game/Templates.cs:502) | none |
| 0x5 → 0x5 | S→C | TemplatePacketEnvironmentVoxelDataResponse | FUN_143058b60 / FUN_1430fff20 | body: PropData list moved out of TemplateBasicData to after the cell list (FUN_1430fff20 vs 143058b60; §B4) | sent (Game/Templates.cs:545) | per-build body |
| 0x6 → 0x6 | C→S | TemplatePacketClientDeleteTemplateRequest | FUN_143059c10 / FUN_143101b60 | same | handled (Game/Templates.cs:506) | none |
| 0x7 → 0x7 | S→C | TemplatePacketEnvironmentDeleteTemplateResponse | FUN_1430588b0 / FUN_1430ffc60 | same | sent (Game/Templates.cs:673; Game/Templates.cs:691) | none |
| 0xa → 0xa | C→S | TemplatePacketClientRequestRenameTemplate | FUN_14305a130 / FUN_143102080 | same | handled (Game/Templates.cs:509) | none |
| 0xb → 0xb | S→C | TemplatePacketClientResponseRenameTemplate | FUN_1430586c0 / FUN_1430ffa70 | same | sent (Game/Templates.cs:712; Game/Templates.cs:725) | none |
| 0xc → 0xc | S→C | TemplatePacketUpdateTemplateResponse | FUN_14305a910 / FUN_143102c70 | body: TemplateBasicData without PropData list (§B4) | sent (Game/Templates.cs:249) | per-build body |
| 0x11 → 0x11 | S→C | TemplatePacketEnvironmentAddOwnershipResponse | FUN_143058780 / FUN_1430ffb30 | same | sent (Game/Templates.cs:62) | none |
| 0x12 → 0x12 | C→S | TemplatePacketClientGetInstanceDetailsRequest | FUN_143059db0 / FUN_143101d00 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:185) | none |
| 0x13 → 0x13 | S→C | TemplatePacketEnvironmentInstanceDetailsResponse | – / – | body: carries TemplateBasicData (§B4, client side only) | not sent | (not implemented; 0.695 body differs) |
| 0x1b → 0x1b | C→S | TemplatePacketExportToItemRequest | FUN_14305a4f0 / FUN_143102440 | same | handled (Game/Templates.cs:512) | none |

### 0xF1 PacketClientFacialAnim

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | C→S | PacketClientFacialAnim | FUN_1433f95e0 / FUN_1434a71c0 | same | not handled | none |

### 0xF2 PacketZoneProximityFacialAnim

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | PacketZoneProximityFacialAnim | FUN_142bb1620 / FUN_142c4b190 | same | not sent | none |

### 0xF3 PacketClientFacialAnimSettings

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | C→S | PacketClientFacialAnimSettings | FUN_1432e2e70 / FUN_14338fc60 | same | not handled | none |

### 0xF4 VoxelConstruction

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | C→S | VoxelConstructionPacketAddTemporaryBlockRequest | FUN_1430b26c0 / FUN_14315c7e0 | same | handled (Network/Handlers/InteractionHandler.cs:176) | none |
| 0x1 → 0x1 | S→C | VoxelConstructionPacketApplyDiffRequest | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | sent (Game/Building.cs:304; Game/Building.cs:336; Game/Building.cs:616) | none |
| 0x2 → 0x2 | S→C | VoxelConstructionPacketClientSelectionAreaRequest | FUN_1430b41f0 / FUN_14315ec60 | same | sent (Game/Building.cs:118 AddTemporaryBlockSub) | none |
| 0x3 → 0x3 | C→S | VoxelConstructionPacketClientUndoRedoRequest | FUN_1430b2ba0 / FUN_14315ccc0 | same | handled (Game/Building.cs:118 UndoRedoSub) | none |
| 0x4 → 0x4 | C→S | VoxelConstructionPacketClientOwnershipRequest | FUN_14307f4a0 / FUN_143129db0 | same | handled (Game/Terrain/VoxelOwnership.cs:41 RequestSub) | none |
| 0x5 → 0x5 | S→C | VoxelConstructionPacketOffGridPasteClientRequest | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |
| 0x6 → 0x6 | S→C | VoxelConstructionPacketClipboardData | FUN_1430b0d80 / FUN_14315b440 | same | sent (Game/Building.cs:290 ClipboardDataSub) | none |
| 0x8 → 0x8 | S→C | VoxelConstructionPacketZoneUnknownPropList | FUN_1430b3240 / FUN_14315d360 | same | not sent | none |
| 0x9 → 0x9 | S→C | VoxelConstructionPacketZoneMissingPropList | FUN_1430b31a0 / FUN_14315d2c0 | same | sent (Game/BuildingClipboard.cs:550) | none |
| 0xa → 0xa | S→C | VoxelConstructionPacketResponseObserved | FUN_1430b11e0 / FUN_14315b8a0 | same | sent (Game/Building.cs:520) | none |
| 0x16 → 0x16 | S→C | VoxelConstructionPacketEnvOwnershipUpToDateResponse | FUN_1430b3060 / FUN_14315d180 | same | sent (Game/Terrain/VoxelOwnership.cs:292) | none |
| 0x17 → 0x17 | S→C | VoxelConstructionPacketEnvOwnershipDataResponse | FUN_1430b0ed0 / FUN_14315b590 | same | sent (Game/Terrain/VoxelOwnership.cs:295) | none |
| 0x1b → 0x1b | S→C | VoxelConstructionPacketZoneDisplayNotification | FUN_1430b1310 / FUN_14315b9d0 | same | not sent | none |

Id-table entries with no client class (no reader / writer compiled; server-to-server or unused): cVoxelConstructionPacketIdResponse 0x7→0x7

### 0xF5 Palette

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | C→S | PalettePacketCreate | FUN_142e723f0 / FUN_142f10f80 | same | family handler, sub not handled (Game/Palettes.cs:78) | none |
| 0x2 → 0x2 | C→S | PalettePacketDelete | FUN_142e72590 / FUN_142f11120 | same | family handler, sub not handled (Game/Palettes.cs:78) | none |
| 0x3 → 0x3 | C→S | PalettePacketAddEntry | FUN_142e72250 / FUN_142f10de0 | same | handled (Game/Palettes.cs:85) | none |
| 0x4 → 0x4 | C→S | PalettePacketDeleteEntry | FUN_142e72740 / FUN_142f112d0 | same | handled (Game/Palettes.cs:94) | none |
| 0x7 → 0x7 | C→S | PalettePacketSlotUpdates | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | handled (Game/Palettes.cs:106) | none |

### 0xF6 PacketCharacterSelectSessionRequest

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0xfffffffe → 0xfffffffe | C→S | PacketCharacterSelectSessionRequest | FUN_142ba96d0 / FUN_142c43250 | same | family handler, sub not handled (Network/Handlers/CharacterSelectHandler.cs:36) | none |

### 0xF7 PacketCharacterSelectSessionResponse

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | PacketCharacterSelectSessionResponse | FUN_142bb57e0 / FUN_142c4eff0 | same | sent (Network/Message/Model/CharacterSelectSession.cs:22) | none |

### 0xF8 Mail

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | C→S | PacketMailComposeRequest | FUN_143583b50 / FUN_143637350 | same | handled (Game/Mail.cs:95) | none |
| 0x4 → 0x5 | S→C | PacketMailComposeResponse | FUN_1432c8190 / FUN_143374fc0 | renumbered | sent (Game/Mail.cs:370) | use sub 0x5 for 0.695 |
| 0x6 → 0x7 | C→S | PacketMailInboxListRequest | FUN_1435829e0 / FUN_1436361e0 | renumbered | handled (Game/Mail.cs:92) | use sub 0x7 for 0.695 |
| 0x7 → 0x8 | S→C | PacketMailInboxListResponse | FUN_1432c75d0 / FUN_143374400 | renumbered | sent (Game/Mail.cs:146) | use sub 0x8 for 0.695 |
| 0x8 → 0x9 | C→S | PacketMailReadMessageRequest | FUN_143582b80 / FUN_143636380 | renumbered | handled (Game/Mail.cs:98) | use sub 0x9 for 0.695 |
| 0x9 → 0xa | S→C | PacketMailReadMessageResponse | FUN_1432c77e0 / FUN_143374610 | renumbered | sent (Game/Mail.cs:411) | use sub 0xa for 0.695 |
| 0xa → 0xb | C→S | PacketMailDeleteMessageRequest | FUN_143582830 / FUN_143636030 | renumbered | handled (Game/Mail.cs:101) | use sub 0xb for 0.695 |
| 0xb → 0xc | S→C | PacketMailDeleteMessageResponse | FUN_1432c7480 / FUN_1433742b0 | renumbered | sent (Game/Mail.cs:669) | use sub 0xc for 0.695 |
| 0xd → 0xe | S→C | PacketMailClearInboxResponse | FUN_1432c80e0 / FUN_143374f10 | renumbered | not sent | (not implemented; 0.695 number 0xe) |
| 0xe → 0xf | S→C | PacketMailNewMailInboxNotification | FUN_1432c8490 / FUN_1433752c0 | renumbered | sent (Game/Mail.cs:348; Game/Mail.cs:599; Game/Mail.cs:617) | use sub 0xf for 0.695 |
| 0xf → 0x10 | C→S | PacketMailConsumeAttachmentRequest | FUN_143582630 / FUN_143635e30 | renumbered | handled (Game/Mail.cs:107) | use sub 0x10 for 0.695 |
| 0x10 → 0x11 | S→C | PacketMailConsumeAttachmentResponse | FUN_1432c73a0 / FUN_1433741d0 | renumbered | sent (Game/Mail.cs:561) | use sub 0x11 for 0.695 |
| 0x13 → 0x14 | S→C | PacketMailEnableWindow | FUN_1432c8360 / FUN_143375190 | renumbered | sent (Game/Mail.cs:82) | use sub 0x14 for 0.695 |
| 0x14 → 0x15 | C→S | PacketMailAttachItemToComposeRequest | FUN_1435821d0 / FUN_1436359d0 | renumbered | handled (Game/Mail.cs:104) | use sub 0x15 for 0.695 |
| 0x15 → 0x16 | S→C | PacketMailAttachItemToComposeResponse | FUN_1432c70e0 / FUN_143373f10 | renumbered | sent (Game/Mail.cs:646) | use sub 0x16 for 0.695 |

Id-table entries with no client class (no reader / writer compiled; server-to-server or unused): cMailPacketIdClearInboxRequest 0xc→0xd; cMailPacketIdComposeResponseReturnAttachments 0x5→0x6; cMailPacketIdConsumeAttachmentZoneRequest 0x11→0x12; cMailPacketIdConsumeAttachmentZoneResponse 0x12→0x13; cMailPacketIdDeveloperMailPlayer –→0x4; cMailPacketIdNone 0x0→0x0; cMailPacketIdSystem 0x16→0x17

### 0xF9 Claims

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | C→S | ClaimPacketIdSnapshotRestoreRequest | FUN_142d051a0 / FUN_142da50e0 | same | handled (Network/Handlers/ClaimsHandler.cs:14) | none |
| – → 0x63 | C→S | ClaimsPacketPlayerEnteredClaim | – / – | new | not handled (family handler default branch) (Network/Handlers/ClaimsHandler.cs:14) | implement (0.695 only) |
| – → 0x45 | C→S | ClaimsPacketSetAdventureFlags | – / – | new | not handled (family handler default branch) (Network/Handlers/ClaimsHandler.cs:14) | implement (0.695 only) |
| – → 0x46 | C→S | ClaimsPacketStartClaimPlacement | – / – | new | not handled (family handler default branch) (Network/Handlers/ClaimsHandler.cs:14) | implement (0.695 only) |
| 0x1 → 0x1 | S→C | ClaimsPacketsIdSendAllClaimBasicInfo | FUN_142d08ce0 / FUN_142da9170 | same | sent (Game/Claims.cs:349) | none |
| 0x2 → 0x2 | S→C | ClaimsPacketsIdUpdateClaimBasicInfo | FUN_142d08e20 / FUN_142da92b0 | same | sent (Game/Claims.cs:475) | none |
| 0x3 → 0x3 | S→C | ClaimsPacketsIdUpdateClaimBasicInfoAndAOI | FUN_142d08ef0 / FUN_142da9380 | body: claim record: site-game trailing u32 + trailing u64 adventure flags (FUN_142da1a50 / 142d9fc20; §B5) | not sent | (not implemented; 0.695 body differs) |
| 0x4 → 0x4 | S→C | ClaimsPacketAOIUpdate | FUN_142d00720 / FUN_142da0530 | body: claim record + site-game changes (§B5) | sent (Game/Claims.cs:171; Game/Claims.cs:725; Game/Claims.cs:727) | per-build body |
| 0x5 → 0x5 | S→C | ClaimsPacketClaimInfoUpdate | – / – | body: site-game trailing u32 + trailing u64; new kind 0x17 = adventure flags (FUN_142da2840; §B5) | sent (Game/Claims.cs:1020; Game/Claims.cs:1028; Game/Claims.cs:1392) | per-build body |
| 0x6 → 0x6 | S→C | ClaimsPacketPlaceClaimResponse | FUN_142d01130 / FUN_142da0f40 | same | sent (Game/Claims.cs:402; Game/Claims.cs:480) | none |
| 0x7 → 0x7 | S→C | ClaimsPacketPlaceClaimComplete | FUN_142d08a50 / FUN_142da8ee0 | body: claim record + site-game changes (§B5) | sent (Game/Claims.cs:627) | per-build body |
| 0x8 → 0x8 | S→C | ClaimsPacketClaimManagmentData | FUN_142d00990 / FUN_142da07a0 | same | sent (Game/Claims.cs:515) | none |
| 0x9 → 0x9 | S→C | ClaimsPacketEditClaimItemData | FUN_142d00c30 / FUN_142da0a40 | body: claim record + site-game changes (§B5) | sent (Game/Claims.cs:616) | per-build body |
| 0xa → 0xa | S→C | ClaimsPacketUpkeepManagmentData | FUN_142d014e0 / FUN_142da12f0 | same | sent (Game/Claims.cs:560) | none |
| 0xc → 0xc | S→C | ClaimsPacketAddUpkeepFundsResponse | FUN_142d007e0 / FUN_142da05f0 | same | not sent | none |
| 0xd → 0xd | S→C | ClaimsPacketIdSendInventoryDetails | FUN_142d00f60 / FUN_142da0d70 | same | not sent | none |
| 0xf → 0xf | S→C | ClaimsPacketIdSendClaimsByOwner | FUN_142d08870 / FUN_142da8d00 | same | sent (Game/Claims.cs:662) | none |
| 0x10 → 0x10 | S→C | ClaimsPacketResultResponse | FUN_142d012c0 / FUN_142da10d0 | same | sent (Game/Claims.cs:1208) | none |
| 0x11 → 0x11 | S→C | ClaimsPacketIdRecievePropExclusionArea | FUN_142d00df0 / FUN_142da0c00 | same | sent (Game/Claims.cs:325) | none |
| 0x12 → 0x12 | S→C | ClaimsPacketLostClaimServerConnection | FUN_142d089c0 / FUN_142da8e50 | same | not sent | none |
| 0x13 → 0x13 | S→C | ClaimPacketIdSnapshotSaveConfirmRequest | FUN_142d08350 / FUN_142da87e0 | same | not sent | none |
| 0x14 → 0x14 | S→C | ClaimPacketIdSnapshotSaveResponse | FUN_142d083c0 / FUN_142da8850 | same | not sent | none |
| 0x15 → 0x15 | S→C | ClaimPacketIdSnapshotRestoreConfirmRequest | FUN_142d08270 / FUN_142da8700 | same | not sent | none |
| 0x16 → 0x16 | S→C | ClaimPacketIdSnapshotRestoreResponse | FUN_142d082e0 / FUN_142da8770 | same | not sent | none |
| 0x18 → 0x18 | S→C | ClaimsPacketContestEntryResults | FUN_142d08640 / FUN_142da8ad0 | same | sent (Game/Contests.cs:219) | none |
| 0x19 → 0x19 | C→S | ClaimsPacketsIdRequestAllClaimBasicInfo | FUN_142d07da0 / FUN_142da8080 | same | handled (Game/Claims.cs:286) | none |
| 0x1a → 0x1a | C→S | ClaimsPacketPlaceClaimRequest | FUN_142d06810 / FUN_142da6750 | same | handled (Game/Claims.cs:222) | none |
| 0x1b → 0x1b | C→S | ClaimsPacketPlaceClaimConfirm | FUN_142d06620 / FUN_142da6560 | same | handled (Game/Claims.cs:225) | none |
| 0x1c → 0x1c | C→S | ClaimsPacketClaimManagmentRequest | FUN_143534dd0 / FUN_1435e6da0 | same | handled (Game/Claims.cs:228) | none |
| 0x1e → 0x1e | C→S | ClaimsPacketRequestEditClaimItemData | FUN_142d06aa0 / FUN_142da69e0 | same | handled (Game/Claims.cs:292) | none |
| 0x22 → 0x22 | C→S | ClaimsPacketAddUpkeepFundsRequest | FUN_142d05970 / FUN_142da58b0 | same | family handler, sub not handled (Network/Handlers/ClaimsHandler.cs:14) | none |
| 0x23 → 0x23 | S→C | ClaimsPacketIdClaimRequestMoveItemToPlayer | FUN_1432d16a0 / FUN_14337e490 | same | not sent | none |
| 0x24 → 0x24 | C→S | ClaimsPacketCreateGroup | FUN_142d05b80 / FUN_142da5ac0 | same | family handler, sub not handled (Network/Handlers/ClaimsHandler.cs:14) | none |
| 0x25 → 0x25 | C→S | ClaimsPacketMoveItemToGroup | FUN_142d06270 / FUN_142da61b0 | same | family handler, sub not handled (Network/Handlers/ClaimsHandler.cs:14) | none |
| 0x27 → 0x27 | C→S | ClaimsPacketIdRequestClaimsByOwner | FUN_142d05f10 / FUN_142da5e50 | same | handled (Game/Claims.cs:297) | none |
| 0x2a → 0x2a | C→S | ClaimsPacketSetItemName | FUN_142d073e0 / FUN_142da7520 | same | handled (Game/Claims.cs:247) | none |
| 0x2b → 0x2b | C→S | ClaimsPacketSetItemDescription | FUN_142d071e0 / FUN_142da7320 | same | handled (Game/Claims.cs:255) | none |
| 0x2c → 0x2c | C→S | ClaimsPacketSetTagString | FUN_142d07990 / FUN_142da7ad0 | same | handled (Game/Claims.cs:263) | none |
| 0x2d → 0x2d | C→S | ClaimsPacketSetItemBasicPermissions | FUN_143534f70 / FUN_1435e6f40 | same | handled (Game/Claims.cs:271) | none |
| 0x2e → 0x2e | C→S | ClaimsPacketSetItemIndividualPermission | FUN_143535110 / FUN_1435e70e0 | same | handled (Game/Claims.cs:277) | none |
| 0x2f → 0x2f | C→S | ClaimsPacketDeleteClaimItem | FUN_142d05d70 / FUN_142da5cb0 | same | handled (Game/Claims.cs:236) | none |
| 0x30 → 0x30 | S→C | ClaimsPacketDeleteClaimItemReply | FUN_142d00b70 / FUN_142da0980 | same | sent (Game/Claims.cs:899; Game/Claims.cs:943; Game/Claims.cs:971) | none |
| 0x35 → 0x35 | C→S | ClaimsPacketIdRequestPropExclusionArea | FUN_142d060c0 / FUN_142da6000 | same | handled (Game/Claims.cs:323) | none |
| 0x37 → 0x37 | S→C | ClaimsPacketIdInventoryListDebugResponse | FUN_142d00d00 / FUN_142da0b10 | same | not sent | none |
| 0x38 → 0x38 | C→S | ClaimPacketIdSnapshotSaveRequest | FUN_142d05580 / FUN_142da54c0 | same | family handler, sub not handled (Network/Handlers/ClaimsHandler.cs:14) | none |
| 0x39 → 0x39 | C→S | ClaimPacketIdSnapshotSaveConfirmResponse | FUN_142d05390 / FUN_142da52d0 | same | family handler, sub not handled (Network/Handlers/ClaimsHandler.cs:14) | none |
| 0x3b → 0x3b | C→S | ClaimPacketIdSnapshotRestoreConfirmResponse | FUN_142d04fb0 / FUN_142da4ef0 | same | family handler, sub not handled (Network/Handlers/ClaimsHandler.cs:14) | none |
| 0x3c → 0x3c | C→S | ClaimPacketIdSnapshotUpdateName | FUN_142d05770 / FUN_142da56b0 | same | family handler, sub not handled (Network/Handlers/ClaimsHandler.cs:14) | none |
| 0x3d → 0x3d | C→S | ClaimsPacketSetAsAdminClaim | FUN_142d06e30 / FUN_142da6f70 | same | family handler, sub not handled (Network/Handlers/ClaimsHandler.cs:14) | none |
| 0x3e → 0x3e | C→S | ClaimsPacketSetSupplementalData | FUN_142d07780 / FUN_142da78c0 | same | family handler, sub not handled (Network/Handlers/ClaimsHandler.cs:14) | none |
| 0x40 → 0x40 | C→S | ClaimsPacketSetClaimTime | FUN_142d06fe0 / FUN_142da7120 | same | handled (Game/Claims.cs:300) | none |
| 0x41 → 0x41 | C→S | ClaimsPacketSubmitContestEntry | FUN_142d07b90 / FUN_142da7e70 | same | handled (Game/Claims.cs:314) | none |
| 0x42 → 0x42 | C→S | ClaimsPacketSetMusicOverrideId | FUN_142d075e0 / FUN_142da7720 | same | handled (Game/Claims.cs:309) | none |
| 0x43 → 0x43 | C→S | ClaimsPacketSetAdventureStatus | FUN_142d06c90 / FUN_142da6dd0 | same | handled (Game/Claims.cs:239) | none |
| 0x44 → 0x44 | C→S | ClaimsPacketOnClientRateClaim | – / – | same: sub 0x44 below the insertion; builder is the claims handler (its closure diffs are the §B5 readers) | handled (Game/Claims.cs:319) | none |
| 0x5e → 0x60 | C→S | ClaimsPacketIdDebugAddItemClaim | FUN_1432dfd00 / FUN_14338caf0 | renumbered | family handler, sub not handled (Network/Handlers/ClaimsHandler.cs:14) | (not implemented; 0.695 number 0x60) |
| 0x5f → 0x61 | S→C | ClaimsPacketUpdatePropCategoryCount | FUN_142d08c00 / FUN_142da9090 | renumbered | sent (Game/PropLimits.cs:78) | use sub 0x61 for 0.695 |

Id-table entries with no client class (no reader / writer compiled; server-to-server or unused): cClaimPacketIdInstancedGameUpdate 0x17→0x17; cClaimPacketIdSnapshotRestoreRequest 0x3a→0x3a; cClaimsPacketIdClaimConfirmMoveItemToPlayer 0xe→0xe; cClaimsPacketIdClaimMoveItemToPlayerOnZone 0x45→0x47; cClaimsPacketIdClearAllProceduralProps 0x47→0x49; cClaimsPacketIdCopyClaimScreenshotsRequest 0x50→0x52; cClaimsPacketIdDebugListClaims 0x5c→0x5e; cClaimsPacketIdDebugPackClaims 0x5d→0x5f; cClaimsPacketIdDebugPlaceClaim 0x5b→0x5d; cClaimsPacketIdFlagContestWinnerRequest 0x3f→0x3f; cClaimsPacketIdGuidToNameRequest 0x56→0x58; cClaimsPacketIdGuidToNameResponse 0x57→0x59; cClaimsPacketIdInventoryListDebugRequest 0x36→0x36; cClaimsPacketIdNameToGuidRequest 0x54→0x56; cClaimsPacketIdNameToGuidResponse 0x55→0x57; cClaimsPacketIdPlayerDeleted 0x60→0x62; cClaimsPacketIdPlayerEnteredClaim –→0x63; cClaimsPacketIdRecoverFlag 0x31→0x31; cClaimsPacketIdRequestClaimPurchase 0x52→0x54; cClaimsPacketIdRequestClaimRemoved 0x53→0x55; cClaimsPacketIdRequestPlayerUpkeepInfo 0x51→0x53; cClaimsPacketIdRequestUpkeepManagmentData 0x20→0x20; cClaimsPacketIdResetTemporaryTerrainDamage 0x46→0x48; cClaimsPacketIdRevertAllTerrain 0x34→0x34; cClaimsPacketIdRevertTerrain 0x33→0x33; cClaimsPacketIdSaveBasicInfo 0x4f→0x51; cClaimsPacketIdSellClaim 0x32→0x32; cClaimsPacketIdSendPlayerUpkeepInfo 0x4c→0x4e; cClaimsPacketIdSendPlayerUpkeepUpdatePayment 0x4d→0x4f; cClaimsPacketIdSendPlayerUpkeepUpdateReminder 0x4e→0x50; cClaimsPacketIdStartClaimPlacement –→0x46; cClaimsPacketIdStatusChange 0x5a→0x5c; cClaimsPacketIdSubmitBasicPermissionsData 0x28→0x28; cClaimsPacketIdSubmitEditClaimItemData 0x1f→0x1f; cClaimsPacketIdSubmitUpkeepManagmentData 0x21→0x21; cClaimsPacketIdUngroup 0x26→0x26; cClaimsPacketIdUpdateAdventureFlags –→0x45; cClaimsPacketIdUpkeepReminder 0xb→0xb; cClaimsPacketIdZoneAddAllClaims 0x4b→0x4d; cClaimsPacketIdZoneAddAllClaimsRequest 0x4a→0x4c; cClaimsPacketIdZoneAddClaim 0x48→0x4a; cClaimsPacketIdZoneRemoveClaim 0x49→0x4b; cClaimsPacketsIdBlockVeins 0x59→0x5b; cClaimsPacketsIdUpdateClaimBasicServerInfo 0x58→0x5a

### 0xFB LootPinata

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | BaseLootPinataPacket | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |
| 0x1 → 0x1 | S→C | LootPinataPacketSpawnLoot | FUN_1430775a0 / FUN_143121ec0 | same | sent (Game/LootPinatas.cs:199) | none |
| 0x2 → 0x2 | S→C | LootPinataPacketRemoveLoot | FUN_1430774e0 / FUN_143121e00 | same | sent (Game/LootPinatas.cs:212) | none |
| 0x4 → 0x4 | S→C | LootPinataPacketNotifyOwnerlessLootClaimed | FUN_143077340 / FUN_143121c60 | same | not sent | none |
| 0x5 → 0x5 | C→S | LootPinataPacketPickupLoot | FUN_143077970 / FUN_143122290 | same | handled (Game/LootPinatas.cs:36 PickupLootSub) | none |
| 0x5 → 0x5 | S→C | LootPinataPacketPickupLootResponse | FUN_143077400 / FUN_143121d20 | same | sent (Game/LootPinatas.cs:36 PickupLootSub) | none |
| 0x7 → 0x7 | C→S | LootPinataPacketRequestClaimOwnerlessLoot | FUN_143077b60 / FUN_143122480 | same | family handler, sub not handled (Network/Handlers/InteractionHandler.cs:255) | none |

Id-table entries with no client class (no reader / writer compiled; server-to-server or unused): cLootPinataPacketIDRequestByItem 0x6→0x6; cLootPinataPacketIdPickupLootResponse 0x3→0x3

### 0xFC PacketTeleportRequestLocations

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x2 → 0x2 | C→S | PacketTeleportRequestLocations | FUN_1430c3d90 / FUN_14316eed0 | same | handled (Game/Travel.cs:27) | none |
| 0x3 → 0x3 | S→C | PacketTeleportLocations | FUN_1430c4150 / FUN_14316f290 | same | sent (Game/Landscapes.cs:232) | none |
| 0x4 → 0x4 | C→S | PacketTeleportRequest | FUN_1430c3bf0 / FUN_14316ed30 | same | handled (Game/Travel.cs:30) | none |
| 0x5 → 0x5 | S→C | PacketTeleportPostQueueNotify | FUN_1430c2960 / FUN_14316daa0 | same | not sent | none |
| 0x6 → 0x6 | C→S | PacketTeleportToQueuedLocation | FUN_1430c3f30 / FUN_14316f070 | same | family handler, sub not handled (Network/Handlers/MiscHandler.cs:120) | none |
| 0x7 → 0x7 | C→S | PacketTeleportCancelQueue | FUN_1430c3140 / FUN_14316e280 | same | family handler, sub not handled (Network/Handlers/MiscHandler.cs:120) | none |
| 0x8 → 0x8 | C→S | PacketTeleportIntraZoneRequest | FUN_1430c32e0 / FUN_14316e420 | same | handled (Game/Travel.cs:41) | none |
| 0x9 → 0x9 | C→S | PacketTeleportPlayEffectRequest | FUN_1430c38a0 / FUN_14316e9e0 | same | family handler, sub not handled (Network/Handlers/MiscHandler.cs:120) | none |
| 0xa → 0xa | C→S | PacketTeleportManagedRequest | FUN_1430c3550 / FUN_14316e690 | same | family handler, sub not handled (Network/Handlers/MiscHandler.cs:120) | none |
| 0xb → 0xb | C→S | PacketTeleportRandomUndergroundRequest | FUN_1430c3a40 / FUN_14316eb80 | same | handled (Game/Travel.cs:34) | none |
| 0xd → 0xd | C→S | PacketTeleportNextUndergroundReply | FUN_1430c36f0 / FUN_14316e830 | same | handled (Game/Travel.cs:37) | none |

### 0xFD PropUpdateInteractionState

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | C→S | PropUpdateInteractionStatePacket | FUN_142b94680 FUN_143002320 / FUN_142c2e170 FUN_1430a7250 | same | handled (Game/PropSettings.cs:241) | none |

### 0xFE PacketPlayerStudioRegistered

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | PacketPlayerStudioRegistered | FUN_142bb0fd0 / FUN_142c4ab40 | same | sent (Game/PlayerManager.cs:95) | none |

### 0xFF Chest

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | PacketChestAddItem | FUN_142da8e70 / FUN_142e46e40 | same | sent (Game/Chests.cs:113; Game/Chests.cs:159) | none |
| – → – | S→C | PacketChestDeleteItem | FUN_142da8f80 / FUN_142e46f50 | same | sent (Game/Chests.cs:113; Game/Chests.cs:159) | none |
| – → – | S→C | PacketChestInventoryListResponse | FUN_142da90f0 / FUN_142e470c0 | same | sent (Game/Chests.cs:113; Game/Chests.cs:159) | none |
| – → – | S→C | PacketChestUpdateItem | FUN_142da92e0 / FUN_142e472b0 | same | sent (Game/Chests.cs:113; Game/Chests.cs:159) | none |
| 0x2 → 0x2 | C→S | PacketChestStopObservingChest | FUN_142da98d0 / FUN_142e478a0 | same | handled (Game/Chests.cs:124) | none |
| 0x3 → 0x3 | C→S | PacketChestInventoryListRequest | FUN_142da9720 / FUN_142e476f0 | same | handled (Game/Chests.cs:121) | none |

Id-table entries with no client class (no reader / writer compiled; server-to-server or unused): cChestPacketIdEnableWindow 0x1→0x1; cChestPacketIdInventoryListResponse 0x4→0x4; cChestPacketIdItemAdd 0x5→0x5; cChestPacketIdItemDelete 0x6→0x6; cChestPacketIdItemUpdate 0x7→0x7; cChestPacketIdNone 0x0→0x0

### 0x100 DebugDraw

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | S→C | PacketDebugDrawSphere | FUN_1432c5930 / FUN_143372760 | same | not sent | none |
| 0x2 → 0x2 | S→C | PacketDebugDrawBox | FUN_1432c55f0 / FUN_143372420 | same | not sent | none |
| 0x3 → 0x3 | S→C | PacketDebugDrawLine | FUN_1432c5790 / FUN_1433725c0 | same | not sent | none |
| 0x4 → 0x4 | S→C | PacketDebugDrawTerrainFieldSphere | FUN_1432c5ad0 / FUN_143372900 | same | not sent | none |
| 0x5 → 0x5 | S→C | PacketDebugDrawText | FUN_1432c5c20 / FUN_143372a50 | same | not sent | none |

Id-table entries with no client class (no reader / writer compiled; server-to-server or unused): cDebugDrawPacketIdNone 0x0→0x0

### 0x102 FogOfWarZoneDataRequest

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | C→S | FogOfWarZoneDataRequestPacket | FUN_14341c8e0 / FUN_1434ca580 | same | not handled | none |

### 0x103 FogOfWarZoneDataReply

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0xffffffff → 0xffffffff | S→C | FogOfWarZoneDataReplyPacket | FUN_142bafc00 / FUN_142c49770 | same | not sent | none |

### 0x104 SocialShare

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | SocialShare::BaseSocialSharePacket | FUN_142bae6f0 / FUN_142c48260 | same | not sent | none |
| 0x1 → 0x1 | C→S | SocialShare::SocialSharePacketGetProviderStatusRequest | FUN_1430d6ab0 / FUN_143181bb0 | same | not handled | none |
| 0x2 → 0x2 | S→C | SocialShare::SocialSharePacketGetProviderStatusResponse | FUN_1430d6860 / FUN_143181960 | same | not sent | none |
| 0x3 → 0x3 | C→S | SocialShare::SocialSharePacketPublishRequest | FUN_1430d6c50 / FUN_143181d50 | same | not handled | none |
| 0x4 → 0x4 | S→C | SocialShare::SocialSharePacketPublishResponse | FUN_1430d6990 / FUN_143181a90 | same | not sent | none |

### 0x105 HeroicMovement

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | C→S | HeroicMovementPacketFireGrapplingHook | FUN_142b86630 FUN_142dc0f10 / FUN_142c1fe00 FUN_142e5ede0 | same | not handled | none |
| 0x2 → 0x2 | C→S | HeroicMovementPacketSetHeroicMovement | FUN_142bafda0 FUN_142de6d00 / FUN_142c49910 FUN_142e84ce0 | same | not handled | none |
| 0x3 → 0x3 | C→S | HeroicMovementPacketSwimmingUpdate | FUN_142b867d0 FUN_1434dc950 / FUN_142c1ffa0 FUN_14358c410 | same | not handled | none |
| 0x4 → 0x4 | C→S | HeroicMovementPacketTerminateGrappleHookEffects | FUN_142dc1290 / FUN_142e5f160 | same | not handled | none |
| 0x5 → 0x5 | C→S | HeroicMovementPacketHeroicMovement | FUN_142dc10b0 / FUN_142e5ef80 | same | not handled | none |

Id-table entries with no client class (no reader / writer compiled; server-to-server or unused): cHeroicMovementPacketTypeNone 0x0→0x0

### 0x107 0x107

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | C→S | MarketplaceImagePacketUploadRequest | FUN_1431408c0 / FUN_1431eb2e0 | same | not handled | none |

### 0x109 ProceduralDataMonitor

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | C→S | ProceduralDataMonitorPacketUpdateRequest | FUN_143159870 / FUN_143204290 | same | not handled | none |

### 0x10A ProceduralDataMonitor

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | ProceduralDataMonitorPacketUpdateResponse | FUN_142bb4ba0 / FUN_142c4e3b0 | same | not sent | none |

### 0x10B ItemReview

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | C→S | ItemReview::PacketOptInAccount | FUN_14353a2f0 / FUN_1435ec2c0 | same | not handled | none |

### 0x10C WorldEditing

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → 0x8 | S→C | WorldEditingPlayEffectPacket | – / – | new | not sent | implement (0.695 only) |
| 0x1 → 0x1 | C→S | WorldEditingBoxUpdatePacket | FUN_142fdcd10 FUN_142fdf050 / FUN_14307ca90 FUN_14307ee00 | same | handled (Game/WorldEditing.cs:162) | none |
| 0x2 → 0x2 | C→S | WorldEditingSphereUpdatePacket | FUN_142fdc1c0 FUN_142fdd4d0 / FUN_14307bee0 FUN_14307d250 | same | handled (Game/WorldEditing.cs:163) | none |
| 0x3 → 0x3 | C→S | WorldEditingShapeUpdatePacket | FUN_142fdc110 FUN_142fdd330 / FUN_14307be30 FUN_14307d0b0 | same | handled (Game/WorldEditing.cs:164) | none |
| 0x4 → 0x4 | C→S | WorldEditingSelectionUpdate | FUN_142fdc040 FUN_142fdd190 / FUN_14307bd60 FUN_14307cf10 | same | handled (Game/WorldEditing.cs:165) | none |
| 0x5 → 0x5 | C→S | WorldEditingPreviewSetupPacket | FUN_142fdbdd0 FUN_1436d60a0 / FUN_14307baf0 FUN_1436caee0 | same | handled (Game/WorldEditing.cs:175) | none |
| 0x6 → 0x6 | C→S | WorldEditingPreviewUpdatePacket | FUN_142fdbf40 FUN_1436d6240 / FUN_14307bc60 FUN_1436cb080 | same | handled (Game/WorldEditing.cs:166) | none |
| 0x7 → 0x7 | C→S | WorldEditingFillUpdatePacket | FUN_142b7eb80 FUN_1436c9450 / FUN_142c18330 FUN_143788f00 | same | handled (Game/WorldEditing.cs:167) | none |

### 0x10D InstancedGame

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → 0x18 | C→S | InstancedGameAddOrRemoveRuleRequestPacket | – / – | new | not handled (family handler default branch) (Game/InstancedGames.cs:737) | implement (0.695 only) |
| – → 0xd | C→S | InstancedGameGetConfigurationRequestPacket | – / – | new | not handled; the number 0xd is the 0.589 meaning on the server (Game/InstancedGames.Editor.cs:308 SubGetConfiguration) | implement (0.695 only) |
| – → 0x19 | C→S | InstancedGameMatchmakingRequest | – / – | new | not handled (family handler default branch) (Game/InstancedGames.cs:737) | implement (0.695 only) |
| – → 0x1a | S→C | InstancedGameMatchmakingUpdate | – / – | new | not sent | implement (0.695 only) |
| – → 0x8 | C→S | InstancedGameRequestJoinTeamPacket | – / – | new | not handled: sub 8 Request handled, but no Request type 9 (Game/InstancedGames.cs:752-789) | implement (0.695 only) |
| 0x0 → 0x0 | S→C | BaseInstancedGamePacket | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | sent (Game/InstancedGames.Editor.cs:104; Game/InstancedGames.Editor.cs:126) | none |
| 0x2 → 0x2 | S→C | InstancedGameAdminMonitorPacket | FUN_143427160 / FUN_1434d6be0 | same | not sent | none |
| 0x3 → 0x3 | S→C | InstancedGameScoreboardPacket | FUN_143427ea0 / FUN_1434d7b40 | same | sent (Game/InstancedGames.cs:35 SubScoreboard) | none |
| 0x4 → 0x4 | S→C | InstancedGamePropRecipePacket | FUN_143427a10 / FUN_1434d7680 | body: item + trailing u32 (FUN_1434d7f90; §B6) | sent (Game/InstancedGames.cs:35 SubPropRecipe) | per-build body |
| 0x5 → 0x5 | C→S | InstancedGamePropRecipeRequest | FUN_14342a8b0 / FUN_1434daad0 | same | handled (Game/InstancedGames.Editor.cs:35 SubPropRecipeRequest) | none |
| 0x6 → 0x6 | S→C | InstancedGameStatePacket | FUN_143427fe0 / FUN_1434d7c80 | same | sent (Game/InstancedGames.cs:1364) | none |
| 0x7 → 0x7 | S→C | InstancedGameEventPacket | FUN_143427430 / FUN_1434d6eb0 | body: u32,u32,u32,u64,u64 then str,str,u32,u32 (was u32) (FUN_1434d6eb0; §B6); types 10/11 read pointCount +0x6c (functions-695 B9) | sent (Game/InstancedGames.cs:35 SubEvent) | per-build body |
| 0x8 → 0x8 | C→S | InstancedGameRequestJoinPacket | FUN_14342ada0 / FUN_1434dafc0 | same | handled (Game/InstancedGames.cs:752) | none |
| 0x8 → 0x8 | C→S | InstancedGameRequestLeavePacket | FUN_14342af40 / FUN_1434db300 | same | handled (Game/InstancedGames.cs:752) | none |
| 0x8 → 0x8 | C→S | InstancedGameRequestPacket | FUN_14342b0e0 FUN_14342ada0 / FUN_1434db4a0 FUN_1434dafc0 | same | handled (Game/InstancedGames.cs:752) | none |
| 0x9 → 0x9 | S→C | InstancedGameHUDPacket | FUN_143432bd0 FUN_14342ba40 / FUN_1434dbe10 | body: entry + trailing bool + u64 (FUN_1434d8180; §B6) | sent (Game/InstancedGames.cs:35 SubHud) | per-build body |
| 0xa → 0xa | C→S | InstancedGamePropRequestPacket | FUN_14342ac00 / FUN_1434dae20 | body: C→S + trailing u32 (writer FUN_1434d62d0; §B6) | handled (Game/InstancedGames.Editor.cs:35 SubPropRequest) | per-build body |
| 0xb → 0xb | C→S | InstancedGamePropRemovePacket | FUN_14342aa60 / FUN_1434dac80 | same | handled (Game/InstancedGames.Editor.cs:35 SubPropRemove) | none |
| 0xd → 0xe | S→C | InstancedGameGetConfigurationResponsePacket | FUN_14342b760 / FUN_1434dbb20 | renumbered+body: settings key u32 = hash(SettingKey), + u32, + string list, merged rule map (FUN_1434dbb20; §B7) | sent (Game/InstancedGames.Editor.cs:308 SubGetConfiguration) | use sub 0xe for 0.695 + body |
| 0xe → 0xf | C→S | InstancedGameSetConfigurationRequestPacket | – / – | renumbered+body: same shapes as the response (writer FUN_1434d6620; §B7) | handled (Game/InstancedGames.Editor.cs:308 SubSetConfiguration) | use sub 0xf for 0.695 + body |
| 0xf → 0x10 | C→S | InstancedGameStartRequest | FUN_14342b420 / FUN_1434db7e0 | renumbered | handled (Game/InstancedGames.Editor.cs:35 SubEnableGame) | use sub 0x10 for 0.695 |
| 0x10 → 0x11 | C→S | InstancedGameEndRequest | FUN_14342a560 / FUN_1434da430 | renumbered | handled (Game/InstancedGames.Editor.cs:35 SubDisableGame) | use sub 0x11 for 0.695 |
| 0x11 → 0x12 | S→C | InstancedGamePropStatePacket | FUN_14342bd10 / FUN_1434dc150 | renumbered+body: entry + trailing u64 (FUN_1434d77c0; §B6) | not sent | (not implemented; 0.695 number 0x12) + body |
| 0x12 → 0x13 | S→C | InstancedGamePropInteractionPacket | FUN_143427900 / FUN_1434d7570 | renumbered | not sent | (not implemented; 0.695 number 0x13) |
| 0x13 → 0x14 | S→C | InstancedGamePlayerRemovalPacket | FUN_14342bb70 / FUN_1434dbfb0 | renumbered | not sent | (not implemented; 0.695 number 0x14) |
| 0x14 → 0x15 | S→C | InstancedGameTeamMembersPacket | FUN_14342bf40 / FUN_1434dc380 | renumbered | sent (Game/InstancedGames.cs:35 SubTeamMembers) | use sub 0x15 for 0.695 |
| 0x15 → 0x16 | C→S | InstancedGameObserverRegistrationPacket | FUN_143427790 FUN_14342a710 / FUN_1434d7400 FUN_1434da930 | renumbered | handled (Game/InstancedGames.cs:741) | use sub 0x16 for 0.695 |
| 0x16 → 0x17 | S→C | InstancedGameUpdatePlayerFlagsPacket | FUN_143428230 / FUN_1434d7ed0 | renumbered | not sent | (not implemented; 0.695 number 0x17) |

### 0x111 ObjectScripting

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | S→C | ObjectScriptingPacketSupportedObjectsResponse | FUN_142ea6b60 / FUN_142f45700 | same | sent (Game/StoryTools.cs:1106) | none |
| 0x2 → 0x2 | S→C | ObjectScriptingPacketSupportedEventsResponse | FUN_142ea4580 / FUN_142f43120 | same | sent (Game/StoryTools.cs:940) | none |
| 0x3 → 0x3 | S→C | ObjectScriptingPacketSupportedActionsResponse | FUN_142ea4420 / FUN_142f42fc0 | same | sent (Game/StoryTools.cs:947) | none |
| 0x4 → 0x4 | S→C | ObjectScriptingPacketSupportedActionsEventsAndDefaultsResponse | FUN_142ea42a0 / FUN_142f42e40 | same | sent (Game/StoryTools.cs:954) | none |
| 0x5 → 0x5 | S→C | ObjectScriptingPacketAddBindingResponse | FUN_142ea3b90 / FUN_142f42730 | same | sent (Game/StoryTools.cs:970) | none |
| 0x6 → 0x6 | S→C | ObjectScriptingPacketListBindingsResponse | FUN_142ea3cd0 / FUN_142f42870 | same | sent (Game/StoryTools.cs:1120) | none |
| 0x7 → 0x7 | S→C | ObjectScriptingPacketRemoveBindingResponse | FUN_142ea4120 / FUN_142f42cc0 | same | sent (Game/StoryTools.cs:988) | none |
| 0x8 → 0x8 | S→C | ObjectScriptingPacketRemoveAllBindingsResponse | FUN_142ea4060 / FUN_142f42c00 | same | sent (Game/StoryTools.cs:49 SubRemoveAllBindings) | none |
| 0x9 → 0x9 | S→C | ObjectScriptingPacketRestoreDefaultsResponse | FUN_142ea41e0 / FUN_142f42d80 | same | sent (Game/StoryTools.cs:49 SubRestoreDefaults) | none |
| 0xa → 0xa | S→C | ObjectScriptingPacketPotentialLinkResponse | FUN_142ea3e30 / FUN_142f429d0 | same | sent (Game/StoryTools.cs:1014) | none |
| 0xb → 0xb | S→C | ObjectScriptingPacketRemoveAddBindingResponse | FUN_142ea3ef0 / FUN_142f42a90 | same | sent (Game/StoryTools.cs:1027) | none |
| 0xc → 0xc | C→S | ObjectScriptingPacketSupportedObjectsRequest | FUN_142ea64c0 / FUN_142f45060 | same | handled (Game/StoryTools.cs:933) | none |
| 0xd → 0xd | C→S | ObjectScriptingPacketSupportedEventsRequest | FUN_142ea6310 / FUN_142f44eb0 | same | handled (Game/StoryTools.cs:936) | none |
| 0xe → 0xe | C→S | ObjectScriptingPacketSupportedActionsRequest | FUN_142ea6120 / FUN_142f44cc0 | same | handled (Game/StoryTools.cs:943) | none |
| 0xf → 0xf | C→S | ObjectScriptingPacketSupportedActionsEventsAndDefaultsRequest | FUN_142ea5f70 / FUN_142f44b10 | same | handled (Game/StoryTools.cs:950) | none |
| 0x10 → 0x10 | C→S | ObjectScriptingPacketAddBindingRequest | FUN_142ea53d0 / FUN_142f43f70 | same | handled (Game/StoryTools.cs:927; Game/StoryTools.cs:965) | none |
| 0x11 → 0x11 | C→S | ObjectScriptingPacketListBindingsRequest | FUN_142ea5570 / FUN_142f44110 | same | handled (Game/StoryTools.cs:973) | none |
| 0x12 → 0x12 | C→S | ObjectScriptingPacketRemoveBindingRequest | FUN_142ea5c10 / FUN_142f447b0 | same | handled (Game/StoryTools.cs:927; Game/StoryTools.cs:979) | none |
| 0x13 → 0x13 | C→S | ObjectScriptingPacketRemoveAllBindingsRequest | FUN_142ea5a60 / FUN_142f44600 | same | handled (Game/StoryTools.cs:1005; Game/StoryTools.cs:927; Game/StoryTools.cs:992) | none |
| 0x14 → 0x14 | C→S | ObjectScriptingPacketRestoreDefaultsRequest | FUN_142ea5dc0 / FUN_142f44960 | same | handled (Game/StoryTools.cs:927; Game/StoryTools.cs:993) | none |
| 0x15 → 0x15 | C→S | ObjectScriptingPacketPotentialLinkRequest | FUN_142ea5720 / FUN_142f442c0 | same | handled (Game/StoryTools.cs:1009) | none |
| 0x16 → 0x16 | C→S | ObjectScriptingPacketRemoveAddBindingRequest | FUN_142ea58c0 / FUN_142f44460 | same | handled (Game/StoryTools.cs:1017; Game/StoryTools.cs:927) | none |

### 0x112 PacketClaimVendorSetPriceRequest

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | S→C | PacketClaimVendorUpdateInventory | FUN_1432cc620 / FUN_143379450 | same | sent (Game/Vendors.cs:48 UpdateInventorySub) | none |
| 0x2 → 0x2 | C→S | PacketClaimVendorSetPriceRequest | FUN_1432cc0d0 / FUN_143378f00 | same | handled (Game/Vendors.cs:171) | none |
| 0x3 → 0x3 | S→C | PacketClaimVendorSetPriceResponse | FUN_1432cb8f0 / FUN_143378720 | same | sent (Game/Vendors.cs:48 SetPriceResponseSub) | none |
| 0x4 → 0x4 | C→S | PacketClaimVendorListItemRequest | FUN_1432cbd40 / FUN_143378b70 | same | handled (Game/Vendors.cs:188) | none |
| 0x5 → 0x5 | S→C | PacketClaimVendorListItemResponse | FUN_1432cb6f0 / FUN_143378520 | same | sent (Game/Vendors.cs:48 ListItemResponseSub) | none |
| 0x6 → 0x6 | C→S | PacketClaimVendorStopObserving | FUN_1432cc270 / FUN_1433790a0 | same | handled (Game/Vendors.cs:159) | none |
| 0x7 → 0x7 | C→S | PacketClaimVendorPurchaseItemRequest | FUN_1432cbee0 / FUN_143378d10 | same | handled (Game/Vendors.cs:164) | none |
| 0x8 → 0x8 | S→C | PacketClaimVendorPurchaseItemResponse | FUN_1432cb7f0 / FUN_143378620 | same | sent (Game/Vendors.cs:48 PurchaseResponseSub) | none |

### 0x113 WarpNetwork

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x2 → 0x2 | S→C | WarpNetwork::WarpNetworkZoneNetworkPacket | FUN_1434504c0 / FUN_1435025c0 | same | sent (Game/WarpNetwork.cs:202) | none |
| 0x4 → 0x4 | C→S | WarpNetwork::WarpNetworkClientRequestPacket | FUN_143450bb0 / FUN_143502cb0 | same | handled (Network/Handlers/MiscHandler.cs:115) | none |

### 0x114 PacketCorrectNpcHeights

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | PacketCorrectNpcHeights | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |

### 0x115 KnownProp

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | S→C | KnownPropPacketAddPropResponse | FUN_142e65310 / FUN_142f03ea0 | same: reader same; only the UI filler 14306bea0→1431167c0 differs (struct offsets) | sent (Game/RecipeBook.cs:182) | none |
| 0x2 → 0x2 | C→S | KnownPropPacketCreatePropItemRequest | FUN_142e648b0 / FUN_142f03440 | same | not handled | none |

### 0x116 ArtData

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1 → 0x1 | S→C | ArtDataPacketTintDefinitionManagerData | FUN_142bae270 / FUN_142c47de0 | same | not sent | none |

### 0x117 Tool

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | ToolPacketUndoStackChange | FUN_1433b4760 / FUN_143462760 | same | not sent | none |
| 0x1 → 0x1 | C→S | ToolPacketActivate | FUN_1433a5960 / FUN_143452ac0 | same | not handled | none |

Id-table entries with no client class (no reader / writer compiled; server-to-server or unused): cToolPacketUndoStackChange 0x2→0x2

### 0x118 ContentManagement

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x1b → 0x1b | C→S | ContentManagement::ContentPacketCommandGoto | FUN_143446e80 / FUN_1434f8f80 | same | not handled | none |
| 0x1b → 0x1b | C→S | ContentManagement::ContentPacketCommandPackageCreate | FUN_1434470c0 / FUN_1434f91c0 | same | not handled | none |
| 0x1b → 0x1b | C→S | ContentManagement::ContentPacketCommandPackageDelete | FUN_143447310 / FUN_1434f9410 | same | not handled | none |
| 0x1b → 0x1b | C→S | ContentManagement::ContentPacketCommandPackageSave | FUN_1434474b0 / FUN_1434f95b0 | same | not handled | none |
| 0x1b → 0x1b | C→S | ContentManagement::ContentPacketCommandPackageSetMetaData | FUN_1434476f0 / FUN_1434f97f0 | same | not handled | none |
| 0x1b → 0x1b | C→S | ContentManagement::ContentPacketCommandSetContentBuilder | FUN_143447930 / FUN_1434f9a30 | same | not handled | none |
| 0x1b → 0x1b | C→S | ContentManagement::ContentPacketCommandSocketActivate | FUN_143447b60 / FUN_1434f9c60 | same | not handled | none |
| 0x1b → 0x1b | C→S | ContentManagement::ContentPacketCommandSocketCreate | FUN_143447d00 / FUN_1434f9e00 | same | not handled | none |
| 0x1b → 0x1b | C→S | ContentManagement::ContentPacketCommandSocketDeactivate | FUN_143447ea0 / FUN_1434f9fa0 | same | not handled | none |
| 0x1b → 0x1b | C→S | ContentManagement::ContentPacketCommandSocketDelete | FUN_1434480e0 / FUN_1434fa1e0 | same | not handled | none |
| 0x1b → 0x1b | C→S | ContentManagement::ContentPacketCommandSocketSetMetaData | FUN_143448320 / FUN_1434fa420 | same | not handled | none |
| 0x1d → 0x1d | S→C | ContentManagement::ContentPacketPackageStateUpdate | FUN_143bd0ec0 FUN_14344bf00 / FUN_143c90ec0 FUN_1434fe000 | same | not sent | none |
| 0x1e → 0x1e | S→C | ContentManagement::ContentPacketSocketStateUpdate | FUN_14344bf80 / FUN_1434fe080 | same | not sent | none |

### 0x11A 0x11A

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | BaseDesignBasedStateMachinePacketPacket | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |

### 0x11B Interaction

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| 0x0 → 0x0 | S→C | BaseInteractionPacket | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | not sent | none |
| 0x1 → 0x1 | S→C | InteractionStartPacket | FUN_1433226b0 / FUN_1433cf490 | same | not sent | none |
| 0x3 → 0x3 | S→C | InteractionCancelPacketZone | FUN_143322460 / FUN_1433cf240 | same | not sent | none |
| 0x4 → 0x4 | S→C | InteractionEndPacket | FUN_143322570 / FUN_1433cf350 | same | not sent | none |

Id-table entries with no client class (no reader / writer compiled; server-to-server or unused): cInteractionPacketCancelClient 0x2→0x2

### 0x11C CharacterCustomization

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → 0x4 | S→C | CharacterCustomizationPacketEquippedAttachmentReply | – / – | new | not sent | implement (0.695 only) |
| – → 0x3 | C→S | CharacterCustomizationPacketEquippedAttachmentRequest | – / – | new | not handled (family handler default branch) (Game/Barbershop.cs:34) | implement (0.695 only) |
| 0x1 → 0x1 | C→S | CharacterCustomizationPacketPlayerSet | FUN_142ffd830 / FUN_1430a0a80 | body: C→S + two trailing u32 (base-actor id, 0): writer FUN_1430a0530 vs template FUN_142ffd830 (functions-695 B5, verified) | handled (Game/Barbershop.cs:37) | per-build body |
| 0x2 → 0x2 | S→C | CharacterCustomizationPacketAddKnowledgeResponse | FUN_1435d77f0 / FUN_14368a5d0 | same | not sent | none |

### Gateway

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | C→S | Gateway::ExternalPackets::PacketLoginRequest | FUN_1439924f0 / FUN_143a4a3d0 | same | see Login/Gateway | none |
| – → – | C→S | Gateway::ExternalPackets::PacketLogout | FUN_143992380 / FUN_143a4a260 | same | see Login/Gateway | none |
| – → – | C→S | Gateway::ExternalPackets::PacketTunnelPacketToExternalConnection | FUN_1439937f0 / FUN_143a4b6d0 | same | see Login/Gateway | none |
| – → – | S→C | Gateway::InternalPackets::PacketTunnelPacketToExternalConnectionList | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | see Login/Gateway | none |
| 0xfffffffe → 0xfffffffe | S→C | Gateway::ExternalPackets::BasePacket | FUN_142bde8a0 / FUN_142c78310 | same | see Login/Gateway | none |
| 0xfffffffe → 0xfffffffe | S→C | Gateway::ExternalPackets::PacketTunnelPacketFromExternalConnection | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | see Login/Gateway | none |

### Login

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | Login::ExternalPackets::PacketCharacterCreateReply | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | see Login/Gateway | none |
| – → – | C→S | Login::ExternalPackets::PacketCharacterCreateRequest | FUN_14399ac60 / FUN_143a52b40 | same | see Login/Gateway | none |
| – → – | S→C | Login::ExternalPackets::PacketCharacterDeleteReply | FUN_14399c8e0 / FUN_143a547c0 | same | see Login/Gateway | none |
| – → – | C→S | Login::ExternalPackets::PacketCharacterDeleteRequest | FUN_14399adc0 / FUN_143a52ca0 | same | see Login/Gateway | none |
| – → – | S→C | Login::ExternalPackets::PacketCharacterLoginReply | FUN_14399ca00 / FUN_143a548e0 | same | see Login/Gateway | none |
| – → – | C→S | Login::ExternalPackets::PacketCharacterLoginRequest | FUN_14399af20 / FUN_143a52e00 | same | see Login/Gateway | none |
| – → – | C→S | Login::ExternalPackets::PacketCharacterSelectInfoRequest | FUN_14399b080 / FUN_143a52f60 | same | see Login/Gateway | none |
| – → – | S→C | Login::ExternalPackets::PacketCharacterTransferServerReply | FUN_14399cbf0 / FUN_143a54ad0 | same | see Login/Gateway | none |
| – → – | C→S | Login::ExternalPackets::PacketCharacterTransferServerRequest | FUN_14399b220 / FUN_143a53100 | same | see Login/Gateway | none |
| – → – | S→C | Login::ExternalPackets::PacketForcedDisconnect | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | see Login/Gateway | none |
| – → – | C→S | Login::ExternalPackets::PacketLoginRequest | FUN_14399b380 / FUN_143a53260 | same | see Login/Gateway | none |
| – → – | C→S | Login::ExternalPackets::PacketLogout | FUN_14399b4e0 / FUN_143a533c0 | same | see Login/Gateway | none |
| – → – | S→C | Login::ExternalPackets::PacketServerListReply | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | see Login/Gateway | none |
| – → – | C→S | Login::ExternalPackets::PacketServerListRequest | FUN_14399b680 / FUN_143a53560 | same | see Login/Gateway | none |
| – → – | S→C | Login::ExternalPackets::PacketServerUpdate | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | see Login/Gateway | none |
| – → – | C→S | Login::ExternalPackets::PacketTunnelAppPacketClientToServer | FUN_14399b820 / FUN_143a53700 | same | see Login/Gateway | none |
| – → – | S→C | Login::ExternalPackets::PacketTunnelAppPacketServerToClient | FUN_14399cfd0 / FUN_143a54eb0 | same | see Login/Gateway | none |
| 0x0 → 0x0 | S→C | Login::ExternalPackets::PacketCharacterSelectInfoReply | FUN_14399f0f0 / FUN_143a56fd0 | same | see Login/Gateway | none |
| 0x0 → 0x0 | S→C | Login::ExternalPackets::PacketLoginReply | FUN_14399fdd0 FUN_14399cda0 / FUN_143a57cb0 FUN_143a54c80 | same | see Login/Gateway | none |

### RemoteAssetDelivery

| Sub 589 → 695 | Dir | Class | Client 589 / 695 | 0.695 status | Emulator (0.589 numbering) | Change for 0.695 |
|---|---|---|---|---|---|---|
| – → – | S→C | RemoteAssetDelivery::AssetDataPacket | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | see Login/Gateway | none |
| – → – | S→C | RemoteAssetDelivery::AssetErrorPacket | FUN_143a7f3f0 / FUN_143b372e0 | same | see Login/Gateway | none |
| – → – | S→C | RemoteAssetDelivery::AssetRequestPacket | FUN_143a7e8a0 / FUN_143b36790 | same | see Login/Gateway | none |
| – → – | S→C | RemoteAssetDelivery::ManifestCrcRequestPacket | FUN_143a7e9f0 / FUN_143b368e0 | same | see Login/Gateway | none |
| – → – | S→C | RemoteAssetDelivery::ManifestDataPacket | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | see Login/Gateway | none |
| – → – | S→C | RemoteAssetDelivery::ManifestRequestPacket | FUN_143a7eb30 / FUN_143b36a20 | same | see Login/Gateway | none |
| 0x0 → 0x0 | S→C | RemoteAssetDelivery::ManifestCrcDataPacket | – / – | same (dispatcher): reader inline in the family dispatcher: dispatcher cases equal (casecmp.tsv / zonecmp.txt) | see Login/Gateway | none |

## 11. Appendix: packet classes without a compiled vtable reference (L53)

In both RTTIs, never referenced by vtable in either decompile, so no header constants; PlayerUpdatePacket* ones are covered by the PlayerUpdate rows in §10. All others belong to families whose dispatchers / zone closures compare equal (famtable.md), i.e. unchanged in 0.695; their sub numbers are not proven from client constants yet.

Abilities::AbilityPacketAbilityLanding, AchievementTrackerInitializePacket, ActivityLaunchRequest, ClientActivityLaunchGaqRequest, ClientActivityLaunchPacketActivityCanceled, ClientActivityLaunchPacketActivityLaunched, ClientActivityLaunchPacketAddMember, ClientActivityLaunchPacketInviteMemberReply, ClientActivityLaunchPacketInviteMemberResult, ClientActivityLaunchPacketOnMatchmakingCancel, ClientActivityLaunchPacketOnMatchmakingLaunch, ClientActivityLaunchPacketOnMatchmakingStart, ClientActivityLaunchPacketPlayerHasBeenAccepted, ClientActivityLaunchPacketRemoveMember, ClientActivityLaunchRequest, CoinStoreClearTransactionHistoryPacket, CombatPacketLastDamageTime, CombatReviveFallenComradePacket, CombatStateManagerTransitionStatePacket, CommandPacketDebrisLaunchPacket, CommandPacketInteractionClaim, CommandPacketInteractionClaimAttachment, CommandPacketInteractionList, CommandPacketInteractionVault, ContentManagement::ContentPacketAllPackages, ContentManagement::ContentPacketSetContentBuilder, Crafting::RecipeRefiningOutputResponsePacket, Currency::CurrencyPacketSetCurrencyDiscount, DefinitionFilters::DefinitionFilterPacketSetDefinitionVariable, DesignBasedStateMachinePacketHandleTransitionAction, EncounterInvitationPacket, EncounterMatchmaking::MatchmakingServerStatusPacket, EncounterPacketEncounterLaunchFailed, EncounterPacketEncounterShutdown, EncounterPacketObjectiveState, EncounterPacketPlayerEnter, EncounterParticipantMessagePacket, EncounterParticipantTerminateEncounterPacket, EncounterRunningListResponsePacket, EncounterStatePacket, EncounterZoneIsReadyPacket, EventPacket, GameCommerce::UramApiAddCreditCardRequest, GameCommerce::UramApiCreditCardTypesRequest, GameCommerce::UramApiDeleteCreditCardRequest, GameCommerce::UramApiFinalizeSteamTransactionRequest, GameCommerce::UramApiFundWalletRequest, GameCommerce::UramApiIsSteamCustomerRequest, GameCommerce::UramApiPaymentSourceRequest, GameCommerce::UramApiPreviewMembershipPurchaseRequest, GameCommerce::UramApiPreviewWalletFundRequest, GameCommerce::UramApiPurchaseMembershipRequest, GameCommerce::UramApiRedeemCodeRequest, GameCommerce::UramApiUpdateCreditCardRequest, GaqGenericNoResponse, GaqItemMarketplaceItemReviewRequest, GaqItemSwapSeatRequest, GaqItemTeleportToZoneRequest, Gateway::ExternalPackets::PacketChannelIsRoutable, Gateway::ExternalPackets::PacketForcedLogout, Gateway::ExternalPackets::PacketLoginReply, Gateway::InternalPackets::PacketTunnelPacketFromExternalConnection, Gateway::InternalPackets::PacketTunnelPacketToExternalConnection, Grief::GriefPacketReset, Grief::GriefPacketSetGriefLevel, Groups::GroupPacketInMinigame, Groups::GroupPacketRemoveGroup, Groups::GroupPacketSetPrimaryTarget, HudMessagePacket, Items::ItemPacketSetItemLineTimers, Items::ItemPacketSetItemLineTrialTimers, Items::ItemPacketSetItemLockTimer, MiniGameCreateGameResultPacket, Missions::MissionPacketSetMissionManager, PacketBaseInGamePurchase, PacketBusinessEnvironments, PacketClientIsReady, PacketClientServerShuttingDown, PacketInGamePurchaseEnableMarketplace, PacketInGamePurchaseStoreBundleCategories, PacketInGamePurchaseStoreBundleCategoryGroups, PacketInGamePurchaseStoreBundleGroups, PacketInGamePurchaseStoreBundleTags, PacketInGamePurchaseWalletBalanceUpdate, PacketPlayerInitiateNameChange, PacketPlayerNameChangeResult, PacketPlayerRewardCurrency, PacketTeleportActivateTerminal, PalettePacketPaletteUpdate, PlayerMemberStatusUpdatePacket, PromotionalBundleDataPacket, PropReplyPacket, QuickChatSendChatToChannelPacket, QuickChatSendTellPacket, QuizRecordUpdatePacket, Raids::RaidPacketRemoveGroup, ReferenceDataPacket, ReferenceDataPacketClientProfileData, RepeatingActivityBaseAdminPacket, Skills::SkillPacketLoadSkillDefinitionManager, Skills::SkillPacketSetSkillPointManager, SocialPacketFriendsGameTopScores, SocialPacketGameTopScores, SocialPacketGamesPlayedCount, TargetPacketRemoveAllTargets, VehicleExpirationPacket, VehiclePacketLoadVehicleDefinitionManager, VoiceLoginPacket
