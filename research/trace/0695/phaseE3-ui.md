# Phase E3 — UI scripts as a source of missed server work (agent "ui", 2026-10-26)

Read-only hunt (brief: agent-phaseE3-brief.md). Slice: the 0.695 Lua / XML (`<0.695 install>\Resources\Scripts`,
`UI\Default`, `InputProfile_Default.xml`, the UI-feeding Resources tables) against the 0.589 copies (the Steam install
`<Landmark folder>`, same paths), and the 29 changed SWFs (re/as3_695 over re/as3). Server:
LandmarkEmu client-0.695 @ 8219f7c. Scratch: `...\scratchpad\e3-ui\` (scripts.diff, ui.diff, newstrings.txt,
luacalls_diff.txt, newstr.py, luacalls.py).

**Method.** (1) Full diff of every changed Lua / XML file (4 scripts, 18 UI files, 2 new windows) and of the UI tables
(RadialMenuButtons, HudIndicators, Requirements, RequirementExpressions, IgGeneralSetting, InputActionGroupEntries,
DynamicInputActionSystems, StringHashToValue, CharCustomizationSlotCost, CharCustOptionMult, InputProfile_Default.xml).
(2) Every quoted string that is new in each changed SWF (data-source names, columns, Lua calls). (3) Every native →
Lua global call (`FUN_142edbdb0(…,"Name",…)` style) whose count differs between the builds (17 names). (4) Each item
checked against ui-diff.md, ui-trace-695.md, ui_server_map_695.md, CLIENT-DIFF §4, functions-695.md, hud-progress-695.md,
OPEN-LEADS and TASKS first; only what none of them records is reported below.

The earlier UI work (ui-trace-695.md, agent #11) is complete for what it covers: every new / changed data source,
column and Lua call in the 29 SWFs and the two new Lua windows is traced there, and every gap it raised (3.22-3.28,
2.1, 2.12, 2.18, 2.21, 2.22, 3.4-3.6) is built. What is left is in places that trace did not look: the newly enabled
**GameObserver input set**, the **observer HUD state**, the newly exposed **Auto Party With Friends** option, and the
tool output (ui_server_map_695.md) that still shows built paths as gaps.

## 1. Findings (ranked by player impact)

### F1. Observers of a site game get no kill-feed, flag or control-point lines — MISMATCH
- **Client [P].** While a player observes a game (ClaimWalkOnTextWindow "Observe" → Lua `InstancedGame.EnterObserverMode`,
  FUN_143604470 → camera mode 0x20), the observer controller's activate function FUN_143062060 (part_0082 38129-38186)
  hides the avatar and switches the game state to **9** (`FUN_1430fc5e0(DAT_145431998, 9)`); its deactivate
  FUN_143062170 goes back to 3. State 9 is `HudStateObservingInstancedGame` (hudstate.lua 15, 27, 707-735), whose
  OnEnterState loads the HUD scoreboard, **WIDGET_STATUS_CHANGE_FEED (56)**, **WIDGET_KILL_FEED (57)**, the mode label and
  the walk-on window for the observer. The event handler FUN_1434e7900 (HandleGameEventPacket, part_0107 34116 ff.) has
  no "is a player" gate — it only needs the UI manager — so an observer shows every Event (0x10D sub 7) it receives.
  (The camera mode 0x20 → controller link is through vtables: [I strong]; the state-9 switch itself is [P].)
- **Server [P].** `InstancedGames.SendEvent` (LandmarkEmulator.WorldServer/Game/InstancedGames.cs:2177-2190: kills,
  types 1-4) and `AnnounceCapture` (InstancedGames.cs:1472-1488: ControlPtTaken, type 9) send only to
  `game.Players`. The 0.695 point-grant events already go to observers too (`SendPointEvent`,
  InstancedGames.Rework.cs:793-798: players in place + `Watchers(game)`), as do state, HUD, scoreboard and banner
  (InstancedGames.cs:870, 1827, 2053, 834). So an observer sees "+N points" lines but never who killed whom or who took a
  point.
- **Fix:** in SendEvent and AnnounceCapture, build the receiver list as SendPointEvent does
  (`game.Players.Where(p => !p.Arriving).Select(p => p.Session).Concat(Watchers(game)).Distinct()`). Consider the same for
  `SendTeamMembers` (InstancedGames.cs:2148-2166, players only): the client keeps that map for team colour / hostility of
  actors (FUN_1434e7280 / FUN_142ea9630); for an observer it only matters for name colours [I] — see lead U-2.
- **In-game check:** yes — observe a team game, let one player knock out another and take a control point; the observer's
  kill feed should show both lines.

### F2. ui_server_map_695.md / coverage_695.md (regenerated 2026-10-26) list built paths as GAPS — STALE
- ui_server_map_695.md's 9 "GAPS" are all false positives, and so is the "sub NOT handled" / "NOT HANDLED" status of the
  packets under them in coverage_695.md. Each is handled in the current server [P]:

  | UI call (map row) | Packet | Handled at |
  |---|---|---|
  | Claims.SetClaimAdventureFlag | ClaimsPacketSetAdventureFlags 0xF9/0x45 | Claims.cs:244, 280 (`if (sub == SetAdventureFlagsSub)`) |
  | Claims.StartClaimPlacement | ClaimsPacketStartClaimPlacement 0xF9/0x46 | Claims.cs:226, 269 |
  | RulesetObject.UpdateValueBool | InstancedGameAddOrRemoveRuleRequestPacket 0x10D/0x18 | InstancedGames.Rework.cs:43, 425 (`if (sub != SubAddOrRemoveRule)`) |
  | Claims.DelistClaimOnGallery, InGamePurchaseStoreScreen.ProductRemove, MarketplaceItemCreationWindow.ProductRemove, PlayerStudioWindow.ProductRemove | PacketInGamePurchaseCommerceProductRemove 0x45/0x32 | Game/Commerce.cs:76, 385-386 → RemoveProduct 745; CommerceServer/ProductService.cs:86 |
  | Mesh.PlaceMesh, Mesh.SelectMesh (and every "checked: informational" ToolPacketActivate row) | ToolPacketActivate 0x117/1 ("NOT HANDLED, seen 194") | Game/Building.Tools.cs:71-76 (`HandleTool`) |

- **Root causes in re/tools/coverage.py [P]:**
  1. Sub tests written `if (sub == X)` / `if (sub != X)` are not seen: the handled-sub regex (line 169) only takes
     `case X:` and `SubOpcode ==/!= X`.
  2. A constant with a comment before its separator (`SubProductRemove = 0x32 /* 0x31 */,` — the whole 0.695 renumbering
     style in Commerce.cs, InstancedGames.cs:46) is not read: the constant regex (line 132) needs `[,;]` right after the
     value, so `case SubProductRemove:` resolves to nothing.
  3. A fully qualified attribute `[Network.Message.ZoneMessageHandler(Network.Message.ZoneMessageOpcode.ToolBase)]`
     (Building.Tools.cs:71) is missed by `ZoneMessageHandler\(ZoneMessageOpcode\.(\w+)` (lines 121, 152), so base 0x117
     counts as not handled at all.
  4. Only LandmarkEmulator.WorldServer is scanned (line 10); the Commerce service project is not (harmless here because
     the world's Commerce.cs also handles 0x32).
- **Fix (tools only):** sub regex `(?:SubOpcode|\bsub)\s*[!=]=\s*([\w.]+)`; constant regex
  `\b([A-Z]\w*)\s*=\s*(0x[0-9A-Fa-f]+|\d+)\s*(?:/\*.*?\*/\s*)?[,;]`; handler regex with an optional
  `(?:[\w.]+\.)?` before `ZoneMessageHandler` and `ZoneMessageOpcode`; then re-run coverage.py and uimap.py with
  LMEMU_CLIENT=695. Optionally add verdicts for the ToolPacketActivate rows as "handled (no reply)". No server change.
- In-game check: no.

### F3. "Auto Party With Friends" is a live option in 0.695 and auto-accepts friends' invites — OK-NOTE
- **New in 0.695 [P]:** Data.xml Social options add "Auto Party With Friends" (GameSettings.Get/SetAutoGroupWithFriends,
  FUN_1435f5bf0 / FUN_1435f5cc0); 0.589 had the same row commented out. agent-leads2-d2.md §1 recorded only that it sets
  local bit 4 (FUN_142df3660); what the bit does was not traced.
- **What it does [P]:** the Invite handler FUN_142debc60 (GroupsBase 0x2B sub 1, `GroupPacketInvite`) stores the invite
  and, when bit 4 of group manager +0x14 is set and the inviter's guid (first u64 of the inviter player block, invite
  +0x70) is in the friends map (`FUN_143546460(DAT_145431a20 + 0x75178, guid)`, the map the Friend packets fill:
  part_0102 2770), calls FUN_1433fc7c0(…, 1) at once instead of showing the popup (FUN_1433f28f0). FUN_1433fc7c0 →
  FUN_142df16b0 sends the ordinary Join (response 1 = accept) from the stored invite — the same packet the popup's
  Accept sends.
- **Server:** compatible as it stands. The invite record's first player block is the inviter with the inviter's character
  guid (Shared/Game/GroupPackets.cs:83-101, `WritePlayer(w, inviterGuid, …)`), and Groups.HandleJoin
  (Groups.cs:289-294) treats response 1 as Accept; the social-service path builds the same bytes (GroupPackets shared).
  Nothing to build; worth one in-game check because the player now joins with no popup.
- **In-game check:** yes — tick the option on client B, friend A invites B: B should join without a popup.

### F4. The GameObserver input set is enabled in 0.695 — OK-NOTE
- **New [P]:** InputProfile_Default.xml 680-745: the `GameObserver` ActionSet (commented out in 0.589) is live: WASD /
  arrows / Space / X fly the observer camera, Shift+Mouse0 SetFocus, V CycleFocusedPlayer, C CycleFocusedCameraMode,
  Z ClearTarget, Comma / Period speed, Tab Open/ClosePvPScoreboard. They drive ControllerInstancedGameObserver (actions
  registered part_0082 ~38500-39550; texts "Free Fly", "Tracking %s", "Following %s", part_0082 ~38010-38100). None of it
  sends a packet (no sender reference in the controller). Not recorded in any trace (grep GameObserver /
  CycleFocusedPlayer: none).
- **Server effect:** none needed for the camera — players and their movement are sent landscape-wide
  (PlayerManager.cs ~400 Introduce on load; Network/MovementFanout.cs: every PositionUpdate to everyone on the
  landscape), so V / Shift+click can focus any game player. Props stream around the observer's (frozen, hidden) avatar
  within PropStreamRange 400 m (Props.Stream.cs:91), more than a site, so a camera flown inside the site sees its props.
- **Client quirk, not a server bug [P]:** in observer state 9 the Tab binding calls global.lua `OpenPvPScoreboard`, which
  returns unless the HUD state is STATE_NORMAL (global.lua 222-223). So an observer in the observer camera cannot open
  the Tab scoreboard on the 0.695 client; the scoreboard data the server sends observers is still used by the observer
  HUD. hud-progress-695.md §3 ("Tab: needs IsInGame() (player or observer), HUD state normal") is correct, but a tester
  should not expect Tab to work while observing.
- **In-game check:** optional (observe a game; V cycles players; Tab does nothing).

### F5. Barbershop.cs says the gender / size price overrides come from a map "this server never fills" — STALE (comment)
- LandmarkEmulator.WorldServer/Game/Barbershop.cs:179-185 hard-codes 60 / 30 with the comment that the client looks
  overrides up "in a client settings map this server never fills". The map is the client table StringHashToValue.txt:
  0.695 adds `CharacterCustomization.GenderChangeCost^60` and `CharacterCustomization.RaceChangeCost^30` (diff vs 0.589;
  items-695.md:321 already says so) [P]. The numbers match, so the price shown and charged agree.
- **Fix:** reword the comment; optionally read the two values from gameData695 StringHashToValue.txt so an edited
  table keeps server and client prices together. In-game check: no.

## 2. Checked and fine (one line each)
- Lua scripts diff: UIObjects.lua UICheckbox (client); admin.lua /barberrace, /barbergender + PrintDs fix (dev, no packet:
  CharacterCustomization.SetRace / SetGender are local); global.lua /gamefinder, OnInspectPlayer, CloseEndGameScoreboard,
  OpenPvPScoreboard gate (all in ui-trace / hud-progress); hudstate.lua OnDisableBuildMode ungated + closes the 7 modular
  windows (CLIENT-DIFF §4), Inspect / TemplateConfirmation / modular window openers (ui-trace), crafting ↔ repair window
  mutual close and AllowCloseOnTop (client only).
- UI XML diff: Data.xml GameFinder rows, StartMenu Ui.GameFinder (ToggleGameFinder exists, GameFinder.lua 28), options
  site-wide LOD (client: claim box to terrain, client-commands-for-slots.md:125), chat opacity, Ambient Volume (client),
  removed GUID attributes of the score HUD / scoreboard sample rows (design-time data, replaced by the handlers);
  GfxHudWidgets 218-226 + 202 position; Inventory.xml height; ConfirmationDialog hides Cancel without callback (server's
  only callback-less use is the Activation Failure box, ItemUses.cs:105 — intended OK-only); CreateTemplate title;
  Chat / LandmarkHUDWidget / ScoreboardWinnerBanner / InGameBrowser / InGameMarketplaceBrowser input flags; Styles /
  Templates (art); manifest (2 new windows); CharacterSelection reset (client).
- GameFinder.lua / .xml: SearchState "1" / "3" / other and StateString (client-made in FUN_1434e9a50: "Searching...",
  "Match found!", "Search inactive.") — server Finder (InstancedGames.Finder.cs) sends what it needs.
- DropdownDialog.lua: only SwitchTeam uses it; JoinableTeams + AllowSwitch built (InstancedGames.cs:143, Rework.cs:217, 474).
- New native → Lua calls (luacalls_diff.txt): Character.InspectData / OnInspectPlayer (Inspect.cs), Character.PlayerBio
  (SendSelfToClient.cs:3121, WorldSession.SetBio 512 cap), Character.Templates(.PointOfSale) (DesignMaterials.cs + currency
  re-send, L57), CharacterCustomization, Claim.AdventureFlags (2.1), Claim.NoCameraPlayMode (rebase-695 / agent-playmode),
  Open/CloseScoreHUD + CloseEndGameScoreboard (hud-progress §3), CreateDropdownDialog, GameFinder.GameFlags /
  MatchmakingInfo / UpdateGameFinder (3.25), RulesetObjects.GameSettings.CategoryList (2.12), ShowHelpWindow (radial
  row 74, client).
- New SWF strings (newstrings.txt): every data source / column / Lua call already in ui-trace-695.md (BarberShop,
  BuildModeTimeWidget, BuildModeWidget Mesh, ClaimManagementWindow Place / Buy, ClaimWalkOnText CanSwitchTeam,
  InstancePropertyEditor rich text, Inventory Bio, RulesetObjectSettings columns, SelectedTemplate Get Resources).
- InspectWidget fields vs Inspect.cs blob (profile 0, Luminary Score, sites, bio, equipped with EquipSlotId); bio shown
  with `.text` (no HTML injection) in InspectWidget.as 111 and InventoryDocumentClass.as 484.
- ClaimManagementWindow OnRecoverFlag now takes `ClaimEditDialog.ClaimItemInfo` row 0 GUID — the filler FUN_142db8ab0
  sets the row id from the claim entry +0x18 (claim id), same value as before.
- Store tag searches (BarberShop MarketName, Site Management "Site Expansion") → Browser.OpenGalleryWithTags
  (gallery-695.md §2, StoreBundles.cs) — built.
- New Requirements 74-81 / RequirementExpressions 42-49 and IgGeneralSetting CLIENT_REQUIREMENT_ID (client-evaluated
  against the settings the server sends: functions-695 §1, InstancedGames.Editor.cs:338-444); InputActionGroupEntries
  requirement 42 on boot actions (2.1) and group 153 Roll Wraps (cosmetics-695.md).
- RadialMenuButtons 74 / 75, HudIndicators 29 (ui-trace; 2.22, 3.4).
- Barbershop price (Barbershop.cs:233-268) follows the 0.695 tables (CharCustomizationSlotCost renamed rows, same costs
  except eye 0→5 model cost, which the server reads from the table; CharCustOptionMult 110 new rows).
- ui_server_map "checked: unreachable / parked / not in 0.589" rows (refining / upgrade stations, vehicles, merchant,
  upkeep deposit, SalvageItem, Browser.OnHide): verdicts re-checked for 0.695 in verdicts.tsv (E.2 / E.3 notes); "native
  not found" list identical in 0.589 and 0.695 (no new dead call).

## 3. New leads
- **U-1. Observer camera bounds.** Need: whether ControllerInstancedGameObserver clamps the free-fly camera (to the site
  or a radius), to know if PropStreamRange around the frozen avatar can ever be too small. Searched: activate /
  deactivate FUN_143062060 / FUN_143062170 and the action registration (part_0082 37660-40100). Try next: the update
  function FUN_1430618d0 (reads camera +0x90, speed +0xb0) for a clamp against a claim box (FUN_142d99550) or distance.
- **U-2. TeamMembers for observers.** Need: whether live sent InstancedGame TeamMembers (sub 0x15) to observers, so their
  client colours / names players by team. Searched: SendTeamMembers (players only); client users of the map
  FUN_142ea9630 / FUN_142ea8f90 / FUN_1434e7280. Try next: the nameplate colour path for a non-member viewer
  (FUN_1434e7280 callers) and whether the observer HUD's status-change feed (widget 56) reads the team map.

## Summary
The 0.695 UI layer was already traced end to end (ui-trace-695.md) and every server answer it asked for is built; a full
diff of the Lua / XML, the UI tables and the native → Lua calls found nothing missing in those windows. The real gap is
one level away from the windows: the 0.695 client actually runs the observer HUD (game state 9 with kill feed and status
feed), and the server sends kill and control-point events only to players, while it already sends point-grant events,
state, HUD and scoreboard to observers (F1, one-line fix per sender). Two newly reachable options are compatible as built
— Auto Party With Friends auto-accepts friends' invites through the normal Join, and the GameObserver key set flies the
observer camera with no packets (F3, F4). The UI→server map regenerated today still lists nine built paths as gaps
because coverage.py misses `if (sub == …)` tests, commented constants and qualified handler attributes (F2, tools only).

Findings: F1 MISMATCH (observer kill / control-point events), F2 STALE (coverage / UI map false gaps), F3 OK-NOTE (Auto
Party With Friends), F4 OK-NOTE (GameObserver controls, Tab blocked while observing), F5 STALE (Barbershop price comment).
