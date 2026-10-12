using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using System;
using System.Collections.Generic;
using NLog;
using LandmarkEmulator.Shared.Game;

namespace LandmarkEmulator.WorldServer.Network.Message.Model.Shared
{
    public class InventoryItem : IReadable, IWritable
    {
        protected static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Item definition header (FUN_142b87790, ClientItem +0x8): definition id, tint (+0xc, the client's item print
        /// "Tint"), two ItemRandom blocks (FUN_142b87690 at +0x10 and +0x68: quality, 4 u32, stats, catalysts; the item
        /// data source reads "Quality" from +0x10) and the quality's cost multiplier (+0xc0, f32 bits; RefItemQuality.txt
        /// COST_MULTIPLIER). The live server sent the definition's quality and its multiplier for every item.
        /// </summary>
        public class DefinitionData : IReadable, IWritable
        {
            // sub_142C20E60
            public class RandomData : IReadable, IWritable
            {
                /// <summary>
                /// ItemRandom::StatInfo (FUN_142b96180). Live values: {stat, text -1, f32 value, f32 1, true, f32 value, f32 1, f32 1}
                /// (the Adventurer's Pick: 3 → 100, 13 → 50, 12 → 80). The floats travel as their bits.
                /// </summary>
                public class StatInfo : IReadable, IWritable
                {
                    public uint StatId { get; set; }
                    public LandmarkText NameText { get; set; } = new();
                    public uint Value { get; set; }
                    public uint Multiplier { get; set; }
                    public bool Flag { get; set; }
                    public uint CurrentValue { get; set; }
                    public uint Multiplier2 { get; set; }
                    public uint Multiplier3 { get; set; }

                    public void Read(GamePacketReader reader)
                    {
                        StatId = reader.ReadUInt();
                        NameText = new LandmarkText(reader.ReadUInt());
                        Value = reader.ReadUInt();
                        Multiplier = reader.ReadUInt();
                        Flag = reader.ReadBool();
                        CurrentValue = reader.ReadUInt();
                        Multiplier2 = reader.ReadUInt();
                        Multiplier3 = reader.ReadUInt();
                    }

                    public void Write(GamePacketWriter writer)
                    {
                        writer.Write(StatId);
                        NameText.Write(writer);
                        writer.Write(Value);
                        writer.Write(Multiplier);
                        writer.Write(Flag);
                        writer.Write(CurrentValue);
                        writer.Write(Multiplier2);
                        writer.Write(Multiplier3);
                    }
                }

                /// <summary>ItemRandom::CatalystRandomInfo: 3 × u32.</summary>
                public class CatalystInfo : IReadable, IWritable
                {
                    public uint Unknown0 { get; set; }
                    public uint Unknown1 { get; set; }
                    public uint Unknown2 { get; set; }

                    public void Read(GamePacketReader reader)
                    {
                        Unknown0 = reader.ReadUInt();
                        Unknown1 = reader.ReadUInt();
                        Unknown2 = reader.ReadUInt();
                    }

                    public void Write(GamePacketWriter writer)
                    {
                        writer.Write(Unknown0);
                        writer.Write(Unknown1);
                        writer.Write(Unknown2);
                    }
                }

                public uint Quality { get; set; }
                public uint Unknown1 { get; set; }
                public uint Unknown2 { get; set; }
                public uint Unknown3 { get; set; }
                public uint Unknown4 { get; set; }
                public List<StatInfo> Stats { get; set; } = new();
                public List<CatalystInfo> Catalysts { get; set; } = new();

                public void Read(GamePacketReader reader)
                {
                    Quality = reader.ReadUInt();
                    Unknown1 = reader.ReadUInt();
                    Unknown2 = reader.ReadUInt();
                    Unknown3 = reader.ReadUInt();
                    Unknown4 = reader.ReadUInt();

                    uint unknown5Count = reader.ReadUInt();
                    for (int i = 0; i < unknown5Count; i++)
                    {
                        StatInfo obj = new();
                        obj.Read(reader);
                        Stats.Add(obj);
                    }

                    uint unknown6Count = reader.ReadUInt();
                    for (int i = 0; i < unknown6Count; i++)
                    {
                        CatalystInfo obj = new();
                        obj.Read(reader);
                        Catalysts.Add(obj);
                    }
                }

                public void Write(GamePacketWriter writer)
                {
                    writer.Write(Quality);
                    writer.Write(Unknown1);
                    writer.Write(Unknown2);
                    writer.Write(Unknown3);
                    writer.Write(Unknown4);

                    writer.Write((uint)Stats.Count);
                    foreach (StatInfo stats in Stats)
                        stats.Write(writer);

                    writer.Write((uint)Catalysts.Count);
                    foreach (CatalystInfo catalysts in Catalysts)
                        catalysts.Write(writer);

                }
            }

            public uint DefinitionId { get; set; }
            public uint Tint { get; set; }
            public RandomData Random { get; set; } = new();
            public RandomData Random2 { get; set; } = new();
            public uint CostMultiplier { get; set; }

            public void Read(GamePacketReader reader)
            {
                DefinitionId = reader.ReadUInt();
                Tint = reader.ReadUInt();

                Random.Read(reader);
                Random2.Read(reader);

                CostMultiplier = reader.ReadUInt();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(DefinitionId);
                writer.Write(Tint);

                Random.Write(writer);
                Random2.Write(writer);

                writer.Write(CostMultiplier);
            }
        }

        /// <summary>Optional block at ClientItem +0x120 (FUN_142b86340): u64, u32, u32. Not seen in the capture.</summary>
        public class SubData : IReadable, IWritable
        {
            public ulong Unknown0 { get; set; }
            public uint Unknown1 { get; set; }
            public uint Unknown2 { get; set; }

            public void Read(GamePacketReader reader)
            {
                Unknown0 = reader.ReadULong();
                Unknown1 = reader.ReadUInt();
                Unknown2 = reader.ReadUInt();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(Unknown0);
                writer.Write(Unknown1);
                writer.Write(Unknown2);
            }
        }

        /// <summary>
        /// Optional block at ClientItem +0x128 (FUN_142b9fa70): the design a Player Design item holds. Live:
        /// {design guid, "1 - EW Section", "Zhang"} = design id, design name, author name.
        /// </summary>
        public class SubData2 : IReadable, IWritable
        {
            public ulong Unknown0 { get; set; }
            public string Unknown1 { get; set; }
            public string Unknown2 { get; set; }

            public void Read(GamePacketReader reader)
            {
                Unknown0 = reader.ReadULong();
                Unknown1 = reader.ReadString();
                Unknown2 = reader.ReadString();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(Unknown0);
                writer.Write(Unknown1);
                writer.Write(Unknown2);
            }
        }

        public class SubData3 : IReadable, IWritable
        {
            public byte Unknown0 { get; set; }
            public uint Unknown1 { get; set; }
            public SubData Unknown2 { get; set; } = new();

            public void Read(GamePacketReader reader)
            {
                Unknown0 = reader.ReadByte();
                if (Unknown0 == 0)
                    return;

                Unknown1 = reader.ReadUInt();
                if (Unknown0 != 1)
                    return;

                Unknown2.Read(reader);
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(Unknown0);
                if (Unknown0 == 0)
                    return;

                writer.Write(Unknown1);
                if (Unknown0 != 1)
                    return;

                Unknown2.Write(writer);
            }
        }

        // sub_14361AAD0
        public class ExtraData : IReadable, IWritable
        {
            public byte Unknown0 { get; set; }

            public void Read(GamePacketReader reader)
            {
                // This data may parse different based on whether it's a weapon or not
                Unknown0 = reader.ReadByte();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(Unknown0);

                // TODO: Handle ExtraData not being 0.
                //if (Unknown0 != 0)
                //    throw new NotImplementedException();
            }
        }

        /// <summary>Item definition header (FUN_142b87790).</summary>
        public DefinitionData Definition { get; set; } = new();
        /// <summary>Item guid (ClientItem +0xc8).</summary>
        public ulong Guid { get; set; }
        /// <summary>Stack count (+0xd0).</summary>
        public uint Count { get; set; }
        /// <summary>ClientItem +0xd4.</summary>
        public uint Unknown2 { get; set; }
        /// <summary>ClientItem +0xd8.</summary>
        public uint Unknown3 { get; set; }
        /// <summary>ClientItem +0xdc (bool).</summary>
        public byte Unknown4 { get; set; }
        /// <summary>
        /// Inventory index (+0xe8, the client's InventoryIndex): 0 = bag, 2 = equipment, 5 = collection.
        /// </summary>
        public uint ContainerId { get; set; }
        /// <summary>Inventory id (+0xf0); the client prints it as InventoryId (FUN_143abd700).</summary>
        public ulong InventoryId { get; set; }
        public uint SlotId { get; set; }
        /// <summary>Owner type (+0xfc): 1 the player; 3 a chest (moved with TransferItemWithOwner).</summary>
        public uint OwnerType { get; set; }
        /// <summary>ClientItem +0x100 (u64).</summary>
        public ulong Unknown9 { get; set; }
        /// <summary>The chest holding the item (+0x108), ChestOwnerGuid in the client's item print.</summary>
        public ulong ChestOwnerGuid { get; set; }
        /// <summary>Slot in that chest (+0x110).</summary>
        public uint ChestSlot { get; set; }
        /// <summary>ClientItem +0x148: wear as float bits (Condition = (max - wear) / max, FUN_1435585b0).</summary>
        public uint Unknown12 { get; set; }
        /// <summary>ClientItem +0x14c (byte).</summary>
        public byte Unknown13 { get; set; }
        /// <summary>Seconds since the item's ability was last used (FUN_142b98a50 stores now - value as the last cast time; 0x7FFFFFFF = never). Feeds item ability cooldowns.</summary>
        public uint SecondsSinceLastCast { get; set; }
        public byte subData1Flag { get; set; }
        public SubData SubDataItem { get; set; }
        public SubData2 SubData2Item { get; set; }
        /// <summary>Item instance metadata (FUN_142b98ad0, ItemInstanceMetaData): bool present, u32 type, type 1 has a body.</summary>
        public SubData3 MetaData { get; set; } = new();
        /// <summary>Seconds until the item is deleted (+0x138, clamped at 0 and timestamped on read); matches the client's items.timed_delete_offset column.</summary>
        public uint TimedDeleteSeconds { get; set; }
        public ExtraData _ExtraData { get; set; } = new();

        public void Read(GamePacketReader reader)
        {
            Definition.Read(reader);

            Guid = reader.ReadULong();
            Count = reader.ReadUInt();
            Unknown2 = reader.ReadUInt();
            Unknown3 = reader.ReadUInt();
            Unknown4 = reader.ReadByte();
            ContainerId = reader.ReadUInt();
            InventoryId = reader.ReadULong();
            SlotId = reader.ReadUInt();
            OwnerType = reader.ReadUInt();
            Unknown9 = reader.ReadULong();
            ChestOwnerGuid = reader.ReadULong();
            ChestSlot = reader.ReadUInt();
            Unknown12 = reader.ReadUInt();
            Unknown13 = reader.ReadByte();
            SecondsSinceLastCast = reader.ReadUInt();

            byte hasSubData = reader.ReadByte();
            subData1Flag = hasSubData;
            if (hasSubData != 0)
            {
                SubDataItem = new();
                SubDataItem.Read(reader);
            }

            byte hasSubData2 = reader.ReadByte();
            if (hasSubData2 != 0)
            {
                SubData2Item = new();
                SubData2Item.Read(reader);
            }

            MetaData.Read(reader);

            TimedDeleteSeconds = reader.ReadUInt();

            _ExtraData.Read(reader);
        }

        /// <summary>
        /// The record plus the one byte the live server put after each item in the login packet (_ExtraData; the
        /// captured SendSelfToClient parses exactly with it).
        /// </summary>
        public void Write(GamePacketWriter writer)
        {
            WriteRecord(writer);
            _ExtraData.Write(writer);
        }

        /// <summary>
        /// The 0.589 item record exactly as the client's reader FUN_142b87110 reads it: it ends at the u32 after the
        /// metadata block. Use this wherever the record is followed by other fields or by the next record (trade
        /// AddItem, chest lists): with the extra byte the trade window dropped the update (the reader's caller wants
        /// no bytes left) and chest items after the first were misaligned (s2cprobe, 2026-10-12).
        /// </summary>
        public void WriteRecord(GamePacketWriter writer)
        {
            Definition.Write(writer);

            writer.Write(Guid);
            writer.Write(Count);
            writer.Write(Unknown2);
            writer.Write(Unknown3);
            writer.Write(Unknown4);
            writer.Write(ContainerId);
            writer.Write(InventoryId);
            writer.Write(SlotId);
            writer.Write(OwnerType);
            writer.Write(Unknown9);
            writer.Write(ChestOwnerGuid);
            writer.Write(ChestSlot);
            writer.Write(Unknown12);
            writer.Write(Unknown13);
            writer.Write(SecondsSinceLastCast);

            if (SubDataItem == null)
                writer.Write((byte)0);
            else
            {
                // TODO: Writer flag to indicate to client there is SubData1
                writer.Write((byte)1);
                SubDataItem.Write(writer);
            }

            if (SubData2Item == null)
                writer.Write((byte)0);
            else
            {
                // TODO: Writer flag to indicate to client there is SubData1
                writer.Write((byte)1);
                SubData2Item.Write(writer);
            }

            MetaData.Write(writer);

            writer.Write(TimedDeleteSeconds);
        }
    }
}
