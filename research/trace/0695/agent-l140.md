# L140: client→server classes parked by the E.3 triage (agent report, 2026-10-18)

Scope: the 58 rows in `re/tools/verdicts.tsv` with verdict `parked` that cite **OPEN-LEADS L140**. These are all the
E.3 "parked" rows except those parked on other leads or decisions. The rows that are out of scope are listed in §5.

Method (read-only):
- **Callers:** a caller index of the whole 0.695 decompile, saved in the scratchpad as `callers695.pkl` and `vtusers695.pkl`
  (every function that names a packet class's `::vftable`). Callers were walked up to Lua natives (`re/out/luabind_695.tsv`)
  or to roots.
- **Roots:** each root was checked against the retail 0.695 exe with `re/tools/refscan.py <0.695 test copy>/Landmark64.exe`.
  Pointer hits were mapped to their vtable and slot through the RTTI complete-object locator and `re/out/rtti_695.tsv`.
- **Shipped UI:** checked `<0.695 test copy>\UI\Default\*.lua`, `Data.xml`, `InputProfile_Default.xml` and `ScriptsBase.bin`,
  plus `re/out/ui_server_map_695.md` and `re/out/agent_ui/uicalls.txt`.
- **Design sources:** a keyword sweep of `design/` (wiki, forum, patch notes, game-rules).
- **Server:** grep of `LandmarkEmulator.WorldServer`, plus `server-data/worlds/dev/opcode_stats.tsv`.

Tags: **[P]** = proven (function id or file quote), **[I]** = inferred.

## 1. Headline findings

1. **"Evac to Safety" in the Escape menu is broken on our server, and it is a live feature [P].**
   - `UI/Default/Data.xml:1081`: `MainMenu` → `Ui.EvacToSafety` → `LuaCallback="Voxel.EvacLocalPlayerToSafepoint"`.
     The native is FUN_1435fdb70.
   - Only one of its three branches sends PacketZoneSafeTeleportRequest 0x7C (FUN_142d0a230): the one for a player
     **outside** the zone's bounds. We handle that branch (Health.EvacuateToSafePoint).
   - The other two branches are the common case:
     - a player standing on a claim (FUN_142ccb810 → FUN_142dbbf40 → claim object +0x3a8);
     - otherwise, the player's own position snapped onto the ground (FUN_142d0a1f0 → FUN_142d0a0d0, with a ground-height
       clamp).
     Both call **FUN_1433c80a0**, which sends **CommandPacketTeleportToLocation 0x1E/0x51**, **…Ex 0x1E/0x52** (heading
     changed) or **TeleportManagedToLocation 0x1E/0x3F6** (player on a mover, actor +0x1459 & 0x10).
   - Before sending, FUN_1433c80a0 sets the **teleport-pending bit** (actor +0x1457 |= 8).
   - Two functions stop working while that bit is set: the actor update (FUN_142eb1950 skips FUN_142eb6a10 /
     FUN_142ecb870) and FUN_143329ed0, which returns at once. Each tests `+0x1457 & 8`.
   - Only **FUN_142d59f30** clears the bit (`& 0xf7`). It is the handler of the **S→C ClientUpdate TeleportToLocation**
     (reader FUN_142c1d530 → FUN_142d59f30 in FUN_142d1ccf0's ClientUpdate case). That is the packet our
     Teleport.To already sends.
   - Our server ignores Command subs 0x51 / 0x52 / 0x3F6 (InteractionHandler.HandleCommand has no case). **So a player who
     clicks Evac to Safety inside the zone is never answered, and the movement update stays switched off** [P chain; not yet
     seen in game].
   - The same three subs are also sent by GM commands, which are gated by the admin-player flag +0x7b0d8 (which our server
     sets for Admins) or the admin client:
     - `/location x y z` (FUN_1433af380);
     - `/teleporttocamera` (FUN_1433b5430);
     - the world map's Admin.MapTeleport (FUN_143054920).

     Note: admin-monitor.md calls teleport-to-camera "client-side". That is wrong: it sends 0x1E/0x51.
2. **AutoAttackOff 0x24/3 is sent in normal play [P chain, I for one slot name].**
   - FUN_142d090a0 sends it unconditionally. It is called from:
     - ProxiedCharacter vtable slot 143 (FUN_142e9a8f0, reached from the actor's death handling FUN_142eb6a10 /
       FUN_142e978c0), when the dying actor is the player's current target;
     - every interaction-list auto-select (FUN_1433d3f90, after CommandPacketInteractionSelect).
   - Our server has no handler for family 0x24 at all. No reply is needed (the client clears its own state first), but the
     packet must be accepted silently.
3. **PacketClientIsReady is already handled.** It is ZoneMessageOpcode.ClientIsReady 0x10 (MiscHandler.cs:233). Its
   verdict row is stale. **PacketTunnelPacketFromExternalConnection** is the Gateway envelope, handled in
   GatewaySession.cs:70. **BasePacket** is a base class, not a message.
4. **Most of the rest is proven dead on the retail exe.** refscan finds no call, jump, LEA or pointer to the sender:
   - claim snapshots;
   - repeating activities;
   - social diplomacy / housing;
   - match / mini-game invites;
   - leaderboard;
   - terrain-cell reset;
   - item-line timer;
   - CancelInteractEvent (so InteractCancel is never sent);
   - the Flourish stub at 0x142ec9c80;
   - GetAllAvailableRequisitionItems.

   The others can only be reached after a server message Landmark never sent (lobby, requisition replies, targeters,
   charge, client objectives, area timers, attachments), or only through a typed `/` command with no live meaning
   (`/attack`, `/hit`, `/queryprops`, `/party` LFM sub-commands).

## 2. Table (one row per parked class)

Verdicts:
- **LIVE-USED** → build.
- **REACHABLE** (reachable in retail but unused on live) → answer minimally.
- **UNREACHABLE** → close.

| Class | opcode (0.695) | Who sends it in 0.695 (path) | Live evidence | Verdict | What the server should answer |
|---|---|---|---|---|---|
| CommandPacketTeleportToLocation | 0x1E/0x51 {vec4 pos} (writer FUN_14338db30: u16 0x1E, u16 0x51, 4×f32 from +0x20) | FUN_1433c80a0 ← FUN_142d0a0d0 ← (FUN_142d0a1f0 / FUN_1435fdb70 **Voxel.EvacLocalPlayerToSafepoint** ← Data.xml MainMenu "Ui.EvacToSafety"); also /location (FUN_1433af380, ptr 0x14475f3a0), /teleporttocamera (FUN_1433b5430, 0x14475f3d0), world-map MapTeleport (FUN_143054920, ControllerWorldMap/ControllerLocalTravel slot 72); the last three need the admin flag +0x7b0d8 or admin client +0xB0 [P] | Escape-menu entry shipped in 0.695 Data.xml [P]; the forum's "stuck" threads (134 files hit "stuck/evac", not read one by one) [I] | **LIVE-USED → build** | ClientUpdate TeleportToLocation (Teleport.To) to the accepted point. This clears pending bit 8 (FUN_142d59f30) [P]. **Always answer**, even when refusing: send the player's current server position |
| CommandPacketTeleportToLocationEx | 0x1E/0x52 {vec4 pos, vec4 rot, vec4 (+0x40, DAT_1455f0740 default)} (writer FUN_14338dd40) | same as 0x51 when the requested heading differs from the current one (claim branch / /location with heading) [P] | as above | **LIVE-USED → build** | as 0x51, with the heading taken from the quaternion |
| CommandPacketTeleportManagedToLocation | 0x1E/0x3F6 {vec4 pos, u64 mover guid (FUN_142c02460)} (writer FUN_14338d910) | FUN_1433c80a0 when the actor is on a managed object (+0x1459 & 0x10) [P] | movers are live (movers-695.md) | **LIVE-USED → build** | as 0x51 (the client froze itself the same way) |
| CombatPacketAutoAttackOff | 0x24/3 {} (FUN_1433ca460) | FUN_142d090a0 ← ProxiedCharacter/LocalProxiedCharacter slot 143 FUN_142e9a8f0 (death path, current target) and FUN_1433d3f90 (InteractionSelect auto-pick ← FUN_1433d3b70 ← Command S→C case 0xB FUN_1433be5d0); also FUN_142d17b70 (ActorEventHandler / TileEventHandler slot 4) and /attack with no name [P] | creature combat is live (Mar 2016 "combat is coming" P, 2016) | **REACHABLE in normal play → accept** | nothing; accept silently (no Unanswered count, debug log only) |
| CombatPacketAutoAttackTarget | 0x24/2 {u64 target} (FUN_1433ca3e0) | /attack name (CommandAttack FUN_1433a1a90 → FUN_1433a3950); BaseClient::MeleeAttack FUN_142d08e00 (loop state +0x32802 is only ever set inside itself) [P] | no Landmark source for /attack; combat was ability-based [I] | **REACHABLE (typed) → minimal** | nothing; log at debug |
| CombatPacketSingleAttackTarget | 0x24/4 {u64 target} (FUN_1433cbad0) | /hit name (CommandHit FUN_1433a2010 → FUN_1433ac600); FUN_142d08e00 [P] | none [I] | **REACHABLE (typed) → minimal** | nothing; log at debug |
| GroupPacketAutoGroup | 0x2B sub 3 | /party autoparty (FUN_142dd8450 ← FUN_142dda5f0 ← command table 0x1446c3c48, agent-leads2-d2); Lua Groups.GroupAutoGroup / Ui.OnFirstTimeIntroFinished (no caller in the 0.695 UI); input action "Generic AutoJoinSquad" (FUN_142d1a5b0), **not in InputProfile_Default.xml** [P]. Carries settings flags +0x14 (bit 4 = "Auto Party With Friends") plus up to 20 friend guids | 14 Jul 2016 notes: "Auto-party toggle is once again an available option in game settings" (forum/patches/2016-07-13-57682), but the toggle only sets a local bit (FUN_142df3660) [P] | **REACHABLE (typed) → minimal** | S→C sub 3 {u32 mode 3, u32 code, u32 0, u32 flags 0} → "OnGroupSystemMessage" (FUN_142deb5f0; texts FUN_143c1c810: 0 Success, 1 Fail, 2 BadPlayerId, 3 AlreadyInGroup, 4 WaitListed, 5 NoAvailableGroup). Answer 3 when grouped, else 5; code 4 sets the client's wait-listed flag +0x1f9 |
| GroupPacketFindLookingForMoreGroup | 0x2B sub 0xD {flags byte} | /party lfmsearch (FUN_142df13b0) [P] | no LFM source [I] | **REACHABLE (typed) → minimal** | optional. The reply FindLookingForMoreGroupReply 0x18 is read and thrown away (FUN_142deb9a0) [P]; send nothing |
| GroupPacketGetGroupInfo | 0x2B sub 0xE {u32} | /party getpartyinfo (FUN_142df1470) [P] | none [I] | **REACHABLE (typed) → minimal** | S→C sub 0xE {u32 0, u32 code} → system message (FUN_142deba20; texts FUN_143c1c850, 0 Success / 1 Fail …). The GetGroupInfoReply 0x19 is read and dropped (FUN_142debaf0) [P] |
| GroupPacketJoinLookingForMore | 0x2B sub 0x10 {u32} | /party lfmjoin (FUN_142df1860) [P] | none [I] | **REACHABLE (typed) → minimal** | S→C sub 0x10 {u32 0, u32 code} → message (FUN_142dec540; codes 0 Success, 1 Fail, 2 AlreadyInGroup, 3 GroupNotFound, 4 WrongFaction). Answer 3 |
| GroupPacketSetGroupDescription | 0x2B sub 9 {string} | /party lfmoptions (FUN_142df1f70), Lua Groups.GroupSetLookingForMoreOptions (no UI caller) [P] | none [I] | **REACHABLE (typed) → minimal** | S→C sub 9 {u32 mode, u32 code, string desc} (reader FUN_142de4320). Mode 2 stores the description in the client group record +0x28; mode 3 shows a message (codes 0 Success, 1 Fail, 2 BadPlayerGroup, 3 NotGroupOwner). Owner → mode 2 to the group; else mode 3 code 3 |
| GroupPacketSetGroupFlags | 0x2B sub 7 {flags byte} | /party partyautoon/off, lfmon/off, lfmoptions (FUN_142df12d0 / FUN_142df1e90 / FUN_142df1f70), Lua GroupSetLookingForMore (no UI caller) [P] | none [I] | **REACHABLE (typed) → minimal** | S→C sub 7 {u32 mode, u32 code, u8 flags} (FUN_142ded840). Mode 2 sets the group flags byte +0x20; mode 3 shows a message (same codes). Owner → mode 2 to the members; else mode 3 code 3 |
| PropPacketRequestList | 0xED/9 | /queryprops (CommandQueryProps FUN_1433a22d0 → FUN_1433b2480; no admin check) [P]; the prop-manager sender FUN_142fc0320 has no reference [P] | dev command [I] | **REACHABLE (typed) → minimal** | nothing; log at debug. The reply ListReply 0xED/0xA exists (reader FUN_142faa9b0, L52) but live sent it at zone-in, not as an answer [I] |
| CommandPacketHexPermissions | 0x1E/0x34 {4-byte bitset} | FUN_142d10ad0 on zone-in / teleport (← FUN_142d58610 ← FUN_142d59f30 / FUN_142d5b290) only when the player is **not** admin and the global string at 0x1451f5a20 (copied from DAT_145431328+0x1460, a client-config field) is non-empty [P] | PS2 hex territory [I]; retail config leaves it empty (never seen in opcode_stats) | **REACHABLE only with a local config edit → minimal** | nothing; accept silently |
| InventoryPacketEquipByItemRecord | 0x2D/8 {List<ItemRecord>} (FUN_142fcbbb0) | InGamePurchaseStoreScreen.EquipStoreBundle (FUN_1435e8d40 → FUN_14324b790 → FUN_14324b820, which looks up a bundle in the store screen +0x460); the requisition reply handler FUN_1431d3260 (server-started) [P] | no shipped UI calls the native; the store pages were Daybreak-served web pages (E.2 note) [I] | **REACHABLE only from the web store → follow decision 6.59 / 6.60 (L137)** | for now nothing; log. If the store is built: equip each listed record the player owns, reusing the normal equip path |
| AbilityPacketClientTargeterEnd | 0x27/0x23 | Abilities::ClientExecutedTargeterBlock / Sweep (FUN_14354d570, vtable slots 0x14479a198 / 0x14479a150); targeters are created only from the S→C AbilityPacketTargeterStart 0x27/9 (FUN_142e1aab0) [P] | client AbilityEx.txt is stripped (one "Test" row), so live use cannot be shown either way [P data] | **UNREACHABLE on our server (server-started)** | none until TargeterStart is ever sent |
| AbilityPacketClientTargeterFindTarget | 0x27/0x24 | FUN_14354e800 (targeter base / sweep slot 7) [P] | as above | **UNREACHABLE (server-started)** | none |
| CommandPacketChargeCollision | 0x1E/0x33 {u64} | LocalProxiedCharacter / FirstPerson slot 128 (FUN_143328d50), when charge state +0x4cf0 is set by a server effect [P] | no charge ability in any source; PS2 shield charge [I] | **UNREACHABLE** | none |
| CommandPacketInteractCancel | 0x1E/0x0A | FUN_142d59db0 ← FirstPersonLocalProxiedCharacter slot 222 ← action event type 8. Its only raisers (FUN_14332d940 / FUN_143337220, CancelInteractEvent) have **no reference** (refscan NONE); FUN_142d4ac30 has none either [P] | — | **UNREACHABLE** | none |
| ClaimPacketIdSnapshotRestoreRequest | 0xF9/0x3A | FUN_142dcfcf0: refscan NONE [P] | no "snapshot" in any design source (agent-leads2-d2) | **UNREACHABLE** | none |
| ClaimPacketIdSnapshotSaveRequest | 0xF9/0x38 | FUN_142dd00a0: NONE [P] | as above | **UNREACHABLE** | none |
| ClaimPacketIdSnapshotUpdateName | 0xF9/0x3C | FUN_142dd0190: NONE [P] | as above | **UNREACHABLE** | none |
| ClaimsPacketSetSupplementalData | 0xF9/0x3E | FUN_142dd0510 (Templates.Paste chain) sends only if the admin-client check `(*DAT_1454311c8+0xb0)()` passes (always false in retail); FUN_142dd1e50 / FUN_142dd1f90 NONE [P] | — | **UNREACHABLE (admin client only)** | none |
| CreateLobbyGamePacket | 0x67/5 | FUN_1434085e0 NONE; FUN_143408a60 is ClientLobby vtable slot 11. ClientLobby is built only by the lobby S→C dispatcher FUN_142cdcbf0 (ctor FUN_143408560) [P] | no lobby in any Landmark source; Landmark used Game Tables / Game Finder [I strong] | **UNREACHABLE (server-started)** | none |
| JoinLobbyGamePacket | 0x67/1 | ClientLobby slot 12 (FUN_143408f90) [P] | as above | **UNREACHABLE** | none |
| LeaveLobbyGamePacket | 0x67/2 | ClientLobby slot 13 (FUN_143409110) [P] | as above | **UNREACHABLE** | none |
| StartLobbyGamePacket | 0x67/3 | ClientLobby slot 14 (FUN_1434097f0) [P] | as above | **UNREACHABLE** | none |
| LeaveLobbyPacket | 0x67/9 | ClientLobby slots 10 / 16 (FUN_143409060 / FUN_143409000) [P] | as above | **UNREACHABLE** | none |
| GetAllAvailableRequisitionItemsRequestPacket | 0x6E/1 | FUN_1431d6a90: NONE [P] | the only design hit is "servers requisitioned by Planetside" [P, irrelevant] | **UNREACHABLE** | none |
| RequisitionItemsItemDefinitionsRequest | 0x6E/5 | Lua RequisitionScreen.SelectRequisitionItem (FUN_1435af320; no caller in the 0.695 UI); the requisition S→C handler FUN_1431d3890 (server-started); FUN_1431d2ed0 NONE [P] | none | **UNREACHABLE** | none |
| MatchInvitationResponsePacket | 0x8A/0xA | FUN_1433fcb00: NONE [P] (agent-leads2-d1 L63) | — | **UNREACHABLE** | none |
| PacketInviteAndStartMiniGameInvitePacket | 0xAD/1 | FUN_1433fca60: NONE [P] | — | **UNREACHABLE** | none |
| PacketInviteAndStartMiniGameStartGamePacket | 0xAD/3 | built inside the zone dispatcher FUN_142d1ccf0 as the reply to S→C PacketInviteAndStartMiniGamePacket 0xAD/0 mode 2 [P] | — | **UNREACHABLE (server-started)** | none |
| ObjectiveClientCompletePacket | 0x30/7 | ClientObjectiveLookAt / ClientObjectiveUIEvent (FUN_14379ead0 / FUN_14379ed00, vtables 0x1447ea330 / 0x1447ea368). Created only by the S→C Objective dispatcher FUN_1433184e0 (FUN_14371fbc0 / FUN_14371fcd0 / FUN_14371fdd0) [P] | the tutorial / FTE uses Ui first-time events, not 0x30 objectives [I] | **UNREACHABLE (server-started)** | none |
| PacketClientAreaTimer | 0xA5 {u32 1 enter / 2 leave, u32 −1, u64} | ClientAreaTimerProperty slot 17 (FUN_1436dc8c0). That property is area property type **8** (FUN_1431adaa0 returns 8) [P]. No row of the 0.695 AreaProperties.txt has TYPE_ID 8 (types used: 15, 17, 18, 21, 23, 24, 26-29, 31-34, 36-38) [P]. Players set properties only through the AreaCreation window, which is disabled in 0.695 Lua (area-definitions-695 §1) [P] | — | **UNREACHABLE** | none |
| PacketClientFacialAnim | 0xF1 | FUN_1434a9c00 ← FUN_1434ad690 (SOEmote LiveDriver loop, "livedriver_feature_enabled" / "soemote_enabled") [P] | LiveDriverSDK.dll is not in any install or in the 2017 file list (agent-leads2-d2) [P files] | **UNREACHABLE** | none |
| PacketClientFacialAnimSettings | 0xF3 | /soemote (CommandSOEmote FUN_1433a2410 → FUN_1433b3180) [P] | as above | **UNREACHABLE (needs the missing DLL)** | none |
| PacketClientIsReady | 0x10 | FUN_142d58210 (WaitForWorldReady) [P] | every zone-in | **already handled** (MiscHandler.cs:233; opcode_stats "in 1000") | — (fix the verdict row) |
| PacketTunnelPacketFromExternalConnection | Gateway op 6 | every send template (gateway-service.md:54) [P] | — | **already handled** (Gateway/GatewaySession.cs:70) | — |
| BasePacket | — | base class of the Gateway / Login packet families (vtable users FUN_142c767c0…), not a message [P] | — | **close (not a message)** | — |
| PlayerUpdatePacketAttachObject | 0xE7 | FUN_142f82520 (AttachProxiedCharacterAction::Activate, sent only when the child +0xf4 is set) ← FUN_142f80780 ← FUN_142f83420 ← the S→C dispatcher (FUN_142cdd250), FUN_142d54040 and the ProjectileManager "Deferred Attachments" step (FUN_1431c7580) [P] | Landmark had no mounts or seats; the projectile attachments come with server-sent projectiles [I] | **UNREACHABLE without server attach state** | none (accept silently if ever seen) |
| PlayerUpdatePacketDetachObject | 0xE8 | FUN_142f83d60 ← FUN_142d20da0 (destroying an attached proxied character) [P] | as above | **UNREACHABLE** | none |
| PlayerUpdatePacketFlourish | 0xAE {u64} | FUN_142d4ae20, reached only by a tail-jump from the stub at 0x142ec9c80, which has **no reference** (refscan NONE) [P] | — | **UNREACHABLE** | none |
| ItemPacketAddItemLineTimer | 0xE6/1 | FUN_142f002a0: NONE [P] | — | **UNREACHABLE** | none |
| ItemPacketRequestTrialItemLine | 0xE6/0 | Lua Items.RequestTrialItem (FUN_1435f03d0 → FUN_142f02c40; no caller in the 0.695 UI); FUN_142f01c80 ← Items S→C handler FUN_142f01a60 (server-started) [P] | "trial" in 2016 notes means the trial key only (P6417) | **UNREACHABLE** | none |
| RepeatingActivityAddCountPacket | 0x8D/6 | FUN_1431e0100: NONE [P] | none in any source | **UNREACHABLE** | none |
| RepeatingActivityClearRequest | 0x8D/4 | FUN_1431e0160 / FUN_1431e0220: NONE [P] | — | **UNREACHABLE** | none |
| RepeatingActivityGetCountRemainingPacket | 0x8D/9 | FUN_1431e02a0: NONE [P] | — | **UNREACHABLE** | none |
| RepeatingActivitySetConsecutivePacket | 0x8D/5 | FUN_1431e0570: NONE [P] | — | **UNREACHABLE** | none |
| RepeatingActivitySetCountPacket | 0x8D/8 | FUN_1431e05d0: NONE [P] | — | **UNREACHABLE** | none |
| SocialPacketDiplomacyEvent | 0x9C/0x13 | FUN_142f6cdc0: NONE [P] | EQN housing / diplomacy | **UNREACHABLE** | none |
| SocialPacketHouseRating | 0x9C/0x15 | FUN_142f6ce90: NONE [P] | — | **UNREACHABLE** | none |
| SocialPacketHouseFurnitureScore | 0x9C/0x18 | FUN_142f6ce20: NONE [P] | — | **UNREACHABLE** | none |
| TargetPacketRequestAddTarget | 0xB5/1 {u64 guid, u32 slot} | ClientTargetManager vtable slot 1 (FUN_1431e5700, request flag set). No virtual `+8` call on the manager (PlayClient +0x32b20) in the decompile; only +0x10 / +0x38 / +0x40 / +0x48 / +0x50 are used [P from a grep of the decompile] | — | **UNREACHABLE** | none |
| TerrainCellPacketResetAllCells | 0xEC/8 | FUN_143158bb0: NONE [P] | — | **UNREACHABLE** | none |
| TerrainCellPacketResetTemporaryCells | 0xEC/9 | FUN_143158bb0: NONE [P] | — | **UNREACHABLE** | none |
| LeaderboardPacketGetPlayerStats | 0xC6/2 | FUN_143304aa0: NONE; ClientLeaderboardManager has only a destructor (agent-leads2-d1 L59) [P] | live leaderboards were the Gallery's (P4670) | **UNREACHABLE** | none |
| SelectTaskRequest | 0x32/6 | Lua Ui.RequestSelectTask (FUN_1435cd520 → FUN_1433fb5c0); the name is in no 0.695 UI Lua, Data.xml or ScriptsBase.bin [P] | none | **UNREACHABLE** | none |

## 3. BUILD list (ranked by player impact)

### B1. Teleport requests 0x1E/0x51, 0x52, 0x3F6 ("Evac to Safety", GM /location and teleport-to-camera)

Impact: every player who uses the Escape menu's "Evac to Safety" inside a zone is left without an answer, and their
movement update stays switched off.

- **Where:** `Network/Handlers/InteractionHandler.cs` HandleCommand. Add cases 0x51, 0x52 and 0x3F6 and route them to a
  new `Teleport.HandleClientRequest(session, sub, body)`.
- **Bodies** (after the u16 0x1E and u16 sub header, which writer FUN_142c12ce0 writes):
  - 0x51: `f32 x, f32 y, f32 z, f32 w` (16 bytes, from FUN_14338db30).
  - 0x52: `vec4 pos, vec4 rot, vec4 extra` (48 bytes, from FUN_14338dd40).
    - `rot` is the heading quaternion `(0, sin(h/2)·k, 0, cos(h/2))`, built from DAT_1455f07a0 / FUN_1437f3920 /
      FUN_1437f3110 [I exact axis].
    - `extra` is DAT_1455f0740, a default constant.
  - 0x3F6: `vec4 pos, u64 mover guid` (from FUN_14338d910; the guid comes from the mover entity's +0x278 getter).
- **Rule:**
  - **Admin** (WorldServer.json Admins, the same flag as +0x7b0d8): accept any point.
  - **Everyone else:** accept the point only if one of these holds:
    - (a) its x / z lies within a few metres (≈ 8 m, to allow for position-update lag) of the server's position for
      the player. This is the evac "snap to ground" case: the client keeps x / z and clamps y to ground + DAT_14469adc0.
    - (b) it lies inside the build site the player is standing in. This is the claim branch: the target is a claim
      object's position, claim +0x348 → hash +0x378 → entry +0x3a8 [P]. Which object that is (site spawn point?) is [I].
  - Otherwise refuse, and answer with the player's current server position.
- **Answer:** always `Teleport.To(session, pos, snapToGround: true)` for 0x51 / 0x3F6, and the given heading for 0x52.
  - This sends ClientUpdate TeleportToLocation, which FUN_142d59f30 needs in order to clear the pending bit 8 [P].
  - Answer within the same tick if possible: the client is frozen until the reply arrives.
  - Respect Teleport.cs's existing in-place-jump / re-zone rules.
  - Throttle by reusing Health's `lastEvacuation` (10 s), but still answer a throttled request with the current position.
- **Verify by the client's reaction:**
  1. On the 0.695 test copy, stand on open ground, press Escape and choose "Evac to Safety".
  2. Before the fix, the character cannot move. After the fix, it is re-seated on the ground and moves normally.
  3. Repeat on your own build site and on a mover.
  4. As an admin, check `/location x y z` and `/teleporttocamera`.

### B2. Accept Combat 0x24 subs 2 / 3 / 4 silently

AutoAttackOff 0x24/3 is sent when the player's target dies and on interaction auto-select.

- Register a `ZoneMessageHandler(ZoneMessageOpcode.CombatBase)`.
- Sub 3 (empty): no-op.
- Subs 2 and 4 `{u64 target}`: log at debug only.
- Do not count them as Unanswered.
- Nothing is sent back: the client resets +0x32802 / +0x32808 itself before sending [P FUN_142d090a0].

### B3. `/party` hidden sub-commands: answer minimally in `Game/Groups.cs`

Today these subs fall into the default branch, which calls OpcodeStats.Unanswered. Replace it with the answers below.
Payloads use GroupPackets framing (`u8 sub` first, then u32 fields, as JoinResult does).

| sub | Request (C→S) | Answer (S→C) |
|---|---|---|
| 3 AutoGroup | — | `{u8 3, u32 3 (result), u32 code, u32 0, u32 0}`; code 3 (already in a party) when grouped, else 5 (NoAvailableGroup, text 8644 "Searching for a party to auto join..."). Do not send code 4: it sets the client's wait-listed flag +0x1f9 |
| 7 SetGroupFlags | `u8 flags` | **Leader:** `{u8 7, u32 2, u32 0, u8 flags}` to every member (the client stores it in its group record +0x20), then `{u8 7, u32 3, u32 0, u8 flags}` to the sender ("Party recruitment flags successfully set.", 8660). **Not leader / not grouped:** `{u8 7, u32 3, u32 3 or 2, u8 0}` |
| 9 SetGroupDescription | string (StringFixed<128>) | **Leader:** `{u8 9, u32 2, u32 0, string desc}` to the members, then `{u8 9, u32 3, u32 0, string ""}` to the sender ("…description successfully set.", 8664). **Otherwise:** code 3 / 2. Reader: FUN_142de4320 (u16, u8 sub → +0x14, u32 → +0x10, u32 → +0x18, string → +0x20) [P] |
| 0xD FindLookingForMoreGroup | — | none needed: the reply 0x18 is read and thrown away (FUN_142deb9a0) [P]. Log only |
| 0xE GetGroupInfo | `u32` | `{u8 0xE, u32 0, u32 code}` → system message (FUN_142deba20; texts FUN_143c1c850: 0 Success, 1 Fail). Send code 0 when grouped, else 1 |
| 0x10 JoinLookingForMore | `u32 group` | `{u8 0x10, u32 0, u32 3}` → "Searching for more members..." (GroupNotFound, FUN_142dec540) |

- **Social-service forwarding:** these subs are not in GroupMessages.IsForwardedSub. Keep them local, or forward 7 / 9
  with the others if the group lives in the service.
- **Verify** on the 0.695 client: type `/party autoparty`, `/party getpartyinfo` and `/party lfmoptions …`, and check
  that the chat log shows the system messages.

### B4. Accept silently (log only, no Unanswered count)

- PropPacketRequestList 0xED/9 (`/queryprops`, no admin check).
- CommandPacketHexPermissions 0x1E/0x34 (only with a client-config string).
- InventoryPacketEquipByItemRecord 0x2D/8: keep it logged until decisions 6.59 / 6.60 settle the store.

## 4. CLOSE lines for OPEN-LEADS (L140)

- **L140 CLOSED 2026-10-18** (agent-l140.md): all 58 parked rows triaged.
  - **Built later:**
    - Command 0x51 / 0x52 / 0x3F6, reached from the Escape menu's "Evac to Safety" (Data.xml:1081 →
      FUN_1435fdb70 → FUN_1433c80a0). The client freezes until the reply arrives (pending bit +0x1457 & 8 is cleared only
      by FUN_142d59f30). Task B1.
    - Combat 0x24/3 is sent in normal play: accept it (B2).
    - Group subs 3 / 7 / 9 / 0xD / 0xE / 0x10 via `/party`: minimal answers (B3).
  - **Closed as handled:** PacketClientIsReady (= ClientIsReady 0x10), PacketTunnelPacketFromExternalConnection (Gateway
    op 6), BasePacket (base class).
  - **Closed UNREACHABLE, no reference in the retail exe (refscan NONE):**
    - claim Snapshot Save / Restore / UpdateName;
    - RepeatingActivity ×5;
    - Social Diplomacy / HouseRating / HouseFurnitureScore;
    - MatchInvitationResponse, MiniGameInvite;
    - Leaderboard GetPlayerStats;
    - TerrainCell ResetAll / ResetTemporary;
    - ItemAddItemLineTimer;
    - GetAllAvailableRequisitionItems;
    - Flourish (stub 0x142ec9c80);
    - InteractCancel (the CancelInteractEvent raisers FUN_14332d940 / FUN_143337220 have no reference).
  - **Closed UNREACHABLE, admin client only (+0xB0):** ClaimsPacketSetSupplementalData.
  - **Closed UNREACHABLE, server-started state Landmark never sent:**
    - lobby ×5 (ClientLobby built only by FUN_142cdcbf0);
    - RequisitionItemDefinitions;
    - MiniGame StartGame;
    - ObjectiveClientComplete (client objectives only from S→C 0x30);
    - ChargeCollision;
    - ClientTargeterEnd / FindTarget (only after S→C TargeterStart 0x27/9);
    - Attach / Detach (attach actions);
    - TargetRequestAddTarget (no `+8` call on ClientTargetManager);
    - TrialItemLine;
    - SelectTaskRequest (no UI caller).
  - **Closed UNREACHABLE for other reasons:**
    - PacketClientAreaTimer: area property type 8 is absent from the 0.695 AreaProperties.txt, and the area property
      editor is disabled.
    - FacialAnim / FacialAnimSettings: LiveDriverSDK.dll was never shipped.
- **New lead (L140a), replaces the targeter part of L140:** did live 0.695 combat send S→C AbilityPacketTargeterStart
  0x27/9 (client-executed sweep / block targeters for melee)?
  - The client AbilityEx.txt is stripped, so the client cannot say.
  - Paths to try: packet captures or videos of 2016 melee hit detection; the server's ability data if ever found.
  - If yes, handle 0x27/0x23 and 0x27/0x24.
- **Doc fix:** trace/architecture/admin-monitor.md lists FUN_1433b5430 (teleport to camera) as "client-side move".
  - It sends CommandPacketTeleportToLocation 0x1E/0x51 through FUN_1433c80a0.
  - The same is true of `/location x y z` (FUN_1433af380 → FUN_1433c80a0).
- **Verdict rows to update** in re/tools/verdicts.tsv:
  - PacketClientIsReady → handled;
  - PacketTunnelPacketFromExternalConnection → handled;
  - BasePacket → informational (not a message);
  - the three Command teleport subs → gap (build B1);
  - CombatPacketAutoAttackOff → gap (B2);
  - the six Group subs → gap (B3);
  - everything else → unreachable, with the evidence above.

## 5. Parked rows not on L140 (left with their own leads / decisions)

| Rows | Parked on |
|---|---|
| MerchantHandler.SellItem / BuyBackItem | merchant NPCs (none in Landmark) |
| Browser / InGameBrowser / InGameMarketplaceBrowser OnHide / OnClose; 22 PacketInGamePurchase* rows; ClientLoyaltyPointsUpdate | decisions 6.59 / 6.60 |
| Store Commerce* / LiveGamerPostItem / WallOfDataMarketplace / PacketClientInGamePurchase / StoreBundleContentRequest | L137 / L123 |
| Teleport.CancelQueue, PacketTeleportPostQueueNotify, PacketLoginQueueStatus | L60 |
| SetWaypointPacket, ZoneWaypointPacket | L136 |
| LoadingScreenPacket, RewardBasePacket, RecipePacketZoneCraftingWindow, InstancedGamePropStatePacket, ExecuteScriptPacket | L143 |
| ClaimsPacketLostClaimServerConnection | L142 |
| BaseSkillAdminPacket, AdminCommandPacket DebugPathQuery / PathingDataMonitor / StorybricksRequestDiagnostics / ZonePlayerCount, ClaimsPacketIdDebugAddItemClaim | E.6 / L4 / L145 / L146 |
| CommandPacketProfileDefineSlot / SetProfile, Loadout* ×6, Skill* ×5 | L138 |
| LootPinataPacketRequestClaimOwnerlessLoot | built (E.4) / L141 |

Also in the lead text but not parked rows (their verdicts are "unreachable"): map teleports are admin-only [P]
(FUN_143054920: Admin.MapTeleport needs +0xB0 or +0x7b0d8).
