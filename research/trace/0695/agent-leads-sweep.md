# Open-leads sweep, 2026-10-17

Read-only pass over every open lead in [OPEN-LEADS.md](OPEN-LEADS.md). Skipped as asked: L4, L60, L135, L141, L145
and L146. Leads that are already closed in their own titles or entries are listed at the end of the table and are
not re-traced: L13, L17, L36, L38, L114 and L118.

**Method.** For each lead I first searched the existing research:

- trace/ (all subfolders), LandmarkEmu/docs, re/out (rtti_695, luabind_695, text_695, forum_raw), re/as3_695;
- the shipped Lua and UI (<0.695 test copy> Resources/Scripts, UI/Default, UI/Admin, UI/TestMod, UI/*.xml, and
  UI/ScriptsBase.bin read as bytes);
- the gameData695 tables, the wiki and forum dumps, and patchnotes.tsv;
- **all 0.695 packs**, with `re/tools/packgrep.py` and `LANDMARK_DIR=<0.695 test copy>`. As a control, `Emote_Index` hits
  5 .mrn files in 31 s.

After that I traced in re/decomp_all_695. I also read raw bytes of <0.695 test copy>/Landmark64.exe with two scratch
helpers in the scratchpad (sweep/rd.py reads, disassembles with capstone, and lists sections; sweep/ripref.py lists
every RIP-relative instruction that targets an address). I also used re/tools/refscan.py and re/tools/deadcode.py.
The exe was only read, never run or changed.

**Marks.** [P] means proven, with the function, table row or file:line given. [I] means inferred, with the reason
given.

---

## 1. Table

| Lead | Title (short) | Verdict | One-line result |
|---|---|---|---|
| L1 | TilingTestZZ.bmp mask | UNCHANGED (+neg) | Not in any 0.695 pack either (packgrep `TilingTest`: none) |
| L2 | Procedural entity-list rows | UNCHANGED | No new source |
| L3 | How retail selects a monitor page | NEEDS-RUNTIME | Only a Frida page hook can show a page; nothing new statically |
| L7 | 54 unresolved send sites | ADVANCED | FUN_14315f270 proven Zone (channel 0 constant, via the L77 trace) |
| L8 | World ↔ zone back-end talk | UNCHANGED | Names only; layouts are L48 |
| L10 | Lost Crystal loot lockout length | ADVANCED | No duration found; the refusal text exists: StringHashToValue `CaveRewards.TooSoonToUse` = 574500, which the server does not use |
| L11 | Tool wear on harvest | ADVANCED (weak) | Forum 56945 (Apr 2016): tools broke and needed Lumen repairs; no per-swing number |
| L12 | Where a Lost Chest could hide | **FOUND** (already built) | The 0.695 ruin_*.otd "Spawner: Ruins: Chests (1 Limit)" props 2731; Outposts.cs picks one at random |
| L14 | Layer 496 "391_Layer2" | UNCHANGED | Table rows listed; the consumer is not found |
| L15 | Continent → VoxelWorldSettings | UNCHANGED | Manager at defs +0x2a8 (FUN_143fbbd00); reader not located |
| L16 | .csc/.ocm, material 65532 | ADVANCED | The client treats materials 1..0xFFFC as solid (FUN_143f9cc80, FUN_144003140), so 65532 is the top of the solid range |
| L18 | EditablePropertyValues.txt | UNCHANGED (+neg) | `EDIT_PROPERTY_ID` is not in any 0.695 pack |
| L19 | Server-only setting rows | UNCHANGED | Forum 57987 / 57064 only describe the IF Gate |
| L20 | Story tools hidden in Play Mode | UNCHANGED | Callers of FUN_142d99b70 listed; not followed |
| L21 | Score / Victory Node, Audio / Group models | ADVANCED | Live prop list: 3011 Score Node uses model 1744 Points Node and 3010 uses 1745, both with image set 0; no prop uses 1742 / 1743 |
| L22 | Story reset message route | UNCHANGED | Not routed through the claim key helper FUN_142db7970 |
| L23 | Firing details | UNCHANGED | — |
| L24 | Login item list / missing items | UNCHANGED | No new capture |
| L25 | Fireworks effect / packs | UNCHANGED | spec-fireworks.md §6 already narrowed it |
| L26 | Roll Wraps source | UNCHANGED | — |
| L27 | Cosmetic pets | UNCHANGED | spec-pets.md §6 has the remaining unknowns |
| L28 | Inspect blob fields | ADVANCED | The 0.695 InspectWidget never shows +0x110, +0x11c or the profile list, so the server can send 0 / empty |
| L29 | Buy materials: result / FoundLumenRecipeForAllItems | **FOUND** (part) | +0x20 is never on the wire (the constructor zeroes it, the reader skips it) and no UI reads it |
| L30 | Story question bubble ids | **FOUND** | NpcDialogResponse HUD indicator (HudIndicators row 29): Yes/No send PropPacketDialogResponse {u64 question id, bool} |
| L31 | Game Finder unnamed fields | UNCHANGED | GameFinder.swf was not exported |
| L32 | Game Table rework fields | UNCHANGED | — |
| L33 | PlayerEnteredClaim reaction | ADVANCED (existing) | leads-L33-L35-L36.md §L33 has the recommendation |
| L35 | ShowActivationFailure texts | ADVANCED (existing) | leads-L33-L35-L36.md §L35 has the text table |
| L37 | Site-game struct u32 +0x15c | ADVANCED [I strong] | It is the team count: IgRuleSets DEFAULT_TEAM_COUNT is 2 for every team rule set |
| L40 | Designs categories TEMPLATE_ID | **FOUND** (existing, built) | Matches TemplateBasicData +0xec (spec-design-categories.md) |
| L41 | Unlabelled functions | UNCHANGED | — |
| L42 | Missing goal tiers | UNCHANGED | — |
| L43 | Replicator trackers / VISIBILITY_REQUIREMENT_ID | ADVANCED (small) | Stored at recipe definition +0x40 (FUN_143cb6e00); reader not found |
| L44 | Non-elite Barren props | UNCHANGED | Forum has only P4709 |
| L45 | Wiki names / riddle goals | UNCHANGED (rest) | — |
| L46 | Strip Miner / Deforester / Curator | UNCHANGED | — |
| L47 | .cpa trailing sections | UNCHANGED | — |
| L48 | Server-only message layouts | UNCHANGED | — |
| L49 | .vfm cave meshes | UNCHANGED | — |
| L50 | GatewayId in CharacterLoginRequest | UNCHANGED | No forum or wiki hit |
| L51 | CaveManager filter | UNCHANGED | — |
| L52 | PropBase 0x0A ListReply | **FOUND** | The 145-PropBase dump parses byte-exactly with the shared 0.589/0.695 reader, so 0.589 can read it |
| L53 | Class ↔ sub for unreferenced classes | UNCHANGED | — |
| L54 | Rich-text tokens in chat | NEEDS-RUNTIME | — |
| L55 | SetClaimAdventureFlag sentinel | **FOUND** | DAT_145476ff0 = 0 [P] |
| L56 | New Lua bindings with no caller | ADVANCED (neg) | No caller in any Lua, UI, ScriptsBase.bin or pack |
| L57 | Design cost line update | NEEDS-RUNTIME | — |
| L58 | Interaction glow missing on 0.695 | ADVANCED → NEEDS-RUNTIME | The 0x6B path is identical in both builds; the flag is dropped if the guid is not yet in the +0x50 table |
| L59 | Leaderboards | ADVANCED (neg) | No caller of Leaderboard.* anywhere (Lua, UI, packs) |
| L61 | Relogin 60 s deadline | **FOUND** | The client gives up with "Timed out while waiting for a relogin session." and shuts down |
| L62 | Commerce session ticket | UNCHANGED | — |
| L63 | Lobby / quiz / requisition natives | ADVANCED (neg) | No caller in Lua, UI or packs |
| L64 | Guild mail addressing | ADVANCED [P layout] | ComposeRequest has no guild field: To names, ToGuid, Subject, Message, COD, attachments |
| L65 | Public chat channels | ADVANCED [P] | OnAddChatChannel / OnRemoveChatChannel are never called from native code (the names are not in the exe) |
| L66 | Friend presence / groups | UNCHANGED (small) | The online notice is skipped while the guid is in the +0x78 map; expiry not found |
| L67 | /who, mail notice, quick chat | ADVANCED (a) | Wiki and forum: /who lists every player on the current server, with a count |
| L68 | When 0x4C was sent | UNCHANGED | — |
| L69 | Gateway internal packet fields | UNCHANGED | — |
| L70 | Gateway with a back end down | UNCHANGED | — |
| L71 | Who sends gateway Logout op 3 | **FOUND** | Nobody: FUN_143a4bb50 is dead in 0.695 (its only references are exception-handling tables) |
| L72 | Expected connections | UNCHANGED | Parameter at UdpParams +0x180 (part_0133:36282); no use found |
| L73 | Re-attach to another zone process | UNCHANGED | — |
| L74 | Gateway commands / flood limits | UNCHANGED | — |
| L75 | Who relayed channel-3 WorldEditing | UNCHANGED | — |
| L76 | Env Server heal / ruin / job types | UNCHANGED | — |
| L77 | OffGridPasteClientRequest send | **FOUND** | std::function callback → FUN_14315f270 → sent on Zone (channel 0); writer FUN_14315b130 |
| L78 | ResetAllCells / ResetTemporaryCells | **FOUND** | The sender FUN_143158bb0 is dead in 0.695 |
| L79 | MaterialChangeInfo fields | ADVANCED | The u16 is stored at block-event +0x70 with the cell set; no reader near it |
| L80 | Manifest flag | ADVANCED [P] | Entry layout decoded (flag at entry +4); reader not found |
| L81 | Page-29 live content | UNCHANGED | — |
| L82 | Page-28 field meanings | UNCHANGED | — |
| L83 | Storybricks pages 30-31 | UNCHANGED | — |
| L84 | Mail attachments missing on 0.695 | NEEDS-RUNTIME | Window fill and item builder: same calls and keys as 0.589 |
| L85 | LightweightToFullNpc creature values | ADVANCED (existing) | full-npc-695.md §4 has the layout and defaults; live values need a capture |
| L86 | Build Site Expansion definition | ADVANCED | Forum: consumed on use, tradable, sold in the Marketplace; item id still missing |
| L87 | Upkeep reminder wording | UNCHANGED | The reminder appeared in the Notification Window [I] |
| L88 | Iron frequency | UNCHANGED | — |
| L89 | Revive invisibility | UNCHANGED | — |
| L90 | Gear passive numbers | UNCHANGED | — |
| L91 / L108 | Four weapon recipe sources | ADVANCED | 25 May 2016: "Recipes for these will drop"; no station mapping in 0.695, so drop-only by then [I] |
| L92 / L93 | Modular castle / retired tutorial goals | UNCHANGED | — |
| L94 | Standing spot in a stamped outpost | NEEDS-RUNTIME | — |
| L95 | Spawner row 40 | UNCHANGED | Options are server-only (EDIT_PROPERTY_ID is in no pack) |
| L96 | Story text over a creature | NEEDS-RUNTIME | Same +0x50 table as L30 and L58 |
| L97 | FFA PvPAllowed | ADVANCED | IgRuleSets HAS_FFA_TEAM_BY_DEFAULT = 1 only for rule sets 2 and 7; setting 11 exists only for rule set 8 |
| L99 | Pioneer caverns | ADVANCED (neg) | The 0.589 caves folder (211 files) has none for 417-421 either |
| L100 | Spire raise | ADVANCED | Two Spire templates; every prop is 0.8 m higher in portalspirebase.otd |
| L101 | UniqueEquip bit reader | ADVANCED (neg) | No code tests definition +0xe & 0x80 |
| L102 | Outposts as build sites | UNCHANGED | User decision; walk-on fields listed |
| L103 | Outfit tint colours | NEEDS-RUNTIME | — |
| L104 | Player waypoints on the server | ADVANCED | The map waypoint is client-only (see L136) |
| L105 | Invalid-guid and default run speed constants | **FOUND** | DAT_145462af8 = 0; DAT_144c410f0 = 1.0f; "default" sends 0.0 |
| L106 | ClientTime handler cases | **FOUND** | All 7 cases decoded from the jump table |
| L107 | Logged-only admin systems | UNCHANGED | User decision |
| L109 | Events world id / name | ADVANCED [P] | The world list name is a text id, so 567016 "Events" is the only candidate |
| L110 | Event island rules | UNCHANGED | User decision on expiring sites |
| L111 | Landmarks of Landmark | ADVANCED | Forum 57555 (not searched before): placement beside each spire; older winners fill the extra landscapes |
| L112 | Tutorial goals' live records | UNCHANGED | — |
| L113 | Procedural placement model | UNCHANGED | — |
| L115 | World list history | UNCHANGED | — |
| L116 | Short move without a loading screen | **FOUND** (existing) | Root cause in OPEN-LEADS "Found" (QuerySimpleVisibility list cycle); the server cannot fix it |
| L117 | Story emotes on creature networks | UNCHANGED | — |
| L119 | Highfall Catapult numbers | UNCHANGED | — |
| L120 | Cavern drops / healers | UNCHANGED | — |
| L121 | Jump / fall in movement state | NEEDS-RUNTIME (+ADV) | The client airborne test has no ability or item-use caller |
| L122 | Group members on other worlds | ADVANCED [P] | +0x10c and +0x104 are name TEXT ids; linkdead null = 0; bonus range is 200 |
| L123 | Player Studio follow | ADVANCED [P] | AddFollowing reply layout and Lua event decoded |
| L124 | Commerce inferred behaviour | ADVANCED (neg) | OnReceiveServerStatus has no consumer in any 0.695 UI, Lua or pack |
| L125 | Zone → Env Server ApplyDiff | UNCHANGED | — |
| L126 | ZoneDisplayNotification texts | ADVANCED [P] | 562155 is the text of Claim.LostClaimServerConnection (L142) |
| L127 | Ruin layers in the Env Server | ADVANCED | TerrainObjectDeploymentGroups.txt is the engine's terrain-object (DPO) system, not ruin layers |
| L128 | Env job types 4 / 5 / 6 / 17 | UNCHANGED | — |
| L129 | Trip landscape / cavern pick | UNCHANGED | — |
| L130 | Spire crystals on other landscapes | NEEDS-RUNTIME | — |
| L131 | Lost Crystal "other worlds" | ADVANCED | The game's own "other worlds" means the Travel window's Other Worlds button → landscapes |
| L132 | Live navmesh | ADVANCED (neg) | No navmesh library in the 0.695 RTTI |
| L133 | Player edits in navigation | UNCHANGED | Design item |
| L134 | Editable property key hash | **FOUND** | Key = one-at-a-time hash of the EditableProperties STRING name |
| L136 | Map waypoints | **FOUND** | Ctrl+click waypoints are client-local (5 slots); no packet is sent |
| L137 | Live Showcase pages | UNCHANGED | Needs archive.org (no browser in this pass) |
| L138 | Server-only tables | UNCHANGED | — |
| L139 | Creature projectiles | **FOUND** (existing) | The LaunchProjectile handler is `ret 0` (OPEN-LEADS L140 note) |
| L140 | Parked C→S messages | UNCHANGED | — |
| L142 | Claim server connection lost | **FOUND** | 0xF9/0x12 has no body and shows text 562155 as a system and chat message; nothing is blocked |
| L143 | Server-driven windows | ADVANCED [P] | ZoneCraftingWindow closes (bool 0) or refreshes (bool 1) the crafting / repair window |
| L144 | Grief points | ADVANCED [P] | Level ≥ 4 blocks the Knife / QuickUse slots and abilities without definition flag 0x10; the earning rule is still open |
| L34 | Crimson Parlor attachment list | **FOUND** (existing) | Its own entry already says FOUND: send the equipped attachments |
| L39 | Tint unlock knowledge | **FOUND** (existing) | leads-L39.md |
| L13, L17, L36, L38, L114, L118 | — | closed already | Not re-traced |

---

## 2. Evidence per lead

### L1 — TilingTestZZ.bmp
- I ran packgrep over every 0.695 pack for `TilingTest` and found nothing; the control search worked. Together with
  the searches already listed in the lead, the mask is in no client file of either build.
- **Try next:** another client build or an archive of EQN-era development tools.

### L2 — Entity-list rows
- I found no new source. The packgrep for `EntityList` was already done in agent-L1-L2.md.
- **Try next:** unchanged; the lead's own plan to rebuild the rows from the sets plus Domino's model still applies.

### L3 — Monitor page selection (NEEDS-RUNTIME)
- Nothing new statically: architecture-leads.md L3 already proves that nothing calls the page setter.
- **Frida probe on the test copy:** get the monitor object from the PlayClient field that architecture-leads.md L3
  names. Then call FUN_143226620(monitor, 1) to enable pages and FUN_143226630(monitor, page) to select one. Follow
  the rule never to detach while hooked. This proves the pages render; it does not change the user's decision to use
  a web admin page.

### L7 — Unresolved send sites
- **[P]** FUN_14315f270 sends through FUN_14315cea0 with `param_3 = 0`. The tunnel byte is `(char)param_3 << 5 | 6`,
  so the channel is 0 = Zone (part of the L77 trace below). The other seven sites are not followed.

### L8 — World ↔ zone
- Nothing new. The names are in architecture-leads.md L8 and the layouts are L48.

### L10 — Lost Crystal lockout
- **Searched:** forum 57065-5 (the developer: the timer is per crystal), 57065-11 (a player: "once every 24 hours or
  so"), 57451 ("significantly increased"), 57252, and text_695 / StringHashToValue for any duration.
- **[P]** gameData695 StringHashToValue.txt:68 has `CaveRewards.TooSoonToUse^574500`. Text 574500, present in both
  0.589 and 0.695, reads: "You've recently bonded with a Lost Crystal in this Chaos Cavern. Use the Travel menu to
  start a new underground adventure!". The server never sends it (grep of LandmarkEmu found no 574500).
- **Server note:** show 574500 on a locked-out crystal. The 4 May 2016 developer post described only a teleport
  prompt, so this text may be the later behaviour [I].
- **Try next:** forum threads after 15 Jun 2016 that quote 574500 together with a time.

### L11 — Tool wear
- Forum 56945 (16 Apr 2016, loading-tips thread, a player) mentions "your tools are broken and you have no lumens to
  repair them with". It asks for durability on weapons and armour only.
- This confirms that tools wore out during harvesting in April 2016. It gives no number per swing.

### L12 — Lost Chest spots: FOUND (already built)
- **[P]** terrain-695.md:28 and :158: the 0.695 ruin_*.otd templates gained "Spawner: Ruins: Chests (1 Limit)" props
  (prop 2731), up to 43 per ruin.
- **[P]** Game/Outposts.cs:63 (`ChestSpotProp = 2731`) and :109-110 / :528-529 collect them, and :545-548 place the
  Lost Chest at a random one of them.
- **[I]** "1 Limit" means one chest per ruin.
- **Server change:** none. Move the lead to Found.

### L14 / L15
- **L14 rows [P]:**
  - VoxelContinentLayers 496 `391_Layer2` has the same biome map 00019, density 1, 83 encounters and TELEPORT_IMAGE_ID
    10229; layer 364 has 10228.
  - VoxelLayerBmpToPropGroups 1610-1612 map layer 496: bmp value 250 → group 234, 125 → 228, 20 → 258.
  - The consumer is not found.
- **L15:** VoxelContinentWorld loads into defs +0x2a8 (part_0162:10440, FUN_143fbbd00). The per-continent reader was
  not located.

### L16 — Material 65532
- **[P]** FUN_143f9cc80 (part_0161:22094) counts a voxel as valid when flag bit 0 is set and `(m-1) < 0xFFFC`, that is
  m in 1..0xFFFC, or when m == 0xFFFF.
- **[P]** FUN_144003140 (part_0163:33055, :33071) counts a voxel as solid only when m is in 1..0xFFFD.
- So 0xFFFC sits inside the solid range as its highest regular id, while 0xFFFD and 0xFFFE are special.
- **[I]** 65532 is a solid placeholder ("any solid") fill in the ruin templates.
- The .ocm grids and the behaviour without the cache files are still open.

### L18 / L95 — EditablePropertyValues
- Neither `EDIT_PROPERTY_ID` nor any other column name is in any 0.695 pack (packgrep). The option values came only
  from the server.

### L19 — IF Gate rows
- Forum 57987 (P6494 / P6495) and 57064 only describe linking the gate. No labels or comparison words were found.

### L20 — Play Mode visibility
- The only "PlayMode" strings are Claim.NoCameraPlayMode, the ToggleClaimPlayMode actions and FUN_142d99b70's
  "PlayMode".
- Callers of FUN_142d99b70: part_0069:14035 and part_0113:7315 / 8883. Not followed yet; they are the next step.

### L21 — Story-node props: ADVANCED [P, from the live capture]
- The 0.695-era live prop list (see L52) has these rows:

  | Prop | Model | Model file | Image set |
  |---|---|---|---|
  | 3010 Victory Node | 1745 | `Char_SimpleObject_RuleSetObjects01_DMWidgets_VictoryNode01.adr` | 0 |
  | 3011 Score Node | **1744** | `..._PointsNode01.adr` | 0 |
  | 2901 Counter | 1728 | — | 21676 |
  | 3000 Scoreboard | 1736 | — | 21676 |
  | 2940 Combat Node | 1727 | — | 21666 |

- So model 1744 "Points Node" belongs to the Score Node [P].
- No prop in the live list uses model 1742 (Audio Node) or 1743 (Group Node) [P for that list].
- Image set 0 means there is no palette icon, which fits "not placed from the palette" [I].

### L22 — Story reset message
- Claim messages go through FUN_142db7970(key string) (part_0069). "Claim.StoryReset.Message" is not among the keys
  passed to it, so the route is a server-sent text id 577089 in a generic text packet [I].

### L28 — Inspect blob: ADVANCED
- **[P]** InspectWidget.as (re/as3_695) reads CharacterName, CharacterGuid, BioString, LuminaryScore and
  NumberOfClaims, plus the equipment rows (EquipmentSlot, SlotNumber, Name, IconId, ItemGuid). It also calls
  `ClientInventory.RequestDetailsForItem`. It never reads +0x110, +0x11c or the profile list.
- **[P]** The profile list {key, value} updates proxy entries (array +0x370) whose key matches. A value v > 1 adds v-1
  to +0x118 (FUN_143089c10 lines 34-78). This looks like a rank-sum from EQN [I].
- **Server:** send 0 for +0x110 / +0x11c and n = 0 for the profile list. Nothing visible depends on them.

### L29 — FoundLumenRecipeForAllItems: FOUND (part)
- **[P]** In the dispatcher at part_0086 ~4372 (case 0x1c), the constructor zeroes +0x20 (`local_cf8 = 0`).
- **[P]** The reader FUN_143100050 reads u16, i8 (+0x10), u64 (+0x18), the recipe array (FUN_143101520, +0x28) and
  u32 TotalLumens (+0xcc8). It never touches +0x20.
- **[P]** FUN_14310c290 publishes +0x20 as `FoundLumenRecipeForAllItems`, so the value is always 0.
- **[P]** No AS3 script reads it (grep of as3_695). TemplateConfirmationWidget.as:91 reads only `TotalLumens`.
- **Server change:** none. The field is dead.
- **Still open:** what live sent after 0x1F. No Template sub after 0x1E exists, so it was generic currency and
  inventory updates [I]; see L57.

### L30 — Question bubble: FOUND [P]
- **[P]** gameData695 HudIndicators.txt has the row
  `NpcDialogResponse^29^31^...^Yes^No^Prop.OnConfirmNpcDialog^Prop.OnDenyNpcDialog`: a HUD indicator of code type 31
  with Yes and No buttons.
- **[P]** The bubble draw path (part_0074 ~40064) checks actor +0x127c, the question bool. When it is set, it calls
  FUN_1434445a0(hud, actor guid, actor+0x1280). That calls FUN_14343ca50 (part_0104 ~22650), which creates a
  `NpcDialogResponseIndicator` (constructor FUN_143436d30) keyed by the speaker guid with indicator[0x30] = actor
  +0x1280.
- **[P]** FUN_142ebfcb0 fills actor +0x1278 (mode), +0x127c (bool) and +0x1280 (the u64 question id) from
  DisplayPropText.
- **[P]** The natives FUN_1435fbf80 (Confirm) and FUN_1435fc030 (Deny) take arg1 as the u64 and send
  PropPacketDialogResponse 0xED/0x1D {u64 arg1, bool 1 or 0}. They take arg2 as the speaker guid and clear its bubble
  through FUN_142f85390 and FUN_142ebd0f0.
- **Server change:** none; PropHandler and StoryTools already handle it.
- The question id on the wire is the DisplayPropText u64 at +0x18. The indicator only appears for actors in the
  +0x50 table, which is the L96 issue.

### L33 / L35
- Already worked out in [leads-L33-L35-L36.md](leads-L33-L35-L36.md): the recommendation for L33 and the table of
  refusal texts for L35. No new evidence. Only a capture can settle live's exact choice.

### L37 — Site-game +0x15c: ADVANCED [I strong]
- **[P]** The walk-on fill (part_0069 ~20740) reads the entry fields in this order:
  - +0x148 InstancedGameState;
  - +0x154 MaxPlayers;
  - +0x158 MinPlayers;
  - **+0x15c**: `CanSwitchTeam` needs ≥ 2, the claim set (+0x38) and +0x80 == 0, and when the state is > 5 also
    FUN_142dc6b80;
  - +0x160 CurrentPlayers;
  - +0x164 Difficulty.
- **[P]** IgRuleSets.txt DEFAULT_TEAM_COUNT is:
  - 2 for 3 Team Deathmatch, 4 Traitors! and 6 Team Zone Control;
  - 0 for 2 Deathmatch, 5 All Scoring and 7 FFA Zone Control;
  - 1 for 8 Adventure.
- **Server:** send the game's team count at +0x15c (the rule set's DEFAULT_TEAM_COUNT unless the game overrides it).

### L40 — FOUND (existing, built)
- spec-design-categories.md §0 items 2-4 (part_0120.c:8670-8716 and :9171): TEMPLATE_ID is compared with
  TemplateBasicData u32 +0xec. The id is the live template database id, which is also the design item's grantId.
- Already built: Templates.cs:156-168 and ItemUses.cs:334-391. Move the lead to Found.

### L43
- **[P]** The Recipes loader FUN_143cb6e00 (part_0147:13496) stores VISIBILITY_REQUIREMENT_ID at recipe definition
  +0x40 (+0x44 is CAN_CRAFT_WITHOUT_STATION).
- Its reader is not found yet. The three `+0x40 != 0` hits in part_0077 are hash-table counts.

### L52 — Prop list: FOUND [P]
- `Resources/PacketDumps/145-PropBase` (353,734 bytes) parses to its last byte (end offset == length) with the reader
  FUN_142faa9b0, which 0.589 shares (FUN_142f0bc80, same code). The layout:
  - Base header FUN_142c2e030: u16 0xED, i8 sub 0x0A, u64 (0x4b561bae72200691 in the dump).
  - FUN_142fa8940: bool, which is 1 in the dump. Only when it is 0 do a u32 (+0x24) and a u64 (+0x28) follow.
  - u32 n = 1982.
  - n × elements of 0x68 bytes (FUN_142fa7e30):

    | Field | Offset |
    |---|---|
    | str model .adr | +0 |
    | u32 prop id | +0x48 |
    | str internal name | +0x18 |
    | str name | +0x30 |
    | f32 0.5 | +0x4c |
    | f32 10.0 | +0x50 |
    | u32 image-set id | +0x54 |
    | u32 0 | +0x58 |
    | u32 flag 0/1/2 (932 / 1042 / 8 rows) | +0x5c |
    | u32 model id | +0x60 |

  - Prop ids run from 3 to 3013.
- **Server change:** the comment at MiscHandler.cs:283 ("0.589 has no reader") is wrong. The send can be un-gated for
  0.589 clients, ideally after a 0.589 test.
- **Still open:** the meaning of the two floats, of +0x58, and of the 0/1/2 flag.

### L54, L57, L94, L103, L130 — NEEDS-RUNTIME, using the lead's own probes
- **L54:** type `~[~b~]~` in chat on the 0.695 test copy and watch both the client and the server log.
- **L57:** buy missing materials and check whether "Total Lumen Cost" redraws before a currency update arrives. Read
  `Character.Currencies` through Lua; the widget redraws only on currency changes (TemplateConfirmationWidget.as:55-58,
  :176-179).
- **L94 / L103 / L130:** these need in-game checks, as their entries describe.

### L55 — Claim sentinel: FOUND [P]
- DAT_145476ff0 lies in .data beyond the raw size, so it is zero-initialised.
- sweep/ripref.py finds seven RIP-relative reads (six loads, one compare) and no write. refscan finds no LEA and no
  pointer to it.
- So the value is **0**: Lua argument 0 means "the current claim" (claim +0x348). packets-695.md §6's "-1" is wrong.
- **Server:** no change; the client substitutes the real claim guid before sending.

### L56 / L59 / L63 / L124 — UI callers (negative, ADVANCED)
- **Searched:** the 31 shipped Lua scripts, UI/Default, UI/Admin, UI/TestMod, UI/*.xml, UI/ScriptsBase.bin (as bytes)
  and **every 0.695 pack** (packgrep).
- **Names searched, none found:** GetEyeSlotCount, SetEyeSlotIndex, Mesh.Populate, GetMeshesFilteredByAvailability,
  GetCursorSmoothing, GetLeaderboard, OnReceiveServerStatus, JoinLobbyGame, RequestQuiz, FinishRequisition,
  OnPlayerTeleportPostQueueNotify, GetFriendPositions, SetWaypoint, OnGriefLevelChanged, ChannelOn,
  OnCommerceAccountAddFollowing.
- AmbientVolume appears only in Data.xml (options).
- **Conclusion:** in the shipped 0.695 client these natives and events have no UI consumer [P for the shipped
  files]. Only the unshipped web pages could have used them (L137).

### L58 — Interaction glow: ADVANCED → NEEDS-RUNTIME
- **[P]** 0.695 FUN_142d54c10 handles 0x6B: reader FUN_142c4beb0 {u16 op, u32 sub, bool on, Array<u64 guid>}, then
  FUN_142f85770.
- **[P]** FUN_142f85770 is identical to 0.589 FUN_142ee6aa0. It looks each guid up in the +0x50 table (0x1003
  buckets, key at entity +0x2b0), sets or clears entity +0x1461 bit 0x40, calls FUN_142e94d20 on clear and always
  calls FUN_142e93f50.
- **[P]** FUN_142e93f50 creates the interaction indicator once, when entity[0x453] == 0 and vtable +0x738 says the
  entity is interactable.
- functions.tsv marks 142e93f50, 142e94d20 and 142f85770 as "same".
- A guid that is not yet in the +0x50 table when 0x6B arrives is silently dropped.
- **Probe:** with Frida on FUN_142f85770 entry (never detach while hooked), log each guid and whether the table lookup
  hits.
- **Cheaper test:** resend 0x6B a few seconds after the NPC spawn and see whether the outline appears.

### L61 — Relogin deadline: FOUND [P]
- FUN_142d57e60 (part_0067:9390) sends 0xF6 and sets game +0x324a0 (= `param_1[0x6494]`) to now + 60000.
- The run-state update FUN_142cd4e60, case 0x15 WaitingForReloginSession (part_0065 ~10850):
  - While the deadline is unset or not yet passed, it keeps waiting if the gateway is connected
    (`DAT_145431088+8` vtable +0x50; FUN_142df6120(…,1000)). Otherwise it logs "Lost connection to the gateway while
    waiting for a relogin session."
  - Once the deadline passes, it logs **"Timed out while waiting for a relogin session."** and goes to run state 0x17
    `cClientRunStateShuttingDown` ("Shutting Down."), so the client quits.
- FUN_142d54a00 (part_0067:7090) has a second consumer: with flag +0x6493 set and the deadline passed, it calls vtable
  +0xe0(…, 1, 0x1e, "Did not receive One-Time Session Id from server.").
- The deadline is reset to DAT_145430f10 in FUN_142c5cdf0 and FUN_142d115d0.
- **Server change:** guarantee an 0xF7 within 60 s even while the world-channel service is down. Answer from the
  gateway or zone fallback, as gateway-service.md §3.5 already holds channel 1. Otherwise the client exits.

### L64 — Guild mail: ADVANCED [P layout]
- Mail.SendMessage (luabind_695, FUN_1435b9f00) calls FUN_14363f210(+0x56eb0, 0), which calls FUN_14363b170.
  FUN_14363b170 fills ComposeRequest from the compose data source:
  - `ToGuid` → +0x18;
  - `To`, split on space, `;` and `,`, into a name array at +0x20;
  - `Subject` +0x830, `Message` +0x890, `CurrencyCount`, `IsCashOnDelivery`;
  - attachments with AttachmentType ≠ 3, ItemGUID and Quantity → +0x930.
- The request is sent by FUN_143635b70. There is no guild flag or kind.
- **[I]** live decided that a mail was a guild mail on the server, from a recipient name (the guild's name) in `To`.

### L65 — Public channels: ADVANCED [P]
- `OnAddChatChannel` and `OnRemoveChatChannel` (ChatManager.lua:382-391) do not appear in the 0.695 exe: a raw byte
  search for both ASCII and UTF-16 finds nothing.
- ScriptsBase.bin holds only their definitions, and the one call from OnAddChatChannel to
  OnAddChatChannelToDefaultChatTab.
- So no native code fires them in 0.695. Public channel tabs must come from the RoomInfoNotification path (room
  manager FUN_14357d8d0).

### L66
- **[P]** 0.695 FUN_143542e40 (FriendOnline) skips `UI_FriendOnline` while the guid is in the +0x78 map.
- **[P]** FUN_1435407d0 stores (guid, now) there, or refreshes the time if the guid is already present.
- The expiry window is not found.

### L67 (a) — /who: ADVANCED
- design/wiki/pages/Commands List.wiki:51-52: "/who — Shows all of the players on the current server (lag while
  loading)". Lines 102-104: "/who <channelname> — See a list of who is in the channel".
- Forum 57170 (May 2016, a player): /who "lists how many players are currently on your server and who they are".
- So the world registered /who and answered with every player on the world. Parts (b) to (d) are unchanged.

### L68
- Forum 48075 (Sep 2014, a player): "Saw the message for server shutdown, but couldnt teleport back". That was a
  warning before the restart, not 0x4C. Nothing else was found.

### L71 — Gateway Logout: FOUND [P]
- refscan of 143a4bb50 finds no call, LEA or pointer. Its only "references" are a .pdata entry and .rdata 0x145080210.
- The bytes at 0x145080210 are `50 bb a4 03 ff ff ff ff`, inside a run of {RVA, state} pairs: a C++ exception
  IP-to-state entry, not a caller.
- deadcode finds no rooted caller.
- So **the 0.695 retail client never sends gateway Logout (op 3)**. It leaves by its other paths.
- **Server:** no handler needed (log only if it ever arrives).

### L72
- UdpParams.processExpectedConnectionsOnly is read into params +0x180 (part_0133:36282). No use was found in the
  UdpLibrary code. It is plausibly a server-side option the client never needs [I].

### L77 — Off-grid paste: FOUND [P] (send site, channel, writer)
- FUN_143159fe0 wraps the copied OffGridPasteClientRequest in a `std::function<void(ConstructionArea,
  HashList<int>)>` (vtable 144715a30, RTTI `_Func_impl<lambda_f0b2ec38…>`).
- Its call slot FUN_143167320 (slot 2) calls **FUN_14315f270**, which:
  1. calls FUN_143161c90(area);
  2. copies the ConstructionArea box into the packet (+0x100, +0x108, +0x110 u32, +0x114 u64, +0x11c u32), unless
     flag +0x130 is set;
  3. fills a (u16, int) list at +0xd0 from the HashList;
  4. sends with **FUN_14315cea0(gateway, packet, 0, …)**, which is channel **0 = Zone**.
- **Body writer FUN_14315b130, in order:** header FUN_14315b0c0; FUN_142da2f20 (+0x20); four ×
  FUN_142c32fb0 (+0x40, +0x50, +0x60, +0x70); u32 +0x80; u8 +0x84; FUN_14315c180 (+0x85); FUN_143100870 (+0x88);
  u64 +0xa0; FUN_14315bea0 (+0xa8); FUN_1431005d0 (+0xc0); u64 +0xf0; FUN_1431006f0 (+0xf8, +0x104); u8 +0x110; and
  more after that.
- **Server change:** handle VoxelConstruction 0xF4 sub 5 on the zone. Read the body with the order above; expand each
  sub-writer with a 0.695-aware pkt.py, since pkt.py's default index does not have 14315b130. Forward it to the
  Environment Server as OffGridPasteZoneRequest 0x13. Building.Handle currently falls through to "Unanswered".
- This also confirms L7's FUN_14315f270 as a Zone sender.

### L78 — ResetAllCells / ResetTemporaryCells: FOUND [P]
- refscan of 143158bb0 finds NONE. .rdata 0x144e93df8 holds `b0 8b 15 03 ff ff ff ff`, an exception IP-to-state
  entry. deadcode finds no rooted caller.
- So the 0.695 retail client never sends 0xEC/8 or /9.
- **Server:** keep them unhandled (log only).

### L79
- **[P]** FUN_1436bb0e0 finds or creates the block event (0xe8 bytes, constructor FUN_143665f00) keyed by the packet's
  trailing u64 in map +0x98. It then calls FUN_143667630(event, u16, cells), which stores the u16 at **event +0x70**
  and copies the cells into +0x78 / list +0x28.
- A scan of every function from 0x143665000 to 0x143669000 for reads of `(param_1 + 0x70)` found only the writer.
- **Next:** the block event's vtable slots and its owner (ZoneProcessingComplete / CellBlockDamage handlers) reading
  +0x70.

### L80 — Manifest flag: ADVANCED [P]
- FUN_143fac700, called from HandleCellManifest FUN_144016680 into impl +0x150, reads:
  - for set s in {0, 1}, for layer k in 0..3: u32 n, then n × {u64 cell, u32 version, u8 flag}.
- The entry it fills: +0 set, **+4 u8 flag** (the last layer read wins), +8 / +0xc / +0x10 / +0x14 versions per layer,
  +0x28 cell id.
- The reader of entry +4 is not found yet.

### L84 — Mail attachments
- **[P]** 0.695 FUN_14363a660 (marked "changed") and 0.589 FUN_143587660 make the same sequence of calls and data
  keys: Sender, Subject, Message, email-message.html, NumAttachments, IsValid, ErrorMessage, CurrentCurrencyCount,
  IsCashOnDelivery, CurrencyCount, the attachment copy, the item builder, AttachmentSlot, AttachmentType.
- **[P]** The item builders 0.695 FUN_142f258f0 and 0.589 FUN_142e86b00 have identical key lists.
- No code difference explains the empty strip.
- **NEEDS-RUNTIME:** a focused screenshot, plus a lualog of ReadEmailMessage on 0.695.

### L85
- full-npc-695.md §4 already maps every LightweightToFullNpc field and the defaults the server sends. Live's values
  need a capture.

### L86 — Build Site Expansion: ADVANCED
- **[P, forum_raw]:**
  - 56259-1, known issue Mar 2016: consuming "Root Build Site Expansion items" should update the "build site unlocks
    available" count in Site Management;
  - 56442-3, a player: right-click uses it and the item disappears, with no flag given;
  - 56713-1: "Base Build Site Expansion is no longer listed in the Marketplace twice";
  - 57105-1: "Get more build sites" opens the purchase window;
  - 57682-1, the developers, Jul 2016: the expansion goes to the inventory, "where you will have the choice of
    whether to consume it yourself or trade it to a friend".
- **Server:** the definition must be tradable (NO_TRADE off) and consumed on use, raising PlayerMaxRootClaims by 1 up to
  8. The item id is still missing.

### L87 / L88 / L89 / L90 / L46 / L119 / L120
- **L87:** P1509 (3 days, previously 1 hour) and P1590 / forum 50269 ("Notification Window ... mail, upkeep reminders").
  The reminder appeared in the Notification Window [I]; no wording was found.
- **L88 / L89:** only P5796 / P5832.
- **L90:** only the item descriptions.
- **L46:** the only forum hit (53904) is unrelated.
- **L119:** forum 57649, a player: "Catapult triggers timer ... waits for projectile to get to wall. about 1 sec". No
  numbers.
- **L120:** forum 57065 (a player found healers hard), P4696 and P5271. No numbers.

### L91 / L108 — Weapon recipes: ADVANCED
- Forum 56713 (1 Apr 2016, the developers): "you will now only get 1 armor or 1 weapon recipe per day ... unlock all
  weapons and armor in under three weeks even if you do not use lumens".
- Forum 57170 (25 May 2016), line 282: "new weapons! Recipes for these will drop, or can be obtained via the Lumen
  Station". Line 430: items missing from the station "can be obtained as loot". Line 892 (the developers): "They ARE
  dropping but are not yet on the lumen station".
- Forum 57252:48 (7 Jun): "Some missing weapon recipes are now available at the Lumen Station".
- The 11 May changes (P5328-P5332) concern only the armour rewards of the 500-kill goals.
- **[I]** The four weapons were drop recipes. With no 0.695 station mapping they were drop-only by then, which matches
  RecipeBook.RandomUnknownRecipeItem.

### L96 — NEEDS-RUNTIME
- The bubble and the Yes/No indicator both need the actor in the +0x50 table (see L30).
- **Test:** send LightweightToFullNpc, then DisplayPropText with the creature guid, as the lead proposes.

### L97 — FFA PvPAllowed: ADVANCED [P tables]
- IgGeneralSetting row 62 has setting 11 "PvP Allowed" (573081) only for rule set 8 (Adventure; default 0, player
  adjustable).
- IgRuleSets HAS_FFA_TEAM_BY_DEFAULT is 1 for rule sets 2 (Deathmatch) and 7 (FFA Zone Control), and 0 for 5 (All
  Scoring).
- **Server:** the interim "PvPAllowed for 2 / 5 / 7" should be 2 / 7 [I]. Whether live forced PvPAllowed or used
  another path is still open.

### L99
- The 0.589 caves folder (211 files) and fileindex_589 have no 417-421 cave file either.
- **Next:** the client's runtime cave generation for a layer without a .cpa.

### L100 — Spire templates: ADVANCED
- Both installs ship the same two templates (identical md5); see the table. Every prop sits 0.8 m higher in
  portalspirebase.otd inside the same 310×170×310 box. That points to a re-cut base, not a per-landscape offset [I].

  | | base_portal_spire.otd | portalspirebase.otd |
  |---|---|---|
  | Hub 1871 | y 21.2 | y 22.0 |
  | Crystal | 19.7 | 20.5 |
  | Mailbox | 20.1 | 20.9 |
  | Other props | Crimson Parlor 2942, Replicator 2894, Repair 2895 | Replicator 2090; no Parlor or Repair |

- **Next:** compare each template's solid ground column under the Hub (otd voxel data) and see which one the server
  stamps (Spire.cs).

### L101
- No `+ 0xe) & 0x80`, `0xe) < '\0'` or `0xc) & 0x800000` test was found in the decompile. The only +0xe test on an
  item definition is part_0077:9085, which tests 0x40.
- **[I]** The client relies on the server: TransferItemResponse reason 0x22 opens the unique-equip dialog.

### L102 / L107 / L110
- These are user decisions. For L102, ClaimWalkOnTextWindow (as3_695) reads AdminTemplateGuid, AdventureStatus,
  ClaimName, ContestName, GameName and the instanced-game fields, among others.

### L104 / L136 — Waypoints: L136 FOUND [P]
- The input action "PlaceWaypoint" (context "WorldMap", part_0082 ~26600-26720) calls FUN_1430d89a0(DAT_145431410,
  position), unless the HUD state is 0x1c.
- FUN_1430d89a0 keeps **5 client-local waypoints** at +0x230..+0x250 and replaces the oldest (by +0x14) when full.
  Each one plays a composite effect at its spot (FUN_1436aabb0 → FUN_143d57960).
- **No packet is sent.** SetWaypointPacket 0x32/0x12 goes out only through Lua Ui.SetWaypoint, which no UI or pack
  calls.
- **Server:** nothing to answer.
- **L104 (ADVANCED):** live's `/goto waypoint` could not have used these waypoints [I]. Look for server-side named
  waypoints.

### L105 — Globals: FOUND [P]
- **DAT_144c410f0** (.rdata, raw) is f32 **1.0** (bytes 00 00 80 3f). In /runspeed (FUN_1433b2a20):
  - `fVar18` starts at 0.0;
  - **"default" sends 0.0** (LAB_1433b2ec5 sets fVar17 = fVar18 = 0);
  - "inc n" sends the current speed + n, and anything below 1.0 becomes 1.0;
  - "dec n" is clamped at 1.0;
  - an absolute n below 0 becomes 0.
  - In offline mode the client's own override DAT_145439f60 (BSS, 0 = none) gives "Player run speed reset to
    default.".
- **DAT_145462af8** is in BSS (beyond the raw size 0x21fa00). It has 123 RIP-relative reads and compares, no write,
  LEA or pointer (ripref.py, refscan), so its value is **0**.
- **Server changes:**
  - AdminCommands.Runspeed (AdminCommands.cs:324-342): **treat speed 0 as "default" and remove the override.** Today
    0 is stored as a 0 m/s run speed.
  - Fix the comment at :316.
  - "No guid" is 0 only; keep 0xFFFF… as a defensive alias.

### L106 — ClientTime: FOUND [P]
- The jump table at 0x1433ba644 holds RVAs of 7 cases: 0x1433ba349, 36b, 3dd, 3f9, 44f, 48d and 531 (rd.py dis).
- The packet carries mode at +0x20 and fields a / b / c at +0x24 / +0x28 / +0x2c.
- The target object is DAT_1454319a0 = **ClientGameTimeManager** (constructor FUN_1431da650):
  - +0xb4 is the f32 rate. Its default is 8.0 (DAT_144722208, a double) unless a setting with hash -0x7e31ddd1 (not
    in StringHashToValue) overrides it.
  - +0xc0 is paused, +0xc1 is the warp flag, and +0x48.. holds the current time.
  - vtable +0x28 is SetTime(h, m, s, bool).

| Mode | Client action |
|---|---|
| 1 set | SetTime(a, b, c, false) (integers) |
| 2 hour | f32 a hours → h = int(a) % 24, m = int(a·60) % 60, s = int(a·3600) % 60 |
| 3 pause | toggle +0xc0 |
| 4 sync | clear +0xc1 and +0xc0, rate = **8.0** |
| 5 warp | if f32 a ≠ 0: +0xc1 = 1, rate = a |
| 6 advance | now + a·3600 s (constant 3600.0 at 0x144722210) |
| 7 reverse | now − a·3600 s, mod 86400 |

- **Server:** echoing the packet is consistent. The world clock can follow the same model: default rate 8.0 game
  seconds per real second, which is a 3-hour day [I from the number].

### L109 — Events world: ADVANCED [P]
- TeleportSpireWidget.as:32 gives `Teleport.Worlds` the columns Name, GUID, Claims, ClaimCapacity and CurrentWorld.
- The native fill (part_0088 ~9550) takes each world entry as {i32 id, u32 **name text id** looked up through
  DAT_145431178 vtable +8, …, ClaimCapacity from entry[3]}. CurrentWorld is `game +0x378cc == id`.
- A world name must therefore be a client text id; 567016 "Events" is the only one.
- The Travel window's "Other Worlds" button (TeleportWidget.as:73, :155) calls Teleport.RequestTeleportLocations.

### L111 — Landmarks: ADVANCED
- Forum **57555** (Week End Wrap Up, 24 Jun 2016; not in the lead's searched list), line 18:
  - winners are placed "beside each spire";
  - "the world of Courage is now complete";
  - "We have slightly more landscapes than we had 2016 winners, so you'll see some of the original landmarks
    resurfacing here and there to fill in a few gaps", so landscapes without a 2016 winner got an earlier winner;
  - example: Fantal's "Aeronautics and Space Administration" on Courage – Timeworn Dunes;
  - the landmarks also appear in loading screens.

### L116 / L118 / L139 / L34 / L39 — already found in existing research
- **L116:** OPEN-LEADS "Found", first bullet: QuerySimpleVisibility (FUN_1440cd6d0) loops on a cycle after a plain
  teleport across visibility cells. Not fixable from the server; keep ShortTeleportDistance at 0.
- **L118:** the title says FOUND (2026-10-16).
- **L139:** see the L140 note: the 0.695 handler FUN_1431ca140 is `ret 0`.
- **L34:** the entry says found. **Server change:** fill EquippedAttachmentReply with the equipped items' attachment
  records (reader FUN_1430a0cc0).
- **L39:** leads-L39.md.

### L121 — Jump / fall: NEEDS-RUNTIME (+ADVANCED [P])
- The client's airborne test FUN_142e934c0 (part_0073:27268) checks:
  - jumping: +0x1450 & 0x20, or +0x145f & 2;
  - falling: +0x1450 & 0x40;
  - gliding: +0x1451 & 0x80;
  - or a negative controller vertical speed when +0x4a70 & 0x10.
- Its only caller is FUN_143325430, which is not an ability or item path, so the campfire refusal was server-side.
- **Probe:** log the type 0 / type 1 movement-body state uint on the server while jumping and falling. No Frida
  needed.

### L122 — Group member record: ADVANCED [P]
- The 0.695 row fillers are FUN_142df4460 (= 0.589 FUN_142d56350) and FUN_142df41b0 (= 0.589 FUN_142d560a0).

  | Data key | Source |
  |---|---|
  | `IsLinkdead` | member +0xf8 ≠ DAT_14543cd00; that global is BSS with 6 reads and no write, so 0 |
  | `WorldNameId` | **text lookup** of member +0x10c (DAT_145431178 vtable +0x10), "InvalidWorld" if absent |
  | `ZoneName` | **text lookup** of member +0x104, "InvalidZone" if absent |
  | `IsInZone` | +0x100 **and** +0x108 equal to the viewer's |
  | `Distance` | from +0x110 / +0x114 / +0x118 |
  | `InGroupBonusRange` | dist² < R², R from a client setting (DAT_145431198+0x3f0), default DAT_144c41210 = **200.0** |

- **Server changes (Game/Groups.cs MemberRecord, :509-532):**
  - write the landscape's **name text id** at +0x104 (today the zone id, which shows "InvalidZone");
  - write the world's **name text id** at +0x10c (e.g. Landscapes.WorldNameText; today 0);
  - keep +0xf8 = 0 for online members;
  - fix the doc comment, which says +0x10c is "looked up as a world id".

### L123 — Follow: ADVANCED [P]
- The 0.695 AddFollowing handler (part_0093 ~7340) uses reader FUN_14322da80:

  | Field | Offset |
  |---|---|
  | u16 op | — |
  | i16 sub | +0x10 |
  | u64 FollowingId | +0x18 |
  | bool result | +0x20 |
  | str FollowingName | +0x28 |

- It logs "Add following result=%d. FollowingId=%lld, FollowingName=%s.". On success it adds the entry to the
  following map (+0x232c88, FUN_143af9160) and refreshes (FUN_14370e410).
- It then calls Lua `OnCommerceAccountAddFollowing(result, id, name)`, which nothing shipped defines (see the L56
  group). RemoveFollowing is at part_0093 ~8005.
- **Server:** answer 0x34 / 0x35 with this reply, keyed by a u64 creator id.

### L126 / L142 — Lost claim server connection: L142 FOUND [P]
- The Claims S→C dispatcher (part_0069 ~14596, case 0x11) builds ClaimsPacketLostClaimServerConnection {u16 0xF9,
  i8 0x12}; reader FUN_142da8e50 reads the header only.
- It calls FUN_142db7970("Claim.LostClaimServerConnection") → CodeStringMappings.txt:97 → **562155** "Unable to
  process the request. Connection to the Build SiteServer is unavailable."
- The text is shown as Lua "SystemMessage" (FUN_1433f98e0 on game +0x32488) and as chat type 7
  (FUN_142ca7900(game, 7, …)). **Nothing is blocked client-side.** The same helper shows 18 other `Claims.Error.*`
  keys.
- **Server change:** when a claim or site request cannot reach the claim / Environment service, send the
  header-only 0xF9/0x12. It shows the same text that Building.cs:702 / RemoteEnvironmentServer currently sends through
  ZoneDisplayNotification.
- **L126:** 562155 is therefore live's LostClaimServerConnection text [P]. ZoneDisplayNotification's live texts remain
  open (neighbours 562148-562158 are upkeep and Founder's Pack texts).

### L127 — Ruin layers: ADVANCED
- FUN_143ca0fd0 (part_0146:34579) parses TerrainObjectDeploymentGroups.txt as text lines `<name><delim><int>` into
  `TerrainObjectDeployment` (RTTI 14494dca8; HashListMap<TerrainObjectDeployInfo>).
- It is created in FUN_1437aec80 (part_0121 ~28486) next to TerrainObjectDpoDefinitionManager and
  "TerrainCalendarDefinitions.txt". That makes it the engine's terrain-object (DPO) deployment, **not ruin layers** [I].
- The file is not shipped.

### L131 — "Other worlds": ADVANCED
- design/wiki/pages/Becoming a Luminary.wiki:6 (a goal step): 'Travel to "other worlds" and visit a landscape that
  doesn't have "Pioneer" in the name'.
- In the game's own wording, "other worlds" is the Travel window's Other Worlds button (tooltip " Teleport to any world
  on any server."), which leads to landscapes.
- **[I]** P5734 means other landscapes and biomes.

### L132
- rtti_695 has no navmesh library classes (no Recast, Detour, Havok AI or Kynapse). It has only the pathing monitor
  classes (ClientPathingMetrics, PathingMonitorState, AdminCommandPacketPathingDataMonitorUpdateRequest).

### L134 — Property key: FOUND [P]
- The key is the client's **one-at-a-time hash** (re/tools/strhash.py `oaat` = StringHashToValue manager
  FUN_14351fe20). Per character: `h = (h+c)*0x401; h ^= h>>6`. Then `h *= 9; h ^= h>>11; h *= 0x8001`. It is applied
  to the EditableProperties.STRING name exactly as written (lowercase).
- oaat("enable_teleportation") = **0xa632764b**; lookup2 gives 0x24387fab, which does not match.
- Other keys appear as literals in the 0.695 decompile:

  | Name | Key | Decompile hits |
  |---|---|---|
  | flinger_launch_speed | 0x423bc843 | 10 |
  | flinger_launch_angle_pitch_degrees | 0xd0aaeb7f | 4 |
  | flinger_trajectory_height | 0x7ad7ab4f | 10 |
  | moving_platform_speed | 0x321630f0 | 2 |
  | moving_platform_pause_delay | 0x30725695 | 2 |
  | prop_text_display_type | 0x45d3a430 | 2 |

- **Server change:** key spawn settings (+0x340 list) by `oaat(EditableProperties.STRING)`. Compute it in PropSettings
  instead of hard-coding the keys.

### L143 — ZoneCraftingWindow: ADVANCED [P]
- In the Crafting dispatcher (part_0077 ~8402, case 0x24), RecipePacketZoneCraftingWindow 0x42/0x24 is read by
  FUN_142f300e0 as {u32 (+0x10), u32 (+0x18, constructor default 0xFFFFFFFF), bool (+0x1c)}.
- **bool 0** → FUN_142f381b0: sends RecipeCraftWindowEnableRequestPacket 0x42/6 {u64 station guid, bool 0} and closes
  the window, with Lua HudStateManager:OnCloseRepairWindow for station type 6 or GameEvents:OnCloseCraftingWindow
  otherwise.
- **bool 1** → FUN_142f3c630(mgr, 1), which rebuilds the window.
- **[I]** This is the server's way to close or refresh a station window, for example when the station is removed or
  out of range.

### L144 — Grief: ADVANCED [P]
- The player's grief object is at +0x76920: +0x76920 points, +0x76928 level, +0x76930 time.
- GriefBase handler FUN_142ee1bf0:
  - sub 1 → FUN_142ee1d80 sets points and level (-1 keeps the current value) and prints "You have %d grief points.";
  - sub 2 → FUN_142ee1850 changes the level, prints "You have entered/left grief equipment lock." or "...grief
    suspension." and calls Lua HudHandler:OnGriefLevelChanged;
  - sub 3 resets.
- **Level ≥ 4 on the client** (part_0073 ~27160, ~27205, ~27557):
  - FUN_142e932b0 and its neighbour refuse the **"Knife"** and **"QuickUse"** ability slots;
  - abilities and items whose definition flag 0x10 is not set become unusable.
- The rule for earning points is still open (forum has no "grief points").

### Remaining UNCHANGED leads
L14, L15, L19, L20, L22, L23, L24, L25, L26, L27, L31, L32, L41, L42, L44, L45, L47, L48, L49, L50, L51, L53, L62,
L69, L70, L73, L74, L75, L76, L81, L82, L83, L92, L93, L95, L112, L113, L115, L117, L125, L128, L129, L133, L137,
L138, L140.

- **Searched this pass:** forum/all, forum_raw, wiki and patchnotes.tsv by each lead's keywords, plus the pack and UI
  greps above where they applied. Nothing new turned up. Each lead's own "Try next" still stands.
- L137 needs archive.org, which this pass did not use (no browser).

---

## 3. FOUND leads that unlock server work

1. **L105** — AdminCommands.Runspeed: speed **0.0 = reset to default** (today it pins the admin at 0 m/s). The client
   floors inc/dec at 1.0. The invalid guid is 0.
2. **L61** — the client exits ("Timed out while waiting for a relogin session.") if 0xF7 does not come within 60 s.
   Always answer 0xF6 within that time, with a fallback when the world-channel service is down.
3. **L134** — the editable-property key is the one-at-a-time hash of the EditableProperties name (0xa632764b =
   enable_teleportation). This unblocks non-default mover settings: flinger speed / angle / height, platform speed /
   pause, one-way pads.
4. **L77** — OffGridPasteClientRequest 0xF4/5 arrives on **Zone**; writer FUN_14315b130 gives the body order. Add the
   handler and forward it as OffGridPasteZoneRequest.
5. **L142** (+ L126) — send header-only **0xF9/0x12** when the claim / Environment service is unreachable; the client
   shows 562155 by itself.
6. **L106** — all seven ClientTime modes decoded. The client clock's default rate is 8.0, which a server game clock
   can match.
7. **L52** — the captured prop list is valid for 0.589 too (same reader, byte-exact parse), so the 0.695-only gate can
   go.
8. **L30** — the question bubble round trip is fully proven (already built; no change).
9. **L71 / L78** — dead senders: no gateway Logout handler and no ResetAllCells / ResetTemporaryCells handler needed.
10. **L136** — map waypoints never reach the server: nothing to build for Ctrl+click waypoints.
11. **L55, L29, L40, L12** — confirmations only: sentinel 0, a dead field, already built twice.

Strong ADVANCED items that also imply small server changes:

- **L122:** write name **text ids** at group record +0x104 / +0x10c. Today the zone id produces "InvalidZone".
- **L37:** send the team count at +0x15c.
- **L97:** set PvPAllowed for rule sets 2 / 7 only.
- **L10:** use CaveRewards.TooSoonToUse 574500 for a locked-out Lost Crystal.
- **L86:** the Build Site Expansion definition must be tradable and consumed on use.
- **L123:** answer Follow / UnFollow with the AddFollowing / RemoveFollowing layout.
