# Bug hunt: services, storage, web, launchers, Docker (2026-10-18)

Branch `local/bugsvc` (worktree `wt-bugsvc`, from client-0.695 at 922a3c8). Scope: CommerceServer, EnvServer +
EnvServer.Core, WorldChannel, WebAPI, the world's web pages (Game/WebPages.cs, admin pages, Showcase API), Launcher /
LaunchPad, Database*, docker/ and compose, plus re/server/deploy.sh and start.ps1 (outside the repo: report only).
Found by reading the code. Each bug was confirmed by tracing the code path. None has been tested in game yet.

The bugs are ranked by impact. The solution builds with 0 errors and the same 23 distinct warnings as before; none are
new in the touched files, and one (CS0649 in the old Launcher) is gone. `dotnet test LandmarkEmulator.sln`: 1344
total, 1310 passed, 0 failed, 34 skipped. That includes the 5 new TerrainStoreTests.

| # | Severity | Bug | Commit |
|---|---|---|---|
| 1 | High (data loss) | **A failed terrain save lost the edits for good.** `TerrainEdits.Flush` marked every dirty cell clean *before* `CellPack.Append` wrote it. If the append threw (disk full; the dev PC's disk has filled twice), the cells stayed clean, so they were never written again unless edited again. After 10 idle minutes `SpillIdle` dropped their in-memory bytes, because `OnDisk` still pointed at the older record, so the edit was gone from memory too. Second problem: a failed append could leave a partial record at the end of `cells.pack`. The next append went after it. At load, the partial record's length swallowed the records behind it, and `Load` cuts the file at the first unreadable record, so every later save of that landscape was lost. `VoxelOwnership.Flush` had the same clean-before-write pattern and rewrote each `.own` file in place, so a crash part-way left a cut-off file that `Load` skips (builder ownership lost). Fix: on a failed append the cells are marked dirty again, the error is logged once a minute and the save is retried after 5 s. A failed append is cut back to where it started; if that also fails, it is cut before the next append. Ownership files are now written to `.tmp` and moved into place, and also retried. | dfc649a, 043db3c |
| 2 | High (data loss) | **Docker killed the world 10 s into its graceful stop.** No compose file set `stop_grace_period`, and Docker's default is 10 s. `HostedService.StopAsync` waits 1 s, then up to 60 s (at least 5 s) for players to go to character select. Only then does it close the sessions (saving each player still connected), write the terrain store's last flush and the gathering state. On `docker compose down`, `stop` or `up --build` (and `start.ps1 -Build`), anyone still connected after about 9 s, typically a client still loading, lost what they had done since their last save, and the last gathering changes were lost. The Environment Server's stop joins its work thread for up to 30 s before its final flush, and 10 s cut that short too. Fix: world 90 s, envserver 45 s. (deploy.sh already stops the world through the dev command `shutdown`.) | f44c8a3 |
| 3 | High (crash / DoS by any player) | **One malformed build edit could allocate about 4 GB.** `BlockData.Deserialize` decodes the client's ApplyDiff and paste blobs (Building.cs, BuildingClipboard.cs, and EnvHub's ApplyDiff). It allocated the palette / material `ushort[]` at whatever count the blob named, before reading it. A count near 2^31 allocated and zeroed about 4 GB before the read failed. That means an OutOfMemoryException at best, and at worst the world or envserver container killed for its memory. The blob is at most 4 MB (`TerrainStore.Unpack`), so a count beyond the bytes left is now refused with InvalidDataException, which the callers already answer with "edit failed". Tests: `TerrainStoreTests`. | 030d423 |
| 4 | High (unauthenticated memory DoS) | **Web pages: portrait uploads could pin gigabytes.** `WebPages.Serve` allocates the body buffer from the Content-Length header before any of the body arrives: up to 6 MB for `/portrait/upload`, and the request may take its 30 s deadline. The port is public in Docker (`WebPagesHost 0.0.0.0`). Any number of connections could each send just that header and hold 6 MB for 30 s, so a thousand connections held 6 GB. Nothing limited how many connections were served at once. Fix: at most 4 uploads are read at a time (others get 503; a client posts one portrait when it takes one), and at most 256 connections are served at once (extra ones are closed on accept). | 30431ee |
| 5 | Medium-High (unauthenticated DoS) | **One address could block every login.** `/srp/start` needs no account and holds each handshake for 2 minutes. The table answers 503 to *everyone* beyond 10,000 entries (listed as a follow-up in bughunt-social). Also, the failure counters (per name and per address) and the registration counters were never pruned, so random names grew them without bound. Fix: at most 16 logins in progress per address (429 beyond that), and expired counters are swept once a minute. All per-address limits count IPv6 by /64, since one host normally holds a whole /64. | 43b8807 |
| 6 | Medium | **Two registrations of one name at once created two accounts.** `AccountExists` and the insert were separate steps, and the `account.username` index is not unique. Every lookup by name (`GetAccountByUsername` in /srp/start, /srp/finish and /authenticate, `AccountExists`, `DeleteAccount`) uses `SingleOrDefault`, which then throws for that name, so the account could never log in again (HTTP 500). Fix: `AuthDatabase.CreateAccount` does the check and the insert under one process-wide lock (every creation path runs in the auth process), and the race loser gets 409. A unique index needs a migration that would fail on a database already holding such a pair (see follow-ups). | 4aba6a0 |
| 7 | Medium | **Commerce following digest: one failing realm starved the rest, and a retry mailed twice.** `FollowService.Tick` *returned* at the first realm whose run threw, so the realms listed after it got no digest while it kept failing. `Run` also stored and sent the mails one by one and only then marked the slot finished. A failure part-way left the first mails sent with the slot unfinished, and the retry a minute later sent them all again. Fix: the loop continues past a failing realm, and the day is marked done only when every realm succeeded. A run's mails and its "finished" mark are one transaction (`CommerceStore.AddDigestRunMails`), and the mails are sent after it commits. | a9b4ce3 |
| 8 | Medium (performance) | **Every DbContext opened an extra, unpooled database connection.** `Extensions.UseConfiguration` called `ServerVersion.AutoDetect` for every context it configured. Pomelo 9.0.0's AutoDetect opens its own connection with `Pooling=false`; I confirmed this from the IL (`MySqlConnectionStringBuilder.set_Pooling` before `Open`). So each `NewContext()` in the auth, world, gateway and social code paid a fresh TCP connect and MySQL login before its first query. Fix: the version is detected once per connection string and cached. A failed detection is not cached. | 68265f2 |
| 9 | Low | **Old WPF Launcher.** It put the user name and password into the `/authenticate` query unescaped, so passwords containing `&`, `#`, `+` or `%` failed. An unreachable server threw out of an `async void` handler, which closes the app. `sessionExpires` was never set. All three are fixed. LaunchPad is not affected. | 4470222 |

## Checked, no bug found

- Commerce: product create / duplicate check, owner checks on remove / update, review voting (one vote per player,
  never on your own, staff queue), take-design (zone side re-checks Unlocks on the world thread, so a double click
  cannot buy twice), payout in-flight guard + world-side claim (Unlocks kind 32), contest votes, portrait size / CRC
  check, import paths (`Path.GetFileName`, SafeName for SQL schema names). All SQL is parameterised; the only literals
  are numbers or SafeName-checked schema names.
- EnvHub: single work thread, keep-out boxes before heals, stamp file names reduced to `Path.GetFileName`.
- WorldChannel: trade broker states and zone-side `BrokerStart` checks, disconnect paths end brokered trades
  (`Trades.Forget` reports to the broker), rating broker (INSERT IGNORE votes).
- LaunchPad: pack paths go through `SafeRelative` and must be files of Daybreak's lists, the whole pack is checked
  before anything changes, the backup doubles as a journal, a server's pack sha256 is checked before it becomes a file
  name, WebView navigation is locked to `https://launchpad.local/`, server text goes in through `textContent`.
- WebAPI PatchHost: pack ids regex-checked, paths checked, owner-supplied files only.
- Web pages: admin pages / APIs check `Admin.IsAdmin` with the session token, static files are regex-matched names,
  store purchases run on the world thread with clamped counts, gallery comment owner lookups are locked.
- Docker: `.dockerignore` keeps `.env`, `server-data/`, `gameData/` out of the build context; runtime images only copy
  `/out/*`; the database is published on 127.0.0.1 only; internal ports (20200, 20210, 20300, 20400, 20500, zone
  internal) are `expose`d only; the website is on its own network and can reach only the account API.

## Seen, not fixed (follow-ups)

- **Commerce payout dedup key survives a commerce database reset.** The world claims `(creator, Unlocks kind 32,
  (uint)payoutId)` in the character database before mailing. If lmemu_commerce is recreated or re-imported while the
  character database keeps those rows, new payouts restart at id 1 and real shares are logged as "already mailed" and
  never paid. Ids above 2^32 also fold. A service-instance id in the key, or clearing kind 32 when the commerce
  database is new, would fix it.
- **Commerce result mails have no in-flight guard** (payouts and digest mails do). A result mail sent at decision time
  is sent again by `SendPendingMail` when another world of the realm links before the first one confirms, so the
  owner gets a duplicate notification. It is text only, with no items.
- **Commerce `PortraitService.Upload` stores under `upload.World`**, the world id the message names, not the
  linked world's id. With internal ports unauthenticated (bughunt-social follow-up), one world link could overwrite
  another world's portraits.
- **HealQueue.TakeDue** removes entries before `Regenerate` heals them. An exception mid-heal loses them in memory until
  the next restart (the log file still has them).
- **In-process ShowcaseFollow** keeps a run's mails only in memory until sent. If the world restarts mid-send, the run
  is taken over later and the mails already sent go out again.
- **EncounterBroker.Start** moves a participant out of an old encounter but leaves that encounter in the table, empty
  or with its other participant never told. `Answer` removes a challenge even when the inviter does not match.
- **ChannelRelay**: an old zone link's late `Closed` runs `TradeBroker/EncounterBroker/RatingBroker.ZoneLost`
  unconditionally, so it can clear what a zone that already reconnected set up.
- **Web pages**: the 256-connection cap is global. An idle flood can still make the pages unavailable for its 30 s.
  A per-address cap would need the same trusted-proxy logic as the account API.
- **AuthDatabase**: add a unique index on `account.username` once a check shows no duplicates exist. Session keys stay
  valid and reusable for 30 minutes.
- **LaunchPad "remember me"** stores the SRP key (PBKDF2 of the password, which is enough to log in to that server) in
  plain text in `%APPDATA%\LandmarkEmu\launchpad.json`. DPAPI (`ProtectedData`, CurrentUser) would protect it.
- **LaunchPad PatchFile.Reader** allocates an entry's `Size` from the index before it is compared with the manifest.
  Only local packs are affected (server patches are switched off).
- **re/server/deploy.sh** (report only):
  - Backup failures are silent: `> file 2>/dev/null || true` continues to the deploy and its migrations with an empty
    or partial dump. Check mariadb-dump's exit code and the `-- Dump completed` trailer, and stop when either fails.
  - The root password goes on the mariadb-dump command line inside the container. Step 3 already uses `MYSQL_PWD`;
    the backup should too.
  - With `set -e`, a failed image build ends the script before its `build X: N` line, so the failure is only in the
    build log.
  - After the 300 s wait for a graceful stop it runs `docker rm -f`, which kills a world that is still saving.
- **start.ps1** (report only):
  - `-Build` builds the images from the working tree, uncommitted changes included. That goes against the rule to
    build dev only from a clean HEAD export; deploy.sh does it correctly.
  - The wait for `"up":true` passes when *any* world is up, for example the two-world stack's world 93.
  - The `$GamePath` default comment names the 0.589 Steam install, but 0.695 tests must use `<0.695 test copy>`.

## In-game checks for the coordinator

Rebuild and restart the dev stack (deploy.sh): auth, world, social and gateway images; also envserver / commerce if
those profiles are used. This restarts the dev world.

1. Build and dig a little, restart the world, and check that the edits are still there (normal save path, fix 1). The
   world log has no "could not be saved" lines.
2. Log in with LaunchPad (SRP) and sign up a new account on the website. Both work as before. Optional: send two
   `/srp/register` requests for the same new name at once; one gets 200 and the other 409 (fix 6).
3. In game, open the Showcase (store and gallery pages load), take a portrait, and check that the upload is stored
   (log `Portrait upload (... bytes): stored`, fix 4).
4. With a player online (or loading), run `docker compose ... stop world`. The container takes up to 90 s, and the
   log ends with `Service Ended!` instead of being cut off (fix 2).
5. Build with the brushes and paste a copied selection. Edits are accepted as before (fix 3 only refuses malformed
   blobs).
6. Optional, commerce service only: `@TestPlayer followdigest` still runs a digest (fix 7).

No settings, Docker environment variables or dev commands were added. `stop_grace_period` is fixed in the compose
files, not an env var.
