# System: Command family (0x1E, u16 sub) — interaction, social helpers, dialogs, admin

Status: [~] request list complete (families/001E_Command.md: 86 requests, 36 replies); per-request reading open.

## Player-facing requests not handled by the server

| Sub | Request | Sent by | Live purpose |
|---|---|---|---|
| 0x4 | EndDialog | Lua DialogEnd | close an NPC dialog |
| 0x6 | PlayerPlaySpeech | | play a speech line |
| 0x7 | DialogResponse | | choose a dialog option |
| 0xA | InteractCancel | | cancel an interaction in progress |
| 0xC | **InteractionSelect** | Lua RadialMenuClicked | choose an entry in the right-click radial menu |
| 0xF | SetProfile | | switch profile (class / loadout) |
| 0x14 | ConfirmFriendResponse | | accept / refuse a friend request |
| 0x15 | SetChatBubbleColor | Lua SetChatColorFgBgSz | chat bubble colours |
| 0x18 | FriendsPositionRequest | Lua GetFriendPositions | friends on the map |
| 0x1D | ProfileDefineSlot | | |
| 0x21 | SetActiveVehicleGuid | | vehicles (none in 0.589) |
| 0x22 / 0x23 | ChatChannelOn / Off | Lua ChannelOn / ChannelOff | join / leave chat channels |
| 0x24 | RequestPlayerPositions | Lua RequestPlayerPositions | player markers on the map |
| 0x33 | ChargeCollision | | |
| 0x34 | HexPermissions | | |
| 0x37 / 0x38 | HelpRequest / HelpOffer (ammo, healing, repair, transport) | Lua Request*/Offer*ByGuid | PlanetSide-style; check 0.589 UI |
| 0x39 | RedeployRequest | Lua Redeploy | PlanetSide-style |
| 0x3B | AFKRequest | | /afk (PlayerUpdate 0x57 AFK reply) |
| 0x47 | ExecuteCommand | | console / slash command to the server |
| 0x49 | **ItemDefinitionRequest** | | the client asks for an item definition it doesn't have → reply 0x4A |
| 0x51 | TeleportToLocation | | (admin / dev) |
| 0x5B | Random | | /random dice roll |

Handled: 0x9 InteractRequest, 0x10 / 0x12 friends, 0x16 SelectPlayer, 0x1A QuestAbandon, 0x1C ActivateProfileFailed,
0x20 Ignore, 0x27 RequestRewardPreviewUpdate, 0x2A RequestPlayIntroEncounter, 0x2B / 0x2D Spot, 0x2E
InteractionString, 0x35 DebrisLaunch, 0x40 NameChange, 0x41 NameValidation, 0x42 GoHome, 0x43 ClientCommand, 0x4C
InteractionItem, 0x5A BuilderMode.

## Admin / debug (sub ≥ 0x3EA): admin builds only

PacketAddItem / DeleteItem / GetItemList / GetItemInfo / VaultItemById / RandomItem / CollectionItemById /
OverflowItemById / BuybackItemById, profile admin (add/delete/list/select/rank/xp/group), GameTime / ClientTime,
GotoRequest, HideMe, AddAmmo, path debug, monitors, VerifyAdminTarget, ZonePlayerCount, force friend add/remove,
Runspeed, acquire timers, DeleteAllProps, SetModel, FreeFlyHide, Storybricks diagnostics, TeleportManagedToLocation,
RendererDump. Useful as GM tools later (they arrive only if the client runs with admin rights).

## Replies (0x1E server → client, 36): to be listed with handlers in the next pass.
