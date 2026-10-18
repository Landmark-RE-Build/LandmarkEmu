# Spec: cosmetic pets (TASKS 3.10 / 4.11, OPEN-LEADS L24 / L27)

Written 2026-10-15 from existing research plus a new trace of the 0.695 client. Read-only research; nothing in
LandmarkEmu was changed. "Proven" = seen in client code, client data or the patch notes. "Inference" = the most
likely reading, not proven.

## 1. Summary

- The 28 Jul 2016 update added 7 figurine items. Right-click one to summon its pet, which follows you. You can have
  one pet at a time, and switching is allowed. Cancel the pet icon above your health bar to dismiss it. Six
  figurines were sold in the Marketplace. The Lop Ear Bunny was a rare drop from Outpost elites (P6142-P6147,
  forum thread 57751, re/out/forum_raw/57751-1.html).
- **The 0.695 client has no pet code.** There is no new class (re/out/rtti_new_695.txt), and no new packet or
  handler. All it adds is data: 7 models, 7 icons, texts, item category 291 "Pet", keyword 86, tag 623 "Cosmetic
  Pets", and profile 379 renamed to "Non-Combat Entity". So everything about pets was done by the server with
  systems the client already had:
  - the item definition (sent by the server);
  - the item use (an ability cast or Inventory 0x15 ActivateItem);
  - a buff icon (Abilities 0x1C EffectInitialized) that the player cancels with Abilities 0x1E RequestCancelEffect;
  - an NPC spawn (PlayerUpdate 0x59 AddLightweightNpc) using the pet model;
  - movement updates from the server (Position 0x7F), as the emulator already sends for creatures.
- The emulator already has every one of these parts: Buffs.cs (icon and cancel), Consumables.cs / ItemUses.cs
  (item use), Creatures.cs (NPC spawn and movement body). What is missing:
  1. item definitions for items that are not in the dump (TASKS 4.8 definition builder; this blocks pets);
  2. a small `Pets` module: one follower per player, shown to everyone on the owner's landscape and moved behind
     the owner;
  3. the Marketplace entries and the Lop Ear drop.

## 2. Items

Texts are from re/out/text_695.tsv (new in 0.695: trace/0695/text-new.tsv). Icons are ImageSets ids and models are
Models.txt rows (gameData695/Models.txt; re/out/agent_items/diff_Models.txt). The table matches items-695.md §1.3.

| Figurine (item name) | Name text | Item description | Pet name (NPC) | Icon | Model (Models.txt) | Source |
|---|---|---|---|---|---|---|
| Shadow Wolf Figurine | 577507 | 577508 | 577510 Shadow Wolf Pup | 21740 Pets_Wolf_Pup01 | 1732 Char_Wolf_SpiritWolf_Entities_WolfPup_00_Chest.adr | Marketplace |
| Stag Figurine | 577512 | 577513 | 577514 Young Stag | 21739 Pets_Deer_Fawn01 | 1733 Char_Deer_Deer_Entities_Fawn_00_Chest.adr | Marketplace |
| Cockatrice Figurine | 577515 (2nd copy 577547) | 577516 | 577517 Baby Cockatrice | 21738 Pets_Cockatrice01 | 1729 Char_FlightlessBird_Cockatrice_Body_00.adr | Marketplace |
| Bunny Figurine | 577518 | 577519 | 577520 Bunny | 21735 Pets_Bunny_Bunny01 | 1730 Char_Bunny_Bunny_00_bunny.adr | Marketplace |
| Beetle Figurine | 577521 | 577522 | 577523 Beetle | 21734 Pets_Beetle_Klicnik01 | 1731 Char_Klicnik_Klicnik_000.adr | Marketplace |
| Jackalope Figurine | 577532 | 577533 | 577534 Jackalope | 21736 Pets_Bunny_Jackalope01 | 1734 Char_Bunny_Bunny_00_Jackalope.adr | Marketplace |
| Lop Ear Bunny Figurine | 577538 | 577539 | 577540 Lop Ear Bunny | 21737 Pets_Bunny_Lopear01 | 1735 Char_Bunny_Bunny_00_Lopear.adr | Outpost elites, rare (P6143) |

Shared rows (proven, new in 0.695):
- Text 577546 is a general figurine description: "This enchanted figurine can be activated to summon a cosmetic pet
  that will accompany you on your travels. Cosmetic pets cannot fight and are strictly for appearance purposes.
  Figurines have unlimited uses, but you may only have one active pet at a time." This states the rules: **not used
  up**, and **one active pet**.
- Item category 291 "Pet" (577509). It is already in the 0.695-era category dump (39-ReferenceDataItemCategory
  Definitions), but no item in the login list uses it.
- KeywordDefinitions 86 "Pet" (577509, HIDE_FROM_CLIENT 0).
- TagDefinitions 623 "Cosmetic Pets" (577544). The other new tag is 622 "Parlor". These are probably the
  Marketplace search tags (inference).
- ProfileDefinitions 379 was renamed "Adventuring Gear" (575475) → "Non-Combat Entity" (577511). Its text id sits
  between the pet texts, so it is probably the pets' NPC profile. Nothing in the spawn packet carries a profile id
  (inference; see §6).
- Models 1729-1731 and 1734-1735 are rows of a new kind: RACE_ID 12 (RaceDefinitions 12 = text 574258 "Monster"),
  SCALE 1, DESCRIPTION = the pet's name, GENDER 1, MATERIAL_TYPE 0, IS_VALID_FOR_PC 1, PATH_BLOCK_RADIUS 0. This is
  the same column pattern as the actor model 1724 "Ebonguard Female" (race 16). Models 1732 and 1733 have the same
  layout as the creature models the server already spawns (1462 Deer, 1463 Spirit Wolf: RACE 0, MATERIAL_TYPE 2,
  IS_VALID_FOR_PC 0, PATH_BLOCK_RADIUS 1). So the wolf pup and the fawn are smaller versions of existing creatures
  (proven by the file names). The forum also says the pets reused the critter rigs (57751-3.html #45, a player's comment).
- No pet collision file: CommonData/Collision in <0.695 test copy> has no .cdt for these models. The wording in
  CLIENT-DIFF.md:138 ("pets") is not supported by the file names. Pets probably have no collision (inference).

**Item ids are not known (L24).** Figurines are not in the live login list (gameData695/items_summary.tsv). The
unproven candidates come from gaps in the dump's id order: 4499-4507 (9 ids for 7 figurines, plus perhaps a second
Cockatrice item, given text 577547). Assign them provisionally in text order (4499 Shadow Wolf, 4500 Stag, 4501
Cockatrice, 4502 Bunny, 4503 Beetle, 4504 Jackalope, 4505 Lop Ear) and keep the mapping in one table so it can be
changed later.

**Closest template in the dump:** 4129 Repair Bot Beacon (category Gadget, slot 0, ability `(1173, type 4)`, text
576585 "Activate this beacon to temporarily summon a handy Repair Bot…"). It is the only "summon" item that existed
before pets (re/out/items_by_category.txt:487). The forum also says "the code for the repair bot" was reused for
the pets (57751-3.html #45; a player's guess).

## 3. Client behaviour (0.695, with addresses)

### 3.1 Using a figurine (proven, unchanged since 0.589)
- Inventory right-click: ItemContainerBase.as:303-316 (re/as3_695/Inventory) calls Lua `ClientInventory.ActivateItem
  (guid)` → FUN_1435dd400 (re/out/luabind_695.tsv) → FUN_143457210 → FUN_143457220.
- MODLOG.md:1074-1078 (traced 2026-10-11): an item whose definition has a type-4 ("use") ability is **cast**
  (AbilityCastAttempt, answered by Abilities.cs). An item without one sends **Inventory 0x2D sub 0x15 ActivateItem
  {u64 guid}** (coverage-695.md:976, same in 0.695; server: InventoryHandler.cs:103-106 → HandleActivate :153-168).
- Either path works on 0.695. Use the **ability path** for figurines, as Repair Bot Beacon and the fireworks pack
  4443 (ability 1237) do. That puts the use on the hotbar and the cast answer, like every other live "use" item.
  Live's ability id for pets is unknown, so the ids are our own (§5.1).
- A refused use on 0.695 can open the client's "Activation Failure" dialog: Inventory 0x2D sub 0x1E
  ShowActivationFailure {u32 text id} (FUN_142f202e0 case 0x1e; packets-695.md §10; TASKS 4.22).

### 3.2 Pet icon and dismissing (proven)
- Buff bar = BuffsWidget, which is unchanged in 0.695 (re/as3/BuffsWidget). Right-click on an icon →
  `Ui.CancelEffect(GUID)` (BuffsWidget.as:181-186) → **Abilities 0x1E RequestCancelEffect** with the effect id
  (reader FUN_142e05150 in 0.695; coverage-695.md:851; server MiscHandler.cs:62-65 / PlayerHandler.cs:30 →
  Buffs.Cancel, Buffs.cs:254-284).
- An icon is added by Abilities 0x1C EffectInitialized (0.695 reader FUN_142e02640, same as 0.589) after 0x0C
  EffectStart, and removed by 0x0D EffectEnd (0.695 handler FUN_142e18c70). The layouts are in Buffs.cs:101-200.
- BuffListItemRenderer.as:77-87: the countdown runs only when `secondsTotal > 0`. **Duration 0 = a permanent icon
  with no timer**, which is right for a pet. The tooltip is the name text only (`_tooltipText = name`), so the
  description is not shown.

### 3.3 The pet entity (proven parts)
- No pet class or packet is new in 0.695. `cPacketIdPetBase` (0x39, id table FUN_1434c7940, part_0107.c:8380) and
  the admin ListPets / PetSetUtility / PetTrick / GivePet ids (part_0107.c:9753-9925) are in both builds. No traced
  family handler uses them (trace/FAMILIES-SUMMARY.md). The pet-name generator FUN_142d88dd0
  ("pet_first"/"pet_second", SQLite) and the store's "OnStorePetPreview" (part_0092.c:7258) are in 0.589 too. All of
  these are Free Realms leftovers and are not needed.
- A pet can only be an NPC entity: PlayerUpdate 0x59 AddLightweightNpc with model 1729-1735
  (PlayerUpdateAddLightweightNpc.cs). ControlMode must be non-zero, or the client leaves the NPC where it spawned
  (reader +0x19c → entity +0x41a0; FUN_142df5160 / FUN_142e130e0, as documented in that file). The client plays
  walk / run / idle from the velocity in the movement body (Creatures.cs:1238-1262).
- Two client mechanisms could link an NPC to a player. Both are optional:
  - **0x28 UpdateOwner** (dispatcher FUN_142cdfa60 case 0x27): reads {u64 entity, …}, then FUN_142d50f90 (copies
    the owner into entity +0x1ed8) and FUN_142d518b0 (entity +0x1456 bit 0x4). Its effect on the client (nameplate,
    targeting) is not traced.
  - **0x2E SeekTarget / 0x2F SeekTargetUpdate** (cases 0x2d / 0x2e): build a
    `ProxiedCharacterSeekMovementController` (FUN_1435a3710, vtable 1435a38b0 / 1435a3900 update / 1435a3fc0
    retarget). It is stored at entity +0x1688 and steers the NPC toward a target entity **on the client**. Reader
    FUN_142c4f880: header, u64 entity +0x18, u64 target +0x20, 5 × u32 +0x28..+0x38 (speed, acceleration, max
    speed, turn …), then a list.
  - The per-frame update FUN_1435a3900 snaps the NPC onto the target once it is within a fixed distance
    (DAT_144692dc0). So it homes in **onto** the target, with no stand-off distance. That fits a homing projectile
    or a charging monster, not a pet walking at your heel (inference). Do not use it for the first version.
- Patch-note hint (P6145): "if the pet is not following you, try summoning it while NOT on a build site". So the
  live server's NPC movement / pathing had trouble on player-built voxels, which means following was server-driven
  (inference). The emulator's creatures have the same limit: they walk on TerrainHeights.HeightAt, the natural
  ground (Creatures.cs:516-541).

## 4. Packets / wire (all are existing layouts; nothing new to decode)

| Step | Direction | Packet | Existing writer / handler |
|---|---|---|---|
| Client learns the figurine | S→C | Command 0x4B login list, or 0x4A reply to 0x49, or ItemAdd 0x29/1 with an embedded definition | ItemDefinitionManager.cs:127-195; ClientUpdateItemAdd.cs:23 (needs the definition builder, TASKS 4.7 / 4.8) |
| Ability info for the hotbar | S→C | AbilityCasterInfoReply | Abilities.BuildItemAbilityInfo (Abilities.cs:46) |
| Right-click | C→S | AbilityCastAttempt with the figurine's ability (or 0x2D/0x15 ActivateItem) | Abilities.cs:207-218 → Consumables.TryUse; InventoryHandler.cs:153 |
| Cast answer | S→C | CastSuccess / CastFail | Abilities.Answer / Refuse |
| Pet icon | S→C | Abilities 0x0C EffectStart + 0x1C EffectInitialized {player, effect id, duration 0, name 577510…, image set 21734-21740, good type 2, stacks 1} | Buffs.ShowIcon (Buffs.cs:131-155); needs a public "show permanent icon" helper |
| Pet appears | S→C, to each viewer | PlayerUpdate 0x59 AddLightweightNpc {guid, transient id, NameId = pet name text, ModelId 1729-1735, position, rotation, ControlMode 2} (0.695: u32 sub-opcode, coverage-695.md:777) | PlayerUpdateAddLightweightNpc.cs |
| Pet moves | S→C, to each viewer | Position 0x7F, type-0 movement body (local frame, velocity, state 0x401 moving / 0x441 still) via QueueMovement | Creatures.MovementBody (Creatures.cs:1235), WorldSession.QueueMovement (WorldSession.cs:908) |
| Dismiss | C→S | Abilities 0x1E RequestCancelEffect {…, effect id} | MiscHandler.cs:62 → Buffs.Cancel |
| Pet removed | S→C | PlayerUpdate 0x01 RemovePlayer {guid} to each viewer; Abilities 0x0D EffectEnd to the owner | Creatures.RemoveFor pattern; Buffs.HideIcon |
| Refusal (optional) | S→C | 0x2D/0x1E ShowActivationFailure {text id} (0.695 only) | not built (TASKS 4.22) |

Optional later: 0x28 UpdateOwner {pet guid, owner guid, …} after the spawn. The layout past the guid (reader
FUN_142c4e1f0 → FUN_142c2cab0) is not traced yet.

## 5. Server design

### 5.1 Data (one table in code, e.g. `Game/Pets.cs`)
`record PetKind(uint Item, uint Ability, string Name, uint FigurineName, uint FigurineDesc, uint PetName, uint Icon,
uint Model)`, filled in from §2. Ability ids are our own: one per figurine, above the highest in the dump (1237),
e.g. 1300-1306. Mark them as emulator ids in a comment.

### 5.2 Definitions (prerequisite, TASKS 4.8 / 4.7)
Build each figurine record from the 4129 Repair Bot Beacon record:
- name / description from the table;
- image set = the icon;
- category list [291];
- slot 0, max stack 1, not consumed;
- one ability (id, type 4);
- tradeable: Lop Ear yes, Marketplace ones unknown (NO_TRADE is unproven; leave them tradeable).

Register the records in ItemDefinitionManager so that the following all work: `Get`, `FindAbility`
(ItemDefinitionManager.cs:260), tooltips, 0x49 replies, and the embedded definition in ItemAdd.

### 5.3 Summon / switch / dismiss
- Add the pet abilities to `Consumables.effects` (Consumables.cs:117-148), each returning **Use.Keep** (unlimited
  uses).
- `Pets.Summon(session, kind)`:
  1. If the same pet is already out, do nothing and answer success. A different pet is dismissed first (P6146).
  2. Show the icon with duration 0, keyed `"Pet"`, so there is one icon and a stable effect id: name = pet name
     text, image set = icon.
  3. Spawn the NPC 1.5 m behind and beside the owner on TerrainHeights, facing the owner's heading.
- `Buffs.Cancel`: add a hook. If the cancelled effect id is `EffectId("Pet")`, call `Pets.Dismiss(session)`, which
  removes the NPC for every viewer and hides the icon. Today Cancel only knows stat buffs and healing
  (Buffs.cs:262-279).
- Also dismiss on logout (PlayerManager.Leave, PlayerManager.cs:218-252, next to `Creatures.Forget`).
- Refusals: answer CastFail with a ShowActivationFailure / chat text when summoning makes no sense:
  - while dead;
  - in an instanced game? (live behaviour unknown; allow by default).

### 5.4 Follow (server-driven)
- Do not put pets in Creatures' region tables. Those are tied to a home region and stream by region
  (Creatures.ViewersOf, Creatures.cs:1284-1294), and a pet travels with its owner. Keep a `Dictionary<WorldSession,
  Pet>` instead, where each Pet holds {guid, transient id, kind, position, yaw, velocity, wasMoving, owner}.
- Guids: use their own base (e.g. 0x5100_0000_0000_0000 | counter) so they never collide with
  `Creatures.GuidBase`. Get transient ids from `PlayerManager.AllocateTransientId()`.
- Viewers: players on the owner's landscape within a view range. Use 120 m (Creatures.ViewRange) for showing, and
  drop at 160 m to save bandwidth (bandwidth rule), rather than zone-wide as players are shown
  (PlayerManager.RelayMovement). Stream once a second like Creatures.Update (Creatures.cs:299-323), sending spawns
  and removes as players come and go.
- AI step at 5 Hz (Creatures.AiHz):
  - Target point = 2 m behind the owner and 1 m to the side.
  - Over 3 m away: move at the owner's speed, capped at about 8 m/s (walk under 2 m/s).
  - Within 1.5 m: stop and send one zero-velocity update (the idle pose), like the `WasMoving` logic.
  - Over 40 m away, or after the owner teleports, warps or changes landscape: snap next to the owner (send
    RemovePlayer + AddLightweightNpc, or one movement update).
  - Height: use `TerrainHeights.HeightAt`. On a build site or when the owner is in the air (gliding, flying with
    the Bauble), use the owner's Y with a small lag so the pet does not sink into built floors. This is an
    improvement over live's known build-site problem (inference that this is acceptable).
- Reuse `Creatures.MovementBody` (public) and the step / face maths from `Creatures.Step` / `Face`. Copy them, or
  make small shared helpers, because `Step` is private and creature-specific (statuses, caverns).
- Movement goes only to viewers, and only while the pet moves (bandwidth rule).

### 5.5 Re-zone and landscape changes
- Every move re-zones (project note "cave teleport hang"): hold pet packets for a player until their re-zone
  finishes, as other world objects are re-sent after it.
- Hook `PlayerManager.Reintroduce` (PlayerManager.cs:160-172): re-send every pet near the arriving player,
  including their own. Hook `ChangedZone` (:186-198): remove the pet from the old landscape's viewers and spawn it
  next to the owner on the new one.
- After a zone load the client drops its effects and asks again (AbilityEffectUpdatesRequest → Buffs.Resend,
  Buffs.cs:54-74). Resend must include the pet icon, as Consumables.ResendGadgets does for the cave detector.
- Persistence over relog is unknown. Buffs are per session ("a relog clears them", Buffs.cs:13), so the pet goes
  on logout. Optionally store the active pet id in the character data and re-summon on login.

### 5.6 Sources
- Marketplace: add a "Pets" section to Store.cs's catalogue with the six Marketplace figurines (not Lop Ear). Icons
  come from re/tools/export_icons.py (21734-21740). Optionally use tag 623 "Cosmetic Pets" as the search tag.
- Lop Ear Bunny: in `Creatures.Kill` (Creatures.cs:804-833), when `c.Boss && c.Cavern >= 0` (an Outpost elite,
  Outposts.EliteDied :280), roll a rare chance and add the figurine to the killer's loot bag. The rate is unknown;
  2 % is a placeholder and should be a named constant.
- Admin / dev: a `pet <name>` dev command (DevCommands.cs) to summon without the item, for tests.

### 5.7 Tests (0.695 test copy <0.695 test copy> only)
- The figurine shows its icon, name and description in the bag. Right-click → the pet appears beside you and the
  icon (no timer) is on the buff bar. Prove it with the client's own state (rpc / effect list), not server logs
  alone.
- Walk, run, glide and teleport: the pet keeps up and plays its walk / run / idle poses.
- Summon a second pet: the first one goes.
- Right-click the icon: the pet and the icon go.
- Log out and back in. Re-zone through a cave.
- A second client sees the pet and its movement, and it disappears when out of range.

## 6. Unknowns / leads (add to OPEN-LEADS L24 / L27)

1. **Item ids** of the 7 figurines (L24). The candidates 4499-4507 are unproven. Also unknown: whether 577547 (a
   second "Cockatrice Figurine" name) is a second item.
2. **Live ability ids** and whether figurines used the cast path or ActivateItem. Unknown; our ids are invented.
3. **Buff icon contents**: which name text (figurine or pet) and image set live used, and whether 577546 was the
   buff description. The tooltip shows only the name, so this has little visible effect.
4. **Profile 379 "Non-Combat Entity"**: is it the pets' profile, and does any S→C packet carry it (e.g.
   LightweightToFullNpc 0x5C, not sent today)? Trace 0x5C's reader for a profile field if nameplates or
   targeting matter.
5. **0x28 UpdateOwner**: full layout (reader FUN_142c4e1f0 → FUN_142c2cab0) and what entity +0x1ed8 / +0x1456
   bit 4 change on the client (maybe the owner's name on the pet, or no targeting). Trace the readers of +0x1ed8.
6. **Pet speed, follow distance, behaviour while gliding / flying / swimming, teleport catch-up.** No source. Look
   for forum posts or videos after 28 Jul 2016, e.g. re/out/forum_raw threads from Aug 2016.
7. **Pet nameplate**: the client shows NPC names only under requirement 21 "NPC Combatant" (Creatures.cs comment
   near SendLife). Pets probably showed no name (inference from "Non-Combat Entity").
8. **Lop Ear drop rate** ("rare chance") and whether it dropped from every Outpost elite or only some.
9. **Tradeability / NO_TRADE** of the Marketplace figurines, and the Marketplace prices.
10. **Persistence** of a summoned pet over logout / server restart.
11. Model files 1729-1735 were not opened. That the .adr files and animations exist in the 0.695 packs is assumed
    from the Models rows and icons. Verify with a spawn test.
