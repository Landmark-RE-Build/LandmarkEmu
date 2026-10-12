# System: items, inventory, equipment (Inventory 0x2D/0x2, Equipment 0xCB, item definitions Command 0x1E/0x4B)

Status: [ ] started — notes only.

## Item definition record (captured, gameData/items.json; client reader FUN_142b7df20 via FUN_142b800a0)

Field profile over 2885 items (weapons = equipSlotId 2, 81 items):
- u17: composite effect id on 3 weapons (11824 PFX_Weapon_Ambient_Fire_Bow_01, 11825 ..Lightning_Sword2H_01, 11826
  ..Physical_Sword1H_01) → **ambient weapon effect** (glow) — server never plays it.
- u41_46 on weapons e.g. [80,358,0,372,2,12] / [83,351,0,369,1,108]: unknown ids (item class? stat sets?) — trace.
- u47: category list (26 = Weapon); u48_53 [0,5,...] (?), u58 = 1500 on 33 weapons (?), u59 lists ([50,82], [22]):
  stat ids?; grantId (705 distinct): item grants (recipes/props); abilities: {id, type (2/3/5...), input name
  ('PrimaryAttack'), category ('Combat')}.
- No server-side item table schema exists in the exe: live items came from a database, so field names must come from
  the client code that reads each ClientItemDefinition offset.

## Inventory / equipment protocol (map level)
- Inventory 0x2D: ✔ tooltips (instance / definition / full), ActivateItem 0x15, RequestTransferItem 0x16, OverflowAbsorb
  0x1C, EquipByGuid 0x3, DeleteItem 0xB; ✗ RequestSalvageItem 0x13 (+ ReplySalvageItem 0x14), ItemActionBarAssign 0x6,
  EquipByItemRecord 0x8, EquipItemGroup 0xA, RequestSplitStack 0xC, ToggleAutoResupply 0xD, RearrangeItemSlots 0xE;
  S→C ✔ TooltipReply, SetSize, TransferItemResponse, SetVoxelStorage; ✗ LootNotification 0x1A, CatalystDefinitions
  0x1B. 0x2 family: InventoryPacketEquippedRemove ✗.
- Equipment 0xCB ✔ set equipment / slot / slots; EquipmentPacketUnsetCharacterEquipmentSlot (no opcode yet).
- ClientUpdate items ✔ ItemAdd / Update / Delete; ✗ ItemDeleteFailed. Command ItemDefinitionRequest 0x49 ✗ (client asks
  for unknown definitions; reply 0x4A ItemDefinitionReply).
- Item timers 0xE6 ✗ (item line timers, trial items). Loadouts 0xBD ✗. Requisition 0x6E ✗ (requisition items).
  Action bar 0x9A ✔ (UpdateSlot S→C ✗). Chests 0xFF ✔. ReferenceData 0x2F ✔ (item classes / categories / profile data).
- Data: EquipmentSlotDefinitions, LoadoutSlot*, ItemClassGroups (712, not read), ItemClassSets, FilterToItemClass,
  DurabilitySet, WeaponAmmoSlots ...; server-only ItemCategories / Groups, ItemClasses, InventorySlotDefinitions,
  ItemMods / ItemModPackages / ItemModGroupEntries / ItemModElements / ItemQualityRanges / Sets / SetEntries,
  ItemBasePackageEntries (ItemRandomCreationManager: random item stats / quality on crafting and loot — live rolled
  item stats on the server; schemas in ../schemas.md FUN_143c05010...).

## Client constants (trace/client-constants.md)
Inventory: BaseInventorySize 40 (bag), MaxInventorySize 100; BaseVaultSize 60, MaxVaultSize 120; BaseCollectionSize /
BaseVaultCollectionSize 250; BaseEquipmentSize 10; BaseLoadoutPoolSize 10; BaseBuybackSize 10; BaseGuildVaultSize 40.
`Currency.ItemDefinitionId` = 1579 = **Lumens are item 1579** (also ReviveToken, WarpNetwork.CostItemId,
ClaimVendor.CurrencyItemId, CharacterCustomization.ResourceId). Vault / Chest close distance 10 / 12 m.
Durability: warning at 20 % (Durability.Ui.WarningThresholdPct; texts Durability.Ui.*). Item.ResourceRatingId = 11
(the stat a gathering tool's Resource Rating is). Loot pinatas: decay 120 s, pickup radius 20 (auto) / 30 (manual),
ownerless loot 5 m, max dropping time 7 s, default pinata 5.

## Live behaviour (wiki, launch 2016: Inventory, Vault, Pocket, Void Vault, Trading, Chests)
- Carried inventory (I): **Bag** tab (items, gear, props), **Materials** tab (gathered materials + crafting
  components; up to 500,000 of each common material, less for some e.g. logs; beyond that they spill into the bag
  in smaller stacks), **Overflow** tab (appears only after a site is deleted / expires: everything from the site;
  must be emptied within 24 h or lost). No weight. Search box. Character sheet with equipped gear to the right.
- Bag size at launch 60 by default (wiki), +20 per Pocket bought for 520 Lumens, max 100 (constants:
  BaseInventorySize 40 / MaxInventorySize 100 — the wiki's 60 may include the starting pocket; check SendSelf).
  Pockets are consumed on use (bag or vault variants).
- **Vaults**: all vaults share one private store ("Universal Vault Network"); Vault tab (default 80 per wiki,
  constants say BaseVaultSize 60, max 120) + Materials tab (not expandable). Only the owner can take from their
  vault whatever the site permissions. Void Vault = soulbound founder vault (only on own claims).
- **Storage chests** (props): one tab, items stay in that chest; **max 50 chests per account** across all sites;
  access follows site permissions (Pillage and Plunder); emptied into Overflow with the chest when the site goes.
- **Loot chests**: Common (surface caves; resources) and Lost (Outposts; more and better). Golden glow + swirl
  particles. **Everyone within 50 m when it is opened gets their own, different loot**, no party needed.
- Trading: storage chests (permissions), **mail** (Spire mailboxes or placed ones; 4 attachment slots; max stack
  9,000 per send; "!" alert icon opens mail), direct trade (right-click player → Trade; target accepts). Marketplace
  purchases tradeable after a 12-24 h wait except designs / soulbound. Shift-drag splits stacks (Lightstones stack 100).
- Teleport to Friend (friends list right-click) has a cooldown.
