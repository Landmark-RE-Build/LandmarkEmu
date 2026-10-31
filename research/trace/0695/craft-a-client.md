# Station crafting: what the 0.695 client does with the server's packets (task A)

2026-10-31. Read-only trace. Sources: `re/decomp_all_695` (fn.py), the client exe for one thunk and two constants
(`<0.695 install>\Landmark64.exe`, capstone), `server-data/worlds/dev/console.log` (the user's session,
container time = UTC, the client's own logs are UTC+11), the server code at 91c5a566. [P] = shown by the code or the
bytes. [I] = inferred.

## Verdict

**The client does not ignore any of the craft packets.** Every RecipeBase, ClientUpdateItemAdd, ItemUpdate and
ItemDelete the server sent in this session matches the 0.695 readers byte for byte, and the client used the items it
was given (below). **The Replicator never crafted at all: the server refused all 19 Replicator requests** with "You are
too far from the station." The Lumen Station (55) crafts (recipes 2386-2389, 5001) worked end to end.

Root cause of the Replicator refusals (server, not client) [P]:

- `Crafting.cs:245-246` (TryStart) looks the open station up with `Props.PositionOf(open.PropGuid)` only. That finds
  placed props only. The Spire's Replicators and its Repair Station are Spire template props (guids `0x53500001CC0000xx`
  in this log), which only `Spire.PositionOf` knows. So the lookup returns null and every craft is refused before
  anything else is checked.
- The window-close check right next to it already uses `StationPosition()` = `Props.PositionOf ?? Spire.PositionOf`
  (`Crafting.cs:125, 142`, the 2026-10-21 fix). TryStart never got that fix. The log shows this split: the Replicator
  window (station 53) opened at 07:15:07 (line 111424, sub 7 station 0x35) and stayed open (no "window closed" line
  after it), while every craft from 07:15:23 to 07:17:07 (lines 111548-112470: recipes 1574, 1442, 765) was refused.
- A second flaw in the same check: TryStart uses a flat 8 m from the prop's pivot. The close check uses
  `max(8, reach at open + 3 m)` (`Crafting.cs:127-128`). So a big placed station whose pivot is more than 8 m from where
  it is clicked can be opened but can never craft.
- The client shows this as "it tries to craft": the start path sets its crafting state before any reply (below). The
  refusal then resets that state and prints the text in chat.

What the next server step should do: in TryStart, use `StationPosition(open.PropGuid)` and the same limit as
CloseWindowsAway. Then retest the Replicator recipes 1574 (item 71 × 50 → item 2000 × 750) and 1442 (item 36 × 250 →
item 110 × 750). Both use only type-1 components, so TryStart supports them.

## 1. Client start path (what "it attempts to craft" is)

- Lua `Recipe.StartRecipe(recipe, count)` = FUN_1435ece70 → FUN_142f3e3b0 (the recipe manager, `DAT_145431a20 +
  0x7aa00`) [P luabind_695.tsv, part_0113.c:13970-14000].
- FUN_142f3e3b0 (part_0077.c:10936-11035) checks five things first. (1) The recipe's cooldown has passed (FUN_142f3bb30,
  recipe +0x44). (2) The open station (+0x180, set by sub 7) lists the recipe (FUN_142f37f30). (3) The recipe is known
  (FUN_142f3af30). (4) The components are present (FUN_142f3afa0). (5) The output fits in container 0 or 5
  (FUN_142f37e90 → FUN_142ca6900). Then it sends **RecipeStartRequest {u16 0x42, u32 sub 1, u32 recipe, u32 count,
  Array<u64 item guid> (empty)}**. Right after that it sets **+0xa8 = recipe and +0xb0 = now**: the client marks itself
  as crafting before the server answers [P]. A refining station takes the other branch (FUN_142f3cd40, Lua
  `RefiningStationAddProcess`).
- Component counts on the client side (FUN_142f3afa0) work as follows [P]. Type 1 counts stacks of that definition
  through FUN_142dbd420 with the filter lambda FUN_142f40960: **only containers 0, 1 and 5-8 count**, and 2-4 and 9-11 do
  not. Lumens (item 1579) are in container 8, so they count. Type 2 is counted by FUN_142fcd500.
- No distance check exists on the client side. The server alone enforces distance.

## 2. RecipeBase 0x42 S2C (dispatcher FUN_142f39870), what each sub does

Every reader below reads `u16 opcode, u32 sub` first. Each field is bounds-checked, and a short packet reads as zeros.

| Sub | Class | Layout after the sub | Client action | Server bytes in this log |
|---|---|---|---|---|
| 2 | RecipeStartResponse | bool ok, string msg | The message goes to the chat window (FUN_142f39150 → DAT_145431a10 vt+0x18; DAT_145431a10 is the "Main.wndChat" object, part_0066.c:9285). When **ok = 0**, FUN_142f37fe0 resets the craft state (+0xa8 = 0, +0xb0, +0x194 = 0, salvage slots, UI refresh FUN_14360df80) | `02000000 01 00000000` (ok, empty). Refusal: `02000000 00 21000000 "You are too far from the station."` [P match] |
| 4 | RecipeCancelResponse | bool, string | Prints the message and always resets (FUN_142f37fe0) | not in this session |
| 5 | RecipeCompleteResponse | bool, u32 value, string (FUN_142d4b5d0) | Prints the message, resets (FUN_142f37fe0), stores the u32 at manager **+0xac** | `05000000 01 54090000 00000000` (recipe 2388) [P match] |
| 7 | RecipeCraftWindowEnableResponse (FUN_142f2fee0) | u32 result, then only if result = 0: u32 station, u64 station guid | result 0: +0x180 = station, +0x188 = guid, FUN_142f3c630(…, 1), Lua `GameEvents:OnOpenCraftingWindow`. result 5: text "Crafting.PrivateStation" to chat, window closed | `07000000 00000000 35000000 0000000000000000` (the guid is always 0, CraftingStations.cs:150) |

**None of these subs touches the inventory** [P]. Resources going down and output arriving come only from ClientUpdate
0x29 subs 2, 3 and 4, plus the currency packets.

## 3. ClientUpdate 0x29 (dispatcher FUN_142cd9980), the item packets

### sub 2 ItemAdd: reader FUN_142c1c880, case 1

- Packet: `u16 0x29, i16 2, i32 len, byte[len]`. The packet must end exactly at the end of the blob (`end - pos < 1`)
  [P].
- Blob: `u8 hasDefinition`. When it is non-zero, an LZ4 definition (FUN_142c19850) follows and goes into the cache
  (FUN_143402db0). Then FUN_142fcf1f0(inventory, reader, def):
  - FUN_142f20190(player inventory manager +0x753d8, reader) reads **one ClientItem record** with FUN_142c208e0. The
    order is: base FUN_142c20f60 {u32 def, u32 tint, ItemRandom ×2 (FUN_142c20e60: 5 u32, stat list, catalyst list),
    u32 cost multiplier}, u64 guid (+0xc8), u32 count (+0xd0), u32 consumed (+0xd4), u32 +0xd8, bool auto-resupply,
    u32 container (+0xe8), u64 inventory id (+0xf0), u32 slot (+0xf8), u32 owner type (+0xfc), u64 +0x100, u64 chest
    owner (+0x108), u32 chest slot (+0x110), u32 wear (+0x148), u8 flags (+0x14c), u32 since last cast (FUN_142c325d0),
    bool + rental block, bool + design block, metadata (FUN_142c32650), u32 timed delete (+0x138).
  - FUN_142f20190 then adds the record with FUN_142c98950. The container used is the record's container when owner
    type = 1, else 0xe. **A container of 12 or more fails with result 3**. Any failure logs "HandleAddItem--Failed to add
    item to inventory index %d, slot %d, of stack size %d.". The add callback (lambda vtable 1446e18a8, FUN_142f27120)
    calls the item's vt+0x38 = FUN_142e46670 on the same reader. That reads **one more byte into item +0x161**, and the
    callback then sets +0x160 = 1 [P].
  - The "ItemReceived" Lua event fires only when a definition was embedded **and** def +0x10c ≠ 0. +0x10c is field
    u23_32[9] in re/items_decode.py, and it is 0 on all 2,885 dump definitions. So no "item received" popup is expected
    for crafted items, and its absence is not the bug [P].
- Server blob: `ClientUpdateItemAdd.cs` writes `00` + `InventoryItem.Write` (record + the extra byte, since owner 1 and
  container < 12). All 40 distinct ItemAdd bodies in console.log were parsed field by field (scratch parser). Each is
  147 = 1 + 145 + 1 bytes, with nothing left over, owner 1, and containers 0 or 5. Example, recipe 2388 at 07:22:31:
  def 3712, quality 2, cost ×4.0, guid 0x10000062, count 1, container 0, slot 8, last cast 0x7FFFFFFF, extra 0
  [P match].

### sub 3 ItemUpdate: reader FUN_142c1ca60, handler FUN_142fcf620

- Packet: `u16, i16 3, u64 guid (+0x18), u32 count (+0x20), u32 consumed (+0x24), u32 (+0x28 → item +0xd8), u64
  (+0x38 → rental expiry, FUN_143b79e10), u32 slot (+0x2c), u32 container (+0x30), ItemRandom (+0x40), ItemRandom
  (+0x98), u32 (+0xf0), u32 wear (+0xf4)`. The case-2 check requires an exact length [P].
- Handler: looks the guid up (FUN_142ccf6e0). **An unknown guid is dropped silently** [P]. Otherwise it sets the count
  (vt+0x18), consumed, rental fields, **copies the whole random block (quality at item +0x10, the cost multiplier
  re-derived by FUN_143b780f0)**, random2, the stat and catalyst lists, and wear. Then:
  - if the container and slot match the item's, it calls vt+0x28 = FUN_142f22b30(manager, item, +0xf0). That refreshes
    the bag, the action bar, the def counters (FUN_142f223a0) and the **recipe manager** (FUN_142f3c6e0 with the def id,
    so the crafting window recounts).
  - if either differs, FUN_142fd3ee0 moves the item.
- Server: `ClientUpdateItemUpdate.cs` = 100-byte body. **All 518 ItemUpdates in the log are 100 bytes = the reader's
  104 minus the 4-byte header** [P match]. The Lumen stack update reads `guid 0x01000337, count 9 999 435, container 8,
  slot 0`.

### sub 4 ItemDelete: reader FUN_142c4eb60, handler FUN_142fcf500

- `u16, i16 4, u64 guid`, exact length. It searches containers 0-11 for the guid and removes it. Server bodies are all 8
  bytes [P match].

## 4. Proof the client accepted the Lumen Station output

Right after each ItemAdd, the client sent Inventory 0x2D sub 0x15 ActivateItem {u64 guid} (ItemUses.cs header) for the
same guids. The server then deleted them: Vault expansions 0x1000005f-61 (07:22:27-29, "Vault storage grew 60 → 120")
and Design expansions 0x10000062-65 and 0x10000069-6b (07:22:35-07:24:08). A client can only activate a guid it holds,
so those items were in its bag [P]. The x3 / x7 / x1 refusals "Error: Failed item requirement" (lines 114557-114595,
115054, 115136-115180) are the server's own cap on expansions (`Crafting.UnlockRefusal`, max 120 vault and 48 M
design), not a client problem.

## 5. Smaller client-side findings

1. **ItemUpdate erases the item's quality** [P]. The server writes both random blocks as zeros
   (ClientUpdateItemUpdate.cs:55-62), and FUN_142fcf620 → FUN_143b780f0 copies them into the item. So after the first
   update, a quality-2 stack (for example Cozy Campfire 3894 ×20 → ×40, guid 0x1000006c, 07:24:11) shows quality 0
   (RefItemQuality row 0) with cost ×2 instead of ×4. Its client-side stacking compare (FUN_143b77f90 reads ItemRandom
   +0x0 / +0x4) also stops matching new quality-2 adds of the same item. Fix: send the item's real quality / cost block
   in ItemUpdate.
2. **The client can close station windows by distance itself, but the server never arms it** [P]. Sub 7's u64 goes to
   manager +0x188. On open, FUN_142f3c630 → FUN_142f3e640 checks it against the "none" guid DAT_145444f20 (.bss).
   When it is set, the client registers a proximity watch (DAT_145431440, FUN_143b7d030) on that entity. The radius is
   entity +0x4118, or the global int at +0x326d0 when that is 0 (FUN_142ea0960), times 1.3 (DAT_1446ccf78). The callback
   LAB_142f3d380 is `jmp FUN_142f381b0`, which sends CraftWindowEnableRequest 6 and closes the window (exe disassembly).
   The server writes 0 there (CraftingStations.cs:150), so the watch is never armed. This contradicts the summary at
   Crafting.cs:22-24 ("the client never closes a station window by distance itself"). Sending the station prop guid in
   sub 7 would let the client close the window the way live did [I that live sent it; P that the client uses it].
3. Sub 5's u32 is stored at +0xac. This log sends the recipe id there. Nothing found reads +0xac yet (open question
   below).

## 6. Containers the client accepts or counts (ClientPlayerInventoryManager)

- Add: owner type 1 → the record's container, else 0xe. Containers 0-11 are accepted, and 12+ fail ("HandleAddItem--
  Failed"). Delete searches 0-11.
- Crafting component counts include containers 0, 1 and 5-8 (FUN_142f40960). The output-space check covers containers
  0 and 5 (FUN_142f37e90 in FUN_142f3e3b0).
- `FUN_142f22b30` additionally runs "Quantity" handling (FUN_142f1f850) for containers 5-6, and sets a flag at +0x1ec
  for container 2.

## Open questions

- Who reads RecipeManager +0xac (sub 5's u32)? Next: grep decomp_all_695 for `param_1 + 0xac)` inside the
  ClientRecipeManager functions FUN_142f3*-FUN_142f40*, and in the Lua bindings for Recipe.* (re/out/luabind_695.tsv).
- Is DAT_145444f20 0 at run time (so the sub 7 u64 = 0 means "none")? It is .bss in the exe. Next: find its writer
  (grep `DAT_145444f20 =` returned none, so look for a static initialiser that stores through a register), or test once
  with the station guid in sub 7.
- Does RefItemQuality.ITEM_CRAFTED_FX / SHOW_NOTIFICATION play on a craft (the live "item crafted" effect and toast)?
  No reader was found on the sub 5 path. Next: xref the RefItemQuality loader's fields +0x?? for those two columns.
