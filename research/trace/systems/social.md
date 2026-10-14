# System: social — chat, friends, ignore, groups, raids, guilds, notifications, mail, trade, vendors, social profile

Status: [~] map level (from FAMILIES-SUMMARY.md); built parts documented in docs/gameplay.md, docs/vendors.md.
✔ = server does it today (stats / code), ✗ = not; "(check)" = status column unreliable for this family (sub in payload).

## Chat (0x13; QuickChat in 0xFF)
✔ normal chat, tells (TellEcho), FromStringId, chat history, quick chat to channel / tell. ✗ C→S
PlayerPublicChatJoinRoomRequest 0xA / LeaveRoomRequest 0xC (public chat rooms; replies Create / TransferOwnership /
InviteUser / RoomInfo exist in the client). Command 0x1E: ✗ ChatChannelOn / Off 0x22 / 0x23, ✗ SetChatBubbleColor
0x15. Data: ChatChannels.txt (server-only list? — check), CombatSpam.txt.

## Friends (0x4D; Command 0x10 / 0x12 / 0x14 / 0x18)
✔ list, online / offline, add / remove, ExtraInfos. ✗ FriendUpdateProfileInfo 0x4, FriendUpdatePositions 0x5 (map
positions; request Command 0x18 GetFriendPositions ✗), FriendStatus 0x9, FriendRename 0xA, ConfirmFriendResponse
(Command 0x14). Acquaintances 0x4B (Add / Remove / Online) ✗ — the client's "recently met" list.

## Ignore (0x72, Command 0x20) ✔ (Game/Ignores.cs).

## Groups (0x2B) ✔ requests (check); ✗ S→C AddMember / InviteToGroupContent / RemoveInvitation (check against
Groups.cs). Raids (0xB8) ✗ all (raid UI in 0.589? check).

## Guilds (0x83 / 0x84; Game/Guilds.cs)
✔ create, invite / reply / revoke, kick, quit, disband, rename, rank change, motd, mute. ✗ UpdateAppDataField 0x11,
Petition / PetitionReply 0x7 / 0x8, UpdateRank 0xE, DescriptionUpdate 0xF. S→C status (check): GuildDataFull 0x12,
GuildData 0x13, Invitations 0x14, AddMember 0x15, RemoveMember 0x16, UpdateInvitation 0x17, MemberOnlineStatus 0x18,
Notification 0x19, Add / RemoveLogUpdate 0x1A / 0x1B (guild log), UpdateAppData 0x1C. Guild tag 0xDF.

## Notifications / Game Action Queue (0x01) ✔ list / add / update (guild, group, trade invites); types not created:
3 GroupApproval, 4 FriendInvite, 9 MarketplaceItemReviewRequest (client-made), GaqItemClaimExpiring /
PendingMail / SwapSeatRequest / TeleportToPlayer / TeleportToZoneRequest.

## Mail (0xF8; Game/Mail.cs) ✔ compose, inbox, read, delete, attachments, COD, kinds (Player Studio). Check:
ClearInboxResponse, DeleteMessageResponse, EnableWindow, AttachItemToComposeResponse (stats say ✗ — sub encoding).

## Trade (0x3E) ✔ (Game/Trades.cs) — stats mark EndSession / AddItem / Accept / Message / Rejected ✗ (check: sub
encoding). Vendors (0x112; Game/Vendors.cs) ✔; ListItemResponse 0x5 / PurchaseItemResponse 0x8 ✗ (check).
Coin store (0x9E) ✗ all — NPC merchant window (no merchant NPCs in 0.589, docs/vendors.md).

## Social profile / reputation (0x9C) ✗ all: UpdateLike, ReputationUpdate, RequestProfile, game top scores, games
played, challenges, house rating / furniture score, diplomacy events; S→C ProfileInfo, HighScoreUpdate, Notification,
ReputationStatsUpdated, ChallengeUpdated. Data: SocialSettings / SocialMiniGameData / Diplomacy* (server-only,
SocialDataManager). Check which 0.589 UI uses them (social wall had no 0.589 content).

## Inspect (0x91) ✗ StartInspect / StopInspect — right-click Inspect another player's gear.
## Voice (0xB9) ✗ VoiceJoinChannel / RequestCustomChannel (Vivox; not needed).
## Facial anims (0xF1 / 0xF2 / 0xF3) ✗ — client facial animations shown to others.
## Speech packs (0xDD) ✗ SetSpeechPack / GetSpeechPackList, PlayerFilterUpdate.

## Client constants (trace/client-constants.md)
Mail: expires after 43200 min (30 days), body ≤ 4096 chars, subject ≤ 128, To ≤ 256, mailbox window closes at 10 m.
Chat: message ≤ 512 chars, room name / password ≤ 32, 10 public rooms per player, tab history 100. Guild.MaxMembers 250.
Group map indicator type 1.

## Live behaviour (wiki, launch 2016: Chat, Commands List, Guild)
- Chat tabs (fixed order): Chat (local), **General (worldwide, all servers)**, Loot (session loot log), Guild,
  Officer (by guild rank), Tells (one tab per partner), Party. Numbered channels /1 General, /2 Trade, /3 Crafting,
  /4 Newbie; /join creates or joins a channel (own tab), /leave, /who (channel or server list), /tell /t /w
  /whisper, /reply /r, /shout /yell (zone, limited range), /ignore add, /friend add, /party invite / accept / reject
  / kick / leave / disband / setowner, /afk, /bug, /camp /exit, /loc, /marketplace open, /reportclaim, /emote,
  /emotelist + mood emotes (/angry /happy /calm /sad, upper body, stack with poses) + poses (/dance /drunk /kneel
  /playdead /skip /sit /lie /sneak /stand) + ~30 gestures (/beckon … /yawn) with their texts "You wave." /
  "You applaud [target]."
- Voice chat: Local (proximity) and Guild channels, push-to-talk (Vivox; out of scope for the server unless we host
  voice).
- **Guilds** (N): create (invitees must be online), ranks with permissions, Message of the Day shown at login;
  guild members are added to the friends list automatically; guild chat + officer chat; max 250 members
  (constants). Friends list: teleport to friend, private chat, invite to party.
- Party: harvest / chest loot sharing (200 m for harvests; 50 m for chests applies to everyone anyway).

## Deep trace (rebuild module 10, 2026-10-14; archive social-v1)
Packet coverage was already complete (REACHABLE-GAPS: 0 sendable C→S unhandled; ui_server_map: every social UI call
handled). This pass checked the server's RULES against the client's own code, tables and constants:
- **Chat channels** = ChatChannels.txt row − 1 (0 Say, 1 Tell, 2 TellEcho, 3 Shout, 4 Emote, 5 Guild, 6 Officers,
  7 System, 8 Party, 9 Public, 10 PlayerPublic, 11 Error, 12 Loot, 13 Combat); chat commands FUN_1432ac6b0 (say 0x33,
  party 0x34, whisper 0x35, shout 0x36, emote 0x37, guild 0x38, officer 0x39). FIXED: officer chat went to everyone
  online, shout and emote lines too; now officers → ranks with JoinOfficerChat, shout → 500 m of the zone (wiki "zone,
  limited range"), emote → say range; channels only the server makes (2, 7, 11–13) are dropped from clients.
- **Guild hidden permissions**: string table 0x145218798 = JoinOfficerChat, MuteGuildChat, MuteOfficerChat (then
  Guild.MaxMembers). FUN_142e5c120 joins channel 6 only with JoinOfficerChat; FUN_142e5ae70 sends
  GuildRequestMutePlayer (0x83/0x10, serializer FUN_142e44450 {u64 guild, u32 channel 5/6, u32 minutes (−1),
  string member, string requester, u8 mute}) for "/guild_debug mute_member|unmute_member <guild id> <member guid> [guildchat] [minutes]" when the
  rank has MuteGuildChat / MuteOfficerChat and outranks the member. The server had no handler and no rank held these
  permissions (so the officer tab never appeared). FIXED: permissions in the rank model (leader all; stock Officer rank
  JoinOfficerChat + MuteGuildChat, migrated on load), mute handler, muted members' guild lines dropped.
  Guild.MaxMembers 250 (was 500).
- **Party size**: the client has no limit of its own; wiki Party page "a party is a group of four people" → 4 (was 6).
- **Mail**: client constants MaxSubjectCharLength 128 / MaxBodyCharLength 4096 (server cut at 64 / 2000; body column
  widened to 4096, migration MailBodyLength); Mail.ExpireTimeMinutes 43200 — the inbox counts down from the sent time
  (FUN_1435849b0); the server now deletes expired messages that hold nothing.
- **Chat rooms**: Chat.MaxRoomNameCharLength 32 (server allowed 64).
- **Trades**: message codes (FUN_14358ee50 case 0xE) 1 canceled, 2 partner disconnected, 3/4 offer invalid, 6 done,
  7 changed after accept, 8/9 you / partner entered an instanced zone, 10 full inventory. FIXED: a trader changing
  landscape or entering a running instanced game cancels the trade (1 / 8 / 9; was left open); the full-bag check
  counted one new slot per offered stack — items that stack onto the receiver's own stacks now fit (a full bag
  refused every trade).
- **Friends**: the window (ContactWidget) uses only Name / GUID / Online; one-way lists, online/offline notes to
  watchers — matches. **Ignore**, **tells**, **groups** (invite / join codes), **guild** notifications: re-tested, match.
- **Emotes**: the player network (playeranimationnetwork64.mrn) has single emotes Beg, Bow, Cheer, Frustrated,
  Laugh, Yes, No, Point, Wave, GoAway, Congratulate, Cry, Flirt, Giggle, Shrug, Sob, TalkLong, Taunt (+ Agree, ROFL
  in the mood sets) — one per client emote word (groan = Frustrated, shoo = GoAway, nod = Yes, talk = TalkLong). The
  Emote_Index → clip order is inside the network's state machine (not in node order); still mapped by eye for 8 words.

### Emote network findings (2026-10-14, in progress)
- Extracted with re/tools/packfind.py: Assets_153.pack PlayerAnimationNetwork64.mrn (51 MB). Strings dumped to re/out/anim/strings.txt.
- Emotes are under StandPoses|...|PartialStances_LowerBody|Emotes.
  - Moods Angry/Sad/Happy are state machines, each holding 20 emote states (Bow, Frustrated, Laugh, Yes, No, Point, GoAway, Beg, Cheer, Cry, Flirt, Sob, TalkLong, Taunt, Agree, Congratulate, Giggle, Shrug, Wave, ROFL) plus Stand.
  - "Nuetral" (sic) and NPC use Switch nodes (Switch1, Switch2). A Morpheme Switch selects a child by a control parameter (Emote_Index), so the order of the switch's child IDs gives index → clip.
  - Nuetral node order: Crafting, Beg, Bow, Cheer, Frustrated, Laugh, Yes, No, Point, Wave, GoAway, [Switch1], Congratulate, Cry, Flirt, Giggle, Shrug, Sob, TalkLong, Taunt, [Switch2], Dancing.
- DECODED:
  - The node-ID → name table is an ID-mapped string table: count 18300 at 0x5fc70, then IDs, offsets from 0x5fc7c, and names from 0x71a6c. Saved as re/out/anim/node_names.json.
  - The neutral emote node IDs (10773-10910) appear as two u16 child arrays:
    - 0x22a24a0 Switch1 = Crafting, Beg, Bow, Cheer, Congratulate, Cry, Flirt, Frustrated, Giggle, GoAway, Laugh.
    - 0x22ca670 Switch2 = Switch1, Point, Shrug, Sob, TalkLong, Taunt, Wave, No, Yes, Dancing.
  - So Emote_Index 1-11 → Switch1 child (index - 1), and 12-20 → Switch2 child (index - 11).
  - Fits the six by-eye matches (bow 3, cheer 4, cry 6, shrug 13, taunt 16, wave 17). Index 20 (Dancing) is the one index without an EmoteSingle row.
  - By-eye errors: "point" (14) was Sob; "cough" (1) was the crafting loop, and the client has no "cough" word.
- Emotes.cs now maps all 18 client words: beg 228, bow 229, cheer 230, congratulate 232, cry 231, flirt 233, groan 234, giggle 235, shoo 222, laugh 237, point 238, shrug 239, sob 240, talk 241, taunt 242, wave 217, no 244, nod 245.
- NodeDef layout (switch, type 0x83): +4 u16 0x0100, +6 u16 node ID, +8 u16 parent, +10 u16 child count, +0x20 u64 offset to the u16 child IDs, +0x28 u64 offset to the control input (u16 source node, u16 pin).
  - Switch1 and Switch2 read ControlParameters|Emote_Index (node 115).
  - Every Emotion_Switch reads Emote_Emotion (node 117), with children in the order neutral, Angry, Happy, Sad (and NPC). The stand-pose EmotionSwitch has the same order.
  - Selection rows 218-221 set Emote_Emotion 0-3. The commands calm 218, angry 219 and happy 220 are the client's mood words; 221 (Sad) has no word.
- Still open: the emote chat lines (559125 "You wave.", 559127 "[*speaker*] waves.").
