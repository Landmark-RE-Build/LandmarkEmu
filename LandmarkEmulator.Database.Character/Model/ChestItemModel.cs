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
        /// <summary>
        /// The item's wear (durability points lost), kept while it is stored: an item instance keeps its condition when
        /// it goes into a chest and comes back out (as CharacterItemModel.Wear).
        /// </summary>
        public float Wear { get; set; }
        /// <summary>
        /// The character who put the item in (0 for rows stored before this was kept: their chest's owner counts). A
        /// non-tradeable item comes out only for them (client text 561999 "Can't take non-tradeable items that didn't
        /// originally belong to you.", TransferItemResponse 0x21).
        /// </summary>
        public ulong DepositorGuid { get; set; }
        /// <summary>
        /// The item's auto-resupply switch, kept while it is stored (as CharacterItemModel.AutoResupply: live's one item
        /// table held it for chest items too, items.chest_owner_guid beside items.auto_resupply).
        /// </summary>
        public bool AutoResupply { get; set; }
    }
}
