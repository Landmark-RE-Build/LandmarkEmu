using LandmarkEmulator.Shared.Network;
using System;
using System.Collections.Generic;
using System.IO;

namespace LandmarkEmulator.Shared.Game
{
    /// <summary>
    /// How equipped items look, for the login server's character list: equipment slot, model file and tint alias per
    /// item definition, read from the item definitions packet (CommandBase 0x4B, Resources/PacketDumps/44-CommandBase;
    /// the world server's ItemDefinitionManager reads the same file in full). Entry layout: u32 id, 3 flag bytes,
    /// u32 name, u32 description, u32 image set, 4 × u32, u32 equip slot, string model, string, 5 × u32, 3 × u32,
    /// string tint alias, ...
    /// </summary>
    public static class ItemAppearances
    {
        public readonly record struct Appearance(uint EquipSlot, string Model, string TintAlias);

        private static Dictionary<uint, Appearance> appearances = new();

        public static Appearance? Get(uint itemDefinitionId) => appearances.TryGetValue(itemDefinitionId, out Appearance a) ? a : null;

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
        }
    }
}
