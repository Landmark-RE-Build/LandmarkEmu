# Agent leads C: L168, L169, L170, L180-L185 (research only, 2026-10-18)

Scope: OPEN-LEADS L168, L169, L170, L180, L181, L182, L183, L184, L185. No code edits, git, game, Frida, Docker or
downloads. Sources:
- design/forum (all + patches), design/wiki/pages, re/out/forum_raw, re/out/text_695.tsv;
- gameData695 (CalculatedStatDefinitions, CodeStringMappings, VoxelMaterials, VoxelSystemTemplates, items.json);
- the 0.695 client's CommonData/Voxel/templates (<0.695 test copy>, read-only);
- re/decomp_all_695 (re/tools/fn.py), capstone on <0.695 test copy>\Landmark64.exe, re/as3 + re/as3_695;
- our code (read only).

Marks: **[P]** = proven (source, address or offset, quote of 15 words or fewer). **[I]** = inferred (reason given).

**Web:** WebFetch refuses web.archive.org ("unable to fetch from web.archive.org", tried on the Player Studio page).
So nothing here comes from the web.

Scratch scripts are in the session scratchpad: `otdmat.py` (template material map), `otdcell.py` (decodes the cells:
materials, layers, flags) and `otdmap.py` (ASCII footprint). Their logic is described in §L180, so they can be rebuilt.

---

## Summary

| Lead | Verdict | Values for a build agent |
|---|---|---|
| **L168** | UNCHANGED since agent-leads5.md. The channel semantics are now **built** (MovementEffects.cs "Channels [P, 0.695]"). | No 2016 magnitudes exist locally. Keep the 2014 costs and cooldowns (agent-leads5.md table). The web path is blocked. |
| **L169** | NOT RESOLVABLE from the client [P negative]. The server's heuristic stays. | No shipped caller. The sibling natives take (storeId, bundleId, quantity). By that pattern the packet carries **arg1 = store id, arg3 = quantity**, and the bundle (arg2) is dropped [I]. The 0x4B result is parsed and dropped [P]. |
| **L170** | **RESOLVED + BUILT** (2fc0c49; agent-l170-followup.md). | Nothing new. Open only: our NPCs passing the attackable check, and the sweep numbers. |
| **L180** | **FOUND the slab assets [P]**. Live's use of them is [I]. | The client ships `claim1x1/2x1/3x1/3x3.otd`. Each is a **5-voxel (2 m) slab of one material**, layers y 1-5. claim3x3 is **525 × 524** voxels, the 210 m site footprint, shaped like a plus. Stamp it centred under new Pioneer sites. The stored material is ARRAY_INDEX 53 (in 0.695 "Raw Amaranthine", so a stale index). Remap it to a free slate (ARRAY_INDEX 26 Worked Slate) behind a setting. |
| **L181** | **FOUND the full 0xB9 protocol [P]**. The Vivox field roles come from the request structs [P offsets, I SDK names]. | S→C sub 0 Login {str account, str password, str server URL, str unused}. Sub 1 JoinChannel {u8 type, str URI, str password, str name}. Sub 2 LeaveChannel {u8 type}. C→S sub 6 RequestCustomChannel {str room, str password}. Channel types: 0 Echo, 1 Proximity, 2 Faction, 3 Group, 4 GroupLeader, 5 Raid, 6 Guild, 7 Custom. **The clean "unavailable" answer is to send nothing**: with no Login the client never connects to Vivox [P]. |
| **L182** | **RESOLVED [P]**: 2016 had no cooldown (removed 5 May 2015). The setting is already built (FriendTeleportCooldownSeconds 0). | Correction: the **client itself** checks 187's cooldown in Ui.TeleportToFriend and prints 562080. So the live-like way to run a non-zero cooldown is the CasterInfo cooldown (+0x70 → record +0x14c). Stat 53 has no client reader [P negative]. |
| **L183** | **RESOLVED / BUILT**: no client cap [P]. The cap is server-side; the source is the wiki only. | MailMaxStack = 9000 with result 5 (Mail.cs:286-300) is already in place. No further source exists locally. |
| **L184** | **PERIOD FOUND [P dev, 2014]**; **client display FOUND [P]**. | Developer post: locked "for up to 12 hours after purchase". Set StoreTradeWaitHours **12** (now 24). The tooltip reply's u64 at item record **+0x120 = FraudTimer (Unix seconds when tradeable)**. The client then shows "Tradable in %TIME" (562375). |
| **L185** | Rules **FOUND [P dev, 2014]**; counts NOT found. Most of it is already built. | The thresholds stay ours (3 / 3). The client's reject reasons are verdict codes 2-7 [P]. Record them for the staff queue. |

---

## L168: movement-effect numbers (unchanged)

- Status of the code: the channel half of agent-leads5.md is built.
  - MovementEffects.cs:61-84 describes it: 326 / 328 / 542 as channelled kind 0x216b5dcb, the release via input manager
    GiveTime FUN_1434783f0, and the server end with a CastSuccess.
  - Nothing more is needed from this lead for the semantics.
- No new local source has 2016 sprint %, jump height, leap distance or glide speed.
  - The search of agent-leads5.md covered every forum thread, forum_raw and the wiki boot pages. It was not repeated.
- **Still open, with the next path:**
  - a video measurement (the 2016 Landmark Live streams listed in design/patchnotes-after-0589.md), once a browser
    route is allowed;
  - the Dash Soles / Roll Wraps root motion: the AnimationId of 432 / 807-813 and the client's ability animation path;
  - the take-off damage of Explosive Leap (row 429 stays a weak candidate).

---

## L169: PurchaseBundleWithInGameCurrency 0xD0 arguments and the 0x4B fields

### What the client does [P]

- **The native** (0.695 FUN_1435e9400, identical in 0.589 FUN_143538f10):
  - it checks that there are 3 numeric Lua arguments;
  - it builds PacketClientInGamePurchase (opcode 0xD0) with +0x10 = arg1, +0x14 = arg3, +0x18 = 0;
  - the writer FUN_1435ebe10 writes u16 opcode, then +0x10, +0x14, +0x18;
  - **arg 2 is checked but never read.**
- **The sibling natives in the same Lua table** (InGamePurchaseStoreScreen) all take (store, bundle, …):
  - CanPurchaseStoreBundle FUN_1435ea290 reads arg1 and drops it, then calls FUN_143248df0 with
    `bundle = arg2, quantity = 1`;
  - EquipStoreBundle FUN_1435e8d40 passes (arg1 → store, arg2 → bundle, arg3);
  - AddStoreBundleToOrder FUN_1435ea400 takes 5 arguments and uses 2, 3 and 5.
- **No script calls the native.** Searched: lua_all.txt (binding list only), re/as3, re/as3_695, design/. Hits: none
  [P negative].
- **The 0x4B reply** cPlayerInGamePurchaseResult:
  - zone dispatcher case 0x4a → FUN_142c4b300 → reader FUN_142c25490;
  - it reads u16 +0x08, u32 +0x10, u32 +0x18, +0x1c and +0x20;
  - the case then jumps straight to the common exit, so **nothing consumes the fields** [P].
- No log or ToString string names the fields. strings.tsv has "storeId / storeBundleId / quantity" formats, but only in
  the store-content and tooltip paths.

### Verdict

- By the sibling pattern (store, bundle, quantity), the 0xD0 body is most likely **{store id, quantity, 0}**, with the
  bundle id lost to a client bug [I, medium].
- It is equally possible that the native's own order was (bundle, store, quantity) [I].
- **The build is already right for both readings:** StoreBundles.HandlePurchase takes whichever field is one of the
  player's bundle ids, and the other field as the quantity.
- Keep the 0x4B echo {first, second, result}. The client drops it, so its meaning cannot matter to the client [P].
- **Still open:** a caller in another Daybreak client build's scripts, or a capture. Neither exists locally. Low value,
  because the server already accepts both orders.

---

## L170: melee targeters (resolved earlier)

- agent-leads5.md found the mechanism: CasterInfoReply +0x118 `List<ClientExecutedTargeterData>`.
- agent-l170-followup.md answered:
  - the Sweep's target filter: none beyond the physics body;
  - bit 45 and the Block effect;
  - the ShapeDefinition fields.
- OPEN-LEADS records it as BUILT (2fc0c49 / merge d11cead).
- Nothing new from this pass.
- Remaining items belong to the follow-up's list:
  - live's real Sweep numbers per weapon (radius, angles, duration, re-hit);
  - Block for PvP viewers.

---

## L180: new-player site slate slab

### The client ships four "claim" slab templates [P]

`CommonData\Voxel\templates\claim1x1.otd`, `claim2x1.otd`, `claim3x1.otd` and `claim3x3.otd` are in both 0.589 and
0.695, with identical sizes (re/out/fileindex_589.tsv / fileindex_695.tsv).

Decoded with the VoxelTemplate format (VoxelTemplate.cs), all version 2:

| Template | Cells | Size (voxels) | Size (m, 0.4 m voxels) | Solid voxels | Material map |
|---|---|---|---|---|---|
| claim1x1 | 25 | 175 × 5 × 175 | 70 × 2 × 70 | 153,125 (all of 175 × 175 × 5) | {53: 153,125} |
| claim2x1 | 45 | 350 × 5 × 174 | 140 × 2 × 70 | 304,500 | {53: 304,500} |
| claim3x1 | 70 | 525 × 5 × 174 | 210 × 2 × 70 | 456,750 | {53: 456,750} |
| claim3x3 | 145 | 525 × 5 × 524 | 210 × 2 × 210 | 763,000 + 116,025 template air | {53: 763,000} |

- **Thickness.** Every solid voxel lies in cell-local layers **y = 1..5**; y = 0 is empty. So each template is one 2 m
  thick slab, one voxel above the template's base [P].
- **The claim3x3 footprint is a plus**, not a full square [P, otdmap.py at 15-voxel resolution]:
  - a 175-voxel-wide column runs the full 524 depth;
  - a 175-voxel-deep row runs the full 525 width;
  - the four 175 × 175 corner tiles have no slab, only scattered template-air voxels (flags 0x63 / 0x43, material 0).
    Those are air marks: the Stamp treats material 0 with the solid bit as air.
- **What the names mean [I strong].** The tile names are claim shapes made of 70 m tiles: 1 × 1, 2 × 1, 3 × 1, and
  3 × 3 as a centre plus four arms. That matches the 2015 root + attachment claims (forum 2015-04-23-54384). claim1x1
  is the pre-launch 70 m site (Claims.cs:126 "70 × 100 × 70 m"). claim3x3's outer extent, 525 × 524, is exactly the
  launch build site (210 × 180 × 210 m = 525 × 450 × 525 blocks, Claims.cs:31).
- **No client code loads them.** No "claim1x1" / "claim3x3" / ".otd" string appears in the 0.695 decompile, and
  VoxelSystemTemplates.txt does not list them (it lists only the starter_* / poi_* stamps) [P negative].
- So they are **server-side assets shipped with the client**, like the cavern ruins (ruin-stamping.md:236 "No client
  table or code names a ruin_*.otd") [I strong].
- The 30 Jun 2016 note fits a server fix [I]: "Corrected some issues with the slate slab placement on new player
  claims" (design/forum/patches/2016-06-29-57596). The files did not change between 0.589 (4 Jun) and 0.695.

### The material problem [P facts, I reading]

- Block data and template material maps use VoxelMaterials **ARRAY_INDEX** [P]:
  - ruin-stamping.md §1 proved it for the version-2 ruins;
  - the starter shapes confirm it: starter_pillars = {41, 46, 26} = Darker / Darkest / plain **Worked Slate**, and
    castle_courtyard / newbie_wall_* = 42 Dark Worked Slate, 27 Worked Stone Slab.
- **ARRAY_INDEX 53 = ID 427 "Raw Amaranthine"** (an ore, category 33, item 122) in both 0.589 and 0.695
  VoxelMaterials. **ID 53 = "Biome_Ashfang_Rubble01".** Neither reading is slate.
- So the claim slabs most likely carry a **stale index** from an older material table, and a "slate slab" on live
  needed a remap or another source [I].
- The free gray slates are all `INCLUDE_IN_TRAY 1`, `ITEM_ID 0`: ARRAY_INDEX 26 Worked Slate, 41 Darker, 42 Dark,
  46 Darkest Worked Slate. The wiki "Free-to-Use Building Materials" counts 6 slate textures in gray [W].

### Values / design for a build agent

- **Where:** Pioneer.cs (after the site is placed, `Settle` / `Place`), or Claims.EnsureStartingLocation's first
  placement on a Pioneer zone (464-468). Run it once per new site.
- **What:** `EnvironmentServer.Instance.StampTemplateAt(VoxelTemplate.Load(Folder + "claim3x3.otd"), vx, vy, vz, zone)`.
  The API is IEnvironmentServer.cs:206. Parameters:
  - corner = the site's min x / min z in voxels (site centre − 262 voxels in x and z);
  - vy = ground voxel − 6, so the slab (layers 1-5) **replaces the top 2 m of the flat Pioneer ground**, flush with
    it [I: the Pioneer lands are "perfectly flat" (wiki), and a raised 2 m platform would not be called flat ground];
  - mark the stamped cells permanent like the Spire floor, so they revert like terrain.
- **Material:** remap 53 → **26 (Worked Slate)** before stamping [I: the patch note says "slate"; 26 is the plain free
  slate the starter shapes use].
- **Settings** (live-default + toggle rule):
  - `PIONEER_SLAB` = `claim3x3` (default) | `claim1x1` (a 70 m slab under the Starting Location, if the plus shape
    looks wrong in game) | `off`;
  - `PIONEER_SLAB_MATERIAL` = 26.
- **Check in game:** the slab must not cover the Starting Location prop. EnsureStartingLocation puts it at the -x/+z
  corner 10 m in. With claim3x3 that corner tile has **no slab**, so place the Starting Location on the slab (an arm
  or the centre) or accept ground under it [I].

### Still open (L180)

- Which template live used (1x1 vs 3x3) and the vertical placement. Next paths:
  - the wiki image `Landmark-pioneer-landscape.jpg` (not in the dump; the browser route via archive.org);
  - Verlin EQL's Pioneer video (wiki Pioneer Landscape, youtube Lj7o-3o93Kg), once the user allows a browser look.
- Whether ARRAY_INDEX 53 was a slate in an older (2015) VoxelMaterials. No older table exists locally. A 2015 client
  build's VoxelMaterials.txt would settle it.

---

## L181: Voice chat (0xB9, Vivox)

### The S→C packets [P, 0.695]

- **The handler** is FUN_1430cfad0, the PlanetsideClientVoice packet entry. It is reached from the zone dispatcher
  FUN_142d1ccf0 (jump-table case 0x52), and the packets it builds carry opcode 0xb9.
- **The header** is u16 opcode, then u8 sub.
- **Strings** are u32 length + bytes (FUN_142b2aaf0 / FUN_142d4b5d0).

| Sub | Class | Wire after the header | Where it goes |
|---|---|---|---|
| 0 | VoiceLoginPacket (reader FUN_1430cb780) | str → +0x48, str → +0x60, str → +0x18, str → +0x30 | Kept at voice +0x38 |
| 1 | VoiceJoinChannelPacket (reader FUN_1430cada0) | u8 type → +0x18, str → +0x20, str → +0x38, str → +0x50 | Becomes a channel entry {type, str1 (+8), str2 (+0x20), str3 (+0x38)}. An older entry of the same type is replaced (except Custom 7). Then FUN_1430ce280 joins. |
| 2 | VoiceLeaveChannelPacket (inline) | u8 type | Every entry of that type is removed, then the channels are re-synced |

### The C→S packet [P]

- **Sub 6, VoiceRequestCustomChannel {str room name, str password}.**
  - It is built by FUN_1430d3640 and sent on the zone channel (FUN_1430cb3f0).
  - Lua `Voice.RequestCustomRoom(roomName[, password])` (FUN_1435fa200) sends it.
  - Usage text: "Usage: Voice.RequestCustomRoom(roomName[, password])".
- **Channel type names** come from FUN_143c6dff0: 0 Echo, 1 Proximity, 2 Faction, 3 Group, 4 GroupLeader, 5 Raid,
  6 Guild, 7 Custom [P].
  - Wiki Chat "Local" = **1 Proximity**: FUN_1430d0a70 calls the positional setup FUN_14369b6b0 only for type 1.
  - Wiki "Guild" = **6** [P names; I mapping to the wiki words].

### What each VoiceLogin field is (from the Vivox calls)

- **The login field is read only when voice is enabled** (voice +0x188, from the settings at DAT_145431328+0x14e0).
  The update FUN_1430cf6b0 then does one of two things:
  - **Vivox not yet connected** → FUN_1430ce900(this, login+0x18, login+0x30). It formats `"%s/api2/"` from
    **login +0x18**, which is the account-management server base URL, then BeginConnect FUN_143691370 [P].
    Capstone check: FUN_1430ce900 overwrites r8 before ever reading it, so **login +0x30 is not used here** [P]. No
    other reader of +0x30 was found [P negative].
  - **Connected (Vivox state +0xec == 3)** → BeginLogin FUN_143691770(vivox, **login+0x48**, **login+0x60**). It fills
    the request: +0x30 the connector handle, **+0x38 = login +0x48**, **+0x40 = login +0x60**, then +0x48 = 2,
    +0x4c = 1, +0x50 = 10, +0x54 = 0, +0x58 = 4. It logs "Sending Login request %s %s %s" (request, the first string,
    the connector) [P offsets].
    That is the Vivox `vx_req_account_login` layout: acct_name, acct_password, answer_mode, enable_text,
    participant_property_frequency = 10, buddies, management mode [I: SDK struct names].
- **So the wire order of VoiceLogin is:**
  1. **account name**;
  2. **account password**;
  3. **server URL** (e.g. the client's built-in default host is "enlp.vivox.com", FUN_1430cfec0, used by the debug
     console's createconnector);
  4. **an unused string**.
- **JoinChannel** → FUN_1430d0a70(type, str1, str2, str3) → BeginSession FUN_143691930. The request takes:
  - +0x40 = str1 = **channel URI** (the client's own debug form is "sip:confctl-2@%s");
  - +0x48 = str2 = **channel password**, set only when non-empty;
  - +0x38 = a display name, taken from str3 for type 7 after stripping a "user." prefix.
  So str1 = URI, str2 = password, str3 = custom room name [P offsets; I names as above].

### The clean "unavailable" answer [P]

- Without a VoiceLogin packet (voice +0x38 == 0), FUN_1430cf6b0 never connects. With voice enabled it does nothing; with
  voice off it calls the Vivox shutdown FUN_14369bed0(vivox, 0).
- RequestCustomRoom without a reply simply never joins. No timeout path was seen.
- **So the server's current behaviour (send no 0xB9 at all) is already the clean "voice unavailable" state.** Do **not**
  send a Login with empty strings: that starts a connect to an empty URL (FUN_1430ce900 → BeginConnect) and Vivox errors
  in VivoxWrapper.log [I].
- **Handle C→S 0xB9 sub 6** by logging and ignoring it (ZoneMessageOpcode.VoiceBase 0xB900 exists), so it is not an
  unknown packet.

### Still open (L181)

- What live sent: the Vivox account naming, the server URL and the channel URIs. No capture or source exists locally.
- Self-hosted voice would need a Vivox-compatible service, which is not available. It stays a later option. This is not
  a write-off: record it as a lead.
- **For the docs:** trace/systems/social.md:45 and social-service.md:168 call voice "not needed / out of scope".
  Reword them as "no voice service; the server sends no 0xB9 (clean unavailable state)", with this layout.

---

## L182: Travel to Friend cooldown

- **The live history [P, design/forum/patches]:**
  - 2014-05-02-34889: "Teleport to friend remains unchanged, with a 30 minute cooldown."
  - 2014-09-16-48275: "will now correctly display an error when it is on cooldown."
  - **2015-05-05-54485 (the big wipe):** "The cooldown has been removed from teleporting to friends through the contact
    list".
  - No later note restores it. So 2016 = **no cooldown**. The wiki Chests line is 2014-era [I strong].
- **Built:** Abilities.cs:571-609, FriendTeleportCooldownSeconds default 0. Nothing to change for live behaviour.
- **Correction to Abilities.cs:577-585** ("no client code reads 562080"): the client does read it, by name [P].
  - Lua `Ui.TeleportToFriend` (FUN_1435c91f0) looks up StringHashToValue hash 0xD84CF278 (Teleport.FriendAbilityId =
    187).
  - It computes cooldown − elapsed with FUN_142e173d0 / FUN_142e16440:
    - the cooldown is ability record +0x14c (seconds), reduced by entity +0x4a74 when record +0x150 is set;
    - elapsed is the timer at record +0x7c.
  - If the result is > 0, it shows code string **AbilityCastError.OnCooldown = 562080** "…for another %d minutes and %d
    seconds" itself (FUN_142b24220 with secs / 60, secs % 60) and does not cast.
  - So for a non-zero owner setting, the live-like way is to put the cooldown in 187's CasterInfo (+0x70 → +0x14c). The
    client then refuses locally and shows the text. Keep the server-side refusal as the fallback.
- **Stat 53 cStatTeleportCooldown** (CalculatedStatDefinitions: base 0, max 100, HIDE_FROM_UI 1, HIDE_FROM_TOOLTIP 1,
  name 568293 "Teleport Cooldown"):
  - no client read of stat 0x35 was found (the stat getter vtable +8 called with 0x35, both through FUN_142ccb400 and
    directly) [P negative, pattern search];
  - sending 0 is right.
- **Still open:** only whether the 2015 removal also covered the Travel window's "My Friends" path. Its text speaks of
  the contact list. Nothing suggests a split [I].

---

## L183: mail stack cap of 9,000

- **Built:** Mail.cs:286-300 OverStackCap, setting MailMaxStack = 9000 (WorldServerConfiguration.cs:652), result 5
  AttachmentInvalid (562293).
- **Client [P, rechecked]:**
  - Mail.AttachItemToCompose FUN_1435ba0b0 → FUN_14363e9d0 copies the Lua arguments (owner type, container, slot,
    quantity, index, flag) straight into PacketMailAttachItemToComposeRequest (0xF8 sub 0x15), with no limit;
  - the compose renderer (as3 EmailComposeWindow AttachmentListItemRenderer.as:122-126) passes the dragged stack's
    quantity;
  - the client has no "stack too large" mail error: the Mail.Error.Compose.* codes are AttachmentInvalid, NoTrade,
    TooManyItemAttachments, and the others listed in CodeStringMappings.
- **Stack sizes:** materials have maxStack 1,000,000 (items.json: Copper Ore, Stone, Dirt …). So 9,000 is not a stack
  limit [P].
- **Sources:** the only one is wiki Trading.wiki:39 [W]. No forum or patch note mentions 9,000 (grep across forum/all,
  forum/patches, forum_raw) [P negative].
- **Verdict:** nothing to change. Next path for confirmation: the browser route to the archived wiki history of
  Trading.

---

## L184: live's trade delay on store purchases

### The period [P, developer]

- design/forum/all/2014-03-26-26036 "The Business of Landmark", Smokejumper [DEV], 26 Mar 2014:
  "Marketplace items are initially locked as NO TRADE for up to 12 hours after purchase".
- The same post: "no more than 12 hours after purchase, your item is unlocked for trading!"
- A player in 2015-04-23-54384:1920 says "12 - 24 hours" [W]. The wiki Trading says the same [W].
- **The value to use: `StoreTradeWaitHours` default 12** (WorldServerConfiguration.cs:635, now 24) [P dev 2014; I that
  2016 kept it]. "Up to" suggests a variable fraud check. A fixed 12 h is the live maximum.

### The client shows the time [P]

- **The tooltip reply carries it.** The item record reader FUN_142f17a30 (0.695 Inventory 0x12 ItemTooltipReply,
  FUN_142f17070 → record at +0x18) reads in order: …, u64 +0x108, u32 +0x110, **u64 +0x120**, u8 +0x128, str +0x130, ….
- The ItemTooltipInfo filler FUN_143763a20 copies record +0x120 → tooltip +0x178.
- The tooltip builders FUN_143768120 / FUN_14376a020 add the Lua field **"FraudTimer"** = that u64 when it differs from
  the zero-initialised global DAT_14548da10. The global is in .bss and has no writer in the decompile, so it is 0 [I
  strong].
- **The UI:** the item tooltip `TooltipItemDocumentClass.as:692-704` and the 0.695 CraftingWidget upgrade panel read
  `FraudTimer`. They compute `os_difftime(FraudTimer, os_time())` and show **UI.TradableInWithTime 562375 "Tradable in
  %TIME"**, refreshed by a timer [P]. **So the value is Unix seconds** of the moment the item becomes tradeable.
- **Where in our code:** `Network/Message/Model/InventoryTooltipReply.cs`. After the stats list the writer has
  `Write(0ul)` (+0x108), `Write(0u)` (+0x110), then **`Write(0ul)` = +0x120 FraudTimer**.
  - Pass the CharacterItemModel (or its ItemState) into `Build`.
  - Write `TradeableAfter` as Unix seconds while `Inventory.IsTradeLocked(item)`, else 0.
  - InventoryHandler.HandleTooltip already has the item.
- **By-product [I]:** +0x108 → tooltip +0x160 feeds "ExpirationTime" (a time left, from the tooltip builder FUN_14376a020
  around the ExpirationTime field). That is likely the overflow expiry countdown of audit-wiki-gaps #8. Read FUN_14376a020
  around 0x14376aac5 before using it.

### Still open (L184)

- Whether the item add / update packets (ClientItem) also carry the timer for the bag icon. The tooltip is enough for
  the player-visible part.

---

## L185: Player Studio review rules

### Rules on record [P, developer posts]

- **Update Notes 18 Jun 2014** (design/forum/all/2014-06-18-41434, Fairan [DEV], post #3):
  - reviewers had "previously purchased at least 1000 SC of items from the Marketplace";
  - online eligible players get "an in-game prompt asking if they would like to review";
  - rejection grounds: "lewd/offensive content, or copyright violations", the latter by name, object or description;
  - "no player will receive more than two opportunities to review an item per day";
  - "If your object is rejected, it is moved to a Customer Service queue". There Customer Service "will either uphold
    the player findings or override them";
  - a listing with no sale in 30 days is removed (it can be resubmitted).
- **26 Jun 2014** (2014-06-25-42258): the owner gets mail for the statuses Peer Review, CS Review, Approved, Rejected.
- **2 Jul 2014** (2014-07-02-42830): My Store statuses Active, Inactive, Review, CS Review, Rejected.
- **Wiki Player Studio** [W]: a reviewer who rejects on taste, against staff's verdict, "will stop getting items to
  review"; and anonymity.

### Client [P]

- **The vote codes.** Lua `ReviewProduct(approve, type, subtype)` (FUN_1435e9f30) maps to the verdict sent:
  - approve → **1**;
  - reject (1, 0) → 2, (1, 1) → 3, (2, 0) → 4, (2, 1) → 5, (3, 0) → 6, (3, 1) → 7, anything else → 0.
  - FUN_1432704a0 sends it (SubmitProductReview).
- **The UI** (as3 PlayerStudioItemReview: RejectionTypeDropDown / RejectionSubTypeDropDown) fills both dropdowns from
  data sources that the client never fills (Commerce.cs note). So on 0.695 a reject arrives as **0**, or as 2 when the
  default indices are passed [I].
- **No client text or packet gives the approval or rejection counts** [P negative: text_695, CodeStringMappings,
  the Commerce readers].

### Already built (Commerce.cs, WorldServerConfiguration.cs:274-309)

- the 1000 DBC minimum spend (PlayerStudioReviewerMinSpend);
- 2 reviews a day;
- an anonymous review copy (WriteProduct with an empty creator);
- the staff queue (PlayerStudioStaffReview on, admin page);
- one vote per product.

### Values / remaining design

- The counts stay **3 approvals / 3 rejections [I, ours]** until a source turns up.
- Keep the verdict code (2-7) on PlayerStudioReviewModel.Verdict, as is done now. Show it in the admin staff queue as
  the reason: type 1/2/3 × subtype. The type names are the empty data sources; label them "Offensive / Copyright
  (name) / Copyright (object or description)" [I from the dev post's three grounds].
- **Optional, live per the wiki [W]:** when staff overrides a rejection, flag the reviewers who rejected. After N
  overrides (a setting, default off) they get no more review prompts.

### Still open (L185)

- The thresholds. Next path: the archived Player Studio FAQ / help pages (landmarkthegame.com/player-studio,
  help.daybreakgames.com Player Studio articles) via the browser route. WebFetch on web.archive.org is refused.
- The 41434 thread is missing pages (98 of 158 posts captured in sibling threads). Later developer replies on counts may
  be there.

---

## Corrections found on the way

1. **Abilities.cs:577-585:** "no client code reads 562080" is wrong. Ui.TeleportToFriend shows it via
   AbilityCastError.OnCooldown (§L182).
2. **InventoryTooltipReply.cs:** the third u64 after the stats is FraudTimer (record +0x120). It is not padding
   (§L184).
3. **trace/systems/social.md:45 and social-service.md:168:** voice is "not needed". Reword as the clean-unavailable
   state with the decoded layout (§L181).
4. **WorldServerConfiguration.cs:631:** the comment "Its length is not in the client … wiki says 12-24" should cite the
   2014 developer post "up to 12 hours" (§L184).

## Files

- This report: `<workspace>\trace\0695\agent-leadsC.md`.
- Code referenced (not edited):
  - `LandmarkEmu\LandmarkEmulator.WorldServer\Game\StoreBundles.cs`
  - `…\Game\Pioneer.cs`, `…\Game\Claims.cs`
  - `…\Game\Abilities.cs`
  - `…\Game\Mail.cs`
  - `…\Game\Commerce.cs`
  - `…\Network\Message\Model\InventoryTooltipReply.cs`
  - `…\WorldServerConfiguration.cs`
  - `LandmarkEmulator.EnvServer.Core\Terrain\VoxelTemplate.cs`
  - `…\IEnvironmentServer.cs`
- Client data read: `<0.695 test copy>\CommonData\Voxel\templates\claim{1x1,2x1,3x1,3x3}.otd`.
