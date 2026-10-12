namespace LandmarkEmulator.Database.Character.Model
{
    /// <summary>A character on another character's ignore list (Game.Ignores; client Blocked list).</summary>
    public class CharacterIgnoreModel
    {
        public ulong CharacterId { get; set; }
        public ulong IgnoredId { get; set; }
        public string Name { get; set; }
    }
}
