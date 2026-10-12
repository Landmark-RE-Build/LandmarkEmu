using LandmarkEmulator.WorldServer.Game.Terrain;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model;
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
    /// outside build sites. Players are sent only the regions around them; a gathered node is removed for everyone in
    /// range and grows back later.
    /// </summary>
    public static class Gathering
    {
        /// <summary>Party members within this many metres share what a member gathers (wiki Gathering: "everyone who is
        /// harvesting within 200m of each other will share the materials being harvested").</summary>
        public const float PartyShareRange = 200f;

        /// <summary>
        /// Drops while gathering (wiki Gathering / Lumens / Recipes / Essence: "the gathering process also has a chance to
        /// drop Essences, recipes, and Lumens ... more likely when mining a vein of ore or uncommon stone, or while
        /// chopping trees"). The live rates were server data; these are ours. <paramref name="rich"/>: a vein, a tree or a
        /// plant (plain digging only rarely drops Lumens).
        /// </summary>
        public static void RollDrops(WorldSession session, bool rich, uint tool = 0)
        {
            // Charmed tools: "a bit of luck" — finds come twice as often.
            double roll = random.NextDouble() / (CharmedTools.Contains(tool) ? 2.0 : 1.0);
            if (!rich)
            {
                if (roll < PlainLumenChance)
                    GiveHarvest(session, Health.LumenItem, 1);
                return;
            }
            if (roll < RichLumenChance)
                GiveHarvest(session, Health.LumenItem, (uint)random.Next(1, 4));
            else if (roll < RichLumenChance + EssenceChance)
                GiveHarvest(session, AdventureEssence, 1);
            else if (roll < RichLumenChance + EssenceChance + RecipeChance
                     && RecipeBook.RandomUnknownRecipeItem(session, random) is uint recipe and not 0)
            {
                session.GiveItem(recipe, 1);
                log.Info($"{session.CharacterName} found recipe item {recipe} while gathering.");
            }
        }

        /// <summary>Charmed Pick / Charmed Axe (wiki: "chance to find recipes, special resources").</summary>
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
        private const float ViewRange = 160f;              // regions this close to a player are sent
        private const float DropRange = 224f;              // and removed again past this (hysteresis)
        private const float SeaLevel = 1002f;              // local metres; the ocean biome lies below ~1000
        private const float MaxSlope = 0.7f;
        private const float Reach = 8f;                    // player to node, horizontal metres
        private const float AimTolerance = 3f;             // swing target to node, horizontal metres
        private static readonly TimeSpan TreeRegrowTime = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan PlantRegrowTime = TimeSpan.FromMinutes(20);
        private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);
        private const ulong GuidBase = 0x4000_0000_0000_0000;

        private const uint PlainWood = 71, BurledWood = 72, TreeFiber = 921, TreeSap = 922;

        private enum NodeType { Tree, Plant }

        private sealed class Node
        {
            public ulong Key;
            /// <summary>The landscape the node grows on.</summary>
            public uint Zone;
            public ulong Guid => GuidBase | Key;
            public NodeType Type;
            public PlayerUpdateAddLightweightProp Spawn;
            public uint Yield;          // wood item for trees, plant item for plants
            public int MaxHits;
            public int Hits;
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
            [50] = new TreeKind { Models = new uint[] { 338, 342, 343, 345, 346, 347, 348, 349 }, Density = 0.16f, Wood = PlainWood },
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

        private static readonly object sync = new();
        private static readonly Dictionary<long, Node[]> regions = new();
        private static readonly Dictionary<ulong, DateTime> gathered = new();
        private static readonly Dictionary<WorldSession, HashSet<long>> sent = new();
        private static readonly Dictionary<WorldSession, DateTime> lastUpdate = new();
        private static readonly Random random = new();
        private static string savePath;
        private static bool dirty;

        /// <summary>
        /// Loads which trees and plants are gathered and when they regrow (one "key ticks" line each).
        /// </summary>
        public static void Load(string path)
        {
            savePath = path;
            if (!File.Exists(path))
                return;
            foreach (string line in File.ReadLines(path))
            {
                string[] f = line.Split(' ');
                if (f.Length == 2 && ulong.TryParse(f[0], out ulong key) && long.TryParse(f[1], out long ticks)
                    && ticks > DateTime.UtcNow.Ticks)
                    gathered[key] = new DateTime(ticks, DateTimeKind.Utc);
            }
            log.Info($"Gathering: {gathered.Count} tree(s)/plant(s) still regrowing.");
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
                TerrainHeights.Trim();   // drop idle landscapes' height grids (about 32 MB each)
            }
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
            if (dirty)
                Save();
        }

        /// <summary>
        /// Drops what was sent to a player who left.
        /// </summary>
        public static void Forget(WorldSession session)
        {
            lock (sync)
            {
                sent.Remove(session);
                lastUpdate.Remove(session);
            }
        }

        /// <summary>
        /// An axe swing (<see cref="Abilities.LoggingAbilities"/>). Each hit gives wood (and sometimes Tree Fiber,
        /// "found on all trees"); the last fells the tree.
        /// </summary>
        public static bool Chop(WorldSession session, ulong targetGuid, Vector3 target, uint tool = 0)
        {
            Node tree = Target(session, NodeType.Tree, targetGuid, target);
            if (tree == null)
            {
                WrongTool(session, targetGuid, NodeType.Plant, "That plant needs a sickle.");
                return false;
            }
            bool felled = Hit(tree, TreeRegrowTime);
            // The Goliath Axe "grants more resources per swing, but swings slower".
            uint wood = (uint)(random.Next(2, 5) * (tool == 3261 ? 1.5 : 1.0) * ToolYieldBonus(tool));
            GiveHarvest(session, tree.Yield, wood);
            Achievements.Harvested(session, tree.Yield, wood);
            LootPinatas.SpawnChunk(session, target + new Vector3(0f, 1f, 0f), tree.Yield);
            if (random.NextDouble() < 0.3)
                GiveHarvest(session, TreeFiber, (uint)random.Next(1, 3));
            if (!felled)
            {
                log.Debug($"{session.CharacterName} hit tree {tree.Key:X} ({tree.Hits}/{tree.MaxHits}).");
                return true;
            }
            if (random.NextDouble() < 0.15)
                GiveHarvest(session, TreeSap, 1);
            RollDrops(session, rich: true, tool);
            AfterUse(session, tool);
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
            Hit(plant, PlantRegrowTime);
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
            if (node == null || node.Type != hit)
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
            w.Write(resource);
            w.Write(0u);
            session.EnqueueMessage(Network.Message.ZoneMessageOpcode.AbilityEffectEnd, output.ToArray());
        }

        /// <summary>Is this guid one of the gathering nodes (trees, plants) the server spawns?</summary>
        public static bool IsNode(ulong guid) => guid >> 62 == 1;

        /// <summary>
        /// The node a swing hits: the entity the cast names (under the crosshair), or without one the standing node of
        /// that type nearest the aim point. It must be within reach of the player.
        /// </summary>
        private static Node Target(WorldSession session, NodeType type, ulong targetGuid, Vector3 target)
        {
            Node node;
            lock (sync)
            {
                if (targetGuid >> 62 == 1)
                    node = Find(targetGuid & ~GuidBase);
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

        /// <summary>Counts a hit; true (and the node is gone until it regrows) when it was the last one.</summary>
        private static bool Hit(Node node, TimeSpan regrow)
        {
            lock (sync)
            {
                node.Hits++;
                if (node.Hits < node.MaxHits)
                    return false;
                node.Hits = 0;
                node.RegrowsAt = DateTime.UtcNow + regrow;
                gathered[node.Key] = node.RegrowsAt;
                dirty = true;
                return true;
            }
        }

        /// <summary>
        /// Removes a felled tree or picked plant from every player who has its region on screen.
        /// </summary>
        private static void Despawn(Node node)
        {
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
                    foreach (Node tree in Nearby(zone, point, 12f).Where(n => n.Standing && n.Type == NodeType.Tree))
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

        /// <summary>Depth below the ground (m) at which surface objects are dropped, and above which they return.</summary>
        private const float UndergroundDropDepth = 60f, UndergroundShowDepth = 40f;

        /// <summary>
        /// Interest management: sends a player the trees and plants of regions within <see cref="ViewRange"/> (skipping ones
        /// still regrowing) and removes regions beyond <see cref="DropRange"/>.
        /// </summary>
        private static void Stream(WorldSession session)
        {
            uint zone = session.ZoneId;
            if (DevCommands.NoStreaming || !TerrainHeights.Has(zone))
                return;   // no baked heights for this landscape: nothing can be placed on its ground
            if (session.IsArriving)
                return;
            Vector3 position = session.Position;
            var spawns = new List<PlayerUpdateAddLightweightProp>();
            var removals = new List<ulong>();
            lock (sync)
            {
                if (!sent.TryGetValue(session, out var have))
                    sent[session] = have = new HashSet<long>();
                // Surface objects aren't sent to players deep underground (caves), and are dropped once they get
                // there; the two depths differ so a player near the threshold doesn't flicker between them.
                float depth = Terrain.TerrainHeights.DepthBelowGround(position, zone);
                bool hideSurface = depth > UndergroundDropDepth, showSurface = depth < UndergroundShowDepth;
                int r0x = (int)MathF.Floor((position.X - ViewRange) / RegionSize), r1x = (int)MathF.Floor((position.X + ViewRange) / RegionSize);
                int r0z = (int)MathF.Floor((position.Z - ViewRange) / RegionSize), r1z = (int)MathF.Floor((position.Z + ViewRange) / RegionSize);
                for (int rz = r0z; rz <= r1z; rz++)
                    for (int rx = r0x; rx <= r1x; rx++)
                    {
                        long key = Pack(zone, rx, rz);
                        if (!showSurface || have.Contains(key) || RegionDistance(rx, rz, position) > ViewRange)
                            continue;
                        have.Add(key);
                        spawns.AddRange(Region(zone, rx, rz).Where(n => n.Standing).Select(n => n.Spawn));
                    }
                foreach (long key in have.ToList())
                {
                    (uint keyZone, int rx, int rz) = Unpack(key);
                    if (keyZone == zone && !hideSurface && RegionDistance(rx, rz, position) <= DropRange)
                        continue;
                    have.Remove(key);
                    if (keyZone == zone)
                        removals.AddRange(Region(keyZone, rx, rz).Where(n => n.Standing).Select(n => n.Guid));
                }
            }
            foreach (ulong guid in removals)
                session.EnqueueMessage(new PlayerUpdateRemovePlayer { Guid = guid });
            foreach (PlayerUpdateAddLightweightProp spawn in spawns)
                session.EnqueueMessage(spawn);
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

        /// <summary>The trees and plants of a region, generated the same way every time from its coordinates.</summary>
        private static Node[] Region(uint zone, int rx, int rz)
        {
            long key = Pack(zone, rx, rz);
            if (regions.TryGetValue(key, out Node[] cached))
                return cached;

            var nodes = new List<Node>();
            // Seeded per landscape (for the home continent, 406/407 as before, so its saved state still matches).
            var treeRng = new Random(HashCode.Combine(rx * 73856093, rz * 19349663, (int)zone));
            var plantRng = new Random(HashCode.Combine(rx * 83492791, rz * 29765723, (int)zone + 1));
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
                if (roll < trees.Density && Ground(zone, x, z) is float y)
                {
                    uint model = trees.Models[pick % trees.Models.Length];
                    bool big = IsLargeTree(model);
                    nodes.Add(Create(zone, key, i, NodeType.Tree, model, new Vector3(x, y - 0.3f, z), yaw, scale,
                        big && trees.Wood == PlainWood ? BurledWood : trees.Wood, big ? 8 : 5));
                }

                float px = rx * RegionSize + (i % CellsPerSide + 0.05f * (float)plantRng.NextDouble()) * cell;
                float pz = rz * RegionSize + (i / CellsPerSide + 0.05f * (float)plantRng.NextDouble()) * cell;
                double plantRoll = plantRng.NextDouble(), rarity = plantRng.NextDouble();
                int plantPick = plantRng.Next(1000);
                float plantYaw = (float)(plantRng.NextDouble() * Math.PI * 2);
                if (plantRoll < PlantDensity && Ground(zone, px, pz) is float py)
                {
                    PlantKind[] kinds = plantKinds.GetValueOrDefault(TerrainMaterials.BiomeAt(new Vector3(px, 0f, pz), zone), fallbackPlants);
                    PlantKind kind = kinds[plantPick % kinds.Length];
                    (uint model, uint item) = rarity < 0.05 ? (kind.RareModel, kind.PerfectItem)
                        : rarity < 0.2 ? (kind.UncommonModel, kind.LargeItem)
                        : (kind.CommonModel, kind.Item);
                    nodes.Add(Create(zone, key, PlantIndexBase + i, NodeType.Plant, model, new Vector3(px, py - 0.1f, pz), plantYaw, 1f, item, 1));
                }
            }
            Node[] result = nodes.ToArray();
            regions[key] = result;
            return result;
        }

        /// <summary>Ground height for a node at x/z, or null where nothing should grow (sea, cliffs, build sites).</summary>
        private static float? Ground(uint zone, float x, float z)
        {
            if (TerrainHeights.HeightAt(x, z, zone) is not float y || y < SeaLevel || TerrainHeights.Slope(x, z, 2f, zone) > MaxSlope)
                return null;
            if (Spire.Covers(zone, x, z))
                return null;
            var global = TerrainEdits.ToGlobal(new Vector3(x, y, z));
            return Claims.MayEdit(0, global.X, global.Y, global.Z, zone) ? y : null;
        }

        /// <summary>
        /// A tree or plant node: a stable key (region << 8 | index), what it yields, how many hits it takes, and the prop
        /// packet that shows it.
        /// </summary>
        private static Node Create(uint zone, long region, int index, NodeType type, uint model, Vector3 position, float yaw, float scale,
            uint yield, int maxHits)
        {
            ulong key = ((ulong)region << 8) | (uint)index;
            return new Node
            {
                Key = key,
                Zone = zone,
                Type = type,
                Yield = yield,
                MaxHits = maxHits,
                RegrowsAt = gathered.GetValueOrDefault(key),
                Spawn = new PlayerUpdateAddLightweightProp
                {
                    Guid = GuidBase | key,
                    TransientId = PlayerManager.AllocateTransientId(),
                    ModelId = model,
                    Position = new Vector4(position, 1f),
                    Rotation = new Vector4(0f, MathF.Sin(yaw / 2), 0f, MathF.Cos(yaw / 2)),
                    Scale = new Vector4(scale, scale, scale, 1f)
                }
            };
        }

        /// <summary>
        /// The client's FullCharacterDataRequest for a tree or plant (it asks for every node it is shown as a lightweight
        /// prop): answered like any prop, with LightweightToFullProp, which gives the node its full form (collision,
        /// interaction) as the live server did. Before, these requests went unanswered.
        /// </summary>
        public static bool SendFull(WorldSession session, ulong guid)
        {
            if ((guid & GuidBase) == 0)
                return false;
            Node node;
            lock (sync)
                node = Find(guid & ~GuidBase);
            if (node == null)
                return false;
            session.EnqueueMessage(new PlayerUpdateLightweightToFullProp { TransientId = node.Spawn.TransientId });
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

        /// <summary>
        /// Writes the gathered list to disk (only when something changed).
        /// </summary>
        private static void Save()
        {
            if (savePath == null)
                return;
            string[] lines;
            lock (sync)
            {
                lines = gathered.Select(f => $"{f.Key} {f.Value.Ticks}").ToArray();
                dirty = false;
            }
            try
            {
                File.WriteAllLines(savePath, lines);
            }
            catch (IOException e)
            {
                log.Warn($"Couldn't save gathered trees and plants: {e.Message}");
            }
        }
    }
}
