# System: activities — missions, quests, tasks / objectives, mini games, lobbies, activity launch, repeating
activities, encounters, matchmaking, leaderboards, quizzes

Status: [~] map level. Most of this is EQ Next / shared code; 0.589 UI reachability must be checked per item (see the
UI inventory: missions / activities had no 0.589 content — MODLOG "UI INVENTORY").

- Missions (0xD1) ✗ all: ListMissions, ConquerZone; S→C SetMissionManager, Add / Remove (global) available mission,
  SetMissionInstanceManager, Add / Remove active mission, ReportCompletedMission. MissionDefinitionManager loads 4
  tables; MissionComponents.txt (8 cols).
- Quests (0x34) ✗ QuestReply / QuestEndReply; S→C QuestInfo / Add / Complete / Failed / Abandoned / Objective* /
  CompletedQuestCount / QuestEnd / QuestSelected. Command QuestAbandon ✔ (no-op).
- Tasks / UI events (0x32): ✔ UIEventTriggered 0x17; ✗ SetWaypoint 0x12, SelectTaskRequest 0x6, SelectQuest 0xB,
  SelectedQuestLocked 0xC; S→C TaskAdd / Update / Complete / Fail, ExecuteScript, StartTimer, ObjectiveTargetUpdate,
  UiMessage, CinematicStartLookAt, WeaponHitFeedback, ActivityProfileList, WaypointCooldown, ZoneWaypoint,
  InteractStart, HeadShotFeedback, LoadingScreen.
- Objectives (0x30) ✗ ObjectiveClientComplete; S→C Activate / Update / Complete / Fail / Unhide / LookAt / Clear /
  CompleteFailed / UIEvent.
- Mini games (0x2A, 0xAD, 0x62) ✗ all (game over, end score, stars, rewards, knock-out, group info, messages, info,
  invite / start). ClientMiniGameManager; MiniGame*.txt server-only.
- Lobbies (0x67 / 0x68) ✗ join / leave / start / create, errors, show lobby UI, definitions.
- Activity launch (0xA8, 18 S→C) ✗ invite / ready / quit / kick / launch / matchmaking; Activity (0xA0): ✔ list of
  activities; ✗ JoinActivityRequest, ScheduledActivity list. Repeating activities (0x8D) ✗ all.
- Encounters (0x2C) ✔ (docs/encounters.md, duels). Matchmaking (0x8A) ✗ queues / invitations.
- Leaderboards (0xC6 / 0xCA) ✗; Quiz (0xAF) ✗ (QuizDefinitions / Results server-only).
- Area timer (0xA5) ✗ ClientAreaTimer; flash / race timers (0xAA) ✗.

## Live behaviour — Story Tools / Linking and Triggering (wiki, launch 2016; server: docs/story-tools.md)
Link: right-click an interactive prop / light → Create Link → pick event ("When this door opens…") then the target's
action ("Light this candle"). Areas: Area Tool box (V creates) linked to props. Play Mode (Play/Pause icon) runs
the story; switching Edit → Play saves the story's starting state; story tools are invisible in Play Mode (except
Scoreboard). Most nodes cost 50 Cut Plain Wood, innate, tintable. Per-node **settings** (the server currently uses
fixed defaults — gap):
- **Story Node**: links to creatures or text-only. Settings: weapon visual (Equipped / Sheathed), mood (Angry /
  Calm / Happy / Sad), stance (Dancing, Drunken, Kneeling, Lying Down, Skipping, Sneaking, Standing, Sitting), emote
  (Beckon … Yell, 29), "Announce emotes in chat", text location (On Screen / In Chat / In Chat Bubble / Bubble +
  Chat), text duration 1-20 s, rich text (bold / italic / underline, 20 colours). In: activate everything / text /
  mood / stance / emote / weapon visuals. Out: when everything / text activates, text expires, stance / emote /
  weapon visuals activate.
- **Combat Node**: one creature; tactics Standard / Stationary / Do Nothing; once linked, lists the creature's
  abilities (Ability 1..n) as link actions ("perform ability").
- **Counting Node** / **Scoreboard** (visible display): start value and max 0-99; in: +1, -1, set max, set 0; out:
  reaches max, reaches 0, increases, decreases.
- **Damage Node**: % of max health, "Heal Instead"; targets a creature or an Area (creatures, Luminaries or both).
- **Focus Node**: creatures look at the node, or at "what activated this"; in: activate / deactivate.
- **Logic Filter**: blue + yellow lights; in: on / off / alternate each or both; out: blue / yellow / either / both
  turn on / off (fires only on a change).
- **Probability Filter** ("dice"): Odds of Winning %; out: wins / loses.
- **Random Link Filter**: any number of "potential actions", one chosen at random (equal odds).
- **Reset Node**: in Play Mode resets every prop and creature to the saved state.
- **Respawn Point**: many per site; Enabled checkbox; facing arrow; in: activate / deactivate; out: activates,
  deactivates, Luminary respawns here. "Flee" revives free at the nearest active respawn point / Starting Location
  (cannot be disabled); "Revive Here" costs 15 Lumens.
- **Starting Location**: exactly one per site, free, placed by default in the site's north-west corner; where
  visitors arrive (teleport) and revive; out: Luminary teleports to this site / respawns here.
- **Timer Filter**: duration 1-600 s, "Loop Until Paused"; in: start / pause / reset / pause+reset; out: completes /
  starts / pauses / resets.
- **Server Time Node**: up to 6 listeners at whole game hours; out: time n reached (+ a buggy "clicked").
- **Travel Point**: creatures walk here; in from creatures or other travel points; out: creature arrives (patrols).
- **Creature Props** (need Monster Essence, some elemental essences): Settings Name, Reaction (Enemy / Neutral /
  Ally), Combat Tactics (Standard / Stationary / Do Nothing), Spawn Automatically, Respawn After Killed, respawn time
  30-300 s. Creatures act only in Play Mode.
- Combined with creatures: killing a creature can collapse voxels (heal / destroy voxels), closing a door spawns a
  creature, etc.

## Deep trace — Instanced games (rebuild module 14, 2026-10-14; archive instancedgames-v1)
Packets: every reachable InstancedGame request handled (REACHABLE-GAPS). Rules come from the client's Ig* tables
(docs/instanced-games.md). Parameter by parameter (IgRuleTypeParameters, names from the text table):
- Applied: Time Limit (1), Points Per Kill (2), Penalty Per Death (3), Penalty Per Self Kill (6), Penalty Per
  Environment Death (7), Point Limit (10), Team Point Limit (11), Points Per Capture (14), Points Per Score Tick (15),
  Score Tick Interval (16), Respawn Delay (19 / 20), Time to Capture (23); general settings min / max players,
  countdown, observers, PvP, difficulty.
- Not applied: Penalty Per Team Kill (4) / Friendly Fire Death (5) — team games never let teammates hurt each other,
  so they cannot occur; Points Per Kill / Heal Assist (8 / 9) — needs per-hit damage credit (Health keeps only the
  last attacker); Control Point is Spawner / Spawn Delay (21 / 22); Start / Exit Respawn Delay (31 / 32); movement
  permissions (rule type 17: Allow Jumping / Climbing / Multi Jump / Gliding / Sprinting / Grappling Hook, all default
  True) — no client code reading them was found (movement flags would have to come from the server), so they stay
  at their defaults. Code Test (34) is a dev parameter.
