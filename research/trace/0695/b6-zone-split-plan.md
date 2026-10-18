# B.6 — zone split, only when needed (plan, 2026-10-18)

Status: **planned, not built.** One world process runs every landscape today and that is enough for a private
server. This is the plan for when one machine can't keep up. Agreed with the user as "planning ideas" (2026-10-18).

## What live had (and what we don't know)

- [P] The client has the packets for handing a player from one zone server to another inside one world:
  PlayerTransferServerRequest / Reply 0x0B / 0x0C, TransferPlayerToServer 0x09, ZoneToZonePacket 0x18
  (internal-messages.md §1), and the gateway routes per (world, channel) with back ends registering their zones.
- [P] Live had a gateway in front, world-level services (chat, friends, guilds, groups, store) apart from the zones.
- Not known: how many zone machines a live world used, and whether landscapes were assigned to machines fixed or by
  load. No capture or dev post says. Every landscape existed once per world, in one shared database — a split divides
  the work, it never copies a world.

## What already exists

- Gateway in front of the world; clients never see which machine holds them.
- Handover between processes (HandOver / AttachConnection; used by cross-world cavern trips, 7e9af69).
- Gateway routing by registration (channel mask + zone ids per back end, B.2).
- Shared social service (chat, friends, guilds, groups, mail) and commerce service (store, Player Studio).
- One database for every process; the Environment Server can own terrain (ENVSVC).
- World clock follows UTC × 8 (398636f), so two processes agree on time of day.

## Step 1 — fixed split by configuration (the minimum)

1. Setting `ZONE_LANDSCAPES` (e.g. `391-402` / `403-416,464-468`; empty = all, as today). A world process loads only
   those landscapes (terrain, caves, outposts, props, creatures, Spire, claims cache) and registers them with the
   gateway.
2. Gateway: route a client to the process that registered its landscape; travel to a landscape owned by another
   process = handover (the cross-world trip path, same world id) instead of a re-zone.
3. Make the "looks across landscapes" places ask the database / a service instead of the local process:
   Site Management list (sites on other landscapes), Landscapes window counts and capacity, teleport to friend and
   friend positions, cavern trips to another landscape, the Travel window, admin pages, Game Finder, warp network
   between landscapes (if any), daily goals / achievements that count across landscapes, prop / claim lookups by id.
4. Docker: a second world container as a compose profile (off by default), same world key, its own landscape list.
5. Tests: two processes with split landscapes, travel both ways, a player's site on the other machine, friends
   across, group across, a process restart while players are on the other one.

Estimate: one or two days of agent work and harness testing.

## Step 2 — spill-over on demand

- A watcher (gateway or an admin script) tracks players and tick time per process (gateway profiler already
  measures bytes / players).
- Over a threshold: start the second process with the quietest **empty** landscapes (saved and closed on the first,
  opened on the second; nobody moves mid-play). Under the threshold again: empty them back and stop it.

## Step 3 — move a busy landscape live (only with real crowds)

- Hand every player on a landscape to the new process at once (batch handover with a hold), then move it. Highest risk
  for data; only if steps 1-2 are not enough.

## Open questions before building

- Exact live handover packets' layouts (L48 / L73 / L114 in OPEN-LEADS).
- Whether any client feature assumes one zone server per world (e.g. zone population packets, Game Finder).
- Claim service: today claims are per landscape in each process — a separate claim service only if several
  processes must see one site (ZoneAddClaim / ZoneAddAllClaims feed several zones).
