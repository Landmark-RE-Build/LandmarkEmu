# Server→client sweep: what the 0.695 UI does with messages our server never sends (E.2 follow-up)

Agent s2c-sweep, 2026-10-18. Read-only: nothing in the repo was changed except this file. No game, Frida, Docker or web
was used. Evidence: **[P]** proven from the 0.695 client (decompile `re/decomp_all_695`, the 0.695 install's Lua / XML,
`UI/ScriptsBase.bin`, AS3 exports `re/as3_695` + `re/as3` for unchanged movies) or from a live capture; **[I]**
inferred; **[W]** wiki / forum / patch notes (`design/`).

## 0. Method and what it can and cannot see

1. **Scope.** (a) Every `re/tools/verdicts.tsv` row tagged "[E.2 2026-10-17: server→client — 0.695 UI reaction still
   to check]" (89 rows). (b) Every S→C class in `trace/0695/coverage-695.md` §10 (732 rows) that the server does not
   send.
2. **Client side.** I built a callgraph of all 236,307 functions in `re/decomp_all_695` (scratchpad `s2c/cg.py`). For
   each S→C class I found the handler `case` that calls its 0.695 reader and walked its callees 3 deep, skipping helpers
   with more than 150 callers (`s2c/sinks.py`). The walk records these sinks:
   - Lua calls: `FUN_142edbdb0(L, "Table:Method", …)` = 0.589 FUN_142e3d110. Each name was checked against every
     function defined in the 0.695 Lua: `Resources/Scripts/*.lua`, `UI/Default/*.lua`, `UI/Admin`, `UI/TestMod`.
     `UI/ScriptsBase.bin` is the Lua 5.3 bytecode of the same 25 Resources/Scripts files (paths inside it). It adds
     none of the missing names (`QuestHandler`, `EQNScreenText`, `FlashTimer`, `MiniMap`, `ReticleHandler`,
     `MembershipHandler`, `ActivityEvents`, `Lobby`, …) [P].
   - **The UI-notification queue (new finding).** `FUN_143721f70(UIProcessor+0xf8, "Name", params)` and
     `FUN_1433f98e0(UIProcessor, n, "Name", params)` queue an event. FUN_143722690 delivers it as
     `<Client.HandlerNotification>:OnNotification("Name", …)`. `Client.HandlerNotification = "NotificationManager"`
     (NotificationManager.lua:133). So a name is visible exactly when NotificationManager defines it. Defined:
     SystemMessage, BroadcastMessage, ErrorMessage, AlertMessage, AdminSystemMessage, CompleteQuest, FailQuest,
     UpdateObjective, CompleteObjective, FailObjective, Start/Update/CompleteCollection, ObjectiveComplete /
     ObjectiveFailed / ObjectiveUnhide, LoadingZone, ItemReceived, ShowMessage. Almost all of these dispatch
     `EVENT_SHOW_NOTIFICATION`, the on-screen notification. `StartQuest` is defined but broken (it dispatches the
     message text as the event name, so nothing listens). Not defined, so they print "[Error] No method defined":
     OnPlayerHeadshot, OnSkillComplete, JobLevelUp, AbilityReceived [P].
   - Chat-log lines: the message log `DAT_145431a10` (vtable +0x28 / +0x30, the same object that prints
     "Screenshot saved to: %s"). The chat-window line FUN_14336b9f0, chat line FUN_142ca7900,
     ShowSystemMessage FUN_142d56570, UI event FUN_1433ff200 and announcement FUN_142e7e090 are the 0.695 partners of
     uisinks.py's 0.589 sinks [P].
   - Data-source names in `PTR_s_*` strings, matched against every `new DataSource("…")` in the AS3 exports.
3. **Server side.** For each class: a search of `LandmarkEmu/**/*.cs` for its name and its 0.589 / 0.695 reader
   address (`s2c/srvcheck.py`), plus `srvsends.py` / `srvmodels.py` re-run on today's source. Every row that matters
   for a verdict was re-checked by hand. The name search misses classes the server sends under its own sub-constant
   names (e.g. RecipeBase 8 AddRecipe), so the "not referenced" status in §3 / §4 is a heuristic. §1 and §2 were all
   checked by hand.
4. **Limits.** C++ that writes straight into a data source or invokes a GFx movie method by name is not caught by the
   walk. The §4 "no sink found" list is therefore "nothing in Lua / notifications / chat / system message"; it does
   not prove there is no UI. Depth-3 hits through shared helpers are marked "[I-auto]".

## 1. The 89 E.2 rows

Opcodes are 0.695 numbers (base / sub). "Handler" = the 0.695 dispatcher case or function.

| Class (verdicts.tsv key) | 0.695 opcode | Client handler (0.695) | UI reaction on 0.695 | Live evidence | Verdict | Layout if BUILD |
|---|---|---|---|---|---|---|
| Claims.DepositUpkeepFunds, UpkeepManagementWindow.DepositUpkeepFunds (UI calls, not S→C) | C→S 0xF9 AddUpkeepFundsRequest | UpkeepManagementWindow.lua:18 | None. UpkeepManagementWindow is only listed in manifest.xml:163. GameEvents:OnOpenUpkeepBank (events.lua:756) opens **ClaimManagementWindow**, not this window. In as3_695, DepositUpkeepFunds appears only in the bundled ClaimsWrapper.as:169, with no widget caller [P] | upkeep is presence-based (existing) | UNREACHABLE (confirmed on 0.695) | – |
| ClientInventory.SalvageItem (UI call) | C→S 0x2D RequestSalvageItem | InventoryView.as:280 (0.695 and 0.589) | **Correction:** a widget does call it (ResultsList ITEM_CLICK while `_isSelected`), but `_isSelected` is only ever set to false (InventoryView.as:111, 281) [P] | – | UNREACHABLE (dead code; new proof) | – |
| InventoryPacketReplySalvageItem | 0x2D / 0x14 | FUN_142f202e0 | Chat texts Salvaging.Success / NoItem / NoReward / Unknown would print [P via 0.589 family trace; handler same] | – | UNREACHABLE (its request is dead code, above) | – |
| WaypointCooldownPacket | 0x32 / 0x13 | FUN_1433f2b90 case 0x13 → FUN_1430d88d0 (waypoint manager DAT_145431410) | No Lua, notification or chat. It only stores cooldowns for HUB / "WARP" waypoints (Waypoints.txt row 2) [P] | none: HUB waypoints are a warp system with no Landmark source (agent-leads2-d2 L104) | NO UI | – |
| ClientUpdatePacketMana | 0x29 / 0xD | FUN_142cd9980 case 0xc → FUN_1433fda80 | Only `CombatGroupHandler:SetPower`, which is not defined in 0.695 Lua or ScriptsBase.bin [P] | – | NO UI | – |
| PlayerPublicChatCreateRoomResponsePacket | 0x13 / 0x7 | FUN_143364980 | It would print a chat-window line, but the client builds no CreateRoom request. 0.695 natives: only Chat.RequestCloseChatTab → LeaveRoom, plus /join → JoinRoom (luabind_695) [P] | custom channels were live, but only through join (ChatRooms.cs) | UNREACHABLE | – |
| PlayerPublicChatDestroyRoomResponsePacket | 0x13 / 0xF | same | as above (chat-window line) | as above | UNREACHABLE | – |
| PlayerPublicChatInviteUserResponsePacket | 0x13 / 0x13 | same | as above. coverage-695 "sent (ChatRooms.cs:34)" is a mis-mapping: ChatRooms sends only 0x09 / 0x0B / 0x0D [P] | as above | UNREACHABLE | – |
| PlayerPublicChatTransferOwnershipResponsePacket | 0x13 / 0x11 | same | as above | as above | UNREACHABLE | – |
| ClaimPacketIdSnapshotSaveConfirmRequest | 0xF9 / 0x13 | FUN_142dbecb0 | It names `GameEvents:OnClaimSaveSnapshotConfirm` (PTR_s_GameEvents_OnClaimSaveSnapshotCo), which no 0.695 Lua defines. The 0.695 SnapshotPanel is an empty 34-line stub (as3_695 controls/SnapshotPanel.as) [P] | no snapshots in design sources | NO UI | – |
| ClaimPacketIdSnapshotRestoreConfirmRequest | 0xF9 / 0x15 | FUN_142dbecb0 | as above (OnClaimRestoreSnapshotConfirm undefined) [P] | as above | NO UI | – |
| ClaimPacketIdSnapshotRestoreResponse | 0xF9 / 0x16 | FUN_142dbecb0 | Chat error texts (Claims.Error.Snapshot*) [P] | as above | UNREACHABLE (no request from the stub panel) | – |
| ClaimsPacketAddUpkeepFundsResponse | 0xF9 / 0xC | FUN_142dbecb0 → FUN_142da8930 | System message + chat (Claim.Upkeep.OnAddFunds.* texts) [P] | – | UNREACHABLE (request unreachable) | – |
| AdminCommandPacketSpawnVehicle | 0x1E / 0x49C | FUN_1433b6030 | A chat-log line [I-auto]. Vehicles.txt and every other Vehicle* table have 0 rows (agent-leads2-d2 L107) [P] | none (PS2) | UNREACHABLE | – |
| AdminCommandPacketWeaponSet | 0x1E / 0x4EE | FUN_1433b64e0 | A chat-log line [I-auto]. Weapons / WeaponAmmoSlots have 0 rows [P] | none (PS2) | UNREACHABLE | – |
| ObjectiveCompletePacket | 0x30 / 0x3 | FUN_1433184e0 case 3 → FUN_14331c610 | **Visible if the objective exists:** it queues `ObjectiveComplete` (NotificationManager defines it → on-screen notification) and looks up text ObjectiveComplete [P]. The objective must first be created by ObjectiveActivatePacket (manager +0x20, FUN_14371f370) | Landmark goals use Achievement objectives 0x92 (sent today). No quest or objective system in design sources [W neg] | UNREACHABLE (no live feature; the UI would react) | – |
| ObjectiveFailPacket | 0x30 / 0x4 | → FUN_14331c920 | `ObjectiveFailed` notification (defined) [P] | as above | UNREACHABLE (UI would react) | – |
| ObjectiveLookAtPacket | 0x30 / 0x6 | → FUN_14371fbc0 | Only adds a client "LookAt" objective (log "Adding LookAt objective"). No UI [P] | as above | NO UI | – |
| ObjectiveClientClearPacket | 0x30 / 0x8 | → FUN_14371fe90 | log only [P] | as above | NO UI | – |
| ObjectiveClientCompleteFailedPacket | 0x30 / 0x9 | → FUN_1437201b0 | log only ("Server failed objective") [P] | as above | NO UI | – |
| ObjectiveUIEventPacket | 0x30 / 0xA | → FUN_14371fdd0 | Only adds a "UIEvent" client objective. No UI [P] | as above | NO UI | – |
| ObjectiveTargetUpdatePacket | 0x32 / 0xD | FUN_1433f2b90 case 0xd → FUN_1433f2930 | Only `MiniMap:ClearSelectedIndicator` and `ObjectiveWindow:SelectTask`, both undefined [P] | as above | NO UI | – |
| QuestAddPacket | 0x34 / 0x3 | FUN_142f2cd80 case 3 → FUN_1433f84c0 | Chat line "%s %s" (QuestStarted text + quest name). Queues `StartQuest`, which is defined but broken (no listener). QuestHandler:PopulateHelper undefined [P] | no quests in Landmark [W neg] | UNREACHABLE (UI would react: chat) | – |
| QuestCompletePacket | 0x34 / 0x4 | case 4 → FUN_1433f6a50 | Chat line (QuestCompleted) + **`CompleteQuest` on-screen notification**, for a quest the client already holds [P] | as above | UNREACHABLE (UI would react) | – |
| QuestFailedPacket / QuestAbandonedPacket | 0x34 / 0x5, 0x6 | cases 5 / 6 | Chat line + `FailQuest` notification [P/I-auto] | as above | UNREACHABLE (UI would react) | – |
| QuestObjectiveAddedPacket | 0x34 / 0x7 | case 7 | Chat line + `UpdateObjective` notification [I-auto]. QuestHandler:UpdateObjective undefined | as above | UNREACHABLE | – |
| QuestObjectiveActivatedPacket | 0x34 / 0x8 | case 8 | QuestHandler:UpdateObjective only (undefined) [P] | as above | NO UI | – |
| QuestObjectiveUpdatePacket / CompletePacket / FailedPacket | 0x34 / 0x9, 0xA, 0xB | cases 9-0xb → FUN_1433f7ec0 / 75e0 / 7a50 | Chat line [I-auto]. EQNScreenText:DisplayText and QuestHandler:UpdateObjective undefined | as above | UNREACHABLE | – |
| BaseMiniGamePacket, MiniGameGameEndScorePacket, MiniGameGameOverPacket, MiniGameGroupInfoPacket, MiniGameInfoPacket, MiniGameKnockOutPacket, MiniGameRewardStatusPacket, MiniGameStarsEarnedPacket | 0x2A / 0x1, 0xE, 0x14, … | FUN_14330f750 / f870 / 143310350 / 143317000 / 143317d10 | Only Lua: MiniGame:*, StarCounter:IncrementCounter, GroupHandler:SetKO, HUD:showMinigame, FlashTimer:ResetTimer, GameEvents:OnMiniGameFirst* — all undefined. No direct chat or notification call (the depth-3 "chat" hits are shared helpers). No AS3 movie references MiniGame [P] | none (site games use 0x10D) | NO UI | – |
| ClientActivityLaunchPacket{AddMember, InviteMemberReply, InviteMemberResult, UpdateMemberState, KickMemberEvent} | 0xA8 / 6, 1, 2, 8, 0xD | receivers FUN_143715630 / 5a20 / 5c70 / 64e0 / 5eb0 (dispatcher FUN_1437168e0) | Logs ("Received …") and `ActivityEvents:*` Lua, which is undefined [P] | none (the Game Finder is 0x10D) | NO UI | – |
| ClientActivityLaunchPacket{InviteMemberRequest, InviteResponse, KickMemberRequest, MemberQuit, MemberReady, OwnerLaunchRequest, OwnerMatchmakingRequest} | 0xA8 / 0x11-0x17 | FUN_143716c10 … 143717020 | These are **client→server senders** ("Sent PlayerInvite", "Sent ReadyNotification", …), not S→C. coverage-695 lists them as S→C by mistake [P] | – | NO UI (not S→C) | – |
| CoinStoreBuyBackResponsePacket, CoinStoreGiftTransactionCompletePacket, CoinStoreItemDefinitionsResponsePacket, CoinStoreTransactionCompletePacket | 0x9E / 0xD, 0x12, 0x3, 0x6 | FUN_143561b50 / 143566fc0 / 143567e80 / 143561f50 | Log only, except TransactionComplete (chat line). **New:** the MerchantWindow *is* live UI in 0.695: CoinStoreMerchantListPacket (FUN_143567820) → FUN_14356c220 → `HandlerMerchant.Show()` → MerchantHandler:Show (MerchantHandler.lua:5) opens MerchantWindow [P] | No NPC merchants on live. A dev wrote "Once our Merchant and Currency systems are implemented" (design/forum); it was never shipped [W] | UNREACHABLE (dormant UI; nothing opens it) | – |
| PacketMountResponse / PacketDismountResponse / PacketSeatChangeResponse | 0xA1 / 0x2, 0x4, 0xB | FUN_142f0e610 / e480 / e680 | Log, and SeatChange's undefined GameEvents:SetAliveVehicleGameState / SetKillCamGameState [P] | wiki: "no mounts in Landmark" [W] | NO UI / UNREACHABLE | – |
| SocialPacketNotification / SocialPacketProfileInfo | 0x9C / 0xD, 0x1 | FUN_142f674e0 / 142f68460 | Free Realms SN_* texts (high scores, challenges, diplomacy). No Lua or notification sink found [I-auto] | none | NO UI | – |
| PacketClientNotifyCoinSpinAvailable | 0xA4 | zone dispatcher case → FUN_142d220a0 queues Lua `HUD:ShowCoinWheel` | `HUD:ShowCoinWheel` undefined [P] | none | NO UI | – |
| PacketMembershipActivation | 0x66 | zone dispatcher → FUN_142fd3bd0 (membership store), FUN_142ec2ff0 (player flag), 2× FUN_142c41640, then the undefined MembershipHandler Lua | Membership state updated [P]. No Lua listener | membership decided (memory: store-membership-decisions) | **SENT already** (Game/Membership.cs:342). The row is out of date | – |
| PacketClientMembershipVipInfo | 0xB3 | zone dispatcher → FUN_142ec6dc0 / FUN_142ecc900 (loyalty data on the player) + MarketplaceHandler:OnLoyaltyInfo (undefined) | stores loyalty data; no visible reaction found [I] | none | NO UI | – |
| SelectQueueForUserPacket | 0x8A / 0xC | FUN_1433bcaf0 | `Lobby:JoinQueueWithID` undefined [P] | none | NO UI | – |
| CommandPacketStartFlashGame | 0x1E / 0xE | FUN_1433bc220 | no sink found [I-auto] | none | NO UI | – |
| PacketClientFlashTimer | 0xAA | zone dispatcher | RaceTimer:* / FlashTimer:* only, all undefined; no AS3 movie [P] | none | NO UI | – |
| StartTimerPacket | 0x32 / 0x9 | case 9 → FUN_1433ff690 "UIProcessor::ShowTimer" | `FlashTimer:SetTimerDisplay` / `FlashTimer:Show` undefined [P]. The StoryTools Timer node is server-side and does not need it | none | NO UI | – |
| UiMessagePacket | 0x32 / 0xE | case 0xe | **Visible:** a red ShowSystemMessage + chat line with QuestLimitMember / QuestLimitNonmember, then the undefined QuestHandler Lua [P] | quest limit = EQN quests [W neg] | UNREACHABLE (UI would react) | – |
| ActivityProfileListPacket | 0x8F / 0x1 | (coverage's "reader" FUN_142cf15f0 is the Lua-param helper, not a reader) | ActivityPortal:* undefined | – | **SENT already**: an empty list at login, MiscHandler.cs:336 `ActivityManagerBase "01-00-00-00-00"`. The row is out of date | – |
| WeaponHitFeedbackPacket | 0x32 / 0x10 | case 0x10 → FUN_1433ff920 | ReticleHandler:OnEnemyHit / OnAllyHit (undefined), **and** FUN_1431f8000(SoundPlayer DAT_145431358) picks a sound from the SoundPlayer's ActorSoundEmitter config (+0x3d0) and plays it (FUN_143db04a0): an **audible hit-confirm sound** [P]. The visual hit marker comes from health updates (Health.cs) | none found (no "hit sound" in wiki / forum / notes) | NO UI (visual) / lead: audio only | – |
| ReportPacketReportPlayerReply | 0x1E / 0x3C (Command id table cCommandPacketIdReportPlayerReply; request ReportPlayer is 0x47 / 1) | FUN_1433bf720 | A system message + chat line [I-auto] | – | UNREACHABLE: Ui.ReportAsOffensive has no caller. /reportclaim uses ClaimReportSubmission 0x56 (handled, Reports.cs) | – |
| RaidPacketAddGroup | 0xB8 / 0x2 | FUN_142dee0f0 | `GameEvents:OnGroupJoin` **is defined** (events.lua:894 → LandmarkHUD group UI) [P] | no raids in Landmark; no Raid natives in luabind_695 [P/W neg] | UNREACHABLE (UI would react) | – |
| RaidPacketRemoveRaid | 0xB8 / 0x2 | FUN_142deebd0 | Chat-window line (UI_RaidPacketRemoveRaid*) [P] | as above | UNREACHABLE | – |
| RemoteWeaponRemovePacket, RemoteProjectileLaunchPacket, RemoteWeaponEmptyPacket, RemoteWeaponSwitchFireModePacket | 0xBA / … | FUN_1435abd00 / 1435abee0 / 1435ab030 | Log (WeaponErrors) only [I-auto]. Weapons tables have 0 rows [P] | none (PS2) | NO UI | – |
| SkillPacketAddSkill | 0xBC / 0x6 | FUN_142f56020 | Queues `OnSkillComplete`, which NotificationManager does not define → nothing [P] | none | NO UI | – |
| VehiclePacketLoadVehicleTerminalDefinitionManager | 0xBF / 0x8 | FUN_143c06b10 | log only | none (0 vehicle rows) | NO UI | – |
| VehicleDamageInfoPacket | 0xBF / 0xB | FUN_142f70a00 | `ReticleHandler:OnPlayerDamage` undefined [P] | none | NO UI | – |
| MissionPacketAddGlobalAvailableMission / AddAvailableMission / AddActiveMission | 0xD1 / … | FUN_142f0d460 / 0b0b0 / 0afc0 | log (MissionsError) [I-auto] | none | NO UI | – |
| FogOfWarZoneDataReplyPacket | 0x103 | zone dispatcher | log only [I-auto] | none | NO UI | – |
| PacketMailClearInboxResponse | 0xF8 / 0xE | FUN_1433765a0 | Success text [I-auto] | – | UNREACHABLE: no ClearInbox native in luabind_695, no Inbox AS3 caller [P] | – |
| RecipeRemoveUpgradeResponsePacket, RecipeUpgradeStatResponsePacket | 0x42 / 0xD, 0xF | FUN_142f39870 | Upgrade texts [P 0.589] | – | UNREACHABLE: CraftingWindowWidget shows the Upgrade tab only for station type 3, and no 0.695 CraftingStations row has type 3 (agent-l138-c2 §6) [P] | – |
| TemplatePacketEnvironmentInstanceDetailsResponse | 0xF0 / 0x13 | FUN_143104c10 | no sink found [I-auto] | – | NO UI | – |

**Totals for the 89:** BUILD 0. SENT already (row out of date) 2: MembershipActivation, ActivityProfileList.
UNREACHABLE, though the 0.695 UI *would* react if sent: Quest family, Objective Complete / Fail, UiMessage, Raids, the
chat-room responses, Upkeep / Snapshot / Salvage / ClearInbox / Report replies, CoinStore. NO UI: everything else.

## 2. Other never-sent S→C classes where the scan found a UI reaction

Server status checked by hand for every row here.

| Class | 0.695 opcode | Handler | UI reaction | Live evidence | Verdict | Layout if BUILD |
|---|---|---|---|---|---|---|
| **BasePreferencePacket** (+ C→S PreferencePacketClientXml) | 0x03 (u32 kind 1) | zone dispatcher FUN_142d1ccf0 case 1 (reader FUN_142c4e700) | When kind == 1: parses the XML into the preference store (param_1[0x6450], FUN_142c50880), then runs **ChatManager:RestoreSavedChatWindows** (defined, ChatManager.lua:84). That reads Chat.GetChatTabs and moves / sizes the main chat window [P] | **[P] live capture**: the commented line MiscHandler.cs:388 holds a live PreferencePacket: `01000000 8e000000 <Root Version="1.0"><World/><Zone/><Client><UI><Chat><ChatWindows IsList="1"/></Chat><ActionBar AreActionBarsLocked="0"/></UI></Client></Root>`. The server does not handle C→S 0x03 | **BUILD** | see §5 #1 |
| ChatPacketEnterArea | 0x13 / 0x2 | FUN_143364980 → FUN_14336cce0 | Chat line "EnteringChatArea" + area text, on wire channel 11 (Error) [P] | none: General is registered by the client itself; no area chat in design sources [W neg] | UNREACHABLE (lead, low) | – |
| ChatPacketDebugChat | 0x13 / 0x3 | same | AdminSystemMessage notification + chat (debug) | dev only | UNREACHABLE (dev) | – |
| PacketTargetClientNotOnline | 0x1A | zone case 6 | Chat line PlayerNotOnline + name [P] | No 0x19 client sender exists (server-internal routing). Whisper-offline is already sent as PacketChat kind 3 (Chat.cs:261) | UNREACHABLE (lead) | – |
| BaseBroadcastPacket | 0x4F | FUN_142cd9130 | "System Broadcast: %s" / "Broadcast from %s: %s" (System / Zone / World formats, Global.Text.%d ids) in chat + `BroadcastMessage` on-screen notification [P] | The client has no sender (AdminCommandPacketBroadcast* exist only as id-table names; no compiled class) [P]. No live mention [W neg] | NO live trigger → lead (server-ops broadcast from the web admin, [I]) | {u16 kind (0 player / 1 system…), u32 text id or 0, str sender, str message} — pkt layout FUN_142c17410 still to fix before building |
| PacketClientPendingKickFromServer | 0x65 | zone case 0x2b | ShowSystemMessage KickedFromServerTimedMessage with a seconds countdown [P] | none found | lead (could front an admin kick / shutdown; [I]) | – |
| CombatPacketBlockBroken | 0x24 / 0xE | → FUN_1433cace0 → `OnBlockBrokenSelf` (global.lua:1073) → HudStateManager:OnBlockBrokenSelf | The body is commented out (hudstate.lua:491-496) → **nothing** [P] | – | NO UI (existing "informational" stands) | – |
| Abilities::AbilityPacketAbilityStartFailed / StartCanceled | 0x27 / 0x10, 0x11 | FUN_142e1aab0 → FUN_142e1c040 / FUN_142e1bdf0 | Only when the id equals the client's pending start (+0x2a0, set by FUN_142e0f810): `GameEvents:OnAbilityCastResult(id, 1)` → ModalBar slot error flash (ModalBarAbilitiesDocumentClass.as:87) [P] | CastFail (sent) already clears +0x2a0 and shows Harvest_CastFailMessage (FUN_142e17eb0) | existing "covered" stands. Lead: which failure message live used for the modal-bar flash | – |
| ClientUpdatePacketCollectionStart / CollectionAddEntry | 0x29 / 0x7, 0x9 | FUN_142cd9980 | StartCollection / UpdateCollection / CompleteCollection notifications (defined) [P] | EQN collections; none in design sources [W neg] | UNREACHABLE (UI would react) | – |
| ClientUpdatePacketUpdateLocation | 0x29 / 0xB | case 10 → FUN_142d59f30 → FUN_142d5ce80 "Waiting for Teleport..." | in-zone teleport screen [P] | already traced (OPEN-LEADS short-teleport entry ~line 1564) | existing lead | – |
| ObjectiveUnhidePacket | 0x30 / 0x5 | → FUN_14331cb30 | Chat line `New Objective: "%s"` for a known objective [P] | as Objective family | UNREACHABLE | – |
| CommandPacketInteractionTool | 0x1E / 0x4F | FUN_1433bed90 | NotificationWindow:OnShowMessage + tool change [I-auto] | none | UNREACHABLE (lead, low) | – |
| PacketPlayersInRadius, AdminCommandPacket{Add, Set, Clear}Currency, ListCurrencyRequest, BaseSkillAdminPacket, AdminCommandPacketFriendsListRequest, PacketWipeInventory, PacketItemFind, AdminCommandPacketInGamePurchaseGetAdminReviewsReply, ClaimsPacketIdInventoryListDebugResponse, PlacedEffect{Debug, Create, Destroy} | 0x1E / …, 0x45 / 0x42, 0xF9 / 0x37, 0xEF / 1-3 | various | Admin / debug echoes in chat or system message | GM tools | belongs to E.6 (admin-commands-695.md); not a player feature | – |
| RatingPacketCreateContestReply | 0xA7 / 0x1B | FUN_142da94c0 | System message [I-auto] | no client CreateContest sender in coverage | UNREACHABLE | – |
| AchievementSecretCompletePacket | 0x92 / 0xE | FUN_142e30720 case 0xe | Adds the record and completes it (FUN_142e2d470 / FUN_143553040 / FUN_142e2e390 → OnAchievementComplete, defined) [P] | Riddle goals ("???") were in the Feb 2017 AchievementInitialize capture (goals-audit-695 e.g. 547, 557), so live listed them up front [P] | NO need (lead: none) | – |
| CoinStoreReceiveGiftItemPacket, PacketSeatSwapRequest, RepeatingActivity* (0x8D), ExperiencePacketSetExperience, MissionPacketReportCompletedMission, MiniGameMessagePacket | – | – | depth-3 "chat" hits through shared helpers only; no direct sink in the handler (checked) [I-auto] | none | NO UI | – |
| Vehicle{RemovedFromQueue, UpdateQueuePosition, PadDestroyNotify, NoAccess, ControllerLogOut} | 0xBF / … | – | Error / System notifications | 0 vehicle rows | UNREACHABLE | – |
| ClaimPacketIdSnapshotSaveResponse | 0xF9 / 0x14 | FUN_142dbecb0 | chat texts | snapshots stub | UNREACHABLE | – |
| InstancedGamePlayerRemovalPacket | 0x10D / 0x14 | FUN_1434ee920 case 0x12 → FUN_1434e8cd0 | FUN_1434e2330 clears game / observer (+0x38 / +0x40), runs CloseEndGameScoreboard (global.lua) and OnCloseGameInProgressDialog, and resets chat channel 0x11 (FUN_14336f480) [P] | the server removes players with Membership(false) + State 0 + ObserverRegistration 0 (InstancedGames.cs:1045) | existing "covered" stands. Lead: check whether channel 0x11 / observer camera 0x20 stay set after our removal | – |
| Already SENT (the scan's name search missed them): RecipeAddRecipeResponsePacket (RecipeBook.cs), RecipeCraftWindowEnableResponsePacket (CraftingStations.cs), Prop PickUp / Delete / AvailableTints replies (Props.cs), VoxelConstructionPacketZoneDisplayNotification (replaced, ZoneTerrain.cs:275), ClaimsPacketLostClaimServerConnection (Claims.cs:814), PacketLoginQueueStatus (TravelQueue.cs:268), PacketTeleportPostQueueNotify (TravelQueue.cs:226), PacketMOTD (PlayerManager.cs:132), TradeRejectedPacket, MailAttachItemToComposeResponse | – | – | – | – | no action | – |

## 3. The rest of the never-sent S→C classes: no Lua, notification, chat or system-message sink found

The automated depth-3 walk (§0 limits) found no sink for 348 rows that the server source does not reference by class
name or reader address. Most are PlanetSide 2 / EQN / Free Realms systems whose tables are empty or whose Lua is
undefined. They stay **NO UI [I-auto]** until someone checks data-source writes or GFx invokes. Grouped by family:

- 0x13 Chat: PlayerPublicChatRoomInviteNotificationPacket, PlayerSendChatHistoryPacket (lead: chat history at login?)
- 0x1B: PacketMonitorState · 0x1E Command: MoveAndInteract, PlayDialogEffect, RequestRewardPreviewUpdateReply,
  PlaySoundIdOnTarget, SpotPlayerReply, ItemDefinitionReply / ItemDefinitions (E.6), UITest, AdminCommandPacketZoneFlags
- 0x24 Combat: AttackTargetDamage / AttackerMissed / TargetDodged / Processed / TargetBlocked / TargetParried
- 0x26 PlayerUpdate: 85 rows. Id-table-only subs and sub-opcodes such as UpdateScale, UpdateTemporaryAppearance,
  StopCompositeEffect, ExpectedSpeed, SlotCompositeEffectOverride, Freeze, Knockback, SetLookAt, ThoughtBubble, …
  These are entity state, not UI. Some may be live (e.g. UpdateTemporaryAppearance, agent-leads2-d2 L107) and belong
  to their systems' traces.
- 0x27 Abilities: TargeterStart / Update / End, PlayVfx, UpdateEffect, LowerCurrentCooldown, ClearCurrentCooldown,
  ShapeStart / End, AbilityStart (cooldown UI is data-driven: a lead for the abilities owner, agent-abilities.md)
- 0x29 ClientUpdate: ItemDeleteFailed, CollectionRemove(Entry), AddProfileAbilitySetApl, Add / RemoveEffectTag,
  Delete / ActivateProfile, AddAbility, NotifyPlayer, PhaseChange, DamageInfo, ModifyMovement / Turn / StrafeSpeed,
  UpdateManagedLocation, ScreenEffect, ManagedMovementVersion, UpdateWeaponAddClips, ClientLoyaltyPointsUpdate,
  UpdateAttributes
- 0x2D InventoryPacketCatalystDefinitions (no catalysts, agent-l138-c2) · 0x30 ObjectiveActivatePacket · 0x32 Task*
  (Add / Update / Complete / Fail), ExecuteScriptPacket (parked L143), LoadingScreenPacket (NotificationHandler:
  LoadingZone undefined: the table is NotificationHandler, not NotificationManager), CinematicStartLookAtPacket
  (GameEvents:OnCinematicModeOn undefined), HeadShotFeedbackPacket (`OnPlayerHeadshot` notification undefined),
  ZoneWaypointPacket, InteractStartPacket
- 0x34 Quest: QuestInfo, CompletedQuestCountUpdate, QuestEnd, QuestSelected · 0x35 RewardBasePacket · 0x38
  PacketDelayWrappedPacket
- 0x42 Crafting: RefiningProcess / RefiningState responses, ComponentImageSetIds, PlayerRecipeDataUpdate
  (CooldownRemaining / CooldownTotal data names: recipe cooldowns, lead), UnhideRecipe, RecipePacketZoneCraftingWindow
  (FUN_142f39870 → FUN_142f381b0 / FUN_142f3c630(…,1): closes the station window [I]; lead for "player walked away")
- 0x45 InGamePurchase: real-money store replies (decisions 6.59 / 6.60, parked)
- 0x4B Acquaintance (Add / Remove / Online) · 0x4D FriendUpdateProfileInfo, FriendRename · 0x54 / 0x5C / 0x64 / 0x6A
  session / area / exit-URL / POI · 0x67 / 0x68 Lobby · 0x6E Requisition · 0x74 KeyCodeRedemptionNotification (GameCode
  window exists: lead with store decisions)
- 0x83 Guild: GuildDataFullPacket, GuildInvitationsPacket (FUN_142ef25f0 fills the client's pending-invite list +
  FUN_1433da9d0; lead: invites to offline players), GuildRemoveLogUpdatePacket (lead: news removal), GuildUpdateAppData
  (AppDataFieldSub exists server-side; check)
- 0x8A EncounterMatchmaking (7) · 0x92 AchievementObjectiveAddedPacket · 0x97 · 0x9A ActionManagerPacketUpdateSlot
  (server has UpdateSlot code: check) · 0x9C Social (5) · 0x9E CoinStore (5) · 0xA0 · 0xA1 Mount (4) · 0xA4 · 0xA8 (8)
  · 0xAA · 0xAD · 0xAF Quiz · 0xB3 · 0xB5 TargetPacketAddTarget · 0xB7 GuideStone · 0xB8 RaidPacketAddRaid · 0xB9 Voice
- 0xBA weapons (30, Weapons.txt empty) · 0xBD Loadouts · 0xBE ExperienceRanks · 0xBF Vehicle (16) · 0xC2 Factions ·
  0xCB Equipment (3; server has SetCharacterEquipment code: check) · 0xD1 Missions (7) · 0xDD / 0xDE / 0xDF GuildTag /
  0xE1 ServerPopulationInfo / 0xE4 / 0xE5 / 0xE6 timers
- 0xEC TerrainCell (2) · 0xED PropProcessorStateReplication · 0xEE ProjectileDebug · 0xF0
  TemplatePacketPurchaseTemplateMaterialsDataResponse (the server references PurchaseTemplateMaterialsData: TASKS
  3.6, check) · 0xF2 · 0xF4 OffGridPasteClientRequest · 0xF9 ClaimsPacketsIdUpdateClaimBasicInfoAndAOI (0.589 trace:
  ClaimDialogs data sources: lead) · 0xFB BaseLootPinata · 0x100 DebugDraw (5) · 0x104 SocialShare (3) · 0x10D
  InstancedGamePropState (sent as SubPropState 0x12: name search miss) / UpdatePlayerFlags (lead) · 0x118 Content (2) ·
  0x11A · 0x11B Interaction (4) · 0x11C CharacterCustomizationPacketEquippedAttachmentReply (server references
  EquippedAttachment: check)

Full rows with handlers: scratchpad `s2c/sinks695.tsv` (all 732), `s2c/vis.tsv`, `s2c/noui.txt`.

## 4. Corrections to existing notes found on the way

- coverage-695.md §10 0xA8 lists seven client→server senders (InviteMemberRequest … OwnerMatchmakingRequest) as S→C.
- coverage-695.md 0x8F ActivityProfileListPacket "reader" FUN_142cf15f0 is the ClientLuaParam array getter.
- coverage-695.md "sent (ChatRooms.cs:34 …)" for PlayerPublicChat InviteUser / TransferOwnership is a mis-mapping.
- The 0.589 verdict "SalvageItem: no widget calls it" is wrong in wording (InventoryView.as:280 calls it) but right in
  outcome (`_isSelected` is never true).
- `StartQuest` in NotificationManager.lua is defined but broken (dispatches the message as the event name).

## 5. Ranked BUILD list

### 1. Client preferences round-trip (BasePreferencePacket 0x03 ↔ PreferencePacketClientXml 0x03) — BUILD [P]

What the player sees: the chat window comes back where they left it, at the size they left it, on every machine.
The action-bar lock state is saved too. Today nothing is stored and RestoreSavedChatWindows never runs.

- **C→S PreferencePacketClientXml** (zone opcode 0x03; class built in FUN_142c46a20(DAT_145431088+8, pkt, 1, 1)):
  body = `u32 kind (= 1)`, `u32 len`, `char[len] xml` (an IString at +0x18; same layout as the S→C reader) [P layout
  from the class fields +0x10 / +0x18; the writer itself is still to dump with pkt.py]. Senders: FUN_142d4a390 at a
  client run-state transition (logout / character select, called from FUN_142d086a0 and the Exit path FUN_142d59850),
  plus FUN_1433630b0 / FUN_14336b4c0 in the chat module (the Chat.SaveChatTabs native FUN_1435ad9b0 → FUN_14335fd60
  path [I]). The XML has `Root Version`, `World`, `Zone`, `Client/UI/Chat/ChatWindows`, `ActionBar AreActionBarsLocked`.
- **Server, on receipt:** if kind == 1 and len ≤ 16 KB (cap) and the text starts with `<Root`, store it per character
  (new column / table in the character DB; overwrite, no history). No reply. Log and drop anything malformed.
- **S→C BasePreferencePacket** (opcode 0x03, reader FUN_142c4e700): `u32 kind = 1`, `u32 len`, `char[len] xml`.
  Send it once per zone-in, where the commented live line sits (MiscHandler.cs:388, before the client's chat setup
  finishes). Send the stored XML. With none stored, send nothing, or send the live default from the capture:
  `<Root Version="1.0"><World/><Zone/><Client><UI><Chat><ChatWindows IsList="1"/></Chat><ActionBar
  AreActionBarsLocked="0"/></UI></Client></Root>`.
  Only kind 1 is applied (`local_f498 == 1` in the 0.695 case). Bandwidth: one message of ~150 B – a few KB per
  zone-in; do not resend on re-zone unless it changed (keep a per-session "sent" flag).
- **Verify by the client's reaction** (memory: verify-client-results). Move or resize the chat window, log out (the
  client sends the XML), log back in, and see the window restored (ChatManager.SavedWindowsRestored). Also check the
  ActionBar lock state after relog.
- Toggle: none needed (live behaviour; harmless).

### Candidates that are not BUILD yet (open leads to add; numbers for the coordinator)

| Lead | What | Why not BUILD | Try next |
|---|---|---|---|
| S2C-a | BaseBroadcastPacket 0x4F as an operator "system broadcast" from the web admin | No client sender and no live mention; the UI shows it (chat + on-screen BroadcastMessage) [P] | Fix the body layout with pkt.py on FUN_142c17410; ask the user if an ops broadcast is wanted (the toggle rule allows it) |
| S2C-b | PacketClientPendingKickFromServer 0x65: countdown kick warning | UI reacts [P]; no live trigger found | Search the forum for kick / maintenance warnings; it could front `/kick` or the shutdown notice |
| S2C-c | WeaponHitFeedbackPacket 0x32/0x10: hit-confirm sound | Plays a SoundPlayer sound [P]; no live evidence of a separate hit sound | Read the SoundPlayer config at +0x3d0 (hash 0xcfd6cc9b, FUN_1431f76a0): if it names Landmark combat sounds, live used it |
| S2C-d | GuildInvitationsPacket 0x83/0x14, GuildRemoveLogUpdatePacket 0x1B, GuildDataFullPacket 0x12 | Client stores pending invites / news removals; no Lua sink | Read the 0.695 GuildWindow AS3 data sources for invitations / news; check whether live invites survive logout |
| S2C-e | PacketTargetClientNotOnline 0x1A | Chat "PlayerNotOnline" [P]; no 0x19 client path | Find which client messages the world wraps with a target (ClientToTargetClientsZoneWrapped) |
| S2C-f | InstancedGamePlayerRemovalPacket 0x10D/0x14 vs our Membership + State + Observer sequence | Ours may leave chat channel 0x11 / camera mode 0x20 set | Frida, or read FUN_14336f480(…, 0x11) and FUN_142d4eaa0 |
| S2C-g | PlayerRecipeDataUpdatePacket 0x42/0x1E (recipe cooldowns), RecipePacketZoneCraftingWindow 0x42/0x24 (close the station window) | Data-driven / window close; not covered by the scan | Check the CraftingWidget AS3 for CooldownRemaining; check whether live closed the window when the player walked away |

## 6. verdicts.tsv row updates (to apply; not applied here)

Replace each row's E.2 tag "[E.2 2026-10-17: server→client — 0.695 UI reaction still to check]" with the text below.
Verdicts stay as they are unless a change is shown.

| Row | Verdict | New evidence text (append) |
|---|---|---|
| Claims.DepositUpkeepFunds | unreachable | [E.2 s2c 2026-10-18: 0.695 the same: UpkeepManagementWindow only in manifest.xml:163; OnOpenUpkeepBank opens ClaimManagementWindow; as3_695 only ClaimsWrapper.as:169] |
| UpkeepManagementWindow.DepositUpkeepFunds | unreachable | same |
| ClientInventory.SalvageItem | unreachable | [E.2 s2c: correction: InventoryView.as:280 calls it, but `_isSelected` is never set true (lines 111, 281) → dead code, 0.589 and 0.695] |
| WaypointCooldownPacket | unreachable | [E.2 s2c: 0.695 case 0x13 → FUN_1430d88d0 HUB / WARP waypoint cooldowns; no Lua / UI sink → NO UI] |
| ClientUpdatePacketMana | unreachable | [E.2 s2c: 0.695 FUN_1433fda80 → CombatGroupHandler:SetPower only, undefined in 0.695 Lua + ScriptsBase.bin → NO UI] |
| InventoryPacketReplySalvageItem | unreachable | [E.2 s2c: would print Salvaging.* chat; request is dead code] |
| PlayerPublicChatCreateRoomResponsePacket / DestroyRoom / InviteUser / TransferOwnership | unreachable | [E.2 s2c: handler FUN_143364980 prints chat-window lines, but the 0.695 client builds only Join / Leave (luabind_695); coverage's "sent ChatRooms.cs:34" is a mis-mapping] |
| ClaimPacketIdSnapshotSaveConfirmRequest / RestoreConfirmRequest | unreachable | [E.2 s2c: GameEvents:OnClaimSave / RestoreSnapshotConfirm undefined in 0.695; SnapshotPanel still an empty stub → NO UI] |
| AdminCommandPacketSpawnVehicle / WeaponSet | unreachable | [E.2 s2c: chat-log echo only; Vehicles / Weapons tables 0 rows] |
| ObjectiveCompletePacket / ObjectiveFailPacket | unreachable | [E.2 s2c: 0.695 UI WOULD react (NotificationManager ObjectiveComplete / ObjectiveFailed) for an objective made by ObjectiveActivate; no live objective system (goals use 0x92)] |
| ObjectiveLookAtPacket / ObjectiveClientClearPacket / ObjectiveClientCompleteFailedPacket / ObjectiveUIEventPacket | unreachable | [E.2 s2c: client objective tracker FUN_14371f… logs only → NO UI] |
| ObjectiveTargetUpdatePacket | unreachable | [E.2 s2c: MiniMap / ObjectiveWindow Lua undefined → NO UI] |
| Quest* (12 rows) | unreachable | [E.2 s2c: 0.695 UI WOULD react: chat lines QuestStarted / QuestCompleted and CompleteQuest / FailQuest / UpdateObjective notifications (FUN_1433f84c0 / f6a50 …); QuestHandler / EQNScreenText undefined; no live quests] (QuestObjectiveActivated: NO UI) |
| BaseMiniGamePacket + MiniGame* (7) | unreachable | [E.2 s2c: only undefined Lua (MiniGame, StarCounter, GroupHandler, HUD, FlashTimer); no AS3 → NO UI] |
| ClientActivityLaunchPacket* (12) | unreachable | [E.2 s2c: receivers log + ActivityEvents Lua (undefined) → NO UI; 0x11-0x17 are C→S senders listed as S→C] |
| CoinStore* (4) | unreachable | [E.2 s2c: replies log only; the MerchantWindow is live UI (CoinStoreMerchantList → MerchantHandler:Show) but nothing on live opened it (no NPC merchants)] |
| PacketMountResponse / DismountResponse / SeatChangeResponse | unreachable | [E.2 s2c: no UI sink; wiki "no mounts in Landmark"] |
| SocialPacketNotification / ProfileInfo | unreachable | [E.2 s2c: no sink found → NO UI] |
| PacketClientNotifyCoinSpinAvailable | unreachable | [E.2 s2c: HUD:ShowCoinWheel undefined → NO UI] |
| PacketMembershipActivation | **sent** | Game/Membership.cs:342 (activation type); handler updates the membership store; MembershipHandler Lua undefined |
| SelectQueueForUserPacket | unreachable | [E.2 s2c: Lobby:JoinQueueWithID undefined → NO UI] |
| CommandPacketStartFlashGame / PacketClientFlashTimer / StartTimerPacket | unreachable | [E.2 s2c: FlashTimer / RaceTimer Lua undefined, no AS3 → NO UI] |
| UiMessagePacket | unreachable | [E.2 s2c: 0.695 UI WOULD react (red system message QuestLimitMember / Nonmember); quest limit not live] |
| ActivityProfileListPacket | **sent** | MiscHandler.cs:336 (empty list at login); ActivityPortal Lua undefined |
| WeaponHitFeedbackPacket | unreachable → **parked** | [E.2 s2c: plays a SoundPlayer hit sound (FUN_1431f8000); visual marker comes from health; live use unknown → lead S2C-c] |
| ReportPacketReportPlayerReply | unreachable | [E.2 s2c: would show a system message; ReportAsOffensive has no caller] |
| RaidPacketAddGroup / RemoveRaid | unreachable | [E.2 s2c: UI WOULD react (OnGroupJoin is defined; chat line); no raids, no Raid natives] |
| PacketClientMembershipVipInfo | unreachable | [E.2 s2c: stores loyalty data; MarketplaceHandler Lua undefined → NO UI found] |
| ClaimsPacketAddUpkeepFundsResponse / ClaimPacketIdSnapshotRestoreResponse | unreachable | [E.2 s2c: chat / system texts; requests unreachable] |
| RemoteWeapon* (4), SkillPacketAddSkill, Vehicle* (2), Mission* (3), FogOfWarZoneDataReplyPacket | unreachable | [E.2 s2c: log only or undefined Lua / notification (OnSkillComplete, ReticleHandler) → NO UI; tables empty] |
| PacketMailClearInboxResponse | unreachable | [E.2 s2c: no ClearInbox native in 0.695] |
| RecipeRemoveUpgradeResponsePacket / RecipeUpgradeStatResponsePacket | unreachable | [E.2 s2c: Upgrade tab needs station type 3; none in 0.695 CraftingStations (agent-l138-c2 §6)] |
| TemplatePacketEnvironmentInstanceDetailsResponse | unreachable | [E.2 s2c: no sink found; request unreachable] |
| *(new row)* BasePreferencePacket | **gap → build** | 0.695 zone case 1 → RestoreSavedChatWindows; live capture MiscHandler.cs:388; trace/0695/agent-s2c-sweep.md §5 #1 |
| *(new row)* PreferencePacketClientXml | **gap → build** | C→S 0x03 kind 1 XML; senders FUN_142d4a390 / FUN_1433630b0 / FUN_14336b4c0 |
