# LandmarkEmu developer documentation

These documents explain how the Landmark client talks to a server and how this emulator answers it. Everything
here was worked out by observing and reading the retail client (Steam build **0.589.1.128895**, x64), so that
anyone can extend the server without repeating the research.

| Document | What it covers |
|---|---|
| [protocol.md](protocol.md) | Transport (SOE UDP, gateway tunnel, zone opcodes), packet layouts for every system the server handles, and the rules the client enforces when reading them. |
| [terrain.md](terrain.md) | The VoxelFarm terrain: cell ids, the block (voxel) format the server sends, the mesh format, how player edits overlay the client's own generated world, ground heights, ore veins and grass. |
| [gameplay.md](gameplay.md) | How each server system works: login/logout, inventory and equipment, abilities and mining, gathering, building, claims, props and crafting, social (chat, friends, groups, guilds, mail), health, travel, the world clock. |
| [client-reference.md](client-reference.md) | Where things live in the client: function addresses with descriptive names, client data tables the server reads, client log files, the UI/Lua entry points behind each menu. |
| [instanced-games.md](instanced-games.md) | Site games: rule sets from the client tables, the InstancedGame packets (state, HUD, scoreboard, queues, observers), zone control, adventures, and the client's own game editor. |
| [story-tools.md](story-tools.md) | Story tools: the ObjectScripting (Links) protocol, Play Mode, every story prop's events and actions, the prop settings window, story creatures. |
| [vendors.md](vendors.md) | Build site vendors (player shops): the ClaimVendor packets, the client script gaps patched at run time, the Showcase "My vendors" API; the NPC merchant window; Point-of-Need purchases. |
| [commerce.md](commerce.md) | In-game commerce (InGamePurchase 0x45) and Player Studio: product creation, the client-driven peer review, result mail kinds, TemplateData XML. |
| [showcase.md](showcase.md) | The Showcase window: the server's web pages (free store, build site gallery), player sign-in by token, the web API, icon export, and running Lua in the client (`ClientLua.RunCode`). |
| [sendself.md](sendself.md) | The SendSelfToClient record field by field (0.589). |
| [unknowns.md](unknowns.md) | Fields whose purpose is still unknown, and what was ruled out. |

## Conventions

- **Byte order** is little-endian unless stated. Types: `u8/u16/u32/u64`, `i32`, `f32`, `f64`, `bool` (1 byte),
  `string` (u32 length + bytes, no terminator), `vec4` (4 × f32), `list<T>` (u32 count + items).
- **Opcodes** are written as the client sends them. Many systems use a *base opcode* followed by a sub-opcode;
  the width of that sub-opcode differs per system (u8, u16 or u32) and is given for each.
- **World units**: the client's local frame is metres; "global" coordinates are decimetres measured from the
  continent origin (`global = local × 10 + zone origin`). The zone origin used here is
  `(25584000, 20000, 25564500)` dm.
- **Client addresses** (for example `FUN_142c83660`) are function entry points in `Landmark64.exe` 0.589 with
  image base `0x140000000`, as named by Ghidra. They are given so contributors can find the same place in their
  own copy of the client. No client code is reproduced in this repository.

## How the client was studied

- **Static:** Ghidra 12 (headless and through an MCP server) on `Landmark64.exe`. Packet readers were found from
  their packet class names (RTTI strings such as `PacketMailInboxListResponse`) and the zone dispatcher.
- **Live:** read-only Frida hooks on the running client (log arguments and return values, never change them) to
  confirm a reading or see which branch the client takes.
- **Client files:** the plain-text tables in `Resources/*.txt`, the UI definitions in `UI/Default/*.xml|*.lua`,
  `Resources/Scripts/*.lua`, the client's own log files, and the `.pack` asset archives (read only).
- **The rule of thumb:** the running client is the source of truth. A field is only called "verified" here when
  the real client accepted it in game.

## Legal note

Landmark is a trademark of Daybreak Game Company. This project ships no game files, no extracted assets and no
decompiled code; you need your own copy of the game. The server reads some of the client's data files from your
installation at run time (see [client-reference.md](client-reference.md#client-data-the-server-reads)).
