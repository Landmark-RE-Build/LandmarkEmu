using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using Microsoft.Extensions.Configuration;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Light and fire effects on props (torch flames, candle glow, lamp light). The client ships the effects as
    /// InteractableCompositeEffectSets.txt (a set per prop kind, e.g. 3 "EFX_Prop_Fire_Torch") with one row per state in
    /// InteractableCompositeEffects.txt (set 3: index 0 "OFF" = no effect, index 1 "ON" = composite 10907 on socket
    /// FX_Attach), but nothing on the client says which prop uses which set: the server turns them on.
    ///
    /// Packet: UpdatePropInteractionState 0xFD (reader FUN_142b94680, applied by FUN_1430065e0): u64 prop guid,
    /// u32 behaviour type (1 = ClientPropInteractableCompEffectStateBehavior), u32 key (slot in the prop's state table),
    /// then the state {u32 value (the set's row index; -1 clears), u32 previous value (-1 = new: the effect starts at
    /// once), u64, u32 n × i32 [set id, 0, start offset ms, apply change (must be non-zero for a state already shown)],
    /// u32 n × string}, bool.
    /// Which prop uses which set: see <see cref="Build"/>.
    /// </summary>
    public static class PropEffects
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private const uint CompositeEffectBehaviour = 1;

        /// <summary>Prop id → (effect set, "on" row index).</summary>
        private static Dictionary<uint, (uint Set, int On)> byProp = new();

        /// <summary>
        /// One-shot effects: the 22 Fireworks Emitters (props 2949-2970, 0.695) → their set (prop − 2820, sets 129-150
        /// "EFX_Prop_Emitter_Fireworks_*", matched by name for all 22; trace/0695/spec-fireworks.md §3). Each set has two
        /// "On" rows (index 0 and 1) and no "Off": a launch plays the burst once (<see cref="Launch"/>), nothing is shown
        /// at spawn and there is no on / off.
        /// </summary>
        private static Dictionary<uint, uint> oneShots = new();

        /// <summary>The value each emitter last launched with (0 / 1): every launch is a state change.</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, uint> launchValue = new();

        public const uint FirstFireworksEmitter = 2949, LastFireworksEmitter = 2970;
        private const uint FireworksSetOffset = 2820;

        /// <summary>Generic sets by model keyword (light sources only), when no set is named after the model.</summary>
        private static readonly (string Keyword, uint Set)[] keywordSets =
        {
            ("FX_Moths", 4), ("Candelabra", 33), ("Fireplace", 31), ("Torch", 3), ("Campfire", 5), ("Brazier", 5),
            ("Candle", 19), ("Chandelier", 10), ("Lantern", 9), ("LampPost", 39), ("Lamp", 9), ("Sconce", 9),
            ("StringLights", 9), ("Lightsource", 9)
        };

        private static readonly string[] colours = { "blue", "green", "indigo", "orange", "red", "violet", "white", "yellow" };

        /// <summary>Words that say nothing about which effect: dropped when matching names.</summary>
        private static readonly HashSet<string> noise = new()
        {
            "efx", "prop", "theme", "widgets", "widget", "motes", "mote", "emitter", "type", "fx", "attach", "flying", "bug",
            "aquarium", "school", "loop", "lg", "colony", "swarm"
        };

        private static readonly Dictionary<string, string[]> synonyms = new()
        {
            ["flame"] = new[] { "fire" }, ["electricity"] = new[] { "lightning" }, ["bats"] = new[] { "bat" },
            ["maggots"] = new[] { "maggot" }, ["drops"] = new[] { "drop" }, ["snowfall"] = new[] { "snow", "falling" }
        };

        /// <summary>"EFX_Prop_Widgets_Motes_Emitter_MagicFire_Small_Blue" gives magic, fire, small, blue.</summary>
        private static HashSet<string> Words(string text)
        {
            text = Regex.Replace(text, "([a-z])([A-Z])", "$1 $2");
            text = Regex.Replace(text, "([A-Za-z])([0-9])", "$1 $2");
            var words = new HashSet<string>();
            foreach (string raw in Regex.Split(text.ToLowerInvariant(), "[^a-z0-9]+"))
            {
                if (raw.Length == 0 || noise.Contains(raw))
                    continue;
                string w = raw.All(char.IsDigit) ? int.Parse(raw).ToString() : raw;
                if (synonyms.TryGetValue(w, out string[] alternatives))
                    words.UnionWith(alternatives);
                else
                    words.Add(w);
            }
            return words;
        }

        /// <summary>
        /// Builds the prop to effect set table from each prop's model (.adr file) and internal description (such as
        /// "landmark_v2: builder: common: torch fire (blue) mote emitter"):
        /// - mote emitters all share one model, so the description's words pick the set (preferring "Emitter" sets);
        /// - light orbs and spotlights (Lightsource01 models): the colour in the description picks set 11-18 / 23-30;
        ///   emissive cubes and orbs give no light;
        /// - otherwise a set whose name's words are all in the model name (most sets are "EFX_Prop_" + model), else a
        ///   keyword for light-source models (torch, candle, lantern...).
        /// </summary>
        public static void Build(IReadOnlyDictionary<uint, (string Model, string Description)> props)
        {
            try
            {
                string gameData = SharedConfiguration.Configuration.GetValue("GameDataPath", "gameData");
                var setNames = new Dictionary<uint, string>();
                foreach (string line in File.ReadLines(Path.Combine(gameData, "InteractableCompositeEffectSets.txt")).Skip(1))
                {
                    string[] f = line.Split('^');
                    if (f.Length > 1 && uint.TryParse(f[0], out uint id) && f[1].Length > 0)
                        setNames[id] = f[1];
                }
                // Each set's "on" row: the row described as ON, else the first row with an effect.
                var onIndex = new Dictionary<uint, int>();
                foreach (string line in File.ReadLines(Path.Combine(gameData, "InteractableCompositeEffects.txt")).Skip(1))
                {
                    string[] f = line.Split('^');
                    if (f.Length < 6 || !uint.TryParse(f[1], out uint set) || !int.TryParse(f[2], out int index)
                        || !uint.TryParse(f[3], out uint effect) || effect == 0)
                        continue;
                    if (f[5].EndsWith(" ON", StringComparison.OrdinalIgnoreCase) || !onIndex.ContainsKey(set))
                        onIndex[set] = index;
                }
                var sets = setNames.Where(s => onIndex.ContainsKey(s.Key))
                    .Select(s => (Id: s.Key, Words: Words(s.Value), Emitter: s.Value.Contains("Emitter"))).ToList();

                uint Pick(string model, string description)
                {
                    string about = description[(description.LastIndexOf(':') + 1)..].Trim().ToLowerInvariant();
                    if (model.Contains("Motes_Emitter01"))
                    {
                        HashSet<string> words = Words(about);
                        return sets.Select(s => (s.Id,
                                Ok: s.Words.Any(words.Contains) && s.Words.Where(w => !w.All(char.IsDigit)).All(words.Contains),
                                Score: s.Words.Count(words.Contains) * 10 - words.Count(w => !s.Words.Contains(w)) + (s.Emitter ? 5 : 0)))
                            .Where(s => s.Ok).OrderByDescending(s => s.Score).FirstOrDefault().Id;
                    }
                    if (model.Contains("Lightsource01_"))
                    {
                        if (about.Contains("emissive"))
                            return 0;
                        int c = Array.FindIndex(colours, about.Contains);
                        return c < 0 ? 9u : (uint)((model.Contains("Cone") ? 23 : 11) + c);
                    }
                    HashSet<string> modelWords = Words(model);
                    var named = sets.Where(s => s.Words.Count >= 3 && s.Words.IsSubsetOf(modelWords))
                        .OrderByDescending(s => s.Words.Count).FirstOrDefault();
                    if (named.Id != 0)
                        return named.Id;
                    if (model.Contains("Decorations") || model.Contains("Emitter"))
                        return 0;
                    if (!model.Contains("lightsource", StringComparison.OrdinalIgnoreCase)
                        && !model.Contains("CampFire") && !model.Contains("Brazier") && !model.Contains("Torch"))
                        return 0;
                    return keywordSets.FirstOrDefault(k => model.Contains(k.Keyword, StringComparison.OrdinalIgnoreCase)).Set;
                }

                var map = new Dictionary<uint, (uint, int)>();
                foreach ((uint propId, (string adr, string description)) in props)
                {
                    uint set = Pick(Path.GetFileNameWithoutExtension(adr), description ?? "");
                    if (set != 0 && onIndex.TryGetValue(set, out int on))
                        map[propId] = (set, on);
                }
                var shots = new Dictionary<uint, uint>();
                for (uint prop = FirstFireworksEmitter; prop <= LastFireworksEmitter; prop++)
                {
                    uint set = prop - FireworksSetOffset;
                    if (!setNames.TryGetValue(set, out string name) || !name.Contains("Fireworks", StringComparison.OrdinalIgnoreCase))
                    {
                        log.Warn($"Fireworks emitter {prop}: set {set} is \"{name}\", not a fireworks set; left without an effect.");
                        continue;
                    }
                    map.Remove(prop);
                    shots[prop] = set;
                }
                byProp = map;
                oneShots = shots;
                log.Info($"{map.Count} props have light or particle effects, {shots.Count} launch fireworks.");
            }
            catch (Exception e)
            {
                log.Error(e, "Could not load the prop effect sets; props stay unlit.");
            }
        }

        public static bool HasEffect(uint propId) => byProp.ContainsKey(propId);

        public static bool IsOneShot(uint propId) => oneShots.ContainsKey(propId);

        /// <summary>
        /// Launches a one-shot effect (a Fireworks Emitter) for the viewers: the state value flips between the set's two
        /// "On" rows (0 / 1) so every launch is a change, previous −1, apply flag 1. The 0.695 client then always plays
        /// the row's composite once (FUN_1430ac970 → behaviour +8 FUN_143476ad0 / +0x10 FUN_143476bb0 → FUN_1430aaeb0).
        /// One body for all viewers.
        /// </summary>
        public static void Launch(IEnumerable<WorldSession> viewers, uint propId, ulong guid)
        {
            if (!oneShots.TryGetValue(propId, out uint set))
                return;
            uint value = launchValue.AddOrUpdate(guid, 0u, (_, v) => v ^ 1u);
            byte[] body = Body(guid, set, value);
            foreach (WorldSession viewer in viewers)
                viewer.EnqueueMessage(ZoneMessageOpcode.UpdatePropInteractionState, body);
        }

        /// <summary>Turns a prop's effect on (or off) for one viewer. Nothing for props without an effect set.</summary>
        public static void Send(WorldSession session, uint propId, ulong guid, bool on = true)
        {
            if (!byProp.TryGetValue(propId, out (uint Set, int On) fx))
                return;
            session.EnqueueMessage(ZoneMessageOpcode.UpdatePropInteractionState, Body(guid, fx.Set, on ? (uint)fx.On : uint.MaxValue));
        }

        private static byte[] Body(ulong guid, uint set, uint value)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(guid);
            writer.Write(CompositeEffectBehaviour);
            writer.Write(set);                         // key: one effect slot per prop
            writer.Write(value);
            writer.Write(uint.MaxValue);               // previous: none, so the effect starts now
            writer.Write(0ul);
            writer.Write(4u);
            writer.Write(set);
            writer.Write(0u);
            writer.Write(0u);                          // start offset (ms)
            writer.Write(1u);                          // non-zero: apply a change to an existing state (FUN_1433c8980)
            writer.Write(0u);                          // strings
            writer.Write(false);
            return output.ToArray();
        }
    }
}
