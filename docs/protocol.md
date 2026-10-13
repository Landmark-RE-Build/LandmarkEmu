# Landmark 0.589 network protocol

How the client and server talk, layer by layer, and the layout of every packet family the emulator handles.
Types and conventions are listed in [README.md](README.md#conventions). "C→S" = client to server, "S→C" = server
to client.

## 1. Transport

### 1.1 SOE reliable UDP

Both the login server and the zone/gateway server speak the SOE (Sony Online Entertainment) UDP protocol, the same
family used by PlanetSide 2 and H1Z1. Protocol opcodes are big-endian u16:

| Opcode | Name | Notes |
|---|---|---|
| 0x01 / 0x02 | SessionRequest / SessionReply | Negotiate CRC seed/length, max packet size, compression flag. |
| 0x03 | MultiPacket | Several protocol packets in one datagram. |
| 0x05 | Disconnect | Either side. The client waits for the server to send one after `ClientLogout` (see §6). |
| 0x06 | Ping | Keep-alive. |
| 0x07 / 0x08 | NetStatusRequest / Reply | Client statistics. |
| 0x09 / 0x0D | Data / DataFragment | Reliable, sequenced payload (fragmented when large). |
| 0x11 / 0x15 | Ack / AckAll | Acknowledgements. |

- **Retransmission:** the server resends an unacknowledged reliable packet after `300 ms + 1.25 × ack ping`, then
  doubles the wait for each further resend (capped at 8 s). Without the backoff, a client that acks slowly (long
  frames while it meshes terrain) had its whole window resent every 1.3 s: 3,500 resends a minute against 1,500
  originals, and loading into a deep cave took 18 minutes instead of 7.
- **Compression:** an optional 1-byte flag before the body (1 = compressed). The compressed body uses the
  client's block codec (see §1.4), and only bodies longer than 0x17 bytes are compressed. It works but is left
  off: the zone payload is RC4 ciphertext by then, so it never shrinks.
- **Encryption:** after the gateway login, Data payloads are RC4-encrypted with the session key.

### 1.2 Gateway and tunnel

Inside SOE Data packets the zone server uses *gateway* packets. The client logs in to the gateway with a
`LoginRequest` (character id + ticket), then everything is wrapped in `TunnelPacketToExternalConnection` (S→C,
0x05) / `TunnelPacketFromExternalConnection` (C→S, 0x06) carrying a channel byte:

| Channel | Use |
|---|---|
| 0 Zone | Almost all game packets (this document). |
| 1 World | Unused here. |
| 2 PositionUpdate | Player movement. |
| 3 Voxel | Terrain cells. |

### 1.3 Zone packets

A zone packet starts with its opcode. Most opcodes are one byte followed by a padding/zero byte (written here as
`u16 base`, e.g. `F9 00`), then a sub-opcode whose width depends on the family:

| Family | Base | Sub-opcode width |
|---|---|---|
| PlayerUpdate | 0x26 | u8 (`26 00 58`) |
| ClientUpdate | 0x29 | u16 |
| Command | 0x1E | u16 |
| AbilityBase | 0x27 | u16 |
| InventoryBase | 0x2D | u16 |
| GroupsBase | 0x2B | u8 |
| UiBase | 0x32 | u8 |
| RecipeBase | 0x42 | u32 |
| FriendBase | 0x4D | u8 |
| GuildBase | 0x83 | u16 |
| ResourcesBase | 0xC4 | u32 sequence + u8 sub |
| EquipmentBase | 0xCB | u8 |
| TerrainCell | 0xEC | u32 |
| PropBase | 0xED | u8 |
| TemplateBase | 0xF0 | u8 |
| VoxelConstructionBase | 0xF4 | u8 |
| MailBase | 0xF8 | u32 |
| ClaimsBase | 0xF9 | u8 |
| LootPinataBase | 0xFB | u8 |
| TeleportBase | 0xFC | u8 |

The client's zone dispatcher looks the opcode up in a byte table (`Zone_DispatchPacket`, see
[client-reference.md](client-reference.md)). In the decompiled switch statements, case labels are often shifted
by one (`case 0x2c` handles sub 0x2D).

### 1.4 The client block codec

Several blobs (terrain cells, compressed SOE bodies) use the same container: the magic bytes `A1 B2 C3 D4`, a
**big-endian** u32 uncompressed size, then a zlib stream. Terrain layer blobs additionally put a little-endian u32
raw size in front.

### 1.5 Rules the client enforces

- **Consume exactly.** Most readers reject the whole packet when bytes are left over or missing. A wrong field
  count usually means the packet is silently ignored. A few, such as the health resource record, crash the
  client instead.
- **Answer every request it waits on.** Examples:
  - An unanswered ability cast leaves the client "cast pending", which shows "Ability on Cooldown" forever.
  - An unanswered `TransferItem` blocks the second half of a tool switch.
  - An unanswered `CasterInfoRequest` disables the tool.
- **Use `WriteUShort` for u16 fields.** `GamePacketWriter.Write` has no `ushort` overload. A `(ushort)` value
  passed to `Write` is promoted to `uint` and goes out as 4 bytes.

## 2. Login server

The login server (default UDP 20042, `Server=` in `ClientConfig.ini`) handles `LoginRequest` (session key from the
launcher or from a character-select session), the server list, the character list, character creation/deletion
and `CharacterLoginRequest`, which hands out the gateway address and ticket.

## 3. Entering the zone

| Step | Packet | Notes |
|---|---|---|
| S→C | `SendSelfToClient` 0x0F | The player's whole state: position, appearance, inventory, hotbars, recipes, currencies. The 0.589 layout differs from 0.695 after `Unknown68`; it must be consumed exactly or the client aborts with `0xBADBEEF`. Its transient id is the player's 2-bit-length entity id. |
| S→C | `ZoneDetails`, `ZoneDoneSendingInitialData` | |
| C→S | `ClientIsReady` | The server then sends the rest of the world (other players, props, claims, trees). |
| S→C | `Synchronization` 0xC3 | 6 × u64 + bool. Every client must get the same server millisecond clock, or remote movement stutters. |
| S→C | `GameTimeSync` 0x37 | `{u64 timestamp, f32 cycle speed, bool}`. The client takes the time of day from the timestamp's UTC h/m/s and advances it at the cycle speed (see gameplay.md, world clock). |

### 3.1 SendSelfToClient fields (0.589)

The reader (`SendSelfToClient_Read`, FUN_142b80420) stores each field into the client's self-data object (global
`0x1453524a0`). Names come from what the client does with each offset. The Lua bindings that read them were found
with `re/tools/luatable.py` + `re/tools/fieldreaders.py`.

| Model field | Offset | Meaning (client evidence) |
|---|---|---|
| `ServerTime` | local | Server clock (unix s). The client keeps (own clock − this) to convert `+0x290`. |
| `TransientId` | +0x38 | 2-bit-length entity id. |
| `Position` / `Rotation` | +0x260 / +0x270 | Spawn position and rotation quaternion. |
| `Coin` | +0x72a38 | Lua `GetPlayerCoinCount`, merchant `SetPlayerCoin`. |
| `Currencies` | +0x72a40 | (currency id, amount), each passed to the currency setter FUN_142f33fd0. |
| `CreationDate` | +0x74a70 | Unix s; `/played` "Character Created". |
| `PlayedMinutes` | +0x74a7c | `/played` adds the current session. |
| `LoginCount` | +0xf4 | `/played` "Character Logins". |
| `IsUnderage` | +0xf8 | Lua `IsUnderage`. |
| `IsDead` | +0xf9 | Opens `RespawnHandler:Show` at login. |
| `MembershipStatus` | +0xfc | 0 none, 1 trial (`IsTrialMember`), 2 member (`IsMember` = non-zero). |
| `IsPlayerStudioRegistered` | +0x100 | Lua of the same name. |
| `ShowMemberNagScreen` | +0x108 | Lua `ShouldShowMemberNagScreen`; ignored for members. |
| `LanguageId` | +0x10c | `ClientRequirementCheckLanguage`; localises shop country names (default 2). |
| `CountryId` | +0x110 | `ClientRequirementCheckCountry`. |
| `PreferredLanguages` | +0x74a98 | Bit mask; the client sends `PlayerUpdatePreferredLanguages` on change. |
| `SpeechSetId` | +0x280 | Voice set used by `PlayerPlaySpeech`. |
| `ClassProgressions` | +0x7a9d0 | `ClassProgression` objects keyed by class id. |
| `FacialAnimReceiver` | +0x285 | SOEmote receiving (`/soemote receiver`). |
| `CombatInputDisabled` | +0x286 | Keeps the "Combat" input context off. |
| `BypassClaimPermissions` | +0x287 | Claim permission and camera checks pass (FUN_142d15900, FUN_142d37330). |
| `IsContentBuilder` | +0x288 | Toggled by ContentManagement `SetContentBuilder`. |
| `Unused74a78`, `UnusedFa`, `Unused74a9c`, `UnusedTimestamp`, `Unused284` | | Stored, never read by the 0.589 client. |

- Identity: `u32 ×3, first name, last name`. The display name is first + " " + last. The three u32s are copied onto
  the player entity; no reader was found yet.
- Profile records (FUN_142b81480, 200-byte client profile): the model's `Unknown0..33` map in order to offsets
  +0x4, +0x8 (name), +0xc, +0x14, +0x1c, +0x28, +0x20, +0x24, flag 0x80, +0x18, list +0x40, flag 0x40, +0x2c, +0x30,
  flag 0x20, +0x38 (signed byte), flag 0x10, flag 0x08, flag 0x04, +0x70, +0x74, +0x78, +0x80, +0x7c, +0x84, +0x8c,
  +0x90, +0x94, +0x98, +0x9c, +0xa0, +0x88, +0x3c, +0x34. Not named yet.
- Inventory: 12 × `{u32 size, bool}` (bag +0x30 / +0x34), then the items. Per item (`ClientItem`, FUN_142b87110):
  guid +0xc8, count +0xd0, `SecondsSinceLastCast` (FUN_142b98a50: last cast = now − value, `0x7FFFFFFF` = never),
  optional sub-records at +0x120 / +0x128, `MetaData` (`ItemInstanceMetaData`), `TimedDeleteSeconds` +0x138
  (clamped at 0). The client also carries the server's item table schema (`items.stack_count`,
  `items.inventory_slot`, `items.timed_delete_offset`, …) as a plain name list, useful as a reference only.

## 4. Entities

### 4.1 PlayerUpdate 0x26 (u8 sub)

| Sub | Name | Layout / notes |
|---|---|---|
| 0x01 | RemovePlayer | `i16 version (0), u64 guid` |
| 0x03 | UpdateHitpoints | `u64 guid, u32 current, u32 max, u32` |
| 0x0B | PlayCompositeEffect | `u8 target type, target, u64 instance, u32 effect, u32 delay ms, u32 duration ms (0 = the effect's own), bool, bool, bool`. Target type 1 TargetCharacterGuid `{vec4 offset, u64 guid}`; 2 TargetLocation `{vec4 offset, vec4 position (w 1), vec4 rotation}` (FUN_142b6a1b0). Used for hits, states (with a duration) and area effects on the ground. |
| 0x2D | Destroyed | `u64 guid, u32, u32, u32, bool keep` |
| 0x3B | Respawn request (C→S) | `u32 type, …` |
| 0x49 | Currency | `u64 guid, u32 n, n × {u8 currency id, u32 qty}` |
| 0x58 | AddLightweightPc | Other players: guid, transient id, identity, model, position/rotation vec4, scale, flags, **movement version byte (must be 1)**, … |
| 0x59 | AddLightweightNpc | The shared lightweight base only (creatures). |
| 0x5B | LightweightToFullPc | `bool compressed, byte[] blob, movement body, stat lists`. The appearance field after the 3 strings must be 0, or the body is hidden. |
| 0x5E | FullCharacterDataRequest (C→S) | `u64 guid` → answer with 0x5B (players) or 0x63 (props/NPCs). |
| 0x62 | AddLightweightProp | Props, trees, plants, the Spire (see 4.2). |
| 0x63 | LightweightToFullNpc/Prop | Needed before a placed prop can be edited. |
| 0x69 / 0x6A | InteractionInfo request / reply | Reply entries `{u64 guid, u32 type (3 = interactable), 4 × u32 colour, u32, f32, f32 range, u8}`. |
| 0x6B | DynamicInteractionFlagging | `bool, list<u64 guid>`: marks entities the client should ask about. |
| 0x71 | BindingCustomization (C→S) | Key-binding report; nothing to store. |
| 0xC7 | ManagedPosition (C→S) | Position of an entity the client drives itself (a prop held for placement). |

### 4.2 Lightweight entity base and the prop part

The lightweight base is shared by props and NPCs:
- `u64 guid`, 2-bit-length transient id;
- two names (string key, literal), `u32` name text id;
- `u32 model id` (Models.txt);
- `vec4 scale`, three alias strings;
- `vec4 position`, `vec4 rotation` (quaternion);
- character-model info, flags, the attachment block;
- **three f64 = the origin the position is relative to** (send the zone origin);
- draw distance, effect tags.

The client places the entity at `(position × 10 + origin) − zone origin`, scaled back by 0.1.

The prop part follows:
- `u64 owner`;
- a physics-body block: `u8 mode, u32 flags, u8, …`. Mode 1 = flags only. This is the movement body, **not**
  collision; collision comes from the model's `.adr`;
- a list, `u32`, `u64`, `bool`.

### 4.3 Position 0x7F and the movement body

`Position` (S→C) is a 2-bit transient id, then the movement body, then 4 floats (scale; 0 = unchanged).

The movement body is `u8 type, u32 timestamp (server ms), u8 version`, then signed 2-bit-length integers divided by
10^precision:

| Type | Content |
|---|---|
| 0 | Full: position (3 × p2), rotation (4 × p5), velocity (3 × p5), 8 × p2, p1, p5, flags |
| 1 | One 2-bit uint |
| 2 | Position + velocity |
| 3 | Rotation + 4 × p2 |

Non-full updates are dropped unless their version byte matches the entity's stored version.

**Frame:** a client sends its own movement in the **local** frame. Movement about **other players** is in the
remote frame (`local + (2558400, 2000, 2556450)`, `WorldSession.RemoteFrameOffset`), matching how players are spawned
(AddLightweightPc). Movement about **NPCs/creatures** is in the local frame, matching AddLightweightNpc (which
carries the zone origin itself); creature updates sent in the remote frame were ignored.
The state uint after the floats (2-bit length) is 0x401-style bits while moving and has bit 0x40 set when standing
still (e.g. 0x441); it is followed by one more byte block (empty: `00`).

### 4.4 Stats and resources

- **ClientUpdate 0x29/0x2C UpdateCalculatedStats:** `u64 guid, list<{u32 stat id, f32 value}>`. Stats used:
  - 3 HarvestingArea: a volume, shown as the mining ring diameter.
  - 11 harvest tier.
  - 13 HarvestSpeed: recast = 50 / max(1, speed) + base.
- **ResourcesBase 0xC4 (health bar):**
  - Header: `u32 sequence (must increase per player), u8 sub, u64 guid`.
  - Record: `u32 resource id, u32 type (1 Health), u32 variable count, …`, then 10 × u32 (the current value is
    first), `u32`, 4 × u64, 3 bools, `u32`.
  - A wrong count crashes the client.
  - Resource types: Health 1, Mana 2, Stamina 10, Armor 11, RevivePoints 12, Shield 14.
- **ClientUpdate 0x29/0x01 Hitpoints:** combat text only. It does not move the HUD bar.

### 4.5 Equipment 0xCB (u8 sub)

| Sub | Name | Layout |
|---|---|---|
| 1 | SetCharacterEquipment | Full set. |
| 2 | SetCharacterEquipmentSlot | `u32 profile, u64 character guid, u32 equip slot, u64 item guid`, 2 strings, attachment `{model .adr, texture alias, tint alias, decal, u32 tint, u32 composite effect, u32 slot, u32, bool}` |
| 3 | UnsetCharacterEquipmentSlot | `u32 profile, u64 guid, u32 slot` |

## 5. Inventory and items

- **Item definitions:** Command 0x1E/0x4B. `u32 size, u32 count`, then per item `u16 compressed size, u16 raw
  size, LZ4 block`. Sent at world entry (the client logs two ItemDefinitions.log lines per item, opening the file
  each time: ~15 s for 2,885 items on a PC with real-time file scanning). The cache survives a logout to character
  select, so the server skips the list for the same client coming back (its account session key is still the one
  from CharacterSelectSessionResponse).
  - 0x49 ItemDefinitionRequest (client → server) `u32 id`: sent for a missing definition from a few icon / tooltip
    wait lists (FUN_14335a2e0); most lookups never ask.
  - 0x4A ItemDefinitionReply `u32 size`, one entry as above; the client adds it and drops the pending request.
- **InventoryBase 0x2D (u16 sub):**

| Sub | Dir | Layout / notes |
|---|---|---|
| 0x02 | C→S | EquippedRemove `{u32 equip slot, u32 profile}` |
| 0x03 | C→S | EquipByGuid `{u64 item guid, u32 profile, u32 equip slot}` (Lua `EquipItem`) |
| 0x0B | C→S | DeleteItem `{u64 guid, u32 count, u32 mode}` |
| 0x0E | C→S | RearrangeItemSlots `{u32 container, u32 n, n × {u32 slot, u64 item guid}}` — the bag window's sort |
| — | — | TransferItem quantity: a count below the stack (split dialog / Ctrl-drag) or onto the same item merges / splits; container 1 = the vault (open through Command 0x1E sub 0x50 InteractionVault `{u16 sub, u64 vault entity}`) |
| 0x0F / 0x10 / 0x11 | C→S | Tooltip requests: by location, by guid, by definition id |
| 0x12 | S→C | Tooltip reply: item record with the compressed definition chunk |
| 0x15 | C→S | ActivateItem `{u64 guid}`: use / start prop placement / learn recipe |
| 0x16 | C→S | TransferItem `{location dest, location source, u64 guid, u32 qty, bool}`. **The first location is the destination.** |
| 0x19 | S→C | TransferItemResponse = the request echoed + `u32 result`. Must answer every 0x16. |

  - Location = `{u32 owner type, u32 container (0 bag, 2 equipped), u32 slot}`, plus a `u64` owner for owner
    types 2, 3 and 5. Slot `0xFFFFFFFF` = any.
  - Right-click quirks:
    - Right-clicking gear in the bag sends 0x16 with destination (equipped, slot 2 = main hand) for *any*
      gear. The server uses the item's own slot.
    - Right-clicking worn gear names bag slot 0. The server falls back to the first free bag slot.
- **Coins = Lumens:** the client's coin count everywhere (top bar, inventory, mail send/cash on delivery, trade)
  is the stack of item 1579 (StringHashToValue `Currency.ItemDefinitionId`) in container 8, which the self packet
  must declare with a size above 0. ClientUpdate 0x29/0x13 CoinCount `{u32}` sets the separate Lua
  `Player.GetPlayerCoinCount` value. The inventory's second currency slot is Daybreak Cash: InGamePurchase 0x45
  sub 0x2D WalletBalanceUpdate `{u32}`.
- **ClientUpdate ItemAdd 0x29/2:** the blob starts with `bool definition follows` (send false), then the item.
- **ClientUpdate ItemUpdate 0x29/3:** `guid, count, durability, u32, u64, slot, container`, then 2 × {5 × u32},
  2 × u32. Use it for moves: delete + add loses the hotbar binding.

## 6. Session lifecycle

| Packet | Notes |
|---|---|
| KeepAlive 0x63 (C→S) | `{u32 game time}`, periodic. No reply. |
| CharacterSelectSessionRequest 0xF6 (C→S) | Logout to character select. |
| CharacterSelectSessionResponse 0xF7 (S→C) | `{bool ok, string login session}`. On `false` the client quits. |
| ClientLogout 0x14 (C→S) | Sent while leaving the zone. **The server must close the connection now.** The client waits up to 2 s; if the disconnect arrives later, its "disconnected" handler starts a second logout and the client shuts down. |

## 7. Abilities 0x27 (u16 sub)

- **2 CasterInfoRequest (C→S):** `u32 ability`. Repeated until answered.
  - Holding a tool asks for its abilities.
  - Weapons ask for combo abilities that aren't on the item.
  - Travel asks for 185/186.
- **3 CasterInfoReply:** includes range (min/max), cast and recovery seconds, cost pool, animation id, tool
  re-pick mode, name/description ids. The server answers from captured bodies and item data.
  - The f32 after the two strings (`ArcDegrees`, stored at record +0x60) is the ability's arc width. With the
    min/max range it shapes the sickle's blue plant-harvest wedge (ActionStateReaping → PlantHarvestDecal, set in
    FUN_1433abd90 as reaping params 9/10/11). An arc of 0 draws nothing.
- **1 AbilityCastAttempt (C→S):** `u64 cast guid, u32 hash, u32 sequence, u32 ability`, target position, tail.
  - For picks, tail bytes 44–55 are the **mining ring point**.
  - For axes, tail +36 is the targeted entity guid.
- **7 CastSuccess (S→C):** caster guid, cast guid + hash, ability, animation, … Every cast must be answered.
- **6 CastFail (S→C):** the answer to a refused cast; same backtimed header as CastSuccess, then `u64 caster, u64 cast
  guid, u32 cast hash, u32 ability, u32, u32 animation (played if not 0), u32, u32 kind (2 on a harvest shows
  Harvest.CastFailMessage)` (reader FUN_142d63390, handler FUN_142d79ee0). The client ends the cast, clears its casting
  / charging state, drops the pending cast and takes back the hotbar cooldown it started (FUN_142d87d90).
  AbilityStartFailed / StartCanceled (0x10 / 0x11, `{u64, u32, u32, u32 ability, u64 caster}` / `{u32 ability, u64
  caster}`) only act on an ability started with AbilityStart.
- **0x18 AbilityCollisionReport (C→S):** the client reporting what an ability hit (seen when arriving at the
  Spire). Not handled yet.
- **Client can-cast errors:** 2/15 cast pending, 3 cost pool, 5 cooldown, 14 busy.
- **0x0E FinishChargedCast (C→S):** `u32 ability, u32, vec4 camera position (w 1), vec4 aim direction (w 0)` — sent
  when a charged ability's button is let go (FUN_142d767b0; charged = caster-info kind 0xF7150354, max / min charge
  seconds at reply +0xd0 / +0xd4). Answer the cast (CastSuccess) only then: one during the charge ends it.
  **0x0F CancelCast (C→S):** the charge was given up.
- **0x0C EffectStart (S→C):** puts an ability effect on a character (reader FUN_142d64890, handler FUN_142d7b690
  → character virtual +0x240). Header `u16 sub, u8, u32`; body `u64 key, u32 key, u32 effect id, u32, u32 type, 4 × u32,
  bool, u32, 4 × u32, u32, u64, u64 target, u32, 8 × u32 (incl. rotation), u32 animation type, u64, u32, vec4,
  5 × u32 (2nd: f32 strength → effect +0xa4, 3rd: f32 fade → +0xa8), vec4, 8 × u32 (7th: f32 duration s → +0x68,
  8th: start in server-clock ms → +0x40), bool, u32`. The type is the hash of an effect type name (FUN_142de0ef0's
  table). The local character acts on some types itself (FUN_1432777f0): **DetectCave 0x6eb699a5** goes to
  CaveDetectionManager (needs strength > 0; ends at start + duration × 1000; fade capped at half the duration) and
  makes the voxel renderer draw nearby caves in green — the Cave Seeker. EffectEnd (0x0D) with the same key removes
  it (FUN_14307df30).

## 8. Terrain 0xEC (`EC 00`, u32 sub)

Full detail in [terrain.md](terrain.md).

| Sub | Dir | Name |
|---|---|---|
| 1 | C→S | ClientCellRequest `{u16, u64, list<{u64 cell, 4 × u32 layer version}>}` |
| 2 | C→S | PendingRequestTimeout `{u64 cell}`: a cell edit was never answered with sub 3 (the client stops editing it) |
| 3 | S→C | ClientCellUpdateResponse `{u64 cell, bool ok, string}`: send one per cell of every ApplyDiffRequest; with ok false the string is shown as an alert |
| 5 | S→C | CellsUpdate `u16 pad, list<{u64 cell, u32 version, list<{u32 layer, u32 layer version, u8 flag, blob}>}>, list<u64 transaction>, bool, bool`. **Version 0 = block (voxel) data**, which the client re-meshes; other versions carry finished meshes. |
| 7 | S→C | CellBlockDamage (crack visuals only) |
| 0x0A / 0x0B | S→C | Manifest / ModifiedCellManifestUpdate `{u64 cell, u32 code, u32 version, u8}` (the client re-requests) |
| 0x0C | S→C | ZoneProcessingComplete `{u16, u64 transaction}` |

## 9. Building: VoxelConstructionBase 0xF4 (u8 sub)

- **1 ApplyDiffRequest (C→S):**
  - Body: `u8, 24 bytes, list<{u64 cell, u64 cell, u32 4, u32 length, blob}>`, then the material usage
    `list<{u16 material, i32 count}>`, cell versions and areas.
  - Blob = 8000-byte changed-voxel mask + the cell in block format 2 (terrain.md).
  - Sent by every brush (Add, Remove, Smooth, Paint, …).
- **7 Response (S→C):** `{u32 request type, u32 result, bool}`.
  - Results: 0 ok, 0x0C insufficient materials, 0x0D props, 0x0E permissions, 0x11 too many template voxels.
  - Without a response, a later resend of the cell makes the client time out and stop editing.
- **2 ClientSelectionAreaRequest (C→S), copy and paste:**
  - Body: `u8 layer (FF), u32 action (0 copy, 4 paste), u64 level-2 cell, u32 x, y, z origin (voxels in that
    cell), u32 sx, sy, sz size, list<PropData>, 4×4 i32 rotation matrix (rows first), list<{u16 material, u32 n}>,
    u64, string, u64, list<{u64 cell, u64 version}> (every cell the box touches), list<{u64 key, u64, u32 type,
    u32 length, packed block, u32 if type is 2 or 3}>, u32, u8, string, u8`.
  - Copy (Select tool + Ctrl+C) carries one block: the box's voxels in a 40³ grid with the box origin at (0,0,0),
    same packed format as ApplyDiff (just the 8000-byte mask when nothing is solid). The server answers
    **6 ClipboardData** `{list<PropData>, list<{u16 material, u32 voxels}>, block list, 3 × u32 size, u32,
    bool has voxels}`. The paste tool only starts when that material list is not empty, and the client checks
    the player holds those amounts before placing.
  - Paste sends no voxels: the server places the copy it kept (grid point s → M·s, shifted so the turned box's
    minimum corner is the origin), charges materials, records one undoable edit, answers a Response (sub 7,
    type 2), then TerrainCell sub 3 for every listed cell **before** sending the new voxels. The client holds the
    listed cells pending (and drops their updates) until it hears sub 3; a copy marks them pending too.
- **0x0A ResponseObserved (S→C)** `{u32 operation, double×4 min, double×4 max, u32×3 direction, bool}` (reader
  FUN_1430b11e0, handler FUN_1430bbd60): another player's edit, effect only. Bounds are global VoxelFarm units (dm;
  the client subtracts its scene origin and scales by 0.1), the w components are ignored. Operation → brush effect
  (ModalToolBrushDefinitions BRUSH_TYPE): 0/2 placement (Stamp 4 when the bool is set, else FillStamp 11), 1 the
  same as a box, 3 delete (2), 4 heal (1), 5 smooth (0), 6 paint (5). Direction is the placement's up axis (0,1,0 for
  boxes); the bool picks ShapePlacement over ShapePlacementNoDestroy. A non-cubic box (or op 1) plays the box effect.
  The server sends it after every applied edit to the other players within 100 m (the builder's client played it).
  ApplyDiffRequest +0xe0 is the client's operation kind (1/2 box placement without/with an aimed cell, 3 any
  sculpting brush, 4–9 other operations); it doesn't name the brush, so the server picks delete/paint/placement/
  smooth from what the edit changed, and heal from the mask-only heal cells.
- **Voxel ownership (Voxel Vision).** The site window's Voxel Vision button fires `GameEvents:OnOpenVoxelVision`;
  the Builder List widget calls Lua `VoxelOwnership.RequestAllCellsForClaim(site)` (FUN_14354c980), which works out
  the site's level-2 cells (16 m, 40³ voxels) itself.
  - **4 ClientOwnershipRequest (C→S)** `{u32 n, n × {u64 cell, u32 cached version (0xFFFFFFFF = none)}}`
    (built in FUN_143086450; a 210 × 180 × 210 m site lists about 2,550 cells).
  - Every listed cell must be answered before the request completes (FUN_143083510) and the builders list fills:
    - **0x16 EnvOwnershipUpToDateResponse** `{u32 n, u64 cells}` when the cached version is current;
    - otherwise **0x17 EnvOwnershipDataResponse** (reader FUN_1430b0ed0) `{u64 (unused), u64 cell, u32 n + grid
      blob, u32 n + strings (owner names), u32 n + u64 (owner character guids), u32 n + i64 (unused), u32 version}`.
      Names and guids pair up into the cell's owner list; entry 0 is "nobody" (send a blank one). An empty blob means
      nobody owns anything in the cell.
  - **Grid blob:** packed codec (`A1 B2 C3 D4`, big-endian raw size, zlib) around FUN_14307ee80:
    - u8 version 4, u64, bool false, `u32 0` (a u16 list);
    - then the 40³ owner indices as 64 chunks of 10³ (chunk = z/10·16 + x/10·4 + y/10): an 8-byte chunk mask, then
      a 125-byte voxel mask per present chunk (bit = dz·100 + dx·10 + dy), then one byte per set voxel. Masks are
      MSB first.
    - The value is the voxel's index into the owner list. Voxel index = y + 40·(x + 40·z), the same as the edit
      mask.
  - The client lists builders (FUN_143603e50: OwnerName, OwnerCharacterGuid, OwnedVoxelCount, OwnedVoxelPct,
    OwnerColor) and highlights each one's voxels. Server: `Game/Terrain/VoxelOwnership.cs`. Every edit's solid
    voxels become the builder's; air, heals, digs and site removal clear them.
- **WorldEditing 0x010C (u32 sub, relayed):** the client sends its build cursor whenever it changes and draws other
  players' cursors from the same packets (ClientWorldEditingEffectManager FUN_142fe3ae0, one effect per guid; it
  does not skip its own guid, so the server must not echo). Wire: `u16 0x010C, u32 sub, u32 id`, then:
  0 Clear `{u8 selection-only, u64 guid}` (0 removes everything for the guid), 1 Box `{vec4 min, vec4 max, string,
  u64 guid}`, 2 Sphere `{vec3, f32 radius, string, u64 guid}`, 3 Shape `{4 × vec4 transform, u32, string, u64 guid}`,
  4 Selection `{u32, 2 × vec4, string, u64 guid, u32}`, 5 PreviewSetup `{vec4, template records, vec4, string, u64
  guid, byte array}`, 6 PreviewUpdate `{vec4, 4 × vec4, bool, string, u64 guid}`, 7 Fill `{u32 n × vec4, string,
  u64 guid}`. The guid is the builder's character guid (BaseClient +0x32508). Server: `Game/WorldEditing.cs` relays
  to players within 160 m, newest per builder at most every 100 ms, clears for viewers leaving range or when the
  builder leaves, and drops packets whose guid is not the sender's.

### Designs (templates): TemplateBase 0xF0 (u8 sub)

- **Save:** the Designs tab "+" (needs a screenshot first) sends VoxelConstruction sub 2 with action 3; the string
  after the material map's u64 is the name, the string near the end the tags, and the block list holds the voxels
  as for a copy. Answer **3 SaveTemplateResponse** `{bool ok, TemplateBasicData}`; the client adds the design.
- **2 EnvironmentListResponse** `{u32 n × TemplateBasicData, u64}` on zone-in.
- **TemplateBasicData** (reader FUN_143058180): `u64 guid, string name, u64, u64 author guid, u64 created,
  u64 acquired, u32 on marketplace, u32 designer template, 3 × u32 size, list<{u16 material, u32 n}>,
  list<PropData>, list<0x38-byte>, list<0x68-byte>, bool default, string tags, bool counts against storage,
  u32, u32, u32, bool own design (true → "Personal", false → "Collected"), string author name, u32 value,
  bool contest winner, u32`. Category (Designs window, FUN_1436966c0 and its list builder): marketplace ≠ 0 →
  Player Studio, else designer ≠ 0 → Starter Castle (`Template.Category.Daybreak`, text 576947), else the own-design
  bool picks Personal / Collected. A designer template's shown name is the text id in the first u32 after "counts
  against storage" (+0xfc); 0 shows "Daybreak Template (Invalid Name)". Designer templates never count against
  storage (the client computes that flag itself). The client's own lists: `Resources/ExportedTemplates.txt`,
  `ExportedTemplateCategory(Map).txt`.
- **0x1B ExportToItemRequest (C→S)** `{u64 guid}` from Lua `ExportToItem` (FUN_14352a680): make a "Player Design" item
  (2433) of a design (not built). The receiving side is **0x11 AddOwnershipResponse** (reader FUN_143058780).
- **4 VoxelDataRequest (C→S)** `{u64, u64 guid, bool}` when a design is picked and not cached → **5 VoxelDataResponse**
  `{u64, TemplateBasicData, block list (as saved), list<image>, bool}`. The client builds its paste clipboard from
  it; the paste request (sub 2 action 4) then carries the design guid in the u64 after the material map.
- **Screenshots:** after a save the client uploads each screenshot as MarketplaceImageBase 0x107 sub 1 (u16)
  `{u64 guid, u64 guid, string "images/templates", string "large"|"small", u32, u32 index, u32 length + JPEG}`. Tiles
  load them over HTTP from ClientConfig `MarketplaceAssets`:
  `/uploads/lm/images/templates[N]/{small|large}/{guid as 24 digits, "/" every 3}.jpg` (FUN_143ad69a0); the
  emulator serves these on port 19001 when MarketplaceAssets points there.
- **6 DeleteTemplateRequest** `{u64 guid, u8}` → **7** `{u64, u64 guid, bool ok, bool cannot delete}`.
- **0xA RequestRenameTemplate** `{u64 guid, string name}` → **0xC UpdateTemplateResponse** `{TemplateBasicData}`;
  **0xB** `{u32 error (1 not found, 2 not author, 3 immutable), u64 guid}` only on failure.

- **ToolBase 0x117 sub 2 UndoStackChange (S→C):** `{u32 undo count, u32 redo count, list<string> names}` enables
  the undo/redo buttons and fills the Tool.UndoStack list.
- **Heal tool** (VoxelEraserCursor) diffs against the client's base terrain layer; with the server's edits on
  that layer it finds nothing and sends nothing.

## 10. Claims (build sites): ClaimsBase 0xF9 (u8 sub)

S→C:

| Sub | Name | Layout |
|---|---|---|
| 1 | SendAllClaimBasicInfo | `u64, u32 n`, n × `{u64 id, u64 parent group (0 = root site), u64 owner, 3 × u32, 3 × f32 min, 3 × f32 max (local m), u32, bool, u32, u32}`. Root sites go into the client's placement-buffer index by their centre. |
| 4 | UpdateClaimsAOI | `bool full (clears all), list<ClaimRecord> adds, list<u64> removes` |
| 6 / 7 | PlaceClaimResponse / Complete | 6: `u32 result (1 ok)`. 7: `u32 result 2, record`. Refusal results pick the client's text: 3 invalid location, 4 insufficient upkeep, 6 no flag, 8 landscape full, 10 site limit, 11 attachment limit, 13 attachment must touch, 25 / 27 caves, 26 too close (buffer), 28 Pioneer, other → "Unable to place build site." |
| 8 | ClaimManagmentData | `u64 upkeep seconds left, list<{bool isGroup, u64 id, string name, bool overriding settings, u64 parent group, string category, u32, u32}>`. The last two u32s are server ids; a mismatch only appends a server name. |
| 9 | EditClaimItemData | `u64 id, bool, ClaimRecord` (applied only to the selected site) |
| 10 | UpkeepManagmentData | `u64 expiry (Unix s)`, root/attached owned, player max root/attached, max root/attached allowed, `u32 warning`, max upkeep days, 3 × u32 |
| 0x0F | SendClaimsByOwner | `u64 owner, u32 n`, gallery entries (not decoded; sent empty) |
| 0x11 | RecievePropExclusionArea | `u32, list` |
| 0x30 | DeleteClaimItemReply | `u64 id, u32 result`: 2 deleted; 0x12 must be on the same landscape; 0x13 delete attachments first; 0x14 site has a game object; other → "Unable to recover build site". |

C→S:

| Sub | Name | Server action |
|---|---|---|
| 0x19 | RequestAllClaimBasicInfo | Answer with `SendAllClaimBasicInfo` (sub 1). (Replying with the full AOI list instead made the client repeat 0x19/0x27/0x35 several times a second.) |
| 0x1A / 0x1B | PlaceClaimRequest / Confirm | 0x1A is `f64 centre (global), 4 × f32 flag point, u64`. |
| 0x1C | ClaimManagmentRequest | Answer with 8 + 10. |
| 0x1E | RequestEditClaimItemData | `{u64 claim id}` → 9. |
| 0x20 | RequestUpkeepManagmentData | → 10. |
| 0x27 | RequestClaimsByOwner | → 0x0F. |
| 0x2D | SetItemBasicPermissions | `{u64 id, u32 mask, u64, bool}` |
| 0x2E | SetItemIndividualPermission | `{u64 id, u8, string name, u8, u32 mask, bool}` |
| 0x2F | DeleteClaimItem | `{u64 id, bool, bool}` |
| 0x35 | RequestPropExclusionArea | → 0x11. |

ClaimRecord:
- `u64 claim id, u64 parent group (0 = a root site; non-zero files the site under that group), u64 owner guid`,
  `string name`, `u64`, 3 strings, `u32`;
- `3 × f64 min, 3 × f64 max` (global decimetres);
- `3 × u32 default permission masks`, `list<{u64 guid, u32 mask}>` players, the same for guilds, a third list;
- … (see `Claims.WriteRecord`).

Permission masks: No Touching 0, Play Around 0x003, Pillage & Plunder 0x007, Get Inspired 0x107, Let's Decorate
0x10F, Ultimate Power 0x11F.

- **0x5F UpdatePropCategoryCount (S→C):** `{u64 site, u32 n × {u32 category name text, u32 current, u32 max}}` (reader
  FUN_142d08c00; the client keeps it per site and fills ClaimDialogs.PropLimits for the site the player is on, which the
  Build Mode widget shows as "current/max Name"). Launch limit: Chests (text 573121) 50 per site.
- The client's claim dispatcher switches on sub − 1: catalogue entries keyed by the case value are one lower than the
  sub on the wire (AOIUpdate is sub 4, "UpdateClaimBasicInfoAndAOI" case 3).

## 11. Props: PropBase 0xED (u8 sub)

| Sub | Dir | Name / layout |
|---|---|---|
| 1 | C→S | PropAccess `{u64 prop, u64, u32}`: the prop tool picked a placed prop (FUN_142f15300); the client waits (+0x159) for sub 2 |
| 2 | S→C | PropData (opens the placement / edit session; needs the entity to exist first). Also the answer to PropAccess: the data when the player may move the prop (owner, or Let's Decorate on its site), else success false with error 10 (no permission) or 13 (Play Mode) |
| 3 | C→S | Save `{6 × vec4: position, rotation, scale, old position, …}` (repeated ~2/s while the gizmo is open) |
| 4 | S→C | SaveReply `{u32 0, u64 guid}` |
| 5 | C→S | PickUp `{u64 guid, u8}` |
| 6 | S→C | PickUp reply `{u64 player, bool ok, u32 error, u64 guid}` (errors: 3 inventory full, 6 invalid location, 10 no claim permission) |
| 7 | C→S | Delete `{u64}` |
| 8 | S→C | Delete reply (same layout as 6) |
| 0x0C | C→S | PropProcessorStateNotification `{u32 state, u64, u32}` (informational) |
| 0x12 | C→S | PlacePropById `{u32 prop id}` |
| 0x16 | C→S | SetTintRequest `{u32 n, n × u64 prop, u64 player, u32 alias hash}` (build tray tint tab; hash of the alias string, see 0x17) |
| 0x17 | S→C | AvailableTintsReply `{u64 player, bool ok (false adds u32 error, u64), u32 groups × {u32 key, u32 id, u32 name text, u32, u32, u32 n × u32 tint id}, u32 values × {u32 key = art tint id, u32 art tint id, u32 name text, u32 sort, u32 image set, string alias "GlobalTint_Coral", u32 alias hash}}` |
| 0x18 | C→S | AvailableTintsRequest (empty; sent when the tint tab opens) |

A tinted prop carries the alias in its spawn (lightweight base "tint alias" string, 4.2) and is updated live with
PlayerUpdate 0x7A SetPropTint `{u64 guid, string alias}`. The colours behind an alias come from the login server's
ArtData tunnel blob (also zone packet 0x116 sub 1); a client without it shows props untinted.

## 12. Crafting: RecipeBase 0x42 (u32 sub)

| Sub | Dir | Name |
|---|---|---|
| 1 | C→S | Start `{u32 recipe, u32 count, list<u64 item>}` |
| 2 | S→C | StartResponse `{bool, string}` |
| 5 | S→C | CompleteResponse `{bool, u32, string}` |
| 7 | S→C | CraftWindowEnable `{u32 result, u32 station, u64}` (0 opens the window) |
| 0x18 | C→S | SalvagingStart `{u32 recipe, u32 n × {u64 item, string, string, u32 icon, u32}}` |
| 0x19 | C→S | SalvagingAddItem `{u32 recipe, u32 slot, u64 item}` (Lua `SalvagingStationAddItem`) |
| 0x1A | S→C | SalvagingAddItemResponse `{bool ok, u32 recipe, u32 slot, u64 item}`; the slot only fills on ok |
| 0x1B | S→C | DefaultLearnedRecipes |
| 0x1C | C→S | ComponentImageSetIdsRequest (empty) |
| 0x1D | S→C | ComponentImageSetIdsResponse `{list<u32>}` |
| 0x1E | S→C | PlayerRecipeDataUpdate |
| 0x23 | C→S | PlayerOpenCraftingWindow `{u32 station}` |

Recipes come from the SendSelf recipe records. A record with an empty output-category list gets no row in the
window.

- **Components:** RecipeComponents.txt rows naming the recipe, plus rows listed for it in
  RecipeRecipeComponentMaps.txt (those rows have RECIPE_ID 0; every Lumen Station price is given this way).
  COMPONENT_TYPE 1 = an item, 4 = any item of that item category, 5 = any item whose category is in that
  crafting category group (CraftingCategoryGroupMapDefinitions.txt), 6/7 accepted by the client as is
  (client check FUN_142e9d0e0). Types 4-7 only occur in salvaging recipes.
- **Stations:** CraftingStations.txt TYPE 1 generic, 5 salvaging (41, the Magical Monument: equipment → Lumens),
  6 repair (56/57, priced in Lumens; durability isn't sent, so items never wear). Station 55 is the Lumen Station
  (prop 2565, whose name text clashes with the salvager's).

## 12b. Chests: ChestBase 0xFF (u32 sub)

Client dispatcher FUN_142dad120; all readers re-read the u16 opcode and u32 sub from the packet start.

| Sub | Dir | Layout / notes |
|---|---|---|
| 1 EnableWindow | S→C | `{u64 chest}`. The client records the chest and sends 3 (what a click on a chest leads to). |
| 2 StopObservingChest | C→S | The window closed. |
| 3 InventoryListRequest | C→S | `{u64 chest}` (Lua `OpenChest`). |
| 4 InventoryListResponse | S→C | `{u32 result (1 ok; 4 → "Chest.ClaimAccessDenied"), u64 owner, u64 chest, u64 inventory, u32 title text id, u32 slots, bool unsolicited, u32 len, items}`. Items = `u32 n`, per item `bool definition follows` + item record. Opens `DisplayChestWindow`. |
| 5 AddItem | S→C | `{u64 chest, u64 inventory, u32 len, bool + item record}` |
| 6 DeleteItem | S→C | `{u64 chest, u64 inventory, u64 item}` |
| 7 UpdateItem | S→C | `{u64 chest, u64 inventory, u64 item, u32 count, u32, u32, u64, u32 slot}` |

- The client keys its chest view by the inventory guid; the server uses the chest prop's guid for both.
- Chest items have owner type 3 (`OwnerType`), `InventoryId` = `ChestOwnerGuid` = the chest, `ChestSlot` = slot. The
  client refuses an item whose stack it can't place ("HandleAddItem--Failed to add item", Logs/ClientItemsError.log).
- Moving items: InventoryBase TransferItem with owner type 3 (+ u64 owner) on the chest side. Errors the client
  explains: 0x16/0x17 access denied, 0x20/0x21 no-trade item, 0x12 fraud timer, 0x25 chest with items into a chest.
- Sizes come from the server (the client's Chests.txt has sizes 10-35 by id but nothing links props to them).

## 13. Social

- **Chat (0x13 sub 1):**
  - Layout: `u16, u16 channel, u64 from, u64 to`, two name blocks
    `{u32, u32, u32, string first, string last}`, `string message`, `vec4 position`, trailing fields.
  - The first u16 is a kind: 0 a message; 2 "User '[target]' not found." and 3 "User '[target]' is offline." (both
    about the second name block) — FUN_1432b7bc0 case 1.
  - Channels (the client's format table FUN_143aaf8f0): 0 say (relayed within 100 m), 1 whisper / tell ("[X]
    whispers:", only to the named player; the receiver's client stores the sender for /reply), 2 whisper echo, 5 guild,
    8 party, 10 player chat room (the room name trails the packet).
  - **TellEcho (sub 5, S→C):** `{u64 target, name block, string message, bool}` (FUN_1432aa700 / FUN_1432c0f90): the
    sender's own "To [X]: …" line.
- **FriendBase 0x4D (u8 sub):** S→C 1 List, 2 Online `{string, u64, bool}`, 3 Offline `{u64}`, 6 Add, 7 Remove.
  - FriendData: `{name block, u64 guid, bool online, …}`.
  - Requests are Command 0x1E subs: 0x10 AddFriend `{string name, u64 guid}` and 0x12 RemoveFriend.
- **GroupsBase 0x2B (u8 sub, then u32 result, u32):**
  - S→C Invite (sub 1, result 1) shows the popup. The invite inside is `{u64 key, u32 raid flag, inviter player,
    invitee player, u32}`, player = `{u64 guid, name block, u8}`. The client answers only if the key is non-zero,
    both guids are set and each player's last byte is 1-3 (the server sends key = inviter guid, bytes 1).
  - C→S Join (sub 2) is the popup's answer: `u32 1, u32 0, u32 response (1 accept, 2 decline), u32, invite`.
  - The client also sends **Notifications** (bytes `01 00`) sub 3 UpdateNotification `{u64 id, u32 state}`. State 4
    only means "popup entry finished" (sent after Accept, Decline or Remove), so it is not an answer.
  - S→C: AddGroup 0x11, AddMember 0x12, RemoveMember 0x16, RemoveGroup 0x15, Kick 5, Disband 6, SetOwner 8.
  - Invite (sub 1) `{u8 sub, u32 type, u32 code, invite}`: type 1 = an invitation (the popup), 4 = remove, anything else
    = a result line for the inviter with the client's own text for `code` (FUN_143b5f4a0: 0 invited, 5 party full, 9 not
    found, 13 already invited, 14 already in a party, 16 declined; [*source*] / [*target*] from the invite's names).
  - RemoveInvitation (sub 0x17) `{u8 sub, u32, u32 code, invite}` (FUN_142d4ea30, texts FUN_143b5f570): 3 "Your party
    invitation to [target] has expired.", 4 "… from [source] has expired." (also drops the invitee's popup when the key
    matches). Join results use FUN_143b5f4d0's table (0 success … 13 inviter group full).
- **GuildBase 0x83 (u16 sub):**
  - C→S: 1 Create, 4 Invite, 5 InviteReply `{u64, u8}`, 0x0B Motd, 0x0D Quit, 0x0E UpdateRank (the whole edited
    rank `{u32 level, string name, u32, string, u32, u32 n × permission, u32, u32, u32 n × {…}, string, u32 rank id
    (0 = new)}` + `u32 action` 1 set / 2 remove), 0x0F DescriptionUpdate `{u64 guild, 3 strings}` (text in the
    non-empty one), 0x11 UpdateAppDataField `{u64 guild, string GuildGameData XML}` (GuildKeywords = the tag).
  - Guild core strings: Description is the 6th string after the name (+0x108); the string after the leader id is the
    app data XML (+0x168). Re-sending GuildData resets the client's rank definitions: send the ranks after it.
  - S→C: 0x13 GuildData (member id = guid as a decimal *string*), 0x0E UpdateRank (permissions by name),
    0x15 AddMember, 0x16 RemoveMember, 0x18 Online, 2 Disband.
- **MailBase 0xF8 (u32 sub):**

| Sub | Dir | Layout |
|---|---|---|
| 6 | C→S | InboxListRequest `{u16 flags, u64 mailbox guid}` (the Open Inbox button sends guid 0) |
| 7 | S→C | InboxListResponse `{u32 list type, u64 mailbox guid, u16 flags, u32 n, n × 0x1D8-byte MailData}`. Flags bit 0 = request complete: it clears the client's pending flag. The client then calls Lua `OpenEmailInbox`. |
| 0x13 | S→C | MailEnableWindow `{u64 mailbox}`: the client requests the list and opens the inbox. A mailbox would send this. **Don't send it at login:** the inbox opens unseen and the button stops working. |

## 14. Other systems

- **TemplateBase 0xF0 (designs, u8 sub):**
  - 2 EnvironmentListResponse `{u32 n, n × 0x130-byte TemplateBasicData, u64}`. Send it (empty) at zone-in, or
    the Designs window shows a placeholder row and "0/0" storage.
  - Others: 3 save, 7 delete, 0x0B rename, 0x0C update, 0x11 ownership.
- **UiBase 0x32 sub 0x17 (C→S):** UIEventTriggered `{u32 event id}` when a window opens (0x1A inventory, 0x18
  journal, 0x1C inbox, …). No reply.
- **InGamePurchase:** 0x0B WalletInfoRequest → 0x0C `{u32 1, u8, u32, u32, string, string, u8}`.
- **TeleportBase 0xFC (u8 sub):** 2 RequestLocations → 3 Locations `{u32 n, n × world location, u32 k, k × entry}`
  (reader FUN_1430c4150; opens the Landscapes window), 4 Request, 8 IntraZone, 10 Managed,
  0x0B RandomUnderground.
  - World location: `{u32 world id, u32 name text, u32 capacity %, u32 n, n × zone}`. The world id must be the
    client's current world (the one SendZoneDetails gave it, 2 here) or the window stays empty.
  - Zone: 7 × u32 `{zone id, name text, biome type (0 = shown under every filter), site count, capacity (f32 bits),
    tier, image id}`.
  - 4 TeleportRequest (C→S) `{u32 world, u32 zone}`: the server re-zones the player (`Game/Travel.cs`).
  - 8 TeleportIntraZoneRequest (C→S, FUN_1430c8260) `{f32 x, y, z, w, u64 target, u8}`: the site game editor's Go To;
    served only to a player editing a game, to a point on that game's site.
- **ZoneSafeTeleportRequest 0x7C (C→S, no body):** Lua EvacLocalPlayerToSafepoint (player outside the zone's bounds)
  and TeleportToSafePoint. The server moves the player to the nearest respawn site (at most every 10 s).
- **Player reports (C→S, u16 base + u16 sub 1):** BugSubmission 0x55 AddBug and ClaimReportSubmission 0x56 AddReport
  `{u32, string, u32, string XML}` (header writer FUN_14306db00). The XML is the client's TinyXML document
  (`<BugSubmission Category BugDescription><PlayerData …/>` / `<ClaimReport ReportDescription>`); the client shows its
  own "submitted" message. Saved to Reports/bugs and Reports/sites (`Game/Reports.cs`).
- **Informational client packets** (no reply): ClientMetrics, FotomatBase, SetLocale, RatingBase, PlayerUpdate
  Loading, ClientPcDataBase, ClientInitializationDetails.
- **LootPinataBase 0xFB (u8 sub):**
  - 1 SpawnLoot: `u32 id, u32 definition (LootPinata.txt: 14 dirt, 19 sand, 21 stone…), vec3 position, u32 item,
    u32, u32 count, f32, u64 owner, string, u32 kind, u32 ×3`. Kind 0 keeps the position; kind 3 zeroes it. Before
    scooping a pinata up, the client checks that the bag or the resource (Collections) container can take item × count
    (FUN_143079860), and otherwise shows "Your inventory is full!".
  - 5 PickupLoot (C→S) → 3 PickupLootResponse `{u32 id, u64 player, u8}`. Chunks fly to their owner.
- **HudManagerBase 0xDC (u8 sub), right-click menus:** the client asks the server which buttons to show.
  - 1 RadialMenuRequest (C→S) `{u64 target guid, u32 request type}`. Types: 1 prop, 2 friend (Friends window),
    3 player in the world, 4 other social row, 5 party member, 6 guild member, 7 chat tab.
  - 2 RadialMenuResponse (S→C) `{u64 target guid (must match), u32 menu type, u32 n, n × {u32 sort order,
    u32 button id, u8 selectable}, u32 request type}`. Button ids are rows of the client's
    `Resources/RadialMenuButtons.txt`, which gives each button's label and Lua call (e.g. 23 `Ui.TeleportToFriend`).
- **Travel abilities** (`StringHashToValue.txt`): 185 Teleport.HubAbilityId (Spire), 186 OwnClaimAbilityId (own
  site), 187 FriendAbilityId (friend; the cast's target is the friend's guid). The client never requests caster info
  for 187 before "Teleport to Friend" casts it, so the server pushes 185-187 at zone-in.
- **EncounterBase 0x2C:** death window: 0x7D shows "You have died.", 0x7E hides it.
- **ActionManager 0x9A:**
  - 1 ClearSlot.
  - 2 (right-click a bag tool) and 5 UpdateSlot share the layout `{u32 bar, u32 slot, u32 type (0 empty,
    1 item definition, 2 item guid), u64 value}`.
  - Hotbars are sized only by the SendSelf hotbar list; a bar sent with 0 slots rejects every update.
- **Server commands (CommandBase 0x1E):**
  - 0x44 (S→C) dynamic command list `{u32 n, n × string name, u32 source (1 = zone)}`: each becomes a "/name" the
    client accepts (DynamicCommands.log); any other unknown "/name" gets "Command not supported".
  - 0x43 (C→S) ClientCommandPacket `{string name, u32 n × string argument}` when one is typed.
- **Emotes:** played with Abilities 0x27 sub 0x12 PlayAnim on the player's guid. AnimationSelection.txt
  "EmoteSingle" rows select a clip through Emote_Index (AnimationSelectionVariables.txt); "EmoteLoop" rows repeat
  until selection 253; stance rows (Stance_Index) persist until 223. A selection that arrives while the character
  is moving is ignored.
- **HeroicMovement 0x0105 (u8 sub):** `{u8 sub, u64 entity, body}`: 1 FireGrapplingHook, 2 SetHeroicMovement,
  3 SwimmingUpdate, 4 TerminateGrappleHookEffects. The pull is client physics (stats 18-21); the server relays the
  packets to nearby players with the entity set to the sender (the client ignores ones about itself).
- **ToolBase 0x0117 sub 1 ToolPacketActivate (C→S):** build tool switched; informational.
- **Mail (MailBase 0xF8, u32 sub):**
  - C→S: 1 Compose `{u64, u32 n × PlayerName to, u32 m × PlayerName, string subject, string body, u32 k ×
    attachment}`, 6 InboxListRequest, 8 ReadMessage `{u64 id}`, 10 Delete `{u64 id}`, 0xF ConsumeAttachment
    `{u64 mail, u64 attachment (0 = all), u8}`, 0x14 AttachItemToCompose `{u32 type 1, u64, u32 bag slot, u32 count,
    u32 container, bool}`.
  - S→C: 4 ComposeResponse `{u32 result (+ PlayerName for 1 / 0xD)}`, 7 InboxListResponse `{u32 0, u64 owner,
    u16 flags (bit 0 = complete), u32 n × MailData}`, 9 ReadMessageResponse `{u64 id, u32 result, u32 kind}` + on
    success `{u64 sender, PlayerName, string subject, string body, u64 sent, u32, u32 n × attachment, u32 n + n
    bytes}`, 0xB DeleteResponse `{u64 id, u32 result}`, 0xE NewMail (empty), 0x10 ConsumeAttachmentResponse
    `{u64 mail, u64 attachment, u32 result}` (one per attachment), 0x13 EnableWindow `{u64}`, 0x15
    AttachItemResponse `{u32 result, request echoed}`.
  - PlayerName `{u32, u32, u32, string first, string last}`. MailData `{u64 id, u64, PlayerName, subject, body,
    u64 sent (Unix s), u32 read, u16 attachments, u32 kind (0 player)}`.
  - Attachment `{u64 id, u32 type, …}`: 1 `{u64 item, u32 count, u32, u32}`, 2 = the client item record
    (FUN_142b87110: the inventory record without the 0.695 trailing byte), 3 currency `{u32, u32}`, 4 `u64 list`.
  - Result codes: client string table at 0x14521a980 (0 Success, 1 unable to find user, 2 attachment to self,
    5 attachment invalid, 7 item no longer exists, 0xB attachments to several people, 0xC too many attachments,
    0xD inbox full, 0xE sent too many, 0x14 message not found, 0x1A attachment not found, 0x1C inventory full…).

### 14.0 Spire Local Travel: WarpNetwork 0x113 (opcode bytes 13 01, u32 sub)

The Spire menu's "Local Travel" (Lua `Teleport:ActivateLocalTravelMap`) switches the camera to ControllerLocalTravel
(mode 0x1E): a top-down map with the hint "Click a HUB or Portal Crystal destination". Server: `Game/WarpNetwork.cs`.

- **Request (C→S) sub 4:** `{u32 type, u32 zone, u32 world, u64 from, u64 to}`.
  - Type 1 is sent once, at the first ClientFinishedLoading; the server re-sends the network on every landscape
    change.
  - Type 2 is a warp: Teleport.TravelConfirmation or Lua WarpToNode.
- **Network (S→C) sub 2:** `{u32 world, u32 zone, u32 n layers × {u32 id, u32 n × {u64 key, u64 node, u32 type,
  u32 biome, u64 cell, f32 x, y, z (local m), u32}, u32 id}, u32}`.
  - The client files it by (world, zone) and looks it up by its own (game +0x378cc, +0x378d0).
  - Node ids are entity guids: the map picks the entity under the cursor.
  - Type 1 = hub, type 2 = portal crystal.
  - The map shows one layer at a time: the cave tier where the player stands (0 on the surface), changed with
    `Teleport.SetCurrentLayer`. While the map is open, every node of that layer gets a HUD marker.
- **TeleportBase 0xFC sub 1 ActivateTerminal (S→C):** `{bool open map, u64 node}` tells the client the player stands
  at that node.
  - Open = the map at once.
  - Otherwise the client asks for the travel locations (the Spire's Landscapes window).
- **Cost:** BaseCost + NodeCost, plus NodeCost again for a crystal, in Lumens (1579).
  - The data's keys "NodeCost " and "ZoneCost " end in a space, so the client uses its defaults (1 and 5).
  - A trip costs 1 Lumen to the hub and 2 to a crystal.
- **Nodes (0.589):**
  - the Spire's "Portal Spire (Hub)" prop;
  - the home continent's 81 cave Graveyard01 pieces (tile 111, tier 1 from VoxelLayerPropertyGroups), each with an
    "Underground Portal Crystal" prop (1881).

### 14.1 Client requests left unanswered on purpose (checked 2026-10-12)

The server counts every received opcode (`opcode_stats.tsv`), and every request that falls through to a handler's
default branch (`OpcodeStats.Unanswered`); `re/tools/coverage.py` compares those counts with the client's packet
classes and lists the default-branch hits as the runtime truth. Some requests are only logged or dropped, by design:

| Request | When | Why there is no answer |
|---|---|---|
| InGamePurchase 0x15 CountryCodesRequest, 0x0F Station Cash price list, 0x36 CommerceRequestAccountStore | Store start-up (`RequestStartupDataInternal`, ClientInGamePurchase.log), each login | The real-money Daybreak Cash store; the store is free and in the Showcase instead. 0x1A AccountInfo and 0x3F GetProductForReview are answered now for Player Studio (docs/commerce.md); the AccountInfo reply makes the client ask for the Station Cash price list (0x0F), which stays unanswered, so the cash store stays unavailable. 0x36 (the player's own products) is still open. |
| InGamePurchase 0x46 type 2 (crafting Point-of-Need) | Crafting window "buy missing" | Opens a store URL in the client; no server reply known. |
| Notifications 0x01 sub 2 AddNotification | The client queued an entry itself | Informational. |
| Notifications 0x01 sub 1 ListNotifications | UI shutdown (logout / exit): the unanswered queue entries `{u32 n, n × Data}` | The server keeps invitations itself and resends a still-valid guild invitation at the next login (Guilds.Enter). |
| ToolBase 0x117 sub 1, PlayerUpdate 0x6C, SocialShare 0x104 | Tool switch, loading screen, sharing | Informational; no reply expected. (WorldEditing 0x10C used to be listed here; it is relayed to other players, §9.) |
| ClaimsBase 0x19 | See §10 | Answered with SendAllClaimBasicInfo (sub 1); never with the AOI list. |

## Achievements (AchievementBase 0x92, u8 sub)

Client dispatcher `FUN_142d92750`. Subs 4/6/7/8 are rejected when bytes are left over.

| Sub | Name | Body |
|---|---|---|
| 2 | Add | one achievement record |
| 3 | Initialize | `u32 n`, n x achievement record |
| 4 | Complete | `u32 achievement` |
| 5 | ObjectiveAdded | `u32 achievement`, objective record |
| 6 | ObjectiveActivated | `u32 ach, u32 obj, u32 current, u32 required, u32 progress, bool` |
| 7 | ObjectiveUpdate | `u32 ach, u32 obj, u32 current, u32 progress` |
| 8 | ObjectiveComplete | `u32 ach, u32 obj, u32 progress, u32 message, bool` |
| 0x0B | TrackerInitialize | `u32 n`, n x `u32 achievement` |
| 0x0C | TrackerUpdate | |
| 0x0E | SecretComplete | one achievement record |
| 0x0F | GlobalAnnounce | |

`progress` is the achievement's completion as float bits (achievement +0x20).

**Achievement record** (`FUN_142b7ca00`): `u32 id, u8, u32 name text, u32 description text, u64 started (Unix s),
u64 completed (Unix s, 0 = not), u32 progress (float)`, reward bundle, `u32 n` x {`u32 key`, objective}, `5 x u32,
i8, u32, bool, 10 x u32, 3 x bool`, `u32 n` x u32, `u32 n` x {`u32 key, u32 m`, m x 16 bytes} (item hints),
`u32 n` x {u32, u32}, `u32`. The 5 x u32 include the subcategory (5th, AchievementSubcategories.txt).

**Objective** (`FUN_142b89710`): `u32 id, u32 text, u32`, reward bundle, `u32 state` (0 not started, 1 active, 2
done; client +0x2150), `u32 current` (+0x2154), `u32 required` (+0x2158), `u32 sort, u32, u32, 4 x u32, u8, u32`.

**Reward bundle** (`FUN_142b95550`): `bool full, u32`, `u32 n` x {u32, u32}, `3 x u32, u64, 5 x u32, u64, u64,
2 x u32`, `u32 n` x {`u32 type`, entry}, `u32`. Entries (factory `FUN_143ac8420`, reader = vtable +0x78) start with
`bool, u32 icon, u32, u32 name text, u32 count, u32 item id, u32, string, u32, bool, u32, u32`; types 6, 7, 8, 10,
0x0E, 0x16, 0x19 (QuestAdd, ProfileAdd, AbilityLine, CollectionAdd, Recipe, AbilityEntry, PropRecipe) add one u32.
Type 1 (Item) adds an item definition only when `full` is set. `re/tools/decode_achievements.py` decodes a capture.
