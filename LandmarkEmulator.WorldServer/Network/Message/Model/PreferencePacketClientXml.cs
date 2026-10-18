using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// PreferencePacketClientXml, zone opcode 0x03 (u16): the client's preference document. Writer FUN_142c13420 (0.695):
    /// u16 opcode 3 (+0x08), u32 kind (+0x10, always 1), then the IString at +0x18 as u32 length + that many characters,
    /// no terminator (FUN_142d4b510). The body is kept raw; Game.Preferences reads and checks it.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.PreferencePacket)]
    public class PreferencePacketClientXml : IReadable
    {
        public byte[] Body { get; private set; }
        public void Read(GamePacketReader reader) => Body = reader.ReadBytes(reader.BytesRemaining);
    }
}
