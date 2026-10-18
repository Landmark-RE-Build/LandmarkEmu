using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// A placed prop's settings window (Lua Prop.SetupObject from the prop's build-mode menu): timer length, dice odds,
    /// damage amount, spawner respawn...
    ///
    /// 1. The client asks with PropBase 0x1B EditSettingsRequest {u64 prop} (FUN_142f17c10).
    /// 2. The server answers 0x1C EditSettingsReply (reader FUN_142f098a0, handler FUN_142f1b980 case 0x1c): u8 sub,
    ///    u64 player, u64 prop, bool ok, properties {u32 n; i32 key, record}, drop-downs {u32 n; u32 key, record,
    ///    options} (none sent), string name, string text. A record (FUN_142b7c740) is: string label, u32 label text id,
    ///    string key ("prop_timer_player_set_duration"), f32 value, f32 default, f32 max, f32 min, f32 step, u32 help
    ///    text id. The client copies them into the prop (+0x4e70) and builds the window (FUN_142f18330): a checkbox when
    ///    min 0, max 1, step 1; otherwise a slider.
    /// 3. Each change comes back as UpdatePropInteractionState 0xFD {u64 prop, u32 type 2, u32 key, state {u32 value,
    ///    u32, u64, ints, strings}, bool} (FUN_142f10200 → FUN_1430064b0), the key being <see cref="KeyHash"/> of the
    ///    record's key string.
    ///
    /// Which settings a prop has: EditableProperties.txt rows by prop kind (see <see cref="settingsByKind"/>). Values
    /// are kept in world_prop_setting and read by the story tools.
    /// </summary>
    public static class PropSettings
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        private static readonly object sync = new();

        private const byte EditSettingsReply = 0x1C;
        private const uint PropertyBehaviour = 2;

        public const string TimerDuration = "prop_timer_player_set_duration", TimerAutoRestart = "prop_timer_player_set_autorestart",
            DiceWeightA = "random_gate_static_weight_a", DiceWeightB = "random_gate_static_weight_b",
            DamageValue = "node_damage_value", DamageHeals = "node_damage_in_healing", FocusOnActivator = "pass_through_look_at",
            SpawnDelay = "player_npc_spawn_delay_flat", RespawnEnabled = "player_npc_respawn_enabled", SpawnerAi = "npc_spawner_ai_capability",
            TextDisplayTime = "prop_text_display_time", EmoteSpam = "npc_emote_spam_enabled",
            TextDisplayType = "prop_text_display_type",
            // Row 9 "Spawn Automatically" (573006 / 573007: "Spawn this creature immediately when Play Mode is activated. If
            // off, the creature must be spawned with a Link."), default 1, in both builds.
            SpawnAutomatically = "player_npc_spawn_when_npcs_turn_on",
            // Row 105 "Damages Terrain" (577601; checkbox, default 1), new in 0.695: Fiery Fungus / Proximity Mine craters
            // on build sites (storytools-695.md).
            DamagesTerrain = "npc_abilities_damage_terrain",
            SpawnerReaction = "prop_npc_spawner_faction_select",
            // Row 112 "Is Question? (Chat Bubble)" (577726): the Story Node's text asks Yes / No (0.695).
            TextIsQuestion = "prop_text_is_question";

        public const string Mood = "npc_mood_setting", Stance = "npc_stance_selection", Emote = "npc_emote_setting";

        /// <summary>
        /// Weapon visuals (0.695 only; trace/0695/agent-story.md): spawner row 40 <c>npc_spawn_weapons_equipped</c> "Spawn
        /// With Weapons Equipped" (577229; P6511 NPCs that spawn with their weapon out) and Story Node row 41
        /// <c>npc_weapon_visuals</c> "Weapon Visuals" (577234). Both are drop-downs with the options Equipped (577235) /
        /// Sheathed (577236) (wiki Story Node: "Weapon Visual ... Equipped, Sheathed"). Row 40 has the drop-down shape (VALUE_DEFAULT
        /// 109, max / min / step 0, as Mood / Stance / Emote); row 41 (default 106, 0-2 step 1) the shape of Reaction (row
        /// 17), another drop-down. Option values are the server's (EditablePropertyValues.txt does not ship, L18): the
        /// AnimationSelection each plays, <see cref="Creatures.AnimNpcWeaponsEquipped"/> / <see cref="Creatures.AnimNpcWeaponsSheathed"/>.
        /// </summary>
        public const string SpawnWeapons = "npc_spawn_weapons_equipped", WeaponVisuals = "npc_weapon_visuals";

        /// <summary>
        /// Respawn Point 2701 (TASKS 2.24 / 2.10): EditableProperties 103 "Enabled" (text 577589; checkbox 0-1, default 1,
        /// IF capable). Server Time Node 2659: row 101 "Amount of Listeners" (577254; 1-6, default 1). Both 0.695 rows
        /// (gameData695/EditableProperties.txt; trace/0695/storytools-695.md §4.7 / §4.8).
        /// </summary>
        public const string RespawnPointEnabled = "respawn_point_enabled", ServerTimeCount = "npc_time_count";

        /// <summary>
        /// Linking Area 1590: EditableProperties row 102 <c>area_voxel_damage_permission</c> "Allow Voxel Destruction"
        /// (577550; 0.695 only; VALUE_DEFAULT 0, max 0 / min 0 like the other drop-down rows): a Linking Area follows
        /// the site-wide setting by default but can allow or block destruction inside itself (P6183;
        /// trace/0695/storytools-695.md §4.11, TASKS 2.6). Read by <see cref="VoxelDestruction"/>.
        /// </summary>
        public const string AreaVoxelPermission = "area_voxel_damage_permission";

        /// <summary>
        /// Row 102's option values: 0 Site-wide setting (577551, the default: VALUE_DEFAULT 0), 1 Allowed (577552),
        /// 2 Blocked. <b>Inferred</b>: the values and their order (the option table EditablePropertyValues.txt does not
        /// ship, OPEN-LEADS L18) and the Blocked option's text — the client has no Linking Area "Blocked" string next to
        /// 577551 / 577552, so the generic 561915 "Blocked" is sent, with its string as the 0.695 option label.
        /// </summary>
        public const uint AreaDestructionSiteWide = 0, AreaDestructionAllowed = 1, AreaDestructionBlocked = 2;

        /// <summary>Server Time Node: at most this many listeners ("Amount of Listeners" max, row 101).</summary>
        public const int ServerTimeSlots = 6;

        /// <summary>
        /// The key of the Server Time Node's "Desired Time <paramref name="slot"/>" (1-6). <b>Ours (inferred)</b>: the
        /// client has no EditableProperties row and no text for these rows (L19); the 0.695 help page
        /// (ScriptObj_Servertime_FTE) only says "Desired Time ... using 24hr clock". The key is server-made, so a client
        /// change of it arrives as <see cref="KeyHash"/> of this string, which <see cref="Handle"/> matches.
        /// </summary>
        public static string DesiredTime(int slot) => "npc_time_" + slot;

        /// <summary>The slot (1-6) of a "Desired Time N" key, or null for any other key.</summary>
        public static int? DesiredTimeSlot(string key)
            => key.StartsWith("npc_time_", StringComparison.Ordinal) && int.TryParse(key.AsSpan(9), out int n) && n is >= 1 and <= ServerTimeSlots
                ? n : null;

        /// <summary>
        /// The Server Time Node's "Desired Time 1-6" rows: hour 0-23, step 1, default 0, label "Desired Time N" as a string
        /// (label text id 0, help text id 0). <b>Ours (inferred)</b>: labels, keys and ranges follow the help page ("Desired
        /// Time ... using 24hr clock") and the wiki (a new row appears for each listener once Amount of Listeners is
        /// saved); live's exact rows are unknown (trace/0695/OPEN-LEADS.md L19). Only the first Amount of Listeners rows
        /// are sent (<see cref="Visible"/>).
        /// </summary>
        private static readonly Definition[] desiredTimes = Enumerable.Range(1, ServerTimeSlots)
            .Select(n => new Definition(20000 + (uint)n, DesiredTime(n), 0, 0, 0f, 23f, 0f, 1f, $"Desired Time {n}")).ToArray();

        /// <summary>
        /// Creature health / damage on spawners (TASKS 2.14; 29 Aug 2016, P6428: creature health and damage can be
        /// changed, health only upwards, damage both ways; P6429: changed
        /// creatures may drop no normal loot). The 0.695 client has the label text 577652 "Damage % Multiplier" but no
        /// EditableProperties row for either (L19), so both are server rows (as the Server Time Node's): damage with the
        /// client's label, health with ours. Keys, ranges and steps are <b>ours (inferred)</b>: health 100-500 %, damage
        /// 25-300 %, steps of 25, default 100 (unchanged).
        /// </summary>
        public const string HealthPercent = "npc_spawner_health_percent", DamagePercent = "npc_spawner_damage_percent";
        private static readonly Definition[] spawnerStrengthRows =
        {
            new(20101, HealthPercent, 0, 0, 100f, 500f, 100f, 25f, "Health % Multiplier"),
            new(20102, DamagePercent, 577652, 0, 100f, 300f, 25f, 25f),
        };

        // ---- IF Gate 2976 (TASKS 2.11 / 2.31) ----------------------------------------------------------------------------

        /// <summary>
        /// The IF Gate's rows: which property of the linked prop it tests, how it compares, and with what. <b>Ours
        /// (inferred)</b>: the client has no EditableProperties rows for them (L19; only "Equal To" is named, P6496), so the
        /// keys, ranges and the comparison words are the server's (the row labels follow a developer post, <see cref="ifGateRows"/>). The property options use the client's own IF
        /// display texts of the IF-capable rows (EditableProperties IF_NODE_CAPABLE = 1: 33 Current Value 577622, 107 Blue
        /// Light 577623, 108 Yellow Light 577624, 103 Enabled 577589, 110 Owning Team 577709, 111 Is Alive 577714).
        /// </summary>
        public const string IfProperty = "if_gate_property", IfCompare = "if_gate_comparison", IfValue = "if_gate_value";

        public const uint IfCurrentValue = 0, IfBlueLight = 1, IfYellowLight = 2, IfEnabled = 3, IfOwningTeam = 4, IfIsAlive = 5;

        /// <summary>
        /// Checks on the triggering character instead of a linked prop (11 Oct 2016, P6535-P6538: the IF node can test
        /// the triggering character's game team, NPC disposition and whether it is a real player). The 0.695 client
        /// predates them (no texts), so their labels are the server's (<see cref="optionLabels"/>). Values: game team 1-4
        /// (0 = not in the site's game), disposition 0 enemy / 1 neutral / 2 ally (the spawner Reaction values; a player
        /// has none: -1), real player 1 / 0.
        /// </summary>
        public const uint IfTriggerTeam = 6, IfTriggerDisposition = 7, IfTriggerIsPlayer = 8;

        /// <summary>Is this IF property about the triggering character (no linked prop needed)?</summary>
        public static bool IsTriggerCheck(uint property) => property is IfTriggerTeam or IfTriggerDisposition or IfTriggerIsPlayer;
        public const uint IfEqual = 0, IfNotEqual = 1, IfLess = 2, IfGreater = 3;

        /// <summary>
        /// The IF Gate's three rows. Labels [P]: the developer's 29 Sep 2016 walk-through (forum 57987 #1) selects the
        /// property "in the <b>If</b> section", the comparison "in the <b>Is</b> section", "and then we set the
        /// <b>slider</b> for the value" (trace/0695/agent-leads3-b.md L19). The value row's label "Value" stays ours [I]
        /// (the post names only the slider).
        /// </summary>
        private static readonly Definition[] ifGateRows =
        {
            new(20101, IfProperty, 0, 0, 0f, 8f, 0f, 1f, "If"),
            new(20102, IfCompare, 0, 0, 0f, 3f, 0f, 1f, "Is"),
            new(20103, IfValue, 0, 0, 0f, 99f, 0f, 1f, "Value"),
        };

        /// <summary>Labels of drop-down options that have no client text (0.695 options carry a string label).</summary>
        private static readonly Dictionary<(string Key, uint Value), string> optionLabels = new()
        {
            [(IfCompare, IfEqual)] = "Equal To", [(IfCompare, IfNotEqual)] = "Not Equal To",
            [(IfCompare, IfLess)] = "Less Than", [(IfCompare, IfGreater)] = "Greater Than",
            [(IfProperty, IfTriggerTeam)] = "Triggering Character: Game Team",
            [(IfProperty, IfTriggerDisposition)] = "Triggering Character: NPC Disposition",
            [(IfProperty, IfTriggerIsPlayer)] = "Triggering Character: Is a Real Player",
        };

        /// <summary>How many listeners a Server Time Node has (row 101, clamped to 1-6; default 1).</summary>
        public static int ServerTimeListeners(ulong prop)
            => Math.Clamp((int)(TryGetSaved(prop, ServerTimeCount, out float v) ? v : 1f), 1, ServerTimeSlots);

        /// <summary>
        /// Drop-down settings and their options {label text id, value}.
        /// <para>
        /// Story Node Mood / Stance / Emote (rows 20 / 21 / 22; TASKS 2.23, storytools-695.md §4.1): the launch-era lists
        /// (4 moods, 8 stances, 29 emotes; wiki Story Node) with the client's own labels (texts 576538-576575, 576610 and
        /// the older 559168-559173 / 571883). Values are AnimationSelection ids, the rows the server plays on the creature
        /// (Creatures.PlayStoryAnimation → AbilityPlayAnim); the client only echoes the value it is given (option values
        /// of EditablePropertyValues.txt are not shipped, L18), so they are the server's own. Which selection each label
        /// plays, from AnimationSelectionVariables.txt and the switch nodes of PlayerAnimationNetwork64.mrn (decoded
        /// 2026-10-16 with the NodeDef layout of trace/systems/social.md; children in order):
        /// </para>
        /// <list type="bullet">
        /// <item>Moods [P]: 218-221 set Emote_Emotion 0-3; every Emotion_Switch reads it in the order neutral (Calm),
        /// Angry, Happy, Sad.</item>
        /// <item>Stances [P]: StanceSwitch|Switch1 (node 14546, reads Stance_Index) = Stand, Kneel, Placeholder, Dance,
        /// Sit_Chair, Laydown, AFK_States → Standing 223 (0), Kneeling 224 (1), Dancing 246 (3), Sitting 351 (4),
        /// Lying Down 480 (5); the outer StanceSwitch (14556) = Switch1, Drunk, Skip, Skip1, Sneak → Drunken 250 (101),
        /// Skipping 248 (102), Sneaking 251 (104).</item>
        /// <item>Emotes [P]: the Emote_Index switches of Emotes_SingleShot|Nuetral — Switch1 (11845) 1-11 Crafting, Beg,
        /// Bow, Cheer, Congratulate, Cry, Flirt, Frustrated, Giggle, Go_Away, Laugh; Switch2 (11846) 12-21 Point, Shrug,
        /// Sob, Talk_Long, Taunt, Wave, No, Yes, Dance, Onod; Switch3 (11868) 22-31 UsePotion, Call, Cough, Flex, Give,
        /// Sneeze, Thanks, Welcome, Yawn, Come_Here; Switch4 (11880) 32-33 Stretch, Hug. The EmoteSingle rows with those
        /// indices: Cough 449 (24), Flex 451 (25), Sneeze 452 (27), Welcome 453 (29), Yawn 454 (30), Stretch 456 (32),
        /// Hug 817 (33). Talk (Continuous) = 252 (EmoteLoop 1 + Talk_Long), Talk (Stop) = 253 (EmoteLoop 0, index 0:
        /// ends a loop). [I, strong] Beckon = Come_Here 455 (31) and Yell = Call 450 (23): the nine 0.695-list emotes
        /// with no player word match the nine EmoteSingle rows 449-456 / 817 one to one, and the client's lines say
        /// "[*speaker*] beckons everyone closer." (572119) / "yells loudly" (572114).</item>
        /// </list>
        /// <para>
        /// Which creatures show them (L117, trace/0695/agent-playmode.md §2): a creature plays them only when its actor's
        /// network (adr AnimationNetwork, loaded as &lt;name&gt;64.mrn) has nodes for them. 111 of the 135 creature props
        /// (Barren, Slaug, Scalewind, NovaTech people, Highfall, Ebonguard, Stoneskin lesser giants, The Outcast, the
        /// Target Dummy) use PlayerAnimationNetwork, which has every mood / stance / emote [P]; their anim sets (Kobold,
        /// Skeleton, Slaug, LesserGiant...) override only idle / move / react / attack clips, so the emotes are the shared
        /// Biped_Core clips [I strong, runtime check open]. The Npc network (Abominations,
        /// NovaTech hounds, Darkfang Specter, Oakmire Stag), Giant (four Giants, Powered Mega-Knight), Chomper and LargeBiped
        /// (Earth Elemental) networks declare Stance_Index (Npc / Giant also Emote_Index, Emote_Emotion, EmoteLoop) but have
        /// no emote, mood or stance animation nodes: no switch reads those parameters and the only condition on any of them
        /// is Stance_Index in (5.5, 6.5) — 6, the 0.695 play-dead stance (FakeDeath), which no Story Node stance uses [P];
        /// the Giant network also has one Stance_Index &gt; 0.5 condition whose transition is not decoded (open). Fiery
        /// Fungus, Vine Trap, Highfall Catapult and Proximity Mine networks have none of these parameters. The selections
        /// are still sent to every creature: each has STOPS_CURRENT_SELECTION 1, so even where nothing plays the client
        /// stops the creature's current selection, as it did live.
        /// </para>
        /// </summary>
        public static readonly Dictionary<string, (uint Text, uint Value)[]> DropDowns = new()
        {
            [Mood] = new (uint, uint)[] { (559168, 219), (559169, 218), (559170, 220), (576538, 221) },
            [Stance] = new (uint, uint)[]
            {
                (559173, 246), (571883, 250), (576539, 224), (576540, 480), (576610, 351), (576541, 248), (576542, 251), (576543, 223)
            },
            [Emote] = new (uint, uint)[]
            {
                (576547, 455), (576544, 228), (576545, 229), (576546, 230), (576550, 232), (576551, 449), (576552, 231),
                (576553, 240), (576554, 451), (576555, 233), (576556, 234), (576557, 817), (576558, 237), (576559, 235),
                (576560, 244), (576561, 245), (576562, 238), (576563, 222), (576564, 239), (576565, 452), (576566, 456),
                (576567, 241), (576569, 252), (576570, 253), (576571, 242), (576572, 217), (576573, 453), (576574, 454),
                (576575, 450)
            },
            // Text Location (576505): PlayerUpdate DisplayPropText modes (handler FUN_142ee6ce0) — 4 "In Chat Bubble"
            // (the setting's default), 2 "On Screen", 3 "In Chat".
            // 0.695 adds 577170 "In Chat Bubble and Chat" (mode 5, FUN_142f859b0), the 0.695 default (row 25 VALUE_DEFAULT 5).
            [TextDisplayType] = new (uint, uint)[] { (576481, 4), (576480, 2), (576482, 3), (577170, 5) },
            // Spawner "Reaction" (row 17, 574214; values 0-2, default 0): the three texts that follow it, 574215 Enemy,
            // 574216 Neutral, 574217 Ally — the story faction values (0 enemy, 1 neutral, 2 ally; order inferred).
            [SpawnerReaction] = new (uint, uint)[] { (574215, 0), (574216, 1), (574217, 2) },
            // Combat Tactics (row 31, 577073; 0.695 key npc_spawner_ai_capability 0-2 replacing the 0.589 checkbox
            // npc_spawner_ai_enabled): 577230 Standard, 577231 Stationary, 577232 Do Nothing (order inferred).
            [SpawnerAi] = new (uint, uint)[] { (577230, 0), (577231, 1), (577232, 2) },
            // Weapon visuals (rows 40 / 41, see SpawnWeapons): Equipped / Sheathed, valued by the AnimationSelection played.
            [SpawnWeapons] = new (uint, uint)[] { (577235, Creatures.AnimNpcWeaponsEquipped), (577236, Creatures.AnimNpcWeaponsSheathed) },
            [WeaponVisuals] = new (uint, uint)[] { (577235, Creatures.AnimNpcWeaponsEquipped), (577236, Creatures.AnimNpcWeaponsSheathed) },
            [IfProperty] = new (uint, uint)[] { (577622, IfCurrentValue), (577623, IfBlueLight), (577624, IfYellowLight),
                (577589, IfEnabled), (577709, IfOwningTeam), (577714, IfIsAlive),
                (0, IfTriggerTeam), (0, IfTriggerDisposition), (0, IfTriggerIsPlayer) },
            [IfCompare] = new (uint, uint)[] { (0, IfEqual), (0, IfNotEqual), (0, IfLess), (0, IfGreater) },
            // Linking Area "Allow Voxel Destruction" (row 102, 577550): Site-wide setting (default), Allowed, Blocked
            // (values and the Blocked text inferred, see AreaDestructionSiteWide).
            [AreaVoxelPermission] = new (uint, uint)[]
            {
                (577551, AreaDestructionSiteWide), (577552, AreaDestructionAllowed), (561915, AreaDestructionBlocked)
            },
        };

        /// <summary>
        /// The option a drop-down starts on when none was chosen, where it is not the first one listed. Mood Calm 218: the
        /// help page ScriptObj_Text_FTE "Calm is the default" [P]. Stance Standing 223 and Emote Wave 217 [I]: the table's
        /// defaults (21 = 81, 22 = 51) are values of the unshipped EditablePropertyValues.txt (L18); Standing changes
        /// nothing and Wave was this server's first emote before the lists grew.
        /// <para>
        /// Weapon visuals [I]: "Spawn With Weapons Equipped" starts on Sheathed (creatures spawned without it before 0.695,
        /// and its default 109 would be the second of a pair 108 / 109 if the unshipped option ids run in label order);
        /// the Story Node's Weapon Visuals starts on Equipped (its default 106 the first of 106 / 107 by the same reading,
        /// and a node is linked to change something).
        /// </para>
        /// </summary>
        private static readonly Dictionary<string, uint> dropDownDefaults = new()
        {
            [Mood] = 218, [Stance] = 223, [Emote] = 217,
            [SpawnWeapons] = Creatures.AnimNpcWeaponsSheathed, [WeaponVisuals] = Creatures.AnimNpcWeaponsEquipped,
        };

        /// <summary>A drop-down's default option value (<see cref="dropDownDefaults"/>, else its first option).</summary>
        public static uint DefaultOption(string key)
            => dropDownDefaults.TryGetValue(key, out uint v) ? v : DropDowns.TryGetValue(key, out var o) ? o[0].Value : 0u;

        /// <summary>
        /// Moods saved before 2026-10-16 used 1 angry / 2 calm / 3 happy (no animation); they map to the Emote_Emotion
        /// selections that replaced them.
        /// </summary>
        private static readonly Dictionary<uint, uint> legacyMoods = new() { [1] = 219, [2] = 218, [3] = 220 };

        /// <summary>A drop-down setting's value, or its default option when nothing valid was chosen.</summary>
        public static uint Choice(ulong prop, string key)
        {
            uint v = (uint)Get(prop, key);
            if (key == Mood && legacyMoods.TryGetValue(v, out uint mood))
                v = mood;
            return DropDowns.TryGetValue(key, out var options) && options.All(o => o.Value != v) ? DefaultOption(key) : v;
        }

        /// <summary>
        /// Duplicating a prop (Props.Duplicate, placed by Props.Save): the copy takes the original's saved window settings
        /// (TASKS 6.30; P4352, 1 Apr 2016: a duplicated creature keeps its name, faction and respawn settings —
        /// Reaction row 17, Respawn Time 18, Respawn After Killed 19). [I] Every other window row of the prop is
        /// copied too (a duplicated timer keeps its duration); read-only rows and per-prop state kept in the same store
        /// (a reward chest's count remaining and per-player lockouts) are not window rows and stay behind.
        /// </summary>
        public static int CopySettings(ulong from, ulong to, uint propId)
        {
            int copied = 0;
            foreach (Definition d in SettingsOf(propId))
                if (!d.ReadOnly && TryGetSaved(from, d.Key, out float v))
                {
                    Set(to, d.Key, v);
                    copied++;
                }
            return copied;
        }

        /// <summary>
        /// An EditableProperties.txt row (or a server row: <paramref name="Label"/> is then the shown label).
        /// <paramref name="ReadOnly"/> is the table's BLOCKS_USER_MODIFICATION (column 10; loader 0.695 FUN_143dc1440 keeps
        /// it at row +0x64): the window shows it as a text line (<see cref="WriteRecord"/>) and changes are refused.
        /// </summary>
        public record Definition(uint Id, string Key, uint NameId, uint HelpId, float Default, float Max, float Min, float Step,
            string Label = null, bool ReadOnly = false);

        /// <summary>
        /// Captions of read-only text rows that have no DISPLAY_NAME_ID. Row 106 loot_chest_remaining_count (Reward Chests):
        /// <b>ours (inferred)</b> — P6414 calls it "the 'count remaining'"; live's wording is unknown (spec-reward-chests
        /// U3).
        /// </summary>
        private static readonly Dictionary<string, string> readOnlyCaptions = new()
        {
            [RewardChests.RemainingKey] = "Count Remaining",
        };

        private static Dictionary<uint, Definition> definitions;

        /// <summary>The placeable Fiery Fungus creature prop (PropCategory 99 Monsters; "spawner: default: small: hazard: fungus fiery").</summary>
        private const uint FieryFungusCreatureProp = 2613;

        /// <summary>EditableProperties ids per story-tool kind (prop id, or "spawner").</summary>
        private static readonly Dictionary<object, uint[]> settingsByKind = new()
        {
            [StoryTools.TimerProp] = new uint[] { 14, 13 },
            // Counting Node / Scoreboard: Starting Count, Max (row 34 has no label: not shown).
            [StoryTools.CountingNodeProp] = new uint[] { 33, 35 },
            [StoryTools.ScoreboardProp] = new uint[] { 33, 35 },
            // Probability Filter: one "Odds of Winning" percentage (row 15, text 573066), as both clients' help pages
            // show (ScriptObj_Probability_FTE); row 16 "Random Roll Weight B" is not offered.
            [StoryTools.DiceRollProp] = new uint[] { 15 },
            [StoryTools.DamageNodeProp] = new uint[] { 30, 32 },
            [StoryTools.FocusNodeProp] = new uint[] { 29 },
            [StoryTools.RusticSignProp] = new uint[] { 25, 37 },
            [StoryTools.FjordenBookOpenProp] = new uint[] { 25, 37 },
            [StoryTools.FjordenBookClosedProp] = new uint[] { 25, 37 },
            [StoryTools.GoldScrollProp] = new uint[] { 25, 37 },
            // Story Node: Text Location (25), Text Duration (37), Announce Emotes in Chat (24), Weapon Visuals (41, 0.695; the
            // wiki lists it first of the creature settings), Mood / Stance / Emote, Is Question (112).
            [StoryTools.StoryNodeProp] = new uint[] { 25, 37, 24, 41, 20, 21, 22, 112 },
            // Respawn Point 2701: "Enabled" (row 103; plan-storytools-next.md Spec 8).
            [StoryTools.RespawnPointProp] = new uint[] { 103 },
            // Server Time Node 2659 (and the unplaceable Hour Node 2945, same kind): "Amount of Listeners" (row 101), then
            // the server's "Desired Time 1-6" rows (desiredTimes; Spec 9).
            [StoryTools.ServerTimeNodeProp] = new uint[] { 101 },
            [StoryTools.HourNodeProp] = new uint[] { 101 },
            // Reward Chests 3003-3008 (0.695): "Player looting cooldown" (row 104, 15-120 min, default 30) and the read-only
            // count remaining (row 106) (spec-reward-chests.md §3).
            [RewardChests.CommonProp] = new uint[] { 104, 106 },
            [RewardChests.StoneProp] = new uint[] { 104, 106 },
            [RewardChests.MetalProp] = new uint[] { 104, 106 },
            [RewardChests.GemProp] = new uint[] { 104, 106 },
            [RewardChests.SuperbProp] = new uint[] { 104, 106 },
            [RewardChests.LegendaryProp] = new uint[] { 104, 106 },
            // Linking Area: "Allow Voxel Destruction" (row 102, 0.695; TASKS 2.6). Rows missing from the data set (0.589's
            // EditableProperties has no 102) are skipped by SettingsOf.
            [StoryTools.LinkingAreaProp] = new uint[] { 102 },
            // Creature spawners: Spawn Automatically (9), Reaction (17), respawn (18 / 19), Combat Tactics (31), Spawn With
            // Weapons Equipped (40, 0.695; SORT_ORDER 3 after Reaction 1 / Tactics 2).
            ["spawner"] = new uint[] { 9, 17, 18, 19, 31, 40 },
            // Combat Node 2940 (0.695): Combat Tactics (row 31, the spawners' drop-down 577230-577232 Standard / Stationary
            // / Do Nothing), which its "Set combat tactics" action applies to the linked creature (StoryTools
            // CombatSetTactics / SpawnerSetTactics read the node's row 31) [P]: P6150 Combat Tactics on creature
            // spawners and on the Combat Node; P6157 set the Combat Tactics in the Combat Node's settings; wiki "Combat
            // Node" (Settings: Name, Combat Tactics). Below it the linked creature's abilities
            // ("Ability 1, Ability 2, etc", P6152 / P6157 / wiki): read-only text rows the server adds (combatAbilityRows,
            // Visible; L19).
            [StoryTools.CombatNodeProp] = new uint[] { 31 },
            // Non-pathing hazards: Proximity Mines (1220) and the Vine Trap (2932, "hazard: proximity mine" in the prop
            // dump) share the faction and respawn settings of other creatures that don't path, such as fiery fungi
            // (P5269;
            // the Fiery Fungus creature prop 2613 likewise): Spawn Automatically (9), Reaction (17), respawn (18 / 19), no
            // Combat Tactics (inferred: a hazard has no tactics to choose).
            [Creatures.ProximityMineProp] = new uint[] { 9, 17, 18, 19 },
            [Creatures.VineTrapProp] = new uint[] { 9, 17, 18, 19 },
            [FieryFungusCreatureProp] = new uint[] { 9, 17, 18, 19 },
            // Target Dummy 2890: harmless and ignored by enemy NPCs (P5275), so only its spawning and respawn
            // (killable, P5445) mean anything (inferred: no Reaction / Combat Tactics rows).
            [Creatures.TargetDummyProp] = new uint[] { 9, 18, 19 },
            ["flinger"] = new uint[] { 4, 5, 6 },
            ["platform"] = new uint[] { 7, 8 },
        };

        private static void EnsureLoaded()
        {
            if (definitions != null)
                return;
            var defs = new Dictionary<uint, Definition>();
            foreach (string[] r in GameDataFiles.Rows("EditableProperties.txt"))
                defs[r.U(0)] = new Definition(r.U(0), r.S(1), r.U(2), r.U(3), F(r, 4), F(r, 5), F(r, 6), F(r, 7), ReadOnly: r.U(10) != 0);
            lock (sync)
            {
                if (definitions != null)
                    return;
                using var context = DatabaseManager.Instance.CharacterDatabase.NewWorldContext();
                foreach (WorldPropSettingModel s in context.WorldPropSetting.ToList())
                    values[(s.PropId, s.Key)] = s.Value;
                definitions = defs;
            }
        }

        private static float F(string[] row, int i)
            => float.TryParse(row.S(i), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;

        private static readonly Dictionary<(ulong Prop, string Key), float> values = new();

        /// <summary>The settings a prop's window shows (empty for props without settings).</summary>
        public static List<Definition> SettingsOf(uint propId)
        {
            EnsureLoaded();
            List<Definition> table = InstancedGames.TableSettings(propId);
            if (table.Count > 0)
            {
                lock (sync)
                    foreach (Definition d in table)
                        dynamicDefaults[d.Key] = d.Default;
                return table;
            }
            StoryTools.Kind kind = StoryTools.KindOf(propId);
            object key = settingsByKind.ContainsKey(propId) ? propId
                : kind?.Name == "creature spawner" ? "spawner" : kind?.Name is "flinger" or "platform" ? kind.Name : null;
            if (key == null || !settingsByKind.TryGetValue(key, out uint[] ids))
                return ServerRowsOf(propId);
            List<Definition> rows = ids.Where(definitions.ContainsKey).Select(i => definitions[i]).ToList();
            if (propId is StoryTools.ServerTimeNodeProp or StoryTools.HourNodeProp)
                rows.AddRange(desiredTimes);         // all six: a client change to any of them is accepted (Handle)
            if (Equals(key, "spawner"))
                rows.AddRange(spawnerStrengthRows);  // health / damage % (2.14)
            return rows;
        }

        /// <summary>A prop's rows when the kind table has none of its own (the IF Gate's server rows).</summary>
        private static List<Definition> ServerRowsOf(uint propId)
        {
            return propId == StoryTools.IfGateProp ? ifGateRows.ToList() : new List<Definition>();
        }

        // ---- Combat Node 2940 ability rows (OPEN-LEADS L19, spec agent-leads4.md B2) ----------------------------------

        /// <summary>
        /// The Combat Node's "Ability N" rows, one per ability of the linked creature, below its Combat Tactics drop-down.
        /// <list type="bullet">
        /// <item>[P] What live showed: P6157 "Open the Combat Node's settings, and set the desired Combat Tactics (e.g.
        /// 'Stationary'). Below this you will see a list of the creature's abilities"; P6152 the list appears once the node
        /// is linked; wiki "Combat Node": the settings list the creature's abilities as Ability 1, Ability 2 and so on
        /// (trace/0695/agent-leads3-b.md L19).</item>
        /// <item>[P] How the 0.695 window draws them: read-only text lines (<see cref="WriteRecord"/>, max 0 / min 0 →
        /// LuaPropertyEditor::PropertyTextLine). The window builder FUN_142fb71f0 draws the drop-down list first and the
        /// plain records after it, so the rows sit below Combat Tactics with no ordering work; the text line takes two
        /// literal strings (FUN_1435becc0(editor, record key string @+0x20, record label string @+0x00)), no text id, so
        /// the caption and the ability's name are written as strings.</item>
        /// <item>[I] Ours: the keys (no EditableProperties row or text exists for them), the caption wording "Ability N"
        /// (the wiki's), and "Secondary Ability" for the nameless shield block (the wiki's own entry for it, e.g. Barren
        /// Protector's Ability 1, a secondary ability that blocks with the shield).</item>
        /// </list>
        /// The count is Creatures.StoryAbilityCount's rule, at most the node's six "perform Ability N" actions, so rows and
        /// actions agree. Not in <see cref="SettingsOf"/>: <see cref="Handle"/> never accepts them, CopySettings skips them.
        /// </summary>
        private static readonly Definition[] combatAbilityRows = Enumerable.Range(1, StoryTools.CombatAbilityActions.Length)
            .Select(n => new Definition(30000 + (uint)n, $"combat_node_ability_{n}", 0, 0, 0f, 0f, 0f, 0f, $"Ability {n}", ReadOnly: true))
            .ToArray();

        /// <summary>The shown name of an ability with no name text (the shield block; wiki wording, see <see cref="combatAbilityRows"/>).</summary>
        private const string UnnamedAbility = "Secondary Ability";

        /// <summary>
        /// The rows a prop's window shows: all of <see cref="SettingsOf"/>, except that a Server Time Node shows only its
        /// first Amount of Listeners "Desired Time" rows (wiki: new rows appear when the window is opened again after the
        /// count was saved), and a Combat Node also shows its linked creature's abilities (<see cref="combatAbilityRows"/>).
        /// <paramref name="texts"/> gets the shown value of every read-only row whose value is text, by key.
        /// </summary>
        private static List<Definition> Visible(ulong guid, uint propId, List<Definition> settings, out Dictionary<string, string> texts)
        {
            texts = new Dictionary<string, string>();
            int listeners = ServerTimeListeners(guid);
            List<Definition> rows = settings.Where(d => DesiredTimeSlot(d.Key) is not int n || n <= listeners).ToList();
            if (propId == StoryTools.CombatNodeProp)
            {
                // From the linked spawner's kind, so the list shows in Edit Mode before anything has spawned.
                uint[] abilities = StoryTools.CombatAbilityNames(guid);
                for (int i = 0; i < abilities.Length && i < combatAbilityRows.Length; i++)
                {
                    Definition d = combatAbilityRows[i];
                    rows.Add(d);
                    texts[d.Key] = abilities[i] != 0
                        ? Shared.GameTable.Text.TextManager.Instance.GetTextForId(abilities[i]) ?? UnnamedAbility
                        : UnnamedAbility;
                }
            }
            return rows;
        }

        /// <summary>A prop's setting: its saved value, else the EditableProperties default.</summary>
        public static float Get(ulong prop, string key)
        {
            EnsureLoaded();
            lock (sync)
            {
                if (values.TryGetValue((prop, key), out float v))
                    return v;
                if (dynamicDefaults.TryGetValue(key, out float d))
                    return d;
            }
            return definitions.Values.FirstOrDefault(d => d.Key == key)?.Default ?? 0f;
        }

        /// <summary>A setting only when the player saved one.</summary>
        public static bool TryGetSaved(ulong prop, string key, out float value)
        {
            EnsureLoaded();
            lock (sync)
                return values.TryGetValue((prop, key), out value);
        }

        /// <summary>Defaults of settings that aren't EditableProperties rows (Game Table rule parameters).</summary>
        private static readonly Dictionary<string, float> dynamicDefaults = new()
        {
            [HealthPercent] = 100f,
            [DamagePercent] = 100f,
        };

        /// <summary>The client's key for a setting: a one-at-a-time hash of its key string (FUN_142f18330).</summary>
        public static int KeyHash(string key)
        {
            int h = 0;
            foreach (byte b in Encoding.ASCII.GetBytes(key))
            {
                h = unchecked(((sbyte)b + h) * 0x401);
                h ^= h >> 6;
            }
            h = unchecked(h * 9);
            h ^= h >> 11;
            return unchecked(h * 0x8001);
        }

        /// <summary>PropBase 0x1B EditSettingsRequest {u64 prop}: the settings window opens.</summary>
        public static void SendSettings(WorldSession session, ulong guid)
        {
            Props.PlacedProp prop = Props.Find(guid);
            bool ok = prop != null && Props.CanEdit(session, guid);
            // A Game Table hosting its site's game opens the client's own game editor instead (the 0.589 UI scripts
            // never call RequestGainActiveAuthorshipOfGame, and the client's settings request waits for no reply).
            if (ok && InstancedGames.EditTable(session, guid))
                return;
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(EditSettingsReply);
            w.Write(session.PlayerGuid);
            w.Write(guid);
            w.Write(ok);
            Dictionary<string, string> texts = null;
            List<Definition> settings = ok ? Visible(guid, prop.PropId, SettingsOf(prop.PropId), out texts) : new List<Definition>();
            List<Definition> plain = settings.Where(d => !DropDowns.ContainsKey(d.Key)).ToList();
            List<Definition> lists = settings.Where(d => DropDowns.ContainsKey(d.Key)).ToList();
            w.Write((uint)plain.Count);
            foreach (Definition d in plain)
            {
                w.Write(KeyHash(d.Key));
                // The count remaining is the chest's own state (a fresh chest has no saved value: its full uses).
                WriteRecord(w, d, d.Key == RewardChests.RemainingKey ? RewardChests.Remaining(guid) : Get(guid, d.Key),
                    texts?.GetValueOrDefault(d.Key));
            }
            // Drop-downs (0.589 FUN_142b9d440): u32 key, record, options {u32 n; u32 label text id, u32 value, bool, string
            // label (the string is new in 0.695: FUN_142c391d0 → FUN_142c1eae0)}.
            w.Write((uint)lists.Count);
            foreach (Definition d in lists)
            {
                uint chosen = Choice(guid, d.Key);
                w.Write(KeyHash(d.Key));
                WriteRecord(w, d, chosen);
                var options = DropDowns[d.Key];
                w.Write((uint)options.Length);
                uint defaultValue = DefaultOption(d.Key);
                foreach ((uint text, uint value) in options)
                {
                    w.Write(text);
                    w.Write(value);
                    w.Write(value == defaultValue);
                    w.Write(text != 0 ? Shared.GameTable.Text.TextManager.Instance.GetTextForId(text) ?? ""
                        : optionLabels.GetValueOrDefault((d.Key, value), ""));
                }
            }
            w.Write(prop?.CustomName ?? "");
            w.Write(prop?.Text ?? "");
            session.EnqueueMessage(ZoneMessageOpcode.PropBase, output.ToArray());
            log.Info($"{session.CharacterName} opened the settings of prop 0x{guid:X}: {(ok ? $"{settings.Count} setting(s)" : "refused")}.");
        }

        /// <summary>
        /// A setting record (0.589 FUN_142b7c740): string label, u32 label text id, string key, value, default, max, min,
        /// step, u32 help text id. The five numbers are f64 in 0.695 (f32 in 0.589; record reader FUN_142c15ee0 fills
        /// +0x38 .. +0x58 with doubles; trace/0695/plan-storytools-next.md §0.1): sent as f32 to a 0.695 client every
        /// settings window was misread from the first value on.
        /// </summary>
        /// <param name="text">A read-only row's shown value when it is text (a Combat Node's ability name); else the
        /// number <paramref name="value"/> is shown.</param>
        private static void WriteRecord(GamePacketWriter w, Definition d, float value, string text = null)
        {
            if (d.ReadOnly)
            {
                // A read-only row: max 0 and min 0 make the 0.695 window builder (FUN_142fb71f0) draw a
                // LuaPropertyEditor::PropertyTextLine (property type 7, new in 0.695), whose row exports (FUN_1435c06f0 /
                // FUN_1435c1b60) show the record's key string as the row's Name and its label string as the Value
                // (InstancePropertyEditor: "Text" rows show valueText = Value). So the label carries the value and the key
                // string the caption; the i32 key before the record stays KeyHash(d.Key) (spec-reward-chests.md §3.2).
                // FUN_142fb71f0 hands the text line both strings as they are (FUN_1435becc0(editor, key @+0x20, label
                // @+0x00)) and does not read the label text id @+0x18 for it, so a text value goes out as the string itself.
                w.Write(text ?? ((int)value).ToString(CultureInfo.InvariantCulture));
                w.Write(0u);
                w.Write(readOnlyCaptions.GetValueOrDefault(d.Key) ?? d.Label
                    ?? Shared.GameTable.Text.TextManager.Instance.GetTextForId(d.NameId) ?? d.Key);
                foreach (double number in new double[] { value, d.Default, 0, 0, d.Step })
                    w.Write(number);
                w.Write(d.HelpId);
                return;
            }
            // Server rows (label text id 0) carry their label as the string (Server Time Node "Desired Time N").
            w.Write(d.Label ?? Shared.GameTable.Text.TextManager.Instance.GetTextForId(d.NameId) ?? d.Key);
            w.Write(d.NameId);
            w.Write(d.Key);
            foreach (float number in new[] { value, d.Default, d.Max, d.Min, d.Step })
                w.Write((double)number);
            w.Write(d.HelpId);
        }

        /// <summary>
        /// UpdatePropInteractionState 0xFD from the client: type 2 is a settings change {u64 prop, u32 2, u32 key hash,
        /// u32 value, ...}. Saved when the player may edit the prop and the key is one of its settings.
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.UpdatePropInteractionState)]
        public static void Handle(WorldSession session, PropInteractionStateRequest request)
        {
            var reader = new GamePacketReader(request.Body);
            if (request.Body.Length < 20)
                return;
            ulong guid = reader.ReadULong();
            uint type = reader.ReadUInt();
            int keyHash = reader.ReadInt();
            int value = reader.ReadInt();
            if (type != PropertyBehaviour)
            {
                log.Info($"{session.CharacterName}: prop interaction state type {type}: {BitConverter.ToString(request.Body)}");
                return;
            }
            Props.PlacedProp prop = Props.Find(guid);
            Definition d = prop == null ? null : SettingsOf(prop.PropId).FirstOrDefault(s => KeyHash(s.Key) == keyHash);
            // Read-only rows (BLOCKS_USER_MODIFICATION, e.g. a reward chest's count remaining) never change from the client.
            if (d == null || d.ReadOnly || !Props.CanEdit(session, guid))
            {
                log.Info($"{session.CharacterName}: setting {keyHash} on 0x{guid:X} refused.");
                return;
            }
            if (DropDowns.TryGetValue(d.Key, out var options))
            {
                if (options.All(o => o.Value != (uint)value))
                    return;
                Set(guid, d.Key, value);
            }
            else
                Set(guid, d.Key, Math.Clamp(value, Math.Min(d.Min, d.Max), Math.Max(d.Min, d.Max)));
            log.Info($"{session.CharacterName} set {d.Key} = {value} on prop 0x{guid:X}.");
            // A Counting Node / Scoreboard out of Play Mode shows its Starting Count on the disc (TASKS 2.19).
            StoryTools.SettingChanged(guid, d.Key);
        }

        /// <summary>
        /// Saves a setting (also dev "propset"). A slider sends a change for every step it is dragged through, so the
        /// value is kept at once and written to the database in a batch (<see cref="Flush"/>, a second later).
        /// </summary>
        public static void Set(ulong guid, string key, float value)
        {
            EnsureLoaded();
            lock (sync)
            {
                values[(guid, key)] = value;
                pending.Add((guid, key));
            }
        }

        private static readonly HashSet<(ulong Prop, string Key)> pending = new();
        private static DateTime nextFlush = DateTime.MinValue;

        /// <summary>Server tick: writes the settings changed since the last flush (at most once a second).</summary>
        public static void Flush()
        {
            if (DateTime.UtcNow < nextFlush)
                return;
            nextFlush = DateTime.UtcNow.AddSeconds(1);
            List<WorldPropSettingModel> rows;
            lock (sync)
            {
                if (pending.Count == 0)
                    return;
                rows = pending.Where(values.ContainsKey).Select(k => new WorldPropSettingModel { PropId = k.Prop, Key = k.Key, Value = values[k] }).ToList();
                pending.Clear();
            }
            DatabaseManager.Instance.CharacterDatabase.SaveWorld(c =>
            {
                foreach (WorldPropSettingModel row in rows)
                {
                    c.WorldPropSetting.RemoveRange(c.WorldPropSetting.Where(s => s.PropId == row.PropId && s.Key == row.Key));
                    c.WorldPropSetting.Add(row);
                }
            }).ContinueWith(t => log.Error(t.Exception, "Saving prop settings failed."),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>A prop went away: its settings too.</summary>
        public static void PropRemoved(ulong guid)
        {
            EnsureLoaded();
            bool had;
            lock (sync)
            {
                var keys = values.Keys.Where(k => k.Prop == guid).ToList();
                had = keys.Count > 0;
                foreach (var k in keys)
                    values.Remove(k);
            }
            if (had)
                DatabaseManager.Instance.CharacterDatabase.SaveWorld(c => c.WorldPropSetting.RemoveRange(c.WorldPropSetting.Where(s => s.PropId == guid)));
        }
    }
}
