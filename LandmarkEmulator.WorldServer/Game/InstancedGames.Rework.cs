using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The 0.695 Game Table rework (29 Sep 2016 game update; TASKS 2.12, "Breaks" B1 / B2 / B9, C.5 JoinTeam and
    /// AddOrRemoveRule). Sources: trace/0695/functions-695.md §1, packets-695.md §5 / §B6-B7, storytools-695.md §4.14,
    /// the patch notes (design/patchnotes-after-0589.md, "(Re)Introducing the Game Table!") and the 0.695 tables
    /// IgRuleSets / IgRules / IgRuleTypes / IgGeneralSetting / RefIgGeneralSettingsTypes (gameData695).
    /// <para>
    /// The reworked Game Table is prop 2313 (IgRuleSetPlacementProps → rule set 8, "Adventure"). Its game editor has
    /// "General Settings" (IgGeneralSetting rows 55-78 of set 8: Number of Teams, Is Free-for-All, Min / Max Players Per
    /// Team, Allow Observers, PvP Allowed, Allow team switching, Team 1-4 Name / Allow Players / Victory Message, Players
    /// needed to start; each shown only when its client requirement holds) and "Game Type Settings": a check box per
    /// IgRuleTypes row with IS_PLAYER_ADDABLE (1 Time Limit, 2 Score On Enemy Kill, 8 Player Score Victory, 9 Team Score
    /// Victory, 11 Control Points) that sends AddOrRemoveRule at once, with the ticked rules' parameters below. The
    /// "Game Props" tab (the PropRecipe) lists the props the active rules need (team / neutral spawn points, control
    /// points) and the script props Score Node 3011 and Victory Node 3010.
    /// </para>
    /// <para>
    /// Proven from the 0.695 client: settings keyed by the name hash (B1, <see cref="SettingKeyHash"/>); Apply sends the
    /// held list back and Reset sends an empty list with the reset flag (B2); the per-trigger u32 / u64 of
    /// GetConfigurationResponse are the rule type and the trigger (FUN_1434d8660, FUN_1434e98d0, FUN_1434f4460);
    /// AddOrRemoveRule {u64 game, u32 rule type, u8 add, u64 trigger} (FUN_1434f4d60); JoinTeam = Request type 9 with the
    /// client's team number (FUN_1434f1210; JoinableTeams rows FUN_1434e4a40 use the team index, gated by
    /// InstancedGame_AllowPlayersTeam&lt;n&gt;); the recipe item's new u32 is "ScriptPropId" (FUN_1434f31e0); event types
    /// 10 / 11 print PointGrantPlayerFFA / PointGrantPlayerOnTeam / PointGrantTeam (texts 577670 / 577669 / 577671)
    /// with targetPlayer / targetTeam / pointCount (FUN_1434e7900; B9). Inferred choices are marked "inferred".
    /// </para>
    /// </summary>
    public static partial class InstancedGames
    {
        /// <summary>
        /// GetConfigurationRequest 0xD / AddOrRemoveRuleRequest 0x18 (C→S, 0.695 only: packets-695.md §5d / §5e).
        /// </summary>
        private const uint SubGetConfigurationRequest = 0x0D, SubAddOrRemoveRule = 0x18;

        /// <summary>
        /// Trigger (rule row) ids of rules a table switched on that its rule set has no IgRules row for: this + the
        /// IgRuleTypes id (ours; the live ids are unknown — the client only echoes them).
        /// </summary>
        private const uint SyntheticRuleBase = 1000;

        /// <summary>Rule types the server places itself on the Adventure table (inferred, see <see cref="ActiveRuleRows"/>).</summary>
        private const uint TeamSpawnersRule = 12, NeutralSpawnersRule = 13;

        private static string RuleKey(uint ruleType) => $"ig_rule_{ruleType}";

        /// <summary>The "Game Name" the owner typed in the 0.695 editor (packed text, <see cref="SaveText"/>).</summary>
        private const string GameNameKey = "ig_game_name";

        /// <summary>
        /// Script props of the 0.695 Game Props tab (the Score Node and Victory Node are two more special game table
        /// props, 29 Sep 2016) with their names (PropDefs 3011 "Score Node" 577664, 3010 "Victory Node" 560924; the
        /// live
        /// prop dump names them "prop: games: ..." with models 1744 PointsNode / 1745 VictoryNode). The client table that
        /// lists them per rule set (RuleSetScriptPropDefinition, FUN_143b75220) is not shipped: offered on the Adventure
        /// table, any number (inferred).
        /// </summary>
        public const uint ScoreNodeProp = 3011, VictoryNodeProp = 3010;
        private static readonly Dictionary<uint, uint> ScriptProps = new() { [ScoreNodeProp] = 577664, [VictoryNodeProp] = 560924 };

        // Link sentences (text ids; storytools-695.md §4.14). Score Node actions: "...Add points to the triggering
        // Luminary's score." and "...Add points to Team 1-4."; its events "When a player scores..." / "When a Team
        // scores...". Victory Node: "...Declare Victory for Team 1-4." / "...for the Triggering Team.". Control Point
        // 1224: "...Attempt to control." and "When control point is captured...".
        public const uint ScorePlayerAction = 577668, ScoreTeam1Action = 577684, ScoreTeam4Action = 577687;
        public const uint VictoryTeam1Action = 577688, VictoryTeam4Action = 577691, VictoryTriggeringTeamAction = 577692;
        public const uint AttemptControlAction = 577715;
        public const uint PlayerScoresEvent = 577665, TeamScoresEvent = 577666, ControlPointCapturedEvent = 577708;

        /// <summary>
        /// The Game Table's own Link events (prop 2313): "On Game Started" (text 573010) and "On Game Ended" (573011), a pair
        /// in both clients' text (next to 573006 "Spawn Automatically" and the other story-tool strings of 0.589). "On Game
        /// Ended" is the Link the developers named for the reworked table (29 Sep 2016 thread, P6517: an On Game Ended
        /// link from the table to a chest) [P: name and owner]; "On Game Started" is its pair [I, strong: same wording,
        /// same block; no source mentions it]. Raised by <see cref="StartRound"/> / <see cref="EndRound"/>
        /// (<see cref="RaiseGameEnded"/>). OPEN-LEADS L21.
        /// </summary>
        public const uint GameStartedEvent = 573010, GameEndedEvent = 573011;

        /// <summary>
        /// "On Game Ended" (<see cref="GameEndedEvent"/>) on the table, once per round end (time limit, point limit, a
        /// Victory Node, the Adventure's completion, the dev "gameend"); not when a round stops because everyone left
        /// (nothing ended: the round was abandoned) [I]. It runs after the result is set and the round has left Running (so
        /// Score / Victory Nodes in its chain do nothing) and before an Adventure round switches the site's Play Mode back
        /// off, so the chain still finds the site's running props (P6517's table → chest). The triggering character [I,
        /// ours]: the round's winner — the winning player in a free-for-all, the best scorer of the winning team in a team
        /// game — so "...Loot this chest." rewards the winner (and their group, P6503) and an IF Gate's "triggering
        /// character's game team" (P6535) reads the winning team; a draw has none. Live's choice is not known (L21).
        /// </summary>
        private static void RaiseGameEnded(Game game)
        {
            Player winner;
            lock (sync)
                winner = game.Rules.TeamCount > 0
                    ? game.WinnerTeam >= 0
                        ? game.Players.Where(p => p.Team == game.WinnerTeam).OrderByDescending(p => p.Score).ThenBy(p => p.Deaths).FirstOrDefault()
                        : null
                    : game.Players.FirstOrDefault(p => game.WinnerPlayer != 0 && p.Session.PlayerGuid == game.WinnerPlayer);
            RaiseTableEvent(game, GameEndedEvent, winner?.Session);
        }

        /// <summary>
        /// Raises one of the table's own events. Marked as a story chain while it runs, so a round ending asked for inside
        /// it (a Victory Node, "...Give reward and teleport to a Chaos Cavern.") waits for the next tick instead of ending
        /// or switching Play Mode under the chain (<see cref="EndRound"/>).
        /// </summary>
        private static void RaiseTableEvent(Game game, uint eventId, WorldSession who)
        {
            bool outer = !inStoryChain;
            inStoryChain = true;
            try
            {
                StoryFire(game.Guid, eventId, who);
            }
            finally
            {
                if (outer)
                    inStoryChain = false;
            }
        }

        /// <summary>EditableProperties row 109 "Point Amount" (577667; −100..100, step 1, default 1) of the Score Node.</summary>
        public const string ScoreNodeAmountKey = "score_node_point_amount";

        /// <summary>Event types (FUN_1434e7900): 10 a player was granted points, 11 a team was.</summary>
        private const uint EventPointGrantPlayer = 10, EventPointGrantTeam = 11;

        #region rule types and active rules

        /// <summary>An IgRuleTypes row (0.695 adds INTERNAL_DESCRIPTION, IS_PLAYER_ADDABLE).</summary>
        private record RuleType(uint Id, uint NameId, bool SinglyAddable, int MinTeams, int MaxTeams, bool TeamlessAllowed,
            string Internal, bool PlayerAddable);

        private static Dictionary<uint, RuleType> ruleTypes;

        private static Dictionary<uint, RuleType> RuleTypes()
            => ruleTypes ??= GameDataFiles.Rows("IgRuleTypes.txt")
                .Select(r => new RuleType(r.U(0), r.U(1), r.I(3) != 0, r.I(4), r.I(5), r.I(6) != 0, r.S(8), r.I(9) != 0))
                .GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());

        /// <summary>Does a rule type suit a game with this many teams (IS_TEAMLESS_GAME_ALLOWED, MIN / MAX_TEAM_COUNT, 0 = any)?</summary>
        private static bool RuleFits(RuleType type, int teams)
            => (teams > 0 || type.TeamlessAllowed) && (type.MinTeams <= 0 || teams >= type.MinTeams) && (type.MaxTeams <= 0 || teams <= type.MaxTeams);

        /// <summary>
        /// The rules a table runs: its rule set's IgRules rows, plus / minus the player-addable rules switched in the game
        /// editor (AddOrRemoveRule; saved on the table), without rules that don't suit the team count. On the Adventure
        /// table, team games get Team Spawners (one spawn point per team: "Team Spawn Points determine where players in
        /// each team will spawn/respawn") in place of Neutral Spawners, free-for-all games Neutral Spawners (inferred:
        /// neither rule is player-addable, and IgRuleTypes 12 is not allowed teamless).
        /// </summary>
        private static List<(uint Index, uint Type)> ActiveRuleRows(Game game, Rules baseSet, int teamCount)
        {
            var rows = new List<(uint Index, uint Type)>(baseSet.RuleRows);
            foreach (RuleType type in RuleTypes().Values.Where(t => t.PlayerAddable))
            {
                if (!PropSettings.TryGetSaved(game.Guid, RuleKey(type.Id), out float on))
                    continue;
                bool has = rows.Any(r => r.Type == type.Id);
                if (on != 0 && !has)
                    rows.Add((SyntheticRuleBase + type.Id, type.Id));
                else if (on == 0 && has)
                    rows.RemoveAll(r => r.Type == type.Id);
            }
            rows.RemoveAll(r => RuleTypes().TryGetValue(r.Type, out RuleType t) && !RuleFits(t, teamCount));
            if (baseSet.Id == AdventureRuleSet)
            {
                uint wanted = teamCount > 0 ? TeamSpawnersRule : NeutralSpawnersRule;
                rows.RemoveAll(r => r.Type == (teamCount > 0 ? NeutralSpawnersRule : TeamSpawnersRule));
                if (!rows.Any(r => r.Type == wanted))
                    rows.Add((SyntheticRuleBase + wanted, wanted));
            }
            return rows;
        }

        #endregion

        #region general settings

        /// <summary>RefIgGeneralSettingsTypes ids of the per-team settings, team 1-4 (TeamName4 is 21, after the others).</summary>
        private static readonly uint[] teamNameSettings = { 14, 15, 16, 21 }, teamOpenSettings = { 17, 18, 19, 20 },
            victoryMessageSettings = { 25, 26, 27, 28 };

        /// <summary>
        /// The game fields a table's general settings decide. <paramref name="value"/> gives a setting's value (saved or
        /// default) by RefIgGeneralSettingsTypes id, null when the rule set has no such setting.
        /// <list type="bullet">
        /// <item>Teams: Is Free-for-All (7) → 0, else Number of Teams (1, 0-4).</item>
        /// <item>Players (inferred from the names): team games take Min / Max Players Per Team (2 / 3) times the teams
        /// that allow players; free for all takes Players needed to start (24, shown only for FFA games: requirement
        /// 49) or Min Players Per Team, and Max Players Per Team.</item>
        /// <item>PvP as the 0.695 client's hostility test (FUN_142ea9630): other teams are always enemies, free-for-all
        /// players only with PvP Allowed (11). A game where nobody is an enemy (one team, or FFA without PvP) is
        /// cooperative.</item>
        /// <item>Allow team switching (12), Team n Allow Players (17-20), Team n Name (14-16, 21), Team n Victory
        /// Message (25-28), Allow Observers (8), Difficulty (10), MaxQueueJoinTime (4: also the countdown, as before).</item>
        /// </list>
        /// </summary>
        private static void ApplyGeneral(Rules own, Func<uint, string> value)
        {
            bool? Bool(uint id) => value(id) is string s ? IsTrue(s) : null;
            int Int(uint id, int fallback) => value(id) is string s && TryNumber(s, out float f) ? (int)f : fallback;
            if (value(1) != null || value(7) != null)
                own.TeamCount = Bool(7) == true ? 0 : Math.Clamp(Int(1, own.TeamCount), 0, 4);
            own.TeamNames = teamNameSettings.Select(id => value(id) ?? "").ToArray();
            own.TeamOpen = teamOpenSettings.Select(id => Bool(id) ?? true).ToArray();
            own.VictoryMessages = victoryMessageSettings.Select(id => value(id) ?? "").ToArray();
            own.AllowSwitch = Bool(12) ?? false;
            own.MinPerTeam = Math.Max(1, Int(2, 2));
            own.MaxPerTeam = Math.Max(own.MinPerTeam, Int(3, 16));
            if (own.TeamCount > 0)
            {
                int open = Math.Max(1, Enumerable.Range(0, own.TeamCount).Count(t => TeamAllowsPlayers(own, t)));
                own.MinPlayers = own.MinPerTeam * open;
                own.MaxPlayers = Math.Max(own.MinPlayers, own.MaxPerTeam * open);
            }
            else
            {
                own.MinPlayers = Math.Max(1, value(24) != null ? Int(24, 1) : own.MinPerTeam);
                own.MaxPlayers = Math.Max(own.MinPlayers, own.MaxPerTeam);
            }
            own.CountdownMs = Int(4, 30000);
            own.AllowsObservers = Bool(8) ?? true;
            own.Difficulty = Int(10, 0);
            // Without a PvP Allowed row (11), a free-for-all set fights only when IgRuleSets HAS_FFA_TEAM_BY_DEFAULT says
            // so (2 / 7; not 5 All Scoring), matching the setting the editor sends the client (L97) [I].
            own.PvP = own.TeamCount > 0 || (Bool(11) ?? own.HasFfaTeam);
            own.Cooperative = own.TeamCount == 1 || (own.TeamCount == 0 && !own.PvP);
        }

        /// <summary>
        /// InstancedGames_ScoreVictoryCond / ScoreVictoryValue (22 / 23; texts 577662 "Score Based Victory", 577663
        /// "Victory Points Required"): no 0.695 rule set has rows for them, but a set that did would end the game at that
        /// score — the team's in team games, a player's otherwise (inferred).
        /// </summary>
        private static void ApplyScoreVictorySetting(Rules own, Func<uint, string> value)
        {
            if (value(22) is not string on || !IsTrue(on) || value(23) is not string text || !TryNumber(text, out float points) || points <= 0)
                return;
            if (own.TeamCount > 0)
                own.TeamPointLimit = (int)points;
            else
                own.PointLimit = (int)points;
        }

        /// <summary>The game's name: the 0.695 editor's "Game Name" when set, else the table's own name (may be empty).</summary>
        private static string NameOf(Game game)
            => LoadText(game.Guid, GameNameKey) is { Length: > 0 } name ? name : game.CustomName ?? "";

        /// <summary>The Adventure table (0.695: the Game Table) plays its site's story during rounds (see StartRound).</summary>
        private static bool PlaysStory(Game game) => game.Rules.Id == AdventureRuleSet;

        /// <summary>
        /// Text settings (team names, victory messages, the game name) are kept as prop settings like the numbers: UTF-8
        /// bytes three to a value (exact in the f32 the settings store), "key#n" = the byte count, "key#i" = bytes
        /// 3i..3i+2. They go away with the table (PropSettings.PropRemoved). Ours: the settings store has no strings.
        /// </summary>
        private const int MaxTextBytes = 96;

        private static void SaveText(ulong prop, string key, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
            PropSettings.Set(prop, key + "#n", bytes.Length);
            for (int i = 0; i < bytes.Length; i += 3)
            {
                int v = bytes[i] | (i + 1 < bytes.Length ? bytes[i + 1] << 8 : 0) | (i + 2 < bytes.Length ? bytes[i + 2] << 16 : 0);
                PropSettings.Set(prop, $"{key}#{i / 3}", v);
            }
        }

        private static string LoadText(ulong prop, string key)
        {
            if (!PropSettings.TryGetSaved(prop, key + "#n", out float n))
                return null;
            int length = Math.Clamp((int)n, 0, MaxTextBytes);
            var bytes = new byte[length];
            for (int i = 0; i < length; i += 3)
            {
                if (!PropSettings.TryGetSaved(prop, $"{key}#{i / 3}", out float f))
                    return null;
                int v = (int)f;
                bytes[i] = (byte)v;
                if (i + 1 < length)
                    bytes[i + 1] = (byte)(v >> 8);
                if (i + 2 < length)
                    bytes[i + 2] = (byte)(v >> 16);
            }
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>A typed text setting: trimmed, no control characters, at most <see cref="MaxTextBytes"/> UTF-8 bytes.</summary>
        private static string CleanText(string text)
        {
            var s = new string((text ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
            while (Encoding.UTF8.GetByteCount(s) > MaxTextBytes)
                s = s[..^1];
            return s;
        }

        /// <summary>
        /// The editor's Reset (0.695 ResetGameSettings: SetConfiguration with the reset flag): the table's general
        /// settings, rule switches, rule parameters and game name go back to the rule set's defaults.
        /// </summary>
        private static void ResetSettings(Game game)
        {
            Rules baseSet = rules[placementProps[game.PropId]];
            foreach (GeneralSetting g in GeneralSettings().Where(g => g.Set == baseSet.Id && g.Adjustable))
            {
                if (g.ValueType == 4)
                    SaveText(game.Guid, GeneralKey(g.SettingId), g.Default);
                else if (PropSettings.TryGetSaved(game.Guid, GeneralKey(g.SettingId), out _))
                    PropSettings.Set(game.Guid, GeneralKey(g.SettingId), g.ValueType == 3 ? (IsTrue(g.Default) ? 1 : 0)
                        : TryNumber(g.Default, out float d) ? d : 0);
            }
            foreach (RuleType type in RuleTypes().Values.Where(t => t.PlayerAddable))
                if (PropSettings.TryGetSaved(game.Guid, RuleKey(type.Id), out _))
                    PropSettings.Set(game.Guid, RuleKey(type.Id), baseSet.RuleRows.Any(r => r.Type == type.Id) ? 1 : 0);
            Dictionary<uint, int> natives = ValuesFor(baseSet.RuleRows);
            foreach (var p in typeParamRows.Values.SelectMany(l => l))
                if (PropSettings.TryGetSaved(game.Guid, ParameterKey(p.Id), out _))
                    PropSettings.Set(game.Guid, ParameterKey(p.Id), natives.TryGetValue(p.Id, out int v) ? v
                        : int.TryParse(p.Default, out int d) ? d : bool.TryParse(p.Default, out bool b) && b ? 1 : 0);
            if (LoadText(game.Guid, GameNameKey) != null)
                SaveText(game.Guid, GameNameKey, "");
        }

        /// <summary>
        /// Live refused changes to a running game: "Error! You cannot modify your game while it is running!"
        /// (CodeStringMappings InstancedGame.ModifyErrorGameRunning = 564516; no client code uses the key, so the server
        /// sent it). Running = the countdown or the round (inferred); the text goes to chat (route inferred).
        /// </summary>
        private static bool RefuseWhileRunning(WorldSession session, Game game)
        {
            if (game.State != StateRunning && game.State != StateWaitingForReady)
                return false;
            Chat.System(session, Shared.GameTable.Text.TextManager.Instance.GetTextForId(564516)
                ?? "Error! You cannot modify your game while it is running!");
            SendConfiguration(session, game);
            log.Info($"{session.CharacterName}: changes to {Title(game)} refused (game running).");
            return true;
        }

        /// <summary>
        /// The table's settings changed (editor apply / reset, a rule switched): outside a round the game takes them at
        /// once (teams, players, names: <see cref="FitTeams"/>); the editors, players and observers get the new
        /// configuration (0.695 clients use it for team names, hostility, switching and victory texts), the editors the
        /// new prop recipe, the site its walk-on info.
        /// </summary>
        private static void SettingsChanged(Game game)
        {
            if (game.State != StateRunning && game.State != StateWaitingForReady)
            {
                game.Rules = RulesFor(game);
                FitTeams(game);
                SendTeamMembers(game);
                SendScoreboard(game);
            }
            List<WorldSession> viewers;
            lock (sync)
                viewers = game.Editors.Concat(game.Players.Where(p => !p.Arriving).Select(p => p.Session)).Concat(game.Observers)
                    .Where(s => s.RezoneTarget == null).Distinct().ToList();
            foreach (WorldSession viewer in viewers)
                SendConfiguration(viewer, game);
            RefreshEditors(game, force: true);
            Claims.SendGameInfo(game.ClaimId);
        }

        /// <summary>
        /// After the rules changed: one score per team, and every player on a team that exists and allows players
        /// (others move to <see cref="PickTeam"/>'s choice; free for all: team 0).
        /// </summary>
        private static void FitTeams(Game game)
        {
            lock (sync)
            {
                int n = Math.Max(1, game.Rules.TeamCount);
                if (game.TeamScores.Length != n)
                {
                    var scores = new int[n];
                    Array.Copy(game.TeamScores, scores, Math.Min(n, game.TeamScores.Length));
                    game.TeamScores = scores;
                }
                foreach (Player p in game.Players)
                    if (game.Rules.TeamCount == 0)
                        p.Team = 0;
                    else if (!TeamAllowsPlayers(game.Rules, p.Team))
                        p.Team = PickTeam(game, p);
            }
        }

        #endregion

        #region 0.695 requests

        /// <summary>
        /// The 0.695-only InstancedGame requests:
        /// <list type="bullet">
        /// <item>GetConfigurationRequest 0xD {u64 game} (FUN_1434eca50; sent on entering a site whose claim entry has a
        /// game, FUN_142dc8ec0): answered with the configuration, so every visitor has the game's team names, PvP flag,
        /// switching and joinable teams.</item>
        /// <item>AddOrRemoveRuleRequest 0x18 {u64 game (manager +0x30), u32 rule type (the check box's ValueIdentifier =
        /// IgRuleTypes id), u8 add, u64 the trigger the client holds for that type (0 / invalid when none)}
        /// (FUN_1434f4d60, writer FUN_1434da290 → FUN_1434d5e90): switches a player-addable rule on the table.</item>
        /// </list>
        /// </summary>
        private static bool HandleRework(WorldSession session, uint sub, byte[] body)
        {
            if (sub == SubGetConfigurationRequest)
            {
                ulong guid = body.Length >= 8 ? BitConverter.ToUInt64(body, 0) : 0;
                Game game = (guid != 0 ? GameByGuid(guid) : null) ?? GameAt(session);
                if (game != null)
                    SendConfiguration(session, game);
                return true;
            }
            if (sub != SubAddOrRemoveRule)
                return false;
            if (body.Length < 13)
                return true;
            var r = new GamePacketReader(body);
            ulong gameGuid = r.ReadULong();
            uint ruleType = r.ReadUInt();
            bool add = r.ReadByte() != 0;
            ulong trigger = r.BytesRemaining >= 8 ? r.ReadULong() : 0;
            Game target = (gameGuid != 0 ? GameByGuid(gameGuid) : null) ?? GameAt(session);
            if (target == null || !Props.CanEdit(session, target.Guid) || !RuleTypes().TryGetValue(ruleType, out RuleType type) || !type.PlayerAddable)
            {
                log.Info($"{session.CharacterName}: AddOrRemoveRule {ruleType} ({(add ? "add" : "remove")}, trigger {trigger}) for 0x{gameGuid:X} refused.");
                if (target != null)
                    SendConfiguration(session, target);
                return true;
            }
            if (RefuseWhileRunning(session, target))
                return true;
            if (add && !RuleFits(type, RulesFor(target).TeamCount))
            {
                Chat.System(session, "That rule doesn't suit this game's teams.");
                SendConfiguration(session, target);
                return true;
            }
            PropSettings.Set(target.Guid, RuleKey(ruleType), add ? 1 : 0);
            log.Info($"{session.CharacterName} {(add ? "added" : "removed")} rule {ruleType} ({type.Internal}) {(add ? "to" : "from")} {Title(target)} on site {target.ClaimId}.");
            SettingsChanged(target);
            return true;
        }

        /// <summary>
        /// RequestJoinTeam (Request type 9, value = the client's team number = server team + 1; Lua
        /// InstancedGame.SwitchTeam from the "Switch Team" drop-down of InstancedGameScoreboard.JoinableTeams). The
        /// client sends it only while in the game and not queued; it offers switching only with 2+ teams and, from state
        /// 6 on, with Allow team switching (FUN_142dc9660) — the server checks the same, plus that the team allows players
        /// and has room (Max Players Per Team). There is no answer type (the S→C Request handler knows 4 / 5 / 6 only):
        /// TeamMembers and the scoreboard show the new team (L31). In a running round the player respawns at the new
        /// team's spawn point (inferred).
        /// </summary>
        private static void SwitchTeam(WorldSession session, Game game, short clientTeam)
        {
            Player p = game?.Find(session);
            if (p == null)
                return;
            int team = clientTeam - 1;
            Rules set = game.Rules;
            string refusal = set.TeamCount < 2 || team < 0 || team >= set.TeamCount ? "There is no such team."
                : team == p.Team ? null
                : game.State > StateReady && !set.AllowSwitch ? "This game doesn't allow switching teams while it is on."
                : !TeamAllowsPlayers(set, team) ? $"{TeamName(game, team)} doesn't take players."
                : game.Players.Count(x => x.Team == team) >= set.MaxPerTeam ? $"{TeamName(game, team)} is full."
                : "";
            if (refusal == null)
                return;
            if (refusal.Length > 0)
            {
                Chat.System(session, refusal);
                log.Info($"{session.CharacterName}: switch to team {clientTeam} in {Title(game)} refused ({refusal}).");
                return;
            }
            lock (sync)
                p.Team = team;
            if (game.State == StateRunning)
                Respawn(game, p);
            SendTeamMembers(game);
            SendScoreboard(game);
            Claims.SendGameInfo(game.ClaimId);
            Broadcast(game, $"{session.CharacterName} switched to {TeamName(game, team)}.");
            log.Info($"{session.CharacterName} switched to team {team} in {Title(game)} on site {game.ClaimId}.");
        }

        #endregion

        #region script props (Game Props tab)

        /// <summary>The Game Props tab's script prop rows: each placed Score / Victory Node on the site, then an Add row.</summary>
        private static IEnumerable<RecipeItem> ScriptPropRows(Game game)
        {
            if (game.Rules.Id != AdventureRuleSet)
                yield break;
            foreach ((uint propId, uint nameId) in ScriptProps)
            {
                var placed = Props.PlacedOf(game.Zone, new HashSet<uint> { propId })
                    .Where(p => Claims.ClaimAt(game.Zone, p.Position) == game.ClaimId).OrderBy(p => p.Guid).ToList();
                uint slot = 0;
                foreach (var p in placed)
                    yield return new RecipeItem(0, nameId, 0, slot++, p.Guid, 0, true, propId);
                yield return new RecipeItem(0, nameId, 0, slot, 0, 0, true, propId);
            }
        }

        /// <summary>
        /// The Game Props tab's Add / Remove on a script prop row: Add (PropRequest with its ScriptPropId) starts placing
        /// that prop, free like the markers; Remove (PropRemove of a placed Score / Victory Node on the site) picks it up.
        /// False when the request is not about a script prop.
        /// </summary>
        private static bool HandleScriptProp(WorldSession session, Game game, uint sub, ulong prop, uint scriptProp)
        {
            if (sub == SubPropRequest)
            {
                if (scriptProp == 0 || !ScriptProps.ContainsKey(scriptProp))
                    return false;
                if (game.Rules.Id != AdventureRuleSet || !Props.CanEdit(session, game.Guid))
                {
                    log.Info($"{session.CharacterName}: script prop {scriptProp} for {Title(game)} refused.");
                    return true;
                }
                Props.PlacedProp started = Props.BeginPlacement(session, scriptProp, free: true);
                if (started == null)
                    Chat.System(session, "You can't place that right now.");
                log.Info($"{session.CharacterName} adds script prop {scriptProp} to {Title(game)}: {(started != null ? "placing" : "refused")}.");
                return true;
            }
            if (sub != SubPropRemove)
                return false;
            Props.PlacedProp placed = Props.Find(prop);
            if (placed == null || !ScriptProps.ContainsKey(placed.PropId))
                return false;
            if (!Props.CanEdit(session, game.Guid) || placed.Zone != game.Zone
                || Claims.ClaimAt(placed.Zone, new Vector3(placed.Spawn.Position.X, placed.Spawn.Position.Y, placed.Spawn.Position.Z)) != game.ClaimId)
            {
                log.Info($"{session.CharacterName}: removing script prop 0x{prop:X} refused.");
                return true;
            }
            Props.PickUp(session, prop, false);
            RefreshEditors(game, force: true);
            return true;
        }

        /// <summary>The Score Node's settings window: row 109 Point Amount (EditableProperties, the row named for the node).</summary>
        private static List<PropSettings.Definition> ScoreNodeSettings()
            => new() { new PropSettings.Definition(109, ScoreNodeAmountKey, 577667, 0, 1f, 100f, -100f, 1f) };

        #endregion

        #region Score Node, Victory Node, Control Point Links

        /// <summary>
        /// Story-tool hook: raises a prop's Link event (prop guid, event text id, triggering player or null) through the
        /// story tools. Set by StoryTools (see the 2.12 notes: it owns the running props and their Links); while unset
        /// the game events below simply have no Links to run.
        /// </summary>
        public static Action<ulong, uint, WorldSession> StoryEvent { get; set; }

        [ThreadStatic]
        private static int storyDepth;

        /// <summary>Set while a story Link action runs here (<see cref="LinkAction"/>): round endings wait for the next tick.</summary>
        [ThreadStatic]
        private static bool inStoryChain;

        /// <summary>Work for the next server tick (<see cref="Update"/>).</summary>
        private static readonly List<Action> deferred = new();

        /// <summary>Raises a Link event via <see cref="StoryEvent"/>, at most 4 nested (a Score Node linked to itself).</summary>
        private static void StoryFire(ulong prop, uint eventId, WorldSession who)
        {
            Action<ulong, uint, WorldSession> hook = StoryEvent;
            if (hook == null || prop == 0)
                return;
            if (storyDepth >= 4)
            {
                log.Debug($"Story event {eventId} on 0x{prop:X} not raised: game Links nested too deep.");
                return;
            }
            storyDepth++;
            try
            {
                hook(prop, eventId, who);
            }
            finally
            {
                storyDepth--;
            }
        }

        /// <summary>The site game a prop stands in.</summary>
        private static Game GameOfProp(ulong prop)
        {
            Props.PlacedProp placed = Props.Find(prop);
            if (placed == null)
                return null;
            var at = new Vector3(placed.Spawn.Position.X, placed.Spawn.Position.Y, placed.Spawn.Position.Z);
            return Claims.ClaimAt(placed.Zone, at) is ulong claim ? GameOn(claim) : null;
        }

        /// <summary>
        /// A story Link action on a game prop (called by StoryTools.Execute; true when the action is one of these, whether
        /// or not it had an effect). Only in a running round of the site's game.
        /// <list type="bullet">
        /// <item>Score Node 577668 "...Add points to the triggering Luminary's score." / 577684-577687 "...Add points to
        /// Team 1-4.": the node's Point Amount (row 109) to that player (and, in team games, their team: the chat line
        /// reads "has scored N points for Team") / to that team; event 10 / 11 (chat lines 577670 / 577669 / 577671); the
        /// node's "When a player scores..." / "When a Team scores..." Links; then the victory check. Patch note:
        /// objects linked to a Score Node give the player who clicked them the number of points the builder set. A
        /// triggering character who isn't playing gets nothing; team actions in a free-for-all or for a team
        /// the game doesn't have do nothing (inferred).</item>
        /// <item>Victory Node 577688-577691 "...Declare Victory for Team 1-4." / 577692 "...for the Triggering Team.":
        /// ends the round with that winner ("arbitrarily end the game by your own rules"). Free for all: the triggering
        /// action makes the triggering player the winner, a team action ends the round on score (inferred).</item>
        /// <item>Control Point 577715 "...Attempt to control.": the triggering character takes the point for their side
        /// at once, unless another side stands on it (it says "attempt" because taking the point can fail, Terrogaunt
        /// 30 Sep 2016) (inferred: the failure rule). Story creatures have no game
        /// side here, so their attempts fail (open).</item>
        /// </list>
        /// </summary>
        public static bool LinkAction(ulong prop, uint action, WorldSession player, ulong creature)
        {
            bool score = action == ScorePlayerAction || (action >= ScoreTeam1Action && action <= ScoreTeam4Action);
            bool victory = action >= VictoryTeam1Action && action <= VictoryTriggeringTeamAction;
            if (!score && !victory && action != AttemptControlAction)
                return false;
            bool outer = !inStoryChain;
            inStoryChain = true;
            try
            {
                RunLinkAction(prop, action, player, creature, score, victory);
            }
            finally
            {
                if (outer)
                    inStoryChain = false;
            }
            return true;
        }

        private static void RunLinkAction(ulong prop, uint action, WorldSession player, ulong creature, bool score, bool victory)
        {
            EnsureLoaded();
            Game game = GameOfProp(prop);
            if (game == null || game.State != StateRunning)
            {
                log.Debug($"Game Link {action} on 0x{prop:X}: no running game on its site.");
                return;
            }
            Player who = player != null ? game.Find(player) : null;
            if (score)
            {
                int amount = (int)Math.Round(PropSettings.Get(prop, ScoreNodeAmountKey));
                if (amount == 0)
                    return;
                if (action == ScorePlayerAction)
                {
                    if (who != null)
                        GrantPlayerPoints(game, who, amount, prop);
                    else
                        log.Debug($"Score Node 0x{prop:X}: the triggering character isn't playing {Title(game)}.");
                }
                else
                {
                    int team = (int)(action - ScoreTeam1Action);
                    if (team < game.Rules.TeamCount)
                        GrantTeamPoints(game, team, amount, prop, player);
                }
                return;
            }
            if (victory)
            {
                if (action == VictoryTriggeringTeamAction)
                {
                    if (who == null)
                        return;
                    if (game.Rules.TeamCount > 0)
                        EndRound(game, DateTime.UtcNow, "victory declared", who.Team);
                    else
                        EndRound(game, DateTime.UtcNow, "victory declared", null, who);
                }
                else
                {
                    int team = (int)(action - VictoryTeam1Action);
                    if (game.Rules.TeamCount == 0)
                        EndRound(game, DateTime.UtcNow, "victory declared");
                    else if (team < game.Rules.TeamCount)
                        EndRound(game, DateTime.UtcNow, "victory declared", team);
                }
                return;
            }
            AttemptControl(game, prop, who, creature);
            return;
        }

        /// <summary>Control Point "...Attempt to control." (see <see cref="LinkAction"/>).</summary>
        private static void AttemptControl(Game game, ulong prop, Player who, ulong creature)
        {
            ControlPoint cp;
            int side;
            lock (sync)
            {
                cp = game.Points.FirstOrDefault(c => c.Guid == prop);
                if (cp == null || who == null || who.RespawnAt != null || who.Arriving || Health.IsDead(who.Session))
                {
                    log.Debug($"Attempt to control 0x{prop:X} failed ({(cp == null ? "not a control point of the round" : creature != 0 ? "a creature has no side" : "no living player")}).");
                    return;
                }
                side = SideOf(game, who);
                if (cp.Owner == side)
                    return;
                ControlPoint point = cp;
                bool contested = game.Players.Any(o => SideOf(game, o) != side && o.RespawnAt == null && !o.Arriving
                    && !Health.IsDead(o.Session) && Vector3.Distance(o.Session.Position, point.Position) <= point.Radius);
                if (contested)
                {
                    log.Debug($"{who.Session.CharacterName}'s attempt to control {cp.Name} failed (contested).");
                    return;
                }
                cp.Owner = side;
                cp.Capturer = -1;
                cp.Progress = 0f;
                AddSideScore(game, side, game.Rules.PointsPerCapture);
            }
            AnnounceCapture(game, cp, side, who);
            SendHudAll(game);
            SendScoreboard(game);
            CheckScoreVictory(game, DateTime.UtcNow);
        }

        /// <summary>Points for a player (and their team in team games) from a Score Node (or dev "gamescore").</summary>
        private static void GrantPlayerPoints(Game game, Player p, int points, ulong node)
        {
            bool teams = game.Rules.TeamCount > 0;
            lock (sync)
            {
                p.Score += points;
                if (teams && p.Team >= 0 && p.Team < game.TeamScores.Length)
                    game.TeamScores[p.Team] += points;
            }
            SendPointEvent(game, EventPointGrantPlayer, p.Session.PlayerGuid, p.Session.CharacterName ?? "", teams ? ClientSide(game, p.Team) : 0, points);
            SendScoreboard(game);
            log.Info($"{Title(game)}: {p.Session.CharacterName} scored {points} point(s){(node != 0 ? $" (Score Node 0x{node:X})" : "")}.");
            StoryFire(node, PlayerScoresEvent, p.Session);
            if (teams)
                StoryFire(node, TeamScoresEvent, p.Session);
            CheckScoreVictory(game, DateTime.UtcNow);
        }

        /// <summary>Points for a team from a Score Node's "...Add points to Team n.".</summary>
        private static void GrantTeamPoints(Game game, int team, int points, ulong node, WorldSession trigger)
        {
            lock (sync)
            {
                if (team < 0 || team >= game.TeamScores.Length)
                    return;
                game.TeamScores[team] += points;
            }
            SendPointEvent(game, EventPointGrantTeam, 0, "", ClientSide(game, team), points);
            SendScoreboard(game);
            log.Info($"{Title(game)}: {TeamName(game, team)} scored {points} point(s) (Score Node 0x{node:X}).");
            StoryFire(node, TeamScoresEvent, trigger);
            CheckScoreVictory(game, DateTime.UtcNow);
        }

        /// <summary>
        /// Event 10 / 11 (0.695 only; 0.589 has no such types): u32 type, i32 team (unused by these lines: -1), i32 other
        /// team (+0x20 = targetTeam / targetColor; type 10 picks PointGrantPlayerFFA when it is below 1, else
        /// PointGrantPlayerOnTeam), u64 actor 0, u64 other actor (+0x30 = targetPlayer), then the tail with the player's
        /// name as the target name and pointCount (+0x6c). To players in place and observers.
        /// </summary>
        private static void SendPointEvent(Game game, uint kind, ulong player, string name, int team, int points)
        {
            var output = new List<byte>();
            var w = Begin(output, SubEvent);
            w.Write(kind);
            w.Write(-1);
            w.Write(team);
            w.Write(0ul);
            w.Write(player);
            WriteEventTail(w, name, "", points);
            byte[] data = output.ToArray();
            List<WorldSession> viewers;
            lock (sync)
                viewers = game.Players.Where(p => !p.Arriving).Select(p => p.Session).Concat(Watchers(game)).Distinct().ToList();
            foreach (WorldSession viewer in viewers)
                viewer.EnqueueMessage(ZoneMessageOpcode.InstancedGameBase, data);
        }

        #endregion
    }
}
