# Phase E3, wire-b: fresh wire audit of the packets changed since the 2026-10-17 audit (2026-10-26)

Brief: [agent-phaseE3-brief.md](agent-phaseE3-brief.md). Read-only. Base = `git rev-list -1 --before=2026-10-17T18:00
client-0.695` = **71bafff**, HEAD **8219f7c** (443 commits, 527 .cs files). Method: for every packet the slice's files
write or read that is new or changed since 71bafff, the server body was compared field by field with the 0.695 client
reader (S→C) or writer (C→S) in `re/decomp_all_695` (`pkt.py`, `fn.py`, raw reads where pkt.py drops a call). Packets
whose only change was dropping a 0.589 branch (`Speaks695` / `v695`) keep the 0.695 layout the 10-17 audit already
proved; those are listed as "unchanged on 0.695" and were not re-read field by field. Opcode framing used throughout
[P]: `WorldSession.GetOpcodeBytes` (enum bytes; PlayerUpdate 0x26 as u16 + u32 sub), checked for every enum value used.
[P] = proven from the client function named; [I] = inferred.

## 1. Findings (ranked by player impact)

### F1. Guild ChangeMemberRank 0x83/0x0A S→C is dropped by every client — MISMATCH [P]

- **Client:** Guild dispatcher FUN_142ef2780 `case 10` builds GuildChangeMemberRankPacket, reads it with FUN_142ee4550
  and calls the handler FUN_142ef15e0 only when the error flag is clear **and** the body is consumed exactly
  (`if ((char)local_40c8 == '\0') if ((int)local_40d0 - (int)local_40d8 < 1)`). FUN_142ee4550 reads: u64 guild (+0x18),
  u64 member (+0x20), string (+0x28), u64 changer (+0x40), string (+0x48), u32 rank (+0x60), **string (+0x68), bool
  (+0x80), bool (+0x81), u32 (+0x84)** (catalog_695.md "GuildChangeMemberRankPacket", read by hand).
- **Server:** both writers stop after the u32 rank (6 fields):
  `LandmarkEmulator.WorldServer/Game/Guilds.cs:1064-1072` (ChangeRank) and `:1116-1124` (TransferLeadership, new since
  10-17), `LandmarkEmulator.SocialServer/GuildService.cs:766-774` and `:794-802`. The string at +0x68 has no length
  bytes → FUN_142d4b5d0 sets the error flag → the handler never runs. GuildService.cs:725's own comment lists the full
  10-field layout.
- **Effect:** FUN_142ef15e0 is what moves the member to the new rank in every online member's roster (member +0x48 =
  packet +0x60), refreshes the roster (FUN_142efae30) and shows the promote / demote line (placeholder notification
  0xb-0xe, chosen by +0x81 and by whether the changer is known). Today a plain promote / demote changes nobody's window
  until the next full GuildData (relog); the leadership transfer is masked because TransferLeadership also calls
  ResendGuild. Not in audit-wire-c.md's Guild table (it was missed on 10-17, not fixed since); not in OPEN-LEADS
  (docs/request-variants.md:117 covers only the C→S side).
- **Fix:** after `w.Write(rank)` in all four writers add `w.Write(""); w.Write(false); w.Write(false); w.Write(0u);`
  — the client's own C→S copy (FUN_142ef91f0) fills those as empty string, u16 0 at +0x80, and DAT_14544268c at +0x84.
  [I] +0x81 picks the message variant (`iVar7 = 0xe - (+0x81 != 0)` / `0xc - …`); pass false until the texts are read.
- **In-game check:** yes — two members online, promote one, both rosters should change at once and the chat line show.

### F2. re/out/coverage_695.md still lists packets the server now sends as "never sent" — STALE [P]

UpdateGuildTag 0xDF (coverage_695.md:"PlayerUpdatePacketGuildTag | 0xDF | never sent", sent by Social/GuildTags.cs:78),
Guild DataFull 0x12 / Invitations 0x14 / RemoveLog 0x1B (Guilds.cs:1349-1380, :1535), InstancedGame PlayerRemoval 0x14 /
TeamMembers 0x15 / PropState 0x12 / PropInteraction 0x13 (lines 839-843), CommerceConfiguration 0x2E, ServerStatusResponse
0x0E, PendingKickFromServer 0x65 (also listed under base "0x1"; the reader FUN_142c4a0d0 reads u16 0x65, string, u32),
PlayerFilterUpdate 0xDD/4 (handled, C→S only). phaseE2-families.md §160 already notes some stale "sub NOT handled"
rows; this adds the S→C side. Fix: regenerate coverage_695.md (catalog2 / coverage tool) from HEAD. No in-game check.

### F3. Small stale comments in changed code — STALE (no wire effect) [P]

- `Game/Claims.cs:1325` says music is "+0x278"; the 0.695 ClaimInfoUpdate handler (part_0069.c:14123, case 0x16)
  takes `local_e070` = reader +0x280, the u32 after the 4-field time block (+0x270..+0x27c) — which is exactly where
  the server writes it. The offset in the comment (and :1288) is the 0.589 one. Fix: "+0x280".

No other MISMATCH or GAP was found in the slice.

## 2. Checked and fine

Abilities 0x27 (u16 + u16 sub, then the "backtimed" i16 sub + u8 + u32 header FUN_142e03920):
- CasterInfoReply 0x03 ExecutedEffects (AbilityMessages.cs:127) — element FUN_142e03a00 = 14 × u32 in order; Effect.Write
  (MovementEffects.cs:162) writes 14 × 4 bytes. OK [P].
- CasterInfoReply ExecutedTargeters (AbilityMessages.cs:158) — element FUN_142e03c60: +0,+4,+8,+0xc,**+0x14,+0x10**,+0x18..+0x28,
  list FUN_142e047c0; ClientExecutedTargeter.Write writes Delay before MaxTargets and a 0 shape count. OK [P].
- CastContinue 0x08 (AbilityMessages.cs:422) — reader FUN_142e011b0: u64,u64,u32(+0x30),u32 ×8 (+0x38..+0x50), bool. OK [P].
- AbilityEnd 0x05 (Abilities.cs:456) — FUN_142e00d20: u64 cast, u32 hash, u32 ability, u64 caster. OK [P].
- PlayAnim 0x12 (Emotes.cs:888) — FUN_142e030c0: u64, u32, 4 × u32 loop. OK [P].
- EffectStart 0x0C (Buffs.cs:186 CharacterEffectStart, used by BlockStance/DetectCave) — FUN_142e028b0, both 4-value loops
  (+0x60, +0x90) fixed 4, three FUN_142c02830 blocks, tail +0xd4..+0xe8. OK [P].
- EffectEnd 0x0D (Buffs.cs:248) — FUN_142e02440. OK [P]. Buffs.WriteHeader uses `Write(ushort)` = 2 bytes
  (GamePacketWriter.cs:60). OK [P].
- C→S ClientTargeterFindTarget 0x24 (writer FUN_14354c760, filled in FUN_14354e800: +0x18 target, +0x20 cast guid, +0x28
  hash, +0x30 targeter, 4 × u32) and ClientTargeterEnd 0x23 (FUN_14354c520 / FUN_14354d570) vs AbilityMessages.cs:240/:262.
  OK [P]. CancelCast 0x0F raw body, trailing bool. OK. CasterInfoRequest u32 ability. OK.

PlayerUpdate 0x26 (u16 + u32 sub; dispatcher FUN_142cdfa60 `switch(sub-1)`, all subs checked against the id table):
- 0x09 UpdateTemporaryAppearance (TemporaryAppearance.cs:36) — FUN_142c2ce10: u32 model, u64 character, exact. OK [P].
- 0x0A RemoveTemporaryAppearance {u64} — case 9 FUN_142c4ce40. OK [P].
- 0x1B CustomizationChange (Barbershop.cs:142) — FUN_142c27ad0: u64, bool, model info FUN_142c18710 (= CharacterModelInfo.Write),
  list of FUN_142c185c0 records = 4 strings (the 4th elided by pkt.py, read raw) + 4 × u32 + bool = WriteWorn. OK [P].
- 0x4B InGamePurchaseResult (StoreBundles.cs:273) — FUN_142c25490: 3 × u32. OK [P].
- 0x4D ManagedObject (Props.Platforms.cs:165) — case 0x4c → FUN_142cdd450 → FUN_142c29910: u64, u64, u64 (24-byte body). OK [P].
- 0x51 UpdateCharacterStateDelta (BlockStance.cs:271 DeltaBody; DeadBodies.cs, SafeEject.cs:167) — FUN_142c2c7a0: u64 guid,
  u64 +0x20 (originator, compared with the local guid in FUN_142ecdd20), u64 +0x28, u64 +0x30, u32 time, exact. OK [P].
- 0x5B LightweightToFullPc (PlayerUpdateLightweightToFullPc.cs) — blob FUN_142c21350: guild tag = 3rd string (+0xf8),
  temporary appearance u32 +0x114, title FUN_142c25e20 +0x118, 3 bools at +0x174 (AFK first); nested UpdateStat
  FUN_142c2cd60 (u16, u32 sub, u64, list of FUN_142c18c40 = u32 id, i8 type, u32, u32 = CharacterStats.WriteList). OK [P].
- 0x58 AddLightweightPc — FUN_142c26e50: u8 +0xd8 faction, u32 model, u32 +0xe0 profile, …, 3 bools +0x114, 5 bools,
  u8 +0x12d, 3 × u64 (+0x130 loop), u32, u32, u64, u8. Renames + values only. OK [P].
- 0x62 AddLightweightProp — values only (ControlMode byte, collidable bit, enabled bool at +0x490 = FUN_142c273d0's last
  read). Widths unchanged since the 10-17 audit. OK [P for +0x490; layout from 10-17].
- 0x65 RequestAnimationSelection (Emotes.cs:478 SelectionBody) — case 100 FUN_142c4d050 → FUN_142c2ae00: u64, u32, bool, bool,
  exact. OK [P].
- 0x73 SetEnabled (Props.cs:1494) — handled in the receive pre-pass FUN_142d54c10 (not the main switch), reader FUN_142c2b660:
  u64, bool, exact. OK [P].
- 0x79 DisplayPropText (Props.cs:217) — FUN_142c27f40: u64 question, u64 prop, string, u32, u32, bool. OK [P].
- 0x7C UpdatePropName (Props.cs:2125) — FUN_142c4de60: u64, string. OK [P].

Other S→C:
- ClientUpdate UpdateStat 0x29/6 (ClientUpdateUpdateStat.cs) — FUN_142c492a0: u16, i16, u32 n × stat. OK [P].
- ClientUpdate TeleportToLocation 0x29/0xC (Teleport.cs:121) — FUN_142c1d530: 2 × 4 floats, u8, bool, bool, u64, u32. OK [P].
- ClientUpdate ZonePopulation 0x29/0x1F — FUN_142c49410: exactly 4 × u32. OK [P]. DoneSendingPreloadCharacters bool. OK.
- UpdateGuildTag 0xDF (Social/GuildTags.cs:56) — FUN_142c4c0a0 (handler FUN_142cdf330): u16, u64, string. OK [P].
- BaseBroadcast 0x4F (Broadcasts.cs:55) — FUN_142c17410: i16 kind, name block FUN_142c25b90 (3 × u32, 2 strings), string,
  u32. OK [P].
- Chat FromStringId 0x13/4 (Emotes.cs:737) — FUN_143356860: u16 4, u64, u32, 3 bools, u32, u64 +0x38, u64 +0x30, u32. OK [P].
- Template 0xF0/0x13 EnvironmentInstanceDetailsResponse (TemplateInstances.cs:213, new today) — FUN_14310bbf0
  `switch(sub-2)` case 0x11: u8 sub, u64, instance FUN_1430ff920 (3 × u64, region FUN_1430ff0c0 = u64 + 3 × u32 + 3 × u32,
  u32, u64, string, bool), TemplateBasicData FUN_1430ff530 (all 23 fields vs Templates.WriteBasicData:612), cells
  FUN_143101860, i32 PropData count. OK [P]. C→S 0x12 GetInstanceDetailsRequest (FUN_1431138e0: u64 instance +0x18,
  u64 +0x20) read at Templates.cs:872. OK [P].
- Claims 0xF9: 0x11 RecievePropExclusionArea now u32 + two list counts (fixes 10-17 audit-wire-b MISMATCH) OK [P];
  0x05 ClaimInfoUpdate (Claims.cs:1295) vs FUN_142da2840 incl. the new guid/value at +0x38/+0x40 and kind 0x16 → +0x280
  OK [P]; WriteRecord three masks OK; C→S 0x2D now reads all three masks (closes the 10-17 semantic gap) OK [P].
- InstancedGame 0x10D (u32 sub): PlayerRemoval 0x14 (InstancedGames.cs RemovalBody; FUN_1434dbfb0: u64, exact),
  ObserverRegistration 0x16 (FUN_1434d7400: u32, u64, u32), Event 7 tail (FUN_1434d6eb0), sub numbers 0x12/0x13/0x14/0x15/0x16
  and the editor's 0xD/0xE/0xF/0x10/0x11/0x18/0x19/0x1A vs coverage_695 and dispatcher FUN_1434ee920 (`case sub-2`). OK [P].
  Editor / configuration bodies: 0.589 branches removed only — unchanged on 0.695.
- InGamePurchase 0x45 (u16 sub): StoreBundles 5/type 1 (StoreBundles.cs:285) vs FUN_14322ed40, FUN_14322c6f0, FUN_1432330d0,
  FUN_14322cae0 → FUN_14322fa80 → FUN_14322d330, items FUN_1432337c0 → FUN_14322d0a0, FUN_143233f30 — every field. OK [P].
  CommerceConfiguration 0x2E (FUN_14322db60: 3 × u32), ServerStatusResponse 0x0E (FUN_143256750: bool, exact),
  SubmitProductReviewResponse 0x44 (FUN_1432534f0: bool, exact). OK [P]. WalletInfo 0x0C value only. Sub numbers in
  Commerce.cs:73-106 / CommerceServer/ClientPackets.cs:19-26 vs dispatcher FUN_143258f50 and coverage_695. OK.
- C→S ClientInGamePurchase 0xD0 (InteractionMessages.cs) — writer FUN_1435e9400 → FUN_1435ebe10: u16, 3 × u32. OK [P].
- CharacterCustomization 0x11C/4 EquippedAttachmentReply (Barbershop.cs:124) — FUN_1430a0cc0: u16, i8 sub, u32 n ×
  FUN_142c185c0. OK [P].
- ArtData tint definitions (ArtTints.cs; JSON renames, values now f32) — alias semantic element FUN_142c2ff10: string,
  3 × u32, string, u32, 4-value loop; all 12,727 Values arrays in Resources/ArtTints.json have 4 entries. OK [P].
- Groups 0x2B (u8 sub): AutoGroup 3 (FUN_142deb5f0: 4 × u32 + optional guid lists by flag), SetGroupFlags 7 (FUN_142ded840:
  u32, u32, u8), SetGroupDescription 9 (FUN_142de4320), GetGroupInfo 0xE / JoinLookingForMore 0x10 (2 × u32). OK [P].
- Guild 0x83: DataFull 0x12 (FUN_142ee4820), Invitations 0x14 (FUN_142ee5040; element FUN_142ee4ee0, names FUN_142ee7aa0 =
  u64 + string), UpdateInvitation element now with time + type 1, RemoveLog 0x1B (FUN_142ee5580: list u32, u64), core
  alias = name — world and social-service writers alike. OK [P]. C→S Invite 4 / Kick 0xC via Internal/Social
  GuildInvites / GuildKicks (u64, u64, u32-length string). OK [P].
- AchievementBase ObjectiveActivated (sub 6) and the NPC-quest reward bundle (Achievements.NpcQuests.cs:129) — same field order as
  Achievements.Tutorial.cs TutorialRewards (all u32 / byte via BinaryWriter, no 7-bit strings). OK [P vs the existing
  writer; I that the cloned template tail offsets fit, same as the tutorial goals].
- Kicks: PendingKickFromServer 0x65 (FUN_142c4a0d0: u16, string, u32) and KickedFromServer 0x53. OK [P].
- PreferencePacket 0x03 S→C (Preferences.cs:94; FUN_142c4e700: u32 kind, string) and C→S raw. OK [P].
- HeroicMovement 0x105 relay (HeroicMovement.cs:399): u16 0x105, u8 sub, u64 subject, body — frame OK; body is the
  client's own. TargetClientNotOnline 0x1A (off by default). OK.
- EquipmentSetCharacterEquipmentSlot for creatures (CreatureWeapons.cs:390/406): model unchanged since 10-17. OK.
- AdminMonitor procedural body: values only. PropSettings / AreaDefinition: renames and 0.589 branches only. OK.

Other C→S:
- PropBase 0x19 SetPropName now {string, u64} (PropHandler.cs:84) = writer FUN_1435fca20 (string +0x18, u64 +0x30). OK [P].
- PropBase 0x1D DialogResponse {u64, bool} = FUN_1435fbf80. OK [P].
- Inventory 0x2D/0x0F tooltip (TooltipRequest.cs) = writer FUN_14350b970: u32, u64, u64 (FUN_142c128f0 writes 8), u32,
  string, u32. OK [P].
- ClientPcData 0xDD/4 PlayerFilterUpdate = FUN_142fc3d20: u32, string, string, u32. OK [P].
- TerrainCellClientCellRequest: u64 cell + 4 × u32 (same 24 bytes as before). OK.
- 0x26/0x4C SafeEject (1 byte), VoiceBase custom-channel request, Mount / MarketplaceImage / Combat SubRequest16 models: readers only
  take what they use. OK.

## 3. Not reached (left for the slice that owns them, or a later pass)

High-traffic models changed since 71bafff that are **not** in this slice's file list (assumed wire-a): SendSelfToClient
(3,148-line diff), ContinentDefinition (now inside ClientBeginZoning and SendZoneDetails, sent on every re-zone), Shared/
Achievement, InventoryItem, RewardBundle, Objective, Mount, ClientGameSettings, SendZoneDetails, AuthServer ArtData /
CharacterSelectInfoReply / LoginReply / ClaimData / TunnelPacket. ClientBeginZoning's own fields were checked (same
widths, renamed); its ContinentDefinition block was not. Also not read field by field: the AddLightweightProp lightweight
base part on 0.695 (pkt.py elides it; values-only change), the Voxel Vision VoxelTemplateLookup terrain layer
(EnvServer.Core/Terrain/VoxelTemplate.cs — terrain cell data, not a GamePacketWriter packet).

## 4. New leads

- **NL-E3b-1 Guild rank-change message variant.** Need: what +0x81 (and +0x80, +0x84, string +0x68) of
  GuildChangeMemberRankPacket mean, so the promote / demote line reads right after F1's fix. Searched: FUN_142ef15e0
  (+0x81 selects placeholder 0xd/0xe or 0xb/0xc), FUN_142ef91f0 (client sends 0, 0, DAT_14544268c). Try next: read
  PTR_s_Placeholder_14520f1e0 entries 0xb-0xe and the CodeStringMappings they name; DAT_14544268c's initializer.

## Summary

Of the ~60 packets in the slice that were new or changed since the 10-17 audit, all but one match the 0.695 client: the
new ability channel / targeter / effect packets, the observer packets (temporary appearance, guild tag, dead bodies,
block stance, animation selection, full-PC title / AFK / tag), the store bundles, Voxel Vision's instance details, the
claim, instanced-game, group, guild-invitation and commerce changes, and the reworked C→S readers. The one MISMATCH is
older than this window but was missed on 10-17: Guild ChangeMemberRank 0x83/0x0A is written 10 bytes short in both the
world and the social-service code, so every client drops it and rank changes never reach the other members' rosters
until a relog. Coverage_695.md is stale for a dozen packets that are now sent.
