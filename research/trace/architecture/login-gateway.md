# Login server → gateway → back ends

Wire formats: LandmarkEmu/docs/protocol.md §1-3, §6 (already traced and running in the emulator). This file covers
the service boundaries, from the client (0.589). Status: partial.

## Login server (its own service)

- Classes: `Login::ExternalLoginApi` with two transports, `Login::ExternalLoginTcpApi` (ctor FUN_1439a0400) and
  `Login::ExternalLoginUdpApi` (ctor FUN_1439a04a0, "ptr:LoginUdp_10" FUN_1439a0540). Neither ctor has a static
  caller in the decompile; **the retail client uses UDP** — proven by the emulator: its UDP login server works with
  the unmodified client (`Server=` in ClientConfig.ini). What would select TCP: not found yet (OPEN-LEADS L6).
- Packets (`Login::ExternalPackets::*`): LoginRequest / Reply, ServerListRequest / Reply, ServerUpdate,
  CharacterSelectInfoRequest / Reply, CharacterCreate / Delete / Login Request / Reply,
  CharacterTransferServerRequest / Reply, ForcedDisconnect, Logout, and **TunnelAppPacketClientToServer /
  ServerToClient** (app messages carried through the login connection, before the gateway — what used them is to
  trace). `Login::GameServerData`.
- Login-level states in the client: "Login Queue Info: Position=%d, EstWaitTime=%0.2f" (FUN_1435d8460, a login
  queue), "Login Server is locked at this time." / "Unable to authenticate with Login Server." (FUN_142c72eb0),
  "Unable to connect to Login Server." (FUN_14300ec40), LoginServer authentication result (FUN_14300de40).
- Business environments (`BusinessEnvironmentManager`, `PacketBusinessEnvironments` handled at FUN_1430352e0): sent
  to the client, contents **to trace**.

## Handover: CharacterLoginReply → gateway

- The login server answers CharacterLoginRequest with the gateway to use; the client logs "Gateway Connect Info,
  ConnectTime = %d, Guid = %llu, Address = %s, Ticket = %s" (FUN_142cba700). The login server is therefore the
  directory of gateways (one per world, by the world chosen).
- FUN_142cba700: reads `UseCompression` from config, creates the gateway connection (client vtable +0x250), sets up
  the secure channel ("Failed to establish s-channel with gateway.", FUN_142c0fc20 "CSC - Gateway is NULL."),
  rejects an empty address ("Invalid gateway address.").
- The gateway UDP layer accepts only expected connections (`UdpParams.processExpectedConnectionsOnly`): the ticket
  from the login server is the expectation.
- Re-login without the login server: "Did not receive One-Time Session Id from server." (FUN_142cbb660), "Lost
  connection to the gateway while waiting for a relogin session.", "Timed out while waiting for a relogin session."
  (FUN_142c3b490); logout to character select uses CharacterSelectSessionRequest / Response on the World channel
  (channels.md) — the World back end issues the session that the login server then accepts.

## Gateway (its own service per world)

- External packets (`Gateway::ExternalPackets::*`, opcodes per LandmarkEmulator.Gateway GatewayMessageOpcode):
  1 LoginRequest, 2 LoginReply, 3 Logout, 4 ForcedLogout, 5 TunnelPacketToExternalConnection, 6
  TunnelPacketFromExternalConnection, 7 ChannelIsRoutable, 8 ConnectionIsNotRoutable, **9 (no class name found yet): a
  ClientCommandPacket for the gateway itself** (dynamic-commands.md).
- Internal packets compiled into the client (server-side library code): `Gateway::InternalPackets::
  PacketTunnelPacketFromExternalConnection`, `PacketTunnelPacketToExternalConnection`,
  `PacketTunnelPacketToExternalConnectionList` — back ends exchange tunnelled packets with the gateway, and can send
  one packet to a list of connections.
- Routing: 4 channels (channels.md); per-channel routability messages.
- Profiling: "GATEWAY STATS ... Use '/gatewayprofiler on' to enable." (FUN_143164670) and per message signature
  "count, totalNano, totalMs, totalRecipients, totalBytes (gatewayHeaderBytes, applicationBytes)" — admin page 24.
- Position updates can come from the gateway itself: `PlayerUpdatePacketUpdatePositionGatewayToClient` (handled in
  FUN_142d58570) next to the zone's `PlayerUpdatePacketUpdatePositionZoneToClient`.

## To trace next

1. TunnelAppPacket (login) users. 2. PacketBusinessEnvironments contents. 3. ServerUpdate (server list updates while
at character select). 4. CharacterTransferServerRequest / Reply (moving a character between worlds). 5. Gateway
opcode 9 replies. 6. The gateway-sourced position path.
