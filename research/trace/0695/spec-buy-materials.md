# Spec: Buy a Design's missing materials (TASKS 3.6)

Update Notes 14 July 2016 (patchnotes.tsv P6087; full post design/forum/all/2016-07-13-57682-update-notes-14-july-2016.md
lines 32-38). Research date 2026-10-15. Builds on packets-695.md §3, ui-trace-695.md (TemplateConfirmationWidget),
OPEN-LEADS L29 / L57. Here **[P]** = proven from the client or the tables, **[I]** = inferred.

## Summary

- The **client** finds what is missing and shows the "Get Resources Now" button. The **server** works out the bundles
  and the price. The client never computes bundles: it only shows the rows the server sends, then echoes them back.
- There are three packets, all in TemplateBase 0xF0: 0x1D DataRequest (C→S), 0x1E DataResponse (S→C) and 0x1F
  PurchaseMissing (C→S). Nothing replies to 0x1F. The result shows only through the normal item and currency updates.
- Bundles come from the Lumen Station (crafting station 55, CraftingStationRecipeMappings). Each of its 943 recipes
  has exactly one component, item 1579 Lumen (type 1), and makes one output item. No item has two recipes, so each
  item has exactly one bundle size (Recipes ITEM_COUNT) and one price. **[P]** (gameData695 scan)
  - Voxel materials are sold in bundles of 2000-24000 for 130 Lumens. The forum example is 4000 amethyn for 130.
  - Props are sold 1 at a time for 65 / 130 / 260 / 520 / 780 Lumens, or 4 / 8 / 20 at a time.
  - 545 "Recipe: …" items are sold 1 at a time. They have a VISIBILITY_REQUIREMENT and MAX_QUEUE 1.
- What the server must add:
  - a handler for 0x1D that answers with 0x1E built from its own copy of the design, followed by a currency refresh;
  - a handler for 0x1F that checks the request, takes the Lumens and grants the bundles at once.
- Almost everything else can be reused: Templates, BuildMaterials, Props recipe cost, RecipeBook, the
  WorldSession item and coin helpers, and GameTableManager.

## Client flow (0.695 addresses)

1. **Design Details window** (SelectedTemplate.swf, `SelectedTemplateDocumentClass.as`).
   - `BuyResourcesButton` ("Get Resources Now", as 154) is visible when either:
     - some `Character.Template.Selected.MaterialItems` row has CountRequired > CountUsed, or
     - some `Character.Template.Selected.Props` row has an ErrorString (as 243-268). **[P]**
   - Clicking it (as 362-365) does three things:
     - calls `Templates.RequestPurchaseTemplateMaterialsData(guid)` (luabind 0x1447ae1e0 → FUN_1435dc520 → builder
       FUN_143113530 → 0xF0/0x1D);
     - opens the widget with `HudStateManager:OnOpenTemplateConfirmation()` (HUD widget 219);
     - plays sound 10330. **[P]**
   - `getResourcesString` (as 295-334) is dead code: nothing calls it, and `SelectedTemplate.lua:52 GetResources` is
     a stub. It is still useful evidence of what was intended, because it lists "1x Recipe:<prop>" for each prop
     with missing knowledge and then "Nx <material>" for each shortfall. **[P]** (dead) / intent **[I]**
2. **How the client works out what is missing**. FUN_143115b90 calls two functions over one shared item→count hash:
   - **FUN_143115670 (materials).** For each entry of the design's material map:
     - skip material 0xFFFC;
     - look up the material's item: material record (FUN_1431521c0) +0x28 → +0x90, which is VoxelMaterials ITEM_ID;
     - skip it if the item is 0 (free).
     
     Then CountRequired = sum of the voxel counts per item (1 item per voxel), and CountUsed = items held
     (FUN_142f1f760). It fills `…Selected.MaterialItems` {ItemId, ItemName, ImageId, CountRequired, CountUsed}. **[P]**
     (part_0086.c 11141-11290)
   - **FUN_143115d40 (props).** For each prop kind:
     - Prop items already held cover what they can.
     - If the player has no knowledge of the prop recipe, the row gets "Templates.Props.MissingPropKnowledge".
     - Otherwise it counts how many props can be crafted from the stock left after the materials step (PropRecipeComp:
       held / component count). Only that craftable amount is added to the component rows. A shortfall gives
       "Templates.Props.NotEnoughResource".
     
     So **missing prop components are not shown in MaterialItems**. The only sign of them is the prop's error string.
     **[P]** (part_0086.c 11429-11830)
3. **0x1D request**. FUN_143113530 sends it only when the design is in the client's design-data cache. It clears
   `Character.Templates.PointOfSale` (FUN_142d62ee0) before sending. The body is the guid followed by the whole design
   as the client holds it (packets-695.md §3a). **[P]**
4. **0x1E handler FUN_14310c290**. Reader FUN_143100050, row reader FUN_143101520 → FUN_1430ff330 (8 × u32).
   - It refills PointOfSale.
   - Row 0 holds {FoundLumenRecipeForAllItems = response +0x20, TotalLumens, TemplateGUID}.
     - The +0x20 field is never read from the wire: the dispatcher sets it to 0, so the flag is always false.
     - The widget never reads it, so it does not matter.
   - Rows 1..n each hold {RecipeId, RecipeCraftCount, LumensNeededToCraft, ItemDefinitionId, ItemCountToBeCrafted,
     ItemCountNeeded, Icon, Name}.
     - `Name` is a **text id**. It is turned into a string through DAT_145431178 vtable +0x10, the same call that
       resolves item name ids in step 2.
   - It does not touch `Character.Currencies`. **[P]**
5. **TemplateConfirmationWidget**. Source: `re/as3_695/TemplateConfirmationWidget/.../TemplateConfirmationWidget.as`,
   338×487.
   - **List** (as 123-141): one row per data row from 1 to n. `TemplateListItemRenderer.as` draws the icon with
     `SetImageBySetId(Icon)` and shows `ItemCountToBeCrafted + "x"` next to the Name. ItemCountNeeded is passed to the
     renderer but not drawn. **[P]**
   - **Cost line** (as 89-116): TotalLumens from row 0, compared with the `Character.Currencies` row "Lumen" Quantity.
     - If TotalLumens is greater, it shows "You need N more lumens!" and the **Buy Lumens** button
       (`Browser.OpenMarketplace`, FUN_1435e1440), and hides OK. This is the "first prompts you to purchase lumens"
       from the patch notes.
     - Otherwise it shows OK. **[P]**
   - **Redraw quirk (L57)**. "Text" is invalidated only at init (as 63) and when currencies change (as 176-179).
     PointOfSale events only invalidate "ItemList" (as 171-174). The widget opens before 0x1E arrives, so the cost line
     keeps showing 0 (with OK visible) until a currency update comes in. **[P]**
   - **OK** (as 159-161) calls `Templates.PurchaseMissingTemplateMaterials(row0.TemplateGUID)` (FUN_1435dc570 →
     FUN_1431118e0) and then closes the widget. **Cancel** only closes it. **[P]**
6. **0x1F builder FUN_1431118e0**.
   - It reads PointOfSale. If row 0's TemplateGUID differs from the argument, it sends nothing.
   - It collects every row with RecipeId > 0 and RecipeCraftCount > 0, up to `Array<RecipeCraft,100>`.
   - It sends **only when the list is not empty**, then clears PointOfSale.
   - It writes u16 0xF0, u8 0x1F, u64 guid, u32 n × {u32 RecipeId, u32 RecipeCraftCount}, u32 TotalLumens. The row
     writer is FUN_143100af0 → FUN_1430fe6d0. **[P]**
   - So pressing OK before 0x1E arrives sends nothing, because the clear in step 3 removed the guid. **[P]**
7. When the Selected template data source changes, the widget is closed (`OnCloseTemplateConfirmation`, as 345). So
   does closing the Details window (SelectedTemplate.lua 19, 26). **[P]**

## Packets

TemplateBase family (`ZoneMessageOpcode.TemplateBase` 0xF000, u8 sub first in the body). Channel Zone:1
(trace/architecture/channels-695.tsv 492).

| Sub | Dir | Body after the sub byte |
|---|---|---|
| 0x1D DataRequest | C→S | u64 guid · TemplateBasicData (0.695) · cell list · PropData list. **The server reads only the guid.** |
| 0x1E DataResponse | S→C | u64 TemplateGUID · u32 n (≤ 100) × {u32 RecipeId, u32 RecipeCraftCount, u32 LumensNeededToCraft, u32 ItemDefinitionId, u32 ItemCountToBeCrafted, u32 ItemCountNeeded, u32 Icon (image set id), u32 Name (text id)} · u32 TotalLumens |
| 0x1F PurchaseMissing | C→S | u64 TemplateGUID · u32 n × {u32 RecipeId, u32 RecipeCraftCount} · u32 TotalLumens |

- Do not write a FoundLumenRecipeForAllItems byte: the reader does not read one. **[P]**
- No reply to 0x1F exists. Template S→C dispatcher FUN_14310bbf0 has no case above 0x1E. **[P]**

## Server design

### Reuse

- `Templates.FindDesign(owner, guid)` (Templates.cs:46) and the starter designs (`Find`, 198).
  - `Templates.Materials(design)` (Templates.cs:227) yields (material, voxel count). **It is private; make it
    internal.**
  - `Templates.PropsOf(design)` (Templates.cs:342) gives the props.
  - Add the new subs to `Templates.Handle` (Templates.cs:504-531). Today both fall through to the default branch at
    526 (coverage-695.md 2151).
- `Terrain.BuildMaterials.ItemFor(material)` (BuildMaterials.cs:36) maps material → item, with 0 = free. It is the
  same as the client's +0x28 → +0x90 lookup (`VoxelsPerItem = 1`).
- `Props.RecipeCost(propId)` (Props.cs:1433; private, so expose it) gives the prop recipe components.
  - `ItemDefinitionManager.ItemsForProp(propId)` (ItemDefinitionManager.cs:254) gives the prop items held, as in
    `PlacementItem` (Props.cs:1489).
  - `RecipeBook.KnowsProp` (RecipeBook.cs:85) gives the knowledge check.
  - Together these mirror the client's prop rules and the server's own placement rules (Props.cs:355-360).
- `GameTableManager.Instance.Recipes`, `CraftingStationRecipeMappings`, `ComponentsOf(recipeId)`
  (GameTableManager.cs:48). Use the same mapping check as `Crafting.TryStart` (Crafting.cs:137-139).
- `ItemDefinitionManager.Get(id)` provides `NameId` and `ImageSetId` for the Name and Icon fields.
- `session.ChangeCoins(-cost)` (WorldSession.cs:605) takes the Lumens (item 1579, `Inventory.CurrencyItem`,
  Inventory.cs:42). `session.GiveItem` (WorldSession.cs:221) grants the bundles and sends item updates. Any change to
  item 1579 already calls `SendCurrencies()` (WorldSession.cs:278-279, 586), which updates `Character.Currencies`.
- `Chat.System` (Chat.cs:202) for refusal text (as Barbershop does).

### New: `Game/DesignMaterials.cs` (or a section of Templates)

**Lumen Station index, built once.** Map item → (recipe, ItemCount, lumen cost):
- take every CraftingStationRecipeMappings row with station 55 and RecipeId ≠ 0 (5 rows have recipe 0; skip them);
- take its one component of type 1 with ComponentId 1579; the cost is ComponentCount;
- if an item somehow had two recipes, keep the lowest cost per unit. None do in 0.695.

**0x1D: `SendPurchaseData(session, guid)`** (only when `session.Speaks695`):

1. `design = FindDesign(session.PlayerGuid, guid)`. If it is not found, send 0x1E with n = 0 and total 0, so the
   window shows an empty list. **[I]**
2. Work out the shortfall per item, in this order (the client's own order, step 2):
   1. **Materials.** For each (material, count) of `Materials(design)`: `item = ItemFor(material)`. Skip item 0 and
      material 0xFFFC. Add the count to `need[item]`.
   2. **Props.** Group `PropsOf(design)` by PropId into kinds, then for each kind:
      - First subtract the prop items held (summed over `ItemsForProp`).
      - If `!RecipeBook.KnowsProp`: add 1 of the Lumen Station "Recipe:" item that teaches that prop, if one is sold.
        Look it up from RecipeBook's item → recipe / prop maps; this needs a new `RecipeBook.TeachingItemsFor(propId)`.
        Then still count the prop's components as below. **[I]**
      - Otherwise, or after the step above, add `remaining × RecipeCost(propId)` to `need`.
   3. **Shortfall.** For each item in `need`: `missing = need − Inventory.CountOf(item)`. Keep it if it is > 0.
      - This server-side shortfall includes prop components. The client's list does not show those (step 2), but the
        patch note promises "you'll be able to place the design as normal". **[I]**
3. **Rows.** For each missing item that has a Lumen Station recipe:
   - RecipeId = recipe;
   - RecipeCraftCount = ceil(missing / ItemCount). This is the forum's 4000-for-3999 rule. **[P]** (forum line 38)
   - LumensNeededToCraft = RecipeCraftCount × cost. **[I]** It could be the per-craft price instead; no UI reads it.
   - ItemDefinitionId = item;
   - ItemCountToBeCrafted = RecipeCraftCount × ItemCount;
   - ItemCountNeeded = missing;
   - Icon = item ImageSetId;
   - Name = item NameId (Recipes OVERRIDE_NAME_ID is 0 for all station-55 recipes).
   
   Items with no Lumen Station recipe are left out (that is the never-sent FoundLumenRecipeForAllItems = false case).
   Stop at 100 rows. Sort by the material order, then props. **[I]**
4. TotalLumens = sum of LumensNeededToCraft. Send 0x1E.
5. **Then send `session.SendCurrencies()`** (PlayerUpdate 0x49) so the widget's cost line redraws with the real total
   and the right OK / Buy Lumens choice (L57). The fix is **[I]**; the need is **[P]**. It costs about 20 bytes, sent
   once per click.

**0x1F: `Purchase(session, body)`:**

1. Parse the guid, n (refuse n > 100), the rows and TotalLumens.
2. Check every row:
   - RecipeId is mapped to station 55 in CraftingStationRecipeMappings;
   - RecipeCraftCount is between 1 and a sane cap (e.g. 1000);
   - the recipe's only component is 1579.
   
   Refuse the whole purchase on any bad row and log it (as `Crafting.TryStart`).
3. Compute the price as **cost = Σ count × component count**, from the tables. Never trust the client's TotalLumens.
   - Option A, recommended: also keep the last 0x1E offer per session (guid → rows) and refuse if the echo differs,
     because the client sends back exactly what it was given.
   - Option B, simpler: accept any valid station-55 rows at list price. That is no worse than the Lumen Station
     window itself.
4. If `!session.ChangeCoins(-cost)`, reply with `Chat.System(session, "You need {cost − Coins} more lumens.")` and
   stop. **[I]** No packet for this exists, and the widget normally prevents it.
5. For each row, `session.GiveItem(recipe.ItemDefinitionId, recipe.ItemCount × count)`. Give everything at once and do
   **not** queue it in Crafting: the patch note says "resources will be added to your inventory", and casting time is
   0.25 s per craft anyway. The materials go to the resource window through the normal inventory placement.
6. Log `"{name} bought materials for design {guid}: n bundles, cost lumens"`.
   - The client re-checks the Design Details window by itself when the item and currency updates arrive, through the
     MaterialItems and Props sources. If the window does not refresh, re-send `Templates.SendUpdated(session, guid)`
     (Templates.cs:241). **[I]** Check this in the test.
7. Optionally call `Achievements.Crafted` per recipe, like a station craft (Crafting.cs:249). Unknown whether live did.

**Bandwidth.** A reply is at most 4 + 8 + 4 + 100 × 32 + 4 bytes, about 3.2 KB, and is sent only on a click. The 0x1D
request can be large (it carries the whole design) but it is client-sent and read only for its first 8 bytes.

### Test (0.695 test copy only)

1. Take a design that needs a material you do not hold, e.g. Cut Marble with fewer than its voxel count in the bag.
2. Open Design Details and click "Get Resources Now". Expect:
   - the list to show "7500x Cut Marble";
   - the total to read 130 × crafts;
   - OK to be visible when you hold enough Lumens, and "Buy Lumens" when you do not. **Do not click "Buy Lumens"
     in tests**: Browser.OpenMarketplace (see the client-external-browser memory).
3. Click OK. Expect the Lumens to drop by the total, Cut Marble to be in the resource window, and the Buy button to
   be hidden.
4. Read the client's own state through Lua: `Character.Templates.PointOfSale` rows, and `Character.Currencies`.

## Unknowns

- **U1** Whether live included "Recipe:" items for props with missing knowledge, and prop components.
  - For: the dead `getResourcesString` lists "1x Recipe:<prop>".
  - Against: a player guessed "just the mats" (forum line 149-158, no developer answer).
  - Default: include both. Put it behind a constant so it can be switched off.
- **U2** Whether LumensNeededToCraft is per row or per craft. No reader in the UI.
- **U3** Whether live sent a currency update after 0x1E (L57), or whether its cost line stayed at 0 until something
  changed.
- **U4** What happened on failure (not enough Lumens, design gone), since no reply packet exists. Chat text is our
  choice.
- **U5** Whether the 0x1D design body (client copy) was used by live instead of the stored design. We use the stored
  one and never trust the client.
- **U6** VISIBILITY_REQUIREMENT_ID (1000+) and MAX_QUEUE 1 on the 571 recipe-item / unlock recipes in station 55. The
  server does not check either yet. They may hide recipes the player already knows, which matters for U1. Logged
  with L29 / L57; add to OPEN-LEADS if this is implemented.
