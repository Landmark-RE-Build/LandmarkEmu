# Phase E audit — everything decompiled, checked against the server (BUILD-PATH-0695.md §4 Phase E)

Started 2026-10-17, when trace/0695/TASKS.md had only user decisions (3.12, 6.59, 6.60), the conditional B.6 and the
running 6.61 left. For each area: what was checked, how, the result, and what it added to TASKS / the code.

## 1. Packets (every family and sub-opcode)

**How.** The 0.589 pipeline re-run on today's source: `re/tools/coverage.py` → `trace.py` (trace/families/*.md,
185 families, 1,461 classes) → `reachable.py` (trace/REACHABLE-GAPS.md): every client→server message the server doesn't
handle that a Lua native / UI can send, and every server→client message never sent whose client handler drives UI,
Lua events, data sources or texts. Each hit read by hand; verdicts in re/tools/verdicts.tsv (269 now). The 0.695
delta from trace/0695/coverage-695.md (agent #10's per-row table, re-run in re/out/agent_coverage_e/ — its static
server scan only reads LandmarkEmulator.WorldServer, so it under-counts since the social / commerce / environment
services moved out; used as a checklist, not a verdict).

**Result.**
- Reachable gaps: 3 client→server + 12 server→client found; **1 real**: GuildInviteRevoke 0x83/6 (only
  "/guild_debug revoke invite <id>" and the unused Lua Guild.InviteRevoke send it). Built in 9fbb106 together with the
  client's invitation states (FUN_142e56100: 1 revoked / 2 closed / 3 declined, texts 9405 / 9415 / 9407) and the
  invitee-id field the client parses (decimal guid). The other 14 are sent / handled through local Send(sub) helpers
  or other services (chat rooms, trades, point of need, wallet, product review, screenshots, EditClaimItemData,
  InstancedGame 4 / 7, AddBindingResponse). After the verdicts: **0 / 0**.
- The 19 messages new in 0.695 (coverage-695 "new"): all have server code now (bio, template product create,
  inspect, prop dialog response, template materials purchase / data, entered claim, adventure flags, start claim
  placement, WorldEditingPlayEffect, the game rules / configuration / matchmaking / join team rework, equipped
  attachment).
- The 0.695 renumbered / header / body-changed rows: handled per family in Phase C (TASKS §0 / §1 / §4; coverage-695
  §5); nothing new found.

## 2. Data tables (0.695)

re/tools/datatables.py on gameData695 → trace/0695/datatables-695.md: 431 tables, 274 with data, the server reads 87,
188 with data not read; 121 of those were described in no system file. First finding: **placement packs and movers**
(PlacementPacks / PlacementPackProps have no reader on either side; the movers run on GlobalAreas / AreaProperties /
GlobalAreaShapes static areas named in the prop spawn) — traced in trace/0695/movers-695.md, built in 22dd72f (TASKS
E.1). VoxelResourceGeology has no client loader (the server's veins use ResourceVeins.txt, same fields);
VoxelMiningResources feeds the client's terrain generator. The other unread tables: **classified 2026-10-17**
(trace/0695/tables-unread-695.md, agent reports agent-l138-a / c1 / c2). Results: the server-only tables are superseded or
test data; **ProjectileDefinitions was live** — the Projectiles family 0xEE is now built and client-tested (Game/Projectiles.cs;
the Abomination's spit flies as a real projectile); chest armour now equips into the Armor slot instead of the appearance
slot (found on the way); the rest client-only / not live on 0.695 with evidence; open parts are leads L156-L162.

## 3. Re-proving "unreachable" (TASKS E.2)

The user's point (2026-10-17): EverQuest Next and PlanetSide 2 code was reused by Landmark, so origin is no evidence.
The movers proved it (area system). Rule now: unreachable needs proof on 0.695 (UI, C++ paths, data, design sources);
otherwise an open lead. Results so far in TASKS E.2; reopened: map waypoints (L136), Showcase store natives (L137).
New tool: re/out/ui_server_map_695.md (uimap.py run on the 0.695 scripts / Flash / bindings).

## 4. Lua bindings, managers, new 0.695 functions

- **Lua bindings / UI:** re/out/ui_server_map_695.md (uimap.py over the 0.695 scripts, Flash and luabind_695.tsv): 11
  "GAPS" rows, all built in Phase C (the map's status column is the old 0.589 catalogue); no other UI call reaches an
  unhandled message (TASKS E.2 / E.3, reachable 0 / 0, 478 verdicts).
- **Managers:** 4 manager / handler classes are new in 0.695's RTTI; 3 are described in trace/0695, the 4th
  (PropHelpWindowDefinitionManager: PropHelpWindow.txt + PropHelpWindowMapping.txt, prop 2899 → ScriptObj_Damage_FTE)
  is used by the server already (FirstTimeEvents, RightClickMenus).
- **New 0.695 functions:** functions-695.md's breaks B1-B9 were the Phase C work list (TASKS §0 / §1 / §4); all ticked.
- **Runtime:** the only request any play session left unanswered is Claims 0xF9 sub 0xE3 (dev world, 6×) — no client
  builder sends it; the world now logs its body once (E.3).

## 5. Client → server messages without a Lua sender (TASKS E.3)

209 classes (scratchpad c2s_unhandled.tsv) triaged with the C++ caller walk and the E.2 rule: verdicts added
(handled / covered / informational / unreachable with evidence / parked); parked ones are OPEN-LEADS L138-L140.
