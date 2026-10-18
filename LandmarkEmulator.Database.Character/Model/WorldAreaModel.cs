namespace LandmarkEmulator.Database.Character.Model
{
    /// <summary>
    /// A trigger volume ("area", AreaDefinition family 0xEA) made with the Area Tool — e.g. the box of a Linking Area.
    /// The record itself is kept exactly as the client reads it (<see cref="Data"/>: AreaDefinition wire bytes, reader
    /// FUN_1432f76a0 in the 0.695 client), so fields whose meaning is not traced yet survive a restart unchanged. See
    /// Game.AreaDefinitions and trace/0695/area-definitions-695.md.
    /// </summary>
    public class WorldAreaModel
    {
        /// <summary>The area guid (AreaDefinition +0x10).</summary>
        public ulong Id { get; set; }
        /// <summary>The landscape (zone) it belongs to; only players there are sent it.</summary>
        public uint ZoneId { get; set; }
        /// <summary>The character who made it.</summary>
        public ulong OwnerId { get; set; }
        /// <summary>The prop (Linking Area, prop 1590) that stands for it and carries its guid; 0 = none.</summary>
        public ulong PropId { get; set; }
        /// <summary>The AreaDefinition record as sent on the wire.</summary>
        public byte[] Data { get; set; } = System.Array.Empty<byte>();
    }
}
