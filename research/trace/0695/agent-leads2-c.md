# Open leads, second pass — group C (rules and numbers from design sources), 2026-10-17

Read-only pass over L10, L24-L27, L42-L46, L87-L93, L95, L100, L108-L112, L115, L119, L120, L129, L131. Each lead
started from its OPEN-LEADS.md entry and the morning sweep (agent-leads-sweep.md). I then searched every archived
source on disk:

- design/forum/all: 203 whole threads, 12,434 posts, every captured reply. re/out/forum_raw is the raw HTML of the same
  203 threads (the id sets match 1:1), so it holds nothing extra.
- design/forum/patches, the full wiki dump (2,337 pages), design/game-rules.md and patchnotes-after-0589.md.
- The client: gameData695 tables, re/out/text_695.tsv against text_589.tsv, the Feb 2017 achievement capture
  (re/out/achievements.tsv), item_names.tsv, and the Lua scripts in <0.695 test copy>.
- Byte compares between the Steam 0.589 install (<Landmark folder>) and the 0.695 test copy.

No Reddit or other archive is on disk.

Marks: **[P]** = a developer statement, a data row or a byte compare. **[I]** = a player report or an inference; the
entry says how many reports agree. P-numbers are rows of trace/0695/patchnotes.tsv.

## Summary

| Lead | Verdict | One line |
|---|---|---|
| L10 | ADVANCED | The only number ("once every 24 hours or so") is from **24 Jun 2016, after** the 16 Jun increase, not from launch. The server's 24 h matches it. 574500 is already built. |
| L24 | UNCHANGED | No design source gives item ids. |
| L25 | UNCHANGED | Only P5913-P5915 (Lumen Station "Fireworks" section). No effect id. |
| L26 | **FOUND** [I strong] | Roll Wraps were never released. Recipe 5331's tracker goal 1467 is missing from the Feb 2017 capture, while every released Replicator boot has one. The texts are new in 0.695. No wiki or forum mention. |
| L27 | ADVANCED (small) | Wiki: unlimited use, one pet out at a time. A player says pets reuse the Repair Bot follower. One player's kill count suggests the Lop Ear drop is rarer than our 2 %. |
| L42 | UNCHANGED | The wiki tier pages are already built 1:1 (Achievements.Tiers.cs). Record ids exist only in captures. |
| L43 | ADVANCED (tracker part FOUND, already built) | Dev, 20 Apr 2015: a recipe achievement completes by crafting the recipe once and gives no reward. Already built as TrackerGoalsCrafted. VISIBILITY_REQUIREMENT_ID stays open. |
| L44 | ADVANCED | Dev: "start with a set of basic creatures, unlock more". The wiki's innate creature list has **no** Barren. Flameblade comes from the Dungeon Builder's Starter Set. |
| L45 | ADVANCED | Rusty Sifter = Rusty Sifter (Small). Coiled Hanging Chain = Coiled Chain (Brass). The riddle goals are already built. |
| L46 | ADVANCED | Curator = goal 576977 "My Life, It Used To Be Incomplete" / "Swap Meet Established" (never live). In 0.695 "Curator" 577736 sits among the giveaway titles. Strip Miner / Deforester = the "Obtain …ium?" / "What About Teak?" capstones (I). |
| L87 | UNCHANGED | No wording. In 2014 the reminder was its own Notification Window type (P1590). The wiki says the mail often failed to send. |
| L88 | ADVANCED [P] | All 88 ContinentVeinData*.vdt are byte-identical in 0.589 and 0.695, so the 7 Jun change was server-only data. The earlier step on 26 Apr is P4808. |
| L89 | ADVANCED [P] | P4717 (26 Apr 2016) says "a few seconds to recover". A player (5 May) describes what ends it. The server's 5 s and drop-on-attack match. |
| L90 | UNCHANGED | No numbers anywhere. |
| L91 / L108 | **FOUND** [P data, I conclusion] | The four weapons were never craftable on live. Their tracker goals 786 / 750 / 788 / 842 are missing from the capture, while all 13 sibling Replicator weapons have theirs. The "missing weapon recipes" notes of May / Jun 2016 name other weapons. |
| L92 | ADVANCED | Dev: castle designs are earned "throughout the tutorial" (P5750) and "earn more through achievements" (P4224 block). There is still no per-goal list. |
| L93 | ADVANCED [I] | Modular Thinking still existed after the 7 Jun final pass (ginsann, 16 Jun 2016: players get stuck in it). |
| L95 | UNCHANGED | Only P6511. |
| L100 | ADVANCED [P chronology] | base_portal_spire.otd (the server's) is the **post-7 Jun** template: it has the Parlor (P5725) and the Repair Station (P5409). portalspirebase.otd is pre-11 May. The raise was for grass. |
| L109 | ADVANCED [I] | The Events world was reached via the teleport menu and was not in the login world list (58006 #3). |
| L110 | ADVANCED [I, 2 reports] | The event island had a Spire: "Kylen's build next to the spire" and "far from the spires" (58006 #11, #12). |
| L111 | ADVANCED | Dev: banner sites can be edited by staff only, and landmarks were "placed by hubs" on slopes. A player: a 25-voxel levelling base, left sticking up on flat ground (this matches our rule). Banner label format "<name> by <creator>". |
| L112 | ADVANCED (see L92 / L93) | |
| L115 | ADVANCED [P] | Apr 2015: 10 worlds. 7 May 2015: 4 worlds. 21 Mar 2016: Museum islands closed (P4235), EU worlds gone (I). Aug 2016: 5 worlds. |
| L119 | ADVANCED (small) | A player measured the rock's flight to a wall as about 1 s. "1 armor or 1 weapon" is read as "one of each" by a player. A pre-cap player data set gives daily recipe totals. No hour for the daily reset. |
| L120 | ADVANCED (small) | P4696: healers chase enemies when they are not healing. The wiki ties healer/support to Radiance. No rates. |
| L129 | ADVANCED [I, 3 reports] | Before 7 Jun, trips stayed on the same landscape and repeated the same caverns. P5734 answers that ("other worlds and biomes each time"). P5927 then extends it to the button. |
| L131 | ADVANCED (**reverses the sweep**) [I strong] | 2016 dev notes always use "world" for the server ("between landscapes or worlds", "same world to remove a group member", "on a new world called Events"). P5734 most likely means other worlds (servers), not only landscapes. |

---

## L10. Lost Crystal loot lockout — ADVANCED

- **The 24 h guess is post-increase.** urbansheople wrote "once every 24 hours or so" with "I dont know the exact
  time limit" (forum 57065 #213). The post is dated **24 Jun 2016**, eight days after P5829 (16 Jun, "significantly
  increased"). OPEN-LEADS.md and the Outposts.cs comment ("at launch a player guessed") place it wrongly.
- **[P] The developer post is 12 May 2016, not 4 May.** AugurDev wrote it (57065 #97). It confirms only a per-crystal
  timer, with the teleport prompt on lockout.
- **[P] The timer was long enough that repeated trips met locked crystals.** P5847 (16 Jun known issue) and P5927
  (30 Jun: "severely reduce your chances of encountering a Lost Crystal that you're unable to loot") say so. A
  10-minute lockout would not need that fix. This fits hours to a day [I].
- **Player proposals, not live values:** Voidlust suggested "every 10 minutes" (57065 #30). The 1-hour "maximum
  lockout" in 57902 / 57918 is about reward chests (L33 system), not crystals.
- **Searched with no further hits:** 57451, 57491, 57555 and 57596 (all replies), the wiki pages Lost Crystals,
  Chaos Caverns, Outposts, Loot and Daily Goals, StringHashToValue (only `CaveRewards.TooSoonToUse^574500`;
  `InstancedGames.LockoutTime^10` is the game tables' lockout), ClientTimerDefinitions, and the Lua scripts.
- **Server:** no change to the value. `Outposts.LockoutTime = 24 h` is now backed by the only post-increase estimate
  (1 report). Fix the comment in Outposts.cs:874-876 to "24 Jun 2016, after P5829" and correct the L10 entry. The
  574500 refusal is already built (Outposts.cs:832-839), so the sweep's server note is done.
- **Next:** archived streams (no transcripts on disk).

## L24. Login item list and missing item ids — UNCHANGED

- Forum and wiki give only names and sources:
  - P6142-P6147: pets, the Lop Ear drop, six from the Marketplace;
  - P5913-P5915: fireworks;
  - 58091 (6 Dec 2016): the three festive Colonist's Flight Suits drop from outposts, and the old holiday sweaters drop
    too;
  - 58108: a blue holiday sweater giveaway on the Landmark Explorer Twitch stream.
- No source gives an item id.
- **Next:** unchanged (other captures).

## L25. Fireworks — UNCHANGED

- **[P] P5913-P5915:**
  - 22 variations;
  - the consumables are crafted in the Lumen Station's "Fireworks" section and "set off a firework over your head";
  - the emitters come from drops or from "Props - Special Effects", and need sunstone.
- **[I]** 57648 links a fireworks-display thread that is not archived.
- No effect id in any source.

## L26. Roll Wraps — FOUND [I strong]: never released

- **[P] Every released Replicator boot has its Journal tracker goal in the live Feb 2017 capture:**
  - 717 Boots of Bounding, 720 Breeze Boots, 746 Dash Soles, 787 Leaper's Launchers, 798 Nimbus Boots (subcategory
    Boots);
  - 795 Neon Blitzing Boots (subcategory Extractor).
- Roll Wraps' recipe 5331 sits in the same Replicator boot sort block (28104, between Bounding 28103 and Breeze 28105).
  Its TRACKER_ACHIEVEMENT_ID 1467 is **not** in the capture, although the capture's ids run to 1490.
- The developers' rule: there is "a recipe achievement for each non-free recipe you can craft" (P3250, 20 Apr 2015).
  So Roll Wraps were not craftable on live by Feb 2017.
- **[P] Roll Wraps texts are new in 0.695.** 577205-577208 and 577211 are absent from text_589. They were authored
  after Jun 2016.
- **[I]** No forum or wiki mention. The wiki's Boot list (post-launch) omits Roll Wraps.
- **Server:** no change. Recipe 5331 is not default-learned and has no station mapping or recipe item, so Roll Wraps
  are unobtainable today, which matches live. BootAbilities.RollWraps stays for admin / dev tests. Mark L26 closed.

## L27. Cosmetic pets — ADVANCED (small)

- **[P] P6142-P6147:**
  - a "figurine" item you right-click to summon;
  - one pet at a time;
  - "cancel the pet icon above your health bar" to dismiss;
  - summon while NOT on a build site if the pet does not follow.
- **[I] Wiki pet pages** (6, all Marketplace for Daybreak Cash): "Unlimited use", "cannot fight". The wiki list omits
  the Lop Ear Bunny.
- **[I] One player:** urbansheople, 57751 #45: pets "take the code for the repair bot". The Repair Bot Beacon (P4799-era,
  26 Apr 2016) summons a follower NPC "for a short time". This points at the follower model.
- **[I] Lop Ear drop rate:** Uxtalzon (11 Aug 2016, 57834 #2) asked whether the bunny drop was affected, after "over
  800" elite kills. His count is mostly from before 28 Jul. If even about 100-200 kills fall after 28 Jul with no
  bunny, a 2 % rate would be unlikely (P(0) ≈ 2-13 %). That is weak evidence for a lower rate.
- **Server option:** consider `RareDrops.LopEarChance` 0.02 → about 0.005-0.01 [I, 1 report]. The decision is
  balance.

## L42. Goal tiers missing from the capture — UNCHANGED

- Every wiki tier page I read matches Achievements.Tiers.cs exactly (counts, Lumens, score, title, predecessor):
  - Slayer of All the Things / Warmaster, There Can Be Only One / Elementalist;
  - I Make the World 5,000,000, This Is All Wrong 5,000,000, All Of Them …Better Worlds 100;
  - Wind Beneath My Wings 18,000 s, I've Been Everywhere, Man 25;
  - Drums in the Deep 25, You've Delved Too Deep! 250, Explorer of the Uncharted 25, Professor of Archaeology 100 /
    Treasure Hunter;
  - I Feel the Earth …Move 500,000.
- What is still missing (live record / objective ids, icons) exists only in packet captures. No design source has it.

## L43. Replicator tracker goals and VISIBILITY_REQUIREMENT_ID — ADVANCED

- **[P] Tracker goals:** AugurDev (forum 54344 #1, 20 Apr 2015, P3250-P3253) says there is a recipe achievement for
  each non-free recipe. It completes "by crafting the recipe once", gives no Founder Score or rewards, and is tracked
  with the "Watch" button next to "Craft".
- **[I] Wiki, launch era** ("Crafting", "Crafting station"): the Journal's Recipes tab and the Replicator Watch button
  agree with that.
- This is **already built** (Achievements.Triggers.cs TrackerGoalsCrafted, E.16). The OPEN-LEADS "Need" line is stale
  for that half.
- **VISIBILITY_REQUIREMENT_ID:** no design source names hidden or greyed recipes. Searched "hidden/greyed/visible
  recipes" across the forum and wiki. Unchanged.

## L44. Non-elite Barren creature props — ADVANCED

- **[P] Domino, 15 Mar 2016** (56205 #1): "You'll start with a basic selection of characters and can collect more as
  you adventure!"
- **[P] P4224, 21 Mar 2016:** "You will start with a set of basic creatures, and will unlock more as you fight,
  complete Goals, or use lumens at the Lumen Shop."
- **[I] Wiki "Innate Creature Props (List)":** the starting set is Abomination ×3, Chomper ×3, Fiery Fungus, Slaug ×3
  and Target Dummy. It has **no Barren creature**. All 12 Barren pages say "Innate: No".
- **[I] Wiki "Cry Havoc!":** the goal's first step is "Craft the Dungeon Builder's Starter Set from the Lumen Station",
  and only then "Place the Barren Flameblade" (?/2). So Flameblades (and the set's Bowman) come from the starter set,
  not innate.
- The client's text holds "Recipe: Barren …" names and descriptions for all 12 (574376-574399, both builds). This
  agrees with "unlock more as you fight": they were drop recipes [I].
- **Server change (proposal, user decision):** stop treating the 12 non-elite Barren props as innate. The routes would
  be:
  - (a) the Dungeon Builder's Starter Set (Flameblade, Bowman; already in ItemUses.cs:85-96);
  - (b) "Recipe: Barren <X>" items minted with texts 574376-574399, added to the random recipe drops
    (RecipeBook.RandomUnknownRecipeItem). Our own ids are needed, because no live item record uses those texts.
- Without (b), 10 of the 12 would become unobtainable, so (b) must come with it.

## L45. Wiki pages under other names; riddle goals — ADVANCED

- **Rusty Sifter = Rusty Sifter (Small), prop 754 [I strong].** Both pages say innate with "40 Smelted Tin". The
  (Medium) page says 60 Cut Plain Wood and the (Large) page says not innate.
- **Coiled Hanging Chain = Coiled Chain (Brass) [I].** The page's only picture is `File:Coiled-chain-brass.jpg`. The
  client has Coiled Chain (Brass) 566499 / 566510, and the chain bundle 564836 lists "3 Coiled Chains". The audit's
  candidates (Curved / Straight Hanging Chain) are less likely.
- **Bowl of Apples, Large Square Knotted Rope, Dark Desert Succulent (+ With Butterflies), Glowing Jungle (Triple)
  Pendant Plant:** their pages are placeholders (no resources, placeholder images). Unchanged.
- **The riddle goals** (Ice Cold Brewskies, Better Than Reality, Hashtag Mutant!, Gosh It's Hot, Venison?, the riddle
  Likes) are already built from the client's own descriptions (574789-574794, Achievements.Triggers.cs:171-242,
  :388-396).
- **Change:** add the two aliases to re/out/agent_wiki/audit.py ALIASES. No server code change.

## L46. Titles with no known goal — ADVANCED

- **Curator 4363 (576980):**
  - [P] The client's text block 576977-576980 is name "My Life, It Used To Be Incomplete", description "Because sharing
    is caring!", objective "Swap Meet Established", reward "Title: Curator". That is the same layout as the next goal
    (576981 "Absence Makes The Heart Grow Fungus" …), which is live goal 1429. The block is present in both 0.589 and
    0.695.
  - [P] No record in the Feb 2017 capture, so the goal was never live then.
  - "Swap Meet" (574593) sits in the old site category list (574589-574593: Western, Combat, Story, Tutorial, Swap
    Meet). The wiki "Swaps" page: a swap is a site that shares designs. So the objective was "publish a site as a swap
    meet" [I].
  - [I] 0.695 adds a separate "Curator" 577736 inside the late-2016 giveaway / event title block (577727-577745: Hero of
    Landmark, Landmark Explorer, Holiday Hero, Creator of Winter Wonderlands, Icy Architect, Curator, "giveaway title for
    sunset players", "Title for builders on winter island 2016"). Landmark Explorer is the Twitch stream that ran
    giveaways (58108). So in the 0.695 era Curator was a staff-given title, not a goal reward.
- **Strip Miner 3925 / Deforester 3926:**
  - [P] Both are announced capstones (575520-575523, announcements 575528-575537).
  - "…finally got enough burled wood!" (575534) fits Deforester and the burled-wood objective of 644 "What About Teak?"
    (50,000 burled wood). "…has obtained the unobtainable!" (575535) fits the unobtainium pun of 642 "Obtain …ium?" (ore
    harvesting), hence Strip Miner [I].
  - [P] In the 2017 capture and on the wiki those goals give only Lumens and score, and their meta goal 645 gives
    Researcher. So both titles were retired from goals before 2016.
- **Server:** no change (live gave none of the three from goals). Optional: list them as admin-grantable giveaway
  titles. Fix the comment at Achievements.cs:1119-1120: 575535 is more likely Strip Miner than Treasure Hunter [I].

## L87. Upkeep reminder wording — UNCHANGED

- **[P] P1509 (7 Aug 2014):** reminder at 3 days (was 1 hour).
- **[P] P1590 (27 Aug 2014):** "Notification Windows (Mail, Upkeep reminder, Player Studio Approvals)".
- **[P] 50269 (14 Nov 2014):** one Notification Window compiles "mail, upkeep reminders".
- So the reminder was its own notification type next to mail. That fits the client's GAQ "Upkeep expires in less than
  [hours] hours!" (Claims.GaqExpiring 562253) and `Claims.ExpireTimeAlertSeconds^86400`, whose creator is dead code in
  0.695 (TASKS 3.18).
- **[I] Wiki** (Build Site Management, Build Sites): "a message warning you"; "In some cases, it is not sent".
- No mail text anywhere. Keep ours.

## L88. Iron frequency — ADVANCED [P]

- **[P] Byte compare:** all 88 `CommonData/Voxel/veinData/ContinentVeinData*.vdt` (per-zone spawn pools and per-vein
  counts, read by OreVeins.ReadVdt) are byte-identical between the Steam 0.589 install and <0.695 test copy> (0.695). With
  ResourceVeins.txt also identical, the 7 Jun 2016 "a little more frequent" change (P5796) lived only in server data. No
  client file holds the new density.
- **[P] Earlier step, P4808 (26 Apr 2016):** "Iron deposits are now more dense in Chaos Caverns and available on the
  surface of (non-Pioneer) Landscapes as well." So surface iron is a **non-Pioneer** rule; check that Pioneer surface
  pools carry no iron.
- **[I]** 57065 #13 (Smallcorners, 4 May): "silver and iron all over everything" underground. Doomsayer (57252 #42) only quotes the note.
  No counts.
- **Server option:** if a multiplier is wanted, a small one (e.g. ×1.2-1.5) on iron mappings' surface / cave counts in
  OreVeins. That is ours; no live number exists.
- **Next:** none in the archives.

## L89. Invisibility after a revive — ADVANCED [P]

- **[P] P4717 (26 Apr 2016):** "You should now have a few seconds to recover after reviving, before Creatures attack
  you again".
- **[I] One player (57066 #17, 5 May 2016):** after a revive you could attack an NPC "but the NPC just stands there …
  until the revive grace period has passed", and asked that "once the player engages the mob, the mob should instantly
  engage". AugurDev liked the post.
- **[P] P5832 (16 Jun):** the immunity "should now properly drop when you enter combat".
- **Rule:** a few seconds, ended by the player attacking. Our `Health.ReviveGrace = 5 s`, dropped when the player
  damages a creature, matches. No change. Close as far as the sources go.
- The client's `Teleport.PlayerTeleportImmunityTimeMs` = 1700 is the separate teleport immunity.

## L90. Frenzied Slayer / Crashing Waves numbers — UNCHANGED

- P5747 / P5748 only. Searched every 2016 thread for frenzied, crashing waves, passive, proc, nerf, execute, insta-kill.
- The wiki pages give only the effect wording ("can instantly kill opponents that are low on life"; "all nearby
  opponents can suffer damage and be Frozen").

## L91 / L108. Four weapon recipe items — FOUND [P data; I conclusion]: never craftable on live

- **[P] Recipe ↔ tracker:**
  - Katana of Crashing Waves 3029: recipe 2429, tracker 786;
  - Desperado's Deadly Arsenal 3038: recipe 2432, tracker 750;
  - Lightbringer's Justice 3028: recipe 2433, tracker 788;
  - Silent Shadow's Condemnation 3039: recipe 2434, tracker 842.
- **None of 786 / 750 / 788 / 842 is in the Feb 2017 capture.** The capture does hold the Replicator "Weapon" tracker
  goal of every sibling recipe in the same sort block 24100-24106:
  - 705 Avenging Champion's Exaltation, 727 Crusader's Courage, 753 Diabolical Scientist's Raygun;
  - 759 Electrified Assault Axe, 763 Flame Bound Bastion, 766 Frenzied Slayer's Battle-Axe;
  - 769 Frost Lord's Fury, 779 Gunslinger's Six-Shooters, 796 Night Herald's Mercy;
  - 807 Overcharged Recon Blaster, 854 Spear of Piercing Judgment, 889 Supercooled Power Cannon;
  - 896 Unflinching Embodiment of Might.
  
  By P3250 (one tracker goal per craftable recipe), the four were not craftable on live.
- **[P] The weapons still had placeholder descriptions** (572835 "A sweet radiant bow.", 572837 "A sweet two handed
  water katana.", …). No wiki weapon page and no forum post names them.
- **[P] Correction to the sweep:** the "missing weapon recipes" quotes name other weapons.
  - Baloris (57170 #35) said only Frenzied, Electrified and Unflinching were at the Lumen Station. Domino replied
    (#44): "They ARE dropping but are not yet on the lumen station".
  - "Some missing weapon recipes are now available at the Lumen Station" (P5735, 7 Jun) refers to the 25 May list
    (Frost Lord's Fury, Blight-Warden's Pain, Crusader's Courage, Night Herald's Mercy, Staff of the Lightning Caller).
  - None of those posts concerns the four.
- **[P] Why they exist:** text block 575951-575964 pairs each weapon recipe with its armour (Frenzied Battle-Axe /
  Chainmail, Katana / Reinforced Coat of Crashing Waves, … Silent Shadow's Condemnation / Cloak). P5327-P5332 (11 May)
  made those armours the 500-kill goal rewards. The matching weapons were authored but not shipped [I].
- **Server change:** add 3996, 3999, 4000, 4001 to `RecipeBook.RetiredRecipeItems` (RecipeBook.cs:236, today
  {3931, 3956, 3957, 3958, 3977}), so they no longer drop. Keep owned copies working.

## L92 / L93 / L112. Tutorial goals and the castle designs — ADVANCED

- **[P] P4224 block, 21 Mar 2016:** "You'll start with a handful of these and earn more through achievements, until
  you have a full set of modular castle pieces".
- **[P] P5750, 7 Jun 2016:** the castle designs are "unlocked as intended throughout the tutorial". So they come from
  more than one tutorial goal, not from one goal [I].
- **[P] 57063 #1 (Domino, 4 May):** new starter designs (the nested arcs) are received "once you complete the final
  building tutorial 'Modular Thinking'".
- **[P] P5361 (11 May)** and **P5544 (25 May):** more starter designs, then the Architecture Tools, from Modular
  Thinking.
- **L93 [I, 1 report]:** ginsann, the castle designer, wrote on 16 Jun 2016 (57451 #133, after the 7 Jun "final
  pass"): "players keep getting stuck in 'modular thinking' tutorial". So Modular Thinking was **not** retired on
  7 Jun. Whether it survived to Feb 2017 is still unproven (the captured player had not finished 685).
- **No change** to the built chain. TASKS 5.6 can treat the three goals as live after 7 Jun [I].

## L95. Spawner row 40 — UNCHANGED

- The only source is P6511 (29 Sep 2016): NPCs that "spawn with weapon equipped" never attacked. Nothing on the option
  values.

## L100. Spire raise — ADVANCED [P chronology]

- **[P] Which template is newer.** base_portal_spire.otd holds the Crimson Parlor 2942 (introduced 7 Jun 2016, P5725)
  and a Repair Station 2895 ("The Spire Hub should now have a Repair Station", P5409, 11 May). portalspirebase.otd has
  neither (and the older Replicator 2090). So base_portal_spire.otd is the **post-7 Jun** Spire. It is the one the
  server stamps (Spire.cs:45). The two files are identical in 0.589 and 0.695 (sizes; the sweep's md5).
- **[P] P5647's reason** was grass: spires were "overgrown with grass on some landscapes".
- Its props sit 0.8 m *lower* in the same 310×170×310 box than in the older template. So the raise was not done by
  moving props up inside the template. It was either the template's ground (voxels) or a placement offset.
- **Next:** decode both templates' cells (VoxelTemplate.Load with cells) and compare the solid column under the Hub.
  Cells are 16 m, so the cell list cannot resolve 0.8 m. If the newer template's base is the same, add a small
  placement offset (≥ the 0.8 m difference, enough to clear grass) in Spire.cs. Otherwise no change.

## L109. The Events world — ADVANCED [I]

- **[P] 58006 #1 (Domino):** "access the Halloween Party Island via the teleport menu, on a new world called
  'Events'". **[I] 58006 #3 (CoyoteDancer):** you cannot log back in onto it; you must pick a regular world.
- So the Events world was a teleport-list world, absent from the login server list.
- No id or text id in any source.
- 567016 "Events" stays the only candidate (sweep).

## L110. Event island rules — ADVANCED [I, 2 reports]

- **Spire: yes.** "Kylen's build next to the spire" (58006 #11, CoyoteDancer, 29 Oct 2016). "so far from the spires
  nobody visit" (58006 #12, Helie). The server's Spire on event islands matches.
- **[P]** 55925 (2015): builds used normal claim flags. 58077: left-over sites expired at the switch (already noted).
- The expiry stays a user decision.

## L111. Landmarks of Landmark — ADVANCED

- **[P] Domino (57799 #6, 5 Aug 2016):**
  - "We tried to choose good locations";
  - a landmark site "with the fancy blue header banner" can be edited only by staff;
  - the contest rules said landmarks must "accommodate placement on a slope" and "will be placed by hubs".
- **[I] Parthin (57799 #7, 13 Aug):** "We were instructed to have a 25-voxel base for leveling, yet some of the
  landmarks have been placed on a flat area with the base sticking up."
  - Live sank the build by its base on slopes, and on flat ground the base stood up. That is what our rule (lowest
    solid layer on the lowest ground under it) produces.
  - No change; cite it in LandmarksOfLandmark.cs.
- **[I] Squishimus (57799 #3):** a banner read "100% Prop Free Tower by Candela" instead of "Dark Elf Tower of
  Darkness by Yarnila". So the banner title was "<build name> by <creator>". Check that our record string uses that
  form.
- (1)-(4) of the lead (exact spots, award model, Willows End template, combined claims) are not in any archived text.

## L115. World list history — ADVANCED [P]

| When | Worlds | Source |
|---|---|---|
| Apr 2015 | Adventure, Confidence, Courage, Determination, Foundations Museum (visit-only), Liberation, Rebellion, Satisfaction (EU), Serenity, Understanding (EU) — 10 | [I] player list, 54355 #42 (Zadira Kindleriver, 23 Apr 2015); matches the client names 560911-560919 |
| 7 May 2015 (big wipe) | Liberation, Courage, Serenity, Satisfaction (EU) — 4 | [P] Dexella, Communications Manager, 54533 #1 |
| 21 Mar 2016 (relaunch) | Foundation Museum islands closed | [P] P4235 "The Foundation Museum islands are no longer accessible" |
| 21 Mar 2016 | EU worlds closed | [I] Druzella (56259 #230, 23 Mar 2016: why were the EU servers closed); TwinDenis (57232 #14, 13 Jun 2016) asks for EU servers |
| May-Aug 2016 | Courage, Determination, Liberation, Rebellion, Serenity — 5 | [I] players on Determination / Rebellion (57065, 57105, May 2016); [P] 57799 lists the five |
| Oct 2016 | + Events | [P] 58006 |

- The client's further names 560920-560926 (Gratitude, Prosperity, Success, Triumph, Opportunity, Inspiration) appear
  in no post.
- What happened to characters and claims on closed worlds: the 2015 and 2016 wipes reset everything (P3478 / P4224
  context). No per-world migration is described.

## L119. Highfall Catapult and the recipe day — ADVANCED (small)

- **[I] WhiskyJack (57751 #106, 28 Jul):** "Catapult triggers timer … waits for projectile to get to wall. about 1 sec". Our
  16 m/s over 10-28 m gives 0.6-1.75 s, which is consistent.
- **[I] Wiki:** about 60 blocks, knock-down AoE (known).
- **P4438:**
  - [P] Domino (56713 #1): "only get 1 armor or 1 weapon recipe per day".
  - [I] Mullian (56713 #18): "receiving one of each". That supports our one armour + one weapon.
  - [I] matt3k (56713 #40, data from 25-31 Mar 2016, *before* the cap): 16 / 16 / 16 / 17 / 7 / 22 / 22 recipe drops
    per day from lumber. All came in the first 2-2.5 h, in batches of 2-3 with 10-30 min gaps, and none after. This is
    useful for Pacing's general recipe curve.
- **Day turnover:** no hour found. The only scheduled times are the Mon / Sat 03:00 PT restarts (57554).
- No catapult numbers. Keep ours.

## L120. Chaos Cavern drops and healers — ADVANCED (small)

- **[P] P4696:** "Healers will now pursue enemy targets who move out of their range when not busy healing." This is a
  behaviour rule. Check Creatures' healer think: when no ally needs healing, a healer should chase and attack like any
  creature.
- **[P] P4353 / P4536 / P4533:** random resource drops; the rare rate was adjusted; Fiery Fungus drops tree loot "with
  a low chance to occasionally drop rare loot".
- **[I] Wiki Affinity:** Radiance is the "healer/support" archetype. That supports the radiance-minion healer
  candidates.
- No rates or heal amounts anywhere.

## L129. Trip landscape / cavern pick — ADVANCED [I, 3 reports + dev wording]

- **Before 7 Jun 2016** both the button and the crystal stayed on the current landscape and repeated caverns:
  - Shardiis (57065 #51, 6 May): "if I am in a Deciduous biome … I will get a Deciduous cavern, the same cavern, every
    time … continue … after clicking on the lost crystal, I get another Deciduous cavern, the same second cavern, every
    time";
  - ManInLevis (#53, 7 May): the crystal "keeps me on the same island. Mix it up please" (liked by AugurDev);
  - Aldente (#224, 27 Jun): four button trips in a row, all Determination / Silver Shallows, three to the same outpost.
- **[P] P5734 (7 Jun):** crystals go "to other worlds and biomes **each time** they're used". That answers those posts:
  a different place, and a different biome, every time.
- **[P] P5927 (30 Jun):** the button "can now send you to Chaos Caverns in any zone, like the Lost Crystal interaction
  already does".
- **[I] 57065 #157 (BuilderX):** caverns are "part of the world", not instances. Another group can be in the same
  cavern (57065 #202, CoyoteDancer, 9 Jun).
- **Server change (Outposts.NextCave):** prefer an outpost on **another landscape whose cave biome differs** from the
  current one. Fall back to any other landscape, then any outpost. The rest stays: lockouts first, and the queueing of
  unplaced landscapes. Caves.TravelRandom (the button) stays "any non-Pioneer landscape".

## L131. "Other worlds" — ADVANCED (reverses the sweep's reading) [I strong]

- **[P] In 2016 the developers use "world" for the server every time:**
  - "different world when they join" and "same world to remove a group member" (P4905 / P4906, 26 Apr 2016);
  - "teleports between landscapes or worlds" (P4907);
  - "change worlds" (P5841, 16 Jun);
  - "World and landscape name with /loc" (Domino, 57105 #72);
  - "the world of Courage" (57555);
  - "a new world called Events" (58006).
- **[I] Players do too:**
  - 57166 #71 (Persephone, 8 Jun): "teleporting to other worlds"; #48 (Sizuma): "port around to different islands, servers, outposts";
  - Fredelas (57451): "log out and choose a different server".
- The Travel window's "Other Worlds" button tooltip reads " Teleport to any world on any server." So cross-world
  teleports were ordinary in 2016.
- P5734's "other worlds and biomes" most likely means another world (server) and landscape. The sweep read it as
  landscapes only.
- **Server change (when more than one world runs, GATEWAY=1 / two-world stack):** let Outposts.NextCave draw from the
  outposts of every running world, with weight by landscape. Hand over with CrossWorld.Travel (B.10) to the chosen
  world's cavern.
  - With one world, behaviour is unchanged.
  - The client side (NextUndergroundReply has only the u64 token; the move is a normal zoning) needs a harness check
    with the two-world stack before the change.

---

## Server changes to make (from this pass)

1. **RecipeBook.cs:236 `RetiredRecipeItems`:** add **3996, 3999, 4000, 4001** (L108). The four weapon recipes leave
   random drops.
2. **Outposts.NextCave:** for crystal trips, prefer another landscape with a different cave biome (L129). Draw across
   worlds when more than one world runs (L131, after a harness test of the hand-over).
3. **Comments / docs:**
   - Outposts.cs:874-876: the 24 h guess is from 24 Jun 2016, after P5829 (L10);
   - Achievements.cs:1119-1120: 575535 is more likely Strip Miner (L46);
   - LandmarksOfLandmark.cs: cite 57799 #6 / #7 for the slope / base rule (L111);
   - OPEN-LEADS.md: close L26 and L108, and mark L43's tracker half as built.
4. **Proposals for the user:**
   - L44: make the 12 Barren props non-innate together with minted "Recipe: Barren …" drop items (texts
     574376-574399);
   - L27: lower `RareDrops.LopEarChance` 0.02 → about 0.005-0.01;
   - L88: optional iron count multiplier (ours).
5. **Checks:**
   - Pioneer surface pools have no iron (P4808);
   - the healer think chases enemies when idle (P4696);
   - the landmark banner title is "<name> by <creator>" (L111).
