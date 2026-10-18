# Outposts and Underground Adventure (TASKS E.15; features audit #60 / #65; OPEN-LEADS L102, L130, L131)

Agent report, 2026-10-17. Branch `e-outposts` (worktree `<workspace>\wt-outposts`), 3 commits on top of `bd03023`:

| Commit | What |
|---|---|
| `9a5894e` | Outposts: walk-on banner as client-only site records (#60, L102) |
| `d7200ca` | Warp network (Local Travel) on every landscape with Chaos Caverns (L130) |
| `77ae7f6` | Docs: docs/gameplay.md sections for both |

`dotnet build LandmarkEmulator.WorldServer/LandmarkEmulator.WorldServer.csproj -v q` succeeds. Nothing was run: no servers,
no client, no DB writes. Shared notes (TASKS, OPEN-LEADS, FEATURES, MODLOG, TESTING-NEEDED) were not touched. The
proposed text for them is in §7. The crystal / lockout code in Outposts.cs (UseCrystal, CompleteChannels, LockedOut)
was not touched (e-props2 is working there). The Outposts.cs edits are only `Stream` (the chat line) and `Forget` (one line).

---

## 1. The outpost banner: evidence

### 1.1 What live showed
- Wiki "Outposts", "Who Made This?": "On nearing the edge of any Outpost, the creator's name and the date it was made
  appears", "The name of the Outpost appears in white text, along with the date it was created", "If you miss the name
  of the Outpost and want to see it again, simply back out and wait a few moments. When you approach again, it will
  appear on the screen."
  - "Edge" plus "back out and approach again" is how the build-site walk-on window behaves. It shows when you enter a
    site's box, and it animates again after you leave and come back (AS3 `getCanAnimate`).
- Wiki "Outposts A-Z (List)" lists 30 outposts as "<title> by <creator> on <M-D-YYYY>". Examples: "Abandoned Bunker by
  Hadder on 6-20-2016" and "Shrine of the Lotus created by Butchywuff on 4-6-2016". Every one of the 30 matches one of
  the 35 0.695 `ruin_*.otd` files (table in §2.2).
- The Landmarks of Landmark sites were "flagged as a landmark claim (with the fancy blue header banner)" (Domino, forum
  57799 #6; LandmarksOfLandmark.cs). That is the admin / contest panel. The outposts' banner is described as **white
  text**, so it is the plain site panel.
- 2015 dev note P3321: "you will not be allowed to claim land with underground ruins in it". Ruins were already treated
  as protected areas.

### 1.2 The client's walk-on window (0.695)
- AS3 `re/as3_695/ClaimWalkOnTextWindow/.../WalkOnTextWindowDocumentClass.as` reads two data sources:
  - `GameData.ClaimDialogs.WalkOnText`: ClaimName, WelcomeMessage, OwnerGuid, AdminTemplateGuid, CanAnimate, CanVote,
    InstancedGame*, AdventureStatus, ContestState / ContestName, and others.
  - `ClaimDialogs.WinnerWalkOnText`: WinnerClaimName, WinnerCompetitionName, WinnerCompetitionDate,
    WinnerCompetitionDescription, and others.
- Panel states (`show()`):
  - 3 CONTEST when `AdminTemplateGuid != "0"`;
  - else 4 / 5 for a site game;
  - else 2 DEFAULT_OWNER for the owner;
  - else 1 DEFAULT.
- Panel contents:
  - **DefaultContent**: ClaimNameLabel = ClaimName and WelcomeMessageLabel = WelcomeMessage, plus ContestInfoLabel,
    Likes, Recommend, Feedback and Report.
  - **ContestWinnerContent**: name, description and CompetitionInfoLabel only.
  - **Neither panel has a date field.** `WinnerCompetitionDate` is fetched (`getWinnerClaimData`) but never displayed.
    The same is true in the 0.589 AS3, which differs only in the site-game buttons. So the date live showed must have
    been part of the text: the name or the welcome message.
- `FUN_142dc7540` (0.695) fills WalkOnText from the claim entry:
  - ClaimName = entry +0x10, WelcomeMessage = entry +0x28;
  - TimeCanVote, OwnerGuid, OwnerName, AdminTemplateGuid (+0x138), DisplayShowAreasCheckbox;
  - the owner / visitor time of day (+0xc8..+0xd4);
  - Rating = +0x360, NumVotes = +0x364, NumHistoricVotes = +0x36c.
- Entering a site (part_0069 ~20300-20460, the function calling `FUN_142dc7540(param_1,param_2,bVar12)`):
  1. RatingPacketCanVoteRequest (0xA7 / 0x18, "Claims;Contests", the site id). The server already answers it: CanVote
     false for a site that isn't ours or in the gallery.
  2. The site's time of day (-1 / 1 = the world clock), music (+0xf4), then Lua `GameEvents:OnEnterClaim` and
     `HudStateManager:OnShowBuildModeCamera`.
  3. ClaimsPacketPlayerEnteredClaim (0xF9 / 0x63). Claims.Entered ignores it when the server has no such site.
  - Leaving (`FUN_142dc93c0`) fires OnExitClaim and clears the window (`FUN_142dc7540(param_1,0,0)`).

### 1.3 Why not the admin (contest) banner
- `FUN_142d98cc0(claim entry, player, mask)` is the client's per-site permission answer (0 = allowed). When entry
  **+0x138 (admin template) is non-zero** and the player's **+0x287** flag is clear, it returns 1 (refused) **for any
  mask**.
  - +0x287 is the SendSelf admin-bypass flag (trace/systems/claims.md:24, :115; set by `FUN_142fd35a0`).
  - `FUN_142db4b50` (the box / point check) returns at once for a +0x287 player. Otherwise any overlapping site that
    refuses → "Claims.Error.NoAccess".
- The prop interaction pass `FUN_1431b47c0` calls `FUN_142fb1a70(prop)`. That function checks mask **2 (use)** at the
  prop's position through `FUN_142db4b50`, unless the entity's +0x1f8 equals the "none" value `DAT_145455ea8`.
  - Whether the server's lightweight props carry that "none" value is not settled.
  - So an admin-template site around a ruin **could switch off the Lost Crystal and the Lost Chest**.
  - The plain banner with Default Access Play Around (0x003, use included) has no such risk.
- Masks seen at the call sites: 2 = use; 8 = decorate / props (part_0079 37636, 37897, 38024, 41014; part_0080 1536);
  0x10 = build / paste (part_0087 32407, 35049; part_0069 19962, 27231); 0x100 = copy (part_0087 32231, part_0116
  41280, part_0119 21855, part_0121 3800); 0x11F = full.
  - No client claim check was found for mining or digging. `Claims.CanLocalPlayerAccess` (`FUN_1435e5490`) is a Lua
    binding that no shipped script calls (re/out/lua_all.txt).
  - So Play Around makes the client refuse building, props, decorating and copying inside an outpost by itself, while
    digging stays a server rule.

## 2. What was built: banner (`Game/OutpostSites.cs`, new)

### 2.1 Mechanism
- Each outpost streamed to a player (Outposts.Stream, ViewRange 160 m, dropped beyond 220 m) is also sent as a **site
  record** in a ClaimsBase AOIUpdate (sub 4, `full = false`). Records the player no longer streams are removed.
  - All of one tick's adds and removes go in one packet: `OutpostSites.Sync`, called at the end of `Outposts.Stream`
    (Outposts.cs:701).
- Record fields:
  - id `0x5453_0000_0000_0000 | outpost index`; owner `0x5453_0000_0000_0000`, a guid no character has.
  - Box: the ruin's slot. x / z = corner + 1 border voxel and the template's size (`VoxelTemplate.SizeOf`, cached per
    ruin). y runs from **20 m under the slot's base** [I] to the template's top. Slots sit 8 m above the cavern's bottom,
    so a player walking up on the cave floor meets the banner at the ruin's edge.
  - Default Access 0x003; no admin template; time -1 / 1.
- **Client only**: the record never enters the server's `Claims` list. Claims.ClaimAt / MayEdit / Allows / BoxesIn /
  Search / IsFree are unchanged, so these stay as they are:
  - digging and dynamite in a ruin (open-world rule; the stamped cells heal back);
  - creature spawning (`Creatures` refuses spawns where `MayEdit(0, …)` fails inside a site, which would have broken
    the garrisons);
  - "Build Site Tourist", site search, the server's placement buffer, graveyards and pets.
  - `Claims.SendDetachedSites` (Claims.cs:1755) writes these records with the normal `WriteRecord`.
- A full claims update (login, every re-zone: `Claims.SendAll`) replaces the client's whole list, so
  `OutpostSites.Reset` (Claims.cs:175) makes the next stream tick send them again. `Outposts.Forget` clears the set.
- Client buffer: the 285 m root-site buffer is 3-D (|dy| < 90 + 285 m, Claims.IsFree). The caverns lie at local y
  ≥ −650 … −1000 and the surface is around +1000, so no surface placement is refused.
- Text:
  - ClaimName = the title ("Abandoned Bunker");
  - WelcomeMessage = "Created by Hadder on 6-20-2016" [I wording], or "Created by <creator>" when no date is found.
  - The "Outpost: <name>" chat line is gone. The Outpost first-time help still fires at 60 m.
- Bandwidth: one record of a few hundred bytes per outpost, only while it is streamed. That is at most a handful per
  player, coalesced per tick.

### 2.2 Credits table (OutpostSites.credits)

Sources: W = wiki list; T = client design texts (re/out/text_695.tsv) for spellings and missing titles.

| Ruin file | Title | Creator | Date | Source / note |
|---|---|---|---|---|
| adjorr_frozenteleporter | Frozen Teleporter | Adjorr | 4-6-2016 | W; T 567330 / 567331 |
| ahnzul_ancestralshrine | Ancestral Shrine | Ahnzul | — | [I] title from the file name; date not found yet |
| ahnzul_market | Longshade Trading Post | Ahnzul | 6-24-2016 | [I] W's other Ahnzul outpost; T 576278 |
| ahnzul_seatofpower | Seat of Power | Ahnzul | 6-22-2016 | W |
| ansgar_darkarrowskyway | Dark Arrow Skyway | Ansgar | — | T 576273 |
| ashlyn_coliseumraid | Raid at the Coliseum | Ashlyn | — | T 576293 / 576294 |
| basikle_handofbrog | Hand of Brog | Basikle | 6-20-2016 | W (T 567539) |
| butchywuff_shrineofthelotus | Shrine of the Lotus | Butchywuff | 4-6-2016 | W; T 567464 / 567466 |
| ceijay_fallenpridekeep | Fallenpride Keep | Ceijay | 6-20-2016 | W; T 567553 |
| dhara_ironorder | The Iron Order Hall | D'hara and FryCaptain | 6-22-2016 | W (T 568485 "Dhara and FryCaptain") |
| diamondfox_ebonglowtower | Ebonglow Tower | Diamondfox | 6-22-2016 | W |
| durwin_bridgetonowhere | Bridge to Nowhere | Durwin | 6-22-2016 | W; T 567057 / 567063 |
| endru_genesisstone | The Genesis Stone | Endru | 6-22-2016 | W; T 567391 |
| fazz_ancientorgan | Ancient Rock Organ | Fazz | 6-20-2016 | W; T 567582 / 567583 |
| felgon_tomb | Tomb of the Unknown King | Felgon | 6-20-2016 | W |
| ginsan_houseofelders | House of the Elders | Ginsan | 4-25-2016 | W (T 576269 "House of Elders") |
| ginsan_shiningshrine | Shining Shrine | Ginsan | — | T 567060 / 567061 |
| hadder_bunker | Abandoned Bunker | Hadder | 6-20-2016 | W; T 567394 / 567395 |
| hanna_pausaniaseye | Pausania's Eye | Hanna | 6-22-2016 | T 567397 (W typo "Pausainia's") |
| khalimzul_curseofthebetrayer | Curse of the Betrayer | Khalimzul | 6-22-2016 | W; T 567062 / 567064 |
| khalimzul_iceboundchalice | The Icebound Chalice | Khalimzul | 6-22-2016 | T 567065 (W typo "Khalimazul") |
| koda_greathall | The Great Hall | Koda | 4-6-2016 | W; T 567667 |
| maxximus_trappedsoul | Trapped Soul | Maxximus | — | T 567066 / 567067; forum 57065 (May 2016, Liberation / Windy Barrens) |
| osmotar_grimstonehall | Grimstone Hall | Osmotar | 6-22-2016 | W; T 567534 / 567535 |
| poem_crag | Crag | Poem | 6-22-2016 | W (T 567546 "The Crag") |
| poem_lifesong | The Lifesong | Poem | 6-22-2016 | W; T 567548 |
| portavar_refinery | Abandoned Refinery | Portavar | 6-22-2016 | T 567664 (W "Portavaron") |
| raenin_lostmission | Lost Mission | Raenin | 4-6-2016 | W; T 567565 / 567566 |
| schag_voxelmancerend | Voxelmancer's End | Schag | 6-22-2016 | W; T 567588 / 567589 |
| shattari_hunterslodge | Horned Owl Hunting Lodge | Shattari | 4-28-2016 | W; T 576270 |
| shattari_magrail | Forgotten Magrail | Shattari | 6-22-2016 | W; T 567557 |
| tenma_chompnstompcathedral | Chomp and Stomp Cathedral | Tenma | 6-20-2016 | W |
| thrift_takingacloserlook | Taking a Closer Look | Thrift | 6-22-2016 | W; T 567572 / 567573 |
| woodstein_clockworkcalamity | Clockwork Calamity | Woodstein | 6-1-2016 | W; T 567468 / 567469 |
| xantheos_abandonedraildepot | Abandoned Rail Depot | Xantheos | 6-2-2016 | W; T 567594 / 567595 |

The client's title / creator text pairs (567040-567728, 576268-576294) belong to the cavern-ruin designs. No shipped
table references them (searched gameData695 and re/out). They look like server-side data, perhaps live's ruin list.

## 3. What was built: Local Travel on every cave landscape (L130, `Game/WarpNetwork.cs`)

- Evidence (L130 "found so far", re-checked):
  - The client files WarpNetworkZoneNetwork (0x113 sub 2) per (world, landscape) (`FUN_143454ec0`).
  - Every island's Underground `.cpa` has the same 81 Graveyard01 pieces (tile 111) as 406, the pieces the home
    crystals are made from.
  - Every landscape has its Spire (TASKS 6.49), and the server now gives 23 more landscapes caverns and outposts (F2).
- Built [I]:
  - Each landscape in `Caves.Landscapes` (setting CavernLandscapes) gets its own network: its Spire's hub
    (`Spire.HubGuidOn` / `ArrivalOn`) and its 81 crystals (prop 1881).
  - The crystals are read on a background task the first time a player is on that landscape (`Prepare`). Their cave
    floors need that landscape's cave index, which is built under CavePieces' lock.
  - The world tick then pushes the network and the crystal props to the players there (`WarpNetwork.Update`,
    registered in HostedService).
  - A landscape without caverns (Pioneer, events) gets its hub only. Before, it got an empty network.
- Ids:
  - The home continent's crystal ids are unchanged (`0x5750… | 1000 + n`), so the dev `terminal c<n>` still works there.
  - Another landscape's are `0x5750… | zone << 16 | 1000 + n` (`zoneOfNode` lookup).
- Fix: the Spire's Portal Crystal opened **the home hub** on every landscape (`InteractionHandler`: `HubNode`). That
  node isn't in another landscape's network. It now opens `HubNodeOn(session.ZoneId)`.
- `Warp` recognises the hub by type, not by the home guid.
- `PlayerManager` sends the crystals on every landscape: the `OnHomeContinent` guards are removed (login and re-zone).
- Dev `terminal hub | c<n>` uses the player's own landscape.
- Still open (L130): whether live listed underground crystals off the home landscape at all. That needs the runtime
  check below.

## 4. Not built, with reasons

### 4.1 Outposts rotating between caverns (audit F8)
- The audit's "change regularly" comes from the client page `HelpPages/ChaosCavern_2Outpost_FTE.html`. In context it
  reads: "Outposts exist in the open world and change regularly. You can team up with any Luminaries you meet along the
  way. If an Outpost is empty, another Luminary might've recently fought through it. Enemies will return to the
  Outpost soon."
  - That is about the shared, changing state of an outpost (killed garrisons that respawn), not a schedule of
    relocations. Respawns are built (6.23).
- Other sources:
  - **P4806** (26 Apr 2016): "They won't always be in the same place, though". This is the Elite and the hidden chest.
    The chest spot is built (6.24 / P5926); the Elite takes a random BOSS spot (GarrisonFor).
  - **P4807**: "The worlds have shifted", a one-time reshuffle with that update.
  - **P5337** (11 May 2016): "… along with the Outpost name and what your exact actions were if it changed while you
    were playing there". These are props that duplicated or shifted (P5333-P5335), a bug.
  - **P4808**: "every Chaos Cavern is now guaranteed to have an Outpost inside of it at all times". This is built
    (FillCaverns).
  - The Environment Server admin page (0.695 part_0091 ~33767, "Ruin Layer Info"; L76) keeps, per ruin layer, Desired
    / Active / Failed / **Last ID** counters.
    - That suggests ruins were placed by jobs up to a desired count, with ids that go up. That fits a maintained
      population, and possibly replacements.
    - It gives no period and no rule for when a ruin is replaced.
- Decision: not built. No source gives a period, a trigger or a rule. A relocation means un-stamping a ruin, which needs
  a restart and a backup of server-data. Open lead below.

### 4.2 "Other worlds" (L131)
- The sweep's finding stands. In the game's own wording, "other worlds" is the Travel window's Other Worlds button
  (tooltip "Teleport to any world on any server."). The Luminary goal "Travel to 'other worlds' and visit a landscape
  that doesn't have 'Pioneer' in the name" (wiki "Becoming a Luminary") uses the phrase for landscapes.
- P5734's "other worlds and biomes" is therefore met by the existing F2 trips (any placed outpost on any landscape, any
  cave biome).
- NextUndergroundReply carries only the offer token (u64). No world handover is implied, and none was built.
  L131 → resolved [I].

## 5. Open leads (nothing is written off)

1. **L102 follow-up: banner verified?**
   - Need: the client actually shows the plain walk-on panel for a record sent mid-stream, and keeps showing it after
     re-entry.
   - The Landmarks of Landmark contest banner has never been checked by hand either (TESTING-NEEDED 1505).
   - Try: the test steps in §6.
2. **Outpost banner wording and the five missing dates.**
   - The exact two lines live showed are not found yet. The wiki screenshot "Outost-name-example-lg.jpg" is not in
     design/wiki (images weren't scraped).
   - Dates not found yet for Ancestral Shrine, Dark Arrow Skyway, Raid at the Coliseum, Shining Shrine and Trapped Soul.
     The title of ruin_ahnzul_ancestralshrine is not found yet either.
   - Searched: design/wiki pages, design/forum/all (202 threads), re/out/text_695.tsv, gameData695.
   - Try next: the wiki image (archive.org fandom file page); Reddit / forum screenshots of outpost banners; the
     owner of the 567xxx / 576xxx title-creator text pairs (a server table?).
3. **Admin-template sites and server props.**
   - Need: does `FUN_142fb1a70` skip the use check for the server's lightweight props (entity +0x1f8 == the "none"
     value `DAT_145455ea8`)?
   - This decides whether the Landmarks of Landmark site (admin template) blocks any usable prop inside it.
   - It would also allow outposts to use the contest panel if that turns out to be live's.
   - Try: Frida on `FUN_142fb1a70` / `FUN_1431b47c0` near the Lost Crystal on the test copy, or trace who writes
     entity +0x1f8 for AddLightweightProp.
4. **Do site records from the AOI list show on the world map?**
   - The map's site list is SendAllClaimBasicInfo (sub 1). Outpost records are not sent there, so they should not
     show.
   - Check in game: open the map in a cavern near an outpost.
5. **F8: how and how often live moved outposts.**
   - Need: the Environment Server's ruin-layer rule (Desired / Active / Failed / Last ID per layer, L76) and any
     schedule.
   - Searched: patch notes (P4803-P4808, P5333-P5337, P5925-P5927), forum 57065, client help pages.
   - Try next: dev streams or posts after 26 Apr 2016 about outposts changing; L47 (.cpa CavernInstanceData /
     LayerInfo).
6. **L130 runtime.**
   - Need: does the 0.695 Local Travel map show another landscape's crystals and warp to them?
   - Did live have them? Not found yet (wiki Spire page is beta-era).
7. **L129** (unchanged): live's pick of landscape and cavern for trips.

## 6. Test steps (0.695 test copy <0.695 test copy> only)

A server restart is needed for this branch. If it is merged and deployed to the dev world, say that the dev world
restarts.

Dev commands go into `dev_commands.txt` next to the world server (or the admin page), one per line. The `@Name`
prefix targets one player.

**Banner (home continent, no setting needed)**
1. `outpost 0` takes you to outpost 0 (the log names its ruin).
   - Within about a second the banner should drop down: the outpost's title in white (e.g. "Abandoned Bunker") and
     "Created by Hadder on 6-20-2016".
   - Expect the plain panel: Report / Feedback / a red Like button. Not the blue contest panel.
2. Walk out of the ruin's footprint, wait a few seconds, walk back in. The banner should show again.
   - The "Outpost: <name>" chat line no longer appears.
   - The Outpost first-time help still opens the first time (60 m).
3. Inside the ruin:
   - dig a wall with the pick, or use dynamite: it should still work;
   - try Build Mode / placing a prop: refused by the client;
   - kill the Elite (or `outpostwake 0`), then use the Lost Crystal: it must still channel (the use check);
   - open the Lost Chest the same way (`outpostuse 0 chest` also works).
4. Re-zone (Travel → An Underground Adventure, or `outpost 40`). The new outpost's banner should work too. Server log:
   no "reports entering site …, but stands elsewhere" spam beyond one Debug line per entry.
5. Open the world map in the cavern: no outpost shows as a build site (open lead 4).

**Local Travel on another landscape** (test stack or dev with `CAVERN_LANDSCAPES=all`)
1. `cavelands` lists the landscapes. Travel to one, e.g. Arid Thicket (391), via the Travel window.
   - The log should say "Warp network: 81 underground portal crystals on landscape 391" within seconds.
2. At that landscape's Spire, use the Portal Crystal and choose Local Travel.
   - The map should show this Spire's hub, and the underground layer(s) with crystals.
   - Before this branch, the node was the home hub and the network was empty.
3. Pick a crystal and confirm: 2 Lumens, arrival on the cave floor at that crystal.
   - Use the crystal there: the Local Travel map opens (`terminal c0 1` does the same from anywhere on that
     landscape; `terminal hub` opens the Spire menu).
4. Back on 406: `terminal c0 1` and the Spire's Local Travel behave as before (same ids).
5. Outposts on that landscape: `cavelands 391` queues their placement, then `outpost 128` (= 128 × (391 − 390) + 0)
   once "ready". The banner check from above applies there too.

## 7. Proposed notes for the shared files (not edited by this agent)

- **TASKS E.15 / 3.14:** banner built (OutpostSites, client-only site records, untested); L130 built (untested); L131
  resolved as landscapes; F8 left open (no period or rule in any source).
- **OPEN-LEADS:**
  - L102: built (`9a5894e`), with the follow-ups in §5.1-§5.4.
  - L130: built [I], runtime check open.
  - L131: resolved [I] (sweep + §4.2).
  - New lead: F8 rotation rule (§5.5).
- **features audit #60** → BUILT (untested); **#65**: L130 built, L131 resolved, F8 open.
- **TESTING-NEEDED:** §6.
