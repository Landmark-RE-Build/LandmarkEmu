# Landmark game rules (server-side reference)

Rules the client doesn't contain, collected from official developer posts. Numbers are from the dates shown and
changed over development (alpha 2014 → launch June 2016, our client is 0.589 ≈ launch era), so treat them as
starting values to tune, not gospel.

## Units
- 1 game unit = 2.5 voxels.
- Base run speed: 10 units/s in alpha (Mar 2014); raised to 13 (Oct 2014) when sprint was folded into normal running;
  8 inside PvP claims. Each point of run speed stat adds 1 unit/s.
  - Our server sends calculated stat 5 (MovementSpeed) = 12; compare against 13 if movement feels off.

## Harvesting (Mar 2014)
- **Reach:** harvest distance = 6 + Reach / 10 units.
- **Harvest speed:** cast time (s) = 50 / HarvestingSpeed (must never be 0). Devs planned diminishing returns.
- **Harvest damage:** subtracted from the resource's hit points per swing; resource "dies" when HP reaches 0.
- **Harvest size:** volume of the targeting sphere in voxels (≈1 voxel per point); about half is usually above ground.
- **Resource rating:** the tier of resource a tool can harvest.
- **Harvest bonus:** chance of an extra resource = HarvestBonus / 100 (cap 100).
- **Discovery:** chance per reward event to roll the rare table (elemental resources, heartwood…) =
  Discovery / (150 × ResourceRating). Rolled per swing on trees / per voxel destroyed.
- **Treasure find:** chance to roll a chest's special table = TreasureFind / (100 × item level of the chest),
  capped around 3%.
- **Light radius:** radius (units) = LightRadius / 10.
- **Jump bonus:** physics-driven, no simple formula published.

### Resource hit points (Mar 2014)
| Metals | HP | Stone-likes | HP | Gems | HP |
|---|---|---|---|---|---|
| Copper | 60 | Ice | 20 | Agate | 75 |
| Tin | 60 | Dirt | 20 | Tourmaline | 75 |
| Iron | 90 | Sand | 20 | Aquamarine | 113 |
| Silver | 99 | Stone | 75 | Topaz | 124 |
| Tungsten | 135 | Marble | 169 | Amethyst | 169 |
| Gold | 149 | Amaranthine | 253 | Emerald | 186 |
| Cobalt | 202 | Obsidian | 278 | Sapphire | 253 |
| Rubicite | 222 | Alabaster | 380 | Ruby | 278 |
| Mithril | 304 | | | Diamond | 380 |
| Etherium | 334 | | | Moonstone | 418 |

Planned: basic materials (sand, dirt, stone, ice) get more HP in higher-tier zones.

## Crafting quality (Mar 2014, later reworked)
- Each quality tier has a weight; chance = weight / sum of weights.
- Quality bonus multiplies the weights of the higher tiers (exceptional, legendary) by (1 + bonus/100).

## Heroic movement (Oct 2014)
Movement items became boots with abilities (energy cost / cooldown / cast time):
| Boots | Ability | Cost | Cooldown | Cast |
|---|---|---|---|---|
| Feather-Tipped Boots / Nimbus Boots | double jump, +10% damage 5 s | 50 | 5 s | instant |
| Boots of the Zephyr / Anti-Grav Boots | glide while holding space, +5 armor/s | 0/s | 0 | instant |
| Neon Blitzing Boots | jump forward, +25% move 3 s | 75 | 10 s | 0.5 s |
| Messenger's Sandals | +37.5% move while channelled | 40/s | 10 s | instant |
| Soles of the Outrider | +25% move, +1 armor/s while channelled | 60/s | 10 s | 0 |
| Boots of Bounding | charged leap (longer charge = further) | 100 | 5 s | 0–1.25 s, auto at 4 s |
| Leaper's Launchers | charged leap + takeoff damage | 80 | 10 s | 0–1.5 s, auto at 4 s |
| Lunar Treads | glide upwards while channelled | 50/s | 5 s | 0 |

## Combat (Oct 2014, placeholder values)
- Soldier's Blade: Slash 0.3×Offense+65 (+10 armor per target), Battle Rush 0.4×Offense+115 with knockback,
  Warrior's Roar restores 300 armor (+50 per snared foe).
- Mercenary's Staff: Frost Orb 0.45×Offense+45 then 0.15×Offense+15 per 0.5 s for 2 s; Shatter 0.3×Offense+30
  (+0.3×Offense+30 knockdown, destroys terrain); Frozen Domain 0.15×Offense+15 per 0.5 s, then 0.9×Offense+90.
- Slayer's Bow: Quick Shot 1×Offense+40 (stacks Focused Strike, 3 = Blazing Shot); Charged Shot 2×Offense+80
  (Blazing 2.75×Offense+110); Immolating Barrage 2×Offense+80 (Blazing: 0.3×Offense+12 per 0.5 s for 5 s).
- Armor sets (chest / head / hands): Soldier 500 armor+200 HP / 300 / 200+200 HP; Mercenary 300+30 energy+15
  offense / 150+30 energy / 150+15 offense; Slayer 100+50 offense / 100+20 offense / 30 offense.

## Default keys (Oct 2014)
Grapple Q, heroic move Left-Shift, heroic jump Space, zoom mouse wheel, build cursor size Ctrl+wheel,
building alternate mode F, prop tweak F, duplicate prop X, reset prop Ctrl+R, pick up prop Delete.

## Landscapes, claims and caverns (Mar 2016, shortly before launch)
- "Landscape" replaced "island". Islands: two primary biomes each (plus water, caves, bits of snow); the list matches
  the client's StaticZoneData names and our biome-bitmap survey (e.g. Lost Caldera = Desert & Volcanic, Sleeping
  Ridge = Old Growth & Volcanic). Islands of the same name were identical on every server.
- **Pioneer Landscapes:** extremely flat, no surface caves. Reserved only for automatically placed new-player claims.
  **New players start with a claim already placed on a Pioneer Landscape and enter the game standing on it**; they
  can pick it up and relocate to any other landscape. (Client text 573267: "You can't place a Build Site here.
  Please travel to a non-Pioneer Landscape to place your Build Site.")
- **Claims:** one standard size, no attached claims; up to 8 per player were planned. The post gives 525 x 525 x 450
  voxels, but the 0.589 client's own placement preview is 70 x 100 x 70 m (FUN_1435aef40), which the server follows.
- **Underground:** only "surface" and "underground"; some resources exist only on one of them.
- **Chaos Caverns:** an underground adventure from anywhere teleports you to the start of a random cavern with
  monsters, resources and treasure, and a lost ruin. Finding the **Lost Crystal** in the ruin gives a reward
  (achievement 622 "Speak Friend and Enter": "Lost Crystal reached").

## Sources
- Islands list (May 2015): https://web.archive.org/web/20170208011736/https://forums.daybreakgames.com/landmark/index.php?threads/landmark-islands-list.54514/
- Landscapes, claim sites and caverns (Mar 2016): https://web.archive.org/web/20170208014622/https://forums.daybreakgames.com/landmark/index.php?threads/landscapes-and-claim-sites-and-caverns-oh-my.56105/
- Update notes index (user-supplied): https://web.archive.org/web/20170207213459/https://forums.daybreakgames.com/landmark/index.php?forums/update-notes-and-developer-discussion.68/
- How Stats Work (Avair, 21 Mar 2014):
  https://web.archive.org/web/20170207231015/https://forums.daybreakgames.com/landmark/index.php?threads/how-stats-work.25683/
- Update Notes 10/15/14 (Felgon):
  https://web.archive.org/web/20170207231009/https://forums.daybreakgames.com/landmark/index.php?threads/update-notes-10-15-14.49225/
- Launch-day stream (video, June 10 2016): https://massivelyop.com/2016/06/10/the-stream-team-landmark-launches/

## Chests and storage (archived update notes)

- Up to 50 chests per build site (Landmark Update Notes, 21 March 2016). Creatures: 100 per site.
- Bag 40 slots; vault 60, max 120 (same notes).
- Chest access: the "Customer" permission ("allows items to be taken or deposited in a chest"); "Copy Only" also gives
  chest access (Hotfix 26 April 2016). In the 0.589 client these are the renamed levels; the chest bit is 0x04.
- Chests can't be disassembled while they may hold items; a chest is deleted by picking it up and trashing the item
  (Hotfix 10 Dec 2015). A picked-up chest keeps its contents.
- Chests can be locked and unlocked (Update Notes 25 May 2016). Not built yet.
- Reward chests (craftable, timed per-player loot) arrived 29 August 2016, after this client.

## Terrain regeneration
- Dug/destroyed land outside build sites regenerates about 20 minutes after destruction; trees too (timer
  unconfirmed). Logging out in a deep hole: the land reappears and the player ends up on the surface. Gathering
  only on unclaimed land, not on build sites. Source: Landmark fan wiki, "Gathering"
  (https://landmark.fandom.com/wiki/Gathering).
