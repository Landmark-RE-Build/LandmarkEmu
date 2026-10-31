# System: inventory & items (module 5 of REBUILD-PLAN) — deep pass started 2026-10-13

> 0.695 rebase (2026-10-26): addresses below are 0.589 unless marked 0.695; their 0.695 partners and changes: trace/0695/address-map-589-695.md, trace/0695/rebase-695.md

Archive: tag `inventory-v1`, `archive/inventory-v1/` (Inventory/*, ItemUses, Durability, Salvaging, Consumables,
DesignItems, Vaults, Chests). Map-level notes: systems/items.md. Unreachable C→S subs (verdicts.tsv): SalvageItem
0x13 (+0x14), ItemActionBarAssign 0x6, EquipItemGroup 0xA, SplitStack 0xC, ToggleAutoResupply 0xD, EquippedRemove.

## TransferItemResponse (0x2D sub 0x19, reader FUN_142e78a30, handler FUN_142e81590 case 0x19)

- Layout: the request echoed + u32 result. The client always writes a debug line "Item Transfer Failed!! - [<ErrorId:
  …>]" with the reason name (FUN_143b966c0, table 0x145219490, 42 names; 1 = "Success!!!", 0 = "Invalid reason").
- Messages shown for: 0x16 / 0x17 Chest.AccessDeniedOnItemAction (no permission on source / destination chest),
  0x21 Chest.ChestAccessCantTakeNoTrade, 0x20 Chest.ChestAccessCantPlaceNoTrade, 0x12 Chest.AccessDeniedFraudTimer,
  0x25 Chest.ChestWithItemsIntoChestError; 0x22 opens the unique-equip dialog (Equip.UniqueEquip, FUN_142e7fe40).
- Other reasons: 2 destination full, 3 source not found, 0x13 failed in player's inventory, 0x18 / 0x19 too far
  from chest, 0x26 claim at max chests, 0x28 casting, 0x29 default.
- Server (2026-10-13): sent 0 = done / 1 = failed (backwards); now 1 = done and the real reasons for chests
  (permission 0x16 / 0x17, NO_TRADE into someone else's site chest 0x20, chest with items 0x25, full 2, missing 3);
  inventory moves that fail send 0x13. Tool switching / equipping still verified (scenario gear).

## NO_TRADE

- Item definition first flag byte bit 7 (client +0x0C >> 7: Lua NoTrade, tooltip UI.Tooltip.NoTrade). Set on 64
  items: Lumens, Build Site Flags, store expansions, titles, founder pickaxes, Alpha Boost Potion...
- ItemDefinition.NoTrade. Used: chest deposits (0x20). 0x21 (take a NO_TRADE item you don't own from a chest) needs
  the original owner per chest item — not stored yet.

## NO_TRADE in trades and mail (2026-10-13)

- Trade offers of NO_TRADE items are ignored (no update sent).
- Mail result table 0x14521a980 (34 reasons): 0xA "can't send NO_TRADE item", 0x12 "can't send chest item (with
  items in it)", 0xF "user is being ignored by target" — now used by AttachItemResponse (0x15) and the send reply;
  0xF only when the recipient is online (ignore lists are loaded per session).

## Item flags (partial)

- Tooltip builder FUN_1434655a0 tests a computed flag byte: 0x40 NoSale, 0x20 NoTrade, 0x10 RemovedOnUse, 8 NoDelete,
  4 NoUpgrade, 2 UniqueEquip, 1 NoSalvage (texts UI.Tooltip.*). Definition bits: flags[0] bit 7 NoTrade (+0x0C >> 7,
  sure); flags[2] bit 7 is on 21 accessories (rings, bracers, orbs) — probably UniqueEquip (transfer reason 0x22),
  not confirmed; flags[0] bit 6 on starter / founder pickaxes and builder trophies; flags[1] bit 1 on 97 tools.
  Open: map the remaining bits from the code that fills the tooltip object's flag byte, then enforce NoDelete /
  NoSale / NoSalvage / UniqueEquip.
- 2026-10-14: the tooltip flag byte is NOT computed by the client: it is the last u32 of the server's ItemTooltipReply
  (reader FUN_142e784e0 → +0x1c8; FUN_1436a9020 passes &+0x1c8 to FUN_1434655a0). The server now sends 0x20 for NoTrade
  definitions. Definition flag census (2,885 items): byte0 bit3 2,180 stackables, bit6 18 founder tools / trophies,
  bit7 64 NoTrade; byte1 bit1 97 gathering tools, bit3 32 Lumens + builder's bags, bit4 690 items the client exempts
  from the overflow warning (FUN_142e83a30: container 7 and !(def+0xd & 0x10)); byte2 bit7 21 accessories (UniqueEquip?
  unproven). No live tooltip capture exists, so the other tooltip bits stay 0.
