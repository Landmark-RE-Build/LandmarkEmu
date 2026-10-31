# Phase E3 — text: the 0.695 text-table changes as a map of what the server may still miss

Agent **text**, 2026-10-26. Read-only hunt (brief: trace/0695/agent-phaseE3-brief.md). Server: LandmarkEmu branch
client-0.695, HEAD 8219f7c. Scratch: `<user folder>\AppData\Local\Temp\claude\<workspace>\28d489f2-5945-4c82-ba0b-9796b72f5283\scratchpad\e3-text\`
(new.tsv, changed.tsv, scan.py, server_hits.txt, nohit.txt).

**Method.** All 531 new strings, plus the 140 of the 1,372 changed strings whose change is more than case or
grey/gray (scratch changed.tsv). For each id I counted hits in decimal and hex across: the server (*.cs, json, txt,
md), gameData695 (all client tables), the 0.695 Resources tables, re/decomp_all_695, Resources/Scripts, UI/Default
Lua/XML, re/as3_695 and trace/*.md. The 0.695 exe holds **no** text id as a constant: 0 hits for any candidate in hex
or decimal. So every string reaches the screen one of three ways: a client table row, a CodeStringMappings key the
code names, or a text id / key the server sends. I followed the server-relevant ones: refusals, mails, announcements,
settings, limits and reused ids. I also diffed CodeStringMappings 0.589 → 0.695 (21 new keys).

Result: 444 of the 671 ids are used by the server, 137 more only by client tables, and 90 by nothing (placeholders,
blanks, dev strings, unplaced creature-ability lines, single Parlor unlockers; all accounted for below).

---

## 1. Findings (ranked by player impact)

### F1. Builders with Ultimate Power can set a site's time in 0.695; the server drops it silently — MISMATCH
- **Client [P].** 0.695 FUN_142dd15d0 is the path for Lua Claims.SetClaimTimeHour / SetClaimTimeRate (FUN_1435e2e00).
  TimePanel.as:327-372 passes claim 0, which becomes the current claim (+0x348). The function gained a permission
  check that 0.589 FUN_142d33d30 lacked: `FUN_142d98cc0(claim, player, 0x10) == 0`. 0x10 is the Ultimate Power bit
  (ClaimPermissionAccessLevelMappings row 23: permission 6, value 16; Claims.cs:79 `PermissionBuild = 0x10`).
- When the check passes, the client:
  - writes the new hour and rate into its own claim entry (+0xc8..+0xd4);
  - applies them to the scene at once (FUN_142dc7540);
  - sends ClaimsPacketSetClaimTime 0xF9/0x40.
- **New key [P].** 0.695 CodeStringMappings gains `Claim.SetTime.Error` = 577525 "You cannot change the Time Settings
  on this Build Site." (with an unmapped twin, 577526). No client code, Lua or AS3 names the key, so it can only come
  from the server.
- **Server [P].** Claims.cs:1000-1004 `SetTime` → `OwnedClaim(session, claimId)` (Claims.cs:1557) accepts the owner
  only and returns silently for anyone else. Result:
  - an Ultimate Power builder sees their own time change locally;
  - nobody else sees it;
  - it is lost on the next claim record / re-zone;
  - 577525 is never sent.
- **Already recorded?** L126 (agent-leads2-a.md:304, :314) names 577525 as a server-only key and notes that "SetTime
  for a non-owner silently returns", but it is CLOSED-BY-DESIGN. It did not find the client's 0x10 rule, which is what
  shows live accepted builders [I strong: the client would not gate on 0x10 if only owners could ever succeed].
- **Fix (described).**
  - In SetTime, resolve the claim by id (0 / sentinel → `ClaimAt(session.ZoneId, session.Position)`).
  - Accept it when `PermissionsFor(claim, player) & PermissionBuild` (the owner's 0x11F includes it).
  - Otherwise: `SystemMessages.ShowKey(session, "Claim.SetTime.Error")`, and send `BuildInfoUpdate(claim, InfoTime)` to
    that session alone, to undo the client's local change.
  - Log either way.
- **In-game check:** yes. TestPlayer2 with Ultimate Power on TestPlayer's site moves the time slider; TestPlayer should
  see it. Then TestPlayer2 with Let's Decorate! should see no change sent; force one with a Lua call to see the refusal
  line.

### F2. The 14 combat-title announcements are never sent — GAP
- **Texts [P].** 576045-576058 are the announcement lines, in this order: "[*player*] has become a Monster
  Slayer!", "... Barren Banisher!", "... Scalewind Scourge!", Highfall Horror, NovaTech Nullifier, Ebonguard Eradicator,
  Stoneskin Smasher, Flamekin, Iceguard, Darkener, Brightshield, Assassin, Destroyer, Tempest.
  - 0.695 fixed the typo in 576047 ("Sourge" → "Scourge"), so the devs were maintaining the line.
  - No client table references the block, the same as the capstone block 575528-575546 that the server already sends
    by id.
- **Mapping [P item ids, I order].** The text order matches the title items exactly, skipping Warmaster 3915, which
  has its own capstone text 575532 (items.json nameId 575487... → ids):

  | Title item | Announcement text |
  |---|---|
  | 3908 Monster Slayer | 576045 |
  | 3909 Barren Banisher | 576046 |
  | 3910 Scalewind Scourge | 576047 |
  | 3911 Highfall Horror | 576048 |
  | 3912 NovaTech Nullifier | 576049 |
  | 3913 Ebonguard Eradicator | 576050 |
  | 3914 Stoneskin Smasher | 576051 |
  | 3916 Flamekin | 576052 |
  | 3917 Iceguard | 576053 |
  | 3918 Darkwalker | 576054 ("Darkener" — the name differs, [I]) |
  | 3919 Brightshield | 576055 |
  | 3920 Assassin | 576056 |
  | 3921 Destroyer | 576057 |
  | 3922 Tempest | 576058 |

- **Server [P].**
  - Achievements.Tiers.cs:52-54 awards all 14 titles (e.g. :66 Murderer of Monsters → 3908).
  - `AnnouncedTitles` (Achievements.cs:1184-1188) maps only the capstones 3900-3929.
  - So `Announce` (Achievements.cs:1195, GlobalAnnounce 0x0F, wire OK per audit-wire-c.md:140) never fires for them.
- **Fix (described).** Add the 14 pairs above to `AnnouncedTitles`. Optionally put this behind the same setting as the
  capstone announcements, if one exists; it defaults to live, which [I] announced them.
- **In-game check:** yes. Complete one tier (e.g. a dev command to finish 576) and confirm the System-channel line and
  the on-screen message on a second client.

### F3. The two upkeep letters arrive cut off mid-sentence on 0.695 — MISMATCH (cosmetic)
- **Text [P].** In the 0.695 Locale file, both letters were cut to 255 characters when they were re-cased. They are
  the only two 0.589 strings that got shorter:
  - 562150 (Claim.UpkeepMail.Body, expired), 692 → 255 characters. It now ends "...and you will find thes".
  - 564968 (Claim.UpkeepMailMaxAdjusted.Body, timer extended), 392 → 255 characters. It now ends "...your Build Site
    will remain in the". The sentence "Each additional day you log in will extend this timer further, to a maximum
    of 90 days" is lost.
  - Checked in the raw bytes of `<0.695 install>\Locale\en_us_data.dat` and of gameData695/en_us_data.dat,
    which are the same file.
- **Server [P].** SiteUpkeep.cs:106-109 (Enter) and :112-117 (MailExpired) send `TextManager.GetTextForId(562150 /
  564968)` as the mail body. The dev world reads gameData695 (docker/setup.sh:18 copies the client's own
  en_us_data.dat), so players get the cut bodies.
- **Live [I].** The 0.695 client can also build these letters itself, for mail kinds 1 and 0x1000: FUN_143639190 →
  FUN_143639ba0 / FUN_143639910, which read the same cut keys. So live 0.695 players probably saw them cut as well.
  SiteContents.cs:31 already notes that 563062 (Chaos Cavern letter) was cut in both builds.
- **Fix (described).** In `SiteUpkeep.Text`, when the 0.695 body is cut (length ≥ 255 and it does not end in
  "Build Site Management"), complete it with the 0.589 tail. Keep the two tails in SiteUpkeep from text_589.tsv
  562150 / 564968. Their [*UpkeepMax*] and 90-day wording match `MaxDays = 90`. Default on; mention it in the module
  comment as a data repair.
- **In-game check:** a quick look at an extended-timer letter (owner, next day's first login).

### F4. World name 560924 "Victory" reads "Victory Node" on 0.695 — STALE (docs and setting check)
- **Text [P].** In 0.695, 560924 was reused for the new prop name "Victory Node" (CLIENT-DIFF §2;
  trace/live-server-architecture.md §2: "560924 'Victory Node' in the same block is not a world").
- **Server [P].** The world-name lists still offer it:
  - .env:51
  - .env.example:60
  - docker/worlds/dev.env:10
  - WorldServerConfiguration.cs:166
  - wiki/Settings-Reference.md:128

  Landscapes.cs:71-74 `IsLiveWorldName` (560911-560926) counts it as a live name. A world set to WORLD_NAME_ID=560924
  therefore shows "Victory Node" in the server list and as its in-game name (`WorldNameText`).
- **Fix (described).**
  - Drop 560924 from the five lists.
  - Make `IsLiveWorldName` exclude 560924. The in-game name then falls back to 560912 Liberation.
  - Log a warning at start when WorldNameId is 560924.
- **In-game check:** no.

### F5. The harvest-in-site refusal text was reworded in 0.695 — OK-NOTE (covered by L126)
- 562568 `Harvest.Fail.InClaim` changed from "Not allowed to harvest entities inside a build site." to "You cannot
  harvest on Build Sites." The rewording [I] suggests live still sent it.
- No client code names the key: FUN_142e18c70 maps result codes 2-9 only. So it was a server-sent key.
- The server (Abilities.cs:1077-1085) answers mining inside a site with result 8 → 576183 "You cannot harvest here."
  Harvest nodes never spawn inside sites (Gathering.cs:828, :842).
- L126 already lists this key among the server-only candidates. Optional: also `ShowKey(session,
  "Harvest.Fail.InClaim")`. No re-report.

### F6. The Play Around description now says "Free Camera" — OK-NOTE (no server change)
- 576937 / 576940 change Play Around to "this level allows Luminaries to access the Free Camera and use props".
  576192 changes No Touching from "look around" to "walk around".
- The client decides cameras itself [P]:
  - outside Play Mode, building cameras need permission bit 8 (FUN_142dd5540 → FUN_142db49c0(…, 8));
  - in Play Mode, Play Around visitors get the free camera only through Play Mode Rules bit 0x08, which the server
    already sends (ClaimInfoUpdate 0x17).
- The new message keys `Claim.NoCameraPlayMode` 577088 ("...disabled on this Build Site") and
  `Claim.NoCameraPermission` 572861 are shown by the client (part_0069:30061-30130, part_0113:7932-7966).
- The only stale item is a doc comment: Props.cs:1031-1032 quotes the 0.589 wording.

---

## 2. Checked and fine

| Item | Verdict | Evidence |
|---|---|---|
| Counting Node reused ids 557290 / 565055 / 577092 (were "MAX" / "Mine" / "Decrement Count", now events) | Server uses the 0.695 meaning | StoryTools.cs:207, :761-762 |
| Cog → light texts 572953-572964, 576090-576094; Dice Roll → Probability Filter 573027 / 573036 / 573037 / 573040 / 574270 | Used by id, meaning-neutral | StoryTools.cs |
| Combat Node 577162-577245, ability rank names 577260-577337, "Fireball 1" etc. | Built | StoryTools.cs, CreatureAbilities.cs |
| Vicious Slash / Venom Blast / Electro Shot / Fire Slag rank lines (unplaced) | Recorded | L152, agent-abilities.md:198-203 |
| Ability Count 577166, Enable Combat AI Movement 577217 (rows 38 / 39, no server key) | Recorded | L19 (OPEN-LEADS:245), storytools-695.md:215 |
| Hour Node, Respawn Location, IF Gate, Random Link Filter, Linking Area destroy / heal / parse, Story Node question / expire, Damage % Multiplier, Damages Terrain, Allow Voxel Destruction | Built | StoryTools.cs, PropSettings.cs, VoxelDestruction.cs |
| AddBinding result 4 → 577233 "You must bind this node to a single prop!" | Built | StoryTools.cs:53, rebase-695.md:281 |
| Play Mode Rules texts 577409 (Grapple.AdventureBlocked), 577437 (NotWhileGliding), 577415 (Requirement 4055) | Client-enforced; server sends the mask | Claims.cs:2112-2135 |
| 577411 ClaimDestruction.Fail.NotAllowed | Built, result 9 | VoxelDestruction.cs:47, :208 |
| Site games 577653-577721 (settings, rules, score lines 577669-577671, Victory / Score Node, control point) | Built | InstancedGames*.cs; phaseE2-families / packets |
| Reward chests 577596-577651: keys OwnerCantLoot / ChestLocked / ChestsYouCantLoot, pickup error 0xE → 577600, uses 10 / 20 / 40, owner mail 577637-577639, Thank You Token, goal 1490 / objective 2209, title 4519 | Built | RewardChests.cs:19-73, :380-429; spec-reward-chests.md |
| 577617 / 577618 (Superb / Legendary "X uses") | Unused placeholders; the dump uses 577632 / 577635 | items.json |
| Pets 577507-577546 ("one active pet at a time", unlimited uses) | Built | Pets.cs:17, :163 |
| Fireworks 577365-577506 (pack contents, "50 safe fireworks") | Built | Fireworks.cs:23, :96, :99 |
| Parlor refusals 577364 / 577416 / 577705-577707; Fantasy pack 577355-577361; Halloween hat unlockers 577699-577704 | Built | ItemUses.cs, Unlockers.cs |
| Single Fantasy option unlockers 577339-577363, 577427-577436 | Recorded | items-695.md:133, leads-L39.md:106 |
| Trial key mail 577594 / 577595 ("free 10-day trial") | Built | Membership.cs:49 |
| Titles 577697-577746; reused 577059 Curator → Landmark Explorer | Titles.cs resolves by name at run time, so it follows the 0.695 table | Titles.cs:44-80; cosmetics-695.md |
| playdead emote 577222-577227 | Built | Emotes.cs |
| Roll Wraps 577205-577211 | Built | BootAbilities.cs |
| Build Site Expansion 571247 ("up to a maximum of eight", flag dropped from the bundle) | Matches | Claims.cs:727-736; L86 |
| Flag text 558814 "no longer required" | No requirement left (ResultNoFlag unused); right-click still starts placement | InteractionHandler.cs:437 |
| Iron 558834 "underground or on the surface" | Enabled surface vein row 247 (surface density 5) exists | ResourceVeins.txt; L88 |
| Upkeep numbers (90-day maximum, 7 start days) | Unchanged | SiteUpkeep.cs:29 |
| 575529 "has completed 'You Blockhead'" | Still mapped to 3901 | Achievements.cs:1186 |
| Client-only labels and settings: 577199 / 577220 / 577630 brush options, 577240 / 577241 / 577636 / 577631 (ini AllowClaimWideLOD, part_0106:17681), 577535 Paste.InProgress (client paste thread), 577716 Game Finder label, 565183 / 565184 / 577541 / 577542 strafing, 557209 F → Y tip, Mesh Shapes 577553-577586 | No server role | — |
| Blank / dev / placeholder ids 577069, 577083, 577212, 577340, 577347, 577348, 577360, 577537, 577592, 577615, 577208 Mutton Chops, 577203 Open Help Window | No server role | — |
| Name-only renames: Lilies, Sergeant, Drowner's, Russet, Badger / Beaver sculptures, Snow and Ice, Curse of the Betrayer, Campsite, Reclaimed, exquisite, Game Table | Data / names only, nothing to check | — |
| CodeStringMappings 0.589 → 0.695: 21 new keys | All accounted for: 20 above, Claim.SetTime.Error → F1 | — |

---

## 3. New leads

- **E3-T1 (F2): did live announce the combat tiers?**
  - Need: proof that live sent GlobalAnnounce with 576045-576058.
  - Searched: design/forum (no "has become a Monster Slayer / Barren Banisher..." quote); the client tables (no
    reference).
  - Try next: forum_raw and wiki talk pages for "has become a"; Reddit / YouTube transcripts of 2016 combat goal
    grinds.
- **E3-T2 (F3): the Feb 2017 build's Locale file.**
  - Need: whether the final en_us_data.dat (CRC 10E110DF, docs/launchpad.md:108) restored 562150 / 564968 in full.
  - Searched: <0.699 copy> holds only UI\, no Locale.
  - Try next: a genuine final-build copy when one turns up; diff its text table with textdiff.py.
- **E3-T3 (F1): what the time panel shows a Let's Decorate! builder.**
  - Need: whether the 0.695 TimePanel greys out for builders without 0x10, or whether its Lua call simply does
    nothing.
  - Searched: TimePanel.as (calls only) and FUN_142dd15d0.
  - Try next: the BuildModeTimeWidget data source (GetData "SceneTime" provider) for an "enabled" field; the in-game
    check under F1.

---

**Summary.** The new and reworded 0.695 strings are mostly already built into the server: story tools, site games,
reward chests, pets, fireworks, the Parlor, titles and the new Play Mode Rules texts. Every new CodeStringMappings key
but one has a server or client owner. Four things are still missing or wrong:
- **F1:** the 0.695 client's new rule that Ultimate Power builders may set a site's time. The server is owner-only,
  drops their change silently, and never sends the new refusal `Claim.SetTime.Error`.
- **F2:** the 14 combat-title "has become a ..." announcements. The server awards the titles but never announces
  them.
- **F3:** the two upkeep letters, which the 0.695 text table cut to 255 characters. The server mails them that way.
- **F4:** text 560924, still offered as the world name "Victory", which 0.695 shows as "Victory Node".

The harvest refusal (F5) and the camera / permission wording (F6) need no change beyond what L126 already records.
