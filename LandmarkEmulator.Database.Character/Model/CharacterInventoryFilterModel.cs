namespace LandmarkEmulator.Database.Character.Model
{
    /// <summary>
    /// One of a character's saved bag filters (Game.InventoryFilters): the client's
    /// <c>InventoryFilters::PlayerInventoryFilter</c> {FilterId, FilterName, FilterString} (0.695 FUN_142efcf10). The client
    /// tells the server about every change with PlayerFilterUpdatePacket (zone 0xDD sub 4) and gets the whole set back in
    /// SendSelfToClient's InventoryFilterManager at the next login. One row per character and filter id.
    /// </summary>
    public class CharacterInventoryFilterModel
    {
        public ulong CharacterId { get; set; }
        /// <summary>The client's filter id (never 0: the client gives a new filter its NextFilterId, which starts at 1).</summary>
        public uint FilterId { get; set; }
        /// <summary>"FilterName": the name the player gave it (Lua ClientInventory.AddFilter argument 2).</summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// "FilterString": what the Inventory window matches (argument 3): a comma-separated list of item Type values
        /// (Inventory.swf ItemContainerBase.filterData splits it on ",").
        /// </summary>
        public string FilterString { get; set; } = "";
    }
}
