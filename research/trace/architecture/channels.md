# Which back end each client message goes to

## The mechanism (traced)

- Every message from the client to the game is wrapped in the gateway's external packet
  `Gateway::ExternalPackets::PacketTunnelPacketFromExternalConnection` (gateway opcode 6). Its first byte is
  `channel << 5 | 6` (e.g. FUN_142ba7ac0: `local_20a8[0] = (char)param_3 << 5 | 6`), then the message itself.
- The client has 593 send templates built that way (each `FUN_142ba....` / `FUN_143159....` taking the channel as
  its third argument) plus small wrappers that fix the channel (e.g. FUN_142b77180 → FUN_142ba7ac0(..., 1, ...)).
- Channel numbers 0-3. **The client never names them** (it only logs `channel=%d`). The emulator's labels
  (LandmarkEmulator.Gateway/Network/Message/Channel.cs: 0 Zone, 1 World, 2 PositionUpdate, 3 Voxel) come from earlier
  community work: 0 / 1 / 2 match what the client sends on them (below); **"Voxel" for 3 is not supported by the
  client** — channel 3 carries world-editing previews, vehicle collision / weapons, managed positions, MultiWeapon
  and EventPacket, while terrain cell requests go on channel 0. Called "channel 3" from here on.
- **Incoming** (ClientGateway vtable 1446000c0, tunnel handler FUN_142d58570(conn, channel, data, length)): channel 2
  is read directly as a compact position update (`PlayerUpdatePacketUpdatePositionGatewayToClient`, opcode 0x7F,
  Movement_ReadBody) and handed to the client (vtable +0x208); **every other channel goes to the same packet
  dispatch** (client vtable +0x138). So the client doesn't care which channel a server message arrives on (except
  2); the routing that mattered was on the server side, by the channel the client sent on.
- The gateway can tell the client a channel is (not) routable: `OnChannelIsRoutable channel=%d isRoutable=%d`
  (FUN_142d582a0), `OnConnectionIsNotRoutable` (FUN_142d58300) — each channel leads to a back end that can be up or
  down on its own.

## Message classes per channel (static, every send site)

Made by `python re/tools/channels.py`: at every call of a send template / wrapper, the class of the packet variable
passed (`local = Class::vftable` in the same function, following parameters up to 3 callers). Full table:
[channels-589.tsv](channels-589.tsv): 523 classes (Zone 408, World 109, Voxel 10, some on more than one). 54 send
sites build their packet in a way the script doesn't follow (Zone 39, World 14, Position 1; e.g. FUN_1431b2170,
FUN_1432fbb50 commerce, FUN_1432f9070 admin friend commands `AdminCommandPacketForceAdd/RemoveFriendRequest` on
World, FUN_142d98fa0 sends the same packet on World or Zone) → **resolve by hand**.

**World channel (110 classes)** — account-wide and cross-world things:
- Friends: CommandPacketAddFriendRequest, AddSteamFriendRequest, ConfirmFriendResponse, RemoveFriendRequest,
  FriendsPositionRequest, FriendUpdateExtraInfos.
- Guilds: GuildCreate / Disband / Invite / InviteReply / InviteRevoke / Kick / Quit / Rename / ChangeMemberRank /
  UpdateRank / MotdUpdate / DescriptionUpdate / Petition* / RequestMutePlayer / UpdateAppDataField.
- Chat membership (not chat lines): CommandPacketChatChannelOn / Off, PlayerPublicChatJoinRoomRequestPacket,
  VoiceRequestCustomChannel.
- Ignore (CommandPacketIgnoreRequest), AFK, GoHome, name change / validation, RequestPlayerPositions.
- Trade: TradeInitiate, TradeInviteReply, TradeCancel.
- Social: SocialPacket* (profile, likes, wall, challenges, reputation, house rating, top scores).
- Ratings / contests: RatingPacketAddVote, CanVoteRequest, RequestContestsRequest; leaderboards
  (ProfileStatsPacketGetLeaderboard...).
- Commerce / Marketplace / Player Studio: PacketInGamePurchase* (account info, store, search, products, reviews,
  follow creator, wallet, Station Cash products, country codes, point of need, server status, Spice web auth),
  PacketCommerceSessionRequest, ItemReview opt-in, WallOfData marketplace / UI / keyboard / voice events.
- Lobbies and matchmaking: Create / Join / Leave / StartLobbyGame, LeaveLobby, Encounter* invitations / entrance /
  exit / cancel, EncounterMatchmaking::MatchInvitationResponse, mini-game invites, quizzes.
- Portraits (PacketPortraitDataRequest, PacketAddClientPortraitCrc), teleport queue (PacketTeleportToQueuedLocation,
  CancelQueue), requisition items, PacketCharacterSelectSessionRequest, Steam info, client Lua metrics.

**Zone channel** — the world simulation: everything else resolved, including chat lines (runtime log: ChatBase on
Zone), **mail** (PacketMailInboxList / ReadMessage / DeleteMessage / AttachItem / ConsumeAttachment requests),
QuickChatSendChatToChannelPacket, inventory, claims, props, voxel construction, abilities, UI requests.

**Channel 3** — all eight WorldEditing 0x10C messages: PreviewSetup / PreviewUpdate / FillUpdate / Clear (paste / fill previews) and BoxUpdate / SphereUpdate / ShapeUpdate / SelectionUpdate (0.695 FUN_143085f20 / FUN_143086400 / FUN_143086280 / FUN_143086110, third argument 3; terrain-service.md §1), EventPacket,
plus a few vehicle / managed-position packets (PlayerUpdateManagedPosition, VehicleCollision, VehicleWeapon,
MultiWeaponPacket — unused in Landmark). **Terrain cell requests (TerrainCellPacketClientCellRequest, sent in
FUN_1430acba0 with channel 0), PendingRequestTimeout, ResetTemporaryCells, VoxelConstruction requests and template
requests all go on channel 0 (Zone)** (channels-589.tsv).

**PositionUpdate channel** — movement (2 send sites).

## Runtime check (dev world, console.log, 2026-10-15, ~8,000 client messages)

World: InGamePurchaseBase, RatingBase, SetLocale, FotomatBase. Zone: ChatBase and all gameplay. Matches the static
table.

## What it proves about the live servers

- A **World** back end, separate from the zone server, took friends, guilds, ignore, trade, chat-room membership,
  social, ratings, commerce / Marketplace / Player Studio, lobbies / matchmaking and portraits.
- **Chat lines and mail went through the zone server** (Zone channel); mail additionally had its own service behind
  it ("ERROR: Sorry Mail Service is currently unavailable :(", FUN_1432c9770).
- Paste / fill previews (WorldEditing) went to a channel-3 back end; terrain cells and voxel edits went to the zone
  server (channel 0).
- How the World back end and the zone servers talked to each other: not found yet (OPEN-LEADS L8) (server-to-server
  traffic never reaches it); `Gateway::InternalPackets::PacketTunnelPacketToExternalConnectionList` shows a back end
  could ask the gateway to deliver one packet to many players.

## 0.695 (re/decomp_all_695 → channels-695.tsv, 2026-10-15)

No message changed channel between 0.589 and 0.695. The 13 new client messages all go on channel 0 (Zone):
CharacterCustomizationPacketEquippedAttachmentRequest, ClaimsPacketPlayerEnteredClaim, ClaimsPacketSetAdventureFlags,
ClaimsPacketStartClaimPlacement, InstancedGameAddOrRemoveRuleRequestPacket, InstancedGameGetConfigurationRequestPacket,
InstancedGameMatchmakingRequest, InstancedGameRequestJoinTeamPacket, PlayerUpdatePacketSetPlayerBio,
PropPacketDialogResponse, RequestInspectPlayerPacket, TemplatePacketPurchaseMissingTemplateMaterials,
TemplatePacketPurchaseTemplateMaterialsDataRequest. (Matchmaking for the Game Finder went to the zone server, not the
World back end.)
