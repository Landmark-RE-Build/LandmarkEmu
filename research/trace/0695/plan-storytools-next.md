# Story tools, Play Mode and site games: what to build next (TASKS §2)

Written 2026-10-15 (read-only research; nothing in LandmarkEmu, the installs or Docker was changed). Inputs:
TASKS.md §2, storytools-695.md, packets-695.md, OPEN-LEADS.md (L18-L23, L30), ui-trace-695.md, functions-695.md,
launch-rules-audit.md, the server (Game/StoryTools.cs = ST, PropSettings.cs = PS, Props.cs, Claims.cs, Graveyards.cs,
Health.cs, RightClickMenus.cs, FirstTimeEvents.cs, DevCommands.cs), gameData695, the 0.695 decompile
(re/decomp_all_695), the 0.695 AS3 export (re/as3_695), the 0.695 HelpPages and the live captures in
LandmarkEmu/LandmarkEmulator.WorldServer/Resources/PacketDumps.

Addresses are 0.695 functions unless marked 0.589. **Proven** = read in the client code / data / a live capture;
**inferred** = a choice the sources support but do not state (kept or added as an open lead).

Line numbers are today's (StoryTools.cs has moved since storytools-695.md was written: e.g. AddBinding is now
ST:1003, the dice odds ST:388-392, Reset ST:626-630 / 912-924, the area test ST:377-382).

---

## 0. New facts found for this plan (proven)

1. **The server's prop-settings window is unreadable for 0.695 clients today.** PS:224-235 writes f32 values; the
   0.695 EditSettingsReply reader FUN_142fa85d0 (= 0.589 FUN_142f098a0) reads its property list with FUN_142c364f0 and
   its drop-down list with FUN_142c36fe0, both through the record reader FUN_142c15ee0: str label, u32 label text id,
   str key, **five f64** (+0x38 value, +0x40 default, +0x48 max, +0x50 min, +0x58 step), u32 help text id. Each
   drop-down option (FUN_142c391d0 → FUN_142c1eae0) is u32 text id, u32 value, bool, **str label**. Every 0.695 settings
   window (timer, dice, damage, spawner, Story Node...) is misread from the first value on.
2. **Settings changes from the 0.695 client are still ints.** The four senders FUN_142faf030 (slider, `(int)float`),
   FUN_142faf1b0 (checkbox), FUN_142faf3a0 (drop-down int), FUN_142faf490 (float → `(int)`) all call FUN_1430ac840(prop,
   2, key, state) with an int state value: PS:241-272 needs no change.
3. **Prop setting state → visuals (S→C 0xFD type 2).** FUN_1430ac970 stores state.value and calls the
   ClientPropSettingBehavior (vtable 0x144779f08) slot +8 (thunk 0x143476bf0) or +0x10 (0x143476b30: `mov r9,[rsp+0x28];
   jmp 0x1434777a0`) — both end in FUN_1434777a0 with the state pointer, so the "previous" field and the trailing bool
   do not matter. FUN_1434777a0 passes **r8d = state.value** to FUN_1430aa110 (lights) and FUN_1430a9260 (digits)
   (disassembly 0x143477813-0x14347782b). FUN_1430aa110 picks the *off* composite when the value is 0, *on* otherwise.
4. **L30 is answered: how the question id comes back.** DisplayPropText u64 #1 → FUN_142f859b0 param_6 →
   FUN_142ebfcb0 stores it at actor +0x1280 (with the question bool at +0x127c) → FUN_142ece660 calls FUN_1434445a0(...,
   actor +0x1280) when +0x127c is set → the NpcDialogResponse indicator (vtable 0x144773808, ctor FUN_143436d30) keeps it
   at indicator[0x30] → slot 21 FUN_143440d90 calls the clicked Lua function (HudIndicators row 29: STRINGPARAM3
   `Prop.OnConfirmNpcDialog` / STRINGPARAM4 `Prop.OnDenyNpcDialog`) with arg 1 = indicator[0x30], arg 2 =
   indicator[3] → FUN_1435fbf80 / FUN_1435fc030 send PropBase 0xED / 0x1D {u64 arg 1, bool}. The server's response id
   comes back unchanged. Lua u64 arguments are decimal strings (FUN_143c9f460 → FUN_142c0c650), so any u64 survives.
   The bubble (and so the question) exists only for display modes 4 / 5 and a speaker entity the client knows
   (FUN_142f859b0: `1 < mode - 4` skips it; entity looked up by guid in the client's actor table).
5. **Models of every 0.695-only story prop are in the live prop capture** (Resources/PacketDumps/145-PropBase, PropBase
   ListReply, loaded by Props.LoadDefinitions): 2940 → 1727 CombatNode01, 2901 → 1728 CountingNode01, 3000 → 1736
   Scoreboard01, 2659 → 1737 ServerTimeNode, 2657 → 1738 RandomNode02, 2701 → 1739 SpawnLocation01 ("scripting:
   Respawn Location"), 2976 → 1741 IFNode01, 3011 → 1744 PointsNode01 ("prop: games: Score Node"), 3010 → 1745
   VictoryNode01, 2945 → 1659 TimerNode01 ("scripting: hour node"), 3012 / 3013 → 1739. All these model rows exist in
   gameData695/Models.txt, so FixMissingModels keeps them as they are (TASKS 2.28 needs no code; L21's "Points Node =
   Score Node" is proven by the same capture).
6. **Which item places which story prop** (live item capture, gameData695/items.json `u35_39[3]`): 1331 "Respawn
   Point / Respawn at this point." → **1225** (RespawnMarker01: keep it as today's open-land respawn point); **4031 and
   4509 → 2701** (the site Respawn Location); 3770 → 2657, 3772 → 2659, 4408 → 2940, 4409 → 2901, 4508 → 3000, 4524 →
   2976, 3090 → 2325, 3131 → 2344, 3137 → 2367, 4388 → 2899. This answers the coordinator note on 2.24.
7. **Probability Filter shows one number.** Both help pages (0.589 Steam HelpPages/ScriptObj_Random_FTE.html "Odds of
   Winning: The percent chance of a dice roll to 'win'"; 0.695 ScriptObj_Probability_FTE.html "This number is the
   probability that a random dice roll will succeed. It defaults to 50%") → odds = row 15 / 100; row 16 is not a window
   setting.
8. **Lua natives usable by tests without mouse / keyboard** (luabind_695.tsv; args are decimal strings):
   `Prop.SetupObject(guid)` FUN_1435faf00 → EditSettingsRequest 0x1B; `Prop.SetupScript(guid)` FUN_1435fafc0 → Links
   0x0F; `Prop.OnConfirmNpcDialog(id, speaker)` / `Prop.OnDenyNpcDialog` → 0xED/0x1D; `Prop.OpenHelpWindow(guid)`
   FUN_1435fbc80; `Claims.SetClaimAdventureFlag(0, bit, on)` FUN_1435e6470 → 0xF9/0x45; `Claims.ToggleAdventureStatus`;
   `Prop.SetTextPropData(guid, text)` → 0xED/0x13; `PropertyEditor.SetPropertyValue(dsRoot, rowGuid, value)`
   FUN_1435ba360 → 0xFD (needs the window's data-source name and row guid). Server side: `luas <Table.Fn> a b`,
   `luacode <lua>` (ClientLua.RunWithArgs / RunCode).

---

## 1. Every open item of TASKS §2

| Item | Status | What decides it |
|---|---|---|
| 2.1 Play Mode Rules | **READY** (store, send, record); voxel-destruction bit **BLOCKED** | Spec 10. Bits 1/2/8/0x10/0x20 are enforced by the client once the mask arrives (functions-695 §4); bit 4 needs the server's destruction path + EffectEnd result 9 (FUN_142e18c70) and the site destruction layer (terrain service) |
| 2.2 Counting Node | **READY** | Spec 7 (with 2.19); behaviour at the limits inferred (L23) |
| 2.3 Scoreboard | **READY** (= Counting Node); Play Mode visibility **NEEDS TRACE** (L20) | Spec 7 |
| 2.4 Logic Filter overhaul | **READY** via 2.19 + 2.32 | ids unchanged (link_texts_695.tsv); lights = spec 5, silent reset = spec 14 |
| 2.5 Story Node additions | split: 2.21 / 2.22 / 2.23 / text-expires **READY**; bubble interrupt (14 Jul) client-side, verify only | specs 15-17 |
| 2.6 Linking Area destruction | **BLOCKED** | site destruction layer with timed heal back to player-set voxels (terrain service hand-off; HealQueue.cs keeps site voxels out today) + row 102's "blocked" option text id (L18) |
| 2.7 Combat Node | **BLOCKED** (runtime); vocabulary + single binding **READY** (specs 3, 4) | no creature ability model (Creatures.cs has no per-creature ability list or cast); tactics need AI modes; ability rows L19 |
| 2.8 Server Time Node | **READY** | Spec 9 (row labels inferred from the help page, L19) |
| 2.9 Random Link Filter | **READY** | Spec 6 |
| 2.10 Respawn Location | **READY** (= 2.24) | Spec 8 |
| 2.11 IF Gate | **NEEDS TRACE** (L19: comparison set and labels, triggering-character option labels); runtime READY behind it | only "Equal To" is proven (P6496); the 0.695 option label is a free string (FUN_142c1eae0) |
| 2.12 Game Table rework, Game Finder, Score / Victory Node | **NEEDS TRACE / BLOCKED** | L21 (who owns 577665-577692, "On Game Ended"), L31, L32, Game Finder in the social service (Phase B); out of this plan |
| 2.13 "When clicked" on all props | **READY** (recycle half already built: Props → ST:322-326) | Spec 12; which props become interactable is inferred |
| 2.14 Creature health / damage | **BLOCKED** | L19 (no EditableProperties rows; only text 577652 "Damage % Multiplier") + creature stat scaling |
| 2.15 Fiery Fungus / Proximity Mine terrain damage | **BLOCKED** | site destruction layer (as 2.6) + creature abilities |
| 2.16 Older fixes | **mostly DONE by data**, verify; Damage Node area targets → 2.25 | row 14 step 1 and ALLOW_USER_RENAME come from gameData695 (PS:100, Props.cs:995); timer completes / resets built (ST:521-545, 877-884) |
| 2.17 Build-gated vocabulary | **READY** | Spec 3 |
| 2.18 0.695 settings window | **READY** (proven, §0.1-0.2) — first to build | Spec 1 |
| 2.19 Prop state broadcast | **READY** (proven, §0.3) | Spec 5 |
| 2.20 AddBinding result codes | **READY** | Spec 4 |
| 2.21 Story text 0.695 | **READY** | Spec 16 |
| 2.22 Question bubbles | **READY** (proven, §0.4); needs 6.33's bubble routing | Spec 17 |
| 2.23 Story Node lists / rows | Text Location 4 options + text-expires **READY**; Mood / Stance / Emote animation ids **NEEDS TRACE**; Weapon Visuals **BLOCKED** (NPC equip packet); Announce Emotes inferred | Spec 15 |
| 2.24 Respawn Point 2701 | **READY** (proven, §0.6) | Spec 8 |
| 2.25 Linking Area 0.695 actions | **READY** except Destroy / Heal Voxels (BLOCKED, 2.6); parse semantics inferred (L23) | Spec 13 |
| 2.26 Damage Node Heal Instead / Luminaries | **READY** | Spec 13 |
| 2.27 Help | **READY** (button proven; FTE moment inferred) | Spec 2 |
| 2.28 Models | **DONE by data** (proven, §0.5); verify in the client | Spec 0 (test only) |
| 2.29 Spawner rows 9 / 17 / 40 / 105 | row 9 **READY**; 17 **NEEDS TRACE** (L18 meaning of 0-2); 40 **BLOCKED** (NPC equip + L18); 105 **BLOCKED** (2.15) | Spec 11 |
| 2.30 Probability odds | **READY** (proven, §0.7) | Spec 1b |
| 2.31 IF-capable runtime properties | **READY** (data model) | Spec 7 / 8 add the readers; consumer is 2.11 |
| 2.32 Snapshot and silent reset | **READY**; the 577089 message route stays L22 | Spec 14 |
| 2.33 Linking Area shape | **NEEDS TRACE** | does the client's area tool save a non-uniform scale for prop 1590? Also: the server stores only Scale.X (Props.cs:1518, restored uniform at 1563), so a box could not survive a restart today |
| 2.34 Triggering character | **READY** (refactor) | Spec 6a |
| 2.36 Per-prop runtimes | Counting / Scoreboard, Random, Server Time **READY**; IF **NEEDS TRACE** (L19); Combat **BLOCKED** | specs 6-9 |

---

## 2. Specs, in build order (smallest and most-blocking first)

Common test setup (0.695 test copy, no input): `TESTGAME='<0.695 test copy>' MATCH=test copies bash re/dlogin.sh tester2 2`
(TestPlayer2), then dev commands in dev_commands.txt, each prefixed `@TestPlayer2`:
`tpclaim`, `playmode off`, `clearprops`, `prop <propId> dx dy dz` (log: `dev prop N placed as 0x<HEX>`). Guids for
Lua are decimal (`printf %d 0x<HEX>`). Visual checks: background screenshot of the test client (`um win shot --hwnd`
or re/winshot.ps1, PrintWindow, no focus). Client errors: <0.695 test copy>\Logs. The 0.589 Frida harness (re/test/
client.js `addlink`, `settings`) has 0.589 addresses only, so two small dev commands are proposed in spec 3 for Links.
Say when a test build restarts the dev world (memory: only-695-server-runs).

### Spec 0 — 2.28 Models (verify only)
- No code. Test: `prop 2940 2 0 0`, `prop 2976 4 0 0`, `prop 2901 6 0 0`, `prop 3000 8 0 0`, `prop 2659 -2 0 0`,
  `prop 2657 -4 0 0`, `prop 2701 -6 0 0`; screenshot: seven widgets visible; server log has no "still missing" for
  them (Props.cs:474). Pass = the client created entities (they also answer `luas Prop.SetupScript <guid>` with a 0x0F
  request in the server log).

### Spec 1 — 2.18 Settings window in the 0.695 layout (proven, §0.1)
- **PS.SendSettings / WriteRecord** (PS:178-235): pass `bool v695 = session.Speaks695`.
  Record: `str label, u32 labelTextId, str key,` then v695 ? 5 × **f64** : 5 × f32 (value, default, max, min, step),
  `u32 helpTextId`. Example 0.695 Timer row 14 (value 60, default 60, max 600, min 1, step 1):
  `00 00 00 00 00 00 4e 40 | 00 00 00 00 00 00 4e 40 | 00 00 00 00 00 c0 82 40 | 00 00 00 00 00 00 f0 3f | 00 00 00 00
  00 00 f0 3f`.
  Drop-down option (PS:209-215): `u32 textId, u32 value, bool isDefault` + v695 ? `str label` (the option's text, or a
  server label for rows without a text id — §0.1; how the window chooses between id and string is not traced, see
  test).
  Header and tail unchanged (`u8 0x1C, u64 player, u64 prop, bool ok, u32 n {i32 key, record}, u32 n {i32 key, record,
  u32 m options}, str name, str text`).
- **Rows per kind** (PS:79-93), read from the build's own EditableProperties (gameData695 already has 46 rows):
  extend `Definition` with columns 10 BLOCKS_USER_MODIFICATION, 11 IF_NODE_CAPABLE, 12 IF_DISPLAY_STRING_ID (PS:100-101).
  Never offer state rows 107 / 108 / 110 / 111 or rows with BLOCKS_USER_MODIFICATION = 1 as editable; refuse 0xFD on
  them (PS:257-262). Row 31's key is `npc_spawner_ai_capability` in 0.695 (PS:45 `SpawnerAi` is unused, drop it).
- **Spec 1b — 2.30 Probability Filter** (proven §0.7): `settingsByKind[DiceRollProp] = { 15 }`;
  `DiceOdds` (ST:388-392) → `Math.Clamp(Get(guid, DiceWeightA), 0, 100) / 100f`. Existing saved b values are ignored.
- Test (input-free): `prop 2367 0 0 -3` → `luas Prop.SetupObject <timer dec>` → log `opened the settings of prop
  0x…: 2 setting(s)`; screenshot shows Duration 60 and Loop Until Paused off (today: garbage / no window). `propset <hex>
  prop_timer_player_set_duration 5`, reopen: 5. Dice: `prop 2345 …`, open: one row "Odds of Winning 50". Story Node
  (drop-downs): `prop 2344 …`, open: the Mood / Stance / Emote / Text Location lists render with labels (proves the
  option string is read). Client write path (int, §0.2): if the data-source name and row guid of the open window are
  read once (`luacode` dumping the data source), `luas PropertyEditor.SetPropertyValue <ds> <row> 7` must log `set
  prop_timer_player_set_duration = 7`.

### Spec 2 — 2.27 Help button and story first-time events
- **RightClickMenus.PropMenu** (RightClickMenus.cs:91-104): add `(74u, 11u)` (RadialMenuButtons row 74: menu 1, BUTTON
  11, text 577204, `Prop.OpenHelpWindow`) when the prop id is in Resources/PropHelpWindowMapping.txt (0.695: row 1 →
  window 1 → prop 2899). Load the mapping from the data dir (add PropHelpWindowMapping.txt and PropHelpWindow.txt to
  gameData695: they are 0.695 client tables not yet copied). 0.589 sessions: skip (no row 74 in the 0.589 table).
  Proven: RadialMenuButtons row, PropHelpWindow(Mapping) rows, FUN_1435fbc80 opens the page itself.
- **First-time events** (FirstTimeEvents.cs:38-75): add ids 28 Logic, 29 Path (Travel Point), 46 Probability (0.695;
  30 in 0.589), 32 Text, 33 Timer, 39 Damage, 40 Focus, 41 Reset, 44 Respawn, 45 Combat, 47 Server Time with pages
  `ScriptObj_<Logic|Path|Probability|Text|Timer|Damage|Focus|Reset|RespawnLoc|Combat|Servertime>_FTE`, "Large",
  "CenterR" (the one placement the client names, PropHelpWindow row 1). Trigger them in Props.Save next to
  `Achievements.PropPlaced` (Props.cs:815) for prop ids 2325, 2330, 2345, 2344, 2367, 2899, 2898, 2900, 2701, 2940,
  2659. **Inferred**: the moment (first placement) — no client caller exists (no `UIEventTriggered` for them in
  as3_695); keep it in L23.
- Test: `prop 2899 …`; `luas Prop.OpenHelpWindow <dec>` → screenshot shows the Damage Node help (proves the client
  side); right-click list: log of RadialMenuResponse contains 74. `fte clear`, then `give` item 3137 and place the Timer
  through the server path (`prop 2367 …` does not go through Save; use `luas Prop.StartPlaceNewObject` only if it can
  be finished without input — otherwise mark the FTE check for the user in TESTING-NEEDED.md).

### Spec 3 — 2.17 0.695 vocabulary (kinds) and gating
- **ST kinds** (ST:170-235): add, as text ids (all proven texts, owners from wording + patch notes + help pages as in
  storytools-695 §4):
  - Counting Node 2901 and Scoreboard 3000: events 557290 max, 565055 zero, 577247 increases, 577092 decreases; actions
    577248 +1, 577249 −1, 577250 set max, 577251 set 0.
  - Random Link Filter 2657: events 577591; actions 577593.
  - Server Time Node 2659 (and 2945 Hour Node, same kind, not placeable): events 577252, 577255-577259 — only the first
    `npc_time_count` of them (ST.KindOf is per prop id; make the event list per prop: `EventsOf(guid)`).
  - Respawn Point 2701: events 577528, 577529, 574557; actions 577530, 577531.
  - Combat Node 2940: events 577162 (choose), 577245; actions 577242, plus 577213-577216 / 577243 / 577244 only once a
    577162 Link exists (P6151) — runtime BLOCKED (2.7), so offer only 577162 / 577242 / 577245 now.
  - IF Gate 2976: events 577629, 577626, 577625; action 577627 (runtime behind 2.11).
  - Spawners (+577163 link to combat node), Linking Area (+577221, 577228, 577723 actions, +577722 event; 577548 /
    577549 wait for 2.6), Story Node (+577246, 577724, 577725 events; 577238 / 577239 wait for the equip packet).
  - Any prop with an IF-capable row (33, 103, 107, 108, 110, 111) also gets action 577628 "...Link this prop to the IF
    condition" (with 2.11).
- **Gating**: a `Only695` flag on these kinds / ids; `session.Speaks695 == false` → 0x0C omits 0.695-only props,
  0x0D-0x0F answer only 0.589 ids, Add refuses 0.695 ids (result 1). The reused ids 557290 / 565055 / 577092 /
  577213-577216 are therefore never shown to 0.589 (they read "MAX" / "Mine" / "Decrement Count" there). With the dev
  world on gameData695 this is cheap insurance, not a 0.589 server.
- **Test tooling** (proposed dev commands, DevCommands.cs): `storylink <src hex> <event> <dst hex> <action>` (calls
  `StoryTools.Add` as the target player, logs the result code) and `storylinks <hex>` (lists). Test: `prop 2901 …`,
  `luas Prop.SetupScript <dec>` → log `opened Links on prop … (counting node)`; screenshot shows "When this node's count
  reaches its maximum..." etc.

### Spec 4 — 2.20 AddBinding result codes (codes proven: FUN_142f46ed0 cases 1-5, FUN_142fee7e0 result 4 → 577233)
- ST.Add (ST:1074-1120) returns a code instead of a model: 0 ok; 1 not editable / not in vocabulary / caps (ST:1077-1103);
  **3** when the same Link exists (today ST:1095-1096 returns the old Link = "ok"); **4** when `eventId` ∈ {577162,
  577629} and the source already has a Link from that event (single binding). Code 2 (invalid cycle) stays unused:
  live's cycle rule is unknown (keep the depth cap ST:370).
- Wire unchanged: sub 5 {u64 src, u64 dst, u32 event, u32 action, **u32 result**} (ST:1004) and sub 11 (ST:1061-1064).
- Test: `storylink` twice with the same args → second logs result 3; Combat Node 577162 → spawner 577163 twice with two
  spawners → second result 4 (client message test: needs the client's own add, i.e. the user, TESTING-NEEDED).

### Spec 5 — 2.19 Prop state broadcast, type 2 (proven, §0.3)
- New `PropSettings.SendState(IEnumerable<WorldSession> viewers, ulong guid, string key, int value)` (or in
  PropEffects), body as PropEffects.Body (PropEffects.cs:216-232) with type **2**:
  `u64 guid, u32 2, i32 KeyHash(key), u32 value, u32 0xFFFFFFFF, u64 0, u32 0 (ints), u32 0 (strings), u8 0`.
  Keys: 107 `script_logic_gate_a` → hash 0x687b558b (blue), 108 `script_logic_gate_b` → 0xde034081 (yellow), 33
  `prop_component_counter_current` → 0x75e90645 (digits |v| % 100). Only to `Speaks695` viewers (0.589 FUN_1433c9790
  only stores it — harmless but useless).
- **When**: Play start (each Logic Filter both lights 0; each counter its start), every change (ST.SetCogs ST:710-726,
  counter ops), Play stop / reset (back to Edit values), and late viewers: in Props.SendSpawns (Props.cs:1335-1353)
  append the current state for story props of sites in Play Mode (never during a re-zone: `RezoneTarget == null`, as
  ST:696-707). **Coalesce**: keep `pendingState[(guid,key)] = value`, flush once per StoryTools.Update tick, one body per
  prop/key shared by all viewers; viewers = zone players within 100 m (ours; same idea as FireworksRange ST:222).
- Edit Mode (inferred, L20): send counters their Starting Count when spawned so builders see the number; lights off.
- Test: Logic Filter: `prop 2325 0 0 -3`, `prop 1599 -3 0 0` (switch), `storylink <switch> 576427 <filter> 572961`,
  `playmode on`, `storyclick <switch>` → log `state 0x687b558b = 1 → 1 viewer(s)`; screenshot: blue bulb popped out.
  `story` shows `lights=True/False`.

### Spec 6 — 2.9 Random Link Filter (+ 6a, 2.34 triggering character)
- ST.Execute: `case 577593`: take `LinksFrom(r.Guid, 577591)`, keep those whose target is running, pick one with
  `Random.Shared.Next(n)`, `Execute(site, target, link.ActionId, ctx, depth + 1)`. No Links → nothing. Proven: P6167,
  help page ScriptObj_Random_FTE ("one link each time will be chosen at random").
- **6a. Triggering character (2.34)**: extend `Context` (ST:358) with `ulong Trigger` (+ `bool TriggerIsPlayer`), set
  where a chain starts: Click (ST:836) player; ClickCreature (ST:860) player (P6538); Linking Area enters (ST:892, 904)
  that player / creature (P6537); creature spawns / dies / arrives / combat (ST:732, 781, 796, 815) the creature
  (dies: keep the killer as `Player`); Starting Location arrival (ST:770) player; timers / Server Time (ST:883) none.
  Carry it unchanged through `Fire` / `Execute` (records `with` already preserve it).
- Test: Random: `prop 2657`, two torches, two `storylink <random> 577591 <torchN> 576363`, a switch → `storylink
  <switch> 576427 <random> 577593`; `playmode on`; `storyclick` ×10; `story` → exactly one torch toggles each time
  (log `random filter … chose link N`).

### Spec 7 — 2.2 / 2.3 / 2.36 Counting Node and Scoreboard (+ 2.31 readers)
- Running gets `int Count`, `int Max`. Play start: `Count = Clamp(Get(guid,"prop_component_counter_current"), 0, Max)`,
  `Max = Clamp(Get(guid,"prop_component_counter_max"), 0, 99)` (rows 33 / 35, 0.695 ranges 0-99, defaults 0 / 10).
- Actions: 577248 `Count+1`, 577249 `Count−1`, 577250 `Count=Max`, 577251 `Count=0`, clamped to [0, Max]. If the value
  changed: fire 577247 (up) or 577092 (down); then 557290 if `Count == Max`, 565055 if `Count == 0`; broadcast (spec 5).
  No change (e.g. +1 at Max) → no event (**inferred**, same "only real changes fire" rule as the Logic Filter P5780 /
  P5819; L23 stays open).
- Window rows: `settingsByKind[2901] = settingsByKind[3000] = { 33, 35 }` (row 34 has no label: hidden).
- 2.31: `StoryTools.ReadProperty(guid, key)` → int: 33 → Count; 107 / 108 → Cog1 / Cog2; 103 → Enabled (spec 8); 111 →
  spawner `Creature != 0`; 110 → Control Point team (with 2.12).
- Scoreboard visibility (P6182 visible in Play Mode, Counting Node hidden P5960): mechanism not found (L20) — no server
  action until traced.
- Test: `prop 2901 0 0 -3`, `propset <hex> prop_component_counter_max 3`, switch → `storylink <sw> 576427 <cnt>
  577248`, torch → `storylink <cnt> 557290 <torch> 576363`; `playmode on`; `storyclick` ×3 → log `count 3 (max)`, torch
  off (`story`), screenshot shows "03" on the disc; 4th click → count stays 3, no event. Scoreboard 3000 same.

### Spec 8 — 2.24 / 2.10 Respawn Point 2701 (proven §0.6)
- Running: `bool Enabled` = `Get(guid, "respawn_point_enabled")` (row 103, default 1) at Play start. Actions 577530 /
  577531 set it; on a real change fire 577528 / 577529. Window rows `settingsByKind[2701] = { 103 }`.
- **Graveyards.SitesFor** (Graveyards.cs:31-47): keep `siteProps` {1225, 1518, 2108} (1225 = item 1331's open-land
  "Respawn Point", proven); add 2701 points **only while their site is in Play Mode and Enabled**, as site entries
  (ClaimId = their site, name "Respawn Point" text 577587). In Edit Mode a site keeps today's rule (Starting Location /
  nearest site entry). **Graveyards.Nearest** already picks the nearest entry of the site the player died on
  (Graveyards.cs:57-63); P6177 + help page ("Luminaries who flee will be sent to the closest active point") match.
- Arrival event: Health.HandleRespawn (Health.cs:505-511) calls `StoryTools.ExpectArrival(session, claim, respawn:
  true)` only for "Build Site"; extend `Site` with `Guid` use: when the chosen site is a 2701 point, expect arrival
  **at that prop** and fire its 574557 only (wiki: only at the location used); Starting Location keeps 574557 when it
  was the one used. Facing = prop rotation (wiki; Teleport.ToZone has no heading today → note, not blocking).
- Enabled look (P6178) not found yet (L20): no packet.
- Test: `prop 2701 10 0 0` (A) and `prop 2701 -10 0 0` (B), `propset <B> respawn_point_enabled 0`, `playmode on`, stand
  near B, `hurt 100000` (death), then `respawn 11` (the death window's Flee, Health.HandleRespawn) → log
  `respawned (type 11) at Respawn Point` and position ≈ A. `storyfire <B> …` / switch Link 577530 → B enabled → repeat
  → lands at B; `storylink <B> 574557 <torch> 576363` → torch toggles only when B was used.

### Spec 9 — 2.8 Server Time Node 2659
- Rows: 101 `npc_time_count` (1-6, default 1) + server rows `npc_time_1` … `npc_time_6` (0-23, step 1, default 0),
  label string "Desired Time N", label text id 0, help id 0; only the first `npc_time_count` rows are sent; new rows
  appear on reopen after the count changes (wiki). **Inferred**: labels / keys (help page says "Desired Time ... using
  24hr clock"; L19 stays open for live's exact rows). Accept 0xFD for these keys by matching `KeyHash("npc_time_N")`.
- Runtime: per site keep `lastHour = (int)(WorldClock.SecondsOfDay / 3600)`; in StoryTools.Update, when the hour
  changes to h, fire `577252` (slot 1) / `577255+N-2` (slots 2-6) for every listener N ≤ count with `npc_time_N == h`.
  One fire per hour change; game time, not the site's time (P6165). Context: no triggering character.
- Test: `prop 2659 …`, `propset <hex> npc_time_count 2`, `propset <hex> npc_time_1 14`, torch Link from 577252,
  `playmode on`, `timeofday 13.99` → within a tick of 14:00 log `server time slot 1 (14)`, torch toggles; `luas
  Prop.SetupObject <dec>` + screenshot: two Desired Time rows.

### Spec 10 — 2.1 Play Mode Rules (store and send; bit 4 blocked)
- **Data**: `ClaimModel.AdventureFlags` (ulong, default 0 = no blocks, no extra cameras: defaults from the notes —
  destruction on, flying camera off), EF migration like 20261024090000_ClaimPlayMode.cs.
- **C→S**: 0.695 Claims 0xF9 / 0x45 arrives as internal sub `ClientWire695.Internal(Claims, 0x45)` = 0xC5 (as
  StartClaimPlacementSub, Claims.cs:208): `{u64 claim, u64 mask, u8 1}` (FUN_142da6bd0; Lua claim arg 0 = current claim,
  L55). Accept from the owner (OwnedClaim, as SetAdventureStatus Claims.cs:1511-1515; **inferred**: live may also have
  allowed builders with manage permission); keep only bits 0x3F.
- **S→C**: ClaimInfoUpdate sub 5 kind **0x17** to the zone (FUN_142da2840 trailing u64 +0x288 → claim entry +0x380):
  write the mask in Claims.WriteInfoUpdate (Claims.cs:1097-1098) instead of `0ul`, and in the claim record
  (Claims.cs:915-916). Add `InfoAdventureFlags = 0x17` to Claims.cs:990.
- **Enforcement**: client-side for bits 1, 2, 8, 0x10, 0x20 (FUN_142e93540, FUN_14370f590, FUN_142dd5540, FUN_143607950,
  FUN_1435e4530). Bit 4: the server must refuse destruction on Play Mode sites and answer Abilities EffectEnd result 9
  (FUN_142e18c70) — needs the destruction trace + site layer → BLOCKED (with 2.6).
- Test: stand on own site, `luas Claims.SetClaimAdventureFlag 0 1 1` → log `site … adventure flags 0x1`; `playmode on`;
  `luacode` printing the `Claim.AdventureFlags` data source rows to chat (FUN_142dd2480: Value per bit) must show
  Block Grappling Hook = 1 — the client's own state, set from kind 0x17. The grapple refusal itself
  ("Grapple.AdventureBlocked") needs a real grapple: leave it to the user (TESTING-NEEDED.md). Relog → the claim record
  carries 0x1.

### Spec 11 — 2.29 Spawner row 9 (Spawn Automatically)
- `settingsByKind["spawner"] = { 9, 18, 19, 31 }`; ST.PlayModeChanged (ST:433-434) spawns only spawners with
  `Get(guid, "player_npc_spawn_when_npcs_turn_on") != 0` (default 1). Proven: text 573007 "Spawn this creature
  immediately when Play Mode is activated. If off, the creature must be spawned with a Link."
- Test: two spawners, one `propset … player_npc_spawn_when_npcs_turn_on 0`; `playmode on`; `story` → one creature.

### Spec 12 — 2.13 "When clicked" on every prop
- KindOf (ST:240-262): every placed prop gets event 576311 (P6432, text 576311 exists in both builds); props with no
  other kind get a `clickOnly` kind {Events = [576311]}. ST.Click (ST:825-841): for non-toggle kinds only fire 576311.
- Interactable flagging (Props.cs:1348, 1385): **inferred** rule to avoid a click cursor on every fence: a prop is
  usable when it has at least one Link from 576311 and its site is in Play Mode; send `DynamicInteractionFlagging` for
  such props when Play starts and when a 576311 Link is added (as Props.SetText does, Props.cs:1022-1028).
- Test: `prop <any chair>`, `storylink <chair> 576311 <torch> 576363`, `playmode on`, `storyclick <chair>` → torch
  toggles.

### Spec 13 — 2.26 / 2.25 Damage Node and Linking Area damage
- `DamagePercent(area)` (ST:397, fixed 50) → the chain's Damage Node: `ctx.DamageNode != 0 ? Get(node,
  "node_damage_value") : 50` (row 30). Heal Instead = `Get(node, "node_damage_in_healing") != 0` (row 32, never read
  today).
- Linking Area actions (ST:631-636): 577148 creatures, 577221 Luminaries + creatures, 577228 Luminaries: players =
  `PlayersOn(site).Where(InArea)`, amount = `ceil(Health.MaxHealth × pct / 100)` → `Health.Damage` or `Health.Heal`
  (Health.cs:113, 216); creatures → `Creatures.DamageStory` (Creatures.cs:1503) or a new `HealStory` (cap at max).
  Creatures hurt by a Damage Node drop no loot (P5768; `Kill(c, null)` already has no killer — verify no drop).
- Spawner action 577068 (ST:649-651) honours Heal Instead too.
- 577723 "...parse every entity in this area..." → for each player / story creature inside, fire 577722 with that entity
  as triggering character (**inferred** from the wording, L23).
- P6192: a story creature despawning inside an area fires 573164 (call the area check from DespawnStory /
  Creature death before `r.Creature = 0`).
- Test: area + Damage Node (`propset … node_damage_value 30`, `… node_damage_in_healing 0`), switch → node 565554, node
  577070 → area 577228; stand inside, `storyclick` → log `TestPlayer2 −30 %`, client HP bar drops (Hitpoints packet);
  heal flag 1 → HP rises.

### Spec 14 — 2.32 Snapshot and silent reset (+2.4 Reset of Logic Filters)
- At Edit → Play (ST.PlayModeChanged): after building `site.Props`, store `site.Snapshot[guid] = (On, Locked, Cog1, Cog2,
  Count, Enabled, TimerLeft)`.
- Reset (ST:626-630 queue; ST:912-924 today = stop + start): replace by `ResetSilently(site)`: despawn story creatures
  (no loot, as Stop), stop timers, restore every Running field from the snapshot and send visuals (ShowOn ST:691-708,
  state broadcast spec 5) **without Fire**; Logic Filters back to both off (P5975); question ids cleared; then spawn
  Spawn-Automatically spawners as on Play start (their "spawns" Links fire — today's behaviour; L23).
- 577089 "The story on this Build Site has been reset!" (CodeStringMappings Claim.StoryReset.Message): route not found
  (L22); if used, `SystemMessages`/sysmsg by text id to players on the site is **inferred** — keep it behind a flag.
- Test: torch lit at Play start, switch → torch off; switch → Reset Node 577085; `storyclick` → `story`: torch on, no
  `linked …` fire lines between `story reset` and the end of the tick.

### Spec 15 — 2.23 Story Node rows (partial)
- PS.DropDowns[TextDisplayType] (PS:63): `(576481, 4) In Chat Bubble, (576480, 2) On Screen, (576482, 3) In Chat,
  (577170, 5) In Chat Bubble and Chat`; 0.695 default is row 25 VALUE_DEFAULT 5 (gameData695). Modes proven
  (FUN_142f859b0). Window rows for 2344: `{ 25, 37, 24, 20, 21, 22, 112 }`.
- Text-expires 577246: when a node's text is shown (6.33 routing), schedule `Fire(node, 577246)` after `Text Duration`
  (row 37) seconds, per speaker; ignore if the site left Play Mode (P5964, P6191).
- **NEEDS TRACE**: AnimationSelection ids for the 0.695 lists (4 moods incl. Sad, 8 stances incl. Lying Down / Sitting,
  29 emotes incl. Beckon, Cough, Flex, Hug, Sneeze, Stretch, Welcome, Yawn, Yell, Talk continuous / stop): Emotes.cs
  maps only the player words; trace the NPC animation networks' Emote_Index / Stance_Index variables
  (AnimationSelectionVariables.txt, the creature .mrn networks; method as trace/systems/social.md). Option *values* are
  the server's own (the client echoes them; L18 only matters for fidelity).
- **BLOCKED**: Weapon Visuals (41, 577237-577239) and spawner row 40 — NPC equip / sheath packet.
- Announce Emotes in Chat (24): **inferred** = also post the emote's chat line (player emote text) when the creature
  emotes; keep off by default (row default 0).

### Spec 16 — 2.21 Story text in 0.695 (proven: LargeTextInput.as 27, 106, 151, 239-259)
- Props.SetText (Props.cs:1009-1028): for Story Nodes / signs keep the tokenised string; count visible characters after
  removing `~[~…~]~` tags and turning `~a~`/`~q~` into one char each: ≤ 512 (cut at 512 visible, keep tags balanced by
  dropping a cut tag). Allowed tags (Flash htmlText after tokenizeText): `P`, `FONT` with only `COLOR="#rrggbb"`, `B`,
  `I`, `U` and their closers; any other `~[~…~]~` is removed; raw `<`/`>` cannot occur (tokenised). Byte cap 4096 as a
  hard guard. 0.589 viewers (if any): strip tags to plain text in DisplayPropText and chat.
- Pass-through: DisplayPropText and Chat.NpcSay send the stored text; the 0.695 SpeechBubbleWidget / Chat /
  NotificationWindow detokenise (SpeechBubbleWidget.as 90-95, ChatDocumentClass.as 331-336).
- Test: `luas Prop.SetTextPropData <node dec> ~[~B~]~Hello~[~/B~]~` → stored as sent; `propshowtext <hex> 4` →
  screenshot bold bubble; a 600-visible-char text is cut to 512.

### Spec 17 — 2.22 Question bubbles (proven §0.4; after 6.33)
- Depends on TASKS 6.33 (route Story Node text by Text Location through DisplayPropText with the creature or node as
  speaker) — today Perform says the text with Chat.NpcSay (ST:674-676).
- Send: when row 112 `prop_text_is_question` is 1 and the mode is 4 or 5, DisplayPropText 0.695 =
  `u64 responseId, u64 speakerGuid, str text, u32 mode, u32 seconds, u8 1` (Props.WritePropText Props.cs:122-134 gains
  `responseId, question`). `responseId` = a per-question counter (u64, never 0) mapped to (site, node guid, speaker,
  expiry = Duration + 60 s).
- Receive: PropHandler (PropHandler.cs:26-94) `case 0x1D`: `{u64 id, u8 accepted}` → `StoryTools.DialogResponse(
  session, id, accepted)`: if the mapping exists and the site is still in Play Mode, fire 577724 (accepted) or 577725
  on the Story Node with Context(Player = responder, Trigger = responder) (P6539); drop the mapping (one answer per
  question per player: keep a set of responders).
- Test: node with text and row 112 = 1 (`propset`), spawner speaker, `playmode on`, `storyfire <node> 576254` (or
  switch) → log `question 7 → TestPlayer2`; screenshot shows Yes / No at the bubble; `luas Prop.OnConfirmNpcDialog 7 <speaker
  dec>` → log `0xED/0x1D id 7 accepted` and the 577724 Link runs (torch toggles). `OnDenyNpcDialog` → 577725.

---

## 3. NEEDS TRACE — precise next reads

| Item | Read |
|---|---|
| 2.11 IF Gate | L19: settings window builder FUN_142fb71f0 for special-cased keys / drop-down handling; design/forum/all/2016-09-29-57990-feedback-game-tables.md and 2016-10-10-58022 for screenshots' wording; runtime capture impossible (no live server) → if nothing, build with "Equal To" + our labels and record as inferred |
| 2.23 lists | AnimationSelectionVariables.txt Emote_Index / Stance_Index rows; the NPC .mrn networks (as trace/systems/social.md did for the player network) |
| 2.29 row 17 | L18: meaning of Reaction 0-2 (`prop_npc_spawner_faction_select`) — 0.589 decompile readers of the key hash, wiki Creature Spawner page, forum story-tools feedback (2016-05-04-57064) |
| 2.33 area shape | client area tool: who places prop 1590 (StringHashToValue `GameScripting.PropNpcDefinitionId`; the string is not in the decompile, look up the StringHashToValue table row and its readers), and whether its PropSave carries a non-uniform scale vec4; server side store Scale as a vector (Props.cs:1504-1518 / 1560-1563) |
| 2.3 visibility | L20: callers of FUN_142d99b70 ("PlayMode") and the prop-hide path in Play Mode |
| 2.12 | L21, L31, L32 (separate plan) |

## 4. BLOCKED — on what

| Item | Blocked on |
|---|---|
| 2.6, 2.15, 2.1 bit 4, 2.25 Destroy / Heal Voxels, 2.29 row 105 | site destruction layer with timed heal to player-set voxels (terrain service / Environment Server, Phase B #3); HealQueue.cs explicitly keeps build-site voxels out today |
| 2.7 Combat Node runtime | creature ability model (per-creature ability list, cast on demand, tactics = AI modes) |
| 2.14 | L19 (rows / labels) + creature stat scaling |
| 2.23 Weapon Visuals, 2.29 row 40 | NPC equipment / sheath packet (not story-specific) |

## 5. Open-lead updates to make (coordinator)

- L30 → Found (§0.4).
- L21: Score Node 3011 = model 1744 "Points Node" (§0.5, live capture).
- Coordinator note on 2.24 → answered (§0.6): 1331 places 1225; 4031 / 4509 place 2701.
- New lead: the 0.695 settings window's choice between the record's label string and its label text id (needed for
  server-only rows; check with spec 9's screenshot).
