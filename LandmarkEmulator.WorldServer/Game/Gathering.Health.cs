using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Tree health and the harvest target (trace/0695/healthbar-*.md, 2026-10-31).
    /// <para>
    /// The client draws a bar over a tree with its Prop HUD indicator (HudIndicators 12, code 18) only while client
    /// requirement 13 holds: <c>TargetInjured &amp;&amp; TargetAlive &amp;&amp; IsHarvestTarget</c> for an unnamed prop [P data:
    /// RequirementExpressions 13, HUDIndicatorSettings 13^18^13 / 1^18^13]. Injured / Alive are the entity's Health
    /// resource (type 1) below 100 % and above 0 % (integer current × 100 / max, FUN_143710070 → FUN_143b8c370 on the
    /// resource container at entity +0x4268, which a ProxiedProp has like any ProxiedCharacter) [P]; IsHarvestTarget is
    /// the entity in the player's target slot 9 (TargetOfPlayer 9, FUN_143710fe0) [P]. So "you only see the health bars of
    /// trees you're currently harvesting" (patch 12 Feb 2014, P0123).
    /// </para>
    /// <para>
    /// Health: the client ships one Health row per tree size (Resources.txt 18 / 16 / 14 / 15 / 21: 350, 750, 1500,
    /// 2250, 3000, each with MAX_VALUE one higher, so a fresh tree already reads 99 % "injured") but not which tree is
    /// which size, which was server data (ProfileResourceMappings has a client loader and was never shipped) [P]. The size
    /// here comes from the model's name and footprint [I]. A swing takes the caster's Harvesting Damage (stat 12: 80 on
    /// the live Adventurer's Axe, SendSelf.bin 2017-02-20; dev Avair 2014: "each point of damage ... another hit point
    /// taken away from the resource") [P], so larger trees take more swings (P0473 "tree health rebalanced to match tree
    /// size"). Setting <c>TreeHealth</c>: <c>health</c> (default, live) or <c>hits</c> (the older fixed 5 / 8 swings).
    /// </para>
    /// <para>
    /// Slot 9: the client's SetCurrentTarget (FUN_142d4e890) asks for slot 8 for an ally, 3 for a hostile and 9 for
    /// anything else, and adopts only the slot the server's SetTarget answer carries [P]. Trees used to be allies
    /// (disposition 2 in LightweightToFullProp), so a click put them in slot 8 and the bar never came. Nodes are now sent
    /// neutral (disposition 1) and the server answers a node's target request with slot 9 whatever slot was asked (a node
    /// clicked before its full data still has the constructor's disposition 2) [I: live's way of getting trees into
    /// slot 9 is not known; slot 9 is the client's own slot for neutral entities]. The client selects only on a completed
    /// click (press and release on the same entity) and its swings set no slot [P], so the first swing at a tree that
    /// isn't the swinger's harvest target puts it there (<see cref="HarvestTarget"/>).
    /// </para>
    /// <para>
    /// Bandwidth: the Health record (one ResourcesBase SetCharacterResource, about 110 bytes) goes only to the player
    /// who swings at or targets the tree, never to everyone in range: nobody else can see the bar (requirement 13). The
    /// full-data answer (LightweightToFullProp) clears every resource on the client (FUN_143b8aef0) [P], so the
    /// harvester's tree gets its record again right after it.
    /// </para>
    /// </summary>
    public static partial class Gathering
    {
        /// <summary>How a tree is felled (setting TreeHealth).</summary>
        public enum TreeHealthModel
        {
            /// <summary>Live's: a Health pool by tree size, each swing takes the axe's damage; the client shows the bar (default).</summary>
            Health,
            /// <summary>The older fixed count: 5 swings, 8 for large trees; no bar.</summary>
            Hits
        }

        /// <summary>Setting "TreeHealth" (health | hits; default health). Docker: TREE_HEALTH.</summary>
        public static TreeHealthModel TreeHealth { get; internal set; } = TreeHealthModel.Health;

        /// <summary>
        /// Resources.txt Health rows (RESOURCE_TYPE 1) by tree size, very small to very large: row, INITIAL_VALUE,
        /// MAX_VALUE [P data]. The row id goes on the wire: the client looks its maximum up by it.
        /// </summary>
        private static readonly (uint Row, uint Initial, uint Max)[] TreeLife =
        {
            (18, 350, 351),     // "Tree - Very Small"
            (16, 750, 751),     // "Tree - Small"
            (14, 1500, 1501),   // "Tree - Medium"
            (15, 2250, 2251),   // "Tree - Large"
            (21, 3000, 3001)    // "Tree - Very Large"
        };

        /// <summary>Harvesting Damage of the live Adventurer's Axe (stat 12, SendSelf.bin) [P].</summary>
        public const uint AxeDamage = 80;

        /// <summary>
        /// Damage per find roll on trees under the health model: the old 5-hit tree's one roll per 5 Adventurer's swings
        /// (ours, OPEN-LEADS L246).
        /// </summary>
        private const uint RewardPace = 5 * AxeDamage;

        /// <summary>Each player's harvest target (target slot 9) when it is a gathering node.</summary>
        private static readonly Dictionary<WorldSession, ulong> harvestTargets = new();

        /// <summary>The client's target slot whose entity is its harvest target (requirement IsHarvestTarget, TargetOfPlayer 9).</summary>
        public const uint HarvestTargetSlot = 9;

        /// <summary>Reads TreeHealth (WorldServer.json or the environment).</summary>
        private static void LoadHealthSettings(IConfiguration config)
        {
            string model = (config?.GetValue("TreeHealth", "health") ?? "health").Trim();
            TreeHealth = model.Equals("hits", StringComparison.OrdinalIgnoreCase) ? TreeHealthModel.Hits : TreeHealthModel.Health;
            if (!model.Equals("hits", StringComparison.OrdinalIgnoreCase) && !model.Equals("health", StringComparison.OrdinalIgnoreCase))
                log.Warn($"TreeHealth \"{model}\" is not health or hits; health is used.");
        }

        /// <summary>
        /// A tree model's size, 0 (very small) to 4 (very large) [I]: the size word in its Models.txt name (DeadLog / Sapling,
        /// Small / Short, Medium, Large / Tall, Huge), a "Large" model with a footprint of 10 m or more (the Feerrott
        /// banyans) counting as very large; without a size word, its footprint (PATH_BLOCK_RADIUS).
        /// </summary>
        internal static int TreeSize(uint model)
        {
            string name = ModelFootprints.NameOf(model) ?? "";
            float radius = ModelFootprints.Of(model)?.Radius ?? 0f;
            bool Has(string word) => name.Contains(word, StringComparison.OrdinalIgnoreCase);
            if (Has("Huge") || Has("Giant"))
                return 4;
            if (Has("DeadLog") || Has("Sapling"))
                return 0;
            if (Has("Large") || Has("Tall"))
                return radius >= 10f ? 4 : 3;
            if (Has("Medium"))
                return 2;
            if (Has("Small") || Has("Short"))
                return 1;
            return radius switch { < 1f => 0, < 2f => 1, < 4.5f => 2, < 6.5f => 3, _ => 4 };
        }

        /// <summary>
        /// Gives a new tree node its Health row and full (initial) health, and its profile: ProfileDefinitions 200-214
        /// "Tree: size: wood" = 200 + 3 × size + wood (0 Plain, 1 Burled, 2 Striped), which the axes' target rule tests
        /// (<see cref="Abilities.TreeTargetRequirement"/>; trace/0695/axe-a-client.md). The size is ours (L241).
        /// </summary>
        private static void SetLife(Node node)
        {
            if (node.Type != NodeType.Tree)
                return;
            int size = TreeSize(node.Spawn.ModelId);
            (node.LifeRow, node.InitialHealth, _) = TreeLife[size];
            node.Health = node.InitialHealth;
            node.Spawn.Profile = TreeProfileBase + 3u * (uint)size + node.Yield switch { BurledWood => 1u, 73 => 2u, _ => 0u };
        }

        /// <summary>ProfileDefinitions 200 "Tree: Very Small: Plain Wood", the first of the 15 tree profiles.</summary>
        private const uint TreeProfileBase = 200;

        /// <summary>A felled or respawned node stands again undamaged.</summary>
        private static void ResetDamage(Node node)
        {
            node.Hits = 0;
            node.Health = node.InitialHealth;
        }

        /// <summary>
        /// One axe swing's damage to a tree: the axe's Harvesting Damage (<see cref="Abilities.ToolHarvestDamage"/>) with the
        /// player's consumables and buffs on stat 12 (Potion of Furious Harvesting +25 %), halved by a broken axe.
        /// </summary>
        private static uint SwingDamage(WorldSession session, uint tool)
        {
            float damage = Abilities.ToolHarvestDamage(tool);
            damage = Buffs.Apply(session, 12, Consumables.Apply(session, 12, damage)) * Durability.ToolStrength(session);
            return (uint)Math.Max(1f, MathF.Round(damage));
        }

        /// <summary>Whether this node shows a health bar to its harvester (a tree, under the health model).</summary>
        private static bool HasLife(Node node) => TreeHealth == TreeHealthModel.Health && node.Type == NodeType.Tree && node.LifeRow != 0;

        /// <summary>
        /// The node's Health record on one player's client. Never below 1 % of the row's maximum while the tree stands: the
        /// client's TargetAlive is the integer current × 100 / max &gt; 0, so a Large tree's last 10 health (10 × 100 / 2251 = 0)
        /// would hide the bar a swing early. A felled tree is removed instead.
        /// </summary>
        private static void SendLife(WorldSession session, Node node)
        {
            if (!HasLife(node) || node.Health == 0)
                return;
            uint max = node.InitialHealth + 1;   // every tree row's MAX_VALUE is INITIAL_VALUE + 1
            CharacterResources.SendFor(session, node.Guid, node.LifeRow, CharacterResources.LifeType,
                Math.Max(node.Health, (max + 99) / 100));
        }

        /// <summary>
        /// The client asked to target a gathering node (TargetBase RequestSetTarget). Remembers it as the player's harvest
        /// target and sends its Health first, so the bar shows as soon as the client adopts the target. True when the guid is
        /// a node: the answer then puts it in slot 9, whatever slot was asked.
        /// </summary>
        public static bool Targeted(WorldSession session, ulong guid)
        {
            if (!IsNode(guid))
                return false;
            Node node;
            lock (sync)
            {
                node = Find(guid & ~GuidBase);
                if (node == null)
                    return false;
                harvestTargets[session] = guid;
            }
            SendLife(session, node);
            return true;
        }

        /// <summary>The player's harvest target slot was cleared (RequestRemoveTarget 9).</summary>
        public static void Untargeted(WorldSession session)
        {
            lock (sync)
                harvestTargets.Remove(session);
        }

        /// <summary>
        /// A swing at a tree that isn't the player's harvest target makes it one: the client selects an entity only on a
        /// completed click on it (FUN_142d0f610: press and release on the same entity) and its swings never set a target
        /// slot [P], so a held-button chop, or the next tree after one falls, had no bar. TargetBase SetTarget {u16 6,
        /// u64 guid, u32 9, bool ok}, which the client adopts as sent (FUN_1431e6b40) [P]: 17 bytes once per tree; the
        /// swing's health record follows it.
        /// </summary>
        private static void HarvestTarget(WorldSession session, Node node)
        {
            lock (sync)
            {
                if (IsHarvestTargetOf(session, node))
                    return;
                harvestTargets[session] = node.Guid;
            }
            session.CurrentTarget = node.Guid;
            session.TargetSlots[HarvestTargetSlot] = node.Guid;
            session.EnqueueMessage(ZoneMessageOpcode.TargetBase, SetTargetBody(node.Guid, HarvestTargetSlot));
        }

        /// <summary>TargetBase sub 6 SetTarget {u16 6, u64 target, u32 slot, bool ok}: the client adopts the slot (FUN_1431e6b40).</summary>
        public static byte[] SetTargetBody(ulong target, uint slot)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.WriteUShort(6);
            writer.Write(target);
            writer.Write(slot);
            writer.Write(true);
            return output.ToArray();
        }

        /// <summary>Whether this player's harvest target is the node (caller holds <see cref="sync"/>).</summary>
        private static bool IsHarvestTargetOf(WorldSession session, Node node)
            => harvestTargets.TryGetValue(session, out ulong guid) && guid == node.Guid;

        /// <summary>After a swing that left the tree standing: its new health to the swinger and to anyone else targeting it.</summary>
        private static void SendLifeAfterHit(WorldSession swinger, Node node)
        {
            if (!HasLife(node))
                return;
            var others = new List<WorldSession>();
            lock (sync)
                foreach ((WorldSession other, ulong guid) in harvestTargets)
                    if (guid == node.Guid && other != swinger)
                        others.Add(other);
            SendLife(swinger, node);
            foreach (WorldSession other in others)
                SendLife(other, node);
        }

        /// <summary>
        /// After a node's full data (which clears its resources on the client): its health again when it is this player's
        /// harvest target. Nobody else can see the bar; targeting the tree later sends it (<see cref="Targeted"/>).
        /// </summary>
        private static void SendLifeAfterFull(WorldSession session, Node node)
        {
            bool send;
            lock (sync)
                send = HasLife(node) && IsHarvestTargetOf(session, node);
            if (send)
                SendLife(session, node);
        }

        /// <summary>A felled node is nobody's harvest target any more.</summary>
        private static void ForgetTargets(Node node)
        {
            lock (sync)
                foreach (WorldSession session in new List<WorldSession>(harvestTargets.Keys))
                    if (harvestTargets[session] == node.Guid)
                        harvestTargets.Remove(session);
        }
    }
}
