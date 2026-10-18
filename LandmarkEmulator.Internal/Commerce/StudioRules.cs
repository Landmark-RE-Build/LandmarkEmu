using System;

namespace LandmarkEmulator.Internal.Commerce
{
    /// <summary>
    /// Player Studio price rules shared by the world (in-process commerce, Game/Commerce.cs) and the commerce service
    /// (LandmarkEmulator.CommerceServer/ProductService.cs), so both refuse and accept the same prices (docs/commerce.md
    /// "The creator's price"). Prices are the creator's, in the units the client's submission window uses (Daybreak Cash
    /// on live; a buyer pays them × LUMENS_PER_DBC in Lumens).
    /// </summary>
    public static class StudioRules
    {
        /// <summary>
        /// Lowest price of any design: the client's own hard check (0.589 FUN_1431b7c20 / 0.695 FUN_143262c00: price
        /// &lt; 0x19 refuses the form) [P], any price above 25 SC (wiki Player Studio) [W]. Also
        /// the minimum price the server sends in CommerceConfiguration 0x2E (Game/Commerce.SendConfiguration).
        /// </summary>
        public const uint MinPrice = 25;

        /// <summary>
        /// Highest price of a design: the client's StringHashToValue "PlayerStudio.MaxListingValue^29600^" (0.695
        /// Resources/StringHashToValue.txt; PlayerStudioWindow turns the price red above it) [P value; the server-side
        /// check is ours, I].
        /// </summary>
        public const uint MaxPrice = 29600;

        /// <summary>
        /// A design's own minimum price, sent as its TemplateValue (TemplateBasicData u32 after the author string, record
        /// +0x110; 0.695 reader FUN_1430ff530, data source column "TemplateValue" part_0086.c:12279) [P field]. The
        /// submission's ProductAddItem (0.695 FUN_143263990) raises the product's minimum price (+0x28, the window's
        /// "Minimum Price") to it when it is above the configured minimum, and the price with it [P]. Its value: 1 % of
        /// the design's voxels, rounded down [I] — a player reported that a 604,200-voxel template needed at least 6042
        /// SC, with a 604 SC listing fee (Hotfix Notes 20 Jun 2014, design/forum/patches/2014-06-19-41689); another saw
        /// a minimum of 32 set by the item's voxel count (Game Update 14 Oct 2016, 2016-10-14-58029, the launch-era
        /// build). The client's own tooltip adds resource rarity: "The minimum price is determined by block
        /// count and resource rarity." (text 563163) — the rarity part is not found yet (OPEN-LEADS L148), so only the
        /// voxel count counts here. Live's other floor, no lower than the dearest template inside it (Hotfix Notes 20
        /// Jun 2014, Fairan [DEV]), needs template instances, which the server does not keep (not built).
        /// </summary>
        public static uint TemplateValue(long voxels) => voxels <= 0 ? 0 : (uint)Math.Min(uint.MaxValue, voxels / 100);

        /// <summary>The lowest price a design with <paramref name="voxels"/> voxels may be listed at (the client's rule: the higher of the two).</summary>
        public static uint MinimumPrice(long voxels) => Math.Max(MinPrice, TemplateValue(voxels));

        /// <summary>
        /// The listing fee exactly as the client computes it (0.695 FUN_143262c00, and the window's "ListingFee" column,
        /// FUN_14370d890): <c>max(minimum fee, (uint)(fee percent × 0.01 × price))</c>, the double 0.01 at 0x144c41100,
        /// multiplied left to right and truncated, then compared as signed ints [P]. Computing it the same way keeps the
        /// server's charge equal to what the client checked against its wallet and showed in the window.
        /// </summary>
        public static uint ListingFee(uint price, uint minimumFee, uint feePercent)
        {
            int percentFee = (int)(uint)((double)(int)feePercent * 0.01 * (double)(int)price);
            return (int)minimumFee < percentFee ? (uint)percentFee : minimumFee;
        }
    }
}
