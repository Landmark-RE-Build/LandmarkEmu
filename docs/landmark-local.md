# Play alone on this PC (LandmarkLocal)

Use this when you only want to play LandmarkEmu **by yourself on this Windows PC**.  
Want friends over the internet? Use Docker instead: [docker.md](docker.md).

## What you need

1. **Landmark** installed from Steam on this PC.
2. The **LandmarkLocal** zip from [Releases](../../releases) (unpack it anywhere, e.g. `C:\LandmarkLocal`).

The release zip is **self-contained** (Windows x64): you do **not** need to install the .NET or ASP.NET Core runtimes.

## How to play

1. Double-click **`LandmarkLocal.exe`**.
2. If the Landmark install isn’t set yet, **Settings → Game folder** opens for you. Browse to the folder with `Landmark64.exe` (usually found automatically under Steam, including extra Steam libraries); a good pick closes Settings, a wrong folder warns. You can reopen Settings (⚙) anytime for language, February-build options, or **Client** (DirectThreadCount recommended 2, optional experimental allocator / contour patches).
3. Choose an **account name** and **password** (first time). That account is both your normal play login and **admin** on this local world.
4. Click **PLAY** — LandmarkLocal starts the servers if needed (first time can take several minutes while it downloads the database and terrain), waits until Ready, creates the account if needed, updates the game when the server hosts a pack, and starts Landmark.

You can still use **Start** to bring servers up without launching the game, then **PLAY** when you are ready.

Later days: open LandmarkLocal → **PLAY** (password is remembered; servers start if they were stopped).  
To shut the servers down: **Stop** (the status changes to **Stopping…** while processes exit — that can take up to about a minute).

**Closing the window:** if servers are still running you will be asked whether to **Stop and quit**, **leave them running** (LandmarkLocal stays in the system tray), or cancel. Minimizing while servers are up also sends LandmarkLocal to the tray.

You do **not** need to open LaunchPad for local play. LaunchPad is still installed into your Landmark folder if you later join a remote (Docker) server.

## If Start or Play fails

| Message / symptom | What to do |
|---|---|
| Landmark folder invalid | Pick the folder that has `Landmark64.exe` inside it. |
| Terrain heights / download failed | Auto-download uses the fixed [terrain](https://github.com/Landmark-RE-Build/LandmarkEmu/releases/tag/terrain) release. If that fails: get `LandmarkEmu-terrain-heights.zip` from there, put it next to `LandmarkLocal.exe` (still zipped), click Start or PLAY again. |
| Port already in use | Often leftover servers from a previous run. Click **Stop**, close other LandmarkEmu/Docker copies, try again. Closing the window without stopping can leave servers running (use the tray or Stop). |
| Wrong account name or password | Clear the remembered login by changing the account name, or enter the correct password. |
| Game build mismatch | Put the `.lmpatch` update in the `patches` folder next to LandmarkLocal, Start again, then Play. |
| Windows blocks the app / Smart App Control | On some Windows 11 PCs, Smart App Control blocks unsigned downloads. Turn Smart App Control off (or allow the app) if Windows will not run `LandmarkLocal.exe`. |
| Landmark fails with a DirectX / graphics error on a fresh PC | Install the [DirectX End-User Runtime](https://www.microsoft.com/en-us/download/details.aspx?id=35) from Microsoft, then try Play again. That is for the game client, not LandmarkLocal. |

## Your saves

Everything stays in the LandmarkLocal folder (the one with `LandmarkLocal.exe`).  
To back up or move: click **Stop**, then copy that whole folder.

---

### Optional: game update packs

To let Play (and LaunchPad) update the game from this PC’s server, put `.lmpatch` files in the `patches` folder next to `LandmarkLocal.exe`. See [launchpad.md](launchpad.md).

### Optional: command line

```
LandmarkLocal setup
LandmarkLocal start
LandmarkLocal play
LandmarkLocal stop
LandmarkLocal status
```

### Building from this repo

```
powershell -File tools/publish-landmark-local.ps1
```

Output goes to `artifacts/LandmarkLocal/` (self-contained win-x64). The root is a single `LandmarkLocal.exe` plus folders (`servers/`, `LaunchPad/`, `data/`, …) — not a DLL pile. Dev runs use `LandmarkLocal/run/` and still need a local .NET SDK/runtime.

### Releases (GitHub Actions)

How to ship a zip (PR → merge → Release, major/minor bumps, where to click):  
**[devops-landmark-local.md](devops-landmark-local.md)**.
