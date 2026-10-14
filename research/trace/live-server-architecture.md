# How the live Landmark servers were built (what can be found, 2026-10-15)

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
  - Every tunnelled packet carries a 3-bit channel: 0 Zone, 1 World, 2 PositionUpdate, 3 Voxel
    (LandmarkEmulator.Gateway/Network/Message/Channel.cs).
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
- **Terrain is fetched per cell, by cell id** (ClientCellRequest, `u64 cell = level<<60 | x<<36 | y<<18 | z`) on its own
  Voxel channel (LandmarkEmu/docs/terrain.md). A gateway could send each request to the terrain server that owns that
  cell, but the client can't show whether there was one terrain server or several: it only ever sees the gateway.

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

## For the emulator

The emulator handles every channel in one world process. The client is fine with that because it only talks to the
gateway. If the server ever needs to scale, the client's own design shows where the seams are: per channel (Voxel /
PositionUpdate / Zone), with a claim service and a mail service on their own.
