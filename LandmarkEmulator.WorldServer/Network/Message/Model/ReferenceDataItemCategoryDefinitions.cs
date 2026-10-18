using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// ReferenceData ItemCategoryDefinitions (S→C): the item category list. The server sends the client's own dump
    /// (Resources/PacketDumps, WorldSession's reference-data step), not this model, so the records are kept as raw bytes
    /// after the u32 count: their layout is not modelled. Read used to read only the count (leaving the records unread) and
    /// Write wrote a list's count but never its entries.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ReferenceDataItemCategoryDefinitions)]
    public class ReferenceDataItemCategoryDefinitions : IReadable, IWritable
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
