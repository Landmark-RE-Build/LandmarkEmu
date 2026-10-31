# Crafting bug, part B: server side (2026-10-31)

User report: "the replicator and maybe other stations can not craft stuff? they attempt to craft stuff but won't take
resources or give me what i crafted". Server: commit 91c5a566 (dev, Docker). Session: TestPlayer,
`server-data/worlds/dev/console.log` lines ~110990-115360 (07:14-07:24 container time). Read-only pass: no code
changed, nothing built or run. [P] = proven from code, decompile or log lines; [I] = inference.

## 1. Result

Two different things happened in the session, and the log shows them apart:

| When | Station | Recipes | Server result | Client |
|---|---|---|---|---|
| 07:15:23-07:17:07 (19 tries) | 53 Replicator, a **Spire** template prop (guid 0x53500001CC000004) | 1574 Cut Plain Wood ×750, 1442 Smelted Iron ×750, 765 Adventurer's Grappling Hook | **refused every time: "You are too far from the station."** Nothing is taken and nothing is given | prints the text, resets its crafting state |
| 07:21:55-07:24:16 | 55 Lumen Station (UI station, no prop) | 2386 / 2387 / 2388 / 2389 / 5001 | Lumens taken, item given, or refused with "Error: Failed item requirement" at the unlock cap | **got everything**: it showed the items, used them and destroyed them (section 4) |

**Root cause of the bug the user saw (Replicator):** `Crafting.TryStart` checks the distance to the open station
with `Props.PositionOf` only (Crafting.cs:245-247) [P]. That function searches the placed-prop list (Props.cs:2447),
and a Spire's own stations (its Replicators and Repair Station) are template props held by `Spire`, not placed
props (Spire.cs:955-967; only `Props.LoadWorld` / `Props.Place` add to that list, Props.cs:2377 / :981) [P]. For a
Spire station the position is `null`, and the code treats `null` as too far:

```csharp
// Crafting.cs:245-247
if (open.PropGuid != 0 && (Props.PositionOf(open.PropGuid) is not System.Numerics.Vector3 stationPosition
        || System.Numerics.Vector3.Distance(stationPosition, session.Position) > MaxStationDistance))
    return "You are too far from the station.";
```

So **no craft at a Spire Replicator has ever been able to start** [P]:
- 2026-10-09 (dad0a76f, from c410dd20 10-08): the check is added (looks up placed props only).
- 2026-10-11 (7eac1ed5): the Spire Replicator (53) and Repair Station (56) become usable through
  `Spire.StationAt`, but `TryStart` is not changed.
- 2026-10-21 (ed2ff4d4): the same bug in the walk-away check (Spire windows closed half a second after opening)
  was fixed by adding `StationPosition = Props.PositionOf ?? Spire.PositionOf` (Crafting.cs:142). Only
  `CloseWindowsAway` (Crafting.cs:125) uses it. `TryStart` kept `Props.PositionOf`. Since then the window stays
  open, but every craft in it is refused.
- No test covers `HandleStart` at a Spire station (`LandmarkEmulator.Tests/Rules/StationWindowRuleTests.cs` tests
  the window only).

Placed stations (a Replicator, Forge, Sifter or Workshop prop on a build site) do pass this check when the player is
within 8 m of the prop's pivot. Station 55 has prop guid 0 and skips the check [P]. The Spire Repair Station works
because repair (`Durability.HandleRepair`) never calls `TryStart`. The log shows it working at 07:14:42: "repaired
item 3764 for 1 Lumen" [P].

The client's side of a refusal, 0.695 Crafting dispatcher FUN_142f39870, case 2 RecipeStartResponsePacket [P]: it
reads {u32 sub, bool ok, string}, prints the string through DAT_145431a10 vtable +0x18, and on `ok == false` calls
FUN_142f37fe0, which resets the crafting state. The client starts its craft UI on its own, so the user sees an
attempt that ends with nothing taken and nothing given. That matches the report.

## 2. The Replicator session, step by step

- 07:14:21 InteractRequest → `07-00-00-00 | 00-00-00-00 | 35-00-00-00 | 0x0` = CraftWindowEnableResponse {sub 7,
  result 0, station 53, u64 0} (CraftingStations.cs:143-153 `OpenWindow`). The prop guid
  0x53500001CC000004 (TargetBase `06-00-04-00-00-CC-01-00-50-53…`) is Spire GuidBase 0x5350… with zone 0x1CC = 460
  << 24 (Spire.cs:43 / :158) [P].
- 07:14:30 Repair Station 56 opened. 07:14:42 two repairs work. 07:14:49 "station 56 window closed by the server (12.7 m
  from it, limit 8.0 m)": the walk-away check works for Spire props [P].
- 07:15:07 Replicator 53 opened again. It stayed open through 07:17:07: no close line appears, even though
  CloseWindowsAway runs twice a second with `Spire.PositionOf`. So the player stood within max(8 m, reach at open
  + 3 m) of it the whole time [P from the absence of a close log + Crafting.cs:125-132].
- 07:15:23 … 07:17:07: every RecipeBase 1 StartRequest is answered with
  `02-00-00-00 | 00 | 21-00-00-00 | "You are too far from the station."` (sub 2, ok = false, 33-character string) [P].
  The server took nothing: no ItemUpdate or ItemDelete follows [P].

Recipe facts (gameData695) [P]:

| Recipe | Output | Components (RecipeComponents RECIPE_ID rows + RecipeRecipeComponentMaps) | Station map |
|---|---|---|---|
| 1574 | 2000 Cut Plain Wood ×750 | row 4195: 71 Plain Wood ×50 | 4445: 53 |
| 1442 | 110 Smelted Iron ×750 | row 3853: 36 Iron Ore ×250 | 4407: 53 |
| 765 | 1178 Adventurer's Grappling Hook ×1 | none in either table (no component rows found) | 4685: 53 |

All three have DEFAULT_LEARNED 1, CAN_CRAFT_WITHOUT_STATION 0, VISIBILITY_REQUIREMENT_ID 0, CASTING_TIME 0.25. With
the distance check fixed, the next checks are the station map (passes) and the components (needs the player's
Plain Wood / Iron Ore). `GameTableManager.ComponentsOf` removes a row listed through both tables (it groups by recipe
and component row id, GameTableManager.cs:48-61), so 1574 costs 50 wood, not 100 [P].

## 3. What the server does for 2386-2389 and 5001 (Lumen Station 55)

| Recipe | Output (Items) | Cost (component row → item 1579 Lumen) | MAX_QUEUE | VIS_REQ | Note |
|---|---|---|---|---|---|
| 2386 | 3710 Vault Expansion ×1 | 5742: 260 Lumen | 3 | 1285 | vault 60→120 by 20 |
| 2387 | 3711 Inventory Expansion ×1 | 5743: 520 Lumen | 3 | 1286 | bag 40→100 by 20 |
| 2388 | 3712 Design Expansion ×1 | 5744: 1040 Lumen | 7 | 1287 | 6 M→48 M voxels by 6 M |
| 2389 | 3760 Dungeon Builder's Starter Set ×1 | 5745: 20 Lumen | 1 | 1104 | opened through ItemUses |
| 5001 | 3894 Cozy Campfire ×20 | 5833: 130 Lumen | 0 | 0 | repeatable, stack 100 |

All five have CAN_CRAFT_WITHOUT_STATION 1 and are mapped to station 55 (CraftingStationRecipeMappings 5448-5451, 5640).
The flow (Crafting.cs:215-286, :431-465):
1. `HandleStart`→`TryStart`: count 1-100, recipe known (DEFAULT_LEARNED 1), station 55 open (prop 0: no distance
   check), map row present, `UnlockRefusal` for VIS_REQ ≠ 0 (section 5), all components of type 1, enough of
   them (`Inventory.CountOf`).
2. `session.TakeItem(1579, cost × count)` → `Inventory.TryRemove` → `SendItem(replace)` = **ClientUpdateItemUpdate**
   for the Lumen stack. Because the item is `Inventory.CurrencyItem` 1579 (Inventory.cs:46), `SendCurrencies`
   (WorldSession.cs:952) also sends **PlayerUpdateCurrencyChange 0x26/0x49** and `Commerce.WalletChanged`, which sends
   **InGamePurchaseBase 0x0C WalletInfoResponse** (Commerce.cs:218-243; it is sent only while the Player Studio
   listing fee is charged, `WalletHoldsLumens`). Then **RecipeBase 2** {ok}.
3. `Queue`: the job is due after max(0.25, CASTING_TIME) × count = 0.25 s. On the next world tick, `Update`
   calls `GiveItem(output, ITEM_COUNT × count)`, which sends **ClientUpdateItemAdd** for a new stack or
   **ClientUpdateItemUpdate** for a grown one. Then `Achievements.Crafted` and **RecipeBase 5** {ok, recipe id, ""}.

These are exactly the packets the task lists. The "currency instead of materials" is right: the Lumen Station
charges Lumens, item 1579, kept as a stack in container 8 (CurrencyContainer, Inventory.cs:27). That is what the
table says (component rows 5742-5745, 5833), not a server mistake [P].

## 4. Decoded TRACE bytes (the server's own writers), and proof that the client took them

Writers: ClientUpdateItemUpdate.cs (0.695 reader FUN_142c1ca60, checked: u64 guid, u32 count +0x20, u32 +0x24,
u32 +0x28, u64 +0x38, u32 slot +0x2c, u32 container +0x30, two ItemRandom FUN_142c20e60, u32 +0xf0, u32 wear +0xf4
[P]); ClientUpdateItemAdd.cs = u32 blob length, bool "definition follows" = 0, InventoryItem.Write = WriteRecord +
the trailing byte when OwnerType 1 and container < 12 (InventoryItem.cs `HasExtra`). The 0.695 ItemAdd path does read
that byte: FUN_142cd9980 case 1 → FUN_142fcf1f0 → FUN_142f20190, which reads the record with FUN_142c208e0 and then calls
FUN_142c98950 with a lambda that holds the same stream, as SendSelf does (8380fb5) [P]. So the trailing byte is
correct for ItemAdd.

07:24:10-11, recipe 5001 ×1 (console.log:115226-115235):
- ItemUpdate `37-03-00-01-00-00-00-00 | A9-8B-98-00 | 0 | 0 | 0(u64) | slot 0 | container 8 | …zeros | wear 0`
  = Lumen stack 0x01000337 → 10,009,513 (the next craft gives 0x988B27 = 10,009,383: exactly 130 less) [P].
- CurrencyChange `01-00-00-00-00-00-00-00 | 01-00-00-00 | 01 | A9-8B-98-00` = {u64 player guid 1, u32 count 1,
  u8 currency 1, u32 10,009,513} [P].
- 0x45 `0C-00 | 01-00-00-00 | 00 | A9-8B-98-00 | 0 | "" | "" | 01` = WalletInfoResponse carrying the Lumens [P].
- RecipeBase `02-00-00-00 | 01 | 00-00-00-00` = StartResponse ok, "" [P].
- ItemAdd blob 0x93 = 147 bytes: def 3894, tint 0, Random{quality 2}, Random2{0}, cost multiplier 0x40800000
  (4.0f), guid 0x1000006C, **count 20**, container 0 (bag), **slot 26**, owner 1, last cast 0x7FFFFFFF, no rental /
  design / metadata, timed delete 0, trailing byte 0. All 151 bytes are used, none left over (decoded with a
  scratch script that follows `InventoryItem.WriteRecord`) [P].
- RecipeBase `05-00-00-00 | 01 | 89-13-00-00 | ""` = CompleteResponse ok, recipe 5001. Its layout matches 0.695
  FUN_142f39870 case 5 {u32 sub, bool, u32 → manager +0xac, string} [P].
- The next crafts grow the same stack by ItemUpdate (guid 0x1000006C count 40/60/…), and a second stack
  0x1000006D starts at slot 40 when the first is full at 100.

07:21:55, recipe 2387: Lumen 6,475 → 5,955 → 5,435 (−520 each). ItemAdd def 3711 count 1 slots 8 / 19 / 26
(guids 0x1000005C-5E) [P].

**The client accepted all of it** [P, from its own requests]:
- It used each Inventory Expansion (InventoryBase 0x15 ActivateItem on a guid it could know only from the ItemAdd):
  07:21:59-07:22:05, "Inventory storage grew from 40 to 60 / 80 / 100", sent as SetSize 0x2D/0x17 {0, 60/80/100}.
- It used the 3 Vault Expansions (07:22:27-29, 60→120) and the 7 Design Expansions (07:22:35-40 and 07:24:08,
  6 M→48 M; SetVoxelStorage 0x2D/0x1D, the last `80-DE-80-02 → 00-6C-DC-02` = 42 M→48 M).
- 07:22:48: it asked for the tooltip of the new Dungeon Builder's Starter Set (0x2D/0x12 reply for def 0x0EB0) and
  opened it at 07:22:49 ("opened the Dungeon Builder's Starter Set"; ItemDelete 0x10000066 plus 2 ItemAdds).
- 07:24:22: it cast Cozy Campfire (ability 1098) from stack 0x1000006C ("used Cozy Campfire", count 100→99).
  07:24:38 / :41: "destroyed 60 × item 3894" and "99 × item 3894".

So the Lumen Station crafting works end to end. Nothing in the 2386-2389 / 5001 path explains "no resources taken /
nothing given".

## 5. Why "Error: Failed item requirement"

`UnlockRefusal` (Crafting.cs:310-326) refuses a one-time Lumen Station unlock that would add nothing (L43, user
decision 2026-10-17). For the storage expansions it uses `ItemUses.ExpansionUsesLeft` (ItemUses.cs:137): the steps
left to the maximum, minus the unused copies held. The refusal goes out as text 560768 "Error: Failed item
requirement" in the StartResponse string [P]. Every refusal in the log follows this rule:
- 2386 ×3 at 07:22:18: vault 60 → 3 steps, 2 copies held → 1 left < 3. ×1 at 07:22:20 and ×3 at 07:22:22 / :24: 3 held → 0 left.
- 2388 ×7 at 07:23:54: design 30 M → 3 steps, 0 held → 3 left < 7 (the client lets MAX_QUEUE 7 through; the server
  refuses the whole request rather than capping it).
- 2388 ×1 nine times at 07:24:02-07: 3 bought and not yet used → 0 left. Once they were used (07:24:08) the recipe was
  hidden (RecipeBase 0x1F `54-09-00-00` = 2388, RecipeBook.cs:323-368).

The client has no "storage at its most" text; 560768 is the fallback the server chose [I, ItemUses.cs:76-92]. This is
working as designed, but the message does not say why.

## 6. Other findings

1. **The two distance limits disagree** [P]. `CloseWindowsAway` keeps a window open up to
   max(8 m, distance at open + 3 m) (Crafting.cs:128, `openReach` + `ReachMargin`). `TryStart` refuses beyond a fixed
   8 m (`MaxStationDistance`). A big station clicked from 6-10 m away keeps its window open, but its crafts fail with
   "too far". This applies to placed stations today, and to Spire stations once they use `StationPosition`.
2. `Inventory.CountOf` counts every stack (Inventory.cs:578). `TryRemove` skips picked-up chests that still hold items
   (Inventory.cs:528). So a check can pass and the take still fail. `TryStart` refunds and answers "Missing
   components." (Crafting.cs:273-279), so this is harmless but confusing [P].
3. `CountOf` also counts the vault (container 1) and equipped stacks, so a craft can take components from the vault.
   Whether live allowed that is not checked here [I].
4. Bandwidth: each Lumen change sends ItemUpdate (100 B) + CurrencyChange (17 B), and WalletInfo (24 B) while the
   listing fee is on. That is 3 packets per craft start, acceptable. A ×N craft is a single take, so it does not
   multiply.

## 7. Server change (for the implementer; not made here)

- In `Crafting.TryStart`, look the station up with `StationPosition(open.PropGuid)` (Props, then Spire) instead of
  `Props.PositionOf`. Use the same limit as `CloseWindowsAway`: max(`MaxStationDistance`, `openReach[session]` +
  `ReachMargin`), through one shared helper so the two can't drift again. Log the distance and the limit in the
  refusal line (as `CloseWindow` does).
- Rule test: open a Spire Replicator window (Spire template prop, station 53) and `HandleStart` recipe 1574 with
  50 Plain Wood → started, 50 wood taken, 750 Cut Plain Wood given after 0.25 s. The same from 30 m → "too far".
  A placed station opened from 10 m → starts.
- In game, verify by the client's reaction: Plain Wood count drops and Cut Plain Wood appears in the bag (ItemAdd),
  not the server log alone.
- docs/gameplay.md (~line 471, station window section): crafting uses the same station position and limit as the
  walk-away close.

## 8. Open questions

- Is 8 m (+3 m reach margin) the right craft range for the Spire Replicator? Its pivot-to-click distance is not
  logged. Next: log the distance at open (`StationOpened`) for Spire stations, or work it out from the Spire template
  (spire_base.txt prop 2894) and the arrival point.
- Recipe 765 (Grappling Hook) has no component rows in RecipeComponents or RecipeRecipeComponentMaps, so it would
  craft for free once the distance is fixed. Next: check the 0.695 PropRecipe / item tables and the wiki for its live
  cost.
- What exactly the client's crafting UI shows during a refused craft (progress, sound, chat line) is part A
  (client side). The server side needs no change for that.
