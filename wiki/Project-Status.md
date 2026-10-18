# Project Status

LandmarkEmu is in **heavy development**. This page sums up, in plain words, what the server does today. The detailed,
system-by-system lists (how each feature worked on live, what the server does, and what is still open) are in the
repository's research notes; the documentation pages explain how each system works.

How to read it:

- **Works**: built, and the game client accepts it.
- **Built, not yet checked in game**: the server side is written and tested offline or with a scripted client, but
  nobody has clicked through it in the real game yet. Bugs are likely here: reports are very welcome
  ([Bug Submission](Bug-Submission)).
- **Partly** / **Open**: something is missing, or a detail of how live did it has not been found yet. Nothing is
  written off as "unknown": open questions are kept on a list and looked at again from other angles.

Where live's exact numbers aren't known (for example how many reviews a Player Studio design needed), the server uses
its own value, says so in `.env.example`, and lets the server owner change it.

## Client and accounts

| | Status |
|---|---|
| The 0.695.1.132647 client logs in and plays; older clients are refused | Works |
| LaunchPad: servers by address, SRP accounts, the verified 0.695 update (from a file or from the server), undo, file validation, crash detection | Works |
| Account sign-up website with spam limits | Works |
| Login queue for a full world (members first), travel queue for a full landscape | Built, not yet checked in game |
| Membership and the one-time trial key | Built, not yet checked in game |

## Building

| | Status |
|---|---|
| Voxel tools (add, delete, heal, smooth, paint, fill, line, selection, copy / paste, undo / redo), free and paid materials | Works |
| Designs: save, list, place, rename, delete, categories, storage expansions; buying a design's missing materials | Works |
| Props: palette, discovered props, tints, scaling, recycling | Works |
| Mesh Shapes (the 0.695 build tab) | Built, not yet checked in game |
| Build sites: placing, deleting, size and buffer, the 8-site cap, upkeep ("Active Days") and expiry, permissions, names, tags, Gallery, screenshots | Works (some launch-era rules not yet checked in game) |
| Storage chests (50 per site), mover props (Translocation Pad, flinger, platforms) | Works |

## Story tools and site games

| | Status |
|---|---|
| Links, Play Mode / Edit Mode, Play Mode Rules (cameras, grapple, boots, dynamite on sites) | Works |
| Story tools: Story Node, Logic / Probability / Timer / Random Link filters, Focus, Damage, Reset, Counting Node and Scoreboard, IF Gate, Hour Node, Combat Node, Linking Areas, creature spawners, travel points, fireworks, reward chests | Mostly built; many of the 0.695 additions not yet checked in game |
| Creatures holding their weapons (Story Node and spawner "weapon visuals") | Partly: the stance works; a weapon model is shown only for the creatures it is known for, and that option is off by default |
| Site games: the 0.589 rule sets and the 0.695 Game Table rework (teams, scores, control points, Score / Victory nodes), Game Finder across worlds | Works on the server; several clicks not yet checked in game |

## World, combat and gathering

| | Status |
|---|---|
| Worlds and landscapes, Spire travel, Pioneer landscapes for new players, travel between worlds | Works (cross-world travel tested on a two-world setup) |
| Surface caves, Chaos Caverns, outposts, elites, Lost Chests and Lost Crystals | Works |
| Graveyards (surface and cavern), death, revive and flee | Built; parts not yet checked in game |
| Weapon combat, abilities, affinities, armour weight, visible projectiles, creature abilities | Works (some numbers are the project's own) |
| Creatures walking around cave rock, ruin walls and player-built walls | Built, not yet checked by eye |
| Gathering, ore veins, tool perks, regrowth, refining and composites | Works |
| Dynamite craters that heal back | Works |
| Day / night clock (8x real time) | Works |

## Items, social and progression

| | Status |
|---|---|
| Inventory, overflow, vaults, durability and repair, consumables | Works |
| Crafting at the Replicator, recipes, the Lumen Station | Works (not every Lumen Station category gone through) |
| Character creation, the Crimson Parlor (including sex and size changes and the Fantasy Parlor Pack), titles, Luminary Score, biography, inspect | Works |
| Chat (local, General, tells, custom channels), friends, ignore, guilds, groups, trade, mail | Works |
| Friends, guilds, groups, chat and mail shared across worlds (optional social service) | Works on a test setup; some clicks not yet checked |
| Showcase: store (live's 2016 items for Lumens, or free), Gallery and visiting, competitions, Player Studio | Works; Player Studio's peer review numbers are the project's own |
| Journal goals, daily goals, the watch list | Built; the new 0.695 tracker goals not yet checked in game |
| Founder's Pack, Year Zero and promotional items for every character (switchable) | Built |
| Player vendor shops | Off by default, as on live (the Vendor Booth was decorative from April 2016); can be switched on |

## Server architecture

Live Landmark ran as several cooperating servers. LandmarkEmu rebuilds that split, each part in its own container:

| | Status |
|---|---|
| Login server, account service, database, one container per world | Works (the default setup) |
| Shared social service (friends, guilds, groups, chat routing, mail, presence) | Optional, being built |
| Shared commerce service (Player Studio, contests, portraits, Showcase following) | Optional, being built |
| Environment Server (terrain store) in its own container | Optional, being built |
| Gateway in its own container (players stay connected while a world restarts), world channel container | Optional, being built |
| Admin web page with live-style monitor data (`/admin`, `/admin/monitor`) | Built |
| Several zone servers per world (one per landscape) | Open: planned for when one process can't keep up |
| Scheduled restarts with in-game warnings, a hung-world watchdog | Works |

## Known open questions

These are kept on the project's list of open leads and looked at again whenever a new source turns up:

- Live's exact numbers for some systems: movement-effect tuning for sprint and boots, recipe cooldowns, travel
  cooldowns, grief points, Player Studio review counts, some store prices.
- Which weapon each kind of creature held.
- Voice chat: the server answers it as unavailable; what live's voice service sent is not known.
- A few fields in the client's records whose meaning hasn't been found yet (they are sent with the values live sent).

## What isn't planned

- Real-money purchases: Daybreak Cash items are bought with Lumens earned in game.
- Anything that needs changed game files: everything must work on the unchanged client.
