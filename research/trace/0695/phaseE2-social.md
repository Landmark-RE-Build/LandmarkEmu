# Phase E part 2 — agent pe2-social (2026-10-26)

Work list: OPEN-LEADS L180, L181, L185, L196 (docs/observer-sync.md OL-1..OL-6), L197 (docs/request-variants.md).
Brief: trace/0695/agent-phaseE2-brief.md. Code: worktree `<workspace>\wt-pe2-social`, branch `local/pe2-social`,
commits **b0c86c6** (code, tests, docs) and **e132e62** (docs/gameplay.md). `dotnet test LandmarkEmulator.sln`:
1966 passed, 0 failed, 0 skipped. `python tools/wiki_reference.py` run (Settings-Reference regenerated; its three
findings — DIGSHAPE_DUMP, LANDMARKLOCAL_ROOT, a README "Page-Name" link — are not from this branch).

Addresses are 0.695 `Landmark64.exe` unless marked 0.589. [P] proven, [I] inferred, [W] wiki.

---

### L180. New-player site slate slab
- **Verdict:** CLOSED (best evidence)
- **Answer:**
  - The slab assets are the client's `CommonData\Voxel\templates\claim1x1/2x1/3x1/3x3.otd` (agent-leadsC.md §L180):
    5-voxel slabs, claim3x3 = 525 × 524 voxels, exactly the launch build-site footprint (210 m) [P size]. No client
    code, data table or script loads them (no "claim1x1" / "claim3x3" / ".otd" in the 0.695 decompile;
    VoxelSystemTemplates.txt lists only starter_* / poi_*) [P negative], so placing them was server work, like the
    cavern ruins.
  - Live placed a "slate slab" on new players' automatic claims and fixed its placement on 30 Jun 2016 [P dev,
    forum/all/2016-06-29-57596 line 58]. New-player sites were automatic since 21 Mar 2016: Pioneer surface first,
    then an isolated Pioneer cavern, then a classic landscape (P 2016-03-21-56259 lines 58 / 74 / 804) [P].
  - The material: the templates store index 53. In 0.589 and 0.695 VoxelMaterials it is ARRAY_INDEX 53 = "Raw
    Amaranthine" (ID 427) and ID 53 = "Biome_Ashfang_Rubble01"; **new this pass:** the oldest material table the client
    still ships, `CommonData\Voxel\VoxelMaterials1.xml` (55 VoxelFarm materials), has id 53 = "Biome_Everfrost_Snow02",
    and `VoxelMaterialRemaps.txt` maps neither 53 nor 427 [P]. No table makes 53 a slate, so the stored index is
    stale and live's slate came from the server [I strong].
  - Which template and the exact height were server-side (2016 server data); nothing local records them.
- **Searched:** design/forum/all + patches (every "slab", "slate", "new player claim", "pioneer" hit in 2016 threads,
  incl. the 57596 thread's replies: only LM-1497 about Darkest Worked Slate textures), design/wiki/pages (no slab),
  gameData695 VoxelMaterials / VoxelMaterialRemaps / VoxelSystemTemplates, CommonData/Voxel/VoxelMaterials1.xml,
  re/out/fileindex_589 / 695 (no other VoxelMaterials), the 0.695 decompile (strings), agent-leadsC.md §L180.
  Web: the wiki image and Verlin EQL's video (agent-leadsC) could not be reached — web.archive.org answered every
  request this session with its bot block page (CDX index requests still work); Common Crawl's index had no capture
  of the related landmarkthegame.com pages where it answered. New lead N3.
- **Server:** matches the best evidence already (built 2026-10-18, leadsfix 40ebd6d): claim3x3 stamped solid-only under
  new Pioneer surface sites, flush with the ground, material 26 Worked Slate; settings PIONEER_SLAB
  (claim3x3 | claim1x1 | off) and PIONEER_SLAB_MATERIAL (26; 0 = the stored index). This branch only adds the
  VoxelMaterials1.xml evidence to the PioneerSlabMaterial comment (WorldServerConfiguration.cs).
- **OPEN-LEADS text:** `- **CLOSED 2026-10-26 (phaseE2-social, best evidence):** the claim*.otd slabs are server-side assets (no client code loads them); no material table makes index 53 a slate (0.589/0.695 VoxelMaterials, VoxelMaterials1.xml id 53 = Biome_Everfrost_Snow02, no remap), so live's slate, template and height were server data. Built as claim3x3 / material 26 behind PIONEER_SLAB / PIONEER_SLAB_MATERIAL. Visual check of live's slab: new lead (archive image / video, Wayback blocked).`

### L181. Voice chat (Vivox, Voice 0xB9)
- **Verdict:** CLOSED (best evidence)
- **Answer:**
  - The protocol is decoded [P] (agent-leadsC §L181, docs/protocol.md): S→C 0 VoiceLogin {account, password, server
    URL, unused}, 1 VoiceJoinChannel {u8 type, URI, password, display name}, 2 VoiceLeaveChannel {u8 type}; C→S 6
    RequestCustomChannel {room, password}; channel types 0 Echo … 7 Custom (FUN_143c6dff0).
  - What live did [P dev, patch notes]: 17 Dec 2014 "Voice chat comes to Landmark!" — off by default; switched on, the
    player joins the **Proximity** channel, a **guild channel named after the guild** and a **party channel**;
    push-to-talk or voice-activated (forum/patches/2014-12-15-51046 lines 619-660). Proximity falloff 20 m instead of
    10 m (23 Dec 2014, 51622 line 30); channels no longer always on regardless of the check box (27 Jan 2015, 52848
    line 198); rebindable proximity push-to-talk (5 Mar 2015, 53766 line 114).
  - Live's Vivox account names, server URL and channel URIs were Daybreak's Vivox account's, issued by live's server
    [P negative for the client]: the client shows a participant by the Vivox display name it is given
    (FUN_1430d5a00 → FUN_143b16e60, a UTF-8 → UTF-16 copy) and parses no URI; its only built-in host is the debug
    console's "enlp.vivox.com" (createconnector, "%s/api2/", "sip confctl-2@%s"). No data table, script, wiki page or
    forum post names them.
  - Sending no 0xB9 is the client's clean "voice off" state (FUN_1430cf6b0 never connects without a VoiceLogin) [P];
    an empty VoiceLogin would make it connect to an empty URL.
- **Searched:** re/decomp_all_695 (every "vivox", "sip", "api2", "confctl" string and the participant / session
  handlers), UI/Default/Data.xml (the Voice Chat options), design/forum/all + patches (every "vivox" / "voice chat"
  hit: 2014-03-26-26034, 2014-04-09-30814, 2014-04-29-34413, 2014-12-12-50967, 2014-12-15-51046, 2014-12-23-51622,
  2015-01-26-52848, 2015-03-05-53766, 2015-05-22-55052), design/wiki/pages (Chat), agent-leadsC.md, docs/protocol.md.
- **Server:** matches: no 0xB9 sent; C→S sub 6 logged with the one-per-session "Voice chat is not available on this
  server." (VoiceHandler.cs). docs/protocol.md now records live's channel behaviour and why the account / URI scheme
  is server-only. A real voice service is new lead N4. (trace/systems/social.md:45 and trace/architecture/
  social-service.md:168 still say "not needed" / "out of scope" — outside this agent's write area; reword them to
  "no voice service; no 0xB9 sent (the client's clean voice-off state); layout in docs/protocol.md".)
- **OPEN-LEADS text:** `- **CLOSED 2026-10-26 (phaseE2-social, best evidence):** live's Vivox account names, URL and channel URIs were server-only (the client takes URIs and display names verbatim, parses nothing; only a debug host "enlp.vivox.com" is built in). Live behaviour recorded from the 2014-15 notes (off by default; Proximity 20 m, guild channel named after the guild, party channel; push-to-talk). Server sends no 0xB9 = the client's clean voice-off state. Self-hosted voice: new lead.`

### L185. Player Studio: approval / rejection counts, reviewer rules, staff queue
- **Verdict:** CLOSED (best evidence)
- **Answer:**
  - Rules [P dev, 18 Jun 2014 P1207-P1210; 26 Jun / 2 Jul 2014 statuses]: reviewers bought ≥ 1000 SC; at most two
    review chances a day; a rejected item goes to a Customer Service queue where staff uphold or override the
    players' verdict. Wiki [W]: anonymous review; "After simple 'thumbs up' from enough players" the item is listed; a
    reviewer who rejected against the two rules, where staff disagreed, "will stop getting items to review".
  - Client [P]: the vote codes (approve 1; rejections 2-7 by type / subtype, FUN_1435e9f30); no text, packet or table
    gives the counts (text_695, CodeStringMappings, the Commerce readers); the reject type / subtype dropdowns' data
    sources are never filled by the client (agent-leadsC).
  - The counts ("enough players") were server data, recorded nowhere we can read.
- **Searched:** design/forum/all + patches (2014-06-18-41434 and siblings, 2014-06-25-42258, 2014-07-02-42830, every
  "Player Studio" thread), design/wiki/pages (Player Studio, Showcase, Designs), re/out/text_695.tsv,
  CodeStringMappings, the 0.695 Commerce readers, re/as3 + as3_695 PlayerStudioItemReview. Web: the archived
  landmarkthegame.com/player-studio-faq (snapshot 20150610094924) and /player-studio (20140706212019) exist in the
  Wayback CDX index, but every page fetch got the bot block page this session; Common Crawl's 2014-2016 indexes had
  no capture where they answered. New lead N2.
- **Server:** changed in local/pe2-social b0c86c6 — the wiki's reviewer rule is built: a rejection is "overruled" when
  the product is listed with fewer approvals than PLAYER_STUDIO_APPROVALS (staff listed it, not the peers); a reviewer
  with that many overruled rejections is offered no review and their votes are refused. Shared rule
  `Internal/Commerce/StudioRules.MayStillReview / Overruled`; world in-process path in `Commerce.MayReview →
  NotOverruled` (one count query off the tick, kept 10 min per reviewer, dropped at every staff approval); commerce
  service path `ProductService.Overruled` + `CommerceStore.OverruledRejections` (per realm). Setting
  **PlayerStudioReviewerOverrules / PLAYER_STUDIO_REVIEWER_OVERRULES**, default **1** (live per the wiki; the count is
  ours), 0 = never; wired in WorldServerConfiguration, CommerceServerConfiguration, ServerSettings.Table, both compose
  files, .env.example, both example JSONs, docs/docker.md, docs/commerce.md, wiki_reference.py. The counts stay 3 / 3
  (PLAYER_STUDIO_APPROVALS / _REJECTIONS) and the staff queue, spend rule, two-a-day rule and anonymity were already
  built (leadsfix 1104357 and earlier). Tests: ReviewSpendRuleTests.OverruleCountKnown_AnswersAtOnce,
  ObserverLeadsRuleTests.OverruledRejections_HoldTheReviewerBack.
- **OPEN-LEADS text:** `- **CLOSED 2026-10-26 (phaseE2-social, best evidence; built b0c86c6):** the counts were server data (no client text / packet / table, no dev post, wiki "enough players"); 3 / 3 stays ours (PLAYER_STUDIO_APPROVALS / _REJECTIONS). The wiki's last rule is built: reviewers whose rejections staff overturned get no more reviews (PLAYER_STUDIO_REVIEWER_OVERRULES, default 1). Archived Player Studio FAQ: new lead (Wayback blocked).`

### L196. Observer-sync leftovers (docs/observer-sync.md OL-1..OL-6)
- **Verdict:** RESOLVED (OL-1, OL-3, OL-4 built; OL-5, OL-6 match) / OL-2 RESOLVED in code, with a two-client check
  listed under Runtime captures; combat states CLOSED (best evidence).
- **Answer:**
  - **OL-1 guild tag [P].** The nameplate tag (entity +0x21a8, `PlayerNameWithGuildTag` "[*player*]<br>[*guildTag*]",
    `GuildTagFormat` "<[*guildTag*]>") is written only by FUN_142ec2610, called from 0xDF PlayerUpdatePacketGuildTag
    {u64, string} (reader FUN_142c4c0a0, handler FUN_142cdf330), the full character's third string (+0xf8, case
    0x5a), and, for the local player, the guild's **alias** on every GuildData (FUN_142eedd60) and GuildRename reply
    (FUN_142ef4ba0, "UI.GuildTagChanged"). The alias is guild core string 2 (+0xa8; core reader FUN_142ee4b50: id,
    name +0x08, alias +0xa8, +0xc0, +0x120, +0xd8, +0xf0, description +0x108). The client's only alias request,
    Guild.Rename (FUN_1435b5f90 → GuildRenamePacket 0x83/3), fills name **and** alias with the one new name; Create
    (FUN_1435b5c40) sends the name alone (≤ 64 chars); no 0.589 / 0.695 window has a tag field — the 0.695 AdminView
    "Tags" box is Guild.SetTag (FUN_1435b61f0 → GuildUpdateAppDataField 0x83/0x11, GuildGameData XML **GuildKeywords**,
    1,024-char search keywords; FUN_143c4f420 / FUN_143c4f1d0), and no UI calls Guild.Rename. So the tag is the guild
    name [P requests; I live's server]. The option General.DisplayPlayerGuildNames defaults to 1 (FUN_142b21620)
    [P]; live showed tags (hotfix 7 Aug 2014 P1525) [P].
  - **OL-2 dead body [P mechanism, I live's packet].** The player network's ControlParameters|State_Dead (26
    conditions in PlayerAnimationNetwork64.mrn, re/out/l117/conds_player.txt) is set every frame by the character
    update FUN_142eb1950 from the knocked-out test: 0x142eb24de `lea rcx,[r14+0x250]; call [rax+0xb0]` (FUN_142ea9ef0:
    +0x250+0x7d0 = entity +0xa20 bit 7) → xmm2 = 1/0 → `call [rax+0x98]` with "State_Dead"; the next blocks set
    State_Stun (bit 6), State_KnockedDown (38), State_KnockedBack (9), State_Snared (39), State_Frozen (13),
    State_Staggered (23) [P disassembly]. AnimationSelection 156 "Death" (State_Dead 1, AnimationSelectionVariables row
    431) would be overwritten on the next frame [I]. The remote KnockedOut handler is empty (FUN_142ead890). The
    queued state delta's apply FUN_142eb6a10 runs the death transition (PlayerDeathVfx — StringHashToValue 0x40a805d9,
    0 in 0.695 —, FUN_142f88950 for a remote character, vtable +0x490, FUN_142eb12d0) and the revive (vtable +0x488)
    [P]. So live set bit 7 on observers through UpdateCharacterState / Delta [I strong: the only writer besides the
    spawn flags]. ProxiedCharacter::SetupActorGroup (FUN_142ec6e20, same logic as 0.589 FUN_142e280f0) reads no state
    bit [P]; the 2026-10-12 0.589 observation of a dropped entity used the whole-word 0x0F.
  - **OL-3 calculated stats [P].** FUN_142e5d990 applies the full character's stat list to any character: stat 4 →
    FUN_142ec5a40 → FUN_142e99950 (the character's self-light on / off and radius), stat 5 → +0x740 (also set for remote
    characters only by PlayerUpdate ExpectedSpeed 0x11, case 0x10), 0x1d → vtable +0x318, 0x25 → +0x4a74. With no
    SEND_TO_OTHERS stat, a snapshot taken during a buff never ended on the observer.
  - **OL-4 build preview [P].** FUN_1430838d0 (0.695 receiver) case 5 builds the preview mesh from PreviewSetup
    (FUN_143688340 / FUN_143688e30 / FUN_143689450 / FUN_143684550 on the manager at +0xf8); case 6 only moves it, and
    FUN_143687a50 looks the guid up and returns when there is no preview. Another cursor kind clears it
    (FUN_143082aa0 ≠ 5 → FUN_143080fa0). Our relay kept only the newest packet: a newcomer got the update without the
    setup, and an update arriving within 100 ms of its setup replaced it for everyone.
  - **OL-5 mining dust [P].** ResponseObserved's handler (FUN_143166bd0) has operations 0-6 only, all building brushes;
    the miner's own client plays the swing selection 226 from CastSuccess (FUN_142d5dfa0 → FUN_143cd5e90) — the same
    selection observers get as AbilityPlayAnim — and the terrain change; MiningDecalVisualEffect is the miner's ring
    UI; VoxelMaterials / VoxelMiningResources have no effect column. No client message shows a dig effect to others.
  - **OL-6 crafting stations [P].** Every Crafting::Recipe* reply (dispatcher in part_0077.c; RecipeStartResponse case
    2 → FUN_142f39150 / FUN_142f37fe0) drives the crafter's window only; AnimationSelection.txt has no crafting
    selection. Nothing exists to show a station in use to others [I strong for live].
  - **Combat states for late arrivals.** The full character has no effect list (blob +0x138 = the resource list,
    FUN_143b8aef0 → entity +0x4268; +0x178 → vtable +0x238; +0x20 attachments), so only a fresh PlayCompositeEffect
    could show a 2-10 s state to a newcomer; whether live re-sent it is server-only [I]. Kept as is.
- **Searched:** re/decomp_all_695 (functions above, the zone / PlayerUpdate / Guild / WorldEditing / Crafting
  dispatchers, every "State_*", "GuildTag", "alias", "GuildKeywords", "DisplayPlayerGuildNames" use), capstone on the
  0.695 exe (FUN_142eb1950, FUN_142ec5a40), re/decomp_all (0.589 SetupActorGroup, FUN_142e130e0), re/as3 + as3_695
  (GuildWindow AdminView, GuildWrapper), UI/Default/Data.xml, gameData695 (AnimationSelection, AnimationSelectionVariables,
  StringHashToValue, CodeStringMappings, CalculatedStatDefinitions), re/out/l117 (mrn conditions), design/forum (guild
  tag hotfix, guild threads), design/wiki/pages/Guild.wiki, trace/systems/abilities-combat.md, docs/gameplay.md,
  MODLOG 2026-10-12 / 10-14.
- **Server:** changed in local/pe2-social b0c86c6 (+ e132e62 docs):
  - OL-1: `Game/Social/GuildTags.cs` (TagOf = guild name; 0xDF to the landscape incl. the player on every change);
    Guilds.cs create / join / Remove / Disband call it; RemoteSocialService sends it when SocialStateForZone changes a
    character's guild; GuildData core alias = name in Guilds.BuildData and SocialServer GuildService.BuildData;
    `PlayerUpdateLightweightToFullPc.GuildTag` (third string) filled by WorldSession.BuildLightweightToFullPc. No
    setting (the tag is display; the client's own option hides it).
  - OL-2: `Game/DeadBodies.cs`: UpdateCharacterStateDelta {set bit 7} to the landscape (not the victim) at a death
    (Health.Damage), to newcomers (Health.IntroduceDead), {clear bit 7} to everyone told on Revive / HandleRespawn /
    a site game's respawn / dev revive; never to the dead player's own client (the 0.589 crash). Setting
    **DeadBodiesVisible / DEAD_BODIES_VISIBLE**, default **true** (live; wired like BlockStanceVisible incl.
    ServerSettings.Table, compose, .env.example, example JSON, docs/docker.md, docs/admin.md).
  - OL-3: `Abilities.CalculatedStatsForOthers` (no Buffs / Consumables changes) for the full character.
  - OL-4: WorldEditing keeps the setup (`Editor.PendingSetup` / `Setup`), sends it before the update that follows it
    and to newcomers before the newest update; another cursor kind (not a Clear flag 1) ends it.
  - OL-5, OL-6, combat states: no change (match).
  - Tests: `LandmarkEmulator.Tests/Rules/ObserverLeadsRuleTests.cs` (0xDF layout, full-character third string,
    landscape broadcast, death set / newcomer / clear, setting off, stats without buffs, preview setup ordering).
  - Docs: docs/observer-sync.md (table, closed leads, checklist items 9-11), docs/protocol.md (0xDF), docs/gameplay.md.
- **OPEN-LEADS text:** `- **CLOSED 2026-10-26 (phaseE2-social; built b0c86c6):** OL-1 tag = guild alias = guild name (Guild.Rename sends the name as alias; SetTag is search keywords) → 0xDF + full character + GuildData alias (Social/GuildTags). OL-2 State_Dead follows state bit 7 every frame (FUN_142eb1950 → FUN_142ea9ef0) → Delta bit 7 to others / newcomers, cleared on revive (DeadBodies, DEAD_BODIES_VISIBLE; two-client check pending). OL-3 full character without temporary buffs. OL-4 preview setup kept and sent first. OL-5 / OL-6 match (no client channel). Combat states: best evidence, kept.`

### L197. Request-variant leftovers (docs/request-variants.md)
- **Verdict:** RESOLVED (leadership transfer, RankSetDescription, contest feedback mail, vendor ListItem) / CLOSED (best
  evidence) (right-click rows, SetItemPriceBySlot)
- **Answer:**
  - **Old leader's rank [P].** FUN_142ef91f0 (Promote) offers "Transfer Guild Leadership" only when the target's next
    rank up (FUN_143c4ed20 → rank +0x110) has the leader rank's id (+0x50 compared with the guild's leader rank at
    +0x31f8); the confirm (FUN_142efadc0) sends the kept promote packet. So the new leader always comes from the rank
    directly under the leader, and "the new leader's former rank" (ours) = "the rank under the leader". The client
    even stores that former rank id at guild manager +0xec for the confirm (written at FUN_142ef91f0, otherwise only
    initialised in FUN_142ee9400, never read) [P].
  - **Right-click rows.** RadialMenuButtons.txt / RadialMenuTypes.txt have no condition column; the response carries
    per-row {sort, button, u8 selectable} (reader FUN_14372cdf0), so live could grey rows or omit them — server-only;
    no capture, wiki, forum or patch note lists them.
  - **Guild.RankSetDescription [P].** FUN_1435b8b10 checks ModifyRanks and that rank arg 2 exists, then fills only the
    third string of GuildDescriptionUpdatePacket 0x83/0x0F exactly as Guild.SetDescription (FUN_1435b64e0); no 0.589
    or 0.695 UI calls it (as3, as3_695, UI/Default, Resources/Scripts). The packet cannot name a rank.
  - **LuaClaimVendor.** ListItem's arguments are proven by the 0.695 developer test window `UI/TestMod/TestGrid.lua`:
    `LuaClaimVendor:ListItem( data.Slot, data.Quantity, 0 )` — bag slot and count; price 0 opens the client's "How
    much would you like to list this item for?" dialog (FUN_1435eeeb0), which calls ListItem again with the price
    [P]. SetItemPriceBySlot (FUN_1435ef390 → FUN_14337bae0 {vendor, u32 slot, u64 listing 0, u32 price}) has no
    caller anywhere (TestGrid.lua included) and the vendor list's rows carry no slot (FUN_143379e40: listing guid,
    Name, Description, ItemID, Icon, Quantity, Price, ListingFeePaid) [P]; live made the vendor booth decorative on
    1 Apr 2016 (P4378).
  - **Feedback mail to another world's contest winner [P].** Characters are global: every Docker world shares
    `lmemu_character` (docs/docker.md "db"; SocialCharacterModel: "lmemu_character.character.id is one u64 for every
    world"), so Mail.CharacterNameOf finds any world's character, and the mailbox is the social service's.
- **Searched:** re/decomp_all_695 (Guild natives FUN_1435b5c40 … FUN_1435b9cd0, FUN_142ef91f0, FUN_142efadc0,
  FUN_143c4ed20 / FUN_143c4edc0, the radial reader, the LuaClaimVendor natives and the ClaimVendor handler
  FUN_14337a260), re/as3 + as3_695, the 0.695 install's UI/Default and UI/TestMod Lua, Resources/RadialMenuButtons.txt
  and RadialMenuTypes.txt, design/forum (guild leadership: only party-leadership notes and the "rank title for guild
  leaders" fix), design/wiki/pages/Guild.wiki, docs/docker.md, LandmarkEmulator.Database.Social.
- **Server:** matches for all five; b0c86c6 only rewrites comments / docs: Vendors.cs (ListItem proof, SetPrice's
  two senders), docs/request-variants.md (closed leads, table statuses). Right-click rows stay "leave out rows that
  cannot apply" (UI, not a gameplay rule; no setting). VENDOR_SHOPS stays off (live).
- **OPEN-LEADS text:** `- **CLOSED 2026-10-26 (phaseE2-social):** transfer only from the rank right under the leader (FUN_142ef91f0 checks the next rank is the leader rank), so ours = live [P]; RankSetDescription sends exactly SetDescription's packet and has no caller [P]; contest feedback mail: characters are global (shared lmemu_character) [P]; vendor ListItem = (bag slot, count) per UI/TestMod/TestGrid.lua [P], BySlot has no caller and rows carry no slot (booths decorative since P4378); right-click rows: server-only, kept (best evidence).`

---

## New leads

### N1. The dead player's own death pose (state bit 7 on the local entity)
- **Need:** whether the 0.695 client survives UpdateCharacterStateDelta {set bit 7} on its **own** entity (live must
  have set it: the local network's State_Dead comes from the same bit, and ReadyToRevive is ignored unless the dead
  entity passes IsKnockedOut — trace/systems/abilities-combat.md); today the dead player stands on their own screen.
- **Searched:** the 0.589 test (2026-10-14) crashed at 0x142e13533 in 0.589 FUN_142e130e0 — the block gated by state
  bit 40 / +0x1461 & 0x40 and +0x947 (0.695 FUN_142eb1950 ≈ lines 584-605, the distance test against the local
  player) — so the crash may come from that test's mask, not bit 7 [I]; the 0.695 local transition in FUN_142eb6a10
  (+0x1452 & 2 branch: FUN_1431cae00, vtable +0xf8 → +0xf9, FUN_142d67090, FUN_142d4e890, vtable +0x478,
  FUN_142ec9f70) is not traced to the end.
- **Try next:** runtime capture (below); then send the Delta to the dead client too (DeadBodies).

### N2. Archived Player Studio FAQ (L185 counts)
- **Need:** live's approval / rejection counts, if the FAQ states them.
- **Searched:** everything in L185; Wayback CDX lists landmarkthegame.com/player-studio-faq 20150610094924 and
  /player-studio 20140706212019, but page fetches (id_ and normal, curl) returned the bot block page all session;
  Common Crawl 2014-2016 indexes: no captures where they answered (several 502 / 504).
- **Try next:** fetch those two snapshots once Wayback answers again (one page, slow, as forumscrape.py --slow);
  help.daybreakgames.com Player Studio articles in the CDX index.

### N3. Live's slate slab, seen (L180 template and height)
- **Need:** which slab (3x3 plus vs 1x1) and its height on a 2016 Pioneer site.
- **Searched:** as L180; Wayback blocked this session.
- **Try next:** the wiki image Landmark-pioneer-landscape.jpg and Verlin EQL's Pioneer video (youtube Lj7o-3o93Kg),
  2016 Pioneer screenshots on the archived forums.

### N4. A voice service for self-hosted servers (L181)
- **Need:** a Vivox-compatible endpoint the 2016 SDK (vivoxsdk_x64.dll, "%s/api2/", SIP) can log into, and the
  server-side account / channel-URI scheme to hand out with VoiceLogin / VoiceJoinChannel.
- **Searched:** the client (protocol decoded; no URI parsing; only "enlp.vivox.com" debug host); no open-source Vivox
  server is known.
- **Try next:** whether a current Vivox (Unity) developer account accepts the 2016 SDK's connector / login requests;
  then a VOICE_* setting set (off by default, as there is no service) that sends VoiceLogin + Proximity / Guild /
  Group channels as live did.

## Runtime captures needed

1. **Dead body on an observer (L196 OL-2, the code in b0c86c6):** two 0.695 clients on one landscape (TestPlayer + the
   TestPlayer2 test client). A dies (fall / creature). Watch on B: A falls and stays down; B relogs next to the body: still
   down, and B's right-click on A offers "Revive Player"; A picks Revive Here: A stands up on B. Hook (optional):
   FUN_142eb6a10 on B to see the delta applied (entity +0xa20 bit 7 before / after) and FUN_142f839d0 to make sure the
   entity is not dropped. If B loses A's entity, set DEAD_BODIES_VISIBLE=false and report.
2. **N1, own death pose:** on a test copy only, send {set bit 7} to the dead client itself (dev hook or a one-line test
   build) with Frida on FUN_142eb1950 (catch an access violation, log r14+0xa20 and r14+0x4a68 / +0x947-equivalent),
   then the death pose and the ReadyToRevive accept (FUN_142ec9910) — tells whether the 0.589 crash was bit 7 or the
   test's mask.
3. **Guild tag (OL-1):** two clients: A creates a guild → "<name>" under A on both screens; A leaves → gone on both.

## Notes for the coordinator
- trace/systems/social.md:45 and trace/architecture/social-service.md:168 still call voice "not needed / out of scope"
  (outside this agent's write area).
- TESTING-NEEDED.md: the three runtime items above and docs/observer-sync.md checklist items 9-11.
- The branch adds two settings: DEAD_BODIES_VISIBLE (world) and PLAYER_STUDIO_REVIEWER_OVERRULES (world + commerce).

## Summary

All five leads have verdicts. L196 is resolved in code: the guild tag under names is the guild's alias, which the
client's own requests make the guild name, now sent by 0xDF, in the full character and as the GuildData alias; a dead
player's body is laid down on other clients through entity state bit 7 — the only thing the 0.695 animation network
reads for State_Dead every frame — with a DEAD_BODIES_VISIBLE toggle and a two-client check still to run; observers no
longer receive buffed calculated stats that could never be reset; template previews keep their setup for viewers and
newcomers; mining dust and crafting stations were confirmed to have no observer channel in the client. L197 is closed:
the leadership transfer always promotes from the rank right under the leader (so the old leader's rank matches),
RankSetDescription cannot name a rank, contest feedback mail works across worlds because characters are global, and
the vendor ListItem arguments are now proven by the client's own TestGrid.lua, while SetItemPriceBySlot and the
right-click row rules are server-only. L181 and L180 are closed on best evidence (live's Vivox scheme and the slab's
exact template were server data; VoxelMaterials1.xml adds that no table ever made index 53 a slate), and L185 is closed
with the wiki's last reviewer rule built behind PLAYER_STUDIO_REVIEWER_OVERRULES while the counts stay 3 / 3. Four new
leads record what only the web or a runtime capture can still settle (own death pose, the archived Player Studio FAQ,
pictures of the slab, a voice service).
