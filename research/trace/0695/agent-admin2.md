# Agent admin2 — admin / session / group / site-game fixes (2026-10-17)

Branch **`e-admin2`** (worktree `<workspace>\wt-admin2`, from `client-0.695` bd03023; merges cleanly with the
current `client-0.695` tip). Not pushed. Built: WorldServer, WorldChannel, GatewayServer, CommerceServer
(`dotnet build -v q`, all succeed). Nothing was run (no Docker / servers / client).

Source reports: `agent-leads-sweep.md` (L105, L61, L77, L52, L122, L97, L37, L106, L123, L65, L144) and
`agent-e-leads.md` (L4, L145). Marks: [P] proven from the client, [I] inferred (reason given).

| Commit | Leads |
|---|---|
| 8c5198b | L105, L106, L4, L145, L61 |
| f7b0111 | L77 |
| 45d23ab | L52, L122, L97, L37 |
| 9018cff | L123 |
| 77f0f18 | L144 |
| 53018cc | dev test hooks `offgridpaste`, `pathquery` |
| c4b4883 | docs/admin.md |

---

## 1. What was built, with evidence

### L105 — `/runspeed default` (Game/AdminCommands.cs `Runspeed`)
- [P, sweep] "default" sends **0.0** (FUN_1433b2a20, LAB_1433b2ec5); inc / dec are floored at DAT_144c410f0 = 1.0;
  an absolute value below 0 is sent as 0.
- **Change:** a speed under 0.001 (and the normal speed) **removes the override**. Before this, 0 was stored as a
  0 m/s run speed. A chat line confirms the reset.
- `NoGuid`: the comment now says the invalid-guid global DAT_145462af8 is .bss with no writer, so its value is **0**
  [P]. All-ones stays as a defensive alias.

### L106 — `/clienttime` (Game/AdminCommands.cs `ClientTime`)
- [P] Jump table 0x1433ba644 → ClientGameTimeManager DAT_1454319a0. The 7 modes are documented in the XML comment:
  set, hour, pause, sync (rate 8.0), warp, advance, reverse (± a·3600 s).
- [P, checked here] The GameTimeSync handler FUN_1431dace0 sets the time, the rate (+0xb4) and paused (+0xc0)
  unconditionally. So a /clienttime change lasts only until the next sync.
- **Change:**
  - The packet is still echoed, which is how the client applies it.
  - Modes 0 and > 7 are refused, as are NaN / infinite float arguments.
  - The admin gets a chat line describing the change.
  - After **sync** (mode 4), the server also sends a GameTimeSync, so the client rejoins the world clock
    [I: from the name; the client's own case only resets the rate].
- `WorldClock.DefaultCycleSpeed` was already 8.0.

### L4 / L145 — admin monitor requests and the path query (new Game/AdminCommands.Monitor.cs)
- [P, checked] All four requests go out on channel 0 (Zone): FUN_14321ccd0, FUN_143204600 and FUN_1432040a0 are
  called with channel 0, and so is FUN_14379adb0.
- **Log-only handlers** (admins only, at most one log line a minute per admin and kind):
  - PathingDataMonitorUpdateRequest 0x4A9 (header only);
  - ZonePlayerCount 0x4CC {u32};
  - StorybricksRequestDiagnostics 0x4FC {u64 target, u8 flag}.
  - No reply is sent, because none of the replies has a reader (L4).
- **DebugPathQueryRequest 0x4A7** {vec4 start, vec4 end, u8 flag}:
  - [P] Writer FUN_14379adb0 re-read: 4 f32, 4 f32, then a byte.
  - The handler runs `Pathing.Line`, then `Pathing.RequestPath` on the pathing thread.
  - The grid is Cave when the landscape has caverns and the start is more than 40 m below the generated ground,
    else Surface. This is the dev "pathing" rule.
  - A new world-tick step, "admin path queries" (`AdminCommands.Update`), reports the result in chat when the
    query is done, or after 10 s: straight-line verdict, reached / partial / none, waypoints, walked length, nodes.
  - 0x4A8 is not sent (no reader).
- Wired in: `IsAdminSub`, the `Handle` default branch, the `InteractionHandler` case list and HostedService.

### L61 — the relogin session is always answered (WorldChannel, WorldServer, GatewayServer)
- [P, sweep] FUN_142d57e60 sets a 60 s deadline. When it passes, run state 0x15 logs "Timed out while waiting for a
  relogin session." and the client shuts down (state 0x17).
- **World-channel service** (`Logout.cs`, `ChannelRelay.cs`):
  - When the login service gives no session (its 10 s limit, or an exception), the request is now **handed on to
    the zone**, which answers from the auth database. Before, the client got "not ok" and shut down.
  - Without the zone's relog tag, the request goes to the zone as before. If the zone link is down, the login
    service answers alone, with a key that has no tag (only the client's item cache is lost).
  - With neither the login service nor the zone, the client is told **"not ok"** at once instead of being left to
    time out.
  - `HandOn` was split out of `FromClient`; `Logout.Answer` builds the 0xF7.
- **Zone** (`LocalWorldChannelService.CharacterSelectSession`):
  - The key is sent only once the save has **succeeded**. Before, a failed save still answered "ok" with a key the
    login server would refuse, because `TaskEvent` runs on a faulted task too.
  - A save that fails or takes more than 30 s is answered "not ok".
- **Gateway** (`GatewayClient.OnTunnelData`):
  - Case: a 0xF6 on channel 1 while nothing can take channel 1 (zone held and no world-channel service). A held
    packet would wait up to `HoldLimit` (120 s).
  - The gateway now answers it itself with the login service's relog session: 0xF7, then the connection closes
    after 30 s, as in the hold path.
  - If the login service gives nothing, the packet is held as before.

### L77 — off-grid paste (new Game/BuildingOffGridPaste.cs; Building.cs, BuildingClipboard.cs)
- [P] Every sub-writer of FUN_14315b130 was expanded. The full body:
  - u8 layer;
  - **f64 × 3 position**. FUN_142e9d500 produces doubles: cell × cell size + voxel × voxel size, which are global
    terrain units;
  - **f32 × 16 transform**, written as 4 rows;
  - u32 paste mode (constructor default 1);
  - u8 Remove voxels;
  - u8 × 3;
  - PropData list (FUN_1430fe1b0 = the PropData writer; `SkipPropData` matches it);
  - u64 design guid;
  - u32 n × {u64 cell, u32, u32};
  - material map u32 n × {u16, i32};
  - ConstructionArea {u64 cell, 3 × u32 origin, 3 × u32 size};
  - u8.
  - The writer **ends there**. The sweep's "and more after that" was wrong.
  - The parse must end exactly at the body length.
- [P] The paste cursor FUN_1436d00b0 calls the on-grid FUN_1431642e0 or the off-grid FUN_1431645b0 with the same
  mode (cursor +0x2e4) and delete flag (+0x2e0). The choice is made by flag +0x2e8.
- [P] The client's own bounds (FUN_14315feb0): `world = pos + l.x·row0 + l.y·row1 + l.z·row2 + row3`, with l in
  voxel size × the clipboard's size.
- **Server:**
  - The transform is inverted. Every target voxel in the turned box takes the clipboard voxel its centre falls in
    (nearest voxel). In-voxel offsets are turned with the matrix [I: resampling rule ours].
  - Then the on-grid paste's rules apply: Player Studio owner check (0x1A), per-cell `MayApply`, a single
    `Env.WriteCells` (job type 11) with the cost checked and charged, an undo record, props placed with the same
    transform, tutorial goals, Response sub 7 with request type 5, the listed cells answered, and the changed cells
    broadcast.
  - Limits: transform determinant between 0.01 and 100; at most 2,000,000 voxels tested; the usual cell limits.
- `PlaceProps` was refactored to take a placement function, so both pastes share it.
- Sub 5 is the same in 0.589 (registry 0x0500F400, decomp_all part_0106). The 0.589 body was not re-read; an
  unreadable body is answered Failed and its cells are released.

### L52 — the prop list goes to both clients (Network/Handlers/MiscHandler.cs)
- [P, checked] The 0.589 PropBase dispatcher FUN_142f1b980 (0xED, case 10) calls FUN_142f0bc80. That is the same
  code as 0.695 FUN_142faa9b0, which the dump parses byte-exact with.
- The gate and the wrong "0.589 has no reader" comment are removed. The comment now gives the layout.

### L122 — group member records (Game/Groups.cs)
- [P, checked] 0.695 FUN_142df4460 looks up +0x10c ("WorldNameId", "InvalidWorld") and +0x104 ("ZoneName",
  "InvalidZone") through the text table DAT_145431178 vtable +0x10.
- **Change:** +0x104 = `Landscapes.Get(zone).NameText`; +0x10c = `Landscapes.WorldNameText`. +0x100 = 1 and
  +0x108 = landscape stay, for IsInZone. The doc comment was corrected.
- [P] Group bonus range: FUN_142df41b0 uses dist² < R², with R from a client setting (default DAT_144c41210 = 200).
  Added `Groups.BonusRange = 200f` and `Groups.InBonusRange(a, b)`.

### L97 / L37 — site games (Game/InstancedGames*.cs)
- [P table] IgRuleSets column 4 `HAS_FFA_TEAM_BY_DEFAULT` is 1 only for sets 2 and 7. Loaded as
  `Rules.HasFfaTeam` and copied in `RulesFor`.
- **Change:** the editor adds the `InstancedGames_PvPAllowed = 1` setting only for those sets (2, 7), no longer
  for 5 All Scoring.
- The server's own PvP default without a setting-11 row now follows the same column (`Bool(11) ?? HasFfaTeam`). Set
  5 is therefore cooperative and listed as PvE [I: keeps the server consistent with what the client then shows].
- L37: the +0x15c team count was already sent (`WriteInfo`, 0.695 only). The comment now cites the walk-on fill
  order and the DEFAULT_TEAM_COUNT values, and notes that the game's own (settings-changed) count is what is sent.

### L123 — Player Studio follow (Game/Commerce.cs; CommerceServer)
- [P, checked] Requests: FollowCreator (wire 0x35 / internal 0x34, FUN_14324d270 → writer FUN_1432347f0) and
  UnFollowCreator (wire 0x36 / 0x35, FUN_1432706c0 → FUN_143235af0). Both are {u64 creator id} on channel 1.
- [P, checked] Replies:
  - AddFollowing (wire 0x3A / 0x39, reader FUN_14322da80): {u64 id, bool result, string name};
  - RemoveFollowing (wire 0x3B / 0x3A, reader FUN_14322de50): {u64 id, bool result}.
- [P, checked] AccountStore followings (FUN_1432333c0) are u32 n × {u64 id, string name}. Before, Commerce.cs
  described them as "strings".
- **In-process:** the followings are kept in memory per follower character. The name comes from the character
  database. Following yourself or id 0 gives `false`. AccountStore lists them.
- **Commerce service:** a new `following` table (CREATE TABLE IF NOT EXISTS, like the other tables) keyed by
  follower world + guid + creator. The creator's name is the owner name of their newest product on the realm.
  ProductService answers both requests, and AccountStore lists the followings.
- [I] The creator id is a product owner's character guid. No shipped script calls the Lua natives
  InGamePurchaseStoreScreen.FollowCreator / UnFollowCreator (luabind_695 rows 968 / 969), so the id's source is not
  traced.

### L144 — grief equipment lock (Game/Grief.cs, Game/Abilities.cs)
- [P, checked] FUN_142e93a40 is called by the weapon-use checks FUN_143335b30 / FUN_1433360e0 / FUN_1433369c0 on
  the held weapon's current fire mode. At grief level ≥ 4 it allows only fire modes whose definition flag **0x10**
  is set.
- [I] Flag 0x10 = FireModes `GRIEF_IMMUNE`, by elimination: the 0x40 test beside it is the sprint-fire check.
- 0.695's FireModes.txt has one row (id 1) with GRIEF_IMMUNE 0, so **every weapon attack is blocked** on the client
  at level ≥ 4.
- [P] The "Knife" / "QuickUse" checks (FUN_142e932b0 / FUN_142e93380) also need a level below 4.
- [I] Those slot names are in no 0.695 table (only the UI text UI.Knife), so they never resolve in Landmark.
- **Change:** `Grief.EquipmentLocked` (level ≥ `EquipmentLockLevel` 4). `Abilities.HandleCast` refuses
  (CastFail) the held-weapon attack path and the combat-gear ability path while locked. Tools, consumables and
  movement abilities are untouched.

### L65 (read, nothing to build)
The sweep shows the Lua `OnAddChatChannel` / `OnRemoveChatChannel` are never fired by native code. Public channel
tabs must come from RoomInfoNotification, so no server change follows from it here.

---

## 2. Open leads (not settled; for OPEN-LEADS.md)

- **L77:**
  - The meaning of the three u8 at +0x85 (clipboard +0x12c..+0x12e) and the last u8 (+0x110, clipboard +0x12f).
  - What sets the paste cursor's off-grid flag +0x2e8, which decides whether retail can send sub 5 at all.
  - The client's own off-grid voxel placement rule. The server resamples nearest-voxel [I]; compare with a live
    capture or with FUN_143160cf0, called right after the send. That function is not decompiled in detail yet.
  - The 0.589 body layout (not re-read).
- **L61:** with no login service and no world-channel service, a zone that is gone still means "not ok" or no
  answer. Only the gateway / login service path can save that case. Test the gateway path with a stopped zone.
- **L106:** the setting hash −0x7e31ddd1 that can override the 8.0 rate is not in StringHashToValue (unchanged).
- **L123:**
  - Where the store screen gets the creator id: no shipped caller of FollowCreator was found in Resources/Scripts,
    UI/*, re/as3_695 or the 0.695 UI Lua.
  - The Lua events OnCommerceAccountAddFollowing / RemoveFollowing are not defined in any shipped script (L56).
  - In-process followings are lost at a restart. Only the commerce service persists them.
- **L144:**
  - Confirm flag 0x10 = GRIEF_IMMUNE in the FireModes loader. FUN_143363b40 / FUN_143667f90 / FUN_143bbfba0 are
    not in decomp_all_695.
  - Find the table behind the named-slot map DAT_1454313d0 (game +0x34158: "Knife", "QuickUse", "ForwardGrip").
  - What earns grief points (unchanged).
- **L145:** the meaning of the path query's u8 flag (cursor +0x268, toggled by FUN_14379b790). It is logged only.
- **L122:**
  - Other server rules that share things "with nearby group members" (RewardChests `GroupRange` 50 m, LootChests)
    could use `Groups.BonusRange`. That is not changed: those files belong to the loot / props work.
  - Whether live's bonus range was the client default 200 or a server value [I].
- **L97:** whether live forced PvPAllowed or used another path (unchanged).
- **L52:** the two floats, +0x58 and the 0/1/2 flag of the prop entries (unchanged). A 0.589 login test is needed:
  the list now goes to 0.589 clients too.

---

## 3. Test steps (dev world on `client-0.695`; client tests with the 0.695 test copy <0.695 test copy>)

Build the dev image from this branch and restart the dev world (**this restarts the dev world**). Log in with
`re/dlogin.sh` as a character listed in `Admins` (ADMIN_ACCOUNT). Write dev commands to
`server-data/worlds/dev/dev_commands.txt`, one line each (`@Name command` for one player). Watch
`server-data/worlds/dev/console.log`.

1. **/runspeed (L105):**
   - Type `/runspeed 30` and check that you run fast. Then type `/runspeed default`.
   - Expected: chat "Run speed reset to default (12 m/s)", normal speed, and the log line `/runspeed default`.
     Before the fix the admin could not move.
2. **/clienttime (L106):**
   - `/clienttime hour 22`: the sky turns to night (your client only).
   - `/clienttime sync`: the sky goes back to the world time at once (GameTimeSync is sent after the echo).
   - `/clienttime warp 200`: the sky moves fast, until the next time sync.
3. **Path query (L145):**
   - `pathquery 20 0 20` (dev). Expected: a chat line "Path query (Surface, landscape …): straight line …; path
     reached: n waypoints …".
   - In a cavern, the same command reports "Cave".
   - As a non-admin, the command is refused and logged ("without being an admin").
4. **Monitor requests (L4):** only a modified client sends them. Check that nothing breaks; a Frida page hook
   (L3) would show the log lines.
5. **Relogin (L61):**
   - Normal Escape → Logout to character select: you return to character select as before.
   - Split stack (`GATEWAY=1 up.sh`) with the world-channel container stopped: logout still reaches character
     select. The zone answers through the gateway's fallback to the zone.
   - Stop the login service as well: logout either reaches character select (zone path) or the client shuts down
     at once with "not ok", not after 60 s.
   - Optional: stop the zone container while in the world, wait about 10 s, then press Logout. The gateway answers
     with a login-service session and you land on character select (log: "answered by the gateway").
6. **Off-grid paste (L77):**
   - On your own build site, select a few blocks with the Select tool and press Ctrl+C.
   - Then `offgridpaste 30` (dev). Expected: the copy appears 2 m in front of you, turned 30°, materials charged,
     and Ctrl+Z undoes it.
   - `offgridpaste 45 <design guid>` places a design.
   - If the retail cursor ever sends sub 5, the log shows "pasted … voxels off-grid".
7. **Prop list on 0.589 (L52):** log in with the 0.589 Steam client. Loading must finish with no reader error, and
   props must show their models. (0.589 is frozen per memory; do this only when the user allows a 0.589 test.)
8. **Party frame (L122):**
   - Two characters in a group (`re/dlogin.sh` twice, or the two-world stack).
   - The party frame shows the landscape name (e.g. the continent name), not "InvalidZone".
   - The world name shows where the frame displays it.
   - Members within 200 m show "in bonus range".
9. **Site games (L97 / L37):**
   - Place a Game Table with All Scoring (5) and open its editor (`gameedit`). There is no PvP Allowed setting,
     and `game` shows it as cooperative.
   - With Deathmatch (2) or FFA Zone Control (7), players can hit each other (PvP Allowed = 1 sent).
   - In a team game (3, 4 or 6), the walk-on window offers "Switch Team" to a player in the game.
10. **Follow (L123):** no retail UI calls FollowCreator. With a Frida call of the Lua native
    `InGamePurchaseStoreScreen.FollowCreator(<creator guid>)`, expect the log line "follows creator …: True". After
    relogging (commerce service on), AccountStore carries the creator (client log "AccountStore received!
    Followings=1").
11. **Grief lock (L144):**
    - `grief level 4` (dev): chat "You have entered grief equipment lock". The weapon swing is refused (client and
      server log "refused, grief level 4"). Tools (pick, sickle) and potions still work.
    - `grief reset`: weapons work again.
