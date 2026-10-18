# Wire audit, group e (2026-10-18): packets the server started writing / reading on 2026-10-18

Scope: `git log --since=2026-10-17T12:00 client-0.695` up to 71e4245 — the merges local/assets (dce93ea, 1a5b831,
ac6ecf8), local/digest (6c2e99a, 3db1e24), local/abilities (1ffa388), local/commerce2 (90e5f68), local/evac (6513c82),
the direct commits 97b5728, e8cbad8, 71e4245, and the store-bundle commits of the same night (03c0ef1, 1c91d45) and
460f555 (MountBase). Method: audit-wire-brief.md; client = `re/decomp_all_695` (pkt.py / fn.py, DECOMP=decomp_all_695).
[P] = client function + offset read in the decompile; [I] = inferred. Worktree: wt-wire2 (local/wire2).

Wire framing checked first: `WorldSession.GetOpcodeBytes` writes PlayerUpdate enum values `0x2600ss` and `0x2600ss00`
both as `26 00 ss 00 00 00` (the stray-byte bug of audit-wire-a M3 is gone); ClientUpdate `0x2900ss00` → `29 00 ss 00`
(u16 base, u16 sub); RecipeBase 0x4200 → `42 00` + u32 sub in the body; InGamePurchase 0x4500 → `45 00` + u16 sub;
0xD000 → `D0 00`; HeroicMovement 0x0501 → `05 01`.

## 1. Table

| Packet | Server writer / reader (file) | Client reader / writer (0.695) | Exact? | Verdict | Detail |
|---|---|---|---|---|---|
| ClientUpdate 0x29/6 UpdateStat S→C (login) | Model/ClientUpdateUpdateStat.cs → Game/CharacterStats.WriteList; sent MiscHandler ClientIsReady | FUN_142cd9980 case 5 → FUN_142c492a0(…,0): u16, i16 sub, i32 n, n × CharacterStat FUN_142c18c40 {u32 id, i8 kind, [kind 0/1: u32 value, u32 base]} [P] | yes (param_4 = 0: `local_30 - local_38 > 0` → drop) | OK | server: u32 n, {u32 id, u8 0/1, u32/f32 value, u32/f32 base 0} — exact |
| PU 0x5B LightweightToFullPc nested UpdateStat (stats 21, 22, 24-29) | Model/PlayerUpdateLightweightToFullPc.cs (`26 00 53 00 00 00`, u64, WriteList) | case 0x5a → FUN_142c2cd60: u16, u32 sub, u64 +0x18, FUN_142c3cef0 (i32 n × FUN_142c18c40) [P] | stream error flag only | OK | same element as above; the following CalculatedStats part unchanged |
| Abilities 0x27/3 CasterInfoReply + ClientExecutedEffectData | Model/AbilityMessages.cs (after Bc4) + Game/MovementEffects.Effect.Write | FUN_142e016b0: … u32 +0xc0, bool +0xc4, **FUN_142e04680 → +0xf8** (i32 n × FUN_142e03a00 = 14 × u32, param_1[0..0xd]), then bool +0xc5 … [P] | — | OK | element: u32 0, u32 type hash, u32 0, 9 × f32, u32 0, u32 0 = 14 × 4 bytes. Hashes = factory registrations part_0070 25208ff: 0x9055911d Glide (FUN_142dff6b0), 0x1c3c381c ChanneledGlide, 0x80d81da0 Jump, 0x7f2bc64f DoubleJump, 0x7faad620 Sprint, 0xf1fae31f ChanneledSprint, 0xea3d9ce0 Fly (each creator's vtable names the class) [P]. Curves: FUN_14354b670 reads +0x0c/+0x10 as param ids (float→int → FUN_1435079e0), +0x14 seconds, ×3 phases [P]; Jump FUN_14354bb30 [P]. Rows 3/9/25/36/202/205/242/271/313/714 of AbilityParameterDefinitions are the constants the comments name [P data]; which rows live used [I] |
| Inventory 0x2D/0x12 ItemTooltipReply keyword list | Model/InventoryTooltipReply.cs (4th int list ← WeaponCombat.ItemKeywordsOf, + Gear armour / accessory affinity) | FUN_142f17070 → record FUN_142f17a30: 3 × FUN_142c34b60, then **FUN_142c34c30 → +0x78** (u32 n × u32) [P] | — | OK | KeywordDefinitions 1-7 = text 940 Fire, 558626 Water, 781 Shadow, 565466 Radiance, 565467 Affliction, 565468 Earth, 565469 Lightning — KeywordOf maps the same [P data] |
| RecipeBase 0x42 sub 0x1F Hide / 0x20 Unhide S→C | Game/RecipeBook.SendHide (`u32 sub, u32 recipe`) | dispatcher FUN_142f39870 `case 0x1f` / `case 0x20` (switch on the sub itself); readers FUN_142f30050 / FUN_142f307f0: u16 +8, u32 sub +0x10, u32 recipe +0x18 [P] | — | OK | hide inserts into RM+0x20 (FUN_142b45c50) when id > 0 |
| SendSelf RecipeData HiddenRecipeIds | Model/SendSelfToClient.cs RecipeData.Write (after the Unknown1 cooldown list, before the records) | FUN_142c1b810: FUN_142c34a10 → +0x8 (known), FUN_142c35bd0 → +0x38, **FUN_142c34a10 → +0x20 (hidden set: u32 n × i32, cleared first)**, records [P] | — | OK | |
| Recipe record AllowCatalyst / TrackerAchievementId / Unknown17 (MaxQueue) | SendSelfToClient Recipe.Write; filled in CraftingStations.BuildRecipeRecords | FUN_142c2e640: 10 × u32, components FUN_142c3f2e0, **bool +0x34**, u32 +0x38, u32 +0x3c, **u32 +0x40**, u32 +0x44, list FUN_142c34c30 +0x50, **u32 +0x48** [P] | — | OK | RecipeEntry columns follow Recipes.txt (ALLOW_CATALYST, TRACKER_ACHIEVEMENT_ID, MAX_QUEUE) [P data] |
| PU 0x4B cPlayerInGamePurchaseResult S→C | Game/StoreBundles.HandlePurchase (opcode PlayerUpdateInGamePurchaseResultWire, 12-byte body) | dispatcher case 0x4a → FUN_142c4b300 → FUN_142c25490: u16, u32 sub, u32 +0x18, +0x1c, +0x20 [P] | yes (unless param_4) | OK | body exactly 3 × u32; field meanings [I] (the client drops them) |
| PacketClientInGamePurchase 0xD0 C→S | Model/InteractionMessages.ClientInGamePurchase (3 × u32) | Lua native FUN_1435e9400 (opcode 0xd0, +0x10 = Lua arg 1, +0x14 = Lua arg 3, +0x18 = 0) → writer FUN_1435ebe10: u16, u32, u32, u32 [P] | — | OK | |
| InGamePurchase 0x45 sub 0x0D → 0x0E ServerStatus | Game/Commerce (in-process, Send: u16 sub + bool true); CommerceServer/ClientPackets.ServerStatus | FUN_143256750: u16, i16 sub (0xe), one byte, nothing after [P]; packet_ids_695: 0x45/0xD Request, 0x45/0xE Response | yes | OK | |
| InGamePurchase 0x45 sub 5 type 1 StoreBundles (03c0ef1 / 1c91d45) | Game/StoreBundles.Build / WriteBundle | FUN_143236e60 → FUN_14322ed40 (u16, i16, u32 type, u32 store) + FUN_14322c6f0 (u32 ×3, str, str, FUN_1432330d0 list); bundle FUN_14322cae0 → FUN_14322fa80 → FUN_14322d330 (items FUN_1432337c0 → FUN_14322d0a0: u32, u32, u64, u32, str, str; FUN_143233f30 {u32,u32}) [P] | yes (param_4) | OK | field for field as the offsets in WriteBundle's comments |
| ClientUpdate 0x29/0xC TeleportToLocation S→C (+0x48 / +0x50 filled) | Game/Teleport.To | FUN_142cd9980 case 0xb → FUN_142c1d530: u16, i16, vec4, vec4, u8 +0x40, bool +0x41, bool +0x42, u64 +0x48, u32 +0x50; applied FUN_142d59f30 only if `ea0 - ea8 < 1` [P] | **yes** | layout OK; **value MISMATCH (E1, fixed)** | FUN_142d59f30: guid +0x48 ≠ DAT_145430c58 and u32 > 0 → FUN_143325060(actor, ms, &guid): guid → now + u32 (FUN_142b4f660, timeGetTime clock = ms) [P]. The key the teleporter checks is the pad's **prop guid**: ClientAreaTeleporter vfunc 0xd0 = FUN_1431b6070 stores packet +0x18 (spawn guid) in its data +0x10 (FUN_142d54040 type 0x17); enter FUN_1436db8b0 looks the entity up by data +0x10 and asks FUN_1433286b0 with that entity's guid; the "*Bamf!*" callback FUN_1436db150 copies data +0x10 to +0x28, which state 1 of FUN_1436db650 asks with [P]. Server sent `to.Spawn.AreaGuid` (MoverAreaTag \| guid) → never matched → the arrival pad was not ignored |
| Command 0x1E sub 0x51 TeleportToLocation C→S | Game/TeleportRequests.Handle (body: vec4 at 0) | FUN_1433c80a0 → FUN_14338db30: tunnel byte, u16 0x1E, u16 0x51 (FUN_142c12ce0), vec4 +0x20 [P] | — | OK | |
| Command 0x1E sub 0x52 TeleportToLocationEx C→S | TeleportRequests.Handle (quaternion at body 16) | FUN_14338dd40: header, vec4 +0x20 position, vec4 +0x30 = (axis·sin(h/2), cos(h/2)) about DAT_1455f07a0, vec4 +0x40 = DAT_1455f0740 [P] | — | OK | quaternion convention vs. the server's Facing [I] |
| Command 0x1E sub 0x3F6 TeleportManagedToLocation C→S | TeleportRequests.Handle (u64 mover at body 16) | FUN_14338d910: header, vec4 +0x20, FUN_142c02460 u64 +0x30 (8 bytes) [P] | — | OK | |
| Combat 0x24 subs 2 / 3 / 4 C→S | Network/Handlers/CombatHandler.cs (CombatRequest : SubRequest16) | header FUN_1433c86c0: u16 0x24, u16 sub; AutoAttackTarget FUN_1433ca3e0 (sub 2, u64) [P] | — | OK | no reply needed |
| Groups 0x2B sub 3 AutoGroup reply | Game/Groups.HandleLookingForMore | dispatcher FUN_142deaa10 case 3 → FUN_142deb5f0: u16, i8 sub, u32 mode, u32 code, u32, u32 flags (bits 2/3 → guid lists) [P] | no | OK | mode 3 → FUN_143c1c810 text; code 4 would set +0x1f9 (never sent) |
| Groups 0x2B sub 7 SetGroupFlags reply / request | HandleLookingForMore (request flags = body[8]) | reply FUN_142ded840: u16, i8 sub, u32 mode, u32 code, u8 flags [P]; request writer FUN_142ddbfd0: header FUN_142c12f00 (u16, u8 sub, u32 mode) + u32 +0x18 + u8 +0x1c [P] | no | OK | |
| Groups 0x2B sub 9 SetGroupDescription reply | HandleLookingForMore | case 9 → FUN_142ded700 → FUN_142de4320: u16, i8 sub, u32 mode, u32 code, string (u32 len + bytes) [P] | no | OK | |
| Groups 0x2B sub 0xD FindLookingForMore | (no reply) | dispatcher has no case 0xd (default → 0) [P] | — | OK | |
| Groups 0x2B sub 0xE GetGroupInfo / 0x10 JoinLookingForMore replies | HandleLookingForMore (request group = body[8..12]) | FUN_142deba20 / FUN_142dec540: u16, i8 sub, u32, u32 code [P]; request writer FUN_142ddba90: header + u32 +0x18 + u32 +0x1c [P] | no | OK | |
| HeroicMovement 0x105 sub 3 SwimmingUpdate re-send | Game/HeroicMovement.IntroduceSwimmer (`05 01`, u8 3, u64 guid, stored client state) | FUN_142cdc570 sub 3 → FUN_142c1ffa0: header FUN_142c1fd70, u8 flags +0x20, u32 +0x24, [flags & 0x40: u32 +0x28, u32 +0x2c] [P] | stream flag | OK | the body is the swimmer's own; the dive bit 0x80 doesn't change the length. Its effect on a receiver not in state 1 [I] |
| Equipment 0xCB/2 SetCharacterEquipmentSlot for creatures | Game/CreatureWeapons.Message → Model/EquipmentSlotMessages.cs | FUN_142e42da0 → FUN_142e41b40 (as audit-wire-a: u16, u8 sub, u32 profile, u64 guid, u32 slot, u64 item, str, str, attachment FUN_142c185c0) [P] | — | OK | model strings are StringFixed<64> (Wield_* names ≤ 39 chars) |
| PU 0x62 AddLightweightProp: literal Name " " (mover parts) | Game/Props.Movers (Spawn.Name = " ") → Model/PlayerUpdateAddLightweightProp.cs | handler FUN_142cde1c0: key string +0x28 (len +0x38) empty → literal +0x40 (len +0x50) ≥ 1 → copied as is (FUN_142b2a9c0 / FUN_142b23aa0: no trim) [P]; FUN_142d54040 names the entity "area" (FUN_142ec18c0) only when entity name length +0x1318 == 0 and +0x268 ≠ invalid [P] | — | OK (wire) | " " has length 1 → no "area" label. What a one-space name plate looks like in play [I runtime] |
| PU 0x62 AddLightweightProp: OwnerGuid 0 (platform parts) | Game/Props.Platforms (Spawn.OwnerGuid = 0) | FUN_142cde1c0: packet +0x2a0 compared with the local player's guid → FUN_142c98c00 (locally managed set); no other read of +0x2a0 in FUN_142cde1c0 / FUN_142d54040 [P] | — | OK | the server's PlacedProp.OwnerGuid is untouched |
| MountBase 0xA1 C→S | Model/InteractionMessages.MountRequest (SubRequest16) | DismountRequest `03 00` seen in capture [P capture] | — | OK | |

## 2. FIX list

**E1 (fixed, commit on local/wire2): Translocation Pad arrival ignore key.**
`LandmarkEmulator.WorldServer/Game/Props.Movers.cs` ManagedTeleport: `ignoreTeleporter: to.Spawn.AreaGuid` →
`ignoreTeleporter: to.Spawn.Guid`. Proof above (ClientAreaTeleporter data +0x10 = packet +0x18, FUN_1431b6070; both
FUN_1433286b0 checks use it). Player impact: with the area guid the client never ignored the arrival pad, so a player
landing on it could be sent straight back (the 71e4245 intent did nothing). Teleport.cs docs corrected (prop guid,
ms on timeGetTime). trace/0695/agent-short-teleport.md §2 / §3 say "area guid (spawn +0x268)" — that note is wrong;
the key is the spawn guid (+0x18).

No other mismatch.

## 3. Not verifiable from the decompile (runtime checks for the coordinator)

- E1 in play: hop between two Translocation Pads and stand still on the arrival pad: no hop back for 2.5 s.
- Mover parts' " " name: no "area" label and no visible empty plate.
- Movement effect values (rows / curves / glide factor) and the integer-kind character stats 11 / 13 — layout [P],
  values [I].
- 0x4B result field meanings; 0xD0 argument meanings (no shipped script calls the native) [I].
- 0x52 heading quaternion convention against the server's Facing [I].
