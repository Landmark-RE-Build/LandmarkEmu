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
    ///   2 Add {achievement}                         one more achievement (a goal tier unlocked), see Achievements.Tiers.cs
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
    public static partial class Achievements
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const byte AddSub = 2, InitializeSub = 3, CompleteSub = 4, ActivatedSub = 6, UpdateSub = 7, ObjectiveCompleteSub = 8,
            TrackerSub = 0x0B, TrackerUpdateSub = 0x0C;

        /// <summary>
        /// The "Getting Started" goals, in order: each starts when the one before it is complete. The last three are the
        /// advanced tutorial goals with no live record, built from the wiki (Achievements.Tutorial.cs, TASKS 6.57).
        /// </summary>
        public static readonly uint[] GettingStarted = { 471, 480, 478, 477, 685, GettingCrafty, BuildingBlockBasics, ModularThinking };

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
            /// <summary>Offset in the template of the objective's list key (followed by its id and text).</summary>
            public int KeyOffset;
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
            /// <summary>Where the reward bundle ends (the objective count) and where the points field (+0x2264) is.</summary>
            public int RewardEnd, PointsOffset;
            /// <summary>
            /// Meta goals: the record's objective → child achievement map (the u32 → u32 map near the record's end;
            /// e.g. 595 Slayer of All the Things: 1037 → 574 ... 1057 → 594). Empty for other goals.
            /// </summary>
            public List<(uint Objective, uint Child)> Map = new();
            /// <summary>
            /// A goal tier the client gets only once the tier before it is complete (AchievementAdd, sub 2): that tier's
            /// id, or 0 for goals every character has from the start (<see cref="Tiers"/>).
            /// </summary>
            public uint UnlockedBy;
            /// <summary>A Daily goal (subcategory 91): sent only while the character has it (<see cref="DailyGoals"/>).</summary>
            public bool Daily;
            /// <summary>
            /// Luminary Score points for completing it: record +0x2264 (the 4th u32 after the objectives, reader
            /// FUN_142b7ca00), which the client's AchievementInfo "TotalPoints" sums over completed goals
            /// (0.695 FUN_14355cdb0).
            /// </summary>
            public uint Points;
            /// <summary>
            /// Template offsets of the reward bundles' recipient guid (ClientRewardBundle +0x20f0, Model.Shared.RewardBundle
            /// CharacterId) where the capture has one: it holds the captured character's own guid, so each copy gets the
            /// receiving player's (<see cref="WriteRecord"/>).
            /// </summary>
            public List<int> RecipientOffsets = new();
        }

        private static byte[] template;
        private static readonly Dictionary<uint, Achievement> achievements = new();
        private static readonly Dictionary<uint, Achievement> byObjective = new();

        /// <summary>Per character: objective id → (state, current). Only objectives that differ from the defaults.</summary>
        private static readonly Dictionary<ulong, Dictionary<uint, (byte State, uint Current)>> progress = new();
        private static readonly object sync = new();

        public static Achievement Get(uint id) => achievements.TryGetValue(id, out Achievement a) ? a : null;

        /// <summary>Every goal's reward items (capture bundles, dailies and the built tiers), filled by <see cref="Load"/>.</summary>
        private static HashSet<uint> rewardItems = new();

        /// <summary>
        /// Is the item some goal's reward? Used to keep goal-only recipe items out of random drops
        /// (<see cref="RecipeBook.RandomUnknownRecipeItem"/>): e.g. 3906 "Recipe: The Outcast" (561 The Bigger They Are) and
        /// the 500-kill tiers' armour recipes (Achievements.Tiers.cs) — trace/0695/item-sources-audit-695.md §3.
        /// </summary>
        public static bool IsRewardItem(uint item) => rewardItems.Contains(item);

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
            AddTiers();
            AddTutorialGoals();
            AddNpcQuests();
            rewardItems = achievements.Values.SelectMany(a => a.Rewards).Select(r => r.Item).ToHashSet();
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
                RewardBundle(r, a.Rewards, a.RecipientOffsets);
                a.RewardEnd = r.Position;
                uint objectives = r.U32();
                for (uint o = 0; o < objectives; o++)
                {
                    int keyOffset = r.Position;
                    r.U32();                                       // list key (= objective id)
                    var objective = new Objective { Id = r.U32(), KeyOffset = keyOffset };
                    objective.TextId = r.U32();
                    r.U32();                                       // unknown
                    RewardBundle(r, null, a.RecipientOffsets);
                    objective.StateOffset = r.Position; objective.CapturedState = (byte)r.U32();
                    objective.CurrentOffset = r.Position; r.U32();
                    objective.Required = r.U32();
                    r.U32(); r.U32();                              // sort order, unknown
                    r.U32();                                       // unknown
                    r.Skip(16); r.U8(); r.U32();                   // 4 x u32, u8, u32
                    a.Objectives.Add(objective);
                    byObjective[objective.Id] = a;
                }
                r.U32(); r.U32(); r.U32();                         // +0x2258, +0x225c, +0x2260
                a.PointsOffset = r.Position;
                a.Points = r.U32();                                // +0x2264 Luminary Score points
                r.U32();                                           // +0x2268
                r.Skip(1 + 4 + 1 + 10 * 4 + 3);                    // i8, u32, bool, 10 x u32, 3 x bool
                r.Skip(r.U32() * 4);                               // u32 list
                uint hints = r.U32();
                for (uint h = 0; h < hints; h++) { r.U32(); r.Skip(r.U32() * 16); }
                uint mapped = r.U32();                             // u32 → u32 map: meta objective → child goal
                for (uint m = 0; m < mapped; m++)
                    a.Map.Add((r.U32(), r.U32()));
                r.U32();
                a.RecordEnd = r.Position;
                a.Daily = DailyGoals.IsDaily(a.Id);
                a.UnlockedBy = TierPredecessor(a.Id);
                achievements[a.Id] = a;
            }
            if (r.Position != template.Length)
                log.Warn($"Achievement capture: parsed {r.Position} of {template.Length} bytes.");
            IndexLinks();
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
        /// Reward bundle (FUN_142b95550; fields named in Model.Shared.RewardBundle): bool, u32 coins, currency map
        /// {u32, u32}, u32 experience, 2 x u32, u64, 5 x u32, u64, u64 recipient, u32 icon, u32 name text, entries, u32.
        /// Entries are {u32 type} + the type's reader (vtable +0x78; factory FUN_143ac8420), all starting with the
        /// base FUN_143ac4130: bool, 6 x u32 (icon, ?, name text, count, item id, ?), string, u32, bool, 2 x u32.
        /// </summary>
        private static void RewardBundle(Reader r, List<(uint, uint)> items, List<int> recipients)
        {
            r.U8(); r.U32(); r.Skip(r.U32() * 8);
            int recipientAt = r.Position + 3 * 4 + 8 + 5 * 4 + 8;
            if (BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(recipientAt)) != 0)
                recipients.Add(recipientAt);
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
            var output = new MemoryStream();
            output.WriteByte(InitializeSub);
            var catchUp = new List<CharacterObjectiveModel>();
            lock (sync)
            {
                CatchUpSteps(character, catchUp);   // tutorial step goals (Achievements.Tutorial.cs)
                // Every record but the Daily goals this character doesn't have today and the goal tiers it hasn't
                // unlocked yet (those come one by one as AchievementAdd, Achievements.Tiers.cs).
                var kept = achievements.Values.Where(a => (!a.Daily || dailies.Contains(a.Id)) && IsAvailable(character, a))
                    .OrderBy(a => a.RecordStart).ToList();
                output.Write(BitConverter.GetBytes((uint)kept.Count));
                ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                foreach (Achievement a in kept)
                    WriteRecord(output, character, a, now);
            }
            Save(catchUp);
            session.EnqueueMessage(ZoneMessageOpcode.AchievementBase, output.ToArray());
            SendTracker(session);
        }

        /// <summary>
        /// One achievement record (FUN_142b7ca00; the layout is in docs/protocol.md "Achievements") with this character's
        /// progress patched in: each objective's state and count, the start and completion times, the progress
        /// float and the reward bundles' recipient guid. The same record is an Initialize list element (sub 3) and the
        /// whole body of AchievementAdd (sub 2: 0.589 reader FUN_142d8c370 / 0.695 FUN_142e2a340 read it with the same
        /// record reader as the list). Call
        /// under <see cref="sync"/>.
        /// </summary>
        private static void WriteRecord(Stream output, ulong character, Achievement a, ulong now)
        {
            byte[] rec = template.AsSpan(a.RecordStart, a.RecordEnd - a.RecordStart).ToArray();
            int at(int offset) => offset - a.RecordStart;
            bool started = false;
            foreach (Objective o in a.Objectives)
            {
                var (state, current) = StateOf(character, o, a);
                BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(at(o.StateOffset)), state);
                BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(at(o.CurrentOffset)), current);
                started |= state != 0;
            }
            bool complete = IsComplete(character, a);
            // Start/completion times are only shown; the server doesn't keep them, so use "now" when set.
            BinaryPrimitives.WriteUInt64LittleEndian(rec.AsSpan(at(a.StartedOffset)), started ? now : 0);
            BinaryPrimitives.WriteUInt64LittleEndian(rec.AsSpan(at(a.CompletedOffset)), complete ? now : 0);
            BinaryPrimitives.WriteSingleLittleEndian(rec.AsSpan(at(a.ProgressOffset)), complete ? 1f : ProgressOf(character, a));
            // The capture's reward bundles name the captured character as the recipient; name this one instead.
            foreach (int recipient in a.RecipientOffsets)
                BinaryPrimitives.WriteUInt64LittleEndian(rec.AsSpan(at(recipient)), character);
            output.Write(rec);
        }

        /// <summary>
        /// AchievementAdd (AchievementBase sub 2 {one achievement record}): the client adds the goal to its list
        /// (handler 0.589 FUN_142d8f4a0 / 0.695 FUN_142e2d470: a new achievement object filled from the record, filed by
        /// id with its name and objective texts) so it shows in the Journal without a new Initialize.
        /// </summary>
        public static void SendAdd(WorldSession session, Achievement a)
        {
            var output = new MemoryStream();
            output.WriteByte(AddSub);
            lock (sync)
                WriteRecord(output, session.PlayerGuid, a, (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            session.EnqueueMessage(ZoneMessageOpcode.AchievementBase, output.ToArray());
            log.Info($"{session.CharacterName}: goal {a.Id} added (AchievementAdd).");
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
        /// The goal a recipe record's TrackerAchievementId (record +0x40) may name: the recipe's
        /// TRACKER_ACHIEVEMENT_ID when that goal is one the client gets (the Journal's Replicator goals), else 0.
        /// <para>
        /// Why (trace 2026-10-18, "the Watch button in the craft menus does not work"): the crafting window's Watch
        /// button (0.695 CraftingWidget CraftingView.as) is enabled only when the selected recipe's AchievementId column
        /// is not "0" and, when clicked, calls ObjectivesController.SetTrackingAchievement(AchievementId, selected)
        /// (native FUN_143605450 → FUN_142e33840). That adds the id to the player's tracked set (player +0x1e600, the
        /// set the window's IsTracking column reads, row builder FUN_14360e950), tells the watch list (FUN_14355baa0)
        /// and sends TrackerUpdate. The watch list shows the goal only if the client holds its record
        /// (FUN_14355baa0 → FUN_142e2f980 lookup → FUN_14355d070 row with the record's ObjectiveItemHint: item, count,
        /// image, recipe → the recipe's components); without a record nothing appears, so the button looks dead [P].
        /// 186 of the 363 station recipes with a tracker id have their goal in the live capture (one objective "the item
        /// name", one ObjectiveItemHint, "can watch after completion" set, e.g. 1558 Cut Agate → 728). The other 177
        /// (e.g. 38 Smelted Copper → 306, 17 → 146) have no record anywhere: the 20 Apr 2015 notes (P3248-P3253) say
        /// the Journal's recipe tab holds a recipe goal for every recipe that is not free (free recipes need no
        /// tracking). Sending 0 for those disables the button instead of leaving a button that does nothing
        /// [I: what live put in +0x40 for those recipes is not captured].
        /// </para>
        /// </summary>
        public static uint WatchableGoal(uint trackerAchievementId)
            => trackerAchievementId != 0 && achievements.ContainsKey(trackerAchievementId) ? trackerAchievementId : 0;

        /// <summary>
        /// A completed goal leaves the tracked set: the client's AchievementComplete handler (FUN_142e2e390) drops the id
        /// from its set (player +0x1e600) whether or not the goal may be watched again [P], so the server drops it too —
        /// otherwise the next TrackerInitialize (relog, the next Getting Started goal) would put it back. Watching it again
        /// (a Replicator goal has "can watch after completion") arrives as a new TrackerUpdate.
        /// </summary>
        private static void StopTrackingCompleted(WorldSession session, uint achievementId)
        {
            List<uint> tracked = Unlocks.Values(session.PlayerGuid, Unlocks.TrackedAchievement);
            if (tracked.Remove(achievementId))
                Unlocks.Replace(session, Unlocks.TrackedAchievement, tracked);
        }

        /// <summary>
        /// AchievementBase from the client. Sub 0x0C TrackerUpdate (ObjectivesController.SetTrackingAchievement: the
        /// Journal's right-click / watch button and the crafting window's Watch button → 0.695 FUN_143605450 →
        /// FUN_142e33840, packet ctor FUN_142e2b8f0): one change, u32 n × {u32 id, AchievementData} (always 0 from the
        /// client), u32 n, n × u32 achievement, u32 removed (0 = now tracked, 1 = no longer tracked) — reader
        /// FUN_142e28e60 (FUN_142e29d50 list, FUN_142e29c10 set) [P]. The client has already changed its own set and
        /// watch list, so no reply is needed (its S2C TrackerUpdate handler FUN_142e33d00 would be a no-op for a goal it
        /// holds). Kept per character and sent back at login (TrackerInitialize). Ids of goals the server doesn't have
        /// are not kept: the client can show nothing for them.
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
                tracked.UnionWith(changed.Where(achievements.ContainsKey));
            if (tracked.Count > MaxTracked)
                return;
            Unlocks.Replace(session, Unlocks.TrackedAchievement, tracked);
            log.Debug($"{session.CharacterName} {(removed ? "stops tracking" : "tracks")} {string.Join(", ", changed)}; tracked now {string.Join(", ", tracked)}.");
            // Your Home Is Your Castle: "Open your Journal [J] and right-click on "Props To You" in the Getting Started
            // category to track it" (1322) — the right-click arrives as this TrackerUpdate with 480 in the list.
            if (!removed && changed.Contains(PropsToYou))
                Progress(session, TrackPropsToYou);
        }

        /// <summary>Getting Started goals and objectives counted by the hooks below (TASKS 5.5; texts 576227-576239).</summary>
        private const uint PropsToYou = 480, TrackPropsToYou = 1322, PlaceCampfire = 1315,
            LikeGallerySite = 1307, TravelToPlayerSite = 1308, FireGrapple = 1309;

        /// <summary>
        /// Cry Havoc!: "Place a campfire [5] for out-of-combat healing. Extra campfires can be crafted in the Lumen
        /// Station." (1315, required 0 in the capture → 1). A Cozy Campfire was placed (Consumables).
        /// </summary>
        public static void CampfirePlaced(WorldSession session) => Progress(session, PlaceCampfire);

        /// <summary>
        /// Getting Around: "Visit a Build Site that is published in the gallery. and use the claim banner to give them a
        /// 'Like'" (1307). A Like counted for another player's site (Ratings): since 2026-10-16 only sites published to
        /// the gallery can be liked at all (Store.Gallery lists only those; TASKS 6.43), so every counted Like is one.
        /// </summary>
        public static void LikedSite(WorldSession session, ulong claimId)
        {
            if (WorldChannel.WorldChannelServices.Commerce.IsOnGallery(claimId))
                Progress(session, LikeGallerySite);
        }

        /// <summary>
        /// Getting Around: "Use the Travel menu to visit another player's build site." (1308). The player travelled to
        /// a build site that isn't theirs (the gallery's Visit).
        /// </summary>
        public static void TravelledToSite(WorldSession session, ulong claimId)
        {
            if (Claims.OwnerOf(claimId) != session.PlayerGuid)
                Progress(session, TravelToPlayerSite);
        }

        /// <summary>
        /// Getting Around: "Ensure you are NOT in build mode or combat, then fire your grappling hook [Q] at your mouse
        /// cursor position" (1309). HeroicMovement 0x105 sub 1 is the hook firing; the client fires it only outside
        /// build mode and combat (inferred from the objective's wording), so every fire counts.
        /// </summary>
        public static void HeroicMovement(WorldSession session, byte sub)
        {
            if (sub == 1)
                Progress(session, FireGrapple);
        }

        /// <summary>
        /// Counts something the player did towards an objective. Only active objectives count. Sends ObjectiveUpdate,
        /// then ObjectiveComplete when the required count is reached, then completes the achievement (rewards, next
        /// Getting Started goal) when it was the last objective. Saves the change.
        /// </summary>
        public static void Progress(WorldSession session, uint objectiveId, uint amount = 1)
        {
            // Lost Crystal reached (TASKS 5.10): the one server event for it is the daily Cavern Crawler count
            // (Outposts → DailyGoals.CrystalReached → objective 665 + 588), which arrives here whether or not that daily
            // is assigned today; it also counts for Speak Friend and Enter 622 and its tiers 623 / 624.
            if (objectiveId == DailyGoals.CavernCrawler + 588 && amount > 0)
                ProgressByText(session, "Lost Crystal reached", amount);
            if (!byObjective.TryGetValue(objectiveId, out Achievement a))
                return;
            Objective o = a.Objectives.First(x => x.Id == objectiveId);
            ulong character = session.PlayerGuid;
            var saves = new List<CharacterObjectiveModel>();
            bool achievementDone = false;
            lock (sync)
            {
                // A goal tier not unlocked yet (the client doesn't have it): nothing counts for it.
                if (!IsAvailable(character, a))
                    return;
                var (state, current) = StateOf(character, o, a);
                // Not started yet (captured state 0: meta goals "<X> completed", the secret emote goals, Guild joined,
                // Blocks removed ...): the first thing that counts for it starts it (ObjectiveActivated), so every goal
                // can be earned. Dailies and the Getting Started chain are started by their own rules.
                if (state == 0 && !a.Daily && Array.IndexOf(GettingStarted, a.Id) < 0)
                {
                    state = 1;
                    Set(character, a, o, 1, 0, saves);
                    session.EnqueueMessage(ZoneMessageOpcode.AchievementBase, Build(ActivatedSub, w =>
                    {
                        w.Write(a.Id); w.Write(o.Id); w.Write(0u); w.Write(Math.Max(o.Required, 1)); w.Write(0u); w.Write(false);
                    }));
                }
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
                    // Tutorial step goals: the next step starts once this one is done (Achievements.Tutorial.cs).
                    if (!achievementDone && IsStepGoal(a.Id))
                        ActivateNextStep(character, a, saves, session);
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
        /// <summary>630 I Feel the Earth ...Move (a tier with no capture record, Achievements.Tiers.cs): objective id inferred.</summary>
        private const uint PickLifetimeTier2 = 1120;

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
                // 630 counts only once 629 is complete (unlocked); counted first, so the swing that completes 629
                // doesn't count for 630 too (as ProgressByText).
                Progress(session, PickLifetimeTier2, count);
                Progress(session, PickLifetime, count);
            }
            uint objective = harvestObjectives.GetValueOrDefault(item);
            if (objective != 0)
                Progress(session, objective, count);
            DailyGoals.Harvested(session, woods.Contains(item), commonGround.Contains(item), count);
            // 13 Apr 2016 (P4559): harvesting dailies count for everyone who receives a share of the harvest
            // (TASKS 6.55). The receivers are the party members Gathering.GiveHarvest shared this harvest with (the same
            // Groups.NearbyMembers call and range); each counts the same amount for their own daily. Only the dailies:
            // the note names no other goal, so the achievement objectives above stay with the gatherer.
            foreach (WorldSession member in Groups.NearbyMembers(session, Gathering.PartyShareRange))
                DailyGoals.Harvested(member, woods.Contains(item), commonGround.Contains(item), count);
            // The objective counted by id above has this very text for most of those items (1133 "Stone harvested" for item
            // 46 Stone, 1148 "Plain Wood harvested" for 71 …): it must not count a second time by its text, which made
            // This Stuff is EVERYWHERE / What About Teak? (and Obtain ...ium? where the ore's name matches) advance twice
            // per harvest (bughunt-combat.md B3).
            ProgressByText(session, ItemName(item) + " harvested", count, except: objective);
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

        /// <summary>
        /// Counts towards every objective whose text is exactly <paramref name="text"/> (case-insensitive);
        /// <paramref name="unmappedOnly"/> leaves out the objectives a meta goal's capture map names (those count by the
        /// map, <see cref="Complete"/>); <paramref name="except"/>: an objective the caller already counted by its id.
        /// </summary>
        private static void ProgressByText(WorldSession session, string text, uint amount = 1, bool unmappedOnly = false, uint except = 0)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            if (!ObjectivesByText.TryGetValue(text.Trim().ToLowerInvariant(), out List<uint> ids))
                return;
            // Dailies are counted by DailyGoals. The goal tiers open now are picked before counting, so the count that
            // completes one tier (and unlocks the next, e.g. the 10th "Monsters killed") does not count for the next
            // tier too (inferred: the live rule is not known).
            List<uint> open;
            lock (sync)
                open = ids.Where(id => id != except && !byObjective[id].Daily && IsAvailable(session.PlayerGuid, byObjective[id])
                    && !(unmappedOnly && mappedObjectives.Contains(id))).ToList();
            foreach (uint id in open)
                Progress(session, id, amount);
        }

        /// <summary>
        /// Like <see cref="ProgressByText"/> for several texts at once (e.g. every name of one prop): each objective any
        /// of them names counts once, so one placement never counts twice for one objective.
        /// </summary>
        private static void ProgressByTexts(WorldSession session, IEnumerable<string> texts, uint amount = 1)
        {
            var ids = new HashSet<uint>();
            foreach (string text in texts)
                if (!string.IsNullOrWhiteSpace(text) && ObjectivesByText.TryGetValue(text.Trim().ToLowerInvariant(), out List<uint> found))
                    ids.UnionWith(found);
            if (ids.Count == 0)
                return;
            List<uint> open;
            lock (sync)
                open = ids.Where(id => !byObjective[id].Daily && IsAvailable(session.PlayerGuid, byObjective[id])).ToList();
            foreach (uint id in open)
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
        /// A prop recipe was learned (a "Recipe:" item that teaches a prop, RecipeBook.LearnProp / TeachPropOf; TASKS
        /// 5.7): the recipe item's own name (Recipe Discoveries, e.g. "Recipe: Dust Devil Mote Emitter") and
        /// "&lt;prop item&gt; recipe unlocked" (Fit For A King "Regal Fireplace recipe unlocked", Winter is Coming...). The
        /// prop item is named by the recipe item without "Recipe: " and by the items that place the prop; each distinct
        /// name counts once.
        /// </summary>
        public static void PropRecipeLearned(WorldSession session, uint recipeItem, uint propId)
        {
            string recipeName = ItemName(recipeItem);
            ProgressByText(session, recipeName);
            // Recipe Discoveries records whose objective text is not the recipe's name ("Recipe: Apples" 913 →
            // objective 1575 "Recipe unlocked"): the goal named like the recipe item counts (TASKS 5.10).
            RecipeGoalByName(session, recipeName);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            const string prefix = "Recipe: ";
            if (recipeName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                names.Add(recipeName[prefix.Length..].Trim());
            foreach (var item in Inventory.ItemDefinitionManager.ItemsForProp(propId))
                if (ItemName(item.Id) is { Length: > 0 } name)
                    names.Add(name.Trim());
            // Goal texts put the qualifier first: "Table (Small Round Regal)" → "Small Round Regal Table recipe unlocked",
            // "Rug (Large Rectangular)" → "Large Rectangular Rug", "Shag Rug (Sliding)" → "Sliding Shag Rug" (TASKS 5.10).
            foreach (string name in names.ToList())
                if (Reordered(name) is string turned)
                    names.Add(turned);
            ProgressByTexts(session, names.Select(n => n + " recipe unlocked"));
            // "<creature> unlocked" (The Bigger They Are, Furry Woodland Creatures, Scalewind Squared, I'm NOT the Tin
            // Dog, Night and Day: 18 objectives, e.g. "Magma Giant unlocked"): the creature prop's recipe item
            // ("Recipe: Magma Giant" 3641 → prop 2483) was used. No other objective text ends in "<prop name> unlocked".
            ProgressByTexts(session, names.Select(n => n + " unlocked"));
        }

        /// <summary>
        /// Voxels built: "Blocks placed", "&lt;material name&gt; blocks placed", and the biome goals ("Blocks placed in old
        /// growth forests" / "volcanic areas") for where the builder stands.
        /// </summary>
        public static void BlocksPlaced(WorldSession session, string materialName, uint count)
        {
            Progress(session, PlaceBlocks, count);   // Building Block Basics "Place blocks" (Achievements.Tutorial.cs)
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
            Progress(session, DeleteBlocks, count);  // Building Block Basics "Delete blocks" (Achievements.Tutorial.cs)
            ProgressByText(session, "Blocks removed", count);
            if (!string.IsNullOrEmpty(materialName))
                ProgressByText(session, materialName + " blocks removed", count);
        }

        /// <summary>"Group joined" (So Happy Together).</summary>
        public static void GroupJoined(WorldSession session) => ProgressByText(session, "Group joined");

        public static bool IsFungus(string creatureName)
            => creatureName.Contains("Fung", StringComparison.OrdinalIgnoreCase) || creatureName.Contains("Mushroom", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// "You Can Kill Those Things" (objective "Dispatch explode-y mushrooms"): a Fiery Fungus died near the player,
        /// whoever or whatever killed it (it counts any Fiery Fungus that dies near you, P5753, 7 Jun 2016).
        /// </summary>
        public static void FungusDied(WorldSession session) => ProgressByText(session, "Dispatch explode-y mushrooms");

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
                if (Groups.IsGrouped(session))
                {
                    ProgressByText(session, "Spend 10 minutes grouped up", 60);
                    ProgressByText(session, "Spend 60 minutes grouped up", 60);
                    ProgressByText(session, "Spend 5 hours grouped up", 60);   // 506 Wind Beneath My Wings (tier)
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
        /// A build site switched mode (F5). Play: "Switch your Build Site to Play Mode" (747); Edit: "Return your site
        /// to Edit Mode" (1317). Only active objectives count. 838 ("…enable Play Mode [F5] on your site once again, and
        /// defeat the Barren Flameblades!", 2 times) is counted by the Flameblade kills (<see cref="Killed"/>), not here:
        /// the count is the two Flameblades the goal had the player place (goals audit 2026-10-16).
        /// </summary>
        public static void SiteModeChanged(WorldSession session, bool play)
        {
            if (play)
                Progress(session, 747);
            else
                Progress(session, 1317);
        }

        /// <summary>Cry Havoc!: "…enable Play Mode [F5] on your site once again, and defeat the Barren Flameblades!" (838, 2).</summary>
        private const uint DefeatFlameblades = 838;

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
            TutorialUiEvent(session, eventId);   // Blocks tab, Fine Tuning paste options (Achievements.Tutorial.cs)
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

        /// <summary>
        /// The player tinted one of their props (build tray tint tab) with the tint <paramref name="alias"/> (a
        /// GlobalTint_* alias of the prop tint palette; empty when not known): Props To You's stool, "Props tinted" (Just
        /// Like Crayons!, 5) and the colour goals (<see cref="ColourTinted"/>, Achievements.Triggers.cs).
        /// </summary>
        public static void PropTinted(WorldSession session, uint propId, string alias = "")
        {
            if (simpleStools.Value.Contains(propId))
                Progress(session, TintSimpleStool);
            ProgressByText(session, "Props tinted");
            ColourTinted(session, propId, alias ?? "");
        }

        /// <summary>A prop was placed for the first time.</summary>
        public static void PropPlaced(WorldSession session, uint propId)
        {
            // "<prop> placed" goals (e.g. "Padded Sofa placed"), named like the items that place the prop, with the
            // goal texts' word orders and plurals (PlacedNames, Achievements.Triggers.cs: "Rug (Large Rectangular)" →
            // "Large Rectangular Rug placed", "Mug" → "Mugs placed").
            ProgressByTexts(session, PlacedNames(propId).Select(n => n + " placed"));
            PlacedTriggers(session, propId);
            TutorialPropPlaced(session, propId);   // Getting Crafty: the Replicator (Achievements.Tutorial.cs)
            if (simpleStools.Value.Contains(propId))
                Progress(session, PlaceSimpleStool);
            else if (placeObjectives.TryGetValue(propId, out uint objective))
                Progress(session, objective);
            else if (propId != 0 && Inventory.ItemDefinitionManager.Get(BarrenFlamebladeItem)?.PropId == propId)
                Progress(session, PlaceFlameblade);
        }

        /// <summary>
        /// The player finished crafting an item (one crafting job of <paramref name="crafts"/> crafts). Only the tutorial
        /// goals count the crafts (Achievements.Tutorial.cs); the other crafting objectives count one per job, as before.
        /// </summary>
        public static void Crafted(WorldSession session, uint itemDefinitionId, uint crafts = 1)
        {
            TutorialCrafted(session, itemDefinitionId, Math.Max(crafts, 1));
            if (itemDefinitionId == DungeonStarterSetItem)
                Progress(session, CraftDungeonStarterSet);
            if (CraftObjectives.TryGetValue(itemDefinitionId, out List<uint> objectives))
                foreach (uint objective in objectives)
                    Progress(session, objective);
            // "Extractor acquired" (Power! Unlimited ...Power!): the NovaTech Extractor is the 0.589 extractor.
            if (ItemName(itemDefinitionId).Contains("Extractor", StringComparison.OrdinalIgnoreCase))
                ProgressByText(session, "Extractor acquired");
            CraftedTriggers(session, itemDefinitionId);
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
                        string itemName = text[..^suffix.Length].Trim();
                        // Plural counts ("Lightstones crafted") name the item in the plural.
                        if (!itemsByName.Contains(itemName) && itemName.EndsWith('s'))
                            itemName = itemName[..^1];
                        foreach (uint item in itemsByName[itemName])
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
        /// <summary>"Designs placed in a Build Site" of the tier 531 All Of Them ...Better Worlds (id inferred, Achievements.Tiers.cs).</summary>
        private const uint DesignsPlacedTier2 = 784;

        /// <summary>
        /// Creatures that count as "Monsters" for "Monsters killed" (TASKS 5.11): the Chaos Caverns monsters, by the
        /// names their kinds carry — "Slaug" (Wood / Rotten / Magma … Slaug, the story spawners' "Slaug"), "Abomination"
        /// and "Chomper" (Forest / Putrid / Scorched … Chomper). Reconciled sources:
        /// <list type="bullet">
        /// <item>wiki They Got In My Way / Hunting the Beasts / Murderer of Monsters: monsters are Slaugs,
        /// Abominations, Chompers and the exploding mushrooms, and nothing else counts;</item>
        /// <item>P4801 (26 Apr 2016, after the launch-era wiki text): Fiery Fungi stop counting for any kill goal. The
        /// later patch note wins for the mushrooms: a Fiery Fungus never counts here (it counts only for
        ///   "Dispatch explode-y mushrooms", <see cref="FungusDied"/>, P5753).</item>
        /// </list>
        /// Barren, Scalewind, Highfall, NovaTech, Ebonguard and Stoneskin characters, kobolds, animals and other story
        /// creatures are not monsters: they count for their own "&lt;Family&gt; killed" goals only.
        /// </summary>
        public static bool IsCaveMonster(string creatureName)
        {
            string n = creatureName ?? "";
            return !IsFungus(n) && (n.Contains("Slaug", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Abomination", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Chomper", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// A creature kill: "Monsters killed" (every tier with that text) for cave monsters only (<see cref="IsCaveMonster"/>,
        /// TASKS 5.11), the creature family's "&lt;Family&gt; killed" (Barren, Scalewind, Highfall, NovaTech, Ebonguard,
        /// Stoneskin — the names the creature kinds carry), "Kills with a(n) &lt;Element&gt; weapon" for the held weapon's
        /// affinity (Physical is the Earth line, as for the daily wielder goals), and the kill-in-a-biome goals
        /// (<see cref="KilledTriggers"/>). Fiery Fungi count for none of them (P4801, 26 Apr 2016): their deaths count
        /// only toward "You Can Kill Those Things"
        /// (<see cref="FungusDied"/>).
        /// </summary>
        public static void Killed(WorldSession session, string creatureName = "")
        {
            string name = creatureName ?? "";
            if (IsFungus(name))
                return;
            if (IsCaveMonster(name))
                ProgressByText(session, "Monsters killed");
            // Cry Havoc! 838: a Barren Flameblade (creature prop 2451 "Barren Flameblade", placed from item 3162 for 745)
            // killed; the story creature's kind carries the prop's name (Creatures.SpawnerKind).
            if (name.Contains("Flameblade", StringComparison.OrdinalIgnoreCase))
                Progress(session, DefeatFlameblades);
            KilledTriggers(session, name);
            foreach (string family in new[] { "Barren", "Scalewind", "Highfall", "NovaTech", "Ebonguard", "Stoneskin" })
                if (name.Contains(family, StringComparison.OrdinalIgnoreCase))
                    ProgressByText(session, family + " killed");
            string element = HitEffects.HeldAffinity(session) switch { "Physical" => "Earth", null => null, var a => a };
            if (!string.IsNullOrEmpty(element))
            {
                ProgressByText(session, $"Kills with a {element} weapon");
                ProgressByText(session, $"Kills with an {element} weapon");
            }
        }

        public static void Traded(WorldSession session) => Progress(session, ItemTraded);

        /// <summary>"Give or receive a Design in trade" (I Made This!): a Player Design item changed hands.</summary>
        public static void DesignTraded(WorldSession session) => ProgressByText(session, "Give or receive a Design in trade");

        /// <summary>"Guild joined" (Strength in Numbers): joining or founding a guild.</summary>
        public static void GuildJoined(WorldSession session) => ProgressByText(session, "Guild joined");

        /// <summary>"Treasure Chests opened" (Raider of Riches): a cave treasure chest opened.</summary>
        public static void TreasureChestOpened(WorldSession session) => ProgressByText(session, "Treasure Chests opened");

        /// <summary>"Design item created" (I Definitely Made This): a design exported to a Player Design item.</summary>
        public static void DesignItemCreated(WorldSession session) => ProgressByText(session, "Design item created");

        /// <summary>"Build Site "Like" given or received" (Hooked on Build Site Likes): both the voter and the owner.</summary>
        public static void LikeGivenOrReceived(WorldSession session) => ProgressByText(session, "Build Site \"Like\" given or received");

        /// <summary>
        /// A Like given to a site with these tags (Build Site Management tags, the client's words 576620-576645: Aquatic,
        /// Fantasy, Modern, Scary, SciFi, Steampunk, Underground, Western, City, Dungeon, Fortress, Home, Guildhall, Inn,
        /// Restaurant, Sculpture, Shop, Theater, Other, In-Progress, Combat, Socialize, Story, Trade, Tutorial, Puzzle):
        /// '"Like" a(n) &lt;Tag&gt; Build Site' for each (What I've Seen With These Eyes, Wander Round-a-Round-a-Round,
        /// Swords to Plowshares).
        /// </summary>
        public static void LikedTaggedSite(WorldSession session, string tags)
        {
            foreach (string tag in (tags ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).Distinct())
            {
                ProgressByText(session, $"\"Like\" a {tag} Build Site");
                ProgressByText(session, $"\"Like\" an {tag} Build Site");
                if (RiddleLikes.TryGetValue(tag, out uint riddle))
                    Progress(session, riddle);
            }
        }

        /// <summary>"Build Sites visited" (All Those Who Wander) and "2 unique Build Sites visited" (Baby Steps).</summary>
        public static void SiteVisited(WorldSession session)
        {
            ProgressByText(session, "Build Sites visited");
            ProgressByText(session, "2 unique Build Sites visited");
        }

        /// <summary>"Scan completed" (Scanning, Captain): a Holoseeker scan.</summary>
        public static void ScanCompleted(WorldSession session) => ProgressByText(session, "Scan completed");
        public static void DesignPlaced(WorldSession session, string name = "")
        {
            Progress(session, DesignsPlacedTier2);   // 531 All Of Them ...Better Worlds, once 530 is complete (first: see Harvested)
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
        /// 575535 "has obtained the unobtainable!" most likely belongs to Strip Miner [I] (trace/0695/agent-leads2-c.md L46;
        /// neither it nor Treasure Hunter is awarded in 0.589).
        /// <para>The combat titles (since 2026-10-26, trace/0695/phaseE3-text.md F2): 576045-576058 ("[*player*] has become
        /// a Monster Slayer!" ... "a Tempest!") follow the combat title items 3908-3914 and 3916-3922 in order, skipping
        /// Warmaster 3915, which has its capstone line [P texts and items; I pairing by order, and that live announced
        /// them]. 576054 says "Darkener" for the Darkwalker title (3918). Tiers.cs awards all fourteen.</para>
        /// </summary>
        private static readonly Dictionary<uint, uint> AnnouncedTitles = new()
        {
            [3900] = 575528, [3901] = 575529, [3902] = 575530, [3907] = 575531, [3915] = 575532, [3923] = 575533,
            [3926] = 575534, [3927] = 575536, [3928] = 575537, [3929] = 575546,
            // Combat titles.
            [3908] = 576045, [3909] = 576046, [3910] = 576047, [3911] = 576048, [3912] = 576049, [3913] = 576050,
            [3914] = 576051, [3916] = 576052, [3917] = 576053, [3918] = 576054, [3919] = 576055, [3920] = 576056,
            [3921] = 576057, [3922] = 576058,
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
            StopTrackingCompleted(session, a.Id);
            // Meta goals: "<achievement name> completed" — only for objectives no capture map names. The maps below are
            // the live link; by text alone, 656 A Light in the Darkness (500 Lightstones) would also tick There Can Be
            // Only One's 1091 "A Light in the Darkness completed", which the map gives to 606 Beacon of Hope (the 10
            // Radiance kills), and finishing a Water tier would tick both 1086 and 1087 (goals audit 2026-10-16).
            ProgressByText(session, (LandmarkEmulator.Shared.GameTable.Text.TextManager.Instance.GetTextForId(a.NameId) ?? "") + " completed", 1, unmappedOnly: true);
            foreach ((uint item, uint count) in a.Rewards)
                session.GiveItem(item, Math.Max(count, 1));
            log.Info($"{session.CharacterName} completed achievement {a.Id}.");
            foreach ((uint item, _) in a.Rewards)
                if (AnnouncedTitles.TryGetValue(item, out uint announce))
                    Announce(session, a, announce);
            // Meta goals by the capture's own objective → child map (TASKS 5.9): also covers the children whose
            // objective text differs from their name ("If It Bleeds ... Completed", "Between a Rock ... Completed").
            if (metaObjectivesByChild.TryGetValue(a.Id, out List<uint> metaObjectives))
                foreach (uint objective in metaObjectives)
                    Progress(session, objective);
            // The next goal tier ("Unlocks ..." on the wiki pages) arrives as AchievementAdd.
            if (unlockedByTier.TryGetValue(a.Id, out List<Achievement> unlocked))
                foreach (Achievement tier in unlocked)
                    SendAdd(session, tier);

            int chain = Array.IndexOf(GettingStarted, a.Id);
            if (chain < 0 || chain + 1 >= GettingStarted.Length || Get(GettingStarted[chain + 1]) is not Achievement next)
                return;
            lock (sync)
                foreach (Objective o in next.Objectives)
                {
                    // A tutorial step goal starts with its first step only (Achievements.Tutorial.cs).
                    if (IsStepGoal(next.Id))
                    {
                        ActivateNextStep(session.PlayerGuid, next, saves, session);
                        break;
                    }
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
        /// <summary>
        /// A character's Luminary Score, as its own client works it out (AchievementInfo "TotalPoints"): the points of every
        /// goal it has completed. Known for characters whose progress is loaded (in the world).
        /// </summary>
        public static uint LuminaryScore(ulong character)
        {
            lock (sync)
                return (uint)achievements.Values.Where(a => IsComplete(character, a)).Sum(a => (long)a.Points);
        }

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
