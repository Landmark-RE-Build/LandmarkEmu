# Open leads, third pass — shared brief (2026-10-17)

The coordinator (main session) runs three read-only research agents (groups A, B, C) and one build agent (Barren
props) in parallel. Each stays strictly inside its own leads.

## Ground rules (all agents)

- Project: LandmarkEmu, a C# server emulator for the Landmark 0.695 client (repo `<workspace>\LandmarkEmu`,
  branch client-0.695). Read `<workspace>\BUILD-PATH-0695.md` first (ground rules).
- **Read-only** for the research agents: write ONLY your own output file `trace/0695/agent-leads3-<group>.md`. Do not
  edit OPEN-LEADS.md, TASKS.md, FEATURES.md, code, or git. The coordinator merges.
- **Never** start / stop / attach to the game client, Frida, Docker containers or servers; never use mouse / keyboard
  automation; never download anything; no web access except reading local archives already on disk; never contact
  anyone or suggest it. The single game client is in use by the coordinator.
- Never call a thing "lost" or "unknown for good": if not found, say exactly what was searched and what to try next.
- Mark every claim **[P]** proven (function address / file:line / table row / archive page) or **[I]** inferred
  (with the reason).
- Leads already had two passes (sweep + pass 2). Read each lead's whole entry in `trace/0695/OPEN-LEADS.md` and its
  pass-2 file (`trace/0695/agent-leads2-*.md`, `agent-leads-sweep.md`), then ONLY try paths not yet tried. Don't
  repeat a search a previous pass already recorded.

## Sources and fast tools

- 0.695 decompile: `<workspace>\re\decomp_all_695\` (one file per function + index.tsv); 0.589: `re/decomp_all/`.
  grep these first (`re/tools/fn.py`, `pkt.py`, `catalog2.py`, `fieldreaders.py`, `refscan.py`, `luabind.py`,
  `codestrings.py`). Ghidra MCP only if grep can't answer.
- Client data: `<workspace>\gameData695\` (0.695 tables), UI as3: `re/as3_695`, Lua: testclient sources, `re/out`.
- Design archive: `<workspace>\design\` (wiki dump `design/wiki`, forum scrape `design/forum` and `re/forum`,
  `design/game-rules.md`, `design/patchnotes-after-0589.md`, `trace/0695/patchnotes.tsv`).
- Existing traces: `trace/` (systems, families, architecture), `LandmarkEmu/docs/`, `MODLOG.md`.
- Server code: `<workspace>\LandmarkEmu\LandmarkEmulator.WorldServer\` etc.

## Output (each research agent)

`trace/0695/agent-leads3-<group>.md`:
1. Summary table: Lead | Verdict (FOUND / ADVANCED / UNCHANGED / NEEDS-RUNTIME / CLOSE-BY-DESIGN) | one line.
2. Per lead: what was new this pass, with [P]/[I] and sources; paths tried with no result.
3. **Server work**: for each lead where the evidence now supports a server change, a concrete change list (file,
   function, packet field / value, the evidence). This is the most valuable part — favour leads that can turn into
   server behaviour now.
4. **Runtime tests wanted**: exact Frida hook / in-game check the coordinator could run (function address, what to log).
5. Hand-offs: anything belonging to another group's leads (one line each).

Final message to the coordinator: the summary table plus the server-work list (short).
