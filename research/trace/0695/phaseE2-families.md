# Phase E2 — packet-family diff 0.589 → 0.695 (agent pe2-families, 2026-10-26)

Work list: pair every class of trace/families (0.589) with trace/families_695 (0.695), report what differs in the
evidence fields, decide per class whether the server must do something different for 0.695, fix gaps or write leads.
Everything here is static (decompiles, the two installs read-only, server source); no client, server, Docker or git
push was touched. No code change was needed, so no worktree was created.

Files written:
- `re/tools/famdiff.py` — the diff tool (requested). `python re/tools/famdiff.py` → `re/out/famdiff.tsv` +
  `re/out/famdiff.md` (against the corrected 0.695 run, see L2); `--raw` → `re/out/famdiff_raw.tsv` / `.md` against
  trace/families_695 as generated.
- `re/out/pe2_families/` — `trace695.py` (trace.py run for 0.695 with three input bugs fixed → `families_695/`,
  `FAMILIES_695.md`), `uimap695.py` (uimap.py on the full 0.695 UI → `ui_server_map_695full.md`), `verdicts.py`
  (writes the verdict file and fails if any real-difference class lacks a verdict).
- `trace/0695/famdiff-695.md` — the verdict per class (131 classes + bulk verdicts for the 539 informative-only ones).

### L1. famdiff.py — what it compares
- **Verdict:** RESOLVED
- **Answer:** Classes are paired by name (1,461 in 0.589, 1,480 in 0.695; 1,459 paired, 21 only in 0.695, 2 only in
  0.589). Per class it compares direction, opcode, family file, server-status class, reader/writer presence, the field
  layout (leaf type + offset list), the Lua natives that send it and the UI using them (C→S), Lua events / calls, data
  sources, text keys, log lines and UI reacting (S→C), handler presence and the normalised handler / builder excerpt.
  Pure address changes are folded away (FUN_/DAT_/PTR_/LAB_ names, `ptr:Name_<addr>[_n]` suffixes, 0x14xxxxxxx
  constants, Ghidra locals). Artifacts of the generators are folded too, each proven:
  - 0.695 strings: the 0.695 catalogue prints `FUN_142d4b5d0 -> +X` with its inlined body where 0.589 prints `str +X`;
    FUN_142d4b5d0 = Stream_ReadString (trace/0695/functions.tsv: "same" as 0.589 FUN_142cb22d0, which pkt.py names
    `str`) [P]. Folded back to `str +X` (layout diffs 94 → 23).
  - trace.py prints at most 60 layout lines; ~10 per 0.695 string, so a long layout is cut earlier: only the common
    prefix is compared when either side hit the cap (23 → 15).
  - re/out/ui_server_map.md (0.589) cut each widget list at 70 characters ("CharacterCre"): cut names match their full
    0.695 name, nothing past the cut is compared; trace.py's sorted 20-item caps likewise.
  - The authority for renumbering and body changes is trace/0695/coverage-695.md §10 (compiled class constants in both
    builds, reader / template closure compare). famdiff joins it per class (fields `renumbered`, `cov_body`); the trace
    files' own opcode field mixes heuristics (0.589 messages.tsv with −k encodings like 0x100000005, empty 0.695 cells)
    and is kept as an informative field only.
  Output: famdiff.tsv (class, dir/opcode/family both builds, field, only-589 items, only-695 items) and famdiff.md
  (only-0.695, only-0.589, real differences, informative only). Result on the corrected run: 670 classes differ, 131
  with a real difference (21 new, 2 gone, 50 renumbered, 24 body / value changes, the rest evidence-field changes).
- **Server:** n/a (tool).
- **OPEN-LEADS text:** none (new tool).

### L2. trace/families_695 has three input bugs (corrected copy made)
- **Verdict:** RESOLVED (corrected output in re/out/pe2_families; trace.py itself not edited — outside my write area)
- **Answer:** [P] from re/tools/trace.py with `LMEMU_CLIENT=695`:
  1. `re/out/messages.tsv` is the 0.589 dispatcher map (subdispatch.py + subnames.py, 0.589 only) and is read in both
     modes (trace.py:322-336). In 0.695 mode it (a) overwrote the 0.695 (opcode, sub) of the 532 S→C classes it names
     with the 0.589 numbers (e.g. PacketMailConsumeAttachmentResponse shows 0xF8 "0x100000001" instead of 0x11), so
     no S→C renumbering was visible, and (b) looked the **0.589** case-function addresses up in re/decomp_all_695,
     where they are other functions or nothing: "data sources" / "UI reacting" came out empty (ClaimsPacketEditClaimItemData,
     GuildAddMemberPacket, FriendMessagePacket …) or carried unrelated strings.
  2. The 0.695 UI corpus was re/as3_695 only = the 29 new / changed SWFs (CLIENT-DIFF §4). No SWF was removed in
     0.695 (0.589 UI 165 .swf, 0.695 the same 165 + 9 new; listing of both installs' UI folders), so every unchanged
     window (TradeWindow, Vault, Journal, Hud, ClaimFeedback …) was missing from "UI reacting".
  3. re/out/ui_server_map_695.md (2026-10-17) was built the same way (as3_695 only, 70-char cut) and still carries
     0.589 coverage statuses, so "UI using them" lost most windows.
  Fix (re/out/pe2_families/trace695.py, uimap695.py): keep the coverage_695.md opcode, translate every case function
  through functions.tsv (addr_589 → addr_695), AS3 corpus = re/as3 overlaid per window by re/as3_695, full UI map.
  Effect: with the raw files 174 classes look different, 43 of them only because of these bugs (all their "gone"
  evidence is back in the corrected run).
- **Server:** n/a.
- **OPEN-LEADS text:** see new lead NL1 (fold the fixes into trace.py / uimap.py and regenerate).

### L3. Server verdict for every differing class
- **Verdict:** RESOLVED — 131 / 131 classes have a verdict in trace/0695/famdiff-695.md: 91 "server effect: matches
  already (file:line)", 40 "no server effect (why)", **0 gaps** for a 0.695 change.
- **Answer (highlights, all [P] unless marked):**
  - **New in 0.695 (21):** every C→S / S→C class is implemented with its 0.695 number: CharacterCustomization 0x11C/3-4
    (Barbershop.cs:33-115), Claims 0xF9/0x45, 0x46, 0x63 (Claims.cs:223-280), InstancedGame 0x10D/0xD, 0x18, 0x19, 0x1A,
    Request type 9 (Rework.cs:43-457, Finder.cs:79-609, InstancedGames.cs:972), Inventory 0x2D/0x1E (ItemUses.cs:69),
    Commerce 0x45/0x30 (Commerce.cs:87-97), SetPlayerBio 0x26/0x80 (C→S, MiscHandler.cs:731; the trace's S→C
    direction is a coverage_695 mis-file), Prop 0xED/0x1D (PropHandler.cs:101-109), Inspect 0x91/3 (Inspect.cs:43),
    Template 0xF0/0x1D-0x1F (DesignMaterials.cs), WorldEditing 0x10C/8 (VoxelDestruction.cs:230-256). The two
    NpcDialogResponse "classes" are HUD indicators, not packets.
  - **Renumbered (50):** Mail 0xF8, InGamePurchase 0x45, InstancedGame 0x10D, Claims 0xF9/0x60-0x61 — the server
    carries the native 0.695 numbers in every place (Mail.cs:36-40, MailService.cs:37-38, Commerce.cs:73-78/652,
    CommerceServer ClientPackets.cs:20-26, InstancedGames.cs:44-46, Editor.cs:37/334, ClaimInventory.cs:44,
    PropLimits.cs:30); the ones the server never sends are noted.
  - **Body / value changes (24):** all written / read in the 0.695 shape (audit-wire-a/b/c rows cited), or never sent.
    The two new client result codes are both produced: EffectEnd result 9 ClaimDestruction.Fail.NotAllowed
    (VoxelDestruction.cs:48, 205-215) and PickUpReply error 0xE partial-loot chest (Props.cs:1769-1806). The
    "new" 0xE text on DeleteReply is a trace artifact: case 8 of FUN_142fba9f0 has no error table (lines 295-321).
  - **Evidence-field changes:** new 0.695 senders all hit handled paths — InspectWidget tooltip requests for the
    inspected player's gear (InventoryHandler.cs:268-273), the modular build windows + Mesh.PlaceMesh/SelectMesh →
    ToolPacketActivate (Building.Tools.cs:68-92), DesignsWindow → VoxelDataRequest (Templates.cs:855), BarberShop UI
    event 33 → AppearancePurchase help (FirstTimeEvents.cs:146), InstancedGame.SwitchTeam / GotoProp (InstancedGames.cs:972,
    Travel.cs:66). The new ObjectScripting text BindSingle is AddBinding result 4, returned by StoryTools.cs:57/2366.
    InstancedGame point-grant texts are event types 10/11 (Rework.cs:135, 783). The Evac to Safety native differs from
    0.589 only in struct offsets; its teleports are handled (InteractionHandler.cs:214-220).
  - **Artifacts explained (no server effect):** luabind_695 natives that only return a vtable as a placeholder
    (UnFollowCreator; FUN_14324ff60 builds RequestAccountStore 0x45/0x37), the 3-function window of trace.py over a
    reordered case-function list (DisplayPropText, RemoveNotifications), the "Invalid" token matching any Lua file
    (chat-room replies), depth-limited layouts (Guild*, CommandPacketInteraction*), 0.589 coverage.md direction
    mis-files (ProductCreateResponse, WalletInfoResponse, PacketTeleportIntraZoneRequest).
  - **Informative-only (539 classes):** code-excerpt changes (readers equal per coverage-695 §10; 8 without a row
    checked one by one, incl. TemplatePacketEnvironmentListResponse whose elements are the 0.695 TemplateBasicData the
    server already writes), trace opcode noise, status-text changes (stale coverage_695 statuses), family-file names.
- **Searched:** trace/families, trace/families_695, re/out/pe2_families/families_695, trace/0695/coverage-695.md
  §2/§9/§10, re/out/agent_coverage_e (rcmp.tsv, casecmp.tsv, hdronly), trace/0695/audit-wire-a..e.md,
  trace/0695/functions.tsv, re/decomp_all_695 + re/decomp_all (FUN_142fba9f0, FUN_142d1ccf0 case 0x4c, FUN_142f47400 /
  FUN_142f46ed0 / FUN_142fee7e0, FUN_14324ab40 vs FUN_14319fc60, FUN_1435fdb70 vs FUN_14354b210, FUN_142f859b0 vs
  FUN_142ee6ce0, FUN_14324ff60, FUN_14310bbf0), re/out/luabind(_695).tsv, the 0.695 install's Lua / UI XML (read only),
  re/as3 + re/as3_695, gameData695 (CodeStringMappings), LandmarkEmu WorldServer / CommerceServer / SocialServer.
- **Server:** matches already (no change).
- **OPEN-LEADS text:** none (no existing lead covers the family diff).

## New leads

### NL1. Fold the 0.695 input fixes into trace.py / uimap.py, refresh coverage_695 statuses
- **Need:** trace/families_695 (and anything reading it) to show real 0.695 evidence: (1) trace.py must not read the
  0.589 re/out/messages.tsv in 0.695 mode as-is — keep the coverage_695 opcode and map case functions through
  trace/0695/functions.tsv (or build a messages_695.tsv from re/out/agent_coverage_e/sd_695.tsv); (2) UI corpus =
  re/as3 overlaid by re/as3_695 per window; (3) re/out/ui_server_map_695.md rebuilt on that corpus with
  luabind_695 / coverage_695 and without the 70-character cut; (4) re/out/coverage_695.md statuses are from before the
  2026-10-17 native renumbering ("sub NOT handled" for 0x45/0x37, 0x40, 0x32, 0x10D/0x16, 0xF9/0x45-0x46, 0x117/1 …,
  all handled today) and it misses two 0.695 classes (PlayerUpdatePacketMobMovement, SetSpawnerActivationEffect) and
  the opcode of 32 S→C classes; (5) luabind_695 attributes a vtable a native only returns as a placeholder
  (FUN_14324ff60 → UnFollowCreator). trace/UI-AUDIT.md inherits (4) (e.g. RequestAllCellsForClaim "handled", NL2).
- **Searched:** re/tools/trace.py:20-30, 185-215, 322-336; re/tools/uimap.py; re/out/messages.tsv,
  ui_server_map_695.md, coverage_695.md; both installs' UI folders.
- **Try next:** apply the replacements in re/out/pe2_families/trace695.py / uimap695.py to the tools (they are plain
  string patches), regenerate trace/families_695 + FAMILIES_695.md, re-run coverage.py-for-695 against today's server,
  then `python re/tools/famdiff.py --raw` should equal the corrected run.

### NL2. VoxelVision "all cells for claim" request 0xF0/0x12 is not answered
- **Need:** what the server must send for TemplatePacketClientGetInstanceDetailsRequest (0xF0/0x12, Lua
  VoxelOwnership.RequestAllCellsForClaim FUN_1435ff2e0 → builder FUN_1431138e0, used by VoxelVisionWidget). The client
  reads the reply TemplatePacketEnvironmentInstanceDetailsResponse 0xF0/0x13 in FUN_14310bbf0 case 0x11 (reader
  FUN_143105fe0: u16, u8, u64, then FUN_1430ff920 + TemplateBasicData FUN_1430ff530 (0.695 shape, §B4), cells
  FUN_143101860, a PropData list FUN_142fa7fb0) and hands it to FUN_14310b940. The server has no case for sub 0x12
  (Game/Templates.cs:850-871 handles 1, 4, 6, 0xA, 0x1B, 0x1D, 0x1F) and never sends 0x13; the gap is the same in
  0.589 (not a 0.695 change) and trace/UI-AUDIT.md lists the call as "handled" from the stale catalogue status.
- **Searched:** re/out/pe2_families/families_695/00F0_Template.md, coverage-695.md:2164-2165, luabind_695.tsv,
  LandmarkEmu (grep RequestAllCellsForClaim / InstanceDetails / sub 0x12 in Game/Templates.cs, Network/Handlers),
  0.695 hudstate.lua:4196-4202 / events.lua:672 (how VoxelVision opens), trace/UI-AUDIT.md §VoxelVisionWidget,
  OPEN-LEADS.md / TASKS.md (no entry).
- **Try next:** trace FUN_14310b940 (what it fills: VoxelOwnership data sources / Lua events) and the cell reader
  FUN_143101860; then answer 0x12 with the claim's template-instance cells in the 0.695 body (Templates.cs WriteBasicData
  :612 already writes TemplateBasicData in the 0.695 shape). Check whether VoxelVision is reachable in retail (how
  GameEvents.OnOpenVoxelVision is raised).

## Runtime captures needed
None.

## Summary
re/tools/famdiff.py pairs the 1,461 / 1,480 classes of the two family traces and reports every evidence-field
difference with address noise and generator artifacts folded away, joining coverage-695 §10 for renumbering and body
changes. Running it showed that trace/families_695 was generated with three 0.589-only inputs (the 0.589 dispatcher
map overwrote 532 S→C opcodes and pointed the evidence lookups at 0.589 addresses; the UI corpus held only the 29
changed SWFs), so a corrected 0.695 run was made under re/out/pe2_families and the fixes are written up for the
coordinator (NL1). On the corrected run 131 classes differ for real; each has a verdict in trace/0695/famdiff-695.md
checked against the server code including CommerceServer and SocialServer: 91 match already (with file:line), 40 have
no server effect, and no 0.695 change is missing on the server — all 21 new classes, all 50 renumbered subs, all 24
body / value changes (including the new EffectEnd result 9 and PickUp error 0xE) and every new 0.695 UI sender are
already handled. One pre-existing gap seen on the way, the unanswered VoxelVision request 0xF0/0x12, is new lead NL2.
