namespace LandmarkEmulator.Database.Social.Model
{
    /// <summary>
    /// ignore_entry: a character on another character's ignore list (the client's Blocked list), with the ignored
    /// name as it was when added (IgnoreBase 0x72 List / Add carry it). social-service.md §5.2, from
    /// lmemu_character.character_ignore.
    /// </summary>
    public class IgnoreEntryModel
    {
        public ulong CharacterGuid { get; set; }
        public ulong IgnoredGuid { get; set; }
        public string Name { get; set; } = "";
    }
}
