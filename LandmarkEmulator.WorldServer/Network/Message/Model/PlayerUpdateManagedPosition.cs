using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// PlayerUpdate 0xC7 from the client: the position of an entity it drives itself (seen while a prop is held for
    /// placement: transient id, then position data). Kept raw; the prop's final place arrives with PropBase Save.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.PlayerUpdateManagedPosition)]
    public class PlayerUpdateManagedPosition : IReadable
    {
        public byte[] Data { get; private set; }

        public void Read(GamePacketReader reader)
        {
            Data = reader.ReadBytes(reader.BytesRemaining);
        }
    }
}
