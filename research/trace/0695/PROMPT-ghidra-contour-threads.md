# Prompt: Ghidra — contour thread count + contour job queue caps (0.695 / 0.699)

Paste this whole file to a **Ghidra / decompile** agent. Research only: read the 0.695 (and Feb/`0.699` if available) decompile; **do not** patch the exe on disk; **do not** change LaunchPad / ClientLaunch unless the coordinator asks later.

**Goal:** pin the exact instructions that force **3** VoxelFarm contour workers and **`min(n, 3)`** on the contour job queue, so a later in-memory patch (same pipeline as P4 `AllocatorSlotPatch`) can raise them (e.g. to **5**, matching 0.589) after the allocator table expand is in place.

Marks: **[P]** already proven in repo docs / prior RE; **[I]** to confirm.

---

## Read first (required)

- `research/trace/0695/allocator-slots-options.md` §2 (pool table) and §3 (`Avg contour`)
- `docs/terrain.md` § "Slow loading screen: allocator slots"
- `docs/gameplay.md` (many-core / contour capped at 3)
- `research/trace/0695/client-commands-for-slots.md` (no ini/console override for these pools)
- Product patch style to match later: `LandmarkEmulator.ClientLaunch/AllocatorSlotPatch.cs` (VA + expected original bytes + validate-before-write)

Decompile roots (as on the research machine): `re/decomp_all_695` (and Feb build if separately indexed). Image base **`0x140000000`**, no ASLR on these exes [P].

---

## Known facts (do not re-prove unless wrong)

| Pool | Entry / start | Count today (0.695) | Notes |
|---|---|---|---|
| Contour threads `ContourThread::contourLoop` | start **`FUN_143ffa540`** from **`FUN_143f94580`**; loop body **`FUN_143f98880`** | **3** [P] | Log: groups of three `Avg contour: %d` lines. 0.589 had **5** [P docs]. |
| Contour job queue | **`FUN_143133360`** | **`min(n, 3)`**, at least 1 [P] | `n` from `GetSystemInfo` via `FUN_142b4e800` (same wrapper P1 would cap). |
| VoxelCellJob | separate | always 3 | Out of scope unless it shares the same immediate. |
| Allocator slots | P4 expands 64→127 | — | Assume P4 is available; contour bump is a **small add-on poke**, not another table move. |

API for contour thread sizing is documented as `GetLogicalProcessorInformation` (physical cores) with a hard **3** in practice [P gameplay/terrain]. Confirm whether the code is literally `3`, `min(cores, 3)`, `max(cores, 3)`, or “always 3 ignoring cores.”

---

## Tasks

### A. Contour threads (`FUN_143f94580` → `FUN_143ffa540`)

1. Open `FUN_143f94580`. Find where it decides how many contour / `contourLoop` threads to start (call to `FUN_143ffa540`, `_beginthreadex`, engine thread wrapper `FUN_142b55e10`, or a loop).
2. Identify the **instruction(s)** that encode the count **3**:
   - preferred: a single imm8/imm32 (`mov ecx, 3`, `cmp …, 3`, `lea`/`and` with 3, etc.)
   - or a short `min`/`cmov` sequence whose immediate is 3
3. Record for **each** poke site:
   - Absolute VA (Ghidra)
   - Full original instruction bytes (hex)
   - Mnemonic + operands
   - Which byte(s) to change for a new count **K** (e.g. 5), and any encoding limits (imm8 sign-extend, etc.)
4. Confirm the same `.text` runs again on **re-zone** (docs: re-zone tears down and recreates contour×3). A start-time memory poke must still apply on the next create — i.e. count comes from patched code, not a one-shot RAM field set only at process init. If a RAM field is filled once, say so and name the writer.
5. Note any second site (array size, job posting limit, “Contouring Job Count” overlay) that would stay at 3 if only the create loop is patched.

### B. Contour job queue (`FUN_143133360`)

1. Find the `min(n, 3)` (or equivalent) that sizes the queue workers.
2. Same deliverable as A: VA, original bytes, how to raise the cap to **K**, side effects if threads > queue or queue > threads.
3. Confirm whether this pool uses `FUN_142b4e800` (`dwNumberOfProcessors`) vs `GetLogicalProcessorInformation`.

### C. Cross-checks

1. **0.589 vs 0.695:** if `re/decomp_all` (0.589) is available, note the old **5** at the corresponding contour start — supports K=5 as historically live.
2. **Feb 0.699:** if the decompile is the same image as stamp `0x58a38fa2`, state that the VAs match the shared P4 profile world (same exe family). If a separate 0.699 decompile exists and VAs differ, list both — but expect one shared map [I, Frida on 0.699].
3. Search for other literals / compares that assume “exactly 3 contour threads” (unrolled waits, static arrays of 3, overlay text). List hits; mark which would break if K≠3.

### D. Out of scope

- Implementing LaunchPad / Frida patches
- Changing Decompression / TaskManagerJob / DirectThreadCount
- P1 CPU cap / P4 table move (already handled elsewhere)
- Server stream-hold

---

## Deliverable format

Write `research/trace/0695/contour-thread-patch-sites.md` (or append a clearly dated section if a related file already fits) with:

1. **Summary table**

| ID | Purpose | VA | Original bytes (hex) | Patch for K=5 (bytes) | Notes |
|---|---|---|---|---|---|
| C1 | Contour thread count | `0x…` | `…` | `…` | |
| C2 | Contour job queue cap | `0x…` | `…` | `…` | |

2. **Disasm snippets** (10–20 bytes context each) + decompile excerpt for the sizing expression.
3. **Re-zone / lifetime:** code path vs one-shot data.
4. **Coupling:** must C1 and C2 change together? Recommended K (5 vs 6 vs min(n,8)).
5. **Risk list:** anything that still assumes 3.
6. **Hand-off for ClientLaunch:** copy-paste ready “expected original bytes” + VA list in the same spirit as `AllocatorSlotPatch`’s shared profile (short hex only — no dumping huge `.text`).

Provenance: tag **[P]** / **[I]** on every claim. If a site cannot be found, say what was searched and stop — do not invent immediates.

---

## Success criteria

A later agent can add contour expand to `AllocatorSlotPatch` (or a sibling) with **validate-before-write** using only this write-up: VAs + expected bytes + new immediates, no further Ghidra.
