# Request variants: one client message, several senders

Many client → server messages are built by more than one place in the 0.695 client: a window, a slash command, the
right-click menu, a Game Action Queue popup, a Lua native. The senders often fill the **same packet differently** — one
names a player by its typed name with guid 0, another by guid with an empty name; one fills an optional field, another
leaves its default. A server that reads only one way answers the other sender wrongly (the 2026-10-18 example:
GuildInvitePacket from the right-click menu carried a guid and an empty name, the server looked the name up and said
"Player not found").

This page lists every client → server message with more than one sender in the 0.695 client, what each sender fills,
and how the server reads it. Audit of 2026-10-21 (branch `local/auditcallers`).

## How the senders were found

- `re/out/luabind_695.tsv` ("sends" / "packet classes" columns): every Lua native and the packet classes it builds; 79
  classes have two or more Lua senders.
- The 0.695 decompile (`re/decomp_all_695`): every function that sets a packet class's vftable (its builders) and every
  caller of those builders — this finds the slash commands (ClientCommandProcessor, registered in FUN_143396930), the
  Game Action Queue popups and other native senders that have no Lua binding.
- The UI: the exported ActionScript (`re/as3`, `re/as3_695`) for which window calls which native with which
  arguments, and `Resources/RadialMenuButtons.txt` for the right-click rows.
- The client's empty guid globals that senders use as "no guid" (DAT_145474880, DAT_145430c58, DAT_145462af8,
  DAT_145473258) are in `.bss` with no writer in the decompile, so they are 0 on the wire [P].

## The right-click menus

`PacketRadialMenuRequest` (HudManagerBase 0xDC sub 1) {u64 target, u32 request type} has five senders, each with its
own request type [P]:

| Request type | Sender | Drawn as |
|---|---|---|
| 1 | an entity in the world (radial input, FUN_14372ebc0) | radial menu (FUN_14372d940) |
| 2 | Friends list: Lua `ClientFriends.HandleRightClick` (FUN_1435e2d20) | context menu |
| 3 | party window: `Groups.HandleRightClick` (FUN_1435f1880, GroupWidget.as) | context menu |
| 4 | guild roster: `Guild.HandleRightClick` (FUN_1435b98e0, RosterView.as / ContactListWidget.as) | context menu |
| 6 | a deletable chat tab: `Chat.HandleRightClick` (FUN_1435ae1d0, ChatDocumentClass.as) | context menu |

The response handler FUN_14372e1c0 sends request types 2-4 and 6 to the social windows' context menu (FUN_143544280 →
ContextMenu.swf), everything else to the radial menu. **The two call a button's Lua function differently:**

- the radial (FUN_14372d940) passes the target guid, then the target actor's full name (empty when the client has no
  actor for that guid), then any `,value` written after the function name in LUA_CALL (`Prop.SetPropEnable,0`);
- the context menu (ContextMenu.as) passes the target guid alone — except `Ui.StartTellByGuid`, which also gets the
  name.

So a native that insists on exactly one argument (Ui.TeleportToFriend, Guild.Kick, Guild.PromoteMember / DemoteMember)
only works from the context menu, and one that needs two (Ui.InspectPlayer) only from the radial; RadialMenuButtons puts
each of them in a menu drawn the right way.

Each request type answers with its own RadialMenuTypes menu (`Game/RightClickMenus.cs`): 2 → 4 (friend), 3 → 5 (party
member), 4 → 6 (guild member), 6 → 7 (chat tab), 1 → 3 (another player) or 1 (a prop the player may edit, in build
mode). **Fixed 2026-10-21:** the request type used to be taken as the menu type, so the party window got the player
menu, the guild roster an empty menu and a chat tab the guild member menu.

Every RadialMenuButtons row (0.695), the native it runs and what reaches the server:

| Menu | Row | Button | LUA_CALL (native) | Arguments it reads | Message | Server |
|---|---|---|---|---|---|---|
| 1 prop | 63 | Script | Prop.SetupScript | guid | ObjectScripting supported actions | handled (story tools) |
| 1 | 56 | Settings | Prop.SetupObject | guid | PropPacketEditSettingsRequest + ToolActivate | handled |
| 1 | 73 / 70 | Light on / off | Prop.SetPropEnable,1 / ,0 (FUN_1435fbee0) | guid, arg 3 = the `,value` | PropPacketSetPropEnable {u64, bool} | handled |
| 1 | 55 | Reposition | Prop.RepositionObject | guid | PropAccess / ProcessorState / Save | handled |
| 1 | 2 | Adjust | Prop.AdjustObject | guid | same | handled |
| 1 | 18 | Duplicate | Prop.DuplicateObject | guid | PropDuplicatePacket | handled |
| 1 | 17 | Send to Inventory | Prop.PickUpObject (FUN_1435fade0 → FUN_142fb66a0) | guid | PropPickUp {guid, u8 0} | handled |
| 1 | 64 | Recycle | Prop.DisassembleObject (FUN_1435fae40 → FUN_142fb63e0) | guid | PropPickUp {guid, u8 1} | handled |
| 1 | 3 | Delete | Prop.DeleteObject (FUN_1435faa80 → confirm → Prop.ConfirmAction) | guid | PropDelete | handled |
| 1 | 74 | Help (0.695) | Prop.OpenHelpWindow | guid | none (client page) | — |
| 3 player | 21 | Invite to Party | Groups.GroupInviteByGuid | guid | GroupPacketInvite by guid | handled |
| 3 | 19 | Start Trade | Ui.InitiateTradeByGuid | guid | TradeInitiate | handled |
| 3 | 15 | Add Friend | Ui.AddFriendByGuid | guid (name if the actor is known) | AddFriendRequest | handled |
| 3 | 25 / 27 | Kick / Make Party Leader | Groups.GroupKickByGuid / SetGroupOwnerByGuid | guid | GroupPacketKick / SetGroupOwner | handled; **now offered** (members, as leader) |
| 3 | 41 | Revive | Ui.RevivePlayer (FUN_1435c4890) | guid | ability 246 cast | handled; **now offered** (dead target) |
| 3 | 57 | Invite to Guild | Guild.Invite (one-argument path) | guid | GuildInvitePacket {guild, guid, ""} | fixed aded215 |
| 3 | 75 | Inspect (0.695) | Ui.InspectPlayer (FUN_1435c45b0, needs 2 args) | guid | RequestInspectPlayer | handled; **now offered** |
| 4 friend | 24 | Invite to Party | Groups.GroupInviteByGuid | guid | GroupPacketInvite | handled |
| 4 | 16 | Remove Friend | Ui.RemoveFriendByGuid | guid | RemoveFriendRequest {"", guid} | fixed f829c8e |
| 4 | 23 | Teleport to Friend | Ui.TeleportToFriend (FUN_1435c91f0, exactly 1 arg) | guid | ability 187 cast | handled (any online player, other worlds too) |
| 4 | 28 / 30 | Kick / Make Leader | as 25 / 27 | guid | as above | handled; offered to party members as leader |
| 4 | 34 | Add Friend | Ui.AddFriendByGuid | guid | AddFriendRequest | not offered (the target is a friend) |
| 4 | 58 | Invite to Guild | Guild.Invite | guid | GuildInvitePacket | fixed aded215 |
| 4 | 65 | Send Tell | Ui.StartTellByGuid | guid, name | none (opens a tell) | — |
| 5 party | 36 | Leave Party | Groups.GroupLeave | — | GroupPacketLeave | handled; own row |
| 5 | 39 | Disband Party | Groups.GroupDisband | — | GroupPacketDisband | handled; own row, leader |
| 5 | 35 / 37 | Kick / Make Leader | as 25 / 27 | guid | as above | handled; other rows, leader |
| 5 | 38 | Add Friend | Ui.AddFriendByGuid | guid | AddFriendRequest | handled |
| 6 guild | 47 | Invite to Party | Groups.GroupInviteByGuid | guid | GroupPacketInvite | handled |
| 6 | 52 | Send Tell | Guild.StartChatToMember | guid | none | — |
| 6 | 68 | Teleport to Guildmate (0.695) | Ui.TeleportToFriend | guid | ability 187 cast | handled (no friendship needed) |
| 6 | 48 | Send Mail | Guild.OpenComposeWindowForTarget | guid | none (compose window, To = name) | — |
| 6 | 49 / 50 | Ignore / Stop Ignoring | Ui.IgnorePlayerByGuid / StopIgnoringPlayerByGuid | guid | IgnoreRequest by guid | handled; **now chosen by the ignore list** |
| 6 | 42 / 46 | Promote / Demote Rank | Guild.PromoteMember / DemoteMember (exactly 1 arg) | guid | GuildChangeMemberRank | handled (+ transfer, below) |
| 6 | 44 | Kick From Guild | Guild.Kick (exactly 1 arg) | guid | GuildKickPacket {guild, guid, ""} | handled |
| 7 chat tab | 67 | Close | Chat.RequestCloseChatTab (FUN_1435ae180 → FUN_143360270) | tab id | LeaveRoomRequest for a room tab | handled |

Which rows are listed per target is ours [I] (live's rules are not in the client): a row is left out when it cannot
apply — Revive on the living, Kick / Make Leader outside the party or when not leading it, Add Friend for a friend,
Ignore / Stop Ignoring by the list (`RightClickMenus.Buttons`).

## Every message with more than one sender

Status: **fixed** (this audit, commit below), **ok** (every variant read correctly, checked in the code named),
**same** (every sender writes the same layout; only values differ), **n/h** (the server does not handle the message at
all — not a variant problem; see trace/0695/coverage-695.md), **open** (none left; see Former open leads).

### Social: who is named, and how

| Message | Senders and what they fill | Server | Status |
|---|---|---|---|
| CommandPacketAddFriendRequest 0x1E/0x10 {string name, u64 guid} | Ui.AddFriend and /friend add (CommandFriend FUN_1433a5e60): name, guid 0 (FUN_142d4a760); Ui.AddFriendByGuid: the actor's name when the client knows it, else "" + guid (FUN_142d4a9c0) | name, else guid (Game/Friends.cs HandleAdd, SocialServer FriendService.HandleAdd) | ok |
| CommandPacketRemoveFriendRequest 0x1E/0x12 {name block, u64 guid} | Ui.RemoveFriend: name; Ui.RemoveFriendByGuid: empty + guid | Internal/Social/FriendRemoveRequest.cs | ok (f829c8e) |
| CommandPacketIgnoreRequest 0x1E/0x20 {u64 guid, name block, u8 on} | Ui.IgnorePlayer / StopIgnoringPlayer and /ignore (CommandIgnore: thunk FUN_1433a2050 → FUN_1433ac760, add and stop): name, guid 0; ...ByGuid, Block / UnblockPlayerByGuid: guid, empty name; the Friend Invite popup's Ignore (FUN_1433dbd70): the asker's guid | guid, else name (Game/Ignores.cs, SocialServer IgnoreService) | ok |
| GroupPacketInvite 0x2B/1 | Groups.GroupInvite, Ui.InviteToGroup (last argument), /invite (FUN_142dd8450 / FUN_142dd9630): name in the invitee block, guid 0 (FUN_142df14d0); Groups.GroupInviteByGuid: guid, no name (FUN_142df15d0); a GAQ accept (FUN_1433da270 → FUN_1433fc680): the stored invite with the player as inviter | name, else guid (Game/Groups.cs HandleInvite, SocialServer GroupService.Invite); in-process by guid finds only this world's players | ok |
| GroupPacketKick 0x2B/5, GroupPacketSetGroupOwner 0x2B/8 | ...ByName: the client finds the member in its own party roster by name (FUN_142de9d10) and sends the guid; ...ByGuid: the guid (FUN_142df18c0 / FUN_142df1a30 / FUN_142df1d90) | guid at +16 / +8 | same |
| GuildInvitePacket 0x83/4 {u64 guild, u64 invitee, string name} | Guild window (GuildWrapper.Invite(guild, "", name)) and /guildinvite (FUN_1433abd90): guid 0 + name; Guild.Invite one-argument path (menus): current guild, guid, "" | Internal/Social/GuildInvites.cs | ok (aded215) |
| GuildKickPacket 0x83/0x0C {u64 guild, u64 member, string name} | Guild.Kick (roster menu, FUN_1435b7ce0): guid, ""; /guildkick and /guildremove (CommandGuildKickPlayer, FUN_1433abf90): 0 + typed name | Internal/Social/GuildKicks.cs (both servers) | **fixed** 2869412 |
| GuildChangeMemberRankPacket 0x83/0x0A | Guild.PromoteMember / DemoteMember (FUN_142ef91f0): the next rank's name; Guild.ChangeMemberRank (FUN_1435b74d0; no shipped UI calls it): rank name + comment from Lua; **Guild.TransferGuildLeadershipConfirm** (FUN_1435b9cd0 → FUN_142efadc0): the kept promote packet naming the leader rank; /guild_debug (FUN_1433a8940, debug) | named rank, else one step; into the leader rank by the leader = transfer (Internal/Social/GuildRankChanges.cs) | **fixed** de11c3d (transfer) |
| GuildDescriptionUpdatePacket 0x83/0x0F {u64 guild, string, string, string text} | Guild.SetDescription (FUN_1435b64e0) and Guild.RankSetDescription (FUN_1435b8b10): both put the text in the third string and nothing else that tells them apart | the last non-empty string = the guild description | same (a rank description cannot be told apart; see Former open leads) |
| GuildMotdUpdatePacket 0x83/0x0B, GuildQuitPacket 0x83/0x0D | Guild.SetMotd / /guildmotd (FUN_1433ac210): text in the first string; Guild.Quit / /guildquit (FUN_1433ac190): the guild | as filled | same |
| GuildRequestMutePlayerPacket 0x83/0x10 | Guild.MutePlayer and /guild_debug: one builder (FUN_142ef9b80) | Game/Guilds.cs Mute | same |
| Other GuildBase requests (Create, Disband, InviteReply, InviteRevoke, Petition*, Rename, UpdateRank, AppDataField) | one Lua native each, plus /guild_debug (FUN_1433a1e10 → FUN_1433a8940) for the admin | — | same |
| GuildUpdateRankPacket 0x83/0x0E | six Guild.Rank* natives, one per field change, plus /guild_debug | whole rank record (Game/Guilds.cs UpdateRank) | same |
| PacketMailComposeRequest 0xF8/1 {u64 ToGuid, To names, Cc names, subject, body, attachments} | Mail.SendMessage (mail window): ToGuid 0, typed names; **Mail.SendMessageForClaimFeedback** (a site's Feedback button, ClaimFeedback.as): ToGuid = the site owner, "To" empty → one empty name block (FUN_14363f210 → FUN_14363b170 for both) | Mail.ReadCompose keeps ToGuid, drops blank names; Mail.ResolveRecipients (in-process and RemoteMail) | **fixed** fd72b6f |
| ReportPacketReportPlayer | Ui.ReportAsOffensive and /report: both by name through FUN_142d3b820 | by name | same |
| TradeInviteReplyPacket {u32 trade, u8 accept} | Trade.RejectInvite (FUN_143642c70, 0); the trade GAQ's accept / decline (FUN_1433da3c0 / FUN_1433dc5c0 → FUN_143642e20) | as filled | same |
| CommandPacketFriendsPositionRequest | Ui.GetFriendPositions and a ClientCommandProcessor command (thunk FUN_1433a1d50; /friendpositions by its registration [I]) | — | same |
| SocialPacketChallengeResponse | Social.AcceptChallenge / RejectChallenge | — | n/h (duels use the server's own /duel commands) |
| HelpRequestPacket / HelpOfferPacket | Ui.Request* / Offer*ByGuid (guid + a help type) | — | n/h (no 0.695 UI or menu row calls them) |

### Items, chests, mail attachments

| Message | Senders and what they fill | Server | Status |
|---|---|---|---|
| InventoryPacketItemInstanceTooltipRequest 0x2D/0x0F {u32 owner type, u64 owner, u64 item, u32, string, u32 mode} (writer FUN_14350b970) | RequestDetailsForItem / ...Windowed: type 1, owner = the player or (Inspect window) the inspected player; RequestDetailsForItemInChest: type 3, owner = the chest; the upgrade station (FUN_142f39870 / FUN_142f3d970): type 1, mode 2, "Crafting.Upgrade.Tooltip" | TooltipRequest.TryReadInstance: own item, the inspected player's equipped item, the open chest's item (Chests.ItemKindFor) | **fixed** 1a3b78f (was read as a transfer location: chest items and inspected gear got no tooltip) |
| InventoryPacketItemInstanceFullTooltipRequest 0x2D/0x10 | only RequestDetailsForItemInMail (FUN_14351ae30): the attachment's whole item record (FUN_14350baa0) | TooltipRequest.TryReadFull: definition + guid | **fixed** 1a3b78f |
| InventoryPacketItemDefinitionTooltipRequest 0x2D/0x11 {u32 def, u8} | RequestDetailsForItemByDefId, RequestDetailsForRecipeByDefId, the mail coin pane (0x392) | by definition | same |
| InventoryPacketRequestTransferItem 0x2D/0x16 | ClientInventory.TransferItem (owner type 1, container + slot), TransferItemWithOwner (owner type per argument: 1 = container, 3 / 5 = owner guid), FUN_142f24db0 | ItemLocation.Read (owner guid for types 2/3/5), Chests.Transfer | ok |
| InventoryPacketToggleAutoResupply | CoinStoreScreen.TurnOn / TurnOffAutoResupply: {u64 item, bool} | InventoryHandler | same |
| PacketMailConsumeAttachmentRequest | Mail.RetrieveAttachment / RetrieveAllAttachments / PayCashOnDelivery: {u64 mail, u64 attachment} | Game/Mail.cs TakeAttachments | same |
| PacketClaimVendorSetPriceRequest 0x112/2 {u32 slot, u64 listing, u32 price} | LuaClaimVendor.SetItemPriceByGuid: slot -1 + listing; ...BySlot: slot + listing 0 | listing only (Game/Vendors.cs) | same (BySlot has no caller and no row carries a slot; see Former open leads) |

### Build sites, props, tools

| Message | Senders | Server | Status |
|---|---|---|---|
| ClaimsPacketDeleteClaimItem | Claims.DeleteClaim and Claims.RecoverFlag: identical bodies {u64} | Game/Claims.cs | same |
| ClaimsPacketSetClaimTime {u64 claim, i32, f32, i32, f32, u8} | SetClaimTimeHour (FUN_142dd15d0) / SetClaimTimeRate (FUN_142dd1850): both send all four values (the other ones from the claim) | Game/Claims.cs | same |
| ClaimsPacketSetItemIndividualPermission | SetAdvancedSecuritySettings: mask from arg 3; DeleteAdvancedSecuritySetting: mask 0x80000000 | 0x80000000 removes (Game/Claims.cs) | ok |
| CommandPacketBuilderMode {u8 on} | Voxel.ToggleBuildingMode / SetEnableBuildingMode and three native callers, one builder FUN_14346cc90 | BuildModeHolster | same |
| PropPickUpPacket / PropDeletePacket | Prop.PickUpObject (u8 0), DisassembleObject (u8 1), ConfirmAction (delete confirm) and the build tools | PropHandler | ok |
| PropAccess / PropSave / PropProcessorStateNotification / PropPacketEditSettingsRequest / ToolPacketActivate | the Prop.* placement natives and the build tools (FUN_142fb6290 ...); each packet one layout | PropHandler (ToolActivate is informational) | same |
| PropPacketDialogResponse | Prop.OnConfirmNpcDialog / OnDenyNpcDialog | — | n/h |
| AreaDefinitionUpdateDefinitionPacket / TransferShape | the Area.* natives: each sends the whole area record | Game/AreaDefinitions.cs | same |
| TemplatePacketClientVoxelDataRequest | Templates.Paste / PasteInCurrentClaim / Select / PopulateTooltip / SelectAdminContributors and the template windows | Game/Templates.cs | same |
| VoxelConstructionPacketClientUndoRedoRequest | VoxelClipboard.Undo / Redo and two ClientCommandProcessor commands (thunks FUN_1433a2350 / FUN_1433a2650, CommandUndo / CommandRedo by their registration [I]) | Game/Building.cs | same |
| UIEventTriggeredPacket | Ui.UIEventTriggered, VoxelClipboard.Paste, three native callers | MiscHandler | same |
| PalettePacketAddEntry | RadialMenu.AssignPaletteSlot, Voxel.SelectShape, FUN_142f13590 | Game/Palettes.cs | same |
| InstancedGameRequestPacket | Claims.Join / Leave / Respawn / Gain / LoseActiveAuthorship, InstancedGame.SwitchTeam, GAQ accept / decline (FUN_1433da170 / FUN_1433dc3c0): a request type field | Game/InstancedGames.cs | same |
| InstancedGameSetConfigurationRequestPacket | ApplyGameSettings / ResetGameSettings | Game/InstancedGames.Editor.cs | same |

### Travel, death, encounters

| Message | Senders | Server | Status |
|---|---|---|---|
| WarpNetworkClientRequestPacket {u32 type, u32 zone, u32 world, u64 from, u64 to} | WarpNetwork.UpdateZoneNetwork and FUN_143172c60: type 1; WarpToNode and Teleport.TravelConfirmation (FUN_143505cf0): type 2 with the nodes | Game/WarpNetwork.cs | ok |
| PacketZoneSafeTeleportRequest (no body) | Ui.TeleportToSafePoint, Voxel.EvacLocalPlayerToSafepoint | Health.EvacuateToSafePoint | same |
| PlayerUpdatePacketRespawn {type, ...} | Respawn.Respawn (type from Lua), ReviveSelf (10, FUN_1434350f0), ReviveAtHub (8), ReviveAtGraveyard (11), ReviveFlee (12) | 10 here (Lumen), anything else the nearest respawn site (Health.HandleRespawn) | ok (8 has no 0.695 UI caller) |
| PlayerUpdatePacketReadyToReviveResponse | Respawn.AcceptRevivalRequest / DeclineRevivalRequest, FUN_142cdfa60 | PlayerHandler | same |
| EncounterParticipantRequestExitPacket | Ui.QuitMiniGame, Ui.LeaveMiniGameOrEncounter, FUN_142d06480 | Game/Encounters.cs | same |
| PacketDismountRequest | Mount.Spawn / Despawn / Dismount and four native callers | logged only (no mounts) | same |

### Commerce, store, Showcase

| Message | Senders | Server | Status |
|---|---|---|---|
| PacketInGamePurchaseCommerceSearch {string, u8} | InGamePurchaseStoreScreen.SearchRemoteStoreBundles and the world map's claim tag filter (Ui.WorldMapClaimTagFilterUpdate → FUN_142d88400): one builder FUN_14326e2c0 | Game/Commerce.cs | same |
| PacketInGamePurchaseCommerceProductCreate / IsTemplateValid / ProductRemove / ProductUpdate / RequestAccountStore / ModifyScreenshots / PointOfNeed / WalletInfo | the store screen's natives and the site window's (Claims.ListClaimOnGallery, DelistClaimOnGallery, EnterContest, RemoveFromContest, TakeScreenshot ...): each packet one layout | Game/Commerce.cs, SiteScreenshots, InteractionHandler | same |
| CoinStore*, WallOfDataMarketplacePacket, Recommendations / Follow / UnFollow | store natives | — | n/h (coverage-695.md) |

### Not handled at all (no variant question)

BaseMiniGamePacket (17 senders), Loadout* (CreateLoadout / CopyLoadout, SelectWeaponSet), SkillPacketSelectSkill,
QuestReplyPacket, RequisitionItemsRequestPacket, ContentPacketCommandGoto, RecipeRefiningCommand /
RecipeRemoveUpgrade, QuickChat*: one layout per message where checked; the server does not answer the family
(trace/0695/coverage-695.md). `luabind_695.tsv` also lists QuestEndReplyPacket under Ui.AddFriend / AddFriendByGuid: a
scan artefact, the natives build only AddFriendRequest.

Other places checked for senders: chat (the chat window's links only show tooltips: linkMouseOver / linkMouseOut, no
request), the map (Ui.SendMapPing → GroupPacketMapPing, one sender; the claim tag filter above), the Showcase (its
Lua natives above).

## Fixes (2026-10-21)

| Commit | Message | What was wrong |
|---|---|---|
| 2869412 | GuildKickPacket | /guildkick Name and /guildremove Name (guid 0 + name) were read by guid only |
| 9a5fd54 | PacketRadialMenuRequest | request type used as menu type: party window, guild roster and chat tab got the wrong menu; Inspect and Revive were never offered |
| fd72b6f | PacketMailComposeRequest | the site Feedback mail names its recipient by ToGuid only; the server skipped ToGuid |
| 1a3b78f | Item tooltip requests 0x0F / 0x10 | read as a transfer location; chest items, inspected gear and mail attachments got no tooltip |
| de11c3d | GuildChangeMemberRankPacket | the confirmed "Transfer Guild Leadership" was refused |

## Former open leads (closed 2026-10-26, trace/0695/phaseE2-social.md)

- **Leadership transfer, the old leader's rank — matches.** The client offers the transfer only when the promoted
  member's next rank up is the leader rank (FUN_142ef91f0: the rank above the target, FUN_143c4ed20 → rank +0x110,
  equals the guild's leader rank record), so the new leader always comes from the rank right under the leader; the
  client even keeps that former rank id at guild manager +0xec for the confirm (written, never read) [P]. "The new
  leader's former rank" (ours) and "the rank under the leader" are therefore the same rank.
- **Right-click rows per target — closed on best evidence.** RadialMenuButtons.txt has no condition column; the
  response lists rows with a per-row "selectable" byte, so live could grey rows out or leave them away — server-side,
  recorded nowhere (client, data, wiki, forum, patch notes searched). Ours leaves out rows that cannot apply.
- **Guild.RankSetDescription — matches.** FUN_1435b8b10 checks the rank exists (argument 2) and then fills only the
  third string of GuildDescriptionUpdatePacket, exactly as Guild.SetDescription does [P]; no 0.589 or 0.695 window or
  script calls it (as3, as3_695, UI/Default, Resources/Scripts searched). The packet cannot carry a rank description,
  so the guild description is the only reading.
- **LuaClaimVendor.SetItemPriceBySlot — closed on best evidence.** No window calls it (the 0.695 developer test window
  UI/TestMod/TestGrid.lua proves ListItem's arguments: "LuaClaimVendor:ListItem( data.Slot, data.Quantity, 0 )", bag
  slot and count), and the vendor list's rows carry no slot (FUN_143379e40), so a slot-only SetPrice finds no listing
  (result 3). Live made vendor booths decorative on 1 Apr 2016 (P4378); VENDOR_SHOPS stays off.
- **Feedback mail to a contest winner on another world — matches.** Characters are global: every Docker world shares
  `lmemu_character` (docs/docker.md), so CharacterNameOf finds a character of any world, and the mailbox is the social
  service's, across worlds.
