using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// Any inventory request from the client: a u16 sub-opcode then its body, which the handler reads per sub-opcode.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.InventoryBase)]
    public class InventoryRequest : SubRequest16
    {
        public const ushort EquippedRemove = 0x02;
        public const ushort EquipByGuid = 0x03;
        public const ushort DeleteItem = 0x0B;
        /// <summary>
        /// InventoryPacketToggleAutoResupply (0.695 sender FUN_142f261b0, writer FUN_142f18aa0; 0.589 FUN_142e87500):
        /// u64 item guid, bool on [P].
        /// </summary>
        public const ushort ToggleAutoResupply = 0x0D;
        /// <summary>RearrangeItemSlots (FUN_14352cfb0 / writer FUN_14352df30): u32 container, u32 n, n × {u32 slot, u64 item guid}.</summary>
        public const ushort RearrangeItemSlots = 0x0E;
        public const ushort ItemInstanceTooltipRequest = 0x0F;
        /// <summary>ActivateItem: u64 item guid (an item right-clicked/used from the inventory).</summary>
        public const ushort ActivateItem = 0x15;
        public const ushort ItemInstanceFullTooltipRequest = 0x10;
        public const ushort ItemDefinitionTooltipRequest = 0x11;
        public const ushort RequestTransferItem = 0x16;
        /// <summary>RequestOverflowAbsorb (Lua ClientInventory.MoveOverflow, FUN_14352da80): u8 0.</summary>
        public const ushort RequestOverflowAbsorb = 0x1C;
    }

    /// <summary>
    /// Where an item is: owner type, inventory (container), slot, and an owner guid for some owner types.
    /// </summary>
    public struct ItemLocation
    {
        public uint OwnerType;
        public uint Container;
        public uint Slot;
        public ulong OwnerGuid;

        public static ItemLocation Read(GamePacketReader reader)
        {
            var location = new ItemLocation
            {
                OwnerType = reader.ReadUInt(),
                Container = reader.ReadUInt(),
                Slot = reader.ReadUInt()
            };
            if (((location.OwnerType - 2) & 0xFFFFFFFC) == 0 && location.OwnerType != 4)
                location.OwnerGuid = reader.ReadULong();
            return location;
        }
    }

    /// <summary>
    /// Move (or use) an item: to, from, item guid (0 when it's implied by the source), quantity, swap.
    /// The destination comes first (the client checks the first location's container for space before sending). A
    /// slot of <see cref="AnySlot"/> means "wherever": as a source, find the item by guid; as a destination, the first
    /// free slot. A transfer onto its own location is what a double-click sends; using a tool from the action bar
    /// sends bag (any slot) → main hand.
    /// </summary>
    public class TransferItemRequest
    {
        public const uint AnySlot = uint.MaxValue;

        public ItemLocation Destination;
        public ItemLocation Source;
        public ulong ItemGuid;
        public uint Quantity;
        public bool Swap;

        public static TransferItemRequest Read(byte[] body)
        {
            var reader = new GamePacketReader(body);
            return new TransferItemRequest
            {
                Destination = ItemLocation.Read(reader),
                Source = ItemLocation.Read(reader),
                ItemGuid = reader.ReadULong(),
                Quantity = reader.ReadUInt(),
                Swap = reader.ReadBool()
            };
        }
    }
}
