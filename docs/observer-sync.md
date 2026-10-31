# Observer sync: what other players see

The rule this document checks: **the acting player's own screen and every other player's screen agree** — now, for a
player who arrives later (login, coming onto the landscape), after either side's re-zone, and when an observer walks into
range. For each thing a player can do or be, it lists what the actor's own client shows, what the others must be sent,
what the server sends, and the range. Audit of 2026-10-21 against the 0.695 client (decompile addresses are 0.695
`Landmark64.exe`); [P] / [I] as in [README.md](README.md).

## How other players reach a client

- **Every player on a landscape is an entity on every other client on it.** PlayerManager.Introduce shows each pair
  once (AddLightweightPc, the visible equipment slots, the title, then the per-module catch-ups below) when both
  loading screens are down (login: 80a7469, re-zone: 6d7da11). The viewer's client then asks for the full character
  (FullCharacterDataRequest → LightweightToFullPc). A viewer's re-zone deletes every entity on its client
  (FUN_142d1ccf0 case 8), so it is introduced to everyone again at that screen's drop; the others keep their view of it.
- **Movement goes to the whole landscape** (MovementFanout), every body, so whatever the client's state word carries
  (sprint, jump, double jump, glide, fall, vault, ledge grab, strafe, the client's own /sit, the platform block) is the
  same everywhere [P, protocol.md §4.3].
- **Everything else is range-limited** (bandwidth rule) — and that is where screens drifted apart. A **one-shot**
  (a swing, an impact, a firework) may be missed by a far observer: that is what live's ranges meant. A **state** (a pose,
  heroic movement, swimming, a title, a health value) stays on the receiver's copy of the character until changed, so
  a state relayed by range needs three things: the end goes to everyone shown the start, a newcomer in range is shown
  the current state, and an introduction resets what the viewer is known to have. The table marks each state.
- **The full character overwrites what came before it.** The 0.695 LightweightToFullPc handler (FUN_142cdfa60 case
  0x5a) applies, unconditionally, the blob's customisation (FUN_142ec0cd0), chat-bubble colours (+0x219c..+0x21a4),
  **title (+0x118 → FUN_142ec5330)**, **AFK (+0x174 → FUN_142ec2c10)**, the third string as the nameplate's guild tag
  (+0xf8 → FUN_142ec2610 → entity +0x21a8), the stat lists, the movement body and, only when non-empty, the
  attachments (FUN_142e95ae0) [P]. Anything the introduction sends in its own packet must therefore also be in the
  blob.

## The table

State = kept on the observer's copy until changed. Range = who is sent it.

| What | Actor's own screen | What observers get (server) | Range | Late / re-zone / into range | Verdict |
|---|---|---|---|---|---|
| Spawn, despawn | — | AddLightweightPc at the drop of both loading screens; RemovePlayer on leave, landscape change, dead client 60 s | landscape | introduction | OK (80a7469, 6d7da11, b00e7bd) |
| Walk, run, sprint, jump, double jump, glide, fall, vault, ledge grab, strafe | client physics | the client's movement bodies (state word bits, protocol.md §4.3) | landscape | last full body in LightweightToFullPc | OK [P] |
| Riding a platform / mover | client | the platform block in type-0 bodies; the mover's own positions | landscape / 120 m | observers keep the last type 0's block | OK [P] (agent-leadsB §4) |
| **Heroic movement (Shift)** | HeroicMovementActivated on its network (FUN_142ec2740) | SetHeroicMovement {bool} (0x105 sub 2) — **state** | 200 m | introduction (in range); **end to everyone shown it; catch-up once a second for viewers who came near** | **Fixed e3172f2**: an observer out of range at the end kept the heroic run; one who came near during it saw none |
| **Grappling hook** | rope, pull | FireGrapplingHook (sub 1), TerminateGrappleHookEffects (sub 4) — rope is a **state** | 200 m | **terminate to everyone shown the hook** | **Fixed e3172f2** (a rope could stay on a viewer that had moved away) |
| **Swimming** | swim pose, water line | SwimmingUpdate (sub 3), coalesced 250 ms, quantised — **state** | 200 m | introduction (L167); **leaving the water reaches out-of-range viewers still holding a swim state; catch-up when near** | **Fixed e3172f2** (a swimmer stayed swimming on dry land for a viewer that was away when they climbed out) |
| Emotes, moods, stances, walk stances, /playdead, looping emotes (/talking) | RequestAnimationSelection | the same to the others; poses, moods and loops remembered (Told + 1 s scan) — **state** for poses / moods / loops | 80 m | IntroducePose after the full character; scan | OK (6dc778d; loops L192 (b)) |
| /sit, /stand | client sit flag | movement word bit 0x2000000; stances clear it (0x26/0x65 clear-sit) | landscape | last full body | OK (954576e) |
| Tool swings (pick, axe, sickle, extractor) | CastSuccess Enter / Release | AbilityPlayAnim Enter, Release 350 ms later, facing the target | 80 m | one-shot | OK |
| Dynamite throw, drinking a potion / brew | — (a consumable's answer animates nothing) | the throw (Class 7 Enter 41 / Release 42 → Biped_Core_Harvesting_Throw_F01) facing the target, and UsePotion 287 (Biped_Core_Utility_UsePotion_01), to the player and the others (L192 (f)) | 80 m | one-shot | OK [I clip choice] |
| Dash Soles / Roll Wraps | CastSuccess AnimationId | the evade in the cast's direction (Roll 415-477 / slide 593-600) | 80 m | one-shot | OK [I clip choice] (L168) |
| Off-hand models (shield, quiver, left gun / dagger) | slot 11 "Off Hand" | a second slot packet after the main hand's; emptied when the main hand changes | landscape | slot packets after AddLightweightPc | OK (L192 (i)) |
| Weapon attacks, charged attacks, gear abilities | CastSuccess | AbilityPlayAnim pair; strike at the let-go; gun look only with the gun in hand | 80 m | one-shot | OK (6dc778d, 21fcbf6, 6e8efef) |
| Blocking stance | Block effect + state bit 45 | UpdateCharacterStateDelta + EffectStart — **state** | 150 m | Told list; introduction | OK |
| Combat states on players (Frenzy, Flow, Burn …) | composite + buff icon | PlayCompositeEffect with the state's duration | 150 m at the start | not caught up (seconds long) | Accepted [I] — see leads |
| Hit impacts | composite | PlayCompositeEffect on the target | 150 m | one-shot | OK |
| Projectiles | server-made | Projectiles 0xEE create / delete | viewers of the shot | one-shot | OK |
| **Held items, armour, cosmetics (equipment)** | SetCharacterEquipmentSlot | slot set / unset to the player and the landscape | landscape | slots right after AddLightweightPc | OK; **fixed 0e238c4**: a worn item used up (TakeItem) stayed drawn on every client |
| Build mode puts the tool away | Unequip / Equip | the same equipment changes | landscape | — | OK |
| Appearance (Crimson Parlor: hair, face, skin, tints, size, gender) | CustomizationChange | the same to the landscape; the client rebuilds the base actor `Char_Biped_*_BaseActor.adr` on a race / gender change (FUN_1430a1e60) [P] | landscape | model id + customisation in the full character | OK |
| Dyes | — | none: item colours come from the item definition's tint alias; the server has no per-item dye | — | — | n/a |
| Temporary appearance | — | RemoveTemporaryAppearance relayed; the server never sets one | landscape | blob +0x114 = 0 | OK |
| Name | — | identity in AddLightweightPc; no renames | — | — | OK |
| **Title** | PlayerTitle | PlayerTitle to the landscape — **state** | landscape | introduction **and the full character** | **Fixed c97593f**: the full character's zero title wiped it on every observer who met the player later |
| **AFK** | own "UI.AFK1" | PlayerUpdate AFK to the landscape — **state** | landscape | introduction **and the full character** | **Fixed c97593f** (same overwrite, blob +0x174) |
| **Guild tag under the name** | own nameplate from the guild's alias (GuildData core +0xa8) | 0xDF GuildTag {u64, string} = the guild name, on every guild change — **state** | landscape | the full character's third string; the guild core's alias for the player | **Built 2026-10-26** (Social/GuildTags, OL-1) |
| Membership flag | — | AddLightweightPc bool; MemberStatus on a change | landscape | introduction | OK |
| Chat bubble colours | own entity | CommandPacketSetChatBubbleColor to the landscape | landscape | full character (+0x8..+0x10) | OK |
| Say, shout, emote lines, bubbles | the server's echo | ChatBase to listeners; the bubble is the client's own from the line | 100 m / 500 m / 80 m | one-shot | OK |
| **Health bar (Life)** | Life resource | Life resource (Resources.txt PACKET_BROADCAST_RANGE 30) — **state** | 30 m | introduction; **catch-up once a second for viewers within 30 m whose copy differs** | **Fixed 25db190**: a viewer who walked up saw the bar from when it was last near |
| Death | death window, knocked-out handling (LocalProxiedCharacter +0x3f0) | KnockedOut (kill feed) and RevivePoints to the landscape; **UpdateCharacterStateDelta set bit 7** (the body lies down) — **state** | landscape | RevivePoints + the Delta on introduction; **clear bit 7 to everyone told** when alive again | **Built 2026-10-26** (DeadBodies, OL-2; DEAD_BODIES_VISIBLE) |
| Revive by another player | — | StartRevive / StopRevive near the body, ReadyToRevive, then a re-show; the reviver plays ReviveAlly (840, Interacting 1) and ends it (841 / 837 Cancel) for itself and the others (L192 (h)) | 100 m / 80 m | re-show | OK |
| **Respawn (Revive Here, graveyard, dev revive)** | alive | Life; **RevivePoints back to 0 for the landscape** | landscape | — | **Fixed e29de97**: the others kept offering "Revive Player" on a living player |
| Consumable buffs | buff bar, stats | nothing: the buff is an own-bar icon (EffectStart type 0 has no visual) | — | — | OK [I] |
| Calculated stats (light radius, run speed, harvest ring) | UpdateCalculatedStats | none after the full character: CalculatedStatDefinitions has SEND_TO_OTHERS 0 on every row [P] (gear-stats-695.md) | — | the full character carries a snapshot **without temporary buffs** | **Fixed 2026-10-26** (OL-3: Abilities.CalculatedStatsForOthers) |
| Pets | — | the pet NPC, its movement, removal; re-zone Reintroduce | 120 m / drop 160 m | streamed | OK |
| Fireworks | — | Fireworks packets at launch | 300 m | one-shot | OK |
| Campfire, Repair Bot | props | prop stream | PropStreamRange (400 m) | streamed | OK |
| Translocation pad effects | TeleportPlayEffectRequest | PlayCompositeEffect on the player | 100 m | one-shot | OK |
| Travel (Spire, sites, caverns, friends) | re-zone | RemovePlayer on the old landscape, introduction on the new one at the drop | landscape | introduction | OK |
| Voxel building edits | own effect | VoxelConstruction ResponseObserved (not the builder) | 100 m | one-shot | OK [P] (leads-L33-L35-L36 §L36) |
| Build cursors (box, sphere, selection, previews) | own cursor | WorldEditing 0x10C newest per builder, Viewers set, Clear on leaving range; a template preview's **setup always before its updates** | 160 m | the last packet to newcomers (the preview's setup first) | **Fixed 2026-10-26** (OL-4) |
| Linking Area destroy / heal | — | WorldEditingPlayEffect | near the area | one-shot | OK (L36) |
| Mining the terrain | the swing (selection 226 from CastSuccess) and the terrain change | terrain change (landscape) + the same swing animation (AbilityPlayAnim 226) | landscape / 80 m | terrain streamed | OK (OL-5: the client has no observer dig effect) |
| Felling trees, picking plants | — | RemovePlayer of the node to clients streaming its region; regrow respawns it | streamed regions | streamed | OK |
| Prop place / move / delete | client-driven while held (ManagedPosition) | the saved prop to its viewers; nothing while held (a static prop sent control mode 0 never plays 0x7F, §4.2) [P] | PropStreamRange | streamed | OK |
| Doors, gates, levers, lights (Links) | — | UpdatePropInteractionState / prop effects to the prop's viewers | streamed | SendStateTo at stream-in | OK |
| Crafting at a station | crafting window | nothing | — | — | OK (OL-6: every Crafting reply is the crafter's window; no station or character visual) |
| Seats | client /sit | movement word | landscape | last full body | OK |
| Mounts, vehicles | — | none in Landmark (AddLightweightVehicle unused) [I] | — | — | n/a |
| Group / guild membership markers | group / guild windows | group and guild packets to members only | members | — | n/a (not an observer view) |
| Play Mode, site games | — | Play-mode prop visibility to the landscape; game packets to players | landscape / game | per module | OK as traced (instanced-games.md) |
| /hideme | — | re-show: removed from everyone, introductions skipped | landscape | — | OK |

## Former open leads (closed 2026-10-26, trace/0695/phaseE2-social.md)

- **OL-1 Guild tag under names — built.** The tag is the guild's **alias**: guild core string 2 (+0xa8, reader
  FUN_142ee4b50), the player's own nameplate (FUN_142eedd60 → FUN_142ec2610); for others 0xDF GuildTag and the full
  character's third string. The client's only request that sets an alias, Guild.Rename (FUN_1435b5f90 →
  GuildRenamePacket 0x83/3), sends the new name as both name and alias; Guild.Create sends the name alone; the 0.695
  "Tags" box is Guild.SetTag → AppDataField GuildKeywords (search keywords, 1,024 characters) [P]. So the tag is the
  guild name [P requests; I live's server]. Option "Display Player Guild Names" (default on) is the player's own.
  Server: Game/Social/GuildTags (0xDF to the landscape on create / join / leave / kick / disband, or a changed
  SocialStateForZone), Guilds.BuildData and the social service's alias = name, LightweightToFullPc.GuildTag.
- **OL-2 A dead player's body — built.** The animation network's State_Dead is set every frame from the
  knocked-out test (entity state word +0xa20 bit 7; FUN_142eb1950 at 0x142eb24de → FUN_142ea9ef0) [P], so only
  UpdateCharacterState(Delta) bit 7 lays a body down (AnimationSelection 156 "Death" would be overwritten the next
  frame; the remote KnockedOut handler is empty). The delta's apply (FUN_142eb6a10) runs the death transition
  (PlayerDeathVfx, vtable +0x490) and the revive (vtable +0x488). Server: Game/DeadBodies (others only; never the
  dead player's own client, whose 0.589 test crashed), setting DEAD_BODIES_VISIBLE.
- **OL-3 Calculated stats in the full character — fixed.** FUN_142e5d990 applies stat 4 to any character (the
  self-light, FUN_142ec5a40 → FUN_142e99950) and stat 5 to +0x740 (which PlayerUpdate ExpectedSpeed 0x11 also sets for
  remote characters only, case 0x10). With no SEND_TO_OTHERS stat a buffed snapshot never ended on the observer; the
  full character now carries the stats without temporary buffs (Abilities.CalculatedStatsForOthers).
- **OL-4 Build preview for a newcomer — fixed.** 0.695 FUN_1430838d0 case 5 builds the preview mesh from
  PreviewSetup; case 6 only moves it and FUN_143687a50 returns when the guid has no preview [P]. WorldEditing keeps the
  setup, sends it before the update that follows it, and to newcomers before the newest update.
- **OL-5 Mining dust — matches.** ResponseObserved's handler (0.695 FUN_143166bd0) has operations 0-6 only, all
  building brushes [P]; the miner's own client plays the swing selection 226 from CastSuccess (FUN_142d5dfa0 →
  FUN_143cd5e90) — the same selection observers get as AbilityPlayAnim — and the terrain change; the mining ring
  (MiningDecalVisualEffect) is the miner's own UI. No message shows a dig effect to others.
- **OL-6 Crafting stations — matches.** Every Crafting::Recipe* reply (handler in part_0077.c: RecipeStartResponse
  case 2 → FUN_142f39150 / FUN_142f37fe0) drives the crafter's window only; AnimationSelection has no crafting
  selection and station props have no "working" state packet. Observers see nothing, as live [I strong].
- **Combat states on players** (Frenzy, Flow …): composites with their duration sent to viewers within 150 m at the
  start; the full character has no effect list (blob +0x138 is the resource list, FUN_143b8aef0 → entity +0x4268;
  +0x178 → vtable +0x238), so a viewer arriving during the 2-10 s state sees none, as live could only do by sending
  the composite again [I]. Kept.

## Two-client test checklist

Two clients (TestPlayer and the TestPlayer2 test client), same landscape. "A" acts, "B" watches.

1. **Title**: A shows a title. B logs in (or re-zones) next to A: B sees A's title under the name and keeps it after A's
   character has fully loaded (before c97593f it vanished a moment after appearing).
2. **AFK**: A types /afk. B relogs near A: A's nameplate still shows AFK on B.
3. **Heroic movement**: B stands 300 m away; A holds Shift and runs towards B: when A is within 200 m, B sees the heroic
   run; A lets go of Shift after B has walked away beyond 200 m; B walks back: A runs normally on B's screen.
4. **Swimming**: A swims in a lake; B approaches from 300 m: B sees A swimming once within 200 m. B walks away beyond
   200 m, A climbs out; B comes back: A stands on land on B's screen.
5. **Grappling hook**: A fires the hook with B 150 m away, then B runs off before the pull ends: no rope is left on A
   when B looks again.
6. **Health bar**: A takes damage (a fall, a creature) with B 100 m away; B targets A from there (bar as last seen),
   then walks within 30 m: within a second B's target frame shows A's real health; after A regenerates to full, B's
   bar is full too.
7. **Revive Player**: A dies with B nearby; A picks Revive Here (or the graveyard on the same landscape); B right-clicks
   A: no "Revive Player" entry any more.
8. **Worn item used up**: A holds the last of an item that a use / recipe takes from the hand; after it is used up the
   item is gone from A's hand on both screens.
9. **Guild tag**: A creates a guild (N). Both screens show "<guild name>" under A's name at once; B relogs next to A
   and still sees it; A leaves the guild: the tag goes on both screens.
10. **Dead body**: A dies with B nearby: on B's screen A falls and stays down (death pose); B relogs next to A's body:
    still down; A picks Revive Here: A stands up on B's screen. A's own screen is unchanged (no crash).
11. **Template preview**: A drags a design preview while B walks in from 300 m: B sees the preview once within 160 m.

## Range rule summary

| Range | Used by |
|---|---|
| landscape | spawns, movement, equipment, appearance, title, AFK, membership, chat-bubble colours, death notice, RevivePoints, dead body (state bit 7), guild tag |
| 30 m | Life (Resources.txt PACKET_BROADCAST_RANGE) + catch-up |
| 80 m | emotes, poses, moods, swings, attacks, emote lines |
| 100 m | say, revive channel, observed build edits, pad effects |
| 120 / 160 m | pets, creatures (show / drop), platforms |
| 150 m | block stance, combat states, hit impacts, area effects |
| 160 m | build cursors |
| 200 m | heroic movement, grappling hook, swimming + catch-up |
| 300 m | fireworks |
| 400 m | prop streaming (PropStreamRange), trees and plants (TreeStreamRange; 160 m before 2026-11-01) |
| 500 m | shout |
