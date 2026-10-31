# Story tools (prop Links and Play Mode)

Build sites can be scripted: props are linked ("When this switch turns off..." → "...Extinguish this torch.") and the
Links run while the site is in Play Mode. The client has the editor; the server supplies what each prop can do, keeps
the Links and runs them (`Game/StoryTools.cs`).

## ObjectScripting packets (base 0x111, wire `11 01`, u8 sub)

Client handler `FUN_142ea87d0`; request builders `FUN_142ea9a00`..`FUN_142ea9f00`. The editor is
`ControllerGameScripting` (game +0x31ed0; states EditingStateIdle, ItemSelect, EventSelectionMenu,
ActionSelectionMenu, ActionEventSelectionMenu), opened by Lua `Prop.SetupScript` from a prop's build-mode menu.

| C→S | Body | S→C reply |
|---|---|---|
| 0x0C SupportedObjects | — | 1 {u32 result, u32 n, n × u64}: linkable props (highlighted while picking a target) |
| 0x0D SupportedEvents | u64 prop | 2 {u64 prop, u32 result, events} |
| 0x0E SupportedActions | u64 prop, u32 | 3 {u64 prop, u32 result, action groups} |
| 0x0F SupportedActionsEventsAndDefaults | u64 prop | 4 {u64 prop, u32 result, action groups, events, default links {u32, u32}} |
| 0x10 AddBinding | u64 source, u64 target, u32 event, u32 action | 5 {u64 source, u64 target, u32 event, u32 action, u32 result} |
| 0x11 ListBindings | u64 prop | 6 {u64 prop, u32 result, links {u64 id, u64 source, u32 event, u64 target, u32 action, u32, u32, u32 list, u32 list}} |
| 0x12 RemoveBinding | u64 link | 7 {u64 link, u32 result} |
| 0x13 RemoveAllBindings | u64 prop | 8 {u64 prop, u32 result} |
| 0x14 RestoreDefaults | u64 prop | 9 {u64 prop, u32 result} |
| 0x15 PotentialLink | u64 prop, u32 flag, u32 | 10 {u64 prop, u32 result} |
| 0x16 RemoveAddBinding | u64 link, u64 source, u64 target, u32 event, u32 action | 11 {u64 link, u64 source, u64 target, u32, u32, u32 result} |

Events and action groups are `{u32 id, u32 text id, u32 sort order}` (`FUN_142ea3b00`, `FUN_142ea3840`; the editor
sorts by the third field). Result 0 = success. AddBinding's other results (0.695 FUN_142f46ed0 / FUN_142fee7e0; 0.589
FUN_142ea8330): 1 permission, 2 invalid cycle, 3 duplicate Link, 4 (0.695) the event allows one Link only
(`AdvancedScripting.UniqueBinding.BindSingle`), else unknown. RemoveAddBinding's reply only tells success from failure
(0.695 FUN_142fef9b0: "Link successfully modified." / "Link modification failure."; its unused result table FUN_142f47140
has 4 RemoveFailure and 5 BindSingle); a refused edit keeps the old Link (`StoryTools.ReplaceLink`).

## Definitions

The live server's definitions are not in the client, but every link sentence is: 291 strings, found with their text
ids by hashing `Global.Text.<id>` for every id (the server's lists are in `Game/StoryTools.cs`). The server uses each sentence's text
id as the event or action id and assigns them to props:

- by name: candelabra, chandelier, candles, candle, torch, lantern, lamp, brazier, fire, fan, emitter, glow, gramophone,
  flinger, platform, launcher, pad, switch / lever (plus "When a Luminary clicks this object..."), shutter, portcullis,
  gate, door (with lock / unlock), chest, light: events {on, off, on or off}, actions {on, off, alternate};
- story-tool props: 2325 Logic Filter (cogs), 2330 Travel Point, 2344 Story Node, 2345 Dice Roll Filter, 2367 Timer
  Filter, 2655 Starting Location, 2898 Focus Node, 2899 Damage Node, 2900 Reset Node, 1590 Linking Area;
- creature spawners (names "Spawner: …"): spawns, dies, enters / exits combat, clicked; spawn, despawn, faction,
  weapon, walk, mood / stance / emote / text, focus, damage.

Links are stored in `world_prop_binding` {id, sourceId, eventId, targetId, actionId} and removed with their props.

## Play Mode

ClaimsBase 0x43 SetAdventureStatus {u64 claim, u32 mode (2 Play, 1 Edit), u8 1} (Lua Claims.ToggleAdventureStatus,
F5). Everyone on the landscape gets ClaimInfoUpdate kind 0x13 with the mode in the record's last u32 (claim entry
+0x370; "PlayMode" when 2). The site record carries it too (record +0x348).

While a site plays, its linkable props run: on/off props switch (lights show it through their composite effect),
locks hold doors shut, Logic Filter cogs, Dice Roll (50/50), Timer (60 s), Story / Focus / Damage nodes raise their
events, Damage hurts creatures (50 %), Reset restarts the story, Linking Areas see players come and go (4 m × scale),
creature spawners spawn their creature (kind from the spawner's name) and respawn it 30 s after it dies. Switches are
clicked like stations (Command InteractRequest). Links are followed at most 64 deep per chain.

Messages to the players on the site (ChatPacketFromStringId, `Emotes.ShowLine`; the client prints neither itself):
when the owner switches Edit → Play, "The story on this Build Site has been saved!" (577105; P5276: entering Play
Mode saves the state of the site's props); when a Reset Node fires, "The story on this Build Site has been reset!"
(577089). Both keys are in the client's CodeStringMappings (Claim.StorySaveSnapshot.Message / Claim.StoryReset.Message);
when and how live sent them is inferred.

### Story tools hidden in Play Mode (`Game/Props.PlayModeVisibility.cs`)

Live hid the story tools while their site played (patch notes 11 May / 30 Jun 2016; wiki "This prop is only visible
in Edit Mode"). The client has no rule for it: the claim entry's mode (+0x378) is read only by the map label
(FUN_142d99b70 "PlayMode"), the AdventureStatus data sources, the camera / grapple / boot-ability rules and the build
tool's pick mask, and the Counting Node and Scoreboard look the same to the client apart from their mesh. So the server
removes them (PlayerUpdate RemovePlayer) when the site enters Play Mode and spawns them again in Edit Mode, and does not
spawn them for players who arrive meanwhile:

| Prop | In Play Mode |
|---|---|
| Combat, Counting, Damage, Focus, Reset, Story, Server Time (and Hour) Node; Logic, Probability, Random Link, Timer Filter; Respawn Point; Travel Point; IF Gate | hidden |
| Starting Location 2655 | shown only to players in build mode (CommandPacketBuilderMode) |
| Scoreboard 3000, Game Table 2313, everything else | shown |

A hidden Story Node can't carry a chat bubble: DisplayPropText shows a bubble only over an entity it finds, and its chat
line then has no "[name] says:" part. Prop states (counter digits, Logic Filter lights) of hidden props are not sent.

### Respawn Point look

The Respawn Point's actor uses RespawnAnimationNetwork (switch on control parameter `Active`: 0 → Idle_01, 1 →
Idle_02). AnimationSelection 848 sets Active 1, 849 sets Active 0. The prop spawn (AddLightweightProp base +0x280:
u32 n × {u32 selection, u8, u8}, played on creation) carries 848 when the point's "Enabled" box is ticked and 849
when not. A change of the box plays the selection at once with AbilityPlayAnim (upright points; it also sets the
heading, aimed along the prop's forward) or re-spawns a tilted point.

## Prop settings window (`Game/PropSettings.cs`)

Lua `Prop.SetupObject` (and SetGlowStrength) sends PropBase 0x1B EditSettingsRequest {u64 prop}. The reply 0x1C
(reader `FUN_142f098a0`, handled in `FUN_142f1b980`) is: u8 sub, u64 player, u64 prop, bool ok, properties {u32 n;
i32 key, record}, drop-downs {u32 n; u32 key, record, options}, string name, string text. A record (`FUN_142b7c740`):
string label, u32 label text id, string key, f32 value, f32 default, f32 max, f32 min, f32 step, u32 help text id. The
client stores them on the prop and builds the window (`FUN_142f18330`): a checkbox when min 0 / max 1 / step 1, a
slider otherwise. Each change is sent as UpdatePropInteractionState 0xFD {u64 prop, u32 type 2, u32 key, state {u32
value, u32, u64, ints, strings}, bool}; the key is a one-at-a-time hash of the key string (h = (c + h) × 0x401,
h ^= h >> 6; then h × 9, h ^= h >> 11, h × 0x8001; signed).

Settings per kind (EditableProperties rows): Timer 14 duration / 13 auto-restart, Dice 15 / 16 weights, Damage Node 30
amount / 32 heal, Focus Node 29, Story Node 37 / 24, spawners 18 delay / 19 respawn / 31 AI, Combat Node 31 Combat
Tactics (0.695), flinger 4-6, platform 7-8.

- **IF Gate** (0.695, no EditableProperties rows): three server rows labelled **If** (the property to test), **Is**
  (the comparison) and **Value** (a slider). "If" / "Is" are the developer's own words (forum 57987, 29 Sep 2016);
  "Value" is ours.
- **Combat Node**: Combat Tactics (row 31: Standard / Stationary / Do Nothing), which "Set combat tactics" applies to
  the linked creature. Under it the window lists the linked creature's abilities, as live did ("Ability 1, Ability 2,
  etc": P6152 / P6157, wiki "Combat Node"): one read-only text row per ability, caption **Ability N**, value the
  ability's name (e.g. a Barren Grunt: Slashing Blade 1, Slashing Blade 2, Slashing Blade 3, Blade Lunge; a creature
  with no known list: one "Basic Attack"; a Target Dummy or a harmless creature: none; Barren Protector's shield block:
  "Secondary Ability", the wiki's word). The creature comes from the spawner the node's "Choose the creature..." Link
  points at, so the list shows in Edit Mode before anything has spawned, and the node's "perform Ability N" Link
  actions are offered from the same list (Edit Mode too). Each row is a text line (max 0 / min 0; the 0.695 window
  builder FUN_142fb71f0 → FUN_1435becc0 takes the record's key string as the row's name and its label string as the
  value, both literal strings); it comes after the Combat Tactics drop-down because the client draws drop-downs first.
  The row keys `combat_node_ability_N` are the server's; changes to them are refused.
Values are saved in `world_prop_setting` and used by the runtime (timer length and restart, dice odds, damage amount,
spawner respawn). Drop-downs (list B, `FUN_142b9d440`): u32 key, record, options {u32 n; u32 label text id, u32 value,
bool}. The Story Node gets the launch-era lists (2026-10-16): Mood (Angry, Calm, Happy, Sad), Stance (Dancing, Drunken,
Kneeling, Lying Down, Sitting, Skipping, Sneaking, Standing) and 29 Emotes (Beckon ... Yell), labelled with the client's
own texts (576538-576575, 576610, 559168-559173, 571883). Every value is an AnimationSelection id, decoded from the
player network PlayerAnimationNetwork64.mrn (switch NodeDef: +6 u16 node id, +10 u16 child count, +0x20 offset to the
u16 child ids, +0x28 offset to the control input; the child order is the index order):

| Control | Switch (node) | Children in order → selection |
|---|---|---|
| Emote_Emotion | every Emotion_Switch | neutral 218 (Calm), Angry 219, Happy 220, Sad 221 |
| Stance_Index 0-6 | StanceSwitch\|Switch1 (14546) | Stand 223, Kneel 224, Placeholder, Dance 246, Sit_Chair 351, Laydown 480, AFK_States |
| Stance_Index 101-104 | StanceSwitch (14556) | (Switch1), Drunk 250, Skip 248, Skip1 249, Sneak 251 |
| Emote_Index 1-11 | Nuetral\|Switch1 (11845) | Crafting 247, Beg 228, Bow 229, Cheer 230, Congratulate 232, Cry 231, Flirt 233, Frustrated (Groan) 234, Giggle 235, Go_Away (Shoo) 222, Laugh 237 |
| Emote_Index 12-21 | Nuetral\|Switch2 (11846) | Point 238, Shrug 239, Sob 240, Talk_Long 241, Taunt 242, Wave 217, No 244, Yes (Nod) 245, Dance, Onod 260 |
| Emote_Index 22-31 | Nuetral\|Switch3 (11868) | UsePotion 287, Call (Yell) 450, Cough 449, Flex 451, Give, Sneeze 452, Thanks, Welcome 453, Yawn 454, Come_Here (Beckon) 455 |
| Emote_Index 32-33 | Nuetral\|Switch4 (11880) | Stretch 456, Hug 817 |

Talk (Continuous) is 252 (EmoteLoop 1 with Talk_Long), Talk (Stop) 253 (EmoteLoop 0). A creature told to perform by
the node (Set mood / stance, Perform emote, Say text, Perform everything) plays mood, stance and emote in that order
(AbilityPlayAnim) and says the node's text. With "Announce Emotes in Chat" (row 24) each also prints the client's own
third-person line ("[*speaker*] waves.", "... looks sad.") through ChatPacketFromStringId (ChatBase 0x13 sub 4), the
spawner prop as speaker. Screenshot: `img_story_node_settings.png` (the older, shorter lists).

The mood and stance stay (the 7 Jun 2016 notes made them last between sessions and site visits): they set the actor variables Emote_Emotion / Stance_Index, which live only in each client, so the creature
keeps them (`Creatures.StoryPose`) and sends them, mood first, to every player who sees it later (with its spawn and
full data, like Equipped weapons; nothing at Calm / Standing). They are saved on the spawner under the server keys
`server_story_creature_mood` / `_stance` in `world_prop_setting` (no window row), so every respawn, Edit → Play, a
later visit and a restart bring them back; picking the spawner up clears them, a duplicate starts at the defaults.
`StoryPosePersists` (`STORY_POSE_PERSISTS`, `true` = live; `false` = only the players there at the time see it).

Triggering character: every chain carries the entity that set it off (a player; else the creature, e.g. one walking
into a Linking Area), fixed at the chain's start and never changed by later Links; the IF Gate's character checks,
the Score Node's "triggering Luminary", Control Point "Attempt to control", reward chest loot and the Combat Node's
ability target read it. A timer's completion and a Story Node's "text expires" run in the chain that started them.

Creature AI hooks (`Creatures.StoryWalk / StoryFaction / StoryFocus`): "...The creature walks to this location." sends the
creature of the chain (a spawner's "spawns", a Travel Point's "arrives") to the Travel Point, ahead of any fighting;
on arrival the point becomes its home and the point's "When a creature arrives here..." runs. Faction actions make it
an enemy (attacks), neutral or an ally (both leave players alone). Focus makes it stand facing the Focus Node (or the
player who set it off, when the node's "Focus on What Activated This" is on); stop focusing lets it wander again. A
story creature starting or ending a fight runs its spawner's combat Links. Story creatures are flagged interactable;
using one runs its spawner's "When a Luminary clicks this creature..." Links (the player is the activator). "Have this
creature walk along their route" runs the spawner's "When this creature is told to walk along their route..." Links
(the route is whatever Travel Points the builder links there).

Weapon visuals (0.695): "...Have this creature equip / unequip their weapon." (577074 / 577075), the spawner's "Spawn With
Weapons Equipped" (row 40) and "...Set this creature's weapon visuals." (577237, from a Story Node's row 41 "Weapon
Visuals" and its "...Activate the weapon visuals on this node." / "When the weapon visuals on this node activates...")
play AnimationSelection 352 (Equipped: WeaponIdle 1, Class 1, NPC 1) or 353 (Sheathed: WeaponIdle 0, Class 0, NPC 1) on
the creature with AbilityPlayAnim (`Creatures.StoryWeapons`). WeaponIdle picks the network's weapon-ready idle
(StandPoses|Idle_Stand_2). Only a change is sent; an equipped creature gets 352 again when a player newly sees it and
after its full NPC data. The weapon model goes with it through EquipmentSetCharacterEquipmentSlot when
`CreatureWeapons` is on (default; Sheathed empties the hand, gameplay.md "Creature weapons"). Which model is a best
guess (research/trace/0695/agent-story.md, agent-storyhide.md): nothing in the client says which weapon model each
creature held — the creature models carry none and no table ties a creature to a `Wield_*` model — so live's
per-creature weapon was server data that was never shipped; the weapon type comes from each creature's palette icon.

Moving props (`Game/PropAnimations.cs`): doors, gates, portcullises, shutters, levers / switches and fans play the
client's SimpleAnimation sets (SimpleAnimationSets.txt: 1 door +90y, 7 door -90y, 2 trap door, 3 portcullis, 4 secret
door, 12/13 gate, 14 lever, 15-17 shutters, 18/19 sliding doors, 5/6 fans; index 0 open / on, 1 close / off) through
UpdatePropInteractionState behaviour type 0 {u64 prop, u32 0, u32 set, state {u32 index, u32 index, u64, ints [set,
0, 0, 0], strings}, bool}. The client registers behaviours 0 simple animation, 1 composite effect, 2 setting, 3
Morpheme request, 4 restriction, 5 graphics. Which set a prop uses is chosen by its name. In Play Mode these props
start shut (switches off) and move when switched; leaving Play Mode shuts them.

Starting Location (2655): travel to a player's site ("My Sites" travel, the death window's Build Site respawn, dev
`tpclaim`) lands on it (`Claims.ArrivalOf`), and in Play Mode its "When a Luminary teleports to / respawns at this Build
Site..." Links run once the re-zone has finished (`Teleport.FinishRezone` → `StoryTools.PlayerArrived`). Story light
changes skip players in the middle of a re-zone.

Dev: `story`, `storyfire <prop hex> <event>`, `storyclick <prop hex>`, `playmode on|off`. Links and story runs were
checked in a running client with scripted runs (the harness is not part of this repository).
