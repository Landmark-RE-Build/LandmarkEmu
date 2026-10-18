# Wire audit, group d (2026-10-17): zoning, terrain, building, warp, creatures / combat, movement, admin pages

Brief: audit-wire-brief.md. Read-only; server = `<workspace>\LandmarkEmu` as of the native-0.695 numbering
(003dc1f) and the 0.589 body removal (4e7e9ed). Client = `re/decomp_all_695`. [P] = read from the client function
named; [I] = inferred. Paths below: `WS/` = `LandmarkEmulator.WorldServer/`, `Env/` = `LandmarkEmulator.EnvServer.Core/`.

Method: for every write / read site in the group I took the server's byte layout and compared it with the 0.695 reader /
writer (`pkt.py` with the 0.695 string reader FUN_142d4b5d0 added to a scratch copy, plus manual reads of the
dispatchers). I also checked the exact-consumption rule in each dispatcher: the zone dispatcher FUN_142d1ccf0 rejects
leftover bytes (e.g. case 4, ZoneCoordinateInfo: `end - cursor < 1`), as do PlayerUpdate readers (`param_4 || rest < 1`),
Abilities (FUN_142e02440's caller part_0071.c:11200), Combat (FUN_1433cb100 / FUN_1433cb410) and 0x7F
(FUN_142c4dd40). The TerrainCell (FUN_143152f70), VoxelConstruction (FUN_143162830), WorldEditing (FUN_1430838d0) and
HeroicMovement (FUN_142cdc570) dispatchers do **not** check for leftover bytes.

Opcode codec reminder (WS/Network/WorldSession.cs:1267 GetOpcodeBytes): the enum bytes are written big-endian without
leading zeros, so `0xEC000300` goes out as `EC 00 03 00`. For the u32-sub families (TerrainCell), the body must add
the two high bytes of the sub. Every TerrainCell builder does this (`WriteBytes(new byte[2])` / `{0,0}`).

## Table

| Packet | Server writer / reader (file:line) | Client 0.695 reader / writer | Verdict | Detail |
|---|---|---|---|---|
| ClientBeginZoning 0x23 (S→C) | WS/Network/Message/Model/ClientBeginZoning.cs:56; built in WS/Game/Teleport.cs:228 | FUN_142c23310 (+ Sky FUN_142c1ddf0) | OK | str, u32, 4×f32, 4×f32, str, u8, 6×u32, bool, bool, Sky. Sky = 3×u32, str, str, u64, 3×f64, 3×f64, 2×bool, (str, u32)×3, 5×u32, 4×u32 (FUN_142c1e3c0), 4×u32, u64, 2×u32, bool, u32, bool, 2×u32 (FUN_142c3d690), u32, str. Sky.cs Write matches 1:1 |
| SendZoneDetails 0x2E (S→C) | WS/Game/Landscapes.cs:341 (capture WS/Network/WorldSession.cs:651 + Model/SendZoneDetails.cs:43); Teleport.cs:247; MiscHandler.cs:297; WorldSession.cs:510 | FUN_142c246b0 | OK | str, u32, bool, str, u32, u32, u64, 3×u32, Sky, **bool** (+0x200). I parsed the capture with the client layout: 395 bytes + 1 trailing byte (00), so the model's `Trailing` carries the client's last bool. Exact |
| ZoneDoneSendingInitialData 0x11 (S→C) | Model/ZoneDoneSendingInitiailData.cs; MiscHandler.cs:217 / 295 | FUN_142d1ccf0 case 3 (no read) | OK | empty |
| ZoneCoordinateInfo 0x12 (S→C) | WS/Network/Handlers/MiscHandler.cs:306 (hex) | FUN_142c23b80 (+ FUN_142c38220) | OK | 3×u64(f64), 3×u32, 3×u32, 2×vec4 = 80 bytes; the hex is 80 bytes; exact |
| GameTimeSync 0x37 (both) | Model/GameTimeSync.cs; MiscHandler.cs:184-195; AdminCommands.cs:411/472 | reader FUN_1431da350, writer FUN_1431da230 | OK | u64 (f64 time), u32 (f32 speed), bool, both directions |
| Synchronization 0xC3 (both) | Model/Sychronization.cs; MiscHandler.cs:371-389 | reader FUN_142c24b00, writer FUN_142c15370 | OK | 6×u64 + bool both ways |
| KeepAlive 0x63 (C→S) | Model/KeepAlive.cs; MiscHandler.cs:53 | writer FUN_142c14f90 | OK | u16 + u32 game time |
| ClientIsReady 0x10 (C→S) | Model/ClientIsReady.cs; MiscHandler.cs:228 | send part_0067.c:9593 → FUN_142c43790 | OK | header only |
| ClientFinishedLoading 0xD (C→S) | Model/ClientFinishedLoading.cs; MiscHandler.cs:348 | FUN_142c43420 | OK | header only |
| ClientUpdate ZonePopulation 0x29/0x1F (S→C) | Model/ClientUpdateZonePopulation.cs:30; MiscHandler.cs:298 | FUN_142c49410 (0.589 FUN_142baf8a0 the same) | **MISMATCH (latent; OK on the wire today)** | The client reads a **fixed 4 × u32** (`lVar6 = 4` loop) after the i16 sub, then requires exact consumption. The model writes `u32 count, count × u8, 3 × u32`. It is right only because `Populations` is always empty (0 + three u32 = 16 bytes). Any entry added → 1-4 extra bytes → the packet is dropped |
| ClientUpdate MovementVersion 0x29/0x26, WorldDisplayInfo 0x59 (zone-in) | MiscHandler.cs:307-308 | FUN_142c48af0 (u8), FUN_142c4b090 (u32) | OK | `01`; `00000000` |
| PlayerUpdate 0x1F EffectTagCompositeEffectsEnable (zone-in) | MiscHandler.cs:282 (3-byte enum 0x26001F) | dispatcher case 0x1e → FUN_142c4bf90 | OK | u32 sub + bool |
| TerrainCell 1 ClientCellRequest (C→S) | Model/TerrainCellClientCellRequest.cs; TerrainHandler.cs:23 | FUN_143149190 (FUN_1431451d0 hdr, FUN_142c02460 u64, FUN_143147500 list) | OK | u16 + u32 sub, u64, u32 n × {u64 cell, 4×u32 = the model's 2×u64}; the opcode match eats `EC 00 01 00` and the model eats the `00 00` |
| TerrainCell 2 PendingRequestTimeout (C→S) | Model/TerrainCellPendingRequestTimeout.cs; TerrainHandler.cs:34 | FUN_143149340 | OK | hdr, u64, u64 |
| TerrainCell 3 ClientCellUpdateResponse (S→C) | WS/Game/Building.cs:564 | FUN_1431465e0 | OK | u32 sub, u64 cell, bool, str |
| TerrainCell 4 UpToDateCells (S→C) | Model/TerrainCellUpToDateCells.cs | FUN_143149ca0 | OK | u32 sub, i32 n × u64 |
| TerrainCell 5 CellsUpdate (S→C) | Env/Terrain/TerrainStore.cs:93 / 117 / 218; TerrainEdits.cs:1636; TerrainLod.cs:97; senders ZoneTerrain.cs:55/108, TerrainHandler.cs:120/131, DevCommands.cs:214/260 | FUN_1431464f0 (cells FUN_143148950 → FUN_143146320 → layer FUN_143146cf0; transactions FUN_143148b80) | OK | u32 sub, n × {u64 cell, u32 version, n × {u32 layer, u32 version, u8 flag, i32 len + bytes}}, n × u64 transaction, bool, bool |
| TerrainCell 6 MaterialChangeInfo (S→C) | not sent | FUN_1431468f0 (u16, u32 sub, u16, map of BoxPartialBitSet, u64) | – | the server never sends it (nothing to compare) |
| TerrainCell 7 CellBlockDamage (S→C) | Env/Terrain/TerrainStore.cs:268 (BuildBlockDamage); DevCommands.cs:213/235 | FUN_143146420 → FUN_143148840 / FUN_143148700 | OK | u32 sub, n × {u64 cell, n × {u16 block, u8 health}}, u64 transaction |
| TerrainCell 0xA ModifiedCellManifest (S→C) | Env BuildManifest (TerrainStore.cs ~190-211); ZoneTerrain.cs:155 | FUN_1431469e0 | OK | u32 sub, i32 len + packed bytes |
| TerrainCell 0xB ModifiedCellManifestUpdate (S→C) | WS/Game/Terrain/ZoneTerrain.cs:133 | FUN_143146ad0 | OK | u32 sub, u64, u32, u32, u8 |
| TerrainCell 0xC ZoneProcessingComplete (S→C) | Env TerrainStore.BuildProcessingComplete; DevCommands.cs:215/236 | FUN_143152f70 case 0xc (inline) | OK | u32 sub, u64. Note: coverage-695.md lists 0xC as "no client class"; it has an inline reader |
| TerrainCell 0x10 DetectVeins / 0x11 EndDetectVeins (S→C) | WS/Game/Terrain/VeinOverlay.cs:44-83 | FUN_1431466b0 (+ FUN_143146dd0 / FUN_143148470); case 0x11 inline | OK | {u32, 3×f64, 3×f64, u32} list, 3×f32, u32 n (0), u32 overlay; End: u32 sub, u32 |
| VoxelConstruction 7 Response (S→C) | WS/Game/Building.cs:131 | FUN_143162830 case 7 (inline) | OK | u8 sub, u32 request type, u32 result, bool. Note: coverage-695.md says "no client class"; there is an inline reader |
| VoxelConstruction 0xA ResponseObserved (S→C) | Building.cs:540 | FUN_14315b8a0 (+ FUN_14315c3a0) | OK | u8, u32 op, 2×(4×f64), 3×u32, bool |
| VoxelConstruction 6 ClipboardData (S→C) | WS/Game/BuildingClipboard.cs:390 (+ WritePropData :325) | FUN_14315b440 (PropData FUN_142fa7fb0, materials FUN_143101270, cells FUN_143101860) | OK | u8, PropData list {u64, u32 name, u32 def, 10×f32, n×16 B, u64, u64, u32, n×20 B, u32 tint, str, str, 3×u32}, n × {u16, u32}, cell list (copied from the client's request), 3×u32, u32, bool |
| VoxelConstruction 8 ZoneUnknownPropList / 9 ZoneMissingPropList (S→C) | BuildingClipboard.cs:632 / 645 | FUN_14315d360 (→ FUN_14315c210 map {u32; u32, u32}) / FUN_14315d2c0 (2 × i32 array) | OK | |
| VoxelConstruction 0x16 / 0x17 EnvOwnership (S→C) | WS/Game/Terrain/ZoneTerrain.cs:311 / 328 | FUN_14315d180 / FUN_14315b590 | OK | 0x16: u8, n × u64. 0x17: u8, u64, u64 cell, i32 len + bytes, str list, u64 list, u64 list (n = 0), u32 |
| VoxelConstruction 0x1B ZoneDisplayNotification (S→C) | not sent (ZoneTerrain.cs:275 uses a different message) | FUN_14315b9d0: u8, u32 text, bool | – | |
| VoxelConstruction C→S 1 ApplyDiff / 2 Selection / 3 UndoRedo / 4 Ownership / 5 OffGridPaste | Model/InteractionMessages.cs:192 (SubRequest8) → Building.cs:585 | 4: FUN_143129db0 (list {u64, u32}, then 3 × u64 the server ignores) | OK | header u16 + u8 sub. Sub 0 AddTemporaryBlockRequest is not handled (logged as Unanswered), which is not a wire error. coverage-695.md mislabels subs 1 / 2 as S→C |
| WorldEditing 0x10C 0-7 (C→S relayed to others) | WS/Network/WorldSession.cs:1510 → WS/Game/WorldEditing.cs (raw relay) | writers FUN_14307c* / dispatcher FUN_1430838d0 | OK | Byte-for-byte relay of the client's own class (u16, u32 sub, u32, body). The dispatcher returns early when the viewer's option +0x153b ("Show Previews of Others' Build Actions") is off. Expected behaviour |
| WorldEditing 0x10C/8 PlayEffect (S→C) | WS/Game/VoxelDestruction.cs:267-298 (raw, 46 bytes) | FUN_1430838d0 case 8 → FUN_14307c4d0 | OK | u16 0x10C, u32 8, u32, u32 effect, vec4 min, vec4 max |
| WarpNetwork 0x113/2 ZoneNetwork (S→C) | WS/Game/WarpNetwork.cs:317-353 | FUN_1435025c0 (+ FUN_1435028f0 / FUN_143502a10) | OK | u32 sub, u32, u32, map {u32 layer; map {u64; u64, u32, u32, u64, 4×u32}; u32}, u32 |
| WarpNetwork 0x113/4 ClientRequest (C→S) | Model/InformationalMessages.cs:54; MiscHandler.cs:121; WarpNetwork.cs:300 | FUN_143502cb0 → FUN_143502130 | OK | u32 sub, u32 type, u32, u32, u64, u64 (28 bytes after the opcode) |
| PlayerUpdatePosition 0x7F (S→C: players, creatures, platforms) | Model/PlayerUpdatePosition.cs; MovementFanout.cs:73; WorldSession.cs:1122; Creatures.cs:2258-2310 (MovementBody); Spire.cs:828 | FUN_142c4dd40 (body FUN_142c22390) | OK | 2-bit-length transient id, movement body, 4×f32, exact. Type 0 = 3×p2, 4×p5, 3×p5, 4×p2, 4×p2, p1, p5, then a 2-bit-length state, then **one more packed int** (case 0 falls through into case 5, +0x84). MovementBody writes that as the final `00`. Correct |
| PositionUpdate channel (C→S) | WS/Network/PositionUpdate.cs; WorldSession.cs:1458 | same body reader / writer | OK | types 0/2/3 decoded as in the client switch |
| PlayerUpdateManagedPosition 0xC7 (C→S) | Model/PlayerUpdateManagedPosition.cs | – | OK | kept raw |
| CorrectNpcHeights 0x114 (C→S) | Model/CorrectNpcHeights.cs; MiscHandler.cs:49 | part_0099.c:620 (FUN_1433278c0 path) | OK | u32 time, u64 list, f32 list (lenient read) |
| ZoneSafeTeleportRequest 0x7C (C→S) | Model/ZoneSafeTeleportRequest.cs | FUN_142c709d0 | OK | header only |
| ClientUpdate TeleportToLocation 0x29/0xC (S→C) | WS/Game/Teleport.cs:106-115 | FUN_142c1d530 | OK | vec4, vec4, u8, bool, bool, u64, u32 |
| ClientUpdate Hitpoints 0x29/1 (S→C) | WS/Game/Health.cs:655-665 | FUN_142c1c7c0 | OK | u32, u32 |
| PlayerUpdate 0x03 UpdateHitpoints (creatures) | WS/Game/Creatures.cs:2124 (+ :2173) | FUN_142c4dc60 → FUN_142c2c9a0 | OK | u32 sub, u64, u32, u32, u32 |
| PlayerUpdate 0x67 CombatResourceEvent | Creatures.cs:2148-2165 | FUN_142c4b910 → FUN_142c278c0 | OK | u64, u64, u64, 5 × u32 (i32 change, type, u32, u32, ability name), bool, u32 |
| PlayerUpdate 0x39 KnockedOut | WS/Game/Health.cs:506-547 | FUN_142c4c2f0 → FUN_142c28930 (info FUN_142c25570, stats FUN_142c30850, report FUN_142c1e790) | OK | info: str, u64, 4×u32, u8, 9×u32, {n×3u32}, u32, str, 2×u32, 7×u32, u64, 2×u32, 3×u32 (FUN_142c381d0), u32; stats u64 + 12×u32; report 2×u8, u32, 3×u64, 8×u32, 4×u32, 4×u32; trailing u32 |
| PlayerUpdate 0x0B PlayCompositeEffect | WS/Game/HitEffects.cs:134 (character), :113 (location) | dispatcher case 0xa → FUN_142c4c780 → FUN_142c2a0c0 | OK | u32 sub, polymorphic target (type byte + target, read through its vtable), u64, 3×u32, 3×bool. The target sub-readers were not re-read: "header-only" in coverage-695 [I] |
| Combat 0x24/0x0A EnableBossDisplay | Creatures.cs:2045 | FUN_1433cb410 → FUN_1433c8f50 | OK | i16 sub, u64, bool, exact. `Write((ushort)0x0A)` is 2 bytes (GamePacketWriter.cs:60 now has Write(ushort)) |
| Combat 0x24/0x0D LastDamageTime | Health.cs:231 | FUN_1433cb100 (dispatcher case 0xc) | OK | i16 sub, u64, exact |
| Projectiles 0xEE/1 Create, /3 Delete | WS/Game/Projectiles.cs:54 / 83 | dispatcher part_0088.c:31030; FUN_14318ce10 / FUN_14318d320 | OK | Create: i16, u32 id, u32 def, 3f, 3f, u64, bool, f32, 3f, f32, u64, f32, 3f, u64. Delete: i16, u32, 4f, u32, bool, u64 |
| Abilities 0x27/0x12 PlayAnim (creatures, props) | Creatures.cs:2236 | FUN_142e030c0 (hdr FUN_142e03920: i16 sub, i16 sub, u8, u32) | OK | hdr, u64, u32, 4×f32 |
| Abilities 0x27/0x0C EffectStart, 0x1C EffectInitialized, 0x0D EffectEnd (statuses) | WS/Game/Buffs.cs:165 / 132 / 220; WS/Game/Gathering.cs:400 (HarvestFail) | FUN_142e028b0 / FUN_142e02640 / FUN_142e02440 | OK (edge case below) | Every field matches. EffectEnd reads +0x54 / +0x58 only when +0x50 != 1; Buffs writes 6 and the gameplay HarvestFail reasons are 3 / 8 / 9 (all != 1). Only the dev command `DevCommands.cs:1497` can pass 1, which leaves 8 extra bytes and the packet is dropped (exact) |
| Abilities CasterInfo for creatures | – | – | n/a | Creatures never get CasterInfo (they use PlayAnim / Projectiles / CombatResourceEvent). The CasterInfo replies in MiscHandler are for the player's own abilities (another group) |
| PlayerUpdate Knockback 0x02, MobMovement 0x72, ExpectedSpeed, LaunchProjectile 0x31 | not sent | – | – | |
| Collision 0xC5 (Damage sub 1) | not used | id table only (packet_ids_695.tsv:1099; no class) | – | AbilityCollisionReport 0x27/0x18 (C→S) Model/AbilityCollisionReport.cs vs writer FUN_142e67590: u64, u64 list, vec4 list, read leniently. OK |
| ShowSystemMessage 0x69 | WS/Game/SystemMessages.cs:25-41 | FUN_142c249f0 | OK | u32 text, str key, n × {str, str, u32}, u32, u32 |
| ProceduralDataMonitor 0x109 (C→S) / 0x10A (S→C), admin page 28 | Model/AdminMonitorMessages.cs; WS/Game/AdminMonitor.cs:147 / 195-275 | FUN_142c4e3b0 → FUN_142c2d290 (layers FUN_142c2df20, rows FUN_142c25270 / FUN_142c25400) | OK | matches the doc comment; the enum wire forms 0x0901 / 0x0A01 match |
| InstancedGame 0x10D/2 AdminMonitor (page 29 push) | WS/Game/InstancedGames.Monitor.cs:94 | part_0107.c:39333 → FUN_1434d6be0 | OK | u32 sub, n × {str, str, u32}; the client applies it only while page 0x1d is shown |
| **HeroicMovement 0x105 (C→S, relayed S→C)** | As first audited: WS/Network/WorldSession.cs ~1546 (raw relay) + WS/Game/PlayerManager.cs:281. **Fixed concurrently (files dated 23:09, OPEN-LEADS L166):** Model/InteractionMessages.cs:231 HeroicMovementRequest + WS/Game/HeroicMovement.cs | writers FUN_142e5ede0 / FUN_142e84ce0 / FUN_14358bdf0 / FUN_142e5f160 / FUN_142e5ef80; S→C dispatcher FUN_142cdc570 (part_0065.c:14745) | **MISMATCH (fixed in the working tree during this audit)** | Before: the enum's `HeroicMovement = 0x0501` (ZoneMessageOpcode.cs:571, wire form) made GetOpcode **match** 05 01, so the raw branch for `opcode == null` never ran. HandleZonePacket found no model and logged "unreadable HeroicMovement(0x501)". Other players saw no hooks or swimming, and objective 1309 never counted. Now: there is a model {u8 sub, u64 entity, body} and a handler that relays subs 1-4 (not 5) with the sender's guid and coalesces SwimmingUpdate. I re-checked it against the layout below: **OK** |

### HeroicMovement 0x105: the layout the server should expect [P]

Common header (FUN_142e5eaf0, used by every writer): `u16 0x0105`, `u8 sub`, `u64 entity` (11 bytes). The entity is
**the local actor's own id** (part_0072.c:29608 `local_b8 = *puVar10` from actor +0x278 vfunc +0x28; part_0111.c:12340
swimming). The old raw-path comment said "0"; the new HeroicMovementRequest documents it correctly. The relay
overwrites it with the sender's guid anyway.

| Sub | Class | Body after the header | Total | S→C handled? |
|---|---|---|---|---|
| 1 | FireGrapplingHook (writer FUN_142e5ebc0, reader FUN_142c1fe00) | vec4 (4×f32, +0x20), vec4 (+0x30), u32 (+0x40), u32 (+0x44), bool (+0x48) | 52 | yes, `FUN_142ea4a10` |
| 2 | SetHeroicMovement (writer FUN_142e84ce0, reader FUN_142c49910) | bool (+0x20) | 12 | yes, `FUN_142ec2740` |
| 3 | SwimmingUpdate (writer FUN_14358bc70, reader FUN_142c1ffa0) | u8 flags (0x80, 0x40), f32 (+0x24); **only if flags & 0x40**: f32 (+0x28), f32 (+0x2c) | 16 or 24 | yes, `FUN_142ea4a20` |
| 4 | TerminateGrappleHookEffects (writer FUN_142e5f160) | – | 11 | yes, `FUN_142ea4f30` |
| 5 | HeroicMovement (writer FUN_142e5ef80, sent at part_0072.c:29603) | u32 (+0x20; the send site sets 1) | 15 | **no**: FUN_142cdc570 handles 1-4 only |

The S→C handler ignores a packet whose entity equals the local player's id (`*plVar8 != lVar6`). It finds the actor by guid
(FUN_142f85390). Only sub 2 is read leniently (FUN_142c49910 is called with param_4 = 1); the others do not check for
leftover bytes. The dispatcher needs ≥ 8 bytes for the entity.

## Other findings (not wire errors in this group)

1. **The same root cause dropped design screenshots (outside group d; also fixed concurrently).** `MarketplaceImageBase =
   0x0701` (ZoneMessageOpcode.cs:573) also matched in GetOpcode, so the raw branch (`07 01 01 00` →
   Game.Templates.StoreImage) never ran. It now has a model (InteractionMessages.cs:251 MarketplaceImageRequest, u16 sub)
   and a handler (Templates.cs:854 HandleImage). StoreImage gets the same body offset as before (after the u16 sub).
   The enum comment was rewritten (ZoneMessageOpcode.cs:561-564). `SocialShareBase 0x0401` is caught by Telemetry
   (harmless), and `ToolBase 0x1701` has a model.
2. Stale notes: `TerrainCellUpToDateCells.cs` ("Write(ushort) emits 4 bytes") and docs/protocol.md §1.5 ("GamePacketWriter.Write
   has no ushort overload") are both out of date: GamePacketWriter.cs:60 `Write(ushort) => WriteUShort`. The code is correct
   either way, because every site writes the high half explicitly.
3. coverage-695.md §0xEC / §0xF4: TerrainCell 0xC ZoneProcessingComplete, TerrainCell 0x11 EndDetectVeins and
   VoxelConstruction 7 Response are listed as "no client class", but they have inline readers in FUN_143152f70 /
   FUN_143162830. VoxelConstruction subs 1 and 2 are listed as S→C, but they are C→S. These are documentation fixes only.
4. VoxelConstruction sub 0 AddTemporaryBlockRequest (C→S, client FUN_14315c7e0) is not handled (Building.Handle falls
   through to Unanswered). This is a feature gap, not a wire error.

## FIX list (ranked by player impact)

1. **HeroicMovement relay (grapple / swim visible to others, objective 1309): done in the working tree during this
   audit** (Game/HeroicMovement.cs + HeroicMovementRequest, 23:09). I checked it against the layout above: header
   {u8 sub, u64 entity}, subs 1-4 relayed with the sender's guid, sub 5 not relayed, Swim body ≥ 5 bytes, and OR-ing
   the dive flag 0x80 does not change the body length (only 0x40 adds the two f32). Remaining: an in-game test (two
   clients, grapple + swim).
   Optional hardening: drop a relayed body whose length doesn't match its sub (1: 41, 2: 1, 3: 5 / 13, 4: 0 bytes after
   the header). The receiving readers for 1, 3 and 4 do not check for leftover bytes, so a malformed body would be
   misread rather than rejected.
2. **MarketplaceImage uploads (outside the group, same root cause): done in the working tree during this audit**
   (MarketplaceImageRequest + Templates.HandleImage). Remaining: an in-game test (save a design, check the stored JPEG).
3. **ClientUpdateZonePopulation model (latent).** Model/ClientUpdateZonePopulation.cs: replace the list + 3 u32 with
   `public uint[] Values { get; } = new uint[4];` and write and read exactly 4 × u32 (client FUN_142c49410 /
   0.589 FUN_142baf8a0: fixed loop of 4 after the i16 sub; exact consumption). The bytes on the wire are unchanged
   (16 zero bytes), so this is safe now and prevents a silent drop later.
4. **HarvestFail reason 1 (dev only).** WS/Game/Gathering.cs:400: write the trailing `resource, 0u` only when
   `reason != 1` (client FUN_142e02440 reads +0x54 / +0x58 only then). Otherwise `dev harvestfail 1` sends a packet
   the client drops.
5. Documentation: update the TerrainCellUpToDateCells comment and protocol.md §1.5 (Write(ushort) is 2 bytes now). In
   coverage-695.md, mark TerrainCell 0xC / 0x11 and VoxelConstruction 7 as inline-read S→C, and VoxelConstruction subs 1 / 2
   as C→S.
