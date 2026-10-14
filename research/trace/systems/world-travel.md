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

## Deep trace (rebuild module 9, 2026-10-14; archive travel-v1)
Every Travel-family path checked for a 0.589 trigger (Lua bindings → UI scripts in testclient/Resources/Scripts and
UI/Default, AS3 → Lua map re/out/ui_server_map.md, packet constructors in the decompile):
- **Teleport queue** (0xFC 6 ToQueuedLocation, 7 CancelQueue; S→C 5 PostQueueNotify): Lua Teleport.CancelQueue /
  TeleportToQueuedLocation; only Dialog.lua's teleport-queue dialog calls CancelQueue, and that dialog opens only
  when the server reports a queue (live: a full destination world). Our server never queues → unreachable by design.
- **PlayEffectRequest 9 / ManagedRequest 0xA**: sent only by PhysicsTeleportCallback (state machine FUN_143620610:
  effect "*Bamf!*" → wait → managed teleport with the area's target), created by ClientAreaTeleporter(Base /
  ClaimVisitation) for **area instances** with a teleport property (client AreaProperties type 23 / 38
  "enable_teleportation" on area definitions 7 / 26). Area instances come from the server's AreaDefinition 0xEA list,
  which we send empty (no live capture of zone areas exists). Unreachable until zone area data exists.
- **GetRespawnLocations 0xCD**: Lua Respawn.UpdateRespawnLocations — no 0.589 script calls it (respawnhandler.lua uses
  ReviveSelf / ReviveAtHub / ReviveFlee); the respawn list is sent unasked. Unreachable.
- **SetWaypoint 0x32/0x12**: Lua Ui.SetWaypoint / RemoveWaypoint — no script or SWF calls them. Unreachable.
- **Fog of war 0x102 / 0x103**: ClientFogOfWarManager (vtable slot 1 FUN_14341d310 sends the request; reply handler
  FUN_14341d280 loads a zone's fog data) — never requested in play (no zone with fog in 0.589 data). Unreachable.
- **Guide stones 0xB7**: S→C only, no 0.589 content. **Warp network 0x113**: handled (ClientRequest answered, network
  sent); travel cost item 1579 with 0 costs = free (client constants).
- Working and re-verified this pass: Spire / landscapes / own site / friend / random cave travel (casts 185-187,
  RandomUnderground, NextUnderground offers), re-zone rules (cave-teleport-hang), safe-point evac 0x7C.
- Audit 2026-10-14: Underground Adventure from Pioneer 416 and from the home continent both landed on the cavern floor
  (y 285.9 / 292.3; adhoc/cavefall). One earlier trip to a cavern entry at y -673.4 ended at y -1305 (a 632 m fall)
  although dev "cavecheck" finds a server floor under all 520 entry points — the client lacked that floor on arrival
  Depth sweep (adhoc/cavedepth*): entries down to y -640 land on the floor; at -780 / -895 the client is still loading
  after 35 s and the player then falls through. FIXED: random trips use entries at y >= -650 (about 430 of 520).
