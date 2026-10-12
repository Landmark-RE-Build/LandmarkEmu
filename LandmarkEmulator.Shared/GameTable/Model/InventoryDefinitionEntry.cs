namespace LandmarkEmulator.Shared.GameTable.Model
{
    /// <summary>
    /// A slot in one of the fixed inventories. Inventory type 5 is the resource bag: each slot holds one item
    /// definition (<see cref="SlotId"/>) up to <see cref="SlotValue"/>.
    /// </summary>
    public class InventoryDefinitionEntry
    {
        public uint Id { get; set; }
        public uint InventoryType { get; set; }
        public uint SlotNumber { get; set; }
        public uint SlotTypeId { get; set; }
        public uint SlotId { get; set; }
        public uint SlotValue { get; set; }
        public uint Param2 { get; set; }
        public uint SortOrder { get; set; }
    }
}
