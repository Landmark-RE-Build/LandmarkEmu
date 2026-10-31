# Emotes in the 0.695 client: the full path (2026-10-21)

Why emotes with a held tool looked wrong ("a short animation before it, then my speed is set to walk, some animations
are just short"). Sources: the 0.695 decompile (re/decomp_all_695), PlayerAnimationNetwork64.mrn from Assets_153.pack
(re/out/l117/mrn.py), gameData695 AnimationSelection*.txt, and read-only Frida traces on the user's own game
(re/test/emote_trace.js, emote_trace2.js; logs re/test/emote_trace*.log) with dev command `emotelead`.
[P] = proven (code, data or runtime), [I] = inferred.

## 1. How an emote reaches the character

1. The player types a word the zone registered (CommandBase 0x44 list). The client sends Command 0x43; the server
   (Game/Emotes.cs) answers with PlayerUpdate 0x26/0x65 RequestAnimationSelection {u64 entity, u32 selection, bool clear
   sit, u8 seated} to the player and everyone in range, plus the chat lines. /sit is the client's own command. [P]
2. Client handler case 100 (FUN_142cdfa60, part_0065 ~20752): finds the entity (FUN_142f85390), clear-sit →
   FUN_142ec9840, seated byte → entity +0xa39, then **FUN_142dfc010(anim system, entity, selection, 0)**. [P]
3. FUN_142dfc010: FUN_142ec3800 (selection side effects, stores the selection at entity +0x95d*8 when a sound/effect
   row exists), then **FUN_143d95d40** (the animation system's play) — its bool is the result. [P]
4. If it returns false the handler queues it: **FUN_142e977b0** → list at entity +0x93a. The per-frame character update
   (FUN_142eb1950, part_0074) retries each queued selection once a frame and **drops it after 6 failures** (~70 ms). [P]
5. AnimationSelection.txt rows: emote words are "EmoteSingle" rows (MESSAGE = the network request) with variables
   Emote_Index n, EmoteLoop 0; "talking" is an "EmoteLoop" row (EmoteLoop 1) ended by 253 (EmoteLoop 0, Emote_Index 0);
   stances are variable-only rows (Stance_Index; 223 stand = Stance_Index 0, BreatheSpeed 1); moods set Emote_Emotion.
   Every emote row has STOPS_CURRENT_SELECTION 1. [P]

## 2. What the client plays by itself

- Every frame, FUN_142eb1950 plays **471 {CombatWalk 0}** (or 470 {CombatWalk 1} while entity flag +0xa20 bit 0x1000
  is set; ids from ClientConfig hashes 0x629334de / 0x1dc3b6b3). Trace: ~96 calls/s for the player, always 471, never
  470 during the emote tests — so the "walk" is **not** CombatWalk. [P]
- On every EquipmentSet the client plays selection 0 from 0x142eacb82 (fails, harmless). [P]

## 3. The animation network: where emotes live

PlayerAnimationNetwork64.mrn (0.695, 15,303 nodes):
- **StandPoses** (node 10128) is a state machine with two states [P names; transitions named A_B]:
  - `StandPoses|Idle_Stand_2` (10129): a switch on **WeaponIdle** — child 0 `Stand` (empty hands: idles, every emote
    clip `Emotes_SingleShot` / `Emotes` / mood `Emotion_Switch`, every stance `Stances|StanceSwitch`), child 1 `Weapon1`
    (220 nodes, tool / weapon idles only, **no emote**).
  - `StandPoses|WeaponSwitchCombo|Weapon1Enter` (14756…): the **weapon switch** — per tool class an Enter (take out) and
    a SwitchOut (put away) clip: PickEnter / PickSwitchOut, AxeEnter / AxeSwitchOut, SickleEnter / SickleSwitchOut,
    ShieldEnter, BucketEnter, Pulverizer, Sword, Bow, Staff, …
  - Transitions `Idle_Stand_2_Weapon1Enter*` and `Weapon1Enter_Idle_Stand_*` between them (their conditions read the
    weapon Class; the condition-to-transition mapping of re/out/l117/condowner.py is approximate) [I].
- Locomotion has an **emoting** branch: `Move|Move|GroundMove|Locomotion|Idle|Locomote_Emoting_Locomote_Nohanded` and
  49 `TurnEmote` nodes; `State_Emoting` is a network parameter only (the exe never names it) [P]. So a character that
  moves while an emote plays walks with the emote's upper body until it ends [I].
- A held item's actor (Wield_*.adr) carries AnimationEnumerations WeaponIdle / Class / Weapon (pick 1 / 1001 / 1,
  shield 1 / 1016 / 4; buckets and quivers none) [P].

## 4. What happened with EmoteHolster (server puts the tool away for the emote)

Runtime on the user's game [P]:
- EquipmentUnset → the client removes the model at once (AttachmentProcessor.log) and the character goes through the
  weapon switch (the pick's SwitchOut) — **the "short animation before it"** [I: the state is the only path from Weapon1
  to Stand; the clip name is from the network].
- For ~1.1 s after the unset a selection is taken (true) but never shown — not even a stance — and a second one is
  refused and dropped: cheer at 0.8 / 0.9 / 1.0 / 1.1 s not shown, 1.2 s (twice) / 1.6 / 2.0 s shown. The emote
  request reaches the network while StandPoses is in WeaponSwitchCombo, where no emote state exists.
- With PutAwayLead 1.5 s the emote shows, but:
  - **Short emotes:** the server equips the tool again as soon as the player walks 0.5 m. EquipmentSet sends the network
    into WeaponSwitchCombo (the take-out clip) again, which leaves Idle_Stand_2 and **cuts the emote** [I, from the
    state machine; the server log shows the re-equip within a second of moving].
  - **"Speed set to walk":** moving while the emote still plays takes the emoting locomotion (walk pace) [I]; the 1.5 s
    wait makes this likely (the player is moving by the time it plays). Not CombatWalk (trace), not the movement-speed
    stat (stat 5 never changes with the tool), not the movement flags (0x2000 = strafing, normal).

## 5. What live did [I]

The client never puts a held item away for an emote, and the emote states exist only for empty hands, so on live an
emote with a tool in hand showed nothing until the player put the tool away themselves (hotbar), which played the same
SwitchOut clip. Taking the tool out again was the player's own action, never automatic.

## 6. Open

- Exact transition conditions / durations of StandPoses (decode the transition NodeDefs; L201).
- Clip lengths of each emote (animation set sources), to know when an emote is over.
- Confirm in game: the short animation = the tool's SwitchOut clip; the walk only lasts while the emote plays.
