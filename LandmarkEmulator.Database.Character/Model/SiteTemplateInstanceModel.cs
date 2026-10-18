using System;

namespace LandmarkEmulator.Database.Character.Model
{
    /// <summary>
    /// A Player Studio design placed on a build site (live's "template instance": client text 563373
    /// Construction.OnlyClaimOwnerMayPlaceOrCopyTemplateInstances "Only the Build Site owner may place, copy or create
    /// Designs using Designs purchased from the marketplace."). Visitors may not copy or save a design over its box.
    /// World data (the world's own database when it has one).
    /// </summary>
    public class SiteTemplateInstanceModel
    {
        public ulong Id { get; set; }
        /// <summary>The landscape (StaticZoneData zone id).</summary>
        public uint ZoneId { get; set; }
        /// <summary>The build site under the box's centre when it was placed (claim id).</summary>
        public ulong SiteGuid { get; set; }
        /// <summary>The placed design (template guid in the placer's designs).</summary>
        public ulong TemplateGuid { get; set; }
        /// <summary>Who placed it (the site's owner then).</summary>
        public ulong PlacedBy { get; set; }
        /// <summary>The placed box in world units (as build sites), corners inclusive.</summary>
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MinZ { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }
        public double MaxZ { get; set; }
        public DateTime PlacedAt { get; set; }
    }
}
