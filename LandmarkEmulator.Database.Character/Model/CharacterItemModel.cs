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
        /// <summary>
        /// A store purchase's waiting period (UTC; null = none): until then the item can't be traded, mailed or put in a
        /// chest (live's "fraud timer", client texts 577061 / 562759; STORE_TRADE_WAIT_HOURS).
        /// </summary>
        public System.DateTime? TradeableAfter { get; set; }
        /// <summary>
        /// When an overflow item is deleted (UTC; null = never / not in overflow): OVERFLOW_EXPIRY_HOURS after it arrived
        /// (wiki Inventory: 24 hours). The client counts it down from the item record's timed-delete seconds.
        /// </summary>
        public System.DateTime? ExpiresAt { get; set; }
        /// <summary>
        /// The item's auto-resupply switch (the client's ClientItem +0xdc, item record byte after the u32 at +0xd8): set by
        /// the client's InventoryPacketToggleAutoResupply (Inventory 0x2D sub 0x0D) and sent back in the item record. The
        /// live server kept it per item: its item table had an <c>auto_resupply</c> column (the column name list in the
        /// 0.695 client, trace/client-result-tables.md).
        /// </summary>
        public bool AutoResupply { get; set; }

        public CharacterModel Character { get; set; }
    }
}
