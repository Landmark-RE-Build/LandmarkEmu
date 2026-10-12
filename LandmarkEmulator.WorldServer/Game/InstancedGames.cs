using LandmarkEmulator.Shared.Network;
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
    /// </summary>
    public static partial class InstancedGames
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        private static readonly object sync = new();

        // InstancedGameBase subs (u32). Server → client:
        private const uint SubScoreboard = 3, SubPropRecipe = 4, SubState = 6, SubEvent = 7, SubRequest = 8, SubHud = 9,
            SubTeamMembers = 0x14, SubObserverRegistration = 0x15;

        // InstancedGameRequest types (i16). Client → server: 1 GainAuthorship (Claims.RequestGainActiveAuthorshipOfGame),
        // 2 LoseAuthorship, 3 Join (Claims.JoinInstancedGame, walk-on "Join"), 7 Leave, 8 Respawn (u64 = the game).
        // Server → client (HandleGameRequestPacket FUN_143438d00): 4 left the queue, 5 queue position, 6 membership:
        // result 1 with value 1 = in the game (manager +0x38 = game), 0 = out of it (FUN_143432090 clears the game).
        private const short RequestGainAuthorship = 1, RequestLoseAuthorship = 2, RequestJoin = 3, RequestMembership = 6,
            RequestLeave = 7, RequestRespawn = 8;

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
        private static readonly TimeSpan ArrivalGrace = TimeSpan.FromSeconds(1);

        /// <summary>Spawn props (IgPropRoleProps): 1371 spawn location (start / respawn / exit), 1223 and 1367 team spawns.</summary>
        private const uint SpawnLocationProp = 1371, TeamSpawnProp0 = 1223, TeamSpawnProp1 = 1367;
        private static readonly HashSet<uint> spawnProps = new() { SpawnLocationProp, TeamSpawnProp0, TeamSpawnProp1 };

        /// <summary>A rule set's settings, resolved from the Ig* tables (see <see cref="LoadRules"/>).</summary>
        public class Rules
        {
            public uint Id;
            public uint NameId;
            public string Name = "";
            /// <summary>0 = free for all.</summary>
            public int TeamCount;
            public int MinPlayers = 2, MaxPlayers = 16;
            public int TimeLimitSeconds = 600;
            public int PointLimit, TeamPointLimit;
            public int PointsPerKill = 1, PenaltyPerDeath, SelfKillPenalty, EnvironmentPenalty;
            public int RespawnDelaySeconds = 6;
            public int CountdownMs = 30000;
            /// <summary>Adventure (rule set 8): players work together against the site's story; no PvP, creature kills
            /// score, the site plays its story while a round is on.</summary>
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
        }

        private const uint ControlPointProp = 1224;

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
        private static Dictionary<uint, Rules> LoadRules()
        {
            var typeParams = GameDataFiles.Rows("IgRuleTypeParameters.txt")
                .Select(r => (Id: r.U(0), Type: r.U(1), NameId: r.U(3), Min: r.S(4), Max: r.S(5), Default: r.S(6)))
                .GroupBy(p => p.Type).ToDictionary(g => g.Key, g => g.OrderBy(p => p.Id).ToList());
            var overrides = GameDataFiles.Rows("IgRuleParameters.txt")
                .Select(r => (Id: r.U(0), Rule: r.U(1), Value: r.S(2)))
                .GroupBy(o => o.Rule).ToDictionary(g => g.Key, g => g.OrderBy(o => o.Id).Select(o => o.Value).ToList());
            var ruleRows = GameDataFiles.Rows("IgRules.txt").Select(r => (Id: r.U(0), Set: r.U(1), Type: r.U(2))).ToList();
            var general = GameDataFiles.Rows("IgGeneralSetting.txt").Select(r => (Setting: r.U(1), Set: r.U(2), Value: r.I(3))).ToList();

            var result = new Dictionary<uint, Rules>();
            foreach (string[] row in GameDataFiles.Rows("IgRuleSets.txt"))
            {
                var set = new Rules { Id = row.U(0), NameId = row.U(1), TeamCount = row.I(3) };
                set.Name = Shared.GameTable.Text.TextManager.Instance.GetTextForId(set.NameId) ?? $"Game {set.Id}";
                // Parameter values by IgRuleTypeParameters id.
                var values = set.Values;
                set.RuleRows = ruleRows.Where(r => r.Set == set.Id).Select(r => (r.Id, r.Type)).ToList();
                foreach (var rule in ruleRows.Where(r => r.Set == set.Id))
                {
                    if (!typeParams.TryGetValue(rule.Type, out var parameters))
                        continue;
                    overrides.TryGetValue(rule.Id, out var own);
                    for (int i = 0; i < parameters.Count; i++)
                    {
                        string text = own != null && i < own.Count && own[i].Length > 0 ? own[i] : parameters[i].Default;
                        bool flag = bool.TryParse(parameters[i].Default, out _);
                        if (int.TryParse(text, out int v))
                            values[parameters[i].Id] = v;
                        else if (bool.TryParse(text, out bool b))
                            values[parameters[i].Id] = b ? 1 : 0;
                        float.TryParse(parameters[i].Min, NumberStyles.Float, CultureInfo.InvariantCulture, out float min);
                        float.TryParse(parameters[i].Max, NumberStyles.Float, CultureInfo.InvariantCulture, out float max);
                        if (parameters[i].NameId != 0 && set.Parameters.All(p => p.Id != parameters[i].Id))
                            set.Parameters.Add(new Parameter(parameters[i].Id, parameters[i].NameId, flag ? 0 : min, flag ? 1 : max, flag));
                    }
                }
                ApplyValues(set);
                int Setting(uint id, int fallback)
                    => general.Where(g => g.Set == set.Id && g.Setting == id).Select(g => (int?)g.Value).FirstOrDefault() ?? fallback;
                set.MinPlayers = Math.Max(1, Setting(2, 2));
                set.MaxPlayers = Math.Max(set.MinPlayers, Setting(3, 16));
                set.CountdownMs = Setting(4, 30000);
                set.Cooperative = set.Id == AdventureRuleSet;
                set.ZoneControl = values.ContainsKey(23);
                set.Difficulty = Setting(10, 0);
                result[set.Id] = set;
            }
            return result;
        }

        /// <summary>The rule fields from the parameter values (IgRuleTypeParameters ids).</summary>
        private static void ApplyValues(Rules set)
        {
            int Value(uint param, int fallback) => set.Values.TryGetValue(param, out int v) ? v : fallback;
            set.TimeLimitSeconds = Math.Max(10, Value(1, 600));
            set.PointsPerKill = Value(2, 1);
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
            if (!placementProps.TryGetValue(propId, out uint setId) || !rules.TryGetValue(setId, out Rules set))
                return new List<PropSettings.Definition>();
            return set.Parameters.Select(p => new PropSettings.Definition(10000 + p.Id, ParameterKey(p.Id), p.NameId, 0,
                set.Values.TryGetValue(p.Id, out int v) ? v : 0, p.Max, p.Min, p.Flag ? 1 : Math.Max(1, (p.Max - p.Min) >= 600 ? 30 : 1))).ToList();
        }

        /// <summary>The rule set with this table's own settings applied (a copy; read at each round start).</summary>
        private static Rules RulesFor(Game game)
        {
            Rules baseSet = rules[placementProps[game.PropId]];
            var own = new Rules
            {
                Id = baseSet.Id, NameId = baseSet.NameId, Name = baseSet.Name, TeamCount = baseSet.TeamCount,
                MinPlayers = baseSet.MinPlayers, MaxPlayers = baseSet.MaxPlayers, CountdownMs = baseSet.CountdownMs,
                Cooperative = baseSet.Cooperative, ZoneControl = baseSet.ZoneControl, Difficulty = baseSet.Difficulty,
                Parameters = baseSet.Parameters, RuleRows = baseSet.RuleRows, Values = new Dictionary<uint, int>(baseSet.Values)
            };
            foreach (Parameter p in baseSet.Parameters)
                if (PropSettings.TryGetSaved(game.Guid, ParameterKey(p.Id), out float v))
                    own.Values[p.Id] = (int)v;
            ApplyValues(own);
            // The game editor's general settings (IgGeneralSetting: 2 / 3 players, 8 observers, 10 difficulty, 11 PvP).
            own.MinPlayers = Math.Max(1, General(game, baseSet, 2, baseSet.MinPlayers));
            own.MaxPlayers = Math.Max(own.MinPlayers, General(game, baseSet, 3, baseSet.MaxPlayers));
            own.Difficulty = General(game, baseSet, 10, baseSet.Difficulty);
            own.AllowsObservers = General(game, baseSet, 8, 1) != 0;
            own.PvP = !own.Cooperative && General(game, baseSet, 11, 1) != 0;
            return own;
        }

        /// <summary>Dev: the rule sets as the server resolved them.</summary>
        public static string DescribeRules()
        {
            EnsureLoaded();
            return string.Join("; ", rules.Values.Select(r => $"{r.Id} {r.Name}: teams {r.TeamCount}, players {r.MinPlayers}-{r.MaxPlayers}, "
                + $"{r.TimeLimitSeconds}s, points {r.PointLimit}/{r.TeamPointLimit}, kill {r.PointsPerKill} death {r.PenaltyPerDeath} "
                + $"self {r.SelfKillPenalty}, respawn {r.RespawnDelaySeconds}s, countdown {r.CountdownMs}ms"));
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
                writer.Write(game?.CustomName ?? "");
                writer.Write(0u);
            }
        }

        #endregion

        #region tick

        /// <summary>Server tick: follows the placed Game Tables and runs each game's clock.</summary>
        public static void Update()
        {
            EnsureLoaded();
            DateTime now = DateTime.UtcNow;
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
                    gamesByClaim[claim] = new Game
                    {
                        Guid = table.Guid,
                        PropId = table.PropId,
                        ClaimId = claim,
                        Zone = table.Zone,
                        Position = table.Position,
                        CustomName = table.Name,
                        Rules = set,
                        TeamScores = new int[Math.Max(1, set.TeamCount)],
                        Disabled = PropSettings.TryGetSaved(table.Guid, DisabledKey, out float off) && off != 0
                    };
                    changed.Add(claim);
                    log.Info($"Site {claim} hosts a {set.Name} game (table 0x{table.Guid:X}).");
                }
            }
            foreach (Game game in ended)
            {
                List<WorldSession> editors;
                lock (sync)
                    editors = game.Editors.ToList();
                foreach (WorldSession editor in editors)
                    SendRecipe(editor, null, false);
                foreach (Player player in game.Players.ToList())
                    RemovePlayer(game, player, "the game table was removed");
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
                    if (game.Players.Count >= game.Rules.MinPlayers)
                        BeginCountdown(game, now);
                    else if (game.State == StateIdle && game.Players.Count > 0)
                        SetState(game, StateStarted);
                    else if (game.State == StateStarted && game.Players.Count == 0)
                        SetState(game, StateIdle);
                    break;
                case StateWaitingForReady:
                    if (game.Players.Count < game.Rules.MinPlayers)
                        SetState(game, game.Players.Count > 0 ? StateStarted : StateIdle);
                    else if (now >= game.StateEnds)
                        StartRound(game, now);
                    break;
                case StateRunning:
                    if (game.Players.Count == 0)
                        SetState(game, StateIdle);
                    else if (now >= game.RoundEnds)
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

        /// <summary>Dev "gamescore n": adds points to the player (and their team).</summary>
        public static void DevScore(WorldSession session, int points)
        {
            Game game = GameOf(session);
            Player player = game?.Find(session);
            if (player == null)
                return;
            lock (sync)
            {
                player.Score += points;
                player.Kills += Math.Max(0, points);
                if (game.Rules.TeamCount > 0)
                    game.TeamScores[player.Team] += points;
            }
            SendScoreboard(game);
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
            if (game.TeamScores.Length != Math.Max(1, game.Rules.TeamCount))
                game.TeamScores = new int[Math.Max(1, game.Rules.TeamCount)];
            ResetScores(game);
            game.RoundEnds = now.AddSeconds(game.Rules.TimeLimitSeconds);
            if (game.Rules.ZoneControl)
                SetUpControlPoints(game, now);
            // Moves first: the players being moved get the new state once they have arrived (SyncArrived).
            List<Player> players;
            lock (sync)
                players = game.Players.ToList();
            foreach (Player player in players)
                Respawn(game, player);
            SetState(game, StateRunning);
            // An Adventure plays the site's story from the start for each round.
            if (game.Rules.Cooperative && Claims.Get(game.ClaimId) is Database.Character.Model.ClaimModel site)
                Claims.ApplyMode(site, play: true, restart: true);
            SendScoreboard(game);
            Broadcast(game, $"{Title(game)} has begun! " + (game.Rules.PointLimit > 0 ? $"First to {game.Rules.PointLimit} points wins."
                : game.Rules.TeamPointLimit > 0 ? $"First team to {game.Rules.TeamPointLimit} points wins." : ""));
            log.Info($"{Title(game)} on site {game.ClaimId} started with {players.Count} player(s) ({game.Rules.TimeLimitSeconds} s, point limit {game.Rules.PointLimit}/{game.Rules.TeamPointLimit}, respawn {game.Rules.RespawnDelaySeconds} s).");
        }

        private static void EndRound(Game game, DateTime now, string why)
        {
            string winner;
            lock (sync)
            {
                if (game.Rules.TeamCount > 0)
                {
                    int best = game.TeamScores.Max();
                    var top = Enumerable.Range(0, game.TeamScores.Length).Where(t => game.TeamScores[t] == best).ToList();
                    game.WinnerTeam = top.Count == 1 ? top[0] : -1;
                    winner = game.WinnerTeam >= 0 ? $"{TeamName(game.WinnerTeam)} wins" : "It's a draw";
                    game.BannerText = game.WinnerTeam >= 0 ? $"{TeamName(game.WinnerTeam)} Wins!" : "Draw!";
                }
                else
                {
                    Player best = game.Players.OrderByDescending(p => p.Score).ThenBy(p => p.Deaths).FirstOrDefault();
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
            if (game.Rules.Cooperative && Claims.Get(game.ClaimId) is Database.Character.Model.ClaimModel site)
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
            if (HandleEditor(session, request.SubOpcode, request.Body))
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
            if (game.State == StateRunning)
                Respawn(game, player);
            else
                MoveToSpawn(game, player, start: true);
            if (!player.Arriving)
            {
                SendState(session, game);
                SendHud(session, game);
            }
            SendTeamMembers(game);
            SendScoreboard(game);
            Claims.SendGameInfo(game.ClaimId);
            Broadcast(game, $"{session.CharacterName} joined {Title(game)}" + (game.Rules.TeamCount > 0 ? $" ({TeamName(player.Team)})." : "."));
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
        /// ObserverRegistration 0x15 {u32, u64 game, u32 flag} (Lua InstancedGame.EnterObserverMode / ExitObserverMode;
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
                SendHud(session, game);
                SendScoreboardTo(session, game);
                log.Info($"{session.CharacterName} watches {Title(game)} on site {game.ClaimId}.");
            }
            else
            {
                SendState(session, was, StateNone);
                log.Info($"{session.CharacterName} stopped watching {Title(was)}.");
            }
        }

        private static int PickTeam(Game game)
        {
            if (game.Rules.TeamCount <= 0)
                return 0;
            return Enumerable.Range(0, game.Rules.TeamCount)
                .OrderBy(t => game.Players.Count(p => p.Team == t)).ThenBy(t => game.TeamScores.ElementAtOrDefault(t)).First();
        }

        private static string TeamName(int team) => team switch { 0 => "Blue team", 1 => "Yellow team", _ => $"Team {team + 1}" };

        private static string Title(Game game) => game.CustomName.Length > 0 ? game.CustomName : game.Rules.Name;

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
        /// </summary>
        public static bool Hit(WorldSession attacker, ulong target, (Vector3 Origin, Vector3 Direction)? ray, Vector3 aim,
            uint damage, float range)
        {
            Game game = GameOf(attacker);
            if (game == null || game.State != StateRunning || Health.IsDead(attacker) || game.Rules.Cooperative || !game.Rules.PvP)
                return false;
            Player me = game.Find(attacker);
            List<Player> enemies;
            lock (sync)
                enemies = game.Players.Where(p => p != me && p.RespawnAt == null && !p.Arriving && !Health.IsDead(p.Session)
                    && p.Session.ZoneId == attacker.ZoneId && (game.Rules.TeamCount == 0 || p.Team != me.Team)).ToList();
            Player victim = null;
            if (target != 0)
                victim = enemies.FirstOrDefault(p => p.Session.PlayerGuid == target
                    && Vector3.Distance(p.Session.Position, attacker.Position) <= range);
            if (victim == null && ray is var (origin, direction))
            {
                float best = float.MaxValue;
                foreach (Player p in enemies)
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

            bool won = game.Rules.TeamCount == 0
                ? game.Rules.PointLimit > 0 && by != null && by.Score >= game.Rules.PointLimit
                : game.Rules.TeamPointLimit > 0 && by != null && game.TeamScores[by.Team] >= game.Rules.TeamPointLimit;
            if (won)
                EndRound(game, DateTime.UtcNow, "point limit reached");
            return true;
        }

        /// <summary>
        /// The Adventure on a site was completed ("...Give reward and teleport to a Chaos Cavern." on its Game Table): the
        /// round ends with everyone in it winning. (The Chaos Cavern trip isn't made.)
        /// </summary>
        public static void CompleteAdventure(ulong claimId)
        {
            Game game = GameOn(claimId);
            if (game == null || game.State != StateRunning || !game.Rules.Cooperative)
                return;
            lock (sync)
                foreach (Player p in game.Players)
                    p.Score += game.Rules.PointLimit > 0 ? game.Rules.PointLimit : 10;
            game.ToCavern = true;
            EndRound(game, DateTime.UtcNow, "adventure complete");
        }

        /// <summary>A player killed a creature: in a running Adventure it scores (points per kill).</summary>
        public static void CreatureKilled(WorldSession killer)
        {
            Game game = killer == null ? null : GameOf(killer);
            Player p = game?.Find(killer);
            if (p == null || game.State != StateRunning || !game.Rules.Cooperative)
                return;
            lock (sync)
            {
                p.Kills++;
                p.Score += game.Rules.PointsPerKill;
            }
            SendScoreboard(game);
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
                        }
                        cp.Progress += dt / game.Rules.CaptureSeconds;
                        hudChanged = true;
                        if (cp.Progress >= 1f)
                        {
                            cp.Owner = cp.Capturer;
                            cp.Capturer = -1;
                            cp.Progress = 0f;
                            AddSideScore(game, cp.Owner, game.Rules.PointsPerCapture);
                            events.Add((cp, cp.Owner));
                        }
                    }
                    else if (sides.Count == 0 && cp.Progress > 0f)
                    {
                        cp.Progress = Math.Max(0f, cp.Progress - dt / game.Rules.CaptureSeconds);
                        if (cp.Progress == 0f)
                            cp.Capturer = -1;
                        hudChanged = true;
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
            {
                Player taker = game.Players.FirstOrDefault(p => SideOf(game, p) == side);
                var output = new List<byte>();
                var w = Begin(output, SubEvent);
                w.Write(9u);                             // InstancedGame.ControlPtTaken
                w.Write(game.Rules.TeamCount > 0 ? side : taker?.Team ?? -1);
                w.Write(-1);
                w.Write(taker?.Session.PlayerGuid ?? 0ul);
                w.Write(cp.Guid);
                w.Write(0u);
                byte[] data = output.ToArray();
                foreach (Player p in game.Players.Where(p => !p.Arriving).ToList())
                    p.Session.EnqueueMessage(ZoneMessageOpcode.InstancedGameBase, data);
                Broadcast(game, $"{(game.Rules.TeamCount > 0 ? TeamName(side) : taker?.Session.CharacterName ?? "Someone")} took {cp.Name}.");
                log.Info($"{Title(game)}: {cp.Name} taken by side {side}.");
            }
            if (hudChanged)
            {
                foreach (Player p in game.Players.Where(p => !p.Arriving).ToList())
                    SendHud(p.Session, game);
                if (events.Count > 0 || now.AddSeconds(game.Rules.TickSeconds) == game.NextScoreTick)
                    SendScoreboard(game);
            }
            bool won = game.Rules.TeamCount > 0
                ? game.Rules.TeamPointLimit > 0 && game.TeamScores.Any(s => s >= game.Rules.TeamPointLimit)
                : game.Rules.PointLimit > 0 && game.Players.Any(p => p.Score >= game.Rules.PointLimit);
            if (won)
                EndRound(game, now, "point limit reached");
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
            if (!MoveToSpawn(game, player, start: false) && (Health.IsDead(player.Session) || Health.Current(player.Session) < Health.MaxHealth))
                Health.Revive(player.Session);
            log.Info($"{Title(game)}: {player.Session.CharacterName} respawned.");
        }

        /// <summary>
        /// Moves a player to one of the game's spawn points on the site: their team's spawn props in team games
        /// (1223 / 1367), else spawn locations (1371) or any spawn prop; next to the Game Table when there are none.
        /// </summary>
        /// <summary>Dev "gametp off": respawn in place (no spawn teleports), for isolating client problems.</summary>
        public static bool SpawnTeleports { get; set; } = true;

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
                uint teamProp = player.Team == 0 ? TeamSpawnProp0 : TeamSpawnProp1;
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
                if (game.State == StateRunning && (Health.IsDead(p.Session) || Health.Current(p.Session) < Health.MaxHealth))
                    Health.Revive(p.Session);
                SendState(p.Session, game);
                SendHud(p.Session, game);
                Claims.SendGameInfoTo(p.Session, game.ClaimId);
                log.Info($"{Title(game)}: {p.Session.CharacterName} arrived at a spawn point (state {game.State}, {Health.Current(p.Session)} hp).");
            }
            SendTeamMembers(game);
            SendScoreboard(game);
        }

        #endregion

        #region packets

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

        /// <summary>A HUD entry (FUN_1434284b0): u32 text id, string label, then seven u32 (+0x24, +0x28, +0x2c, +0x34,
        /// +0x30, +0x3c, +0x38); a round timer's end time (the client's synced clock, ms) is +0x3c.</summary>
        private static void WriteHudEntry(GamePacketWriter w, string label, uint endTime = 0)
        {
            w.Write(0u);
            w.Write(label);
            for (int i = 0; i < 5; i++)
                w.Write(0u);
            w.Write(endTime);
            w.Write(0u);
        }

        /// <summary>
        /// HUD sub 9 (reader FUN_14342ba40 → FUN_143427640, handler FUN_143432bd0 → GameData.InstancedGameScoreHUD):
        /// round counter entry (its label heads the HUD), lists of round timers, wave progress, control indicators,
        /// progress bars (FUN_143428640 entries) and two more, then u32 state for the message line (3 "Waiting for
        /// players...", 4 the countdown, 8 "Game over! Resetting in N seconds" with N = 1 + the next u32 / 1000).
        /// </summary>
        private static void SendHud(WorldSession session, Game game)
        {
            var output = new List<byte>();
            var w = Begin(output, SubHud);
            WriteHudEntry(w, Title(game));
            uint now = (uint)ServerClock.Milliseconds;
            if (game.State == StateRunning)
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
            // Control indicators (FUN_143432bd0 → InstancedGameScoreHUD.ControlIndicators): entry +0x34 progress (%),
            // +0x30 the taking side's colour, +0x3c owned, +0x38 the holder's colour.
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
                w.Write(taking ? cp.Capturer : cp.Owner);
                w.Write(cp.Owner >= 0 ? 1u : 0u);
                w.Write(cp.Owner);
            }
            for (int i = 0; i < 3; i++)
                w.Write(0u);
            w.Write(game.State);
            w.Write(game.State == StateFinishing ? (uint)Math.Max(0, (game.StateEnds - DateTime.UtcNow).TotalMilliseconds - 1000) : 0u);
            session.EnqueueMessage(ZoneMessageOpcode.InstancedGameBase, output.ToArray());
        }

        /// <summary>
        /// Scoreboard sub 3 (reader FUN_143427ea0, handler FUN_143434580 → GameData.InstancedGameScoreboard): teams
        /// {u32 id, name {u32 text id, string}, f64 values (team columns), bool}, players {u32 team, u64 guid, name,
        /// f64 values (player columns, "Param1".. in the window, sorted by the first; the handler reads them as
        /// doubles and takes the best "Score" as a free-for-all's winner), bool}, a u64 list, three string
        /// lists (unused, team column names, player column names: the header), u32 winning team (-1 none), bool.
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

        private static void SendScoreboard(Game game, bool final = false)
        {
            byte[] data = BuildScoreboard(game, final);
            List<Player> receivers;
            lock (sync)
                receivers = game.Players.Where(p => !p.Arriving).ToList();
            foreach (Player p in receivers)
                p.Session.EnqueueMessage(ZoneMessageOpcode.InstancedGameBase, data);
            foreach (WorldSession watcher in Watchers(game))
                watcher.EnqueueMessage(ZoneMessageOpcode.InstancedGameBase, data);
        }

        private static byte[] BuildScoreboard(Game game, bool final = false)
        {
            List<Player> players;
            lock (sync)
                players = game.Players.ToList();
            var output = new List<byte>();
            var w = Begin(output, SubScoreboard);
            int teams = Math.Max(1, game.Rules.TeamCount);
            w.Write((uint)teams);
            for (int t = 0; t < teams; t++)
            {
                w.Write((uint)t);
                w.Write(0u);
                w.Write(game.Rules.TeamCount > 0 ? TeamName(t) : "Players");
                w.Write(1u);
                w.Write((double)(game.Rules.TeamCount > 0 ? game.TeamScores[t] : players.Sum(p => p.Kills)));
                w.Write(false);
            }
            w.Write((uint)players.Count);
            foreach (Player p in players)
            {
                w.Write((uint)p.Team);
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
            w.Write("Score");                           // player columns (Param1..3)
            w.Write("Kills");
            w.Write("Deaths");
            // Result (+0xc0, banner in the client's scoreboard handler FUN_143434580): -1 none (round on); a team id
            // (1-based) = that team won ("InstancedGame.Teams.DefaultName_<id>", colour <id> % 13); 0 = a draw in team
            // games, game over / the winner's name in free for all (one team in the list).
            bool over = final || game.State == StateVictory || game.State == StateFinishing;
            w.Write(!over ? -1 : game.Rules.TeamCount > 0 && game.WinnerTeam >= 0 ? game.WinnerTeam + 1 : 0);
            w.Write(false);
            return output.ToArray();
        }

        /// <summary>TeamMembers sub 0x14 (reader FUN_14342bf40, handler FUN_1434390e0): {u64 guid, u32 team, string name, u32 team}.</summary>
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
                w.Write((uint)p.Team);
                w.Write(p.Session.CharacterName ?? "");
                w.Write((uint)p.Team);
            }
            byte[] data = output.ToArray();
            foreach (Player p in players.Where(p => !p.Arriving))
                p.Session.EnqueueMessage(ZoneMessageOpcode.InstancedGameBase, data);
        }

        /// <summary>
        /// Event sub 7 (reader FUN_143427430, handler FUN_143437440): u32 type (1 enemy kill, 2 team kill, 3 self kill,
        /// 4 environment kill, 7 flag taken, 8 flag captured, 9 control point taken), u32 team, u32 other team (-1
        /// none), u64 actor, u64 other actor, u32 text id. The client adds a kill feed line and plays its sound.
        /// </summary>
        private static void SendEvent(Game game, uint kind, Player by, Player victim)
        {
            var output = new List<byte>();
            var w = Begin(output, SubEvent);
            w.Write(kind);
            w.Write(by != null ? by.Team : -1);
            w.Write(victim.Team);
            w.Write(by?.Session.PlayerGuid ?? 0ul);
            w.Write(victim.Session.PlayerGuid);
            w.Write(0u);
            byte[] data = output.ToArray();
            foreach (Player p in game.Players.ToList())
                p.Session.EnqueueMessage(ZoneMessageOpcode.InstancedGameBase, data);
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
            lock (sync)
                return $"{Title(game)} (rule set {game.Rules.Id}) on site {game.ClaimId}, table 0x{game.Guid:X}, state {game.State}, "
                    + $"{game.Players.Count}/{game.Rules.MaxPlayers} players (min {game.Rules.MinPlayers}): "
                    + string.Join(", ", game.Players.Select(p => $"{p.Session.CharacterName} t{p.Team} {p.Score}/{p.Kills}/{p.Deaths}"))
                    + DescribeQueue(game);
        }

        #endregion
    }
}
