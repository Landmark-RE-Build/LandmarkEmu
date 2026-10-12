# System: world — time, areas, POIs, fog of war, guide stones, waypoints, travel / teleport, warp network, mounts,
vehicles, heroic movement (grapple, swimming), gathering & loot

Status: [~] map level.

- Time: GameTimeSync ✔, ClientGameSettings time scalar ✔ (world clock). Terrain phases: ClientUpdate PhaseChange 0x1D
  ✗; TerrainCalendarDefinitions / TerrainPhaseDefinitions (server-only loader FUN_1436f6da0). Sky / weather: Sky*.xml.
- Areas (0xEA) ✗ AreaDefinition transfer / update / remove / create trigger area; S→C RequestResponse,
  CreateTriggerAreaResponse; AreaDefinitionSetDefinitions (no opcode yet). SetClientArea 0x5C ✗, POIChangeMessage 0x6A ✗.
  Data: Areas.txt, AreaProperties.txt (client), AreaDefinitions (captured ✔).
- Fog of war (0x102 / 0x103) ✗ zone data request / reply. Guide stones (0xB7) ✗ AddInfo. Waypoints (0x32 SetWaypoint)
  ✗; Waypoints.txt (9 cols).
- Travel / teleport (0xFC) ✔ TeleportLocations; ✗ TeleportRequestLocations 0x2, TeleportRequest 0x4,
  TeleportToQueuedLocation 0x6, TeleportCancelQueue 0x7, PlayEffect 0x9, Managed 0xA, **RandomUnderground 0xB /
  NextUndergroundReply 0xD (caves)**, S→C PostQueueNotify 0x5, IntraZoneRequest 0x8. Travel casts 185-187 ✔
  (Abilities). Warp network (0x113) ✗ ClientRequest 0x4 (received 168× — answered nothing on purpose, protocol.md
  §14.1), ZoneNetwork 0x2. GetRespawnLocations (0xCD) ✗ (respawn list sent unasked).
- Heroic movement (0x105) ✗ all C→S: FireGrapplingHook 0x1, SetHeroicMovement 0x2, SwimmingUpdate 0x3 (received
  183×), TerminateGrappleHookEffects 0x4, HeroicMovement 0x5 — the grappling hook (0.589 has the grapple equipment
  slot; Inventory 0x18 GrapplingHookEquipped) and swimming state; others never see the grapple.
- Mounts (0xA1) ✗ all; Vehicles (0xBF) ✗ all (Vehicles.txt; no vehicles in 0.589?). Thrust pads (0xCF) ✗.
- Gathering & loot: Loot pinatas (0xFB) ✔ spawn / remove; ✗ **PickupLoot 0x5 (C→S) / PickupLootResponse 0x5**,
  RequestClaimOwnerlessLoot 0x7, NotifyOwnerlessLootClaimed 0x4. Inventory LootNotification 0x1A ✗. PlayerUpdate
  LootEvent 0x15 ✗, StartHarvest / StopHarvest ✗; harvest results (Abilities EffectEnd 0xD) ✗. Data: LootPinata*,
  HarvestableDefinitions? (check), ProceduralPlacement* (node spawning; ProceduralPlacementEntityLists server-only).
- Interaction (0x11B) ✗ InteractionStart / CancelZone / End (timed interactions). Hud radial menu (0xDC): ✗
  RadialMenuRequest 0x1 (C→S), ✔ Response (check). Target (0xB5) ✔ set / remove; ✗ AddTarget / RemoveAllTargets.
- Projectiles (0xEE) ✗ create / update / delete; placed effects (0xEF) ✗; CorrectNpcHeights (0x114) ✗;
  ManagedPosition (0xC7) ✗.

## Client constants (trace/client-constants.md)
Teleport abilities: FriendAbilityId 187, HubAbilityId 185, OwnClaimAbilityId 186; PlayerTeleportImmunityTimeMs 1700;
loading screen when the jump is > 80 m. Warp network: cost item 1579 (Lumens), BaseCost / HubCost / WorldCost 0 —
**travel was free at launch**. Time.SeekDuration 3 s (time of day blend).

## Live behaviour (wiki, launch 2016: Spire, Servers and Landscapes, Hub, Pioneer Landscape)
- **Spire** near each landscape's centre: click the floating crystal (stand in the glowing circle) → World /
  Continent travel (servers = Worlds, landscapes shown as half-and-half biome images, biome filter "See All") or
  Local travel. Amenities: Replicator, Repair-o-Matic, Crimson Parlor (change 6 features, not size; Lumens),
  Mailbox (4 items; site visitor comments arrive as mail), Founder's Chest (replaces lost founder items).
- **"Everywhere Spire"** — the pale blue crystal UI icon (WIDGET_SPIRE_MENU / TELEPORT), usable anywhere: My Sites,
  Others' Build Sites (published / Gallery), My Friends (teleport to friend; cooldown), My Guildmates, Spire
  (nearest), Other Worlds (same menu as a physical spire), **An Underground Adventure** (random spot deep in the Chaos
  Caverns). Replaced Stones of Recall / portal shards. Ley Line Network (Dec 2014) removed by launch.
- Servers: Courage, Liberation, Serenity, Satisfaction (EU), Foundations Museum (visit-only, contest winners); free
  travel between servers, identical landscapes, player builds differ. /join General needed after each server change.
- Landscapes have 2 biomes (central + outer) plus ocean; 24 named landscapes listed on the wiki page (Arid Thicket,
  Snowcapped Wilds, … Grand Fjord) with their biomes — compare with gameData continents / ZoneDefinitions.
- **Pioneer Landscapes**: perfectly flat, one biome (Tundra, Tropical, Deciduous, Desert, Old Growth; no Volcanic);
  every new character starts with a site already placed there; the Journal goals tutorial runs there. Deleting the
  pioneer site gives the flag back (overflow) and **you can never place a site on a Pioneer landscape again**
  (Claims.UsersCantPlaceInNewbieZone). Alpha/beta veterans got a 2nd site via a build site expansion flag.
- Daily server "bounce" at 5 am PST (tree / ore respawn issues).
