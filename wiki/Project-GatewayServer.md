# LandmarkEmulator.GatewayServer

**Program** (Docker: `docker/gateway`, optional, one per world or one for several worlds). The gateway as its own
process: it owns the world's client-facing UDP port, and the zone and other back ends dial it.

Back to [Code Documentation](Code-Documentation).

## What it does

- Listens on the world's game port (`WORLD_PORT`) with the [Gateway](Project-Gateway) session code.
- Checks each login with the login service's internal port (`LOGIN_INTERNAL_ADDRESS`, the auth container's 20210):
  the single-use server ticket and that the character belongs to the account. Without that address it reads the
  auth and character databases itself.
- Keeps a **routing table** of back ends (`RoutingTable.cs`, `ZoneBackEnds.cs`) on its internal port 20300. A back
  end registers its kind (zone, world channel, social, Environment Server, commerce), its world, the landscapes it
  serves and the client channels it takes. Channel 0 goes to the zone serving the player's landscape; channels 1-3 to a
  service registered for that channel, else to the zone.
- **Holds connections** while their zone is away (a crash or a container restart) for `GATEWAY_HOLD_SECONDS`, then
  sends the player to character select.
- **Several worlds** (`GATEWAY_MULTI_WORLD`): zones of other worlds may register too; the zones are told which worlds
  are reachable and may hand a connection to another world's zone. The 0.695 client changes world by re-zoning on the
  same connection, so one gateway reaching every world is how the client expects travel between worlds to work.

## Main files

| Path | What |
|---|---|
| `GatewayServer.cs`, `HostedService.cs` | Start-up |
| `GatewayServerConfiguration.cs` | `GatewayServer.json` ([Settings Reference](Settings-Reference#gatewayserverjson)) |
| `GatewayClient.cs` | One client connection and its channel → back end answers |
| `RoutingTable.cs` | The registered back ends |
| `ZoneBackEnds.cs` | The internal side: registrations, attach / close, tunnelled packets back to clients |

Run it with the world's compose files plus `docker/gateway/world.override.yml` (which makes the world dial the
gateway); `docs/docker.md` in the repository has the commands.
