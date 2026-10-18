# Instanced games (site games and story tools)

Build sites can host games: a deathmatch, team zone control, or a cooperative adventure built from story tools. The
client ships the game definitions and the UI; the live server ran the games. This page collects what is decoded.

## Client data (the client's `Resources/*.txt` tables, in the server's game data folder)

| Table | What it holds |
|---|---|
| IgRuleSets | 8 rule sets: 2 Deathmatch, 3 Team Deathmatch, 4 Traitors!, 5 All Scoring, 6 Team Zone Control, 7 FFA Zone Control Deathmatch, 8 Adventure (default team count, FFA team, players per team, observers) |
| IgRules | rule set → rule types it uses (player adjustable or not) |
| IgRuleTypes | 21 rule types (21 GmPointAward; others unnamed, identified by their parameters) |
| IgRuleTypeParameters | per rule type: Time Limit (1), Points Per Kill / Penalty Per Death (2), team kill penalties (3), self kill (4), environment death (5), kill assist (6), heal assist (7), Point Limit (8), Team Point Limit (9), capture points / score tick / spawner / capture time (11), Respawn Delay (12, 13), movement permissions (17: jump, climb, multi jump, glide, sprint, grapple), Start / Exit Respawn Delay (18, 19), Code Test (21) |
| IgRuleParameters | per rule: override of a parameter's default |
| IgGeneralSetting | per rule set: general settings (team count, players, observers…) with defaults and ranges |
| IgPropRoles / IgPropRoleProps | props with a role in a rule type: 1224 (type 11, zone control point), 1223 / 1367 (type 12, team spawn points), 1371 (types 13, 18, 19: starting / respawn / exit location), 1436 (type 16) |
| IgRuleSetPlacementProps | the controller prop that creates a game of a rule set: 1346 → 2 Deathmatch, 1347 → 3 Team Deathmatch, 1226 → 6 Team Zone Control, 2313 → 8 Adventure |
| EditableProperties | properties a game prop exposes in the property editor (timer duration / autorestart, dice weights, flinger angle / speed, moving platform speed, NPC spawner faction, teleport enable…) |

Story tool items: 3090 Logic Filter, 3127 Travel Point, 3131 Story Node, 3132 Dice Roll Filter, 3137 Timer Filter,
3790 Starting Location, 4388 Damage Node, 4389 Focus Node, 4390 Reset Node (help pages HelpPages/ScriptObj_*_FTE.html).

## Packets (InstancedGameBase 0x10D, u32 sub; client ClientInstancedGameManager, handler FUN_14343c3d0)

The base is two bytes: u16 0x010D on the wire (`0D 01`), then the u32 sub. The client's switch is on sub - 2.

Server → client:

| Sub | Packet | Layout | Client use |
|---|---|---|---|
| 2 | AdminMonitor | u32, list, i32, i32 (FUN_143427160) | |
| 3 | Scoreboard | teams {u32 id, name {u32 text id, string}, f64 values, bool}, players {u32 team, u64 guid, name {u32, string}, f64 values, bool}, u64 list, 3 string lists (unused, team columns, player columns), i32 winning team (-1 none), bool (FUN_143427ea0) | FUN_143434580 → GameData.InstancedGameScoreboard (PvPScoreboard window). Values are doubles. Player columns become Param1..n with the names as ScoreboardHeader; the window sorts by Param1. In a free-for-all (one team, winner 0) the best "Score" names the winner |
| 4 | PropRecipe | u64 game, bool, bool, list {u32, u32, string name, u32, u32, u32, u64 prop guid, bool, u32, bool}, bool open (FUN_143427a10) | HandleGameRecipePacket FUN_143438b30: the game's props for the instance property editor; game none closes it, the last bool opens it (GameEvents:OnOpenInstancedGameSettings) |
| 6 | State | u32 state, u32 next state, u32 ms, u64 game (FUN_143427fe0) | FUN_143438fe0 → FUN_14343ff60: 3/4/6 show the game HUD, 4 plays InstancedGame.CountDown.SoundId, 6 InstancedGame.StartMatch.SoundId, 7 opens the end-of-game scoreboard, 0/1/2/9 put the HUD away. State 4 with next 6 starts the countdown clock (ms) |
| 7 | Event | u32 type, i32 team, i32 other team, u64 actor, u64 other actor, u32 text id (FUN_143427430) | FUN_143437440: kill feed line and sound. 1 enemy kill, 2 team kill, 3 self kill, 4 environment kill, 7 flag taken, 8 flag captured, 9 control point taken |
| 8 | Request | i16 type, i16 result, i16 value, i16, i16, u64 game, u32, string (FUN_143427cf0) | FUN_143438d00: type 6 result 1 value 1 = in the game (manager +0x38 = game: Leave button), value 0 = out (FUN_143432090); 5 queue position (value, +0x2c, +0x30); 4 left the queue |
| 9 | HUD | entry, 6 lists, u32 state, u32 (FUN_14342ba40 → FUN_143427640) | FUN_143432bd0 → GameData.InstancedGameScoreHUD. Entry (FUN_1434284b0): u32 text id, string label, u32 ×7 (+0x24, +0x28, +0x2c, +0x34, +0x30, +0x3c, +0x38). The first entry's label heads the HUD; list 1 = round timers (+0x3c = end time on the client's synced clock, ms: the server's Synchronization clock), 2 wave progress, 3 control indicators, 4 progress bars (FUN_143428640 entries), 5 and 6 unknown. The state picks the message line: 3 "Waiting for players...", 4 the countdown, 6 nothing (or "Observer Mode"), 8 "Game over! Resetting in N seconds" with N = 1 + last u32 / 1000 |
| 0xD | GetConfigurationResponse | | |
| 0x11 | PropState (0.695: 0x12) | bool clear all, u32 n × {u32 0, u32 0, string label, u64 prop, i32 controller, u64 controlling player} (0.695 handler FUN_1434e8ea0) | The client's overhead game-object indicator (HudIndicators row 22) over each control point, labelled with its holder: controller −1 "Not Controlled", 0 the controlling player's name, n ≥ 1 team n's name |
| 0x12 | PropInteraction (0.695: 0x13) | u64 player, u64 prop, u32 start, i32 duration ms (FUN_143427900; 0.695 reader FUN_1434d7570, handler FUN_1434e8d70) | A capture bar over the prop, filled (client clock − start) / duration in the player's team colour (FUN_1434f2c10); duration 0 removes it. The start is on the client's own clock |
| 0x13 | PlayerRemoval (0.695: 0x14) | u64 game (0.695 reader FUN_1434dbfb0) | FUN_1434e8cd0: the full manager reset (as Request type 6 value 0) and the observer camera (mode 0x20) off. Sent after Membership 0 + State 0 when the server takes a player out of a game, and after ObserverRegistration 0 + State 0 when a game ends under its observers (L176) |
| 0x14 | TeamMembers | {u64 guid, u32, string name, u32 team} (FUN_14342bf40 / FUN_143428170) | FUN_1434390e0: team map (manager +0x158); team -1 = observer |
| 0x16 | UpdatePlayerFlags | u64, u8 (FUN_143428230) | |

Client → server: 8 Request (FUN_14343e590 / e680 / e760 / e840 / eb90; types 1 GainAuthorship (Lua
Claims.RequestGainActiveAuthorshipOfGame), 2 LoseAuthorship, 3 Join (Claims.JoinInstancedGame), 7 Leave
(Claims.LeaveInstancedGame, also InstancedGameRequestLeavePacket), 8 Respawn (Claims.RespawnInstancedGame; the game is
manager +0x38)), 0xA PropRequest (InstancedGame.AddAdditionalProp), 0xB PropRemove (InstancedGame.RemoveProp), 0xE
SetConfigurationRequest (ApplyGameSettings / ResetGameSettings), 0xF StartRequest, 0x10 EndRequest, 0x15
ObserverRegistration (sent when leaving: u32 1, ...), 5 PropRecipeRequest.

## Site record and walk-on window

The site's game rides in the claim record (FUN_142d01c40 → struct FUN_142cffe50 at record +0x1e8, copied to the client's
site entry +0x140 by FUN_1434868f0) and in ClaimInfoUpdate kind 0x11: u64 game (0 = none), u32 state, bool can join,
u32 queue size, u32 min players, u32 current players, u32 difficulty, u32 max players, bool, name {u32 text id, string
used when not empty}, restriction tags {u32, string}.

The walk-on window (ClaimWalkOnTextWindow, data source ClaimDialogs.WalkOnText, filled by FUN_142d2b3f0) shows it:
InstancedGame (state not 0), InstancedGameGuid, GameName ("– Difficulty Tier n" when difficulty is not 0),
InstancedGameStatus (FUN_142d1d3b0: 1 Configured, 2 Idle, 3 Started, 4 WaitingForReady, 5 Ready, 6 Running, 7 Victory,
8 Finishing, 9 Restarting), Current/Min/MaxPlayers (players needed when current < min), InstancedGameQueuePosition and
the buttons: CanJoin = not in a game (or queued) and the site's can-join flag and not queued for it; CanLeave = in a
game and not queued; observer buttons from the team map.

## Server (Game/InstancedGames.cs)

- A Game Table (IgRuleSetPlacementProps) on a build site makes the site host a game of its rule set; games follow the
  placed tables (checked every 2 s), nothing is stored.
- Rules come from the tables: IgRules lists a set's rule types, IgRuleTypeParameters their parameters (defaults), and
  IgRuleParameters overrides them per rule in the same order as the type's parameters (row ids ascending).
  IgGeneralSetting 2/3/4 = min players, max players, countdown ms. Deathmatch: 600 s, 20 points, 1 per kill, -1 self
  kill, respawn 6 s, 2-16 players, 30 s countdown.
- Started → WaitingForReady (countdown) → Running → Victory (scoreboard, 10 s) → Finishing (10 s) → Started.
- Weapon hits between players of a running game (FFA: anyone; team games: the other team), kills score, the dead
  respawn at the game's spawn props (1371, team 1223 / 1367; else near the table) after the respawn delay; no death
  window.
- Moving a player is a re-zone. Nothing else for that player may go out until the client has finished it (ClientIsReady
  → Teleport.FinishRezone, then 1 s): hit points, game state, HUD, team list, scoreboard or the site update in the same
  burst as ClientBeginZoning hung the client for good in QuerySimpleVisibility (FUN_144009a40, its visibility list
  looping). A plain TeleportToLocation used to hang it the same way (a zero facing vector, fixed 2026-10-18; terrain.md
  "Long teleports").
- Dev: game, game rules, gamejoin, gameleave, gamestart, gameend, gamescore n, gametp off|on, finderwaiting (below).

## Game Table settings

The Game Table's prop settings window (Prop.SetupObject → PropBase 0x1B, see docs/story-tools.md) lists its rule
set's parameters (IgRuleTypeParameters: NAME_ID, MIN/MAX; yes/no parameters as checkboxes), e.g. Deathmatch: Time
Limit, Point Limit, Points Per Kill, Penalty Per Death, Penalty Per Self Kill, Respawn Delay. Saved values (key
`ig_rule_param_<id>`) replace the rule set's value from the next round (`InstancedGames.RulesFor`). The client's own
game editor (authorship, PropRecipe, InstancePropertyEditor) isn't served.

## Zone control (rule sets 6, 7)

Rule type 11 parameters: 14 Points Per Capture, 15 Points Per Score Tick, 16 Score Tick Interval (s), 23 Time to
Capture (s) (Team Zone Control: 5, 10, 10, 5; FFA Zone Control Deathmatch: 0, 2, 20). Control points are the site's
props 1224 (radius 5 m × scale), named "Point A, B…" (or their custom name). A side (team, or each player in free for
all) standing alone on a point takes it over Time to Capture; the progress fades when nobody stands on it. Taking it
scores Points Per Capture and sends Event 9 (ControlPtTaken, kill feed + sound); every tick each held point scores for
its holder. The HUD's ControlIndicators (ControlIndicator.as: bar height = Progress / 100 in ProgressColor; Owned and
OwnerColor mark the holder) follow it; colours are the client's team colour table (index = side). Dev: `gamepoint`.

Over the points themselves (OPEN-LEADS L32, 0.695 numbers; the sub table above is in 0.589 numbers, protocol.md §1.6):

- **Labels:** PropState names every control point with its holder while a round runs, sent with the HUD only when the
  holders changed for that client, and cleared when the round is over.
- **Capture bars:** PropInteraction shows the side taking a point. The bar's start is written on each client's own
  clock, which the client reports in its KeepAlives (0x63 `{u32 game time}`; `WorldSession.ClientClockNow`). The bar only
  rises, so a point left alone (our progress fades) or contested gets a 0, and a fresh bar, back-dated by the progress
  kept, when a side takes it up again [I: when live sent it].
- Until the server spoke 0.695 natively PropState went out as 0x11 and the 0.695 client dropped it (Frida, 2026-10-17).
  The HUD is re-sent in 20 % capture steps, not every tick (that was ~700 packets a second). Dev `gamelabels off` stops
  both packets.

## Adventure (rule set 8)

Cooperative: no PvP; creature kills score (points per kill); the walk-on window shows "Difficulty Tier n" (general
setting 10). A round puts the site in Play Mode and restarts its story (`Claims.ApplyMode`); the round's end takes it
out again. The Adventure Game Table (2313) is linkable: its action "...Give reward and teleport to a Chaos Cavern."
(text 577090) completes the adventure (everyone wins); after the scoreboard its players leave the game and travel to
a random Chaos Cavern (the underground caverns, `Caves.TravelRandom`). There is no reward beyond the score (no data).
The table's own events "On Game Started" (text 573010) and "On Game Ended" (573011, the Link the developers named on
29 Sep 2016) run its Links when a round starts (after the story restarts) and when it ends (after the result, before
Play Mode goes back off); the round's winner is the triggering character of "On Game Ended" (ours).

## Game editor (the client's RulesetObjectSettings window)

The client has its own editor for a site game: a window titled with the game's name, a **Game Settings** tab
(General Settings and Game Type Settings sections), a **Prop Settings** tab (the props the game needs, each with Add,
Go To or Remove), and Defaults / Apply / Enable-Disable Game buttons (Apply and Defaults only while the game is
disabled). It is driven entirely by the server (Game/InstancedGames.Editor.cs):

| Step | Packets |
|---|---|
| Open | Lua `Claims.RequestGainActiveAuthorshipOfGame` → request type 1 {u64 game}. The client handles no reply to it; the server sends GetConfigurationResponse then PropRecipe with *open* set (dev: `gameedit`). |
| Close | `RequestLoseActiveAuthorshipOfGame` → request type 2; the server sends PropRecipe with game 0 (`GameEvents:OnCloseInstancedGameSettings`). |
| Prop list | **PropRecipe** (sub 4): u64 game (the client's "authored game", manager +0x30), bool enabled, bool has required props, u32 n × item, bool open. Item: u32 id, LocalizedString name {u32 text id, string}, u32 rule index, u32 role, u32 slot, u64 prop guid (0 = not placed: Add button), bool include, u32 team (non-zero adds " - team name"), bool optional (not "required"). The whole list comes with every packet. |
| Settings | **GetConfigurationResponse** (sub 0xD): current block, defaults block, rule parameter definitions, rule parameter values. Block: LocalizedString name (window title), LocalizedString, map {u32 n; string code; value {u32 IgGeneralSetting id, string code, string, string value, u32}}. Definitions: {u32 n; u64 trigger, u32 m; m × {u32 type (1 int, 2 float, 4 checkbox, 5 int), string name, u32 name text id, string, string min, string max}}. Values: {u32 n; u64 trigger, u32 m; m × {string value, u32 rule type, u32 index}}. |
| Apply / Defaults | **SetConfigurationRequest** (sub 0xE): u64 game, LocalizedString ×2, settings map, values map, u8. |
| Enable / Disable | sub 0xF / 0x10 {u64 game}. |
| Add | **PropRequest** (sub 0xA): u64 game, u32 rule index, u32 role, u32 team, u32 slot. |
| Remove | **PropRemove** (sub 0xB): u64 prop, u64 game, u32 rule index, u32 role, u32 slot. |
| Go To | TeleportBase 0xFC sub 8 **TeleportIntraZoneRequest**: f32 x, y, z, w, u64 target, u8. |
| Refresh | sub 5 {u64 game} (Lua `RequestPropRecipeList`). |

Notes:
- General settings come from `IgGeneralSetting` (per rule set: default, PLAYER_ADJUSTABLE, min / max) with the codes
  of `RefIgGeneralSettingsTypes` (InstancedGames_NumberOfTeams, Min/MaxPlayersPerTeam, MaxQueueJoinTime (ms; shown
  in seconds), IsGameFFA, AllowsObservation, Difficulty, PvPAllowed). The client shows only adjustable rows (its
  definition table is these rows, +0x14 = the flag). Min / Max Players sliders take their range from the defaults
  block. Bools are read by their first character (1 / t / T).
- A trigger is one `IgRules` row of the rule set (its id is the u64); index = the parameter's position within its rule
  type in `IgRuleTypeParameters` order. The client hides rule types 8 / 9 index 1 and type 11 index 3 / 4 (control
  point spawner) and fills those itself when applying.
- The prop list's roles: `IgRules` → rule types → `IgPropRoles` (per-team flag, min / max count, name) →
  `IgPropRoleProps` (team t uses the role's t-th prop). Missing required props and one optional "add" slot per role
  are listed with guid 0.
- The map value's last u32 is stored by the client's reader (FUN_1434270b0) over the low half of the code string's
  vtable pointer; its string destructors reset the vtable first, so it's harmless. The server sends 0.
- Saved per Game Table (world_prop_setting): `ig_general_<setting>`, `ig_rule_param_<parameter>` (shared with the
  prop settings window) and `ig_game_disabled`. A disabled game sends its players out and can't be joined. Settings
  apply from the next round (`RulesFor`: player limits, difficulty, observers allowed, PvP).
- Add starts placing the role's prop: from a matching bag item if the player has one, else free (markers have no
  recipe, PropDefs PROP_RECIPE_ID 0, so the editor is how they are placed). Remove is the normal pickup, but a free
  marker gives nothing back (any IgPropRoleProps prop not paid with an item). Go To is only served to a player with
  the editor open, to a point on that game's site.
- The live prop dump names model 1739 for the spawn points, which this client doesn't have: it then creates no entity
  at all (invisible, Go To can't find them). Props.FixMissingModels replaces such ids by the model named like the
  prop's internal name (1699 Theme_Widgets_RuleSetObjects01_SpawnLocation01).
- The 0.589 UI scripts (`UI/ScriptsBase.bin`) never call RequestGainActiveAuthorshipOfGame; the only thing
  that opens the window is the server's PropRecipe. The server opens it when the owner (or a builder) uses the Game
  Table's **Settings** (PropBase 0x1B; the client waits for no reply to it). Dev: `gameedit [off]`.

## Game Finder (0.695 client, Game/InstancedGames.Finder.cs)

- C→S MatchmakingRequest 0x10D / 0x19 `{u64 flag mask (start only), u64 player, u8 searching, u8}` (Lua
  InstancedGame.StartMatchmaking / StopMatchmaking). Flag bits: 2 FFA, 4 Teams, 8 PvE, 0x10 PvP, 0x40 In Progress.
- S→C MatchmakingUpdate 0x10D / 0x1A `{u64, u32 Players, u32 ActiveGames, u32 QueuedGames, u32 InactiveGames, u32
  SearchState, u64 match flags}` (reader FUN_1434d7270, handler FUN_1434e9a50). The leading u64 is stored by the reader
  and read by nothing (we send the player guid). SearchState 0 searching, 1 "Match found!" (clears the client's
  matchmaking flag, plays a sound, prints the match's kind bits), 3 removed from the queue. The client does nothing
  else on a match: the join is the game's queue entry (Request type 5, position 0 = "Join Now").
- Across worlds (shared social service, `SOCIAL_ADDRESS`): each zone publishes its games (with landscape, kind and
  name) and hands searches to the service, which counts every world and names a matching game on another world. If
  that world is on the same gateway, the searcher's zone sends "Match found!" and a queue entry for that game; "Join
  Now" hands the player over to the other world (cross-world travel, HandOver / AttachConnection `JoinGame`), where the
  zone re-zones them beside the Game Table and joins them. "Leave Queue" or 30 s without an answer drops the offer.
  This move is ours: the client's Game Finder packets carry no world.
- What the client proves (0.695 decompile): the update's leading u64 is stored by the reader (packet +0x18) and read by
  nothing (handler FUN_1434e9a50 takes +0x20..+0x38 only); the S→C Request handler FUN_1434e93e0 knows only types 4
  (left queue), 5 (queue entry: guid kept in its own set, entry built from the packet alone) and 6 (membership); the
  entry's Join Now (GaqInstancedGameQueue accept FUN_1433da170 → FUN_1434e11e0) sends Request 0x10D / 8 type 6 value 1
  with the entry's game guid (entry +0x140, stored by FUN_1433daaf0 → ctor FUN_1433d7930) and makes it the client's
  current game; RequestJoinTeam (type 9) has no answer type — TeamMembers and the scoreboard carry the result.
- Afterwards the player stays on the game's world (`FINDER_RETURN_HOME=false`, the default; no source says live
  brought them back, and a match on the player's own world never moves them back either). With
  `FINDER_RETURN_HOME=true` on the searcher's world the hand-over also carries the world, landscape and spot they left
  from (HandOver / AttachConnection `ReturnTo`); 3 s after they leave the game (still on its landscape, not moving)
  the game's world hands them back, and they arrive at that spot (`ArriveAt`). A Chaos Cavern trip after an Adventure,
  travelling on by themselves or logging out cancels the way back; a game gone on arrival sends them back at once.
- Dev `finderwaiting <table guid hex> [off]` / `finderwaiting off` (no player needed; output in the world log): this
  world publishes that game to the shared Game Finder with at least one player in it, so a search on another world is
  matched with it — a cross-world test with one client. The log line lists this world's games and their table guids.

## Status

Working (tested on the 0.589 client; on 0.695 the Game Table settings, teams, join / start / spawn, the HUD's top score
and the end banner were checked): game on the site record and walk-on window, join / leave, state and countdown, HUD
with round timer, scoreboard, kill events, respawn at spawn points, round end with the end-of-game scoreboard.
Adventure games run the site's story tools (see docs/story-tools.md).
The game editor (settings, prop list, enable / disable) works (see above).
Queues: see InstancedGames.Queue.cs (request type 5 queue position {i16 queue size, i16 position (0 = Join Now), i16
wait s, name}, type 4 left the queue; MaxQueueJoinTime to take a freed place).
Not yet: control points as spawners. Winner banner: the client builds it from InstancedGames.VictoryMessage / GameOverMessage / DrawMessage and
InstancedGame.Teams.DefaultName_<id>, none of which this client's StringHashToValue.txt has, so on its own it always
reads "Team 1" (the server does send the result: scoreboard +0xc0 team id 1-based, 0 = draw / free-for-all, -1 =
none). The banner's UI script copies its data source into the banner movie with `Invoke('setText', text)`; the
server does the same 1.2 s after the Victory state (and once more at 2.4 s) through `ClientLua.RunCode` (see
[showcase.md](showcase.md)): "<player> Wins!", "<team> Wins!", "Draw!" or "Game Over".
