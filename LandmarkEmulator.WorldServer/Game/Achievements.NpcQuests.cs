using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Custom Journal goals offered by world NPCs (Game.Npc.WorldNpcs). Built like tutorial goals from a captured
    /// record, preferring the live "Dispatch explode-y mushrooms" text ids so the Journal shows readable copy.
    /// </summary>
    public static partial class Achievements
    {
        /// <summary>Scout's Errand — defeat 3 Fiery Fungus (Fungus Scout).</summary>
        public const uint ScoutsErrand = 9101;
        public const uint ScoutsErrandObjective = 9111;

        /// <summary>Herbalist's Gift — accept a thank-you (count 1) after the Scout quest.</summary>
        public const uint HerbalistsGift = 9102;
        public const uint HerbalistsGiftObjective = 9112;

        private static readonly HashSet<uint> npcQuestIds = new() { ScoutsErrand, HerbalistsGift };

        private sealed record NpcQuestGoal(uint Id, uint ObjectiveId, uint Required, uint Lumens);

        private static readonly NpcQuestGoal[] NpcQuests =
        {
            new(ScoutsErrand, ScoutsErrandObjective, 3, 30),
            new(HerbalistsGift, HerbalistsGiftObjective, 1, 15),
        };

        private const int NpcObjectiveTailBytes = 45;

        /// <summary>Called from <see cref="Load"/> after tutorial goals.</summary>
        private static void AddNpcQuests()
        {
            Achievement textSource = FindByObjectiveText("Dispatch explode-y mushrooms");
            if (!achievements.TryGetValue(471, out Achievement layout) || layout.Objectives.Count == 0)
            {
                log.Warn("NPC quests: model 471 missing; not built.");
                return;
            }
            uint nameId = textSource?.NameId ?? layout.NameId;
            uint descriptionId = BinaryPrimitives.ReadUInt32LittleEndian(template.AsSpan((textSource ?? layout).RecordStart + 9));
            uint objectiveTextId = textSource?.Objectives[0].TextId ?? layout.Objectives[0].TextId;

            var extra = new List<byte[]>();
            foreach (NpcQuestGoal goal in NpcQuests)
            {
                if (achievements.ContainsKey(goal.Id))
                    continue;
                extra.Add(NpcQuestRecord(goal, layout, nameId, descriptionId, objectiveTextId));
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
            log.Info($"{extra.Count} NPC quest goal(s) made.");
        }

        private static Achievement FindByObjectiveText(string text)
        {
            string want = text.Trim().ToLowerInvariant();
            foreach (Achievement a in achievements.Values)
                foreach (Objective o in a.Objectives)
                {
                    string t = Shared.GameTable.Text.TextManager.Instance.GetTextForId(o.TextId)?.Trim().ToLowerInvariant();
                    if (t == want)
                        return a;
                }
            return null;
        }

        private static byte[] NpcQuestRecord(NpcQuestGoal goal, Achievement model, uint nameId, uint descriptionId, uint objectiveTextId)
        {
            int rewardStart = model.ProgressOffset + 4;
            byte[] head = template[model.RecordStart..rewardStart];
            BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(0), goal.Id);
            BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(5), nameId);
            BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(9), descriptionId);
            head.AsSpan(model.StartedOffset - model.RecordStart, 8 + 8 + 4).Clear();

            byte[] rewards = NpcQuestRewards(template[rewardStart..model.RewardEnd], goal.Lumens);

            Objective first = model.Objectives[0];
            byte[] objectiveModel = template[first.KeyOffset..(first.StateOffset + NpcObjectiveTailBytes)];
            int state = first.StateOffset - first.KeyOffset;
            var objectives = new MemoryStream();
            objectives.Write(BitConverter.GetBytes(1u));
            byte[] o = (byte[])objectiveModel.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(0), goal.ObjectiveId);
            BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(4), goal.ObjectiveId);
            BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(8), objectiveTextId);
            BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(12), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(state), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(state + 4), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(state + 8), goal.Required);
            objectives.Write(o);

            int tailStart = model.Objectives[^1].StateOffset + NpcObjectiveTailBytes;
            byte[] tail = template[tailStart..model.RecordEnd];
            int points = model.PointsOffset - tailStart;
            BinaryPrimitives.WriteUInt32LittleEndian(tail.AsSpan(points), 5);
            BinaryPrimitives.WriteUInt32LittleEndian(tail.AsSpan(points + 38), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(tail.AsSpan(points + 46), 21552);
            BinaryPrimitives.WriteUInt32LittleEndian(tail.AsSpan(points + 50), 2000 + (goal.Id - 9100));

            var record = new MemoryStream();
            record.Write(head);
            record.Write(rewards);
            record.Write(objectives.ToArray());
            record.Write(tail);
            return record.ToArray();
        }

        /// <summary>Lumen-only reward bundle (same layout as <c>TutorialRewards</c>).</summary>
        private static byte[] NpcQuestRewards(byte[] model, uint lumens)
        {
            int mapCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(model.AsSpan(5));
            int headerEnd = 1 + 4 + 4 + mapCount * 8 + 12 + 8 + 20 + 16;
            var output = new MemoryStream();
            var w = new BinaryWriter(output);
            w.Write(model, 0, headerEnd);
            w.Write(LumenIcon);
            w.Write(LumenName);
            w.Write(1u);
            w.Write(1u);
            w.Write((byte)0);
            w.Write(LumenIcon);
            w.Write(0u);
            w.Write(LumenName);
            w.Write(lumens);
            w.Write(Lumen);
            w.Write(0u);
            w.Write(0u);
            w.Write(0u);
            w.Write((byte)0);
            w.Write(0u);
            w.Write(0u);
            w.Write(1051u);
            w.Flush();
            return output.ToArray();
        }

        /// <summary>NPC quests stay out of Initialize until offered (objective state ≠ 0).</summary>
        private static bool NpcQuestVisible(ulong character, Achievement a)
        {
            if (!npcQuestIds.Contains(a.Id))
                return true;
            lock (sync)
                return a.Objectives.Any(o => StateOf(character, o, a).State != 0);
        }

        /// <summary>Offer a world-NPC quest: activate objective, AchievementAdd, tracker refresh.</summary>
        public static void OfferNpcQuest(WorldSession session, uint achievementId, uint objectiveId)
        {
            if (!achievements.TryGetValue(achievementId, out Achievement a))
            {
                log.Warn($"OfferNpcQuest: unknown achievement {achievementId}.");
                return;
            }
            var saves = new List<CharacterObjectiveModel>();
            Objective o = a.Objectives.FirstOrDefault(x => x.Id == objectiveId) ?? a.Objectives[0];
            lock (sync)
            {
                var (state, _) = StateOf(session.PlayerGuid, o, a);
                if (state == 2)
                    return;
                if (state == 0)
                {
                    Set(session.PlayerGuid, a, o, 1, 0, saves);
                    session.EnqueueMessage(ZoneMessageOpcode.AchievementBase, Build(ActivatedSub, w =>
                    {
                        w.Write(a.Id); w.Write(o.Id); w.Write(0u); w.Write(Math.Max(o.Required, 1)); w.Write(0u); w.Write(false);
                    }));
                }
            }
            Save(saves);
            SendAdd(session, a);
            SendTracker(session);
            log.Info($"{session.CharacterName}: offered NPC quest {achievementId}.");
        }

        public static bool NpcQuestReady(ulong character, uint achievementId, uint objectiveId)
        {
            if (!achievements.TryGetValue(achievementId, out Achievement a))
                return false;
            Objective o = a.Objectives.FirstOrDefault(x => x.Id == objectiveId) ?? a.Objectives.FirstOrDefault();
            if (o == null)
                return false;
            lock (sync)
            {
                var (state, current) = StateOf(character, o, a);
                return state == 2 || current >= Math.Max(o.Required, 1);
            }
        }

        public static void CompleteNpcQuest(WorldSession session, uint achievementId)
        {
            if (!achievements.TryGetValue(achievementId, out Achievement a) || a.Objectives.Count == 0)
                return;
            Objective o = a.Objectives[0];
            if (!IsComplete(session.PlayerGuid, achievementId))
                Progress(session, o.Id, Math.Max(o.Required, 1));
        }

        public static void ResetNpcQuest(WorldSession session, uint achievementId)
        {
            if (!achievements.ContainsKey(achievementId))
                return;
            ResetObjectives(session.PlayerGuid, achievementId, 0);
            log.Info($"{session.CharacterName}: NPC quest {achievementId} reset.");
        }
    }
}
