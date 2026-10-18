# Wire audit, group a (2026-10-17): PlayerUpdate 0x26, ClientUpdate 0x29, SendSelf 0x0F, Equipment 0xCB, Inventory 0x2D, ActionBar 0x9A, Loadouts 0xBD, Abilities 0x27 (CasterInfo, casts, buffs/effects), Resources 0xC4, Harvest

Read-only audit against the 0.695 client (`re/decomp_all_695`, `pkt.py` / `fn.py`). Brief: `audit-wire-brief.md`.
[P] = client function + offset read in the decompile; [I] = inferred (coverage-695 "same" closure, or proven only by the
client's behaviour in play).

## 0. How the wire is built (checked first)

- `WorldSession.GetOpcodeBytes` (Network/WorldSession.cs:1267) writes a PlayerUpdate enum value `0x2600ss` as
  `26 00 ss 00 00 00` — correct for 0.695 [P: dispatcher FUN_142cdfa60 reads `*(int *)` after the u16 and switches on
  `sub - 1`]. A value of the form `0x2600ss00` comes out as `26 00 ss 00 00 00 00`: the trailing enum byte lands as a
  **stray first body byte**. The method's comment says no S→C model uses one — one does (Barbershop, see M3).
- C→S `GetOpcode` (WorldSession.cs:1320) accepts `26 00 ss 00 00 00`, looks up `0x2600ss` then `0x2600ss00`, body at +6. OK.
- `GamePacketWriter.Write(ushort)` exists now (Shared/Network/GamePacketWriter.cs:60 → WriteUShort), so the
  `ushort` sub writes in Buffs.WriteHeader / ItemUses / InventoryTooltipReply are 2 bytes. (docs/protocol.md §1.5 still
  says a ushort goes out as 4 bytes — stale.)
- Reader wrappers of the form `FUN_142c4xxxx(pkt, buf, len, allowTrailing)`: `allowTrailing = 0` means the body must
  be consumed exactly or the packet is dropped.

## 1. Table

| Packet | Server writer / reader (file:line) | Client reader / writer (0.695) | Verdict | Detail |
|---|---|---|---|---|
| PU 0x01 RemovePlayer S→C | Model/PlayerUpdateRemovePlayer.cs:16 | case 0 FUN_142c4cd60 → FUN_142c2a960 [P] | OK | i16 version 0, u64 guid; exact |
| PU 0x03 UpdateHitpoints S→C | Game/Creatures.cs:2124 (HitpointsBody), :2173 | case 2 FUN_142c4dc60 → FUN_142c2c9a0 [P] | OK | u64, u32 cur, u32 max, u32 |
| PU 0x0A RemoveTemporaryAppearance S→C | Handlers/PlayerHandler.cs:49-51 | case 9 FUN_142c4ce40 [P] | OK | u64; exact |
| PU 0x0A RemoveTemporaryAppearance C→S | Model/PlayerUpdateRequests.cs:14 (raw) | send template, header FUN_142c13350 [P] | OK | body kept raw |
| PU 0x0B PlayCompositeEffect S→C | Game/HitEffects.cs:113 (PlayAt), :134 (Build) | case 10 FUN_142c4c780 → FUN_142c2a0c0 [P] + target object read via vtable [I] | OK | target, u64, u32, u32, u32, 3 bools; target part unchanged per coverage [I] |
| PU 0x14 SetDisposition S→C | Model/PlayerUpdateSetDisposition.cs:58 | case 0x13 FUN_142c4f960 → FUN_142c2b5a0 [P] | OK | u64, u32 |
| **PU 0x1B CustomizationChange S→C** | Game/Barbershop.cs:151 (opcode `PlayerUpdateCustomizationChange` = 0x26001B00) | case 0x1A FUN_142c4b980(…,1) → FUN_142c27ad0 [P] | **MISMATCH (M3)** | body right (u64, bool, FUN_142c18710 model data, u32 n × attachment FUN_142c185c0), but the 4-byte enum value puts a stray `00` before the body → guid misread, entity not found, look not applied |
| **PU 0x1C PlayerTitle S→C** | Game/Titles.cs:164 (UpdatePacket), sent :160 and PlayerManager.cs:142 | case 0x1B FUN_142c4c860(…,0) → FUN_142c2a390 [P] | **MISMATCH (M2)** | client reads **title {u32 id, u32 type, u32 name} first (FUN_142c25e20 → +0x20), then u64 guid (+0x18)**; the handler looks the entity up by +0x18 and applies +0x20. Server writes guid first. Same order in 0.589 (FUN_142b909a0) — never worked |
| PU 0x1F EffectTagCompositeEffectsEnable S→C | Handlers/MiscHandler.cs:282 | case 0x1E FUN_142c4bf90 [P] | OK | bool; exact |
| PU 0x35 MemberStatus S→C | Game/Membership.cs:347, :356 | case 0x34 FUN_142c4f210 → FUN_142c25ad0 [P] | OK | u64, bool |
| PU 0x39 KnockedOut S→C | Game/Health.cs:506 (BuildKnockedOut), :215 | case 0x38 FUN_142c4c2f0 → FUN_142c28930 [P] | OK | u64; 2 × {info FUN_142c25570: str, u64, 4 u32, u8, 9 u32, model data, 7 u32, u64, 2 u32, 3 u32 fixed (FUN_142c381d0), u32; stats FUN_142c30850: u64 + 12 u32}; report FUN_142c1e790: 2 u8, u32, 3 u64, 8 u32, 4 u32, 4 u32; u32 — field for field |
| PU 0x3B Respawn C→S | Model/PlayerUpdateRespawn.cs:15 | writer part_0104 16168 (type = param_2 first) [P] | OK | u32 type read, rest skipped |
| PU 0x3D ReadyToReviveResponse C→S | Health.cs:470 reads body[0] | writer FUN_143435120: bool (+0x18), u64 guid (+0x20) [P] | OK | |
| PU 0x42 StartRevive S→C | Game/Health.cs:395-406 | case 0x41 FUN_142c4d900 → FUN_142c2c030 [P] | OK | u64, u64, u32 |
| PU 0x43 StopRevive S→C | Game/Health.cs:445-453 | case 0x42 FUN_142c4db80 → FUN_142c2c400 [P] | OK | u64, u64, bool |
| PU 0x44 ReadyToRevive S→C | Game/Health.cs:457-465 | case 0x43 FUN_142c4c940 → FUN_142c2a510 [P] | OK | u64, u64, u64, bool, u32 |
| PU 0x49 CurrencyChange S→C | Network/WorldSession.cs:691 | case 0x48 FUN_142c4a8c0 → FUN_142c38fc0 [P] | OK | u64, u32 n × {u8, u32} |
| PU 0x57 AFK S→C | Game/Afk.cs:61 | case 0x56 FUN_142c4b3e0 → FUN_142c26160 [P] | OK | u64, bool |
| PU 0x58 AddLightweightPc S→C | Model/PlayerUpdateAddLightweightPc.cs:47 | case 0x57 FUN_142c26e50 [P] | OK | u64, 2-bit tid, identity (3 u32, 2 str), u8, u32, u32, vec4, vec4, f32, 3 bools, u64, 2 u32, 5 bools, u8 movement version (+0x12d), 3 u64, 2 u32, u64, u8; exact |
| PU 0x59 AddLightweightNpc S→C | Model/PlayerUpdateAddLightweightNpc.cs:35 | case 0x58 FUN_142c26790 [P] | OK | base incl. attachment block FUN_142c228c0 (u64 0 = none), 3 × f64 origin (FUN_142c383f0), effect-tag u8 + list (FUN_142c3ba30), spawn-animation list (FUN_142c39ea0); exact |
| PU 0x62 AddLightweightProp S→C | Model/PlayerUpdateAddLightweightProp.cs:105 | case 0x61 FUN_142c273d0 = base + u64 + physics FUN_142c40140 (mode 1: u8, u32, u8, u32) + list FUN_142c3b8a0 + u32, u64, bool [P] | OK | |
| **PU 0x5B LightweightToFullPc S→C** | Model/PlayerUpdateLightweightToFullPc.cs:52 | case 0x5A: FUN_142c294b0 (bool, byte array), FUN_142c22390 (movement), **FUN_142c2cd60 (embedded UpdateStat: u16, u32 sub, u64, list)**, FUN_142c1d7b0 (embedded CalculatedStats: u16, i16, u64, list) [P] | **MISMATCH (M1)** | the embedded PlayerUpdate UpdateStat header is written `26 00 53` (0.589 u8 sub); 0.695 reads a u32 sub → everything after is 3 bytes short, the stream error flag is set and case 0x5A drops the packet (`if ((char)uStack_7e20 == '\0')` else `break` → return 0). Blob (FUN_142c21350), movement and CalculatedStats parts otherwise match |
| PU 0x5C LightweightToFullNpc S→C | Model/PlayerUpdateLightweightToFullNpc.cs:50 | case 0x5B FUN_142c4c3d0 → FUN_142c28ba0 [P] | OK | matches the documented field list; Target u8 read through a vtable [I] |
| PU 0x5E FullCharacterDataRequest C→S | Model/PlayerUpdateFullCharacterDataRequest.cs:17 | writer part_0074 26853: u64 (+0x18) [P] | OK | |
| PU 0x63 LightweightToFullProp S→C | Model/PlayerUpdateLightweightToFullProp.cs:14 | case 0x62 FUN_142c4c440 = FUN_142c28ba0 + str, u32, byte array [P] | OK | |
| PU 0x67 CombatResourceEvent S→C | Game/Creatures.cs:2148 | case 0x66 FUN_142c4b910 → FUN_142c278c0 [P] | OK | 3 u64, 5 u32, bool, u32 |
| PU 0x69 InteractionInfoRequest C→S | Model/InteractionMessages.cs:72 | writer part_0078 34676 (u64 array) [P] | OK | u32 n × u64 |
| PU 0x6A InteractionInfoReply S→C | Model/InteractionMessages.cs:88 | FUN_142d54c10 (BaseClient side), FUN_142c285e0 → entry FUN_142c202b0 [P] | OK | u64, u32, 4 u32, u32, f32, f32, bool; exact |
| PU 0x6B DynamicInteractionFlagging S→C | Model/InteractionMessages.cs:53 | FUN_142d54c10 → FUN_142c4beb0 [P] | OK | bool, list<u64> |
| PU 0x6C Loading C→S | Model/InformationalMessages.cs:42 (0x26006C00) | — | OK | read via 0x2600ss00 fallback, ignored |
| PU 0x71 BindingCustomization C→S | Model/PlayerUpdateBindingCustomization.cs:10 | — | OK | ignored |
| PU 0x18 / 0x1A / 0x73 / 0x77 requests C→S | Handlers/PlayerHandler.cs:26-64 | StripEffect FUN_142d3c100 (u32); PreferredLanguages part_0080 15538 (u64, u32); EmoteStringById part_0101 38195 (u32 text) [P] | OK | offsets the handlers use match |
| PU 0x79 DisplayPropText S→C | Game/Props.cs:191 | case 0x78 FUN_142c4be40(…,1) → FUN_142c27f40 [P] | OK | u64 question (+0x18), u64 speaker (+0x20, looked up), str, u32 mode, u32 seconds, bool |
| PU 0x7A SetPropTint S→C | Game/Props.cs:1356-1362, :1520 | case 0x79 FUN_142c4d540 [P] | OK | u64, str; exact |
| PU 0x7C UpdatePropName S→C | Game/Props.cs:1943 | case 0x7B FUN_142c4de60 [P] | OK | u64, str |
| PU 0x80 SetPlayerBio C→S | Model/PlayerUpdateSetPlayerBio.cs:20 | packets-695 §1 [P] | OK | str |
| Position 0x7F S→C | Model/PlayerUpdatePosition.cs:14, MovementFanout.cs:69 | FUN_142c4dd40 / movement FUN_142c22390 [P] | OK | 2-bit tid, body, 4 f32; works in play |
| ClientUpdate 0x01 Hitpoints | Game/Health.cs:661-665 | FUN_142c1c7c0 [P] | OK | u32, u32 |
| ClientUpdate 0x02 ItemAdd | Model/ClientUpdateItemAdd.cs:16 | FUN_142c1c880 (byte array) [P]; item record [I, works in play] | OK | |
| ClientUpdate 0x03 ItemUpdate | Model/ClientUpdateItemUpdate.cs:24 | FUN_142c1ca60 [P] | OK | u64, 3 u32, u64, 2 u32, 2 × FUN_142c20e60 {5 u32, list, list}, 2 u32 |
| ClientUpdate 0x04 ItemDelete | Model/ClientUpdateItemDelete.cs | FUN_142c4eb60 [P] | OK | u64 |
| ClientUpdate 0x0C TeleportToLocation | Game/Teleport.cs:106-115 | FUN_142c1d530 [P] | OK | vec4, vec4, u8, bool, bool, u64, u32 |
| ClientUpdate 0x13 CoinCount | Network/WorldSession.cs:721 | FUN_142c48520 [P] | OK | u32 |
| ClientUpdate 0x1B DoneSendingPreloadCharacters | Model/ClientUpdateDoneSendingPreloadCharacters.cs; MiscHandler.cs:216, :269 | FUN_142c48880 [P] | OK | bool |
| ClientUpdate 0x1F ZonePopulation | Model/ClientUpdateZonePopulation.cs:34; MiscHandler.cs:298 | FUN_142c49410: **4 × u32 fixed**, exact [P] | OK (latent) | model writes `u32 n, n × u8, 3 u32`; only correct while the list is empty (it always is: 16 bytes) |
| ClientUpdate 0x20 RespawnLocations | Model/ClientUpdateRespawnLocations.cs | FUN_142c1d2a0 → FUN_142c2ee10 [P] | OK | u64, u8, vec4, 4 u32, str, u32, bool, u32, bool, pair list |
| ClientUpdate 0x26 MovementVersion | MiscHandler.cs:308 | FUN_142c48af0 [P] | OK | u8 |
| ClientUpdate 0x2C UpdateCalculatedStats | Model/ClientUpdateUpdateCalculatedStats.cs; GearStats.cs:160; WorldSession.cs:488 | FUN_142c49070 / FUN_142c1d7b0 [P] | OK | u64, list {u32, f32} |
| SendSelfToClient 0x0F | Model/SendSelfToClient.cs:2199 | FUN_142c19bd0 [P tail; I body] | OK | tail: admin bool (+0x7b0d8), FUN_142c3d470 (list + u32), str bio (+0x7aea8); the reader aborts unless consumed exactly (FUN_142b4e1b0), and login works |
| Equipment 0xCB 2 SetCharacterEquipmentSlot | Model/EquipmentSlotMessages.cs:19 | FUN_142e41b40: header FUN_142e415f0 (u16, u8 sub, u32 profile, u64 guid), u32 slot, u64 item, str, str, attachment FUN_142c185c0 (4 str, 4 u32, bool) [P] | OK | |
| Equipment 0xCB 3 UnsetCharacterEquipmentSlot | Model/EquipmentSlotMessages.cs:57 | same header + u32 [I] | OK | |
| Inventory 0x2D 0x12 TooltipReply | Model/InventoryTooltipReply.cs | FUN_142f17070 [P layout, I values] | OK | coverage "same"; works in play |
| Inventory 0x2D 0x17 SetSize / 0x1D SetVoxelStorage | Game/ItemUses.cs:236-252 | FUN_142f17440 (u32, u32) / FUN_142f17500 (u32 +0x1c, u32 +0x18) [P] | OK | |
| Inventory 0x2D 0x18 GrapplingHookEquipped | MiscHandler.cs:293 | dispatcher case 0x18: bool [P] | OK | |
| Inventory 0x2D 0x19 TransferItemResponse | Handlers/InventoryHandler.cs:36-42 | FUN_142f17650 → FUN_142f178a0 (2 locations FUN_142f17960, u64, u32, bool), u32 [P] | OK | echo + result |
| Inventory 0x2D 0x1A LootNotification | Game/LootPinatas.cs:174-186 | FUN_142f171b0 [P] | OK | str, u32, u32, u32 |
| Inventory 0x2D 0x1E ShowActivationFailure | Game/ItemUses.cs:112-118 | packets-695 §10 [P] | OK | u32 text |
| Inventory 0x2D C→S 0x02/0x03/0x0B/0x0E/0x0F-0x11/0x15/0x16/0x1C | Handlers/InventoryHandler.cs:22-110 | coverage-695 0x2D "same" [I] | OK | |
| ActionManager 0x9A 5 UpdateSlot S→C | Model/ActionManagerUpdateSlot.cs (0x9A0005) | FUN_142cd9040 (sub must be 5) → FUN_142c16b50 [P] | OK | u16, u8 5, u32, u32, u32, u64 |
| ActionManager 0x9A 1-4 C→S | Handlers/ActionBarHandler.cs | coverage "same" [I] | OK | |
| Loadouts 0xBD 1 S→C | MiscHandler.cs:291 (captured blob) | dispatcher FUN_142f07370 case 1 → FUN_142f07490 (u16, u8, u32, u32, loadout FUN_142c217f0) [P header] | UNKNOWN | captured body not decoded field by field; one dummy "Default Loadout" |
| Abilities 0x27 3 CasterInfoReply | Model/AbilityMessages.cs:15; raw hex MiscHandler.cs:517, :560 | FUN_142e016b0 [P] | OK | model matches field for field; the raw captured bodies [I] |
| Abilities 0x27 5 AbilityEnd | Game/Abilities.cs:375-385 | FUN_142e00d20 (backtimed header FUN_142e03920: u16, i16, i16, u8, u32), u64, u32, u32, u64 [P] | OK | |
| Abilities 0x27 6 CastFail / 7 CastSuccess | Model/AbilityMessages.cs:274, :303 | FUN_142e013b0 / FUN_142e01530 [P] | OK | |
| Abilities 0x27 0x0C EffectStart | Game/Buffs.cs:165 | FUN_142e028b0 [P] | OK | field for field |
| Abilities 0x27 0x0D EffectEnd | Game/Buffs.cs:220; Game/Gathering.cs:400-418 | FUN_142e02440 [P] | OK | u64, 3 u32, 2 u64, 5 u32 |
| Abilities 0x27 0x12 PlayAnim | Game/Creatures.cs:2236; Game/Emotes.cs:380 | FUN_142e030c0 [P] | OK | u64, u32, vec4 |
| Abilities 0x27 0x14 DefaultInfoReply | MiscHandler.cs:595 | FUN_142e059b0 [P] | OK | u32 |
| Abilities 0x27 0x1C EffectInitialized | Game/Buffs.cs:132 | FUN_142e02640 [P] | OK | u64, 5 u32, u64, 7 u32 |
| Abilities 0x27 C→S (CastAttempt, CasterInfoRequest, FinishChargedCast, CancelCast, DefaultInfoRequest, EffectUpdatesRequest, RequestCancelEffect, DelayedTargetInfo, CollisionReport, Landing) | Model/Ability*.cs, PlayerUpdateBindingCustomization.cs | coverage 0x27 "same" [I] | OK | casts are answered in play |
| Resources 0xC4 2 SetCharacterResource | Game/CharacterResources.cs:59-96 | part_0114 21332 → FUN_142c18870 (FUN_142c2ec50 id/type/variables, 10 u32, u32, 4 u64, 3 bools, u32) [P] | OK | |
| Harvest | Game/Gathering.cs (EffectEnd, RemovePlayer, AddLightweightProp, ToFullProp) | as above | OK | PU 0x37 / 0x38 StartHarvest / StopHarvest are never sent (no wire issue) |

## 2. FIX list (ranked by player impact)

**M1 — LightweightToFullPc embedded UpdateStat header (other players never become full on 0.695).**
`LandmarkEmulator.WorldServer/Network/Message/Model/PlayerUpdateLightweightToFullPc.cs:51-52`:
```csharp
// PlayerUpdateUpdateStat embedded: u16 base 0x26 + u32 sub 0x53 (0.695 reader FUN_142c2cd60).
writer.WriteBytes(new byte[] { 0x26, 0x00, 0x53, 0x00, 0x00, 0x00 });
```
Why: case 0x5A reads the embedded packet with FUN_142c2cd60 (u16, **u32**, u64, list). With `26 00 53` the following
fields are 3 bytes short, the error flag is set and the whole LightweightToFullPc is dropped, so a viewer's client keeps
every other player as the invisible lightweight placeholder (no appearance, stats or equipment). The gateway's header
translation never touched bytes inside a body, so this has been wrong for 0.695 since before 2026-10-17 too [P].

**M2 — PlayerTitle 0x1C field order (titles over other players never change).**
`LandmarkEmulator.WorldServer/Game/Titles.cs:164-172` (`UpdatePacket`): write the title first, then the guid:
```csharp
Write(w, title ?? new Title(0, 0, 0));   // u32 id, u32 type, u32 name text (FUN_142c25e20 → +0x20)
w.Write(session.PlayerGuid);             // u64 player (+0x18, read last; the handler looks the entity up by it)
```
Also correct the comments (Titles.cs:29, MODLOG/docs "{u64 guid, title}"). Optional: fill the title block of the
LightweightToFullPc blob (FUN_142c21350 +0x118, the three u32 the server writes as 0 after the temporary-appearance u32,
PlayerUpdateLightweightToFullPc.cs:85-86) so a player who meets someone already titled sees it without waiting for 0x1C.

**M3 — CustomizationChange 0x1B opcode (Crimson Parlor purchase: look doesn't change for anyone).**
`LandmarkEmulator.WorldServer/Network/Message/ZoneMessageOpcode.cs`: add
`PlayerUpdateCustomizationChangeWire = 0x26001B,` and use it at `Game/Barbershop.cs:151`. The current
`PlayerUpdateCustomizationChange = 0x26001B00` goes out as `26 00 1B 00 00 00 | 00 …`; the stray `00` shifts the guid,
the reader (allowTrailing = 1) still "succeeds", FUN_142f85390 finds no entity and nothing is applied — this is the
2026-10-17 "stays the same after Purchase" path, so that fix has no effect yet.
Hardening (recommended): in `WorldSession.GetOpcodeBytes`, for a PlayerUpdate value with a 4th byte, drop it
(write only `26 00 ss 00 00 00`) or throw in debug builds, since no 0.695 PlayerUpdate opcode carries a byte after the u32.

**Low / latent**
- ClientUpdate 0x1F ZonePopulation (`Model/ClientUpdateZonePopulation.cs`): 0.695 reads exactly 4 × u32
  (FUN_142c49410). Change the model to four u32 fields so a future non-empty `Populations` can't break it.
- Loadouts 0xBD sub 1 captured blob (MiscHandler.cs:291): decode against FUN_142f07490 / FUN_142c217f0 (UNKNOWN).
- docs/protocol.md §1.5 "Use WriteUShort … a ushort goes out as 4 bytes" is stale (`Write(ushort)` exists); §4.1 has
  no PlayerTitle row — add `0x1C PlayerTitle {u32 id, u32 type, u32 name text, u64 guid}`.
