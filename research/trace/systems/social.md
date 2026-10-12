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
