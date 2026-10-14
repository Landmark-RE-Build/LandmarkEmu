using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.WorldServer.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// What usable items do (right-click "use" items: their ability has type 4 in the item definition). Effects follow
    /// each item's own description text; stat ids are CalculatedStatDefinitions (3 harvesting area, 4 light radius,
    /// 5 run speed, 6 jump height, 7 discovery, 12 harvesting damage, 13 harvest speed, 17 harvest bonus, 29 fly).
    /// </summary>
    public static class Consumables
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private enum Use { Consume, Keep, Refuse }

        /// <summary>An item's effect: (player, the item used, the point aimed at) → whether one is used up.</summary>
        private sealed record Effect(string Name, Func<WorldSession, CharacterItemModel, Vector3, Use> Apply);

        private static readonly TimeSpan Minutes15 = TimeSpan.FromMinutes(15), Minutes30 = TimeSpan.FromMinutes(30);

        private static Effect StatBuff(string name, uint stat, float add, float percent, TimeSpan duration)
            => new(name, (s, item, _) => { Buffs.AddStat(s, name, stat, add, percent, duration, item.ItemDefinitionId); return Use.Consume; });

        private static Effect Heal(string name, float percentPerSecond, int seconds, int cooldown)
            => new(name, (s, item, _) =>
            {
                if (Health.Current(s) >= Health.MaxHealth)
                {
                    Chat.System(s, "You are already at full health.");
                    return Use.Refuse;
                }
                if (Buffs.OnCooldown(s, item.ItemDefinitionId, TimeSpan.FromSeconds(cooldown)))
                {
                    Chat.System(s, $"{name} is still on cooldown.");
                    return Use.Refuse;
                }
                Buffs.HealOverTime(s, percentPerSecond, TimeSpan.FromSeconds(seconds), item.ItemDefinitionId);
                return Use.Consume;
            });

        private static Effect Detector(string name, string found)
            => new(name, (s, _, _) =>
            {
                bool on = Buffs.Toggle(s, name);
                Chat.System(s, on ? $"{name} activated: searching for {found}." : $"{name} deactivated.");
                if (on)
                    Report(s, name);
                else
                    Terrain.VeinOverlay.End(s, Terrain.VeinOverlay.Prospecting);
                return Use.Keep;
            });

        /// <summary>The client's DetectCave ability effect type (hash of "DetectCave").</summary>
        private const uint DetectCave = 0x6eb699a5;
        private const float CaveStrength = 1f, CaveFade = 1f;
        private static readonly TimeSpan CaveDuration = TimeSpan.FromHours(1);
        /// <summary>Players with a cave detector on: which one and when its client effect was last started.</summary>
        private static readonly Dictionary<WorldSession, (string Name, DateTime Started)> caveSeekers = new();

        /// <summary>
        /// Cave Seeker / Automatic Chaos Cavern Sounder ("Detects nearby caves when activated", right-click toggles):
        /// live showed the caves in the client's own voxel view — the DetectCave ability effect
        /// (<see cref="Buffs.StartClientEffect"/>), on until toggled off, restarted every hour and after a zone load.
        /// </summary>
        private static Effect CaveDetector(string name)
            => new(name, (s, _, _) =>
            {
                bool on = Buffs.Toggle(s, name);
                lock (caveSeekers)
                {
                    if (caveSeekers.TryGetValue(s, out var other) && other.Name != name && on)
                    {
                        Buffs.Toggle(s, other.Name);   // one detector at a time
                        Buffs.EndClientEffect(s, other.Name);
                    }
                    if (on)
                        caveSeekers[s] = (name, DateTime.UtcNow);
                    else
                        caveSeekers.Remove(s);
                }
                if (on)
                    Buffs.StartClientEffect(s, name, DetectCave, CaveStrength, CaveFade, CaveDuration);
                else
                    Buffs.EndClientEffect(s, name);
                return Use.Keep;
            });

        /// <summary>After a zone load the client has dropped its effects: an active cave detector starts again.</summary>
        public static void ResendGadgets(WorldSession session)
        {
            string name;
            lock (caveSeekers)
            {
                if (!caveSeekers.TryGetValue(session, out var c) || !Buffs.IsOn(session, c.Name))
                    return;
                name = c.Name;
                caveSeekers[session] = (name, DateTime.UtcNow);
            }
            Buffs.StartClientEffect(session, name, DetectCave, CaveStrength, CaveFade, CaveDuration);
        }

        /// <summary>
        /// Item abilities with a cooldown (seconds), sent in their caster info (+0x70, record +0x14c: FUN_142d79400) so the
        /// client greys the hotbar slot and counts it down itself: the healing potions (10 / 25 / 45 s).
        /// </summary>
        private static readonly Dictionary<uint, float> cooldowns = new() { [443] = 10f, [444] = 25f, [445] = 45f };

        public static float CooldownOf(uint ability) => cooldowns.GetValueOrDefault(ability);

        /// <summary>Item ability id → effect.</summary>
        private static readonly Dictionary<uint, Effect> effects = new()
        {
            [170] = StatBuff("Alpha Boost", 17, 20f, 0f, TimeSpan.FromDays(14)),         // +20 % resources, 2 weeks
            [205] = StatBuff("Sharp-Eyed Elixir", 7, 5f, 0f, Minutes15),                 // Discovery +5
            [206] = StatBuff("Potion of Minor Yield", 17, 10f, 0f, Minutes15),           // Harvest Bonus +10
            [207] = StatBuff("Potion of Furious Harvesting", 12, 0f, 25f, Minutes15),    // Harvesting Damage +25 %
            [208] = StatBuff("Potion of Greedy Harvesting", 3, 0f, 25f, Minutes15),      // Harvesting Size +25 %
            [209] = StatBuff("Potion of Nimble Harvesting", 13, 0f, 10f, Minutes15),     // Harvesting Speed +10 %
            [210] = StatBuff("Highstep Brew", 6, 5f, 0f, Minutes15),                     // Jump Bonus +5
            [211] = StatBuff("Bottled Moonlight", 4, 30f, 0f, Minutes30),                // Light Radius +30
            [212] = StatBuff("Spirit of the Wolf", 5, 5f, 0f, Minutes15),                // Run Speed +5
            [221] = StatBuff("Lightstone", 4, 15f, 0f, Minutes30),                       // Light Radius +15, 30 min
            [222] = StatBuff("Greater Lightstone", 4, 15f, 0f, TimeSpan.FromHours(1)),   // Light Radius +15, 1 h
            [443] = Heal("Small Healing Potion", 2f, 10, (int)CooldownOf(443)),
            [444] = Heal("Healing Potion", 3f, 15, (int)CooldownOf(444)),
            [445] = Heal("Large Healing Potion", 5f, 15, (int)CooldownOf(445)),
            [198] = Detector("Ore Prospector", "ore veins"),
            [199] = Detector("Ground Sounder", "gems and rare stones"),
            [480] = CaveDetector("Cave Seeker"),
            [481] = CaveDetector("Automatic Chaos Cavern Sounder"),
            [440] = new("Portal Shard", (s, _, _) => { Spire.TravelTo(s); return Use.Consume; }),
            [338] = new("Builder's Bauble of Flight", (s, _, _) =>
            {
                bool on = Buffs.Toggle(s, "Builder's Bauble of Flight");
                Chat.System(s, on ? "Flight enabled on build sites you can build on." : "Flight disabled.");
                s.SendCalculatedStats();
                return Use.Keep;
            }),
            [1173] = new("Repair Bot Beacon", SummonRepairBot),
            [51] = new("High Yield Dynamite", Blast),
            [1098] = new("Cozy Campfire", LightCampfire),
        };

        /// <summary>Raw ores the Ore Prospector looks for (Copper, Tin, Iron, Silver, Gold).</summary>
        private static readonly HashSet<uint> ores = new() { 34, 35, 36, 37, 39 };
        /// <summary>Gems and rare stones for the Ground Sounder (Marble, Amaranthine, Obsidian, Alabaster, Agate, Emerald, Sapphire, Ruby, Diamond).</summary>
        private static readonly HashSet<uint> gems = new() { 47, 48, 49, 50, 51, 56, 57, 58, 59 };


        /// <summary>Is this ability one of the usable items handled here?</summary>
        public static bool Handles(uint ability) => effects.ContainsKey(ability);

        /// <summary>
        /// Uses an item: runs its effect and, when the effect says so, takes one from the stack. False when the use was
        /// refused (full health, cooldown) — nothing is consumed then.
        /// </summary>
        public static bool TryUse(WorldSession session, CharacterItemModel item, uint ability, Vector3 target)
        {
            if (!effects.TryGetValue(ability, out Effect effect))
                return false;
            Use use = effect.Apply(session, item, target);
            if (use == Use.Consume)
                session.TakeItem(item.ItemDefinitionId, 1);
            log.Info($"{session.CharacterName} used {effect.Name} ({use}).");
            return use != Use.Refuse;
        }

        /// <summary>Extra stat effects of on/off gadgets: flight on a build site the player may build on.</summary>
        public static float Apply(WorldSession session, uint stat, float value)
        {
            if (stat == 29 && Buffs.IsOn(session, "Builder's Bauble of Flight"))
            {
                var at = Terrain.TerrainEdits.ToGlobal(session.Position);
                if (Claims.ClaimAt(session.ZoneId, session.Position) != null
                    && Claims.MayEdit(session.PlayerGuid, at.X, at.Y, at.Z, session.ZoneId))
                    return 1f;
            }
            return value;
        }

        private static DateTime nextReport = DateTime.UtcNow;
        private static readonly Dictionary<WorldSession, bool> flying = new();

        /// <summary>
        /// Gadget tick (from <see cref="Buffs.Update"/>): active prospectors report every 15 s, cave detectors are renewed; the flight bauble is switched
        /// on/off as its owner enters or leaves a build site (the stats are only re-sent on a change).
        /// </summary>
        public static void Update()
        {
            foreach (WorldSession s in Buffs.WithToggle("Builder's Bauble of Flight"))
            {
                bool now = Apply(s, 29, 0f) > 0f;
                if (!flying.TryGetValue(s, out bool was) || was != now)
                {
                    flying[s] = now;
                    s.SendCalculatedStats();
                }
            }
            UpdateCampfires();
            UpdateHoloseekers();
            List<(WorldSession, string)> renew = new();
            lock (caveSeekers)
                foreach (var (s, c) in caveSeekers.ToList())
                    if (s.Disconnected)
                        caveSeekers.Remove(s);
                    else if (DateTime.UtcNow - c.Started > CaveDuration - TimeSpan.FromMinutes(1))
                    {
                        caveSeekers[s] = (c.Name, DateTime.UtcNow);
                        renew.Add((s, c.Name));
                    }
            foreach (var (s, name) in renew)
                Buffs.StartClientEffect(s, name, DetectCave, CaveStrength, CaveFade, CaveDuration);
            if (DateTime.UtcNow < nextReport)
                return;
            nextReport = DateTime.UtcNow.AddSeconds(15);
            foreach (string name in new[] { "Ore Prospector", "Ground Sounder" })
                foreach (WorldSession s in Buffs.WithToggle(name))
                    Report(s, name);
        }

        private static void Report(WorldSession session, string detector)
        {
            Vector3 me = session.Position;
            string result = detector switch
            {
                "Ore Prospector" => Describe(Terrain.OreVeins.Nearest(me, 64f, ores, session.ZoneId), me),
                _ => Describe(Terrain.OreVeins.Nearest(me, 64f, gems, session.ZoneId), me)
            };
            // The prospectors also light up their veins around the player (the client's holographic vein effect).
            if (detector is "Ore Prospector" or "Ground Sounder")
            {
                ISet<uint> wanted = detector == "Ore Prospector" ? ores : gems;
                Terrain.VeinOverlay.Send(session, Terrain.VeinOverlay.Prospecting, ProspectRadius, wanted.Contains);
            }
            string text = result != null ? $"{detector}: {result}." : $"{detector}: nothing nearby.";
            log.Debug($"{session.CharacterName} {text}");
            Chat.System(session, text);
        }

        private static string Describe((uint Item, Vector3 At, float Distance)? hit, Vector3 me)
        {
            if (hit is not { } h)
                return null;
            string name = Inventory.ItemDefinitionManager.Get(h.Item) is { } d
                ? LandmarkEmulator.Shared.GameTable.Text.TextManager.Instance.GetTextForId(d.NameId) ?? "ore" : "ore";
            // Veins are known per terrain cell (16 m): inside the player's own cell there is no better direction to give.
            float cell = (float)(Terrain.TerrainEdits.CellSize / 10.0);
            if (h.Distance < cell)
                return $"{name} right around you (within {cell:0} m)";
            float depth = me.Y - h.At.Y;
            return $"{name} {h.Distance:0} m {Direction(me, h.At)}{(depth > 3 ? $", {depth:0} m down" : depth < -3 ? $", {-depth:0} m up" : "")}";
        }

        /// <summary>Compass direction from one point to another (north = +Z on the client's map).</summary>
        private static string Direction(Vector3 from, Vector3 to)
        {
            double angle = Math.Atan2(to.X - from.X, to.Z - from.Z) * 180 / Math.PI;
            string[] names = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            return names[(int)Math.Round(((angle % 360) + 360) % 360 / 45) % 8];
        }

        /// <summary>Dynamite crater radius (decimetres, as pick dig radii: the Adventurer's pick digs 10) and throw reach (m).</summary>
        private const double BlastRadius = 40;
        private const float BlastReach = 30f;

        /// <summary>High Yield Dynamite: blows a crater where it's thrown and collects what it broke loose.</summary>
        private static Use Blast(WorldSession session, CharacterItemModel item, Vector3 target)
        {
            int voxels = Abilities.Excavate(session, target, BlastRadius, collect: true, digOn: false, BlastReach);
            if (voxels == 0)
            {
                Chat.System(session, "Nothing there to blow up.");
                return Use.Refuse;
            }
            Emotes.ShowSwing(session, 226, target);
            return Use.Consume;
        }

        /// <summary>How far the prospecting overlays reach, metres.</summary>
        private const float ProspectRadius = 48f;

        /// <summary>
        /// Tools that show the vein overlay while held: the NovaTech Holoseeker (3704, "reveals stone, gem, and ore veins
        /// in the world via a holographic overlay") and the Farseer Pick (3257, "shows outline/distance of all mined
        /// resources").
        /// </summary>
        private static readonly HashSet<uint> veinSeers = new() { 3704, 3257 };

        /// <summary>Players with the Holoseeker out: when and where their overlay was last sent.</summary>
        private static readonly Dictionary<WorldSession, (DateTime At, Vector3 Where)> holoseekers = new();

        /// <summary>
        /// Holoseeker tick (once a second): while one is equipped its overlay shows every vein near the player, re-sent
        /// after the player has moved 12 m or every 25 s (the effects last 30 s); put away, the overlay goes.
        /// </summary>
        private static void UpdateHoloseekers()
        {
            foreach (WorldSession s in PlayerManager.Snapshot())
            {
                bool held = s.Inventory?.Items.Any(i => veinSeers.Contains(i.ItemDefinitionId)
                    && i.Container == Inventory.Inventory.EquippedContainer) == true;
                bool shown = holoseekers.TryGetValue(s, out var last);
                if (!held)
                {
                    if (shown)
                    {
                        holoseekers.Remove(s);
                        Terrain.VeinOverlay.End(s, Terrain.VeinOverlay.Holoseeker);
                    }
                    continue;
                }
                if (shown && DateTime.UtcNow - last.At < TimeSpan.FromSeconds(25) && Vector3.Distance(last.Where, s.Position) < 12f)
                    continue;
                Terrain.VeinOverlay.Send(s, Terrain.VeinOverlay.Holoseeker, ProspectRadius);
                if (!shown)
                    Achievements.ScanCompleted(s);     // "Scan completed" (Scanning, Captain)
                holoseekers[s] = (DateTime.UtcNow, s.Position);
            }
            foreach (WorldSession gone in holoseekers.Keys.Where(k => k.Disconnected).ToList())
                holoseekers.Remove(gone);
        }

        private sealed record Campfire(ulong Guid, uint Zone, Vector3 Position, DateTime Ends);

        private static readonly List<Campfire> campfires = new();

        /// <summary>How long a Cozy Campfire burns, how near it warms, and its two heals (% of max health per second).</summary>
        private static readonly TimeSpan CampfireLifetime = TimeSpan.FromMinutes(10), EnduringWarmthDuration = TimeSpan.FromMinutes(10);
        private const float CampfireRange = 8f, ComfortingWarmth = 4f, EnduringWarmth = 1f;

        /// <summary>Cozy Campfire: a campfire at the aim point for a while (see <see cref="UpdateCampfires"/>).</summary>
        private static Use LightCampfire(WorldSession session, CharacterItemModel item, Vector3 target)
        {
            (ulong guid, Vector3 at) = Props.PlaceFromItem(session, item.ItemDefinitionId, target);
            lock (campfires)
                campfires.Add(new Campfire(guid, session.ZoneId, at, DateTime.UtcNow + CampfireLifetime));
            return Use.Consume;
        }

        /// <summary>
        /// Campfire tick (once a second): players near a burning campfire get Comforting Warmth (a quick heal) while
        /// wounded and Enduring Warmth (a slow heal that goes with them) once at full health; burnt-out fires go away.
        /// </summary>
        private static void UpdateCampfires()
        {
            List<Campfire> burning;
            lock (campfires)
            {
                foreach (Campfire fire in campfires.Where(f => f.Ends <= DateTime.UtcNow).ToList())
                {
                    campfires.Remove(fire);
                    Props.Despawn(fire.Guid);
                }
                if (campfires.Count == 0)
                    return;
                burning = campfires.ToList();
            }
            foreach (WorldSession player in PlayerManager.Snapshot())
            {
                if (!burning.Any(f => f.Zone == player.ZoneId && Vector3.Distance(f.Position, player.Position) <= CampfireRange))
                    continue;
                if (Health.Current(player) < Health.MaxHealth)
                    Health.Heal(player, (uint)Math.Ceiling(Health.MaxHealth * ComfortingWarmth / 100f));
                else if (!Buffs.IsHealing(player))
                    Buffs.HealOverTime(player, EnduringWarmth, EnduringWarmthDuration);
            }
        }

        /// <summary>How long a summoned Repair Bot stays.</summary>
        private static readonly TimeSpan RepairBotLifetime = TimeSpan.FromMinutes(5);

        /// <summary>Repair Bot Beacon: a Repair Bot (crafting station 57) next to the player for a few minutes.</summary>
        private static Use SummonRepairBot(WorldSession session, CharacterItemModel item, Vector3 target)
        {
            uint prop = CraftingStations.PropForStation(57);
            if (prop == 0)
            {
                Chat.System(session, "The Repair Bot didn't answer the beacon.");
                return Use.Refuse;
            }
            ulong guid = Props.PlaceNear(session, prop, new Vector3(1.5f, 0f, 1.5f));
            if (guid == 0)
                return Use.Refuse;
            Chat.System(session, $"A Repair Bot arrives for {RepairBotLifetime.TotalMinutes:0} minutes.");
            System.Threading.Tasks.Task.Delay(RepairBotLifetime).ContinueWith(_ => Props.Despawn(guid));
            return Use.Consume;
        }
    }
}
