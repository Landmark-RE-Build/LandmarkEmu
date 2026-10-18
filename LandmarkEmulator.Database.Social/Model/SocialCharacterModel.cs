using System;

namespace LandmarkEmulator.Database.Social.Model
{
    /// <summary>
    /// social_character: the social service's own directory of characters (social-service.md §5.2). Characters are
    /// global (lmemu_character.character.id is one u64 for every world), so the guid is the key everywhere in the
    /// service. Filled once from lmemu_character (§5.7, <see cref="SocialImport"/>), then kept up to date by the
    /// world's CharacterEnteredWorld / CharacterDirectoryUpdate messages and by presence (last world / zone / online).
    /// </summary>
    public class SocialCharacterModel
    {
        public ulong Guid { get; set; }
        public ulong AccountId { get; set; }
        /// <summary>The current name; looked up case-insensitively (column collation utf8mb4_unicode_ci).</summary>
        public string Name { get; set; } = "";
        /// <summary>The name before a rename (character.originalName), null when never renamed.</summary>
        public string OriginalName { get; set; }
        /// <summary>When the character was deleted (it keeps its row: guild logs keep names, P5312), null otherwise.</summary>
        public DateTime? DeletedAt { get; set; }
        /// <summary>The world (WorldServerConfiguration.ZoneServerId) the character was last on.</summary>
        public ulong LastWorld { get; set; }
        /// <summary>The landscape (zone id) the character was last on.</summary>
        public uint LastZone { get; set; }
        /// <summary>
        /// Name text ids of that landscape and world (StaticZoneData NameText, WorldServerConfiguration.WorldNameId), sent
        /// by the zone: the guild roster's Location "world:zone" (MemberGameData LastZone / LastWorld are text ids).
        /// </summary>
        public uint LastZoneNameText { get; set; }
        public uint LastWorldNameText { get; set; }
        /// <summary>When the character last left the game (UTC); the friends list's "last online" (FriendData u64).</summary>
        public DateTime? LastOnline { get; set; }
    }
}
