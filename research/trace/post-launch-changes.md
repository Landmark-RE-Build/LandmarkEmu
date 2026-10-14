# Post-launch changes (June 2016 to Feb 2017) vs the 0.589 client

Sources:
- Developer posts from every update after the 0.589 build (2016-06-04), in design/patchnotes-after-0589.md (taken from design/forum/patches).
- The final file list (re/out/final-digest-2017): 426 changed and 583 new files, about 2.8 GB.

The 0.695 files themselves can't be recreated:
- The exe is compiled code, and a rebuilt file would never match the SHA-1s.
- Features must work on the unmodified client, so editing client files is ruled out anyway.

What can be recreated is the game's behaviour, where the 0.589 client already has the support.

## A. Server-side: possible on the 0.589 client
Balance, loot, rules and fixes the server decides:
- Outposts:
  - population pass.
  - exactly one treasure chest each.
  - NPCs not inside walls.
  - no collision after death.
- Lost Crystal loot lockout raised; teleports to other worlds and biomes.
- Underground Adventure to any zone, and from Pioneer landscapes.
- Iron a little more frequent.
- No ore nodes around spires.
- Combat equipment recipe costs; missing weapon recipes at the Lumen Station.
- Armour passives replaced or retuned (10 sets, Frenzied Slayer, Coat of Crashing Waves).
- Fiery Fungus and Proximity Mine damage terrain on build sites (29 Aug).
- Creature health and damage settings on spawners (29 Aug), if the 0.589 spawner window has the fields. Needs a trace.
- Halloween drops (hats as Crimson Parlor unlockers, Skeletal Bodysuit), then the December flight suits and holiday sweaters.
  - The 0.589 text already has "Skeletal Bodysuit" and the Colonist's Flight Suits, so these are drop-table changes.
- Reward chest group loot, Player Studio submission fix, game finder across worlds (server parts only).
- Goal fixes: "You Can Kill Those Things" needs more fungus kills, "Props to You", the Floor Switch goal removed.

Rule conflict: the user chose launch-era (0.589) rules. Changes like dynamite on build sites (30 June) differ from launch. These would need a switch (e.g. RulesEra = launch | final) rather than replacing launch behaviour.

## B. Already in 0.589 (shipped at the 7 June launch)
Crimson Parlor, Damage Node, Focus Node, Logic Filter, control points, scoreboard text, fireworks text.
- Check what our server already does for each before claiming gaps.

## C. Needs the 0.695 client: not possible on 0.589
New props, windows or assets the 0.589 client doesn't contain (no text, UI or models):
- Counting Node, IF Gate, Combat Node, Server Time Node, Random Link Filter, Respawn Location.
- Victory and Score Nodes, Game Table and Game Finder UI, Play Mode Rules window.
- Mesh shapes tab, modular build windows, cosmetic pets, Reward Chests, Trial Key.
- Bio and inspect, response buttons on Story Nodes, fill "smoothing" option, ambient volume slider.
- Continents 436-438 ocean and surface data, Halloween island assets.
- These could only come back with the real 0.695 files.
