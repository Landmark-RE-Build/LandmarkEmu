using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.GameTable;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Story tools: Links between placed props ("When this torch is lit..." → "...Open this door."). The client's
    /// link editor (ControllerGameScripting, opened with Prop.SetupScript from a prop's build-mode menu) asks the server
    /// what a prop supports and stores the Links through ObjectScripting packets (base 0x111, u8 sub; client handler
    /// FUN_142ea87d0, request builders FUN_142ea9a00..FUN_142ea9f00):
    ///
    /// | C→S | | S→C reply |
    /// |---|---|---|
    /// | 0x0C SupportedObjects (no body) | | 1 {u32, u64 set (u32 n, n × u64)}: the props that can be linked (highlighted while picking a target) |
    /// | 0x0D SupportedEvents {u64 prop} | | 2 {u64 prop, u32 result, events} |
    /// | 0x0E SupportedActions {u64 prop, u32} | | 3 {u64 prop, u32 result, action groups} |
    /// | 0x0F SupportedActionsEventsAndDefaults {u64 prop} | | 4 {u64 prop, u32 result, action groups, events, default links {u32, u32}} |
    /// | 0x10 AddBinding {u64 source, u64 target, u32 event, u32 action} | | 5 {u64 source, u64 target, u32 event, u32 action, u32 result} |
    /// | 0x11 ListBindings {u64 prop} | | 6 {u64 prop, u32 result, links {u64 id, u64 source, u32 event, u64 target, u32 action, u32, u32, u32 list, u32 list}} |
    /// | 0x12 RemoveBinding {u64 link} | | 7 {u64 link, u32 result} |
    /// | 0x13 RemoveAllBindings {u64 prop} | | 8 {u64 prop, u32 result} |
    /// | 0x14 RestoreDefaults {u64 prop} | | 9 {u64 prop, u32 result} |
    /// | 0x15 PotentialLink {u64 prop, u32 flag, u32} | | 10 {u64 prop, u32 result} |
    /// | 0x16 RemoveAddBinding {u64 link, u64 source, u64 target, u32 event, u32 action} | | 11 {u64 link, u64 source, u64 target, u32, u32, u32 result} |
    ///
    /// Events and action groups are {u32 id, u32 text id, u32 sort order} (the editor sorts by the third and shows the
    /// text). The live server's definitions aren't in the client; the vocabulary is: the client still has every link
    /// string (re/out/link_texts.tsv), so each id here is the text id of its sentence, assigned to props by kind (prop
    /// name: torch, door, switch..., and the story-tool props by id). Result 0 = success.
    ///
    /// Links run while the build site is in Play Mode (ClaimsBase 0x43 SetAdventureStatus {u64 claim, u32 mode 2,
    /// u8}; see <see cref="Run"/>).
    /// </summary>
    public static class StoryTools
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        private static readonly object sync = new();

        private const byte SubObjects = 1, SubEvents = 2, SubActions = 3, SubActionsEventsAndDefaults = 4, SubAddBinding = 5,
            SubListBindings = 6, SubRemoveBinding = 7, SubRemoveAllBindings = 8, SubRestoreDefaults = 9, SubPotentialLink = 10,
            SubRemoveAddBinding = 11;
        /// <summary>
        /// AddBinding results the client shows (0.695 FUN_142f46ed0 cases 1-5; FUN_142fee7e0: 4 → text 577233): 0 ok,
        /// 1 failed, 2 invalid cycle (unused: live's cycle rule is unknown; the run depth cap stays), 3 the same Link
        /// already exists, 4 the event allows a single Link and has one.
        /// </summary>
        private const uint ResultOk = 0, ResultFailed = 1, ResultDuplicate = 3, ResultSingleBinding = 4;

        /// <summary>
        /// RemoveAddBinding (sub 0x16 → reply 11) results. The reply's own result table (0.695 FUN_142f47140; 0.589
        /// FUN_142ea8550 had 0-4) is 0 ok, 1 permission, 2 invalid cycle, 3 duplicate, 4
        /// GameScripting.RemoveAddBindingResultMessage.RemoveFailure, 5 (new in 0.695)
        /// AdvancedScripting.UniqueBinding.BindSingle [P]. The 0.695 reply handler FUN_142fef9b0 itself only tests
        /// result == 0 ("Link successfully modified." and a refresh, else "Link modification failure." and nothing
        /// else) [P], so a refused edit must leave the old Link in place: the client keeps showing it.
        /// </summary>
        public const uint RemoveAddRemoveFailed = 4, RemoveAddSingleBinding = 5;

        /// <summary>
        /// Replaces a Link as one step (RemoveAddBinding): <paramref name="old"/> leaves <paramref name="links"/>, the
        /// new Link is tried (<paramref name="add"/> returns an AddBinding result); only when it is stored is the old
        /// one forgotten for good (<paramref name="forget"/>, the database row), otherwise it goes back. Returns the
        /// RemoveAddBinding result: AddBinding's single-binding code 4 is 5 in this reply's table.
        /// </summary>
        public static uint ReplaceLink<T>(List<T> links, object gate, T old, Func<uint> add, Action<T> forget)
        {
            lock (gate)
                links.Remove(old);
            uint result = add();
            if (result == ResultOk)
                forget(old);
            else
                lock (gate)
                    links.Add(old);
            return result == ResultSingleBinding ? RemoveAddSingleBinding : result;
        }

        /// <summary>Events that allow one Link only (Combat Node "choose", IF Gate condition; trace/0695/plan-storytools-next.md Spec 4).</summary>
        private static readonly HashSet<uint> singleBindingEvents = new() { 577162, 577629 };

        #region kinds

        /// <summary>What a kind of prop can do in Links: its events and actions (text ids), and how it behaves.</summary>
        public class Kind
        {
            public string Name;
            public uint[] Events = Array.Empty<uint>();
            public uint[] Actions = Array.Empty<uint>();
            /// <summary>On/off props (lights, doors, switches...): events {on, off, on or off}, actions {on, off, toggle}.</summary>
            public bool Toggle;
            /// <summary>Lockable (doors, gates, portcullises, chests): events {locked, unlocked}, actions {lock, unlock}.</summary>
            public uint[] LockEvents = Array.Empty<uint>(), LockActions = Array.Empty<uint>();
        }

        private static Kind ToggleKind(string name, uint on, uint off, uint either, uint doOn, uint doOff, uint doToggle,
            uint[] extraEvents = null, uint[] lockEvents = null, uint[] lockActions = null)
            => new()
            {
                Name = name,
                Toggle = true,
                Events = new[] { on, off, either }.Concat(extraEvents ?? Array.Empty<uint>()).Concat(lockEvents ?? Array.Empty<uint>()).ToArray(),
                Actions = new[] { doOn, doOff, doToggle }.Concat(lockActions ?? Array.Empty<uint>()).ToArray(),
                LockEvents = lockEvents ?? Array.Empty<uint>(),
                LockActions = lockActions ?? Array.Empty<uint>()
            };

        /// <summary>Kinds found by a word in the prop's name (first match wins, so "Candles" before "Candle").</summary>
        private static readonly (string Word, Kind Kind)[] namedKinds =
        {
            ("Candelabra", ToggleKind("candelabra", 576399, 576400, 576401, 576402, 576403, 576674)),
            ("Chandelier", ToggleKind("chandelier", 576342, 576343, 576344, 576345, 576346, 576667)),
            ("Candles", ToggleKind("candles", 576364, 576365, 576366, 576367, 576368, 576670)),
            ("Candle", ToggleKind("candle", 576377, 576378, 576379, 576380, 576381, 576671)),
            ("Torch", ToggleKind("torch", 576359, 576360, 576361, 576362, 576363, 576669)),
            ("Lantern", ToggleKind("lantern", 576352, 576353, 576354, 576355, 576356, 576609)),
            ("Lamp", ToggleKind("lamp", 576347, 576348, 576349, 576350, 576351, 576668)),
            ("Brazier", ToggleKind("brazier", 576369, 576370, 576371, 576372, 576373, 50098)),
            ("Campfire", ToggleKind("fire", 576339, 576340, 576341, 51796, 51872, 576665)),
            ("Fire", ToggleKind("fire", 576339, 576340, 576341, 51796, 51872, 576665)),
            ("Fan", ToggleKind("fan", 576394, 576395, 576396, 576397, 576398, 576673)),
            ("Emitter", ToggleKind("emitter", 576440, 576441, 576442, 576445, 576444, 576393)),
            ("Glow", ToggleKind("glow", 576449, 576450, 576451, 571529, 571531, 576685)),
            ("Gramophone", ToggleKind("gramophone", 576469, 576470, 576471, 576472, 576473, 576686)),
            ("Flinger", ToggleKind("flinger", 568671, 568672, 568674, 2043, 2044, 576679)),
            ("Platform", ToggleKind("platform", 576406, 576407, 576408, 576409, 576410, 576680)),
            ("Launcher", ToggleKind("launcher", 576416, 576417, 576418, 576419, 576420, 576682)),
            ("Pad", ToggleKind("pad", 576411, 576412, 576413, 576415, 576414, 576681)),
            ("Switch", ToggleKind("switch", 576427, 576426, 576425, 576309, 576310, 576683, extraEvents: new uint[] { ClickedEvent })),
            ("Lever", ToggleKind("switch", 576427, 576426, 576425, 576309, 576310, 576683, extraEvents: new uint[] { ClickedEvent })),
            ("Shutter", ToggleKind("shutter", 576429, 576430, 576431, 576432, 576433, 576684)),
            ("Portcullis", ToggleKind("portcullis", 576382, 576383, 576384, 576385, 576386, 576387,
                lockEvents: new uint[] { 577131, 577132 }, lockActions: new uint[] { 577143, 577144 })),
            ("Gate", ToggleKind("gate", 577138, 577139, 577140, 576676, 576677, 576675,
                lockEvents: new uint[] { 577141, 577142 }, lockActions: new uint[] { 577135, 577136 })),
            ("Door", ToggleKind("door", 566347, 566348, 566349, 57474, 62049, 560098,
                lockEvents: new uint[] { 577129, 577130 }, lockActions: new uint[] { 577133, 577134 })),
            ("Chest", new Kind
            {
                Name = "chest", Events = new uint[] { 576464, 573144, 573145 }, Actions = new uint[] { 576358, 573142, 573143 },
                LockEvents = new uint[] { 573144, 573145 }, LockActions = new uint[] { 573142, 573143 }
            }),
            ("Light", ToggleKind("light", 576388, 576389, 576390, 576391, 576392, 576672)),
            // 26 Apr 2016, P4856: every interactive prop, emitters and lights included, works with linking and
            // triggering; P5371 the Buccaneer wall sconce switches on and off; P5227 the emissive cube's
            // "...turn this glow off."; P5222 dev the Round Control Panel ought to be linkable (TASKS 6.36). Sconces
            // are
            // lights (691, 692, 2096, 2204, 2581), Emissive Orbs / Cubes glows (2127-2142), Control Panels switches
            // (1172, 1176, 1177; the switch sentences, inferred).
            ("Sconce", ToggleKind("light", 576388, 576389, 576390, 576391, 576392, 576672)),
            ("Emissive", ToggleKind("glow", 576449, 576450, 576451, 571529, 571531, 576685)),
            ("Control Panel", ToggleKind("switch", 576427, 576426, 576425, 576309, 576310, 576683, extraEvents: new uint[] { ClickedEvent })),
        };

        /// <summary>"When a Luminary clicks this object..."</summary>
        public const uint ClickedEvent = 576311;

        // Story-tool props (PropDefs ids; the items 3090 Logic Filter, 3127 Travel Point, 3131 Story Node, 3132 Dice
        // Roll Filter, 3137 Timer Filter, 3790 Starting Location, 4388 Damage, 4389 Focus, 4390 Reset place them).
        /// <summary>
        /// Props with text (PropBase 0x13 / 0x14) that Links can show: Rustic Sign, Fjorden Book (open / closed), Gold
        /// Trimmed Scroll — "...Have this sign / book / scroll display the text." (texts 577157 / 577149 / 577150).
        /// </summary>
        public const uint RusticSignProp = 2939, FjordenBookOpenProp = 2228, FjordenBookClosedProp = 2229, GoldScrollProp = 2639;
        public const uint SignDisplaysText = 577157, BookDisplaysText = 577149, ScrollDisplaysText = 577150;

        public const uint LogicFilterProp = 2325, TravelPointProp = 2330, StoryNodeProp = 2344, DiceRollProp = 2345,
            TimerProp = 2367, StartingLocationProp = 2655, FocusNodeProp = 2898, DamageNodeProp = 2899, ResetNodeProp = 2900,
            LinkingAreaProp = 1590;

        /// <summary>0.695 story props: Counting Node, Scoreboard 3000, Random Link Filter, Server Time Node, Respawn Point,
        /// Combat Node, IF Gate.</summary>
        public const uint CountingNodeProp = 2901, ScoreboardProp = 3000, RandomLinkFilterProp = 2657, ServerTimeNodeProp = 2659,
            RespawnPointProp = 2701, CombatNodeProp = 2940, IfGateProp = 2976;

        /// <summary>Hour Node 2945 ("scripting: hour node", text 577253): the Server Time Node's kind, not placeable from a
        /// palette (trace/0695/plan-storytools-next.md Spec 3).</summary>
        public const uint HourNodeProp = 2945;

        /// <summary>
        /// Server Time Node 2659 / Hour Node 2945: "When game time in slot 1 is reached..." (577252) and slots 2-6
        /// (577255-577259); slot N is Events[N - 1]. No actions. A node offers only its first Amount of Listeners events
        /// (<see cref="EventsOf"/>).
        /// </summary>
        private static readonly Kind serverTimeKind = new()
        {
            Name = "server time node", Events = new uint[] { 577252, 577255, 577256, 577257, 577258, 577259 }
        };

        // Respawn Point 2701 (texts: link sentences say "Respawn Location" 577527; storytools-695.md §4.8).
        /// <summary>"When this Respawn Location activates / deactivates..." and "...Activate / Deactivate this Respawn Location."</summary>
        public const uint RespawnPointActivates = 577528, RespawnPointDeactivates = 577529, ActivateRespawnPoint = 577530,
            DeactivateRespawnPoint = 577531;

        /// <summary>Counting Node / Scoreboard: count reaches max / 0, increases / decreases; +1, −1, set max, set 0.</summary>
        private static Kind CountingKind(string name) => new()
        {
            Name = name, Events = new uint[] { 557290, 565055, 577247, 577092 }, Actions = new uint[] { 577248, 577249, 577250, 577251 }
        };

        // Logic Filter cogs.
        public const uint Cog1Starts = 572953, Cog1Stops = 572954, Cog2Starts = 572956, Cog2Stops = 572955, BothStarted = 572957,
            BothStopped = 572958, EitherStarts = 572959, EitherStops = 572960;
        public const uint StartCog1 = 572961, StopCog1 = 572962, StartCog2 = 572963, StopCog2 = 572964, StartBoth = 576091,
            StopBoth = 576090, ToggleCog1 = 576092, ToggleCog2 = 576093, ToggleBoth = 576094;
        // Dice Roll Filter.
        public const uint DiceWins = 573027, DiceLoses = 573037, RollDice = 573036;
        // Timer Filter.
        public const uint TimerStarts = 573099, TimerPauses = 573100, TimerResets = 573101, TimerCompletes = 573102;
        public const uint StartTimer = 573096, PauseTimer = 573097, ResetTimer = 573098, PauseAndResetTimer = 576503;
        // Story Node.
        public const uint NodeEverythingActivates = 577047, NodeMoodActivates = 576251, NodeStanceActivates = 576253,
            NodeEmoteActivates = 576263, NodeTextActivates = 576254;
        /// <summary>
        /// "When the text on this node expires..." (0.695, 577246): its Text Duration (row 37, 1-20 s, default 5) after a
        /// creature said the node's text (P5964 / P6191; the moment is inferred, plan-storytools-next.md Spec 15).
        /// </summary>
        public const uint NodeTextExpires = 577246;
        /// <summary>"When a Luminary accepts..." / "When a Luminary denies..." (0.695, question Story Nodes, P6539).</summary>
        public const uint NodeAccepted = 577724, NodeDenied = 577725;
        /// <summary>
        /// Weapon visuals (0.695; wiki Story Node: "change weapon visuals", the creature's weapons shown drawn or
        /// sheathed): the node's in-action "...Activate the weapon visuals on this node." (577239), its event
        /// "When the weapon visuals on this node activates..." (577238) and the spawner's "...Set this creature's weapon
        /// visuals." (577237), which takes the node's row 41 Weapon Visuals (Equipped 577235 / Sheathed 577236) as the
        /// Story Node's mood reaches its spawner's "Set mood" (storytools-695.md §4.1). On the creature:
        /// <see cref="Creatures.StoryWeapons"/>.
        /// </summary>
        public const uint ActivateWeaponVisuals = 577239, NodeWeaponVisualsActivates = 577238, SetWeaponVisuals = 577237;
        /// <summary>
        /// IF Gate (0.695, 29 Sep 2016; P6494-P6496): "Choose the prop to use in the IF condition..." (577629, one Link) to
        /// the tested prop's "...Link this prop to the IF condition" (577628); "Peform IF check..." (577627) fires "If..."
        /// (577626) or "Else..." (577625).
        /// </summary>
        public const uint IfChooseProp = 577629, IfLinkTarget = 577628, IfCheck = 577627, IfTrue = 577626, IfFalse = 577625;
        public const uint ActivateEverything = 576243, ActivateMood = 576248, ActivateStance = 576252, ActivateEmote = 576262,
            ActivateText = 576255;
        // Focus / Damage / Reset nodes.
        public const uint FocusActivates = 577079, FocusDeactivates = 577169, ActivateFocus = 577080, DeactivateFocus = 577167;
        public const uint DamageActivates = 577070, ActivateDamage = 565554;
        public const uint ResetStory = 577085;
        /// <summary>"The story on this Build Site has been reset!" (L22).</summary>
        private const uint StoryResetText = 577089;
        /// <summary>"The story on this Build Site has been saved!" (L22; <see cref="AnnounceSaved"/>).</summary>
        private const uint StorySavedText = 577105;
        /// <summary>The Adventure Game Table (IgRuleSetPlacementProps 2313 → rule set 8) and its one action: "...Give reward
        /// and teleport to a Chaos Cavern.", which completes the adventure (InstancedGames.CompleteAdventure).</summary>
        public const uint AdventureTableProp = 2313, CompleteAdventure = 577090;
        // Starting Location and Linking Area.
        public const uint LuminaryTeleportsHere = 574556, LuminaryRespawnsHere = 574557;
        public const uint LuminaryEnters = 565950, LuminaryExits = 565951, CreatureEnters = 573163, CreatureLeaves = 573164,
            DamageCreaturesInArea = 577148;
        /// <summary>
        /// 0.695 Linking Area actions "...Damage all Luminaries and creatures in this area." (577221), "...Damage all
        /// Luminaries in this area." (577228), "...parse every entity in this area..." (577723) and its event "For every
        /// object in this area when parsed..." (577722), run once per player / story creature inside with that one as the
        /// triggering character (inferred from the wording; storytools-695.md, plan-storytools-next.md Spec 13).
        /// </summary>
        public const uint DamageAllInArea = 577221, DamageLuminariesInArea = 577228, ParseArea = 577723, EntityParsed = 577722;
        /// <summary>
        /// 0.695 Linking Area actions "...Destroy Voxels in this area." (577548) and "...Heal Voxels in this area." (577549)
        /// (28 Jul 2016: a Link can target a Linking Area to destroy (or heal) inside it, P6185;
        /// trace/0695/storytools-695.md §4.11, TASKS 2.6). Run by <see cref="VoxelDestruction.AreaAction"/>.
        /// </summary>
        public const uint DestroyVoxelsInArea = 577548, HealVoxelsInArea = 577549;
        // Travel Point.
        public const uint CreatureArrives = 572991, WalkHere = 572992;
        // Creature spawners.
        public const uint CreatureSpawns = 572997, CreatureDies = 572824, CreatureEntersCombat = 576447, CreatureExitsCombat = 576448,
            CreatureClicked = 51877, CreatureToldToWalk = 576661;
        public const uint SpawnCreature = 572860, DespawnCreature = 573003, FactionAlly = 576439, FactionNeutral = 576438,
            FactionEnemy = 576434, EquipWeapon = 577074, UnequipWeapon = 577075, WalkRoute = 576662, SetMood = 576259,
            SetStance = 576258, PerformEmote = 576261, SayText = 3, PerformEverything = 577048, FocusOnNode = 577067,
            StopFocusing = 577168, DamageCreature = 577068;

        /// <summary>
        /// Combat Node 2940 (28 Jul 2016, P6150-P6161; storytools-695.md §4.4, TASKS 2.7): "Choose the creature this node will
        /// control..." (577162, out; single binding, AddBinding result 4) → a spawner's "...Link this creature to the combat
        /// node." (577163). In: 577242 "...Set the combat tactics on this node's linked creature.", 577213-577216 / 577243 /
        /// 577244 "...Have this node's linked creature perform Ability 1-6." (offered only once a creature is linked, P6151).
        /// Out: 577245 "When the combat tactics on this node activates..." → a spawner's 577218 "...Set this creature's
        /// combat tactics." (the node's row 31, as the Story Node's mood reaches its spawner); spawner 577219
        /// "...Deactivate this creature's combat AI" = Do Nothing.
        /// </summary>
        public const uint CombatChooseCreature = 577162, CombatLinkTarget = 577163, CombatSetTactics = 577242,
            CombatTacticsActivates = 577245, SpawnerSetTactics = 577218, SpawnerDeactivateAi = 577219;

        /// <summary>The six "perform Ability N" actions, N = index + 1.</summary>
        public static readonly uint[] CombatAbilityActions = { 577213, 577214, 577215, 577216, 577243, 577244 };

        /// <summary>
        /// Reward Chests 3003-3008 (0.695; TASKS 3.11, spec-reward-chests.md §5.5): "When this chest is looted..." (577597)
        /// and "...Loot this chest" (577598) [P: storytools-695 §4.15], plus the storage chests' Lock / Unlock sentences
        /// (events 573144 / 573145, actions 573142 / 573143) [I, strong: the server text "PlayerLoot.ChestLocked" 577651
        /// and players' locked reward chests driven by Links, forum 57918 #7]. Listed by id: with 0.695 names, "... Reward
        /// Chest" would otherwise match the storage chests' "Chest" kind (Open / Lock) and never offer the loot sentences.
        /// </summary>
        private static readonly Kind rewardChestKind = new()
        {
            Name = "reward chest",
            Events = new uint[] { RewardChests.LootedEvent, 573144, 573145 },
            Actions = new uint[] { RewardChests.LootAction, 573142, 573143 },
            LockEvents = new uint[] { 573144, 573145 },
            LockActions = new uint[] { 573142, 573143 }
        };

        private static readonly Dictionary<uint, Kind> kindsById = new()
        {
            [RewardChests.CommonProp] = rewardChestKind,
            [RewardChests.StoneProp] = rewardChestKind,
            [RewardChests.MetalProp] = rewardChestKind,
            [RewardChests.GemProp] = rewardChestKind,
            [RewardChests.SuperbProp] = rewardChestKind,
            [RewardChests.LegendaryProp] = rewardChestKind,
            [LogicFilterProp] = new Kind
            {
                Name = "logic filter",
                Events = new[] { Cog1Starts, Cog1Stops, Cog2Starts, Cog2Stops, BothStarted, BothStopped, EitherStarts, EitherStops },
                Actions = new[] { StartCog1, StopCog1, StartCog2, StopCog2, StartBoth, StopBoth, ToggleCog1, ToggleCog2, ToggleBoth }
            },
            [TravelPointProp] = new Kind { Name = "travel point", Events = new[] { CreatureArrives }, Actions = new[] { WalkHere } },
            [StoryNodeProp] = new Kind
            {
                Name = "story node",
                Events = new[] { NodeEverythingActivates, NodeMoodActivates, NodeStanceActivates, NodeEmoteActivates, NodeTextActivates, NodeTextExpires, NodeAccepted, NodeDenied,
                    NodeWeaponVisualsActivates },
                Actions = new[] { ActivateEverything, ActivateMood, ActivateStance, ActivateEmote, ActivateText, ActivateWeaponVisuals }
            },
            [DiceRollProp] = new Kind { Name = "dice roll filter", Events = new[] { DiceWins, DiceLoses }, Actions = new[] { RollDice } },
            [TimerProp] = new Kind
            {
                Name = "timer filter",
                Events = new[] { TimerStarts, TimerPauses, TimerResets, TimerCompletes },
                Actions = new[] { StartTimer, PauseTimer, ResetTimer, PauseAndResetTimer }
            },
            [StartingLocationProp] = new Kind { Name = "starting location", Events = new[] { LuminaryTeleportsHere, LuminaryRespawnsHere } },
            [FocusNodeProp] = new Kind
            {
                Name = "focus node", Events = new[] { FocusActivates, FocusDeactivates }, Actions = new[] { ActivateFocus, DeactivateFocus }
            },
            [DamageNodeProp] = new Kind { Name = "damage node", Events = new[] { DamageActivates }, Actions = new[] { ActivateDamage } },
            [RusticSignProp] = new Kind { Name = "sign", Actions = new[] { SignDisplaysText } },
            // 0.695 story props (TASKS 2.17; texts are the client's, owners from wording, patch notes and help pages:
            // trace/0695/storytools-695.md §4, plan-storytools-next.md Spec 3). Their runtimes come with 2.2 / 2.7 / 2.8 /
            // 2.9 / 2.10 / 2.11; the Combat Node offers its tactics sentences once that runtime exists (P6151).
            [CountingNodeProp] = CountingKind("counting node"),
            [ScoreboardProp] = CountingKind("scoreboard"),
            [RandomLinkFilterProp] = new Kind { Name = "random link filter", Events = new uint[] { 577591 }, Actions = new uint[] { 577593 } },
            [ServerTimeNodeProp] = serverTimeKind,
            [HourNodeProp] = serverTimeKind,
            [RespawnPointProp] = new Kind
            {
                Name = "respawn point", Events = new uint[] { RespawnPointActivates, RespawnPointDeactivates, LuminaryRespawnsHere },
                Actions = new uint[] { ActivateRespawnPoint, DeactivateRespawnPoint }
            },
            [CombatNodeProp] = new Kind
            {
                Name = "combat node", Events = new uint[] { CombatChooseCreature, CombatTacticsActivates },
                Actions = new uint[] { CombatSetTactics }
            },
            // 0.695 site games (2.12, InstancedGames.Rework): the Score Node, the Victory Node and Control Point 1224 (also
            // an IF Gate's tested prop, "Owning Team" row 110). Their actions run in InstancedGames.LinkAction; their events
            // come back through InstancedGames.StoryEvent.
            [InstancedGames.ScoreNodeProp] = new Kind
            {
                Name = "score node", Events = new uint[] { InstancedGames.PlayerScoresEvent, InstancedGames.TeamScoresEvent },
                Actions = new uint[] { InstancedGames.ScorePlayerAction, 577684, 577685, 577686, 577687 }
            },
            [InstancedGames.VictoryNodeProp] = new Kind
            {
                Name = "victory node", Actions = new uint[] { 577688, 577689, 577690, 577691, InstancedGames.VictoryTriggeringTeamAction }
            },
            [1224] = new Kind
            {
                Name = "control point", Events = new uint[] { InstancedGames.ControlPointCapturedEvent },
                Actions = new uint[] { InstancedGames.AttemptControlAction }
            },
            [IfGateProp] = new Kind { Name = "IF gate", Events = new uint[] { IfChooseProp, IfTrue, IfFalse }, Actions = new uint[] { IfCheck } },
            [FjordenBookOpenProp] = new Kind { Name = "book", Actions = new[] { BookDisplaysText } },
            [FjordenBookClosedProp] = new Kind { Name = "book", Actions = new[] { BookDisplaysText } },
            [GoldScrollProp] = new Kind { Name = "scroll", Actions = new[] { ScrollDisplaysText } },
            [ResetNodeProp] = new Kind { Name = "reset node", Actions = new[] { ResetStory } },
            // The Game Table's events "On Game Started" / "On Game Ended" (InstancedGames.GameStartedEvent / GameEndedEvent),
            // raised by the site game through InstancedGames.StoryEvent.
            [AdventureTableProp] = new Kind
            {
                Name = "adventure game", Events = new[] { InstancedGames.GameStartedEvent, InstancedGames.GameEndedEvent },
                Actions = new[] { CompleteAdventure }
            },
            [LinkingAreaProp] = new Kind
            {
                Name = "linking area",
                Events = new[] { LuminaryEnters, LuminaryExits, CreatureEnters, CreatureLeaves, EntityParsed },
                Actions = new[] { DamageCreaturesInArea, DamageAllInArea, DamageLuminariesInArea, ParseArea, DestroyVoxelsInArea, HealVoxelsInArea }
            },
        };

        /// <summary>
        /// Link action of the Fireworks Emitters (props 2949-2970, 0.695; text 577506 "...Launch this firework.", their only
        /// Link sentence): each run launches the burst once (<see cref="PropEffects.Launch"/>). Listed by id so the name
        /// match doesn't take "Fireworks Emitter" for a fire or a toggled emitter.
        /// </summary>
        public const uint LaunchFirework = 577506;

        private static readonly Kind fireworksKind = new() { Name = "fireworks emitter", Actions = new[] { LaunchFirework } };

        /// <summary>Viewers of a firework: players within this distance (the bursts climb 50-80 m and are seen from afar).</summary>
        private const float FireworksRange = 300f;

        /// <summary>Ours: the shortest time between two launches of one emitter, so a looping chain can't flood clients.</summary>
        private static readonly TimeSpan FireworksInterval = TimeSpan.FromSeconds(1);

        private static readonly Dictionary<ulong, DateTime> lastLaunch = new();

        private static readonly Kind spawnerKind = new()
        {
            Name = "creature spawner",
            Events = new[] { CreatureSpawns, CreatureDies, CreatureEntersCombat, CreatureExitsCombat, CreatureClicked, CreatureToldToWalk },
            Actions = new[] { SpawnCreature, DespawnCreature, FactionAlly, FactionNeutral, FactionEnemy, EquipWeapon, UnequipWeapon,
                WalkRoute, SetMood, SetStance, PerformEmote, SayText, PerformEverything, FocusOnNode, StopFocusing, DamageCreature,
                CombatLinkTarget, SpawnerSetTactics, SpawnerDeactivateAi, SetWeaponVisuals }
        };

        private static readonly Dictionary<uint, Kind> kindCache = new();

        /// <summary>The Link kind of a prop definition, or null when it has no events or actions.</summary>
        public static Kind KindOf(uint propId)
        {
            lock (kindCache)
            {
                if (kindCache.TryGetValue(propId, out Kind cached))
                    return cached;
                Kind kind = kindsById.GetValueOrDefault(propId);
                if (kind == null && propId is >= PropEffects.FirstFireworksEmitter and <= PropEffects.LastFireworksEmitter)
                    kind = fireworksKind;
                if (kind == null && IsCreatureProp(propId))
                    kind = spawnerKind;
                if (kind == null)
                {
                    uint nameId = GameTableManager.Instance.PropDefs.GetEntry(propId)?.NameStringId ?? 0;
                    string name = nameId != 0 ? Shared.GameTable.Text.TextManager.Instance.GetTextForId(nameId) ?? "" : "";
                    if (name.StartsWith("Spawner:", StringComparison.OrdinalIgnoreCase) || name.Contains("[Spawner]")
                        || name.Contains("NPCSpawner"))
                        kind = spawnerKind;
                    else
                        kind = namedKinds.FirstOrDefault(k => name.Contains(k.Word, StringComparison.OrdinalIgnoreCase)).Kind;
                }
                kindCache[propId] = kind;
                return kind;
            }
        }

        /// <summary>PropCategory 98 "Creatures" (text 573378): the Prop Palette's creature tab.</summary>
        private const uint CreaturesCategory = 98;

        private static HashSet<uint> creatureProps;

        /// <summary>
        /// Is the prop a creature prop — one the palette files under 98 "Creatures" or one of its sub-categories
        /// (PropCategoryGroup 98 → 99 Monsters, 100 Barren, 101 Scalewind, 102 Highfall, 103 NovaTech, 104 Stoneskin, 106
        /// Ebonguard, 121 "Combat Hardware"; PropCategoryPropMap)? Their names carry no "Spawner:" (e.g. 2451 "Barren
        /// Flameblade", 2890 "Target Dummy", 1220 "Proximity Mine"; the prop dump describes them as "landmark_v2:
        /// spawner: ..."), yet they are the build-site creatures: "Proximity Mines are treated as creatures on your build
        /// site" (P5269), the Target Dummy is an "NPC" (P5275), and wiki "Creature Prop" gives every one the creature
        /// settings (Name, Reaction, Combat Tactics, respawn) and Links. 25 May 2016 (P5522): mines and dummies sit in a
        /// sub-category of "Creatures" — 121 "Combat Hardware" (2890, 1220, 2941 Highfall Catapult).
        /// </summary>
        private static bool IsCreatureProp(uint propId)
        {
            if (creatureProps == null)
            {
                var categories = new HashSet<uint> { CreaturesCategory };
                foreach (string[] r in GameDataFiles.Rows("PropCategoryGroup.txt"))
                    if (r.U(0) == CreaturesCategory)
                        categories.Add(r.U(1));
                creatureProps = GameDataFiles.Rows("PropCategoryPropMap.txt").Where(r => categories.Contains(r.U(1)))
                    .Select(r => r.U(2)).Where(p => p != 0).ToHashSet();
            }
            return creatureProps.Contains(propId);
        }

        private static Kind KindOfProp(ulong guid) => Props.Find(guid) is Props.PlacedProp prop ? KindOf(prop.PropId) : null;

        /// <summary>
        /// Every other placed prop: only "When a Luminary clicks this object..." (576311), which the 29 Aug 2016 update
        /// gave every prop (P6432; storytools-695.md §4.15). No actions, so it can't be a Link's target.
        /// </summary>
        private static readonly Kind clickOnlyKind = new() { Name = "prop", Events = new[] { ClickedEvent } };

        /// <summary>A placed prop's kind as a Link source: its story kind, else <see cref="clickOnlyKind"/>.</summary>
        private static Kind SourceKindOf(ulong guid)
            => Props.Find(guid) is Props.PlacedProp prop ? KindOf(prop.PropId) ?? clickOnlyKind : null;

        /// <summary>Whether a placed prop has a "When a Luminary clicks this object..." Link of its own.</summary>
        private static bool HasClickLink(ulong guid)
        {
            EnsureLoaded();
            lock (sync)
                return bindings.Any(b => b.SourceId == guid && b.EventId == ClickedEvent);
        }

        /// <summary>
        /// The events one placed prop offers (null when it has no kind). As its kind's, except a Server Time Node: only its
        /// first Amount of Listeners slots (row 101; storytools-695.md §4.7, wiki: "offered per listener"), so the link
        /// editor (0x0D / 0x0F) lists, and Add accepts, only the slots it has.
        /// </summary>
        /// <summary>
        /// The actions one placed prop offers: its kind's, plus "...Link this prop to the IF condition" (577628) on the
        /// props an IF Gate can test (the IF-capable rows' owners: counting nodes / scoreboards, Logic Filters, Respawn
        /// Points, creature spawners, control points; and on / off props, P6494 IF tests on doors and lights).
        /// </summary>
        private static uint[] ActionsOf(ulong guid)
        {
            Kind kind = KindOfProp(guid);
            if (kind == null)
                return null;
            uint[] actions = IfCapable(kind) && !kind.Actions.Contains(IfLinkTarget) ? kind.Actions.Append(IfLinkTarget).ToArray() : kind.Actions;
            // A Combat Node offers "perform Ability N" only once a creature is linked, one per ability it has (P6151). The
            // count comes from the linked spawner's creature kind, so the Links can be made in Edit Mode, before anything has
            // spawned, and agree with the node's "Ability N" settings rows (PropSettings, L19).
            if (kind.Name == "combat node" && CombatSpawnerOf(guid) is Props.PlacedProp spawner)
                actions = actions.Concat(CombatAbilityActions.Take(Creatures.StoryAbilityCount(spawner.PropId, SpawnerName(spawner.PropId))))
                    .ToArray();
            return actions;
        }

        /// <summary>A prop definition's name (PropDefs NAME_ID text; "" when none): a spawner's creature kind is found from it.</summary>
        private static string SpawnerName(uint propId)
        {
            uint nameId = GameTableManager.Instance.PropDefs.GetEntry(propId)?.NameStringId ?? 0;
            return nameId != 0 ? Shared.GameTable.Text.TextManager.Instance.GetTextForId(nameId) ?? "" : "";
        }

        /// <summary>
        /// The creature spawner a Combat Node is linked to: the target of the node's one "Choose the creature this node will
        /// control..." Link (577162; single binding), in Edit Mode as in Play Mode. Null when it has none (or the target is
        /// gone / not a spawner).
        /// </summary>
        private static Props.PlacedProp CombatSpawnerOf(ulong node)
        {
            foreach (WorldPropBindingModel link in LinksFrom(node, CombatChooseCreature))
                if (Props.Find(link.TargetId) is Props.PlacedProp p && KindOf(p.PropId) == spawnerKind)
                    return p;
            return null;
        }

        /// <summary>
        /// The abilities of a Combat Node's linked creature, as name text ids in "Ability N" order (0: the nameless shield
        /// block), from the linked spawner's creature kind (<see cref="Creatures.StoryAbilityNames"/>; works in Edit Mode).
        /// Empty when the node is linked to no spawner. Read by the node's settings window (PropSettings, L19).
        /// </summary>
        public static uint[] CombatAbilityNames(ulong node)
            => CombatSpawnerOf(node) is Props.PlacedProp spawner
                ? Creatures.StoryAbilityNames(spawner.PropId, SpawnerName(spawner.PropId))
                : Array.Empty<uint>();

        /// <summary>The live creature a Combat Node controls: its spawner through the node's one 577162 Link (null: none).</summary>
        private static ulong? CombatCreatureOf(ulong node)
        {
            Site site = SiteOf(node);
            if (site == null)
                return null;
            WorldPropBindingModel link = LinksFrom(node, CombatChooseCreature).FirstOrDefault(l => site.Props.ContainsKey(l.TargetId));
            return link != null && site.Props[link.TargetId].Creature is ulong c and not 0 ? c : null;
        }

        private static bool IfCapable(Kind kind)
            => kind.Toggle || kind == spawnerKind || kind.Name is "counting node" or "scoreboard" or "logic filter" or "respawn point"
                or "control point";

        private static uint[] EventsOf(ulong guid)
        {
            Kind kind = SourceKindOf(guid);
            if (kind == serverTimeKind)
                return kind.Events.Take(PropSettings.ServerTimeListeners(guid)).Append(ClickedEvent).ToArray();
            // P6432: every prop also offers the click event.
            return kind == null ? null : kind.Events.Contains(ClickedEvent) ? kind.Events : kind.Events.Append(ClickedEvent).ToArray();
        }

        #endregion

        #region links

        private static readonly List<WorldPropBindingModel> bindings = new();
        /// <summary>Most Links from one prop / on one build site (the same Link twice counts once).</summary>
        private const int MaxLinksPerProp = 64, MaxLinksPerSite = 1000;
        private static long nextBindingId;
        private static bool loaded;

        private static void EnsureLoaded()
        {
            if (loaded)
                return;
            lock (sync)
            {
                if (loaded)
                    return;
                using var context = DatabaseManager.Instance.CharacterDatabase.NewWorldContext();
                bindings.AddRange(context.WorldPropBinding.ToList());
                nextBindingId = bindings.Count == 0 ? 0 : (long)bindings.Max(b => b.Id);
                loaded = true;
                log.Info($"Loaded {bindings.Count} story-tool link(s).");
                // The site game raises Score Node / Control Point events through here (2.12).
                InstancedGames.StoryEvent ??= (guid, ev, player) =>
                {
                    Site s = SiteOf(guid);
                    if (s != null && s.Props.TryGetValue(guid, out Running running))
                        Fire(s, running, ev, new Context(player, 0), 0);
                };
            }
        }

        /// <summary>Links whose source raises this event.</summary>
        public static List<WorldPropBindingModel> LinksFrom(ulong source, uint eventId)
        {
            EnsureLoaded();
            lock (sync)
                return bindings.Where(b => b.SourceId == source && b.EventId == eventId).ToList();
        }

        private static List<WorldPropBindingModel> LinksOf(ulong prop)
        {
            lock (sync)
                return bindings.Where(b => b.SourceId == prop || b.TargetId == prop).ToList();
        }

        private static void Save(Action<Database.Character.CharacterContext> change)
            => DatabaseManager.Instance.CharacterDatabase.SaveWorld(change)
                .ContinueWith(t => log.Error(t.Exception, "Saving story-tool links failed."),
                    System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);

        private static void Delete(List<WorldPropBindingModel> gone)
        {
            if (gone.Count == 0)
                return;
            lock (sync)
                bindings.RemoveAll(gone.Contains);
            var ids = gone.Select(b => b.Id).ToList();
            Save(c => c.WorldPropBinding.RemoveRange(c.WorldPropBinding.Where(b => ids.Contains(b.Id))));
        }

        /// <summary>A prop was picked up or deleted: its Links go with it.</summary>
        public static void PropRemoved(ulong guid)
        {
            EnsureLoaded();
            Delete(LinksOf(guid));
        }

        #endregion

        #region play mode

        /// <summary>A linkable prop's state while its site is in Play Mode.</summary>
        private class Running
        {
            public ulong Guid;
            public uint PropId;
            public Kind Kind;
            public System.Numerics.Vector3 Position;
            public float Scale = 1f;
            /// <summary>The prop definition's name (a spawner's creature kind is found from it).</summary>
            public string Name = "";
            /// <summary>The owner's name for the prop (PropBase 0x19), "" when none: a spawner's creature is shown by it (6.30).</summary>
            public string CustomName = "";
            public bool On;
            public bool Locked;
            public bool Cog1, Cog2;
            public bool TimerRunning;
            public TimeSpan TimerLeft;
            public DateTime TimerEnds;
            public ulong Creature;
            public DateTime? RespawnAt;
            public bool NodeActive;
            /// <summary>A spawner creature's mood: the Story Node Mood selection it last played (218 calm, 219 angry, 220 happy, 221 sad).</summary>
            public uint Mood = 218;
            /// <summary>
            /// The chain that showed this Story Node's text (its triggering character and creature): "When the text on this
            /// node expires..." runs in it, so P5964's example once the creature has said this text, it says the next
            /// one reaches the same creature through the next node's fallback (TASKS 2.34; [I]: carrying the
            /// chain across the delay is ours).
            /// </summary>
            public Context TextContext;
            /// <summary>
            /// The chain that last started this timer: "When this timer completes..." runs in it (TASKS 2.34; [I] ours, as
            /// <see cref="TextContext"/>; a timer started by nothing in particular completes with no triggering character).
            /// </summary>
            public Context TimerContext;
            /// <summary>Counting Node / Scoreboard: the count and its maximum (EditableProperties 33 / 35, 0-99).</summary>
            public int Count, Max;
            /// <summary>Story Node: when its shown text expires (<see cref="NodeTextExpires"/>), null when none is showing.</summary>
            public DateTime? TextExpiresAt;
            /// <summary>Respawn Point 2701: whether Flee may bring players here (row 103 at Play start; Links 577530 / 577531).</summary>
            public bool Enabled;
            public readonly HashSet<WorldSession> Inside = new();
            /// <summary>Story creatures inside a Linking Area (for its creature enters / leaves events).</summary>
            public readonly HashSet<ulong> CreaturesInside = new();
        }

        /// <summary>
        /// A chain of Links: who or what set it off — a player (switch, area) and/or a creature (spawner events) — and the
        /// nodes met on the way. <see cref="Player"/> / <see cref="Creature"/> are also "the creature in the chain" that
        /// actions work on, and a Link may change them (a spawner's "walk along their route" puts its own creature in,
        /// <c>ctx with { Creature = ... }</c>).
        /// <para>
        /// The <b>triggering character</b> (TASKS 2.34; 11 Oct 2016, P6536: whichever entity started the action; P6537
        /// an NPC walking into a trigger volume → the NPC; P6538 a player clicking an NPC → the player) is
        /// <see cref="TriggerPlayer"/> / <see cref="TriggerCreature"/>: set once from the constructor's player / creature
        /// where the chain starts and copied unchanged by every <c>with</c>, so no Link in between can swap it. Only
        /// "...parse every entity in this area..." starts a sub-chain per entity with that entity as its trigger
        /// (wording, L23). Read by the IF Gate's character checks, the Score Node's "triggering Luminary", Control Point
        /// "Attempt to control", reward chest loot and the Combat Node's ability target.
        /// </para>
        /// </summary>
        private record Context(WorldSession Player, ulong Creature, ulong DamageNode = 0, ulong StoryNode = 0, ulong FocusNode = 0,
            ulong CombatNode = 0)
        {
            /// <summary>The player who kicked the chain off (null when a creature or a timer did).</summary>
            public WorldSession TriggerPlayer { get; init; } = Player;

            /// <summary>The creature that kicked the chain off, when no player did it (P6537); 0 otherwise.</summary>
            public ulong TriggerCreature { get; init; } = Player == null ? Creature : 0;
        }

        private class Site
        {
            public ulong ClaimId;
            public uint Zone;
            public readonly Dictionary<ulong, Running> Props = new();
            /// <summary>
            /// The world clock's hour (0-23) when the Server Time Nodes last looked (<see cref="Update"/>); set at Play
            /// start, so the hour a site starts in does not fire.
            /// </summary>
            public int LastHour;
        }

        private static readonly Dictionary<ulong, Site> sites = new();

        // ---- Prop state shown by the 0.695 client (TASKS 2.19) ---------------------------------------------------------

        /// <summary>
        /// Logic Filter lights: EditableProperties 107 / 108 (script_logic_gate_a blue, _b yellow). The 0.695 client shows a
        /// prop setting's state from UpdatePropInteractionState type 2 {u64 prop, u32 2, i32 key hash, u32 value, u32 -1,
        /// u64 0, u32 0, u32 0, u8 0}: FUN_1430ac970 → ClientPropSettingBehavior → FUN_1434777a0 passes the value to
        /// FUN_1430aa110 (light on when non-zero) (trace/0695/plan-storytools-next.md §0.3, Spec 5).
        /// </summary>
        public const string LogicGateA = "script_logic_gate_a", LogicGateB = "script_logic_gate_b";

        /// <summary>Counting Node / Scoreboard rows 33 (Starting Count; also the shown count's state key) and 35 (Max).</summary>
        public const string CounterCurrent = "prop_component_counter_current", CounterMax = "prop_component_counter_max";

        /// <summary>Counting Node actions / events (TASKS 2.2 / 2.3; plan-storytools-next.md Spec 7).</summary>
        private const uint CountUp = 577248, CountDown = 577249, CountToMax = 577250, CountToZero = 577251,
            CountReachedMax = 557290, CountReachedZero = 565055, CountIncreased = 577247, CountDecreased = 577092;

        /// <summary>Random Link Filter: "...Assign potential action..." (its outgoing Links) and "...Trigger Random Action."</summary>
        private const uint RandomPotential = 577591, RandomTrigger = 577593;

        /// <summary>Viewers of a prop's state: players within this distance (ours).</summary>
        private const float StateRange = 100f;

        /// <summary>States waiting for the next tick (coalesced: the last value per prop and key goes out).</summary>
        private static readonly Dictionary<(ulong Guid, string Key), (uint Zone, System.Numerics.Vector3 At, uint Value)> pendingStates = new();

        private static void QueueState(Site site, Running r, string key, uint value) => QueueState(site.Zone, r.Position, r.Guid, key, value);

        private static void QueueState(uint zone, System.Numerics.Vector3 at, ulong guid, string key, uint value)
        {
            lock (sync)
                pendingStates[(guid, key)] = (zone, at, value);
        }

        /// <summary>
        /// A Counting Node / Scoreboard's number outside Play Mode: its Starting Count (row 33) within its Max Count (row
        /// 35). [I] (storytools-695.md §6.7; L20): what the disc shows in Edit Mode is not stated; the Starting Count lets
        /// the builder see the number the story will start from, and is what §6.7 sends on leaving Play Mode.
        /// </summary>
        private static uint EditCount(ulong guid)
            => (uint)Math.Clamp((int)PropSettings.Get(guid, CounterCurrent), 0, Math.Clamp((int)PropSettings.Get(guid, CounterMax), 0, 99));

        /// <summary>
        /// A prop's setting changed in its window (PropSettings.Handle) or by dev "propset": a Counting Node / Scoreboard
        /// whose site is not playing shows its new Starting Count (TASKS 2.19). In Play Mode the running count stays as it
        /// is (settings are read at the next Play start).
        /// </summary>
        public static void SettingChanged(ulong guid, string key)
        {
            // A Respawn Point's "Enabled" box: its enabled / disabled look (P6178; Props.RespawnLookChanged).
            if (key == PropSettings.RespawnPointEnabled)
            {
                Props.RespawnLookChanged(guid);
                return;
            }
            if (key is not (CounterCurrent or CounterMax) || SiteOf(guid) != null || Props.Find(guid) is not Props.PlacedProp p
                || p.PropId is not (CountingNodeProp or ScoreboardProp))
                return;
            QueueState(p.Zone, new System.Numerics.Vector3(p.Spawn.Position.X, p.Spawn.Position.Y, p.Spawn.Position.Z), guid,
                CounterCurrent, EditCount(guid));
        }

        private static byte[] StateBody(ulong guid, string key, uint value)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(guid);
            w.Write(2u);
            w.Write(PropSettings.KeyHash(key));
            w.Write(value);
            w.Write(uint.MaxValue);
            w.Write(0ul);
            w.Write(0u);
            w.Write(0u);
            w.Write((byte)0);
            return output.ToArray();
        }

        /// <summary>Sends the queued states, one body per prop and key for all its viewers.</summary>
        private static void FlushStates()
        {
            List<KeyValuePair<(ulong Guid, string Key), (uint Zone, System.Numerics.Vector3 At, uint Value)>> due;
            lock (sync)
            {
                if (pendingStates.Count == 0)
                    return;
                due = pendingStates.ToList();
                pendingStates.Clear();
            }
            foreach (var ((guid, key), (zone, at, value)) in due)
            {
                // A Counting Node / Logic Filter is not in the world while its site plays (Props.PlayModeVisibility.cs):
                // its states go to nobody then (bandwidth); it gets its state again when it is spawned (SendStateTo).
                Props.PlayModeSight sight = Props.SightOf(guid);
                if (sight == Props.PlayModeSight.Nobody)
                    continue;
                byte[] body = StateBody(guid, key, value);
                foreach (WorldSession p in PlayerManager.InZone(zone))
                    if (p.RezoneTarget == null && System.Numerics.Vector3.Distance(p.Position, at) <= StateRange
                        && Props.Sees(p, sight))
                        p.EnqueueMessage(ZoneMessageOpcode.UpdatePropInteractionState, body);
            }
        }

        /// <summary>
        /// An on/off prop's state while its site is in Play Mode (Links may have switched it), or null when its site is
        /// not playing — the prop's own Disabled flag (PropBase SetPropEnable) holds then. Read by the moving platform
        /// (Props.Platforms: linking and triggering work with the Moving Platform, P2668).
        /// </summary>
        public static bool? PlayModeOn(ulong guid)
        {
            lock (sync)
                foreach (Site s in sites.Values)
                    if (s.Props.TryGetValue(guid, out Running r))
                        return r.On;
            return null;
        }

        /// <summary>A prop came into a player's view: its current story state (Logic Filter lights in Play Mode).</summary>
        public static void SendStateTo(WorldSession session, ulong guid)
        {
            Running r = null;
            lock (sync)
                foreach (Site s in sites.Values)
                    if (s.Props.TryGetValue(guid, out r))
                        break;
            if (r == null)
            {
                // Not playing: a Counting Node / Scoreboard still shows its number (its Starting Count, [I] L20); a Logic
                // Filter shows its lights off, the client's own look without a state, so nothing is sent for it.
                if (Props.Find(guid) is Props.PlacedProp p && p.PropId is CountingNodeProp or ScoreboardProp)
                    session.EnqueueMessage(ZoneMessageOpcode.UpdatePropInteractionState, StateBody(guid, CounterCurrent, EditCount(guid)));
                return;
            }
            if (r.PropId is CountingNodeProp or ScoreboardProp)
            {
                session.EnqueueMessage(ZoneMessageOpcode.UpdatePropInteractionState, StateBody(guid, CounterCurrent, (uint)r.Count));
                return;
            }
            // What Links switched while the site plays (ShowOn), for a player the prop is streamed to now (prop streaming,
            // Props.Stream.cs, or after a re-zone): a door / gate / lever a Link opened plays its open move (the spawn
            // shows the closed one), a light a Link switched gets its effect as the Link left it (the spawn shows it as
            // built, Props.SendSpawns: on unless switched off with SetPropEnable).
            if (PropAnimations.SetFor(r.PropId) != 0)
            {
                if (r.On)
                    PropAnimations.Send(session, r.PropId, guid, PropAnimations.Open);
            }
            else if (PropEffects.HasEffect(r.PropId) && Props.Find(guid) is Props.PlacedProp built && r.On == built.Disabled)
                PropEffects.Send(session, r.PropId, guid, r.On);
            if (r.PropId != LogicFilterProp)
                return;
            session.EnqueueMessage(ZoneMessageOpcode.UpdatePropInteractionState, StateBody(guid, LogicGateA, r.Cog1 ? 1u : 0u));
            session.EnqueueMessage(ZoneMessageOpcode.UpdatePropInteractionState, StateBody(guid, LogicGateB, r.Cog2 ? 1u : 0u));
        }
        private static readonly HashSet<ulong> pendingResets = new();
        /// <summary>Links followed per chain at most: a loop of Links (A turns on B, B turns on A) stops there.</summary>
        private const int MaxChain = 64;
        private const float AreaRadius = 4f;

        /// <summary>
        /// Inside a Linking Area. One made with the Area Tool is a trigger volume (AreaDefinition, TASKS 2.33): inside
        /// any of its shapes — the box the player drew and later moved / scaled / turned through its prop (centre =
        /// prop position, size = prop scale per axis, the prop's rotation; <see cref="AreaDefinitions.BoxesOf"/>, read
        /// live so an edit counts at once). A Linking Area without an area (placed before areas were kept, or copied in a
        /// design) keeps the old shape: within its radius (× scale) across the ground and 1.5 × that up or down (a plain
        /// sphere missed creatures walking on ground a few metres below where the area was placed).
        /// </summary>
        private static bool InArea(Running area, System.Numerics.Vector3 at)
        {
            if (AreaDefinitions.BoxesOf(area.Guid) is { Length: > 0 } boxes)
            {
                foreach (AreaDefinitions.Box box in boxes)
                    if (box.Contains(at))
                        return true;
                return false;
            }
            float radius = AreaRadius * area.Scale;
            float dx = at.X - area.Position.X, dz = at.Z - area.Position.Z;
            return dx * dx + dz * dz <= radius * radius && Math.Abs(at.Y - area.Position.Y) <= radius * AreaHalfHeight;
        }

        /// <summary>
        /// A Linking Area as the upright cylinder the terrain destruction layer takes (<see cref="VoxelDestruction"/>):
        /// the old radius shape as it is; for an Area Tool box (first shape) the cylinder around it — radius = half the
        /// box's ground diagonal, so any turn about the vertical stays covered, half height = half the box height
        /// (a box tilted on its side is only approximated). A box test in the destruction layer would be exact.
        /// </summary>
        private static (System.Numerics.Vector3 Centre, float Radius, float HalfHeight) AreaCylinder(ulong guid, System.Numerics.Vector3 position, float scale)
        {
            if (AreaDefinitions.BoxesOf(guid) is { Length: > 0 } boxes)
            {
                AreaDefinitions.Box b = boxes[0];
                return (b.Centre, MathF.Sqrt(b.Half.X * b.Half.X + b.Half.Z * b.Half.Z), b.Half.Y);
            }
            return (position, AreaRadius * scale, AreaRadius * scale * AreaHalfHeight);
        }

        /// <summary>A Linking Area's half height as a multiple of its radius (<see cref="InArea"/>).</summary>
        private const float AreaHalfHeight = 1.5f;

        /// <summary>
        /// The Linking Areas of a site in Play Mode whose "Allow Voxel Destruction" (row 102,
        /// <see cref="PropSettings.AreaVoxelPermission"/>) is not "Site-wide setting": (centre, radius, half height — local
        /// metres, the <see cref="InArea"/> cylinder — and the choice: <see cref="PropSettings.AreaDestructionAllowed"/> or
        /// <see cref="PropSettings.AreaDestructionBlocked"/>). Empty when the site is not playing. Read by
        /// <see cref="VoxelDestruction"/> before a destruction (outside its terrain lock).
        /// </summary>
        public static List<(System.Numerics.Vector3 Centre, float Radius, float HalfHeight, uint Rule)> DestructionAreas(ulong claimId)
        {
            List<(ulong Guid, System.Numerics.Vector3 Position, float Scale)> areas;
            lock (sync)
            {
                if (!sites.TryGetValue(claimId, out Site site))
                    return new();
                areas = site.Props.Values.Where(r => r.PropId == LinkingAreaProp).Select(r => (r.Guid, r.Position, r.Scale)).ToList();
            }
            var result = new List<(System.Numerics.Vector3, float, float, uint)>();
            foreach ((ulong guid, System.Numerics.Vector3 position, float scale) in areas)
            {
                uint rule = PropSettings.Choice(guid, PropSettings.AreaVoxelPermission);
                if (rule == PropSettings.AreaDestructionSiteWide)
                    continue;
                (System.Numerics.Vector3 centre, float radius, float halfHeight) = AreaCylinder(guid, position, scale);
                result.Add((centre, radius, halfHeight, rule));
            }
            return result;
        }

        // Settings from the props' settings windows (PropSettings; EditableProperties defaults: timer 60 s without
        // restart, dice 50/50, damage 50 %, spawner respawn after 30 s).
        private static TimeSpan TimerLength(Running r) => TimeSpan.FromSeconds(Math.Max(1f, PropSettings.Get(r.Guid, PropSettings.TimerDuration)));

        private static float DiceOdds(Running r)
        {
            // "Odds of Winning" in percent (TASKS 2.30); a weight B saved before is ignored.
            return Math.Clamp(PropSettings.Get(r.Guid, PropSettings.DiceWeightA), 0f, 100f) / 100f;
        }

        /// <summary>
        /// The amount (% of max health) of the Damage Node that set the chain off ("Damage Amount", row 30; its default 50
        /// when no node did), and whether it heals instead ("Heal Instead", row 32).
        /// </summary>
        private static (float Percent, bool Heal) DamageOf(Context ctx)
            => (PropSettings.Get(ctx.DamageNode, PropSettings.DamageValue),
                ctx.DamageNode != 0 && PropSettings.Get(ctx.DamageNode, PropSettings.DamageHeals) != 0);

        /// <summary>A Damage Node's share of a story creature's health, as damage or healing. Creatures it kills drop nothing (P5768).</summary>
        private static void HurtCreature(ulong creature, Context ctx)
        {
            (float percent, bool heal) = DamageOf(ctx);
            if (heal)
                Creatures.HealStory(creature, percent);
            else
                Creatures.DamageStory(creature, percent);
        }

        /// <summary>A Damage Node's share of a player's maximum health, as damage or healing.</summary>
        private static void HurtPlayer(WorldSession player, Context ctx)
        {
            (float percent, bool heal) = DamageOf(ctx);
            uint amount = (uint)Math.Ceiling(Health.Max(player) * percent / 100f);
            if (amount == 0)
                return;
            if (heal)
                Health.Heal(player, amount);
            else
                Health.Damage(player, amount);
            log.Info($"Story damage: {player.CharacterName} {(heal ? "+" : "−")}{percent:0} %.");
        }

        /// <summary>The site's story creatures inside a Linking Area.</summary>
        private static List<ulong> CreaturesIn(Site site, Running area)
            => site.Props.Values.Where(s => s.Creature != 0 && Creatures.StoryPosition(s.Creature) is System.Numerics.Vector3 at && InArea(area, at))
                .Select(s => s.Creature).ToList();

        /// <summary>
        /// A Respawn Point's "Enabled" box (row 103, default 1). The default is spelled out here rather than read from
        /// EditableProperties so that a data set without row 103 (0.589's) does not start every point disabled.
        /// </summary>
        private static bool RespawnPointEnabledSetting(ulong guid)
            => !PropSettings.TryGetSaved(guid, PropSettings.RespawnPointEnabled, out float v) || v != 0f;

        /// <summary>
        /// The world's game hour (0-23): the shared day/night clock everyone sees (<see cref="WorldClock"/>), not a
        /// site's own custom time (P6165: Server Time Nodes use game time).
        /// </summary>
        private static int GameHour() => Math.Clamp((int)(WorldClock.SecondsOfDay / 3600), 0, 23);

        /// <summary>
        /// Play Mode switched on or off for a site (Claims.SetPlayMode). On: the site's linkable props start (lights keep
        /// how they look, spawners spawn their creature). Off: creatures go away, lights go back to how they were built.
        /// </summary>
        public static void PlayModeChanged(ClaimModel claim, bool on)
        {
            EnsureLoaded();
            Stop(claim.Id);
            if (!on)
                return;
            var site = new Site { ClaimId = claim.Id, Zone = claim.ZoneId, LastHour = GameHour() };
            foreach (var p in Props.PlacedAll(null).Where(p => p.Zone == claim.ZoneId && Claims.ClaimAt(p.Zone, p.Position) == claim.Id))
            {
                // Story props, and any other prop with a click Link of its own (P6432).
                Kind kind = KindOf(p.PropId) ?? (HasClickLink(p.Guid) ? clickOnlyKind : null);
                if (kind == null)
                    continue;
                Props.PlacedProp placed = Props.Find(p.Guid);
                site.Props[p.Guid] = new Running
                {
                    Guid = p.Guid,
                    PropId = p.PropId,
                    Kind = kind,
                    Position = p.Position,
                    Scale = placed?.Spawn.Scale.X ?? 1f,
                    Name = SpawnerName(p.PropId),
                    CustomName = placed?.CustomName ?? "",
                    // Lights start as built (lit unless switched off); moving props start shut.
                    On = placed != null && !placed.Disabled && PropAnimations.SetFor(p.PropId) == 0,
                    TimerLeft = TimeSpan.FromSeconds(PropSettings.Get(p.Guid, PropSettings.TimerDuration)),
                    Max = Math.Clamp((int)PropSettings.Get(p.Guid, CounterMax), 0, 99),
                    // Respawn Points start as their "Enabled" box says (before the site is published, so a Flee at this
                    // moment already sees it). Its enabled / disabled look (P6178, AnimationSelection 848 / 849) follows
                    // the Edit Mode box only: in Play Mode the point is hidden (Props.PlayModeVisibility.cs).
                    Enabled = p.PropId == RespawnPointProp && RespawnPointEnabledSetting(p.Guid)
                };
            }
            lock (sync)
                sites[claim.Id] = site;
            // Counting Nodes / Scoreboards start at their Starting Count (shown on the disc, 0.695).
            foreach (Running r in site.Props.Values.Where(r => r.PropId is CountingNodeProp or ScoreboardProp))
            {
                r.Count = Math.Clamp((int)PropSettings.Get(r.Guid, CounterCurrent), 0, r.Max);
                QueueState(site, r, CounterCurrent, (uint)r.Count);
            }
            // Logic Filters start with both lights off.
            foreach (Running r in site.Props.Values.Where(r => r.PropId == LogicFilterProp))
            {
                QueueState(site, r, LogicGateA, 0);
                QueueState(site, r, LogicGateB, 0);
            }
            log.Info($"Site {claim.Id} is in Play Mode: {site.Props.Count} story prop(s).");
            FlagClickable(site);
            // Spawners with "Spawn Automatically" (row 9) spawn now; the others wait for a Link (TASKS 2.29).
            foreach (Running r in site.Props.Values.Where(r => r.Kind == spawnerKind
                && PropSettings.Get(r.Guid, PropSettings.SpawnAutomatically) != 0).ToList())
                Spawn(site, r, 0);
        }

        private static void Stop(ulong claimId)
        {
            Site site;
            lock (sync)
                if (!sites.Remove(claimId, out site))
                    return;
            lock (sync)
                clickLinked.ExceptWith(site.Props.Keys);
            foreach (Running r in site.Props.Values)
            {
                if (r.Creature != 0)
                    Creatures.DespawnStory(r.Creature);
                if (r.PropId == LogicFilterProp && (r.Cog1 || r.Cog2))
                {
                    QueueState(site, r, LogicGateA, 0);   // lights off again in Edit Mode
                    QueueState(site, r, LogicGateB, 0);
                }
                // Counters show their Edit value again (§6.7: "On leaving Play Mode send the Edit values"). A reset's
                // restart queues its Play-start count right after; the flush sends only the last value per prop.
                if (r.PropId is CountingNodeProp or ScoreboardProp && (uint)r.Count != EditCount(r.Guid))
                    QueueState(site, r, CounterCurrent, EditCount(r.Guid));
                // Lights back to how they were built; doors shut.
                Props.PlacedProp placed = Props.Find(r.Guid);
                if (PropAnimations.SetFor(r.PropId) != 0)
                {
                    if (r.On)
                        ShowOn(site, r, false);
                }
                else if (r.Kind.Toggle && placed != null && r.On == placed.Disabled)
                    ShowOn(site, r, !placed.Disabled);
            }
            // Questions asked on this site take no more answers (§6.8 "clear question indicators"; a reset starts the
            // story again, so its old questions are gone too). The client's Yes / No indicator has no known server-side
            // close; an answer to it is ignored (DialogResponse: unknown id).
            lock (sync)
                foreach (ulong id in questions.Where(q => q.Value.ClaimId == claimId).Select(q => q.Key).ToList())
                    questions.Remove(id);
            log.Info($"Site {claimId} left Play Mode.");
        }

        private static Site SiteOf(ulong prop)
        {
            lock (sync)
                return sites.Values.FirstOrDefault(s => s.Props.ContainsKey(prop));
        }

        /// <summary>Raises a prop's event: every Link from it runs its action.</summary>
        private static void Fire(Site site, Running source, uint eventId, Context ctx, int depth)
        {
            if (depth > MaxChain)
                return;
            foreach (WorldPropBindingModel link in LinksFrom(source.Guid, eventId))
                if (site.Props.TryGetValue(link.TargetId, out Running target))
                    Execute(site, target, link.ActionId, ctx, depth + 1);
        }

        /// <summary>Runs an action on a prop.</summary>
        private static void Execute(Site site, Running r, uint action, Context ctx, int depth)
        {
            Kind k = r.Kind;
            if (k.Toggle)
            {
                int i = Array.IndexOf(k.Actions, action);
                if (i is >= 0 and <= 2)
                {
                    // A locked door / gate / portcullis stays as it is.
                    if (r.Locked && k.LockEvents.Length > 0)
                        return;
                    SetOn(site, r, i == 0 || (i == 2 && !r.On), ctx, depth);
                    return;
                }
            }
            int li = Array.IndexOf(k.LockActions, action);
            if (li >= 0 && k.LockEvents.Length == 2)
            {
                bool locked = li == 0;
                if (r.Locked != locked)
                {
                    r.Locked = locked;
                    Fire(site, r, k.LockEvents[li], ctx, depth);
                }
                return;
            }
            switch (action)
            {
                case RewardChests.LootAction:
                    // "...Loot this chest": the chain's triggering player (and their group) loot it, locked or not (a
                    // Link is how builders deliver loot from locked chests, 57918 #7 [I]); no player in the chain:
                    // nothing [I]. "When this chest is looted..." runs only when someone got loot.
                    RewardChests.Loot(ctx.TriggerPlayer, r.Guid, () => Fire(site, r, RewardChests.LootedEvent, ctx, depth));
                    break;
                case 576358:                             // ...Open this chest.
                    if (!r.Locked)
                        Fire(site, r, 576464, ctx, depth);
                    break;
                case StartCog1: SetCogs(site, r, true, r.Cog2, ctx, depth); break;
                case StopCog1: SetCogs(site, r, false, r.Cog2, ctx, depth); break;
                case StartCog2: SetCogs(site, r, r.Cog1, true, ctx, depth); break;
                case StopCog2: SetCogs(site, r, r.Cog1, false, ctx, depth); break;
                case StartBoth: SetCogs(site, r, true, true, ctx, depth); break;
                case StopBoth: SetCogs(site, r, false, false, ctx, depth); break;
                case ToggleCog1: SetCogs(site, r, !r.Cog1, r.Cog2, ctx, depth); break;
                case ToggleCog2: SetCogs(site, r, r.Cog1, !r.Cog2, ctx, depth); break;
                case ToggleBoth: SetCogs(site, r, !r.Cog1, !r.Cog2, ctx, depth); break;
                case RollDice:
                    Fire(site, r, Random.Shared.NextSingle() < DiceOdds(r) ? DiceWins : DiceLoses, ctx, depth);
                    break;
                case StartTimer:
                    if (!r.TimerRunning)
                    {
                        r.TimerRunning = true;
                        r.TimerEnds = DateTime.UtcNow + r.TimerLeft;
                        r.TimerContext = ctx;
                        Fire(site, r, TimerStarts, ctx, depth);
                    }
                    break;
                case PauseTimer:
                    if (r.TimerRunning)
                    {
                        r.TimerRunning = false;
                        r.TimerLeft = r.TimerEnds - DateTime.UtcNow;
                        Fire(site, r, TimerPauses, ctx, depth);
                    }
                    break;
                case ResetTimer:
                case PauseAndResetTimer:
                    if (action == PauseAndResetTimer)
                        r.TimerRunning = false;
                    r.TimerLeft = TimerLength(r);
                    if (r.TimerRunning)
                        r.TimerEnds = DateTime.UtcNow + r.TimerLeft;
                    Fire(site, r, TimerResets, ctx, depth);
                    break;
                case ActivateEverything: ActivateNode(site, r, NodeEverythingActivates, PerformEverything, ctx, depth); break;
                case ActivateMood: ActivateNode(site, r, NodeMoodActivates, SetMood, ctx, depth); break;
                case ActivateStance: ActivateNode(site, r, NodeStanceActivates, SetStance, ctx, depth); break;
                case ActivateEmote: ActivateNode(site, r, NodeEmoteActivates, PerformEmote, ctx, depth); break;
                case ActivateText: ActivateNode(site, r, NodeTextActivates, SayText, ctx, depth); break;
                case ActivateWeaponVisuals: ActivateNode(site, r, NodeWeaponVisualsActivates, SetWeaponVisuals, ctx, depth); break;
                case SetMood:
                case SetStance:
                case PerformEmote:
                case SayText:
                case PerformEverything:
                case SetWeaponVisuals:
                    if (r.Creature != 0)
                        Perform(site, r, action, ctx);
                    break;
                case EquipWeapon:
                case UnequipWeapon:
                    // "...Have this creature equip / unequip their weapon." (0.589 sentences, kept in 0.695): its weapons
                    // equipped or sheathed (Creatures.StoryWeapons; the same two selections as the Story Node's Weapon
                    // Visuals).
                    if (r.Creature != 0 && Creatures.StoryWeapons(r.Creature, action == EquipWeapon))
                        log.Debug($"Story creature 0x{r.Creature:X} {(action == EquipWeapon ? "equips" : "sheathes")} its weapons.");
                    break;
                case ActivateFocus:
                    r.NodeActive = true;
                    Fire(site, r, FocusActivates, ctx with { FocusNode = r.Guid }, depth);
                    break;
                case DeactivateFocus:
                    r.NodeActive = false;
                    Fire(site, r, FocusDeactivates, ctx with { FocusNode = r.Guid }, depth);
                    break;
                case WalkHere:
                    // The creature in the chain (a spawner's "spawns", a Travel Point's "arrives") walks to this point.
                    if (ctx.Creature != 0)
                        Creatures.StoryWalk(ctx.Creature, r.Position, r.Guid);
                    break;
                case FactionEnemy:
                case FactionNeutral:
                case FactionAlly:
                    if (r.Creature != 0)
                    {
                        Creatures.StoryFaction(r.Creature, action == FactionEnemy ? 0 : action == FactionNeutral ? 1 : 2);
                        log.Info($"Story creature 0x{r.Creature:X} is now {(action == FactionEnemy ? "an enemy" : action == FactionNeutral ? "neutral" : "an ally")}.");
                    }
                    break;
                case WalkRoute:
                    // "...Have this creature walk along their route.": the route is what the builder linked from the
                    // spawner's "When this creature is told to walk along their route..." (Travel Points in turn).
                    if (r.Creature != 0)
                        Fire(site, r, CreatureToldToWalk, ctx with { Creature = r.Creature }, depth);
                    break;
                case FocusOnNode:
                    // Look at the Focus Node, or at whoever set it off when the node says so ("Focus on What Activated This").
                    // "What activated this" is the chain's triggering character (2.34): the player, else the creature that
                    // set it off ([I] the creature case: P6536 "may be either a player or an NPC").
                    if (r.Creature != 0 && site.Props.TryGetValue(ctx.FocusNode, out Running focus))
                        Creatures.StoryFocus(r.Creature, PropSettings.Get(focus.Guid, PropSettings.FocusOnActivator) == 0 ? focus.Position
                            : ctx.TriggerPlayer != null ? ctx.TriggerPlayer.Position
                            : ctx.TriggerCreature != 0 && ctx.TriggerCreature != r.Creature && Creatures.StoryPosition(ctx.TriggerCreature) is System.Numerics.Vector3 c
                                ? c : focus.Position);
                    break;
                case StopFocusing:
                    if (r.Creature != 0)
                        Creatures.StoryFocus(r.Creature, null);
                    break;
                case ActivateDamage:
                    Fire(site, r, DamageActivates, ctx with { DamageNode = r.Guid }, depth);
                    break;
                case CombatSetTactics:
                {
                    // The node's tactics (its row 31) on its linked creature now, then its "tactics activates" Links (which
                    // reach spawners' "Set this creature's combat tactics" with this node in the chain).
                    if (CombatCreatureOf(r.Guid) is ulong linked)
                        Creatures.StoryTactics(linked, (int)PropSettings.Choice(r.Guid, PropSettings.SpawnerAi));
                    Fire(site, r, CombatTacticsActivates, ctx with { CombatNode = r.Guid }, depth);
                    break;
                }
                case SpawnerSetTactics:
                    // From a Combat Node's "tactics activates" Link: that node's tactics; otherwise the spawner's own row 31.
                    if (r.Creature != 0)
                        Creatures.StoryTactics(r.Creature, (int)PropSettings.Choice(ctx.CombatNode != 0 ? ctx.CombatNode : r.Guid,
                            PropSettings.SpawnerAi));
                    break;
                case SpawnerDeactivateAi:
                    if (r.Creature != 0)
                        Creatures.StoryTactics(r.Creature, Creatures.TacticsNothing);
                    break;
                case CombatLinkTarget:
                    break;                                        // only marks the creature a Combat Node controls
                case CompleteAdventure:
                    InstancedGames.CompleteAdventure(site.ClaimId);
                    break;
                case IfCheck:
                {
                    // The condition's prop: the gate's one "Choose the prop..." Link. No prop, or it isn't running: Else.
                    // A check on the triggering character needs no prop (P6535-P6538).
                    WorldPropBindingModel pick = LinksFrom(r.Guid, IfChooseProp).FirstOrDefault(l => site.Props.ContainsKey(l.TargetId));
                    bool result = PropSettings.IsTriggerCheck(PropSettings.Choice(r.Guid, PropSettings.IfProperty))
                        ? IfHolds(r.Guid, null, ctx)
                        : pick != null && IfHolds(r.Guid, site.Props[pick.TargetId], ctx);
                    log.Debug($"IF gate 0x{r.Guid:X}: {(result ? "if" : "else")}.");
                    Fire(site, r, result ? IfTrue : IfFalse, ctx, depth);
                    break;
                }
                case IfLinkTarget:
                    break;                                        // only marks the prop an IF Gate tests
                case RandomTrigger:
                {
                    // One of the filter's "potential action" Links, at random each time (P6167; help page
                    // ScriptObj_Random_FTE: "one link each time will be chosen at random"). None: nothing.
                    var links = LinksFrom(r.Guid, RandomPotential).Where(l => site.Props.ContainsKey(l.TargetId)).ToList();
                    if (links.Count == 0 || depth > MaxChain)
                        break;
                    int pick = Random.Shared.Next(links.Count);
                    log.Debug($"Random filter 0x{r.Guid:X} chose link {pick + 1} of {links.Count}.");
                    Execute(site, site.Props[links[pick].TargetId], links[pick].ActionId, ctx, depth + 1);
                    break;
                }
                case CountUp:
                case CountDown:
                case CountToMax:
                case CountToZero:
                {
                    // Clamped to 0..Max; only a real change fires (as the Logic Filter's events, P5780 / P5819): up or down,
                    // then "reaches its maximum" / "reaches 0". The disc shows the count (state key 33).
                    int was = r.Count;
                    r.Count = Math.Clamp(action switch { CountUp => was + 1, CountDown => was - 1, CountToMax => r.Max, _ => 0 }, 0, r.Max);
                    if (r.Count == was)
                        break;
                    QueueState(site, r, CounterCurrent, (uint)r.Count);
                    log.Debug($"Counter 0x{r.Guid:X}: count {r.Count}{(r.Count == r.Max ? " (max)" : "")}.");
                    Fire(site, r, r.Count > was ? CountIncreased : CountDecreased, ctx, depth);
                    if (r.Count == r.Max)
                        Fire(site, r, CountReachedMax, ctx, depth);
                    if (r.Count == 0)
                        Fire(site, r, CountReachedZero, ctx, depth);
                    break;
                }
                case ActivateRespawnPoint:
                case DeactivateRespawnPoint:
                {
                    // "...Activate / Deactivate this Respawn Location." (577530 / 577531): only a real change fires "When
                    // this Respawn Location activates / deactivates..." (577528 / 577529). Inferred (ours): the "only real
                    // changes fire" rule is the Logic Filter's (P5780 / P5819), applied here as for the Counting Node
                    // (OPEN-LEADS L23). Play Mode only: the stored "Enabled" box (row 103) is left as the builder set it.
                    bool enable = action == ActivateRespawnPoint;
                    if (r.Enabled == enable)
                        break;
                    r.Enabled = enable;
                    log.Debug($"Respawn point 0x{r.Guid:X} {(enable ? "enabled" : "disabled")}.");
                    Fire(site, r, enable ? RespawnPointActivates : RespawnPointDeactivates, ctx, depth);
                    break;
                }
                case LaunchFirework:
                {
                    DateTime now = DateTime.UtcNow;
                    lock (sync)
                    {
                        if (lastLaunch.TryGetValue(r.Guid, out DateTime last) && now - last < FireworksInterval)
                            break;
                        lastLaunch[r.Guid] = now;
                    }
                    // Not to players in the middle of a re-zone (anything with it can hang their client).
                    PropEffects.Launch(PlayerManager.InZone(site.Zone)
                        .Where(p => p.RezoneTarget == null && System.Numerics.Vector3.Distance(p.Position, r.Position) <= FireworksRange),
                        r.PropId, r.Guid);
                    break;
                }
                case SignDisplaysText:
                case BookDisplaysText:
                case ScrollDisplaysText:
                    // Where (Text Location: on screen / chat bubble / chat) and for how long (Text Duration) are the
                    // prop's own settings.
                    Props.DisplayText(r.Guid, PropSettings.Choice(r.Guid, PropSettings.TextDisplayType),
                        (uint)Math.Max(1f, PropSettings.Get(r.Guid, PropSettings.TextDisplayTime)));
                    break;
                case ResetStory:
                    // Restarts Play Mode once this chain has finished (next tick).
                    lock (sync)
                        pendingResets.Add(site.ClaimId);
                    break;
                case DamageCreaturesInArea:
                case DamageAllInArea:
                case DamageLuminariesInArea:
                    if (action != DamageLuminariesInArea)
                        foreach (ulong creature in CreaturesIn(site, r))
                            HurtCreature(creature, ctx);
                    if (action != DamageCreaturesInArea)
                        foreach (WorldSession p in PlayersOn(site).Where(p => InArea(r, p.Position)))
                            HurtPlayer(p, ctx);
                    break;
                case DestroyVoxelsInArea:
                case HealVoxelsInArea:
                    // The area's cylinder (as InArea) in the terrain destruction layer, honouring row 102 / the site rule for
                    // destroying; the WorldEditingPlayEffect goes to the 0.695 players near it (VoxelDestruction).
                    {
                        (System.Numerics.Vector3 centre, float radius, float halfHeight) = AreaCylinder(r.Guid, r.Position, r.Scale);
                        VoxelDestruction.AreaAction(site.ClaimId, site.Zone, r.Guid, centre, radius, halfHeight,
                            action == DestroyVoxelsInArea);
                    }
                    break;
                case ParseArea:
                    // "For every object in this area when parsed...": once per player, then per story creature inside.
                    foreach (WorldSession p in PlayersOn(site).Where(p => InArea(r, p.Position)))
                        Fire(site, r, EntityParsed, ctx with { Player = p, Creature = 0, TriggerPlayer = p, TriggerCreature = 0 }, depth);
                    foreach (ulong creature in CreaturesIn(site, r))
                        Fire(site, r, EntityParsed, ctx with { Player = null, Creature = creature, TriggerPlayer = null, TriggerCreature = creature }, depth);
                    break;
                case SpawnCreature:
                    if (r.Creature == 0)
                        Spawn(site, r, depth);
                    break;
                case DespawnCreature:
                    if (r.Creature != 0)
                    {
                        Creatures.DespawnStory(r.Creature);
                        r.Creature = 0;
                        r.RespawnAt = null;
                    }
                    break;
                case DamageCreature:
                    if (r.Creature != 0)
                        HurtCreature(r.Creature, ctx);
                    break;
                case uint a when Array.IndexOf(CombatAbilityActions, a) >= 0:
                {
                    // "...Have this node's linked creature perform Ability N": the creature uses that ability now on the
                    // triggering player, else on whoever it is fighting (target not stated, L23: ours).
                    int n = Array.IndexOf(CombatAbilityActions, a) + 1;
                    if (CombatCreatureOf(r.Guid) is ulong linked && !Creatures.StoryUseAbility(linked, n, ctx.TriggerPlayer))
                        log.Debug($"Combat node 0x{r.Guid:X}: creature 0x{linked:X} can't use ability {n} now.");
                    break;
                }
                default:
                    // Score Node / Victory Node / Control Point actions belong to the site game (2.12).
                    // The triggering character: "Add points to the triggering Luminary's score", "Attempt to control" (2.34).
                    if (InstancedGames.LinkAction(r.Guid, action, ctx.TriggerPlayer, ctx.TriggerCreature))
                        break;
                    log.Debug($"Story action {action} on {k.Name} 0x{r.Guid:X} has no effect yet.");
                    break;
            }
        }

        /// <summary>
        /// A Story Node activates: its Links run; with no Link for that part, the fallbacks of 11 May 2016 (P5284-P5288,
        /// P5116): the Story Node looks at what first triggered the Link; a creature says the text itself (and plays
        /// the animations), otherwise the Story Node says it.
        /// </summary>
        private static void ActivateNode(Site site, Running node, uint eventId, uint creatureAction, Context ctx, int depth)
        {
            ctx = ctx with { StoryNode = node.Guid };
            bool linked = LinksFrom(node.Guid, eventId).Any(l => site.Props.ContainsKey(l.TargetId));
            Fire(site, node, eventId, ctx, depth);
            if (linked)
                return;
            Running spawner = ctx.Creature == 0 ? null : site.Props.Values.FirstOrDefault(s => s.Creature == ctx.Creature);
            if (spawner != null)
                Perform(site, spawner, creatureAction, ctx);
            else if (creatureAction is SayText or PerformEverything)
                SayNodeText(site, node.Guid, node.Guid, 0, node.Position, ctx);
        }

        /// <summary>
        /// A spawner's creature acts out the Story Node that set the chain off: its mood, stance and emote (each an
        /// AnimationSelection played on the creature, <see cref="PropSettings.DropDowns"/>) and its text
        /// (<see cref="SayNodeText"/>). The mood goes first: Emote_Emotion colours the stance's idle and the emote that
        /// follow (the player network's Emotion_Switch nodes, trace/systems/social.md). With "Announce Emotes in Chat"
        /// (row 24) on the node, each also prints the client's own third-person line (<see cref="AnnounceLines"/>).
        /// <para>
        /// Weapon visuals (row 41, <see cref="SetWeaponVisuals"/>) go first, being the pose the rest plays over. In "perform
        /// everything" (P6113 "...perform all actions") they are applied only when the node's Weapon Visuals was saved by
        /// its builder: [I, ours] the row is new in 0.695, so a node made before it (or never set) leaves the creature's
        /// weapons as they are instead of drawing them on every "everything".
        /// </para>
        /// </summary>
        private static void Perform(Site site, Running spawner, uint action, Context ctx)
        {
            ulong node = ctx.StoryNode;
            bool all = action == PerformEverything;
            bool announce = PropSettings.Get(node, PropSettings.EmoteSpam) != 0;
            System.Numerics.Vector3? at = Creatures.StoryPosition(spawner.Creature);
            if (action == SetWeaponVisuals || (all && node != 0 && PropSettings.TryGetSaved(node, PropSettings.WeaponVisuals, out _)))
                Creatures.StoryWeapons(spawner.Creature,
                    PropSettings.Choice(node, PropSettings.WeaponVisuals) == Creatures.AnimNpcWeaponsEquipped);
            void Play(uint selection)
            {
                Creatures.PlayStoryAnimation(spawner.Creature, selection);
                if (announce && at is System.Numerics.Vector3 where && AnnounceLines.TryGetValue(selection, out uint line))
                    AnnounceEmote(site, spawner, where, line);
            }
            if (all || action == SetMood)
            {
                spawner.Mood = PropSettings.Choice(node, PropSettings.Mood);
                Play(spawner.Mood);
                KeepPose(spawner, spawner.Mood, null);
            }
            if (all || action == SetStance)
            {
                uint stance = PropSettings.Choice(node, PropSettings.Stance);
                Play(stance);
                KeepPose(spawner, null, stance);
            }
            if (all || action == PerformEmote)
                Play(PropSettings.Choice(node, PropSettings.Emote));
            if ((all || action == SayText) && at is System.Numerics.Vector3 position)
                SayNodeText(site, node, spawner.Guid, spawner.Creature, position, ctx);
            log.Info($"{Creatures.StoryName(spawner.Creature)} 0x{spawner.Creature:X} performs story action {action} (node 0x{node:X}).");
        }

        /// <summary>
        /// The spawner's kept creature pose: the mood / stance a Story Node last gave its creature (7 Jun 2016 update notes,
        /// design/patchnotes-after-0589.md: creature moods and stances are kept between sessions and site visits) [P
        /// note; trace/0695/audit-wiki-gaps.md #6]. Saved with the spawner's settings under server keys (no
        /// window row: the spawner's window never shows them; PropSettings.SendSettings sends window rows only), so it
        /// outlives the creature, Edit / Play, the site's players leaving and a server restart; picking the spawner up
        /// clears it (PropSettings.PropRemoved). Each (re)spawn takes it (<see cref="Spawn"/>) and the creature keeps it for
        /// players who see it later (Creatures.StoryPose → SendPose). [I] how live stored it is not known; a duplicate
        /// starts at the defaults (CopySettings copies window rows only).
        /// WorldServer.json StoryPosePersists (Docker STORY_POSE_PERSISTS), on = live since 7 Jun 2016; off = as before
        /// that patch: the pose is played to the players there at the time only.
        /// </summary>
        public const string KeptMoodKey = "server_story_creature_mood", KeptStanceKey = "server_story_creature_stance";

        private static bool PosePersists
            => Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config?.StoryPosePersists ?? true;

        private static void KeepPose(Running spawner, uint? mood, uint? stance)
        {
            if (!PosePersists)
                return;
            Creatures.StoryPose(spawner.Creature, mood, stance);
            if (mood is uint m)
                PropSettings.Set(spawner.Guid, KeptMoodKey, m);
            if (stance is uint s)
                PropSettings.Set(spawner.Guid, KeptStanceKey, s);
        }

        /// <summary>
        /// "Announce Emotes in Chat" (Story Node row 24 <c>npc_emote_spam_enabled</c>, text 576504, checkbox, default 0):
        /// the chat line each mood / stance / emote selection prints, the client's own "[*speaker*] ..." emote texts
        /// (the third-person lines its player emotes use; the line for each word is matched by its wording [I, strong]).
        /// Talk (Stop) 253 has no line. [I] Moods and stances are announced too (the player's /sit, /lie, /angry... have
        /// such lines); live's exact choice is not known.
        /// </summary>
        private static readonly Dictionary<uint, uint> AnnounceLines = new()
        {
            // Moods: Calm, Angry, Happy, Sad.
            [218] = 560178, [219] = 560172, [220] = 560174, [221] = 560175,
            // Stances: Standing, Kneeling, Dancing, Sitting, Lying Down, Drunken, Skipping, Sneaking.
            [223] = 560183, [224] = 560180, [246] = 560192, [351] = 572086, [480] = 571885, [250] = 560186, [248] = 560188,
            [251] = 560190,
            // Emotes.
            [455] = 572119, [228] = 560133, [229] = 559358, [230] = 560138, [232] = 576532, [449] = 572094, [231] = 560143,
            [240] = 560163, [451] = 572115, [233] = 560145, [234] = 560148, [817] = 575936, [237] = 560154, [235] = 560150,
            [244] = 559371, [245] = 560170, [238] = 559373, [222] = 560152, [239] = 560160, [452] = 572116, [456] = 572120,
            [241] = 560165, [252] = 560194, [242] = 560168, [217] = 559127, [453] = 572117, [454] = 572118, [450] = 572114,
        };

        /// <summary>
        /// Prints an announce line to the players near the creature: ChatPacketFromStringId (ChatBase 0x13 sub 4; 0.695
        /// handler FUN_143364980 case 4) fills [*speaker*] from the speaker entity it looks up with FUN_142f85390 — the same
        /// entity table as DisplayPropText (+0x50), which holds a creature only once it is a full entity (L96, bubble tested
        /// 2026-10-17) — so, as for the bubble, the creature speaks to players whose client has it full [I: same lookup, the
        /// chat line itself not tested] and its spawner prop (custom name or prop name) to the rest.
        /// </summary>
        private static void AnnounceEmote(Site site, Running spawner, System.Numerics.Vector3 at, uint line)
        {
            // Players whose client has the creature as a full entity see it named itself (L96, as the bubble).
            var near = PlayerManager.InZone(site.Zone).Where(p => p.RezoneTarget == null
                && System.Numerics.Vector3.Distance(p.Position, at) <= Emotes.ViewRange).ToList();
            var byCreature = near.Where(p => Creatures.IsFullFor(p, spawner.Creature)).ToList();
            if (byCreature.Count > 0)
                Emotes.ShowLine(byCreature, spawner.Creature, line);
            Emotes.ShowLine(near.Except(byCreature), spawner.Guid, line);
        }

        /// <summary>
        /// Shows a Story Node's text by its Text Location (row 25) for its Text Duration (row 37) (TASKS 6.33): 4 bubble,
        /// 5 bubble and chat, 2 on screen, 3 chat. DisplayPropText finds its speaker in the client's entity table
        /// (FUN_142f859b0: +0x50, key at entry +0x2b0), where a creature is only once it is a full entity: tested
        /// 2026-10-17 (L96), a bubble spoken by a promoted kobold shows over its head, while a lightweight creature showed
        /// nothing (2026-10-15). So the creature speaks to players whose client has it full and its spawner
        /// <paramref name="speaker"/> to the rest; chat (3) is the creature's Say line. With no creature the node speaks.
        /// </summary>
        private static void SayNodeText(Site site, ulong node, ulong speaker, ulong creature, System.Numerics.Vector3 at, Context ctx)
        {
            if (Props.Find(node) is not Props.PlacedProp storyNode || string.IsNullOrEmpty(storyNode.Text))
                return;
            uint mode = PropSettings.Choice(node, PropSettings.TextDisplayType);
            uint seconds = (uint)Math.Clamp(PropSettings.Get(node, PropSettings.TextDisplayTime), 1f, 20f);
            ulong question = 0;
            if (mode is 4 or 5 && PropSettings.Get(node, PropSettings.TextIsQuestion) != 0)
                question = AskQuestion(site, node, speaker, seconds);
            if (mode == 3 && creature != 0)
                Chat.NpcSay(creature, Creatures.StoryName(creature), site.Zone, at, storyNode.Text);
            else
                Props.DisplayTextBy(speaker, site.Zone, at, storyNode.Text, mode, seconds, question, creature);
            if (site.Props.TryGetValue(node, out Running nodeRun))
            {
                nodeRun.TextExpiresAt = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
                // "When the text on this node expires..." runs in this chain, with the creature that spoke as the chain's
                // creature (the triggering character is kept as it is; TASKS 2.34).
                nodeRun.TextContext = (creature != 0 ? ctx with { Creature = creature } : ctx) with { StoryNode = node };
            }
        }

        /// <summary>
        /// The IF Gate's test: the chosen property of the tested prop (2.31: count, lights, enabled, owning team, is alive;
        /// "Current Value" of an on / off prop is 1 when on) against the value. Comparisons other than "Equal To" are ours.
        /// The triggering-character checks (P6535-P6538) read the chain's triggering character instead
        /// (<see cref="Context.TriggerPlayer"/> / <see cref="Context.TriggerCreature"/>, fixed where the chain started): the
        /// player who started it, else the creature (an NPC walking into an area triggers as itself, P6537; a player
        /// clicking an NPC triggers as the player, P6538).
        /// </summary>
        private static bool IfHolds(ulong gate, Running target, Context ctx)
        {
            uint property = PropSettings.Choice(gate, PropSettings.IfProperty);
            int value = (int)PropSettings.Get(gate, PropSettings.IfValue);
            int actual = property switch
            {
                PropSettings.IfTriggerTeam => ctx.TriggerPlayer != null ? InstancedGames.TeamNumberOf(ctx.TriggerPlayer) : 0,
                PropSettings.IfTriggerDisposition => ctx.TriggerPlayer != null ? -1 : Creatures.StoryFactionOf(ctx.TriggerCreature),
                PropSettings.IfTriggerIsPlayer => ctx.TriggerPlayer != null ? 1 : 0,
                _ when target == null => int.MinValue,
                PropSettings.IfBlueLight => target.Cog1 ? 1 : 0,
                PropSettings.IfYellowLight => target.Cog2 ? 1 : 0,
                PropSettings.IfEnabled => target.Enabled ? 1 : 0,
                PropSettings.IfOwningTeam => InstancedGames.ControlPointOwner(target.Guid),
                PropSettings.IfIsAlive => target.Creature != 0 ? 1 : 0,
                _ => target.PropId is CountingNodeProp or ScoreboardProp ? target.Count : target.On ? 1 : 0,
            };
            return PropSettings.Choice(gate, PropSettings.IfCompare) switch
            {
                PropSettings.IfNotEqual => actual != value,
                PropSettings.IfLess => actual < value,
                PropSettings.IfGreater => actual > value,
                _ => actual == value,
            };
        }

        /// <summary>A question asked by a Story Node: its site, node, speaker (the creature's spawner), when it stops taking answers, and who answered.</summary>
        private sealed record Question(ulong ClaimId, ulong Node, ulong Speaker, DateTime Until, HashSet<WorldSession> Answered);

        private static readonly Dictionary<ulong, Question> questions = new();
        private static long nextQuestion;

        /// <summary>
        /// A new question id (never 0) for a Story Node's bubble. Answers count for its duration plus a minute (ours), one
        /// per player (plan-storytools-next.md Spec 17).
        /// </summary>
        private static ulong AskQuestion(Site site, ulong node, ulong speaker, uint seconds)
        {
            ulong id = (ulong)Interlocked.Increment(ref nextQuestion);
            DateTime now = DateTime.UtcNow;
            lock (sync)
            {
                foreach (ulong old in questions.Where(q => q.Value.Until < now).Select(q => q.Key).ToList())
                    questions.Remove(old);
                questions[id] = new Question(site.ClaimId, node, speaker, now + TimeSpan.FromSeconds(seconds + 60), new HashSet<WorldSession>());
            }
            log.Info($"Story question {id} from node 0x{node:X} (speaker 0x{speaker:X}).");
            return id;
        }

        /// <summary>
        /// PropPacketDialogResponse: a player clicked Yes / No on a question bubble. While the site is still in Play Mode,
        /// the node's "When a Luminary accepts..." / "...denies..." Links run with the player as the triggering character.
        /// </summary>
        public static void DialogResponse(WorldSession session, ulong id, bool accepted)
        {
            Question q;
            Site site;
            lock (sync)
            {
                if (!questions.TryGetValue(id, out q) || q.Until < DateTime.UtcNow || !q.Answered.Add(session))
                {
                    log.Debug($"{session.CharacterName}: answer to unknown / old question {id} ignored.");
                    return;
                }
                sites.TryGetValue(q.ClaimId, out site);
            }
            if (site == null || !site.Props.TryGetValue(q.Node, out Running node))
                return;
            log.Info($"{session.CharacterName} {(accepted ? "accepted" : "denied")} story question {id}.");
            // The creature that asked (the speaker is its spawner) is the one the Links act on.
            ulong creature = site.Props.TryGetValue(q.Speaker, out Running asker) ? asker.Creature : 0;
            Fire(site, node, accepted ? NodeAccepted : NodeDenied, new Context(session, creature, StoryNode: q.Node), 0);
        }

        private static void SetOn(Site site, Running r, bool on, Context ctx, int depth)
        {
            if (r.On == on)
                return;
            r.On = on;
            ShowOn(site, r, on);
            Fire(site, r, r.Kind.Events[on ? 0 : 1], ctx, depth);
            Fire(site, r, r.Kind.Events[2], ctx, depth);
        }

        /// <summary>Lights, fires and glows show it (their composite effect on or off, as PropBase SetPropEnable does).</summary>
        private static void ShowOn(Site site, Running r, bool on)
        {
            // Doors, gates, portcullises, shutters, levers and fans move (SimpleAnimation sets); lights glow.
            // Only to players streaming the prop (Props.Stream.cs; others get the look when it is streamed to them,
            // SendStateTo) and not in the middle of a re-zone (anything with it can hang their client).
            if (PropAnimations.SetFor(r.PropId) != 0)
            {
                foreach (WorldSession p in Props.ViewersOf(r.Guid))
                    if (p.RezoneTarget == null)
                        PropAnimations.Send(p, r.PropId, r.Guid, on ? PropAnimations.Open : PropAnimations.Close);
                return;
            }
            if (!PropEffects.HasEffect(r.PropId))
                return;
            foreach (WorldSession p in Props.ViewersOf(r.Guid))
                if (p.RezoneTarget == null)
                    PropEffects.Send(p, r.PropId, r.Guid, on);
        }

        private static void SetCogs(Site site, Running r, bool cog1, bool cog2, Context ctx, int depth)
        {
            bool was1 = r.Cog1, was2 = r.Cog2;
            (r.Cog1, r.Cog2) = (cog1, cog2);
            if (cog1 != was1)
                QueueState(site, r, LogicGateA, cog1 ? 1u : 0u);
            if (cog2 != was2)
                QueueState(site, r, LogicGateB, cog2 ? 1u : 0u);
            if (cog1 != was1)
                Fire(site, r, cog1 ? Cog1Starts : Cog1Stops, ctx, depth);
            if (cog2 != was2)
                Fire(site, r, cog2 ? Cog2Starts : Cog2Stops, ctx, depth);
            if ((cog1 && !was1) || (cog2 && !was2))
                Fire(site, r, EitherStarts, ctx, depth);
            if ((!cog1 && was1) || (!cog2 && was2))
                Fire(site, r, EitherStops, ctx, depth);
            if (cog1 && cog2 && !(was1 && was2))
                Fire(site, r, BothStarted, ctx, depth);
            if (!cog1 && !cog2 && (was1 || was2))
                Fire(site, r, BothStopped, ctx, depth);
        }

        private static void Spawn(Site site, Running r, int depth)
        {
            r.RespawnAt = null;
            // The creature's kind comes from the prop; its shown name is the spawner's own name when the builder gave it one
            // (TASKS 6.30, P4227 "creature name"; the creature itself can't be renamed: it is the spawner that is).
            // "Spawn With Weapons Equipped" (row 40, P6511): every (re)spawn starts as the row says; Links change it later.
            r.Creature = Creatures.SpawnStory(site.Zone, r.Position, r.Name, r.PropId, r.CustomName,
                PropSettings.Get(r.Guid, PropSettings.HealthPercent), PropSettings.Get(r.Guid, PropSettings.DamagePercent),
                PropSettings.Choice(r.Guid, PropSettings.SpawnWeapons) == Creatures.AnimNpcWeaponsEquipped, site.ClaimId);
            // Its Damages Terrain (row 105): whether a fungus / mine leaves a crater (VoxelDestruction.CreatureBlast).
            if (r.Creature != 0)
                Creatures.StoryDamagesTerrain(r.Creature, PropSettings.Get(r.Guid, PropSettings.DamagesTerrain) != 0);
            // The spawner's Reaction (row 17): how the creature starts out; Links can change it later.
            uint reaction = PropSettings.Choice(r.Guid, PropSettings.SpawnerReaction);
            if (r.Creature != 0 && reaction != 0)
                Creatures.StoryFaction(r.Creature, (int)reaction);
            // Its Combat Tactics (row 31).
            uint tactics = PropSettings.Choice(r.Guid, PropSettings.SpawnerAi);
            if (r.Creature != 0 && tactics != 0)
                Creatures.StoryTactics(r.Creature, (int)tactics);
            // The mood / stance its Story Nodes last gave it (KeepPose): played to whoever sees it now, kept for later viewers.
            if (r.Creature != 0 && PosePersists)
            {
                uint? mood = PropSettings.TryGetSaved(r.Guid, KeptMoodKey, out float m) ? (uint)m : null;
                uint? stance = PropSettings.TryGetSaved(r.Guid, KeptStanceKey, out float s) ? (uint)s : null;
                if (mood != null || stance != null)
                {
                    r.Mood = mood ?? r.Mood;
                    Creatures.StoryPose(r.Creature, mood, stance, play: true);
                }
            }
            Fire(site, r, CreatureSpawns, new Context(null, r.Creature), depth);
        }

        /// <summary>Players on their way to a build site: the site, whether they respawn, and the Respawn Point (2701) they
        /// respawn at (0: the site's Starting Location).</summary>
        private static readonly Dictionary<WorldSession, (ulong ClaimId, bool Respawn, ulong Point)> arriving = new();

        /// <summary>A player left the world.</summary>
        public static void Forget(WorldSession session)
        {
            lock (sync)
                arriving.Remove(session);
        }

        /// <summary>A player is being moved to a build site (travel or respawn there); its Starting Location (or the
        /// Respawn Point <paramref name="point"/> they respawn at) hears of it once they have arrived
        /// (<see cref="PlayerArrived"/>).</summary>
        public static void ExpectArrival(WorldSession session, ulong claimId, bool respawn, ulong point = 0)
        {
            lock (sync)
                arriving[session] = (claimId, respawn, point);
        }

        /// <summary>Name of a Respawn Point 2701 as a respawn site (text 577587 "Respawn Point"; Health logs it).</summary>
        private static string RespawnPointName
            => Shared.GameTable.Text.TextManager.Instance.GetTextForId(577587) is string s && s.Length > 0 ? s : "Respawn Point";

        /// <summary>
        /// Where the death window's Flee takes a player who died at <paramref name="from"/> (Health.HandleRespawn, and the
        /// spot it shows in RespawnLocations): <see cref="Graveyards.Nearest"/>, except that while the site they died on
        /// is in Play Mode its <b>enabled</b> Respawn Points (2701) are site entries too and the nearest of them all wins
        /// (P6177; help page ScriptObj_RespawnLoc_FTE: Luminaries who flee are sent to the closest active point; the
        /// Starting Location can't be turned off and stays one of them). In Edit Mode, or off a site, nothing changes:
        /// 2701 points are not respawn sites then (plan-storytools-next.md Spec 8).
        ///
        /// With <paramref name="respawning"/> (the player is really being sent there) and a Respawn Point chosen, the
        /// point's "When a Luminary respawns at this Build Site..." (574557) is armed for their arrival — that point only
        /// (wiki: only at the location used). A Starting Location chosen keeps Health's own arrival call (Name "Build
        /// Site"), so its 574557 runs as before.
        ///
        /// Ours (inferred): the player stands 0.5 m above the point (as Graveyards does for Starting Locations). Facing
        /// the prop's rotation (wiki) is not done: Teleport.ToZone has no heading.
        /// </summary>
        public static Graveyards.Site RespawnSite(WorldSession session, System.Numerics.Vector3 from, bool respawning)
        {
            Graveyards.Site nearest = Graveyards.Nearest(session, from);
            ulong diedOn = Claims.ClaimAt(session.ZoneId, from) ?? 0;
            Site site = null;
            if (diedOn != 0)
                lock (sync)
                    site = sites.GetValueOrDefault(diedOn);
            if (site == null || site.Zone != session.ZoneId)
                return nearest;
            Running point = site.Props.Values.Where(r => r.PropId == RespawnPointProp && r.Enabled)
                .OrderBy(r => System.Numerics.Vector3.Distance(r.Position, from)).FirstOrDefault();
            if (point == null)
                return nearest;
            // A site entry Graveyards found (Starting Location, or a graveyard / 1225 point on this site) that is nearer wins.
            if (nearest != null && nearest.ClaimId == diedOn && nearest.Zone == session.ZoneId
                && System.Numerics.Vector3.Distance(nearest.Position, from) < System.Numerics.Vector3.Distance(point.Position, from))
                return nearest;
            if (respawning)
                ExpectArrival(session, diedOn, respawn: true, point: point.Guid);
            return new Graveyards.Site(point.Guid, RespawnPointName, point.Position + new System.Numerics.Vector3(0f, 0.5f, 0f),
                site.Zone, diedOn);
        }

        /// <summary>
        /// A re-zone finished (Teleport.FinishRezone). If it brought the player to a site in Play Mode, the site's Starting
        /// Location runs "When a Luminary teleports to / respawns at this Build Site..." — or, when they respawned at a
        /// Respawn Point, that point alone runs its "respawns" Links (not earlier: anything sent with the re-zone can hang
        /// the client).
        /// </summary>
        public static void PlayerArrived(WorldSession session)
        {
            (ulong ClaimId, bool Respawn, ulong Point) a;
            Site site;
            lock (sync)
            {
                if (!arriving.Remove(session, out a))
                    return;
                site = sites.GetValueOrDefault(a.ClaimId);
            }
            if (site == null)
                return;
            if (a.Point != 0)
            {
                if (site.Props.TryGetValue(a.Point, out Running point))
                    Fire(site, point, LuminaryRespawnsHere, new Context(session, 0), 0);
                log.Info($"{session.CharacterName} respawned at respawn point 0x{a.Point:X} on site {a.ClaimId} (Play Mode).");
                return;
            }
            foreach (Running start in site.Props.Values.Where(r => r.PropId == StartingLocationProp).ToList())
                Fire(site, start, a.Respawn ? LuminaryRespawnsHere : LuminaryTeleportsHere, new Context(session, 0), 0);
            log.Info($"{session.CharacterName} {(a.Respawn ? "respawned" : "arrived")} at site {a.ClaimId} (Play Mode).");
        }

        /// <summary>A story creature reached the Travel Point it was sent to: the point's "a creature arrives" Links run.</summary>
        public static void CreatureArrived(ulong creature, ulong travelPoint)
        {
            Site site = SiteOf(travelPoint);
            if (site != null && site.Props.TryGetValue(travelPoint, out Running point))
            {
                log.Info($"Story creature 0x{creature:X} arrived at travel point 0x{travelPoint:X}.");
                Fire(site, point, CreatureArrives, new Context(null, creature), 0);
            }
        }

        /// <summary>A story creature started (or stopped) fighting: its spawner's combat Links run.</summary>
        public static void CreatureCombat(ulong creature, bool entered)
        {
            Site site;
            Running spawner;
            lock (sync)
            {
                site = sites.Values.FirstOrDefault(s => s.Props.Values.Any(r => creature != 0 && r.Creature == creature));
                spawner = site?.Props.Values.First(r => creature != 0 && r.Creature == creature);
            }
            if (spawner != null)
                Fire(site, spawner, entered ? CreatureEntersCombat : CreatureExitsCombat, new Context(null, creature), 0);
        }

        /// <summary>A story creature died (Creatures.Kill): its spawner's "dies" Links run, and it comes back later.</summary>
        public static void CreatureDied(ulong creature, WorldSession killer)
        {
            Site site;
            Running spawner;
            lock (sync)
            {
                site = sites.Values.FirstOrDefault(s => s.Props.Values.Any(r => creature != 0 && r.Creature == creature));
                spawner = site?.Props.Values.First(r => creature != 0 && r.Creature == creature);
            }
            if (spawner == null)
                return;
            spawner.Creature = 0;
            spawner.RespawnAt = PropSettings.Get(spawner.Guid, PropSettings.RespawnEnabled) != 0
                ? DateTime.UtcNow + TimeSpan.FromSeconds(PropSettings.Get(spawner.Guid, PropSettings.SpawnDelay))
                : null;
            Fire(site, spawner, CreatureDies, new Context(killer, creature), 0);
        }

        private static List<WorldSession> PlayersOn(Site site)
            => PlayerManager.InZone(site.Zone).Where(p => Claims.ClaimAt(p.ZoneId, p.Position) == site.ClaimId).ToList();

        /// <summary>
        /// The owner (or an Ultimate Power builder) switched a site from Edit to Play Mode (Claims.SetAdventureStatus):
        /// "The story on this Build Site has been saved!" (577105) to the players on the site, the same way the Reset
        /// Node's "…has been reset!" (577089) is sent (ChatPacketFromStringId through Emotes.ShowLine, speaker 0).
        /// [P text / key]: CodeStringMappings rows 102 / 103 pair Claim.StoryReset.Message^577089 with
        /// Claim.StorySaveSnapshot.Message^577105, both texts in text_589 and text_695; P5276 (11 May 2016): "When you
        /// enter Play Mode, the current state of all props on your Build Site will be saved." [I trigger, I carrier]: the
        /// client prints neither text itself (the Claims snapshot packets 0xF9 / 0x13-0x16 are terrain snapshots whose
        /// handlers print neither), so the server sends it on Edit → Play; not on a server start (RestoreModes) or an
        /// Adventure round's restart. trace/0695/agent-leads3-c.md L22.
        /// </summary>
        public static void AnnounceSaved(ClaimModel claim)
        {
            List<WorldSession> players = PlayerManager.InZone(claim.ZoneId)
                .Where(p => Claims.ClaimAt(p.ZoneId, p.Position) == claim.Id).ToList();
            if (players.Count > 0)
                Emotes.ShowLine(players, 0, StorySavedText);
        }

        /// <summary>Switches (and levers) can be clicked; only in Play Mode do they do anything.</summary>
        public static bool IsClickable(uint propId) => KindOf(propId) is Kind k && k.Events.Contains(ClickedEvent);

        /// <summary>
        /// Any other prop is usable (the interact cursor, InteractRequest) only while its site is in Play Mode and it has a
        /// click Link: so fences and decorations don't all show the cursor. Ours (inferred; plan-storytools-next.md Spec 12).
        /// </summary>
        public static bool IsClickLinked(ulong guid)
        {
            lock (sync)
                return clickLinked.Contains(guid);
        }

        /// <summary>The click-linked props of the sites in Play Mode (asked for every prop streamed to a player).</summary>
        private static readonly HashSet<ulong> clickLinked = new();

        /// <summary>Flags a site's click-linked props as interactable for the players in the zone (Play start, new Link).</summary>
        private static void FlagClickable(Site site, ulong only = 0)
        {
            var flagging = new DynamicInteractionFlagging();
            foreach (Running r in site.Props.Values)
                if (r.Kind == clickOnlyKind && (only == 0 || r.Guid == only))
                    flagging.Guids.Add(r.Guid);
            lock (sync)
                clickLinked.UnionWith(flagging.Guids);
            if (flagging.Guids.Count == 0)
                return;
            // Each player gets the props streamed to them (Props.Stream.cs); a prop streamed later is flagged with its
            // spawn (Props.SendSpawns: IsClickLinked).
            foreach (WorldSession viewer in PlayerManager.InZone(site.Zone))
            {
                var theirs = new DynamicInteractionFlagging();
                theirs.Guids.AddRange(flagging.Guids.Where(g => Props.Streams(viewer, g)));
                if (theirs.Guids.Count > 0)
                    viewer.EnqueueMessage(theirs);
            }
        }

        /// <summary>
        /// Whether a prop is locked by a Lock Link in its running site (Play Mode only; false otherwise). Reward chests
        /// refuse clicks while locked (<see cref="RewardChests.Click"/>).
        /// </summary>
        public static bool IsLocked(ulong guid)
        {
            Site site = SiteOf(guid);
            return site != null && site.Props.TryGetValue(guid, out Running r) && r.Locked;
        }

        /// <summary>A clicked reward chest's own "When a Luminary clicks this object..." Links (P6432), when its site runs.</summary>
        public static void FireClickLinks(WorldSession session, ulong guid)
        {
            Site site = SiteOf(guid);
            if (site != null && site.Props.TryGetValue(guid, out Running r))
                Fire(site, r, ClickedEvent, new Context(session, 0), 0);
        }

        /// <summary>A reward chest was looted by a click: its "When this chest is looted..." Links run, the clicker triggering.</summary>
        public static void ChestLooted(WorldSession session, ulong guid)
        {
            Site site = SiteOf(guid);
            if (site != null && site.Props.TryGetValue(guid, out Running r))
                Fire(site, r, RewardChests.LootedEvent, new Context(session, 0), 0);
        }

        /// <summary>
        /// A player clicked a prop (Command InteractRequest). Its click Links run; a switch also flips (its on / off
        /// events). A reward chest is looted (<see cref="RewardChests.Click"/>; also reached so from dev "storyclick").
        /// </summary>
        public static bool Click(WorldSession session, ulong guid)
        {
            if (RewardChests.IsRewardChest(guid))
                return RewardChests.Click(session, guid);
            Props.PlacedProp placed = Props.Find(guid);
            if (placed == null || !(IsClickable(placed.PropId) || HasClickLink(guid)))
                return false;
            Site site = SiteOf(guid);
            if (site == null || !site.Props.TryGetValue(guid, out Running r))
            {
                Chat.System(session, "Nothing happens. (The build site is not in Play Mode.)");
                return true;
            }
            var ctx = new Context(session, 0);
            Fire(site, r, ClickedEvent, ctx, 0);
            if (r.Kind.Toggle)
                SetOn(site, r, !r.On, ctx, 0);
            log.Info($"{session.CharacterName} clicked {r.Kind.Name} 0x{guid:X} ({(r.On ? "on" : "off")}).");
            return true;
        }

        /// <summary>
        /// A player clicked a story creature: its spawner's "When a Luminary clicks this creature..." Links run, with the
        /// player as the one who set them off (Focus Nodes, Travel Points and the like act on them / the creature).
        /// </summary>
        public static bool ClickCreature(WorldSession session, ulong creature)
        {
            if (creature == 0)
                return false;
            Site site;
            Running spawner;
            lock (sync)
            {
                site = sites.Values.FirstOrDefault(s => s.Props.Values.Any(r => creature != 0 && r.Creature == creature));
                spawner = site?.Props.Values.First(r => creature != 0 && r.Creature == creature);
            }
            if (spawner == null)
                return false;
            // Enemies are too busy fighting to be interacted with (P5305).
            if (!Creatures.IsInteractable(creature))
                return true;
            Fire(site, spawner, CreatureClicked, new Context(session, creature), 0);
            log.Info($"{session.CharacterName} clicked story creature 0x{creature:X} ({spawner.Kind.Name}).");
            return true;
        }

        /// <summary>Living story creatures on a site in Play Mode (0 when it isn't).</summary>
        public static int LiveCreaturesOn(ulong claimId)
        {
            lock (sync)
                return sites.TryGetValue(claimId, out Site site) ? site.Props.Values.Count(r => r.Creature != 0) : 0;
        }

        /// <summary>Whether a story creature's spawner has a "When a Luminary clicks this creature..." Link.</summary>
        public static bool CreatureHasClickLink(ulong creature)
        {
            ulong spawner;
            lock (sync)
                spawner = sites.Values.SelectMany(s => s.Props.Values).FirstOrDefault(r => creature != 0 && r.Creature == creature)?.Guid ?? 0;
            return spawner != 0 && LinksFrom(spawner, CreatureClicked).Count > 0;
        }

        /// <summary>Server tick: timers, spawner respawns, Linking Areas, pending resets.</summary>
        public static void Update()
        {
            FlushStates();
            List<Site> all;
            lock (sync)
                all = sites.Values.ToList();
            DateTime now = DateTime.UtcNow;
            int hour = GameHour();
            foreach (Site site in all)
            {
                // Server Time Nodes: once per change of the game hour (one fire per hour change, not per tick).
                if (site.LastHour != hour)
                {
                    site.LastHour = hour;
                    foreach (Running r in site.Props.Values.Where(r => r.Kind == serverTimeKind).ToList())
                        ServerTimeReached(site, r, hour);
                }
                List<WorldSession> players = null;
                foreach (Running r in site.Props.Values.ToList())
                {
                    if (r.TimerRunning && now >= r.TimerEnds)
                    {
                        r.TimerLeft = TimerLength(r);
                        r.TimerRunning = PropSettings.Get(r.Guid, PropSettings.TimerAutoRestart) != 0;
                        if (r.TimerRunning)
                            r.TimerEnds = now + r.TimerLeft;
                        // In the chain that started it (its triggering character; TASKS 2.34).
                        Fire(site, r, TimerCompletes, r.TimerContext ?? new Context(null, 0), 0);
                    }
                    if (r.TextExpiresAt is DateTime expires && now >= expires)
                    {
                        r.TextExpiresAt = null;
                        // In the chain that showed the text: the creature that spoke is still the chain's creature.
                        Fire(site, r, NodeTextExpires, r.TextContext ?? new Context(null, 0), 0);
                        r.TextContext = null;
                    }
                    if (r.RespawnAt is DateTime at && now >= at && r.Creature == 0)
                        Spawn(site, r, 0);
                    if (r.PropId == LinkingAreaProp)
                    {
                        players ??= PlayersOn(site);
                        var inside = players.Where(p => InArea(r, p.Position)).ToHashSet();
                        foreach (WorldSession p in inside.Except(r.Inside).ToList())
                            Fire(site, r, LuminaryEnters, new Context(p, 0), 0);
                        foreach (WorldSession p in r.Inside.Except(inside).ToList())
                            Fire(site, r, LuminaryExits, new Context(p, 0), 0);
                        r.Inside.Clear();
                        r.Inside.UnionWith(inside);
                        // The site's story creatures: "When a creature enters / leaves this area..." (the creature is
                        // the one the Links act on, e.g. a Travel Point's "walk here").
                        var creatures = site.Props.Values.Where(s => s.Creature != 0)
                            .Select(s => (s.Creature, At: Creatures.StoryPosition(s.Creature)))
                            .Where(c => c.At is System.Numerics.Vector3 pos && InArea(r, pos))
                            .Select(c => c.Creature).ToHashSet();
                        foreach (ulong c in creatures.Except(r.CreaturesInside).ToList())
                            Fire(site, r, CreatureEnters, new Context(null, c), 0);
                        foreach (ulong c in r.CreaturesInside.Except(creatures).ToList())
                            Fire(site, r, CreatureLeaves, new Context(null, c), 0);
                        r.CreaturesInside.Clear();
                        r.CreaturesInside.UnionWith(creatures);
                    }
                }
            }
            List<ulong> resets;
            lock (sync)
            {
                resets = pendingResets.ToList();
                pendingResets.Clear();
            }
            foreach (ulong claimId in resets)
                if (Claims.Get(claimId) is ClaimModel claim)
                {
                    // A silent reset (TASKS 2.32 / 2.4): every story prop goes back to its Play-start state, which is the
                    // built state (lights as built, doors shut, Logic Filters both off, counters at their Starting Count,
                    // timers stopped, Respawn Points as their box says), shown without raising any event; story creatures
                    // despawn without loot and the "Spawn Automatically" spawners spawn again (their spawn Links run, as
                    // at Play start). Players already inside a Linking Area stay inside (no new "enters").
                    Dictionary<ulong, WorldSession[]> inside;
                    lock (sync)
                        inside = sites.TryGetValue(claimId, out Site old)
                            ? old.Props.Values.Where(r => r.Inside.Count > 0).ToDictionary(r => r.Guid, r => r.Inside.ToArray())
                            : new();
                    PlayModeChanged(claim, false);
                    PlayModeChanged(claim, true);
                    // "The story on this Build Site has been reset!" (577089) to the players on the site: the client has no
                    // key for it anywhere (Claim.StoryReset.Message is absent from the exe and UI), so the server sends the
                    // text id as a ChatPacketFromStringId line (trace/0695/agent-leads2-b.md L22) [P text; I carrier].
                    Site resetSite;
                    lock (sync)
                        sites.TryGetValue(claimId, out resetSite);
                    if (resetSite != null)
                        Emotes.ShowLine(PlayersOn(resetSite), 0, StoryResetText);
                    lock (sync)
                        if (sites.TryGetValue(claimId, out Site fresh))
                            foreach ((ulong guid, WorldSession[] players) in inside)
                                if (fresh.Props.TryGetValue(guid, out Running area))
                                    area.Inside.UnionWith(players);
                    log.Info($"Site {claimId}: story reset.");
                }
        }

        /// <summary>
        /// The game hour became <paramref name="hour"/>: each of the Server Time Node's listeners (1 .. Amount of
        /// Listeners, row 101) whose "Desired Time N" is this hour fires its slot — 577252 for slot 1, 577255-577259 for
        /// slots 2-6 (storytools-695.md §4.7; plan-storytools-next.md Spec 9). Game time, not the site's time (P6165).
        /// Nobody set it off: no triggering player or creature. Settings are read now, so a change in Play Mode counts
        /// from the next hour. Ours (inferred): a clock jump (dev "timeofday") past an hour skips it; the hour jumped to
        /// fires.
        /// </summary>
        private static void ServerTimeReached(Site site, Running r, int hour)
        {
            int listeners = PropSettings.ServerTimeListeners(r.Guid);
            for (int slot = 1; slot <= listeners; slot++)
            {
                if ((int)PropSettings.Get(r.Guid, PropSettings.DesiredTime(slot)) != hour)
                    continue;
                log.Info($"Server time node 0x{r.Guid:X}: server time slot {slot} ({hour}).");
                Fire(site, r, serverTimeKind.Events[slot - 1], new Context(null, 0), 0);
            }
        }

        /// <summary>Dev "storyfire guid event": raises a prop's event as if it happened.</summary>
        public static void DevFire(WorldSession session, ulong guid, uint eventId)
        {
            Site site = SiteOf(guid);
            if (site != null && site.Props.TryGetValue(guid, out Running r))
                Fire(site, r, eventId, new Context(session, 0), 0);
            else
                Chat.System(session, "That prop isn't running (its site is not in Play Mode).");
        }

        /// <summary>Dev "story": the running state of the props on the player's site.</summary>
        public static string Describe(WorldSession session)
        {
            ulong? claim = Claims.ClaimAt(session.ZoneId, session.Position);
            Site site = null;
            lock (sync)
                if (claim is ulong c)
                    sites.TryGetValue(c, out site);
            if (site == null)
                return $"Site {claim?.ToString() ?? "none"} is not in Play Mode.";
            return string.Join("; ", site.Props.Values.Select(r => $"{r.Kind.Name} {r.Guid:X} on={r.On} locked={r.Locked} "
                + $"cogs={r.Cog1}/{r.Cog2} timer={(r.TimerRunning ? (r.TimerEnds - DateTime.UtcNow).TotalSeconds.ToString("F0") + "s" : "stopped")} creature={r.Creature:X}"
                + (r.PropId == RespawnPointProp ? $" enabled={r.Enabled}" : "")));
        }

        #endregion

        #region packets

        [ZoneMessageHandler(ZoneMessageOpcode.GameScriptingBase)]
        public static void Handle(WorldSession session, GameScriptingRequest request)
        {
            EnsureLoaded();
            var reader = new GamePacketReader(request.Body);
            // Sub 0x0F (SupportedActionsEventsAndDefaultsRequest) is what the prop menu's Script button sends
            // (Prop.SetupScript FUN_1435fafc0 → FUN_142f489e0): the menu closed, its name label goes (Props.MenuName.cs).
            if (request.SubOpcode == 0x0F)
                Props.HideMenuName(session);
            // Subs that change stored Links count against the player's storage limit; refused ones answer "failed".
            bool writes = request.SubOpcode is 0x10 or 0x12 or 0x13 or 0x14 or 0x16;
            bool allowed = !writes || session.StorageLimiter.TryTake();
            if (!allowed)
                log.Warn($"{session.CharacterName}: Link change (sub 0x{request.SubOpcode:X}) refused: too many too fast.");
            switch (request.SubOpcode)
            {
                case 0x0C:
                    SendSupportedObjects(session);
                    break;
                case 0x0D:
                {
                    ulong prop = reader.ReadULong();
                    Kind kind = KindOfProp(prop);
                    Send(session, SubEvents, w => { w.Write(prop); w.Write(kind != null ? ResultOk : ResultFailed); WriteDefinitions(w, EventsOf(prop)); });
                    break;
                }
                case 0x0E:
                {
                    ulong prop = reader.ReadULong();
                    Kind kind = KindOfProp(prop);
                    Send(session, SubActions, w => { w.Write(prop); w.Write(kind != null ? ResultOk : ResultFailed); WriteDefinitions(w, ActionsOf(prop)); });
                    break;
                }
                case 0x0F:
                {
                    ulong prop = reader.ReadULong();
                    Kind kind = KindOfProp(prop);
                    Send(session, SubActionsEventsAndDefaults, w =>
                    {
                        w.Write(prop);
                        w.Write(ResultOk);
                        WriteDefinitions(w, ActionsOf(prop));
                        WriteDefinitions(w, EventsOf(prop));
                        w.Write(0u);                     // default links
                    });
                    log.Info($"{session.CharacterName} opened Links on prop 0x{prop:X} ({kind?.Name ?? "nothing to link"}).");
                    break;
                }
                case 0x10:
                {
                    ulong source = reader.ReadULong(), target = reader.ReadULong();
                    uint eventId = reader.ReadUInt(), actionId = reader.ReadUInt();
                    uint result = allowed ? Add(session, source, eventId, target, actionId) : ResultFailed;
                    Send(session, SubAddBinding, w => { w.Write(source); w.Write(target); w.Write(eventId); w.Write(actionId); w.Write(result); });
                    break;
                }
                case 0x11:
                {
                    ulong prop = reader.ReadULong();
                    SendLinks(session, prop);
                    break;
                }
                case 0x12:
                {
                    ulong id = reader.ReadULong();
                    WorldPropBindingModel link;
                    lock (sync)
                        link = bindings.FirstOrDefault(b => b.Id == id);
                    bool ok = allowed && link != null && Props.CanEdit(session, link.SourceId);
                    if (ok)
                        Delete(new List<WorldPropBindingModel> { link });
                    Send(session, SubRemoveBinding, w => { w.Write(id); w.Write(ok ? ResultOk : ResultFailed); });
                    log.Info($"{session.CharacterName} removed link {id}: {(ok ? "ok" : "refused")}.");
                    break;
                }
                case 0x13:
                case 0x14:
                {
                    // Remove all / restore defaults (no default links here, so both clear the prop's Links).
                    ulong prop = reader.ReadULong();
                    bool ok = allowed && Props.CanEdit(session, prop);
                    if (ok)
                    {
                        List<WorldPropBindingModel> gone;
                        lock (sync)
                            gone = bindings.Where(b => b.SourceId == prop).ToList();
                        Delete(gone);
                    }
                    Send(session, request.SubOpcode == 0x13 ? SubRemoveAllBindings : SubRestoreDefaults,
                        w => { w.Write(prop); w.Write(ok ? ResultOk : ResultFailed); });
                    break;
                }
                case 0x15:
                {
                    // PotentialLink: may this prop be linked (as a target)? Yes when it has actions and the player may edit it.
                    ulong prop = reader.ReadULong();
                    bool ok = KindOfProp(prop) != null && Props.CanEdit(session, prop);
                    Send(session, SubPotentialLink, w => { w.Write(prop); w.Write(ok ? ResultOk : ResultFailed); });
                    break;
                }
                case 0x16:
                {
                    ulong id = reader.ReadULong(), source = reader.ReadULong(), target = reader.ReadULong();
                    uint eventId = reader.ReadUInt(), actionId = reader.ReadUInt();
                    WorldPropBindingModel old;
                    lock (sync)
                        old = bindings.FirstOrDefault(b => b.Id == id);
                    // Until 2026-10-26 the old Link was deleted first and stayed deleted when the new one was refused,
                    // while the client (FUN_142fef9b0, result ≠ 0) kept showing it (trace/0695/rebase-695.md G1).
                    uint result;
                    if (!allowed)
                        result = ResultFailed;
                    else if (old == null || !Props.CanEdit(session, old.SourceId))
                        result = RemoveAddRemoveFailed;
                    else
                        result = ReplaceLink(bindings, sync, old, () => Add(session, source, eventId, target, actionId),
                            gone => Save(c => c.WorldPropBinding.RemoveRange(c.WorldPropBinding.Where(b => b.Id == gone.Id))));
                    Send(session, SubRemoveAddBinding, w =>
                    {
                        w.Write(id); w.Write(source); w.Write(target); w.Write(eventId); w.Write(actionId); w.Write(result);
                    });
                    break;
                }
                default:
                    log.Info($"{session.CharacterName}: object scripting sub 0x{request.SubOpcode:X}: {BitConverter.ToString(request.Body)}");
                    break;
            }
        }

        /// <summary>Stores a Link when both props can be edited by the player and support the event and action.</summary>
        /// <summary>Adds a Link; returns the AddBinding result code (<see cref="ResultOk"/> ...).</summary>
        public static uint Add(WorldSession session, ulong source, uint eventId, ulong target, uint actionId)
        {
            // Loaded first: a Link added before the first load (the dev storylink path) was read back from the database
            // as a second copy, and the id counter started from 0.
            EnsureLoaded();
            Kind from = SourceKindOf(source), to = KindOfProp(target);
            if (from == null || to == null || !EventsOf(source).Contains(eventId) || !(ActionsOf(target)?.Contains(actionId) ?? false)
                || !Props.CanEdit(session, source) || !Props.CanEdit(session, target))
            {
                log.Info($"{session.CharacterName}: link 0x{source:X} {eventId} → 0x{target:X} {actionId} refused.");
                return ResultFailed;
            }
            // Caps (a client could add Links without end): per prop and per site.
            Props.PlacedProp placed = Props.Find(source);
            ulong? site = placed == null ? null : Claims.ClaimAt(placed.Zone, new System.Numerics.Vector3(placed.Spawn.Position.X, placed.Spawn.Position.Y, placed.Spawn.Position.Z));
            int fromProp, onSite = 0;
            lock (sync)
            {
                fromProp = bindings.Count(b => b.SourceId == source);
                if (site != null)
                    foreach (WorldPropBindingModel b in bindings)
                        if (Props.Find(b.SourceId) is Props.PlacedProp p
                            && Claims.ClaimAt(p.Zone, new System.Numerics.Vector3(p.Spawn.Position.X, p.Spawn.Position.Y, p.Spawn.Position.Z)) == site)
                            onSite++;
                if (bindings.Any(b => b.SourceId == source && b.EventId == eventId && b.TargetId == target && b.ActionId == actionId))
                    return ResultDuplicate;
                if (singleBindingEvents.Contains(eventId) && bindings.Any(b => b.SourceId == source && b.EventId == eventId))
                    return ResultSingleBinding;
            }
            if (fromProp >= MaxLinksPerProp || onSite >= MaxLinksPerSite)
            {
                Chat.System(session, fromProp >= MaxLinksPerProp ? $"A prop can have at most {MaxLinksPerProp} links."
                    : $"A build site can have at most {MaxLinksPerSite} links.");
                return ResultFailed;
            }
            var link = new WorldPropBindingModel
            {
                Id = (ulong)Interlocked.Increment(ref nextBindingId),
                SourceId = source,
                EventId = eventId,
                TargetId = target,
                ActionId = actionId
            };
            lock (sync)
                bindings.Add(link);
            Save(c => c.WorldPropBinding.Add(new WorldPropBindingModel
            {
                Id = link.Id, SourceId = source, EventId = eventId, TargetId = target, ActionId = actionId
            }));
            log.Info($"{session.CharacterName} linked {from.Name} 0x{source:X} ({eventId}) → {to.Name} 0x{target:X} ({actionId}).");
            Achievements.Linked(session, Props.Find(source)?.PropId ?? 0, Props.Find(target)?.PropId ?? 0);   // linking goals 960-963
            return ResultOk;
        }

        /// <summary>Events / action groups: {u32 id, u32 text id, u32 sort order}; ids are the text ids.</summary>
        private static void WriteDefinitions(GamePacketWriter w, uint[] ids)
        {
            ids ??= Array.Empty<uint>();
            w.Write((uint)ids.Length);
            for (int i = 0; i < ids.Length; i++)
            {
                w.Write(ids[i]);
                w.Write(ids[i]);
                w.Write((uint)i);
            }
        }

        /// <summary>The props the player may link: linkable props on build sites they can edit, on their landscape.</summary>
        private static void SendSupportedObjects(WorldSession session)
        {
            // Every prop can be a Link's source since P6432 (its click event).
            var guids = Props.PlacedAll(null).Where(p => p.Zone == session.ZoneId && Props.CanEdit(session, p.Guid))
                .Select(p => p.Guid).ToList();
            Send(session, SubObjects, w =>
            {
                w.Write(ResultOk);
                w.Write((uint)guids.Count);
                foreach (ulong guid in guids)
                    w.Write(guid);
            });
        }

        private static void SendLinks(WorldSession session, ulong prop)
        {
            List<WorldPropBindingModel> links;
            lock (sync)
                links = bindings.Where(b => b.SourceId == prop || b.TargetId == prop).ToList();
            Send(session, SubListBindings, w =>
            {
                w.Write(prop);
                w.Write(ResultOk);
                w.Write((uint)links.Count);
                foreach (WorldPropBindingModel link in links)
                {
                    w.Write(link.Id);
                    w.Write(link.SourceId);
                    w.Write(link.EventId);
                    w.Write(link.TargetId);
                    w.Write(link.ActionId);
                    w.Write(0u);
                    w.Write(0u);
                    // Two fixed arrays of 4 × u32 (binding reader FUN_142ea38d0: 72 bytes a link), not two list
                    // counts. With 2 × u32 here every list holding a link ran the client's reader past the end and
                    // the whole list was dropped (s2cprobe, 2026-10-12). The client's add request carries no values
                    // for them.
                    for (int i = 0; i < 8; i++)
                        w.Write(0u);
                }
            });
        }

        private static void Send(WorldSession session, byte sub, Action<GamePacketWriter> body)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(sub);
            body(w);
            session.EnqueueMessage(ZoneMessageOpcode.GameScriptingBase, output.ToArray());
        }

        #endregion
    }
}
