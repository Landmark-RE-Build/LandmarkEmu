# Open leads, second pass — group D2 (server-side behaviour and systems), 2026-10-17

Read-only pass, deeper than this morning's sweep (agent-leads-sweep.md). Leads: L81, L82, L83, L97, L99, L104, L107,
L113, L122, L123, L124, L125, L127, L128, L132, L133, L135, L137, L140, L157, L158. OPEN-LEADS.md was not edited and no
code was changed.

**Sources used.** re/decomp_all_695 and re/decomp_all, plus fn.sh, pkt.py and refscan.py. I read
<0.695 test copy>/Landmark64.exe and its Resources / CommonData / UI as bytes, and nothing was run. I also used gameData695
and gameData (0.589), gameData695/items.json (the live 2016 login list), ActorCompositeEffectDefinitions.xml,
design/forum/all, design/wiki, trace/0695/patchnotes.tsv, re/out (rtti_695, luabind_695, text_695, strings,
fileindex_*), re/as3 and re/as3_695, and re/tools/verdicts.tsv.

**Marks.** [P] means proven, with the address, file or row given. [I] means inferred, with the reason given.

---

## Summary

| Lead | Verdict | One line |
|---|---|---|
| **L99** | **FOUND [I strong]** | Live's Pioneer Landscapes are most likely zones **464-468**, on continents **440-444**, not 417-421. Continents 440-444 ship Underground cave files of ~78 000 pieces each. |
| **L158** | **FOUND (items) [P]** | Item tooltips read their keyword list from the **4th int list of the ItemTooltipReply 0x2D/0x12 item record**. Our server writes that list empty. |
| **L157** | ADVANCED [P data / I mapping] | I matched each weapon to its projectile rows by **bone source + effect-id family**: cannon / raygun use R_UpWeaponFlex rows, Six-Shooters use the R/L Small rows, Night Herald's Mercy uses row 119, the wand uses 112, the Blazing Bow uses 111. |
| **L128** | ADVANCED [P] | Page 33's columns are **Pending** (+0x20+4i) then **Active** (+0x6C+4i). Total Jobs is two numbers, +0xB8 and +0xBC, both current counts. Our JobCounters names have the two columns swapped. |
| **L140** | ADVANCED / part FOUND [P] | The six "parked" **Group** packets can be sent from retail through hidden **/party** sub-commands. **SOEmote** (FacialAnim) cannot run: LiveDriverSDK.dll is in no install or file list. |
| **L83** | FOUND (negative) [P + I] | The retail client has no reader for the Storybricks reply, and the design sources show Storybricks was dropped from Landmark. Nothing to answer. |
| L97 | ADVANCED [P] | The client's hostility rule is confirmed. IsGameFFA (setting 7) defaults to 1 exactly for rule sets 2 and 7, which matches what we built. |
| L104 | ADVANCED [P] | The only waypoints the server holds are the zone "HUB / WARP" waypoints (0x32/0x14 ZoneWaypoint, 0x13 Cooldown). /goto waypoint has no player data to use. |
| L107 | ADVANCED [P] | Vehicles.txt has 0 rows, so vehicles can't be used. PlacedEffect Create plays an effect and prints a debug line; its Destroy does nothing. The SetModel path exists. |
| L113 | ADVANCED [P forum] | A Dec 2015 player post shows server-placed "environmental props" (huge boulders) inside claims that could not be deleted. |
| L122 | ADVANCED (built) | The name-text fix is in (Groups.cs:544-547). The rest is unchanged. |
| L123 | ADVANCED [P layout / I reply] | The Recommendations request body is {u32 100, u32 5}. It has no reply class, so the likely answer is StoreBundleSearchResults. |
| L124 | ADVANCED [P] | (b) The Contest window closes at once on submit, with no wait and no timeout. (c) Creator **character** names are shown (hotfix 1 Apr 2016). |
| L125 | ADVANCED [P ids] | In the VoxelConstruction id table, the client requests are 1-9 and the zone↔Env twins start at 0xF. ApplyDiff has no twin, and 0x12 ZoneRequestFailedCellList is the Env→zone reply. Both fit forwarding the request unchanged. |
| L127 | ADVANCED [P] | The page prints a ruin layer only by its **list index**. The record has no layer id or landscape field. |
| L132 | ADVANCED [P RTTI + forum] | There is no DebugPathQueryResponse class. Players' 2016 reports describe a voxel-aware navmesh that was rebuilt on edits and built lazily. |
| L133 | ADVANCED [forum] | On live, creatures pathed around player walls and doorways and broke when a floor material changed, so edits did reach the navmesh. |
| L135 | UNCHANGED (+forum) | Forum: the pad turns with the dock rotation as it moves. The rider test is still needed. |
| L137 | UNCHANGED | Needs archive.org. Target hosts taken from the exe are listed below. |
| L81 / L82 | UNCHANGED | Nothing new beyond the client block (FUN_1434e65d0). |

---

## L99 — Pioneer caverns: FOUND [I strong]. The Pioneer lands are zones 464-468, not 417-421

**Evidence.**
1. **StaticZoneData** (0.589 and 0.695 identical) has two sets of zones:
   - rows 417-421: NAME_ID 568878-568882 = "Jungle_Zone", "Desert_Zone", "Tundra_Zone", "OldGrowth_Zone",
     "DeciduousForest_Zone" (internal names);
   - rows **464-468** "Pioneer Island Jungle464" … "Pioneer Island Deciduous468", continents **440-444**, NAME_ID
     **573314-573318** = "Pioneer Jungle", "Pioneer Tundra", "Pioneer Desert", "Pioneer Old Growth",
     "Pioneer Deciduous" [P].
   - Row 463 / continent 439 is "Geometry_PioneerIsland_459", text 573250 "Pioneer Island BROKEN": an abandoned first try.
   - Our server keeps 417-421 and has to override their names with 573314-8 (Landscapes.cs:174-181 `pioneerNames`). That
     override is a sign it picked the wrong set.
2. **Caves [P].** `CommonData/Voxel/caves/ContinentCavePlacements[440..444]_Surface.cpa` (68 bytes = empty) and
   `_Underground.cpa` (15.6-15.8 MB) exist in both installs (fileindex_589 / _695: 10 files).
   - Parsed with the Caves.cs layout: version 14, **77 990 pieces** for 440 and 77 971 for 441, 12 piece types (104 open
     cavern ×23 168), y from 204.8 m to 2745.6 m global. Island 391 has 3 268 pieces.
   - This matches the 2016 notes. P4120 / P4299: no cavern networks or Chaos Caverns. P4300: "isolated caverns in the
     underground … reserved solely for auto-placing Build Sites". The empty Surface file and the many caverns fit both.
   - The sweep missed these files because it looked for 417-421.
3. **Layers [P].** VoxelContinentToLayerMapping gives 440-444 the layers 489-494 "44x_Layer1": CAVE_DENSITY_PCT **50**,
   CAVE_DESIRED_RANDOM_ENCOUNTERS **0** (no creatures). Continent 417 has only 495 "TEST_1Cavern" (density 0.0001).
4. **Flat land [P].** Continent_Shape_440-444.bmp are byte-identical (md5 1f13a9c2…) and dominated by a few repeated
   values, i.e. a plateau.
   - This matches P4031 "the flat terrain on the new Pioneer Landscapes".
   - Shape_417-421 are each different and noisy.
   - BiomeIds_440 = biomes 49 TropicalSurface, 54 OceanSurface, **71 "TropicalSurface Plain"**, **74 "TropicalSurface
     Path"**; BiomeIds_441 = 51 Tundra, 54, 80 TundraSurfacePlain, 81 TundraSurfacePath. These are designed plains with
     paths and a coast. BiomeIds_417 is one biome (49) over 2000×2000.
5. Only HeightLayerIds_**440**.bmp exists for this set. Because the five shapes are identical, one height-layer bitmap
   serves all five [I]. Our zone details name the bitmaps themselves (Landscapes.cs:340), so this works.

**Server change (needs the user's decision and a client test first).**
- Make zones 464-468 (continents 440-444) the Pioneer lands:
  - name text = StaticZoneData NAME_ID (no override);
  - bitmaps BiomeIds_44x and Shape_44x;
  - HeightLayerIds_440 for all five [I].
- Then Pioneer.FindCavern (step 2 of P4116 / P4361) gets caverns from `[44x]_Underground.cpa` with no other change.
- Landscapes.Load today drops ids > 421 and requires HeightLayerIds_<continent>.bmp. Both need relaxing for 464-468.
- Decide what happens to 417-421: unlisted, or kept as extra landscapes.
- Existing Pioneer sites on 417-421 would need moving. Back up first.

**Next steps.**
- Runtime: dev-travel to zone 464 on the 0.695 test copy and look for the flat plain, the paths, the coast and the Spire.
- Read the .cpa tail (CavePlacementAsset: CavernInstanceData, TileInstanceData, ManuallyPlacedExportedTemplateData;
  RTTI) for the cavern list, so sites can be placed one per cavern.

---

## L158 — keyword map: FOUND for items [P]; ability side unchanged

**Items [P].** I traced the ItemTooltipInfo slot FUN_143763a20 (part_0120.c:21487).
- It walks `*(param_2 + 0x68)` as a linked list: node {u32 keyword id, next at +8}.
- For each node it looks the id up in KeywordDefinitionManager (DAT_14543b478, definition +0x30), skips it when
  HIDE_FROM_CLIENT is set (+0x18), and adds NAME_ID (+0x14) to the tooltip's name set (+0x240).

param_2 is the item record of **InventoryPacketItemTooltipReply 0x2D/0x12**. The 0.695 reader FUN_142f17070 calls the
record reader FUN_142f17a30 at reply+0x18. Its record-relative fields match FUN_143763a20's uses one for one:

| Record offset | Field | Tooltip use |
|---|---|---|
| +0, +4, +8 | name, description, icon | +0x14, +0x18, +0x1c |
| +0x10 | definition id | +0x10 |
| +0x18 | 1st int list (categories) | +0x180 |
| +0x30 | 2nd int list | +0x198 |
| +0x48 | 3rd int list | +0x1b0 |
| **+0x60** | **4th list, FUN_142c34c30: a linked list whose head is at +0x68** | **the keyword list** |

FUN_142c34c30 [P] reads `u32 n` and then n × u32 into list nodes {u32 value; next at +8}.

ItemTooltipInfoWeapon (vtable 0x1447e4e40, slot FUN_14376ec10) calls the same function and then lists the weapon's
abilities. So weapon tooltips show both the keywords and the abilities.

**Server change.** In Network/Message/Model/InventoryTooltipReply.cs, the `writer.Write(0u);` right after the third int
list is this keyword count. Write instead: `u32 n, n × u32 KeywordDefinitions id`.
- Weapons: the kit's affinity (1-7), plus Ranged 56 / Melee 57 (as WeaponCombat.KeywordsOf, without Basic Attack 59).
- Pet figurines: Pet 86. That row is new in 0.695 together with the pets [I].
- Hidden rows (HIDE_FROM_CLIENT 1, e.g. Bow 36, Staff 40) are skipped by the client, so they cost bytes and show nothing.

Which items carried keywords on live is [I]. items.json has no keyword field: its `u59` list is DynamicInputActionSystems,
see BootAbilities.cs.

**Ability side.** No change from agent-l138-c1 §5 / the 2026-10-17 entry: map value = keyword, already built. The
visible rows a weapon attack could carry are 54 Defense Breaker, 55 Combo End, 56 Ranged, 57 Melee, 58 HeavyAttack,
59 Basic Attack and 60 Ranged-SlowProj (HIDE 0) [P table]. Ranged-SlowProj fits slow shots such as Ooze Bolt or the
40 m/s bolts [I].

**Next:** a hover test for both tooltips (the keyword_probe harness + one tooltip reply with {1, 56}).

---

## L157 — weapon → ProjectileDefinitions row: ADVANCED [P rows, I mapping]

**Method.**
- I joined every row to its Models.txt model, its trail / impact composite names (ActorCompositeEffectDefinitions.xml)
  and its **BONE_SOURCE**.
- I grouped the composite **effect ids** into families: ids authored together have neighbouring numbers, and they sit
  next to the weapon-specific cast / release effects.
- I matched those families against the 2016 ranged weapons in items.json.
- R_UpWeaponFlex and L_Weapon are character-skeleton bones (re/out/anim/strings.txt).

| Weapon (item, affinity) | Proposed rows | Evidence | Today (WeaponCombat.ProjectileRow) |
|---|---|---|---|
| Supercooled Power Cannon 3036 (Water, Pulverizer model) | **114** Water_Projectile_Large (charged Tidal Burst), **115** (s120) or **108** (s80) for Torrent | Only Water rows with BONE R_UpWeaponFlex (108 / 114 / 115) [P]; 12715 Water_Projectile_Large sits beside the 2016 gun ids; 12439 / 12458 "…LaserCannon" cast effects | 108 |
| Diabolical Scientist's Raygun 3032 (Affliction, Shrinkray) | **93** (s40) for the Ooze Bolt lob, **99 / 110** Affliction_Projectile01_Large (12443) for charged Sludge Storm | Only Affliction rows with R_UpWeaponFlex [P]; 12443 sits beside 12444 / 12445 "Affliction_Cast_Gun" | 100 |
| Gunslinger's Six-Shooters 3040 (Earth, dual revolvers) | **117** (R_Weapon) / **118** (L_Weapon), alternating shots | Physical_Projectile_Small_01 12718 sits between 12716 / 12717 "DualRevolver_MuzzleFlash" and 12719 "Cast_DualGuns" [P ids]; the only Physical R/L pair | 109 |
| Night Herald's Mercy 4089 (Shadow bow) | **119** Shadow_Projectile_Basic_Small arrow | 12739 sits between 12737 Shadow_Arrow_AOE (our Agonizing Volley effect), 12738 Shadow_Bow_MuzzleFlash_02 and 12740 Shadow_Bow_Arrow_Cast_03 | 89 |
| Wand of Boundless Light 3767 (Radiance) | **112** Radiance_Wand_Ability01_Projectile_03 (Brilliance) | Its release 11574 Radiance_Wand_Ability01_Release is the wand's; the server already uses 112 for the healer | 95 |
| Wanderer's Blazing Bow 3761 (Fire) | **111** (trail Fire_Bow_Attack01_ArrowTrail01 + impact Fire_Arrow, s80) | Fire_Bow cast / release 11345-11351 belong to this bow; 111 is the only Fire_Bow row with a Landmark-era impact | 87 |
| Overcharged Recon Blaster 3033 (Lightning gun) | **101** (L_Weapon s120) / **113** (L_Weapon s80) | The Lightning rows use the left-hand bone; Lightning_Cast/Release_Gun 12432-12435 / 12730 [I: which hand it is held in] | 96 |
| Staff of the Lightning Caller 4107 | **121** (Lightning, s40) for the slow Crackling Orb | Its 12783 ChargeStaff / 12784 Staff_AOE (our Static Bomb) [I] | 96 |
| Staff of Frozen Sorrow 3762 (Water staff) | **97** Ice bolt (R_Weapon) | 108 is a cannon row (R_UpWeaponFlex); the staff's own effects are "Ice_Staff" [I] | 108 |
| Lightbringer's Justice 3028 (Radiance bow, Heavenly) | **91** arrow, or the "EpicBow" blast | 12422-12425 pfx_Combat_Radiance_EpicBow_*; no row trail names EpicBow [I] | 91 |
| Crusader's Courage 4096 (charged swing launches an orb, text 576317) | **116** or **120** Radiance_Projectile_Blunt_01 | 12720 sits beside 12734 Radiance_MuzzleFlash_Blunt_01 [I: "Blunt" may be a mace] | none |

Rows 102 / 106 (Fire, R / L, s120) and 103 (Ice, s120) also exist, matching 12628 / 12637 Fire_Cast_Gun / _L01 and
12631-3 Ice_ChargeGun. No Fire or Ice gun is in the 2016 login list, so these are open (an NPC or a later item).

**Server change [I].** Replace the bow / other split in WeaponCombat.ProjectileRow with a per-weapon table as above. For
dual guns, alternate R/L rows per shot (Unload ticks). Keep the speeds from ProjectileDefinitions.SPEED: 114 / 108 are
80, 115 / 117 / 118 / 101 are 120, 93 / 121 are 40. Mark every entry [I].

**Side finding.** Texts 573167 "Run n' Gun", 573168 "Get 'em Good", 573169 "All or Nothin'" (descriptions 573170-573172:
revolver shot, shank stab, diving burst) and 573184 "Dirty Fighting" look like **Desperado's Deadly Arsenal**'s named
attacks [I strong: "your revolver … your shank"]. WeaponCombat.GenericKit's comment says that weapon has none.

**Next:** harness `fire` with each proposed row, to check the start bone (R_UpWeaponFlex / L_Weapon) on the right model.

---

## L128 — Env job types: ADVANCED [P]

FUN_14320ea00 is the 0.695 page-33 drawer (part_0091.c:33623; 0.589 FUN_143163fe0 is the same).
- The column header is `%50s  %10s%10s` with **"Pending"** then **"Active"** (the 5th vararg, local_10d8 = "Active").
- Each row prints name, then `+0x20+4i`, then `+0x6C+4i`. So **+0x20 = Pending** and **+0x6C = Active** [P].
- "Total Jobs" prints `+0xB8` and `+0xBC` in the same two columns. These are the two words right after the arrays:
  pending total and active total. **Total Jobs counts current jobs, not finished ones** [P layout; I that they are
  sums].
- The name table FUN_143c74ef0 is 0x14 slots with 19 names, so live used types 0-18.

**Server change.** JobCounters.cs / EnvMetrics are documented as "Active at +0x20, Pending at +0x6C, Total = finished".
- Swap them: the first 19 values are Pending.
- Send two totals (Σ pending, Σ active) in place of "finished since start". Finished counts can stay in Summary() as our
  own extra.
- The reply 0x4AC has no retail reader, so this only changes our admin page.

What types 4 / 5 / 6 / 17 were: still names only (UNCHANGED).

---

## L140 — parked C→S messages: ADVANCED, two parts FOUND [P]

1. **Group LFM / auto-group are reachable in retail [P].** FUN_142dd8450 is the /party handler.
   - It is called with "party" from FUN_142dda5f0, case 0 of command dispatcher FUN_142dda830, which is referenced from
     the command table at .rdata 0x1446c3c48.
   - It accepts hidden sub-commands that are not in its own usage text. Each one sends a packet:

     | Sub-command | Function | Packet |
     |---|---|---|
     | `partyautoon/off` | FUN_142df12d0 | **SetGroupFlags** |
     | `lfmon/off` (DAT_1446c3d10/18) | FUN_142df1e90 | SetGroupFlags bit 0x40 |
     | `lfmoptions` | FUN_142df1f70 | **SetGroupDescription + SetGroupFlags** |
     | `lfmsearch` | FUN_142df13b0 | **FindLookingForMoreGroup** |
     | `lfmjoin` (DAT_1446c3d40) | FUN_142df1860 | **JoinLookingForMore** |
     | `getpartyinfo` | FUN_142df1470 | **GetGroupInfo** |
     | `autoparty` | FUN_142df1180 | **AutoGroup** |

   - The options window's "Auto Party With Friends" (Data.xml:1154, GameSettings.SetAutoGroupWithFriends →
     FUN_142df3660) only sets a local flag bit, 4.
   - So a player who types these reaches the server. Live's answers are unknown: there is no design source for LFM.
   - **Server:** read and log each one, answer GetGroupInfo with the normal group record, and treat the others as no-ops
     until a source turns up. Do not drop them silently.
2. **SOEmote / FacialAnim cannot run [P files, I].** The pieces of the system are all in the client:
   - /soemote (ClientCommandProcessor::CommandSOEmote);
   - PacketClientFacialAnim, PacketClientFacialAnimSettings and the S→C PacketZoneProximityFacialAnim;
   - SOEmote face animations in the packs.

   But the capture needs **LiveDriverSDK.dll** (strings CreateLiveDriver / LiveDriverSDK.dll). That file is in neither
   install and not in the 2017 final file list (re/out/final-digest-2017).
   - Design: roadmap 26 Mar 2014 "adding this in now"; players in the 5 Mar 2015 blueprint thread asked where SOEmote
     was. It never shipped [I strong]. Nothing to build.
3. **Claim snapshots** (Snapshot Save / Restore / UpdateName, confirm-dialog natives): no shipped UI, Lua or pack
   references them, and no forum or wiki hit for "snapshot". They stay parked [P negative].
4. **LeaderboardPacketGetPlayerStats:** live's leaderboards were the Gallery's most-liked sites (P4670). This is
   profile-stat leaderboards, not used [I].
5. **Trial item line:** "trial" in 2016 notes means only the trial key (P6417). RequestTrialItem has no shipped caller
   [I].
6. **Lobby / mini-game / requisitions / repeating activities / housing / auto-attack:** 0 patch-note hits. Landmark used
   Game Tables (InstancedGames) [I]. They stay parked.

---

## L83 — Storybricks pages: FOUND (negative)

- The reply side has no retail reader (architecture-leads L4; admin-pages.md:16 "Only 28 / 29 have readers in retail").
- 0x4FC / 0x4FD exist only as requests in the id registry (part_0107.c:10524-10529). There is no response class in RTTI.
- The request is sent only by an admin client.
- Design: no wiki page. Forum threads in 2014-15 (12 dev threads, all player replies) treat Storybricks as gone from
  Landmark: Mar 2015 "what happened with Storybricks"; another post says "Storybricks is no longer part of Landmark".
- **Server behaviour:** do not answer 0x4FC / 0x4FD (log only). Close as "not live in Landmark" [I strong].

## L97 — FFA PvPAllowed: ADVANCED [P]

- FUN_142ea9630: in a running game (state 6), two team-0 players are enemies only when FUN_1434ec610 finds
  "InstancedGames_PvPAllowed" = '1' / 't' / 'T' in the game's settings map (+0x130 / +0x150) [P].
- FUN_143c1c060 adds one team when "InstancedGames_IsGameFFA" is set.
- IgGeneralSetting setting 7 (= IsGameFFA, RefIgGeneralSettingsTypes) has DEFAULT_VALUE_BOOL 1 only for rule sets 2
  and 7 (rows 6, 27) [P]. This agrees with HAS_FFA_TEAM_BY_DEFAULT.
- Our InstancedGames.Editor.cs:458-465 (PvPAllowed added for HasFfaTeam = 2 / 7) is consistent. No change.
- Still open: whether live sent the setting or forced PvP another way.

## L104 — /goto waypoint: ADVANCED [P]

- Waypoints.txt has two rows only: 1 "Personal Player Waypoint" (client requirement 3) and 2 "HUB Waypoint" (6561
  "WARP", requirement 11 TargetIsProfile 56).
- The server-fed waypoints are ZoneWaypointPacket 0x32/0x14:
  - reader FUN_1433eac80 / FUN_1433e8ee0 / FUN_1433e9040: `u8, bool clear, map u32 key → list {u32, u32, u32 delta}`;
  - FUN_1430d7c00 turns each delta into an absolute time on the waypoint manager (+0x168).
- WaypointCooldownPacket 0x13 is `map<int,int>` → FUN_1430d88d0.
- Personal waypoints never reach the server (L136).
- **Server:** /goto waypoint has no player data. Answer with an error line, as today. Zone HUB waypoints are a warp
  system with no Landmark source; leave them unbuilt.

## L107 — logged-only admin systems: ADVANCED [P]

- Vehicles.txt and every Vehicle*.txt have 0 rows, except VehicleArmorMappings (6 rows). Like Weapons.txt, no vehicle
  can exist, so /vehicle stays logged.
- PlacedEffect 0xEF:
  - Create (reader FUN_142f8bc50: u32 +0x10, u64 effect guid +0x18, u32 definition +0x20, position list, u64 +0x50
    time, bool +0x58 loop) → FUN_142f8c780 plays the composite and prints "PlacedEffect: creating placed effect…" as a
    system message. It is a debug tool.
  - **Destroy is parsed and does nothing** (part_0079.c ~2898).
- SetModel: PlayerUpdate UpdateTemporaryAppearance 0x26/9 has a reader (coverage-695.md:697), so it could be built if
  wanted.
- AddAmmo / WeaponSet: no data, because Weapons / WeaponAmmoSlots have 0 rows (agent-l138-c1 §2).
- Still the user's decision.

## L113 — procedural placement: ADVANCED (item 3)

- Forum 55919 (Holiday update thread, player post 16 Dec 2015): "environmental props within your claim that you can't
  delete … these boulders were pretty honking huge", seen "in many other places".
- So live placed separate **rock / boulder props** on landscapes, outside players' control, in late 2015 [P forum; I that
  they are the placement sets' decoration]. This supports turning ProceduralPlacementDecor on (with claim exclusion).
- Items (1), (2), (4) are unchanged.

## L122 — group member record: ADVANCED (built)

- Groups.cs:544-547 now writes the zone name text and the world name text.
- +0x100 is still a constant 1. Writing the world id there [I] would let "IsInZone" fail across worlds without
  HideLocation's zeroing. That is optional, and the position zeroing remains a bandwidth / privacy choice.
- Group lifetime after logout and cross-world cave invites: no source found (wiki has no group page).

## L123 — follow / recommendations: ADVANCED

- Follow / UnFollow / AddFollowing / RemoveFollowing are built (CommerceServer ProductService.cs:70-72,
  ClientPackets.cs:132-143).
- Recommendations request (0.695 FUN_143250d70): opcode 0x45, sub 0x38, body {u32 **100**, u32 **5**}. The first is
  probably a result limit [I]. Sent from Lua GetRecommendations, which has no shipped caller.
- There is no reply class. The only product-list reply is StoreBundleSearchResults 0x3F (reader FUN_14322eff0: string,
  bundle list, u32, u64 list).
- **Server [I]:** answer Recommendations with StoreBundleSearchResults holding up to 100 popular products. This is
  low-priority; only the web pages could call it.

## L124 — commerce inferences: ADVANCED [P]

- (b) ContestWindowDocumentClass.as:305-308 calls Lua `EnterContest(...)` and then **immediately `Close`**.
  ClaimManagementWindow shows only the claim's "InContest" data. The client never waits, so a missing answer just leaves
  the site out of the contest.
- (c) Hotfix 1 Apr 2016 (forum 56713, P4453): "Designs created by a third party should now correctly display the name of
  the **character** that created it (this includes Player Studio designs)". Creator identity is per character [P].
  Purchase currency stays account-wide (wiki).
- (a), (d) unchanged.

## L125 — zone → Env ApplyDiff: ADVANCED [P ids]

The VoxelConstruction registry (part_0107.c) splits into two blocks:
- **Client requests 0x1-0x9:** ApplyDiff 1, ClientSelectionArea 2, ClientUndoRedo 3, Ownership 4, OffGridPasteClient 5,
  Clipboard 6, Response 7, Unknown / MissingPropList 8 / 9.
- **Server-side block from 0xA:** ResponseObserved 0xA, AdminForceInstancesSetting 0xB, HealClaim 0xC,
  PasteClaimArea 0xD, DeleteClaimArea 0xE, ZoneSelectionArea 0xF, UndoRedo 0x10, RemoveUndoById 0x11,
  ZoneRequestFailedCellList 0x12, OffGridPasteZone 0x13, …, EnvServerResponse 0x15, DisplayMessage 0x1B.

Each client request the zone had to enrich got a twin; ApplyDiff did not. The Env→zone answer for a cell edit is
ZoneRequestFailedCellList.
- This fits the zone forwarding the client's ApplyDiffRequest itself, as ours does [I strong].
- Internal layouts: still open.

## L127 — ruin layers: ADVANCED [P]

- FUN_14320ea00 prints each 16-byte record as `Layer <list index>: Desired / Active / Failed / Last ID`
  (part_0091.c:33786-33799).
- There is no layer id or landscape field, so live's page identified layers only by their order.
- Our one-layer-per-landscape list is compatible. Keep a fixed landscape order so indices are stable.

## L132 / L133 — navmesh: ADVANCED

- [P] RTTI has AdminCommandPacketDebugPathQueryRequest but no DebugPathQueryResponse class (registry 0x4A8 exists),
  and no navmesh library.
- **Player reports on live (forum 56713, the hotfix thread of 1 Apr 2016):**
  - NPCs "walk all around the edge walls to the next pathing node";
  - a 45° doorway must be "as wide as a normal door would be on the X or Y axis" for a creature to pass;
  - "changing a texture on the floor under pathing will cause the npc to stop pathing";
  - "some npc's do not do their pathing until you get close";
  - props too close to nodes block the creature.

  Together this means live's navmesh was built from the edited voxels per cell. It was axis-aligned (voxel grid), was
  rebuilt or re-stitched when a cell changed, even a material-only change ("PendingStitchSections"), and was built
  lazily near players ("NavMeshJobsRemaining") [I strong].
- **Server direction (L133):** creature navigation on build sites must see player edits. A coarse per-cell solidity
  summary pushed with CellVersionsChanged (the lead's own idea) matches live's behaviour. Treat prop footprints as
  blocked (P5781).

## L135 — Magical Platform

- Forum adds only: the pad rotates as it moves when the dock ends are rotated (27 Aug 2014 notes). Players complained of
  "a chaotic ride as it twirls" and of a gap to the docks (fixed 11 May 2016).
- Dock-to-dock orientation interpolation (Props.Platforms.cs quaternion bodies) is right. The rider test is still the
  open item.

## L137 — Showcase pages: UNCHANGED

- archive.org targets from the exe: store.landmarkthegame.com, store-main.landmarkthegame.com, store.eqnlandmark.com.
- The client's REST path is `/marketplace/search/limit=50&filterEquals=Purchasable`.
- PlayerStudioRegistrationUrl is player-studio.daybreakgames.com/w9 (ClientConfig.ini).

## L81 / L82 — UNCHANGED

- Page 29: the client fills its own "Instanced Game Information - Client" block (FUN_14320f5f0 → FUN_1434e65d0). Nothing
  builds the server block, and no request id exists.
- Page 28: RTTI has only PlacementEntityList(Entities)Definition and ProceduralPlacementVoxelInterface, with no spawner
  class.
- Both remain as built (admin-pages.md §11).
