using System;

namespace LandmarkEmulator.Database.Social.Model
{
    /// <summary>
    /// friend: one row per (character, friend) — friends lists are one-way (adding someone doesn't add you to theirs;
    /// the client has no friend-request flow, ConfirmFriendRequest 0x1E/0x13 is unused). social-service.md §5.2, from
    /// lmemu_character.character_friend.
    /// </summary>
    public class FriendModel
    {
        public ulong CharacterGuid { get; set; }
        public ulong FriendGuid { get; set; }
        public DateTime AddedAt { get; set; }
    }
}
