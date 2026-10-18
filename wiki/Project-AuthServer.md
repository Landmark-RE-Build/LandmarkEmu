# LandmarkEmulator.AuthServer

**Program** (Docker service `auth`). The login server: the first server the game client talks to, and the host of the
web API that LaunchPad and the sign-up website use. One login server serves every world.

Back to [Code Documentation](Code-Documentation).

## What it does

- **Login** over SOE UDP (`LOGIN_PORT`, default 20042): the client logs in with the session key LaunchPad passed it,
  gets its character list, creates and deletes characters (name rules: `STRICT_NAMES`, the client's name blacklist),
  and the server list with each world's state.
- **Character login**: issues the single-use server ticket for the chosen world and sends the client that world's
  address (each world writes its own address into the `zone_server` table; `WorldAddress` forces one for all).
- **Friends at character select**: each character in the list carries its friends list, so the client can count each
  world's online friends (`CHARACTER_SELECT_FRIENDS`); online state comes from the social service when
  `SOCIAL_ADDRESS` is set.
- **Login queue**: a world at its `MAX_PLAYERS` limit makes further logins wait, members first.
- **Web API**: starts [WebAPI](Project-WebAPI) in the same process (port 5000 inside the container, `ACCOUNT_PORT`
  outside).
- **Login internal port** (`Internal`, default 0.0.0.0:20210, Docker network only): gateways running as their own
  process consume server tickets and ask for relog sessions here instead of reading the database
  (`LoginInternalService.cs`).
- **Console**: when run in a terminal, `account create <username> <password>` (Docker disables the console).

## Main files

| Path | What |
|---|---|
| `AuthServer.cs`, `HostedService.cs` | Start-up: configuration, database migrations, game tables, the UDP listener, the web API, the internal port |
| `AuthServerConfiguration.cs` | `AuthServer.json` ([Settings Reference](Settings-Reference#authserverjson)) |
| `Network/AuthSession.cs` | One client connection (SOE session, encryption) |
| `Network/Handlers/LoginHandler.cs` | Login, character list / create / delete / login, server list |
| `Network/Message/Model/` | The login packets (`LoginRequest`, `CharacterSelectInfoReply`, `ServerListReply`, `CharacterCreateRequest` …) and the tunnel data packets (name validation, claim and art data) |
| `CharacterSelectFriends.cs` | The friends lists sent with the character list |
| `LoginInternalService.cs` | The internal port for separate gateways and world channels |
| `Command/AuthCommandHandler.cs` | The console command |
| `Design/` | EF Core design-time factories (for `dotnet ef migrations`) |
| `Resources/ArtTints.json` | Tint data shared with the world server |

## Talks to

- Game client (SOE UDP), LaunchPad and the website (HTTP, through WebAPI).
- `lmemu_auth` and `lmemu_character` ([Database: Auth](Database-Auth), [Database: Character](Database-Character)).
- The social service (friends' online state), when configured.
- Gateways and world channels in their own process (internal port 20210).

Packet layouts: `docs/protocol.md` §2 "Login server" in the repository.
