# Phase E, part 3: what the 0.589 → 0.695 traces may still have missed (shared brief, 2026-10-26)

The user asked: "Can you find anything else that we may have missed with the 0.589 to 0.695 traces". This is a
**read-only hunt**. Each agent gets its own slice (its prompt) and writes one report.

## Ground rules (stricter than part 2)

1. **Read-only.** Do NOT edit any server code, docs, tests, OPEN-LEADS.md or any existing file. Do NOT create git
   branches, worktrees or commits. Do NOT run, stop or restart servers, Docker, game clients or Frida. No web outreach
   to people; reading public web pages is fine. The only files you may write: your report
   `trace/0695/phaseE3-<name>.md`, and scratch files under **your own folder**
   `<user folder>\AppData\Local\Temp\claude\<workspace>\28d489f2-5945-4c82-ba0b-9796b72f5283\scratchpad\e3-<name>\`
   (create it). **Never delete with wildcards; never delete anything outside your own folder.** (A part-2 agent wiped
   the shared scratchpad with a wildcard delete.)
2. **The 0.695 install (<0.695 install>) and the Steam install are read-only.**
3. **Check existing research first** before tracing (trace/0695/*.md: CLIENT-DIFF.md, functions-695.md,
   packets-695.md, coverage-695.md, famdiff-695.md, rebase-695.md, phaseE2-*.md, audit-wire-*.md, OPEN-LEADS.md;
   trace/systems/*.md; LandmarkEmu/docs/*.md). Only report what is NOT already recorded as found, built or closed — if
   you find an existing lead that covers it, cite the lead id instead of re-reporting.
4. **Every claim cites evidence:** the 0.695 function (FUN_14xxxxxxx), the 0.589 partner where relevant, the server
   file:line, data row, script line or design source. Mark [P] proven / [I] inferred.

## Sources and tools

- Server code: `<workspace>\LandmarkEmu`, branch client-0.695 (HEAD 8219f7c). Read with grep / git (read-only git
  commands such as `git log`, `git show`, `git diff` are fine). The last full wire audit was 2026-10-17
  (trace/0695/audit-wire-*.md); `git rev-list -1 --before=2026-10-17T18:00 client-0.695` is its base.
- 0.695 decompile `re/decomp_all_695` (part_*.c, index.tsv); 0.589 decompile `re/decomp_all`.
  `DECOMP=decomp_all_695 python re/tools/fn.py <addr>` (print), `fn.py -x <addr>` (callers), `fn.py -g <regex>`;
  `DECOMP=decomp_all_695 python re/tools/pkt.py <reader>` prints a reader's wire layout with offsets.
- Function map `trace/0695/functions.tsv` (addr_695, name_695, size, status, addr_589, name_589, strings, code);
  address map `trace/0695/address-map-589-695.md`; normalised comparison helpers `re/out/agent_coverage_e/rcmp.py`
  (`body`, `norm`, `norm_wire`, `closure`, map `M`); rebase tool `re/tools/rebase695.py` and outputs re/out/rebase695*.
- Packet tables: re/out/coverage_695.md, re/out/agent_packets/catalog_695.md, re/out/packet_ids_695.tsv,
  re/out/luabind_695.tsv, re/out/ui_server_map_695.md, trace/families_695/*.md, trace/REACHABLE-GAPS-695.md.
- UI: `<0.695 install>\Resources\Scripts`, `UI\Default` (Lua), re/as3_695 (the 29 changed SWFs) over re/as3.
- Text: re/out/text_589.tsv, re/out/text_695.tsv, trace/0695/text-new.tsv. Data: gameData695/, trace/0695/tables-*.md.
- Design sources: design/wiki/pages, design/forum, design/patchnotes-after-0589.md.

## Report format (`trace/0695/phaseE3-<name>.md`)

1. **Findings**, ranked by player impact. Each: title; verdict MISMATCH (server and client disagree) / GAP (client
   expects something the server never does) / STALE (docs or research say something no longer true) / OK-NOTE (worth
   knowing, no change); evidence with [P]/[I]; the server file:line; the exact fix you would make (described, not
   applied); whether it needs an in-game check.
2. **Checked and fine**: one line per item checked, so the coordinator can see the coverage.
3. **New leads** (Need / Searched / Try next) for anything you could not settle.
End with a short summary paragraph. Your final message: the findings list (title + verdict + one line each).
