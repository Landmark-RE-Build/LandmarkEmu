# L39 — Parlor unlocks: which knowledge ids an unlocker grants, and how the 0.695 Crimson Parlor gates "[Unlock]" rows

Sources: 0.695 decompile (re/decomp_all_695), RTTI (re/out/rtti_695.tsv), BarberShopWidget ActionScript
(re/as3_695/BarberShopWidget), client tables (gameData695 + the 0.695 install's Resources: ArtTints.txt,
ArtTintGroupUnlock.txt, ArtTintGroupUnlockTint.txt, Images/ImageSets.txt), text table re/out/text_695.tsv,
AuthServer Resources/ArtTints.json (the TintDefinitionManagerData the server sends).
[P] = read in the client code / data, [I] = inferred.

## 1. The knowledge store (player +0x7ae28)

- [P] The object at ClientPlayer +0x7ae28 is a `ClientKnownCharCustomizationManager` (RTTI 1446ee310; vtable
  `[0]` 142fca300 destructor, `[1]` (+0x8) **FUN_142fd1200**, `[2]` (+0x10) **FUN_142fd1440**, `[3]` (+0x18)
  FUN_142fd17c0 = empty "loaded" hook). It holds two int sets:
  - **set A at +0x08** (player +0x7ae30; tree root +0x10): `contains` = vtable +0x8, FUN_142fd1200 →
    FUN_142f38860(this + 8, id).
  - **set B at +0x20** (player +0x7ae48; tree root +0x28): `contains` = vtable +0x10, FUN_142fd1440 →
    FUN_142f38860(this + 0x20, id).
- [P] **SendSelfToClient** (0.695 reader FUN_142c19bd0, part_0059 ~l.24210): after the known-props list (+0x7adf8)
  it reads `u32 n, n × u32` into +0x7ae30 (set A), then `u32 n, n × u32` into +0x7ae48 (set B) (FUN_142c34a10 clears
  the set, then inserts), then calls vtable +0x18 (empty). The server model already writes these two lists in this
  order (SendSelfToClient.CustomizationKnowledge, CustomizationKnowledge4); nothing filled them before.
- [P] **CharacterCustomization 0x11C sub 2 AddKnowledgeResponse** (S→C). Dispatch: ClientCharacterCustomizationManager
  vtable (RTTI 144703688) last entry FUN_1430a29d0: sub 2 → FUN_14368a830(player + 0x7ae28, packet), then
  **FUN_1430a33a0** (rebuild the option lists) and **FUN_1430a3d30** (data source refresh) — the open Parlor updates
  at once. Reader FUN_14368a5d0, whole wire:
  `u16 0x011C, u8 sub 2, u32 n, n × u32 id, u32 type`.
  FUN_14368a830 rejects the packet unless it is consumed exactly (`end - cursor < 1`), so **no trailing bytes**.
  **type 3 → set A (+0x08)**, **type 4 → set B (+0x20)**; any other type adds nothing. Ids already present are
  skipped (tree lookup before FUN_142b45c50), so the packet is additive and can carry only the new ids.

## 2. What the sets hold

### Set A (type 3) = CharacterCreateOptions IDs — [P]
- Option-list builder **FUN_1430a33a0** (the "[Unlock]" row filter, part_0083 ~l.28060): for each OptionEntry of the
  character's race / gender / slot (FUN_143098150; entry stride 0x98, `+0x00` option ID, `+0x30` byte
  DEFAULT_UNLOCKED, `+0x3c` UNLOCKING_ITEM_ID — OptionEntry built by FUN_143090610 from the definition parsed by
  FUN_143c6a010 (CharacterCreateOptions.txt columns): def +0x28 DEFAULT_UNLOCKED → entry +0x30, def +0x2c
  UNLOCKING_ITEM_ID → entry +0x3c):
  1. DEFAULT_UNLOCKED → listed;
  2. else listed if the player is admin (DAT_1454311c8 vtable +0xc0), **or set A contains the option ID**
     (vtable +0x8), **or** UNLOCKING_ITEM_ID ≠ 0 and the store (DAT_1454311c8[0x640a] + 0xd98, the Marketplace
     product hash) has a product for that item;
  3. otherwise the row is **not listed at all**.
- Per-slot data source **FUN_1430a37a0** (OptionType 1): for the currently selected option of a slot, when not admin
  and not in set A and the store sells UNLOCKING_ITEM_ID: "PurchaseRequired" = 1 plus "MarketName" (product name text,
  entry +0x17c) and "MarketCost" (cheapest price, FUN_143c2c370); else PurchaseRequired 0.
- [P] BarberShopWidget.as:292-299 only shows the "requires <pack> — purchase from the Marketplace" button for
  PurchaseRequired = 1; the Purchase button (:386-390) calls `CharacterCustomization.ConfirmCustomization` without any
  knowledge check, and Lua ConfirmCustomization (FUN_143607d80) just sends PlayerSet. **The client never blocks a
  locked option: the server must refuse it.**
- [P] FUN_1430a2b30 is the plain "option known" test (admin or vtable +0x8).

### Set B (type 4) = ArtTint IDs — [P]
- Tint list **FUN_1430a4720** (OptionType 2, data source "TintHash"): for each tint hash of the slot it gets the tint
  id from the hash map at DAT_145431310 + 0x2980 (key = alias hash at +0x18, value = tint id; filled by
  FUN_143cd0ef0 from the tint table: `+0x1910` id → hash, `+0x2980` hash → id), then lists the tint if admin, or
  ArtTint(id) (CharacterCreateOptionsManager DAT_145439c60 +0x120 = ArtTints definitions, lookup FUN_1430984b0 keyed
  by row +0x58) has DEFAULT_UNLOCKED (+0x40, parsed by FUN_143db52d0), **or set B contains the tint id** (vtable
  +0x10), or FUN_1430a3210 (store sells an unlocking item of the tint). FUN_1430a2b80 is the same "tint known" test.
- [P] FUN_1430a3210 / FUN_143098500 (tint → unlocking items): DAT_145439c60 +0x128 = ArtTintGroupUnlockDefinitionManager
  (ctor FUN_143c6a830): `HashList<int>` at +0x38 (count +0x40, buckets +0x60) maps ART_TINT_ID → UNLOCK_GROUP_ID
  (ArtTintGroupUnlockTint.txt), `HashList<ArtTintUnlockGroup>` at +0x8 (count +0x10, buckets +0x30, key +0x50) gives the
  group's UNLOCKING_ITEM_ID at +0x3c (ArtTintGroupUnlock.txt). So the tint key on every path is the **ArtTint ID**,
  not the group id and not the item id.
- [P] ids line up: ArtTints.txt (0.695) rows 1165-1210 are the only DEFAULT_UNLOCKED = 0 tints (1165 DarkElf_Skin_14
  with no unlocker; 1166-1210 Human_Skin_090-092, 100-141 = the 45 ArtTintGroupUnlockTint rows). The server's
  ArtTints.json list `Unknown1` {id, alias hash} has the same ids, and each hash equals the client's one-at-a-time
  hash of the alias (checked for all 46: e.g. 1166 Human_Skin_090 → -1552204180).
- [P] The PlayerSet (0x11C sub 1) slot tints are these alias hashes: FUN_1430a5f10 (the Parlor price) looks the
  slot's tint field (+8 of each 12-byte slot entry) up in the same +0x2980 hash → id map.

## 3. What an unlocker grants

| Item | Name / description / icon [P] | Options (type 3) | Tints (type 4) | "Already" text [P] |
|---|---|---|---|---|
| 4419 Fantasy Parlor Pack | 577355 / 577356 ("When consumed from your inventory … can be traded …") / 21675 `Conucopia01` [I: the one non-option icon in Images/icons_barbershop and in the parlor icon block 21667-21680] | the 24 rows with UNLOCKING_ITEM_ID 4419 (faces 203, 204, 214, 215, 230, 238, 253, 254, 259, 269, 277, 285, 290, 307, 316, 321; cat eyes 329-336) [P data] | groups 2, 5-9 → 45 tints 1166-1210 [P data] | 577416 |
| 4521 Cowboy Hat | 577699 / 577700 / 21775 wearable_Hats_Cowboy | 295 + equalities 296, 297, 298 | — | 577705 |
| 4522 Ninja Mask | 577701 / 577702 / 21776 wearable_Hats_Ninja | 305 + 303, 304, 306 | — | 577706 |
| 4523 Samurai Helmet | 577703 / 577704 / 21777 wearable_Hats_Samurai | 301 + 299, 300, 302 | — | 577707 |

- [P data] Only the hat "(master)" rows 295 / 301 / 305 carry the item id; the other race / gender rows (296-300,
  302-304, 306) have item 0 and DEFAULT_UNLOCKED 0, so FUN_1430a33a0 never lists them unless set A holds them.
- [I] The server therefore grants the item's rows **plus their CharCreateOptionEqualities associates**
  (rows 1349-1357 tie 295 → 296-298, 305 → 303/304/306, 301 → 299/300/302). Backed by the item texts ("Unlocks access
  to the Cowboy Hat (Hair) option for Luminaries **of all genders and sizes**", 577700 / 577702 / 577704); without
  the associates a male or large character could never see the hat they unlocked. For 4419 the associates are rows
  that already carry 4419 themselves.
- [I] Category 289 "Parlor Option" (577338) for all four (the category exists in the 0.695 category dump; no login-list
  item uses it). Tradeable: 577356 says the pack can be traded; hats were loot (P6505).
- [I] 577364 ("all of the faces in this pack") belongs to a faces-only pack, not to 4419; 4419 uses 577416.
- [I] No success notice exists for these items in the text table; the client's own refresh of the open Parlor (sub 2
  → FUN_1430a33a0) is the visible effect.

## 4. Server rules derived (implemented in LandmarkEmulator.WorldServer/Game/Unlockers.cs)

1. Use (Inventory 0x2D sub 0x15 ActivateItem → ItemUses.TryActivate → Unlockers.TryUse): grant the missing option /
   tint ids, persist them (Unlocks kind 23, value = type << 24 | id), consume one item, send AddKnowledgeResponse
   (one packet per type, only the new ids). Nothing missing → ShowActivationFailure with the item's text.
2. Login: SendSelfToClient lists A (options) and B (tints) from the stored rows.
3. Purchase (PlayerSet): refuse a look that picks a DEFAULT_UNLOCKED = 0 option or tint the character does not know,
   unless it is what the slot already wears (or, for options, counts as the same via CharCreateOptionEqualities).
   Prices are unchanged: the client's price (FUN_1430a5f10) never looks at knowledge.

## 5. Still open
- Live's success feedback on use (none found; [I] none).
- Whether live also granted single-option / single-tint unlockers (texts 577339-577353, 577362-577363, 577427-577436
  name them; refusal texts 577178 / 577179) — the client maps every such row to 4419 only, so they would have been
  Marketplace-only products, not separate unlock rows (OPEN-LEADS).
- Marketplace products for 4419 (MarketName / MarketCost need a store entry; TASKS 4.9 store part).
