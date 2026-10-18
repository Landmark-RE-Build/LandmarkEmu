using System;

namespace LandmarkEmulator.WorldServer.Game.Inventory
{
    /// <summary>
    /// What an item instance carries besides its kind and count, and keeps when it changes hands or is split. Live kept
    /// item instances whole ([I]: the server's own durability model wears the instance, Durability.cs), so a trade, a mail
    /// attachment or a chest hands over the same item and its wear (durability points lost; the client's Condition from
    /// item record +0x148) comes with it (trace/0695/audit-wiki-gaps.md #1). Before, every hand-over rebuilt the item
    /// from its definition, which repaired it for free and skipped the Repair-o-Matic's Lumen cost.
    /// <para>
    /// <see cref="TradeableAfter"/>: a store purchase's waiting period (live's "fraud timer"; audit-wiki-gaps #7): until
    /// then the item can't be traded, mailed or put in a chest. It never leaves its owner, so only splits keep it.
    /// </para>
    /// <para>
    /// <see cref="AutoResupply"/>: the item's auto-resupply switch (Inventory 0x2D sub 0x0D; ClientItem +0xdc). It goes
    /// with the item through trades, chests and splits [I: live kept one item row whose auto_resupply column moved with
    /// it]. It does not decide stacking: the client's stack test (FUN_143b77f90 / FUN_143b789b0) never reads +0xdc [P].
    /// </para>
    /// </summary>
    public readonly record struct ItemState(float Wear = 0f, DateTime? TradeableAfter = null, bool AutoResupply = false)
    {
        /// <summary>No state: such items stack with any other plain stack of their kind (the auto-resupply switch aside).</summary>
        public bool IsPlain => Wear <= 0f && (TradeableAfter is not DateTime t || t <= DateTime.UtcNow);
    }
}
