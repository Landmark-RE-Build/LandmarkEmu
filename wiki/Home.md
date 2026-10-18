# Welcome to LandmarkEmu

LandmarkEmu is a fan-made server emulator for **Landmark**, the voxel building MMO by Daybreak that closed in
February 2017. Its goal is to bring back the game's servers as they were when the game shut down, for the last client
build, **0.695.1.132647**: the world you dig and build in, build sites, designs, props, crafting, gathering, gear and
combat, creatures and caverns, chat, friends, groups, guilds, mail and trade, goals, site games and the story tools.
The servers are written in C# (.NET), run in Docker, and are open source under the GNU AGPL v3. Everything the server
does is worked out from the game client itself and from what the developers published at the time.

> **LandmarkEmu is in heavy development.** Many systems are built but not yet checked in game, and things change
> quickly. See [Project Status](Project-Status).

You need your own copy of Landmark from Steam. This project contains no game files.

---

## Where do you want to start?

### I'm a player and want to play

You need a copy of Landmark in your Steam library, **LaunchPad** (the project's replacement for the game's own
launcher) and the address of a LandmarkEmu server.

- [Client Connection Guide](Client-Connection-Guide): install LaunchPad, add a server, create an account, play.
- [LaunchPad](LaunchPad): everything LaunchPad does, including the update from the Steam build to 0.695.
- [FAQ](FAQ): common questions.

### I'm a developer or server hoster and want to set up my own server

The servers run with Docker Compose on Windows or Linux. Hosting for yourself on one PC takes a few minutes; hosting
for others needs an address they can reach and a few open ports.

- [Server Guide](Server-Guide): the overview, in order.
- [Server Installation on Windows](Server-Installation-Windows) and [on Linux](Server-Installation-Linux).
- [Game Data](Game-Data): what the server reads from your own Landmark folder.
- [Account Creation](Account-Creation) and [Hosting Patches](Hosting-Patches).

> The project is in heavy development. Run a server for yourself and friends, back up your data, and expect changes
> between versions. There is no "official" public server.

### I'm a developer and want to contribute

- [Contributing](Contributing): how to help, and what a change needs before it is merged.
- [Code Style](Code-Style), [Database Style](Database-Style), [Database Migrations](Database-Migrations),
  [Bug Submission](Bug-Submission).
- [Server Building](Server-Building): build the solution and run the tests.
- Documentation: [Code](Code-Documentation), [Assets](Asset-Documentation), [Database](Database-Documentation),
  [Commands](Command-Documentation) and the [Settings Reference](Settings-Reference).

---

LandmarkEmu is a fan project. It is not affiliated with or endorsed by Daybreak Game Company; Landmark is a trademark
of its owners.
