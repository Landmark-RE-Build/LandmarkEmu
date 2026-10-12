namespace LandmarkEmulator.Database.Character.Model
{
    /// <summary>
    /// A character's progress on one achievement objective (goals, achievements and recipe discoveries are all
    /// achievements in the client). Only objectives the character has touched have a row; the rest use the defaults in
    /// Game/Achievements.cs.
    /// </summary>
    public class CharacterObjectiveModel
    {
        public ulong CharacterId { get; set; }
        public uint ObjectiveId { get; set; }
        public uint AchievementId { get; set; }
        /// <summary>0 not started, 1 active, 2 complete (the client's objective +0x2150).</summary>
        public byte State { get; set; }
        /// <summary>Current count towards the objective's required count.</summary>
        public uint Current { get; set; }
    }
}
