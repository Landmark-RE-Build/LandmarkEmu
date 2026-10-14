# Running LandmarkEmu in Docker

Two kinds of container stacks, matching how live Landmark was organised:

- **Auth stack** (`docker/auth`), one per installation:
  - MariaDB holds the accounts and the server list (`lmemu_auth`), the shared characters (`lmemu_character`), and each world's own database (`lmemu_world_<key>`).
  - The auth (login) server.
- **World stacks** (`docker/world`, one per world). A world is a "server" in the game's list, such as Serenity or Liberation. Like on live, every world has the whole set of landscapes, and its build sites, props, chests, vendors, prop links and terrain are its own. Characters are shared, so one character can play on every world.

Without a `Database:Zone` setting a world server keeps everything in the character database (the older way of running it directly on Windows).

## Starting it

`start.bat` (in the project folder) does everything; see the README. What it runs, for anyone working by hand:

```
docker compose --env-file network.env --env-file docker/auth/.env -p landmarkemu-auth -f docker/auth/docker-compose.yml up -d --build
docker compose --env-file network.env --env-file docker/auth/.env --env-file docker/worlds/main.env -p landmarkemu-world-main -f docker/world/docker-compose.yml up -d --build
```

The settings files are read in that order; a later file's value wins:

| File | Holds |
|---|---|
| `network.env` | The address players reach this PC on (`PUBLIC_HOST`) and every port: the one file to edit for networking |
| `docker/auth/.env` | Database passwords (random), your game folder (`GAME_DIR`), the server's game data (`GAME_DATA_DIR`), the name and sign-up settings |
| `docker/worlds/<name>.env` | One world: `WORLD_KEY`, `WORLD_ID`, `WORLD_NAME_ID`, `WORLD_DATA_DIR`, admins, and its own ports if it isn't the first world |

The first run makes them from the `.example` files next to them.

## More worlds

1. Copy `docker/worlds/main.env` to e.g. `liberation.env` and change:
   - `WORLD_KEY=liberation`, which gives the database `lmemu_world_liberation`.
   - `WORLD_ID=2`, unique and never reused.
   - `WORLD_NAME_ID=560912`, which shows as "Liberation". The client's server names are listed in the file.
   - `WORLD_DATA_DIR`, a folder of its own.
   - `WORLD_PORT=19010` and `WEB_PAGES_PORT=19011`: free ports, which override network.env's for this world.
2. Run `start.bat`: it starts every world in `docker/worlds`. Open the new ports in the firewall too.

On start a world writes its own entry in the server list: id, name and `PUBLIC_HOST:WORLD_PORT`. The auth server picks it up within a few seconds; players see it after logging in again.

## Account website

The auth stack also runs a sign-up page (service `web`, nginx) on `WEBSITE_PORT`, default 8080. Players open `http://<server address>:8080`, choose a name and password, and then sign in with LaunchPad.

How it is kept safe:

- **The password never leaves the player's browser.** The page derives the SRP salt and verifier itself (`docker/web/site/srp.js`, the same maths as `Shared/Network/Cryptography/Srp.cs`, checked against .NET) and sends only those. The server stores only the verifier.
- **No database access.** The web container is only on the `landmarkemu-web` network, together with the auth server:
  - It can reach the auth server's account API and nothing else. It can't even resolve the database's or the worlds' names.
  - It has no passwords.
  - It runs read-only, as a non-root user.
  - The database port is published on 127.0.0.1 only.
- **One route.** Only `POST /api/register` is passed on (to `/srp/register`). Every other `/api/` path returns 404, and other methods return 403.
- **Spam limits, in layers:**
  - nginx: 5 sign-ups a minute per address (burst 3), 60 a minute in total, and 20 connections per address.
  - Auth server: `REGISTRATIONS_PER_ADDRESS_PER_HOUR` (default 3) and `REGISTRATIONS_PER_HOUR` in total (default 60). These limits apply to launcher sign-ups as well.
  - `REGISTRATION_OPEN=false` closes sign-ups.
  - The page also has a hidden field that bots fill in, and a 2-second minimum before submitting.
- **The real player address.** nginx replaces `X-Forwarded-For` with the address it sees, so a client can't fake one. The auth server trusts that header only from the website's fixed address, 172.30.77.10 (`WebApi:TrustedProxy`).
- **Security headers:** a strict Content-Security-Policy, no framing, nosniff and no-referrer.

For a public server, put HTTPS in front of port 8080, for example with Caddy or a Cloudflare tunnel. The password is never sent even over plain HTTP, but HTTPS stops anyone on the way from changing the page. Behind such a proxy, set nginx's `real_ip_header` / `set_real_ip_from` so the limits still see the player's address.

## Players on other PCs

- Set `PUBLIC_HOST` in `network.env` to the address they reach this PC on: LAN IP, VPN IP (Tailscale / ZeroTier) or host
  name, then run `start.bat`.
- Open the ports: UDP `LOGIN_PORT` and `WORLD_PORT`, TCP `ACCOUNT_PORT`, `WEB_PAGES_PORT` and `WEBSITE_PORT` (and each
  extra world's own two ports).

## Everyday commands

- `start.bat -Logs`: follow the world's console. `start.bat -Stop`: stop everything; data stays.
- `start.bat -Update`: rebuild after code changes.
- By hand, add `logs -f`, `restart` or `down` to the compose commands above.

Where the data lives:
- `landmarkemu-auth_db` (a Docker volume) holds every database.
- Each world's saved files are in its `WORLD_DATA_DIR`, a folder on this PC (`server-data\worlds\<name>` by
  default). With it empty, they go in a Docker volume, `landmarkemu-world-<name>_data`.
  - The folder holds the terrain edits, screenshots, contests, reports, felled trees and gathered nodes.
  - It also holds the world's `console.log`, and `dev_commands.txt`: a line appended there runs as a dev command,
    e.g. `shutdown 60 Restarting`.

`docker compose down -v` deletes the volumes. A `WORLD_DATA_DIR` folder stays.

The database is published on `127.0.0.1:DB_PORT` (3307) for tools like HeidiSQL. If Docker Desktop's forward of that
port closes connections at the handshake (seen on one Windows PC), run the client inside the container instead:
`docker exec -it landmarkemu-auth-db-1 mariadb -ulandmarkemu -p`.

## Moving from servers run directly on Windows

Earlier versions ran the servers directly on Windows with MariaDB installed. To move such a server to Docker:

1. **Back up.** Dump `lmemu_auth` and `lmemu_character` with `mariadb-dump --single-transaction --databases ...`.
   Archive the world server's folders (`TerrainEdits`, `SiteImages`, `Contests`, `Reports`, `Portraits`,
   `TemplateImages`, `TemplateProps`) and its `felled_trees.txt` and `gathered_nodes.txt`.
2. **Import both dumps into the Docker database, as bytes.** They contain binary data.
   - Windows MariaDB writes table names in lower case, but on Linux EF needs `__EFMigrationsHistory`. Rename it in the
     dumps.
   - Check first that both sides have the same migrations.
3. **Copy the world's own rows into `lmemu_world_<key>`,** after its first start (which creates the schema): `claim`,
   `claim_permission`, `claim_vendor_item`, `world_prop`, `world_prop_binding`, `world_prop_setting` and `chest_item`.
   The old servers kept them in the character database; a Docker world reads them from its own.
4. **Unpack the world files into the world's data folder,** stop the old servers, and run `start.bat`.

## How the world split works (code)

- `CharacterDatabase.SeparateWorld` is true when `Database:Zone` is set.
- World tables (`claim`, `claim_permission`, `claim_vendor_item`, `world_prop`, `world_prop_binding`, `world_prop_setting`, `chest_item`) are read and written through `NewWorldContext()` / `SaveWorld()`.
  - Callers: `Claims`, `Chests`, `Props`, `Vendors`, `PropSettings`, `StoryTools`.
  - Everything else uses the shared database.
  - Both databases are created by the same migrations; each uses its own part.
- IDs that servers pick themselves are allocated per world, so two running worlds never collide:
  - Item guids: world n uses n × 2^28 up to the next.
  - Guild ids: world n × 2^20.
- `WorldServer.json`:
  - `WorldNameId` and `PublicAddress` give the server-list entry (`AuthDatabase.RegisterZoneServer` at start).
  - `WebPagesPort` and `WebPagesUrl` give the in-game web pages.
- The auth server (`ZoneServerManager.Update`) adds new worlds and address changes while it runs.

Not yet shared live between running worlds: guilds, mail, friends and Player Studio are cached per world. A change on one world reaches another when that world restarts. Cross-world sync is the next step.
