# Phase E2: rebase of the written traces from the 0.589 client onto the 0.695 client (agent pe2-rebase)

2026-10-26. Brief: trace/0695/agent-phaseE2-brief.md. Outputs:

| File | What |
|---|---|
| re/tools/rebase695.py | the tool (collect citations → build per citation → 0.695 partner → compare; `-r` review dump, `-d` one diff, `-w` write-up) |
| re/out/rebase695.tsv | one row per (file, cited function): build, 0.589 / 0.695 address, status, cited lines, names, diff details |
| re/out/rebase695_summary.txt | counts and per-file majorities |
| re/out/rebase695_verdicts.tsv | the hand-written verdict of every changed / unpaired function (input of `-w`) |
| trace/0695/rebase-695.md | verdicts grouped by trace file |
| trace/0695/address-map-589-695.md | one table: every cited function, 0.589 → 0.695 → status → files |
| branch local/pe2-rebase (worktree wt-pe2-rebase), commit 3d6b1e1 | the one server gap found (G1) + three doc fixes |

A one-line note pointing to the map and the verdicts was added under the title of all 39 trace files that cite
0.589 addresses (trace/systems ×14, trace/architecture ×12, trace top level ×13; mixed-build files say so). The traces'
text was not changed otherwise. LandmarkEmu/docs files were not given the note (they are server docs, not traces;
their 0.589 citations are in the map like the rest).

## Method

1. **Citations.** Every `FUN_14xxxxxxx`, `thunk_FUN_…`, `0x14xxxxxxx` and bare `14xxxxxxx` in trace/systems/*.md,
   trace/architecture/*.md, trace/*.md and LandmarkEmu/docs/*.md that is a function start in the 0.589 index
   (re/decomp_all/index.tsv) or the 0.695 index (re/decomp_all_695/index.tsv). 3,636 citations (file × function) in
   59 files; 128 other hits (vtables, data, mid-function labels) skipped.
2. **Build per citation.** Start in one index only → that build. Start in both and functions.tsv pairs it with
   itself → "both" (counted as 0.589). Start in both, different functions → the nearest "0.695"/"695" or
   "0.589"/"589" on the same line (80 characters before, 20 after), else the file's majority of unambiguous
   citations, else 0.589.
3. **Partner.** trace/0695/functions.tsv. For the 38 cited 0.589 functions it leaves unpaired: **caller alignment**
   (25: the 0.589 call to the function in a paired caller and the 0.695 call at the same place of the aligned caller
   bodies — this replaced a first body-similarity "twin" search, which paired 7 PlayerUpdate wrappers wrongly), by
   hand from a text both builds share or a replaced table (10, listed in `MANUAL` in the tool with the evidence), an
   identical body after the signature line (2), none (1: FUN_1434411d0, the 0.589 Game Table settings page, rebuilt in
   0.695 from RulesetObjects data; docs/client-to-server-shifts.md row C).
4. **Comparison** with rcmp.py's helpers (`norm`, `norm_wire`, map `M`, loaded from re/out/agent_coverage_e/rcmp.py):
   the function and its depth-1 callees (FUN_ and named calls; identical-code callees accepted as the same call).
   `same` = equal under `norm`; `same-normalised` = equal only with struct offsets / numbers folded; `changed`;
   `unpaired`; `new-in-695` (cited 0.695 function with no 0.589 partner).
5. **Review.** `rebase695.py -r` writes, per changed / unpaired function, the citing trace lines, the server files and
   0.695 traces that already cite the 0.695 address, the strings only in one build, and a readable diff (callees
   renamed to their 0.695 partners, numbers folded to find the hunks, the numeric changes listed). Every one of the
   347 functions was read (both decompiles where the diff was not self-explaining) and given a verdict.
6. **Same-normalised constants** (not required, checked anyway): the numeric changes of all 150 same-normalised 0.589
   functions were listed; every small-number change was looked at (see "Constant changes" below).

## Counts

| | same | same-normalised | changed | unpaired | new-in-695 |
|---|---|---|---|---|---|
| 0.589-cited functions (2,009) | 1,522 | 150 | 336 | 1 | – |
| 0.695-cited functions (585) | 438 | 68 | 53 | – | 26 |

Verdicts (347 changed / unpaired 0.589 functions, counted per (0.589, 0.695) pair): **169 no server effect**, **177
server effect — the server already matches**, **1 server effect — gap, fixed** (G1). 106 of the 177 are the
PlayerUpdate u8 → u32 sub-opcode, 13 the commerce +1 sub shift.

## Server-effect findings and outcome

All matched already unless marked; per-function rows in trace/0695/rebase-695.md.

1. **PlayerUpdate 0x26 sub-opcode u8 → u32** in every reader / wrapper / the dispatcher (PlayerUpdate_Dispatch
   FUN_142c45fa0 → FUN_142cdfa60) and the C→S header writer (FUN_142b79bb0 → FUN_142c13350) — server writes / reads the
   u32 sub (protocol.md §1.6, OpcodeCodecTests). PU body changes found with it, all built: 0x1B CustomizationChange
   (bool first, attachment list; Barbershop.SendLookChange), 0x79 DisplayPropText (+u64, +bool; Props.cs), 0x80
   SetPlayerBio sent by the client at most every 1.5 s (FUN_142fce9a0; PlayerUpdateSetPlayerBio.cs).
2. **Commerce 0x45 subs ≥ 0x30 +1** (FollowCreator, RequestAccountStore, Recommendations, GetProductForReview,
   SubmitProductReview, AccountStore, ModifyScreenshots 0x49 → 0x4A, PointOfNeedPrompt 0x47 → 0x48) — native 0.695
   numbers in Commerce.cs / StaffQueue.cs / SiteScreenshots.cs. Player Studio template products: the client checks
   the design's template data itself (FUN_143258720 result 9, FUN_143262c00 kind 2) — Commerce.cs / StudioRules.cs.
3. **Mail 0xF8 subs +1** (FUN_1432c9770, FUN_14358a220 constants) — protocol.md, Mail.cs.
4. **New result codes / texts the server sends:** harvest EffectEnd reason 9 ClaimDestruction.Fail.NotAllowed
   (VoxelDestruction.cs); PropBase PickUpReply error 0xE PartialLootChest (RewardChests.cs); Inventory 0x2D/0x1E
   ShowActivationFailure (ItemUses.cs); AddBinding result 4 BindSingle (StoryTools.cs); Grapple.AdventureBlocked /
   Claim.NoCameraPlayMode through the Play Mode Rules flags (Claims.cs ClaimInfoUpdate kind 0x17, claim record u64).
5. **New C→S messages the server handles:** ClaimsPacketPlayerEnteredClaim 0xF9/0x63 (FUN_142dc8ec0; Claims.cs),
   Template 0x1D / 0x1F purchase design materials (new dispatcher FUN_14310bbf0; DesignMaterials.cs).
6. **Body changes (all per-build in the server):** TemplateBasicData without PropData list (Templates.cs); ClaimRecord
   and site-game struct, ClaimInfoUpdate trailing u64 (Claims.cs); SendSelfToClient bio string (SendSelfToClient.cs);
   settings records f64 (PropSettings.WriteRecord, TASKS 2.18); InstancedGame Event tail (2 strings, text id,
   pointCount) and event types 10 / 11, HUD entry bool + u64, settings map value with two trailing u32s, matchmaking
   update 0x1A, CanSwitchTeam / AllowSwitch, team names from InstancedGames_TeamName (InstancedGames*.cs,
   MatchmakingMessages.cs); WorldEditing 0x10C/8 PlayEffect (StoryTools.cs / VoxelDestruction.cs).
7. **Rules:** the 0.695 hostility test FUN_142ea9630 (neutral entities, instanced-game teams incl. pets' owners,
   FFA from the PvP setting) — InstancedGames.cs / Rework.cs / PlayerUpdateSetDisposition.cs follow it; Play Mode
   Rules gate camera modes (bits 3 / 4 / 5) and grapple (bit 0) — flags sent.
8. **Data table columns** the 0.695 client reads and the server uses: IgRuleTypes INTERNAL_DESCRIPTION /
   IS_PLAYER_ADDABLE, IgGeneralSetting NAME_STRING_ID / DATA_TYPE / STEP_SIZE / CLIENT_REQUIREMENT_ID / SORT_ORDER,
   EditableProperties BLOCKS_USER_MODIFICATION / IF_NODE_CAPABLE (IF_DISPLAY_STRING_ID is a client-side label),
   CharacterCreateOptions UNLOCKING_ITEM_ID, CharCustomizationSlotCost / CharCustOptionMult (Barbershop.cs).
9. **G1 — gap, fixed (local/pe2-rebase 3d6b1e1).** RemoveAddBinding (GameScripting 0x16 → reply 11): the server
   deleted the old Link before trying the new one and left it deleted when the new one was refused, while the
   client's reply handler (0.695 FUN_142fef9b0, 0.589 FUN_142f4fb80) only tests result == 0 — on failure it says "Link
   modification failure." and keeps showing the old Link [P]. Now `StoryTools.ReplaceLink` takes the old Link out,
   tries the add, and puts it back unless the new one is stored; a missing / not-editable old Link answers 4
   (RemoveFailure) and a single-binding refusal 5 (BindSingle), the numbers of the reply's 0.695 result table
   FUN_142f47140 (which itself has no caller in 0.695) [P]. Tests: Rules/StoryToolReplaceLinkRuleTests (4).
   `dotnet test LandmarkEmulator.sln`: 1,968 passed, 0 failed, 0 skipped. Needs an in-game check (TESTING-NEEDED):
   edit a Link to a target that refuses it — the old Link must still work.

**Doc fixes in the same commit:** docs/instanced-games.md Event row (0.695 tail, types 10 / 11) and the settings-map
value (two trailing u32s, FUN_1434d6b10; the old line described the 0.589 reader); docs/story-tools.md AddBinding /
RemoveAddBinding result codes.

## Constant changes in same-normalised functions

Checked every small-number change: commerce subs +1 (14 functions), mail subs +1 (FUN_1432c9770, FUN_14358a220),
ModifyScreenshots 0x49 → 0x4A (FUN_143531e50 / f40 / FUN_143533660), VoxelConstruction result 0x11's
PointOfNeedPrompt 0x47 → 0x48 (FUN_1430b6560) — all matched. One unhandled message changed:
**ClaimsPacketSetSupplementalData 0xF9/0x3E** (FUN_142d34590 → FUN_142dd1e50, FUN_142d346d0 → FUN_142dd1f90) accepts
index < 0x19 in 0.695 (< 0x18 in 0.589) [P]; the server does not handle 0x3E (parked in L140) — see the OPEN-LEADS
text below.

## OPEN-LEADS text

- For **L140** (parked C→S messages), add: "- **2026-10-26 (phaseE2-rebase.md):** claim supplemental data
  (ClaimsPacketSetSupplementalData 0xF9/0x3E, senders FUN_142dd1e50 / FUN_142dd1f90) takes index 0-24 in 0.695 (0-23
  in 0.589) [P]; if it is built, accept index < 0x19."

## New leads

### L-R1. trace/families_695 cite 0.589 case-function addresses
- **Need:** trace/families_695/*.md (generated 2026-10-26 "from 0.695") list dispatcher case functions with 0.589
  addresses (e.g. 0026_PlayerUpdate.md PlayCompositeEffect: FUN_142bce750 FUN_142bb2f10 FUN_142be4330 — 0.589;
  0.695 partners FUN_142c4c780 …). Readers following them land in the wrong build.
- **Searched:** trace/families_695/0026_PlayerUpdate.md by hand; not in this task's file set.
- **Try next:** add `trace/families_695` to `FILES` in re/tools/rebase695.py and re-run; or regenerate the family
  files with the 0.695 addresses (the address map has every pair).

### L-R2. coverage-695.md "Emulator" column is stale for built packets
- **Need:** rows still say "not sent / not handled" for packets the server now handles: 0x26/0x1B CustomizationChange
  (Barbershop.cs), 0x2D/0x1E ShowActivationFailure (ItemUses.cs), 0xF0/0x1D-0x1F (DesignMaterials.cs), 0xF9/0x63
  PlayerEnteredClaim (Claims.cs), 0x10C/8 PlayEffect (StoryTools.cs, VoxelDestruction.cs).
- **Searched:** the rows above while checking the server for each verdict.
- **Try next:** re-run the generator of coverage-695.md (re/tools/coverage.py with the 0.695 inputs) against the
  current client-0.695 head.

## Runtime captures needed

None.

## Limits (so nothing is read as more than it is)

- Callees are compared to depth 1; a change two calls down is seen only where a trace cites that function itself.
- functions.tsv pairs each function once; identical-code wrappers left over were paired by caller alignment, which
  depends on the caller being paired and its call sites aligning.
- "same" means equal after normalisation (names, addresses, variables folded): data-address and struct-offset
  changes are not differences by design.

## Summary

re/tools/rebase695.py collected the 3,636 function citations in the 59 trace and server-doc files, decided the build
of each, paired every cited 0.589 function with its 0.695 partner (functions.tsv, plus caller alignment and hand
evidence for the 38 it leaves unpaired; only the 0.589 Game Table settings page has none, as already documented),
and compared each with its depth-1 callees using rcmp.py's normalisation: of 2,009 cited 0.589 functions, 1,672 are
unchanged or differ only in offsets, 336 changed and 1 is unpaired. All 347 changed / unpaired pairs were read and
given a verdict in trace/0695/rebase-695.md: 169 client-only, 177 that touch the server and already match (mostly the
PlayerUpdate u32 sub, the commerce / mail sub shifts, and 0.695 bodies, results and rules that earlier agents had
built), and one real gap — RemoveAddBinding lost the old Link on a refused edit — fixed with tests on
local/pe2-rebase (3d6b1e1), together with three outdated passages in docs/instanced-games.md and docs/story-tools.md.
trace/0695/address-map-589-695.md maps every cited address, and each 0.589-era trace file now points to both files.
Two housekeeping leads (families_695 addresses, stale coverage column) and one L140 addition are above.
