# Allocator slots on many-core PCs: what our options are (research, 2026-10-21)

Research only. Nothing was patched, no game file was touched, no client was started. Every byte below was read from
`<0.695 install>\Landmark64.exe` (read-only) or from `re/decomp_all_695`.
Marks: **[P]** proven (read in the code / bytes / logs), **[I]** inferred (reasoned, not yet measured).

## The short version

The game's memory allocator has **64 "fast lanes"** (per-thread slots). Every game thread that allocates memory takes
one and keeps it for as long as it lives. A thread that finds all 64 taken gets no lane; then **every** allocation
it makes checks all 64 owners with 5-7 Windows calls each, inside one lock the whole game shares. That is what
makes loading hang at 50 % and the design preview freeze the game.

Two of the game's thread pools start **one thread per logical processor with no upper limit**. So the bigger the
CPU, the fuller the table: a 12-thread PC uses ~58 of 64; a 16-thread PC needs ~66; an Intel Core Ultra 9 (22 or
24 threads) needs ~78-82. Only `DirectThreadCount` can be set from a file, and it is not enough on big CPUs.

| # | Option | Changes game files? | Fixes big CPUs? | Risk | Effort |
|---|---|---|---|---|---|
| N1 | `DirectThreadCount=1` (LaunchPad already writes 2) | ClientConfig.ini only (as now) | No (saves 1 slot) | none | none |
| N2 | Start the game with a CPU affinity mask / job object | No | **No** – the game counts CPUs with APIs that ignore affinity [I] | none | low |
| N3 | Windows: msconfig / `bcdedit /set numproc 8` | No (Windows boot setting) | Yes | whole PC uses fewer CPUs until undone | player, reboot |
| N4 | BIOS: Hyper-Threading / SMT off, or Intel E-cores off | No | Yes (Ultra 9 285K: 24 → 8 threads) | slower PC for other work | player |
| P1 | **Cap the CPU count the game sees at 8** (one 17-byte change in one function) | in memory: no / on disk: yes | **Yes** (~50 slots on any CPU) | low | low |
| P2 | Cap only the two pools at the call sites (2 × 7 bytes) | as P1 | Yes | low (fixed count, also on small PCs) | low |
| P3 | Make the "no lane" path cheap: check 1 owner instead of 64 (4 bytes) | as P1 | Partly: no more freeze; late threads still slower | low | low |
| P3b | Same with no code change: flip the allocator's own "off" byte while the table is full | game memory only | Partly, like P3 | low-medium (needs a watcher) | medium |
| P4 | Raise 64 → 127 slots (move the table; 10 code places) | as P1 | Yes up to ~40 threads | medium | medium |
| P5 | Reserve slots for important threads (preview, contour) | as P1 | Partly | medium-high (new code) | high |
| P6 | A hook DLL (proxy `version.dll` / injected DLL) doing P1/P3 | adds a file (proxy) or none (injection) | Yes | medium (AV, loader timing) | medium-high |

**Best value for least risk:** P1 (cap at 8), optionally with P3 as a safety net, applied in memory by LaunchPad
after starting the game suspended. On PCs with 8 or fewer threads P1 changes nothing at all. **Measure first** (end
of this file) – two numbers decide it.

---

## 1. How the slot table works (0.695) [P]

- Slot finder `FUN_142c05050`. Table at **0x145427450**, 64 × 0x258 bytes, owner thread id first; used count
  **0x145426dd8**, free count 0x145426de8, global lock (critical section) **0x145426420**, TLS index 0x145430a50.
  The table is in the zero-filled part of `.data`, fixed size, and the TLS index sits **directly after it**
  (0x145430a50), so it cannot grow in place.
- Who calls it: the alloc path (`FUN_142c04c50` → `FUN_142c070d0`, blocks up to 0x8000 bytes) and the free path
  (`FUN_142c05ef0`), only when the thread's TLS value is empty. So **a thread claims its slot at its first small
  allocation or free, not when it starts** [P]. Thread start (`_beginthreadex` → trampoline `FUN_142b55e10` →
  `Run`) allocates nothing itself; a pool worker (`FUN_143a95170`) waits for its first job, and only an optional
  "thread started" listener runs before that [I: whether that listener allocates].
- What it does, in order:
  1. If the allocator's off byte **0x14542698b** is set → return "no slot" at once (set only at shutdown,
     `FUN_142c090c0`; the only reader is 0x142c05054) [P].
  2. Take the global lock.
  3. If the free count is not 0: reuse a slot whose owner is 0.
  4. Else if used < 64 (`cmp rcx,0x40` at **0x142c050f3**): take the next new slot.
  5. Else **scan all 64** (loop bound `cmp rdi,0x9600` at **0x142c05145**): for each owner call `FUN_142c07cf0` =
     OpenThread, GetExitCodeThread, GetModuleHandleA + GetProcAddress("GetProcessIdOfThread"),
     GetProcessIdOfThread, GetCurrentProcessId, CloseHandle. The first owner that is dead (or not in this process)
     loses its slot to the caller. If all 64 are alive → return "no slot".
- A slot-less thread is not remembered as slot-less: the TLS value stays empty, so **step 5 runs again on every
  small allocation and every free**, ~300-450 Windows calls each time, all under the lock the main thread also needs
  (for blocks over 0x8000 bytes and its own misses) [P code; the cost estimate is I]. 2026-10-14 stack samples showed
  exactly these calls (NtOpenThread / NtQueryInformationThread / NtClose) in the stuck contour threads [P].
- Slot-less allocations still work: up to 0x800 bytes they go to the locked small allocator `FUN_142c05350`,
  larger ones to the locked general path [P]. So the game never crashes from this, it just crawls.
- **Finished threads do give their slot back, just late** [P]: (a) step 5 hands a dead thread's slot to the next
  thread that asks; (b) `FUN_142c059e0` sweeps the table for dead owners every **10,000** large/locked allocations
  (counter 0x145426de0, reset value `0x2710` at 0x142c05a67) and puts them on the free count. So the problem is only
  ever **more than 64 live threads that allocate**, never leaked slots. (Small leak [I]: Windows reuses thread ids;
  a new thread with a dead one's id keeps that stale slot "alive" until it exits.)
- The table is referenced by exactly **8 RIP-relative LEAs**, all in the allocator (0x142c050c5, 0x142c05a19,
  0x142c05c5c, 0x142c070b7, 0x142c0712e, 0x142c076e0, 0x142c088fb (+0x238), 0x142c09112) and the two bounds above;
  no 64-bit bitmask anywhere [P, full `.text` scan + disassembly].

## 2. How each pool decides its thread count [P unless marked]

| Pool (thread name) | Code | Count | API |
|---|---|---|---|
| `Resource::Manager Decompression` | FUN_143a8d560, call at 0x143a8d7eb | **n** (no cap) | GetSystemInfo `dwNumberOfProcessors` via FUN_142b4e800 |
| `TaskManagerJob` | FUN_143a98090, call at 0x143a9836a | **n** (no cap) | same |
| Contour job queue | FUN_143133360 | min(n, 3), at least 1 | same |
| (not a pool) | FUN_142d120a0 | flag "n > 3" for FUN_142e5bf50 | same |
| (debug text only) | FUN_14313cb40 "System Hardware Thread Count" | – | same |
| `VoxelCellJob` | FUN_14408a960 via FUN_14400e800 | 3 | GetLogicalProcessorInformation cores, always 3 |
| Contour threads (ContourThread::contourLoop) | FUN_143ffa540 from FUN_143f94580 | 3 | same (log shows 3 "Avg contour" lines at a time) |
| `MorphemeNetworkThread` | FUN_1440b4440 | min(logical, 4) | GetLogicalProcessorInformation (FUN_143b0d5d0) |
| LZHAM compressor helpers | FUN_143a6fcd0 / FUN_143a74f10 | n − 1, max 16 | GetSystemInfo – **no code path reaches the LZHAM compressor** in 0.695 (refscan: its init and entry points have no call/LEA/pointer) [I: likely never starts] |
| libdispatch / pthread work-queue workers | FUN_140008c70, FUN_140001a50 | scales with n, on demand | GetSystemInfo [I: whether the game queues work there] |
| `FileLoader` | FUN_143b310f0 | ClientConfig `[AssetDelivery] DirectThreadCount` (default 4, Steam 10, LaunchPad 2) | config |
| Fixed: ParticleEmitter 4, FileRemoteDelivery 4, PhysX dispatcher 2, LodChunkDecompressionThread, SoftwareOcclusion, SceneVisibilityListJob, LocalResourceCache, TcpDriverAsyncAddress, DelayedAssetLoader, Voxel Farm Scene Thread, Input Thread, InfiniteLoopMonitor … 1 each | | fixed | – |

**No other way to set them** [P for what was searched]: the only thread count read from a file is DirectThreadCount
(integer reader FUN_142b21540; other keys it reads are UdpParams.*). No environment variable (the only getenv
reads are curl proxies, Lua paths, JPEGMEM, LIBDISPATCH_DISABLE_KWQ, windir, symbol paths). `CalcCrcThreads` (4)
belongs to the asset-manifest builder, not play.

**Does the count follow CPU affinity?** No [I, Windows API behaviour]: `GetSystemInfo().dwNumberOfProcessors` and
`GetLogicalProcessorInformation` report the processors of the processor group, not the process's affinity mask
or a job object's. (The game itself only uses GetProcessAffinityMask in FUN_1443b6f80, to pin one timer thread to
the first CPU of the *system* mask.) Check with a 10-line test program started with `start /affinity 3`.

### Slot estimate per CPU size [I]

Measured 2026-10-14 on the 12-thread dev PC with DirectThreadCount=2: **58 slots at world entry, ~61 later** [P].
Taking the two uncapped pools out leaves ~34 slots that do not grow with the CPU (this includes the 2 FileLoaders).
So, if every pool worker has allocated at least once:

| Logical processors | DTC=2 (LaunchPad) | DTC=10 (Steam) | With P1 cap 8 (DTC=2) |
|---|---|---|---|
| 4 | 42 | 50 | 42 |
| 8 | 50 | 58 | 50 |
| 12 | 58 (measured) | 66 → full (the old 50 % hang) | 50 |
| 16 | 66 → full | 74 | 50 |
| 22 (Core Ultra 9 185H) | 78 | 86 | 50 |
| 24 (Core Ultra 9 285K, no HT) | 82 | 90 | 50 |
| 32 | 98 | 106 | 50 |

Add ~3 for threads that start later in a session. This matches the 12-thread history exactly (Steam's 10 hung,
2 fixed it), which is the main support for "every pool worker holds a slot".

## 3. The player report (Core Ultra 9, "Avg contour" spam, intermittent)

- **Where the line comes from** [P]: `ContourThread::contourLoop` = FUN_143f98880 (VoxelFarmWrapper.cpp). Each
  contour thread adds up the time it spends contouring; each time its total passes **5,000 ms** it logs
  `Avg contour: %d` (the average contour time from its rate tracker, in ms [I]) to the main client log
  `Logs\Landmark PlayClient (… ) (x64).log` (level 4; on this PC "Landmark PlayClient (Test) (x64).log", 78 lines,
  `Avg contour: 2`-`3` in groups of three = the 3 contour threads). So: **every line = another 5 seconds of
  terrain meshing by one thread**. A normal load prints a handful; an hour of spam means the meshing ran at a crawl
  – the slot-less pattern from 2026-10-14.
- **Slots on that CPU** [I]: ~78 (185H, 22 threads) or ~82 (285K, 24 threads) wanted vs 64 available, even with
  LaunchPad's DirectThreadCount=2 (1 would save one). Hybrid P/E cores make no difference to the count: Windows
  reports all of them.
- **Why only sometimes** [I, from the code in section 1]: slots are taken at a thread's *first allocation*, not at
  start, so it is a race. The 48 pool workers only take one when they get their first job. Who wins the last slots
  depends on how big the burst of decompression / task jobs was before the 3 contour threads first allocate, and on
  thread scheduling. A contour thread that wins keeps its slot for the whole session; one that loses crawls
  until some other thread exits. The later 10-15 s freezes fit the same cause: the main thread waits for the shared
  lock while slot-less threads run their 64-owner checks inside it.
- **Why a fresh Pioneer site loads in seconds** [I]: a new character lands on a small site, so the scene has few
  terrain cells to mesh and loads few assets. Fewer jobs wake fewer pool workers before the contour threads ask
  (they are more likely to get a slot), and even slow meshing of a few cells finishes quickly. The old character
  stands where the scene is large: many cells, many assets, every pool worker busy.
- **Hybrid CPUs and the options:** pinning the game to the P-cores (affinity) does *not* lower the count [I] (see
  above); it may even give the extra workers less room. Processor groups only matter above 64 logical processors
  (one group here). Real player-side fixes: BIOS E-cores off (285K → 8 threads, ~50 slots), msconfig / `bcdedit
  /set numproc` 8-12, or, exotic, `bcdedit /set groupsize 8` (splits the CPU into groups of 8 so GetSystemInfo
  reports 8 to the game; system-wide, admin, reboot; most programs then use only one group) [I]. The client-side
  fix that works on any CPU is P1.

### "DirectThreadCount=20 worked a lot better" (same player, own launcher, previous build)
- **This does not fit the slot model, and the only measurement we have says the opposite** for 0.695: on the
  12-thread dev PC, Steam's 10 FileLoaders gave 150-300 s scenes and 2 gave ~1 s (2026-10-14, repeated fresh starts)
  [P]. 20 FileLoaders take 18 more slots than 2 (FileLoaders start in CreateAssetSystem at game start and load files
  at once, so they claim early and keep their slots) [P code, I timing].
- Taking slots early does not protect the contour threads: they claim theirs only when they first mesh terrain at
  world entry (their loop, FUN_143f98880, waits in 100 ms steps without allocating until there is work) [P]. So
  with 20 FileLoaders the table is full *sooner*, and the contour threads lose *more* often [I].
- Likely explanations [I]: (1) the hang is a race, so a few good runs after a change look like a fix; (2) "previous
  build" may be 0.589 (built 4 June 2016, before the live fixes; 5 contour threads there), or "20" was another key
  in that launcher's file; (3) more FileLoaders do make *file loading* faster, which helps only when the wait is
  for files, not for meshing. To settle it: slots.py + owner attribution (section 5) with DTC 2 vs 20 on one PC,
  and the exact line the player's launcher wrote.

### "Fine until I harvested a tree, then every load does the avg-contour thing"
- The server-side trigger is a separate investigation (local/treeload). The client-side part [I, from section 1]:
  the slot race is decided again on **every game start**; what changes after the tree is the **size of the
  meshing job** at world entry. Each `Avg contour` line is 5 s of meshing by one thread, so the number of lines
  measures the job.
- Small job (few cells, e.g. building a few voxels, or a fresh Pioneer site): even a contour thread with no slot
  finishes in seconds, so losing the race goes unnoticed. Large job (whatever the tree makes the server send –
  many cells, or cells that keep being re-meshed): a slot-less contour thread crawls through it at a small fraction
  of normal speed, and the long, busy load also wakes more pool workers that take the remaining slots. The same
  race then costs an hour instead of seconds. Inside a session, contour threads that won a slot at entry keep it,
  so a tree cut mid-session meshes at normal speed; only the next login pays.
- So the tree case is a workload multiplier on the slot problem, not a second client bug [I]. P1 / P3 would cut
  its cost; a server fix that keeps the tree from enlarging the scene would remove the trigger.

## 4. The options in detail

All addresses are fixed: the exe has **no relocations and no ASLR** (DllCharacteristics 0x8120, DYNAMIC_BASE off,
reloc directory empty) [P], no Authenticode signature, and **no Steam DRM wrapper** (no `.bind` section; sections
.text .rdata .data .pdata .tls _RDATA .rsrc) [P]. File offset of a `.text` address = VA − 0x140000C00 [P].

### N1-N4 – without touching the game
Covered in the table and section 3. N2 (affinity / job object) does not help [I]. N3/N4 are what live players did in
2016 (msconfig); they slow the whole PC and need the player to change Windows or BIOS settings.

### P1 – cap the CPU count the game uses (recommended)
- **How:** FUN_142b4e800 is the game's own `GetSystemInfo().dwNumberOfProcessors` wrapper. Its tail (17 bytes,
  including 8 bytes of int3 padding) becomes `min(n, K)`:
  - at **0x142b4e80f** (file 0x2B4DC0F), now `8B 44 24 40 48 83 C4 58 C3 CC CC CC CC CC CC CC CC`
  - new `8B 44 24 40 6A 08 59 39 C8 0F 47 C1 48 83 C4 58 C3`
    (`mov eax,[rsp+40]; push 8; pop rcx; cmp eax,ecx; cmova eax,ecx; add rsp,58; ret`, K = 8 in the `6A 08`).
- **What it changes:** Decompression and TaskManagerJob get min(n, 8) threads; the contour queue (already ≤ 3) and
  the "n > 3" flag are unchanged for K ≥ 4; the debug overlay shows 8. Nothing that uses GetLogicalProcessorInformation,
  libcef, PhysX or libdispatch is touched. PCs with ≤ 8 threads: byte-for-byte the same behaviour.
- **Risk:** low. Possibly a little slower asset decompression on big CPUs (8 instead of 24 workers; file reading is
  already limited to 2 FileLoaders) [I].
- **Verify:** slots.py used count at world entry ≤ ~50 on a 16+ PC; `Contouring Job Count / System Hardware Thread
  Count` overlay shows 8; "Finish SCENE" time in ClientSceneProcessing.log ~1 s.

### P2 – cap only the two pools
- At **0x143a8d7eb** (Decompression) and **0x143a9836a** (TaskManagerJob): `E8 xx xx xx xx 8B D0` (call + `mov
  edx,eax`) → `BA 08 00 00 00 90 90` (`mov edx,8`).
- A fixed 8 also on small PCs (a 4-thread PC gets 8 instead of 4: harmless, slightly more slots). Use only if P1's
  wider reach is unwanted.

### P3 – make the "no slot" path cheap
- At **0x142c05148** (file 0x2C04548), the scan bound `00 96 00 00` (64 slots) → `58 02 00 00` (1 slot). When the
  table is full, a slot-less thread then checks one owner instead of 64.
- Dead threads' slots still come back through the 10,000-count sweep; optionally make that faster: at **0x142c05a6e**
  `10 27 00 00` (10,000) → e.g. `E8 03 00 00` (1,000).
- **Changes:** no more 300-450 Windows calls per allocation under the shared lock; late threads still use the
  slower locked allocator, so the preview / contour work is slower than with a slot but should no longer freeze
  the game [I]. Does not lower the thread count; good as a safety net next to P1.

### P3b – the same without changing code
- Write `1` to the allocator's own off byte **0x14542698b** while used count (0x145426dd8) is 64 and nothing is on
  the free count; write `0` again when slots free up. With it set, a thread without a slot skips the search and goes
  straight to the locked allocator; threads that have a slot are not affected [P: the byte's only reader is
  0x142c05054, which returns "no slot" before taking the lock].
- Needs LaunchPad (or a helper) to keep running and poll the game's memory; new threads get no slot while it is set,
  even if one frees between polls. Data-only, but still a write into the running game.

### P4 – more slots (64 → 127)
- Allocate a new zeroed table of 127 × 0x258 = 0x129A8 bytes within ±2 GB of the code (e.g. just after the image,
  0x1461B0000; SizeOfImage 0x61AF000) and before the first allocation:
  - rewrite the disp32 of the 8 LEAs listed in section 1 to the new address (+0x238 for 0x142c088fb);
  - **0x142c050f6** imm8 `40` → `7F` (127 is the largest an imm8 compare allows; 128+ needs a code cave);
  - **0x142c05148** imm32 `00 96 00 00` → `A8 29 01 00` (127 × 0x258).
- **Changes:** room for ~40 logical processors at DTC=2 (34 + 2n ≤ 127 → n ≤ 46, minus late threads). Risk medium:
  if one reference is missed, slot memory and the TLS index collide → crash on start (easy to notice, hard to debug).
  Must be applied while the process is suspended, before the loader runs TLS callbacks and the CRT allocates.

### P5 – give important threads first claim
- E.g. keep the last N slots for threads that mark themselves (the preview thread FUN_143f99120, the 3 contour
  threads) via a flag set in their start code. Needs new code in a cave in 3-4 places; the preview thread is made by
  FUN_143f93a50 "when the scene holds none", so it may already keep its slot for a whole scene [I]. High effort for
  less than P1 gives.
- Releasing a slot at thread exit (the exe's TLS callback 0x140008ab0 already sees thread detach) would not help:
  finished threads are already reclaimed (section 1).

### P6 – how a patch would be applied
- **In memory, by LaunchPad (no file change)** [I: standard Windows technique]: `CreateProcess(…,
  CREATE_SUSPENDED)` → `VirtualProtectEx` (.text to RWX) → `WriteProcessMemory` the bytes above (fixed addresses:
  no ASLR) → restore protection → `FlushInstructionCache` → `ResumeThread`. LaunchPad already starts the exe itself
  (`Landmark64.exe SessionId=… STEAM_ENABLED=0`), so this fits in its PLAY step and can be an option (live default
  off/on, toggle). Keeps the SHA-256 genuine-file check passing; the file on disk stays the shipped one. Cons:
  antivirus tools sometimes flag programs that write into other processes; a client started by hand or by Steam
  gets no fix; it is still a change to the client's behaviour at run time, which the project's no-mods rule covers
  in spirit – it would need the user's decision.
- **On disk (patched exe):** the same bytes at the file offsets given. Simple and permanent, but **breaks the
  SHA-256 genuine-file check and the no-game-file-mods rule** – not an option for releases.
- **Hook DLL:** (a) a proxy DLL in the game folder named like one the exe loads from there – `VERSION.dll` (3
  imports, not a KnownDLL), `WINMM.dll`, `DINPUT8.dll` – forwarding the real functions and patching / hooking at
  load. It **adds a file to the game folder** (Steam "verify" leaves extra files, but our genuine-file check should
  be told about it). (b) LaunchPad injecting a DLL into the suspended process (no file in the game folder). Either
  could hook the exe's GetSystemInfo import (IAT slot **0x14463F4A0**, filled by the loader, so it must be done after
  imports are bound) to cap `dwNumberOfProcessors`: same effect as P1 plus libdispatch/LZHAM, not libcef/PhysX
  (their own imports). More moving parts than P1 for the same result.

## 5. What to measure first (read-only)

1. `LMEMU_CLIENT=695 python re/tools/slots.py` (it already reads the 0.695 table; docs/terrain.md still says it reads
   0.589 – stale line) at: main menu, character select, right after world entry, after a design preview. On the
   12-thread dev PC and, when possible, on a 16+ thread PC. Expected: 16+ threads → 64/64 at world entry.
2. Who holds the slots: map the 64 owner ids to each thread's start address (read-only
   `NtQueryInformationThread(ThreadQuerySetWin32StartAddress)`) → count Decompression / TaskManagerJob workers among
   them. That proves or disproves "every pool worker takes a slot" and the "claimed at first job" race.
3. The affinity question (N2): a tiny test program started with `start /affinity 3` printing
   dwNumberOfProcessors – expected: unchanged.
4. Before/after for any patch: ClientSceneProcessing.log "Finish SCENE: Total Process Time", the number of
   `Avg contour` lines in the main log per load, and slots.py at world entry.

## Open (for OPEN-LEADS L198)
- Whether the worker "thread started" listener (FUN_143a948d0 → +0xf8) allocates, i.e. whether workers claim at
  start or at first job [I].
- Whether libdispatch workers run game work (they would add n-scaled slots) [I].
- Whether the LZHAM compressor can be reached by any indirect path [I: refscan finds none].
- Where the finished PreviewGenerationThread is dropped (still open from docs/gameplay.md).

## Measurement 2026-10-21 (read-only, re/tools/slotmap.py + a stack-walk of every thread)

PC: AMD Ryzen 5 5600X, 6 cores / 12 logical processors. Client: the hard-link 0.695 test copy (TestPlayer2), ClientConfig
DirectThreadCount=2, landing on the dev world. Nothing was written to the process.

- **Timeline:** 1 slot at start → 16 by 25 s (loading) → **54 of 64 at world entry (50 s), stayed 54** for the next
  100 s in the world. Live threads: 7 → 85 (loading) → 148-154 in the world. [P]
- **Who holds a slot** (thread start resolved past the CRT wrappers FUN_1437f1d38 / FUN_1437f1bd8 and the engine
  thread wrapper FUN_142b55e10): 30 × FUN_143a95170 (the engine's pool worker loop — Decompression / TaskManagerJob
  workers), **3 × FUN_143f98880 (ContourThread::contourLoop: all three contour threads got a slot on this PC)**, 12 ×
  FUN_1400031e0 (a third-party worker pool, FUN_140006220 wrapper), 2 × _getptd_noexit, 1 each FUN_143f5d100,
  FUN_143aac880, FUN_143f99df0, FUN_142b20980 (FUN_1437ed1bc), FUN_142b65b20, FUN_142c070d0 (FUN_14422b800), main. [P]
- **Live threads without a slot:** 23 × FUN_143a95170 (pool workers that haven't allocated yet), 24 ×
  FUN_140008eb0 ("worker_main(prio=%d,cpu=%d)", the third-party pool's workers — they never use the game allocator), 25
  outside the exe (Windows / driver threads), TLS-callback / atexit threads, a few others. [P]
- **Reading:** on 12 logical processors the table settles at 54 with 10 free, and the contour threads are inside. The
  engine pool alone has 53 workers here (≈ 4.4 per logical processor) and 30 of them already took a slot (≈ 57 %);
  every extra logical processor therefore adds ≈ 2.5 slot users [I] → ≈ 64 (full) at 16 threads, ≈ 79 at 22, ≈ 84 at 24
  — the same as the estimate table above. Which threads miss out depends on who allocates first (the race), so a
  many-core PC can lose the contour threads on one login and not on the next [I].
- **Not measured yet:** the design preview (would need the client driven into placement mode) and a 16+ thread PC.
