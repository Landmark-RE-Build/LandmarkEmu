using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model.Shared;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// Adds an item to the player's inventory. The body is a length-prefixed blob: a "has item" byte, then the same
    /// item record SendSelfToClient uses (0.589 client).
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ClientUpdateItemAdd)]
    public class ClientUpdateItemAdd : IWritable
    {
        public InventoryItem Item { get; set; }

        public void Write(GamePacketWriter writer)
        {
            var blob = new List<byte>();
            var blobWriter = new GamePacketWriter(blob);
            // Leading bool: an item definition follows (LZ4 chunk, client FUN_142b800a0) before the item. The client
            // already has every definition from login, so only the item is sent.
            blobWriter.Write(false);
            Item?.Write(blobWriter);

            writer.Write((uint)blob.Count);
            writer.WriteBytes(blob.ToArray());
        }
    }
}
