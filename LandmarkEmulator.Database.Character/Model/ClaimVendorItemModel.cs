using System;

namespace LandmarkEmulator.Database.Character.Model
{
    /// <summary>
    /// One listing on a build site vendor (prop "Build Site_Vendor_Object"): items its owner put up for sale, at a
    /// price per item in coins. See Game.Vendors.
    /// </summary>
    public class ClaimVendorItemModel
    {
        /// <summary>The listing's id (an item guid, shown to the client as the row's item guid).</summary>
        public ulong Id { get; set; }
        /// <summary>The vendor prop's guid.</summary>
        public ulong VendorId { get; set; }
        public ulong OwnerGuid { get; set; }
        public uint ItemDefinitionId { get; set; }
        public uint Count { get; set; }
        /// <summary>Coins per item.</summary>
        public uint Price { get; set; }
        /// <summary>Listing fee paid (10% of the listed value).</summary>
        public uint FeePaid { get; set; }
        public DateTime ListedAt { get; set; }
    }
}
