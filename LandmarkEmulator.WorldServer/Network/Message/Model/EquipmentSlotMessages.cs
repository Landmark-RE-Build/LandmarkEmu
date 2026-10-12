using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// Shows an item in one equipment slot of a character (e.g. the pick in the main hand). The client attaches
    /// <see cref="ModelName"/> to the character and records the item for its profile; an empty model only records it.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.EquipmentSetCharacterEquipmentSlotWire)]
    public class EquipmentSetCharacterEquipmentSlot : IWritable
    {
        public uint ProfileId { get; set; }
        public ulong CharacterGuid { get; set; }
        /// <summary>EquipmentSlotDefinitions id: 2 = main hand, 4 = feet, 5 = grappling hook, 8 = chest...</summary>
        public uint EquipSlotId { get; set; }
        public ulong ItemGuid { get; set; }
        public string ModelName { get; set; } = "";
        public string TextureAlias { get; set; } = "";
        public string TintAlias { get; set; } = "";
        public uint TintId { get; set; }
        /// <summary>
        /// The attachment's composite effect: a weapon's ambient glow (item definition u17, e.g. 11824
        /// PFX_Weapon_Ambient_Fire_Bow_01), played on the model while it is equipped.
        /// </summary>
        public uint CompositeEffect { get; set; }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(ProfileId);
            writer.Write(CharacterGuid);
            writer.Write(EquipSlotId);
            writer.Write(ItemGuid);
            writer.Write(""); // two names kept with the slot record; not needed to show the model
            writer.Write("");
            // Attachment, as in the character select payload.
            writer.Write(ModelName);
            writer.Write(TextureAlias);
            writer.Write(TintAlias);
            writer.Write(""); // decal alias
            writer.Write(TintId);
            writer.Write(CompositeEffect);
            writer.Write(EquipSlotId);
            writer.Write(0u);
            writer.Write(false);
        }
    }

    /// <summary>
    /// Empties one equipment slot of a character and removes its model.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.EquipmentUnsetCharacterEquipmentSlotWire)]
    public class EquipmentUnsetCharacterEquipmentSlot : IWritable
    {
        public uint ProfileId { get; set; }
        public ulong CharacterGuid { get; set; }
        public uint EquipSlotId { get; set; }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(ProfileId);
            writer.Write(CharacterGuid);
            writer.Write(EquipSlotId);
        }
    }
}
