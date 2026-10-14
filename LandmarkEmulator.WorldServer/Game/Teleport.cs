using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using LandmarkEmulator.WorldServer.Network.Message.Model;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Moves a player (ClientUpdate 0xC TeleportToLocation, client reader FUN_142b83d70): vec4 position, vec4
    /// rotation, u8, bool, bool, u64, u32.
    /// </summary>
    public static class Teleport
    {
        /// <summary>
        /// Teleports farther than this (metres) go through the client's loading screen. A long jump makes the client
        /// reload VoxelFarm and mesh the destination; done in-game that blocks the main thread, and caves can take
        /// longer than the client's 3-minute hang watchdog (FUN_142ab3ab0, which kills the process with 0xBAADC0DE).
        /// Plain TeleportToLocation can hang the client renderer (QuerySimpleVisibility, FUN_144009a40) even for a few
        /// metres (2026-10-10: 3.5 m and 225 m jumps both hung), so every teleport re-zones; within a landscape that keeps
        /// the terrain and only shows a short loading screen.
        /// </summary>
        public const float LoadingScreenDistance = 0f;

        /// <summary>
        /// Moves a player to a position on a given landscape: a plain <see cref="To"/> when they are already there, else
        /// a re-zone onto that landscape.
        /// </summary>
        public static void ToZone(WorldSession session, Vector3 position, uint zoneId)
        {
            if (zoneId == 0 || zoneId == session.ZoneId)
                To(session, position);
            else
            {
                // Another landscape (own site, a friend, a respawn): the players left behind stop seeing this one and
                // those there start, as for the Landscapes window (Landscapes.TravelTo).
                uint oldZone = session.ZoneId;
                Rezone(session, Gathering.ClearOfTrees(position, zoneId), zoneId);
                PlayerManager.ChangedZone(session, oldZone);
            }
        }

        /// <summary>
        /// Moves a player: ClientUpdate TeleportToLocation {vec4 position, vec4 rotation, u8, bool, bool snap to ground,
        /// u64, u32}. With snapping on, the client drops the player onto the terrain at that x/z.
        /// The second byte (+0x41, "loading screen") moves the client into cClientRunStateWaitForTeleport (state
        /// 0x13, FUN_142cc0ba0): it shows "Waiting for Teleport...", polls WaitForWorldReady (FUN_142cc5480) every
        /// frame like at login, and returns to Running by itself. No server packet is needed to end it.
        /// </summary>
        public static void To(WorldSession session, Vector3 position, bool snapToGround = true, bool? loadingScreen = null)
        {
            // The client's ground snap finds the natural terrain, which on the Spire's platform is underneath it.
            if (Spire.Covers(session.ZoneId, position.X, position.Z))
                snapToGround = false;
            if (snapToGround)
                position = Gathering.ClearOfTrees(position, session.ZoneId);   // never arrive inside a trunk
            // Long jumps go through a re-zone (loading screen, fresh terrain engine); short ones move in place.
            // (With the distance at 0 every move re-zones, a zero-length one included: a plain TeleportToLocation onto
            // the spot the player already stood on hung the client too, 2026-10-11.)
            if (loadingScreen ?? (LoadingScreenDistance <= 0f || Vector3.Distance(session.Position, position) > LoadingScreenDistance))
            {
                Rezone(session, position);
                return;
            }
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(new Vector4(position, 1f));
            writer.Write(new Vector4(0f, 0f, 0f, 1f));
            writer.Write((byte)0);
            writer.Write(false);        // +0x41: loading screen; see Rezone for why it isn't used
            writer.Write(snapToGround); // +0x42: client puts the player on the terrain at x/z (FUN_142cc0ba0)
            writer.Write(0ul);
            writer.Write(0u);
            session.EnqueueMessage(ZoneMessageOpcode.ClientUpdateTeleportToLocation, output.ToArray());
            session.SetPosition(position);
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
        public static void Rezone(WorldSession session, Vector3 position, uint? zoneId = null)
        {
            uint zone = zoneId ?? session.ZoneId;
            // A placement in progress is cancelled first (re-zoning during one crashes the client) and the zoning goes
            // out a second later, after the client has reset its placement. The re-zone counts as pending meanwhile.
            // Boss displays go off first too (the client keeps boss-listed entities across the re-zone teardown).
            bool bosses = Creatures.ClearBossDisplays(session);
            if (Props.CancelPendingPlacement(session) | bosses)
            {
                session.RezoneTarget = position;
                session.RezoneStarted = DateTime.UtcNow;
                lock (deferred)
                    deferred[session] = (zone, DateTime.UtcNow.AddSeconds(1));
                return;
            }
            // A re-zone on this landscape is still loading (no ClientIsReady yet): a second ClientBeginZoning on top of
            // it left the client busy for good (2026-10-11, a site game spawn 6 s after a teleport). Retarget the
            // pending one instead; FinishRezone moves the player to the latest target when the client is ready.
            if (session.RezoneTarget != null && zone == session.ZoneId && DateTime.UtcNow - session.RezoneStarted < PendingRezoneLimit)
            {
                session.SetPosition(position);
                session.RezoneTarget = position;
                return;
            }
            RezoneNow(session, position, zone);
        }

        /// <summary>Re-zones waiting for a cancelled placement to clear on the client: zone and when to send.</summary>
        private static readonly Dictionary<WorldSession, (uint Zone, DateTime Due)> deferred = new();

        /// <summary>Server tick: sends deferred re-zones (to the latest target) once due.</summary>
        public static void Update()
        {
            List<(WorldSession Session, uint Zone)> due;
            lock (deferred)
            {
                due = deferred.Where(d => DateTime.UtcNow >= d.Value.Due).Select(d => (d.Key, d.Value.Zone)).ToList();
                foreach (var d in due)
                    deferred.Remove(d.Session);
            }
            foreach ((WorldSession session, uint zone) in due)
                if (session.RezoneTarget is Vector3 target && PlayerManager.Snapshot().Contains(session))
                    RezoneNow(session, target, zone);
        }

        private static void RezoneNow(WorldSession session, Vector3 position, uint zone)
        {
            lock (deferred)
                deferred.Remove(session);
            byte[] zoneDetails = Landscapes.ZoneDetails(zone);
            var details = new SendZoneDetails();
            details.Read(new GamePacketReader(zoneDetails));
            var zoning = new ClientBeginZoning
            {
                ZoneName = details.Unknown0,
                ZoneType = details.Unknown1,
                Position = new[] { position.X, position.Y, position.Z, 1f },
                SkyXml = details.Unknown3,
                ZoneId = zone,
                // +0xa8 (named ZoneNameId in the model) is the client's current world (client +0x378cc; the
                // Landscapes window selects that world) and +0xac (Unknown8) its zone (+0x378d0). Same name and world
                // as now: an in-place re-zone; otherwise the client changes zone.
                ZoneNameId = Landscapes.WorldId,
                Unknown8 = zone,
                SkyData = details.SkyData
            };
            session.EnqueueMessage(zoning);
            // Zoning builds a fresh voxel engine that stays idle (focus 0,0,0, its +0x78 "not started" flag set)
            // until it gets the continent settings. Those come in SendZoneDetails (the client then logs
            // "VoxelFarmCore::LoadSettings ContinentId ..."), exactly as at login.
            session.EnqueueMessage(ZoneMessageOpcode.SendZoneDetails, zoneDetails);
            // The destination first: the manifest is built for the landscape and position the player arrives at.
            if (zone != session.ZoneId)
                session.SetZone(zone);
            session.SetPosition(position);
            // The changed-cell manifest right away too (it is sent again at the re-zone's ClientIsReady): the new
            // engine meshes the destination while the loading screen is up, and without the manifest it has no
            // reason to fetch the server's edits there (the Spire platform had no collision after travel).
            Terrain.TerrainStore.SendManifest(session);
            session.RezoneTarget = position;
            session.RezoneStarted = DateTime.UtcNow;
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
        public static void FinishRezone(WorldSession session)
        {
            session.RezoneTarget = null;
            session.ArrivalConfirmed();
            // Everyone else on the landscape sees the player at the destination now. Their own movement updates would
            // do it, but a player who stands still after travelling stayed at the old spot on other screens.
            PlayerManager.ShowPosition(session);
            Health.Arrived(session);
            StoryTools.PlayerArrived(session);
        }
    }
}
