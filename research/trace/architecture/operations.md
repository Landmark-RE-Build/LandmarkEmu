# Operations: scheduled restarts, health checks, saving, server-side admin data (Phase B items 7 and 8)

Agent register #16 (coordinator). The Environment Server, first planned here, turned out to be the voxel job back end
and belongs to terrain-service.md (#14): see "Environment Server" below for the evidence handed over.

## Existing research used

- trace/live-server-architecture.md (developer posts on restarts, bounces, hot data pushes).
- trace/families/004C_PacketClientServerShuttingDown.md, 005F_PacketWorldShutdownNotice.md (generated family traces).
- trace/architecture/admin-monitor.md (monitor pages, requests), trace/0695/architecture-leads.md L3 / L4 (the retail
  client can't open the paged monitor), the user's decision of 2026-10-15 (admin data server-side).
- Emulator: Game/Shutdown.cs, Game/ServerHeartbeat.cs, WorldSession position saves, docker/*/docker-compose.yml,
  LandmarkEmu/docs/docker.md.

## What live did (evidence)

| Fact | Evidence |
|---|---|
| Scheduled restarts **every Monday and Saturday at 3 a.m. Pacific**, "expected down time under an hour", from 25 Jun 2016 | dev post "Server restarts Saturdays & Mondays", 24 Jun 2016 |
| Earlier: a nightly 5 a.m. "server bounce" (2015), then "regularly scheduled overnight bounce on Saturday morning" (2016) | P2523, P2678, P4833, P4837 |
| Restarts were how most server fixes and data changes shipped ("requires a server restart"); some data was pushed hot ("did not require server downtime") | P4783, Aug 2016 posts |
| Rollbacks on restart were a bug fixed in Feb 2014: state lived in the servers and was saved on a schedule | P0133 |
| A zone could crash on its own and lose recent unsaved changes (claims made just before) | P0042 |
| The client is warned before a stop: **WorldShutdownNotice 0x5F** {u32 seconds, str message, u32} → "ServerShutdownMessageWithTimeRemaining" (h / m / s) above 5 s, "ServerImmediateShutdownMessage" at ≤ 5 s, plus "Shutdown Reason: %s" | reader FUN_142b8b650, handler FUN_142c4d020 (family 005F) |
| At the stop the client can be told **PacketClientServerShuttingDown 0x4C** (no body) → "The server has shutdown and logged you out." | zone dispatcher case 0x21 (family 004C). When live sent it is not found yet — sending it at the final stop matches its text (inference) |
| A back end going down shows per channel (ChannelIsRoutable / ConnectionIsNotRoutable); login refuses a world that's down ("Game Server not found.") | channels.md; ServerHeartbeat |

## Emulator today

- **Shutdown.cs**: dev command `shutdown <seconds> [message]` with warnings at 3600 … 5 s, final notice, clean stop;
  `shutdown cancel`; Ctrl+C sends the immediate notice; the login heartbeat is cleared while a shutdown is pending.
  Not sent: PacketClientServerShuttingDown 0x4C.
- **No schedule**: restarts only by hand.
- **Docker**: `restart: unless-stopped` on the world, auth server, website and database; a healthcheck only on the
  database (docker/auth/docker-compose.yml:29). The world has none, so a hung (not crashed) world is never restarted.
- **Saving**: most game state is written straight through to the database as it changes; positions every 30 s
  (WorldSession PositionSaveInterval) and on logout. Known memory-only state that a restart loses: Play Mode per site
  (Claims.cs "Not persisted", TASKS 6.56). Transient by nature (lost on restart as on live): groups, trades, site-game
  rounds, creature spawns.

## Spec

1. **Restart schedule** — built 2026-10-15 (Game/RestartSchedule.cs, RESTART_SCHEDULE / RESTART_TIME_ZONE) (WorldServer.json / `.env`): `RestartSchedule = "Mon,Sat 03:00"`, `RestartTimeZone =
   "America/Los_Angeles"` (live's), `RestartWarningSeconds = 3600`, `RestartMessage`. A scheduler starts
   `Shutdown.Schedule(warning, message)` so the warning steps run as today; the container's `restart: unless-stopped`
   brings the world back. Empty schedule = off (the default for home servers; documented in docs/docker.md).
2. **Final stop**: unchanged — players are sent to character select with a fresh login session (Shutdown.cs, tested
   2026-10-14) so the client doesn't quit. PacketClientServerShuttingDown (0x4C, "logged you out") is not sent: when
   live used it is not found yet, and it would undo the character-select hand-over (open lead).
3. **Health checks**: each service answers a small health probe (a TCP port or HTTP `/health` on its internal
   network) that fails when its main loop stalls (last tick older than N seconds), and compose `healthcheck:` uses it,
   so a hung service is restarted, not just a crashed one. World: tick age; auth: accepting logins; later every Phase B
   service the same way.
4. **Save on schedule**: keep write-through; add a periodic flush (positions already) and persist the remaining
   memory-only state that live kept across restarts — Play Mode per site (6.56). Re-audit after each Phase B service
   lands (each spec lists its memory-only state).
5. **Server-side admin page** (user decision 2026-10-15): one web page (the account website or a small admin
   service) showing the live monitor data per service: players / zones / tick times (world), gateway profiler per
   message (count, bytes, recipients — admin page 24's fields), Environment Server job summary and ruin layers (#14's
   service, fields below), instanced games, procedural spawns, pathing. Admin login = the existing admin accounts.
   The client replies for pages 28 / 29 (0x10A, 0x10D sub 2) stay a Phase B item 7 sub-task.

## Environment Server (handed to #14, terrain-service.md)

The client's Environment Server page (0.695 FUN_14320ea00) prints "Job Summary": 19 job types (names from
FUN_143c74ef0) with Active / Pending counts and "Total Jobs": Cell Poi Lod, Cell Permanent Lod, Cell Timed Persistent
Lod, Cell Transient Lod, Cell Temporary Modifications, Cell Generation, Cell Composite Update Lod, Construction Apply
Diff Cell, Construction Template, Construction Undo Redo, Construction Gather Ownership Info, Construction Paste
Template/Clipboard, Construction Paste Template/Clipboard Off-grid, Construction Clear Exported Template, Construction
Area Iterator, Vein Spawn Vein, Vein Validate Cells, Misc Cell Ownership Reusable, Misc Manifest Compression; then
"Ruin Layer Info" per layer: 16-byte records {u32 Desired (label at 0x14472a400), u32 Active, u32 Failed, i32 Last ID}
printed as "Layer n: Desired / Active / Failed / Last ID" — it kept a desired number of ruins per ruin layer, spawned
them and counted failures. So the Environment Server was the voxel job back end (cell LODs per layer, construction,
veins, ownership) and placed the cavern ruins.

## Build / test plan

1. Scheduler + 0x4C at stop in the world server; test in the dev world with a schedule two minutes ahead: warnings in
   the 0.589 and 0.695 test clients' chat / screen text, the stop, the container coming back, players able to log in.
2. Health endpoint + compose healthcheck; test by pausing the world's tick (dev command) and watching Docker restart it.
3. Play Mode persistence (6.56) with a restart test.
4. Admin page after the services it shows exist (Phase B order).

## Open

- When live sent 0x4C (inference only) — add to OPEN-LEADS if a source turns up.
