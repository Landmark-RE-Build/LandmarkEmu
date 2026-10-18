using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Game.WorldChannel;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Travel window requests that go through TeleportBase 0xFC (u8 sub). Sub 2 RequestLocations (Other Worlds) is
    /// answered with sub 3 Locations (client reader FUN_1430c4150): {u32 n, n × world location (FUN_1430c2ad0),
    /// u32 k, k × entry (FUN_143007a30)}. Only one world exists on this server, so both lists are empty for now.
    /// Sub 0x0B RandomUndergroundRequest sends the player to a random cavern (<see cref="Caves"/>), on another world of the
    /// gateway when the trip's world roll says so (OPEN-LEADS L131, <see cref="CrossWorld.CavernTripsEnabled"/>).
    /// </summary>
    public static class Travel
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private const byte LocationsSub = 3, IntraZoneSub = 8;
        /// <summary>TeleportPlayEffectRequest (0.695 FUN_1431736e0) and TeleportManagedRequest (FUN_143173480), sent by
        /// teleporter areas (trace/0695/movers-695.md §4).</summary>
        private const byte PlayEffectSub = 9, ManagedSub = 10;
        /// <summary>
        /// The travel queue, sent on the World channel: 6 TeleportToQueuedLocation {u32 world, u32 zone} (0.695: the Game
        /// Action Queue item's Accept FUN_1433fcec0, Lua Teleport.TeleportToQueuedLocation FUN_143173a90), 7
        /// TeleportCancelQueue {same} (Decline / the queue dialog's Cancel, FUN_143171570). The world-channel back end's
        /// (trace/architecture/world-channel-service.md §3.6); built in <see cref="TravelQueue"/> (L60).
        /// </summary>
        private const byte ToQueuedLocationSub = 6, CancelQueueSub = 7;

        public static void Handle(WorldSession session, TeleportRequest request)
        {
            switch (request.SubOpcode)
            {
                case TeleportRequest.RequestLocations:
                    Landscapes.SendLocations(session);
                    break;
                case Landscapes.TeleportRequestSub:
                    if (request.Body.Length >= 8)
                    {
                        // {u32 world, u32 zone}: another world's landscape hands the player over to that world (B.10).
                        uint world = BitConverter.ToUInt32(request.Body, 0), zone = BitConverter.ToUInt32(request.Body, 4);
                        // This world's landscapes may be full: the travel queue (L60) holds the player until a place frees.
                        if (world != Landscapes.WorldId && Landscapes.OtherWorld(world) is Landscapes.OtherWorldInfo other)
                            CrossWorld.Travel(session, other, zone);
                        else if (!TravelQueue.Request(session, world, zone))
                            Landscapes.TravelTo(session, zone);
                    }
                    break;
                case TeleportRequest.RandomUnderground:
                    // Another world first when the roll says so (L131, CavernTripsOtherWorlds), else this world's
                    // landscape-then-graveyard roll.
                    if (!Caves.TravelToOtherWorld(session))
                        Caves.TravelRandom(session);
                    break;
                case 0x0D:
                    // NextUndergroundReply {u64}: the Lost Crystal's "Eureka!" dialog accepted (Outposts), for the offer
                    // whose token it echoes (F9).
                    if (request.Body.Length >= 8 && Outposts.TakeOffer(session, BitConverter.ToUInt64(request.Body, 0)))
                        Outposts.NextCave(session);
                    break;
                case IntraZoneSub:
                    // TeleportIntraZoneRequest (FUN_1430c8260): f32 x, y, z, w, u64 target (0), u8. Sent by the game
                    // editor's Go To (FUN_14343ecd0: a spot next to the prop). Only served for a player editing a game,
                    // to a point on that game's build site.
                    if (request.Body.Length >= 16)
                    {
                        var to = new System.Numerics.Vector3(BitConverter.ToSingle(request.Body, 0), BitConverter.ToSingle(request.Body, 4),
                            BitConverter.ToSingle(request.Body, 8));
                        if (InstancedGames.MayGoTo(session, to))
                            Teleport.To(session, to, snapToGround: false);
                        else
                            log.Info($"{session.CharacterName}: intra-zone teleport to {to} refused.");
                    }
                    break;
                case PlayEffectSub:
                    // TeleportPlayEffectRequest: a Translocation Pad's departure / arrival effect (Props.Movers, E.1).
                    Props.TeleportEffect(session, request.Body);
                    break;
                case ManagedSub:
                    // TeleportManagedRequest: a Translocation Pad sends the player to its pack's other pad (E.1).
                    Props.ManagedTeleport(session, request.Body);
                    break;
                case ToQueuedLocationSub:
                case CancelQueueSub:
                    if (WorldChannelServices.WorldChannel.TeleportQueueRequest(session, request.SubOpcode, request.Body))
                        break;
                    goto default;
                default:
                    log.Info($"Teleport request 0x{request.SubOpcode:X2}: {BitConverter.ToString(request.Body)}");
                    break;
            }
        }
    }
}
