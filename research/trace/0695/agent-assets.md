# Client asset evidence: Regalia outfits, creature weapons (agent "assets", 2026-10-18)

Branch `local/assets` (worktree `<workspace>\wt-assets`). [P] proven from the 0.695 client / its data, [I] inferred.
Sources: <0.695 test copy> (0.695, read-only), re/out/fileindex_695.tsv / fileindex_589.tsv, gameData695, the live prop list
(Resources/PacketDumps/145-PropBase), design/wiki, design/forum/patches. Scratch scripts in the session scratchpad
(get695.py = extract pack entries by fileindex offsets; strs.py = printable strings; props.py = decode 145-PropBase).

## 1. Noble's / Courtier's Regalia (OPEN-LEADS L149) — found, built

| | Noble's Regalia | Courtier's Regalia |
|---|---|---|
| Name / description text [P] | 560077 / 560091 "... worn by fearless Trailblazers." | 560075 / 560089 "... the most audacious of Explorers." |
| Mesh [P files] | `Char_Biped_[RACE][GENDER]_Entities_Trailblazer_000_Chest.adr` (ArtSourceActors 25, 28, 694, 719; Models 609) | `..._Entities_TrailBlazer_001_Chest.adr` (244, 247, 695, 718) |
| Icon [P] | ImageSet 754 wearable_Medium_TrailBlazer_000 | 755 wearable_Medium_TrailBlazer_001 |
| Tint type / aliases [P] | 43 → TrailBlazer_00..05 | same |
| .adr default tint (human bodies) [P] | TrailBlazer_01 (material 1) | TrailBlazer_00 (LargeMale's .adr says _01, LargeFemale's none) |
| Used by a dump item | no | no |

Matching [I, strong]: the 512 px icons (Resources/Images/icons_wearables/icon_wearable_Medium_TrailBlazer_00x_512.dds,
rendered): 000 = fantasy armour with a long blue cloak, 001 = white shirt, red vest, tan gloves and trousers, no cloak.
Wiki "Noble's Regalia": "fantasy-type armor ... an animated cloak"; wiki "Courtier's Regalia": "upper shirt and vest are
white and red. The gloves and pants are tan" — an exact fit. Tier pattern as the Tech gear (Founder_000 = Trailblazer's
Tech Commander, Founder_001 = Explorer's Tech Sergeant). The pre-modular full-body actors agree: Models 391 / 393
`Char_Biped_Human*_TrailBlazer_01.adr` and 414 / 415 `Char_Biped_Human*_Courtier_01.adr` (mesh `..._Explorer_01.dme`), both
textured `Wearables_Medium_TrailBlazer_TrailBlazer_000`; 416 / 417 `TechSergeant_01` and `Founder_01` textured
`Wearables_Medium_Founder_Founder_000`. Same files in 0.589 (no new art in 0.695). Forum: 18 Apr 2014 hotfix "outfits
... tint incorrectly: Courtier's Regalia, Noble's Regalia"; 27 Jan 2015 female Courtier's visual fixed; 22 May 2015
Noble's cloak clipping on females fixed.

Built: Cosmetics.cs NoblesRegalia 900344 / CourtiersRegalia 900345 (template 2521 like the other outfits), added to the
founder entitlement package. Not proven: the tint each item used (mesh defaults taken), the item ids (ours), live's
no-trade flag (the other built outfits are tradeable too, as before).

## 2. Creature weapons (OPEN-LEADS L95 lead 1) — no creature → weapon data in the client

Searched (all 0.695):
- Every spawner prop's model (145-PropBase, 185 props with "spawner" plus the landmark_ruin_npc_* rows; 75 distinct
  .adr): none has a Wield_* reference, EquipType or ChildAttachSlotsEx. Kobold / Skeleton meshes reference only body and
  wearable textures (Kobold_Wearables_Melee/Caster/Boss, Skeleton_000; rendered: no weapon art in the atlases).
- Tables: ArtSourceActors, ArtSourceActorTintTypes, Models, ProfileDefinitions, EquipmentSlotDefinitions,
  CharCreateGearSetMapping (char-create preview gear only), LootPinata (dropped-item meshes; 124 = kobold bow mesh as a
  generic bow drop), ActorSockets, ActorCompositeEffectDefinitions (weapon effects are the items' ambient / trail
  effects; PFX_Weapon_WeaponTrail_Kobold_Spear 10231), TriggerFxTag (kobold rows marked DEPRECATED), AnimEnums,
  AnimationSelection*: nothing ties a creature, prop or model to a weapon. 0.695 decompile: no Wield_ string.
- Wiki creature pages: one real weapon, Barren Flameblade "Uses a flaming greatsword"; the rest carry the template
  placeholder "uses a flaming bow to attack". Images are not local.

Partial evidence [P data, I meaning]:
- AnimationOverrides.xml set "Skeleton" (Barren) replaces Core Adventurer **Sword1H** AttackQuick L / R and
  ShadowSwordAndShield AttackIdle with its own Weapon01 animations → the Barren melee were animated as 1H-sword fighters.
- Kobold-made weapon actors, used by no item record: Wield_Bow_Kobold_Kobold_A_A1_000 (Class 1011 BowUser),
  Wield_Staff_Kobold_Kobold_A_A1_000 (1012 StaffUser, tint Staff_Kobold_01), Wield_Staff_Kobold_Kobold_D_A1_000
  (1023 GreatStaffUser); the kobold spear exists only as a .dme (no .adr: cannot be attached). Kobold animation set has
  SoldierBow / SoldierSpear / SoldierStaff attacks (these belong to KoboldAnimationNetwork, the old SOE Live kobolds;
  0.695 kobolds use PlayerAnimationNetwork).
- Weapon actors set Class / Weapon / WeaponIdle on the holder (AnimEnums: Class 1010-1029 SwordUser ... DualAxes; these
  are also the player combat weapons' classes, so no NPC-only weapon set exists).

Carrier re-read [P]: EquipmentSetCharacterEquipmentSlot handler FUN_142e42da0 — reader FUN_142e41b40: profile u32, guid,
slot u32 (must be > DAT_1455fe56c), item guid, ...; other guid → FUN_142cd1cb0 → vtable +0x498 clears the slot →
model non-empty → FUN_142ebd620 attach; FUN_142ea49e0 records the item guid only when entity +0xa68 exists (creatures:
returns). So an empty model clears a creature's hand.

Built (server, off by default): Game/CreatureWeapons.cs — table model → weapon with only the name-matched kobold weapons
(1570 Scalewind Ranged → kobold bow, 1571 Scalewind Caster → kobold staff A) [I]; the hand is sent once after the full
NPC data and on Story Node / Link Equip / Unequip (Sheathed = hand emptied [I]); setting CreatureWeapons
(CREATURE_WEAPONS, default false); dev `creatureweapon <guid hex|near> <Wield_*.adr|off> [tint]` to test any weapon on any
creature.

Still open (L95): live's per-creature weapons (Barren, Highfall, Ebonguard, NovaTech, Stoneskin, bosses); try: wiki /
forum screenshots, videos, captures of a live NPC full data packet; the runtime check whether a weapon actor's Class
changes a creature's attack pose.
