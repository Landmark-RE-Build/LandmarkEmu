# 0.695 UI windows: what they show, what they call, what the server must feed them (agent #11)

Traced 2026-10-15 from the JPEXS exports (0.695 `re/as3_695/<window>/scripts`, 0.589 `re/as3/<window>/scripts`), the
0.695 Lua / XML (`<0.695 install>\UI\Default`, `Resources\Scripts`, `InputProfile_Default.xml`,
`Resources\RadialMenuButtons.txt`, `HudIndicators.txt`, `FirstTimeEvents.txt`), `re/out/luabind_695.tsv` and the 0.695
decompile (`DECOMP=decomp_all_695 python re/tools/fn.py`). Addresses are 0.695 unless marked 0.589. Packet layouts are
agent #3's (packets-695.md, cited as "#3 §n"); this file only says which packet / data each window needs.

Scratch and tools (re/out/agent_ui): `uicalls.py` → `uicalls.txt` (every CallLuaFunction / CallLuaWindowFunction /
GetLuaValue / wrapper call and `new DataSource("…")` per window, 0.695 vs 0.589, framework folders skipped),
`bindcheck.py` → `bindcheck.txt` (each call: 0.695 / 0.589 native binding, Lua script function, 0.589 windows using it),
`diffs.txt` (every non-trivial script diff), `newbinds.txt` (bindings in luabind_695 but not luabind), `hud_*.c`.

**Scope.** 29 .swf changed (update-0589-to-0695-files.csv): 9 new (InspectWidget, TemplateConfirmationWidget, the
seven modular build windows) and 20 changed; plus two new Lua windows (GameFinder.xml/.lua, DropdownDialog.xml/.lua),
which are the "Game Finder" UI (there is no GameFinder.swf). ExternalInterface.call appears only in framework code
(soe/utils/GameUtils.as, SoeGuiProxy.as); every game call goes through `GameUtils.CallLuaFunction` and data comes from
`DataSource` names, so those are what is traced below.

**Baseline for every row:** a 0.695 client needs TASKS §0 first (login delta, PlayerUpdate u32 sub-opcode, family
renumbering, #3 "Changed layouts"). "Works with today's server" below means: once that baseline is in, the window needs
nothing more.

## Summary

| Window (HUD id) | New / changed | Status | What is needed |
|---|---|---|---|
| InspectWidget (218) | new | needs packet | StartInspectPacket 0x91/1 answering RequestInspectPlayerPacket 0x91/3 (#3 §2, TASKS 3.4); other players' item tooltips (3.22) |
| TemplateConfirmationWidget (219) | new | needs packet | TemplatePacketPurchaseTemplateMaterialsDataRequest/Response/PurchaseMissing 0xF0/0x1D-0x1F (#3 §3, 3.6) |
| PropWindow (220) | new | works with today's server | same bindings as the 0.589 BuildModeWidget prop tab (PropPacketPlacePropById handled) |
| TintsWindow (221) | new | works with today's server | client palette; Prop.SelectPropTintEntry → ToolPacketActivate ("informational", no reply) |
| MaterialsWindow (222) | new | client-only | Voxel.* palette calls send nothing |
| MeshesWindow (223) | new | client-only | Mesh.* → ToolPacketActivate (StampBrush), no shape id on the wire (terrain-695 §5) |
| DesignsWindow (224) | new | needs packet (layout) | Template packets in the 0.695 TemplateBasicData form (#3 §B4); categories from tables (L40) |
| ShortcutsWindow (225) | new | client-only | runs the modal-bar rows' LUACALLs (existing tool activations) |
| ToolsWindow (226) | new | works with today's server | Tool.ActivateTool (informational), Voxel.SelectShape → PalettePacketAddEntry (handled) |
| GameFinder (Lua window) | new | needs packet | InstancedGameMatchmakingRequest/Update 0x10D/0x19/0x1A (#3 §5a-b, 2.12, 3.25) |
| DropdownDialog (Lua window) | new | needs packet | InstancedGameRequestJoinTeamPacket (Request type 9, #3 §5c) + Scoreboard JoinableTeams settings |
| BarberShopWidget | changed | needs packet + data | EquippedAttachmentRequest/Reply 0x11C/3-4, PlayerSet +2 u32 (#3 §9, functions B5); unlock knowledge / store bundles (L39); UI event 33 (3.23) |
| BuildModeHUDWidget | changed | needs data | Play Mode Rules mask in claim records / ClaimInfoUpdate kind 0x17 (#3 §B5) for visitors' camera buttons |
| BuildModeTimeWidget | changed | needs packet | ClaimsPacketSetAdventureFlags 0xF9/0x45 + ClaimInfoUpdate kind 0x17 back (#3 §6, 2.1) |
| BuildModeWidget | changed | works with today's server | modular toggles and Mesh tab are client-only; UI event 34 first-time help (3.23) |
| CharacterCreateWindow | changed | client-only | two label texts |
| Chat | changed | client-only | opacity options; tokenised rich text passed through by the server (2.21) |
| ClaimManagementWindow | changed | needs packet | ClaimsPacketStartClaimPlacement 0xF9/0x46 → Command 0x4D (#3 §7, 3.5); 0.695 claim records (#3 §B5); upkeep data sub 10 already sent (3.26) |
| ClaimWalkOnTextWindow | changed | needs packet + data | site-game trailing u32 ≥ 2 (L37) and setting InstancedGames_AllowSwitch for "Switch Team"; Request type 9 |
| CraftingWidget | changed | client-only | recipe list keeps its selection |
| GuildWindow | changed | works with today's server | Edit button now on every rank incl. the leader rank; Guilds.cs allows renaming it |
| HUDScoreboardWidget | changed | needs packet | 0.695 HUD entry (trailing bool + u64, #3 §B6) and a localized name per entry (Label) (3.24) |
| InstancePropertyEditor | changed | needs data | tokenised rich text stored / relayed (2.21), 0.695 EditableProperties (2.18), PropRequest trailing u32 (ScriptPropId) |
| Inventory | changed | needs packet | PlayerUpdatePacketSetPlayerBio 0x26/0x80 stored + SendSelfToClient trailing bio (#3 §1, 3.4) |
| LoadingScreen | changed | client-only | art only (0 scripts changed) |
| NotificationWindow | changed | client-only | detokenises text (2.21) |
| RulesetObjectSettings | changed | needs packet | GetConfiguration 0x10D/0xD → 0xE, AddOrRemoveRule 0x18, SetConfiguration 0xF, PropRecipe trailing u32 (#3 §5d-e, §B6-B7, 2.12) |
| SelectedTemplate | changed | needs packet | "Get Resources Now" → 3.6 packets; 0.695 template data (#3 §B4) |
| SpeechBubbleWidget | changed | client-only | detokenises text; the question buttons are a HUD indicator, not this swf (2.22) |
| TeleportSpireWidget | changed | client-only | tier label removed, search field fix |
| TeleportWidget | changed | works with today's server | Underground Adventure now always enabled (3.28) |

## New windows

### InspectWidget (widget 218, `widgets/InspectWidget.swf`)
- **Opens:** right-click a player → radial row 75 (`RadialMenuButtons.txt`: menu type 3 = player, button 7, text
  563898 "Inspect", LUA_CALL `Ui.InspectPlayer`) → FUN_1435c45b0 sends RequestInspectPlayerPacket (0x91/3). The window
  opens only when the reply arrives: the reply handler fills `Character.InspectData` (FUN_14308b5c0) and calls Lua
  `OnInspectPlayer` → global.lua 180 → `HudStateManager:OnOpenInspect()` (hudstate.lua, widget id from
  LandmarkHUDWidget.lua 103 `WIDGET_INSPECT = 218`). Input action "Inspect" (InputProfile_Default.xml 57) too.
- **Shows** (com/scaleform/mmo/inspect/InspectWidget.as 15, 108-123): row 0 `CharacterName`, `LuminaryScore`,
  `NumberOfClaims` ("Build Sites"), `BioString` (read-only), `CharacterGuid`; rows 1..n per equipped item `Name`,
  `IconId`, `ItemGuid`, `SlotNumber` (slots 1, 2, 3, 4, 7, 8, 9). It does **not** show NumberOfGalleryListings or the
  blob's unnamed u32s (L28).
- **Calls:** EquipmentItemRenderer.as 53 rollover → `ClientInventory.RequestDetailsForItem(ItemGuid, PlayerGuid)`
  (FUN_1435de7d0 → FUN_143519570) → InventoryPacketItemInstanceTooltipRequest (0x2D/0xF) whose item location carries
  **the inspected player's guid** (FUN_143519570 copies param_3 = owner into the request, local_90). Unless the item is
  in the client's tooltip cache.
- **Server:** StartInspectPacket (#3 §2); the tooltip request for another player's equipped item: today's
  InventoryHandler.HandleTooltip (InventoryHandler.cs 189-199) looks only in the requester's inventory and drops it
  (gap 3.22).

### TemplateConfirmationWidget (widget 219, "buy a Design's missing materials")
- **Opens:** SelectedTemplate "Get Resources Now" (SelectedTemplateDocumentClass.as 364-365) sends
  `Templates.RequestPurchaseTemplateMaterialsData(guid)` (FUN_1435dc520 → 0xF0/0x1D) and opens the widget
  (`HudStateManager:OnOpenTemplateConfirmation`, `WIDGET_TEMPLATE_CONFIRMATION = 219`).
- **Shows** (TemplateConfirmationWidget.as 50-54, 91-125): `Character.Templates.PointOfSale` (filled by the 0x1E
  handler FUN_14310c290) row 0 `TotalLumens`, `TemplateGUID`; rows 1..n `Name`, `ItemDefinitionId`, `Icon`,
  `ItemCountToBeCrafted`, `ItemCountNeeded`; and `Character.Currencies` row "Lumen" `Quantity`. If TotalLumens > Lumens
  it shows "You need N more lumens!" and "Buy Lumens" (`Browser.OpenMarketplace`, in-game marketplace) instead of OK.
  It never reads `FoundLumenRecipeForAllItems` (L29 answer: the UI does not need it).
- **Calls:** OK → `Templates.PurchaseMissingTemplateMaterials(TemplateGUID)` (FUN_1435dc570 → 0xF0/0x1F) and closes.
  No result UI: the result can only show through inventory / currency updates (L29).
- **Client quirk:** the cost line is redrawn only on `Character.Currencies` changes (invalidate("Text") at 63 and 178;
  PointOfSale events only invalidate "ItemList", 173). The widget opens before the 0x1E reply arrives, so the first
  draw reads an empty / previous TotalLumens (L57).
- The swf also carries repair panels (com/scaleform/mmo/repair/*, `Crafting.Repair.*`) copied from RepairWidget; the
  document class has no instance of them (no public var), so they are unused library symbols.

### The modular build windows (widgets 220-226)
Opened from the Build Mode palette when it is collapsed (`expansionState == 0`): BuildModeWidget.as 417 / 437 / 457 /
477 / 498 / 516 / 527 call `HudStateManager:OnToggle{Props,Tools,Tints,Designs,Meshes,Materials,Shortcuts}(autoClose)`
(new in hudstate.lua; each loads its widget into a LandmarkHUDWidget window and passes "Auto-close Window" via
`OnSetAutoClose`). `OnDisableBuildMode` closes all seven. GfxHudWidgets.xml registers ids 220-226 with
`SavesSettings="true"`: the window layout is saved by the client, not the server (TASKS 3.2 → client-only).
Client quirk: hudstate.lua `OnOpenMaterials` compares `window.WIDGET_Materials` (wrong case), so the Materials window
does not close itself when its widget unloads.

All seven bind the same data sources as the 0.589 BuildModeWidget megapalette (`Character.MegaPalette.<Kind>.Entries /
.Categories`, `Character.PropPalette.PropTooltip`): collections built by the client (PaletteCollection* — e.g.
FUN_14374b520 Prop, FUN_14374c670 PropTint, FUN_14374d050 Materials, FUN_143752f60 Template, FUN_143757fa0 Tools; 0.589
FUN_14368f630 …) from data the server already sends (inventory, recipes, design lists). Only the Mesh collection
(FUN_14374d8a0, PaletteCollectionMeshes) is new.

| Window | Calls (binding, address) | Server |
|---|---|---|
| PropWindow 220 | Prop.SelectCategory FUN_1435fb120, SearchPalette FUN_1435fb1e0, FilterPropsByAvailability FUN_1435fb4b0, SelectProp FUN_1435fb180, **StartPlaceNewObject FUN_1435fa9d0** (PropPacketPlacePropById, handled), GetTooltipForProp FUN_1435fb430 | as 0.589 |
| TintsWindow 221 | Prop.SelectPropTintCategory FUN_1435fb910, SearchPropTint FUN_1435fb960, **SelectPropTintEntry FUN_1435fb8b0** (ToolPacketActivate, verdict "informational") | as 0.589 |
| MaterialsWindow 222 | Voxel.SelectCategory FUN_1435fe440, SearchPalette FUN_1435fe500, SelectPaletteMaterial FUN_1435fe4a0, FilterMaterialsByAvailability FUN_1435fe980 | none (client only) |
| MeshesWindow 223 | **new** Mesh.SelectCategory FUN_143607f90, SearchPalette FUN_1436081a0, FilterMeshesByAvailability FUN_143608100, PlaceMesh FUN_143608060 / SelectMesh FUN_143607fe0 (ToolPacketActivate with the StampBrush tool; shape id stays client-side, terrain-695 §5) | none (edit arrives as the unchanged ApplyDiffRequest) |
| DesignsWindow 224 | Templates.SelectCategory FUN_1435dc340, SearchTemplates FUN_1435dc3a0, FilterTemplatesByAvailability FUN_1435dc480, **Select FUN_1435db2c0 / Paste FUN_1435db0d0 / PopulateTooltip FUN_1435db320** (TemplatePacketClientVoxelDataRequest, handled), Template:InspectTemplate (right-click, Template.lua), ToggleTemplatesWindow, GameEvents:CreateTemplateOpen; data `Character.Template.Tooltip.{TemplateData,MaterialItems,Props}` | 0.695 TemplateBasicData / VoxelDataResponse layout (#3 §B4); rollovers send voxel-data requests in bursts (functions-695 §2); categories L40 |
| ShortcutsWindow 225 | data `GameData.Character.ActionBars.ModalBar` (filled by the tool input handler FUN_1434505f0 from the ModalTool tables); click runs the row's `LUACALL` | none new |
| ToolsWindow 226 | **Tool.ActivateTool FUN_143606470** (ToolPacketActivate, informational), **Voxel.SelectShape FUN_1435fda10** (PalettePacketAddEntry, handled); data `Character.MegaPalette.{Tools,Shapes}.Entries` | as 0.589 |

### GameFinder (Lua window `UI/Default/GameFinder.xml/.lua`, not a swf)
- **Opens:** `/gamefinder` (global.lua 11 `BindCommandToLua("gamefinder", "ShowGameFinder()")`) and a new game-menu row
  `Ui.GameFinder` (Data.xml, LuaCallback `ToggleGameFinder`, icon 8312).
- `ShowGameFinder()` calls `InstancedGame.ShowGameFinder` (FUN_143604db0 → FUN_1434ed2e0), which only republishes the
  client's cached values: `GameFinder.MatchmakingInfo` (Players, ActiveGames, QueuedGames, InactiveGames) and
  `GameFinder.GameFlags` rows (Name, ValueIndent = bit index, Enabled) bound by GameFinder.xml 35
  (`/GameData.GameFinder.GameFlags`, rows FFA Games / Team Games / PvE / PvP / In Progress Games).
- **Checkbox** → `InstancedGame.SetGameFinderFlag(DataIndex, Checked)` (FUN_143604dd0; local flag word only).
  **Search / Stop** → `InstancedGame.IsMatchmaking()` (FUN_143604e80) then `StartMatchmaking` (FUN_143604e40) /
  `StopMatchmaking` (FUN_143604e60) → InstancedGameMatchmakingRequest (0x10D/0x19, #3 §5a).
- **Feed:** InstancedGameMatchmakingUpdate (0x10D/0x1A) → FUN_1434e9a50 → Lua `UpdateGameFinder(data)` →
  `GameFinder:UpdateData` (GameFinder.lua 43-63): SearchState "3" → options page, button "Search"; "1" → info page,
  "Search"; anything else → info page, "Stop". Info page texts bind Players / ActiveGames / QueuedGames /
  InactiveGames, the status line binds `StateString` (GameFinder.xml 4, 19-22). Nothing in the UI moves a matched
  player (L31).

### DropdownDialog (Lua window `UI/Default/DropdownDialog.xml/.lua`)
- Generic dialog: `CreateDropdownDialog(title, message, dataSrc, returnProperty, onClose, …)` (DropdownDialog.lua
  129-134); OK returns the selected row's `returnProperty` to the callback (74-88).
- **Only caller:** `InstancedGame.SwitchTeam()` with no argument (FUN_143604510: "Switch Team", "Select a team...",
  data source `InstancedGameScoreboard.JoinableTeams`, return property "GUID", callback "InstancedGame.SwitchTeam");
  the callback re-enters SwitchTeam with the row's GUID and sends InstancedGameRequestJoinTeamPacket (#3 §5c).
  JoinableTeams is filled by the Scoreboard handler FUN_1434e4a40 (0x10D sub 3) from the
  `InstancedGame_AllowPlayersTeam<n>` settings (functions-695 §1). No other `CreateDropdownDialog` reference in the
  0.695 decompile, Lua or AS3.

## Changed windows

### BarberShopWidget (Crimson Parlor)
- Data sources renamed in the exe and swf together: `CharacterCustomization.*` → `CharacterCustomizationData.ActiveSelections
  / .CurrentCost` (BarberShopWidget.as 89, 91; 0.695 exe strings, fillers FUN_1430a37a0 / FUN_1430a3d30 /
  FUN_1430a4720). New sliders Size / Head / Gender / Eyes / Race (265-268); options with `PurchaseRequired` show an
  unlock button whose tooltip names `MarketName` (293).
- **Calls:** unlock / purchase buttons → `OpenMarketplaceTagSearch(MarketName)` (in-game marketplace, global.lua 619),
  `HudStateManager:OnOpenCharacterInventory`, **`Ui.UIEventTriggered(33)`** (401, 410; new id). Purchase →
  `CharacterCustomization.ConfirmCustomization` (FUN_143607d80, PlayerSet with two more u32, functions-695 §3).
- **Server:** #3 §9 (0x11C/3 sent when the Parlor opens, FUN_143607d10 StartCustomization), PlayerSet 0.695 body,
  unlock knowledge (L39) and store bundles for the purchase-gated options (functions-695 §3); first-time help for UI
  event 33 (3.23).

### BuildModeHUDWidget
- BuildModeHudWidget.as 152-193: grid / undo / redo / camera buttons now refresh for everyone; owners without build
  permission get disabled Play / Edit buttons (175); the whole bar is shown when the player has build permission **or
  any camera state** (`Tool.GlobalSettings` CameraState_FlyingCharacter / _Orbit / _Fly) is on (181) — i.e. visitors
  in a Play Mode site see the cameras its Play Mode Rules allow. Needs the claim's Play Mode Rules mask on the client
  (claim record trailing u64 / ClaimInfoUpdate kind 0x17, #3 §B5; functions-695 B6).

### BuildModeTimeWidget (Advanced Build Settings, widget 202)
- TimePanel.as 41-52, 76, 139-144, 400-420: six new checkboxes bound to `Claim.AdventureFlags` (FUN_142dd2480: six
  rows, `Value` = that bit of claim entry +0x380 (0 / 1), `ValueKey` = the bit 1 / 2 / 4 / 8 / 0x10 / 0x20 = Block
  Grappling Hook, Block Boot Abilities, Block Voxel Destruction, Allow Free Camera, Allow Flying Character Camera,
  Allow Orbit Camera). Toggle → `Claims.SetClaimAdventureFlag(0, ValueKey, selected)` (FUN_1435e6470 →
  FUN_142dd08e0 → ClaimsPacketSetAdventureFlags 0xF9/0x45 with the whole new mask).
- Claim argument: the AS passes **0**; FUN_1435e6470 substitutes the current claim (+0x348) when the argument equals
  DAT_145476ff0 (a zero-initialised .data/BSS global with no writer in the decompile, so most likely 0). #3 §6 says "-1"; L55.
- Also: ShowCharacterCheckbox enabled, time drop-downs 8 rows, default position top-right (client only).
- **Server:** TASKS 2.1 — handle 0xF9/0x45, store, answer with ClaimInfoUpdate kind 0x17 so the checkboxes (and every
  client's blocks) update.

### BuildModeWidget
- New tabs meshTab / materialTab / modalTab and "Auto-close Window"; collapsed palette → modular windows (above);
  expanded palette has a Mesh view (MeshView.as: `Character.MegaPalette.Mesh.Entries / .Categories`, Mesh.SelectCategory
  / PlaceMesh / SelectMesh) and `Ui.SetActivePaletteCategory(6)` (client only). Selecting the Mesh view sends
  **`Ui.UIEventTriggered(34)`** (BuildModeWidget.as 337; new id), handled server-side only as goal / first-time hooks
  (UIEventTriggeredPacket, handled; 3.23). "Use Collision" checkbox relabelled "Can Build" (filter).

### CharacterCreateWindow
- CharacterAppearanceWidget.as 63-67: labels "Size" → "Body", Face → "Head". CharacterSelection.lua 41 now resets the
  create window. Client only.

### Chat
- ChatDocumentClass.as 152-156, 511-551: focus / fade-out opacity from dynamic options ChatFocusOpacity /
  ChatFadeOutOpacity (`Options.Get/SetDynamicOptionValue`, existing bindings FUN_1436070d0 / FUN_143606ff0; new Data.xml
  options); the options button opens settings page "Social" (620).
- **Every chat line is detokenised** (315, 331-339): `~[~` → `<`, `~]~` → `>`, `~a~` → `'`, `~q~` → `"`. The only
  producer found is the property editor's rich text (LargeTextInput.tokenizeText, below), so chat shows story text
  with formatting when the server relays it unchanged (TASKS 2.21; origin of other tokenised lines: L54).

### ClaimManagementWindow (Site Management)
- The tree list became a flat ScrollingList (ClaimManagementWindowDocumentClass.as 43, 167-175, 281-325): one row per
  owned site (`GUID`, `Name`), then **PlayerMaxRootClaims − owned** empty rows with a "Place" button, then one row
  with a "Buy" button when (free + owned) < **MaxRootClaimsAllowed**; both numbers from
  `GameData.ClaimDialogs.ClaimUpkeepManagmentDialog` (line 32; filled by FUN_142dba100 = 0.589 FUN_142d1b1a0 from Claims
  sub 10 UpkeepManagmentData, which the emulator sends: Claims.cs SendUpkeepData). The upkeep panel lost its
  counters and "Get More Build Sites" button (UpkeepWidget.as).
- **Calls:** Place → `Claims.StartClaimPlacement()` (controls/ClaimListItemRenderer.as 87; FUN_1435e2fa0 →
  ClaimsPacketStartClaimPlacement 0xF9/0x46, #3 §7); Buy → `OpenMarketplaceTagSearch("Site Expansion")` (90);
  OnRecoverFlag now takes the GUID from `claimItemInfoDS` (373).
- **Server:** 3.5 (0xF9/0x46 → Command 0x4D InteractionClaim result 2, no flag needed for 0.695) and 3.26 (re-send
  sub 10 after a placement too, so the Place rows update; the window re-lists on upkeep data-source changes, 127-129 and 327-334). Claim records in the 0.695 form (#3 §B5).

### ClaimWalkOnTextWindow
- WalkOnTextWindowDocumentClass.as 417-430, 730-736, 829, 1276-1280: new "Switch Team" button, visible when the
  walk-on data source (`GameData.ClaimDialogs.WalkOnText`) has `CanSwitchTeam` = 1 (FUN_142dc9660: needs setting
  `InstancedGames_AllowSwitch` and the site-game struct's new u32 ≥ 2, #3 §B5 / L37); click →
  `InstancedGame.SwitchTeam()` → DropdownDialog (above) → Request type 9. InstancedGameContent /
  InstancedGameOwnerContent got the button symbol.

### CraftingWidget
- RecipesListWidget.as: keeps the selected recipe index across refreshes. hudstate.lua: opening the crafting window
  closes the repair window and stops dispatching OnReset / OnSetStationType (lines 3017-3056). Client only.

### GuildWindow
- GuildRanksListItemRenderer.as 63-112 + views/AdminView.as 417: the Edit button was hidden for rank GUID "1" (leader)
  in 0.589; in 0.695 every existing rank shows it (only the "Create New Rank" row hides it). Matches the patch note
  "guild leader rank title editable" (TASKS 3.17). Today's server already accepts it: Guilds.cs 547-572 renames the
  leader rank and keeps all its permissions. Works.

### HUDScoreboardWidget (in-game score HUD)
- ProgressBarsDisplay.as 55-58, 116-195, 226: listens for END_CHANGES, shows the second bar only when it has data, and
  prints each bar's **`Label`** instead of its `Position` number.
- Label is filled by the HUD handler FUN_1434e2f70 (0x10D sub 9; 0.589 FUN_143432bd0 had no Label): for each
  progress-bar entry FUN_143c1c7b0(entry, …) returns the entry's own string if non-empty, else its text id's text,
  else **"Missing Text"** (re/out/agent_ui/hud_695.c ~709-819). The entries are the HUD entries of 0x50 bytes whose
  new trailing fields (#3 §B6) are used here: +0x40 bool = team entry (matched against the local team, manager +0xa0,
  via +0x30), otherwise a player entry matched by the u64 +0x48 against the local player guid (BaseClient +0x32508)
  to set `IsSelf` (hud_695.c 659, 727-760). Gap 3.24.

### InstancePropertyEditor (prop settings)
- **Rich text** in large text settings (controls/LargeTextInput.as, +182 lines): Bold / Italic / Underline / colour
  buttons; the HTML is saved tokenised (tokenizeText 239-250: drops the TEXTFORMAT / font noise, `<` `>` `'` `"` →
  `~[~` `~]~` `~a~` `~q~`) through `PropertyEditor.SetPropertyValue` (FUN_1435ba360, client-side; the prop's settings
  reach the server through the existing prop paths, #6); the counter shows **512 − visible length** (106).
  Server: store and relay the tokenised string unchanged, size by tokenised length (TASKS 2.21, 2.18).
- PropertyEditorListView.as: "saved" text fades; text CHANGE listened on the inner text field.
- Shares RulesetObjectSettingListItemRenderer with RulesetObjectSettings (below).

### Inventory (Bio)
- InventoryDocumentClass.as 54, 479-483, 686-693, 795: Bio text area (512 chars, "No biography available...") bound
  to `Character.PlayerBio` `BioString`; every edit → `Ui.SetBio(text)` (FUN_1435c4740 → player +0x7aea8, sent by
  FUN_142fd4290 as PlayerUpdatePacketSetPlayerBio 0x26/0x80 at most every 1,501 ms and only if it passes the
  bad-word filter, #3 §1). Window grew 100 px; title drop-down 8 rows.
- **Server:** TASKS 3.4 / §0: store the bio, send it back in SendSelfToClient's trailing string (so the box is filled
  at login) and in StartInspect.

### LoadingScreen
- No script changed; the swf changed only in embedded assets (217 new loading-screen PNGs under
  UI/Default/images/loadingScreens). Data sources `GameData.LoadingScreen` / `GameData.LoadingProgress` as in 0.589.
  Client only.

### NotificationWindow
- NotificationWindowDocumentClass.as 205-219: ShowMessage centres the text and detokenises it (same tokens as Chat).
  Client only; server relays tokenised text unchanged (2.21).

### RulesetObjectSettings (Game Table settings)
- Option rows (RulesetObjectSettingListItemRenderer.as, +181 / −55; OptionsListView.as; SettingsView.as) read whole
  rows (`GetDataObject(i, null)`) and use new columns `DataType`, `ValueIdentifier`, `ArgIndex`, `SectionHeader`,
  `ScriptPropId`, `IsProp` and option types `TextInput`, `Header`, `DividerLine` (besides CheckBox / Slider / DropDown /
  Text). Fillers: GetConfigurationResponse handler FUN_1434e98d0 → FUN_1434f4460 (`RulesetObjects.GameSettings.%d`:
  ValueIdentifier, DataType, OptionType, SectionHeader, DividerLine) and FUN_1434f39a0; PropRecipe handler FUN_1434e9210
  → FUN_1434f31e0 (`RulesetObjects.PropSettings.CategoryList`: IsProp, PropRuleIndex, PropRole, PropTeam, PropSlot,
  **ScriptPropId** = PropRecipe item +0x44, the new trailing u32 of #3 §B6).
- **Calls (all new):** CheckBox → `RulesetObject.UpdateValueBool(DataType, ValueIdentifier, value, ArgIndex)`
  (renderer 333; FUN_143608450 → InstancedGameAddOrRemoveRuleRequestPacket 0x10D/0x18); slider (on mouse-up / dirty
  flag) → `UpdateValueFloat` (99, 343; FUN_1436083a0, local); text input → `UpdateValueString` (363; FUN_143608260,
  local); mouse release anywhere → `RulesetObject.UpdateDirtySliders` (RulesetObjectSettingsDocumentClass.as 250;
  FUN_143608500, rebuilds the UI). The local edits go to the server with SetConfiguration (0xF, #3 §B7).
  Category.as 270: `InstancedGame.AddAdditionalProp(PropRuleIndex, PropTeam, PropSlot, ScriptPropId)` — now 4
  arguments (FUN_143603fc0 reads Lua args 1-4 into FUN_1434f0f00) → InstancedGamePropRequestPacket (0xA) with the new
  trailing u32. The document class reads `GameData.RulesetObjects.SettingsData` Name / GameEnabled / HasRequiredProps.
- **Server:** TASKS 2.12 with the 0.695 shapes (#3 §5d-e, §A5, §B6-B7, functions-695 B1/B2).

### SelectedTemplate (Design Details)
- SelectedTemplateDocumentClass.as 151-153, 242-268, 292-329, 359-364: the Player Studio icon went; a "Get Resources
  Now" button shows when any `Character.Template.Selected.MaterialItems` row has CountRequired > CountUsed or any
  `Character.Template.Selected.Props` row has an ErrorString (text 576186 "Missing Prop knowledge!"); fillers
  FUN_143115670 / FUN_143115d40 (0.589 FUN_14306ad50 …), client-computed from the design data. Click →
  RequestPurchaseTemplateMaterialsData + open TemplateConfirmationWidget (above). Closing the window closes the
  confirmation (SelectedTemplate.lua 19, 26). `SelectedTemplate:GetResources` (lua 52-60) is an unused stub.
- **Server:** 3.6; template data in the 0.695 form (#3 §B4).

### SpeechBubbleWidget
- SpeechBubbleWidget.as 74, 135: bubble text detokenised; SpeechBubbleRenderer resets visibility / alpha on new text
  (bubbles no longer lost when one interrupts another). The Story Node **Yes / No buttons are not in this swf**: they
  are HUD indicator row 29 `NpcDialogResponse` (HudIndicators.txt, new row: STRINGPARAM1/2 "Yes" / "No",
  STRINGPARAM3/4 `Prop.OnConfirmNpcDialog` / `Prop.OnDenyNpcDialog`, FUN_1435fbf80 / FUN_1435fc030 →
  PropPacketDialogResponse 0xED/0x1D). Server: TASKS 2.22.

### TeleportSpireWidget
- TeleportSpireListItemRenderer.as: the "Tier N" (SurfaceTier) label removed; SearchField filters on every CHANGE.
  Client only.

### TeleportWidget
- TeleportWidget.as 120-124: 0.589 disabled "An Underground Adventure" via `Ui.IsInNewbieZone` ("There are no
  underground adventures available in your current World"); 0.695 always enables it. Click →
  `Teleport.TeleportToRandomUndergroundCave` (FUN_1435e8270 → PacketTeleportRandomUndergroundRequest, handled today)
  + UI event 14. The server must now cope with the request from every zone (3.28, with 3.14).

## Other UI entry points that changed (Lua / tables / input)

- global.lua: `/gamefinder`; `OnInspectPlayer()`; `OpenPvPScoreboard` now requires `InstancedGame.IsInGame()`
  (FUN_143604410, true while observing — functions-695 B3) and HUD state normal; `CloseEndGameScoreboard()` added.
- InputProfile_Default.xml: Tab → `OpenPvPScoreboard` / `ClosePvPScoreboard` (commented out in 0.589, active in 0.695);
  Semicolon → `Tool.ToggleCursorSmoothing` (action ToggleSmoothing, text 577630 — the caller of that new binding);
  Comma / Period FreeLookStrafeLeft / Right. 3.27.
- RadialMenuButtons.txt rows 74 (prop menu, "Open Help", `Prop.OpenHelpWindow`) and 75 (player menu, "Inspect").
- Data.xml: game menu `Ui.GameFinder`; options "Allow Site-wide Level Of Detail" (Claims.Get/SetAllowClaimWideLOD),
  "Ambient Volume" (GameSettings.Get/SetAmbientVolume), chat opacity options; `GameData.GameFinder.GameFlags`.
- admin.lua `/barberrace`, `/barbergender` (CharacterCustomization.SetRace / SetGender, existing bindings
  FUN_143607ec0 / FUN_143607ef0); UIObjects.lua UICheckbox; ConfirmationDialog hides Cancel when there is no callback.
- FirstTimeEvents.txt +43 AppearancePurchase, +48 MeshTabHelp (and 44-47 story tools): the UI events 33 / 34 above
  are their natural triggers (3.23).

## Lua bindings: new and changed (for these windows)

luabind_695 has 2,115 bindings; 42 names are not in luabind.tsv, but **the six `SocialShare.*` are not new**: 0.589's
table lists them under the namespace `SocialShare_1446500cd_3` (re/out/luabind.tsv). So **36 new bindings**:

| Binding | 0.695 address | Sends | Called from |
|---|---|---|---|
| Ui.InspectPlayer | FUN_1435c45b0 | RequestInspectPlayerPacket 0x91/3 | radial row 75 |
| Ui.SetBio | FUN_1435c4740 | (PlayerUpdatePacketSetPlayerBio later, FUN_142fd4290) | Inventory |
| Claims.SetClaimAdventureFlag | FUN_1435e6470 | ClaimsPacketSetAdventureFlags 0xF9/0x45 | BuildModeTimeWidget |
| Claims.StartClaimPlacement | FUN_1435e2fa0 | ClaimsPacketStartClaimPlacement 0xF9/0x46 | ClaimManagementWindow |
| Claims.Get/SetAllowClaimWideLOD | FUN_1435e6360 / FUN_1435e6390 | – | Data.xml option |
| Templates.RequestPurchaseTemplateMaterialsData | FUN_1435dc520 | 0xF0/0x1D | SelectedTemplate |
| Templates.PurchaseMissingTemplateMaterials | FUN_1435dc570 | 0xF0/0x1F | TemplateConfirmationWidget |
| Prop.OnConfirmNpcDialog / OnDenyNpcDialog | FUN_1435fbf80 / FUN_1435fc030 | PropPacketDialogResponse 0xED/0x1D | HudIndicators row 29 |
| Prop.OpenHelpWindow | FUN_1435fbc80 | – (Lua ShowHelpWindow) | radial row 74 |
| InstancedGame.ShowGameFinder | FUN_143604db0 | – | GameFinder.lua |
| InstancedGame.SetGameFinderFlag | FUN_143604dd0 | – | GameFinder.lua |
| InstancedGame.StartMatchmaking / StopMatchmaking | FUN_143604e40 / FUN_143604e60 | MatchmakingRequest 0x10D/0x19 | GameFinder.lua |
| InstancedGame.IsMatchmaking | FUN_143604e80 | – | GameFinder.lua |
| InstancedGame.SwitchTeam | FUN_143604510 | RequestJoinTeam (Request type 9) | ClaimWalkOnTextWindow, DropdownDialog |
| RulesetObject.UpdateValueBool | FUN_143608450 | AddOrRemoveRule 0x10D/0x18 | RulesetObjectSettings, InstancePropertyEditor |
| RulesetObject.UpdateValueFloat / UpdateValueString / UpdateDirtySliders | FUN_1436083a0 / FUN_143608260 / FUN_143608500 | – (local; SetConfiguration later) | RulesetObjectSettings |
| Mesh.SelectCategory / SearchPalette / FilterMeshesByAvailability | FUN_143607f90 / FUN_1436081a0 / FUN_143608100 | – | BuildModeWidget, MeshesWindow |
| Mesh.SelectMesh / PlaceMesh | FUN_143607fe0 / FUN_143608060 | ToolPacketActivate | BuildModeWidget, MeshesWindow |
| Mesh.Populate / GetMeshesFilteredByAvailability | FUN_1436080e0 / FUN_143608160 | – | no caller found yet (L56) |
| Tool.ToggleCursorSmoothing / GetCursorSmoothing | FUN_1436066a0 / FUN_1436066e0 | – | InputProfile action ToggleSmoothing (getter: no caller found yet, L56) |
| GameSettings.Get/SetAmbientVolume | FUN_1435f2f50 / FUN_1435f2d40 | – | Data.xml option |
| Ui.Get/SetAmbientVolume | FUN_1435ce900 / FUN_1435ce760 | – | no caller found yet (L56) |
| CharacterSelect.GetEyeSlotCount / SetEyeSlotIndex | FUN_1435da2c0 / FUN_1435da360 | – | no caller found yet (L56) |

Changed existing bindings these windows depend on (evidence in the decompile / functions-695):

| Binding | 0.695 / 0.589 | Change |
|---|---|---|
| InstancedGame.AddAdditionalProp | FUN_143603fc0 / FUN_143551660 | 4th Lua arg ScriptPropId → PropRequest trailing u32 |
| InstancedGame.IsInGame | FUN_143604410 / FUN_143551a80 | true while observing (functions-695 B3); gates the Tab scoreboard |
| CharacterCustomization.StartCustomization | FUN_143607d10 / FUN_143554800 | now sends EquippedAttachmentRequest 0x11C/3 |
| CharacterCustomization.ConfirmCustomization | FUN_143607d80 / FUN_143554870 | PlayerSet + two trailing u32 |
| Templates.Paste / PasteInCurrentClaim | FUN_1435db0d0 / FUN_143529490 … | threaded paste, "paste in progress" refusal (functions-695 §2); same packets |

## Gaps (TASKS §3 style, continuing after 3.21)

- [ ] **3.22 Inspect: other players' item tooltips.** InspectWidget's equipment rollover sends
      InventoryPacketItemInstanceTooltipRequest with the inspected player's guid in the item location
      (EquipmentItemRenderer.as 53 → FUN_1435de7d0 → FUN_143519570). InventoryHandler.HandleTooltip
      (InventoryHandler.cs 189-199) only looks in the requester's inventory and drops it. Answer from the target's
      equipped items (only what StartInspect showed). Needs 3.4.
- [ ] **3.23 First-time help for the two new UI events (0.695 sessions).** `Ui.UIEventTriggered(33)` from the Crimson
      Parlor unlock / purchase buttons (BarberShopWidget.as 401, 410) and `(34)` from the Build Mode Mesh tab
      (BuildModeWidget.as 337) are new; FirstTimeEvents.txt 0.695 adds 43 AppearancePurchase and 48 MeshTabHelp, help
      pages AppearancePurchase_FTE / Mesh_FTE. Add 33 → 43 and 34 → 48 to FirstTimeEvents.cs `uiEvents` / `pages`
      (pairing by name: verify the page position against the help page sizes).
- [ ] **3.24 Game HUD progress-bar names.** The 0.695 score HUD prints each bar's `Label` (ProgressBarsDisplay.as 116,
      168) = the HUD entry's localized name, "Missing Text" when the entry has neither a string nor a known text id
      (FUN_143c1c7b0). Send a team / player name in every HUD progress entry and the 0.695 trailing bool (team entry)
      + u64 (player guid) so `IsSelf` and the "best other" bar are right (FUN_1434e2f70; #3 §B6).
- [ ] **3.25 Game Finder window states.** Answer StartMatchmaking with MatchmakingUpdate SearchState 0 and
      StopMatchmaking with 3 (GameFinder.lua 48-60: 3 → options page / "Search", 1 → info page / "Search", other →
      info page / "Stop"), fill Players / ActiveGames / QueuedGames / InactiveGames, and send a new update whenever they
      change while the player is searching — `ShowGameFinder` only shows the last values received (FUN_1434ed2e0).
      Part of 2.12; joining a found match is L31.
- [ ] **3.26 Site Management rows follow the upkeep data.** The 0.695 window builds its Place / Buy rows from Claims
      sub 10 (PlayerMaxRootClaims − owned Place rows; a Buy row while below MaxRootClaimsAllowed;
      ClaimManagementWindowDocumentClass.as 281-325). Claims.cs sends sub 10 on opening (0x1C, line 230), on 0x20
      (234), after a delete (901) and after Point-of-Need upkeep (535), but not after a placement (PlaceConfirm only
      calls SendClaimsByOwner): re-send it there too, and when the player's slot count changes. With 3.5.
- [ ] **3.27 Tab scoreboard (0.695).** Tab now opens PvPScoreboard (InputProfile_Default.xml, global.lua 221-240)
      when `InstancedGame.IsInGame()` and the HUD state is normal: verify the scoreboard data (`GameData.
      InstancedGameScoreboard.*`, Scoreboard 0x10D sub 3) is sent to players and observers of a site game, and the
      end-game banner (state 7, functions-695 B3) closes / blocks it as the Lua expects.
- [ ] **3.28 Underground Adventure from every zone.** TeleportWidget no longer disables the button in newbie zones
      (TeleportWidget.as 120-124 vs 0.589 Ui.IsInNewbieZone). Check PacketTeleportRandomUndergroundRequest handling
      from every landscape (newbie, Pioneer, event islands) and the refusal message where no cave exists. With 3.14.

## Hand-offs

- **#3 (packets):** SetClaimAdventureFlag's "current claim" argument: the UI passes 0 (TimePanel.as 405-420) and
  FUN_1435e6470 compares with DAT_145476ff0 (BSS, no writer found) — §6 says -1 (L55). HUD entry trailing bool / u64
  meaning (team entry / player guid) from FUN_1434e2f70 (gap 3.24). AddAdditionalProp's 4th argument = PropRecipe
  item +0x44 (ScriptPropId) confirms §B6's PropRequest trailing u32.
- **#10 (coverage):** the window → packet list above (Summary table) for the coverage cross-check; ToolPacketActivate
  stays "informational" for the Mesh / Tints / Tools windows.
- **#6 (story tools):** tokenised rich text — producer LargeTextInput.tokenizeText (InstancePropertyEditor), 512
  visible characters, consumers Chat, NotificationWindow, SpeechBubbleWidget (2.21); the question buttons are HUD
  indicator row 29, not SpeechBubbleWidget (2.22, L30).
- **#7 (items):** Parlor `PurchaseRequired` / `MarketName` options and unlockers (L39) — the UI only opens the
  marketplace tag search with MarketName.
- **#8 (architecture):** Game Finder counts are per matchmaking service (Players / Active / Queued / Inactive games) —
  the shared social service in BUILD-PATH Phase B1.
- **Coordinator — leads the AS export answers:** L28 (InspectWidget reads only CharacterName, LuminaryScore,
  NumberOfClaims, BioString, CharacterGuid and Name / IconId / ItemGuid / SlotNumber; the right-click entry is radial
  row 75), L29 (TemplateConfirmationWidget never reads FoundLumenRecipeForAllItems and has no result UI), L30 (buttons
  = HudIndicators row 29; SpeechBubbleWidget only detokenises), L31 (GameFinder.lua / DropdownDialog.lua traced: no
  join logic in the UI; DropdownDialog's only caller is SwitchTeam). CLIENT-DIFF §4b: SocialShare.* are not new;
  Tool.ToggleCursorSmoothing's caller is the ToggleSmoothing key action. TASKS 3.2: the modular windows are client-only
  (layout saved by the client). TASKS 3.17 "guild leader rank title editable": UI confirmed, server already allows it.

## Open leads added (OPEN-LEADS.md)

L54 other sources of tokenised text in chat / notifications · L55 SetClaimAdventureFlag current-claim sentinel ·
L56 new bindings with no caller found · L57 TemplateConfirmationWidget cost line refresh.
