# LandmarkEmu — project status (2026-10-12)

Detailed history: MODLOG.md. Things for you to try in game: TESTING-NEEDED.md. Developer docs: LandmarkEmu/docs/.

## Working (built and verified with the test harness and/or on screen)

- **Login and world:** login (auth, gateway, encryption), character select / create, zone entry, movement and
  multiplayer sync, day/night clock, landscapes / continents travel, the Spire hub, Other Worlds.
- **Social:** chat (flood limits), friends, groups, guilds (invite popup, promote / demote / kick, Officer rank),
  mail (coins, cash on delivery), trade (invites through the popups), emotes, titles, achievements and goals.
- **World:** trees and plants, mining (yields by slope and biome), caves and Chaos Caverns, ore veins (Holoseeker),
  creatures (kobolds, Slaug, wolves, deer, ogres) with combat, loot and death, death window and respawns,
  "fell out of the world" rescue.
- **Terrain:** the VoxelFarm terrain decoded, smooth dug holes, natural regrowth, persistence, re-zone rules that
  prevent client hangs and crashes.
- **Building:** build sites (place, delete, permissions, upkeep, rename, music, rating, contests), voxel building
  (place, smooth, paint, undo/redo, copy/paste), designs (save, list, place, rename, delete), props (placement, tints,
  lights, settings window, door / lever animations, chests), palettes.
- **Items:** crafting stations and recipes, salvaging, consumables, durability and repair, gear and outfits, weapon
  attacks, floating damage numbers.
- **Site games:** Deathmatch, Team Deathmatch, Zone Control, Adventure; HUD, scoreboard, observers, queues; the
  client's own Game Table editor (settings, prop list, enable/disable); boss display for BOSS spawners; adventures end
  with a Chaos Cavern trip.
- **Story tools:** Links and Play Mode; switches, doors, timers, dice, story nodes, spawners, Travel Points,
  Linking Areas, clicking creatures, creature routes.
- **Showcase (HUD button):** free store (outfits, decorations, designs, with icons), build site gallery with Visit, Help
  page; players signed in by token. **Build site vendors** (player shops: list items from the Showcase, visitors buy
  in the game's Vendor window, sellers paid by mail). Winner banner shows the real winner. Portrait requests answered
  from stored portraits (the 0.589 UI shows none).
- **Other:** bug reports and build-site reports saved to Reports/; hardening (forged sizes refused, rate limits).

## Needs work / parked

- **0.695 client:** waiting for that client (the server speaks 0.589).
- **Needs you:** held-mouse mining pace, mountain-top hole rims, and the checklist in TESTING-NEEDED.md.
- **Client data missing:** story creatures equipping weapons, warp network, NPC merchants (the merchant window exists, no NPCs).
- **Not traced yet:** what makes the client capture and upload its own portrait; the crafting window's "buy missing" (Point of Need) query.
- **Heal on dug ground:** the client's own heal finds nothing to restore (natural regrowth covers it).
- **Your decision:** props outside build sites (currently build sites only); store prices later (free for now).
- **Not in 0.589's content:** NPC merchants (the client's merchant window exists but nothing opens it).

## Tools used

- **Reverse engineering:** Ghidra (headless + the pyghidra-mcp server), JPEXS FFDec 26.3.0 (UI Flash to
  ActionScript), Python 3.12 with capstone 5.0.1 and pefile, and the scripts in re/tools (fn.py, pkt.py,
  coverage.py, uimap.py, packgrep.py, strhash.py and more).
- **Live client inspection and tests:** Frida 17.23.0 + frida-tools 14.11.0; the re/test harness (run.py, client.js,
  ~65 scenarios) against a patched test copy of the client; window screenshots (re/winshot.ps1).
- **Data:** lz4, minidump, numpy, Pillow.
- **Server:** .NET SDK 10.0.401 (C#, Entity Framework migrations), MariaDB 13.0.2, NLog.
