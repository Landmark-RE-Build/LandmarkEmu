# Dynamic (server-registered) slash commands

Traced 2026-10-15 in the 0.589 client.

## Registration: CommandListPacket (Command 0x1E, sub 0x44)

- Command family handler FUN_14330e110, `case 0x44`: builds `CommandListPacket` (opcode 0x1E, sub 0x44), reads it with
  FUN_1432d5410 and passes it to FUN_14330df60.
- Layout (pkt.py FUN_1432d5410): `u16 0x1E, u8/i16 sub 0x44`, then a list (u32 count, count × string: command
  name), then `u32 source` (+0x38).
- FUN_14330df60: logs to DynamicCommands.log "command list from %s with %d entries." with `source == 1 ? "zone" :
  "world"`; for each name not already known it creates a `ServerCommand` (RTTI vtable 144696150: execute
  FUN_143316f90) holding the name and the source, registers it with the client's command system (FUN_142d39300 with
  the source), logs "Command from server: %s"; a repeat is logged "Duplicate command from server, ignored: %s".

So **the world server and the zone server each sent the client their own command list**.

## Execution: ClientCommandPacket (Command 0x1E, sub 0x43)

FUN_143316f90 (ServerCommand execute): builds `ClientCommandPacket` (0x1E / 0x43) with the command name
(StringFixed<64>) and the typed arguments split into a string list (StringFixed<256> input), logs "Command Executed:
%s", then sends it by the command's source:

| Source | Sent as |
|---|---|
| 1 (zone) | tunnel, channel 0 (Zone) — FUN_1432dfea0(gateway, packet, 0, 1) |
| 2 (world) | tunnel, channel 1 (World) — FUN_1432dfea0(gateway, packet, 1, 1) |
| 3 | FUN_1432d0790: a **gateway external packet with opcode 9** wrapping the ClientCommandPacket directly (no channel byte): commands handled by the gateway itself. No class name for opcode 9 in the 0.589 RTTI (the emulator's gateway knows opcodes 1-8 only) → not found yet, OPEN-LEADS L5. |

Logged "Execution: %s".

## For the emulator

- Today the world server handles slash commands itself. To match live: the zone server, the world server and the
  gateway each send a CommandListPacket with their own commands (source 1 / 2 / 3 — the source values the client
  understands: 1 zone, 2 world, 3 gateway; the list's own `source` field is compared only with 1 for the log text),
  and each answers ClientCommandPacket on its own channel.
- Which commands live used in each list: not found yet (server data; OPEN-LEADS). Candidates are the commands players used
  (wiki Commands List) — assign by the channel their client packets already use (channels.md).
