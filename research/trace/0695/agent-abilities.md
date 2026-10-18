# Features audit #55: rank 2 / 3 abilities, /playdead, Roll Wraps (agent report, 2026-10-17)

Branch `e-abilities` (worktree `<workspace>\wt-abilities`), two commits on top of client-0.695 d285059:

- `c46adaf` Creature abilities: Combat Node ranks 1-3 and specials, ability names in combat spam (#55a)
- `9d8b1ea` Emotes: /playdead for 0.695 clients (#55c)

`dotnet build LandmarkEmulator.WorldServer/LandmarkEmulator.WorldServer.csproj -v q` succeeds. Nothing was pushed or run.
No shared notes were edited. The OPEN-LEADS updates are proposed in §4.

Tags: [P] proven from client data or code, [I] inferred.

---

## 1. #55a Rank 2 / 3 abilities: what they are

### 1.1 They are creature abilities, not weapon abilities [P]

The audit row called them "weapon abilities". They are not.

- Texts 577260-577337 come in pairs ("Poison Blade 2" / "Poison Blade 3" ... "Darkfire 2" / "Darkfire 3", 39 lines).
  The rank-1 texts are older ids that 0.695 **renamed**: 572981 "Slashing Blade" → "Slashing Blade 1", 564156 "Fireball" →
  "Fireball 1", 573031 "Poison Blade" → "Poison Blade 1" (trace/0695/text-new.tsv).
- Every rank-1 text sits in the **creature text block** 572869-573380, right after the name of the creature that uses
  it. For example: 572980 Barren Grunt, 572981 Slashing Blade 1, 572982 Blade Lunge, then 572983 Barren Flameblade,
  572984 Burning Blade 1, 572985 Fiery Lunge.
- The fandom wiki's creature pages list them as the **Combat Node's** choices. For example, design/wiki/pages/Barren
  Grunt.wiki: "When linked to a Combat Node, this creature can be made to perform the following Abilities: Ability 1:
  Slashing Blade 1, Ability 2: Slashing Blade 2, Ability 3: Slashing Blade 3, Ability 4: Blade Lunge". 25 pages have a
  list. The Combat Node came in the 28 Jul 2016 update (P6149-P6157: "a list of the creature's abilities will be
  displayed in the Combat Node settings window").

### 1.2 How the client picks a rank: it doesn't [P]

The client never chooses a rank. The server chooses which ability a creature uses. The client only shows the name.

- **No client code.** functions-695.md §8 found no new client code for them. Every Ability* table in gameData695 is a
  stub: AbilityLines and AbilityLineMembers have a few test rows, AbilityEx has one "Test" row, and AbilitySets has 2
  rows. None of them contains any of these text ids. A grep of all 467 tables for 577260 finds nothing.
- **No item, stat or account link.** No item record names these abilities, so there is no item, stat, account or
  weapon-tier choice to make.
- **The ability definitions lived on the server.** AbilityDefinitions.txt (NAME_ID, …) was never shipped
  (trace/systems/abilities-combat.md).

### 1.3 Where the client shows the name: combat spam [P]

CombatSpam.txt row 1 "Damage" uses 564566 "#Owns([*SourcePlayer*]) [*Ability*] hit YOU for [*Value*] damage." (564567 and
564568 are the outgoing and other-player lines).

- **Spam builder.** 0.695 FUN_142fe2840(this, spamType, &source, &target, {u32 nameText, float value, u32 stat}) looks
  up `*param_5` with FUN_142d8b3e0 and fills the "Ability" token with it (part_0080 ~26495).
- **From the wire to the spam.**
  - The PlayerUpdate CombatResourceEvent 0x26/0x67 reader is FUN_142c278c0 (via FUN_142c4b910). Its object layout is:
    - +0x18 u64 source
    - +0x20 u64 target
    - +0x28 u64
    - +0x30 i32 delta
    - +0x34 u32 type
    - +0x38 u32
    - +0x3c u32
    - +0x40 u32
    - +0x44 bool crit
    - +0x48 u32
  - The handler case (part_0065 ~20821) sends a negative delta of type 1 to FUN_14308e060(&target, &source, amount,
    [+0x38], [+0x40], [+0x3c], crit, [+0x48]).
  - FUN_14308e060 calls FUN_14308de50, which calls `FUN_142fe2240(spam, source, target, param_6 = [+0x40])` with the
    damage as the 5th argument.
  - The disassembly of FUN_142fe2240 (capstone, read-only) is:
    `mov [rsp+0x30], r9d; cvtdq2ps [rsp+0x70] → [rsp+0x34]; [rsp+0x38]=0; edx = (source==X)+1; call FUN_142fe2840`.
- **So the wire u32 at +0x40 is the [*Ability*] text id.** The server had always written 0 there.
- **0.589.** It has the same chain (FUN_142fee730 → FUN_142f424d0 with param_6) [I, same code shape].

### 1.4 Which creature uses which ability

All 39 rank lines were mapped to a creature. The table is `Combat.CreatureAbilities.Sets`.

- **W (wiki, 25 lists):** Abomination Skullslapper / Skulltosser / Slapper, Agitated / Lunging / Spitting Chomper,
  Barren Bowman / Coldmage / Fireshot / Flameblade / Flamemage / Grunt / Icebrute / Lightbringer / Protector /
  Shadowshot / Stormmancer / Stormshot, Elite Barren Captain / Sergeant, Fiery Fungus, Powered Mega-Knight, Slaug
  Slasher / Stormer / Stormslasher, Vine Trap.
- **T [I] (text adjacency, 23 lists):** the rank-1 text directly follows the creature's name text.
  - Elite Barren Lieutenant (Shaded Slash, Shaded Strike) and Elite Barren Mageking (Darkfire, Shadow Seal). The wiki
    gives each four unnamed abilities, which matches the four found here.
  - Scalewind Dropper (Poison Blade / Toxic Lunge), Highfall Frostblade, Highfall Warden, Scalewind Lightrunt, Elite
    Scalewind Nightblade, Elite NovaTech Researcher, Ebonguard Brutalizer, Elite Ebonguard Commander, Highfall Shockblade,
    Highfall Darkguard, NovaTech Colonist, Scalewind Toxifier, Ebonguard Archer / Attacker / Fireblade / Watcher /
    Sentinel / Shadowslayer, NovaTech Pilot, NovaTech Scientist, Scalewind Smasher.
  - Order: ranks first, then the special. 8 of the 13 wiki lists that have both use this order.
- **Two "Arctic Blast" lines.** 577330 / 577331 belong to the Barren Coldmage: the last five pairs are the Barren
  casters in wiki order (Fireball, Arctic Blast, Voltaic Sphere, Light Orb, Darkfire). 577322 / 577323 belong to the
  NovaTech Scientist, placed among the NovaTech lines [I, strong].
- **Not placed yet.** Four lines have no creature name directly before them (lead E1 in §4).

## 2. #55c /playdead [P]

- **The word.**
  - The word is 577222 "playdead". Like the other emote words (559123-559192), it is a server command: the zone's
    command list registers it, and nothing in the client code references it.
  - The client handles only /sit itself, through CodeStringMappings Emote.SitSelf / SitOther. A grep of the decompile
    for the playdead ids (hex and decimal) finds nothing, and CodeStringMappings has no playdead row.
- **The animation.** It is AnimationSelection **844**, a row new in 0.695. Its variables (AnimationSelectionVariables
  2781 / 2782) are **Stance_Index 6** and **BreatheSpeed 0**. 0.695 also gave 223 "stand" **BreatheSpeed 1** (row
  2783, new).
- **The 0.695 animation network.** I extracted PlayerAnimationNetwork64.mrn from Assets_153.pack (read only, offset
  from fileindex_695) and decoded its node name table. Its StanceSwitch|Switch1 (node 14514, control
  ControlParameters|Stance_Index) has these children, in order: Stand, Kneel, Placeholder, Dance, Sit_Chair, Laydown,
  **PlayDead**, AFK_States.
  - So index 6 is the **new PlayDead state**: clips Biped_Core_React_Death_01 / _2, exit Biped_Core_React_GetUp_F01.
  - In 0.589 (re/out/anim), index 6 was AFK_States and there was no PlayDead.
  - Scratch files: `PlayerAnimationNetwork64_695.mrn`, `node_names_695.json` and `switch.py` in this session's
    scratchpad.
  - The other stance indices (L117 / PropSettings) are unchanged: 0-5 are the same, and AFK_States moved to 7.
- **The chat lines.**
  - To the player: 577224 "You play dead." / 577225 "You play dead with [*target*]."
  - To others: 577227 "[*speaker*] plays dead." / 577226 "[*speaker*] plays dead with [*target*]."
  - They are sent as ChatPacketFromStringId (ChatBase 0x13 sub 4). The reader FUN_143356860 reads {u64 speaker, u32
    text, bool×3, u32, u64 → +0x38, u64 → +0x30, u32}. The 0.695 handler FUN_143364980 case 4 fills "target" from
    +0x38, the first u64 after the u32, so the existing `FromStringId` layout {…, u64 target, u64 owner, u32} is right.
  - 577223 "Playing Dead" is the state's label and is not sent.

## 3. #55b Roll Wraps 4406: where they came from. Still not found (L26)

### 3.1 Searched this session (in addition to L26's list)

- **wiki pages.** No "Roll Wraps" page. Boot.wiki lists 9 boots, and Roll Wraps is not among them.
- **design/forum/all and re/out/forum_raw.** No "roll wraps", "acrobatic", "dodge roll" or "roll in any direction"
  in a Landmark context.
- **patchnotes.tsv from Jul 2016.** No boot, roll or dodge note.
- **Every gameData695 table.** 4406 appears only in Recipes 5331. The row 4406 in CraftingStationRecipeMappings is a
  row id, not this item. Chests, LootPinata, MerchantSets, ItemGroups / ItemGroupMappings, ItemLineMembers and
  CharCreateGearSet have nothing.
- **Recipe item.** There is no "Recipe: Roll Wraps" item (item_names.tsv).
- **Reward-chest descriptions (577607-577635).** These are resources plus "the occasional recipe".

### 3.2 What the data does show

- **The recipe can never be crafted.** Recipe 5331 is new in 0.695, with DEFAULT_LEARNED 0, CAN_CRAFT_WITHOUT_STATION 0
  and **no station mapping**. Every other boot recipe is default-learned at station 53 (83, 85, 233, 234, 1152, 1153,
  1329). With no station, the 0.695 client would not list 5331 at any station, even if the recipe were taught. The
  0.695 Recipes table has 931 rows like this (403 of them type 3), mostly retired rows such as the old trees, cut
  blocks and superseded gear recipes. Roll Wraps is the only one whose item has no other recipe.
- **The tracker achievement does not exist.** TRACKER_ACHIEVEMENT 1467 is shared with "Recipe: Painting: The Withered
  Land" 5084. It is not in the live achievement capture, and Requirements has no row 1467 (L43).
- **Why it was in the live item list.** The live login item list included it (items-695.md §1.1, flags [0,0,0]:
  tradeable, not stackable). The list holds items that tables reference, such as this recipe. Marketplace and event
  items are missing from it, which points away from a store or event route [I].
- **Probably added with the Combat Node.** Text 577205 sits beside the July 2016 Combat Node / Crimson Parlor texts
  (577182-577196). Item 4406 sits between Recipe: Highfall Catapult 4403 / The Crimson Parlor 4404 and Combat Node
  4408 [I].
- **Conclusion so far.** In the final client data, Roll Wraps has no player route: no station, no recipe item, no loot
  table, no store entry, no patch note. This is **not found yet**, not lost. The next paths to try are in §4.

### 3.3 What I built for Roll Wraps

Nothing. The rules forbid inventing an acquisition route. The item stays admin-only (`give 4406 1`), and its roll
abilities 1207-1214 already work (BootAbilities.cs, TASKS 4.15).

## 4. What was built, numbers that are ours, open leads

### 4.1 Built

- **Game/Combat/CreatureAbilities.cs (new).**
  - It holds the ability lists (§1.4) and the role of each ability: Attack, Strong (the special), Heal (Rejuvenate),
    Block ("Secondary Ability", the shield; no name text), Fizzle (Grow-splode Dud).
  - Each ability has a rank. `NameFor(speaks695)` gives 0.589 clients the rank-1 text, because 577260+ do not exist
    in 0.589.
- **Game/Creatures.cs.**
  - `StoryAbilityCount` returns the creature's list length, capped at 6 (577213-577216, 577243, 577244). Creatures
    without a list keep 1.
  - `StoryUseAbility(n)`:
    - an attack becomes the creature's next attack (`Creature.Pending`), as before: turn on the trigger, attack now;
    - Rejuvenate runs TryHeal now;
    - Block makes the creature immune for 3 s (`ShieldUntil`, checked in Hit / Wound / HurtCreature);
    - an exploder goes off, and a Dud does so harmlessly (`Dud` in ThinkFungus).
  - The AI's own attacks use the creature's rank-1 ability. Damage is Kind.Damage × the ability's factor.
  - The hitting ability's name text now goes to every player hit, through `SendCombatEvent(..., abilityName)` at
    +0x40. Spit projectiles carry their ability.
- **Game/Emotes.cs.**
  - `playdead` is added to the 0.695 command list and to /emotelist (0.695 only).
  - `PlayDeadNow`:
    - plays selection 844 for the player and for 0.695 viewers within 80 m, through `PlayAnim(..., only695: true)`.
      It is a stance, so walking ends it with "stand";
    - sends the player's own line and the others' line (0.695 viewers who don't ignore the player);
    - uses the "with [target]" forms when `CurrentTarget` is set and isn't the player.
  - `FromStringId` gained the target field.

### 4.2 Numbers that are ours [I]

- Rank damage: ×1 / ×1.25 / ×1.5.
- Specials: ×1.5.
- Shield block: 3 s.
- A Dud harms nobody (from the name).
- The AI uses rank 1 on its own. That ability kept the 0.589 name, and 0.589 had only that one.

### 4.3 Open leads (add to OPEN-LEADS; I did not edit it)

- **E1: four rank lines with no creature yet.** Each would go on a creature's list once its creature is proven.
  - Vicious Slash 573075 / 577278 / 577279 + Deadly Strike 573076. No creature name directly before it; nearest is
    Scalewind Flamehunter 573068 / Elite Scalewind Ranger 573077.
  - Electro Shot 573095 / 577292 / 577293. Possibly NovaTech Explorer 573092, with Maelstrom 573106 or Electro Charge
    573108 as the special?
  - Fire Slag 573109 / 577318 / 577319 + Molten Slag 573110. Possibly NovaTech Pioneer 573107.
  - Venom Blast 573088 / 577324 / 577325 + Corrupting Grasp 573089. Possibly Elite Scalewind Corruptor 573086.
  - Also missing lists: Elite Ancient Abomination, Elite Enraged Chomper, Elite Slaug Berserker (wiki "?"), and every
    other Scalewind / Highfall / NovaTech / Ebonguard / Stoneskin / Wisp creature without adjacent ability texts (their
    specials: Ooze Bolt, Sludge Storm, Tsunami …).
  - Try next: the forum / livestream notes on the Combat Node (Jul-Sep 2016), wiki edit history, and any creature
    capture that has CasterInfo or ability ids.
- **E2: whether live's AI used ranks 2 / 3 on its own.** Possibilities include a combo chain like the players' Combo1-3
  impact effects (10043 / 12117 / 11844), or picking ranks by difficulty. There is also an unused "Difficulty" setting
  text, 572990 / 573000 "Game Difficulty", which sits inside the Barren block. Try next: forum feedback threads on
  outposts / elites, EditableProperties rows that use 572990.
- **E3: rank damage and the special abilities' real effects.** Lunge = dash, Spike = projectile, Frozen Orb, Tempest
  Jolt, Power Stomp knockdown, Shadow Seal … Ours are plain damage factors. This is server data (L19), and their
  animations could differ: AttackIndex selections for NPC networks (L117).
- **E4: Combat Node settings-window rows naming the abilities (L19).** P6152 says the list is shown. The names are now
  in CreatureAbilities, but the EditSettingsReply rows (free label strings, FUN_142c1eae0) are not built.
- **E5: 0.589's CombatResourceEvent +0x40.** The same code shape was seen (FUN_142fee730), but the disassembly was not
  checked. The client test will settle it.
- **E6: chat lines of the other emote words** (559125 "You wave." / 559127 "[*speaker*] waves." …; social.md "still
  open"). /playdead now shows the pattern (FromStringId with speaker and target), so the same could be done for the
  18 older words once their self / other / with-target text ids are mapped (the 559123-559192 block is not in id order).
- **L26 (Roll Wraps), narrowed further (§3.2).** Try next:
  - the 0.695 client's crafting-station recipe filter, to prove that a learned recipe with no mapping is never listed
    (FUN loading CraftingStationRecipeMappings);
  - a hard-coded 4406 (0x1136) in the decompile near boot or equipment code;
  - Daybreak forum threads of Jul-Oct 2016 that are not in design/forum (the scrape covers 202 threads), and
    livestream recaps;
  - other 0.6xx test builds, if any turn up.

## 5. In-game test steps (0.695 test copy <0.695 test copy>; needs a dev world restart to load the branch)

The dev world restarts to load the new build. Commands go in `dev_commands.txt` next to the server, or through the
admin page queue.

### 5.1 /playdead

1. Log in with the 0.695 client and type `/emotelist`. "playdead" should be listed.
2. Type `/playdead` with nothing targeted:
   - the character falls into the death pose and stops breathing;
   - the chat shows "You play dead.";
   - a second 0.695 character nearby sees the pose and "<name> plays dead.".
3. Target the second character (click it) and type `/playdead` again. The lines should be "You play dead with
   <name>." and, for the other player, "<name> plays dead with <name>.".
4. Walk a step. The character gets up (GetUp clip) and breathes again. `/stand` should also end the pose.
5. With a 0.589 client logged in nearby: the word is not offered to it, and it gets neither the pose packet nor the
   lines.

### 5.2 Rank abilities and combat spam

1. On your own build site, place the creature props (`giveprop 2450` gives Barren Grunt; `giveprop 2458` gives
   Barren Stormmancer) and a Combat Node (`give 4408 1`). Link the node's "Choose the creature this node will
   control..." to the Grunt's spawner.
2. Turn on Play Mode (`playmode on`) and get hit by the Grunt. Check the combat chat (Combat tab, CombatSpam
   "Damage"): it should read "Barren Grunt's Slashing Blade 1 hit YOU for N damage.". Before this branch the
   ability name was empty.
3. In Edit Mode, open the Link window from a Floor Switch (or any trigger) to the Combat Node. With the creature
   linked and spawned, "...perform Ability 1"-"...perform Ability 4" should be offered (four for the Grunt; before
   this branch, only Ability 1).
4. Link the switch to "perform Ability 3". In Play Mode, step on the switch: the Grunt attacks at once, and the spam
   reads "Slashing Blade 3", for about 1.5× damage. "perform Ability 4" reads "Blade Lunge".
5. Barren Protector (`giveprop 2453`): "perform Ability 1" is the shield. Hit it within 3 s: hit effects play, but
   no damage numbers appear and its health bar doesn't move.
6. Barren Lightbringer (`giveprop 2457`): hurt a creature of its side, then "perform Ability 1" (Rejuvenate). The
   heal effect plays and the hurt creature's health rises.
7. Fiery Fungus (story spawner): "perform Ability 2" (Grow-splode Dud). It explodes, but nobody nearby takes damage.
8. With a 0.589 client, get hit by a Grunt that uses Ability 3. The spam should read "Slashing Blade" (the rank-1
   text, which 0.589 has without the number). If the 0.589 spam shows something wrong, lead E5 is answered: gate the
   field by build.

`story` (dev command) lists the site's running props and creature guids. `storylink <src hex> <event> <node hex>
577215` links "perform Ability 3" without the UI.

## 6. Follow-up 2026-10-17: emote lines showed as speech (commit 984c517)

- **What was seen in game.** /playdead's own line read " [name] says: You play dead." (channel 0).
- **Cause [P].**
  - The handler for ChatPacketFromStringId (sub 4) is 0.695 FUN_143364980 case 4. Its reader is FUN_143356860.
  - The first bool, at +0x24, chooses how the line is shown:
    - **clear:** FUN_14336da10 → FUN_142ca7920 on channel 0 (Say). The client shows it as 558679 " [[*player*]] says:
      [*message*]";
    - **set**, with the bubble bool (+0x26) clear: FUN_14336cb40 → FUN_142ca7920 on channel 4 (Emote). The line only
      shows when the local player is within the client's emote range of the speaker.
  - When +0x26 is set, the line is an overhead bubble (FUN_1433ff200), coloured by the u32 at +0x28 (0 white,
    1 red, 2 yellow, 3 green, 4 blue).
  - 0.589's handler FUN_1432b7bc0 has the same branches.
- **Fix.** `Emotes.FromStringId` now sets the +0x24 flag (parameter `emote`, default true). It is used by all three
  callers: /playdead, the /sit relay (572086) and the Story Node's "Announce Emotes in Chat" (StoryTools.AnnounceEmote).
- **Test.** /playdead, /sit seen by a second player, and a Story Node announcing an emote should each show as a plain
  emote line on the Emote channel ("You play dead.", "<name> sits down."), without "says:".
