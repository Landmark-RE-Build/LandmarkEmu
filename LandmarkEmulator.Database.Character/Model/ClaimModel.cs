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
        /// <summary>
        /// Access everyone else has ("Default Access", data source column PermissionsPublic), as the client's permission
        /// mask: the first of the three masks of ClaimsPacketSetItemBasicPermissions (0xF9/0x2D) and of the claim record
        /// (client claim entry +0x2d8).
        /// </summary>
        public uint DefaultPermissions { get; set; }
        /// <summary>
        /// The second Default Access mask ("PermissionsGroup", client claim entry +0x2dc), stored as the client sends it.
        /// The shipped Site Management window never sets it (it always arrives as 0) and the client's own permission
        /// checks never read it (FUN_143c17590 is always called with its group / guild flags false), so the server does
        /// not apply it either (docs/protocol.md §10, "Default Access").
        /// </summary>
        public uint GroupPermissions { get; set; }
        /// <summary>The third Default Access mask ("PermissionsGuild", client claim entry +0x2e0); as <see cref="GroupPermissions"/>.</summary>
        public uint GuildPermissions { get; set; }
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
        /// <summary>
        /// The owner left the site in Play Mode (F5), so its story starts again after a server restart (launch notes
        /// P4248 / P4351: a server restart no longer stopped creatures pathing; TASKS 6.56). Adventure rounds
        /// switch Play Mode too but are not saved.
        /// </summary>
        public bool PlayMode { get; set; }
        /// <summary>
        /// Play Mode Rules (0.695, "Adventure flags"): the owner's per-site rule bits, set from the client by
        /// Claims.SetClaimAdventureFlag (FUN_1435e6470 → ClaimsPacketSetAdventureFlags 0xF9/0x45, writer FUN_142da6bd0)
        /// and sent back in the claim record (FUN_142da1a50 record +0x3e0) and ClaimInfoUpdate kind 0x17
        /// (FUN_142da2840 +0x288), both landing on the client's claim entry +0x380. Only bits 0x3F are used. 0 (the
        /// default) = no rule set: voxel destruction allowed, no blocked tools, no forced camera.
        /// trace/0695/plan-storytools-next.md Spec 10.
        /// </summary>
        public ulong AdventureFlags { get; set; }
        /// <summary>
        /// Items held by the site that are not placed as props (the admin's /item claimAddItem, ClaimsPacketIdDebugAddItemClaim
        /// 0xF9/0x60; L146): "guid:item:tint:count" entries separated by ';'. The placed props' items are the rest of
        /// the site's inventory (world_prop.paidByItem). Empty = none.
        /// </summary>
        public string Inventory { get; set; } = "";
    }
}
