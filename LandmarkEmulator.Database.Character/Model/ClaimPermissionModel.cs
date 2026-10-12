namespace LandmarkEmulator.Database.Character.Model
{
    /// <summary>
    /// A player's own access to a build site ("Special Access" in Site Management), as the client's permission mask:
    /// 0x003 Play Around, 0x007 Pillage &amp; Plunder, 0x107 Get Inspired, 0x10F Let's Decorate!, 0x11F Ultimate Power.
    /// </summary>
    public class ClaimPermissionModel
    {
        public ulong ClaimId { get; set; }
        public ulong CharacterId { get; set; }
        public uint Mask { get; set; }
    }
}
