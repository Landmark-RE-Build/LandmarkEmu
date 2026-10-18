# Server Installation on Windows

## 1. Install Docker Desktop

Install **Docker Desktop** for Windows from docker.com, start it once, and accept its terms. It uses WSL 2; the
installer sets that up. Wait until Docker Desktop says it is running.

You need about 10 GB of free disk and 16 GB of RAM or more.

## 2. Get the project and your game ready

1. Download or clone the project into a folder of its own, for example `C:\LandmarkEmu`
   ([Server Cloning](Server-Cloning)).
2. Make sure your own Landmark folder is **build 0.695.1.132647** (install the update with LaunchPad first:
   [Game Data](Game-Data)).
3. Recommended: download **`LandmarkEmu-terrain-heights.zip`** from the project's Releases and put it, still zipped,
   in the project folder next to `docker-compose.yml`.

## 3a. The easy way: start.bat

Double-click **`start.bat`** in the project folder (or run it in a terminal). On its first run it:

1. starts Docker Desktop if it isn't running;
2. copies `.env.example` to `.env` and asks for your **Landmark folder**, your **account name** for admin rights, and
   the **address for players** (it lists this PC's addresses; leave it empty to play on this PC only);
3. fills in two random database passwords;
4. if you gave an address, offers to open the server ports in Windows Firewall (Windows asks for permission);
5. builds and starts everything (the first build takes several minutes) and waits until the world is up.

It then shows the address players add in LaunchPad and the sign-up website's address.

> start.bat writes the admin name as you type it. Write it as `account:YourName` (or check `ADMIN_ACCOUNT` in `.env`
> afterwards and add `account:` in front): see [Account Creation](Account-Creation#admins).

Later:

| Command | |
|---|---|
| `start.bat` | Start (or apply changed settings in `.env`) |
| `start.bat -Stop` | Stop everything (all data stays) |
| `start.bat -Logs` | Watch the world's console (Ctrl+C leaves) |
| `start.bat -Update` | Rebuild after downloading a new version of the project |
| `start.bat -Setup` | Ask the first-run questions again |

`start.bat` also starts and stops every extra world in `docker\worlds\*.env`.

## 3b. By hand: docker compose

Everything `start.bat` does can be done by hand. In a terminal in the project folder:

```
copy .env.example .env
notepad .env
```

Set `GAME_DIR` (forward slashes, e.g. `C:/Program Files (x86)/Steam/steamapps/common/Landmark`), the two passwords,
`PUBLIC_HOST` if others will play, and `ADMIN_ACCOUNT=account:<your account name>`. Then:

```
docker compose up -d --build
docker compose logs -f world
```

The world is ready about a minute after it starts (Ctrl+C leaves the log; the servers keep running).

## 4. Open the firewall (players on other PCs)

`start.bat` offers to do this. By hand, in an administrator terminal:

```
netsh advfirewall firewall add rule name=LandmarkEmu-UDP dir=in action=allow protocol=UDP localport=20042,19000
netsh advfirewall firewall add rule name=LandmarkEmu-TCP dir=in action=allow protocol=TCP localport=5000,19001,8080
```

(Use your own ports if you changed them in `.env`.) For players outside your home network without a VPN, forward the
same ports on your router to this PC. See [Server Installation](Server-Installation#players-on-other-pcs).

## 5. Play

1. Install LaunchPad in your Landmark folder ([Client Connection Guide](Client-Connection-Guide)).
2. Add the server: `127.0.0.1` on this PC (other players use `PUBLIC_HOST`).
3. Create your account ([Account Creation](Account-Creation)) and press PLAY.

## Troubleshooting

| Problem | What to do |
|---|---|
| `docker compose` can't connect to Docker | Start Docker Desktop, wait until it says it is running, try again |
| "Set GAME_DIR / DB_PASSWORD in .env" | `.env` is missing or that setting is empty |
| "GAME_DIR in .env is not your Landmark folder" | `docker compose logs setup` shows it; fix `GAME_DIR` (the folder with `Landmark64.exe`) |
| The world doesn't come up | `docker compose logs world`; the error is usually near the end |
| Names are missing in game | The Landmark folder had no `Locale\en_us_data.dat`; check `GAME_DIR`, then `docker compose up -d` |
| The database port closes connections at the handshake (seen with Docker Desktop's port forward) | Use the client inside the container: `docker exec -it landmarkemu-db-1 mariadb -ulandmarkemu -p` |
