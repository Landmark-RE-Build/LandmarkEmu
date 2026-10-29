# Prompt: Frida runtime probe — grow / bypass the 64-slot allocator table

Paste this whole file to an implementation agent that writes Frida scripts. Research only: **do not** patch
`Landmark64.exe` on disk, **do not** change LaunchPad / shipping code unless the user later asks. Goal is a
**test-copy** Frida experiment that proves whether we can fix (or cheaply mitigate) the 64-slot hang **in memory**.

**Read first (required):**

- `research/trace/0695/allocator-slots-options.md` — full options matrix; **P4** is the table move
- `research/trace/0695/allocator-slots-fix-design.md` — LaunchPad **P1** (CPU cap) design (not built); use as the
  simpler control experiment
- `docs/terrain.md` § "Slow loading screen: allocator slots"
- Existing Frida patterns: `tools/paste/hook_placeoffset.js` + `tools/paste/run_placeoffset.py` (attach, `rva()`,
  stamp check, detach-after-exit rule)

Marks below: **[P]** proven in those docs / decompile; **[I]** inferred.

---

## 1. The issue (hand this summary unchanged)

The 0.695 client's custom allocator has a **fixed 64-entry "fast lane" table** (per-thread cache slots).

| Fact | Detail |
|---|---|
| Table | VA **`0x145427450`**, **64 × `0x258`** bytes; owner thread id at the start of each slot [P] |
| Used / free counts | `0x145426dd8` / `0x145426de8` [P] |
| Global lock | critical section `0x145426420` [P] |
| TLS index for "my slot" | **`0x145430a50` — immediately after the table** [P] |
| Slot finder | `FUN_142c05050` [P] |
| Claim moment | first small alloc / free while TLS empty — **not** at thread create [P] |
| Cap check | `cmp rcx, 0x40` at **`0x142c050f3`** [P] |
| Full-table miss path | loop bound `cmp rdi, 0x9600` (= 64×`0x258`) at **`0x142c05145`**: for **every** slot-less alloc, scan all 64 owners with OpenThread / GetExitCodeThread / GetProcessIdOfThread / CloseHandle **under the shared lock** [P] |

**Why it hurts:** two pools (`Resource::Manager Decompression` `FUN_143a8d560`, `TaskManagerJob` `FUN_143a98090`) start **one worker per logical processor with no cap**. On 16+ thread CPUs the live allocating threads want **> 64** slots. Contour / preview threads that lose the race mesh at a crawl (`Avg contour` spam; 50 % load hang). Slots are reclaimed when owners die — this is **not** a leak; it is **too many live allocators** [P].

**Why the table cannot grow in place:** TLS index sits hard against the end of the 64-slot block. Raising 64 → N requires a **new** buffer and retargeting every code reference [P].

**Server already mitigates order** (`STREAM_HOLD_UNTIL_LOADED`) so contour work starts before the asset burst; it cannot add slots. Client-side options are still needed on big CPUs.

---

## 2. The "move the table" plan (P4) — what to explore

From `allocator-slots-options.md` § P4:

1. Allocate a **new zeroed** table of **127 × `0x258` = `0x129A8`** bytes within **±2 GB** of `.text` (RIP-relative `disp32`). Suggested VA just past the image: image `SizeOfImage` `0x61AF000` → e.g. **`0x1461B0000`** region via `VirtualAlloc` / Frida `Memory.alloc` (must still be RIP-reachable from the LEA sites) [P/I].
2. Before the first game allocator claim (ideally **process still suspended / before CRT + TLS callbacks allocate**):
   - rewrite **disp32** of all **8** RIP-relative LEAs that point at the old table (list below);
   - change capacity imm8 **`0x40` → `0x7F`** at **`0x142c050f6`** (127 is the max that fits an imm8 `cmp`; 128+ needs a code cave) [P];
   - change miss-scan bound imm32 at **`0x142c05148`**: `00 96 00 00` (64×`0x258`) → `A8 29 01 00` (127×`0x258`) [P].
3. Leave the old `.data` table untouched (dead); leave TLS index where it is (it is not inside the slot array).
4. Risk: **one missed LEA** → allocator / TLS corruption → early crash. Medium effort; must be early [P].

### Exact LEA sites that must retarget [P]

All in the allocator; full `.text` scan found **exactly these eight** (no 64-bit bitmask elsewhere):

| VA of instruction (LEA) | Note |
|---|---|
| `0x142c050c5` | |
| `0x142c05a19` | |
| `0x142c05c5c` | |
| `0x142c070b7` | |
| `0x142c0712e` | |
| `0x142c076e0` | |
| `0x142c088fb` | points at **table + `0x238`** — retarget to `newBase + 0x238` |
| `0x142c09112` | |

Plus the two immediates above (`0x142c050f6`, `0x142c05148`).

### Build / address rules [P]

- January 2017 `Landmark64.exe` (SHA-256 `8B0FC625…F4F9`): **no ASLR**, fixed VAs (`DllCharacteristics` DYNAMIC_BASE off). Frida still rebases with `Process.getModuleByName("Landmark64.exe").base` vs image base `0x140000000` — use the same `rva(abs)` helper as paste hooks.
- **Verify build before patching:** PE TimeDateStamp and/or known prologues. Wrong build → abort, do not write.
- **Never write the patched bytes to disk.** Memory only, test-client copy preferred.

---

## 3. Frida timing constraint (critical)

P4 docs: apply **while suspended, before TLS callbacks / CRT allocate**. Ordinary `frida.attach` to an already-in-world client is **too late** for a clean table move (slots already claimed against the old address).

**Required approach for P4:**

1. Obtain a **fresh session key** (§3a), then `frida.spawn` the exe with that key on the argv (process stays paused until `resume`).
2. `attach(pid)` → load script → allocate new table → patch LEAs + immediates → verify reads back → **then** `device.resume(pid)`.
3. If spawn-with-suspend is awkward on this machine, document failure and fall back to **P1 / P3** experiments (below), which are still valuable.

Do **not** attempt mid-session "move while used_count > 0" unless you have an explicit migration plan (copy slot contents + rewrite every TLS value that cached a slot pointer). That is out of scope for the first probe.

### 3a. Auth token = `SessionId` (how LaunchPad / LandmarkLocal PLAY works) [P]

The client does **not** take a username/password on the command line. PLAY always:

1. SRP-logs in to the account HTTP API (`POST /srp/start` + `/srp/finish`) → sealed session key → client opens it with `H(S|"session")` XOR (`SrpClient.OpenSessionKey`).
2. Writes `ClientConfig.ini` (`Server=<host>:<loginPort>`, marketplace URL, DirectThreadCount).
3. Starts:
   ```
   Landmark64.exe SessionId=<sessionKey> CasSessionId=0 STEAM_ENABLED=0
   ```
   Optional: `Internationalization:Locale=en_US` when locale is set.
   Working directory = game folder. Strip `SteamAppId` / `SteamGameId` from the environment.

Code truth:

- Args builder: `LandmarkEmulator.ClientLaunch/GameSession.cs`
- SRP HTTP: `LandmarkEmulator.ClientLaunch/ServerApi.cs` + `SrpClient.cs`
- Callers: LaunchPad `Bridge.cs` `"play"`; LandmarkLocal `LocalPlay.cs`

**Session keys expire** (auth DB `sessionKeyExpiration`). Always mint a **new** one for each spawn; do not reuse an old PLAY's argv.

**`ClientConfig.ini` must already point at your server** before spawn (same as PLAY's `ClientConfigFile.PointAt`). If the test copy still has Steam's Daybreak `Server=`, login UDP will fail even with a good `SessionId`. Either run LaunchPad/LandmarkLocal PLAY once first, or have the runner call the same PointAt logic.

### 3c. Revert the slot mitigation in the INI (needed to *see* the bug)

LaunchPad / LandmarkLocal PLAY always rewrite:

```ini
[AssetDelivery]
DirectThreadCount=2
```

Steam ships **`DirectThreadCount=10`** (code default if the line is missing: **4**). Each FileLoader takes one of the 64 allocator slots early and keeps it. **2 was the deliberate mitigation** (2026-10-14): frees 8 slots so contour threads usually win on ≤12-thread PCs (`ClientConfig.cs` `Threads.Recommended = "2"`; docs/terrain.md, docs/launchpad.md). LandmarkLocal passes `options: null` → always writes 2 (`LocalPlay.cs`).

**For Frida repro of the hang / P4 value, undo that mitigation** on the **test copy** only:

1. Set `[AssetDelivery] DirectThreadCount=10` (Steam's value), **or** use LaunchPad advanced mode → DirectThreadCount → **Leave the file as it is** after restoring 10 / copying from `ClientConfig.ini.orig` if that still has Steam's line.
2. The spawn runner's PointAt (when it writes Server=) must **not** silently re-apply recommended 2. Pass a `ClientConfigOptions` equivalent of Custom/`10`, or `Leave` after the file already has 10. Env knob e.g. `LMEMU_DIRECT_THREAD_COUNT=10` (default **10** for this research tool; never change LaunchPad's product default).
3. Optional second pressure (not an INI): Docker/world `STREAM_HOLD_UNTIL_LOADED=false` — server-side ordering workaround; turning it off makes the asset burst hit during the load screen again. Do this only when measuring "full" hang; leave on if you only need a full slot table.

Confirm before world entry: read the test copy's `ClientConfig.ini` and log the DirectThreadCount the runner will honor.

### 3b. Recommended runner shape (build under `tools/allocator-slots/`)

Prefer **reuse ClientLaunch** over re-implementing SRP in Python (one crypto path, matches LaunchPad).

**A. Tiny C# helper** (console, references `LandmarkEmulator.ClientLaunch`):

- Inputs: `--host 127.0.0.1 --api-port 5000 --account <name> --password <pw>`  
  **or** `--saved-key` / `--saved-salt` (LandmarkLocal `settings.json` fields `savedSrpKey` / `savedSrpSalt` after a successful Play; account = `adminAccountName`).  
  LandmarkLocal defaults: account API **5000**, login UDP **20042**, bind `127.0.0.1` (`LocalSettings.Defaults`).
- Output (stdout, one JSON line): `{ "sessionKey", "folder", "locale", "exe" }` then exit 0.
- Does **not** start the game (Frida owns spawn).

**B. Python `run_spawn.py`** (Frida, paste-venv style):

```python
# sketch — implement for real
import json, subprocess, frida, os, sys

meta = json.loads(subprocess.check_output([dotnet_helper, ...], text=True))
exe = meta["exe"]
folder = meta["folder"]
argv = [
    exe,
    f"SessionId={meta['sessionKey']}",
    "CasSessionId=0",
    "STEAM_ENABLED=0",
]
if meta.get("locale"):
    argv.append(f"Internationalization:Locale={meta['locale']}")

device = frida.get_local_device()
# Windows: spawn leaves the process suspended until resume()
pid = device.spawn(argv, cwd=folder, env={...})  # drop SteamAppId/SteamGameId if inherited
session = device.attach(pid)
script = session.create_script(open("hook_p4_move.js", encoding="utf-8").read())
script.load()
# hook_p4_move.js applies patches synchronously on load (process still frozen)
device.resume(pid)
```

**C. Env / CLI convenience for the Frida agent**

| Input | Source |
|---|---|
| `LMEMU_LANDMARK_FOLDER` | Test copy with `Landmark64.exe` |
| `LMEMU_ACCOUNT` / `LMEMU_PASSWORD` | Or read LandmarkLocal `settings.json` (remembered SRP key — **never commit**) |
| `LMEMU_API_HOST` / `LMEMU_API_PORT` | Default `127.0.0.1:5000` (LandmarkLocal) or LaunchPad server's account port |
| `LMEMU_SESSION_KEY` | Escape hatch: skip SRP if the user already minted a key (still expires) |

Also accept `--attach` for Experiments A/C (observe / P3) against an already-running client started by normal PLAY — **not** valid for P4.

**Do not** scrape `SessionId` from a running process's command line as the primary path: by then it is too late for P4, and the key may already be consumed/near expiry.

---

## 4. Recommended experiment ladder (do in order)

Ship scripts + a short `FINDINGS.md` under something like `tools/allocator-slots/` (or `re/hooks/allocator-slots/` if that tree exists on the research machine). Prefer one runner + one or more `.js` modes.

### Experiment A — observe only (always first)

Read-only poll:

- used count `@ 0x145426dd8`, free `@ 0x145426de8`
- off byte `@ 0x14542698b`
- optionally dump 64 owner TIDs from `@ 0x145427450` stride `0x258`

Attach in-world on a many-core box (or force load). Confirm used approaches 64 and correlate with `Avg contour` / `ClientSceneProcessing.log` "Finish SCENE". Mirror what `slots.py` / `slotmap.py` did in research (those tools may live only on the research workspace — reimplement minimally in Frida/`python` if missing from this clone).

### Experiment B — P1 CPU cap (easiest real fix; preferred control)

Patch **`FUN_142b4e800`** tail at **`0x142b4e80f`** (17 bytes) to `min(n, 8)` — exact bytes in `allocator-slots-fix-design.md`. Apply via spawn-suspended **or** as early as possible before the two pools construct.

**Expect:** used slots at world entry well under 64 (~45–50) on any CPU; all three contour threads (`FUN_143f98880`) hold slots [I/P].

Signature gate (optional but good): search `.text` for  
`48 83 EC 58 48 8D 4C 24 20 FF 15 ?? ?? ?? ?? 8B 44 24 40 48 83 C4 58 C3` + 8×`CC`.

### Experiment C — P3 cheap miss path (safety net, can attach later)

At **`0x142c05148`**, change scan bound `00 96 00 00` → `58 02 00 00` (check **1** owner instead of 64). Optionally lower dead-slot sweep period at **`0x142c05a6e`**: `10 27 00 00` (10000) → `E8 03 00 00` (1000).

**Expect:** no more multi-second freezes from lock storm; late threads still slower than slotted ones [I].

### Experiment D — P3b data-only (no code patch)

While used==64 and free==0, set off byte `0x14542698b = 1`; clear when free slots appear. Threads without a slot skip the scan [P]. Needs a poller; races possible.

### Experiment E — P4 table move (the question this prompt exists for)

Spawn-suspended path only. Steps:

1. `Memory.alloc(0x129A8)` (or `Memory.alloc` + ensure zeroed); confirm `newBase` is within ±2 GB of each LEA IP.
2. For each of the 8 LEAs: decode current RIP-relative target; rewrite disp32 so target becomes `newBase` (or `newBase+0x238` for `0x142c088fb`). Prefer patching via `Memory.protect` + write; verify with disassembly or raw bytes.
3. Patch capacity + scan bound immediates.
4. Resume; run Experiment A metrics; force a heavy zone-in on a 16+ thread machine if available.
5. **Abort criteria:** any crash before main menu → capture which patch step was last; do not iterate blindly — check for a missed LEA or non-reachable `newBase`.

**Success criteria for P4:**

- Process reaches character select / world without crash
- used count can exceed 64 (e.g. 70+) without the OpenThread storm on the miss path
- Contour threads hold slots on a CPU that previously filled 64
- `Finish SCENE` time comparable to a small-CPU / P1 run (order-of-magnitude, not microbench)

**Failure that still teaches:** crash → note which reference was wrong; unreachable alloc → need `VirtualAlloc` at a chosen VA near the image (Frida `Memory.map` / Native `VirtualAlloc` with preferred address).

---

## 5. Implementation constraints

| Rule | Detail |
|---|---|
| Test copy only | Prefer the hard-link / TestPlayer 0.695 copy; never the Steam install players validate |
| No disk patches | Memory only |
| Detach / kill | Same as terrain/paste: **do not kill the Frida runner while hooks are live** on a running client (known crash); exit game first, then detach — or use spawn scripts that exit cleanly |
| Writes | For A: read-only. For B–E: patch `.text` / data with protect dance; log before/after bytes |
| No server changes | Stream-hold stays as-is; this probe is client-only |
| No LaunchPad product code | Unless user later says "land P1"; this prompt stops at Frida proof |
| Provenance | Tag findings `[P Frida]` vs `[I]` |

Suggested layout:

```
tools/allocator-slots/
  README.md          # how to run
  run.py             # spawn or attach
  hook_observe.js    # Experiment A
  hook_p1_cap.js     # Experiment B
  hook_p3_miss.js    # Experiment C
  hook_p4_move.js    # Experiment E
  FINDINGS.md        # filled by the agent after runs
```

Reuse paste venv Frida if present (`tools/paste/.venv`).

---

## 6. Explicit non-goals

- Shipping LaunchPad CREATE_SUSPENDED patcher (already designed in `allocator-slots-fix-design.md`)
- Patching the exe on disk / breaking SHA-256 genuine check
- Raising capacity past 127 without a code cave
- Migrating an already-populated table mid-session
- BIOS / `bcdedit` / affinity workarounds (documented; not this task)
- Changing LaunchPad's product default `DirectThreadCount=2` (research runner forces 10 for repro only; product stays 2)
- Treating `DirectThreadCount=10` as a "fix" — it is the opposite: it restores Steam pressure so the bug can appear

---

## 7. Deliverable

1. Working Frida runner + scripts for **A** and at least one of **B** or **E** (E preferred if spawn-suspend works; else B + C).
2. `FINDINGS.md`: build stamp, whether spawn-early worked, used-count timelines, crashes, conclusion:
   - **P4 viable at runtime?** yes / no / only-with-preferred-VirtualAlloc / blocked-by-timing
   - **Is P1 enough?** (if measured)
3. Short note for the user: recommended next step (Frida-only toolkit vs building LaunchPad P1).

## Suggested branch / worktree

Research scripts may stay untracked under `tools/allocator-slots/` or `local/scratch/` if the user does not want them committed. If committing: confirm feature branch name with the user first (e.g. `research/frida-allocator-slots`). Do not start coding on `main` without asking.
