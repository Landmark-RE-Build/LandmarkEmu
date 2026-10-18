# Wire audit, group c (2026-10-17): commerce, mail, chat / command, social, achievements, titles, grief, rating, trade, encounters, groups, UI

Read-only audit by agent c (brief: audit-wire-brief.md). Server is `<workspace>\LandmarkEmu` as of today (after 003dc1f and
4e7e9ed). The client is `re/decomp_all_695`, read with `re/tools/pkt.py` / `fn.py` (DECOMP=decomp_all_695). Every 0.695
reader named below was opened and walked field by field. Most of these classes are "same" as 0.589 (coverage-695.md), so
the main risks were writers that were wrong all along, and sub numbers left over from the renumbering. [P] means proved
from the client code, [I] means inferred.

Conventions: "S→C" rows give the server writer and the 0.695 reader. "exact" means the client's handler drops the packet
unless the body is consumed exactly (`(int)end - (int)cursor < 1`). Sub widths: InGamePurchase u16, Mail u32, Chat u16,
Command u16, Friend u8, Ignore u8, Groups u8, Guild u16, Achievement u8, PlayerTitle u8, Grief u8, Rating u8, Trade u8,
Encounter u16, QuickChat u16, Ui u8, BugSubmission / ClaimReport u16, PlayerUpdate u32 (the opcode codec writes
`26 00 ss 00 00 00`).

## 1. Result table

### InGamePurchase 0x45 (S→C dispatcher FUN_143258f50, `switch(*(u16*)(pkt+2))`, no −1 shift) [P]

| Packet | Server writer | Client reader (0.695) | Verdict | Detail |
|---|---|---|---|---|
| 0x0C WalletInfoResponse | Game/Commerce.cs:211; CommerceServer/ClientPackets.cs WalletInfo | FUN_143258ad0 → wallet FUN_1432300e0 | OK | u32 result, then u8 (+0x59), u32 balance, u32, str, str, u8 zip flag. exact |
| 0x0E ServerStatusResponse | RemoteCommerceService.cs:438 `0E 00 00`; ClientPackets.ServerStatus | FUN_143256750 | OK | bool. exact |
| 0x1B AccountInfoResponse | Commerce.cs:356; ClientPackets.AccountInfo | FUN_143251680 → FUN_14322d7b0 | OK | u32, str, str, bool, str. exact |
| 0x2E CommerceConfiguration | Commerce.cs:320 | FUN_143252250 → FUN_14322db60 | OK | u32 min fee, u32 percent, u32 min price. exact |
| 0x31 ProductCreateResponse | Commerce.cs:1150; ClientPackets.CreateResponse | FUN_143252300 → FUN_143237500 | OK | u32 status, str |
| 0x39 AccountStore | Commerce.cs:567; ClientPackets.AccountStore | FUN_143251a40 → FUN_14322d9d0 → FUN_14322c7c0 | OK | account {u64, bool, followings {u64, str} (FUN_1432333c0), purchases, list, u64×3, u32×2}, string list, bundles, products. exact |
| 0x3A AddFollowing | Commerce.cs:641; ClientPackets.AddFollowing | FUN_143251c60 → FUN_14322da80 | OK | u64, bool, str. exact |
| 0x3B RemoveFollowing | Commerce.cs:654; ClientPackets.RemoveFollowing | FUN_143252b40 → FUN_14322de50 | OK | u64, bool. exact |
| 0x3F CommerceSearchResponse | Commerce.cs:118 | FUN_143253340 → FUN_14322eff0 | OK | str, list {u32,u32} (FUN_143233bf0), u32, list u64 (FUN_143233530). exact |
| 0x41 ReviewProductResponse | Commerce.cs:1006; ClientPackets.ReviewProduct | FUN_143252e90 → FUN_14322dfa0 → product FUN_14322f3f0 | OK | product {u64, u64, u32×5, str, str, u64, u64, u32, str, items (u64, u32, u32, u64, bool, str) FUN_14322f630}, u32. exact |
| 0x44 SubmitProductReviewResponse | Commerce.cs:1108; ClientPackets.ReviewDone | FUN_1432534f0 | OK | bool. exact |
| 0x46 ModifyScreenshotsResponse | Game/SiteScreenshots.cs:106 | FUN_143254940 → FUN_14322e350 | OK | u64, u64, bool. exact |
| 0x4E IsTemplateValidResponse | Commerce.cs:189 | FUN_143258720 → FUN_14322dc40 | OK | u64, bool. exact |
| C→S 0x2F ProductCreate / 0x30 TemplateProductCreate | Commerce.CheckProduct; FromWire maps 0x30 to 0x2F | writer FUN_14322b430 (product part, also first in FUN_14322bea0) | OK | u32 ×3, str ×3, items FUN_14322c210 {u64, u32, u32, u64, u8, str}, u8, str, then a trailing u32 the server ignores |
| C→S 0x3E Search, 0x43 SubmitReview, 0x35 / 0x36 Follow | Commerce.cs | writers FUN_14322bda0 {str, u32}, FUN_14322be20 {u64, u32}, FUN_14322b9a0 / FUN_14322ba50 {u64} [I which is which] | OK | widths match the server's reads |
| C→S 0x47 PointOfNeed type 1 | InteractionHandler.cs:383 | writer FUN_14322bf60 {u32, u8, u32, u64, u32, u32} [I identity] | OK | the server reads the u32 at offset 5 |
| C→S 0x4A ModifyScreenshots | SiteScreenshots.HandleModify | class built at FUN_1435e3b70 (0x45/0x4A); serializer not located | OK [I] | u32 action, u32 index, u64 site, then a ClaimsImage array the server ignores (seen in play 20× on 0.589) |
| 0x77 / 0x78 CommerceSession | WorldSession.cs:1209 | FUN_142c4f100 | OK | bool, str. exact |
| 0xFE PlayerStudioRegistered | LocalCommerceService.cs:44 | FUN_142c4ab40 | OK | bool. exact |
| 0x66 MembershipActivation | Membership.cs:341 | FUN_142c4a7c0 | OK | u32. exact |
| PlayerUpdate 0x35 MemberStatus | Membership.cs:356 | case 0x34 → FUN_142c4f210 → FUN_142c25ad0 | OK | u64, bool |
| PlayerUpdate 0x49 CurrencyChange (wallet / Lumens) | WorldSession.cs:691 | case 0x48 → FUN_142c4a8c0 | OK | u64, list {u8, u32}. exact |
| 0x98/3 PlayerImageData (portraits) | Game/Portraits.cs:81; CommerceServer PortraitService (ClientPackets.PlayerImageData) | FUN_142f59730 | OK | u64, str, bool, {u32, u32, u64, u32, attachment list, customization {list, u32, str, u32, u32}, u32×4, str}, blob |
| Rating 0x12 VoteReply | Ratings.cs:73; LocalCommerceService.cs:66; ClientPackets.VoteReply | FUN_142da26d0 | OK | u64, str, bool, u64 |
| Rating 0x19 CanVoteReply | Ratings.cs:121 | FUN_142da2300 | OK | u64, str, bool, u64 |
| Rating 0x1E RequestContestsReply | Contests.cs:167; ClientPackets.ContestList | FUN_142da95a0 → FUN_142da21e0 | OK | u64, u32 n × {str, str, u32, u32, u64×3}. exact |
| Claims 0x18 ContestEntryResults (CommerceServer / zone) | Contests.cs:236; RemoteCommerceService.cs:785; ClientPackets.ContestEntryResults | FUN_142da8ad0 | OK | bool, str, str (sub 0x18 is below the +2 shift) |

### Mail 0xF8 (S→C dispatcher FUN_1433765a0 cases 5, 8, 0xA, 0xC, 0xE, 0xF, 0x11, 0x14, 0x16) [P]

| Packet | Server writer | Client reader | Verdict | Detail |
|---|---|---|---|---|
| 5 ComposeResponse | Mail.cs:403; SocialServer/MailService.cs:273 | FUN_143374fc0 | OK | u32 result; PlayerName only when result 1 or 0xD. exact |
| 8 InboxListResponse | Mail.cs:155; RemoteMail.cs:145; MailService.cs:106 | FUN_143374400 → FUN_143373d90 | OK | u32, u64, u16, n × {u64, u64, name, str, str, u64, u32, u16, u32} |
| 0xA ReadMessageResponse | Mail.cs:472 | FUN_143374610 → FUN_143373a90 | OK | u64, u32 result, u32 kind, [stop on failure], u64, name, str, str, u64, u32, attachments {u64, u32 type; 2 = item record FUN_142c208e0, 5 / 6 = u32}, u32 n + bytes |
| 0xC DeleteMessageResponse | Mail.cs:729; RemoteMail.cs:160; MailService.cs:147 | FUN_1433742b0 | OK | u64, u32 |
| 0xF NewMailNotification | Mail.cs:382 (+2 more); MailService.cs:399 | FUN_1433752c0 | OK | u16. exact |
| 0x11 ConsumeAttachmentResponse | Mail.cs:621; RemoteMail.cs:170; MailService.cs:237 | FUN_1433741d0 | OK | u64, u64, u32 |
| 0x14 EnableWindow | Mail.cs:91 | FUN_143375190 | OK | u64. exact |
| 0x16 AttachItemResponse | Mail.cs:706 | FUN_143373f10 | OK | u32 result + echo of the request body (C→S writer FUN_143635070: u32, u64, u32×3, bool) |
| C→S 7 / 9 / 0xB / 0x10 / 0x15 / 1 | Mail.Handle | 0.695 writers FUN_1436361e0 / FUN_143636380 (u64) / FUN_143636030 / FUN_143635e30 / FUN_1436359d0 / FUN_143637350, sent as PacketTunnelPacketFromExternalConnection | OK | sub constants are the 0.695 numbers |

### Chat 0x13 (u16 sub)

| Packet | Server writer | Client reader | Verdict | Detail |
|---|---|---|---|---|
| 1 PacketChat | Chat.cs:283 Build (Route, SendTell, SystemChannel :222, NpcSay :190); SocialServer/ChatService.cs:304 | FUN_143356a90 | OK | i16 kind, i16 channel, u64, u64, name, name, str, 4×u32, u64, u32×3, u8, bool [ch 9: i16; ch 10: str]. The 38-byte tail (TrailingFixedLength) matches 16+8+4+4+4+1+1 |
| 4 ChatPacketFromStringId | Emotes.cs:274 | FUN_143356860 | OK | u64, u32, bool×3, u32, u64 (+0x38), u64 (+0x30), u32 |
| 5 TellEcho | Chat.cs:270; ChatService.cs:206 | FUN_143357610 | OK | u64, name, str, bool |
| 9 RoomInfo | ChatRooms.cs:191; ChatService.cs:287 | FUN_1433571f0 → FUN_143357950 | OK | str, n × {u64, name}, u32 |
| 0xB / 0xD Join / LeaveResponse | ChatRooms.cs:181; ChatService.cs:279 | FUN_143357050 / FUN_143357120 | OK | str, u32 |

### Command 0x1E (S→C dispatcher FUN_1433baf00 = ClientCommandProcessor vtable +0x10) [P]

| Packet | Server writer | Client reader | Verdict | Detail |
|---|---|---|---|---|
| 2 WhoReply | Who.cs:42 | FUN_1433bc350 → FUN_143383a70 → FUN_143385c40 | OK | n × {str, u8, u32, u32×3, u64}, bool |
| 0x13 ConfirmFriendRequest | Friends.cs:180; SocialServer/ClientPackets.cs ConfirmFriendRequest | FUN_1433823c0 | OK | u64, name, name, bool. exact |
| 0x15 SetChatBubbleColor (relay) | ChatBubbleColor.cs:76 | FUN_1433bbef0 → FUN_1433833e0 | OK | u32×3, u64. exact |
| 0x25 RequestPlayerPositionsReply | PlayerPositions.cs:80 | FUN_1433831b0 → FUN_143385790 | OK | u64, bool, bool, n × {u64, str, bool×3, f32×4} |
| 0x44 CommandList | Emotes.cs:125 | FUN_143382200 | OK | string list, u32. exact |
| 0x45 / 0x46 AddWorld / ZoneCommand | DynamicCommands.cs:158 | FUN_143391be0 / FUN_143391ce0 | OK | str |
| 0x53 RunClientLua | ClientLua.cs:25 | FUN_1433be710 → FUN_143382890 | OK | str, f32×3 (`Write(ushort)` overload, so 2 bytes) |
| 0x5B Random | RandomRoll.cs:56 | FUN_1433bf480 → FUN_1433830a0 | OK | u32, u32, u64, u32. exact |
| C→S 0x15, 0x18, 0x22 / 0x23, 0x24, 0x3B, 0x43, 0x47, 0x5B, 0x10 / 0x12 / 0x14 / 0x20 | InteractionHandler.HandleCommand | – | OK | subs as in the 0.695 id table; bodies read the way the S→C twin is laid out |
| FirstTimeEvent 0x6D/5 (ClientLua.RunWithArgs) | ClientLua.cs:49 | FUN_143632800 | OK | u8 sub, str, str, u32, bool |
| PlayerUpdate 0x57 AFK | Afk.cs:61 | case 0x56 → FUN_142c4b3e0 → FUN_142c26160 | OK | u64, bool |
| C→S PlayerUpdate 0x77 EmoteStringById | PlayerHandler.cs:59 | sender part_0101.c:38195 {u32 text} | OK | the server takes the last u32 |

### Friends 0x4D / Ignore 0x72 / Notifications 0x01 / ShowSystemMessage 0x69 / QuickChat 0x46

| Packet | Server writer | Client reader | Verdict | Detail |
|---|---|---|---|---|
| 4D/1 FriendList, /6 FriendAdd | Friends.cs:361 WriteFriend; SocialServer ClientPackets.FriendList / FriendAdd | dispatcher FUN_143543050 case 1 / 6 → FUN_14353cc50 / FUN_14353d3a0 → FUN_14353bd30 | OK | name, u64, bool; offline: u64; online: u32×4, bool (set, so the 3 u32 are skipped), u32, bool, u32×5; then str |
| 4D/2 Online, /3 Offline, /7 Remove | Friends.cs; ClientPackets | FUN_14353c210 / FUN_14353d590 / FUN_14353d6c0 | OK | {str, u64, bool} / {u64} / {u64} |
| 4D/5 FriendUpdatePositions | FriendPositions.cs:34 | FUN_14353d860 → FUN_14353bba0 | OK | n × {u64, str, bool hidden, [f32×3 when not hidden]} |
| 4D/8 FriendMessage | Friends.cs:270; ClientPackets.FriendMessage | case 8 → FUN_14353c090 | OK | u8, u64, name, name |
| 72/1 List, /2 Add, /3 Remove | Ignores.cs:47 / 162 / 173; ClientPackets.Ignore* | FUN_143543e70 (inline 1 → FUN_14353cf40, 2 → FUN_14353d990, 3 inline) | OK | {n × (u64, str)} / {u64, str} / {u64}. exact |
| 01/1 ListNotifications (guild invite) | Notifications.cs:29 | FUN_1433d7110 → FUN_1433d6140 | OK | u32 n × {u64, u32, u32, u64, name, u64, name, u64, u64, u32} |
| 0x69 ShowSystemMessage | SystemMessages.cs:25; SocialServer ClientPackets.SystemMessage | FUN_142c249f0 | OK | u32, str, params {str, str, u32}, u32, u32 |
| 0x46/1 QuickChat SendData | MiscHandler.cs:268 `01 00 00000000` | FUN_143345ab0 (sub 1) → FUN_14333e050 | OK | i16 sub 1, u32 count 0 |

### Guild 0x83 (u16 sub; dispatcher FUN_142ef2780) [P]

| Packet | Server writer | Client reader | Verdict | Detail |
|---|---|---|---|---|
| 2 Disband | Guilds.cs:907; GuildService.cs:668 | FUN_142ee8740 | OK | u64. exact |
| 0xE UpdateRank | Guilds.cs:1212 | FUN_142ee59b0 → FUN_142ee5aa0 | OK | u64, {u32, str, u32, str, u32, str list, u32, u32, list, str, u32}, u32 |
| 0x13 GuildData | Guilds.cs:1067 | FUN_142ee85f0 → FUN_142ee4750 / FUN_142ee4b50 / FUN_142ee7aa0 | OK | core {u64, str×7, u64, str, u64, u32×3, str, str, u64}, members FUN_142ee5180, invitations, news (u32 key + row), u64, list, bool, {bool, str, list}, n × {u64, str} |
| 0x15 AddMember | Guilds.cs:1164 | FUN_142ee8300 | OK | u64, member, str, u64, u64, str. exact |
| **0x16 RemoveMember** | **Guilds.cs:853; SocialServer/GuildService.cs:630** | **case 0x16 → FUN_142ee5650; handler FUN_142ef4720** | **MISMATCH** | client {u64 guild +0x18, u64 member +0x20, str name +0x28}, exact; the server writes {u64 guild, **str member-id string**, u64 member, str name}, so the client drops it |
| 0x17 UpdateInvitation | Guilds.cs:1191 | FUN_142ee58d0 → FUN_142ee4ee0 | OK | u64, u64, str×3, u64, str, u32, u32, str, str, u32 |
| 0x18 OnlineStatus | Guilds.cs:1177 | FUN_142ee52b0 | OK | u64, u64, bool |
| 0x19 Notification | Guilds.cs:1247; GuildService.cs:~1080 | FUN_142ee54a0 | OK | str, name, u8 |
| 0x1A AddLogUpdate | Guilds.cs:1345; GuildService.cs:862 | FUN_142ee4280 → FUN_142ee4d70 | OK | {u32, str, str, str, u64, u32, u32, str, str}, u64 |

### Groups 0x2B (u8 sub; dispatcher FUN_142deaa10, `switch(sub)`) [P]

| Packet | Server writer | Client reader | Verdict | Detail |
|---|---|---|---|---|
| 1 Invite (incoming / result) | Shared/Game/GroupPackets.cs:83 InviteRecord | FUN_142debc60 → FUN_142de3d30 | OK | u32 type, u32 code, u32, invite {u64, u32, player, player, u32} (FUN_142ddcd70) |
| **2 Join (result to the joiner)** | **GroupPackets.cs:108 JoinResult** | **FUN_142dec100 → FUN_142de3e50** | **MISMATCH** | client reads 4 u32 (type +0x10, code +0x18, response +0x1c, +0x20), then the invite at +0x28; the server writes **5** u32 (type, code, response, 0, 0) before the invite. The handler does not check the error flag, so the line still prints, but the invite is shifted by 4 bytes: [*source*] / [*target*] come out empty or garbled |
| 8 SetOwner | GroupPackets.SetOwner | FUN_142ded9a0 → FUN_142de4490 | OK | u32, u32, u64, u32 |
| 0x11 AddGroup | GroupPackets.AddGroup | FUN_142deac60 → FUN_142ddca90 | OK | u32, group {key {u32, u64, u8}, u8, str, u32, n × {u64, member}, u32, bool} |
| 0x12 AddMember / 0x13 UpdateMember | GroupPackets; member record Groups.cs:532 MemberRecord | FUN_142de37f0 / FUN_142dedd20 → member FUN_142ddce40 | OK | member: player, u32×8, bool×2, u32, u64 link id (+0xf8), u32×4 (+0x100), f32×3 (+0x110), u32×4, u64, u32, u32, bool, n × u32×3. HideLocation / MarkLinkdead offsets match |
| 0x15 RemoveGroup | GroupPackets.RemoveGroup | FUN_142deca60 (inline) | OK | u32, u32 reason, u32 group |
| 0x16 RemoveMember | GroupPackets.RemoveMember | FUN_142decfa0 (inline) | OK | u32, u32 reason, u64, [reason 4: u64, bool] |
| **0x17 RemoveInvitation** | **Groups.cs:487 / 490; SocialServer/GroupService.cs:255 / 258 (both via InviteRecord)** | **FUN_142decc00 → FUN_142de4220** | **MISMATCH** | client {u32 +0x10, u32 code +0x18, invite +0x20}, which is 2 u32 before the invite; InviteRecord writes 3 (type, code, 0). So the invite key no longer matches the pending invite (`*plVar4 == param_1+0x60`): the invitee's expired popup is not dropped, and the names are shifted |
| 0x24 InviteToGroupContent | Groups.cs:705 | FUN_142debfa0 → FUN_142ddd7c0 | OK | u32, u64, u32×3, u32×4 |
| C→S 1 / 2 / 5 / 8 / 0x26 | Groups.Handle | Join reader offsets; Kick writer FUN_142ddbdf0 | OK | Join response at body offset 8; SetOwner guid at 8; Kick u64 at 16; AcceptTeleport u64 at 4 |

### Achievements 0x92 / PlayerTitle 0x94 + PlayerUpdate 0x1C / Grief 0xC0

| Packet | Server writer | Client reader | Verdict | Detail |
|---|---|---|---|---|
| 92/2 Add, /3 Initialize | Achievements.cs (records copied from the 0.589 dump 71-AchievementBase) | dispatcher FUN_142e30720 → FUN_142e2a340 / FUN_142e2a520 | OK [I] | the class is "same" (packets-695 B9); the record bytes come from a 0.589 capture |
| 92/4 Complete | Achievements.cs:1155 | FUN_142e2a400 | OK | u32. exact |
| 92/6 / 7 / 8 ObjectiveActivated / Update / Complete | Achievements.cs:519 / 532 / 538 | FUN_142e28920 / FUN_142e28c60 / FUN_142e28b20 | OK | u32×5 + bool / u32×4 / u32×4 + bool |
| 92/0xB Tracker | Achievements.cs:391 | case 0xB → FUN_142e29c10 | OK | n × u32 |
| 92/0xF GlobalAnnounce | Achievements.cs:1134 | FUN_142e28780 | OK | u32, u32, name, u64 |
| 94/1 TitleAdd | Titles.cs:~120 | FUN_142f28770 case 1 → FUN_142f27d20 | OK | u32 id (+0x18), u32, u32. exact |
| **PlayerUpdate 0x1C PlayerTitle** | **Titles.cs:169 UpdatePacket (Broadcast :158, PlayerManager.cs:142)** | **FUN_142cdfa60 case 0x1B → FUN_142c4c860 → FUN_142c2a390** | **MISMATCH** | the client reads **title {u32 id, u32 type, u32 name} first (FUN_142c25e20 → +0x20), then the u64 guid (+0x18)**; the handler looks the entity up by +0x18. The server writes the guid first. Same in 0.589 (FUN_142b909a0). Size is equal (20 bytes), so nothing is dropped, but the entity lookup misses and no title is ever shown on a name plate |
| C0/1 SetGriefPoints, /2 SetGriefLevel, /3 Reset | Grief.cs:415, :373, :267 | dispatcher FUN_142ee1bf0 → FUN_142ee1d80 / FUN_142ee1ce0 | OK | i32, i32 / i32, i32 (disassembled in Grief.cs) / header |

### Trade 0x3E (u8 sub)

| Packet | Server writer | Client reader | Verdict | Detail |
|---|---|---|---|---|
| 2 Invite | Trades.cs:130 | FUN_14363fb80 | OK | name, u32 |
| 4 StartSession | Trades.cs:154 | FUN_14363fd50 | OK | u64, name, u32 |
| 5 EndSession | Trades.cs:340 / 375 / 456 / 479 | FUN_143640dd0 | OK | u32 |
| 0xA UpdateCoinCount | Trades.cs:223 | FUN_143640040 | OK | u32, u32, bool, u32 |
| 0xB UpdateAddItem | Trades.cs:192 | FUN_14363ff00 | OK | u32, u32, bool, item record FUN_142c208e0, bool |
| 0xC UpdateItemCount | Trades.cs:183 | FUN_143640150 | OK | u32, u32, bool, u64, u32, bool |
| 0xD UpdateAccept | Trades.cs:239 | FUN_14363fe40 | OK | u32, bool |
| 0xE Message | Trades.cs (many) | FUN_143640ef0 | OK | u8 |
| 0xF Rejected | Trades.cs:149 | FUN_143640fb0 | OK | name |
| C→S 1 / 3 / 6 / 7 / 8 / 9 | Trades.Handle | writers FUN_142c46d60 {u64}, FUN_14363f8f0 {u32, u8}, FUN_14363f6b0 {u32, u32, u32}, FUN_143640660 {u32}, FUN_14363f9c0 {u32, u64, u32}, FUN_14363f830 {u32, u32} | OK | |

### Encounters 0x2C / UI 0x32 / reports

| Packet | Server writer | Client reader | Verdict | Detail |
|---|---|---|---|---|
| header of every 0x2C S→C | Encounters.cs:91 Send | FUN_142cdc370 → FUN_142c17550 | OK | u16 sub, u32 +0x14, u32 +0x18 |
| 0x7D / 0x7E death window | Encounters.cs SendDeathWindow | FUN_142cdc370 inline | OK | header only |
| 2 PlayerEnter, 3 Complete, 6 Shutdown, 0x6A State, 0x6B ZoneIsReady, 0x72 Details, 0x78 ParticipantMessage, 0x79 Terminate | Encounters.cs:91 / 178 | ClientCommandProcessor vtable +0x18 = FUN_1433bd040; its switch is an undecoded jump table | UNKNOWN | bodies not re-verified. Classes are "same" as 0.589 (no reader catalogued: rcmp "no call") |
| C→S 0x67 / 0x69 / 0x6C / 0x6D / 0x7C | Encounters.cs | writers FUN_142c42b70 / FUN_1433e9320 / FUN_142c42f00 / FUN_142c430b0 / FUN_142c42d60 | OK [I] | "same" |
| C→S Ui 0x17 EventTriggered | MiscHandler.cs:81 | writer FUN_14315c600 → FUN_142f29130 | OK | u16 base, u8 sub, u32 |
| C→S BugSubmission 0x55/1, ClaimReport 0x56/1 | Game/Reports.cs | writer FUN_1431185c0 (u16 sub; u32, str, u32, str) | OK | u16 sub, and the XML is found by scanning |
| C→S Rating 8 / 0x18 / 0x1D | MiscHandler.cs:157 | – | OK | the body includes the sub byte; the readers skip it |

Not sent, so out of scope: Friend 4 / 9 / 0xA, AddAcquaintance 0x4B, Social 0x9C, UpdatePlayerFriendStatus 0xAB, UpdateGuildTag 0xDF,
EncounterMatchmaking 0x8A, SocialShare 0x104, Chat DebugChat 3 (its 0.695 body differs: trailing bool), the Ui 0x32
S→C subs, Guild 0x12 / 0x14 / 0x1B / 0x1C, Mail 0xE ClearInboxResponse.

## 2. FIX list (ranked by player impact)

1. **Guild RemoveMember 0x83/0x16 is dropped** (exact-consumption reader). Today, members who quit or are kicked stay
   in everyone's roster, and a leaver's client keeps the guild window and guild chat. In both writers, drop the id
   string:
   - `LandmarkEmulator.WorldServer/Game/Guilds.cs:853-859` and `LandmarkEmulator.SocialServer/GuildService.cs:630-636`:
     ```csharp
     byte[] removed = Build(RemoveMemberSub, w =>
     {
         w.Write(guildId);        // +0x18
         w.Write(characterId);    // +0x20 (handler FUN_142ef4720 matches the member by it)
         w.Write(name);           // +0x28
     });
     ```
   [P: FUN_142ef2780 case 0x16 → FUN_142ee5650 {u64, u64, str}, exact; 0.589 FUN_142e468c0 identical]
2. **PlayerTitle (PlayerUpdate 0x1C) field order.** Titles never appear on name plates (own or others'). In
   `LandmarkEmulator.WorldServer/Game/Titles.cs:163-173` (UpdatePacket), write the title before the guid:
   ```csharp
   Write(w, title ?? new Title(0, 0, 0));   // u32 id, u32 type, u32 name text (FUN_142c25e20 → +0x20)
   w.Write(session.PlayerGuid);             // u64 (+0x18, the entity lookup key)
   ```
   Also fix the class summary line 29 ("{u64 player guid, title}" → "{title, u64 player guid}"). [P: FUN_142c2a390; 0.589 FUN_142b909a0 the same]
3. **Groups RemoveInvitation 0x2B/0x17 has one u32 too many.** Expired invitations are not cleared on the invitee's
   client, and the "expired" lines have shifted names. Add a dedicated builder in
   `LandmarkEmulator.Shared/Game/GroupPackets.cs` and use it at `Groups.cs:487/490` and `GroupService.cs:255/258`:
   ```csharp
   public static byte[] RemoveInvitation(uint code, ulong inviterGuid, string inviterName, ulong inviteeGuid, string inviteeName) => Build(w =>
   {
       w.Write(RemoveInvitationSub);
       w.Write(0u);          // +0x10
       w.Write(code);        // +0x18 (3 to the inviter, 4 to the invitee; FUN_143c1c940)
       w.Write(inviterGuid); // invite key (FUN_142ddcd70 +0x20): must equal the pending invite's key
       w.Write(0u);
       WritePlayer(w, inviterGuid, inviterName, 1);
       WritePlayer(w, inviteeGuid, inviteeName, 1);
       w.Write(0u);
   });
   ```
   [P: FUN_142de4220 reads two u32 then the invite]
4. **Groups Join result 0x2B/2 has one u32 too many.** The "You have joined the party." / "invitation is no longer
   valid" lines show empty or garbled names. In `GroupPackets.cs:108` JoinResult, remove the second `w.Write(0u);`
   after the response, so the body is type, code, response, one u32, then the invite (u64 inviterGuid, u32, player,
   player, u32). [P: FUN_142de3e50 reads 4 u32 then FUN_142ddcd70 at +0x28; 0.589 FUN_142d45bf0 the same; the
   server's own HandleJoin already reads the response at offset 8, i.e. the 4-u32 layout]

No sub-number mismatch was found: every InGamePurchase, Mail, Chat, Command, Groups, Guild, Achievement, Rating, Trade
and Grief constant in use equals the 0.695 dispatcher case (InGamePurchase 0x31..0x4E, Mail 5..0x16 after the renumber;
the PlayerUpdate subs 0x1C / 0x35 / 0x49 / 0x57 / 0x77 go out as u32).

## 3. Open / unverified (not written off)

- **Encounter S→C bodies** (subs 2, 3, 6, 0x6A, 0x6B, 0x72, 0x78, 0x79): they are read behind ClientCommandProcessor
  vtable +0x18 (FUN_1433bd040), a jump table Ghidra did not decode. Next step: disassemble the table at the
  `switchD_1444bff56` reference (re/tools/subdispatch.py style) and pkt each case reader. Until then this is UNKNOWN.
  Duels were never run with two clients on 0.695 (TASKS line ~1088).
- **Achievement Initialize / Add records** are bytes from a 0.589 capture (Resources/PacketDumps/71-AchievementBase).
  The wire class is "same", but any goal content that changed in 0.695 is not reflected (content, not layout) [I].
- **Item record** (Mail attachment type 2, Trade 0xB) uses InventoryItem.WriteRecord against FUN_142c208e0. It is
  checked by the inventory group, and spot-checked here (seconds-since-cast i32, two flagged sub-blocks, metadata, u32).
- **ModifyScreenshots C→S** (0x45/0x4A): its serializer was not located in 0.695. Field order is from the 0.589
  builder and play-tested there [I].
