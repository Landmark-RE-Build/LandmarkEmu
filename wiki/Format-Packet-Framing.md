# Format: Packet Framing

How a packet travels between the game client and the servers, layer by layer, and how the server programs talk to
each other. A summary; every packet family's layout is in `docs/protocol.md` in the repository.

Back to [Asset Documentation](Asset-Documentation).

## Layers (client ↔ server)

```
UDP datagram
└─ SOE protocol packet          big-endian u16 opcode, CRC, optional compression flag
   └─ Data / DataFragment        reliable, sequenced payload (RC4-encrypted after the gateway login)
      └─ Gateway packet          opcode + channel
         └─ Tunnel packet        TunnelPacketToExternalConnection (S→C) / FromExternalConnection (C→S)
            └─ Zone packet       u16 base opcode, then a sub-opcode of the family's width, then the body
```

### SOE reliable UDP

The protocol of Sony Online Entertainment's games, used by both the login server and the world's gateway. Protocol
opcodes are **big-endian** u16:

| Opcode | Name | Use |
|---|---|---|
| 0x01 / 0x02 | SessionRequest / SessionReply | Negotiate the CRC seed and length, the largest packet, compression |
| 0x03 | MultiPacket | Several protocol packets in one datagram |
| 0x05 | Disconnect | Either side |
| 0x06 | Ping | Keep-alive |
| 0x07 / 0x08 | NetStatusRequest / Reply | Client statistics |
| 0x09 / 0x0D | Data / DataFragment | Reliable, sequenced payload; fragmented when large |
| 0x11 / 0x15 | Ack / AckAll | Acknowledgements |

The server resends an unacknowledged packet after `300 ms + 1.25 × ack ping`, doubling the wait for each further resend
(capped at 8 s). Compression (the block codec, below) is negotiated but left off: the payload is RC4 ciphertext by then
and does not shrink.

### Gateway and channels

The client logs in to the gateway with `LoginRequest` (character id and the single-use server ticket); after the reply
the data is RC4-encrypted with the session key. Game packets then travel in tunnel packets carrying a **channel**:

| Channel | Use | Answered by |
|---|---|---|
| 0 Zone | Almost every game packet, terrain cell requests included | the zone |
| 1 World | Friends, guilds, ignore lists, chat membership, trade invitations, Player Studio, logout to character select | the world-channel service if one is registered, else the zone |
| 2 PositionUpdate | Player movement | the zone |
| 3 | Build-cursor previews | the zone |

The gateway tells the client `ChannelIsRoutable` when a channel loses or regains its back end.

### Zone packets

A zone packet starts with its opcode: mostly one byte and a zero byte (written as a u16, e.g. `F9 00`), then a
sub-opcode whose **width depends on the family** (u8, u16 or u32), then the body. Examples: PlayerUpdate `0x26` (u32
sub in 0.695), Command `0x1E` (u16), InventoryBase `0x2D` (u16), TerrainCell `0xEC` (u32), PropBase `0xED` (u8),
VoxelConstructionBase `0xF4` (u8), ClaimsBase `0xF9` (u8). The server reads and writes every opcode and sub-opcode
exactly as the **0.695.1.132647 client** numbers them; there is no translation layer.

Rules the client enforces: most readers reject a packet with bytes left over or missing; every request the client
waits on must be answered (an unanswered ability cast leaves it "cast pending" for good).

### Body primitives

Little-endian: `u8 / u16 / u32 / u64`, `i32`, `f32`, `bool` (one byte), `string` = u32 byte length + bytes (no
terminator), `list<T>` = u32 count + items.

## The block codec

Used for terrain cells and for compressed SOE bodies: magic `A1 B2 C3 D4`, a **big-endian** u32 uncompressed size,
then a zlib stream. Terrain layer blobs also put a little-endian u32 raw size in front.

## Login server

The login server speaks the same SOE layer (no gateway): login with the LaunchPad session key, character list,
create / delete, character login (which issues the server ticket and the world's address), server list. Layouts:
`docs/protocol.md` §2.

## Internal transport (between the server programs)

Ours. One TCP connection per link; every message is `u32 length` + message, the message's first byte being its
opcode and the body following in the same little-endian primitives. The gateway's opcodes: `0x07` / `0x08` tunnelled
client packets, `0x16` one packet for several connections, `0x20` RegisterBackEnd (kind, world, landscapes, channel
mask, name, build stamp), `0x21` AttachConnection, `0x22` ConnectionAttached, `0x23` ConnectionClosed, `0x24`
CloseConnection, `0x25` HandOver, `0x26` PositionCopy, `0x27` ZoneChanged, `0x28` Ping, `0x29` WorldsReachable, `0x30`
ServicePacket (a service's own message inside). Each service's messages are classes in
[LandmarkEmulator.Internal](Project-Internal).

## Web traffic

Not packets, but the other way clients reach the servers: LaunchPad and the sign-up website use the login server's
HTTP API ([WebAPI](Project-WebAPI)); the game's in-game browser opens the world's web pages (Showcase, store, admin
menu) over HTTP.
