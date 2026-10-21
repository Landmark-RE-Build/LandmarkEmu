# Client-to-server shifts (0.589 → 0.695)

Some things the 0.589 client did by itself are no longer done by the 0.695 client, so the server has to do them. The
example that started this audit: right-click a tool in the bag. The 0.589 client put the tool on the hotbar itself
(FUN_1433aa0c0 → FUN_1433a0b80). In 0.695 that routine is FUN_14344e300, which nothing calls, while loading tip 351
("Right-Clicking a tool in your Inventory will send it directly to your hotbar for use.") still promises the
behaviour. The server now places the slot (gameplay.md "Inventory, equipment and the hotbar").

This document lists every shift of this kind that a full scan of both clients found, says what the server does about
each one, and records how the scan was made so that it can be run again. Addresses are 0.589 / 0.695 as marked;
[P] / [I] as in README.md. Scan date: 2026-10-21.

## 1. How the clients were compared

Both executables were read as call graphs: every direct call / jump (E8 / E9), every RIP-relative LEA of a function
start, and every 8-byte pointer to a function start anywhere in the file (vtables, Lua binding tables, callback
tables). Function bounds came from the two Ghidra exports. A function is **live** when a walk from a root reaches it,
where the roots are functions whose address is stored in data (they may be called indirectly), the entry point, and
code outside any known function. Conservative on purpose: anything reached through a pointer counts as live.
Functions were paired between the clients with the project's function catalogue (0.695 address → 0.589 address,
"same / changed / new").

| Client | Functions | Live |
|---|---|---|
| 0.589.1.128895 (Steam) | 234,539 | 133,397 |
| 0.695.1.132647 | 236,307 | 134,425 |

Seven scans, each looking for the same thing from a different side:

| # | Scan | Hits | Real shifts among them |
|---|---|---|---|
| 1 | Paired functions live in 0.589 and dead in 0.695 | 53 | 1: FUN_1433a0b80 / FUN_14344e300 (the hotbar). The rest are generic container / string templates folded into another copy, compiler cleanup funclets, the developer profiler report (FUN_142b5db20) and classes whose callers were inlined. |
| 2 | 0.589 functions without a 0.695 partner, live, using a string or class name | 51 of 577 | 0: every string and class they use is still used by live 0.695 code (catalogue pairing gaps). |
| 3 | Call edges X → Y in 0.589 whose paired 0.695 X no longer reaches Y (Y small and specific, X changed) | 36 | 1: the hotbar again (FUN_143457220 no longer reaches FUN_14344e300). The others are inlining (e.g. the repair currency filler FUN_14355bfd0 inlined into FUN_14360fa90) or pairing errors. |
| 4 | Lua bindings: every 0.589 callee (3 levels) whose 0.695 partner is not reached (6 levels) by the same binding | 19 bindings | 1: ClientInventory.ActivateItem. The Crimson Parlor and Game Table bindings show the two redesigns in §2. |
| 5 | Lua bindings compared after masking addresses, constants and variable names | 41 differ | 0 new: null guards added, inlining, argument counts. |
| 6 | Every C→S packet class: its live builders, and every native root / direct caller that reaches them, 0.589 vs 0.695 | 3 classes | 1: ActionManagerPacketRequestSetSlot lost its ActivateItem root. InstancedGameHUDPacket is §2 B; the third was a pairing error. |
| 7 | String literals: used by live 0.589 code but only by dead (or no) 0.695 code | 9 | The Game Table setting keys (§2 B); the rest is the profiler report. |

Plus the UI layer: the 0.695 ActionScript of every changed window compared with 0.589 for natives it no longer calls
(found: `Ui.IsInNewbieZone` in TeleportWidget, the renamed Parlor data sources, `Ui.Face` in CharacterCreateWindow),
and the plain-text Lua files in `UI/Default` (only widget ids, a cancel-button tweak and the character-create reset
differ).

**Result:** the hotbar right-click is the only place where the 0.695 client still has the 0.589 routine but no longer
calls it, and the only C→S request the 0.695 client stopped sending. The other shifts are redesigns: the 0.695
client asks the server for something it used to work out itself, or lets the server decide something it used to
refuse in the UI. All of them have server support already (§2).

## 2. The shifts

| | System | 0.589 client did itself | 0.695 client instead | Live evidence | Server |
|---|---|---|---|---|---|
| A | Hotbar: right-click a main-hand bag item | `ClientInventory.ActivateItem` → FUN_1433aa0c0: sent ToolPacketActivate, then FUN_1433a0b80 put the item on the first free slot of the bar being shown (else the first free slot), with ActionManagerPacketRequestSetSlot 0x9A sub 2 type 2 = item guid; nothing if a slot already held the guid [P] | FUN_143457220: ToolPacketActivate, then TransferItem bag → equipped (FUN_142f24db0). FUN_14344e300 has no call, jump or pointer reference [P] | Loading tip 351 and goal / help texts 571163, 571685 (0.695 text table) | **Done** (792ca98): InventoryHandler.IsRightClickEquip → WorldSession.PlaceOnActionBar sends ActionManagerUpdateSlot. Bar 0 first: see lead A1. |
| A2 | Hotbar: right-click an *equipped* main-hand item | Same branch: tool activate + hotbar slot | FUN_142f24db0 with "in bag" false sends TransferItem equipped → bag (unequip) [P] | — | Nothing to add: the client asks for the unequip itself and the server does it. |
| B | Site games: round start / end HUD | FUN_14343b9a0 (game states 3, 4, 6): switched the HUD state manager to 8 / 9 and filled the game HUD data source from an *empty* InstancedGameHUD record through FUN_143432bd0, so the HUD started blank; FUN_14343bad0 restored HUD state 3 [P] | FUN_1434ec8b0 / FUN_1434ec970 fire OpenScoreHUD / CloseScoreHUD and open / close HUD widgets 0x10, 0x12; the HUD filler FUN_1434e2f70 runs only from the packet dispatcher (FUN_1434ee920, InstancedGameHUD 0x10D sub 9) [P] | — | **Covered**: InstancedGames.SendHud on every state change, join, arrival at a spawn point and observer registration, and SendHudAll when bars change (docs/instanced-games.md). |
| C | Site games: game settings | Lua `InstancedGame.ApplyGameSettings` / `ResetGameSettings` (FUN_143431540) and the settings page (FUN_1434411d0) built the InstancedGames_* settings (teams, FFA, PvP, players per team, queue times) from local tables [P] | The only 0.695 code naming `InstancedGames_IsGameFFA` / `_NumberOfTeams` is FUN_143c1c060, which nothing references; settings come from GetConfigurationResponse 0x10D sub 0xD, rules are sent with AddOrRemoveRule 0x10D sub 0x18 (RulesetObject.UpdateValueBool) [P] | Patch notes on Game Tables (FEATURES) | **Covered**: InstancedGames.SendConfiguration (on entering the site and on joining), rule requests (docs/instanced-games.md). |
| D | Crimson Parlor selections | `CharacterCustomization.*` data sources filled from the local character (OptionType / OptionSlot / SelectedIndex / TintHash, 0.589 FUN_143000490) [P] | `StartCustomization` sends EquippedAttachmentRequest 0x11C sub 3; `CharacterCustomizationData.*` is filled from the reply (sub 4) [P] | — | **Covered**: Game/Barbershop.cs answers sub 3 with sub 4. |
| E | Travel: An Underground Adventure | TeleportWidget disabled the button in newbie zones (`Ui.IsInNewbieZone`) [P] | Button always enabled; the server decides [P] | Patch notes: "can now send you to Chaos Caverns in any zone", "now works on Pioneer Landscapes" | **Covered**: Caves.TravelRandom serves every landscape (TASKS 3.28). |
| F | Entering a build site | Walk-on text, music, Lua OnEnterClaim and RatingPacketCanVoteRequest (0.589 FUN_142d2ad00) | The same (FUN_142dc8ec0), then PlayerEnteredClaim 0xF9 sub 0x63 {u64 claim} [P] | — | **Covered**: Claims.cs reads it (no reply is read by the client). |
| G | Item activation refusals | No such message: the 0.589 inventory reader has no case 0x1E [P] | Shows the server's reason from InventoryPacketShowActivationFailure 0x2D sub 0x1E {u32 text id} [P] | — | **Covered**: ItemUses.Refuse. |
| H | Emotes and stances | Not re-traced in this audit; the scans above found no removed local animation call (docs/protocol.md "Emotes") | Plays what the server sends: RequestAnimationSelection (0x26 sub 0x65) to the player and observers | Wiki "Commands List" (words and messages) | **Covered**: Game/Emotes.cs (EmoteAuditRuleTests). |

New 0.695 requests that come with new features rather than replacing something the client did (Play Mode Rules,
placing a site from Site Management, Inspect, Bio, Story Node response buttons, buying a Design's missing materials,
the Game Finder, Mesh Shapes) are traced in trace/0695/ui-trace-695.md and handled by the server; they are not
listed above.

Client-only changes found on the way (no server work): hudstate.lua no longer checks `Voxel.AllowBuildingMode()`
before restoring the combat HUD; the Claim camera permission check (`Claim.NoCameraPermission`) is still made by the
client; Chat fade opacity and the "Social" options page; GuildWindow's rank edit button; the in-game HUD state 8 / 9
is no longer used (B).

## 3. Open leads

- **A1 Which bar a right-clicked tool goes to.** The 0.589 routine preferred the bar being shown (action bar manager
  +0x78), else the first free slot of any bar. The 0.695 client does not tell the server which bar is shown
  (`Ui.SetActiveActionBar` FUN_1435c8290 sends nothing, and no hotbar or tool packet carries a bar id found yet), so
  the server fills bar 0 first. Searched: the ActionManager 0x9A builders, ToolPacketActivate, Lua Ui.* hotbar
  bindings. Next: a runtime capture of the 0.695 client switching bars, and the live capture archive.
- **A2 Equip refused.** 0.589 placed the slot whatever happened to the tool activation; the server places it only
  when its equip succeeded. Whether live put a refused tool on the bar is not known yet (no capture of a refused
  right-click).
- **A3 Definition slots.** The 0.589 routine treated only an item-guid slot (type 2) as "already on the bar"; the
  server also skips the right-click when a type-1 (item definition) slot holds the item, because the 0.695 client binds
  such a slot to that item (FUN_14344daa0). Live behaviour for the starting bar's default tools is not captured.

## 4. Running the scan again

The scan needs both Ghidra exports (function index + decompile) and both executables. In outline:

1. Build each client's reference graph and live set as in §1 (the project's `re/tools/deadcode.py` builds the
   graph; a forward walk from its roots gives the live set).
2. Join the two live sets through the function catalogue (`trace/0695/functions.tsv`, 0.695 → 0.589).
3. Run scans 1–7. Most hits are noise from identical-code folding, inlining and catalogue pairing; check each by
   reading both decompiles, and prove a "never called" with `re/tools/refscan.py` on the 0.695 exe (no call, jump,
   LEA or pointer; only exception-table RVAs).
4. For each real shift: what the 0.589 client did, what the 0.695 client sends or expects, what the live game did
   (texts, patch notes, wiki), and whether the server does it.
