# Rebuild plan: archive → client trace → rewrite, every module

User directive 2026-10-13 (after terrain v2): do the terrain method for every server module, existing and future.

Per module:
1. Archive: git tag `<module>-v1` (LandmarkEmu repo) + copy to `archive/<module>-v1/`.
2. Trace the client end to end (handlers, state, what blocks/clears what, versions/caches, data tables, server
   library code compiled into the client); confirm with captured live data. Write `trace/systems/<module>.md` first.
3. Rewrite to the client's model; measure with logs/probes; verify in the test client.
4. Docs (LandmarkEmu/docs), MODLOG, TESTING-NEEDED, commit per step.

Status: `todo` · `tracing` · `rewriting` · `verify` (waiting for the user's in-game check) · `done`.

| # | Module (server files) | Client families | Status | Archive |
|---|---|---|---|---|
| 1 | Terrain (Game/Terrain/*, TerrainHandler) | 0xEC TerrainCell | verify (v2: layers, coarse copies, busy gate) | terrain-v1 |
| 2 | Building (Building, BuildingClipboard, WorldEditing, Templates, SiteDesigns, BuildMaterials, Palettes) | 0xF4 VoxelConstruction, 0x10C WorldEditing, TemplateBase, Palette, Tool 0x117 | verify (ApplyDiff versions, ResponseObserved, undo OK, voxel design storage; open: template instances 0x12/0x13, palette Create/Delete (no 0.589 UI), design props → #3) | building-v1 |
| 3 | Props (Props, PropSettings, PropEffects, PropAnimations, PropLimits, RightClickMenus, StoryTools) | 0xED Prop, PlayerUpdate prop subs, Interaction; + props in copies/designs (building.md) | verify (prop-move undo, props in copies/designs, text props via Links, UpdatePropName; mode-1 text window built (reading signs / books / scrolls, 10-13); open: 0xD replication, 0x7D (settings already update through 0xFD), RequestList 9 (no UI)) | props-v1 |
| 4 | Claims / build sites (Claims, SiteContents, SiteUpkeep, SiteScreenshots, Outposts, Spire) | Claims family, ClaimVendor | verify (deep pass 2026-10-12; ResultResponse 0x10 added; groups / upkeep deposit / snapshots unreachable in 0.589) | claims-v1 |
| 5 | Inventory & items (Inventory/*, ItemUses, Durability, Salvaging, Consumables, DesignItems, Vaults, Chests) | ClientUpdate items, Container, Chest | verify (transfer reasons, NO_TRADE in chests / trades / mail; open: remaining item flag bits — NoDelete / NoSale / NoSalvage / UniqueEquip) | inventory-v1 |
| 6 | Gathering & mining (Gathering, Abilities mining path, LootPinatas, OreVeins link) | Abilities 0x27, Gathering, LootPinata | verify (refusal reasons, drops, tool table built; tool numbers are server data — wiki values) | gathering-v1 |
| 7 | Combat & abilities (Abilities, Combat/*, WeaponAttacks, HitEffects, Health, Buffs, Creatures, Encounters) | 0x27, Combat, PlayerUpdate | verify (protocol table re-checked: charged casts / cancel handled, observers get PlayAnim; numbers are server data) | combat-v1 |
| 8 | Crafting (Crafting, CraftingStations, RecipeBook, Unlocks) | Recipe, Crafting | verify (start / cancel / complete responses already sent; refining / upgrades not in 0.589 data) | crafting-v1 |
| 9 | Travel (Teleport, Travel, WarpNetwork, Landscapes, Caves, Graveyards) | Teleport, WarpNetwork, Zone | verify (deep trace 2026-10-14: every unhandled Travel packet has no 0.589 trigger — queue, waypoints, respawn list, fog of war — or needs zone area data no capture has: area teleporters; world-travel.md) | travel-v1 |
| 10 | Social (Chat, ChatRooms, Groups, Guilds, Friends, Ignores, Mail, Emotes, Trades, Reports, Ratings) | Chat, Group, Guild, Mail, Trade | verify (deep trace 2026-10-14: officer chat only to JoinOfficerChat ranks, guild mute, shout / emote ranges, mail 128 / 4096 + expiry, party 4, guild 250, room names 32, trades cancel on zone / game, stack-aware trade fit; social.md) | social-v1 |
| 11 | Progression (Achievements, DailyGoals, Titles, FirstTimeEvents, Pioneer, CharacterResources) | Achievement, Objective | verify (deep trace 2026-10-14: 15 objective kinds added — kills by family / element, guild, design trade, chests, design items, likes, visits, scan, extractor; crafting-meta.md) | progression-v1 |
| 12 | Commerce & store (Commerce, Store, Vendors, Contests, WebPages) | Commerce, InGamePurchase | verify (deep trace 2026-10-14: requests all handled; open: purchased-design instances for Voxel Vision need instance ids in the terrain ownership grid + a Player Studio purchase flow — crafting-meta.md) | commerce-v1 |
| 13 | Player & session (PlayerManager, Barbershop, Portraits, Notifications, SystemMessages, ServerClock, WorldClock, Shutdown) | Login/zone, PlayerUpdate | verify (deep trace 2026-10-14: duplicate login kicks the older client with 0x53; the rest of the session messages have no 0.589 trigger or are covered — session.md) | player-v1 |
| 14 | Instanced games (InstancedGames*) | InstancedGames | verify (deep trace 2026-10-14: requests handled, Ig* parameters applied except assists / spawner points / start-exit respawn delays / movement permissions — activities.md) | instancedgames-v1 |
| 15 | Network/session layer (Shared/Network) | SOE protocol | done (audit only: limiter, multipacket, fragments fixed 2026-10-13; 2026-10-14: per-packet resend backoff confirmed — resends cluster in the zone-in burst while the client is loading) | |

Future modules (not built yet) go through the same steps before any code; add them here when found in the trace.

Cross-module (2026-10-13): trace/client-result-tables.md lists every client code→text table; PlayerUpdate subs are
one byte on the wire (enum values 0x26xxyy, never 0x26xxyy00 for S→C).
