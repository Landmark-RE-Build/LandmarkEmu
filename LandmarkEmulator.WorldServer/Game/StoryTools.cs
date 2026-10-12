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
        private const uint ResultOk = 0, ResultFailed = 1;

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
        };

        /// <summary>"When a Luminary clicks this object..."</summary>
        public const uint ClickedEvent = 576311;

        // Story-tool props (PropDefs ids; the items 3090 Logic Filter, 3127 Travel Point, 3131 Story Node, 3132 Dice
        // Roll Filter, 3137 Timer Filter, 3790 Starting Location, 4388 Damage, 4389 Focus, 4390 Reset place them).
        public const uint LogicFilterProp = 2325, TravelPointProp = 2330, StoryNodeProp = 2344, DiceRollProp = 2345,
            TimerProp = 2367, StartingLocationProp = 2655, FocusNodeProp = 2898, DamageNodeProp = 2899, ResetNodeProp = 2900,
            LinkingAreaProp = 1590;

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
        public const uint ActivateEverything = 576243, ActivateMood = 576248, ActivateStance = 576252, ActivateEmote = 576262,
            ActivateText = 576255;
        // Focus / Damage / Reset nodes.
        public const uint FocusActivates = 577079, FocusDeactivates = 577169, ActivateFocus = 577080, DeactivateFocus = 577167;
        public const uint DamageActivates = 577070, ActivateDamage = 565554;
        public const uint ResetStory = 577085;
        /// <summary>The Adventure Game Table (IgRuleSetPlacementProps 2313 → rule set 8) and its one action: "...Give reward
        /// and teleport to a Chaos Cavern.", which completes the adventure (InstancedGames.CompleteAdventure).</summary>
        public const uint AdventureTableProp = 2313, CompleteAdventure = 577090;
        // Starting Location and Linking Area.
        public const uint LuminaryTeleportsHere = 574556, LuminaryRespawnsHere = 574557;
        public const uint LuminaryEnters = 565950, LuminaryExits = 565951, CreatureEnters = 573163, CreatureLeaves = 573164,
            DamageCreaturesInArea = 577148;
        // Travel Point.
        public const uint CreatureArrives = 572991, WalkHere = 572992;
        // Creature spawners.
        public const uint CreatureSpawns = 572997, CreatureDies = 572824, CreatureEntersCombat = 576447, CreatureExitsCombat = 576448,
            CreatureClicked = 51877, CreatureToldToWalk = 576661;
        public const uint SpawnCreature = 572860, DespawnCreature = 573003, FactionAlly = 576439, FactionNeutral = 576438,
            FactionEnemy = 576434, EquipWeapon = 577074, UnequipWeapon = 577075, WalkRoute = 576662, SetMood = 576259,
            SetStance = 576258, PerformEmote = 576261, SayText = 3, PerformEverything = 577048, FocusOnNode = 577067,
            StopFocusing = 577168, DamageCreature = 577068;

        private static readonly Dictionary<uint, Kind> kindsById = new()
        {
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
                Events = new[] { NodeEverythingActivates, NodeMoodActivates, NodeStanceActivates, NodeEmoteActivates, NodeTextActivates },
                Actions = new[] { ActivateEverything, ActivateMood, ActivateStance, ActivateEmote, ActivateText }
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
            [ResetNodeProp] = new Kind { Name = "reset node", Actions = new[] { ResetStory } },
            [AdventureTableProp] = new Kind { Name = "adventure game", Actions = new[] { CompleteAdventure } },
            [LinkingAreaProp] = new Kind
            {
                Name = "linking area",
                Events = new[] { LuminaryEnters, LuminaryExits, CreatureEnters, CreatureLeaves },
                Actions = new[] { DamageCreaturesInArea }
            },
        };

        private static readonly Kind spawnerKind = new()
        {
            Name = "creature spawner",
            Events = new[] { CreatureSpawns, CreatureDies, CreatureEntersCombat, CreatureExitsCombat, CreatureClicked, CreatureToldToWalk },
            Actions = new[] { SpawnCreature, DespawnCreature, FactionAlly, FactionNeutral, FactionEnemy, EquipWeapon, UnequipWeapon,
                WalkRoute, SetMood, SetStance, PerformEmote, SayText, PerformEverything, FocusOnNode, StopFocusing, DamageCreature }
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

        private static Kind KindOfProp(ulong guid) => Props.Find(guid) is Props.PlacedProp prop ? KindOf(prop.PropId) : null;

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
                using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
                bindings.AddRange(context.WorldPropBinding.ToList());
                nextBindingId = bindings.Count == 0 ? 0 : (long)bindings.Max(b => b.Id);
                loaded = true;
                log.Info($"Loaded {bindings.Count} story-tool link(s).");
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
            => DatabaseManager.Instance.CharacterDatabase.Save(change)
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
            public string Name = "";
            public bool On;
            public bool Locked;
            public bool Cog1, Cog2;
            public bool TimerRunning;
            public TimeSpan TimerLeft;
            public DateTime TimerEnds;
            public ulong Creature;
            public DateTime? RespawnAt;
            public bool NodeActive;
            /// <summary>A spawner creature's mood (Story Node: 1 angry, 2 calm, 3 happy).</summary>
            public uint Mood = 2;
            public readonly HashSet<WorldSession> Inside = new();
            /// <summary>Story creatures inside a Linking Area (for its creature enters / leaves events).</summary>
            public readonly HashSet<ulong> CreaturesInside = new();
        }

        /// <summary>Who or what set a chain of Links off: a player (switch, area) and/or a creature (spawner events).</summary>
        private record Context(WorldSession Player, ulong Creature, ulong DamageNode = 0, ulong StoryNode = 0, ulong FocusNode = 0);

        private class Site
        {
            public ulong ClaimId;
            public uint Zone;
            public readonly Dictionary<ulong, Running> Props = new();
        }

        private static readonly Dictionary<ulong, Site> sites = new();
        private static readonly HashSet<ulong> pendingResets = new();
        /// <summary>Links followed per chain at most: a loop of Links (A turns on B, B turns on A) stops there.</summary>
        private const int MaxChain = 64;
        private const float AreaRadius = 4f, AreaDamagePercent = 50f;

        /// <summary>
        /// Inside a Linking Area: within its radius (× scale) across the ground and 1.5 × that up or down. A plain
        /// sphere missed creatures walking on ground a few metres below where the area was placed.
        /// </summary>
        private static bool InArea(Running area, System.Numerics.Vector3 at)
        {
            float radius = AreaRadius * area.Scale;
            float dx = at.X - area.Position.X, dz = at.Z - area.Position.Z;
            return dx * dx + dz * dz <= radius * radius && Math.Abs(at.Y - area.Position.Y) <= radius * 1.5f;
        }

        // Settings from the props' settings windows (PropSettings; EditableProperties defaults: timer 60 s without
        // restart, dice 50/50, damage 50 %, spawner respawn after 30 s).
        private static TimeSpan TimerLength(Running r) => TimeSpan.FromSeconds(Math.Max(1f, PropSettings.Get(r.Guid, PropSettings.TimerDuration)));

        private static float DiceOdds(Running r)
        {
            float a = PropSettings.Get(r.Guid, PropSettings.DiceWeightA), b = PropSettings.Get(r.Guid, PropSettings.DiceWeightB);
            return a + b <= 0 ? 0.5f : a / (a + b);
        }

        /// <summary>The amount (% of max health) of the Damage Node that set the chain off (its default otherwise).</summary>
        private static float DamagePercent(Site site, Context ctx) => PropSettings.Get(ctx.DamageNode, PropSettings.DamageValue);

        private static float DamagePercent(Running area) => AreaDamagePercent;

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
            var site = new Site { ClaimId = claim.Id, Zone = claim.ZoneId };
            foreach (var p in Props.PlacedAll(null).Where(p => p.Zone == claim.ZoneId && Claims.ClaimAt(p.Zone, p.Position) == claim.Id))
            {
                Kind kind = KindOf(p.PropId);
                if (kind == null)
                    continue;
                Props.PlacedProp placed = Props.Find(p.Guid);
                uint nameId = GameTableManager.Instance.PropDefs.GetEntry(p.PropId)?.NameStringId ?? 0;
                site.Props[p.Guid] = new Running
                {
                    Guid = p.Guid,
                    PropId = p.PropId,
                    Kind = kind,
                    Position = p.Position,
                    Scale = placed?.Spawn.Scale.X ?? 1f,
                    Name = nameId != 0 ? Shared.GameTable.Text.TextManager.Instance.GetTextForId(nameId) ?? "" : "",
                    // Lights start as built (lit unless switched off); moving props start shut.
                    On = placed != null && !placed.Disabled && PropAnimations.SetFor(p.PropId) == 0,
                    TimerLeft = TimeSpan.FromSeconds(PropSettings.Get(p.Guid, PropSettings.TimerDuration))
                };
            }
            lock (sync)
                sites[claim.Id] = site;
            log.Info($"Site {claim.Id} is in Play Mode: {site.Props.Count} story prop(s).");
            foreach (Running r in site.Props.Values.Where(r => r.Kind == spawnerKind).ToList())
                Spawn(site, r, 0);
        }

        private static void Stop(ulong claimId)
        {
            Site site;
            lock (sync)
                if (!sites.Remove(claimId, out site))
                    return;
            foreach (Running r in site.Props.Values)
            {
                if (r.Creature != 0)
                    Creatures.DespawnStory(r.Creature);
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
                case ActivateEverything: Fire(site, r, NodeEverythingActivates, ctx with { StoryNode = r.Guid }, depth); break;
                case ActivateMood: Fire(site, r, NodeMoodActivates, ctx with { StoryNode = r.Guid }, depth); break;
                case ActivateStance: Fire(site, r, NodeStanceActivates, ctx with { StoryNode = r.Guid }, depth); break;
                case ActivateEmote: Fire(site, r, NodeEmoteActivates, ctx with { StoryNode = r.Guid }, depth); break;
                case ActivateText: Fire(site, r, NodeTextActivates, ctx with { StoryNode = r.Guid }, depth); break;
                case SetMood:
                case SetStance:
                case PerformEmote:
                case SayText:
                case PerformEverything:
                    if (r.Creature != 0)
                        Perform(site, r, action, ctx);
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
                    if (r.Creature != 0 && site.Props.TryGetValue(ctx.FocusNode, out Running focus))
                        Creatures.StoryFocus(r.Creature, PropSettings.Get(focus.Guid, PropSettings.FocusOnActivator) != 0 && ctx.Player != null
                            ? ctx.Player.Position : focus.Position);
                    break;
                case StopFocusing:
                    if (r.Creature != 0)
                        Creatures.StoryFocus(r.Creature, null);
                    break;
                case ActivateDamage:
                    Fire(site, r, DamageActivates, ctx with { DamageNode = r.Guid }, depth);
                    break;
                case CompleteAdventure:
                    InstancedGames.CompleteAdventure(site.ClaimId);
                    break;
                case ResetStory:
                    // Restarts Play Mode once this chain has finished (next tick).
                    lock (sync)
                        pendingResets.Add(site.ClaimId);
                    break;
                case DamageCreaturesInArea:
                    foreach (Running spawner in site.Props.Values.Where(s => s.Creature != 0).ToList())
                        if (Creatures.StoryPosition(spawner.Creature) is System.Numerics.Vector3 at
                            && InArea(r, at))
                            Creatures.DamageStory(spawner.Creature, DamagePercent(r));
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
                        Creatures.DamageStory(r.Creature, DamagePercent(site, ctx));
                    break;
                default:
                    // Travel, faction and weapon actions need creature AI hooks.
                    log.Debug($"Story action {action} on {k.Name} 0x{r.Guid:X} has no effect yet.");
                    break;
            }
        }

        /// <summary>
        /// A spawner's creature acts out the Story Node that set the chain off: its stance and emote (animations), its
        /// mood (kept; moods have no animation of their own) and its text, said in chat.
        /// </summary>
        private static void Perform(Site site, Running spawner, uint action, Context ctx)
        {
            ulong node = ctx.StoryNode;
            bool all = action == PerformEverything;
            if (all || action == SetStance)
                Creatures.PlayStoryAnimation(spawner.Creature, PropSettings.Choice(node, PropSettings.Stance));
            if (all || action == PerformEmote)
                Creatures.PlayStoryAnimation(spawner.Creature, PropSettings.Choice(node, PropSettings.Emote));
            if (all || action == SetMood)
                spawner.Mood = PropSettings.Choice(node, PropSettings.Mood);
            if ((all || action == SayText) && Props.Find(node) is Props.PlacedProp storyNode
                && Creatures.StoryPosition(spawner.Creature) is System.Numerics.Vector3 at)
                Chat.NpcSay(spawner.Creature, Creatures.StoryName(spawner.Creature), site.Zone, at, storyNode.Text);
            log.Info($"{Creatures.StoryName(spawner.Creature)} 0x{spawner.Creature:X} performs story action {action} (node 0x{node:X}).");
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
            if (PropAnimations.SetFor(r.PropId) != 0)
            {
                foreach (WorldSession p in PlayerManager.InZone(site.Zone))
                    if (p.RezoneTarget == null)
                        PropAnimations.Send(p, r.PropId, r.Guid, on ? PropAnimations.Open : PropAnimations.Close);
                return;
            }
            if (!PropEffects.HasEffect(r.PropId))
                return;
            // Not to players in the middle of a re-zone (anything with it can hang their client); they see the prop
            // again when their re-zone ends.
            foreach (WorldSession p in PlayerManager.InZone(site.Zone))
                if (p.RezoneTarget == null)
                    PropEffects.Send(p, r.PropId, r.Guid, on);
        }

        private static void SetCogs(Site site, Running r, bool cog1, bool cog2, Context ctx, int depth)
        {
            bool was1 = r.Cog1, was2 = r.Cog2;
            (r.Cog1, r.Cog2) = (cog1, cog2);
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
            r.Creature = Creatures.SpawnStory(site.Zone, r.Position, r.Name, r.PropId);
            Fire(site, r, CreatureSpawns, new Context(null, r.Creature), depth);
        }

        private static readonly Dictionary<WorldSession, (ulong ClaimId, bool Respawn)> arriving = new();

        /// <summary>A player left the world.</summary>
        public static void Forget(WorldSession session)
        {
            lock (sync)
                arriving.Remove(session);
        }

        /// <summary>A player is being moved to a build site (travel or respawn there); its Starting Location hears of it
        /// once they have arrived (<see cref="PlayerArrived"/>).</summary>
        public static void ExpectArrival(WorldSession session, ulong claimId, bool respawn)
        {
            lock (sync)
                arriving[session] = (claimId, respawn);
        }

        /// <summary>
        /// A re-zone finished (Teleport.FinishRezone). If it brought the player to a site in Play Mode, the site's Starting
        /// Location runs "When a Luminary teleports to / respawns at this Build Site..." (not earlier: anything sent with
        /// the re-zone can hang the client).
        /// </summary>
        public static void PlayerArrived(WorldSession session)
        {
            (ulong ClaimId, bool Respawn) a;
            Site site;
            lock (sync)
            {
                if (!arriving.Remove(session, out a))
                    return;
                site = sites.GetValueOrDefault(a.ClaimId);
            }
            if (site == null)
                return;
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

        /// <summary>Switches (and levers) can be clicked; only in Play Mode do they do anything.</summary>
        public static bool IsClickable(uint propId) => KindOf(propId) is Kind k && k.Events.Contains(ClickedEvent);

        /// <summary>A player clicked a prop (Command InteractRequest). A switch flips and raises its click event.</summary>
        public static bool Click(WorldSession session, ulong guid)
        {
            Props.PlacedProp placed = Props.Find(guid);
            if (placed == null || !IsClickable(placed.PropId))
                return false;
            Site site = SiteOf(guid);
            if (site == null || !site.Props.TryGetValue(guid, out Running r))
            {
                Chat.System(session, "Nothing happens. (The build site is not in Play Mode.)");
                return true;
            }
            var ctx = new Context(session, 0);
            Fire(site, r, ClickedEvent, ctx, 0);
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
            Fire(site, spawner, CreatureClicked, new Context(session, creature), 0);
            log.Info($"{session.CharacterName} clicked story creature 0x{creature:X} ({spawner.Kind.Name}).");
            return true;
        }

        /// <summary>Server tick: timers, spawner respawns, Linking Areas, pending resets.</summary>
        public static void Update()
        {
            List<Site> all;
            lock (sync)
                all = sites.Values.ToList();
            DateTime now = DateTime.UtcNow;
            foreach (Site site in all)
            {
                List<WorldSession> players = null;
                foreach (Running r in site.Props.Values.ToList())
                {
                    if (r.TimerRunning && now >= r.TimerEnds)
                    {
                        r.TimerLeft = TimerLength(r);
                        r.TimerRunning = PropSettings.Get(r.Guid, PropSettings.TimerAutoRestart) != 0;
                        if (r.TimerRunning)
                            r.TimerEnds = now + r.TimerLeft;
                        Fire(site, r, TimerCompletes, new Context(null, 0), 0);
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
                    PlayModeChanged(claim, false);
                    PlayModeChanged(claim, true);
                    log.Info($"Site {claimId}: story reset.");
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
                + $"cogs={r.Cog1}/{r.Cog2} timer={(r.TimerRunning ? (r.TimerEnds - DateTime.UtcNow).TotalSeconds.ToString("F0") + "s" : "stopped")} creature={r.Creature:X}"));
        }

        #endregion

        #region packets

        [ZoneMessageHandler(ZoneMessageOpcode.GameScriptingBase)]
        public static void Handle(WorldSession session, GameScriptingRequest request)
        {
            EnsureLoaded();
            var reader = new GamePacketReader(request.Body);
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
                    Send(session, SubEvents, w => { w.Write(prop); w.Write(kind != null ? ResultOk : ResultFailed); WriteDefinitions(w, kind?.Events); });
                    break;
                }
                case 0x0E:
                {
                    ulong prop = reader.ReadULong();
                    Kind kind = KindOfProp(prop);
                    Send(session, SubActions, w => { w.Write(prop); w.Write(kind != null ? ResultOk : ResultFailed); WriteDefinitions(w, kind?.Actions); });
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
                        WriteDefinitions(w, kind?.Actions);
                        WriteDefinitions(w, kind?.Events);
                        w.Write(0u);                     // default links
                    });
                    log.Info($"{session.CharacterName} opened Links on prop 0x{prop:X} ({kind?.Name ?? "nothing to link"}).");
                    break;
                }
                case 0x10:
                {
                    ulong source = reader.ReadULong(), target = reader.ReadULong();
                    uint eventId = reader.ReadUInt(), actionId = reader.ReadUInt();
                    uint result = allowed && Add(session, source, eventId, target, actionId) != null ? ResultOk : ResultFailed;
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
                    if (allowed && old != null && Props.CanEdit(session, old.SourceId))
                        Delete(new List<WorldPropBindingModel> { old });
                    uint result = allowed && Add(session, source, eventId, target, actionId) != null ? ResultOk : ResultFailed;
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
        private static WorldPropBindingModel Add(WorldSession session, ulong source, uint eventId, ulong target, uint actionId)
        {
            Kind from = KindOfProp(source), to = KindOfProp(target);
            if (from == null || to == null || !from.Events.Contains(eventId) || !to.Actions.Contains(actionId)
                || !Props.CanEdit(session, source) || !Props.CanEdit(session, target))
            {
                log.Info($"{session.CharacterName}: link 0x{source:X} {eventId} → 0x{target:X} {actionId} refused.");
                return null;
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
                    return bindings.First(b => b.SourceId == source && b.EventId == eventId && b.TargetId == target && b.ActionId == actionId);
            }
            if (fromProp >= MaxLinksPerProp || onSite >= MaxLinksPerSite)
            {
                Chat.System(session, fromProp >= MaxLinksPerProp ? $"A prop can have at most {MaxLinksPerProp} links."
                    : $"A build site can have at most {MaxLinksPerSite} links.");
                return null;
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
            return link;
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
            var guids = Props.PlacedAll(null).Where(p => p.Zone == session.ZoneId && KindOf(p.PropId) != null
                && Props.CanEdit(session, p.Guid)).Select(p => p.Guid).ToList();
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
