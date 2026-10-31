# Phase E3, wire-a: login / zone-in writes changed since the 2026-10-17 wire audit (2026-10-26)

Slice: everything the server writes at login and zone-in whose code changed since the last full wire audit (base
`71bafff` = `git rev-list -1 --before=2026-10-17T18:00 client-0.695`; HEAD `8219f7c`, 443 commits). Files:
SendSelfToClient.cs (+3148/-1668 lines since base), ClientBeginZoning.cs, SendZoneDetails.cs, ClientGameSettings.cs,
Shared/ContinentDefinition.cs, ClientPcDataMessages.cs, Shared/Achievement.cs, Shared/Objective.cs,
Shared/InventoryItem.cs, Shared/RewardBundle.cs, Shared/Mount.cs, plus what they call (CharacterModelInfo,
GamePacketWriter, LandmarkText, the builders WorldSession.BuildSendToSelfPacket, Teleport.RezoneNow,
Landscapes.ZoneDetails, Titles.SelfData, InventoryFilters.SelfData, Inventory.ToPacket).

Read-only. No code, docs or leads changed.

## How the SendSelf body was checked (method, so the coordinator can judge the coverage)

1. Top level: the 0.695 reader FUN_142c19bd0 flattened (bounds-check idiom folded into one READ per field; scratch
   `ss_flat.txt`) and compared line by line with `SendSelfToClient.Write` / `WriteTail` (SendSelfToClient.cs:2929-3168).
   Every field, width and list is in the same order, ending with the bio and the exact-consumption check
   (`if (error || consumed < length) FUN_142b4e1b0()`, ss_flat.txt:471) [P].
2. The live 0.695-era capture (Resources/PacketDumps/SendSelf.bin, 2017-02-20, 655,356 bytes) was parsed with the
   compiled HEAD model (LandmarkEmulator.Tests/bin/Debug/net10.0, built 2026-10-26 08:47; `dotnet fsi` script in my
   scratch folder): `Read` uses the body to the last byte (0 remaining). Because a wrong record layout cannot land at
   exactly zero over 655 KB, every record that is **non-empty in the capture** is proven to be the 0.695 client's layout
   [P by the capture]: 1 profile, 1 class progression, 24 items (stats in both ItemRandom blocks, a Player Design block,
   containers 0/2/5/8), **865 achievements** (142 type-1 reward entries, objectives), 2,363 recipe records, 1 recipe
   cooldown, 4 hidden recipes, 12 known props, 10 hotbars, 15 first-time events, 47 stats, 1 experience record,
   1 loadout, the fixed blocks (grief, rate tiers, acquire/item timer flags, skill points, vehicle tail, filters'
   NextFilterId, bio).
3. Records that are **empty in the capture but filled by the server**, checked against their readers one by one:
   inventory filters (FUN_142c3d470 → FUN_142c37990), titles (FUN_142c1b230), the u32 sets that get ~1,364 known recipe
   ids and the Crimson Parlor ids (FUN_142c34a10), overflow / vault items (container index, TimedDeleteSeconds).
4. Records empty in both (sent empty) were still compared with their readers, since they are in the assigned files:
   collection + entries, buff/effect, palette, acquaintance, mount, quest/objective, currencies, cooldowns.

## 1. Findings (ranked by player impact)

### F1. Item "extra" byte is read only for player-owned items in containers 0-11 — OK-NOTE (latent MISMATCH)

- [P] The SendSelf inventory reader FUN_142c47af0 reads each record (FUN_142c208e0) and hands it to FUN_142c98950
  with a lambda (FUN_142d609d0 → item vtable +0x38 = FUN_142e46670, one bool into ClientItem +0x161). FUN_142c98950
  picks the container `index = (record +0xfc OwnerType == 1) ? record +0xe8 ContainerId : 0xe` and **only if
  index < 0xc** calls FUN_142c987c0, which calls the lambda (reads the byte) whenever the item object was created —
  even when the add itself fails. For OwnerType ≠ 1 or ContainerId ≥ 12 the byte is **not read**.
- Server: `InventoryItem.Write` always writes `Extra` (Model/Shared/InventoryItem.cs:454-458); InventoryData.Write
  calls it for every item (SendSelfToClient.cs:574-576). One such item would leave 1 byte for the next field, and
  SendSelf would fail its exact-consumption check (FUN_142b4e1b0) → no login.
- Today no item can trigger it: Inventory.ToPacket always sets OwnerType = 1 (Game/Inventory/Inventory.cs:1092), and
  every container the server stores is < 12 (0 bag, 1 vault, 2 equipped, 4 buyback, 5 resources, 6 vault collection,
  7 overflow, 8 currency). The capture's 24 items are all owner 1, containers 0/2/5/8 [P].
- docs/unknowns.md ("after the record (SendSelf list only)") and the InventoryItem.ExtraData comment describe the byte
  but not the condition.
- **Fix:** in `InventoryData.Write`, write `item.WriteRecord(writer)` and then the extra byte only when
  `item.OwnerType == 1 && item.ContainerId < 12`, or leave such items out of the list with a log line. Add the
  condition to the ExtraData comment and docs/unknowns.md. No in-game check needed (no current item reaches it).

### F2. docs/sendself.md: "+0x18 string = chat language (Lua SetChatLanguage reads it)" is wrong — STALE

- [P] Lua `Ui.SetChatLanguage` (FUN_1435d1d70) and `Ui.GetChatLanguage` (FUN_1435d1f80) call the player's **vtable**
  slots +0x18 / +0x20 (`(**(code **)(*DAT_145431a20 + 0x18))(DAT_145431a20, n)`); they don't read the field at
  +0x18. The row comes from re/out/fieldmap_SendSelfToClient_Read.md, which matched `*DAT_145431a20 + 0x18` (a vtable
  load) as a field read.
- **Fix:** docs/sendself.md table row `0x18`: change "chat language / Lua SetChatLanguage reads it" to "string (the
  model's AccountName); live 'Freldam' = the character's name; no reader found yet (see lead N1)".

### F3. LanguageId / CountryId sent as 0 (live 2 / 218; client default 2) — GAP (low)

- [P] Unchanged since base. SendSelf +0x10c LanguageId and +0x110 CountryId (SendSelfToClient.cs:2477-2479, no
  default, BuildSendToSelfPacket never sets them) go out as 0. Live capture: language 2, country 218.
- What reads them [P]: the in-game-purchase country list handler FUN_1432537e0 takes the language from player +0x10c
  (default 2 when there is no player) and names every country with FUN_143a91410(language, countryCode, …) — the
  country codes it lists include 0xda = 218. ClientRequirementCheckLanguage / Country (FUN_14370e660 /
  FUN_14370e5e0, called with +0x10c / +0x110 from part_0118.c:20443/20489) compare them with a requirement's value.
- Effect [I]: with 0 the store's country names may come out in no language and any requirement keyed to language 2 /
  a country fails. Not found in OPEN-LEADS.
- **Fix:** default `LanguageId = 2` and `CountryId = 218` in the model (the live values; 2 is also the client's own
  default), or take them from the account if the login server ever learns them. In-game check: open the store's
  country / purchase screen if it is reachable on this server.

### F4. Other placeholder-vs-live fields in SendSelf, no visible effect — OK-NOTE

All [P] from the capture parse and the readers; none changed since base:
- `IsPlayerStudioRegistered` (+0x100): live true, server false. Only reader is Lua
  `InGamePurchaseStoreScreen.IsPlayerStudioRegistered` (FUN_1435e9710); no 0.695 UI script, SWF or Lua calls it
  (re/out/agent_ui/uicalls.txt, UI/, re/as3_695: no hits). No effect.
- `FacialAnimReceiver` (+0x285): live true, server false (`//message.FacialAnimReceiver = true;`,
  WorldSession.cs:1345). Only the /soemote receiver path; no effect without SOEmote.
- `Unused74a78` (+0x74a78): live 2354, server 0. The reader is its only access in 0.695 (part_0059.c:23856/23861).
- Inventory containers 7-11: live (24, true) each (24 = item count), server (0, true) and container 8 (1, false).
  FUN_142c47af0 stores them at container +0x30 / +0x34; works in play; not traced further.
- `Stats` (+0x75670): live 47, server 0 — by design: the server sends the player's own stats after ClientIsReady
  (Game/CharacterStats.SendOwn, MiscHandler.cs:364) into the same table (FUN_142fd48b0). OK.
- Item stats only on gathering tools (live also sent armour / weapon stats in ItemRandom block 1 and on two items in
  block 2): already recorded in docs/unknowns.md "Item records" ("the tooltip doesn't read it"). Not re-reported.

### F5. ClientBeginZoning rename kept every value in its slot — OK-NOTE

- [P] The 2026-10-18 renames (WorldId / ZoneId / WorldNameId "four slots off") left the wire as it was (u8, 6 × 4-byte,
  2 bools) and FUN_142c23310 reads exactly that. Compared base and HEAD Teleport.RezoneNow: +0xa4 was `zone`, is now
  `details.GeometryId` = `zone.Id` (Landscapes.ZoneDetails:394; 406 in the capture) — same value; +0xa8 world and +0xac
  zone unchanged; new values: MovementVersion 1, ZoneMode = details (0), WorldNameId = details. The dispatcher (case
  8, FUN_142d1ccf0) stores +0xa4 → game +0x31fac, +0xa8 → +0x378cc, +0xac → +0x378d0, +0xb0 → SetWorldZoneInfo
  worldNameId, matching the model's comments. Case 8 and case 0x11 (SendZoneDetails) both drop the packet unless it is
  consumed exactly (`remaining < 1`) [P]; both models write exactly the reader's fields.

## 2. Checked and fine

- SendSelf top level (FUN_142c19bd0) vs Write/WriteTail: same order and widths field for field, incl. the 2-bit
  transient id (FUN_142c0bd80), model block, 4+4 floats, identity, the 4 bools after class progressions, the u32 list
  after first-time events (each id → FUN_142fd1560, an empty function in 0.695 too), stats {key, record}, skill
  instances, vehicle tail (u64,u32,u64,u64,u32,u32), admin bool, filters, bio [P].
- Live capture round-trip with the HEAD model: 655,356 / 655,356 bytes [P].
- CharacterModelInfo (FUN_142c18710 / FUN_142c3f540): list {3 × u32}, u32, string, u32, u32 [P]. Only a rename since base.
- CharacterIdentity (FUN_142c25b90): 3 × u32, 2 strings [P].
- Profile (FUN_142c1ac40): 8 × u32, flag, u32, item classes {key, u32, u32}, flag, 2 × u32, flag, i8, 3 flags, 15 × 4
  bytes in the reader's order (+0x70 … +0x34) [P]; server values follow ProfileDefinitions as the capture.
- Class progression (FUN_142c36d50): {key, +0xc, +0x8}; server (46, 46, 0) = live [P].
- Currencies (FUN_142c35320), cooldowns (FUN_142c35860 / FUN_142c1e470), palettes (FUN_142c3fc60 / FUN_142c25c60 /
  FUN_142c35af0 / FUN_142c25d30), acquaintances (FUN_142c2e960) [P].
- Collection + entries (FUN_142c18f50 / FUN_142c3d290 / FUN_142c190d0) and RewardBundle inside it [P].
- Buff / ClientEffectData (FUN_142c19230): 161 bytes, every field in order incl. 4 × u32 at +0x80, +0xa0..+0xb0, 4 ×
  u32 at +0x90 (FUN_142c02830) [P].
- Inventory (FUN_142c47af0): 12 × {u32, bool}, then items [P]; item record (FUN_142c208e0 → FUN_142c20f60 definition
  header with two ItemRandom blocks FUN_142c20e60 → stats FUN_142c3b3d0/FUN_142c2fc70 (8 fields), catalysts
  FUN_142c38c90/FUN_142c18530; FUN_142c325d0 last-cast (0x7FFFFFFF = never); rental FUN_142c1fb10; design
  FUN_142c395e0; metadata FUN_142c32650 (body only for type 1); +0x138 clamped ≥ 0) — matches WriteRecord [P].
- RewardBundle (FUN_142c2f040): bool, coins, currency map, 4 × u32, i64, 5 × u32, 2 × u64 (vtable setters), 2 × u32,
  typed entries, u32 [P]. Entry types other than 1 are written as the type alone (RewardBundle.BundleRewardEntry) —
  latent only: no server code builds a BundleRewardEntry (grep), Game.Achievements writes its bundles itself.
- Achievement (FUN_142c161b0) and Objective (FUN_142c22ee0, event bar FUN_142c22e30) [P, and 865 records in the capture].
- Mount (FUN_142c23fe0): 3 × u32, u64, bool, u32, string [P]; sent empty.
- Quest manager (FUN_142c1b3d0) and quest objective: as the model comments; sent empty [P layout of the empty form].
- Titles (FUN_142c1b230): u32 n × {id, type, name}, u32 current [P]; Titles.SelfData fills it.
- Inventory filters (FUN_142c3d470 → FUN_142c37990): u32 n × {u32 key → +0x50, u32 +0x8, string +0x10, string
  +0x28}, u32 NextFilterId [P]; InventoryFilters.SelfData writes key = id; string reader FUN_142d4b5d0 has no length cap
  beyond the body, so the server's 255 / 2048-byte caps are safe [P].
- u32 sets (FUN_142c34a10: known props, Parlor sets, first-time events, known recipe ids): u32 n × u32, no cap [P].
- ClientBeginZoning (FUN_142c23310) and ContinentDefinition (FUN_142c1ddf0, 4-float block FUN_142c1e3c0, 2-float
  block FUN_142c3d690): every field's packet offset maps to the model's definition offset (packet − 0xb8 / − 0x68) [P].
- SendZoneDetails (FUN_142c246b0): str, u32, bool, str, u32, u32, u64, 3 × u32, continent, bool +0x200; senders all use
  Landscapes.ZoneDetails (WorldSession.cs:725, MiscHandler.cs:391, Teleport.cs:360) [P].
- ClientGameSettings (FUN_142c236c0): 3 × u32, bool, 6 × 4 bytes = the 37-byte LiveBody sent (WorldSession.cs:730) [P];
  the model's new field split matches the reader, the bytes never changed.
- ClientPcDataMessages.cs: C→S only (PlayerFilterUpdate parse = writer FUN_142fc3d20 order); nothing written. Speech
  pack subs 1/2 unhandled — already in coverage-695.md / famdiff-695.md (not re-reported).
- Opcode bytes: SendSelf 0x0F00 → `0F 00` + u32 size (prependSize, as the capture), 0x2300, 0x2E00, 0x8E00 →
  u16 bases [P via GetOpcodeBytes / EnumBytes].

## 3. New leads

### N1. SendSelf +0x10 (u64) and +0x18 (string): what are they?
- **Need:** the meaning of the first u64 (model `Guid`) and the string (model `AccountName`). Live: +0x10 =
  0x2D4ADFB8 (≠ the character guid 0x4B561BAE72200691 at +0x30), +0x18 = "Freldam" (= the character's first name).
  Server: both u64 = Character.Id, string = Account.Username — so the client may show or use the login name where live
  had the character / display name.
- **Searched:** the SendSelf dispatcher (FUN_142d1ccf0 case 2: logs +0x30 and +0xa8 only); "Player GUID" (game
  +0x32500, not SendSelf); a scan of every function that loads the player (`… + 0xf18)` / DAT_145431a20) and then reads
  +0x10 / +0x18 / +0x20 of it (11 / 7 / 7 hits, e.g. FUN_142cbada0, FUN_143637f60, FUN_1435e5500 — the ones opened were
  other objects or the teardown); the Lua bindings GetName / GetPlayerName / SetChatLanguage (none read it);
  docs/sendself.md, docs/unknowns.md, OPEN-LEADS (no entry).
- **Try next:** a Frida read watch on player+0x10..+0x30 after login (who reads them), or Ghidra xrefs on
  `[reg+0x18]` inside ClientPcData's methods 0x142fc0000-0x142fe0000. If +0x18 turns out to be shown (chat name,
  social), send the character name as live did.

## Summary

Every byte the server writes at login and zone-in in this slice matches the 0.695 readers. The SendSelf body was
compared field for field with FUN_142c19bd0, and the HEAD model reads the live 0.695-era capture to the last of its
655,356 bytes. So every record the capture fills is proven (865 achievements, 2,363 recipes, 24 items, …), and the
records the server fills that the capture leaves empty (filters, titles, u32 sets) were checked against their readers
one by one. ClientBeginZoning, SendZoneDetails, ContinentDefinition and ClientGameSettings match their readers exactly;
the 2026-10-18 renames kept every value in its slot. No live MISMATCH. Findings: one latent framing trap (F1: the
per-item byte the client skips for non-player-owned items or containers ≥ 12), one stale doc row (F2), low-impact
placeholder values (F3 language/country 0 instead of 2/218; F4 no-effect fields) and one lead (N1: +0x10/+0x18
meaning; the server sends the account username where live sent the character name).

Housekeeping: scratch is in `…\scratchpad\e3-wire-a\` (flat.py, ss_flat.txt, zd.c, cap*.fsx). By mistake six
read-only decompile dumps were also written to Git Bash `/tmp` (`<user folder>\AppData\Local\Temp\x_FUN_*.c`:
142cbada0, 142cd9980, 14347b0b0, 1435dd7b0, 1435e5500, 143637f60). They are outside my folder, so I did not delete
them; they are safe to remove.
