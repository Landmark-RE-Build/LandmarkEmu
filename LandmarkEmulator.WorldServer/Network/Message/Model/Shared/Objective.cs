using LandmarkEmulator.Shared.Game;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model.Shared
{
    /// <summary>
    /// One entry of an achievement's objective list (HashList&lt;ObjectiveData&gt;, reader FUN_142c35990): the key, then
    /// the objective. The key is the objective id (the client files the objective under it, node +0x2190; live: always
    /// equal to <see cref="Objective.ObjectiveId"/>).
    /// </summary>
    public class ObjectiveEntry : IReadable, IWritable
    {
        public uint Index { get; private set; }
        public Objective Objective { get; private set; } = new();

        public void Read(GamePacketReader reader)
        {
            Index = reader.ReadUInt();
            Objective.Read(reader);
        }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(Index);
            Objective.Write(writer);
        }
    }

    /// <summary>
    /// The client's ObjectiveData (RTTI HashList&lt;ObjectiveData&gt;), shared by achievements and quests. Reader 0.695
    /// FUN_142c22ee0 (0.589 FUN_142b89710, same code); copies FUN_142e2bdd0 / FUN_142e2c9f0, writer FUN_142e27cf0.
    /// Offsets below are ObjectiveData offsets. Readers: the Journal objective rows FUN_143556ff0 ("Status", "Count",
    /// "Total", "StatusImageId", "Name", "Description"), the achievement row FUN_1435553a0, the objective sorter
    /// FUN_142e26d90, the quest objective activation FUN_142f2bcb0 (and FUN_142f2beb0 / FUN_142f2de30 / FUN_142f2e510).
    /// "never read [P]" = only the reader and the copies touch it (searched as pointer offset and as array index).
    /// Live values from the goal capture (Resources/PacketDumps/71-AchievementBase, 1,191 objectives).
    /// </summary>
    public class Objective : IReadable, IWritable
    {
        /// <summary>
        /// +0x2160..+0x216c (reader FUN_142c22e30): the objective's event bar. Quest objectives hand it to the event bar
        /// when they activate (FUN_142f2bcb0 → FUN_1431d81f0 → FUN_1431d7f00, "EventBar:SetupBar") [P]; the achievement
        /// code never reads it. Live: all four 0 on every goal objective.
        /// </summary>
        public class EventBarData : IReadable, IWritable
        {
            /// <summary>+0x2160 bar kind: 1, 2 or 5 make a bar; anything else (0) none (FUN_1431d7f00) [P].</summary>
            public uint BarType { get; set; }
            /// <summary>+0x2164 the bar's label text (resolved through the text table) [P].</summary>
            public LandmarkText LabelTextId { get; set; } = new();
            /// <summary>+0x2168 a value the bar is built with (its start value when the mode is 1) [P].</summary>
            public uint Value { get; set; }
            /// <summary>+0x216c mode: 1 = start from <see cref="Value"/> (FUN_1431d7f00) [P].</summary>
            public uint Mode { get; set; }

            public void Read(GamePacketReader reader)
            {
                BarType = reader.ReadUInt();
                LabelTextId = new LandmarkText(reader.ReadUInt());
                Value = reader.ReadUInt();
                Mode = reader.ReadUInt();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(BarType);
                LabelTextId.Write(writer);
                writer.Write(Value);
                writer.Write(Mode);
            }
        }

        /// <summary>+0x0 objective id.</summary>
        public uint ObjectiveId { get; private set; }
        /// <summary>+0x4 name text ("Name"; "##Objective name id" when missing, FUN_142e2d470) [P].</summary>
        public LandmarkText NameId { get; private set; } = new();
        /// <summary>+0x8 description text ("Description") [P].</summary>
        public LandmarkText DescriptionId { get; private set; } = new();
        /// <summary>+0x10 the objective's own ClientRewardBundle (the "ObjectiveRewards" column, FUN_143556d90) [P].</summary>
        public RewardBundle ObjectiveRewardBundle { get; private set; } = new();
        /// <summary>
        /// +0x2150 "Status": 0 not started, 1 active, 2 complete (also picks "StatusImageId"; the Journal filter counts
        /// 1-2 as started) [P]. The server patches it per character (Game.Achievements).
        /// </summary>
        public uint Status { get; private set; }
        /// <summary>+0x2154 current count ("Count", tracker "GoalCount") [P].</summary>
        public uint Count { get; private set; }
        /// <summary>+0x2158 required count ("Total"; the achievement's "ObjectivesCount" for one-objective goals) [P].</summary>
        public uint Total { get; private set; }
        /// <summary>+0x2170 sort order of the objective rows (compared by FUN_142e26d90) [P]. Live: per objective.</summary>
        public uint SortOrder { get; private set; }
        /// <summary>+0x215c u32. Never read [P]. Live: 0 on every objective.</summary>
        public uint Slot215C { get; private set; }
        /// <summary>
        /// +0xc a third text id. The achievement code never reads it [P]; the quest objective wrapper resolves it next to
        /// the name and description (FUN_142f28c90 → its +0x2270 string). Live: 576535 "This item can be crafted at the
        /// Replicator" on the 186 recipe tracker objectives, 576214 "Place Castle Courtyard design from your Build Mode
        /// Design tab [Shift-1]" and 577160 once each, 0 elsewhere: a hint line [I].
        /// </summary>
        public LandmarkText HintTextId { get; private set; } = new();
        /// <summary>+0x2160 event bar (quests only) [P].</summary>
        public EventBarData EventBar { get; private set; } = new();
        /// <summary>
        /// +0x2178 flags byte: bit 0x80 picks the other set of "StatusImageId" icons (FUN_143556ff0) [P]. Live: 0.
        /// </summary>
        public byte Flags { get; private set; }
        /// <summary>+0x2174 u32. Never read [P]. Live: 1 on 1,043 objectives, 0 on 148.</summary>
        public uint Slot2174 { get; private set; }

        public void Read(GamePacketReader reader)
        {
            ObjectiveId = reader.ReadUInt();
            NameId = new LandmarkText(reader.ReadUInt());
            DescriptionId = new LandmarkText(reader.ReadUInt());

            ObjectiveRewardBundle.Read(reader);

            Status = reader.ReadUInt();
            Count = reader.ReadUInt();
            Total = reader.ReadUInt();
            SortOrder = reader.ReadUInt();
            Slot215C = reader.ReadUInt();
            HintTextId = new LandmarkText(reader.ReadUInt());

            EventBar.Read(reader);

            Flags = reader.ReadByte();
            Slot2174 = reader.ReadUInt();
        }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(ObjectiveId);
            NameId.Write(writer);
            DescriptionId.Write(writer);

            ObjectiveRewardBundle.Write(writer);

            writer.Write(Status);
            writer.Write(Count);
            writer.Write(Total);
            writer.Write(SortOrder);
            writer.Write(Slot215C);
            HintTextId.Write(writer);

            EventBar.Write(writer);

            writer.Write(Flags);
            writer.Write(Slot2174);
        }
    }
}
