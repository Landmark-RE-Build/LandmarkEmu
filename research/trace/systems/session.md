# System: login, character select, zone entry and session messages

Status: [~] traced from families/Login_protocol.md, Gateway_protocol.md and the zone-dispatcher case code
(Zone_DispatchPacket = FUN_142c83660, cases read inline); server status read from LandmarkEmulator.AuthServer /
WorldServer (enum use counted, not coverage.md).

## Login protocol (login server, Login::ExternalPackets; our AuthServer)

| Op | Message | Server (AuthServer LoginHandler) |
|---|---|---|
| 0x1 / 0x2 | LoginRequest / LoginReply | handled (LoginUdp_9 and _10) |
| 0x3 | Logout | **not handled** |
| 0x4 | ForcedDisconnect (S→C) | **never sent** |
| 0x5 / 0x6 | CharacterCreate | handled (LoginUdp_9; _10 is a TODO) |
| 0x7 / 0x8 | CharacterLogin | handled (reply carries the chosen id, c875828) |
| 0x9 / 0xA | CharacterDelete | handled |
| 0xB / 0xC | CharacterSelectInfo | handled |
| 0xD / 0xE | ServerList | handled |
| 0xF | ServerUpdate (S→C: server status / population changes) | **never sent** |
| 0x10 / 0x11 | TunnelAppPacket client→server / server→client | handled for types 1 NameValidationRequest → 2 Reply, 6 LoginInit → 7 (text) + 9 ClaimData |
| 0x12 / 0x13 | CharacterTransferServer | **not handled** |

Tunnel data types known to AuthServer: 1, 2, 4 ServerQueue, 6, 7, 9 ClaimData, 0xA ArtData; **3, 5, 8 unknown** — the
client's tunnel handler is reached through a vtable (reader FUN_14399cfd0 ← FUN_14399f3c0, no static caller); find it
from the payload readers.

Client character-select Lua events: CharacterSelectHandler:OnCharacterNameValidation (FUN_14300c720),
OnCharacterCreate (FUN_14300dfd0), OnCharacterDelete (FUN_14300e560), OnCharacterLogin (FUN_14300e740),
OnCharacterLoginComplete (FUN_142c3b490); ServerQueueHandler:CancelQueue (FUN_142c80f20), text Login.Queue
(FUN_143011bb0, login queue UI) — the queue (tunnel type 4 / zone 0xE0 LoginQueueStatus) is not implemented.
Data tables: CharacterCreateOptions (184), CharacterCreateActors (184), CharCreateOptionEqualities (264),
CharacterCreatePresetDefinitions, CharCreateGearSet* — client-side character creation; server stores the result.

## Zone entry (WorldServer; docs/protocol.md has the order)

SendSelfToClient 0xF (sent), ZoneCoordinateInfo 0x12 (sent, captured values; per-zone origins open — see Zones in
SYSTEMS-WORKLIST), SendZoneDetails 0x2E (sent), ClientSettings 0xE9 (sent), ClientGameSettings 0x8E (sent; handler
fires GameEvents:OnSetGameTimeScalar), ZoneDoneSendingInitialData 0x11 (sent), ClientBeginZoning 0x23 (sent),
CharacterSelectSessionResponse 0xF7 (sent; GameEvents:SetCharacterSelectGameState), PlayerStudioRegistered 0xFE
(sent), ClientIsReady / ClientFinishedLoading 0xD (handled), GameTimeSync 0x37 (handled), ClientLogout 0x14 (sent).

## Session messages the server never sends (handler behaviour from the zone switch)

| Op | Message | Client does | Needed for |
|---|---|---|---|
| 0x1A | TargetClientNotOnline | prints text PlayerNotOnline with the name | tells, invites, friend actions to offline players |
| 0x38 | DelayWrappedPacket | queues the wrapped packet for later delivery (FUN_142c1df60) | timed delivery (optional) |
| 0x4C | ClientServerShuttingDown {u16} | message "The server has shutdown and logged you out." | final shutdown (we send WorldShutdownNotice 0x5F countdown; add this at the end) |
| 0x53 | ClientKickedFromServer {string} | message box KickedFromServerMessage (ErrorLabelWarning), logs out | GM kick, duplicate login |
| 0x59 | WorldDisplayInfo | (crash-report field; captured blob sent once) | — |
| 0x5A | MOTD | UI.MessageOfTheDay | message of the day at login |
| 0x5C | SetClientArea {u32 area, bool, u32} | sets an area state on an object (FUN_143c09d10) | area / region entry |
| 0x64 | ClientExitLaunchUrl {string} | stores a URL opened when the client exits | optional |
| 0x65 | ClientPendingKickFromServer {seconds, message} | text KickedFromServerTimedMessage ("player", "seconds") | timed kick warning |
| 0x66 | MembershipActivation | MembershipHandler:OnMembershipActivated, then ActivityListRefreshRequest | membership (store free: decide) |
| 0x69 | ShowSystemMessage {u32 text id, string, params[5], u32, u32} | shows a localized system text with parameters (by id or key) | server-side system texts in the client's own language |
| 0x6A | POIChangeMessage {u32 text, u32, u32} | text + POI marker update (FUN_142f343e0) | points of interest entered |
| 0x78 | CommerceSessionResponse | session ticket for the web store | store (free) |
| 0x91 | StartInspect {u8, u64, list} | inspect another player's gear | right-click Inspect |
| 0x97 | BeginJobCustomization | | not in 0.589 UI? |
| 0xA4 | ClientNotifyCoinSpinAvailable | HUD:ShowCoinWheel | daily coin wheel |
| 0xAA | ClientFlashTimer | RaceTimer:StartStopWatch / Stop, FlashTimer checkpoints | race timers (story tools / site games) |
| 0xAD | InviteAndStartMiniGame | mini game invite | mini games |
| 0xB3 | ClientMembershipVipInfo {u64, u32, u32, u32} | MarketplaceHandler:OnLoyaltyInfo | loyalty / VIP |
| 0xE1 | ServerPopulationInfo | population info | server population display |
| 0xE4 | PlayerUpdate Stop | stops an entity (FUN_142c43b00) | movement stop |
| 0xF2 | ZoneProximityFacialAnim | facial animation on an entity (FUN_1433fe790) | emotes / facial anims |
| 0x116 | ArtDataPacketTintDefinitionManagerData | tint definitions from the server | tints (client has ArtTint* tables) |

## Gaps (summary)

Login: Logout, ServerUpdate, ForcedDisconnect, CharacterTransfer, tunnel types 3/5/8, login queue, LoginUdp_10
create. Zone: kicks (0x53/0x65), final shutdown 0x4C, MOTD 0x5A, ShowSystemMessage 0x69, TargetClientNotOnline 0x1A,
Inspect 0x91, POI 0x6A, SetClientArea 0x5C, coin wheel 0xA4, flash timers 0xAA, population 0xE1, PlayerStop 0xE4,
facial anims 0xF2, tint data 0x116 — each to be judged against 0.589 UI reachability in its own system.
