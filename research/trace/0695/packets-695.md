# 0.695 packets: the new classes and the changed layouts

Traced 2026-10-15 from the 0.695 decompile (re/decomp_all_695) against the 0.589 one (re/decomp_all). Every address
below is a 0.695 function unless marked "0.589". Wire types: u8/u16/u32/u64 little-endian, `str` = u32 length + bytes
(client reader FUN_142d4b5d0 = 0.589 Stream_ReadString; writer FUN_142d4b510), `bool` = 1 byte, `vec4` = 4 × f32.
"Header" = the family's base opcode (u16) and sub-opcode; its width is given per family.

Channel = the tunnel channel byte of the send template (`(char)param_3 << 5 | 6`); every new C→S packet below is sent
with param_3 = 0 (emulator label "Zone"), matching trace/architecture/channels-695.tsv ("Zone:n" = n send sites).
S→C packets have no channel requirement (the client dispatches all channels but 2 the same way, channels.md).

Scratch / tools used (re/out/agent_packets): catalog_589.md / catalog_695.md (catalog2.py over both decompiles),
packetids_589/695.txt (the client's packet-id name tables FUN_143419ce0 / FUN_1434c7940), closure_check.py (0.589 →
0.695 reader comparison through functions.tsv with names / addresses / variables normalised), pktraw.py / pktnorm.py
(pkt.py with the 0.695 string reader), disx.py (capstone on the 0.695 exe, read-only).

## Summary

| Class | Dir | Opcode / sub | Channel | Emulator |
|---|---|---|---|---|
| PlayerUpdatePacketSetPlayerBio | C→S (S→C read and ignored) | 0x26 / 0x80 (u32 sub) | Zone (1 site) | stub: MiscHandler.PlayerSetPlayerBio reads nothing, answers a 10-byte 0x26/0x80 the 0.695 client discards; bio not stored |
| RequestInspectPlayerPacket | C→S | 0x91 / 3 | Zone (1) | not handled; the reply StartInspectPacket (0x91/1) is never sent |
| TemplatePacketPurchaseTemplateMaterialsDataRequest | C→S | 0xF0 / 0x1D | Zone (1) | not handled |
| TemplatePacketPurchaseTemplateMaterialsDataResponse | S→C | 0xF0 / 0x1E | – | not sent |
| TemplatePacketPurchaseMissingTemplateMaterials | C→S | 0xF0 / 0x1F | Zone (1) | not handled |
| PropPacketDialogResponse | C→S | 0xED / 0x1D | Zone (2) | not handled |
| InstancedGameMatchmakingRequest | C→S | 0x10D / 0x19 (u32 sub) | Zone (2) | not handled |
| InstancedGameMatchmakingUpdate | S→C | 0x10D / 0x1A | – | not sent |
| InstancedGameRequestJoinTeamPacket | C→S | 0x10D / 8 (Request type 9) | Zone (1) | Request handler has no type 9 |
| InstancedGameGetConfigurationRequestPacket | C→S | 0x10D / 0xD | Zone (1) | not handled (emulator pushes the config itself; its 0.589 response sub 0xD is now this request's number) |
| InstancedGameAddOrRemoveRuleRequestPacket | C→S | 0x10D / 0x18 | Zone (1) | not handled |
| ClaimsPacketSetAdventureFlags | C→S | 0xF9 / 0x45 | Zone (1) | not handled (0x45 was ClaimMoveItemToPlayerOnZone in 0.589) |
| ClaimsPacketStartClaimPlacement | C→S | 0xF9 / 0x46 | Zone (1) | not handled |
| ClaimsPacketPlayerEnteredClaim | C→S | 0xF9 / 0x63 | Zone (1) | not handled |
| CharacterCustomizationPacketEquippedAttachmentRequest | C→S | 0x11C / 3 | Zone (1) | logged as unhandled sub (Barbershop.cs) |
| CharacterCustomizationPacketEquippedAttachmentReply | S→C | 0x11C / 4 | – | not sent |
| InventoryPacketShowActivationFailure | S→C | 0x2D / 0x1E (i16 sub) | – | not sent |
| WorldEditingPlayEffectPacket | S→C | 0x10C / 8 (u32 sub) | – | not sent |
| PacketInGamePurchaseCommerceTemplateProductCreate | C→S | 0x45 / 0x30 (u16 sub) | Zone (send FUN_143235910(.., 0, 1)) | not handled; shifts every 0x45 sub ≥ 0x30 by one (see Changed layouts) |

No other `*Packet*` class is in re/out/rtti_new_695.txt. Related: ClaimsPacketClaimInfoUpdate gained kind 0x17
(adventure flags), PlayerUpdate 0x79 DisplayPropText gained the Story Node question fields (both under Changed
layouts), Mail sub 4 cMailPacketIdDeveloperMailPlayer is new in the id table but has no class (FUN_1434c7940).

**The biggest finding is not a new class:** in 0.695 the PlayerUpdate (0x26) sub-opcode is a u32 in both directions
(was u8), and the Claims (+2 from 0x45), Mail (+1 from 4), InGamePurchase (+1 from 0x30) and InstancedGame (+1 from
0xD) sub numbers shifted. A 0.695 client cannot read any PlayerUpdate the emulator sends today. Details at the end.

---

## 1. PlayerUpdatePacketSetPlayerBio — 0x26 / 0x80

- **Class / functions:** vtable 1446aa1f0 (1 slot, dtor 142c94390); ctor FUN_142fc8200 (no callers); sent by
  FUN_142fd4290 through send template FUN_142fc5360 (header writer FUN_142c13350, string FUN_142d4b510 at +0x18).
- **Wire (C→S):** u16 0x26, **u32** sub 0x80, str bio. Empty bio = 10 bytes (0.589 header was u16 + u8).
- **Trigger:** Lua `Ui.SetBio(text)` (FUN_1435c4740) → FUN_142fd35c0 copies the text (StringFixed<512>) into player
  +0x7aea8 and sets the dirty flag +0x7b0c8. The player update FUN_142fce9a0 calls FUN_142fd4290 when the flag is set
  and at least 0x5DD (1501) time units passed since the last send (+0x7b0d0), then clears the flag. FUN_142fd4290 fills
  the `Character.PlayerBio` data source (BioString, BioLengthMax 0x200, BioLengthCurrent), runs the bad-word filter
  (DAT_145431428 vtable +0x60, mode 7) and marks each bad word (StartIndex / EndIndex rows, BioIsValid, BadWords);
  **the packet is sent only when the bio is valid** (no bad words).
- **S→C 0x26/0x80:** the PlayerUpdate dispatcher FUN_142cdfa60 case 0x7F reads it with FUN_142c4d440 (u16, u32 sub,
  str) and does nothing with it. Own bio reaches the client in SendSelfToClient (trailing str, FUN_142c19bd0 → player
  +0x7aea8); other players' bios in the Inspect reply (§2).
- **Server:** store the bio per character (≤ 512 chars; the client already filtered it); send it back in
  SendSelfToClient (0.695) and in StartInspect. No reply packet is needed.
- **Emulator:** ZoneMessageOpcode.PlayerUpdateSetPlayerBio = 0x26008000, model PlayerUpdateSetPlayerBio reads nothing,
  MiscHandler answers 26 00 80 00 + 6 zero bytes (= u16 0x26, u32 0x80, str "" for 0.695: read and ignored).

## 2. RequestInspectPlayerPacket — 0x91 / 3, and its reply StartInspectPacket — 0x91 / 1

- **Class:** vtable 1447abd40. Built in FUN_1435c45b0 = Lua `Ui.InspectPlayer(guid)` (needs 2 Lua args; arg 1 parsed
  as a u64 by FUN_143c9f460), sent by FUN_1435d54e0 (header FUN_142c13010 = u16 + u8, u64 FUN_142c02460).
- **Wire (C→S):** u16 0x91, u8 3, u64 target character guid. Lua side: global.lua `OnInspectPlayer()` →
  `HudStateManager:OnOpenInspect()` (CLIENT-DIFF §4); the radial menu entry: RadialMenuButtons +2 (not traced yet).
- **Reply the client accepts:** StartInspectPacket, 0x91 sub 1, zone dispatcher FUN_142d1ccf0 case 0x3D: BaseInspectPacket reader FUN_142c4e670 (sub must be 1), body reader FUN_142c2f8c0 (same as 0.589
  FUN_142b95dd0): **u64 target guid, u32 n, n bytes blob**. The target must be a known actor (FUN_142f85390 on the
  actor list); the client builds an InspectProxy (FUN_14308a260, 0x448 bytes) and parses the blob with FUN_143089c10:

  | # | Field | Stored | Use |
  |---|---|---|---|
  | 1 | u32 n; n × {u32 key, u32 value} | +0x114 | sets the value of the proxy's profile entry with that key (array +0x370, copied from the actor); values > 1 add value-1 to +0x118 |
  | 2 | u32 | +0x120 | `LuminaryScore` |
  | 3 | u32 | +0x124 | `NumberOfClaims` |
  | 4 | u32 n; n × u64 claim guid | +0x358 | `NumberOfGalleryListings` (+0x128) = how many of them the client's gallery cache (DAT_1454311c8 +0x32050, FUN_143250b60) knows |
  | 5 | str bio | +0x130 | `BioString` |
  | 6 | u32 | +0x110 | not found yet (L28) |
  | 7 | u32 n; n × {i32 slot, item record FUN_142c208e0 (= 0.589 FUN_142b87110, the Mail.cs item record), item definition FUN_142c19850 (= 0.589 FUN_142b800a0, LZ4 ClientItemDefinition)} | list +0x390 | equipment rows: SlotName / SlotNumber / Name / ItemName / IconId / ItemDefinitionId / ItemGuid |
  | 8 | u32 | +0x11c | not found yet (L28) |

  Then FUN_14308b5c0 fills `Character.InspectData` (CharacterName from the actor +0x13b8, CharacterGuid, the counts,
  BioString, one row per equipped slot) and calls Lua `OnInspectPlayer`.
- **0.589 difference:** the 0.589 blob parser FUN_142fe9cb0 had no fields 2-5 and read each item entry inline (u32,
  u32, two FUN_142b87690 records, u32, ClientItemDefinition, FUN_142b87790 entries); the emulator never sent it.
- **Server:** answer RequestInspect with StartInspect for a player the requester can see, filling the blob from the
  target's character (score, claims, claim guids, bio, equipped items with definitions). StopInspect (0x91/2, C→S,
  0.589 FUN_142cc08f0) still closes it.

## 3. Buying a Design's missing materials — 0xF0 / 0x1D, 0x1E, 0x1F

Template family header: u16 0xF0 + u8 sub (writer FUN_1430fdeb0, reader in FUN_14310bbf0 `switch(sub - 2)`; 0.695
adds only case 0x1C = sub 0x1E to the 0.589 cases).

### 3a. TemplatePacketPurchaseTemplateMaterialsDataRequest — 0xF0 / 0x1D (C→S)
- vtable 14470e1d0, ctor FUN_143104dc0, built in FUN_143113530 = Lua
  `Templates.RequestPurchaseTemplateMaterialsData(guid)` (FUN_1435dc520), writer FUN_1431027f0.
- Sent only when the design is in the client's design-data cache (hash at Templates manager +0x30/+0x50, key = entry
  +0x190); the cached 0x180-byte record is copied (FUN_143106990, source = the found entry: disassembly 1431135fc).
- **Wire:** u16 0xF0, u8 0x1D, u64 design guid, TemplateBasicData (0.695 form, writer FUN_1430fe830, see Changed
  layouts §B4), cell list (FUN_143100cd0 = 0.589 FUN_1430b1d30: u32 n × {u64 key, u64 cell, u32 type, u32 len + blob,
  ...}), PropData list (FUN_143100870 = 0.589 FUN_1430b1ad0: u32 n × PropData FUN_1430afea0 layout). I.e. the whole
  design as the client holds it.
- Before sending, FUN_142d62ee0 is called on the `Character.Templates.PointOfSale` data source (the call every filler
  makes before adding rows).

### 3b. TemplatePacketPurchaseTemplateMaterialsDataResponse — 0xF0 / 0x1E (S→C)
- vtable 14470e230; reader FUN_143100050; handler FUN_14310c290.
- **Wire:** u16 0xF0, u8 0x1E, u64 TemplateGUID, u32 n (≤ 100: Array<RecipeCraft,100>) × RecipeCraft {u32 RecipeId,
  u32 RecipeCraftCount, u32 LumensNeededToCraft, u32 ItemDefinitionId, u32 ItemCountToBeCrafted, u32 ItemCountNeeded,
  u32 Icon, u32 Name (text id, resolved by DAT_145431178 vtable +0x10)}, u32 TotalLumens.
- **Client:** fills `Character.Templates.PointOfSale`: row 0 {FoundLumenRecipeForAllItems = packet +0x20,
  TotalLumens, TemplateGUID}, one row per RecipeCraft (the 8 names above). **+0x20 is never read from the wire**
  (reader skips it; the dispatcher initialises it to 0), so FoundLumenRecipeForAllItems is always false (L29).
  The confirmation UI is TemplateConfirmationWidget.swf (HUD widget 219, not exported yet).

### 3c. TemplatePacketPurchaseMissingTemplateMaterials — 0xF0 / 0x1F (C→S)
- vtable 14470e290, built in FUN_1431118e0 = Lua `Templates.PurchaseMissingTemplateMaterials(guid)` (FUN_1435dc570),
  writer FUN_1431025f0 (RecipeCraft list writer FUN_143100af0 → FUN_1430fe6d0).
- Sent only when PointOfSale row 0 TemplateGUID equals the guid; it re-reads the rows: every row with RecipeId > 0 and
  RecipeCraftCount > 0.
- **Wire:** u16 0xF0, u8 0x1F, u64 TemplateGUID, u32 n × {u32 RecipeId, u32 RecipeCraftCount}, u32 TotalLumens (as
  the server sent it).
- **Server:** keep nothing between the two (the client echoes the recipes); on purchase check the lumens, craft
  RecipeCraftCount × each recipe into the player's inventory, take TotalLumens. **No reply class exists** (Template
  dispatcher has no sub > 0x1E); the result can only show through the normal inventory / currency packets (L29).

## 4. PropPacketDialogResponse — 0xED / 0x1D (C→S)

- vtable 1447b7f28; id table: `cPropPacketIdNpcDialogResponse` 0x1d00ed00 (FUN_1434c7940). Built in FUN_1435fbf80 =
  Lua `Prop.OnConfirmNpcDialog(a, b)` (bool 1) and FUN_1435fc030 = `Prop.OnDenyNpcDialog(a, b)` (bool 0); writer
  FUN_1435fc490 (Prop header FUN_142fa7450 = u16 + u8).
- **Wire:** u16 0xED, u8 0x1D, u64 (Lua arg 1, the id the bubble was opened with), bool accepted.
- Lua arg 2 is the speaker actor: its chat bubble is closed first (FUN_142ebd0f0: removes it from
  `Character.ActiveChatBubbles`).
- **What opens the question:** PlayerUpdate 0x79 DisplayPropText in its 0.695 form (Changed layouts §B2): the handler
  (dispatcher case 0x78) calls FUN_142f859b0 → FUN_142ebfcb0 which stores a duration (+0x1278), the text, a
  "question" bool (+0x127c) and a u64 (+0x1280) on the speaking actor; a question bubble additionally calls
  FUN_1434445a0 (indicator at the bubble). How the SpeechBubbleWidget gets Lua arg 1 is not found yet (L30).
- **Server:** on 0xED/0x1D fire the Story Node's "When a Luminary accepts / denies" events (text-new.tsv) for the node
  that asked.

## 5. Game Finder and the Game Table rework (InstancedGame 0x10D)

InstancedGame header: u16 0x10D + **u32** sub in both builds (writer FUN_1434d5c40 = 0.589 FUN_143426610; S→C
dispatcher FUN_1434ee920 `switch(sub - 2)`). The full 0.695 sub table is under Changed layouts §A5.

### 5a. InstancedGameMatchmakingRequest — 0x10D / 0x19 (C→S)
- vtable 144790770. Start: FUN_1434f2da0 (Lua `InstancedGame.StartMatchmaking`, FUN_143604e40); refused locally with
  "You cannot queue for a game while playing another one!" when manager +0x38 (current game) is set. Stop:
  FUN_1434f2f90 (`StopMatchmaking`). Writer FUN_1434da790 → FUN_1434d5fd0.
- **Wire:** u16 0x10D, u32 0x19, u64 Game Finder flag mask (manager +0xe0; start only — stop leaves this field
  unset, stack garbage), u64 player guid (BaseClient +0x32508), u8 searching (1 start / 0 stop), u8 (1 start / 0 stop).
  Client sets manager +0x402 (IsMatchmaking) to 1 / 0.
- Flag mask: `InstancedGame.SetGameFinderFlag(index, on)` (FUN_1434f23c0) sets bit 1 << index (off = XOR). Rows of
  `GameFinder.GameFlags` (FUN_1434ed2e0): index 1 "FFA Games" (bit 2), 2 "Team Games" (4), 3 "PvE" (8), 4 "PvP"
  (0x10), 6 "In Progress Games" (0x40) (GameFinder.lua `GameFinderFlagCheckbox` → DataIndex = ValueIndent).

### 5b. InstancedGameMatchmakingUpdate — 0x10D / 0x1A (S→C)
- vtable 144790780; reader FUN_1434d7270; handler FUN_1434e9a50.
- **Wire:** u16, u32 0x1A, u64 (not read by the handler; L31), u32 Players, u32 ActiveGames, u32 QueuedGames,
  u32 InactiveGames, u32 SearchState, u64 match flags.
- **Client:** stores the four counts (manager +0x420..+0x42c), fills `GameFinder.MatchmakingInfo` (+ SearchState,
  StateString), calls Lua `UpdateGameFinder`. SearchState 0 "Searching..." + chat "You've been entered into the
  search queue..."; 1 "Match found!" (clears +0x402, calls FUN_1431f7e90 with a value looked up under key 0x565a5445, chat "You've been queued for a match! Match
  details:" with FFA / Teams / PvE / PvP Yes/No from the flags bits 2/4/8/0x10); 3 "You've been removed from the
  matchmaking queue."; other values "Search inactive.". GameFinder.lua shows the info page for "1", the options page
  for "3".
- **Server:** a matchmaking queue across sites (and worlds, patch 14 Oct 2016); send this on start / stop / changes.
  How a found match moves the player (existing Request type 6 membership?) is not shown by this handler (L31).

### 5c. InstancedGameRequestJoinTeamPacket — 0x10D / 8, Request type 9 (C→S)
- vtable 1447906a8 (ctor FUN_1434dd530 sets type 9). Built in FUN_1434f1210 (from Lua
  `InstancedGame.SwitchTeam`, FUN_143604510) only while in a game (manager +0x38) and manager +0x80 == 0.
- **Wire (RequestPacket writer FUN_1434d6470, same as 0.589 FUN_143426bb0):** u16, u32 8, i16 type 9, i16 0, i16 team
  (the argument), i16 0, i16 0, u64 game (manager +0x38), u32 0, str "" (FUN_1434d6710).
- **Server:** move the player to that team (Allow team switching setting). The S→C Request handler FUN_1434e93e0 still
  knows only types 4 / 5 / 6, so there is no specific answer type; the team view is TeamMembers (now sub 0x15) (L31).

### 5d. InstancedGameGetConfigurationRequestPacket — 0x10D / 0xD (C→S)
- vtable 144790600; built in FUN_1434eca50, writer FUN_1434da5e0. Only caller: entering a claim, FUN_142dc8ec0, when
  the claim entry's game (+0x140) is set and the manager exists (disassembly 142dc9340: rdx = entry + 0x140).
- **Wire:** u16 0x10D, u32 0xD, u64 site game guid.
- **Reply:** GetConfigurationResponse, now sub **0xE** (0.589: 0xD), new layout (Changed layouts §B7).

### 5e. InstancedGameAddOrRemoveRuleRequestPacket — 0x10D / 0x18 (C→S)
- vtable 144790668; built in FUN_1434f4d60 for category 3 = Lua `RulesetObject.UpdateValueBool(category, key,
  value, index)` (FUN_143608450); writer FUN_1434da290 → FUN_1434d5e90.
- **Wire:** u16, u32 0x18, u64 game being edited (manager +0x30, set by the PropRecipe packet handler FUN_1434e9210),
  u32 rule key (Lua arg 2), u8 add (value ≠ 0), u64 looked up in the manager's table at +0x3d8 by the key
  (DAT_14546b180 when missing).
- **Server:** add or remove that rule on the site's game, then (by the client's model) resend the configuration. What
  the u64 is: not found yet (L32).

## 6. Play Mode Rules — ClaimsPacketSetAdventureFlags — 0xF9 / 0x45 (C→S)

- vtable 1446c16d0, id name `cClaimsPacketIdUpdateAdventureFlags`. Built in FUN_142dd08e0 from Lua
  `Claims.SetClaimAdventureFlag(claim, flag, on)` (FUN_1435e6470; claim -1 = the current claim +0x348). Writer
  FUN_142da6bd0 (Claims header FUN_142d9e650 = u16 + u8).
- **Wire:** u16 0xF9, u8 0x45, u64 claim guid, u64 new flag mask (claim entry +0x380 with the bit set / cleared),
  u8 1.
- **Flags** (data source `Claim.AdventureFlags`, FUN_142dd2480): 1 Block Grappling Hook, 2 Block Boot Abilities,
  4 Block Voxel Destruction, 8 Allow Free Camera, 0x10 Allow Flying Character Camera, 0x20 Allow Orbit Camera.
- **Client state:** claim entry +0x380 (u64), filled from the claim record's new trailing u64 (record +0x3e0 →
  FUN_142db5ca0 `pfVar13 + 0xe0`) and from ClaimInfoUpdate kind 0x17 (Changed layouts §B5). FUN_142e93540 refuses the
  grappling hook (text key Grapple.AdventureBlocked) when the current claim is in Play Mode (entry +0x378 == 2) and
  bit 1 is set.
- **Server:** store per claim; reply / broadcast ClaimInfoUpdate kind 0x17 with the new mask; include it in every
  claim record. Defaults from the patch notes (voxel destruction on, flying camera off) are server data.

## 7. ClaimsPacketStartClaimPlacement — 0xF9 / 0x46 (C→S)

- vtable 1446c1438; FUN_142dd22f0 from Lua `Claims.StartClaimPlacement()` (FUN_1435e2fa0); writer FUN_142da7cd0.
- **Wire:** u16 0xF9, u8 0x46 — no body.
- **Server:** start placement as the flag did: Command 0x4D InteractionClaim {u32 result} (handler 0.695 FUN_1433be380,
  identical to 0.589 FUN_143311590: 2 = enter placement, other = claim error). Per the 0.695 flag text ("This item is
  no longer required to place Build Sites") the "no flag" refusal must not apply for 0.695.

## 8. ClaimsPacketPlayerEnteredClaim — 0xF9 / 0x63 (C→S)

- vtable 1446c1748; sent by FUN_142dc8ec0 (entering a claim: sets current claim +0x348, time / music, Lua
  `GameEvents:OnEnterClaim`, build-mode camera / time / icon HUD for the owner) through FUN_142da8230.
- **Wire:** u16 0xF9, u8 0x63, u64 claim guid. Immediately after it the client sends GetConfigurationRequest when the
  claim has a game (§5d).
- **Server:** no reply is read by the client. What the live server did with it: not found yet (L33).

## 9. Crimson Parlor attachments — 0x11C / 3 and 4

Header u16 0x11C + u8 (writer FUN_1430a0430; S→C entry FUN_1430a29d0 handles sub 2 and sub 4).
- **EquippedAttachmentRequest (C→S, sub 3):** vtable 144703730; FUN_1430a5ae0 from Lua
  `CharacterCustomization.StartCustomization(guid)` (FUN_143607d10); writer FUN_1430a08d0. Wire: u16, u8 3, u64
  character guid (-1 → own player). The client then switches to the customization controller (0x23) and copies the
  actor's appearance.
- **EquippedAttachmentReply (S→C, sub 4):** vtable 144703740; reader FUN_1430a0cc0: u16, u8 4, u32 n ×
  CharacterAttachmentData (FUN_142c185c0 = 0.589 FUN_142b7ee10, the FullPcData attachment record: str, str, str, str,
  u32, u32, u32, u32, bool). Stored by FUN_1430a5c60 into the customization manager +0x1360. What the UI does with it:
  not found yet (L34).
- **Server:** answer sub 3 with the target's equipped attachments (same records as in LightweightToFullPc).

## 10. InventoryPacketShowActivationFailure — 0x2D / 0x1E (S→C)

- vtable 1446e10f0. Inventory dispatcher FUN_142f202e0 (0.589 FUN_142e81590 had no case 0x1E): header u16 0x2D,
  **i16** sub; body u32 text id.
- **Client:** CreateConfirmationDialog("Activation Failure", <text of the id>).
- **Server:** send when an item use is refused (which text ids live used: L35).

## 11. WorldEditingPlayEffectPacket — 0x10C / 8 (S→C)

- vtable 144700a90. WorldEditing dispatcher FUN_1430838d0 (0.589 FUN_142fe3ae0), header u16 0x10C, u32 sub, u32
  (the second header u32 every 0x10C packet has; WorldEditing.cs HeaderLength 10). The whole dispatcher returns early
  unless DAT_145431328 +0x153b is set.
- **Wire:** header (sub 8), u32 effect id, vec4 min, vec4 max (FUN_14307c4d0 → FUN_142c38220).
- **Client:** plays the effect (FUN_143d57870 on DAT_145431060) at the box centre ((min + max) / 2).
- **Server:** send when world edits need an effect (Linking Area destroy / heal, patch 28 Jul 2016) — which effect ids:
  L36.

## 12. PacketInGamePurchaseCommerceTemplateProductCreate — 0x45 / 0x30 (C→S)

- vtable 14472e5a0; ctor FUN_14323da10 (sub 0x30). Built in FUN_143258720 when IsTemplateValidResponse (now 0x45 /
  0x4E, reader FUN_14322dc40) says valid for a held product (failure texts "Subtemplates are modified and listed on
  the marketplace!" / "Couldn't find template data!"); also FUN_143262c00. Writer FUN_143235910 (commerce header
  FUN_14322b8a0 = u16 + **u16** sub).
- **Wire:** u16 0x45, u16 0x30, ProductCreate body (FUN_14322b430 = 0.589 FUN_1431809d0, the body the emulator already
  parses for 0x2F), TemplateBasicData (0.695 form), cell list, PropData list (same three as §3a).
- **0.589 difference:** 0.589 sent the held ProductCreate (0x2F) at that point; 0.695 sends the design data with it, so
  the server gets the design without a separate lookup.

---

## Changed layouts of existing packets

Method: every S→C class in trace/families/*.md: its reader / case functions and their callees (depth 2) mapped through
functions.tsv and compared with addresses, names and variables normalised (closure_check.py; 908 classes: 499 same,
102 different, 307 without a reader in the traces); every C→S send template's header writer (hdrw.py); the packet-id
name tables (packetids_*.txt); the family dispatchers' case lists. Struct-offset-only differences are not listed.

### A. Header and sub-opcode changes (affect every packet of the family)

1. **PlayerUpdate 0x26: sub is u32 (was u8), both directions.** S→C dispatcher FUN_142cdfa60 reads `*(int *)` after
   the u16 (0.589 PlayerUpdate_Dispatch 142c45fa0: `(char)`); every body reader starts with u16 + u32 (e.g.
   RemovePlayer FUN_142c2a960 vs 0.589 RemovePlayer_Read). C→S header writer FUN_142c13350 writes 4 bytes (0.589
   FUN_142b79bb0: 1) and is used by all 13 PlayerUpdate send templates (StripEffect 0x18, Bio 0x80,
   FullCharacterDataRequest 0x5E, SafeEject 0x4C, InteractionInfoRequest, PreferredLanguages 0x1A, UpdateSteamInfo
   0x68, SetEnabled 0x73, RemoveTemporaryAppearance, EmoteStringById 0x77, ReadyToReviveResponse 0x3D,
   ClientLoading 0x6C). For 0.695 the emulator must write `26 00 ss 00 00 00` before every PlayerUpdate body (all the
   *Wire opcodes and the PlayerUpdate models), and C→S bodies start 3 bytes later (WorldSession.cs 1129 `data[2] ==
   0x6C` still matches). Every other family's header writer is unchanged ("same" in functions.tsv).
2. **Claims 0xF9: subs 0x45..0x60 moved up by 2** (0x45 / 0x46 inserted; id tables: e.g. UpdatePropLimits 0x5F→0x61,
   PlayerDeleted 0x60→0x62, DebugAddItemClaim 0x5E→0x60, NameToGuid 0x54→0x56, ...; 0x63 new). S→C dispatcher
   FUN_142dbecb0: only its last case moved (0x5E→0x60 = sub 0x61 UpdatePropCategoryCount). Emulator: PropLimits.cs
   sends sub 0x5F → 0x61 for 0.695.
3. **Mail 0xF8: subs 4..0x16 moved up by 1** (sub 4 cMailPacketIdDeveloperMailPlayer inserted). Emulator Mail.cs:
   every constant except ComposeRequest 1 (InboxList 6→7 / 7→8, Read 8→9 / 9→0xA, Delete 0xA→0xB / 0xB→0xC,
   NewMail 0xE→0xF, ConsumeAttachment 0xF→0x10 / 0x10→0x11, EnableWindow 0x13→0x14, AttachItem 0x14→0x15 /
   0x15→0x16, ComposeResponse 4→5).
4. **InGamePurchase 0x45: subs ≥ 0x30 moved up by 1** (0x30 TemplateProductCreate inserted). S→C dispatcher
   FUN_143258f50 cases 0x31..0x4E vs 0.589 FUN_1431adf70 0x30..0x4D, catalog hints for the C→S ones. Emulator
   Commerce.cs (ProductCreateResponse 0x30→0x31, ProductRemove 0x31→0x32, ProductUpdate 0x33→0x34,
   RequestAccountStore 0x36→0x37, AccountStore 0x38→0x39, Search 0x3D→0x3E / 0x3E→0x3F, GetProductForReview
   0x3F→0x40, ReviewProductResponse 0x40→0x41, SubmitProductReview 0x42→0x43 / 0x43→0x44, IsTemplateValid
   0x4C→0x4D / 0x4D→0x4E), SiteScreenshots.cs (ModifyScreenshots 0x49→0x4A), Claims.cs Point-of-Need 0x46→0x47.
   0x2F ProductCreate and everything below 0x30 are unchanged.
5. **InstancedGame 0x10D: subs ≥ 0xD moved up by 1** (0xD GetConfigurationRequest inserted; 0x18, 0x19, 0x1A new).
   0.695 table — S→C: 2 AdminMonitor, 3 Scoreboard, 4 PropRecipe, 6 State, 7 Event, 8 Request, 9 HUD,
   **0xE GetConfigurationResponse** (0.589 0xD), **0x12 PropState** (0x11), **0x13 PropInteraction** (0x12),
   **0x14 PlayerRemoval** (0x13), **0x15 TeamMembers** (0x14), **0x16 ObserverRegistration** (0x15),
   **0x17 UpdatePlayerFlags** (0x16), 0x1A MatchmakingUpdate. C→S: 5 PropRecipeRequest, 8 Request, 0xA PropRequest,
   0xB PropRemove, 0xD GetConfigurationRequest, **0xF SetConfiguration** (0.589 0xE), **0x10 StartRequest** (0.589
   0xF, emulator "EnableGame"), **0x11 EndRequest** (0.589 0x10, "DisableGame"), 0x16 ObserverRegistration, 0x17
   UpdatePlayerFlags, 0x18 AddOrRemoveRule, 0x19 MatchmakingRequest (ctors part_0107 26071 / 26309, send sites
   part_0107 40917 / 41291, 0.589 part_0107 3249 / 3620 / part_0106 30650 / 30871). Note: a 0.695 SetConfiguration
   (0xF) would hit the emulator's EnableGame. Emulator: InstancedGames.cs / InstancedGames.Editor.cs constants.

### B. Body changes

1. **SendSelfToClient (0xF)** — known: FUN_142c19bd0 = 0.589 SendSelfToClient_Read + one trailing str (bio, player
   +0x7aea8) after the admin flag (now stored at +0x7b0d8). No other nested reader differs (closure depth 3).
2. **PlayerUpdate 0x79 DisplayPropText** (emulator sends it: Props.cs 76-124). 0.589 FUN_142b8e6c0: u64 prop, str
   text, u32, u32. **0.695 FUN_142c27f40** (via FUN_142c4be40): u64 (+0x18), **u64 (+0x20, new)**, str text (+0x28),
   u32 (+0x40), **u32 (+0x48, new)**, **bool (+0x4c, new)**. The handler passes text, +0x20, +0x48 (duration),
   +0x4c (question → response buttons, §4) and +0x18 (stored at actor +0x1280) to FUN_142f859b0. Which 0.589 u32
   became which: L30.
3. **PlayerUpdate 0x1B CustomizationChange** (emulator does not send it): 0.589 u64, customization data
   (FUN_142b7ef60), bool. 0.695 FUN_142c27ad0: u64, bool, customization data (FUN_142c18710), u32 n ×
   CharacterAttachmentData (FUN_142c3cd10 → FUN_142c185c0, new).
4. **TemplateBasicData lost its PropData list.** 0.589 FUN_143058180: u64, str, 4 × u64, u32, u32, 3 × u32, material
   list, **PropData list (FUN_142f0a340)**, contributor list, sub-template list, bool, str, bool, u32, u32, u32, bool,
   str, u32, bool, u32. 0.695 FUN_1430ff530: the same without the PropData list (nested readers "same" otherwise).
   Affects every Template packet carrying it (emulator Templates.WriteBasicData: subs 2 EnvironmentList, 3
   SaveTemplateResponse, 5 VoxelDataResponse, 0xC UpdateTemplateResponse; client-side also 0x15 InstanceDetails) and the commerce copies.
   **VoxelDataResponse (0xF0 / 5)** 0.695 FUN_1430fff20: u64, TemplateBasicData, cell list (FUN_143101860),
   **PropData list (FUN_142fa9070, moved here)**, image list (FUN_1431017a0), bool (0.589 FUN_143058b60: u64,
   TemplateBasicData with props, cell list, image list, bool).
5. **Claim record and ClaimInfoUpdate.**
   - Site-game struct (0.589 FUN_142cffe50 → FUN_142d9fc20): one **trailing u32** added. Field meaning by the walk-on
     window FUN_142dc9660 (0.589 FUN_142d2b3f0): 0.695 order u64 game, u32 state, bool can join, u32 queue size,
     u32 min players, u32 current players, u32 difficulty, u32 max players, bool, u32 name text id, str name, tags
     list, **u32 new (entry +0x15c; CanSwitchTeam needs it ≥ 2)** — the wire order of the old fields is unchanged
     (the struct moved current players / difficulty from +0x1c/+0x20 to +0x20/+0x24). Meaning of the new u32: L37.
   - Claim record (0.589 FUN_142d01c40 → FUN_142da1a50): the site-game change plus a **trailing u64 adventure flags**
     (record +0x3e0 → claim entry +0x380). Readers using the record: Claims S→C subs 3 (FUN_142d08ef0), 4 AOIUpdate
     (FUN_142d00720 → FUN_142d04200), 7 PlaceClaimComplete (FUN_142d08a50), 9 EditClaimItemData (FUN_142d00c30).
     Emulator Claims.WriteRecord.
   - ClaimInfoUpdate sub 5 (0.589 FUN_142d029f0 → FUN_142da2840): site-game change plus a **trailing u64** (+0x288);
     the dispatcher's new kind **0x17** copies it to claim entry +0x380 (adventure flags). Emulator BuildInfoUpdate.
6. **InstancedGame bodies** (emulator sends all of these):
   - PropRecipe (sub 4) item (0.589 FUN_1434282f0 → FUN_1434d7f90): **trailing u32** (+0x44), which PropRequest
     now echoes (C→S PropRequest 0xA, writer FUN_1434d62d0: 0.589 u64 game + 4 × u32, 0.695 **+ trailing u32**;
     builder FUN_1434f0f00 matches the item by rule / team / role / the new field).
   - HUD (sub 9) entry (0.589 FUN_1434284b0 → FUN_1434d8180): **trailing bool + u64**.
   - PropState (sub 0x12) entry (0.589 FUN_143427b50 → FUN_1434d77c0): **trailing u64**.
   - Event (sub 7) (0.589 FUN_143427430 → FUN_1434d6eb0): 0.589 u32, u32, u32, u64, u64, u32; 0.695 u32, u32, u32,
     u64, u64, **str, str, u32, u32** (L32).
   - Scoreboard, State, TeamMembers, ObserverRegistration, UpdatePlayerFlags, PlayerRemoval, PropInteraction: same
     bodies (only the sub numbers of §A5).
7. **GetConfigurationResponse (0x10D / 0xE)** (0.589 FUN_14342b760 → FUN_1434dbb20, handler FUN_1434e98d0):
   0.695 wire: u16, u32 0xE, then twice {LocalizedString (u32 text id, str), LocalizedString, settings map {u32 n;
   **u32 key** (0.589: str); u32 id, str SettingKey, str SettingDescription, str SettingValue, u32, **u32 (new)**},
   **string list {u32 n; str} (new)**}, then the rule map {u32 n; u64 trigger; parameter list {u32 n; u32 type, str
   name, u32 name text, str, str min, str max}, value list {u32 n; str value, u32 rule type, u32 index}, **u32, u64
   (new)**} — the 0.589 separate definitions and values maps are merged per trigger. Readers FUN_1434d9330 /
   FUN_1434d6b10, FUN_142c3b970, FUN_1434d9600 / FUN_1434d8660 / FUN_1434d9ee0 / FUN_1434d8700 / FUN_1434d9e10 /
   FUN_1434d85d0. UI: FUN_1434f39a0 (`RulesetObjects.GameSettings.*`: "General Settings", "Game Type Settings",
   "Game Name"). **SetConfigurationRequest (C→S 0xF)** has the same shapes (writer FUN_1434d6620: u64 game, 2 ×
   LocalizedString, settings map FUN_1434d8bd0 → FUN_1434d5d60, string list FUN_1430a6f80, rule map FUN_1434d8cd0 →
   FUN_1434d6870 → FUN_1434d9240 / FUN_1434d9160, u8). Unnamed fields: L32.
8. **Chat 0x13 / 2 DebugChat** (0.589 FUN_1432a9800 → FUN_1433566e0): trailing bool added; not sent by the
   emulator.
9. **Checked and unchanged** (normalised readers identical): all other sent classes with readers in the traces —
   ClientUpdate (ItemAdd / ItemDelete / Hitpoints / UpdateStat / CoinCount / RespawnLocations / ...), Abilities,
   TerrainCell, Commerce bodies, Rating, Friends, Achievements (handler-only diffs), Chest, LootPinata, Trade,
   ObjectScripting (handler-only diffs; AddBindingResponse default field 4 → 5 is an initial value, not a sub),
   VoxelConstruction, PropBase, KnownProp, PlayerTitle, WarpNetwork, LightweightToFullPc (FullPcData_Read),
   Movement_ReadBody, every PlayerUpdate body except 0x79 and 0x1B (header only). Not compared yet (no reader in
   the traces or catalogues; inline readers): L38.
