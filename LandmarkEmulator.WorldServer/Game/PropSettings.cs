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
            SpawnDelay = "player_npc_spawn_delay_flat", RespawnEnabled = "player_npc_respawn_enabled", SpawnerAi = "npc_spawner_ai_enabled",
            TextDisplayTime = "prop_text_display_time", EmoteSpam = "npc_emote_spam_enabled";

        public const string Mood = "npc_mood_setting", Stance = "npc_stance_selection", Emote = "npc_emote_setting";

        /// <summary>
        /// Drop-down settings and their options {label text id, value}: the client's own emote words (text 559123-559192).
        /// Emote and stance values are AnimationSelection ids (as the player emotes use, see <see cref="Emotes"/>); moods
        /// have no animation of their own (1 angry, 2 calm, 3 happy).
        /// </summary>
        public static readonly Dictionary<string, (uint Text, uint Value)[]> DropDowns = new()
        {
            [Mood] = new (uint, uint)[] { (559169, 2), (559168, 1), (559170, 3) },
            [Stance] = new (uint, uint)[] { (559171, 223), (559172, 224), (559173, 246), (559174, 250), (559175, 248), (559176, 251) },
            [Emote] = new (uint, uint)[] { (559123, 217), (559144, 229), (559154, 230), (559155, 231), (559162, 240), (559163, 239), (559166, 242) },
        };

        /// <summary>A drop-down setting's value, or its first option when nothing valid was chosen.</summary>
        public static uint Choice(ulong prop, string key)
        {
            uint v = (uint)Get(prop, key);
            return DropDowns.TryGetValue(key, out var options) && options.All(o => o.Value != v) ? options[0].Value : v;
        }

        /// <summary>An EditableProperties.txt row.</summary>
        public record Definition(uint Id, string Key, uint NameId, uint HelpId, float Default, float Max, float Min, float Step);

        private static Dictionary<uint, Definition> definitions;

        /// <summary>EditableProperties ids per story-tool kind (prop id, or "spawner").</summary>
        private static readonly Dictionary<object, uint[]> settingsByKind = new()
        {
            [StoryTools.TimerProp] = new uint[] { 14, 13 },
            [StoryTools.DiceRollProp] = new uint[] { 15, 16 },
            [StoryTools.DamageNodeProp] = new uint[] { 30, 32 },
            [StoryTools.FocusNodeProp] = new uint[] { 29 },
            [StoryTools.StoryNodeProp] = new uint[] { 37, 24, 20, 21, 22 },
            ["spawner"] = new uint[] { 18, 19, 31 },
            ["flinger"] = new uint[] { 4, 5, 6 },
            ["platform"] = new uint[] { 7, 8 },
        };

        private static void EnsureLoaded()
        {
            if (definitions != null)
                return;
            var defs = new Dictionary<uint, Definition>();
            foreach (string[] r in GameDataFiles.Rows("EditableProperties.txt"))
                defs[r.U(0)] = new Definition(r.U(0), r.S(1), r.U(2), r.U(3), F(r, 4), F(r, 5), F(r, 6), F(r, 7));
            lock (sync)
            {
                if (definitions != null)
                    return;
                using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
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
                return new List<Definition>();
            return ids.Where(definitions.ContainsKey).Select(i => definitions[i]).ToList();
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
        private static readonly Dictionary<string, float> dynamicDefaults = new();

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
            List<Definition> settings = ok ? SettingsOf(prop.PropId) : new List<Definition>();
            List<Definition> plain = settings.Where(d => !DropDowns.ContainsKey(d.Key)).ToList();
            List<Definition> lists = settings.Where(d => DropDowns.ContainsKey(d.Key)).ToList();
            w.Write((uint)plain.Count);
            foreach (Definition d in plain)
            {
                w.Write(KeyHash(d.Key));
                WriteRecord(w, d, Get(guid, d.Key));
            }
            // Drop-downs (FUN_142b9d440): u32 key, record, options {u32 n; u32 label text id, u32 value, bool}.
            w.Write((uint)lists.Count);
            foreach (Definition d in lists)
            {
                uint chosen = Choice(guid, d.Key);
                w.Write(KeyHash(d.Key));
                WriteRecord(w, d, chosen);
                var options = DropDowns[d.Key];
                w.Write((uint)options.Length);
                foreach ((uint text, uint value) in options)
                {
                    w.Write(text);
                    w.Write(value);
                    w.Write(value == options[0].Value);
                }
            }
            w.Write(prop?.CustomName ?? "");
            w.Write(prop?.Text ?? "");
            session.EnqueueMessage(ZoneMessageOpcode.PropBase, output.ToArray());
            log.Info($"{session.CharacterName} opened the settings of prop 0x{guid:X}: {(ok ? $"{settings.Count} setting(s)" : "refused")}.");
        }

        /// <summary>A setting record (FUN_142b7c740).</summary>
        private static void WriteRecord(GamePacketWriter w, Definition d, float value)
        {
            w.Write(Shared.GameTable.Text.TextManager.Instance.GetTextForId(d.NameId) ?? d.Key);
            w.Write(d.NameId);
            w.Write(d.Key);
            w.Write(value);
            w.Write(d.Default);
            w.Write(d.Max);
            w.Write(d.Min);
            w.Write(d.Step);
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
            if (d == null || !Props.CanEdit(session, guid))
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
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
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
                DatabaseManager.Instance.CharacterDatabase.Save(c => c.WorldPropSetting.RemoveRange(c.WorldPropSetting.Where(s => s.PropId == guid)));
        }
    }
}
