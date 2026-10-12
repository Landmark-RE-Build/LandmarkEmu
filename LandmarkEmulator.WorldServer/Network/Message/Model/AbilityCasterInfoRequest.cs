using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// Abilities 0x27 sub 2 (C→S) {u32 ability id}: the client wants an ability's caster info (range, cast time, costs)
    /// before it will use it. Answered in MiscHandler.AbilityCasterInfoRequest.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.AbilityCasterInfoRequest)]
    public class AbilityCasterInfoRequest : IReadable
    {
        public uint Unknown0 { get; set; }

        public void Read(GamePacketReader reader)
        {
            Unknown0 = reader.ReadUInt();
        }
    }
}
