using System;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model.Shared;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// Changes an item the client already has (reader FUN_142b832a0, applied by FUN_142f2ff70; 0.695 reader FUN_142c1ca60,
    /// applied by FUN_142fcf620). A new container or slot moves the existing item object, so action bar slots bound to
    /// it stay bound; delete + add would unbind them (the slot greys out and loses its selection). Item field names are
    /// the client's own, from its item printer FUN_143b793d0 ("Item: {Guid, Definition, Name, TintOverride, Count,
    /// ConsumedCount, LastCastTime, RentalExpirationTime, RentalUpsellsSent, MarketplaceBundleId}") [P].
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ClientUpdateItemUpdate)]
    public class ClientUpdateItemUpdate : IWritable
    {
        public ulong Guid { get; set; }
        /// <summary>+0x20 Count (item +0xd0, through the item's vtable +0x18).</summary>
        public uint Count { get; set; }
        /// <summary>
        /// +0x24 ConsumedCount (item +0xd4, FUN_143b79ca0 clamps it to 0..Count) [P name]: how many of the stack are used
        /// up. Was named "Durability" here; the server sends 0 (the dev command "durability" still writes it).
        /// </summary>
        public uint ConsumedCount { get; set; }
        /// <summary>
        /// +0x28 item +0xd8: with RentalExpirationTime it makes the item a rental: the client never stacks or merges an
        /// item whose expiration is set or whose +0xd8 is above 0 (FUN_143b789b0, FUN_143b78c10) [P use]; the rental's
        /// length before it starts counting [I name]. 0 = not a rental.
        /// </summary>
        public uint RentalDuration { get; set; }
        /// <summary>
        /// +0x38 RentalExpirationTime (item +0x120 block, FUN_143b79e10) [P name]: the client time the rental ends
        /// (FUN_143b79b30 = expired, FUN_143b79660 = time left). 0 = none: the "no expiration" value DAT_1455fc4b8 is a
        /// .bss global nothing writes, i.e. 0 [P].
        /// </summary>
        public ulong RentalExpirationTime { get; set; }
        public uint Slot { get; set; }
        public uint Container { get; set; }
        /// <summary>Wear, written as the last field (client item +0x148, a float; FUN_142f2ff70).</summary>
        public float Wear { get; set; }
        /// <summary>
        /// The item's two ItemRandom blocks, as its ItemAdd record has them (quality, tool stats). The client copies them
        /// over the item (FUN_142fcf620 → FUN_143b780f0) and re-derives the cost multiplier from the quality, so zeros
        /// turned every updated stack into quality 0 and wiped a tool's stats (trace/0695/craft-a-client.md §5.1). Null =
        /// empty blocks.
        /// </summary>
        public InventoryItem.DefinitionData.RandomData Random { get; set; }
        public InventoryItem.DefinitionData.RandomData Random2 { get; set; }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(Guid);
            writer.Write(Count);
            writer.Write(ConsumedCount);
            writer.Write(RentalDuration);
            writer.Write(RentalExpirationTime);
            writer.Write(Slot);
            writer.Write(Container);
            // Two blocks (FUN_142b87690): five u32s, then the stat and catalyst lists. The client only applies the update
            // when the packet is consumed exactly, so these must be complete.
            (Random ?? new InventoryItem.DefinitionData.RandomData()).Write(writer);
            (Random2 ?? new InventoryItem.DefinitionData.RandomData()).Write(writer);
            writer.Write(0u);
            writer.Write(BitConverter.SingleToUInt32Bits(Wear));
        }
    }
}
