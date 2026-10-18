# L138 part C1: combat / EQN / PS2-origin client tables on 0.695

Agent report, 2026-10-17. Read-only and static. Sources: the 0.695 decompile (`re/decomp_all_695`), `gameData695`, the 0.695
`ActorCompositeEffectDefinitions.xml` (<0.695 test copy>\Resources), shipped Lua, `re/out` (rtti_695, text_695, fileindex_695),
`design/forum/patches`, `design/wiki`, and the server source (client-0.695). Nothing was run.
[P] = proven (function, table row, file:line, source). [I] = inferred (the reason is given).

## Summary

| Table(s) | Verdict | What the server must do |
|---|---|---|
| ProjectileDefinitions | **LIVE**, driven by the server through the Projectiles 0xEE family | Send 0xEE Create (sub 1) when a ranged shot leaves and Delete (sub 3) when it lands. Start with the Abomination spit (rows 54 / 83). |
| FireModes / FireGroups / FireGroupMappings / FireModeEffectGroups / FireModeProjectileMappings | **NOT REACHABLE on 0.695 as data.** The client code exists, but no weapon definition can exist. | Nothing to send. Correct the reasoning in the Grief.cs comment. |
| MerchantSets | **Client path reachable; not live in Landmark** [I strong]. | None. Keep it as a low-priority open lead. |
| HeroicMovement | **CLIENT-ONLY**: an Energy-cost gate the client checks itself. Every cost is 0. | None for the table. The 0x105 relay is already done. |
| KeywordDefinitions | **LIVE** as a display table for item and ability tooltips. The keyword ids come from server data. | Fill the CasterInfoReply keyword map for weapon attacks. **OPEN**: confirm the map's key/value meaning first. |

---

## 1. ProjectileDefinitions.txt

### Verdict: LIVE (the server sends it; the client draws it)

**Loader and owner [P].**
- FUN_142cacd40 (part_0063.c:26766) builds `Projectiles::BaseProjectileDefinitionManager`. FUN_142c59ee0 takes the tag "Projectile" and the file name "ProjectileDefinitions.txt".
- The manager is stored at client+0x314f0 → +0xf48 and in the global DAT_145439c80.

**Only reader [P].**
- The one read of DAT_145439c80 is in FUN_14318fbe0 (part_0088.c:31073). It calls FUN_14318f850, a hash lookup by id at definition +0x68.
- FUN_14318fbe0 is the Projectiles family handler. The zone dispatcher FUN_142d1ccf0 reaches it at `case 0x73: FUN_14318fbe0(param_1[0x6412], …)` (part_0066.c:15479).
- param_1[0x6412] is the `Projectiles::ClientProjectileManager`. It is ticked every frame by FUN_14318f990 (part_0066.c:10593).
- coverage-695.md lists the family as 0xEE, unchanged from 0.589.
- No other code creates a ClientProjectile [P]:
  - FUN_14318e900 (the pool create, factory hash 0xd1064f33) has a single caller, FUN_14318fbe0.
  - The ClientProjectile init FUN_1436d6830 has no direct callers; it is the vtable slot.
- So a flying projectile with a ProjectileDefinitions model and trail is only ever drawn when the **server sends 0xEE**.

**What the client does with a definition [P].** ClientProjectile init FUN_1436d6830:
- reads the definition's gravity (+0x24; above a threshold it re-aims through FUN_1436d5f00 with +0x28);
- reads MODEL_ID (+0x2c), resolved through the Models manager DAT_145439c58. The default is "Projectiles_Arrow_Arrow001.adr";
- spawns the model as an actor along the direction vector.

FUN_1436d70c0 handles Update. FUN_1436d6b60 handles the end: it stores a hit flag, the impact point and a target guid, and looks the target actor up through DAT_1454311c8. The client sends nothing back, so the projectile is visual only [I: no send in these paths]. The server keeps all hit timing.

**The 0.695 wire layouts.** The handler FUN_14318fbe0 reads them, header `u16 opcode, i16 sub` [P]. The read order and offsets come from pkt.py on the 0.695 readers [P]. The field meanings are [I] from how FUN_14318fbe0 passes each field on.

Sub 1 Create (reader FUN_14318ce10):
- u32 projectile id;
- u32 ProjectileDefinitions ID (+0x1c → FUN_14318f850);
- vec3 origin (+0x20);
- vec3 direction or velocity (+0x2c);
- u64 guid (+0x38 → projectile+0x20: the owner or target);
- bool (+0x40);
- u32 (+0x48);
- vec3 (+0x4c);
- u32 (+0x44);
- u64 (+0x58);
- u32 (+0x60);
- vec3 (+0x64);
- u64 (+0x70).

The handler then sets projectile+0xcc = 2 and files the projectile under its id in 32 buckets.

Sub 2 Update (reader FUN_14318d480): {u32 id, vec3, vec3} → FUN_1436d70c0.

Sub 3 Delete (reader FUN_14318d320): {u32 id, vec3 impact point, u32, bool hit, u64 target guid} → FUN_1436d6b60. Afterwards the manager removes the projectile.

Sub 4 Debug (reader FUN_14318d160): sets debug fields. coverage-695 lists Debug with sub "–", but the handler takes it at sub 4 [P].

**Landmark data uses it [P: rows and names joined to the 0.695 ActorCompositeEffectDefinitions.xml and Models.txt].** The full join is in this report's scratch (proj_names). The rows reference Landmark-era content, and the models ship in the 0.695 packs (fileindex_695.tsv):

| Rows | What they are |
|---|---|
| 54, 83 | Abomination skull toss: model 1396 FX_Projectiles_..._Affliction_Skull_01, bone Tongue_05, impact 11625 PFX_Character_Abomination_SkullToss_Impact. This is the same impact effect the server already plays (Creatures.cs:137). |
| 41, 42 | FX_Projectiles_Rocks_CatapultRock_Firey. The wiki "Highfall Catapult" page describes it launching burning rocks at distant targets [P]. |
| 14-17, 37-40 | ScorpionBush stinger, bones VineA-H_12, impact PFX_Character_ScorpionBush_Attack_Impact. |
| 22, 27-36, 58 | FX_Projectiles_Staff_BasicAttack. |
| 23-26, 45, 46, 48, 56, 57, 68-82 | Bow arrow trails (Bow_Attack01/02 and the EQNPrototype BowShort / BowGreat quick, heavy, dash and counter attacks). |
| 85-92, 111, 119 | Arrows, one per affinity: Physical, Affliction, Fire, Lightning, Shadow, Ice, Radiance, Water. |
| 93-110, 112-121 | Bolts, one per affinity, plus the Radiance wand ability (row 112). |

**Design sources [P].**
- 26 Mar 2015 notes: wisps easier to hit "with projectiles".
- 1 Apr 2016 hotfix: projectiles "no longer sometimes appear to target the opponent's feet instead of their chest". This is an aim point the server chose for the Create packet [I].
- 25 May 2016: the Raygun's basic-attack projectiles get "a lower arc".
- KeywordDefinitions rows 23 "Projectile" and 60 "Ranged-SlowProj".

**PlayerUpdate LaunchProjectile 0x31** is a different, empty path (`ret 0`, OPEN-LEADS L139 / L140). The server's existing note in Creatures.cs:1101-1110 ("No projectile packet is sent … the release and impact effects are what shows the shot") did not know about 0xEE. Correct it.

### Server changes
1. **New `Network/Message/Model/Projectile*` messages** for family 0xEE: Create (sub 1), Update (sub 2), Delete (sub 3), using the layouts above. Mark the meaning of each unnamed field [I] in code until a client test confirms it.
2. **Abomination spit** (Creatures.Spit / ResolveShots).
   - At release: send Create with def 54 (or 83 for the small variant), origin = the creature eye, direction = the aim, guid = the creature (or the target; test which).
   - Interest range: the creature's viewers.
   - At impact: send Delete with the impact point, hit flag and target guid. The server already times arrival as distance / SPEED 25 [P: ProjectileDefinitions row 54].
   - Keep the release / impact composites until a test shows whether Delete plays COLLISIONEFFECT_ID itself (FUN_1436d6b60 stores the hit; the effect is not yet traced) [I].
3. **Player ranged weapons** (WeaponCombat Pierce / FirstHit / AreaAtAim attacks for bows, staves, wands and rayguns).
   - Send Create / Delete with the row that matches the weapon's affinity and attack (85-121).
   - The weapon → row mapping was server data and is lost. Choose rows by model and affinity name, and mark the choice [I].
   - Hits can stay instant, or be delayed by distance / SPEED. Delaying them matches live better [I].
4. **Other creature shots** (catapult 41/42, scorpion bush 14-17/37-40) belong with the creatures, once those creatures exist.
5. **Bandwidth:** one Create and one Delete per shot, sent only to viewers in range. Never send Update unless the course changes (homing).

**Try next (client test, 0.695 copy).** Send one Create for def 54 toward the player.
- Confirm that +0x2c is a direction and not a target point, and what the u64 at +0x38 must be.
- Send Delete and confirm whether the impact composite plays.

---

## 2. FireModes / FireGroups / FireGroupMappings / FireModeEffectGroups / FireModeProjectileMappings

### Verdict: NOT REACHABLE ON 0.695 as data (the client code exists; no weapon can use it)

**Loader [P].** FUN_14340fc00 (part_0103.c:26139) is called from FUN_14340fd10, the `ClientWeaponDefinitionManager` load. It loads, in order:
- the weapon table (first call; Weapons.txt);
- WeaponAmmoSlots.txt, FireGroups.txt, FireGroupMappings.txt, FireModes.txt, PlayerStateProperties.txt, FireModeProjectileMappings.txt.

The manager is constructed in FUN_142d12360 (part_0066.c:8810) and stored in DAT_145431a78.

**The 0.695 data is empty or placeholder [P: gameData695]:**

| Table | Contents |
|---|---|
| Weapons.txt | 0 rows |
| FireGroupMappings.txt (WEAPON_ID → FIRE_GROUP_ID) | 0 rows |
| WeaponAmmoSlots.txt | 0 rows |
| FireModeProjectileMappings.txt | 0 rows |
| ClientItemDatasheetData.txt (WEAPON_ID, FIRE_GROUP_ID) | 0 rows |
| DeployInfo.txt (FIRE_GROUP) | 0 rows |
| FireGroups.txt | one row, id 1, with PRIMARY / SECONDARY / TERTIARY / QUATERNARY_FIRE_MODE_ID all 0 |
| FireModes.txt | one row, id 1. Non-zero fields: AUTOMATIC 1, EFFECT_GROUP 1, FIRE_DETECT_RANGE 50, and the 1.0 scalars (COF, turn / move modifiers, zoom, ARMS_FOV). GRIEF_IMMUNE and SPRINT_FIRE are 0. |
| FireModeEffectGroups.txt | one row: group 1, tag "cooldown", effect 0 |

FireModes row 1 is reachable only from a fire group that lists it, and no fire group does [P]. A fire group is reachable only from a weapon, through FireGroupMappings or WeaponAddFireGroupPacket, and no weapon definition exists [P: Weapons.txt has 0 rows]. The rows look like a compiler default, not authored data [I].

**No server-fed weapon definitions on 0.695 [P: rtti_695].**
- The only ReferenceData packets are ReferenceDataPacketItemClassDefinitions, ItemCategoryDefinitions and ClientProfileData. None of them carries weapons.
- The weapon packets (WeaponAddFireGroupPacket, WeaponFirePacket …, family 0xBA) were never received in play (trace/systems/abilities-combat.md "Combat 0x24 and Weapon 0xBA").
- Landmark weapons are item abilities driven by input action groups (casts 760 / 761: WeaponCombat.cs header; Abilities.HandleCast).

**The weapon-use checks** FUN_143335b30 / FUN_1433360e0 / FUN_1433369c0 are PlanetSide-style firearm checks: "in ironsights", "player is sprinting", "primary weapon is reloading / chambering". They are vtable methods; only FUN_1433369c0 has a direct caller (FUN_143336ae0) [P].
- They act on the weapon that FUN_143331110 returns for the current slot (param_1[0xa24]).
- That object exists only when a ClientWeapon was built from a ClientWeaponDefinition [I: nothing else fills the weapon list; no definitions exist].
- So the grief check FUN_142e93a40 on fire mode +0x1b0 never sees a Landmark weapon.

**Proposed server change (comment only).**
- Grief.cs:46-55 currently concludes "FireModes.txt has one row (id 1) with GRIEF_IMMUNE 0, so every weapon attack is blocked". The table cannot carry that conclusion on 0.695.
- Keep the server rule that equipment-locked players cannot attack, but cite a design source for it, or mark it [I] as ours. Do not cite FireModes.
- No packets to add.

**Shared-engine note.** The code is not dead: a server could create weapons through the 0xBA packets. It is the data that makes the system unusable on 0.695, so classify it "parked" (as REACHABLE-GAPS / verdicts.tsv do for WeaponSwitchFireModeRequest etc.), not "unused".

---

## 3. MerchantSets.txt

### Verdict: client path reachable [P]; not live in Landmark [I strong, from design sources] → keep as a low-priority open lead

**Loader [P].** FUN_143bd1dd0 loads "MerchantSet" / "MerchantSets.txt" into `Merchant::MerchantSetDefinitionManager`. It is called in FUN_142cacd40 (part_0063.c:26645) and stored at +0x97010 and in DAT_145439c88.

**Data [P].** There are 2 rows, both NAME_ID 562272 "Generic Merchant" (text_695).

**Only reader [P].**
- FUN_143567820 reads DAT_145439c88 (part_0110.c:15200). It is the **CoinStoreMerchantListPacket** handler, case 0xA of the CoinStore dispatcher FUN_143567bd0 (family 0x9E).
- Its body, by pkt.py on FUN_14355f470, is {u32, u64, u64, list, u32 merchant set id, list}.
- FUN_143bd2830 maps the merchant set id to its NAME_ID for the merchant window's title. The handler logs "Received a list of %d item%s to sell" and fills the window.
- MerchantHandler.lua ships with 0.695 (Show / Hide / Buy / Sell / BuyBack through CoinStoreScreen). MerchantWindow.gfx is in Assets_173.pack (fileindex_695) [P].

**Design sources say no NPC merchant shipped [P sources; verdict I].**
- 24 Apr 2014 notes: Pockets went to the Showcase "once our Merchant and Currency systems are implemented", i.e. the system was not implemented yet.
- 1 Apr 2016 hotfix: the Vendor Booth's old UI was removed; it was "not being developed for Landmark".
- 16 Jun 2016 thread: players still ask for a way to sell to others "via the merchant stalls".
- The wiki dump has no merchant NPC page.
- LandmarkEmu/docs/vendors.md already records "not built: no merchant NPCs".

**Server change:** none.

**Try next:**
- the 28 Sep, 11 Oct, 14 Oct and 6 Dec 2016 notes (none mention merchants — confirmed by grep for merchant / vendor);
- any 0.695 Lua or UI path that fires EVENT_OPEN_MERCHANT (hudstate.lua:3475) from a non-CoinStore source.

If none exists, close it as "not live".

---

## 4. HeroicMovement.txt

### Verdict: CLIENT-ONLY (an Energy-cost gate the client checks itself; every cost in 0.695 is 0)

**Loader [P].** FUN_143c1d220 loads "HeroicMovementDefinition" / "HeroicMovement.txt" into the static `ClientHeroicMovementDefinitionManager` DAT_14543d1b0. FUN_140824240 is its static initialiser. The getter is FUN_142df7f50 (it returns DAT_14543d128).

**Use [P].**
- FUN_142df7c10(name) hashes a code name and calls FUN_142df7c80(hash).
- FUN_142df7c80 looks the definition up. When COST (+0x34) ≠ 0, it reads the player's resource of type 2 (CharacterResources.cs:30: EnergyType = 2 [P]) and refuses when Energy < COST, unless COST_TYPE (+0x30) is 2 (cost over time).
- It also refuses when DAT_145438918+0x58 ≠ -1 [state not traced].

**Callers [P]:**
- the movement code (part_0081.c:26585-26609 and 31820-31844 check "Glide" and "HeroicMovement");
- FUN_143200930;
- FUN_143325430 (hash 0x9055911d) and FUN_143325480 (hash 0x80d81da0), combined with FUN_142e934c0;
- FUN_1433255b0, FUN_143329c60, FUN_14358d760.

**Data [P].** There are 14 rows (Jump, Double Jump, Glide, grappleRein, Surface / Submerged swimming, GrappleFire, HeroicMovement, Teleport, Dash, Roll, JumpJet, LedgeGrab, LedgeGrabPull). **COST is 0 in every row**, so the gate always passes.
- "Dash", "Roll" and "JumpJet" are marked "(not hooked up)" in the table's own DESCRIPTION column. Landmark's boot dash and roll go through ability casts instead (BootAbilities).

**Design [P].** Grapple, glide and heroic movement were live (a grapple-while-gliding fix in the 2016 notes). No source gives an Energy cost for them.

**Server:**
- No table work is needed.
- The 0x105 relay is already in place (WorldSession.cs:1450-1456, PlayerManager.RelayHeroicMovement). It covers grapple fire, swimming and hook-effect end.
- Cast 326 is answered as instant with no cost (MiscHandler.cs:469-480, Abilities.cs:177).
- If an Energy cost for gliding or heroic movement is ever wanted, it must be server-side resource drains. Changing this table would change the client files, which is not allowed.

---

## 5. KeywordDefinitions.txt

### Verdict: LIVE (a display table for item and ability tooltips). The keyword ids come from server data. The ability side is partly OPEN.

**Loader [P].**
- FUN_142d8fc20 (part_0068.c:14648) builds `KeywordDefinitionManager`. Its file-name method FUN_143cbe680 returns "KeywordDefinitions.txt".
- It is stored in DAT_14543b478 and registered as "Keyword" in the named-manager registry (FUN_143cd5ce0).

**Readers [P]:**
- `Tooltips::ItemTooltipInfo` / `ItemTooltipInfoPocket` slot FUN_143763a20 (part_0120.c:21487). It takes a keyword list at +0xd0 of its item record.
- `Tooltips::AbilityTooltipInfo` slot FUN_14376c210 (part_0120.c:26648). It takes the list at +0x128 of the client ability record that FUN_142e160e0 returns (the player's ability list at DAT_1454311c8+0x32070).

For each keyword id, the tooltip:
- looks it up (definition id at +0x30);
- skips it when HIDE_FROM_CLIENT is set (+0x18);
- uses the field at +0x14 together with the UiColorDefinition manager DAT_1454319e8, i.e. the keyword name in its UI colour [I: NAME_ID / UI_COLOR_ID; not split].

**Data is Landmark-live [P].** There are 81 rows.
- Visible: the seven affinities with icons (Fire, Water, Shadow, Radiance, Affliction, Earth, Lightning), plus Primal 52 and Pet 86. Pet is new in 0.695 (tables-diff: KeywordDefinitions changed 80 → 81; spec-pets.md).
- Hidden tags include the 2016 affinity properties Burn / Surge / Flow / Supercharge / Frenzy / Electrocute / Energize / Regrowth / Infect / Rejuvenate / Harden / Rot / Torment / Condemn / Illuminate / Exalt (WeaponCombat's status set), plus Basic / Power Attack, Charged, Projectile, Ranged-SlowProj and the weapon types (Bow, Wand, Staff …).
- A row added in the 0.695 era means the table was maintained while live [I].

**Where the ids come from.**
- **Abilities [I strong]:**
  - The client ability record is the CasterInfoReply body shifted by 0x18: record +0x8 / +0xc are the reply's cost resource / amount at +0x20 / +0x24, which AbilityTooltipInfo reads as cost type 1 / 2 [P read].
  - So record +0x128 is reply +0x140, which falls inside the "int map" the reply reader builds at +0x138 (FUN_142c34f90: count, then u32 key and u32 value pairs; pkt.py on FUN_142e016b0).
  - The server writes that map empty (AbilityMessages.cs, `writer.Write(0u); // int map`). The captured live replies (MiscHandler.cs:361-366: lightstone 221, 1098, 326, 1122) are tool abilities with an empty map, so live combat casts are uncaptured.
- **Items [OPEN]:**
  - The +0xd0 that ItemTooltipInfo reads is not the item definition's +0xd0, which is a plain u32 in the 0.695 reader FUN_142c176d0 (pkt.py).
  - So the tooltip's param_2 is another record (the merged tooltip data). Its source is not traced.

**Server change (after confirming):**
- In `Abilities.BuildItemAbilityInfo` for weapon attacks, write the weapon's affinity keyword (1-7, 52) and the attack's tags (27 / 28 / 33 / 23) into the CasterInfoReply int map. These are the keyword ids the client then shows in the ability tooltip.
- Only do this once the key/value roles are confirmed.

**Try next:**
1. Decode the map node layout (FUN_142d2ee50 / FUN_142afbb00) against the tooltip walk in FUN_14376c210 (`*puVar14`), to tell whether the keyword is the key or the value.
2. Client test on the 0.695 copy: one CasterInfoReply for cast 760 with map {1 → 1}, then read the ability tooltip text.
3. Find the caller that fills ItemTooltipInfo's record (the vtable slot that passes param_2 to FUN_143763a20) for item keywords.

---

## Open leads to add (for the parent agent; OPEN-LEADS.md was not edited)
- **L139 update:** the visible projectile path is the 0xEE family (FUN_14318fbe0, ProjectileDefinitions), not PlayerUpdate 0x31. Layouts are in §1; the meanings of +0x2c / +0x38 / the trailing fields need a client test.
- **Weapon → ProjectileDefinitions row mapping** for player bows, staves, wands and rayguns. This was server data; choose by affinity and model [I].
- **Keyword map in CasterInfoReply** (§5 try-next 1-3).
- **MerchantSets:** close as "not live" after the final notes sweep (§3).
- **Grief.cs comment:** re-source the equipment-lock rule (§2).
