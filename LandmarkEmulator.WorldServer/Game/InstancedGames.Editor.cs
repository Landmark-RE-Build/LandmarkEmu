using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The site game editor (the client's RulesetObjectSettings window: "Prop Settings" tab = the game's prop recipe,
    /// "Game Settings" tab, Enable / Disable Game). Decoded from ClientInstancedGameManager (DAT_145358b90):
    ///
    /// - Lua Claims.RequestGainActiveAuthorshipOfGame / RequestLoseActiveAuthorshipOfGame send InstancedGame request
    ///   type 1 / 2 {u64 game} (FUN_14343e590 / FUN_14343e840). The client handles no request reply for these: the
    ///   server answers with the recipe.
    /// - PropRecipe (sub 4, reader FUN_143427a10, handler HandleGameRecipePacket FUN_143438b30): u64 game (manager
    ///   +0x30 = the game being authored; 0 = none → GameEvents:OnCloseInstancedGameSettings), bool game enabled
    ///   (+0x8c), bool has required props (+0x8d), u32 n × item, bool open (→ OnOpenInstancedGameSettings). The list is
    ///   rebuilt from every packet. Item (FUN_1434282f0, 0x48 bytes): u32 id, LocalizedString name {u32 text id, string
    ///   (used when not empty)}, u32 rule index, u32 role, u32 slot, u64 prop guid, bool include (false = remove that
    ///   guid's row), u32 team (non-zero → " - team name" after the role name), bool optional. The window's rows
    ///   (FUN_143440a30, data source RulesetObjects.PropSettings.CategoryList): a placed prop (guid ≠ 0) shows Remove /
    ///   Go To buttons, an unplaced one an Add button and "required" unless optional.
    /// - Lua InstancedGame.RequestPropRecipeList → sub 5 {u64 game}; EnableGame → sub 0xF {u64 game}; DisableGame →
    ///   sub 0x10 {u64 game} (FUN_14343ec70 / FUN_14343e530; allowed by FUN_143440890 for the authored game).
    ///
    /// Roles come from the client tables: IgRules (rule set → rule types), IgPropRoles (rule type → role: per-team flag,
    /// min / max count, name) and IgPropRoleProps (role → props), e.g. Team Spawn Point (1223 / 1367, per team, 1-10),
    /// Neutral Spawn Point (1371), Control Point (1224), Game Marker (1436).
    /// </summary>
    public static partial class InstancedGames
    {
        private const uint SubPropRecipeRequest = 5, SubPropRequest = 0xA, SubPropRemove = 0xB, SubEnableGame = 0xF,
            SubDisableGame = 0x10;

        /// <summary>A prop role a rule type asks for (IgPropRoles + IgPropRoleProps).</summary>
        private record Role(uint Id, uint RuleType, bool PerTeam, int Min, int Max, uint NameId, List<uint> Props);

        private static List<Role> roles;

        private static List<Role> Roles()
        {
            if (roles != null)
                return roles;
            var props = GameDataFiles.Rows("IgPropRoleProps.txt").Select(r => (Role: r.U(1), Prop: r.U(2)))
                .GroupBy(p => p.Role).ToDictionary(g => g.Key, g => g.Select(p => p.Prop).ToList());
            roles = GameDataFiles.Rows("IgPropRoles.txt")
                .Select(r => new Role(r.U(0), r.U(1), r.I(2) == 1, r.I(3), r.I(4), r.U(5),
                    props.TryGetValue(r.U(0), out var list) ? list : new List<uint>()))
                .ToList();
            return roles;
        }

        /// <summary>One row of the editor's prop list.</summary>
        private record RecipeItem(uint RoleId, uint NameId, uint RuleIndex, uint Slot, ulong Prop, uint Team, bool Optional);

        /// <summary>
        /// The game's prop recipe: for every role its rule set's rule types ask for (per team for team roles: team t
        /// uses the role's t-th prop when it has one per team), the props placed on the site, then rows for the
        /// missing required ones and one optional "add" row while the role has room.
        /// </summary>
        private static List<RecipeItem> Recipe(Game game, out bool complete)
        {
            complete = true;
            var items = new List<RecipeItem>();
            int teams = Math.Max(1, game.Rules.TeamCount);
            foreach ((uint ruleIndex, uint ruleType) in game.Rules.RuleRows)
                foreach (Role role in Roles().Where(r => r.RuleType == ruleType))
                    for (int t = 0; t < (role.PerTeam ? teams : 1); t++)
                    {
                        var propIds = role.PerTeam && role.Props.Count >= teams ? new HashSet<uint> { role.Props[t] } : role.Props.ToHashSet();
                        uint team = role.PerTeam ? (uint)(t + 1) : 0u;
                        var placed = Props.PlacedOf(game.Zone, propIds)
                            .Where(p => Claims.ClaimAt(game.Zone, p.Position) == game.ClaimId)
                            .OrderBy(p => p.Guid).ToList();
                        uint slot = 0;
                        foreach (var p in placed)
                            items.Add(new RecipeItem(role.Id, role.NameId, ruleIndex, slot++, p.Guid, team, slot > Math.Max(0, role.Min)));
                        for (int i = placed.Count; i < role.Min; i++)
                        {
                            items.Add(new RecipeItem(role.Id, role.NameId, ruleIndex, slot++, 0, team, false));
                            complete = false;
                        }
                        if (role.Max < 0 || slot < role.Max)
                            items.Add(new RecipeItem(role.Id, role.NameId, ruleIndex, slot, 0, team, true));
                    }
            return items;
        }

        /// <summary>PropRecipe sub 4 to one editor (game null = close the editor).</summary>
        private static void SendRecipe(WorldSession session, Game game, bool open)
        {
            var output = new List<byte>();
            var w = Begin(output, SubPropRecipe);
            if (game == null)
            {
                w.Write(0ul);
                w.Write(false);
                w.Write(false);
                w.Write(0u);
                w.Write(false);
                session.EnqueueMessage(ZoneMessageOpcode.InstancedGameBase, output.ToArray());
                return;
            }
            List<RecipeItem> items = Recipe(game, out bool complete);
            w.Write(game.Guid);
            w.Write(!game.Disabled);
            w.Write(complete);
            w.Write((uint)items.Count);
            foreach (RecipeItem item in items)
            {
                w.Write(item.RoleId);
                w.Write(item.NameId);
                w.Write("");
                w.Write(item.RuleIndex);
                w.Write(item.RoleId);
                w.Write(item.Slot);
                w.Write(item.Prop);
                w.Write(true);
                w.Write(item.Team);
                w.Write(item.Optional);
            }
            w.Write(open);
            session.EnqueueMessage(ZoneMessageOpcode.InstancedGameBase, output.ToArray());
            game.EditorRecipe = Signature(items, game);
        }

        private static string Signature(List<RecipeItem> items, Game game)
            => (game.Disabled ? "d" : "e") + string.Join(",", items.Select(i => $"{i.RoleId}.{i.Team}.{i.Slot}.{i.Prop:X}"));

        /// <summary>Request type 1: the player opens the game editor (needs build rights on the Game Table).</summary>
        private static void GainAuthorship(WorldSession session, Game game)
        {
            if (game == null || !Props.CanEdit(session, game.Guid))
            {
                Chat.System(session, game == null ? "There is no game on this build site." : "You can't edit this game.");
                return;
            }
            lock (sync)
            {
                foreach (Game g in gamesByClaim.Values)
                    g.Editors.Remove(session);
                game.Editors.Add(session);
            }
            SendConfiguration(session, game);
            SendRecipe(session, game, true);
            log.Info($"{session.CharacterName} edits {Title(game)} on site {game.ClaimId}.");
        }

        /// <summary>
        /// The Game Table's Settings (PropBase 0x1B): when the table hosts its site's game, opens the game editor for it.
        /// False for other props (and tables that don't host the game: a second table on a site).
        /// </summary>
        public static bool EditTable(WorldSession session, ulong table)
        {
            EnsureLoaded();
            Game game = GameByGuid(table);
            if (game == null)
                return false;
            GainAuthorship(session, game);
            return true;
        }

        /// <summary>Dev "gameedit [off]": opens / closes the game editor for the site's game.</summary>
        public static void Edit(WorldSession session, bool open)
        {
            EnsureLoaded();
            if (open)
                GainAuthorship(session, GameAt(session));
            else
                LoseAuthorship(session);
        }

        /// <summary>Is this prop a site game marker (any IgPropRoleProps prop: spawn points, control points, markers)?</summary>
        public static bool IsMarker(uint propId) => Roles().Any(r => r.Props.Contains(propId));

        /// <summary>Request type 2 (or leaving the world): the editor closes.</summary>
        private static void LoseAuthorship(WorldSession session, bool tell = true)
        {
            bool was = false;
            lock (sync)
                foreach (Game g in gamesByClaim.Values)
                    was |= g.Editors.Remove(session);
            if (was && tell)
                SendRecipe(session, null, false);
        }

        /// <summary>Editor requests: recipe refresh (5), enable (0xF), disable (0x10); body {u64 game}.</summary>
        private static bool HandleEditor(WorldSession session, uint sub, byte[] body)
        {
            if (sub == SubSetConfiguration)
            {
                HandleSetConfiguration(session, body);
                return true;
            }
            if (sub == SubPropRequest || sub == SubPropRemove)
            {
                HandleEditorProp(session, sub, body);
                return true;
            }
            if (sub != SubPropRecipeRequest && sub != SubEnableGame && sub != SubDisableGame)
                return false;
            ulong guid = body.Length >= 8 ? BitConverter.ToUInt64(body, 0) : 0;
            Game game = (guid != 0 ? GameByGuid(guid) : null) ?? GameAt(session);
            if (game == null || !Props.CanEdit(session, game.Guid))
            {
                log.Info($"{session.CharacterName}: game editor sub 0x{sub:X} for 0x{guid:X} refused.");
                return true;
            }
            if (sub == SubPropRecipeRequest)
            {
                SendRecipe(session, game, false);
                return true;
            }
            SetEnabled(game, sub == SubEnableGame);
            log.Info($"{session.CharacterName} {(game.Disabled ? "disabled" : "enabled")} {Title(game)} on site {game.ClaimId}.");
            return true;
        }

        /// <summary>
        /// The editor's prop list buttons (Lua InstancedGame.AddAdditionalProp / RemoveProp, FUN_14343e9d0 /
        /// FUN_14343eaf0, for the row's rule index / role / team / slot):
        /// - PropRequest (sub 0xA, writer FUN_143426a50): u64 game, u32 rule index, u32 role, u32 team, u32 slot. Starts
        ///   placing the role's prop (team t: the role's t-th prop) the usual way: from a matching item in the bag, else
        ///   paid like the build palette (materials, learned recipe).
        /// - PropRemove (sub 0xB, writer FUN_143426930): u64 prop, u64 game, u32 rule index, u32 role, u32 slot. Picks the
        ///   prop up (the normal pickup and refund).
        /// </summary>
        private static void HandleEditorProp(WorldSession session, uint sub, byte[] body)
        {
            var r = new GamePacketReader(body);
            ulong prop = sub == SubPropRemove ? r.ReadULong() : 0;
            ulong guid = r.ReadULong();
            uint ruleIndex = r.ReadUInt(), roleId = r.ReadUInt();
            uint team = sub == SubPropRequest ? r.ReadUInt() : 0;
            Game game = (guid != 0 ? GameByGuid(guid) : null) ?? GameAt(session);
            Role role = Roles().FirstOrDefault(x => x.Id == roleId);
            if (game == null || role == null || !Props.CanEdit(session, game.Guid)
                || !game.Rules.RuleRows.Any(x => x.Index == ruleIndex && x.Type == role.RuleType))
            {
                log.Info($"{session.CharacterName}: game editor prop sub 0x{sub:X} (role {roleId}) refused.");
                return;
            }
            if (sub == SubPropRemove)
            {
                Props.PlacedProp placed = Props.Find(prop);
                if (placed == null || !role.Props.Contains(placed.PropId) || placed.Zone != game.Zone
                    || Claims.ClaimAt(placed.Zone, new Vector3(placed.Spawn.Position.X, placed.Spawn.Position.Y, placed.Spawn.Position.Z)) != game.ClaimId)
                {
                    log.Info($"{session.CharacterName}: game editor remove of 0x{prop:X} refused.");
                    return;
                }
                Props.PickUp(session, prop, false);
                RefreshEditors(game, force: true);
                return;
            }
            int teams = Math.Max(1, game.Rules.TeamCount);
            uint propId = role.PerTeam && role.Props.Count >= teams && team >= 1 && team <= role.Props.Count
                ? role.Props[(int)team - 1] : role.Props.FirstOrDefault();
            if (propId == 0)
                return;
            var item = session.Inventory.Items.FirstOrDefault(i => Inventory.ItemDefinitionManager.Get(i.ItemDefinitionId)?.PropId == propId
                && i.Container != Inventory.Inventory.EquippedContainer);
            // Markers have no recipe (PropDefs PROP_RECIPE_ID 0): the editor's Add is how they're placed, free.
            Props.PlacedProp started = item != null
                ? Props.BeginPlacement(session, propId, item.ItemDefinitionId, item.Id)
                : Props.BeginPlacement(session, propId, free: true);
            if (started == null)
                Chat.System(session, "You can't place that right now (you need the item, or its materials and recipe).");
            log.Info($"{session.CharacterName} adds prop {propId} (role {role.Id}, team {team}) to {Title(game)}: {(started != null ? "placing" : "refused")}.");
        }

        /// <summary>The game editor's Go To: may this player teleport to that point (they edit a game, it's on its site)?</summary>
        public static bool MayGoTo(WorldSession session, Vector3 to)
        {
            lock (sync)
                return gamesByClaim.Values.Any(g => g.Editors.Contains(session) && g.Zone == session.ZoneId
                    && Claims.ClaimAt(g.Zone, to) == g.ClaimId);
        }

        /// <summary>The setting that keeps a disabled game disabled across restarts (on the Game Table).</summary>
        private const string DisabledKey = "ig_game_disabled";

        /// <summary>
        /// Enables or disables hosting. A disabled game ends its round, sends its players out and can't be joined; the
        /// editors see the new state.
        /// </summary>
        private static void SetEnabled(Game game, bool enabled)
        {
            if (game.Disabled == !enabled)
            {
                RefreshEditors(game, force: true);
                return;
            }
            game.Disabled = !enabled;
            PropSettings.Set(game.Guid, DisabledKey, enabled ? 0 : 1);
            if (!enabled)
                foreach (Player player in game.Players.ToList())
                    RemovePlayer(game, player, "the game was disabled");
            Claims.SendGameInfo(game.ClaimId);
            RefreshEditors(game, force: true);
        }

        #region game settings

        private const uint SubGetConfiguration = 0xD, SubSetConfiguration = 0xE;

        /// <summary>
        /// A rule set's general setting: IgGeneralSetting row (id, setting, rule set, default int / float / bool,
        /// player adjustable, min, max) with its RefIgGeneralSettingsTypes code (InstancedGames_NumberOfTeams ...) and
        /// value type (1 int, 2 float, 3 bool). The client shows a row only when PLAYER_ADJUSTABLE (its definition table
        /// DAT_14535a748 +0x4d0 holds these rows; +0x14 = the flag).
        /// </summary>
        private record GeneralSetting(uint RowId, uint SettingId, uint Set, string Code, uint ValueType, string Default,
            bool Adjustable, int Min, int Max);

        private static List<GeneralSetting> generalSettings;

        private static List<GeneralSetting> GeneralSettings()
        {
            if (generalSettings != null)
                return generalSettings;
            var types = GameDataFiles.Rows("RefIgGeneralSettingsTypes.txt").ToDictionary(r => r.U(0), r => (Type: r.U(1), Code: r.S(2)));
            generalSettings = GameDataFiles.Rows("IgGeneralSetting.txt")
                .Where(r => types.ContainsKey(r.U(1)))
                .Select(r =>
                {
                    var t = types[r.U(1)];
                    string def = t.Type == 3 ? (r.I(5) != 0 ? "True" : "False") : t.Type == 2 ? r.S(4) : r.I(3).ToString();
                    return new GeneralSetting(r.U(0), r.U(1), r.U(2), t.Code, t.Type, def, r.I(7) != 0, r.I(8), r.I(9));
                }).ToList();
            return generalSettings;
        }

        private static string GeneralKey(uint settingId) => $"ig_general_{settingId}";

        /// <summary>A general setting's value on this table (saved in the editor, else the rule set's default).</summary>
        private static string GeneralValue(Game game, GeneralSetting setting)
        {
            if (!PropSettings.TryGetSaved(game.Guid, GeneralKey(setting.SettingId), out float v))
                return setting.Default;
            return setting.ValueType == 3 ? (v != 0 ? "True" : "False") : ((int)v).ToString();
        }

        private static bool IsTrue(string text) => text.Length > 0 && "1tT".Contains(text[0]);

        /// <summary>A general setting of the game's rule set as an int (bools 1 / 0), or the fallback.</summary>
        private static int General(Game game, Rules set, uint settingId, int fallback)
        {
            GeneralSetting setting = GeneralSettings().FirstOrDefault(g => g.Set == set.Id && g.SettingId == settingId);
            if (setting == null)
                return fallback;
            string text = GeneralValue(game, setting);
            return setting.ValueType == 3 ? (IsTrue(text) ? 1 : 0) : int.TryParse(text, out int i) ? i : fallback;
        }

        private record RuleParameter(uint Id, uint ParamType, uint NameId, string Min, string Max, string Default);

        private static Dictionary<uint, List<RuleParameter>> ruleTypeParameters;

        /// <summary>The rule parameters of a rule type in the editor's order (index = position by row id).</summary>
        private static List<RuleParameter> RuleTypeParameters(uint ruleType)
        {
            ruleTypeParameters ??= GameDataFiles.Rows("IgRuleTypeParameters.txt")
                .Select(r => (Type: r.U(1), Parameter: new RuleParameter(r.U(0), r.U(2), r.U(3), r.S(4), r.S(5), r.S(6))))
                .GroupBy(r => r.Type)
                .ToDictionary(g => g.Key, g => g.Select(r => r.Parameter).OrderBy(p => p.Id).ToList());
            return ruleTypeParameters.TryGetValue(ruleType, out var list) ? list : new List<RuleParameter>();
        }

        private static bool TryNumber(string text, out float value)
            => float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);

        /// <summary>
        /// GetConfigurationResponse (sub 0xD, reader FUN_14342b760; the client keeps the defaults block at manager +0xe0
        /// and FUN_1434411d0 fills RulesetObjects.GameSettings.*): {current block, defaults block, rule parameter
        /// definitions, rule parameter values}. Block: LocalizedString name (the window title), LocalizedString, settings
        /// map {u32 n; string code; value {u32 IgGeneralSetting id, string code, string, string value, u32 (stored over
        /// the code string's vtable low half by the client's reader FUN_1434270b0; its string destructors reset the
        /// vtable first, so it's harmless: 0)}}. Definitions: {u32 n; u64 trigger (= the IgRules row), u32 m; m ×
        /// {u32 param type (1 int, 2 float, 4 checkbox, 5 int), string name, u32 name text id, string, string min,
        /// string max}}. Values: {u32 n; u64 trigger, u32 m; m × {string value, u32 rule type, u32 index}} (the client
        /// hides rule types 8 / 9 index 1 and type 11 index 3 / 4). The Min / Max Players sliders take their range from
        /// the defaults block.
        /// </summary>
        private static void SendConfiguration(WorldSession session, Game game)
        {
            var output = new List<byte>();
            var w = Begin(output, SubGetConfiguration);
            var settings = GeneralSettings().Where(g => g.Set == game.Rules.Id).OrderBy(g => g.SettingId).ToList();
            void Block(bool current)
            {
                w.Write(game.Rules.NameId);
                w.Write(current ? game.CustomName ?? "" : "");
                w.Write(0u);
                w.Write("");
                w.Write((uint)settings.Count);
                foreach (GeneralSetting g in settings)
                {
                    string value = current ? GeneralValue(game, g) : g.Default;
                    // The defaults block's player counts are the sliders' range.
                    if (!current && g.SettingId == 2)
                        value = "1";
                    else if (!current && g.SettingId == 3)
                        value = Math.Max(int.TryParse(g.Default, out int d) ? d : 16, 16).ToString();
                    w.Write(g.Code);
                    w.Write(g.RowId);
                    w.Write(g.Code);
                    w.Write("");
                    w.Write(value);
                    w.Write(0u);
                }
            }
            Block(true);
            Block(false);
            var rows = game.Rules.RuleRows.Where(r => RuleTypeParameters(r.Type).Count > 0).ToList();
            w.Write((uint)rows.Count);
            foreach ((uint index, uint type) in rows)
            {
                List<RuleParameter> parameters = RuleTypeParameters(type);
                w.Write((ulong)index);
                w.Write((uint)parameters.Count);
                foreach (RuleParameter p in parameters)
                {
                    w.Write(p.ParamType);
                    w.Write("");
                    w.Write(p.NameId);
                    w.Write("");
                    w.Write(p.Min);
                    w.Write(p.Max);
                }
            }
            Rules current = RulesFor(game);
            w.Write((uint)rows.Count);
            foreach ((uint index, uint type) in rows)
            {
                List<RuleParameter> parameters = RuleTypeParameters(type);
                w.Write((ulong)index);
                w.Write((uint)parameters.Count);
                for (int i = 0; i < parameters.Count; i++)
                {
                    RuleParameter p = parameters[i];
                    int v = current.Values.TryGetValue(p.Id, out int saved) ? saved
                        : p.ParamType == 4 ? (IsTrue(p.Default) ? 1 : 0) : int.TryParse(p.Default, out int d) ? d : 0;
                    w.Write(p.ParamType == 4 ? (v != 0 ? "True" : "False") : v.ToString());
                    w.Write(type);
                    w.Write((uint)i);
                }
            }
            session.EnqueueMessage(ZoneMessageOpcode.InstancedGameBase, output.ToArray());
        }

        /// <summary>
        /// SetConfigurationRequest (sub 0xE; Lua InstancedGame.ApplyGameSettings / ResetGameSettings → FUN_143431540,
        /// writer FUN_143426d60): u64 game, LocalizedString, LocalizedString, settings map, values map (as above; the
        /// client adds the hidden arguments itself), u8. Saves the adjustable general settings and the rule parameters
        /// on the table, clamped to their ranges; they apply from the next round.
        /// </summary>
        private static void HandleSetConfiguration(WorldSession session, byte[] body)
        {
            var r = new GamePacketReader(body);
            ulong guid = r.ReadULong();
            Game game = (guid != 0 ? GameByGuid(guid) : null) ?? GameAt(session);
            if (game == null || !Props.CanEdit(session, game.Guid))
            {
                log.Info($"{session.CharacterName}: game settings for 0x{guid:X} refused.");
                return;
            }
            r.ReadUInt(); r.ReadString();
            r.ReadUInt(); r.ReadString();
            var settings = GeneralSettings().Where(g => g.Set == game.Rules.Id).ToList();
            int changed = 0;
            uint n = r.ReadUInt();
            for (uint i = 0; i < n && i < 64; i++)
            {
                string key = r.ReadString();
                r.ReadUInt(); r.ReadString(); r.ReadString();
                string value = r.ReadString();
                r.ReadUInt();
                GeneralSetting g = settings.FirstOrDefault(s => s.Code == key);
                if (g == null || !g.Adjustable)
                    continue;
                float v;
                if (g.ValueType == 3)
                    v = IsTrue(value) ? 1 : 0;
                else if (!TryNumber(value, out v))
                    continue;
                if (g.ValueType != 3 && g.Max > g.Min)
                    v = Math.Clamp(v, g.Min, g.Max);
                if (g.SettingId is 2 or 3)
                    v = Math.Clamp(v, 1, 64);
                PropSettings.Set(game.Guid, GeneralKey(g.SettingId), v);
                changed++;
            }
            uint triggers = r.ReadUInt();
            for (uint t = 0; t < triggers && t < 64; t++)
            {
                ulong trigger = r.ReadULong();
                uint m = r.ReadUInt();
                (uint Index, uint Type) rule = game.Rules.RuleRows.FirstOrDefault(x => x.Index == trigger);
                List<RuleParameter> parameters = rule.Type != 0 ? RuleTypeParameters(rule.Type) : new List<RuleParameter>();
                for (uint i = 0; i < m && i < 64; i++)
                {
                    string value = r.ReadString();
                    uint type = r.ReadUInt();
                    uint index = r.ReadUInt();
                    if (type != rule.Type || index >= parameters.Count)
                        continue;
                    RuleParameter p = parameters[(int)index];
                    float v;
                    if (p.ParamType == 4)
                        v = IsTrue(value) ? 1 : 0;
                    else if (!TryNumber(value, out v))
                        continue;
                    else if (TryNumber(p.Min, out float min) && TryNumber(p.Max, out float max) && max >= min)
                        v = Math.Clamp(v, min, max);
                    PropSettings.Set(game.Guid, ParameterKey(p.Id), (int)v);
                    changed++;
                }
            }
            log.Info($"{session.CharacterName} applied {changed} setting(s) to {Title(game)} on site {game.ClaimId}.");
            List<WorldSession> editors;
            lock (sync)
                editors = game.Editors.ToList();
            foreach (WorldSession editor in editors)
                SendConfiguration(editor, game);
            Claims.SendGameInfo(game.ClaimId);
        }

        #endregion

        /// <summary>Re-sends the recipe to the game's editors when it changed (props placed / removed) or when forced.</summary>
        private static void RefreshEditors(Game game, bool force = false)
        {
            List<WorldSession> editors;
            lock (sync)
                editors = game.Editors.Where(e => e.RezoneTarget == null).ToList();
            if (editors.Count == 0)
                return;
            if (!force && Signature(Recipe(game, out _), game) == game.EditorRecipe)
                return;
            foreach (WorldSession editor in editors)
                SendRecipe(editor, game, false);
        }
    }
}
