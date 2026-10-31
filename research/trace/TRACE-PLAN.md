# Full system trace — plan and progress

> 0.695 rebase (2026-10-26): addresses below are 0.589 unless marked 0.695; their 0.695 partners and changes: trace/0695/address-map-589-695.md, trace/0695/rebase-695.md

**Directive (user, 2026-10-12):** stop feature work; fully examine and trace every system of the 0.589 client first,
save it so it can't be lost, then keep building until the emulator has and matches all of the live servers' functions.

**After a context reset:** re-read this file, continue at the first unchecked item, keep the Progress log current.

## Approach after the trace (user approved 2026-10-12)

No full rewrite. Per system: finish its trace → audit the server module against it → rewrite the module where it
diverges (keep what matches) → verify with the test clients + TESTING-NEEDED.md checklist → next system.

## Method

Three layers, from mechanical to understood:

1. **Generated maps** (re-runnable, never hand-edited):
   - `families/*.md` + `FAMILIES.md` (re/tools/trace.py): every packet class per opcode family — direction, server
     status, reader/writer, field layout, the client functions that build/handle it with their strings (Lua events,
     data sources, text keys, log lines), and for client requests the Lua natives / UI windows that send them.
   - `re/out/subdispatch.tsv` (re/tools/subdispatch.py): exact (opcode, sub) → handler for every server message, from
     the client's own dispatchers (zone switch + each family's sub switch), so no message is left "opcode unknown".
   - Still to generate: UI windows → Lua calls → natives → packets (from re/as3 + luabind); client data tables → the
     client code that loads them and whether the server reads them; Lua events → which UI listens.
2. **System files** (`systems/<system>.md`, hand-written from layer 1 + reading the client code): for each system,
   how it works end to end in the client, what the live server must have done (evidence: client checks, texts, UI,
   data tables, archived dev notes in design/game-rules.md), what our server does today, and the gaps — each gap with
   enough detail to implement.
3. **Gap list** (`GAPS.md`): every gap from the system files, ordered for implementation (foundations first).

Live-behaviour sources (user, 2026-10-12): the Landmark wiki https://landmark.fandom.com/wiki/Landmark_Wiki (scrape per
system: features, numbers, rules, how things worked) + archived dev posts (design/game-rules.md). Save findings into
the matching system file under "Live behaviour" with the page URL.

Rules: trace the client before changing the server (read-client-before-fixing); evidence over guesses; when live data
is truly server-only (e.g. ability definitions), say so and record what client data the rebuild is based on.

## Systems checklist (layer 2)

Status: [ ] not traced · [~] partly · [x] traced (system file complete)

Core / session
- [~] Login, character select/create, zone entry, SendSelf, zone details, logout, idle, kick, shutdown → systems/session.md
- [ ] Movement, position updates, swimming, heroic movement, falling, mounts (0xA1), vehicles (0xBF)
- [ ] Time, weather, world clock, sky, areas/POI (0xEA), regions, zone coordinate info
- [ ] Client settings, preferences (0x3), UI events (0x32), first-time events, tutorials, help
World
- [ ] Terrain / VoxelFarm cells, edits, veins, regrowth (0xEC)
- [ ] Building: voxel construction (0xF4), world editing (0x10C), templates/designs (TemplateBase), paste
- [ ] Props: placement, pick-up, tints, prop rules, story props
- [~] Claims / build sites (0xF9): placement, permissions, upkeep, tags, time, music, gallery, contests → systems/claims.md
- [ ] Gathering: nodes, trees, plants, harvest, ore, tools
- [ ] Creatures / NPCs: spawn, AI, movement, combat, loot pinatas (0xFB), projectiles (0xBA)
Character
- [ ] Inventory, equipment, loadouts, item definitions, durability/repair, bags
- [ ] Stats, calculated stats, resources (health/energy pools), buffs/effects
- [ ] Abilities (0x27): caster info, casts, effects, cooldowns, ability lines, skills (0x1 SkillAdmin)
- [ ] Combat (0x24) and weapons: attacks, animations, hit effects, damage, death, revive, knockout
- [ ] Crafting: recipes (0x42), stations, refining, fuel, upgrades
- [ ] Achievements, goals, rewards (0x35), titles
Social
- [ ] Chat (0x13), emotes, quick chat
- [ ] Friends, ignore (0x72), groups/raids (0xB8), guilds (0x83), notifications/GAQ (0x1)
- [ ] Mail (0xF8), trade, vendors (0x112), coin store (0x9E)
- [ ] Social wall / share (0x9C, 0x104), portraits (0x98), ratings/contests (0xA7)
Content / activities
- [ ] Story tools / object scripting (0x111), instanced games (0x10D), encounters/duels (0x2C)
- [ ] Missions/quests (0xD1, 0x34, 0x30), activities (0xA0, 0xA8), repeating activities (0x8D), mini games (0x2A),
      match invitations (0x8A), lobbies
- [ ] Travel: Spire, warp network (0x113), teleport queue (0xFC), friends teleport
Economy / meta
- [ ] Commerce (0x45): store, wallet, Player Studio, marketplace, gallery, membership, currency
- [ ] Admin / GM / content management (0x1B, 0x1F, 0x1E admin subs), reports, bug reports
- [ ] Command family (0x1E, 86 requests): every sub

Whole client, not only the network (user, 2026-10-12: "make sure you understand everything about the client as it
may be needed by the server even if the client has no connection to it")
- [ ] Shared/server code compiled into the exe that never runs on the client (e.g. AbilityDefinitions.txt loader,
      other server-side table loaders, rule/formula code): find each, read what it loads and how — the server needs it
- [ ] Client-side game rules and formulas (stats, damage, costs, cooldowns, regen, placement checks, crafting checks,
      movement limits): the server must enforce the same rules
- [ ] Every client data table (383) and every pack file type: what it holds, who loads it, what it means for the server
- [ ] Engine systems the server must mirror (animation network/selections, effects, physics shapes, terrain, actor
      definitions (.adr), templates (.otd), audio/music ids)

Cross-cutting layers (layer 1 extras)
- [ ] UI windows ↔ Lua ↔ natives ↔ packets map (all 167 windows)
- [ ] Client data tables ↔ client loaders ↔ server use (383 tables)
- [ ] Lua events fired by handlers ↔ UI listeners

## Progress log

- 2026-10-12: plan written. re/tools/trace.py → families/ (172 families, 1370 classes; status from coverage.md,
  heuristic). re/tools/subdispatch.py started (exact opcode/sub/handler for server messages).

## Reported issues to look at after the trace (do not fix yet — user, 2026-10-12)

- Build site flag placement bugged while the TestPlayer2 test client was running (user closed the client). Check during
  the Claims trace: two sessions / test client effects on PlaceClaimRequest, overlap with TestPlayer2's site, refusals.
- Power attack (right click) stopped working after a while; no casts reached the server. Traced: not the charged-cast
  path (our weapon CasterInfo uses the instant kind hash 0xDFB05E5F; charged = 0xF7150354, FUN_142d767b0). Most likely
  Energy: power costs 60, the client checks Energy itself, the server only sends a value after paying — verify whether
  the client regenerates from it (watch the bar), else send Energy updates (CombatResourceEvent / ClientUpdate Mana 0xD).
- Weapons: hit effects missing; animation choice is a rebuild from client data (WeaponAttacks.AttackAnimation).
- 2026-10-12: tools: re/tools/buildindex.py (callgraph/strings/vtables indexes) + q.py (2 s lookups);
  serverdata.py → server-only-data.md (79 data files the exe can load that 0.589 doesn't ship, with loaders:
  AbilityDefinitions/AbilityEventMappingDefinitions (FUN_143ae2670, type "Ability"/"AbilityEventMapping"), Profile*
  (FUN_143b26e60), ItemCategories/Groups, ItemClasses, InventorySlotDefinitions, ItemMods/Quality*, ResourceTriggers,
  CalculatedStatGroupEntries, ClaimAccessLevels, TerrainCalendar, MiniGame*, Quiz*, Social*...);
  schemas.py → schemas.md (255 schema functions; column lists of every table incl. the server-only ones: ability
  definition FUN_143bef660, NPC/creature stats FUN_143bf3360, spawn entity lists FUN_143c5fa30, profiles
  FUN_143b23de0, resource triggers FUN_143adb7c0, item mods FUN_143c05010.., projectiles FUN_143b79d70, continent
  settings FUN_143c21900).
- 2026-10-12: datatables.py → datatables.md: 427 client tables, 269 with data, server reads 63, **206 with data not
  read** (incl. AttributeEntryDefinitions 1736, AbilityParameterDefinitions 746, DesignBasedState* state machines,
  ProjectileDefinitions, TriggerFXMap/TriggerFxTag, Footstep, TagDefinitions, voxel/biome tables, CharacterCreate*).
  Each gets a line in the matching system file (what it is, who loads it, server need).
- 2026-10-12: managers.md: 33 definition loaders (log strings) + 425 manager/handler classes (RTTI) = the inventory
  of client subsystems; every one must end up described in a system file (systems/INDEX.md maps them).
  Family files regenerated: Login / Gateway protocols separated, families named from their classes, S2C evidence
  taken from the dispatcher's own switch case (no more shared dispatcher strings). systems/claims.md written.
- 2026-10-12: trace.py: family files embed the client code (handler switch case for S2C incl. the 30 messages read in
  Zone_DispatchPacket; builder lines for C2S); zone opcodes fixed from dispatch_map; buildindex call graph now
  includes calls to renamed functions (373,599 edges). systems/session.md written.
- 2026-10-12: re/tools/ctorname.py names packet constructors Ghidra never defined (vtable lea → rtti) —
  playerupdate_subs.md: full PlayerUpdate 0x26 sub map (102 subs, ~85 named: composite effects 0xB/0xC/0x56,
  TriggerEffectPackage 0x19, effect tags 0x1D-0x1F, Knockback 0x2, LootEvent 0x15, Destroyed 0x2D, SeekTarget 0x2E/0x2F,
  rails 0x2A-0x2C, UpdateStat 0x53, AnimationRequest 0x54, AFK 0x57, name change 0x5F/0x60, combat notification 0x78,
  prop text/tint/name/edit 0x79-0x7D ...). composite_effects.tsv: 1610 effects from ActorCompositeEffectDefinitions.xml.
  systems/abilities-combat.md started.
- 2026-10-12: subdispatch.py finished (165 zone cases, 1552 rows) + subnames.py (ctor-vtable names) →
  re/out/messages.tsv (1345 dispatcher messages, 475 named; PlayerUpdate's 102 subs added). trace.py now: opcodes /
  subs from the dispatchers (115 messages the catalogue never had), server status from runtime opcode_stats.tsv
  (SENT n× / not sent) instead of the static heuristic. 145 server classes still have no opcode (not reached by any
  decoded dispatcher) → to resolve per system. Item field profile → systems/items.md (u17 = ambient weapon effect).
- 2026-10-12: FAMILIES-SUMMARY.md (every message, ✔/✗; totals C→S 204/548 handled, S→C 186/913 sent incl. engine /
  admin leftovers). Map-level system files written: abilities-combat, entities-movement, commands, social, building,
  character, activities, world-travel, crafting-meta, items, session, claims (+ commerce in docs/commerce.md).
  NEXT: deep pass per system (read handlers / builders for every ✗ that 0.589 can reach; settle '(check)' statuses
  against the server code), UI reachability per message, client rules/formulas, the 145 no-opcode classes.
- 2026-10-12: manual_opcodes.tsv + placeclasses.py: Resources 0xC4 subs 1-3, crafting inline subs (Start 0x2, Cancel 0x4,
  Complete 0x5, RemoveRecipe 0x9, UpgradeStat 0xF, RefiningOutput 0x15) and 39 classes placed by call graph; 100 classes
  left without opcode = non-packets (UdpLibrary, asset requests, Steam callbacks, Uram web store API).
- 2026-10-12: client-rules.md filtered to game keys (rules.py) and superseded by **code-strings.md**
  (re/tools/codestrings.py): all 2581 client message keys from Resources/CodeStringMappings.txt with English text and
  the client functions using them (565 referenced). **Wiki dumped**: re/tools/wikiscrape.py → design/wiki/pages (2340
  pages, wikitext) + index.tsv (categories). **0.589 exe is dated 2016-06-04 = the launch build**, so the wiki's
  launch-era (2016) descriptions apply directly. Claims deep pass: placement flow, client pre-check, result codes →
  texts, live rules (slots, buffer, delete, Active Days upkeep, permission levels); **flag bug explained** (refusal
  result 0 → generic error → client re-enters placement each time) — see systems/claims.md.
  NEXT: per system, wiki pages + deep client pass: building/props/designs → gathering → crafting → combat → inventory
  → travel → journal/goals → social → story tools → commerce/UI.
- 2026-10-12: **client-constants.md** (re/tools/constants.py): all 687 StringHashToValue constants with the client
  functions reading them (352 referenced) — key finds: build site is **210 × 180 × 210 m** (server has 70 × 100 × 70),
  buffer to non-friendly sites 285 (clamped 100-400), Lumens = item 1579 (revive 15, warp cost 0, vendor
  currency), inventory 40 / 100, vault 60 / 120, mail 30 days, chests 50 / account (wiki), prop limit 20000 / site,
  design storage 6M / 48M. Constants sections added to every system file. **Wiki pass done for all main systems**
  ("Live behaviour" sections: claims, building, gathering (new systems/gathering.md), crafting / journal / showcase /
  player studio, combat, items, travel, social, character, story tools) + trace/wiki-goals.md (184 Journal goals
  with requirements + rewards; re/tools/wikigoals.py). re/tools/wiki.py prints any page as plain text.
  trace.py status fix (enum literal forms) + re/tools/famsummary.py (FAMILIES-SUMMARY regenerable).
- 2026-10-12: **Rebuild loop started (approved approach).** Claims round (size 210 m, client buffer rule, refusal
  codes, flag-loop fix, basic info + record parent field, delete reply, Active Days upkeep + letters, Starting
  Location, default Play Around, access levels for use / move / pick-up, prop placement rules) — systems/claims.md
  "Rebuild status". Combat round 1: Energy cost in weapon caster info (right-click report resolved — client regen
  measured), hit impact effects (PlayCompositeEffect decoded + verified). Tools: re/testcopy probe_claimcheck*,
  hook_claimbasic / claimtext / proptext / resources / castcheck / composite, probe_power (all read-only or
  game-thread calls); tools/run_frida_pid2.py <script> <log> <pid>. subnames.py now fixes per-dispatcher sub offsets
  (0xF0 +2, 0xF9 +1, 0x10D +2, 0x111 +1). EffectStart (0x27 sub 0xC) type hash CHANGESTAT (0x91126390) = floating
  combat text; impact composite set listed in HitEffects.cs.

### Done: Cave Seeker visuals (2026-10-12) — see docs/protocol.md §7 EffectStart; Buffs.StartClientEffect
Live drew the outline of nearby caves client-side: ability effect type DetectCave (hash 0x6eb699a5; DetectMineral
-0x752abf3e) reaching the local character's effect-added handler FUN_1432777f0 → CaveDetectionManager
(DAT_145351ee0, FUN_14307de30; ticks in FUN_142c3ce60) with the effect's id (+0x18), params +0xa4 / +0xa8, duration
(+0x68 float) and start (+0x40). EffectInitialized (0x27 sub 0x1C, FUN_142d7b4f0) builds that struct with +0xa4 /
+0xa8 / +0x68 = 0, so the parameters must come from the EffectStart path (FUN_142d7b690) — trace which packet fields
land there before switching the server's chat report (Consumables.Report) to the client effect.

### Done: uimap name collisions (2026-10-12) — luabind.tsv namespace column, uimap matches it
re/tools/uimap.py maps a Lua call to the FIRST native binding of that name (luabind.tsv has no namespace), so
Claims.TakeScreenshot / ClearScreenshots / MakePrimaryScreenshot showed as "client only" (they send ModifyScreenshots)
and Templates.SetName as an AreaDefinition call. Give luabind.tsv the namespace (the binding table each entry sits in)
and match on it.

