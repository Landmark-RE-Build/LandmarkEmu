# Contour thread + Contouring Job queue patch sites (0.695 / 0.699)

Research only (2026-10-29). Read from open Ghidra program `Landmark64.exe` at `D:\Landmark\Game\Landmark64.exe`.
**No exe / LaunchPad / ClientLaunch changes.** Marks: **[P]** proven in this decompile / bytes; **[I]** inferred; **[P docs]** prior repo docs.

Image base `0x140000000`. Two builds share the same site bytes **[P]**:

- `Landmark64_0.695.exe` — PE **TimeDateStamp `0x586D8017`** (2017-01-04) → `AllocatorSlotPatch.TimeDateStamp0695`
- Ghidra `Landmark64.exe` / profile world — **`0x58A38FA2`** (2017-02-14) → `AllocatorSlotPatch.ExpectedTimeDateStamp`

ClientLaunch accepts either stamp; all allocator LEA/cap/scan and contour C1/C2 sites match on both **[P]** (byte check on `Landmark64_0.695.exe` 2026-10-29).

**0.589:** no binary / decompile on this machine — cannot pin the historical **5** immediates. Treat K=5 as the documented live-era target only **[P docs]** / unavailable here.

---

## 1. Summary table

| ID | Purpose | VA | Original bytes (hex) | Patch for K=5 (bytes) | Notes |
|---|---|---|---|---|---|
| **C1a** | Contour floor when physical cores ≤ 4 | `0x143f94767` | `b9 03 00 00 00` | `b9 05 00 00 00` | `mov ecx, 3` → `5` **[P]** |
| **C1b** | Contour `min` compare | `0x143f94773` | `83 f9 03` | `83 f9 05` | `cmp ecx, 3` → `5` **[P]** |
| **C1c** | Contour `min` immediate | `0x143f94783` | `83 e0 03` | `83 e0 05` | `and eax, 3` → `5` **[P]** |
| **C2a** | Contouring Job queue `min` compare | `0x143133840` | `83 f8 03` | `83 f8 05` | `cmp eax, 3` → `5` after `GetSystemInfo` **[P]** |
| **C2b** | Contouring Job queue `min` immediate | `0x143133850` | `83 e1 03` | `83 e1 05` | `and ecx, 3` → `5` **[P]** |

Minimal poke set for “always K on any CPU”: **C1a+C1b+C1c** (and **C2a+C2b** if raising the job pool too). Imm8 only — K must fit signed/unsigned imm8 as written (`0x03`…`0x7f` safe; keep K small, e.g. 5–8) **[P]**.

Optional leave-alone: `cmp ecx, 4` / `jg` at `0x143f94762` (`83 f9 04 7f 07`) — threshold “cores ≤ 4 → use floor”; with C1a=K and min=K the threshold no longer changes the result **[P]**.

---

## 2. Contour threads (`FUN_143f94580` → `FUN_143ffa540`)

### Sizing expression **[P]**

```text
FUN_143b0d5d0(&physicalCores, &logicalInPackages);  // GetLogicalProcessorInformation
if (physicalCores <= 4)  t = 3;          // C1a
else                     t = physicalCores - 1;
count = min(t, 3);                       // C1b + C1c  (branchless)
FUN_143ffa540(scene, count);             // starts ContourThread workers
// same count also → FUN_14400e800(..., count) → FUN_14408a960(..., count) → VoxelCellJob
*(int*)(scene + 0x4610) = count;         // written inside FUN_143ffa540
```

**Verdict on “3 vs min(cores,3) vs always 3”:** code is **`min(adjusted, 3)` with floor 3 when cores≤4**. On every realistic PC the final value is **exactly 3** (the core API is consulted then discarded by the cap) **[P]**. Not `max(cores,3)`; not a bare `mov ecx,3` with no API call.

API: **`GetLogicalProcessorInformation`** via `FUN_143b0d5d0` (physical RelationProcessorCore count into first out-arg) **[P]** — **not** `FUN_142b4e800` / `dwNumberOfProcessors`.

### Disasm (context) **[P]**

```text
143f94756  e8 75 8e b7 ff        call    FUN_143b0d5d0
143f9475b  8b 8c 24 90 00 00 00  mov     ecx, [rsp+90h]
143f94762  83 f9 04              cmp     ecx, 4
143f94765  7f 07                 jg      143f9476e
143f94767  b9 03 00 00 00        mov     ecx, 3          ; C1a
143f9476c  eb 02                 jmp     143f94770
143f9476e  ff c9                 dec     ecx
143f94770  41 8b c4              mov     eax, r12d       ; eax=0
143f94773  83 f9 03              cmp     ecx, 3          ; C1b
143f94776  0f 9c c0              setl    al
143f94779  f7 d8                 neg     eax
143f9477b  44 8b f0              mov     r14d, eax
143f9477e  44 23 f1              and     r14d, ecx
143f94781  f7 d0                 not     eax
143f94783  83 e0 03              and     eax, 3          ; C1c
143f94786  44 0b f0              or      r14d, eax       ; r14d = min(t,3)
143f94789  41 8b d6              mov     edx, r14d
143f9478c  48 8b cf              mov     rcx, rdi
143f9478f  e8 ac 5d 06 00        call    FUN_143ffa540
```

Original blob for validate (from `0x143f94762`, 37 bytes):

`83 f9 04 7f 07 b9 03 00 00 00 eb 02 ff c9 41 8b c4 83 f9 03 0f 9c c0 f7 d8 44 8b f0 44 23 f1 f7 d0 83 e0 03 44 0b f0`

### Re-zone / lifetime **[P docs + P]**

- Count is computed in **`.text` every time** `FUN_143f94580` runs; `FUN_143ffa540` then loops `param_2` times and stores `param_2` at **`scene+0x4610`** **[P]**.
- Prior note: zone load rebuilds terrain / voxel farm; re-zone tears down and recreates contour×3 (`client-commands-for-slots.md` §3.2) **[P docs]**. So a **start-time `.text` poke still applies on the next create** — not a one-shot RAM field at process init **[P + P docs]**.
- Sole code caller of `FUN_143f94580`: `FUN_143f94b30` **[P]**. Sole code caller of `FUN_143ffa540`: `FUN_143f94580` **[P]**.

### Second sites if only the create loop is patched

| Site | Effect if C1 raised and this left alone |
|---|---|
| **VoxelCellJob** `FUN_14408a960` ← same `count` arg | **Follows C1 automatically** (no separate imm3). Docs “always 3” was the shared cap, not a second hardcode **[P]** (corrects allocator-slots-options row). |
| Contouring Job / C2 pool | Stays `min(n,3)` unless C2 patched — **different pool** **[P]** |
| Overlay `Contouring Job Count` | Reads live worker count from the C2 queue (`FUN_143a93f80(+0x800)`); no hard 3 **[P]** |
| `Avg contour: %d` | Per-thread log; group size = live thread count — no hard 3 **[P]** |

No unrolled “exactly 3 waits” found in the sizing / start path **[P]** (searched this create path only; broader “arrays of 3” not exhaustively scanned **[I]**).

---

## 3. Contouring Job queue (`FUN_143133360`)

Ctor of **FullScreenClaimOwnership**; starts workers via `FUN_143a95260` with name **`"Full Screen Claim Ownership Thread Pool"`** (`0x1447126b8`). Debug overlay still labels the count **“Contouring Job Count”** (`FUN_14313cb40`) **[P]**. Matches the pool docs call “contour job queue” **[P docs]**.

### Sizing expression **[P]**

```text
n = FUN_142b4e800();              // GetSystemInfo → dwNumberOfProcessors
q = min(n, 3);                    // C2a + C2b
workers = max(q, 1);              // at least 1
FUN_143a95260(this+0x800, workers, 0, 2, "Full Screen Claim Ownership Thread Pool");
```

Uses **`FUN_142b4e800`** (logical processors), **not** `GetLogicalProcessorInformation` **[P]**.

### Disasm (context) **[P]**

```text
143133839  e8 c2 af a1 ff        call    FUN_142b4e800
14313383e  8b ce                 mov     ecx, esi        ; 0
143133840  83 f8 03              cmp     eax, 3          ; C2a
143133843  0f 9c c1              setl    cl
143133846  f7 d9                 neg     ecx
143133848  44 8b c1              mov     r8d, ecx
14313384b  44 23 c0              and     r8d, eax
14313384e  f7 d1                 not     ecx
143133850  83 e1 03              and     ecx, 3          ; C2b
143133853  44 0b c1              or      r8d, ecx        ; r8d = min(n,3)
143133856  41 83 f8 01           cmp     r8d, 1
…                                  ; max(..., 1) into edx
143133888  e8 …                  call    FUN_143a95260
```

Original blob for validate (from `0x143133840`, 20 bytes):

`83 f8 03 0f 9c c1 f7 d9 44 8b c1 44 23 c0 f7 d1 83 e1 03 44 0b c1`

---

## 4. Coupling / recommended K

| Question | Answer |
|---|---|
| Must C1 and C2 change together? | **No hard dependency** — different pools / APIs **[P]**. Raising C1 alone increases VoxelFarm contour + VoxelCellJob workers; C2 alone increases the claim-ownership / “Contouring Job Count” pool. |
| Recommended | **K=5** on **C1a+C1b+C1c** (and C2a+C2b for symmetry with old caps) after P4 allocator expand — matches documented 0.589 contour count **[P docs]** without a 0.589 binary here. |
| K=6 / `min(n,8)` | Feasible (imm8); +3–5 more slot consumers vs today — only after P4 (≥127) **[I]**. Prefer K=5 first. |
| Threads > queue or queue > threads | Contour mesh (C1) vs Contouring Job (C2) are not a 1:1 producer/consumer pair in the sizing code **[P]**; mismatch is unlikely to hard-fail, but keep them equal unless measuring shows otherwise **[I]**. |

---

## 5. Risk list (still assume 3)

| Risk | Severity | Notes |
|---|---|---|
| Slot pressure | Medium without P4 | +2 contour + +2 VoxelCellJob (+ optional +2 C2) vs today **[I]** |
| Floor-only patch (C1a only) | High / wrong | Cap still forces 3 — must change C1b+C1c **[P]** |
| Cap-only (C1b+C1c), leave C1a=3 | Medium | ≤4 physical cores stay at 3; ≥6 physical get min(cores−1, K) **[P]** |
| Static arrays / unrolled 3 | Low (not seen in start path) | Broader scan not done **[I]** |
| Overlay / logs | None | Dynamic counts **[P]** |

---

## 6. Product setting rules (when wired into LaunchPad / LandmarkLocal)

Mirror **Allocator slot expand** (Off / Auto / On, Client settings, labeled **experimental**). Contour raise is a **second** experimental toggle with stricter gating:

| Rule | Detail |
|---|---|
| Default | **Off** |
| Label | **experimental** (same badge pattern as allocator expand in `launchpad.js`) |
| Enable only if P4 is active | Contour expand may be **Auto** or **On** only when `AllocatorSlotExpand` is **Auto** or **On**. If allocator expand is **Off**, contour control is disabled / forced **Off** — user cannot turn it on. |
| Turning P4 off | Setting allocator expand back to **Off** clears contour expand to **Off** (persist that). |
| Apply order | Suspended start: apply **P4 first**, then contour sites only if P4 actually applied this launch (not merely “setting On” if validate failed). If P4 validate fails → alert, no contour pokes. |
| Patch set | **C1a+C1b+C1c** and **C2a+C2b** together at K=5 (recommended). |
| Alert | Same fail-loud style as P4 if sites mismatch. |

UI copy sketch: *“Contour threads (experimental) — raise VoxelFarm contour / Contouring Job workers from 3 to 5 (0.589-era). Requires Allocator slot expand (Auto or On). In memory only.”*

---

## 7. Hand-off for ClientLaunch (AllocatorSlotPatch style)

Same PE gate: **`AcceptedTimeDateStamps`** = `0x586D8017` | `0x58A38FA2`. Validate-before-write; short expected hex only.

```csharp
// Contour thread count (FUN_143f94580) — raise floor + min cap to K=5
// C1a
public const ulong ContourFloorVa = 0x143F94767UL;
public const string ContourFloorExpectedHex = "b9 03 00 00 00"; // mov ecx, 3
public const string ContourFloorPatchHex    = "b9 05 00 00 00"; // mov ecx, 5

// C1b
public const ulong ContourMinCmpVa = 0x143F94773UL;
public const string ContourMinCmpExpectedHex = "83 f9 03"; // cmp ecx, 3
public const string ContourMinCmpPatchHex    = "83 f9 05"; // cmp ecx, 5

// C1c
public const ulong ContourMinAndVa = 0x143F94783UL;
public const string ContourMinAndExpectedHex = "83 e0 03"; // and eax, 3
public const string ContourMinAndPatchHex    = "83 e0 05"; // and eax, 5

// Contouring Job queue (FUN_143133360) — min(n, K), then existing max(...,1)
// C2a
public const ulong ContourJobMinCmpVa = 0x143133840UL;
public const string ContourJobMinCmpExpectedHex = "83 f8 03"; // cmp eax, 3
public const string ContourJobMinCmpPatchHex    = "83 f8 05"; // cmp eax, 5

// C2b
public const ulong ContourJobMinAndVa = 0x143133850UL;
public const string ContourJobMinAndExpectedHex = "83 e1 03"; // and ecx, 3
public const string ContourJobMinAndPatchHex    = "83 e1 05"; // and ecx, 5
```

Optional wider validate window (contour): read 37 bytes at `0x143f94762` expecting  
`83 f9 04 7f 07 b9 03 00 00 00 eb 02 ff c9 41 8b c4 83 f9 03 0f 9c c0 f7 d8 44 8b f0 44 23 f1 f7 d0 83 e0 03 44 0b f0`.

Optional wider validate (job): read 20 bytes at `0x143133840` expecting  
`83 f8 03 0f 9c c1 f7 d9 44 8b c1 44 23 c0 f7 d1 83 e1 03 44 0b c1`.

---

## Provenance cheat-sheet

| Claim | Tag |
|---|---|
| VAs / bytes / formulas above | **[P]** this Ghidra session |
| Re-zone recreates contour workers | **[P docs]** `client-commands-for-slots.md` §3.2 |
| 0.589 had 5 contour threads | **[P docs]** only — **no 0.589 binary here** |
| Stamps (sites identical) | **[P]** `0x586D8017` (0.695.exe) + `0x58A38FA2` (profile) |
| VoxelCellJob shares C1 count | **[P]** (docs table “separate always 3” was misleading) |
