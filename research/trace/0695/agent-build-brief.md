# Build agents — shared brief (2026-10-17)

The coordinator (main session) runs several build agents in parallel, each in its own git worktree of the LandmarkEmu
repo (branch `local/<topic>`), and merges their commits into `client-0.695`.

## Rules for every build agent

- Work ONLY in your worktree `<workspace>\wt-<topic>`. Never touch `<workspace>\LandmarkEmu` (the main
  checkout) or another agent's worktree. Never push.
- Never start / stop / attach to the game client, Frida, Docker containers or servers; no mouse / keyboard; no
  downloads; no web; never contact anyone or suggest it. The coordinator runs all in-game tests.
- Read `<workspace>\BUILD-PATH-0695.md` (ground rules) first. Read the client's handling before coding
  (`re/decomp_all_695` grep first: `re/tools/fn.py` with `DECOMP=decomp_all_695`, `pkt.py`, `catalog2.py`), plus the
  existing traces named in your task (trace/0695/*.md, trace/architecture/*.md, LandmarkEmu/docs/). Never guess a
  packet layout: prove it from the client ([P]) or mark the inference ([I]) with the reason.
- **Packet numbering (since 003dc1f, 2026-10-17):** the server speaks 0.695 natively — every sub number in the code is
  the real 0.695 number; PlayerUpdate 0x26 subs are u32 on the wire (`WorldSession.GetOpcodeBytes`; a 4-byte enum value
  0x2600ss00 or its "Wire" form 0x2600ss). There is no translator. Check every packet family you use against the 0.695
  trace first (trace/0695/packets-695.md, coverage-695.md, audit-wire-*.md).
- **Defaults (user 2026-10-18):** every gameplay rule defaults to how live did it, with a server-owner toggle.
- New behaviour beyond live goes behind a server setting that defaults to live behaviour; data-writing features get a
  setting (Docker env var in `docker/world/docker-compose.yml` + `.env.example`). Mind bandwidth (coalesce, interest
  ranges, no per-tick resends).
- Comment in the existing detailed style with sources; update `docs/` where behaviour changes. MODLOG.md,
  TESTING-NEEDED.md, OPEN-LEADS.md, FEATURES.md are outside the repo — leave them to the coordinator, but put what
  they need into your final message.
- `dotnet build` the solution (`LandmarkEmulator.sln`): 0 errors, no new warnings in files you touch. Commit on your
  branch; messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Final message: commit hash(es), files changed, evidence per change ([P]/[I]), anything skipped and why, settings
  added, and exact in-game test steps for the coordinator (dev commands are lines appended to the world's
  `dev_commands.txt`, e.g. `@TestPlayer <command>`; list the commands you add).
