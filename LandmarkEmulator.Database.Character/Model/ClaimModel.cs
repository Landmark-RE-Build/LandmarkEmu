namespace LandmarkEmulator.Database.Character.Model
{
    /// <summary>
    /// A build site claimed by a character: an axis-aligned box in world units (decimetres).
    /// </summary>
    public class ClaimModel
    {
        public ulong Id { get; set; }
        /// <summary>Owning character's entity guid (what the client compares against).</summary>
        public ulong OwnerGuid { get; set; }
        public string Name { get; set; } = "";
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MinZ { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }
        public double MaxZ { get; set; }
        /// <summary>Access everyone else has ("Default Access"), as the client's permission mask.</summary>
        public uint DefaultPermissions { get; set; }
        /// <summary>The landscape (StaticZoneData zone id) it is on.</summary>
        public uint ZoneId { get; set; } = 406;
        /// <summary>
        /// Time of day inside the site for its owner (Site Management "time" settings): hour 0-23, or -1 for the world's
        /// clock, and how fast it runs (1 = normal). Sent in the claim record (client claim +0xc8/+0xcc).
        /// </summary>
        public int TimeHour { get; set; } = -1;
        public float TimeRate { get; set; } = 1f;
        /// <summary>The same for everyone else in the site (client claim +0xd0/+0xd4).</summary>
        public int VisitorTimeHour { get; set; } = -1;
        public float VisitorTimeRate { get; set; } = 1f;
        /// <summary>Welcome message ("Your Build Site's Epic Story" in Build Site Management).</summary>
        public string Description { get; set; } = "";
        /// <summary>
        /// The site's tags (Lua Claims.SetClaimTags → ClaimsPacketSetTagString 0x2C; ClaimInfoUpdate kind 4). A site
        /// needs tags to be published to the gallery.
        /// </summary>
        public string Tags { get; set; } = "";
    }
}
