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
    /// The three advanced tutorial goals the wiki lists but the Feb 2017 AchievementInitialize capture lacks (TASKS 6.57,
    /// OPEN-LEADS L42, trace/0695/goals-audit-695.md "Tutorial goals built from the wiki"): Getting Crafty, Building Block
    /// Basics and Modular Thinking. Evidence: <b>[P]</b> client text / table / decompile / capture pattern, <b>[W]</b> wiki
    /// page, <b>[N]</b> patch note or developer post, <b>[I]</b> inferred.
    ///
    /// <para><b>Where they sit.</b> Three more advanced tutorial goals about crafting and the block building tools were
    /// added, of which only the first could be completed at first (26 Apr 2016, P4787); Building Block Basics is
    /// followed by Modular Thinking (29 Apr, P4994), the last building tutorial (dev post, forum 57063) [N]: Getting
    /// Crafty → Building Block Basics → Modular Thinking. They follow 685 Becoming a
    /// Luminary, the last goal of the captured chain [I]: the captured Getting Started records carry their predecessor in
    /// the record's 7th u32 of the 10 after the points field (477 → 478, 478 → 480, 685 → 477; no other record uses
    /// it) [P], and the captured player had not finished 685 (objective 1319 still active), which is why the goals after
    /// it are not in the capture (a goal arrives only once the one before it is complete, as the tiers do,
    /// Achievements.Tiers.cs) [I]. So each is sent with AchievementAdd when the one before it completes, its record names
    /// that one as predecessor, and the Getting Started tracker moves on to it (<see cref="GettingStarted"/>).</para>
    ///
    /// <para><b>Records.</b> Made from 471 Your Home Is Your Castle (same layout, subcategory 235 Getting Started,
    /// tutorial quality 7, bundle id 1051 like every captured Getting Started record) [P], with: ids of ours that no
    /// client table or capture uses (achievements 9001-9003, objectives 9011-9037: the capture's achievements end at
    /// 1490 and its objectives at 2209, Recipes.TRACKER / VISIBILITY ids end at 1584); the client's own name, description
    /// and objective texts (576237-576292, 577049; in both 0.589 and 0.695) [P]; counts and the 30 Lumens / 5 Luminary
    /// Score reward from the wiki pages [W]; sort indices 1006-1008 after the captured 1001-1005 [I]; icons of the
    /// matching captured goals (649 That's So Metal, 512 Who Makes the World?, 530 Building Better Worlds) [I].</para>
    ///
    /// <para><b>Steps.</b> The objectives come one at a time, in order (ObjectiveActivated when the one before is done):
    /// Swapping the selection and paint steps of Building Block Basics (11 May 2016, P5324) only means something if the
    /// order matters, and a player describes Modular Thinking as steps that follow one another (forum 57064 #138)
    /// [N→I]. The order: Getting Crafty as the wiki lists it; Building Block Basics in text order
    /// with selection before paint (P5324); Modular Thinking in text order with the final-pass Entry step (577049, which
    /// replaced the doorway copy 576286 / "select Paste with Interior Empty Space" 576290 that the wiki no longer lists)
    /// in the doorway's place, and Redo right after Undo (a new edit clears the redo list) [I].</para>
    ///
    /// <para><b>Rewards.</b> 30 Lumens and 5 points each [W]. The wiki's reward lists leave out designs (471 shows only
    /// Lumens there, the capture gives 13 castle designs), so: Building Block Basics also gives the 8 Modular Castle
    /// design items with a client file (4098-4105) — Modular Thinking asks the player to place the Modular Castle Long
    /// Room, Window and Entry, so they must come before it, and the wipe took the Modular Castle designs from every
    /// player so the tutorial could hand them out as intended (7 Jun 2016, P5750) [I,
    /// OPEN-LEADS U-DC3]; Modular Thinking gives the Architecture Tools: an updated set is its reward (25 May, P5544),
    /// two designs of odd / even nested arcs for finishing it, and the planned set of odd / even nested arcs and
    /// arches, angled lines and columns (Domino, forum 57063 #1 / #34) [N] = items 4358 / 4359 Architecture
    /// Tools - Even / Odd Radius Arcs, 4391 / 4392 Even / Odd Radius Arches, 1742 Angled Lines, 4393 Pillars [P names].</para>
    /// </summary>
    public static partial class Achievements
    {
        /// <summary>Our ids for the three goals (no capture / client table uses 9001-9003).</summary>
        public const uint GettingCrafty = 9001, BuildingBlockBasics = 9002, ModularThinking = 9003;

        /// <summary>Getting Crafty's objectives (texts 576246, 576247, 576256, 576249, 576250).</summary>
        private const uint PlaceReplicator = 9011, CraftCutPlainWood = 9012, CraftFarseerPick = 9013,
            CraftSmeltedCopper = 9014, CraftCutAgate = 9015;

        /// <summary>Building Block Basics' objectives (texts 576275-576284).</summary>
        private const uint OpenBlocksTab = 9021, UseAddTool = 9022, PlaceBlocks = 9023, UseDeleteTool = 9024,
            DeleteBlocks = 9025, UseSelectionTool = 9026, PaintSelection = 9027, CopyBlocks = 9028, PasteBlocks = 9029;

        /// <summary>Modular Thinking's objectives (texts 576285, 577049, 576287, 576292, 576288, 576289, 576291).</summary>
        private const uint PlaceLongRoom = 9031, PasteEntry = 9032, UndoBuild = 9033, RedoBuild = 9034,
            WindowSurroundingAir = 9035, WindowInteriorAir = 9036, WindowAsDelete = 9037;

        /// <summary>The captured record the three are made from: 471 Your Home Is Your Castle.</summary>
        private const uint TutorialModel = 471;

        /// <summary>Bytes of one objective record from its state field to its end (state … u8, u32; see Parse).</summary>
        private const int ObjectiveTailBytes = 45;

        /// <summary>One goal: texts, predecessor, icon, sort index, steps (objective id, text, count) and reward items.</summary>
        private sealed record TutorialGoal(uint Id, uint NameText, uint DescriptionText, uint After, uint Icon, uint Sort,
            (uint Id, uint Text, uint Required)[] Steps, uint[] Items);

        private static readonly TutorialGoal[] TutorialGoals =
        {
            new(GettingCrafty, 576237, 576238, 685, 21552, 1006, new[]
            {
                (PlaceReplicator, 576246u, 1u),      // Place the Replicator prop on your Build Site
                (CraftCutPlainWood, 576247u, 1u),    // Craft Cut Plain Wood
                (CraftFarseerPick, 576256u, 1u),     // Craft a Farseer Pick
                (CraftSmeltedCopper, 576249u, 3u),   // Craft Smelted Copper (3, wiki)
                (CraftCutAgate, 576250u, 2u),        // Craft Cut Agate (2, wiki)
            }, Array.Empty<uint>()),
            new(BuildingBlockBasics, 576265, 576266, GettingCrafty, 12095, 1007, new[]
            {
                (OpenBlocksTab, 576275u, 1u),        // Open the Blocks tab of Build Mode [Shift-4]
                (UseAddTool, 576276u, 1u),           // Use the Add Tool [1]
                (PlaceBlocks, 576277u, 200u),        // Place blocks (200, wiki)
                (UseDeleteTool, 576279u, 1u),        // Use the Delete Tool [2]
                (DeleteBlocks, 576280u, 200u),       // Delete blocks (200, wiki)
                (UseSelectionTool, 576282u, 1u),     // Use the Selection Tool [3] to select some blocks on your site
                (PaintSelection, 576281u, 1u),       // Use the Paint tool [4] on the selection volume ...
                (CopyBlocks, 576283u, 1u),           // Copy [Ctrl-C] some blocks you have selected ...
                (PasteBlocks, 576284u, 1u),          // Paste [Ctrl-V] the blocks you copied
            }, new uint[] { 4101, 4105, 4098, 4104, 4099, 4100, 4102, 4103 }),   // Modular Castle designs [I]
            new(ModularThinking, 576271, 576272, BuildingBlockBasics, 21567, 1008, new[]
            {
                (PlaceLongRoom, 576285u, 1u),        // Place the Modular Castle Long Room design ...
                (PasteEntry, 577049u, 1u),           // Use Fine Tuning [T] to paste the Modular Castle Entry into the wall ...
                (UndoBuild, 576287u, 1u),            // Undo [Ctrl-Z] your last build action
                (RedoBuild, 576292u, 1u),            // Redo [Ctrl-Y] your last build action
                (WindowSurroundingAir, 576288u, 1u), // ... "Modular Castle Window" ... Paste with Surrounding Empty Space
                (WindowInteriorAir, 576289u, 1u),    // ... Paste With Interior Empty Space into a wall
                (WindowAsDelete, 576291u, 1u),       // ... Paste as Delete into a wall
            }, new uint[] { 4358, 4359, 4391, 4392, 1742, 4393 }),               // Architecture Tools [N]
        };

        private static readonly Dictionary<uint, TutorialGoal> tutorialGoals = TutorialGoals.ToDictionary(g => g.Id);

        /// <summary>The goal that unlocks one of the three, or 0 for any other goal.</summary>
        private static uint TutorialPredecessor(uint id) => tutorialGoals.TryGetValue(id, out TutorialGoal g) ? g.After : 0;

        /// <summary>Goals whose objectives come one at a time, in order.</summary>
        private static bool IsStepGoal(uint id) => tutorialGoals.ContainsKey(id);

        /// <summary>Makes the three records from <see cref="TutorialModel"/>, appends them and parses everything again.</summary>
        private static void AddTutorialGoals()
        {
            if (!achievements.TryGetValue(TutorialModel, out Achievement model) || model.Objectives.Count == 0
                || model.Objectives[^1].StateOffset + ObjectiveTailBytes != model.PointsOffset - 12)
            {
                log.Warn($"Tutorial goals: model record {TutorialModel} missing or not in the expected layout; not built.");
                return;
            }
            var extra = new List<byte[]>();
            foreach (TutorialGoal goal in TutorialGoals)
                if (!achievements.ContainsKey(goal.Id))
                    extra.Add(TutorialRecord(goal, model));
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
            log.Info($"{extra.Count} tutorial goals made (Getting Crafty, Building Block Basics, Modular Thinking).");
        }

        /// <summary>One goal's record: the model's head, a new reward bundle, the goal's objectives, the model's tail patched.</summary>
        private static byte[] TutorialRecord(TutorialGoal goal, Achievement model)
        {
            int rewardStart = model.ProgressOffset + 4;
            byte[] head = template[model.RecordStart..rewardStart];
            BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(0), goal.Id);
            BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(5), goal.NameText);
            BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(9), goal.DescriptionText);
            head.AsSpan(model.StartedOffset - model.RecordStart, 8 + 8 + 4).Clear();

            byte[] rewards = TutorialRewards(template[rewardStart..model.RewardEnd], goal);

            // One objective record per step, from the model's first objective (its own reward bundle is kept as is).
            Objective first = model.Objectives[0];
            byte[] objectiveModel = template[first.KeyOffset..(first.StateOffset + ObjectiveTailBytes)];
            int state = first.StateOffset - first.KeyOffset;
            var objectives = new MemoryStream();
            objectives.Write(BitConverter.GetBytes((uint)goal.Steps.Length));
            foreach ((uint id, uint text, uint required) in goal.Steps)
            {
                byte[] o = (byte[])objectiveModel.Clone();
                BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(0), id);        // list key
                BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(4), id);        // id
                BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(8), text);
                BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(12), 0);        // second text (hint) unused
                BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(state), 0);     // patched per character when sent
                BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(state + 4), 0);
                BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(state + 8), required);
                objectives.Write(o);
            }

            // Tail: 3 × u32, points, subcategory, i8, u32, bool, 10 × u32 (h3), 3 × bool, lists, map, u32.
            int tailStart = model.Objectives[^1].StateOffset + ObjectiveTailBytes;
            byte[] tail = template[tailStart..model.RecordEnd];
            int points = model.PointsOffset - tailStart;
            BinaryPrimitives.WriteUInt32LittleEndian(tail.AsSpan(points), 5);                 // Luminary Score [W]
            BinaryPrimitives.WriteUInt32LittleEndian(tail.AsSpan(points + 38), goal.After);   // h3[6] predecessor
            BinaryPrimitives.WriteUInt32LittleEndian(tail.AsSpan(points + 46), goal.Icon);    // h3[8] icon
            BinaryPrimitives.WriteUInt32LittleEndian(tail.AsSpan(points + 50), goal.Sort);    // h3[9] sort index

            var record = new MemoryStream();
            record.Write(head);
            record.Write(rewards);
            record.Write(objectives.ToArray());
            record.Write(tail);
            return record.ToArray();
        }

        /// <summary>
        /// The reward bundle (layout on <see cref="TierRewards"/>): the model's header, the design items first and the 30
        /// Lumens last (as 471's bundle orders them), bundle id 1051 (every captured Getting Started goal's).
        /// </summary>
        private static byte[] TutorialRewards(byte[] model, TutorialGoal goal)
        {
            int mapCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(model.AsSpan(5));
            int headerEnd = 1 + 4 + 4 + mapCount * 8 + 12 + 8 + 20 + 16;
            var entries = new List<(uint Icon, uint Name, uint Count, uint Item)>();
            foreach (uint item in goal.Items)
            {
                var d = Inventory.ItemDefinitionManager.Get(item);
                if (d == null)
                {
                    log.Warn($"Tutorial goal {goal.Id}: reward item {item} has no definition; left out.");
                    continue;
                }
                entries.Add((d.ImageSetId, d.NameId, 1, item));
            }
            entries.Add((LumenIcon, LumenName, 30, Lumen));
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
            w.Write(1051u);
            w.Flush();
            return output.ToArray();
        }

        /// <summary>
        /// A step goal: activates its first objective that isn't done, unless one is already active (call under
        /// <see cref="sync"/>). Sends ObjectiveActivated when <paramref name="notify"/> is set; at zone-in the record carries
        /// the state instead.
        /// </summary>
        private static void ActivateNextStep(ulong character, Achievement a, List<CharacterObjectiveModel> saves, WorldSession notify)
        {
            foreach (Objective o in a.Objectives)
            {
                byte state = StateOf(character, o, a).State;
                if (state == 2)
                    continue;
                if (state == 1)
                    return;
                Set(character, a, o, 1, 0, saves);
                notify?.EnqueueMessage(ZoneMessageOpcode.AchievementBase, Build(ActivatedSub, w =>
                {
                    w.Write(a.Id); w.Write(o.Id); w.Write(0u); w.Write(Math.Max(o.Required, 1)); w.Write(0u); w.Write(false);
                }));
                return;
            }
        }

        /// <summary>
        /// Zone-in (under <see cref="sync"/>): a step goal the character has (its predecessor complete) with no active step —
        /// e.g. 685 was completed before these goals existed — gets its next step active.
        /// </summary>
        private static void CatchUpSteps(ulong character, List<CharacterObjectiveModel> saves)
        {
            foreach (TutorialGoal goal in TutorialGoals)
                if (achievements.TryGetValue(goal.Id, out Achievement a) && IsAvailable(character, a) && !IsComplete(character, a))
                    ActivateNextStep(character, a, saves, null);
        }

        private static string TextOf(uint id) => LandmarkEmulator.Shared.GameTable.Text.TextManager.Instance.GetTextForId(id)?.Trim() ?? "";

        // ---- Hooks (each names what the server sees and its evidence) -------------------------------------------------

        /// <summary>
        /// UI events (UiBase 0x17, Ui.UIEventTriggered from the Flash UI, re/out/ui_events.txt) [P]: 12 Build Mode's Blocks
        /// tab (BuildModeWidget.switchView BUILD_VIEW; Designs 9, Tint 10, Props 11 are the other tabs, Props being
        /// [Shift-2] in 1305's text); 17 / 18 / 19 the Fine Tuning window (TweakMode.swf) confirmed with Paste Air
        /// (Voxel.SetShouldPasteAir 2) / Interior Air (3) / Remove Voxels selected — the client fires these only there,
        /// and they match the three Window steps, so live most likely counted those steps by them [I]. The event does not
        /// name the design; the paste itself (<see cref="Pasted"/>) counts too when it is the Window.
        /// </summary>
        private static void TutorialUiEvent(WorldSession session, uint eventId)
        {
            switch (eventId)
            {
                case 12: Progress(session, OpenBlocksTab); break;
                case 17: Progress(session, WindowSurroundingAir); break;
                case 18: Progress(session, WindowInteriorAir); break;
                case 19: Progress(session, WindowAsDelete); break;
            }
        }

        /// <summary>
        /// ToolPacketActivate (ToolBase 0x117 sub 1, Building.Tools.cs) [P]: ToolTypes 10 StampBrush = the Add Tool [1],
        /// 6 SubtractiveBrush = the Delete (Remove) Tool [2], 2 Selection = the Selection Tool [3]. "…to select some blocks
        /// on your site" is not checked: selecting sends nothing until a copy [P], so the switch counts [I].
        /// </summary>
        public static void ToolActivated(WorldSession session, uint tool)
        {
            switch (tool)
            {
                case 10: Progress(session, UseAddTool); break;
                case 6: Progress(session, UseDeleteTool); break;
                case 2: Progress(session, UseSelectionTool); break;
            }
        }

        /// <summary>
        /// An applied build edit (ApplyDiffRequest, Building.Handle). Operation kind 6 is set only by the Paint cursor's
        /// apply (FUN_1436d5480, next to VoxelPaintCursor's vtable methods → FUN_1430b9560 sets request +0xe0 = 6), which
        /// paints the cursor's selection volume [P kind, I class]; the Paint tool being the active tool counts too.
        /// </summary>
        public static void BuildEdit(WorldSession session, uint kind)
        {
            if (kind == 6 || Building.ActiveTool(session) == Building.ToolType.PaintBrush)
                Progress(session, PaintSelection);
        }

        /// <summary>A successful undo / redo (VoxelConstruction sub 3 {u32 0 undo / 1 redo}, Building.UndoRedo) [P].</summary>
        public static void UndoRedone(WorldSession session, bool redo) => Progress(session, redo ? RedoBuild : UndoBuild);

        /// <summary>A copy with something in it (ClientSelectionAreaRequest action 0, BuildingClipboard.Copy) [P].</summary>
        public static void Copied(WorldSession session) => Progress(session, CopyBlocks);

        /// <summary>
        /// A successful on-grid paste (ClientSelectionAreaRequest action 4, BuildingClipboard.Paste) [P]: the clipboard
        /// (no design) for "Paste [Ctrl-V] the blocks you copied"; a design by its shown name — Modular Castle Long Room
        /// (574304), Modular Castle Entry (574310), and Modular Castle Window (574311) with the request's paste mode (u32
        /// after the block list, cursor +0x2c8: 2 Paste Air = with surrounding empty space, 3 Paste Interior Air) or its
        /// "Remove voxels" flag (u8 after it, cursor +0x2c4 = Paste as Delete; writer FUN_1430b96a0, request constructor
        /// FUN_1430b3990 +0x148 / +0x14c; modes from the Fine Tuning menu FUN_1436b14b0, trace/0695/ruin-stamping.md §1.2).
        /// Off-grid pastes (0xF4 sub 5 OffGridPasteClientRequest, same paste mode / Remove-voxels arguments from the cursor)
        /// report here too (Building.OffGridPaste).
        /// </summary>
        public static void Pasted(WorldSession session, string designName, uint mode, bool asDelete)
        {
            if (string.IsNullOrEmpty(designName))
            {
                Progress(session, PasteBlocks);
                return;
            }
            if (designName.Equals(TextOf(574304), StringComparison.OrdinalIgnoreCase))
                Progress(session, PlaceLongRoom);
            else if (designName.Equals(TextOf(574310), StringComparison.OrdinalIgnoreCase))
                Progress(session, PasteEntry);
            else if (designName.Equals(TextOf(574311), StringComparison.OrdinalIgnoreCase))
            {
                if (asDelete)
                    Progress(session, WindowAsDelete);
                else if (mode == 2)
                    Progress(session, WindowSurroundingAir);
                else if (mode == 3)
                    Progress(session, WindowInteriorAir);
            }
        }

        /// <summary>A prop placed: a Replicator (crafting station 53, its props found by name, CraftingStations) [P].</summary>
        private static void TutorialPropPlaced(WorldSession session, uint propId)
        {
            if (CraftingStations.StationForProp(propId) == 53)
                Progress(session, PlaceReplicator);
        }

        /// <summary>
        /// A craft finished (Crafting.Update) [P], <paramref name="crafts"/> times: Cut Plain Wood, Farseer Pick, Smelted
        /// Copper, Cut Agate by item name. The counts are crafts, not items: one Smelted Copper craft makes 150 (Recipes 38),
        /// so "Craft Smelted Copper (3)" can only mean three crafts [I].
        /// </summary>
        private static void TutorialCrafted(WorldSession session, uint item, uint crafts)
        {
            switch (ItemName(item).Trim())
            {
                case "Cut Plain Wood": Progress(session, CraftCutPlainWood, crafts); break;
                case "Farseer Pick": Progress(session, CraftFarseerPick, crafts); break;
                case "Smelted Copper": Progress(session, CraftSmeltedCopper, crafts); break;
                case "Cut Agate": Progress(session, CraftCutAgate, crafts); break;
            }
        }
    }
}
