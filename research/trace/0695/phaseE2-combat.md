# Phase E part 2 — combat, movement, emotes and animation leads (agent "pe2-combat", 2026-10-26)

Work list: L95, L168, L170, L188, L192, L194, L201. Brief: trace/0695/agent-phaseE2-brief.md.
Code: worktree `<workspace>\wt-pe2-combat`, branch `local/pe2-combat` (from client-0.695 5cbb2c2), three commits:

| Commit | What |
|---|---|
| 9226d91 | Off hand: a main-hand item's second model (shield, quiver, left gun / dagger) in slot 11, for players and creatures |
| 813ca1a | No Spire / My Site cooldown (setting `TravelCooldownSeconds`); /yell = the yell emote (setting `YellIsEmote`); /emote left to the client (clip command now /emoteclip); looping emotes remembered |
| de3e79e | Dash Soles / Roll Wraps evades by direction; potion and throw clips; the reviver's ReviveAlly; the sword combo without its dash attack; docs |

`dotnet test LandmarkEmulator.sln`: 1981 total, 1981 passed, 0 failed, 0 skipped. `python tools/wiki_reference.py`
run (wiki/Settings-Reference.md, Commands-Social.md, Command-Documentation.md regenerated). New settings wired like the
others (WorldServerConfiguration, ServerSettings.Table, docker/world/docker-compose.yml, .env.example,
WorldServer.example.json, docs/docker.md, tools/wiki_reference.py).

Marks: [P] proven (client code / data / network file / dev post), [I] inferred, [W] wiki.

Tools made for this (kept): `re/out/pe2_combat/sm.py` — Morpheme state-machine decoder for the 0.695
PlayerAnimationNetwork64.mrn (states, transitions, blend durations, conditions; layout in its header);
`re/out/pe2_combat/dgrep.py` — "functions whose body matches all these regexes" over re/decomp_all_695. The network
was read out of Assets_153.pack read-only into the session scratchpad (not kept: 51 MB).

---

## Findings that several leads share

### F1. The animation network's state machines can now be read (0.695 PlayerAnimationNetwork64.mrn)

- State machine NodeDef (type 0xa): child ids at +0x90; child records (0x30 each) at +0xd0 + u32@+0xe8; per state
  its condition index list and exit transitions; condition offset table at +0xd0 + u32@+0x100 [P layout, checked
  against StandPoses where every count matches: 18 / 9 conditions, 6 / 5 exits].
- Transition NodeDef (type 0x192, flags 8): +0x90 u16 from / u16 to, **+0xe0 f32 blend duration (s)** [P].
- Conditions by type (meaning read from their data, [I] names): 0x259 OnMessage (an index into the network's
  message list: 75 WeaponEnd, 76 WeaponSwitch, 90 WeaponEquipped, 61 Emote, 67 EmoteLoop, 68 EmoteSingle, 99 Roll,
  91 Enter_Roll …), 0x25e control parameter greater, 0x260 less, 0x263 crossed duration fraction of a node, 0x269
  node active, 0x273 in range (with an invert byte).
- Switch NodeDefs carry their thresholds (e.g. Translations_only 20…27, Sidestep 10…17, Roll 0…7, SwordUser Switch1
  0…5) [P].

### F2. A second model for the off hand (L192 (i), L95)

- Item definitions carry a second model and tint: record tail strings 1 and 3 (client +0x80 / +0xb0, data +0x88 /
  +0xb8; the client's own dump FUN_142e43c70 prints them as "secondaryModelName" / "secondaryTintAlias") [P].
  Items: shields of 3030 / 3766 / 1528 / 3037 / 4091, quivers of 3761 / 4089, the left gun / dagger / katana / bottle of
  3040 / 3765 / 1517 / 3039 / 3038 (gameData695/items.json) [P].
- The client attaches the second model only in its own item-to-actor builders (FUN_142ebde20, FUN_142cb1f60; check
  FUN_143b7a520 = item in the primary slot and has a second model; slot FUN_143b7a4a0 = slot manager +0x1f4), and that
  slot is StringHashToValue **SecondaryWeaponSlotName = "Off Hand"** (PrimaryWeaponSlotName "Main Hand"; read by
  FUN_142d12360 → FUN_143d49c90) = EquipmentSlotDefinitions 11 [P].
- In the world, EquipmentSetCharacterEquipmentSlot (handler FUN_142e42da0) attaches only the one model in the packet
  (FUN_142ebd620, one record per slot +0x188) and never reads the item definition [P]. The actor's Usage names the bone
  for the secondary slot: `boneAttachmentNameSecondary` = OffHandWeapon on the DarkElf dagger, Shield on the shields,
  BackWeapon on the quiver [P adr].
- So the server must send the second model in slot 11 itself; before 9226d91 it never did, and every sword-and-shield
  and dual weapon showed one hand only [I strong: no other packet carries a second model]. Built; item guid 0 in that
  packet, because FUN_142ea49e0 would re-slot the item record into the local equipment model.

### F3. Zone commands replace the client's own chat commands of the same name (L192 (c) (d))

- The chat manager's set-up FUN_1433595d0 registers its commands through FUN_14336e150 → FUN_142dd7560 into the
  global command table DAT_14543c6c0: say, whisper/tell/t, reply/r, shout/**yell**, **emote/em/me** (0x37), guild,
  officer, join, leave, whochannel, groupsay/partysay/psay/gsay/g (strings read from the exe at DAT_144759c90 …) [P].
- The zone's CommandList handler FUN_1433bad50 registers each server command through the same FUN_142dd7560, which
  removes any entry with the same name hash before adding the new one ("last registration wins") [P].

---

### L95. Spawner row 40 "Spawn With Weapons Equipped" / creature weapons
- **Verdict:** CLOSED (best evidence)
- **Answer:** The row (drop-down, Equipped / Sheathed) and both carriers were closed earlier [P] (agent-storyhide.md,
  agent-assets.md). What was left: (1) live's weapon actor and tint per creature, (2) shields / the off hand, (3) how
  bows look on creature bodies.
  (1) The weapon *type* per creature is client data [P] (the props' palette icons, dev post 15 Mar 2016); the *actor*
  is server-only data: no table, prop, model, .adr (EquipType / ChildAttachSlotsEx empty), prop list field or packet
  capture ties a creature to a Wield_*.adr (searched below). The table stays the documented best guess, on by default
  (live creatures held weapons: wiki "Uses a flaming greatsword", "Can block damage with its shield") [W].
  (2) Solved by F2: the off hand is its own slot packet (slot 11) and the shield / second dagger / quiver actors name
  their own bone [P]. Built: every "Swordshield" creature a shield (its player weapon's own — Fire / Shadow — else
  Wield_Shield_Epic_Physical_Physical_000, the one shield no item uses that sets no animation enumerations; the others
  set Class 1016 and would fight the sword's) [I], every "Daggers" creature the second dagger (the icon names a pair,
  Class 1015 DualDagger, as item 3765) [I strong], the two player bows their item's quiver (3761 Quive_Halas_07, 4089
  Quive_Halas_03) [P item data]. The Barren skeleton borrows the player skeleton (borrowSkeleton 1) and the Highfall /
  Ebonguard / NovaTech creatures are player bodies, so the bones exist [P adr]; the kobold skeleton is its own (look).
  (3) Visual only — a look in game, not a research gap (runtime checks below).
- **Searched:** 0.695 packs (every spawner / creature prop model, 75 .adr; Skeleton_00 / Kobold_Melee / PCNPC .adr and
  .dme), gameData695 (ArtSourceActors, ArtSourceActorTintTypes, Models, ProfileDefinitions, EquipmentSlotDefinitions,
  CharCreateGearSetMapping, LootPinata, ActorSockets, ActorCompositeEffectDefinitions, TriggerFxTag, AnimationSelection*,
  WeaponAnimationClasses, items.json), Resources/PacketDumps (145-PropBase, SendSelf.bin: no Wield_ string), the 0.695
  decompile (no Wield_ string; the slot handler and attach path above), design/wiki creature pages, design/forum.
- **Server:** changed in local/pe2-combat 9226d91 (CreatureWeapons: `Weapon.OffModel` / `OffTint`, `OffHandMessage`;
  Creatures.SendFull / SendWeapon send slot 11, emptied with the hand). Setting `CreatureWeapons` (default true = live
  had weapons) unchanged. Tests: OffHandRuleTests, CreatureWeaponsTests (off models attachable).
- **OPEN-LEADS text:** `- **CLOSED 2026-10-26 (pe2-combat, best evidence; 9226d91):** weapon type per creature is client data [P], the actor is server-only (every pack / table / capture searched, phaseE2-combat.md L95); the off hand is solved: item definitions carry a second model (tail +0x80/+0xb0, FUN_142e43c70 "secondaryModelName") that the client attaches only in its previews, in SecondaryWeaponSlotName "Off Hand" = slot 11, and the in-world slot handler FUN_142e42da0 attaches only the packet's model [P] — so creatures now get a shield (Swordshield icons), the second dagger (Daggers) or the item's quiver (player bows) in slot 11. Bows / shields on creature bodies: in-game look only.`

### L168. Live's movement-effect rows for sprint / boots / glide
- **Verdict:** CLOSED (best evidence)
- **Answer:** Semantics are proven and built (channels, charges, the release path; MovementEffects.cs) [P]. The 2016
  magnitudes (sprint %, jump heights, leap distances, glide speed, take-off damage) exist only in live's server ability
  data: AbilityEx.txt is empty, AbilityParameterDefinitions has no ability column, and no 2016 source states them; the
  2014 Heroic Movement post's costs / cooldowns / shapes stay [P 2014, I 2016] (row 242 1.4375 → 1.417 kept for 328
  [I weak]; Explosive Leap's blast ours, row 429 a weak candidate [I]).
  **Dash Soles / Roll Wraps movement — found:** no ClientExecutedEffect moves sideways, so the evades are
  root-motion animations. Proven pieces [P]: (a) each cast id is a direction — InputActionGroupEntries groups 82 / 153,
  INPUT_MODIFIER = RefInputProfileActions 1 MoveForward, 2 MoveBackward, 7 StrafeLeft, 8 StrafeRight; 432 F, 807 FL,
  808 FR, 809 none (= back: no row asks for back alone; UI_DISPLAY 1), 810 BL, 811 BR, 812 L, 813 R; 1211 F, 1212 FL,
  1213 FR, 1208 none, 1209 BL, 1210 BR, 1214 L, 1207 R; (b) the network's evade branch Action|Action|Evade_Roll switches
  on AbilityIndex (thresholds from the NodeDefs): Switch4 (8098) ≥ 20 → Translations_only (8096: 20 Teleport_Front,
  21 _Right, 22 _Left, 23 _Back, 24 FR, 25 FL, 26 BR, 27 BL), else Switch3 (8047) ≥ 10 → Sidestep (8343, 10-17), else
  Roll (8381, 0-7: Evade_Front …); (c) AnimationSelection rows 415 / 465-467 / 474-477 = AbilityIndex 0-7 (Translation
  0.6), 523-537 = 10-17 (0.75), 593-600 = 20-27 (0.5) (+ 601-608, 623-630, 634-640). Which row live's ability named is
  server data [I]: Roll Wraps → the Roll set ("acrobatic roll", 577211), Dash Soles → the Translations_only slide
  ("sudden dash", 567463). Built: the CastSuccess carries it (CastSuccess +0x3c → FUN_142d5dfa0 → FUN_143cd5e90 plays
  it on the caster, the same path the harvest swings use) and the players within 80 m get the same selection.
- **Searched:** design/forum/all (every thread; Heroic Movement 2014-10-14-49225, hotfixes 2014-10-17, 2016 notes
  56999, 57252), forum_raw, design/wiki (Boot and every boot page, Run Speed, Jump Bonus, Controls), gameData695
  (AbilityParameterDefinitions, InputActionGroupEntries, DynamicInputActionSystems, DesignBased* tables,
  AnimationSelection / Variables, RefInputProfileActions), the 0.695 decompile (seven ClientExecutedEffect inits,
  FUN_142e255e0, the cast-start path), the 0.695 PlayerAnimationNetwork64.mrn (F1), PacketDumps (no movement casts).
- **Server:** changed in local/pe2-combat de3e79e (BootAbilities.EvadeSelection; Abilities' boot branch). Under the
  existing setting `MovementEffects` (default true = live). Tests: CombatAnimationLeadsRuleTests.
- **OPEN-LEADS text:** `- **CLOSED 2026-10-26 (pe2-combat, best evidence; de3e79e):** semantics proven and built; 2016 magnitudes are server-only (every forum thread, patch note and boot wiki page searched) — the 2014 post's numbers stay [P 2014, I 2016]. Dash Soles / Roll Wraps found: each cast id is a direction (groups 82 / 153 modifiers 1 F / 2 B / 7 L / 8 R; no modifier = back) and the network's Evade_Roll switches on AbilityIndex (0-7 Roll, 10-17 Sidestep, 20-27 Translations_only, order F R L B FR FL BR BL) [P]; built: CastSuccess AnimationId = Roll set (415/465-467/474-477) for Roll Wraps, the slide (593-600) for Dash Soles [I which set], also to watchers. Runtime check: does the root motion move the player.`

### L170. Did live melee use server-started targeters?
- **Verdict:** CLOSED (best evidence)
- **Answer:** The mechanism is resolved [P] (client-run Sweep from CasterInfoReply +0x118; FindTarget / End; built
  2fc0c49) and so are the two follow-ups the entry still lists [P] (agent-l170-followup.md): the Sweep has **no**
  attackable / faction / alive test — any character with a physics body in the arc is reported; our creatures get one
  (full data, Health resource > 0, control mode 2); the reply sends MaxTargets 0 so corpses / allies can't use up the
  client's limit (WeaponCombat.Targeters.cs:141) [P built]; bit 45 + the Block effect type 0xC5838838 (EffectStart
  +0x110 arc, +0x114 height) are the blocking state, built as BlockStance (setting BlockStanceVisible) [P]. Left are
  live's Sweep numbers per weapon (radius, angles, duration, re-hit) and the Block arc / height: server-only ability
  data (the client's ability tables are stripped; no capture of a weapon CasterInfoReply exists). Ours: the server
  cone's own Range / Size / MaxTargets and the animation's AttackTime for the timing [I], documented.
- **Searched:** 0.695 decompile (Sweep FUN_14354d190 / d880 / e3a0 / e800, Block FUN_14354cda0 / dc10, reply reader
  FUN_142e016b0, EffectStart FUN_142e19660), gameData695 (AbilityEx, AbilityEffectTypeSettings, ClientEffects:
  header-only), PacketDumps, design/forum (melee / block texts: 2016-05-24-57170 l.312, 2016-05-11-57105, 2016-04-26),
  design/wiki weapon pages (costs / cast times only).
- **Server:** matches already (2fc0c49 targeters, MaxTargets 0, BlockStance). Settings `MeleeTargeters`,
  `BlockStanceVisible` (both default true).
- **OPEN-LEADS text:** `- **CLOSED 2026-10-26 (pe2-combat, best evidence):** mechanism and both follow-ups proven and built (no attackable test in the Sweep, our creatures have bodies, MaxTargets 0; bit 45 + Block effect = BlockStance) [P]; only live's per-weapon sweep numbers and the Block arc / height remain, server-only ability data (tables stripped, no capture) — ours = the server cone's Range / Size and the clip's AttackTime [I].`

### L188. Spire / My Site travel cooldown (abilities 185 / 186)
- **Verdict:** CLOSED (best evidence)
- **Answer:** The 60 s the two casts carried was the **Cozy Campfire's** caster info (ability 1098 of item 3894, the
  capture MiscHandler copied the travel bodies from), not travel data [P: MiscHandler.capturedCasterInfo 185 / 186 =
  the 1098 body with the id swapped; Consumables [1098] = Cozy Campfire]. Live's history [P dev notes]: one-hour
  claim / Spire cooldowns (26 Mar 2014), own claim 30 min (1 May 2014), "adjusted" (2 May 2014), Stone of Recall to
  claim / Spire and the 10-minute Portal Shard (17 Dec 2014), **"The Stone of Recall no longer has a cooldown" (5 May
  2015, 2015-05-05-54485 l.150)**, and the Teleport menu (185 / 186) "replaces the need for the Stone of Recall" (21 Mar
  2016, 2016-03-21-56259 l.76). No later note, 2016 thread or wiki page names a travel cooldown (wiki Spire: "At any
  time, players can also use a UI element to travel"), and the 0.695 travel windows show none (re/as3_695
  TeleportWidget / TeleportSpireWidget) [P negative]. So live 2016 had none [I strong].
- **Searched:** design/forum/all and patches (cooldown / teleport / travel / spire / recall / shard), design/wiki
  (Spire, Travel, Travel Point, Exploring, Chests, Portal Shard, Stone of Recall), re/as3_695 (both teleport widgets),
  0.695 UI Lua (global.lua OpenTravelMenu / OpenSpireTravelWindow), MiscHandler capture table.
- **Server:** changed in local/pe2-combat 813ca1a: setting `TravelCooldownSeconds` (Docker `TRAVEL_COOLDOWN_SECONDS`,
  default 0 = live 2016) goes into 185 / 186's caster info +0x70 (the client then refuses a repeat itself, record
  +0x14c) and the server refuses with CastFail + text 562080 when non-zero. Tests: PacketLayoutTests.
- **OPEN-LEADS text:** `- **CLOSED 2026-10-26 (pe2-combat, best evidence; 813ca1a):** the old 60 s was the Cozy Campfire capture (ability 1098) the travel bodies were copied from, not travel data [P]; live dropped the Stone of Recall's cooldown on 5 May 2015 and the 21 Mar 2016 Teleport menu replaced it, no later source names one [P notes, I 2016]. Default now 0; setting TravelCooldownSeconds (TRAVEL_COOLDOWN_SECONDS) sends a value in 185 / 186's caster info +0x70 and the server checks it.`

### L192. Emote / animation details
- **Verdict:** RESOLVED for (a) (b) (d) (e) (g) (i); CLOSED (best evidence) for (c) (f) (h)
- **Answer:**
  - **(a) live's packet for emote chat lines** — RESOLVED [P]. The client's only reader that fills [*speaker*] /
    [*target*] in a text id is ChatPacketFromStringId (Chat sub 4, reader FUN_143356860; FUN_143364980 case 4 sets the
    tokens speaker / target / owner / player); the other speaker formatter (PlayerUpdate 0x19 EffectPackage,
    FUN_142ce71d0) uses "speaker" / "listener" only. The client's own /sit sends C→S PlayerUpdate 0x77
    EmoteStringById with "Emote.SitOther" (FUN_1433b4e90), for the server to relay. Ours sends exactly
    ChatPacketFromStringId — matches.
  - **(b) /talking on move** — RESOLVED [P] + built. The loop is the lasting variable EmoteLoop 1 (rows 236 / 243 /
    252 / 265). In the network's locomotion state machine (node 206) a global transition 1408 enters the emoting
    locomotion on "EmoteLoop > 0.5 and WeaponIdle < 0.5"; it is left only by 1415 "EmoteLoop < 0.5 and ForwardSpeed
    > 9" or 1416 "EmoteLoop < 0.5 and EndSplit"; the idle turns likewise (1861 / 1862) [P transitions; I Morpheme's
    global-transition rule]. So walking never ends it on any client; only /stop (253) or a single-shot emote (every
    EmoteSingle row sets EmoteLoop 0) does [P data]. Built: loops remembered like moods (late arrivals, walking keeps
    it, /stop or a single-shot emote forgets it and reaches everyone shown it).
  - **(c) /yell vs the shout command** — CLOSED (best evidence) + built. The client has /yell as an alias of /shout
    (F3) and also "yell" as an emote word text of its own (570042) next to its four yell lines (572100 "You yell
    loudly.", 572095, 572114, 572107) [P]; the 2016 wiki emote table lists "/yell — You yell loudly." [W]. A zone
    command replaces the client's alias (F3) [P], so live's /yell was the emote [I strong]. Built with setting
    `YellIsEmote` (YELL_IS_EMOTE, default true); /shout is always the shout.
  - **(d) "/emote" free text vs our "/emote <n>"** — RESOLVED [P] + fixed. /emote, /em, /me are the client's free-text
    emote on the Emote channel (F3; wiki 2016 "/emote <text> — Allows you to send any emote that you type"). Our zone
    command "emote" replaced it (F3); renamed to /emoteclip. The server relays the Emote channel like /say (Chat.cs).
  - **(e) the six-shooter shooter's own animation** — RESOLVED (mechanism) [P]. The caster's client plays what the
    server sends: CastSuccess +0x3c (FUN_142d5dfa0 → FUN_143cd5e90) and CastContinue +0x48 (FUN_142e17b90 →
    FUN_142dfc010); nothing else animates the shooter's own weapon casts. The server sends each tick's Enter and Release
    (ContinueChannel) — matches; the look (right / left hand) is an in-game check.
  - **(f) potions / dynamite clips** — CLOSED (best evidence) + built. The network's only drinking clip is
    Biped_Core_Utility_UsePotion_01 (Emotes_SingleShot, Emote_Index 22 = selection 287) and its only throw clip is
    Biped_Core_Harvesting_Throw_F01 (Action|Enter / Release|Landmark switch on Class, child 7 = Class 7: selections 41
    Enter / 42 Release) [P]. Which selection live's item abilities named is server data [I]. Built: potions and the
    three brews play 287; dynamite plays 41 / 42 (it was the pick swing 226 / 227), to the player and watchers.
  - **(g) stance / mood on the player's own client across a re-zone** — RESOLVED [P]. ClientBeginZoning deletes every
    entity but the player (FUN_142d1ccf0 case 8 → FUN_142d60340, cited in MiscHandler.ClientIsReady), so the player's
    own network keeps Stance_Index / Emote_Emotion / EmoteLoop; the others get them through IntroducePose after the full
    character; a stance still ends server-side because the arrival is more than 0.5 m away (Emotes.Moved) and its
    "stand" reaches the player's own client too — consistent on both sides.
  - **(h) building pose and revive seen by watchers** — CLOSED (best evidence) + built (revive). Revive [P data /
    network]: selection 840 {AbilityIndex 1, Interacting 1} = Action|Action|Idle_Pass|Interaction child 1
    "ReviveAlly" (835 is child 0 Crystal_Interaction), 841 {Interacting 0} ends it, 837 "Cancel" breaks it off, 839
    "Revive" is the revived's get-up; the client plays nothing itself for StartRevive (dispatcher FUN_142cdfa60 case
    0x41 only records the reviver, FUN_142ec9910, and shows the bar). Built: the reviver plays 840 and ends with 841 /
    837 (self + watchers). Building pose: no AnimationSelection row sends any of the network's build messages
    (Translate 48 … Undo 58, Building_End 64) and no code sends them by name [P negative], so no server packet can ask
    a remote character for a build pose by selection; what watchers see of a builder is the cursor / edit stream the
    server already relays (WorldEditing 0x10C, VoxelConstruction ResponseObserved) [I]. New lead N2.
  - **(i) shields (off hand slot 11)** — RESOLVED [P] + built: F2.
- **Searched:** 0.695 decompile (chat set-up FUN_1433595d0, FUN_14336e150, FUN_142dd7560, FUN_1433bad50, Chat sub 4
  FUN_143364980 / FUN_143356860, PlayerUpdate dispatcher cases 0x18 / 0x41-0x43, CastContinue FUN_142e17b90,
  FUN_142e42da0, FUN_142ebde20, FUN_142cb1f60, FUN_143b7a520 / 7a4a0), exe strings (DAT_144759c90 …), gameData695
  (AnimationSelection / Variables, StringHashToValue, items.json, ChatChannels), the 0.695 network (F1; message list,
  Locomotion 206, Interaction switch 4732, Landmark switches 4843 / 5471), re/out/text_695.tsv, design/wiki Commands
  List, re/out/packet_ids_695.tsv, trace/families_695/0026_PlayerUpdate.md.
- **Server:** changed in local/pe2-combat — 9226d91 (i); 813ca1a (b) (c) (d), setting `YellIsEmote`; de3e79e (f) (h).
  Tests: EmoteAuditRuleTests (loop, yell, emoteclip), OffHandRuleTests, CombatAnimationLeadsRuleTests.
- **OPEN-LEADS text:** `- **CLOSED 2026-10-26 (pe2-combat; 9226d91, 813ca1a, de3e79e):** (a) ChatPacketFromStringId is the client's only [*speaker*]/[*target*] path — matches [P]; (b) EmoteLoop lasts through walking (locomotion SM 206: 1408 in, 1415/1416 out only on EmoteLoop < 0.5) [P] — loops now remembered like moods; (c) zone commands replace the client's chat commands (FUN_142dd7560), the client's own word 570042 + lines → /yell is the emote (setting YellIsEmote, default on) [I strong]; (d) /emote /em /me are the client's free-text emote (FUN_14336e150 0x37) — our "emote" hid it, now /emoteclip [P]; (e) the caster plays CastSuccess +0x3c / CastContinue +0x48 — matches [P]; (f) UsePotion 287 and the Class 7 throw 41/42 are the network's only clips — built [P clips, I choice]; (g) re-zone keeps the player entity (FUN_142d60340) — consistent [P]; (h) revive = 840 ReviveAlly / 841 / 837 — built; no selection exists for a build pose (new lead N2); (i) off hand slot 11 built (F2).`

### L194. Weapon channel limits, charged follow-ups, Recon volley numbers
- **Verdict:** CLOSED (best evidence)
- **Answer:** The numbers asked for (longest hold / hits per channel, whether a charged Basic's follow-up was charged,
  the Recon volley's reach / arc / shot count / spacing / cost) are live's server-side ability data: AbilityEx.txt is
  empty, AbilityParameterDefinitions rows carry no ability id, the live SendSelf loadout is zeros, no capture contains a
  weapon CasterInfoReply or cast [P negative]. The texts give the shape only ("Channel …", "a rapid volley of shots",
  "For a short time after leaping you can activate this attack again") [P 574719-574724, 574876-574887]; the wiki's
  weapon pages give costs / cast times / cooldowns (already used in WeaponAttacks.Balance) [W]; the 26 Apr / 25 May
  2016 notes give only directions ("higher energy cost over time", "slight delay after channeling ends") [P]. Ours stay
  as documented (ticks 3-5, block 2 s, volley 6 Energy) [I]. **Which clip the left-click follow-up plays** — found:
  the SwordUser (Class 1010) attack switch Action|Action|Enter|SwordUser|Switch1 (node 4942, thresholds 0-5) plays
  **Biped_Core_Adventurer_Sword1H_DashAttack_01 at AttackIndex 1** (selections 316 / 317; 317 Translation 0.525 = it
  moves the character) [P] — that is the "charge" the user saw. Both SwordUser Basic Attacks only swing in front
  (Assault 574453, Sanctify 576315) while the dash belongs to Overrun ("spinning dash", 574455, the Power Attack) [P
  text]; built: the sword's basic combo skips index 1 (0, 2, 0 …) [I live's choice].
- **Searched:** gameData695 (AbilityEx, AbilityParameterDefinitions, InputActionGroupEntries, DesignBased* tables,
  AnimationSelection / Variables, WeaponAnimationClasses), re/out/text_695.tsv, design/wiki weapon pages (Overcharged
  Recon Blaster, Gunslinger's Six-Shooters, Supercooled Power Cannon, Flame Bound Bastion, Armament of Unrelenting
  Vengeance, Avenging Champion's Exaltation, Blight Warden's Pain, Staff of Frozen Sorrow …), design/forum/all
  (2016-04-26-56999, 2016-05-11-57105, 2016-05-24-57170), PacketDumps (SendSelf.bin), the 0.695 network (atk switches).
- **Server:** changed in local/pe2-combat de3e79e (WeaponAttacks.IsDashIndex). Numbers unchanged (documented as ours in
  docs/gameplay.md). Tests: CombatAnimationLeadsRuleTests.
- **OPEN-LEADS text:** `- **CLOSED 2026-10-26 (pe2-combat, best evidence; de3e79e):** channel holds / ticks, charged follow-ups and the Recon volley's numbers are server-only (AbilityEx empty, no weapon cast captured; texts / wiki / 2016 notes give shapes and costs only) — ours stay, documented. The follow-up clip found: SwordUser AttackIndex 1 = Sword1H_DashAttack_01 (switch 4942) [P], the user's "charge"; both sword Basic Attacks only swing in front, the dash is Overrun's → the sword's basic combo now skips index 1.`

### L201. The client's ~1.1 s emote blind spot after a held item is removed
- **Verdict:** RESOLVED (best evidence for the Morpheme rule)
- **Answer:** The window is two state-machine transitions of `StandPoses` (node 10128; F1) [P durations and
  conditions]: on the WeaponEnd request (message 75) with Class < 0.5, WeaponIdle < 0.5 and the Weapon1 idle still
  active, transition **15157 Idle_Stand_2 → WeaponSwitchCombo, 0.6 s** (the put-away clip; the user's "short animation
  before it"); in WeaponSwitchCombo, **15153 back to Idle_Stand_2 on WeaponIdle < 0.5, 0.5 s**. 0.6 + 0.5 = **1.1 s**,
  exactly the measured edge (0.8-1.1 s not shown, 1.2 s shown). During it no emote state is active (every emote is
  under Idle_Stand_2|Stand, and Morpheme evaluates a destination's exits only once its transition is done [I]), so the
  Emote / EmoteSingle request is consumed and lost and the queued second selection is refused. The other exits:
  15147 at 95 % of the switch clip (0.3 s), 15154 on WeaponEquipped (0.15 s), 15155 / 15156 for Class 1017 while moving
  (0.3 s); entries 15148-15152 (0.2-0.4 s) for taking an item out. Durations are animation seconds, so the window is
  the same on every PC (± one frame) [I]. Nothing in it reaches the server, so EmoteHolster keeps a fixed lead (1.5 s;
  the setting EmotesPutAwayHeldItem is off by default = live). Not explained yet: why a *stance* (no request message,
  only Stance_Index) also never showed when sent inside the window — new lead N1.
- **Searched:** the 0.695 PlayerAnimationNetwork64.mrn (StandPoses 10128, WeaponSwitchCombo 14756, the 13 transition
  NodeDefs, the message list, the Pick switch-out sources 14777 / 14778), trace/0695/emotes-client.md, re/test/emote_trace
  logs (as reported there), the 0.695 decompile (FUN_142e43550, FUN_142dfc010, FUN_143d95d40 — the selection play:
  stop-current, variables, then the message with the root-motion flag record +0x25).
- **Server:** EmoteHolster.PutAwayLead documented (de3e79e); docs/protocol.md "Emotes" updated. No behaviour change.
- **OPEN-LEADS text:** `- **RESOLVED 2026-10-26 (pe2-combat):** StandPoses (node 10128) runs 15157 Idle_Stand_2 → WeaponSwitchCombo on WeaponEnd (msg 75) with Class / WeaponIdle < 0.5 (0.6 s blend) then 15153 back on WeaponIdle < 0.5 (0.5 s) = 1.1 s, the measured edge [P transitions / durations, decoder re/out/pe2_combat/sm.py]; no emote state is active meanwhile, so the request is lost. Animation time, not frames. Server keeps the fixed 1.5 s lead (setting off by default). Stance-in-window detail → new lead (phaseE2-combat.md N1).`

---

## New leads

### N1. Why a stance sent inside the 1.1 s put-away window never shows (from L201)
- **Need:** stance rows (223, 224, 246, 248, 480, 844) have no request message, only Stance_Index (a lasting
  variable), yet a stance sent 0.8-1.1 s after EquipmentUnset never showed, even after StandPoses was back in
  Idle_Stand_2 (emotes-client.md §4). Either the client resets runtime variables when the attachments change (the
  actor's AnimationEnumerations re-applied after the unequip), or the Stances state machine (node 14236) needs an entry
  transition that only fires on a change.
- **Searched:** StandPoses / WeaponSwitchCombo (decoded), FUN_143d95d40 (variables are set, then the message),
  AnimationSelection rows.
- **Try next:** `re/out/pe2_combat/sm.py <mrn> 14236` (and the StanceSwitch children) for their conditions; the
  attachment path after vtable +0x498 for a call that re-applies enumerated runtime variables; a Frida read of the
  network's Stance_Index control parameter 1.0 s after an unset with a kneel sent at 0.9 s (low priority: the setting is
  off by default).

### N2. What watchers' clients show of a builder (from L192 (h))
- **Need:** whether a remote builder is animated at all on live (State_Building / the build messages 48-58 are never
  sent by name in the client code, and no AnimationSelection row sends them), e.g. from the WorldEditing 0x10C cursor
  stream or VoxelConstruction ResponseObserved handlers.
- **Searched:** AnimationSelection (no build-message rows), the decompile for the message / State_Building names (none).
- **Try next:** the 0.695 handlers of WorldEditing 0x10C and ResponseObserved (do they reach the remote character's
  network, e.g. via a hashed control-parameter set); a two-client look at a builder.

## Runtime captures / in-game checks needed

Nothing in this list blocks a verdict; they verify the builds (for TESTING-NEEDED.md):
1. **Off hand (L192 (i), L95):** `wield 3030` (Flame Bound Bastion), 3040 (Six-Shooters), 3765 (Poison Drenched
   Daggers), 3761 (Wanderer's Blazing Bow): shield / left gun / second dagger / quiver on the player's own screen and a
   second client; swap to a plain sword → the off hand empties. Creatures: Elite Barren Captain (2455), Ebonguard
   Fireblade (2514), Barren Fireshot (2446), a Scalewind Defender (kobold skeleton: does the shield sit on a bone?).
2. **Dash Soles (235) / Roll Wraps (4406) (L168):** Shift + each direction — the evade plays and the character
   travels with it (root motion). If the clip plays in place without moving, the local controller ignores the
   selection's root motion: report it (then the server must move the player, a new lead). Second client: the same
   evade.
3. **Potions / dynamite / revive (L192 (f) (h)):** drink a Healing Potion with empty hands (UsePotion clip), throw High
   Yield Dynamite (throw clip, self and watcher), revive a dead player (ReviveAlly loop, ended on success / walk-away).
4. **Commands (L192 (c) (d)):** `/yell` → yell emote + "You yell loudly."; `/shout hi` → shout; `/emote waves happily`,
   `/me …`, `/em …` → the free-text emote line for both clients; `/emoteclip 4` → cheer clip.
5. **Loops (L192 (b)):** `/talking`, walk around, stop → still talking; a second client arriving later sees it; `/stop`
   ends it on both.
6. **Sword combo (L194):** Armored Brawler's Blade (3764) left, left, left — no lunge on the second click.
7. **Travel (L188):** Travel to Spire twice in a row — no cooldown; with `TRAVEL_COOLDOWN_SECONDS=60` the client refuses
   the second with its own text.

## Summary

All seven leads have a verdict: L201 resolved (the 1.1 s window is StandPoses transitions 15157 + 15153, 0.6 s + 0.5 s,
read from the 0.695 network with a new state-machine decoder); L95, L168, L170, L188, L194 closed on best evidence
(what is left is live's server-side ability / creature data, every source listed); L192 resolved or closed item by
item. On the way two client facts changed the server: item definitions carry a second model that the client shows only
when the server sends it in the off-hand slot 11 (shields, quivers, left guns / daggers were missing for players and
creatures — now sent), and zone commands replace the client's own chat commands of the same name (our "emote" had
hidden the client's free-text /emote — now /emoteclip — and /yell is registered as live's yell emote). Also built: no
Spire / My Site cooldown by default (the old 60 s was the Cozy Campfire capture; setting TravelCooldownSeconds), looping
emotes remembered, Dash Soles / Roll Wraps evades by direction, drink / throw / revive clips, and the sword combo without
its dash attack. Three commits on local/pe2-combat (9226d91, 813ca1a, de3e79e), 1981 tests passing; two new leads (N1
stance inside the put-away window, N2 watchers' view of a builder) and a list of in-game checks for the coordinator.
