# Server systems

What each part of `LandmarkEmulator.WorldServer` does, which packets it uses (layouts in
[protocol.md](protocol.md)) and what is still missing. Paths are relative to `LandmarkEmulator.WorldServer/`.

## Sessions, zone-in and logout

- `Network/WorldSession.cs` holds one connected player.
  - It builds `SendSelfToClient` from the database (position, rotation, appearance, inventory, hotbars, recipes,
    currencies).
  - It saves the position periodically and on disconnect.
- `Game/PlayerManager.cs` runs zone-in. Each player gets:
  - other players (`AddLightweightPc` + their visible equipment);
  - the claim list, health, placed props, the (empty) design list, the Spire;
  - friends and guild data.
- Players whose heartbeat timed out are pruned every second, so friends/guilds see them go offline.
- **Logout to character select** (`Network/Handlers/CharacterSelectHandler.cs`):
  1. The client asks for a character-select session; the server stores a fresh login session key.
  2. The client sends `ClientLogout`; the server closes the connection immediately.
  3. The client reconnects to the login server with the new key.

## Inventory, equipment and the hotbar

- `Game/Inventory/Inventory.cs` holds the containers: bag (0), equipped (2), plus materials/resources, all persisted
  per character. `InventoryDefinition.txt` maps equipment slot ids to container slots (main hand 2 → 2, feet 4 → 7,
  chest 8 → 9, …). `Fits` checks an item may sit in a slot.
- `Network/Handlers/InventoryHandler.cs`:
  - transfers: dragging, right-click equip/unequip, picking a tool from the hotbar;
  - delete, activate (use an item: place its prop, learn its recipe, …);
  - tooltips;
  - equip/unequip packets.
  - Every transfer is answered. Equipment changes go out to everyone as `SetCharacterEquipmentSlot`.
- `Network/Handlers/ActionBarHandler.cs`: hotbar slot set/clear, saved in `character_action_slot`.
- Item definitions are the client's item list as dumped from a live server (`gameData/items.json`).

## Abilities, tools and mining (`Game/Abilities.cs`)

- **Caster info:**
  - Holding an item makes the client ask for its abilities.
  - Replies are built from item data, or from captured bodies for a few special abilities: lightstone 221,
    campfire 1098, and travel 185/186.
- **Calculated stats** are sent on login and on equipment change:
  - HarvestingArea (stat 3) is a *volume* the client draws as the mining ring's diameter. The server sends
    `(2r / 0.233)³` so the ring matches the dig radius.
  - HarvestSpeed (stat 13) = 400 keeps recasts short.
- **Mining (picks: 1018, 136–142, 169):**
  1. The dig centre is the ring point in the cast tail.
  2. It must be within reach and not inside someone else's claim.
  3. A sphere is carved (radius by pick tier) as an overlay with smoothed offsets.
  4. The yield is the ground material, or ore in vein cells (see terrain.md).
  5. Chunks are spawned as loot piñatas that fly to the player (`Game/LootPinatas.cs`).
  - Every cast is answered, even refusals.
- **Weapons:** combo casts become melee swings resolved by aim point (`Game/Creatures.cs`).
- **Travel:** cast 186 (My Sites) teleports to the player's own build site, 185 (Spire) to the Spire, 187 (friends
  list right-click → Teleport to Friend) next to the friend, never below the ground there. All run after the
  4-second cast animation.
- **Underground Adventure** (`Game/Caves.cs`, `Game/Travel.cs`): a random cavern from the client's own cave
  placements.
  Random trips use cavern entries at y >= -650 only: deeper caverns were still loading on the client 35 s after
  arrival and the player fell through the world (audit 2026-10-14).
- **Right-click menus** (`Game/RightClickMenus.cs`): the server chooses the buttons for players, friends, party and
  guild members, props and chat tabs, from the client's RadialMenuButtons.txt.

## Gathering (`Game/Gathering.cs`)

- **Trees:**
  - They are server entities, generated per 64 m region from a seed. The forest type depends on the biome; slopes,
    the sea and build sites are skipped.
  - Each region is sent within 160 m and removed beyond 224 m.
  - Chopping (axes 1020, 176): 5 hits (8 for large trees), 2–4 wood per hit (Plain, Burled for large, Striped for
    volcanic). Tree Fiber and Sap are a chance.
  - A felled tree regrows after 30 minutes; felled trees are saved.
- **Plants** (sickles 153–157) are entities too. A swing gathers the plant nearest the aim point and gives the
  plant item (common, large or perfect variant).

## Building (`Game/Building.cs`, `Game/Terrain/*`)

- Handles `ApplyDiffRequest` from every brush (add, remove, smooth, paint, fill, …):
  - per-voxel claim permission check;
  - material cost: 1 item per voxel (the client's own pre-check counts the brush's voxels), charged from the inventory, refunded on removal (`BuildMaterials.cs` maps
    voxel materials to items);
  - apply to the overlay, save, broadcast to nearby players;
  - reply with `Response` (ok / insufficient materials / no permission).
- Voxel material → item: the material's CURRENCY_ID (dirt, sand, stone, woods, metals, …) gives the item. Raw items
  are used for dirt, sand and stone; processed ones (cut wood, smelted metal) for the rest.

## Claims / build sites (`Game/Claims.cs`)

- **Placing a site:**
  1. Right-click the Build Site Flag item.
  2. The server answers with `InteractionClaim`: result 2 starts placement; a refusal code shows the client's own
     text instead (10 = already at the site limit, 6 = no flag, 28 = Pioneer landscape).
  3. The client shows the placement view, checks the spot itself (buffer, below) and sends `PlaceClaimRequest`,
     then `Confirm`. A refused request (result ≠ 1) shows its text and puts the client back into placement.
  4. The server answers with `PlaceClaimComplete` (result 2) and the record. The site is the launch size, 210 × 180 ×
     210 m (525 × 450 × 525 blocks) around the requested centre: the client's constant `ClaimCursor.ClaimSize`, which
     also sizes its placement cursor. Older 70 × 100 × 70 m sites are grown at server start when there is room.
- **Buffer:** no new site may come within 285 m of another root site's centre on any axis
  (`ClaimsManager.BufferDistanceToNonfriendlyClaim`). The client applies this itself once it has the site list
  (`SendAllClaimBasicInfo`); the server checks it again (result 26, "too close to another build site").
- **Permissions:** the owner, per-player and per-guild masks, and defaults. `MayEdit` is used by mining, building
  and props.
- **Management window:** management data, upkeep data, rename, description, permissions, delete.
  - Delete reverts the site's terrain, returns its props and gives the flag back.
- Upkeep: sites of owners away more than 30 days are removed (checked hourly).
- Sites belong to a landscape (`claim.zoneId`). A player is sent the sites of the landscape they are on, at zone-in
  and again after every re-zone (a full AOI update replaces the client's list). `RequestAllClaimBasicInfo` is
  answered with the light site list `SendAllClaimBasicInfo` (protocol.md §10), which feeds the map and the client's
  placement check.
- Pioneer lands (zones 417-421) refuse hand-placed sites with the client's own text. They hold the sites given to new
  players (`Game/Pioneer.cs`): a character logging in for the first time (no saved position, no site) gets a site on
  the Pioneer land with the fewest sites, on the free 390 m grid spot nearest its centre with dry, gentle ground, and
  starts in its middle. This needs that land's baked heights (`tools/terrain`); without any, new players start at the
  Spire.

## Props and crafting (`Game/Props.cs`, `Game/Crafting.cs`, `Game/CraftingStations.cs`, `Game/RecipeBook.cs`)

- **Props:**
  - Placed from the build palette or by using a prop item, and paid for with the prop's recipe components.
  - Moved with the gizmo (Save), picked up, deleted. They are persisted and shared with nearby players.
- **Crafting stations** are props mapped to `CraftingStations.txt` by name or model keyword. Clicking one goes
  through interaction flagging → `InteractRequest` → `CraftWindowEnable`.
- **Crafting** checks and consumes the components and gives the output. Recipes are learned from recipe items.

## Storage chests (`Game/Chests.cs`)

- Craftable chest props (PropDefs with a prop recipe and "Chest" in the name) hold items: small 10 slots, large 30,
  the rest 20. Contents are in `chest_item`.
- Clicking a chest opens it (EnableWindow → the client asks for the list). Its owner can always use it; others need
  Pillage & Plunder on the build site it stands on.
- Items move with the normal drag and drop (TransferItem, owner type 3). Everyone looking into the chest sees changes.
- Rules from the live game's update notes: at most 50 chests per build site (21 March 2016); chest access is the
  "Customer" permission (26 April 2016), the 0.589 client's Pillage & Plunder level (the 0x04 bit).
- Picking a chest up keeps its contents inside it (Daybreak: chests "can't be disassembled because of the risk that
  you might lose items stored in the chest"): it comes back as its own bag item, and the items follow it when it is
  placed again. A chest holding items can't go into another chest or be traded, and can't be deleted in place.
- Not built yet: locking and unlocking chests (May 2016 notes), the tooltip list of a carried chest's items.
- Opening a Simple Medium Chest counts the Props To You goal.
- Dev commands: `chestopen`, `chestput <item> <n>`.

## Social

- `Game/Chat.cs`: wire channel = ChatChannels.txt row − 1 (0 Say, 1 Tell, 3 Shout, 4 Emote, 5 Guild, 6 Officers,
  8 Party, 9 Public, 10 player rooms). Say and emote lines reach 100 m, shouts 500 m in the same landscape, party
  the group, guild the guild, officers the guild ranks holding JoinOfficerChat, rooms the room, the rest everyone.
  Channels only the server writes (2 TellEcho, 7 System, 11 Error, 12 Loot, 13 Combat) are dropped when a client
  sends them. `Chat.System` sends a server line.
- Guild ranks also carry three permissions the client checks but its rank editor never shows (string table next
  to Guild.MaxMembers): JoinOfficerChat (the client joins channel 6 with it), MuteGuildChat / MuteOfficerChat
  ("/guild_debug mute_member <guild id> <member guid> [guildchat] [minutes]" → GuildRequestMutePlayer 0x83/0x10).
  Leaders hold all three, the stock Officer rank the first two. Guilds hold up to 250 members, parties 4.
- Mail limits are the compose window's (subject 128, body 4096); messages expire after 30 days (the client's
  Mail.ExpireTimeMinutes) and are deleted then unless they still hold items or coins.
- Trades end when a trader changes landscape (TradeCanceled) or enters a running site game (codes 8 / 9). The bag
  check counts stacking onto the receiver's own stacks.
- `Game/Friends.cs`: one-way friend lists with online/offline notices, stored in `character_friend`.
- `Game/Groups.cs`: invites (the popup's Accept / Decline come back as a GroupsBase Join), the party window, leader, kick, leave, disband.
- `Game/Guilds.cs`: create, invite, ranks with permissions, MOTD, online flags, persisted (`guild`,
  `guild_member`). The invitation popup is a client notification (`Game/Notifications.cs`, Notifications 0x01
  ListNotifications type 6). Invitations live 5 minutes on the server. When the client's UI shuts down (logout, idle
  logout, exit) it sends its unanswered queue entries back as ListNotifications sub 1 (FUN_1433302e0 from
  GuiOnShutdown) {u32 n, n × Data}. An invitation still valid at the next login is shown again.
- `Game/Mail.cs`: player and system mail with items, coins and cash on delivery; vendor sales pay sellers by mail.

## Goals and achievements (`Game/Achievements.cs`)

- The Goals tracker, the journal, recipe discoveries and replicator lists are all **achievements** with
  objectives (AchievementBase 0x92; packet layouts in protocol.md).
- Definitions (898 achievements, 1191 objectives, rewards) come from a live AchievementInitialize capture. At
  zone-in the server sends a copy with the character's own progress patched in: objective state (0 not started,
  1 active, 2 done) and count, start/completion time, progress fraction.
- Progress is saved per objective in `character_objective`.
- **Getting Started** is a chain: 471 Your Home Is Your Castle → 480 Props To You → 478 Cry Havoc! → 477 Getting
  Around → 685 Becoming a Luminary. Each starts when the previous completes; the tracker shows the current one.
- On completion: ObjectiveComplete / AchievementComplete, reward items (e.g. 685 gives 30 Lumen and the
  "Title: Luminary" item), next goal activated.
- Counted so far:
  - Underground Adventure (1319);
  - wood and pick harvesting (tutorial, lifetime and per-material objectives);
  - biome visits (Tundra's Still the Best), checked when the biome under the player changes;
  - placing the tutorial props (chest, stool, Starting Location, Target Dummy);
  - opening Build Site Management;
  - by the objective's own text (every tier with that text): Monsters killed, "<Family> killed", Dispatch explode-y
    mushrooms, Kills with a(n) <Element> weapon (held weapon affinity, Physical = Earth), Guild joined, Give or
    receive a Design in trade, Treasure Chests opened, Design item created, Build Site "Like" given or received,
    Build Sites visited, Scan completed (Holoseeker), Extractor acquired, "<item> crafted" (plural names too),
    "<prop> placed", "<material> blocks placed / removed", "<achievement> completed".
- Dev command `objective <id> [n]` counts any objective.

## Health, death and creatures (`Game/Health.cs`, `Game/Creatures.cs`)

- The health bar is a Resources record. Regeneration is 50/s server-side. The death window offers revive options.
  There is no fall damage in 0.589.
- Creatures are lightweight NPCs with server-side movement and hit points. Players hit them by aim point.
- **Placement follows launch** (wiki "Monsters (PvE)": only Fiery Mushrooms on the surface; every other monster is
  cave-only):
  - The surface has scattered Fiery Fungus.
  - Each Chaos Cavern holds a group around the floor under its middle, streamed by 3D distance (150 m in, 200 m out).
    The cavern list comes from the client's Underground cave placements (`Game/Caves.cs`).
  - A group is the cave biome's Chomper and Slaug variants, an Abomination and two Fiery Fungus. The variants are
    picked from VoxelLayerPropertyGroups → VoxelBiomes: 41 Ancient Forest → Forest / Wood, 62 Deciduous → Putrid /
    Rotten, 63 Volcanic → Scorched / Magma, 45 Crystal → Frozen / Pale.
- **Models** are the creature props' own (prop dump).
- **Behaviours and effects:**
  - Chompers are stationary and spray a spore cloud within 7 m (effects 11676 / 11611).
  - Abominations close to 16 m, back off under 6 m and spit (11627 / 11625).
  - Fiery Fungus arms when a player comes within 3.5 m. After 1.5 s it explodes (10918, impact 10922) for everyone
    within 5 m and leaves dirt and stone; a hit sets it off.
  - Slaugs chase and hit.
- Cavern creatures walk the cave floor (`Caves.FloorNear` on the client's cave shapes) and react to 3D distance, so
  players on the surface above don't wake them. Health, damage and loot are ours: live's were server data. Loot is
  Monster Essence, the affinity essence and Adventure Essence.
- The spawn's control-mode byte (AddLightweightNpc, after the three u32s that follow the character model block;
  read to entity +0x41a0 by SetupLightweightNpc 142cbaca0) must be non-zero: the client's per-frame update of a
  proxied character (vtable +0x4c8, FUN_142e130e0) only follows server positions then. Remote players are forced to 2;
  creatures are sent 2. With 0 they stay where they spawned.
- Animals with no damage (deer) flee from players within 8 m or when hit, until 22 m away.
- Name and health over an NPC: HUDIndicatorSettings type 13/1 → indicator code 22 (Npc) when requirement 21 "NPC
  Combatant" (`NotPlayer && NotProp && TargetCanBeHarmed && TargetNotPropProfile`) holds; TargetCanBeHarmed is
  TargetMaxHealth > 0, so the hit points (PlayerUpdate 0x03) are sent with every spawn.

## Weapon combat (`Game/Combat/WeaponCombat.cs`, `Statuses.cs`, `Gear.cs`)

Launch combat (wiki "Combat", "Weapons", "Affinity", the weapon pages; the client's own texts). The cast ids are the
same for every weapon (input table: 760 basic, 761 power, combo stages …), so the server applies the **held weapon's
kit**: its affinity and two named attacks, e.g. Staff of Frozen Sorrow "Basic Attack: Shatter" (text 574445,
description 574446) / "Power Attack: Blizzard" (574447/574448). 21 named kits cover 31 weapon items (launch weapons and
the pre-launch items they replaced); weapons without named attacks (Lightbringer's Justice, Desperado's Deadly Arsenal)
use the generic "Basic Attack" / "Power Attack" and their affinity's main state.

| Affinity | Properties (wiki) | Kits (weapon: basic / power) |
|---|---|---|
| Fire | Erupt (double damage roll), Frenzy (faster attacks), Burn (no attacks, DoT) | Wanderer's Blazing Bow: Explosive Shot / Immolating Barrage · Frenzied Slayer's Battle-Axe: Fiery Cleave / Conflagration · Flame Bound Bastion: Cinder Strike / Molten Rebuke (block) |
| Water | Flow (stacks, energy), Surge (half the energy as damage), Freeze (no attacks or movement) | Staff of Frozen Sorrow: Shatter / Blizzard · Supercooled Power Cannon: Tidal Burst / Torrent · Frost Lord's Fury, Katana of Crashing Waves: Fracture / Frozen Domain |
| Lightning | Supercharge (Power +damage +cost), Energize (stacks, regen), Electrocute | Falchion of the Inner Spark: Arcing Strike / Terminal Voltage · Overcharged Recon Blaster: Feedback / Shock and Awe · Staff of the Lightning Caller: Crackling Orb / Static Bomb · Electrified Assault Axe: Overcharge / Rolling Thunder |
| Earth | Harden (next hit reduced), Regrowth (heal on hit), Crush (knocked down) | Armored Brawler's Blade: Assault / Overrun · Gunslinger's Six-Shooters: Unload / Trick Shot · Unflinching Embodiment of Might: Decimate / Obliteration Turn |
| Affliction | Infect (stacks, +damage), Rot (−damage dealt, DoT), Cripple (slowed) | Poison Drenched Daggers: Debilitating Shiv / Contaminate · Blight-Warden's Pain: Enfeeble / Corrupting Revenge (block) · Diabolical Scientist's Raygun: Ooze Bolt / Sludge Storm |
| Shadow | Siphon (Power damage → life), Torment (+damage taken), Condemn (rooted) | Armament of Unrelenting Vengeance: Shadow Rend / Dark Embrace · Silent Shadow's Condemnation, Spear of Piercing Judgment: Hateseeker / Nightfall · Night Herald's Mercy: Cursed Bolt / Agonizing Volley |
| Radiance | Illuminate (shield), Exalt (+damage, stronger properties), Rejuvenate (regen) | Wand of Boundless Light: Brilliance / Cascade · Avenging Champion's Exaltation: Smite / Consecrate · Crusader's Courage: Sanctify / Righteous Swing |

- **Shapes**: cone in front, all around, first hit along the aim (with splash), pierce along the aim, area at the aimed
  point, a dash path, a blocking stance (the next hit from the front is stopped and countered by a shield bash).
  Channels are timed hits; Blizzard / molten field / ice storm / blazing cyclone are lingering areas.
- **States** (`Statuses`): on creatures they stop attacks (Burn, Freeze, Electrocute, Crush), stop movement (Freeze,
  Electrocute, Crush, Condemn), slow (Cripple), deal damage over time (Burn, Rot), change damage (Torment +25 % taken,
  Rot −25 % dealt). Each plays the client's state effect (PFX_Combat_Misc_State_*) for its duration. On the player they
  show on the buff bar with the property's name and the affinity's buff icon (image sets 12206-12218).
- **Caster info**: the attack buttons' caster info carries the held weapon's attack name/description; it is sent again
  when the weapon changes and when Frenzy (cast time × 0.6) or Supercharge (Power cost × 1.5) start or end — the client
  overwrites its cached entry with every reply.
- **Armour / accessories** (`Gear`): the 28 chest pieces' Maximum Affinity Effects (only with a weapon of the same
  affinity) and the 21 accessories' property boosts, each from its description.
- **Charged attacks** ("Charge a powerful …"): their caster info has the charged kind 0xF7150354 with max / min charge
  (1.2 s / 0.15 s, reply +0xd0 / +0xd4). The client charges while the button is held (ClientAbilityManager +0x2b0 = 1)
  and on release sends FinishChargedCast {u32 ability, u32, vec4 camera, vec4 aim}; the server times the hold (damage
  60-100 %, "fully charged" at ≥ 95 % of 1.2 s), runs the attack with the release aim and only then answers the cast —
  a CastSuccess during the charge ends it (FUN_142d7a370 clears the charging state). CancelCast closes it unanswered.
- **Attack animation timing** (measured in the client, re/test/scenarios/adhoc/castanim): the caster's own
  CastSuccess plays its animation (the Enter) plus ActiveTimeStart 509; one **cast time** later the client plays
  ActiveTimeEnd 510, which stops the current selection. The Release (strike) is sent at the selection's AttackTime as a
  CastSuccess without Force (+0x48), so it plays without restarting the window; the weapon casts' cast time is sized
  to the latest strike + 0.35 s motion + 0.15 s for the answer's trip (WeaponAttacks.StrikeWindowSeconds). With the
  old 0.4 s cast time and a forced second success the strike was stopped 50-130 ms in ("janky" swings).
- **Ours, not the client's**: durations, chances, damage shares (the ability data was server-side).
- Dev: `wield <item>`, `weaponattack basic|power [n]`, `states`, `combatgear`, `revive`, `god on|off`. Tests:
  `re/test/scenarios/weaponkits.json`, `charge.json` (client.js `castability` presses / releases through FUN_142d71800).

## Landscapes (`Game/Landscapes.cs`, `Game/Travel.cs`)

- The Landscapes window lists the islands (391-416) and Pioneer lands (417-421) from the client's StaticZoneData,
  with their real names and site counts. Travelling re-zones the player (`Teleport.Rezone`).
- Each landscape has its own state, keyed by zone id:
  - terrain edits (`TerrainEdits/<zone>/`; the home continent 406 keeps the top folder);
  - trees, plants and creatures (seeded per landscape, streamed only when its heights are baked);
  - build sites and placed props (database `zoneId` columns);
  - ore veins and biomes from the client's per-continent files.
- Players only see players, props, sites, terrain broadcasts and `say` chat on their own landscape.
- The Spire and the captured terrain store (cells recorded from the live servers) are on the home continent only.

## World

- `Game/WorldClock.cs`: one shared time of day for all players, 9.1× real time, starting at 09:00.
- `Game/Spire.cs`: the teleporter hub at the zone origin (client models 612 + 596).
- `Game/Teleport.cs`: moves a player, snapped to the ground.
- `Game/Templates.cs`: designs (save, list, place, rename, delete); design items add the client's .otd templates
  as designer templates (Starter Castle).
- `Game/WebPages.cs`: a small local web server for the client's embedded browser (Showcase / Gallery), set with
  `MarketplaceUrl=http://127.0.0.1:19001` in `ClientConfig.ini`. Without it the browser retries a dead URL and
  stalls the client.

## Developer commands (`Game/DevCommands.cs`)

Write lines to `dev_commands.txt` next to the server executable; it is read every second. Useful ones:

| Command | Effect |
|---|---|
| `send <hex>` | Send raw zone bytes to everyone. |
| `give <item> <n>` | Give items. |
| `finditem <text>` | Search item names. |
| `timeofday <h>` | Set the world clock. |
| `carve`, `dig`, `digmode` | Shape terrain / change how digging shapes holes. |
| `tp`, `tpclaim` | Teleport to a position / a build site. |
| `kobold`, `creatures`, `hurt` | Spawn creatures, list them, damage the player. |
| `tree`, `treefall` | Inspect / fell the nearest tree. |
| `prop`, `station`, `claims`, `unclaim` | Props, crafting stations and build sites. |
| `landscapes`, `landscape <zone>`, `pioneer` | List landscapes, travel to one, run the new-player Pioneer start. |
| `testcell`, `livecell`, `blockcell`, `blockdmg` | Terrain packet experiments. |

The full list is the `switch` in `DevCommands.cs`.

## Not built yet

- The 0.695 client (the server speaks 0.589).
- Rendered character portraits (live had a render service; the server keeps and serves the client's own uploads —
  no 0.589 window shows portraits).
- Story creatures equipping weapons (the client has no NPC equipment data for it) and control points as spawners.
- Things that need a person at the keyboard to judge are listed in the repository's testing checklist.

## Death and revive (decoded 2026-10-12)

- **Death window:** EncounterBase 0x7D / 0x7E (docs/encounters.md). Its choices come back as PlayerUpdate 0x3B Respawn:
  10 revive here (ReviveToken item 1579 × ReviveTokenCount 15, ResourceConstants; Character.Revive.AbilityId 1170),
  11 nearest safe point, 12 flee.
- **Reviving another player** (player right-click "Revive Player", Lua Ui.RevivePlayer FUN_14351ace0): the client's
  action manager (FUN_1433acca0, action type 6) runs a revive action that casts Abilities.ReviveInteractionAbilityId
  **246** at the target (FUN_1436760c0 → ClientActionManager FUN_1433abc00). The cast is only sent when the client's copy
  of the target (entity lookup FUN_142ee6730) passes two checks:
  1. its state bit 7 is set (character-state field entity +0xa20, FUN_142e0b7a0), and
  2. it holds RevivePoints above 0 (resource type 12; Resources.txt ids 29 and 112, 50 points, FUN_143ad04e0).
  The client then whispers "I am reviving you!" to the target.
- Server side (Game/Health.cs): a death sends the dead player's RevivePoints (resource 29) to everyone on the landscape
  (and to newcomers), and cast 246 at a dead player within 8 m starts a revive: StartRevive (0x42 {u64 dead, u64
  reviver, u32 duration ms}, 3000 ms, our length) to players within 100 m of the body (every client records the reviver
  on the dead entity, FUN_142e2abe0; the reviver's client shows the HUD revive bar, FUN_142ea2cc0 mode 2), then
  ReadyToRevive after the channel; StopRevive (0x43 {u64 dead, u64 reviver, bool}) if the reviver dies, leaves or walks
  off. The revived / reviver lines are the client's CombatSpam row 8 texts (564592, 564593); when an entity comes back
  to life the client prints that row itself from the recorded reviver (vtable +0x488 → FUN_142e20a10 → FUN_142f43730). 0.589 has no accept prompt
  (UI.DoYouAcceptRevive / UI.BeingRevivedBy exist only as text mappings) and no point cost.
- **What the live server sent** (packet registry cPlayerUpdatePacketId*): 0x39 KnockedOut to everyone when a player
  goes down (now sent: u64 victim, two character-info blocks, two stat blocks, a damage report, u32; read exactly by the
  client, which adds a kill-feed line), 0x44 ReadyToRevive to the dead player when someone revives them (now sent on
  cast 246), the dead client's 0x3D ReadyToReviveResponse (sent by itself for the local player, also Lua
  AcceptRevivalRequest / DeclineRevivalRequest; handled: revives in place and re-shows the player to others), 0x42 / 0x43
  StartRevive / StopRevive, 0x50 HideCorpse, 0x3C RespawnReply, 0x52 UpdateMissionRespawnTimer; admin commands
  PlayerForceRespawnRequest / RevivePlayerRequest.
- **0.589 limit:** for another player's character (ProxiedCharacter) the knocked-out handler (vtable +0x3f0,
  FUN_142e0f020) is empty, so KnockedOut never marks the body as down; and state bit 7 (the revive check's "down" flag)
  makes ProxiedCharacter::SetupActorGroup (vtable +0x158) fail, so the client drops the entity. The right-click revive
  therefore can't reach the cast in this client build; the server side is complete for a client that can.
  Checked at runtime: remote players are always the base ProxiedCharacter (vtable 0x14460f108); only
  LocalProxiedCharacter / FirstPersonLocalProxiedCharacter have a knocked-out handler. Still open: whether
  SetupActorGroup (which loads the character's actor data) fails on bit 7 by design or because the live server sent
  data we don't (a downed-appearance actor), so treat "unfinished" as likely, not proven.
- After a teleport the other players on the landscape now get the player's new position at once
  (PlayerManager.ShowPosition); before, someone who stood still after travelling stayed at the old spot on other screens.
