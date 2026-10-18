using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// ReferenceData ProfileDefinitions (S→C): character profile (class) definitions. The server sends the client's own dump
    /// (Resources/PacketDumps, WorldSession's reference-data step), not this model, so the records are kept as raw bytes
    /// after the u32 count: their layout is not modelled. Read used to loop over the count itself — an empty record per
    /// entry, so a forged count of 0xFFFFFFFF from a client (the opcode has a factory) allocated without bound on the
    /// world thread — and Write wrote only the count.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ReferenceDataProfileDefinitions)]
    public class ReferenceDataProfileDefinitions : IReadable, IWritable
    {
        /// <summary>The record count the packet starts with.</summary>
        public uint Count { get; set; }
        /// <summary>The records, as sent (layout not modelled).</summary>
        public byte[] Records { get; set; } = System.Array.Empty<byte>();

        public void Read(GamePacketReader reader)
        {
            Count = reader.ReadUInt();
            Records = reader.ReadBytes(reader.BytesRemaining);
        }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(Count);
            writer.WriteBytes(Records ?? System.Array.Empty<byte>());
        }
    }
}
