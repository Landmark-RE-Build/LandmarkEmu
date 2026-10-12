using System;

namespace LandmarkEmulator.Database.Character.Model
{
    /// <summary>
    /// A Player Studio product (Game.Commerce; docs/commerce.md): a player's design offered on the marketplace.
    /// Status: 0 waiting for peer review, 1 accepted (listed), 2 rejected, 3 removed by the owner.
    /// </summary>
    public class PlayerStudioProductModel
    {
        public ulong Id { get; set; }
        public ulong OwnerGuid { get; set; }
        /// <summary>The product's item: a design (ItemType 2) or a build site published to the gallery (ItemType 3, claim id).</summary>
        public ulong TemplateId { get; set; }
        public uint ItemType { get; set; } = 2;
        public string Name { get; set; }
        public string Description { get; set; }
        public string Headline { get; set; }
        public string Tags { get; set; }
        public uint Price { get; set; }
        public byte Status { get; set; }
        public uint Approvals { get; set; }
        public uint Rejections { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
