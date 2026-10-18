# LandmarkEmulator.CommerceServer

**Program** (Docker service `commerce`, optional, compose profile `commerce`; one for all worlds). Player Studio, the
account store, contests, portraits and Showcase following for every world at once, like live's single commerce back
end.

Back to [Code Documentation](Code-Documentation).

## What it does

Each world's zone dials it (`COMMERCE_ADDRESS`, port 20400 on the Docker network) and says which world it is and which
character database (realm) it uses. The Showcase web pages stay on each world and read their commerce data through it.

| Service | File | What |
|---|---|---|
| Player Studio / Marketplace | `ProductService.cs` | Product creation, the listing fee, peer review (`PLAYER_STUDIO_*`), the staff queue, buying a design and the creator's share (`studio_payout`) |
| Contests | `ContestService.cs` | The Contest window's contests, entries from every world, votes and phases |
| Portraits | `PortraitService.cs` | Character portraits and headshots (files in its data folder) |
| Following | `FollowService.cs` | Followed creators and tags, and the daily digest mail (`SHOWCASE_FOLLOW_DIGEST`, `SHOWCASE_DIGEST_HOUR_UTC`) |

With `COMMERCE_ADDRESS` empty, each world runs the same code itself (`Game/Commerce.cs`, `Game/Contests.cs`,
`Game/Portraits.cs`, `Game/ShowcaseFollow.cs` through `LocalCommerceService`).

## Main files

| Path | What |
|---|---|
| `CommerceServer.cs`, `HostedService.cs` | Start-up: the database and the one-time import of the in-process Player Studio rows, then the internal listener |
| `CommerceServerConfiguration.cs` | `CommerceServer.json` ([Settings Reference](Settings-Reference#commerceserverjson)) |
| `CommerceHub.cs` | One world's link |
| `CommerceStore.cs` | `lmemu_commerce`, plain SQL ([Database: Commerce](Database-Commerce)) |
| `ClientPackets.cs` | Client packets the service builds for the zones to deliver |

Client side and rules: `docs/commerce.md` and `docs/showcase.md` in the repository.
