# Bug hunt: sessions, networking, social, internal links, admin, web (2026-10-18)

Branch `local/bugsocial` (worktree `wt-bugsocial`, from client-0.695 at 9280c50). Found by reading the code. Each
fix was checked by tracing the code path; none has been tested in game yet. Ranked by impact. The whole solution
builds with 0 errors and the same 25 warnings as before (none new).

| # | Severity | Bug | Commit |
|---|---|---|---|
| 1 | Critical | **One malformed UDP datagram crashes the login or world server.** `Connection.StartReceiving` caught only `SocketException`. Anything thrown while handling a datagram escaped the UDP thread, and an unhandled exception on any .NET thread ends the process. A 1-byte datagram is enough: `ProtocolPacket` reads a u16 opcode and `Buffer.BlockCopy` throws. So is a truncated SessionRequest, because `message.Read` ran outside the handler's try. Fix: the receive loop catches per datagram, datagrams under 2 bytes are dropped, and `Read` runs inside a try. | ecf28ab |
| 2 | Critical (security) | **Any player could become admin.** `Admin.IsAdmin` accepted an entry if it matched either the account name or the character name, and players choose both. If `ADMIN_ACCOUNT` names the admin's *character*, anyone can register an account with that name (`/srp/register`). If it names the *account*, anyone can create a character with that name (non-strict names). Either way they got every dev command (`Admin.Run`, AdminCommands, the /admin web menu). Fix: an entry is **contested**, and grants admin to nobody, when the matched name is also taken in the other namespace by another account. The world logs an error once. Lookups are cached for 1 min. docs/admin.md updated. | ecd98cc |
| 3 | High | **After any lost or reordered packet, client packets are handled late for the rest of the session.** `DataStreamInput.ParseDataFragments` parsed only one ready packet per in-order arrival. When a gap filled, the packets held behind it waited for later arrivals. From then on, each client packet was handled one packet late, and one more late after each further loss. The last packet of a burst (ClientIsReady, logout, a chat line) was not handled at all until the client sent something else, which is a possible stuck-loading path. Fix: parse everything that is contiguous. | 8839a08 |
| 4 | High | **Lost reliable packets were never resent (race in DataStreamOutput).** The tick (PackData, RTO), the UDP receive thread (acks) and the out thread (NotifySent, send window) all used the send cache without a lock. PackData sets `NextSequence` before filling the slot. An ack handled between those two steps ran AdvancePendingOverHoles, saw an empty slot and moved LastAck past the new packet. That packet then counted as acked and was never resent. If it was lost, the client's reliable stream stalled until timeout. The existing "LastAck desync" repair is a symptom of this kind of race. Fix: one lock around every public entry point. | 28a7d7f |
| 5 | High | **Resend storm, and an unbounded resend queue.** `ProcessRtoResends` runs every world tick (about 1 ms). It queued every overdue packet again on each tick until that copy actually hit UDP, because the packet's age only resets in NotifySent. When a full window of up to 400 packets came due at once, the result was thousands of duplicates. With sending paused (`CanProcessPackets` off), the queue grew without bound for up to 120 s. Accelerated resends (on each selective ack) and forced resends duplicated the same way. Fix: `DataPacket.ResendQueued` means a packet is queued for resend only once until it is sent. | 9098d65 |
| 6 | High | **In-process gateway: zone handlers ran on the UDP receive thread, alongside the world tick.** `ClientSession.ToZone` called `WorldSession.FromClient` directly from the receive thread. Concrete case: a craft finishing on the tick (`Crafting.Update` → GiveItem) and the same player's bag request both change the unlocked Inventory lists at once. Fix: packets are queued and run on the tick in `OnProcessZonePackets`, before Flush and in client order. This is what the separate gateway (GatewayLink) already did. | 84916ec |
| 7 | High | **In-process gateway: a player's leave ran on the UDP thread.** For the client's own logout or quit (an SOE Disconnect), `ClientSession.OnDisconnect` ran `LinkClosed` on the receive thread. That means `PlayerManager.Leave` and every module's Forget ran beside the tick. A kick ran it on a thread-pool thread. Fix: a new `NetworkSession.OnRemoved` is called by NetworkManager on the tick when it drops the session. It is also called at Shutdown, so players are still saved. | 6be198c |
| 8 | Medium | **Separate gateway: ghost zone session.** `GatewayLink.Attach` loads the account and character off the world thread. If a ConnectionClosed arrived during the load (the player quit, or the gateway closed the connection), it found no session and did nothing, and the load then created the session anyway. That session was never closed: it stayed in byConnection and ZoneSessions and was flushed every tick. The same happened to loads still running when the gateway link dropped, and to a re-attach racing a stale load. Fix: each attach registers a serial before loading, ConnectionClosed or CloseAll removes it, and the load only creates the session if its own serial is still registered. | f9ac0fd |
| 9 | Medium | **A handler that throws silently stops an internal link.** In `InternalLink.ReadLoop`, any exception from a handler that the I/O catch filter did not cover faulted the read task without closing the link. Nothing more was read, but Closed never fired, so there was no reconnect, no gateway hold and no presence clean-up. One example: a zone link that stopped this way left all of its players frozen. Fix: handler exceptions are caught per message (new static `HandlerFailed` event, otherwise stderr). Parse errors close the link, as InvalidDataException already did. | fc000a2 |
| 10 | Medium | **Guild rank privilege escalation** (in the social service GuildService and the zone's Game/Guilds.cs). Any member with ModifyRanks could edit their own rank or any rank above it. That let them add DisbandGuild, TransferGuildLeadership or KickFromGuild, set level 2, or remove higher ranks. Fix: an editor below the leader may change only ranks below their own level, sets levels below their own, and can grant only permissions they hold. [I] live's exact rule is not traced. | 36254fa |
| 11 | Medium | **First reliable packet lost means a dead session.** The input stream's expected sequence started at whichever sequence arrived first, while the parser starts at slot 0. If packet 0 was lost, the resent 0 counted as a duplicate (answered with AckAll only), nothing was ever parsed, and the session timed out. Fix: start at 0, which the parser already assumed and which our output also uses. | 8839a08 |
| 12 | Medium | **Web pages: idle connections are held forever.** `ReceiveTimeout` does not apply to `ReadAsync`. The port is public in Docker, so bulk idle connections could use up the world process's sockets, including its database connections. Fix: a 30 s deadline per request on both reads. | 97ff257 |
| 13 | Low–Medium | **Auth and gateway outgoing queues were a plain `Queue<T>`** that was filled on the receive thread and drained on the tick. Fillers on the receive thread: ServerListReply (polled by character select), NameValidation, refusals, LoginQueue.Cancel → SendStatus to every queued session, and /gatewayprofiler lines. A concurrent Enqueue and TryDequeue can lose a reply or dequeue null. Fix: ConcurrentQueue. | 2c52965 |

## Seen, not fixed (follow-ups)

- **Internal ports have no authentication.** Anything that can reach the social service, gateway internal, zone
  internal (ZONE_INTERNAL_PORT) or login internal port can act as a world, zone or gateway. For example, it can send
  `TunnelFromExternal` for any connection id to the zone, impersonating that player. These ports are on the Docker
  network only, but each one needs a shared secret in RegisterBackEnd / PresenceSnapshot.
- **WebAPI `/srp/start`:** the handshake table is capped at 10,000, after which it answers 503 to everyone. An
  unauthenticated client can fill it, which blocks all logins for 2 minutes. The failure counters (`failures`,
  `registrations`) are never pruned for keys that never succeed, so random usernames make them grow.
- **Mail hand-over and the 60 s timeout:** if the zone→social link drops after the zone has handed over the items but
  before its answer arrives, and the player asks again after reconnect but before the held answer is sent
  (RemoteSocialService.OnConnected sets `link` before `mail.Reconnected()`), the attachment can be handed over twice.
  The window is narrow. A fix would send the held mail messages before publishing the new link.
- **`Admin.Leave` / `Telemetry.Leave` are keyed by character guid**, not by session. An old session's late
  ForgetNeverEntered (after a relog kick) clears the new session's /hideme, /runspeed and telemetry budget.
- **IgnoreService.cache** grows by one entry per character ever checked (each recipient of a General line). It is
  small but never pruned.
- **ChatService** relays the client's `kind` field unchanged. A modified client can show "user not found / offline"
  style lines to the audience of a channel. This is cosmetic.

## In-game checks for the coordinator

Restart the dev world. These changes touch the world, auth, gateway and social images.

1. Normal login, enter the world, move, chat (say, tell, General), open the bag and move items while a craft
   finishes, then log out to character select and log in again. Expect no change from before, and nothing new in
   the logs. Check that `left the world` appears for the logout. It now runs on the tick, up to one tick later.
2. Teleport and re-zone a few times (Spire, a cave) and confirm the loading screen finishes as before. Fixes 3, 6 and
   7 change when the zone handles packets: one world tick later at most.
3. Admin: with `ADMIN_ACCOUNT` set to the admin's account name, /admin still works. Optionally, create a character on
   another account named like the admin account. That character must not get admin, and the world logs
   `Admin entry '…' … is ignored`. Admin is then off for both until the list is fixed, which is the intended
   behaviour.
4. Guild: give the Officer rank ModifyRanks as leader. As the officer, try to add DisbandGuild to Officer and expect
   it to be refused (log line `may not change guild … rank`). Editing the Member rank with permissions the officer
   holds still works.
5. Separate-gateway stack (GATEWAY=1): log in and close the client during the loading screen. The world log shows
   `Attach for connection … dropped` or `closed by the gateway while its attach was loading`, and the admin
   monitor's zone session count goes back down.
6. Robustness, optional: send a 1-byte UDP datagram to the login port (20042) or the world port (19000) (for example
   `python -c "import socket;socket.socket(2,2).sendto(b'x',('127.0.0.1',19000))"`). The server keeps running.

No settings, dev commands or Docker variables were added.
