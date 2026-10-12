namespace LandmarkEmulator.Database.Character.Model
{
    /// <summary>
    /// An action bar slot a character set themselves (over the ActionSlotDefinitions defaults). Type as the client's
    /// ActionManager: 0 = empty, 1 = item definition id, 2 = item instance guid.
    /// </summary>
    public class CharacterActionSlotModel
    {
        public ulong CharacterId { get; set; }
        public uint BarId { get; set; }
        public uint SlotId { get; set; }
        public uint Type { get; set; }
        public ulong Value { get; set; }
    }
}
