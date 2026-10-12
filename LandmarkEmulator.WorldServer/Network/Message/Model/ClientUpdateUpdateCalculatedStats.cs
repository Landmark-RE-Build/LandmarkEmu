using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// Sets calculated stat values on an entity; this is what fills the client's ClientCalculatedStatManager
    /// (0.589 client: ClientUpdatePacketUpdateCalculatedStats).
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ClientUpdateUpdateCalculatedStats)]
    public class ClientUpdateUpdateCalculatedStats : IWritable
    {
        public ulong Guid { get; set; }
        public List<(uint StatId, float Value)> Stats { get; } = new();

        public void Write(GamePacketWriter writer)
        {
            writer.Write(Guid);
            writer.Write((uint)Stats.Count);
            foreach ((uint statId, float value) in Stats)
            {
                writer.Write(statId);
                writer.Write(value);
            }
        }
    }
}
