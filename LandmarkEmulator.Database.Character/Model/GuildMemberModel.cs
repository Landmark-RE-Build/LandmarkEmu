namespace LandmarkEmulator.Database.Character.Model
{
    /// <summary>A character's guild membership (a character is in at most one guild).</summary>
    public class GuildMemberModel
    {
        public ulong CharacterId { get; set; }
        public ulong GuildId { get; set; }
        public uint RankId { get; set; }
    }
}
