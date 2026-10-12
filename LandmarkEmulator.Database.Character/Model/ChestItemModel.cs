namespace LandmarkEmulator.Database.Character.Model
{
    /// <summary>
    /// An item stored in a chest prop. Item guids share one sequence with character items, so an item keeps its guid
    /// when it moves between a bag and a chest.
    /// </summary>
    public class ChestItemModel
    {
        /// <summary>Item guid.</summary>
        public ulong Id { get; set; }
        /// <summary>The chest prop's guid (world_prop.id).</summary>
        public ulong ChestGuid { get; set; }
        public uint ItemDefinitionId { get; set; }
        public uint Count { get; set; }
        /// <summary>Slot in the chest, 0 to its size - 1.</summary>
        public uint Slot { get; set; }
    }
}
