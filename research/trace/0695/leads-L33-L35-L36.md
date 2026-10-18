# Leads L33, L35, L36 (OPEN-LEADS.md) — research 2026-10-15

Read-only research from the 0.695 decompile (re/decomp_all_695), the 0.589 decompile (re/decomp_all), gameData695,
the 0.695 client files under <0.695 test copy> (Resources, UI Lua), re/as3_695, text_589/695.tsv and the patch notes.
Addresses are 0.695 unless marked "0.589". **[P]** = proven from client code / data, **[I]** = inference.

---

## L33 — ClaimsPacketPlayerEnteredClaim 0xF9 / 0x63 (C→S)

### What the client does around it [P]

FUN_142dc8ec0 (0.589 FUN_142d2ad00; the 0.695 version only appends steps 5 and 6) runs, in this order:

1. When the local character guid (BaseClient +0x32508) is valid: **RatingPacketCanVoteRequest 0xA7 / 0x18** with the
   strings "Claims;Contests" / "Claims;" and the claim guid (already answered by Ratings.HandleCanVote, Ratings.cs:83-114).
2. Adds the claim to the entered-claims list (+0x410 … +0x438), sets the current claim (+0x348) and calls FUN_142dbde10.
3. Claim time / music (FUN_142dc7540, OwnerTimeHour / VisitorTimeHour …).
4. Lua `GameEvents:OnEnterClaim`, `HudStateManager:OnShowBuildModeCamera`; for the owner also `OnShowBuildModeTime` /
   `OnShowBuildModeIcon`.
5. **New: PlayerEnteredClaim** {u16 0xF9, u8 0x63, u64 claim guid} through FUN_142da8230.
6. **New:** if the claim entry's game (+0x140) is set and the instanced-game manager (DAT_145438110) exists:
   InstancedGameGetConfigurationRequest 0x10D / 0xD (FUN_1434eca50; packets-695.md §5d).

Callers: FUN_1436da2d0 (claim lookup FUN_1436da260 → FUN_142dbc0b0; while the player state is 0x20 it only enters the
claim of the current game) and FUN_1430547f0 (closing Map Depths re-enters the current claim). So **the same claim
can be reported again without leaving it**, and there is **no "left claim" packet** (exit only raises Lua
`GameEvents:OnExitClaim` / `OnExitClaimBuffer`, part_0069.c:20534/20559).

No client code reads a reply. No walk-on field shows a visit count: the 0.695 ClaimWalkOnTextWindow data
(re/as3_695/ClaimWalkOnTextWindow) has NumVotes / NumHistoricVotes / CanVote / TimeCanVote / VoxelCount / game fields,
but no visitors. The only "visit" client strings are the time settings (VisitorTimeHour / Rate).

### Server-observable uses that need "player is on this site" [P texts, I link]

| Use | Evidence | Status in the emulator |
|---|---|---|
| Daily goal "Build Site Tourist" (575703 / 575727 "Build Sites visited") and achievements "Baby Steps" / "All Those Who Wander" (574914-574916 "N unique Build Sites visited") | text_695, DailyGoals.cs:37 | inferred from movement: DailyGoals.Moved (DailyGoals.cs:153-169) → Achievements.SiteVisited (Achievements.cs:826-831) |
| Reward-chest arrival notice `ClaimPlayerLoot.ChestsYouCantLoot` 577634 "There are chests on this build site that you have a lockout timer for!" | new in 0.695 CodeStringMappings; **no client code references the key** (server key, like PlayerLoot.OwnerCantLoot 577633 / ChestLocked 577651); "ClaimPlayerLoot" (site-level) vs "PlayerLoot" (per click) | not built (spec-reward-chests.md §5.8, U1) |
| Site game configuration after entry | step 6 above | InstancedGames.Editor.cs:311-401 (0.695 response sub is 0xE) |
| Contest vote state | step 1 above (unchanged from 0.589) | handled |

Timing [I]: PlayerEnteredClaim did not exist in 0.589 (June 2016); the only site-entry feature added between the two
builds whose text is server-only and site-level is the reward-chest lockout notice (reward chests 29 Aug 2016, P6411;
group loot / lockouts P6413, P6503). The visit goals existed in 0.589 (live counted them without this packet then).
Best reading: live used it as the authoritative "player stands on site X" signal (visits, the lockout notice), so it
did not have to recompute claim membership from positions.

### Still unknown
- Whether live sent 577634 on entry, on chest click, or both (spec-reward-chests U1). Nothing in the client decides it.
- Whether live counted visits from this packet or from positions (no client-visible difference).

### Server recommendation
- **Game/Claims.cs → HandleRequest** (switch at :242): add `case 0x63` (0.695 sub; does not collide, the 0.589 Claims
  ids end at 0x60 → 0x62): read u64 claim. Validate: the claim exists on `session.ZoneId` and
  `Claims.ClaimAt(session.ZoneId, session.Position)` is that claim (or the player is within the site's buffer);
  otherwise ignore (the client may also send it for a site whose record arrived first).
- Keep `session.CurrentClaim` (or a Claims-side map). **Dedupe**: ignore a repeat for the claim already current
  (Map Depths re-entry). Clear it in the movement path when ClaimAt no longer returns the claim (no exit packet).
- On a real change: call a single `Claims.Entered(session, claim)` that
  - moves the visit counting there: DailyGoals Tourist + Achievements.SiteVisited for non-owners, once per site per day
    (same rule as DailyGoals.Moved; keep Moved as the fallback / exit detector);
  - when reward chests exist (TASKS 3.11 / 4.14): if any chest on the site has a lockout row for this character,
    `SystemMessages.ShowKey(session, "ClaimPlayerLoot.ChestsYouCantLoot")` once per entry [I];
  - sends nothing else (no reply is read). Bandwidth: zero extra packets in the common case.
- The GetConfigurationRequest that follows needs the 0.695 response (sub 0xE) — already TASKS item for §5d.

---

## L35 — InventoryPacketShowActivationFailure 0x2D / 0x1E (S→C)

### Handler [P]
Inventory dispatcher FUN_142f202e0 case 0x1e (re-read 2026-10-15):
- Wire: **u16 0x2D, i16 0x1E, u32 text id** (each field bounds-checked; nothing else is read).
- Title: the literal "Activation Failure" (not a text id, not localized).
- Message: `DAT_145431178->vtable[+0x10](id, &out)` = plain lookup of a client text by id. **No parameter filling**:
  texts with `[*name*]` or `%d` would show raw.
- Shows Lua `CreateConfirmationDialog(title, message)` (UI/Default/ConfirmationDialog.lua:113) with no callback — a
  modal box with the message.
- The client makes no item-use check of its own: Lua `ClientInventory.ActivateItem` → FUN_143457220 only splits
  tools (ToolPacketActivate 0x117) from the rest (0x2D / 0x15 ActivateItem or the ability cast). Every refusal is a
  server decision.
- Separate, older channel (not this packet): FUN_142f01700 "HandleClientError" maps an item-line result code 0..10
  through FUN_143c500b0 to `Item.Success` / `Item.Error*` (560758-560768) for the Items trial / item-line packets.

### Which texts fit (all exist in text_695; none takes parameters)

| Refusal | Text id | Source / status |
|---|---|---|
| Fantasy Parlor Pack 4419, every option already unlocked | **577416** "You've already unlocked the Crimson Parlor options in this pack." | new in 0.695, no CodeStringMappings row → server-sent [P text, I packet] |
| A Fantasy **faces** pack / item whose faces are all known | **577364** "You've already unlocked all of the faces in this pack!" | new in 0.695, no mapping |
| Cowboy Hat 4521 / Ninja Mask 4522 / Samurai Helmet 4523 already unlocked | **577705 / 577706 / 577707** | new in 0.695, no mapping |
| Single Fantasy option unlockers (faces 577339-577349, cat eyes 577350 / 577362) already known | **577178** `Customization.OptionKnown` "You already know this customization option!" | 0.589-era server key |
| Fantasy Skintones unlockers (577352, 577427-577436) already known | **577179** `Customization.TintKnown` "You already know this customization tint!" | 0.589-era server key |
| Build Site Expansion with all slots unlocked | **571174** "You already have the maximum number of Build Site Expansions!" | 0.589-era text |
| Attached-site expansion at max | **571175** "You already have the maximum number of expansions for attached Build Sites!" | 0.589-era text |
| Prop recipe item, prop already known | **568756** "You've already learned this prop!" | 0.589-era text |
| Design item, design already known | **568826** "You already know the Design this item contains!" | 0.589-era text |
| Inventory 3711 / Vault 3710 / Design storage 3712 at max | no live refusal text exists; fallback **560768** `Item.ErrorFailedItemRequirement` "Error: Failed item requirement" (the max is stated in the descriptions 574289 / 574287 / 574291) | [I] |
| Title item already owned, recipe item already known, anything else without its own text | **560768** (generic) | [I] |
| Pets | no refusal text exists; 577546 says one active pet at a time → **swap, never refuse** | pets use the ability path (spec-pets.md §3.1) |
| Consumables on cooldown (ability items: potions, fireworks, figurines) | **not this packet**: the cast fails with `AbilityCastError.OnCooldown` 562080 (has `%d`, formatted by the client's cast-error path) | Abilities.cs |
| Ability-less consumable on cooldown | 571245 "Ability on Cooldown" (no parameters) | [I] |
| Reward chests | not item activation: placed as props; refusals are `PlayerLoot.*` keys via ShowSystemMessage | spec-reward-chests.md |

Grant notices such as 562804 / 567734 ("No Add Tool / Stone of Recall was given, since you already have one") are
not activation refusals.

### Still unknown
- Which refusals live sent through this packet vs. ShowSystemMessage (no capture). The new 0.695 refusal texts
  (577364, 577416, 577705-577707) have no CodeStringMappings key, which fits a packet carrying an id rather than a
  key, so they are the best-supported uses.

### Server recommendation
- **Game/ItemUses.cs**: add `public static void Refuse(WorldSession s, uint textId)` → enqueue
  `ZoneMessageOpcode.InventoryBase` with body `WriteUShort(0x1E)` + `Write(textId)` (same pattern as Expand,
  ItemUses.cs:141-157). Never send a text with placeholders. Do not consume the item.
- Use it instead of Chat.System at: ItemUses.Expand :132 (560768), Claims.UseExpansion :589 (571174; 571175 for
  attached), RecipeBook.LearnProp :198 (568756), RecipeBook.Learn :220 (560768), ItemUses.LearnDesign (add the
  "already known" check → 568826), Titles :105 (560768), and the new unlockers of TASKS 4.9 / 4.10 (577416 / 577364,
  577705-577707, 577178 / 577179).
- Ability-path items keep the cast-fail answer (AbilityCastError.*); add 0x1E only where the use goes through
  0x2D / 0x15.

---

## L36 — WorldEditingPlayEffectPacket 0x10C / 8 (S→C)

### The gate DAT_145431328 +0x153b [P]
- DAT_145431328 is the client options object; FUN_143489560 loads UserOptions `[General]`:
  +0x1539 ShowResourceRange, **+0x153a DemoModeSelf**, **+0x153b DemoModeOthers** (default 1), +0x153c
  DisplayPlayerGuildNames.
- DemoModeOthers = option **"Show Previews of Others' Build Actions"** (`UI.DemoModeOthers` 563834); Lua
  `GameSettings.GetDemoModeOthers` FUN_1435f7ec0 / `SetDemoModeOthers` FUN_1435f8500 → FUN_14348d8f0, which writes the
  ini and, when turned off, clears everything ClientWorldEditingEffectManager shows (FUN_1430810c0).
- The 0.589 dispatcher FUN_142fe3ae0 has the same gate (+0x1533). So a player who turned that option off ignores
  all 0x10C, PlayEffect included. The server cannot see the option; sending is harmless.

### The header u32 (+0x14) [P]
FUN_142c18330 reads u16 opcode → +8, u32 sub → +0x10, u32 → +0x14. The client fills it in its own 0x10C packets with
FUN_142cd2690 (0.589 FUN_142c38cc0): timeGetTime-based clock + the server sync offset (BaseClient +0x32be0 → +0x3c),
the same value PacketKeepAlive sends. No S→C case reads +0x14 (case 8 included). **Server: send its game clock in ms
(the value it syncs the client to), 0 is also accepted.**

### Body and effect table [P]
- Reader FUN_14307c4d0: header, **u32 effect id** (+0x18), FUN_142c38220 **vec4 min, vec4 max** (+0x20 / +0x30).
  Total 46 bytes.
- FUN_143d57870 → FUN_143d576a0 looks the id up in DAT_145431058, the **ActorCompositeEffectDefinitions** table
  (Resources/ActorCompositeEffectDefinitions.xml; missing id → log "Failed to QueueCompositeEffectAtLocation due to
  missing effect definition for Id #%d!", nothing plays).
- Position = 0.5 × (min + max) per component (constant 0x14468dcc0 = 0.5 ×4, read from Landmark64.exe), w
  included → send w = 1 in both. Rotation / scale identity: the effect is **not scaled to the box** (unlike the
  client's own volume effects, FUN_143087780, which scale by sqrt of the box area).

### What it is for
- Player tool edits are **not** it [P]: observers already get build-action effects through
  VoxelConstructionPacketResponseObserved 0xF4 / 10 → FUN_143166bd0 → FUN_143086570 / FUN_143086b50 (operation →
  `WorldEditingEffect.Volume_*` / `Shape*_24`), unchanged since 0.589 (functions.tsv "same").
- No composite was added for it: the 69 composites new in 0.695 are fireworks, counting-node digits, light bulbs,
  noticeboard and respawn markers.
- Best fit [I]: the Linking Area actions added 28 Jul 2016 (after 0.589): **577548 "...Destroy Voxels in this area."**
  and **577549 "...Heal Voxels in this area."** (P6183-P6186, P6201-P6208). They are server-initiated (no player
  construction op), work on a box (the area), and need a one-shot effect.
- Candidate ids (StringHashToValue `WorldEditingEffect.*`, same in 0.589): Volume_Delete **10541**
  (particle 10685 + sound 10340), Volume_Heal **10532** (particle 10677); also Brush_Delete 10616 / Brush_Heal 10617
  (particle + sound), Volume_Placement 10533, Volume_Smooth 10534, Volume_Paint 10540, Volume_PlacementNoDestroy 11829.
- Dynamite / creature terrain damage (P6427) go through abilities and their own effects, not this packet. The
  story-tool damage area (DamageCreaturesInArea) hurts creatures, not voxels: no PlayEffect.

### Still unknown
- Which composite ids live sent (no capture); 10541 / 10532 are the natural pair.
- Whether live sent one effect per action or several across a large area (the effect is unscaled).

### Server recommendation
- **Game/WorldEditing.cs**: add `PlayEffect(uint zoneId, Vector3 min, Vector3 max, uint compositeId)`: u16 0x010C,
  u32 8, u32 server clock ms, u32 composite, vec4 (min, 1), vec4 (max, 1); send to players in the zone within
  ViewRange (160 m) of the centre. Coalesce: at most one per area per second.
- **Game/StoryTools.cs**: add the actions 577548 / 577549 to the Linking Area kind (:204-209, today only
  DamageCreaturesInArea). Destroy = temporary voxel destruction of the area box (respecting the area's own "Allow
  Voxel Destruction" 577550 setting / the site rule) then `PlayEffect(…, 10541)`; Heal = restore player-set voxels
  only (P6201) then `PlayEffect(…, 10532)`. The voxel change itself goes out through the normal terrain updates.
