# FAQ

## Playing

### Do I need to own the game?

Yes. You need **Landmark in your Steam library**, installed by Steam. LandmarkEmu ships no game files: the servers
read data tables from the host's own copy, and LaunchPad only works on a complete, unchanged Steam install (it checks
every file against Steam's own list of the install before it changes anything).

### Which version of the game do I need?

The servers speak the last client build, **0.695.1.132647** (February 2017). Older clients are refused.

Steam still installs the older build **0.589.1.128895** (June 2016). [LaunchPad](LaunchPad) installs the update from
0.589 to 0.695 on top of your Steam copy, from an update file (`.lmpatch`) you have, or from the server you play on
(it always asks first). It never downloads the game itself, and it only accepts Daybreak's genuine 0.695 files.

### Can I start the game from Steam?

Not with Steam's Play button: Steam stops at "getting legacy key", because the old Daybreak key service is gone.
Start `LaunchPad.exe` from the Landmark folder, from a desktop shortcut, or add it to Steam as a non-Steam game. See the
[Client Connection Guide](Client-Connection-Guide).

### Does it cost anything?

No. The project is free and open source. In game, the store (the Showcase) sells live's 2016 items for **Lumens**,
the currency you earn by playing; a server owner can also make everything free. Nothing is ever paid with real money.

### Is there a public server?

The project doesn't run one, and it is in heavy development. Anyone can host a server for themselves and their
friends ([Server Guide](Server-Guide)); ask the person who runs it for its address.

### Can I keep my builds?

On a LandmarkEmu server, yes: your characters, build sites, designs and items are kept in that server's database and
world folder between restarts, for as long as its owner keeps them (owners: back up `server-data` and the database,
see [Server Installation](Server-Installation)).

Builds from the live game can't be brought over: live's servers and their data are gone, and the client never had
them (designs and sites were stored on Daybreak's servers only).

### Is it safe?

- **Your password never leaves your PC.** LaunchPad and the sign-up website log in with SRP-6a: the server only
  stores a verifier, never the password. "Remember me" keeps a key that works for that account on that server only.
- **Only genuine game files get into your game.** Every file the 0.695 update writes is checked against the SHA-256 of
  Daybreak's genuine file, which is built into LaunchPad, before it replaces anything. A server can't change that list,
  and nothing it sends is trusted for coming from that server. Every change is backed up and can be undone.
- **LaunchPad only starts a genuine game exe** (Steam's 0.589 one, or a known 0.695 build's).
- Get `LaunchPad.exe` from the project's **Releases** only, never from a server owner: a changed LaunchPad could
  trust anything.

### Can I play on Linux or a Mac?

LaunchPad is a Windows program (WPF and WebView2), and so is the game. Running them under Wine or Proton has not been
tested. The **servers** run anywhere Docker runs, including Linux ([Server Installation on Linux](Server-Installation-Linux)).

### The game froze, crashed or shows an error. What now?

LaunchPad comes back to the front when the game closes, and tells a crash from a normal exit. The game's own log
files are in the `Logs` folder of your Landmark folder. See [Bug Submission](Bug-Submission) for what to include in a
report.

## Hosting

### Where are the settings?

- **Server:** one file, `.env`, in the project folder (made from `.env.example`, where every setting is explained). Run
  `docker compose up -d` after changing it. All settings: [Settings Reference](Settings-Reference).
- **LaunchPad:** `%AppData%\LandmarkEmu\launchpad.json` (servers, saved logins, options). Its records of what it
  installed into your game are in the `LaunchPadEmu` folder inside your Landmark folder.

### Does the game play like live Landmark did?

That's the aim. Every gameplay setting defaults to how live Landmark behaved (the note next to each setting in
`.env.example` names the source), and each one can be changed by the server owner. Extras that go beyond live are off
by default. What is built and what is still open: [Project Status](Project-Status).

### What does the server need from my game?

The data tables and the English text from your Landmark folder, read-only, plus an optional terrain-heights file from
the project's Releases. See [Game Data](Game-Data).

### Can I run more than one world?

Yes: each world is its own container with its own build sites, props and terrain, and characters are shared between
them. See [Server Installation](Server-Installation#more-worlds).

### How do I become an admin on my own server?

Put `ADMIN_ACCOUNT=account:<your account name>` in `.env` and run `docker compose up -d`. In game, type `/admin`. See
[Account Creation](Account-Creation#admins).
