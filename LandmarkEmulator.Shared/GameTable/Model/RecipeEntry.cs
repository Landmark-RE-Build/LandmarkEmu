namespace LandmarkEmulator.Shared.GameTable.Model
{
    /// <summary>
    /// A crafting recipe: makes <see cref="ItemCount"/> of <see cref="ItemDefinitionId"/>.
    /// </summary>
    public class RecipeEntry
    {
        public uint Id { get; set; }
        public uint SortOrder { get; set; }
        public uint ItemDefinitionId { get; set; }
        public uint ItemCount { get; set; }
        public uint RewardSetId { get; set; }
        public uint RecipeType { get; set; }
        public uint RecipeGroupId { get; set; }
        public uint RecipeRaceId { get; set; }
        public float CastingTime { get; set; }
        public bool DefaultLearned { get; set; }
        public bool AllowCatalyst { get; set; }
        public uint StackSize { get; set; }
        public uint OverrideNameId { get; set; }
        public uint TrackerAchievementId { get; set; }
        public float CooldownTime { get; set; }
        public uint VisibilityRequirementId { get; set; }
        public bool CanCraftWithoutStation { get; set; }
        public uint MaxQueue { get; set; }
    }
}
