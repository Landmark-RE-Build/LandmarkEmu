# Server Cloning

Get the project's source code. Use the repository this wiki belongs to: its address is under the green **Code**
button on the repository's main page.

## Branches

| Branch | For |
|---|---|
| `main` | The server for the 0.695.1.132647 client, which everything in this wiki describes |
| `client-0.589` | The older server for the Steam build 0.589 (no longer developed) |

## With git

```
git clone https://github.com/Landmark-RE-Build/LandmarkEmu.git LandmarkEmu
cd LandmarkEmu
```

To update later (keep your `.env`, it isn't part of the repository):

```
git pull
docker compose up -d --build
```

## Without git

On the repository page, **Code > Download ZIP** (the `main` branch), and unpack it anywhere,
for example `C:\LandmarkEmu`. To update, download the new ZIP, unpack it over the old folder (your `.env` and
`server-data` folder stay), and run `docker compose up -d --build` (or `start.bat -Update` on Windows).

## What's in the folder

| Folder / file | |
|---|---|
| `LandmarkEmulator.*` | The servers, LaunchPad, the shared code, the database projects and the tests |
| `docker-compose.yml`, `.env.example`, `docker/` | How the servers run in Docker |
| `start.bat`, `start.ps1` | The optional Windows helper |
| `docs/` | Developer documentation: protocol, terrain, every system |
| `research/` | The research notes the server is built from (when present) |
| `data/` | Small tables derived from the client |
| `tools/` | Optional tools (terrain-height baking, the genuine-file hash list) |

Next: [Game Data](Game-Data), then [Server Installation](Server-Installation).
