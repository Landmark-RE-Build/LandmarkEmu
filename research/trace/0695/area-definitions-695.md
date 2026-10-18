# Area definitions (family 0xEA) and the Area Tool — 0.695 trace

TASKS 2.33 (Linking Area shape). Sources: `re/decomp_all_695` (grep `// ===== FUN_xxx`), `re/out/rtti_695.tsv`,
`re/out/luabind_695.tsv`, the 0.695 UI Lua (`<0.695 test copy>\UI\Default\AreaCreation.lua`), `trace/client-constants.md`.
**[P]** = proven from client code, **[I]** = inferred (no live capture of this family exists).

## 1. Summary

- A **Linking Area** (prop 1590) made with the **Area Tool** (ToolTypes row 4 "TriggerVolume", client class
  `ToolTriggerVolume`) is two linked things: an **AreaDefinition** (trigger volume, family 0xEA) that holds a box, and a
  **prop 1590** whose lightweight spawn carries the area's guid. Links use the **prop guid** (the prop is what the player
  clicks, links and edits); the area guid only ties the prop to its volume. [P] for the prop ↔ area tie (§4), [I] that the
  server creates the prop (§5).
- The box is the prop's own transform: centre = prop position, size per axis = prop **scale** (a unit-cube model),
  rotation = prop rotation. The tool edits an existing area by moving / scaling that prop and saving it with an ordinary
  **PropSave** (non-uniform scale). [P] (FUN_143739860 / FUN_143739440, §4.3).
- Every 0xEA request that sets the client's "busy" flag (subs 2, 3, 4) must be answered with **RequestResponse (6)**;
  until then the client sends no other area change. [P] (§3.5).
- The old AreaCreation window (Group / Ungroup / SetName / properties) is switched off in 0.695 Lua ("PLA Disabling the
  deprecated client area editing UI for now"; only `/areacreationshow` for Admin). Its natives still exist under the Lua
  table `Area` and can be called with `luacode` (§7). [P]

## 2. Records (wire order = reader order)

All integers little-endian; `string` = u32 length + bytes.

### 2.1 AreaDefinition (0x88 bytes in memory; reader FUN_1432f76a0, ctor FUN_143db7d80)
| order | type | struct | meaning |
|---|---|---|---|
| 1 | u32 | +0x08 | static definition id (data-table areas, AreaProperties.txt AREA_DEFINITION_ID); 0 for player / server areas. Shape matching in FUN_143db8320 switches on it [I] |
| 2 | u64 | +0x10 | **area guid** (StrongType `SoeUtilSignatureAreaGuid`; 0 = none: `DAT_14548b770` is zero-init bss) [P] |
| 3 | string | +0x18 | name (the tool's local copy uses text `UI.DefaultTriggerVolumeName` 557902) [P] |
| 4 | u32 | +0x30 | ? (ctor 0) |
| 5 | bool | +0x34 | ? (ctor argument; 0 from the tool) |
| 6 | bool | +0x35 | ? (ctor 1) |
| 7 | u32 | +0x38 | ? (ctor 0) |
| 8 | array | +0x50 | `Array<AreaShapeDefinition>`: u32 count + count × §2.2 |
| 9 | array | +0x68 | `Array<AreaPropertyDefinition>`: u32 count + count × §2.3 |
| 10-13 | 4 × u32 | +0x3c +0x40 +0x44 +0x48 | ? (ctor 0) |

### 2.2 AreaShapeDefinition (0x68; reader FUN_1432f7e40, writer FUN_14319e570, ctor FUN_143db5d10)
| order | type | struct | meaning |
|---|---|---|---|
| 1 | u32 | +0x08 | ? (0) |
| 2 | u32 | +0x0c | ? (0) |
| 3 | u64 | +0x10 | **shape guid** (TransferShape and Area.Remove find a shape by it; FUN_143db8a90) [P] |
| 4 | u32 | +0x18 | shape type: **2 = box** (AreaShapeDataBox::type FUN_143de1100), **1 = sphere** (FUN_143de1110) [P] |
| 5 | 3 × f64 | +0x20 | box **min** x, y, z [P] (FUN_143db7440) |
| 6 | 3 × f64 | +0x38 | box **max** x, y, z [P] |
| 7 | 3 × f32 | +0x50 | rotation (radians). The tool fills it with FUN_142e550d0's Euler split of the widget matrix: +0x50 pitch (asin −r2.y), +0x54 yaw (atan2 r2.x, r2.z), +0x58 roll (atan2 r0.y, r1.y) [I: order of the decompiled out-params] |
| 8 | bool | +0x5c | ? (ctor 1) |
| 9 | u32 | +0x60 | coordinate frame: 0 = min/max are client-local floats (what the tool sends, FUN_143de12b0 sets 0); non-zero = absolute doubles relative to an origin × 0.1 (FUN_143de0a60 else-branch) [P] |

Box test (AreaShapeDataBox FUN_143de11f0): min ≤ p ≤ max on every axis, no rotation [P]. Valid box (FUN_143de1150):
max − min > 0 on every axis [P] — the server rejects others with code 2.

### 2.3 AreaPropertyDefinition (0x120; reader FUN_1432f7bb0, ctor FUN_143dc24a0)
| order | type | struct | meaning |
|---|---|---|---|
| 1 | u32 | +0x08 | id (AreaProperties.txt ID) [I] |
| 2 | u32 | +0x0c | area definition id (AREA_DEFINITION_ID) [I] |
| 3 | u32 | +0x10 | **property type** (TYPE_ID; data source `Design.TriggerVolumePropertyTypes.<name>` FUN_1431ae820; Blank = 19, FUN_143db3530) [P] |
| 4 | u32 | +0x14 | property index (PROPERTY_INDEX) [I] |
| 5 | 9 × i32 | +0x18 | int params ("Int" Param n → +0x18 + 4n, FUN_1431ae820) [P] |
| 6 | 2 × u64 | +0x40 | two guids [I] |
| 7 | 9 × f32 | +0x50 | float params ("Float" → +0x50 + 4n) [P] |
| 8 | 3 × string | +0x78 | string params ("String" → (n + 5) × 0x18) [P] |
| 9 | 3 × string | +0xc0 | editable property names (EDITABLE_PROPERTY1-3) [I] |

### 2.4 Header (BaseAreaDefinitionPacket, FUN_1432f7fd0)
u16 opcode 0xEA (+0x08), **u32 sub** (+0x10), u64 data set (+0x18; the client always sends 0 = `DAT_14545f4d8`, bss),
bool (+0x20; 0). In the server's numbering the enum `0xEA00xx00` covers the u16 and the low half of the sub, so every
body starts with the sub's high half (`00 00`), then the u64 and the bool.

## 3. Messages

| sub | dir | name | body after the header | client side |
|---|---|---|---|---|
| 1 | S→C | SetDefinitions | Array<AreaDefinition> | FUN_143d5ff60: **clears** every area, then adds these [P] |
| 2 | C→S | TransferShape | AreaDefinition A, AreaDefinition B, u64 shape guid | built by FUN_1432fa4c0 (Lua Area.Group / Area.Ungroup); sets busy flag [P] |
| 2 | S→C | TransferShape | same | FUN_143d600f0: moves shape from B to A, re-adds A, re-adds B or removes it when empty [P] |
| 3 | C→S | UpdateDefinition | AreaDefinition, bool | FUN_1432fa5e0 (Area.SetName / AddProperty / RemoveProperty / SetPropertyValue(s) / Rotate; Area.Remove of one of several shapes); sets busy flag; bool 0 [P] |
| 3 | S→C | UpdateDefinition | AreaDefinition, bool apply | FUN_143d60310: when bool, replaces (by guid) or adds (FUN_143d608d0) [P] |
| 4 | C→S | RemoveDefinition | AreaDefinition (only the guid matters) | FUN_1432fa3f0 (Area.Remove of the last shape); sets busy flag [P] |
| 4 | S→C | RemoveDefinition | AreaDefinition | FUN_143d5ff40: removes the area with that guid [P] |
| 5 | — | SaveUndoDetails | — | no reader / builder in 0.695 (enum only) |
| 6 | S→C | RequestResponse | u64 (+0x28, area guid [I]), bool success (+0x30), u32 error (+0x34) | FUN_1432f8e60: clears the busy flag; on failure error 1 → "We have failed to update your area since the desired DataSet could not be found on the zone!", 2 → "We have failed to save your trigger volume due to invalid values!", other → nothing shown [P] |
| 7 | C→S | CreateTriggerAreaRequest | AreaShapeDefinition, i32 pending request id | FUN_1431a93e0 from the Area Tool (FUN_143739100 / Lua Area.Create); the id counts up per client run; no busy flag [P] |
| 8 | S→C | CreateTriggerAreaResponse | bool success (+0x28), u32 request id (+0x2c), u64 area guid (+0x30) | FUN_1431b0a70 → the tool's callback (lambda f7dd99c, FUN_14373a160) [P] |

### 3.5 The busy flag [P]
FUN_1432fa4c0 / fa5e0 / fa3f0 only send when `mgr+0xfd0` is clear, then set it (+0xfd4 = sub, +0xfd8 = area guid). Only
RequestResponse (6) clears it. A server that never answers blocks every later area change of that client.

## 4. The Area Tool (`ToolTriggerVolume`, 0.695 ctor FUN_143737720; same class in 0.589 at 0x14367bf80)

States (vtable 0x1447df2e0..): Inactive, Idle, CreatingVolumeSelection, EditingVolumeSelection,
WaitingForAreaCreationResponse, EditingArea, WaitingForAreaUpdateResponse. Deactivating while editing shows
`Tool.AreaEditingDeactivationWarning` for `Tools.AreaEditing.DeactivationWarningMessageDuration` seconds.

### 4.1 Create (FUN_143739100, Lua `Area.Create()` → FUN_1435d6bf0 finds the tool "TriggerVolume") [P]
1. Box = the selection widget's local bounds + its position (FUN_1436edfc0), rotation from the widget matrix.
2. Builds a local AreaDefinition (one box shape, one Blank property type 19, name `UI.DefaultTriggerVolumeName`) — kept
   locally, **not sent**.
3. Sends CreateTriggerAreaRequest with only the box shape (via AreaShapeDataBox: type 2, frame 0, min/max, **rotation
   zero** — FUN_143de12b0 passes `DAT_1455f0740`, bss) and the pending id.
4. State WaitingForAreaCreationResponse. On the response (FUN_14373a160 → FUN_143738ad0) tool+0x20 = new area guid when
   successful, then the tool clears its selection (FUN_143739ad0) and returns to Idle.
   The tool itself never asks for a prop to be placed. [P]

### 4.2 Prop ↔ area tie [P]
- Lightweight spawn (PlayerUpdate 0x62, 0.695 base reader FUN_142c26790): the **u64 at packet +0x268** is copied to the
  prop entity +0x240 (FUN_142d54040: `param_2[0x48] = *(param_3 + 0x268)`); when non-zero the prop gets an extra look
  (FUN_142ec18c0). In the server's writer this is the first u64 after the draw distance, the u32 list and a u32.
- The u32 at packet +0x260 binds client area properties (teleporter 0x17 / 0x18 ...) and is 0 for Linking Areas.

### 4.3 Edit (FUN_143739860 select, FUN_143739440 save) [P]
- Selecting a prop (ToolVolumeEventHandler FUN_143738ea0, event 0x400 / click 8) opens it for editing only when prop
  +0x240 (area guid) is non-zero **and** the client knows that area with exactly **one shape of type 2**.
- The widget is set from the **prop**: bounds = position ± scale / 2, rotation = the prop's rotation matrix.
- Saving builds scale = box size (x, y, z, 1), rotation = widget rotation, position = rotated box centre, applies them to
  the prop (FUN_142ec5900 scale) and sends **PropSave** (FUN_142fbec90: header 0xED/3, pending id, prop guid, position,
  rotation, scale, previous position / rotation / scale, ...). State WaitingForAreaUpdateResponse until the PropSave
  reply (PendingPropSaveId callback). No 0xEA message is sent for an edit.

## 5. Server behaviour (LandmarkEmu, Game/AreaDefinitions.cs)

- Create [I]: client constant `GameScripting.PropNpcDefinitionId = 1590` is never read by the client
  (trace/client-constants.md) — a server-side setting. With §4.1 (the tool places nothing) and §4.3 (editing needs a prop
  with the area guid), the server answers a create by making the area **and** a prop 1590 at the box centre with
  scale = box size, linked through the spawn's area guid. Permission: build access at the box centre on a build site
  (as a prop placement). The prop is free (the tool has no cost path).
- PropSave of a linked prop → the area's box follows the prop (centre, size, rotation) and an UpdateDefinition goes to
  the landscape.
- Deleting / picking up the prop removes the area (RemoveDefinition); removing the area (Area.Remove) deletes the prop.
- Login and every re-zone: SetDefinitions with the landscape's areas (0.695 clients; empty for older ones).
- Story Tools: "in this area" = inside any of the area's shapes (rotated box test); Linking Areas without an area keep
  the old 4 m × scale cylinder.

## 6. Open points

- AreaDefinition +0x30 / +0x34 / +0x35 / +0x38 / +0x3c..+0x48 and shape +0x08 / +0x0c / +0x5c meanings (defaults sent).
- Whether live attached a Blank (19) property to tool areas (the tool's local model has one; the server sends none, to
  save 128 bytes per area). → OPEN-LEADS.
- Sphere shapes (type 1): field use of min/max for spheres is not traced; the server tests the sphere inscribed in them.
- Which error live used for a permission refusal (the client has only codes 1 and 2); the server sends code 0 (silent)
  and shows `Construction.InsufficentPermissions`.

## 7. Developer checks without the mouse

- `luacode Area.Create()` — sends CreateTriggerAreaRequest for the Area Tool's current selection (the tool must exist).
- `luacode Area.SetName("<area guid>", "Test")` — UpdateDefinition (needs the area in the client's list).
- `luacode Area.Remove("<guid>")` — FUN_1431b3060: finds the client's volume by that guid (the object's +0x150 is
  the shape guid, +0x188 the area guid [I]); RemoveDefinition when the area has one shape, else UpdateDefinition
  without that shape.

## 8. Server code

`Game/AreaDefinitions.cs` (rules, persistence `world_area` / migration `20261026093300_WorldArea`),
`Network/Message/Model/AreaDefinitionMessages.cs` (records + C→S messages), `Network/Handlers/AreaDefinitionHandler.cs`,
`PlayerUpdateAddLightweightProp.AreaGuid` (spawn +0x268), hooks in `Props` (LoadWorld, Save, Remove,
PlaceLinkingArea, RespawnLinkingArea), `StoryTools.InArea` / `AreaCylinder`, `MiscHandler.ClientIsReady` (SendAll).
- Server log lines start with `Area` (AreaDefinitions); RequestResponse failures show in the client chat as the texts in §3.
