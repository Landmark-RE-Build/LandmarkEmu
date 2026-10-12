using System.Linq;
using LandmarkEmulator.Shared.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;

namespace LandmarkEmulator.WorldServer.Game.Inventory
{
    /// <summary>
    /// What the server knows about each item definition, read from the item definitions packet the server sends at
    /// login (the live game's list). Only the fields the server needs are kept.
    /// </summary>
    public static class ItemDefinitionManager
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public class ItemDefinition
        {
            public uint Id { get; init; }
            public uint NameId { get; init; }
            /// <summary>
            /// Equipment slot (EquipmentSlotDefinitions.txt): 2 = main hand, 4 = feet, 5 = grappling hook, 8 = chest...
            /// </summary>
            public uint EquipSlotId { get; init; }
            public uint MaxStack { get; init; }
            public string ModelFile { get; init; }
            /// <summary>
            /// The weapon's ambient composite effect (record field u17, after the model strings): set on three weapons, e.g.
            /// 11824 PFX_Weapon_Ambient_Fire_Bow_01, 11825 ..Lightning_Sword2H_01, 11826 ..Physical_Sword1H_01.
            /// </summary>
            public uint AmbientEffect { get; init; }
            public uint DescriptionId { get; init; }
            public uint ImageSetId { get; init; }
            /// <summary>Item category ids (ReferenceData item categories), e.g. 2 for the pick.</summary>
            public List<uint> Categories { get; init; } = new();
            public string TintAlias { get; init; }
            /// <summary>
            /// Abilities the item gives. Type 3 is the primary use (the pick's swing, ability 1018); the client picks its
            /// tool mode (mining, logging, reaping) from which types the held item has.
            /// </summary>
            public List<ItemAbility> Abilities { get; init; } = new();
            /// <summary>The prop this item places (PropDefs id; item field u35_39[3]), 0 if none.</summary>
            public uint PropId { get; init; }
            /// <summary>
            /// DurabilitySet.txt id (definition +0x168; the 5th u32 after quality): 2 weapons, 3 armour, 0 = never wears
            /// (gathering tools, materials).
            /// </summary>
            public uint DurabilitySet { get; init; }
            /// <summary>Quality (RefItemQuality.txt: 0 common … 3, 4); colours the name and sets the cost multiplier.</summary>
            public uint Quality { get; init; }
            /// <summary>
            /// The definition exactly as in the definitions packet (u16 compressed size, u16 decompressed size, LZ4
            /// block). Tooltips carry it back to the client, which decompresses it (FUN_142b800a0).
            /// </summary>
            public byte[] RawData { get; set; }
        }

        public record ItemAbility(uint AbilityId, uint Type, string Input, string Category);

        private static Dictionary<uint, ItemDefinition> definitions = new();
        private static Dictionary<uint, (ItemDefinition, ItemAbility)> abilityIndex = new();

        public static ItemDefinition Get(uint id) => definitions.TryGetValue(id, out ItemDefinition d) ? d : null;
        public static int Count => definitions.Count;
        public static IEnumerable<ItemDefinition> All => definitions.Values;

        /// <summary>
        /// Reads the item definitions packet (opcode bytes, u32 size, u32 count, then per item a u16 compressed size,
        /// u16 decompressed size and an LZ4 block).
        /// </summary>
        public static void Load(string packetPath)
        {
            var result = new Dictionary<uint, ItemDefinition>();
            byte[] data = File.ReadAllBytes(packetPath);
            int offset = 4 + 4;
            uint count = BitConverter.ToUInt32(data, offset);
            offset += 4;
            for (uint i = 0; i < count; i++)
            {
                ushort compressed = BitConverter.ToUInt16(data, offset);
                ushort decompressed = BitConverter.ToUInt16(data, offset + 2);
                byte[] chunk = data.AsSpan(offset, 4 + compressed).ToArray();
                offset += 4;
                byte[] item = Lz4Block.Decode(data.AsSpan(offset, compressed), decompressed);
                offset += compressed;

                ItemDefinition definition = Parse(item);
                definition.RawData = chunk;
                result[definition.Id] = definition;
            }
            definitions = result;
            var index = new Dictionary<uint, (ItemDefinition, ItemAbility)>();
            foreach (ItemDefinition definition in result.Values)
                foreach (ItemAbility ability in definition.Abilities)
                    index.TryAdd(ability.AbilityId, (definition, ability));
            abilityIndex = index;
            log.Info($"Loaded {definitions.Count} item definitions.");
        }

        /// <summary>
        /// Reads the fields the server needs from one decompressed item definition: id, name/description text ids,
        /// image set, categories, equipment slot, stack size, model and tint, the prop it places, and its abilities
        /// (the rest of the record is skipped).
        /// </summary>
        private static ItemDefinition Parse(byte[] item)
        {
            var reader = new GamePacketReader(item);
            uint id = reader.ReadUInt();
            reader.ReadByte(); reader.ReadByte(); reader.ReadByte();
            uint nameId = reader.ReadUInt();
            uint descriptionId = reader.ReadUInt();
            uint imageSetId = reader.ReadUInt();
            for (int i = 0; i < 4; i++) reader.ReadUInt();
            uint equipSlot = reader.ReadUInt();
            string model = reader.ReadString();
            reader.ReadString();
            var u14 = new uint[5];
            for (int i = 0; i < 5; i++) u14[i] = reader.ReadUInt();
            reader.ReadUInt();                          // min stack
            reader.ReadUInt();
            uint maxStack = reader.ReadUInt();
            string tintAlias = reader.ReadString();
            for (int i = 0; i < 10; i++) reader.ReadUInt();
            reader.ReadString();
            reader.ReadString();
            var u35 = new uint[5];
            for (int i = 0; i < 5; i++) u35[i] = reader.ReadUInt();
            uint quality = reader.ReadUInt();
            var afterQuality = new uint[6];
            for (int i = 0; i < 6; i++) afterQuality[i] = reader.ReadUInt();
            var categories = new List<uint>();
            uint categoryCount = reader.ReadUInt();
            for (int i = 0; i < categoryCount; i++) categories.Add(reader.ReadUInt());
            for (int i = 0; i < 6; i++) reader.ReadUInt();
            var abilities = new List<ItemAbility>();
            uint abilityCount = reader.ReadUInt();
            for (int i = 0; i < abilityCount; i++)
            {
                uint abilityId = reader.ReadUInt();
                uint type = reader.ReadUInt();
                string input = reader.ReadString();
                string category = reader.ReadString();
                reader.ReadUInt();
                reader.ReadByte();
                abilities.Add(new ItemAbility(abilityId, type, input, category));
            }
            return new ItemDefinition
            {
                Id = id, NameId = nameId, DescriptionId = descriptionId, ImageSetId = imageSetId, Categories = categories, EquipSlotId = equipSlot, MaxStack = maxStack,
                ModelFile = model, TintAlias = tintAlias, Abilities = abilities, PropId = u35[3],
                DurabilitySet = afterQuality[4], Quality = quality, AmbientEffect = u14[3]
            };
        }

        /// <summary>Item definitions that place a given prop.</summary>
        public static IEnumerable<ItemDefinition> ItemsForProp(uint propId)
            => definitions.Values.Where(d => d.PropId == propId);

        /// <summary>
        /// The item that gives an ability, and the ability's entry on it; null when no item has it.
        /// </summary>
        public static (ItemDefinition Item, ItemAbility Ability)? FindAbility(uint abilityId)
            => abilityIndex.TryGetValue(abilityId, out var found) ? found : null;

        /// <summary>
        /// Minimal LZ4 block decoder (the format each item definition is compressed with).
        /// </summary>
        private static class Lz4Block
        {
            /// <summary>
            /// Decodes one LZ4 block into a buffer of the known decompressed size.
            /// </summary>
            public static byte[] Decode(ReadOnlySpan<byte> src, int outputSize)
            {
                var dst = new byte[outputSize];
                int s = 0, d = 0;
                while (s < src.Length)
                {
                    int token = src[s++];
                    int literals = token >> 4;
                    if (literals == 15)
                    {
                        int b;
                        do { b = src[s++]; literals += b; } while (b == 255);
                    }
                    src.Slice(s, literals).CopyTo(dst.AsSpan(d));
                    s += literals;
                    d += literals;
                    if (s >= src.Length)
                        break;

                    int matchOffset = src[s] | (src[s + 1] << 8);
                    s += 2;
                    int matchLength = token & 15;
                    if (matchLength == 15)
                    {
                        int b;
                        do { b = src[s++]; matchLength += b; } while (b == 255);
                    }
                    matchLength += 4;
                    for (int k = 0; k < matchLength; k++, d++)
                        dst[d] = dst[d - matchOffset];
                }
                if (d != outputSize)
                    throw new InvalidDataException($"LZ4 block decoded to {d} bytes, expected {outputSize}.");
                return dst;
            }
        }
    }
}
