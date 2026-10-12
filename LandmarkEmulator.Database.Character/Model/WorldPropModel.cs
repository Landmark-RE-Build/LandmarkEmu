namespace LandmarkEmulator.Database.Character.Model
{
    /// <summary>
    /// A prop placed in the world (furniture, crafting station, campfire). Position is in the zone's local frame.
    /// </summary>
    public class WorldPropModel
    {
        /// <summary>Entity guid shown to clients.</summary>
        public ulong Id { get; set; }
        /// <summary>PropDefs id (0 for props placed by using an item, which keep their model in ModelId).</summary>
        public uint PropId { get; set; }
        public uint ModelId { get; set; }
        public uint NameId { get; set; }
        /// <summary>Owning character's guid.</summary>
        public ulong OwnerGuid { get; set; }
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float PositionZ { get; set; }
        public float RotationX { get; set; }
        public float RotationY { get; set; }
        public float RotationZ { get; set; }
        public float RotationW { get; set; } = 1f;
        public float Scale { get; set; } = 1f;
        /// <summary>The landscape (StaticZoneData zone id) it is on.</summary>
        public uint ZoneId { get; set; } = 406;
        /// <summary>Prop tint alias ("GlobalTint_Coral"; empty = the model's own colours), set with the build tray's tint tab.</summary>
        public string TintAlias { get; set; } = "";
        /// <summary>The owner's own name for the prop (empty = its normal name).</summary>
        public string CustomName { get; set; } = "";
        /// <summary>Text shown on a sign prop.</summary>
        public string Text { get; set; } = "";
        /// <summary>Switched off (lights and effects dark).</summary>
        public bool Disabled { get; set; }
    }
}
