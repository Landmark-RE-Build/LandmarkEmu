# Creature health bars by distance (2026-10-31)

Player report: "red health bars for monsters and other stuff that use the same health bar have no range distance on
them, I can see them from far away". Research workflow wf_58cf4ef3-40f (client resource range + removal, indicator
distance gates, server send paths, live footage; skeptical review). [P] proven from code / data / frames, [I] inferred.

## Client

- PACKET_BROADCAST_RANGE (Resources.txt; 122 of 126 rows 30, rows 118/119/121/122 0) is loaded into ResourceDefinition
  +0x84 (FUN_143c77940, part_0146.c:3132) and only validated (FUN_143b9c9a0); no reader of the row lookup
  FUN_143c77750 / DAT_1455fd138 / BaseClient+0x32208 reads +0x84 [P]. The client never drops a resource record by
  distance, LOD, budget or timer (FUN_142f87230 promotes the nearest 50; FUN_142e93120 only sorts) [P].
- ResourcesBase 0xC4 has subs 1 SetCharacterResources (FUN_14361c300 → FUN_14354fa00 → FUN_14354ef20: removes every
  record, then reads u32 count × records), 2 SetCharacterResource and 3 UpdateCharacterResource (overwrite / create one
  record) [P]. A sub 1 with count 0 (u32 seq, u8 1, u64 guid, u32 0) empties the container; the hold FUN_14361cd30
  applies it at once [P]. Records otherwise go only with the full-data wipe (FUN_143b8aef0, once per entity) and the
  destructors [P].
- NPC indicator (row 1^22^21, requirement 21 "NPC Combatant": NotPlayer && NotProp && TargetCanBeHarmed &&
  TargetNotPropProfile; TargetCanBeHarmed = Life max > 0): fails → the whole indicator goes [P]. Bar flag
  (FUN_143449e00): spawn bit 0x80 clear, Life max >= 1, hostile or injured [P]. Draw FUN_143727270: name alpha full to
  25 m, linear to 0 at 100 m (StringHashToValue NpcNameplate.CloseProximityRange / MaxPromximityRange); the bar
  (FUN_143725870) reads no distance, fixed alpha (Npc PARAM2) [P]. At most 20 indicators per group (DAT_1446b8dc8 = 20,
  setting hash 0x47247cd8), sorted by priority, on-screen, distance [P]. Visibility FUN_14344a820: actor rendered
  (FUN_142ed0620 < 3) and camera LOS state != 2 [P].
- Prop (tree) bar: requirement 13, only the harvest target or within 7 m for plant profiles 90/91/296 [P]. Player bar
  (1^1^3 TargetIsPlayer, bar when Life max >= 1 and not the local player): no distance either [P].

## Live

- 2016 footage (frames viewed: hb-*/t0045, t0240, hb-Ux2LRi2To_4/hd/t0010): full red bars on creatures well beyond
  30 m, out to about where the names fade [P frames, I distances]. So live did not cut bars at 30 m; most likely it sent
  Life with spawn / full data at any streamed distance and PACKET_BROADCAST_RANGE limited change broadcasts [I].
- Our creatures stream out to ~301 m (surface regions) / 150-200 m (caverns), so bars showed out to ~300 m.

## Built (Creatures.SyncLife, CharacterResources.ClearFor)

Setting CreatureLifeRange (default 100 m = the name fade-out, slack 10 m; 0 = every streamed viewer): Life at spawn and
after the full data only within the range, changes only within it, cleared with sub 1 when a viewer is beyond range +
10 m; checked once a second after streaming (Stream → SyncShownLife). Bosses always keep theirs. Tests
CreatureLifeRangeTests. Corrected: full-npc-695.md §4's "+0x282e0 = 300 m full-data range" is a +300 sort penalty for
entities not rendered last frame (FUN_142e93120 part_0073.c:27125).
