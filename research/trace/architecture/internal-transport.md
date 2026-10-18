# Internal transport between the Docker services (Phase B contract)

Every Phase B service (gateway, zone / world server, social, terrain, world-channel, Environment Server) talks to the
others only over the network, so a container can move to another machine by changing an address in `.env`. This file
is the one contract they all follow; each service spec (social-service.md, gateway-service.md, terrain-service.md,
world-channel-service.md, environment-server.md) lists its own messages in these terms.

## What is traced (client evidence) and what is our design

| Part | Status | Source |
|---|---|---|
| The gateway is the client's only connection; back ends receive the client's packets by **channel** (0 Zone, 1 World, 2 Position, 3) and answer through the gateway | traced | channels.md, login-gateway.md |
| Gateway ↔ back end packets are `Gateway::InternalPackets::PacketTunnelPacketFromExternalConnection`, `PacketTunnelPacketToExternalConnection`, `PacketTunnelPacketToExternalConnectionList` (one packet to many players); ToExternalConnectionList is internal opcode **0x16**, header **11 bytes + 12 per destination** | traced (names, opcode, header size) | architecture-leads.md L8 (ApplicationPacketTracker FUN_143c78d10) |
| A channel's back end can be up or down on its own (`ChannelIsRoutable`, `ConnectionIsNotRoutable` to the client) | traced | channels.md |
| Services talked with **the client's own packet families and sub ids** (ZoneAddClaim, ConsumeAttachmentZoneRequest, WorldKick, BroadcastWorld, EnvServerResponse ...) | traced names / ids; field layouts **not found yet (L48)** | architecture-leads.md L8 |
| Field layouts of the internal packets, framing, service discovery | **our design** (below) — the client never sees them | — |

## Design (ours, keep it small)

1. **One framed TCP stream per pair of services.** Frame: `u32 length, u8 internal opcode, body` (u8: see "Changes adopted" below) (little endian like
   the game). Connections are made by the *client side* of each pair to an address from `.env`
   (e.g. `SOCIAL_ADDRESS=social:20200`) and re-made on loss; a service that can't reach a back end tells the gateway
   so it can report the channel as not routable.
2. **Gateway internal opcodes** (names from the client; numbers ours except 0x16):
   - `TunnelPacketFromExternalConnection` (gateway → back end): u64 connection id, u64 character guid, u8 channel,
     bytes = the client packet exactly as received (after ClientWire695 header translation, so back ends always see
     the 0.589 numbering).
   - `TunnelPacketToExternalConnection` (back end → gateway): u64 connection id, bytes = a zone packet in 0.589 form;
     the gateway applies the per-build translation (ClientWire695) for that connection.
   - `TunnelPacketToExternalConnectionList` **0x16**: u32 n, n × destination (u64 connection id + u32, = 12 bytes,
     matching the traced 12 per destination), bytes. Per-build bodies (PerBuild) are sent as one list per layout.
   - Connection events (ours): character entered / left the world (connection id, character guid, name, world id,
     zone id, client build), so back ends know who is online where without polling.
3. **Service-to-service messages** reuse the client's family / sub ids and names where one exists (ZoneAddClaim 0xF9
   0x48, ConsumeAttachmentZoneRequest 0xF8 0x11, BroadcastWorld 0x1E 0x43E, ...), wrapped in a frame with internal
   opcode `ServicePacket` = u16 family, u32 sub, body (our layout, documented in the owning service's spec). New
   messages with no client name get a name in the same style and are marked "ours".
4. **Data ownership:** each service owns its tables (its own database / schema); nobody reads another service's
   tables. Shared lookups (character name ↔ guid, online status) go through the owning service's messages.
5. **Restart behaviour:** a back end restarting must not drop the player's connection (the gateway keeps it and
   reports the channel not routable meanwhile) — the reason live split the gateway out.

## Changes adopted from gateway-service.md (2026-10-15, verified by the coordinator)

- The client's packet tracker (FUN_143c7a9c0) reads a **one-byte internal opcode**: **7** and **8** with fixed header
  sizes (ctor values 0x13 / 0xd) and **0x16** with 11 bytes + 12 per destination (count read at offset 7); external
  packets there are 5 / 6. So the gateway link frames start with a u8 opcode (not u16); 7 / 8 / 0x16 are live's numbers
  (which of 7 / 8 is "from" vs "to" is an inference, gateway-service.md), the rest ours.
- Back ends dial the gateway (the gateway owns the client connection and outlives them).
- Field layouts in gateway-service.md fit those header sizes.

## Order (BUILD-PATH Phase B)

Specs first (one agent each, trace/0695/AGENTS.md), then build one service at a time, each tested in an isolated
Docker copy: 1 social, 2 gateway, 3 terrain, 4 world-channel, 5 Environment Server, 6 zones per landscape (only if
needed), 7 admin page, 8 operations.
