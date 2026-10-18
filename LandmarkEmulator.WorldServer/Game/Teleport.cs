using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using Microsoft.Extensions.Configuration;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Moves a player (ClientUpdate 0xC TeleportToLocation, client reader FUN_142b83d70): vec4 position, vec4
    /// rotation, u8, bool, bool, u64, u32.
    /// </summary>
    public static class Teleport
    {
        /// <summary>
        /// Teleports up to this far (metres, same landscape) just move the character: a plain TeleportToLocation with no
        /// loading screen. The 0.695 client's handler (FUN_142d59f30) moves the player and re-centres its terrain engine
        /// (FUN_1431589a0, the heavy step) only when the destination is more than the engine's VoxelFarm setting
        /// 0x2c55a58 away (FUN_143152c40; default 80 m, DAT_144714810) or not loaded yet (FUN_143155b40); a short move
        /// keeps the terrain it has. Farther teleports re-zone (<see cref="Rezone"/>).
        /// History: from 2026-10-10 every teleport re-zoned because a plain TeleportToLocation had hung the 0.589 client
        /// in QuerySimpleVisibility; the cause found a day later was other packets in the same burst as a move (hit
        /// points, game HUD, site info: docs/terrain.md). A short move therefore counts as a pending arrival for
        /// <see cref="ShortMoveSettle"/>, so everything that waits for a re-zone's arrival waits for it too.
        /// Setting ShortTeleportDistance (WorldServer.json / environment); 0 = every teleport re-zones as before.
        /// DEFAULT 60 m since 2026-10-18: the earlier hangs (2026-10-16 4th hop; round 77 13th hop; round 80 3rd hop) came
        /// from the +0x30 field — a zero facing vector made the player's visibility boxes NaN (FacingVector,
        /// trace/0695/agent-visibility-analysis.md). With the facing fixed, round 83 made 30+ plain hops of 3-45 m, some
        /// right after re-zones, without a hang. 60 m stays inside the client's 80 m terrain window (no engine re-centre).
        /// </summary>
        public static float LoadingScreenDistance =>
            loadingScreenDistance ??= LandmarkEmulator.Shared.Configuration.SharedConfiguration.Configuration?.GetValue("ShortTeleportDistance", 60f) ?? 60f;
        private static float? loadingScreenDistance;

        /// <summary>
        /// Short moves with the packet's own "loading screen" byte (+0x41): the 0.695 client stores the new position first
        /// (FUN_142d5ce80) and then waits in cClientRunStateWaitForTeleport ("Waiting for Teleport...") until its world
        /// is ready there, instead of moving inside a normal frame. A brief screen, far shorter than a re-zone (which
        /// drops and re-adds every entity). Setting ShortTeleportWaitScreen (default off while measured, L116).
        /// </summary>
        public static bool ShortMoveWaitScreen =>
            shortMoveWaitScreen ??= LandmarkEmulator.Shared.Configuration.SharedConfiguration.Configuration?.GetValue("ShortTeleportWaitScreen", false) ?? false;
        private static bool? shortMoveWaitScreen;

        /// <summary>How long a short move counts as a pending arrival before its arrival work runs (ours).</summary>
        public static readonly TimeSpan ShortMoveSettle = TimeSpan.FromSeconds(1);

        /// <summary>Short moves whose arrival work (<see cref="FinishRezone"/>) is due: player → when.</summary>
        private static readonly Dictionary<WorldSession, DateTime> shortMoves = new();

        public static bool IsShortMovePending(WorldSession session)
        {
            lock (shortMoves)
                return shortMoves.ContainsKey(session);
        }

        /// <summary>
        /// Is a real re-zone of this player still loading (not a short move settling), or is the player arriving from
        /// another world (<see cref="WorldSession.FullRezoneNext"/>)? <see cref="To"/> would then re-zone rather than send a
        /// TeleportToLocation.
        /// </summary>
        public static bool RezoneLoading(WorldSession session)
            => (session.RezoneTarget != null && !IsShortMovePending(session)) || session.FullRezoneNext;

        /// <summary>
        /// Moves a player to a position on a given landscape: a plain <see cref="To"/> when they are already there, else
        /// a re-zone onto that landscape.
        /// </summary>
        /// <param name="facing">Which way the player faces on arrival (a Starting Location's rotation; null: unchanged
        /// identity, as before). See <see cref="Rezone"/>.</param>
        /// <param name="snapToGround">For a move on the same landscape: let the client put the player on the terrain at
        /// x/z (<see cref="To"/>). False for a point in a Chaos Cavern (a cavern graveyard), as the warp network's crystals.</param>
        public static void ToZone(WorldSession session, Vector3 position, uint zoneId, Quaternion? facing = null, bool snapToGround = true)
        {
            if (zoneId == 0 || zoneId == session.ZoneId)
                To(session, position, snapToGround, facing: facing);
            else
            {
                // Another landscape (own site, a friend, a respawn): the players left behind stop seeing this one and
                // those there start, as for the Landscapes window (Landscapes.TravelTo) — when the re-zone is actually
                // sent, which can be later (announceZoneChange; review 2026-10-18 #3).
                Rezone(session, Gathering.ClearOfTrees(position, zoneId), zoneId, facing, announceZoneChange: true);
            }
        }

        /// <summary>
        /// Moves a player: ClientUpdate TeleportToLocation {vec4 position, vec4 rotation, u8, bool, bool snap to ground,
        /// u64, u32}. With snapping on, the client drops the player onto the terrain at that x/z.
        /// The second byte (+0x41, "loading screen") moves the client into cClientRunStateWaitForTeleport (state
        /// 0x13, FUN_142cc0ba0): it shows "Waiting for Teleport...", polls WaitForWorldReady (FUN_142cc5480) every
        /// frame like at login, and returns to Running by itself. No server packet is needed to end it.
        /// </summary>
        /// <param name="ignoreTeleporter">The prop guid of a teleporter the client must not fire for <paramref name="ignoreMs"/>
        /// after the move (packet +0x48 u64 / +0x50 u32, FUN_143325060: guid → now + u32 on the client's timeGetTime clock,
        /// i.e. milliseconds; the teleporter's enter FUN_1436db8b0 and state machine FUN_1436db650 check it through
        /// FUN_1433286b0 with the pad's prop guid, the teleporter area's data +0x10) [P] — set for a Translocation Pad hop so
        /// the arrival pad doesn't send the player straight back (trace/0695/agent-short-teleport.md, audit-wire-e.md).</param>
        public static void To(WorldSession session, Vector3 position, bool snapToGround = true, bool? loadingScreen = null,
            Quaternion? facing = null, ulong ignoreTeleporter = 0, uint ignoreMs = 0)
        {
            // The client's ground snap finds the natural terrain, which on the Spire's platform is underneath it.
            if (Spire.Covers(session.ZoneId, position.X, position.Z))
                snapToGround = false;
            if (snapToGround)
                position = Gathering.ClearOfTrees(position, session.ZoneId);   // never arrive inside a trunk
            // Long jumps go through a re-zone (loading screen, fresh terrain engine); short ones move in place. A real
            // re-zone still loading takes the new target instead (one zone change at a time).
            bool realRezonePending = session.RezoneTarget != null && !IsShortMovePending(session);
            // An arrival from another world (WorldSession.FullRezoneNext): the client is still on that world, so only a
            // real re-zone (with this world's id) will do, however near the spot is.
            if (realRezonePending || session.FullRezoneNext || (loadingScreen ?? (LoadingScreenDistance <= 0f
                || Vector3.Distance(session.Position, position) > LoadingScreenDistance)))
            {
                Rezone(session, position, facing: facing);
                return;
            }
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(new Vector4(position, 1f));
            // +0x30 is a forward DIRECTION, not a quaternion: the apply path (0x14332a000 → FUN_142ec1650) masks w off and
            // normalises x/y/z as the actor's facing [P]. The identity quaternion sent until 2026-10-18 is the zero vector
            // there, which made the actor's rotation singular and the player's visibility boxes NaN a few frames later — the
            // octree then lost and double-linked them and QuerySimpleVisibility spun: the plain-teleport freeze
            // (trace/0695/agent-visibility-analysis.md, Frida round 80) [I strong]. So: a flat unit vector, never zero.
            writer.Write(FacingVector(facing ?? session.Facing));
            // +0x40 the client's movement version (FUN_142d59f30 stores it at game +0x32560, the byte its movement bodies
            // carry; ClientBeginZoning.MovementVersion) [P]: 1 as at login. 0 until 2026-10-18 dropped the mover's
            // non-full updates on other clients until its next full update.
            writer.Write(ClientBeginZoning.LiveMovementVersion);
            writer.Write(ShortMoveWaitScreen);  // +0x41: wait screen at the new position (ShortMoveWaitScreen)
            writer.Write(snapToGround); // +0x42: client puts the player on the terrain at x/z (FUN_142cc0ba0)
            writer.Write(ignoreTeleporter);   // +0x48: teleporter prop guid to ignore after the move (0 = none)
            writer.Write(ignoreMs);           // +0x50: for this long
            session.EnqueueMessage(ZoneMessageOpcode.ClientUpdateTeleportToLocation, output.ToArray());
            session.SetPosition(position);
            // A pending arrival for a moment: hit points, game HUD and site info wait for FinishRezone (Update) rather
            // than going out in the same burst as the move.
            session.RezoneTarget = position;
            session.RezoneStarted = DateTime.UtcNow;
            lock (shortMoves)
                shortMoves[session] = DateTime.UtcNow + ShortMoveSettle;
        }
    
        /// <summary>
        /// TeleportToLocation's +0x30 facing: the facing quaternion applied to +Z, flattened onto the ground plane and
        /// normalised, as (x, 0, z, 0); (0, 0, 1, 0) when that is degenerate or not finite. Never the zero vector.
        /// </summary>
        public static Vector4 FacingVector(Quaternion facing)
        {
            Vector3 forward = Vector3.Transform(Vector3.UnitZ, facing);
            forward.Y = 0f;
            float length = forward.Length();
            if (!float.IsFinite(length) || length < 1e-3f)
                return new Vector4(0f, 0f, 1f, 0f);
            forward /= length;
            return new Vector4(forward.X, 0f, forward.Z, 0f);
        }

        /// <summary>
        /// A long-range move through ClientBeginZoning (handler FUN_142cc6ed0 → FUN_142cbf940), the client's own
        /// zone-change path: loading screen, WaitForZoneLoad (run state 0x14) around the new position, then Running.
        /// When the zone name and zone id match the current zone (Zone_DispatchPacket sets the "same zone" flag) the
        /// client keeps its terrain and entities and only re-centres the load.
        /// Why not TeleportToLocation's loading-screen flag: state 0x13 checks world-ready around the character's
        /// stored position (+0x260), which only zoning updates, so it finishes at the old spot at once. The client
        /// then loads the destination inside a normal frame and can hang its renderer (QuerySimpleVisibility,
        /// FUN_144009a40) past the 3-minute watchdog.
        /// </summary>
        /// <param name="facing">
        /// The player's facing on arrival: ClientBeginZoning's rotation (packet +0x70) is the player actor's orientation —
        /// the 0.695 zoning apply FUN_142d58cd0 hands it to FUN_142ec4d70, which normalises the quaternion and sets it on
        /// the actor, then to its controller (FUN_143d0d060) [P]. Used for a Starting Location's direction (P5825, 16 Jun
        /// 2016: an arrow shows which way arriving players face; L118). Null: identity, as before.
        /// </param>
        /// <param name="announceZoneChange">When the re-zone changes landscape: tell the players of both landscapes
        /// (<see cref="PlayerManager.ChangedZone"/>) at the moment the ClientBeginZoning goes out. Callers used to do it
        /// right after this call, which was too early (the old landscape still set) when the re-zone was deferred.</param>
        public static void Rezone(WorldSession session, Vector3 position, uint? zoneId = null, Quaternion? facing = null,
            bool announceZoneChange = false)
        {
            uint zone = zoneId ?? session.ZoneId;
            // A short move still settling is not a zone load: drop it and zone for real.
            bool hadShortMove;
            lock (shortMoves)
                hadShortMove = shortMoves.Remove(session);
            if (hadShortMove)
                session.RezoneTarget = null;
            DateTime now = DateTime.UtcNow;
            bool inFlight = session.RezoneInFlight && now - session.RezoneStarted < PendingRezoneLimit;
            // A placement in progress is cancelled first (re-zoning during one crashes the client) and the zoning goes
            // out a second later, after the client has reset its placement. The re-zone counts as pending meanwhile.
            // Boss displays go off first too (the client keeps boss-listed entities across the re-zone teardown).
            bool bosses = Creatures.ClearBossDisplays(session);
            if (Props.CancelPendingPlacement(session) | bosses)
            {
                if (!inFlight)
                {
                    session.RezoneTarget = position;
                    session.RezoneStarted = now;
                }
                lock (deferred)
                {
                    bool announce = announceZoneChange || (deferred.TryGetValue(session, out Deferred old) && old.Announce);
                    deferred[session] = new Deferred(zone, position, facing, now.AddSeconds(1), announce);
                }
                return;
            }
            // A re-zone already waiting to be sent (above, or queued below): the newest request replaces its destination
            // and keeps its time, so the requests still go out one at a time (review 2026-10-18 #3).
            lock (deferred)
                if (deferred.TryGetValue(session, out Deferred waiting))
                {
                    deferred[session] = waiting with
                    {
                        Zone = zone, Position = position, Facing = facing ?? waiting.Facing, Announce = waiting.Announce || announceZoneChange
                    };
                    if (!inFlight)
                        session.RezoneTarget = position;
                    return;
                }
            if (inFlight)
            {
                // A re-zone on this landscape is still loading (no ClientIsReady yet, or its arrival hold is running): a
                // second ClientBeginZoning on top of it left the client busy for good (2026-10-11, a site game spawn 6 s
                // after a teleport). Retarget the pending one instead; FinishRezone moves the player to the latest
                // target when the client is ready.
                if (zone == session.ZoneId)
                {
                    session.SetPosition(position);
                    session.RezoneTarget = position;
                    return;
                }
                // Another landscape: queued until the pending one has finished (one zone change at a time, across
                // landscapes too). Sent at once before 2026-10-18, which put the first re-zone's held zone-done flags
                // and Reintroduce into the middle of the second load (review 2026-10-18 #3a).
                lock (deferred)
                    deferred[session] = new Deferred(zone, position, facing, now + QueuedRezoneRetry, announceZoneChange);
                return;
            }
            RezoneNow(session, position, zone, facing, announceZoneChange);
        }

        /// <summary>
        /// A re-zone waiting to be sent: for a cancelled placement to clear on the client (1 s), or for the re-zone in
        /// flight to finish (<see cref="WorldSession.RezoneInFlight"/>; checked every <see cref="QueuedRezoneRetry"/>).
        /// </summary>
        private sealed record Deferred(uint Zone, Vector3 Position, Quaternion? Facing, DateTime Due, bool Announce);

        /// <summary>Re-zones waiting to be sent, per player (at most one: the newest request wins).</summary>
        private static readonly Dictionary<WorldSession, Deferred> deferred = new();

        /// <summary>How often a re-zone queued behind one in flight checks again.</summary>
        private static readonly TimeSpan QueuedRezoneRetry = TimeSpan.FromMilliseconds(500);

        /// <summary>Server tick: sends deferred re-zones (to the latest target) once due.</summary>
        private static readonly List<(WorldSession Session, DateTime Due, Action Do)> afters = new();

        /// <summary>Runs <paramref name="action"/> for a session after <paramref name="ms"/> on the server tick (dropped if the
        /// player's connection has ended meanwhile).</summary>
        public static void After(WorldSession session, int ms, Action action)
        {
            lock (afters)
                afters.Add((session, DateTime.UtcNow.AddMilliseconds(ms), action));
        }

        public static void Update()
        {
            List<(WorldSession Session, DateTime Due, Action Do)> ready;
            lock (afters)
            {
                ready = afters.Where(a => DateTime.UtcNow >= a.Due).ToList();
                afters.RemoveAll(a => DateTime.UtcNow >= a.Due);
            }
            // A disconnected session is never run: WorldReady stays set after a disconnect, so an arrival hold firing
            // after the player dropped re-entered the dead session into the world (PlayerManager.Enter: friends saw it
            // come online, a fast relog of the character was kicked by the stale-session check; review 2026-10-18 #2).
            foreach (var a in ready)
                if (!a.Session.Disconnected && (PlayerManager.Snapshot().Contains(a.Session) || a.Session.WorldReady))
                    a.Do();
            List<(WorldSession Session, Deferred Rezone)> due;
            DateTime now = DateTime.UtcNow;
            lock (deferred)
            {
                due = deferred.Where(d => now >= d.Value.Due).Select(d => (d.Key, d.Value)).ToList();
                foreach (var d in due)
                    deferred.Remove(d.Session);
            }
            foreach ((WorldSession session, Deferred rezone) in due)
            {
                // As the holds above: a session placed on arrival from another world is not in the player list until its
                // re-zone finished (MiscHandler.FinishZoneIn), and a re-zone queued behind that one must still go out.
                if (session.Disconnected || !(PlayerManager.Snapshot().Contains(session) || session.WorldReady))
                    continue;
                if (session.RezoneInFlight && now - session.RezoneStarted < PendingRezoneLimit)
                {
                    // Still loading the previous one (or in its arrival hold): wait, unless a newer request took the slot.
                    lock (deferred)
                        deferred.TryAdd(session, rezone with { Due = now + QueuedRezoneRetry });
                    continue;
                }
                RezoneNow(session, rezone.Position, rezone.Zone, rezone.Facing, rezone.Announce);
            }
            List<WorldSession> settled;
            lock (shortMoves)
            {
                settled = shortMoves.Where(m => DateTime.UtcNow >= m.Value).Select(m => m.Key).ToList();
                foreach (WorldSession s in settled)
                    shortMoves.Remove(s);
            }
            foreach (WorldSession session in settled)
                if (PlayerManager.Snapshot().Contains(session))
                    FinishRezone(session, rezoned: false);   // a short move: no ClientBeginZoning, no Spire retries
        }

        private static void RezoneNow(WorldSession session, Vector3 position, uint zone, Quaternion? facing = null,
            bool announceZoneChange = false)
        {
            lock (deferred)
                deferred.Remove(session);
            // A new load: an arrival hold still pending belongs to the previous one (MiscHandler checks the serial), and
            // the early-sent flags are that arrival's, not this one's (Reintroduce would otherwise skip this landscape's
            // props / Spire; review 2026-10-18 #3).
            session.RezoneSerial++;
            session.SpireSentEarly = false;
            // The client deletes every entity but the player with this ClientBeginZoning: its prop stream starts again
            // from nothing, and nothing is streamed to it while it loads (Props.Stream.cs).
            Props.Forget(session);
            uint oldZone = session.ZoneId;
            byte[] zoneDetails = Landscapes.ZoneDetails(zone);
            var details = new SendZoneDetails();
            details.Read(new GamePacketReader(zoneDetails));
            var zoning = new ClientBeginZoning
            {
                ZoneName = details.ZoneName,
                ZoneType = details.ZoneType,
                Position = new[] { position.X, position.Y, position.Z, 1f },
                Rotation = facing is Quaternion q ? new[] { q.X, q.Y, q.Z, q.W } : new[] { 0f, 0f, 0f, 1f },
                SkyXml = details.SkyDefinition,
                // The same zone slots as the details that follow (client +0x31fac, +0x32868, +0x378cc / +0x378d0):
                // the world id is the client's current world (the Landscapes window selects that world); same name and
                // world as now = an in-place re-zone, otherwise the client changes zone.
                GeometryId = details.GeometryId,
                ZoneMode = details.ZoneMode,
                WorldId = Landscapes.WorldOf(zone),
                ZoneId = zone,
                WorldNameId = details.WorldNameId,
                // Version 1, as at login: 0 here left the player's movement bodies on version 0 after every re-zone
                // (ClientBeginZoning.MovementVersion) [P].
                MovementVersion = ClientBeginZoning.LiveMovementVersion,
                Continent = details.Continent
            };
            session.EnqueueMessage(zoning);
            // Zoning builds a fresh voxel engine that stays idle (focus 0,0,0, its +0x78 "not started" flag set)
            // until it gets the continent settings. Those come in SendZoneDetails (the client then logs
            // "VoxelFarmCore::LoadSettings ContinentId ..."), exactly as at login.
            session.EnqueueMessage(ZoneMessageOpcode.SendZoneDetails, zoneDetails);
            // The destination first: the manifest is built for the landscape and position the player arrives at.
            // Whether the landscape changes (either way the client deletes every entity but its own player at
            // ClientBeginZoning, FUN_142d1ccf0 case 8 [P]; the landscape's are sent again at ClientIsReady).
            session.RezoneChangedLandscape = zone != session.ZoneId;
            // Behind this load's screen until it drops (ClientLoading {0} after the zone-in): the landscape's players and
            // this one meet then, not at ClientIsReady 15-25 s earlier (PlayerManager.RezoneStarted / LoadingScreenLeft).
            // Before ChangedZone below, so the new landscape's players aren't shown the player yet.
            PlayerManager.RezoneStarted(session, session.RezoneChangedLandscape);
            if (zone != session.ZoneId)
                session.SetZone(zone);
            session.SetPosition(position);
            // The changed-cell manifest right away too (it is sent again at the re-zone's ClientIsReady): the new
            // engine meshes the destination while the loading screen is up, and without the manifest it has no
            // reason to fetch the server's edits there (the Spire platform had no collision after travel).
            Terrain.ZoneTerrain.SendManifest(session);
            session.RezoneTarget = position;
            session.RezoneStarted = DateTime.UtcNow;
            session.RezoneInFlight = true;
            session.RezoneZonedTo = position;
            if (announceZoneChange && oldZone != zone)
                PlayerManager.ChangedZone(session, oldZone);
        }

        /// <summary>A pending re-zone older than this is taken as lost (a new one is sent).</summary>
        public static readonly TimeSpan PendingRezoneLimit = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Called when the client confirms a re-zone (its repeat ClientIsReady): the re-zone is over, so the next one
        /// may be sent and whatever waited for the player's arrival (site games) can go out. This was written to also
        /// send a TeleportToLocation to the same spot (to re-centre the voxel engine) but was never called; re-zones
        /// finish without it, and a plain TeleportToLocation is what hangs the client (QuerySimpleVisibility), so it
        /// only clears the target.
        /// </summary>
        /// <param name="rezoned">
        /// False for a settled short move (TeleportToLocation): it sends no ClientBeginZoning, so no entity was deleted
        /// (FUN_142d1ccf0 case 8 [P]) and the Spire props keep their collision; their retries
        /// (<see cref="Spire.ReassertCollision"/>, 5 × 7 PlayerUpdatePosition ≈ 2.2 KB within 300 m of the Spire) are
        /// skipped (audit-bandwidth-2026-10-18 R2).
        /// </param>
        public static void FinishRezone(WorldSession session, bool rezoned = true)
        {
            // Retargeted while it loaded (Rezone on the same landscape: an evac answer, a Translocation Pad, a site game
            // spawn during the load or the arrival hold): the client stands where it was zoned to, the server at the new
            // target. Move it there with a fresh re-zone a second later — a re-zone rather than a plain TeleportToLocation,
            // as every teleport here re-zones by default (LoadingScreenDistance 0) — so server and client agree
            // (review 2026-10-18 #3b).
            Vector3? retarget = session.RezoneInFlight && session.RezoneTarget is Vector3 target
                && Vector3.Distance(target, session.RezoneZonedTo) > 1f ? target : null;
            session.RezoneInFlight = false;
            session.RezoneTarget = null;
            session.ArrivalConfirmed();
            // Everyone else on the landscape sees the player at the destination now. Their own movement updates would
            // do it, but a player who stands still after travelling stayed at the old spot on other screens.
            PlayerManager.ShowPosition(session);
            Health.Arrived(session);
            StoryTools.PlayerArrived(session);
            if (rezoned)
                Spire.ReassertCollision(session);
            lock (deferred)
            {
                if (retarget is Vector3 moveTo)
                    deferred.TryAdd(session, new Deferred(session.ZoneId, moveTo, null, DateTime.UtcNow.AddSeconds(1), false));
                // A re-zone waits to go out next (the retarget above, or one queued during this load): the player counts
                // as re-zoning until it is sent, so streams and teleports keep waiting for it.
                if (deferred.TryGetValue(session, out Deferred next))
                {
                    session.RezoneTarget = next.Position;
                    session.RezoneStarted = DateTime.UtcNow;
                }
            }
        }
    }
}
