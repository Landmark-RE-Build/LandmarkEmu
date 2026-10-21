using LandmarkEmulator.Shared.GameTable;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The Spire's Local Travel (wiki "Spire": Local Travel goes deep into the caves; a map opens and the player clicks
    /// a spire icon): the landscape's warp network. The client's ControllerLocalTravel (camera
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
    /// Cost [P] (0.695 client: the click on a node, FUN_143055480 in ControllerLocalTravel, builds a from and a to
    /// {zone, world, location key (node +0x24), node guid, node type} and calls the confirm-cost function FUN_143504a80;
    /// the settings are read by FUN_143505fd0 from StringHashToValue): BaseCost, + NodeCost when the from node is a
    /// crystal (type 2), + WorldCost to another world, + ZoneCost to another zone (landscape), + HubCost when the two
    /// location keys (or zone / world) differ, + NodeCost when the destination is a crystal; in item
    /// WarpNetwork.CostItemId 1579 (Lumens). Shipped data (gameData695 StringHashToValue rows 675-680): BaseCost 0,
    /// HubCost 0, WorldCost 0, CostItemId 1579; "WarpNetwork.NodeCost " and "WarpNetwork.ZoneCost " carry a trailing
    /// space, so the client's lookups of the plain names miss and its defaults apply (NodeCost 1, ZoneCost 5). So the
    /// Confirm Travel window shows hub → crystal 1, crystal → hub 1, crystal → crystal 2 (+ 5 to another landscape),
    /// checks it ("Ui.Teleport.CannotAfford"), and the server charges the same (<see cref="CostOf"/>). (The 0.589
    /// /warpnetwork debug list, FUN_143452280, adds NodeCost always and HubCost off layer -1; it is a debug estimate,
    /// not the confirm. 0.589 FUN_143453b90 = 0.695 FUN_143505c90 is the node +0x24 lookup, not a cost.)
    /// Source: trace/0695/agent-leads3-c.md L130.
    /// </para>
    /// <para>
    /// Nodes (0.589 data): the hub is the Spire's Portal Crystal (props 575 / 1871 "Portal Spire (Hub)"); underground,
    /// the continent's cave placements (ContinentCavePlacements[406]_Underground.cpa) put a Graveyard01 piece (tile 111)
    /// every ~700 m — 81 of them, tier 1, in two cave biomes (VoxelLayerPropertyGroups: property group → biome, tier) —
    /// and each gets an "Underground Portal Crystal" (prop 1881). Whether live made players discover crystals first
    /// (the client has a "Leyline discovery" fanfare, composite 11674) is not known; here every node is open. The client
    /// keeps no discovery state for warp nodes (node +0x24 is a location key, the +0x248 default 11674 has no reader;
    /// trace/0695/agent-leads3-c.md L130), so live's discovery, if it outlived the 5 May 2015 notes, was the server leaving
    /// undiscovered nodes out of the network [I].
    /// </para>
    /// <para>
    /// Where a crystal stands (<see cref="SiteOf"/>): the same 81 Graveyard01 pieces are the client's type-3 "cave beginning
    /// locations" (.cpa tile byte 32 set, byte 38 = 3; agent-leads3-c.md L51) and each has a cavern graveyard
    /// (graveyard_launch.otd, Graveyards.Landscape.cs) whose stamped box covers the piece's column, so a crystal stands on
    /// that stamp's ground when the graveyard is laid out from the template, else on the cave floor under the piece.
    /// </para>
    /// <para>
    /// Every landscape with Chaos Caverns (OPEN-LEADS L130, trace/0695/agent-outposts.md §3) [I]: each island's own
    /// Underground placement file has the same 81 Graveyard01 pieces (tile 111) as 406, every landscape has its Spire, and
    /// the client files a network per (world, landscape) (FUN_143454ec0), so each landscape the server gives caverns
    /// (Caves.Landscapes, setting CavernLandscapes) gets its own network: its Spire's hub and its 81 crystals, read in the
    /// background the first time a player is there (the cave floors under them need that landscape's cave index) and
    /// pushed to the players there once ready. Node ids (crystal guids): the home continent's as before (base + 1000 + n),
    /// another landscape's base + zone &lt;&lt; 16 + 1000 + n. Whether live listed crystals off the home landscape is the
    /// open part of L130 (runtime check in the test client).
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
        /// <summary>
        /// Travel cost settings as the 0.695 client resolves them from the shipped data (see the class notes,
        /// FUN_143505fd0): BaseCost 0, WorldCost 0, ZoneCost 5 (default: the data key has a trailing space), HubCost 0,
        /// NodeCost 1 (default, trailing space), in item 1579 (Lumens).
        /// </summary>
        public const uint CostItem = 1579, BaseCost = 0, WorldCost = 0, ZoneCost = 5, HubCost = 0, NodeCost = 1;
        private const ulong CrystalGuidBase = 0x5750_0000_0000_0000;

        private sealed class Node
        {
            public ulong Id;
            public uint Type;
            public uint Biome;
            public int Layer;
            public Vector3 Position;
            public ulong Cell;
            /// <summary>A crystal's Graveyard01 piece (local metres, the .cpa record's position).</summary>
            public Vector3 Piece;
        }

        /// <summary>Each landscape's underground portal crystals (the home continent's loaded at start, the rest on demand).</summary>
        private static readonly ConcurrentDictionary<uint, List<Node>> underground = new();
        /// <summary>Every crystal by node id (= its prop guid), with its landscape.</summary>
        private static readonly ConcurrentDictionary<ulong, uint> zoneOfNode = new();
        /// <summary>Landscapes whose crystals are being read in the background.</summary>
        private static readonly ConcurrentDictionary<uint, byte> loading = new();
        /// <summary>Landscapes read in the background, waiting for the world tick to push them (<see cref="Update"/>).</summary>
        private static readonly ConcurrentQueue<uint> loaded = new();
        private static string gameDirectory;
        private static Dictionary<uint, (uint Biome, int Tier)> groups;
        /// <summary>
        /// The world id each client uses. The client asks only once (its first ClientFinishedLoading), so the server
        /// pushes the network of every landscape the player enters (<see cref="EnterLandscape"/>), filed under
        /// (world, landscape).
        /// </summary>
        private static readonly Dictionary<WorldSession, uint> worlds = new();
        /// <summary>Each landscape's crystal props (built once), and every one by guid (LightweightToFullProp).</summary>
        private static readonly ConcurrentDictionary<uint, List<PlayerUpdateAddLightweightProp>> crystals = new();
        private static readonly ConcurrentDictionary<ulong, PlayerUpdateAddLightweightProp> crystalByGuid = new();
        private static uint Home => Terrain.TerrainStore.HomeContinent;

        /// <summary>Reads the home continent's cave Graveyard pieces (the underground portal crystals' sites).</summary>
        public static void Load(string gameDirectory, uint continent)
        {
            WarpNetwork.gameDirectory = gameDirectory;
            try
            {
                List<Node> nodes = ReadCrystals(continent, Home);
                underground[Home] = nodes;
                log.Info($"Warp network: {nodes.Count} underground portal crystals under continent {continent}.");
            }
            catch (Exception e)
            {
                log.Warn($"No warp network ({e.Message}).");
            }
        }

        /// <summary>
        /// One landscape's crystals: the Graveyard01 pieces of its continent's Underground placement file, each on the cave
        /// floor under it (that landscape's cave shapes), tier and biome from its property group.
        /// </summary>
        private static List<Node> ReadCrystals(uint continent, uint zone)
        {
            if (groups == null)
            {
                var read = new Dictionary<uint, (uint Biome, int Tier)>();
                foreach (string line in File.ReadLines(Path.Combine(gameDirectory, "Resources", "VoxelLayerPropertyGroups.txt")))
                {
                    string[] f = line.Split('^');
                    if (f.Length >= 3 && uint.TryParse(f[0], out uint id))
                        read[id] = (uint.Parse(f[1]), int.Parse(f[2]));
                }
                groups = read;
            }
            var nodes = new List<Node>();
            string path = Path.Combine(gameDirectory, "CommonData", "Voxel", "caves", $"ContinentCavePlacements[{continent}]_Underground.cpa");
            byte[] d = File.ReadAllBytes(path);
            int n = BitConverter.ToInt32(d, 4);
            var origin = Terrain.TerrainStore.ZoneOrigin;
            // The home continent keeps its ids (base + 1000 + n); another landscape's carry its zone in bits 16-31.
            ulong idBase = CrystalGuidBase | (zone == Home ? 0ul : (ulong)zone << 16);
            for (int i = 0, o = 8; i < n && o + 39 <= d.Length; i++, o += 39)
            {
                if (BitConverter.ToUInt32(d, o) != GraveyardTile)
                    continue;
                double x = BitConverter.ToDouble(d, o + 4), y = BitConverter.ToDouble(d, o + 12), z = BitConverter.ToDouble(d, o + 20);
                uint group = BitConverter.ToUInt32(d, o + 33);
                (uint biome, int tier) = groups.GetValueOrDefault(group, (0u, 1));
                var piece = new Vector3((float)((x - origin.X) / 10), (float)((y - origin.Y) / 10), (float)((z - origin.Z) / 10));
                Vector3 at = SiteOf(piece, zone);
                var node = new Node
                {
                    // Node ids are entity guids: the Local Travel map (FUN_142fb55e0) picks the node by the
                    // entity under the cursor, so each node is its crystal prop.
                    Id = idBase | (1000 + (ulong)nodes.Count),
                    Type = CrystalType,
                    Biome = biome,
                    Layer = Math.Max(1, tier),
                    Position = at,
                    Cell = Terrain.TerrainEdits.CellAt(at),
                    Piece = piece
                };
                nodes.Add(node);
                zoneOfNode[node.Id] = zone;
            }
            return nodes;
        }

        /// <summary>
        /// Where a crystal stands at its Graveyard01 piece [I: the piece is ours, class notes]: on the cavern graveyard's
        /// stamped template where the landscape has one there (the template's box covers the piece's column, so the stamp
        /// decides the ground: Graveyards.StandAtPiece, CavernGraveyardStamps), else on the cave floor under the piece, 1 m
        /// above the rock (the client's cave pieces), as before. The warp arrives at the same point.
        /// </summary>
        private static Vector3 SiteOf(Vector3 piece, uint zone)
            => Graveyards.StandAtPiece(zone, piece) ?? Caves.Floor(piece + new Vector3(0f, 10f, 0f), zone);

        /// <summary>
        /// Start-up, after the cavern graveyards' stamps are known (Graveyards.LoadStamps): the home continent's crystals,
        /// read before the templates folder and the graveyards were, move onto their graveyards' stamped ground
        /// (<see cref="SiteOf"/>). The other landscapes' are read later, in the background, when both are known.
        /// </summary>
        public static void SettleOnGraveyards()
        {
            if (!underground.TryGetValue(Home, out List<Node> nodes))
                return;
            int moved = 0;
            foreach (Node node in nodes)
                if (Graveyards.StandAtPiece(Home, node.Piece) is Vector3 at && at != node.Position)
                {
                    node.Position = at;
                    node.Cell = Terrain.TerrainEdits.CellAt(at);
                    moved++;
                }
            if (moved > 0)
                log.Info($"Warp network: {moved} of {nodes.Count} underground portal crystals stand on their cavern graveyards' stamped ground.");
        }

        /// <summary>
        /// A landscape with caverns that has no crystals yet: read in the background (its cave floors), then pushed to the
        /// players there by <see cref="Update"/>. Nothing for the home continent (read at start) or a landscape without caves.
        /// </summary>
        private static void Prepare(uint zone)
        {
            if (zone == Home || gameDirectory == null || underground.ContainsKey(zone) || !Caves.HasCaverns(zone) || !loading.TryAdd(zone, 0))
                return;
            uint continent = Landscapes.Get(zone)?.Continent ?? zone;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    List<Node> nodes = ReadCrystals(continent, zone);
                    underground[zone] = nodes;
                    log.Info($"Warp network: {nodes.Count} underground portal crystals on landscape {zone}.");
                }
                catch (Exception e)
                {
                    underground[zone] = new List<Node>();
                    log.Warn($"Warp network of landscape {zone}: no crystals ({e.Message}).");
                }
                loaded.Enqueue(zone);
            });
        }

        /// <summary>World tick: a landscape's crystals were read: its network and crystal props to the players there.</summary>
        public static void Update()
        {
            while (loaded.TryDequeue(out uint zone))
                foreach (WorldSession session in PlayerManager.InZone(zone).Where(s => !s.StreamsOnHold))
                {
                    EnterLandscape(session);
                    Enter(session);
                }
        }

        /// <summary>A landscape's hub node: its Spire's "Portal Spire (Hub)" prop (0 = none yet).</summary>
        public static ulong HubNodeOn(uint zone) => Spire.HubGuidOn(zone == 0 ? Home : zone);

        private static Node Hub(uint zone)
        {
            ulong id = HubNodeOn(zone);
            if (Spire.ArrivalOn(zone) is not Vector3 at || id == 0)
                return null;
            return new Node { Id = id, Type = HubType, Biome = 0, Layer = SurfaceLayer, Position = at, Cell = Terrain.TerrainEdits.CellAt(at) };
        }

        /// <summary>
        /// The nodes of a player's landscape: its Spire's hub and its underground crystals (every landscape with caverns;
        /// one whose crystals aren't read yet gets the hub now and the rest once they are, <see cref="Update"/>).
        /// </summary>
        private static List<Node> NodesFor(WorldSession session)
        {
            var nodes = new List<Node>();
            uint zone = session.ZoneId == 0 ? Home : session.ZoneId;
            if (Hub(zone) is Node hub)
                nodes.Add(hub);
            if (underground.TryGetValue(zone, out List<Node> crystalNodes))
                nodes.AddRange(crystalNodes);
            else
                Prepare(zone);
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
                    // Node +0x24 [P field, I value]: the node's location key (reader FUN_143502390; default -2,
                    // DAT_145262608). Its only reader is FUN_143505c90 (lookup by guid), whose callers in
                    // ControllerLocalTravel feed it into the confirm cost FUN_143504a80, which adds HubCost when the
                    // from and to keys differ. It is not a "discovered" flag (the client keeps no discovery state).
                    // The server's key is the node's layer (0 = the Spire hub on the surface, cave tier for a crystal),
                    // the same key CostOf compares; HubCost is 0 in the shipped data, so the window shows no change.
                    // Source: trace/0695/agent-leads3-c.md L130.
                    w.Write(node.Layer);
                }
                w.Write(layer.Key);
            }
            w.Write(0u);
            session.EnqueueMessage(ZoneMessageOpcode.WarpNetworkWire, output.ToArray());
            log.Debug($"{session.CharacterName}: warp network {world}/{zone}: {nodes.Count} node(s) in {layers.Count} layer(s).");
        }

        /// <summary>
        /// The cost of a warp, as the client's confirm-cost function FUN_143504a80 computes it [P] (class notes). Both
        /// nodes are on the network the player holds; the zone / world terms are kept so that the sum stays the client's
        /// if a network ever lists another landscape's or world's nodes. A <paramref name="from"/> that is not a node of
        /// the network (the player did not open the map at a node) counts as the hub [I]: no from-node term.
        /// </summary>
        private static uint CostOf(Node from, uint fromZone, Node to, uint toZone)
        {
            uint cost = BaseCost;
            if (from?.Type == CrystalType)
                cost += NodeCost;
            // One world per server, so WorldCost (0 in the data) never applies here.
            if (fromZone != toZone)
                cost += ZoneCost;
            if (fromZone != toZone || (from?.Layer ?? SurfaceLayer) != to.Layer)
                cost += HubCost;
            if (to.Type == CrystalType)
                cost += NodeCost;
            return cost;
        }

        /// <summary>A warp the client confirmed: the destination node, for the client's confirm cost (<see cref="CostOf"/>).</summary>
        private static void Warp(WorldSession session, ulong from, ulong to)
        {
            List<Node> nodes = NodesFor(session);
            Node target = nodes.FirstOrDefault(n => n.Id == to);
            if (target == null || from == to)
            {
                log.Info($"{session.CharacterName}: warp {from} → {to} refused (no such node).");
                return;
            }
            uint zone = session.ZoneId == 0 ? Home : session.ZoneId;
            Node fromNode = nodes.FirstOrDefault(n => n.Id == from);
            uint cost = CostOf(fromNode, zone, target, zoneOfNode.GetValueOrDefault(target.Id, zone));
            if (cost > 0 && !session.TakeItem(CostItem, cost))
            {
                Chat.System(session, "You can't afford to travel there.");
                return;
            }
            if (target.Type == HubType)
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

        /// <summary>
        /// Sends the underground portal crystals of the player's landscape (static props, all of them, on every entry: the
        /// client drops them with a re-zone). A landscape whose crystals aren't read yet gets them once they are (<see cref="Update"/>).
        /// With <paramref name="send"/> false (a loading screen holds the world streams, PlayerManager "Stream hold") only
        /// the reading starts; the crystals go out when the hold ends.
        /// </summary>
        public static void Enter(WorldSession session, bool send = true)
        {
            uint zone = session.ZoneId == 0 ? Home : session.ZoneId;
            if (!underground.TryGetValue(zone, out List<Node> nodes))
            {
                Prepare(zone);
                return;
            }
            if (!send)
                return;
            List<PlayerUpdateAddLightweightProp> props = crystals.GetOrAdd(zone, _ => nodes.Select(n =>
            {
                var prop = new PlayerUpdateAddLightweightProp
                {
                    Guid = n.Id,
                    TransientId = PlayerManager.AllocateTransientId(),
                    ModelId = Props.ModelOf(UndergroundCrystalProp) is uint m and not 0 ? m : Props.ModelOf(PortalCrystalProp),
                    Position = new Vector4(n.Position, 1f),
                    Rotation = new Vector4(0f, 0f, 0f, 1f),
                    Scale = new Vector4(1f, 1f, 1f, 1f),
                    NameId = GameTableManager.Instance.PropDefs.GetEntry(UndergroundCrystalProp)?.NameStringId ?? 0
                };
                crystalByGuid[prop.Guid] = prop;
                return prop;
            }).ToList());
            foreach (PlayerUpdateAddLightweightProp prop in props)
                session.EnqueueMessage(prop);
            if (props.Count > 0)
            {
                var flagging = new DynamicInteractionFlagging();
                flagging.Guids.AddRange(props.Select(c => c.Guid));
                session.EnqueueMessage(flagging);
            }
        }

        /// <summary>The node id (crystal prop guid) of the home continent's n-th underground crystal, 0 when there is none.</summary>
        public static ulong CrystalNode(int index) => CrystalNodeOn(Home, index);

        /// <summary>The node id of a landscape's n-th underground crystal, 0 when there is none (or not read yet).</summary>
        public static ulong CrystalNodeOn(uint zone, int index)
            => underground.TryGetValue(zone == 0 ? Home : zone, out List<Node> nodes) && index >= 0 && index < nodes.Count ? nodes[index].Id : 0;

        /// <summary>Is this guid an underground portal crystal? (usable: clicking it is ActivateTerminal.)</summary>
        public static bool IsCrystal(ulong guid) => (guid & 0xFFFF_0000_0000_0000) == CrystalGuidBase && zoneOfNode.ContainsKey(guid);

        /// <summary>The crystal's node (0 when not a crystal).</summary>
        public static ulong NodeOf(ulong guid) => IsCrystal(guid) ? guid : 0;

        /// <summary>LightweightToFullProp for a crystal the client wants solid / interactable.</summary>
        public static bool SendFull(WorldSession session, ulong guid)
        {
            if (!crystalByGuid.TryGetValue(guid, out PlayerUpdateAddLightweightProp prop))
                return false;
            session.EnqueueMessage(new PlayerUpdateLightweightToFullProp { TransientId = prop.TransientId });
            return true;
        }
    }
}
