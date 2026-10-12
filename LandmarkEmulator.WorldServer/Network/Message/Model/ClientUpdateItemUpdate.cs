using System;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// Changes an item the client already has (reader FUN_142b832a0, applied by FUN_142f2ff70). A new container or
    /// slot moves the existing item object, so action bar slots bound to it stay bound; delete + add would unbind
    /// them (the slot greys out and loses its selection).
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ClientUpdateItemUpdate)]
    public class ClientUpdateItemUpdate : IWritable
    {
        public ulong Guid { get; set; }
        public uint Count { get; set; }
        public uint Durability { get; set; }
        public uint Unknown28 { get; set; }
        public ulong Unknown38 { get; set; }
        public uint Slot { get; set; }
        public uint Container { get; set; }
        /// <summary>Wear, written as the last field (client item +0x148, a float; FUN_142f2ff70).</summary>
        public float Wear { get; set; }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(Guid);
            writer.Write(Count);
            writer.Write(Durability);
            writer.Write(Unknown28);
            writer.Write(Unknown38);
            writer.Write(Slot);
            writer.Write(Container);
            // Two blocks (FUN_142b87690): five u32s, then two lists (empty). The client only applies the update when
            // the packet is consumed exactly, so these must be complete.
            for (int block = 0; block < 2; block++)
            {
                for (int i = 0; i < 5; i++)
                    writer.Write(0u);
                writer.Write(0u);
                writer.Write(0u);
            }
            writer.Write(0u);
            writer.Write(BitConverter.SingleToUInt32Bits(Wear));
        }
    }
}
