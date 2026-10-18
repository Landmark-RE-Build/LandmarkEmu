# LandmarkEmulator.Gateway

**Library.** The client-facing side of a world connection, shared by the world server's in-process gateway and by
[GatewayServer](Project-GatewayServer).

Back to [Code Documentation](Code-Documentation).

## What it does

- The **SOE reliable UDP session** on top of the shared networking code ([Shared](Project-Shared)): sequencing,
  acknowledgements, fragments, resends with back-off.
- The **gateway login**: the client's `LoginRequest` (character id + server ticket), the `LoginReply`, then RC4
  encryption with the session key. Only 0.695 clients are accepted (`Network/ClientBuilds.cs`).
- **Tunnel packets**: the client's game packets arrive wrapped in `TunnelPacketFromExternalConnection` with a channel
  byte (0 zone, 1 world, 2 position updates, 3 build previews) and leave in `TunnelPacketToExternalConnection`.
  `ChannelIsRoutable` tells the client when a channel loses or regains its back end.
- **What every packet passes before the zone sees it** (`Network/ZoneGatewaySession.cs`): flood limits and the drop
  of one client packet kind the zone never needs. Packets are otherwise passed unchanged: the server speaks the 0.695
  client's numbering natively.
- **Gateway commands**: the gateway's own `/gatewayprofiler` (see [Commands: Debug](Commands-Debug)).

## Main files

| Path | What |
|---|---|
| `GatewayProvider.cs` | Message tables and set-up |
| `Network/GatewaySession.cs` | The gateway session: login, tunnels, channels, the command list |
| `Network/ZoneGatewaySession.cs` | The per-packet checks; where the zone is (in-process or a back end) is the subclass's business |
| `Network/ClientBuilds.cs` | Which client builds are accepted |
| `Network/Message/` | Gateway opcodes and message models (`LoginRequest`, `LoginReply`, `ChannelIsRoutable`, `ForceDisconnect`, `TunnelPacketToExternalConnection`) |
| `Network/Packets/GatewayPacket.cs` | The gateway packet frame |

Formats: [Format: Packet Framing](Format-Packet-Framing) and `docs/protocol.md` §1 in the repository.
