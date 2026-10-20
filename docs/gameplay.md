# Server systems

What each part of `LandmarkEmulator.WorldServer` does, which packets it uses (layouts in
[protocol.md](protocol.md)) and what is still missing. Paths are relative to `LandmarkEmulator.WorldServer/`.

## Sessions, zone-in and logout

- `Network/WorldSession.cs` holds one connected player.
  - It builds `SendSelfToClient` from the database (position, rotation, appearance, inventory, hotbars, recipes,
    currencies).
  - It saves the position periodically and on disconnect.
- `Game/PlayerManager.cs` runs zone-in. Each player gets:
  - other players (`AddLightweightPc` + their visible equipment), and it is shown to them, when its login loading
    screen drops (PlayerUpdate ClientLoading 0x6C {0}, `PlayerManager.LoadingScreenLeft`; 90 s at most). That is
    15-25 s after `ClientFinishedLoading`, which only ends WaitForWorldReady: the screen then waits for the terrain.
    Until then no movement goes to or from the player. Every re-zone holds the player the same way (see "Teleports");
  - the claim list, health, the placed props within `PropStreamRange` (see "Props and crafting"), the (empty) design
    list, the Spire;
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
- `Network/Handlers/ActionBarHandler.cs`: hotbar slot set/clear, saved in `character_action_slot`. The session keeps
  a copy of the bars as the client has them (`WorldSession.actionSlots`, from SendSelf and every confirmed change):
  a right-clicked bag tool (0.695 sends only the equip) is put on the first free slot by the server
  (`InventoryHandler.IsRightClickEquip` -> `WorldSession.PlaceOnActionBar`), as the 0.589 client did by itself.
- Item definitions are the client's item list as a live server sent it: the captured packet
  `Resources/PacketDumps/44-CommandBase`, sent at world entry (protocol.md §5).
- Items are instances: a trade, a mail attachment or a chest hands over the same item, so it keeps its wear (condition;
  `Inventory.ItemState`). Wear is saved with the item row (`character_item.wear`, `chest_item.wear`, the mail
  attachment text "definition:count:wear" / `social_mail_attachment.wear`). A worn item never stacks onto another
  stack. Before 2026-10-18 every hand-over rebuilt the item from its definition (a free repair) and wear was not saved
  at all (a relog repaired everything).
- Overflow (container 7, items that didn't fit the bag) expires: `OVERFLOW_EXPIRY_HOURS` (default 24, wiki Inventory)
  after an item arrives it is deleted (`character_item.expiresAt`; online players checked each minute,
  `Game/OverflowExpiry.cs`, offline ones at login). The item record's timed-delete seconds (+0x138) carry the time
  left, and the client titles the tab "Overflow: Items expire in H:MM:SS" (0.695 FUN_142f258f0 "ExpirationTime",
  Inventory SWF). While items expire an arrival tops up only an overflow stack of the same item that arrived within
  the last minute (its end at most 1 min before the arrival's, which it keeps), else it takes its own stack and
  time; so a full bag that keeps looting does not fill the 500 slots one item at a time. `0` keeps them until
  retrieved and stacks freely.
  An item taken out of overflow onto an occupied slot (or worn straight from overflow) never swaps the occupant into
  overflow: the occupant goes to a free bag slot, or nothing moves.
- Resources over the resources tab's cap (SLOT_VALUE 24,000,000) wait in overflow instead of being dropped.
- The starter kit is given once per character (Unlocks kind 34), not to every inventory that is empty at login.
- Vault items move (TransferItem, right-click Equip, chest deposit, mail attachment) only while a vault is open within
  reach, whichever locations the request names.

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
- **Weapons:** combo casts become melee swings resolved by aim point (`Game/Creatures.cs`); melee swings run as the
  client's Sweep (see "Weapon combat").
- **Sprint, leaps, double jump and glide** (`Game/MovementEffects.cs`; OPEN-LEADS L160, L168): the movement is the
  client's, but the 0.695 client starts it only from the movement effects listed in the ability's caster info
  (ClientExecutedEffectData, protocol.md §7); its own sprint path is for admin clients only. With the list empty (before
  2026-10-18) Shift and the boots were cast and answered but nothing moved. The server sends:
  - **Channels, held as long as the key is:** Shift (326, Adventurer's Sprint) +25 %, Messenger's Sandals (328,
    Immediate Delivery) +41.7 % (the nearest parameter row to the dev post's +37.5 %), Breeze Boots (542, Endless
    Breeze) a glide until landing. Their replies carry the channelled kind; the server answers the press with
    CastContinue (never a CastSuccess while held) and each key release (CancelCast) with a CastSuccess a second later,
    unless the player cast again first; running out of Energy, death or a zone change end the channel the same way.
    The answer to the release matters: a channel that ended by itself (landing, unable to sprint) otherwise leaves the
    client refusing every cast (protocol.md §7 "Channelled casts").
  - **One-shot:** Nimbus Boots (320, Cloud Burst) a double jump and +10 % damage for 5 s; Boots of Bounding (321,
    Charged Leap) 15-25 m forward by charge (up to 1.25 s) and Leaper's Launchers (322, Explosive Leap) 10 m (up to
    1.5 s), both answered at FinishChargedCast, Explosive Leap with a take-off blast of the base weapon damage ×
    (1 + charge) on every creature within 5 m in sight [I]; Neon Blitzing Boots (327, Electric Charge) a 0.5 s cast,
    then a jump forward and +25 % for 3 s.
  - **Costs and cooldowns** from the developers' Heroic Movement post (14 Oct 2014), in the caster info so the client
    refuses what it can't pay itself, and kept by the server (CastFail): Cloud Burst 50 Energy / 5 s, Charged Leap
    100 / 5 s, Explosive Leap 80 / 10 s, Electric Charge 75 / 10 s, Immediate Delivery 40 Energy a second / 10 s;
    Shift's sprint and the glide cost nothing [I].
  - Jump heights stay 3.0, the client's default, because the client keeps the last jump height. The packet layouts
    are proven; which parameter rows live used and whether 2016 kept the 2014 numbers are ours.
  - Setting `MovementEffects` (Docker `MOVEMENT_EFFECTS`, default true = live; off answers every movement cast at once
    with no effects, costs or cooldowns); dev `movementeffects on|off` (new caster info only).
- **Character stats** (`Game/CharacterStats.cs`; L162): the movement tuning live sent every player, from the live login
  capture: backpedal 0.65, strafe 0.75, crouch 0.25, sprint 1.0, the eight acceleration / deceleration times (0.2 /
  0.15 s), waist twist 48 and the rest of the capture's 37 stats (ClientUpdate UpdateStat at login). Other players get
  the eight times inside LightweightToFullPc (the stats the client table marks SEND_TO_REMOTE_CLIENT), which the client
  copies into each character's animation, so their start / stop blends are no longer instant. No extra packet. Setting
  `LiveCharacterStats` (Docker `LIVE_CHARACTER_STATS`, default true = live); dev `charstats on|off`.
- **Travel:** cast 186 (My Sites) teleports to the player's own build site, 185 (Spire) to the Spire, 187 (friends
  list right-click → Teleport to Friend) next to the friend, never below the ground there. All run after the
  4-second cast animation. Teleport to Friend has no cooldown, as live from 5 May 2015 (the 2014 alpha had 30
  minutes); `FriendTeleportCooldownSeconds` (`FRIEND_TELEPORT_COOLDOWN_SECONDS`, default 0; dev `friendtpcooldown <s>`)
  adds one. It goes into 187's caster info (+0x70 → the ability record's +0x14c), so the client's own Ui.TeleportToFriend
  refuses a repeat and prints 562080 "This ability is on cooldown for another %d minutes and %d seconds." itself, as
  the client does (OPEN-LEADS L182); a cast that still arrives inside it gets CastFail and the same text from the
  server. With 0, 187's +0x70 is 0 (the 1098 capture it was derived from carried 60 s, a client-side cooldown live
  2016 did not have).
  A friend who is offline (on no world) gets the client's own text 560709 "[player] is not online at this." as a
  system line with the friend's name (`Game/TargetNotOnline.cs`; the same line answers a trade invitation or Inspect
  aimed at a player who left). `TargetNotOnlinePacket` (`TARGET_NOT_ONLINE_PACKET`, default false) sends the client's
  PacketTargetClientNotOnline 0x1A `{u64, string}` instead, whose line shows the reader's own name (client quirk).
- **Underground Adventure** (`Game/Caves.cs`, `Game/Travel.cs`): a random landscape with cavern graveyards, then a
  random cavern graveyard on it (user decision 2026-10-17, OPEN-LEADS L51 / L129). The graveyards are the client's
  type-3 "cave beginning locations" (`.cpa` tile record byte 32 set, byte 38 = 3: the 81 Graveyard01 pieces of each
  island); the client's own roll among them (CaveManager GetRandomCaveBeginningLocation, 0.695 FUN_143f84420) never
  picks the current one, and neither does the server (the nearest graveyard within 350 m of the player is left out).
  The player arrives 0.6 m over the stamped graveyard plinth (1 m above its top solid sample, as the cave-floor arrivals
  always were), or on the cave floor there when `CavernGraveyardStamps` is off.
  Pioneer lands have only type-2 locations and are never picked. A landscape whose outposts or cavern graveyards
  aren't ready yet (laid out and stamped in the background on first use) is queued and a ready one is picked; with
  none ready the trip goes to a random cavern as before.
  - **Where the arrival is** (`Game/Graveyards.Arrivals.cs`; checked on all 1,944 island graveyards, dev
    `cavecheck all`): each island has 81 type-3 locations, all Graveyard01 pieces with rotation 0, each with one
    graveyard_launch placement whose box minimum is (−22.0, −26.4, −6.4) m from the piece whatever its quarter turn.
    The plinth top is 22.0 m below the piece on every graveyard; the arrival (the area's offset (0, ·, 6) from the
    centrepiece, turned with the template like its props) is the same spot of the template everywhere, on the plinth.
    The client's own beginning location (AddCave FUN_143f813e0 / GetRandomCaveBeginningLocation FUN_143f84420) is
    the piece origin itself, with no offset of its own. The plinth stands in the generated cavern's air: the client's
    own floor is 4.0-4.4 m under the arrival, and no floor within 40 m is lower than the stamped box's bottom (5.4 m).
  - **Which graveyards random trips use** (bug 2026-10-18: a trip to graveyard 38 of 406, 2,028 m below a 1,423 m
    mountain, fell through the world the moment the loading screen dropped; one to graveyard 24, 747 m deep, stood):
    arrival at y >= −650 as before (deeper caverns were still loading 35 s after arrival and the player fell, audit
    2026-10-14), at most 1,600 m below the landscape's ground (the trips that fell were 1,768 m and more below it, the
    ones that worked at most 1,419 m) [I], more than 100 m below it, the client's own floor at most 6 m under the
    arrival. 1,568 of the 1,944 island graveyards pass (125 are below −650, 251 too deep below the ground); the cavern
    fallback trip uses the same two depth rules.
  - **Fall guard** (`Game/CavernArrivalGuard.cs`; setting `CavernArrivalRescue`, Docker `CAVERN_ARRIVAL_RESCUE`,
    default on; ours): in the first 30 s after a trip's loading screen dropped, a player reported more than 12 m below
    the arrival within 60 m of it has fallen through the world (nothing there is that low) and is re-zoned back to the
    arrival; a second fall goes to the landscape's shallowest open graveyard; a third is only logged. Positions from
    behind the loading screen never count.
- **Right-click menus** (`Game/RightClickMenus.cs`): the server chooses the buttons for players, friends, party and
  guild members, props and chat tabs, from the client's RadialMenuButtons.txt.
  - **Prop name while the menu is open** (`Game/Props.MenuName.cs`; setting `BuildMenuPropName`, Docker
    `BUILD_MENU_PROP_NAME`, default on): right-clicking a prop in build mode puts its name over it (the owner's own
    name for it, else the prop's name) for that player only, with PlayerUpdate 0x7C UpdatePropName; no respawn, so
    movers and platforms keep running. The client sends nothing when the menu closes (the X, Escape, clicking away
    and Help send no packet), so the name goes with the next prop request (any button that sends one: Reposition,
    Adjust, Duplicate, Send to Inventory, Recycle, Delete, Settings, Light on / off, Script), another right-click,
    leaving build mode, a zone change, or 10 seconds after the menu opened. Put back is the name the prop carries for
    everyone: a renamed prop's name, a mover part's blank " ", else " " (an empty 0x7C name makes the client look up
    the spawn's name text id instead). Area Tool props (client-named "area") are left as they are.

## Gathering (`Game/Gathering.cs`)

- **Trees:**
  - They are server entities, generated per 64 m region from a seed. The forest type depends on the biome; slopes,
    the sea and build sites are skipped.
  - Each region is sent within 160 m and removed beyond 224 m.
  - Chopping (axes 1020, 176): 5 hits (8 for large trees), 2–4 wood per hit (Plain, Burled for large, Striped for
    volcanic). Tree Fiber and Sap are a chance.
  - What happens to a felled tree is the respawn model (below); felled trees are saved.
- **Placement sets** (`Game/ProceduralPlacement.cs`, when switched on): on Desert, Tropical, Old Growth and Tundra ground the trees come
  from the client's own placement rules (gameData `ProceduralPlacementSets`, `…SetEntities`, `…SetsSetEntities`,
  `…SetMaterials`, and the mask bitmaps in `CommonData/ProceduralPlacementTool/Masks`). The client never reads these;
  the live server did.
  - `PROP_ID` is a PropDefs prop; its model comes from the prop definition dump. `MATERIAL_ID` is a VoxelMaterials
    id; the baked height grid's surface code is that material's `ARRAY_INDEX`, so "soil" is checked against the
    real ground.
  - The sets name the 2014 biomes (18-31). A launch biome uses the sets of the `_Large_001` biome with the same
    `BIOME_TYPE_ID`. Volcanic, Deciduous and the ocean shore have no sets and keep the old lists.
  - Per set: one candidate per `AVERAGE_DISTANCE` cell, at least `MINIMUM_DISTANCE` apart. Each candidate is then
    checked against:
    - slope in degrees;
    - altitude in global metres (local + 2000);
    - soil;
    - the mask, where a pixel is (local + 4000 m) × `MASK_SCALE`, wrapping;
    - the trunks already placed by higher `ENCROACHMENT_PRIORITY` sets.

    The entity is picked by `SPAWN_CHANCE`. Scale, heading, pitch, roll and offsets come from the set's ranges (entity
    offsets in cm). `ORIENT_TO_SURFACE` tilts the entity to the ground.
  - Wood: Desert gives Striped, Old Growth Burled, Tropical and Tundra Plain.
  - A respawn comes after the set's `MIN..MAX_RESPAWN_MINUTES` (12–18 on most sets; 30 minutes for the old lists).
  - Everything is a hash of landscape, set and cell, so the result is the same after a restart.
  - Set nodes use key indices 128–255 of a region.
  - Settings (WorldServer.json or environment): `ProceduralPlacement` (default off, because the sets name only 2014 alpha biomes and the launch-biome match is inferred, L113; off = the old lists everywhere),
    `ProceduralPlacementDecor` (default off; also places the sets' rocks, bushes and flowers, which can't be
    gathered), `ProceduralPlacementMasks` (default on).
  - Details and the density check: research/trace/0695/procedural-placement-695.md.
- **Respawn model** (`Game/Gathering.Respawn.cs`; setting `ProceduralRespawn`, Docker `PROCEDURAL_RESPAWN`):
  - `minmax` (default) is live's model, from a developer's forum post on respawns (forum 55352, 19 Jun 2015): every
    spawnable type has a minimum and a maximum per island, per layer and per biome; at start a random count between them
    stands on random allowed spots; a cut tree changes nothing while its type is above the minimum; below it another one
    grows back after about 20 minutes at a random allowed spot of that biome on that island, not where it was cut. Spots
    covered by build sites are not used (they look for other spots, same thread #18).
  - A **pool** is one landscape × placement set × set entity (live's server-only EntityListEntities table had
    `MIN_SPAWNS_PER_ZONE` / `MAX_SPAWNS_PER_ZONE` per entity of an entity list; our entity lists are the sets, each with
    one biome). The old tree lists pool per landscape × biome × model. Decoration always stands.
  - **max** = every spot the placement makes on the landscape today; **min** = round(`ProceduralRespawnMinShare` × max),
    at least 1 (default 0.67, Docker `PROCEDURAL_RESPAWN_MIN_SHARE`; the post's 1000 / 1500 example, numbers the developer
    called made up). Example, home continent 406 with the old lists: 17 pools, 45,551 spots (Old Growth pines ~5,300 per
    model, min ~3,550).
  - The spots are counted by a **census**: the same generator run over the whole landscape on a background thread, the
    first time a player is there after a start (0.7 s with the old lists on 406; with the sets 7 s on 406, 15 s on 391).
  - First population: a random count in [min, max] per pool, on random spots; the rest are free spots. Saved in
    `gathered_nodes.txt` (`Z <landscape> <generator>` once rolled, `E <node key>` per free spot), so a restart neither
    re-rolls nor double-spawns (live's restarts reset every spawn). Re-rolled only when the generator changes
    (`ProceduralPlacement`, `ProceduralPlacementDecor`, `ProceduralPlacementMasks`). Saved at most every 10 s, off the
    world thread, and when the world stops.
  - A felled tree frees its spot. While its pool is below min, one respawn per missing tree is queued; when due it stands
    at a random free spot of the same pool, checked again against build sites, the Spire's platform, graveyards and
    players standing there; a spot cut within the last hour is used only when every free spot was, and a pool with no
    usable spot tries again a minute later. Outposts are in the caverns far below, so they never meet a tree.
  - The census (first visit after a start) holds the landscape's trees only for its very first population; after a
    restart the saved free spots apply at once and the census only fills in the counts.
  - Bandwidth: a respawned tree is one AddLightweightProp to the players who have its 64 m region; nothing per tick.
  - `inplace`: the older model — a felled tree grows back where it stood after its respawn minutes.
  - Dev commands: `treepools` (pools of your landscape, most short of their minimum first, in console.log),
    `treepools fell <n>` (fell n standing trees of the nearest tree's pool, no drops), `treepools now` (queued respawns
    due now).
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
- Player Studio designs (live's "template instances", client text 563373): a bought design goes only on the player's
  own sites, and each placement is recorded (`site_template_instance`, `Game/TemplateInstances.cs`). Copy and Save
  Design by anyone but the site's owner are refused (0x1A) where the box overlaps one, even with Get Inspired. A record
  lasts while its site exists ([I]: undo / recycle don't clear it; dev `psinstances clear` does). A bought design
  can't be exported to an item either.

## Claims / build sites (`Game/Claims.cs`)

- **Placing a site:**
  1. "Place this build site" in Site Management (ClaimsPacketStartClaimPlacement), or right-click an old Build Site
     Flag item (it is no longer needed or used up: 14 Jul 2016, P6081-P6085).
  2. The server answers with `InteractionClaim`: result 2 starts placement; a refusal code shows the client's own
     text instead (10 = already at the site limit; the 0.589 server also refused 6 = no flag, 28 = Pioneer landscape).
  3. The client shows the placement view, checks the spot itself (buffer, below) and sends `PlaceClaimRequest`,
     then `Confirm`. A refused request (result ≠ 1) shows its text and puts the client back into placement.
  4. The server answers with `PlaceClaimComplete` (result 2) and the record. The site is the launch size, 210 × 180 ×
     210 m (525 × 450 × 525 blocks) around the requested centre: the client's constant `ClaimCursor.ClaimSize`, which
     also sizes its placement cursor. Older 70 × 100 × 70 m sites are grown at server start when there is room.
- **Buffer:** no new site may come within 285 m of another root site's centre on any axis
  (`ClaimsManager.BufferDistanceToNonfriendlyClaim`). The client applies this itself once it has the site list
  (`SendAllClaimBasicInfo`); the server checks it again (result 26, "too close to another build site").
- **Permissions:** the owner (everything), a player's Special Access entry (No Touching = an entry of 0, which still
  wins), else the site's public Default Access — the client's own rule (protocol.md §10). The window's group and guild
  Default Access masks are stored and sent back but, as in the client, never applied. `MayEdit` is used by mining,
  building and props; chests, prop use / moving, copying and Play Mode switching test their own bits.
- **Management window:** management data, upkeep data, rename, description, permissions, delete.
  - Delete reverts the site's terrain and returns its props (no flag comes back: sites are slots).
  - A site's inventory (`Game/ClaimInventory.cs`) is the props on it that were placed from bag items, plus items an
    admin added with `/item claimAddItem`; `/item claimItemToInventory` moves one to a player. Deleting a site
    returns the added items too (bag, then overflow). When the Environment Server is away a delete or a build edit is
    refused with the client's "Connection to the Build SiteServer is unavailable" (Claims 0x12).
  - 0.695: sites are slots. The window lists the player's sites, then one "Place this build site" row per free slot,
    then an "Unlock More Build Sites" row; Place (`ClaimsPacketStartClaimPlacement`) starts placement without a flag,
    and the rows are re-sent (management data + upkeep data) whenever the player's site count changes.
- **Upkeep (Active Days):** a site stays while its owner logs in at least once every N days: N starts at 7 and grows
  by one for every two unique login days, up to 90 (the launch rule). The day it grows the owner gets the client's
  "timer extended" letter; an owner away until 3 days are left gets a reminder letter; when the time runs out the
  site is packed into Build Site Contents and the owner is mailed (checked hourly).
- **Event islands closing:** with `EventIslandExpireOnClose` (Docker `EVENT_ISLAND_EXPIRE_ON_CLOSE`, default true = live),
  a start where the Events world's island differs from the last run's expires the sites left on the island that
  closed the same way (design, ground back, contents mailed; the letter says the island closed), as live did
  (forum 58077). Each one is logged; with false they stay and the start-up log lists them. See terrain.md "The Events
  world".
- Sites belong to a landscape (`claim.zoneId`). A player is sent the sites of the landscape they are on, at zone-in
  and again after every re-zone (a full AOI update replaces the client's list). `RequestAllClaimBasicInfo` is
  answered with the light site list `SendAllClaimBasicInfo` (protocol.md §10), which feeds the map and the client's
  placement check.
- **Walking onto a site:** the 0.695 client reports it (Claims 0x63 PlayerEnteredClaim, no answer). The report counts
  (site visits for Build Site Tourist and the visit goals, once per site a day) only when the server's position of the
  player is on that site or within 4 m of its box: at the border the server's position can be one movement update
  behind the client's. Reports for a site the player is nowhere near are ignored.
- Pioneer lands (zones 464-468, see "Landscapes") take hand-placed sites (29 Sep 2016 update; 0.589 refused them).
  They hold the sites given to new players (`Game/Pioneer.cs`): a character logging in for the first time (no saved
  position, no site) gets a site on the Pioneer land with the fewest sites, on the free 390 m grid spot nearest its centre with dry, gentle ground, and
  starts in its middle. This needs that land's baked heights (`tools/terrain`); without any, new players start at the
  Spire. When the Pioneer surfaces are full, the site goes into one of a Pioneer land's isolated caverns, at most one
  site per cavern (the client's type-2 cave beginning locations; docs/terrain.md "Isolated caverns"), and after that
  onto a classic landscape.

## Props and crafting (`Game/Props.cs`, `Game/Crafting.cs`, `Game/CraftingStations.cs`, `Game/RecipeBook.cs`)

- **Props:**
  - Placed from the build palette or by using a prop item, and paid for with the prop's recipe components.
  - Moved with the gizmo (Save), picked up, deleted. They are persisted and shared with nearby players.
  - **Placing from the bag opens Build Mode** (`Game/BuildModeHolster.cs`; setting `PlaceOpensBuildMode` /
    `PLACE_OPENS_BUILD_MODE`, default `true` at the user's request — **live did not do this**, `false` = live). When a
    prop item's placement starts (the bag's right-click → InventoryPacketActivateItem → `Props.BeginPlacement`), the
    server runs the client's own `Voxel.SetEnableBuildingMode(true)` through client Lua, guarded by
    `Voxel.AllowBuildingMode()` exactly as Tab is, with the build widget on its Props page. The client answers with
    CommandPacketBuilderMode {1} as for Tab (the held item is put away). When that placement is placed (first Save) or
    cancelled (Escape → PropPickUp, re-zone) build mode is closed the same way a second later — unless the player
    switched build mode off/on themselves meanwhile or started another placement from the palette. Only a build mode
    the client confirmed opening (its CommandPacketBuilderMode {1} within 2 s of the server's open) counts as the
    placement's: if the client's guard said no and the player then presses Tab, that build mode is theirs and stays
    open. The close runs on the world tick. Already in build mode: nothing changes. Off a build site the placement is refused first, so nothing opens. Why Lua: the 0.695 client
    switches build mode only in FUN_14346cc90, reached with "on" only from Voxel.ToggleBuildingMode /
    SetEnableBuildingMode, the Tab key command and the zone-in restore from SendSelf +0x286 — no server packet turns
    it on mid-session, and neither the bag's ActivateItem (FUN_1435dd400) nor the placement start calls it.
  - **Streamed by region** (`Game/Props.Stream.cs`, bandwidth audit 2026-10-18 R8). Each landscape's placed props are
    listed by the 64 m square their origin stands in; a mover pack's parts in every part's square, so the whole pack
    comes and goes together. A player is sent the props of the squares within `PropStreamRange` (Docker
    `PROP_STREAM_RANGE`, default 400 m) at zone-in and after every re-zone (nearest square first, all at once), then
    once a second the squares they walk into (at most 200 props a second, nearest first) and loses the squares more
    than 80 m beyond the range (the prop's effect off, then RemovePlayer, as a pick-up). `0` = every placed prop of the
    landscape at every zone-in, as before. Why 400: the client has no prop draw distance, only the camera far plane =
    the Render Distance option, whose slider gives 120 + 2.81 × % metres (≈ 400 m at 99 %) and unlimited at 100 % and
    by default; a player on "unlimited" sees props appear at 400 m, as trees appear at 160 m. Live's own range is not
    known.
  - Everything said about a prop goes only to the players it is streamed to: placement (to those holding its square),
    settled moves (a move into another square adds or drops it for each player), pick-up / delete, tint, name, on /
    off, sign text, Play Mode's hidden story tools, the Respawn Point's look, story Links' door moves and lights,
    click-link flags and moving-platform positions. A prop streamed in later gets its current story look (an opened
    door plays its open move, a Link-switched light its effect, counters their count).
  - A re-zone deletes every entity on the client, so the stream starts again from nothing when the re-zone goes out
    (nothing is streamed to a loading client); a short move (no loading screen) keeps it and the new squares follow once
    the arrival has settled. Trigger volumes (AreaDefinitions) are still sent for the whole landscape (~135 B each):
    their trigger runs on the server, the client needs a Linking Area's prop only to edit it with the Area Tool.
  - Dev `propstream [<metres>|off]`: sets the range until the restart (applied on each player's next tick) and logs
    each targeted player's stream (squares held, props streamed / placed on the landscape).
  - Moving platforms (Magical, Glass, Three-dock; `Game/Props.Platforms.cs`) travel from dock to dock: speed =
    the "Speed Adjustment" setting × 0.01 m/s (160 → 1.6 m/s), a rest of "Delay Adjustment" whole seconds (2) at each
    dock. Players within 120 m get a position update at each start and stop and, while it moves, every 1 s on a leg
    without a turn (the client extrapolates along the record's velocity with no limit: Network.ExtrapolationLimit 0 →
    +infinity, OPEN-LEADS L186) or every 0.5 s on a turning leg (angular velocity is sent as 0) (they stay
    viewers until 130 m; one who leaves while it moves gets a last update with zero speed, since the client rebuilds
    a moving prop's collision every frame); nobody near, nothing is sent. Switched off (prop menu or a Link in Play Mode) it stops where it is. The moving part is
    spawned with control mode 2, as creatures are (with 0 the client queues its position updates and never plays
    them), and with no owner (the client ignores server positions for a prop the local player owns, so the pad never
    moved on its owner's screen); the server keeps the real owner (protocol.md §4.2, OPEN-LEADS L135).
  - Mover parts (platforms, docks, Translocation Pads, flingers, launchers) carry a static area and an area guid. The
    client names an unnamed spawn with an area guid "area" and shows that over it, so they are sent a blank name.
  - Translocation Pads, and arriving by re-zone over placed props: see "Teleports".
- **Crafting stations** are props mapped to `CraftingStations.txt` by name or model keyword. Clicking one goes
  through interaction flagging → `InteractRequest` → `CraftWindowEnable`. The client never closes a station window by
  distance; the server does (RecipePacketZoneCraftingWindow 0x42/0x24 `{u32 sub, u32, bool 0}`, the client then sends
  CraftWindowEnableRequest 6 itself) when its player is more than 8 m from the station or the station prop is gone,
  twice a second (`CraftingWindowCloseAway` / `CRAFTING_WINDOW_CLOSE_AWAY`, default true; dev `closecraft`). Recipe
  cooldowns (PlayerRecipeDataUpdate 0x42/0x1E) are not sent: every recipe's `COOLDOWN_TIME` is 0 in the client's own
  Recipes.txt.
- **Crafting** checks and consumes the components and gives the output. Recipes are learned from recipe items.
  A recipe that lists the same item in two component rows (18 recipes: the RECIPE_ID rows of RecipeComponents next to
  the RecipeRecipeComponentMaps rows) needs their sum. A craft still running when the player leaves the world is
  finished into their inventory at logout (every recipe takes at most 1 s per item).
- **Lumen Station unlocks** (OPEN-LEADS L43): the 581 recipes with a `VISIBILITY_REQUIREMENT_ID` (the "Recipe:"
  items, the fireworks emitter recipes, the Dungeon Builder's Starter Set, the Vault / Inventory / Design Expansions)
  are one-time purchases. Buying one that would add nothing is refused before any Lumens are taken: a recipe item
  whose recipe or prop is already known (568756 "You've already learned this prop!" for props, else 560768 "Error:
  Failed item requirement"), one already held unused, more than one at a time, a Starter Set while an unopened one is
  held, or an expansion past its most (counting unused copies; the recipes' `MAX_QUEUE` 3 / 3 / 7 is exactly the
  number of steps). The text comes back as the StartResponse string, which the client prints in chat.
  - Owned unlocks are **hidden** from the Lumen Station's list (since 2026-10-18): the client never evaluates
    `VISIBILITY_REQUIREMENT_ID` and hides recipes only from a server-sent list (SendSelf, then RecipeBase 0x1F hide /
    0x20 unhide; sendself.md "Crafting block"). The server lists a "Recipe:" item whose recipes or prop are all known
    and an expansion at its most, and updates the list when a recipe item, prop or expansion is used. A held unused copy
    does not hide it (the purchase is refused instead), nor does the Starter Set.
  - The recipe records carry ALLOW_CATALYST, TRACKER_ACHIEVEMENT_ID (the window's "tracking" mark for the Replicator
    goal) and MAX_QUEUE from Recipes.txt (protocol.md §12).
  - **Watch button** (crafting window; since 2026-10-18): enabled when the recipe's tracker id is not 0, it calls
    `ObjectivesController.SetTrackingAchievement(id, on)`, the same as the Journal's watch: the client adds or removes
    the goal in its tracked set and watch list itself and sends TrackerUpdate (0x92 sub 0x0C); the server keeps the
    set (`character_unlock` kind 8) and sends it back at login (TrackerInitialize). The id is sent only for the 186
    recipes whose Replicator goal exists in the live capture; the other 177 tracker ids (free recipes such as Smelted
    Copper; the 20 Apr 2015 notes stopped tracking free recipes) have no goal, so the button stays disabled. A goal
    that completes (crafting the recipe once) leaves the tracked set, as it does in the client.
- **Creature props** (`Game/BarrenRecipes.cs`): the twelve non-elite Barren creatures are not innate (wiki "Innate:
  No"). The server builds twelve "Recipe: Barren …" items from the client's own texts (574376-574399; own ids
  900701-900712, a copy of 3561 "Recipe: Elite Barren Captain" with the creature's icon). They are in every random
  recipe drop, and killing a Barren (Outpost garrisons, props 2702-2706) also has a 1 % chance of one the killer
  doesn't know (30 % from a Barren Outpost Elite; rates ours). The Dungeon Builder's Starter Set still gives two
  Flameblades and a Bowman as bag items, which place without the recipe.
- **Colonist's Elegant Robe (Green): two items, one route** (TASKS 5.12, closed by design). The client has two
  outfits of that name. Item **4162** (tint Colony_Light_04) is the one the wiki says the Lumen Station sold for 520
  Lumens: recipe 5120, default-learned, on the Lumen Station (55). That is how this server sells it. Item **2551**
  (tint Colony_Light_11, a different green) is an older copy, and live gave it no route anyone can find:
  - its recipe 2545 costs the same 520 Lumens (component 5832), but it is not default-learned, it is on no crafting
    station, and no item teaches it. This holds in both the 0.589 and the 0.695 client tables, so the client could
    never list it;
  - no goal gives it (the live AchievementInitialize capture);
  - the wiki has one Green page (the Lumen Station one). For Red, where both copies were on the Lumen Station, the
    wiki noted "2 versions", and Daybreak renamed the darker one Russet (28 Jul 2016, P6193). Nothing like that
    exists for Green;
  - the forum, the patch notes and the server's loot tables never name it.

  So 2551 gets no route. An owner who wants it can price it with `STORE_PRICES=2551=<Lumens>` (or turn on `FREE_STORE`).
  The free store lists one item per name, the lowest id, so it shows 2551 under that name and not 4162. The same goes
  for Colonist's Elegant Robe (Teal): 2631 is a goal reward ("Roll for Initiative!") and 4165 is sold at the Lumen
  Station. Both have a route.

## Founder's Pack and other account items (`Game/Entitlements.cs`; TASKS 6.59)

Live gave some items to accounts, not for playing: the Founder's Packs (2014 alpha / beta), the Year Zero gifts (mailed
on the alpha's first anniversary) and 2014 giveaways / Fan Faire souvenirs. This server gives them to **every
character, once** (WorldServer.json / `.env`):

| Setting (`.env`) | Default | |
|---|---|---|
| `FounderEntitlements` (`FOUNDER_ENTITLEMENTS`) | `true` | `false`: nobody gets them, and their props leave the prop palette (items already handed out stay and can still be placed from the bag). Live gave them only to buyers, survivors and winners; `true` is this server's choice (user 2026-10-17 / 10-18) |
| `EntitlementPackages` (`ENTITLEMENT_PACKAGES`) | `founder,yearzero,promo` | which packages |
| `EntitlementDelivery` (`ENTITLEMENT_DELIVERY`) | `auto` | `auto` = as live, per package: `founder` into the bag (founders found the pick on logging in, wiki "Founder's Pickaxe"), `yearzero` by mail (wiki "Year Zero Trophy"; the client's own letter), `promo` by mail (live's way not known). `mail`: every package as letters from the Landmark Team (four items per letter); `bag`: every package into the bag. Per package: `founder=bag,promo=bag` (a package not named keeps its `auto` way). Whatever doesn't fit the bag is mailed |

| Package | Items | Also |
|---|---|---|
| `founder` | Settler / Explorer / Trailblazer Flag, Void Vault, Settler's / Explorer's / Trailblazer's Pickaxe, Tech Commander's Gear, Tech Sergeant's Gear, Noble's Regalia, Courtier's Regalia | learns the recipes for the flags (Workshop) and the three pickaxes and their Change Mods (Stone Forge) |
| `yearzero` | Year Zero Trophy and one Year Zero Survivor's Outfit (random colour) | the client's own Year Zero letter |
| `promo` | Iron Portcullis, Fan Faire 2013 / 2014 Souvenir Flag, Layers of Landmark | |

A character gets each package once (`character_unlock` kind 26 = the package id); `clearunlocks 26` (dev command) and a
relog grant them again (a character that already had the founder package before the Regalia were added gets them only
after `clearunlocks 26`). The founder outfits are server-built item records (`Game/Cosmetics.cs`, ids 900341-900345):
the client has their names, icons and meshes but no item rows. The Regalia meshes are Trailblazer_000 (Noble's, the
cloaked one) and TrailBlazer_001 (Courtier's), matched by their icons 754 / 755 and the wiki's descriptions; tints are each
mesh's default. Not given (no item record exists): Mastercraft Bracer, Ring of Bounty, Mega Pocket, the original
Founder's Pickaxe, the title Founder.

**Founder's Chest** (prop 898, at every Spire beside the Resupply Chest): it gives back missing founder items (hotfix
28 Mar 2014; wiki Spire 2016). Clicking it gives back the founder
package's soulbound (NO_TRADE) items the character holds nowhere — not in the bag / vault / equipment / overflow, not
placed by them, not in their chests (or chests on their sites, or a picked-up chest in their bag), not in a letter —
into the bag (the rest by mail), and re-teaches the package's recipes. Answers are live's own texts: 562158 "Retrieving
your missing Founder's Pack items…", 562157 nothing missing, 562156 no Founder's Pack (founder package switched off),
562130 refused while lapsed-site contents wait. The client's Spire template has only the Resupply Chest (same model,
724), so the server adds 898 1.6 m beside it (live's spot is not known). The mailbox is read once per click off the
world tick, and a second click within 5 s is ignored. Dev `founderchest`.

## Storage chests (`Game/Chests.cs`)

- Craftable chest props (PropDefs with a prop recipe and "Chest" in the name) hold items: small 10 slots, large 30,
  the rest 20. Contents are in `chest_item`.
- Clicking a chest opens it (EnableWindow → the client asks for the list). Its owner can always use it; others need
  Pillage & Plunder on the build site it stands on.
- Items move with the normal drag and drop (TransferItem, owner type 3). Everyone looking into the chest sees changes.
- Rules from the live game's update notes: at most 50 chests per build site (21 March 2016; Build Mode shows
  "n/50 Chests"); an extra cap per account (all the account's sites together) only when the owner sets
  `ChestAccountCap` (`CHEST_ACCOUNT_CAP`, default 0 = none; the wiki's "50 per account" is not in a developer source);
  chest access is the
  "Customer" permission (26 April 2016), the 0.589 client's Pillage & Plunder level (the 0x04 bit).
- Picking a chest up keeps its contents inside it (Daybreak kept chests from being recycled so stored items could not be
  lost): it comes back as its own bag item, and the items follow it when it is
  placed again. A chest holding items can't go into another chest or be traded, and can't be deleted in place.
- A stored item stays the same item: it keeps its wear, and a Player Design item keeps its design (taking it out moves
  the design onto the new bag item, as a trade does).
- Non-tradeable items: only the site owner may put them in (0x20, text 561997), and each comes out only for the player
  who put it in (`chest_item.depositorGuid`; older rows: the chest's owner); anyone else gets 0x21, the client's 561999
  "Can't take non-tradeable items that didn't originally belong to you." (0.695 FUN_142f202e0).
- Not built yet: locking and unlocking chests (May 2016 notes), the tooltip list of a carried chest's items.
- Opening a Simple Medium Chest counts the Props To You goal.
- Dev commands: `chestopen`, `chestput <item> <n>`.

## Social

- `Game/Social/`: the seam for the shared social service (research/trace/architecture/social-service.md §6 step 1). Code
  outside Friends / Ignores / Guilds / Chat / ChatRooms / Mail reaches them only through `SocialServices`:
  `Service` (`ISocialService`: world entry / exit, friend / ignore / guild / room / mail requests, routed chat lines,
  system mail, "item waiting in mail") and the zone's read-only caches `IgnoreCache`, `GuildCache`, `FriendCache`,
  `Presence`. By default all of them are `LocalSocialService`, which runs the code below in this process. With
  `SocialAddress` / `SOCIAL_ADDRESS` set they are `RemoteSocialService` (§6 steps 3-5): presence, friends, ignore
  lists, guilds, routed chat, chat rooms and mailboxes are the `social` container's (LandmarkEmulator.SocialServer,
  docs/docker.md "Social service"); the world announces its players (PresenceSnapshot, CharacterEnteredWorld /
  LeftWorld, ZoneChanged), hands the friend / ignore / guild / room / mail requests and the checked chat lines on,
  queues the client packets the service sends back (TunnelToExternal / List) to the sessions, and keeps the ignore
  lists (IgnoreListSync, into `Ignores`' table), friend-of lists and guild membership / mute bits
  (SocialStateForZone) for its own checks. The zone keeps say / shout / emote / party chat (`Chat.Handle`); tells,
  guild, officer, public and room lines go to `RouteChat` (`Chat.Route` while local).
- Mail with the remote service (`Game/Social/RemoteMail.cs`, messages in `LandmarkEmulator.Internal/Social/
  MailMessages.cs`): the zone answers attach-to-compose (a bag check), reads and checks a compose against the bag,
  takes its items and coins into escrow and sends ComposeRequestStage2; the service checks recipients, ignore, inbox
  size and send limit, stores it and answers the client, then tells the zone to drop the escrow (Stage3) or give it
  back (ComposeResponseReturnAttachments; returned by mail if the sender has left). Taking attachments: the service
  applies the cash-on-delivery rules and asks the zone to hand the items / coins over or charge the COD
  (ConsumeAttachmentZoneRequest / Response), then marks them taken. The reading window's packet is written by the
  zone (`Mail.SendRead`) from the service's MailReadData, since it carries item records. Inventory changes from the
  link run on the world thread (`SocialServices.Update`).
- `Game/Chat.cs`: wire channel = ChatChannels.txt row − 1 (0 Say, 1 Tell, 3 Shout, 4 Emote, 5 Guild, 6 Officers,
  8 Party, 9 Public, 10 player rooms). Say and emote lines reach 100 m, shouts 500 m in the same landscape, party
  the group, guild the guild, officers the guild ranks holding JoinOfficerChat, rooms the room, General (public
  channel 0) everyone who has not left it (`PublicChannels`, ChatChannelOff / On; for the session), the rest everyone.
  A chat bubble colour (`ChatBubbleColor`, Command 0x15) is saved on the character and shown to the landscape.
  Channels only the server writes (2 TellEcho, 7 System, 11 Error, 12 Loot, 13 Combat) are dropped when a client
  sends them. `Chat.System` sends a server line.
- Guild ranks also carry three permissions the client checks but its rank editor never shows (string table next
  to Guild.MaxMembers): JoinOfficerChat (the client joins channel 6 with it), MuteGuildChat / MuteOfficerChat
  ("/guild_debug mute_member <guild id> <member guid> [guildchat] [minutes]" → GuildRequestMutePlayer 0x83/0x10).
  Leaders hold all three, the stock Officer rank the first two. Guilds hold up to 250 members, parties 4.
- Mail limits are the compose window's (subject 128, body 4096); messages expire after 30 days (the client's
  Mail.ExpireTimeMinutes) and are deleted then unless they still hold items or coins. One attachment carries at most
  `MAIL_MAX_STACK` (9,000, wiki Trading) of an item; more is "attachment is invalid" (5). Store purchases in their
  waiting period can't be attached (0xA).
- Trades end when a trader changes landscape (TradeCanceled) or enters a running site game (codes 8 / 9). The bag
  check counts stacking onto the receiver's own stacks.
- Emotes and stances (`Game/Emotes.cs`; wire in docs/protocol.md "Sitting"): /sit is the client's own, the stances
  (/kneel, /dance, /skip, /drunk, /sneak, /playdead) and /stand are the server's. **With a tool or weapon in the hand
  (a pick, an axe, a sword ...) no sit or stance shows**, on the player's screen or anyone else's, although /sit still
  prints "You sit down.": the held item puts the character's animation network in its weapon / tool idle, which has
  no stances (client rule, so live did the same; protocol.md). Put the item away first, then /sit.
- `Game/Friends.cs` (and the social service's `FriendService.cs`): friend lists with online/offline notices, stored
  per direction in `character_friend` (`friend` in lmemu_social). A request ("/friend add Name" or the player
  menu) shows the other player the Friend Invite popup; **accepting makes both friends**: the asker's list gets the one
  who accepted ("[player] has accepted your friend request."), the accepter's list gets the asker ("[player] has been
  added as a friend."), each at once with the other's online state, on whatever worlds they are (live: wiki Friends
  List, people "you either sent friend requests to or from whom you accepted friend requests"; the client never adds a
  row by itself). `FriendRequestsMutual` (`FRIEND_REQUESTS_MUTUAL`, default true; with `SOCIAL_ADDRESS` the social
  service reads the same line) false keeps one-way lists, where only the asker gets a friend. Each list holds 200
  (ours): a full side gets no row and "Your friends list is full.", the other side still gets its row. An ignore either
  way (also one made while the popup was up) stops the accept with nothing said (as the 7 Aug 2014 Steam friends
  import: "as long as they aren't ignoring you, and vice-versa"). Removing a friend — the friends window or
  "/friend remove Name" (by name) and the player right-click menu's Remove Friend (Ui.RemoveFriendByGuid, by guid) —
  takes them off the remover's list only (no source shows a removal reaching the other list; the client has no "removed
  you" text). Admin `/friend forceadd` stays one-way. Rules: `Internal/Social/FriendAccept.cs`,
  `Internal/Social/FriendRemoveRequest.cs`. Friendships made before mutual requests (2026-10-18) stay one-way until
  asked again.
- `Game/Groups.cs`: invites (the popup's Accept / Decline come back as a GroupsBase Join), the party window, leader, kick, leave, disband.
- `Game/Guilds.cs` (and the social service's `GuildService.cs`): create, invite, ranks with permissions, MOTD, online
  flags, persisted (`guild`, `guild_member`). The invitee's client queues the invitation popup (GAQGuildInvite, with the
  guild's and inviter's names) itself from the invitation (type 1, addressed to it) in UpdateInvitation 0x83/0x17.
  Invitations live 5 minutes on the server. When the client's UI shuts down (logout, idle logout, exit) it sends its
  unanswered queue entries back as ListNotifications sub 1 (FUN_1433302e0 from GuiOnShutdown) {u32 n, n × Data}; the
  server keeps the invitation itself: at every world entry GuildInvitations 0x83/0x14 replaces the client's invitation
  list (a still-valid invitation comes back with its popup). A player in no guild gets an empty GuildDataFull 0x83/0x12
  at entry (empties a guild kept from before a relog). When the News tab's 100-row cap drops old rows, online members
  get GuildRemoveLogUpdate 0x83/0x1B for them.
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
  - Fiery Fungus arms when a player comes within 3.5 m (or when hit): plays 10918 immediately (buildup then boom at
    1.5 s in that composite) plus the attack anim for the mesh inflate; at 1.5 s it deals damage (impact 10922) for
    everyone within 5 m and leaves dirt and stone.
  - Slaugs chase and hit.
- Cavern creatures walk the cave floor and react to 3D distance, so players on the surface above don't wake them.
  Health, damage and loot are ours: live's were server data. Loot is Monster Essence, the affinity essence and
  Adventure Essence.
- **Creature pathing** (`Game/Pathing.cs`, TASKS 6.61; research/trace/architecture/pathing.md). Live ran a pathing system with
  navigation meshes built from the voxel terrain (the client's admin page 32 "Pathing Information - Zone": NavMeshCells,
  stitch sections, silhouettes, avoidance obstacles, nav-mesh jobs); its format was server-only. Ours is a walkable
  column grid, built from data the server already has:
  - **Columns** 1.2 m wide (Sight's ruin block). Each keeps its floors: the bottom of open air at least 2 m tall, with
    its ceiling. Cavern / outpost columns come from `Sight.ColumnAir` — the client's cave pieces (`CavePieces.AirSpans`)
    with every stamped outpost's ruin grid over them, the same solidity line of sight uses. Surface columns come from the
    baked ground heights.
  - **Tiles** of 16 × 16 columns (19.2 m), built on first need by one low-priority background thread (about 0.1 ms
    each) and cached, at most 3,072 (~2 KB each, least recently used dropped). Stamping an outpost drops the tiles under
    it (`Pathing.OutpostsChanged`). Player edits are read on build sites and in dug outposts only (below).
  - **Moves**: to a neighbouring column (8 ways; diagonals only past two open sides) onto a floor at most 1.25 m higher
    or lower with 2 m of shared headroom.
  - **Walking** (`Creatures.Walk`): straight when the straight line is walkable on the grid (the cheap common case);
    else A* on the background thread (at most 6,000 nodes, within 96 m), the path thinned to the corners. The world tick
    only reads finished tiles and paths and never waits. A chaser that can't reach its target (a ledge, a wall top)
    stands at the nearest point facing it; going home or wandering never targets a blocked spot. Where the grid has
    nothing yet, or the creature or its target stands where the grid says rock (the data doesn't match the world there),
    the creature steps straight as before (fail open, like Sight).
  - **Heights**: cavern creatures take their height from the grid's floors, so they now stand on outpost floors and stairs
    (the old cave-only floor search ignored the ruins); a client's height report (CorrectNpcHeights) is kept as an offset.
  - **Who uses it**: Chaos Cavern and outpost creatures (grid of caves and ruins), surface creatures off build sites
    (ground heights: no cliffs) and story creatures on their build site (below).
  - **Build sites** (OPEN-LEADS L133; live: a 2016 patch shrank the collision around props, large trees especially, so
    creatures path past them, P5781; 2016 players saw NPCs walk along their walls to the doorways, which had to be the
    width of a normal door, forum 56713): story creatures path around player-built
    walls, through doorways, around dug holes and placed props. Mechanism ours (live's mesh was server-only, L132):
    - the site's edited voxels come from the Environment Server as a solidity grid (`IEnvironmentServer.SolidityOf`,
      docs/terrain.md "Solidity grids"): 1.2 m columns × 0.4 m rows, written solid = rock, written air = a hole,
      unwritten = the generated ground; laid over the walkable columns as the ruin grids are over the caves (surface
      sites over the ground heights, cave build sites over the cave pieces);
    - placed props whose model has a Models.txt PATH_BLOCK_RADIUS (× scale, around PATH_BLOCK_OFFSET; trees, rocks,
      braziers, chests... — most furniture and the crates have 0 and don't block) block the columns within that radius
      from their base to 2 m above it; creature spawners, Linking Areas, game markers and moving platforms / docks
      never block (`Props.PathObstaclesIn`);
    - built in the background on the first step of a story creature on the site (until then it steps straight, as
      before), again **2 s after the last change** there (every terrain change passes `ZoneTerrain.Broadcast` →
      `Pathing.CellsChanged`; props are compared once a second, and a prop-only change re-lays the obstacles without a
      service call); one build at a time; at most 16 sites / outposts held, a site forgotten after 10 minutes without a
      story creature step;
    - a Chaos Cavern outpost players dig into (pick or dynamite, `Pathing.Dug`) gets the same grid of its ruin box for
      its creatures (caves only), forgotten 30 minutes after its last change (the holes heal after 20);
    - nothing is sent to clients; one internal Environment Server call per rebuild.
    - Logs: `Site pathing: site N on Z: grid …×…×… blocks from … stored cell(s): … written, … solid; … prop obstacle(s);
      built in … ms (first build | rebuild … s after the last change)` and, for a story creature whose straight line is
      blocked, `Site pathing: story <kind> 0x… on site N: straight line to (…) blocked, path of … waypoint(s) …`.
    - **Setting** `SitePathing` (Docker `SITE_PATHING`, default on = live; needs `CreaturePathing`); dev
      `sitepathing [on|off]` (lists the grids held), `wall dx dz length height x|z [door] [material]` (a test wall of the
      player's voxels with a doorway).
  - **Setting** `CreaturePathing` (Docker `CREATURE_PATHING`, default on); dev `pathing [on|off]`. The admin web page
    /admin/monitor shows the page-32 numbers ("Pathing Information - Zone"; what each counts is ours).
- The spawn's control-mode byte (AddLightweightNpc, after the three u32s that follow the character model block;
  read to entity +0x41a0 by SetupLightweightNpc 142cbaca0) must be non-zero: the client's per-frame update of a
  proxied character (vtable +0x4c8, FUN_142e130e0) only follows server positions then. Remote players are forced to 2;
  creatures are sent 2. With 0 they stay where they spawned.
- Animals with no damage (deer) flee from players within 8 m or when hit, until 22 m away.
- Name and health over an NPC: HUDIndicatorSettings type 13/1 → indicator code 22 (Npc) when requirement 21 "NPC
  Combatant" (`NotPlayer && NotProp && TargetCanBeHarmed && TargetNotPropProfile`) holds; TargetCanBeHarmed is
  TargetMaxHealth > 0, so the hit points (PlayerUpdate 0x03) are sent with every spawn.
- **Creature weapons** (`Game/CreatureWeapons.cs`; OPEN-LEADS L95, on by default, a best guess): a weapon model in a creature's main
  hand, EquipmentSetCharacterEquipmentSlot (slot 2) for the creature's guid, sent once to each player right after the
  creature's full NPC data (the client finds only full entities by guid) and again when a story creature's weapons are
  Equipped / Sheathed (Story Node "Weapon Visuals", spawner "Spawn With Weapons Equipped", the Equip / Unequip Links);
  Sheathed empties the hand. The client has no table of which weapon model each creature held (live kept that on the
  server; research/trace/0695/agent-assets.md §2), so the table is a best guess with a proof per row ([P] client data, [W]
  wiki / forum / dev posts, [I] ours):
  - **Type [P]**: each creature prop's palette icon names its weapon (ImageSetMappings → Images.txt:
    `Barren_Sword_Fire`, `Edbonguard_Daggers_Earth`, `Lumerian_Blaster_Lightning`, `Giant_Club_Earth` ...; a dev post
    of 15 Mar 2016 says the icons show the creature's type, weapon and damage types). Icons that say
    `Nothing` (monsters, animals, hounds, wisps, giants, golem, elemental) get no weapon. The Barren skeleton's
    animation set replaces the 1H-sword attacks, so its swords are 1H [P].
  - **Model [W] / [I]**: an actor made for the race that no item uses (kobold bow / staffs, DarkElf bow); else the
    players' weapon of that type and the creature's affinity (wiki "Creature Prop": affinity creatures' weapons copy
    the attacks of the players' Affinity equipment), with the item's tint; else the art set of the
    creature's armour (Qeynos for Highfall, DarkElf for Ebonguard, Colony for NovaTech, the giant-sized Ogre set for
    Stoneskin); else a plain actor of that type. Wiki pages that name a weapon are cited (Barren Flameblade "flaming
    greatsword", Scalewind Marine / Highfall Forceguard "a large sword", Stoneskin Stormlasher "lightning infused
    sword", Ebonguard Fireblade "a menacing blade", Barren Fireshot "a flaming bow"; the Target Dummy "swing its sword",
    hotfix 11 May 2016 → the practice sword).
  - **Which creature**: one row per creature prop (with its ruin-template twins); the outposts' ruin spawners
    (`Spawner: Ruins: Barren Melee` ...) take the creature of their model and role; other creatures of a listed model
    take the model's default. Only the main hand is sent (sword-and-shield creatures show the sword).

  **Setting** `CreatureWeapons` (Docker `CREATURE_WEAPONS`, default `true`; `false` = none); dev `creatureweapon
  <guid hex|near> <Wield_*.adr|off> [tint]` puts any weapon actor in a creature's hand whatever the setting (e.g.
  `creatureweapon near Wield_Blade1H_Dwarf_Dwarf_B_A0_000.adr`).

## Weapon combat (`Game/Combat/WeaponCombat.cs`, `Statuses.cs`, `Gear.cs`)

Launch combat (wiki "Combat", "Weapons", "Affinity", the weapon pages; the client's own texts). The server applies
the **held weapon's kit**: its affinity and two named attacks, e.g. Staff of Frozen Sorrow "Basic Attack: Shatter"
(text 574445, description 574446) / "Power Attack: Blizzard" (574447/574448). 21 named kits cover 31 weapon items
(launch weapons and the pre-launch items they replaced); a weapon without named attacks (Lightbringer's Justice) uses
the generic "Basic Attack" / "Power Attack" and its affinity's main state.

- **Which button sends which cast** (`Game/Combat/WeaponInputs.cs`) is the client's own data: the weapon's item record
  lists DynamicInputActionSystems ids (u59) → a state machine (DesignBasedStateMachineDefinition / StateDefinition /
  StatePropertiesDefinition) → input action groups → InputActionGroupEntries (TRIGGER_ID 9 Primary = left mouse,
  10 Secondary = right mouse, 11 Tertiary = E; ACTION_ID = the cast). A state's entry firing moves it to a follow-up
  state (StateMachineMap → StateToStateTransitionMap → TransitionGroupRuleMap → TransitionRuleDefinition type 1), which
  is how the second left click sends the combo id. Every launch weapon: left = the Basic Attack (and its follow-up),
  right = the Power Attack; the right mouse has follow-ups only for the Six-Shooters (801 → 1080, Trick Shot again),
  the Overcharged Recon Blaster (779 → 1077, the channelled volley) and the Electrified Assault Axe (761 → 1181 → 1182,
  Rolling Thunder's leaps). **E** is commented out of the 0.695 InputProfile_Default.xml and no launch weapon has an
  E entry in either build; **Q** is the grappling hook. All weapon entries fire on press (TRIGGER_ON 2, no minimum
  hold); the hold behaviour is the caster info's kind: charged (hold to charge, release to strike), channelled (held
  while the button is down) or instant.
- **Caster info only for the held weapon's casts**: on a weapon change and when Frenzy / Supercharge / Energize start
  or end the server re-sends the 2-5 replies of the ids the held weapon's buttons send (before 2026-10-18: every weapon
  cast id in the input table, ≈120 replies). A cast id the client asks about is described with the weapon that sends
  it (a weapon's tooltip in the bag no longer shows the held weapon's attack names).
- **Held channels** ("Channel …": Unload on the Six-Shooters' left mouse; Torrent, Dark Embrace, Consecrate, the Recon
  volley, and the blocking stances Molten Rebuke / Corrupting Revenge on the right mouse): their caster info has the
  channelled kind 0x216b5dcb, so the client keeps the cast open while the button is held. The server confirms with
  CastContinue (carrying the attack's Enter, then its Release), deals the hits while it is held (at most the attack's
  hit count, each further hit paying the weapon's "energy cost over time"), and answers (CastSuccess, Force off, then
  AbilityEnd) when the button comes up (CancelCast), the last hit lands, the Energy runs out or the player dies. A
  stance stays up while held, at most its 2 s, and ends when it catches a hit. The 11 May 2016 notes' "Secondary
  channeled abilities should now show a cast bar" is the live side of this.

- **Special (E) attacks**: only four weapons have an E cast in the client's input table (trigger 11): Lightbringer's
  Justice 765, Katana of Crashing Waves 768, Desperado's Deadly Arsenal 795, Silent Shadow's Condemnation 798 — exactly
  the four whose recipes never went live (RecipeBook.RetiredRecipeItems). A kit may carry a Special attack for it
  (`Kit.Special`); without one the E cast runs the Power attack (×2.4, 80 Energy).
- **Desperado's Deadly Arsenal** (OPEN-LEADS L163; admin-granted only): three named attacks — LMB 793 Run n' Gun
  (revolver shot), RMB 794 Get 'em Good (shank stab), E 795 All or Nothin' (dive with mid-air shots, then a shank hit
  on landing). Each hits for the AbilityParameterDefinitions row its description names (stat 10 × PARAM_2 + PARAM_3,
  the number the tooltip shows): 703 / 704 / 710 + 708. A landed shot has a 25 % chance of Dirty Fighting (573184,
  4 s, buff bar); the next attack spends it for its stronger form: shot 706 + Snare 2 s, stab bleeds 707 × 3 over 3 s,
  dive shots 711 + Snare and the landing shank bleeds 709 × 3. Snare slows like Cripple.

| Affinity | Properties (wiki) | Kits (weapon: basic / power) |
|---|---|---|
| Fire | Erupt (double damage roll), Frenzy (faster attacks), Burn (no attacks, DoT) | Wanderer's Blazing Bow: Explosive Shot / Immolating Barrage · Frenzied Slayer's Battle-Axe: Fiery Cleave / Conflagration · Flame Bound Bastion: Cinder Strike / Molten Rebuke (block) |
| Water | Flow (stacks: Energy maximum +20 each, plus 10 Energy), Surge (half the energy as damage), Freeze (no attacks or movement) | Staff of Frozen Sorrow: Shatter / Blizzard · Supercooled Power Cannon: Tidal Burst / Torrent · Frost Lord's Fury, Katana of Crashing Waves: Fracture / Frozen Domain |
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
  affinity) and the 21 accessories' property boosts, each from its description (575654-575681, 565621-565743, 574932;
  the same texts in 0.589 and 0.695, so the 7 Jun 2016 passive rework is what both clients describe). Every passive
  that fires logs `<character>: <item> passive: <what>` (WorldServer log). Chances, radii and shares are ours (the
  passive ability ids 1082-1148 are in no client table; OPEN-LEADS L90). Details:
  - **Reinforced Coat of Crashing Waves** (P5748): 30 % when an opponent hits you — the client's ice blast 11533 on the
    wearer, then 0.4 of the weapon damage, Freeze and the ice hit flash 10114 on every opponent within 5 m; no damage
    reduction (removed on 7 Jun).
  - **Harden** passives: the hit that uses up Harden tells the armour (`Gear.OnHurt(hardened)`); Armored Brawler's
    Doublet explodes with the client's own PFX_Combat_Physical_Passive_Explosion_5m (12732, FX "Combat_Earth_Harden_
    Explode_5m"), Unflinching Embodiment of Stone stops the whole hit and deals revenge damage.
  - **Gunslinger's Last Resort** fires when your attack Crushes someone; Tectonic Destroyer's Platemail when you hit
    someone already Crushed (their two texts).
  - **Weaker properties** (wiki accessory pages; the accessories carry the weapons' property abilities 1062-1071 in the
    item data): an accessory whose affinity the held weapon lacks gives that affinity's property at half strength
    (`Gear.WeakerProperty`: Erupt / Regrowth / Rejuvenate at half the chance, Flow / Energize / Infect on half the hits,
    Siphon at half the share), boosted by the accessories as usual.
  - **Flow** raises the Energy maximum (21 Mar 2016 notes: successful attacks raise it for a while): +20 a stack,
    +30 with Sigil of Piercing Floods (its description: Flow grants more), sent as the Energy
    resource's MAX_VALUE variable on every Energy update while Flow lasts and dropped when it ends
    (`WeaponAttacks.EnergyCap`). Spite Etched Bracer marks a target when Siphon happens.
- **Ranged shots** (`Game/Projectiles.cs`): each shot is a Projectiles 0xEE Create / Delete pair (protocol.md §14) for
  the players who see it, with the client's ProjectileDefinitions row for the weapon (OPEN-LEADS L157; matched by the
  rows' bones and effect ids [I]): the Supercooled Power Cannon and the Raygun fire from the shoulder (Water 115 / 114
  large, Affliction 93 lob / 99 large), the Gunslinger's Six-Shooters alternate right 117 / left 118, the Recon Blaster
  uses the left-hand Lightning rows 113 / 101, Night Herald's Mercy 119, the Wand of Boundless Light 112, the
  Wanderer's Blazing Bow 111, the Staff of the Lightning Caller 121, the Staff of Frozen Sorrow 97, Lightbringer's
  Justice 91; pre-launch items with the same kit use its rows. The hit lands after distance / the row's SPEED.
- **Melee swings as the client's Sweep** (`Combat/WeaponCombat.Targeters.cs`; OPEN-LEADS L170): a held melee weapon's
  single-hit, uncharged cone attack carries one Sweep targeter in its caster info (protocol.md §7). The client turns it
  from −half-arc to +half-arc round the player's facing (radius = the attack's Range, target limit = its MaxTargets;
  delay = the animation's earliest strike, duration = the strikes' spread over the combo plus 0.35 s) and reports each
  character it passes through once (ClientTargeterFindTarget), then ClientTargeterEnd. Such a cast does no damage at
  once: each report is checked against the server's own cone (alive, same landscape, within Range + 2 m, inside the
  half-arc + 20° of the swing's or the current facing, in sight for a cavern creature; target limit, one hit per
  opponent per swing) and struck through the normal path; End runs the attack's whole-swing rule. If neither message
  comes within delay + duration + 1.5 s, the server resolves the swing itself, once. Reports for other players (PvP is
  the server's) or for casts with no waiting swing are ignored. Charged attacks, channels and the other shapes stay the
  server's. The client's Block targeter is not sent in our replies. Setting `MeleeTargeters` (Docker `MELEE_TARGETERS`,
  default true = live); dev `meleetargeters on|off`.
- **Blocking seen by others** (`Combat/BlockStance.cs`; OPEN-LEADS L170): a player's stance (Molten Rebuke, Corrupting
  Revenge; the `Block` state, 2 s, stopped by the next attack from the front) and a creature's shield block (3 s, immune)
  are shown to the other players in range (150 m for a player, a creature's viewers), never to the blocker: entity state
  bit 45 (PlayerUpdate 0x51 UpdateCharacterStateDelta, set) plus the client's **Block** effect on the blocker (Abilities
  EffectStart, type 0xC5838838, arc 157° for a player = the server's head-on test, 360° for a shield, height 4 m; ours).
  Their melee Sweeps then stop on the blocker with the "blocked" effect 508 and report no hit, which matches the server
  (it lets no damage through either way). On the end (the block caught a hit, ran out, death) the same clients get the
  Delta with clear = bit 45 and EffectEnd; a refresh re-sends the effect; a player who meets the blocker meanwhile gets
  the current state. Live's arc / height and whether live creatures set the bit are not known. Setting
  `BlockStanceVisible` (Docker `BLOCK_STANCE_VISIBLE`, default true = live); dev `blockvisible on|off`, `blockstances`.
- **Charged attacks** ("Charge a powerful …"): their caster info has the charged kind 0xF7150354 with max / min charge
  (1.2 s / 0.15 s, reply +0xd0 / +0xd4). The client charges while the button is held (ClientAbilityManager +0x2b0 = 1)
  and on release sends FinishChargedCast {u32 ability, u32, vec4 camera, vec4 aim}; the server times the hold (damage
  60-100 %, "fully charged" at ≥ 95 % of 1.2 s), runs the attack with the release aim and only then answers the cast —
  a CastSuccess during the charge ends it (FUN_142d7a370 clears the charging state). CancelCast closes it unanswered.
- **Attack animation timing** (measured in the client with a Frida probe): the caster's own
  CastSuccess plays its animation (the Enter) plus ActiveTimeStart 509; one **cast time** later the client plays
  ActiveTimeEnd 510, which stops the current selection. The Release (strike) is sent at the selection's AttackTime as a
  CastSuccess without Force (+0x48), so it plays without restarting the window; the weapon casts' cast time is sized
  to the latest strike + 0.35 s motion + 0.15 s for the answer's trip (WeaponAttacks.StrikeWindowSeconds). With the
  old 0.4 s cast time and a forced second success the strike was stopped 50-130 ms in ("janky" swings).
- **Ours, not the client's**: durations, chances, damage shares (the ability data was server-side).
- Dev: `wield <item>`, `weaponattack basic|power|special [n]`, `status DirtyFighting`, `states` (also Energy now / maximum),
  `struck [damage] [count]` (the nearest creature hits you through the defences and armour passives), `combatgear`,
  `revive`, `god on|off`. Checked in the client with scripted weapon-kit and charge runs (presses and releases through
  FUN_142d71800).

## Grief (`Game/Grief.cs`; OPEN-LEADS L144)

The client keeps PlanetSide 2's grief system: grief points, damage points and a level (1-3 warnings, 4 equipment lock,
5 suspension). Its warnings say what earns grief — text 56075 "You have been firing on ally forces. Exercise more
caution in the future." up to 56078 "… Weapon lock is now active." and 56079 "Weapon lock is now deactivated." — and
no Landmark source names an amount, so all numbers are ours.

- **Packets** (GriefBase 0xC0, u8 sub): 1 SetGriefPoints {i32 points, i32 damage points} (negative = keep; the client
  prints "You have %d grief points."), 2 SetGriefLevel {i32 old, i32 new} (the client prints entered / left grief level
  1-3, equipment lock 4, suspension 5), 3 Reset (header only, silent). SendSelfToClient carries the three values at
  login (player +0x76920), so the level survives a relog.
- **Lock**: from level 4 the client refuses its Knife / QuickUse slots and non-GRIEF_IMMUNE fire modes (none reach
  Landmark weapons); the server refuses weapon and combat-gear attacks. Level 5 = the client's "account suspended".
- **Earning** (setting `GriefAwards` / `GRIEF_AWARDS`, default true = live): in team site games a hit aimed at a teammate
  (its target, or first on the aim ray; never the swing's area) lands and earns the attacker one grief point per
  `GRIEF_DAMAGE_PER_POINT` damage (the remainder is the damage points). Levels start at `GRIEF_LEVEL_POINTS`
  (10,20,30,40,60); each new level 1-4 shows its warning; points wear off at `GRIEF_DECAY_PER_HOUR` in real time and the
  level drops with them; level 5 ends the session after a 10 s notice and turns the character away for
  `GRIEF_SUSPENSION_MINUTES`. Rule sets that score team kills (IgRuleTypes 3: Traitors!, All Scoring) earn no grief.
  Off, teammates can't hurt each other and nothing earns grief.
- **Storage**: character_unlock kinds 27-31 (points, damage points, level, last decay minute, suspension end); award
  changes go to the client and the database at most every 15 s, level changes at once.
- **Admin**: the client's admin grief commands (GriefAdmin 0x1E / 0x4DA: set damage points, set level, reset) act on
  the admin; dev `grief level <n> | points <n> | damage <n> | award <damage> | show | reset`.

## Landscapes (`Game/Landscapes.cs`, `Game/Travel.cs`)

- The Landscapes window lists the islands (391-416) and Pioneer lands (464-468) from the client's StaticZoneData,
  with their real names and site counts. Travelling re-zones the player (`Teleport.Rezone`).
- Each landscape has its own state, keyed by zone id:
  - terrain edits (`TerrainEdits/<zone>/`; the home continent 406 keeps the top folder);
  - trees, plants and creatures (seeded per landscape, streamed only when its heights are baked);
  - build sites and placed props (database `zoneId` columns);
  - ore veins and biomes from the client's per-continent files.
- Players only see players, props, sites, terrain broadcasts and `say` chat on their own landscape.
- The captured terrain store (cells recorded from the live servers) is on the home continent only.
- **Pioneer lands** (`Landscapes.PioneerZones`; OPEN-LEADS L99, since 2026-10-17): zones 464-468 on continents
  440-444, with the client's own names 573314-573318 (Pioneer Jungle, Tundra, Desert, Old Growth, Deciduous). Their
  shape bitmaps are the same flat plateau (P4031: flat terrain), their underground holds ~78,000 isolated cave
  pieces each and no creatures (P4300: the caverns only take auto-placed build sites), and only `Continent_HeightLayerIds_440.bmp` exists for the set, so all five use it. Their ground heights come
  from the client's surface cache (format and checks: `tools/terrain/csc.py`). StaticZoneData `IS_NEWBIE_ZONE` is 1 for exactly these
  (and the abandoned 463); the client parses it and never reads it, so "new players start here" is a server rule. Before
  2026-10-17 the server used zones 417-421 (hilly islands with internal names and no Pioneer caverns); those are no
  longer loaded or listed. Sites still on them move at every start (`Pioneer.MoveOldSites`, setting
  `MoveOldPioneerSites` / Docker `MOVE_OLD_PIONEER_SITES`, default true, user decision 2026-10-17) to the land of the
  same biome (417→464, 418→466, 419→465, 420→467, 421→468; `Landscapes.OldPioneerToNew`): a Design of the owner's
  build there is saved (as for a lapsed site), then the site takes a free spot on the new land in the new-player order
  (that land's surface first, then any Pioneer surface, a Pioneer cavern, a classic landscape) keeping its id, name,
  tags, access and settings; the old voxels and props stay behind unseen. One log line per site; nothing left = no-op.
  Without the Environment Server at start-up the move runs on the first world tick that reaches it. A character saved
  on 417-421 starts at their own (moved) site at login, else at the replacement land's Spire (`Pioneer.LeaveOldLand`).
- **The slate slab under a new Pioneer site** (`Game/Pioneer.Slab.cs`; OPEN-LEADS L180): live's 30 Jun 2016 notes fixed
  the slate slab placed on new players' build sites. The client ships the slabs as `CommonData/Voxel/templates/
  claim1x1 / 2x1 / 3x1 / 3x3.otd` (no client code loads them): one 2 m slab each, claim3x3 a 525 × 5 × 524-voxel plus over
  the 210 m site (its 70 m corner tiles empty), claim1x1 one 70 m tile. A site granted on a Pioneer surface (the first
  step above; not moved, cavern or classic-landscape sites) is stamped once in the background: the template's solid
  voxels only (its scattered air marks would pit the ground), none within 40 m of the Starting Location's -x / +z
  corner, in `PioneerSlabMaterial` (default 26 Worked Slate; the templates store ARRAY_INDEX 53, "Raw Amaranthine" in
  both client tables, a stale index), centred on the site with its top layer on the ground voxel at the site's middle
  (flush with the flat ground [I]), into layer 0 owned by nobody (not in the owner's Design or refunds). It stays while
  the site stands (the heal pass leaves layer 0 inside sites alone) and goes with it (delete / lapse revert the box).
  `TerrainEdits/pioneer_slabs.txt` records pending and stamped sites (a restart in between stamps it at the next start;
  a stamped site never again). Settings `PioneerSlab` (`claim3x3` default | `claim1x1` | `off`) / Docker
  `PIONEER_SLAB`, `PIONEER_SLAB_MATERIAL`. Live's exact template and height are not known [I]. Dev `pioneerslab` queues
  the slab under the caller's own site on the Pioneer land they stand on (test aid; once per site).

### Teleports (`Game/Teleport.cs`, `Game/TeleportRequests.cs`, `Game/Props.Movers.cs`)

- **Re-zone or move in place.** Every teleport re-zones (loading screen) unless it stays on the landscape and is no
  longer than `ShortTeleportDistance` (Docker `SHORT_TELEPORT_DISTANCE`, default 60 m; 0 = always re-zone). Plain
  TeleportToLocation moves used to hang the client's renderer now and then; the cause (a zero facing vector, fixed
  2026-10-18) and the history are in docs/terrain.md "Long teleports". A short move counts as a pending arrival for 1 s, so hit points,
  game HUD and site info wait for it as they wait for a re-zone. An arrival from another world always re-zones.
- **Meeting after a re-zone.** A re-zone (long teleport, landscape / Spire / site travel, cavern trip, travel to a
  friend, an arrival from another world, a re-attach) shows the same loading screen as a login (GameStateLoading,
  ClientLoading 0x6C `{1}` when it comes up, `{0}` when it drops in run state 0x12, 15-25 s after ClientIsReady). The
  re-zoned player and the landscape's players meet when that `{0}` arrives (`PlayerManager.RezoneStarted` /
  `ZoneInDone` / `LoadingScreenLeft`): the re-zoned client is sent the others then, and players on a new landscape
  are sent the player then; players who kept seeing it through a re-zone on the same landscape are not sent it again
  (they get its arrival position at the zone-in). No movement goes to or from it meanwhile. A `{0}` before the
  re-zone's zone-done flags belongs to the screen before and is ignored. Fallbacks: 90 s after the zone-in, 3 min if
  the zone-in never comes. Short moves (TeleportToLocation) are not held.
- **Evac to Safety and the admin teleports** (OPEN-LEADS L140): the Escape menu's Evac to Safety, `/location`,
  teleport-to-camera and the admin map teleport send Command 0x1E teleport requests and freeze the player until a
  TeleportToLocation answers (protocol.md §14). Admins go to the exact point. Players get an evac near their own spot
  (8 m) or just off the build site they stand on (the client's own rule: half the box diagonal + 10 m along their
  heading), once per 10 s; anything else is answered with their current position. Setting `PlayerTeleportRequests`
  (Docker `PLAYER_TELEPORT_REQUESTS`, default true = live; off, players' requests are answered in place).
- **Translocation Pads** (mover packs, `Props.ManagedTeleport`): the client sends a managed teleport request when the
  player steps on a pad; the server moves the player to the pack's other pad, always in place (no re-zone), 0.25 m
  above it, and tells the client to ignore the arrival pad for 2.5 s (TeleportToLocation +0x48 / +0x50 with the pad's
  prop guid, protocol.md §14), so the player isn't sent straight back. Before 2026-10-18 the area guid was sent there,
  which the client never matched.
- **Flingers, launchers and thrust pads** (`Game/SafeEject.cs`): the client throws the player itself (Spring-loaded
  Flinger area 24 ChainThrustPad, Launcher area 32 Thrower, AntiGravThrust area 12, and the server-sent thrust pads 0xCF)
  and at that moment sets its own entity state bit 26 ("launched") and sends PlayerUpdate SafeEject 0x26/0x4C `{u8 1}`
  once (protocol.md 4.1). It is not an unstuck request (that is Evac to Safety above). The client never clears the bit
  itself; while it is set no second SafeEject goes out and type-1 thrust pads don't fire. The server ends the launch
  when the movement state word shows the player back on the ground after being in the air (or 3 s on the ground) with
  UpdateCharacterStateDelta `clear = 1 << 26` to that player [I: what live sent is not captured; only a server state
  record can clear it]. Not while a re-zone loads. Setting `SafeEjectReset` (Docker `SAFE_EJECT_RESET`, default true);
  dev `safeeject [clear|on|off]`.
- **Arriving over props.** A re-zone deletes every entity on the client, props included, and their collision bodies
  come back 2.5-2.9 s after the zone load. A re-zone that puts the player 1.5 m or more above the ground with a placed
  prop under them (within 4 m sideways, 12 m down: a Magical Platform, a walkway) sends the props within 48 m of it
  at ClientIsReady (the rest of the stream range follows the flags) and holds the zone-done flags 4 s, so the player can't move before the bodies exist; the Spire does the
  same with 6 s (docs/terrain.md "Voxel templates", the Spire's arrival).

### Rare and seasonal drops (`Game/RareDrops.cs`)

Rates are ours; the patch notes only say "rare" or "a chance".

- Lop Ear Bunny figurine from Outpost elites (2 %; 28 Jul 2016); Repair Bot Beacon as global loot from creatures and
  rich gathering (0.5 %; 26 Apr 2016).
- **Hat unlockers** (Ninja Mask 4522, Samurai Helmet 4523) from Outposts and the higher Reward Chests, 5 % (29 Sep 2016:
  a chance at two new hats). Docker `HAT_DROPS`: `season` (default = live: only while the Halloween season
  is on; Halloween items stopped dropping on 6 Dec 2016), `always`, `off`.
- **Seasonal outfits**, 5 %: a Skeletal Bodysuit from Outposts in the Halloween season; a festive Colonist's Flight Suit
  or one of the three older Holiday Sweaters from Outposts, and a festive Flight Suit from the higher Reward Chests, in
  the Winter season. Docker `LANDMARK_SEASON`: `auto` (default = live's 2016 calendar every year: Halloween 28 Sep - 5
  Dec, Winter 6 Dec - 10 Jan; the end day is ours), `halloween`, `winter`, `off`. The 2016 Green / Blue Holiday Sweaters
  were giveaways, never drops; they use the green and blue tints (GreenGreen00, Colony_Light_09).

### Chaos Caverns on every landscape (`Game/Caves.cs`, `Game/Outposts.cs`; TASKS 3.14 F2)

- Live (patch notes): Underground Adventure could send players to the Chaos Caverns of any zone, as the Lost Crystals
  already did (30 Jun 2016, P5927); Lost Crystals could send them to other worlds and biomes (7 Jun 2016, P5734); from a Pioneer landscape a trip goes to a non-Pioneer one (P5928 / P6092); Pioneer lands have no
  Chaos Caverns (P4299). The 0.695 data agrees: an Underground `.cpa` for 391, 393-408, 410-416 (80-82 Chaos Caverns
  each, 499-860 cavern pieces); the Pioneer lands' files hold only isolated cave pieces, and the event islands have
  none.
- The client asks only "a random cave" (TeleportBase 0xFC sub 0x0B) or "the next one" (sub 0x0D after the "Eureka!"
  offer 0x0C); both are "same" in 0.695 (research/trace/0695/coverage-695.md) and carry no destination, so the landscape is the server's
  choice. A trip to another landscape re-zones there (ClientBeginZoning with that zone).
- Cavern indices are shared by all landscapes: the home continent's 0-519 first (creature guids unchanged), then the
  others by zone id. Outpost indices are per landscape: home 0-80, island `z` from 128 × (z − 390) (prop guids keep
  16 bits; lockouts of those outposts are character_unlock kind 25 rows, {12-bit index, 20-bit end hour}, so kind 21
  rows of the home continent are untouched).
- **Lazy placement:** the home continent's outposts are stamped at start-up as before. Another landscape's are stamped
  by a background thread (below normal priority) the first time a player is on it or a trip picks it — the 35 ruins
  once each in live's slots, then a copy in every empty cavern, one decoded ruin at a time, one landscape at a time
  (a few minutes, up to ~1 GB of heap while running, ~110 MB of terrain edits). Each outpost goes live as soon as it is
  stamped; its cells go to the players there. Marker `TerrainEdits/outposts695d_<zone>.txt` (cavern, ruin, corner,
  number, standing spot; `#complete` at the end): a restart marks the cells permanent again from the templates' cell
  positions, without decoding a ruin.
- **Trips:** Underground Adventure picks a landscape at random, then one of its cavern graveyards (see Travel above);
  if its outposts or graveyards aren't ready yet it is queued and the trip goes to a ready landscape instead (ours).
  The Lost Crystal's next cavern is a placed outpost (not locked out for the player), preferably on another landscape with another cave biome, else another landscape, else another biome here
  (other worlds and biomes, P5734, after players kept landing in the same cavern; the order is ours), and each use queues one random unplaced landscape (ours); both trips may first go
  to another world (below). Group follow (P5188)
  goes to the same landscape and spot.
- In the background each landscape also gets its cavern floors, its treasure chest sites (`Game/LootChests.cs`, surface
  caves and caverns) and line-of-sight grids (`Game/Sight.cs`). Creatures stream a landscape's cavern groups even
  where its surface heights aren't baked.
- **Settings:** `CavernLandscapes` (Docker `CAVERN_LANDSCAPES`): `all` (default), `home` (the home continent only, as
  before), or zone ids; `OutpostPreload` (`OUTPOST_PRELOAD`, off): stamp the unvisited landscapes too, one after
  another.
- **Local Travel on every cave landscape** (`Game/WarpNetwork.cs`, OPEN-LEADS L130) [I]: each landscape with caverns
  gets its own warp network (WarpNetworkZoneNetwork 0x113 sub 2, filed by the client per (world, landscape)): its
  Spire's hub plus the 81 underground portal crystals of its own Underground `.cpa` (Graveyard01 pieces, tile 111, as on
  406), read in the background on the first visit and pushed to the players there. The home continent's crystal ids
  are unchanged; another landscape's are `0x5750… | zone << 16 | 1000 + n`. The Spire's crystal opens its own
  landscape's hub. The 0.695 test client draws another landscape's network (391: hub on layer 0, its crystals on
  layer 1). Where a crystal stands: its Graveyard01 piece is also the client's type-3 "cave beginning location" (`.cpa`
  tile byte 32 set, byte 38 = 3) and the site of a cavern graveyard whose stamped `graveyard_launch` box covers the
  piece's column, so the crystal (and the warp's arrival) stands on that stamp's ground when the graveyard is laid out
  from the template (`CavernGraveyardStamps`), else on the cave floor under the piece, as before. Cost: what the
  client's Confirm Travel window shows (hub → crystal 1 Lumen, crystal → crystal 2). Whether live listed crystals off the
  home landscape at all, and whether it hid undiscovered ones (the client keeps no discovery state), is still open.
- **Trips to other worlds** (`CrossWorld.CavernTrip`, OPEN-LEADS L131) [I strong]: the 7 Jun 2016 notes let every Lost Crystal
  use go to other worlds and biomes (P5734). "World" is the server in the game's words: the wiki's "Servers and
  Landscapes" page (servers, called worlds in the game, listed beside the landscapes; every server with the same
  landscapes), the client's world name texts (560911 Courage … 560926 Inspiration), a landscape named "on Serenity" in
  forum 57596, Courage called a world in P5905. With a multi-world gateway and
  `CavernTripsOtherWorlds` on (Docker `CAVERN_TRIPS_OTHER_WORLDS`, default true = live), a crystal's trip and the
  Underground Adventure button (P5927: any zone, as the Lost Crystals) first roll the
  world: this one and each other world on the gateway equally likely (every world ran the same landscapes) [I]. Another
  world: the player is handed over (HandOver / AttachConnection with the optional trailing `CavernTrip` byte: 1 Lost
  Crystal, 2 Underground Adventure) to one of its Chaos Cavern landscapes, and that world picks the cavern — an outpost
  whose crystal they can still loot on that landscape, else elsewhere there (crystal); its own landscape-then-graveyard
  roll (button). The arrival is always a real re-zone (`WorldSession.FullRezoneNext`), since only ClientBeginZoning
  carries the new world id. Group follow works across worlds: members using the same crystal go to the same world and
  landscape and, there, the same outpost (P5203); members left behind by a button trip get the usual prompt and arrive
  beside the member they follow (P5188). Nobody is brought back (no source says so): the next trip, the Landscapes
  window or the next login moves them on. Crystal lockouts are the character's and carry their outpost index only, so
  they count on every world [I]. Dev: `cavetrip <crystal|adventure> [client world id] [zone]`.

### Outposts change caverns (`Game/Outposts.Rotation.cs`; audit F8, off by default)

- Sources: the client's help page "Outposts exist in the open world and change regularly" (ChaosCavern_2Outpost_FTE;
  it can also mean the shared garrison state, which is built), the 26 Apr 2016 notes (every Chaos Cavern always has an
  Outpost), the Environment Server page's per-ruin-layer Desired / Active /
  Failed / Last ID counters and the client's Spawn / DespawnExportedTemplateRequest (0xF4 / 0x18, 0x19). No source gives
  a period or a share, so both are settings and the default is never.
- `OutpostRotationHours` (Docker `OUTPOST_ROTATION_HOURS`, 0 = never) and `OutpostRotationShare`
  (`OUTPOST_ROTATION_SHARE`, 0.25): every so many hours per landscape (the clock starts once its outposts are all
  placed; `TerrainEdits/outposts-rotation.txt`), that share of its outposts gets another ruin **in its own cavern**:
  one of the cavern's free ruin slots (live's slots, as at placement), the ruin with the fewest copies on that landscape,
  never overlapping another outpost or an arrival platform. Never replaced: an outpost a player is within 400 m of, one
  streamed to anyone, one with a crystal channel or a woken crystal, and the last copy of a ruin on its landscape (so
  all 35 ruins stay on every landscape).
- Each replacement runs on the background worker: the world tick takes the outpost out of play (props, banner site,
  garrison, navigation tiles), the old ruin's box goes back to the natural ground (`RevertBox`), the new ruin is stamped
  (cells permanent), the marker line is rewritten in place, and the outpost comes back on the next tick with the same
  index — prop guids, banner id and crystal lockout ("once in each Chaos Cavern") stay with the cavern. Changed cells go
  once to the players on that landscape. A journal (`TerrainEdits/outposts-rotation-pending.txt`) lets the next start
  finish a replacement a shutdown interrupted.
- Dev `outpostrotate [zone|here] [count]`: one rotation now, whatever the setting says.

### Outpost walk-on banner (`Game/OutpostSites.cs`; features audit #60, OPEN-LEADS L102)

- Wiki "Outposts": nearing an Outpost shows its creator's name and the date it was made, again after backing out and
  coming back — the client's build-site walk-on window (ClaimWalkOnTextWindow; 0.695
  FUN_142dc7540: ClaimName = claim entry +0x10, WelcomeMessage = +0x28; no date field, so the date is in the message).
- Each outpost streamed to a player (160 m) is also sent as a site record (ClaimsBase AOIUpdate sub 4, added / removed
  in one packet per tick; sent again after a full claims update): id `0x5453… | outpost index`, owner the same
  non-character guid, box = the ruin's slot from 20 m under its base to its top, Default Access Play Around (0x003),
  no admin template. Name = the outpost's title, message "Created by <creator> on <M-D-YYYY>" [I wording] (wiki
  "Outposts A-Z (List)" for 30 ruins, the client's design texts for the spellings and four more titles).
- Not the contest banner: with an admin template guid the client refuses every claim permission to non-staff players
  inside (FUN_142d98cc0; SendSelf +0x287 is the staff bypass), which could switch off the Lost Crystal and Chest.
- The records exist on the client only; the server's site list never holds them, so digging, creature spawning,
  goals, searches and the placement buffer are unchanged.

## World

- `Game/WorldClock.cs`: one shared time of day for all players ("Island Time"), 8× real time (wiki: a day lasts 3 real
  hours; setting `DayCycleSpeed` / `DAY_CYCLE_SPEED`). It follows real UTC time × the speed, so it carries on across
  restarts and is the same in every server process (until 2026-10-18 it restarted at 09:00, so a dev world restarted
  often never reached night). The client asks for the time every ~11 s (GameTimeSync 0x37) and runs its own copy at the
  sent speed in between; that copy drives the sky and the client's `GameData.Time` data source (SceneTime = the time
  where the player is, IslandTime = the world clock, EarthTime = the PC's clock), which the build-mode time panel
  (cog icon > Advanced Build Settings) shows. The HUD clock (Options > Show Clock, ClockWidget: a sun / moon dial)
  reads the PC's own clock (`new Date()`) and has no server input.
- Build site time (Advanced Build Settings, `Claims.SetTime`): owner and visitor settings, hour -1 = the world clock
  ("Game Time" / "Custom Speed"), 0-23 = stopped at that hour, -2 = the visitor's PC clock (client-supported, no
  menu entry); speed 0.25-1000 as the client clamps it (the slider runs 1-1000).
- `Game/Spire.cs`: the teleporter hub of every landscape (the client's Spire template, model 596 and its props): nearest
  level ground to the zone centre (home included; deck ≤ 17 m relief ≈ plinth height); deck seated into the landscape
  (no air-box stamp / fill; docs/terrain.md "Voxel templates"). Usable props: Replicator, Repair Station, Mailbox, Crimson Parlor, Portal Crystal,
  Resupply Chest 884 (missing starter tools back) and the Founder's Chest 898 the server adds beside it (missing
  Founder's Pack items back).
- `Game/Teleport.cs`: moves a player, snapped to the ground.
- `Game/Templates.cs`: designs (save, list, place, rename, delete); design items add the client's .otd templates
  as designer templates (Designs tab "Collected"). Starter Castle: the 27 castle pieces with a client .otd file are
  served by guid ("ST" + index), never stored. Every character has 6 defaults (Courtyard, Hallway - Long, Tower -
  Inline, Wall - Corner, Wall - Medium, Wall Entry) and, for now, the 8 Modular Castle pieces; the other 13 come from
  their design items (goal 471 reward bundle 1051), which unlock the piece (unlock kind 20) instead of storing a copy.
  Characters made before 2026-10-16 were given all 27 once, at their next zone-in. Designer designs carry their
  exported template id, so the 0.695 client files the castle pieces under "Starter Castle".
- `Game/WebPages.cs`: a small web server for the client's embedded browser (Showcase / Gallery) on `WebPagesPort`
  (19001); LaunchPad writes its address as `MarketplaceUrl` in `ClientConfig.ini` on PLAY. Without it the browser
  retries a dead URL and stalls the client.

## Developer commands (`Game/DevCommands.cs`)

Write lines to the world's `dev_commands.txt` (next to the server executable; in Docker
`server-data/worlds/<WORLD_KEY>/dev_commands.txt`); it is read every second. Useful ones:

| Command | Effect |
|---|---|
| `send <hex>` | Send raw zone bytes to everyone. |
| `give <item> <n>` | Give items. |
| `finditem <text\|#id>` | Search item names (or by id); at most 40 matches logged, empty refused. |
| `timeofday <h>` | Set the world clock. |
| `clock` | Log the world clock and tell the players in chat (compare with the client's `GameData.Time`). |
| `carve`, `dig`, `digmode` | Shape terrain / change how digging shapes holes. |
| `tp`, `tpclaim` | Teleport to a position / a build site. |
| `kobold`, `creatures`, `hurt` | Spawn creatures, list them, damage the player. |
| `tree`, `treefall` | Inspect / fell the nearest tree. |
| `prop`, `station`, `claims`, `unclaim` | Props, crafting stations and build sites. |
| `landscapes`, `landscape <zone>`, `pioneer` | List landscapes, travel to one, run the new-player Pioneer start. |
| `eventexpire`, `eventexpire confirm <zone>` | Dry run of the event island expiry (setting, island open now / at the last run, every site on 460-462 and what happens to it, in the log); `confirm` expires a closed island's sites now. |
| `cave [zone]`, `cavelands [zone]`, `outpost <n>` | Underground Adventure (on one landscape); every cave landscape's outpost state (with a zone: place its outposts now); go to outpost n (re-zoning to its landscape). |
| `creatureweapon <guid hex\|near> <Wield_*.adr\|off> [tint]` | Put a weapon actor in a creature's main hand (or empty it), for players that have it as a full entity. |
| `sight`, `pathing [on\|off]` | Line of sight of the creatures near the player; creature navigation at the player's spot and each nearby creature's walk state (on / off until the restart). |
| `testcell`, `livecell`, `blockcell`, `blockdmg` | Terrain packet experiments. |
| `grief level <n>\|points <n>\|damage <n>\|award <damage>\|show\|reset` | Grief state (`Game/Grief.cs`): set it, count ally damage as friendly fire would (also with GRIEF_AWARDS off), make the client print the points it holds, clear it. |
| `graveyard [go] [cavern\|surface]` | The landscape's graveyards, the player's layer, whether they stand in a buffer zone, the nearest graveyard; `go` moves there. |
| `safeeject [clear\|on\|off]` | Launches reported by SafeEject (flinger / launcher / thrust pad) and not ended yet, in the log; `clear` ends the targeted players' launches now (clears their entity state bit 26); `on` / `off` switches SafeEjectReset until the restart. |
| `blockvisible on\|off`, `blockstances` | Show blocking stances to the other clients (setting BlockStanceVisible) until the restart; list the stances up now and how many clients were told (log). Put a player in the stance with `status Block [seconds]`. |
| `movementeffects on\|off`, `charstats on\|off`, `meleetargeters on\|off` | Switch the sprint / boot movement effects, the live character stats and the melee Sweep targeters until the restart (settings MovementEffects, LiveCharacterStats, MeleeTargeters). Movement effects apply to new caster info only (relog); the other two are re-sent to the targeted players at once. |
| `movement` | The player's open movement channel (sprint, Immediate Delivery, Endless Breeze), charging leap, movement-ability cooldowns and Energy, in the log. |
| `activate <item id>` | Use one held item of that kind as the bag's Use does (recipe items, storage expansions, unlockers, prop placement): harness tests of item uses. |
| `sysmail [subject]` | A system mail to the player (tests the new-mail notification). |
| `giveoverflow <item> <n> [minutes]` | Put items straight into overflow; with minutes they expire that soon. |
| `psinstances [clear]` | The placed Player Studio designs recorded on the player's current site (visitors can't copy over them); `clear` drops them. |
| `setwear <item> <points>`, `durabilityof <item>` | Wear the player's items of that kind (saved, shown) / log their wear and condition: tests that wear survives trade, mail and chests. |
| `autoresupply <item> [on\|off]` | Set the auto-resupply switch of the player's first stack of that kind (as Inventory 0x2D sub 0x0D, without the client's definition check that no live item passes) and log every stack's switch; the client sees it in the item record at the next login. |
| `propstream [<metres>\|off]` | Placed-prop stream range until the restart (`off` / 0 = whole landscape; setting `PropStreamRange`), and each targeted player's stream (squares held, props streamed) in the log. |
| `spirefull on\|off` | Push each Spire prop's full data with the prop, unasked (off by default: the client asks, as for every prop; it does nothing for collision). |
| `gamelabels on\|off` | Site games' control-point labels and capture bars (PropState / PropInteraction) on or off, for isolating client problems. |

The full list is the `switch` in `DevCommands.cs`.

## Not built yet

- Rendered character portraits (live had a render service; the server keeps and serves the client's own uploads —
  no 0.589 window shows portraits).
- The exact weapon model each creature held on live (the client has no table for it; `CreatureWeapons` is a best
  guess: the type from the creature's icon, the model ours), a sword-and-shield creature's shield (off hand), and
  control points as spawners.
- Features that are built but not yet checked in game are marked "untested in game" in the feature list
  (`research/trace/0695/FEATURES.md`).

## Graveyards (`Game/Graveyards.cs`, `Game/Graveyards.Landscape.cs`; FEATURES #47, 2026-10-17)

Full write-up with sources: research/trace/0695/agent-graveyards.md.

- **The client's graveyard** is a static area: GlobalAreas 23, a 30 m sphere (GlobalAreaShapes row 20) with the
  properties Graveyard (type 33), TerrainPermission kind 4, ProcedurallyPlacedEntities "Prevent Spawning" and the
  server-only types 36 / 37. The server names it on the Graveyard Centerpiece's spawn (prop 1518, AddLightweightProp
  +0x260). The client turns the kind-4 sphere into a claim placement exclusion zone (FUN_1436de260): a build site whose
  box comes within 30 m plus its half-diagonal of the centrepiece is refused with "Claims.InvalidClaimLocation"
  (FUN_142dc6870 code 3). The server refuses the same (Claims.CanPlace).
- **Chaos Caverns:** one graveyard per graveyard placement of every island's Underground .cpa (81 each): the
  `ManuallyPlacedExportedTemplateData` records with template id 76 and a box of graveyard_launch.otd's size
  (83 × 22 × 83 voxels), each at a fixed offset from its Graveyard01 cave piece (tile 111). The retail client parses
  these records and never uses them (L47), so the server builds them (setting `CavernGraveyardStamps`, Docker
  `CAVERN_GRAVEYARD_STAMPS`, default on, as live had them):
  - **What is stamped:** graveyard_launch.otd — a 26 m plinth 4.4 m high in a 33.2 m box of explicit air, its 9 cells
    written into layer 0 like the outpost ruins (corner = box minimum − 1 voxel; air on the cave's own air left out),
    turned by the record's `a` (quarter turns, see docs/terrain.md "Turned stamps"). Its 25 props stand where the
    turned template has them: the Graveyard Centerpiece 1518 (the template's own, carrying area 23, never a second
    one), gravestones, glowing mushrooms and four Stone Gargoyles. The arrival point is 0.6 m over the plinth, 6 m from
    the centrepiece (Underground Adventure above: which graveyards trips use, the fall guard). The ring of air around the plinth lies below the Graveyard01 piece's floor, so the stamp leaves a
    trench 1-3 m deep around it (measured on 391 / 406; that live wrote the template's air too is inferred, as for the
    ruins).
  - **When:** in the background on a landscape's first visit, one landscape at a time (81 small stamps, a few MB of
    terrain files), never on the world thread. Each stamp is a line in `TerrainEdits/graveyards_<zone>.txt`
    (placement, template, corner voxel, turns; `#complete` at the end); at start-up those cells are marked permanent
    again (no heal) and nothing is stamped twice. The changed cells go to the landscape's players from the world tick.
  - **Off:** no new stamps; a graveyard not stamped yet is only its centrepiece on the cave floor under the box's middle
    (Caves.Floor), as before. One stamped earlier keeps its template layout (it is in the terrain).
  - Streamed by 3D distance: the centrepiece and the other props within 160 m, removed beyond 220 m. Dev `graveyard`
    reports how many of the landscape's graveyards are stamped.
- **Surface** (setting `SurfaceGraveyards`, Docker `SURFACE_GRAVEYARDS`, default on): no client file places them, so
  ours are inferred: one per 1 km square of dry, level land, none within 500 m of the landscape's middle, built from the
  props-only template graveyard01.otd (no terrain writes). The centrepieces of the player's landscape are sent once on
  arrival (every exclusion sphere known); the other props stream within 160 m. A graveyard an existing build site
  already overlaps stays hidden while the site is there.
- **Respawn by layer:** Flee goes to the nearest graveyard of the layer the player died on (P2296, P2718). More than
  100 m below the ground is the cavern layer (cavern graveyards only, no snap to the terrain on arrival); above it the
  surface sites (surface graveyards, placed graveyards and respawn points, the Spire, the player's own site). Deaths on a
  build site keep the site's own Starting Location / Respawn Points.
- **Buffer zone:** creatures don't pick, or keep chasing, a player inside a graveyard's 30 m sphere (wiki Graveyard), and
  no tree, plant or creature spawns inside it (Prevent Spawning). The radius is the area's; that the buffer is this area
  is inferred.

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
