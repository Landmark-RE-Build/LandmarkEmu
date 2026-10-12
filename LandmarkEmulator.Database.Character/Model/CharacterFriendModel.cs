namespace LandmarkEmulator.Database.Character.Model
{
    /// <summary>A player on a character's friends list (one-way: adding someone doesn't add you to theirs).</summary>
    public class CharacterFriendModel
    {
        public ulong CharacterId { get; set; }
        public ulong FriendId { get; set; }
    }
}
