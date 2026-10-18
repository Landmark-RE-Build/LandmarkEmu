# LandmarkEmulator.SocialServer

**Program** (Docker service `social`, optional, compose profile `social`; one for all worlds). The social systems
shared by every world, as on live Landmark where friends, guilds and mail reached across servers.

Back to [Code Documentation](Code-Documentation).

## What it does

Each world's zone dials it (`SOCIAL_ADDRESS`, port 20200 on the Docker network) and sends a presence snapshot first;
the login server asks it who is online for the character-select friends lists. The services moved out of the world
server with the same client behaviour:

| Service | File | What |
|---|---|---|
| Presence | `PresenceService.cs` | Who is online and where; a grace time (`SOCIAL_PRESENCE_GRACE_SECONDS`) so changing world shows no logout |
| Friends | `FriendService.cs` | Friends lists and their online notices, across worlds |
| Ignore lists | `IgnoreService.cs` | The client's Blocked list |
| Groups | `GroupService.cs` | Parties across worlds |
| Guilds | `GuildService.cs` | Guilds and rosters across worlds |
| Chat | `ChatService.cs` | Tells and channels that reach players on any world |
| Mail | `MailService.cs` | One inbox for every world |
| Game Finder | `MatchmakingService.cs` | One list of searchers and site games for every world |

With `SOCIAL_ADDRESS` empty, each world runs the same systems itself (`Game/Social/LocalSocialService.cs` in the
world server).

## Main files

| Path | What |
|---|---|
| `SocialServer.cs`, `HostedService.cs` | Start-up: the database (create / migrate `lmemu_social`, the one-time import from the character database when its tables are empty), then the internal listener |
| `SocialServerConfiguration.cs` | `SocialServer.json` ([Settings Reference](Settings-Reference#socialserverjson)) |
| `SocialHub.cs` | One world's link |
| `ClientPackets.cs` | Client packets the service builds for the zones to deliver |
| `Design/SocialContextFactory.cs` | EF Core design-time factory |

Database: [Database: Social](Database-Social), model in [Database.Social](Project-Database-Social).
