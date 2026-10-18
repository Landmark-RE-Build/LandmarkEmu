using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// ProceduralDataMonitorPacketUpdateRequest 0x109 (client admin monitor page 28; trace/architecture/admin-pages.md §3):
    /// the bare u16 opcode, no body, sent by the page's update FUN_143171e20 (0.695 FUN_14321c840) every 5 s while page 28
    /// is the current monitor page, on the Zone channel. Answered by <see cref="Game.AdminMonitor"/> with 0x10A.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ProceduralDataMonitorUpdateRequest)]
    public class ProceduralDataMonitorRequest : IReadable
    {
        public byte[] Body { get; private set; }

        public void Read(GamePacketReader reader) => Body = reader.ReadBytes(reader.BytesRemaining);
    }
}
