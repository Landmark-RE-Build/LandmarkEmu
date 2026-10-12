namespace LandmarkEmulator.Database.Character.Model
{
    /// <summary>
    /// Something a character has unlocked or chosen, as (kind, value): kind 1 = a title they own (value = title id),
    /// kind 2 = the title they display (one row), kind 3 = storage expansions used (value = expansion item id, one row
    /// per use). One table for all small per-character unlocks.
    /// </summary>
    public class CharacterUnlockModel
    {
        public ulong CharacterId { get; set; }
        public uint Kind { get; set; }
        public uint Value { get; set; }
    }
}
