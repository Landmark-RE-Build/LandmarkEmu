# L43 — VISIBILITY_REQUIREMENT_ID reader (agent-l43, 2026-10-18)

Static analysis of the 0.695 decompile (`re/decomp_all_695`) plus a byte scan of `<0.695 test copy>\Landmark64.exe`
(read only). [P] = client function + offset proven, [I] = inference.

## Verdict

**The client never reads RecipeDefinition +0x40.** VISIBILITY_REQUIREMENT_ID is loaded from Recipes.txt and then
ignored. The 0.695 client does hide recipes, but only through a **server-owned hidden-recipe set**. The server fills
that set in SendSelf and changes it with RecipeBase 0x42 sub 0x1F Hide / sub 0x20 Unhide. So the live server evaluated
the requirement and told the client which recipes to hide [I strong]. Our server already has the field
(`SendSelfToClient.RecipeData.Unknown2`) but always sends it empty, and it never sends 0x1F or 0x20.

No runtime test is needed for the reader question. T1 in agent-leads4.md §3 can be dropped, or replaced by the
check in §5 below.

## 1. What FUN_142b213d0 stores at +0x40 [P]

- `FUN_142b213d0(char*, int*)` is a plain atoi: it skips spaces, reads a sign and digits, and writes an `int`. It is
  the same parser the loader uses for ID, SORT_ORDER, ITEM_DEFINITION_ID and the other numbers. So **+0x40 is a
  plain int32**, not an object.
- `Crafting::RecipeDefinition` (vtable 0x144951720, ctor FUN_143cb6d30) has 3 virtual methods: Load
  FUN_143cb6e00, dtor FUN_143cb6d90 and GetId FUN_143cb6df0 (returns +8). None of them touches +0x40.
- The full layout from FUN_143cb6e00 and the ctor:

| offset | field |
|---|---|
| +0x08 | ID |
| +0x0c | SORT_ORDER |
| +0x10 | ITEM_DEFINITION_ID |
| +0x14 | ITEM_COUNT |
| +0x18 | REWARD_SET_ID |
| +0x1c | RECIPE_TYPE |
| +0x20 | RECIPE_GROUP_ID |
| +0x24 | RECIPE_RACE_ID |
| +0x28 | CASTING_TIME (float) |
| +0x2c | DEFAULT_LEARNED (u8) |
| +0x2d | ALLOW_CATALYST (u8) |
| +0x30 | STACK_SIZE |
| +0x34 | TRACKER_ACHIEVEMENT_ID |
| +0x38 | OVERRIDE_NAME_ID |
| +0x3c | COOLDOWN_TIME |
| **+0x40** | **VISIBILITY_REQUIREMENT_ID (int)** |
| +0x44 | CAN_CRAFT_WITHOUT_STATION (u8) |
| +0x48 | MAX_QUEUE |
| +0x50 | List\<int\> |
| +0x80 | hash key |
| +0x88 | hash next |

## 2. Every reader of RecipeDefinition [P]

- The definitions live in `Crafting::RecipeDefinitionManager` (vtable 0x1446ae4b8, ctor FUN_142c69db0). It holds a
  HashList\<RecipeDefinition\> at manager +8, with count at +0x10, buckets at +0x30, node key +0x80 and next +0x88.
  It is created in FUN_142cacd40 and stored at **Client +0x32260** and at DAT_1454312f0.
- **DAT_1454312f0 is never read.** A RIP-relative scan of .text finds only two writes: 0x142cae0c2 and the setter
  at 0x142d4c220.
- **The displacement 0x32260 appears 14 times in .text** (byte scan for `60 22 03 00`):
  - Construction and teardown: FUN_142c5cdf0, FUN_142cacd40 ×2, FUN_142cbada0 ×2.
  - **0x142f03dd0** is a getter `mov rax,[rcx+0x32260]; ret` that Ghidra did not decompile. It has **no** E8/E9
    callers and no 8-byte pointer to it anywhere in the image, so it is unused.
  - Readers:
    - FUN_142f04160: component maps only.
    - FUN_143115d40: template props, component map.
    - FUN_143519be0: reads def +0x10 (item id for a tooltip).
    - FUN_143556840: reads def +0x14, component map.
    - FUN_14355c450 / FUN_14355d070: read def +0x58 (the component list).
    - FUN_1437a9d70 / FUN_1437a9fa0: component maps.

  **None of them reads def +0x40.**
- The manager's own methods do not read +0x40 either:
  - LoadDefinitions FUN_143be87d0 and the add method FUN_143be7100 build two indexes: a set of DEFAULT_LEARNED ids
    from +0x2c (manager +0xf8) and an id → ITEM_DEFINITION_ID map (manager +0x140).
  - Update FUN_143be9a70 and Delete FUN_143be77d0 only update those two indexes.
  - The other vtable slots are stubs: FUN_142c98750 and FUN_142d12070 return 1, FUN_142d55460 returns 0, and
    FUN_142d0fca0 only names the error log.
- The earlier sweeps found +0x40 hits, but they belong to other objects: manager +0x40 is the component-hash count,
  and node +0x40 is the component key.

## 3. How the client really hides recipes [P]

The crafting window lists **server-sent recipe records** (`Crafting::RecipeData`). It does not list Recipes.txt
definitions. The RecipeManager is embedded in the player object at +0x7aa00 (DAT_145431a20 + 0x7aa00), with this
layout:

| offset | content |
|---|---|
| +0x08 | std::set\<int\> known / learned |
| +0x20 | **std::set\<int\> hidden recipes** |
| +0x38 | HashListMap\<PlayerRecipeData\> (cooldowns) |
| +0x78 | HashListMap\<int, RecipeData\> (records; count +0x80, buckets +0xa0, key +0xa0, next +0xa8) |
| +0x178 | window open |
| +0x180 | station |

- **SendSelf fills it.** FUN_142c1b810, called from part_0059 as `FUN_142c1b810(player + 0x7aa00, reader)`, reads
  in this order:
  1. FUN_142c34a10 → +0x08: u32 count, then u32 ids. The set is cleared first.
  2. FUN_142c35bd0 → +0x38.
  3. **FUN_142c34a10 → +0x20: u32 count, then u32 ids. The set is cleared first, so every SendSelf replaces the
     hidden set.**
  4. The record list → +0x78.

  On our server these four blocks are `RecipeData.Unknown0`, `Unknown1`, `Unknown2` and `RecipeList`. The server
  fills Unknown0 with the known recipes and leaves **Unknown2 empty**.
- **Hide / Unhide packets**, both handled in FUN_142f39870 (the RecipeBase 0x42 dispatcher):
  - **sub 0x1F RecipeHideRecipeResponse** (reader FUN_142f30050: u16 opcode → +8, u32 sub → +0x10, u32 → +0x18).
    If the recipe id at +0x18 is > 0, the client inserts it into the hidden set (FUN_142b45c50 on RM+0x20). It then
    calls FUN_14360d410(RM+0x70 data source, id), which rebuilds the list.
  - **sub 0x20 RecipeUnhideRecipeResponse** (reader FUN_142f307f0, same layout). It looks the id up
    (FUN_142f38860), erases it (FUN_142f3ea00) and rebuilds the list.
  - These are the only writes to RM+0x20 besides the SendSelf reader.
- **What hiding does.** Both effects use the hidden-set test **FUN_143c53c70** (`set_find(RM+0x20, id) != 0`):
  - The window list builder FUN_143611630 ("Crafting.Recipes") walks the station's recipe list
    (`Crafting::CraftingStationDefinitionManager`, DAT_1454312e0 +0x2b8, local data). It keeps only recipes that
    have a server record in RM+0x78. It then **skips every recipe for which FUN_143c53c70 is true**. Hidden recipes
    are therefore removed from the list. They are not greyed out.
  - FUN_142f37f30 means "recipe is on the current station and not hidden". The client's own start paths require it:
    Lua `Recipe.StartRecipe` (FUN_1435ece70 → FUN_142f3e3b0) and `Recipe.RefiningStationAddProcess`
    (FUN_1435ed6c0 → FUN_142f3cd40). A hidden recipe cannot be started from the UI.
- The client has no greying based on any requirement. The only per-row states are the columns written by
  FUN_14360e950: IsLearned, IsTracking, CountCanMake, HasSpace and the cooldown columns.

## 4. Side finding: the recipe record's fields [P]

From reader FUN_142c2e640 (wire order) and the row builder FUN_14360e950 (column names). It corrects several
"Unknown" names in `SendSelfToClient.RecipeData.Recipe`:

| server field | record off | client use |
|---|---|---|
| RecipeId | +0x04 | RecipeId |
| Unknown1 | +0x08 | RecipeType |
| Unknown2 | +0x00 | sort key (the server sends SORT_ORDER) |
| Itemid / Quantity | +0x0c / +0x10 | ItemDefinitionId / ItemCountAwarded |
| NameId / DescriptionId | +0x14 / +0x18 | name / Description |
| IconId | +0x28 | icon |
| Unknown9 | +0x30 | read as **float** (FuelNeeded = fuel × this) |
| **MemberOnly** | +0x34 | is really **AllowCatalyst** |
| Unknown13 | +0x3c | QueueSlots divisor (refining) |
| **Unknown14** | +0x40 | **AchievementId**: the tracker achievement; IsTracking = the player's tracked set (player +0x1e628) contains it |
| Unknown15 | +0x44 | CooldownTotal |
| Unknown16 | +0x50 list | categories |
| Unknown17 | +0x48 | MaxQueue |

Unknown14 is the TRACKER_ACHIEVEMENT_ID that the Replicator "track" button uses. The server sends 0 today. This
bears on the tracker half of L43 [I].

## 5. Server change needed

The layout is proven [P]. The rule for when to hide is an inference [I]: it is the live meaning of a VISIBILITY
requirement, and the client cannot evaluate it.

1. **SendSelf:** put into `Recipes.Unknown2` (rename it to `HiddenRecipeIds`) the id of every recipe that has
   `VisibilityRequirementId != 0` and that the player already "has":
   - the "Recipe: X" item's prop or recipe is known (`RecipeBook.TeachesNew == false`);
   - a storage expansion is at its maximum;
   - the Starter Set rule, if one is wanted.

   Wire format: `u32 count, count × u32 recipeId`, after the Unknown1 cooldown block and before the record list. At
   most 581 × 4 B ≈ 2.3 KB. Each SendSelf replaces the client's set, so the server must send the full list every
   time.
2. **In session**, when an unlock becomes owned (recipe item used, expansion reaches its cap), send RecipeBase 0x42
   with payload **`u32 0x1F, u32 recipeId`** (8 bytes, same framing as the AddRecipe sub 8 in RecipeBook.cs). If it
   ever becomes available again, send **`u32 0x20, u32 recipeId`**. The client rebuilds an open window by itself.
3. Keep `Crafting.UnlockRefusal` as the server-side guard. A hidden recipe can still be requested by a modified
   client, and an "unused copy held" is not a reason to hide.
4. Optional: fill record `Unknown14` with TRACKER_ACHIEVEMENT_ID, rename `MemberOnly` to `AllowCatalyst` and fill
   it from ALLOW_CATALYST, and fill `Unknown17` with MAX_QUEUE.

**Optional runtime check** (coordinator, <0.695 test copy>, normal Frida runner, never killed with hooks attached).
After step 1, log in a character who knows the Hanging Lantern prop, with recipe 2003 in Unknown2, and open the
Lumen Station. Expected: "Recipe: Hanging Lantern" is absent from the list. To confirm by hook, attach `onLeave` to
0x143c53c70 and log `args[1]` and the return value while the list builds. The hook should return 1 for 2003.
