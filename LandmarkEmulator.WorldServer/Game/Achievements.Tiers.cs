using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Goal tiers the live AchievementInitialize capture does not hold (TASKS 5.6, trace/0695/wiki-content-audit.md §3.3,
    /// OPEN-LEADS L42). The capture only lists the goals its player had been given: a tier ("Hunting the Beasts", 150
    /// monsters) arrived from the server only when the tier before it ("They Got In My Way", 10) was complete — the wiki
    /// pages say "Unlocks …" / "Must complete … to unlock", and the client has a message for exactly that:
    /// AchievementAdd (AchievementBase 0x92 sub 2, one achievement record; dispatcher 0.589 FUN_142d92750 / 0.695
    /// FUN_142e30720 case 2 → reader FUN_142d8c370 / FUN_142e2a340 → handler FUN_142d8f4a0 / FUN_142e2d470).
    ///
    /// Each missing tier is made from the record of the first tier of its line (same layout, subcategory, parent meta
    /// goal and objective text), with:
    /// <list type="bullet">
    /// <item>id, name and description texts: the client's text table runs in achievement order inside each category
    ///   (e.g. 574 → name 575033 / description 575081, so 575 → 575034 "Hunting the Beasts" / 575082), checked against
    ///   every name and description text of the 38 tiers below (one exception, the Water line's swapped name texts, is
    ///   given explicitly in the table);</item>
    /// <item>objective id: the first tier's objective id + the tier's offset (574 → 1016, so 575 → 1017, 576 → 1018).
    ///   Every one of these ids is a gap in the capture's objective ids. INFERRED (the client never names them);</item>
    /// <item>required count, Lumens, Luminary Score points, title and recipe rewards: the wiki goal pages
    ///   (design/wiki/pages, re/out/agent_wiki/goals.tsv);</item>
    /// <item>the record's other per-reward fields follow the capture's records with the same reward: 10 points → field
    ///   h3[1] 10 and reward bundle id 934 (35 Lumens), 15 points → 11 and 935 (80 Lumens). INFERRED for the tiers with
    ///   extra title / recipe items (the capture's 489-491 and 551-552 keep the plain bundle id with an extra item);</item>
    /// <item>reward entries: Lumens first, then the recipe item, then the title item (order INFERRED; 542-655 put the
    ///   Lumens first), each with the item's own icon and name text (as the capture's title entries: icon 1755);</item>
    /// <item>icon: the first tier's (INFERRED); sort index: the first tier's + offset (the capture's indices run on).</item>
    /// </list>
    /// A new tier's objective starts active at 0 (INFERRED: whether live carried the lifetime count over is not known).
    /// Three more goals with client text but no record — 576237 Getting Crafty, 576265 Building Block Basics, 576271
    /// Modular Thinking (Getting Started) — are built in Achievements.Tutorial.cs (TASKS 6.57), after 685 in the chain.
    /// </summary>
    public static partial class Achievements
    {
        /// <summary>
        /// One missing tier: its id, the captured record it is made from (<paramref name="Model"/>, the first tier of the
        /// line), the tier that unlocks it, the wiki's count, points and Lumens, extra reward items (recipe, title) and,
        /// when they differ from the model's + offset, the objective text and the name text.
        /// </summary>
        private sealed record Tier(uint Id, uint Model, uint After, uint Required, uint Points, uint Lumens, uint[] Items,
            uint ObjectiveText = 0, uint NameText = 0);

        private static readonly uint[] None = Array.Empty<uint>();

        /// <summary>Title items (wiki rewards; item names "Title: …" in the client's item definitions).</summary>
        private const uint MonsterSlayer = 3908, BarrenBanisher = 3909, ScalewindScourge = 3910, HighfallHorror = 3911,
            NovaTechNullifier = 3912, EbonguardEradicator = 3913, StoneskinSmasher = 3914, Flamekin = 3916, Iceguard = 3917,
            Darkwalker = 3918, Brightshield = 3919, Assassin = 3920, Destroyer = 3921, Tempest = 3922, TreasureHunter = 3924;

        /// <summary>Recipe items the 500-kill weapon tiers give (wiki "Recipe: …").</summary>
        private const uint DragonfireRitualRobes = 4023, ReinforcedCoatOfCrashingWaves = 4003, SilentShadowsCloak = 4008,
            LightbringersOath = 4007, DesperadosDeadlyPoncho = 4006, TectonicDestroyersPlatemail = 4026,
            OverchargedReconGear = 3990;

        /// <summary>The missing tiers, by line. Counts and rewards: wiki goal pages; ids and links: see the class notes.</summary>
        private static readonly Tier[] Tiers =
        {
            // Slayer of All the Things (595) children: "<X> killed" 10 → 150 → 500.
            new(575, 574, 574, 150, 10, 35, None),                       // Hunting the Beasts (Monsters killed)
            new(576, 574, 575, 500, 15, 80, new[] { MonsterSlayer }),    // Murderer of Monsters
            new(578, 577, 577, 150, 10, 35, None),                       // Rattling the Bones (Barren killed)
            new(579, 577, 578, 500, 15, 80, new[] { BarrenBanisher }),   // Skeleton Scourge
            new(581, 580, 580, 150, 10, 35, None),                       // Me YES Take Candle (Scalewind killed)
            new(582, 580, 581, 500, 15, 80, new[] { ScalewindScourge }), // Die, Vermin!
            new(584, 583, 583, 150, 10, 35, None),                       // Tis But a Scratch (Highfall killed)
            new(585, 583, 584, 500, 15, 80, new[] { HighfallHorror }),   // Have At Thee!
            new(587, 586, 586, 150, 10, 35, None),                       // Reverse Their Polarity (NovaTech killed)
            new(588, 586, 587, 500, 15, 80, new[] { NovaTechNullifier }),// Temporal Paradox
            new(590, 589, 589, 150, 10, 35, None),                       // They Must Be Bad Guys (Ebonguard killed)
            new(591, 589, 590, 500, 15, 80, new[] { EbonguardEradicator }), // Sorry, Salvatore
            new(593, 592, 592, 150, 10, 35, None),                       // If It Bleeds (Stoneskin killed)
            new(594, 592, 593, 500, 15, 80, new[] { StoneskinSmasher }), // They're in Rubble Now
            // There Can Be Only One (618) children: "Kills with a(n) <Element> weapon" 10 → 150 → 500.
            // The 500-kill recipe rewards are the ones after the Hotfix Notes of 11 May 2016 (patchnotes.tsv P5327-P5332 /
            // P5436-P5441, older ids P4029-P4034 / P4140-P4143; TASKS 6.10), which the wiki goal pages also give, and no
            // later note changes them: H2-Ouch → Reinforced Coat of Crashing Waves (was Frost Lord's Guard, 4024),
            // Eternal Night → Silent Shadow's Cloak (was Night Herald's Promise, 4029), Praise the Light →
            // Lightbringer's Oath (was Crusader's Calling, 4028), Assassin's Choice → Desperado's Deadly Poncho (was
            // Blight-Warden's Suffering, 4027), 1.21 ...Something → Overcharged Recon Gear (was Vestment of the
            // Lightning Caller, 4025). None of these tiers is in the live capture, so nothing older overrides them.
            new(598, 597, 597, 150, 10, 35, None),                       // Burn, Baby, Burn! (Fire)
            new(599, 597, 598, 500, 15, 80, new[] { DragonfireRitualRobes, Flamekin }),         // KILL IT WITH FIRE
            // Water: the capture's own map of 618 There Can Be Only One (objective 1086 "Chill Out completed" → child 601,
            // 1087 "H2-Ouch completed" → 602) puts Chill Out (150) at 601 and H2-Ouch (500, the recipe and the Iceguard
            // title) at 602, but the name texts run the other way round (575060 H2-Ouch, 575061 Chill Out) while the
            // descriptions run in id order (601 "…'Cool down, pal'", 602 "You have become: the Iceguard!"): the name text
            // is given explicitly for these two (goals audit 2026-10-16, trace/0695/goals-audit-695.md).
            new(601, 600, 600, 150, 10, 35, None, NameText: 575061),     // Chill Out (Water)
            new(602, 600, 601, 500, 15, 80, new[] { ReinforcedCoatOfCrashingWaves, Iceguard }, NameText: 575060), // H2-Ouch
            new(604, 603, 603, 150, 10, 35, None),                       // My Inner Darkness (Shadow)
            new(605, 603, 604, 500, 15, 80, new[] { SilentShadowsCloak, Darkwalker }),          // Eternal Night
            new(607, 606, 606, 150, 10, 35, None),                       // Glory Seeker (Radiance)
            new(608, 606, 607, 500, 15, 80, new[] { LightbringersOath, Brightshield }),         // Praise the Light
            new(610, 609, 609, 150, 10, 35, None),                       // Killing Them Slowly (Affliction)
            new(611, 609, 610, 500, 15, 80, new[] { DesperadosDeadlyPoncho, Assassin }),        // Assassin's Choice
            new(613, 612, 612, 150, 10, 35, None),                       // Between a Rock (Earth)
            new(614, 612, 613, 500, 15, 80, new[] { TectonicDestroyersPlatemail, Destroyer }),  // Seismically Snuffed Out
            new(616, 615, 615, 150, 10, 35, None),                       // Overcharged (Lightning)
            new(617, 615, 616, 500, 15, 80, new[] { OverchargedReconGear, Tempest }),           // 1.21 ...Something
            // Social: Lean On Me 504 → A Little Help From My Friends 505 → Wind Beneath My Wings ("Spend 5 hours
            // grouped up", text 574913, counted in seconds by Update); All Those Who Wander 508 → I've Been Everywhere.
            new(506, 505, 505, 18000, 15, 80, None, 574913),
            new(509, 508, 508, 25, 15, 80, None),
            // Building: Who Makes the World? 512 → I Make the World; That Doesn't Go There 514 → This Is All Wrong;
            // Building Better Worlds 530 → All Of Them ...Better Worlds (the wiki names no link for this one: INFERRED
            // from the name, the text order and the 10 → 100 counts).
            new(513, 512, 512, 5_000_000, 15, 80, None),
            new(515, 514, 514, 5_000_000, 15, 80, None),
            new(531, 530, 530, 100, 15, 80, None),
            // Exploration: Speak Friend and Enter 622 → Drums in the Deep → You've Delved Too Deep! ("Lost Crystal
            // reached": the client has no plural text); Raider of Riches 625 → Explorer of the Uncharted → Professor of
            // Archaeology (Title: Treasure Hunter); It's Not Just for Dwarves 629 → I Feel the Earth ...Move.
            new(623, 622, 622, 25, 10, 35, None),
            new(624, 622, 623, 250, 15, 80, None),
            new(626, 625, 625, 25, 10, 35, None),
            new(627, 625, 626, 100, 15, 80, new[] { TreasureHunter }),
            new(630, 629, 629, 500_000, 10, 35, None),
        };

        private static readonly Dictionary<uint, uint> tierPredecessors = Tiers.ToDictionary(t => t.Id, t => t.After);

        /// <summary>
        /// The tier (or, for the three tutorial goals of Achievements.Tutorial.cs, the goal) that unlocks
        /// <paramref name="id"/>, or 0 for a goal everyone has.
        /// </summary>
        private static uint TierPredecessor(uint id) => tierPredecessors.TryGetValue(id, out uint after) ? after : TutorialPredecessor(id);

        /// <summary>Tier → the tiers its completion adds; child goal → the meta objectives counting it (capture maps).</summary>
        private static Dictionary<uint, List<Achievement>> unlockedByTier = new();
        private static Dictionary<uint, List<uint>> metaObjectivesByChild = new();
        /// <summary>Every objective a capture map names: counted by its child goal's completion, never by text.</summary>
        private static HashSet<uint> mappedObjectives = new();

        /// <summary>Builds the link indexes after every parse.</summary>
        private static void IndexLinks()
        {
            var unlocked = new Dictionary<uint, List<Achievement>>();
            var meta = new Dictionary<uint, List<uint>>();
            var mapped = new HashSet<uint>();
            foreach (Achievement a in achievements.Values.OrderBy(a => a.Id))
            {
                if (a.UnlockedBy != 0)
                {
                    if (!unlocked.TryGetValue(a.UnlockedBy, out var list))
                        unlocked[a.UnlockedBy] = list = new List<Achievement>();
                    list.Add(a);
                }
                foreach ((uint objective, uint child) in a.Map)
                {
                    if (!meta.TryGetValue(child, out var list))
                        meta[child] = list = new List<uint>();
                    list.Add(objective);
                    mapped.Add(objective);
                }
            }
            unlockedByTier = unlocked;
            metaObjectivesByChild = meta;
            mappedObjectives = mapped;
        }

        /// <summary>
        /// Does the character have this goal? Every goal but a tier whose previous tier isn't complete. Call under
        /// <see cref="sync"/>.
        /// </summary>
        private static bool IsAvailable(ulong character, Achievement a)
        {
            // World-NPC quests stay hidden until OfferNpcQuest activates an objective (Achievements.NpcQuests.cs).
            if (!NpcQuestVisible(character, a))
                return false;
            return a.UnlockedBy == 0 || (achievements.TryGetValue(a.UnlockedBy, out Achievement before) && IsComplete(character, before));
        }

        /// <summary>Makes the <see cref="Tiers"/> records, appends them to the template and parses everything again.</summary>
        private static void AddTiers()
        {
            var extra = new List<byte[]>();
            foreach (Tier tier in Tiers)
            {
                if (achievements.ContainsKey(tier.Id))
                    continue;                          // a capture that has it wins
                if (!achievements.TryGetValue(tier.Model, out Achievement model) || model.Objectives.Count != 1)
                {
                    log.Warn($"Goal tier {tier.Id}: model record {tier.Model} missing or not a one-objective goal.");
                    continue;
                }
                extra.Add(TierRecord(tier, model));
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
            log.Info($"{extra.Count} goal tiers made (AchievementAdd when the tier before them completes).");
        }

        /// <summary>One tier's record: the model record's head and tail, patched, around a new reward bundle.</summary>
        private static byte[] TierRecord(Tier tier, Achievement model)
        {
            uint offset = tier.Id - model.Id;
            int rewardStart = model.ProgressOffset + 4;

            // Head: id, u8, name, description, started, completed, progress.
            byte[] head = template[model.RecordStart..rewardStart];
            uint nameText = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(5));
            uint descriptionText = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(9));
            BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(0), tier.Id);
            BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(5), tier.NameText != 0 ? tier.NameText : nameText + offset);
            BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(9), descriptionText + offset);
            head.AsSpan(model.StartedOffset - model.RecordStart, 8 + 8 + 4).Clear();

            byte[] rewards = TierRewards(template[rewardStart..model.RewardEnd], tier);

            // Tail: the objective list and the record's trailing fields.
            byte[] tail = template[model.RewardEnd..model.RecordEnd];
            int at(int templateOffset) => templateOffset - model.RewardEnd;
            Objective o = model.Objectives[0];
            uint objectiveId = o.Id + offset;
            BinaryPrimitives.WriteUInt32LittleEndian(tail.AsSpan(at(o.KeyOffset)), objectiveId);      // list key
            BinaryPrimitives.WriteUInt32LittleEndian(tail.AsSpan(at(o.KeyOffset) + 4), objectiveId);  // id
            if (tier.ObjectiveText != 0)
                BinaryPrimitives.WriteUInt32LittleEndian(tail.AsSpan(at(o.KeyOffset) + 8), tier.ObjectiveText);
            BinaryPrimitives.WriteUInt32LittleEndian(tail.AsSpan(at(o.StateOffset)), 1);              // active once given
            BinaryPrimitives.WriteUInt32LittleEndian(tail.AsSpan(at(o.CurrentOffset)), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(tail.AsSpan(at(o.CurrentOffset) + 4), tier.Required);
            int points = at(model.PointsOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(tail.AsSpan(points), tier.Points);
            // h3[1] (points + 18): 8 / 10 / 11 / 12 for 5 / 10 / 15 / 20 points in the capture.
            BinaryPrimitives.WriteUInt32LittleEndian(tail.AsSpan(points + 18), tier.Points >= 15 ? 11u : 10u);
            // h3[9] (points + 50): the sort index, running on from the model's.
            uint sort = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(points + 50));
            BinaryPrimitives.WriteUInt32LittleEndian(tail.AsSpan(points + 50), sort + offset);

            var record = new MemoryStream();
            record.Write(head);
            record.Write(rewards);
            record.Write(tail);
            return record.ToArray();
        }

        /// <summary>The capture's Lumen reward entry: icon 12147, name text 565430 (item 1579's).</summary>
        private const uint Lumen = 1579, LumenIcon = 12147, LumenName = 565430;

        /// <summary>
        /// A reward bundle (FUN_142b95550) for a tier: the model bundle's header (bool, u32, map, 3 × u32, u64, 5 × u32,
        /// 2 × u64) kept, then the shown reward (icon, name text of the first entry), the item entries and the bundle id.
        /// Entries are type 1 (Item) in the base layout FUN_143ac4130 — bool, icon, u32, name text, count, item, u32,
        /// string, u32, bool, u32, u32 — as in the capture (no item definition: the bundle's "full" flag is clear).
        /// </summary>
        private static byte[] TierRewards(byte[] model, Tier tier)
        {
            int mapCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(model.AsSpan(5));
            int headerEnd = 1 + 4 + 4 + mapCount * 8 + 12 + 8 + 20 + 16;
            var entries = new List<(uint Icon, uint Name, uint Count, uint Item)> { (LumenIcon, LumenName, tier.Lumens, Lumen) };
            foreach (uint item in tier.Items)
            {
                var d = Inventory.ItemDefinitionManager.Get(item);
                if (d == null)
                {
                    log.Warn($"Goal tier {tier.Id}: reward item {item} has no definition; left out.");
                    continue;
                }
                entries.Add((d.ImageSetId, d.NameId, 1, item));
            }
            var output = new MemoryStream();
            var w = new BinaryWriter(output);
            w.Write(model, 0, headerEnd);
            w.Write(entries[0].Icon);
            w.Write(entries[0].Name);
            w.Write((uint)entries.Count);
            foreach (var e in entries)
            {
                w.Write(1u);                 // type 1 Item
                w.Write((byte)0);
                w.Write(e.Icon);
                w.Write(0u);
                w.Write(e.Name);
                w.Write(e.Count);
                w.Write(e.Item);
                w.Write(0u);
                w.Write(0u);                 // empty string
                w.Write(0u);
                w.Write((byte)0);
                w.Write(0u);
                w.Write(0u);
            }
            w.Write(tier.Points >= 15 ? 935u : 934u);   // bundle id (INFERRED, see the class notes)
            w.Flush();
            return output.ToArray();
        }
    }
}
