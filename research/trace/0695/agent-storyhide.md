# Story tools hidden in Play Mode, Story Node / spawner leftovers (agent "storyhide", 2026-10-17)

Branch `local/storyhide` (worktree `<workspace>\wt-storyhide`). Marks: [P] proven from the client, [I] inferred.

## 1. Play Mode hiding (FEATURES "Story tools and Links" row 1, TASKS E.17, L20)

Already traced and built before this task: agent-playmode.md §1 (merge ebaaeae "Merge e-playmode", in this branch).
The client has no Play Mode rule for props (every read of claim entry +0x378 traced), so the server leaves the hidden
tools out (`Props.PlayModeVisibility`: Nobody / BuildModeOnly for the Starting Location / Everyone). TASKS E.17 is
ticked and L20 closed; only the FEATURES row still says "not traced" (stale).

Re-checked here: the 0.695 Story Tools palette (PropCategoryPropMap category 120) is 2325, 2330, 2345, 2655, 2367,
2344, 2898, 2899, 2900, 2940, 2901, 2659, 2657, 3000, 2701, 2313, 2976 (plus 0 rows). The hidden set covers all of them
except the Scoreboard 3000 (shown, P6182), the Starting Location 2655 (its build-mode rule) and the Game Table 2313
(L20-b: no statement found). Patch notes P5234 / P5288 / P5960 / P6178 / P6182 hold nothing new.

L20-e (prop part bool +0x490 → entity +0x4e9c): its readers are the mover areas — ClientAreaChainThrustPad
(FUN_1436da050 → FUN_1436d9c90, FUN_1436d9680) and ClientAreaTeleporter / …ClaimVisitation (FUN_1436db8b0,
FUN_1436dbf00) by rtti_695 vtables [P]. A mover enable flag, not a visibility flag; nothing to do for Play Mode.

## 2. Story Node / Creature spawner rows: the weapon model (L95 open lead 1)

Carriers [P]:
- LightweightToFullNpc +0x28: n × CharacterAttachmentData, reader FUN_142c185c0 = 4 strings (model, texture alias, tint
  alias, decal alias), u32 tint (+0x180), u32 composite effect (+0x184), u32 slot (+0x188), u32 (+0x18c), bool (+400);
  applied by FUN_142e9a050.
- EquipmentSetCharacterEquipmentSlot (0xCB), handler FUN_142e42da0: a guid other than the player's own goes through
  FUN_142cd1cb0 → FUN_142f85390 (the shared entity table full NPCs are in, L96), vtable +0x498 clears the slot, and a
  non-empty model is attached; the profile id must be above DAT_1455fe56c. Same record layout as above.
- Both end in FUN_142ebd620 (per-slot record keyed by +0x188, then actor vtable +0x340 attach).

Missing data (not found yet; searched):
- Creature actors carry no weapon: Char_Biped_HumanMale_Entities_PCNPC_Qeynos_Heavy_000_Chest, Skeleton_00_Chest,
  Kobold_Melee_000 .adr (0.695 packs): empty EquipType / ChildAttachSlotsEx, no Wield_* name.
- gameData695: Wield_* names only in ArtSourceActors (the weapon actors themselves), ArtSourceActorTintTypes and
  LootPinata (dropped-item models). No table ties a creature prop, PropDefs row or model to a weapon. The live prop list
  (Resources/PacketDumps/145-PropBase) has no attachment field. No NPC packet captures exist.
- Wiki creature pages say some: Barren Flameblade "Uses a flaming greatsword"; Barren Bowman's line is a template
  placeholder. Several creatures share one model (all Barren use 1457), so live told them apart by server-side weapons.
- Try next: wiki / forum screenshots per creature, videos; NPC weapon models in the packs (e.g.
  Wield_Bow_Kobold_Kobold_A_A1_000.adr, Wield_Staff_Kobold_Kobold_[A|D]_A1_000.adr,
  Wield_Polearm_Combine_KoboldSpear_000) matched to creature families. Building a creature → weapon table would be ours,
  behind a setting.

Not built: an invented creature → weapon table would be guesswork.

L95 open lead 5 (non-biped creatures and WeaponIdle) [I, weak]: Npc, Giant, Pet, Chomper, LargeBiped and FieryFungi
networks all have the control parameter WeaponIdle, and the condition-record scan (re/out/l117/conds.py) finds
WeaponIdle > 0.5 records in Npc (12), Giant (12) and Chomper (17); condowner.py names only Giant FakeDeath exits and
Chomper Attack_01 Release as owners, and the FieryFungi switch FieryFungi_Core_Stand_01 reads it. So unlike the moods
(L117), 352 / 353 may change something on these creatures; runtime check.

Other partial items in the two rows (lists, announce lines, health / damage %, duplicates) are built and wait only on
in-game tests; nothing left for the client evidence to specify.
