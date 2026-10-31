using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Game.WorldChannel;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Site games (instanced games): a Game Table placed on a build site makes the site host a game of its rule set
    /// (IgRuleSetPlacementProps: prop 1346 Deathmatch, 1347 Team Deathmatch, 1226 Team Zone Control, 2313 Adventure).
    /// Players standing on the site see it in the walk-on window and join or leave with its buttons. See
    /// docs/instanced-games.md for the packets.
    ///
    /// Games are not stored: they follow the placed Game Tables (checked every two seconds), so picking the table up
    /// ends the game and a server restart brings back an idle game for every table.
    ///
    /// Flow: Started ("Waiting for players...") until the rule set's minimum have joined → WaitingForReady (the
    /// countdown, general setting 4) → Running (time limit, point limit) → Victory (the end-of-game scoreboard opens)
    /// → Finishing ("Game over! Resetting in N seconds") → Started again with whoever stayed. In a running game the
    /// players hurt each other (FFA: everyone; team games: the other team), kills score, and the dead respawn at the
    /// game's spawn points after the respawn delay instead of seeing the death window.
    ///
    /// The 0.695 Game Table rework (teams / names / switching from the table's general settings, rules switched in the
    /// editor, Score / Victory Nodes, point-grant events) is in InstancedGames.Rework.cs; the editor in
    /// InstancedGames.Editor.cs, the Game Finder in InstancedGames.Finder.cs, queues in InstancedGames.Queue.cs.
    /// </summary>
    public static partial class InstancedGames
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        private static readonly object sync = new();

        // InstancedGameBase subs (u32), 0.695 numbers [P]: the 0.695 client inserted GetConfigurationRequest at 0xD and
        // moved every 0.589 sub from 0xD up by one (packets-695.md "Changed layouts" A5, coverage-695.md §0x10D; 0.695
        // S→C dispatcher cases PropState 0x10 / PropInteraction 0x11 = sub - 2). Checked with Frida 2026-10-17: PropState
        // must arrive as 0x12 (the 0.589 0x11 + 1; until the server spoke 0.695 natively it was written as 0.589 0x11 and
        // the gateway's translation added the 1). Server → client (ObserverRegistration both ways), 0.589 in brackets:
        private const uint SubScoreboard = 3, SubPropRecipe = 4, SubState = 6, SubEvent = 7, SubRequest = 8, SubHud = 9,
            SubPropState = 0x12 /* 0x11 */, SubPropInteraction = 0x13 /* 0x12 */, SubTeamMembers = 0x15 /* 0x14 */,
            SubObserverRegistration = 0x16 /* 0x15 */, SubPlayerRemoval = 0x14 /* 0x13 */;

        // InstancedGameRequest types (i16). Client → server: 1 GainAuthorship (Claims.RequestGainActiveAuthorshipOfGame),
        // 2 LoseAuthorship, 3 Join (Claims.JoinInstancedGame, walk-on "Join"), 7 Leave, 8 Respawn (u64 = the game),
        // 9 JoinTeam (0.695 only: InstancedGameRequestJoinTeamPacket, value = the team; see SwitchTeam).
        // Server → client (HandleGameRequestPacket FUN_143438d00): 4 left the queue, 5 queue position, 6 membership:
        // result 1 with value 1 = in the game (manager +0x38 = game), 0 = out of it (FUN_143432090 clears the game).
        private const short RequestGainAuthorship = 1, RequestLoseAuthorship = 2, RequestJoin = 3, RequestMembership = 6,
            RequestLeave = 7, RequestRespawn = 8, RequestJoinTeam = 9;

        /// <summary>
        /// Game states (walk-on status text FUN_142d1d3b0; the client's state switch FUN_14343ff60: 3/4/6 show the game
        /// HUD, 4 plays the countdown sound, 6 the start-of-match sound, 7 opens the end-of-game scoreboard, 0/1/2/9
        /// put the HUD away). The HUD packet carries the state again for its message line (FUN_143432bd0).
        /// </summary>
        public const uint StateNone = 0, StateConfigured = 1, StateIdle = 2, StateStarted = 3, StateWaitingForReady = 4,
            StateReady = 5, StateRunning = 6, StateVictory = 7, StateFinishing = 8, StateRestarting = 9;

        // Event types (HandleGameEventPacket FUN_143437440): kill feed lines and their sounds.
        private const uint EventEnemyKill = 1, EventTeamKill = 2, EventSelfKill = 3, EventEnvironmentKill = 4;

        /// <summary>How long the end-of-game scoreboard stays before the reset countdown, and that countdown.</summary>
        private static readonly TimeSpan VictoryTime = TimeSpan.FromSeconds(10), FinishingTime = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan SyncInterval = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Steps a capture's HUD control indicator (sub 9) is re-sent in: 5 = every 20 % (10 until the 2026-10-18
        /// bandwidth audit R6, which halved the HUD resends during a capture: 2 → 1 a second per capturing point with the
        /// default 5 s CaptureSeconds). The overhead PropInteraction bar is unaffected (it runs on the client's clock)
        /// [I ours: live's HUD step is not known].
        /// </summary>
        private const float CaptureHudSteps = 5f;
        private static readonly TimeSpan ArrivalGrace = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Spawn props (IgPropRoleProps): 1371 spawn location (start / respawn / exit); team spawns of role 2 in team order
        /// (rows 4, 5, 17, 18): 1223 TeamSpawner - Blue, 1367 - Red, and the 0.695 3012 - Yellow, 3013 - Green (texts
        /// 577680-577683), team t (0-based) using the t-th.
        /// </summary>
        private const uint SpawnLocationProp = 1371, TeamSpawnProp0 = 1223, TeamSpawnProp1 = 1367, TeamSpawnProp2 = 3012, TeamSpawnProp3 = 3013;
        private static readonly uint[] teamSpawnProps = { TeamSpawnProp0, TeamSpawnProp1, TeamSpawnProp2, TeamSpawnProp3 };
        private static readonly HashSet<uint> spawnProps = new() { SpawnLocationProp, TeamSpawnProp0, TeamSpawnProp1, TeamSpawnProp2, TeamSpawnProp3 };

        /// <summary>A rule set's settings, resolved from the Ig* tables (see <see cref="LoadRules"/>).</summary>
        public class Rules
        {
            public uint Id;
            public uint NameId;
            public string Name = "";
            /// <summary>0 = free for all. IgRuleSets DEFAULT_TEAM_COUNT (column 3), unless the game's settings change it.</summary>
            public int TeamCount;
            /// <summary>
            /// IgRuleSets HAS_FFA_TEAM_BY_DEFAULT (column 4): 1 only for 2 Deathmatch and 7 FFA Zone Control, 0 for 5 All
            /// Scoring (agent-leads-sweep.md L97) [P table]. The free-for-all sets whose players fight each other.
            /// </summary>
            public bool HasFfaTeam;
            public int MinPlayers = 2, MaxPlayers = 16;
            public int TimeLimitSeconds = 600;
            public int PointLimit, TeamPointLimit;
            public int PointsPerKill = 1, PenaltyPerDeath, SelfKillPenalty, EnvironmentPenalty;
            public int RespawnDelaySeconds = 6;
            public int CountdownMs = 30000;
            /// <summary>
            /// Nobody is anybody's enemy: one team, or free for all without PvP Allowed (ApplyGeneral). The score bars
            /// show only with a score to reach; the Game Finder lists it as PvE. (Before the 0.695 settings this meant
            /// the Adventure table; its site story now follows the rule set, <see cref="PlaysStory"/>.)
            /// </summary>
            public bool Cooperative;
            /// <summary>The rule set's parameter values by IgRuleTypeParameters id (after IgRuleParameters overrides).</summary>
            public Dictionary<uint, int> Values = new();
            /// <summary>The parameters a Game Table's settings window offers, in order.</summary>
            public List<Parameter> Parameters = new();
            /// <summary>The rule set's IgRules rows: (rule index, rule type).</summary>
            public List<(uint Index, uint Type)> RuleRows = new();
            /// <summary>Difficulty tier shown by the walk-on window (IgGeneralSetting 10; 0 = none).</summary>
            public int Difficulty;
            /// <summary>Players may watch (IgGeneralSetting 8) / hit each other (11; never in cooperative games).</summary>
            public bool AllowsObservers = true, PvP = true;
            /// <summary>Zone control (rule type 11): control points (prop 1224) are taken by standing on them.</summary>
            public bool ZoneControl;
            public int PointsPerCapture, PointsPerTick;
            public float TickSeconds = 10f, CaptureSeconds = 5f;
            /// <summary>Rule type 1 "Time Limit" is on (0.695 Game Table: a game without it runs until a victory).</summary>
            public bool HasTimeLimit = true;
            /// <summary>
            /// Rule type 2 "Score On Enemy Kill" is on: kills of enemy players and of creatures score Points Per Kill
            /// (29 Sep 2016: "If you want killing other players or NPCs in the game to award points, add the Score on
            /// Enemy Kill setting").
            /// </summary>
            public bool KillScoring = true;
            /// <summary>
            /// Per server team (index 0 = the client's team 1): the general settings InstancedGames_TeamName1-4,
            /// InstancedGame_AllowPlayersTeam1-4 and InstancedGames_VictoryMessageTeam1-4 (RefIgGeneralSettingsTypes
            /// 14-16 / 21, 17-20, 25-28). Empty name / message = the client's default (<see cref="TeamName(Game, int)"/>).
            /// </summary>
            public string[] TeamNames = Array.Empty<string>(), VictoryMessages = Array.Empty<string>();
            public bool[] TeamOpen = Array.Empty<bool>();
            /// <summary>InstancedGames_AllowSwitch (12): players may change team while a round is on (before it, always).</summary>
            public bool AllowSwitch;
            /// <summary>InstancedGames_Min / MaxPlayersPerTeam (2 / 3) as set on the table.</summary>
            public int MinPerTeam = 1, MaxPerTeam = 16;
        }

        /// <summary>An IgRuleTypeParameters row a rule set uses: name text, range, and whether it's a yes/no.</summary>
        public record Parameter(uint Id, uint NameId, float Min, float Max, bool Flag);

        private class Player
        {
            public WorldSession Session;
            public int Team;
            /// <summary>Free-for-all side for control points (a number per player in the game).</summary>
            public int Slot;
            public int Kills, Deaths, Score;
            /// <summary>Dead, back at a spawn point at this time.</summary>
            public DateTime? RespawnAt;
            /// <summary>
            /// Being moved to a spawn point (a re-zone). Game state, HUD, team list, scoreboard and the revive wait until
            /// the client has finished it (<see cref="SyncArrived"/>).
            /// </summary>
            public bool Arriving;
            /// <summary>When the re-zone was seen finished; the player is synced a moment later.</summary>
            public DateTime? ArrivedAt;
        }

        /// <summary>A control point (prop 1224) in a zone control round.</summary>
        private class ControlPoint
        {
            public ulong Guid;
            public Vector3 Position;
            public float Radius = 5f;
            public string Name = "";
            /// <summary>The side holding it (team, or player slot in free-for-all); -1 nobody.</summary>
            public int Owner = -1;
            /// <summary>The side taking it and how far (0-1).</summary>
            public int Capturer = -1;
            public float Progress;
            /// <summary>
            /// While one side stands on it: when its capture would have started at a steady rate (now − Progress × Time to
            /// Capture) and the player shown taking it; null when nobody is taking it (PropInteraction).
            /// </summary>
            public DateTime? CaptureOrigin;
            public ulong CapturePlayer;
        }

        private const uint ControlPointProp = 1224;

        /// <summary>
        /// A control point's holder for an IF Gate (row 110 "Owning Team", 577709): the team number as the client counts
        /// them (1-based, 0 = nobody / free-for-all slot), from whichever running game has that point.
        /// </summary>
        public static int ControlPointOwner(ulong propGuid)
        {
            lock (sync)
                foreach (Game game in gamesByClaim.Values)
                    foreach (ControlPoint cp in game.Points)
                        if (cp.Guid == propGuid)
                            return cp.Owner < 0 ? 0 : game.Rules.TeamCount > 0 ? cp.Owner + 1 : 0;
            return 0;
        }

        private class Game
        {
            public ulong Guid;
            public uint PropId;
            public ulong ClaimId;
            public uint Zone;
            public Vector3 Position;
            public string CustomName = "";
            public Rules Rules;
            public uint State = StateIdle;
            public DateTime StateEnds;
            public DateTime RoundEnds;
            public int[] TeamScores = Array.Empty<int>();
            public int WinnerTeam = -1;
            public ulong WinnerPlayer;
            /// <summary>Winner banner text and when to (re)apply it (see <see cref="ShowBanner"/>).</summary>
            public string BannerText;
            public DateTime BannerAt, BannerAgainAt;
            public readonly List<Player> Players = new();
            public readonly List<ControlPoint> Points = new();
            /// <summary>Players watching without playing (ObserverRegistration).</summary>
            public readonly HashSet<WorldSession> Observers = new();
            public DateTime NextScoreTick, LastControlTick;
            public int NextSlot;
            /// <summary>The adventure was completed ("Give reward and teleport to a Chaos Cavern"): after the scoreboard
            /// its players go underground.</summary>
            public bool ToCavern;
            /// <summary>Hosting switched off in the game editor (Disable Game): no rounds, can't be joined.</summary>
            public bool Disabled;
            /// <summary>Players with the game editor open (authorship), and the recipe they last got.</summary>
            public readonly HashSet<WorldSession> Editors = new();
            public string EditorRecipe = "";
            /// <summary>Players waiting to join (InstancedGames.Queue.cs).</summary>
            public readonly List<Queued> Queue = new();
            /// <summary>
            /// What the score HUD's progress bars last showed everyone (<see cref="BarKey"/>): the HUD goes out again only
            /// when this changes (<see cref="RefreshBars"/>), never per tick.
            /// </summary>
            public string ShownBars = "";
            /// <summary>The site was in Play Mode before this round put it there (an Adventure table round): restored after.</summary>
            public bool WasPlaying;

            public bool CanJoin => !Disabled && State != StateVictory && State != StateFinishing && Players.Count < Rules.MaxPlayers;
            public Player Find(WorldSession session) => Players.FirstOrDefault(p => p.Session == session);
        }

        private const uint AdventureRuleSet = 8;
        private static readonly Dictionary<ulong, Game> gamesByClaim = new();
        private static Dictionary<uint, uint> placementProps;   // controller prop → rule set
        private static Dictionary<uint, Rules> rules;
        private static DateTime nextSync = DateTime.MinValue;

        #region rules

        /// <summary>Is this prop a game table (a site game's controller, IgRuleSetPlacementProps)?</summary>
        public static bool IsGameTable(uint propId)
        {
            EnsureLoaded();
            return placementProps.ContainsKey(propId);
        }

        private static void EnsureLoaded()
        {
            if (rules != null)
                return;
            placementProps = GameDataFiles.Rows("IgRuleSetPlacementProps.txt").ToDictionary(r => r.U(1), r => r.U(2));
            rules = LoadRules();
            log.Info($"Site games: {placementProps.Count} game tables, {rules.Count} rule sets.");
        }

        /// <summary>
        /// Rule sets from the client tables: IgRuleSets (name, team count, players per team), IgGeneralSetting (setting
        /// 2 min players, 3 max players, 4 countdown ms) and the rules' parameters: IgRules lists a set's rule types,
        /// IgRuleTypeParameters their parameters with defaults, and IgRuleParameters overrides them per rule, in the
        /// same order as the type's parameters (row ids ascending).
        /// </summary>
        /// <summary>IgRuleTypeParameters rows by rule type (ids ascending = the type's parameter order).</summary>
        private static Dictionary<uint, List<(uint Id, uint Type, uint NameId, string Min, string Max, string Default)>> typeParamRows;
        /// <summary>IgRuleParameters override values by IgRules row, in the type's parameter order.</summary>
        private static Dictionary<uint, List<string>> ruleOverrides;

        private static Dictionary<uint, Rules> LoadRules()
        {
            typeParamRows = GameDataFiles.Rows("IgRuleTypeParameters.txt")
                .Select(r => (Id: r.U(0), Type: r.U(1), NameId: r.U(3), Min: r.S(4), Max: r.S(5), Default: r.S(6)))
                .GroupBy(p => p.Type).ToDictionary(g => g.Key, g => g.OrderBy(p => p.Id).ToList());
            ruleOverrides = GameDataFiles.Rows("IgRuleParameters.txt")
                .Select(r => (Id: r.U(0), Rule: r.U(1), Value: r.S(2)))
                .GroupBy(o => o.Rule).ToDictionary(g => g.Key, g => g.OrderBy(o => o.Id).Select(o => o.Value).ToList());
            var ruleRows = GameDataFiles.Rows("IgRules.txt").Select(r => (Id: r.U(0), Set: r.U(1), Type: r.U(2))).ToList();

            var result = new Dictionary<uint, Rules>();
            foreach (string[] row in GameDataFiles.Rows("IgRuleSets.txt"))
            {
                var set = new Rules { Id = row.U(0), NameId = row.U(1), TeamCount = row.I(3), HasFfaTeam = row.I(4) != 0 };
                set.Name = Shared.GameTable.Text.TextManager.Instance.GetTextForId(set.NameId) ?? $"Game {set.Id}";
                set.RuleRows = ruleRows.Where(r => r.Set == set.Id).Select(r => (r.Id, r.Type)).ToList();
                set.Values = ValuesFor(set.RuleRows);
                // The 0.589-style settings window (TableSettings) offers the parameters of the set's own rules.
                foreach ((uint _, uint type) in set.RuleRows)
                    foreach (var p in typeParamRows.GetValueOrDefault(type) ?? new())
                    {
                        bool flag = bool.TryParse(p.Default, out _);
                        float.TryParse(p.Min, NumberStyles.Float, CultureInfo.InvariantCulture, out float min);
                        float.TryParse(p.Max, NumberStyles.Float, CultureInfo.InvariantCulture, out float max);
                        if (p.NameId != 0 && set.Parameters.All(x => x.Id != p.Id))
                            set.Parameters.Add(new Parameter(p.Id, p.NameId, flag ? 0 : min, flag ? 1 : max, flag));
                    }
                ApplyValues(set);
                // The general settings' defaults (IgGeneralSetting): teams, players, observers, PvP, names...
                Func<uint, string> defaults = id => GeneralSettings().FirstOrDefault(g => g.Set == set.Id && g.SettingId == id)?.Default;
                ApplyGeneral(set, defaults);
                ApplyScoreVictorySetting(set, defaults);
                result[set.Id] = set;
            }
            return result;
        }

        /// <summary>
        /// Parameter values (by IgRuleTypeParameters id) of a list of rules: each rule type's defaults, overridden per
        /// IgRules row by IgRuleParameters (same order as the type's parameters). Rows the server made up
        /// (<see cref="SyntheticRuleBase"/>) have no overrides.
        /// </summary>
        private static Dictionary<uint, int> ValuesFor(IEnumerable<(uint Index, uint Type)> rows)
        {
            var values = new Dictionary<uint, int>();
            foreach ((uint index, uint type) in rows)
            {
                if (!typeParamRows.TryGetValue(type, out var parameters))
                    continue;
                ruleOverrides.TryGetValue(index, out var own);
                for (int i = 0; i < parameters.Count; i++)
                {
                    string text = index < SyntheticRuleBase && own != null && i < own.Count && own[i].Length > 0 ? own[i] : parameters[i].Default;
                    if (int.TryParse(text, out int v))
                        values[parameters[i].Id] = v;
                    else if (bool.TryParse(text, out bool b))
                        values[parameters[i].Id] = b ? 1 : 0;
                }
            }
            return values;
        }

        /// <summary>The rule fields from the parameter values (IgRuleTypeParameters ids) and the rules present.</summary>
        private static void ApplyValues(Rules set)
        {
            int Value(uint param, int fallback) => set.Values.TryGetValue(param, out int v) ? v : fallback;
            bool Has(uint type) => set.RuleRows.Any(r => r.Type == type);
            set.HasTimeLimit = Has(1);
            set.KillScoring = Has(2);
            set.ZoneControl = Has(11);
            set.TimeLimitSeconds = Math.Max(10, Value(1, 600));
            set.PointsPerKill = set.KillScoring ? Value(2, 1) : 0;
            set.PenaltyPerDeath = Value(3, 0);
            set.SelfKillPenalty = Value(6, 0);
            set.EnvironmentPenalty = Value(7, 0);
            set.PointLimit = Value(10, 0);
            set.TeamPointLimit = Value(11, 0);
            // Respawn delay: type 12 (team games) or 13.
            set.RespawnDelaySeconds = Value(19, Value(20, 6));
            // Rule type 11: 14 Points Per Capture, 15 Points Per Score Tick, 16 Score Tick Interval, 23 Time to Capture
            // (21/22 "Control Point is Spawner" / its spawn delay: not used).
            set.PointsPerCapture = Value(14, 0);
            set.PointsPerTick = Value(15, 0);
            set.TickSeconds = Math.Max(1, Value(16, 10));
            set.CaptureSeconds = Math.Max(1, Value(23, 5));
        }

        /// <summary>The settings-window key of a rule parameter on a Game Table.</summary>
        public static string ParameterKey(uint parameterId) => $"ig_rule_param_{parameterId}";

        /// <summary>
        /// A Game Table's settings (its prop settings window): the rule set's parameters with their names and ranges from
        /// IgRuleTypeParameters, defaulting to the rule set's values. Empty for other props.
        /// </summary>
        public static List<PropSettings.Definition> TableSettings(uint propId)
        {
            EnsureLoaded();
            // The 0.695 Score Node's one setting (Point Amount, row 109; see InstancedGames.Rework.cs).
            if (propId == ScoreNodeProp)
                return ScoreNodeSettings();
            if (!placementProps.TryGetValue(propId, out uint setId) || !rules.TryGetValue(setId, out Rules set))
                return new List<PropSettings.Definition>();
            return set.Parameters.Select(p => new PropSettings.Definition(10000 + p.Id, ParameterKey(p.Id), p.NameId, 0,
                set.Values.TryGetValue(p.Id, out int v) ? v : 0, p.Max, p.Min, p.Flag ? 1 : Math.Max(1, (p.Max - p.Min) >= 600 ? 30 : 1))).ToList();
        }

        /// <summary>
        /// The rule set with this table's own settings applied (a copy): the general settings saved in the game editor
        /// (<see cref="ApplyGeneral"/>), the rules switched on / off there (<see cref="ActiveRuleRows"/>) and their
        /// parameters. The running round keeps the copy it started with (<see cref="StartRound"/>); outside a round the
        /// game follows every change (<see cref="SettingsChanged"/>).
        /// </summary>
        private static Rules RulesFor(Game game)
        {
            Rules baseSet = rules[placementProps[game.PropId]];
            var own = new Rules
            {
                Id = baseSet.Id, NameId = baseSet.NameId, Name = baseSet.Name, TeamCount = baseSet.TeamCount, HasFfaTeam = baseSet.HasFfaTeam,
                CountdownMs = baseSet.CountdownMs, Parameters = baseSet.Parameters
            };
            ApplyGeneral(own, id => GeneralSettings().FirstOrDefault(g => g.Set == baseSet.Id && g.SettingId == id) is GeneralSetting g
                ? GeneralValue(game, g) : null);
            own.RuleRows = ActiveRuleRows(game, baseSet, own.TeamCount);
            own.Values = ValuesFor(own.RuleRows);
            foreach ((uint _, uint type) in own.RuleRows)
                foreach (var p in typeParamRows.GetValueOrDefault(type) ?? new())
                    if (PropSettings.TryGetSaved(game.Guid, ParameterKey(p.Id), out float v))
                        own.Values[p.Id] = (int)v;
            ApplyValues(own);
            ApplyScoreVictorySetting(own, id => GeneralSettings().FirstOrDefault(g => g.Set == baseSet.Id && g.SettingId == id) is GeneralSetting g
                ? GeneralValue(game, g) : null);
            return own;
        }

        /// <summary>Dev: the rule sets as the server resolved them.</summary>
        public static string DescribeRules()
        {
            EnsureLoaded();
            return string.Join("; ", rules.Values.Select(r => $"{r.Id} {r.Name}: teams {r.TeamCount}, players {r.MinPlayers}-{r.MaxPlayers}, "
                + $"{(r.HasTimeLimit ? r.TimeLimitSeconds + "s" : "no time limit")}, points {r.PointLimit}/{r.TeamPointLimit}, kill {r.PointsPerKill} death {r.PenaltyPerDeath} "
                + $"self {r.SelfKillPenalty}, respawn {r.RespawnDelaySeconds}s, countdown {r.CountdownMs}ms, PvP {r.PvP}, "
                + $"rules {string.Join(",", r.RuleRows.Select(x => x.Type))}"));
        }

        #endregion

        #region site record

        /// <summary>Does the site have a game object (refuses deleting it: Claims.Error.AttemptToDeleteClaimWithActiveInstancedGame)?</summary>
        public static bool HasGame(ulong claimId) => GameOn(claimId) != null;

        private static Game GameOn(ulong claimId)
        {
            lock (sync)
                return gamesByClaim.TryGetValue(claimId, out Game game) ? game : null;
        }

        /// <summary>
        /// The site's game in the claim record and ClaimInfoUpdate (reader FUN_142cffe50 → claim entry +0x140, read by the
        /// walk-on window FUN_142d2b3f0): u64 game (0 = none), u32 state, bool can join, u32 queue size, u32 min players,
        /// u32 current players, u32 difficulty (shown as "Difficulty Tier n" when not 0), u32 max players, bool,
        /// name {u32 text id, string (used when not empty)}, restriction tags {u32 count; u32, string}.
        /// </summary>
        public static void WriteInfo(GamePacketWriter writer, ulong claimId)
        {
            Game game = GameOn(claimId);
            lock (sync)
            {
                writer.Write(game?.Guid ?? 0ul);
                writer.Write(game?.State ?? StateNone);
                writer.Write(game?.CanJoin ?? false);
                writer.Write((uint)(game?.Queue.Count ?? 0));
                writer.Write((uint)(game?.Rules.MinPlayers ?? 0));
                writer.Write((uint)(game?.Players.Count ?? 0));
                writer.Write((uint)(game?.Rules.Difficulty ?? 0));
                writer.Write((uint)(game?.Rules.MaxPlayers ?? 0));
                writer.Write(false);
                writer.Write(game?.Rules.NameId ?? 0u);
                writer.Write(game != null ? NameOf(game) : "");
                writer.Write(0u);
                // 0.695 (FUN_142d9fc20): one more u32 (entry +0x15c), the game's team count. The walk-on fill (part_0069
                // ~20740) reads the entry as +0x148 state, +0x154 max, +0x158 min, +0x15c, +0x160 current, +0x164
                // difficulty, and the walk-on window FUN_142dc9660 offers "Switch Team" (CanSwitchTeam) only when +0x15c
                // is ≥ 2, the player is in the game and not queued (and, from state 6 on, with InstancedGames_AllowSwitch)
                // — exactly IgRuleSets DEFAULT_TEAM_COUNT's values: 2 for the team sets 3 / 4 / 6, 0 for the free-for-all
                // 2 / 5 / 7, 1 for the Adventure 8 (agent-leads-sweep.md L37) [I strong]. The game's own count (its
                // settings may change it), not just the rule set's default.
                writer.Write((uint)Math.Max(0, game?.Rules.TeamCount ?? 0));
            }
        }

        #endregion

        #region tick

        /// <summary>Server tick: follows the placed Game Tables and runs each game's clock.</summary>
        public static void Update()
        {
            EnsureLoaded();
            DateTime now = DateTime.UtcNow;
            List<Action> later;
            lock (sync)
            {
                later = deferred.ToList();
                deferred.Clear();
            }
            foreach (Action action in later)
                action();
            List<Game> banners;
            lock (sync)
                banners = gamesByClaim.Values.Where(g => g.BannerText != null && (now >= g.BannerAt || now >= g.BannerAgainAt)).ToList();
            foreach (Game game in banners)
                ShowBanner(game, now);
            List<WorldSession> due;
            lock (sync)
            {
                due = cavernTrips.Where(t => now >= t.Due).Select(t => t.Session).ToList();
                cavernTrips.RemoveAll(t => now >= t.Due);
            }
            foreach (WorldSession session in due)
                if (PlayerManager.Snapshot().Contains(session))
                    Caves.TravelRandom(session);
            if (now >= nextSync)
            {
                nextSync = now + SyncInterval;
                SyncWithProps();
                List<Game> edited;
                lock (sync)
                    edited = gamesByClaim.Values.Where(g => g.Editors.Count > 0).ToList();
                foreach (Game game in edited)
                    RefreshEditors(game);
                List<Game> queued;
                lock (sync)
                    queued = gamesByClaim.Values.Where(g => g.Queue.Count > 0).ToList();
                foreach (Game game in queued)
                    TickQueue(game, now);
                TickFinder();
            }
            List<Game> games;
            lock (sync)
                games = gamesByClaim.Values.ToList();
            foreach (Game game in games)
                Tick(game, now);
        }

        /// <summary>One game per build site: the first Game Table on it. Tables picked up end their game.</summary>
        private static void SyncWithProps()
        {
            var tables = Props.PlacedAll(placementProps.Keys.ToHashSet()).OrderBy(t => t.Guid).ToList();
            var wanted = new Dictionary<ulong, (ulong Guid, uint PropId, uint Zone, Vector3 Position, string Name)>();
            foreach (var table in tables)
                if (Claims.ClaimAt(table.Zone, table.Position) is ulong claim && !wanted.ContainsKey(claim))
                    wanted[claim] = table;

            var changed = new List<ulong>();
            var ended = new List<Game>();
            lock (sync)
            {
                foreach ((ulong claim, Game game) in gamesByClaim.ToList())
                    if (!wanted.TryGetValue(claim, out var table) || table.Guid != game.Guid)
                    {
                        gamesByClaim.Remove(claim);
                        ended.Add(game);
                        changed.Add(claim);
                    }
                foreach ((ulong claim, var table) in wanted)
                {
                    if (gamesByClaim.TryGetValue(claim, out Game existing))
                    {
                        if (existing.CustomName != table.Name)
                        {
                            existing.CustomName = table.Name;
                            changed.Add(claim);
                        }
                        continue;
                    }
                    if (!rules.TryGetValue(placementProps[table.PropId], out Rules set))
                        continue;
                    var created = new Game
                    {
                        Guid = table.Guid,
                        PropId = table.PropId,
                        ClaimId = claim,
                        Zone = table.Zone,
                        Position = table.Position,
                        CustomName = table.Name,
                        Rules = set,
                        Disabled = PropSettings.TryGetSaved(table.Guid, DisabledKey, out float off) && off != 0
                    };
                    // The table's own settings (teams, players, rules) from the start: the walk-on window and the
                    // Game Finder show them before any round.
                    created.Rules = RulesFor(created);
                    created.TeamScores = new int[Math.Max(1, created.Rules.TeamCount)];
                    gamesByClaim[claim] = created;
                    changed.Add(claim);
                    log.Info($"Site {claim} hosts a {set.Name} game (table 0x{table.Guid:X}, {created.Rules.TeamCount} team(s)).");
                }
            }
            foreach (Game game in ended)
            {
                List<WorldSession> editors, watching;
                lock (sync)
                {
                    editors = game.Editors.ToList();
                    watching = game.Observers.ToList();
                    game.Observers.Clear();
                }
                foreach (WorldSession editor in editors)
                    SendRecipe(editor, null, false);
                foreach (Player player in game.Players.ToList())
                    RemovePlayer(game, player, "the game table was removed");
                // Observers of a game that is gone: out of observer mode (they kept the dialog, chat channel 0x11 and
                // the observer camera before, L176).
                foreach (WorldSession observer in watching.Where(o => !o.Disconnected))
                    EndObservation(observer, game);
                log.Info($"The {game.Rules.Name} game on site {game.ClaimId} ended (table gone).");
            }
            foreach (ulong claim in changed.Distinct())
                Claims.SendGameInfo(claim);
        }

        private static void Tick(Game game, DateTime now)
        {
            // Players who went to another landscape are out of the game.
            List<Player> gone;
            lock (sync)
                gone = game.Players.Where(p => p.Session.ZoneId != game.Zone).ToList();
            foreach (Player player in gone)
                RemovePlayer(game, player, "left the landscape");

            List<Player> respawns;
            lock (sync)
                respawns = game.State == StateRunning
                    ? game.Players.Where(p => p.RespawnAt is DateTime at && at <= now).ToList()
                    : new List<Player>();
            foreach (Player player in respawns)
                Respawn(game, player);
            SyncArrived(game, now);

            switch (game.State)
            {
                case StateIdle:
                case StateStarted:
                    // NPCs count toward the minimum players to start, not the maximum (dev reply, 29 Sep 2016; TASKS 3.21):
                    // the site's living story creatures fill missing places, at least one real player needed.
                    if (game.Players.Count > 0 && game.Players.Count + StoryTools.LiveCreaturesOn(game.ClaimId) >= game.Rules.MinPlayers)
                        BeginCountdown(game, now);
                    else if (game.State == StateIdle && game.Players.Count > 0)
                        SetState(game, StateStarted);
                    else if (game.State == StateStarted && game.Players.Count == 0)
                        SetState(game, StateIdle);
                    break;
                case StateWaitingForReady:
                    if (game.Players.Count == 0 || game.Players.Count + StoryTools.LiveCreaturesOn(game.ClaimId) < game.Rules.MinPlayers)
                        SetState(game, game.Players.Count > 0 ? StateStarted : StateIdle);
                    else if (now >= game.StateEnds)
                        StartRound(game, now);
                    break;
                case StateRunning:
                    if (game.Players.Count == 0)
                        SetState(game, StateIdle);
                    else if (game.Rules.HasTimeLimit && now >= game.RoundEnds)
                        EndRound(game, now, "time is up");
                    else if (game.Rules.ZoneControl)
                        TickControlPoints(game, now);
                    break;
                case StateVictory:
                    if (now >= game.StateEnds)
                    {
                        game.StateEnds = now + FinishingTime;
                        SetState(game, StateFinishing);
                        if (game.ToCavern)
                            SendToCaverns(game, now);
                    }
                    break;
                case StateFinishing:
                    if (now >= game.StateEnds)
                    {
                        ResetScores(game);
                        SetState(game, game.Players.Count > 0 ? StateStarted : StateIdle);
                        SendScoreboard(game);
                    }
                    break;
            }
        }

        private static void BeginCountdown(Game game, DateTime now)
        {
            game.StateEnds = now.AddMilliseconds(game.Rules.CountdownMs);
            SetState(game, StateWaitingForReady);
            Broadcast(game, $"{Title(game)} starts in {game.Rules.CountdownMs / 1000} seconds.");
        }

        /// <summary>Dev "gamestart": skips the wait for players and the countdown.</summary>
        public static void DevStart(WorldSession session)
        {
            Game game = GameOf(session) ?? GameAt(session);
            if (game == null)
            {
                Chat.System(session, "No site game here.");
                return;
            }
            StartRound(game, DateTime.UtcNow);
        }

        /// <summary>
        /// Dev "gamescore n": adds points to the player (and their team) the way a Score Node's "...Add points to the
        /// triggering Luminary's score." does (<see cref="GrantPlayerPoints"/>: point-grant chat line, scoreboard, victory
        /// check).
        /// </summary>
        public static void DevScore(WorldSession session, int points)
        {
            Game game = GameOf(session);
            Player player = game?.Find(session);
            if (player == null)
                return;
            GrantPlayerPoints(game, player, points, 0);
        }

        /// <summary>Dev "gameend": ends the round now.</summary>
        public static void DevEnd(WorldSession session)
        {
            Game game = GameOf(session) ?? GameAt(session);
            if (game != null && game.State == StateRunning)
                EndRound(game, DateTime.UtcNow, "ended by a developer");
        }

        private static void StartRound(Game game, DateTime now)
        {
            // The table's settings (its settings window) apply from this round on.
            game.Rules = RulesFor(game);
            FitTeams(game);
            ResetScores(game);
            game.RoundEnds = game.Rules.HasTimeLimit ? now.AddSeconds(game.Rules.TimeLimitSeconds) : DateTime.MaxValue;
            if (game.Rules.ZoneControl)
                SetUpControlPoints(game, now);
            // Moves first: the players being moved get the new state once they have arrived (SyncArrived).
            List<Player> players;
            lock (sync)
                players = game.Players.ToList();
            foreach (Player player in players)
            {
                WorldChannelServices.WorldChannel.TradePartyMoved(player.Session, intoGame: true);
                Respawn(game, player);
            }
            SetState(game, StateRunning);
            // An Adventure table (the 0.695 Game Table) plays the site's story from the start for each round: Links
            // (Score / Victory Nodes, Control Point "Attempt to control") run only in Play Mode (storytools-695.md §6.5).
            if (PlaysStory(game) && Claims.Get(game.ClaimId) is Database.Character.Model.ClaimModel site)
            {
                game.WasPlaying = Claims.IsPlaying(game.ClaimId);
                Claims.ApplyMode(site, play: true, restart: true);
            }
            // The table's "On Game Started" Links, on the story that has just (re)started; no triggering character [I].
            RaiseTableEvent(game, GameStartedEvent, null);
            SendScoreboard(game);
            Broadcast(game, $"{Title(game)} has begun! " + (game.Rules.PointLimit > 0 ? $"First to {game.Rules.PointLimit} points wins."
                : game.Rules.TeamPointLimit > 0 ? $"First team to {game.Rules.TeamPointLimit} points wins." : ""));
            log.Info($"{Title(game)} on site {game.ClaimId} started with {players.Count} player(s) ({(game.Rules.HasTimeLimit ? game.Rules.TimeLimitSeconds + " s" : "no time limit")}, "
                + $"point limit {game.Rules.PointLimit}/{game.Rules.TeamPointLimit}, respawn {game.Rules.RespawnDelaySeconds} s, teams {game.Rules.TeamCount}, PvP {game.Rules.PvP}).");
        }

        /// <summary>
        /// Ends the round. The winner is the best team / player by score, unless the caller names one: a Victory Node's
        /// "...Declare Victory for Team n / the Triggering Team." (<paramref name="forcedTeam"/>, <paramref
        /// name="forcedPlayer"/> in free for all) or a player reaching Player Score Victory's Point Limit in a team game
        /// (rule type 8: "Team wins when one character reaches score") — their team. The banner shows the team's
        /// Victory Message setting (InstancedGames_VictoryMessageTeam&lt;n&gt;, which the 0.695 scoreboard handler
        /// FUN_1434e4a40 also reads itself) when the table has one.
        /// </summary>
        private static void EndRound(Game game, DateTime now, string why, int? forcedTeam = null, Player forcedPlayer = null)
        {
            // From inside a story Link chain (a Victory / Score Node): on the next tick, so the Adventure table's Play
            // Mode switch (StoryTools.PlayModeChanged) doesn't stop the site's story under the chain that is running.
            if (inStoryChain)
            {
                lock (sync)
                    deferred.Add(() =>
                    {
                        if (game.State == StateRunning)
                            EndRound(game, DateTime.UtcNow, why, forcedTeam, forcedPlayer);
                    });
                return;
            }
            string winner;
            lock (sync)
            {
                if (game.Rules.TeamCount > 0)
                {
                    int best = game.TeamScores.Max();
                    var top = Enumerable.Range(0, game.TeamScores.Length).Where(t => game.TeamScores[t] == best).ToList();
                    game.WinnerTeam = forcedTeam is int ft && ft >= 0 && ft < game.Rules.TeamCount ? ft : top.Count == 1 ? top[0] : -1;
                    winner = game.WinnerTeam >= 0 ? $"{TeamName(game, game.WinnerTeam)} wins" : "It's a draw";
                    string message = game.WinnerTeam >= 0 ? game.Rules.VictoryMessages.ElementAtOrDefault(game.WinnerTeam) : null;
                    game.BannerText = game.WinnerTeam < 0 ? "Draw!" : !string.IsNullOrWhiteSpace(message) ? message : $"{TeamName(game, game.WinnerTeam)} Wins!";
                }
                else
                {
                    Player best = forcedPlayer != null && game.Players.Contains(forcedPlayer) ? forcedPlayer
                        : game.Players.OrderByDescending(p => p.Score).ThenBy(p => p.Deaths).FirstOrDefault();
                    game.WinnerPlayer = best?.Session.PlayerGuid ?? 0;
                    game.WinnerTeam = 0;
                    winner = best != null ? $"{best.Session.CharacterName} wins with {best.Score} point(s)" : "Nobody won";
                    game.BannerText = best != null ? $"{best.Session.CharacterName} Wins!" : "Game Over";
                }
                foreach (Player p in game.Players)
                    p.RespawnAt = null;
            }
            // Nobody stays dead through the scoreboard.
            foreach (Player p in game.Players.ToList())
                if (Health.IsDead(p.Session))
                    Health.Revive(p.Session);
            game.StateEnds = now + VictoryTime;
            game.BannerAt = now + BannerDelay;
            game.BannerAgainAt = now + BannerDelay * 2;
            // The final scoreboard (with the result) goes before the Victory state that opens the winner banner.
            SendScoreboard(game, final: true);
            SetState(game, StateVictory);
            // The table's "On Game Ended" Links (P6517), while the site's story still runs.
            RaiseGameEnded(game);
            // An Adventure table round ran the site's story (StartRound): Play Mode goes back to what it was.
            if (PlaysStory(game) && !game.WasPlaying && Claims.Get(game.ClaimId) is Database.Character.Model.ClaimModel site)
                Claims.ApplyMode(site, play: false);
            Broadcast(game, $"{Title(game)} is over ({why}). {winner}!");
            log.Info($"{Title(game)} on site {game.ClaimId} ended ({why}): {winner}.");
        }

        /// <summary>
        /// The winner banner's text. The client builds it from InstancedGames.VictoryMessage / GameOverMessage /
        /// DrawMessage and InstancedGame.Teams.DefaultName_t (scoreboard handler FUN_143434580 →
        /// GameData.InstancedGameScoreboard.ScoreboardBanner), none of which this client's StringHashToValue.txt has, so
        /// the banner read "Team 1" whatever happened. The UI script (ScriptsBase.bin) copies that data source into the
        /// banner movie with Invoke('setText', text); the server does the same with the real result once the banner is
        /// open (<see cref="BannerDelay"/> after the Victory state), and once more a moment later in case the window
        /// opened late.
        /// </summary>
        private static void ShowBanner(Game game, DateTime now)
        {
            string text;
            List<WorldSession> viewers;
            lock (sync)
            {
                text = game.BannerText;
                if (now >= game.BannerAgainAt)
                    game.BannerText = null;
                else
                    game.BannerAt = DateTime.MaxValue;
                viewers = game.Players.Select(p => p.Session).Concat(game.Observers).Distinct().ToList();
            }
            if (text == null)
                return;
            string code = $"local w = SoeGui.GetWindow('ScoreboardWinnerBannerWindow') if w then "
                + $"w:GetChild('GFxMovie'):Invoke('setText', {ClientLua.Quote(text)}) end";
            foreach (WorldSession session in viewers)
                if (!session.Disconnected)
                    ClientLua.RunCode(session, code);
        }

        private static readonly TimeSpan BannerDelay = TimeSpan.FromSeconds(1.2);

        private static void ResetScores(Game game)
        {
            lock (sync)
            {
                foreach (Player p in game.Players)
                    (p.Kills, p.Deaths, p.Score, p.RespawnAt) = (0, 0, 0, null);
                Array.Clear(game.TeamScores);
                game.WinnerTeam = -1;
                game.WinnerPlayer = 0;
            }
        }

        private static void SetState(Game game, uint state)
        {
            if (game.State == state)
                return;
            game.State = state;
            foreach (Player player in game.Players.ToList())
                if (!player.Arriving)
                {
                    SendState(player.Session, game);
                    SendHud(player.Session, game);
                }
            foreach (WorldSession watcher in Watchers(game))
            {
                SendState(watcher, game);
                SendHud(watcher, game);
            }
            lock (sync)
                game.ShownBars = BarKey(game);
            Claims.SendGameInfo(game.ClaimId);
        }

        #endregion

        #region players

        private static Game GameOf(WorldSession session)
        {
            lock (sync)
                return gamesByClaim.Values.FirstOrDefault(g => g.Find(session) != null);
        }

        /// <summary>The game of the site the player stands on.</summary>
        private static Game GameAt(WorldSession session)
            => Claims.ClaimAt(session.ZoneId, session.Position) is ulong claim ? GameOn(claim) : null;

        private static Game GameByGuid(ulong guid)
        {
            lock (sync)
                return gamesByClaim.Values.FirstOrDefault(g => g.Guid == guid);
        }

        /// <summary>The player's team in their site game as players see it (1-4), or 0 when not in a team game.</summary>
        public static int TeamNumberOf(WorldSession session)
        {
            Game game = GameOf(session);
            if (game == null || game.Rules.TeamCount == 0)
                return 0;
            lock (sync)
                return game.Find(session) is Player p ? p.Team + 1 : 0;
        }

        /// <summary>Is the player in a site game whose round is on?</summary>
        public static bool InRunningGame(WorldSession session) => GameOf(session)?.State == StateRunning;

        [ZoneMessageHandler(ZoneMessageOpcode.InstancedGameBase)]
        public static void Handle(WorldSession session, InstancedGameRequest request)
        {
            EnsureLoaded();
            if (request.SubOpcode == SubObserverRegistration && request.Body.Length >= 16)
            {
                var r = new GamePacketReader(request.Body);
                uint a = r.ReadUInt();
                ulong watched = r.ReadULong();
                uint flag = r.ReadUInt();
                Observe(session, flag == 1 ? GameByGuid(watched) ?? GameAt(session) : null, a);
                return;
            }
            if (HandleEditor(session, request.SubOpcode, request.Body) || HandleFinder(session, request.SubOpcode, request.Body)
                || HandleRework(session, request.SubOpcode, request.Body))
                return;
            if (request.SubOpcode != SubRequest)
            {
                log.Info($"{session.CharacterName}: instanced game sub {request.SubOpcode}: {BitConverter.ToString(request.Body)}");
                return;
            }
            // InstancedGameRequest (FUN_143427cf0): i16 type, i16 result, i16 ×3, u64 game, u32, string.
            var reader = new GamePacketReader(request.Body);
            short type = (short)reader.ReadUShort();
            reader.ReadUShort();
            short value = (short)reader.ReadUShort();
            reader.ReadUShort();
            reader.ReadUShort();
            ulong guid = reader.ReadULong();
            log.Info($"{session.CharacterName}: instanced game request {type} for 0x{guid:X}.");
            // A Game Finder offer of a game on another world (its queue entry's Join Now / Leave Queue; L31).
            if (HandleRemoteOffer(session, type, value, guid))
                return;
            switch (type)
            {
                case RequestJoin:
                    JoinGame(session, GameByGuid(guid) ?? GameAt(session));
                    break;
                case RequestLeave:
                    if (!Dequeue(session))
                        Leave(session);
                    break;
                case RequestMembership:
                    // The queue entry's buttons (GaqInstancedGameQueue → FUN_14332d370 / FUN_14332f5c0 →
                    // FUN_1434307f0 / FUN_143432a90): value 1 "Join Now" takes the offered place, 0 "Leave Queue".
                    if (value == 1)
                        JoinGame(session, (guid != 0 ? GameByGuid(guid) : null) ?? GameAt(session));
                    else
                        Dequeue(session);
                    break;
                case RequestRespawn:
                    RequestedRespawn(session);
                    break;
                case RequestGainAuthorship:
                    GainAuthorship(session, (guid != 0 ? GameByGuid(guid) : null) ?? GameAt(session));
                    break;
                case RequestLoseAuthorship:
                    LoseAuthorship(session);
                    break;
                case RequestJoinTeam:
                    SwitchTeam(session, (guid != 0 ? GameByGuid(guid) : null) ?? GameOf(session), value);
                    break;
            }
        }

        /// <summary>Joins the game of the site the player stands on (dev "gamejoin").</summary>
        public static void Join(WorldSession session) => JoinGame(session, GameAt(session));

        /// <summary>Joins a site game (walk-on Join button).</summary>
        private static void JoinGame(WorldSession session, Game game)
        {
            if (game == null)
            {
                Chat.System(session, "There is no game on this build site.");
                return;
            }
            Game current = GameOf(session);
            if (current == game)
                return;
            if (current != null)
                Leave(session);
            if (!game.CanJoin || (game.Queue.Count > 0 && !QueueTurn(game, session)))
            {
                if (game.Disabled)
                {
                    Chat.System(session, "That game is switched off by its owner.");
                    return;
                }
                // Full, or showing its scoreboard (or others are waiting first): wait in its queue.
                Enqueue(game, session);
                return;
            }
            Player player;
            lock (sync)
            {
                player = new Player { Session = session, Team = PickTeam(game), Slot = game.NextSlot++ };
                game.Players.Add(player);
            }
            SendMembership(session, game.Guid, true);
            // The 0.695 client keeps the game's settings (team names, PvPAllowed, AllowSwitch, joinable teams, victory
            // messages) from GetConfigurationResponse; it asks on entering the site, a player who joined from elsewhere
            // (Game Finder) may not have them yet.
            SendConfiguration(session, game);
            if (game.State == StateRunning)
                Respawn(game, player);
            else
                MoveToSpawn(game, player, start: true);
            if (!player.Arriving)
                SendState(session, game);
            SendTeamMembers(game);
            // Scoreboard before HUD: the client takes its own team (manager +0xa0, which the HUD's IsSelf bar needs)
            // from the scoreboard's player list (FUN_1434e4a40). The scoreboard re-sends the HUD to everyone when the
            // bars change; the newcomer gets it here otherwise.
            bool barsSent = SendScoreboard(game);
            if (!player.Arriving && !barsSent)
                SendHud(session, game);
            Claims.SendGameInfo(game.ClaimId);
            Broadcast(game, $"{session.CharacterName} joined {Title(game)}" + (game.Rules.TeamCount > 0 ? $" ({TeamName(game, player.Team)})." : "."));
            log.Info($"{session.CharacterName} joined {Title(game)} on site {game.ClaimId} ({game.Players.Count} player(s)).");
        }

        /// <summary>Leaves the player's game (walk-on Leave button, dev "gameleave", walking off the landscape, logout).</summary>
        public static void Leave(WorldSession session)
        {
            Game game = GameOf(session);
            Player player = game?.Find(session);
            if (player != null)
                RemovePlayer(game, player, "left");
        }

        /// <summary>Players leaving a completed adventure for a Chaos Cavern, and when they go.</summary>
        private static readonly List<(WorldSession Session, DateTime Due)> cavernTrips = new();

        /// <summary>
        /// A completed adventure's players leave the game and, a moment later (game packets must not share a burst with
        /// the re-zone), travel to a random Chaos Cavern (the underground caverns, <see cref="Caves.TravelRandom"/>).
        /// </summary>
        private static void SendToCaverns(Game game, DateTime now)
        {
            game.ToCavern = false;
            foreach (Player player in game.Players.ToList())
            {
                RemovePlayer(game, player, "off to a Chaos Cavern");
                lock (sync)
                    cavernTrips.Add((player.Session, now.AddSeconds(2)));
            }
        }

        private static void RemovePlayer(Game game, Player player, string why)
        {
            lock (sync)
                game.Players.Remove(player);
            WorldSession session = player.Session;
            if (Health.IsDead(session))
                Health.Revive(session);
            SendMembership(session, game.Guid, false);
            // State 0: the client puts the game HUD away (FUN_14343ff60 → FUN_14343bad0).
            SendState(session, game, StateNone);
            // The server's removal (L176): also takes the client out of the observer camera.
            SendRemoval(session, game.Guid);
            SendTeamMembers(game);
            SendScoreboard(game);
            Claims.SendGameInfo(game.ClaimId);
            Broadcast(game, $"{session.CharacterName} left {Title(game)}.");
            log.Info($"{session.CharacterName} left {Title(game)} on site {game.ClaimId} ({why}).");
        }

        /// <summary>The player left the world: out of any game.</summary>
        public static void Forget(WorldSession session)
        {
            Leave(session);
            Dequeue(session, tell: false);
            LoseAuthorship(session, tell: false);
            lock (sync)
                foreach (Game g in gamesByClaim.Values)
                    g.Observers.Remove(session);
        }

        /// <summary>
        /// ObserverRegistration 0x16 (0.589 0x15) {u32, u64 game, u32 flag} (Lua InstancedGame.EnterObserverMode / ExitObserverMode;
        /// the client also sends flag 0 on leaving a game). The server's echo (handler FUN_1434391f0) with flag 1 makes the
        /// client an observer of that game (manager +0x40) and opens GameEvents:OnOpenGameInProgressDialog; flag 0 closes
        /// it. Observers get the game's state, HUD and scoreboard (not the players' re-zones or hits).
        /// </summary>
        private static void Observe(WorldSession session, Game game, uint a)
        {
            Game was;
            lock (sync)
            {
                was = gamesByClaim.Values.FirstOrDefault(g => g.Observers.Contains(session));
                was?.Observers.Remove(session);
                if (game != null && game.Find(session) == null && game.Rules.AllowsObservers)
                    game.Observers.Add(session);
                else
                    game = null;
            }
            if (game == null && was == null)
                return;
            var output = new List<byte>();
            var w = Begin(output, SubObserverRegistration);
            w.Write(a);
            w.Write(game?.Guid ?? was.Guid);
            w.Write(game != null ? 1u : 0u);
            session.EnqueueMessage(ZoneMessageOpcode.InstancedGameBase, output.ToArray());
            if (game != null)
            {
                SendState(session, game);
                SendScoreboardTo(session, game);
                SendHud(session, game);
                log.Info($"{session.CharacterName} watches {Title(game)} on site {game.ClaimId}.");
            }
            else
            {
                SendState(session, was, StateNone);
                log.Info($"{session.CharacterName} stopped watching {Title(was)}.");
            }
        }

        /// <summary>
        /// The team a newcomer goes to: the emptiest team that allows players (InstancedGame_AllowPlayersTeam&lt;n&gt;;
        /// all of them if none does), then the lowest score.
        /// </summary>
        private static int PickTeam(Game game, Player except = null)
        {
            if (game.Rules.TeamCount <= 0)
                return 0;
            var open = Enumerable.Range(0, game.Rules.TeamCount).Where(t => TeamAllowsPlayers(game.Rules, t)).ToList();
            if (open.Count == 0)
                open = Enumerable.Range(0, game.Rules.TeamCount).ToList();
            return open.OrderBy(t => game.Players.Count(p => p != except && p.Team == t)).ThenBy(t => game.TeamScores.ElementAtOrDefault(t)).First();
        }

        private static bool TeamAllowsPlayers(Rules set, int team)
            => team >= 0 && team < set.TeamCount && (team >= set.TeamOpen.Length || set.TeamOpen[team]);

        /// <summary>
        /// A team's name as the 0.695 client names it (FUN_1434e7570, used for the scoreboard's team rows and the kill
        /// feed's sourceTeam / targetTeam): game setting InstancedGames_TeamName&lt;id&gt; if the game has one, else
        /// InstancedGame.Teams.DefaultName_&lt;id&gt; (CodeStringMappings: _1 = text 564623 "Team Cobalt", _2 = 564624 "Team
        /// Gold"; _0 = 564622 "FFA Team"), else "Team %d" — with id the client's number, team + 1 (<see cref="ClientSide"/>).
        /// The bars, chat lines and banner use the same names so they read like the client's own rows.
        /// </summary>
        private static string TeamName(int team) => (team + 1) switch { 1 => "Team Cobalt", 2 => "Team Gold", int id => $"Team {id}" };

        /// <summary>A team's name in this game: its InstancedGames_TeamName&lt;n&gt; setting when set (as the 0.695 client
        /// names it, FUN_1434e7570), else the default names above.</summary>
        private static string TeamName(Game game, int team)
        {
            string own = game?.Rules.TeamNames.ElementAtOrDefault(team);
            return !string.IsNullOrWhiteSpace(own) ? own : TeamName(team);
        }

        private static string Title(Game game) => NameOf(game) is { Length: > 0 } name ? name : game.Rules.Name;

        private static void Broadcast(Game game, string text)
        {
            foreach (Player player in game.Players.ToList())
                Chat.System(player.Session, text);
        }

        #endregion

        #region combat

        /// <summary>
        /// A weapon attack by a player in a running game: another living player of the game (an enemy in team games) is
        /// hit when they are the attack's target, lie on its aim ray, or stand at the swing's aim point. Returns true
        /// if someone was hit (the creature checks are skipped then).
        /// <para>Friendly fire (only with grief awards on, <see cref="Grief.Enabled"/>): in a team game a teammate who is
        /// the attack's target or the first player on its aim ray is hit too — never by the swing's area — and the
        /// damage earns the attacker grief (the client's warnings: "You have been firing on ally forces", Grief.cs),
        /// except in rule sets that score team kills (IgRuleTypes 3; 4 Traitors!, 5 All Scoring), where hitting
        /// teammates is part of the game. A teammate killed so is a team kill (<see cref="Died"/>, event 2) [I].</para>
        /// </summary>
        public static bool Hit(WorldSession attacker, ulong target, (Vector3 Origin, Vector3 Direction)? ray, Vector3 aim,
            uint damage, float range)
        {
            Game game = GameOf(attacker);
            // PvP follows the 0.695 client's hostility rule (FUN_142ea9630): other teams are enemies; in free for all
            // everyone is, when PvPAllowed is set (Rules.PvP, ApplyGeneral).
            if (game == null || game.State != StateRunning || Health.IsDead(attacker) || !game.Rules.PvP)
                return false;
            Player me = game.Find(attacker);
            List<Player> enemies, allies;
            bool friendlyFire = Grief.Enabled && game.Rules.TeamCount > 0;
            lock (sync)
            {
                var others = game.Players.Where(p => p != me && p.RespawnAt == null && !p.Arriving && !Health.IsDead(p.Session)
                    && p.Session.ZoneId == attacker.ZoneId).ToList();
                enemies = others.Where(p => game.Rules.TeamCount == 0 || p.Team != me.Team).ToList();
                allies = friendlyFire ? others.Where(p => p.Team == me.Team).ToList() : new List<Player>();
            }
            // Aimed hits (target, ray) can land on a teammate under friendly fire; the swing's area only on enemies.
            List<Player> aimable = allies.Count > 0 ? enemies.Concat(allies).ToList() : enemies;
            Player victim = null;
            if (target != 0)
                victim = aimable.FirstOrDefault(p => p.Session.PlayerGuid == target
                    && Vector3.Distance(p.Session.Position, attacker.Position) <= range);
            if (victim == null && ray is var (origin, direction))
            {
                float best = float.MaxValue;
                foreach (Player p in aimable)
                {
                    Vector3 centre = p.Session.Position + new Vector3(0f, 1f, 0f);
                    float t = Vector3.Dot(centre - origin, direction);
                    if (t <= 0 || t > range + 10f)
                        continue;
                    if (Vector3.Distance(origin + direction * t, centre) <= 1.2f && t < best)
                    {
                        best = t;
                        victim = p;
                    }
                }
            }
            if (victim == null)
                victim = enemies.Where(p => Flat(p.Session.Position, attacker.Position) <= 3.5f || Flat(p.Session.Position, aim) <= 2f)
                    .OrderBy(p => Flat(p.Session.Position, aim)).FirstOrDefault();
            if (victim == null)
                return false;
            Health.Damage(victim.Session, damage, attacker);
            if (allies.Contains(victim) && !game.Rules.RuleRows.Any(r => r.Type == Grief.TeamKillRuleType))
                Grief.AllyDamage(attacker, victim.Session, damage);
            // Floating damage numbers for both players (CombatResourceEvent, as for creature hits).
            Creatures.SendCombatEvent(attacker, attacker.PlayerGuid, victim.Session.PlayerGuid, damage);
            Creatures.SendCombatEvent(victim.Session, attacker.PlayerGuid, victim.Session.PlayerGuid, damage);
            return true;
        }

        private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

        /// <summary>
        /// A player died (Health.Damage). In a running game the death scores and the player respawns at a spawn point
        /// after the respawn delay; returns true then, so no death window is shown.
        /// </summary>
        public static bool Died(WorldSession victim, WorldSession killer)
        {
            Game game = GameOf(victim);
            if (game == null || game.State != StateRunning)
                return false;
            Player dead = game.Find(victim);
            Player by = killer != null && killer != victim ? game.Find(killer) : null;
            uint kind;
            lock (sync)
            {
                dead.Deaths++;
                dead.RespawnAt = DateTime.UtcNow.AddSeconds(game.Rules.RespawnDelaySeconds);
                if (by != null && (game.Rules.TeamCount == 0 || by.Team != dead.Team))
                {
                    kind = EventEnemyKill;
                    by.Kills++;
                    by.Score += game.Rules.PointsPerKill;
                    dead.Score += game.Rules.PenaltyPerDeath;
                    if (game.Rules.TeamCount > 0)
                        game.TeamScores[by.Team] += game.Rules.PointsPerKill;
                }
                else if (by != null)
                    kind = EventTeamKill;
                else
                {
                    kind = killer == victim ? EventSelfKill : EventEnvironmentKill;
                    dead.Score += kind == EventSelfKill ? game.Rules.SelfKillPenalty : game.Rules.EnvironmentPenalty;
                }
            }
            SendEvent(game, kind, by, dead);
            SendScoreboard(game);
            Chat.System(victim, $"You were {(by != null ? $"defeated by {by.Session.CharacterName}" : "defeated")}. Respawning in {game.Rules.RespawnDelaySeconds} seconds.");
            if (by != null)
                Chat.System(by.Session, $"You defeated {victim.CharacterName}.");
            log.Info($"{Title(game)}: {victim.CharacterName} died ({(by != null ? $"killed by {by.Session.CharacterName}" : "no killer")}).");
            CheckScoreVictory(game, DateTime.UtcNow);
            return true;
        }

        /// <summary>
        /// Ends a running round whose score reached a victory condition: Player Score Victory (rule type 8, Point Limit:
        /// "Team wins when one character reaches score" — in a team game the player's team wins) or Team Score Victory
        /// (rule type 9, Team Point Limit). True if the round ended.
        /// </summary>
        private static bool CheckScoreVictory(Game game, DateTime now)
        {
            if (game.State != StateRunning)
                return false;
            Player top;
            int? team = null;
            lock (sync)
            {
                top = game.Rules.PointLimit > 0
                    ? game.Players.Where(p => p.Score >= game.Rules.PointLimit).OrderByDescending(p => p.Score).FirstOrDefault() : null;
                if (top == null && game.Rules.TeamCount > 0 && game.Rules.TeamPointLimit > 0)
                {
                    int best = game.TeamScores.Max();
                    if (best >= game.Rules.TeamPointLimit)
                        team = Array.IndexOf(game.TeamScores, best);
                }
            }
            if (top == null && team == null)
                return false;
            if (top != null)
                EndRound(game, now, "point limit reached", game.Rules.TeamCount > 0 ? top.Team : null, game.Rules.TeamCount > 0 ? null : top);
            else
                EndRound(game, now, "team point limit reached", team);
            return true;
        }

        /// <summary>
        /// The Adventure on a site was completed ("...Give reward and teleport to a Chaos Cavern." on its Game Table): the
        /// round ends with everyone in it winning. (The Chaos Cavern trip isn't made.)
        /// </summary>
        public static void CompleteAdventure(ulong claimId)
        {
            Game game = GameOn(claimId);
            if (game == null || game.State != StateRunning || game.Rules.Id != AdventureRuleSet)
                return;
            lock (sync)
                foreach (Player p in game.Players)
                {
                    int points = game.Rules.PointLimit > 0 ? game.Rules.PointLimit : 10;
                    p.Score += points;
                    if (game.Rules.TeamCount > 0 && p.Team < game.TeamScores.Length)
                        game.TeamScores[p.Team] += points;
                }
            game.ToCavern = true;
            EndRound(game, DateTime.UtcNow, "adventure complete");
        }

        /// <summary>
        /// A player killed a creature: in a running game with Score On Enemy Kill (rule type 2) it scores Points Per Kill
        /// for the player and their team (kills of players or NPCs score only with the Score on Enemy Kill setting, 29
        /// Sep 2016). Whether a creature of the killer's own side would count is not
        /// known (story creatures have no game team here).
        /// </summary>
        public static void CreatureKilled(WorldSession killer)
        {
            Game game = killer == null ? null : GameOf(killer);
            Player p = game?.Find(killer);
            if (p == null || game.State != StateRunning || !game.Rules.KillScoring)
                return;
            lock (sync)
            {
                p.Kills++;
                p.Score += game.Rules.PointsPerKill;
                if (game.Rules.TeamCount > 0 && p.Team < game.TeamScores.Length)
                    game.TeamScores[p.Team] += game.Rules.PointsPerKill;
            }
            SendScoreboard(game);
            CheckScoreVictory(game, DateTime.UtcNow);
        }

        /// <summary>The site's control points (props 1224), all neutral, for a new round.</summary>
        private static void SetUpControlPoints(Game game, DateTime now)
        {
            var found = Props.PlacedOf(game.Zone, new HashSet<uint> { ControlPointProp })
                .Where(p => Claims.ClaimAt(game.Zone, p.Position) == game.ClaimId).OrderBy(p => p.Guid).ToList();
            lock (sync)
            {
                game.Points.Clear();
                for (int i = 0; i < found.Count; i++)
                    game.Points.Add(new ControlPoint
                    {
                        Guid = found[i].Guid,
                        Position = found[i].Position,
                        Radius = 5f * (Props.Find(found[i].Guid)?.Spawn.Scale.X ?? 1f),
                        Name = found[i].Name.Length > 0 ? found[i].Name : $"Point {(char)('A' + i % 26)}"
                    });
                game.NextScoreTick = now.AddSeconds(game.Rules.TickSeconds);
                game.LastControlTick = now;
            }
        }

        /// <summary>A player's side for control points: their team, or their own slot in a free-for-all.</summary>
        private static int SideOf(Game game, Player p) => game.Rules.TeamCount > 0 ? p.Team : p.Slot;

        /// <summary>
        /// Control points: a side standing alone on a point (within its radius, alive) takes it over the rule's Time to
        /// Capture; taking it scores Points Per Capture; every Score Tick Interval each point scores Points Per Score
        /// Tick for its holder. The HUD's control indicators follow (progress, holder).
        /// </summary>
        private static void TickControlPoints(Game game, DateTime now)
        {
            float dt = (float)Math.Clamp((now - game.LastControlTick).TotalSeconds, 0, 1);
            game.LastControlTick = now;
            bool hudChanged = false;
            var events = new List<(ControlPoint Point, int Side)>();
            lock (sync)
            {
                foreach (ControlPoint cp in game.Points)
                {
                    var sides = game.Players.Where(p => p.RespawnAt == null && !p.Arriving && !Health.IsDead(p.Session)
                            && Vector3.Distance(p.Session.Position, cp.Position) <= cp.Radius)
                        .Select(p => SideOf(game, p)).Distinct().ToList();
                    if (sides.Count == 1 && sides[0] != cp.Owner)
                    {
                        if (cp.Capturer != sides[0])
                        {
                            cp.Capturer = sides[0];
                            cp.Progress = 0f;
                            cp.CaptureOrigin = null;
                            hudChanged = true;
                        }
                        if (cp.CaptureOrigin == null)
                        {
                            cp.CaptureOrigin = now.AddSeconds(-cp.Progress * game.Rules.CaptureSeconds);
                            cp.CapturePlayer = game.Players.FirstOrDefault(p => SideOf(game, p) == sides[0]
                                && Vector3.Distance(p.Session.Position, cp.Position) <= cp.Radius)?.Session.PlayerGuid ?? 0ul;
                        }
                        // The HUD's control indicator follows in CaptureHudSteps steps (the overhead bar runs on the
                        // client's clock, PropInteraction): re-sending it every tick sent ~700 packets a second (2026-10-17).
                        int step = (int)(cp.Progress * CaptureHudSteps);
                        cp.Progress += dt / game.Rules.CaptureSeconds;
                        hudChanged |= (int)(cp.Progress * CaptureHudSteps) != step;
                        if (cp.Progress >= 1f)
                        {
                            cp.Owner = cp.Capturer;
                            cp.Capturer = -1;
                            cp.Progress = 0f;
                            cp.CaptureOrigin = null;
                            AddSideScore(game, cp.Owner, game.Rules.PointsPerCapture);
                            events.Add((cp, cp.Owner));
                        }
                    }
                    else if (cp.CaptureOrigin != null)
                    {
                        cp.CaptureOrigin = null;     // contested, left or held: the client's bar goes (PropInteraction)
                        hudChanged = true;
                    }
                    if (sides.Count == 0 && cp.Progress > 0f)
                    {
                        int step = (int)MathF.Ceiling(cp.Progress * CaptureHudSteps);
                        cp.Progress = Math.Max(0f, cp.Progress - dt / game.Rules.CaptureSeconds);
                        if (cp.Progress == 0f)
                            cp.Capturer = -1;
                        hudChanged |= (int)MathF.Ceiling(cp.Progress * CaptureHudSteps) != step;
                    }
                }
                if (now >= game.NextScoreTick)
                {
                    game.NextScoreTick = now.AddSeconds(game.Rules.TickSeconds);
                    foreach (ControlPoint cp in game.Points.Where(c => c.Owner >= 0))
                        AddSideScore(game, cp.Owner, game.Rules.PointsPerTick);
                    hudChanged |= game.Points.Any(c => c.Owner >= 0);
                }
            }
            foreach ((ControlPoint cp, int side) in events)
                AnnounceCapture(game, cp, side, game.Players.FirstOrDefault(p => SideOf(game, p) == side));
            if (hudChanged)
            {
                // Control indicators and score bars: players and observers (the bars' key is noted so the
                // scoreboard below does not send the HUD a second time).
                SendHudAll(game);
                if (events.Count > 0 || now.AddSeconds(game.Rules.TickSeconds) == game.NextScoreTick)
                    SendScoreboard(game);
            }
            CheckScoreVictory(game, now);
        }

        /// <summary>
        /// A control point changed hands: the ControlPtTaken event (type 9, kill-feed line 564493), a chat line, and the
        /// point's "When control point is captured..." Links (577708) with the taker as triggering character.
        /// </summary>
        private static void AnnounceCapture(Game game, ControlPoint cp, int side, Player taker)
        {
            var output = new List<byte>();
            var w = Begin(output, SubEvent);
            w.Write(9u);                             // InstancedGame.ControlPtTaken
            w.Write(game.Rules.TeamCount > 0 ? ClientSide(game, side) : taker != null ? ClientSide(game, taker.Team) : -1);
            w.Write(-1);
            w.Write(taker?.Session.PlayerGuid ?? 0ul);
            w.Write(cp.Guid);
            WriteEventTail(w, "", taker?.Session.CharacterName ?? "");
            byte[] data = output.ToArray();
            // Players and watchers: an observer's HUD (game state 9) loads the kill / status feed too (phaseE3-ui.md F1).
            List<WorldSession> viewers = game.Players.Where(p => !p.Arriving).Select(p => p.Session).Concat(Watchers(game)).Distinct().ToList();
            foreach (WorldSession viewer in viewers)
                viewer.EnqueueMessage(ZoneMessageOpcode.InstancedGameBase, data);
            Broadcast(game, $"{(game.Rules.TeamCount > 0 ? TeamName(game, side) : taker?.Session.CharacterName ?? "Someone")} took {cp.Name}.");
            log.Info($"{Title(game)}: {cp.Name} taken by side {side}.");
            StoryFire(cp.Guid, ControlPointCapturedEvent, taker?.Session);
        }

        /// <summary>Points for a side: the team's score (and its players'), or the free-for-all player's.</summary>
        private static void AddSideScore(Game game, int side, int points)
        {
            if (points == 0)
                return;
            if (game.Rules.TeamCount > 0)
            {
                if (side >= 0 && side < game.TeamScores.Length)
                    game.TeamScores[side] += points;
            }
            else if (game.Players.FirstOrDefault(p => p.Slot == side) is Player p)
                p.Score += points;
        }

        /// <summary>Dev "gamepoint": the zone control state.</summary>
        public static string DescribePoints(WorldSession session)
        {
            Game game = GameOf(session) ?? GameAt(session);
            if (game == null)
                return "No game here.";
            lock (sync)
                return string.Join("; ", game.Points.Select(c => $"{c.Name} owner {c.Owner} capturer {c.Capturer} {c.Progress:P0}"))
                    + $" | teams {string.Join("/", game.TeamScores)}";
        }

        /// <summary>RespawnInstancedGame: back now if the delay is over.</summary>
        private static void RequestedRespawn(WorldSession session)
        {
            Game game = GameOf(session);
            Player player = game?.Find(session);
            if (player?.RespawnAt is DateTime at && at <= DateTime.UtcNow)
                Respawn(game, player);
        }

        /// <summary>
        /// Back into the round at a spawn point. The move comes first and the revive (hit points) only after the client
        /// has finished the re-zone: both in one burst sometimes hung the client for good in QuerySimpleVisibility
        /// (FUN_144009a40, its visibility list looping), 2 of ~8 respawns on 2026-10-11, while plain re-zones never did.
        /// </summary>
        private static void Respawn(Game game, Player player)
        {
            player.RespawnAt = null;
            // Already on the chosen spawn point there is no move (no re-zone, so no "arrived at a spawn point" later):
            // the player is revived where they stand, and the log says which happened.
            bool moving = MoveToSpawn(game, player, start: false);
            if (!moving && (Health.IsDead(player.Session) || Health.Current(player.Session) < Health.Max(player.Session)))
                Health.Revive(player.Session);
            log.Info($"{Title(game)}: {player.Session.CharacterName} respawned{(moving ? "" : " in place")}.");
        }

        /// <summary>
        /// Moves a player to one of the game's spawn points on the site: their team's spawn props in team games
        /// (1223 / 1367), else spawn locations (1371) or any spawn prop; next to the Game Table when there are none.
        /// </summary>
        /// <summary>Dev "gametp off": respawn in place (no spawn teleports), for isolating client problems.</summary>
        public static bool SpawnTeleports { get; set; } = true;

        /// <summary>Dev "gamelabels off": no PropState / PropInteraction (control-point labels and bars), for isolating client problems.</summary>
        public static bool ControlLabels { get; set; } = true;

        /// <summary>Returns true when the player is being moved (a re-zone is under way).</summary>
        private static bool MoveToSpawn(Game game, Player player, bool start)
        {
            if (!SpawnTeleports)
                return false;
            // Joining while the round hasn't begun: a player already on the site stays where they are (every move is a
            // short re-zone with a loading screen).
            if (start && Claims.ClaimAt(player.Session.ZoneId, player.Session.Position) == game.ClaimId)
                return false;
            var spawns = Props.PlacedOf(game.Zone, spawnProps)
                .Where(s => Claims.ClaimAt(game.Zone, s.Position) == game.ClaimId).ToList();
            var choice = spawns;
            if (game.Rules.TeamCount > 0)
            {
                uint teamProp = teamSpawnProps[Math.Clamp(player.Team, 0, teamSpawnProps.Length - 1)];
                if (spawns.Any(s => s.PropId == teamProp))
                    choice = spawns.Where(s => s.PropId == teamProp).ToList();
            }
            else if (spawns.Any(s => s.PropId == SpawnLocationProp))
                choice = spawns.Where(s => s.PropId == SpawnLocationProp).ToList();
            Vector3 at;
            if (choice.Count > 0)
            {
                // The spawn point farthest from the other players.
                var others = game.Players.Where(p => p != player).Select(p => p.Session.Position).ToList();
                at = choice.Select(s => s.Position)
                    .OrderByDescending(pos => others.Count == 0 ? Random.Shared.NextSingle() : others.Min(o => Vector3.Distance(o, pos)))
                    .First();
            }
            else
            {
                float angle = Random.Shared.NextSingle() * MathF.Tau;
                at = game.Position + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * (3f + Random.Shared.NextSingle() * 5f);
            }
            if (player.Session.ZoneId != game.Zone)
                return false;
            // Already standing on that spawn point: no move (each one is a re-zone with a loading screen).
            if (Flat(player.Session.Position, at) < 1.5f)
                return false;
            Teleport.To(player.Session, at + new Vector3(0f, 0.5f, 0f));
            player.Arriving = true;
            player.ArrivedAt = null;
            return true;
        }

        /// <summary>
        /// Players whose spawn re-zone has finished (the client's ClientIsReady cleared the session's re-zone target)
        /// get, a second later, what was held back: revive, game state, HUD, team list and scoreboard. Any of those in
        /// the same burst as the re-zone could hang the client for good in QuerySimpleVisibility (FUN_144009a40: its
        /// visibility list loops), seen on 2026-10-11 for hit points (1 in 4 respawns) and for the start-of-round state
        /// (2 in 3 starts), while plain re-zones never hung (0 in 8).
        /// </summary>
        private static void SyncArrived(Game game, DateTime now)
        {
            List<Player> ready = new();
            lock (sync)
                foreach (Player p in game.Players.Where(p => p.Arriving))
                {
                    if (p.Session.RezoneTarget != null)
                        continue;
                    p.ArrivedAt ??= now;
                    if (now - p.ArrivedAt.Value >= ArrivalGrace)
                    {
                        p.Arriving = false;
                        ready.Add(p);
                    }
                }
            if (ready.Count == 0)
                return;
            foreach (Player p in ready)
            {
                if (game.State == StateRunning && (Health.IsDead(p.Session) || Health.Current(p.Session) < Health.Max(p.Session)))
                    Health.Revive(p.Session);
                SendState(p.Session, game);
                Claims.SendGameInfoTo(p.Session, game.ClaimId);
                log.Info($"{Title(game)}: {p.Session.CharacterName} arrived at a spawn point (state {game.State}, {Health.Current(p.Session)} hp).");
            }
            SendTeamMembers(game);
            // Scoreboard before HUD (the client's own team, manager +0xa0, comes from the scoreboard: FUN_1434e4a40).
            if (!SendScoreboard(game))
                foreach (Player p in ready)
                    SendHud(p.Session, game);
        }

        #endregion

        #region packets

        /// <summary>
        /// InstancedGamePlayerRemovalPacket 0x10D sub 0x14 (0.589 0x13; 0.695 dispatcher case 0x12, reader FUN_1434dbfb0,
        /// consumed exactly) [P]: u64 game (the handler doesn't read it). Handler FUN_1434e8cd0: the full reset
        /// FUN_1434e2330 (state 0 — closing the end-of-game scoreboard when it was 7 —, game +0x38 and observed game +0x40
        /// cleared, team members / scoreboard lists emptied; the same reset Membership value 0 runs, FUN_1434e93e0 type 6),
        /// then, if the camera is in mode 0x20 (the observer camera of Lua InstancedGame.EnterObserverMode,
        /// FUN_143604470), back to the previous camera (FUN_142d4eaa0(client, 0)). Its last block (leave chat channel
        /// 0x11, GameEvents:OnCloseGameInProgressDialog when +0x40 was set) never runs, as the reset has already cleared
        /// +0x40; ObserverRegistration flag 0 (FUN_1434ea130) is what does that. Sent after Membership 0 + State 0 when
        /// the server takes a player out of a game, and after ObserverRegistration 0 when it ends an observation.
        /// </summary>
        private static void SendRemoval(WorldSession session, ulong game)
            => session.EnqueueMessage(ZoneMessageOpcode.InstancedGameBase, RemovalBody(game));

        /// <summary>The removal's body: u32 sub 0x14, u64 game.</summary>
        internal static byte[] RemovalBody(ulong game)
        {
            var output = new List<byte>();
            Begin(output, SubPlayerRemoval).Write(game);
            return output.ToArray();
        }

        /// <summary>
        /// The server ends a player's observation: ObserverRegistration {u32 0, u64 game, u32 flag 0} (FUN_1434ea130: clears
        /// +0x40, leaves chat channel 0x11, closes the game-in-progress dialog), state 0, then the removal (observer camera off).
        /// </summary>
        private static void EndObservation(WorldSession session, Game game)
        {
            var output = new List<byte>();
            var w = Begin(output, SubObserverRegistration);
            w.Write(0u);
            w.Write(game.Guid);
            w.Write(0u);
            session.EnqueueMessage(ZoneMessageOpcode.InstancedGameBase, output.ToArray());
            SendState(session, game, StateNone);
            SendRemoval(session, game.Guid);
            log.Info($"{session.CharacterName} no longer watches {Title(game)} (the game ended).");
        }

        private static GamePacketWriter Begin(List<byte> output, uint sub)
        {
            var writer = new GamePacketWriter(output);
            writer.Write(sub);
            return writer;
        }

        /// <summary>
        /// Request sub 8 type 6 (HandleGameRequestPacket FUN_143438d00): i16 type, i16 result, i16 value, i16, i16,
        /// u64 game, u32, string. Value 1 = the player is in the game (Leave button), 0 = out of it.
        /// </summary>
        private static void SendMembership(WorldSession session, ulong game, bool inGame)
        {
            var output = new List<byte>();
            var w = Begin(output, SubRequest);
            w.Write((ushort)RequestMembership);
            w.Write((ushort)1);
            w.Write((ushort)(inGame ? 1 : 0));
            w.Write((ushort)0);
            w.Write((ushort)0);
            w.Write(game);
            w.Write(0u);
            w.Write("");
            session.EnqueueMessage(ZoneMessageOpcode.InstancedGameBase, output.ToArray());
        }

        /// <summary>
        /// State sub 6 (reader FUN_143427fe0, handler FUN_143438fe0): u32 state (manager +0x28), u32 next state (+0x2c),
        /// u32 duration ms (+0x48), u64 game. State 4 with next 6 starts the client's countdown clock (+0x98, +0x90).
        /// </summary>
        private static void SendState(WorldSession session, Game game, uint? state = null)
        {
            uint s = state ?? game.State;
            uint next = s == StateWaitingForReady ? StateRunning : 0;
            int ms = s == StateWaitingForReady ? (int)Math.Max(0, (game.StateEnds - DateTime.UtcNow).TotalMilliseconds) : 0;
            var output = new List<byte>();
            var w = Begin(output, SubState);
            w.Write(s);
            w.Write(next);
            w.Write((uint)ms);
            w.Write(game.Guid);
            session.EnqueueMessage(ZoneMessageOpcode.InstancedGameBase, output.ToArray());
        }

        /// <summary>
        /// A HUD entry: 0x50 bytes in 0.695 (FUN_1434d8180; 0x40 in 0.589, reader FUN_1434284b0). Wire order (struct
        /// offset): u32 text id (+0x00), string label (+0x08), u32 +0x24, u32 +0x28, u32 +0x2c, i32 value (+0x34: a control
        /// point's progress %, a progress bar's value), i32 colour (+0x30: palette index, -1 white; for team bars also the
        /// team the 0.695 client compares with its own), u32 +0x3c (a round timer's end time on the client's synced ms
        /// clock; a control point's "owned" flag), i32 +0x38 (a control point's holder colour), then the 0.695 tail
        /// (<see cref="WriteHudEntryTail"/>). The label the handler shows (FUN_143c1c7b0) is the string when non-empty,
        /// else the text id's text, else "Missing Text". Layout: trace/0695/hud-progress-695.md.
        /// </summary>
        private static void WriteHudEntry(GamePacketWriter w, string label, uint endTime = 0, int value = 0,
            int colour = 0, bool team = false, ulong player = 0)
        {
            w.Write(0u);                                 // +0x00 text id (none: the label string is used)
            w.Write(label);                              // +0x08
            w.Write(0u);                                 // +0x24
            w.Write(0u);                                 // +0x28
            w.Write(0u);                                 // +0x2c
            w.Write(value);                              // +0x34
            w.Write(colour);                             // +0x30
            w.Write(endTime);                            // +0x3c
            w.Write(0u);                                 // +0x38
            WriteHudEntryTail(w, team, player);
        }

        /// <summary>
        /// 0.695 HUD entries (FUN_1434d8180; not in 0.589 FUN_1434284b0) end with bool +0x40 and u64 +0x48. The HUD
        /// handler FUN_1434e2f70 uses them for the progress bars only: a team entry (+0x40 true) is the local player's
        /// own when its +0x30 equals the client's team (manager +0xa0, taken from the scoreboard by FUN_1434e4a40), a
        /// player entry when its +0x48 equals the local player's guid (BaseClient +0x32508; only while +0xa0 is set).
        /// </summary>
        private static void WriteHudEntryTail(GamePacketWriter w, bool team = false, ulong player = 0)
        {
            w.Write(team);
            w.Write(player);
        }

        /// <summary>
        /// The client's number for a side. Team games number their teams from 1: index 0 of the scoreboard's team list
        /// is the "FFA Team" slot (InstancedGame.Teams.DefaultName_0; _1 / _2 are "Team Cobalt" / "Team Gold"), which the
        /// scoreboard handler FUN_1434e4a40 never shows as a team row, and the result +0xc0 names the winning row. So
        /// server team t is t + 1 wherever the client compares it with its own team or colours by it (palette in
        /// FUN_1434e2f70 / FUN_1434e4a40: index % 11 of AF7FAF, 0034f8, ff0000, D4AB1F, 00cc00, FF7F7F, 7F7FFF, 7FFF7F,
        /// FFFF7F, FF7FFF, 7FFFFF; -1 FFFFFF). Free-for-all sides (player slots) and -1 (nobody) are sent as they are.
        /// </summary>
        private static int ClientSide(Game game, int side) => side < 0 ? -1 : game.Rules.TeamCount > 0 ? side + 1 : side;

        /// <summary>One score-HUD progress bar: label, value, colour (palette index), team entry or the player's guid.</summary>
        private readonly record struct Bar(string Label, int Value, int Colour, bool Team, ulong Player);

        /// <summary>
        /// The score bars show while a round is on and through its end screens: in competitive games, and in cooperative
        /// ones (one team, or free for all without PvP) that have a score to reach.
        /// </summary>
        private static bool BarsShown(Game game) => (!game.Rules.Cooperative || ScoreGoal(game) > 0)
            && (game.State == StateRunning || game.State == StateVictory || game.State == StateFinishing);

        /// <summary>The score the bars fill towards: the rule set's team / player point limit, 0 = none.</summary>
        private static int ScoreGoal(Game game) => game.Rules.TeamCount > 0 ? game.Rules.TeamPointLimit : game.Rules.PointLimit;

        /// <summary>
        /// The bar entries for one viewer, best first: every team in team games; in free for all the leader and the
        /// runner-up, plus the viewer when neither. The 0.695 client picks two of them itself (FUN_1434e2f70): the
        /// viewer's own (IsSelf, arrow) and the best other one, the higher value on top. With no own team known (an
        /// observer, manager +0xa0 = -1) it keeps the two best in a single pass that only works on a best-first list.
        /// </summary>
        private static List<Bar> Bars(Game game, WorldSession viewer)
        {
            lock (sync)
            {
                if (game.Rules.TeamCount > 0)
                    return Enumerable.Range(0, game.TeamScores.Length)
                        .OrderByDescending(t => game.TeamScores[t]).ThenBy(t => t)
                        .Select(t => new Bar(TeamName(game, t), game.TeamScores[t], ClientSide(game, t), true, 0)).ToList();
                List<Player> shown = game.Players.OrderByDescending(p => p.Score).ThenBy(p => p.Slot).Take(2).ToList();
                Player me = viewer != null ? game.Find(viewer) : null;
                if (me != null && !shown.Contains(me))
                    shown.Add(me);
                return shown.Select(p => new Bar(p.Session.CharacterName ?? "", p.Score, p.Slot, false, p.Session.PlayerGuid)).ToList();
            }
        }

        /// <summary>
        /// What the bars show (empty when hidden): the state and every score they are built from. The HUD is re-sent
        /// when this changes (<see cref="RefreshBars"/>), i.e. per score change, never per tick.
        /// </summary>
        private static string BarKey(Game game)
        {
            if (!BarsShown(game))
                return "";
            lock (sync)
                return game.Rules.TeamCount > 0
                    ? $"{game.State}|{ScoreGoal(game)}|{string.Join(",", game.TeamScores)}"
                    : $"{game.State}|{ScoreGoal(game)}|{string.Join(",", game.Players.Select(p => $"{p.Session.PlayerGuid:X}:{p.Score}"))}";
        }

        /// <summary>The HUD to every player in place and every observer; notes what the bars now show.</summary>
        private static void SendHudAll(Game game)
        {
            List<Player> players;
            lock (sync)
            {
                players = game.Players.Where(p => !p.Arriving).ToList();
                game.ShownBars = BarKey(game);
            }
            foreach (Player p in players)
                SendHud(p.Session, game);
            foreach (WorldSession watcher in Watchers(game))
                SendHud(watcher, game);
        }

        /// <summary>Re-sends the HUD to everyone when the score bars changed; true if it did.</summary>
        private static bool RefreshBars(Game game)
        {
            string key = BarKey(game);
            lock (sync)
                if (key == game.ShownBars)
                    return false;
            SendHudAll(game);
            return true;
        }

        /// <summary>
        /// HUD sub 9 (0.589 reader FUN_14342ba40 → FUN_143427640, handler FUN_143432bd0; 0.695 FUN_1434dbe10 →
        /// FUN_1434d7120, handler FUN_1434e2f70 → GameData.InstancedGameScoreHUD): the round counter entry (its label
        /// heads the HUD), entry lists of round timers, wave progress and control indicators, the progress-bar groups
        /// (<see cref="WriteBars"/>), two more entry lists the handler does not read, then u32 state for the message line
        /// (3 "Waiting for players...", 4 the countdown, 8 "Game over! Resetting in N seconds" with N = 1 + the next
        /// u32 / 1000) and that u32.
        /// </summary>
        /// <summary>The control-point labels each client was last sent (session, game) → key (PropState).</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<(WorldSession, ulong), string> shownPropStates = new();

        /// <summary>
        /// PropState (InstancedGameBase sub 0x12, 0.695 number; 0.589 0x11; handler FUN_1434e8ea0; trace/0695/agent-leads2-a.md L32) [P]:
        /// {bool clear all, u32 n × {u32 0, u32 0, string label, u64 prop, i32 controller (-1 nobody → "Not Controlled",
        /// 0 a player → that player's name, n ≥ 1 team n → the team's name), u64 the controlling player}}. It puts the
        /// client's overhead "InstancedGameObject" indicator (HudIndicators row 22) over each control point, labelled with
        /// its holder. Sent with the HUD, only when the holders changed for that client; cleared when the round is over.
        /// </summary>
        private static void SendPropStates(WorldSession session, Game game)
        {
            if (!ControlLabels)
                return;
            bool show = game.State == StateRunning && game.Rules.ZoneControl;
            List<(ulong Guid, string Name, int Controller, ulong Player)> points;
            lock (sync)
                points = show ? game.Points.Select(cp =>
                {
                    Player holder = cp.Owner < 0 || game.Rules.TeamCount > 0 ? null : game.Players.FirstOrDefault(p => p.Slot == cp.Owner);
                    int controller = cp.Owner < 0 ? -1 : game.Rules.TeamCount > 0 ? ClientSide(game, cp.Owner) : 0;
                    return (cp.Guid, cp.Name, controller, holder?.Session.PlayerGuid ?? 0ul);
                }).ToList() : new();
            string key = string.Join(";", points.Select(x => $"{x.Guid:X}.{x.Controller}.{x.Player:X}"));
            var id = (session, game.Guid);
            if (shownPropStates.TryGetValue(id, out string shown) ? shown == key : key.Length == 0)
                return;
            if (key.Length == 0)
                shownPropStates.TryRemove(id, out _);
            else
                shownPropStates[id] = key;
            var output = new List<byte>();
            var w = Begin(output, SubPropState);
            w.Write(true);                               // replace what the client holds
            w.Write((uint)points.Count);
            foreach (var (guid, name, controller, player) in points)
            {
                w.Write(0u);
                w.Write(0u);
                w.Write(name);
                w.Write(guid);
                w.Write(controller);
                w.Write(player);
            }
            session.EnqueueMessage(ZoneMessageOpcode.InstancedGameBase, output.ToArray());
        }

        /// <summary>The capture bar each client was last sent per control point: (session, point) → key.</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<(WorldSession, ulong), string> shownInteractions = new();

        /// <summary>
        /// PropInteraction (InstancedGameBase sub 0x13, 0.695 number; 0.589 0x12; reader FUN_1434d7570, handler
        /// FUN_1434e8d70; trace/0695/agent-leads2-a.md L32) [P]: {u64 player, u64 prop, u32 start, i32 duration ms}. A
        /// positive duration puts a capture bar over the prop (FUN_1434f2c10: (client clock FUN_142cd2690 − start) /
        /// duration, coloured by the player's team); 0 removes it. The start is on the client's own clock, which the
        /// client reports in its KeepAlives (<see cref="WorldSession.ClientClockNow"/>), so it is written per client. The
        /// bar only rises, so a point left alone (our progress decays) or contested gets a 0, and a fresh bar, back-dated by
        /// the progress kept, when a side takes it up again [I: when live sent it].
        /// </summary>
        private static void SendPropInteractions(WorldSession session, Game game)
        {
            if (!ControlLabels || session.ClientClockNow is not uint clientNow)
                return;
            bool show = game.State == StateRunning && game.Rules.ZoneControl;
            var bars = new List<(ulong Prop, ulong Player, DateTime? Origin)>();
            int duration = (int)(game.Rules.CaptureSeconds * 1000f);
            lock (sync)
                foreach (ControlPoint cp in game.Points)
                    bars.Add((cp.Guid, cp.CapturePlayer, show ? cp.CaptureOrigin : null));
            DateTime now = DateTime.UtcNow;
            foreach (var (prop, player, origin) in bars)
            {
                string key = origin is DateTime o ? $"{player:X}.{o.Ticks}.{duration}" : "";
                var id = (session, prop);
                if (shownInteractions.TryGetValue(id, out string shown) ? shown == key : key.Length == 0)
                    continue;
                if (key.Length == 0)
                    shownInteractions.TryRemove(id, out _);
                else
                    shownInteractions[id] = key;
                var output = new List<byte>();
                var w = Begin(output, SubPropInteraction);
                w.Write(player);
                w.Write(prop);
                w.Write(origin is DateTime start ? unchecked(clientNow - (uint)Math.Max(0, (now - start).TotalMilliseconds)) : 0u);
                w.Write(origin != null ? duration : 0);
                session.EnqueueMessage(ZoneMessageOpcode.InstancedGameBase, output.ToArray());
            }
        }

        private static void SendHud(WorldSession session, Game game)
        {
            SendPropStates(session, game);
            SendPropInteractions(session, game);
            var output = new List<byte>();
            var w = Begin(output, SubHud);
            WriteHudEntry(w, Title(game));
            uint now = (uint)ServerClock.Milliseconds;
            if (game.State == StateRunning && game.Rules.HasTimeLimit)
            {
                w.Write(1u);
                WriteHudEntry(w, "Time Left", now + (uint)Math.Max(0, (game.RoundEnds - DateTime.UtcNow).TotalMilliseconds));
            }
            else if (game.State == StateWaitingForReady)
            {
                w.Write(1u);
                WriteHudEntry(w, "Starts In", now + (uint)Math.Max(0, (game.StateEnds - DateTime.UtcNow).TotalMilliseconds));
            }
            else
                w.Write(0u);
            w.Write(0u);                                 // wave progress
            // Control indicators (InstancedGameScoreHUD.ControlIndicators): entry +0x34 progress (%), +0x30 the taking
            // side's colour, +0x3c owned, +0x38 the holder's colour (team sides as the client numbers them, ClientSide).
            List<ControlPoint> points;
            lock (sync)
                points = game.State == StateRunning ? game.Points.ToList() : new List<ControlPoint>();
            w.Write((uint)points.Count);
            foreach (ControlPoint cp in points)
            {
                w.Write(0u);
                w.Write(cp.Name);
                w.Write(0u);
                w.Write(0u);
                w.Write(0u);
                // ControlIndicator.as: bar height = Progress / 100 in ProgressColor; owned points show the holder's colour.
                bool taking = cp.Capturer >= 0;
                w.Write(taking ? (uint)Math.Round(cp.Progress * 100f) : cp.Owner >= 0 ? 100u : 0u);
                w.Write(ClientSide(game, taking ? cp.Capturer : cp.Owner));
                w.Write(cp.Owner >= 0 ? 1u : 0u);
                w.Write(ClientSide(game, cp.Owner));
                WriteHudEntryTail(w);
            }
            WriteBars(w, session, game);
            w.Write(0u);                                 // entry list (body +0xa0 / 0.695 +0xb0), not read by the handler
            w.Write(0u);                                 // entry list (body +0xb8 / 0.695 +0xc8), not read by the handler
            w.Write(game.State);
            w.Write(game.State == StateFinishing ? (uint)Math.Max(0, (game.StateEnds - DateTime.UtcNow).TotalMilliseconds - 1000) : 0u);
            session.EnqueueMessage(ZoneMessageOpcode.InstancedGameBase, output.ToArray());
        }

        /// <summary>
        /// The HUD's progress-bar groups (trace/0695/hud-progress-695.md): u32 n, then per group (0x40 bytes; 0.589
        /// FUN_143429ea0 → FUN_143428640, 0.695 FUN_1434d9bb0 → FUN_1434d8360) u32 text id (+0x00), string label
        /// (+0x08), bool time (+0x20: the header's Type "Time", else "Int"), i32 total (+0x24), entry list (+0x28).
        /// Group g (at most 5) fills InstancedGameScoreHUD.ProgressBars.ProgressBar&lt;g&gt;: row 0 "LabelTotal" {Label,
        /// Type, Value = total}, rows 1-2 "ProgressBar" {Color, Value, Position = entry +0x30, IsSelf, Label (0.695)}.
        /// ProgressBarsDisplay.as draws the header label with the total as its amount and each bar's value out of that
        /// total (values above it are capped). (0.589 made a row of every entry in order and printed Position instead of a
        /// label, so it was sent the leader and its own bar only.) One group: the scores, out of the point limit (or out
        /// of the best score when the rules have none).
        /// </summary>
        private static void WriteBars(GamePacketWriter w, WorldSession session, Game game)
        {
            List<Bar> bars = BarsShown(game) ? Bars(game, session) : null;
            if (bars == null || bars.Count == 0)
            {
                w.Write(0u);
                return;
            }
            int goal = ScoreGoal(game);
            w.Write(1u);
            w.Write(0u);                                 // +0x00 text id
            w.Write(goal > 0 ? "Score Limit" : "Top Score"); // +0x08 header label
            w.Write(false);                              // +0x20 Type "Int"
            w.Write(goal > 0 ? goal : Math.Max(1, bars.Max(b => b.Value))); // +0x24 total = every bar's maximum
            w.Write((uint)bars.Count);
            foreach (Bar b in bars)
                WriteHudEntry(w, b.Label, 0, b.Value, b.Colour, b.Team, b.Player);
        }

        /// <summary>
        /// Scoreboard sub 3 (reader 0.589 FUN_143427ea0, 0.695 the same body; handler 0.589 FUN_143434580, 0.695
        /// FUN_1434e4a40 → GameData.InstancedGameScoreboard, the PvPScoreboard window Tab opens and the end-of-game
        /// banner): teams (+0x18) {u32 id, name {u32 text id, string}, f64 values (team columns), bool}, players (+0x30)
        /// {u32 team, u64 guid, name, f64 values (player columns, "Param1".. in the window, sorted by the first), bool},
        /// a u64 list, three string lists (unused, team column names, player column names: the header), i32 result
        /// (+0xc0), bool rebuild (+0xc4). See <see cref="BuildScoreboard"/>.
        /// </summary>
        private static List<WorldSession> Watchers(Game game)
        {
            lock (sync)
                return game.Observers.Where(o => o.RezoneTarget == null).ToList();
        }

        private static void SendScoreboardTo(WorldSession session, Game game)
        {
            byte[] data = BuildScoreboard(game);
            session.EnqueueMessage(ZoneMessageOpcode.InstancedGameBase, data);
        }

        /// <summary>
        /// The scoreboard to every player in place and every observer, then the HUD to everyone if the score bars
        /// changed (the scoreboard goes first: it tells the client its team, which the HUD's own bar needs). True if
        /// the HUD went out.
        /// </summary>
        private static bool SendScoreboard(Game game, bool final = false)
        {
            byte[] data = BuildScoreboard(game, final);
            List<Player> receivers;
            lock (sync)
                receivers = game.Players.Where(p => !p.Arriving).ToList();
            foreach (Player p in receivers)
                p.Session.EnqueueMessage(ZoneMessageOpcode.InstancedGameBase, data);
            foreach (WorldSession watcher in Watchers(game))
                watcher.EnqueueMessage(ZoneMessageOpcode.InstancedGameBase, data);
            return RefreshBars(game);
        }

        /// <summary>
        /// The scoreboard body. Team list: free for all sends one team (id 0, the handler's "one team" = free for all);
        /// team games send the client's empty slot 0 and then team t as id / index t + 1 (<see cref="ClientSide"/>):
        /// FUN_1434e4a40 makes team rows of indexes 1.. only, marks the row whose index equals the result IsWinner, and
        /// groups players by their team (TeamId), which also becomes the client's own team (manager +0xa0, set once
        /// from the entry with the local guid when a player column is named "Score"). Result +0xc0: -1 none (round on),
        /// the winning team's index, 0 = a draw in team games, game over / the best "Score" player in free for all.
        /// The trailing bool selects the handler's path: false only updates rows it already has (players by guid:
        /// Param values; teams by index: IsWinner, TeamScore), true clears and rebuilds the header, banner
        /// (ScoreboardBanner, which the state-7 OpenEndGameScoreboard reads), team rows (+ Joinable) and player rows.
        /// Every scoreboard here is complete, so it is always true (with false the client never had any rows).
        /// </summary>
        private static byte[] BuildScoreboard(Game game, bool final = false)
        {
            List<Player> players;
            lock (sync)
                players = game.Players.ToList();
            var output = new List<byte>();
            var w = Begin(output, SubScoreboard);
            bool teamGame = game.Rules.TeamCount > 0;
            if (teamGame)
            {
                w.Write((uint)(game.Rules.TeamCount + 1));
                w.Write(0u);                            // slot 0 ("FFA Team"), never a row in team games
                w.Write(0u);
                w.Write("");
                w.Write(1u);
                w.Write(0d);
                w.Write(false);
                for (int t = 0; t < game.Rules.TeamCount; t++)
                {
                    w.Write((uint)ClientSide(game, t));
                    w.Write(0u);
                    w.Write(TeamName(game, t));
                    w.Write(1u);
                    w.Write((double)game.TeamScores.ElementAtOrDefault(t));
                    w.Write(false);
                }
            }
            else
            {
                w.Write(1u);
                w.Write(0u);
                w.Write(0u);
                w.Write("Players");
                w.Write(1u);
                w.Write((double)players.Sum(p => p.Kills));
                w.Write(false);
            }
            w.Write((uint)players.Count);
            foreach (Player p in players)
            {
                w.Write((uint)(teamGame ? ClientSide(game, p.Team) : 0));
                w.Write(p.Session.PlayerGuid);
                w.Write(0u);
                w.Write(p.Session.CharacterName ?? "");
                w.Write(3u);
                w.Write((double)p.Score);
                w.Write((double)p.Kills);
                w.Write((double)p.Deaths);
                w.Write(false);
            }
            w.Write(0u);                                // u64 list
            w.Write(0u);                                // unused string list
            w.Write(1u);
            w.Write("Score");                           // team columns
            w.Write(3u);
            w.Write("Score");                           // player columns (Param1..3); "Score" also gives the client its team
            w.Write("Kills");
            w.Write("Deaths");
            // Result (+0xc0; banner "InstancedGames.VictoryMessage" with the winning team's name, DrawMessage, or in free
            // for all the best "Score" player / GameOverMessage; colour = palette[result]).
            bool over = final || game.State == StateVictory || game.State == StateFinishing;
            w.Write(!over ? -1 : teamGame && game.WinnerTeam >= 0 ? ClientSide(game, game.WinnerTeam) : 0);
            w.Write(true);                              // rebuild (see above)
            return output.ToArray();
        }

        /// <summary>
        /// TeamMembers sub 0x15 (0.589 0x14; reader 0.589 FUN_14342bf40, 0.695 FUN_1434dc380 → FUN_1434d7e10;
        /// handler 0.589 FUN_1434390e0, 0.695 FUN_1434e97c0): {u64 guid (+0x00), u32 (+0x08), string name (+0x10), u32
        /// team (+0x28)}, kept in a map by guid (manager +0x2b8). Team is the client's number (<see cref="ClientSide"/>):
        /// 0 = free for all, teams from 1, -1 = none (the local player's entry with a team != -1 ends observer mode).
        /// The client reads +0x28 for: hostility FUN_142ea9630 (team 0 → everyone is an enemy if the game setting
        /// InstancedGames_PvPAllowed is set, FUN_1434ec610; else enemies = a different team), friendliness
        /// FUN_142ea8f90 (team 0 → the normal rules; else the same team), the team colour FUN_1434e7280 /
        /// FUN_1434e7380 / FUN_1434f2c10 (palette index: 0 AF7FAF, 1 0034f8, 2 ff0000, 3 D4AB1F, 4 00cc00, as the HUD
        /// and scoreboard) and the walk-on window FUN_142dc9660 (-1 = not on a team). Sending the server's 0-based
        /// team made the first team "free for all" (hostile to its own players or to nobody) while the second saw it
        /// as the enemy, so team games send team + 1 like the scoreboard.
        /// </summary>
        private static void SendTeamMembers(Game game)
        {
            List<Player> players;
            lock (sync)
                players = game.Players.ToList();
            var output = new List<byte>();
            var w = Begin(output, SubTeamMembers);
            w.Write((uint)players.Count);
            foreach (Player p in players)
            {
                w.Write(p.Session.PlayerGuid);
                w.Write(ClientSide(game, p.Team));
                w.Write(p.Session.CharacterName ?? "");
                w.Write(ClientSide(game, p.Team));
            }
            byte[] data = output.ToArray();
            foreach (Player p in players.Where(p => !p.Arriving))
                p.Session.EnqueueMessage(ZoneMessageOpcode.InstancedGameBase, data);
        }

        /// <summary>
        /// Event sub 7 (reader FUN_143427430, handler FUN_143437440; 0.695 FUN_1434d6eb0, FUN_1434e7900): u32 type (1
        /// enemy kill, 2 team kill, 3 self kill, 4 environment kill, 7 flag taken, 8 flag captured, 9 control point
        /// taken), u32 team (+0x1c), u32 other team (+0x20, -1 none), u64 actor, u64 other actor, u32 text id. The
        /// client adds a kill feed line and plays its sound. The teams are the client's numbers (<see cref="ClientSide"/>):
        /// FUN_1434e7900 names them sourceTeam / targetTeam through FUN_1434e7570 (InstancedGame.Teams.DefaultName_&lt;id&gt;)
        /// and colours them sourceColor / targetColor = palette[id % 11] (-1 FFFFFF), the same table as the HUD bars.
        /// </summary>
        private static void SendEvent(Game game, uint kind, Player by, Player victim)
        {
            var output = new List<byte>();
            var w = Begin(output, SubEvent);
            w.Write(kind);
            w.Write(by != null ? ClientSide(game, by.Team) : -1);
            w.Write(ClientSide(game, victim.Team));
            w.Write(by?.Session.PlayerGuid ?? 0ul);
            w.Write(victim.Session.PlayerGuid);
            WriteEventTail(w, victim.Session.CharacterName ?? "", by?.Session.CharacterName ?? "");
            byte[] data = output.ToArray();
            // Watchers too: in observer mode the 0.695 client switches to its observing HUD (game state 9), which loads the
            // kill-feed and status-feed widgets and shows every Event it gets (trace/0695/phaseE3-ui.md F1).
            foreach (WorldSession viewer in game.Players.Select(p => p.Session).Concat(Watchers(game)).Distinct().ToList())
                viewer.EnqueueMessage(ZoneMessageOpcode.InstancedGameBase, data);
        }

        /// <summary>
        /// The end of an Event (0.589: only a u32 text id). 0.695 (reader FUN_1434d6eb0, handler FUN_1434e7900; proven
        /// 2026-10-16): str target name (+0x38), str source name (+0x50), u32 object text id (+0x68), i32 pointCount
        /// (+0x6c). The handler names "targetPlayer" from the other actor's entity (+0x1308 name) and only when the client
        /// doesn't know that actor uses the target string, likewise "sourcePlayer" / the source string; the text id
        /// fills "object" (empty when 0); pointCount fills the point-grant lines of types 10 / 11 (B9).
        /// </summary>
        private static void WriteEventTail(GamePacketWriter w, string targetName = "", string sourceName = "", int points = 0)
        {
            w.Write(targetName ?? "");
            w.Write(sourceName ?? "");
            w.Write(0u);
            w.Write(points);
        }

        /// <summary>Dev "game": the game here and its players.</summary>
        public static string Describe(WorldSession session)
        {
            EnsureLoaded();
            Game game = GameOf(session) ?? GameAt(session);
            if (game == null)
            {
                lock (sync)
                    return $"No game here. Site games: {string.Join(", ", gamesByClaim.Values.Select(g => $"{Title(g)} on site {g.ClaimId} (state {g.State}, {g.Players.Count} players)"))}";
            }
            Rules now = RulesFor(game);
            lock (sync)
                return $"{Title(game)} (rule set {game.Rules.Id}) on site {game.ClaimId}, table 0x{game.Guid:X}, state {game.State}, "
                    + $"{game.Players.Count}/{game.Rules.MaxPlayers} players (min {game.Rules.MinPlayers}), "
                    + $"{game.Rules.TeamCount} team(s) [{string.Join("/", Enumerable.Range(0, game.Rules.TeamCount).Select(t => $"{TeamName(game, t)} {game.TeamScores.ElementAtOrDefault(t)}"))}], "
                    + $"PvP {game.Rules.PvP}, switch {game.Rules.AllowSwitch}, rules {string.Join(",", game.Rules.RuleRows.Select(r => r.Type))}"
                    + (now.RuleRows.Count != game.Rules.RuleRows.Count || now.TeamCount != game.Rules.TeamCount ? $" (next round: {now.TeamCount} team(s), rules {string.Join(",", now.RuleRows.Select(r => r.Type))})" : "")
                    + ": " + string.Join(", ", game.Players.Select(p => $"{p.Session.CharacterName} t{p.Team} {p.Score}/{p.Kills}/{p.Deaths}"))
                    + DescribeQueue(game);
        }

        #endregion
    }
}
