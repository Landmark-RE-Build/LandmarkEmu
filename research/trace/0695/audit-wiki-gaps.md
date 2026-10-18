# Audit: live rules vs the server — gaps left after the wiki / patch-note cross-references (2026-10-18)

Read-only audit. It compares the full wiki dump (`design/wiki/pages`, 2,340 pages), the 2016 patch notes and developer
posts (`design/forum/patches`, `design/forum/all` 2016 threads, `design/patchnotes-after-0589.md`, `design/game-rules.md`)
with what the server does. The server side is `trace/0695/FEATURES.md`, `TASKS.md`, `OPEN-LEADS.md` and the code in
`LandmarkEmu/LandmarkEmulator.WorldServer/Game/*.cs` at the current working tree.

Tags:
- **[P]**: proven, with a source path and a short quote.
- **[W]**: wiki or forum-player claim, not a developer statement.
- **[I]**: inferred.

Nothing here was edited outside this file.

## 0. How this pass was run, and what it did not repeat

- **Earlier passes.** wiki-systems.md ticks all 282 rule pages. wiki-content-audit.md covers the 1,575 content pages.
  launch-rules-audit.md covers all 1,625 `launch` patch items. A script check found **0** uncited `launch` items.
  This pass did not redo any of that.
- **Post-0.589 items.** 684 of the 852 `post589` items in patchnotes.tsv are not cited by id in any trace. P-ids shift
  when threads are added, so each server-relevant item was rechecked **by keyword** in trace/ and in the code. Most
  were either covered under another id or pure client/UI fixes. Examples of client/UI fixes: modular build windows,
  Esc closes windows, ambient volume, the LOD option, paste-progress text, a random default face, and the chat opacity
  sliders. Those are not listed here.
- **Wiki rule pages.** The rule-bearing pages were re-read for numbers and "can / cannot / only" rules: Inventory,
  Chests, Trading, Soulbound, Vault, Pocket, Party, Chat, Guild, Friends List, Watch List, Journal, Daily Goals,
  Luminary Score, Lumens, Death, Repair-o-Matic, Gallery, Player Studio, Showcase, Daybreak Cash, Build Sites,
  Build Site Management, Designs, Spire, Caves, Outposts, Lost Crystals / Chests, Combat, Monsters, Gathering, Mining
  Guide, Crafting, Stats, Character Creation / Sheet, Crimson Parlor, Grappling Hook, Pioneer Landscape, Servers and
  Landscapes, Commands List, User Interface and Beginner Guide. Each rule was then checked in the code.
- **Checked and already right** (not gaps, so you don't need to recheck them):
  - party share 200 m, chest share 50 m;
  - revive 15 Lumens, upkeep 7 / +1 per 2 login days / 90;
  - designs 6 M → 48 M;
  - daily goals: 30-45 Lumens, kept while in progress;
  - Luminary Score;
  - the guild "last on" date and the leader rank rename;
  - the full title list;
  - visitor claim time (owner only);
  - the Game Table rule "NPCs count toward the minimum only";
  - seasonal outpost drops;
  - the Floor Switch goal (absent from the capture);
  - the party bonus-range indicator (client, 200 m);
  - the cavern group-trip prompt;
  - the restart schedule;
  - likes don't decay;
  - goal Lumens update the HUD (WorldSession.SendItem → SendCurrencies).

## 1. Gaps, ranked by player impact

Status: **missing** = no FEATURES row, TASKS item or lead; **wrong** = the code contradicts the source; **partial** =
part built, or only a lead with no task.

| # | Source | Live behaviour | Our status | Where in our code | Proposed fix |
|---|---|---|---|---|---|
| 1 | [I] Item instances keep their state on live. [P] the server's own durability model: `Durability.cs:92-103` wears the item instance (`item.Wear`). | Trading, mailing or storing an item in a chest keeps its wear. Wear is per item instance. | **wrong** (free repair): every hand-over rebuilds the item from its definition, so `Wear` goes back to 0. Mailing a damaged weapon to an alt, or putting it in a chest and taking it out, repairs it for free and skips the Repair-o-Matic Lumen sink. | `Trades.cs:287` `to.GiveItem(definition, count)`; `Mail.cs:605` `session.GiveItem(attached[i].Definition, …)`; `Chests.cs:282` `session.GiveItem(item.ItemDefinitionId, count)`. `ChestItemModel` has no `Wear` field (Database.Character/Model/ChestItemModel.cs). | 1. Carry `Wear` through trades, mail attachments and chest items: add a column to ChestItemModel and to the mail attachment record. 2. Re-create the item with that wear, or move the CharacterItemModel row instead of re-creating it. 3. Add a harness test: a worn sword round-trips through trade, mail and chest and comes back with the same %. |
| 2 | [P] client text 561999 `Chest.ChestAccessCantTakeNoTradeItemIfNotOwner`: "Can't take non-tradeable items that didn't originally belong to you." (re/out/text_695.tsv; used by FUN_142e81590, trace/code-strings.md:2287) | NO_TRADE / soulbound items in a chest can only be taken out by the player they belonged to. | **wrong** (exploit): `Withdraw` has no NO_TRADE check. A site owner can deposit Lumen-bought, founder or soulbound items, and any visitor with chest access (Pillage and Plunder) can take them. This defeats the NO_TRADE rules that Trades.cs and Mail.cs enforce. | `Chests.cs:273-282` (Withdraw); only `Deposit` checks NoTrade (`Chests.cs:232-235`) | 1. Store the depositor guid on ChestItemModel. 2. In Withdraw, refuse a NoTrade item to anyone but its depositor, with the TransferItemResponse code that FUN_142e81590 maps to 561999 (read that function for the number). |
| 3 | [P] `DesignItems.cs:15-16`: "A trade gives the receiver a new item, so the copy moves to it". Trades handle this (`Trades.cs:288-293`), but chests don't. | A Player Design item (2433) carries one design: the template row is keyed by the item guid. | **wrong** (data loss): a Player Design item stored in a chest and taken out comes back as a new item. `DesignItems.Moved` is never called on that path, so the design is orphaned. Mail already refuses them ("can't be mailed yet"). | `Chests.cs:282` (Withdraw); `DesignItems.cs:64` (Moved) | Either refuse 2433 in chests, or call `DesignItems.Moved(oldGuid, newGuid)` on withdraw. The second is better and matches the trade path. |
| 4 | [W] wiki Trading: "The exception to items that can be traded … purchased Templates". [W] wiki Player Studio: purchased Designs cannot "be sent to other players via the in-game mail system". | Designs bought in Player Studio stay with the buyer and are never traded. | **wrong** (loophole): the Designs window's "Send to inventory" (Lua `Templates.ExportToItem`, Template.lua:29 per spec-design-categories.md §1.5) works on Player Studio copies. Export makes a tradeable 2433 item, so one purchase can be handed around for free, and the creator loses sales. `Templates.cs:55` says it outright: "designs traded as items are not restricted". | `DesignItems.cs:25-58` (Export: no `Templates.IsPlayerStudioCopy` check); `Templates.cs:57` | 1. In Export, refuse `Templates.IsPlayerStudioCopy(session.PlayerGuid, designGuid)`. 2. Mark any 2433 made from a non-own design NO_TRADE as a second line of defence. 3. Daybreak / starter designs are the separate open question U-DC6 in spec-design-categories.md. |
| 5 | [P] client text 563373 `Construction.OnlyClaimOwnerMayPlaceOrCopyTemplateInstances`: "Only the build site owner may place, copy or create Designs using Designs purchased…" [W] wiki Player Studio: "Purchased Designs can not be copied by visitors to your claim". | The site remembers where purchased designs were placed ("template instances"). Visitors, even with Get Inspired, cannot copy or Save-Design over them. | **missing**: only the *place* half is built. Copy and Save Design check only the site's copy permission. No record of placed Player Studio instances is kept. | `BuildingClipboard.cs:270` (copy: `Claims.MayCopyIn` only); `BuildingClipboard.cs:484-505` (place half) | 1. On a successful place of a Player Studio copy, store its box (site, AABB, product) in a `site_template_instance` table. 2. In copy / Save Design, refuse a non-owner whose selection overlaps an instance, with result 0x1A (the same text). 3. Recycle or delete clears the record when the volume is cleared. |
| 6 | [P] update notes 7 Jun 2016 (design/patchnotes-after-0589.md:30): "Moods and stances on creatures should now persist between sessions/site visits." | A story creature keeps the mood / stance a Story Node gave it. A player who arrives later, or comes back, sees it that way. | **partial**: `Perform` keeps only the mood in memory and keeps no stance. `PlayStoryAnimation` is a one-shot AbilityPlayAnim to the current viewers. Later arrivals, re-entering view, a re-zone or a restart all show the default (Calm / standing). | `StoryTools.cs:1477-1484`; `StoryTools.cs:654` (`Mood = 218`, runtime); `Creatures.cs:3332-3337`, `2300-2305` | 1. Keep `Mood` and `Stance` (and weapon visuals) per spawner in the story runtime, and save them with the site's story state. 2. Replay them when a creature becomes visible / full for a new viewer, as the weapon visuals already are. 3. Edit → Play / Reset restore them from the saved state. |
| 7 | [P] client text 577356 (Fantasy Parlor Pack): "there is a waiting period before purchased items become tradable". [W] wiki Trading: "waiting period … 12-24 hours". [W] wiki Pocket: "can be traded after a time limit". | Store (Marketplace) purchases are no-trade for a while, then tradeable. | **missing**: FEATURES.md:282 names it under "How it worked" but has no row, task or lead. Store items are tradeable at once. | `Store.cs:485` (`session.GiveItem(itemId, count)`); the checks in `Trades.cs:167-169`, `Mail.cs:258/699`, `Chests.cs:233` look only at the definition's NoTrade | 1. Add `TradeableAfter` (UTC) to CharacterItemModel, set on store grants; setting STORE_TRADE_DELAY_HOURS, default 24 [I], 0 = off. 2. Refuse trade, mail and chest moves before that time with the NoTrade results. 3. Open a lead for the exact live period, and check whether the client's item record carries a time (the Parlor Pack tooltip text suggests it shows one [I]). |
| 8 | [W] wiki Inventory: "you must remove anything within in 24 hours or it will be unrecoverable". [W] wiki Pioneer Landscape: "You have a limited time to move that flag". | Overflow items expire 24 h after they arrive. | **wrong** (contradicts the source, with no lead): items are kept until they are retrieved. | `Inventory/Inventory.cs:29-32`: "the live expiry isn't known, so overflow items are kept until retrieved" | 1. Timed delete on overflow items, using the client's own countdown (same comment: "The client counts down an expiry when an item carries a timed delete"). 2. Setting OVERFLOW_EXPIRY_HOURS = 24 (live default), 0 = keep. 3. Purge on login / tick. 4. Record the wiki source in FEATURES. |
| 9 | [P] update notes 21 Mar 2016: "You can place up to 50 chests on a claim." [W] wiki Chests: "up to 50 chests on their Build Sites (per account)". [P] the client has two distinct errors: 4 "Only [maxCount] chests can be placed" and 12, the category limit (`Props.cs:986-993`). | 50 storage chests per site (developer notes). The wiki says per account. | **wrong or undecided**: the server enforces **both**, 50 per site and 50 per account, so an 8-site builder is capped at 50 in total. FEATURES.md:128 lists only "50 per site". | `Props.cs:998` `MaxChestsPerAccount = 50`, `Props.cs:1051-1059`; per site `Props.cs:1046-1047` | Decide with the user (rule: live default + toggle). Keep 50 per site (developer source). Make the account cap a setting, CHESTS_PER_ACCOUNT, default **off** [I], unless a 2016 source for the account cap turns up. Note the two client errors as evidence that both existed at some point. |
| 10 | [W] wiki Player Studio: reviewers are players who spent "a minimum of 1000 Daybreak Cash"; "The name of the item creator does not appear anywhere"; "If an item is rejected, it will then be queued for review by staff". | Peer review is limited to established buyers and is anonymous. A rejection goes to a staff queue, not straight out. | **wrong / partial**: anyone except the creator reviews. The review payload names the creator (ItemOwner in the TemplateData XML). Enough rejections set `Rejected` with no staff step. | `Commerce.cs:993-1012` (SendProductForReview loads and sends `creator`); `Commerce.cs:1099-1104` (Rejected) | 1. Eligibility setting, default "spent ≥ 1,300 Lumens in the store" (1000 DBC × 1.3) [I]. 2. Blank ItemOwner in the review copy. 3. Rejections → a `StaffReview` status shown on the /admin page with approve / reject. 4. A lead for the live approval and rejection counts. |
| 11 | [P] forum patches 2014-03-28-27446 and 2014-12-15-51046: "the Founder's Chest at each hub should now properly restore anything you had missing". [W] wiki Spire (2016): the chest "will only grant missing items to those who participated in alpha and beta". | Founder's Chest prop 898 at every Spire re-grants missing founder package items. | **partial**: only described in a lead (agent-store-entitlements.md §F, L149 / L153). There is no TASKS item. `Entitlements.cs:45`: "the Founder's Chest prop 898 … is not built". | `Entitlements.cs:42-45`; `Spire.cs:96-108` (Resupply Chest 884 only gives starter tools) | 1. Place 898 in the Spire layout (as 884 is). 2. Interaction re-grants the missing items of the packages enabled by ENTITLEMENT_PACKAGES, with texts 562124-562126 / 562156-562158 [I]. 3. Only while FOUNDER_ENTITLEMENTS is on. |
| 12 | [P] update notes 30 Jun 2016 (design/forum/patches/2016-06-29-57596): "Corrected some issues with the slate slab placement on new player claims." | New players' automatically placed sites came with a slate slab, probably a floor under the Starting Location [I]. | **missing**: the new Pioneer site gets only the Starting Location. | `Pioneer.cs` (Place); `Claims.EnsureStartingLocation` | Lead first. Search the client's designs and templates (Daybreak designs, ExportedTemplates, starter designs) and the tutorial goals ("Your Home is Your Castle", Building Block Basics) for the slab's shape and material. Then stamp it under the Starting Location on new Pioneer sites. |
| 13 | [W] wiki Chat: voice "Local … Guild" channels, push-to-talk. [P] client: VivoxWrapper, `VoiceChatWidget` (trace/UI-AUDIT.md:881-888), Voice 0xB9. | In-game voice chat, local (proximity) and guild. | **missing, written off**: trace/systems/social.md:45 says "✗ VoiceJoinChannel / RequestCustomChannel (Vivox; not needed)"; social-service.md:168 says "out of scope". There is no TASKS item or lead, which breaks the BUILD-PATH rule 2 ("nothing written off"). | none (`Telemetry.cs:94` only logs VoiceChatEvent) | 1. Open a lead with the 0xB9 layouts, what live sent (Vivox account / channel URIs), and whether the client survives a "voice unavailable" answer. 2. Answer VoiceJoinChannel cleanly so the widget doesn't wait [I]. 3. Self-hosted voice stays a later option. |
| 14 | [P] the client shows a site's category limits in Build Mode (`PropLimits.cs:11-18`, ClaimDialogs.PropLimits). [P] update notes 25 May 2016: 125 creature spawners per site (`Props.cs:1005`). | Build Mode shows "n / 125 Creatures" alongside "n / 50 Chests". | **partial**: the 125 limit is enforced (refusal 12 with CreaturesCategoryName 573378), but UpdatePropCategoryCount (0x61) sends only Chests, so the counter never shows. | `PropLimits.cs:31,77` (only `ChestsName`); `Props.cs:1048-1049` | Add the Creatures category (573378, max 125) to the 0x61 list. Send it on entering a site and when a spawner is placed or recycled, coalesced like the chest count. |
| 15 | [W] wiki Chests: "There is a 'cool down' period for traveling instantly, so teleport to friend may not work if it was used recently." | Travel-to-friend (ability 187) has a cooldown. | **partial [I]**: the server never refuses a repeat, and calculated stat 53 `cStatTeleportCooldown` is sent as 0. Whether the client starts a hotbar cooldown from the ability record isn't known. | `Abilities.cs:442-470` (ability 187); `DefaultCalculatedStats.cs` (53 = 0) | 1. A lead for the length (the ability caster info for 187, and stat 53's meaning). 2. A server cooldown setting with a source-backed default once found; off until then. |
| 16 | [W] wiki Trading: "The largest stack of gathered materials … that can be sent at once it 9,000." | Mail attachments are capped at 9,000 per stack. | **missing [I]**: no cap in Mail.cs. Whether the client caps it is unknown. | `Mail.cs` (attachment checks around 217-260) | Low priority. Check the client's mail send path for a limit first. If there is none, add a 9,000 cap with the "attachment is invalid" result (5). |
| 17 | FEATURES.md:115 (Build sites): "+1 per day you log in"; FEATURES.md:124 repeats "7 / +1 / 90". | Every **2** unique login days add 1 day (wiki Build Site Management; forum 53943). | **doc wrong, code right**: `SiteUpkeep.cs:64` uses `count / 2`. | trace/0695/FEATURES.md:115, 124 | Fix the FEATURES wording to "+1 per 2 login days". |

## 2. Items for TASKS.md (not added; for the next bookkeeping pass)

1. **Item wear survives trade / mail / chest** (#1): ChestItemModel + mail attachment wear, re-create with wear; harness
   round-trip test. Fix this first: it is an economy exploit live right now.
2. **Chest NO_TRADE withdraw rule** (#2): depositor guid on chest items; refuse with the 561999 result (read
   FUN_142e81590 for the code).
3. **Player Design items in chests** (#3): call `DesignItems.Moved` on chest withdraw (or refuse 2433).
4. **Player Studio copies: no export to item** (#4) and **template-instance copy protection** (#5): instance table,
   copy / Save Design refusal 0x1A.
5. **Story creature mood / stance persistence** (#6): saved per spawner, replayed to new viewers and restored on reset.
6. **Store purchase trade delay** (#7): `TradeableAfter`, STORE_TRADE_DELAY_HOURS 24 (live default, togglable) + lead for
   the live period.
7. **Overflow expiry** (#8): OVERFLOW_EXPIRY_HOURS 24 using the client's timed-delete countdown.
8. **Chest cap decision** (#9): per-site 50 (developer notes) vs the extra per-account 50; make the account cap a setting.
9. **Player Studio review rules** (#10): reviewer eligibility, anonymous review copy, staff queue on /admin.
10. **Founder's Chest prop 898 at the Spires** (#11).
11. **Leads to open in OPEN-LEADS.md**: new-player slate slab (#12), voice chat 0xB9 / Vivox (#13), friend-teleport
    cooldown (#15), mail stack cap (#16), live store trade-delay length (#7), live Player Studio approval / rejection
    counts (#10).
12. **Small fixes**: Creatures category in UpdatePropCategoryCount 0x61 (#14); FEATURES wording for Active Days (#17).
