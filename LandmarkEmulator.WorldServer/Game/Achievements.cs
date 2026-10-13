using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Goals and achievements. In the client they are one system ("achievements"): the Goals tracker, the journal, the
    /// recipe discoveries and the replicator lists are all achievements with objectives. AchievementBase 0x92, u8 sub
    /// (client dispatcher FUN_142d92750):
    ///   3 Initialize {u32 n, n x achievement}       the whole list, at zone-in
    ///   4 Complete {u32 achievement}                "Completed achievement ...", leaves the tracker
    ///   6 ObjectiveActivated {u32 ach, u32 obj, u32 current, u32 required, u32 progress, bool}
    ///   7 ObjectiveUpdate {u32 ach, u32 obj, u32 current, u32 progress}
    ///   8 ObjectiveComplete {u32 ach, u32 obj, u32 progress, u32 message, bool}   fires GameEvents:OnObjectiveComplete
    ///   0x0B TrackerInitialize {u32 n, n x u32 achievement}
    /// "progress" is the achievement's completion as float bits (achievement +0x20). The client rejects 4/6/7/8 with
    /// trailing bytes.
    ///
    /// The definitions (names, objectives, required counts, rewards) come from a live AchievementInitialize capture
    /// (Resources/PacketDumps/71-AchievementBase, 898 achievements); its layout is in docs/protocol.md. That capture
    /// also carries the captured player's progress, so the server patches each character's own progress into a copy at
    /// zone-in (see <see cref="Load"/> for the patched fields).
    /// </summary>
    public static class Achievements
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const byte InitializeSub = 3, CompleteSub = 4, ActivatedSub = 6, UpdateSub = 7, ObjectiveCompleteSub = 8,
            TrackerSub = 0x0B, TrackerUpdateSub = 0x0C;

        /// <summary>The "Getting Started" goals, in order: each starts when the one before it is complete.</summary>
        public static readonly uint[] GettingStarted = { 471, 480, 478, 477, 685 };

        public class Objective
        {
            public uint Id;
            /// <summary>The objective's text (client text id), e.g. "Smelted Mithril crafted".</summary>
            public uint TextId;
            public uint Required;
            /// <summary>The objective's state in the capture (0 not started, 1 active, 2 complete).</summary>
            public byte CapturedState;
            /// <summary>Offsets in the template of the objective's state (+0x2150) and current count (+0x2154).</summary>
            public int StateOffset, CurrentOffset;
        }

        public class Achievement
        {
            public uint Id;
            /// <summary>The achievement's name (client text id).</summary>
            public uint NameId;
            public List<Objective> Objectives = new();
            /// <summary>Reward items (RewardBundleEntry type 1): item definition id and count.</summary>
            public List<(uint Item, uint Count)> Rewards = new();
            /// <summary>Offsets of the start time (u64 Unix seconds), completion time (u64) and progress (float).</summary>
            public int StartedOffset, CompletedOffset, ProgressOffset;
            /// <summary>The record's bytes in the template ([start, end)).</summary>
            public int RecordStart, RecordEnd;
            /// <summary>A Daily goal (subcategory 91): sent only while the character has it (<see cref="DailyGoals"/>).</summary>
            public bool Daily;
        }

        private static byte[] template;
        private static readonly Dictionary<uint, Achievement> achievements = new();
        private static readonly Dictionary<uint, Achievement> byObjective = new();

        /// <summary>Per character: objective id → (state, current). Only objectives that differ from the defaults.</summary>
        private static readonly Dictionary<ulong, Dictionary<uint, (byte State, uint Current)>> progress = new();
        private static readonly object sync = new();

        public static Achievement Get(uint id) => achievements.TryGetValue(id, out Achievement a) ? a : null;

        /// <summary>
        /// Parses the captured AchievementInitialize with the client's layout (FUN_142d8bf90 → FUN_142b7ca00 per
        /// achievement, FUN_142b95550 per reward bundle, FUN_142b89710 per objective) and records where each
        /// character-specific field sits, so a copy can be patched per character without re-encoding.
        /// </summary>
        public static void Load()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Resources", "PacketDumps", "71-AchievementBase");
            if (!File.Exists(path))
            {
                log.Warn("No achievement capture; goals are off.");
                return;
            }
            template = File.ReadAllBytes(path).AsSpan(2).ToArray(); // drop the u16 opcode
            Parse();
            AddDailies();
            log.Info($"{achievements.Count} achievements, {byObjective.Count} objectives ({achievements.Values.Count(a => a.Daily)} daily).");
        }

        /// <summary>Reads <see cref="template"/>: every achievement with the offsets of its per-character fields.</summary>
        private static void Parse()
        {
            achievements.Clear();
            byObjective.Clear();
            objectivesByText = null;
            var r = new Reader(template);
            r.U8(); // sub 3
            uint count = r.U32();
            for (uint i = 0; i < count; i++)
            {
                int recordStart = r.Position;
                var a = new Achievement { Id = r.U32(), RecordStart = recordStart };
                r.U8();
                a.NameId = r.U32();
                r.U32();                                           // description text
                a.StartedOffset = r.Position; r.U64();
                a.CompletedOffset = r.Position; r.U64();
                a.ProgressOffset = r.Position; r.U32();
                RewardBundle(r, a.Rewards);
                uint objectives = r.U32();
                for (uint o = 0; o < objectives; o++)
                {
                    r.U32();                                       // list key (= objective id)
                    var objective = new Objective { Id = r.U32() };
                    objective.TextId = r.U32();
                    r.U32();                                       // unknown
                    RewardBundle(r, null);
                    objective.StateOffset = r.Position; objective.CapturedState = (byte)r.U32();
                    objective.CurrentOffset = r.Position; r.U32();
                    objective.Required = r.U32();
                    r.U32(); r.U32();                              // sort order, unknown
                    r.U32();                                       // unknown
                    r.Skip(16); r.U8(); r.U32();                   // 4 x u32, u8, u32
                    a.Objectives.Add(objective);
                    byObjective[objective.Id] = a;
                }
                r.Skip(5 * 4 + 1 + 4 + 1 + 10 * 4 + 3);           // 5 x u32, i8, u32, bool, 10 x u32, 3 x bool
                r.Skip(r.U32() * 4);                               // u32 list
                uint hints = r.U32();
                for (uint h = 0; h < hints; h++) { r.U32(); r.Skip(r.U32() * 16); }
                r.Skip(r.U32() * 8);                               // u32 → u32 map
                r.U32();
                a.RecordEnd = r.Position;
                a.Daily = DailyGoals.IsDaily(a.Id);
                achievements[a.Id] = a;
            }
            if (r.Position != template.Length)
                log.Warn($"Achievement capture: parsed {r.Position} of {template.Length} bytes.");
        }

        /// <summary>
        /// The capture holds two Daily goals (669 Scalewind Hunt, 679 Shadow Wielder: the captured player's dailies that
        /// day). The other 19 are made from 669's record — same layout, subcategory 91 (Daily), a Lumen reward — with
        /// their own id, name, objective (id = achievement + 588, text, count), reward and icon (9338 + id), all from the
        /// client's own texts (Daily: ... 575703-575723). Appended to the template, then everything is parsed again.
        /// </summary>
        private static void AddDailies()
        {
            if (!achievements.TryGetValue(669, out Achievement model))
                return;
            byte[] slice = template[model.RecordStart..model.RecordEnd];
            int requiredAt = model.Objectives[0].StateOffset - model.RecordStart + 8;
            var extra = new List<byte[]>();
            foreach (DailyGoals.Daily d in DailyGoals.All)
            {
                if (achievements.ContainsKey(d.Id))
                    continue;
                byte[] rec = (byte[])slice.Clone();
                BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(0), d.Id);
                Patch(rec, 575709, d.NameText, 1);
                Patch(rec, 1257, d.Id + 588, 2);
                Patch(rec, 575131, d.ObjectiveText, 1);
                Patch(rec, 669 + 9338, d.Id + 9338, 1);
                BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(requiredAt), d.Required);
                // The reward entry: {…, u32 count 30, u32 item 1579 (Lumen), …}.
                for (int i = 0; i + 8 <= rec.Length; i++)
                    if (BinaryPrimitives.ReadUInt32LittleEndian(rec.AsSpan(i)) == 30
                        && BinaryPrimitives.ReadUInt32LittleEndian(rec.AsSpan(i + 4)) == 1579)
                        BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(i), d.Lumens);
                extra.Add(rec);
            }
            if (extra.Count == 0)
                return;
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(template.AsSpan(1));
            var output = new MemoryStream();
            output.WriteByte(template[0]);
            output.Write(BitConverter.GetBytes(count + (uint)extra.Count));
            output.Write(template, 5, template.Length - 5);
            foreach (byte[] rec in extra)
                output.Write(rec);
            template = output.ToArray();
            Parse();
        }

        /// <summary>Replaces every little-endian u32 <paramref name="from"/> in a record (expected <paramref name="expect"/> times).</summary>
        private static void Patch(byte[] rec, uint from, uint to, int expect)
        {
            int found = 0;
            for (int i = 0; i + 4 <= rec.Length; i++)
                if (BinaryPrimitives.ReadUInt32LittleEndian(rec.AsSpan(i)) == from)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(i), to);
                    found++;
                    i += 3;
                }
            if (found != expect)
                log.Warn($"Daily record patch {from} → {to}: {found} found, {expect} expected.");
        }

        /// <summary>
        /// Reward bundle (FUN_142b95550): bool, u32, u32→u32 map, 3 x u32, u64, 5 x u32, 2 x u64, 2 x u32, entries, u32.
        /// Entries are {u32 type} + the type's reader (vtable +0x78; factory FUN_143ac8420), all starting with the
        /// base FUN_143ac4130: bool, 6 x u32 (icon, ?, name text, count, item id, ?), string, u32, bool, 2 x u32.
        /// </summary>
        private static void RewardBundle(Reader r, List<(uint, uint)> items)
        {
            r.U8(); r.U32(); r.Skip(r.U32() * 8);
            r.Skip(3 * 4 + 8 + 5 * 4 + 16 + 2 * 4);
            uint entries = r.U32();
            for (uint e = 0; e < entries; e++)
            {
                uint type = r.U32();
                r.U8();
                uint[] v = { r.U32(), r.U32(), r.U32(), r.U32(), r.U32(), r.U32() };
                r.Skip(r.U32());                                   // string
                r.U32(); r.U8(); r.U32(); r.U32();
                if (type is 6 or 7 or 8 or 10 or 0xe or 0x16 or 0x19)
                    r.U32();                                       // types with one extra u32 (QuestAdd, Recipe, ...)
                if (type == 1 && v[4] != 0)
                    items?.Add((v[4], v[3]));
            }
            r.U32();
        }

        /// <summary>A character's objective state: saved progress, or the default for a character who hasn't touched it.</summary>
        private static (byte State, uint Current) StateOf(ulong character, Objective objective, Achievement owner)
        {
            if (progress.TryGetValue(character, out var mine) && mine.TryGetValue(objective.Id, out var saved))
                return saved;
            int chain = Array.IndexOf(GettingStarted, owner.Id);
            if (chain >= 0)
                return (chain == 0 ? (byte)1 : (byte)0, 0);
            // Elsewhere objectives the captured player had finished start active; the rest keep the captured state
            // (0 for ones that unlock later, such as dailies and "complete these first" goals).
            return (objective.CapturedState == 2 ? (byte)1 : objective.CapturedState, 0);
        }

        private static bool IsComplete(ulong character, Achievement a) =>
            a.Objectives.Count > 0 && a.Objectives.All(o => StateOf(character, o, a).State == 2);

        /// <summary>Completion fraction shown by the client: counts done over counts required, all objectives.</summary>
        private static float ProgressOf(ulong character, Achievement a)
        {
            long required = a.Objectives.Sum(o => (long)Math.Max(o.Required, 1));
            long done = a.Objectives.Sum(o => (long)Math.Min(StateOf(character, o, a).Current, Math.Max(o.Required, 1)));
            return required == 0 ? 0f : (float)done / required;
        }

        /// <summary>Loads a character's saved progress (at login).</summary>
        public static void LoadCharacter(ulong character)
        {
            var rows = DatabaseManager.Instance.CharacterDatabase.GetObjectives(character);
            lock (sync)
                progress[character] = rows.ToDictionary(r => r.ObjectiveId, r => (r.State, r.Current));
        }

        /// <summary>
        /// Zone-in: the captured list with this character's progress patched in (objective state and count, start and
        /// completion times, progress), then the tracker with the active Getting Started goal.
        /// </summary>
        public static void SendInitial(WorldSession session)
        {
            if (template == null)
                return;
            ulong character = session.PlayerGuid;
            HashSet<uint> dailies = DailyGoals.Assign(session);
            byte[] body = (byte[])template.Clone();
            lock (sync)
            {
                foreach (Achievement a in achievements.Values)
                {
                    bool started = false;
                    foreach (Objective o in a.Objectives)
                    {
                        var (state, current) = StateOf(character, o, a);
                        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(o.StateOffset), state);
                        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(o.CurrentOffset), current);
                        started |= state != 0;
                    }
                    bool complete = IsComplete(character, a);
                    // Start/completion times are only shown; the server doesn't keep them, so use "now" when set.
                    ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    BinaryPrimitives.WriteUInt64LittleEndian(body.AsSpan(a.StartedOffset), started ? now : 0);
                    BinaryPrimitives.WriteUInt64LittleEndian(body.AsSpan(a.CompletedOffset), complete ? now : 0);
                    BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(a.ProgressOffset), complete ? 1f : ProgressOf(character, a));
                }
            }
            // Every record but the Daily goals this character doesn't have today.
            var kept = achievements.Values.Where(a => !a.Daily || dailies.Contains(a.Id)).OrderBy(a => a.RecordStart).ToList();
            var output = new MemoryStream();
            output.WriteByte(body[0]);
            output.Write(BitConverter.GetBytes((uint)kept.Count));
            foreach (Achievement a in kept)
                output.Write(body, a.RecordStart, a.RecordEnd - a.RecordStart);
            session.EnqueueMessage(ZoneMessageOpcode.AchievementBase, output.ToArray());
            SendTracker(session);
        }

        /// <summary>
        /// TrackerInitialize (sub 0x0B {u32 n, n × u32 achievement}): the goals the Goals panel (watch list) shows — the
        /// ones the player chose to track, or else the first unfinished Getting Started goal.
        /// </summary>
        private static void SendTracker(WorldSession session)
        {
            uint[] tracked = Unlocks.Values(session.PlayerGuid, Unlocks.TrackedAchievement).ToArray();
            if (tracked.Length == 0)
                lock (sync)
                    tracked = GettingStarted.Where(id => achievements.ContainsKey(id) && !IsComplete(session.PlayerGuid, achievements[id]))
                        .Take(1).ToArray();
            session.EnqueueMessage(ZoneMessageOpcode.AchievementBase, Build(TrackerSub, w =>
            {
                w.Write((uint)tracked.Length);
                foreach (uint id in tracked)
                    w.Write(id);
            }));
        }

        /// <summary>Most goals one player may track.</summary>
        private const int MaxTracked = 64;

        /// <summary>
        /// AchievementBase from the client. Sub 0x0C TrackerUpdate (ObjectivesController.SetTrackingAchievement →
        /// FUN_142d95870 → FUN_142d8d920): one change, captured as u32 0, u32 n, n × u32 achievement, u32 removed
        /// (0 = now tracked, 1 = no longer tracked). Kept per character and sent back at login (TrackerInitialize).
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.AchievementBase)]
        public static void HandleRequest(WorldSession session, AchievementRequest request)
        {
            if (request.SubOpcode != TrackerUpdateSub || request.Body.Length < 12)
            {
                log.Debug($"{session.CharacterName}: unhandled achievement sub {request.SubOpcode}: {BitConverter.ToString(request.Body)}");
                return;
            }
            var reader = new GamePacketReader(request.Body);
            reader.ReadUInt();
            uint n = reader.ReadUInt();
            if (n > MaxTracked || request.Body.Length < 12 + 4 * n)
                return;
            var changed = new List<uint>();
            for (uint i = 0; i < n; i++)
                changed.Add(reader.ReadUInt());
            bool removed = reader.ReadUInt() != 0;
            var tracked = Unlocks.Values(session.PlayerGuid, Unlocks.TrackedAchievement).ToHashSet();
            if (removed)
                tracked.ExceptWith(changed);
            else
                tracked.UnionWith(changed);
            if (tracked.Count > MaxTracked)
                return;
            Unlocks.Replace(session, Unlocks.TrackedAchievement, tracked);
            log.Debug($"{session.CharacterName} {(removed ? "stops tracking" : "tracks")} {string.Join(", ", changed)}; tracked now {string.Join(", ", tracked)}.");
        }

        /// <summary>
        /// Counts something the player did towards an objective. Only active objectives count. Sends ObjectiveUpdate,
        /// then ObjectiveComplete when the required count is reached, then completes the achievement (rewards, next
        /// Getting Started goal) when it was the last objective. Saves the change.
        /// </summary>
        public static void Progress(WorldSession session, uint objectiveId, uint amount = 1)
        {
            if (!byObjective.TryGetValue(objectiveId, out Achievement a))
                return;
            Objective o = a.Objectives.First(x => x.Id == objectiveId);
            ulong character = session.PlayerGuid;
            var saves = new List<CharacterObjectiveModel>();
            bool achievementDone = false;
            lock (sync)
            {
                var (state, current) = StateOf(character, o, a);
                if (state != 1)
                    return;
                uint required = Math.Max(o.Required, 1);
                current = (uint)Math.Min((long)current + amount, required);
                state = current >= required ? (byte)2 : (byte)1;
                Set(character, a, o, state, current, saves);
                float fraction = ProgressOf(character, a);
                if (state == 1)
                    session.EnqueueMessage(ZoneMessageOpcode.AchievementBase, Build(UpdateSub, w =>
                    {
                        w.Write(a.Id); w.Write(o.Id); w.Write(current); w.Write(BitConverter.SingleToUInt32Bits(fraction));
                    }));
                else
                {
                    session.EnqueueMessage(ZoneMessageOpcode.AchievementBase, Build(ObjectiveCompleteSub, w =>
                    {
                        w.Write(a.Id); w.Write(o.Id); w.Write(BitConverter.SingleToUInt32Bits(fraction)); w.Write(0u); w.Write(false);
                    }));
                    achievementDone = IsComplete(character, a);
                }
            }
            if (achievementDone)
                Complete(session, a, saves);
            Save(saves);
        }

        /// <summary>Objective ids the server counts. Names are the client's objective texts.</summary>
        public const uint UndergroundAdventure = 1319;
        /// <summary>Becoming a Luminary: visit a landscape without "Pioneer" in its name.</summary>
        public const uint VisitNonPioneer = 1318;           // Becoming a Luminary: travel to an Underground Adventure
        private const uint AxeTutorial = 741, PickTutorial = 742; // Getting Around: harvest 25 wood / 50 ore, gems, stone
        private const uint AxeLifetime = 1122, PickLifetime = 1119;

        /// <summary>Harvested item → its "X harvested" objective (This Stuff is EVERYWHERE, Obtain ...ium?, What About Teak?).</summary>
        private static readonly Dictionary<uint, uint> harvestObjectives = new()
        {
            [44] = 1130, [76] = 1131, [45] = 1132, [46] = 1133,                    // dirt, ice, sand, stone
            [34] = 1138, [35] = 1139, [37] = 1140, [36] = 1141, [39] = 1142,      // copper, tin, silver, iron, gold
            [71] = 1148, [73] = 1149, [72] = 1150                                  // plain, striped, burled wood
        };
        private static readonly HashSet<uint> woods = new() { 71, 72, 73 };
        private static readonly HashSet<uint> commonGround = new() { 44, 45, 46, 76 };

        /// <summary>
        /// Items from an axe or pick swing: wood counts for the axe goals; ore, gems and uncommon stone (anything but
        /// dirt, sand, stone and ice) for the pick goals; each material for its own "harvested" objective.
        /// </summary>
        public static void Harvested(WorldSession session, uint item, uint count)
        {
            if (woods.Contains(item))
            {
                Progress(session, AxeTutorial, count);
                Progress(session, AxeLifetime, count);
            }
            else if (!commonGround.Contains(item))
            {
                Progress(session, PickTutorial, count);
                Progress(session, PickLifetime, count);
            }
            if (harvestObjectives.TryGetValue(item, out uint objective))
                Progress(session, objective, count);
            DailyGoals.Harvested(session, woods.Contains(item), commonGround.Contains(item), count);
            ProgressByText(session, ItemName(item) + " harvested", count);
        }

        private static Dictionary<string, List<uint>> objectivesByText;

        /// <summary>Objective ids by their text (lower case), e.g. "sand harvested", "regal fireplace recipe unlocked".</summary>
        private static Dictionary<string, List<uint>> ObjectivesByText
        {
            get
            {
                if (objectivesByText != null)
                    return objectivesByText;
                var map = new Dictionary<string, List<uint>>();
                foreach (Achievement a in achievements.Values)
                    foreach (Objective o in a.Objectives)
                    {
                        string text = LandmarkEmulator.Shared.GameTable.Text.TextManager.Instance.GetTextForId(o.TextId)?.Trim().ToLowerInvariant();
                        if (string.IsNullOrEmpty(text))
                            continue;
                        if (!map.TryGetValue(text, out var list))
                            map[text] = list = new List<uint>();
                        list.Add(o.Id);
                    }
                return objectivesByText = map;
            }
        }

        /// <summary>Counts towards every objective whose text is exactly <paramref name="text"/> (case-insensitive).</summary>
        private static void ProgressByText(WorldSession session, string text, uint amount = 1)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            if (ObjectivesByText.TryGetValue(text.Trim().ToLowerInvariant(), out List<uint> ids))
                foreach (uint id in ids)
                    if (!byObjective[id].Daily)      // dailies are counted by DailyGoals
                        Progress(session, id, amount);
        }

        private static string ItemName(uint item)
            => Inventory.ItemDefinitionManager.Get(item) is { } d
                ? LandmarkEmulator.Shared.GameTable.Text.TextManager.Instance.GetTextForId(d.NameId) ?? ""
                : "";

        /// <summary>
        /// A recipe was learned (from a recipe item): "&lt;output&gt; recipe unlocked" and the recipe item's own name
        /// (collection goals such as "Recipe: Dust Devil Mote Emitter").
        /// </summary>
        public static void RecipeLearned(WorldSession session, uint recipeItem, uint outputItem)
        {
            ProgressByText(session, ItemName(outputItem) + " recipe unlocked");
            ProgressByText(session, ItemName(recipeItem));
        }

        /// <summary>
        /// Voxels built: "Blocks placed", "&lt;material name&gt; blocks placed", and the biome goals ("Blocks placed in old
        /// growth forests" / "volcanic areas") for where the builder stands.
        /// </summary>
        public static void BlocksPlaced(WorldSession session, string materialName, uint count)
        {
            ProgressByText(session, "Blocks placed", count);
            if (!string.IsNullOrEmpty(materialName))
                ProgressByText(session, materialName + " blocks placed", count);
            int biome = Terrain.TerrainMaterials.BiomeAt(session.Position, session.ZoneId);
            if (biome is 50 or 72 or 75)
                ProgressByText(session, "Blocks placed in old growth forests", count);
            else if (biome == 47)
                ProgressByText(session, "Blocks placed in volcanic areas", count);
        }

        /// <summary>Voxels taken down by building tools: "Blocks removed" and "&lt;material name&gt; blocks removed".</summary>
        public static void BlocksRemoved(WorldSession session, string materialName, uint count)
        {
            ProgressByText(session, "Blocks removed", count);
            if (!string.IsNullOrEmpty(materialName))
                ProgressByText(session, materialName + " blocks removed", count);
        }

        /// <summary>"Group joined" (So Happy Together).</summary>
        public static void GroupJoined(WorldSession session) => ProgressByText(session, "Group joined");

        private static DateTime nextGroupTick = DateTime.UtcNow;

        /// <summary>
        /// Server tick: once a minute, every player in a group earns 60 s towards "Spend 10/60 minutes grouped up"
        /// (counted in seconds).
        /// </summary>
        public static void Update()
        {
            if (DateTime.UtcNow < nextGroupTick)
                return;
            nextGroupTick = DateTime.UtcNow.AddMinutes(1);
            // A new day: the Journal's Daily goals change for everyone online.
            foreach (WorldSession session in PlayerManager.Snapshot())
                if (DailyGoals.NewDay(session))
                    SendInitial(session);
            foreach (WorldSession session in PlayerManager.Snapshot())
                if (Groups.MembersOf(session).Count > 1)
                {
                    ProgressByText(session, "Spend 10 minutes grouped up", 60);
                    ProgressByText(session, "Spend 60 minutes grouped up", 60);
                }
        }

        /// <summary>Biome id (VoxelBiomes.txt, surface biomes) → "X visited" objective of Tundra's Still the Best.</summary>
        private static readonly Dictionary<int, uint> biomeObjectives = new()
        {
            [48] = 1112,                                   // DesertSurface → Desert visited
            [49] = 1113, [66] = 1113, [67] = 1113, [68] = 1113, [69] = 1113, [70] = 1113, [71] = 1113, [74] = 1113, // Tropical → Jungle
            [51] = 1114, [80] = 1114, [81] = 1114,         // Tundra
            [50] = 1115, [72] = 1115, [75] = 1115,         // Old Growth Forest
            [52] = 1116, [73] = 1116, [76] = 1116,         // Deciduous Forest
            [47] = 1117,                                   // Volcanic
            [54] = 1118                                    // Ocean
        };
        private static readonly Dictionary<ulong, int> lastBiome = new();

        /// <summary>
        /// Player moved (position update or server teleport): when the biome under them changes (one lookup in the
        /// client's biome bitmap), counts a visit to it. Nothing is sent unless its visit objective is active.
        /// </summary>
        public static void Moved(WorldSession session, System.Numerics.Vector3 position)
        {
            DailyGoals.Moved(session, position);
            PropLimits.Moved(session, position);
            int biome = Terrain.TerrainMaterials.BiomeAt(position, session.ZoneId);   // that landscape's biome bitmap
            lock (sync)
            {
                if (lastBiome.TryGetValue(session.PlayerGuid, out int last) && last == biome)
                    return;
                lastBiome[session.PlayerGuid] = biome;
            }
            if (biomeObjectives.TryGetValue(biome, out uint objective))
                Progress(session, objective);
        }

        /// <summary>
        /// UI event id (UIEventTriggered) → the objective it completes. The ids are the constants the client's Flash UI
        /// passes to Ui.UIEventTriggered (re/tools/swf_uievents.py, re/out/ui_events.txt): 4 TeleportWidget opened
        /// (Travel menu), 5 the Lumen count button (HudMenuWidget.handleEtherShardButton), 11 Build Mode switched to the
        /// Prop tab (BuildModeWidget.switchView: 12 Build, 11 Prop, 10 Tint, 9 Templates).
        /// </summary>
        private static readonly Dictionary<uint, uint> uiObjectives = new()
        {
            [4] = 834,   // Open your Travel Menu
            [5] = 835,   // Open Lumen Station by clicking the Lumen count
            [11] = 1305  // Open Build Mode and switch to the Prop tab
        };

        /// <summary>
        /// A build site switched mode (F5). Play: "Switch your Build Site to Play Mode" (747) and "enable Play Mode on
        /// your site once again" (838, 2 times); Edit: "Return your site to Edit Mode" (1317). Only active objectives count.
        /// </summary>
        public static void SiteModeChanged(WorldSession session, bool play)
        {
            if (play)
            {
                Progress(session, 747);
                Progress(session, 838);
            }
            else
                Progress(session, 1317);
        }

        /// <summary>"Close build mode [Tab] and equip your weapon".</summary>
        public const uint EquipWeapon = 1272;

        /// <summary>UI event 2: BuildModeWidget opened (Tab).</summary>
        private const uint BuildModeOpened = 2;
        /// <summary>"Press [TAB] to open Build Mode while on your Build Site".</summary>
        private const uint OpenBuildModeOnSite = 728;

        /// <summary>
        /// The client reported a UI event (UiBase 0x17 {u32 event}): a window opened, a mode switched. Counts the
        /// objective waiting for it, if any.
        /// </summary>
        public static void UiEvent(WorldSession session, uint eventId)
        {
            log.Debug($"{session.CharacterName} UI event {eventId}.");
            if (uiObjectives.TryGetValue(eventId, out uint objective))
                Progress(session, objective);
            else if (eventId == BuildModeOpened && Claims.IsOnOwnSite(session))
                Progress(session, OpenBuildModeOnSite);
        }

        /// <summary>Prop id (PropDefs.txt) → "place this prop" objective of Props To You.</summary>
        private static readonly Dictionary<uint, uint> placeObjectives = new()
        {
            [2113] = 739,   // Chest (Simple Medium)
            [1380] = 1310,  // Simple Stool
            [2655] = 1313,  // Starting Location
            [2890] = 2156   // Target Dummy
        };

        /// <summary>Item 3162 Barren Flameblade, a creature prop: "Place the Barren Flameblade character" (Cry Havoc!, 2×).</summary>
        private const uint BarrenFlamebladeItem = 3162, PlaceFlameblade = 745;
        /// <summary>Item 3760 Dungeon Builder's Starter Set: "Craft the Dungeon Builder's Starter Set" (Cry Havoc!).</summary>
        private const uint DungeonStarterSetItem = 3760, CraftDungeonStarterSet = 744;

        /// <summary>Props To You: "Tint your Simple Stool using the Build Mode Tint tab".</summary>
        private const uint PlaceSimpleStool = 1310, TintSimpleStool = 1312;

        /// <summary>
        /// Every Simple Stool prop: the plain one and the coloured variants (items "Simple Stool (Green/Orange/...)" place
        /// their own props), so placing or tinting any of them counts for the stool goals.
        /// </summary>
        private static readonly Lazy<HashSet<uint>> simpleStools = new(() =>
        {
            var set = new HashSet<uint> { 1380 };
            foreach (var d in Inventory.ItemDefinitionManager.All)
                if (d.PropId != 0 && (Shared.GameTable.Text.TextManager.Instance.GetTextForId(d.NameId) ?? "")
                        .StartsWith("Simple Stool", StringComparison.OrdinalIgnoreCase))
                    set.Add(d.PropId);
            return set;
        });

        /// <summary>The player tinted one of their props (build tray tint tab).</summary>
        public static void PropTinted(WorldSession session, uint propId)
        {
            if (simpleStools.Value.Contains(propId))
                Progress(session, TintSimpleStool);
        }

        /// <summary>A prop was placed for the first time.</summary>
        public static void PropPlaced(WorldSession session, uint propId)
        {
            // "<prop> placed" goals (e.g. "Padded Sofa placed"), named like the item that places the prop.
            foreach (var item in Inventory.ItemDefinitionManager.ItemsForProp(propId).Take(1))
                ProgressByText(session, ItemName(item.Id) + " placed");
            if (simpleStools.Value.Contains(propId))
                Progress(session, PlaceSimpleStool);
            else if (placeObjectives.TryGetValue(propId, out uint objective))
                Progress(session, objective);
            else if (propId != 0 && Inventory.ItemDefinitionManager.Get(BarrenFlamebladeItem)?.PropId == propId)
                Progress(session, PlaceFlameblade);
        }

        /// <summary>The player finished crafting an item.</summary>
        public static void Crafted(WorldSession session, uint itemDefinitionId)
        {
            if (itemDefinitionId == DungeonStarterSetItem)
                Progress(session, CraftDungeonStarterSet);
            if (CraftObjectives.TryGetValue(itemDefinitionId, out List<uint> objectives))
                foreach (uint objective in objectives)
                    Progress(session, objective);
        }

        private static Dictionary<uint, List<uint>> craftObjectives;

        /// <summary>
        /// "&lt;item name&gt; crafted" objectives (e.g. "Smelted Mithril crafted", "Blue Lumicite crafted"), matched to the
        /// item definitions of that name. Built on first use, after the item definitions are loaded.
        /// </summary>
        private static Dictionary<uint, List<uint>> CraftObjectives
        {
            get
            {
                if (craftObjectives != null)
                    return craftObjectives;
                var itemsByName = Inventory.ItemDefinitionManager.All
                    .Select(d => (d.Id, Name: LandmarkEmulator.Shared.GameTable.Text.TextManager.Instance.GetTextForId(d.NameId)))
                    .Where(d => !string.IsNullOrEmpty(d.Name))
                    .ToLookup(d => d.Name.Trim(), d => d.Id, StringComparer.OrdinalIgnoreCase);
                var map = new Dictionary<uint, List<uint>>();
                const string suffix = " crafted";
                foreach (Achievement a in achievements.Values)
                    foreach (Objective o in a.Objectives)
                    {
                        string text = LandmarkEmulator.Shared.GameTable.Text.TextManager.Instance.GetTextForId(o.TextId);
                        if (text == null || !text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                            continue;
                        foreach (uint item in itemsByName[text[..^suffix.Length].Trim()])
                        {
                            if (!map.TryGetValue(item, out var list))
                                map[item] = list = new List<uint>();
                            list.Add(o.Id);
                        }
                    }
                log.Info($"{map.Count} items count towards crafting goals.");
                return craftObjectives = map;
            }
        }

        /// <summary>"Monsters killed" (They Got In My Way).</summary>
        public const uint MonstersKilled = 1016;
        /// <summary>"Item given or received in trade" (Quid Pro Quo).</summary>
        public const uint ItemTraded = 845;
        /// <summary>"Designs placed in a Build Site" (Building Better Worlds).</summary>
        public const uint DesignsPlaced = 783;

        public static void Killed(WorldSession session) => Progress(session, MonstersKilled);
        public static void Traded(WorldSession session) => Progress(session, ItemTraded);
        public static void DesignPlaced(WorldSession session, string name = "")
        {
            Progress(session, DesignsPlaced);
            // Getting Started: "Place Castle Courtyard design", "Place Castle Wall Entry design on your Build Site"…
            if (name.Length > 0)
            {
                ProgressByText(session, $"Place {name} design");
                ProgressByText(session, $"Place {name} design on your Build Site");
            }
        }

        /// <summary>Weapon attacks (Cry Havoc!): 1273 Basic Attack [LMB], 1274 Power Attack [RMB].</summary>
        public const uint BasicAttackUsed = 1273, PowerAttackUsed = 1274;
        /// <summary>Open Lumen Station by clicking the Lumen count (Cry Havoc!).</summary>
        public const uint OpenLumenStation = 835;

        /// <summary>Open Build Site Management window [U] (Your Home Is Your Castle).</summary>
        public const uint OpenSiteManagement = 840;
        /// <summary>Customize your Build Site name (Your Home Is Your Castle).</summary>
        public const uint CustomizeSiteName = 841;

        public const byte GlobalAnnounceSub = 0x0F;

        /// <summary>
        /// Capstone titles the whole server hears about: title item → its announcement text (the texts follow the title
        /// names in the client's table, 575528-575537 and 575546). Which achievement gives which title comes from the
        /// live AchievementInitialize capture's reward bundles: 510 → the Dramatic (3900), 526 You Blockhead → Blockhead
        /// (3901), 542 All The Things → the Collector (3902), 566 It's ...ALIVE! → the Creator (3907), 595 Slayer of All
        /// the Things → Warmaster (3915), 618 There Can Be Only One → Elementalist (3923), 645 → Researcher (3927),
        /// 655 Ridiculously Good Outfits → Fashion Mogul (3928), 685 Becoming a Luminary → Luminary (3929). Deforester
        /// (3926, "finally got enough burled wood!") has no achievement in the 0.589 data but is listed for completeness;
        /// 575535 "has obtained the unobtainable!" belongs to Treasure Hunter or Strip Miner (neither awarded in 0.589).
        /// </summary>
        private static readonly Dictionary<uint, uint> AnnouncedTitles = new()
        {
            [3900] = 575528, [3901] = 575529, [3902] = 575530, [3907] = 575531, [3915] = 575532, [3923] = 575533,
            [3926] = 575534, [3927] = 575536, [3928] = 575537, [3929] = 575546,
        };

        /// <summary>
        /// GlobalAnnounce (0x0F, reader FUN_142d8a7b0: {u32 achievement, u32 text, name block {u32 x 3, string first,
        /// string last}, u64 player}) to everyone online: the client fills [*player*] with the name and prints the line
        /// on the System channel in the announcement colour and as an on-screen message (FUN_142d921c0).
        /// </summary>
        private static void Announce(WorldSession player, Achievement a, uint textId)
        {
            byte[] packet = Build(GlobalAnnounceSub, w =>
            {
                w.Write(a.Id);
                w.Write(textId);
                w.Write(0u);
                w.Write(0u);
                w.Write(0u);
                w.Write(player.CharacterName ?? "");
                w.Write("");
                w.Write(player.PlayerGuid);
            });
            foreach (WorldSession session in PlayerManager.Snapshot())
                session.EnqueueMessage(ZoneMessageOpcode.AchievementBase, packet);
            log.Info($"Announced {player.CharacterName}'s achievement {a.Id} (text {textId}) to everyone.");
        }

        /// <summary>An achievement is done: tell the client, give the reward items, start the next Getting Started goal.</summary>
        private static void Complete(WorldSession session, Achievement a, List<CharacterObjectiveModel> saves)
        {
            session.EnqueueMessage(ZoneMessageOpcode.AchievementBase, Build(CompleteSub, w => w.Write(a.Id)));
            // Meta goals: "<achievement name> completed".
            ProgressByText(session, (LandmarkEmulator.Shared.GameTable.Text.TextManager.Instance.GetTextForId(a.NameId) ?? "") + " completed");
            foreach ((uint item, uint count) in a.Rewards)
                session.GiveItem(item, Math.Max(count, 1));
            log.Info($"{session.CharacterName} completed achievement {a.Id}.");
            foreach ((uint item, _) in a.Rewards)
                if (AnnouncedTitles.TryGetValue(item, out uint announce))
                    Announce(session, a, announce);

            int chain = Array.IndexOf(GettingStarted, a.Id);
            if (chain < 0 || chain + 1 >= GettingStarted.Length || Get(GettingStarted[chain + 1]) is not Achievement next)
                return;
            lock (sync)
                foreach (Objective o in next.Objectives)
                {
                    if (StateOf(session.PlayerGuid, o, next).State != 0)
                        continue;
                    Set(session.PlayerGuid, next, o, 1, 0, saves);
                    session.EnqueueMessage(ZoneMessageOpcode.AchievementBase, Build(ActivatedSub, w =>
                    {
                        w.Write(next.Id); w.Write(o.Id); w.Write(0u); w.Write(Math.Max(o.Required, 1)); w.Write(0u); w.Write(false);
                    }));
                }
            SendTracker(session);
        }

        /// <summary>Puts an achievement's objectives in a state (Daily goals: active and at 0 when given, 0 when dropped).</summary>
        public static void ResetObjectives(ulong character, uint achievementId, byte state)
        {
            if (!achievements.TryGetValue(achievementId, out Achievement a))
                return;
            var saves = new List<CharacterObjectiveModel>();
            lock (sync)
                foreach (Objective o in a.Objectives)
                    Set(character, a, o, state, 0, saves);
            Save(saves);
        }

        /// <summary>Is the achievement complete for this character?</summary>
        public static bool IsComplete(ulong character, uint achievementId)
        {
            lock (sync)
                return achievements.TryGetValue(achievementId, out Achievement a) && IsComplete(character, a);
        }

        /// <summary>Has the character made progress on it (any objective count above 0)?</summary>
        public static bool HasProgress(ulong character, uint achievementId)
        {
            lock (sync)
                return achievements.TryGetValue(achievementId, out Achievement a)
                    && a.Objectives.Any(o => StateOf(character, o, a).Current > 0);
        }

        private static void Set(ulong character, Achievement a, Objective o, byte state, uint current, List<CharacterObjectiveModel> saves)
        {
            if (!progress.TryGetValue(character, out var mine))
                progress[character] = mine = new();
            mine[o.Id] = (state, current);
            saves.Add(new CharacterObjectiveModel { CharacterId = character, ObjectiveId = o.Id, AchievementId = a.Id, State = state, Current = current });
        }

        private static void Save(List<CharacterObjectiveModel> rows)
        {
            if (rows.Count == 0)
                return;
            System.Threading.Tasks.Task.Run(() =>
            {
                try { DatabaseManager.Instance.CharacterDatabase.SaveObjectives(rows); }
                catch (Exception e) { log.Error(e, "Saving objectives failed."); }
            });
        }

        private static byte[] Build(byte sub, Action<GamePacketWriter> body)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(sub);
            body(writer);
            return output.ToArray();
        }

        /// <summary>Little-endian cursor over the template.</summary>
        private sealed class Reader
        {
            private readonly byte[] data;
            public int Position { get; private set; }
            public Reader(byte[] data) => this.data = data;
            public byte U8() => data[Position++];
            public uint U32() { uint v = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(Position)); Position += 4; return v; }
            public ulong U64() { ulong v = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(Position)); Position += 8; return v; }
            public void Skip(long n) => Position += (int)n;
        }
    }
}
