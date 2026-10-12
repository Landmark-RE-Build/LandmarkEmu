namespace LandmarkEmulator.Shared.GameTable.Model
{
    /// <summary>
    /// One ingredient of a recipe: <see cref="ComponentCount"/> of item <see cref="ComponentId"/>.
    /// </summary>
    public class RecipeComponentEntry
    {
        public uint Id { get; set; }
        public uint RecipeId { get; set; }
        public uint SortOrder { get; set; }
        public uint ComponentType { get; set; }
        public uint ComponentId { get; set; }
        public uint ComponentCount { get; set; }
        public uint IconId { get; set; }
        public uint NameId { get; set; }
        public uint DescriptionId { get; set; }
    }
}
