using System.Collections.Generic;

namespace LandmarkEmulator.Shared.Network.Message.Model.Shared
{
    /// <summary>
    /// The client's CharacterCustomizationData (0.695 reader FUN_142c18710, writer FUN_142f59020; constructor
    /// FUN_143b7b370): +0x00 slot list (SlotData {slot, option, tint}), +0x18 skin tint, +0x20 string, +0x80 race,
    /// +0x84 gender. Used in the character-select payload, SendSelf and the lightweight → full PC packet.
    /// </summary>
    public class CharacterModelInfo : IWritable, IReadable, ISize
    {
        public List<(uint, uint, uint)> Customisations { get; set; } = new();
        public uint SkinId { get; set; }
        /// <summary>
        /// +0x20 (StringFixed&lt;64&gt;): a material name applied to the whole character [I for the meaning, P for the
        /// path]. Its setter FUN_143b7b5d0 has one caller, FUN_142ec5b90 (character vtable slot 12, reached from
        /// FUN_1433381d0 FirstPersonLocalProxiedCharacter): it stores the name here and passes it to FUN_143d0b620 for the
        /// character's actor and for each attachment whose model has a material of that name (FUN_143da1800, upper-case
        /// hash), which swaps the matching material slot (FUN_143cfcea0 → FUN_143d9db70 → FUN_143cfd6b0). Character create
        /// never sets it (always "" in CharacterCreateRequest); the server sends "" (the default look).
        /// </summary>
        public string MaterialOverride { get; set; } = "";
        public uint Race { get; set; } = 1;
        public uint Gender { get; set; } = 1;

        public uint GetSize()
        {
            uint totalSize = 0u;

            totalSize += 4u; // Vector3s Count
            Customisations.ForEach(x => totalSize += 12u); // 3x Float

            return (uint)(totalSize + 4u + 4u + MaterialOverride.Length + 4u + 4u);
        }

        public void Read(GamePacketReader reader)
        {
            var customisationsCount = reader.ReadUInt();
            for (int i = 0; i < customisationsCount; i++)
            {
                var slot = reader.ReadUInt();
                var option = reader.ReadUInt();
                var tint = reader.ReadUInt();
                Customisations.Add(new(slot, option, tint));
            }
            SkinId = reader.ReadUInt();
            MaterialOverride = reader.ReadString();
            Race = reader.ReadUInt();
            Gender = reader.ReadUInt();
        }

        public void Write(GamePacketWriter writer)
        {
            writer.Write((uint)Customisations.Count);
            foreach (var v in Customisations)
            {
                writer.Write(v.Item1);
                writer.Write(v.Item2);
                writer.Write(v.Item3);
            }
            writer.Write(SkinId);
            writer.Write(MaterialOverride);
            writer.Write(Race);
            writer.Write(Gender);
        }
    }
}
