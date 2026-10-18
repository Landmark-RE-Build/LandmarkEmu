# Contributing

Thanks for wanting to help. LandmarkEmu recreates a game whose servers are gone, so most of the work is finding out
**what the live server did** and then building exactly that. Code, research, testing and bug reports all help.

## Ways to help

- **Play and report.** Many systems are built but not yet checked in game ([Project Status](Project-Status)). Playing
  through them and reporting what breaks is one of the most useful things you can do: [Bug Submission](Bug-Submission).
- **Research.** Work out how the client handles something the server doesn't answer yet, or find a source (an
  archived developer post, a patch note) for one of the open questions. Write up what you found in your own words.
- **Code.** Fix a bug, build a missing piece, or move a system closer to how live did it.
- **Documentation.** Explain a format or a system that the documentation pages don't cover yet.

## The rules every change follows

1. **The client is the source of truth.** Every packet layout, rule and behaviour comes from the game client (its
   code, data tables, UI scripts, its own log files, or what it does when it runs) or from what the developers
   published at the time. Say where each fact comes from in the code's comments ([Code Style](Code-Style)). If
   something isn't proven, mark it as an inference, never as fact.
2. **No guessing to make something work.** Before changing how the server answers the client, read how the client
   handles that message. A fix is done when the **client's own reaction** shows it (its log lines, the window it
   opens, the state it keeps), not only when the server log looks right.
3. **Live behaviour by default, switchable by owners.** A gameplay setting defaults to what live Landmark did, and
   server owners can turn it off or change it. Extras beyond live are off by default.
4. **No game file changes.** Everything must work on the unchanged client.
5. **Be careful with bandwidth.** Send only what a player needs, to the players who need it, and coalesce updates.
6. **Nothing from the game in the repository.** No game files, no extracted assets, no decompiled code, no text dumps
   from the client, and no copied third-party text (patch notes, forum posts, wiki pages). Describe what you found
   in your own words and cite where it came from.
7. **Tests pass.** `dotnet test LandmarkEmulator.Tests` reports 0 failed and 0 skipped
   ([Server Building](Server-Building#tests)).
8. **Docs follow the code.** A new or changed format, packet or system is explained in `docs/` in the same change.

## How to send a change

1. Fork the repository and make a branch from `main` (the 0.695 server).
2. Build and run the tests ([Server Building](Server-Building)).
3. Keep each pull request to one subject. In the description say what changed, why, the client evidence (function
   addresses in the 0.695 `Landmark64.exe`, data tables, log lines) and how you checked it in game.
4. Commit messages: a short first line saying what changed, then a body with the details and evidence.

## Style guides

- [Code Style](Code-Style)
- [Database Style](Database-Style)
- [Database Migrations](Database-Migrations)
- [Bug Submission](Bug-Submission)

## Licence

LandmarkEmu is licensed under the GNU AGPL v3. By contributing you agree that your contribution is released under the
same licence.
