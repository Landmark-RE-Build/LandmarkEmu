namespace LandmarkEmulator.Database.Character.Model
{
    /// <summary>
    /// One slot of a character's build palette (the radial menu): what the slot holds, as the client's palette entry
    /// {u32 slot, u32 type, u64 value} (PaletteBase AddEntry / PaletteUpdate).
    /// </summary>
    public class CharacterPaletteEntryModel
    {
        public ulong CharacterId { get; set; }
        public uint Slot { get; set; }
        public uint Type { get; set; }
        public ulong Value { get; set; }
    }
}
