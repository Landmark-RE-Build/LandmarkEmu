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
| Emotes, moods, stances, walk stances, /playdead | RequestAnimationSelection | the same to the others; poses and moods remembered (Told + 1 s scan) — **state** for poses / moods | 80 m | IntroducePose after the full character; scan | OK (6dc778d) |
| /sit, /stand | client sit flag | movement word bit 0x2000000; stances clear it (0x26/0x65 clear-sit) | landscape | last full body | OK (954576e) |
| Tool swings (pick, axe, sickle, extractor, dynamite) | CastSuccess Enter / Release | AbilityPlayAnim Enter, Release 350 ms later, facing the target | 80 m | one-shot | OK |
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
| Guild tag under the name | — | nothing (not built) | — | — | Lead (OL-1 below) |
| Membership flag | — | AddLightweightPc bool; MemberStatus on a change | landscape | introduction | OK |
| Chat bubble colours | own entity | CommandPacketSetChatBubbleColor to the landscape | landscape | full character (+0x8..+0x10) | OK |
| Say, shout, emote lines, bubbles | the server's echo | ChatBase to listeners; the bubble is the client's own from the line | 100 m / 500 m / 80 m | one-shot | OK |
| **Health bar (Life)** | Life resource | Life resource (Resources.txt PACKET_BROADCAST_RANGE 30) — **state** | 30 m | introduction; **catch-up once a second for viewers within 30 m whose copy differs** | **Fixed 25db190**: a viewer who walked up saw the bar from when it was last near |
| Death | death window, knocked-out handling (LocalProxiedCharacter +0x3f0) | KnockedOut (kill feed) and RevivePoints to the landscape | landscape | RevivePoints on introduction | OK for what the server sends; the body's pose on observers: lead OL-2 |
| Revive by another player | — | StartRevive / StopRevive near the body, ReadyToRevive, then a re-show | 100 m | re-show | OK |
| **Respawn (Revive Here, graveyard, dev revive)** | alive | Life; **RevivePoints back to 0 for the landscape** | landscape | — | **Fixed e29de97**: the others kept offering "Revive Player" on a living player |
| Consumable buffs | buff bar, stats | nothing: the buff is an own-bar icon (EffectStart type 0 has no visual) | — | — | OK [I] |
| Calculated stats (light radius, run speed, harvest ring) | UpdateCalculatedStats | none after the full character: CalculatedStatDefinitions has SEND_TO_OTHERS 0 on every row [P] (gear-stats-695.md) | — | the full character carries a snapshot | Lead OL-3 |
| Pets | — | the pet NPC, its movement, removal; re-zone Reintroduce | 120 m / drop 160 m | streamed | OK |
| Fireworks | — | Fireworks packets at launch | 300 m | one-shot | OK |
| Campfire, Repair Bot | props | prop stream | PropStreamRange (400 m) | streamed | OK |
| Translocation pad effects | TeleportPlayEffectRequest | PlayCompositeEffect on the player | 100 m | one-shot | OK |
| Travel (Spire, sites, caverns, friends) | re-zone | RemovePlayer on the old landscape, introduction on the new one at the drop | landscape | introduction | OK |
| Voxel building edits | own effect | VoxelConstruction ResponseObserved (not the builder) | 100 m | one-shot | OK [P] (leads-L33-L35-L36 §L36) |
| Build cursors (box, sphere, selection, previews) | own cursor | WorldEditing 0x10C newest per builder, Viewers set, Clear on leaving range | 160 m | the last packet to newcomers | OK; preview detail lead OL-4 |
| Linking Area destroy / heal | — | WorldEditingPlayEffect | near the area | one-shot | OK (L36) |
| Mining the terrain | own dig effect | terrain change (landscape) + the swing animation | landscape / 80 m | terrain streamed | OK for the terrain; dust effect lead OL-5 |
| Felling trees, picking plants | — | RemovePlayer of the node to clients streaming its region; regrow respawns it | streamed regions | streamed | OK |
| Prop place / move / delete | client-driven while held (ManagedPosition) | the saved prop to its viewers; nothing while held (a static prop sent control mode 0 never plays 0x7F, §4.2) [P] | PropStreamRange | streamed | OK |
| Doors, gates, levers, lights (Links) | — | UpdatePropInteractionState / prop effects to the prop's viewers | streamed | SendStateTo at stream-in | OK |
| Crafting at a station | crafting window | nothing | — | — | Lead OL-6 |
| Seats | client /sit | movement word | landscape | last full body | OK |
| Mounts, vehicles | — | none in Landmark (AddLightweightVehicle unused) [I] | — | — | n/a |
| Group / guild membership markers | group / guild windows | group and guild packets to members only | members | — | n/a (not an observer view) |
| Play Mode, site games | — | Play-mode prop visibility to the landscape; game packets to players | landscape / game | per module | OK as traced (instanced-games.md) |
| /hideme | — | re-show: removed from everyone, introductions skipped | landscape | — | OK |

## Open leads (not found yet)

- **OL-1 Guild tag under names.** Live showed it (hotfix 7 Aug 2014, P1525: "Guild Tags appearing before your name
  (instead of underneath your name)") and the client has all of it: zone opcode **0xDF GuildTag {u64 character, string}**
  (zone client vtable +0x278 → FUN_142cdf330, reader FUN_142c4c0a0, applied by FUN_142ec2610 to entity +0x21a8, which
  rebuilds the nameplate with `PlayerNameWithGuildTag` "[*player*]<br>[*guildTag*]" / `GuildTagFormat`
  "<[*guildTag*]>"), the full character's third string (same setter), the local player's own tag from its guild's alias
  (guild record +0xa8, FUN_142ef4ba0 / FUN_142eedd60, "UI.GuildTagChanged"), and the option DisplayPlayerGuildNames
  (UserOptions +0x153c). Not found yet: which string the server put in the alias (the guild name, or a tag set by
  Lua Guild.SetTag whose XML field is GuildKeywords?) and which GuildData core string is +0xa8. Searched: decompile
  0.695 (above), trace/0695 (coverage-695.md: 0xDF "not sent"), design/forum, patch notes.
- **OL-2 A dead player's body on other screens.** The knocked-out handler of the remote character class
  (ProxiedCharacter vtable +0x3f0) is empty in 0.695 too (FUN_142ead890; LocalProxiedCharacter's is FUN_143328e80), as
  gameplay.md found for 0.589. So KnockedOut alone does not lay the body down on observers. Not found yet: what live
  sent for it (state bit 7 by UpdateCharacterState, a death animation selection, the Life resource at 0?). Next:
  read ProxiedCharacter::SetupActorGroup (FUN_142ec6e20) for bit 7 and the callers of vtable +0x488 on 0.695; a
  two-client test of a death shows what an observer sees today.
- **OL-3 Calculated stats in the full character.** CalculatedStatDefinitions marks no stat SEND_TO_OTHERS [P], yet
  the full character carries the player's current list (Light Radius with Bottled Moonlight, for one), applied with
  its side effects (FUN_142e5d990: 4 light radius, 5 run speed). Not found yet: whether a remote character's light
  radius shows anything; if it does, a buffed player met later keeps the buffed light on that observer after the
  buff ends. Next: what FUN_142e5d990's stat-4 side effect does on a ProxiedCharacter.
- **OL-4 Build preview for a newcomer.** WorldEditing gives a viewer who comes into range only the builder's last
  0x10C packet; a template preview is PreviewSetup (sub 5) followed by PreviewUpdate (sub 6), so a newcomer may get
  the update without its setup. Not checked against FUN_142fe3ae0 yet.
- **OL-5 Mining dust.** The miner's own client plays its dig effect; observers get the terrain change and the swing.
  Whether live sent observers a dig effect (the server sends ResponseObserved only for building edits, Building.Observe)
  is not found yet. Next: the 0.695 handler of ResponseObserved (FUN_143166bd0) for an operation a pick would use.
- **OL-6 Crafting stations.** The server sends no working animation or effect for a station in use; whether live did
  (prop interaction state, composite on the station) is not found yet.
- **Combat states on players** (Frenzy, Flow …) are composites with their duration sent to viewers within 150 m at the
  start; a viewer arriving during the few seconds sees none. Accepted as live-like [I]; to be checked if live's states
  lasted longer than ours.

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

## Range rule summary

| Range | Used by |
|---|---|
| landscape | spawns, movement, equipment, appearance, title, AFK, membership, chat-bubble colours, death notice, RevivePoints |
| 30 m | Life (Resources.txt PACKET_BROADCAST_RANGE) + catch-up |
| 80 m | emotes, poses, moods, swings, attacks, emote lines |
| 100 m | say, revive channel, observed build edits, pad effects |
| 120 / 160 m | pets, creatures (show / drop), platforms |
| 150 m | block stance, combat states, hit impacts, area effects |
| 160 m | build cursors |
| 200 m | heroic movement, grappling hook, swimming + catch-up |
| 300 m | fireworks |
| 400 m | prop streaming (PropStreamRange) |
| 500 m | shout |
