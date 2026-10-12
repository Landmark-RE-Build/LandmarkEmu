using LandmarkEmulator.Shared.GameTable;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
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
    /// The Spire's Local Travel (wiki "Spire": "Local Travel: this option allows you to travel deep into caves … a map
    /// will open … click on the spire icon"): the landscape's warp network. The client's ControllerLocalTravel (camera
    /// mode 0x1E, Lua Teleport:ActivateLocalTravelMap from the Spire menu, hint 6 "Click a HUB or Portal Crystal
    /// destination") shows the network's nodes; picking one opens the Confirm Travel window (Teleport.Confirmation:
    /// node name and cost) and confirming asks the server to warp.
    /// <para>
    /// Wire (opcode bytes 13 01, then u32 sub):
    /// <list type="bullet">
    /// <item>C→S sub 4 WarpNetworkClientRequest (FUN_1434545a0 / FUN_143454620): {u32 type, u32 zone (the landscape,
    /// client game +0x378d0), u32 world (the server, +0x378cc), u64 from node, u64 to node} (seen: 1, 416, 2 at login on
    /// landscape 416 of world 2). Type 1 at ClientFinishedLoading (and Lua UpdateZoneNetwork): send the network;
    /// type 2 from Teleport.TravelConfirmation / Lua WarpToNode: warp from → to.</item>
    /// <item>S→C sub 2 WarpNetworkZoneNetwork (reader FUN_1434504c0, handler FUN_1434530c0): {u32 world, u32 zone,
    /// u32 n layers × {u32 layer id, u32 n nodes × {u64 key, u64 node id, u32 type (1 = hub), u32 biome, u64 cell,
    /// f32 x, y, z (local metres), u32}, u32 layer id}, u32}. The client files it by (world, zone) and looks it up by
    /// its own game +0x378cc / +0x378d0 (FUN_143454ec0). The Local Travel camera shows one layer: the tier of the cave
    /// tile the player stands in (FUN_142e03940 → FUN_143f34cb0; 0 on the surface), switched with Lua
    /// Teleport.SetCurrentLayer; while it is open the warp manager gives every node of that layer a HUD marker
    /// (FUN_1433905f0, indicator category 0x13). Node ids are entity guids: a click picks the entity under the cursor
    /// (FUN_142fb55e0).</item>
    /// <item>S→C TeleportBase 0xFC sub 1 ActivateTerminal (FUN_1430c6a80): {bool open map, u64 node} — the player is at
    /// that node (client warp manager +0x802a0). Open = the Local Travel map straight away; otherwise the client fires
    /// GameEvents:OnPlayerTeleportTerminalInteraction and asks for the travel locations (0xFC sub 2), i.e. the Spire
    /// menu. The node must be in the network the client holds.</item>
    /// </list>
    /// Cost (client FUN_143452280 / FUN_143453b90): BaseCost + NodeCost (+ NodeCost again for a type-2 node, + WorldCost /
    /// ZoneCost to another world / zone, + HubCost off the surface layer), in item WarpNetwork.CostItemId 1579 (Lumens).
    /// The data has "WarpNetwork.NodeCost " and "ZoneCost " with a trailing space, so the client's lookups of the plain
    /// names miss and its defaults apply (NodeCost 1, ZoneCost 5): a trip on the landscape costs 1 Lumen, which the
    /// client shows and checks ("Ui.Teleport.CannotAfford"); the server charges the same.
    /// </para>
    /// <para>
    /// Nodes (0.589 data): the hub is the Spire's Portal Crystal (props 575 / 1871 "Portal Spire (Hub)"); underground,
    /// the continent's cave placements (ContinentCavePlacements[406]_Underground.cpa) put a Graveyard01 piece (tile 111)
    /// every ~700 m — 81 of them, tier 1, in two cave biomes (VoxelLayerPropertyGroups: property group → biome, tier) —
    /// and each gets an "Underground Portal Crystal" (prop 1881). Whether live made players discover crystals first
    /// (the client has a "Leyline discovery" fanfare, composite 11674) is not known; here every node is open.
    /// </para>
    /// </summary>
    public static class WarpNetwork
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const uint RequestSub = 4, NetworkSub = 2;
        public const uint RequestNetwork = 1, RequestWarp = 2;
        /// <summary>The hub node: the Spire's "Portal Spire (Hub)" prop (node ids are the nodes' entity guids).</summary>
        public static ulong HubNode => Spire.HubGuid;
        private const int SurfaceLayer = 0;
        /// <summary>Node types: 1 = hub (the Spire), 2 = portal crystal (the map only offers hubs and crystals).</summary>
        private const uint HubType = 1, CrystalType = 2;
        private const uint GraveyardTile = 111, UndergroundCrystalProp = 1881, PortalCrystalProp = 575;
        /// <summary>Travel cost (see the class notes) in Lumens: NodeCost, again for a crystal destination.</summary>
        public const uint CostItem = 1579, NodeCost = 1;
        private const ulong CrystalGuidBase = 0x5750_0000_0000_0000;

        private sealed class Node
        {
            public ulong Id;
            public uint Type;
            public uint Biome;
            public int Layer;
            public Vector3 Position;
            public ulong Cell;
        }

        private static readonly List<Node> underground = new();
        /// <summary>
        /// The world id each client uses. The client asks only once (its first ClientFinishedLoading), so the server
        /// pushes the network of every landscape the player enters (<see cref="EnterLandscape"/>), filed under
        /// (world, landscape).
        /// </summary>
        private static readonly Dictionary<WorldSession, uint> worlds = new();
        private static List<PlayerUpdateAddLightweightProp> crystals;

        /// <summary>Reads the home continent's cave Graveyard pieces (the underground portal crystals' sites).</summary>
        public static void Load(string gameDirectory, uint continent)
        {
            var groups = new Dictionary<uint, (uint Biome, int Tier)>();
            try
            {
                foreach (string line in File.ReadLines(Path.Combine(gameDirectory, "Resources", "VoxelLayerPropertyGroups.txt")))
                {
                    string[] f = line.Split('^');
                    if (f.Length >= 3 && uint.TryParse(f[0], out uint id))
                        groups[id] = (uint.Parse(f[1]), int.Parse(f[2]));
                }
                string path = Path.Combine(gameDirectory, "CommonData", "Voxel", "caves", $"ContinentCavePlacements[{continent}]_Underground.cpa");
                byte[] d = File.ReadAllBytes(path);
                int n = BitConverter.ToInt32(d, 4);
                var origin = Terrain.TerrainStore.ZoneOrigin;
                for (int i = 0, o = 8; i < n && o + 39 <= d.Length; i++, o += 39)
                {
                    if (BitConverter.ToUInt32(d, o) != GraveyardTile)
                        continue;
                    double x = BitConverter.ToDouble(d, o + 4), y = BitConverter.ToDouble(d, o + 12), z = BitConverter.ToDouble(d, o + 20);
                    uint group = BitConverter.ToUInt32(d, o + 33);
                    (uint biome, int tier) = groups.GetValueOrDefault(group, (0u, 1));
                    var at = new Vector3((float)((x - origin.X) / 10), (float)((y - origin.Y) / 10), (float)((z - origin.Z) / 10));
                    at = Caves.Floor(at + new Vector3(0f, 10f, 0f));
                    underground.Add(new Node
                    {
                        // Node ids are entity guids: the Local Travel map (FUN_142fb55e0) picks the node by the
                        // entity under the cursor, so each node is its crystal prop.
                        Id = CrystalGuidBase | (1000 + (ulong)underground.Count),
                        Type = CrystalType,
                        Biome = biome,
                        Layer = Math.Max(1, tier),
                        Position = at,
                        Cell = Terrain.TerrainEdits.CellAt(at)
                    });
                }
                log.Info($"Warp network: {underground.Count} underground portal crystals under continent {continent}.");
            }
            catch (Exception e)
            {
                log.Warn($"No warp network ({e.Message}).");
            }
        }

        private static Node Hub()
        {
            if (Spire.Arrival is not Vector3 at || HubNode == 0)
                return null;
            return new Node { Id = HubNode, Type = HubType, Biome = 0, Layer = SurfaceLayer, Position = at, Cell = Terrain.TerrainEdits.CellAt(at) };
        }

        /// <summary>The nodes of a player's landscape (the home continent holds the Spire and the caves).</summary>
        private static List<Node> NodesFor(WorldSession session)
        {
            var nodes = new List<Node>();
            if (!session.OnHomeContinent)
                return nodes;
            if (Hub() is Node hub)
                nodes.Add(hub);
            nodes.AddRange(underground);
            return nodes;
        }

        /// <summary>WarpNetworkClientRequest (0x113 sub 4); <paramref name="body"/> starts after the u32 sub.</summary>
        public static void Handle(WorldSession session, byte[] body)
        {
            if (body.Length < 12)
                return;
            uint type = BitConverter.ToUInt32(body, 0), zone = BitConverter.ToUInt32(body, 4), world = BitConverter.ToUInt32(body, 8);
            ulong from = body.Length >= 20 ? BitConverter.ToUInt64(body, 12) : 0, to = body.Length >= 28 ? BitConverter.ToUInt64(body, 20) : 0;
            if (type == RequestNetwork)
            {
                lock (worlds)
                    worlds[session] = world;
                SendNetwork(session, world, zone);
            }
            else if (type == RequestWarp)
                Warp(session, from, to);
            else
                log.Info($"{session.CharacterName}: warp network request type {type}: {BitConverter.ToString(body)}");
        }

        private static void SendNetwork(WorldSession session, uint world, uint zone)
        {
            List<Node> nodes = NodesFor(session);
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(NetworkSub);
            w.Write(world);
            w.Write(zone);
            var layers = nodes.GroupBy(n => n.Layer).OrderBy(g => g.Key).ToList();
            w.Write(layers.Count);
            foreach (var layer in layers)
            {
                w.Write(layer.Key);
                w.Write(layer.Count());
                foreach (Node node in layer)
                {
                    w.Write(node.Id);
                    w.Write(node.Id);
                    w.Write(node.Type);
                    w.Write(node.Biome);
                    w.Write(node.Cell);
                    w.Write(node.Position.X);
                    w.Write(node.Position.Y);
                    w.Write(node.Position.Z);
                    w.Write(0u);
                }
                w.Write(layer.Key);
            }
            w.Write(0u);
            session.EnqueueMessage(ZoneMessageOpcode.WarpNetworkWire, output.ToArray());
            log.Debug($"{session.CharacterName}: warp network {world}/{zone}: {nodes.Count} node(s) in {layers.Count} layer(s).");
        }

        /// <summary>A warp the client confirmed: the destination node, for a Lumen.</summary>
        private static void Warp(WorldSession session, ulong from, ulong to)
        {
            Node target = NodesFor(session).FirstOrDefault(n => n.Id == to);
            if (target == null || from == to)
            {
                log.Info($"{session.CharacterName}: warp {from} → {to} refused (no such node).");
                return;
            }
            uint cost = NodeCost + (target.Type == CrystalType ? NodeCost : 0);
            if (!session.TakeItem(CostItem, cost))
            {
                Chat.System(session, "You can't afford to travel there.");
                return;
            }
            if (target.Id == HubNode)
                Spire.TravelTo(session);
            else
                Teleport.To(session, target.Position, snapToGround: false);
            log.Info($"{session.CharacterName} warped {from} → {to} ({target.Position}).");
        }

        /// <summary>The player used a portal crystal: they stand at that node (the Spire menu opens on their screen).</summary>
        public static void ActivateTerminal(WorldSession session, ulong node, bool openMap)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write((byte)1);
            w.Write(openMap);
            w.Write(node);
            session.EnqueueMessage(ZoneMessageOpcode.TeleportBase, output.ToArray());
        }

        // ---- Underground portal crystals (props) ------------------------------------------------------------------------

        /// <summary>A player entered a landscape: that landscape's network, under the ids their client uses.</summary>
        public static void EnterLandscape(WorldSession session)
        {
            uint world;
            lock (worlds)
                if (!worlds.TryGetValue(session, out world))
                    return;
            SendNetwork(session, world, session.ZoneId);
        }

        public static void Forget(WorldSession session)
        {
            lock (worlds)
                worlds.Remove(session);
        }

        /// <summary>Sends the underground portal crystals to a player entering the home continent (static props, once).</summary>
        public static void Enter(WorldSession session)
        {
            crystals ??= underground.Select(n => new PlayerUpdateAddLightweightProp
            {
                Guid = n.Id,
                TransientId = PlayerManager.AllocateTransientId(),
                ModelId = Props.ModelOf(UndergroundCrystalProp) is uint m and not 0 ? m : Props.ModelOf(PortalCrystalProp),
                Position = new Vector4(n.Position, 1f),
                Rotation = new Vector4(0f, 0f, 0f, 1f),
                Scale = new Vector4(1f, 1f, 1f, 1f),
                NameId = GameTableManager.Instance.PropDefs.GetEntry(UndergroundCrystalProp)?.NameStringId ?? 0
            }).ToList();
            foreach (PlayerUpdateAddLightweightProp prop in crystals)
                session.EnqueueMessage(prop);
            if (crystals.Count > 0)
            {
                var flagging = new DynamicInteractionFlagging();
                flagging.Guids.AddRange(crystals.Select(c => c.Guid));
                session.EnqueueMessage(flagging);
            }
        }

        /// <summary>The node id (crystal prop guid) of the n-th underground crystal, 0 when there is none.</summary>
        public static ulong CrystalNode(int index) => index >= 0 && index < underground.Count ? underground[index].Id : 0;

        /// <summary>Is this guid an underground portal crystal? (usable: clicking it is ActivateTerminal.)</summary>
        public static bool IsCrystal(ulong guid) => (guid & 0xFFFF_0000_0000_0000) == CrystalGuidBase && underground.Any(n => n.Id == guid);

        /// <summary>The crystal's node (0 when not a crystal).</summary>
        public static ulong NodeOf(ulong guid) => IsCrystal(guid) ? guid : 0;

        /// <summary>LightweightToFullProp for a crystal the client wants solid / interactable.</summary>
        public static bool SendFull(WorldSession session, ulong guid)
        {
            PlayerUpdateAddLightweightProp prop = crystals?.FirstOrDefault(p => p.Guid == guid);
            if (prop == null)
                return false;
            session.EnqueueMessage(new PlayerUpdateLightweightToFullProp { TransientId = prop.TransientId });
            return true;
        }
    }
}
