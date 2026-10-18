# Internal messages between the services (TASKS L8)

Written 2026-10-16 from trace/0695/architecture-leads.md L8 and the ids the LandmarkEmu services use today
(LandmarkEmulator.Internal). Two kinds of ids live side by side:

1. **Live's own server-to-server messages.** Their names and ids are compiled into the client's packet-id registry
   (re/out/packet_ids.tsv for 0.589; 0.695 registry calls in part_0107 FUN_1434c7940) but no client code uses them. They
   share the client families and id space. Where we build the same exchange we **use live's family and sub id**.
   Ids below are 0.589 numbering; in 0.695 Claims subs ≥ 0x40 moved +2 and Mail subs ≥ 4 moved +1 (packets-695.md §A).
   Their field layouts are not known (L48): ours are our own.
2. **Ours.** Where live's name isn't known, our messages use the client family with a sub at **0x10000 and above**,
   which no client id reaches (client subs are u8 / u16), so they can never be confused with a client packet.

The transport itself (framing, opcodes 7 / 8 / 0x16 tunnels, our 0x20-0x30 control messages) is in
internal-transport.md and gateway-service.md.

## 1. Live's server-to-server messages (registry names)

| Parties | Family | Messages (0.589 sub) | Used by LandmarkEmu |
|---|---|---|---|
| Claim service ↔ zone | Claims 0xF9 | ZoneAddClaim 0x48, ZoneRemoveClaim 0x49, ZoneAddAllClaimsRequest 0x4A, ZoneAddAllClaims 0x4B, ClaimMoveItemToPlayerOnZone 0x45, UpdateClaimBasicServerInfo 0x58, SaveBasicInfo 0x4F, StatusChange 0x5A, PlayerDeleted 0x60, NameToGuid / GuidToName Request + Response 0x54-0x57, RequestClaimPurchase 0x52, RequestClaimRemoved 0x53, UpdatePropLimits 0x5F, BlockVeins 0x59, ClearAllProceduralProps 0x47, ResetTemporaryTerrainDamage 0x46, SendPlayerUpkeepInfo / UpdatePayment / UpdateReminder 0x4C-0x4E | not yet (claims still run in the zone; a claim service would use these ids) |
| Mail service ↔ zone | Mail 0xF8 | ComposeRequestStage2 / Stage3 0x2 / 0x3, ComposeResponseReturnAttachments 0x5, ConsumeAttachmentZoneRequest / Response 0x11 / 0x12 | **yes** — Social service mail (MailFamily: ComposeStage2Sub 2, ComposeStage3Sub 3, ComposeReturnAttachmentsSub 5, ConsumeZoneRequestSub 0x11, ConsumeZoneResponseSub 0x12) |
| Zone ↔ voxel / Environment Server | VoxelConstruction 0xF4 | ZoneSelectionAreaRequest 0xF, ZoneRequestFailedCellList 0x12, OffGridPasteZoneRequest 0x13, EnvServerResponse 0x15, HealClaimRequest 0xC, PasteClaimAreaRequest 0xD, DeleteClaimAreaRequest 0xE, ExportContestClaimRequest / Response 0x14 / 0x1A, SpawnExportedTemplateRequest / DespawnExportedTemplateRequest 0x18 / 0x19 | not yet (the Environment Server runs in-process, B.3; the remote split should use these ids) |
| World ↔ zone / gateway (admin) | Command 0x1E | WorldKickRequest / Reply / Pending 0x43A / 0x43B / 0x451, WorldKick 0x443, BroadcastZone 0x43D, BroadcastWorld 0x43E, WorldRequest 0x482, ZoneRequest 0x3F4, ServerInformation 0x3ED, ServerVersion 0x431, ServerUptime 0x432, ZoneVars 0x456, WorldVars 0x46A, LockStatusQuery / Reply 0x4E4 / 0x4E5, InternalConnectionBypass 0x4E0, GatewayProfilerRegistration 0x4D7, ZoneExecuteCommand 0x48, Profiler 0x450 | not yet (admin page B.7, gateway profiler) |
| World ↔ zone, zone ↔ zone (base opcodes, no sub; added 2026-10-16 from the zone base registry 0.695 FUN_1434ba530 / 0.589 FUN_14340c950; live-server-architecture.md "0.695" §3) | base | ZoneLoginRequest / Reply 0x04 / 0x05, ZoneLogout 0x06, ZoneIsReady 0x07, ZoneStats 0x08, TransferPlayerToServer 0x09, SavePlayer 0x0A, PlayerTransferServerRequest / Reply 0x0B / 0x0C, ProxiedPlayerBase 0x15, WorldInitiatedPlayerTransferServer 0x16, ZoneToWorldForPlayer 0x17, ZoneToZonePacket 0x18, ClientToTargetClientsZoneWrapped 0x19 (client-handled answer TargetClientNotOnline 0x1A), WorldToClientWrapped 0x1B, WorldToZoneForPlayer 0x1C, LogoutPlayer 0x20, ShutdownZone 0x21, ZoneReportingClientIsReady 0x22, SavePlayerTrackedStats 0x28, ImmediatelyDeletePlayerFromZone 0x33, EscrowGivePackage / GotPackage 0x3F / 0x40, ZoneWorldMiniGameStatus 0x43, ZoneWorldNpcKnockout 0x44, WorldZoneRenamePlayerRequest / Reply 0x49 / 0x4A, WorldZoneSyncTime 0x4E, NpcTransferServerRequest / Reply / NpcTransferServer 0x50-0x52 | not yet (one zone process per world; a multi-zone build's handover uses PlayerTransferServer* / ZoneToZonePacket, L73 / L114) |
| Stats / leaderboards ↔ zone | ProfileStats 0xCA | GetZonePlayerProfileStats / ZonePlayerProfileStats 0x1 / 0x3, GetZoneCharacterStats / ZoneCharacterStats 0xA / 0xB, UpdatePlayerLeaderboards / Reply 0x4 / 0x5 | not yet |

## 2. Ours (sub ≥ 0x10000)

| Parties | Family | Messages | Where |
|---|---|---|---|
| Gateway ↔ login service | Login 0xF6 | TicketConsume 0x10001 / Reply 0x10002, RelogSession 0x10003 / Reply 0x10004, NoteRelogin 0x10060, ZoneRelogTag 0x10063 | login-gateway.md |
| Zone ↔ social service (mail) | Mail 0xF8 | ReadData 0x10001, AttachmentQuery 0x10002 / Reply 0x10003, SystemMail 0x16 (live's family sub, layout ours) | social-service.md §5 |
| Zone ↔ social service (presence) | Command 0x1E | PresenceSnapshot 0x10001, CharacterEnteredWorld 0x10002, CharacterLeftWorld 0x10003, ZoneChanged 0x10004, CharacterDirectoryUpdate 0x10005, PresenceQuery 0x10006 / Reply 0x10007 | social-service.md |
| Zone ↔ social service (friends / ignore / guilds) | Friend 0x4D, Ignore 0x72, Guild 0x83 | SocialStateForZone 0x10001 (friends), IgnoreListSync 0x10001 (ignore), GuildJoined 0x10001 (guild); plus the client's own subs forwarded (AddFriend 0x10, RemoveFriend 0x12, Ignore 0x20) | social-service.md |
| Zone ↔ social service (chat) | Chat 0x13 | the client's ChatLine 1, JoinRoom 0x0A, LeaveRoom 0x0C forwarded | social-service.md |
| World channel ↔ zone (trades) | Trade 0x3E | TradeZoneSessionStart 0x10030, End 0x10031, Open 0x10032, Ready 0x10033 (broker trade ids from 0x40000001) | world-channel-service.md §6.4 |
| Zone ↔ social service (Game Finder) | InstancedGameBase 0x10D | MatchmakingRequest 0x10100 (zone → social), MatchmakingGames 0x10101 (zone → social), MatchmakingUpdate 0x10102 (social → zone); next free 0x10103 | world-channel-service.md §6.8, social-service.md §9; LandmarkEmulator.Internal/Social/MatchmakingMessages.cs |
| Zone ↔ social service (groups across worlds, B.9) | GroupsBase 0x2B | the client's Invite 1, Join 2, Leave 4, Kick 5, Disband 6, SetGroupOwner 8 forwarded (ClientRequestForward); GroupMemberRow 0x10001 (zone → social), GroupStateForZone 0x10002 (social → zone); next free 0x10003 | social-service.md §10; LandmarkEmulator.Internal/Social/GroupMessages.cs |
| World channel ↔ zone (claim ratings) | Claims 0xF9, RatingBase 0xA7 | ClaimInfoQuery 0xF9/0x10040 (WC → zone) / ClaimInfoReply 0xF9/0x10041, RatingVoteCounted 0xA7/0x10042 (WC → zone), RatingVoteRefused 0xA7/0x10043 (WC → zone), RatingClientRequest 0xA7/0x10044 (zone → WC); next free 0xF9/0x10042 (0x10020-0x10021 kept for ContestEntrySubmit / Result, §6.3), 0xA7/0x10045 | world-channel-service.md §6.5; LandmarkEmulator.Internal/WorldChannel/RatingMessages.cs |
| Zone ↔ commerce service (B.4 step 8) | InGamePurchase 0x45, RatingBase 0xA7, Claims 0xF9, Fotomat 0x98, PortraitCrc 0x75 | the client's requests handed on with a caller block (CommerceClientRequest: 0x45 subs, 0xA7/0x1D, 0xA7/8 "Contests", 0x98/2, 0x75/0) and CommerceProductCreate 0x45/0x2F (zone-checked); ours 0x45: Hello 0x10002, MarketplaceStatus 0x10010, GalleryStatus 0x10011, GrantDesignRequest / Reply 0x10012 / 0x10013, MarketSnapshot 0x10014, StudioListRequest / Reply 0x10015 / 0x10016, DesignTaken 0x10017, Notice 0x10018, ResultMail 0x10019 / Sent 0x1001A, AdminRequest / Reply 0x1001B / 0x1001C, ImportRequest / File / Done 0x1001D-0x1001F (next free 0x10020); 0xF9: ContestEntrySubmit 0x10020 (0x10021 stays reserved for the spec's ContestEntryResult, unused; 0xF9 next free stays 0x10042); 0x98: PortraitUpload / Reply 0x10030 / 0x10031 (next free 0x10032) | world-channel-service.md §6.3 "Built"; LandmarkEmulator.Internal/Commerce/CommerceMessages.cs |
| World channel ↔ zone (encounters) | EncounterBase 0x2C | EncounterParticipants 0x10050 (WC → zone), EncounterCreateDuel 0x10051, EncounterForfeit 0x10052, EncounterDuelAnswer 0x10053 (zone → WC), EncounterNotice 0x10054 (WC → zone); next free 0x10055 (broker encounter ids from 0x40000001) | world-channel-service.md §6.5; LandmarkEmulator.Internal/WorldChannel/EncounterMessages.cs |

## 3. Rules for new messages

- A message live named (section 1) keeps its family and sub id when we build it, with the 0.695 shift applied when the
  receiving side speaks 0.695 numbering (internal services use 0.589 numbering unless stated).
- Anything else takes the family it belongs to and the next free sub from 0x10000 up in that family; add it to
  section 2.
- Layouts are ours until a capture or a decoded server binary shows live's (L48).

## 2026-10-18 additions (local/review, merge 259980d)
- CommerceProductCreate: trailing u32 CreateId; the service answers with a typed create result for that id (sub 0x10027 CommerceProductCreateResult) and the world can ask the status of a create (sub 0x10028 CommerceProductCreateStatus) when no answer came within 30 s or the link dropped.
- CommercePayout delivery: the service remembers which world it offered each share to (10 min while that link is open); worlds record a payout id (character_unlock kind 32) before mailing and confirm repeats without mailing.
- TicketConsume: the trailing world id is optional (read only when present), so mixed gateway / login builds still log in.
