# LandmarkEmulator.WebAPI

**Library**, hosted by the [AuthServer](Project-AuthServer) process. The HTTP API for accounts and for LaunchPad.

Back to [Code Documentation](Code-Documentation).

## Endpoints

| Route | What |
|---|---|
| `GET /` | A short text answer (health check) |
| `GET /info` | What LaunchPad shows for the server: name, login port, the in-game web pages, the account website link, the game build players need, news, and the patches offered |
| `POST /srp/register` | Create an account with SRP-6a: the client sends a salt and verifier, never the password |
| `POST /srp/start`, `POST /srp/finish` | SRP-6a login; the result is a session key the game client logs in with |
| `POST /create`, `POST /authenticate`, `POST`/`DELETE /delete` | Older account routes: user name and password in a form or JSON body (at most 4 KB; a password in the URL is refused); answered only for requests from the server's own PC; `/create` follows the sign-up rules |
| `GET /patches`, `GET /patches/{id}/pack.json`, `GET /patches/{id}/pack.lmpatch` | The patch folder: the list, a pack's manifest and the pack itself |

Sign-ups are limited per address and per hour (`REGISTRATION_OPEN`, `REGISTRATIONS_PER_ADDRESS_PER_HOUR`,
`REGISTRATIONS_PER_HOUR`); only the website proxy (`TrustedProxy`) may name the player's own address. See
[Account Creation](Account-Creation) and [Settings Reference](Settings-Reference#sign-ups).

## Main files

| Path | What |
|---|---|
| `AuthWebAPI.cs` | The ASP.NET Core minimal API: routes, rate limits, the `/info` answer |
| `PatchHost.cs` | The patch folder: every pack is checked with the same rules LaunchPad uses (`LandmarkEmulator.PatchPacks`) before it is listed; refused packs move to `rejected/` with the reason |
| `Configuration/WebApiConfig.cs` | The `WebApi` settings block of `AuthServer.json` |
| `Models/Auth/` | Request bodies |

## Talks to

- `lmemu_auth` (accounts) through the Database projects.
- [PatchPacks](Project-PatchPacks) for the pack checks. See [Hosting Patches](Hosting-Patches) and
  [Format: LMPatch](Format-LMPatch).
