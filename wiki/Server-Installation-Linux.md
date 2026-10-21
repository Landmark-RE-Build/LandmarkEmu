# Server Installation on Linux

The servers run on Linux with **Docker Engine** and the **compose plugin**. (Most testing so far has been on Windows
with Docker Desktop; if something behaves differently on a Linux host, please open an issue:
[Bug Submission](Bug-Submission).)

## 1. Install Docker

Install Docker Engine and the compose plugin from Docker's documentation for your distribution, then check:

```
docker compose version
```

Add your user to the `docker` group (or use `sudo` for the commands below). You need about 10 GB of free disk and
16 GB of RAM or more.

## 2. Get the project and the game data

```
git clone https://github.com/Landmark-RE-Build/LandmarkEmu.git LandmarkEmu
cd LandmarkEmu
```

([Server Cloning](Server-Cloning).) The server needs a **Landmark folder at build 0.695.1.132647** on this machine
([Game Data](Game-Data)):

- if Steam on this machine installed Landmark, that folder (e.g. `~/.steam/steam/steamapps/common/Landmark`) after
  the 0.695 update;
- otherwise copy a 0.695 Landmark folder from a Windows PC (updated with LaunchPad there), unchanged, for example to
  `/srv/landmark`.

Recommended: download `LandmarkEmu-terrain-heights.zip` from the project's Releases into the project folder, still
zipped.

## 3. Settings

```
cp .env.example .env
nano .env
```

Set at least:

- `GAME_DIR=/srv/landmark` (the folder with `Landmark64.exe` and `Resources`);
- `DB_ROOT_PASSWORD` and `DB_PASSWORD`: two long random passwords, e.g. from `openssl rand -hex 16`. Set them before
  the first start;
- `PUBLIC_HOST`: the address players reach this machine on (empty = this machine only);
- `ADMIN_ACCOUNT=account:<your account name>`.

Every setting: `.env.example` and the [Settings Reference](Settings-Reference).

## 4. Start

```
docker compose up -d --build
docker compose logs -f world
```

The first build takes several minutes. The `setup` step copies the game data, then the servers start; the world is
ready about a minute later. Ctrl+C leaves the log, the servers keep running. They start again by themselves after a
reboot (`restart: unless-stopped`) as long as the Docker service starts at boot.

The everyday commands, backups and extra worlds are the same everywhere: [Server Installation](Server-Installation).

## 5. Open the ports

With `ufw`, for example (use your own ports if you changed them):

```
sudo ufw allow 20042/udp
sudo ufw allow 19000/udp
sudo ufw allow 5000/tcp
sudo ufw allow 19001/tcp
sudo ufw allow 8080/tcp
```

Don't open `DB_PORT`: the database is published on `127.0.0.1` only. For players outside your network without a VPN,
forward the same ports on your router.

## 6. Play

Players (on Windows) add this machine's address in LaunchPad ([Client Connection Guide](Client-Connection-Guide)),
create an account ([Account Creation](Account-Creation)) and press PLAY.

## Troubleshooting

| Problem | What to do |
|---|---|
| "GAME_DIR in .env is not your Landmark folder" | `docker compose logs setup`; `GAME_DIR` must contain `Landmark64.exe` and `Resources` |
| Permission errors on `server-data` | The containers write to `server-data/` in the project folder: make sure your user (or Docker) can write there |
| The world doesn't come up | `docker compose logs world`; the error is usually near the end |
| Address clashes with another Docker network | Set `NETWORK_NAME` / `WEB_SUBNET_PREFIX` in `.env` (Advanced section) |
