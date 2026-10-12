namespace LandmarkEmulator.Database.Character.Model
{
    /// <summary>A crafting recipe a character learned (from a recipe item); default-learned ones aren't stored.</summary>
    public class CharacterRecipeModel
    {
        public ulong CharacterId { get; set; }
        public uint RecipeId { get; set; }
    }
}
