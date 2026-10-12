namespace LandmarkEmulator.Shared.GameTable.Model
{
    /// <summary>
    /// A kind of crafting station (workshop, forge, sifter...); recipes are tied to stations by
    /// <see cref="CraftingStationRecipeMappingEntry"/>.
    /// </summary>
    public class CraftingStationEntry
    {
        public uint Id { get; set; }
        public uint NameId { get; set; }
        public uint DescriptionId { get; set; }
        public uint CraftingStationTypeId { get; set; }
        public uint Param1 { get; set; }
        public uint Param2 { get; set; }
        public float FloatParam1 { get; set; }
        public float FloatParam2 { get; set; }
        public string CodeFactoryName { get; set; }
        public bool Private { get; set; }
        public uint StationAnimStart { get; set; }
        public uint StationAnimStop { get; set; }
        public uint CraftersFxId { get; set; }
        public string Description { get; set; }
        public uint PlayerAnimStart { get; set; }
        public uint PlayerAnimStop { get; set; }
        public bool IsGlobal { get; set; }
    }
}
