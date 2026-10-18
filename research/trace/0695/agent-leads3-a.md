# Open leads, third pass — group A (architecture, gateway, terrain service, admin monitor, commerce / world channel), 2026-10-17

Read-only pass over L3, L4, L8, L48, L50, L53, L56, L62, L63, L64, L68, L69, L70, L72, L73, L74, L75, L76, L79, L80,
L81, L82, L83, L114, L115, L122, L123, L124, L125, L126, L127, L128, L132, L133. Only paths not recorded by the sweep
(agent-leads-sweep.md), pass 2 (agent-leads2-a / b / c / d1 / d2.md) or agent-e-leads.md were tried. Nothing was
edited except this file; no game, Frida, Docker, git or web.

Sources: re/decomp_all_695 (grep + re/tools/fn.py, refscan.py on the read-only <0.695 test copy>/Landmark64.exe),
re/tools/luadis.py on <0.695 test copy>/UI/ScriptsBase.bin (output kept in the session temp dir only), the 0.695 install's
UI/Default *.lua / *.xml and Resources/*.txt tables, re/out (rtti_695.tsv, luabind_695.tsv, lua_all.txt, strings.tsv,
text_695.tsv, launchpad/), re/as3 and re/as3_695, the server code (read only).

Marks: **[P]** proven (address / file:line / table row). **[I]** inferred (reason given).

**New search spaces this pass** (none had been used for these leads before): the client's data-table Lua columns
(RadialMenuButtons.txt LUA_CALL), the compiled UI Lua disassembly (luadis.py: listener → handler → window calls), the
config object's key sections (which keys are launcher arguments), the monitor object's own vtable and every load of its
global, the VoxelCellJobQueueHandler job classes in RTTI.

---

## 1. Summary

| Lead | Verdict | One line |
|---|---|---|
| **L64** | **FOUND (UI path) [P]** | The only guild "Send Mail" in 0.695 is the guild roster radial menu (RadialMenuButtons row 48, menu type 6, text 564058) → `Guild.OpenComposeWindowForTarget(memberGuid)` → GameEvents:OnComposeGuildMail(member name) → HudStateManager:SendGuildEmailMessage → compose window with **To = that member's name**. No guild-wide compose exists |
| **L50** | **FOUND (source) [P]** | `GatewayId` is read from the client's main config object with no section, the same place as the launcher arguments `SessionId`, `Ticket`, `Server`, `StationId`, `CasSessionId`; that object parses the command line (`key=value` words) before and after ClientConfig.ini / LocalConfig.ini. So it is a launch argument (`GatewayId=N`), not only an ini key. Live's launcher never passes it in any file we have → 0 |
| **L79** | **ADVANCED [P]** | A non-zero trailing u64 queues a client-local **VoxelFarm::VoxelCellJobQueueHandler::CellGenerateMaterialsJob** per cell (FUN_14408a770); the u64 is only a key handed back with the regenerated material data to VoxelCellBlockCacheImpl. No packet goes back. Server: keep u64 0 |
| **L62** | **ADVANCED [P]** | The windows that would start a Spice REST action (GameCode → `PromotionalScreen.RedeemCode`, StationCashPurchase → `InGamePurchaseStoreScreen.FundWalletWithPaypal / Sms`, CreditCard*) are loaded (UI/Default manifest) but **nothing opens them**: no Lua, XML, data table or native shows them. Only the in-game web pages could (L137). 0x78 {false} (built) is the right answer for good |
| **L3** | **CLOSE-BY-DESIGN [P]** | Monitor global DAT_145432880 (set in FUN_142d12360); its vtable 0x14472a8c8 slots +0x20 / +0x28 / +0x30 / +0x38 / +0x40 (next / prev / set page / toggle / set +0x342a8) are called only by FUN_142d56520 (no reference). Every other use of the global reads +0x50 (page), +0x60, +0x168, +0x186. Retail cannot show a page; decided 2026-10-15 (web admin) |
| **L4** | **CLOSE-BY-DESIGN** | Already answered (no reader, no sender in retail; agent-e-leads.md §3). Nothing client-side can show live's layouts |
| **L81 / L82** | **CLOSE-BY-DESIGN [P]** | The page-29 / page-28 replies are applied only while the monitor's current page (+0x50) is 0x1D / 0x1C (part_0107:39346, part_0066:15664), and retail can't select a page (L3). Ours (admin opt-in push) stays our design |
| **L83** | **CLOSE (FOUND negative)** | Pass 2 proved it; nothing new. Do not answer 0x4FC / 0x4FD |
| **L53** | **ADVANCED [P] → CLOSE-BY-DESIGN** | Grief 0xC0 dispatcher FUN_142ee1bf0: sub 1 SetGriefPoints, sub 2 SetGriefLevel (FUN_142ee1850, old → new level texts), **sub 3 Reset = header only (zeroes state inline)**. Method for the rest: per-family dispatcher on demand; every family is unchanged in 0.695 |
| **L74** | **ADVANCED [P] → CLOSE-BY-DESIGN (limits)** | Client side of dynamic commands fully read: source 1 → sent on channel 0, 2 → channel 1, 3 → gateway opcode 9, **any other source → nothing is sent**; a name already registered by any source is ignored ("Duplicate command from server"). Live's flood limits can't be seen from the client |
| **L122** | **ADVANCED [P] → CLOSE-BY-DESIGN** | 0.695 FUN_142df41b0 / FUN_142df4460: IsInZone needs both +0x100 and +0x108 equal to the viewer's; +0x10c is a text id (DAT_145431178 +0x10, "InvalidWorld" on a miss). Our Groups.cs already matches; group lifetime across worlds is server-internal |
| L56 | **CLOSE-BY-DESIGN [P]** | None of the five bindings sends a packet (luabind_695 "sends packet" empty); SetEyeSlotIndex only changes the character-select preview value. Whatever calls them, the server sees nothing new |
| L63 | **CLOSE-BY-DESIGN [P neg]** | The data-table Lua columns (new space) hold none of the lobby / quiz / requisition / Wall-of-Data natives |
| L8 / L48 | CLOSE-BY-DESIGN | Server-to-server messages; the client never parses them. Our internal layouts (internal-transport.md) are ours by design |
| L68 | CLOSE-BY-DESIGN | 0x4C is only a chat line (pass 2). Keep it out of the character-select hand-over; optional on a final stop |
| L69 | CLOSE-BY-DESIGN | The tracker header sizes are profiler bookkeeping in shared engine code; the client never sends or reads opcodes 7 / 8 / 0x16 |
| L70 | CLOSE-BY-DESIGN | The client only logs ChannelIsRoutable / ConnectionIsNotRoutable; holding vs dropping is the gateway's choice. Our 2026-10-16 rules stand |
| L72 | CLOSE-BY-DESIGN | processExpectedConnectionsOnly is a server-side UdpLibrary option; the client is the connecting side. Our gateway's ticket check in LoginRequest does that job |
| L73 | CLOSE-BY-DESIGN (rest) | The restart case is answered and built; zone-to-zone process hand-over is server-internal (names only in the client) |
| L75 | CLOSE-BY-DESIGN | Client constraint only: 0x10C is accepted on any channel except 2. Which back end relays is our choice (the zone today) |
| L76 | CLOSE-BY-DESIGN (a, c) / built (b) | (a) heal rules from dev posts, built; (b) ruin layers = .cpa LayerInfo kind-2 [I strong], built behind RuinLayers; (c) → L128 |
| L80 | CLOSE-BY-DESIGN (runtime optional) | Flag stored and copied, never read in the cache code (pass 2); 0 works on both clients |
| L114 | CLOSE (FOUND 2026-10-16) | No new work; B.10 built on it |
| L115 | CLOSE-BY-DESIGN | History; the server list is configurable (WORLD_NAME_ID, .env) and the 2016 five + Events are known |
| L123 | CLOSE-BY-DESIGN | Follow built; Recommendations has no reply class and no shipped caller (pass 2) |
| L124 | CLOSE-BY-DESIGN | (a) no consumer of OnReceiveServerStatus; (b) / (c) answered; (d) web pages (L137) |
| L125 | CLOSE-BY-DESIGN | Id table fits forwarding ApplyDiff unchanged (pass 2); internal layouts are ours |
| L126 | CLOSE-BY-DESIGN (use rule) | Both bool paths proven; which texts live sent is server-internal. No packet dump with 0xF4/0x1B exists (Resources/PacketDumps: 8 files, none VoxelConstruction). Text ids beside 562155 are unrelated (ids are allocated by date) |
| L127 / L128 | CLOSE-BY-DESIGN | FUN_143c74ef0 has exactly one caller (the page-33 drawer FUN_14320ea00, refscan: one call 0x14320ed09): no other user of the job-type enum. Counters are server-internal |
| L132 / L133 | CLOSE-BY-DESIGN | Navmesh is server-only (no class, no library, no reader). Players' 2016 reports (pass 2) are the spec; design proposal below |

---

## 2. Per lead

### L64 — guild mail: FOUND (the client's only compose path) [P]

New search space: the data tables' Lua columns and the compiled hud Lua.

1. **Radial menu [P].** Resources/RadialMenuButtons.txt (0.695; columns ID, MENU_TYPE_ID, BUTTON, STRING_ID,
   IMAGE_SET_ID, LUA_CALL …), menu type 6 = the guild roster member menu:
   - 42 Guild.PromoteMember (564051), 44 Guild.Kick (564052), 46 Guild.DemoteMember (564054),
     47 Groups.GroupInviteByGuid (564053), **48 `Guild.OpenComposeWindowForTarget`, text 564058 "Send Mail"**,
     49 Ui.IgnorePlayerByGuid, 50 Ui.StopIgnoringPlayerByGuid, 52 Guild.StartChatToMember (564061 "Send Tell"),
     68 Ui.TeleportToFriend (576079).
2. **The native [P].** `Guild.OpenComposeWindowForTarget` = FUN_1435b9990 (luabind_695 0x1447a46e0; 0.589
   FUN_1435061a0, lua_all.txt:88). It reads argument 1 as a guid (FUN_143c9f460: string → u64), walks the guild list
   (DAT_145431a20 +0x56d90, selected index +0x56dc8), finds the member by guid (FUN_143c4ebb0: hash at guild +0x2bc8,
   key +0x40) and calls Lua `GameEvents:OnComposeGuildMail(member +8 = name)` (part_0112:8505).
3. **The Lua [P, luadis on ScriptsBase.bin].** `GameEvents.OnComposeGuildMail` dispatches EVENT_COMPOSE_GUILD_MAIL;
   HudStateManager listens with `SendGuildEmailMessage(self, name)` (function at script line 3842), which closes any
   open compose window, creates EmailComposeWindow in write mode on GameData.EmailMessageData.Compose,
   `Mail.OnOpenComposeWindow()`, `ASInvoke('SetReplyMode', true)`, `ASInvoke('SetState', 'write')`,
   **`ASInvoke('SetTo', Ui.MakeHtmlSafe(name))`** (constant table at file offset 279417: … SetReplyMode, SetState, SetTo,
   MakeHtmlSafe …). The player then sends an ordinary ComposeRequest.
4. So a "guild mail" from the 0.695 UI is **a normal mail to one guild member by name**. No UI composes to a whole
   guild [P for the shipped UI].
5. What stays [I]: received mail kind 0x100 ("[Guild] [*GuildName*]", guild id in MailData's second u64) and result 0x11
   / text 564049 ("…before you can send mail to one") still show live had guild-wide mail; with no client compose for
   it, the only senders are the server itself (guild notices) or a typed `To` naming the guild (pass 2's rule).

Paths with no result: as3_695/GuildWindow (no mail call; only Disband / Close / Quit / sounds), every UI/Default lua /
xml (no caller of the native), ScriptsBase.bin (only the listener chain above).

### L50 — GatewayId: FOUND (where the value comes from) [P]

- The CharacterLoginRequest builder (0.695 part_0084:2357) reads `FUN_142b21540(config +0x32750, "", "GatewayId", 0)`.
- The same object and the same empty section hold exactly the launcher-supplied keys: `CasSessionId`, `Country`,
  `Environment`, `Inifile`, `LiveGamer`, `LoadingScreenId`, `Region`, `Server`, `SessionId`,
  `SpecialLoadingScreenId`, `StationId`, `Ticket` (grep of every `0x32750),&DAT_144c40a60,"…"` read).
- The object is built in FUN_142ce9580 (part_0065:23415-23441): `FUN_142b539e0(cfg, param_2)` (a parser of
  space-separated `key=value` words, FUN_142b539e0: splits on '=' 0x3d and ' ' 0x20), then `Inifile` / ClientConfig.ini
  is loaded, the command line is applied **again** (so it overrides the file), then LocalConfig.ini is merged.
- Our launchers start the client with the same syntax: LaunchPad/Game.cs:101 and Launcher/MainWindow.xaml.cs:90
  (`SessionId=… CasSessionId=0 STEAM_ENABLED=0`).
- So `GatewayId=N` on the command line (or a top-level `GatewayId=` in ClientConfig.ini / LocalConfig.ini) reaches the
  login server [P]. Live's launcher builds its arguments from data we don't have (lp2016.txt has no GatewayId besides the
  unrelated `NetworkGatewayId` fingerprint, pass 2), so for every live player we know of it was 0 [I].
- Meaning on live: still not provable from the client (the login server's use). Our design is free.

### L79 — MaterialChangeInfo trailing u64: ADVANCED [P]

- FUN_14401ecf0 → FUN_14408d780 allocates 0xA0 bytes and builds **FUN_14408a770 =
  VoxelFarm::VoxelCellJobQueueHandler::CellGenerateMaterialsJob** (RTTI 0x1449b12a0): +0x78 queue handler, +0x80
  cell id, +0x88 the third argument, +0x90 the u64.
- Execute FUN_14408c1c0: loads the cell from the cache and generates a 0x21380-byte material map (FUN_144093a00 /
  FUN_144093e60).
- Completion FUN_14408d1d0: calls the queue handler's owner (+0x78 → +8, the VoxelCellBlockCacheImpl built in
  FUN_14400e890) at vtable +0x18 with (materials, u64).
- So u64 ≠ 0 = "regenerate the material data of these cells locally"; the u64 is just a correlation key. No packet goes
  back to the server [P within the chain]. The u16 is still unread (pass 2).
- Server: nothing to send. If ever needed, a MaterialChangeInfo with u64 = any non-zero key makes the client recompute
  materials from its cached cells (useful only if the client's material inputs changed, e.g. vein / material tables)
  [I].

### L62 — commerce session: ADVANCED [P]

- Every Spice REST builder in part_0133 (e.g. `redeemcode.action`, line ~46986) returns an invalid id at once when the
  REST object's +0x30 byte is 0 (not configured), otherwise asks the session through interface +0x78 (the 0x77
  sender, pass 2).
- The Lua natives that start these actions (luabind_695): InGamePurchaseStoreScreen.FundWalletWithCreditCard /
  Paypal / Sms / External, PreviewFundWallet…, Purchase/PreviewSubscriptionWithStationCash, OnFundWalletScreenOpen;
  PromotionalScreen.RedeemCode / IsCodeRedemptionEnabled.
- Their only shipped callers are UI/Default/GameCode.lua (`SubmitGameCode` → RedeemCode), StationCashPurchase.lua
  (`GotoPaymentOption` → FundWalletWithPaypal / Sms), CreditCardSelection.lua / CreditCardEntryForm.lua. These windows
  are in the manifest, but **nothing creates or shows them**: no Lua / XML reference besides their own files and
  `Client.HandlerPromotions = "GameCode"` (used only by the redemption result callback FUN_142fd74e0, called from
  FUN_142fd67e0 / FUN_142fd7810 "OnCodeRedemption"), no Resources table, no string in the exe naming the windows.
- So in the retail 0.695 client a 0x77 can only come from the in-game web pages calling these natives (L137) [I].
  0x78 {false, ""} (built, ef483fb) ends any such request at once. Nothing else to build; the lead can close.

### L3 / L81 / L82 — monitor pages: CLOSE-BY-DESIGN [P]

- Monitor object: BaseClientPerformanceMonitor, vtable 0x14472a8c8 (rtti_695.tsv): +0x18 update FUN_143226700, +0x20
  next page FUN_143226660, +0x28 previous FUN_143226690, +0x30 set page FUN_143226630, +0x38 toggle FUN_1432266e0,
  +0x40 set FUN_143226620, +0x48 enabled check, +0x50 current page.
- refscan: those slots are referenced only by the vtable (0x14472a8e8 … 0x14472a908); FUN_14320a560 (page index) only
  by them.
- The global DAT_145432880 (assigned in FUN_142d12360 line 592) is loaded 217 times; the only vtable calls through it
  (direct or through a local copy) are +0x40 / +0x30 in FUN_142d56520 (refscan NONE), +0x50 (page query) and +0x60.
  The only other writer of +0x342a8 (part_0063:22319) is an unrelated struct (a bounding-box copy).
- Page 28 / 29 replies are copied only when +0x50 returns 0x1C / 0x1D. So in retail they are always dropped.
- These are the facts that close L3 / L81 / L82 for the retail client. The live content of pages 28 / 29 is server
  data; ours (admin-pages.md §11) is by design. A dev-only Frida page hook (below) can still show our replies render.

### L53 — Grief family [P]

FUN_142ee1bf0 (0.695 Grief 0xC0 dispatcher, part_0075:12519): byte at offset 2 = sub; 1 → FUN_142ee1d80
(Grief::GriefPacketSetGriefPoints: u32, i8, i32 points, i32; prints "You have %d grief points."), 2 → FUN_142ee1ce0 →
FUN_142ee1850 (SetGriefLevel: level texts "entered / left grief equipment lock / suspension", levels 1-3 warnings),
3 → inline reset of the state (**GriefPacketReset, header only**). Our Grief.cs already uses this handler. The
remaining ~80 classes need the same per-family read only when the server has to send one; all their dispatchers
compare equal 0.589 vs 0.695 (coverage-695.md §9), so nothing is unknown about 0.695 behaviour. Proposal: close as a
documentation lead; re-open per family when a server feature needs a class.

### L74 — dynamic command lists [P]

- FUN_1433bad50 (CommandList handler): logs "command list from %s with %d entries" with "zone" when source == 1, else
  "world"; per entry (stride 0x18, non-empty name only) registers a ServerCommand unless the name already exists
  ("Duplicate command from server, ignored: %s"), passing the source to FUN_142dd7560.
- Execution = ServerCommand vtable 0x14475f158 slot 0, FUN_1433c3d80 (part_0102:8383; its only reference is that
  vtable) with the source as third argument [P for the branches; I that the dispatcher passes the stored source, as
  FUN_142dd7560 registers it]: source 1 → FUN_14338cc90(…, channel 0), 2 → channel 1, **3 → FUN_14337d580 (gateway
  opcode 9)**, anything else → logged "Execution: %s" and **not sent**.
- Server constraints: (1) a name must be unique across zone, world and gateway lists: the first list that names it
  wins for the whole session; (2) source must be 1 / 2 / 3. Our zone (Emotes.cs:120, source 1), world commands
  (DynamicCommands) and gateway (GatewaySession.cs:151, source 3, "gatewayprofiler") comply.
- Live's gateway list beyond `/gatewayprofiler` and its flood limits are not client-visible: CLOSE-BY-DESIGN.

### L122 — group member record [P]

0.695 FUN_142df41b0 (part_0070 ~10260): IsInZone = (+0x100 equal) **and** (+0x108 equal), then Distance and
InGroupBonusRange (range DAT_144c41210 or the client setting). FUN_142df4460: WorldNameId = text lookup of +0x10c
(DAT_145431178 vtable +0x10, "InvalidWorld" when missing); IsLinkdead = +0xf8 ≠ DAT_14543cd00 (no writer = 0).
Groups.cs:544-547 and GroupService HideLocation already follow this. Whether a live group survived logout / world
change cannot be shown by the client.

### L56 / L63 — callers [P neg]

- New space: Resources/*.txt Lua columns. RadialMenuButtons.txt / ModalToolBrushOptionDefinitions.txt list 60 Lua
  calls (Area.*, Chat.*, Groups.*, Guild.*, Prop.*, Tool.*, Ui.*, Voxel.*); none of the L56 or L63 natives.
- L56: none of the five bindings sends a packet (luabind_695.tsv "sends packet" column empty for 0x1447ad870,
  0x1447ad8e0, 0x1447bad80, 0x1447bada0, 0x1447a7520 / 30). SetEyeSlotIndex FUN_1435da360 writes the character-select
  preview value (+0x32490 +0x20, key DAT_1447add44); GetEyeSlotCount reads it. Server-neutral → CLOSE-BY-DESIGN.
- A full-text grep of the whole 0.695 install's Resources, CommonData and UI folders (every file, binary included) for
  GetEyeSlotCount / SetEyeSlotIndex / GetMeshesFilteredByAvailability / Mesh.Populate / Ui.Get/SetAmbientVolume found
  **no file** [P neg].

### Leads closed by design (reasoning)

- **L4, L8 / L48, L69, L72, L125:** server-to-server data. The client contains only names (id registries), profiler
  header sizes or nothing; it never parses these messages, so nothing on the client side can prove more and nothing
  constrains our layouts beyond what is already in internal-transport.md.
- **L70, L73 (rest), L75:** the client only logs routability, keeps one gateway connection for every re-zone, and takes
  0x10C on channels 0 / 1 / 3. The server's choices (hold 256 / re-zone on restart / zone relays 0x10C) satisfy these.
- **L68:** 0x4C = one chat line; never during the hand-over (would print "logged you out" while the client continues).
- **L76, L127, L128:** Environment Server internals. The page's record formats are proven (Pending / Active columns,
  ruin layers by index); the job-type enum has no other client user; ruin placement inputs (LayerInfo, manual
  templates) are server-side data by proof (L47: the client never uses the manual templates).
- **L80:** never read; 0 works.
- **L115, L123, L124:** history / web / no consumer; our settings cover them.
- **L126:** both client paths are known; picking texts is our design (rule below).
- **L132 / L133:** server-only navmesh; behaviour spec from 2016 reports.

---

## 3. Server work

1. **L64 — guild "Send Mail" (no code change required; check one thing).** The roster's Send Mail sends an ordinary
   ComposeRequest with `To` = the member's character name. Check that WorldServer/Game/Mail.cs / SocialServer
   MailService.cs deliver a mail to a guild member **on another world** by name (P4905-P4907 cross-world social). Keep
   the [I] rule "To = the sender's own guild name → kind 0x100 to every member, result 0x11 when not in a guild" as an
   optional extra; document that the client has no compose for it.
2. **L50 — GatewayId.** AuthServer/Network/Message/Model/CharacterLoginRequest.cs:16 comment: "ClientConfig.ini
   GatewayId" → "launch argument `GatewayId=N` (also a top-level ClientConfig.ini / LocalConfig.ini key; command line
   wins), read with the launcher's SessionId / Ticket / Server keys (0.695 part_0084:2357, config object FUN_142ce9580)".
   Optional [I]: in the two-world stack, AuthServer LoginHandler.cs:500 may honour a non-zero GatewayId as "prefer this
   gateway / world" (and LaunchPad could add `GatewayId=` per world entry); default stays "one gateway per world,
   ignore".
3. **L74 — command lists.** Add a startup check (or unit test) that the zone list (Emotes.SendCommandList), the world
   list (DynamicCommands) and the gateway list (GatewaySession.GatewayCommands) share no name: the client keeps the
   first and ignores later duplicates for the whole session (FUN_1433bad50), so a duplicate silently routes to the wrong
   back end. Never send a list with a source other than 1 / 2 / 3 (the client registers it but sends nothing).
4. **L62 — none.** 0x78 {false, ""} stays. Note in docs/commerce.md: the payment / redeem windows are unreachable in the
   shipped 0.695 UI; only web pages could start them.
5. **L79 — none.** Keep not sending 0xEC/6; if sent, u16 0 and u64 0 (u64 ≠ 0 only triggers client-local material
   regeneration).
6. **L126 — use rule (optional, unchanged from pass 2).** Use ZoneDisplayNotification (bool 1 → system message) for
   bare-text Environment Server refusals; keep 562155 for "Environment Server unavailable".
7. **L133 — design (unchanged direction, now the closing spec).** EnvServer pushes a coarse per-cell solidity summary
   (1.2 m blocks) with CellVersionsChanged; Game/Pathing drops and rebuilds nav tiles of changed cells lazily near
   players; prop footprints (Game/ModelFootprints) block columns on build sites (P5781); a material-only change also
   invalidates the tile (forum 56713).

## 4. Runtime tests wanted (optional; all close-by-design leads stand without them)

- **L3 / L81 / L82 page proof (dev-only, 0.695 test copy <0.695 test copy>):** Frida: read the pointer at DAT_145432880,
  call FUN_143226620(mon, 1) then FUN_143226630(mon, 28) (and 29) while an admin-opt-in push is active; expect the
  page-28 tree / page-29 key list to draw. Detach only after the client is idle (Frida detach crash rule).
- **L80:** Frida read-watch on +4 of one manifest entry (impl +0x150 map) during a zone load; expect no read.
- **L79:** send one 0xEC/6 {u16 0, n = 1, cell, box, bitset, u64 1} on the test client; hook FUN_14408c1c0 /
  FUN_14408d1d0 to see the job run; expect no outgoing packet and no visible change.
- **L64:** in the guild window, right-click a member → Send Mail; expect the compose window with To = their name, and
  our server to deliver it (also cross-world with the two-world stack).

## 5. Hand-offs

- Group B (client systems): RadialMenuButtons.txt is a Lua-call source table no lead search used before (0.695 file
  43 lines vs 41 in gameData 0.589; the two new rows were not identified this pass). Its LUA_CALL column can answer
  "who calls native X" questions for other groups too.
- Group B: FUN_1435b9990's neighbour Lua native (part_0112 ~8450) builds HudPackets::PacketRadialMenuRequest (fields
  0xDC, 1, a guid, 4 [I: base / sub / target / menu]) on channel 0; the server side is Game/RightClickMenus.cs — only a
  cross-check that the menu type it asks for is answered.
- L137 owner: the only possible 0x77 / Spice REST starters are the Marketplace web pages' calls into the
  InGamePurchaseStoreScreen / PromotionalScreen natives.
