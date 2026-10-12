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
sorts by the third field). Result 0 = success; other values are shown through `FUN_142ea8330`.

## Definitions

The live server's definitions are not in the client, but every link sentence is: 291 strings, listed with their text
ids in `re/out/link_texts.tsv` (found by hashing `Global.Text.<id>` for every id). The server uses each sentence's text
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
amount / 32 heal, Focus Node 29, Story Node 37 / 24, spawners 18 delay / 19 respawn / 31 AI, flinger 4-6, platform 7-8.
Values are saved in `world_prop_setting` and used by the runtime (timer length and restart, dice odds, damage amount,
spawner respawn). Drop-downs (list B, `FUN_142b9d440`): u32 key, record, options {u32 n; u32 label text id, u32 value,
bool}. The Story Node gets Mood (Calm / Angry / Happy), Stance (calm, Kneel, Dancing, drunk, skip, sneak) and Emote
(wave, bow, cheer, cry, point, shrug, taunt) from the client's emote words (text 559123-559192); stance and emote
values are AnimationSelection ids. A creature told to perform by the node (Set mood / stance, Perform emote, Say text,
Perform everything) plays them (AbilityPlayAnim) and says the node's text (UpdatePropText) in say chat under its own
name and guid. Screenshot: `img_story_node_settings.png`.

Creature AI hooks (`Creatures.StoryWalk / StoryFaction / StoryFocus`): "...The creature walks to this location." sends the
creature of the chain (a spawner's "spawns", a Travel Point's "arrives") to the Travel Point, ahead of any fighting;
on arrival the point becomes its home and the point's "When a creature arrives here..." runs. Faction actions make it
an enemy (attacks), neutral or an ally (both leave players alone). Focus makes it stand facing the Focus Node (or the
player who set it off, when the node's "Focus on What Activated This" is on); stop focusing lets it wander again. A
story creature starting or ending a fight runs its spawner's combat Links. Story creatures are flagged interactable;
using one runs its spawner's "When a Luminary clicks this creature..." Links (the player is the activator). "Have this
creature walk along their route" runs the spawner's "When this creature is told to walk along their route..." Links
(the route is whatever Travel Points the builder links there). Not yet: weapon equip / unequip (NPC equipment visuals).

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

Dev: `story`, `storyfire <prop hex> <event>`, `storyclick <prop hex>`, `playmode on|off`. Tests:
`re/test/scenarios/links.json`, `storyrun.json`.
