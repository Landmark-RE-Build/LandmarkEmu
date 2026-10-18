# Live server architecture: client traces

Everything here is traced from the client (0.589 decompile re/decomp_all; 0.695 when re/decomp_all_695 is done), with
function addresses. Nothing is guessed: anything not proven yet is listed in ../0695/OPEN-LEADS.md and searched again later. Summary and recreation plan:
../live-server-architecture.md. Build order: ../../BUILD-PATH-0695.md Phase A / B.

| File | What |
|---|---|
| [channels.md](channels.md) | Which back end each client message goes to (gateway channel per message class) |
| [channels-589.tsv](channels-589.tsv) | The full table: message class → channel(s), from every send site |
| [dynamic-commands.md](dynamic-commands.md) | Slash commands registered by the world and zone servers, and how they are routed |
| [admin-monitor.md](admin-monitor.md) | The performance monitor (Alt+F) and its server pages |
| [login-gateway.md](login-gateway.md) | Login server, handover to the gateway, gateway packets and routing |
| [recreation-design.md](recreation-design.md) | Live's split per channel / service next to what LandmarkEmu runs, status, and the recommended build order (2026-10-16) |
| [gateway-service.md](gateway-service.md), [world-channel-service.md](world-channel-service.md), [social-service.md](social-service.md), [terrain-service.md](terrain-service.md) | Phase B service specs |
| [internal-transport.md](internal-transport.md), [internal-messages.md](internal-messages.md) | Transport between the services; live's server-to-server message names and ours |
| [operations.md](operations.md), [admin-pages.md](admin-pages.md) | Restarts / health / saving; the server-side admin pages |
| [pathing.md](pathing.md) | Live's pathing as the client shows it (page 32, admin commands 0x4A2-0x4AA) and our creature navigation (TASKS 6.61) |

Tool: `python re/tools/channels.py [decomp] [out]` regenerates channels-589.tsv (run it on re/decomp_all_695 too).

To do (Phase A): voxel.md (Voxel
channel and terrain service), environment-server.md, procedural.md.
