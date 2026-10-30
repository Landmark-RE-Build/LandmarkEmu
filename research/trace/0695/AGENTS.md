# Agent work register

Which helper agent owns which topic, so no two agents work on the same thing. Every agent reads this first and stays
strictly inside its own topic; when it finds something that belongs to another topic it writes a one-line note in its
own output file under "Hand-offs" instead of tracing it. The coordinator (main session) merges results into
TASKS.md / FEATURES.md / OPEN-LEADS.md and starts the next queued topic when a slot frees up (max 4 at once).

**Before launching an agent** the coordinator searches the existing research for its topic (trace/systems/,
trace/families/, trace/*.md, LandmarkEmu/docs/, MODLOG.md, re/out, testclient Lua sources) and names those files in
the brief; the agent builds on them, cites them in an "Existing research used" section and only traces what is
missing or wrong. (Added 2026-10-15 after the Phase B agents were briefed without the full list; they were sent it.)

| # | Topic | Status | Output | Owns | Does NOT touch |
|---|---|---|---|---|---|
| 1 | Pack / file index; open leads L1, L2 (procedural masks, entity-list tables) | done | trace/0695/agent-L1-L2.md, re/tools/fileindex.py | file indexes, procedural placement data | — |
| 2 | 0.695 terrain data (.otd / .ocm / .csc, continent layers, VoxelShapes / Mesh Shapes) | done | trace/0695/terrain-695.md | terrain files, voxel tables, mesh shape data | packets other than terrain / mesh |
| 3 | Every new 0.695 packet class + changed layouts of existing packets | done | trace/0695/packets-695.md | packet layouts, opcodes, handlers, replies | non-packet logic |
| 4 | Launch-era patch-note rules vs the server (TASKS §6) | done | trace/0695/launch-rules-audit.md | rule checks against server code | the client decompile |
| 5 | New / changed 0.695 functions grouped by system (non-packet, non-terrain logic) | done | trace/0695/functions-695.md | functions.tsv `code` = new / changed | packet layouts (#3), terrain / mesh (#2), story-tool specs (#6), item definitions (#7) |
| 6 | Story tools in 0.695: per node links, text ids, settings, prop ids, server spec (TASKS §2) | done | trace/0695/storytools-695.md | story-tool props and Links | packet layouts (#3) |
| 7 | Item definitions for 0.695-only items (TASKS §4) + ItemDefinitionRequest | done | trace/0695/items-695.md | item data, models, icons, item definition packets | other packets (#3) |
| 8 | Architecture leads L3-L8 + ProceduralPlacementTool/CaveMeshes | done | trace/0695/architecture-leads.md | OPEN-LEADS L3-L8, cave meshes | procedural masks / tables (#1 done) |
| 9 | Wiki content obtainability + goals vs server (TASKS §5) | done | trace/0695/wiki-content-audit.md | wiki-xref content pages, goals | launch rules (#4) |
| 10 | 0.695 packet coverage audit (all families × server handling) | done | trace/0695/coverage-695.md | coverage tables | per-packet layouts (#3: read its output instead) |
| 11 | 0.695 UI windows (new / changed .swf): data sources, Lua / ExternalInterface calls, what the server must feed | done | trace/0695/ui-trace-695.md | re/as3_695 vs re/as3, ui-diff.md, luabind_695 | packet layouts (#3/#10: read their files), story-tool spec (#6), items (#7), wiki (#9) |

## Phase B (architecture) — specs before building, one service per agent

All read trace/architecture/internal-transport.md (the shared contract) and write only their own spec.

| # | Topic | Status | Output | Owns | Does NOT touch |
|---|---|---|---|---|---|
| 12 | Shared social service: friends, ignore, guilds, chat (General / tells / guild / rooms), mail, presence across worlds | done | trace/architecture/social-service.md | client evidence + emulator Friends / Ignores / Guilds / Chat / ChatRooms / Mail | gateway internals (#13), commerce / ratings / lobbies (#15), terrain (#14) |
| 13 | Gateway as its own service: external + internal packets, channel routing, routability, connection survival across back-end restarts, gateway commands (opcode 9), profiler | done | trace/architecture/gateway-service.md | LandmarkEmulator.Gateway, WorldSession tunnel code, login handover | what each back end does with its messages |
| 14 | Terrain / voxel service: layers (base, player, destruction + heal queue), cell requests, edits, channel-3 previews, EnvOwnership, who calls it | done | trace/architecture/terrain-service.md | Terrain/*, TerrainEdits, WorldEditing, VoxelConstruction | — (owns the Environment Server: its jobs are voxel work) |
| 15 | World-channel service: Marketplace / commerce / Player Studio, ratings / contests, portraits, locale, trade, lobbies / encounters, character-select session | done | trace/architecture/world-channel-service.md | Commerce, Ratings, Trades, Encounters, portraits | social (#12) |
| 16 | Operations (scheduled restarts with warnings, health checks, save-on-schedule) + server-side admin page data. Environment Server jobs moved to #14 (2026-10-15: FUN_143c74ef0 shows the EnvServer is the voxel job back end) | running (coordinator) | trace/architecture/operations.md | restarts, health, saves, admin page | Environment Server / terrain (#14) |
| 17 | Admin monitor pages: reply layouts for pages 28 / 29, every page's data → server-side web admin page spec | done | trace/architecture/admin-pages.md | monitor pages, admin page spec | the services' internals (#12-#15) |
| 18 | BUILD social step 1: ISocialService interface split inside the world server (no behaviour change) | done (merged 8e2d7d9) | git worktree <workspace>\wt-social, branch phaseb/social-split | Friends, Ignores, Guilds, Chat, ChatRooms, Mail + their call sites | commerce / trade / encounters (#19), transport library (coordinator) |
| 19 | BUILD world-channel step 1: interface split for trade broker / encounters / commerce / claim votes / relog / locale (no behaviour change) | done (merged 6d7dbbf) | git worktree <workspace>\wt-worldchannel, branch phaseb/worldchannel-split | Commerce, Trades, Encounters, Ratings, portraits, relog session | social (#18), transport library (coordinator) |
| 20 | BUILD Environment Server step 1: archive tag + terrain / voxel library split inside the world server (no behaviour change) | done (merged 7304e4a) | git worktree <workspace>\wt-envserver, branch phaseb/envserver-split | Game/Terrain/*, TerrainEdits, WorldEditing, Building, BuildingClipboard, Caves, OreVeins, Outposts ruin stamping | social (#18), world-channel (#19), transport (coordinator) |
| 21 | BUILD social steps 2-4 + presence / friends / ignore moved into LandmarkEmulator.SocialServer (SOCIAL_ADDRESS empty = local, unchanged) | done (merged 8a66bae) | git worktree <workspace>\wt-social2, branch phaseb/social-service | SocialServer, Database.Social, RemoteSocialService, Game/Social, Friends, Ignores | commerce / trade / encounters (#19), terrain (#20), gateway (coordinator) |
| 22 | Tunable content loader: host-editable tables under world `/data`, reload without world restart; extract existing C# loot / rate tables on touch | done (`feature/creature-loot-capture`) | `Game/ContentTables.cs`, `Resources/Content/loot/*`, docs/docker.md + docs/gameplay.md; first tables: cavern resources, creature kill loot / kill extras, cave treasure, Lost Chest, Lost Crystal | Content root + reload API; loot / rate JSON; migration of hardcoded tables when a system is touched | packet layouts, client `/gamedata` Resources/*.txt, Chaos Cavern geometry / outpost stamping (hand off) |

## Conventions — tunable resources (added 2026-10-28)

Server-authored numbers that operators (and agents) will retune — loot tables, drop weights, chances, amounts,
spawn densities, healer intervals, similar balance knobs — must not stay as only `const` / `static readonly` in C#.

**What counts.** Drop tables and rates (creature kill, cavern resources, chests when retuned, gathering chances,
kill extras like campfire / Lumens / recipe). Not packet layouts, opcodes, or client `Resources/*.txt` shipped under
`GameDataPath` / `/gamedata`.

**Where files live.**

- **Defaults in the repo** (shipped with the world image), e.g. under WorldServer content next to the program.
- **Live overrides on the host** under `server-data/worlds/<WORLD_KEY>/content/` → container `/data/content/` (same RW
  volume as `settings-overrides.json` and `dev_commands.txt`). Edit on the host without rebuilding the image.
- **Never** put server-authored tunables only under `/gamedata`: that tree is the client table copy, mounted read-only,
  and `setup` refreshes it from `GAME_DIR`.
- Precedence: baked / shipped default &lt; world override under `/data/content/`.

**Reload.** Every tunable loader must support reload without a world restart (dev command such as `loot reload` /
`content reload`; admin hook optional). A bad or missing override keeps the previous good table and logs; never
half-apply. Next roll uses the new table; in-flight bags may keep what they already rolled.

**Existing hardcoded tables.** When you **touch** a system that still embeds rates in C# (`Creatures.CavernResource` /
`CavernRareChance`, `Kind.Loot`, kill extras, `Gathering` chances, `RewardChests` / `LootChests` / outpost crystal
picks, …), **extract that table in the same change** onto the content path above. Do not add a second parallel C#
table. New Chaos Caverns / combat / gathering work must not introduce new `const double` drop rates.

**Evidence honesty.** Files keep `[P]` / `[I]` / "ours" provenance (header or `_meta`). Moving a guess into JSON does
not make it live data.

**Coordination.** Topic #22 owns the loader and shared table files. Agents calibrating loot (e.g.
`NetworkCaptures/_research/combat-loot-probe/PROMPT-loot-table-tuning.md`) or working Chaos Cavern gameplay edit
**content files + reload**, not `Creatures.cs` constants; if the loader is not landed yet, extract as part of the
same feature branch or hand off one line to #22. Quiver / creature weapons stay out of loot (see
`agent-creature-quiver.md`).
