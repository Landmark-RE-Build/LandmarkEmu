# Design: a LaunchPad CPU cap for the 64-slot loading bug (documented, NOT built)

Status 2026-10-21: design only, nothing applied (user: "read and document don't apply"). Background and the other
options: [allocator-slots-options.md](allocator-slots-options.md) (P1 there). [P] = read from the 0.695 client /
measured; [I] = inferred.

## 1. What the fix changes

The 0.695 client asks Windows how many CPUs it has in exactly one place, `FUN_142b4e800` [P]:

```
(decompiled client code omitted: decompile the function named above from your own copy of the client)
```

Bytes in the genuine January 2017 exe (`Landmark64.exe` SHA-256 `8B0FC625…F4F9`), `.text`, no ASLR so addresses are
fixed [P] (file offset = VA − 0x140000000 − 0x1000 + 0x400 for `.text`):

| VA | File offset | Bytes | Meaning |
|---|---|---|---|
| 0x142b4e800 | 0x2B4DC00 | `48 83 EC 58` | sub rsp, 0x58 |
| 0x142b4e804 | 0x2B4DC04 | `48 8D 4C 24 20` | lea rcx, [rsp+0x20] |
| 0x142b4e809 | 0x2B4DC09 | `FF 15 91 0C AF 01` | call [GetSystemInfo] |
| **0x142b4e80f** | **0x2B4DC0F** | `8B 44 24 40` | mov eax, [rsp+0x40] (dwNumberOfProcessors) |
| 0x142b4e813 | 0x2B4DC13 | `48 83 C4 58` | add rsp, 0x58 |
| 0x142b4e817 | 0x2B4DC17 | `C3` | ret |
| 0x142b4e818 | 0x2B4DC18 | `CC × 8` | padding; the next function starts at 0x142b4e820 [P] |

The fix rewrites the 17 bytes from 0x142b4e80f (the 9 instruction bytes + the 8 padding bytes) as:

```
8B 44 24 40      mov  eax, [rsp+0x40]   ; the real count
6A 08            push 8                 ; the cap (N); the byte after 6A is the cap, 1..127
59               pop  rcx
39 C8            cmp  eax, ecx
0F 47 C1         cmova eax, ecx         ; eax = min(count, N)
48 83 C4 58      add  rsp, 0x58
C3               ret
```

It ends exactly at 0x142b4e820, so no other function is touched [P].

**Who reads the count** — all five callers then see at most N [P]:

| Caller | Uses it for | With cap 8 |
|---|---|---|
| FUN_143a8d560 `Resource::Manager Decompression` | one worker per CPU (uncapped) | 8 workers |
| FUN_143a98090 `TaskManagerJob` | one worker per CPU (uncapped) | 8 workers |
| FUN_143133360 | min(count, 3) — a capped pool | unchanged (3) |
| FUN_14313cb40 | a job-system size (next to FUN_143a93f80) | at most 8 [I: exact use not read] |
| FUN_142d120a0 | audio start-up (near "#Audio.log") | at most 8 [I] |

PCs with N or fewer logical processors behave exactly as today. Measured on a 12-thread PC (2026-10-21): 54 of 64
slots at world entry, 53 engine-pool workers of which 30 held a slot [P]; with N = 8 the two big pools shrink by ~8
workers each → ≈ 45-50 slots on ANY CPU [I], leaving the three contour threads and late threads (design preview) room.

## 2. How LaunchPad would apply it (in memory, no file changes)

Today PLAY starts the game with `Process.Start` (`LandmarkEmulator.LaunchPad/Game.cs` `Start`). The fix would:

1. **Check the exe is a known genuine build** — LaunchPad already verifies `Landmark64.exe` by SHA-256 before PLAY
   (PatchPacks strong list). Only the January 2017 exe has the byte map above. The February 2017 exe (SHA-256
   `84D46570…` from the org's fix/Feb-release branch) is a different compile: its function moves, so use step 2's
   signature search, never a fixed address for it.
2. **Find the site by signature, not only by address:** search `.text` for
   `48 83 EC 58 48 8D 4C 24 20 FF 15 ?? ?? ?? ?? 8B 44 24 40 48 83 C4 58 C3 CC CC CC CC CC CC CC CC`; require exactly one
   match (a second match or none → don't patch, start normally, log why).
3. **Start the game suspended:** `CreateProcessW(..., CREATE_SUSPENDED, ...)` with the same command line and working
   folder as today (SessionId, CasSessionId=0, STEAM_ENABLED=0, locale).
4. **Write the 17 bytes:** `VirtualProtectEx` → PAGE_EXECUTE_READWRITE, `WriteProcessMemory`, restore the old
   protection, `FlushInstructionCache`, then `ReadProcessMemory` the 17 bytes back and compare.
5. **Resume:** `ResumeThread` on the main thread. Any failure in 3-5 → kill nothing; resume the process unpatched and log
   "CPU cap not applied: <reason>" (the game still runs, as today).
6. LaunchPad keeps watching the game as now (exit codes, crash detection); the patch lives only in that process and is
   gone when the game closes.

No game file changes: the SHA-256 genuine-file check, Validate and Undo are unaffected [P]. No admin rights needed
(same user, own child process) [I].

## 3. The setting (LaunchPad advanced mode)

- **"Limit the game to N CPUs (fixes the 50 % / 'avg contour' loading hang on PCs with many CPU threads)"**:
  Off / **Auto** / Always; N = 8 by default, 4-16 allowed.
- **Auto** = apply only when `Environment.ProcessorCount` > N (nothing changes for smaller PCs).
- Default: Auto is the recommendation (the live client had this bug until shutdown — forum thread 57166, notes
  16 / 30 Jun 2016 — so this is a fix, not a change of gameplay; the user decides, see §5).
- Shown next to DirectThreadCount in the Client settings panel, with the same plain-words explanation.

## 4. Risks and how to check

| Risk | Note |
|---|---|
| Antivirus heuristics | "start suspended + write into the child" is a pattern some AV products flag [I]. Signed LaunchPad releases and a clear log line help; Off always works. |
| Wrong build | signature + SHA-256 gate; no match → no patch |
| Fewer workers on big CPUs | 8 decompression / job workers instead of 24 — loading is disk / slot bound, not CPU bound [I]; measure |
| Audio / job system using the count | they see ≤ 8; same as an 8-thread PC, which the game ran on at launch [I] |

**Verification** (before shipping): unit tests for the signature finder and the byte builder (synthetic buffers, no
game bytes in the repo); on a 12-thread PC with the cap at 4 (so it applies) and on a 16+ thread PC:
`LMEMU_CLIENT=695 python re/tools/slotmap.py <pid> 150 5` → slots used well under 64 at world entry, all three
`FUN_143f98880` contour threads holding a slot; loading time and in-world hitches compared with Off.

## 5. Rules this touches (decision for the user)

- **No game file mods:** respected — nothing on disk changes.
- **"Features must work on the unmodified client":** the server stays unchanged; this is an optional client-side fix
  for a 2016 client bug, applied only by LaunchPad and only in memory. It is still a modification of how the client
  runs, so it needs the user's explicit go-ahead before it's built or shipped.
- **Fallbacks without any patch:** Windows `msconfig` → Boot → Advanced options → Number of processors = 8; BIOS:
  E-cores off on Intel hybrid CPUs; `DirectThreadCount=2` (LaunchPad default).
