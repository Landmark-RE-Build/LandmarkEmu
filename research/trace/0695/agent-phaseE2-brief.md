# Phase E, part 2 — shared brief for every agent (2026-10-26)

The user asked: finish the Phase E audit, deep-trace everything on the 0.695 client, and close **every** open lead,
including new ones found on the way. This file is the common brief; each agent also gets its own work list.

## The project in one paragraph

LandmarkEmu (<workspace>\LandmarkEmu, git, branch `client-0.695`) is a C# server emulator for the Landmark 0.695
client (the final build, Feb 2017). The workspace <workspace> holds the research: trace/ (system traces),
trace/0695/ (0.695 work: OPEN-LEADS.md, TASKS.md, FEATURES.md, coverage-695.md, functions.tsv, many agent-*.md
reports), design/ (wiki dump design/wiki, 2,340 pages; forum threads design/forum/all + patches; game-rules.md),
gameData695/ (the 0.695 data tables the server reads), re/ (decompiles and tools). Read BUILD-PATH-0695.md §2 (ground
rules) before starting.

## Sources and tools (use these, in this order: existing research first)

- **0.695 decompile:** re/decomp_all_695 (part_*.c + index.tsv). Lookups: `DECOMP=decomp_all_695 python re/tools/fn.py
  <addr|FUN_name>` (print), `fn.py -x <addr>` (callers), `fn.py -g <regex>` (search). Plain grep over
  re/decomp_all_695/*.c is fast. The 0.589 decompile is re/decomp_all (fn.py without DECOMP).
- **0.589 → 0.695 function map:** trace/0695/functions.tsv (addr_695, name_695, size, status, addr_589, name_589,
  strings, code). Normalised comparison helpers (addresses / names / variables folded): re/out/agent_coverage_e/rcmp.py
  (`body(d, a)`, `norm`, `norm_wire`, `closure`, map `M`).
- **0.695 packet tables:** trace/0695/coverage-695.md, re/out/coverage_695.md, re/out/agent_packets/catalog_695.md
  (readers / writers / layouts), re/out/packet_ids_695.tsv, re/out/luabind_695.tsv (Lua natives → C++ → packets),
  re/out/ui_server_map_695.md, trace/families_695/*.md (one file per packet family, generated today from 0.695),
  trace/REACHABLE-GAPS-695.md.
- **0.695 UI:** Lua scripts in `<0.695 install>\Resources\Scripts` and `UI\Default`, Flash ActionScript in
  re/as3_695. **The 0.695 install is read-only: never write anything in <0.695 install> or the Steam install.**
- **Design sources:** design/wiki/pages/*.wiki, design/forum/all/*.md, design/forum/patches/*.md,
  design/patchnotes-after-0589.md, design/game-rules.md.
- **Data:** gameData695/*.txt (0.695 tables), re/out/fileindex_695.tsv (every file and pack entry), re/tools/pack.py /
  packfind.py (read pack entries; point them at the 0.695 install read-only).
- **Server code:** <workspace>\LandmarkEmu (read freely). Docs for the server: LandmarkEmu/docs/*.md.

## Rules

1. **Trace the client before concluding.** Every claim cites the 0.695 function address (FUN_14xxxxxxx), data row,
   script line or design source. Mark each claim [P] (proven from client / data / source) or [I] (inferred).
2. **Nothing is written off.** "Not found" is never a conclusion on its own: list every path searched.
3. **Closing a lead.** A lead is closed when one of these holds:
   - **Resolved:** the answer is proven [P] from the client, its data, or a developer post / patch note / wiki page,
     and the server matches it (or the code change that makes it match is done, see 6).
   - **Closed on best evidence:** the missing piece existed only on live's servers (server-only data or rules no
     client code, data table, script, wiki page, forum post or patch note records), proven by listing every source
     searched; the server's behaviour is then set to the best-evidenced choice, documented with that evidence, and —
     if it is a gameplay choice — a server setting defaulting to the live-like behaviour (user rule: gameplay options
     default to live behaviour and are always togglable by server owners).
   Never close as "unknown". If a lead truly needs a runtime capture on the client, say exactly what to capture (which
   function to hook, which action to take) under "Runtime capture needed" — the coordinator runs captures; do NOT
   start game clients, Frida, or touch the user's running game yourself.
4. **New leads.** Anything new you find that you cannot close yourself: write it as a new lead in your report (same
   format as OPEN-LEADS.md: Need / Searched / Try next). Do not edit trace/0695/OPEN-LEADS.md yourself — the
   coordinator merges every report into it.
5. **Do not** run, stop or restart any server or Docker container, push anything, contact anyone, or write outside
   your report file, your own worktree, and new files you create under trace/0695/ or re/out/.
6. **Code changes** (only when a lead's resolution needs the server to change): make them in your own git worktree:
   `git -C "<workspace>/LandmarkEmu" worktree add "<workspace>/wt-<yourname>" -b local/<yourname> client-0.695`.
   Match the surrounding code style (detailed /// comments citing client addresses, as the rest of the codebase).
   Add or update tests (LandmarkEmulator.Tests) and run `dotnet test LandmarkEmulator.sln` in your worktree: 0 failed,
   0 skipped. Commit on your branch with a clear message ending with
   `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Do not merge into client-0.695; the coordinator merges.
   Keep server-facing docs (LandmarkEmu/docs) in step when behaviour changes. If a change adds a setting, wire it
   like the others (WorldServerConfiguration, ServerSettings.Table, docker/world/docker-compose.yml, .env.example,
   WorldServer.example.json, docs/docker.md) and run `python tools/wiki_reference.py` in the worktree.
7. **PC resources are limited** (RAM, C: disk): don't copy large files, keep scratch files small, delete temporary
   files you made. Long Python passes over the decompile are fine.

## Report format (write to the report file named in your work list)

For each item:
```
### L<n>. <title>
- **Verdict:** RESOLVED | CLOSED (best evidence) | NEEDS RUNTIME CAPTURE
- **Answer:** what is true, with [P]/[I] and citations.
- **Searched:** every path checked (for best-evidence closures).
- **Server:** matches already | changed in local/<branch> commit <sha> (what) | setting <Name> (default, why).
- **OPEN-LEADS text:** the exact lines to put at the top of the lead (e.g. "- **CLOSED 2026-10-26 (…):** …").
```
Then a section **New leads** (each with Need / Searched / Try next) and **Runtime captures needed** (if any).
End with a one-paragraph summary.
