# Prop collision: project history + 0.589 / 0.695 client path (agent collision-history, 2026-10-17)

Read-only research. [P] = proven from code / logs / commits, [I] = inference. Addresses: 0.589 = re/decomp_all,
0.695 = re/decomp_all_695 (mapping from trace/0695/functions.tsv, checked by reading both).

## A. What the project already learned (chronological)

| When / commit | Finding | Fix / outcome |
|---|---|---|
| 10-08 (MODLOG 172-180) | AddLightweightProp prop part read as "collision block (u8 type 1 + u32 flags)". | Corrected 10-09: that block is the movement / physics-body mode (types 0-5 with shape floats, entity +0x2b0, vtable +0xd0), **not** model collision [P, MODLOG 698-701; docs/protocol.md 283]. |
| 10-09, 68e185b | Prop collision comes from the model's `.adr` (`CollisionType` + `CollisionData fileName=X.cdt`). Trees 344 / 350 have no `.cdt` → players walked through. | Those two models dropped from spawn lists. |
| 10-10, 21c45d3 / f8e2282 | Spire = stamped `.otd` template + 7 template props; Spire travel from another landscape re-zones to the home continent. | — |
| 10-11, 7eac1ed (03:37) | Arrival +6 m Z was inside the Spire pillar; "the client pushed players out under the platform"; client ground snap put players on natural terrain under the stamp. | Arrival offset (7.2, -0.5, -6.5), no ground snap on the Spire footprint (source of the Spire.cs:128-129 comment). |
| 10-11 helper trace (MODLOG 1136-1137) | "client path identical to trees, no packet field gates physics; model collision only 4 pillars ~30 m out — middle open by design." | **Wrong about the mesh**: the 10-17 `.cdt` read (docs/terrain.md "Collision meshes") shows dais at pivot -1.537, deck -2.924, arms -1.144, ramps, legs [P]. |
| 10-11, 8352e5a (03:59) | Spire props never got LightweightToFullProp: stayed lightweight (+0x147c = 2), no InteractionInfoRequest. User: "spire has no collision". | Spire.SendFull answers FullCharacterDataRequest. |
| 10-11, a879085 (05:55) | "re-zone builds a fresh voxel engine that meshed the destination before it had the manifest ... stamped cells (Spire platform) had no collision". | Manifest sent right after ClientBeginZoning + ZoneDetails (Teleport.cs:255-258). **Verified: lands at 1203.7 and stays** (was 1184.19 = natural ground). 0.589 client, home continent. |
| re-read of that test | Spire.cs:136 now says 1203.70 = pivot -1.537 = the **dais top of the Hub prop's `.cdt`**, and both Spire templates hold no solid voxel (docs/terrain.md 461-463) [P]. | So the 10-11 player stood on **prop** collision; the "stamped cells had no collision" explanation does not fit. The manifest fix most likely worked by making the loading screen last until the terrain streamed, giving the props time to get collision [I]. |
| 10-12, aba2bea | Trees / plants also answer full-data requests ("collision, interaction"). | — |
| 10-12 (MODLOG 1408) | User "dropping through the Spire, its collision is gone": **not reproduced** (TestPlayer2 arrives and stands on the platform; full-prop requests answered). 0.589. Scenario re/test/scenarios/spirefall.json = `@TestPlayer2 spire` + 2 screenshots (no pass check; .last 10-14 "0/0 passed"). | — |
| 10-14 (TESTING-NEEDED 1491-1495) | 0.589 test clients (shared install, cache off, slow loads): "after Spire travel a client sometimes stood on the ground 20 m under the platform". | Left for the user to check. |
| 10-16, 750de51 | A Spire on every landscape → arrivals at a non-home Spire from another landscape exist from here on (0.695 only). | — |
| 10-17 L100 (agent-leads3-b.md 255-275), effe77a | Spire in a ~21 m pit; landscape 395 arrival fell to the floor (y 1088.2) "deck's collision not there yet". | Floor on ground, and **LightweightToFullProp sent right after each AddLightweightProp** (handler needs only the +0xcb4 flag). |
| 10-17, acf3ed7 (18:51) | First visits fell while the props were built in Enter. | Props built with the stamp, before the traveller moves. |
| 10-17, 4c5a7c6 (19:00) | Arrivals from another landscape fell (395 / 396 from 391). | Spire props + full sent **before** the zone-done flags. Still fell. |
| 10-17, b2ad107 (19:10) | — | DoneSendingPreloadCharacters with its wait flag set. Still fell (covers only characters). |
| 10-17, 6daee78 (19:21) | — | Zone-done flags held 4 s on a Spire arrival. Still fell (395 / 397 / 398). |
| 10-17, e6e6c58 (19:44) | — | Dev `spirefull on|off`; log line when the client asks for a Spire prop's full data. |
| 10-17, ffed92f (19:56, HEAD client-0.695) | "An in-place re-zone keeps the client's entities, and sending a Spire prop again then cost it its collision." | Spire props sent only when `RezoneChangedLandscape`; all hold / wait experiments undone. |
| other | Moving platforms (agent-e-leads.md 38-64, agent-props2.md 98): ProxiedProp re-registers collision ≤ 1 Hz after 0x7F moves (FUN_1430ab000 +0x4F00 / +0x4F08). | — |

**Did props ever have collision after a cross-landscape arrival in a verified test? No record of one.** [P, from
MODLOG / TESTING-NEEDED / commits] The verified "stands on the deck" cases are 0.589, home continent:
a879085 (10-11, 1203.7) and the 10-12 non-reproduction (dev `spire` from the home landscape; whether the traveller came
from another landscape is not recorded). Every 0.695 cross-landscape Spire arrival tested on 10-17 fell
(395-398 from 391). 0.589 also showed occasional falls on slow loads (10-14).

**Conflict to resolve [P code vs note].** ffed92f (and Teleport.cs:126-130, 249-250) say an in-place re-zone keeps the
client's entities. The client code says otherwise: ClientBeginZoning FUN_142cc6ed0 (0.695 FUN_142d60340) calls
FUN_142ee4930(mgr, localPlayer) (0.695 FUN_142f83590) **unconditionally**; it removes and destroys
(FUN_143aaefc0 → vfunc +8 with 1) every entity in the guid map except the local player, for in-place and landscape
change alike. Only the terrain reset is gated by the "same zone" flag (param_9; see B.6). PlayerManager.Reintroduce's own
comment (PlayerManager.cs:143-147) agrees with the code. So if an in-place re-zone with no Spire props re-sent still
gives deck collision, the collision comes from objects that outlive the destroyed entity [I]. A landscape change would
then clear them through its extra resets [I]. In that case "in-place works" does not show that newly sent props get
collision. Worth checking with Frida: entity count / FindByTransientId for a Spire transient id after an in-place
re-zone, before anything is re-sent.

## B. How the client makes a prop collidable (0.589, same structure in 0.695)

### B.1 Spawn: AddLightweightProp
- Handler **0.589 FUN_142c44700** ("BaseClient::HandlePlayerUpdateAddLightweightProp", part_0065:8743) = 0.695
  FUN_142cde1c0 [P].
- It calls SpawnProxiedProp **FUN_142eebb10** (0.695 FUN_142f8a800). For a new guid: alloc 0x4ee0, ProxiedProp ctor
  **FUN_143002810** (0.695 FUN_1430a7760), then thunk_FUN_142e280f0 (model / actor init), then insert into the guid map
  (mgr +0x30). For an existing guid it **reuses the entity**: no new actor, no init [P].
- Ctor defaults: **+0x4E20 = 1** (collision enabled; 0.695 +0x4E50), u16 at +0x4E6C = 1 (+0x4E6C "enabled" = 1,
  +0x4E6D "first actor update seen" = 0; 0.695 +0x4E9C / +0x4E9D), body [0x977] = shape [0x976] = 0 (0.695 [0x97D] /
  [0x97C]). The 0.695 layout is the 0.589 one shifted by +0x30 from +0x4B20 [P].
- **Gate [P, both builds]:** `if (entity == 0 || EntityManager(+0x31e60)->+0x282c8 == 0) return 0`. +0x282c8 is the
  local player (its guid is compared with the prop owner at packet +0x2a0). The rest of the handler is skipped when it
  is 0:
  - +0x9cd model id;
  - FUN_142cbaca0 (0.695 FUN_142d54040) base apply;
  - the physics-body block (vtable +0xd0 = thunk_FUN_14344e400);
  - **+0xcb4 = 1** (the lightweight flag);
  - FUN_1430054c0 (packet +0x340 state lists; 0.695 FUN_1430ab830);
  - interaction range +0x4118;
  - FUN_143005b30 (+0x4e6c enabled from packet +0x490; 0.695 FUN_1430abea0);
  - vtable +0x328.

  With +0xcb4 = 0, a later LightweightToFullProp is ignored (see B.4).
- Who sets and clears +0x282c8 (FUN_142eeb250): set by SendSelf FUN_142c18180. Cleared by:
  - RemovePlayer for the own guid (PlayerUpdate_Dispatch);
  - "Failed to load local player actor" (FUN_142c76580);
  - the entity clears FUN_142ee4930 / FUN_142ee4d70.

  ClientBeginZoning **keeps** it [P]. So the gate should pass after a re-zone, unless something in the landscape-change
  path removes the local player [I: not seen; server RemovePlayer excludes self, PlayerManager.cs:200-205].

### B.2 Model collision: Actor::CreateCollision (the `.cdt` shapes)
- Actor (RTTI "Actor") vfunc **+0x188 = FUN_143c49550**; BaseActor's is FUN_143c49b80 [P, rtti.tsv slot 49].
- Gate: actor +0x42F bit 0x10 clear (collision not disabled) **and** model data actor[0x69] (+0x348) +0xD40 != 0. +0xD40
  is the collision resource loaded with the model's `.adr` / `.cdt`.
- The collision type hash is model[0x6d] +0xB88. Default -0xa741e34; -0x36a3f94f is a special case. Groups come from
  the CollisionTypeManager FUN_143c1a160.
- Shapes are made through the physics world **DAT_14533f438** (0.695 DAT_14541e9b8), using its SDK at +0x88 (if 0,
  nothing is made). Objects are stored in actor +0x380 / +0x388 and registered with FUN_142ae3cb0(world, obj, 1).
  Then the owner listener (actor +0x340 = the entity's 0x250 sub-object) gets slot +0x60.
- Called from:
  - actor model / LOD-loaded **FUN_143c48780**: `if ((actor+0x3b8 & 0x20) && model+0xd40) vfunc+0x188`;
  - FUN_143c08c20 / FUN_143c08dd0;
  - re-enable FUN_143c407b0(actor, 0) (0.695 FUN_143cff670): clear bit 0x10, then +0x188. Disable (1) sets bit 0x10
    and calls FUN_143c49ee0 (destroy).

  Only FUN_143003990(entity, 0), FUN_143047330 and FUN_143577410 ever disable it [P].
- **Nothing on this path reads the lightweight flag +0xcb4** [P: all +0xcb4 readers listed. FUN_142dcbf20 is the
  character controller's capsule for non-lightweight *characters*; FUN_142ee8540 is the full-data request scheduler,
  limit mgr +0x282dc; the rest are PC / zone code]. So "a lightweight prop has no collision until it is full"
  (Spire.cs:767, 796) is **[I], not proven**. A full conversion re-runs the appearance (B.4), which re-triggers B.3.

### B.3 ProxiedProp registration (the entity side)
- Actor per-frame Update (vfunc +0x28 = **FUN_143c4f1a0**):
  - returns early while the model data is missing;
  - first run sets actor +0x42D bit 0x20 and calls vfunc +0x180;
  - every run calls listener slot **+0x88**.
- vfunc +0x180 = ReadyCheck **FUN_143c491d0**: needs +0x310 == 0, +0x42D bits 0x20 | 4 | 2 and scene node +0x368 != 0,
  then calls listener slot **+0x70**.
- Bit 4 is cleared by FUN_143c4f020(actor, 1), on full conversion / appearance rebuild. It is set again by
  FUN_143c4f020(actor, 0), from FUN_143b6c550 (appearance done), FUN_142fa2650 and FUN_143287490.
- ProxiedProp listener (vtable at entity +0x250):
  - slot 0x70 = **FUN_143004870** (0.695 FUN_1430aab50): `if (entity[0x977] body == 0) FUN_143006020`;
  - slot 0x88 = **FUN_1430048e0** (0.695 FUN_1430aabc0): once, sets +0x4E6D = 1, then FUN_143006020.
- **FUN_143006020** (0.695 FUN_1430ac390), the prop's collision setup:
  - requires +0x4E20 (enabled), actor = vfunc +0x58, and actor +0x348 != 0;
  - if model +0xD40: FUN_143c407b0(actor, 0) (re-enable / create the `.cdt` collision);
  - the 0x758 object, if any, gets vfunc +0x78;
  - needs the current LOD (+0x2b4 into +0x188 list) loaded (+0x10);
  - bounds FUN_143005d60;
  - then, if **+0x4E6D && (no 0x758 object || its vf+0x170)**: builds a separate body. Shape [0x976] from actor
    vfunc +0x128 (FUN_143a4d630 / FUN_143a4d700); body [0x977] via FUN_143a4aab0, flags 0x1000004 / 7, or 0x2000 when
    FUN_142ee8050(mgr, guid). It is added with group **2, or 0xC when +0x4D68 != 0** (FUN_142ae3cb0).
- **Pose / scale / group refresh FUN_143003230** (0.695 FUN_1430a82a0): for the actor's collision object
  (FUN_142c31fc0 = actor +0x388 - 0x18; 0.695 FUN_142ccb9e0) and the body [0x977], it sets the transform from
  entity[0x30] vf+0x10, the scale FUN_143c433a0 and the group. Called from ProxiedProp update FUN_143004d20 (0.695
  FUN_1430ab000) when the stored transform +0x4DE0.. differs, and from the placement code.

### B.4 LightweightToFullProp
- Handler **0.589 FUN_142c459f0 → FUN_142c45940** = 0.695 FUN_142cdf4b0 → FUN_142cdf400. The code is identical [P].
- Steps:
  1. find by transient id;
  2. **require +0xcb4 != 0**;
  3. if there is no appearance string (packet +0x40 < 1): FUN_142e1f070(entity, 1) (0.695 FUN_142ebdae0). That
     rebuilds the appearance composite and clears actor bit 4 until it is done; then B.3 runs again;
  4. else FUN_142df77b0 + FUN_142e1f070(entity, 0) + FUN_142dfbcf0;
  5. FUN_142cb9f50 full apply (0.695 FUN_142d532f0: +0x147c etc.);
  6. +0xcb4 = 0;
  7. prop part: FUN_142cb57a0 (0.695 FUN_142d4eab0) only stores a string at +0x4E68.
- No collision call in the packet path itself [P].

### B.5 `.cdt` cache
- **0.589 FUN_142ee6300** = 0.695 FUN_142f84f60 ("same"): an EntityManager-owned cache keyed by a name hash (0x800
  buckets at mgr +0x28310, entries +0x282e8, parse FUN_143b06740). No direct callers (vtable).
- FUN_142ee4930 does not clear mgr +0x282e8 [P: it clears +0x180b8 / 0x8068 / 0x10090 / 0x200f0 / 0x28118-0x281a0 /
  0x281e0]. So the cache survives zone changes [I].
- APEX `..\CommonData\Collision\%s` loader FUN_142b16e30 is unrelated (DynamicSystem .apb / .apx).
- "TileStatic" / "TileSeamless" (FUN_142cb9af0) and "Tile Create Collision" (FUN_1436fed50) are terrain mode / voxel
  tiles, not props [P].

### B.6 What a zone change resets
- Physics world DAT_14533f438 lasts as long as the app: set in BaseClient::CreateAppServices FUN_142c12f60 and cleared
  only in FUN_142c22b30. It is not per zone [P].
- **Zone_DispatchPacket, ClientBeginZoning:** "same zone" cVar8 = zone id == +0x31fa8 && name == current && world u32 ==
  +0x378cc. If different: FUN_142c0eee0 (terrain world). Then **FUN_142cc6ed0** (0.695 FUN_142d60340):
  - fires GameEvents:OnPlayerBeginZoning;
  - if **not** same zone: terrain reset FUN_1430ade40(game+0x314f0 +0xF20, 1, 0) (0.695 FUN_1431589a0) and
    FUN_1431550c0(DAT_145351db8) (0.695 FUN_1431ffae0);
  - **always**: FUN_142ee4930(mgr, localPlayer) destroys all other entities (props included);
  - then FUN_142cbf940 → run state WaitForZoneLoad.
- So after a landscape change every Spire prop is a **new** ProxiedProp, whose collision needs B.2 + B.3 on the new
  actor: model / LOD load, the first actor Update, ReadyCheck with a scene node (+0x368). After an in-place re-zone, a
  re-sent guid would also be new (the entity was destroyed). That is why the "keeps entities" claim needs the check in A.

### B.7 0.589 vs 0.695 differences on this path
1. **FUN_1430ac390 (0.695) adds an else branch** [P]. When no separate body is built (+0x4E9D == 0, or the 0x758
   object's vf+0x170 is false), 0.695 calls FUN_1430a82a0, which re-applies the actor collision's pose / scale / group
   in the world. 0.589 FUN_143006020 does nothing in that case.

   0.695 also ends with +0x4F00 = 0 and +0x4F08 = now. That is the ≤ 1 Hz moved-prop refresh in FUN_1430ab000, new
   since 0.589.
2. Everything else on the path reads the same apart from shifted addresses / offsets:
   - AddLightweightProp gate and order;
   - SpawnProxiedProp reuse;
   - listener slots 0x70 / 0x88;
   - Actor::CreateCollision gate;
   - LightweightToFull handlers;
   - begin-zoning entity clear (both builds keep only the local player);
   - `.cdt` cache.

   The functions.tsv "changed" marks on these are address-only, except item 1 [P for the functions read here; the body
   of FUN_143c49550 vs its 0.695 twin was not compared line by line].

## C. Leads for the 0.695 trace (not tested; no game / Frida run here)
- On a landscape-change arrival, hook the 0.695 twins of FUN_143c491d0 (ReadyCheck), FUN_143004870 / FUN_1430048e0
  (= 0.695 FUN_1430aab50 / FUN_1430aabc0) and FUN_1430ac390 for a Spire transient id. Each missing call names the
  blocking gate:
  - model / LOD not loaded;
  - scene node +0x368 = 0;
  - +0x4E50 = 0;
  - actor +0x42F bit 0x10 set;
  - EntityManager +0x282c8 = 0 at AddLightweightProp time (then +0xcb4 stays 0 and LightweightToFullProp is ignored).
- Check whether the Hub actor's `.cdt` objects (actor +0x388) exist and are in DAT_14541e9b8 after arrival.
- Check the "in-place keeps entities" conflict (A) before relying on in-place behaviour as the reference.
