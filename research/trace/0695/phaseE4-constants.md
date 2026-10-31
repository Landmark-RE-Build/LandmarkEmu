# Phase E, part 4: tuning values the client keeps as data (lead L222 / phaseE3-deep.md L-D2), 2026-10-26

Read-only hunt (brief: trace/0695/agent-phaseE3-brief.md). Server code HEAD db90a5f on client-0.695. Exes read
read-only with pefile: 0.695 `<0.695 install>\Landmark64.exe` (PE stamp 2017-01-04 23:07) and 0.589
`<Landmark folder>\Landmark64.exe` (PE stamp 2016-06-04 09:17, the build re/decomp_all comes
from). Scratch scripts and outputs: `<user folder>\AppData\Local\Temp\claude\<workspace>\28d489f2-5945-4c82-ba0b-9796b72f5283\scratchpad\e4-constants\`
(scan.py / scan2.py pairing, rd.py byte reader, roots.py server-relevant closure, init.py initialiser literals,
lit.py DAT-vs-literal check; pairs2.tsv, review.tsv, rel.tsv).

## Method

1. **Pairing.** Every function pair in trace/0695/functions.tsv (233,295 pairs). For each pair with data
   references on both sides (71,166 pairs), the ordered list of `DAT_` / `_DAT_` / `PTR_*_` / `UNK_` references was
   taken from both decompiles. Where functions.tsv says the code is `same` (normalised, `rcmp.norm` rules), the lists
   align by position. The same-code pairs with unequal list lengths (49 in the DAT-only pass, 47 with PTR / UNK too)
   were skipped by the position pass. The 49 were re-aligned with difflib (mism.py). Where the code
   changed (5,238 pairs), the full normalised token streams were aligned with difflib and only DATs inside matching
   blocks were paired. Result: 73,784 distinct (0.589 DAT, 0.695 DAT) pairs.
2. **Values.** For every pair the 8 bytes at both addresses were read (16 bytes for 16-byte aligned pairs). Pairs in
   the zero-initialised part of .data (31,222) and pointer-valued pairs (28,118) were set aside; 14,444 pairs of
   initialised data were compared: 13,851 identical in all 8 bytes, 368 identical in the first 4, 225 different.
3. **Runtime-initialised globals.** A value in .bss is set by code. All literal stores `DAT_x = <number>;` were
   collected from both decompiles (4,433 globals in 0.589, 4,442 in 0.695) and compared for every DAT pair from step
   1 (4,435 pairs have literal stores on both sides), and again by writer-function pairing (initialiser vs. its
   partner, store by store).
4. **Mixed rendering.** In changed functions, a DAT on one side aligned with a numeric literal on the other side
   (Ghidra typed one copy, not the other) was checked: DAT value vs. literal.
5. **Whole-section check.** Every "round" f32 / f64 value (≤ 3 significant digits, 0.001 … 1e6) in each .rdata was
   counted; values present in one build only were looked up.
6. **Relevance.** Server-relevant 0.695 functions = 5,933 roots (2,089 0.695 functions and 1,057 0.589 functions
   (mapped) cited anywhere in LandmarkEmu; the 2,085 Lua natives of re/out/luabind_695.tsv; the 1,697 packet
   handler / sender functions of re/out/agent_coverage_e/inst_695.tsv) plus their direct callees to depth 3 = 22,542
   functions (rel.tsv). The scan itself covers the whole exe, so relevance only ranks.
7. **Server side.** Every DAT the server cites (60 addresses in LandmarkEmu *.cs), every `DAT_x = value` claim in
   LandmarkEmu, trace/0695 and trace/systems (28 claims), and every server constant whose comment cites a client
   function (srv_consts_cited.txt, 187 lines; the 17 float / double ones read one by one) were checked against the
   0.695 exe.

## 1. Findings

### F1. No tuning value held as data changed between 0.589 and 0.695 — OK-NOTE (closes L222)
- **Verdict:** OK-NOTE. Nothing for the server to change.
- **Evidence [P]:** of the 225 + 368 differing pairs, every one is one of these:
  - **.text switch tables** (relative jump offsets, e.g. FUN_1433ba260's table 0x1433ba644, the one the server cites
    in AdminCommands.cs:424): the offsets differ because the code moved.
  - **1-byte booleans read with neighbours**: the first byte is equal (`01` = true in both). The differences are in the
    next bytes, which belong to other globals (e.g. DAT_1451f6311/2 vs. 0.589 DAT_14511d290/1, used by 18 world
    functions such as FUN_142cdd200; DAT_145219a09 used by FUN_142fbd5f0, the function L-D2 named).
  - **Misaligned pairs in changed functions**: one 0.589 DAT was paired with two 0.695 DATs or the other way round
    (flagged AMB). Examples: string literals "Type" / "Name" (DAT_1446bc690), the 1e-12 / 1e-4 pair at DAT_144c410b0 /
    DAT_144c41050 (swapped, not changed), the sign table of FUN_142fe75f0 (±1.0 entries reordered).
  - **Neighbour bytes in 16-byte reads**: bytes 8-15 hold the next, unrelated constant (e.g. 0.589 {1e6, 2000} vs.
    0.695 {1e6, π} at DAT_1446ee910): the referenced first 8 bytes are equal.
  - **Engine geometry**: index table DAT_1449e1e00 (FUN_144290a10, a contour/geometry routine; not server-relevant,
    an offset read from a table base).
  - The double pair 0.5 → 1.0 (DAT_144c41178 vs. 0.589 DAT_14460a250 / DAT_144b72eb8) is a misalignment in the changed
    FUN_144004c20 (rendering, depth 9): the 0.695 function reads both 0.5 and 1.0 elsewhere.
- Runtime-initialised globals: 0 differences in 4,435 pairs and 0 by writer-function pairing [P].
- DAT vs. literal in changed functions: 0 differences [P].
- Whole-section check: only 0.589 has the f32 values 87, 131, 189, 285, 288, 306, 467, 575, 596, 708, 716, 854, 856,
  857, 972, 984, 1520. Only 0.695 has 21, 54.5, 101, 121, 186, 193, 219, 221, 252, 347, 443, 447, 548, 554, 560, 614
  and the f64 42.0. None of them is a DAT any paired function reads (step 2 would have found it). They are integers in
  tables seen as floats, or values read by 0.695-new functions [I]. 285 (BufferDistanceToNonfriendlyClaim) comes
  from StringHashToValue.txt:162, unchanged in 0.695. The 0.589 exe's own default is 250 (0.589 DAT_14511fd88,
  shown by Ghidra as `PTR_LAB_14511fd88._0_4_`; 0.695 DAT_1451f95c4 = 250), clamped to 100-400 (DAT_1446b8e38 = 400,
  DAT_144689f6c = 100). All three values are the same in both builds [P].
- **Fix:** none. L222 can be closed: "every paired DAT read from both exes; no tuning value changed".
- **In-game check:** no.

### F2. Every client value the server hard-codes by DAT matches the 0.695 exe — OK-NOTE
- **Verdict:** OK-NOTE (no MISMATCH). Values read from the 0.695 exe [P], each the same in 0.589 at its paired address:
  - AdminCommands.cs:324 DAT_144c410f0 = 1.0f (run speed floor); :438 DAT_144722208 = 8.0 (f64, default rate).
  - Barbershop.cs:181 DAT_1446f40b8 = 60.0, DAT_144703b10 = 30.0 (f64). StringHashToValue 0.695 has the same 60 / 30.
  - Groups.cs:545 DAT_144c41210 = 200.0 (f64), BonusRange 200 (Groups.cs:548).
  - Props.Platforms.cs:34/100 DAT_144c4103c = 0.01f (PlatformSpeedUnit 0.01). Props.Platforms.cs:66 DAT_145471a98:
    .bss, its initialiser copies 0x1452B7990 = 0x7F800000 (+inf) [P]. Network.ExtrapolationLimit is 0 in both
    StringHashToValue files.
  - StoreBundles.cs:67 / docs/protocol.md:1110 DAT_14526873c = 1. MovementEffects.cs:49 DAT_1452667f0 = 271.
  - Teleport.cs:23 DAT_144714810 = 80.0 (f64). TeleportRequests.cs:24/61 DAT_1446f4800 = 10.0f; :29 DAT_14469adc0 = 5.0f.
  - Pets.cs:37 DAT_144692dc0 = 0.05f (seek snap distance; not used by the server, as stated).
  - LootPinatas.cs:56-60: the table value DistanceToPickupOwnerlessLoot = 5.0 is what the server uses. The 0.695
    fallback when the key is missing is 2.0 (DAT_144c411a8 in FUN_143127a10) and does not apply, because the shipped
    table has the key.
  - Claims.cs:45 BufferFromCentre 285 m = table value. Exe default 250 and clamp 100-400 are unchanged (F1).
  - All 28 `DAT_x = value` claims in trace/0695, trace/systems and LandmarkEmu match the exe they cite
    (agent-leads3-c.md:53 `= -2` is 0xFFFFFFFE as a signed int).
- The other 17 float / double server constants with client citations (Abilities ToolRange 30, Chat Say / Shout
  100 / 500, AreaDefinitions MaxSide 256, Health ReviveRange 8, Landscapes ArrivalHeight 1800, WeaponCombat charge
  1.2 / 0.15 s, SocketHeartbeat 29 / 60 s, Claims HeightSlack 2, …) are documented as the server's own choices.
  Where a client number is cited, it is a code literal (e.g. keepAliveDelay 29000 in FUN_143a0a980) or a
  StringHashToValue row, not exe data. Code literals were covered by the earlier code-diff passes.

### F3. Claims.cs:566-576 "its [0.695] tool code not re-read yet" — STALE (comment)
- **Verdict:** STALE. The claim-placement tool is proven unchanged [P]:
  - 0.589 FUN_1435afc20 → 0.695 FUN_143662a00, FUN_1435b0b20 → FUN_143663900, FUN_1435b14a0 → FUN_143664280 and
    FUN_1435aef40 → FUN_143661d20 have code `same`. FUN_1435b0150 → FUN_143662f30 has code `same`, status changed
    (offsets only).
  - The defaults the tool reads the table keys into (0.589 DAT_1451b4d00 / 4d04 → 0.695 DAT_14528f6d0 / 6d4) are
    100.0 / −100.0 in both.
  - ClaimPlacement.TerrainFollowHeightOffsetMin / Max (−75 / 150) and ClaimCursor.ClaimSize are not among the
    StringHashToValue rows that differ between 0.589 and 0.695.
- **Fix (comment only):** replace "(Code read in 0.589; the 0.695 constants are the same, its tool code not re-read
  yet.)" with the 0.695 partners above and "code and defaults identical in 0.695 [P]". Also replace 0.589
  FUN_1435afc20 / DAT_1451b4d04 / DAT_1451b4d00 with 0.695 FUN_143662a00 / DAT_14528f6d4 / DAT_14528f6d0, or label
  them "0.589".
- **In-game check:** no.

### F4. Server comments that cite 0.589 DAT globals without a "0.589" label — STALE (comments; extends phaseE3-stale.md F4)
- **Verdict:** STALE, low priority. phaseE3-stale.md F4 covered 0.589 *function* addresses. Its srcscan pass did not
  cover *data* addresses. These DAT citations are 0.589 addresses. Each 0.589 address is followed by its 0.695
  partner [P, pairs2.tsv]:
  - ArtTints.cs:12: DAT_145351d90 (TintDefinitionManager) → DAT_145431310, with the 0.589 functions FUN_142c6ec20 /
    FUN_143c113c0 / FUN_143c10110 → FUN_142d08480 / FUN_143cd0780 / FUN_143ccf6f0. ArtData.cs:14 names the
    TintDefinitionManager DAT_1454313b8 instead (a 0.695 address; its 0.589 partner is DAT_145351e38), so the two
    files name different globals for the same manager. Unlockers.cs:23 uses DAT_145431310 +0x2980 for the tint hash
    map. [I] One of the two names is wrong; settle it by reading the global loaded before the call in FUN_142d08480's
    assembly (Ghidra lost the argument).
  - Chat.cs:220: FUN_1432b7bc0 / DAT_145352490 → FUN_143364980 / DAT_145431a10. This is the same global that
    Crafting.cs:307 cites as DAT_145431a10.
  - PlayerUpdateLightweightToFullNpc.cs:23-24: FUN_142bcdf40 / DAT_14511a190 → FUN_142c67960 / DAT_1451f31c8. The
    value is 0xFFFFFFFF in both.
  - BlockData.cs:17 and VoxelTemplate.cs:375: DAT_14552de88 → DAT_14560e128 (.bss). The 0.695 FUN_143f52470 cited
    next to it reads neither.
  - InstancedGames.Editor.cs:13 / :343: DAT_145358b90 / DAT_14535a748 → DAT_145438110 / DAT_145439cc8. The same file
    also cites 0.695 functions (FUN_1434f39a0 is 0.695-new).
  - TerrainCellPendingRequestTimeout.cs:9: DAT_1453743d8 (0.589; TerrainCellClientCellRequest.cs:27-28 labels it
    and gives the 0.695 DAT_145453e88).
- **Fix (comments only):** label each one "0.589" or replace it with the 0.695 partner above. Add a DAT pass to the
  citation fix in phaseE3-stale.md F4 step 1, using pairs2.tsv as the address map for data.
- **In-game check:** no.

## 2. Checked and fine

- All 233,295 function pairs; 71,166 with data references; 73,784 DAT pairs. 14,444 initialised pairs compared,
  none changed in a referenced value.
- 4,435 runtime-initialised global pairs (literal stores): none changed. Writer-function pairing: none changed.
- 49 same-code pairs with unequal DAT counts, re-aligned with difflib: only boolean neighbour bytes differ. The 47
  of the PTR / UNK pass were not re-aligned. 32 of them are among the 49. The other 15 have equal DAT_ counts, so
  their DAT_ pairs are already in the DAT-only pass. Only their PTR / UNK references are unchecked (pointer data).
- DAT vs. literal in 5,238 changed functions: none differ.
- Vector constants (16-byte reads, 69 differing blocks): all differences are in the unreferenced neighbour half.
- Server-relevant closure (22,542 functions): 2,797 identical, 168 same in the first 4 bytes, 32 different. All 200
  are covered by the classes in F1.
- The 60 DAT addresses the server cites: 0.695 values verified (F2), or .bss / pointer globals with no static value.
- 28 `DAT_x = value` claims in research and code: all match.
- StringHashToValue.txt 0.589 vs. 0.695: the changed rows (camera heights, Gender / RaceChangeCost, ScriptCounter /
  LogicGate effects, GameFinder sound, Paste font size, CounterProp field name, AdvancedScripting binding text) are
  already handled (Barbershop.cs:181, phaseE3-ui.md F5, tables-diff.md row StringHashToValue). The claim, loot-pinata,
  network and buffer rows the server uses are unchanged.
- Post-0.589 patch notes (trace/0695/patchnotes.tsv, 2016-06-04 onward): no note announces a client tuning change.
  The numbers they give (dynamite 100 s, trial key 10 days, …) are server or table rules.

## 3. New leads

### E4-C1. Which global the TintDefinitionManager is (ArtTints.cs vs ArtData.cs)
- **Need:** one name for the manager whose lists the ArtData 0x116 handler replaces. ArtTints.cs (0.589
  DAT_145351d90 → 0.695 DAT_145431310) and ArtData.cs (0.695 DAT_1454313b8) disagree.
- **Searched:** the decompile of 0.695 FUN_142d08480 shows `FUN_143cd0780()` with no arguments (Ghidra lost them).
  FUN_143cd0780 works on param_1 / param_2 offsets only. DAT_1454313b8 is assigned in its initialiser and read
  through +0x70 hash buckets.
- **Try next:** disassemble 0.695 FUN_142d08480 around the call to FUN_143cd0780 (Ghidra MCP `disassemble`, or the
  Ghidra project re/Landmark695) and see which global goes into rcx. Comment-only either way.

### E4-C2. Data read only by 0.695-new functions
- **Need:** the 3,013 0.695-new functions have no 0.589 partner, so their data values cannot be compared. Any tuning
  value they read is new behaviour.
- **Searched:** the whole-section round-value list (F1) gives the candidates that exist only in 0.695.
- **Try next:** only if a new-function trace needs it: list the DATs read by new functions that the server reaches
  (rel.tsv ∩ functions.tsv status `new`) and read them with rd.py. The traces of new systems (phaseE2/E3) already read
  those functions' code.

## Summary

Every data reference in every paired 0.589 / 0.695 function was aligned by position and the bytes were read from both
executables. That covers 14,444 initialised constants, 4,435 runtime-initialised globals (by their initialiser
literals), constants Ghidra showed as a literal in one build only, and a whole-.rdata check of round values. Not one
tuning value (range, cooldown, limit, speed, distance, price) held as data changed between 0.589 and 0.695. The only
differing bytes are switch tables, boolean neighbour bytes, misaligned pairs and an engine geometry table. The server
therefore uses no stale 0.589 data value, and none is missing. Every client value the server hard-codes by DAT (run
speed floor, barbershop prices, group bonus range, platform speed unit and extrapolation limit, store type, movement
"none" row, teleport distances) matches the 0.695 exe. Research claims of the form `DAT_x = value` were all confirmed.
What remains is comment hygiene: Claims.cs's "0.695 tool code not re-read" can now say "proven unchanged" (F3). Seven
comments cite 0.589 data addresses without a label (F4), and one of them disagrees with ArtData.cs about which global
is the TintDefinitionManager (lead E4-C1). L222 can be closed.
