# Forum / Reddit notes for creature + chest loot soft-tune

Corpus re-check 2026-10-30 (landmark-forum MCP): ~1,559 threads / ~34,899 posts
(`xf` ~34,631 + `reddit` 268 posts / 75 threads).

## Reddit

Almost entirely EQNL alpha (2013–early 2014) — before Chaos Cavern combat/loot. Keyword sweep over
`reddit:*` for chest/loot/essence/cavern/campfire/Lumen/recipe/outpost/crystal found **no useful
inventories**. No plan change from Reddit. Re-search if more 2016+ Reddit is ingested later.

## Daybreak forum (usable)

Best thread: [Loot from chests, review?](https://forums.daybreakgames.com/landmark/index.php?threads/loot-from-chests-review.57897/) (`57897`).

| Evidence | Stable ids | Implication |
|----------|------------|-------------|
| Kill bags: Lumens, recipes, essences | `xf:603072` Shardiis | Confirms Courage / kill extras |
| Cavern kills can drop campfire-like loot; crystal + hidden chest give **more** | `xf:602522` Zorkon | Campfire-on-kill; chests/crystal above mob bags |
| Surface **cave** chests (loot chat): Monster Essence, wood, gems, Lightstone, Bottled Midnight, Lumens, ore, Potion of Minor Yield | `xf:606924` Tattle | High-value mixed bags, not tiny ore piles |
| **Outpost** chests: huge mats + Adventure/Monster/affinity essence + Lightstone + potions + Lumens | `xf:606925` Tattle | Lost Chest richer; consumables/essences |
| Biome affects chest mats | `xf:606938` | Optional later |
| Domino: every outpost Elite + secret treasure chest | `xf:594672` | Design already matches |
| Early-beta: rare outfit from cave chest; launch-era: “only lumens, recipes, props, mats” | `xf:601822` Graysilk | Do **not** add outfits to launch chest tables from this |
| Dec 2016 outfits — Elite vs chest vs Crystal unclear | `xf:609363` Mysstie | Leave to rare-drop / seasonal systems |
| Wish-lists (Crystal = Lumens + nice item) | `xf:597353` | Flavor only |

**Caveat:** Huge sand/rock/ice “unseen inventory” counts are dig noise; prefer loot-chat lines. No rate tables.

**Item ids used in content JSON** (client / starter kit / audits): Lightstone **231** (Inventory starter kit; ability “lightstone” 221 is not the item), Potion of Minor Yield **1101**, Bottled Moonlight **1110** (forum “Bottled Midnight” treated as Moonlight — second Tattle post names Moonlight), Cut Plain/Burled/Striped wood **2000/2001/2002**, raw woods **71/72/73**.

Soft-nudge amounts remain `[I]`; presence of mix is `[P forum]`.
