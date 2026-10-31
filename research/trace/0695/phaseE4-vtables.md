# Phase E4 (agent vtables): changes below virtual calls (lead L-D1 of phaseE3-deep.md)

2026-10-26. Brief: trace/0695/agent-phaseE3-brief.md (read-only hunt). Server code read at HEAD db90a5f
(client-0.695). Nothing in the server, docs, traces or OPEN-LEADS.md was changed. The 0.695 exe was only read.
Scratch scripts and outputs are private, in
`<user folder>\AppData\Local\Temp\claude\<workspace>\28d489f2-5945-4c82-ba0b-9796b72f5283\scratchpad\e4-vtables\`:
`vtslots.py` (full vtable slot lists from the PE), `facts.py` / `facts2.py` (statements and virtual call sites of every
0.695 function), `resolve.py` + `infer.py` (per-site resolution), `ptredges.py` (calls through `PTR_FUN_` code pointers),
`walk2.py` (the e3 walk with the new edges), `superset.py` (walk from every game-side vtable slot), `runsig.py` /
`sig.py` / `d.py` (logic-only diffs, the e3 tools with `_DAT_` / `_UNK_` / `uRam` folded), `rd.py` (read bytes at a
VA). Outputs: `vedges_all.pkl`, `globals_typed.tsv`, `super4.tsv`, `sig_all4.txt`, `virt3.tsv`.

## Method

L-D1 asked for the virtual calls (`(**(code **)(*obj + off))(…)`) that the e3 walk did not follow. I did it two ways.

1. **Per call site (as L-D1 proposed).**
   - Full slot lists for all 11,190 RTTI vtables, read from the PE. The 64-entry cap in rtti_695.tsv is not used (F1).
   - Receiver types from: vftable stores (`*V = X::vftable`, constructor calls, factories that return them), `this`
     of constructors and of vtable slot functions, stores of typed values into DAT_ globals, object fields
     (`*(T **)(this + N) = typed value`, also members of members) and return values. 4 rounds to a fixed point.
   - Result: **213 typed pointer globals and 32 static objects** (`globals_typed.tsv`), and 7,999 of 97,246 virtual
     call sites resolved. Merged with the simpler pass (`resolve.py`) and 984 non-library `PTR_FUN_` code-pointer
     edges, that is 9,905 + 984 edges.
   - Examples: claims dispatcher FUN_142dbecb0 → ClientStringProvider (DAT_145431178) +0x20 / +0x28 = FUN_142fdd650 /
     FUN_142fdeba0 and ChatWindow (DAT_145431a10 = PlayClient[0x64de], built in FUN_142d12360 by FUN_1430df2e0
     "Main.wndChat") +0x18 = FUN_1430dfec0. Others: DAT_1454311c8 PlayClient, DAT_145431080 ActorManager,
     DAT_1454319c0 ProxiedCharacterManager, DAT_1454319f8 / DAT_145431a00 ClientRequirementManager, DAT_1454319c8 /
     DAT_1454319d0 ClientAreaDataManagerClient, DAT_1454319b8 ClientAreaDefinitionManager, DAT_145431a50
     ClientItemDefinitionManager, DAT_145431a80 / DAT_145431a88 ClientVehicleDefinitionManager, DAT_145431428
     WordFilterManager, DAT_145431ac8 ClientBusinessEnvironmentManager, DAT_1454312d0 MissionDefinitionManager.
   - Re-running the e3 walk (same 21,297 packet roots, depth 4, same library and system cut-offs) with these edges
     reached **12,160 nodes instead of 10,879 (+1,281)**. Only 22 of the new nodes are changed logic functions, and
     17 of those were not seen before. All 17 were read (§2).
2. **Upper bound over every virtual target.** Any function that is reached only through a vtable is a vtable slot,
   or is below one. So I walked from **every slot of all 37,450 game-side vtable entries** (library namespaces such as
   Scaleform / physx / std / FMOD left out), depth 0 = the slot, then 4 levels down. The walk used direct callees plus
   the resolved virtual and pointer edges, with the same cut-offs as e3. It reached 69,501 functions. Of these,
   **1,271 are changed logic functions in game systems** (final.tsv kind not layout-only / same-logic /
   new-boilerplate). **524** of them were not already seen: not cited in trace/ or docs/ or in a server comment, not
   compared by the rebase, and not read in the e3 walk. They split into 351 with a 0.589 partner and 173 new.
   - The 351: `sig.py` diffs, with numbers folded and plain field inits, cookies and signature lines dropped. 153
     still show logic lines, and every one of those was read. Their `C` lines (same shape, changed constant on a
     compare / return) were checked one by one.
   - The 173 new: grouped by vtable class. Each class was checked against trace/ and docs/ by name; functions in
     unnamed or new game classes were opened.

## 1. Findings (by player impact)

**No MISMATCH and no GAP.** Below the virtual calls, nothing changed in a way the server sees: no new branch on a
packet field, result code, required field, limit or server-expected state. The per-site walk and the upper-bound walk
agree.

### F1. rtti_695.tsv (and rtti.tsv) list at most 64 slots per vtable — STALE (research tooling)

- `re/tools/rtti.py` build() stops at `while len(entries) < 64`, so every cached slot list is cut at 64 entries [P].
- Reading the PE shows **155 0.695 vtables have more than 64 slots, up to 241** [P]. PlayClient (0x144682458) has
  106, but its row in re/out/rtti_695.tsv lists 64. rtti.tsv (0.589) is built the same way (158 rows at exactly 64).
- So any `*obj + off` with off ≥ 0x200 cannot be looked up in the TSV. phaseE3-deep.md L-D1 / OPEN-LEADS.md:2427
  say the file "has every 0.695 vtable with its slot list"; that is only true up to slot 63. Earlier traces that used
  higher slots (agent-e-leads.md slot 83, OPEN-LEADS +0x2F0) counted from the vtable address, so they are fine.
- **Fix I would make:** in rtti.py, replace the 64 cap with "stop at the first non-.text entry or at the next
  vtable's locator qword" (what `vtslots.py` does), then rebuild both caches (RTTI_CACHE=… for 0.695). The rebuild
  reads the exe only. No in-game check.

### F2. Terrain engine: CBlockData's set-voxel slot now takes a flag mask — OK-NOTE (for hand-off #2)

**0.589** FUN_142a191c0 has `(…, material, nx, ny, nz, char overwrite, listener)` [P]:
- overwrite 0: the material is written only into an empty voxel (no material bit, or material −3 / 0);
- overwrite 1: the normal / offset bytes (+0x2ee00..02, flag bit 2) are also written.

**0.695** FUN_142ab18f0 (VoxelFarm::CBlockData@8 slot +0x0, also ContourOptionalBlockData@8) and FUN_142ab3130
(CBufferBlockData +0x0) take doubles and a **byte flag mask** [P]:
- 1 = write the material only if the voxel is empty;
- 2 = always write the material;
- 4 = write the offset only if the voxel has none (material −2, or material 0 with offsets 0x80 0x80 0x80, or flag
  bit 2 clear);
- 8 = always write the offset;
- an offset of (0, 0, 0) clears bit 2;
- material 0xFFFE = "no material change".

The brush entry VoxelFarmImpl +0x28 (FUN_143f9cea0 → FUN_144004c20, 0.589 FUN_143f40d30, 163 changed logic lines)
gained the mesh / stamp shape paths (modes 1 / 2). VoxelStampCursor +0x20 FUN_14378da60 gained a scale step.

Server: none today. The client computes the edit and sends the usual ApplyDiffRequest, whose writer is unchanged
(terrain-695.md §5; Mesh Shapes §1.5). **Fix I would make:** none now. If the server-side VoxelFarm port (generator /
paste, ruin-stamping.md §2) ever re-implements set-voxel, it should follow the 0.695 flag semantics above, not the
0.589 bool. No in-game check.

### F3. Fill brush minimum size 2 → 1 — OK-NOTE

VoxelFillCursor's slot +0x2e0 returned 2 in 0.589 (FUN_1436cb180). In 0.695 it returns 1 (FUN_14378ac10), the same as
every other cursor (FUN_1436aeaf0) [P]. The slot is the lower clamp of the brush size in FUN_1436acb60 [P]. The
server does not clamp brush sizes (no fill / size check in LandmarkEmulator.WorldServer) and only gets the diff.
Nothing to do.

### F4. A prop ignores "play effect" with the null effect id — OK-NOTE

ProxiedProp's interface slot (ProxiedProp@0x4b50 +0x0, FUN_1430aaca0, 0.589 FUN_1430049c0) now returns early when
the effect id (param_3) equals DAT_14544faa8. That global is zero-initialised .data and is never assigned by name in
the decompile, so it reads as 0 [P for the code / I for the value]. A server request for effect 0 on a prop now does
nothing (in 0.589 it went on to FUN_142d10eb0). No server path is known to rely on effect 0 meaning something.
Nothing to do.

## 2. Checked and fine

**Packet-root walk with virtual edges (17 newly reached changed functions):**
- FUN_1430320f0, FUN_143629830, FUN_142eae890, FUN_142d6a020, FUN_143043bb0, FUN_142ecafd0, FUN_143d823a0,
  FUN_143d826f0: 0 logic lines after folding.
- FUN_1432f5a00 (ClientRequirementManager +0x10): an init only.
- FUN_1435bd3c0: an array clear.
- FUN_142eacc60 (LocalProxiedCharacter +0x10): equipment tint / material asset (hash −0x347e659f), client render.
- New VoxelPasteCursor / PasteThread / PreviewGenerationThread slots FUN_1436ceb20, FUN_1436ccb70, FUN_1436cd370,
  FUN_143f8b580, FUN_143f8b790: the known paste engine (functions-695.md "Paste").
- FUN_1437ac450 (MeshPaletteEntry): Mesh Shapes.

**Upper-bound walk, by area:**
- **PlayClient / BaseClient vtables (106 slots):** 16 changed slots. Cited ones: +0x48, +0xf0, +0x138, +0x208, +0x218,
  +0x270, +0x288, +0x290, +0x2d0, +0x340. Layout-only: +0xf8, +0x2a8. 0 logic lines: +0x1b0 / +0x1c0 / +0x268 /
  +0x2e0 (FUN_142cd8dc0, FUN_142cd8f00, FUN_142d17e30, FUN_142cbe8f0).
- **AreaPropertyData\* (31 vtables, the area data the server sends areas for):**
  - Only slot +0x8 changed, for Teleporter / TeleporterBase / TeleporterClaimVisitation / Thrower / ChainThrustPad /
    MovingObject(ParamEditProxy) / MovingObjectWaypoint (FUN_143db30d0, FUN_143db3150, FUN_143db31a0, FUN_143db3210,
    FUN_143db2bc0, FUN_143db2e10, FUN_143db2d90, FUN_143db2e90).
  - That slot initialises the editor-parameter entries from the area row. The entry FUN_143db2ad0 / FUN_143db39b0
    went from float min / max / default (FLT_MAX, FLT_MIN, 5.0, 0x50 B) to double (DBL_MAX, DBL_MIN, 5.0, 0x68 B).
  - Every other slot is "same", so no reader or apply changed. MovingObject / Waypoint +0x8 are agent-e-leads.md
    item 3.
- **Building / palette:**
  - CategoryDefinitionManager +0x30 FUN_1434d4700 now also accepts "MeshCategory".
  - PaletteCollection +0x18 FUN_1434d2530 / FUN_1434d25a0 now accept palette type 7 (meshes, +0x100). Both are Mesh
    Shapes (CLIENT-DIFF / functions-695.md).
  - CategoryDefinitionManager +0x28 FUN_1434d20f0 (mesh map clear).
  - VoxelSelectionCursor +0x1c0..+0x1d8 (extra FUN_1436ea360 refresh).
  - ToolTriggerVolume FUN_143738220 / FUN_1436eec30 (extra +0x128 test, input / visual).
  - VoxelMeshCursor FUN_1437982e0.
  - ClientCategoryPaletteManager FUN_14374a8c0 (offsets).
- **Character / camera / HUD:**
  - FirstPersonLocalProxiedCharacter +0x258 / +0x388 / +0x488 and FUN_143337be0 (argument passing).
  - ControllerCharacterCustomization +0x240 FUN_142fe5b80 (an inverted select, same result).
  - GameStateCharacterSelect FUN_143097120 (pointer array → 0x98 B entries, same filter).
  - TargetProcessor FUN_142f7b0b0 (hit-normal flip removed, client picking).
  - Controller +0x1e0 FUN_142d67f60 (StatId lookups via the renamed accessor).
  - HUD: FUN_143726320 (`< 0x12` → `< 0x13`: one more indicator category, NpcDialogResponse); FUN_14343e9a0 (indicator
    type id 0x1F = NpcDialogResponseIndicator, mis-paired with a 0.589 0x1C getter); FUN_14342e330 (setting hash
    registrations); FUN_1434413c0, FUN_1434479e0, FUN_14342e090 / FUN_14342e100 (swapped pairs).
- **Designs:**
  - TemplateExportAsset / AdminTemplateTester FUN_143524a00, FUN_143528f20, FUN_143521eb0, FUN_143523130,
    FUN_1435241a0, FUN_143528660, ClientTemplateManager FUN_143105870 (the PropData-in-record move, e3 F2).
  - New ExportedTemplate\* / PaletteCollectionTemplates / TemplatePacketPurchase\* destructors and loaders (Daybreak
    Designs, already traced).
- **Props:**
  - LuaPropertyEditor PropertyDropDown option entry 0xC → 0x20 B, value read at +0x18 (FUN_1435c19d0, FUN_142fb1360,
    FUN_142fafe70); still the same int value.
  - PropertySlider FUN_1435c1920 (change test).
  - ProxiedProp FUN_1430aaca0 (F4).
  - PropProcessor FUN_142fb3830 (renamed call).
  - PropHelpWindow\* new loaders (spec #6).
- **Items / social / store:**
  - Gaq\* invite / request queue items (GuildInvite, FriendInvite, TradeInvite, PendingMail, TeleportToPlayer,
    TeleportToZoneRequest, SwapSeat, GroupApproval / Invite, ClaimExpiring, MarketplaceItemReviewRequest,
    GenericNoResponse, FUN_1433e6730..FUN_1433e6f30): the IString recompilation pattern (FUN_142d519b0) only.
  - HashList<ItemInstance> element size 200 → 0x170 (FUN_14308a9f0..FUN_14308aac0, inspect cache).
  - RecipeDefinitionManager FUN_143be7d20 / FUN_143be6f70 (one more map cleared).
  - Store data sources FUN_143703010 / FUN_143704b80.
  - FriendNodeGroup FUN_143548ec0.
  - InspectProxy +0x48 FUN_14308b480 (null check).
- **Site games:**
  - ControllerInstancedGameObserver +0x248 FUN_143064390 (string order).
  - TriggerData map FUN_1434de9a0 / FUN_1434df4d0 (functions-695.md finding 1).
  - New RuleRequirement / RuleType / RuleSetScriptProp / GeneralSetting containers and loaders (IgRule\* tables,
    storytools-695.md / datatables-695.md).
  - New requirement factory registrations under BaseClient +0xf0 (FUN_1432a49b0..FUN_1432a4bf0,
    FUN_143274330..FUN_1432746d0: the requirement list itself is already traced).
- **Area / data loaders:**
  - StaticAreaDefinitionManager +0x20 FUN_143d300c0 and FUN_1431afc80 (container rewrite).
  - EditablePropertyDefinitionManager +0x8 FUN_143dc0420 (EditableProperties / EditablePropertyValues loader
    restructured).
  - ArtTint\* loaders FUN_143093ad0, FUN_143ccd4f0, FUN_143ccd6c0, FUN_143ccd890, FUN_143db49e0 (renamed helpers).
  - New ArtTintGroupUnlock\*, CharacterCustomizationCost\*, CharCreateOptionEquality / CharacterCustomizationPreset
    containers (cosmetics-695.md, functions-695.md §9).
- **Claims:**
  - Claim / ClaimBase new slot callees FUN_142d97270 / FUN_142d975e0 (destructors).
  - ClientAreaClaimRestrictionBufferProperty FUN_142dc9390 (null check).
  - NonProxyGroupMemberIndicator FUN_143449940.
  - HashListMap element sizes FUN_142db67e0 / FUN_142db6800.
- **Other:**
  - BaseClient +0x268 → FUN_142d5c1c0 (biome sky influence now waits for the zone terrain, FUN_143155c80).
  - BaseClient +0xd0 → FUN_142ede230 (two more Lua table registrations: BugSubmission, RulesetObject).
  - ControllerLocalTravel +0x2f0 FUN_143054480 (map position helper, agent-l140.md).
  - ClientLuaInternals FUN_142edea90 (chat print native).
  - ClientPropMorphemeRequestStateBehavior FUN_143476b00 / FUN_143476bc0 → FUN_142ec03b0 (code moved into a helper).
  - Container trait returns (List<Product> / SearchBundleResult / Set<StoreBundleId> 1↔0, Array<int,N> capacities
    7 → 0xb, 0x33 → 0x20e).
  - UIDeepFontCanvas / UIDeepAssetCanvas / AppFx / DeepAssetHelper (engine).
  - VoxelFarmImpl +0x30 FUN_143f9da00 / FUN_1440084e0 (one more bool argument).
- **PTR_FUN_ code-pointer calls:** 5,138 edges, 984 below the library cut-off. They reach no changed function the
  other edges did not already reach.

## 3. New leads

### L-V1. Secondary vtables that Ghidra names after the class
- **Need:** an exact vtable address for `*V = X::vftable` stores in classes with more than one base. Ghidra gives
  every vtable of X the same name, so the per-site map (infer.py) takes X's primary one.
  - Example: DAT_14541d3f8 is `this + 0x70` of AppPhysics::PhysicsEnginePhysX (FUN_142b9e820). That is its
    PhysicsCommandFactory interface. infer.py types it as the PhysicsEnginePhysX primary vtable; resolve.py types it
    as the base constructor's class.
  - The upper-bound walk does not depend on this. Only exact call chains do.
- **Searched:** rtti_695 COL offsets (they give the offset, not which store); vftable names in the decompile.
- **Try next:** read the `lea reg,[rip+disp]` operand at each store site (re/out/agent_terrain/x64dis.py on the
  bytes at the function address, read-only) to get the vtable VA, then rerun infer.py.

### L-V2. Exact resolution covers 8 % of virtual call sites
- **Need:** exact packet → method chains for the other 89 k sites. Most receivers are parameters, list / listener
  elements, or results of untyped calls.
- **Searched:** intra-procedural typing plus fields / globals / returns (infer.py, 4 rounds).
- **Try next:** pass argument types into callee parameters (call-site argument → callee param_N) and type
  container elements from their Array / HashList template names. Only needed if someone wants exact chains, because
  §1 already bounds the result.

### L-V3. Function-pointer calls not through a vtable or a PTR_ global
- **Need:** about 9.8 k `(*(code *)expr)(…)` sites, where expr is a field, a parameter or a table entry: listener
  callbacks, luabind / Scaleform thunks, worker jobs.
- **Searched:** the `PTR_FUN_` form only (ptredges.py).
- **Try next:** type the expression's base the same way as L-V2 and read the stored pointer from the constructor
  that fills the field.

## Summary

L-D1 is closed. Two methods cover the virtual calls below the 0.695 packet handlers, C→S builders and sending
natives:
- **Per call site.** Singleton managers and their fields were typed from constructors and vftable stores, and
  `*obj + off` was mapped to the concrete slot using slot lists read in full from the exe. Adding these edges and the
  code-pointer edges to the e3 walk reached 1,281 more functions. Only 17 of them were changed and not already seen.
- **Upper bound.** A walk from every slot of every game-side vtable, 4 levels deep, found 524 changed functions no
  trace had looked at. All were diffed against their 0.589 partner or opened as new code.

None of them changes what the server sees or must answer. The real changes are:
- client-side engine work: the CBlockData set-voxel flag mask and the new brush / stamp modes. This is worth knowing
  for the server terrain port (F2);
- the fill brush's minimum size (F3);
- a null-effect guard on props (F4);
- editor-parameter metadata on area properties, which moved from float to double.

One research file is stale: rtti_695.tsv / rtti.tsv stop at 64 slots, while 155 vtables are longer (F1).
