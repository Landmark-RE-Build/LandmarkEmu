namespace LandmarkEmulator.Database.Character.Model
{
    /// <summary>
    /// Per-character narrative flags for world NPCs (Game.Npc.WorldNpcs): quest offers, greetings, chain unlocks.
    /// Kept apart from <see cref="CharacterObjectiveModel"/> so achievement progress stays the Journal bus only.
    /// </summary>
    public class CharacterNpcFlagModel
    {
        public ulong CharacterId { get; set; }
        /// <summary>NPC definition id (e.g. fungus_scout), not the creature guid.</summary>
        public string NpcId { get; set; } = "";
        public string Key { get; set; } = "";
        public int Value { get; set; }
    }
}
