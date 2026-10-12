using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// ReferenceData ItemCategoryDefinitions (S→C): the item category list.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ReferenceDataItemCategoryDefinitions)]
    public class ReferenceDataItemCategoryDefinitions : IReadable, IWritable
    {
        public List<uint> Categories { get; set; } = new();

        public void Read(GamePacketReader reader)
        {
            uint categoryCount = reader.ReadUInt();
        }

        public void Write(GamePacketWriter writer)
        {
            writer.Write((uint)Categories.Count);
        }
    }
}
