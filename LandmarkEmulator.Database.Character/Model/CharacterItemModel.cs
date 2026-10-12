namespace LandmarkEmulator.Database.Character.Model
{
    /// <summary>
    /// One item stack owned by a character.
    /// </summary>
    public class CharacterItemModel
    {
        /// <summary>
        /// Item guid, unique across all characters (what the client calls the item's id).
        /// </summary>
        public ulong Id { get; set; }
        public ulong CharacterId { get; set; }
        public uint ItemDefinitionId { get; set; }
        public uint Count { get; set; }

        /// <summary>
        /// 0 = bag, 2 = equipped, 5 = resources, 8 = currency (as in the live game).
        /// </summary>
        public uint Container { get; set; }
        public uint Slot { get; set; }
        /// <summary>Wear (durability points lost); the client shows Condition = (max - wear) / max.</summary>
        public float Wear { get; set; }

        public CharacterModel Character { get; set; }
    }
}
