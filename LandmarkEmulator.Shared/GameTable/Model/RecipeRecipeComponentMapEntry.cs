namespace LandmarkEmulator.Shared.GameTable.Model
{
    /// <summary>
    /// Components of a recipe given by reference (RecipeRecipeComponentMaps.txt): a RecipeComponents row (whose own
    /// RECIPE_ID is 0) per entry. Lumen Station prices are listed this way.
    /// </summary>
    public class RecipeRecipeComponentMapEntry
    {
        public uint Id { get; set; }
        public uint RecipeId { get; set; }
        public uint SortOrder { get; set; }
        public uint RecipeComponentId { get; set; }
    }
}
