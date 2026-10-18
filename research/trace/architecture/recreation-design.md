# Recreation design: live's server split next to LandmarkEmu (2026-10-16)

TASKS §7 "Design the recreation (gateway + services per channel) and decide what to build". Live's split comes from
the client traces only (../live-server-architecture.md incl. its "0.695" section, channels.md, gateway-service.md,
world-channel-service.md, social-service.md, terrain-service.md, internal-messages.md, operations.md,
../0695/architecture-leads.md); what is built comes from the git log of LandmarkEmu (branch client-0.695), TASKS §8
and docs/docker.md. Marks: [P] proven by client code / developer post, [I] inference (sources in the files named).
Status words: **verified** = built and seen working in the 0.695 client (rule 4); **built, untested** = code merged,
no client check yet; **to build**.

**§5 is the recommendation for the user to confirm.**

## 1. Per channel

The client sends every game message through its one gateway connection, tagged with a channel (channels.md); the
channel decided the back end [P].

| Channel / opcode | Live back end (traced) | Client sends | LandmarkEmu dev world today | LandmarkEmu split stack (opt-in) |
|---|---|---|---|---|
| 0 Zone | zone server of the player's landscape; it forwarded to the Environment Server (terrain-service.md §0), claim service, mail service, chat routing and the 0.695 Game Finder [P / I] | 1,145 send sites: gameplay, chat lines, mail, terrain, claims, 13 new 0.695 messages | world process (every landscape) | gateway → the back end registered for channel 0 and the player's landscape (routing table by registration, RoutingTable.cs; today the world's one zone process); zone forwards social traffic to `social` |
| 1 World | world-level back end: friends, guilds, ignore, chat membership, trade invitations, commerce / Player Studio, ratings, encounters / lobbies, portraits, locale, relog session 0xF6 [P] | 370 send sites | world process | `worldchannel`: sinks, logout session, trade broker, encounter broker; everything else passed to the zone; zone → `social` for the social families. While `worldchannel` is down the gateway sends channel 1 to the zone (ours; live: L70) |
| 2 Position | gateway: could send PlayerUpdatePositionGatewayToClient itself (no opcode, body identical in both builds) [P] | movement (2 send sites) | world process parses; other players' movement goes as zone packet 0x7F00 on channel 0 | zone computes viewers and sends one 0x16 list per mover and tick (MovementFanout.cs, 2c6c313); the gateway-sourced channel-2 path is not used (deviation, §6) |
| 3 | back end not found yet (L75): all eight WorldEditing 0x10C build-cursor previews [P] | 26 send sites | world process relays (Game/WorldEditing) | same (zone) |
| opcode 9 | the gateway's own commands (CommandList source 3, `/gatewayprofiler`) [P] | gateway commands | gateway library (both modes) | gateway |

## 2. Per service

| Service | Live (traced) | LandmarkEmu | Status |
|---|---|---|---|
| Login | own service, UDP; server list, queue, gateway directory, relog sessions; own failure domain (P6543-P6554) [P] | AuthServer (container `auth`); internal port 20210: TicketConsume, RelogSession (ours, 0xF6 / 0x10001-0x10004) | **verified**: gateway logins through it, hold end → 0xF7 relog session (TASKS §8 B.2). To build: login queue 0xE0 (not needed at our size) |
| Gateway | the client's only connection; routes channels; holds through back-end loss; one connection for every zone change [P]; scope per world or wider: L114 | LandmarkEmulator.GatewayServer (docker/gateway, opt-in) | **verified** (0.695, isolated stack): login, chat, mail, Spire re-zone, world restart, gateway restart, hold + reattach (ChannelIsRoutable 0 → 1, no disconnect), login service, opcode 9 profiler. **Built, untested**: movement 0x16 fan-out (needs two clients); routing table by registration (channel mask + zone ids per back end, per-channel ChannelIsRoutable, ConnectionIsNotRoutable at a login with no zone; 2026-10-16). **To build**: HandOver 0x25 (multi-zone), expected connections (L72), test plan steps 6-8 (gateway-service.md §5), dev world switched to it |
| World server / world channel | world-level back end above the zones: channel 1, kicks, broadcasts, rename, time sync, player transfers (server-only ids, live-server-architecture.md "0.695" §3) [P names / I] | LandmarkEmulator.WorldChannel (docker/worldchannel, opt-in) | **verified**: pass-through, sinks, logout session (FakeGateway test). **Built, untested**: trade broker (367ef81), encounter broker (8259373). **To build**: claim ratings (step 6), teleport queue (L60), lobbies / mini-games (no UI caller, L63) |
| Zone | one per landscape per world, crashed on its own (P0042, P2558, P5984) [P]; pathing and procedural spawns ran here (monitor pages "Zone") [P] | LandmarkEmulator.WorldServer: all landscapes of a world in one process | **verified** as the monolith. **To build (only if load needs it)**: zone per landscape, handover with PlayerTransferServer* / ZoneToZonePacket names (L73, L114) |
| Social (chat routing, friends, ignore, guilds, mail, presence) | shared by all worlds (P1679, P4905, P4907, P5841; wiki Chat) [P / W] | LandmarkEmulator.SocialServer + lmemu_social (compose profile `social` + SOCIAL_ADDRESS) | **verified** (isolated stack, 0.695 client + scripted second world): presence, friends, ignore, guilds, routed chat / rooms, mail, cross-world cases, down behaviour, restart. **To build**: in-client friend add check (needs input); **groups across worlds** (P4906; Groups stay zone-side today, social-service.md §5.1) |
| Game Finder (0.695 matchmaking) | shared, "work between worlds" (P6551); request on channel 0 [P] | module of the social service (MatchmakingService, 6f544b3) | **built, untested**. **To build**: moving a matched player to another world (L31 + L114) |
| Environment Server (voxel DB + jobs) | one service behind the zones: layers, heal queue, construction, veins, ruin layers, ownership (terrain-service.md §0) [P] | LandmarkEmulator.EnvServer.Core library inside the world process; persisted heal queue | **verified** in-process (dig / heal / re-zones on 0.695). **To build**: `envserver` container (decision first: zone-side mirror vs ApplyDiff in the service, TASKS B.3), job counters, ruin-layer jobs |
| Claim (build site) service | own service feeding zones (ZoneAddClaim / ZoneAddAllClaims, "Build Site Server is unavailable") [P] | inside the world process | **not planned** until several zone processes share a world (§5 item 9) |
| Commerce (Player Studio, Marketplace, wallet, contests, portraits, Showcase) | account-wide Daybreak platform [I] | interface split only (ICommerceService, 6d7dbbf); runs in the world process (Commerce.cs, Contests.cs, Portraits.cs, WebPages) | **to build**: `commerce` container (world-channel-service.md §8 step 8) |
| Stats / leaderboards | own service (ProfileStats Zone* ids) [P names] | sink | open (L59: no UI caller) |
| Admin | admin player flag in SendSelf; admin Command subs; world-level admin ids [P] | flag for WorldServer.json Admins (5aeed5d), Game/AdminCommands.cs (0eb4af7); web admin page (B.7, admin-pages.md, another agent) | **built, untested** (flag, commands); page in progress |
| Operations | Mon + Sat 3 a.m. restarts (P5902), bounces for performance (P5906), all worlds down for updates (P6534) [P] | RestartSchedule, watchdog + healthcheck, Play Mode kept across restarts (B.0) | **verified** |
| Events world | a separate world in the teleport menu, island switched at a bounce (P6529, P6560) [P] | second world entry {3, 567016} in the same world process (d644089) | **built, untested**; live's id L109 |

### Deployment today

- **Dev world** (docker/worlds/dev.env, .env: COMPOSE_PROFILES and SOCIAL_ADDRESS empty): `auth`, `db`, `web` and one
  world process that is gateway + zone + world channel + social + Environment Server + commerce.
- **Split stack**: `social`, `gateway-<world>`, `worldchannel-<world>` exist as opt-in containers (docs/docker.md
  "Social service", "Gateway in its own container", "World channel in its own container") and have only run in
  isolated test stacks (re/test/socialtest, FakeWorld, FakeGateway). So none of the Phase B split reaches the dev world
  the user plays yet.

## 3. Verified, built-untested, to build (one list)

- **Verified**: transport library; restarts / watchdog / healthcheck; social service (all six steps, cross-world);
  gateway process, hold + reattach, login service, opcode 9 + profiler; dynamic commands 0x45-0x47; world-channel
  pass-through, sinks, logout session; Environment Server library + heal queue (in-process).
- **Built, untested**: movement 0x16 fan-out; gateway routing table by registration; trade broker; encounter broker (duels); cross-world Game Finder counts;
  admin flag + admin commands; Events world; procedural placement (default off).
- **To build**: dev world on the split layout; world-channel ratings; `commerce`
  container; groups across worlds; cross-world travel from the teleport window (L114 first); admin web page (B.7,
  in progress elsewhere); `envserver` container; zone per landscape + handover (L73, L114); claim service (not planned).

## 4. What live had that the client still hides (do not build until traced)

L114 (how a world change reached another world's servers), L73 (zone-process handover layouts), L70 (gateway on
back-end loss), L75 (channel 3 owner), L60 (teleport queue), L31 (Game Finder join), L48 (internal layouts — ours
until a capture), L115 (world list history).

## 5. Decision: what to build next — recommendation for the user to confirm

Criteria: **risk** (to the dev world and to the client), **bandwidth** (rule 8), **what the user gets**,
dependencies, and rule 1 (trace before build).

| # | Step | Why now / why here | Risk | Bandwidth | User gets |
|---|---|---|---|---|---|
| 1 | **Verify the split stack, then make it the dev layout.** Run gateway-service.md §5 steps 6-8 (zone restart with the 0.695 client connected, edge cases, profiler before / after) with `gateway` + `worldchannel` + `social` together; check the built-untested pieces (fan-out, trade broker, duels, Game Finder) with a scripted second player (FakeGateway / FakeWorld; two 0.695 clients don't fit in RAM, memory pc-resource-limits). Then, with the user told, switch the dev world to that layout (one compose profile). | All Phase B work so far runs only in throwaway stacks; rule 4 ticks nothing unverified; every later service plugs into the gateway | low (no new features; isolated stack first, backup before the switch) | measured here: one 0x16 per broadcast, no rise in bytes / player / min against the monolith | zone restarts and crashes without being dropped (live: P0042, P5984), cross-world friends / guilds / mail / chat on the world they play |
| 2 | **Gateway routing table by registration** (channel mask + served zone ids per back end, per-connection zone → back end, ConnectionIsNotRoutable when none) | small; removes hard-coded zone / world-channel slots; prerequisite for 6-8 | low | none | nothing visible; makes later steps safe |
| 3 | **World-channel ratings (step 6), then the `commerce` container (step 8)**: Player Studio, Marketplace, contests, portraits, Showcase shared by all worlds | the last cross-world service players notice (TASKS §7 "Remains: Player Studio"); spec complete (world-channel-service.md §6.3, §8) | medium: data migration with world ids (backup first), product ids < 2^32, split requests (Create on Zone, store on World) | small (UI request / reply) | a design listed on one world reviewed and taken on another, one contest list |
| 4 | **Groups across worlds** (P4906): trace GroupsBase on 0.695 first (rule: check 0.695 before porting), then group membership in the social service, party chat routed there | live had it; social service already has presence | low-medium | small | groups that survive a world change |
| 5 | **Cross-world travel** from the teleport window: trace L114 first; build only from what it proves (Locations listing every running world from the login service's server list, TeleportRequest to another world) | live's "free travel between servers" (wiki, P4400, P5734); today a world change needs character select | high until L114 is answered | small (one transfer per trip) | live-like world hopping |
| 6 | **Admin web page** (B.7) | in progress by another agent (admin-pages.md); its Environment Server panel waits for 7 | low | admin only | live monitor data in a browser |
| 7 | **`envserver` container** (B.3): first a design note deciding zone-side mirror vs ApplyDiff in the service (terrain-service.md §5.3), then build | highest risk: Building reads voxels synchronously, 8.5 MB manifests and every dig gain an internal hop; on one machine players see no difference; the in-process library already follows live's model (layers, persisted heals) | high | heavy internal traffic (cells, manifests); must stay off the client path | job counters / ruin layers for admin; the base for several zone processes |
| 8 | **Zone per landscape + handover** (B.6) | only if one process can't hold a world's players; names known (PlayerTransferServer*, ZoneToZonePacket), layouts ours | high (L73: same-landscape re-zone, entity teardown) | more internal traffic | scale only |
| 9 | Claim service as its own process | live fed several zones with ZoneAddClaim / ZoneAddAllClaims; with one zone per world there is nothing to feed | — | — | nothing until 8 |

**Recommended next step: #1, then #2 and #3.** Reasons: #1 turns finished work into something the user actually runs
and proves it by the client's reaction; #2 is cheap and unblocks every later back end; #3 completes the cross-world
services, which are what players of live noticed (shared Marketplace). The Environment Server container (#7) and
zone splitting (#8) give the user nothing visible on one machine and carry the most risk and internal bandwidth, so
they wait until the rest is stable, and #5 waits for its trace (L114). The Events world stays in the world process
(it is switched by EVENT_ISLAND at start-up, as live switched it at a bounce) until L114 shows that live ran it as its
own server set.

TASKS §8 items: #1 + #2 = B.5 (with B.2's routing table), #3 = B.4 steps 6 / 8, #4 = B.9, #5 = B.10, #6 = B.7,
#7 = B.3, #8 + #9 = B.6.

## 6. Deliberate differences from live (recorded)

| Ours | Live | Why / lead |
|---|---|---|
| Channel 1 goes to the zone while `worldchannel` is down | not known whether held or dropped | keeps the zone's channel-1 handlers serving during the move (L70) |
| One gateway per world | gateway scope not proven | deployment choice (L114) |
| Others' movement: zone packet 0x7F00 in one 0x16 list per mover | gateway could send channel-2 positions itself | zone knows ranges and landscapes today; the channel-2 body has no zone opcode, so the gateway path would save a few bytes per update [I] — revisit with #2 |
| Game Finder in the social service | host not known | needs cross-world presence (world-channel-service.md §6.8) |
| Events world in the main world process | its own world | L109 / L114 |
| Internal message layouts | not in the client | names / ids from the client (internal-messages.md), layouts ours (L48) |
