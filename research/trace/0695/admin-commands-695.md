# Admin slash commands of the retail client (0.695) — Command 0x1E subs

Traced 2026-10-16 from the 0.695 decompile (re/decomp_all_695; 0.589 names in trace/0695/coverage-695.md). The client
only runs these commands for an admin client (never on retail, `+0xB0` returns 0) OR an admin player (the bool the
server sends in SendSelfToClient, player +0x7b0d8; the emulator sets it for WorldServer.json "Admins" since
2026-10-16; see trace/architecture/admin-monitor.md "0.695: every use of the admin-player flag"). Server:
`LandmarkEmulator.WorldServer/Game/AdminCommands.cs`, dispatched from InteractionHandler.HandleCommand, gated again
by `Game.Admin.IsAdmin` (a modified client's command is refused and logged).

Common wire prefix (every writer calls FUN_142c12ce0): `u16 0x1E, u16 sub`. Field writers: `u64` FUN_142c02460,
`string` FUN_142d4b510 (u32 length + bytes), `vec4` FUN_142c32fb0 (4 × f32), `bool` FUN_142c32c60 (u8). The
send functions build a PacketTunnelPacketFromExternalConnection whose first byte is `(channel << 5) | 6`; channel 1
(/servertime, /friend forceadd) means the world server, 0 the zone — the same handler here.
`[P]` = read from the writer / command function, `[I]` = inferred (named by the command's use, not proven).

Status column: **built** = implemented on the server, **logged** = decoded and logged only, **refused** = never done.

**2026-10-17 fix (f7e2307):** until then the server wrote the admin-player flag in SendSelf as `false` (the bool before
the last list and the bio, 0.695 reader FUN_142c19bd0 → player +0x7b0d8), so no admin client gate ever opened and none of
these commands left the client. Found by reading the byte in the running test client (re/test/flag_probe.js: 0 for a
configured admin). Everything below "untested in game" was blocked by this, not by its own code.
All are **untested in game** (no admin client session yet; test steps in TESTING-NEEDED.md).

| Sub | Packet | Client command / sender | Wire body (after the prefix) | Server | Status |
|---|---|---|---|---|---|
| 0x42E | AdminCommandPacketGotoRequest | /goto [player\|npc] name/guid, /warp (FUN_1433a7d70 → FUN_14338a3c0) | u64 guid (+0x20: typed guid, else admin target, else invalid) [P]; string name (+0x28 StringFixed<64>) [P]; u64 (+0x88, never assigned by the command) [P] | `Goto`: online player by guid / exact / unique prefix / unique substring → `Teleport.ToZone` beside them (landscape change included) | built |
| 0x430 | AdminCommandPacketGotoWaypointRequest | /goto waypoint [name/guid], /goto way (ctor FUN_1433960f0, writer FUN_14338a580) | u64 guid (+0x20: own guid when no target) [P]; string name (+0x28) [P] | `GotoWaypoint`: logged + chat line; server has no player waypoints (L104) | logged |
| 0x4B5 | AdminPacketVerifyAdminTarget | /target name/guid, /gm target (FUN_1433a7420 → FUN_14338c780); only when the character is not in view (local lookups FUN_142f853f0 / FUN_142cd1cb0 first) | C→S: u64 guid (+0x20) [P], string name (+0x28 StringFixed<32>) [P]. **S→C reply, same sub** (reader FUN_1433919c0, handler FUN_1433bf9b0): u64 guid, string (i32 len + bytes) [P] → client sets its admin target to the guid, prints "Admin Target set to Guid: %llu"; the string is not used | `VerifyTarget`: character database by guid / exact name (online or not) → reply {guid, name}; miss → chat "Could not locate target" | built |
| 0x458 | AdminCommandPacketHideMe | /hideme [on\|off] (FUN_1433ac520 → FUN_14338a730) | u8 hide (+0x20) [P]; no arg = toggle from bit 31 of own entity +0xa20 | `HideMe`: PlayerUpdateRemovePlayer to the landscape, no introduction / movement fan-out while hidden (`PlayerManager.Introduce`, `RelayMovement`, `ShowPosition`, `RelayHeroicMovement`); unhide re-introduces (`Reshow`) | built |
| 0x4D8 | AdminCommandPacketRunspeed | /run inc\|dec n \| default \| n (FUN_1433b2a20 → FUN_14338ba60) | f32 speed m/s (+0x20) [P]: absolute; inc/dec add to the controller's current speed (vfunc +0xC0), floor = constant DAT_144c410f0 (= "default"; value not in the decompile text, L105). Client prints "Player run speed set to %.3f" / "reset to default" itself. Readable back by FUN_143391910 (same layout) | `Runspeed`: overrides calculated stat 5 (cStatTypeMovementSpeed, default 12) for the session and re-sends the stats [I: the stat is the server's run-speed knob] | built [I] |
| 0x429 | AdminCommandPacketGameTime | /servertime (FUN_1433b3d00, channel 1) and /zonetime (FUN_1433b7350, channel 0): hour h \| pause \| sync \| warp n (→ FUN_14338a220 → FUN_14337f8d0) | u8 +0x20, u8 +0x21, u8 +0x22, u8 +0x23, u8 +0x24, f32 +0x28 [P]. Fills [P]: hour → {0,0,h,FF,FF,0}; pause → {1,0,FF,FF,FF,0}; sync → {0,1,FF,FF,FF,0}; warp → {0,0,FF,FF,FF,n}. So +0x20 toggle pause, +0x21 resync, +0x22 hour (FF none; /servertime allows 0-24), +0x23/+0x24 minute/second [I], +0x28 game s per real s | `GameTime`: `WorldClock.SetPaused / SetHour / SetRate`, then GameTimeSync to everyone online (CycleSpeed 0 while paused) | built |
| 0x42A | AdminCommandPacketClientTime | /clienttime set h m s \| hour f \| pause \| sync \| warp f \| advance f \| reverse f (FUN_1433b55e0 → FUN_1433897f0 → FUN_14337f650) | u32 mode (+0x20), u32 (+0x24), u32 (+0x28), u32 (+0x2C) [P]; modes [P]: 1 set {h,m,s}, 2 hour {f32}, 3 pause, 4 sync, 5 warp {f32}, 6 advance {f32}, 7 reverse {f32}. **S→C same sub**: reader FUN_143381ee0 → handler FUN_1433ba260 (admin-gated), 7-way jump table on mode not recovered by the decompiler (L106) | `ClientTime`: echoes the packet to the sender [I: live's zone server relayed it to the client's own clock]; logged | built [I] |
| 0x4EA | AdminCommandPacketSpawnProp | /prop id (FUN_1433b1e50 → FUN_14338c210, element FUN_143380290) | u32 count; per prop: u32 propId, f32 x, f32 y, f32 z, vec4 rotation (quaternion from camera yaw, FUN_1433b88f0), vec4 scale (client unit constant) [P]. Position = ground under the look-at point (ray FUN_142b7e470; "Failed to find ground to spawn Prop."); client prints "Trying to summon prop with prop ID %d" | `SpawnProp` → `Props.PlaceAdmin`: free prop at that transform, owned by the admin, persisted, broadcast | built |
| 0x4ED | AdminCommandPacketDeleteAllProps | /prop reset_zone_and_wipe_db_forever_with_no_undo (FUN_1433b1e50 → FUN_143389990) | header only [P] | `DeleteAllProps`: logs who tried; nothing deleted | refused |
| 0x49C | AdminCommandPacketSpawnVehicle | /vehicle id [auto mount] [faction 1-3] (FUN_1433b6030 → FUN_14338c420 → FUN_143380400) | u32 vehicleId (+0x20), u8 factionId (+0x3C), f32 x, y, z (+0x24..; ground under look-at), f32 yaw (+0x30), f32 0 (+0x34), bool autoMount (+0x38) [P]; client prints "Trying to summon vehicle with vehicle ID %d" | `SpawnVehicle`: logged (no vehicle module, L107) | logged |
| 0x4F0 | AdminCommandPacketSetModel | /setmodel id (FUN_1433b4110 → FUN_14338bde0) | u64 targetGuid (+0x20, admin target or invalid), u32 modelId (+0x28) [P] | `SetModel`: logged (L107) | logged |
| 0x499 | AdminCommandPacketAddAmmo | /ammo add n [ammo slot] [equip slot] \| addto name/guid n ... (FUN_1433a3170 → FUN_143389300 → FUN_14337f410) | string targetName (+0x20 StringFixed<32>, "addto" only), u64 targetGuid (+0x60), u32 count (+0x68), u32 ammoSlot (+0x6C, -1), u32 equipmentSlot (+0x70) [P] | `AddAmmo`: logged (no ammo system, L107) | logged |
| 0x4EE | AdminCommandPacketWeaponSet | /weaponset profile\|equip\|cycle\|dump\|select (FUN_1433b64e0 → FUN_14338c5c0) | u64 targetGuid (+0x20), string command (+0x28), string args (+0x40) [P] | `WeaponSet`: logged (L107) | logged |
| 0x4EF | AdminCommandPacketPlacedEffect | /placedeffect command args (FUN_1433b02d0 → FUN_14338aac0) | u64 targetGuid (+0x20), string command (+0x28), string args (+0x40) [P] | `PlacedEffect`: logged (L107) | logged |
| 0x4F1 | AdminCommandPacketFreeFlyHide | free-fly hide [on\|off] (FUN_1433a5d80 → FUN_143389e90) | u8 hide (+0x20) [P]; no arg = toggle from bit 35 of own entity +0xa20 (u64) | `FreeFlyHide`: logged; what it hid is not proven (L107) | logged |
| 0x4CF | AdminCommandPacketForceAddFriendRequest | /friend forceadd target [source] (FUN_1433a5e60 → FUN_143389b30, channel 1) | identity target (+0x20), identity source (+0xD0) [P]; identity (FUN_142ddc260) = u32, u32, u32, string first, string second — the typed name in the first string (normalised FUN_143b87120), rest empty / 0 | `ForceAddFriend`: `Friends.HandleAdd` on the source's session (the admin when empty; the source must be online) | built |
| 0x4D0 | AdminCommandPacketForceRemoveFriendRequest | /friend remove target [source] (FUN_1433a5e60 → FUN_143389ce0, channel 1) | identity target, identity source [P] | `ForceRemoveFriend`: `Friends.HandleRemove` on the source's session | built |
| 0x3EC | AdminCommandPacketProfile* | /profile add\|setgroup\|delete\|select\|setrank\|setJobExp\|showSelected\|list (FUN_1433b0810) | u16 profileSub (+0x20) [P]: 1 Add, 2 Delete, 3 SetXp, 4 SetRank, 5 Select, 6 ShowSelected, 7 ListAll, 8 SetProfileGroup. Add/Delete/Select/SetGroup (FUN_14338ac80/aec0/b2f0/bfd0): u32 profileId (+0x28), u64 targetGuid (+0x30). SetRank/SetXp (FUN_14338b530/b6d0 → FUN_14337fe20/fef0): u32 profileId, u32 rank/xp (+0x2C), u64 targetGuid (+0x30). ListAll/ShowSelected (FUN_14338b100/b870): u64 targetGuid (+0x28) [P] | `Profile`: decoded and logged (job profiles are not a server feature, L107) | logged |

### /item (FUN_1433acb90, admin-player gated) — handler group "Item", built 2026-10-17 (AdminCommands.Items.cs)

Usage: `/item add <item> [count] [tint] [target]`, `delete|undelete <item guid> [target]`, `list [target]`,
`check <item> [target]`, `vault|unvault|collect|uncollect|vaultcollect|vaultuncollect|overflow|unoverflow|buyback|unbuyback
<item id, 0 = all> [target]`, `random <item> [catalyst -1] [count 1]`, `claimAddItem <item> <count> <tint> <claim> [target]`,
`claimItemToInventory <item guid> <claim>`. The target is a typed name / guid, else the admin target, else the admin.

| Sub | Packet | Wire body (after u16 0x1E, u16 sub) | Server | Status |
|---|---|---|---|---|
| 0x3EA | PacketAddItem (FUN_1433c7300 → FUN_14338f8c0 / FUN_1433813c0) | u32 item, u32 tint, u32 count (≥ 1), u64 target, u32 0, u32 0, u8 0 [P] | GiveItem to the online target; tint reported only (no tinted items) | built |
| 0x3EB | PacketDeleteItem (FUN_143390000) | u64 item guid, u64 target, u8 undelete [P] | delete: TakeFromStack, remembered until restart; undelete gives it back | built |
| 0x433 | PacketDeleteItemById (FUN_1433c7690 → FUN_143390200, no caller) | u32, u32, u64 target [P]; read as item, count [I] | TakeItem | built [I] |
| 0x434 | PacketGetItemList (FUN_143390590) | u64 target, u32 [P] | chat list by container | built |
| 0x48A | PacketGetItemInfo (FUN_1433903a0) | u64 target, u32 item [P] | chat: count and stacks | built |
| 0x4F3 | PacketRandomItem (FUN_143390d20 / FUN_143381b30) | u32 item, i32 catalyst (-1), u32 count [P] | gives the item (no rolled variants in 0.695) [I] | built |
| 0x4F2 / 0x4F4 / 0x4FA / 0x4FF / 0x500 | Vault / Collection / VaultCollection / Overflow / Buyback ItemById | u64 item (guid or id, 0 = all), u64 target, u8 into [P] | moves bag stacks into the container (vault 1, collection = resources 5 via the resource slots, overflow 7) or back; buyback 4 and vault collection 6 have no 0.695 data source, refused with the reason | built |
| Claims 0xF9 / 0x60 | ClaimsPacketIdDebugAddItemClaim (FUN_1433c73a0 → FUN_14338caf0 / FUN_143380850) | u32 item, u32 tint, u32 count, u64 target, u64 claim, u32, u32, u8 [P] | claims hold no items here (L146) | parked |

### Grief (GriefAdmin 0x1E / 0x4DA, GriefBase 0xC0) — 2026-10-17

Admin request {u32 state 1, u32 type, ...}: 1 SetDamageLevel {u32}, 2 SetGriefLevel {u32}, 3 ResetGrief [P]. **The senders
(FUN_142ee2280 / FUN_142ee2590 / FUN_142ee21f0) are never referenced in the 0.695 exe** (re/tools/refscan.py), so the
retail client cannot send them; the server handles them anyway (AdminCommands GriefAdmin). The receiving side is live:
GriefBase 0xC0 (FUN_142ee1bf0, called by the dispatcher) {u8 sub}: 1 points {i32 points, i32 second value; -1 = keep},
2 level {i32 old, i32 new} (0.589 FUN_142e42f50; texts "entered / left grief level %d", equipment lock at 4, suspension
at 5, Lua HudHandler:OnGriefLevelChanged), 3 reset. Server: Game/Grief.cs. Live's rule for earning grief: L144.

### Admin senders with no reference in the 0.695 exe (proof for "unreachable", 2026-10-17)

re/tools/refscan.py found no call / jmp / RIP-relative LEA / 8-byte pointer to these senders (only the exception
tables name them; control: the scan finds /item add's call at 0x1433acf0b): all 15 SkillAdmin (0x1E / 0x49A, base
{u32 state, u32 type, u64 sender, u64 target}; types 1 Echo ... 0xF ResetAllSkills), AcquireTimerAdmin 0x4DE (types 1-4;
the client only reads the List reply, FUN_142e361a0), ItemAdmin 0x4E7 (item-line trial timers), CurrencyAdmin 0x4E8
(discounts), Leaderboard 0xC6 / 1 and 4, MiniGameFlashCommandAdmin 0x62 / 0x2F, StorybricksRequestDataErrors 0x4FD,
GriefAdmin 0x4DA. The Content builder commands (ContentPacketCommand*) are reached only from Content.* Lua natives that
no 0.695 UI script nor the shipped admin.lua calls. ClaimsPacketSetAsAdminClaim (FUN_142dd0650) and
EnvServerDataMonitorUpdateRequest are gated by the admin-client check alone (+0xB0, always 0 on retail).
Still open: PathingDataMonitor 0x4A9, StorybricksRequestDiagnostics 0x4FC and ZonePlayerCount 0x4CC (admin monitor
pages, replies L4), DebugPathQueryRequest 0x4A7 (pointer table 0x1447e9698, L145).

Also in the same command family but **not sent by an admin**: CommandPacketHexPermissions 0x34 (FUN_142d10ad0 skips it
for admins); and the /friend sub-commands `friendpositions` (0x40D CommandPacketFriendsPositionRequest, sent as a
normal CommandPacketUITest-shaped packet) and `list` / `client` are not admin packets.

## Client-side only (no packet, listed for completeness)

/loc, /clear resource, Demo mode, radius / fog / foreground scale / shadow quality / near-far, teleport to camera
(a client move the server sees as a position update), SOEmote webcam, "/target clear" ("Admin Target Cleared.") —
see admin-monitor.md.

## Constants and globals met

- `DAT_145462af8`: the client's invalid-guid value (compared everywhere a guid may be "none"); its initialiser is
  outside the decompile text (L105). The server treats both 0 and 0xFFFFFFFFFFFFFFFF as none.
- `DAT_1454311c8 + 0x1a0` (vfunc): the current admin target guid; `DAT_1454311c8[0x64a1]`: the player's own guid.
- `DAT_144c410f0`: the default / minimum run speed (m/s) used by /run default (L105); `DAT_145439f60`: the
  last run speed set by the admin (client-side memory).
- Handler-group names in the packet classes: "ZoneCommand" (most), "Snoop" (HideMe, FreeFlyHide), "Vehicle",
  "Profile", "PlacedEffect", "WeaponSet", "SetModel" — live's zone-server dispatch groups.

## Open leads (trace/0695/OPEN-LEADS.md)

L104 player waypoints on the server (for /goto waypoint), L105 the invalid-guid and default-run-speed constants,
L106 the ClientTime receive jump table (7 cases), L107 the systems behind the logged-only subs (vehicles, set model,
ammo, weapon sets, placed effects, free-fly hide, job profiles).
