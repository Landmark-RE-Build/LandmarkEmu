# Phase E4: "seen in play, not handled" and the default-branch hit (2026-10-26)

Slice: re/out/coverage_695.md "Seen in play, not handled (fix first)" (20 rows) and "Reached a server default branch
in play (OpcodeStats.Unanswered)" (1 row). Runtime data: server-data/worlds/dev/opcode_stats.tsv (read-only; created
2026-10-15 01:19, cumulative). Server HEAD db90a5f (client-0.695). Read-only: nothing was changed. Scratch:
`...\scratchpad\e4-seen\` (friend-list handler dump, a string reader for the 0.695 exe, aggregated stats).

**Verdict for the 21 rows.** No row is a player-visible bug. Of the 20 "fix first" rows:
- 13 Groups rows, 2 Activity rows, the SkillAdminPacketEcho row and the Notifications row are scanner misses.
- FriendUpdateExtraInfos, ListNotifications and DismountRequest are handled with no answer, and none is needed.
- StationCashProductsRequest is the only one the server truly leaves unanswered, by design and with no effect.

The default-branch row (Claims 0xE3) is already explained by L202. Two side results came out of the hunt:
- The stats file mixes three numbering eras, so its counts mislead the scanner (F1).
- docs/protocol.md §14.1 is out of date (F3).

## 1. Findings (ranked by player impact)

### F1. GAP (low): the Game Action Queue is not restored after a relog; ListNotifications is only logged
- The client hands its unanswered popup queue to the server when its UI shuts down. Notifications 0x01, u32 sub 1
  ListNotifications {u32 n, n × Data}, is sent by FUN_1433d6ae0 (docs/gameplay.md:645: FUN_1433302e0 from
  GuiOnShutdown) [P]. Seen 12× (`in 01000100`).
- The same class is the only Notifications packet the client accepts from the server. GameActionQueue::
  HandleNotificationPacket FUN_1433e3bf0 reads it with FUN_1433d7110 and rejects every other sub ("Bad notification
  packet type found …") [P].
- For each Data, FUN_1433e4730 rebuilds a popup by type [P]:
  - 2 GaqItemGroupInvite (FUN_1433d8160)
  - 3 GaqItemGroupApproval (FUN_1433d7f10)
  - 4 GaqItemFriendInvite (FUN_1433d7e40)
  - 6 GaqItemGuildInvite (FUN_1433d8300)
  - 7 GaqItemTradeInvite (FUN_1433d8da0)
  - 9 GaqItemMarketplaceItemReviewRequest (FUN_1433d8640)

  It then queues the popup with FUN_1433dac20, which also sends AddNotification sub 2 back (seen 16×).
- So the client is built for a round trip: it hands the queue over at shutdown and gets it back at the next login [I].
- The server logs the packet only (Network/Handlers/InteractionHandler.cs:262-266).
  - Guild invitations are brought back another way, by GuildInvitations 0x83/0x14 at entry (docs/protocol.md:1456,
    gameplay.md:645).
  - Group invites are keyed by the invitee's session (Game/Groups.cs:100), so a relog drops them anyway. The inviter
    hears "expired" after 2 minutes (Groups.cs:502-511).
  - A pending friend request (keyed by guid, 2 min, Game/Friends.cs) keeps waiting, but the asked player's popup is
    gone after a relog.
- Impact: a relog within the 2-minute window loses the popup of a pending friend, group or trade invitation.
- **Fix (described):** at world entry, send the player their own pending invitations again.
  - Simplest: resend the original packets, which make the client queue the popup itself, as Guilds does:
    ConfirmFriendRequest 0x1E/0x13 for a pending friend request. A group invite would also need its entry keyed by
    guid instead of session (Groups.cs:100).
  - Alternatively: an S→C ListNotifications with type-2/4 Data. The layout was proven in trace/0695/audit-wire-c.md:99,
    `u32 n × {u64, u32, u32, u64, name, u64, name, u64, u64, u32}`, from when the server sent it for guild invites
    (removed in da8feb2).
- Needs an in-game check: relog while a friend request is pending.

### F2. OK-NOTE: MountBase uses a u8 sub; the server reads a u16
- 0.695 header writer FUN_142c146b0: u16 base, then **u8** sub (+0x10). PacketDismountRequest's sender
  FUN_142c442b0 then writes one u8 flag (+0x18) [P].
- The response reader FUN_142f0db20 also reads a u8 sub (catalog_695 PacketDismountResponse: u16 +0x8, u8 +0x10) [P].
- Senders [P]:
  - FUN_142f100a0 ("SendDismountRequest", flag 0) and FUN_143049410 / FUN_14304df20 (logout path, flag 0)
  - FUN_142cbe430 (flag **1**)
  - the Lua natives FUN_1435ef530 / FUN_1435ef620 / FUN_1435ef6a0
- The runtime key `in A1000300 ×2` is sub 3 + flag 0.
- The server's MountRequest is `SubRequest16` (Network/Message/Model/InteractionMessages.cs:306). So it reads
  sub | flag << 8, and the flag-1 sender would be logged as "sub 259". The summary at :299-304 says "03 00, no body".
- No reply is needed. The client's DismountResponse case (FUN_142f0f1c0 case 4) only dismounts a rider entity, and
  Landmark has no mounts. MiscHandler.cs:53-56 logging only is right.
- **Fix:** `MountRequest : SubRequest8` and the summary "u8 sub, then u8 flag". No in-game check needed.

### F3. STALE: docs/protocol.md §14.1 still lists 0x37 RequestAccountStore as unanswered
- docs/protocol.md:1453 (row "checked 2026-10-12") says "0x37 (the player's own products) is still open".
- It has been answered since 6f9e9ed (2026-10-12): in-process at Game/Commerce.cs:382-384 (SendAccountStore), and in
  CommerceServer/ProductService.cs (case SubRequestAccountStore). The runtime confirms it: 0x37 ×84 in, 0x39 ×83 out.
- The other two subs in that row are still correct: 0x15 CountryCodesRequest and 0x0F StationCashProductsRequest reach
  the `default:` of Commerce.Handle (Commerce.cs:404-406, log only).
- **Fix:** drop 0x37 from the row and say the account store is answered (docs/commerce.md).

### F4. STALE (tooling): opcode_stats.tsv mixes three numbering eras, so "seen in play" counts mislead
- The file was created 2026-10-15 01:19 and is merged forever (OpcodeStats.Flush). It holds:
  - (a) 0.589 clients, until 10-15 10:33;
  - (b) 0.695 clients through the gateway translation 723cc6c (ClientWire695), 10-15 10:33 to 10-17 18:01. It counts
    internal 0.589 numbers, and 0x1000 | sub for subs that exist only in 0.695;
  - (c) native 0.695, from 003dc1f (10-17 18:01) on.
- Proven artefacts [P]:
  - `in 0D010D10 ×6` = InstancedGame 0x10D sub 0x100D. This is the translator's internal number for 0.695
    GetConfigurationRequest 0xD (723cc6c: `Family InstancedGame = new(0x0D, 0x01, 4, 0x0D, 1, {0x0D, 0x18, 0x19, 0x1A},
    0x1000)`).
  - `in 0D011500 ×13` = 0x15 in 0.589 numbering, i.e. 0.695 ObserverRegistration 0x16 (Game/InstancedGames.cs:44-46).
  - `in 45003600 ×19` and `in 45003F00 ×14` = 0.589-numbered RequestAccountStore 0x36 and GetProductForReview 0x3F
    (0.695 0x37 / 0x40). There is no FollowCreator 0x35 and no 0x3E anywhere in the file.
  - `in F900E34A ×6`: Claims 0xE3, the 0x80 | 0x63 artefact (L202).
- Effect: coverage.py adds these to 0.695 classes, or to none. The "default branch" table can never empty, because the
  0xE3 row is permanent.
- **Fix:** start a fresh counter at the native switch.
  - Move the current file aside, as opcode_stats-pre-native-20261017.tsv, and let OpcodeStats begin a new
    opcode_stats.tsv, ideally with a first `#since <utc>` line that coverage.py skips.
  - Or have coverage.py ignore keys whose sub ≥ 0x1000 and print the file's start date.
- Nothing has to change in the game.

### F5. STALE (tooling): scanner false results in this section beyond the three known (L214)
1. **Activity 0xA0** (2 rows). The wire is u16 0xA0, u8 group 1, u8 kind (ActivityRequest.cs:6-12, from header writer
   FUN_142c12c10 and sender FUN_142c41640), and the scanner takes the group byte as the sub.
   - The 6 hits are `A0000104`, kind 4 ActivityListRefreshRequest. They are answered by ActivityHandler.cs:24-31 with
     ListOfActivities `01 01 | 02000000 | 00000000`, which matches reader FUN_142e373c0 (u32 + u32 n) [P].
   - JoinActivityRequest (kind 2) was never seen. Ui.RequestToJoinActivity (FUN_1435cd0c0) has no shipped
     Lua/AS3 caller.
2. **Skills::SkillAdminPacketEcho** is CommandBase 0x1E, u16 sub **0x49A** (FUN_142f518c0: local_38 = 0x1e,
   local_30 = 0x49a; header FUN_142c12ce0 = u16 + u16) [P]. The scanner read its two u32 fields (1, 1) as base and sub.
   It was never seen (no `1E009A04` key). The 12 hits are ListNotifications.
3. **PacketInGamePurchaseCountryCodesRequest 0x45/0x15** is listed "handled (sub in handler)" (coverage_695.md:271),
   but it reaches Commerce.Handle's `default:` (log only). This is the known "shared file lends case labels" effect,
   in the other direction: a hidden unanswered request. See F6.
4. **Groups**: the real sub is at +0x14 (FUN_142c12f00 writes u16 base, **u8 +0x14**, then u32 +0x10 "mode") [P].
   - The scanner reads the +0x10 mode constant (1 or 2), which also mislabels the C→S GroupPacketMapPing (sub **0xB**,
     FUN_142d4aeb0 / Ui.SendMapPing FUN_1435d1550) and GroupPacketChangingJob (sub **0xA**, FUN_142df0160) as
     "sub 2, handled" (coverage-695.md:964-966).
   - Groups.Handle has no case 0xA or 0xB (Groups.cs:158-200 → `Unanswered("Groups")`). Neither is reachable:
     SendMapPing has no shipped caller, and FUN_142df0160 has no callers.
- **Fix (coverage.py):** take the sub width and position per family: Groups +0x14 u8, Activity u8 kind after u8 group,
  Command u16. Keep an allow-list of "answered on purpose / nothing to answer" rows (F6, F7, F2, Notifications) so they
  stop showing as "fix first".

### F6. OK-NOTE: StationCashProductsRequest 0x45/0x0F (and CountryCodes 0x15) stay unanswered, by design
- The request is sent automatically: the AccountInfoResponse handler FUN_143251680 runs FUN_14326b560
  {StringFixed<8> currency}, but only after a fully read result-1 reply. The client logs "Received account info …" [P].
- Runtime proof that our AccountInfoResponse (Commerce.cs:363-372: u32 1, "US", "USD", true, "") is accepted
  (reader FUN_14322d7b0 = u32, string, string, bool, string) [P]: 0x1A, 0x15 and 0x0F all show **53**. The 0x0F count
  equals the 0x1A count.
- The response 0x10 handler FUN_143256db0 fills the cash-SKU list and calls Lua `OnReceiveStationCashPriceList(ok)`.
  No shipped script or AS3 listens to it (UI/Default, Resources/Scripts, re/as3, re/as3_695). The list feeds only the
  real-money StationCashPurchase / CreditCard windows.
- CountryCodesRequest 0x15 (FUN_14326b170, from RequestStartupDataInternal FUN_14326b450 whenever the list is empty)
  would only fill InGamePurchaseDataSource::CountryCodes for the credit-card form. `OnUpdatedCountryCodes` has no
  listener either.
- **Fix:** none. This is protocol.md §14.1's intent (no real money). Mark both rows "by design" in coverage.py.

### F7. OK-NOTE: FriendUpdateExtraInfos 0x4D/0xB is the client's friend-group store; nothing visible is lost
- Sent by FUN_1435489b0 from two places [P]:
  - the friends manager's 10-second tick FUN_143542340, when the store is dirty;
  - the FriendList handler FUN_1435425d0.
- Body [P]: HashListMap<IString, FriendExtraInfoEntry>. The key is the friend's guid formatted "%llu" (0x1446b89bc);
  the entry also carries the friend's group string. Seen 42× (`4D000B01`, n = 1).
- There is no S→C class for it: the dispatcher FUN_143543050 has cases 1-10 and no 0xB.
- The only way back to the client is FriendData's trailing string (+0x108, reader FUN_14353bd30). FUN_1435425d0 sorts
  friends by it ("%.3d" counter for empty ones) and places each one with FUN_143544e00 under that group path [P/I].
- The groups come from ClientFriends.CreateGroup / MoveFriends / RenameGroup / DeleteGroup (luabind_695), which **no
  shipped AS3 or Lua calls**.
- So the server logging the packet (InteractionHandler.cs:240-244) and writing "" (SocialServer/ClientPackets.cs:152,
  Game/Friends.cs) loses nothing a player can see.
- This is the part of L66 left open ("friend groups were not followed", agent-leads2-d1.md:251).
- **Optional fix (live behaviour):** store (character, friend guid) → group string from 0x4D/0xB, as in the
  friend_extra_info table planned in social-service.md:253, and write it as FriendData's trailing string.

### F8. Covered already: Claims 0xF9 sub 0xE3 (the only "default branch" row)
L202 RESOLVED (phaseE2-packets.md:145): the gateway translation's 0x80 | 0x63 PlayerEnteredClaim, raw `F900E34A`.
Claims.cs:413-418 already says so. It stays in the table because of F4.

## 2. Checked and fine

- **Groups**, 13 rows (all `0x2B, 0x1` in the scanner, the known L214 effect).
  - Real keys: `2B000101` ×9 Invite, `2B000201` ×1 Join, `2B000601` ×1 Disband.
  - All are handled in Game/Groups.cs:151-200 (InteractionHandler.cs:249-253), or forwarded to the social service
    (GroupMessages.IsForwardedSub: 1, 2, 4, 5, 6, 8).
  - The other 10 classes have their own cases: Leave 4, Kick 5, SetOwner 8, AutoGroup 3, SetGroupFlags 7,
    SetGroupDescription 9, FindLfm 0xD, GetGroupInfo 0xE, JoinLfm 0x10, AcceptTeleport 0x26.
- **Notifications ListNotifications** (12×), and the AddNotification 2 (16×) / UpdateNotification 3 (15×) next to it:
  family handler InteractionHandler.cs:262-266. Log only by design (protocol.md:1455-1456). The restore part is F1.
- **Skills::SkillAdminPacketEcho**: not this opcode (F5.2). It was never sent in play.
- **ActivityListRefreshRequest**: answered, and the reply layout matches the reader (F5.1).
- **ActivityPacketJoinActivityRequest**: never sent in play; no shipped caller.
- **PacketDismountRequest**: no reply needed (F2).
- **FriendUpdateExtraInfos**: no reply class exists (F7).
- **StationCashProductsRequest**: unanswered by design (F6).
- **Claims 0xE3**: L202.
- **0x45 runtime subs** (in-process commerce: console.log "Commerce: in-process", docker/world/.env
  COMMERCE_ADDRESS empty):
  - 0x0B wallet ×518, 0x1A ×53, 0x37 ×84, 0x40 ×41, 0x47 ×48, 0x4A ×3: answered.
  - 0x15 ×53, 0x0F ×53: default (F6).
  - 0x36 ×19, 0x3F ×14: translation era (F4).
- **Families seen in play without an `Unanswered` counter**, spot-checked against their switches:
  - Teleport 0xFC subs 2/4/9/0xA/0xB (Game/Travel.cs:37-95)
  - Trade 0x3E subs 1/3/6/8 (Trades.cs:41, 92-107)
  - HudManager 0xDC sub 1 (RightClickMenus.cs:164)
  - FirstTimeEvent 0x6D sub 1 (FirstTimeEvents.cs:37, 218)
  - Chest 0xFF subs 2/3 (Chests.cs:35, 117-130)
  - Encounter 0x2C subs 0x6C/0x6D (Encounters.cs:61, WorldChannel/EncounterBroker.cs:56)
  - ActionManager 0x9A subs 1/2 (ActionBarHandler.cs:24-46)
  - Rating 0xA7 0x18/0x1D (MiscHandler.cs:162-174)
  - Target 0xB5 subs 2/3 (InteractionHandler.cs:333-349)
  - InstancedGame 0x10D GetConfiguration (Rework.cs:43)

  All have cases.
- **Every seen base has a handler**: no "no handler …" and no "raw 0x…" Unanswered rows. Both counters date from
  7cd2183 (10-12), before the file existed.

## 3. New leads

- **NL1 (F1).** Need: whether live sent ListNotifications back at login, and which Data fields carry what (types 2, 3,
  4, 7, 9).
  - Searched: FUN_1433e3bf0, FUN_1433e4730 and the six GaqItem constructors; protocol.md §14.1; gameplay.md:645;
    audit-wire-c.md:99.
  - Try next: the readers of FUN_1433d6140 (the Data reader); design/forum for "invite" + "relog"; a Frida capture of
    the client's shutdown list with a pending friend request.
- **NL2 (F7).** Need: the exact FriendExtraInfoEntry wire (three strings?) and whether FriendData +0x108 is a group
  path ("a/b") or a flat group name.
  - Searched: FUN_1435489b0, FUN_1435425d0, FUN_143544e00 (path parse into a 2048-byte buffer), luabind_695.
  - Try next: the entry serializer (catalog: 0.589 FUN_143498ce0 / FUN_14348cd20 → 0.695 partners), and
    ClientFriends.MoveFriends FUN_1435e2820, to see which field it sets at +0x140.
- **NL3 (F4).** Need: other translation artefacts in the file (Mail 0x1004, InGamePurchase 0x1030).
  - Searched: in-keys aggregated by family (scratch in_b2.tsv / in_b3.tsv); none seen for 0xF8 / 0x45.
  - Try next: nothing, once F4's fresh counter exists.

**Summary.** Nothing in this slice was a player-visible fault:
- The scanner missed real handling (Groups; Activity refresh; SkillAdminEcho, actually Command 0x1E/0x49A).
- Three requests are logged with no answer, and none is needed (Dismount, FriendUpdateExtraInfos, ListNotifications).
- StationCashProductsRequest is unanswered by design. Its runtime count proves that our AccountInfoResponse is parsed.
- The single default-branch hit is the known L202 translation artefact.

What matters for the coordinator: opcode_stats.tsv mixes 0.589, translated and native 0.695 numbers (F4, with four
proven artefacts), and coverage.py has more false readings (F5). protocol.md §14.1 still calls 0x37 open (F3). Two
optional behaviour gaps remain: popups are not restored after a relog (F1), and friend groups are not stored (F7).
One model is the wrong width: MountRequest (F2).
