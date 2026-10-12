using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// ZoneSafeTeleportRequest (0x7C, C→S, no body; FUN_142c709d0). Sent by Lua EvacLocalPlayerToSafepoint
    /// (FUN_14354b210) when the player is outside the zone's bounds (fell out of the world) and by TeleportToSafePoint
    /// (FUN_143518170). See <see cref="Game.Health.EvacuateToSafePoint"/>.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ZoneSafeTeleportRequest)]
    public class ZoneSafeTeleportRequest : IReadable
    {
        public void Read(GamePacketReader reader)
        {
        }
    }
}
