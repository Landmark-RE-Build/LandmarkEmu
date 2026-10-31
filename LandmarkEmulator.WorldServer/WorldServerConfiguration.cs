using LandmarkEmulator.Database.Configuration;
using LandmarkEmulator.Shared.Configuration;

namespace LandmarkEmulator.WorldServer
{
    /// <summary>
    /// WorldServer.json, overridable by environment variables with the same names (ZoneServerId, Admins__0, ...).
    /// Every gameplay setting defaults to live's behaviour and stays a server-owner toggle (user 2026-10-18); extras beyond
    /// live default to off. Docker forwards each owner setting from .env (docker/world/docker-compose.yml, .env.example):
    /// the code default, the compose default and the .env.example value are kept the same.
    /// <para>Read directly from the configuration rather than through this class (also in WorldServer.example.json):
    /// ProceduralPlacement (false; Docker PROCEDURAL_PLACEMENT), ProceduralPlacementDecor (false;
    /// PROCEDURAL_PLACEMENT_DECOR), ProceduralPlacementMasks (true; PROCEDURAL_PLACEMENT_MASKS) - Game/Gathering.Load;
    /// ProceduralRespawn ("minmax"; PROCEDURAL_RESPAWN), ProceduralRespawnMinShare (0.67; PROCEDURAL_RESPAWN_MIN_SHARE) -
    /// Game/Gathering.Respawn; TreeHealth ("health"; TREE_HEALTH) - Game/Gathering.Health; ShortTeleportDistance (60), ShortTeleportWaitScreen (false) - Game/Teleport (a client-hang
    /// workaround, L116, not an owner setting); ItemDefinitionChunkSize (100; ITEM_DEFINITION_CHUNK_SIZE; 0 = one packet as
    /// the live dump) - Game/Inventory/ItemDefinitionManager (keeps the client's window responding while it takes the list);
    /// AdminMonitorPush (false) - Game/AdminMonitor; WebPagesPort (19001;
    /// WEB_PAGES_PORT), WebPagesHost (loopback; Docker 0.0.0.0), WebPagesUrl (empty = http://127.0.0.1:WebPagesPort) -
    /// Game/WebPages, Game/Store; SettingsOverridesFile (empty = settings-overrides.json beside the program; Docker
    /// /data/settings-overrides.json) - Game/ServerSettings, the admin Settings page, whose saved values are the last
    /// configuration source and win over everything above. Environment only: LANDMARK_SEASON, HAT_DROPS (Game/RareDrops);
    /// internal, not owner settings: LANDMARK_NOSTREAM (a test aid, Game/DevCommands.NoStreaming), ZONE_INTERNAL_PORT (set
    /// by docker/gateway/world.override.yml), GATEWAY_INTERNAL_ADDRESS (the older spelling of
    /// <see cref="GatewayInternalAddress"/>).</para>
    /// <para>Internal keys (no .env line on purpose; tools/wiki_reference.py INTERNAL_SETTINGS): Network__Host (Docker
    /// listens on every interface in the container; the port is WORLD_PORT), the Database__*__Provider keys (always
    /// MySql), <see cref="StoreLumenPrices"/> (the JSON form of STORE_PRICES).</para>
    /// </summary>
    public class WorldServerConfiguration
    {
        /// <summary>The game port (Docker: WORLD_PORT). Network__Host is internal: the listen address (Docker: every interface).</summary>
        public NetworkConfig Network { get; set; }
        /// <summary>Auth, Character and Zone connection strings (Docker sets them from DB_PASSWORD / WORLD_KEY); Provider is internal (MySql).</summary>
        public DatabaseConfig Database { get; set; }
        public string GameDataPath { get; set; }
        /// <summary>
        /// Lets every player build and place props anywhere, without a claim (the client's claim checks pass when the
        /// SendSelf flag at +0x287 is set). For testing and creative servers; off by default = live (claims were required;
        /// props only where you have build permission). Docker: FREE_BUILD.
        /// </summary>
        public bool FreeBuild { get; set; }

        /// <summary>
        /// Shift's sprint and the boot abilities (double jump, leaps, glide, Electric Charge) carry their client-run movement
        /// effects in the caster-info reply (Game/MovementEffects, OPEN-LEADS L160). true = live (the client moves only
        /// from those effects; sprint, Immediate Delivery and Endless Breeze are channels held with their key; the 2014
        /// Energy costs, cooldowns and Electric Charge's cast time; Cloud Burst's damage buff; Explosive Leap's take-off
        /// blast, L168); false sends none and answers every movement cast at once, as before 2026-10-18.
        /// Docker: MOVEMENT_EFFECTS.
        /// </summary>
        public bool MovementEffects { get; set; } = true;

        /// <summary>
        /// Melee weapon swings carry a client-run Sweep targeter in their caster-info reply; the client reports each
        /// opponent the sweep hits (ClientTargeterFindTarget 0x27/0x24) and the server strikes it after checking it against
        /// its own cone (Game/Combat/WeaponCombat.Targeters, OPEN-LEADS L170). true = live (the client's targeter); false
        /// sends none and the server resolves every swing itself, as before 2026-10-18. Docker: MELEE_TARGETERS.
        /// </summary>
        public bool MeleeTargeters { get; set; } = true;

        /// <summary>
        /// A blocking stance is shown to the other clients (Game/Combat/BlockStance, OPEN-LEADS L170): a player's Molten
        /// Rebuke / Corrupting Revenge stance and a creature's shield block set entity state bit 45 (PlayerUpdate
        /// UpdateCharacterStateDelta 0x26/0x51) and put the client's "Block" effect on the blocker (Abilities EffectStart
        /// 0x27/0x0C, arc and height) for the viewers in range, so their melee sweeps stop on the blocker and play the
        /// "blocked" effect, and clear both when it ends. true = live (the client's mechanism for blocking; the server still
        /// decides the damage); false sends nothing (others' swings then pass through a blocker on their screen, the
        /// server still blocks). Docker: BLOCK_STANCE_VISIBLE.
        /// </summary>
        public bool BlockStanceVisible { get; set; } = true;

        /// <summary>
        /// A dead player's body lies down on the other players' clients (Game/DeadBodies, OPEN-LEADS L196 OL-2): the client
        /// plays its death pose only from entity state bit 7 (the animation network's State_Dead, set every frame from
        /// FUN_142ea9ef0), so the others are sent UpdateCharacterStateDelta {set bit 7} at the death and to newcomers, and
        /// {clear bit 7} when the player is alive again. true = live (the client's mechanism; live had no other); false
        /// sends nothing (the body stands on the others' screens, as before 2026-10-26). Docker: DEAD_BODIES_VISIBLE.
        /// </summary>
        public bool DeadBodiesVisible { get; set; } = true;

        /// <summary>
        /// A launch the client reports (PlayerUpdate SafeEject 0x26/0x4C: a flinger, launcher or thrust pad threw the player
        /// and the client set its entity state bit 26) is ended when the player lands: UpdateCharacterStateDelta 0x26/0x51
        /// clears bit 26 on that player's client (Game/SafeEject). The client never clears the bit itself, so without it the
        /// player sends no second SafeEject and type-1 thrust pads never fire again for them. true = live [I: only the server
        /// can clear it]; false only logs the report, as before 2026-10-18. Docker: SAFE_EJECT_RESET.
        /// </summary>
        public bool SafeEjectReset { get; set; } = true;

        /// <summary>
        /// The character stats live sent: the player's own movement tuning at login (ClientUpdate UpdateStat: backpedal 0.65,
        /// strafe 0.75, crouch 0.25, acceleration / deceleration times …, the live capture) and the eight
        /// SEND_TO_REMOTE_CLIENT acceleration times to other players with each character (Game/CharacterStats, L162).
        /// true = live; false sends none (the client then uses its built-in defaults). Docker: LIVE_CHARACTER_STATS.
        /// </summary>
        public bool LiveCharacterStats { get; set; } = true;

        /// <summary>
        /// Placed props are streamed by 64 m region: a player is sent those within this many metres (nearest first, at
        /// zone-in and as they move) and loses them again 80 m further out (Game/Props.Stream.cs, bandwidth audit R8).
        /// 400 = the client's Render Distance slider at its last bounded step (99 %; 100 % is unlimited). 0 = every placed
        /// prop of the landscape at every zone-in and re-zone, as before 2026-10-18. Live's own range is not known.
        /// Dev "propstream &lt;metres&gt;" changes it until the restart. Docker: PROP_STREAM_RANGE.
        /// </summary>
        public float PropStreamRange { get; set; } = 400f;

        /// <summary>
        /// While a player's loading screen is up (after a login or a re-zone, until the client's ClientLoading {0}), the
        /// asset-heavy world streams wait: creatures, trees and gathering nodes, chests, outposts, warp crystals, the Spire's
        /// props other than its deck, and the placed props beyond the ones under the arrival point (Game/PlayerManager.cs
        /// "Stream hold"). It works around the 0.695 client's 64 allocator slots: on PCs with 16+ CPU threads the client's
        /// decompression / job workers that the asset burst wakes can take the last slots before its three terrain contour
        /// threads do, and the terrain then meshes at a crawl ("Avg contour" in the client log) for up to an hour
        /// (trace/0695/allocator-slots-options.md). Only the order changes: the same packets go out once the screen drops
        /// (or after <see cref="StreamHoldLimitSeconds"/>). false = everything at zone-in, as before 2026-10-21.
        /// Docker: STREAM_HOLD_UNTIL_LOADED.
        /// </summary>
        public bool StreamHoldUntilLoaded { get; set; } = true;

        /// <summary>
        /// The longest a <see cref="StreamHoldUntilLoaded"/> hold lasts (seconds; from the login's world entry, or from a
        /// re-zone's zone-in): a client that never reports its loading screen down gets the streams anyway. 10-600.
        /// Docker: STREAM_HOLD_LIMIT_SECONDS.
        /// </summary>
        public int StreamHoldLimitSeconds { get; set; } = 90;

        /// <summary>
        /// Keep the vertex-only voxels (surface position, no material) that clients send with build edits: they keep
        /// edited surfaces smooth (line slopes, spheres) when the server sends the cell back; dropping them gave
        /// saw-toothed lines and stair-stepped fills after a relog (2026-10-14). The client stores and meshes them
        /// itself (its own cache, Cache/2/Voxel/416, held 67,662 of them in 45 of the player's cells). The server never
        /// makes such voxels itself. Dev "vertexonly on|off" switches it while running.
        /// <para>The Environment Server has the same switch (EnvServer.json KeepVertexOnlyVoxels): with
        /// <see cref="EnvServerAddress"/> set the terrain store is the service's, so ITS value is the one that counts;
        /// keep both the same. Docker sets both from one .env line, KEEP_VERTEX_ONLY_VOXELS.</para>
        /// </summary>
        public bool KeepVertexOnlyVoxels { get; set; } = true;

        /// <summary>
        /// Serve coarse (LOD) copies of player edits (Game/Terrain/TerrainLod), as live did: without them edits vanish
        /// when the client draws the area at a coarser level (from a distance). On by default.
        /// <para>The Environment Server has the same switch (EnvServer.json TerrainLodCopies): with
        /// <see cref="EnvServerAddress"/> set the service builds and serves the copies, so ITS value is the one that
        /// counts; keep both the same. Docker sets both from one .env line, TERRAIN_LOD_COPIES.</para>
        /// </summary>
        public bool TerrainLodCopies { get; set; } = true;

        /// <summary>
        /// The Landmark 0.695 install the server reads world data from (biome maps, cave placements, models; read-only,
        /// game files are never changed). Required: with Docker it is GAME_DIR in .env, mounted at /game (README.md,
        /// docs/docker.md); without Docker set it in WorldServer.json.
        /// </summary>
        public string GameDirectory { get; set; } = "";

        /// <summary>This world's row in the login database's zone_server table (its heartbeat for character select).</summary>
        public ulong ZoneServerId { get; set; } = 1;

        /// <summary>The default <see cref="WorldNameId"/>: 560916 "Serenity" (also docker/world compose and .env.example).</summary>
        public const uint DefaultWorldNameId = 560916;

        /// <summary>
        /// This world's name in the server list: a client text id (0 = keep the stored one; a new row then gets 62147). The
        /// client has the live server names: 560911 Courage, 560912 Liberation, 560913 Satisfaction (EU), 560914
        /// Understanding (EU), 560915 Rebellion, 560916 Serenity, 560917 Determination, 560918 Confidence, 560919
        /// Adventure, 560920 Gratitude, 560921 Prosperity, 560922 Success, 560923 Triumph, 560925 Opportunity, 560926
        /// Inspiration; 62147 is "Dev Server" (560924, "Victory" in 0.589, reads "Victory Node" in 0.695: not a name). Default 560916 Serenity. A live name is also the name
        /// shown in game (<see cref="ClientWorldNameText"/>) and picks the Landmarks of Landmark set
        /// (<see cref="LandmarksOfLandmarkWorld"/> auto). Docker: WORLD_NAME_ID.
        /// </summary>
        public uint WorldNameId { get; set; } = DefaultWorldNameId;

        /// <summary>
        /// The world id this world's clients are told (zone details, ClientBeginZoning +0xa8, the Landscapes window;
        /// Game/Landscapes). 0 = 2, the live 406 capture's. Worlds that share a MultiWorld gateway (cross-world travel,
        /// TASKS B.10) need different ids, and never 3 (the Events world). Docker: CLIENT_WORLD_ID.
        /// </summary>
        public uint ClientWorldId { get; set; }

        /// <summary>
        /// This world's name in game (Landscapes window, zone details, the group / guild location): a client text id.
        /// 0 (default) = as live, where every world showed its own name: <see cref="WorldNameId"/> when that is a live
        /// world name (560911-560926), else 560912 "Liberation" (the live 406 capture's; e.g. for 62147 "Dev Server",
        /// which is not a world name). Game/Landscapes.WorldNameText. Docker: CLIENT_WORLD_NAME_TEXT.
        /// </summary>
        public uint ClientWorldNameText { get; set; }

        /// <summary>
        /// "host:port" game clients use to reach this world, written to its server-list entry at start (empty = keep the
        /// stored one). Each world container sets its own (docs/docker.md).
        /// </summary>
        public string PublicAddress { get; set; } = "";

        /// <summary>
        /// Continent of the home (starting) landscape (sent in SendZoneDetails; terrain heights, caves, veins, warp network
        /// and loot chests are loaded for it). Default 406 = the live capture's. The Environment Server has its own
        /// (EnvServer.json Continent) and the two must be the same; Docker sets both from one .env line, HOME_CONTINENT.
        /// </summary>
        public uint Continent { get; set; } = 406;

        /// <summary>
        /// Whose Landmarks of Landmark stand beside the Spires (Game/LandmarksOfLandmark; live placed a different set per
        /// world, forum 3 Aug 2016): auto (the live world named by <see cref="WorldNameId"/>, or the teleport window's
        /// Liberation when that is 0; any other name: Courage), Courage, Determination, Liberation, Rebellion, Serenity, or
        /// off. A landscape keeps the landmark it was given first. Docker: LANDMARKS_WORLD.
        /// </summary>
        public string LandmarksOfLandmarkWorld { get; set; } = "auto";

        /// <summary>
        /// Which landscapes have Chaos Caverns with outposts for Travel → An Underground Adventure and the Lost Crystals'
        /// "Eureka!" trips (Game/Caves, Game/Outposts; audit-outposts-695.md F2): "all" (default, as live from June
        /// 2016: Underground Adventure trips to Chaos Caverns in any zone, P5927, and Lost Crystal trips to other
        /// worlds and biomes, P5734) — every non-Pioneer island with cave data; "home" — the home continent
        /// only, as before 2026-10-16; or zone ids separated by commas ("406,391,393"). Another landscape's outposts are
        /// stamped in the background on its first visit (minutes, ~110 MB of terrain edits each). Read at start-up.
        /// Docker: CAVERN_LANDSCAPES.
        /// </summary>
        public string CavernLandscapes { get; set; } = "all";

        /// <summary>
        /// Also stamp the outposts of landscapes nobody has visited yet, one landscape after another in the background
        /// (with <see cref="CavernLandscapes"/>); off: a landscape is stamped on its first visit or when a trip picks it.
        /// The first full run takes about 4 minutes per landscape and ~2.5 GB of terrain edits for all 23. Docker:
        /// OUTPOST_PRELOAD.
        /// </summary>
        public bool OutpostPreload { get; set; }

        /// <summary>
        /// Outposts change caverns (Game/Outposts.Rotation.cs; audit-outposts-695.md F8): every this many hours per
        /// landscape, a share (<see cref="OutpostRotationShare"/>) of its outposts gets another ruin in its own cavern,
        /// stamped in the background (the old ruin's box reverted), leaving alone any outpost a player is near. The client's
        /// help page says "Outposts exist in the open world and change regularly"; no source gives a period, so 0 (default)
        /// = never, as before. Writes terrain edits and the outpost markers. Docker: OUTPOST_ROTATION_HOURS.
        /// </summary>
        public double OutpostRotationHours { get; set; }

        /// <summary>The share of a landscape's outposts one rotation replaces (0-1; default 0.25). Docker: OUTPOST_ROTATION_SHARE.</summary>
        public double OutpostRotationShare { get; set; } = 0.25;

        /// <summary>
        /// Creature navigation (Game/Pathing, TASKS 6.61): Chaos Cavern and outpost creatures walk around cave rock and ruin
        /// walls (a walkable grid from the cave pieces and the outposts' ruin grids, paths searched on a background thread),
        /// and surface creatures off build sites around cliffs; off = every creature steps straight at its goal, as before.
        /// Default on (it falls back to the old straight step wherever its data is missing or contradicts the world). Read
        /// at first use; the dev command "pathing on|off" switches it until the restart. Docker: CREATURE_PATHING.
        /// </summary>
        public bool CreaturePathing { get; set; } = true;

        /// <summary>
        /// Story creatures see player builds (Game/Pathing site grids, OPEN-LEADS L133): on a build site they path around
        /// player-built walls (through doorways), dug holes and placed props with a path-block radius, as live's creatures did
        /// (P5781, forum 56713); a Chaos Cavern outpost players dug into also gets its holes for its creatures. A coarse grid
        /// per site from the Environment Server, built in the background on the first story creature step and again 2 s after
        /// the last edit or prop change there; nothing is sent to clients. Default on (live); off = story creatures step
        /// straight at their goal, as before. Needs CreaturePathing. Dev "sitepathing on|off". Docker: SITE_PATHING.
        /// </summary>
        public bool SitePathing { get; set; } = true;

        /// <summary>
        /// Creatures hold weapons (Game/CreatureWeapons.cs, OPEN-LEADS L95): a weapon model in the main hand of every
        /// humanoid creature kind, sent once per creature to each player that has it as a full entity
        /// (EquipmentSetCharacterEquipmentSlot); a story creature shows it only while its weapons are Equipped (Story Node
        /// row 41 / spawner row 40 / the Equip / Unequip Links). Live's creatures held weapons, but which actor each held
        /// was server data not in the client: the table is a best guess (the weapon type from each creature prop's palette
        /// icon [P], the actor from the players' weapon of the creature's affinity or its art set [W/I]; user decision
        /// 2026-10-18). Default on; false = no creature weapons. Dev "creatureweapon" works either way. Docker:
        /// CREATURE_WEAPONS.
        /// </summary>
        public bool CreatureWeapons { get; set; } = true;

        /// <summary>
        /// Surface graveyards (Game/Graveyards.Landscape.cs, FEATURES #47): the map's red circles — Flee sites on the surface
        /// with a 30 m buffer zone creatures leave alone, where build sites can't be placed. No client file says where live
        /// put them, so ours stand one per 1 km of dry, level land (inferred), built from the client's props-only
        /// graveyard01 template; nothing is written to the terrain or the database. The Chaos Caverns' graveyards come from
        /// the client's cave placements and are always on. Read at start-up. Docker: SURFACE_GRAVEYARDS.
        /// </summary>
        public bool SurfaceGraveyards { get; set; } = true;

        /// <summary>
        /// The Spire area (Game/Spire.cs): the client's own 90 m sphere round each Spire (GlobalAreas 3), as live after the
        /// 6 Oct 2014 hotfix — no build sites in it (the client's cursor and the server), no mining, harvesting or building,
        /// no trees or plants. Off = only the platform (no trees on it, no mining or building within 10 m of it) and build
        /// sites allowed. Read at start-up. Docker: SPIRE_AREA.
        /// </summary>
        public bool SpireArea { get; set; } = true;

        /// <summary>
        /// Build the Chaos Caverns' graveyards into the terrain (Game/Graveyards.Landscape.cs): the client's own
        /// graveyard_launch template (a raised plinth, gravestones, glowing mushrooms, four gargoyles) stamped once at each
        /// of the 81 graveyard placements of a landscape's Underground cave file, turned as the placement says, in the
        /// background on the landscape's first visit (a few MB of terrain edits per landscape, remembered in
        /// TerrainEdits/graveyards_&lt;zone&gt;.txt so a restart never stamps again). Default on: live had them (the client
        /// parses the placements and leaves them to the server). Off: no new stamps — a graveyard not stamped yet is only
        /// its centrepiece on the cave floor, as before; one stamped earlier stays as stamped (it is in the terrain).
        /// Read at start-up. Docker: CAVERN_GRAVEYARD_STAMPS.
        /// </summary>
        public bool CavernGraveyardStamps { get; set; } = true;

        /// <summary>
        /// The fall guard of the cavern trips (Game/CavernArrivalGuard): a player who falls through the world in the first
        /// 30 s after an Underground Adventure's loading screen dropped (more than 12 m below the arrival, where no cave floor
        /// is) is re-zoned back to the arrival, the second time to the landscape's shallowest graveyard open to trips.
        /// Default on (ours: live's answer is not known). Read at each trip. Docker: CAVERN_ARRIVAL_RESCUE.
        /// </summary>
        public bool CavernArrivalRescue { get; set; } = true;

        /// <summary>
        /// The island on the "Events" world of the Landscapes window (0.695 clients; Game/Landscapes.ActiveEventZone):
        /// auto (live's 2016 dates: Halloween Party 3 Oct - 20 Nov, Winter Holiday Party 21 Nov - 10 Jan), halloween,
        /// winter, judgement, or off. Read at start-up. Docker: EVENT_ISLAND. Default auto = live (event-islands-695.md,
        /// P6532): the islands' heights were baked on 2026-10-16 (TerrainHeights460-462.bin; docs/terrain.md "Event
        /// islands"). The dev command "landscape 460" still reaches a closed island.
        /// </summary>
        public string EventIsland { get; set; } = "auto";

        /// <summary>
        /// When the Events world's island changes between two runs (Halloween closes, Winter opens, or the island closes
        /// for the season), the build sites left on the island that closed are expired like a site whose upkeep ran out:
        /// a Design is saved, the terrain reverts, props and resources go to the owner by mail with the Build Site
        /// Contents item (Game/Landscapes.EventExpiry.cs). Live did this at the switch (sites left on the Halloween
        /// island were expired that morning, forum 58077, 21 Nov 2016). Default true = live
        /// (user 2026-10-18: "togglable … but how it was on live"); a server owner can set false to keep the sites
        /// (unreachable from the menu until the island opens again; the start-up log names them). It deletes builds:
        /// back up before switching islands. Docker: EVENT_ISLAND_EXPIRE_ON_CLOSE.
        /// </summary>
        public bool EventIslandExpireOnClose { get; set; } = true;

        /// <summary>
        /// At every start, build sites still on the old Pioneer lands 417-421 (the server's Pioneer lands before
        /// 2026-10-17; live's are 464-468) move to the replacement land of the same biome (417→464, 418→466, 419→465,
        /// 420→467, 421→468): a design of the owner's build there is saved, the site gets a free spot on the new land
        /// (Pioneer start order) with its id, name, access and settings; the old voxels and props stay behind unseen
        /// (Game/Pioneer.cs MoveOldSites). Default true (user decision 2026-10-17); live never renumbered its Pioneer lands,
        /// so this has no live behaviour to match. False leaves them (unreachable; the start-up log names them).
        /// Docker: MOVE_OLD_PIONEER_SITES.
        /// </summary>
        public bool MoveOldPioneerSites { get; set; } = true;

        /// <summary>
        /// The slate slab under a new Pioneer site (Game/Pioneer.Slab.cs, OPEN-LEADS L180): live fixed how the slate
        /// slab is placed on new player sites (patch notes 30 Jun 2016, design/forum/patches/ 2016-06-29-57596) [P that
        /// new player sites had one]. The client ships the slabs as CommonData/Voxel/templates/
        /// claim*.otd, which no client code loads [P negative], so they are server-side assets (trace/0695/agent-leadsC.md
        /// L180). "claim3x3" (default) = the 210 m plus-shaped 2 m slab (525 × 5 × 524 voxels, the launch site's
        /// footprint); "claim1x1" = its 70 m middle tile only; "off" = none. Which template live stamped and at what height
        /// are not known [I]; the slab is centred under the site, flush with the ground at its middle. Only sites granted
        /// on a Pioneer land's surface from now on get one (never moved, cavern or classic-landscape sites), once each.
        /// Docker: PIONEER_SLAB.
        /// </summary>
        public string PioneerSlab { get; set; } = "claim3x3";

        /// <summary>
        /// The slab's material, a VoxelMaterials ARRAY_INDEX. The templates store ARRAY_INDEX 53, which in the 0.589 and
        /// 0.695 tables is "Raw Amaranthine" (an ore), not a slate: a stale index from an older table [I]; the oldest
        /// material table the client still ships, CommonData/Voxel/VoxelMaterials1.xml (55 VoxelFarm materials), has id 53
        /// "Biome_Everfrost_Snow02", no slate either, and VoxelMaterialRemaps.txt maps neither 53 nor 427 (OPEN-LEADS L180,
        /// trace/0695/phaseE2-social.md). Default 26 =
        /// plain Worked Slate, the free slate the client's starter shapes use (starter_pillars = 41 / 46 / 26) [I: the
        /// patch note says "slate"]. 0 = the template's own stored material. Docker: PIONEER_SLAB_MATERIAL.
        /// </summary>
        public uint PioneerSlabMaterial { get; set; } = 26;

        /// <summary>
        /// Player Studio peer review: approvals that list a product, and rejections that turn it down (the live
        /// server's numbers are not in the client or the archive: P1207-P1210 give only the reviewer rules; 3 / 3 is
        /// ours). In-process commerce only: with <see cref="CommerceAddress"/> set, the commerce service's own
        /// CommerceServer.json values count. Docker: PLAYER_STUDIO_APPROVALS / PLAYER_STUDIO_REJECTIONS (forwarded to the
        /// world and to the commerce service alike, so both modes agree).
        /// </summary>
        public uint PlayerStudioApprovals { get; set; } = 3;

        /// <summary>Peer-review rejections that turn a Player Studio product down (see <see cref="PlayerStudioApprovals"/>).</summary>
        public uint PlayerStudioRejections { get; set; } = 3;

        /// <summary>
        /// Who may peer-review Player Studio products: players whose account has spent at least this much Daybreak Cash
        /// (converted at <see cref="LumensPerDaybreakCash"/>: 1000 → 1,300 Lumens) on the store and on Player Studio
        /// designs. 1000 = live: reviewers had bought at least 1000 SC on the Marketplace before (Update Notes 18 Jun
        /// 2014, P1207; wiki Player Studio: 1000 Daybreak Cash at least); 0 =
        /// anyone but the creator. Checked by the world in both commerce modes. Docker: PLAYER_STUDIO_REVIEWER_MIN_SPEND.
        /// </summary>
        public uint PlayerStudioReviewerMinSpend { get; set; } = 1000;

        /// <summary>
        /// Products one player may review in 24 hours; 0 = no limit. 2 = live: at most two review offers per player a
        /// day (P1209). In-process commerce only: with <see cref="CommerceAddress"/>
        /// set, the commerce service's own value counts. Docker: PLAYER_STUDIO_REVIEWS_PER_DAY (world and commerce alike).
        /// </summary>
        public uint PlayerStudioReviewsPerDay { get; set; } = 2;

        /// <summary>
        /// On (live) = a product the peers reject (<see cref="PlayerStudioRejections"/>) is not turned down but waits for
        /// staff, as live's "Customer Service queue" (P1210: Customer Service then confirmed or overturned the players'
        /// verdict; status "CS Review", Update Notes 26 Jun 2014): the admin page's Player Studio list approves or
        /// rejects it. Off = peer rejections turn it down at once. In-process commerce only: with
        /// <see cref="CommerceAddress"/> set, the commerce service's own value counts. Docker: PLAYER_STUDIO_STAFF_REVIEW.
        /// </summary>
        public bool PlayerStudioStaffReview { get; set; } = true;

        /// <summary>
        /// A reviewer whose rejections staff overturned this many times (a product they voted down that staff then listed)
        /// is offered no more reviews; 0 = never held back. 1 = live as the wiki describes it (Player Studio: a reviewer whose
        /// rejection Daybreak staff disagreed with "will stop getting items to review" [W]; the count is ours [I]) —
        /// Internal/Commerce/StudioRules.MayStillReview, OPEN-LEADS L185. In-process commerce only: with
        /// <see cref="CommerceAddress"/> set, the commerce service's own value counts. Docker: PLAYER_STUDIO_REVIEWER_OVERRULES
        /// (world and commerce alike).
        /// </summary>
        public uint PlayerStudioReviewerOverrules { get; set; } = 1;

        /// <summary>
        /// The Player Studio creator's share of what a buyer pays for their design, in percent (Game/Commerce.TakeDesign;
        /// in-process commerce only — the commerce service has its own CommerceServer.json setting). 40 = live's split:
        /// a 60/40 split of each sale's net revenue (Fairan [DEV], Update Notes 18 Jun 2014,
        /// design/forum/all/2014-06-18-41434), read by the players as Daybreak keeping 60 % and the creator 40 % (a
        /// player in the same thread) [W]. Live paid it quarterly in real money; here it is
        /// the Lumens the buyer paid, mailed to the creator at once [I]. 0 = creators get nothing. Docker:
        /// PLAYER_STUDIO_CREATOR_SHARE.
        /// </summary>
        public uint PlayerStudioCreatorSharePercent { get; set; } = 40;

        /// <summary>
        /// Player Studio listing fee (Game/Commerce: CommerceConfiguration 0x2E, the wallet, ProductCreate). On
        /// (default) = live: every Player Studio submission paid a listing fee of 10 % of its price, at least 100 SC
        /// (Fairan [DEV], Update Notes 18 Jun 2014), still a 100 DBC fee in Oct 2016 (Syrah, Game Update 14 Oct 2016),
        /// non-refundable (client text 563260); here in Lumens at <see cref="LumensPerDaybreakCash"/>
        /// (130 Lumens minimum, 13 % at 1.3), charged when a design is submitted and given back only if the server refuses
        /// the listing. The client shows the fee and refuses a submission the wallet cannot pay (0x10). Off = no fee. Never
        /// charged with <see cref="FreeStore"/> on. Docker: PLAYER_STUDIO_LISTING_FEE.
        /// </summary>
        public bool PlayerStudioListingFee { get; set; } = true;

        /// <summary>
        /// Message of the day, shown once per login (PacketMOTD 0x5A): the client prints "subject\nbody" as a system
        /// message and adds a row to its UI.MessageOfTheDay data source. Nothing is sent when both are empty. The text is
        /// each server's own (live's messages are not in the archive). Docker: MOTD_SUBJECT.
        /// </summary>
        public string MotdSubject { get; set; } = "Welcome to Landmark";

        /// <summary>The message of the day's body (see <see cref="MotdSubject"/>). Docker: MOTD_BODY.</summary>
        public string MotdBody { get; set; } = "This server is LandmarkEmu, a fan-made recreation. Have fun building!";

        /// <summary>
        /// Bootstrap admins for the in-game admin menu (/admin, <see cref="Game.Admin"/>), OR'd with the auth DB
        /// <c>admin</c> table. Each entry is "account:Name" (only the account username is checked), "character:Name"
        /// (only the character name) or a bare name (either; ignored while an account and another player's character
        /// share it, docs/admin.md). Docker: ADMIN_ACCOUNT, ADMIN_2, ADMIN_3 (Admins__0-2); more in <see cref="AdminList"/>.
        /// </summary>
        public System.Collections.Generic.List<string> Admins { get; set; } = new();

        /// <summary>
        /// More bootstrap admins, comma separated ("Alice,Bob,Carol"), added to <see cref="Admins"/> — one .env line for
        /// any number of them (Docker: ADMINS). Empty = none. Prefer the auth DB <c>admin</c> table for day-to-day
        /// adds/removes.
        /// </summary>
        public string AdminList { get; set; } = "";

        /// <summary>
        /// The dev commands that run code or raw packets in a game client (lua, luacode, luas, send; Game/DevCommands,
        /// Game/ClientLua) may target any player. false (default): only the caller (the admin who ran it, when known) and
        /// players who are admins themselves; other targets are refused and the refusal logged. Also false: code naming a
        /// client native that opens the player's own desktop web browser (Ui.OpenSupportUrl, Ui.LaunchBrowser, BrowseTo ...,
        /// <see cref="Game.ClientLua.ExternalBrowserNatives"/>) is refused for every target. Those commands are test tools;
        /// live had no such thing. true = both checks off (every use is still logged with caller and target).
        /// Docker: DEV_CLIENT_CODE_ON_OTHERS.
        /// </summary>
        public bool DevClientCodeOnOthers { get; set; }

        /// <summary>
        /// Players allowed in the world at once; more logins wait in the login server's queue (reported with the
        /// heartbeat; members first). 0 = no limit (default: live queued logins, E.8 trace, but its cap is not known).
        /// Docker: MAX_PLAYERS.
        /// </summary>
        public int MaxPlayers { get; set; }
        /// <summary>
        /// Players allowed on one landscape at once; more Travel-window arrivals wait in the travel queue (the client's
        /// "This location is currently full" dialog, then a Game Action Queue "teleport" item when a place frees;
        /// <see cref="Game.TravelQueue"/>). 0 = no limit. Dev "landscapecap" changes it while running. Docker: LANDSCAPE_CAPACITY.
        /// </summary>
        public int LandscapeCapacity { get; set; }

        /// <summary>
        /// The Showcase store (Game/Store, TASKS 6.60). Off (default) = live's store: only what the live Marketplace sold
        /// in 2016 (Build Site Expansion, Fantasy Parlor Pack, the six pet figurines, the Superb / Legendary Reward
        /// Chests), each for Lumens at <see cref="LumensPerDaybreakCash"/>; everything else keeps its live route (Lumen
        /// Station, discovery, drops, goals). On = every outfit, decoration and design free, as before 2026-10-17 (a
        /// creative / test server). Nothing is ever paid with real money. Docker: FREE_STORE.
        /// </summary>
        public bool FreeStore { get; set; }

        /// <summary>
        /// What one Daybreak Cash was worth in Lumens on live, for the store items live sold for Daybreak Cash
        /// (trace/0695/agent-store-entitlements.md §1): 1.3 = the Lumen Box's 650 Lumens for 500 DBC (client texts
        /// 574410-574422) and the 65-Lumen grid of 939 of the 943 Lumen Station prices. Docker: LUMENS_PER_DBC.
        /// </summary>
        public double LumensPerDaybreakCash { get; set; } = 1.3;

        /// <summary>
        /// Store prices set by the server owner, in Lumens, by item id ("900001": 1000; 0 = free). Overrides the converted
        /// price of a live Marketplace product, and adds any other item of the full catalogue to the store at that price.
        /// Used only while <see cref="FreeStore"/> is off. Environment: StoreLumenPrices__&lt;item id&gt;; with Docker the
        /// one-line form <see cref="StorePrices"/> (STORE_PRICES) is easier, so this dictionary has no .env line of its own
        /// (internal form). An entry here wins over the same id there.
        /// </summary>
        public System.Collections.Generic.Dictionary<string, uint> StoreLumenPrices { get; set; } = new();

        /// <summary>
        /// The same owner prices as <see cref="StoreLumenPrices"/> in one line: "item id=Lumens" pairs, comma separated
        /// ("900001=500,2551=1300"; 0 = free). Empty (default) = live's Marketplace prices only. Docker: STORE_PRICES.
        /// </summary>
        public string StorePrices { get; set; } = "";

        /// <summary>
        /// Account entitlements (Game/Entitlements, TASKS 6.59): on (default) = every character is treated as a
        /// Trailblazer founder, a Year Zero survivor and a 2013 / 2014 promotion winner: it is sent those items once and
        /// may place their props. Off = nobody is: the founder / Year Zero / promotional props leave the palette (items
        /// already handed out stay). Live gave them only to buyers, survivors and winners (P0247; wiki "Year Zero
        /// Trophy"); on is the user's decision (2026-10-17, confirmed 2026-10-18: founder items for everyone), kept as a
        /// toggle. Docker: FOUNDER_ENTITLEMENTS.
        /// </summary>
        public bool FounderEntitlements { get; set; } = true;

        /// <summary>
        /// Which entitlement packages <see cref="FounderEntitlements"/> grants, comma separated: founder (Founder's Pack
        /// items and recipes), yearzero (the Year Zero trophy and outfit), promo (Iron Portcullis, Fan Faire souvenirs).
        /// Docker: ENTITLEMENT_PACKAGES.
        /// </summary>
        public string EntitlementPackages { get; set; } = "founder,yearzero,promo";

        /// <summary>
        /// How entitlement items arrive (Game/Entitlements). "auto" (default) = per package as live:
        /// <list type="bullet">
        /// <item>founder → the bag: founders found the pick on their character at login (wiki "Founder's Pickaxe") and
        /// the
        /// items stayed with the character through wipes (P0247) [W]; what doesn't fit is mailed;</item>
        /// <item>yearzero → mail: live mailed the Year Zero gifts (wiki "Year Zero Trophy"), and the client ships the
        /// letter's own texts 568827-568829 [P];</item>
        /// <item>promo → mail [I: how live handed out giveaway prizes and Fan Faire souvenirs is not known].</item>
        /// </list>
        /// "mail" or "bag" = every package that way; or per package, "founder=bag,yearzero=mail,promo=bag" (a package not
        /// named keeps its auto way). Docker: ENTITLEMENT_DELIVERY.
        /// </summary>
        public string EntitlementDelivery { get; set; } = "auto";

        /// <summary>
        /// Scheduled restarts (Game/RestartSchedule): days and local time, e.g. "Mon,Sat 03:00" or "Daily 05:00"; empty
        /// (default) = none. Live restarted on Monday and Saturday mornings at 3 a.m. Pacific from 25 Jun 2016 (P5902,
        /// P5906; earlier 5 a.m. three nights a week, P3643): an owner who wants that sets "Mon,Sat 03:00" with
        /// <see cref="RestartTimeZone"/> America/Los_Angeles (operational, so it stays the owner's choice: user
        /// 2026-10-18). The countdown starts <see cref="RestartWarningSeconds"/> before, and the container's restart
        /// policy starts the world again. Docker: RESTART_SCHEDULE.
        /// </summary>
        public string RestartSchedule { get; set; } = "";
        /// <summary>The schedule's time zone (IANA or Windows id); live's was Pacific time, "America/Los_Angeles". Docker: RESTART_TIME_ZONE.</summary>
        public string RestartTimeZone { get; set; } = "America/Los_Angeles";

        /// <summary>
        /// The time zone whose calendar days count as build site upkeep login days (Game/SiteUpkeep.cs DayZone; IANA or
        /// Windows id; unknown or empty = UTC). Default America/Los_Angeles = live: "each day (determined as 12:00:00AM to
        /// 11:59:59 PM Pacific Time) you log in will be tracked" (developer, Upcoming Claim Upkeep Changes, 17 Mar 2015).
        /// Docker: UPKEEP_TIME_ZONE.
        /// </summary>
        public string UpkeepTimeZone { get; set; } = "America/Los_Angeles";
        /// <summary>
        /// How long before a scheduled restart the warnings start (the countdown's first step is 1 hour; live's lead time
        /// is not in the archive). Docker: RESTART_WARNING_SECONDS.
        /// </summary>
        public int RestartWarningSeconds { get; set; } = 3600;
        /// <summary>
        /// A file the world touches every few seconds while its tick runs (Game/Watchdog), for the container's
        /// healthcheck; empty = none (Docker: /tmp/world-alive).
        /// </summary>
        public string HealthFile { get; set; } = "";
        /// <summary>
        /// Seconds without a server tick before the world ends itself to be restarted (0 = never; default 180, as in
        /// Docker). Never while a debugger is attached (a breakpoint stops the tick). Outside Docker something must start
        /// the world again (a service manager, a loop in a script): a hung world is down either way. Docker:
        /// WATCHDOG_SECONDS.
        /// </summary>
        public int WatchdogSeconds { get; set; } = 180;
        /// <summary>
        /// Seconds without any packet from a client before its connection is closed and the character removed from the
        /// world at once (others get the despawn): a crash, a killed process, Alt+F4, power or network loss. Default 60 =
        /// two of the 0.695 client's 29 s SOE keep-alives (UdpLibrary keepAliveDelay, FUN_143a0a980; sent from its UDP
        /// thread even in loading screens) and a little more, so one lost keep-alive is survived; in the world the client
        /// also sends KeepAlive 0x63 every second. Below 30 is raised to 30; 0 = never (only the 120 s
        /// unacknowledged-data rule). Live's server value is not known (the client's own limit for a silent server is
        /// 90 s). This world's in-process gateway reads it at each check; with a separate gateway (GATEWAY_INTERNAL_ADDRESS)
        /// the gateway container's value counts (the same .env line). Docker: CLIENT_TIMEOUT_SECONDS.
        /// </summary>
        public double ClientTimeoutSeconds { get; set; } = Shared.Network.SocketHeartbeat.DefaultClientTimeoutSeconds;
        /// <summary>The message shown with the restart warnings (ours: live's wording is not in the archive). Docker: RESTART_MESSAGE.</summary>
        public string RestartMessage { get; set; } = "Scheduled server restart.";

        /// <summary>
        /// "host:port" of the shared social service (LandmarkEmulator.SocialServer; Docker: social:20200). Set: presence,
        /// friends and ignore lists are the service's, shared by every world (Game/Social/RemoteSocialService). Empty:
        /// this world runs the social code itself, as before (LocalSocialService). The environment variable
        /// SOCIAL_ADDRESS is used when this is empty.
        /// </summary>
        public string SocialAddress { get; set; } = "";

        /// <summary>
        /// "host:port" of the shared commerce service (LandmarkEmulator.CommerceServer; Docker: commerce:20400;
        /// world-channel-service.md §8 step 8). Set: Player Studio / Marketplace, the wallet and account answers, peer
        /// review, contests and portraits are the service's, shared by every world (Game/WorldChannel/RemoteCommerceService);
        /// the Showcase web pages stay on this world and read their commerce data from it. Empty: this world runs the
        /// commerce code itself, as before (LocalCommerceService). The environment variable COMMERCE_ADDRESS is used when this
        /// is empty.
        /// </summary>
        public string CommerceAddress { get; set; } = "";

        /// <summary>
        /// "host:port" of this world's Environment Server (LandmarkEmulator.EnvServer; Docker: envserver:20500;
        /// trace/architecture/terrain-service.md §7). Set: the terrain store (edits, heals, veins, ownership, LOD copies,
        /// stamps) is the service's (Game/Terrain/RemoteEnvironmentServer) and this world's TerrainEdits folder keeps only
        /// the placement markers. Empty: this world runs the terrain store itself, as before (LocalEnvironmentServer). The
        /// environment variable ENVSERVER_ADDRESS is used when this is empty.
        /// </summary>
        public string EnvServerAddress { get; set; } = "";

        /// <summary>How long start-up waits for the Environment Server's index before going on without it (seconds).</summary>
        public uint EnvServerStartupWaitSeconds { get; set; } = 120;

        /// <summary>
        /// "host:port" of a gateway running as its own process (LandmarkEmulator.GatewayServer, gateway-service.md): this
        /// world then opens no UDP port, dials the gateway and serves the clients it attaches. Empty: the gateway runs in
        /// this process on the world port, as before. The environment variable GATEWAY_INTERNAL_ADDRESS (internal, an older
        /// spelling) is used when this is empty. Docker: set by docker/gateway/world.override.yml, not by .env.
        /// </summary>
        public string GatewayInternalAddress { get; set; } = "";

        /// <summary>
        /// Membership bought with Lumens (Game/Membership, TASKS 3.12; trace/0695/agent-membership.md): its price in Lumens
        /// for <see cref="MembershipDays"/> days. Default 1950 [I]: live sold All Access for real money (US$14.99 a month,
        /// not in the archive) — 1499 Daybreak Cash at the store's 1.3 Lumens per Daybreak Cash, rounded up to 5 Lumens.
        /// 0 = membership can't be bought. Docker: MEMBERSHIP_PRICE_LUMENS.
        /// </summary>
        public uint MembershipPriceLumens { get; set; } = 1950;

        /// <summary>Days of membership one purchase adds (default 30 [I]: All Access was a monthly plan). Docker: MEMBERSHIP_DAYS.</summary>
        public uint MembershipDays { get; set; } = 30;

        /// <summary>
        /// The trial key (29 Aug 2016, P6417): each account is mailed one "Invite your friends!" key, which can be mailed
        /// once to a friend and gives that friend's account <see cref="TrialKeyDays"/> days of trial membership when used.
        /// Off = no key letters, and keys already handed out can't be used. Docker: TRIAL_KEYS.
        /// </summary>
        public bool TrialKeys { get; set; } = true;

        /// <summary>Days of trial membership a used trial key gives (live: "a single-use, 10-day trial key"). Docker: TRIAL_KEY_DAYS.</summary>
        public uint TrialKeyDays { get; set; } = 10;

        /// <summary>
        /// Player vendor shops on the Vendor Booth (prop 1491). Off = live: on 1 Apr 2016 (P4378) the booth's "old,
        /// non-functioning UI" was removed and it became "simply a decorative prop". On = our working shop (Game/Vendors,
        /// stocked from the Showcase's My vendors page). Stock left in booths while it is off is mailed back to the owners
        /// at start-up. Docker: VENDOR_SHOPS.
        /// </summary>
        public bool VendorShops { get; set; }

        /// <summary>
        /// The prop's name over it while its build-mode right-click menu is open, for the player who opened it only
        /// (Game/Props.MenuName.cs; user request 2026-10-18): its custom name, else its definition's name, sent with
        /// PlayerUpdate 0x7C UpdatePropName and put back when the menu closes (the next prop action, another right-click,
        /// leaving build mode, or 10 s, as the client reports no close). No source shows live doing it. Default on; off =
        /// no label. Docker: BUILD_MENU_PROP_NAME.
        /// </summary>
        public bool BuildMenuPropName { get; set; } = true;

        /// <summary>
        /// Storage chests one account may have placed across all its build sites; 0 (default) = no account cap. Live's
        /// rule is 50 per build site (update notes 21 Mar 2016, always enforced, Game/Chests.MaxPerSite); the wiki's "50
        /// per account" reading is not in any developer source, so it is an owner option (50 = the wiki's number).
        /// Docker: CHEST_ACCOUNT_CAP.
        /// </summary>
        public int ChestAccountCap { get; set; }

        /// <summary>
        /// Seconds a character must wait between two Teleport to Friend trips (Game/Abilities.Travel); 0 (default) = none,
        /// as live from 5 May 2015 (update notes: no cooldown any more for teleporting to a friend from the contact
        /// list);
        /// 1800 = the 2014 alpha's 30 minutes (2 May 2014 notes). Sent in 187's caster info (+0x70), so the client refuses a
        /// repeat itself with its text 562080, as Ui.TeleportToFriend does (OPEN-LEADS L182); the server refuses too.
        /// Dev "friendtpcooldown" changes it while running. Docker: FRIEND_TELEPORT_COOLDOWN_SECONDS.
        /// </summary>
        public int FriendTeleportCooldownSeconds { get; set; }

        /// <summary>
        /// Seconds a character must wait between two trips with Travel to Spire (ability 185) and, separately, Travel to My
        /// Site (186) (Game/Abilities.TravelCooldownSeconds); 0 (default) = none, as live 2016: the Stone of Recall these
        /// replaced (21 Mar 2016 notes) lost its cooldown on 5 May 2015, and no later source names one (OPEN-LEADS L188).
        /// 1800 / 3600 = the 2014 alpha's 30 minutes / one hour. Sent in both casts' caster info (+0x70), so the client
        /// refuses a repeat itself; the server refuses too. Docker: TRAVEL_COOLDOWN_SECONDS.
        /// </summary>
        public int TravelCooldownSeconds { get; set; }

        /// <summary>
        /// An accepted friend request makes the friendship mutual (Game/Friends.HandleResponse, rules in
        /// Internal/Social/FriendAccept.cs): the asker's list gets the one who accepted and theirs gets the asker, both
        /// told at once with FriendAdd. On (default) = live (wiki "Friends List": people "you either sent friend requests
        /// to or from whom you accepted friend requests"); off = one-way lists, only the asker's list changes. Read at each
        /// answer. With SOCIAL_ADDRESS set the social service's own FriendRequestsMutual counts (the same .env line).
        /// Docker: FRIEND_REQUESTS_MUTUAL.
        /// </summary>
        public bool FriendRequestsMutual { get; set; } = true;

        /// <summary>
        /// Story creatures keep the mood and stance a Story Node gave them (Game/StoryTools KeepPose): saved with their
        /// spawner, taken by every respawn and shown to players who arrive later. On (default) = live from 7 Jun 2016
        /// (update notes: creature moods and stances are kept between sessions and site visits); off = shown only to
        /// the
        /// players there when it happens. Docker: STORY_POSE_PERSISTS.
        /// </summary>
        public bool StoryPosePersists { get; set; } = true;

        /// <summary>
        /// Placing a prop from the bag (using a prop item) opens Build Mode on its Props page, as Tab would
        /// (Game/BuildModeHolster). Cancel before the first Save closes it again; a successful place leaves it open
        /// (Tab out when done — closing on first Save killed the placement gizmo). <b>On by default although live did
        /// not do it</b> (the 0.695 client never enters build mode for a placement and live's notes only say such items
        /// are still placed by right-clicking them in the inventory): the user asked for it (2026-10-18).
        /// Off = live (the placement runs outside build mode). Docker: PLACE_OPENS_BUILD_MODE.
        /// </summary>
        public bool PlaceOpensBuildMode { get; set; } = true;

        /// <summary>
        /// Emotes, stances, /playdead and the client's /sit put the held tool or weapon away (to the bag) and bring it
        /// back when the player walks off, or at /stand (Game/EmoteHolster). With anything in hand the 0.695 client plays
        /// no emote at all (its animation network has emotes only for empty hands). Off by default = live (the client never
        /// puts an item away for an emote; players put the tool away themselves). On: the client first plays the tool's
        /// put-away clip (StandPoses WeaponSwitchCombo, ~1.1 s in which no emote shows), and handing the tool back plays
        /// the take-out clip, which ends an emote still running (trace/0695/emotes-client.md; the user chose live
        /// 2026-10-21). Docker: EMOTES_PUT_AWAY_HELD_ITEM.
        /// </summary>
        public bool EmotesPutAwayHeldItem { get; set; }

        /// <summary>
        /// /yell is the yell emote (the Call clip, "You yell loudly.", Game/Emotes): registered with the zone's command list,
        /// which replaces the client's own /yell alias of /shout (one client command table). On (default) = live: the client's
        /// own emote word text "yell" (570042) and its four yell lines, and the 2016 wiki's emote table (OPEN-LEADS L192 (c));
        /// off = /yell stays the client's shout (/shout always is). Takes effect at the next zone-in (the command list).
        /// Docker: YELL_IS_EMOTE.
        /// </summary>
        public bool YellIsEmote { get; set; } = true;

        /// <summary>
        /// Speed of the world's day/night clock in game seconds per real second (Game/WorldClock). 8 = live: one day
        /// and night take 3 real hours (wiki Astronomy), the client's own default too.
        /// 0 or less = 8. The clock is anchored to real UTC time × this speed, so it carries on across restarts.
        /// Docker: DAY_CYCLE_SPEED.
        /// </summary>
        public float DayCycleSpeed { get; set; } = 8f;

        /// <summary>
        /// The daily Showcase following mail: once a day an in-game mail to every player who follows creators or tags,
        /// listing what those creators listed and what was listed with those tags on the Marketplace (Player Studio designs)
        /// and the Gallery (build sites) since the last one; nothing when there is nothing new. On = live: once a day,
        /// a mail listing what the followed creators and tags had newly on the Marketplace or Gallery (Update Notes 27
        /// Aug 2014). Off = no digest mail
        /// (following itself stays). In-process commerce (Game/ShowcaseFollow); with <see cref="CommerceAddress"/> set the
        /// commerce service runs the digest and reads its own CommerceServer.json setting of the same name (the same Docker
        /// variable). Docker: SHOWCASE_FOLLOW_DIGEST.
        /// </summary>
        public bool ShowcaseFollowDigest { get; set; } = true;

        /// <summary>
        /// The UTC hour (0-23) the daily following mail goes out (default 10 = 2-3 a.m. Pacific, off-peak for live's US
        /// servers; live's own hour is not known [I]); a world that was down at that hour sends it when it is back the same day.
        /// With <see cref="CommerceAddress"/> set the commerce service's own setting counts. Docker: SHOWCASE_DIGEST_HOUR_UTC.
        /// </summary>
        public uint ShowcaseDigestHourUtc { get; set; } = 10;

        /// <summary>
        /// A player the Game Finder takes to a game on another world (OPEN-LEADS L31) is brought back to the world, landscape
        /// and spot they left from a few seconds after they leave that game. Off = they stay on the game's world (the
        /// default: no source says live brought them back, and a match on their own world never moves them back either
        /// [I]); they travel home with the Landscapes window. Read on the searcher's world. Docker: FINDER_RETURN_HOME.
        /// </summary>
        public bool FinderReturnHome { get; set; }

        /// <summary>
        /// Lost Crystal trips and Travel → An Underground Adventure may land in a Chaos Cavern on another world of the same
        /// multi-world gateway (OPEN-LEADS L131): on = live, each Lost Crystal use may go to another world and biome (7
        /// Jun 2016, P5734; a world is a server in the game's words), with the button rolling the
        /// same way since 30 Jun 2016 (P5927) [I]. Each trip treats this world and every other reachable world as equally
        /// likely; the world it lands on picks the cavern. Off = trips stay on this world's landscapes. No effect with one
        /// world per gateway. Read on the world the trip starts from. Docker: CAVERN_TRIPS_OTHER_WORLDS.
        /// </summary>
        public bool CavernTripsOtherWorlds { get; set; } = true;

        /// <summary>
        /// Lumens a member account gets every 30 days while a member. Default 0 = live: Landmark had no member perks
        /// (wiki Daybreak Cash: none of the usual All Access benefits; agent-membership.md §2, §7) (user
        /// 2026-10-18). An extra a server owner can switch on, e.g. 650 = All Access's monthly 500 Daybreak Cash in
        /// other Daybreak games × 1.3 Lumens per Daybreak Cash [I] (the stipend the user designed on 2026-10-17). Trials
        /// get none. Docker: MEMBER_MONTHLY_LUMENS.
        /// </summary>
        public uint MemberMonthlyLumens { get; set; } = 0;

        /// <summary>
        /// Percent off Lumen store prices for members and trials. Default 0 = live (no Landmark member perks, as
        /// <see cref="MemberMonthlyLumens"/>; the client's store data has a MemberSalePrice field but live's member prices
        /// are not known). An owner can set e.g. 10. Docker: MEMBER_STORE_DISCOUNT.
        /// </summary>
        public uint MemberStoreDiscountPercent { get; set; } = 0;

        /// <summary>
        /// Hours a store purchase waits before it can be traded, mailed or put in a chest (trace/0695/audit-wiki-gaps.md
        /// #7). Live had this "fraud timer" [P]: TransferItemResponse 0x12 "Item can't be traded if it has fraud timer on
        /// it", texts 577061 "That item was recently purchased and may not be traded yet. Please wait and try again
        /// later.", 562759 "Recent purchases can't be placed into chests right away." and the Fantasy Parlor Pack's 577356
        /// "there is a waiting period before purchased items become tradable". The length is not in the client; the
        /// developer post "The Business of Landmark" (Smokejumper, 26 Mar 2014, design/forum/all/2014-03-26-26036) says
        /// Marketplace items start as no-trade for at most 12 hours after purchase [P dev], so 12 by default (live's
        /// maximum; the "at most" suggests a variable fraud check, and that 2016 kept the 12 hours is [I]; the
        /// wiki's Trading page says 12-24 [W]). 0 = off (store items are tradeable at once). Applies to items bought for
        /// Lumens (a free store item has no wait). The item's tooltip counts it down: "Tradable in %TIME" (562375) from the
        /// tooltip reply's FraudTimer (InventoryTooltipReply, trace/0695/agent-leadsC.md L184). Docker:
        /// STORE_TRADE_WAIT_HOURS.
        /// </summary>
        public uint StoreTradeWaitHours { get; set; } = 12;

        /// <summary>
        /// Hours an item waits in overflow (the inventory's third tab, items that didn't fit the bag) before it is deleted
        /// (trace/0695/audit-wiki-gaps.md #8). Default 24 = live: wiki Inventory, overflow items are lost after 24
        /// hours [W]; the client shows the countdown ("Overflow: Items expire in H:MM:SS", from
        /// the item record's timed-delete seconds) [P]. 0 = overflow items are kept until retrieved. Docker:
        /// OVERFLOW_EXPIRY_HOURS.
        /// </summary>
        public uint OverflowExpiryHours { get; set; } = 24;

        /// <summary>
        /// Most of one item a mail attachment may carry (trace/0695/audit-wiki-gaps.md #16). Default 9000 = live: wiki
        /// Trading, at most 9,000 of a gathered material (e.g. Copper Ore) per mail [W]; the
        /// client has no cap of its own (it sends the dragged stack's count) [P]. A bigger stack is refused with mail result
        /// 5 "attachment is invalid". 0 = no cap. Coins are not capped. Docker: MAIL_MAX_STACK.
        /// </summary>
        public uint MailMaxStack { get; set; } = 9000;

        /// <summary>
        /// May players hand each other Lumens (docs/live-rules.md)? The client's coin panes are Lumens (item 1579): the trade
        /// window's coin offer (Trade CoinCount, sub 9) and the mail compose window's
        /// send money / cash on delivery (attachment types 1 with guid 0 / 5). Off (default) = live: Lumens "cannot be
        /// traded" (developer, Landmark Wipe Q&amp;A, 14 Mar 2016, thread 56195; players still asked for Lumen trading on
        /// 29 Aug 2016, thread 57902). A coin offer is answered "your offer is invalid" and stays 0; a mail carrying coins
        /// or asking for them on delivery is refused with mail result 0xA (no-trade item). On = Lumens move by trade and
        /// mail. Docker: LUMEN_TRADING.
        /// </summary>
        public bool LumenTrading { get; set; }

        /// <summary>
        /// Player Studio designs take no design storage (Game/Templates.cs StudioDesignsFree; docs/live-rules.md). On
        /// (default) = live: designs bought from the Marketplace (Player Studio) don't count toward the limit (wiki Designs
        /// and Build Mode) and a design submitted to Player Studio no longer counts against Voxel Storage (developer, Hotfix
        /// Notes 17 Oct 2014); the Designs window's "free" column shows it, and a full storage doesn't stop a purchase.
        /// Off = they count like any other design, and a purchase needs room. Docker: PLAYER_STUDIO_DESIGNS_FREE_STORAGE.
        /// </summary>
        public bool PlayerStudioDesignsFreeStorage { get; set; } = true;

        /// <summary>
        /// Designs follow the account (Game/Templates.cs DesignsFollowAccount; docs/live-rules.md). On (default) = live:
        /// the Designs window's designs were "tied to your account, not your character" (developer, Landmark Wipe Q&amp;A,
        /// 14 Mar 2016): at world entry the designs held by deleted characters of the same account (their own and the
        /// designs made of their build sites) move to the character, keeping it as their author. Off = a deleted
        /// character's designs stay with it (unreachable). Docker: DESIGNS_FOLLOW_ACCOUNT.
        /// </summary>
        public bool DesignsFollowAccount { get; set; } = true;

        /// <summary>
        /// Keep each item's auto-resupply switch (Inventory 0x2D sub 0x0D InventoryPacketToggleAutoResupply, the item
        /// record's byte at ClientItem +0xdc; Game/Inventory/Inventory.cs SetAutoResupply). Default on = live: the live
        /// item table had an <c>auto_resupply</c> column (its column names are in the 0.695 client) [P]. Off = the toggle
        /// is ignored and every item is sent with false (the server before 2026-10-18). The 0.695 client only sends the
        /// toggle for definitions that pass its own check (FUN_142f1d360), which none of the 2,885 live definitions does,
        /// so in play this is reached through the dev command <c>autoresupply</c>. Docker: ITEM_AUTO_RESUPPLY.
        /// </summary>
        public bool ItemAutoResupply { get; set; } = true;

        /// <summary>
        /// Grief awards (Game/Grief.cs, OPEN-LEADS L144). The client's grief warnings say what earned grief: "You have
        /// been firing on ally forces" (texts 56075-56078, CodeStringMappings UI.GriefLevelOne-Four) [P]. On = in team site
        /// games a weapon hit aimed at a teammate lands (friendly fire) and earns the attacker grief, which rises through
        /// the warning levels to equipment lock (4) and suspension (5) and decays with time. Default on = live: the client
        /// ships the warnings for firing on ally forces (texts 56075-56079) and team-kill rule sets (Traitors!), so live
        /// awarded grief (user 2026-10-18: live behaviour by default, togglable). Off = teammates can't hurt each other and
        /// nothing earns grief; the admin / dev grief commands still work. Live's own amounts and decay are not known
        /// (L144), so the numbers below are ours [I]. Docker: GRIEF_AWARDS.
        /// </summary>
        public bool GriefAwards { get; set; } = true;

        /// <summary>Damage dealt to allies per grief point [I] (the client keeps the remainder as its "damage points"). Docker: GRIEF_DAMAGE_PER_POINT.</summary>
        public uint GriefDamagePerPoint { get; set; } = 100;

        /// <summary>
        /// Grief points at which levels 1-5 begin, comma separated [I] (1-3 warnings, 4 equipment lock, 5 suspension:
        /// the client's level texts, FUN_142ee1850). Docker: GRIEF_LEVEL_POINTS.
        /// </summary>
        public string GriefLevelPoints { get; set; } = "10,20,30,40,60";

        /// <summary>Grief points that wear off per hour, online or not [I]. 0 = grief never decays. Docker: GRIEF_DECAY_PER_HOUR.</summary>
        public uint GriefDecayPerHour { get; set; } = 30;

        /// <summary>How long a grief suspension (level 5) keeps the character out of the world [I]. Docker: GRIEF_SUSPENSION_MINUTES.</summary>
        public uint GriefSuspensionMinutes { get; set; } = 60;
    }
}
