# 0.695 functions: every new / changed function by system (agent #5)

Traced 2026-10-15 from the 0.695 decompile (re/decomp_all_695) against the 0.589 one (re/decomp_all). Every address
is a 0.695 function unless marked "0.589"; "0.695 X / 0.589 Y" pairs come from trace/0695/functions.tsv (`addr_589`) or,
where funcdiff left a function unpaired, from the string-set or code-shape searches described in §0. Scope: the
`code` = new (3,013) / changed (5,238) rows. Packet layouts / opcodes (#3, packets-695.md), terrain / Mesh Shapes
(#2, terrain-695.md), story-tool props and Links (#6, storytools-695.md) and item definitions (#7, items-695.md) are
only named here and handed off.

Scratch tools and full per-function results (re/out/agent_funcs, private research files):
- `build_index.py` → index.pkl (0.695 call graph, vftable refs, source-file and Class::Method strings for all 236,307
  functions; code of every target and its 0.589 partner).
- `classify.py` → labels.tsv (label + evidence per function), `sysmap.py` → systems.tsv (label → system, with a few
  hand-checked overrides), `compare.py` → compare.tsv (normalised 0.589 vs 0.695 comparison), `falsenew.py` →
  falsenew.tsv, `strpartner.py` → strpartner.tsv, `join.py` → **final.tsv** (one row per function: system, kind,
  label, evidence, ratio), `digest.py` / `show.py` / `ndiff.py` (readers), newstrings.tsv (every string literal in a
  new / changed function that does not occur anywhere in the 0.589 decompile: 169).

## Summary

**Most of the 8,251 rows are not new behaviour.** 3,038 are compiler exception-cleanup funclets (`Unwind@` /
`Catch@`), 588 are static initialisers / CRT in the low address range (461 "new" ones hash header-defined item-DB
column names such as `items_chest_owner_guid` — the same strings exist in 0.589, only the number of translation units
including the header changed). Of the remaining 4,625 (1,686 new, 2,939 changed): **601 new functions with their
own logic**; 1,085 "new" functions whose code (addresses and struct offsets masked) is identical to some 0.589
function (template instantiations for the new definition classes, or existing functions funcdiff could not pair);
and of the 2,939 changed ones **1,488 are identical once struct offsets are masked** (438 identical even with
offsets) — the classes grew, the logic did not. 664 differ slightly, 467 moderately, 320 a lot; a large share of the "major" ones
are one recompilation pattern (SoeUtil::IString temporaries no longer built for string arguments, refcount release
outlined — e.g. 0.695 FUN_143638e90 vs 0.589 FUN_143585690, FUN_142f258f0 vs FUN_142e86b00) or funcdiff mispairs
(e.g. FUN_143165920 paired with the unrelated 0.589 FUN_143ed53f0).

### Systems ranked (functions per system)

"new logic" = new without a 0.589 twin; "boilerplate" = new with an identical 0.589 code twin; "same" = identical or
identical modulo struct offsets. Assignment evidence and per-function rows: re/out/agent_funcs/final.tsv.

| System | new | changed | new logic | boilerplate | same | minor | moderate | major | Key functions (0.695) |
|---|---|---|---|---|---|---|---|---|---|
| Site games / Game Finder (InstancedGame) | 234 | 135 | 100 | 134 | 47 | 20 | 33 | 35 | dispatcher 1434ee920, Game Finder 1434e9a50 / 1434ed2e0 / 1434f2da0, SwitchTeam 143604510, rules UI 1434f39a0 / 1434edcd0, events 1434e7900, IsInGame 143604410, RuleDataManager 143b736b0 |
| Designs / templates | 238 | 77 | 114 | 124 | 23 | 12 | 26 | 16 | ExportedTemplate* managers 143de3af0 / 143de3e00, palette 1437546f0, voxel-data queue 14310b4d0 / 1431136c0, buy materials 14310c290 / 1431118e0 / 143113530, paste 1436d00b0 |
| Crimson Parlor / character customization | 166 | 85 | 60 | 106 | 34 | 8 | 12 | 31 | open 1430a5ae0, 0x11C entry 1430a29d0, accept 1430a2530, base actor 1430a6570, presets 1430a68a0 / 143c13c90, option data sources 1430a37a0 / 1430a4720 / 1430a3210 |
| Build sites (Claims) / Play Mode Rules | 95 | 146 | 20 | 75 | 90 | 22 | 24 | 10 | `Claim.AdventureFlags` 142dd2480, SetAdventureFlags 142dd08e0, StartClaimPlacement 142dd22f0, camera checks 142dd5540 / 143607950 / 1435e4530, destruction result 142e18c70 |
| Props, story-tool UI, prop help (spec #6) | 111 | 107 | 44 | 67 | 33 | 24 | 33 | 17 | help window 1435fbc80 / 143c16400, NPC dialog indicator 14343ca50 / 143440d90, property editor 1435c1b60 / 1435bc890, counter digits 1430a9260 |
| Building tools / paste | 124 | 79 | 50 | 74 | 19 | 26 | 23 | 11 | VoxelPasteCursor 1436ce040 / 1436d11d0 / 1436d17d0, PasteThread 143f98b80, PreviewGenerationThread 143f99120, clipboard 143111480, categories 1434d0970 |
| Tints and art sources | 85 | 49 | 27 | 58 | 29 | 5 | 7 | 8 | ArtTintGroupUnlock managers 143c6be40 / 143c6c260 / 143c6cf50, ArtTint loader 143db52d0 |
| Character, movement, combat | 21 | 281 | 8 | 13 | 197 | 39 | 36 | 9 | grapple 142e93540 / 142e639b0, boot requirement 14370f590, CanUseBootAbility 1432dbd70 / 1432e3b50 |
| Client core / network / commands | 87 | 211 | 9 | 78 | 102 | 62 | 31 | 16 | PlayerUpdate dispatcher 142cdfa60 (#3), run loop 142cd4e60, client tick 142cd6830 |
| Inspect / Bio (packets: #3) | 17 | 14 | 10 | 7 | 8 | 1 | 1 | 4 | 142fd4290, 14308b5c0, 1435d54e0 |
| Mesh Shapes (hand-off #2) | 50 | 2 | 23 | 27 | 0 | 0 | 2 | 0 | 14374e110, 14374d8a0, 1437ac720 |
| Terrain / VoxelFarm (hand-off #2) | 49 | 198 | 29 | 20 | 129 | 37 | 16 | 16 | 14315e3e0, 14314cfe0, 143f93c90 |
| UI framework (Scaleform widgets) | 51 | 131 | 20 | 31 | 64 | 26 | 11 | 30 | UICheckbox 143f0e6b0, UIDropdownbox 143ee07c0, GFx canvas 143719bc0 |
| Settings, audio, input | 34 | 74 | 4 | 30 | 68 | 1 | 1 | 4 | ambient volume 1435f2d40 / 142d5c0f0, 1434883e0 |
| Warp / travel / area | 21 | 87 | 4 | 17 | 32 | 14 | 18 | 23 | AreaPropertyDefinition loader 143dc1440 (story tools, #6) |
| HUD indicators / tooltips | 37 | 55 | 4 | 33 | 40 | 7 | 7 | 1 | — |
| Social (chat, guild, mail) | 7 | 64 | 1 | 6 | 18 | 21 | 14 | 11 | chat tab timestamps 14336b760 |
| Items, inventory, crafting | 13 | 55 | 4 | 9 | 8 | 10 | 16 | 21 | inventory dispatcher 142f202e0 (#3 §10), repair 14360fa90 |
| Store / commerce / Player Studio | 15 | 50 | 2 | 13 | 16 | 21 | 11 | 2 | 143262c00, 143258720 |
| Requirements | 35 | 19 | 9 | 26 | 8 | 4 | 6 | 1 | 143710380 (game setting), 14370f590 (boot ability) |
| Content / data management | 31 | 37 | 5 | 26 | 17 | 6 | 8 | 6 | manager registration 142d8fc20 / 142d224e0 |
| Vehicles | 4 | 116 | 3 | 1 | 76 | 25 | 10 | 5 | none with new behaviour found |
| Graphics / physics / audio engine | 1 | 181 | 1 | 0 | 79 | 72 | 27 | 3 | — |
| Third-party T4 text library | 0 | 83 | 0 | 0 | 56 | 25 | 2 | 0 | — |
| Social share / screenshots | 9 | 3 | 0 | 9 | 2 | 1 | 0 | 0 | — |
| Other engine / misc | 77 | 402 | 21 | 56 | 214 | 107 | 46 | 35 | — |
| Unassigned (no usable evidence) | 74 | 198 | 29 | 45 | 79 | 68 | 46 | 5 | L41 |
| Noise: static initialisers / CRT | 461 | 127 | 450 | 11 | 114 | 7 | 4 | 2 | — |
| Noise: EH funclets | 866 | 2172 | 3038 | 0 | 0 | 0 | 0 | 0 | — |
| **Total** | 3,013 | 5,238 | 4,089 | 1,096 | 1,602 | 671 | 471 | 322 | |

### Server-relevant findings (details in the sections)

1. **Game settings are keyed by a string hash.** 0.695 looks every Game Table setting up by the SOE one-at-a-time
   hash of its key name (`h = (h + c) * 0x401; h ^= h >> 6` per byte, then `h = ((h*9) >> 11 ^ h*9) * 0x8001`, signed
   int shifts): FUN_143c1c060 (IsGameFFA, NumberOfTeams), FUN_142dc6b80 (InstancedGames_AllowSwitch), FUN_143c1c690
   (InstancedGame_AllowPlayersTeam<n>), FUN_1434e7570 (InstancedGames_TeamName<n>), FUN_1434e4a40
   (InstancedGames_VictoryMessageTeam<n>), FUN_143710380 (any setting named by a requirement). The settings reader
   FUN_1434d9330 stores the wire u32 as the entry's key (+0x68), so **the GetConfigurationResponse settings-map key
   must be that hash of SettingKey** (resolves the "u32 key" of L32). Values stay strings ("1"/"t"/"T" = true).
2. **Per-team settings now drive the UI:** team names, "allow players" per team, victory messages per team and team
   switching (InstancedGames_AllowSwitch, plus #3's new site-game u32) come from the settings map; a server that only
   sends the 0.589 settings gets the default names "Team %d" and no switching.
3. **Game Finder** (1434e9a50, 1434ed2e0, 1434f2da0): the server must answer MatchmakingRequest with
   MatchmakingUpdate (counts + search state 0 searching / 1 match found / 2 searching / 3 removed + game-type flag bits
   2 FFA, 4 Teams, 8 PvE, 0x10 PvP); all text is client-side.
4. **Lua InstancedGame.IsInGame now returns true while observing** (0.695 FUN_143604410 vs 0.589 FUN_143551a80):
   an ObserverRegistration (0.695 sub 0x16) puts the observer's HUD into in-game mode.
5. **New end-of-game state 7**: resetting the game manager closes the end-game scoreboard when state +0x28 == 7
   (FUN_1434e2330 vs 0.589 FUN_143432090).
6. **GameEvent type 10 / 11 (point grants)** are new (FUN_1434e7900 vs 0.589 FUN_143437440): chat lines
   InstancedGame.PointGrantPlayerFFA / PointGrantPlayerOnTeam / PointGrantTeam with `pointCount` read from event
   +0x6c (one of #3's new Event fields, L32).
7. **Play Mode Rules are enforced by the client** for grappling (bit 1), boot abilities (bit 2), free / orbit camera
   (bits 8 / 0x20) and the flying character camera (bit 0x10) — but **voxel destruction (bit 4) only by the server**:
   the client shows `ClaimDestruction.Fail.NotAllowed` when an Abilities EffectEnd arrives with the new result code 9
   (FUN_142e18c70 vs 0.589 FUN_142d7aca0, which knew codes 2-8).
8. **Crimson Parlor accept (PlayerSet, 0x11C sub 1) gained two trailing u32** (writer FUN_1430a0530; 0.589 body:
   u32, u64, customization data): the new base-actor id of `Char_Biped_<race/gender>_BaseActor.adr` (FUN_1430a6570,
   manager +0x1058) and a 0. The client rebuilds its own actor when race or gender changed (FUN_1430a2530). Hand-off #3.
9. **Barbershop options are gated in 0.695** (0.589 listed every tint): a tint is listed only if the player is admin,
   the ArtTint row is DEFAULT_UNLOCKED, the customization-knowledge set (player +0x7ae28, filled from SendSelfToClient and
   0x11C sub 2 AddKnowledgeResponse) contains it, or the store sells an item that unlocks it (then PurchaseRequired +
   MarketName / MarketCost from the cheapest store bundle). FUN_1430a4720 / FUN_1430a3210, option slots FUN_1430a37a0.
   **A 0.589-era server that sends no knowledge shows only default-unlocked tints to 0.695 clients.**
10. **Opening the Parlor now waits for a reply**: FUN_1430a5ae0 sends EquippedAttachmentRequest (0x11C/3) on every
    open (0.589 FUN_143001510 sent nothing). The window opens without waiting; what it does with the reply: L34.
11. **Design voxel-data requests are queued and throttled** (FUN_1431136c0 / 14310b4d0 vs 0.589 FUN_143068e70 /
    143061eb0): up to 25 TemplatePacketClientVoxelDataRequest (0xF0/4) per burst, a burst at most every 0x65 time
    units; same wire.
12. **Daybreak Designs category is data-driven**: the 0.589 fixed "Template.Category.Daybreak" (0.589 FUN_1436966c0)
    is replaced by ExportedTemplateCategory / ExportedTemplateCategoryMap (FUN_1437546f0) — category 1 "Starter Castle"
    (text 576947, 28 template ids) and 3 "Collected" (148 ids); which template field the ids match: L40.

---

## 0. Method and caveats

1. **System assignment** (classify.py, sysmap.py): (a) direct evidence — the function is in an RTTI vtable
   (re/out/rtti_695.tsv), constructs / destroys a class (`X::vftable` reference), names its source file / method in a
   string, or is a Lua binding (re/out/luabind_695.tsv): 1,614 functions; (b) otherwise the callers' (910) or callees'
   (254) direct labels; (c) otherwise the nearest labelled neighbours by address (MSVC links a translation unit's
   functions together): 1,851; (d) 584 had no evidence (312 of them static initialisers, set aside as noise; 272 Unassigned, L41). Labels were mapped to systems by keyword, and
   16 data-source / loader functions whose strings prove another system were reassigned by hand (sysmap.py `OVR`).
   Locality-only assignments are the weakest; every function cited below was read.
2. **Change kind** (compare.py): both decompiles normalised (FUN_ / DAT_ / LAB_ / s_ names, absolute addresses, local
   variable names masked) → ratio; then struct offsets masked → `layout-only` when equal; `minor` ≥ 0.95,
   `moderate` ≥ 0.80, `major` below. Five functions are too large for the line diff (142cdfa60 = PlayerUpdate
   dispatcher, #3; 14062e5c0, 143a5f680, 143a66e30: identical apart from table / label addresses; 142a88de0: maths
   helper with one extra byte parameter).
3. **"new" is not always new.** falsenew.py: 1,096 new functions have an identical masked twin in 0.589;
   strpartner.py: 31 of the 60 new functions with ≥ 2 strings have a 0.589 function with a near-identical string set
   (e.g. the scoreboard handler FUN_1434e4a40 = 0.589 FUN_143434580, the HUD handler FUN_1434e2f70 = 0.589
   FUN_143432bd0, the rule loader FUN_143b736b0 = 0.589 FUN_143ab8cb0). The comparisons below use those partners.
4. **Text ids:** no new 0.695 text id (577069-577746) is used as a constant by any new / changed function (one changed
   id, 562228, in FUN_142dbe1d0). New messages reach the screen through string keys looked up in the locale (e.g.
   `Grapple.AdventureBlocked`) or through Lua / Flash; the complete list of new literals is newstrings.tsv (169).

---

## 1. Site games (Game Tables) and Game Finder — 234 new / 135 changed

Owner class ClientInstancedGameManager (DAT_145438110; created in FUN_1434dca20, registered by FUN_142d8fc20). Wire
details of every packet below: packets-695.md §5, §A5, §B6, §B7.

**S→C dispatch (FUN_1434ee920 / 0.589 FUN_14343c3d0).** Same structure; the cases moved with #3's renumbering and one
case was added: 0x1A MatchmakingUpdate → FUN_1434e9a50. Every other handler is the 0.589 one with the class layout
grown (State FUN_1434e96c0 / 143438fe0 minor; Request FUN_1434e93e0 / 143438d00; PropState FUN_1434e8ea0 / 143438840;
ObserverRegistration FUN_1434ea130 same; TeamMembers FUN_1434e97c0 layout-only).

**Game Finder (new).**
- Lua `InstancedGame.ShowGameFinder` (FUN_143604db0 → FUN_1434ed2e0) fills data sources `GameFinder.MatchmakingInfo`
  (ActiveGames, QueuedGames, InactiveGames, Players = manager +0x424 / +0x428 / +0x42c / +0x420) and
  `GameFinder.GameFlags` (rows FFA Games, Team Games, PvE, PvP, In Progress Games; `Enabled` = bits 2 / 4 / 8 / 0x10 /
  0x40 of the flag word +0xe0).
- `SetGameFinderFlag(bit, on)` (FUN_143604dd0 → FUN_1434f23c0) sets the bit, or **toggles** it when `on` is false
  (XOR — a client quirk, not a clear).
- `StartMatchmaking` (FUN_143604e40 → FUN_1434f2da0): if the player is not in a game (+0x38 invalid) it sends
  MatchmakingRequest {player guid, flag word, start flags 0x101} and sets "matchmaking" (+0x402); otherwise prints "You cannot
  queue for a game while playing another one!" (FUN_142ca7150 type 7). `StopMatchmaking` (FUN_1434f2f90) sends 0.
  `IsMatchmaking` (FUN_143604e80) returns +0x402.
- MatchmakingUpdate handler FUN_1434e9a50 stores the four counts, sets `SearchState` / `StateString` ("Searching..."
  for 0 and 2, "Match found!" for 1, "Search inactive." otherwise; 1 and other values also clear +0x402), calls Lua
  `UpdateGameFinder`, and prints: state 0 "You've been entered into the search queue...", state 3 "You've been removed
  from the matchmaking queue.", state 1 "You've been queued for a match! Match details:" + FFA / Teams / PvE / PvP from
  the update's flag byte (+0x38), then looks up a client constant (key 0x565a5445 in the list DAT_145431198 +0x2258)
  and passes it to FUN_1431f7e90 (an effect / sound cue; not followed). It does not move the player — L31.
- **Server:** keep a queue, answer every request with an update (state 0 / 3), announce matches with state 1, then
  join the player through the existing InstancedGame flow (L31).

**Teams.** `InstancedGame.SwitchTeam` (FUN_143604510, new) opens Lua `CreateDropdownDialog("Switch Team", "Select a
team...", data source InstancedGameScoreboard.JoinableTeams, callback InstancedGame.SwitchTeam)`; the callback sends
RequestJoinTeam (FUN_1434f1210, Request type 9; #3 §5c). Team names (FUN_1434e7570 / 0.589 FUN_143437160): setting
`InstancedGames_TeamName<n>` first (new), then the locale key `InstancedGame.Teams.DefaultName_%d`, then "Team %d".
Joinable teams: setting `InstancedGame_AllowPlayersTeam<n>` (FUN_143c1c690, new). The walk-on window's CanSwitchTeam
(FUN_142dc9660 / 0.589 FUN_142d2b3f0) also needs `InstancedGames_AllowSwitch` (FUN_142dc6b80) and #3's new u32 ≥ 2
(L37).

**Game settings and rules (the Game Table rework).**
- Settings are no longer a fixed list: 0.589 `ResetGameSettings` (FUN_143431540) wrote ten named settings
  (MaxQueueOfflineTime, IsGameFFA, NumberOfTeams, Min/MaxPlayersPerTeam, MaxQueueJoinTime, MaxGameOfflineTime,
  AllowsObservation, Difficulty, PvPAllowed); 0.695 FUN_1434e1f30 iterates whatever settings the server sent
  (`RulesetObjects.GameSettings.%d`).
- Key = string hash (Summary 1). Reader FUN_1434d9330 → entry {u32 key at +0x68; FUN_1434d6b10: u32 id, str
  SettingKey, str SettingDescription, str SettingValue, u32, u32}.
- UI: FUN_1434f39a0 builds `RulesetObjects.GameSettings.CategoryList` ("General Settings", "Game Type Settings",
  "Game Name", add buttons) and per setting OptionType TextInput / Slider (MinValue, MaxValue, StepInterval) /
  CheckBox; FUN_1434edcd0 does the same for rule trigger parameters (TriggerTypeId, ArgIndex, ShowHelp).
  RulesetObject.UpdateValueBool (FUN_143608450 → FUN_1434f4d60) sends AddOrRemoveRule at once; UpdateValueFloat /
  UpdateValueString (FUN_1434f4fe0 / FUN_1434f5190) only edit the local entry (callees FUN_1434d9fc0 / FUN_1434da070,
  no packet); UpdateDirtySliders (FUN_1434f31a0) only rebuilds the settings UI (FUN_1434f39a0); the whole set goes
  back with SetConfiguration (#3 §B7).
- Tables (client-side, loader FUN_143b736b0 / 0.589 FUN_143ab8cb0): the 0.589 Ig* set plus two new ones, "Ruleset
  Script Props" (RuleSetScriptPropDefinition FUN_143b75220: RULE_SET_ID, PROP_ID, MIN_COUNT, MAX_COUNT, NAME_ID) and
  "required rules" (RuleRequirementFromGeneralSettingDefinition FUN_143b74c10: RULE_TYPE_ID, GENERAL_SETTING_ID,
  REQUIRED_VALUE). IgRuleTypes gained INTERNAL_DESCRIPTION, IS_PLAYER_ADDABLE (FUN_143b75330 / 0.589 FUN_143aba120);
  IgGeneralSetting ("IgRuleSetGeneralSettings" in the loader) gained CLIENT_REQUIREMENT_ID, DATA_TYPE, NAME_STRING_ID, SORT_ORDER, STEP_SIZE
  (FUN_143b74e40 / 0.589 FUN_143ab9e00). New requirement ClientRequirementInstancedGameSettingValue (FUN_143710380):
  compares a named setting's value (atof) with the requirement's value — rules can depend on settings.
- **Server:** the settings it sends must use the setting names of RefIgGeneralSettingsTypes (0.695 adds ids 12-28:
  InstancedGames_AllowSwitch, TeamName0-4, InstancedGame_AllowPlayersTeam1-4, ScoreVictoryCond, ScoreVictoryValue,
  PlayersToStart, VictoryMessageTeam1-4; per-rule-set values in IgGeneralSetting), hashed; rules a setting
  disables (required rules) must be refused when the client would not offer them.

**HUD, scoreboard, events.**
- HUD handler FUN_1434e2f70 (= 0.589 FUN_143432bd0): same fields (RoundCounter, RoundTimer, Wave, Indicator,
  LabelTotal, ProgressBar, Label, "Waiting for players...", "Game over! Resetting in %d %s..."); only the colour
  constant "FFFFFF" went and a "Label" row was added.
- Scoreboard handler FUN_1434e4a40 (= 0.589 FUN_143434580): victory text per team from setting
  `InstancedGames_VictoryMessageTeam<n>` (new), else InstancedGames.VictoryMessage / DrawMessage / GameOverMessage.
- Event handler FUN_1434e7900 (0.589 FUN_143437440): types 1-4 kills, 7-9 flag / control point unchanged; new 10
  (PointGrantPlayerFFA if the game has no teams, else PointGrantPlayerOnTeam) and 11 (PointGrantTeam) with
  `pointCount` (event +0x6c). These are the "score chat lines" of the Score Node.
- State: reset (FUN_1434e2330) closes the end-game scoreboard for state 7 and calls FUN_1434ec970 (CloseScoreHUD,
  `GameEvents:OnCloseGameInProgressDialog`); a new FUN_1434ec8b0 opens it (`OpenScoreHUD`).
- `IsInGame` (Summary 4). The observer guid +0x40 is set by ObserverRegistration type 1 (FUN_1434ea130, unchanged,
  which also opens `GameEvents:OnOpenGameInProgressDialog`).
- Queue window (GaqInstancedGameQueue, FUN_1433e67a0 / 0.589 FUN_143339ea0): same strings (UI.LeaveQueue,
  UI.JoinNow); recompile only.

## 2. Designs / templates — 238 new / 77 changed

- **Daybreak Designs categories** (FUN_1437546f0, 0.589 FUN_1436966c0): 0.589 had four fixed categories (Personal,
  Daybreak, PlayerStudio, Collected). 0.695 loads ExportedTemplateCategory (ID, NAME_ID, SORT_ORDER) and
  ExportedTemplateCategoryMap (ID, CATEGORY_ID, TEMPLATE_ID), recognises Personal / PlayerStudio / Collected by name
  and gives every category a set of template ids. The shipped tables (identical files in both installs; only 0.695
  reads them): 1 "Starter Castle" (28 ids), 2 Personal, 3 Collected (148 ids), 4 Player Studio. What a TEMPLATE_ID
  (6 … 1324) is matched against, and so whether the server must send those Designs: L40.
- **ExportedTemplates** (ExportedTemplateDefinitionManager + SideLoader, loaders FUN_143de3af0 / FUN_143de3e00):
  TEMPLATE_FILE_NAME (.otd), TEMPLATE_VERSION, TEMPLATE_NAME_ID, AUTHOR_NAME_ID, IS_ROTATABLE_ABOUT_Y_AXIS,
  AIR_PASTE_OPTION, DOES_FLASH_TEMPLATE_INFO, PLAYER_CAN_DELETE, CREATION_DATE, WIDTH / LENGTH / HEIGHT; sides:
  FACE_ID, ATTACHMENT_TYPE_ID, BUFFER_IN_VOXELS, ALLOWED_EMBED_DEPTH, ALL_AIR_VOXEL_PADDING (the modular-piece
  attachment data). No caller outside the definition managers was found; whether any client feature uses the sides:
  part of L40.
- **Voxel-data requests** (Summary 11): FUN_1431136c0 gained a "defer" flag; deferred guids go to a list (+0x100,
  count +0x110) that the manager tick FUN_14310b4d0 flushes 25 at a time. The 0.589 rate limits for the other two
  timers (0x5DD) are unchanged.
- **Clipboard** (FUN_143111480 / 0.589 FUN_1430671e0): also publishes `Character.Templates` → `PropCount` (props in
  the copied Design).
- **Paste** (new threaded paste, VoxelFarm::Internal::PasteThread / PreviewGenerationThread): the cursor
  (FUN_1436ce040) shows "Generating preview..." and "Pasting in progress..." / `Paste.InProgress` with progress;
  FUN_1436d00b0 refuses a second paste ("A paste is currently in process. Please wait until it completes and try
  again!") and otherwise hands the props and cells to the existing paste senders FUN_1431642e0 / FUN_1431645b0
  (moderate, same packets). Server: nothing new; pastes simply cannot overlap.
- **Buy a Design's missing materials**: senders FUN_143113530 (DataRequest), FUN_1431118e0 (Purchase), handler
  FUN_14310c290 (data source `Character.Templates.PointOfSale`: TemplateGUID, TotalLumens, RecipeId,
  RecipeCraftCount, LumensNeededToCraft, ItemDefinitionId, ItemCountToBeCrafted, ItemCountNeeded,
  FoundLumenRecipeForAllItems). Wire and open questions: packets-695.md §3, L29.
- **Player Studio**: FUN_143258720 (0.589 partner, major) adds the error "Failed to create product. Couldn't find
  template data!" when the IsTemplateValid response names a template the client has no data for; product creation
  now goes through the new TemplateProductCreate (#3 §12).
- TemplateBasicData lost its PropData list (#3 §B4); the template S→C dispatcher FUN_14310bbf0 (new address, case list
  +0x1E) is #3's.

## 3. Crimson Parlor (character customization) — 166 new / 85 changed

- **Open** (FUN_1430a5ae0 / 0.589 FUN_143001510): new — touches data source `CharacterCustomization`, sends
  EquippedAttachmentRequest for the target (own guid if none), builds the preview actor from the target's race / gender
  (FUN_1430a6570: `Char_Biped_<name>_BaseActor.adr`, name from FUN_143b88330(race +0x1248, gender +0x124c), actor id →
  +0x1058, default preset FUN_143bae210 → +0x1250), then the old steps and `HudStateManager:OnOpenBarberShop`.
- **0x11C S→C entry** (FUN_1430a29d0, new): sub 2 AddKnowledgeResponse → the existing knowledge store
  (FUN_14368a830 = 0.589 FUN_1435d7a50) and then **refreshes the open window** (FUN_1430a33a0, FUN_1430a3d30); sub 4
  EquippedAttachmentReply → manager +0x1360 (FUN_1430a5c60; use: L34).
- **Race / gender** (admin `/barberrace`, `/barbergender`; Lua CharacterCustomization.SetRace / SetGender): presets
  per race and gender (CharacterCreatePreset, CharacterCreateSelectionDefinition, CharacterPresetToSelectionMapping
  loaded by FUN_143c13c90; slot fill FUN_1430a68a0, logs "Character preset missing data {Race, Gender}"), option
  tables per race / gender (new RTTI hash maps of CharacterCreateOptionsManager::GenderOptions / RaceOptions /
  SlotOptions, re/out/rtti_new_695.txt; loader FUN_14309b9f0 / 0.589 FUN_142ff9bd0; equality rules
  CharCreateOptionEqualities). The data source FUN_1430a3d30 (0.589 FUN_1430001a0)
  adds Gender, Race, NumOptions, OptionIndex, OptionSlot, OptionType.
- **Accept** (FUN_1430a2530 / 0.589 FUN_142ffeec0): PlayerSet with the two new trailing u32 (Summary 8); if race or
  gender changed it rebuilds the local actor (FUN_142eca480, FUN_1430a1e00). **Server:** apply race / gender, the
  model id and the costs (CharCustomizationSlotCost / CharCustOptionMult tables, loaded by FUN_143c114b0 into
  CharacterCustomizationCostDefinitionManager), save, and broadcast CustomizationChange (PlayerUpdate 0x1B — new attachment list, #3 §B3) to others.
- **Purchase-gated options** (Summary 9): tints FUN_1430a4720 + FUN_1430a3210, option slots FUN_1430a37a0 (0.589
  FUN_142fffea0 added MarketName, MarketCost, PurchaseRequired, ActorId, RequiresActorId). Prices come from the
  InGamePurchase manager (DAT_1454311c8 +0x32050; item → bundle index +0xd98, bundle price FUN_143c2c370, bundle name
  text +0x17c), i.e. from the store bundles the server sends.

## 4. Build sites and Play Mode Rules — 95 new / 146 changed

Claim entry fields (from #3 / #6): mode +0x378 (2 = Play Mode), Play Mode Rules mask +0x380.
- Data source `Claim.AdventureFlags` (FUN_142dd2480, new): six rows with `Value` = bit and `ValueKey` = 1 Block
  Grappling Hook, 2 Block Boot Abilities, 4 Block Voxel Destruction, 8 Allow Free Camera, 0x10 Allow Flying Character
  Camera, 0x20 Allow Orbit Camera; toggles go through `Claims.SetClaimAdventureFlag` (FUN_1435e6470 → FUN_142dd08e0,
  sends the whole new mask).
- **Client enforcement** (all new or changed):
  - grappling (FUN_142e93540 / 0.589 FUN_142df5360): refused while gliding (`Grapple.NotWhileGliding`, new) or in a
    Play Mode site with bit 1 (`Grapple.AdventureBlocked`); the fire path FUN_142e639b0 calls it.
  - boot abilities: requirement ClientRequirementCanUseBootAbility (FUN_14370f590): false in Play Mode with bit 2;
    shows the requirement row's text id (+0x3c; the 0.695 text 577415 "Boot abilities are disabled on this Build
    Site." is the likely one — not checked against Requirements.txt).
  - camera (FUN_142dd5540 orbit / free, FUN_143607950 `Controller.ToggleCharacterFlight`, FUN_1435e4530
    `Claims.ActivateCameraCharacterFlight`): in Play Mode the flags decide; elsewhere the claim permission (≥ 8 for
    flying camera). 0.589 (FUN_142d373f0) refused the flying camera in Play Mode outright (`Claim.NoCameraPlayMode`).
  - voxel destruction: server only (Summary 7; Abilities EffectEnd result 9 → `ClaimDestruction.Fail.NotAllowed`).
- `Claims.StartClaimPlacement` (FUN_1435e2fa0 → FUN_142dd22f0) — place a site from the window; PlayerEnteredClaim is
  sent by FUN_142dc8ec0 (0.589 FUN_142d2ad00, minor) — #3 §7-8, L33.
- `Claims.Get/SetAllowClaimWideLOD` (FUN_1435e6360 / 1435e6390) and the site-wide LOD option FUN_143489560 /
  FUN_14348cb50: client setting only.
- Claim record readers / CreateClientClaim (FUN_142db5ca0 / 0.589 FUN_142d16cf0) and the dispatcher
  FUN_142dbecb0: #3 §A2, §B5.

## 5. Props, story-tool UI and prop help — 111 new / 107 changed (spec: #6)

- **Prop help windows** (new tables PropHelpWindow / PropHelpWindowMapping, manager FUN_143c16400 / FUN_143c16c80,
  columns WINDOW_ID, WINDOW_STRING, SIZE_STRING, ALIGN_STRING): `Prop.OpenHelpWindow(prop)` (FUN_1435fbc80) maps the
  prop's definition id (actor +0x2458) to a window and calls Lua `ShowHelpWindow` with its three strings. Client-only.
- **NPC dialog response** (Story Node question): HUD indicator HudIndicatorDefinitionNpcDialogResponse /
  NpcDialogResponseIndicator (FUN_14343ca50, FUN_143440d90, FUN_14372c440); Confirm / Deny send PropPacketDialogResponse
  (FUN_1435fbf80 / FUN_1435fc030 → FUN_1435fc490). #3 §4, L30.
- **Property editor**: text fields (LuaPropertyEditor::PropertyTextLine, FUN_1435bc890) and the option builder
  FUN_1435c1b60 (0.589 FUN_143510b70) now emit OptionType TextInput / largeTextInput with MaxCharacters, CheckBox with
  DefaultValue, a text block; drop-downs (FUN_1435c0270) drop the generated "Option %d" labels. Prop settings with
  text values (team names, victory messages) — #6.
- Counting Node digits (`ScriptCounter.FirstDigit.CompositeEffect.0`, FX_Attach1-4: FUN_1430a9260 / FUN_1430aa110),
  link messages (`AdvancedScripting.UniqueBinding.BindSingle`, FUN_142fee7e0), AreaPropertyDefinition
  IF_NODE_CAPABLE / IF_DISPLAY_STRING_ID / BLOCKS_USER_MODIFICATION (FUN_143dc1440): already in storytools-695.md.

## 6. Building tools — 124 new / 79 changed

- Paste / preview threads and cursor: §2.
- Category tables (CategoryDefinitionManager FUN_1434d0970 / FUN_1434d1810 / FUN_1434d3350, moderate) also load
  MeshCategory / MeshCategoryGroup / MeshCategoryMeshMap (log lines "%d MeshCategoryMeshMapDefinitions loaded"); hand-off
  #2.
- Undo / redo: FUN_143165920 adds a log when VoxelFarm reports an unchanged cell ("removed cell from final pending
  list"); its funcdiff partner is wrong, so no comparison. Wire unchanged (#2 §6, #3).
- Tool input handlers FUN_1434505f0 / FUN_14346d4a0 (moderate): same strings in both builds; layout / recompile.
- WorldEditing dispatcher FUN_1430838d0 adds WorldEditingPlayEffectPacket (#3 §11, L36).
- `Tool.ToggleCursorSmoothing` / `GetCursorSmoothing` (FUN_1436066a0 / FUN_1436066e0): client option ("Smoothing
  Enabled").

## 7. Tints and art sources — 85 new / 49 changed

- New ArtTintGroupUnlock (UNLOCK_GROUP_ID, UNLOCKING_ITEM_ID) and ArtTintGroupUnlockTint tables (managers
  FUN_143c6be40, FUN_143c6c260, FUN_143c6cf50; loaded from the CharacterCreateOptionsManager loader FUN_14309b9f0 /
  0.589 FUN_142ff9bd0), ArtTints gained DEFAULT_UNLOCKED (FUN_143db52d0 / 0.589 partner, major). Tint → unlock items:
  FUN_143098500. Use: §3 (gating). Which knowledge id the server must grant when an unlocker is used: L39.

## 8. Character, movement, combat — 21 new / 281 changed

197 of the 281 changes are layout-only (the player classes grew; e.g. the embedded FirstPersonLocalProxiedCharacter
sub-object moved from qword 0x964 to 0x96a, FUN_14332ec70 / 0.589 FUN_143281e00). Behaviour changes: grappling (§4), the boot-ability
requirement (§4), the destruction result code 9 (§4; handler FUN_142e18c70 in the Abilities dispatcher FUN_142e1aab0).
No code for the Roll Wraps roll, strafing or rank 2 / 3 abilities was found among the new functions — they use the
existing ability / animation code with new table rows (tables-diff.md; items: #7).

## 9. Client core, network, commands — 87 new / 211 changed

- The big loops are unchanged in substance: run-state machine FUN_142cd4e60 (0.589 FUN_142c3b490) and client tick
  FUN_142cd6830 have the same strings (the tick adds "Ambient Audio"); control flow only reshuffled by the compiler.
- PlayerUpdate dispatcher FUN_142cdfa60 (u32 sub-opcode) and every other family header: #3 §A1.
- New Lua natives with no server side: Ui.GetAmbientVolume / SetAmbientVolume, GameSettings.Get/SetAmbientVolume
  (FUN_1435f2d40, channel "Ambient Audio" FUN_142d5c0f0).
- Chat: PrintMessageInChatTab (FUN_14336b760 / 0.589 FUN_1432be950) stores `Timestamp` and `Message` per line (chat
  window timestamps; client clock). Mail / guild / Player-Studio mail functions (FUN_143638e90, FUN_143639de0,
  FUN_14363a0c0, FUN_142ef4330) are the IString recompile pattern — same strings.

## 10. Inspect and Bio — 17 new / 14 changed

Bio editor `Character.PlayerBio` (FUN_142fd4290, bad-word marking, 1,501-unit send throttle) and Inspect
(`Ui.InspectPlayer` FUN_1435c45b0 → FUN_1435d54e0; reply → FUN_14308a260 → `Character.InspectData` FUN_14308b5c0:
CharacterName, CharacterGuid, NumberOfClaims, NumberOfGalleryListings, LuminaryScore, BioString, equipment slots;
Lua `OnInspectPlayer`) are fully traced in packets-695.md §1-2 (L28).

## 11. Smaller systems

- **UI framework:** new native widgets UICheckbox (FUN_143f0e6b0 …) and UIDropdownbox (FUN_143ee07c0 …) for the Lua UI
  (UIObjects.lua UICheckbox, DropdownDialog.lua); GFx canvas changes (FUN_143718f30 drops the "pDevice->Clear" log,
  FUN_143719bc0) are renderer-side. No server effect.
- **Items / inventory:** inventory dispatcher FUN_142f202e0 adds ShowActivationFailure → `CreateConfirmationDialog`
  ("Activation Failure", #3 §10, L35); repair data sources FUN_14360fa90 / FUN_14360fc00 add CurrencyIcon /
  CurrencyName (repair cost shown with a currency icon and name; source fields not followed). Chest / player inventory data sources (FUN_142f258f0, FUN_142f23a70): recompile pattern.
- **Store:** `Browser.SetMarketplaceMode` (FUN_1435e1780, boilerplate twin of 0.589 FUN_143530290); commerce senders
  moved by the 0x45 renumbering (#3 §A4).
- **Requirements:** ClientRequirementCanUseBootAbility, ClientRequirementInstancedGameSettingValue (above);
  ClientRequirementCheckCountry FUN_14370e760 already existed (RTTI in both builds; unpaired).
- **Warp / area:** teleport senders (FUN_143173390 …) are layout / recompile; AreaPropertyDefinition: §5.
- **Vehicles, graphics, physics, audio, T4:** no new strings, no new packets; layout / recompile.
- **Mesh Shapes:** the palette (FUN_14374e110) enumerates DAT_145437390 — the VoxelClient's shape manager created in
  FUN_14314bda0, read with the same accessors as 0.589 (FUN_14349c7a0 = 0.589 FUN_1433eebd0, FUN_14349e9a0 = 0.589
  FUN_1433f0dc0) — and its entry writer FUN_1437ac720 has the same string set as the 0.589 Shapes-palette writer
  FUN_1436f4bb0: supports #2's L17 (client-only palette); selection sends ToolPacketActivate (Mesh.PlaceMesh /
  SelectMesh).
- **Unassigned (272) and Other (479):** mostly layout-only / minor helpers; L41.

---

## Breaks / behaviour changes (what can break a 0.589-era server feature on the 0.695 client)

| # | Area | Change | Effect on today's server | Evidence |
|---|---|---|---|---|
| B1 | Game Table settings | settings-map key = hash(SettingKey), names from the 0.695 RefIgGeneralSettingsTypes table | settings the server sends with 0.589 keys / strings are never found; FFA, team count, switching, team names fall back to defaults | FUN_1434d9330, FUN_143c1c060, FUN_142dc6b80, FUN_1434e7570 |
| B2 | Game Table settings | reset / apply iterate the received list instead of ten fixed names | the 0.589 fixed-name handling in InstancedGames.Editor.cs does not match what 0.695 sends back | FUN_1434e1f30 vs 0.589 FUN_143431540 |
| B3 | InstancedGame | IsInGame true while observing; state 7 = end-game scoreboard | observers get the in-game HUD; a server that never uses state 7 never shows the 0.695 end scoreboard | FUN_143604410, FUN_1434e2330 |
| B4 | Crimson Parlor | tints / options gated by DEFAULT_UNLOCKED, knowledge, store | without knowledge ids, 0.695 players see fewer tints than 0.589 players | FUN_1430a4720, FUN_1430a3210, FUN_1430a37a0 |
| B5 | Crimson Parlor | PlayerSet has two trailing u32; open sends 0x11C/3 | a server reading the 0.589 PlayerSet length may reject / mis-read; 0x11C/3 is logged as unhandled | FUN_1430a0530, FUN_1430a5ae0 |
| B6 | Play Mode Rules | grappling / boot / camera checks read the claim mask +0x380 | if the server does not fill the new trailing u64 / kind 0x17 (#3 §B5) the mask is 0: no blocks, and Play Mode camera toggles are refused | FUN_142e93540, FUN_14370f590, FUN_142dd5540 |
| B7 | Play Mode Rules | voxel-destruction block is server-side, reported as EffectEnd result 9 | a server must refuse destruction itself and send code 9 (code 8 shows the harvest message) | FUN_142e18c70 vs 0.589 FUN_142d7aca0 |
| B8 | Designs | Daybreak category from tables; voxel-data requests arrive in bursts of up to 25 | Designs the server files under the 0.589 "Daybreak" category may not appear (L40); request handling must tolerate bursts | FUN_1437546f0, FUN_14310b4d0 |
| B9 | Game events | types 10 / 11 read pointCount at event +0x6c | needs #3's new Event body; 0.589 bodies give 0 points | FUN_1434e7900 |

Plus #3's family-wide renumbering (packets-695.md §A), which the handlers above depend on.

## Hand-offs

- **#3 (packets):** PlayerSet (0x11C/1) C→S body gained two trailing u32 — writer FUN_1430a0530 vs 0.589 template
  FUN_142ffd830 (body: u32, u64, customization data FUN_142f59020 = 0.589 FUN_142eba3f0); the first is the base-actor
  id (FUN_1430a6570), the second 0 on this path. Not in packets-695.md yet.
- **#3 / coordinator (L32):** GetConfigurationResponse settings-map u32 key = SOE string hash of SettingKey (Summary 1).
- **#3 / coordinator (L31):** MatchmakingUpdate +0x38 is the game-type flag byte (2 FFA, 4 Teams, 8 PvE, 0x10 PvP),
  +0x30 the search state (FUN_1434e9a50).
- **#3 / coordinator (L34):** FUN_1430a5c60 only moves the reply's attachment list into manager +0x1360 (list copy
  through FUN_142f5a3e0); no reader of +0x1360 was found among the new / changed functions.
- **#6 (story tools):** prop help windows (§5), property-editor option types (FUN_1435c1b60), point-grant events
  (§1), settings-driven team names / victory messages (§1).
- **#2 (terrain / Mesh Shapes):** Mesh palette source evidence for L17 (§11); paste / preview threads and
  FUN_143165920 (§2, §6).
- **#7 (items):** tint unlocker items (ArtTintGroupUnlock.UNLOCKING_ITEM_ID) and the knowledge they grant (L39).
- **Coordinator:** "new" in functions.tsv includes ~1,100 boilerplate / unpaired functions (falsenew.tsv,
  strpartner.tsv); a re-run of funcdiff with string-set pairing would shrink the list.

## Open leads added (OPEN-LEADS.md)

L39 tint knowledge granted by unlockers; L40 ExportedTemplateCategoryMap template ids and the use of
ExportedTemplateSide; L41 unassigned / locality-only functions.
