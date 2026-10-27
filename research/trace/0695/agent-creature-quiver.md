# Creature bow + quiver (0xCB/2 outer vs attach slot)

**Verdict: (A)** — send bow `{outer=2, att=2, bool=0}` then quiver `{outer=2, att=11, bool=1}`. Requires splitting outer `EquipSlotId` from attachment slot (+0x188) and writing the trailing bool in `EquipmentSlotMessages` (today both slots are the same u32 and bool is hard `false`).

Capture (Courage) already matches (A); player shield is (B)-shaped (`outer=11,att=11,bool=1`) and is not what live uses for NPC quivers.

## Clear / attach / bool map

| Role | Field | Evidence |
|------|-------|----------|
| **Clear key** | **Outer** `EquipSlotId` | `HandleEquipmentSetCharacterEquipmentSlot` `FUN_142e42da0`: `(vtbl+0x498)(entity, local_258, 1, 0)` with `local_258` = outer from reader. Impl `ClearCharacterEquipmentSlotAttachments` `FUN_142ebb340`. |
| Clear hash blank | Outer, optionally remapped | In `FUN_142ebb340`, if arg2≠0 look up `EquipmentSlotDefinitions` (`DAT_1454313d0`); if **def+0x54** (`CUSTOMIZATION_SLOT_OVERRIDE`) > 0, hash-clear uses that instead of outer. Main Hand / Off Hand rows have override **0** → clear-hash key = outer. Walks entity buckets `+0xee`, key `+0x1b8` (same index space as attach `+0x188`). |
| Clear side effect | `entity+0x280` vtbl `+0x10(outer)` | Always after optional hash blank. Attachment **group manager**, not `FUN_143b97060`. |
| **Attach key** | Record **`+0x188` only** | `ApplyCharacterAttachmentBySlot` `FUN_142ebd620`: `uVar1 = *(uint*)(rec+0x188)`; bucket `this[(uVar1&3)+0xee]`, match `+0x1b8`. |
| **Trailing bool `+400` / `0x190`** | Replace-if-occupied on **attach slot** | Read by `ReadCharacterAttachmentData` `FUN_142c185c0`. Passed through `FUN_142ebd620` → entity vtbl `+0x340` (`FUN_142ebd740`) → `FUN_143b95320` → `AddOrReplaceModelAttachmentBySlot` `FUN_143b95650`: if an entry already exists at that attach slot and **bool≠0**, call `FUN_143b97060` (remove) then insert. Bool **0** skips that remove. Same bool flows on full-NPC apply `FUN_142e9a050` → `FUN_142ebd620`. No decompile name beyond that behavior (weapon=0, quiver/shield=1 in capture). |
| Outer when ≠ `+0x188` | Validation + profile bookkeeping | Gate `outer > DAT_1455fe56c` (**0**). Then `FUN_142ea49e0(entity, outer, &itemGuid)` — returns immediately if `entity+0xa68==0` (**creatures**). Not the mesh attach key. |

### Why outer=2 on quiver does not drop the bow (static + capture)

Clear is invoked with **outer 2**, so it blanks the entity hash entry for key 2 and pokes `+0x280`. Model-actor mesh teardown by attach slot (`FUN_143b97060`) is **not** on the clear path; it only runs when a later attach targets the **same** `+0x188` with **bool=1**. Quiver attaches at **11**, so the bow’s model-actor entry at attach slot **2** is left alone. Capture shows both; recipe (A) matches that.

## Q answers (ordered)

1. **Clear key:** outer (not `+0x188`). Remap only via def+0x54 when set; MH/OH = 0. Both still show because attach/mesh replace is by `+0x188` (see above).
2. **Attach key:** confirmed — `FUN_142ebd620` reads only `*(rec+0x188)` for the hash index.
3. **Bool +400:** replace-existing at that attach slot (`FUN_143b95650` / `FUN_143b97060`). Path: Set handler → `FUN_142ebd620` → `+0x340`; full-NPC `FUN_142e9a050` same apply helper.
4. **Outer ≠ attach:** validate `> DAT_1455fe56c`; `FUN_142ea49e0` item↔slot bookkeeping (skipped for creatures). Not ignored for non-player guids on the handler entry — same `FUN_142cd1cb0` path — but bookkeeping is a no-op without `+0xa68`.
5. **Server recipe:** **(A)**; yes, split outer vs attachment slot + set bool.
6. **Creature shields:** Same `FUN_142e42da0` / `FUN_142ebd620` for any guid from `FUN_142cd1cb0` — no player-only gate on Off Hand (11). Code allows NPC `outer=11,att=11,bool=1` (player-shield style) or quiver-style `outer=2,att=11,bool=1`; no NPC shield packets in the cited dump.

## Hand-offs

| Owner | Action |
|-------|--------|
| **Emu / WorldServer** | Extend `EquipmentSetCharacterEquipmentSlot` writer: separate attachment slot + bool; `CreatureWeapons` send quiver after bow as (A) with `Wield_Quiver_Halas_Halas_B_A3_000.adr`. |
| **Frida** (optional) | Only if you need live proof that `+0x280.+0x10(2)` does not call `FUN_143b97060(2)` on the quiver packet — anchors above. |
| **Out of scope** | Full creature→weapon table; loot; player equipment UI. |

## Ghidra renames (this pass)

| VA | Name |
|----|------|
| `0x142e42da0` | `HandleEquipmentSetCharacterEquipmentSlot` |
| `0x142ebb340` | `ClearCharacterEquipmentSlotAttachments` |
| `0x142ebd620` | `ApplyCharacterAttachmentBySlot` |
| `0x142c185c0` | `ReadCharacterAttachmentData` |
| `0x143b95650` | `AddOrReplaceModelAttachmentBySlot` |
