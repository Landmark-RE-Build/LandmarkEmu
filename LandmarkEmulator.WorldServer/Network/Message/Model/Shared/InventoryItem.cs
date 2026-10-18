using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using System.Collections.Generic;
using NLog;
using LandmarkEmulator.Shared.Game;

namespace LandmarkEmulator.WorldServer.Network.Message.Model.Shared
{
    /// <summary>
    /// One item record, the client's ClientItem (RTTI; base ItemRecord). Reader 0.695 FUN_142c208e0 (0.589
    /// FUN_142b87110, same code). The inventory takes the record over with FUN_143b78d70; the client's item print
    /// FUN_143b793d0 names several fields ("Guid, Definition, Name, TintOverride, Count, ConsumedCount, LastCastTime,
    /// RentalExpirationTime, RentalUpsellsSent, MarketplaceBundleId"), the other print FUN_143b79490 (ClientItem vtable
    /// +0x10) the rest (Tint, OwnerType, InventoryIndex, InventoryId, Slot, ChestOwnerGuid, ChestSlot). Offsets below are
    /// ClientItem offsets.
    /// Live values: the captured login packet (Resources/PacketDumps/SendSelf.bin, 24 items; docs/unknowns.md).
    /// </summary>
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
            /// <summary>
            /// The item's "random component" (ItemRecord::SetRandomComponent, FUN_143b780f0; reader 0.695 FUN_142c20e60):
            /// ItemRandom +0x0 quality, four u32, the StatInfo list and the CatalystRandomInfo list.
            /// </summary>
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

                /// <summary>
                /// ItemRandom::CatalystRandomInfo (RTTI): 3 × u32 (list reader FUN_142c38c90, element FUN_142c18530; kept
                /// at ClientItem +0x48). No reader of the three values found yet (searched: the item row and tooltip
                /// builders, the ItemRecord compare / copy, every function naming CatalystRandomInfo). Live: no captured
                /// item has one. The client's SQLite cache names a catalyst row catalyst_definition_id, slot_no, stat_id,
                /// upgrade_amount, so likely three of those [I, weak].
                /// </summary>
                public class CatalystInfo : IReadable, IWritable
                {
                    public uint Slot00 { get; set; }
                    public uint Slot04 { get; set; }
                    public uint Slot08 { get; set; }

                    public void Read(GamePacketReader reader)
                    {
                        Slot00 = reader.ReadUInt();
                        Slot04 = reader.ReadUInt();
                        Slot08 = reader.ReadUInt();
                    }

                    public void Write(GamePacketWriter writer)
                    {
                        writer.Write(Slot00);
                        writer.Write(Slot04);
                        writer.Write(Slot08);
                    }
                }

                /// <summary>ItemRandom +0x0 (ClientItem +0x10): the quality ("Quality"; must match for two items to stack).</summary>
                public uint Quality { get; set; }
                /// <summary>
                /// ItemRandom +0x4 (ClientItem +0x14): must be equal for two items to stack (ItemRecord compare
                /// FUN_143b77f90) [P]; nothing else reads it. Live: 0 on every item, in both blocks.
                /// </summary>
                public uint Slot04 { get; set; }
                /// <summary>ItemRandom +0x8. Copied with the block (FUN_143b780f0), never read [P]. Live: 0.</summary>
                public uint Slot08 { get; set; }
                /// <summary>ItemRandom +0xc. Copied, never read [P]. Live: 0.</summary>
                public uint Slot0C { get; set; }
                /// <summary>ItemRandom +0x10. Copied, never read [P]. Live: 0.</summary>
                public uint Slot10 { get; set; }
                public List<StatInfo> Stats { get; set; } = new();
                public List<CatalystInfo> Catalysts { get; set; } = new();

                public void Read(GamePacketReader reader)
                {
                    Quality = reader.ReadUInt();
                    Slot04 = reader.ReadUInt();
                    Slot08 = reader.ReadUInt();
                    Slot0C = reader.ReadUInt();
                    Slot10 = reader.ReadUInt();

                    uint statCount = reader.ReadUInt();
                    for (int i = 0; i < statCount; i++)
                    {
                        StatInfo obj = new();
                        obj.Read(reader);
                        Stats.Add(obj);
                    }

                    uint catalystCount = reader.ReadUInt();
                    for (int i = 0; i < catalystCount; i++)
                    {
                        CatalystInfo obj = new();
                        obj.Read(reader);
                        Catalysts.Add(obj);
                    }
                }

                public void Write(GamePacketWriter writer)
                {
                    writer.Write(Quality);
                    writer.Write(Slot04);
                    writer.Write(Slot08);
                    writer.Write(Slot0C);
                    writer.Write(Slot10);

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

        /// <summary>
        /// Optional rental block, ClientItem +0x120 (FUN_142c1fb10), named by the client's item print (FUN_143b793d0:
        /// "RentalExpirationTime: %lld, RentalUpsellsSent: %d, MarketplaceBundleId: %d") [P]. A rented item does not
        /// stack with an unrented one (FUN_143b789b0 / FUN_143b78c10). Live: no captured item has one; not sent.
        /// </summary>
        public class RentalData : IReadable, IWritable
        {
            /// <summary>+0x0 when the rental runs out ("RentalExpirationTime").</summary>
            public ulong RentalExpirationTime { get; set; }
            /// <summary>+0x8 "RentalUpsellsSent" (set through FUN_143b79ff0).</summary>
            public uint RentalUpsellsSent { get; set; }
            /// <summary>+0xc the store bundle it was rented from ("MarketplaceBundleId", set through FUN_143b79d20).</summary>
            public uint MarketplaceBundleId { get; set; }

            public void Read(GamePacketReader reader)
            {
                RentalExpirationTime = reader.ReadULong();
                RentalUpsellsSent = reader.ReadUInt();
                MarketplaceBundleId = reader.ReadUInt();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(RentalExpirationTime);
                writer.Write(RentalUpsellsSent);
                writer.Write(MarketplaceBundleId);
            }
        }

        /// <summary>
        /// Optional block at ClientItem +0x128 (FUN_142c395e0; the inventory copy keeps the u64 at +0x0 and the two
        /// strings at +0x8 / +0x20 through FUN_143b797f0 / FUN_143b796b0): the design a Player Design item holds. Live:
        /// {design guid, "1 - EW Section", "Zhang"} = design id, design name, author name.
        /// </summary>
        public class DesignData : IReadable, IWritable
        {
            public ulong DesignId { get; set; }
            public string DesignName { get; set; }
            public string AuthorName { get; set; }

            public void Read(GamePacketReader reader)
            {
                DesignId = reader.ReadULong();
                DesignName = reader.ReadString();
                AuthorName = reader.ReadString();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(DesignId);
                writer.Write(DesignName);
                writer.Write(AuthorName);
            }
        }

        /// <summary>
        /// Body of metadata type 1, ItemInstanceMetaCharacaterGuid (RTTI, sic; made by FUN_143b79230, read by
        /// FUN_142c20cb0 into its +0x10 / +0x18 / +0x1c). Only the item writer FUN_14350c1c0 reads it back [P].
        /// </summary>
        public class MetaCharacterGuid : IReadable, IWritable
        {
            /// <summary>+0x10 the character guid the class is named after.</summary>
            public ulong CharacterGuid { get; set; }
            /// <summary>+0x18 u32. Never read [P].</summary>
            public uint Slot18 { get; set; }
            /// <summary>+0x1c u32. Never read [P].</summary>
            public uint Slot1C { get; set; }

            public void Read(GamePacketReader reader)
            {
                CharacterGuid = reader.ReadULong();
                Slot18 = reader.ReadUInt();
                Slot1C = reader.ReadUInt();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(CharacterGuid);
                writer.Write(Slot18);
                writer.Write(Slot1C);
            }
        }

        /// <summary>
        /// Item instance metadata, ClientItem +0x130 (ItemInstanceMetaData; reader 0.695 FUN_142c32650, 0.589
        /// FUN_142b98ad0): u8 present; when non-zero a u32 type (FUN_143b79230 makes the object: 1 =
        /// ItemInstanceMetaCharacaterGuid, any other type none) and, for type 1 only, its body [P]. Live: absent on every
        /// item.
        /// </summary>
        public class SubData3 : IReadable, IWritable
        {
            /// <summary>Non-zero = a metadata block follows.</summary>
            public byte HasMetaData { get; set; }
            /// <summary>The metadata class: 1 = ItemInstanceMetaCharacaterGuid.</summary>
            public uint MetaDataType { get; set; }
            /// <summary>Type 1's body.</summary>
            public MetaCharacterGuid CharacterGuidData { get; set; } = new();

            public void Read(GamePacketReader reader)
            {
                HasMetaData = reader.ReadByte();
                if (HasMetaData == 0)
                    return;

                MetaDataType = reader.ReadUInt();
                // The client reads the body for type 1 only (FUN_142c32650). This used to test the present byte
                // instead, which agreed only while both were 1.
                if (MetaDataType != 1)
                    return;

                CharacterGuidData.Read(reader);
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(HasMetaData);
                if (HasMetaData == 0)
                    return;

                writer.Write(MetaDataType);
                if (MetaDataType != 1)
                    return;

                CharacterGuidData.Write(writer);
            }
        }

        /// <summary>
        /// The byte after each item of the SendSelf inventory list: the list reader (0.695 FUN_142c47af0) adds the item
        /// and then calls the item's vtable +0x38 with the stream (lambda FUN_142d609d0), which for a ClientItem is
        /// FUN_142e46670: one bool into ClientItem +0x161 [P]. No reader of ClientItem +0x161 found (the other +0x161
        /// readers in the decompile belong to other classes). Live: 1 on Lightstone (231) and Cozy Campfire (3894), 0
        /// on the other 22 items: both are right-click consumables with an ability, but the meaning is not proven yet.
        /// The server sends 0.
        /// </summary>
        public class ExtraData : IReadable, IWritable
        {
            /// <summary>ClientItem +0x161.</summary>
            public bool Flag161 { get; set; }

            public void Read(GamePacketReader reader)
            {
                Flag161 = reader.ReadBool();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(Flag161);
            }
        }

        /// <summary>Item definition header (FUN_142b87790).</summary>
        public DefinitionData Definition { get; set; } = new();
        /// <summary>Item guid (ClientItem +0xc8).</summary>
        public ulong Guid { get; set; }
        /// <summary>Stack count (+0xd0).</summary>
        public uint Count { get; set; }
        /// <summary>
        /// ClientItem +0xd4 "ConsumedCount" (the client's item print FUN_143b793d0) [P]: how many of the stack are used
        /// up; kept between 0 and the count, and added together when stacks merge (FUN_143b789b0). Live: 0 on every item.
        /// </summary>
        public uint ConsumedCount { get; set; }
        /// <summary>
        /// ClientItem +0xd8 u32: a positive value keeps the item from stacking with items without one, the same rule as
        /// the rental block (FUN_143b789b0 / FUN_143b78c10) [P]; nothing else reads it. Live: 0 on every item.
        /// </summary>
        public uint Slot0D8 { get; set; }
        /// <summary>
        /// ClientItem +0xdc: auto-resupply on. The client flips it and sends Inventory 0x2D sub 0x0D
        /// InventoryPacketToggleAutoResupply {u64 item guid, bool} (FUN_142f261b0) [P]. Live: false on every item. The
        /// server sends the switch it saved for the item (CharacterItemModel.AutoResupply; InventoryHandler
        /// HandleToggleAutoResupply). Kept by the record copies (FUN_143b78d70, FUN_143f8a080) and written back by the
        /// client's item record writers (FUN_14350baa0, FUN_143b818d0); only the uncalled getter FUN_142f21ff0 reads it [P].
        /// </summary>
        public bool AutoResupply { get; set; }
        /// <summary>
        /// Inventory index (+0xe8, the client's InventoryIndex): 0 = bag, 2 = equipment, 5 = collection.
        /// </summary>
        public uint ContainerId { get; set; }
        /// <summary>Inventory id (+0xf0); the client prints it as InventoryId (FUN_143abd700).</summary>
        public ulong InventoryId { get; set; }
        public uint SlotId { get; set; }
        /// <summary>Owner type (+0xfc): 1 the player; 3 a chest (moved with TransferItemWithOwner).</summary>
        public uint OwnerType { get; set; }
        /// <summary>
        /// u64 the reader puts at +0x100 of its temporary record; the inventory copy (FUN_143b78d70) does not take it
        /// over, so nothing reads it [P]. Live: 0 on every item.
        /// </summary>
        public ulong Slot100 { get; set; }
        /// <summary>The chest holding the item (+0x108), ChestOwnerGuid in the client's item print.</summary>
        public ulong ChestOwnerGuid { get; set; }
        /// <summary>Slot in that chest (+0x110).</summary>
        public uint ChestSlot { get; set; }
        /// <summary>
        /// ClientItem +0x148: the wear, as float bits. The item row's "Condition" comes from it and the definition's
        /// durability (+0x168) through FUN_14360c0b0 (FUN_142f258f0) [P]. Live: 0 (unworn).
        /// </summary>
        public uint Wear { get; set; }
        /// <summary>
        /// ClientItem +0x14c flags [P]. 0x80: stacks of one only, the definition's stack size is ignored (FUN_143b79610,
        /// FUN_142e4aa70; FUN_143b79950 sets it from a bool). 0x40: no warning when the item lands in the overflow
        /// inventory (container 7; without it the client opens "Inventory Warning!", FUN_142f22d40 → FUN_142f1e580).
        /// Live: 0 on every item, so the overflow warning shows; the server sends 0.
        /// </summary>
        public byte Flags { get; set; }
        /// <summary>Seconds since the item's ability was last used (FUN_142b98a50 stores now - value as the last cast time; 0x7FFFFFFF = never). Feeds item ability cooldowns.</summary>
        public uint SecondsSinceLastCast { get; set; }
        /// <summary>Rental block (ClientItem +0x120), sent when not null.</summary>
        public RentalData Rental { get; set; }
        /// <summary>Player Design block (ClientItem +0x128), sent when not null.</summary>
        public DesignData Design { get; set; }
        /// <summary>Item instance metadata (ClientItem +0x130), see <see cref="SubData3"/>.</summary>
        public SubData3 MetaData { get; set; } = new();
        /// <summary>Seconds until the item is deleted (+0x138, clamped at 0 and timestamped on read); matches the client's items.timed_delete_offset column.</summary>
        public uint TimedDeleteSeconds { get; set; }
        /// <summary>The byte the SendSelf inventory list reads after the record (<see cref="ExtraData"/>).</summary>
        public ExtraData Extra { get; set; } = new();

        public void Read(GamePacketReader reader)
        {
            Definition.Read(reader);

            Guid = reader.ReadULong();
            Count = reader.ReadUInt();
            ConsumedCount = reader.ReadUInt();
            Slot0D8 = reader.ReadUInt();
            AutoResupply = reader.ReadBool();
            ContainerId = reader.ReadUInt();
            InventoryId = reader.ReadULong();
            SlotId = reader.ReadUInt();
            OwnerType = reader.ReadUInt();
            Slot100 = reader.ReadULong();
            ChestOwnerGuid = reader.ReadULong();
            ChestSlot = reader.ReadUInt();
            Wear = reader.ReadUInt();
            Flags = reader.ReadByte();
            SecondsSinceLastCast = reader.ReadUInt();

            if (reader.ReadByte() != 0)
            {
                Rental = new();
                Rental.Read(reader);
            }

            if (reader.ReadByte() != 0)
            {
                Design = new();
                Design.Read(reader);
            }

            MetaData.Read(reader);

            TimedDeleteSeconds = reader.ReadUInt();

            Extra.Read(reader);
        }

        /// <summary>
        /// The record plus the one byte the client reads after each item of the login packet's inventory list
        /// (<see cref="Extra"/>; the captured SendSelfToClient parses exactly with it).
        /// </summary>
        public void Write(GamePacketWriter writer)
        {
            WriteRecord(writer);
            Extra.Write(writer);
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
            writer.Write(ConsumedCount);
            writer.Write(Slot0D8);
            writer.Write(AutoResupply);
            writer.Write(ContainerId);
            writer.Write(InventoryId);
            writer.Write(SlotId);
            writer.Write(OwnerType);
            writer.Write(Slot100);
            writer.Write(ChestOwnerGuid);
            writer.Write(ChestSlot);
            writer.Write(Wear);
            writer.Write(Flags);
            writer.Write(SecondsSinceLastCast);

            writer.Write((byte)(Rental == null ? 0 : 1));
            Rental?.Write(writer);

            writer.Write((byte)(Design == null ? 0 : 1));
            Design?.Write(writer);

            MetaData.Write(writer);

            writer.Write(TimedDeleteSeconds);
        }
    }
}
