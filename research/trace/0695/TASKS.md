# 0.695 working task list

Every gap between the live game and LandmarkEmu, with where it came from. Work through it top-down within each
section; tick an item only when it is built AND verified by the client's own reaction (see memory: verify client
results). Sources: `P####` = trace/0695/patchnotes.tsv item (date and thread given, since ids shift when sources
are added); `wiki:` = a design/wiki page; `client:` = the 0.695 client (CLIENT-DIFF.md).

Status: `[ ]` to do · `[~]` partly built / needs checking · `[x]` done and verified.

Started 2026-10-15. Branch: `client-0.695`.

---

## 0. Running the 0.695 client at all

- [x] **Auth LoginRequest has 13 more bytes in 0.695.** Not a layout change: the packet is six strings in both builds
      (writer FUN_143a54110); the reader took the third / fourth as u32s, so 0.695's locale "en_us" left 13 bytes.
      Reader fixed 2026-10-15 (L9 found).
- [x] **PlayerUpdate (0x26) sub-opcode is a u32 in 0.695, both directions (was u8)** (done: C.1, ClientWire695) — blocks every PlayerUpdate the
      server sends (trace/0695/packets-695.md "Changed layouts" A1; verified by the coordinator: 0.695 header writer
      FUN_142c13350 writes 4 bytes after the u16, 0.589 FUN_142b79bb0 writes 1; S→C dispatcher FUN_142cdfa60). For 0.695
      write `26 00 ss 00 00 00`; C→S bodies start 3 bytes later (WorldSession.cs ~1129 `data[2]`).
- [x] **Sub-opcodes renumbered in 0.695** (done: C.2 tables; bodies C.3 / C.4) (packets-695.md A2-A5): Claims >= 0x45 +2 (PropLimits 0x5F → 0x61), Mail
      >= 4 +1 (all of Mail.cs but ComposeRequest), InGamePurchase >= 0x30 +1 (Commerce, SiteScreenshots, Point-of-Need
      upkeep), InstancedGame >= 0xD +1 (0.695 SetConfiguration 0xF would hit EnableGame). Body changes (site records
      + u64 Play Mode Rules flags, site-game block + u32, design summary / data reply, DisplayPropText question
      fields, InstancedGame packets) in packets-695.md B. Full family × sub coverage: agent #10 → coverage-695.md.
- [x] **Client behaviour changes that break 0.589-era server features on 0.695** ✅ all nine checked 2026-10-16: B1 / B2 / B9 (7bb5e1f), B3 (rounds end in state 7 StateVictory for 10 s; observers kept apart, InstancedGames.Observers), B4 / B5 (Barbershop.cs: unlock-gated tints, PlayerSet tail, 0x11C/3 → sub 4), B6 / B7 (verified on the client), B8 (3.7 ExportedTemplate categories) — (trace/0695/functions-695.md
      "Breaks" B1-B9, agent #5; B7 verified by the coordinator: case 9 exists only in 0.695 FUN_142e18c70):
      ✅ 2026-10-16 B1 / B2 / B9 built with 2.12 (7bb5e1f); B6 and B7 verified on the client earlier.
      B1/B2 Game Table settings keyed by hash of the 0.695 RefIgGeneralSettingsTypes names, apply/reset iterate the
      received list; B3 IsInGame while observing, state 7 = end-game scoreboard; B4 Parlor tints gated by unlock list
      (send knowledge ids or 0.695 players see fewer tints); B5 Parlor PlayerSet + 2 u32, open sends 0x11C/3; B6 Play
      Mode Rules mask (site record u64 / ClaimInfo kind 0x17) drives grapple / boot / camera blocks; B7 voxel
      destruction blocked server-side, EffectEnd result 9; B8 Designs categories from ExportedTemplateCategory tables,
      voxel-data requests in bursts of 25 (L40); B9 game events 10 / 11 carry pointCount.
      Most of the 8,251 new/changed rows are noise (3,626 compiler stubs / initialisers, 1,085 copies, 1,488
      offset-only changes); per-function table: re/out/agent_funcs/final.tsv. Open: L39-L41.
- [x] **PlayerUpdateSetPlayerBio has 6 more bytes in 0.695** (done: C.4 reads the bio string; stored 4a6dd77) (the class is new in 0.695's RTTI; 0.589 only had the
      opcode). Trace writer + reader; store the bio (see 3.4).
- [x] **SendSelfToClient gained a trailing bio string in 0.695** (built 2026-10-15: shared 0.589 tail + bio; the old capture tail had 34 unread bytes the 0.695 reader would abort on) (traced 2026-10-15): the 0.695 reader FUN_142c19bd0
      is the 0.589 SendSelfToClient_Read (142b80420) plus one final read FUN_142d4b5d0(stream, player +0x7aea8) — the
      Bio (IString; max 512 chars, "BioLengthMax" 0x200) — after the inventory filters; the admin-player flag moved in
      memory (+0x7aea8 → +0x7b0d8), its place in the stream is unchanged. Nested readers: check them in
      trace/0695/functions.tsv (status changed). Server: append the bio (u32 length + chars) for 0.695 clients.
- [x] **PlayerUpdate 0x26 sub 0x80 SetPlayerBio** (done 4a6dd77: character.bio, sent in SendSelfToClient; the Inspect view of others' bios is 3.4) (0.695 class PlayerUpdatePacketSetPlayerBio, sent by FUN_142fd4290
      after the client's own bad-word check "BioIsValid" / "BadWords"): C→S {string bio}. Store it per character;
      the reply / broadcast format: trace the 0.695 handler.
- [x] **Decide how the server tells builds apart** (2026-10-15: the gateway login's ClientBuild → WorldSession.Speaks695 / ZoneOpcodeTranslator.Uses695Wire; first 0.695 client in the world through the Docker stack the same day) (login version string / exe build) and gate every 0.695-only
      packet and field on it, so 0.589 keeps working (memory: version strategy, build for 0.589, gate, upgrade).
- [x] **Full decompile of the 0.695 exe** → re/decomp_all_695 (237 parts; Ghidra project re/Landmark695, re/analyze695.py).
- [x] **Function-level diff 0.589 → 0.695** (done: functions.tsv + functions-695.md, agent #5): every changed / new function, with what it does and what it needs from
      the server (CLIENT-DIFF.md). Then re-check every handler the server already has against the 0.695 reader.

### 0.695 packet coverage (trace/0695/coverage-695.md, agent #10; gating plan in its §5)

- [x] **C.1 Per-build PlayerUpdate header** (built 2026-10-15: Network/Message/ClientWire695.cs, hooked in WorldSession.SendPacket / OnTunnelData; unit-checked, not yet tried with a 0.695 client) (u32 sub) in WorldSession.GetOpcode / GetOpcodeBytes for 0.695; 32
      implemented sub-opcodes (§5.1). Source: coverage-695 §5.1, packets-695 §A1.
- [x] **C.2 Sub translation tables** (built 2026-10-15, same file: 0.695-only subs get internal numbers NewBase + sub — Claims 0x80+, others 0x1000+; use ClientWire695.Internal(...) in their handlers) for Claims, Mail, InGamePurchase, InstancedGame, both directions, with the four
      0.695-only collisions routed to new ids (§5.2). 35 implemented messages.
- [x] **C.3 Per-build body serializers** (built 2026-10-15: SendSelf bio, DisplayPropText, design data, claim record / info, InstancedGame HUD / Event / PropRecipe / GetConfiguration; Prop EditSettingsReply f64 records still open → 2.18) for SendSelfToClient, DisplayPropText, TemplateBasicData / VoxelDataResponse,
      claim record + ClaimInfoUpdate, InstancedGame PropRecipe / HUD / Event / GetConfigurationResponse (hashed keys),
      EditSettingsReply (§5.3).
- [x] **C.4 Per-build parsers** (built 2026-10-15: SetConfiguration 0.695 shape; PlayerSet / PropRequest trailers are read past; SetPlayerBio reads the bio): PlayerSet (+2 u32), InstancedGame PropRequest (+u32), SetConfiguration (0.695 shape),
      SetPlayerBio (read the string).
- [x] **C.5 New 0.695 messages** ✅ 2026-10-16 all built: JoinTeam / AddOrRemoveRule (7bb5e1f), WorldEditingPlayEffect (VoxelDestruction.PlayEffect, L36 answered in leads-L33-L35-L36.md), TemplateProductCreate (Commerce.FromWire) — (2026-10-16: built bio, inspect, design materials 0x1D-0x1F, DialogResponse 0x1D, GetConfiguration, Matchmaking 0x19/0x1A, SetAdventureFlags, StartClaimPlacement, PlayerEnteredClaim, 0x11C/3-4, ShowActivationFailure; open: JoinTeam type 9, AddOrRemoveRule 0x18, WorldEditingPlayEffect (L36), TemplateProductCreate) (each is in TASKS §1 already; this is the packet list): 0x26/0x80 SetPlayerBio (store,
      SendSelfToClient), 0x91/3 RequestInspectPlayer → StartInspect 0x91/1, 0xF0/0x1D-0x1F design materials,
      0xED/0x1D DialogResponse, 0x10D/0xD GetConfigurationRequest, /8 type 9 JoinTeam, /0x18 AddOrRemoveRule, /0x19
      MatchmakingRequest → /0x1A MatchmakingUpdate, 0xF9/0x45 SetAdventureFlags, /0x46 StartClaimPlacement, /0x63
      PlayerEnteredClaim, 0x11C/3 → /4 EquippedAttachmentRequest / Reply, 0x2D/0x1E ShowActivationFailure,
      0x10C/8 WorldEditingPlayEffect, 0x45/0x30 TemplateProductCreate.
- [x] **C.6 Value domains** ✅ 2026-10-16 EffectEnd result 9 (blocked destruction); PickUpReply 0xE ✅ (built with the Reward Chests, 54d0138: Props.PickUp answers a paid-out chest's pick-up with error 0xE; re-checked 2026-10-16 against 0.695 FUN_142fba9f0 case 6 → `Prop_PropPickupErrorPartialLootChest` 577600; untested in game) —: EffectEnd result 9 when Play Mode Rules block destruction; PickUpReply 0xE (partial loot).
- [x] **C.7 AchievementAdd 0x92/2** ✅ 2026-10-16 (agent) Achievements.SendAdd; 0.695 test: completing 574 sent AchievementAdd 575, client stayed connected, reward paid (Journal view: user) (one achievement record) for goal tiers (agent #9 / wiki audit) — both builds.
- [x] **C.8 Re-check the 0.695-only prop list send** moot on client-0.695 (only 0.695 clients connect; they get the live-captured list; the 0.589 question stays with the frozen branch, L52) — (MiscHandler.cs:283-287) against L52.

### Terrain in 0.695 (trace/0695/terrain-695.md §7)

The terrain protocol is unchanged (every terrain reader / writer is "same"). Server work the 0.695 data needs:

- [x] **0.T1 GameDirectory → the 0.695 install** ✅ 2026-10-15 (GAME_DIR / GAME_DATA_DIR in docker/worlds/dev.env; Outposts read the 0.695 ruin_*.otd) — for templates and terrain files (superset of 0.589 except two 0-byte
      templates it replaced): serves both clients, gives the re-saved ruins and the real Landmarks-of-Landmark winners.
- [x] **0.T2 Event islands, continents 436-438** ✅ heights baked 2026-10-16 from the 0.695 client (TerrainHeights460-462.bin, 100 % valid) and the bitmap names confirmed against the shipped caches (L13 found); ✅ 2026-10-16 (agent; untested in game) Landscapes loads 460-462 (0.695
      install only) and sends continent 436-438 with Shape_424 + HeightLayerIds_424 + BiomeIds_427 (460) / BiomeIds_437
      (461, 462) [I: surface caches match the template island at the same-bitmap noise floor]; .csc decoded (cell range,
      not a height: no heights from it); TerrainMaterials.BiomeBitmapOf; no caves / veins (no files, keyed by zone);
      heights = bake on the 0.695 client (hook picks FUN_144087ab0) + tools/terrain/csc.py check, steps in
      TESTING-NEEDED "Event islands" (trace/0695/event-islands-695.md) — (zones 460 Halloween Party, 461 Winter Holiday Party, 462 Isle of
      Judgement), 0.695 clients only: Landscapes / SendZoneDetails must send continent 436-438 with bitmap names that
      exist (template island 424-429 Shape / HeightLayerIds; BiomeIds 427/429 or 437) — which names live sent is L13.
      Server data: heights bake, materials, no caves (.cpa) and no ore veins (.vdt) on these islands.
- [x] **0.T3 Ruin templates' spawner props → server spawns** ✅ 2026-10-15 (Outposts: spawners, Elites per BOSS kind, chest spots 2731, crystal 2654 from the template) — (chests, creatures, bosses; 1,750 spawner props in the 32
      re-saved `ruin_*` .otd) when the Environment Server places ruins (TASKS 7).
- [x] **0.T4 Layer 496 `391_Layer2`** (second, tier-2 underground layer of Arid Thicket): n/a for the server, 2026-10-16 — the row (VoxelContinentToLayerMapping 1125: 391 → 496; VoxelContinentLayers 496 = layer 364's biome map, only the teleport image 10229 differs) has no client data behind it: one ContinentCavePlacements[391]_Underground.cpa (layer 1's groups) and no second vein file, and the client generates caves from those files, so there is nothing a server could serve on it. L14 stays open as the question of what live meant it for.
- Mesh Shapes, VoxelBiomes and VoxelMaterials changes need nothing from the server (client-side stamp brush; the
  edit arrives as the unchanged ApplyDiffRequest).

## 1. New client packets in 0.695 (from RTTI: re/out/rtti_new_695.txt)

Each needs: its reader/writer layout (pkt.py on the 0.695 decompile), its opcode, what the client does with it,
and the server side. Grouped by the feature they belong to.

**All 19 new classes are traced** (layouts, opcodes, replies, emulator state): trace/0695/packets-695.md (agent #3);
open questions L28-L38. The lines below are now "implement", not "trace".

- [x] `PlayerUpdatePacketSetPlayerBio`, `RequestInspectPlayerPacket`: the Bio and Inspect window (3.4) ✅.
- [x] `TemplatePacketPurchaseMissingTemplateMaterials` (✅ 2026-10-15, 3.6), `TemplatePacketPurchaseTemplateMaterialsDataRequest/Response`:
      buying a Design's missing materials with lumens (3.6).
- [x] ✅ 2.22 (0x1D answered; tested from Lua) `PropPacketDialogResponse`, `Hud::HudIndicatorDefinitionNpcDialogResponse`, `NpcDialogResponseIndicator`:
      Story Node response buttons (2.9).
- [x] `InstancedGameMatchmakingRequest/Update` (✅ 2026-10-15 search states, 3.25; ✅ 2026-10-16 JoinTeam type 9 + "Match found!" into the game's queue with Join Now (7bb5e1f), counts across worlds via the social service (6f544b3); moving to another world's game is L31), `InstancedGameRequestJoinTeamPacket`,
      `InstancedGameGetConfigurationRequestPacket`, `InstancedGameAddOrRemoveRuleRequestPacket`,
      `ClientRequirementInstancedGameSettingValue`, `InstancedGames::RuleSetScriptPropDefinition`,
      `InstancedGames::RuleRequirementFromGeneralSettingDefinition`: the reworked Game Table and the Game Finder (2.10).
- [x] `ClaimsPacketSetAdventureFlags` ✅ 2.1, `ClientRequirementCanUseBootAbility`: Play Mode Rules (2.1).
- [x] `ClaimsPacketStartClaimPlacement` (✅ 2026-10-15, 3.5): placing a build site from the site window, no flag (3.5).
- [x] `ClaimsPacketPlayerEnteredClaim` ✅ L33 (visit count, reward-chest notice): (find what it drives).
- [x] `CharacterCustomizationPacketEquippedAttachmentRequest/Reply` (✅ 2026-10-15, 3.3): Crimson Parlor gender / size change (3.3).
- [x] `ArtTintGroupUnlockDefinition(+Manager, TintDefinition)` ✅ 4.9 (rows in Unlockers.cs): tint unlockers (the Halloween hats were unlockers; 3.8).
- [x] `InventoryPacketShowActivationFailure` ✅ ItemUses.Refuse (seen on 0.695): item use refused (pets, unlockers, reward chests?).
- [x] ✅ 2.6 `WorldEditingPlayEffectPacket`: effects for world edits (Linking Area destroy/heal? dynamite?) (2.6).
- [x] `PacketInGamePurchaseCommerceTemplateProductCreate`: Player Studio / Design product creation. ✅ 2026-10-16 (1cb2bbd, Commerce.FromWire): 0x30 maps onto ProductCreate 0x2F, whose body it starts with (packets-695.md §12); the design data the client appends is ignored because the server reads its own stored copy of the design, as for 0.589.
- [x] ✅ 3.7 `ExportedTemplate*` (Definition, Category, Mapping, Side, SideLoader): Daybreak-granted Designs categories (3.7).
- [x] `MeshCategory*`, `MeshPaletteEntry`, `PaletteCollectionMeshes`: the Mesh Shapes tab (3.1) ✅ 2026-10-16 client-only
      (PaletteCollectionMeshes fills from the client's VoxelShapes.txt via VoxelFarmCore::InitVoxelStructures FUN_14349d440;
      L17 answered; trace/0695/mesh-shapes-695.md §4). Nothing to serve.
- [x] `PropHelpWindowDefinition(+Manager, Mapping)` ✅ 2.27: help windows for story-tool props (First Time / help entries).
- [x] ✅ 2026-10-17 any read-only settings row goes out as a text line (PropSettings.WriteRecord: max 0 / min 0 → the 0.695 PropertyTextLine; key string = caption, label string = value): the Reward Chest count (row 106) and the Combat Node's "Ability N" rows (70f7d05; untested in game); Game Table and team names are editable text settings (2.12), not text lines — (2026-10-16: read-only rows sent as text lines for the Reward Chest count) `LuaPropertyEditor::PropertyTextLine`: a text-line property in the prop settings editor (Game Table names,
      team names, Story Node?).
- [x] `VoxelFarm::Internal::PasteThread`, `PreviewGenerationThread` ✅ 2026-10-16 client only: worker threads of the client's VoxelFarm library (ctors FUN_143f8a500 / FUN_143f8a960 in part_0161, 6 MB thread stacks, no packet class used), so the preview is computed by the client and the server sends nothing: paste-preview progress messages (client only,
      check whether the server sends progress).

## 2. Story tools, Play Mode and site games added after 0.589

The server already has: Links, Play Mode, switches, doors, timers, dice, Story Nodes, spawners, Travel Points,
Linking Areas, clicking creatures, creature routes, Damage Node, Focus Node, Logic Filter (StoryTools.cs). Check each
of those against the post-0.589 changes too (2.12).

- [x] **2.1 Play Mode Rules** (✅ 2026-10-16 complete: grapple, boot abilities and cameras are enforced by the 0.695 client from the mask the server sends — grapple FUN in part_0073 ~27310 refuses when the claim is in Play Mode (+0x378 == 2) with rule bit 1 and shows Grapple.AdventureBlocked 577409; boot abilities via Requirement 74 CanUseBootAbility (failure 577415); voxel destruction is the server's (bit 4, ClaimDestruction.Fail.NotAllowed 577411)) (30 Jun 2016, P-items "Play Mode Rules ... Advanced Build Settings"): per-site toggles, ✅ 2026-10-15 (agent) stored (AdventureFlags), set by 0.695 sub 0x45 from the owner, sent as info kind 0x17 and in the claim record; 0.695 test 0x1 → 0x5 → 0x4 (client keeps the mask); bit 4 ✅ 2026-10-16 (dynamite refused with EffectEnd 9),
      Play Mode only, all players including the owner: Free Camera, Orbit Camera, Flying Character Camera (default
      off), Grappling Hook, Boot abilities, Voxel destruction (default on). Visitors see the rules in Advanced Build
      Settings, get error messages, and only see the allowed camera buttons. Dynamite works on build sites by default
      (temporary destruction, heals after 100 s). Harvesting on sites stays impossible.
- [x] **2.2 Counting Node** ✅ 2026-10-15 (Spec 7: counts 0-99, ops 577248-577251, events, disc digits via type-2 state; 0.695 test) (30 Jun 2016): counts 0-99, start and max in settings; events at max / at 0 / increase /
      decrease; actions +1, −1, set max, set 0; value shown on the disc; hidden in Play Mode.
- [x] **2.3 Scoreboard** ✅ 2026-10-15 (same runtime as the Counting Node, prop ScoreboardProp) (28 Jul 2016): a Counting Node that stays visible in Play Mode.
- [x] **2.4 Logic Filter overhaul** ✅ 2026-10-16 all parts built: lights = 2.19 (type-2 states, 0.695 test passed), silent reset = 2.32 (Logic Filters back to both off, no Links fire); ids checked 2026-10-16: the 17 Logic Filter sentences keep their 0.589 ids with the light wording (link_texts_695.tsv: 572953-572964, 576090-576094 "changed", Cog 1 = blue, Cog 2 = yellow as StoryTools maps them); a reset now also drops the site's open questions; untested in game: the reset (30 Jun 2016): blue / yellow lightbulbs instead of cogs, new wording for every
      action and event; Reset Node resets Logic Filters without firing Links. Server has the Logic Filter: check the
      0.695 texts / ids and the Reset Node behaviour.
- [x] **2.5 Story Node additions** ✅ 2026-10-16: formatting / 512 count = 2.21, text-expires = 2.23 (now carried in the chain that showed the text, 2.34), questions = 2.22; bubble interrupt (P5977, P6094) is client-side in 0.695 [P]: DisplayPropText → FUN_142ebfcb0 overwrites the speaker's bubble text and time (+0x1278 / +0x1288) and FUN_142ece660 keeps one `Character.ActiveChatBubbles` row per actor, so a second text replaces the first; the server sends every text (DisplayPropText now skips players mid re-zone); count-down is the editor's (LargeTextInput.as); untested in game (P6107-P6113 recipe in TESTING-NEEDED): text colour / bold / italic / underline per character (30 Jun); event "When the
      text on this node expires..." (30 Jun); response buttons with accept / deny events (11 Oct, 2.9); chat bubbles
      not lost when one interrupts another (14 Jul); character limit counts down.
- [x] **2.6 Linking Area destruction** ✅ 2026-10-16 (agent, VoxelDestruction.cs) row 102, Destroy / Heal Voxels, WorldEditingPlayEffect (L36); 0.695 test: dynamite on a Play Mode site refused with EffectEnd 9 when bit 4 is set (item kept), allowed → crater healed after ~104 s; area Destroy / Heal needs a built wall (user) — (28 Jul 2016): per-area voxel destruction setting (site default / allow / block);
      links into an area destroy or heal its blocks (heal only player-set voxels; a pending heal isn't cancelled by a
      new destroy).
- [x] **2.7 Combat Node** ✅ 2026-10-16 (StoryTools + Creatures.StoryUseAbility; untested in game): node → creature link (577162 / 577163, single binding), Set tactics from the node's row 31 (577242, then 577245 → spawner 577218), Ability actions offered only once linked, one per ability (ours: every creature kind has one attack; live's per-creature ability sets are server data, L19), Ability 1 = the creature attacks the triggering player now (target ours, L23), spawner 577219 Deactivate combat AI — (28 Jul 2016): "Combat Tactics" (standard, stationary, do nothing) on spawners and the node;
      the node links to one creature, lists its abilities, and links can make it use Ability N or change tactics.
- [x] **2.8 Server Time Node** (28 Jul 2016): up to 6 listeners, each a game-time hour (0-23), fires its event at that ✅ 2026-10-15 (agent): Server Time Node listeners (row 101), Desired Time rows, slot events on the game hour; test: slot 1 at 14 fired, slot 3 refused with 2 listeners
      hour (game time, not the site's time of day).
- [x] **2.9 Random Link Filter** ✅ 2026-10-15 (577593 picks one 577591 link) (28 Jul 2016): fires one of its links at random; Dice Filter renamed Probability Filter.
- [x] **2.10 Respawn Location** ✅ 2026-10-15 (see 2.24) (28 Jul 2016): enabled / disabled setting, can be switched by links; players who die or
      flee on the site go to the nearest enabled one (or the Starting Location).
- [x] **2.11 IF Gate** ✅ 2026-10-16 runtime + settings (comparison words other than Equal To ours, L19); triggering-character checks ✅ 9e73842 (game team 1-4, NPC disposition, real player; server-labelled options, the 0.695 client predates them; untested in game) — (29 Sep 2016; 11 Oct: checks on the triggering character): links to a prop with properties
      (door, light, counting node...), compares a property (equal, less, greater...) with a value, fires "if" or
      "else"; can check the triggering character's game team, NPC disposition, and whether it is a real player.
- [x] **2.12 Game Table rework, Game Finder, Score Node, Victory Node** ✅ 2026-10-17 all open parts built or settled: across worlds (Join Now on another world's match, 1c039b1; optional way home FINDER_RETURN_HOME, cadb157), "On Game Started" / "On Game Ended" (E.11), L21 / L31 / L37 closed (agent-leads2-a.md, agent-leads3-c.md); left only the hand tests (settings window, team switch, cross-world join: TESTING-NEEDED) ✅ 2026-10-16 built (7bb5e1f, InstancedGames.Rework.cs) and checked on the 0.695 client (new 2313 table → 2-team Adventure, rules 1,2,12, PvP, switch; join / start / spawn arrival; `gamescore 5` → the client's team HUD 'Top Score 5', Team 1 5 / Team 2 0, timer 19:49; end → Team 1 wins; settings window / team switch / Finder still to try by hand): table 2313 settings drive teams / limits / PvP, ticked rules kept with rule type + trigger id, text settings, JoinTeam, AddOrRemoveRule, GetConfigurationRequest, Score / Victory Node rows + Links, Control Point Links, edits refused mid-round (564516); untested in game (test steps in TESTING-NEEDED); open: across worlds (needs a world-channel message), L21, L31, L37 — (29 Sep 2016; 14 Oct: works across worlds):
      game name, teams (count, names, players allowed), Time Limit, Score on Enemy Kill, Team / Player Score Victory,
      Control Points (update rate, points); Game Props tab (Team Spawn Points, Control Points); Score Node gives points
      through links; Victory Node ends the game through links; /gamefinder lists games to join. The server already has
      the 0.589 Game Table (Deathmatch, TDM, Zone Control, Adventure): compare with the 0.695 rule sets.
- [x] **2.13 "When clicked" event on all props** (29 Aug 2016); links cleared when a linked prop is recycled. ✅ 2026-10-15: 576311 offered on every placed prop (clickOnlyKind), Links button on every prop, click-linked props flagged usable in Play Mode (inferred rule); untested in game
- [x] **2.14 Creature health / damage settings on spawners** ✅ 2026-10-16 built (see the git log, untested in game): unblocked through L19's finding that 0.695 settings records carry their own label, the same way the Server Time Node rows work: server rows Health % Multiplier (our label, 100-500 %) and Damage % Multiplier (client text 577652, 25-300 %), step 25, default 100 [I: keys / ranges]; the story creature's health / damage scale with them; a changed creature drops no loot (P6429) — (29 Aug 2016): health only up, damage up or down;
      changed creatures may drop no loot.
- [x] **2.15 Fiery Fungus and Proximity Mine damage terrain on build sites** ✅ 2026-10-16 VoxelDestruction.CreatureBlast (temporary, heals 100 s; row 105 Damages Terrain read in StoryTools.Spawn, 3317b9a) — (29 Aug 2016).
- [x] **2.16 Older story tool fixes after 0.589** (2026-10-17: every part built; the arrow-vs-facing check is untested in game, TESTING-NEEDED) ✅ 2026-10-16 checked one by one: Damage Node via Linking Area on Luminaries / creatures / both = 2.25 / 2.26; Timer 1 s steps and typed numbers = gameData695 row 14 (step 1, 1-600) + the 0.695 window (2.18); completes vs resets = StoryTools (completion fires only "completes", 573101 only from Reset / Pause-and-Reset, L23); walk route = 576661 fires only from 576662 (P5776); nameable = PropDefs ALLOW_USER_RENAME from gameData695 (every story tool 1 except Starting Location 2655 and Linking Area 1590, which the client's own table keeps at 0); Starting Location arrow = the prop model (client). ✅ 2026-10-16 the arriving player's facing (P5825; L118 found: ClientBeginZoning rotation = player orientation via FUN_142ec4d70; respawn / visit / own-site travel send the Starting Location's rotation; arrow-vs-facing check in TESTING-NEEDED) — previously: arrivals sent the identity rotation, the client's use of the zoning / teleport rotation is not traced: L118 — (7 Jun - 14 Jul 2016): Damage Node can target Luminaries,
      creatures or both through a Linking Area; Timer in 1 s steps and typed numbers; Timer "completes" vs "resets"
      events; Starting Location shows its facing; walk-route actions/events renamed; all story-tool props nameable.
      Check each against StoryTools.cs.

From trace/0695/storytools-695.md (agent #6; full spec in its §6). Coordinator note on 2.24: the 0.589 server uses prop 1225 for item 1331 "Respawn Point" (the 0.589 placeable); 2701 becomes placeable only in 0.695 — add 2701 for 0.695 and verify which prop item 1331 places in 0.589 before changing 1225.

- [x] **2.17 Build-gated story vocabulary** (2026-10-17: the weapon-visual sentences 577237-577239 are offered too, E.11 / merge e-story; untested in game): two kind sets (0.589 / 0.695) chosen per session; 0.695-only props never ✅ 2026-10-15 vocabulary for 2901 / 3000 / 2657 / 2659 / 2701 / 2940 / 2976 (StoryTools kindsById); 0.695 test: Counting Node Links window lists its 4 events (shots_counting_node_links_695.png). 2026-10-16 checked: the spawner (577218 / 577219 / 577163), Linking Area (577221 / 577228 / 577548 / 577549 / 577722 / 577723), Story Node (577246 / 577724 / 577725) and IF (577628) sentences are all offered; open only the weapon-visual ones (577237-577239, L95); 0.589 gating moot on this branch
      offered to 0.589; reused ids 557290 / 565055 / 577092 / 577213-577216 only on 0.695 (§1.1, §6.1).
- [x] **2.18 0.695 settings window**: f64 record values (FUN_142c15ee0), option label strings (FUN_142c1eae0), ✅ 2026-10-15: 0.695 settings records use f64 values and option labels (PropSettings.WriteRecord); 0.695 test: Timer window shows Duration 60 / Loop Until Paused (shots_settings_timer_695.png)
      0.695 EditableProperties rows (46; row 31 key renamed, 14 step 1, 33 / 35 ranges 0-99) loaded for 0.695
      sessions; read-only and IF-only rows (§6.9).
- [x] **2.19 Prop state broadcast** (2026-10-17: everything built; only the Edit-mode digits are untested in game) (type 2): Logic Filter lights (107 / 108) and counter digits (33) for 0.695 ✅ 2026-10-15 Logic Filter lights (type-2 state 0x687b558b / 0xde034081, coalesced per tick, 100 m, late viewers on spawn); 0.695 test: blue light on after a switch click (shots_logic_filter_light_695.png). Counter digits wait for 2.2 → ✅ 2026-10-16 counter digits (row 33 → key 0x75e90645) at Play start / every change / late viewers (with 2.2), and the Edit values: leaving Play Mode puts the Starting Count back, counters out of Play Mode show their Starting Count to viewers on spawn and when rows 33 / 35 change (window or `propset`; [I] L20); untested in game (Edit-mode part)
      viewers, initial + on change + late joiners, coalesced (§6.7).
- [x] **2.20 AddBinding result codes**: 4 single binding (Combat Node 577162, IF Gate 577629), 3 duplicate, 1 ✅ 2026-10-15 StoryTools.Add returns 0 / 1 / 3 duplicate / 4 single-binding (577162, 577629); dev storylink: a repeated Link gave 3
      permission (§6.4).
- [x] **2.21 Story text 0.695**: ✅ 2026-10-15 Props.StoryText (tag whitelist P/B/I/U/FONT COLOR, 512 visible, 4096 cap); 0.695 test: bubble showed the text with a SCRIPT tag removed. tokenised rich text stored and passed through, 512 visible characters, tag
      whitelist, plain text for 0.589 viewers; replaces the 1024 cap in Props.cs:1005 (with 6.35).
- [x] **2.22 Question bubbles**: ✅ 2026-10-15 row 112, question id in DisplayPropText, 0xED/0x1D → 577724 / 577725; 0.695 test: Lua Prop.OnConfirmNpcDialog sent 0x1D with our id → "accepted story question". Speaker = spawner prop (creatures can't carry the bubble: L96). DisplayPropText question flag + response id; PropPacketDialogResponse (0xED / 0x1D)
      handler → Story Node 577724 / 577725 with the responder as triggering character (row 112).
- [x] **2.23 Story Node lists and new rows** (2026-10-18: creature weapon models sent (CreatureWeapons.cs, 827fb93; CREATURE_WEAPONS off — live's per-creature data stays open as L95, dev creatureweapon sets any)) (2026-10-17: Weapon Visuals row 41 and 577237-577239 built (E.11, Creatures.StoryWeapons, AnimationSelection 352 / 353; untested in game); left: no weapon model on the creature — both client carriers are proven, live's per-creature weapon data was server-only (agent-storyhide.md, L95); non-bipeds: L117 closed, they have no emote / stance / mood animations): ✅ 2026-10-15 rows 25 (+ mode 5 "In Chat Bubble and Chat") / 37 / 24 in the window, 577246 text-expires after Text Duration; ✅ 2026-10-16 Mood 4 / Stance 8 / Emote 29 lists with the client's labels, each an AnimationSelection traced through the player network's switch nodes (Sad 221, Sitting 351, Lying Down 480, Cough 449, Flex 451, Sneeze 452, Welcome 453, Yawn 454, Stretch 456, Hug 817, Talk continuous / stop 252 / 253 [P]; Beckon 455 / Yell 450 [I strong]; creatures other than player-like bipeds: L117); the mood now plays (Emote_Emotion) before stance and emote; defaults Calm [P] / Standing / Wave [I]; old saved moods 1-3 mapped; Announce Emotes in Chat (row 24): the client's own "[*speaker*] ..." line per mood / stance / emote (ChatPacketFromStringId, spawner as speaker, L96) [I]; untested in game. Open: Weapon Visuals (row 41, 577237-577239) — no proven NPC draw / sheathe mechanism (L95). Mood 4 / Stance 8 / Emote 29 / Text Location 4 options (launch lists,
      §4.1), Weapon Visuals (41) with creature equip / sheath, Announce Emotes in Chat (24), text-expires event.
      Option values: L18.
- [x] **2.24 Respawn Point 2701**: replace Graveyards.cs:21's 1225 with 2701, Enabled flag (103) and its Links, ✅ 2026-10-15 (agent): Respawn Point enable / disable, respawns at the nearest enabled point in Play Mode, events; untested in game
      nearest enabled point or Starting Location on Flee, 574557 at the point used.
- [x] **2.25 Linking Area 0.695 actions**: ✅ 2026-10-15 577221 / 577228 / 577148 with node % + Heal Instead, 577723 → 577722 per player / creature; despawn → leaves (existing tick); untested in game. Luminary / both damage variants (577221, 577228), node % and Heal Instead
      instead of the fixed 50 %, parse-every-entity pair (577722 / 577723), creature leaves on despawn (P6192).
- [x] **2.26 Damage Node Heal Instead and Luminaries** ✅ 2026-10-15 (DamageOf / HurtCreature / HurtPlayer, Creatures.HealStory) (row 32 is never read; players are never hurt / healed).
- [x] **2.27 Help**: Open Help radial button 74 for PropHelpWindowMapping props; ScriptObj first-time events on first ✅ 2026-10-15 (agent): button 74 for PropHelpWindowMapping props (0.695), ScriptObj_* FTE pages on first placement (inferred moment); 0.695 test: fte 33 shows the Timer Filter page; Prop.OpenHelpWindow from Lua showed nothing (claim dialog open?) — user check via right-click.
      placement (§6.10).
- [x] **2.28 Models for 0.695-only story props** ✅ by data (plan §0.5; Counting Node / Target Dummy / Respawn Point models seen on 0.695) (§3: 1727, 1728, 1736-1739, 1741, 1744, 1745; by Models.txt names —
      verify in the client before relying on it).
- [x] **2.29 Spawner rows 9 / 17 / 40 / 105** (2026-10-17: row 40 Spawn With Weapons Equipped built with the Equip / Unequip Links, E.11; untested in game; the weapon model itself: 2.23 / L95): ✅ 2026-10-15 row 9 Spawn Automatically, row 17 Reaction (Enemy / Neutral / Ally → starting faction), row 31 Combat Tactics (Standard / Stationary / Do Nothing, 0.695 key); ✅ 2026-10-16 row 105 Damages Terrain (read at spawn → Creatures.StoryDamagesTerrain → VoxelDestruction.CreatureBlast, 3317b9a with 2.15). Open: row 40 Spawn With Weapons Equipped — a drop-down by its row shape (L95 updated: options likely 577235 / 577236), but no proven NPC weapon draw / sheathe mechanism yet, so not offered (a dead setting otherwise). spawn on Play only with Spawn Automatically; Reaction drop-down;
      Spawn With Weapons Equipped; Damages Terrain (with 2.15).
- [x] **2.30 Probability Filter odds**: if only "Odds of Winning" is shown (help page), odds = value %, not ✅ 2026-10-15: one "Odds of Winning" row, odds = value / 100; 0.695 test shows Odds of Winning 50 (shots_settings_odds_695.png)
      a / (a + b) (StoryTools.cs:369-373) — check what the 0.589 window shows first.
- [x] **2.31 IF-capable runtime properties** ✅ 2026-10-16 count / lights / enabled / owning team / is alive / on (IfHolds); triggering-character checks ✅ 9e73842 (game team, NPC disposition, real player; server-labelled options), now reading the fixed triggering character of 2.34; untested in game —: keep count, lights, enabled, controlling team, is-alive (and door /
      light state, L19) readable by the IF Gate; triggering-character checks (team, disposition, is player).
- [x] **2.32 Play Mode snapshot and silent reset**: ✅ 2026-10-15 reset rebuilds the Play-start (= built) state silently, area occupants kept; 577089 route still unknown (L22). snapshot at Edit → Play, restore on Reset without firing Links,
      Logic Filters reset, message 577089 (§6.8); today Reset = stop + start (StoryTools.cs:884-890).
- [x] **2.33 Linking Area shape** ✅ 2026-10-16 (ad947bb): Area Tool boxes as AreaDefinitions (family 0xEA, table world_area), prop 1590 carries the area guid (AddLightweightProp +0x268), InArea and area destroy / heal use the boxes; trace/0695/area-definitions-695.md — check whether the placed area carries per-axis scale (Area Tool box) and test
      inside-ness against the box instead of the 4 m × scale.X radius (StoryTools.cs:352, 358-363).
- [x] **2.34 Triggering character through every chain** ✅ 2026-10-16 `Context.TriggerPlayer` / `TriggerCreature` set once where a chain starts (player > creature, P6536-P6538) and copied unchanged by every Link (`with`), so a spawner putting its own creature in the chain no longer changes the trigger; "parse every entity" starts one sub-chain per entity with that entity as trigger; consumers: IF Gate character checks, Score Node / Control Point (InstancedGames.LinkAction), reward chest loot, Combat Node ability target, Focus Node "focus on what activated this" (player, else the triggering creature [I]); timer completion and text-expires run in the chain that started / showed them ([I], L23 note); untested in game (2026-10-15: Context.Player / Creature already set at every chain start and carried by `with`; a separate Trigger field waits for its first consumer, IF Gate 2.11 / Score Node 2.12) (needed by IF Gate, Damage on Luminaries, Score Node
      "triggering Luminary", Victory "Triggering Team", Control Point "Attempt to control").
- [x] **2.35 0.695 copies of PropDefs / EditableProperties / Models in gameData** ✅ 2026-10-15: the 0.695 server reads
      gameData695 (all 57 changed tables + en_us_data.dat from the 0.695 client, CRC-checked; header changes only append
      columns). Code that assumed 0.589 rows (e.g. the Logic Filter rename) is checked as each system is rebuilt. for 0.695 sessions (Logic Filter
      rename, new rows, new props); today gameData holds the 0.589 tables.
- [x] **2.36 Counting Node / Scoreboard, Combat Node, Random Link Filter, Server Time Node, IF Gate runtime** ✅ 2026-10-16 (2.2, 2.3, 2.7, 2.8, 2.9, 2.11 done) per
      §6.5 (the per-prop halves of 2.2, 2.3, 2.7, 2.8, 2.9, 2.11).

---

## 3. Other features added after 0.589

- [x] **3.1 Mesh Shapes tab** (28 Jul 2016 test; 11 Aug fill works; 29 Aug more shapes; Fill "Smoothing Enabled"
      option): shapes used like brush shapes with the chosen tool and material. ✅ 2026-10-16 (agent; untested in game):
      traced — Mesh.SelectMesh / PlaceMesh activate the StampBrush (ToolPacketActivate 0x117/1, now decoded: u8 by item,
      u32 item, u32 ToolTypes id) and the edit is the unchanged ApplyDiffRequest, so the brush path (permissions, cost,
      broadcast) already applies; built Game/Building.Tools.cs: the active tool per player picks the ResponseObserved
      effect others see (Add / mesh / paste = Stamp, Fill Brush = FillStamp no-destroy, Remove / Heal / Smooth / Paint).
      Palette, categories and "Smoothing Enabled" are client-only. trace/0695/mesh-shapes-695.md.
- [x] **3.2 Modular build mode** n/a: client-only (ui-trace-695.md, agent #11) — (28 Jul 2016): build UI in separate windows (client only? check for saved layout).
- [x] **3.3 Crimson Parlor** (7 Jun 2016, at every spire hub and as a site prop; 28 Jul: gender and size change).
      ✅ 2026-10-17 L34 closed: EquippedAttachmentReply lists the visible equipment and Purchase sends PlayerUpdate
      CustomizationChange to the buyer and the landscape (6daee78, 17e9a86; user-verified: look applied, outfit kept).
      The Pack's in-client Marketplace product stays with 4.9.
      ✅ 2026-10-15 gender / size: Barbershop.cs answers 0x11C/3 with sub 4 (empty attachment list, as the full PC data),
      reads PlayerSet race / gender, saves race, gender and the preset's profile (46 / 291; WorldSession.SetBody), and
      charges the client's own 0.695 price (FUN_1430a5f10: gender 60, size 30, CharCustOptionMult, equalities, preset
      options free). 0.695 test: Cost 60 / 30 shown and charged, body saved and kept after a relog
      (trace/0695/shots_parlor_gender_695.png, _size_695.png). The Parlor prop on sites ✅ 2026-10-16 (prop 2942, free to place in the Crafting category; a placed one opens the barber window and shows interactable, as the Spire's; P5725). Fantasy Parlor Pack ✅ (4.9, L39 answered). Open: L34 (what the barber window does with the attachment list).
      Server has the barbershop (Barbershop.cs): add gender / size (EquippedAttachment packets), the prop, the
      Fantasy Parlor Pack (30 Jun) items.
- [x] **3.4 Bio and Inspect** (30 Jun 2016) ✅ 2026-10-15: bio stored (4a6dd77); Game/Inspect.cs answers
      RequestInspectPlayer 0x91/3 with StartInspect 0x91/1 (blob: Luminary Score = goal points like the client's
      TotalPoints, sites, bio, equipped items with records + definitions); 0.695 test (Ui.InspectPlayer(guid, x) needs two
      Lua args): Inspect window shows name, gear, Luminary Score 45, Build Sites 1, the bio (trace/0695/shots_inspect_695.png).
      Open: L28 fields (profile entries, +0x110 / +0x11c). Right-click another player → Inspect shows their bio and character basics.
- [x] **3.5 Build sites placed from the site window** (2026-10-17: the Build Site Expansion 900001 is sold in the store for 1,040 Lumens and "Get more build sites" opens it (6.60); the id stays ours, L86; the store link untested in game) (14 Jul 2016): no flags; "Place this build site" per unplaced
      site; "unlock more" opens the Marketplace for an expansion item (tradeable); flags still work if kept.
      Pioneer islands allow build sites (29 Sep 2016). ✅ 2026-10-15 for 0.695 sessions (Claims.UsesFlags): 0xF9/0x46
      StartClaimPlacement (internal 0xC6) → Command 0x4D placement; no flag needed or taken, none given back on delete /
      lapse; Pioneer landscapes allowed. 0.695 test: Lua Claims.StartClaimPlacement() from the Place row → placement
      mode (trace/0695/shots_site_place_695.png). Build Site Expansion built (4.8): slots = 1 + expansions used (max 8, Claims.SlotsOf); its real id and the
      Marketplace listing are still open (L86).
- [x] **3.6 Buy a Design's missing materials** ✅ 2026-10-15 (spec trace/0695/spec-buy-materials.md): Game/DesignMaterials.cs
      0xF0/0x1D → 0x1E rows from the Lumen Station's 943 bundles + currency refresh, 0x1F checked against the offer and
      priced by the tables. 0.695 test: widget listed 240000x Dirt / 24000x Ice, 1430 Lumens, "need more" + Buy Lumens,
      then OK with Lumens; Templates.PurchaseMissingTemplateMaterials bought 11 bundles (shots_buy_materials_*_695.png).
      Open: U1-U6 of the spec (recipe items / prop components included by our choice). (14 Jul 2016): Design Details offers to buy what's missing with lumens,
      in Lumen Station bundle sizes and prices, with a confirmation listing the bundles and total.
- [x] **3.7 Daybreak-granted Design categories** ✅ 2026-10-15 (spec trace/0695/spec-design-categories.md; built by agent): template id in TemplateBasicData +0xec, starter pieces as unlocks (kind 20), 6 + 8 defaults, old characters keep all 27 (migration logged); needs the user's look at the Designs tab. Was: (30 Jun 2016) (ExportedTemplate tables).
- [x] **3.8 Seasonal drops** (2026-10-18 done: live defaults b24efca — LANDMARK_SEASON default auto, hats only in the Halloween season via HAT_DROPS = season | always | off, user: toggle + live default) ✅ 2026-10-16 drops built (b964e2f: RareDrops.Season via LANDMARK_SEASON off / auto / halloween / winter, Skeletal Bodysuits in the Halloween season, flight suits + sweaters in winter, from Lost Crystals and Superb / Legendary chests; hats stay all year until the user decides); the islands are 0.T2 / 3.19 —: Halloween hats as unlockers (29 Sep 2016) and Skeletal Bodysuits; from 6 Dec 2016 the
      three festive flight suits and the three holiday sweaters instead. Halloween Party Island (Oct 2016) and Winter
      Holiday Island (Nov 2016): islands from the 0.695 data (continents 436-438?).
- [x] **3.9 Fireworks** (2026-10-17: the open parts are built in 4.12 — 22 consumables, packs, emitter gather drops; ids ours, L24 / L25 closed by design) (30 Jun 2016; spec trace/0695/spec-fireworks.md) ✅ 2026-10-15 emitters: PropEffects one-shot sets
      (props 2949-2970 → sets 129-150; nothing at spawn, no on / off), StoryTools kind with action 577506 Launch (300 m,
      1 s minimum interval); 0.695 test: placed emitters loaded nothing, the client loaded EMIT_PROP_Fireworks_* sound
      banks exactly at each launch (DirectAssets.log). Open: the 22 consumables and the packs (L24 / L25: no item
      definitions; needs the definition builder 4.8), emitter drops (spec §5.2 F). Was:: 22 variations; consumables crafted at the Lumen Station (one burst over your
      head) and emitter props (random drops / Lumen Station recipes, sunstone to place, fire once when triggered).
- [x] **3.10 Cosmetic pets** (2026-10-17: Lop Ear 2 % from Outpost elites (4.11); the six sold in the store for 650 Lumens each, a placeholder price (6.60, L147); left only the walking-follow check by the user, untested in game) (28 Jul 2016): ✅ 2026-10-15 Pets.cs, items 900101-900107 (ours); 0.695 test: summon, swap, back after re-zone, cancel (pet1-6 shots). Open: Lop Ear drop, Marketplace, walking-follow check by the user. figurine items, right-click to summon one pet, follows you, cancel the
      buff icon to dismiss; Lop Ear Bunny from Outpost elites (rare), six from the Marketplace.
- [x] **3.11 Reward Chests** (2026-10-17: all built; Superb / Legendary also in the store at the Lumen Station's 260 / 780 (6.60); visitor loot untested in game (2 players); tier amounts ours, U1-U12) ✅ 2026-10-15 (agent) RewardChests.cs: loot Link 577598 / event 577597, cooldown row 104, count row 106 read-only, group loot 50 m, lockout notice on entry, consume + expiry mail; 0.695 test: settings window + owner refusal; Thank You Token ✅ 2026-10-16 (own id 900401 in the expiration mail; live id L24); open: visitor loot test (2 players), Superb/Legendary amounts (U1-U12 in RewardChests.cs). (29 Aug 2016; 29 Sep two higher tiers, group loot, mail reward on expiry): crafted at the
      Lumen Station, placed on a site; settings: per-player loot cooldown (minutes), count remaining; loot only in Play
      Mode, never by the owner; when used up it's consumed and the owner gets mail with bonus resources.
- [x] **3.12 Trial Key** ✅ built 2026-10-17 (membership + trial key + member benefits; harness-verified purchase / stipend / discount; merged with 32129b2; mailing and using a key untested in game, TESTING-NEEDED; open questions agent-membership.md §6 M1-M10) (29 Aug 2016): a single-use 10-day trial key by in-game mail. (Probably out of scope: no
      paid accounts. Decide.)
  **User decision 2026-10-17:** the trial key is a membership item a player can mail once to a friend; membership must be buyable in the store with Lumens (in progress, branch e-membership).
- [x] **3.13 Highfall catapult** ✅ 2026-10-16 stationary story creature (agent); ✅ 2026-10-16 siege AI (Creatures.ThinkSiege, Behaviour.Siege): faces the placed prop's rotation and never turns (Ouch / fire animations face ahead too), fires only at players / other-side creatures in a ±30° cone 10-28 m ahead (60 blocks × 0.4 m ≈ 24 m [I]), a rock every 5 s flies to the aimed point and lands with 12826 + damage + knock-down (Crush) in 4 m; Activate_01 (AnimationSelection 272) + 12825 on firing; Elite: ignores crowd control; numbers ours (L119); untested in game — (7 Jun 2016): one facing, ranged only, can't hit close enemies.
- [x] **3.14 Lost Crystals / Outposts / Underground Adventure** (2026-10-18: L131 built — trips to other worlds of the gateway, 7e9af69, harness two-world run; F2 client test in TESTING-NEEDED) (2026-10-17: L130 closed — per-landscape Local Travel crystals, on the stamped cavern graveyards (779229a); F8 rotation built between ruin slots of a cavern (779229a, OUTPOST_ROTATION_HOURS default 0 = off, no source rule); trips land on a random cavern graveyard (9a4ee5a) and prefer another landscape / cave biome (01e6ad6); left: L131 Lost Crystal trips to other worlds (servers), and the F2 client test) (audit trace/0695/audit-outposts-695.md; ✅ 2026-10-15 F1:
      outposts are the 35 0.695 ruin_*.otd with their own crystal, garrison spawners (Elite per BOSS kind) and chest spots, the
      chest at a new spot each time; old cavern_ruins stamps reverted once (tested on a copy of the dev data: 23 reverted,
      35 stamped); GAME_DIR is the 0.695 install. 2026-10-16: F3 lockout (built earlier, 7-bit index now), F6 catapult (3.13), F7 first-time help (client-tested 2026-10-16: the Outpost page opened on approaching outpost 40) + F9 offer tokens (572c066), F8 one outpost in every Chaos Cavern (6.8, e181565; outposts don't rotate yet); F5 crystal awake 15 min after the Elite dies, on the clock (c10c2c5), F4 line of sight through cave rock + ruin walls for cavern aggro, ranged shots and weapon area hits (800f0af, client check running); F2 built 2026-10-16 (agent, uncommitted, untested in game): Chaos Caverns on every non-Pioneer island with cave data (23 more: 391, 393-408, 410-416, 80-82 caverns each) — Underground Adventure picks a landscape at random (unplaced ones queued, trip to a ready one [I], L129), Lost Crystal trips to any placed outpost on any landscape, group follow keeps the landscape; outposts stamped lazily in a background thread on first visit / trip pick (one landscape, one ruin at a time; marker outposts695d_<zone>.txt, restart re-marks cells without decoding), indices 128 × (zone − 390) + n, lockouts of those in character_unlock kind 25; cavern creatures, treasure chests and line of sight per landscape; setting CavernLandscapes (all default; dev.env home until verified) + OutpostPreload (off); packets unchanged (0xFC 0x0B / 0x0C / 0x0D, 0x23 same in 0.695); open: F2 client test (TESTING-NEEDED), L130 portal crystals off-home, L131 other worlds, F8 rotation) (7 Jun - 11 Aug 2016): teleport to other worlds and
      biomes; long loot lockout per cavern; one treasure chest per outpost; no NPCs in walls; no collision after death;
      Underground Adventure to any zone and from Pioneer landscapes; elite kill unlocks the crystal (not permanently).
      Server has Outposts.cs: check each.
      2026-10-17: F4 ranged part — outpost archers / casters shoot dodgeable affinity arrows / bolts (785b579), with the
      sight checks already in place; creature and weapon shots drawn as Projectiles 0xEE (L156 / L157).
- [x] **3.15 Equipment changes** (2026-10-18: F1 Coat of Crashing Waves ice burst built (d6005bd, numbers [I]); L90 closed by design — no source has the numbers) (2026-10-17: L91 closed — the four weapons were never craftable on live, recipes retired (01e6ad6); left: F1 Coat of Crashing Waves ice burst (not built), L90 passive numbers) (audit trace/0695/audit-equipment-goals-695.md: passives and Lumen Station prices are in
      the data; ✅ 2026-10-15 F2 Night Herald's Promise only on the first Power Attack per Condemn; open F1 Coat of Crashing
      Waves ice burst (effect id would be ours), L90 passive numbers, L91 four weapon recipes) (7 Jun 2016): ten armour sets got new passives; Frenzied Slayer's trigger chance and
      Coat of Crashing Waves reduced; Dash Soles energy fix; missing weapon recipes; recipe cost changes (30 Jun).
- [x] **3.16 Goals** (2026-10-18: L92 / L93 closed by design (pass 4: built from the wiki; record ids only in captures)) (audit trace/0695/audit-equipment-goals-695.md: goal data is the Feb 2017 capture; ✅ 2026-10-15 F3
      fungus deaths count "You Can Kill Those Things" for players who see them and no kill goal (P4801 / P5753; test: a
      killed fungus moved no objective, a kobold moved 2); 2026-10-16: F4 Props To You built (moving the auto-placed Starting Location counts objective 1313; client check in TESTING-NEEDED), F5 done with 3.7 (6 defaults, old characters keep 27), F6 resolved — the live capture (Feb 2017) gives 477 bundle 1051 like 471, kept over the wiki's shorter list; open L92 / L93) (7 Jun 2016 and later): tutorial goals final pass; Journal search box (client); "You Can Kill
      Those Things" counts fungus deaths nearby and needs more; "Props to You" fix; "Recipe: Floor Switch" removed.
- [x] **3.17 Small rules** (2026-10-17: the last open part settled — L88 closed by user decision: iron density unchanged, no source for a number) (checked 2026-10-15: overflow "retrieve all" → resources tab ✅ built (Inventory.AbsorbResource,
      P5789); already true: titles list sends every title (Titles.SelfData), /emotelist (Emotes.cs), leader rank name
      editable (Guilds.cs), Electrocute strips no buffs, rooms kept across a world change (SocialServer ChatService);
      client-only: Fly mode outside the site (P6418, camera states), grappling while gliding (P5981); open: iron
      frequency (L88: ResourceVeins.txt unchanged in 0.695, so live changed server data by an unknown amount), revive
      invisibility to creatures ✅ built 2026-10-16 (Health.ReviveGrace / Unnoticed, 5 s, ends on attacking; P4717 / P5832; the 5 s is ours, L89); no ore around spires ✅ (P5797: OreVeins.NoVeins =
      Spire.Touches, veins on the platform moved at start; the test landscape had none within 120 m before or after).): Iron more frequent; no ore nodes around spires; potion buffs survive Electrocute; revive
      invisibility drops on entering combat; grappling while gliding no longer kills; /emotelist for everyone; titles
      list shows all titles; guild leader rank title editable; chat channels kept when changing worlds; Fly mode
      continues outside your site (can't start outside); overflow "retrieve all" goes to resources.
- [x] **3.18 Upkeep expiry warning** (wiki: Build Sites, Build Site Management): a warning mail three days before a
      site expires, and the client's own notice "Upkeep expires in less than [*hours*] hours!" (Claims.GaqExpiring,
      text 562253, FUN_14332aec0). ✅ 2026-10-15: SiteUpkeep.MailWarning from the hourly expiry check, once per absence
      (unlock 18); text is ours (L87). The GAQ notice is dead code in both clients (its creator FUN_1433da3e0 /
      FUN_14332d5e0 has no call, vtable slot or address reference), so nothing to send. Also fixed the timer rule to the
      launch one: +1 day per 2 unique login days (forum 53943, kept by P4133), was +1 per login day; existing timers
      kept (unlock 17 seeded from them). Tested on the isolated stack: reminder at 2 days left, not repeated after a
      restart; login days 7 → 10 days (kept), 8 → 11 days + the client's "extended" letter.

- [x] **3.19 Events world and seasonal islands** (2026-10-17: expiring the sites at the switch built, e08249f, EVENT_ISLAND_EXPIRE_ON_CLOSE default off for the user; L110 closed; untested in game) ✅ 2026-10-16 (agent; untested in game) second world "Events" (id 3 ours,
      text 567016 [I], L109) in Locations for 0.695 clients holding the island picked at start-up by EventIsland /
      EVENT_ISLAND (auto = 2016 dates: Halloween 3 Oct - 20 Nov, Winter 21 Nov - 10 Jan; halloween / winter / judgement /
      off); world id in zone details, ClientBeginZoning and site lists; normal site rules; closed island at login →
      home Spire; open: expiring sites at the switch, island content (L110) — (forum news 3 Oct and 21 Nov 2016): a separate world "Events" in the
      teleport menu holding the Halloween Party Island (Oct - Nov) and then the Winter Holiday Island (Nov - Jan),
      open for anyone to stake a site; switched at a restart. Needs the islands' terrain from the 0.695 data
      (VoxelContinentLayers +1, new .otd files).
- [x] **3.20 Landmarks of Landmark** ✅ 2026-10-16 (untested in game) LandmarksOfLandmark.cs + .Table.cs (145 entries,
      5 worlds × 29 landscapes, from the 3 Aug 2016 post; one world by setting LandmarksOfLandmarkWorld / LANDMARKS_WORLD,
      auto = the live world of WorldNameId, else Courage): the client's own landmark templates stamped once beside each
      Spire (marker TerrainEdits/landmarks_of_landmark.txt; the damaged aetherion_tower.otd repaired in memory), a
      server-owned site with the admin template guid set (record +0x158 → entry +0x138: the walk-on contest banner with
      name, competition and contributors; nobody may build there), template props + the award model streamed within
      320 m. VoxelTemplate.StampAt voxel order fixed (y first). trace/0695/landmarks-of-landmark-695.md; open: L111 —
      (forum, Jun - Aug 2016): contest-winning builds placed beside each island's spire
      on every world (list in the 3 Aug 2016 thread). Like the outposts, these are developer-placed designs.
- [x] **3.21 Game Table player counts** ✅ 2026-10-16 StoryTools.LiveCreaturesOn counts toward MinPlayers (not Max); every table site is already a game in the Finder — (dev reply, 29 Sep 2016): NPCs count toward the minimum players to start but
      not the maximum; every site with a Game Table is listed in the Game Finder.

From trace/0695/ui-trace-695.md (agent #11: 31 windows; 3.2 modular build mode is client-only).

- [x] **3.22 Inspect: other players' item tooltips.** Built 2026-10-15 (InventoryHandler.HandleTooltip: the location's
      owner guid → that player's equipped item); not exercised yet (needs a second player; TESTING-NEEDED). InspectWidget's equipment rollover sends
      InventoryPacketItemInstanceTooltipRequest with the inspected player's guid in the item location
      (EquipmentItemRenderer.as 53 → FUN_1435de7d0 → FUN_143519570). InventoryHandler.HandleTooltip
      (InventoryHandler.cs 189-199) only looks in the requester's inventory and drops it. Answer from the target's
      equipped items (only what StartInspect showed). Needs 3.4.
- [x] **3.23 First-time help for the two new UI events (0.695 sessions).** ✅ 2026-10-15: FirstTimeEvents.cs 33 → 43
      AppearancePurchase_FTE (Large, CenterR), 34 → 48 Mesh_FTE (Large, TopR): Large because both pages are wider / taller
      than 640×400 and the client's only own choice (PropHelpWindow.txt) is Large; 0.695 test: Ui.UIEventTriggered(34) /
      (33) open both pages (trace/0695/shots_fte_mesh_695.png, shots_fte_appearance_695.png). `Ui.UIEventTriggered(33)` from the Crimson
      Parlor unlock / purchase buttons (BarberShopWidget.as 401, 410) and `(34)` from the Build Mode Mesh tab
      (BuildModeWidget.as 337) are new; FirstTimeEvents.txt 0.695 adds 43 AppearancePurchase and 48 MeshTabHelp, help
      pages AppearancePurchase_FTE / Mesh_FTE. Add 33 → 43 and 34 → 48 to FirstTimeEvents.cs `uiEvents` / `pages`
      (pairing by name: verify the page position against the help page sizes).
- [x] **3.24 Game HUD progress-bar names.** ✅ 2026-10-16 (agent) bars sent (layout trace/0695/hud-progress-695.md); untested in game — The 0.695 score HUD prints each bar's `Label` (ProgressBarsDisplay.as 116,
      168) = the HUD entry's localized name, "Missing Text" when the entry has neither a string nor a known text id
      (FUN_143c1c7b0). Send a team / player name in every HUD progress entry and the 0.695 trailing bool (team entry)
      + u64 (player guid) so `IsSelf` and the "best other" bar are right (FUN_1434e2f70; #3 §B6).
- [x] **3.25 Game Finder window states.** ✅ 2026-10-15: Game/InstancedGames.Finder.cs (0x10D/0x19 → 0x1A SearchState 0 /
      3; counts per the window labels: searching players, games with a round on, games with players but no round, empty
      games; re-sent on change every 2 s sync). 0.695 test: Searching... / Players 1 / Stop, then options page / Search
      inactive. / Search (trace/0695/shots_gamefinder_search_695.png, _stop_695.png). Match found (state 1) = L31. Answer StartMatchmaking with MatchmakingUpdate SearchState 0 and
      StopMatchmaking with 3 (GameFinder.lua 48-60: 3 → options page / "Search", 1 → info page / "Search", other →
      info page / "Stop"), fill Players / ActiveGames / QueuedGames / InactiveGames, and send a new update whenever they
      change while the player is searching — `ShowGameFinder` only shows the last values received (FUN_1434ed2e0).
      Part of 2.12; joining a found match is L31.
- [x] **3.26 Site Management rows follow the upkeep data.** ✅ 2026-10-15: Claims.SendSiteRows (management data sub 8 +
      upkeep sub 10: the rows come from both) after placing, granting and removing sites; 0.695 test with the window open:
      placing turns the Place row into the site, lapsing turns it back (trace/0695/shots_site_rows_695.png). The 0.695 window builds its Place / Buy rows from Claims
      sub 10 (PlayerMaxRootClaims − owned Place rows; a Buy row while below MaxRootClaimsAllowed;
      ClaimManagementWindowDocumentClass.as 281-325). Claims.cs sends sub 10 on opening (0x1C, line 230), on 0x20
      (234), after a delete (901) and after Point-of-Need upkeep (535), but not after a placement (PlaceConfirm only
      calls SendClaimsByOwner): re-send it there too, and when the player's slot count changes. With 3.5.
- [x] **3.27 Tab scoreboard (0.695).** ✅ 2026-10-16 (agent) rebuild flag true (rows were never built), teams 1-based (slot 0 = FFA); untested in game — Tab now opens PvPScoreboard (InputProfile_Default.xml, global.lua 221-240)
      when `InstancedGame.IsInGame()` and the HUD state is normal: verify the scoreboard data (`GameData.
      InstancedGameScoreboard.*`, Scoreboard 0x10D sub 3) is sent to players and observers of a site game, and the
      end-game banner (state 7, functions-695 B3) closes / blocks it as the Lua expects.
- [x] **3.28 Underground Adventure from every zone.** ✅ 2026-10-15 checked: Caves.TravelRandom serves any landscape (re-zones to the home continent's caverns when elsewhere); the only refusal (no caverns / disabled) has no client text, ours. TeleportWidget no longer disables the button in newbie zones
      (TeleportWidget.as 120-124 vs 0.589 Ui.IsInNewbieZone). Check PacketTeleportRandomUndergroundRequest handling
      from every landscape (newbie, Pioneer, event islands) and the refusal message where no cave exists. With 3.14.

## 4. Items that exist only in 0.695

Item definitions come from the server (the client has no item list). Correction (trace/0695/items-695.md, agent #7):
gameData/items_summary.tsv is a live **0.695-era** dump (after 28 Sep 2016, before 6 Dec 2016), not 0.589; its login
list leaves out items the client names by id (4419, 4521-4523), pets, fireworks consumables, event cosmetics. The
item-definition protocol is identical in both builds. Every missing item needs a definition built from the 0.695 data
(model, icon, text, category):

- [x] Pets: Beetle, Bunny (Lop Ear), Cockatrice, Jackalope, Shadow Wolf, Stag figurines.
- [x] Fireworks: 22 emitters + the consumables. ✅ (4.12 / 4.13)
- [x] Reward Chests: Common + two higher tiers.
- [x] Outfits ✅ 2026-10-16 built (Cosmetics.Outfits, 22 outfits 900301-900343); tints ✅ 2026-10-17 (L103 closed: every built outfit worn by harness and matched to its icon, Merry Red / Festive Blue moved, a7b54d6; 2016 Green / Blue sweaters ada7c10): Skeletal Bodysuit (Orange...), Holiday Sweaters (Green & Tan, Red & Brown...), festive flight suits,
      Year Zero Survivor's Outfits, Colonist's Elegant Robe (Russet) rename, Tech Commander's / Tech Sergeant's Gear?
- [x] Halloween hats (unlockers), Fantasy Parlor Pack contents. ✅ (4.9 / 4.10)
- [x] The full list: every wiki content page with no client item ✅ 2026-10-16 trace/0695/wiki-no-client-item.md (165 item-like pages; fuzzy match: 29 do have a client item under another spelling — e.g. Canteloupes, Loveseat, "Recipe:" names — missed by wiki-xref, 69 only a client text, 17 variants the client lacks, 50 nothing in the client; system / meta / goal / list pages are covered elsewhere) (trace/0695/wiki-xref.tsv, client_item "-"),
      after fuzzy matching (spelling, numbered variants).

- [x] **4.7 Answer every 0x49** (2026-10-17: every item below is built and in the login list / 0x4A; live ids not on disk, L24 closed by design): ✅ 2026-10-16 built items (4419, 4521-4523, pets, 22 fireworks, 900001) are in the login list and 0x4A; titles, flight suits, sweaters ✅ (b964e2f), Thank You Token ✅ 2026-10-16 (own id 900401, texts 577640 / 577641, coin icon; mailed with an emptied Reward Chest); every built item is in the login list — when an id has no definition, the server stays silent (ItemDefinitionManager.cs:184-189)
      and the client keeps it pending (FUN_1434063a0). Build definitions for the items below and send them by 0x4A,
      and embed them in ItemAdd (ClientUpdateItemAdd.cs:23) for items not in the login list, as the client allows
      (FUN_142cd9980 case 1).
- [x] **4.8 Definition builder** ✅ 2026-10-15: Game/Inventory/ItemDefinitionBuilder.cs (template record, fixed-size
      field patches, literal-only LZ4, appended to the 0x4B list + 0x4A); first item Build Site Expansion 900001 (ours, L86).
      0.695 test: client logged "Received item definition data, id: 900001", used it from the bag, Site Management showed
      two Place rows (trace/0695/shots_site_expansion_695.png). Was:: write records from data (same layout as re/items_decode.py, LZ4 + sizes) instead of
      only replaying the dump, so new items (4419, 4521-4523, pets, fireworks consumables, Thank You Token, titles,
      flight suits, sweaters) can exist. Template each on the closest dump item (title → 4519, consumable → a type-4
      ability item, unlocker → a NO_TRADE-free stackable).
- [x] **4.9 Fantasy Parlor Pack 4419** (2026-10-17: sold in the Showcase store for 650 Lumens (6.60, Store.MarketplaceProducts); left: the in-client Marketplace product so the Parlor shows MarketName / MarketCost — not built; the visual parlor check): ✅ 2026-10-16 (agent, L39 answered: set A = option ids type 3, set B = ArtTint ids type 4) Unlockers.cs; 0.695 test: used + consumed, relog with filled lists OK; open: Marketplace product, visual parlor check (user). definition (name 577355, description 577356 / 577361, category 289 Parlor Option), (2026-10-18: in-client store product 1c91d45, untested in game)
      use → unlock CharacterCreateOptions rows with UNLOCKING_ITEM_ID 4419 + ArtTintGroupUnlock 2, 5-9 (their 45
      tints); persist per character; send 0x11C sub 2 and the set in SendSelf (+0x7ae28); refusal 577416 / 577364 via
      0x2D sub 0x1E. Marketplace product so the parlor shows MarketName / MarketCost.
- [x] **4.10 Hat unlockers 4521 / 4522 / 4523** (2026-10-18: drops follow the Halloween season by default, HAT_DROPS toggle, b24efca) ✅ 2026-10-16 definitions + use + refusals (agent); drops: 4522 / 4523 at 5 % from Lost Crystals and Superb / Legendary chests (RareDrops, rates ours) (Cowboy Hat / Ninja Mask / Samurai Helmet, icons 21775-21777, texts
      577699-577707): definitions, use → unlock option 295 / 305 / 301, refusals 577705-577707; drops from Outposts and
      the Superb / Legendary chests (P6505), tradeable.
- [x] **4.11 Pets** (2026-10-17: the six in the store at 650 Lumens, placeholder price (6.60, L147); live ids L24 closed by design): ✅ 2026-10-16 Pets.cs (3.10) + Lop Ear 2 % from Outpost elites (RareDrops); open: live ids (L24), Marketplace for six — ids (L24), definitions with icons 21734-21740 and texts §1.3, summon / dismiss (L27), Marketplace
      for six, Lop Ear Bunny as a rare Outpost-elite drop (P6143).
- [x] **4.12 Fireworks consumables (22)** (2026-10-17: L24 / L25 closed by design — ids ours, the launch plays the emitter composites 12836-12857 [I]; the Lumen Station section is the client's own recipe table, recipe 5332 crafted 4.20): ✅ 2026-10-16 (agent) Fireworks.cs: packs, Unstable pack, 22 consumables (candidate ids, our abilities), launch at the player, gather drops of emitters; 0.695 test: pack 4444 → 5 × 4421-4424, Ui.AbilityStart(1215) consumed one and the client loaded EMIT_PROP_Fireworks_Standard01.fsb at that second; open: live ids L24 / L25 — ids (L24), definitions with icons 21689-21711, the launch effect (L25), Lumen
      Station "Fireworks" section; pack contents for 4444-4449 (descriptions) and Unstable pack ability 1237 (50 random).
- [x] **4.13 Fireworks emitters**: ✅ 2026-10-15 (PropEffects one-shot, StoryTools 577506) place props 2949-2970, the "...Launch this firework." action (577506) plays
      InteractableCompositeEffectSets 129-150; random-drop source (P5915); emitter recipe items teach via grantId.
- [x] **4.14 Reward chests** ✅ (3.11) (with TASKS 3.11): Common / Superb / Legendary craftable at station 55 (recipes 5362-5364),
      settings, loot per tier, group loot, owner can't loot, lockout, use-up mail + Thank You Token, pickup refusal
      (Prop.PropPickupErrorPartialLootChest 577600). Old tiers 4511-4513 / props 3004-3006: keep working if owned.
- [x] **4.15 Roll Wraps 4406** (2026-10-17: L26 closed — Roll Wraps were never released (tracker goal of recipe 5331 missing from the capture), so admin-only as on live) ✅ 2026-10-16 abilities wired (b964e2f: caster info + HandleCast answer boot abilities while worn, never a weapon swing); left: where Roll Wraps came from (L26) —: the roll boot ability (u59 90) and where it was obtained (L26). 2026-10-16: u59 =
      DynamicInputActionSystems id → abilities 1207-1214 (cosmetics-695.md §3, data in Game/BootAbilities.cs); left: wire
      IsBootAbility into MiscHandler caster info + Abilities.HandleCast (as 326), and the source (L26).
- [x] **4.16 New titles** (built b964e2f; 2026-10-17 PlayerTitle field order fixed, 16a789f — titles never showed before; the look with a free bag untested in game; ids ours, L24 closed by design) —: Champion of the People (4519, grant 60) is in the dump; The Lumerian Lens, Hero of Landmark, Client check 2026-10-16 (split test stack, HEAD 88d9bd5): give 900401 / 900201 / 900203 / 4519 / 900301 / 900335 all reached the client — its full bag sent them to overflow ("Overflow Inventory!" dialog), so the item adds were accepted; 900401 has no client definition until the next full login (that session's list came from the older image; the client asks 0x49 when it needs the icon). Bag / title / outfit look still to check with a free bag (TESTING-NEEDED).
      Landmark Explorer, Holiday Hero, Creator of Winter Wonderlands, Icy Architect need ids / definitions (L24);
      check Titles.cs knows title 60. 2026-10-16: built (Cosmetics.Titles, own ids 900201-900206) + Titles.cs comma-suffix
      fix (4519 had no title text); client test pending (cosmetics-695.md §4 / §6).
- [x] **4.17 Holiday cosmetics 2016** (built b964e2f incl. the reward-chest seasonal hook; 2026-10-17 tints worn and matched by harness, L103 closed (a7b54d6, ada7c10); the bag look untested in game) —: Colonist's Flight Suit ×3 (577737-577740) and Holiday Sweater Green / Blue Client check 2026-10-16 (split test stack, HEAD 88d9bd5): give 900401 / 900201 / 900203 / 4519 / 900301 / 900335 all reached the client — its full bag sent them to overflow ("Overflow Inventory!" dialog), so the item adds were accepted; 900401 has no client definition until the next full login (that session's list came from the older image; the client asks 0x49 when it needs the icon). Bag / title / outfit look still to check with a free bag (TESTING-NEEDED).
      (577741 / 577742) as Outpost drops from 6 Dec 2016 (P6566-P6570); Skeletal Bodysuits and the three older sweaters
      as seasonal drops (P6506, P6567). Ids / tint aliases: L24. 2026-10-16: built (Cosmetics.Outfits, own ids 900301-900343,
      tints [I] L103); LANDMARK_SEASON drops wired in Outposts; chest hook RareDrops.ChestSeasonal for RewardChests (hand-off).
      Correction: Green / Blue 2016 sweaters were giveaways, not drops (P6570).
- [x] **4.18 Recipe items by grantId**: ✅ (5.8) RecipeBook links "Recipe:" items by name (RecipeBook.cs:37-75) with 0.589 text,
      which cannot name the 22 emitter recipes; use the record's grantId (prop id for 526 recipe items, recipe id for
      58) and parse it in ItemDefinitionManager.Parse.
- [x] **4.19 0.695 text on the server** ✅ 2026-10-15 (gameData695/en_us_data.dat, 26 492 entries; see 2.35): TextManager reads gameData/en_us_data.dat = the 0.589 file; the 63 0.695-era
      items have no server-side names (mail, chat links, name matching). Load the 0.695 text (it is a superset for
      these ids) or gate by build.
- [x] **4.20 0.695 recipe / prop tables per build** (2026-10-15: the 0.695 server now reads the 0.695 tables, gameData695;
      0.589 clients are not served by this branch, so per-build lists are only needed if they come back; crafting the new
      recipes ✅ exercised 2026-10-16: on the test stack the client's Recipe.RecipeListSelectStation(55) opened the Lumen Station, and 0.695 recipe 5332 (Pack of Unstable Fireworks, 130 Lumens) crafted; the client knew item 4443 from the login list): Recipes (+33 / -1 / ~13), RecipeComponents (+10 / ~3),
      RecipeRecipeComponentMaps (+36 / -1), CraftingStationRecipeMappings (+32 / -2), PropDefs (+82 / ~15),
      PropCategoryPropMap (+58): the server reads the 0.589 copies; serve 0.695 crafting (fireworks, chests, Lumicite
      750) to 0.695 clients and keep 0.589 lists for 0.589 clients.
- [x] **4.21 Gate 0.695-only items for 0.589 clients** n/a on client-0.695 (only 0.695 clients connect; 0.589 is frozen on its branch) —: the 63 items (no 0.589 name, 57 without 0.589 icons) and props
      2940-3013 must not be granted / placed for a 0.589 client (or must degrade cleanly); the login list itself is
      fine as it is.
- [x] **4.22 0x2D sub 0x1E ShowActivationFailure {u32 text id}** ✅ (ItemUses.Refuse, seen on 0.695) (0.695 only): use it for every refused item use on 0.695
      clients (already-unlocked parlor items, pets where summoning is refused…); layout check by #3.

## 5. Whole-wiki cross-reference (in progress)

- [x] **5.1 Gear stats** ✅ 2026-10-16 (agent, trace/0695/gear-stats-695.md) armour weight → Life max variable + stats 1 / 10; 0.695 test: heavy armour, a 450 hit dropped the bar ~27 % (max ~1800); numbers ours — (wiki: Armor Weight, Armor, Affinity): max life and damage should come from the equipped
      armour (light: small life / large damage, medium: balanced, heavy: large life / small damage) and items. The
      server keeps max health at a fixed 900 (Health.cs). Trace the client's stat path first (CalculatedStatDefinitions,
      ItemCategoryStatMappings, DefaultCalculatedStats, the character sheet's stat lines) for how the live server sent
      max health and damage stats.

- [x] Review every page in trace/0695/wiki-systems.md (311 feature / rule pages) against the server; write the ✅ checked 2026-10-16: all 282 listed pages ticked (section totals 31+5+101+6+22+28+46+19+12+7+2+1+2; 311 was the count before page de-duplication), results in FEATURES.md (115 rows), gaps already filed here as 6.x items;
      result into FEATURES.md and add each gap here.
- [x] Content pages: confirm every matched item can be obtained the way the wiki says (innate, discovered, Lumen
      Station, drop, Marketplace). ✅ 2026-10-16 (agent; trace/0695/item-sources-audit-695.md, scripts re/out/agent_item_sources): 1,410 pages with a client item — 1,356 as the wiki, 17 wiki silent (server follows the client), 33 a different route where the 0.695 client data overrules the wiki (12 Barren creatures innate, L44 updated; 15 props the wiki calls innate are discovered; Emerald outfits at the Workshop…), 3 not obtainable in the 0.695 data either (Sharp-Eyed Elixir, Colonist's Patrol Armor Purple / Orange), 1 overview page; 930 pages without a client item counted by type. Fixed: goal-only recipe items (3906 + the seven 500-kill tier recipes) and the retired wisp recipes (3931, 3956-3958, 3977) no longer drop at random (RecipeBook.DropsAsRecipe, Achievements.IsRewardItem). New: 6.59, 6.60, L108.
- [x] Goals: compare the wiki's 184 goals (trace/wiki-goals.md) with the server's goals and rewards. ✅ 2026-10-16 (agent; trace/0695/goals-audit-695.md): 183 wiki rows — 175 followed, 2 followed with the capture over the wiki, 1 not built (Iron From Ice → 6.58), 3 wiki-only tutorial goals with no live record (→ 6.57); fixed: Water line name texts swapped (Chill Out / H2-Ouch), Gosh It's Hot counted (fireplace in a volcanic biome, inferred), objectives a capture map names are never counted by text.

- [x] **5.2 Exploration checks** (2026-10-17: the outpost banner built — outposts go to the client as client-only site records, its own walk-on banner shows the name and "Created by" (OutpostSites.cs, 9a5894e, E.15; L102 closed; look untested in game); party loot sharing built (LootChests.ShareRange, Gathering.PartyShareRange; no two-player test)) ✅ 2026-10-16 day length 8× (wiki, WorldClock); outpost name shown as a chat line today — live showed the client's site-entry banner (name + creation date) because outposts were player build sites: L102; Foundations Museum world ✅ resolved 2026-10-16: closed at launch (P4235, 21 Mar 2016 "The Foundation Museum islands are no longer accessible"); its zones 431-434 (continents 424-427, texts 571186-571189, surface caches shipped) stay out of the Landscapes list, as Landscapes.cs already does — (wiki: Servers and Landscapes, Outposts, Chaos Caverns, Astronomy): the visit-only
      Foundations Museum world (contest winners); the outpost's name and builder shown when you approach it; party
      members sharing loot in the caverns; day length (wiki measured 3 real hours = 8× with sun 04:00-20:00; the
      server runs 9.1×) — find the source of 9.1 or switch to the client's own cycle constant.

- [x] **5.3 Vault size** (wiki: Vault): the wiki says "default 80, maximum 120"; the developers' notes say "New Vault
      size: 60, max 120" (P3254, 21 Mar 2016). The server (60, +20 per pocket, 120) matches the developers. Closed.

- [x] **5.4 Commands players used** (2026-10-17: the rest built — public channel on / off 0x22 / 0x23 and bubble colour 0x15 (E.14, merge e-chat, harness: General leave / join, bubble colour), RequestPlayerPositions 0x24 → 0x25 (9fdb0d9; untested in game)) ✅ 2026-10-16 /afk (Game/Afk.cs), 0x49 answered (4.8); /random 0x5B (6886654, Game/RandomRoll.cs: server rolls, CommandPacketRandom to roller + say range); /who ✅ 2026-10-16 (2d9d66a, a world command answered with WhoReply 0x02; client test: the client's own /who ran it, 1 player); friends on the map 0x18 → FriendBase sub 5 (35f757e, Game/FriendPositions.cs); ExecuteCommand 0x47 ✅ (9e59ec8); chat bubble colour 0x15 and channels on / off 0x22 / 0x23: Lua natives exist (Ui.SetChatColorFgBgSz, Ui.ChannelOn) but no UI script calls them (REACHABLE-GAPS) — left; friend confirm 0x13 / 0x14 ✅ 2026-10-16 (132c846 + c0198d2: AddFriendRequest → ConfirmFriendRequest {u64, name block, name block, bool} = the client's GAQ popup, answer 0x14 {u64 asker, accept, name block}; FriendMessage 5 sent / 1 accepted / 2 declined / 3 timed out (2 min, ours) / 8 repeat; in-process and social service. Client test 2026-10-16 (isolated stack + FakeWorld 92): /friend add Wanderer → the 0x13 reached world 92; Wanderer's accept → saved, friend-of list pushed; a request from world 92 shows the 0.695 client's own "Wanderer has asked to be your friend." Accept / Dismiss popup (first try with plain strings: dropped by the client, fixed c0198d2). Not run: clicking Accept / Dismiss (needs input), TESTING-NEEDED); RequestPlayerPositionsReply 0x25 is read and freed by the client handler (FUN_14330e110 case 0x25 → FUN_143317fd0) — nothing to show; /random 0x5B is an item dev command ("usage: /random <item def id> …"); open: chat channels / friend confirm / bubble colour / /who (Social service, Phase B.1), ExecuteCommand 0x47 — (wiki: Commands List, Controls; trace/systems/commands.md lists the unhandled
      Command 0x1E subs): /afk (0x3B → PlayerUpdate 0x57), /random (0x5B), /who, chat channels on / off (0x22 / 0x23,
      custom channels), confirm friend request (0x14), friends and players on the map (0x18 / 0x24), chat bubble
      colour (0x15), ExecuteCommand (0x47), and **ItemDefinitionRequest (0x49 → 0x4A)**: the client asking for an item
      definition it doesn't have (needed for every 0.695-only item, section 4).

From trace/0695/wiki-content-audit.md (agent #9: 1,575 content pages — 1,454 present & obtainable; 184 goal pages, server counts 512 of 1,191 objectives; per-row tables in re/out/agent_wiki/). Verified by the coordinator: RecipeBook.LearnProp (RecipeBook.cs:185-200) never reports to Achievements.

- [x] **5.5 Getting Started chain stalls at goal 1** ✅ 2026-10-16 (agent) hooks for 1322 / 1315 / 1307 / 1308 / 1309 (§3.4; Achievements.cs:42, :246, :338-363): count 1322 when the
      player tracks 480 Props To You (AchievementBase sub 0x0C with 480 in the list), 1315 when a Cozy Campfire is placed
      (Consumables / Props.PlaceFromItem), 1307 on a Like given at a gallery-published site (Ratings), 1308 on a travel to
      another player's site (Travel / Commerce gallery visit), 1309 on a grappling-hook fire outside build mode and
      combat. Verify by the client's tracker advancing to Becoming a Luminary.
- [x] **5.6 Goal tiers the capture lacks** (2026-10-17: the Lost Crystal tiers count — every objective of the 958 goals has a counting path, E.16 / merge e-goals) ✅ 2026-10-16 (agent) 38 tier records (Achievements.Tiers.cs); Getting Crafty / Building Block Basics / Modular Thinking built (8b4d3d9; the dev world logs "3 tutorial goals made"); open: Lost Crystal tiers count (5.10) — (§3.3, L42): build the 28 combat tier records (575-617) and the 13 other
      goals from the client texts + wiki counts and rewards (Lumens 35 / 80, score 10 / 15, recipe and title items,
      §3.1), and send AchievementAdd (0x92 sub 2, reader FUN_142d8c370) when the previous tier completes ("Unlocks …" on
      each wiki page). Layout of sub 2: hand-off #3. Unblocks 595 and 618 and 17 title items (§3.5).
- [x] **5.7 Prop recipe items count no goal** ✅ 2026-10-16 (agent) PropRecipeLearned (RecipeBook.cs:185-204): LearnProp must call Achievements.RecipeLearned
      (recipe-item name and "<prop item> recipe unlocked"); 307 objectives (303 Recipe Discoveries + Winter is Coming).
- [x] **5.8 Recipe items by grantId for props** ✅ 2026-10-16 (agent; type 6 / 45, grant = record len − 20 verified on all 2,885) (extends TASKS 4.18): link every "Recipe:" item through its grantId
      (526 prop grants, 58 recipe grants) instead of the name (RecipeBook.cs:37-75); fixes the 13 props everybody can
      place today (§2.8), the 5 items that teach a station-less type-3 recipe instead of their prop, and the 29 Recipe
      Discoveries objectives whose recipe item the name link misses (objectives.tsv).
- [x] **5.9 Meta goals by the capture's map, not by text** ✅ 2026-10-16 (agent, with 5.6) (Achievements.cs:883): the 8 meta records carry objective →
      child achievement pairs (decode_achievements `map`); use them. Fixes 11 objectives now — 645 Best Harvester EVAR and
      655 Ridiculously Good Outfits ("… complete" ×8), 566 "The Bigger They Are … completed" → 561, 510 "Weeping in the
      Willows completed" → 496 Weeping Willow, 526 "Always Like Going South completed" → 524 Trees With Beards? — and the
      595 / 618 children of 5.6 whose texts differ ("If It Bleeds … Completed", "Between a Rock … Completed").
- [x] **5.10 Goal triggers not built** (2026-10-17: Iron From Ice built (6.58, d2327b4, blue [I strong]); item-name tracker goals built (E.16: all 1,269 objectives have a counting path, incl. the 186 Replicator tracker goals; untested in game); 11 riddle goals count by rules read from their wording, L45) ✅ 2026-10-16 (agent, Achievements.Triggers.cs); 2026-10-16 re-checked against the code: Holiday Like counts (TagDefinitions 604 "Holiday" is a Genre tag, stored by name; LikedTaggedSite), the rug recipes / placements count (PropRecipeLearned and PlacedNames reorder "Rug (Large Rectangular)" → "Large Rectangular Rug", "Shag Rug (Sliding)" → "Sliding Shag Rug"; the Ornate rug's "Rectuangular" typo is the item's own) — re/out/agent_wiki/objectives.tsv's "no" marks predate those; open: Iron From Ice (L45), item-name tracker goals — (objectives.tsv "no server code counts this text" / "no prop item of that name";
      wiki hints in goals.tsv): <Faction> characters placed ×6 and "<X> monster placed" ×4 (placing creature props of a
      faction); Props placed (1000), Replicators placed (5), Props tinted (5); decor "placed" names that are not item names
      (Large Rectangular Rug, Painted Wooden Table / Chairs, Mugs, Sliced Roast, Sliced Bread with Cutting Board, Square
      Platter, Medium Candle Group, Fjorden Book, Shelf Fungus / Glowing Mushroom names: map to the props); linking goals
      (Any two props linked, Timer Object / Floor Switch linked, REALLY nice bathroom link); Prop placed in <biome> ×6;
      Door painted red / black, One is red / black; "<creature> unlocked" ×18 (creature recipe learned); Lost Crystal
      reached (622; only the daily counts it, DailyGoals.cs:148); Adventure landscape Build Site claimed (511); Boom
      knowledge obtained (637: wiki "Use High Yield Dynamite"); the 12 emote challenges 492-503 (wiki: /drunk in a
      desert, /flirt alone or in a group, /dance in a tundra, /angry in a jungle with a group, /talk in a group…);
      Revenge taken (wiki: Darkfang Specter killed in a tundra), Fixed a bad match (Frostbite Giant killed in a volcanic
      biome), Clothing malfunction (armour at 0 % condition), Breeze Boots (crafted), riddle Likes (Western / sci-fi /
      fantasy sites), "Like" a Holiday site (tag missing from the tag list 576620-576645), Thank You Tokens (0.695,
      TASKS 4.14). Riddles the wiki marks with "?" stay open (L45).
- [x] **5.11 "Monsters killed" counts every creature** ✅ 2026-10-16 cave monsters only (P4801 over the wiki) — (Achievements.cs:757-765, Creatures.cs:817): the wiki (Hunting
      the Beasts / Murderer of Monsters: "Monsters include Slaugs, Abominations, Chompers, and the exploding mushrooms.
      Other mobs do not count") limits it to cave monsters, while P4801 (26 Apr 2016) says Fiery Fungi update no kill
      goals; the server counts any kill (Barren, kobolds, story creatures) and fungus kills by weapon. Reconcile with #4.
- [x] **5.12 Event and promo items with no route** (2026-10-17: Year Zero Trophy, Iron Portcullis and Void Vault decided and built — mailed to every character once by the founder / Year Zero / giveaway entitlements (6.59, FOUNDER_ENTITLEMENTS); left: the Colonist's Elegant Robe (Green) still has no route outside FREE_STORE or an owner's StoreLumenPrices entry): (2026-10-16: the robe's recipe 2545 can't be offered at a station — the client lists station recipes from its own CraftingStationRecipeMappings; Year Zero Trophy / Iron Portcullis / Void Vault: decisions asked in TESTING-NEEDED) — Colonist's Elegant Robe (Green) (wiki Lumen Station 520 Lumens; r2545 (2026-10-18: 2551 had no live route, closed by design; docs/gameplay.md)
      has no station and is not default-learned), Year Zero Trophy 2483 / prop 2043 (mailed 31 Jan 2014), Iron Portcullis
      406 / prop 823 (beta contest code), Void Vault prop 407 (Founder's Pack bonus, free for everyone today): decide per
      item (mail grant, account entitlement, or keep free) and record it.

## 6. Launch-era items (21 Mar - 4 Jun 2016) to verify

First pass 2026-10-15: the 303 launch items that state a rule (numbers, costs, rates, limits, drops, rewards) were
read against the server. Gaps found:

- [x] **6.1 Lost Crystal channel 10 s** ✅ 2026-10-16 (P3529, 13 Apr 2016: "channel power from the Lost Crystal for 10 seconds");
      Outposts.ChannelTime is 5 s.
- [x] **6.2 Site prop limits** ✅ 2026-10-16 125 spawners (category 98 row in the Build Mode limits, error 12); total stays 20,000 = the 0.695 client constant Claims.PropLimit — (P4195-P4198, 25 May 2016): 100,000 props per site (Story Nodes don't count), 125
      creature spawners per site (was 7,000 props, P3556). Only the 50-chest limit exists (PropLimits.cs).
- [x] **6.3 Lumen and recipe pacing** ✅ 2026-10-16 Pacing.Factor session curve (ours); ✅ 2026-10-16 daily gear recipe limit (P4438, 1 Apr 2016 — the real id; "only get 1 armor or 1 weapon recipe per day"): RecipeBook.RandomUnknownRecipeItem (every random recipe drop) skips armour (chest, slot 8) / weapon (slot 2, not a tool) recipes once one of that category dropped today (UTC day; character_unlock kind 24 Unlocks.RecipeDropDay, survives relog); read as one of each [I], reset hour unknown (L119); untested in game — (P3477-P3479, P3584, P3730-P3734, P4295, P3431): no hard daily cap, but lumens
      come slower early in a session and more over long sessions (always more than repairs cost); recipe drops paced
      and limited to 1 armour + 1 weapon recipe per day (all unlockable in under three weeks); common materials (dirt,
      sand, ice, stone) can drop lumens and prop recipes; harvest bonus also raises lumen / recipe drops. Check
      Gathering / Creatures / LootChests drop code against each.
- [x] **6.4 Creature aggro** ✅ 2026-10-16 Creature.Threat / NoteDamage, random target within 40 m (RandomTargetRange) (P3651-P3652, 26 Apr 2016): attack the highest damage dealer; an undamaged creature picks a
      random target within 40 m. Creatures.cs takes the nearest player within 12 m.
- [x] **6.5 Creature loot rules**: ✅ 2026-10-16 campfires 5 %, fungus tree + rare loot, no loot from creature kills (6.20), Play Mode exit despawns without loot, fungi excluded from kill goals (5.11) — NPCs can drop Cozy Campfires (P3583); Fiery Fungus drops tree loot with a low chance
      of rare loot, plus terrain loot when it explodes (P3522); no loot when an ally or neutral creature makes the kill
      (P3676); leaving Play Mode drops nothing (P3390); Fiery Fungi don't count for kill goals (P3758; later "You Can
      Kill Those Things" counts fungus deaths nearby, 7 Jun).
- [x] **6.6 Spawner respawn time** ✅ checked 2026-10-16: story spawners use row 18 (0-300 s, step 5, default 30) — setting up to 5 minutes (P3675); Creatures.RespawnTime is a fixed 3 min for world
      creatures — check the spawner setting range.
- [x] **6.7 Durability pace** ✅ checked 2026-10-16: rates come from DurabilitySet.txt (identical in 0.589 / 0.695, both after 26 Apr) and the station's 0.004 Lumen/point; gathering tools never wear; the 0 % strength (½) is ours — (P3728-P3729, 26 Apr 2016): items take nearly twice as long to reach 0 %, repairs cheaper,
      items a bit more functional at 0 % (P3803); Founder's Pickaxe never damaged (P3791). Compare Durability.cs rates.
- [x] **6.8 Every Chaos Cavern has an Outpost** ✅ 2026-10-16 built (e181565, Outposts.FillCaverns; tested 2026-10-16 on the isolated test world from empty data: all 81 stamped in ~4 min, heap ≤ 0.95 GB, 113 MB of terrain edits, no warnings; the client's own help page ChaosCavern_2Outpost_FTE says "Every Chaos Cavern has at least one Outpost in it" too; the client reached outposts 0 / 40 / 70 / 80 — 40-80 are copies — ruin, crystal and garrison present (killed by outpost 80's garrison); dev world: 46 more stamped at its next restart): the 81 Chaos Caverns are the .cpa's CavernInstanceData (= the 81 portal crystals; the 520 "caverns" are cavern pieces); 35 had a ruin, each other cavern now gets one copy of a ruin fitting one of its free slots (fewest copies first, never the same ruin twice in one cavern, P4971), each ruin decoded once for all its copies (startup cost stays ~35 loads); crystal lockout index widened to 7 bits (old rows read the same). Iron: already in the 0.695 vein data the server uses (vein 247 surface iron, 5,314 on 406; 240 cave iron density 30, the highest with silver); bottomless caverns: random trips stop at MinTravelDepth. — (P4808, 26 Apr 2016) and Iron deposit changes (same post: read in
      full).
- [x] **6.9 Weapon balance passes** ✅ 2026-10-16 (205a202): per-weapon Basic / Power factors and Power Attack costs (WeaponAttacks.Balance), channel energy over time — (26 Apr and 25 May 2016, P3678-P3727, P4220-P4288): per-weapon damage / energy
      changes, melee weapons give slight passive damage resistance, heavy / medium armour raise weapon damage, new
      weapons (Frenzied Slayer's Battle-Axe, Electrified Assault Axe, Unflinching Embodiment of Might, Crusader's
      Courage...) with their attack descriptions. The server's per-weapon numbers are ours: check them against the
      relative changes and the attack descriptions (WeaponCombat.cs).
- [x] **6.10 Goal reward changes** ✅ 2026-10-16 (205a202): the five 500-kill element goal rewards after the 11 May 2016 hotfix (Achievements.Tiers.cs); recipe discovery goals count since 5.10 (Achievements.Triggers); starter designs refuse Delete (Templates.Delete, P4789) — (P4029-P4034, P4140-P4143): Eternal Night → Silent Shadow's Cloak, Praise the Light →
      Lightbringer's Oath, Assassin's Choice → Desperado's Deadly Poncho, 1.21 ...Something → Overcharged Recon Gear;
      goals exist for every discoverable recipe (P3755); free tutorial designs can't be deleted (P3746).
- [x] **6.11 The remaining launch items**: all 1,625 launch-era items classified against the server
      (trace/0695/launch-rules-audit.md, per-item table with evidence): 241 not followed, 52 not built, 15 superseded,
      203 followed, 1,114 client-only. New gaps 6.12-6.56 below (item ids are from the current patchnotes.tsv, P4xxx /
      P5xxx; 6.1-6.10 quote older P3xxx ids). Open leads L10-L12 added.

### Gathering and loot

- [x] **6.12 Plants are decoration; sickles gather nothing** ✅ 2026-10-16 (PlantsHarvestable = false) (P4128, 21 Mar 2016, "Landmark Update Notes - March 21":
      "Plants are no longer harvested, and sickles are no longer used for anything."): Gathering.cs still spawns
      harvestable plant nodes (PlantKind, PlantDensity 0.04 in Region()) and Gathering.Reap gives plant items to sickle
      swings (Abilities ReapingAbilities). Must: no plant nodes (plants stay client decoration), sickle swings gather
      nothing.
- [x] **6.13 One wood per biome; Old Growth gives Burled Wood** ✅ 2026-10-16 (P4628, 13 Apr 2016, "Update Notes - April 13":
      "Trees within a biome should now more consistently drop the same type of wood"; P5415, 11 May 2016, "Hotfix Notes
      - 11 May": "All old growth forest trees should now be giving burled wood"): Gathering.Region (~line 552) gives
      Old Growth (biome 50) small trees Plain Wood (71) and only large ones Burled Wood (72). Must: every Old Growth tree
      gives Burled Wood; one wood item per biome.
- [x] **6.14 Axe perks and Harvest Bonus on trees** ✅ 2026-10-16 (P4131, 21 Mar 2016: "Picks and Axes now have different abilities
      such as increased speed, damage, or resource bonus"; P4389, 1 Apr 2016, "Hotfix notes - April 1": harvest-bonus
      items "granted more tree resources than intended", i.e. they apply to trees): Abilities.cs:247 calls
      `Gathering.Chop(session, guid, aimedAt)` without the tool item, so the Charmed Axe (3260, luck), Goliath Axe
      (3261, yield) and Hotfoot Axe (3263, speed burst) never work; Chop also ignores Harvest Bonus (stat 17), which
      mining applies (Abilities.cs:715). Must: pass `source.ItemDefinitionId` and apply stat 17 to wood.
- [x] **6.15 No mining or building next to a Portal Spire** ✅ 2026-10-16 Spire.MayEdit (platform + 10 m, ours) in Excavate and ZoneTerrain.MayApply, text 560944 (P4425, 1 Apr 2016, "Hotfix notes - April 1": "If you try to
      mine beside the spire you should now get clearer feedback"; client text 560944 "You are near a Portal Spire. You
      must move away before you can mine or build."): Spire.Covers is only used for tree placement (Gathering.Ground) and
      teleports; Abilities.Mine, Building and TerrainEdits.MayApply allow digging and building around the Spire (only
      its stamped base is kept from regrowth, TerrainEdits.MarkPermanent); text 560944 is never sent. Must: refuse
      mining and building inside the Spire's area with text 560944. (TASKS 3.17 "no ore nodes around spires" is the
      later, separate rule.)
- [x] **6.16 Lumens and recipes from combat; Chaos Cavern monsters drop resources** ✅ 2026-10-16 kill Lumens / recipes; ✅ 2026-10-16 cavern resources (Creatures.CavernResource): every Chaos Cavern / outpost garrison kill adds one random underground resource (wiki Resources: 1-3 Iron / Silver / Gold / Obsidian / Alabaster), 8 % a rare gem instead (Ruby / Emerald / Diamond, "rare" [I]); Elites roll three times; rates ours (L120); untested in game — (P4169, 21 Mar 2016: "Any activity -
      such as harvesting or combat - that gets you loot has a chance of giving you lumens and recipes"; P4166 / P4224
      recipes unlocked "as you fight"; P4353, 1 Apr 2016: "Enemies in Chaos Caverns now drop random resource rewards";
      P4536, 13 Apr 2016: drop rate of rare resources on Chaos Cavern NPCs): Creatures.RollLoot is a fixed per-kind list
      with no lumen or recipe roll; cavern Chompers / Slaugs / Abominations drop essences only (Creatures.CavernKinds),
      outpost garrisons copper + Adventure Essence (SpawnerKind). Must: lumen and recipe chances on kills (paced like
      6.3), random resources (some rare) from cavern monsters. Extends 6.3 / 6.5.
- [x] **6.17 Essence pacing** ✅ 2026-10-16 gathering essences on the curve; ✅ 2026-10-16 loot essences (Pacing.PaceEssences: counts × Pacing.Factor, the fraction rounded at random) on creature kills, fungus blasts, Lost Chests, Lost Crystals and treasure chests; untested in game — (P4776, 26 Apr 2016, "Hotfix notes - 26 April": "You now earn fewer essences (monster
      essence, adventure essence, fire essence, etc.) at the beginning of a play session, but more over longer
      sessions"): essences are flat chances (Gathering.EssenceChance 0.03, creature loot tables). Must: the same session
      curve as lumens (6.3).
- [x] **6.18 Repair Bot Beacon as rare global loot** ✅ 2026-10-16 RareDrops.Beacon (creatures, rich gathering; 0.5 % ours) (P4835, 26 Apr 2016: "Repair Bot Beacons can also drop rarely as
      global loot"): no loot path gives item 4129 (searched Gathering.RollDrops, Creatures.RollLoot, LootChests,
      Outposts chest / crystal loot). Must: a rare beacon drop wherever loot rolls.

### Creatures and combat

- [x] **6.19 Cozy Campfire rules** ✅ 2026-10-16 combat / full heal / 1-minute refresh; ✅ 2026-10-16 airborne refusal (P4702): the client does not block it (cast errors FUN_14351ebd0 have no airborne case; no client Requirement tests jumping / falling), so Consumables.LightCampfire refuses (CastFail, item kept, a chat line) while the latest movement update rises / sinks faster than 2.5 m/s (WorldSession.LastVelocity from type 0 / 2 bodies; threshold ours; the client's own jump / fall flags are not sent as far as traced, L121); untested in game — (P4190, 21 Mar 2016: "If you're near the campfire while not in combat, you'll
      immediately be healed to full. (Campfires have no effect during combat.)"; P4539, 13 Apr 2016: "Enduring Warmth
      buff can now be refreshed every minute"; P4702, 26 Apr 2016: not castable "while jumping or falling"):
      Consumables.UpdateCampfires heals 4 % a second within 8 m even in combat, and gives Enduring Warmth (10 min) only
      at full life while no heal-over-time runs; LightCampfire has no airborne check. Must: nothing during combat
      (e.g. Health.HurtWithin as Outposts uses), full heal at once out of combat, Enduring Warmth refreshable once a
      minute, refuse lighting while airborne (check whether the client already blocks it).
- [x] **6.20 Ally creatures fight enemies; combat before scripted walks** ✅ 2026-10-16 (agent; also: every palette creature prop (category 98) is now a story creature) (P4223, 21 Mar 2016: "Ally creatures will
      assist players against enemies. And neutral creatures will simply watch"; P5225, 4 May 2016, "[Feedback] Prop
      Linking", dev: "Combat AI has the highest priority on all creatures"): Creatures.Think only ever targets players
      (allies and neutrals just leave players alone), and in Idle a story walk comes first and "ignores players on the
      way". Must: allies attack enemy creatures near them and enemies attack allies; combat interrupts walks. (Then
      P4719 applies: no loot when an ally or neutral makes the kill, 6.5.)
- [x] **6.21 Creature AI details** (P4715, 26 Apr 2016: "Creatures who return to their spawn point should now also face ✅ 2026-10-15 partial: 5 s post-revive grace (Health.Unnoticed, dropped on attacking, P4717 / P5832; length ours, L89); ✅ 2026-10-16 spawn heading (Creature.HomeYaw, taken on the first AI step / at a story walk's end; restored on reaching home and on respawn) and healers (Kind.Healer: Scalewind Shaman / Ebonguard Priestess / Stoneskin Riftmender spawners [I, by name]; ranged casters that heal the most hurt creature of their side < 75 % within 15 m by 15 % every 4 s with 12572 / 12571, and otherwise follow a target out of range and attack with radiance bolts; numbers ours, L120); untested in game.
      in the correct direction"; P4696: "Healers will now pursue enemy targets who move out of their range when not busy
      healing"; P4717: "You should now have a few seconds to recover after reviving, before Creatures attack you
      again"): the Return state restores position and health but not the spawn heading; no creature heals; Idle aggro
      takes any living player within 12 m and Health.Revive sets no grace. Must: restore the heading, ignore a revived
      player for a few seconds, healer behaviour when casters heal.
- [x] **6.22 Elites and very large creatures ignore crowd control** ✅ 2026-10-16 Creatures.IgnoresControl (Boss or Ogre kind): CanAct / CanMove / speed ignore the statuses, which still land (P5266, 11 May 2016, "Hotfix Notes - 11 May":
      "'Elite' creatures and very large creatures are now immune to stun, root, freeze, snare, and silence (burn) ...
      the attack's effects will still play"): Combat/Statuses.Apply applies every status to any entity; only the
      armour execute skips Elites (Creatures.cs:669). Must: on Elites / bosses (and large kinds) play the effect but
      don't apply the disabling statuses (Freeze, Electrocute, Crush, Condemn, Cripple, Burn's silence).
- [x] **6.23 Outpost respawn timers** ✅ 2026-10-16 Creatures.RespawnDelay: caverns with an Outpost 8-10 min, Elites 15 min (P5193 / P5195 / P5197, 4 May 2016, "[Feedback] Chaos Caverns + Outposts", dev:
      "every non-elite Outpost creature should have a 8-10 minute respawn. Elites have 15 minute respawn"):
      Creatures.Kill sets `RespawnAt = now + RespawnTime` (3 min) for every creature, outpost garrisons and Elites
      included. Must: 8-10 min (random) for outpost creatures, 15 min for Elites. (6.6 is about world creatures.)
- [x] **6.24 Elite loot and the hidden chest's spot** ✅ 2026-10-16 Elite loot (Creatures.Kill); the chest already respawns at a random ruin chest spot (Outposts.ChestProp) (P5628, 25 May 2016, "Update Notes - 25 May": "Lost Crystals, Lost
      Treasure Chests ..., and Outpost Bosses now drop significantly more loot"; P5014, wiki Game Updates 2016:
      "increased drop rate for Recipes"; P4806, 26 Apr 2016: the hidden chest "won't always be in the same place"):
      the Elite drops what any garrison creature drops (SpawnerKind: copper 1-3, Adventure Essence 0-1); the Lost Chest
      is always at one spot per outpost (Outposts.cs:240, the same prop after ChestRespawn). Must: an Elite loot table
      well above the minions' (recipes, essences, resources); the chest moves between several hidden spots when it
      comes back (open lead L12).
- [x] **6.25 Lost Crystal loot lockout** ✅ 2026-10-15 (Outposts LockedOut / LockOut, Unlocks 21, 24 h ours) (P4123, 21 Mar 2016: "you will only get rewards from the first one you find
      within a specific cave network"; P5180 / P5181, 4 May 2016, dev: "The timer is for each Lost Crystal, though, not
      globally" and "when you use the Lost Crystal while it's on Lockout, it should just show the teleport prompt message
      without giving you another reward"): Outposts.CompleteChannels gives loot on every channel; there is no timer.
      Must: a per-player, per-crystal lockout; while locked, no loot, only NextUndergroundOffer. Length unknown (open
      lead L10). TASKS 3.14 has the later, longer timer.
- [x] **6.26 Group trips underground** ✅ 2026-10-16 Caves.OfferGroupTrip / AcceptGroupTrip (client prompt GAQ.GroupMemberContent 574353 via Groups 0x24, accept 0x26 FUN_142de7a90), Outposts groupNext; untested (needs 2 players) (P5188, 4 May 2016, dev: "If someone goes to a new Chaos Cavern via the Travel
      Menu, you'll get a message asking if you want to go with them, but you should be able to decline it"; P5203: the
      group members who use the same crystal go to the same next Chaos Cavern): Caves.TravelRandom and
      Outposts.NextCave pick a cavern per player; groups play no part. Must: offer the trip to group members (accept /
      decline) and send a group using the same crystal to the same cavern.
- [x] **6.27 No Underground Adventure from Pioneer landscapes in 0.589** n/a on client-0.695 (0.695 allows it from every zone: 3.28) — (P5158, 4 May 2016: "selecting 'An Underground
      Adventure' from the Travel Menu on any non-Pioneer Landscape"; P4299, 21 Mar: "There are no Chaos Caverns ... on
      Pioneer Landscapes"): Travel.Handle RandomUnderground → Caves.TravelRandom works from any landscape. Must refuse
      it on Pioneer lands for 0.589 (0.695 allows it, TASKS 3.14). Low.
- [x] **6.28 Target Dummy** ✅ 2026-10-16 (agent; default-learned confirmed) (P5275 / P5433, 11 May 2016: "This NPC will stand in one location and intermittently swing
      its sword. Note that it does not do any actual damage, and will not be relevant to any enemy NPCs. The target
      dummy is default learned for all players"; P5445, 13 May: it "can be attacked and 'killed'"): prop 2890 is not a
      spawner to StoryTools.KindOf (its name lacks "Spawner:"), so it stays a static prop; only Achievements.cs:645
      knows it. Must: in Play Mode a killable, harmless, slowly swinging creature that other creatures ignore; check
      it is default-learned in the recipe data.
- [x] **6.29 Proximity Mines** ✅ 2026-10-16 (agent; Vine Trap too) (P5269, 11 May 2016: "Proximity Mines are treated as creatures on your build site, and
      they have the same faction and respawn settings as other non-pathing creatures like fiery fungi"; P4866, 26 Apr:
      "Proximity mines should once again work"): prop 1220 has no Link kind and no code (StoryTools, Creatures, Props
      searched). Must: a stationary exploding creature like the Fiery Fungus, with faction and respawn settings.
      (TASKS 2.15 is its later terrain damage.)
- [x] **6.30 Spawner settings: faction, name, duplicate** ✅ 2026-10-16 faction = row 17 Reaction (2.29); name: a renamed spawner's creature is shown with the spawner's name (Creatures.SpawnStory displayName → AddLightweightNpc Name; kind still from the prop); duplicate: the copy takes the original's name at once and its window settings when placed (PropSettings.CopySettings: Reaction, Respawn Time, Respawn After Killed, Spawn Automatically, Tactics... [P P4352 for name / faction / respawn, I for the other rows]; read-only rows and reward-chest state stay behind); untested in game — (P4222, 21 Mar 2016: "Creatures can be set to ally, neutral,
      or enemy faction"; P4883, 26 Apr: "Right-click both creatures and choose 'Open Settings' to change both
      creatures' Faction to Ally"; P4227, 21 Mar: "Right-click a creature to access more options such as the creature
      name and respawn rate"; P4352, 1 Apr: "When duplicating a creature, the name, faction and respawn settings should
      now copy"): PropSettings `settingsByKind["spawner"] = {18, 19, 31}` leaves out EditableProperties row 17
      `prop_npc_spawner_faction_select` (0-2) and Creatures.SpawnStory never reads a faction (enemy until a Link changes
      it); the creature is named after the prop definition (StoryTools.PlayModeChanged `Running.Name`), not the
      spawner's custom name; Props.Duplicate copies only the tint. Must: offer and apply row 17, use the custom name,
      copy name and settings on Duplicate.
- [x] **6.31 Creatures can't be tinted** ✅ 2026-10-16 SetTint skips spawners (P4354, 1 Apr 2016: "Creatures placed on your build sites can no longer be
      tinted"; P4466 dev reason): Props.SetTint tints any prop the player owns. Must refuse creature spawners (and props
      without tints). Low (the client may already hide it).
- [x] **6.32 Interaction prompt only where something happens** ✅ 2026-10-16 Creatures.Interactable / StoryTools.CreatureHasClickLink (P5304, 11 May 2016: "The prompt to interact ... now only
      appears on creatures who have active and available interaction Links on them"; P5305: "While on the Enemy
      Faction, creatures now realize they're too busy killing you to be interacted with"; P5482 / P5492, 25 May):
      Creatures.SendSpawn flags every story creature interactable and StoryTools.ClickCreature runs the click Links
      whatever the faction. Must: flag only creatures with a "clicked" Link that aren't Enemy (re-flag on faction
      change); ignore clicks on enemies.
- [x] **6.33 Story Node text display** ✅ 2026-10-15 routed by Text Location / Duration (bubble via the spawner prop, chat = creature Say, on screen) (P4876, 26 Apr 2016: chat bubble, chat channel or on screen; P5297, 11 May: "Text
      Display setting now defaults to Chat Bubble"; P5477, 25 May: "Chat & Chat Bubble"; P5478: "Text Display Duration";
      P5495: own area, shown "for the duration specified on the story node"): StoryTools.Perform always sends a
      creature's text as a Say line (Chat.NpcSay) and ignores the node's Text Display and Duration;
      PropSettings.DropDowns[TextDisplayType] lacks 0.589's text 577170 "In Chat Bubble and Chat". Must: route the text
      by the node's setting (bubble / chat / both / on screen) for its duration and add the "both" option. (TASKS 2.5 is
      the later formatting.)
- [x] **6.34 Story Node fallback speakers** ✅ 2026-10-16 StoryTools.ActivateNode / SayNodeText (P5286-P5288, 11 May 2016: with no outgoing text Link, "the Story Node checks
      whatever triggered the Link initially. If it's a creature, then that creature will say the Text", otherwise "the
      Story Node itself will say the Text" (no bubble); P5284 / P5116 same for animations): text reaches only creatures
      linked with "...Have this creature say the text." (StoryTools.Perform). Must add both fallbacks.
- [x] **6.35 Story Node text limits** ✅ 2026-10-16 superseded by 0.695 rich text (2.21: 512 visible, tag whitelist) — (P4875, 26 Apr 2016: "store up to 512 characters of text"; P5299, 11 May: "text
      can no longer display HTML"): Props.SetText caps every prop's text at 1024 characters and passes markup through.
      Must: 512 for Story Nodes; strip or escape HTML in prop text (first check whether the 0.589 client escapes it).
- [x] **6.36 Linkable sconces, emissive orbs / cubes, control panels** ✅ 2026-10-16 named kinds; sweep of all 0.695 prop names for light / glow / switch words found no other missed toggles (crystals, curtains, forges, windows, signs, plants only) — (P4856, 26 Apr 2016: "All interactive props
      (including emitters and lights) should now function properly with linking and triggering"; P5371, 11 May:
      "Buccaneer wall sconce can now turn on and off"; P5222, 4 May dev: "The Round Control Panel should be Linkable";
      P5227: the emissive cube's "...turn this glow off." Action): StoryTools.namedKinds matches words in the prop name,
      so the Wall / Hanging Sconces (691, 692, 2096, 2204, 2581), the Emissive Orbs and Cubes (2127-2142) and the Control
      Panels (1172, 1176, 1177) get no Link kind. Must: give them their kinds (light, glow, switch) from the client's link
      texts; then sweep every prop name the word list misses.
### Build sites, props and designs

- [x] **6.37 ✅ 2026-10-16 (agent; untested in game) One Starting Location per site** (P4818, 26 Apr 2016: "Attempting to place a new Starting Location prop if
      you already have one placed on your build site will now automatically pick up the old one and place it on your
      cursor"): Props.BeginPlacement places a second one; Claims.ArrivalOf takes whichever it finds first. Must: move
      the existing one instead.
- [x] **6.38 ✅ 2026-10-16 (agent; untested in game) Recycle returns the resources** (P5344, 11 May 2016: "When a prop is recycled, you will see what resources
      you got back in the loot window"; trace/systems/building.md "Recycle returns them all"): Props.PickUp with Delete
      (PropBase 0x07) refunds nothing; PickUp (0x05) gives the prop item back (Props.Remove → GiveItem), never the
      recipe materials. Must: Recycle returns the recipe components (Props.RecipeCost) with loot notices; "Send to
      Inventory" stays for props placed from items. First confirm which sub the 0.589 menu's Recycle and Send to
      Inventory send.
- [x] **6.39 ✅ 2026-10-16 (agent; untested in game) Site delete and foreclosure return props as materials and all resources** (P4870, 26 Apr 2016: "Deleting
      your site (or automatic foreclosure) should now return the props as materials, not as items in your overflow";
      P4133, 21 Mar: on expiry "A Design of the Build Site and all resources will be sent to the Luminary via in-game
      mail"; P4365, 1 Apr: expiry designs "should save their screenshots"): Claims.Delete returns props as items
      (Props.ReturnInBox) and voxel materials (SiteDesigns.Refund); a lapsed site (SiteContents.Pack) packs props as
      items behind the Build Site Contents item and refunds no voxel materials ("the server doesn't know which of them
      the player paid for"); SiteDesigns.Save copies no screenshots. Must: props back as materials in both cases, a
      lapsed site's resources by mail, the site's screenshots on its design.
- [x] **6.40 ✅ 2026-10-16 (agent; untested in game) Designs build known props from materials** (P4462, 1 Apr 2016, dev: "You cannot 'create' the prop (via
      Build Mode or Template placing) without the recipe" — with it you can; P4569, 13 Apr: designs also use inventory
      props without the recipe): Props.PlaceDesignProp places a design's prop only from a bag item ("a design gives the
      layout, not the props"). Must: a bag item first, else build it from materials when the recipe is known and
      affordable, else list it as skipped (ZoneMissingPropList) or unknown (ZoneUnknownPropList).
- [x] **6.41 ✅ 2026-10-16 (agent; untested in game) Chests aren't reclaimable** (P4853, 26 Apr 2016: "Chests are intentionally not reclaimable as they may hold
      items inside"): Props.PickUp lets a chest with items be picked up (its contents go with the item, Chests.Move).
      Decide: refuse non-empty chests like live, or keep this as a deliberate convenience. Low.
- [x] **6.42 ✅ 2026-10-16 friends / guild (agent) + sites → designs, Special Access (Claims.CharacterDeleted); guild news n/a (none kept) Character deletion clean-up** (P4958, 26 Apr 2016: "Deleting a character now automatically creates a
      Design for any build sites that character had in the world"; P4624, 13 Apr: "Deleted characters will now be
      correctly removed from friend and guild lists"; P5412, 11 May: "Names of deleted characters no longer remain in
      the Special Access list"; P5311 guild / officer channels; P5312: "Guild News should now persist the name of
      players who are deleted"): AuthServer LoginHandler.HandleCharacterDeleteRequest only sets DeleteTime and
      OriginalName; the world server leaves the sites placed and the friend rows, guild membership and Special Access
      entries in place (Friends.cs checks DeleteTime only when adding). Must: on delete, save each site as a design and
      remove it, drop the character from friends, guilds (names kept in guild news) and Special Access.
      2026-10-16 PARTLY BUILT, not verified in client: friends / guild clean-up (Game/CharacterDeletion.cs sweep →
      ISocialService.CharacterDeleted: local Friends / Guilds, remote FriendService / GuildService via
      CharacterDirectoryUpdate Deleted). Sites → design + Special Access wait for a Claims entry point (Claims.cs is
      another work stream); no guild news exists yet (P5312).
- [x] **6.43 Gallery: published sites, tags, comments, lists, Play Mode filter, likes** ✅ 2026-10-16 (agent; untested in game; trace/0695/gallery-695.md): only listed + fully tagged sites in the gallery, publish needs the three TagDefinitions drop-downs, unpublish withdraws contest entries, "PlayMode" tag word → live Play Mode filter, likes / visits / contest entries only for listed sites (576478), comments (gallery_comment table, migration 20261027090000), most-liked + rising-stars side lists, 0.695 InGamePurchase sub shift (Commerce.FromWire / ToWire) — (P4590 / P4591, 13 Apr 2016:
      search by the three tag drop-downs and the site name, untagged sites hidden; P4783, 26 Apr: publish / unpublish
      button; P4139 / P4686: comments on gallery entries; P4670: leaderboards (most-liked) and "rising stars" (recently
      listed); P5624, 25 May: "filter Build Sites by Play Mode status"; P5630, 25 May: "It is only possible to Like a
      build that is listed in the Gallery"; client text 576478 "You must publish your Build Site to the gallery before
      submitting to a competition"): Store.Gallery lists every site ("TODO: only published sites", though
      Commerce.IsOnGallery exists), the page (Resources/Web/showcase.js) searches free text over one vote-ordered list,
      no comments; Ratings.HandleAddVote takes votes for any site; Contests entries don't check publishing. Must follow
      each.
- [x] **6.44 ✅ 2026-10-16 (agent; untested in game) Ultimate Power may switch Play / Edit Mode** (P5343, 11 May 2016: "Builders with full ('ultimate power!')
      permissions on another person's build site can now change Play/Edit mode"): Claims.SetAdventureStatus accepts
      only the owner (OwnedClaim). Must also accept players with all permissions (0x11F).
- [x] **6.45 ✅ 2026-10-16 (agent; untested in game) Player Studio designs only on your own site** (P4632, 13 Apr 2016: "error messaging for attempts to place a
      Player Studio Design on a build site you do not own"; P4954, 26 Apr): BuildingClipboard's design paste checks only
      build permission (TerrainEdits.MayApply). Must refuse a bought design on someone else's site, with the client's
      message.
- [x] **6.46 ✅ 2026-10-16 (agent; untested in game) Build only inside build sites** (P4141, 21 Mar 2016, Build Mode: "you can only actually make use of it on
      a build site where you have access to build"; P5651, 25 May: "It is still possible via a bug to paste rotated block
      selections outside your claim boundaries; needless to say this is unintended"): Claims.BuildCheckIn /
      TerrainEdits.MayApply allow every point outside all sites ("Outside claims: yes"), so build edits, heals and pastes
      (Building ApplyDiff, BuildingClipboard) are accepted in the open world. Must refuse build-layer edits and pastes
      outside a site where the player may build (mining stays allowed).
- [x] **6.47 ✅ 2026-10-16 (agent; untested in game) Site height limits** (P4135, 21 Mar 2016: sites "can be sunk somewhat into the terrain and can be floated
      somewhat into the air"; P4298: "You cannot place your Build Site far from the surface in the underground"):
      Claims.CanPlace / PlaceConfirm accept any centre height. Must check the centre against the ground (take the
      limits from the client's placement code).
- [x] **6.48 ✅ 2026-10-16 (agent; Pioneer cavern step has no cave data for 417-421, L99) New-player site fallbacks** (P4116, 21 Mar 2016: Pioneer surface first, "Second priority places build sites
      in an underground cavern on a Pioneer Landscape", then a classic Landscape; P4361, 1 Apr): Pioneer.Place tries
      Pioneer surfaces only, else the character gets no site. Must add the Pioneer-cavern and classic-landscape steps.
      2026-10-16 BUILT, not verified in client: Pioneer.Place surface → cavern → classic landscape; sites keep off
      the Spire. The cavern step finds nothing yet: no Pioneer cave placements in the client data (OPEN-LEADS L99).

### Travel and world

- [x] **6.49 ✅ 2026-10-16 (agent; one Spire per landscape, height L100) A Spire on every landscape** (P5647, 25 May 2016: "Central spires are now raised slightly higher out of the
      ground"; P4946, 26 Apr: "the travel menu now allows travel to the nearest portal spire at any time"; P4965:
      "When traveling to a pioneer landscape you should now correctly arrive at the spire"): Spire.cs builds one Spire,
      on the home continent; Spire.TravelTo re-zones there from any other landscape; Landscapes.TravelTo lands other
      landscapes at their centre with no Spire. Must: stamp a Spire (base_portal_spire.otd, Replicator, Repair Station,
      Mailbox, Parlor, crystal) at each landscape's centre, Pioneer lands included, and send "Spire" travel to the
      current landscape's.
      2026-10-16 BUILT, not verified in client: Spire.cs one Spire per landscape (stamped on first use, marker
      spire_base_<zone>.txt), Landscapes.TravelTo arrives at it, Travel → Spire goes to the current landscape's,
      Graveyards / PlayerManager use it on every landscape. Open: the "raised higher" offset (L100); Local Travel /
      warp hub on other landscapes (WarpNetwork is home-only).
- [x] **6.50 No Local Travel at launch** n/a on client-0.695 (0.589-only rule; 0.695 has Local Travel) — (P4246, 21 Mar 2016, known issue [Fixed]: "'Local' is still showing as an
      option to travel at the portal spire, but should be removed."): WarpNetwork.cs serves the Spire's Local Travel map
      and 81 underground portal crystals (InteractionHandler: Spire crystal → WarpNetwork.ActivateTerminal). Must switch
      Local Travel and the leyline warps off for 0.589 (FEATURES: "The Leyline Network was removed at launch").
- [x] **6.51 No PvP at launch** n/a on client-0.695 (0.589-only; 0.695 has the reworked game tables, 2.12) — (P4230, 21 Mar 2016: "We have removed PVP game tables. PVP is no longer available.";
      P4302: "There is no PvP."): InstancedGames.cs runs Deathmatch (1346), Team Deathmatch (1347) and Team Zone Control
      (1226) tables in which players hurt each other. Must: PvP tables unavailable in 0.589 (Adventure 2313 stays); the
      29 Sep 2016 rework (2.12) brings games back for 0.695. Decided 2026-10-15 (user): per client build — off for
      0.589, the reworked games for 0.695.
- [x] **6.52 Vendor Booth is decorative** ✅ 2026-10-17 (user: "do the vendor how the live server did"; 89d587f: decorative by default, VENDOR_SHOPS=true keeps the shop) — (P4378, 1 Apr 2016: "Vendor Booth had an old, non-functioning UI attached which
      has been removed ... it will be available in Landmark simply as a decorative prop"): Vendors.cs makes "Build
      Site_Vendor_Object" (1491) a working shop (ClaimVendorBase, coins, listings). Decide: off for live fidelity, or
      keep as a documented extra.

### Gear, goals

- [x] **6.53 ✅ 2026-10-16 (agent; 1 point per swing ours, L11) Gathering tools wear** (P4524, 13 Apr 2016, "Update Notes - April 13": "For equipped harvesting tools,
      condition is lost when the tool is used to harvest"): Durability.cs "Gathering tools have no set and never wear";
      DurabilitySet rows 1 (9751) and 5 (14751) have no loss per damage or death (tool-like). Must: find which items use
      sets 1 / 5 and the loss per harvest (open lead L11), wear tools on use, repair them like gear.
      2026-10-16 BUILT, not verified in client: Durability.ToolUsed (1 point per harvesting swing, INFERRED, L11) for
      the 12 tools with sets 1 / 5; broken axe gives half wood (Durability.ToolStrength; mining yield not yet:
      Abilities.Dig).
- [x] **6.54 ✅ 2026-10-16 (agent; unique-equip flag, L101) One of each accessory** (P4840, 26 Apr 2016: "Multiple copies of the Ring of Bounty can no longer be
      equipped at the same time"): Inventory.Fits / ToggleEquip only check the slot. Must refuse an item whose
      definition is already in the other accessory slot.
      2026-10-16 BUILT, not verified in client: ItemDefinition.UniqueEquip (flag byte 2 bit 7, the 21 accessories),
      Inventory.DuplicatesUnique refuses a second copy with TransferItemResponse 0x22 (client dialog), tooltip
      "Unique Equipped" bit; L101.
- [x] **6.55 ✅ 2026-10-16 (agent; dailies only) Daily goals count for the group** (P4559, 13 Apr 2016: "Daily Goals should now update harvesting goals for
      everyone who receives harvests, and combat goals for everyone fighting the creature"): Gathering.GiveHarvest
      shares the items, but Achievements.Harvested / DailyGoals.Harvested run for the gatherer only, and Creatures.Kill
      counts the kill for the killer only. Must count harvests for every member who received them and kills for
      everyone who damaged the creature.
      2026-10-16 BUILT, not verified in client: Achievements.Harvested counts the dailies for the party members
      GiveHarvest shared with; Creatures.Kill counts DailyGoals.Killed for everyone in the creature's damage table.

### Play Mode

- [x] **6.56 Play Mode survives restarts** (built 33f894b: claim.playMode, restored on the first tick) (P4248, 21 Mar 2016, known issue [Fixed]: "On a server reset, pathing
      creatures on build sites will not start pathing again until play mode is turned off and on again"; P4351, 1 Apr:
      "Server reset should no longer make creatures stop pathing"): Claims.cs keeps Play Mode in memory only ("Not
      persisted: sites start in Edit"), so a restart stops every site's story. Must: save the mode per site and restart
      Play Mode (StoryTools.PlayModeChanged) at load.

### Additions and answers for existing items (merge into 6.1-6.10, 5.2)

- 6.5: P4349 (1 Apr, "Exiting play mode no longer has a chance to drop loot") is followed (StoryTools.Stop →
  Creatures.DespawnStory, no loot); P4801 (26 Apr, "Fiery Fungi should no longer update any kill goals") is followed
  (Creatures.ThinkFungus calls no kill counters). Still open: campfire drops (P4594), fungus tree loot (P4533), ally
  kills (P4719, waits for 6.20).
- 6.6: answered for build sites: EditableProperties row 18 `player_npc_spawn_delay_flat` is 0-300 s (default 30),
  which the spawner window offers (P4718, 26 Apr, "Creature respawn time on your Site can now be set up to 5
  minutes"). Outposts: see 6.23.
- 6.10: add P5437 / P5328 (11 May): "The combat achievement H2-Ouch now grants the Reinforced Coat of Crashing Waves
  recipe instead of the Frost Lord's Guard recipe". P4789 (free tutorial designs can't be deleted) is followed
  (Templates.Delete answers "cannot delete" for Starter Castle designs).
- 5.2: P4235 (21 Mar, "The Foundation Museum islands are no longer accessible.") — at launch the Museum was closed;
  Landscapes.cs already leaves the Museum zones out, so for 0.589 the server matches the notes.

Resolved by the notes: vault 60 → 120 (P3253-P3254, 21 Mar 2016) — the server is right and the wiki's "80" is wrong
(TASKS 5.3 closed); design storage 6M → 48M (P3258) ✅; 50 chests per site (P3259) ✅; revive costs lumens directly
(P3581) ✅.

## 7. Live server architecture

- [x] **6.57 Tutorial goals with no live record** (goals audit 2026-10-16, L42): ✅ 2026-10-16, untested in game (agent; Game/Achievements.Tutorial.cs, goals-audit-695.md "Tutorial goals built from the wiki", L112): Getting Crafty 9001 → Building Block Basics 9002 → Modular Thinking 9003 after 685 in the Getting Started chain (AchievementAdd when the one before completes), records from 471 with the client's texts, wiki counts and 30 Lumens / 5 score; steps one at a time; hooks: Replicator placed, crafts (Cut Plain Wood, Farseer Pick, Smelted Copper ×3 crafts, Cut Agate ×2), UI events 12 (Blocks tab) / 17-19 (Fine Tuning paste options), tool switches 10 / 6 / 2, blocks placed / removed ×200, paint edit (kind 6), copy, clipboard paste, design pastes (Long Room, Entry, Window with paste mode 2 / 3 / Remove voxels), undo, redo. Rewards [I/N]: Building Block Basics the 8 Modular Castle designs (now not defaults, U-DC3), Modular Thinking the Architecture Tools (4358 4359 4391 4392 1742 4393). Not done: the server's paste ignores paste modes / Remove voxels; off-grid pastes (L77). Original item: Getting Crafty, Building Block Basics, Modular Thinking are on the wiki (tutorial chain, 26 Apr / 7 Jun 2016 notes) but not in the Feb 2017 capture; build them from the wiki pages (objectives the server can already detect: placing the Replicator, crafts, copy / paste / undo / tool use) with ids of ours, or confirm they were replaced. Found 2026-10-16: the client has all their texts (0.589 and 0.695): Getting Crafty 576237 (desc 576238; objectives 576246 Replicator, 576247 Cut Plain Wood, 576256 Farseer Pick, 576249 Smelted Copper, 576250 Cut Agate), Building Block Basics 576265 (576266; 576275-576284: Blocks tab, Add / Delete / Paint / Selection tools, place / delete blocks, copy, paste), Modular Thinking 576271 (576272; 576285-576292: Long Room design, Fine Tuning pastes, undo / redo).
- [x] **6.61 Creature pathing (navmesh)** (2026-10-18: built; only the in-game walk-round check is left, in TESTING-NEEDED) (2026-10-17: built and committed, 3d5094a; story creatures on build sites path too since 73dc97c (L133 closed, SitePathing on, harness round 60); left only the TESTING-NEEDED walk-round check, per the note below) (admin page 32 "Pathing Information - Zone" with NavMeshCells etc. shows live
      had navigation meshes for NPCs; admin-pages.md §page 32): our creatures step straight at their target and leash home,
      so they walk into walls and through ruins. Needs a per-landscape walkable mesh (or grid) over the voxel terrain +
      cave pieces + stamped ruins, path queries for chase / return / patrol, and the page-32 numbers. Trace live's
      parameters first (page 32 fields, any navmesh files in the client: none found by name yet).
      ✅ 2026-10-17 built, untested in game — tick after the TESTING-NEEDED check (agent; uncommitted; trace/architecture/pathing.md): the client holds no mesh
      format or parameters (0x4A2-0x4AA names, page 32, "Pathing" channel; L132), so ours [I] is a walkable column grid
      (1.2 m columns, floors with ≥ 2 m headroom, climb ≤ 1.25 m) from Sight's own solidity — cave pieces
      (CavePieces.AirSpans, new) with the stamped ruin grids over them (Sight.ColumnAir, new) — and, on the surface, the
      baked heights; 19.2 m tiles built lazily on one background thread (~0.1 ms, ~2 KB, ≤ 3,072 cached, dropped under a
      newly stamped outpost); straight walk when the line is walkable, else A* on the background thread (≤ 6,000 nodes,
      partial path when unreachable). Creatures.Walk: chase / Abomination approach / return home / story walks; wander
      only to points in a clear line; cavern heights from the grid (outpost floors now count; client height offset kept);
      chasers stand facing an unreachable target; fail open (straight, as before) wherever the data is missing or
      contradicts the world. Story creatures (build sites) keep straight steps (edited ground, L133). Setting
      CreaturePathing / CREATURE_PATHING default **on**; dev `pathing [on|off]`; /admin/monitor "Pathing Information -
      Zone" (page-32 meanings ours). Offline harness on 406's real cave data: tiles, lines, a synthetic ruin doorway path
      and the step probe agree. TESTING-NEEDED "Creature pathing".
  Harness check 2026-10-17 (outpost 0, test stack): tiles build, 13 paths (8 reached / 5 partial), Blocked / Clear lines, no errors; the visual walk-round check and one "Mismatch, no path" chaser are in TESTING-NEEDED.
- [x] **6.58 Iron From Ice** ✅ 2026-10-17 (d2327b4): blue — "Iron From Ice" is Telltale's Game of Thrones ep. 1 and
  ironwood burns blue there; a Link to/from Magic Fire (Blue) / (Small Blue) Mote Emitter counts 964 [I, strong; L45].
  Was: (goal 557, objective 964 "Wood of iron burning"): which Magic Fire colour on which wood counts is a riddle (L45); nothing counts it today. Narrowed 2026-10-16: a Link goal (it sits in the Link block 574756-574760; description 574800 "Link anything to this most special of magic fire colors"), so the answer is one of the seven Magic Fire Mote Emitters (Blue, Green, Pink, Purple, Red, Yellow, Orange; texts 567308-567323); "wood of iron burning" / "Iron From Ice" don't settle which — not guessed.
- [x] **6.59 Account entitlements** ✅ built 2026-10-17 (e-store merged, 141bc77; harness on the isolated stack: packages granted once, unlock kind 26 rows 1-3; the letters and recipes by eye untested in game; per character, not per account; founder items with no client record: L149) (item-sources audit 2026-10-16, item-sources-audit-695.md §3.2 / §3.4): the wiki's only route for the Founder's Pack items (Trailblazer / Explorer / Settler Flag, Void Vault, Mastercraft Bracer, Ring of Bounty, Founder's Pickaxe, Mega Pocket, Noble's / Courtier's Regalia, Tech Commander's / Sergeant's Gear, title Founder; wiki "Founder's Packs"), the Year Zero Trophy and Year Zero outfits (mailed to accounts active on the alpha's one-year anniversary, 31 Jan, wiki "Year Zero Trophy") and the 2014 giveaways (Iron Portcullis, P1138). Today the flags, Void Vault and trophy are innate palette props for everyone (the client data gives them no recipe item) and the rest have no client item. Needs an account-level grant (e.g. a per-account entitlement list in the auth DB / .env, delivered by mail or on first login) and the user's decision on who gets what on this server.
  **User decision 2026-10-17:** give the founder / reward items to everyone for now (also as tester items) behind a server setting that can turn it off (in progress, branch e-store).
- [x] **6.60 Showcase catalogue vs the live Marketplace** ✅ built 2026-10-17 (e-store merged, 141bc77; harness on the isolated stack: prices, refusal, purchase; the page and HUD count by eye untested in game; pet / chest DBC prices unknown, L147) (item-sources audit 2026-10-16): Store.Catalogue hands out, free, every outfit (incl. the 290 Lumen Station outfits, 520 Lumens each on live) and every categorised prop (incl. the 448 discovered props / creatures the wiki says come from finds or the Lumen Station) — a route the wiki names for none of them (only beta Marketplace crates for four Rancher's Outfits, the pets, the Fantasy Parlor Pack). Decide with the user: keep the free store as a dev / creative convenience (switchable), or limit it to Marketplace products (pets, Fantasy Parlor Pack, Build Site Expansion, designs, Lumens) with a price.
  **User decision 2026-10-17:** match live (Lumen Station outfits for Lumens, props from their live sources) with a server toggle for a free store; anything live sold for Daybreak Cash gets a Lumen price at the Lumen value of the time; nothing is ever paid with real money (in progress, branch e-store).
- [x] **Procedural placement** (architecture/admin-monitor.md page 28): ✅ 2026-10-16, untested in game (agent; Default OFF (coordinator 2026-10-16): the sets name only alpha biomes 18-31, so their use on launch landscapes is an inference; switch on with ProceduralPlacement=true to compare.
      trace/0695/procedural-placement-695.md, Game/ProceduralPlacement.cs + Gathering.cs): the sets are server rules (no
      client loader in 0.695 [P]); PROP_ID = PropDefs props (Biome_* trees, rocks, bushes, flowers, sickle plants),
      MATERIAL_ID = VoxelMaterials (= the baked grid's surface code by ARRAY_INDEX [P]); the sets name only alpha-era biomes
      18-31, bridged to the launch biomes 48-51 by BIOME_TYPE_ID [I]. Trees on Desert / Tropical / Old Growth / Tundra now
      follow the sets (spacing, slope, altitude, soil, masks, encroachment, SPAWN_CHANCE, scale / heading, respawn
      12-18 min), deterministic per landscape; Volcanic / Deciduous / shore keep the old lists. Settings
      ProceduralPlacement (on; density 0.5-2.3x the old lists), ProceduralPlacementDecor (off), ProceduralPlacementMasks
      (on). Open: L1, L2, L82, L113 (launch-era sets, column units, respawn elsewhere).
      Original item: the live server spawned procedural entities
      from ProceduralPlacementEntityLists / EntityListEntities (server tables, not shipped); the client ships
      ProceduralPlacementSets, SetEntities, SetMaterials, SetsSetEntities (~1,100 rows) which the emulator does not
      use. Trace how the client uses those four tables and what the server must spawn from them (trees, rocks,
      harvestables?) before changing Gathering's placement.
      Contents (2026-10-15): ProceduralPlacementSets (108 sets: placement data group, BIOME_ID, site angle / altitude
      range, MAX_SPAWNS, MIN/MAX_RESPAWN_MINUTES e.g. 12-18, offset / heading / pitch / roll / scale ranges,
      AVERAGE_DISTANCE / MINIMUM_DISTANCE, ORIENT_TO_SURFACE, MASK bitmap + MASK_SCALE, ENCROACHMENT_PRIORITY);
      SetEntities (PROP_ID, SPAWN_CHANCE, MAX_SPAWNS, offsets, scale); SetMaterials (SET_ID, MATERIAL_ID,
      SOIL_SUITABILITY); SetsSetEntities (links). trace/datatables.md says the client never loads them: server rules
      shipped with the client. **Masks found (2026-10-15, agent-L1-L2.md):** loose 24-bit BMPs in
      CommonData\ProceduralPlacementTool\Masks\ (4096², 1024², 32²; same in both builds; 33 files, 13 of the 14 the
      table names) — inputs of Daybreak's offline ProceduralPlacementTool (CommonData\VoxeleinData      HowToGenerateVeins.txt), never read by the client: usable by the server. Still open: TilingTestZZ.bmp (set 170)
      and the rows of the two server entity-list tables (whose column schemas are compiled into the client) — OPEN-LEADS.
- [x] **Admin player flag** (5aeed5d + 0eb4af7; untested in game): SendSelfToClient `IsAdminMaybe` (+0x7aea8) is the client's admin-player flag; set it for
      server admins and answer the admin Command subs they then send (architecture/admin-monitor.md).
      ✅ 2026-10-16 flag set for WorldServer.json "Admins" (WorldSession SendSelf). ✅ 2026-10-16 (agent) the admin
      Command subs decoded from the 0.695 writers and handled in Game/AdminCommands.cs (trace/0695/admin-commands-695.md,
      docs/admin.md): built — /goto player 0x42E, /target 0x4B5 (reply same sub), /hideme 0x458, /runspeed 0x4D8,
      /servertime + /zonetime 0x429 (hour / pause / sync / warp on WorldClock), /clienttime 0x42A (echoed), /prop 0x4EA
      (free prop via Props.PlaceAdmin), /friend forceadd / remove 0x4CF / 0x4D0; refused — delete all props 0x4ED;
      decoded + logged — /goto waypoint 0x430 (L104), /vehicle 0x49C, /setmodel 0x4F0, /ammo 0x499, /weaponset 0x4EE,
      /placedeffect 0x4EF, free-fly hide 0x4F1, /profile 0x3EC (L107). **Untested in game** (TESTING-NEEDED.md). Open:
      L104-L107.

- [x] Extend trace/live-server-architecture.md from the 0.695 client and the patch notes (world splits, zone crashes,
      "servers apply the actual data", overnight bounces, cross-world game finder, operations notes).
      ✅ 2026-10-16 trace/live-server-architecture.md "What the 0.695 client and the 2016 patch notes add" (worlds and
      the Events world, world travel, new: the zone base registry's server-only ids — world server, player / NPC
      transfers between servers, ZoneToZonePacket —, cross-world services, zone crash / reconnect as the client sees
      it, opcode 9, dynamic commands, admin flag, operations); stale patch-note ids in the posts section corrected;
      internal-messages.md §1 row added; new leads L114 (world change path / gateway scope), L115 (world list history).
- [x] Design the recreation (gateway + services per channel) and decide what to build.
      ✅ 2026-10-16 trace/architecture/recreation-design.md (per channel and per service: live vs today, verified /
      untested / to build; §5 recommended order **for the user to confirm**: verify the split stack and make it the dev
      layout → gateway routing table → ratings + `commerce` → groups across worlds → cross-world travel after L114 →
      admin page → `envserver` → zone per landscape). Items B.5, B.6, B.9, B.10 in §8.
- [x] **Cross-world services** (2026-10-17: the last part built — Join Now on another world's match moves the player there (1c039b1, merge e-travel; FINDER_RETURN_HOME cadb157; L31 closed); Player Studio shared since 33464ad; the cross-world join needs a two-client test, TESTING-NEEDED) (2026-10-16: chat / friends / guilds / mail / groups (B.1, B.9), Game Finder counts and Player Studio (shared commerce, 33464ad) all span worlds now; left: moving a matched player to another world's game, L31) (wiki: Chat "General: worldwide chat that includes all Servers and Landscapes";
      Friends, Guild, Mail; notes 14 Oct 2016 "Game finder should now work between worlds"): General chat, friends'
      online state, guilds, mail, Player Studio and the Game Finder shared live between world servers (today each
      world caches its own; docs/docker.md "How the world split works"). Chat / friends / guilds / mail: B.1 below.
      Game Finder ✅ 2026-10-16 (agent; **untested**): counts across worlds in the social service
      (MatchmakingService; MatchmakingRequest / Games / Update 0x10D / 0x10100-0x10102; social-service.md §9,
      world-channel-service.md §6.8); joining a match on this world as before; a match on another world is reported,
      not joined. Remains: Player Studio (the `commerce` container, world-channel-service.md §8 step 8) and moving a
      matched player between worlds (L31).

From trace/0695/architecture-leads.md (agent #8; plan changes for live-server-architecture.md listed in its "Changes to the recreation plan"). Coordinator check: no caller of the page setter in either decompile (0.589 FUN_142cbd160 / 0.695 FUN_142d56520).

- [x] **Gateway opcode 9** (architecture-leads.md L5): ✅ 2026-10-16 built (01d1a37) and verified on the 0.695 client (DynamicCommands.log: 'Command from server: gatewayprofiler', 'Command Executed: gatewayprofiler, dump'; gateway log 'Gateway command /gatewayprofiler dump'): command list source 3 after login, /gatewayprofiler on | off | dump per connection, system lines through the tunnel — the gateway service accepts external opcode 9 (BasePacket 9 +
      ClientCommandPacket body), runs its own commands, sends a CommandListPacket with source 3 after login, and answers
      only through tunnelled packets. Emulator gateway knows opcodes 1-8 only.
- [x] **AddWorldCommand / AddZoneCommand / ExecuteCommand** (L5): ✅ 2026-10-16 built (9e59ec8, Game/DynamicCommands.cs: 0x45 / 0x46 {string}, 0x47 {u32 name hash, string args}) and verified on the 0.695 client: '/duel Nobody' became '__sendworldcommand duel Nobody' (CommandQueue.log) and the server resolved the hash ('ran world command duel Nobody'), so the replicated hash is right — accept 0x1E/0x47 on both World and Zone channels; use
      0x45 / 0x46 where a single command is added at runtime.
- [x] **CharacterLoginRequest tail** (L6): ✅ 2026-10-16 LocaleId + GatewayId named; one gateway per world, a non-zero id is logged — name the emulator's `Unknown2` as u32 LocaleId + u32 GatewayId; when several
      gateways exist, honour a non-zero GatewayId.
- [x] **Internal message inventory** (L8): ✅ 2026-10-16 trace/architecture/internal-messages.md (live's named ids + ours ≥ 0x10000, rules) — write trace/architecture/internal-messages.md from the L8 table and use those
      family / sub ids between the Docker services (claims, mail, terrain / Environment Server, world admin, stats).
- [x] **Underground teleports like CaveManager** ✅ 2026-10-16 Caves.TravelRandom skips caverns within 150 m of the player (ours) — (Caves): "random cave" never returns the cave the player is in; keep a
      per-continent list of cave beginning locations in the terrain service.
- [x] **Admin monitor scope** (L3 / L4; both replies built 2026-10-16, the Frida page-hook check pending): only 0x10A and 0x10D sub 2 have client readers; build them last, test with a
      Frida page hook. Decided 2026-10-15 (user): the panel goal becomes a server-side / web admin page.
      ✅ 2026-10-16 both replies built (untested in game; client test = the Frida page hook, admin-pages.md §8): 0x10D sub 2
      pushed to admins on a site game with AdminMonitorPush (default off), 0x109 → 0x10A for admins (B.7 below).

## 8. Phase B — architecture build (specs in trace/architecture/; order per BUILD-PATH)

- [x] **B.0 Transport library** — LandmarkEmulator.Internal (3573fb1): framing, 7 / 8 / 0x16 + ours, listener / dialer.
- [x] **B.0 Operations** — scheduled restarts (89a7ddc), watchdog + healthcheck (8e8074c), Play Mode kept across
      restarts (33f894b, 6.56).
- [x] **B.1 Social** (social-service.md §6; all steps built and client-tested 2026-10-16; clicks left in TESTING-NEEDED): 1 interface split ✅ merged 8e2d7d9 (Game/Social; follow-ups: Trades.cs ignore
      check and Commerce.cs system mail moved to SocialServices ✅; runtime check of chat / friends / guild / mail
      in the client still due); 2-4 ✅ merged 8a66bae (LandmarkEmulator.SocialServer, Database.Social / lmemu_social,
      RemoteSocialService behind SOCIAL_ADDRESS, off by default; self-test 58/58); 5 presence / friends / ignore ✅ →
      guilds ✅ (76b3c94; 0.695 check: GuildBase / Notifications / ShowSystemMessage "same"; isolated test: guild created
      through the service, the client's own /guildmotd round trip saved in lmemu_social and broadcast back)
      → chat routing ✅ (f02b095; ChatBase / QuickChat "same" in 0.695; client-sent /tell (not found), /join TestRoom,
      /guild line routed by the service and delivered back) → mail + system mail ✅ (MailBase "renumbered only" in
      0.695 — ClientWire695 shifts subs ≥ 4 per session, bodies same; mailboxes in lmemu_social social_mail /
      social_mail_attachment, import 15 → 15 mails / 6 → 6 slots; zone keeps the bag side: escrow on compose,
      hand-over on take, the reading window's item records; isolated test on 0.695: inbox opens from the service
      (client's Mail FTE fired, rows shown), read (window + is_read), coins taken (HUD 47 → 67), compose with coins /
      an item (escrow taken, stored), refused compose gives the escrow back, self mail → NewMail, delete, COD blocks
      then pays (52, payment mailed to the sender), items handed over, service stopped → result 3 with nothing taken,
      restart → reconnect. Open: L84 — the 0.695 reading window shows no attachment rows); 6 migration + down behaviour; isolated test 2026-10-15 (one world,
      0.695 client; re/test/socialtest/up.sh / down.sh): import 5 characters / 1 friend, remote mode, presence on
      login, FriendBase / IgnoreBase delivered from the service, social restart with the player online → reconnect +
      snapshot "1 online"; cross-world cases ✅ 2026-10-15 with a scripted second world (re/test/socialtest/fakeworld.sh,
      FakeWorld/: world 92 with TestPlayer; TestPlayer2 on world 91 in the 0.695 client — two clients + two worlds did not
      fit in the free RAM): FriendOnline across worlds; tells both ways (the client's own /reply went to the world-92
      sender, so it took the incoming tell); mail both ways with NewMail on each world and coins taken; guild created
      on 92, joined from 91 (roster Location 62147:571468), guild chat both ways; leave + re-enter within the grace →
      nothing to guildmates; real leave → offline after 30 s; TestPlayer2's client killed → world drops it after its 180 s
      heartbeat, then FriendOffline + guild offline on 92. Seen once, not reproduced: IgnoreList / IgnoreListSync /
      SocialStateForZone sent twice on one entry. Step 6 down-behaviour ✅ (§5.6: mail answered with the client's own
      unavailable codes, chat lines get a system line; tested on 0.695). In-client friend add ✅ 2026-10-16 (the client's own /friend add through the service, with the new confirm step, 5.4; clicking Accept still needs a person);
      Game Finder module (0.695) ✅ 2026-10-16 (social MatchmakingService, see "Cross-world services"). Claims "friends" / "guild" default access (§4): resolved 2026-10-15 from existing research, no change needed — the
      shipped UI (re/as3/ClaimManagementWindow SecurityPanel.as:285) calls SetBasicSecuritySettings(site, public value)
      only and AdvancedSecurity always passes isGuild false; the client's own check FUN_143b5b480 is always called
      with the friend / guild flags 0; the wiki has "set for the public, or per named player" (trace/systems/claims.md).
      Wire noted: 0x2D = u64 claim, u32 public, u32 friends, u32 guild, u8; 0x2E = u64 claim, u8 isGuild, string name,
      u8, u32 mask, u8 (both "same" in 0.695).
- [x] **B.2 Gateway** (gateway-service.md §5; every step built and client-tested on 0.695 by 2026-10-16 incl. routing by registration, the failure run and cross-world hand-over (B.10); the 0.589-client run is dropped — 0.589 is frozen): step 1 library ✅ (a2fc3de + LandmarkEmulator.Internal); step 2
      in-process seam ✅ 2026-10-15 (Network/ClientSession.cs gateway side, WorldSession zone side, IClientLink; the
      network manager now closes timed-out sessions so the zone saves them; 0.695 test: login, /say, mail, Spire
      re-zone to ClientIsReady, client disconnect and 180 s timeout both leave + save; logout → character select and
      back can't be reached by the direct test copy → TESTING-NEEDED); step 3 own process ✅ 2026-10-15
      (LandmarkEmulator.GatewayServer + docker/gateway, the world's GatewayLink / RemoteClientLink behind
      GATEWAY_INTERNAL_ADDRESS, off by default; the gateway-side logic shared as Gateway/Network/ZoneGatewaySession;
      ticket check still read from the auth DB, TicketConsume via the login service later; 0.695 test through the
      gateway container: login, /say, inbox, Spire re-zone; world restart → its shutdown logs the player out; gateway
      restart → zone closes + saves at once, re-registers); hold + reattach ✅ 2026-10-15 (§3.5: zone container killed
      with the 0.695 client in the world → held, ChannelIsRoutable 0 / 1 in the client's NetInfo.log, restarted zone
      re-attached it and re-zoned it, chat + mail after; zone down > 120 s → closed, client OnDisconnect); login
      service ✅ (§3.7: TicketConsume / RelogSession on the auth server's internal port 20210; gateway without DB
      access; hold end → 0xF7 relog session, the client answered with ClientLogout and left); routing table per
      channel by registration ✅ 2026-10-16, untested (GatewayServer/RoutingTable.cs + ZoneBackEnds.cs + GatewayClient.cs:
      (world, channel) → registered back ends with zone ids; channel 0 the landscape's zone, 1-3 a registered service
      else the zone; per-channel ChannelIsRoutable on loss / return, ConnectionIsNotRoutable + wait at a login with no
      zone; zone sends ZoneChanged 0x27; gateway-service.md §3.3 / §3.5 notes; TESTING-NEEDED "Gateway routing by
      registration"), movement fan-out ✅ 2026-10-16 (MovementFanout.cs: one 0x16 list per mover and tick to its viewers on the gateway), profiler + gateway commands ✅ (01d1a37 / e06bcde, verified on the client);
      0x16 fan-out; opcode 9 commands; profiler data; isolated test restarting the zone container with a 0.589 and a
      0.695 client connected. Risk L73 (same-landscape re-zone after a zone restart).
- [x] **B.3 Environment Server** (terrain-service.md §7; steps 1-6 built, in-process and service mode tested 2026-10-16, click-only checks in TESTING-NEEDED): 1 archive + library split ✅ merged 7304e4a
      (IEnvironmentServer, LandmarkEmulator.EnvServer.Core; 0.589 login + terrain checked in the client; the spec's
      in-game regression checked on 0.695 2026-10-15 through dev commands: dig off-site → CellsUpdate + loot, on another
      player's site refused; heal (regendelay 10) → "Terrain regenerated in 1 cell", CellsUpdate; spire and cave
      re-zones load; outpost 0 loads ruin, props, creatures, combat. Not covered: the client's own pick swing (the
      landmark64.exe input grant was declined) → TESTING-NEEDED); persisted heal queue ✅ 2026-10-15 in-process
      (§4.3, HealQueue.cs: one heal per destruction at its own time, survives restarts; tested on 0.695); then the
      `envserver` container: undo, ownership, veins, LOD, template stamps, ruin layers (Desired / Active / Failed), job
      counters. **Decided 2026-10-16 (terrain-service.md §5.5): no zone-side voxel mirror — every voxel write is one
      transaction in the service** (the zone checks permissions first and sends the player's material budget / the
      destruction filter / site boxes as data; it keeps only the version index and a CellsUpdate cache). Steps 3-6 ✅
      2026-10-16 (agent; **built, compiled, smoke-tested in one process over loopback; untested on Docker and with the
      client; off by default — dev servers unchanged except that the in-process building path now runs the same
      transactions**): messages LandmarkEmulator.Internal/Environment/EnvironmentMessages.cs (envelope = ServicePacket +
      request id + landscape, every answer EnvServerResponse 0xF4/0x15; client ids 0xEC/1, 0xEC/0xB, 0xF4/1, 0xF4/4,
      0xF4/0x18, 0xF9/0x33, 0x1E/0x4AB, ours ≥ 0x10000); EnvServer.Core: IEnvironmentServer data-only (ApplyEdit /
      WriteCells / ClearCells / BuildCellsUpdates, VoxelFilter, VoxelBox, MaterialBudget — EnvironmentTypes.cs),
      EnvironmentWire.cs, LocalEnvironmentServer transactions, BuildMaterials moved in, VoxelTemplate.Source /
      CellsAsOtd, TerrainStore.BuildManifest(entries), TerrainEdits.AtomicWrites / Zones, OreVeins.OreItems; zone
      Game/Terrain/RemoteEnvironmentServer.cs + EnvironmentServer.Configure / Update / Stop (ENVSERVER_ADDRESS /
      WorldServer.json EnvServerAddress, EnvServerStartupWaitSeconds), Building / BuildingClipboard / ZoneTerrain
      (batched broadcast, owed manifest notes, ZoneDisplayNotification 0xF4/0x1B text 562155) / TerrainHandler (batched
      answers) / SiteDesigns / VoxelDestruction / Abilities (filter as data) / Claims (AllBoxes; site delete and lapse
      wait while the service is away) / Spire (PlatformBoxes) / HostedService; new LandmarkEmulator.EnvServer (EnvHub,
      DataImport, RuinRecords, JobCounters, EnvServer.example.json; port 20500); Dockerfile stage `envserver`,
      docker/envserver, root compose profile `envserver`, docker/world EnvServerAddress, .env.example; re/test/socialtest
      up.sh ENVSVC=1 / down.sh / socialtest.env; docs/terrain.md "Where it runs", docs/docker.md "Environment Server".
      0.695 check: 0xEC 1-5 / 0xA / 0xB and 0xF4 1-4 / 6 / 7 / 0xA / 0x16 / 0x17 / 0x1B "same"; 0x1B reader read in the
      0.695 decompile. Next: step 7 on the isolated stack (TESTING-NEEDED.md "Environment Server in its own container"),
      then placement (outposts / Spire markers) into the service (L127). Leads L125-L128.
      ✅ 2026-10-16 committed 2fef97a (agent); in-process regression on the test stack (one-process world from HEAD, 0.695
      client): claim placement with vein moves, carve, dig + heal queue ("Terrain regenerated in 2 cells") all fine, no
      errors, no new VoxelDataErrors lines; the client's own build-tool edits through the new Building path need a person
      (TESTING-NEEDED, first item) — the agent's in-process harness covered edits / budget refusal / undo / Heal tool.
      ✅ 2026-10-16 service mode on the isolated stack (ENVSVC=1, 0.695 client): one-time import (24,472 files, 81 ruins, the
      Spire stamp; "done before" on restart), envserver ~200 MB, login fetch with 0 new VoxelDataErrors, cave re-zone, dig
      through the service healed on time by the service, service stopped → digs do nothing, service back → relinked.
      Found + fixed: CellVersionsChanged's size check (21 instead of 17 bytes) dropped the link on every larger push
      (9bb1ebe). Notes: a first start's import outlasts the world's 120 s wait (it then runs read-only until the link
      comes, ~1 min later); the client's 0xF4/0x1B notice needs a refused build-tool edit (a person).
- [x] **B.4 World-channel + commerce** (world-channel-service.md; steps 1-8 built by 2026-10-16 — 2-4 client-tested, 5-8 untested, see TESTING-NEEDED): 1 interface split ✅ merged 6d7dbbf
      (Game/WorldChannel: IWorldChannelService, ICommerceService); 2 pass-through ✅ 2026-10-15
      (LandmarkEmulator.WorldChannel + docker/worldchannel: registers with the gateway for channel 1 and hands every
      packet to the zone's internal port 20310; the gateway routes channel 1 to it, to the zone while it is away;
      0.695 test: /join through gateway → world channel → zone → social); 3 sinks ✅ (SetLocale kept per connection,
      SteamInfo, LuaMetrics, leaderboard requests end in WC); 4 logout session ✅ (0xF6 in WC: key = zone relog tag +
      random via the login service's RelogSession, NoteRelogin to the zone, 0xF7 to the client; tested with
      re/test/socialtest/FakeGateway since the direct test copy never sends 0xF6: 0xF7 01 "R05B53A2E784…", account
      session key saved); 5 trade broker ✅ 2026-10-16 (agent, 367ef81: TradeBroker.cs, TradeZoneSessionStart / Ready / Open / End; untested — test plan in the commit's agent notes: GATEWAY=1 WC=1 up.sh + trade.json scenarios); 7 encounters ✅ 2026-10-16 (agent; untested: LandmarkEmulator.Internal/WorldChannel/EncounterMessages.cs (0x10050-0x10054), LandmarkEmulator.WorldChannel/EncounterBroker.cs + ChannelRelay / HostedService / WorldChannelConfiguration / WorldChannel.example.json, WorldServer Game/Encounters.cs + Game/WorldChannel/WorldChannelLink.cs + IWorldChannelService.cs + Network/GatewayLink.cs, docs/encounters.md "Where it runs"; test plan TESTING-NEEDED.md "Duels through the world-channel container"); 6 ratings ✅ 2026-10-16 (agent; untested: LandmarkEmulator.WorldChannel/RatingBroker.cs + VoteStore.cs (own schema lmemu_wc_<world>.claim_vote, kind-11 import at start), LandmarkEmulator.Internal/WorldChannel/RatingMessages.cs (0xF9/0x10040-0x10041, 0xA7/0x10042-0x10044), zone Ratings.cs ClaimInfo / BrokerCounted / BrokerRefused + WorldChannelLink / GatewayLink / DevCommands ratevote, docker/worldchannel + db-init grant; contest votes stay in the zone until `commerce`; test plan TESTING-NEEDED.md "Build-site likes through the world-channel container"); 8 shared `commerce` ✅ 2026-10-16 (agent; **built, untested**: new LandmarkEmulator.CommerceServer (CommerceServer.cs, HostedService.cs, CommerceServerConfiguration.cs, CommerceServer.example.json, CommerceHub.cs, CommerceStore.cs — own schema lmemu_commerce: commerce_world, product (+ design snapshot), product_review, taken_design, contest, contest_entry, contest_vote, portrait, commerce_import —, ProductService.cs, ContestService.cs, PortraitService.cs, ClientPackets.cs), LandmarkEmulator.Internal/Commerce/CommerceMessages.cs (0x45/0x2F + 0x10002, 0x10010-0x1001F, 0xF9/0x10020, 0x98/0x10030-0x10031; spec §6.3 "Built" lists the deviations: the zone is commerce's client, realm keys), zone Game/WorldChannel/RemoteCommerceService.cs + ICommerceService / LocalCommerceService / WorldChannelServices (Configure, COMMERCE_ADDRESS / WorldServer.json CommerceAddress, off by default), Commerce.cs (CheckProduct shared; FromWire / ToWire no longer shift a second time), Portraits.cs (PlayerImageData sub 2 bytes), Ratings / Achievements / Gallery / Store / WebPages / DevCommands / AdminMonitor / HostedService / WorldServerConfiguration through the seam; docker/commerce, Dockerfile stage `commerce`, docker-compose.yml profile `commerce`, docker/world CommerceAddress, db-init grant, .env.example; docs/commerce.md "Where it runs", docs/docker.md "Commerce service", docs/showcase.md; test tooling re/test/socialtest up.sh COMMERCE=1, down.sh, socialtest.env, fakeworld.sh + FakeWorld commerce commands; one-time import: DB rows at the service's first start, each world's Contests/ + Portraits/ files at its first link; 0.695 check: 0x45 renumbered (double shift found + fixed), 0xA7 / 0xF9 0x41 / 0x18 / 0x98 / 0x75 / 0xFE same; test plan TESTING-NEEDED.md "Player Studio, contests and portraits in the shared commerce container"; leads L123, L124). Next: B.4 step 9 down tests with the client (WC / commerce / login), step 10 the 0.695 client against the split stack.
- [x] **B.5 Split stack verified, then the dev layout** (2026-10-18 DONE: the one dev server runs gateway + world + social, re/server/deploy.sh; user: one server only) (recreation-design.md §5 #1, the recommended next step —
      user to confirm): in an isolated stack run gateway-service.md §5 steps 6-8 with `gateway` + `worldchannel` +
      `social` together on the 0.695 test copy (zone restart with the client connected, edge cases, profiler bytes /
      player / min against the monolith) and check the built-untested parts (movement 0x16 fan-out, trade broker,
      duels, cross-world Game Finder) with a scripted second player (FakeGateway / FakeWorld); then, with the user told
      and a backup, switch the dev world to the split layout (one compose profile), and finish B.2's routing table by
      registration (channel mask + zone ids per back end, ConnectionIsNotRoutable when none; §5 #2) — ✅ 2026-10-16
      routing by registration, untested (see B.2; test with GATEWAY=1 WC=1 up.sh, TESTING-NEEDED).
      ✅ 2026-10-16 first combined run (GATEWAY=1 WC=1, social, own -test images, one 0.695 client + FakeWorld 92):
      gateway + world channel + zone register, /say through the social service, /gatewayprofiler handled by the gateway
      container, cross-world Game Finder counted both worlds (players 2; 9001-9003 active / waiting / inactive; other-world
      match reported, L31), /duel → world command → encounter path. Found + fixed: 16 opcodes ≥ 0x100 in the wrong enum
      form (b653b73; CorrectNpcHeights 0x114 had never reached its handler — the logged bytes parse exactly as the model:
      game time, 1 × guid 0x5100000000000001, 1 × f32 439.4; the client sends it only for an NPC with a pending height
      correction, none came in the follow-up session, so the handler's first live hit is still to see). Still to run: zone restart with the client
      connected, trade broker and duels with two players, profiler bytes against the monolith.
      ✅ 2026-10-16 failure run (images from a clean HEAD export, GATEWAY=1 WC=1, 0.695 client): world channel stopped →
      gateway "channel 1 -> its zone", chat still reached the zone; WC back → channel 1 routed to it again; zone container
      killed → client got ChannelIsRoutable 0 for channels 0 / 3 and kept responding; zone back → gateway re-attached the
      client to the new zone, ChannelIsRoutable 1 for 0 / 3, the zone re-zoned it. Found + fixed: the re-attached session
      never rejoined the player list while idle (88d9bd5). Still to run: trade broker and duels with two players (needs two clients / a person).
      ✅ 2026-10-16 profiler bytes against the monolith (/gatewayprofiler, 120 s idle at the Spire, same world data, 0.695
      client): own gateway out 1,753 / in 4,733 bytes; gateway container out 2,855 / in 5,159 — the whole difference is 24
      PlayerUpdate 0x6A InteractionInfoReply (51 B every 5 s) answering the client's interaction-info requests (it faced an
      interactable in the second run; the extra 426 bytes in are those requests): the split adds no traffic of its own;
      an idle player costs ~15 B/s down. Gotcha: starting the gateway container while the one-process world still holds
      UDP 19100 leaves the container without a network — recreate it (--force-recreate).
- [ ] **B.6 Zone per landscape + handover** (2026-10-18: planned with the user — steps 1-3 in trace/0695/b6-zone-split-plan.md: fixed split by ZONE_LANDSCAPES, then spill-over on demand; build only when one machine can't keep up) (BUILD-PATH B item 6; recreation-design.md §5 #8 / #9): only if one process
      can't hold a world's players. Uses live's names PlayerTransferServerRequest / Reply 0x0B / 0x0C,
      TransferPlayerToServer 0x09, ZoneToZonePacket 0x18 (internal-messages.md §1; layouts ours, L48), gateway HandOver
      0x25 (gateway-service.md §3.6); open: L73, L114. A separate claim (build site) service only after this
      (ZoneAddClaim / ZoneAddAllClaims feed several zones).
- [x] **B.9 Groups across worlds** (recreation-design.md §5 #4; P4906 "You no longer have to be on the same world to
      remove a group member from your group"): groups stay zone-side today (social-service.md §5.1). Check GroupsBase
      on 0.695 first (rule: check 0.695 before porting), then group membership and party chat in the social service.
      ✅ 2026-10-16 (agent; **untested** in the client): GroupsBase checked "same" in 0.695 (coverage-695 §0x2B; requests
      on the Zone channel); with SOCIAL_ADDRESS membership, invitations, kick / leave / leader / disband and party chat
      run in the social service (SocialServer/GroupService.cs) for players on any world; the zone publishes its members'
      party-frame records (GroupMemberRow 0x2B/0x10001) and keeps GroupStateForZone (0x2B/0x10002) for the cave group
      trip, loot / gathering shares and goals on its own world; positions only reach players on the same world; a group
      survives a world change (presence grace, linkdead meanwhile). Client packets from one shared builder
      (Shared/Game/GroupPackets.cs), byte-identical to before; without SOCIAL_ADDRESS nothing changes. social-service.md
      §10, test plan TESTING-NEEDED.md, open: OPEN-LEADS L122.
      ✅ 2026-10-16 client test (isolated stack, images from 36d496d, one 0.695 client on world 91 + FakeWorld 92): the client's /invite Wanderer reached the fake world (Invite from TestPlayer), accept → Party frame shows TestPlayer + Wanderer, Wanderer's health bar follows `row 901 500`; party chat /p reached world 92; /invite Roamer + accept → 3 members (AddMember to the others); /party kick Roamer → RemoveMember reason 4 to both fakes; Wanderer leave + enter within the grace → group back (AddGroup, flags 2); /party disband → RemoveGroup reason 4; an invite from world 92 shows the client's own "Wanderer has invited you to join their party" Accept / Deny popup. Not run (needs a click): Accept / Deny on that popup, the 2-minute expiry, a service restart.
- [x] **B.10 Cross-world travel from the teleport window** (recreation-design.md §5 #5; wiki Servers and Landscapes,
      P0514, P4400, P5734): trace L114 first (how a world change reached another world's servers; CharacterTransferServer
      trigger); then list every running world in Locations (0xFC/3) from the login service's server list and move a
      TeleportRequest {world, zone} to another world the way L114 shows. Today each world lists only itself (+ Events)
      and a world change needs character select.
      ✅ 2026-10-16 L114 traced (OPEN-LEADS: the client's CharacterTransferServerRequest sender is unreachable — no pointer, call or jump in the 0.695 exe — so a world change is a re-zone on the same connection) and built (0afe874) and client-tested 2026-10-16 — isolated stack, MultiWorld gateway with world 91 (client id 91 Liberation) and world 93 (client id 93 Courage, re/test/socialtest/world2.sh), one 0.695 client: gateway "Worlds reachable: 91, 93", Teleport.TeleportToLocation(93, 406) → world 91 let go, gateway handed over, world 93 brought the player in at its Spire (loading screen 16 s, ClientIsReady on 93, dev commands reach the player there), and back to 91 the same way; no errors in any container. Not run: picking the world in the window by mouse (TESTING-NEEDED). Design: MultiWorld gateway (GATEWAY_MULTI_WORLD) routes several worlds' zones with a world per connection, WorldsReachable 0x29 to the zones (client world id + name text from RegisterBackEnd), Landscapes window lists them, TeleportRequest to one → target landscape + LastServerId saved, session dropped, HandOver 0x25 with the target world → transfer attach → that zone re-zones the client to the landscape's arrival (Game/CrossWorld.cs). Client world id / name per world (CLIENT_WORLD_ID / CLIENT_WORLD_NAME_TEXT, default 2 / Liberation). Live's list came from where [I: the gateway's registrations here]; worlds in other data centres (L114) not modelled.
- [x] **B.7 Admin page** (admin-pages.md, agent #17) after the services it shows. ✅ 2026-10-16 built (untested in game):
      world web page /admin/monitor + /api/admin/monitor (Game/AdminMonitor.cs, AdminMonitorPage.cs, WebPages.cs; button in
      /admin): zone stats, per-system tick times, gateway / social / world-channel / commerce link states, terrain heals,
      site games, procedural nodes, packet table (OpcodeStats live counts + bytes), players. ✅ 2026-10-16 status from the other containers (ServiceStatus every 10 s from gateway / social / world channel / commerce, a table per service on /admin/monitor, 4d35b20; untested in a browser). Env Server job counters ✅ with B.3 (2fef97a, "environment server" table). Pathing: page 32 answers "Nothing to display" because the server has no navmesh — tracked as its own system, 6.61.
- [x] **Bug: a terrain lookup for "landscape 1048576"** (fixed: Gathering.SendFull took creature guids 0x50.. / 0x51.. for tree nodes; top-byte check) (2^20) at the first 0.589 login after the 2026-10-15 merges
      (console.log: TerrainMaterials "No biome map for landscape 1048576", TerrainHeights the same, right after the
      player's props were sent). Find the caller passing that zone id (TerrainMaterials.MapOf callers); not seen on
      the next login (cached).
- [x] **B.7 merge admin-pages.md** (agent #17 done; all parts built 2026-10-16, client check of pages 28 / 29 pending in TESTING-NEEDED): page 29 = 0x10D sub 2 {u32 n; key, value, u32 indent} pushed
      to admins; page 28 0x10A needs the two server-only id tables or it draws blank; web page /admin/monitor in the
      world's WebPages; fix the families generator (InstancedGame admin monitor is sub 2, not 4).
      ✅ 2026-10-16 (untested in game; admin-pages.md §11): page 29 built (Game/InstancedGames.Monitor.cs, push in
      AdminMonitor.Update, setting AdminMonitorPush default off); page 28 built from the placement sets / Gathering nodes
      with both row tables filled (ours mapping; L2 / L82 stay open for live's tables and field meanings); /admin/monitor
      built; generator fixed (re/tools/subnames.py applied a switch's bias twice: 0x10D now AdminMonitor 2, PropRecipe 4,
      State 6…, Template 0xF0 too; families 010D / 00F0 + their FAMILIES-SUMMARY lines regenerated).
- [x] **Creature full data** ✅ 2026-10-15: LightweightToFullNpc 0x26/0x5C traced (reader FUN_142b8f230, handler
      FUN_142c45940 → apply FUN_142cb9f50; the dispatcher switches on sub − 1, so Ghidra's case 0x5B is wire 0x5C) and
      sent by Creatures.SendFull with the client's constructor defaults; 0.695 test (outpost 0): 36 requests answered,
      none repeated in the next minute, no client read errors, creatures rendered and fought. Live's field values for
      creatures: L85.

## 9. Phase E — full audit findings (trace/AUDIT-0695.md)

- [x] **E.0 Packet reachability** (2026-10-17): coverage / trace / reachable re-run; 15 gaps, 1 real — GuildInviteRevoke
  0x83/6 built with the client's invitation states (9fbb106); 14 verdicts added (re/tools/verdicts.tsv). 0 / 0 left.
- [x] **E.1 Placement packs and mover props** (2026-10-18 VERIFIED in game: platform carries the rider — round 88; Translocation Pad translocates and the flinger throws onto its target — round 92) ✅ platforms built 2026-10-17 (merge e-props2: Props.Platforms.cs; ride test in TESTING-NEEDED); ✅ 2026-10-17 movers traced and built (22dd72f, Props.Movers.cs, trace/0695/movers-695.md): a mover is placed as its pack's parts with their client areas; Translocation Pad / Dark Elf Teleporter moved by the server on TeleportManagedRequest 0xFC/10; flinger and launcher are client physics; settings keys found (L134 closed); platform legs harness-checked at 1.6 m/s. Left: whether a rider stays on a moving platform (L135, runtime), pads / flinger / launcher untested in game (data tables audit 2026-10-17): PropDefs PLACEMENT_PACK_ID names a pack
  (PlacementPacks.txt: MIN / MID / MAX_SEPARATION_DISTANCE) whose parts are PlacementPackProps rows {pack, prop, index,
  offset x / z, scale, heading}: Translocation Pad (prop 1196 → 2 × pad 1194, 78-298 apart), Spring-loaded Flinger
  (1200 → flinger 1188 + target 1191), Magical Platform (1207 → platform 1193 + 2 docks 1192), Glass Platform
  (1423 → 1421 + 2 docks 1422), Three dock moving platform (1288), Spring-loaded Launcher (1328 → 1233), Dark Elf
  Teleporter (1213 → 2 × 1206), Test Portal (1178). Wiki ("Translocation Pad", Category:Movers): "a mover prop … two
  parts that work together … adjust the location of each part … to set the direction and way it moves player
  characters who stand on it". Items 1290 / 1285 / 1289 are innate. The server knows only the on / off toggle
  (StoryTools "Pad" / "Platform" / "Flinger" / "Launcher" kinds); PlacementPackId is read but unused. Client:
  ClientPlacementPackPropDefManager (0.695 global DAT_14543b4a8; FUN_143535a60 maps a part's prop to its pack,
  used by the known-prop lookups FUN_142f03b90 / FUN_142f03de0). To trace: how the client places a pack (what it
  sends), how parts are moved within the separation limits, and what moves the player (client physics or server).
- [x] **E.2 Re-prove the "unreachable" verdicts** ✅ 2026-10-17 (first full pass; parked items stay open in L136-L140) (user, 2026-10-17: EQN / PS2 code was reused by Landmark — the movers
  run on the area system an earlier verdict called a dev tool). 169 verdicts in re/tools/verdicts.tsv say unreachable,
  87 of them by origin ("EQN / PS2 leftover"). Each needs proof against 0.695: (1) no 0.695 UI / Lua caller — done
  2026-10-17 with re/out/ui_server_map_695.md: no new UI caller for any of them; (2) no C++ path from a live game system
  (areas, prop spawn fields, physics, timers, abilities) — to do, class by class; (3) no data enabling it in the 0.695
  tables; (4) no design source describing it. Unproven ones become OPEN-LEADS and, where the feature existed, tasks.
  Progress 2026-10-17: (1) done for the client→server ones; (2) C++ caller walk (scratchpad cpp_paths.py, callgraph
  depth 4) for all 70 client→server ones: 13 are handled / sent today (verdicts updated), the rest reach only Lua
  natives with no UI caller, slash / debug commands, or replies to server messages Landmark never sends (mounts: no mount
  items or notes); (4) design-source keyword sweep (wiki dump, patch notes, forum): **reopened** map waypoints (L136)
  and the Showcase store natives incl. following creators (L137 / L123) — parked, not written off. Still to do: the 89
  server→client ones against the 0.695 UI reactions (first pass: nothing new besides generic event names).
- [x] **E.3 Client→server messages with no Lua sender** ✅ 2026-10-17: all 209 triaged into verdicts.tsv (handled /
  covered / informational / unreachable with evidence / **parked** on L138-L140 where not provable); reachable 0 / 0, 478
  verdicts; runtime: the only unanswered request in play is Claims 0xE3 (dev, 6×; no client builder sends that sub — now
  logged with its body to identify it). (209 unhandled, no verdict; scratchpad list c2s_unhandled.tsv,
  reachable.py only follows Lua natives): triage by the same proof rule; found so far: TeleportBase 9 / 10 (built in E.1).
- [x] **E.4 Ownerless loot pinatas** ✅ 2026-10-17 (claim reply 0xFB/4; rule from the client: only unearned loot spawns ownerless — the fungus blast terrain loot; trace/0695/agent-loot-rule.md; live rule itself stays L141 [I]) (user, 2026-10-17: not "covered" — build it): the claim answer is built (sub 7,
  commit after 45e73db); open: **when** live made loot ownerless (owner gone / timed out / group loot?) — OPEN-LEADS
  L141. Then call LootPinatas.SpawnOwnerless from that rule.
- [x] **E.5 Re-check every "covered" / "informational" verdict by the same rule** ✅ 2026-10-17 (user's point on the
  pinatas): a client path our server merely never triggers is a feature to build or a lead, not a closed item. 134 rows
  re-checked: 7 were already handled / sent (ClientKickedFromServer on a duplicate login, Guild News 0x1A built
  e789614, GuildNotification 0x19, design Unknown / MissingPropList, EnvironmentAddOwnershipResponse, TeleportCancelQueue
  answer); 62 admin / GM packets → **E.6**; 7 telemetry / external hooks → **E.7**; login queue → **E.8**; 22 real-money
  store rows parked on decisions 6.59 / 6.60; travel queue parked on L60; 5 → leads L142 / L143; 34 stay with their
  proof re-read (client-side tool switches, log-only texts, replies to cases the server answers another way).
- [x] **E.6 Live's GM tools: the remaining admin packets** (E.5): 62 client→server admin / debug packets outside the
  Command 0x1E subs already built (AdminCommands.cs): SkillAdmin* (grant / reset skills, skill lines, skill sets,
  points), ItemAdmin* (item-line trial timers), item commands (PacketAddItem / DeleteItem / GetItemList / GetItemInfo /
  RandomItem / Vault / Collection / Overflow / Buyback by id), GriefAdmin* (grief / damage level), AcquireTimerAdmin*,
  CurrencyAdmin* (discounts), LeaderboardPacketAdmin*, ClaimsPacketSetAsAdminClaim / IdDebugAddItemClaim,
  AdminCommandPacket Storybricks / monitor / path / player count, Content builder commands, MiniGameFlash admin,
  ExecuteScript / AddWorld / AddZoneCommand / ItemDefinitionReply / RendererDump replies. For each: the 0.695 writer
  and its slash command (layout [P]), the server action on the existing system, gated by Admin.IsAdmin; table in
  trace/0695/admin-commands-695.md.
  Progress 2026-10-17: /item built (11 subs, AdminCommands.Items.cs); grief built (Grief.cs: the client's GriefBase 0xC0
  texts + GriefAdmin 0x4DA); AddWorld / AddZoneCommand, ItemDefinitionReply already sent; **39 proven unreachable** on
  the retail exe with the new re/tools/refscan.py (no code or data reference to the sender at all: Skill / AcquireTimer
  / ItemAdmin / Currency / Leaderboard / MiniGame / Grief admin senders; Content.* natives with no caller;
  SetAsAdminClaim / EnvServer behind the admin-client check); parked: monitor-page requests on L4, DebugPathQuery L145,
  claim items L146, leaderboard admin L59. Older "unreachable" verdicts re-scanned (re/tools/unreach_scan.py →
  re/out/unreach_scan.tsv): of 236, 33 have senders with no reference at all (proof added, incl. vehicle spawn / active
  vehicle, missions, PunkBuster, weapon toggle effects), 127 have a referenced sender (their caller-chain reason stays:
  Lua native without a caller, admin client …), 76 have no 0.695 sending function in the decompile text (UI call names,
  or sent through another connection). The whole reverse call graph (re/tools/deadcode.py → re/out/deadcode_unreachable.tsv) agrees: the 33 stay dead, each of the 127 reaches a pointer root (Lua binding / command table / vtable), so their reasons stay with the caller-side proof (no UI or Lua caller).
- [x] **E.7 Telemetry and external-service hooks** ✅ 2026-10-17 (e004002; Game/Telemetry.cs → Telemetry/<date>.jsonl, routed through IWorldChannelService.ClientTelemetry; none answered — the social-share and Spice web-auth replies would send the client to an outside web service) (E.5): WallOfData keyboard / voice chat, ClientLuaMetrics,
  SocialShare provider status / publish, CommerceSession, OptInAccount, SpiceWebAuthUrlRequest — decode each and record
  it in the world channel's sink (as live's collectors received them); answer the ones the client waits on.
- [x] **E.8 Login queue** ✅ 2026-10-17 (built, client side needs a Play click: TESTING-NEEDED) — live flow traced: CharacterLoginReply result 8 → characterselecthandler.lua CreateLoginQueueDialog; login tunnel type 3 QueueStatus {i32 place, i32 members, i32 non-members, i32 ×2 seconds per place, u8 member} feeds LoginQueueDataSource (state 1); tunnel type 4 ServerQueue closes the dialog and makes the client re-send CharacterLoginRequest. Built in the auth server (Zone/LoginQueue.cs, e73a744) from the world limit WorldServer "MaxPlayers" / MAX_PLAYERS reported with the heartbeat (zone_server.maxPlayers). The zone packet 0xE0 is the travel queue (state 2, closed only by TeleportBase 6 / 7) — L60. (E.5): PacketLoginQueueStatus — when a world is at its player limit, live queued logins and
  sent the queue position; build it on the world capacity setting.
- [x] **E.9 Get Inspired** ✅ 2026-10-17 (22da502; features audit #93): copy / Save Design needs the site's copy permission
  on every build site under the selection (Claims.MayCopyIn); test step in TESTING-NEEDED.
- [x] **E.10 Graveyards** ✅ built 2026-10-17 (merge e-graveyards; harness: 81 cavern / 32 surface; the cavern template graveyard_launch.otd stamped, 0fb9cc4, on in dev, plinth seen on the test world; Underground Adventure lands on a graveyard, 9a4ee5a; by eye untested in game, TESTING-NEEDED) (audit #47): cavern Graveyard01 spots as respawn points, the safety buffer, surface graveyards
  (in progress, branch e-graveyards).
- [x] **E.11 Story tool links** (2026-10-18: weapon model on the creature built, 827fb93; data L95) ✅ built 2026-10-17 (merge e-story; in-game test pending; left: the weapon model on the creature — both carriers proven, live's per-creature weapon data server-only, agent-storyhide.md / L95) (audit #6 / #22 / #28): spawner Equip / Unequip weapon, Spawn With Weapons Equipped, Story
  Node Weapon Visuals, Game Table "On Game Ended" (in progress, branch e-story).
- [x] **E.12 Abilities and emotes** ✅ built 2026-10-17 (merge e-abilities; Roll Wraps source settled: never released, L26 closed; /playdead line fix 984c517; untested in game) (audit #55): rank 2 / 3 weapon abilities, /playdead, Roll Wraps source (in progress,
  branch e-abilities).
- [x] **E.13 Cross-world** ✅ 2026-10-17 (friend travel 7de005d; Game Finder Join Now across worlds, merge e-travel; two-client tests in TESTING-NEEDED) (audit #63 / #29): teleport to a friend on another world ✅ built 2026-10-17 (needs a two-client test); Game Finder match on another world
  (B.10 hand-over).
- [x] **E.14 Chat channels** ✅ 2026-10-17 (merge e-chat: General membership, bubble colour + migration ChatBubbleColor, chat-room parse fix; senders are Lua-only, report agent-chat.md) (audit #91, L65): public channel on / off (0x1E 0x22 / 0x23), chat bubble colour (0x15).
- [x] **E.15 Outposts** (2026-10-18: L131 built, 7e9af69 — Lost Crystal / Underground trips to other worlds) ✅ banner + Local Travel per cave landscape built 2026-10-17 (merge e-outposts); rotation built 2026-10-17 between ruin slots of the same cavern (779229a, OUTPOST_ROTATION_HOURS default 0 = off: no source rule); portal crystals on the stamped graveyards (L130 closed); left: L131 — still open: pass 2 reads P5734 as other worlds (servers) [I strong], against the sweep's "landscapes"; trips prefer another landscape / biome (01e6ad6); (audit #60 / #65): outpost entry banner (L102), outposts rotating between caverns, Local Travel
  crystals off the home landscape (L130), other worlds (L131).
- [x] **E.16 Journal tracker goals** ✅ 2026-10-17 (merge e-goals: all 1,269 objectives have a counting path) (audit #88, task 5.10): item-name tracker goals.
- [x] **E.17 Play Mode visibility** ✅ 2026-10-17 (merge e-playmode: hidden story tools aren't sent while a site plays; Respawn Point look 848 / 849; non-biped creatures have no emote / mood animations — proven, agent-playmode.md) (audit #1 / #16, L20) and story nodes on non-biped creatures (#6, L117): trace first.

## G. Wiki gap audit (2026-10-18, trace/0695/audit-wiki-gaps.md)
- [x] **G.1 Economy exploits and item rules** (merged cec6e42; in-game checks in TESTING-NEEDED): wear survives trade / mail / chests; chest no-trade withdrawals only by the depositor (561999); design items keep their design through chests; Player Studio designs not exportable / copyable by visitors (563373); store purchases tradeable after a wait (577356); overflow expiry 24 h; mailed stacks ≤ 9,000.
- [x] **G.2 World / UI gaps** (merged 96ef517; in-game checks in TESTING-NEEDED): story creature mood / stance persist; chest cap 50 per claim (account cap off by default); Player Studio review eligibility, anonymity, staff queue; Founder's Chest (898) at Spires; voice chat 0xB9 answered 'unavailable'; Build Mode creature counter; teleport-to-friend cooldown; slate slab lead.
