# Score HUD progress bars and the scoreboard (0x10D sub 9 / sub 3), 0.589 vs 0.695

TASKS 3.24 / 3.27. Sources: re/decomp_all (0.589) and re/decomp_all_695, re/out/agent_ui/hud_695.c (handler copy),
re/as3_695/HUDScoreboardWidget/.../ProgressBarsDisplay.as, Resources/Scripts/global.lua 221-270 (0.695 test copy),
string constants read from Landmark64.exe 0.695 (pointer table at 0x145261040..0x145261150).
**Proven** = read in the decompile / AS / Lua. **Inferred** = our choice where the client does not decide.

## 1. HUD body (sub 9) — proven

| | 0.589 | 0.695 |
|---|---|---|
| packet reader | FUN_14342ba40 (u16, u32 sub, body at packet +0x18) | FUN_1434dbe10 (same) |
| body reader | FUN_143427640 | FUN_1434d7120 |
| handler | FUN_143432bd0 | FUN_1434e2f70 (`ClientInstancedGameManager::FillDataSourceInstancedGameHUD`) |

Wire order of the body (struct offsets 0.589 / 0.695):

| # | field | 0.589 | 0.695 | reader |
|---|---|---|---|---|
| 1 | round counter entry (HUD title) | +0x00 | +0x00 | entry (below) |
| 2 | round timer entries | +0x40 | +0x50 | FUN_143429d50 / FUN_1434d9a50 |
| 3 | wave progress entries | +0x58 | +0x68 | same |
| 4 | control indicator entries | +0x70 | +0x80 | same |
| 5 | **progress-bar groups** | +0x88 | +0x98 | FUN_143429ea0 → FUN_143428640 / FUN_1434d9bb0 → FUN_1434d8360 |
| 6 | entry list (not read by the HUD handler) | +0xa0 | +0xb0 | FUN_143429e00 / FUN_1434d9b10 |
| 7 | entry list (not read by the HUD handler) | +0xb8 | +0xc8 | same |
| 8 | u32 state (message line) | +0xe0 | +0xf0 | |
| 9 | u32 ms (state 8 "Resetting in N") | +0xe4 | +0xf4 | |

### HUD entry — proven
0.589 FUN_1434284b0 (0x40 bytes), 0.695 FUN_1434d8180 (0x50 bytes). Wire order → offset:

| wire | offset | meaning |
|---|---|---|
| u32 | +0x00 | text id |
| string | +0x08 | label (length at +0x18) |
| u32 | +0x24 | — |
| u32 | +0x28 | — |
| u32 | +0x2c | — |
| i32 | +0x34 | value: bar value, control progress % |
| i32 | +0x30 | colour index / team (bar `Position`, `Color`) |
| u32 | +0x3c | round timer end (synced ms clock); control point "owned" |
| i32 | +0x38 | control point holder colour |
| bool | +0x40 | **0.695 only**: team entry |
| u64 | +0x48 | **0.695 only**: player guid |

Label text (FUN_143c1c7b0, both builds): the string if its length is non-zero, else the text id via the text
provider, else `"Missing Text"`.

### Progress-bar group — proven
Same in both builds (0x40 bytes): `u32 text id (+0x00), string label (+0x08), bool (+0x20), i32 total (+0x24),
entry list (+0x28: data +0x30, count +0x38)`.

### Handler (0.695 FUN_1434e2f70, hud_695.c 659-948) — proven
For g = 0..4 (five data sources `InstancedGameScoreHUD.ProgressBars.ProgressBar%d`, string at 0x14478ed98; groups
beyond the count are cleared):
- Row 0: `Name` "LabelTotal", `Label` = group label (FUN_143c1c7b0), `Type` = "Time" if bool +0x20 else "Int",
  `Value` = total +0x24.
- Then over the group's entries, with `team` = manager +0xa0 (the client's own team) and `self guid` =
  BaseClient(DAT_1454311c8) +0x32508:
  - team entry (+0x40 true) is **own** when +0x30 == team; player entry (+0x40 false) is own when +0x48 == self
    guid; both only while team != -1.
  - non-own entries: keep the best by +0x34 ("best other").
  - team == -1 (observers, before the first scoreboard): single pass keeping a max (marked IsSelf) and a second;
    correct only for a best-first list (an increase with no second yet drops the old max).
- Up to two `ProgressBar` rows: `Color` = palette[+0x30 % 11] (-1 → "FFFFFF"), `Value` = +0x34, `Position` = +0x30,
  `IsSelf` 1 for the own entry / 0 for the other, `Label` = FUN_143c1c7b0(entry). Row 1 = the higher value (own
  entry wins ties), row 2 = the other; with no own entry the other goes to row 2.
- Palette (0x145261100, 11 entries): AF7FAF, 0034f8, ff0000, D4AB1F, 00cc00, FF7F7F, 7F7FFF, 7FFF7F, FFFF7F, FF7FFF,
  7FFFFF. Control indicators use the same palette for +0x30 / +0x38. (0.589: the same table taken % 13.)

0.589 handler (hud_589.c 670-860): one row per entry **in wire order**, `Position` = +0x30, `IsSelf` = (+0x30 ==
manager +0xa0); no `Label`.

### ProgressBarsDisplay.as — proven
Header text = row 0 `Label`, amount text = row 0 `Value` (as a clock when `Type` == "Time"). Bars 1 and 2 = rows 1
and 2: `maximum` = row 0 `Value` (the total), value = row `Value` capped at the maximum, text = `Value`, name text =
`Label` (0.589: `Position`), arrow at the bar with `IsSelf` == "1", colour from `Color` (RRGGBB). Bar 2 hidden when
row 2 is missing (0.695). A total of 0 gives a zero-width scale: the server never sends 0.

## 2. Scoreboard (sub 3) — proven
Reader 0.589 FUN_143427ea0 (0.695 same body); handler 0.589 FUN_143434580, 0.695 FUN_1434e4a40
(`FillDataSourceInstancedGameScoreboard`). Packet: teams +0x18 (count +0x28), players +0x30 (data +0x38, count
+0x40; entry 0x50: u32 team +0, u64 guid +8, name +0x10, values +0x38/+0x40, bool), u64 list +0x60, string lists
+0x78, +0x90 (team columns), +0xa8 (player columns), i32 result +0xc0, **bool +0xc4**.

- **bool +0xc4 = rebuild.** false: only updates rows that exist (player rows found by guid: changed `Param<n>`;
  team rows found by index: `IsWinner`, `TeamScore`). true: clears and rebuilds the header, `ScoreboardBanner`,
  team rows (+ `Joinable`) and player rows. The server used to send false only, so the client never had rows.
- **Team rows skip list index 0** (both paths: `if (iVar17 != 0)`), and IsWinner = (result == row index). Team list
  slot 0 is the "FFA Team" (CodeStringMappings `InstancedGame.Teams.DefaultName_0` 564622 "FFA Team", _1 564623
  "Team Cobalt", _2 564624 "Team Gold"); team games number their teams from 1. One team in the list = free for all
  (`local_343`): result 0 → banner from the best "Score" player (manager +0xd0), else GameOverMessage / DrawMessage.
  Banner texts: `InstancedGames.VictoryMessage.Text` 564695 "[*winner*] wins!", GameOverMessage 564696 "Game Over!",
  DrawMessage 564697 "Draw!" (or the game setting `InstancedGames_VictoryMessageTeam<n>`).
- **The client's own team** (manager +0xa0): set once, while it is -1, to the `team` of the player entry with the
  local guid — only if a player column is named "Score" (case-insensitive). Reset to -1 only by FUN_1434e2330
  (PlayerRemoval 0x14 → FUN_1434e8cd0, and membership-out in the Request handler FUN_1434e93e0). Read only by the
  HUD bars (FUN_1434e2f70) and the scoreboard handler. So a scoreboard must reach the client before the HUD for
  IsSelf to work.

## 3. State 7 and Tab (global.lua) — proven
- State switch FUN_1434f2420: entering 7 calls Lua `OpenEndGameScoreboard` (opens ScoreboardWinnerBannerWindow, text /
  colour from `GameData.InstancedGameScoreboard.ScoreboardBanner` row 0 Name / Value, else "Something went
  wrong."); leaving 7 for any state calls `CloseEndGameScoreboard`; 3 / 4 / 6 open the score HUD, 0 / 1 / 2 / 9
  close it; 8 changes nothing (HUD stays, message line "Game over! Resetting in N").
- `OpenPvPScoreboard` (Tab): needs `InstancedGame.IsInGame()` (player or observer), HUD state normal, and the banner
  not visible. `ClosePvPScoreboard` does nothing while the banner is visible.
- Server: final scoreboard (result, rebuild) → state 7 (banner) → 10 s → state 8 (banner closes, Tab works again)
  → 10 s → state 3. Leaving / stopping observing sends state 0 (banner and HUD close). Matches.

## 4. What the server sends (LandmarkEmulator.WorldServer/Game/InstancedGames.cs) — inferred where noted
- One bar group while a competitive round is on and through states 7 / 8 (not in Adventures): label "Score Limit"
  with the rule set's TeamPointLimit / PointLimit, or "Top Score" with the best score (at least 1) when there is no
  limit (inferred: the client only needs a non-zero total).
- Team games: every team, best first: label = team name, value = team score, +0x30 = team + 1, team entry true.
- Free for all: leader and runner-up, plus the viewer when neither: label = character name, value = score, +0x30 =
  the player's slot (same colour as the control points they hold; inferred), player entry with the guid.
- 0.589 sessions: no tail; at most two entries (leader, then own entry or runner-up) since 0.589 shows rows in order.
- Team ids on the wire are team + 1 in the scoreboard (team list slot 0 empty, players' team, result) and the HUD
  (bars, control indicator colours). Kill-feed events and TeamMembers still send the server's 0-based team (open).
- The HUD is re-sent only when what the bars show changes (state, goal, scores; FFA also the roster), after the
  scoreboard; scoreboard before HUD on join, arrival and observing.

## 5. Kill feed and TeamMembers use the same team numbers — proven (2026-10-16)
- **Event** (sub 7, handler FUN_1434e7900): team +0x1c / other team +0x20 → `sourceTeam` / `targetTeam` names via
  FUN_1434e7570 (game setting `InstancedGames_TeamName<id>`, else `InstancedGame.Teams.DefaultName_<id>`, else
  "Team %d") and `sourceColor` / `targetColor` = palette[id % 11] (-1 FFFFFF). Same ids as the scoreboard team list.
- **TeamMembers** (server sub 0x14 = 0.695 wire 0x15; reader FUN_1434dc380 → FUN_1434d7e10 {u64 guid +0, u32 +8,
  string +0x10, u32 team +0x28}; handler FUN_1434e97c0 → map manager +0x2b8 by guid; own entry with team != -1
  ends observer mode). Team +0x28 is read by:
  - FUN_142ea9630 (hostility): team 0 → hostile to everyone only if game setting `InstancedGames_PvPAllowed`
    (FUN_1434ec610); else hostile = different team.
  - FUN_142ea8f90 (friendliness): team 0 → normal rules; else same team.
  - FUN_1434e7280 (ARGB if-chain 0 AF7FAF, 1 0034f8, 2 ff0000, 3 D4AB1F, 4 00cc00; caller FUN_14336cef0),
    FUN_1434e7380 (palette string; caller FUN_143604330), FUN_1434f2c10 (same if-chain).
  - FUN_1434e7420 → walk-on window FUN_142dc9660 (-1 = not on a team; its site-game team list also skips index 0).
  So 0 = free for all, teams count from 1. The server now sends team + 1 in team games in both packets (and in the
  control-point-taken event); free for all stays 0. Server team names follow DefaultName_1 / _2 (0.695 en_us:
  "Team Cobalt", "Team Gold"), else "Team <id>".
- Not changed: PropRecipe item team (InstancedGames.Editor.cs) is rule data the client echoes back, not a live team.
