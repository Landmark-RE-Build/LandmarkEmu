using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// PlayerUpdate 0x3D ReadyToReviveResponse (C→S; client FUN_143388250, Lua AcceptRevivalRequest /
    /// DeclineRevivalRequest, and sent by itself when ReadyToRevive reaches the local player): u8 accept, u64 the
    /// player's own guid.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.PlayerUpdateReadyToReviveResponseWire)]
    public class PlayerUpdateReadyToReviveResponse : IReadable
    {
        public byte[] Body { get; private set; }

        public void Read(GamePacketReader reader) => Body = reader.ReadBytes(reader.BytesRemaining);
    }
}
