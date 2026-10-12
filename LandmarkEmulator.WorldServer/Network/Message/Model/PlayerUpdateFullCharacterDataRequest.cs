using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// Sent by the client after <see cref="PlayerUpdateAddLightweightPc"/>, asking for the character's full data; it
    /// stays an invisible placeholder until <see cref="PlayerUpdateLightweightToFullPc"/> arrives.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.PlayerUpdateFullCharacterDataRequestWire)]
    public class PlayerUpdateFullCharacterDataRequest : IReadable
    {
        public ulong Guid { get; set; }

        public void Read(GamePacketReader reader)
        {
            Guid = reader.ReadULong();
        }
    }
}
