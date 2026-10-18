using LandmarkEmulator.Shared.Game;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Network.Message.Model.Shared
{
    /// <summary>
    /// One achievement ("goal") record, the client's AchievementData (List / HashList&lt;AchievementData&gt; RTTI). Reader
    /// 0.695 FUN_142c161b0 (0.589 FUN_142b7ca00, same code), used by AchievementBase 0x92 sub 2 Add, sub 3 Initialize and
    /// sub 0x0E SecretComplete, and by the SendSelfToClient achievement block (player +0x38658). The constructor is
    /// FUN_142c5b840, the copy FUN_142e2b330, the destructor FUN_142c765c0. Offsets below are AchievementData offsets.
    ///
    /// Who reads what (0.695): the Journal row data source FUN_1435553a0 (columns in quotes), the AchievementInfo totals
    /// FUN_14355cdb0, the Journal sorters FUN_143552b80..FUN_1435537f0, the filter FUN_143553410, the goal tracker
    /// FUN_14355d070 / FUN_14355baa0 / FUN_14355bc20, AchievementAdd FUN_142e2d470 and AchievementComplete FUN_142e2e390
    /// (dispatcher FUN_142e30720). "never read [P]" means: only the reader, the copy, the constructor and the writer
    /// (FUN_142e275a0) touch the offset — searched as pointer offset and as array index over the whole 0.695 decompile.
    ///
    /// Live values: the Feb 2017 AchievementInitialize capture (Resources/PacketDumps/71-AchievementBase, 898 records),
    /// which <see cref="Game.Achievements"/> sends as recorded; this model is what the SendSelf block uses (sent empty).
    /// The defaults here are the values every live record had.
    /// </summary>
    public class Achievement : IReadable, IWritable
    {
        /// <summary>
        /// +0x22a8 Array&lt;int&gt; (reader FUN_142c34b60): achievement ids shown as the "Dependency" column, each as
        /// "id^name^quality*" (FUN_1435553a0) [P]. Live: 57 records have one entry (510, 526, 542, 595, 618, ...: the
        /// goal tier before them).
        /// </summary>
        public class DependencyList : IReadable, IWritable
        {
            public List<uint> AchievementIds { get; set; } = new();

            public void Read(GamePacketReader reader)
            {
                uint count = reader.ReadUInt();
                for (int i = 0; i < count; i++)
                    AchievementIds.Add(reader.ReadUInt());
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write((uint)AchievementIds.Count);
                foreach (uint id in AchievementIds)
                    writer.Write(id);
            }
        }

        /// <summary>
        /// AchievementDefinition::ObjectiveItemHint (RTTI; 16 bytes, element reader FUN_142c23120): an item that
        /// satisfies an objective. Read by the "ObjectivesItems" column builder FUN_143556840 and the tracker's
        /// "Item Requirement" rows (FUN_14355d070: "GoalItemDefId", "GoalTotal", "GoalImageSetId") [P].
        /// </summary>
        public class ObjectiveItemHint : IReadable, IWritable
        {
            /// <summary>+0x0 item definition id ("GoalItemDefId") [P].</summary>
            public uint ItemDefinitionId { get; set; }
            /// <summary>+0x4 how many are needed ("GoalTotal"; the column shows it less the objective's count) [P].</summary>
            public uint Count { get; set; }
            /// <summary>+0x8 image set shown for it ("GoalImageSetId") [P].</summary>
            public uint ImageSetId { get; set; }
            /// <summary>
            /// +0xc recipe id: when set, the hint lists that recipe's components (looked up in the recipe table at game
            /// +0x32260, quantities divided by the recipe's output count) [P]. Live: 0 on every hint.
            /// </summary>
            public uint RecipeId { get; set; }

            public void Read(GamePacketReader reader)
            {
                ItemDefinitionId = reader.ReadUInt();
                Count = reader.ReadUInt();
                ImageSetId = reader.ReadUInt();
                RecipeId = reader.ReadUInt();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(ItemDefinitionId);
                writer.Write(Count);
                writer.Write(ImageSetId);
                writer.Write(RecipeId);
            }
        }

        /// <summary>
        /// +0x22c0 HashListMap&lt;int, Array&lt;AchievementDefinition::ObjectiveItemHint&gt;&gt; (reader FUN_142c363d0): per
        /// objective id, the items that satisfy it [P]. Live: 786 records carry hints, e.g. {2433, 1, 1744, 0}.
        /// </summary>
        public class ObjectiveItemHintMap : IReadable, IWritable
        {
            public List<(uint ObjectiveId, List<ObjectiveItemHint> Hints)> Entries { get; set; } = new();

            public void Read(GamePacketReader reader)
            {
                uint count = reader.ReadUInt();
                for (int i = 0; i < count; i++)
                {
                    uint objective = reader.ReadUInt();
                    var hints = new List<ObjectiveItemHint>();
                    uint hintCount = reader.ReadUInt();   // Array reader FUN_142c39f80
                    for (int h = 0; h < hintCount; h++)
                    {
                        var hint = new ObjectiveItemHint();
                        hint.Read(reader);
                        hints.Add(hint);
                    }
                    Entries.Add((objective, hints));
                }
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write((uint)Entries.Count);
                foreach (var (objective, hints) in Entries)
                {
                    writer.Write(objective);
                    writer.Write((uint)hints.Count);
                    foreach (ObjectiveItemHint hint in hints)
                        hint.Write(writer);
                }
            }
        }

        /// <summary>
        /// +0x22f0 HashListMap&lt;int,int&gt; (reader FUN_142c34f90: first u32 = key, node +0x18; second = value, node +0):
        /// objective id → the achievement that objective stands for. The values are the "RequiredAchievements" column
        /// ("id^name^quality*", FUN_1435553a0); the objective rows look their own id up as the key and show the value as
        /// "ObjectiveAchievementId" (FUN_143556ff0) [P]. Live: meta goals, e.g. 595 Slayer of All the Things
        /// 1037 → 574 ... 1057 → 594.
        /// </summary>
        public class RequiredAchievementMap : IReadable, IWritable
        {
            public List<(uint ObjectiveId, uint AchievementId)> Entries { get; set; } = new();

            public void Read(GamePacketReader reader)
            {
                uint count = reader.ReadUInt();
                for (int i = 0; i < count; i++)
                    Entries.Add((reader.ReadUInt(), reader.ReadUInt()));
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write((uint)Entries.Count);
                foreach (var (objective, achievement) in Entries)
                {
                    writer.Write(objective);
                    writer.Write(achievement);
                }
            }
        }

        /// <summary>+0x0 achievement id.</summary>
        public uint AchievementId { get; set; }
        /// <summary>
        /// +0x4 flags byte. Bit 0x10 set = not active today: the Journal's "ActiveToday" column is
        /// !(flags &amp; 0x10) (FUN_1435553a0) [P]; no other bit is read. Live: 0 on every record (the server only sends the
        /// Daily goals a character has today, so 0 is right).
        /// </summary>
        public byte Flags { get; set; }
        /// <summary>+0x8 name text ("Name" via the manager's name table, FUN_142e2fb30) [P].</summary>
        public LandmarkText NameId { get; set; } = new();
        /// <summary>+0xc description text ("Description", FUN_142e2fa90) [P].</summary>
        public LandmarkText DescriptionId { get; set; } = new();
        /// <summary>+0x10 start time, Unix seconds ("StartTimeStamp"; 0 = not started) [P].</summary>
        public ulong TimeStarted { get; set; }
        /// <summary>+0x18 completion time, Unix seconds ("FinishTimeStamp"; "IsComplete" = not the zero time) [P].</summary>
        public ulong TimeFinished { get; set; }
        /// <summary>+0x20 completion fraction.</summary>
        public float Progress { get; set; }

        /// <summary>+0x28 ClientRewardBundle: "HasReward" = it has an entry, "Rewards" lists the entries [P].</summary>
        public RewardBundle AchievementRewardBundle { get; set; } = new();

        /// <summary>+0x2168 HashList&lt;ObjectiveData&gt; (reader FUN_142c35990): {u32 objective id (key), objective}.</summary>
        public uint ObjectiveCount { get; private set; }
        public List<ObjectiveEntry> AchievementObjectives { get; set; } = new();

        /// <summary>
        /// +0x2258 IconData (vtable set at +0x2250 by the constructor) first field, the icon. Never read [P]: the Journal
        /// shows "ForegroundImageId" / "BackgroundImageId" instead. Live: 0 on every record.
        /// </summary>
        public uint IconId { get; set; }
        /// <summary>+0x225c IconData second field (tint, by the IconData layout [I]). Never read [P]. Live: 0.</summary>
        public uint IconTintId { get; set; }
        /// <summary>
        /// +0x2260 trophy ("TrophyId", FUN_1435553a0) [P]: AchievementTrophies.txt, which is empty in 0.695. Live: 0.
        /// </summary>
        public uint TrophyId { get; set; }
        /// <summary>
        /// +0x2264 Luminary Score points ("Points"; AchievementInfo "TotalPoints" sums them over completed goals,
        /// FUN_14355cdb0; also a Journal sort key) [P]. Live: 0, 5, 10, 15 or 20.
        /// </summary>
        public uint Points { get; set; }
        /// <summary>
        /// +0x2268 subcategory, AchievementSubcategories.txt ("SubcategoryGuid", and through it "CategoryGuid" /
        /// "TabGuid") [P]. Live: 29 subcategories (242 Recipe Discoveries on 569 records).
        /// </summary>
        public uint SubcategoryId { get; set; }
        /// <summary>
        /// +0x226c signed byte (stored as an int). Never read [P]. Live: 2 on the two captured Daily goals (669, 679),
        /// 0 elsewhere.
        /// </summary>
        public sbyte Slot226C { get; set; }
        /// <summary>+0x2270 u32. Never read [P]. Live: 0.</summary>
        public uint Slot2270 { get; set; }
        /// <summary>
        /// +0x2274: completing the goal also unlocks the Steam achievement named by its id (AchievementComplete →
        /// FUN_142fdd250 → SteamUserStats GetAchievement / SetAchievement, FUN_142fdd260) [P]. Live: false on every
        /// record.
        /// </summary>
        public bool UnlocksSteamAchievement { get; set; }
        /// <summary>
        /// +0x2278 ActorCompositeEffect played on the local player when AchievementComplete arrives (the dispatcher looks
        /// it up in ActorCompositeEffectDefinitionManager and plays it, FUN_142e30720 case 4) [P]. Live: 0 on every record.
        /// </summary>
        public uint CompletionCompositeEffectId { get; set; }
        /// <summary>
        /// +0x227c AchievementQuality.txt id ("QualityId"; its image → "QualityImageId" / "CardImageId"; a Journal sort
        /// key and filter) [P]. Live: 13 on the recipe goals, 7-12 on the rest.
        /// </summary>
        public uint QualityId { get; set; }
        /// <summary>+0x2280 "VisibilityType" [P]; the constructor's default is 4. Live: 4 on every record.</summary>
        public uint VisibilityType { get; set; } = 4;
        /// <summary>+0x2284 hint text: AchievementAdd files it in the manager's "Hint" table (FUN_142e2d470, FUN_142e2fae0) [P]. Live: 0.</summary>
        public LandmarkText HintTextId { get; set; } = new();
        /// <summary>+0x2288 u32. Never read [P]. Live: 0.</summary>
        public uint Slot2288 { get; set; }
        /// <summary>+0x228c u32. Never read [P]. Live: 0.</summary>
        public uint Slot228C { get; set; }
        /// <summary>
        /// +0x2290 u32. Never read [P]. Live: 477 → 478, 478 → 480, 685 → 477, i.e. the goal before it in the Getting
        /// Started chain 471 → 480 → 478 → 477 → 685 [I] (480 itself has 0); 0 elsewhere.
        /// </summary>
        public uint Slot2290 { get; set; }
        /// <summary>+0x2294 "ForegroundImageId" (the Journal card image) [P]. Live: 682 different values.</summary>
        public uint ForegroundImageId { get; set; }
        /// <summary>+0x2298 "BackgroundImageId" [P]. Live: 12089 on the recipe goals, others per subcategory.</summary>
        public uint BackgroundImageId { get; set; }
        /// <summary>+0x229c sort order in the Journal lists (compared by FUN_143552c70 / FUN_1435537f0) [P]. Live: per record.</summary>
        public uint SortOrder { get; set; }
        /// <summary>
        /// +0x22a0 "CanWatchAfterCompletion" (also the tracker's "IsRewatchable"; the objective rows and "ObjectivesItems"
        /// show the full count when set) [P]. Live: true on 187 records.
        /// </summary>
        public bool CanWatchAfterCompletion { get; set; }
        /// <summary>
        /// +0x22a1: when the goal is added to the watch list it starts collapsed ("IsCollapsed", FUN_142e33d00 →
        /// FUN_14355bc20) [P]. Live: false on every record.
        /// </summary>
        public bool TrackCollapsed { get; set; }
        /// <summary>+0x22a2 bool. Never read [P]. Live: true on 471, 477, 478 and 685 (Getting Started), false elsewhere.</summary>
        public bool Slot22A2 { get; set; }

        /// <summary>+0x22a8 "Dependency" achievement ids [P].</summary>
        public DependencyList Dependencies { get; set; } = new();
        /// <summary>+0x22c0 items per objective ("ObjectivesItems", tracker "Item Requirement") [P].</summary>
        public ObjectiveItemHintMap ObjectiveItemHints { get; set; } = new();
        /// <summary>+0x22f0 objective → required achievement ("RequiredAchievements", "ObjectiveAchievementId") [P].</summary>
        public RequiredAchievementMap RequiredAchievements { get; set; } = new();

        /// <summary>
        /// +0x22a4 u32, read last. Never read [P]. Live: 1 on 811 records, 0 on 44; on the rest it equals the
        /// objective count (486: 9 objectives → 9) except 508 (11 with one objective) and 685 (6 with two).
        /// </summary>
        public uint Slot22A4 { get; set; }

        public void Read(GamePacketReader reader)
        {
            AchievementId = reader.ReadUInt();
            Flags = reader.ReadByte();
            NameId = new LandmarkText(reader.ReadUInt());
            DescriptionId = new LandmarkText(reader.ReadUInt());
            TimeStarted = reader.ReadULong();
            TimeFinished = reader.ReadULong();
            Progress = reader.ReadSingle();

            AchievementRewardBundle.Read(reader);

            ObjectiveCount = reader.ReadUInt();
            for (int i = 0; i < ObjectiveCount; i++)
            {
                ObjectiveEntry objective = new();
                objective.Read(reader);
                AchievementObjectives.Add(objective);
            }

            IconId = reader.ReadUInt();
            IconTintId = reader.ReadUInt();
            TrophyId = reader.ReadUInt();
            Points = reader.ReadUInt();
            SubcategoryId = reader.ReadUInt();
            Slot226C = (sbyte)reader.ReadByte();
            Slot2270 = reader.ReadUInt();
            UnlocksSteamAchievement = reader.ReadBool();

            CompletionCompositeEffectId = reader.ReadUInt();
            QualityId = reader.ReadUInt();
            VisibilityType = reader.ReadUInt();
            HintTextId = new LandmarkText(reader.ReadUInt());
            Slot2288 = reader.ReadUInt();
            Slot228C = reader.ReadUInt();
            Slot2290 = reader.ReadUInt();
            ForegroundImageId = reader.ReadUInt();
            BackgroundImageId = reader.ReadUInt();
            SortOrder = reader.ReadUInt();

            CanWatchAfterCompletion = reader.ReadBool();
            TrackCollapsed = reader.ReadBool();
            Slot22A2 = reader.ReadBool();

            Dependencies.Read(reader);
            ObjectiveItemHints.Read(reader);
            RequiredAchievements.Read(reader);

            Slot22A4 = reader.ReadUInt();
        }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(AchievementId);
            writer.Write(Flags);
            NameId.Write(writer);
            DescriptionId.Write(writer);
            writer.Write(TimeStarted);
            writer.Write(TimeFinished);
            writer.Write(Progress);

            AchievementRewardBundle.Write(writer);

            writer.Write((uint)AchievementObjectives.Count);
            foreach (ObjectiveEntry objective in AchievementObjectives)
                objective.Write(writer);

            writer.Write(IconId);
            writer.Write(IconTintId);
            writer.Write(TrophyId);
            writer.Write(Points);
            writer.Write(SubcategoryId);
            writer.Write((byte)Slot226C);
            writer.Write(Slot2270);
            writer.Write(UnlocksSteamAchievement);

            writer.Write(CompletionCompositeEffectId);
            writer.Write(QualityId);
            writer.Write(VisibilityType);
            HintTextId.Write(writer);
            writer.Write(Slot2288);
            writer.Write(Slot228C);
            writer.Write(Slot2290);
            writer.Write(ForegroundImageId);
            writer.Write(BackgroundImageId);
            writer.Write(SortOrder);

            writer.Write(CanWatchAfterCompletion);
            writer.Write(TrackCollapsed);
            writer.Write(Slot22A2);

            Dependencies.Write(writer);
            ObjectiveItemHints.Write(writer);
            RequiredAchievements.Write(writer);

            writer.Write(Slot22A4);
        }
    }
}
