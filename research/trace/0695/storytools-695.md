# Story tools in the 0.695 client (agent #6)

Story-tool props, their Links, settings and Play Mode in the final client (0.695) against the launch client (0.589)
and against LandmarkEmu. Written 2026-10-15 by helper agent #6 (trace/0695/AGENTS.md). Read-only trace: nothing in the
server, the installs or Docker was changed.

Scratch / tools: `re/out/agent_story/` — `link_texts_695.tsv` (every link sentence in both builds, new / changed /
same, 355 rows), `props.py` / `find.py` (story props from PropDefs + PropCategoryPropMap), `keyhash.py` (the client's
setting-key hash for every EditableProperties row), dumped functions (`fn_1430a9260.c`, `fn_142f859b0.c`, `rec589.c`
/ `rec695.c`, `dd589.c` / `dd695.c`, `os_handler_589.c` / `_695.c`, `advflags.c`).

Used, not redone: launch-rules-audit.md 6.33 (Story Node text display), 6.34 (fallback speakers), 6.36 (sconces /
orbs / control panels have no Link kind), 6.56 (Play Mode lost on restart); docs/story-tools.md (the 0.589 protocol);
FEATURES.md "Story tools and Links"; TASKS §2.

---

## 1. Summary

1. **The client still holds no prop → Link table.** Which events / actions a prop offers is the server's answer to
   ObjectScripting 0x0D-0x0F (unchanged readers: 0.695 FUN_142f42470 / FUN_142f426a0 / FUN_142f423e0 = 0.589
   FUN_142ea38d0 / FUN_142ea3b00 / FUN_142ea3840, "same" in functions.tsv). What changed is the vocabulary: **56 new
   and 27 reworded link sentences** (re/out/agent_story/link_texts_695.tsv; its 3 other new / changed rows,
   570560, 575463, 577356, are descriptions that merely start with "When"). Four 0.695 events reuse text ids that
   meant something else in 0.589 (557290 "MAX", 565055 "Mine", 577092 "Decrement Count", 577213-577216 "Activate
   ability N..."), so a 0.589 client would show wrong labels for Counting / Combat Node links → build-gate them.
2. **New story-tool props in 0.695** (placeable, category 120 "Story Tools"): Combat Node 2940, IF Gate 2976,
   Scoreboard 3000 — and four props that existed as dormant rows in 0.589 become placeable: Random Link Filter 2657,
   Server Time Node 2659, Respawn Point 2701, Counting Node 2901. The Game Table 2313 moves into the Story Tools
   palette. Not in any palette: Hour Node 2945, Score Node 3011, Victory Node 3010, Team Spawners 3012 / 3013.
3. **EditableProperties grew from 30 to 46 rows** with 3 new columns (BLOCKS_USER_MODIFICATION, IF_NODE_CAPABLE,
   IF_DISPLAY_STRING_ID; loader 0.695 FUN_143dc1440 vs 0.589 FUN_143d01940). Six rows are IF-Gate-capable: counter
   current value, Respawn Point enabled, Logic Filter blue / yellow light, Control Point owning team, spawner is alive.
4. **The settings-window protocol changed** (packet layouts are #3's, recorded here because the story settings depend
   on them): the setting record's value / default / max / min / step are **f64** in 0.695 (record reader FUN_142c15ee0,
   `EditableProperty<double>`, RTTI `EditableProperty@N`) instead of f32 (0.589 FUN_142b7c740, `EditableProperty@M`);
   every drop-down option gains a trailing **string label** (0.695 FUN_142c1eae0 vs 0.589 FUN_142b85320). That string
   is how the live server labelled options the text table does not have (IF Gate comparisons, "Desired Time N").
5. **Two story props now show state on the prop itself**, driven by the server through the prop "setting" behaviour
   (UpdatePropInteractionState type 2 → ClientPropSettingBehavior vtable 0x144779f08 → FUN_1434777a0): the Logic
   Filter's blue / yellow lightbulbs (FUN_1430aa110, keys `script_logic_gate_a` 0x687b558b / `_b` 0xde034081 →
   composite effects 12835 / 12834 and 12833 / 12832) and the Counting Node / Scoreboard digits (FUN_1430a9260, key
   `prop_component_counter_current` 0x75e90645, |value| % 100 shown with ScriptCounter.*Digit.CompositeEffect.0-9 on
   FX_Attach1-4). 0.589 has none of these keys (FUN_1433c9790 only stores the value).
6. **Story Node text is rich text in 0.695**: the editor (InstancePropertyEditor `controls/LargeTextInput.as`) saves
   Flash htmlText with `<` `>` `'` `"` tokenised as `~[~` `~]~` `~a~` `~q~`, counts **512** characters down, offers
   bold / italic / underline and 20 colours; the speech bubble, chat window and on-screen notification de-tokenise it
   (SpeechBubbleWidget.as, ChatDocumentClass.as, NotificationWindowDocumentClass.as). The server must store and pass
   it through; its 1024-character cap (Props.cs:1005) would cut formatted text.
7. **Question bubbles**: 0.695 DisplayPropText carries an is-question byte and a response id (handler FUN_142f859b0
   params 5-6 → bubble FUN_142ebfcb0 stores them at entity +0x127c / +0x1280; FUN_142ece660 then raises HUD
   indicator **NpcDialogResponse** (HudIndicators row 29, Yes / No → Lua `Prop.OnConfirmNpcDialog` /
   `Prop.OnDenyNpcDialog`), which send the new **PropPacketDialogResponse** (PropBase 0xED sub 0x1D {u64 id, bool
   accepted}; FUN_1435fbf80 / FUN_1435fc030). The server turns that into the Story Node events "When a Luminary
   accepts / denies..." (577724 / 577725).
8. **AddBinding result 4 is new**: "You must bind this node to a single prop!" (StringHashToValue
   `AdvancedScripting.UniqueBinding.BindSingle` = 577233; 0.695 FUN_142fee7e0 gets the result as an int from the
   handler FUN_142f47400, where 0.589 FUN_142f4ea90 only got `result == 0`). Used by the Combat Node and IF Gate
   "choose the prop" links. Other codes (both builds, FUN_142ea8330 / 0.695 FUN_142f46ed0): 1 permission (567721),
   2 invalid cycle (567722), 3 duplicate (no message), else 567723.
9. **Server today** (Game/StoryTools.cs, PropSettings.cs) covers the 0.589 set well; for 0.695 it has none of the
   new props, none of the new Link ids, the old settings wire format, the 0.589 EditableProperties copy
   (LandmarkEmu/gameData/EditableProperties.txt = 0.589 file), no setting-state broadcast, no question bubbles, and
   Respawn Points are wired to the wrong prop (Graveyards.cs:21 uses 1225, a game-table marker, not 2701).
   New task lines 2.17-2.36 (§7), open leads L18-L23 (§8).

---

## 2. How the system works (both builds) and what 0.695 changed in the mechanism

| Piece | 0.589 | 0.695 | Evidence |
|---|---|---|---|
| Link vocabulary per prop | server (ObjectScripting 0x0D-0x0F answers; ids = text ids of the sentences) | same | docs/story-tools.md; readers unchanged (§1.1) |
| Link sentences in the text table | 291 (re/out/link_texts.tsv) | +56 new, 27 reworded (Logic Filter cogs → lights, Dice → Probability, Counting / Combat reuse) | re/out/agent_story/link_texts_695.tsv |
| Link editor UI | ControllerGameScripting, context menu FUN_142f55200 / FUN_142f56820 | unchanged (FUN_142ff5030 / FUN_142ff6650 differ only in addresses) | normalised diff |
| AddBinding result | bool success | int; 4 = single binding only | FUN_142f4ea90 vs FUN_142fee7e0; handler diff FUN_142ea87d0 / FUN_142f47400 |
| Settings rows | EditableProperties.txt 30 rows (10 columns) | 46 rows, 13 columns | tables-diff.md; loaders FUN_143d01940 / FUN_143dc1440 |
| Drop-down option table | EditablePropertyValues.txt loaded but **not shipped** | same (loader FUN_143dc1690: IS_DEFAULT, NAME_STRING_ID, VALUE, EDIT_PROPERTY_ID) | file index both builds; → L18 |
| Settings record | f32 values | f64 values | FUN_142b7c740 vs FUN_142c15ee0 |
| Drop-down option | {u32 text id, u32 value, bool} | {u32 text id, u32 value, bool, string label} | FUN_142b85320 vs FUN_142c1eae0 |
| Slider sends | every drag step | on mouse-up only (`handleMouseSliderChange`) | InstancePropertyEditor RulesetObjectSettingListItemRenderer.as diff |
| Prop setting state → visuals | stored only (FUN_1433c9790) | Logic Filter lights, counter digits (FUN_1434777a0 → FUN_1430aa110, FUN_1430a9260) | §1.5 |
| Prop text | plain, no limit shown | tokenised rich text, 512 chars | §1.6 |
| DisplayPropText (PlayerUpdate 0x78 in 0.695 dispatch FUN_142cdfa60) | text, prop, mode, seconds | + u8 question, + u64 response id | FUN_142ee6ce0 vs FUN_142f859b0 |
| Display modes | 1 text window (HudStateManager:OnOpenTextDisplay), 2 on screen (colour FFF6AA), 3 chat "[%s] says: %s", 4 bubble, 5 bubble + chat | same | FUN_142f859b0 switch |
| Question response | — | PropPacketDialogResponse 0xED / 0x1D | §1.7 |
| Prop help | — | radial button 74 "Open Help" (577204, `Prop.OpenHelpWindow`, RadialMenuButtons new row); PropHelpWindow row 1 ScriptObj_Damage_FTE Large CenterR, mapped to prop 2899 | tables-diff; luabind_695 |
| First-time help events | ScriptObj Logic 28, Path 29, Random 30, Text 32, Timer 33, Damage 39, Focus 40, Reset 41 | + Respawn 44, Combat 45, Probability 46 (30 renamed ScriptObjProbability), ServerTime 47 | FirstTimeEvents diff; pages HelpPages/ScriptObj_*_FTE.html |
| Claim record (Play Mode) | mode at entry +0x370 | +0x378; Play Mode Rules byte at +0x380 (bits 1 Block Grappling Hook, 2 Block Boot Abilities, 4 Block Voxel Destruction, 8 Allow Free Camera, 0x10 Allow Flying Character Camera, 0x20 Allow Orbit Camera; data source `Claim.AdventureFlags`) | FUN_143485f00 vs FUN_142d99b70; FUN_142dd2480 |

**Visibility in Play Mode** (story tools hidden, Scoreboard and Starting Location visible): not found yet how the
client decides — Counting Node 2901 and Scoreboard 3000 have identical PropDefs rows apart from the name; no prop-id
constant in either decompile. → L20.

---

## 3. Story-tool props in 0.695

Ids are PropDefs rows (0.695 `<0.695 install>\Resources\PropDefs.txt`). "Palette" = PropCategoryPropMap
category 120 "Story Tools" (or the category shown). Models: 0.695 Models.txt rows **matched by their DESCRIPTION /
file name** (the client has no prop → model link; the server sends the model id, Props.LoadDefinitions; the 0.589-era
dump has no rows for 0.695-only props). Status vs 0.589: **same** / **renamed** / **activated** (row existed in
0.589 but TRIVIAL 0, no recipe, no category) / **new**.

| Prop | Name (text id) | Palette 0.695 | vs 0.589 | Model (0.695 Models.txt) | Help page / FTE |
|---|---|---|---|---|---|
| 2325 | Logic Filter (572947) | 120 | ALLOW_USER_RENAME 0 → 1; Link texts reworded | (dump) | ScriptObj_Logic_FTE / 28 |
| 2330 | Travel Point (572977) | 120 | same | (dump) | ScriptObj_Path_FTE / 29 |
| 2344 | Story Node (573034) | 120 | settings + links added | (dump) | ScriptObj_Text_FTE / 32 |
| 2345 | Probability Filter (573040; 0.589 "Dice Roll Filter") | 120 | renamed | (dump) | ScriptObj_Probability_FTE / 46 (30) |
| 2367 | Timer Filter (573094) | 120 | duration step 10 → 1 | (dump) | ScriptObj_Timer_FTE / 33 |
| 2655 | Starting Location (574434) | 120 (+ category 0) | same | (dump) | ScriptObj_StartLoc_FTE |
| 2898 | Focus Node (577076) | 120 | same | (dump) | ScriptObj_Focus_FTE / 40 |
| 2899 | Damage Node (577072) | 120 | Link targets in areas added; Open Help | (dump) | ScriptObj_Damage_FTE / 39; PropHelpWindow 1 |
| 2900 | Reset Node (577084) | 120 | same | (dump) | ScriptObj_Reset_FTE / 41 |
| 2657 | Random Link Filter (574496) | 120 | **activated** (0.589 "[auto] Theme_Widgets_DMWidgets_RandomNode01") | 1738 RandomNode02 "Random Link Filter" | ScriptObj_Random_FTE |
| 2659 | Server Time Node (574498) | 120 | **activated** (0.589 "[auto] ..._TimerNode01") | 1737 "Server Time Node" | ScriptObj_Servertime_FTE / 47 |
| 2701 | Respawn Point (577587; links say "Respawn Location" 577527) | 120 | **activated** (0.589 "[auto] ..._SpawnLocation01", name 576089) | 1739 "Respawn Node" | ScriptObj_RespawnLoc_FTE / 44 |
| 2901 | Counting Node (577093; 0.589 "Counter Node") | 120 | **activated** | 1728 Theme_Widgets_CountingNode01_CountingNode01_01 | — |
| 2940 | Combat Node (577196) | 120 | **new** | 1727 ..._DMWidgets_CombatNode01 | ScriptObj_Combat_FTE / 45 |
| 2976 | IF Gate (577524) | 120 | **new** | 1741 "IF Node" | — (help text 577588) |
| 3000 | Scoreboard (577536) | 120 | **new** | 1736 Theme_Widgets_CountingNode01_Scoreboard01_01 | — |
| 2313 | Game Table (572907; 0.589 "Adventure Game Table") | 120 | **activated** in the palette (TRIVIAL 0 → 1, recipe 0 → 52) | (dump) | — |
| 2945 | Hour Node (577253) | none | **new**, not placeable from a palette (dev name of the Server Time Node? both sit beside "Amount of Listeners" 577254) | — | — |
| 3011 | Score Node (577664) | none | **new** | 1744 "Points Node" (by name, not proven) | — |
| 3010 | Victory Node (560924) | none | **new** | 1745 "Victory Node" | — |
| 1224 | Control Point (577717; 0.589 internal name) | none (Game Table props) | renamed; new links | (dump) | — |
| 1223 / 1367 / 3012 / 3013 | TeamSpawner Blue / Red / Yellow / Green (577680-577683) | none (IgPropRoleProps role 2: rows 4, 5, 17, 18) | 3012 / 3013 new; 1223 / 1367 renamed | (dump) / — | — |
| 1590 | Linking Area (577124) | Area tool (StringHashToValue `GameScripting.PropNpcDefinitionId` = 1590) | links + setting added | (dump) | — |
| spawners | "Spawner: …", creature props (category 98 Creatures) | 98 | links + settings added | (dump) | — |
| 2949-2970 | Fireworks Emitter (22 kinds, 577439-577460) | 17 Emitters | **new** (TASKS 3.9) | — | — |
| 3003-3008 | Reward Chests (577596, 577602-577606) | none | **new** (TASKS 3.11) | — | — |

Models with no prop at all: 1742 "Audio Node", 1743 "Group Node" (no PropDefs row, no text) → L21.

---

## 4. Per prop: settings, Links, what the client shows, what the server must do

Link notation: **in** = actions other props' events can run on it ("...X"), **out** = its events ("When ..."). Ids
are text ids (= the ids the server uses on the wire). Which prop owns which sentence is not in the client: it is
taken from the sentence's own wording ("this node's count", "this Respawn Location") confirmed by the patch notes
(P-ids, trace/0695/patchnotes.tsv), the wiki (design/wiki/pages) and the client help pages (HelpPages/ScriptObj_*).
Settings: EditableProperties row id, key, label text, range (min-max, step), default; "IF" = IF_NODE_CAPABLE with its
label.

### 4.1 Story Node 2344 (changed)
- **Settings** (0.695 rows): 20 `npc_mood_setting` Mood (576508), 21 `npc_stance_selection` Stance (576507), 22
  `npc_emote_setting` Emote (576506), 24 `npc_emote_spam_enabled` Announce Emotes in Chat (576504, checkbox, 0), 25
  `prop_text_display_type` Text Location (576505; VALUE_DEFAULT 4 in 0.589, **5** in 0.695), 37
  `prop_text_display_time` Text Duration (577151; 1-20 s, 5), **new** 41 `npc_weapon_visuals` Weapon Visuals (577234;
  options 577235 Equipped / 577236 Sheathed), **new** 112 `prop_text_is_question` "Is Question? (Chat Bubble)" (577726,
  checkbox, 0). Text box: 512 characters, rich text (§1.6; colours, in order: dark red 0x990000, red 0xFF0000, orange
  0xFF6600, light orange 0xFF9900, yellow 0xFFFF00, dark green 0x006600, green 0x009900, light green 0x00FF00, dark
  blue 0x000099, blue 0x0000FF, light blue 0x0066CC, magenta 0xFF00FF, purple 0x6600FF, light purple 0x6666FF, brown
  0x663300, white, light gray 0x999999, medium gray 0x666666, dark gray 0x333333, black — LargeTextInput.getColorData).
- **Drop-down options** (labels live in the client text; the option *values* come from EditablePropertyValues.txt,
  not shipped → L18): Mood: Angry 559168, Calm 559169 (default, help page "Calm is the default"), Happy 559170, Sad
  576538. Stance: Dancing 559173, Drunken 571883, Kneeling 576539, Lying Down 576540, Skipping 576541, Sneaking 576542,
  Standing 576543, Sitting 576610. Emote (29): Beckon 576547, Beg 576544, Bow 576545, Cheer 576546, Congratulate
  576550, Cough 576551, Cry 576552, Cry (Sob) 576553, Flex 576554, Flirt 576555, Groan 576556, Hug 576557, Laugh 576558,
  Laugh (Giggle) 576559, No 576560, Nod 576561, Point 576562, Shoo 576563, Shrug 576564, Sneeze 576565, Stretch 576566,
  Talk 576567, Talk (Continuous) 576569, Talk (Stop) 576570, Taunt 576571, Wave 576572, Welcome 576573, Yawn 576574,
  Yell 576575 (lists: wiki Story Node; all texts exist in both builds). Text Location: On Screen 576480, In Chat
  576482, In Chat Bubble 576481, In Chat Bubble and Chat 577170. These are launch-era (0.589) lists; LandmarkEmu offers
  3 moods / 6 stances / 7 emotes / 3 locations (PropSettings.cs:56-64).
- **in**: 576243 ...Activate everything, 576255 text, 576248 mood, 576252 stance, 576262 emote, **new 577239**
  ...Activate the weapon visuals on this node.
- **out**: 577047 everything activates, 576254 text activates, 576251 mood, 576253 stance, 576263 emote, **new
  577238** When the weapon visuals on this node activates..., **new 577246** When the text on this node expires...
  (P5964, fixed P6191), **new 577724 / 577725** When a Luminary accepts / denies... (P6539).
- **Creature side** (spawner actions): 576259 Set mood, 576258 stance, 576261 emote, 3 say the text, 577048 perform
  everything, **new 577237** ...Set this creature's weapon visuals.
- **Client shows**: text per Text Location through DisplayPropText (modes 2-5; §2) — bubble over the speaking entity,
  chat "[name] says: text", on-screen; with "Is Question?" on and a bubble mode, Yes / No buttons (HUD indicator 29).
- **Server must**: route text by Text Location and Duration (6.33), fallback speakers (6.34), 512-char limit and the
  token whitelist (2.21), raise "text expires" Duration seconds after the text is shown, send question bubbles with a
  response id and turn PropPacketDialogResponse into accept / deny with the responder as triggering character (2.22),
  weapon visuals on creatures (needs the NPC equipment packet — hand-off), "Announce Emotes in Chat" (row 24).

### 4.2 Logic Filter 2325 (reworded, visible state)
- **Settings**: none in the window (help page ScriptObj_Logic_FTE: "There's no settings window because everything is
  handled via Links"). State rows (not user-set): 107 `script_logic_gate_a` (IF "Blue Light" 577623), 108
  `script_logic_gate_b` (IF "Yellow Light" 577624), 0 / 1.
- **in** (ids unchanged): 572961 / 572962 blue on / off, 572963 / 572964 yellow on / off, 576091 / 576090 both on /
  off, 576092 / 576093 / 576094 alternate blue / yellow / both.
- **out**: 572953 / 572954 blue on / off, 572956 / 572955 yellow on / off, 572957 / 572958 both on / off, 572959 /
  572960 either on / off. Only changes fire (P5780, P5819).
- **Client shows** (0.695): bulbs pop in / out (P5941) when the server sets the state: UpdatePropInteractionState type
  2, key 0x687b558b (blue) / 0xde034081 (yellow), value 0 / 1 → FUN_1430aa110 applies composite effect 12835 (blue on)
  / 12834 (blue off) / 12833 (yellow on) / 12832 (yellow off) (StringHashToValue ScriptLogicGate.CompositeEffect.A /
  A.Off / B / B.Off; key hashes verified with keyhash.py).
- **Server**: logic built (StoryTools.cs:676-692); missing the state broadcast (2.19) and the Reset Node rule "resets
  all Logic Filters without firing Links" (P5975 / P5976; 2.32).

### 4.3 Counting Node 2901 and Scoreboard 3000 (new in 0.695)
- **Settings**: 33 `prop_component_counter_current` Starting Count (577095; 0-99, 1, default 0; IF "Current Value"
  577622), 35 `prop_component_counter_max` Max Count (577096; 0-99, 1, default 10), 34 `prop_component_counter_min`
  (all zero, no label: hidden). 0.589 rows: 33 0-10, 35 1-10 default 5. StringHashToValue
  `CounterProp.StartingValueFieldName` = "Starting Value" (new; wiki also says "Starting Value").
- **in**: 577248 Increase by 1, 577249 Decrease by 1, 577250 Set to its maximum, 577251 Set to 0.
- **out**: 557290 reaches its maximum, 565055 reaches 0, 577247 increases, 577092 decreases (557290 / 565055 / 577092
  were "MAX" / "Mine" / "Decrement Count" in 0.589).
- **Client shows**: the value on both sides of the disc (P5956): type-2 state key 0x75e90645, FUN_1430a9260 shows
  |value| % 100 as two digits (tens, ones) with composite effects `ScriptCounter.FirstDigit.CompositeEffect.0-9`
  (12905-12914) and `SecondDigit` (12860-12869) on FX_Attach1-4. Counting Node hidden in Play Mode, Scoreboard visible
  (P5960, P6182; mechanism L20).
- **Server must**: hold the count per Play session (start = Starting Count), clamp to 0..Max, fire increase /
  decrease on change and reaches max / 0 when it lands there, broadcast the value (2.19), expose it to the IF Gate.
  What +1 at Max (or −1 at 0) fires is not stated anywhere found → L23.

### 4.4 Combat Node 2940 (new)
- **Settings**: 31 `npc_spawner_ai_capability` Combat Tactics (577073; 0-2, default 0; options Standard 577230,
  Stationary 577231, Do Nothing 577232 — value order not proven, L18) — the same row is the spawner's Combat Tactics
  (P6150). 38 `combat_controller_number_abilities` Ability Count (577166; 1-3, default 1). 39
  `enable_combat_ai_movement` Enable Combat AI Movement (577217; checkbox, 1) — owner prop (node or spawner) not proven.
  After linking, "a list of the creature's abilities" (P6152, help page) — rows the server makes (L19).
- **out**: 577162 Choose the creature this node will control... → spawner **in** 577163 ...Link this creature to the
  combat node. (single binding: AddBinding result 4). 577245 When the combat tactics on this node activates... (→
  spawner 577218 ...Set this creature's combat tactics., same pattern as the Story Node's mood).
- **in**: 577242 ...Set the combat tactics on this node's linked creature., 577213-577216 / 577243 / 577244 ...Have
  this node's linked creature perform Ability 1-6. Wiki: before a creature is linked only the tactics action is offered.
- **Spawner extras**: 577218 set combat tactics, 577219 ...Deactivate this creature's combat AI.
- **Server must**: keep the node → creature binding unique (result 4), offer ability actions only once linked (P6151),
  make the linked creature use its Nth ability, apply tactics (Standard = normal AI, Stationary = no movement, Do Nothing
  = no AI; P6161). LandmarkEmu: nothing (2.7).

### 4.5 IF Gate 2976 (new)
- **out**: 577629 Choose the prop to use in the IF condition... → target **in** 577628 ...Link this prop to the IF
  condition (single binding, result 4); 577626 If..., 577625 Else...
- **in**: 577627 Peform IF check... (sic).
- **Settings** (P6495-P6496): the property ("If"), the comparison ("Is": "Equal To"...), a value slider. Properties =
  IF-capable rows of the linked prop, labelled by IF_DISPLAY_STRING_ID: 33 Current Value (Counting Node / Scoreboard),
  103 Enabled (Respawn Point; no IF label → its display name 577589), 107 Blue Light / 108 Yellow Light (Logic Filter),
  110 Controlling Team (Control Point, 0-4, 577709; team names 577710-577713), 111 Is Alive (spawner, 577714). P6494
  also names doors / lights ("an object that has properties that define its state, such as a door"): no client row for
  door / light state → server-made (L19). Comparison labels are in neither the text table nor the exe (searched
  "Equal To", "Greater Than", "Less Than", "Not Equal") → server strings in the 0.695 option label (L19). 11 Oct
  (P6535-P6538): checks on the **triggering character** — game team, NPC disposition, real player or not.
- **Server must**: on "Peform IF check" read the live property of the linked prop (or of the triggering character),
  compare, fire If... or Else... with the same context. LandmarkEmu: nothing (2.11).

### 4.6 Random Link Filter 2657 (activated)
- No settings beyond Name (help page). **out** 577591 ...Assign potential action... (any number); **in** 577593
  ...Trigger Random Action. Server: run exactly one of its 577591 Links, chosen with equal odds (P6167, wiki).
  LandmarkEmu: nothing (2.9).

### 4.7 Server Time Node 2659 (activated) / Hour Node 2945 (not placeable)
- **Settings**: 101 `npc_time_count` Amount of Listeners (577254; 1-6, default 1); "Desired Time 1-6" (0-23, help page
  "using 24hr clock") — no EditableProperties rows, no text → server rows with string labels (L19). Wiki: new Desired
  Time rows appear after Amount of Listeners is saved.
- **out**: 577252 When game time in slot 1 is reached..., 577255-577259 When time 2-6 reached (wiki: offered per
  listener). No **in**. Wiki also saw "When a Luminary clicks this object..." on it (the 29 Aug "clicked on all props").
- **Server**: fire slot N when the world's game hour (WorldClock.SecondsOfDay, Game/WorldClock.cs) reaches Desired
  Time N — game time, not the site's custom time (P6165). LandmarkEmu: nothing (2.8).

### 4.8 Respawn Point 2701 (activated)
- **Settings**: 103 `respawn_point_enabled` Enabled (577589; checkbox, default 1; IF capable).
- **in**: 577530 / 577531 Activate / Deactivate this Respawn Location. **out**: 577528 / 577529 activates /
  deactivates, 574557 When a Luminary respawns at this Build Site... (fires only at the location used; wiki).
- **Server**: a player who dies on the site and Flees goes to the nearest *enabled* Respawn Point or the Starting
  Location (P6177; help page); facing = prop rotation (wiki). Appearance shows enabled / disabled (P6178; mechanism
  not found yet, L20). LandmarkEmu: Graveyards.cs:21 lists 1225 ("Theme_Widgets_RuleSetObjects01_RespawnMarker01",
  a game marker) as "Respawn Point" and ignores enabled state; 2701 is not handled (2.24).

### 4.9 Probability Filter 2345 (renamed)
- Settings 15 `random_gate_static_weight_a` Odds of Winning (573066; 0-100, step 5, default 50), 16 ..._b "Random
  Roll Weight B" (573067). Help page: "This number is the probability that a random dice roll will succeed. It
  defaults to 50%". Links unchanged (573036 roll; 573027 wins; 573037 loses; texts reworded). LandmarkEmu uses
  a / (a + b) (StoryTools.cs:369-373): equal to "Odds of Winning %" only while b = 100 − a → 2.30.

### 4.10 Timer Filter 2367
- Row 14 duration 1-600 s, step **1** (0.589 step 10; P5821, typed numbers P5980); 13 Loop Until Paused (573104).
  Links unchanged. P5822: on completion only "completes" fires, even when looping; "resets" only on an explicit
  "...Reset the timer." LandmarkEmu follows (StoryTools.cs:518-526, 843-850); whether Pause-and-Reset (576503) should
  fire "resets" is not stated (it does today).

### 4.11 Damage Node 2899 and Linking Area 1590
- Damage Node settings 30 Percent of Max Health (577078; 1-100, 50), 32 Heal Instead (577082). Links unchanged
  (in 565554, out 577070). Creatures it hurts drop no loot (P5768). 0.695: Open Help button + help window.
- Linking Area **in**: 577148 ...Damage all creatures in this area. (0.589), **new** 577221 ...Damage all Luminaries
  and creatures in this area., **new** 577228 ...Damage all Luminaries in this area. (P5823), **new** 577548 / 577549
  ...Destroy / Heal Voxels in this area. (P6185; heal only player-set voxels, P6201; a pending heal is not cancelled by
  a later destroy, P6205), **new** 577723 ...parse every entity in this area... **out**: 565950 / 565951 Luminary
  enters / exits, 573163 / 573164 creature enters / leaves (a creature despawning inside counts as leaving, P6192),
  **new** 577722 For every object in this area when parsed... Setting **new** 102 `area_voxel_damage_permission`
  Allow Voxel Destruction (577550; options Site-wide setting 577551 (default, P6183), Allowed 577552, a "blocked"
  option whose text id is not proven — L18).
- Parse semantics (once per entity, entity = triggering character) are read from the wording only → L23.
- LandmarkEmu: area damage hits creatures only at a fixed 50 % (StoryTools.cs:352, 378, 597-602), not the linked
  Damage Node's %, never Luminaries; Heal Instead (key `node_damage_in_healing`) is never read; the area is a 4 m ×
  scale radius (StoryTools.cs:358-363) while the Area Tool makes boxes (2.33).

### 4.12 Creature spawners (changed)
- Settings (rows): 9 Spawn Automatically (573006, 1; help 573007), 17 Reaction (574214, 0-2), 18 Respawn Time
  (574283, 0-300 step 5, 30), 19 Respawn After Killed (574285, 1), 31 Combat Tactics (0.589 key
  `npc_spawner_ai_enabled` checkbox "Enable Combat AI" → 0.695 key `npc_spawner_ai_capability` 0-2; the key hash
  changes: 0.695 0x0a34ad2a), **new** 40 `npc_spawn_weapons_equipped` Spawn With Weapons Equipped (577229; drop-down,
  P6511), **new** 105 `npc_abilities_damage_terrain` Damages Terrain (577601; checkbox, 1 — Fiery Fungus / Proximity
  Mine, 2.15), **new** 111 `npc_spawner_is_alive` (IF only), health / damage (2.14; "Damage % Multiplier" 577652 has
  text but no row → L19).
- Links: as 0.589 plus 577218, 577219, 577237, 577163 (§4.1, §4.4).
- LandmarkEmu offers rows 18, 19, 31 only (PropSettings.cs:90); spawns every spawner on Play regardless of row 9
  (StoryTools.cs:414-415); Reaction (17) not offered (faction only via Links).

### 4.13 Unchanged: Travel Point 2330, Focus Node 2898, Reset Node 2900, Starting Location 2655, signs / books / scroll
- Ids and settings as 0.589 (Focus 29 `pass_through_look_at`). Reset Node text 577085; "The story on this Build Site
  has been reset!" 577089 (CodeStringMappings `Claim.StoryReset.Message`; no client code reads the key → how live
  showed it: L22).

### 4.14 Game props (Game Table rework, TASKS 2.12) — Links only
- Score Node 3011: setting 109 `score_node_point_amount` Point Amount (577667; −100..100, default 1). Sentences
  577668 ...Add points to the triggering Luminary's score., 577684-577687 ...Add points to Team 1-4., 577665 When a
  player scores..., 577666 When a Team scores... — Score Node vs Game Table ownership of these not proven.
- Victory Node 3010: 577688-577691 ...Declare Victory for Team 1-4., 577692 ...for the Triggering Team.
- Control Point 1224: 577715 ...Attempt to control. (uses the triggering entity, P6519), 577708 When control point is
  captured...; row 110 Controlling Team (IF).
- "On Game Ended" Link from the table (P6517): no sentence found → L21. The rule-set rework itself (IgGeneralSetting,
  IgRuleTypes, Game Finder) is not traced here.

### 4.15 Other props with new Links
- Fireworks Emitters 2949-2970: 577506 ...Launch this firework. (fires once, P5915). Reward Chests 3003-3008: 577598
  ...Loot this chest, 577597 When this chest is looted...; rows 104 Player looting cooldown (577599; 15-120 min, 30),
  106 `loot_chest_remaining_count` (BLOCKS_USER_MODIFICATION = 1). Owned by TASKS 3.9 / 3.11.
- "When a Luminary clicks this object..." 576311 on **every** prop (P6432, 29 Aug); Links cleared when a linked prop
  is recycled (P6431). 577091 "When a light turns on..." exists in both builds, owner not found.

---

## 5. LandmarkEmu against the 0.695 story tools

Files: LandmarkEmu/LandmarkEmulator.WorldServer/Game/StoryTools.cs (ST), PropSettings.cs (PS), Props.cs, Claims.cs,
Graveyards.cs, Creatures.cs, RightClickMenus.cs, FirstTimeEvents.cs.

| Item | Status | Where / why |
|---|---|---|
| ObjectScripting packets, Link store, caps 64 / prop, 1000 / site | built | ST:921-1037, 253, 1040-1086 |
| AddBinding result codes | partly | ST:969 sends 1 for every refusal; 0.695 needs 4 (single binding); 1 / 2 / 3 exist in both builds |
| Link kinds by prop name (lights, doors...) | partly | ST:81-116; misses sconces / orbs / panels (6.36); click event only on switches / levers (ST:101-102; 2.13) |
| Story-tool kinds | partly | ST:170-210 has 0.589 props only; none of 2657, 2659, 2701, 2901, 2940, 2945, 2976, 3000, 3010, 3011 |
| Spawner kind | partly | ST:212-218 lacks 577163, 577218, 577219, 577237 |
| Linking Area kind | partly | ST:204-209 lacks 577221, 577228, 577548, 577549, 577722, 577723; area = radius (ST:352, 358-363) |
| Story Node | partly | ST:179-184 lacks 577238 / 577239 / 577246 / 577724 / 577725; text only as Say (ST:640-642, 6.33); no fallbacks (6.34) |
| Logic Filter | partly | logic ST:676-692; no light state broadcast; Reset re-runs Play Mode (ST:592-596, 884-890) |
| Probability Filter odds | wrong (if the window shows only Odds of Winning) | ST:369-373 |
| Timer | built | ST:502-526, 843-850 |
| Damage Node | partly | Heal Instead unused; Luminaries never damaged; area fixed 50 % (ST:378) |
| Focus Node, Travel Point, Starting Location events | built | ST:540-577, 548-552, 723-738 |
| Respawn Point | wrong | Graveyards.cs:21 uses 1225; no enabled flag; 2701 unknown |
| Counting Node / Scoreboard / Combat / IF / Random / Server Time | missing | — |
| Settings window, 0.589 format | built | PS:178-235 (f32 record PS:224-235) |
| Settings window, 0.695 format | missing | f64 record, option label string |
| Settings rows | partly | PS:79-93 (0.589 table copy, gameData/EditableProperties.txt); Story Node lacks 25 (6.33) and 41 / 112; spawner lacks 9, 17, 40, 105 |
| Drop-down options | partly | PS:56-64: 3 moods, 6 stances, 7 emotes, 3 locations (launch lists: 4 / 8 / 29 / 4) |
| Setting state → client visuals (type 2) | missing | — |
| Story text | partly | Props.cs:999-1006 caps 1024, no 512 limit, no token whitelist (6.35); DisplayPropText Props.cs:81-96 |
| Question bubbles / DialogResponse | missing | — |
| Help button 74, story FTEs 28-47 | missing | RightClickMenus.cs:91-104; FirstTimeEvents.cs has no ScriptObj ids |
| Play Mode persistence | missing | Claims.cs:1377-1378 (6.56) |
| Play Mode start / stop | built | Claims.ApplyMode 1405-1421 → ST:384-439 |
| Reset = saved state, no Links | wrong | ST:884-890 stops and restarts Play Mode (spawn events fire; lights go back to *built* state, not the state saved at Edit → Play) |
| Links removed with recycled props | built | Props.cs:1279 → ST:303-307 |
| Prop rename rule | partly | Props.cs:985 reads the 0.589 PropDefs copy (Logic Filter 2325 renameable only in 0.695) |

---

## 6. Server spec

### 6.1 Build gating
- Keep two vocabularies: the 0.589 set (today's) and the 0.695 set (this file). Choose by the session's client build
  for: kinds offered (0x0D-0x0F), SupportedObjects (0x0C; skip 0.695-only props for 0.589), settings rows (load
  EditableProperties from the matching build), the settings wire format, DisplayPropText fields, setting-state
  broadcasts, help button 74, FTEs 44-47. A Link stored by a 0.695 client stays valid and runs for everyone; a 0.589
  client simply never sees the 0.695-only props (they have no 0.589 definitions).

### 6.2 Data model
- `world_prop_binding` {id, sourceId, eventId, targetId, actionId} (today). Add nothing for single-binding links:
  uniqueness is a rule (6.4).
- `world_prop_setting` {propId, key, value} (today). Keys are the EditableProperties STRING; store f64 (0.695 sends /
  shows doubles; integer values are fine as ints). Server-only rows (Desired Time 1-6, IF property / comparison /
  value, creature health / damage) get keys of their own; until L19 is answered use descriptive keys
  (`if_property`, `if_compare`, `if_value`, `npc_time_<n>`...) and send them with string labels.
- Prop text: keep exactly what the client saved (tokenised for 0.695); validate the token set (6.6).
- Claim: `play_mode` (bool) and `adventure_flags` (byte, §2 bits) on the claim row (6.56, 2.1).
- Runtime per Play session (not persisted, rebuilt from the saved snapshot): per prop On / Locked / lights / count /
  enabled / timer / creature / node bindings / question ids; per site the snapshot taken at Edit → Play.

### 6.3 Kinds (what 0x0D-0x0F return), 0.695
- Every prop: + 576311 "When a Luminary clicks this object..." (out).
- Story Node 2344: §4.1. Logic Filter 2325, Timer 2367, Probability 2345, Focus 2898, Damage 2899, Reset 2900, Travel
  2330, Starting Location 2655: as today. Counting Node 2901 and Scoreboard 3000: §4.3. Combat Node 2940: §4.4 (offer
  577213-577244 only after 577162 is bound; 577242 always). IF Gate 2976: §4.5. Random Link Filter 2657: §4.6. Server
  Time Node 2659: out 577252 + 577255.. up to Amount of Listeners. Respawn Point 2701: §4.8. Linking Area 1590 and
  spawners: §4.11, §4.12. Score / Victory / Control Point: §4.14 (with the Game Table work). Fireworks / Reward
  Chests: §4.15. Any prop with an IF-capable property also gets in 577628.
- Sort order (third field) = list order above; ids = text ids (as today).

### 6.4 Link rules
- Add (0x10 / 0x16): both props editable by the player (result 1 otherwise); the event must be in the source's kind
  and the action in the target's (refused with 1 today; no better code is known, keep 1); same Link twice → 3 (duplicate) instead of "ok"; a source event in
  {577162 Combat Node choose, 577629 IF choose} that already has a Link → **4**; a Link that would make a cycle the
  live server refused → 2 is offered by the client but the live rule is not known (today: chains are cut at 64 deep,
  ST:351) — keep allowing loops, keep the depth cap.
- Remove with the prop (built), and when either end is recycled (P6431).

### 6.5 Firing rules (Play Mode only)
- Every event runs all its Links in creation order with a **context** {triggering character (player or creature),
  triggering creature, story / focus / damage node, depth}. The triggering character is the entity that started the
  chain (P6536-P6538: an NPC walking into an area → the NPC; a player clicking an NPC → the player) and is carried
  through every Link unchanged.
- State changes fire only on a real change (Logic Filter; Counting Node, Respawn enable / disable, doors, lights).
- Logic Filter: as today + broadcast (6.7).
- Counting Node / Scoreboard: value v in [0, Max]; +1 / −1 / max / 0 move v (clamped); after a change fire
  increases / decreases, then reaches max (v == Max) or reaches 0 (v == 0). Behaviour at the limits → L23.
- Random Link Filter: "...Trigger Random Action." picks one of its 577591 Links uniformly and runs only that.
- Server Time Node: once a minute check the world hour; when it crosses Desired Time N (whole hours, 0-23) fire slot
  N for listeners 1..Amount. One fire per hour change, not per tick.
- IF Gate: "Peform IF check..." reads the bound prop's property (live runtime value) or the triggering character's
  team / disposition / is-player, compares with the setting, fires If... or Else... with the same context.
- Combat Node: "perform Ability N" makes the bound creature cast its Nth ability now (target: its current target or
  the triggering character — not stated, L23); tactics: Standard = normal AI, Stationary = no movement, Do Nothing =
  no AI (combat AI off; P6161).
- Respawn Point: Activate / Deactivate set the enabled flag (+ events). Flee on the site → nearest enabled Respawn
  Point or the Starting Location; its 574557 event fires at the location used.
- Story Node: text per 6.33 / 6.34; "text expires" Duration seconds after the text is shown (per speaker); question
  (row 112 on and a bubble mode): send a response id = the node guid (any id the server can map back works; the
  client echoes it), on PropPacketDialogResponse fire 577724 (accepted) or 577725 with the responder as triggering
  character; ignore responses for nodes no longer in Play Mode.
- Linking Area: damage actions use the Damage Node of the chain (% and Heal Instead), on creatures, Luminaries or both
  by the action; Destroy / Heal Voxels go to the terrain destruction layer (hand-off #2) honouring row 102; "...parse
  every entity in this area..." fires 577722 once per entity inside, each with that entity as triggering character
  (wording; L23).
- Damage Node: % of max health of the target, heal when row 32 is on; creatures it hurts drop no loot (P5768).

### 6.6 Text
- 0.695 Story Node / sign text: accept up to 512 visible characters (strip tokens before counting); allow only the
  tags Flash produces here (`~[~FONT COLOR="#xxxxxx"~]~`, `B`, `I`, `U`, `P`, their closers, `~a~`, `~q~`); drop
  anything else (covers 6.35's "no HTML"). Send the stored text unchanged in DisplayPropText / chat; the 0.695 widgets
  de-tokenise. For 0.589 viewers strip the tokens to plain text.

### 6.7 Prop state broadcast (0.695 viewers in range)
- UpdatePropInteractionState, behaviour type 2, key = KeyHash(row STRING) (PS:164-175), state.value = the value:
  Logic Filter rows 107 / 108, Counting Node / Scoreboard row 33. Send on Play start (initial values), on every change,
  and to players arriving in range / finishing a re-zone (not during one). Coalesce: one message per prop per tick
  with the latest value. On leaving Play Mode send the Edit values (count = Starting Count, lights off).

### 6.8 Play Mode start / stop / reset / restart
- **Edit → Play** (owner F5 or ClaimsBase 0x43): snapshot every story prop's state on the site (lights / doors as
  set in Edit, lock state, settings: start counts, enabled flags, lights off), then start: spawners with Spawn
  Automatically (row 9) spawn (their "spawns" events fire — Play start is the story's start), timers idle, counts =
  Starting Count, Respawn Points = Enabled setting. Persist `play_mode = true`.
- **Play → Edit**: stop timers, despawn story creatures without loot (P4349, built), restore the snapshot, clear
  question indicators, persist `play_mode = false`.
- **Reset Node** ("...If in Play Mode, reset the story"): after the current chain finishes, restore the snapshot
  silently — no Links fire for the restoring changes (P5976), Logic Filters reset (P5975) — then restart as on Play
  start. Tell players on the site 577089 (route: L22). Whether the restart's spawn events fire is not stated (L23);
  today they do.
- **Server start**: sites with `play_mode = true` start as on Edit → Play from their saved Edit state (6.56;
  P4351 "Server reset should no longer make creatures stop pathing").

### 6.9 Settings window (0.695)
- EditSettingsReply with f64 values and option strings; rows per kind from the 0.695 table (§4); IF Gate and Server
  Time Node rows built by the server; Combat Node ability rows once bound; read-only rows (BLOCKS_USER_MODIFICATION)
  shown but changes refused; the state rows 107 / 108 / 110 / 111 are not offered in the window (the Logic Filter has
  no window, help page), while 103 Enabled and 33 / 35 are normal settings.

### 6.10 Help
- Right-click menu: add RadialMenuButtons 74 "Open Help" for props in PropHelpWindowMapping (0.695: 2899) — the
  client opens the page itself (Prop.OpenHelpWindow FUN_1435fbc80). First placement of each story tool by a player:
  trigger its ScriptObj first-time event (28 Logic, 29 Path / Travel, 30 / 46 Probability, 32 Text, 33 Timer, 39
  Damage, 40 Focus, 41 Reset, 44 Respawn, 45 Combat, 47 Server Time) through FirstTimeEvents.Trigger. Pages and
  positions: the live choice isn't in the client (PropHelpWindow gives Large / CenterR for the Damage Node).

---

## 7. Gaps (TASKS §2 style, continuing after 2.16)

- [ ] **2.17 Build-gated story vocabulary**: two kind sets (0.589 / 0.695) chosen per session; 0.695-only props never
      offered to 0.589; reused ids 557290 / 565055 / 577092 / 577213-577216 only on 0.695 (§1.1, §6.1).
- [ ] **2.18 0.695 settings window**: f64 record values (FUN_142c15ee0), option label strings (FUN_142c1eae0),
      0.695 EditableProperties rows (46; row 31 key renamed, 14 step 1, 33 / 35 ranges 0-99) loaded for 0.695
      sessions; read-only and IF-only rows (§6.9).
- [ ] **2.19 Prop state broadcast** (type 2): Logic Filter lights (107 / 108) and counter digits (33) for 0.695
      viewers, initial + on change + late joiners, coalesced (§6.7).
- [ ] **2.20 AddBinding result codes**: 4 single binding (Combat Node 577162, IF Gate 577629), 3 duplicate, 1
      permission (§6.4).
- [ ] **2.21 Story text 0.695**: tokenised rich text stored and passed through, 512 visible characters, tag
      whitelist, plain text for 0.589 viewers; replaces the 1024 cap in Props.cs:1005 (with 6.35).
- [ ] **2.22 Question bubbles**: DisplayPropText question flag + response id; PropPacketDialogResponse (0xED / 0x1D)
      handler → Story Node 577724 / 577725 with the responder as triggering character (row 112).
- [ ] **2.23 Story Node lists and new rows**: Mood 4 / Stance 8 / Emote 29 / Text Location 4 options (launch lists,
      §4.1), Weapon Visuals (41) with creature equip / sheath, Announce Emotes in Chat (24), text-expires event.
      Option values: L18.
- [ ] **2.24 Respawn Point 2701**: replace Graveyards.cs:21's 1225 with 2701, Enabled flag (103) and its Links,
      nearest enabled point or Starting Location on Flee, 574557 at the point used.
- [ ] **2.25 Linking Area 0.695 actions**: Luminary / both damage variants (577221, 577228), node % and Heal Instead
      instead of the fixed 50 %, parse-every-entity pair (577722 / 577723), creature leaves on despawn (P6192).
- [ ] **2.26 Damage Node Heal Instead and Luminaries** (row 32 is never read; players are never hurt / healed).
- [ ] **2.27 Help**: Open Help radial button 74 for PropHelpWindowMapping props; ScriptObj first-time events on first
      placement (§6.10).
- [ ] **2.28 Models for 0.695-only story props** (§3: 1727, 1728, 1736-1739, 1741, 1744, 1745; by Models.txt names —
      verify in the client before relying on it).
- [ ] **2.29 Spawner rows 9 / 17 / 40 / 105**: spawn on Play only with Spawn Automatically; Reaction drop-down;
      Spawn With Weapons Equipped; Damages Terrain (with 2.15).
- [ ] **2.30 Probability Filter odds**: if only "Odds of Winning" is shown (help page), odds = value %, not
      a / (a + b) (StoryTools.cs:369-373) — check what the 0.589 window shows first.
- [ ] **2.31 IF-capable runtime properties**: keep count, lights, enabled, controlling team, is-alive (and door /
      light state, L19) readable by the IF Gate; triggering-character checks (team, disposition, is player).
- [ ] **2.32 Play Mode snapshot and silent reset**: snapshot at Edit → Play, restore on Reset without firing Links,
      Logic Filters reset, message 577089 (§6.8); today Reset = stop + start (StoryTools.cs:884-890).
- [ ] **2.33 Linking Area shape**: check whether the placed area carries per-axis scale (Area Tool box) and test
      inside-ness against the box instead of the 4 m × scale.X radius (StoryTools.cs:352, 358-363).
- [ ] **2.34 Triggering character through every chain** (needed by IF Gate, Damage on Luminaries, Score Node
      "triggering Luminary", Victory "Triggering Team", Control Point "Attempt to control").
- [ ] **2.35 0.695 copies of PropDefs / EditableProperties / Models in gameData** for 0.695 sessions (Logic Filter
      rename, new rows, new props); today gameData holds the 0.589 tables.
- [ ] **2.36 Counting Node / Scoreboard, Combat Node, Random Link Filter, Server Time Node, IF Gate runtime** per
      §6.5 (the per-prop halves of 2.2, 2.3, 2.7, 2.8, 2.9, 2.11).

---

## 8. Open leads added to OPEN-LEADS.md

L18 EditablePropertyValues.txt (drop-down option values and defaults) · L19 server-only setting rows and labels (IF
comparisons, Desired Time, creature health / damage, Combat Node ability rows, door / light IF properties) · L20 how
the client hides story tools in Play Mode, shows the Scoreboard and the Respawn Point's enabled look · L21 placement
of Score / Victory Nodes, the Game Table's "On Game Ended" Link, the Audio / Group Node models · L22 how "The story on
this Build Site has been reset!" reached players · L23 unstated firing details (counter at its limits, reset spawn
events, parse-every-entity, Combat Node ability target).

---

## 9. Hand-offs

- **#3 packets**: 0.695 EditSettingsReply record f64 (FUN_142c15ee0 vs FUN_142b7c740) and drop-down option + string
  (FUN_142c1eae0 vs FUN_142b85320); PlayerUpdate 0x78 DisplayPropText + u8 question + u64 response id (dispatch
  FUN_142cdfa60 case 0x78, handler FUN_142f859b0); new C→S PropPacketDialogResponse 0xED / 0x1D {u64, bool}
  (FUN_1435fbf80 / FUN_1435fc030); ObjectScripting AddBinding response result as int (FUN_142f47400 →
  FUN_142fee7e0); ClaimInfo record mode +0x370 → +0x378 and the adventure-flags byte +0x380 (FUN_142d99b70,
  FUN_142dd2480) + ClaimsPacketSetAdventureFlags; confirm the S2C UpdatePropInteractionState type-2 body (applied by
  FUN_1430ac970: state.value / previous → ClientPropSettingBehavior).
- **#5 functions**: story-side new / changed functions traced here — FUN_1430a9260 (counter digits, new),
  FUN_1430aa110 (logic lights), FUN_1434777a0 (setting setter), FUN_142ece660 (question indicator), FUN_142dd2480
  (Play Mode Rules data source, bit layout above; TASKS 2.1 still needs its error paths).
- **#7 items**: items that place 2940 / 2976 / 3000 / 2657 / 2659 / 2701 / 2901 (innate, 50 Cut Plain Wood per the
  wiki), Fireworks Emitter items and recipes (577462-577504), Reward Chests.
- **#2 terrain**: Linking Area Destroy / Heal Voxels (577548 / 577549) and row 102 need the site destruction layer
  with timed heals (heal only player-set voxels; pending heals not cancelled).
- **Coordinator**: Game Table rule-set rework, Game Finder (TASKS 2.12) and Play Mode Rules (2.1) beyond the bits
  above were not traced here; NPC weapon equip / sheath visuals (Story Node 577239 / spawner 577237, row 40) need the
  NPC equipment packet (not story-specific).
