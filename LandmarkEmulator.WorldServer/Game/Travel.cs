using LandmarkEmulator.Shared.Network;
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
    /// Sub 0x0B RandomUndergroundRequest sends the player to a random cavern (<see cref="Caves"/>).
    /// </summary>
    public static class Travel
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private const byte LocationsSub = 3, IntraZoneSub = 8;

        public static void Handle(WorldSession session, TeleportRequest request)
        {
            switch (request.SubOpcode)
            {
                case TeleportRequest.RequestLocations:
                    Landscapes.SendLocations(session);
                    break;
                case Landscapes.TeleportRequestSub:
                    if (request.Body.Length >= 8)
                        Landscapes.TravelTo(session, BitConverter.ToUInt32(request.Body, 4));
                    break;
                case TeleportRequest.RandomUnderground:
                    Caves.TravelRandom(session);
                    break;
                case 0x0D:
                    // NextUndergroundReply {u64}: the Lost Crystal's "Eureka!" dialog accepted (Outposts).
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
                default:
                    log.Info($"Teleport request 0x{request.SubOpcode:X2}: {BitConverter.ToString(request.Body)}");
                    break;
            }
        }
    }
}
