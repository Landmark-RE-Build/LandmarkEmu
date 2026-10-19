using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Network;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LandmarkEmulator.Shared.Game
{
    /// <summary>
    /// How equipped items look, for the login server's character list: equipment slot, model file and tint alias per
    /// item definition. Loaded from the item definitions packet (CommandBase 0x4B, Resources/PacketDumps/44-CommandBase)
    /// plus <see cref="Cosmetics.Outfits"/> (world-built Char_ clothing not in that dump). The world server's
    /// ItemDefinitionManager reads the same dump in full and builds Cosmetics from the same rows, so character select
    /// and in-world wear stay aligned. Entry layout: u32 id, 3 flag bytes, u32 name, u32 description, u32 image set,
    /// 4 × u32, u32 equip slot, string model, string, 5 × u32, 3 × u32, string tint alias, ...
    /// </summary>
    public static class ItemAppearances
    {
        public readonly record struct Appearance(uint EquipSlot, string Model, string TintAlias);

        /// <summary>Equipped inventory container (same value as the world server's Inventory.EquippedContainer).</summary>
        public const uint EquippedContainer = 2;

        private static Dictionary<uint, Appearance> appearances = new();

        public static Appearance? Get(uint itemDefinitionId) => appearances.TryGetValue(itemDefinitionId, out Appearance a) ? a : null;

        /// <summary>Overwrite or add one look (built clothing after the dump load).</summary>
        public static void Register(uint itemDefinitionId, uint equipSlot, string model, string tintAlias)
            => appearances[itemDefinitionId] = new Appearance(equipSlot, model ?? "", tintAlias ?? "");

        public static void Load(string packetPath)
        {
            var result = new Dictionary<uint, Appearance>();
            byte[] data = File.ReadAllBytes(packetPath);
            int offset = 4 + 4;
            uint count = BitConverter.ToUInt32(data, offset);
            offset += 4;
            for (uint i = 0; i < count; i++)
            {
                ushort compressed = BitConverter.ToUInt16(data, offset);
                ushort size = BitConverter.ToUInt16(data, offset + 2);
                offset += 4;
                byte[] item = Lz4Block.Decode(data.AsSpan(offset, compressed), size);
                offset += compressed;
                var reader = new GamePacketReader(item);
                uint id = reader.ReadUInt();
                reader.ReadBytes(3);
                for (int k = 0; k < 7; k++) reader.ReadUInt();
                uint slot = reader.ReadUInt();
                string model = reader.ReadString();
                reader.ReadString();
                for (int k = 0; k < 8; k++) reader.ReadUInt();
                string tint = reader.ReadString();
                result[id] = new Appearance(slot, model, tint);
            }
            appearances = result;
            RegisterBuiltClothing();
        }

        /// <summary>
        /// World-built outfits (<see cref="Cosmetics"/>) so character select can dress them the same way the Parlor and
        /// equipment packets do in-world.
        /// </summary>
        public static void RegisterBuiltClothing()
        {
            foreach (Cosmetics.Outfit outfit in Cosmetics.Outfits)
                Register(outfit.Id, Cosmetics.OutfitEquipSlot, outfit.ModelFile, outfit.Tint);
        }

        /// <summary>
        /// Body clothing from the character's last saved equipped inventory: Char_ models only (tools and weapons are
        /// not shown on character select). Same attachment fields the Parlor's EquippedAttachmentReply uses.
        /// </summary>
        public static IEnumerable<Appearance> BodyFromEquipped(IEnumerable<CharacterItemModel> items)
        {
            foreach (CharacterItemModel item in items.Where(i => i.Container == EquippedContainer).OrderBy(i => i.Slot))
                if (Get(item.ItemDefinitionId) is { } look && look.Model.StartsWith("Char_", StringComparison.Ordinal))
                    yield return look;
        }
    }
}
