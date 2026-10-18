# Bug hunt: items, crafting, commerce, trade, mail, chests, loot (2026-10-18)

Branch `local/bugitems` (worktree `wt-bugitems`, from client-0.695 at 9280c50). Found by reading the code paths end to
end; layouts were not changed (every fix is server-side state / checks). Line numbers are those of the base commit
9280c50. Solution builds clean (0 errors, 32 warnings = the base count; none in touched files).

Severity: **High** = item / Lumen duplication or creation a player can repeat; **Medium** = item loss in a normal
play path, or a rule bypass (no-trade, vault reach); **Low** = edge case, leak, admin-only path.

## Fixed (ranked)

| # | Sev | Where (base) | Scenario | Commit |
|---|---|---|---|---|
| 1 | High | `Game/Crafting.cs:151-158` | 18 recipes list one item in two component rows (RecipeComponents RECIPE_ID rows + RecipeRecipeComponentMaps rows; e.g. forge recipe 28 "ore 108 × 600" and "× 150", default-learned, station 48). The check was per row, the second `TakeItem` failed silently, but both rows went into `Paid`. Holding only the larger row: craft paid 600, Cancel refunded 750 (+150 per loop, ore 1747 too); every such craft also cost less than the recipe. Now summed per item, and only what was actually taken is recorded. | 3c2873c |
| 2 | Medium | `Game/Crafting.cs:263-298`, `PlayerManager.Leave` | A craft / salvage running at logout or disconnect: components already taken, job dropped by `Update` for the dead session → components and output lost. `Crafting.Forget` (called from `Leave`) now finishes the job into the inventory (≤ 1 s per item, ≤ 100 items). Also cleans the open-station maps. | 598ad84 |
| 3 | Medium | `Game/Vendors.cs:310-326` | (VENDOR_SHOPS on) `List` accepted NO_TRADE items and store purchases in their waiting period (any visitor could buy them: no-trade bypass; with Founder's Chest restore, a soulbound-item source), chests holding items and Player Design items (sold empty, contents / design lost). Refused now. The 10 % fee was `(uint)` of a double that can exceed 2^32 (unspecified cast → tiny fee); now compared in doubles. | 92f9419 |
| 4 | Medium | `Game/Props.cs:1212` | Placing a prop from a bag item took "one of that definition" (`TakeItem`), not the activated stack. With a picked-up chest holding items and an empty chest of the same kind in the bag, placing the empty one could delete the full one (contents lost) and keep the empty one. Now takes `PaidByItemGuid` first. | 81549eb |
| 5 | Medium | `Game/Inventory/Inventory.cs:748-763` (`Move`) | The occupant of the destination swapped into the moving item's old place even when that was overflow: taking an item out of overflow onto an occupied slot, or equipping straight from overflow, sent the bag / worn item into overflow where it expired (24 h) and was deleted. From overflow / resources tab / purse the occupant now goes to a free bag slot (nothing moves when none). | 0ed6a84 |
| 6 | Medium | `Network/Handlers/InventoryHandler.cs:248-259, 133-137`, `Game/Chests.cs:264-266` | Vault reach (`Vaults.CanAccess`) was checked on the request's containers, not the item: a TransferItem naming an empty bag slot but a vault item's guid moved it out of the vault from anywhere; the hotbar tool switch could wield a vault tool (and swap the held one in); right-click Equip and chest deposit found vault items by guid too. All four now check the item. | 1fb4319 |
| 7 | Medium | `Game/Inventory/Inventory.cs:477-480` (`TryRemove`) | "Take N of this kind" (crafting, placement fallback, item uses, coins) could take a picked-up chest holding items (contents lost) and took worn items as readily as bag spares (held tool vanished, other players not told). Chests holding items are skipped; worn stacks go last. | 86e644b |
| 8 | Low | `Game/Inventory/Inventory.cs:271-288` (`Add`, resources) | Resources past the tab cap (24,000,000) were dropped silently (mail, vendor, builder's bags), and a stack already over the cap was cut to it by any addition. Excess now waits in overflow. | 4bd3a03 |
| 9 | Low | `Game/Inventory/Inventory.cs:85-86` | Starter kit given whenever an inventory loaded empty: destroy the six starter items, relog, get a new kit (repeatable item source). Now once per character (new Unlocks kind 34 `StarterKitGiven`; characters with items are marked at their next login). | 570bbd1 |
| 10 | Low | `Game/Trades.cs:263-264` | Completion only re-checked "stack still in the bag with enough items". `Inventory.MoveCount` merges a store purchase in its waiting period into a plain stack and locks it, so merging onto an offered stack and accepting handed the purchase over (fraud-timer bypass). Final check now repeats trade-lock and chest-with-items. | ac0d626 |
| 11 | Low | `Game/Mail.cs:261-266, 762-768` | Compose (`CheckBag`, local and remote mail) accepted a Player Design item by guid (design lost; only `Attach` refused it), and both Attach and compose took vault items without vault reach. | f8e636e |
| 12 | Low | `Game/Salvaging.cs:98-99`, `InventoryHandler.cs:67-80` | Keyword salvage slots (type 6/7) take any unstackable item, so a chest holding items could be salvaged (contents lost). Refused now. Destroying such a chest (DeleteItem) left its contents in `chest_item` and in `Chests` memory forever; now emptied with it. | 988bc38 |
| 13 | Low | `Game/Props.cs:1923-1931` (`Remove`) | A full chest returned as its item with no free bag slot (admin `claimItemToInventory`; a player's own pick-up of a full chest is refused): nothing given, contents keyed to the removed prop → lost. Now contents to overflow and the chest item given (overflow). | 6de292a |
| 14 | Low | `Game/Consumables.cs:221-229` | `flying` kept an entry (the whole WorldSession) for every player who ever used the Builder's Bauble of Flight; never removed. Now pruned each tick. | 3f86906 |

Docs: cb15d0a (docs/gameplay.md inventory + crafting notes, docs/vendors.md listing refusals).

## Checked, not fixed (with reason)

| Sev | Where | Finding | Why not fixed |
|---|---|---|---|
| Data | RecipeComponents / RecipeRecipeComponentMaps | The 18 recipes of #1 list extra RECIPE_ID rows the component map doesn't (recipe 28: map 600 + RECIPE_ID 150). Which list live charged is not proven; the server sends all rows to the client (`CraftingStations.BuildRecipeRecords`), so client and server agree on the sum. Recipe 17 (forge, default-learned) has a component of item **0 × 100**, so it can never be crafted ("Missing components."). | Data question for a trace (client recipe loader FUN_143bf6940/6a30 not in the decompile index yet) → OPEN-LEADS. |
| Low | `Game/Trades.cs:240-260` | `Accept` decides "both accepted" under the lock but `Complete` removes the trade later outside it; two Accepts on two threads could complete twice. | All zone packets and the world-channel trade calls run on the world tick thread (`NetworkManager.Update`), so it can't happen today. Cheap to harden if packet handling ever goes parallel. |
| Low | `Game/Social/RemoteMail.cs:287-308` + `SocialServer/MailService.cs` | A hand-over whose zone answer is lost past the service's hand-over expiry could be handed over again (double give). | Needs a link loss longer than the expiry; the zone holds and resends its answer. Fix belongs to the social-service owner (token de-dup on the zone side). |
| Low | `Game/Entitlements.cs:197-260` | Founder's Chest: local `Mail.SendSystemMail` saves asynchronously, so a second click before the save commits finds the item "missing" again and mails another (soulbound) copy. | Soulbound items, no trade value; the Vendor hole (#3) that made them sellable is closed. |
| Low | `Game/Membership.cs:166-176` | Stipend claimed in the auth DB, then lost if the player leaves before the world tick pays it. | Documented behaviour ("paid at the next claim period"). |
| Low | `Game/Crafting.cs` / `Durability.HandleRepair` | The repair station counts as open until the window-close packet (walking away only resets the cursor), so repairs work anywhere afterwards. Chest windows, `LootChests.OpenChest`, `Chests.Transfer` have no distance check either. | Reach rules for interactions are a separate audit (no item creation). |
| Low | `Game/Inventory/Inventory.cs:588-607` (`CanReceive`) | Counts resources / currency as bag stacks and lets worn items "stack" onto plain stacks. | Errs both ways but never loses items (Add overflows); trades only offer bag items. |
| Low | `Game/ItemUses.cs:479, 559` | Builder's bags / sifted bags check bag space for resources that go to the resources tab. | Over-refusal only. |
| Low | `Game/Vendors.cs:262` | Purchase total capped at uint.MaxValue. | Only matters to a buyer holding exactly 2^32-1 Lumens. |
| Low | `Game/Mail.cs:665-671` | A coin attachment above int.MaxValue can never be taken (`ChangeCoins` limit). | Only reachable with such a coin count in the first place. |
| Low | `Game/DesignItems.cs`, `Inventory.ExpireOverflow`, DeleteItem, Vendors | Player Design items destroyed / expired leave their template row (owned by the item guid) in the database. | Orphan rows only; no player-visible effect. |
| Low | `Game/Consumables.cs:475` | Repair Bot despawn runs on a thread-pool thread (`Task.Delay`) into `Props.Despawn`. | Props' own thread safety; campfires already use the world tick. |
| Info | `Game/Unlocks.cs:61` | The unlock cache is never evicted at logout. | Bounded by characters ever logged in. |
| Info | `Game/LootChests.cs:124` | CS0675 warning (`GuidBase \| (ulong)chest.Index`). | Index is never negative: harmless. |

## In-game test steps (coordinator)

Dev commands are lines in the world's `dev_commands.txt`, e.g. `@TestPlayer give 108 600`. No new commands or settings.

1. **#1 crafting**: `@TestPlayer give 108 600`, `@TestPlayer give 1747 900`, open a Stone Forge (`@TestPlayer station 48`),
   start recipe 28 (output item 92) → refused "Missing components." Then `give 108 150` and `give 1747 150`, start it,
   cancel at once → exactly 750 / 1050 back, nothing more.
2. **#2 logout**: start a 100-count craft, log out at once, log back in → output in the bag (or overflow).
3. **#5 overflow swap**: `@TestPlayer giveoverflow 231 1`, fill the bag, drag the overflow item onto an occupied bag
   slot → the occupant moves to a free slot (or, with none, nothing moves); it never lands in overflow.
4. **#6 vault**: away from any vault, the hotbar / right-click equip of a vault item does nothing; at a vault
   (`@TestPlayer vaultnear`) everything works as before.
5. **#3 vendors** (VENDOR_SHOPS on): list a NO_TRADE item from Showcase › My vendors → "That item can't be sold on a
   vendor."
6. **#9 starter kit**: on a test character destroy all six starter items, relog → inventory stays empty.

## For the coordinator's files

- MODLOG: the 14 fixes above (commits 3c2873c … 570bbd1, docs cb15d0a).
- TESTING-NEEDED: the steps above.
- OPEN-LEADS: recipe component rows (which of RECIPE_ID rows / map rows live charged; recipe 17's item-0 component).
- New Unlocks kind 34 (StarterKitGiven) — check for a clash if another branch added kind 34 meanwhile.
