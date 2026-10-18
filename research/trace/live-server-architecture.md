# How the live Landmark servers were built (what can be found; updated 2026-10-16: 0.695 + 2016 patch notes section at the end)

Daybreak never published Landmark's server architecture. No talk, slides, job ad or developer post that describes it
was found. What is known comes from the 0.589 client, which was built against the live servers (re/out/rtti.tsv,
re/decomp_all), plus a few interviews.

A player's recollection, as passed to the user: "Edge gateway that did routing to internal microservices. Terrain was
handled by multiple servers (how? CellId?). Not sure how the rest of the zone work was done."

## From the client (strong evidence)

- **Gateway: a front door with an internal side.**
  - Classes `Gateway::ExternalGatewayApi` and `Gateway::ExternalPackets::*`: LoginRequest/Reply, Logout, ForcedLogout,
    ChannelIsRoutable, TunnelPacketFromExternalConnection and TunnelPacketToExternalConnection.
  - The client also contains one internal packet: `Gateway::InternalPackets::PacketTunnelPacketToExternalConnectionList`
    (part_0144.c:27797). That is a back-end server asking the gateway to deliver one packet to a list of player
    connections, i.e. back-end services behind the gateway fanning messages out to players.
  - `List<Gateway::ExternalDestination>` and the string "GatewayId" fit this too.
- **Traffic is routed per channel.**
  - Every tunnelled packet carries a 3-bit channel (0-3; the client never names them — emulator labels 0 Zone, 1
    World, 2 PositionUpdate, 3 "Voxel", the last unproven; see architecture/channels.md for what goes on each).
  - The gateway can tell the client that a channel is or isn't routable (`OnChannelIsRoutable channel=%d isRoutable=%d`,
    FUN_142d582a0; `OnConnectionIsNotRoutable`, FUN_142d58300). That only makes sense if each channel leads to a
    separate back end that can be up or down on its own.
- **Movement could come from the gateway itself.** There are two position packets:
  - `PlayerUpdatePacketUpdatePositionZoneToClient`, from the zone server;
  - `PlayerUpdatePacketUpdatePositionGatewayToClient`, handled in ClientGateway (FUN_142d58570).

  So position updates could come straight from the gateway, without going through the zone server.
- **Separate services named in the client:**
  - A build-site (claim) server: `ClaimsPacketLostClaimServerConnection` (0xF9/0x12) and the text "Connection to the
    Build SiteServer is unavailable" (562155; "Claim" was renamed "Build Site" everywhere).
  - Mail: "Mail service is unavailable" (562281).
  - Login server ("Unable to connect to Login Server", Login.Servers, Login.Queue).
  - Commerce sessions (PacketCommerceSessionRequest) and lobby packets (inherited from PlanetSide 2's ForgeLight).
  - The video upload service ("(503)").
- **Terrain is fetched per cell, by cell id** (ClientCellRequest, `u64 cell = level<<60 | x<<36 | y<<18 | z`,
  LandmarkEmu/docs/terrain.md). Correction 2026-10-15: the request goes on **channel 0 (Zone)**, not a voxel channel
  (FUN_1430acba0; architecture/channels.md). The zone server answered cells; whether it asked a separate voxel
  database (P0026) is server-internal and not visible to the client.

## From interviews and the wiki (weaker)

- Dave Georgeson, GDC 2014 ([GamersNexus](https://gamersnexus.net/gg/1362-gdc-eq-next-landmark-interview)):
  - the biggest optimisation problems were "data distribution and I/O";
  - the server is hit each time a player loads a new cell;
  - the server tells the client when something new is placed, and the client then requests it.
- Voxel Farm (Miguel Cepero) supplied the voxel technology. His blog describes server-side ("farm") generation and,
  later, a distributed "Farm Cloud" for storing user edits ([procworld](http://procworld.blogspot.com/2016/09/introducing-farm-cloud.html)).
  That is his own product, not a description of Daybreak's servers.
- Worlds: identical landscapes on every world, free travel between them, builds differ
  ([wiki: Servers and Landscapes](https://landmark.fandom.com/wiki/Servers_and_Landscapes)).
- EverQuest Next NPC pathing needed 6 GB of pathing data per island per world (secondhand).

## The client's admin monitor screens (found 2026-10-15)

The client has developer / admin "monitor" states that draw statistics the live servers sent about themselves
(classes from re/out/rtti.tsv, texts from re/decomp_all):

- **Environment Server** (`EnvironmentServerMonitorState`, request `AdminCommandPacketEnvServerDataMonitorUpdateRequest`,
  data `ClientEnvironmentServerMetrics::LayerInfo`): "========= Environment Server Information =========", a
  **Job Summary** (per job type: Active, Pending) and **Ruin Layer Info** (per layer: Total Jobs, Pending, Failed,
  Last ID). A separate server ran queued background jobs per world layer, including placing the ruins / outposts.
- **Pathing** (`PathingMonitorState`, request `AdminCommandPacketPathingDataMonitorUpdateRequest`): "Pathing Information
  - Zone": NavMeshCells, NavMeshJobsRemaining / CompletedThisFrame / CompletedLifetime, PendingStitchSections,
  DynamicSilhouettes, AvoidanceObstacles, Characters, and a Performance Summary with **"Zone avg fps / frame ms"** and
  **"World Step avg fps / frame ms"**. The zone server ran a fixed-rate simulation step and built creature
  navigation meshes from the voxel terrain as jobs (stitching sections together, obstacles from props and creatures).
  This is the "6 GB of pathing data per island per world" of the EQ Next era.
- **Procedural data** (`ProceduralDataMonitorState`, `ProceduralDataMonitorPacketUpdateRequest / Response`): "Zone Node
  Count", "Zone Spawn Count (% of nodes) F / N / P", per BiomeId and LayerId: Spawns, Available, Unused, Above Min /
  Below Min / Over Max, a **Layer Queue**. The zone server spawned the world's procedural entities (resource nodes,
  harvestables) from a pool of placement nodes per biome and layer, keeping each between a minimum and maximum.
- **Instanced games** (`InstancedGameMonitorState`, `InstancedGameAdminMonitorPacket` key/value list): "Instanced Game
  Information - Server" next to "- Client".
- **Environment templates** (`TemplatePacketEnvironmentListResponse`, `...InstanceDetailsResponse`,
  `...AddOwnershipResponse`, `...DeleteTemplateResponse`): designs placed into the world by the developers (outposts,
  hub buildings) were managed as "environment" template instances with ownership.
- **Business environments** (`BusinessEnvironmentManager`, `PacketBusinessEnvironments`): named deployment environments
  sent at login (live / test / internal).
- **Connection flow strings:** login over UDP (the retail client always builds `LoginUdp_10`, FUN_1430b3a70 → FUN_143a58420; the `ExternalLoginTcpApi` class is unused — trace/0695/architecture-leads.md L6), "Login Queue Info: Position=%d,
  EstWaitTime=%0.2f", "Login Server is locked at this time."; the login server hands the client a gateway: "Gateway
  Connect Info, ConnectTime = %d, Guid = %llu, Address = %s, Ticket = %s"; the UDP gateway accepts only expected
  connections (`UdpParams.processExpectedConnectionsOnly`) and gives "One-Time Session Id"s for re-login
  ("Lost connection to the gateway while waiting for a relogin session"); the gateway's profiler counts
  "totalRecipients ... gatewayHeaderBytes ... applicationBytes" per message signature (one message fanned out to
  many players); "Received %d template image-pairs from ZoneServer"; "ASSET_SERVER_05" (remote asset delivery);
  lobby / matchmaking (Lobby:JoinQueueWithID, OnMatchmakingStart / Launch / Cancel); commerce server status
  (PacketInGamePurchase ServerStatusRequest).

Public sources add little: ForgeLight (shared with PlanetSide 2 and H1Z1) was described only at marketing level
("2,000 people on one server at 60fps", Shacknews, GDC 2013); no Daybreak talk on the back end was found (searched
2026-10-15).

## Which back end each message went to (the client's channel per message)

Every client message is tagged with one of the gateway's four channels; the gateway sends each channel to its own
back end. The client's 593 send templates (built around Gateway::ExternalPackets::PacketTunnelPacketFromExternalConnection,
e.g. FUN_142ba7ac0, channel = 3rd argument, byte = channel << 5 | 6) are called with channel 0 (Zone) 1,145 times,
1 (World) 370 times, 3 (Voxel) 26 times, 2 (PositionUpdate) twice. Which messages use which, measured from the dev
world's console.log (the gateway's trace line "Received Gateway packet ... | Channel: X", 2026-10-15, ~8,000
messages):

- **World channel:** InGamePurchaseBase (Marketplace / commerce), RatingBase (build-site ratings), SetLocale,
  FotomatBase (portraits). These went to a world-level service, not the zone server.
- **Zone channel:** everything else seen, including **ChatBase** (chat went to the zone server first), inventory,
  claims, templates, voxel construction, UI, commands, client metrics and logs.
- **Channel 3** (not "Voxel"): build-cursor / paste previews only (all eight WorldEditing 0x10C messages); terrain cell requests go on channel 0 (corrected 2026-10-15, channels.md, terrain-service.md).
- **PositionUpdate:** movement.
- Not yet observed: friends, guild and mail messages (not used during the logged sessions). Check the next log.

What this says about the "social service": chat entered through the zone server, yet General chat reached every world
and "custom chat channels" survived a change of world (fixed 16 Jun 2016), and mail had its own "Mail Service is
currently unavailable" error. So the zone servers handed social traffic to shared services behind them, while
account-level things (store, ratings, portraits) had a world-level service the client addressed directly.

## From the developers' own posts (patch-note threads, 2014-2016)

Found by searching every staff post in trace/0695/patchnotes.tsv (forum + wiki update pages) for server terms. Ids
are patchnotes.tsv items (re-checked against the current file 2026-10-16: several ids written on 10-15 pointed at
other notes after the file was regenerated; corrected below).

- **A separate voxel database.** "To prevent our voxel database from crashing under load, we are converting it to
  64-bit processes" (P0026, 3 Feb 2014). Terrain edits lived in their own database service, apart from the game
  database, and it ran as several processes.
- **Zone servers, which could crash on their own.** "Fixed an issue that could result in claims being lost if they
  were created shortly before a zone crashed" (P0042, Feb 2014); "Fixed numerous client and zone crashes" (P2558,
  Dec 2014); "Addressed some issues that could result in a zone crash" (P5984, 30 Jun 2016). A zone (a landscape on
  a world) ran in a process of its own, and its writes reached the database with a delay (a claim made just before a
  crash could be lost).
- **Worlds are hardware.** "Spin up more worlds ... We are acquiring hardware for more right now" (P0010, Feb 2014,
  when claims filled the first worlds). Each world was its own set of servers.
- **Rollbacks on restart** were fixed in Feb 2014 (P0133): state was held in the servers and saved on a schedule.
- **Regular restarts.** A nightly 5 a.m. "server bounce" (2015: P2850, P3058) used to ship small fixes; in Jun 2015
  restarts went to three early mornings a week because server-side respawns needed them (P3643, with P3639); by 2016 a
  "regularly scheduled overnight bounce on Saturday morning" (P6448, P6454). Some fixes "require a server restart"
  (prop disassembly, P6448, Aug 2016), others are client-only or hot data pushes "that did not require server
  downtime" (P5680, P6221: a design placement bug fixed by a "stealth ninja hotfix"). A dev reply on 2 Aug 2016
  (P6223): a client fix "won't work until we can swap the servers too, since they apply the actual data" — the
  servers, not the client, apply design / paste data to the world.
- **Restart schedule at launch:** "Starting June 25th, we are going to schedule a brief server restart every Monday and
  Saturday morning at 3am Pacific time ... Expected down time should be under an hour" ("Server restarts Saturdays &
  Mondays", P5902, 24 Jun 2016); "we are going to resume doing occasional overnight bounces of the servers to improve
  performance" (P5906, Week End Wrap Up, 24 Jun 2016).
- **An "Events" world for seasonal islands:** "You can access the Halloween Party Island via the teleport menu, on a new
  world called 'Events'" (P6529, 3 Oct 2016); "As of this morning's bounce the Halloween Party Island is no longer open
  on the Events world, and instead the Winter Holiday Island is up" (P6560, 21 Nov 2016). Seasonal content was a world of its own,
  switched at a restart.
- **Developer-placed builds per island:** the "Landmarks of Landmark" contest winners were placed beside each spire,
  world by world, island by island (Jun - Aug 2016), the same way outposts were placed in the caverns.
- **Separate world groups.** "We'll bounce the Foundation Museum worlds shortly" (P3966, Dec 2015): the visit-only
  museum worlds and seasonal islands (the winter holiday party island) were restarted on their own.
- **Operations and a database team.** "Once our database team is back in the office, we'll check who bought the
  chest" (P3703, 2015); "working with our operations team to improve alerts and reporting" after a login outage
  (P6554, 14 Oct 2016). Login was its own service whose failure stopped everyone getting in while worlds stayed up.
- **Cross-world services.** "Game finder should now work between worlds" (P6551, 14 Oct 2016): matchmaking was a service
  shared by all worlds; General chat, friends, guilds and mail already were (wiki: Chat "General: worldwide chat that
  includes all Servers and Landscapes").
- **Terrain destruction is a layer, healed by queued requests.** A dev (27 Jul 2016) on Linking Area destruction:
  "The world is made of layers of voxel data. On the bottom level is the world itself. On top of that, is your data.
  Destruction is applied on top of that to get the final product when we build the mesh. So when you destroy a volume
  ... you're applying a volume of negative space at the topmost layer" (P6204); "it puts in a pending 'heal' request
  with the desired time", and a new destroy doesn't cancel an earlier pending heal (P6205).

## What the server pieces were (best reconstruction)

"Emulator today" updated 2026-10-16; the full per-service status and the build order are in
[architecture/recreation-design.md](architecture/recreation-design.md).

| Piece | Evidence | Emulator today |
|---|---|---|
| Login server (accounts, sessions, server list) | client Login.* texts, ExternalLogin, the Oct 2016 login outage (P6543, P6554) | AuthServer (one) ✅; internal port 20210 answers the gateway's TicketConsume / RelogSession |
| Gateway (the client's only connection; routes 4 channels) | Gateway::ExternalGatewayApi, ChannelIsRoutable, internal tunnel packets | LandmarkEmulator.GatewayServer, own container, opt-in (docs/docker.md); the dev world still runs it in-process |
| World server (world-level back end: channel 1, kicks / broadcasts, player transfers between servers) | channel 1 classes (channels.md); server-only ids WorldKick*, BroadcastWorld, WorldToZoneForPlayer, WorldInitiatedPlayerTransferServer (§ 0.695 below) | LandmarkEmulator.WorldChannel (channel-1 back end: sinks, logout session, trade broker, encounters), own container, opt-in |
| Zone servers (one per landscape per world) | "zone crash" notes, zone ids in every packet family, ZoneToZonePacket / PlayerTransferServer* ids | one process runs every landscape of a world |
| Environment Server = voxel database + job service (layers base / player / destruction with heal queue, construction and vein jobs, ruin layers; FUN_143c74ef0 job types, terrain-service.md) | P0026, the 27 Jul 2016 layer explanation; cell requests reach the zone server (channel 0) | library LandmarkEmulator.EnvServer.Core inside the world process (persisted heal queue); no container yet |
| Claims (build site) service | ClaimsPacketLostClaimServerConnection, "Connection to the Build Site Server is unavailable" | in the world process |
| Mail service | "Mail service is unavailable" | social service (opt-in), else the world process |
| Shared social services (General chat, friends, guilds, mail, Game Finder) | wiki Chat, P4905-P4907, P5841, P6551 | LandmarkEmulator.SocialServer, own container, opt-in (SOCIAL_ADDRESS); else per world |
| Game database (characters, items) + database team | P3703 | MariaDB (auth, character, one per world, lmemu_social) ✅ |
| Commerce / Marketplace / Player Studio (Daybreak platform) | PacketCommerceSession*, the Showcase pages | interface split (ICommerceService); still WebPages / Commerce in the world process |

## Recreation plan

Superseded 2026-10-16 by [architecture/recreation-design.md](architecture/recreation-design.md) (live split vs what
runs today, per channel and per service, and the recommended build order). Kept for the record:

**Deployment (decided direction, 2026-10-15):** every service in its own Docker container on one machine first,
talking only over the network (no shared files or memory between services), so any container can later move to
another machine by changing an address. See the table below for the services.


The client only ever talks to its gateway, so the split is invisible to it; recreate it where it pays off:

1. **Shared services first** (what players notice): one social service for General chat, friends, guilds, mail,
   Player Studio and the Game Finder, used by every world (TASKS 7).
2. **Gateway as its own process** per world (LandmarkEmulator.Gateway exists): routes Zone / World / Position / Voxel
   channels to back ends; lets a zone restart without dropping the player's connection.
3. **Terrain service**: the layered voxel store (base, player layer, destruction layer with timed heals) behind the
   zone (channel 0) as the Environment Server, shared by the zones of a world.
4. **Zone processes** per landscape only if one process can't hold a world's players.
5. **Operations**: scheduled restarts with warnings (the server already has a shutdown countdown), health checks,
   and save-on-schedule so a crash loses at most a few seconds.

## For the emulator

The emulator handles every channel in one world process. The client is fine with that because it only talks to the
gateway. If the server ever needs to scale, the client's own design shows where the seams are: per channel (Voxel /
PositionUpdate / Zone), with a claim service and a mail service on their own. (2026-10-16: the gateway, world-channel
and social services now exist as opt-in containers; the dev world still runs as one process — recreation-design.md.)

## Additions from the architecture leads (2026-10-15, trace/0695/architecture-leads.md)

Folded in from agent #8's L3-L8 trace (evidence and addresses in that file). Item 1's TCP correction is applied above.

1. **Login:** replace "login over TCP (ExternalLoginTcpApi)" in "Connection flow strings" with: the retail client
   always uses UDP (FUN_1430b3a70 → FUN_143a58420 `LoginUdp_10`); the TCP class is unused. Add: CharacterLoginRequest
   carries locale, locale id and a ClientConfig.ini `GatewayId` (FUN_1430b3cb0) — the login service can be asked for a
   specific gateway.
2. **Gateway:** add external opcode 9 = gateway-executed slash command (bare BasePacket + ClientCommandPacket body); the
   client accepts only gateway opcodes 2 / 4 / 5 / 7 / 8, so gateway answers are tunnelled packets; the gateway has its
   own command list (source 3) incl. `/gatewayprofiler`. Internal side: TunnelPacketToExternalConnectionList is internal
   opcode 0x16, header 11 bytes + 12 per destination (ApplicationPacketTracker FUN_143c78d10).
3. **Commands:** world and zone also add single commands with AddWorldCommand / AddZoneCommand (0x1E/0x45 / 0x46),
   executed by the client as CommandPacketExecuteCommand 0x1E/0x47 on World / Zone channel (update dynamic-commands.md).
4. **Server-to-server messages:** add a section listing the id-only messages of L8 (claims ↔ zone, mail ↔ zone, zone ↔
   voxel / Environment Server, world ↔ zone / gateway admin, stats) as the traced inventory of internal messages; the
   recreation's containers use these family / sub ids and names.
5. **Claims and mail as services:** strengthen the table rows "Claims (build site) service" and "Mail service" with
   ZoneAddClaim / ZoneAddAllClaims / ClaimMoveItemToPlayerOnZone and ConsumeAttachmentZoneRequest / Response.
6. **Environment Server:** add that it answers cell ownership (VoxelConstructionPacketEnvOwnership* 0xF4/0x16-0x17,
   client-handled) and exported-template spawns (0xF4/0x15, 0x18, 0x19), besides ruin-layer jobs.
7. **Caves:** add "Caves were generated offline (ProceduralPlacementTool, ProcessSurfaceCaves / ProcessCaveNodes) into
   per-continent `.cpa`; client and server load the same files; CaveManager (server code in the client) picks cave
   beginning locations for the underground teleports". Drop any idea of a cave-generating service.
8. **Admin monitor:** state that the retail client cannot open the paged monitor (L3) and has no reader for pages 11,
   12, 24, 30, 32, 33 (L4); the "client's admin panel working" goal (BUILD-PATH §1) is reachable only with a test hook
   (Frida) and only for pages 28 / 29. Decision needed from the user before spending Phase B item 7 effort.
9. **Channel table:** "Commerce template products are created through the zone (PacketInGamePurchaseCommerceTemplateProductCreate,
   channel 0)" and the L7 resolutions (World: SetLocale, PlaceOrder / PreviewOrder / PlaceOrderClientTicket /
   StoreClientStatistics, guild rank packets).

## What the 0.695 client and the 2016 patch notes add (2026-10-16)

Marks: **[P]** proven — client code (addresses **0.695 / 0.589**), a client text, or a developer post quoted in
trace/0695/patchnotes.tsv (P-ids) / design/forum; **[W]** wiki or player post (weaker); **[I]** inference from those.
Existing traces were used as they are (named per line); new client work in this pass: the zone-base id registry (§3).

### 1. What did not change from 0.589 to 0.695

- No client message changed channel; the 13 new 0.695 messages all go on channel 0 (channels.md "0.695",
  channels-695.tsv) [P]. Gateway external packets and Movement_ReadBody are identical in both builds
  (gateway-service.md §1.3) [P]. The server-only id registry has the same names in both (0.695 FUN_1434ba530 /
  0.589 FUN_14340c950, §3) [P].
- [I] So the 2016 server split is the one the 0.589 traces describe; 0.695 adds features on channel 0, not new
  back ends.

### 2. Worlds: the list, splits and the Events world

- **World names in the client**: texts 560911-560926 — Courage, Liberation, Satisfaction (EU), Understanding (EU),
  Rebellion, Serenity, Determination, Confidence, Adventure, Gratitude, Prosperity, Success, Triumph, Opportunity,
  Inspiration (560924 "Victory Node" in the same block is not a world) (re/out/text_695.tsv) [P]. The world list shows
  names by text id only (design/world-name-texts.txt) [P].
- **History** [P unless marked]: Feb 2014 "Spin up more worlds ... acquiring hardware for more", first new world
  Serenity (P0010); Jul 2014 a "Foundation Museums" world for contest winners (P1455); May 2015 wipe: "multiple worlds
  (servers) as before ... The names of the worlds have not changed (e.g. Adventure, Courage, Understanding)" (P3478);
  Jun 2015 a player asks about "removing the plethora of servers" (forum 55218) [W]; closed-beta wiki list Courage,
  Foundations Museum (visit-only), Liberation, Satisfaction (EU), Serenity (wiki Servers and Landscapes) [W]; Aug 2016
  the developers' Landmarks of Landmark list names five worlds: Courage, Determination, Liberation, Rebellion, Serenity
  (forum 57799, trace/0695/landmarks-of-landmark-695.md); Oct 2016 a new world "Events" (P6529), its island switched at
  a bounce (P6560).
- [I] The world count fell from the 2014 peak (15 names) to five at launch, plus Events in Oct 2016. **How** worlds
  were merged or closed (characters, claims moved?) — no developer note found yet: **L115**.
- Worlds held identical landscapes with different builds (wiki Servers and Landscapes) [W]; claim limits were per
  island per world (500 per island, removed at a restart, P3578) [P].
- **A world in the client** is a world id + name text, carried by SendZoneDetails (Unknown8 / Unknown9) and
  ClientBeginZoning (+0xa8 → the client's current world +0x378cc); a different world is handled like a different zone
  (0.589 dispatcher part_0065 ~44895; trace/0695/event-islands-695.md §6) [P].
- **Teleport window** Locations (0xFC/3, reader FUN_14316f290 / FUN_1430c4150): n worlds × {world id, name text,
  capacity %, zones × {zone id, name, biome, total claims, capacity, tier, image}}; TeleportRequest {u32 world, u32
  zone} (writer FUN_1430c8600, 0.589) (layout in LandmarkEmulator.WorldServer Game/Landscapes.cs) [P]. [I] The server
  a player was on could list every world with its fill level: a world directory shared by all worlds.
- **Full destinations**: the teleport queue (ToQueuedLocation / CancelQueue / PostQueueNotify, channel 1) opens its
  dialog only when the server reports a queue (Dialog.lua; trace/systems/world-travel.md) [P]; that it queued entry to
  a full world is [I] (L60).
- **Events world**: in the emulator a second world entry {3, 567016} served by the same world process
  (Game/Landscapes.cs, event-islands-695.md); live's id / text: L109. [I] Live restarted world groups on their own
  ("bounce the Foundation Museum worlds", P3966; Events island switched "as of this morning's bounce", P6560).

### 3. Moving between worlds and between servers

- Players changed world from the Spire / teleport menu ("The portal spire crystal now takes you directly to the worlds
  list", P4400; arrival at the hub of the same island, P0514) and Lost Crystals sent players "to other worlds and
  biomes" (P5734) [P].
- The client connects to a gateway in one place only (run state 0xA, FUN_142d53aa0 / FUN_142cba700) and never
  reconnects on ClientBeginZoning (gateway-service.md Summary) [P]. The login protocol has CharacterTransferServerRequest
  / Reply (0x12 / 0x13, reader FUN_143a54ad0 / FUN_14399cbf0); what triggers them: not found yet (L114) [P names].
- **New (2026-10-16): server-only ids of the zone base registry.** 0.695 FUN_1434ba530 (part_0107 ~8313-8420, each
  through FUN_1434c7940) / 0.589 FUN_14340c950 (re/out/strings.tsv) name base opcodes that have no class, reader or
  other reference in the 0.695 decompile (checked name by name) [P names]: ZoneLoginRequest / Reply 0x04 / 0x05,
  ZoneLogout 0x06, ZoneIsReady 0x07, ZoneStats 0x08, TransferPlayerToServer 0x09, SavePlayer 0x0A,
  PlayerTransferServerRequest / Reply 0x0B / 0x0C, ProxiedPlayerBase 0x15, WorldInitiatedPlayerTransferServer 0x16,
  ZoneToWorldForPlayer 0x17, ZoneToZonePacket 0x18, ClientToTargetClientsZoneWrapped 0x19, WorldToClientWrapped 0x1B,
  WorldToZoneForPlayer 0x1C, LogoutPlayer 0x20, ShutdownZone 0x21, ZoneReportingClientIsReady 0x22,
  SavePlayerTrackedStats 0x28, ImmediatelyDeletePlayerFromZone 0x33, EscrowGivePackage / GotPackage 0x3F / 0x40,
  ZoneWorldMiniGameStatus 0x43, ZoneWorldNpcKnockout 0x44, WorldZoneRenamePlayerRequest / Reply 0x49 / 0x4A,
  WorldZoneSyncTime 0x4E, NpcTransferServerRequest / Reply 0x50 / 0x51, NpcTransferServer 0x52. Only
  TargetClientNotOnline 0x1A has a client class (RTTI 0x1446acef0).
  - [I] A **world server above the zones**: zones logged in to it (ZoneLogin*), it carried per-player traffic both ways
    (WorldToZoneForPlayer, ZoneToWorldForPlayer, WorldToClientWrapped), synced time, renamed players, shut zones down
    and could move a player to another server (WorldInitiatedPlayerTransferServer). Zones handed players and NPCs to
    each other (PlayerTransferServer*, TransferPlayerToServer, NpcTransferServer*) and passed player-to-player traffic
    across zones (ClientToTargetClientsZoneWrapped, answered TargetClientNotOnline). These are engine (ForgeLight)
    ids; which ones Landmark used is not visible in the client.
  - [I] Together with the single gateway connection: a change of landscape **or world** was a server-side transfer
    under the same gateway connection (the client cannot reconnect without going back to character select). So live's
    gateway was the fixed endpoint across zone servers, and across worlds if world travel used the same path. Whether
    one gateway served several worlds or handed a connection to another world's gateway: not found yet (**L114**).
    "Gateway per world" in BUILD-PATH / login-gateway.md is therefore our deployment choice, not a traced fact.
  - Names for our internal messages: architecture/internal-messages.md §1 (row added 2026-10-16).

### 4. Cross-world services

- **Social** [P]: friends / guildmates shown online across worlds without offline / online lines on a world change
  (P1679 2014, P4907 Apr 2016); guild roster location for a member on another world (P4905); a group member can be
  removed from another world (P4906: **groups spanned worlds**); custom chat channels kept across a world change
  (P5841); mail with its own service (text 562281, result codes; social-service.md §1). General chat "includes all
  Servers and Landscapes" (wiki Chat) [W].
- **Game Finder** [P]: "Game finder should now work between worlds" (P6551, 14 Oct 2016); the search goes on channel 0
  (InstancedGameMatchmakingRequest 0x10D/0x19, FUN_1434f2da0 / FUN_1434f2f90; world-channel-service.md §1). [I] The
  zone forwarded searches to one shared matchmaking component.
- **Commerce / Player Studio**: account-wide on the Daybreak platform (world-channel-service.md §5.1: CasApiHandler /
  UramApiHandler, Marketplace texts) [I with the sources there].
- **World directory** (teleport window capacity per world, §2) [I].

### 5. Zone crash, restart and reconnect as the client sees it

- **Patch notes** [P]: zones crashed on their own (P0042, P2558, P5984); after server resets or crashes teleport
  arrival points vanished from claims (P4329), creatures on build sites stopped pathing (P4248, fixed P4351), zones lost
  all mining nodes (P5421), the Goal tracker was cleared when the servers went down (P4562). [I] Zone state that was
  not saved, or not rebuilt at start, was lost with the process.
- **ChannelIsRoutable / ConnectionIsNotRoutable** are only logged to NetInfo.log (OnChannelIsRoutable FUN_142df6310 /
  FUN_142d582a0, OnConnectionIsNotRoutable FUN_142df6370 / FUN_142d58300): no message box, no state change; the
  client keeps sending on that channel [P]. What live's gateway did with those packets: L70.
- Every incoming channel except 2 goes to the same dispatcher (tunnel handler FUN_142df65e0 / FUN_142d58570) [P]: any
  back end may answer on any channel.
- Reliable packets sent before LoginReply are queued in the client and flushed on LoginReply(true) (FUN_143a4b710 /
  FUN_143993830) [P].
- A same-landscape ClientBeginZoning builds a fresh terrain engine and drops every entity except the player
  (FUN_142f83590 / FUN_142ee4930; docs/terrain.md "Long teleports") [P] — what lets a restarted zone rebuild a held
  client. Emulator: gateway hold + reattach built and tested 2026-10-15 on 0.695 (client NetInfo.log
  OnChannelIsRoutable 0 → 1, Begin Zoning, no disconnect; TASKS §8 B.2).
- **Relog session**: CharacterSelectSessionRequest 0xF6 on channel 1 (FUN_142d57e60 / FUN_142cbead0) with a 60 s
  deadline; "Lost connection to the gateway while waiting for a relogin session." (run state 0x15); 0xF7 → run state
  0x16 / 0x17; "Did not receive One-Time Session Id from server." (FUN_142d54a00 / FUN_142cbb660) [P]
  (gateway-service.md §1.2, world-channel-service.md §6.7).
- Shutdown and login: WorldShutdownNotice 0x5F countdown, ClientServerShuttingDown 0x4C "logged you out"; login answers
  "Game Server not found." for a world that is down; a login queue exists (LoginQueueStatus 0xE0) (operations.md,
  gateway-service.md) [P].
- [I] The client never reconnects by itself: live kept a player through a zone crash only if the gateway held the
  connection; losing the gateway always meant the disconnect box.

### 6. Gateway opcode 9 and dynamic commands

- **Opcode 9** = a slash command for the gateway itself: bare BasePacket 9 + ClientCommandPacket with its zone header
  `1E 00 43 00` (builder FUN_14337d580 / FUN_1432d0790) [P]. The client receives only gateway opcodes 2 / 4 / 5 / 7 / 8
  (switch FUN_143a4bd20 / FUN_143993e40), so the gateway answers through tunnelled packets [P]. Its list is
  CommandList source 3; the client names only `/gatewayprofiler` (counts per message signature, FUN_143c79940) [P].
  Built and verified on 0.695 (01d1a37; DynamicCommands.log "Command from server: gatewayprofiler").
- **Dynamic commands**: CommandList 0x1E/0x44 with source 1 zone / 2 world / 3 gateway (FUN_1433bad50 /
  FUN_14330df60), executed as 0x43 on channel 0, channel 1 or opcode 9 (FUN_1433c3d80 / FUN_143316f90) [P];
  AddWorldCommand 0x45 / AddZoneCommand 0x46 and ExecuteCommand 0x47 {u32 name hash, string args}, verified on 0.695
  ("/duel Nobody" → `__sendworldcommand duel Nobody`, hash resolved by the server; 9e59ec8) [P]. [I] Three back ends
  each owned commands: zone, world, gateway.

### 7. Admin player flag

- SendSelfToClient carries an admin-player bool (reader FUN_142c19bd0 → player +0x7b0d8 / FUN_142b80420 → +0x7aea8),
  checked as "admin client OR admin player" in 37 0.695 functions; the admin-client slot (+0xB0) returns 0 in retail
  (architecture/admin-monitor.md) [P]. [I] Live granted admin to characters from the server; a retail client then sent
  the admin Command subs (≥ 0x42A: goto, target, hideme, runspeed, game / client time, prop spawn, friend force-add,
  vehicles ...) to the server. Emulator: flag for WorldServer.json Admins (5aeed5d), subs in Game/AdminCommands.cs
  (0eb4af7), untested in game.
- The admin ids with no client code (WorldKick*, BroadcastZone / World, LockStatus*, ServerInformation, ZoneVars /
  WorldVars, InternalConnectionBypass, GatewayProfilerRegistration; internal-messages.md §1) [P names] → [I] the world
  level administered its zones and gateways.

### 8. Operations notes (2015-2016)

- Restarts: three early mornings a week from Jun 2015 because spawn pools needed them (P3643, P3639); Mon + Sat 3 a.m.
  Pacific from 25 Jun 2016 (P5902) and "occasional overnight bounces ... to improve performance" (P5906) [P] →
  [I] long-running zones degraded and were restarted on purpose.
- Two kinds of fix: client data pushed through the patcher without server downtime (P5680, P6221, P6382) and server
  data that needs a restart (P6223 "they apply the actual data", P6448 prop disassembly) [P].
- Updates took **all worlds** down together ("All Landmark worlds will be down", P6534); downtime was extended when
  tests failed (P6207) [P].
- Login was its own failure domain: "login issues" with unscheduled downtime and an operations team improving alerts
  (P6543, P6550, P6554) [P].
- Limits were set by "engineers monitoring server performance" (prop link count, P5251) [P].

### 9. Open leads

New: **L114** (how a world change reached another world's servers; gateway scope; CharacterTransferServer trigger),
**L115** (world list history: merges / closures 2015-2016). Existing: L31 (Game Finder join), L60 (teleport queue),
L70 (gateway behaviour on back-end loss), L73 (zone-process handover; §3 names added), L109 (Events world id).
