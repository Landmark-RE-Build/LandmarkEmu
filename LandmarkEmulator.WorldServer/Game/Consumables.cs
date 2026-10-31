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

        /// <summary>
        /// What a use did: take one (Consume), take none (Keep: toggles, figurine-like gadgets), refuse (CastFail, nothing
        /// taken), or Taken: the effect already took the item itself (it had to come out before what replaces it went in,
        /// e.g. the Pack of Unstable Fireworks).
        /// </summary>
        private enum Use { Consume, Keep, Refuse, Taken }

        /// <summary>An item's effect: (player, the item used, the point aimed at) → whether one is used up.</summary>
        private sealed record Effect(string Name, Func<WorldSession, CharacterItemModel, Vector3, Use> Apply);

        private static readonly TimeSpan Minutes15 = TimeSpan.FromMinutes(15), Minutes30 = TimeSpan.FromMinutes(30);

        private static Effect StatBuff(string name, uint stat, float add, float percent, TimeSpan duration)
            => new(name, (s, item, _) => { Buffs.AddStat(s, name, stat, add, percent, duration, item.ItemDefinitionId); return Use.Consume; });

        private static Effect Heal(string name, float percentPerSecond, int seconds, int cooldown)
            => new(name, (s, item, _) =>
            {
                if (Health.Current(s) >= Health.Max(s))
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

        /// <summary>
        /// An item ability's cooldown in seconds (0 = none): the potions', and the firework consumables' shared launch
        /// cooldown (<see cref="Fireworks.CooldownSeconds"/>, ours).
        /// </summary>
        public static float CooldownOf(uint ability)
            => cooldowns.TryGetValue(ability, out float seconds) ? seconds : Fireworks.CooldownOf(ability);

        /// <summary>
        /// Fireworks (TASKS 4.12, trace/0695/spec-fireworks.md §5.2 E): the 22 consumables (our abilities 1215-1236 on the
        /// built items 4421-4442, <see cref="Fireworks.Kinds"/>) launch their emitter composite at the player and are used
        /// up — refused while the player's launch cooldown runs; the Pack of Unstable Fireworks (live ability 1237) turns
        /// into 50 random consumables, refused when they don't fit.
        /// </summary>
        private static Dictionary<uint, Effect> WithFireworks(Dictionary<uint, Effect> map)
        {
            foreach (Fireworks.Kind kind in Fireworks.Kinds)
                map[kind.AbilityId] = new(kind.Name, (s, _, _) => Fireworks.Launch(s, kind) ? Use.Consume : Use.Refuse);
            map[Fireworks.UnstableAbility] = new("Pack of Unstable Fireworks",
                (s, item, _) => Fireworks.OpenUnstablePack(s, item) ? Use.Taken : Use.Refuse);
            return map;
        }

        /// <summary>Item ability id → effect.</summary>
        private static readonly Dictionary<uint, Effect> effects = WithFireworks(new()
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
        });

        /// <summary>Raw ores the Ore Prospector looks for (Copper, Tin, Iron, Silver, Gold).</summary>
        private static readonly HashSet<uint> ores = new() { 34, 35, 36, 37, 39 };
        /// <summary>Gems and rare stones for the Ground Sounder (Marble, Amaranthine, Obsidian, Alabaster, Agate, Emerald, Sapphire, Ruby, Diamond).</summary>
        private static readonly HashSet<uint> gems = new() { 47, 48, 49, 50, 51, 56, 57, 58, 59 };


        /// <summary>
        /// The drinking clip (OPEN-LEADS L192 (f)): AnimationSelection 287 (EmoteSingle, Emote_Index 22), whose clip in the
        /// 0.695 player network is Biped_Core_Utility_UsePotion_01 (StandPoses|…|Emotes_SingleShot|Nuetral|…UsePotion_01,
        /// node 11829, and its mood twins) — the network's only potion / drinking clip [P]. Which clip live's potion
        /// abilities named is server data (no client table ties an item ability to a selection) [I: the one that exists].
        /// Played for the player (no turn: RequestAnimationSelection) and the players within Emotes.ViewRange. As every
        /// emote clip it shows only with empty hands (StandPoses|Idle_Stand_2 child 0) [P].
        /// </summary>
        public const uint UsePotionAnimation = 287;

        /// <summary>The drinks: the three healing potions and the three brews (Highstep Brew, Bottled Moonlight, Spirit of the Wolf).</summary>
        private static readonly HashSet<uint> Drinks = new() { 443, 444, 445, 210, 211, 212 };

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
            if (use == Use.Consume && Drinks.Contains(ability))
                Emotes.PlayAnim(session, UsePotionAnimation);
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
            // Players who left (or put the bauble away): every session that ever used it stayed here for good, keeping
            // the sessions alive (bughunt-combat.md B9). One pass after the update (the merge of c04d3af had a second,
            // narrower one before it; review 2026-10-18b #11).
            if (flying.Count > 0)
                foreach (WorldSession gone in flying.Keys.Where(k => k.Disconnected || !Buffs.IsOn(k, "Builder's Bauble of Flight")).ToList())
                    flying.Remove(gone);
            UpdateCampfires();
            UpdateRepairBots();
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
                "Ore Prospector" => Describe(Terrain.EnvironmentServer.Instance.NearestVein(me, 64f, ores, session.ZoneId), me),
                _ => Describe(Terrain.EnvironmentServer.Instance.NearestVein(me, 64f, gems, session.ZoneId), me)
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

        /// <summary>The throw's Enter selection (Class 7 ThrowUser, AttackIndex 0); its Release is the next id, 42.</summary>
        public const uint ThrowEnterAnimation = 41;

        /// <summary>Dynamite crater radius (decimetres, as pick dig radii: the Adventurer's pick digs 10) and throw reach (m).</summary>
        private const double BlastRadius = 40;
        private const float BlastReach = 30f;

        /// <summary>
        /// High Yield Dynamite: blows a crater where it's thrown and collects what it broke loose. A temporary destruction
        /// tool (0.695, <see cref="VoxelDestruction"/>): it heals after 100 s (P6037), works on build sites in Play Mode
        /// unless their Play Mode Rules or a Linking Area block it (then the client got EffectEnd result 9 and nothing is
        /// used up — inferred: live's refusal came before the item was spent), and collects nothing on a site.
        /// </summary>
        private static Use Blast(WorldSession session, CharacterItemModel item, Vector3 target)
        {
            int voxels = Abilities.Excavate(session, target, BlastRadius, collect: true, digOn: false, BlastReach,
                explosive: true, healDelay: VoxelDestruction.DynamiteHeal);
            if (voxels < 0)
                return Use.Refuse;
            Achievements.DynamiteUsed(session);   // Physician Boom (637): "Use High Yield Dynamite"
            if (voxels == 0)
            {
                Chat.System(session, "Nothing there to blow up.");
                return Use.Refuse;
            }
            // The throw (OPEN-LEADS L192 (f)): AnimationSelection 41 Enter / 42 Release, Class 7, which the 0.695 player
            // network's Action|Action|Enter|Landmark and …|Release|Landmark switches (on Class) play as child 7,
            // Biped_Core_Harvesting_Throw_F01 — its only throw clip [P]; it was the pick swing (226/227) before. To the
            // thrower too: no cast answer animates a consumable on its own client.
            Emotes.ShowSwing(session, ThrowEnterAnimation, target, includeSelf: true);
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
        private const float CampfireRange = 8f, EnduringWarmth = 1f;

        /// <summary>Cozy Campfire: a campfire at the aim point for a while (see <see cref="UpdateCampfires"/>).</summary>
        private static Use LightCampfire(WorldSession session, CharacterItemModel item, Vector3 target)
        {
            // The Cozy Campfire can no longer be cast while jumping or falling, so no more floating campfires (hotfix
            // 26 Apr 2016, P4702) [P]. The client doesn't check it itself
            // (TASKS 6.19, checked 2026-10-16): its cast errors are only AlreadyCharging / NoMana / NoReagent / OnCooldown /
            // NoHealth / NoResource / ItemSwap / EffectFailed (strings.tsv, FUN_14351ebd0), none of the 75 client
            // Requirements (Requirements.txt) tests jumping or falling, and the campfire's item record carries no
            // requirement — so live refused it on the server. Refused here: CastFail (the item is kept, its hotbar
            // cooldown handed back) and a line of text (ours: the client has no text for it).
            if (Airborne(session))
            {
                Chat.System(session, "You can't light a campfire while jumping or falling.");
                return Use.Refuse;
            }
            (ulong guid, Vector3 at) = Props.PlaceFromItem(session, item.ItemDefinitionId, target);
            Achievements.CampfirePlaced(session);   // Cry Havoc! "Place a campfire [5]" (1315)
            lock (campfires)
                campfires.Add(new Campfire(guid, session.ZoneId, at, DateTime.UtcNow + CampfireLifetime));
            return Use.Consume;
        }

        /// <summary>Vertical speed (m/s) above which a moving player counts as jumping or falling (ours).</summary>
        private const float AirborneVerticalSpeed = 2.5f;
        /// <summary>Movement older than this means the player stopped (a client sends movement only while moving).</summary>
        private static readonly TimeSpan MovementFresh = TimeSpan.FromSeconds(0.75);

        /// <summary>
        /// Is the player jumping, falling or gliding now? The movement body's state word carries the client's own flags
        /// (writer BaseClient::BroadcastLocalPlayerMovement FUN_142ca4ce0; trace/0695/agent-leads2-b.md L121) [P]: 0x10
        /// jumping, 0x20 falling, 0x800 double jump, 0x1000 gliding — the client's own airborne test FUN_142e934c0 checks the
        /// same flags. Every flag change is sent at once (a type-0 or type-1 body), so the last word stays valid. Before the
        /// first word arrives: the vertical speed of the latest movement update (<see cref="WorldSession.LastVelocity"/>)
        /// above <see cref="AirborneVerticalSpeed"/> [I].
        /// </summary>
        public static bool Airborne(WorldSession session)
            => session.MovementState != 0 ? (session.MovementState & AirborneFlags) != 0
                : DateTime.UtcNow - session.LastVelocityAt <= MovementFresh && MathF.Abs(session.LastVelocity.Y) > AirborneVerticalSpeed;

        /// <summary>State-word flags of a player in the air: jumping 0x10, falling 0x20, double jump 0x800, gliding 0x1000.</summary>
        private const uint AirborneFlags = 0x10 | 0x20 | 0x800 | 0x1000;

        /// <summary>Combat, for the campfire: hurt this recently (as the Lost Crystal's check).</summary>
        private static readonly TimeSpan CampfireCombat = TimeSpan.FromSeconds(6);

        /// <summary>When each player last got Enduring Warmth: refreshable once a minute (13 Apr 2016, P4539).</summary>
        private static readonly Dictionary<WorldSession, DateTime> lastWarmth = new();

        /// <summary>
        /// Campfire tick (once a second): out of combat, a player near a burning campfire is "immediately healed to full"
        /// and gets Enduring Warmth (a slow heal that goes with them), refreshable once a minute; campfires do nothing
        /// during combat (21 Mar 2016, P4190). Burnt-out fires go away.
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
                if (Health.IsDead(player) || Health.HurtWithin(player, CampfireCombat))
                    continue;
                if (Health.Current(player) < Health.Max(player))
                    Health.Heal(player, Health.Max(player) - Health.Current(player));
                DateTime now = DateTime.UtcNow;
                lock (lastWarmth)
                {
                    if (lastWarmth.TryGetValue(player, out DateTime last) && now - last < TimeSpan.FromMinutes(1))
                        continue;
                    lastWarmth[player] = now;
                    foreach (WorldSession gone in lastWarmth.Keys.Where(k => k.Disconnected).ToList())
                        lastWarmth.Remove(gone);
                }
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
            // Sent away by the world tick (UpdateRepairBots), like a burnt-out campfire. It was a Task.Delay continuation:
            // Props.Despawn on a pool thread changed the prop streams, StoryTools / PropSettings / build-mode state and
            // sent packets while the world thread used them (bughunt-combat.md B7).
            lock (repairBots)
                repairBots.Add((guid, DateTime.UtcNow + RepairBotLifetime));
            return Use.Consume;
        }

        /// <summary>Summoned Repair Bots and when each leaves.</summary>
        private static readonly List<(ulong Guid, DateTime Ends)> repairBots = new();

        /// <summary>World tick (from <see cref="Update"/>): Repair Bots whose time is up go.</summary>
        private static void UpdateRepairBots()
        {
            List<ulong> gone;
            DateTime now = DateTime.UtcNow;
            lock (repairBots)
            {
                if (repairBots.Count == 0)
                    return;
                gone = repairBots.Where(b => b.Ends <= now).Select(b => b.Guid).ToList();
                repairBots.RemoveAll(b => b.Ends <= now);
            }
            foreach (ulong guid in gone)
                Props.Despawn(guid);
        }
    }
}
