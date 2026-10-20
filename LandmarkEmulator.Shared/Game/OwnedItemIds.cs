namespace LandmarkEmulator.Shared.Game
{
    /// <summary>
    /// Ids for server-built items that are not in the live login list (Resources/PacketDumps/44-CommandBase).
    /// <see cref="Cosmetics"/> and the world server's ItemDefinitionBuilder both use this base.
    /// </summary>
    public static class OwnedItemIds
    {
        /// <summary>Far above every dump id (largest 4524) so a later find of a real id cannot collide.</summary>
        public const uint Base = 900000;
    }
}
