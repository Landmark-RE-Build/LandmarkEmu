# Open leads, second pass, group D1 (architecture, server behaviour, data files), 2026-10-17

Read-only pass over L1, L2, L3, L4, L7, L8, L47, L48, L49, L50, L59, L60, L62, L63, L64, L65, L66, L67, L68, L69, L70,
L72, L73, L74, L75 and L76. It goes deeper than the morning sweep (agent-leads-sweep.md).

Sources: the 0.695 decompile (re/decomp_all_695), the read-only exe <0.695 test copy>/Landmark64.exe through
re/tools/refscan.py, the 0.695 data files (<0.695 test copy>/CommonData), gameData695, re/as3 / re/as3_695, and
design/forum. Scratch scripts are in the scratchpad `d1/`:
- cavvfm.py, match.py, shift.py, cavtypes.py: compare .cav with .vfm;
- cpa_l47.py, cavhdr.py: parse the .cpa sections.

Marks: **[P]** proven (address, file or data offset given). **[I]** inferred (reason given).

## Summary

| Lead | Verdict | One line |
|---|---|---|
| L7 | **FOUND** | All 8 remaining send sites resolved: 7 on Zone, and 1 that picks World or Zone; 3 of them are dead |
| L47 | **ADVANCED (major)** | The client never uses the manual templates (their only collector has no reference), so the server must stamp them. LayerInfo, CavernInstanceData and the manual-template record decoded across all .cpa files |
| L49 | **FOUND** | .vfm planar submeshes = the .cav connection records (92 of 92 match). The 36-index box = the tile's frame (.cav = .vfm shifted so that box is centred) |
| L60 | **FOUND** (confirmation) | PostQueueNotify uses only its text id. The queued pair is the client's own last TeleportRequest {world, zone} |
| L62 | **ADVANCED** | Commerce session = ticket for 12 Spice REST actions (`https://<host>/rest/commerce/<id>/<action>`). The SpiceWebAuthUrl senders are dead |
| L64 | ADVANCED | No guild field and no guild compose UI. Text 564049 implies a guild named as the recipient [I] |
| L65 | **FOUND** (by agent-chat.md) | General = public id 0, the only public channel in 0.695; the client sets it up itself |
| L66 | **FOUND** (most) | Friend row columns, FriendStatus / ProfileInfo layouts, and the 10 s offline debounce proven |
| L67 | **FOUND** (b, c, d) + **server bug** | NewMailInboxNotification needs a u16 count, or the 0.695 client drops it |
| L68 | ADVANCED | 0x4C only prints a chat line (no state change). Live's countdown text came from 0x5F (forum 2014) |
| L59 | **FOUND** (negative) | ClientLeaderboardManager has only a destructor: no reader, no caller |
| L63 | ADVANCED | The match-invitation and mini-game-invite response senders are dead in 0.695 |
| L69 | ADVANCED | The tracker's header table is now read: external 5 / 6 = 1-byte headers; internal 7 = 19 B, 8 = 13 B, 0x16 = 11 + 12·n (n at byte 7) |
| L76 | ADVANCED | Heal rules from the dev posts (already built). Ruin layers ↔ .cpa LayerInfo [I strong] |
| L8 / L48 | ADVANCED (small) | Internal tunnel header sizes proven (L69); the sibling client layouts named |
| L1, L2, L3, L4, L50, L70, L72, L73, L74, L75 | UNCHANGED | Notes below |

---

## L7 — the 8 remaining send sites: FOUND [P]

All 8 sites are resolved. The send helper's third argument is the channel (0 = Zone, 1 = World).

| Site (0.695) | Packet | Channel | Notes |
|---|---|---|---|
| FUN_142e36f70 | ActivityPacketJoinActivityRequest 0xA0/1 (send helper FUN_142e38410, writer FUN_142e37100: u32 +0x20, u32 +0x24) | World if arg 1, Zone if arg 2 | The wrapper itself has no reference (refscan NONE). The live sender FUN_142e40680 (part_0072:2795) picks World when activity +0x1c == 1 and Zone when it is 2 |
| FUN_142d4ab90 | PlayerUpdatePacketCameraUpdate (ctor part_0060:32024, opcode 0x80 at +0x96998; writer FUN_142c155f0) | Zone | Sent only while the debug BoolCommand `send_camera_updates` (DAT_145430b13, part_0029:20202) is on, so a retail client does not send it [I: default false] |
| FUN_14315f270 | OffGridPasteClientRequest (sweep / L77) | Zone | — |
| FUN_143165810 | VoxelConstructionPacketApplyDiffRequest 0xF4/1 | Zone | ctor FUN_14315dfc0; writer FUN_14315acb0 writes +0x90 map, +0xc8 CellIdAndPermanentVersion array, +0xe0, +0xe4, +0xf0 ConstructionArea array, +0x108 bool. Matches the ctor field for field |
| FUN_14330e300 / FUN_14330e370 | BaseMiniGameAdminPacket 0x62 (writer FUN_14330e4d0) / MiniGameFlashCommandAdminPacket 0x62/0x2F | Zone | Both wrappers have no reference (refscan NONE) |
| FUN_143329ed0 | Abilities::AbilityPacketAbilityLanding 0x27/0x1D (ctor part_0098:32791; send FUN_143322830: header + u64 +0x18, u64 +0x20) | Zone | Sent when flag +0x4c38 is set and timer +0x4c3c runs out, i.e. the landing after ability-driven movement |

- **Server change:** accept 0x27/0x1D AbilityLanding {u64, u64} on Zone. Log it or ignore it (no handler today:
  `ZoneMessageOpcode.AbilityAbilityLanding` has none). Nothing else to build.
- **Next:** none. channels-695.tsv can be updated with this table.

## L47 — the .cpa sections and the manual template placements: ADVANCED (major)

**Who reads the manual placements [P].**
- The asset (VoxelFarm::CavePlacementAsset, user type 4, created in FUN_143ff56a0) keeps
  ManuallyPlacedExportedTemplateData at +0xd0 (data +0xd8, count +0xe0, stride 0x68).
- Loaded assets go to the cave data object at core +0x53d8:
  - OnAssetReady case 4 → FUN_143fe9370 → FUN_143fe9ad0 / FUN_143fea570 (sorted by LayerInfo).
  - Getter: FUN_143ff7f70.
- The only function that reads asset +0xd8 / +0xe0 is **FUN_143ff7bc0**. It collects every manual placement of every
  loaded asset into one array (FUN_143ff54f0 appends).
- refscan: **FUN_143ff7bc0 has no call, LEA or pointer** (only EH / .pdata entries). The getter's only other caller,
  FUN_143ffebd0 (OnAssetUnavailable), does not touch them.
- **So the retail client parses the manual templates and never uses them.** They are server-side data: the server had
  to stamp them, and the client sees them only as normal terrain and props. That is what our server does for ruins.

**The "Room" record is VoxelFarm::CaveRoomObjectGap [P].**
- Layout: Box (8 × f64) plus Array<CaveRoomObjectGap::Attachment> of {i32, i32, i32} (FUN_143fa3590; copy
  FUN_143ff54f0).
- These gaps come from VoxelFarm::CaveRoomObjectGapGenerator (FUN_14409c260).
- Its Options are {f64 size x, y, z; Array<Attachment>; int count at +0x30; int mode at +0x34}.
- In FUN_14409a480, mode 0 adds a gap only if it overlaps no existing gap (octree +0x1e). Mode 1 checks two octrees.
  **Mode 2 adds without any overlap check.** That is why the 4,030 ruin slots overlap. [P for the modes; I that the
  tool ran in mode 2]
- The debug "spawn cavern" path FUN_144090a60 builds one Options entry with size = (x, y, z) × voxel size, count 1 and
  Attachment {2, 0, value}. So the slot point (2, 0, 12) seen in every .cpa is an Attachment {kind 2, 0, 12}.
- What kind 2 / 12 mean is not traced (the attachment is used in FUN_14409a480 / FUN_14409b960).

**All Underground .cpa files parsed (cpa_l47.py).** 224 and 367 fail at the version ≤ 12 arrays. The other 60 parse.

- **LayerInfo** = {kind, layer, first tile, tile count, first cavern, cavern count} [P: the tile and cavern ranges are
  contiguous and add up to the totals in every file]:
  - Old continents (v12, 325-384): one kind-3 record (layer 0, all non-cavern tiles first, 0 caverns), then **kind-2
    layers 0-4, five underground cave layers**. Each has its own tile and cavern range. For example 325: caverns
    20 / 15 / 10 / 10 / 15 = 70 ✓; tiles add up to 20,699 ✓.
  - Islands (v14, 391-416): (3, 0, 0, n, 0, ~81) and an empty (2, 0, n, 0, 0, 0). One layer holds everything.
  - Events / 432-444: a single kind-2 layer.
- **CavernInstanceData header** {u32 a, u32 b, u32 c, u32 d}:
  - d = the cavern's first tile index [P: strictly increasing and < tile count in every file].
  - a = a VoxelCavernDefinitions ID (values 2-8 only; v14 islands use 5 and 7) [I strong: b / c grow with a].
  - b / c = width / length in cavern tiles (2-10) [I]. The metre sizes do not equal the table's MIN/MAX numbers, so the
    table's unit is not metres or 64 m tiles (open).
- **ManuallyPlacedExportedTemplateData** = {u32 record version, Room(record version), u32 a, u32 b, u32 c}:
  - The first u32 **equals the file version** (12 / 13 / 14) in every record. The reader passes it to the Room parser
    as its version (FUN_143fa3b70 → FUN_143fa3590) [P]. So ruin-stamping.md §5 / agent-graveyards.md misread "first
    field 1-4": that value is **a**.
  - a = 1..4 (rotation [I]). b = 2 or 4. c = template id.
  - v14 islands: always (a, 4, **76**), one per cavern: the graveyard.
  - v12 continents: two kinds, (a, 2, **61**) and (a, 4, **75**).
  - So b is a category tied to the template: 4 = graveyard-type (75 / 76), 2 = template 61 [I]. Ids 61 / 75 / 76 are in
    no client table (as before).
- Both u32 after LayerInfo: u1 is constant per tool run (2582300881 for v12, 4174129765 for the islands: a seed or
  hash [I]). u2 is 0 in v12 and varies in v14 (open).

**Server change:**
- Correct the parser comment in ruin-stamping.md §5 / Graveyards: the record's first u32 is its version; rotation is
  the next u32.
- When stamping graveyard_launch.otd (L-d in agent-graveyards.md), use **a** as the rotation (1-4) [I] and c = 76
  (graveyard).
- The ruin layers of the Environment Server = the LayerInfo kind-2 layers (see L76).

**Next:**
- The meaning of Attachment {2, 0, 12} (FUN_14409b960 reads it).
- The unit of VoxelCavernDefinitions MIN/MAX against the cavern boxes.
- u2.
- Why 224 / 367 fail (version < 12 layout).

## L49 — .vfm submeshes and the mesh → .cav transform: FOUND [P by data]

**.cav trailing records.** The ".cav room-gap records" in docs/terrain.md are **connection records**, 54 bytes each:
`{u32 4, u32 shape hash, u32 n, f32 normal[3], f32 tangent[3], f32 centre[3], u8 flag1, f32 bottom y, u8 flag2}`.
- Hashes: 0x1970 / n 61 is the round tunnel opening. 0xaa00 / n 51 is the cavern side. 0xa0a3 / n 159 and 0xa8c4 /
  n 182 are cavern wall halves. Entrance tiles use 0x2d2a / 84, 0x3329 / 30 and 0x2cee / 79.
- flag2 = 1 only on the surface opening of entrance tiles [I: "exposed"].
- These hashes are what the generator's "connection hash" matching uses (FUN_144030970) [I].

**The .vfm submeshes:**
- Each **planar submesh** (index counts 180 / 150 / 471 / 540 / 87 / 234 / 249) is one opening face. The face's centre
  and bottom-y equal a .cav connection record's centre and bottom y.
  - **92 of 92 records match** after the shift below (shift.py). The index count follows the vertex count n
    (e.g. 471 = 3·(159−2), 540 = 3·(182−2)).
  - Graveyard01 has one extra planar 180-index face with no record (an unused opening).
- The **36-index submesh** (12 triangles) is the tile's frame box. It is missing in Enterance01-03.
- The **2-4 large submeshes** are the surface regions. Each tile's .cav LOD0 has exactly three repaint types besides
  air 0 and the dominant fill type (cavtypes.py), so they are the floor / wall / ceiling material regions [I strong:
  the counts match, and their y-ranges are floor-only, ceiling-only and full].

**Transform [P, 92 / 92].**
- Same axes and units (metres; .cav voxel = 0.4 m, no axis swap).
- The .cav frame is the .vfm frame shifted so that the 36-index box is centred on the origin (shift = −centre, rounded
  to 0.4 m).
  - Most tiles need no shift.
  - CavernWall04 needs (0, 0, +6.4).
  - Connection013 needs (−3.2, 0, +6.4).
  - Hub01 needs (0, 0, −3.2).
- The .cav box lies inside that shifted box. It is trimmed by up to 12.8 m on sides with no opening and is flush at
  openings [I].

**Server change:** none needed for terrain. Optional: CavePieces can read the connection records (centre, normal,
bottom y, flags) to know cave openings and entrances exactly. That is useful for L51's "cave beginning location" and
for arrival points.

## L60 — teleport queue: FOUND (confirms the E.8 build) [P]

- PostQueueNotify handler 0.695 **FUN_1431726c0** (part_0088:7931). Reader FUN_14316daa0: u8 sub, then u32 +0x18,
  u32 +0x1c, u32 +0x20, u32 +0x24.
  - It uses only the text: +0x24 if non-zero, else +0x1c.
  - It calls Lua `GameEvents:OnPlayerTeleportPostQueueNotify(text)`. With no Lua answer it calls FUN_1433db960 on
    UIProcessor +0x18. That runs FUN_1433d8c20, which **creates GaqItemTeleportToZoneRequest ("GAQTeleportToZone")**
    with that text.
  - +0x18 and +0x20 are never read.
- The pair sent back by Accept (FUN_1433fcec0) and by Lua Teleport.TeleportToQueuedLocation (FUN_1435e8080 →
  FUN_143173a90; both on channel 1, World) is teleport manager +0x30 / +0x34.
  - Those are written **only by FUN_143173750**, the TeleportRequest 0xFC/4 sender (Lua Teleport.TeleportToLocation;
    TeleportSpireWidget passes world GUID, zone GUID).
  - So the queued location = the client's own last {world, zone} request. PostQueueNotify does not carry it.
- Server: TravelQueue.cs already matches (u8 sub, 0, text, 0, 0). No change. The OPEN-LEADS "(to confirm)" is
  confirmed.

## L62 — commerce session ticket: ADVANCED [P]

- Sender 0.695 **FUN_142d3bac0** builds PacketCommerceSessionRequest 0x77 on channel 1 (World).
- Its only reference is the jmp in FUN_143649720. That is slot 15 (+0x78) of ClientInGamePurchaseManager's second
  vtable (RTTI 14472ec00, the interface at manager +8).
- Every Spice REST request builder in part_0133 (e.g. FUN_143a1fa90) does the same:
  1. stores that interface in the pending request;
  2. formats `https://%s/rest/commerce/%d/%s` (host helper +0x2b0, id +0x34);
  3. calls interface +0x78 to ask for a session ("Failed to send session ticket request." if that fails).
- There are 12 actions: addcreditcard, creditcardtypes, deletepaymentsource, finalizesteamtransaction, fundwallet,
  issteamcustomer, paymentsources, purchasepreview, fundwalletpreview, purchase, redeemcode, updatecreditcard.
  - Example caller: FundWalletWithCreditCard (part_0093 FUN_14324ea80).
  - Also `authenticated/link/wallet/fundWalletWithMOP.action` (part_0134:5394).
- The reply 0x78 PacketCommerceSessionResponse (reader FUN_142c4f100: bool ok +0x10, string ticket +0x18) goes to
  zone case 0x35 → FUN_143a2a410:
  - ok: the request gets form field `sessionID` = ticket and is POSTed;
  - not ok: "Failed to request commerce session." and the request's callback gets failure 4.
- **SpiceWebAuthUrlRequest senders FUN_14324ee20 / FUN_143264160: no reference (dead in 0.695).**
- **Server change:** answer 0x77 with 0x78 {false, ""} instead of recording it only (Telemetry.cs). The pending
  payment request then fails at once in the client and does not wait forever. The server must never hand out a ticket
  for real-money endpoints.
- **Next:** which window starts a REST action in the 0.695 UI (Marketplace "buy Daybreak Cash" / redeem code), and
  where helper +0x2b0 / +0x34 (host, id) come from (likely a commerce-config packet).

## L64 — guild mail: ADVANCED

- [P] The data-source column `ToGuid` (PTR 0x145289bd8) is read only by the compose builder FUN_14363b170.
  - In the shipped UI only ClaimFeedback sets it.
  - The 0.695 AS3 export has no mail windows. They did not change, so the 0.589 search (no guild action) stands.
  - luabind has no guild-mail native (Mail.* list checked).
- [P] Text 564049: "You need to be in a Guild before you can send mail to **one**." The recipient is a guild, and only
  the sender's own guild can qualify.
- **[I]** The best-supported server rule: a ComposeRequest whose `To` names the sender's guild (its name; a "guild"
  keyword is a weaker alternative) becomes kind-0x100 guild mail to every member. A sender with no guild gets result
  0x11.
- **Next:** forum / wiki have no "guild mail" hits (searched). Old patch notes or the 2014 beta wiki.

## L65 — public channels: FOUND (agent-chat.md)

agent-chat.md §1.1-1.3 proves it [P]:
- A channel-9 line carries a u16 public id, and **only id 0 "General" has a name** (FUN_143b67f50).
- The client sets up General itself (FUN_143367e40, /1).
- ChannelOn / Off are sent only by Lua natives that no shipped script calls.
- So the 0.695 client has no Trade / Crafting / Newbie channels.

Leftovers are listed in agent-chat.md §3 (bubble colours, trailing u64 / u32 fields). The lead can close.

## L66 — friend presence, positions, groups: FOUND (most) [P]

**Friend row columns** (FUN_143548d00): FriendName +0xa0, GUID +0xe8, **InEncounter** +0x114, **LocationX / Y / Z**
+0x118 / +0x11c / +0x120, Online +0xf0, Popularity +0x124, ProfileBackgroundImageId +0x110, ProfileName +0x104,
ProfileIconId +0x108, ProfileRank +0x10c, IsLocal +0x100, Status +0x128, CurrentMinigameType +0x12c,
CurrentMinigameName +0x130, CategoryName "Contacts.Friends".

**Packets:**
- **FriendUpdatePositions 0x4D/5** (FUN_143543760): per entry guid → InEncounter (our FriendPositions.cs calls it
  "hidden"), X, Y, Z.
- **FriendUpdateProfileInfo 0x4D/4** (FUN_143543bf0), after the name: ProfileName → +0x104, IconId → +0x108,
  BackgroundImageId → +0x110, Rank → +0x10c (wire order Name, Icon, Background, Rank).
- **FriendStatus 0x4D/9** (reader FUN_14353c540, handler FUN_143543b70): {u8 sub, string name, u64 guid, 5 × u32
  (FUN_14353c460)} → Status +0x128, CurrentMinigameType +0x12c, CurrentMinigameName +0x130, +0x134, +0x138.

**Nothing shows these.**
- ContactListWidget (re/as3 ContactWidget; unchanged in 0.695) asks for FriendName, GUID, CategoryName, Online,
  Location, Status, LastPlayed and IsGroup, but no renderer reads Status, Location or LastPlayed.
- The row filler never sets "Location" or "LastPlayed".
- LocationX/Y/Z and InEncounter only reach the map's friend markers through GetFriendPositions; no shipped UI calls
  it (sweep).

**Offline debounce [P].**
- FriendOffline 0x4D/3 (FUN_1435407d0) stores (guid, _time64 seconds).
- The manager update FUN_143542340 (part_0109:27215) shows UI_FriendOffline only after **10 s**
  (`now − t < 10` keeps waiting; FUN_1435472e0).
- A FriendOnline 0x4D/2 in that window (FUN_143542e40) drops the entry and shows neither line.

**Server change:** none required. PresenceService's 30 s grace already does more than the client does. FriendStatus
can now be sent ({name, guid, Status, minigame type, minigame name text, 0, 0}), but nothing visible changes, so it is
optional.

**Next:** friend groups (0x4D/0xB extra infos) were not followed.

## L67 — /who, pending mail, quick chat, undeliverable tells: FOUND (b, c, d) + server bug

- **(a)** Already built (Game/Who.cs; wiki / forum in the sweep).
- **(b) [P] The pending-mail prompt is not the Notifications family.**
  - It is Mail **0xF8/0xF (0.695 numbering) PacketMailNewMailInboxNotification**. Reader FUN_1433752c0: u16 family,
    u32 sub, **u16 count**, and it returns false when the count is missing.
  - Handler FUN_14363d770 (part_0115) → FUN_1433db2d0 → FUN_1433db2e0 → FUN_1433d87b0: GaqItemPendingMail "You have
    [count] message(s)" (Mail.GAQ.SingleMessage 562631 / PluralMessages 562632, `count` param). It also re-requests
    the inbox list when the inbox is open.
  - **Server bug:** SocialServer/MailService.cs:392 and WorldServer/Game/Mail.cs:380 / :657 / :675 send it with an
    empty body. The reader then fails, so the 0.695 client drops it: no prompt and no inbox refresh.
  - **Fix:** write a u16 = the recipient's unread (or new) message count after the sub. 0.589 uses the same pattern
    (FUN_1432c8490).
- **(c) [P]** Quick chat is family **0x46**: sub 3 SendChatToChannel (part_0099:30110, send FUN_14333e230) and sub 2
  SendTell (FUN_14333e3d0), both channel 0 (Zone). The server sends an empty phrase list (0x46/1), so the client has
  no phrase to send. Nothing to build.
- **(d) [P]** PacketTargetClientNotOnline 0x1A {StringFixed<32> name} (zone case 6, part_0066:14050) prints code
  string PlayerNotOnline = text **560709** "[*player*] is not online at this." as a chat line. The server can use it
  when a tell cannot be delivered because the social back end is down. Ordinary offline targets already use chat
  kinds 2 / 3.

## L68 — PacketClientServerShuttingDown 0x4C: ADVANCED

- [P] Zone case 0x21 (part_0066:14360) only prints "The server has shutdown and logged you out." as a chat line
  (vtable +0x28 of the chat window). It does not change run state and does not disconnect.
- So the operations.md note "0x4C would undo the hand-over" is wrong: it would only add a misleading line.
- [P] The countdown players saw on live is **WorldShutdownNotice 0x5F** (FUN_142ce6a80: "The server is shutting down
  in: … Shutdown Reason: %s", text keys ServerShutdownMessageWithTimeRemaining / ServerImmediateShutdownMessage). A
  forum player quotes this text with the reason "Server is shutting down" (thread 47194, 29 Aug 2014). Already built
  (Shutdown.cs).
- [I] Live sent 0x4C at the final close, after the 0x5F countdown. It only makes sense when the connection really
  ends.
- **Server:** keep not sending it during the character-select hand-over. Send it only when a world stops with no
  hand-over (optional).

## L59 — leaderboards: FOUND (negative) [P]

- RTTI ClientLeaderboardManager 14474fec0 has **one vtable slot, the deleting destructor FUN_143304780** (ctor
  FUN_1433044f0). So no reader or handler is inlined there.
- Together with the morning sweep (no caller in Lua, UI or packs) and coverage-695.md (no reader for ProfileStats 0x7
  / 0x9), the 0.695 client neither asks for nor shows leaderboards.
- The admin classes LeaderboardPacketAdminFill / ClearPlayerStats have only RTTI pointers.
- **Server:** nothing to build. The lead can close.

## L63 — Lua-only world requests: ADVANCED [P]

- EncounterMatchmaking::MatchInvitationResponsePacket sender **FUN_1433fcb00** and
  PacketInviteAndStartMiniGameInvitePacket sender **FUN_1433fca60**: refscan NONE. No GAQ item vtable points at them.
  - In 0.695 these invitation answers are never sent, even if a server invited.
  - Compare the teleport GAQ response FUN_1433fcec0, which is live.
- The other builders found (QuizRequest part_0078:15226, RequisitionItemsRequest part_0090:25904, JoinLobbyGame
  part_0103:20813) sit behind Lua natives that no shipped script calls (sweep).
- **Server:** nothing to build.
- **Next:** if wanted, refscan the Quiz / Requisition / Lobby sender functions to tell Lua-reachable from dead.

## L69 — gateway internal packet fields: ADVANCED [P]

ApplicationPacketTracker classification FUN_143c7a9c0 (part_0146:5466), with the table set in ctor FUN_143c78d10:
- **External** packets (client side): opcode low 5 bits **5** (TunnelPacketToExternalConnection) and **6**
  (…FromExternalConnection) have a **1-byte header** (+0x340bc / +0x340c0). The top 3 bits are the channel.
- **Internal** packets (server side):
  - opcode **7** → **19-byte** header (+0x340a8);
  - opcode **8** → **13-byte** header (+0x340ac);
  - opcode **0x16** → **11 + 12 · n**, with **u32 n at byte offset 7** (+0x340b8) (+0x340b0 / +0x340b4).
- The ctor builds the 0x16 list packet with two fields 8 and 1 before an empty List<ExternalDestination>.
- FUN_143c7a750 then reads the game opcode at offset 2 or 6 (+0x340a0 / +0x340a4) for families 0x13 / 0x1e / 0x1f /
  0xff / 0xba / 0xc4: the per-opcode profiler accounting.
- [I] 7 = TunnelPacketFromExternalConnection internal (gateway → server; 19 B = opcode + u64 connection + 8 + 2 more)
  and 8 = …ToExternalConnection (server → gateway; 13 B = opcode + u64 connection + u32). A list destination = u64
  connection + u32. Which of 7 / 8 is which is still unproven.
- **Server:** our internal layouts already fill these sizes (gateway-service.md §3.4). No change.

## L76 — Environment Server: ADVANCED

- **(a) Heal [P, dev posts, forum thread 57751 (28 Jul 2016)]:**
  - The destroy timer is 10000 s ("first pass").
  - Destruction is a negative-space layer on top of world + player data, and heal removes it; terrain is never
    healed back.
  - Each destroy queues its own pending heal with its due time. A second destroy does not cancel it (there is no
    octree of pending heals).
  - A claim reset or a game turning on / off heals the claim.
  - The server already has 10000 s (VoxelDestruction.cs:224, P6203). Heals are bounded by the destroyed volume (the
    linking area / sphere) [I].
- **(b) Ruin layers [I strong]:** the Environment Server page's per-"Ruin Layer" {Desired, Active, Failed, Last ID}
  rows line up with the .cpa **LayerInfo kind-2 layers** (L47): 5 cave layers on the old continents, 1 on the islands.
  - Desired ≈ caverns in that layer (layer's cavern count) [I].
  - The ruin slots = the caverns' CaveRoomObjectGaps (mode-2 candidates).
- **(c)** Job types 4 / 5 / 6 / 17: unchanged.
- **Next:** the Chaos Caverns feedback thread 57065 for "ruins per cavern"; Desired vs SUGGESTED_CAVERN_COUNT.

## L8 / L48 — server-to-server messages: ADVANCED (small)

- The transport evidence is in L69: the client's tracker knows the internal tunnel headers (opcodes 7 / 8 / 0x16), so
  zones talked to clients through the gateway's internal tunnel [P for the format; I for "all zones used it"].
- Sibling client layouts that can start the server-only messages [P]:
  - ClientSelectionAreaRequest ctor FUN_14315e0a0 → ZoneSelectionAreaRequest;
  - OffGridPasteClientRequest writer FUN_14315b130 (L77) → OffGridPasteZoneRequest;
  - ApplyDiffRequest writer FUN_14315acb0 (L7);
  - ActivityJoinActivityRequest's World / Zone choice (L7), which shows activities had both world- and zone-scoped
    owners.
- No new layouts for ZoneAddClaim, ConsumeAttachmentZoneRequest and the like.

## Unchanged

| Lead | Note |
|---|---|
| L1 / L2 | No new file source. The sweep's negatives stand. |
| L3 | Runtime only (a Frida page hook); nothing new statically. |
| L4 | Answered by E-leads: retail has no readers for those replies. Live layouts would need other builds. |
| L50 | [P, negative] <0.695 test copy> ClientConfig.ini / .orig have no GatewayId key. The only "GatewayId" in the launcher strings is the hardware fingerprint field `NetworkGatewayId` (re/out/launchpad lp2016.txt:5868), which is unrelated. The AuthServer already logs a non-zero value. Nothing else to find statically. |
| L70 | No forum or patch-note text on routable / zone-crash hand-off (only "zone crash" fixes, 2014-12-23 and 2016-06-30). Our 10-16 rules stand. |
| L72 | processExpectedConnectionsOnly has no reader beyond the params copy (sweep). It is a server option. |
| L73 | Zone-to-zone transfer names only (unchanged). |
| L74 | No gateway command or flood text in the client or the forum. |
| L75 | The tracker decode (L69) shows no gateway-side relay class. Channel-3 packets are ordinary tunnelled game packets. Who answered them is still open. |

## Server changes, in priority order

1. **Mail new-mail notification (L67b, real bug).** NewMailInboxNotification must carry a **u16 count** after the
   sub (0.695 reader FUN_1433752c0). Today's empty body makes the 0.695 client drop it: no "You have N message(s)"
   prompt and no inbox refresh. Files: SocialServer/MailService.cs:392, WorldServer/Game/Mail.cs:380, :657, :675.
2. **CommerceSessionRequest 0x77 (L62):** answer 0x78 {bool false, ""} so a pending Spice REST request fails at once.
3. **AbilityLanding 0x27/0x1D (L7):** accept it on Zone, log only.
4. **.cpa manual-template record (L47):** the first u32 is the record version. Rotation is the next field (1-4),
   then 4 = graveyard kind, then template 76. Use that when stamping graveyard_launch. Correct the parser notes in
   ruin-stamping.md §5 and agent-graveyards.md.
5. **Optional:**
   - FriendStatus 0x4D/9 (L66) has no visible effect.
   - 0x1A PlayerNotOnline 560709 when tells cannot be delivered because the back end is down (L67d).
   - 0x4C only on a final stop with no hand-over (L68).
