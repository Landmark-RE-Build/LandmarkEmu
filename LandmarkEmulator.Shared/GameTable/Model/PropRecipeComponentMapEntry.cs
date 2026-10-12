namespace LandmarkEmulator.Shared.GameTable.Model
{
    /// <summary>
    /// Materials a prop recipe needs (PropRecipeComponentMaps.txt): a RecipeComponents row per entry.
    /// </summary>
    public class PropRecipeComponentMapEntry
    {
        public uint Id { get; set; }
        public uint PropRecipeId { get; set; }
        public uint SortOrder { get; set; }
        public uint RecipeComponentId { get; set; }
    }
}
