namespace LandmarkEmulator.Shared.GameTable.Model
{
    /// <summary>
    /// A default action bar slot: bar 0 starts with the mining pick, logging axe, lightstone and campfire.
    /// <see cref="DefaultType"/> 1 means <see cref="DefaultValue"/> is an item definition id.
    /// </summary>
    public class ActionSlotDefinitionEntry
    {
        public uint Id { get; set; }
        public uint BarId { get; set; }
        public uint SlotId { get; set; }
        public uint RestrictionId { get; set; }
        public uint DefaultType { get; set; }
        public uint DefaultValue { get; set; }
    }
}
