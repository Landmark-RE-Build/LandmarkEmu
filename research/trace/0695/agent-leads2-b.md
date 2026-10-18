# Open leads, second pass, group B (client systems), 2026-10-17

Sources: the 0.695 decompile (re/decomp_all_695), RTTI (re/out/rtti_695.tsv), luabind_695.tsv, the 0.695 test copy's
Resources and packs (read-only, through re/out/fileindex_695.tsv offsets), re/as3_695, design/forum, and the server's
own AuthServer Resources/ArtTints.json. [P] means read in client code or data. [I] means inferred, with the reason given.
Scratch scripts are in the session scratchpad `tint/` (ext.py pack extractor, names.py / sw2.py Morpheme switch scan,
adrnet.py actor → network map). None of them were added to the repo.

| Lead | Verdict | One line |
|---|---|---|
| L121 movement state word | **FOUND** | Writer BaseClient::BroadcastLocalPlayerMovement FUN_142ca4ce0. Every bit is mapped to a player flag. The animation-parameter hashes name most of them. 0x10 = jumping, 0x20 = falling, 0x400 = landed, 0x1000 = gliding, 0x40 = not moving. 0x10000000 adds a support-entity tail. |
| L103 tint colours | **FOUND (colour source)** / Green-Blue sweater ADVANCED | Every alias's RGB is in the ArtData that the server sends (ArtTints.json, a live capture). All runtime screenshots match it. Live had no 4th or 5th Holiday alias. |
| L15 continent → world setting | **FOUND** | VoxelFarmCore::InitVoxelStructureContinent FUN_143ffa7c0 reads the client table VoxelContinentWorld, keyed by the core's continent id. |
| L14 layer 496 | ADVANCED | The client loads every mapped layer of a continent (FUN_143fe3930), so 391 gets both 364 and 496. Each layer's biome bmp and its bmp → property-group pairs are registered. |
| L51 CaveManager filter | ADVANCED | The filter is the cavern record's type byte. AddCave FUN_143f813e0 enters only type-3 caverns in the random-pick tables. |
| L117 Story Node emotes on creatures | ADVANCED | No creature network has an Emote_Index or Stance_Index switch or any emote clip. The player network drives kobolds, lesser giants, goblins, slaug, void goliaths, skeletons, ogres, dark elves, kerra, dwarves and Iksar. |
| L56 Lua bindings with no caller | ADVANCED | Tool.GetCursorSmoothing has a caller: ModalToolBrushOptionDefinitions.txt row 45, ACTIVATED_LUA_CHECK_FUNC. The other five still have none. |
| L21 Score / Victory Node | ADVANCED | The Game Table's prop list (InstancedGame.AddAdditionalProp, ScriptPropId) is the only client path that places a prop with no palette entry. Forum 57990 confirms players placed Victory and Score Nodes, that score changes printed chat lines, and what happened at game end. |
| L22 story reset message | ADVANCED | The key `Claim.StoryReset.Message` is absent from the exe, Lua and swf, so the server sent the text id. ChatPacketFromStringId is the existing carrier. |
| L23 firing details | ADVANCED (forum) | At game end (Sep 2016) the site reset and players were teleported to the start. The Score Node had an "add points to team" Link and a chat message. |
| L41 unlabelled functions | ADVANCED (3 relabels) | FUN_142ca4ce0, FUN_142c30e20 and FUN_142ec4be0 relabelled; see below. |
| L101 UniqueEquip reader | UNCHANGED (neg, wider) | The item data source exposes NoTrade (+0xc bit 7) and IsEquippable (+0xd bit 5) but no unique key. No code tests +0xe bit 7. |
| L16, L18, L19 | UNCHANGED | Notes below. |

---

## L121 — the movement state word: FOUND [P]

**Writer.** FUN_142ca4ce0 is `BaseClient::BroadcastLocalPlayerMovement` (its own profiler string, part_0063 ~20900-21480). It builds
the movement record (`local_238`: +0 type, +4 time, **+8 state word**, +0x10 position, +0x20 rotation, +0x30 4 × p2,
+0x40 velocity, +0x50 4 × p2, +0x60 p1, +0x64 p5, +0x70 support offset, +0x80 support id, +0x84 extra uint). The word is
`uVar14 | 1`, built from the player entity's flag bytes. FUN_142ecc7f0 then adds or clears 0x10000000.

**Size calculator.** FUN_142c30e20 matches our parser: type 0 is 20 values, then the 2-bit-length state uint, then the
+0x84 uint (the trailing `00` block). Type 1 is the state uint alone. Types 2, 3, 4 and 5 carry no state word; the
client fills it with 1.

**Remote apply.** FUN_142ea8bb0 (part_0074:10779) is how another client applies a received word to a proxy, through the
same setters. Most setters push a named animation parameter. The hashes use the client's one-at-a-time variant with
arithmetic shifts (see L103), and they resolve against PlayerAnimationNetwork64 strings.

| Bit | Source (player entity) | Meaning | Evidence |
|---|---|---|---|
| 0x1 | always set | — | [P] |
| 0x2 | +0x145a b0 | **Diving** | setter FUN_142ec2d00 pushes "Diving" [P] |
| 0x4 | +0x145b b7 | **Sprint** | FUN_142ec3230 is called with the "Sprint" input state (part_0081 ~26515 / 27534) [P call]; its hash 0x5aa09c18 is unresolved |
| 0x8 | +0xfc b7 | one-shot "force resend", cleared after it is read | [P] |
| 0x10 | +0x1450 b5 "Player IsJumping" | **jumping** | debug panel part_0066:5046; remote apply vfunc +0x2c0 [P] |
| 0x20 | +0x1450 b6 "Player IsFalling" | **falling** | FUN_142ec17a0 pushes "Fall" [P]; matches the runtime 0x21 at vy −3.1 |
| 0x40 | !FUN_142eaa130 "Player IsMoving" | **not moving**; velocity sent as 0 | [P] |
| 0x80 | +0x145b b1 | unknown; setter FUN_142ec3350, hash 0x09635e8f | [P bit] |
| 0x100 | — | never set by the sender; excluded from change detection; the reader tests it | [P] |
| 0x200 | — | never set by the sender; the reader copies it to proxy +0x145c b3 | [P] |
| 0x400 | +0x145c b0 | **landed / on the ground** | FUN_142ec4be0 pushes "Land" and clears jump, fall, flung and glide. Fall, glide and double-jump clear the bit [P] |
| 0x800 | +0x1450 b1 | **double jump** | FUN_142ec1070 pushes "DoubleJump" [P] |
| 0x1000 | +0x1451 b7 | **gliding** | FUN_142ec1c40 pushes "Glide" / "State_Gliding" [P] |
| 0x2000 | +0x145f b0 | **strafing** | FUN_142ec5fb0 pushes "State_Strafing" [P] |
| 0x4000 | +0x1460 b7 | **movement input held** | FUN_142ec4500 pushes "State_MoveInput" [P] |
| 0x8000 | player vfunc +0x310 | unknown (it also gates the "Selection" input) | [P bit] |
| 0x10000 | +0x1451 b6 | **vaulting** | FUN_142ec6be0 pushes "Vault" / "State_Vaulting" [P] |
| 0x40000 | +0x1451 b2 | **grapple deploying** | FUN_142ec1fb0 pushes "Deploy" / "State_Deploying" [P] |
| 0x80000 | +0x1451 b4 | **grapple (fire)** | FUN_142ec2260 pushes "Grapple" [P] |
| 0x100000 | +0x1451 b3 | **grappling** | FUN_142ec2110 pushes "State_Grappling" [P] |
| 0x200000 | +0x1460 b5 | **ledge grab** | FUN_142ec2eb0 pushes "LedgeGrab" / "LedgePull" / "LedgeDrop" [P] |
| 0x400000 | +0x1460 b4 | unknown; set in the grapple controller FUN_142e66c70 | [P bit] |
| 0x800000 | +0x1460 b0 "Player IsFlining" | **flung** | FUN_142ec2dd0 pushes "Flung" [P] |
| 0x1000000 | +0x1461 b7 "Player FallOff" | fell off | [P] |
| 0x2000000 | +0x1461 b2 | unknown; on clear the reader calls FUN_142e93b20 | [P bit] |
| 0x8000000 | +0x1450 b2 or b3 | unknown | [P bit] |
| **0x10000000** | support entity | **standing on a supporting entity.** The body then carries 3 × p2 (the offset from it) and a signed 2-bit int (its id, entity +0x1464) | FUN_142ecc7f0, FUN_142c30e20 [P] |
| 0x20000000 | +0x1461 b1 | the +0x84 uint carries player +0x4ae8 | [P] |

"Change reasons" counters (FUN_143b03830): a change in mask 0x201f810 counts as "Animation flag" and forces a full type-0
body. Any other bit change (except 0x100) sends a type-1 body. So the server sees every flag change as soon as it
happens, and the last word stays valid until the next change.

**Support entity (0x10000000).** The physics controller +0x22c holds the id of the AppPhysicsActor stood on, if that
actor's type is 0x10 (FUN_142e6dcc0, part_0072 ~37377). That id goes to player +0x16b4 (part_0072:35696), and is
resolved through entity manager DAT_145431080. The runtime shows this bit while standing on open ground. Which entity
that is remains open.

**Server changes:**
1. `Consumables.Airborne`: use the word, as the client's own test FUN_142e934c0 does (jumping, falling, gliding):
   `(MovementState & 0x1030) != 0`, optionally also `0x800` (double jump). Keep the vertical-speed test only as a
   fallback before the first type-0 or type-1 body arrives. This replaces the [I] slope-sensitive rule.
2. docs/protocol.md 4.3 and the PositionUpdate.cs remarks:
   - add types 4 (4 × p2, p1, p5) and 5 (+0x84 uint only);
   - name the trailing block after the state word (the +0x84 uint);
   - document the 0x10000000 tail (3 × p2 and a signed int) after any body whose word has the bit;
   - add the bit table above.
3. Anything that parses past the state word, or builds player bodies from LastFullMovement for other viewers, must
   allow for the 0x10000000 tail. Relaying the bytes unchanged is fine.

**Still open:** which entity the support id names on open ground (log the tail's id and compare it with the transient
ids we hand out); hashes 0x5aa09c18 / 0x09635e8f; bits 0x8000, 0x400000, 0x2000000, 0x8000000.

## L103 — tint colours: colour source FOUND [P]; the 2016 Green / Blue sweaters ADVANCED

**Client path [P].**
- `ToolTintDefinitionManager` (vtable 0x144956660; init FUN_143cd2e30) loads TintSemanticTables.txt (FUN_143cd0ef0;
  columns ID, ALIAS_NAME, SLOT, SEMANTIC_GROUP, SEMANTIC_NAME, EDIT_TYPE, STRING_PARAM, 4 floats), then
  ArtTintOverrideGroups, ArtSourceActorTintTypes, ArtSourceActors, ArtTints and ArtTintTypeTints.
- TintSemanticTables.txt is not shipped, neither loose nor in any pack (fileindex_695). The colours reach the client in
  the server's ArtData 0x116 TintDefinitionManagerData, which replaces the table (Game/ArtTints.cs doc,
  trace/server-only-data.md "TintSemanticTables.txt").
- Lookup FUN_143ccf6f0 takes an alias hash and a slot hash, falling back to "ANY". The material apply FUN_143d9e080
  calls it with no tint-type check, so any alias with SLOT ANY applies to any mesh [P for this path].
- Hash: the client's one-at-a-time variant uses **signed (arithmetic) shifts**: `h=(h+c)*0x401; h^=h>>6` (int), then
  `h*=9; h^=h>>11` (int), then `h*=0x8001`. Checked: "Generic_Red" gives its group id, and the twelve semantic hashes give
  BaseTint{Highlight,Midtone,Shadow,Smoothness,Dielectric,Metallic}{A,B}. An unsigned-shift hash misses some names,
  which may be why the earlier .dma search failed.
- The `./Resources/Tints.xml` loader (FUN_143d510a0; `<Tints><TintVal id r g b name WearableTintAutoAdd>`, keyed by the
  same hash, "BadTintAliasHashes.txt" on a duplicate) is a second palette, also not shipped.

**Data [P].**
- AuthServer Resources/ArtTints.json holds the 1,114 ArtTints aliases with RGB per semantic. It matches the 0.695
  ArtTints.txt exactly, except the five test aliases myTester / cTest. It is a 0.695-era live capture (upstream file,
  ids up to 1210).
- Per alias, the colours are BaseTintHighlightA/B (hash b89339c1 / ca54dd44), MidtoneA/B (2fa1022a / 89cf3685) and
  ShadowA/B (0595012a / 37e7e5cf).
- **Every runtime screenshot matches:**
  - the flight suit (Colony_Heavy_001) shows channel B on all 15 aliases: 10 orange, 11 pink, 12 blue, 13 deep red,
    02 green, 03 navy, 05 grey;
  - Holiday 00 = A green / B tan, 02 = A red / B brown, 03 = A tan / B green;
  - Anniversary 00..03 = navy, white, red, black (the text order);
  - Skeleton 00..06 = white, pink, orange, cyan, green, red, plus 06 pale green, which is unused;
  - Founder_08 = A orange / B near-black, so Tech Sergeant's Gear (Orange) is confirmed.

**2016 sweaters [I].** The live table has only Holiday_Light_00 / 02 / 03, and none of them is blue. So live reused a
non-Holiday alias. That works on any mesh per FUN_143d9e080, but not yet seen on the sweater. Aliases green in both
channels: GreenGreen00 (A 0.29,0.57,0.29 / B 0.13,0.33,0.12), TavernGreenPaint00, Gunslinger_Light_02. Blue in both:
Colony_Light_09 / Colony_02 (0.00,0.45,0.66), Iron, Qeynos_Heavy_10, Gun1H_Colony_07.

**Server change:**
- Cosmetics.Outfits: mark Skeleton / Holiday / Anniversary / Founder_08 as [P data].
- Optionally switch the Green / Blue placeholders to GreenGreen00 / Colony_Light_09, after a `weartint` check on the
  sweater mesh (TESTING-NEEDED).
- Worth adding: a dev `tintinfo <alias>` that prints the A/B colours from ArtTints.json, so no future pick needs screenshots.

## L15 — the continent's world setting: FOUND [P]

FUN_143ffa7c0 is VoxelFarmCore::InitVoxelStructureContinent (its own error strings). It:
1. walks ContinentWorldManager (definitions +0x2a8, list head +0x2c0) for rows whose CONTINENT_ID equals core +0x5208;
2. collects their WORLD_IDs;
3. walks WorldSettingBiomeManager (definitions +0x630, list +0x648) for those worlds' biome rows.

Core +0x5208 (with +0x5aa8) is set by FUN_143f9aa30 / FUN_143f9aad0 / FUN_143f94580 from the zone's continent
definition. So the biome set follows the client table by continent id. The SendZoneDetails sky fields play no part,
which fits the 2026-10-16 bake evidence.

**Server change:** none. The continent id we send per landscape must stay correct.

## L14 — layer 496: ADVANCED [P]

The definitions container +0x1008 is VoxelContinentLayerDefinitionManager (RTTI 0x1449a4cf0; loader FUN_143fe2de0).
It turns VoxelContinentToLayerMapping into a multimap keyed by continent, and VoxelLayerBmpToPropGroups into
Map<int, BmpValueToPropertyGroupPair>. Readers:

- **FUN_143fe3930(manager, continent, &Array<int>)** returns *all* layers of a continent. For 391 that is 364 and 496.
- InitVoxelStructureContinent then:
  - FUN_143ff3180 collects every layer's property groups (FUN_143fe3830 pairs → definitions +0x1310) into core +0x4f80;
  - FUN_143ff25c0 requests each layer's UNDERGROUND_BIOME_MAP once per file name (both use
    Continent_Underground_BiomeIds_00019.bmp) and builds a bmp value → property-group map per layer.
- VoxelFarmCore::LoadContinentCaves (FUN_143ffcde0) resolves a cavern's property group through that map
  (FUN_143fa0ed0 / FUN_143fa0f20; "Invalid Property Group Id" otherwise).

So the client registers the tier-2 groups 234 / 228 / 258 for 391. No 391 .cpa record uses them, so nothing places
them [P data, terrain-695.md §3].

TELEPORT_IMAGE_ID 10228 / 10229 is not an ImageSets id (those rows are outfit icons); the table it points to is
unresolved.

**Next:** where the per-layer map built in FUN_143ff25c0 is stored (core offset) and how a cave record picks its layer
index; runtime on 391.

## L51 — CaveManager cave filter: ADVANCED [P]

- AddCave FUN_143f813e0(location, type, …) appends every location to +0xd68. Only when **type == 3** does it create a
  pick node (+0x1460, id at +0x58) and append that id to the per-type list (+0x1490, buckets +0x14b8, keyed by type,
  bucket = type & 1).
- GetRandomCaveBeginningLocation's `*param_5` is that type: it picks at random among caves of the given type and skips
  the current one.
- The caller is LoadContinentCaves (part_0163:27947). It adds a cave when cavern-record byte +0x24 is set, with the type
  from byte +0x2d, the location at +8 / +0x10, a byte +0x2c and an int +0x20.

**Next:** decode CavernInstanceData in the .cpa (L47) to see which records have +0x24 set and type 3. Those are live's
"cave beginning locations" for TeleportRandomUnderground; the server should pick among them.

## L117 — Story Node emote / stance on creatures: ADVANCED [P]

- Node-name tables and switch NodeDefs (type 0x83) were decoded in the 0.695 networks (scratch sw2.py).
- In PlayerAnimationNetwork64 the Emote_Index / Stance_Index switches are found as before (Walk_Emotions*, Emotes|*).
- NpcAnimationNetwork64 and GiantAnimationNetwork64 declare Emote_Index (54), Stance_Index (55), Emote_Emotion,
  EmoteLoop and SitHeight, but **no switch reads them and they have no emote or stance clip nodes**. Their switches use
  only OuchIndex, AttackIndex, CharacterScale and Pose. The Pet, Chomper and LargeBiped networks show the same: no such
  switch.
- Actor → network, from the .adr `<AnimationNetwork fileName>` in all packs:
  - the player network drives 136 actors: all PC races, plus Kobold Caster / Runt / Melee / Boss, LesserGiant (12),
    Goblin, Slaug, VoidGoliath, Skeleton, Ogre, Iksar, FirionaVie, DarkElf / Kerra / Dwarf PCNPCs;
  - the npc network drives Wolf, SpiritWolf, Deer, Quadbot, Klickbot and Abomination;
  - the giant network drives Giant, Stone Giant and IronGolem;
  - the others each have their own network.

So a Story Node mood / stance / emote shows only on player-network actors [P, network data]. Whether each clip exists
for those skeletons is [I].

**Server change:** none needed. Optional: skip sending emote / stance for other models.

## L56 — bindings: ADVANCED [P]

`Tool.GetCursorSmoothing` is called by the client through Resources/ModalToolBrushOptionDefinitions.txt row 45
("Smoothing enabled", ACTIVATION_LUA_CALL Tool.ToggleCursorSmoothing, ACTIVATED_LUA_CHECK_FUNC Tool.GetCursorSmoothing).
Data-table Lua columns were a search space the sweep missed. The other Resources tables have no Mesh.*,
CharacterSelect.* or Ui.Get/SetAmbientVolume calls. Data.xml uses GameSettings.*AmbientVolume, a different namespace.

Still no consumer: CharacterSelect.GetEyeSlotCount / SetEyeSlotIndex, Mesh.Populate /
GetMeshesFilteredByAvailability, Ui.Get/SetAmbientVolume.

## L21 — Score / Victory Node placement: ADVANCED

- [P UI] The Game Table window (RulesetObjectSettings Category.as:270) places recipe props through
  `InstancedGame.AddAdditionalProp(PropRuleIndex, PropTeam, PropSlot, ScriptPropId)` → InstancedGamePropRequestPacket.
  This is the only client path that places a prop without a palette entry, which fits image set 0 on 3010 / 3011.
- [P forum 57990, Sep–Oct 2016] Players built games with "a counting node and a victory node". A Score Node value of −1
  "added a point" through its "add points to team" Link. Scoring printed a chat line per team gain.
- [I] So live listed 3010 / 3011 in the Game Table's PropRecipe.

**Server change:** include the Victory Node 3010 and Score Node 3011 in the Adventure / PvE PropRecipe
(InstancedGames.Editor.cs).

Which prop owns 577665 / 577666 / 577668 / 577684-577687 is still open.

## L22 — story reset message: ADVANCED [P neg]

"Claim.StoryReset.Message" / "StorySaveSnapshot" / "Claim.Story" are absent from Landmark64.exe, every UI Lua / swf
and the decompile. Other claim keys are looked up by literal string (FUN_142ca7080 → DAT_145431178 vtable +0x20), so
the client never resolves this key. The server must send the text id. The server already has ChatPacketFromStringId
(ChatBase 0x13 sub 4, Emotes.FromStringId).

**Server change:** on ResetStory (StoryTools.cs ~1329), send 577089 through FromStringId to the players on the site.
[I: the carrier; the text and its trigger are P].

## L23 — firing details: ADVANCED (forum)

Forum 57990 (TR3LON1ST, 29 Sep 2016, and Persephone): when a Game Table game is won, players are teleported to the
spawn point, the claim's story resets automatically, and "TEAM wins" shows. Respawn-point states set during the game
do not survive the reset. The counter limits, spawner events on restart and the Combat Node target are still not stated.

## L41 — relabels from this pass [P]

| Function | final.tsv label | New label |
|---|---|---|
| FUN_142ca4ce0 | ClientWaypointManager::GiveTime (caller) | BaseClient::BroadcastLocalPlayerMovement (own string) |
| FUN_142c30e20 | PlayerUpdateManagedPosition (caller) | movement body size calculator |
| FUN_142ec4be0 | ProxiedCharacter (locality±9) | Player SetLanded ("Land" anim parameter) |

Also named: FUN_143b03830 (movement change reasons), FUN_142ea8bb0 (apply remote state word), FUN_143ffa7c0 /
FUN_143ffcde0 (InitVoxelStructureContinent / LoadContinentCaves) and FUN_143fe3930 (layers of a continent).

**Method for the rest:** resolve hashed constants with the signed one-at-a-time hash against mrn and table strings.
That named 24 of 29 animation hashes here.

## L101 — UniqueEquip: UNCHANGED (negative, wider) [P]

The item data source (part_0076 ~25220) exposes ItemID, Description, IsEquippable (+0xd bit 5), Quality,
ExpirationTime, NoTrade (+0xc bit 7), TotalCoolDown and Condition. It has no unique key. Combined with no +0xe & 0x80
test anywhere, the client never reads the bit. Enforcement is the server's reason 0x22 only.

**Next:** none on the client. The bit's meaning stays [I strong] (the 21 accessories).

## L16 / L18 / L19 — UNCHANGED

- **L16:** nothing new beyond the sweep's solid range for 0xFFFC.
- **L18:** EditablePropertyDefinitionManager (RTTI 0x14496d338) groups EditablePropertyValueDefinition by
  EDIT_PROPERTY_ID (EmbeddedHashList). The 0.695 settings window takes its drop-down options from the server's
  list-B records (docs/story-tools.md), so the values exist only server-side.
- **L19:** forum 57990 / 58022 hold only a player's suggested IF wording ("greater then / equals / less then"), which
  is not proof of the live labels.
