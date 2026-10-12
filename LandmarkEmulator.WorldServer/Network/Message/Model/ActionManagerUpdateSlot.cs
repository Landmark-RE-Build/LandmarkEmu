using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// Sets one action bar slot. Type 1 = item definition id in <see cref="Value"/> (as ActionSlotDefinitions.txt).
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ActionManagerUpdateSlotWire)]
    public class ActionManagerUpdateSlot : IWritable
    {
        public uint BarId { get; set; }
        public uint SlotId { get; set; }
        public uint Type { get; set; }
        public ulong Value { get; set; }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(BarId);
            writer.Write(SlotId);
            writer.Write(Type);
            writer.Write(Value);
        }
    }
}
