# LandmarkEmulator.Internal

**Library.** The transport between the server programs and the message classes of every service link, so both ends of
a link share one definition.

Back to [Code Documentation](Code-Documentation).

## The transport

- One **TCP connection** per link (`InternalLink.cs`): every message is framed as `u32 length` + the message; the
  message's first byte is its opcode (`InternalOpcode.cs`), the body follows (`InternalBuffer.cs` writes and reads the
  primitives). Sends are queued and written in order; received messages are handed on in order.
- `InternalListener` (the service side) and `InternalDialer` (the client side, which reconnects by itself) wrap a link.
  A service can restart and the worlds reconnect without restarting themselves.
- Gateway opcodes: `0x07` / `0x08` tunnelled client packets (from / to the client), `0x16` a packet for several
  connections, `0x20` RegisterBackEnd, `0x21` AttachConnection, `0x22` ConnectionAttached, `0x23` ConnectionClosed,
  `0x24` CloseConnection, `0x25` HandOver, `0x26` PositionCopy, `0x27` ZoneChanged, `0x28` Ping, `0x29`
  WorldsReachable, `0x30` ServicePacket (a service's own message inside). The numbers 7, 8 and 0x16 match the internal
  opcodes the client's own packet tracker knows; the rest are this project's.
- `ServiceStatus.cs`: the service states shown on the admin monitor page.

## Message folders

| Folder | Link |
|---|---|
| `Login/` | Gateways and world channels ↔ the login service's internal port: server tickets, relog sessions (`LoginClient.cs`) |
| `Social/` | Worlds ↔ the social service: presence, friends, ignores, groups, mail, matchmaking; the character-select friends lists |
| `Commerce/` | Worlds ↔ the commerce service: products, the staff queue, Player Studio rules, the following digest |
| `Environment/` | Zone ↔ Environment Server |
| `WorldChannel/` | Zone ↔ world channel: trades, encounters (duels), ratings |
| `InternalMessages.cs` | The gateway messages (`RegisterBackEnd` and the others above) |

The ports (20200 social, 20210 login, 20300 gateway, 20310 zone, 20400 commerce, 20500 Environment Server) are only on
the Docker network. See [Format: Packet Framing](Format-Packet-Framing).
