# Shared social service (Phase B item 1) — spec

Agent #12, 2026-10-15. Topic: friends, ignore, guilds, chat (General / tells / guild / officer / party / custom rooms),
mail (incl. the mail ↔ zone attachment hand-over), presence (who is online, on which world / zone) across worlds.
Follows trace/architecture/internal-transport.md (the Phase B contract). Addresses: **0.589** (re/decomp_all) unless
marked; 0.695 equivalents from trace/0695/coverage-695.md §10. "Traced" = proven by the client or a dev post;
"inference" = deduced, not proven; "ours" = our design (the client never sees it). Scratch: re/out/agent_social/
(mail_disp_589.txt, friend_disp_589.txt, mail_compose_*.txt, mail_inbox_row_589.txt, ptrtable.py,
mail_result_names_589.tsv).

## 1. Summary

- **What live had (traced).** Friends, guilds, ignore and chat-room *membership* went on the client's **World**
  channel; chat *lines*, quick chat and **all mail requests** went on the **Zone** channel (channels-589.tsv; L7). The
  social state was shared by every world: General chat "includes all Servers and Landscapes" (wiki Chat), friends and
  guildmates are seen online and located across worlds (P0554, P1679, P4905, P4907), custom chat channels survive a
  change of world (P5841, 16 Jun 2016), and mail had its own service with its own "service unavailable" result codes
  (client table 0x14521a980: codes 3, 0x13, 0x15, 0x17, 0x18, 0x1B). The mail service and the zone talked with
  ConsumeAttachmentZoneRequest / Response (0xF8/0x11-0x12), ComposeRequestStage2 / Stage3 (0x2 / 0x3) and
  ComposeResponseReturnAttachments (0x5) — names only (L8 / L48).
- **Emulator today.** Six static classes in the world process (Friends, Ignores, Guilds, Chat, ChatRooms, Mail),
  data in the shared `lmemu_character` database, every "who is online" answer from that process's own PlayerManager.
  Characters are already shared between worlds (one global u64 character guid), but each world caches guilds,
  rooms, ignore lists and online state on its own, so nothing social crosses worlds live (docs/docker.md "How the
  world split works").
- **The service (§5).** One container `social` owning its own database `lmemu_social`, holding presence for every
  world, receiving the social World-channel families from the gateways and the social Zone-channel traffic from the
  zones, and delivering to players through each world's gateway with TunnelPacketToExternalConnection(List) (one
  frame per gateway per message). Zones keep what needs position (say / shout / emote range, party) and the
  inventory side of mail; the service keeps the lists, guilds, rooms, mailboxes and presence.
- **New open leads:** L64-L67 (guild mail addressing; public numbered channels; friend presence / position fields and
  friend groups; /who, pending-mail notification and chat-service-down texts). Existing L48 covers the internal
  message layouts.

## 2. Existing research used

| File | What it already settled (not re-traced here) |
|---|---|
| trace/systems/social.md | Status per family; chat channel ids (ChatChannels.txt row − 1) and chat commands FUN_1432ac6b0; guild hidden permissions (JoinOfficerChat / MuteGuildChat / MuteOfficerChat, FUN_142e5c120, FUN_142e5ae70); client constants (Mail 30-day expiry, subject 128, body 4096; chat 512 chars, 10 rooms, room name 32; Guild.MaxMembers 250); wiki live behaviour (tabs, numbered channels /1-/4, /join, /who). |
| trace/families/0013_Chat.md, 004D_FriendUpdateExtraInfos.md, 0072_IgnoreList.md, 0083 / 0084 Guild*.md, 00F8_Mail.md, 004B_AddAcquaintance.md, 001A_PacketTargetClientNotOnline.md, 0046_QuickChatSendData.md, 009C_Social.md, 001E_Command.md | Per-message readers / writers / builders and server status (generated). 0x84 "GuildDisband" is the same 0x83 dispatcher FUN_142e539f0 (generator split). |
| trace/0695/coverage-695.md §10 | Every social sub 0.589 → 0.695 with readers in both builds and the emulator file:line; Mail ≥ 4 renumbered +1 (sub 4 DeveloperMailPlayer inserted); all other social families unchanged. |
| trace/0695/packets-695.md §A3, §B8 | Mail renumbering detail; Chat DebugChat (sub 3) gained a trailing bool. |
| trace/architecture/channels.md, channels-589.tsv / -695.tsv, architecture-leads.md L7 / L8 | Channel per message (World: friends, guilds, ignore, room join / leave, ChatChannelOn / Off, AFK, admin friend packets; Zone: PacketChat, quick chat, mail incl. compose); L8 inventory of server-only mail ↔ zone ids. |
| trace/FAMILIES.md, trace/managers.md, trace/server-only-data.md | Family index; managers ClientQuickChatManager, ClientSocialProfileManager, SocialShare; Friends.txt (UI data source, no server table), Social* tables are SocialDataManager (profile / mini-games, #15). |
| LandmarkEmu/docs/protocol.md §10, §13, gameplay.md "Social" | Wire layouts as built (PacketChat, TellEcho, FriendBase, GroupsBase, GuildBase); claim records carry default masks for everyone / friends / guild mates and a per-guild special-access list. |
| MODLOG.md (Chat 2026-10-08, Friends 10-09, Guilds 10-09, MAIL 10-10, MAIL ATTACHMENTS 10-10, COD) | Verified behaviour of today's server (two-client chat, friend list / online, guild window, mail compose / read / attachments / COD end to end). |
| trace/0695/ui-trace-695.md, functions-695.md §9 | 0.695: chat tokenised rich text (L54), leader rank editable, chat timestamps client-side; mail / guild functions only recompiled. |

## 3. Client evidence

### 3.1 Families, channels, readers / writers

C→S rows give the channel the client sends on; S→C rows the reader (0.589 / 0.695) and handler. "Emulator" =
today's handling (file:line in §4).

**Chat 0x13 (u16 sub; dispatcher FUN_1432b7bc0 / 0.695 FUN_143364980)**

| Sub | Dir | Class | Channel | Client 589 / 695 | Expects / notes | Emulator |
|---|---|---|---|---|---|---|
| 1 | C→S | PacketChat | Zone (9 send sites, L7: FUN_143367ce0 … / 0.589 writer FUN_1432a93e0) | — | u16 kind, u16 channel, u64 from, u64 to, 2 name blocks, message, vec4 position, string, u32 × 3, u8 × 2, [room name for channel 10] | Chat.Handle |
| 1 | S→C | PacketChat | — | FUN_1432aed20 FUN_1432a9b80 / FUN_14335bc40 FUN_143356a90 | kind 0 line; 2 "User '[target]' not found." (UI.ChatPacketInvalidTarget); 3 "User '[target]' is offline." (Chat.WhisperTargetOffline); channel 7 → notification manager | sent |
| 2 | S→C | ChatPacketEnterArea | — | FUN_1432ab430 / FUN_143358340 | not sent | — |
| 3 | S→C | ChatPacketDebugChat | — | FUN_1432a9800 / FUN_1433566e0 (+ bool) | not sent | — |
| 4 | S→C | ChatPacketFromStringId | — | FUN_1432a9950 / FUN_143356860 | text-id lines (emotes) | Emotes |
| 5 | S→C | TellEchoPacket | — | FUN_1432aa700 / FUN_143357610; handler FUN_1432c0f90 | {u64 target, name, message, bool} "To [X]: …" | sent |
| 7 | S→C | PlayerPublicChatCreateRoomResponse | — | FUN_1432a9e90 / FUN_143356da0 | not sent | — |
| 8 | S→C | PlayerPublicChatRoomInviteNotification | — | FUN_1432aa3c0 / FUN_1433572d0 | not sent (no invite UI found) | — |
| 9 | S→C | PlayerPublicChatRoomInfoNotification | — | FUN_1432aa2e0 / FUN_1433571f0; room manager FUN_14357d8d0 | {room, n × {u64, name}, u32 kind 1 entered / 2 joined / 5 left / 7 destroyed / 8 owner} | sent |
| 0xA | C→S | PlayerPublicChatJoinRoomRequest | **World** (FUN_1432aaf70(..,1,1)) | FUN_1432aaf70 / FUN_143357e80 | {string room, string password} | ChatRooms |
| 0xB | S→C | JoinRoomResponse | — | FUN_1432aa140 / FUN_143357050 | {room, u32 result} (1 ok, table FUN_143ab0000…) | sent |
| 0xC | C→S | PlayerPublicChatLeaveRoomRequest | **World** (FUN_1432baa60 → FUN_1432ab120(..,1,1); missing from channels-589.tsv, checked here) | FUN_1432ab120 / FUN_143358030 | {room, bool}; Lua RequestCloseChatTab | ChatRooms |
| 0xD | S→C | LeaveRoomResponse | — | FUN_1432aa210 / FUN_143357120 | {room, u32 result} | sent |
| 0xF | S→C | DestroyRoomResponse | — | FUN_1432a9f60 / FUN_143356e70 | not sent | — |
| 0x11 / 0x13 | S→C | TransferOwnership / InviteUser responses | — | FUN_1432aa4a0 / FUN_1432aa030 | used for result texts | sent |
| 0x14 | S→C | PlayerSendChatHistoryPacket | — | FUN_1432aa5a0 / FUN_1433574b0 | {string, list FUN_1432aac10, u32}: history of a channel / room — not sent (useful after a world change) | — |

**Quick chat 0x46**: S→C QuickChatSendDataPacket (FUN_143298c40; sent at ready, MiscHandler.cs:221). C→S
QuickChatSendChatToChannelPacket (FUN_143290f30, hints 0xff / 8) and QuickChatSendTellPacket, both **Zone**
(channels-589.tsv): no server handler today; family / sub of the C→S pair not proven (hints only) → L67.

**Friends 0x4D (u8 sub; ClientFriendsDataManager dispatcher FUN_143493360 / 0.695 FUN_143543050)**

| Sub | Dir | Class | Client 589 / 695 | Layout / client behaviour | Emulator |
|---|---|---|---|---|---|
| 0 / 8 | S→C | FriendMessagePacket | FUN_14348c3a0 / FUN_14353c090; handler FUN_143492e90 | {u8 code, u64, name, name}: 0x0C invalid name | Friends.cs:86 |
| 1 | S→C | FriendListPacket | FUN_14348d770 → list FUN_14348cf60 / FUN_14353d460 | u32 n × FriendData (FUN_14348c040): name, u64 guid, bool online; offline u64 last-online; online 4 × u32, bool (false → 3 × u32), u32, bool, 5 × u32 (FUN_14348c770); trailing string (+0x108) | sent |
| 2 | S→C | FriendOnlinePacket | FUN_14348c520 / FUN_14353c210 | {string name, u64 guid, bool}; handler **FUN_143493150 prints UI_FriendOnline only if the guid is not in the "recently went offline" map** | sent |
| 3 | S→C | FriendOfflinePacket | FUN_14348d8a0 / FUN_14353d590 | {u64}; handler **FUN_143490ae0 puts (guid, now) into that map (manager +0x78)** — the client's own debounce for world / landscape changes (fits P1679, P4907) | sent |
| 4 | S→C | FriendUpdateProfileInfoPacket | FUN_14348c990 / FUN_14353c680 | {str, 4 × u32} — meaning L66 | — |
| 5 | S→C | FriendUpdatePositionsPacket | FUN_14348db70 / FUN_14353d860; handler FUN_143493a70 | list {u64 guid, string, bool (+0x114), 3 × u32 (+0x118…)} — map positions, answer to FriendsPositionRequest (L66) | — |
| 6 / 7 | S→C | FriendAdd / FriendRemove | FUN_14348d6b0 / FUN_14348d9d0 | {FriendData} / {u64} | sent |
| 9 | S→C | FriendStatusPacket | FUN_14348c850 / FUN_14353c540; handler FUN_143493e80 | {name, u64 guid, 5 × u32} copied over the friend's status block (+0x128…+0x138) — L66 | — |
| 0xA | S→C | FriendRenamePacket | FUN_14348c6a0 / FUN_14353c390; handler FUN_143493db0 | {name, u64 guid}: renames the row (live renames existed) | — |
| 0xB | C→S | FriendUpdateExtraInfos | writer FUN_14348d3b0 / FUN_14353d0a0, built by FUN_143498ce0 | **World**; HashListMap<IString, Friends::FriendExtraInfoEntry>: u32 n, n × 3 strings (key = friend guid formatted, the friend's group from ClientFriends.MoveFriends / CreateGroup …) — client-side friend groups the server is expected to store (round trip L66) | logged only (InteractionHandler.cs:132) |

**Command 0x1E subs (u16; World unless said)**: 0x10 AddFriendRequest {string, u64} (Ui.AddFriend / AddFriendByGuid
FUN_14351d8b0 / FUN_14351da30), 0x11 AddSteamFriendRequest, 0x12 RemoveFriendRequest {name} (FUN_14351df90 /
FUN_14351e410), 0x13 ConfirmFriendRequest (S→C, reader FUN_1432d55d0) / 0x14 ConfirmFriendResponse (lists are
one-way; unused), 0x18 FriendsPositionRequest (Ui.GetFriendPositions FUN_1435189e0), 0x20 IgnoreRequest {u64, name,
u8} (Ui.IgnorePlayer… FUN_14351fcc0 / FUN_143520cd0 / FUN_143521620 / FUN_143521990 / Block FUN_143521d80 /
Unblock FUN_143513270), 0x22 / 0x23 ChatChannelOn / Off {string channel} (Ui.ChannelOn FUN_143521850 / ChannelOff
FUN_143521b30), 0x24 RequestPlayerPositions / 0x25 reply (FUN_1432d63c0), 0x3B AFKRequest, 0x15 SetChatBubbleColor
(**Zone**), 0x4CF / 0x4D0 / 0x4D1 / 0x4D2 admin ForceAdd / ForceRemove friend, FriendsListRequest / Reply (World,
L7 FUN_1433a5e60). 0x2 WhoReply (reader FUN_1432d6c80, WhoData FUN_1432d8e50 {string name, i8, u32, u32, u32, u64};
shown as "%d %s" with UI_WhoCommandPlayersFound in FUN_14330f560); **0x1 WhoRequest has an id but no client class**
(RTTI has only CommandPacketWhoReply) → /who is a server (dynamic) command (L67).

**Ignore 0x72 (u8 sub; dispatcher FUN_143494180)**: 1 List {u32 n × {u64, string}}, 2 Add (FUN_14348dca0 /
FUN_14353d990), 3 Remove. The client keeps the list (requirement ClientRequirementIsOnIgnoreList).

**Guild 0x83 (u16 sub; dispatcher FUN_142e539f0 / 0.695 FUN_142ef2780), all C→S on World**: 1 Create, 2 Disband,
3 Rename, 4 Invite, 5 InviteReply, 6 InviteRevoke, 7 / 8 / 9 Petition / Reply / Revoke (applications; GuildGameData
OpenToApps), 0xA ChangeMemberRank, 0xB Motd, 0xC Kick, 0xD Quit, 0xE UpdateRank, 0xF DescriptionUpdate, 0x10
RequestMutePlayer, 0x11 UpdateAppDataField. S→C: 0x12 GuildDataFull (FUN_142e45a90), 0x13 GuildData (FUN_142e49860),
0x14 Invitations (FUN_142e462b0), 0x15 AddMember, 0x16 RemoveMember, 0x17 UpdateInvitation, 0x18 MemberOnlineStatus
{u64 guild, u64 member, u8}, 0x19 Notification (texts table 0x1451356d0), 0x1A / 0x1B Add / RemoveLogUpdate (guild
news; P5312 "Guild News should now persist the name of players who are deleted"), 0x1C UpdateAppData. All "same" in
0.695. Cross-world fields: each member carries a **MemberGameData XML** parsed by FUN_143b92760 — `LastOnline`,
`LastZone`, **`LastWorld`** (name text ids; the roster's Location column shows "world:zone"), `LastContinent`,
`BattleRank` (Guilds.cs:1015). Dev posts: P4905 "Guild list should now correctly display a new guild member's
location even if they are on a different world when they join"; P4620 last online date. Notifications 0x01 type 6 =
GaqItemGuildInvite (Notifications.cs:10-20).

**Mail 0xF8 (u32 sub; dispatcher FUN_1432c9770 / 0.695 FUN_1433765a0), all C→S on Zone**

| 589 → 695 | Dir | Class | Client 589 / 695 | Notes |
|---|---|---|---|---|
| 1 → 1 | C→S | ComposeRequest | FUN_143583b50 / FUN_143637350; filled by FUN_143588240 | u64 **ToGuid** (+0x18; ClaimFeedback sets it to the site owner, ClaimFeedbackDocumentClass.as:155), To names split on space / ';' / ',', CC list, subject, body, attachments |
| 2, 3 | zone ↔ mail | ComposeRequestStage2 / Stage3 | id only (L8) | server-only |
| 4 → 5 | S→C | ComposeResponse | FUN_1432c8190 / FUN_143374fc0; UI FUN_14358ad00 → Lua OnSendEmailResponse | u32 result (+ name for 1 / 0xD) |
| — → 4 | ? | DeveloperMailPlayer | id only (0.695) | inference: admin / dev mail to one player |
| 5 → 6 | mail → zone | ComposeResponseReturnAttachments | id only | server-only |
| 6 → 7 / 7 → 8 | C→S / S→C | InboxListRequest / Response | FUN_1435829e0 / FUN_1432c75d0 | first u32 of the response: the debug printer treats **0x13 as "service unavailable"** (case 7) — inference that it is a result field (Mail.cs writes 0) |
| 8 → 9 / 9 → 0xA | | ReadMessage | FUN_143582b80 / FUN_1432c77e0 | result 0x14 not found |
| 0xA → 0xB / 0xB → 0xC | | DeleteMessage | FUN_143582830 / FUN_1432c7480 | |
| 0xC → 0xD / 0xD → 0xE | | ClearInbox Request (id only) / Response FUN_1432c80e0 | | |
| 0xE → 0xF | S→C | NewMailInboxNotification | FUN_1432c8490 | |
| 0xF → 0x10 / 0x10 → 0x11 | | ConsumeAttachment Request / Response | FUN_143582630 / FUN_1432c73a0 | |
| **0x11 → 0x12 / 0x12 → 0x13** | mail ↔ zone | **ConsumeAttachmentZoneRequest / Response** | id only (L8) | the hand-over of attachments into the zone's inventory |
| 0x13 → 0x14 | S→C | EnableWindow | FUN_1432c8360 | |
| 0x14 → 0x15 / 0x15 → 0x16 | | AttachItemToCompose | FUN_1435821d0 / FUN_1432c70e0 | |
| 0x16 → 0x17 | ? | System | id only | inference: system mail injected by a server |

**Mail result codes** (FUN_143bc34b0 → key table 0x14521aa90, FUN_143bc34d0 → names 0x14521a980; dumped from the
exe with re/out/agent_social/ptrtable.py → mail_result_names_589.tsv; UI text via FUN_143584fd0):
0 Success; 1 unable to find user (Mail.Error.Compose.UserNotFound 562279); 2 self with attachment; **3 mail service
unavailable (Mail.Error.Compose.ServiceUnavailable, 562281 "Mail service is unavailable. Please try again later.")**;
4 insufficient space; 5 attachment invalid; 6 insufficient funds; 7 item no longer exists; **8 "you're not near a
mailbox"**; 9 item definition NULL; 0xA NO_TRADE; 0xB multiple recipients with attachment; 0xC too many attachments;
0xD inbox full; 0xE sent too many; 0xF ignored by target (no UI key: debug name shown); 0x10 item must be on player;
**0x11 "player tried to send guild mail and is not in a guild"** (564049); 0x12 chest with items; **0x13, 0x15, 0x17,
0x18, 0x1B mail service unavailable (Mail.Error.Inbox.ServiceUnavailable 562285)**; 0x14 / 0x16 / 0x19 message not
found; 0x1A attachment not found; 0x1C inventory full; 0x1D already consumed; 0x1E currency full; 0x1F must pay COD
first; 0x20 insufficient funds for COD; 0x21 something went wrong. Inference from the grouping: 0x13 inbox, 0x14/0x15
read, 0x16/0x17 delete, 0x18 clear, 0x19-0x1B consume.

**Mail kinds** (MailData +0x1d0, FUN_143585ac0): 0 player; 1 / 2 / 4 / 8 / 0xF0 / 0x1000 system kinds; **0x100 guild
mail** — sender "[Guild] [*GuildName*]" (Guild.MailFrom 564046) from the guild id in the MailData's second u64,
body prefix "Guild message sent from: [*PlayerName*]" (Guild.MailBody 564047) (FUN_143589e50); 0x800 FollowManager
digest (Player Studio follow mail, #15); 0x2000 / 0x4000 / 0x8000 sender / subject / body are text ids. Other client
mail texts: Mail.SentBack.* (returned mail, FUN in part_0114 ~3794), Mail.CashOnDeliveryPayment.*,
Claim.UpkeepMail*, Mail.GAQ.Single/PluralMessages ("You have [*count*] message(s)", FUN_14332b9a0 — the pending-mail
notification, L67). How a player addressed a guild: L64.

**Gateway-level and other**: PacketTargetClientNotOnline 0x1A (reader FUN_142bb13b0, zone dispatcher; not sent —
candidate answer for a tell / invite to someone offline, L67). Acquaintances (recently met): S→C 0x4B 1 Add / 2 Remove
FUN_142f269c0 / 3 Online FUN_142f24f70 {u64, bool}, plus a list in SendSelfToClient (SendSelfToClient.cs:1846).
Voice (Vivox) 0xB9: out of scope.

### 3.2 What the client expects across worlds (traced)

| Behaviour | Evidence |
|---|---|
| General chat reaches every world and landscape | wiki Chat ("worldwide chat that includes all Servers and Landscapes") |
| General is a joinable "custom" channel with server-kept membership | P3672 "custom chat channel (such as General)"; P1826 / P1893 General unusable after logging out in it; P4311 "General chat channel will now work for everyone, not just admins or the first person in"; P5310 General can be disabled in Chat Options (→ ChatChannelOff) |
| Custom rooms survive a world change | P5841 |
| Friends / guildmates online across worlds, no offline / online message on a world or landscape change | P1679 (2014), P4907 (2016); client debounce FUN_143490ae0 / FUN_143493150 |
| Teleport to a friend on another world | P0554 (2014: "friend who is not on the same island or world"); Ui.TeleportToFriend → ability 187 (Abilities.cs:406) |
| Guild roster shows each member's world and zone, also for members on another world | MemberGameData LastWorld / LastZone (FUN_143b92760); P4905 |
| Guild / officer channels follow the character, not the world | P5311 |
| Mail can fail on its own ("service unavailable") while the game runs | result codes 3 / 0x13 / 0x15 / 0x17 / 0x18 / 0x1B; live-server-architecture.md |
| Guild mail existed | result 0x11, kind 0x100 (L64) |
| Tells to offline characters answered | PacketChat kind 3; P4933 "Chat should not create a new tab if you try to send a tell to a character who isn't online" |

Not found yet: whether tells and room lines crossed worlds (inference: yes — same channel family as General), the
public-channel wire identity (L65), the friend presence fields (L66).

## 4. Emulator today (LandmarkEmu/LandmarkEmulator.WorldServer unless said)

| Module | Reads / writes | Online state | Notes |
|---|---|---|---|
| Game/Friends.cs | `character_friend` via CharacterDatabase.GetFriends / GetFriendOf / FindCharacterByName / GetCharacterById (Database.Character/CharacterDatabase.cs:198-232); `character.LastOnline` | PlayerManager.Snapshot() of this process (:164) | Enter :45 (list + Online to watchers :57-59), Leave :63, HandleAdd :74 (MaxFriends 200), HandleRemove :126; FriendData online fields all zero (:186-208) |
| Game/Ignores.cs | `character_ignore` (NewContext :40, Save :144 / :155) | in-memory dictionary per online player (:34) | Ignoring :64, Refuses :75, Handle :85 (MaxIgnored 100) |
| Game/Guilds.cs | `guild`, `guild_member` (ranks JSON in guild.ranks), names from `character` | whole roster cached once per process (EnsureLoaded :147), invites (:141) and mutes (:229) in memory only | ids per world range when worlds share the DB (:397-400, GuildIdRange 2^20); MemberGameData from this process's sessions or `character.ZoneId` / `LastOnline`, LastWorld = this world's name text (:1015-1035) |
| Game/Chat.cs | `character` (name exists check for tells :193) | PlayerManager | Handle :59: flood limiter, server-only channels dropped, mute check :91, recipients :99-103 (Party → Groups, 5 / 6 → Guilds, 10 → ChatRooms, everything else incl. **General (9)** → every player of this process), ignore filter :108, range :106; SendTell :185; NpcSay :124; Chat.System / SystemChannel used by ~every module |
| Game/ChatRooms.cs | none (memory) | room member lists of WorldSession (:43-52) | Join :82, Leave :118, Forget :147 (on logout), MembersFor :171; ChatChannelOn / Off (0x22 / 0x23) unhandled |
| Game/Mail.cs | `mail` (attachments as "definition:count", Kind) | PlayerManager for NewMail notifications | Handle :85; Compose :183 (ignore check :262 only for online recipients, bag checks :286-310, coins :320, TakeItem :326); TakeAttachments :497 (ChangeCoins :520, GiveItem :545); SendSystemMail :574; PayCod :604; Attach :626; HasAttachment :462 |
| Database | `CharacterContext.cs` mail :395, guild :708, guild_member :760, character_friend :812, character_ignore :831 — all in the shared `lmemu_character` | | docs/docker.md:133-148 |

**Zone code that depends on them** (each becomes a service call or a pushed cache):

| Caller | Dependency |
|---|---|
| Game/PlayerManager.cs:92-96, 239, 249-251 | Friends / Ignores / Guilds Enter + Leave, ChatRooms.Forget — becomes the gateway's character-entered / left events |
| Network/Handlers/InteractionHandler.cs:57 (mailbox prop → Mail.OpenFor), :88 / :91 (friend add / remove), :102 (ignore), :132 (FriendUpdateExtraInfos logged), :170 (guild) | request routing |
| Network/Handlers/ChatHandler.cs:20-23 | PacketChat and room requests |
| Network/Handlers/MiscHandler.cs:104 (mail), :221 (quick chat data at ready) | |
| Game/Emotes.cs:186, Game/Trades.cs:88, Game/Groups.cs:164 | ignore checks (zone needs the listener's / invitee's ignore list) |
| Game/Chat.cs:91-108 | guild mute, guild / officer / room / party recipients, ignore |
| Game/Claims.cs:784-790 | claim record default masks for everyone / friends / guild mates (client +0x2d0 / 2d4 / 2d8; permission check FUN_143b5b480 / FUN_142d044a0 / guild list FUN_142d04a20) — the server writes the same mask three times and has no guild list; real friends / guild masks need friend-of and guild-of answers |
| Game/Claims.cs:1184-1212 | Special Access by name "Player or Guild could not be found" (result 0x17) — needs guild name → id |
| Game/SiteContents.cs:50 | Mail.HasAttachment (an item still waiting in mail counts as owned) |
| Game/SiteUpkeep.cs:61 / 69, Game/Vendors.cs:251 / 340, Game/Commerce.cs:596 | Mail.SendSystemMail (upkeep, vendor sales / returns, Player Studio kind) |
| Game/Notifications.cs:29 | guild invitation popup (called from Guilds :180 / :460) |
| Game/Achievements.cs:780 | GuildJoined progress (called from Guilds :415 / :494) |
| Game/Abilities.cs:406, MiscHandler.cs:367 | teleport to friend (ability 187) — needs the friend's world / zone / position |
| Game/DevCommands.cs:338, :1369 | dev guild commands |
| Game/Groups.cs:82 | party chat recipients (Groups stay zone-side) |

## 5. Service spec

### 5.1 Responsibilities

The `social` container owns (one instance for all worlds):
1. **Presence**: every online character — world id, gateway id, connection id, client build, zone id, AFK flag,
   guild id; last world / zone / online time for offline ones. Fed by the gateways' connection events and the zones'
   zone-change notes.
2. **Friends** (one-way lists, friend extra infos / groups), **ignore lists**, **acquaintances** (later).
3. **Guilds**: roster, ranks, invites (persisted), mutes, MOTD, description, app data, guild log, petitions.
4. **Chat routing** for everything not range-based: tells (+ TellEcho, offline / not-found kinds), guild (5),
   officer (6), Public channels (9: General / Trade / … — L65), player rooms (10), room membership, chat-channel
   on / off; ignore filtering on the receiving side; mute; flood limit per sender for the channels it routes.
5. **Mail**: mailboxes, compose validation of recipients, attachments in escrow, COD, expiry / return-to-sender,
   system mail from zones and other services, guild mail (L64).
6. Admin: AdminCommand friends packets (0x4CF-0x4D2), dev commands for guilds.

Zones keep: say (0) / shout (3) / emote (4) (range), party (8) and Groups (moved to the service 2026-10-16 when it is
remote: §10), the inventory / coin side of mail
(escrow on compose, delivery on consume), mailbox proximity (result 8), claims permissions (with cached friend /
guild answers), teleports.

### 5.2 Its own database `lmemu_social` (characters identified by the global character guid)

Characters are already global: `lmemu_character.character.id` (u64) is unique across worlds and `LastServerId` names
the last world. The service never reads `lmemu_character` after migration; it keeps a directory of its own.

| Table | Columns (key) | From today |
|---|---|---|
| social_character | guid (PK), account_id, name (unique, case-insensitive), original_name, deleted_at, last_world, last_zone, last_online | copy of `character` (id, accountId, name, originalName, deleteTime, lastServerId, zoneId, lastOnline); kept up to date by CharacterCreated / Renamed / Deleted (§5.4) and presence |
| friend | (character_guid, friend_guid), added_at | character_friend |
| friend_extra_info | (character_guid, key), value1, value2 | new (FriendUpdateExtraInfos, L66) |
| ignore_entry | (character_guid, ignored_guid), name | character_ignore |
| guild | id (PK), name (unique), motd, leader_guid, created_at, description, app_data, ranks (JSON) | guild |
| guild_member | character_guid (PK), guild_id, rank_id, joined_at | guild_member |
| guild_invite | invitee_guid, guild_id, inviter_guid, inviter_name, created_at | new (today memory only) |
| guild_mute | (guild_id, channel, member_guid), until | new (today memory only) |
| guild_log | id, guild_id, at, kind, actor_guid, actor_name, target_guid, target_name | new (0x1A / 0x1B, names kept after deletion: P5312) |
| chat_membership | (character_guid, channel_name), kind (public / room), joined_at | new (General opt-out P5310; rooms across world change P5841) |
| chat_room | name (PK), password, owner_guid, created_at, persistent | new (today memory only; persistence across restarts not proven — keep rooms while members exist, ours) |
| mail | id, recipient_guid, sender_guid, sender_name, subject, body, sent_at, is_read, kind, guild_id (kind 0x100) | mail (minus `attachments`) |
| mail_attachment | (mail_id, slot), type (item / coins / cod), item_definition, count, item_record (blob, for items that carry data such as Player Designs), taken | parsed from mail.attachments "definition:count" |
| mail_send_log | sender_guid, at | new (today memory, MaySend window) |

### 5.3 Gateway-tunnel messages (contract §2)

**Received** (TunnelPacketFromExternalConnection, client bytes in 0.589 numbering):
- World channel: Friend 0x4D sub 0xB; Command 0x1E subs 0x10, 0x11, 0x12, 0x14, 0x18, 0x20, 0x22, 0x23, 0x3B (AFK),
  0x4CF-0x4D2; Guild 0x83 (all); Chat 0x13 subs 0xA / 0xC. **Routing hand-off to #13 / #15:** the gateway sends a
  channel to one back end; the World back end is #15's service. Either the gateway routes World-channel families
  0x4D, 0x83, 0x13 and those 0x1E subs to `social` (a family override table, ours), or #15 forwards them. Proposed:
  the gateway's override table (one hop, social can restart on its own).
- From zones (not from the gateway): Zone-channel PacketChat for channels 1, 5, 6, 9, 10, quick chat tells /
  channels, and all Mail 0xF8 requests, forwarded as ServicePackets (§5.4) — live sent these to the zone first.

**Sent** (back end → gateway; the gateway does the 0.695 Mail renumbering per connection):
- TunnelPacketToExternalConnection for one player (FriendList, IgnoreList, GuildData + ranks, Mail replies, room
  results, FriendMessage, guild notifications).
- **TunnelPacketToExternalConnectionList 0x16** for fan-out, **one frame per gateway** with every destination on
  that gateway (FriendOnline / Offline to watchers, MemberOnlineStatus, guild / officer / room / General lines, room
  info). Bandwidth: never one frame per recipient; General lines coalesce per gateway; presence notes are debounced.
- Connection events consumed (contract §2.4, ours): CharacterEnteredWorld {conn id, char guid, name, world id,
  gateway id, zone id, client build}, CharacterLeftWorld {conn id, char guid, reason}, and on (re)connect a
  PresenceSnapshot (every connection on that gateway) so a restarted social service rebuilds presence.

### 5.4 Service-to-service messages (ServicePacket = u16 family, u32 sub, body; layouts ours, L48)

Client names where they exist; "(ours)" otherwise. Directions: Z = zone (world server today), S = social, G = gateway,
L = login / character service, W = other world-level services (#15 commerce, #16 Environment Server).

| Message | Family / sub (0.589) | Dir | Body (ours) | Purpose |
|---|---|---|---|---|
| ZoneChanged (ours) | 0x1E / ours | Z → S | char guid, world id, zone id | presence location (guild LastZone, friend status) |
| PacketChat forward | 0x13 / 1 | Z → S | conn id + sender guid (zone-verified) + the client body | tells, guild, officer, public, rooms; S fills the real sender, filters ignore / mute, fans out |
| QuickChatSendTell / ToChannel forward | 0x46 / (L67) | Z → S | same | quick chat |
| ChatDeliver (ours) | 0x13 / ours | Z → S | list of char guids + PacketChat body | let a zone send a line to players it doesn't host (party members on other landscapes when zones split) |
| IgnoreListSync (ours) | 0x72 / ours | S → Z | char guid, full list / delta | zone-side checks (say range, emotes, trade, group invites); pushed on enter and on change |
| SocialStateForZone (ours) | 0x4D / ours | S → Z | char guid, guild id, rank permissions, friend-of list (who lists this character) | claims friends / guild masks, guild tag, achievements; pushed on enter and on change |
| GuildJoined (ours) | 0x83 / ours | S → Z | char guid, guild id | Achievements.GuildJoined, guild tag |
| GuildNameToId Request / Response (ours, modelled on Claims NameToGuid 0xF9/0x54-0x57) | 0x83 / ours | Z → S → Z | name → guild id | claim Special Access by guild name |
| CharacterNameToGuid / GuidToName (ours; Claims 0x54-0x57 are the traced siblings) | 0x1E / ours | any → S | name ↔ guid, deleted flag | lookups for zones and other services (S answers from social_character) |
| PresenceQuery (ours) | 0x4D / ours | Z → S | char guid | world / zone of a friend or guildmate (teleport to friend 187, cross-world travel) |
| FriendsPositionRequest forward | 0x1E / 0x18 | S → Z (zone of each friend) → S | list of guids | FriendUpdatePositions 0x4D/5 (L66) |
| **PacketMailComposeRequest forward → ComposeRequestStage2** | 0xF8 / 1 → 0xF8 / 2 | Z → S | sender guid, conn id, recipients, ToGuid, subject, body, escrowed attachments (full item records), coins, COD | zone has validated mailbox (8), item ownership (7 / 0x10), NO_TRADE (0xA), chest (0x12) and **removed the items / coins into escrow** |
| **ComposeRequestStage3** | 0xF8 / 3 | S → Z | compose token, result | stored (0): the zone drops its escrow copy; S sends ComposeResponse 0 to the client |
| **ComposeResponseReturnAttachments** | 0xF8 / 5 | S → Z | compose token, result, attachments | refused (1, 0xB, 0xD, 0xE, 0xF, 0x11 …): the zone gives the escrow back; S sends ComposeResponse with the code |
| Inbox / Read / Delete / Clear forward | 0xF8 / 6, 8, 0xA, 0xC | Z → S | conn id, char guid, client body | S answers the client directly through the gateway |
| PacketMailConsumeAttachmentRequest forward | 0xF8 / 0xF | Z → S | conn id, char guid, mail id, attachment id | |
| **ConsumeAttachmentZoneRequest** | 0xF8 / 0x11 | S → Z | token, char guid, mail id, attachment ids, item records / coins / COD to charge | traced name: the mail service asks the zone holding the player to hand over the attachments |
| **ConsumeAttachmentZoneResponse** | 0xF8 / 0x12 | Z → S | token, per attachment result (0, 0x1C inventory full, 0x1E currency full, 0x20 COD funds) | S marks taken (idempotent by token), pays COD by mail, sends ConsumeAttachmentResponse per attachment |
| System mail (ours name from id **cMailPacketIdSystem 0x16**) | 0xF8 / 0x16 | Z / W → S | recipient guid, kind, sender / subject / body (or text ids), coins, item records | upkeep, vendors, Player Studio, reward chests, follow digest (#15) |
| DeveloperMailPlayer (0.695 id 4) | 0xF8 / 4 | admin → S | as System | admin mail (inference from the name) |
| CharacterCreated / Renamed / Deleted (ours; Claims PlayerDeleted 0xF9/0x60 is the traced sibling) | 0x1E / ours | L → S | guid, account, name, old name | directory; on rename send FriendRename 0x4D/0xA to watchers; on delete remove from friends / guilds / ignore, keep names in guild log (TASKS 6.42, P4624, P5312) |

Zone ↔ social for party (Groups): §10 (built 2026-10-16; GroupMemberRow / GroupStateForZone, 0x2B family).

### 5.5 Presence handling

- Source of truth: gateway connection events (+ zone ZoneChanged). One record per character guid; a character
  online on two gateways at once (relog race) keeps the newest connection.
- **World change** = Left on gateway A, then Entered on gateway B. The service holds Left for a grace period (ours:
  30 s, configurable) before it tells watchers Offline; an Entered within the grace sends no Offline / Online, only
  location updates (FriendStatus 0x4D/9 and guild MemberGameData via MemberOnlineStatus / AddMember refresh) — the
  P1679 / P4907 behaviour. The client's own debounce (FUN_143490ae0 / FUN_143493150) is a second line of defence.
- On Entered: send FriendList (sub 1), IgnoreList (0x72/1), GuildData + ranks, room / channel re-joins
  (RoomInfoNotification kind 1 for every room in chat_membership; General per L65), pending guild invitation
  (Notifications type 6), pending-mail notification (L67); push IgnoreListSync + SocialStateForZone to the zone.
- On Left (after grace): FriendOffline to watchers, MemberOnlineStatus 0, room "user left" (kind 5) — but keep
  chat_membership (rooms survive world changes and logouts like General did).
- Last world / zone / online time are written to social_character (guild roster LastWorld / LastZone / LastOnline;
  friends' last-online u64).

### 5.6 Behaviour when the service is down

- **Mail** (the client has texts for it): zones answer mail requests themselves while `social` is unreachable —
  ComposeResponse 3 (Mail.Error.Compose.ServiceUnavailable 562281), InboxListResponse with result 0x13, Read 0x15,
  Delete 0x17, Clear 0x18, Consume 0x1B (Mail.Error.Inbox.ServiceUnavailable 562285; per-operation mapping is the
  inference in §3.1). Escrowed compose attachments are given back at once (no Stage3 within a timeout = refused).
  **Built 2026-10-15** (Game/Social/RemoteMail.cs): compose → 3 with nothing taken; inbox → result 0x13 with no rows
  (the inbox window ignores the result field, FUN_14358a7d0, so the client text 562285 is also sent as a system line);
  read → 0x15 (the client opens the reading window with IsValid 0, as for 0x14); delete → 0x17; take → 0x1B.
  Deviation: a compose already in escrow when the link drops is not given back on a timeout but sent again on
  reconnect (stored once by its token) — giving it back while the service may have stored it would duplicate the
  items; the cost is that a world stopping in that window loses the escrow.
- **World-channel social families**: if the gateway routes them to `social` (§5.3) and it is down, the gateway drops
  them (the client expects no reply to most); ChannelIsRoutable(World, false) must **not** be sent for social alone,
  because the World channel also carries commerce (#15). Hand-off to #13.
- **Chat**: zones keep say / shout / emote / party working; tells and guild / room / General lines fail with a system
  line (no client text for a chat outage found yet — L67). **Built 2026-10-15** (RemoteSocialService.ChatUnavailable,
  a plain system line of ours).
- **Restart**: on reconnect each gateway sends PresenceSnapshot; social re-sends FriendList, GuildData and room info
  to every online player (to verify in test that FriendList replaces the client's list — FUN_1434928e0 / FUN_143497b40
  from FriendList case 1), and zones get fresh IgnoreListSync / SocialStateForZone.

### 5.7 Migration of existing data

1. Back up `lmemu_character` (mariadb-dump --single-transaction) and `server-data/`.
2. Social's first start creates `lmemu_social` (EF migrations of a new LandmarkEmulator.Database.Social).
3. One-time copy (same MariaDB instance; a SQL script run by the service when its tables are empty and
   `SOCIAL_IMPORT_FROM=lmemu_character` is set): social_character ← character; friend ← character_friend;
   ignore_entry ← character_ignore; guild ← guild (ids kept: the per-world 2^20 ranges become irrelevant, new ids
   continue from max + 1); guild_member ← guild_member; mail ← mail (ids kept) and mail_attachment ← mail.attachments
   split on ';' / ':' (definition 0 = coins, 0xFFFFFFFF = COD, as Mail.cs:44).
4. Verify counts per table; world servers switched to the remote social client (`SOCIAL_ADDRESS` set) stop writing
   the old tables; the old tables stay read-only for one release, then a migration drops them.

## 6. Build and test plan (isolated Docker copy; no change to the running dev servers)

Build (each step tested before the next; per-module rule 7: archive tag social-v1 first):
1. **Interface split inside the world server.** `ISocialService` with today's code as `LocalSocialService`;
   Friends / Ignores / Guilds / Chat (routed channels) / ChatRooms / Mail call it; zone-side caches (ignore, friend-of,
   guild id) behind small interfaces. No behaviour change; current tests still pass.
2. **LandmarkEmulator.SocialServer + LandmarkEmulator.Database.Social** (net project, own Dockerfile under
   docker/social, compose service `social`, `.env`: `SOCIAL_ADDRESS=social:20200`, DB `lmemu_social`). Internal
   framing from internal-transport.md (shared library with #13).
3. **RemoteSocialService** in the world server (ServicePacket client, reconnecting), selected by `SOCIAL_ADDRESS`.
4. **Delivery path**: until #13's gateway container exists, each world server accepts TunnelPacketToExternalConnection
   / List frames from `social` and enqueues them to its sessions (it is the gateway today); connection events come
   from PlayerManager Enter / Leave. When the gateway service lands, the same frames go to it unchanged.
5. Move data + logic per area in this order: presence → friends / ignore → guilds → chat routing (tells, guild,
   officer, rooms, General) → mail (two-phase compose, consume hand-over) → system mail callers.
   **Status 2026-10-15:** all areas moved (presence / friends / ignore 8a66bae, guilds 76b3c94, chat f02b095, mail +
   system mail: LandmarkEmulator.Internal/Social/MailMessages.cs has the bodies, SocialServer/MailService.cs the
   mailboxes, WorldServer Game/Social/RemoteMail.cs the zone's escrow / hand-over / read packet). Read answers go
   through the zone (MailReadData, ours) because ReadMessageResponse carries item records only the zone can write —
   so the item_record blob column of §5.2 is not needed while Player Design items stay out of mail (Mail.Attach
   refuses them). mail_send_log is kept in memory (service restart resets the send window).
6. Migration script (§5.7) and down-behaviour (§5.6).

Test (isolated copy, project rule "servers run in Docker", BUILD-PATH rule 11):
- Compose project `landmarkemu-socialtest`, own network, own image tags (`landmarkemu/*:socialtest`), own ports
  (e.g. login 19100, worlds 19110 / 19120, social 19200, DB 3317), DB volume restored from a fresh dump of the dev DB;
  **two worlds** (WORLD_ID 91 / 92) + auth + db + social. Never touches landmarkemu-auth / landmarkemu-world-dev.
- Clients: the patched test copies with re/dlogin.sh (no real input; project rule "test harness, no input"); close clients
  after each run (project rule "PC resource limits"). Verify by the client's reaction (chat log / Lua rpc / UI data
  sources), not server logs.
- Cases: (1) A on world 91, B on world 92: friend add, both see online; B changes world → A sees no offline / online
  line, location updates; (2) tell A→B across worlds + TellEcho; tell to offline / unknown name → kinds 3 / 2;
  (3) General line from A reaches B; Chat Options General off → B stops getting it; (4) custom room joined on 91,
  member changes world → still in room (P5841); (5) guild: create on 91, invite B on 92, accept, roster Location
  shows "world:zone" for both, guild + officer chat across worlds, mute; (6) mail A→B with item + COD across worlds,
  B pays COD and takes item, A gets payment mail; inbox full / spam / ignore codes; (7) stop `social`: compose shows
  562281, inbox 562285, items stay with A; say chat still works; start `social`: lists and rooms come back;
  (8) restart world 91 alone: friends on 92 see A offline after the grace, back online after login; (9) 0.695
  client: mail subs renumbered by the gateway / world (coverage-695 Mail rows); (10) migration of a dev DB copy:
  row counts equal, guild ids / mail ids unchanged.
- Cleanup: `docker compose -p landmarkemu-socialtest down -v`, remove test image tags, delete Frida temp files.
- Log in MODLOG.md, TESTING-NEEDED.md (user checks: General across two worlds, guild roster locations, mail outage
  texts), docs/docker.md (new service), docs/protocol.md §13 (mail result codes, friend debounce).

## 7. Risks

- **Item duplication / loss in mail** across two processes: two-phase compose with escrow tokens, idempotent
  Stage3 / ReturnAttachments / ConsumeAttachmentZoneResponse (dedupe by token), escrow persisted zone-side until
  answered; timeouts return items.
- **Ordering**: a FriendOnline reaching a client before its FriendList, or chat before the room join — social sends
  the per-player bootstrap in one batch before any fan-out for that connection.
- **Presence races** (relog, world change, gateway restart): newest connection wins; grace period; snapshots.
- **Chat latency**: one extra hop (zone → social → gateway) for routed channels; say / shout stay local.
- **Routing ownership**: World-channel family override must be agreed with #13 / #15, or social traffic is lost.
- **Claims semantics** (resolved 2026-10-15): FUN_143b5b480 = owner → special player → special guild → friends mask
  (if flag) → guild mask (if flag) → public; every caller passes both flags 0, and the shipped Site Management UI
  only sets the public mask and player entries (SecurityPanel.as / ClaimsWrapper.as). At launch there was no friends /
  guild default access, so claims need no friend-of / guild answers for permissions.
- **Unproven parts** (L64-L67, L48): guild mail addressing, public channel identity, friend presence fields,
  inbox result field, internal layouts. Keep them behind clearly named stubs; no guessed client-visible behaviour.
- **Single point**: one social service for all worlds — health check + automatic restart; mail fail-safe texts.

## 8. Hand-offs

- **#13 gateway**: route World-channel families 0x4D, 0x83, 0x13 (subs 0xA / 0xC) and 0x1E subs 0x10-0x14, 0x18,
  0x20, 0x22, 0x23, 0x3B, 0x4CF-0x4D2 to `social` (override table); connection events + PresenceSnapshot; 0x16 list
  fan-out per gateway; Mail 0.695 renumbering for frames from `social`; do not mark World unroutable for social alone.
- **#15 world-channel service**: SocialPacket* 0x9C (profile, likes, wall, top scores incl. RequestFriendsGameTopScores)
  and SocialShare 0x104 are yours; Player Studio approval mails, FollowManager digest mail (kind 0x800) and commerce
  mails go through social's System mail message; character-select session / name change (0x1E 0x40 / 0x41) should
  emit CharacterRenamed to social.
- **#16 Environment Server / operations**: scheduled restarts must restart `social` separately from worlds (presence
  survives world restarts); health checks for `social`; reward-chest expiry mails via System mail.
- **Zone (unassigned)**: Groups / party chat moved to the service when it is remote (§10, 2026-10-16); claims friends /
  guild masks and guild Special Access use SocialStateForZone + GuildNameToId; teleport to friend across worlds uses
  PresenceQuery; mailbox proximity (result 8).
- **Login / character data (unassigned)**: CharacterCreated / Renamed / Deleted events (TASKS 6.42).

## 9. Game Finder module (cross-world matchmaking) — built 2026-10-16 (agent; untested in the client)

Patch notes 14 Oct 2016 "Game finder should now work between worlds"; BUILD-PATH decision 2026-10-15 (the 0.695 Game
Finder is a module of this service); message design world-channel-service.md §6.8. Client side unchanged and already
traced: InstancedGameMatchmakingRequest 0x10D / 0x19 and InstancedGameMatchmakingUpdate 0x10D / 0x1A on the **Zone**
channel (trace/0695/packets-695.md §5a / §5b; handler FUN_1434e9a50, flag rows FUN_1434ed2e0). No new client trace was
needed: the 0.695 layouts the local Game Finder (InstancedGames.Finder.cs) already uses are the ones reused.

- **Split.** The zone keeps the client side: it reads 0x19, writes 0x1A with its existing writer (SendMatchmaking; the
  0.695 sub numbering is the zone's ClientWire695), finds matches on its own world (same landscape, not already in
  the game, the game's queue and "Join Now" as before) and publishes its games. The service
  (LandmarkEmulator.SocialServer/MatchmakingService.cs) keeps the searchers and games of every world and computes the
  counts the window shows. Only when SOCIAL_ADDRESS is set (`SocialServices.Matchmaking`); in-process the local Game
  Finder runs unchanged.
- **Messages** (ours, internal-messages.md §2; LandmarkEmulator.Internal/Social/MatchmakingMessages.cs):
  MatchmakingRequest 0x10D / 0x10100 zone → social {u32 world, u64 char, u64 flag mask, bool searching} (a stop
  carries the search's own mask: the client leaves the field unset); MatchmakingGames 0x10D / 0x10101 zone → social
  {u32 world, u32 n, n × {u64 game, u32 zone, u64 flags, u32 players, u32 state}} (whole list per world; flags = the
  client kind bits FFA 2 / Teams 4 / PvE 8 / PvP 0x10 / In Progress 0x40 + ours 0x1_0000_0000 open, 0x2_0000_0000
  queued; state = the client's game state number; disabled games are not published); MatchmakingUpdate 0x10D / 0x10102
  social → zone {u64 char, u32 players, u32 active, u32 waiting, u32 inactive, u32 SearchState, u64 flags, u32
  otherWorld, u64 otherGame}.
- **Counts** (same rules as the local FinderCounts, `MatchmakingMessages.Counts`): Players = searchers on every world;
  Active = games with a round on (state 3-8), Waiting = no round yet and someone in it or queued, Inactive = no round,
  nobody in it or queued — over the games of every world of the searcher's ticked kinds.
- **Updates.** A start is answered at once (SearchState 0), a stop with SearchState 3. While a search runs, its
  counts are re-checked once a second, only after something changed, and sent only when they differ (the client keeps
  the last values). One update per changed searcher, to that searcher's world.
- **Matches.** On the searcher's own world: the zone (TryMatch, SearchState 1, queue) as before; it then takes the
  player out of the service's list (the service's state 3 answer to that is dropped by the zone). On another world:
  the service names the most populated game that would match (`MatchmakingMessages.Matches`: open, of the ticked
  kinds, someone waiting before its round or running with In Progress ticked); the zone tells the player once per game
  with a system line and logs it; nobody is moved and SearchState 1 is not sent for it — how live moved a matched
  player, across worlds or not, is OPEN-LEADS L31.
- **Publishing / bandwidth.** The zone compares its game list on every 2 s game sync and sends MatchmakingGames only
  when it changed, at most every 5 s (PublishInterval); after every (re)connect it sends the list in full and its
  searches again at once.
- **Links.** The service drops a world's games and searches when its link closes, and a character's search when it
  leaves the world (CharacterLeftWorld). While the service is unreachable a zone's searches fall back to that world's
  own counts (the local path) and move back to the service after the reconnect.
- **Not built here:** the 0.589 EncounterMatchmaking 0x8A replies (no 0.589 UI caller, L63) and the cross-world join
  (L31).
- **Test:** TESTING-NEEDED.md 2026-10-16 "Game Finder across worlds" (isolated stack re/test/socialtest, second world
  = FakeWorld `games` / `search` commands). Logic self-test (two in-process worlds against a SocialHub): 9/9.

## 10. Groups across worlds — built 2026-10-16 (agent; TASKS B.9; untested in the client)

Patch note P4906 (hotfix 26 Apr 2016, Social): "You no longer have to be on the same world to remove a group member
from your group" — so live's groups spanned worlds by launch (recreation-design.md §5 #4). Groups were zone-side until
now (§5.1, §8 "Zone (unassigned)"); with the social service remote (SOCIAL_ADDRESS) group membership moves here, the
same way friends / guilds / chat / mail moved. Without SOCIAL_ADDRESS the zone keeps its in-process groups unchanged
(Game/Groups.cs, local path).

**0.695 check (rule "check 0.695 before porting").** GroupsBase 0x2B: all 24 dispatcher cases and every reader "same"
in 0.695 (coverage-695.md lines 94 / 296 and §0x2B: 0.589 dispatcher FUN_142d4c840 → 0.695 FUN_142deaa10). Channels
(channels-589.tsv / -695.tsv): GroupPacketInvite / Join / Leave / Kick / Disband / SetGroupOwner / AcceptTeleport on the
**Zone** channel, MapPing on World — so, like mail, the zone receives the requests and hands them on. Party frame row
fillers traced again for the cross-world rows (0.589): FUN_142d560a0 sets "IsInZone" only when the member record's
+0x100 and +0x108 equal the viewer's own record's (then "Distance" from +0x110 and "InGroupBonusRange");
FUN_142d56350 shows +0x104 as "ZoneName", looks +0x10c up as "WorldNameId" ("InvalidWorld" when unknown) and shows a
non-zero +0xf8 as "IsLinkdead".

**Split.**

| Where | What |
|---|---|
| Social service (LandmarkEmulator.SocialServer/GroupService.cs) | membership (id, leader, members in joining order, max 4), invitations (2 min, RemoveInvitation 3 / 4), invite by name or guid on any world, ignore refusal, Join accept / decline, Leave, Kick, SetGroupOwner, Disband, leader hand-over, "too small" end, party chat audience (channel 8 → ChatService), every GroupsBase packet about these to every member wherever they are |
| Zone (Game/Groups.cs, Game/Social/RemoteGroups.cs) | the member record (health, energy, landscape, position: Groups.MemberRecord) of its grouped players, published on change; the Chaos Cavern group trip (InviteToGroupContent 0x24 / AcceptTeleport 0x26, Caves.cs) for members on the same world; group loot / gathering shares in range (RewardChests, Gathering.NearbyMembers); the Outposts crystal choice per group id; the "grouped" goals |

The client packets are written by one shared builder (LandmarkEmulator.Shared/Game/GroupPackets.cs) used by both the
zone's local path and the service, so they are byte-identical to what Groups.cs sent before (offline check: 45/45,
scratchpad b9_groups_test: every builder against the old Groups.cs code, transcribed from 9bc4c7d).

**Messages** (internal-messages.md §2; LandmarkEmulator.Internal/Social/GroupMessages.cs):
- Client requests handed on: ClientRequestForward family **0x2B**, sub = the client's u8 sub (Invite 1, Join 2, Leave 4,
  Kick 5, Disband 6, SetGroupOwner 8), body after the sub unchanged. AcceptTeleport 0x26 stays in the zone.
- **GroupMemberRow 0x2B / 0x10001** zone → social: u64 character, u32 world, u32 zone, u32 locationOffset, u32 n + the
  member record bytes. Sent when the row changes (the zone's 1-s check: health, energy / 10, landscape, position / 5 m,
  knocked out), and right before every Invite / Join the zone hands on (a new group has every member's row at once). A
  row from a world the character is no longer connected through is dropped; an unchanged record is not fanned out.
- **GroupStateForZone 0x2B / 0x10002** social → zone: u64 character, u32 group (0 = none), u64 leader, u32 n × u64
  members (all worlds), u8 flags (1 joined now → the zone runs the "join a group" goal; 2 world entry). Sent on every
  change of members / leader and on every world entry.
- Party lines: the zone's PacketChat forward (0x13 / 1, channel 8) while groups are remote and the link is up.

**Positions stay on their world (ours).** The service passes a member's record as is to members on the member's own
world, and to members on another world with the world field (+0x100) set to 0 and the position zeroed
(GroupPackets.HideLocation; the zone always writes 1 there, so the viewer's own record never matches: the client shows
the member as not "IsInZone", no distance, outside the bonus range; the landscape name stays). Members elsewhere get an
UpdateMember only when that hidden copy changed (not for every 5 m step). Live's values for +0x100 / +0x10c are open
(OPEN-LEADS L122).

**Logout, world change, restarts (ours, follows the presence grace §5.5).** A member who leaves their world stays in
the group for the grace (30 s) and shows "linkdead" to the others (link id +0xf8 set to 1, GroupPackets.MarkLinkdead);
entering any world within the grace gives the client its group again (AddGroup on the new connection), the new world
gets GroupStateForZone and publishes a fresh row — a group survives a world change. When the grace runs out the member
leaves (RemoveMember reason 3, as a logout did before). Groups live in memory: a service restart ends them; at the next
PresenceSnapshot every world gets GroupStateForZone "none" with the entry flag and a zone that still had a group for the
character sends the client RemoveGroup reason 2 "invalid" (ours). A world link that drops puts its members into the
grace (linkdead); its snapshot on the reconnect brings them back without a new AddGroup (same connection ids).

**While the service is unreachable.** Group requests get a system line ("Groups are unavailable right now; …", ours;
no client text found), nothing changes; party chat falls back to the members on this world (zone cache); rows are sent
again once the link is back (the zone only remembers rows that went out).

**Not built / open.** MapPing (World channel) and the other unanswered GroupsBase requests (AutoGroup, LookingForMore,
GetGroupInfo, SetGroupDescription / Flags, ChangingJob) stay unanswered as before; how live laid out a member on another
world and a member logging out, and whether a cave group trip reached members on other worlds: OPEN-LEADS L122.

**Test:** TESTING-NEEDED.md 2026-10-16 "Groups across worlds" (isolated stack re/test/socialtest, second world =
FakeWorld `group` / `row` commands).
