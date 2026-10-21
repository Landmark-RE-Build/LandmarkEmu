# LandmarkEmu developer documentation

These documents explain how the Landmark client talks to a server and how this emulator answers it. Everything here
was worked out by observing and reading the retail client, so that anyone can extend the server without repeating
the research. The server speaks the **0.695.1.132647** client (the last build, x64; protocol.md §1.6) and refuses the
Steam build 0.589.1.128895, on which most layouts were first traced; LaunchPad updates a 0.589 install to 0.695
(launchpad.md). Where a layout differs between the two, the documents say so.

| Document | What it covers |
|---|---|
| [docker.md](docker.md) | Running the servers in Docker: the services, every `.env` setting, more worlds, the optional gateway / world-channel / social / commerce / Environment Server containers, the account website, backups. |
| [landmark-local.md](landmark-local.md) | LandmarkLocal: solo forever-local Windows host — servers on 127.0.0.1 plus in-app Play (account = admin). Docker stays the multi-user path. |
| [launchpad.md](launchpad.md) | LaunchPad: installing it, the Steam check, the verified 0.695 update and its SHA-256 list, `.lmpatch` packs, the server's patch folder, what PLAY does. |
| [protocol.md](protocol.md) | Transport (SOE UDP, gateway tunnel, zone opcodes), the 0.695 wire numbering, packet layouts for every system the server handles, and the rules the client enforces when reading them. |
| [gameplay.md](gameplay.md) | How each server system works: sessions, inventory, abilities and movement, gathering, building, build sites, props and crafting, account items, chests, social, goals, creatures, combat, grief, landscapes, teleports, caverns and outposts, graveyards, death, developer commands. |
| [live-rules.md](live-rules.md) | The live game's rules from the wiki, the update notes and the developers' posts, each with its source and what the server does (matches, fixed with a live-default setting, missing), and the conflicts between sources. |
| [terrain.md](terrain.md) | The VoxelFarm terrain: the Environment Server split, cell ids, the block and mesh formats, edits, heals, ore veins, grass, the event islands, caves, long teleports, voxel templates and stamps, the client's mesh cache, slow loading screens. |
| [sendself.md](sendself.md) | The SendSelfToClient record block by block and field by field, with the client code behind each name. |
| [client-reference.md](client-reference.md) | Where things live in the client: function addresses, loading-bar stages, the client data tables the server reads, the UI / Lua entry points, the client's log files. |
| [admin.md](admin.md) | Admin players: the SendSelf admin flag, admin entries, the client's admin slash commands, broadcasts and kicks, the server monitor page. |
| [commerce.md](commerce.md) | In-game commerce (InGamePurchase 0x45) and Player Studio: product creation, peer review, the listing fee, buying designs, the commerce service. |
| [showcase.md](showcase.md) | The Showcase window: the server's store and gallery pages, following and the daily mail, sign-in by token, the web API, icons, running Lua in the client, likes, portraits. |
| [membership.md](membership.md) | Membership: the client's membership state, PacketMembershipActivation 0x66, MemberStatus, the login queue's member priority, buying it with Lumens, the trial key. |
| [vendors.md](vendors.md) | Build site vendors (player shops), the client script gaps patched at run time, the NPC merchant window, Point-of-Need purchases. |
| [instanced-games.md](instanced-games.md) | Site games: rule sets from the client tables, the InstancedGame packets, zone control, adventures, the client's game editor, the Game Finder. |
| [story-tools.md](story-tools.md) | Story tools: the ObjectScripting (Links) protocol, Play Mode, every story prop's events and actions, the prop settings window, story creatures. |
| [npc-scripting.md](npc-scripting.md) | World NPCs: JSON definitions, per-player flags, dialogue bubbles, achievement quests, admin commands, playtests. |
| [encounters.md](encounters.md) | Encounters (EncounterBase 0x2C): the packets in both directions, duels, the death window, the world-channel split. |
| [observer-sync.md](observer-sync.md) | What other players see of a player: every action and state checked so the actor's screen and the observers' agree (now, arriving later, after a re-zone, walking into range), the range of each, the fixes and the open leads. |
| [client-to-server-shifts.md](client-to-server-shifts.md) | What the 0.589 client did by itself that the 0.695 client leaves to the server (the hotbar right-click, game HUD, Game Table settings, Parlor, Underground Adventure...), how both clients were scanned for it, and what the server does about each. |
| [request-variants.md](request-variants.md) | Client messages with more than one sender (window, slash command, right-click menu, popup, Lua): what each sender fills (name vs guid, defaults, optional fields), how the server reads every variant, and every right-click menu row with the arguments its Lua function gets. |
| [unknowns.md](unknowns.md) | The record of how every field once called `Unknown*` was named, and the few slots whose reader is still not found. |
| [bugs.md](bugs.md) | Known bugs found while running or developing the emulator (open and fixed). |

`img_story_node_settings.png` is the screenshot story-tools.md refers to.

## Conventions

- **Byte order** is little-endian unless stated. Types: `u8/u16/u32/u64`, `i32`, `f32`, `f64`, `bool` (1 byte),
  `string` (u32 length + bytes, no terminator), `vec4` (4 × f32), `list<T>` (u32 count + items).
- **Opcodes** are written as the client sends them. Many systems use a *base opcode* followed by a sub-opcode;
  the width of that sub-opcode differs per system (u8, u16 or u32) and is given for each.
- **World units**: the client's local frame is metres; "global" coordinates are decimetres measured from the
  continent origin (`global = local × 10 + zone origin`). The zone origin used here is
  `(25584000, 20000, 25564500)` dm.
- **Client addresses** (for example `FUN_142c83660`) are function entry points in `Landmark64.exe` with image base
  `0x140000000`, as named by Ghidra: 0.695 where the text says so (or the 0.695 decompile is cited), else 0.589. They
  are given so contributors can find the same place in their own copy of the client. No client code is reproduced in
  this repository.
- **[P] / [I]**: proven from the client (or a capture) / inferred. A field is only called "verified" when the real
  client accepted it in game.
- **Lnnn**, **TASKS n.n**: entries in the project's open-leads and task lists (`research/trace/0695/` in the published
  repository).

## How the client was studied

- **Static:** Ghidra on `Landmark64.exe`. Packet readers were found from their packet class names (RTTI strings such
  as `PacketMailInboxListResponse`) and the zone dispatcher.
- **Live:** read-only Frida hooks on the running client (log arguments and return values, never change them) to
  confirm a reading or see which branch the client takes.
- **Client files:** the plain-text tables in `Resources/*.txt`, the UI definitions in `UI/Default/*.xml|*.lua`,
  `Resources/Scripts/*.lua`, the client's own log files, and the `.pack` asset archives (read only).
- **The rule of thumb:** the running client is the source of truth.

## Legal note

Landmark is a trademark of Daybreak Game Company. This project ships no game files, no extracted assets and no
decompiled code; you need your own copy of the game. The server reads some of the client's data files from your
installation at run time (see [client-reference.md](client-reference.md#client-data-the-server-reads)).
