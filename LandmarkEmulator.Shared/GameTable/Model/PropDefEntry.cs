namespace LandmarkEmulator.Shared.GameTable.Model
{
    /// <summary>
    /// A placeable prop (furniture, crafting stations...). Items that place a prop name it in their definition.
    /// </summary>
    public class PropDefEntry
    {
        public uint Id { get; set; }
        public bool Trivial { get; set; }
        public uint NameStringId { get; set; }
        public float MinScale { get; set; }
        public float MaxScale { get; set; }
        public uint ImageSetId { get; set; }
        public uint PlaceMode { get; set; }
        public bool DrawInPlayerTemplates { get; set; }
        public uint PropRecipeId { get; set; }
        public uint PlacementPackId { get; set; }
        public bool AllowUserRename { get; set; }
    }
}
