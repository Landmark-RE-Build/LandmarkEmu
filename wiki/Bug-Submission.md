# Bug Submission

Report bugs as **GitHub issues** on the project's repository. One issue per problem; search the open issues first, and
add to an existing one if it is the same bug.

## What to include

1. **What you did**, step by step, so someone else can do the same: where you were (world, landscape, on a build
   site or not), what you clicked or typed, and with which item or prop.
2. **What happened**, and **what you expected**. If you know how live Landmark behaved, say so and where that comes
   from (a patch note date, a wiki page).
3. **Versions**: the LandmarkEmu version or commit the server runs (`git log -1 --oneline` in the project folder, or
   the release name), and the client build LaunchPad shows at the top left (it should be 0.695.1.132647).
4. **Server settings** you changed from `.env.example` (just the names and values; **never** the passwords).
5. **Logs** (below), cut to the time of the problem.

A short video or your own screenshot of the problem helps, too.

## Client logs

The game writes its logs into the `Logs` folder inside your Landmark folder (next to `Landmark64.exe`). The most
useful ones:

| Log | Shows |
|---|---|
| `Landmark PlayClient (Test) (x64).log` | The game's main log: loading, zone connections, slow frames |
| `NetInfo.log` | Connection details and disconnect reasons |
| `LuaErrors.log`, `FlashErrors.log`, `Ui.log` | Errors in the game's windows |
| `ItemDefinitions.log`, `ActionBarManager.log`, `CalculatedStat.log` | Data the game accepted or rejected |
| `LoadingScreen.txt` | When the loading screen dropped |

Attach the whole log of the session with the problem, or the part around the time it happened. Close the game first so
the files are complete.

For a LaunchPad problem, copy the message LaunchPad showed. Don't attach `%AppData%\LandmarkEmu\launchpad.json`: it
holds your saved login keys.

## Server logs (server owners)

With the Docker setup:

```
docker compose logs --since 30m world      # the world server
docker compose logs --since 30m auth       # login, accounts, LaunchPad requests, patch checks
docker compose logs setup                  # the game-data copy at start
```

The world's full console log is also in `server-data/worlds/<WORLD_KEY>/console.log` (`main` by default). With the
optional services, add `social`, `commerce` or `envserver` as needed.

Before posting, check the log for anything private (the address of your PC if you don't want it public, account
names of other players).

## Security problems

If you find a way to harm players or servers (for example getting a file past LaunchPad's checks), open an issue that
names the part affected and what could happen, but leave out the exact steps to exploit it.
