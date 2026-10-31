using LandmarkEmulator.WorldServer.Game.Terrain;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using Microsoft.Extensions.Configuration;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Trees (axe) and plants (sickle) to gather. Both are server entities in Landmark (the client's tree destruction
    /// looks a tree up by its ProxiedCharacter guid; the axe and sickle outline them as targets and name them in the
    /// cast), so the server places them: each 64 m region gets a fixed, seeded set from its biome (the client's biome
    /// map), standing on the generated ground (<see cref="TerrainHeights"/>), above sea level, on gentle slopes and
    /// outside build sites. Players are sent only the regions around them (TreeStreamRange, Gathering.Stream.cs); a gathered
    /// node is removed for everyone in range and grows back later.
    /// <para>
    /// On the Desert, Tropical, Old Growth and Tundra surface biomes the trees follow the shipped procedural placement
    /// sets (<see cref="ProceduralPlacement"/>: the sets' own tree props, spacing, slope / altitude limits, ground
    /// materials, masks, scale / heading ranges and respawn minutes); Volcanic, Deciduous and the ocean's shore, which
    /// have no sets, keep the tree lists below. Set nodes use indices 128-255 of a region's keys.
    /// </para>
    /// <para>
    /// What happens after a node is gathered is the respawn model (setting ProceduralRespawn, Gathering.Respawn.cs): live's
    /// minimum / maximum per type and landscape with respawns elsewhere (default), or the older regrow-in-place.
    /// </para>
    /// </summary>
    public static partial class Gathering
    {
        /// <summary>Party members within this many metres share what a member gathers (wiki Gathering: everyone
        /// harvesting within 200 m of each other shares the materials).</summary>
        public const float PartyShareRange = 200f;

        /// <summary>
        /// Drops while gathering (wiki Gathering / Lumens / Recipes / Essence: gathering may also drop Essences,
        /// recipes and Lumens, more often from ore veins, uncommon stone and trees). The live rates were server data;
        /// these are ours. <paramref name="rich"/>: a vein, a tree or a
        /// plant (plain digging only rarely drops Lumens).
        /// </summary>
        public static void RollDrops(WorldSession session, bool rich, uint tool = 0, bool wearTool = true)
        {
            // One harvesting swing: the held tool loses condition (TASKS 6.53, Durability.ToolUsed). RollDrops runs once
            // per collecting mining swing (Abilities.Dig) and per reaped plant; Chop wears on every hit itself and
            // passes wearTool: false for the felling hit.
            if (wearTool)
                Durability.ToolUsed(session);
            // Charmed tools: "a bit of luck" — finds come twice as often.
            double roll = random.NextDouble() / (CharmedTools.Contains(tool) ? 2.0 : 1.0);
            // Lumens and essences follow the play-session curve (Pacing: fewer early, more in long sessions).
            double pace = Pacing.Factor(session);
            double lumenChance = RichLumenChance * pace, essenceChance = EssenceChance * pace;
            if (!rich)
            {
                if (roll < PlainLumenChance * pace)
                    GiveHarvest(session, Health.LumenItem, 1);
                return;
            }
            if (roll < lumenChance)
                GiveHarvest(session, Health.LumenItem, (uint)random.Next(1, 4));
            else if (roll < lumenChance + essenceChance)
                GiveHarvest(session, AdventureEssence, 1);
            else if (roll < lumenChance + essenceChance + RecipeChance
                     && RecipeBook.RandomUnknownRecipeItem(session, random) is uint recipe and not 0)
            {
                session.GiveItem(recipe, 1);
                log.Info($"{session.CharacterName} found recipe item {recipe} while gathering.");
            }
            else if (roll >= lumenChance + essenceChance + RecipeChance
                     && roll < lumenChance + essenceChance + RecipeChance + EmitterChance)
            {
                // A rare prop drop: a Fireworks Emitter item 4455-4476 (P5915: dropped at random like other props; a
                // player
                // saw one drop on day one, forum 57596; trace/0695/spec-fireworks.md §5.2 F). Uncommon kinds come twice
                // as often as rare ones (Fireworks.RandomEmitterItem). The band starts after the recipe band so a player
                // who knows every recipe doesn't get emitters more often.
                uint emitter = Fireworks.RandomEmitterItem(random);
                session.GiveItem(emitter, 1);
                log.Info($"{session.CharacterName} found fireworks emitter item {emitter} while gathering.");
            }
            // A rare Repair Bot Beacon, rolled on its own (P4835 "global loot").
            if (RareDrops.Beacon(random) is uint beacon and not 0)
            {
                session.GiveItem(beacon, 1);
                log.Info($"{session.CharacterName} found a Repair Bot Beacon while gathering.");
            }
        }

        /// <summary>Charmed Pick / Charmed Axe (wiki: a chance at recipes and special resources).</summary>
        private static readonly HashSet<uint> CharmedTools = new() { 3254, 3260 };
        /// <summary>Hotfoot Axe, NovaTech Velocitizer: "a brief speed boost after logging" / "a small speed buff after use".</summary>
        private static readonly HashSet<uint> SpeedBurstTools = new() { 3263, 3705 };

        /// <summary>Extra yield of a tool: the NovaTech Harvestron "gives a boost to the amount of resources gathered".</summary>
        public static float ToolYieldBonus(uint tool) => tool == 3703 ? 1.25f : 1f;

        /// <summary>After a tool is used: the speed burst of the Hotfoot Axe and the Velocitizer (run speed +25 % for 8 s).</summary>
        public static void AfterUse(WorldSession session, uint tool)
        {
            if (SpeedBurstTools.Contains(tool))
                Buffs.AddStat(session, "Speed Burst", MaxMovementSpeedStat, 0f, 25f, TimeSpan.FromSeconds(8), tool);
        }

        /// <summary>StatId.MaxMovementSpeed (client constant).</summary>
        private const uint MaxMovementSpeedStat = 2;

        private const double PlainLumenChance = 0.003, RichLumenChance = 0.04, EssenceChance = 0.03, RecipeChance = 0.0075;
        /// <summary>Chance of a fireworks emitter prop item per rich gather (ours; live's rate was server data).</summary>
        private const double EmitterChance = 0.002;
        /// <summary>Adventure Essence (item 3683): a crafting component from gathering and exploring.</summary>
        public const uint AdventureEssence = 3683;

        /// <summary>Gives a harvest to the gatherer and the same to their party members nearby.</summary>
        public static void GiveHarvest(WorldSession session, uint item, uint count)
        {
            session.GiveItem(item, count);
            LootPinatas.Notify(session, item, count);
            foreach (WorldSession member in Groups.NearbyMembers(session, PartyShareRange))
            {
                member.GiveItem(item, count);
                LootPinatas.Notify(member, item, count);
            }
        }

        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private const float RegionSize = 64f;
        private const int CellsPerSide = 8;               // candidate spots per region side (one per 8 m)
        private const int PlantIndexBase = CellsPerSide * CellsPerSide;
        private const float SeaLevel = 1002f;              // local metres; the ocean biome lies below ~1000
        private const float MaxSlope = 0.7f;
        private const float Reach = 8f;                    // player to node, horizontal metres
        private const float AimTolerance = 3f;             // swing target to node, horizontal metres
        private static readonly TimeSpan TreeRegrowTime = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan PlantRegrowTime = TimeSpan.FromMinutes(20);
        private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);
        private const ulong GuidBase = 0x4000_0000_0000_0000;

        private const uint PlainWood = 71, BurledWood = 72, TreeFiber = 921, TreeSap = 922;

        /// <summary>Tree (axe), Plant (sickle) or Decor (a placement set's rock / bush / flower: shown, not gathered).</summary>
        private enum NodeType { Tree, Plant, Decor }

        private sealed class Node
        {
            public ulong Key;
            /// <summary>The landscape the node grows on.</summary>
            public uint Zone;
            public ulong Guid => GuidBase | Key;
            public NodeType Type;
            /// <summary>The placement set it came from (respawn minutes), or null for the old tree lists.</summary>
            public ProceduralPlacement.PlacementSet Set;
            /// <summary>The set entity it came from (admin monitor counts), or null for the old tree lists.</summary>
            public ProceduralPlacement.SetEntity Entity;
            /// <summary>The biome under an old-list node (part of its respawn pool, <see cref="PoolKeyOf"/>); 0 for set nodes.</summary>
            public int Biome;
            public PlayerUpdateAddLightweightProp Spawn;
            public uint Yield;          // wood item for trees, plant item for plants
            public int MaxHits;         // swings to fell / gather under TreeHealth=hits (and for plants)
            public int Hits;
            /// <summary>A tree's Resources.txt Health row, its full health and what is left (TreeHealth=health; Gathering.Health.cs).</summary>
            public uint LifeRow, InitialHealth, Health;
            public DateTime RegrowsAt;
            public bool Standing => RegrowsAt <= DateTime.UtcNow;
        }

        private sealed class TreeKind
        {
            public uint[] Models;
            public float Density;   // chance per candidate spot
            public uint Wood;
        }

        /// <summary>
        /// A harvestable plant: Biome_Generic_Plants_[name]_{Common,Uncommon,Rare}01 and the plant items the sickle
        /// gathers ("Gathers once from all size plants"): the plain plant, then (Large) and (Perfect) where 0.589 has them.
        /// </summary>
        private sealed record PlantKind(uint CommonModel, uint UncommonModel, uint RareModel, uint Item, uint LargeItem, uint PerfectItem);

        private static readonly PlantKind Amberleaf = new(464, 471, 470, 735, 735, 735);
        private static readonly PlantKind Cotton = new(472, 475, 474, 736, 2803, 2804);
        private static readonly PlantKind Earthblood = new(476, 479, 478, 737, 737, 737);
        private static readonly PlantKind Sunblossom = new(480, 483, 482, 738, 2811, 2812);
        private static readonly PlantKind Vilebloom = new(484, 487, 486, 739, 2813, 2814);

        // Biome id (VoxelBiomes) → trees that grow there. Models.txt ids of the biome's own tree sets. Only models whose
        // .adr names a collision mesh (CollisionData .cdt): 344 GnarledPine_Large01 and 350 Birch_Medium01 have none, so
        // players walked through them.
        private static readonly Dictionary<int, TreeKind> treeKinds = new()
        {
            // OldGrowthForestSurface (sky/audio "JaggedPine"): jagged, gnarled and diamond pines.
            // Every Old Growth tree gives Burled Wood (hotfix 11 May 2016, P5415: "All old growth forest trees should now be
            // giving burled wood"); one wood per biome (13 Apr 2016, P4628).
            [50] = new TreeKind { Models = new uint[] { 338, 342, 343, 345, 346, 347, 348, 349 }, Density = 0.16f, Wood = BurledWood },
            // VolcanicSurface (Lavastorm): sparse scorched trees, striped wood.
            [47] = new TreeKind { Models = new uint[] { 225, 226, 1370, 1371 }, Density = 0.05f, Wood = 73 }
        };
        private static readonly TreeKind fallbackTrees = new() { Models = new uint[] { 351, 352, 1467, 1468, 1469 }, Density = 0.03f, Wood = PlainWood };

        private static readonly Dictionary<int, PlantKind[]> plantKinds = new()
        {
            [50] = new[] { Amberleaf, Cotton, Earthblood },
            [47] = new[] { Sunblossom, Vilebloom }
        };
        private static readonly PlantKind[] fallbackPlants = { Cotton, Sunblossom };
        private const float PlantDensity = 0.04f;

        /// <summary>Whether the world grows harvestable plant nodes: not since 21 Mar 2016 (P4128).</summary>
        private const bool PlantsHarvestable = false;

        private static readonly object sync = new();
        private static readonly Dictionary<long, Node[]> regions = new();
        private static readonly Dictionary<ulong, DateTime> gathered = new();
        private static readonly Dictionary<WorldSession, HashSet<long>> sent = new();
        private static readonly Dictionary<WorldSession, DateTime> lastUpdate = new();
        private static readonly Random random = new();
        private static string savePath;
        private static bool dirty;

        /// <summary>
        /// Loads which trees and plants are gathered: with the regrow-in-place model when they regrow ("key ticks" lines),
        /// with the min / max model which candidate spots are free ("E key") and which landscapes have had their first
        /// population rolled ("Z landscape generator"); see <see cref="LoadRespawnLine"/>.
        /// </summary>
        public static void Load(string path)
        {
            savePath = path;
            // The placement sets (gameData) and their masks (the install); settings in WorldServer.json or the
            // environment: ProceduralPlacement (default off), ProceduralPlacementDecor (off), ProceduralPlacementMasks (on).
            // Off by default: all 108 shipped sets name the 2014 alpha biomes (18-31), none a launch biome, so live's launch
            // placement came from rules the client doesn't carry and using the sets on launch landscapes rests on the
            // inferred biome-type match (trace/0695/procedural-placement-695.md, L113); it also halves Old Growth forests.
            var config = LandmarkEmulator.Shared.Configuration.SharedConfiguration.Configuration;
            ProceduralPlacement.Load(config?.GetValue("GameDataPath", "gameData") ?? "gameData", Landscapes.GameDirectory,
                config?.GetValue("ProceduralPlacement", false) ?? false,
                config?.GetValue("ProceduralPlacementDecor", false) ?? false,
                config?.GetValue("ProceduralPlacementMasks", true) ?? true);
            LoadRespawnSettings(config);
            LoadHealthSettings(config);
            if (!File.Exists(path))
                return;
            foreach (string line in File.ReadLines(path))
            {
                string[] f = line.Split(' ');
                if (LoadRespawnLine(f))
                    continue;
                // Regrow-in-place entries; under the min / max model nothing regrows in place, so they are dropped.
                if (Respawn == RespawnModel.InPlace && f.Length == 2 && ulong.TryParse(f[0], out ulong key)
                    && long.TryParse(f[1], out long ticks) && ticks > DateTime.UtcNow.Ticks)
                    gathered[key] = new DateTime(ticks, DateTimeKind.Utc);
            }
            if (Respawn == RespawnModel.InPlace)
                log.Info($"Gathering: {gathered.Count} tree(s)/plant(s) still regrowing.");
            else
                log.Info($"Gathering: {empty.Count} free spot(s) saved on {rolled.Count} landscape(s).");
        }

        /// <summary>Called every server tick: streams regions around each player, regrows nodes and saves.</summary>
        private static DateTime nextTrim;

        public static void Update()
        {
            if (!TerrainHeights.Loaded)
                return;
            DateTime now = DateTime.UtcNow;
            if (now >= nextTrim)
            {
                nextTrim = now.AddMinutes(1);
                TerrainHeights.Trim();   // drop idle landscapes' height grids (up to about 64 MB each)
            }
            RefillRegionGeneration(now);   // the players' shared region generation time (Gathering.Stream.cs)
            foreach (WorldSession session in PlayerManager.Snapshot())
            {
                lock (sync)
                {
                    if (lastUpdate.TryGetValue(session, out DateTime last) && now - last < UpdateInterval)
                        continue;
                    lastUpdate[session] = now;
                }
                Stream(session);
            }
            RegrowDue(now);
            RespawnUpdate(now);
            // At most every SaveInterval, written off the world thread (the min / max model's list of free spots is
            // thousands of lines per landscape).
            if (dirty && now >= nextSave)
            {
                nextSave = now + SaveInterval;
                Save(background: true);
            }
        }

        private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(10);
        private static DateTime nextSave;

        /// <summary>
        /// Drops what was sent to a player who left.
        /// </summary>
        public static void Forget(WorldSession session)
        {
            lock (sync)
            {
                sent.Remove(session);
                lastUpdate.Remove(session);
                harvestTargets.Remove(session);   // the client drops its targets with the entities (re-zone, logout)
            }
            // (Also called after a re-zone, PlayerManager.Reintroduce: losing the 5 s throttle then costs nothing.)
            lastWrongTool.TryRemove(session, out _);
        }

        /// <summary>
        /// An axe swing (<see cref="Abilities.LoggingAbilities"/>). Each hit gives wood (and sometimes Tree Fiber,
        /// "found on all trees") and takes the axe's damage off the tree's health (TreeHealth=health; the swinger's bar
        /// follows it, Gathering.Health.cs); the hit that empties it fells the tree.
        /// </summary>
        public static bool Chop(WorldSession session, ulong targetGuid, Vector3 target, uint tool = 0)
        {
            Node tree = Target(session, NodeType.Tree, targetGuid, target);
            if (tree == null)
            {
                WrongTool(session, targetGuid, NodeType.Plant, "That plant needs a sickle.");
                return false;
            }
            uint damage = SwingDamage(session, tool);
            bool felled = Hit(tree, damage, RegrowTime(tree, TreeRegrowTime));
            Durability.ToolUsed(session);   // every axe hit is a harvest (TASKS 6.53)
            // The Goliath Axe gives more per swing but swings slower; Harvest Bonus (stat 17) counts on trees
            // too (1 Apr 2016, P4389: harvest-bonus items gave too much from trees — reduced, not removed).
            uint wood = (uint)(random.Next(2, 5) * (tool == 3261 ? 1.5 : 1.0) * ToolYieldBonus(tool)
                * (1f + Buffs.Apply(session, 17, 0f) / 100f) * Durability.ToolStrength(session));   // broken axe: half (6.53)
            wood = Math.Max(1u, wood);
            GiveHarvest(session, tree.Yield, wood);
            Achievements.Harvested(session, tree.Yield, wood);
            LootPinatas.SpawnChunk(session, target + new Vector3(0f, 1f, 0f), tree.Yield, wood);
            if (random.NextDouble() < 0.3)
                GiveHarvest(session, TreeFiber, (uint)random.Next(1, 3));
            // Finds and Tree Sap: once per felled tree under TreeHealth=hits; under the health model a chance on every swing
            // (live rolled Discovery per swing on trees, design/game-rules.md), scaled so 80 damage keeps the old 5-hit tree's
            // rate per swing (OPEN-LEADS L246).
            if (HasLife(tree) ? random.NextDouble() < Math.Min(1.0, damage / (double)RewardPace) : felled)
            {
                if (random.NextDouble() < 0.15)
                    GiveHarvest(session, TreeSap, 1);
                RollDrops(session, rich: true, tool, wearTool: false);
            }
            if (!felled)
            {
                if (HasLife(tree))
                    HarvestTarget(session, tree);   // the swinger's target slot 9 holds this tree (its bar)
                SendLifeAfterHit(session, tree);
                log.Debug(HasLife(tree) ? $"{session.CharacterName} hit tree {tree.Key:X} ({tree.Health}/{tree.InitialHealth} health)."
                    : $"{session.CharacterName} hit tree {tree.Key:X} ({tree.Hits}/{tree.MaxHits}).");
                return true;
            }
            AfterUse(session, tool);   // the speed burst "after logging": per felled tree
            Despawn(tree);
            log.Debug($"{session.CharacterName} felled tree {tree.Key:X} (model {tree.Spawn.ModelId}).");
            return true;
        }

        /// <summary>A sickle swing (<see cref="Abilities.ReapingAbilities"/>): gathers the plant as its plant item.</summary>
        public static bool Reap(WorldSession session, ulong targetGuid, Vector3 target, uint tool = 0)
        {
            Node plant = Target(session, NodeType.Plant, targetGuid, target);
            if (plant == null)
            {
                WrongTool(session, targetGuid, NodeType.Tree, "That tree needs an axe.");
                return false;
            }
            Hit(plant, 0, RegrowTime(plant, PlantRegrowTime));
            GiveHarvest(session, plant.Yield, 1);
            RollDrops(session, rich: true, tool);
            LootPinatas.SpawnChunk(session, target + new Vector3(0f, 0.5f, 0f), plant.Yield);
            Despawn(plant);
            log.Debug($"{session.CharacterName} gathered plant {plant.Key:X} (item {plant.Yield}).");
            return true;
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<WorldSession, DateTime> lastWrongTool = new();

        /// <summary>
        /// The client lets any gathering tool target any node (trees and plants are both plain props to it), so a swing
        /// with the wrong tool plays but gathers nothing: say why, at most every few seconds (held swings repeat).
        /// </summary>
        private static void WrongTool(WorldSession session, ulong targetGuid, NodeType hit, string message)
        {
            if (!IsNode(targetGuid))
                return;
            Node node;
            lock (sync)
                node = Find(targetGuid & ~GuidBase);
            // Decoration (a set's rock or bush) can't be gathered with any tool.
            if (node == null || node.Type != hit && node.Type != NodeType.Decor)
                return;
            DateTime now = DateTime.UtcNow;
            if (lastWrongTool.TryGetValue(session, out DateTime last) && now - last < TimeSpan.FromSeconds(5))
                return;
            lastWrongTool[session] = now;
            // The client's own harvest failure: "It is not possible to harvest that." (Harvest.Fail.Prevent).
            HarvestFail(session, HarvestPrevent);
            log.Debug($"{session.CharacterName}: {message}");
        }

        /// <summary>Harvest result reasons (client FUN_142d7aca0): 2/5 tool rating too low, 3 not possible, 4 several,
        /// 7 a pulverizer-only resource ([*resource*] can't be harvested), 8 not allowed here.</summary>
        public const uint HarvestTier = 2, HarvestPrevent = 3, HarvestMultiple = 4, HarvestPulverizer = 7, HarvestNotAllowed = 8;

        /// <summary>
        /// A failed harvest the client's way: Abilities EffectEnd (0x27 sub 0x0D) for the player with fraction 0 and a
        /// reason at +0x50 (resource at +0x54 for reason 7). The client (FUN_142d7aca0, when +0x38 is the local player)
        /// stops the swing and shows Harvest.Fail.* (Tier / Prevent / Multiple / IsPulverizer / NotAllowed).
        /// Body as Buffs' EffectEnd: u64 key, u32, u32 effect, u32, u64 player, u64 target, u32, f32 fraction, u32
        /// reason, u32 resource, u32.
        /// </summary>
        public static void HarvestFail(WorldSession session, uint reason, uint resource = 0)
        {
            var output = new List<byte>();
            var w = new LandmarkEmulator.Shared.Network.GamePacketWriter(output);
            w.Write((ushort)0x0D);
            w.Write((byte)0);
            w.Write(0u);
            w.Write(session.PlayerGuid);
            w.Write(0u);
            w.Write(0u);
            w.Write(0u);
            w.Write(session.PlayerGuid);
            w.Write(session.PlayerGuid);
            w.Write(0u);
            w.Write(0f);
            w.Write(reason);
            // The client reads the resource and the last u32 only when the reason isn't 1 (leftover bytes drop the
            // packet; trace/0695/audit-wire-d.md) [P].
            if (reason != 1)
            {
                w.Write(resource);
                w.Write(0u);
            }
            session.EnqueueMessage(Network.Message.ZoneMessageOpcode.AbilityEffectEnd, output.ToArray());
        }

        /// <summary>Is this guid one of the gathering nodes (trees, plants) the server spawns?</summary>
        public static bool IsNode(ulong guid) => (guid & 0xFF00_0000_0000_0000) == GuidBase;

        /// <summary>
        /// The node a swing hits: the entity the cast names (under the crosshair), or without one the standing node of
        /// that type nearest the aim point. It must be within reach of the player.
        /// </summary>
        private static Node Target(WorldSession session, NodeType type, ulong targetGuid, Vector3 target)
        {
            Node node;
            lock (sync)
            {
                // Before a landscape's first population is rolled, nothing is shown there and nothing can be gathered.
                if (!NodesReady(session.ZoneId))
                    return null;
                if (IsNode(targetGuid))
                    node = Find(targetGuid & ~GuidBase);
                else if (targetGuid != 0)
                    node = null;   // the swing named something else (a prop, the Spire, a creature): not the tree beside it
                else
                    node = Nearby(session.ZoneId, target, AimTolerance).Where(n => n.Type == type && n.Standing)
                        .OrderBy(n => HorizontalDistance(Base(n), target)).FirstOrDefault();
            }
            if (node == null || node.Type != type || !node.Standing || HorizontalDistance(Base(node), session.Position) > Reach)
            {
                log.Debug($"{session.CharacterName} swung at {targetGuid:X} / {target} with no {type.ToString().ToLowerInvariant()} in reach.");
                return null;
            }
            return node;
        }

        /// <summary>
        /// How long a gathered node stays gone under the regrow-in-place model (ProceduralRespawn inplace): a set node's
        /// MIN..MAX_RESPAWN_MINUTES (12-18 on most sets; "within about 20 minutes", Domino 19 Jun 2015, forum 55352),
        /// otherwise the old list's fixed time. The min / max model (default) uses the same delays for its respawns
        /// elsewhere (<see cref="RespawnDelay"/>).
        /// </summary>
        private static TimeSpan RegrowTime(Node node, TimeSpan fallback)
        {
            if (node.Set == null)
                return fallback;
            lock (sync)
                return node.Set.Respawn(random);
        }

        /// <summary>
        /// Counts a hit: a tree under the health model loses <paramref name="damage"/>, anything else counts one of its
        /// MaxHits. True (and the node is gone until it regrows) when it was the last one.
        /// </summary>
        private static bool Hit(Node node, uint damage, TimeSpan regrow)
        {
            lock (sync)
            {
                if (HasLife(node))
                {
                    node.Health = damage >= node.Health ? 0 : node.Health - damage;
                    if (node.Health > 0)
                        return false;
                }
                else if (++node.Hits < node.MaxHits)
                    return false;
                ResetDamage(node);
                if (Respawn == RespawnModel.MinMax)
                {
                    Felled(node, DateTime.UtcNow);   // gone; maybe another of its kind elsewhere (Gathering.Respawn.cs)
                    return true;
                }
                node.RegrowsAt = DateTime.UtcNow + regrow;
                gathered[node.Key] = node.RegrowsAt;
                dirty = true;
                return true;
            }
        }

        /// <summary>
        /// A build site was placed (or moved) over this box (global dm): the trees, plants and decoration standing in it go,
        /// as on live, where placing a site cleared them (trace/0695/claim-trees.md) and a new Pioneer site's slab had nothing
        /// on it, no rocks or bushes either (trace/0695/pioneer-slab-live.md [P]). A tree or plant is taken out like a felled
        /// one — under the min / max model a free spot, its kind respawning elsewhere when it drops below its minimum;
        /// regrowing in place it stays gone (generation and respawns keep out of sites, <see cref="Allowed"/>). Decoration
        /// (a placement set's rocks, bushes and flowers, setting ProceduralPlacementDecor) is never gathered or respawned,
        /// so it is only hidden until a restart regenerates its region, which keeps it out of the site (<see cref="Allowed"/>);
        /// it takes no pool's place. Every player who has the node's region is told. Until 2026-10-31 the site test ran only
        /// when a region was first generated, so trees standing before a site was placed stayed in it until a restart; until
        /// 2026-11-01 decoration was skipped. When the site goes, its spots come back through respawns or the next start.
        /// </summary>
        public static void SitePlaced(uint zone, (double X, double Y, double Z) min, (double X, double Y, double Z) max)
        {
            var gone = new List<Node>();
            DateTime now = DateTime.UtcNow;
            lock (sync)
                foreach (Node[] nodes in regions.Values)
                    foreach (Node node in nodes)
                    {
                        if (node.Zone != zone || !node.Standing)
                            continue;
                        Vector3 at = Base(node);
                        var global = TerrainEdits.ToGlobal(at);
                        // The trunk's footprint (Models.txt PATH_BLOCK_RADIUS × scale, dm) against the box on x / z.
                        double trunk = (ModelFootprints.Of(node.Spawn.ModelId)?.Radius ?? 0.5f) * node.Spawn.Scale.X * 10;
                        if (global.X < min.X - trunk || global.X > max.X + trunk || global.Z < min.Z - trunk || global.Z > max.Z + trunk
                            || global.Y < min.Y - 10 || global.Y > max.Y)
                            continue;
                        ResetDamage(node);
                        if (node.Type != NodeType.Decor && Respawn == RespawnModel.MinMax)
                            Felled(node, now);
                        else
                            node.RegrowsAt = DateTime.MaxValue;   // not saved: gone until a restart regenerates the region
                        gone.Add(node);
                    }
            foreach (Node node in gone)
                Despawn(node);
            if (gone.Count > 0)
                log.Info($"A build site on landscape {zone} took {gone.Count} tree(s) / plant(s) / decoration out.");
        }

        /// <summary>
        /// Removes a felled tree or picked plant from every player who has its region on screen.
        /// </summary>
        private static void Despawn(Node node)
        {
            ForgetTargets(node);
            long region = RegionKey(node.Zone, Base(node));
            foreach (WorldSession other in PlayerManager.Snapshot())
                lock (sync)
                    if (sent.TryGetValue(other, out var have) && have.Contains(region))
                        other.EnqueueMessage(new PlayerUpdateRemovePlayer { Guid = node.Guid });
        }

        /// <summary>Guid of the nearest standing tree within <paramref name="range"/> metres, or 0 (dev/testing).</summary>
        public static ulong NearestStandingGuid(Vector3 position, float range = 64f, uint zone = 0)
        {
            lock (sync)
                return Nearby(ZoneOrHome(zone), position, range).Where(n => n.Standing && n.Type == NodeType.Tree)
                    .OrderBy(n => HorizontalDistance(Base(n), position)).Select(n => n.Guid).FirstOrDefault();
        }

        /// <summary>Base of the nearest standing tree (or plant) within <paramref name="range"/> metres (dev/testing).</summary>
        public static Vector3? NearestStanding(Vector3 position, bool plant = false, float range = 64f, uint zone = 0)
        {
            NodeType type = plant ? NodeType.Plant : NodeType.Tree;
            lock (sync)
                return Nearby(ZoneOrHome(zone), position, range).Where(n => n.Standing && n.Type == type)
                    .OrderBy(n => HorizontalDistance(Base(n), position)).Select(n => (Vector3?)Base(n)).FirstOrDefault();
        }

        /// <summary>
        /// A point moved out of any standing tree trunk: arrivals (login, teleports) can land inside a tree that was
        /// generated after the position was saved, and the player is then stuck in its collision. The trunk is the
        /// model's path-block footprint from Models.txt (<see cref="ModelFootprints"/>) times its scale; the point is
        /// pushed out horizontally and put back on the ground.
        /// </summary>
        public static Vector3 ClearOfTrees(Vector3 point, uint zone = 0)
        {
            zone = ZoneOrHome(zone);
            lock (sync)
            {
                for (int pass = 0; pass < 4; pass++)
                {
                    bool moved = false;
                    foreach (Node tree in Nearby(zone, point, 12f).Where(n => n.Standing && n.Type != NodeType.Plant))
                    {
                        if (ModelFootprints.Of(tree.Spawn.ModelId) is not (float footprint, Vector3 offset))
                            continue;
                        float scale = tree.Spawn.Scale.X;
                        Vector3 trunk = Base(tree) + offset * scale;
                        float radius = footprint * scale + 0.6f;   // + the player's own radius
                        var away = new Vector2(point.X - trunk.X, point.Z - trunk.Z);
                        float distance = away.Length();
                        if (distance >= radius)
                            continue;
                        away = distance < 0.01f ? Vector2.UnitX : away / distance;
                        log.Info($"Arrival {distance:0.0} m from tree model {tree.Spawn.ModelId} (blocks {radius:0.0} m); moved out.");
                        point = new Vector3(trunk.X + away.X * (radius + 0.5f), point.Y, trunk.Z + away.Y * (radius + 0.5f));
                        moved = true;
                    }
                    if (!moved)
                        break;
                    if (Terrain.TerrainHeights.HeightAt(point.X, point.Z, zone) is float ground)
                        point.Y = ground + 0.5f;
                }
            }
            return point;
        }

        /// <summary>
        /// Brings back trees and plants whose regrow time has passed and shows them to players nearby.
        /// </summary>
        private static void RegrowDue(DateTime now)
        {
            var regrown = new List<Node>();
            lock (sync)
            {
                foreach ((ulong key, DateTime at) in gathered.ToList())
                {
                    if (at > now)
                        continue;
                    gathered.Remove(key);
                    dirty = true;
                    if (regions.TryGetValue((long)(key >> 8), out Node[] nodes))
                        regrown.AddRange(nodes.Where(n => n.Key == key));
                }
            }
            foreach (Node node in regrown)
                foreach (WorldSession session in PlayerManager.Snapshot())
                    lock (sync)
                        if (sent.TryGetValue(session, out var have) && have.Contains(RegionKey(node.Zone, Base(node))))
                            session.EnqueueMessage(node.Spawn);
        }

        /// <summary>
        /// The trees and plants of a region (cached), generated the same way every time from its coordinates, with their
        /// saved state (regrowing, or a free spot under the min / max model). Caller holds <see cref="sync"/>.
        /// </summary>
        private static Node[] Region(uint zone, int rx, int rz)
        {
            long key = Pack(zone, rx, rz);
            if (regions.TryGetValue(key, out Node[] cached))
                return cached;
            Node[] result = Generate(zone, rx, rz, census: false);
            foreach (Node node in result)
                node.RegrowsAt = SavedState(node);
            regions[key] = result;
            return result;
        }

        /// <summary>
        /// A region's nodes from its coordinates alone, all standing. Touches no shared state (the census runs it on its
        /// own thread for a whole landscape); <paramref name="census"/>: no transient ids are taken for the throw-away
        /// nodes.
        /// </summary>
        private static Node[] Generate(uint zone, int rx, int rz, bool census)
        {
            long key = Pack(zone, rx, rz);
            var nodes = new List<Node>();
            // Seeded per landscape (for the home continent, 406/407 as before, so its saved state still matches).
            var treeRng = new Random(Shared.StableHash.Combine(rx * 73856093, rz * 19349663, (int)zone));
            var plantRng = new Random(Shared.StableHash.Combine(rx * 83492791, rz * 29765723, (int)zone + 1));
            float cell = RegionSize / CellsPerSide;
            for (int i = 0; i < CellsPerSide * CellsPerSide; i++)
            {
                // Trees sit in the middle 70% of their 8 m cell, plants near its corner, so they don't overlap.
                float x = rx * RegionSize + (i % CellsPerSide + 0.15f + 0.7f * (float)treeRng.NextDouble()) * cell;
                float z = rz * RegionSize + (i / CellsPerSide + 0.15f + 0.7f * (float)treeRng.NextDouble()) * cell;
                double roll = treeRng.NextDouble();
                int pick = treeRng.Next(1000);
                float yaw = (float)(treeRng.NextDouble() * Math.PI * 2), scale = 0.85f + 0.3f * (float)treeRng.NextDouble();
                int biome = TerrainMaterials.BiomeAt(new Vector3(x, 0f, z), zone);
                TreeKind trees = treeKinds.GetValueOrDefault(biome, fallbackTrees);
                // Biomes with placement sets get their trees from the sets (below).
                if (roll < trees.Density && !ProceduralPlacement.Covers(biome) && Ground(zone, x, z) is float y)
                {
                    uint model = trees.Models[pick % trees.Models.Length];
                    bool big = IsLargeTree(model);
                    nodes.Add(Create(zone, key, i, NodeType.Tree, model, new Vector3(x, y - 0.3f, z), yaw, scale,
                        trees.Wood, big ? 8 : 5, biome, census));
                }

                float px = rx * RegionSize + (i % CellsPerSide + 0.05f * (float)plantRng.NextDouble()) * cell;
                float pz = rz * RegionSize + (i / CellsPerSide + 0.05f * (float)plantRng.NextDouble()) * cell;
                double plantRoll = plantRng.NextDouble(), rarity = plantRng.NextDouble();
                int plantPick = plantRng.Next(1000);
                float plantYaw = (float)(plantRng.NextDouble() * Math.PI * 2);
                // No plant nodes: plants are no longer harvested and sickles have no use (21 Mar 2016, P4128); plants
                // stay the client's own decoration. The tables above are kept for reference.
                int plantBiome = TerrainMaterials.BiomeAt(new Vector3(px, 0f, pz), zone);
                if (PlantsHarvestable && plantRoll < PlantDensity && !ProceduralPlacement.Covers(plantBiome) && Ground(zone, px, pz) is float py)
                {
                    PlantKind[] kinds = plantKinds.GetValueOrDefault(plantBiome, fallbackPlants);
                    PlantKind kind = kinds[plantPick % kinds.Length];
                    (uint model, uint item) = rarity < 0.05 ? (kind.RareModel, kind.PerfectItem)
                        : rarity < 0.2 ? (kind.UncommonModel, kind.LargeItem)
                        : (kind.CommonModel, kind.Item);
                    nodes.Add(Create(zone, key, PlantIndexBase + i, NodeType.Plant, model, new Vector3(px, py - 0.1f, pz), plantYaw, 1f, item, 1,
                        plantBiome, census));
                }
            }
            AddSetNodes(zone, key, rx, rz, nodes, census);
            return nodes.ToArray();
        }

        /// <summary>First key index of the placement-set nodes of a region (0-63 old trees, 64-127 old plants).</summary>
        private const int SetIndexBase = 128;

        /// <summary>
        /// Adds a region's placement-set nodes (<see cref="ProceduralPlacement.Region"/>): trees give their biome type's
        /// wood, sickle plants only while plants are harvestable, decoration only with ProceduralPlacementDecor. At most
        /// 128 per region (key indices 128-255); trees-only regions hold far fewer (about 4-10 on 64 m).
        /// </summary>
        private static void AddSetNodes(uint zone, long region, int rx, int rz, List<Node> nodes, bool census)
        {
            if (!ProceduralPlacement.Enabled || !ProceduralPlacement.Loaded)
                return;
            List<ProceduralPlacement.Placement> placed = ProceduralPlacement.Region(zone, rx * RegionSize, rz * RegionSize, RegionSize,
                PlantsHarvestable, (x, z, y) => Allowed(zone, x, y, z));
            for (int i = 0; i < placed.Count; i++)
            {
                if (i >= 256 - SetIndexBase)
                {
                    log.Debug($"Region {rx},{rz} of landscape {zone}: {placed.Count - i} placement(s) over the 128-node limit left out.");
                    break;
                }
                ProceduralPlacement.Placement p = placed[i];
                (NodeType type, uint yield, int hits) = p.Entity.Kind switch
                {
                    ProceduralPlacement.EntityKind.Tree => (NodeType.Tree, SetWood(p.Set.BiomeType),
                        p.Entity.Footprint >= LargeTreeFootprint ? 8 : 5),
                    ProceduralPlacement.EntityKind.Plant => (NodeType.Plant, SetPlantItem(p.Entity.Model), 1),
                    _ => (NodeType.Decor, 0u, int.MaxValue)
                };
                if (type == NodeType.Plant && yield == 0)
                    continue;   // a set plant with no sickle item here (Flax, Jute, Silkfall)
                ulong key = ((ulong)region << 8) | (uint)(SetIndexBase + i);
                var node = new Node
                {
                    Key = key,
                    Zone = zone,
                    Type = type,
                    Set = p.Set,
                    Entity = p.Entity,
                    Yield = yield,
                    MaxHits = hits,
                    Spawn = new PlayerUpdateAddLightweightProp
                    {
                        Guid = GuidBase | key,
                        TransientId = census ? 0u : PlayerManager.AllocateTransientId(),
                        ModelId = p.Entity.Model,
                        Position = new Vector4(p.Position, 1f),
                        Rotation = new Vector4(p.Rotation.X, p.Rotation.Y, p.Rotation.Z, p.Rotation.W),
                        Scale = new Vector4(p.Scale, p.Scale, p.Scale, 1f)
                    }
                };
                SetLife(node);
                nodes.Add(node);
            }
        }

        /// <summary>
        /// Gathering nodes of one landscape, per (placement set, set entity) — or per model for the old tree lists (Set and
        /// Entity null) — for the admin monitor: the page-28 reply (ProceduralDataMonitorPacketUpdateResponse 0x10A,
        /// <see cref="AdminMonitor"/>) and the web page. <c>Standing</c>: nodes standing now; <c>Total</c>: candidate spots
        /// (the page's "node count"); <c>Min</c> / <c>Max</c>: the per-landscape minimum and maximum; <c>Queued</c>: respawns
        /// waiting (min / max model) or nodes regrowing in place.
        /// <para>
        /// Min / max model (<see cref="Respawn"/>): from the landscape's census, i.e. the whole landscape, once it is in.
        /// Regrow-in-place: only the regions a player has caused to be generated (that model works per region, on demand);
        /// Min 0, Max the set entity's MAX_SPAWNS (10,000) or, for the old lists, the nodes generated.
        /// </para>
        /// </summary>
        public sealed record NodeCount(ProceduralPlacement.PlacementSet Set, ProceduralPlacement.SetEntity Entity, uint Model, int Standing,
            int Total, int Min, int Max, int Queued);

        public static List<NodeCount> NodeCounts(uint zone)
        {
            if (CensusCounts(zone) is List<NodeCount> fromCensus)
                return fromCensus;
            var counts = new Dictionary<(int Set, uint Entity), NodeCount>();
            DateTime now = DateTime.UtcNow;
            lock (sync)
                foreach (Node[] nodes in regions.Values)
                    foreach (Node node in nodes)
                    {
                        if (node.Zone != zone)
                            continue;
                        var key = (node.Set?.Id ?? -1, node.Entity?.Id ?? node.Spawn.ModelId);
                        counts.TryGetValue(key, out NodeCount had);
                        bool standing = node.RegrowsAt <= now;
                        int total = (had?.Total ?? 0) + 1, up = (had?.Standing ?? 0) + (standing ? 1 : 0);
                        counts[key] = new NodeCount(node.Set, node.Entity, node.Spawn.ModelId, up, total,
                            0, node.Entity?.MaxSpawns ?? total, total - up);
                    }
            return counts.Values.ToList();
        }

        /// <summary>A set tree whose blocking radius (Models.txt) is at least this is "large": 8 hits like the old list's big trees.</summary>
        private const float LargeTreeFootprint = 4.5f;

        /// <summary>
        /// The wood of a placement set's trees by biome type (wiki Gathering / Striped Wood: Plain in Jungle and Tundra,
        /// Striped in the Desert; hotfix 11 May 2016, P5415: every Old Growth tree gives Burled Wood).
        /// </summary>
        private static uint SetWood(int biomeType) => biomeType switch
        {
            1 => 73,          // Desert → Striped Wood
            4 => BurledWood,  // Old Growth Forest
            _ => PlainWood    // Tropical, Tundra
        };

        /// <summary>The plant item of a set's sickle plant model (the plant kinds above), 0 if it has none.</summary>
        private static uint SetPlantItem(uint model) =>
            new[] { Amberleaf, Cotton, Earthblood, Sunblossom, Vilebloom }.FirstOrDefault(k => k.CommonModel == model)?.Item ?? 0;

        /// <summary>
        /// The server's own refusals at a ground point: the Spire (its platform, and the 90 m Spire area that suppresses
        /// procedural props, P1830), build sites (their box, whoever may build there), graveyards.
        /// </summary>
        private static bool Allowed(uint zone, float x, float y, float z)
        {
            var at = new Vector3(x, y, z);
            if (Spire.Covers(zone, x, z) || Spire.InArea(zone, at) || Graveyards.PreventsSpawning(zone, at))
                return false;
            var global = TerrainEdits.ToGlobal(at);
            return !Claims.CoversPoint(zone, global.X, global.Y, global.Z);
        }

        /// <summary>
        /// Ground height for a node at x/z, or null where nothing should grow (sea, cliffs, build sites, a graveyard's
        /// "Prevent Spawning" area, Graveyards.PreventsSpawning).
        /// </summary>
        private static float? Ground(uint zone, float x, float z)
        {
            if (TerrainHeights.HeightAt(x, z, zone) is not float y || y < SeaLevel || TerrainHeights.Slope(x, z, 2f, zone) > MaxSlope)
                return null;
            return Allowed(zone, x, y, z) ? y : null;
        }

        /// <summary>
        /// A tree or plant node: a stable key (region << 8 | index), what it yields, how many hits it takes (a tree also its
        /// health, <see cref="SetLife"/>), and the prop packet that shows it.
        /// </summary>
        private static Node Create(uint zone, long region, int index, NodeType type, uint model, Vector3 position, float yaw, float scale,
            uint yield, int maxHits, int biome, bool census)
        {
            ulong key = ((ulong)region << 8) | (uint)index;
            var node = new Node
            {
                Key = key,
                Zone = zone,
                Type = type,
                Biome = biome,
                Yield = yield,
                MaxHits = maxHits,
                Spawn = new PlayerUpdateAddLightweightProp
                {
                    Guid = GuidBase | key,
                    TransientId = census ? 0u : PlayerManager.AllocateTransientId(),
                    ModelId = model,
                    Position = new Vector4(position, 1f),
                    Rotation = new Vector4(0f, MathF.Sin(yaw / 2), 0f, MathF.Cos(yaw / 2)),
                    Scale = new Vector4(scale, scale, scale, 1f)
                }
            };
            SetLife(node);
            return node;
        }

        /// <summary>
        /// The client's FullCharacterDataRequest for a tree or plant (it asks for every node it is shown as a lightweight
        /// prop): answered like any prop, with LightweightToFullProp, which gives the node its full form (collision,
        /// interaction) as the live server did. Before, these requests went unanswered.
        /// </summary>
        public static bool SendFull(WorldSession session, ulong guid)
        {
            // The whole top byte: creature guids (0x50.., cavern 0x51..) share bit 62 with GuidBase 0x40.., and a creature's
            // request decoded as a node made a region for "landscape" 0x100000 / 0x110000 (2026-10-15).
            if ((guid & 0xFF00_0000_0000_0000) != GuidBase)
                return false;
            Node node;
            lock (sync)
                node = Find(guid & ~GuidBase);
            if (node == null)
                return false;
            // Neutral (disposition 1), so the client's own target rule asks for the harvest slot 9 (Gathering.Health.cs).
            session.EnqueueMessage(new PlayerUpdateLightweightToFullProp { TransientId = node.Spawn.TransientId, Disposition = 1 });
            SendLifeAfterFull(session, node);
            return true;
        }

        /// <summary>
        /// The node with this key (regenerating its region if needed).
        /// </summary>
        private static Node Find(ulong key)
        {
            (uint zone, int rx, int rz) = Unpack((long)(key >> 8));
            return Region(zone, rx, rz).FirstOrDefault(n => n.Key == key);
        }

        private static bool IsLargeTree(uint model) => model is 344 or 345 or 346 or 347 or 226 or 1370;

        /// <summary>
        /// Nodes within <paramref name="range"/> metres (horizontal) of a point.
        /// </summary>
        private static IEnumerable<Node> Nearby(uint zone, Vector3 point, float range)
        {
            int r0x = (int)MathF.Floor((point.X - range) / RegionSize), r1x = (int)MathF.Floor((point.X + range) / RegionSize);
            int r0z = (int)MathF.Floor((point.Z - range) / RegionSize), r1z = (int)MathF.Floor((point.Z + range) / RegionSize);
            for (int rz = r0z; rz <= r1z; rz++)
                for (int rx = r0x; rx <= r1x; rx++)
                    foreach (Node node in Region(zone, rx, rz))
                        if (HorizontalDistance(Base(node), point) <= range)
                            yield return node;
        }

        private static Vector3 Base(Node node) => new(node.Spawn.Position.X, node.Spawn.Position.Y, node.Spawn.Position.Z);

        private static float HorizontalDistance(Vector3 a, Vector3 b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

        /// <summary>
        /// Horizontal distance from a point to the nearest edge of a region (0 inside it).
        /// </summary>
        private static float RegionDistance(int rx, int rz, Vector3 p)
        {
            float cx = Math.Clamp(p.X, rx * RegionSize, (rx + 1) * RegionSize);
            float cz = Math.Clamp(p.Z, rz * RegionSize, (rz + 1) * RegionSize);
            return MathF.Sqrt((p.X - cx) * (p.X - cx) + (p.Z - cz) * (p.Z - cz));
        }

        private static long RegionKey(uint zone, Vector3 p) => Pack(zone, (int)MathF.Floor(p.X / RegionSize), (int)MathF.Floor(p.Z / RegionSize));

        private static uint Home => TerrainStore.HomeContinent;
        private static uint ZoneOrHome(uint zone) => zone == 0 ? Home : zone;

        // Region key: the landscape in bits 32+ (0 = the home continent, so its keys, guids and saved felled trees are
        // the ones from before landscapes), region x and z in 16 bits each (an 8 km landscape is ±63 regions). The low
        // 8 bits of a node key (region << 8) are its index in the region (trees 0-63, plants 64-127).
        private static long Pack(uint zone, int rx, int rz) =>
            ((long)(zone == Home ? 0 : zone) << 32) | ((long)(ushort)(short)rx << 16) | (ushort)(short)rz;

        private static (uint, int, int) Unpack(long key)
        {
            uint code = (uint)(key >> 32);
            return (code == 0 ? Home : code, (short)(ushort)(key >> 16), (short)(ushort)key);
        }

        private static readonly object saveSync = new();
        private static long saveSequence, writtenSequence;

        /// <summary>
        /// Writes the gathered list to disk (only when something changed): regrowing nodes ("key ticks") and the min / max
        /// model's state (<see cref="RespawnLines"/>). The lines are taken under the lock; <paramref name="background"/>
        /// writes them on the thread pool, an older snapshot never overwriting a newer one. Written in place (not via a
        /// temporary file): in Docker the file is a symbolic link into the world's data folder.
        /// </summary>
        private static void Save(bool background)
        {
            if (savePath == null)
                return;
            string[] lines;
            long sequence;
            lock (sync)
            {
                lines = gathered.Select(f => $"{f.Key} {f.Value.Ticks}").Concat(RespawnLines()).ToArray();
                dirty = false;
                sequence = ++saveSequence;
            }
            if (background)
                System.Threading.Tasks.Task.Run(() => Write(lines, sequence));
            else
                Write(lines, sequence);
        }

        private static void Write(string[] lines, long sequence)
        {
            lock (saveSync)
            {
                if (sequence < writtenSequence)
                    return;
                writtenSequence = sequence;
                try
                {
                    File.WriteAllLines(savePath, lines);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    log.Warn($"Couldn't save gathered trees and plants: {e.Message}");
                }
            }
        }

        /// <summary>Writes what changed since the last save now (the world is stopping).</summary>
        public static void Flush()
        {
            if (dirty)
                Save(background: false);
        }
    }
}
