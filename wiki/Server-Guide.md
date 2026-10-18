# Server Guide

How to run your own LandmarkEmu server. The supported way is **Docker Compose**: one settings file (`.env`), one
command, every server in its own container. It works the same on Windows and Linux.

> **LandmarkEmu is in heavy development.** Run it for yourself and your friends, back up your data, and expect changes
> between versions. Public servers are not something the project supports yet.

## What you need

- A PC with **Docker**: Docker Desktop on Windows (or macOS), Docker Engine with the compose plugin on Linux. About
  10 GB of free disk and 16 GB of RAM or more.
- **Your own Landmark folder**, updated to build 0.695.1.132647 (the update is installed with LaunchPad on a Steam
  copy). The server copies its data tables from it and reads its terrain data; nothing in it is changed. See
  [Game Data](Game-Data).
- **This project**, downloaded or cloned.
- Recommended: **`LandmarkEmu-terrain-heights.zip`** from the project's Releases (ground heights for trees, creatures
  and starting build sites).

You don't need the .NET SDK to host: Docker builds the servers itself. You need it only to develop
([Server Building](Server-Building)).

## The steps

1. **Get the code**: [Server Cloning](Server-Cloning).
2. **Prepare the game data**: [Game Data](Game-Data).
3. **Install and start the servers**: [Server Installation](Server-Installation), then
   [on Windows](Server-Installation-Windows) (Docker Desktop, `start.bat`) or
   [on Linux](Server-Installation-Linux) (`docker compose`).
4. **Create your account and make yourself an admin**: [Account Creation](Account-Creation).
5. **Optional: offer the 0.695 update to your players**: [Hosting Patches](Hosting-Patches).
6. **Tell your players the address.** They add it in LaunchPad ([Client Connection Guide](Client-Connection-Guide)).

Every setting: [Settings Reference](Settings-Reference) and the notes in `.env.example`.

## For developers

To build the code yourself, run the tests or work on the servers: [Server Building](Server-Building)
([Windows](Server-Building-Windows), [Linux](Server-Building-Linux)).
