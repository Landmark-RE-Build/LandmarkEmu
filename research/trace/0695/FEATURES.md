# Landmark feature list: how each system worked, and what LandmarkEmu does

One section per system. For each feature: **How it worked** (the live game, launch 2016 to Feb 2017, with sources:
`wiki:` page, `notes:` patch-note date, `client:` the 0.589 / 0.695 client), **Server** (what LandmarkEmu does today,
with the file), and **Gap** (→ an item in TASKS.md, or a lead Lnnn in OPEN-LEADS.md). Statuses: ✅ built · 🟡 partly
· ❌ missing · — no server work (client only, with the proof) · 🆕 0.695 only (a tag next to the status). A ✅ row that
says "untested in game" is built but has no in-game check on record (TESTING-NEEDED.md).

Progress of the whole-wiki cross-reference: trace/0695/wiki-systems.md (ticked pages are covered here).

Statuses checked against the code 2026-10-17, second pass late on 2026-10-17 against client-0.695 HEAD 936f360 and the
day's MODLOG (trace/0695/agent-features-audit.md; the second pass: trace/0695/agent-bookkeeping.md).

---

## Story tools and Links

**How it worked** (wiki: Linking and Triggering, Story Tools, each node's page; notes 25 May - 11 Oct 2016):
props with an interaction, lights and story tools can be linked: an outgoing **event** of one prop ("When this door
opens...") runs an incoming **action** of another ("...Light this candle."). Links run only in **Play Mode** (the
pause / play icon top right, for those with access); **Edit Mode** pauses them. Switching Edit → Play saves the
site's story state; the Reset Node restores it. Story tools are innate, cost 50 Cut Plain Wood (Starting Location
free), can be tinted, have a Name setting only the owner sees, and are hidden in Play Mode (Scoreboard and Starting
Location excepted). Areas (the Area Tool) can be linked like props.

| Feature | How it worked | Server | Status |
|---|---|---|---|
| General rules: innate, 50 Cut Plain Wood, tintable, owner-only Name, hidden in Play Mode (Scoreboard and Starting Location excepted) | as above | recipe cost from the data; renaming from ALLOW_USER_RENAME (2.16); tint Props.SetTint; the server sends nothing for the Play Mode hiding | ✅ Play Mode hiding: Props.PlayModeVisibility (e-playmode, harness-checked 2026-10-17; the client has no Play Mode rule for props, the server leaves hidden tools out) |
| Links, Play / Edit Mode | as above; link editor through ObjectScripting packets | StoryTools.cs (Handle, Add, PlayModeChanged), Claims.SetAdventureStatus | ✅ (harness on 0.589; the 0.695 Links window checked) |
| Toggle props (lights, doors, gates, portcullises, switches, emitters, fans, platforms, pads, launchers, flingers, gramophone, shutters) and lockable doors / chests | on / off / toggle; lock / unlock | StoryTools namedKinds, Execute; mover props (Translocation Pad, flinger, launcher, moving platforms) placed as their placement-pack parts with their client areas (Props.Movers.cs, Props.Platforms.cs; 22dd72f, E.1) | ✅ (harness on 0.589; the 6.36 sconce / emissive / control-panel kinds untested in game; movers: platform legs harness-checked 2026-10-17, riding one is L135, pads / flinger / launcher untested in game) |
| "When clicked" on every prop | notes 29 Aug 2016 | StoryTools clickOnlyKind, FlagClickable | ✅ 🆕 2.13, untested in game |
| Signs, books, scrolls show text | wiki Story Node tips | StoryTools (2939, 2228, 2229, 2639) | ✅ untested in game |
| Story Node | creature(s): weapon visuals (Equipped / Sheathed), mood (Angry, Calm, Happy, **Sad**), stance (8), emote (29), text; text-only: on screen / chat / bubble / bubble + chat, 1-20 s, bold / italic / underline, 20 colours; "Announce Emotes in Chat"; events for everything / text / text **expires** / mood / stance / emote / **weapon visuals**; response buttons "Is Question?" with accept / deny events (11 Oct) | StoryTools (ActivateNode, Perform, SayNodeText, AskQuestion, DialogResponse; everything, text, mood / stance / emote with the full launch lists, text expires, formatting, response buttons, announce lines); Weapon Visuals row 41 and its action / event 577237-577239 (2026-10-17, E.11; Creatures.StoryWeapons, AnimationSelection 352 / 353) | 🟡 weapon visuals built 2026-10-17, untested in game: the stance only, no weapon model on the creature (both client carriers proven, live's per-creature weapon data was server-only: agent-storyhide.md, L95). Mood / stance / emote play only on player-network creatures; the others have no such animations (L117 closed, proven). Lists and announce lines untested in game; text bubbles / announce lines come from the creature itself for clients that have it as a full entity (L96, harness 2026-10-17). |
| Logic Filter | two lights (blue, yellow), 9 actions, 8 events; only state *changes* fire events | StoryTools (SetCogs; 0.695 Blue / Yellow Light wording) | ✅ (0.695 test; the silent reset untested in game) |
| Probability Filter (was Dice) | "Roll the probability dice!"; wins / loses at "Odds of Winning" % | StoryTools (DiceOdds) | ✅ (0.695 window shows "Odds of Winning"; the rename is client text) |
| Timer Filter | 1-600 s, Loop Until Paused; start / pause / reset / pause-and-reset; completes / starts / pauses / resets (reset only on an explicit reset) | StoryTools (TimerLength, TimerAutoRestart) | ✅ 2.16, untested in game: the loop and the reset-event rule (the window checked on 0.695) |
| Focus Node | creatures look at the node, or at what activated it ("Focus on What Activated This"); activate / deactivate | StoryTools + Creatures.StoryFocus | ✅ untested in game ("Focus on What Activated This", 2.34) |
| Damage Node | % of max health, "Heal Instead"; a creature, or an area (creatures / Luminaries / both); damaged creatures drop no loot; 0.695 "Damage % Multiplier" | StoryTools (DamageOf, HurtCreature, HurtPlayer); the multiplier is a spawner row | ✅ 🆕 2.25 / 2.26, untested in game |
| Reset Node | in Play Mode, everything back to the state saved on Edit → Play; doesn't fire Links; resets Logic Filters | StoryTools (ResetStory; silent restore, 2.32) | ✅ untested in game; "The story on this Build Site has been reset!" (577089) sent on reset (790f139) and "... has been saved!" (577105) on Edit → Play (88fa629); the carrier is ours (L22 closed) |
| Starting Location | one per site, placed free in the NW corner on new sites, facing arrow, events: teleports here / respawns here; can't be disabled | Claims.EnsureStartingLocation | ✅ (harness on 0.589; one-per-site 6.37 and facing on arrival untested in game) |
| Respawn Point / Location | many per site; Enabled setting; activate / deactivate actions and events; "Flee" revives at the nearest enabled one or the Starting Location; event fires only at that location | StoryTools (kind 2701, RespawnSite), Graveyards | ✅ 🆕 2.10 / 2.24, untested in game |
| Counting Node | 0-99, Starting Value, Max Count; +1 / −1 / max / 0; reaches max / 0 / increases / decreases; shown on the disc | StoryTools (CountingKind, EditCount, QueueState) | ✅ 🆕 2.2 (0.695 test; the Edit-Mode digits, 2.19, untested in game) |
| Scoreboard | Counting Node visible in Play Mode | StoryTools (same runtime as the Counting Node, kind 3000); Props.PlayModeVisibility (story tools hidden in Play Mode, Scoreboard kept: P6182) | ✅ 🆕 2.3 / E.17: hide / show harness-checked 2026-10-17 (trace/0695/agent-playmode.md) |
| Random Link Filter | any number of "Assign potential action" links; "Trigger Random Action" picks one evenly | StoryTools (RandomTrigger) | ✅ 🆕 2.9, untested in game |
| Server Time Node / Hour Node | 1-6 listeners, whole game-time hours (0-23); "When game time in slot 1 is reached", "When time 2..6 reached"; game time even if the site has a custom time | StoryTools (ServerTimeReached, GameHour), PropSettings DesiredTime | ✅ 🆕 2.8, untested in game (dev-command check only); the Desired Time rows are ours (L19) |
| Combat Node | links to one creature; Combat Tactics (Standard / Stationary / Do Nothing) on the node and on spawners; "Perform Ability 1..6"; Enable Combat AI Movement; Spawn With Weapons Equipped | StoryTools, Creatures.StoryUseAbility; each creature's ability list (ranks 1-3 and its special, up to 6) in Combat/CreatureAbilities.cs (2026-10-17, E.12) | ✅ 🆕 2.7, untested in game; the settings window lists the linked creature's abilities as read-only "Ability N" rows below Combat Tactics (70f7d05, L19 closed; row keys and wording ours). Ours: rank damage factors, four ability lines with no creature yet, the AI using rank 1 only (agent-abilities.md §4.3 E1-E4) |
| IF Gate | "Choose the prop to use in the IF condition", property (Current Value, Blue / Yellow Light, Controlling Team, Is Alive, open / lit...), comparison, value; If... / Else...; triggering character's team, NPC disposition, real player | StoryTools (IfHolds), PropSettings ifGateRows | ✅ 🆕 2.11, untested in game; the "If" / "Is" row labels follow a dev post (88fa629); the other comparison words and the trigger-check labels are ours (L19 closed) |
| Linking Area | Luminary / creature enters / leaves; damage in area; 0.695: Destroy / Heal Voxels in this area, Allow Voxel Destruction (site-wide / allowed / blocked), "For every object in this area... parse every entity" | StoryTools (enters / leaves / damage, CreaturesIn), VoxelDestruction.AreaAction, AreaDefinitions.cs | ✅ 🆕 2.6, untested in game: the area Destroy / Heal (dynamite allow / refuse checked on 0.695) |
| Creature spawners | spawn / despawn, faction, equip / unequip, walk route, mood, stance, emote, say, focus, damage; events spawns / dies / combat / clicked / told to walk; 0.695: health / damage settings, combat tactics, weapon visuals | StoryTools spawnerKind, Spawn; Creatures.SpawnStory; PropSettings; Equip / Unequip weapon Links 577074 / 577075 and row 40 "Spawn With Weapons Equipped" (2026-10-17, E.11) | 🟡 2.14: weapon Links and row 40 built, untested in game; left: the weapon model (the stance only, live's per-creature weapon data was server-only: L95, as the Story Node row). Health / damage % (2.14) and duplicates (6.30) untested in game. Story creatures path around player walls, holes and props on sites (73dc97c, SitePathing on; harness round 60) |
| Travel Point | creature arrives / walk here | StoryTools (CreatureArrived) | ✅ (harness on 0.589) |
| Fireworks Emitter | "...Launch this firework." (fires once when triggered) | StoryTools (LaunchFirework), PropEffects.cs | ✅ 🆕 3.9 (0.695 test: the client loaded the firework sounds at each launch) |
| Reward Chest | "When this chest is looted...", "...Loot this chest" | RewardChests.cs (Loot), StoryTools | ✅ 🆕 3.11, visitor loot untested in game: the settings window and owner refusal checked on 0.695; tier amounts ours |

## Site games (Game Tables)

**How it worked:** at launch (0.589) the Game Table carried rule sets for Deathmatch, Team Deathmatch, Zone Control
and Adventure (PvP itself had been removed from the open world before launch; wiki: Player vs. Player). The 29 Sep
2016 rework (notes; 0.695 IgGeneralSetting / IgRuleTypes rewritten) gave one Game Table with: a name, teams (number,
names, allow players, allow team switching), Players needed to start, Time Limit, Score On Enemy Kill, Player / Team
Score Victory (Victory Points Required), Control Points (update rate, points), per-team Victory Messages, and a Game
Props tab with Team Spawners (blue, red, yellow, green), Neutral Spawners and Control Points. The Score Node and
Victory Node tie scoring and victory into Links. /gamefinder (or the Landmark menu) lists running games across worlds
(14 Oct 2016).

| Feature | Server | Status |
|---|---|---|
| 0.589 rule sets: Deathmatch, TDM, Zone Control, Adventure; HUD, scoreboard, observers, queues; the client's Game Table editor | InstancedGames.cs, .Editor.cs, .Queue.cs | ✅ (harness on 0.589) |
| 0.695 Game Table settings and rule types | InstancedGames.Rework.cs (SettingKeyHash, AddOrRemoveRule, GetConfiguration) | ✅ 🆕 2.12: teams, join / start / spawn, HUD "Top Score" and the end banner checked on 0.695; settings-window clicks and team switching need a hand test; the site-game block's trailing u32 is the team count, already sent (L37 closed) |
| Score Node, Victory Node, yellow / green team spawners, Control Point "Attempt to control" / Controlling Team; the table's "On Game Started" / "On Game Ended" Links (573010 / 573011) | InstancedGames.Rework.cs (ScriptPropRows, GameStartedEvent / GameEndedEvent, RaiseTableEvent), StoryTools, InstancedGames.cs | ✅ 🆕 2.12 / E.11, untested in game: "On Game Started" / "On Game Ended" built 2026-10-17 (the winner is the triggering character, ours); the owner of 577665 / 577666 / 577668 is inferred (L21 closed: everything the client can show is built); Score was only exercised by the `gamescore` dev command |
| Game Finder (matchmaking packets), across worlds | InstancedGames.Finder.cs, social matchmaking (IMatchmakingService) | ✅ 🆕 2.12 / 3.25: counts and "Match found" across worlds checked on 0.695; joining a match on another world built (1c039b1, e-travel), optional way back home behind FINDER_RETURN_HOME (cadb157, off: no source); cross-world join untested in game |

## Play Mode Rules

**How it worked** (notes 16 and 30 Jun 2016; 0.695 text): per-site rules in Advanced Build Settings, Play Mode only,
for everyone including the owner, separate from Site Permissions: Free Camera, Orbit Camera, Flying Character Camera
(default off; only the Character Camera by default), Grappling Hook, Boot abilities, Voxel destruction (default on:
dynamite works on sites, healing after 100 s). Visitors can read the rules in Advanced Build Settings, see only the
allowed camera buttons, and get "... is disabled on this Build Site." messages. Harvesting on sites stays impossible.

**Server:** ✅ 🆕 2.1: Claims.SetAdventureFlags (0.695 ClaimsPacketSetAdventureFlags; the mask kept in the site record
and info 0x17), Claims.RuleBlocks, VoxelDestruction.cs. 0.695 test: the client kept mask 0x1 → 0x5 → 0x4; dynamite was
refused with EffectEnd 9; a crater healed after ~104 s. The client itself enforces cameras, grapple and boot abilities.

## Building (Build Mode, tools, designs, props)

**How it worked** (wiki: Build Mode, Building Tools, Add / Delete / Heal / Smooth / Paint / Fill / Line / Selection /
Area / Eyedropper Tool, Designs, Prop, Prop Palette; notes 2016): Build Mode (Tab or the tools icon) is one "mega
palette" with tabs Designs, Props, Tints, Building Tools + Materials, and (28 Jul 2016) Mesh Shapes; it can be
split into modular windows. Tools work only on your own sites or where you have build permission. Common terrain
textures (dirt, stone, sand, ice, snow) are free and unlimited (∞); everything else must be gathered and refined
first. Props are not crafted: the palette places any prop you know, costs its resources, and gives them back when
recycled; basic props are innate, others are discovered as "recipes" (gathering, combat) or bought at the Lumen
Station; props can be scaled, rotated and tinted. Designs (formerly templates) are stored on the server only, in the
categories Starter Castle, Player Studio (bought), Personal, Collected; storage 6,000,000 blocks, expandable with
lumens; placing one needs its materials (and its props, or they are left out); a design turns red outside the site.
Undo / redo 20 steps; Ctrl+C / Ctrl+V copy and paste (props too, if you have them).

| Feature | Server | Status |
|---|---|---|
| Voxel tools (add, delete, heal, smooth, paint, fill, line, selection, paste, undo / redo) | Building.cs (undo depth 20), WorldEditing.cs, BuildingClipboard.cs, Terrain/ | ✅ (harness on 0.589; heal on dug ground: client finds nothing to restore) |
| Free terrain materials, material costs | Building.cs, BuildMaterials | ✅ (harness on 0.589) |
| Designs: save, list, place, rename, delete; categories; storage 6M → 48M | Templates.cs, SiteDesigns.cs, DesignItems.cs, ItemUses.cs | ✅ (the 3.7 categories wait for the user's look) |
| Prop palette, innate / discovered props, tints, recycle refunds | Props.cs, Palettes.cs, ArtTints.cs, RecipeBook.cs (KnowsProp; founder props gated by Entitlements.PropAllowed) | ✅ (recycle refunds, 6.38, untested in game) |
| Mesh Shapes tab (30 new VoxelShapes, MeshCategory tables), Fill "Smoothing Enabled" | Building.cs (unchanged ApplyDiff path), Building.Tools.cs (tool switch 0x117 → edit effect) | ✅ 🆕 3.1 built 2026-10-16, untested in game; palette / tables / smoothing are client-only (trace/0695/mesh-shapes-695.md) |
| Buy a Design's missing materials with lumens | DesignMaterials.cs (SendOffer, Purchase) | ✅ 🆕 3.6 (0.695 test: 11 bundles bought; spec U1-U6 are our choices) |
| Paste / preview progress messages, site-wide level of detail | client | — client only: PasteThread / PreviewGenerationThread send no packet (TASKS §1); Claims.Get / SetAllowClaimWideLOD is a client-only binding (CLIENT-DIFF.md) |

## Build sites

**How it worked** (wiki: Build Sites, Build Site Management, Build Site Flag, Build Site Expansion; notes): a site is
525 × 525 × 450 blocks inside a buffer zone no other site may enter; up to 8 sites (1 to start; extras bought as
Build Site Expansions, tradeable); new players start with a site on a Pioneer Landscape. Placing: the flag (until 14
Jul 2016; then "Place this build site" in the U window), map view, mouse to move, wheel to raise / lower, confirm.
Deleting (only while on that landscape) packs everything into overflow and makes a design of the site. Upkeep is
"Active Days": 7 to start, +1 per day you log in, up to 90; three days before expiry a warning; on expiry the site
becomes a design and its resources and props come back by mail. Site Management (U): names, description, tags
(publish to the Gallery), screenshots, permissions for everyone and per player: No Touching, Play Around (default),
Pillage and Plunder, Get Inspired (copy designs: "swaps"), Let's Decorate, Ultimate Power. Storage chests: 50 per
site (notes 21 Mar 2016; the wiki says per account).

| Feature | Server | Status |
|---|---|---|
| Size, buffer, placement, delete (packs to overflow + design), 8-site cap | Claims.cs, SiteContents.cs | ✅ (harness on 0.589; height limits 6.47 and delete-as-materials 6.39 untested in game) |
| Active Days upkeep (7 / +1 per two logins, SiteUpkeep.cs:64 / 90), expiry → design + mail | SiteUpkeep.cs | ✅ (isolated-stack test, 3.18) |
| Warning before expiry (wiki: mail 3 days before; client GAQ "Upkeep expires in less than [hours] hours!" Claims.GaqExpiring 562253) | SiteUpkeep.MailWarning, Claims.cs | ✅ 3.18 (isolated-stack test; the client's GAQ notice is dead code, proof in TASKS 3.18; the letter text is ours, L87) |
| Permissions, Play Around default, specific players | Claims.cs; Get Inspired enforced by Claims.MayCopyIn (E.9, see Interface); all three Default Access masks kept and echoed, No Touching entries kept, changes sent as ClaimInfoUpdate kinds 5 / 6 (936f360) | ✅ (harness: Special Access list; 936f360 untested in game) |
| Name, description, tags, Gallery, screenshots, rating, contests, music | Claims.cs, Gallery.cs, Ratings.cs, Contests.cs, SiteScreenshots.cs | ✅ untested in game: the 6.43 gallery rules (older harness checks passed) |
| 50 storage chests per site (and 125 spawners) | PropLimits.cs | ✅ (harness) |
| Starting Location placed on new sites | Claims.EnsureStartingLocation | ✅ (harness on 0.589) |
| Place sites from the window (no flag), expansions open the Marketplace, sites on Pioneer islands (29 Sep 2016) | Claims.UsesFlags, Claims.SlotsOf; Build Site Expansion 900001 sold in the store for 1,040 Lumens (Store.cs MarketplaceProducts); "Get more build sites" opens the store on "Site Expansion" | ✅ 🆕 3.5, the store link untested in game: placing from the window checked on 0.695; 900001 is our id (L86) |
| Play Mode Rules | Claims.SetAdventureFlags (see Play Mode Rules) | ✅ 🆕 2.1 |

## Death and respawn

**How it worked** (wiki: Death, Graveyard, Respawn Point): on death, "Revive Here" for 15 lumens, or "Flee" (free)
to the nearest graveyard (red circles on the map; also deep in the Chaos Caverns, with a safe zone; never inside a
site) or, on a site, to the nearest enabled Respawn Point / the Starting Location (centre of the site, on the surface,
if neither exists). Death has no other penalty than gear damage (repair at a Repair-o-Matic or with a Repair Bot
Beacon).

| Feature | Server | Status |
|---|---|---|
| Revive here (15 lumens), flee to the site's respawn points / Starting Location, the Spire or your own site | Health.cs (ReviveCost), Graveyards.cs (SitesFor, Nearest) | ✅ (harness) |
| Graveyards: on the surface (the map's red circles, also on Pioneer islands) and deep in the Chaos Caverns with a buffer zone where monsters don't attack; Flee to the nearest one on the layer you died on; no build site on a graveyard | Graveyards.cs (layer rule: more than 100 m below ground = cavern layer), Graveyards.Landscape.cs (81 cavern graveyards per island from the Underground .cpa Graveyard01 pieces; surface graveyards from graveyard01.otd, setting SURFACE_GRAVEYARDS); Creatures.cs (30 m buffer, no spawns inside), Gathering.cs (no trees inside), Claims.CanPlace (refusal 3), Health.cs / Teleport.cs (cavern arrival without ground snap) | ✅ E.10 built 2026-10-17, untested in game (steps trace/0695/agent-graveyards.md §4; harness: 81 cavern / 32 surface on 406). The cavern graveyards' whole template graveyard_launch.otd is stamped at the .cpa placements (0fb9cc4, CAVERN_GRAVEYARD_STAMPS, on in dev since 2026-10-17; plinth seen on the test world; L47); Underground Adventure lands on a random cavern graveyard (9a4ee5a). Ours: the surface positions (no client file places them), the 100 m layer threshold, a site already on a graveyard hides it (live removed the site), the template's turn [I]; the map circle is inferred (agent-graveyards.md §3 L-a … L-j) |
| Respawn Location (0.695 activate / deactivate, Enabled) | StoryTools (kind 2701, RespawnSite) | ✅ 🆕 2.10, untested in game |
| Durability loss and repair | Durability.cs (ToolUsed) | ✅ (harness; tool wear, 6.53, untested in game) |

## Combat, gear and creatures

**How it worked** (wiki: Combat, Armor, Armor Weight, Affinity (Equipment List), Maximum Affinity Effect, Elite
Monsters, Enemy Factions, Monsters (PvE), Loot, Essence, Cozy Campfires; notes 25 May 2016 balance pass and later):
one class (Adventurer); your weapon and armour decide your abilities (melee, ranged, spells). Every weapon, armour
and accessory has an affinity (Fire, Earth, Water, Lightning, Radiance, Affliction, Shadow): matching pieces give the
Maximum Affinity Effect (the chest armour's effect only works with a weapon of its affinity; accessories make one
property happen more). Armour weight: light = small max-life bonus, large damage bonus; medium = balanced; heavy =
large life, small damage. Innate starter sets per affinity; the rest discovered or bought at the Lumen Station;
crafting needs Elemental Essences (converted from Monster / Adventure Essence at the Replicator). Monsters (chompers,
slaugs, fiery fungus on the surface and in caves...) roam the Chaos Caverns; factions (Barren skeletons, Scalewind
kobolds, Highfall, NovaTech, Ebonguard elves, Stoneskin golems) hold Outposts with an Elite each; all are also
Creature Props for build sites. Loot: chests, Lost Chests, Lost Crystals, monsters: mostly resources, plus essences,
campfires, dynamite, potions, lightstones, recipes. Cozy Campfires heal you while you stand near them (everyone
starts with a stack; 20 for 130 lumens at the Lumen Station).

| Feature | Server | Status |
|---|---|---|
| Weapon attacks, abilities, hit effects, statuses, floating damage | Combat/WeaponCombat.cs, WeaponAttacks.cs, Abilities.cs, HitEffects.cs, Combat/Statuses.cs; optional third (E) attack per kit, Desperado's Deadly Arsenal (80a386c, L163; admin-only, never craftable on live) | ✅ (harness; the three-attack kit untested in game) |
| Affinity effects (chest MAE, accessories) | Combat/Gear.cs | ✅ (chances / sizes are ours) |
| **Gear stats: max life and damage from armour weight and items** | Combat/GearStats.cs (WeightOf) | ✅ 5.1 (0.695 test: heavy armour gives ~1800 max life; the numbers are ours) |
| Creatures, factions, elites, outposts, loot | Creatures.cs, Outposts.cs, Encounters.cs, LootChests.cs, LootPinatas.cs; creature pathing round cave rock, ruin walls and cliffs (Pathing.cs, 3d5094a, CREATURE_PATHING on; 6.61) | ✅ (harness: monsters, outposts; many 6.x launch rules of 2026-10-16 untested in game; ownerless loot: E.4, L141; pathing harness-checked at outpost 0, the walk-round by eye untested in game (TESTING-NEEDED "Creature pathing"); the grid is ours, live's navmesh format unknown: L132) |
| **Projectiles you can see** (arrows, bolts, the Abomination's spit, catapult rocks, healer bolts) | Projectiles.cs (Projectiles family 0xEE: Create / Delete), WeaponCombat.ProjectileRow, Creatures.Shoot / Spit / ShowBolt | ✅ 🆕 2026-10-17 harness (probe + screenshots); rows chosen by affinity [I] (L156 / L157) |
| Outpost archers and casters shoot (dodgeable, affinity arrows / bolts, no shots through walls) | Creatures.RangedProjectileFor / Shoot (P5727) | ✅ 🆕 2026-10-17 harness |
| Weapon tooltips list their keywords (affinity, Ranged / Melee, Basic Attack) | AbilityCasterInfoReply.Keywords, WeaponCombat.KeywordsOf | ✅ 🆕 parse verified; tooltip look untested (L158) |
| Cozy Campfire (heal near it) | Consumables.cs (TryUse) | ✅ untested in game (the 6.19 launch rules: no heal in combat, full heal, airborne refusal) |
| Rank 2 / 3 abilities (0.695 texts 577260-577337): these are creature abilities chosen with the Combat Node, not weapon abilities | Combat/CreatureAbilities.cs (39 rank lines mapped to creatures), Creatures.StoryUseAbility; the ability name goes to the combat spam (CombatResourceEvent +0x40) | ✅ 🆕 E.12 built 2026-10-17, untested in game. Ours: rank damage ×1 / ×1.25 / ×1.5, specials ×1.5, 3 s shield; four lines with no creature yet (agent-abilities.md §4.3 E1-E5) |
| Roll Wraps roll (item 4406) | BootAbilities.cs (abilities 1207-1214 work) | ✅ 🆕 4.15: admin-only, as on live: Roll Wraps were never released (recipe 5331's tracker goal is missing from the Feb 2017 capture, no station, recipe item, loot table or store entry; L26 closed) |
| playdead emote | Emotes.cs (PlayDeadNow: AnimationSelection 844, stance 6; lines 577224-577227 with the "with [target]" forms; 0.695 viewers only) | ✅ 🆕 E.12 built 2026-10-17, untested in game |
| 0.695 creature props (Ebonguard set, Slaug, Abomination, Chomper, Target Dummy, Vine Trap, The Outcast, Catapult) | StoryTools.IsCreatureProp, Creatures.SpawnerKind (own models; Dummy, mine / Vine Trap and catapult with their own behaviour) | ✅ 🆕 3.13, untested in game; the other kinds are melee with name-based numbers (ours) |

## Exploration and travel

**How it worked** (wiki: Spire, Servers and Landscapes, Pioneer Landscape, Caves, Chaos Caverns, Outposts, Lost
Crystals, Lost Chests, Biomes, Astronomy, Exploring, Leyline Network, High Yield Dynamite): every world has the same
landscapes (two biomes each, plus ocean); worlds Courage, Liberation, Satisfaction (EU), Serenity, and the visit-only
**Foundations Museum** (contest winners); free travel between worlds and landscapes from a Spire's crystal or the
"Spire anywhere" crystal icon (World / Continent / Local travel, biome filter). New characters start on a flat
single-biome Pioneer Landscape (5 biomes, no Volcanic) with a site placed for them; once deleted, never again.
Surface caves (common loot chests, no monsters but fungus) and Chaos Caverns (Adventure button: deep, monsters,
player-built Outposts from contests with the outpost's name and date shown on approach, one Elite, one buried Lost
Chest, one Lost Crystal that wakes when the Elite dies: channel a few seconds, not in combat, a hit cancels; 1-2
recipes, campfires, essences, lumens; then a trip to another cavern). Party members share loot. The Leyline Network
was removed at launch. Day / night: 3 real hours per day (8× real time), sun 04:00-20:00. High Yield Dynamite blows
a crater (temporary on build sites, from 30 Jun 2016) and doesn't hurt players.

| Feature | Server | Status |
|---|---|---|
| Worlds, landscapes, Spire travel, Other Worlds, Pioneer starts | Travel.cs, Spire.cs (TravelTo), Landscapes.cs, Pioneer.cs, Teleport.cs, CrossWorld.cs; Pioneer lands are zones 464-468 as on live (0b3247c, L99); Spire floor on the ground and the deck solid after every re-zone (effe77a, 650a6ca, 6cd233a) | ✅ untested in game: cross-world travel checked on the test stack (B.10); a Spire on every landscape (6.49): arrivals on the deck harness + Frida-checked on 391 / 395 / 397 / 398 (2026-10-17), by eye in TESTING-NEEDED; Local Travel crystals off the home landscape: harness-verified on 391 (L130 closed) |
| Spire Resupply Chest (missing starter tools back, 31 Mar 2015 hotfix) | Spire.Resupply (prop 884) | ✅ 🆕 2026-10-17 harness (L161) |
| Foundations Museum (visit-only world) | Landscapes.cs (zones 431-434 left out) | — n/a: closed at launch (P4235, 21 Mar 2016; TASKS 5.2) |
| Surface caves, Chaos Caverns, Outposts, Elites, Lost Chests, Lost Crystals | Caves.cs, Outposts.cs, LootChests.cs | ✅ (the client reached outposts 0 / 40 / 70 / 80, 6.8; 10 s channel, 6.1) |
| Outpost name and builder shown on approach | OutpostSites.cs (client-only site records: the client's own walk-on banner with the outpost name and "Created by"; 9a5894e) | ✅ built 2026-10-17 (E.15 / L102); in-game look untested (TESTING-NEEDED) |
| Party shares loot in the caverns | LootChests.ShareRange, Gathering.PartyShareRange, RewardChests GroupRange | ✅ untested in game (no two-player test) |
| Day length (wiki: 8×, 3 h; sun 04:00-20:00) | WorldClock.DefaultCycleSpeed | ✅ (wiki Astronomy; the client follows the server's clock sync) |
| Teleport to a friend | Abilities.cs (same world); a friend on another world through the social service's presence and the B.10 hand-over (CrossWorld.cs, gateway ArriveNear; 7de005d) | ✅ E.13, untested in game across worlds: built 2026-10-17, needs a two-client test; the same world checked |
| Dynamite (craters, heals back) | WorldEditing.cs, Consumables.cs, VoxelDestruction.cs | ✅ (0.695 test, 2.6; sites: Play Mode Rules 2.1) |
| 0.695 Underground Adventure from Pioneer, any zone; Lost Crystal lockout | Caves.cs (TravelRandom), Outposts.cs (lazily stamped outposts per island), Outposts.Rotation.cs, WarpNetwork.cs | ✅ 🆕 trips roll a landscape then a cavern graveyard (9a4ee5a), Lost Crystal lockout, crystals on graveyard plinths, trips to other worlds (7e9af69, two-world harness: client switched to Courage and back); outpost rotation built but off (no source for its period, L165) |

## Gathering and resources

**How it worked** (wiki: Gathering, Gathering Tools, Tools, Extractor, Common Materials, Free-to-Use Building
Materials, Composite Resources, Refining, Tier, Sickle, Plant): only off build sites; the land heals about 20
minutes after it is dug; trees give Plain (Jungle, Tundra, Deciduous), Burled (Old Growth) or Striped (Desert) wood
plus Tree Fiber and Sap; picks mine stone, metal and gems (uncommon stone, ore and gems in veins, more in the Chaos
Caverns); dirt, common stone, sand, ice come along passively. Tools: Adventurer's axe and pick to start, crafted ones
with perks (Charmed: luck / recipes, Farseer: outlines veins, Hotfoot: speed), NovaTech Extractors (gather
everything and tunnel; Harvestron more yield, Holoseeker outlines, Velocitizer speed), gadgets (Ore Prospector,
Ground Sounder, Cave Seeker). Refining at the Replicator multiplies (50 raw wood → 750 cut); composites combine
refined resources (metals, gems, cloth, plastic, lumicite). Sickles and plant harvesting were removed before launch
(plants are props now). Tiers are only a soft progression since launch.

| Feature | Server | Status |
|---|---|---|
| Trees, mining by slope and biome, veins, passive common resources, regrowth after 20 min | Gathering.cs, EnvServer.Core Terrain/TerrainEdits.cs; felled trees respawn elsewhere within live's min / max per type and landscape (3c8f9c4, L2 / L113, user decision; on in dev since 2026-10-17) | ✅ (harness; the 6.12 / 6.13 rules untested in game; the min / max respawn checked on the test world, round 59) |
| Tool perks (Charmed, Farseer, Hotfoot, extractors, prospectors) | Abilities.cs, Gathering.cs | ✅ untested in game (axe perks and harvest bonus on trees, 6.14) |
| Refining and composites | Crafting.cs, CraftingStations.cs | ✅ (harness) |

## Crafting, items and storage

**How it worked** (wiki: Crafting, Crafting station, Replicator, Lumen Station, Lumens, Recipes, Innate Items,
Quality, Repair-o-Matic, Vault, Inventory, Stats, Storage, Soulbound): one station, the Replicator (at Spires; free
to place on sites, two looks), refines resources and crafts everything (gear, tools, consumables, composites). No
crafting skills: recipes are innate or discovered (purple satchel + card) from gathering, combat, chests, Lost
Crystals, Journal goals, or bought at the Lumen Station (a window: recipes, resource bundles, outfits, props,
storage pockets, campfires...). Lumens come from gathering, monsters, chests and goals (or Daybreak Cash); they
also pay for reviving here and the Crimson Parlor. The Repair-o-Matic (free to place) repairs gear for lumens.
Inventory: Bag, Materials and (when needed) Overflow tabs, no weight; Vaults (universal network: every vault opens
the same storage; Vault tab 60, up to 120 with pockets (notes 21 Mar 2016; the wiki says 80); Materials tab); storage chests on sites. Quality:
Average, Superior (green), Exceptional (blue), Legendary. Stats: run speed (base 10 units/s, +1 per point), jump
bonus, light radius (radius = stat / 10), harvest bonus (1 % extra per point, cap 100), harvesting damage.

| Feature | Server | Status |
|---|---|---|
| Replicator, recipes, refining, composites, discovery cards | Crafting.cs, CraftingStations.cs, RecipeBook.cs | ✅ (harness) |
| Lumen Station (recipes, bundles, outfits, pockets) | station 55 through CraftingHandler + the client's tables | ✅ untested in game: one 0.695 recipe crafted (4.20); the categories not gone through |
| Lumens from activities; revive and Parlor costs | Pacing.cs, Health.cs, Barbershop.cs, Gathering / Creatures | ✅ (the pacing curve is ours) |
| Repair-o-Matic (lumens per wear point) | Durability.cs | ✅ (harness) |
| Inventory tabs, overflow, vaults, expansions | Inventory/, Vaults.cs, ItemUses.cs | ✅ (harness; vault 60 → 120 as the developers' notes; the wiki's 80 is wrong) |
| Consumable stats (run speed, jump, light radius, harvest bonus) | Consumables.cs, Buffs.cs, Abilities.cs | ✅ (harness) |

## Character

**How it worked** (wiki: Character Creation, Character Sheet, Crimson Parlor, Fantasy Parlor Pack, Grappling Hook,
Outfits, Cosmetic Gear, NPCs): human, male or female, small or large (both fit a 9 × 5 block doorway); 4 face
presets per sex and size, skin tones, hair and colour, facial hair (male), eye colour; one of three starter styles
(fantasy sword, sci-fi water gun, Western pistols). The Character Sheet (in the inventory): equipped item, grappling
hook, armour, boots, outfit (cosmetic, one slot, no stats), accessories; title choice, Luminary Score, sites and
Gallery listings, Biography (0.695), Lumens and Daybreak Cash. The Crimson Parlor (at every surface Spire, also a
site prop): change face, skin, hair, colours, eyes, and (from 28 Jul 2016) sex and size, each option costing lumens;
the Fantasy Parlor Pack (499 DBC) adds 4 fantasy faces per body (pointy ears, horns, tusks), skin tones and two
animal eyes; options can be previewed and the pack bought from the Parlor. Grappling hook: Q to fire, space to let
go; Adventurer's and Delver's (crafted). Outfits are single-piece cosmetics (crafted from textiles, or from the
Lumen Station); Founder's outfits were no-trade gifts.

| Feature | Server | Status |
|---|---|---|
| Character creation, appearance, starter gear | AuthServer character handlers, Combat/Gear.cs | ✅ (in use) |
| Character sheet, titles, Luminary Score | Titles.cs, Achievements.cs; PlayerTitle written title-then-guid (16a789f: titles never showed before) | ✅ (harness; the new 0.695 titles, 4.16, and the 16a789f fix still need a check by eye) |
| Crimson Parlor (appearance options, lumen costs) | Barbershop.cs | ✅ (0.695 tints gated by unlocks) |
| Parlor: sex and size change, Fantasy Parlor Pack and other unlockers | Barbershop.cs, Unlockers.cs, RareDrops.cs; the Pack sold in the store for 650 Lumens (Store.cs, 6.60) | ✅ 🆕 sex / size change, the Pack and unlockers (checked on 0.695); the Pack is an in-client store product so the Parlor shows its Unlock button (1c91d45, 03c0ef1; client log "Store: 1, Count: 11"); outfit kept and look applied after Purchase (user-verified) |
| Biography, Inspect | Inspect.cs; the bio in SendSelfToClient | ✅ 🆕 3.4 (0.695 test; other players' tooltips, 3.22, untested in game; unnamed reply fields: L28). Other players' full data (LightweightToFullPc nested UpdateStat u32 sub, 16a789f) confirmed by a two-client check 2026-10-17 |
| Grappling hooks | client + Abilities; other players' grapple / swim relayed (HeroicMovement 0x105, Game/HeroicMovement.cs, 158ff76: dead since b653b73) | ✅ (the 158ff76 relay untested in game; L166 closed) |
| Outfits (crafted / Lumen Station / Showcase) | Crafting.cs, Cosmetics.cs; the Lumen Station's outfits cost Lumens there, no longer free in the store (6.60) | ✅ |
| Founder's outfits (Noble's / Courtier's Regalia, Tech Commander's / Sergeant's Gear): not in the item dump | Cosmetics.cs (Tech Commander's / Sergeant's Gear records), given to every character by the founder entitlement (Entitlements.cs; see Accounts) | ✅ 2026-10-18 (827fb93): Regalia meshes found in the client (Trailblazer 000 / 001 chests), items 900344 / 900345 in the founder package; tints [I] (L149); in-game check in TESTING-NEEDED |

## Social, Showcase and progression

**How it worked** (wiki: Chat, Friends List, Guild, Party, Trading, Gallery, Showcase, Player Studio, Journal,
Luminary Score, Watch List): chat tabs Chat (local), General (worldwide: **all worlds and landscapes**), Loot,
Guild, Officer, Tells, Party; /tell, /p, numbered channels, custom channels (kept when changing worlds, 16 Jun 2016);
voice chat. Friends (K): online filter, search, tell, teleport, mail, guild invite. Guilds (N): ranks with
permissions, message of the day, officer chat. Parties of 4: members within 200 m share harvested materials; anyone
within 50 m of an opened chest gets loot. Trading: direct trade, mail (Mailbox at Spires or on sites), chests with
permissions; Marketplace items tradeable after a wait, purchased designs and soulbound items never. Showcase (B):
Marketplace (Daybreak Cash, lumens, site slots, Player Studio designs), Gallery (published sites: name, up to 4
screenshots taken on the site, tags; Visit), Competitions, My Store. Player Studio: players sell designs; other
players review submissions (copyright, offensive). Journal (J): Goals (categories, cards, lumens and Luminary Score
5 / 10 / 15 / 20; daily goals and recipes give no score) and Recipes tabs; the Watch List tracks goals and recipe
materials.

| Feature | Server | Status |
|---|---|---|
| Chat channels, tells, custom channels, flood limits | Chat.cs, ChatRooms.cs, SocialServer ChatService | ✅ (harness; public channel on / off: see Interface); typed rich-text tokens (~[~ ~]~ ~a~ ~q~) neutralised in chat, guild MOTD / description, site names (L54) |
| **General chat, friends, guilds, mail across worlds** | Social/*, LandmarkEmulator.SocialServer | ✅ B.1 / B.9 (checked on the isolated stack; some clicks still listed in TESTING-NEEDED) |
| Friends, guilds (ranks, MOTD, officer chat, News tab), groups (4, 200 m sharing), ignore | Friends.cs, Guilds.cs, Groups.cs, Ignores.cs | ✅ (harness: popups, roster; the Guild News tab, E.5, untested in game) |
| Trade, mail (coins, cash on delivery), chests with permissions | Trades.cs, Mail.cs, Chests.cs | ✅ (harness) |
| Showcase: store, Gallery + Visit, competitions, Player Studio, vendors | Store.cs (live's Marketplace products in Lumens, or the whole catalogue free with FREE_STORE; see Accounts), WebPages.cs, Contests.cs, Commerce.cs, Vendors.cs | ✅ store, Gallery, competitions; Player Studio paid in Lumens with live's 40 % share, listing fee and per-design minimum (cb06bde, fbf760c); Vendor Booth decorative as live (VENDOR_SHOPS); open: contributor split (no per-voxel owners in designs), L148 |
| Journal goals, daily goals, titles, Luminary Score, watch list | Achievements*.cs (Achievements.Triggers.cs: TrackerGoalsCrafted, renamed recipe goals), DailyGoals.cs, Titles.cs | ✅ 5.10 / E.16, the new tracker goals untested in game: every objective of the 958 goals (1,269) has a counting path (trace/0695/agent-goals.md), including the 186 Replicator tracker goals (2026-10-17); 11 riddle goals count by rules read from their wording (L45) |
| Reward Chests, Thank You Tokens, "Champion of the People" goals | RewardChests.cs (objective 2209), Cosmetics.cs (title 4519) | ✅ 🆕 3.11, untested in game; the token id 900401 is ours (live's: L24) |

## Accounts, store and server operations

**How it worked** (wiki: Daybreak Cash, Lumens, Founder's Packs, Year Zero Trophy, Fantasy Parlor Pack; notes 2016;
client: 0.695): Landmark was buy-to-play, and Daybreak's All Access membership gave nothing in Landmark. Daybreak Cash
(DBC) bought Marketplace products; in 2016 these were the Build Site Expansion (799 DBC from 1 Apr 2016), the Fantasy
Parlor Pack (499 DBC, 30 Jun 2016), six pet figurines (28 Jul 2016), the Superb / Legendary Reward Chests (29 Sep 2016,
also at the Lumen Station) and, from July 2016, Lumens (the client's Lumen Box … Hoard of Lumens texts: 650 to 13,000
Lumens). Founder's Packs (Settler, Explorer, Trailblazer) gave flags, the Void Vault, Founder's pickaxes, the Tech and
Regalia outfits and the Founder title; accounts active on the alpha's first anniversary were mailed the Year Zero Trophy
and a Year Zero Survivor's Outfit; 2014 promotions gave the Iron Portcullis and the Fan Faire flags. From 29 Aug 2016
every player was mailed a single-use 10-day trial key for a friend ("Invite your friends!", the code entered on
landmarkthegame.com). A full world queued logins at character select (the client's queue dialog: place, member /
non-member counts). The client also carries live's GM tools (admin slash commands such as /item, /goto, /hideme behind
an admin flag), a grief system (points, levels 1-3, equipment lock, suspension notices) and telemetry reports (Lua
metrics, UI events, voice chat, social share) that live's collectors received.

| Feature | Server | Status |
|---|---|---|
| Lumen store: live's 2016 Marketplace products priced in Lumens (1 DBC = 1.3 Lumens: Build Site Expansion 1,040, Fantasy Parlor Pack 650, six pets 650, Superb / Legendary Reward Chest 260 / 780); Lumens never sold; a free-store switch | Store.cs (MarketplaceProducts, PricedCatalogue, LumensFor, Take), WebPages.cs, Resources/Web/showcase.js; settings FREE_STORE, LUMENS_PER_DBC, StoreLumenPrices (trace/0695/agent-store-entitlements.md) | ✅ 6.60: harness on the isolated stack (prices, refusal, purchase); the page, the HUD count and the client's store links untested by eye. The pet / chest / Lumen-pack DBC prices are not found (pets at a placeholder; L147); the client's StoreBundle packets stay unsent (no layout guessed) |
| Showcase following of creators and tags + daily mail of new items (Update Notes 27 Aug 2014; 2016 wiki) | Game/ShowcaseFollow.cs, Commerce.Follow (stored in showcase_follow), Showcase page Following tab | ✅ built 2026-10-18 (52af835); e-mail copy not built; with COMMERCE_ADDRESS: port in progress (local/commerce2); L137 |
| Founder entitlements: Founder's Pack, Year Zero and the 2014 giveaways to every character once, by mail (or into the bag), with their recipes; their props leave the palette when switched off | Entitlements.cs, RecipeBook.KnowsProp, Unlocks kind 26; settings FOUNDER_ENTITLEMENTS, ENTITLEMENT_PACKAGES, ENTITLEMENT_DELIVERY | ✅ 6.59: harness (granted once; unlock rows 1, 2, 3); the letters and recipes untested by eye. Per character, not per account (agent-store-entitlements.md §6 H); founder items with no client record (Founder's Pickaxe, Regalia, Ring of Bounty, Mastercraft Bracer, Mega Pocket, title Founder): L149 |
| Membership and the trial key | Membership.cs (account state in the auth DB; SendSelf membership type, PacketMembershipActivation 0x66, PlayerUpdate MemberStatus, the AddLightweightPc member bool); the "Invite your friends!" letter with a key item, mailed once per account; a mailed key turns No Trade and gives 10 days of trial, once per account; `/membership buy` (1,950 Lumens / 30 days, ours) | ✅ 3.12 built 2026-10-17, untested in game (steps agent-membership.md §8). Live gave members nothing in Landmark; the benefits here are the user's decision (2026-10-17): login-queue priority, 650 Lumens every 30 days (MEMBER_MONTHLY_LUMENS), 10 % off store prices (MEMBER_STORE_DISCOUNT), membership listed in the store. Open: agent-membership.md §6 M1-M10 (live's trial limits: M7) |
| Login queue for a full world (place, member priority, Cancel) | AuthServer Zone/LoginQueue.cs (CharacterLoginReply result 8; login tunnel 3 QueueStatus, 4 ServerQueue, 5 Cancel); the world's MAX_PLAYERS reported with its heartbeat | ✅ E.8 built, untested in game (needs a Play click at character select; harness checked the server side). The travel queue 0xE0 for a full landscape is built too (merge e-travel, LANDSCAPE_CAPACITY; harness 2026-10-17: the client's own queue dialog and the "Teleport to …?" offer; L60 closed); Accept / Deny by hand untested |
| GM tools: admin slash commands, /item | SendSelfToClient admin flag (f7e2307), AdminCommands.cs, AdminCommands.Items.cs (11 subs) | ✅ E.6: /item checked by harness (the client's own lines); /goto, /hideme, /run (the 0.695 run-speed command; there is no /runspeed), /target, /servertime, /prop untested by hand. 39 admin packets proven unreachable; parked: monitor requests L4, DebugPathQuery L145, claim items L146, leaderboard admin L59 |
| Grief (points, levels 1-3, equipment lock, suspension notices) | Grief.cs (GriefBase 0xC0, GriefAdmin 0x4DA; dev `grief`) | ✅ built (5831b55): earning from ally damage in team site games behind GRIEF_AWARDS (off: live numbers unknown, L144), levels / decay / suspension [I], persisted; untested in game |
| Telemetry reports | Telemetry.cs → `Telemetry/<date>.jsonl` through IWorldChannelService.ClientTelemetry (at most 60 a minute per player) | ✅ E.7 built, untested in game. None is answered: the social-share and web-auth replies would send the client to an outside web service |

## Interface, commands and client-side features

**How it worked** (wiki: User Interface, Controls, Commands List, Coordinate System, Screenshot, Cache, Tool Tips;
building technique pages: Fine Tuning, Micro-Voxel, Anti / Zero / Zero Data / Zero Volume Voxel, Voxel Board,
Terraform, Shapes, Texture, Materials Tray, Tint, Emitter, Gadget, Swaps): Adventure and Build UI modes; Watch
List (eye icon), notifications (!), Showcase, inventory, Lumen count (+ buys lumens); keys (Q grapple, U sites,
L designs, J journal, K friends, N guild, M map, B Showcase, O screenshot / social, F12 help); slash commands
(/afk, /bug, /camp, /exit, /join, /leave, /loc, /who, /yell, /tell, /reply, /ignore add, /friend add, /party
invite / accept / reject / kick / leave / disband / setowner, /reportclaim, /emotelist, /random, /gamefinder);
coordinates: 1 unit = 2.5 blocks, landscapes 8000 × 8000 units. The voxel-trick techniques and the cache are client
behaviour: the server only has to store exactly what was built (it keeps the VoxelFarm data as sent).

| Feature | Server | Status |
|---|---|---|
| Emotes, /emotelist, bug and site reports | Emotes.cs (/playdead: see Combat), Reports.cs | ✅ (harness) |
| /afk, /random, /who, channel join / leave, friend confirm, map positions, ItemDefinitionRequest | Afk.cs, RandomRoll.cs, Who.cs, Friends.cs, FriendPositions.cs, InteractionHandler (0x49 → 0x4A) | ✅ 5.4 / E.14: everything built (public channel on / off and bubble colour in E.14, e873dd6; RequestPlayerPositions 0x24 → 0x25, 9fdb0d9 — the client reads and discards the reply); 0x24 / 0x25 untested in game |
| Emitters (on / off, scalable), gadgets (prospector, sounder, lightstones, repair bot beacon) | Props.cs, PropEffects.cs, Abilities.cs, Consumables.cs | ✅ (harness: Cave Seeker, beacons) |
| Swaps (Get Inspired permission) | Claims.MayCopyIn, BuildingClipboard.cs: a copy / Save Design selection needs the copy permission on every build site under it (owner always, open land free; 22da502) | ✅ E.9 built 2026-10-17, untested in game |

---

*Whole-wiki review: all 282 feature / rule pages of trace/0695/wiki-systems.md are covered above (2026-10-15).
Content pages (props, outfits, weapons, creatures, resources) are matched by name in wiki-xref.tsv; the unmatched
ones are the 0.695 items in TASKS §4.*
