# Health bar task B: the harvest target (slot 9) and the Prop HUD indicator (0.695)

2026-10-31. Read-only trace (no code changed, no game / Frida / Ghidra run). Sources: re/decomp_all_695 (fn.py, grep),
re/out/rtti_695.tsv, gameData695 tables, float constants read from `<0.695 install>\Landmark64.exe` .rdata,
and the dev server log `server-data/worlds/dev/console_38.log`. **[P]** = proven (FUN_ address and what it does, or
file:line), **[I]** = inferred.

Existing research used (not repeated): trace/0695/full-npc-695.md (disposition +0x147c: 0 hostile, 1 neutral,
2 friendly; ctor default 2; SetDisposition 0x26/0x14), LandmarkEmu/docs/protocol.md:1373 and Game/Props.MenuName.cs
(Prop indicator shows the entity name; ShowProxyName = +0x1454 bit 0x40), trace/0695/agent-prop-collision.md §2.1
(spawn flag bytes), Network/Handlers/InteractionHandler.cs:317-353 (Target 0xB5 echo). Nothing earlier traced the
target slots, HUDIndicatorSettings TYPE_IDs or the Prop bar.

## 1. TargetOfPlayer 9 ("IsHarvestTarget") = "the entity is in slot 9 of the local player's ClientTargetManager"

- Requirements.txt row 19: `19^4030^TargetOfPlayer^1^9^...^IsHarvestTarget` [P data].
- Class ClientRequirementIsTargetOfPlayer, vtable 144741930, evaluate slot = **FUN_143710fe0** [P rtti_695]. It takes
  the entity (`param_2[0]`, or `param_2[1]` when +0x88 is set), reads its guid (entity +0x278 vfunc +0x28) and returns
  `FUN_1431e6210(*(BaseClient + 0x32b20), guid) == PARAM_1` [P]. [I] the "Target..." factory names register the
  `RequirementAutoInitializeParam<…,1>` variant (+0x88 = 1 → `param_2[1]` = the target; TargetMaxHealth FUN_143711bb0
  reads `param_2[1]` directly).
- **FUN_1431e6210** walks the manager's node list (+0x18, next +0x10) and returns the slot (+0x20) of the first node
  whose guid (+0) matches; 0 if none [P].
- The manager: BaseClient +0x32b20 (`param_1[0x6564]`) = **ClientTargetManager** (ctor FUN_1431e4db0, vtable
  144723698; base class TargetManager 144949008) [P]. Created in FUN_142d12360 (part_0066 ~9358), which registers
  `targeting_ring_red.dds` for **slot 3** and `targeting_ring_blue.dds` for **slot 8** (FUN_1431e72e0); every other
  slot uses the default `targeting_ring.dds` [P].
- Node layout (AddTarget FUN_143c73ee0): {u64 guid +0, u32 slot +8, next +0x10, prev +0x18, u32 slot key +0x20,
  bucket next +0x28}; bucket = slot % 5 [P]. SetTarget (FUN_143c74960) = RemoveTarget(slot) (FUN_143c74610, removes
  every node of that slot) then AddTarget → **one entity per slot** [P].

### ClientTargetManager vtable 144723698 [P]

| off | fn | meaning |
|---|---|---|
| +0x08 | FUN_1431e5700 | AddTarget(slot, guid, request): request=1 sends TargetPacketRequestAddTarget 0xB5/1, else local add |
| +0x10 | FUN_1431e7220 | SetTarget(slot, guid, request): request=1 sends **TargetPacketRequestSetTarget 0xB5/2 {u64 guid, u32 slot}** and changes nothing locally; request=0 sets locally (FUN_143c74960) and notifies BaseClient+0x31ec0 vfunc +0xa8 |
| +0x18 | FUN_143c74610 | RemoveTarget(slot) |
| +0x20 | FUN_143c74500 | RemoveAll |
| +0x28 | FUN_143c74110 | GetTarget(slot) |
| +0x38 / +0x40 | FUN_143c740f0 / FUN_143c740c0 | GetTarget(3) / GetTarget(8) |
| +0x48 / +0x50 | FUN_143c74930 / FUN_143c748f0 | SetTarget(**3**, guid, request) / SetTarget(**8**, …) |

### Who sets slot 9 [P]

Only two writers exist (grep of every +0x32b20 / `[0x6564]` / FUN_142ccb990 use; the only other getter user,
PlayerUpdate dispatcher case 0x22, only reads slots 3 and 8):

1. **BaseClient::SetCurrentTarget FUN_142d4e890(client, &guid, request)**. Runs only when the config value with hash
   0xe822e0d0 (`-0x17dd1f30`) is 1 — default DAT_144c41178 = **1.0** (read from the exe) — and the guid is not the
   local player. For the entity found by guid:
   - FUN_142ea8f90(local player, target) "is ally" true → +0x50 → **slot 8** (blue ring);
   - else if disposition (+0x147c) == 0 and !(entity +0x1460 & 4) → +0x48 → **slot 3** (red ring);
   - else → +0x10(**9**, guid, request) → **slot 9**.
   With a PC player and a non-PC target whose FactionId (+0x1448) is outside 1-3, "is ally" ⇔ disposition == 2
   (FUN_142ea8f90). So: hostile (0) → 3, friendly (2) → 8, **neutral (1) → 9**. +0x1460 bit 4 (sends a
   disposition-0 entity to 9 as well) is set only in SpawnProxiedCharacter FUN_142f8a0b0 from spawn byte +0x598 bit 0,
   which the NPC handler FUN_142cddaa0 fills from lightweight packet +0x29a bit 6; the prop handler FUN_142cde1c0
   never sets it (the only `|= 4` writer of +0x1460 found by grep).
   Every non-clearing caller passes request = 1, so the client **asks the server** and waits; all request = 0
   callers (FUN_142cd6830, FUN_142d4ce20, FUN_142eb6a10, dispatcher case 0x13) pass an empty guid (clear the
   current-target pointer only; slots are untouched). Request = 1 callers: click on an entity FUN_142d0f610 →
   FUN_142cbed70 (sends CommandPacketSelectPlayer 0x1E/0x16) → **FUN_142ca6f00**; Lua Ui.TargetGroupMember
   (FUN_1435c82f0); tab-cycling FUN_1431e5de0 (friendly list, disposition 2) / FUN_1431e5e70 (disposition 0 list);
   CombatPacketAttackProcessed handler FUN_1433ca930 (auto-targets an attacker when the local player is hit and has no
   target); FUN_142d11210; HUD FUN_143458d90 / FUN_143459e30.
2. **Server packets** (0xB5 S→C dispatch FUN_1431e6690): sub 5 AddTarget → FUN_1431e6930 → vtable +0x08 local;
   sub 6 **SetTarget** → FUN_1431e6b40 → vtable +0x10(slot, guid, 0) local; sub 7 RemoveTarget → FUN_1431e6ab0 →
   FUN_143c74610; RemoveAllTargets FUN_1431e69e0 → FUN_143c74500. Each needs its bool ok = 1. Reader FUN_1431e3fd0:
   `u16 0xB5, u16 sub, u64 guid, u32 slot, bool ok` (struct +0x20 guid, +0x28 slot, +0x18 ok). **The client applies
   whatever slot the server sends**, so the server can also place any entity in slot 9 itself.

Nothing in the harvest / ability / tool code writes a target slot (no harvest-toggle path, P0726): the harvest
target is "the neutral entity the player selected" [P for the absence by grep; I for the design intent].

### Clearing [P]
ClientTargetManager::CheckTargets **FUN_1431e57e0** (every frame from BaseClient::GiveTime) sends
TargetPacketRequestRemoveTarget {u32 slot} for each slot whose entity is gone or farther than the config value hash
0x9d2b6292 (default DAT_1446b8dd8 = **50.0**) from the local player; it repeats each frame until the server answers
RemoveTarget ok. So slot 9 stays on the last neutral entity selected until it is felled/despawned, the player is
50 m away, or another neutral is selected.

### Runtime proof on our server (console_38.log) [P]
- 2614-2620 (04:39:15, Player chopping with cast 1020): client sent TargetBase RequestSetTarget for gathering node
  0x4001CCFFF2FFF625 (GuidBase 0x4000…, Gathering.cs:142); our reply `06-00 25-F6-FF-F2-FF-CC-01-40 08-00-00-00 01`
  = SetTarget slot **8**, ok. The client asked for slot 8 because the node's disposition is 2 (friendly):
  ctor FUN_142e88840 default, and our LightweightToFullProp writes +0x90 = 2
  (PlayerUpdateLightweightToFullProp.cs:30), which FUN_142d532f0 copies to +0x147c.
- 3570-3599 (04:39:18): "felled tree", RemovePlayer for that guid, then three RequestRemoveTarget slot 8 (CheckTargets)
  answered `07-00 08-00-00-00 01`.
- So **IsHarvestTarget is never true on our trees today**: they sit in slot 8, not 9.

## 2. HUDIndicatorSettings.txt: loader and TYPE_IDs

Loader **FUN_142fe8980** (0.695; FUN_142f48c30 is the 0.589 address in datatables-695.md) [P]: rows `TYPE_ID^PARAM1^
CLIENT_REQ_ID`, kept only when TYPE_ID is 1..13; record {TYPE +0x08 and +0x28, PARAM1 (= HudIndicators
CODE_ENUM_TYPE) +0x0c, CLIENT_REQ_ID +0x10}; hashed by TYPE & 0x1f, **new rows prepended** to the bucket, so lookups
walk a TYPE's rows in reverse file order. The manager is reached through BaseClient vfunc +0x90.
FUN_14344a890(hud, entity, guid, TYPE, code) = first row with that TYPE and PARAM1 == code (code 0 = any): req −1 →
false, 0 → true, else RequirementExpression via FUN_1432f5870(mgr, req, local player, entity, guid); no row → false.

| TYPE | Used by | Meaning | Rows |
|---|---|---|---|
| **13** | FUN_14343b0b0 from per-entity update FUN_143444620 | **choose the indicator type**: first row (reverse order) whose req passes gives the code type; cached in entity +0x14b8 (recomputed only while 0 / −1; none → −1, no indicator) | 27→27, 22→21, 19→11, **18→13**, 1→3 |
| **1** | FUN_143444620 every update | **show the indicator at all** | 1→3, **18→13**, 19→0, 22→21 |
| **2** | FUN_143449e00 → vfunc +0x110 → indicator +0xd0 | **show the name**; only if entity +0x1454 bit 0x40 clear (else only if hud +0x518) | 1→0, **18→13**, 22→39 |
| **3** | FUN_143449e00 → vfunc +0x190 → indicator **+0xfc** | **show the health bar** (conditions below) | 1→25, **18→2**, 22→2 |
| 7 | FUN_143449e00 → vfunc +0xf8 → indicator +0x38 | read by FUN_1434479e0 to keep an off-screen indicator clamped at the screen edge [I meaning] | 6→0, 18→2 |
| 8 / 9 / 11 | → +0xd8 / +0x3b / +0xf6 | no rows (always false) | — |
| 10 | part_0105 ~3116 / ~3594 (other indicator classes) | no rows | — |

[P] for the uses and rows; the TYPE 7 meaning is [I].

## 3. The Prop indicator (HudIndicators row 12, code 18) and its bar

- Per-entity update **FUN_143444620**: code type from TYPE 13, gate TYPE 1; then if hud +0x580
  ("useHudIndicatorDefinitions", printed by FUN_143441b10) is set and the type is in mask 0x642002 (1, 13, 18, 21,
  22) it uses the native **ProxiedCharacterIndicator** (FUN_14343d330 → ctor FUN_143436fd0; full update vfunc +0x278 =
  **FUN_143449e00**, per-frame position +0x270 = FUN_143449190) drawn by the HudIndicatorDefinition; otherwise the
  Flash **FlashProxiedCharacterIndicator** (FUN_14343b5b0; update +0x290 = FUN_143449560; Hud SWF
  indicator/PlayerIndicator.as, `m_healthBar` shown when health > 0) [P].
- **Bar visibility** (FUN_143449e00) [P]: `bar = !(entity +0x1454 & 0x80) && maxHealth(resource type 1) >= 1 &&
  (IsEnemy(local, entity) || TYPE 3 row for the code passes)` → indicator +0xfc. IsEnemy = FUN_142ea9630; [I] false in
  Landmark (the player's FactionId equals DAT_1455fe51c, so "neutral" short-circuits), so for a prop the bar needs
  **3^18^2 = TargetInjured (health % < 100)**. Entity +0x1454 bit 0x80 comes from lightweight packet **+0x298 bit 5**
  (FUN_142d54040; NPC spawn path +0x599 bit 1) — we send 0.
- **Bar value** [P]: `FUN_143b8c370(entity + 0x4268, type 1)` = `current * 100 / max` (int percent; 0 when the entity
  has no Health resource) → vfunc +0x198 FUN_143446720 → indicator **+0x100**. This is exactly the value
  TargetHealthPercent (FUN_143710070) tests, so TargetInjured, TargetAlive and the bar fill all read the same Health
  resource as task A.
- **Draw: HudIndicatorDefinitionProp vtable 14476eab0 slot +0x08 = FUN_143726b80** [P]:
  - bar when indicator +0xfc, definition PARAM18 (+0x94 = **3604**, getter FUN_14342c040) and PARAM19 (+0x98 =
    **3603**) are non-zero and hud +0x57d / +0x57e are 0;
  - both ids go through FUN_1433f2330(BaseClient +0x32488 …) to two images and into **Deep::ProgressBarTextured**
    (FUN_1440e9730) with fill = `indicator+0x100 × DAT_144c4103c` (= **0.01**, from the exe → 0..1), anchor 7 =
    **centred** (−0.5, −0.5), offset DAT_1447dc178 = −8.0 from the indicator point;
  - colours `alpha << 24 | 0xffffff` with alpha = PARAM1 (+0x50) × 255 and PARAM2 (+0x54) × 255 → **0.75 / 0.75 =
    75 % opacity of the two bar layers** (which layer is which: [I]);
  - name text above (line height DAT_14469b0f4 = 18.0) when indicator +0xd0 (TYPE 2) and the label is non-empty.
  The bar colour (green in the capture) is in images 3604 / 3603, which are not in gameData695/icons [open].
- **TargetNameHidden (type 4052) = ClientRequirementProxyNameHidden FUN_1437119c0** [P]: false (both operators) when
  the entity's display name is empty (+0x13c8 = length of the IString at +0x13b8, set by FUN_142ec5150); else operator
  1 = hidden, 2 (ShowProxyName) = not hidden, from entity **+0x1454 bit 0x40**. That bit is set by FUN_142ec28c0 from
  lightweight packet **+0x298 bit 4** (FUN_142d54040) or spawn +0x599 bit 2 (FUN_142f8a800 / FUN_142f8a0b0). So
  ShowProxyName = "has a display name and the spawn's name-hidden flag is clear". Our nodes send no name (NameId 0,
  empty strings; Gathering.cs:754-762) → ShowProxyName false.

## 4. Exact client conditions for a bar over a tree (native path)

Let T be the tree entity, L the local player.
1. TYPE 13 picks code 18 (needs req 27, 21 to fail first: they do for a prop since NotProp fails) when **req 13**
   passes; TYPE 1 needs **req 13** each update:
   `(TargetIsProp && ShowProxyName) || (TargetInjured && TargetAlive && (IsHarvestTarget || (DistToTarget <= 7 &&
   profile ∈ {90, 91, 296})))`, where TargetIsProp = vfunc +0x100 non-null (FUN_143710e10), TargetInjured =
   health % < 100, TargetAlive = health % > 0, IsHarvestTarget = T in L's slot 9, DistToTarget = L↔T distance
   (FUN_14370fb10), profile = T +0x1474 (FUN_143710dd0; set by FUN_142ec55e0 from lightweight packet **+0x1a0** —
   we send 0 — or from the featherweight spawn +0x70).
2. Bar: T has a Health resource with max ≥ 1 and **current < max** (TYPE 3 req 2), packet +0x298 bit 5 clear.
3. Fill = current/max. No label while T's name is empty or hidden.

With unnamed trees that means: **bar only while the tree is damaged, not dead, and is the player's slot-9 target**
(or, for plant profiles 90/91/296, damaged and within 7 m). That is P0123 (2014-02-12) "only see the health bars of
trees you're currently harvesting" [I: the patch note matches the IsHarvestTarget clause].

## 5. What the server must do

- **IsHarvestTarget needs the server**: either (a) make gathering nodes neutral so the client itself asks for slot 9,
  or (b) push slot 9 itself.
  - (a) [P mechanism] send disposition **1** for trees / plants: LightweightToFullProp +0x90 = 1 for nodes (today 2),
    or PlayerUpdate 0x14 SetDisposition {u64 guid, u32 1} after the spawn. The click on the tree then sends
    RequestSetTarget {node, 9}; the existing echo (InteractionHandler.cs:331-339) confirms it. [I] this is how live
    did it: the client's own AddFeatherweightObject handler FUN_142cdd710 spawns a collidable prop with **disposition
    1** and a profile id (+0x70 → FUN_142ec55e0), i.e. exactly what a harvest node needs.
  - (b) on the first harvest hit on a node (or whenever the harvester's slot-9 target is not that node) send
    TargetPacketSetTarget `{u16 6, u64 node guid, u32 9, bool 1}` to that player only. Works without a click; it
    replaces any other slot-9 target. [I] not known to be live's way.
  - Either way keep answering RequestRemoveTarget (CheckTargets) with RemoveTarget ok, as now.
- **Health** (task A): the node needs Health resource type 1 (max ≥ 1) and current lowered per hit, current > 0 until
  felled; nothing shows at full health (TargetInjured).
- Keep lightweight base +0x298 bits 4 / 5 clear for nodes (bit 5 would hide the bar). Do not give nodes a visible
  name: with a name, (TargetIsProp && ShowProxyName) shows a label over every tree all the time.
- Optional: send the node's profile id at lightweight base +0x1a0 (plants 90 / 91 / 296 get the 7 m bar; trees the
  TargetIsTree* profiles of Requirements.txt). Which model uses which profile is open.

## 6. Open

- hud +0x580 (native vs Flash indicators) default: set only by toggles FUN_143439b70 / FUN_14343a5e0 / FUN_143447510
  (no direct callers; probably console commands); constructor value not found. Next: find the indicator manager
  ctor (class with +0x88 hash, +0x518, +0x580; FUN_143441b10 dumps it) or read it at runtime.
- Images 3604 / 3603: find the image-set table the client loads for FUN_1433f2330 (BaseClient +0x32488) to see the
  bar art and which layer is fill.
- Profile ids for tree / plant models (Requirements.txt 13-25 rows, profiles 57, 58, 101, 105, 200-214, 90, 91, 296)
  and where the live server took them from (ClientProfile / ReferenceDataPacketClientProfileData?).
- Config names of hashes 0xe822e0d0 (targeting on, default 1) and 0x9d2b6292 (target range, default 50) — not in
  StringHashToValue.txt; check the ClientConfig / game-settings lists (DAT_145431198 +0x6b0 / +0x14c0).
- In-game check after (a): RequestSetTarget slot 9 for a tree in the log, then the bar after the first hit.
