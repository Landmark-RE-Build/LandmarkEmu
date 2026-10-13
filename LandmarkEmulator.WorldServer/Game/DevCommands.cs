using System.Globalization;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Developer commands read from a text file next to the server (dev_commands.txt), so packets and items can be tried
    /// on live clients without restarting. Each line is run once against every player in the world, then the file is
    /// emptied. Local file only; nothing is exposed on the network.
    /// <list type="bullet">
    /// <item><c>give &lt;itemDefinitionId&gt; &lt;count&gt;</c></item>
    /// <item><c>send &lt;hex bytes&gt;</c>: raw zone packet, opcode bytes included (e.g. <c>9A 00 05 ...</c>)</item>
    /// <item><c>prop &lt;propId&gt; [dx dy dz]</c>: places that prop next to each player</item>
    /// <item><c>station &lt;stationId&gt;</c>: opens that station's crafting window, wherever the player is</item>
    /// </list>
    /// </summary>
    public static class DevCommands
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        /// <summary>Test switch: no trees, plants or creatures are streamed (set LANDMARK_NOSTREAM=1).</summary>
        public static readonly bool NoStreaming = System.Environment.GetEnvironmentVariable("LANDMARK_NOSTREAM") == "1";

        private static readonly string path = Path.Combine(AppContext.BaseDirectory, "dev_commands.txt");
        private static DateTime lastCheck = DateTime.MinValue;

        /// <summary>
        /// Runs the commands written to dev_commands.txt (checked at most once a second, then emptied) for every player
        /// online. One command per line; lines starting with # are ignored.
        /// </summary>
        /// <summary>Commands from the admin page (<see cref="Admin"/>), run with the file's on the next tick.</summary>
        private static readonly System.Collections.Concurrent.ConcurrentQueue<string> queued = new();

        /// <summary>Queues one command line ("@Name command" for one player, else everyone).</summary>
        public static void Enqueue(string line) => queued.Enqueue(line);

        public static void Update(IEnumerable<WorldSession> players)
        {
            var fromQueue = new List<string>();
            while (queued.TryDequeue(out string q))
                fromQueue.Add(q);
            if (fromQueue.Count == 0 && (DateTime.UtcNow - lastCheck).TotalSeconds < 1)
                return;
            string[] lines = Array.Empty<string>();
            if ((DateTime.UtcNow - lastCheck).TotalSeconds >= 1)
            {
                lastCheck = DateTime.UtcNow;
                if (File.Exists(path))
                {
                    try
                    {
                        lines = File.ReadAllLines(path);
                        if (lines.Length > 0)
                            File.WriteAllText(path, "");
                    }
                    catch (IOException)
                    {
                        lines = Array.Empty<string>();
                    }
                }
            }
            lines = lines.Concat(fromQueue).ToArray();
            if (lines.Length == 0)
                return;

            List<WorldSession> everyone = players.ToList();
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;
                // "@Name command" runs the command for that character only (tests run beside real players).
                List<WorldSession> targets = everyone;
                if (line.StartsWith("@"))
                {
                    string[] split = line.Split(' ', 2);
                    targets = everyone.Where(t => string.Equals(t.CharacterName, split[0][1..], StringComparison.OrdinalIgnoreCase)).ToList();
                    line = split.Length > 1 ? split[1].Trim() : "";
                }
                string[] parts = line.Split(' ', 2);
                try
                {
                    switch (parts[0])
                    {
                        case "give":
                            string[] args = parts[1].Split(' ');
                            foreach (WorldSession session in targets)
                                session.GiveItem(uint.Parse(args[0]), uint.Parse(args[1]));
                            break;
                        case "send":
                            byte[] packet = Convert.FromHexString(parts[1].Replace(" ", "").Replace("-", ""));
                            foreach (WorldSession session in targets)
                                session.SendRaw(packet);
                            break;
                        case "testcell":
                        {
                            // testcell <level> <x> <y> <z> [height 0-65535] [extent] [variant]
                            string[] a = parts[1].Split(' ');
                            ulong id = Terrain.TerrainStore.CellId(uint.Parse(a[0]), uint.Parse(a[1]), uint.Parse(a[2]), uint.Parse(a[3]));
                            ushort height = a.Length > 4 ? ushort.Parse(a[4]) : (ushort)16384;
                            ushort extent = a.Length > 5 ? ushort.Parse(a[5]) : (ushort)32767;
                            int variant = a.Length > 6 ? int.Parse(a[6]) : 0;
                            ushort origin = a.Length > 7 ? ushort.Parse(a[7]) : variant >= 8 ? Terrain.TestMesh.PositionZero : (ushort)0;
                            byte[] geometry = Terrain.TestMesh.FlatQuad(height, extent, Terrain.TestMesh.Arrays(variant), origin);
                            Terrain.TerrainStore.SetLayer(id, 0, Terrain.TerrainStore.LayerBlob(geometry));
                            foreach (WorldSession session in targets)
                                Terrain.TerrainStore.SendManifestUpdate(session, id);
                            break;
                        }
                        case "timeofday":
                        {
                            // timeofday <hour 0-24>: moves the world clock; everyone online is synced at once.
                            WorldClock.SetHour(double.Parse(parts[1], CultureInfo.InvariantCulture));
                            foreach (WorldSession session in targets)
                                session.EnqueueMessage(new Network.Message.Model.GameTimeSync
                                {
                                    Time = WorldClock.SyncTime(Network.Handlers.MiscHandler.LastClientTime),
                                    CycleSpeed = WorldClock.CycleSpeed,
                                });
                            log.Info($"World clock now {WorldClock.SecondsOfDay / 3600:F2} h.");
                            break;
                        }
                        case "ringscale":
                            Abilities.HarvestAreaScale = float.Parse(parts[1], CultureInfo.InvariantCulture);
                            foreach (WorldSession session in targets)
                                session.SendCalculatedStats();
                            break;
                        case "pinata":
                        {
                            // pinata <definition> [kind] [a b c]: a loot pinata 2 m ahead of the player.
                            string[] a = parts[1].Split(' ');
                            uint P(int i, uint d) => a.Length > i ? uint.Parse(a[i]) : d;
                            foreach (WorldSession session in targets)
                                LootPinatas.Spawn(session, session.Position + new System.Numerics.Vector3(0, 1.5f, a.Length > 5 ? float.Parse(a[5]) : -4f), P(0, 21), P(1, 3), P(2, 0), P(3, 0), P(4, 0));
                            break;
                        }
                        case "digmode":
                            // digmode <n>: test switch for smoothed hole surfaces (0 off).
                            Terrain.TerrainEdits.OffsetMode = int.Parse(parts[1]);
                            log.Info($"Dig offset mode {Terrain.TerrainEdits.OffsetMode}.");
                            break;
                        case "carvefwd":
                        {
                            // carvefwd <ahead m> <right m> <radius dm>: carve in front of the player (as they face), for
                            // screenshots of holes from the default camera.
                            string[] a = parts[1].Split(' ');
                            WorldSession session = targets.FirstOrDefault();
                            if (session == null)
                                break;
                            var forward = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitZ, session.Facing);
                            forward.Y = 0;
                            forward = forward.LengthSquared() > 1e-6f ? System.Numerics.Vector3.Normalize(forward) : System.Numerics.Vector3.UnitZ;
                            var right = new System.Numerics.Vector3(forward.Z, 0, -forward.X);
                            var at = session.Position + forward * float.Parse(a[0], CultureInfo.InvariantCulture) + right * float.Parse(a[1], CultureInfo.InvariantCulture);
                            at.Y = Terrain.TerrainHeights.HeightAt(at.X, at.Z, session.ZoneId) ?? at.Y;
                            var (cells, voxels) = Terrain.TerrainEdits.Sphere(at, double.Parse(a[2], CultureInfo.InvariantCulture), 0, session.ZoneId);
                            Terrain.TerrainEdits.Broadcast(cells, session.ZoneId);
                            log.Info($"Dug {voxels} voxels ahead at {at} (facing {forward}).");
                            break;
                        }
                        case "rimmode":
                            Terrain.TerrainEdits.RimMode = int.Parse(parts[1]);
                            break;
                        case "carve":
                        {
                            // carve <dx> <dz> <radius dm> [dy]: carve a sphere at the ground next to the first player.
                            string[] a = parts[1].Split(' ');
                            WorldSession session = targets.FirstOrDefault();
                            if (session == null)
                                break;
                            float x = session.Position.X + float.Parse(a[0], CultureInfo.InvariantCulture);
                            float z = session.Position.Z + float.Parse(a[1], CultureInfo.InvariantCulture);
                            float y = (Terrain.TerrainHeights.HeightAt(x, z, session.ZoneId) ?? session.Position.Y)
                                + (a.Length > 3 ? float.Parse(a[3], CultureInfo.InvariantCulture) : 0f);
                            var (cells, voxels) = Terrain.TerrainEdits.Sphere(new System.Numerics.Vector3(x, y, z),
                                double.Parse(a[2], CultureInfo.InvariantCulture), 0, session.ZoneId);
                            Terrain.TerrainEdits.Broadcast(cells, session.ZoneId);
                            log.Info($"Dug {voxels} voxels at ({x:F1}, {y:F1}, {z:F1}).");
                            break;
                        }
                        case "livecell":
                        {
                            // livecell <level> <x> <y> <z> [height] [variant]: change a cell while players are in the world,
                            // pushed as a block event (damage + CellsUpdate naming the same transaction).
                            string[] a = parts[1].Split(' ');
                            ulong id = Terrain.TerrainStore.CellId(uint.Parse(a[0]), uint.Parse(a[1]), uint.Parse(a[2]), uint.Parse(a[3]));
                            ushort height = a.Length > 4 ? ushort.Parse(a[4]) : (ushort)40000;
                            int variant = a.Length > 5 ? int.Parse(a[5]) : 10;
                            byte[] geometry = Terrain.TestMesh.FlatQuad(height, 32767, Terrain.TestMesh.Arrays(variant), Terrain.TestMesh.PositionZero);
                            Terrain.TerrainStore.SetLayer(id, 0, Terrain.TerrainStore.LayerBlob(geometry));
                            ulong tx = Terrain.TerrainStore.NewTransaction();
                            byte[] damage = Terrain.TerrainStore.BuildBlockDamage(
                                new[] { (id, (IReadOnlyCollection<(ushort, byte)>)new[] { ((ushort)0, (byte)0) }) }, tx);
                            byte[] update = Terrain.TerrainStore.BuildCellsUpdate(new[] { id }, new[] { tx });
                            foreach (WorldSession session in targets)
                            {
                                session.EnqueueMessage(ZoneMessageOpcode.TerrainCellCellBlockDamage, damage);
                                session.EnqueueMessage(ZoneMessageOpcode.TerrainCellCellsUpdate, update);
                                session.EnqueueMessage(ZoneMessageOpcode.TerrainCellZoneProcessingComplete,
                                    Terrain.TerrainStore.BuildProcessingComplete(tx));
                            }
                            break;
                        }
                        case "blockdmg":
                        {
                            // blockdmg <level> <x> <y> <z> <firstBlock> <lastBlock> <health%>: probe CellBlockDamage on real terrain.
                            string[] a = parts[1].Split(' ');
                            ulong id = Terrain.TerrainStore.CellId(uint.Parse(a[0]), uint.Parse(a[1]), uint.Parse(a[2]), uint.Parse(a[3]));
                            ushort first = ushort.Parse(a[4]), last = ushort.Parse(a[5]);
                            byte health = byte.Parse(a[6]);
                            var blocks = new List<(ushort, byte)>();
                            for (int b = first; b <= last; b++)
                                blocks.Add(((ushort)b, health));
                            ulong tx = Terrain.TerrainStore.NewTransaction();
                            byte[] damage = Terrain.TerrainStore.BuildBlockDamage(
                                new[] { (id, (IReadOnlyCollection<(ushort, byte)>)blocks) }, tx);
                            foreach (WorldSession session in targets)
                            {
                                session.EnqueueMessage(ZoneMessageOpcode.TerrainCellCellBlockDamage, damage);
                                session.EnqueueMessage(ZoneMessageOpcode.TerrainCellZoneProcessingComplete,
                                    Terrain.TerrainStore.BuildProcessingComplete(tx));
                            }
                            break;
                        }
                        case "blockcell":
                        {
                            // blockcell <level> <x> <y> <z> <x0> <y0> <z0> <x1> <y1> <z1> [material] [layer] [prefixSize]:
                            // push a voxel box as block data (live).
                            string[] a = parts[1].Split(' ');
                            ulong id = Terrain.TerrainStore.CellId(uint.Parse(a[0]), uint.Parse(a[1]), uint.Parse(a[2]), uint.Parse(a[3]));
                            int[] box = a.Skip(4).Take(6).Select(int.Parse).ToArray();
                            ushort material = a.Length > 10 ? ushort.Parse(a[10]) : (ushort)390;
                            uint layer = a.Length > 11 ? uint.Parse(a[11]) : 0;
                            bool prefix = a.Length > 12 && a[12] == "1";
                            var blocks = new Terrain.BlockData();
                            for (int z = box[2]; z <= box[5]; z++)
                                for (int y = box[1]; y <= box[4]; y++)
                                    for (int x = box[0]; x <= box[3]; x++)
                                        blocks.SetSolid(Terrain.BlockData.Index(x, y, z), material);
                            byte[] voxelBytes = blocks.Serialize();
                            byte[] blob = prefix ? Terrain.TerrainStore.LayerBlob(voxelBytes) : Terrain.TerrainStore.Pack(voxelBytes);
                            byte[] update = Terrain.TerrainStore.BuildBlocksUpdate(id, layer, (uint)Environment.TickCount, blob);
                            foreach (WorldSession session in targets)
                                session.EnqueueMessage(ZoneMessageOpcode.TerrainCellCellsUpdate, update);
                            log.Info($"blockcell: {voxelBytes.Length} raw bytes, {blob.Length} blob bytes.");
                            break;
                        }
                        case "dig":
                        {
                            // dig [dx dy dz]: a pick swing at an offset from the player (default 2 m ahead, 1 m down).
                            string[] a = parts.Length > 1 ? parts[1].Split(' ') : Array.Empty<string>();
                            var digOffset = a.Length >= 3
                                ? new System.Numerics.Vector3(float.Parse(a[0]), float.Parse(a[1]), float.Parse(a[2]))
                                : new System.Numerics.Vector3(2, -1, 0);
                            foreach (WorldSession session in targets)
                                Abilities.Mine(session, 3253, session.Position + digOffset);
                            break;
                        }
                        case "tpto":
                        {
                            // tpto <character>: to another player's landscape and position (two-player tests).
                            WorldSession other = PlayerManager.Snapshot().FirstOrDefault(p => string.Equals(p.CharacterName, parts[1].Trim(), StringComparison.OrdinalIgnoreCase));
                            if (other != null)
                                foreach (WorldSession session in targets)
                                    if (session != other)
                                        Teleport.ToZone(session, other.Position + new System.Numerics.Vector3(1f, 0f, 0f), other.ZoneId);
                            break;
                        }
                        case "tpz":
                        {
                            // tpz x y z: teleport through a re-zone (loading screen), like long jumps.
                            string[] a = parts[1].Split(' ');
                            var v = new System.Numerics.Vector3(float.Parse(a[0]), float.Parse(a[1]), float.Parse(a[2]));
                            foreach (WorldSession session in targets)
                                Teleport.To(session, v, true, loadingScreen: true);
                            break;
                        }
                        case "tp":
                        case "tpo":
                        {
                            // tp x y z [nosnap] (local metres) / tpo dx dy dz (relative): ClientUpdate 0xC
                            // TeleportToLocation. "nosnap" keeps the exact height (e.g. inside a cave) instead of
                            // dropping onto the surface.
                            string[] a = parts[1].Split(' ');
                            var v = new System.Numerics.Vector3(float.Parse(a[0]), float.Parse(a[1]), float.Parse(a[2]));
                            bool snap = !(a.Length > 3 && a[3] == "nosnap");
                            foreach (WorldSession session in targets)
                                Teleport.To(session, parts[0] == "tpo" ? session.Position + v : v, snap);
                            break;
                        }
                        case "cave":
                            // cave: the same as Travel -> An Underground Adventure (a random cavern).
                            foreach (WorldSession session in targets)
                                Caves.TravelRandom(session);
                            break;
                        case "objective":
                            // objective <id> [amount]: counts towards an achievement objective (goals testing).
                            {
                                string[] o = parts[1].Split(' ');
                                uint amount = o.Length > 1 ? uint.Parse(o[1]) : 1;
                                foreach (WorldSession session in targets)
                                    Achievements.Progress(session, uint.Parse(o[0]), amount);
                            }
                            break;
                        case "achreset":
                            // achreset <achievement id>: back to not started (tests that complete an achievement).
                            foreach (WorldSession session in targets)
                                Achievements.ResetObjectives(session.PlayerGuid, uint.Parse(parts[1]), 0);
                            break;
                        case "rezonecave":
                            // rezonecave: test only. A random cavern through Teleport.Rezone (ClientBeginZoning).
                            foreach (WorldSession session in targets)
                                if (Caves.RandomCavern() is System.Numerics.Vector3 at)
                                {
                                    if (session.OnHomeContinent)
                                        Teleport.To(session, at, snapToGround: false, loadingScreen: true);
                                    else
                                        Teleport.ToZone(session, at, Terrain.TerrainStore.HomeContinent);
                                    log.Info($"rezone to cavern {at}");
                                }
                            break;
                        case "guildpacket":
                        {
                            // guildpacket <sub hex> <body hex>: handle a GuildBase request as if the player's client sent
                            // it (tests the roster's right-click requests, which can't be driven through client Lua).
                            string[] a = parts[1].Split(' ');
                            foreach (WorldSession session in targets)
                                Guilds.Handle(session, Convert.ToUInt16(a[0], 16), Convert.FromHexString(a.Length > 1 ? a[1] : ""));
                            break;
                        }
                        case "lua":
                        {
                            // lua <Table:Function or Table.Function> [a b c]: run a Lua function in the client
                            // (InteractionRunClientLua) with up to three numbers.
                            string[] a = parts[1].Split(' ');
                            float N(int i) => a.Length > i ? float.Parse(a[i], System.Globalization.CultureInfo.InvariantCulture) : 0f;
                            foreach (WorldSession session in targets)
                                ClientLua.Run(session, a[0], N(1), N(2), N(3));
                            break;
                        }
                        case "spire":
                            // spire: the same as casting the Spire travel ability.
                            foreach (WorldSession session in targets)
                                Spire.TravelTo(session);
                            break;
                        case "creatures":
                            foreach (WorldSession session in targets)
                                log.Info(Creatures.Describe(session));
                            break;
                        case "pinatafor":
                            // pinatafor <item> [item…]: the loot chunk model each resource item drops (LootPinata.txt id).
                            log.Info(string.Join(", ", parts[1].Split(' ').Select(x => $"{x} → {LootPinatas.PinataFor(uint.Parse(x))}")));
                            break;
                        case "attackinfo":
                            // attackinfo [ids]: how weapon casts are classified (kind, damage ×, Energy cost).
                            foreach (string id in (parts.Length > 1 ? parts[1] : "760 816 761 1181 1182 799 801 1049 1050 1051").Split(' '))
                                log.Info($"attack {id}: {WeaponAttacks.KindOf(uint.Parse(id))} ×{WeaponAttacks.DamageMultiplier(uint.Parse(id))} energy {WeaponAttacks.EnergyCost(uint.Parse(id))}");
                            break;
                        case "ambients":
                            // ambients: items whose definition carries an ambient composite effect.
                            log.Info("ambient effects: " + string.Join(", ", Inventory.ItemDefinitionManager.All.Where(d => d.AmbientEffect != 0)
                                .Select(d => $"{d.Id}={d.AmbientEffect}")));
                            break;
                        case "iteminfo":
                            // iteminfo <id>…: model, image set, tint of item definitions.
                            foreach (string id in parts[1].Split(' '))
                                if (Inventory.ItemDefinitionManager.Get(uint.Parse(id)) is { } def)
                                    log.Info($"iteminfo {id}: model '{def.ModelFile}' imageSet {def.ImageSetId} tint '{def.TintAlias}' categories {string.Join(",", def.Categories)}");
                            break;
                        case "hitnear":
                            // hitnear <damage>: the nearest creature takes a hit from this player.
                            foreach (WorldSession session in targets)
                                Creatures.DevHitNearest(session, parts.Length > 1 ? uint.Parse(parts[1]) : 50u, 30f);
                            break;
                        case "slay":
                            // slay [radius]: the nearest creature dies as if this player killed it (loot bag, goals).
                            foreach (WorldSession session in targets)
                                log.Info($"{session.CharacterName}: slay {(Creatures.DevSlayNearest(session, parts.Length > 1 ? float.Parse(parts[1]) : 30f) ? "done" : "nothing near")}.");
                            break;
                        case "clearcreatures":
                            // clearcreatures [radius]: kills creatures near the player (they respawn later).
                            foreach (WorldSession session in targets)
                                log.Info($"{session.CharacterName}: {Creatures.DevKillNear(session, parts.Length > 1 ? float.Parse(parts[1]) : 60f)} creature(s) cleared.");
                            break;
                        case "respawn":
                            // respawn [type]: answers the death window for the player (10 here, 11 safe point).
                            foreach (WorldSession session in targets)
                                Health.HandleRespawn(session, parts.Length > 1 ? uint.Parse(parts[1]) : 11u);
                            break;
                        case "creature":
                        {
                            // creature <kobold|runt|shaman|skeleton|slaug|ogre> [dx dz]: a hostile creature near the player.
                            string[] k = parts.Length > 1 ? parts[1].Split(' ') : new[] { "kobold" };
                            float dx = k.Length > 2 ? float.Parse(k[1]) : 0f, dz = k.Length > 2 ? float.Parse(k[2]) : 6f;
                            foreach (WorldSession session in targets)
                                Creatures.SpawnNear(session, dx, dz, k[0]);
                            break;
                        }
                        case "kobold":
                            // kobold [dx dz]: a hostile kobold next to the player.
                            {
                                string[] k = parts.Length > 1 ? parts[1].Split(' ') : new[] { "0", "6" };
                                foreach (WorldSession session in targets)
                                    Creatures.SpawnNear(session, float.Parse(k[0]), float.Parse(k[1]));
                            }
                            break;
                        case "npc":
                        {
                            // npc <model> [dx dz]: spawn a creature model next to the player (visual test).
                            string[] a = parts[1].Split(' ');
                            foreach (WorldSession session in targets)
                            {
                                var at = session.Position + new System.Numerics.Vector3(a.Length > 2 ? float.Parse(a[1]) : 3f, 0f, a.Length > 2 ? float.Parse(a[2]) : 3f);
                                if (Terrain.TerrainHeights.HeightAt(at.X, at.Z, session.ZoneId) is float gy)
                                    at.Y = gy;
                                session.EnqueueMessage(new Network.Message.Model.PlayerUpdateAddLightweightNpc
                                {
                                    Guid = 0x5000_0000_0000_0000ul | (ulong)Environment.TickCount64,
                                    TransientId = PlayerManager.AllocateTransientId(),
                                    ModelId = uint.Parse(a[0]),
                                    Position = new System.Numerics.Vector4(at, 1f)
                                });
                            }
                            break;
                        }
                        case "lastmove":
                            foreach (WorldSession session in targets)
                                log.Info($"last full movement: {BitConverter.ToString(session.LastFullMovement ?? Array.Empty<byte>())}");
                            break;
                        case "hurt":
                            // hurt <hp>: damage the player (death at 0).
                            foreach (WorldSession session in targets)
                                Health.Damage(session, parts.Length > 1 ? uint.Parse(parts[1]) : 500u);
                            break;
                        case "coins":
                        {
                            // coins <+n|-n|=n>: gives, takes or sets coins.
                            string amount = parts[1].Trim();
                            foreach (WorldSession session in targets)
                            {
                                long delta = amount.StartsWith('=') ? long.Parse(amount[1..]) - session.Coins : long.Parse(amount);
                                log.Info($"{session.CharacterName}: coins {(session.ChangeCoins(delta) ? "now" : "unchanged at")} {session.Coins}.");
                            }
                            break;
                        }
                        case "take":
                        {
                            // take <item> [count]: removes items (all of them without a count).
                            string[] a = parts[1].Split(' ');
                            uint item = uint.Parse(a[0]);
                            foreach (WorldSession session in targets)
                            {
                                uint have = session.Inventory.CountOf(item);
                                uint n = a.Length > 1 ? Math.Min(uint.Parse(a[1]), have) : have;
                                if (n > 0)
                                    session.TakeItem(item, n);
                                log.Info($"{session.CharacterName}: took {n} × item {item}, {session.Inventory.CountOf(item)} left.");
                            }
                            break;
                        }
                        case "drop":
                        {
                            // drop <metres>: teleport straight up without snapping to the ground (fall tests).
                            float h = parts.Length > 1 ? float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 20f;
                            foreach (WorldSession session in targets)
                                Teleport.To(session, session.Position + new System.Numerics.Vector3(0f, h, 0f), snapToGround: false);
                            break;
                        }
                        case "tree":
                        {
                            // tree [dx dz [plant]]: stand next to the nearest standing tree or plant (default 2.5 m on its -x side).
                            string[] a = parts.Length > 1 ? parts[1].Split(' ') : new[] { "-2.5", "0" };
                            foreach (WorldSession session in targets)
                                if (Gathering.NearestStanding(session.Position, a.Length > 2 && a[2] == "plant", 64f, session.ZoneId) is System.Numerics.Vector3 t)
                                    Teleport.To(session, t + new System.Numerics.Vector3(float.Parse(a[0]), 1f, float.Parse(a[1])));
                            break;
                        }
                        case "treefall":
                        {
                            // treefall [u64 u32 u32 u32 u32 u32 f f f f]: PlayerUpdate 0x2D Destroyed for the nearest tree.
                            string[] a = parts.Length > 1 ? parts[1].Split(' ') : Array.Empty<string>();
                            string Arg(int i, string d) => i < a.Length ? a[i] : d;
                            foreach (WorldSession session in targets)
                            {
                                ulong guid = Gathering.NearestStandingGuid(session.Position, 64f, session.ZoneId);
                                var body = new List<byte> { 0x26, 0x00, 0x2D };
                                body.AddRange(BitConverter.GetBytes(guid));
                                body.AddRange(BitConverter.GetBytes(ulong.Parse(Arg(0, "0"))));
                                for (int i = 1; i <= 5; i++)
                                    body.AddRange(BitConverter.GetBytes(uint.Parse(Arg(i, "0"))));
                                for (int i = 6; i <= 9; i++)
                                    body.AddRange(BitConverter.GetBytes(float.Parse(Arg(i, "0"), System.Globalization.CultureInfo.InvariantCulture)));
                                log.Info($"treefall {guid:X}: {BitConverter.ToString(body.ToArray())}");
                                session.SendRaw(body.ToArray());
                            }
                            break;
                        }
                        case "equipped":
                            // equipped: each equipped item with its definition's slot, model and categories.
                            foreach (WorldSession session in targets)
                                foreach (var i in session.Inventory.Items.Where(i => i.Container == Inventory.Inventory.EquippedContainer))
                                {
                                    var d = Inventory.ItemDefinitionManager.Get(i.ItemDefinitionId);
                                    string name = LandmarkEmulator.Shared.GameTable.Text.TextManager.Instance.GetTextForId(d?.NameId ?? 0) ?? "?";
                                    log.Info($"{session.CharacterName} slot {i.Slot}: item {i.ItemDefinitionId} {name} (def slot {d?.EquipSlotId}, model {d?.ModelFile}, tint {d?.TintAlias}, categories {string.Join(",", d?.Categories ?? new())})");
                                }
                            break;
                        case "slotitems":
                        {
                            // slotitems <equip slot id>: item definitions for that equipment slot (1 outfit, 2 main hand,
                            // 3 accessory, 4 feet, 5 hook, 8 chest).
                            uint wantSlot = uint.Parse(parts[1]);
                            foreach (var d in Inventory.ItemDefinitionManager.All.Where(d => d.EquipSlotId == wantSlot).Take(60))
                                log.Info($"slot {wantSlot}: item {d.Id} {LandmarkEmulator.Shared.GameTable.Text.TextManager.Instance.GetTextForId(d.NameId)} model '{d.ModelFile}' tint '{d.TintAlias}'");
                            log.Info($"slot {wantSlot}: {Inventory.ItemDefinitionManager.All.Count(d => d.EquipSlotId == wantSlot)} definitions");
                            break;
                        }
                        case "finditem":
                        {
                            // finditem <text|#id>: item definitions whose name contains the text (as this client names them), or by id.
                            string needle = parts.Length > 1 ? parts[1] : "";
                            foreach (var d in Inventory.ItemDefinitionManager.All)
                            {
                                string name = LandmarkEmulator.Shared.GameTable.Text.TextManager.Instance.GetTextForId(d.NameId) ?? "";
                                // "#38" looks an item up by id.
                                if (needle.StartsWith('#') ? needle[1..] == d.Id.ToString() : name.Contains(needle, StringComparison.OrdinalIgnoreCase))
                                    log.Info($"item {d.Id}: {name} (max stack {d.MaxStack}, categories {string.Join(",", d.Categories)}{(d.PropId != 0 ? $", prop {d.PropId}" : "")}){(d.Abilities.Count > 0 ? " abilities " + string.Join(",", d.Abilities.Select(x => x.AbilityId)) : "")}"
                                        + (parts.Length > 1 && needle.Length > 0 ? $" — {LandmarkEmulator.Shared.GameTable.Text.TextManager.Instance.GetTextForId(d.DescriptionId)}" : ""));
                            }
                            break;
                        }
                        case "shutdown":
                        {
                            // shutdown <seconds> [message] | shutdown cancel: the server shutdown countdown (Game.Shutdown).
                            string[] a = parts.Length > 1 ? parts[1].Split(' ', 2) : Array.Empty<string>();
                            if (a.Length > 0 && a[0] == "cancel")
                                Shutdown.Cancel();
                            else if (a.Length > 0 && int.TryParse(a[0], out int seconds))
                                Shutdown.Schedule(seconds, a.Length > 1 ? a[1] : "");
                            break;
                        }
                        case "psoffer":
                            // psoffer: sends the product-for-review reply now (test tooling; the client asks at login).
                            foreach (WorldSession session in targets)
                                Commerce.OfferReview(session);
                            break;
                        case "psreview":
                        {
                            // psreview <product id> approve|reject: decides a Player Studio product (Game.Commerce).
                            string[] a = parts[1].Split(' ');
                            bool done = Commerce.AdminDecide(ulong.Parse(a[0]), a.Length > 1 && a[1] == "approve");
                            log.Info($"psreview {parts[1]}: {(done ? "done" : "no pending product")}");
                            break;
                        }
                        case "abilitystats":
                        {
                            // abilitystats: the abilities the captured item definitions name (distinct ids, by type and input).
                            var all = Inventory.ItemDefinitionManager.All.SelectMany(d => d.Abilities.Select(a => (d, a))).ToList();
                            var ids = all.Select(x => x.a.AbilityId).Distinct().OrderBy(x => x).ToList();
                            log.Info($"abilitystats: {ids.Count} distinct abilities on {all.Select(x => x.d.Id).Distinct().Count()} items ({all.Count} item-ability links).");
                            foreach (var g in all.GroupBy(x => (x.a.Type, x.a.Input, x.a.Category)).OrderByDescending(g => g.Count()))
                                log.Info($"abilitystats: type {g.Key.Type} input '{g.Key.Input}' category '{g.Key.Category}': {g.Select(x => x.a.AbilityId).Distinct().Count()} abilities, {g.Count()} items");
                            log.Info($"abilitystats ids: {string.Join(",", ids)}");
                            break;
                        }
                        case "whoami":
                            foreach (WorldSession session in targets)
                                log.Info($"{session.CharacterName}: guid 0x{session.PlayerGuid:X}, zone {session.ZoneId}, position {session.Position}, target 0x{session.CurrentTarget:X}");
                            break;
                        case "landscape":
                            // landscape <zone id>: travel there (same as choosing it in the Landscapes window).
                            foreach (WorldSession session in targets)
                                Landscapes.TravelTo(session, uint.Parse(parts[1]));
                            break;
                        case "landscapes":
                            // landscapes: sends the Other Worlds list (the client opens the Travel window on it).
                            foreach (WorldSession session in targets)
                                Landscapes.SendLocations(session);
                            break;
                        case "ensureclaim":
                            // ensureclaim: claimhere only for players without a build site (tests that need one).
                            foreach (WorldSession session in targets)
                                if (!Claims.Owns(session.PlayerGuid))
                                    Claims.DevPlaceHere(session);
                                else
                                    log.Info($"{session.CharacterName} already has a build site.");
                            break;
                        case "claimhere":
                            // claimhere: claims a build site at the player (gives the flag, then request + confirm).
                            foreach (WorldSession session in targets)
                                Claims.DevPlaceHere(session);
                            break;
                        case "sitename":
                            // sitename <name>: renames the player's site (same checks as Build Site Management).
                            foreach (WorldSession session in targets)
                                Claims.DevRename(session, parts[1]);
                            break;
                        case "unclaim":
                            foreach (WorldSession session in targets)
                                Claims.Abandon(session);
                            break;
                        case "matname":
                        {
                            // matname <n>: VoxelMaterials rows with ID n and with ARRAY_INDEX n (name, item).
                            string n = parts[1].Trim();
                            string gameData = LandmarkEmulator.Shared.Configuration.SharedConfiguration.Configuration["GameDataPath"] ?? "gameData";
                            foreach (string row in File.ReadLines(Path.Combine(gameData, "VoxelMaterials.txt")))
                            {
                                string[] f = row.Split('^');
                                if (f.Length < 41 || (f[0] != n && f[1] != n))
                                    continue;
                                string name = LandmarkEmulator.Shared.GameTable.Text.TextManager.Instance.GetTextForId(uint.Parse(f[2])) ?? "?";
                                log.Info($"Material ID {f[0]} index {f[1]}: {name}, item {f[40]}");
                            }
                            break;
                        }
                        case "vertexonly":
                            // vertexonly on|off: keep the vertex-only voxels of client build diffs (round surfaces).
                            Terrain.BlockData.KeepVertexOnly = parts.Length > 1 && parts[1].Trim() == "on";
                            log.Info($"Vertex-only voxels from build diffs: {(Terrain.BlockData.KeepVertexOnly ? "kept" : "dropped")}.");
                            break;
                        case "observed":
                            // observed <operation> [size m]: sends the player a ResponseObserved (another player's edit
                            // effect, Building.SendObserved) for a box of that size 3 m in front of their feet.
                            foreach (WorldSession session in targets)
                            {
                                string[] a = parts.Length > 1 ? parts[1].Trim().Split(' ') : Array.Empty<string>();
                                uint op = a.Length > 0 && uint.TryParse(a[0], out uint o) ? o : 0;
                                double size = a.Length > 1 && double.TryParse(a[1], System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out double m) ? m * 10 : 20;
                                double x = session.Position.X * 10 + 30, y = session.Position.Y * 10, z = session.Position.Z * 10;
                                Building.SendObserved(new[] { session }, op, (x, y, z), (x + size, y + size, z + size));
                                log.Info($"Sent {session.CharacterName} ResponseObserved {op} ({size / 10} m box).");
                            }
                            break;
                        case "lodbuild":
                            // lodbuild: refreshes the coarse copies of the edits on the player's landscape now
                            // (Terrain.TerrainLod; normally automatic once edits have been quiet for 20 s).
                            foreach (WorldSession session in targets)
                            {
                                Terrain.TerrainLod.RefreshNow(session.ZoneId);
                                break;
                            }
                            break;
                        case "tpclaim":
                            // tpclaim: to the middle of the player's own build site.
                            foreach (WorldSession session in targets)
                                if (Claims.ArrivalOf(session.PlayerGuid) is (System.Numerics.Vector3 centre, uint siteZone, ulong siteId))
                                {
                                    StoryTools.ExpectArrival(session, siteId, respawn: false);
                                    var ground = Terrain.TerrainHeights.OnGround(centre.X, centre.Z, siteZone) ?? centre;
                                    Teleport.ToZone(session, ground + new System.Numerics.Vector3(0, 1, 0), siteZone);
                                    log.Info($"{session.CharacterName} to build site at {ground}.");
                                }
                                else
                                    log.Info($"{session.CharacterName} has no build site.");
                            break;
                        case "abilities":
                            // abilities [item ...]: the abilities (id, type, input, category) of item definitions;
                            // without arguments, of every item that has any (to the log).
                            foreach (var d in (parts.Length > 1 ? parts[1].Split(' ').Select(x => Inventory.ItemDefinitionManager.Get(uint.Parse(x)))
                                    : Inventory.ItemDefinitionManager.All.Where(d => d.Abilities.Count > 0)).Where(d => d != null))
                                log.Info($"item {d.Id} {LandmarkEmulator.Shared.GameTable.Text.TextManager.Instance.GetTextForId(d.NameId)}: "
                                    + string.Join("; ", d.Abilities.Select(x => $"{x.AbilityId} type {x.Type} input '{x.Input}' cat '{x.Category}'")));
                            break;
                        case "uievent":
                            // uievent <id>: as if the client reported UIEventTriggered <id> (goal hooks, first-time help).
                            foreach (WorldSession session in targets)
                            {
                                Achievements.UiEvent(session, uint.Parse(parts[1]));
                                FirstTimeEvents.UiEvent(session, uint.Parse(parts[1]));
                            }
                            break;
                        case "portraitcrc":
                            // portraitcrc <crc hex>: as if the client sent AddClientPortraitCrc (tests the upload check).
                            foreach (WorldSession session in targets)
                                Portraits.Announce(session, Convert.ToUInt32(parts[1].Trim(), 16));
                            break;
                        case "portraittest":
                        {
                            // portraittest <character> <Headshot|Portrait> <image set id>: store an exported icon as that
                            // character's image and send it to the player (tests the client's portrait display).
                            string[] a = parts[1].Split(' ');
                            byte[] png = File.ReadAllBytes(GameDataFiles.PathOf(Path.Combine("icons", a[2] + ".png")));
                            Portraits.Store(ulong.Parse(a[0]), a[1], png);
                            foreach (WorldSession session in targets)
                                log.Info($"portraittest {a[0]} {a[1]}: sent {Portraits.Send(session, ulong.Parse(a[0]), a[1])}");
                            break;
                        }
                        case "vendoropen":
                            // vendoropen <vendor guid hex>: as if the player clicked that build site vendor.
                            foreach (WorldSession session in targets)
                                Vendors.Open(session, Convert.ToUInt64(parts[1].Trim(), 16));
                            break;
                        case "storetoken":
                            // storetoken: log the player's Showcase token (tests call the store's web API with it).
                            foreach (WorldSession session in targets)
                                log.Info($"storetoken {session.CharacterName} {Store.TokenOf(session)}");
                            break;
                        case "luacode":
                            // luacode <Lua code>: run a line of Lua in the client (ClientLua.RunCode).
                            foreach (WorldSession session in targets)
                                ClientLua.RunCode(session, parts[1]);
                            break;
                        case "luas":
                            // luas <Table.Method> [arg arg ...]: call a client Lua function with string arguments.
                            foreach (WorldSession session in targets)
                                ClientLua.RunWithArgs(session, parts[1].Split(' ')[0], parts[1].Split(' ').Skip(1).ToArray());
                            break;
                        case "fte":
                            // fte <id>: show a first-time event (if not seen yet); fte clear: forget every seen event.
                            foreach (WorldSession session in targets)
                                if (parts.Length > 1 && parts[1] == "clear")
                                    Unlocks.Clear(session, Unlocks.FirstTimeEventSeen);
                                else
                                    FirstTimeEvents.Trigger(session, uint.Parse(parts[1]));
                            break;
                        case "stream":
                            // stream: why surface objects are (not) streaming to each player.
                            foreach (WorldSession session in targets)
                                log.Info($"{session.CharacterName}: zone {session.ZoneId} at {session.Position}, nostream {NoStreaming}, "
                                    + $"heights {Terrain.TerrainHeights.Has(session.ZoneId)}, arriving {session.IsArriving}; {Creatures.Describe(session)}");
                            break;
                        case "pioneer":
                            // pioneer: the new-player start for an existing character without a site.
                            foreach (WorldSession session in targets)
                                if (Claims.Owns(session.PlayerGuid))
                                    log.Info($"{session.CharacterName} already has a build site.");
                                else if (Pioneer.Place(session.PlayerGuid, session.CharacterName) is (uint zone, System.Numerics.Vector3 at))
                                    Teleport.ToZone(session, at, zone);
                            break;
                        case "claims":
                            foreach (WorldSession session in targets)
                                Claims.SendAll(session);
                            break;
                        case "craft":
                            // craft <recipe> <count>: start a craft at the open station, as the client's Start request.
                            foreach (WorldSession session in targets)
                                Crafting.HandleStart(session, uint.Parse(parts[1].Split(' ')[0]), uint.Parse(parts[1].Split(' ')[1]));
                            break;
                        case "composite":
                            // composite <effect id>: plays that composite effect on the player (as a hit impact does).
                            foreach (WorldSession session in targets)
                                HitEffects.PlayOn(session.PlayerGuid, uint.Parse(parts[1]), new[] { session });
                            break;
                        case "setres":
                            // setres <resource id> <type> <value>: sets one of the player's resource pools on their client
                            // (19 2 = Energy, 13 10 = Stamina, 4 1 = Life), as SetCharacterResource does.
                        {
                            string[] a = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            foreach (WorldSession session in targets)
                                CharacterResources.Send(session, uint.Parse(a[0]), uint.Parse(a[1]), uint.Parse(a[2]));
                        }
                            break;
                        case "claimbasic":
                            // claimbasic: re-sends the landscape's SendAllClaimBasicInfo (as answering 0x19 does).
                            foreach (WorldSession session in targets)
                                Claims.DevSendBasicInfo(session);
                            break;
                        case "beginclaim":
                            // beginclaim: puts the client into build-site placement, as using a Build Site Flag does.
                            foreach (WorldSession session in targets)
                                Claims.BeginPlacement(session);
                            break;
                        case "use":
                            // use <item id>: use one of that item as its ability would (consumables: potions, gadgets).
                            foreach (WorldSession session in targets)
                            {
                                var held = session.Inventory.Items.FirstOrDefault(i => i.ItemDefinitionId == uint.Parse(parts[1]));
                                var ability = held == null ? null : Inventory.ItemDefinitionManager.Get(held.ItemDefinitionId)?.Abilities
                                    .FirstOrDefault(a => Consumables.Handles(a.AbilityId));
                                if (ability != null)
                                    Consumables.TryUse(session, held, ability.AbilityId, session.Position);
                            }
                            break;
                        case "cancelbuff":
                            // cancelbuff <buff name>: as the buff bar's cancel button for that buff.
                            foreach (WorldSession session in targets)
                                Buffs.Cancel(session, Buffs.EffectId(parts[1]));
                            break;
                        case "ratevote":
                            // ratevote <claim id>: as the walk-on window's rate button (AddVote "Claims", the id).
                            foreach (WorldSession session in targets)
                            {
                                var body = new List<byte> { 8 };
                                var w = new LandmarkEmulator.Shared.Network.GamePacketWriter(body);
                                w.Write("Claims"); w.Write(parts[1]); w.Write(""); w.Write(""); w.Write(1f); w.Write(0ul);
                                Ratings.HandleAddVote(session, body.ToArray());
                            }
                            break;
                        case "sitemusic":
                            // sitemusic <ClaimMusic id>: as Build Site Management's music list for the player's own site.
                            foreach (WorldSession session in targets)
                                Claims.SetMusic(session, 0, uint.Parse(parts[1]));
                            break;
                        case "paletteslot":
                            // paletteslot <slot> <type> <value>: as the radial menu's AddEntry.
                            foreach (WorldSession session in targets)
                            {
                                string[] a = parts[1].Split(' ');
                                Palettes.SetSlot(session, uint.Parse(a[0]), uint.Parse(a[1]), ulong.Parse(a[2]));
                            }
                            break;
                        case "game":
                            // game: the site game here (or the player's) and its players; "game rules": the rule sets.
                            foreach (WorldSession session in targets)
                            {
                                string text = parts.Length > 1 && parts[1] == "rules" ? InstancedGames.DescribeRules() : InstancedGames.Describe(session);
                                Chat.System(session, text);
                                log.Info($"game ({session.CharacterName}): {text}");
                            }
                            break;
                        case "gamejoin":
                            foreach (WorldSession session in targets)
                                InstancedGames.Join(session);
                            break;
                        case "gameleave":
                            foreach (WorldSession session in targets)
                                InstancedGames.Leave(session);
                            break;
                        case "gameedit":
                            // gameedit [off]: opens (closes) the client's game editor for the site's game, as the
                            // client's Claims.RequestGainActiveAuthorshipOfGame does.
                            foreach (WorldSession session in targets)
                                InstancedGames.Edit(session, !(parts.Length > 1 && parts[1] == "off"));
                            break;
                        case "gamestart":
                            // gamestart: start the round now (no minimum players, no countdown).
                            foreach (WorldSession session in targets)
                                InstancedGames.DevStart(session);
                            break;
                        case "gametp":
                            InstancedGames.SpawnTeleports = parts.Length < 2 || parts[1] != "off";
                            break;
                        case "story":
                            foreach (WorldSession session in targets)
                            {
                                string text = StoryTools.Describe(session);
                                Chat.System(session, text);
                                log.Info($"story ({session.CharacterName}): {text}");
                            }
                            break;
                        case "storyfire":
                        {
                            // storyfire <prop guid hex> <event id>: raises a prop's event (Play Mode).
                            string[] a = parts[1].Split(' ');
                            foreach (WorldSession session in targets)
                                StoryTools.DevFire(session, Convert.ToUInt64(a[0], 16), uint.Parse(a[1]));
                            break;
                        }
                        case "proptext":
                        {
                            // proptext <prop hex> <text>: as the sign / Story Node text editor.
                            string[] a = parts[1].Split(' ', 2);
                            foreach (WorldSession session in targets)
                                Props.SetText(session, Convert.ToUInt64(a[0], 16), a.Length > 1 ? a[1] : "");
                            break;
                        }
                        case "propname":
                        {
                            // propname <prop hex> <name>: as the prop menu's rename (PropBase 0x19).
                            string[] a = parts[1].Split(' ', 2);
                            foreach (WorldSession session in targets)
                                Props.SetName(session, Convert.ToUInt64(a[0], 16), a.Length > 1 ? a[1] : "");
                            break;
                        }
                        case "propshowtext":
                        {
                            // propshowtext <prop hex> <mode> [seconds]: shows a prop's text as Links would (Props.DisplayText).
                            string[] a = parts[1].Split(' ');
                            Props.DisplayText(Convert.ToUInt64(a[0], 16), uint.Parse(a[1]), a.Length > 2 ? uint.Parse(a[2]) : 5);
                            break;
                        }
                        case "propset":
                        {
                            // propset <prop hex> <setting key> <value>: as the prop's settings window.
                            string[] a = parts[1].Split(' ');
                            PropSettings.Set(Convert.ToUInt64(a[0], 16), a[1], float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture));
                            break;
                        }
                        case "storyfaction":
                        {
                            // storyfaction <creature guid hex> <0 enemy | 1 neutral | 2 ally>: a story creature's faction.
                            string[] f = parts[1].Split(' ');
                            Creatures.StoryFaction(ulong.Parse(f[0], System.Globalization.NumberStyles.HexNumber), int.Parse(f[1]));
                            break;
                        }
                        case "storyclick":
                            foreach (WorldSession session in targets)
                                if (!StoryTools.Click(session, Convert.ToUInt64(parts[1], 16)))
                                    StoryTools.ClickCreature(session, Convert.ToUInt64(parts[1], 16));
                            break;
                        case "playmode":
                            // playmode on|off: as the Play Mode button for the site the player stands on.
                            foreach (WorldSession session in targets)
                                if (Claims.ClaimAt(session.ZoneId, session.Position) is ulong site)
                                    Claims.SetAdventureStatus(session, site, parts.Length > 1 && parts[1] == "off" ? Claims.EditMode : Claims.PlayMode);
                            break;
                        case "gamepoint":
                            foreach (WorldSession session in targets)
                            {
                                string text = InstancedGames.DescribePoints(session);
                                Chat.System(session, text);
                                log.Info($"gamepoint ({session.CharacterName}): {text}");
                            }
                            break;
                        case "sysmsg":
                        {
                            // sysmsg id <text id> [name=value ...] | sysmsg key <key or text> | sysmsg chan7 <text>:
                            // PacketShowSystemMessage / a channel-7 chat line, to compare what the client shows.
                            string[] a = parts[1].Split(' ', 2);
                            foreach (WorldSession session in targets)
                            {
                                if (a[0] == "id")
                                {
                                    string[] f = a[1].Split(' ');
                                    var ps = f.Skip(1).Select(x => x.Split('=', 2)).Where(x => x.Length == 2).Select(x => (x[0], x[1])).ToArray();
                                    SystemMessages.Show(session, uint.Parse(f[0]), ps);
                                }
                                else if (a[0] == "key")
                                    SystemMessages.ShowKey(session, a[1]);
                                else
                                    Chat.SystemChannel(session, a[1]);
                                log.Info($"sysmsg {parts[1]} sent to {session.CharacterName}.");
                            }
                            break;
                        }
                        case "contestphase":
                            // contestphase <id> entries|voting|judging: moves a contest's dates so it is in that phase now.
                            {
                                string[] a = parts[1].Split(' ');
                                log.Info($"contestphase: {Contests.DevSetPhase(uint.Parse(a[0]), a[1])}");
                            }
                            break;
                        case "contests":
                            foreach (WorldSession session in targets)
                            {
                                string text = Contests.Describe();
                                Chat.System(session, text);
                                log.Info($"contests: {text}");
                            }
                            break;
                        case "contestenter":
                        {
                            // contestenter <contest name>: as the Contest window's Enter for the player's site.
                            foreach (WorldSession session in targets)
                                if (Claims.ClaimAt(session.ZoneId, session.Position) is ulong site)
                                {
                                    var body = new List<byte>();
                                    var w = new LandmarkEmulator.Shared.Network.GamePacketWriter(body);
                                    w.Write(site); w.Write(parts.Length > 1 ? parts[1] : "Community Showcase"); w.Write(0u); w.Write((byte)0);
                                    Contests.Submit(session, body.ToArray());
                                }
                            break;
                        }
                        case "gamescore":
                            foreach (WorldSession session in targets)
                                InstancedGames.DevScore(session, int.Parse(parts[1]));
                            break;
                        case "gameend":
                            foreach (WorldSession session in targets)
                                InstancedGames.DevEnd(session);
                            break;
                        case "itemstats":
                            // itemstats on|off: tool stats in item records (from the next item sent / login).
                            Inventory.Inventory.SendItemStats = parts.Length > 1 && parts[1] == "on";
                            break;
                        case "cancelcraft":
                            // cancelcraft: as the client's RecipeBase 3 CancelRequest.
                            foreach (WorldSession session in targets)
                                Crafting.Cancel(session);
                            break;
                        case "station":
                            foreach (WorldSession session in targets)
                                CraftingStations.OpenWindow(session, uint.Parse(parts[1]));
                            break;
                        case "chestopen":
                        case "chestput":
                        case "chestpickup":
                        {
                            // chestopen: open the nearest chest (EnableWindow, as a click does).
                            // chestput <item> <n>: put items into the nearest chest.
                            string[] a = parts.Length > 1 ? parts[1].Split(' ') : Array.Empty<string>();
                            foreach (WorldSession session in targets)
                            {
                                ulong chest = Props.NearestChest(session);
                                if (chest == 0)
                                    log.Info($"{session.CharacterName}: no chest nearby.");
                                else if (parts[0] == "chestpickup")
                                    Props.PickUp(session, chest, false);
                                else if (parts[0] == "chestopen")
                                    Chests.Interact(session, chest);
                                else
                                    Chests.DevPut(chest, uint.Parse(a[0]), uint.Parse(a[1]));
                            }
                            break;
                        }
                        case "casterinfo":
                            // casterinfo <ability>: sends that item ability's CasterInfoReply unasked (what the client
                            // gets when it asks because the item is held), e.g. to check the sickle's arc in a test.
                            if (Inventory.ItemDefinitionManager.FindAbility(uint.Parse(parts[1])) is { } src)
                                foreach (WorldSession session in targets)
                                    session.EnqueueMessage(Abilities.BuildItemAbilityInfo(src.Item, src.Ability));
                            break;
                        case "clearprops":
                            // clearprops: removes all of the player's props, no refund (test clean-up).
                            foreach (WorldSession session in targets)
                                log.Info($"{session.CharacterName}: {Props.DevClear(session)} prop(s) cleared.");
                            break;
                        case "beginprop":
                            // beginprop <prop id>: starts a placement as if picked from the props palette;
                            // beginprop #<item id>: as if placing that prop item from the bag (the player needs one).
                            foreach (WorldSession session in targets)
                                if (parts[1].StartsWith('#'))
                                {
                                    var item = session.Inventory.Items.FirstOrDefault(i => i.ItemDefinitionId == uint.Parse(parts[1][1..]));
                                    uint prop = Inventory.ItemDefinitionManager.Get(uint.Parse(parts[1][1..]))?.PropId ?? 0;
                                    if (item != null && prop != 0)
                                        Props.BeginPlacement(session, prop, item.ItemDefinitionId, item.Id);
                                }
                                else
                                    Props.BeginPlacement(session, uint.Parse(parts[1]));
                            break;
                        case "anim":
                            // anim <AnimationSelection id> [dx dz]: play an animation on the player (emote testing),
                            // turning them towards their position + (dx, 0, dz).
                            {
                                string[] a = parts[1].Split(' ');
                                foreach (WorldSession session in targets)
                                    Emotes.PlayAnim(session, uint.Parse(a[0]), a.Length >= 3
                                        ? session.Position + new System.Numerics.Vector3(float.Parse(a[1]), 0f, float.Parse(a[2]))
                                        : null);
                            }
                            break;
                        case "outpost":
                        {
                            // outpost <n>: go to the n-th outpost (Chaos Caverns), next to its middle.
                            int n = int.Parse(parts[1]);
                            if (n < Outposts.All.Count)
                                foreach (WorldSession session in targets)
                                {
                                    var o = Outposts.All[n];
                                    Teleport.To(session, o.Floor + new System.Numerics.Vector3(0f, 1f, 0f), snapToGround: false, loadingScreen: true);
                                    log.Info($"outpost {n}: {o.Name} (cavern {o.Cavern}, {Outposts.GarrisonName(o)}) at {o.Floor}");
                                }
                            break;
                        }
                        case "outpostuse":
                        {
                            // outpostuse <n> chest|crystal: as if the player clicked that outpost's Lost Chest / Lost Crystal.
                            string[] a = parts[1].Split(' ');
                            var o = Outposts.All[int.Parse(a[0])];
                            foreach (WorldSession session in targets)
                                if (a[1] == "chest")
                                    Outposts.OpenChest(session, o.Chest.Guid);
                                else
                                    Outposts.UseCrystal(session, o.Crystal.Guid);
                            break;
                        }
                        case "gc":
                            // gc: full compacting garbage collection (memory measurements), then the heap size.
                            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                            GC.Collect(2, GCCollectionMode.Forced, true, true);
                            log.Info($"gc: heap {GC.GetTotalMemory(false) / (1024 * 1024)} MB, {Terrain.TerrainEdits.MemoryReport()}");
                            break;
                        case "harvestfail":
                            // harvestfail <reason> [resource]: the client's harvest failure message (Gathering.HarvestFail).
                            foreach (WorldSession session in targets)
                            {
                                string[] a = parts[1].Split(' ');
                                Gathering.HarvestFail(session, uint.Parse(a[0]), a.Length > 1 ? uint.Parse(a[1]) : 0);
                            }
                            break;
                        case "lootbag":
                        {
                            // lootbag <item> [count]: a loot bag at the player's feet (pickup → loot toast).
                            string[] a = parts[1].Split(' ');
                            foreach (WorldSession session in targets)
                                LootPinatas.SpawnBag(session, session.Position + new System.Numerics.Vector3(1f, 0.5f, 0f),
                                    new[] { (uint.Parse(a[0]), a.Length > 1 ? uint.Parse(a[1]) : 1u) });
                            break;
                        }
                        case "chest":
                            // chest [go|open]: the nearest treasure chest; "go" walks there, "open" opens it.
                            foreach (WorldSession session in targets)
                                if (LootChests.Nearest(session.Position) is (int i, float d, ulong g))
                                {
                                    log.Info($"chest {i} at {LootChests.PositionOf(i)}, {d:F0} m");
                                    if (parts.Length > 1 && parts[1] == "go")
                                        Teleport.To(session, LootChests.PositionOf(i) + new System.Numerics.Vector3(2f, 1f, 0f), snapToGround: false, loadingScreen: true);
                                    else if (parts.Length > 1 && parts[1] == "open")
                                        LootChests.OpenChest(session, g);
                                }
                            break;
                        case "terrainmem":
                            // terrainmem: edited terrain cells held, how many expanded, compact bytes.
                            log.Info(Terrain.TerrainEdits.MemoryReport());
                            break;
                        case "terminal":
                        {
                            // terminal <hub | c<index> | hex node> [open 0/1]: TeleportBase ActivateTerminal, as if
                            // the player used that portal crystal.
                            string[] a = parts[1].Split(' ');
                            ulong node = a[0] == "hub" ? WarpNetwork.HubNode
                                : a[0].StartsWith('c') ? WarpNetwork.CrystalNode(int.Parse(a[0][1..]))
                                : Convert.ToUInt64(a[0], 16);
                            foreach (WorldSession session in targets)
                                WarpNetwork.ActivateTerminal(session, node, a.Length > 1 && a[1] == "1");
                            log.Info($"terminal {a[0]} = node {node:X}");
                            break;
                        }
                        case "grantaccess":
                        {
                            // grantaccess <player> <hex mask>: Special Access for a player on this player's own site
                            // (0 removes it; 0x11F = Ultimate).
                            string[] a = parts[1].Split(' ');
                            foreach (WorldSession session in targets)
                                log.Info($"{session.CharacterName}: grantaccess {a[0]} " +
                                    (Claims.DevGrant(session, a[0], Convert.ToUInt32(a[1], 16)) ? "done" : "no site"));
                            break;
                        }
                        case "ownblock":
                        {
                            // ownblock [material] [edge voxels]: a cube built 3 m east of the player as the player's own
                            // voxels (the paste path: overlay + Voxel Vision ownership), sent to everyone in range.
                            string[] a = parts.Length > 1 ? parts[1].Split(' ') : new string[0];
                            ushort material = a.Length > 0 ? ushort.Parse(a[0]) : (ushort)79;
                            int edge = a.Length > 1 ? int.Parse(a[1]) : 4;
                            foreach (WorldSession session in targets)
                            {
                                (double gx, double gy, double gz) = Terrain.TerrainEdits.ToGlobal(session.Position + new System.Numerics.Vector3(3f, 0.5f, 0f));
                                double size = Terrain.TerrainEdits.VoxelSize;
                                long vx0 = (long)Math.Floor(gx / size), vy0 = (long)Math.Floor(gy / size), vz0 = (long)Math.Floor(gz / size);
                                var byCell = new Dictionary<ulong, List<Terrain.TerrainEdits.VoxelValue>>();
                                for (long vx = vx0; vx < vx0 + edge; vx++)
                                    for (long vy = vy0; vy < vy0 + edge; vy++)
                                        for (long vz = vz0; vz < vz0 + edge; vz++)
                                        {
                                            const int S = Terrain.BlockData.Size;
                                            ulong cell = Terrain.TerrainStore.CellId(Terrain.TerrainEdits.Level, (uint)(vx / S), (uint)(vy / S), (uint)(vz / S));
                                            int index = Terrain.TerrainEdits.VoxelIndex((int)(vx % S), (int)(vy % S), (int)(vz % S));
                                            if (!byCell.TryGetValue(cell, out var list))
                                                byCell[cell] = list = new List<Terrain.TerrainEdits.VoxelValue>();
                                            byte c = Terrain.BlockData.CentreOffset;
                                            list.Add(new Terrain.TerrainEdits.VoxelValue(index, 3, material, c, c, c));
                                        }
                                foreach ((ulong cell, var list) in byCell)
                                    Terrain.TerrainEdits.Write(cell, list, session.ZoneId, session.PlayerGuid);
                                Terrain.TerrainEdits.Broadcast(byCell.Keys.ToList(), session.ZoneId);
                                foreach (ulong cell in byCell.Keys)
                                    log.Info($"{session.CharacterName}: ownblock cell {cell:X16}: " + string.Join(", ",
                                        Terrain.VoxelOwnership.Census(session.ZoneId, cell).Select(kv => $"{kv.Key} owns {kv.Value}")));
                            }
                            break;
                        }
                        case "clearcell":
                            // clearcell <hex cell id>: drop every override in one terrain cell on the player's landscape.
                            foreach (WorldSession session in targets)
                                Terrain.TerrainEdits.ClearCell(Convert.ToUInt64(parts[1], 16), session.ZoneId);
                            break;
                        case "weapons":
                            // weapons: item definitions with abilities that aren't gathering tools (what HeldWeapon accepts).
                            foreach (var d in Inventory.ItemDefinitionManager.All.Where(d => d.EquipSlotId == 2
                                         && !d.Abilities.Any(x => Abilities.IsHarvestAbility(x.AbilityId))))
                                log.Info($"weapon? item {d.Id} {LandmarkEmulator.Shared.GameTable.Text.TextManager.Instance.GetTextForId(d.NameId)} slot {d.EquipSlotId} model {d.ModelFile} categories {string.Join(",", d.Categories)} abilities {string.Join(",", d.Abilities.Select(x => x.AbilityId))}");
                            break;
                        case "propremove":
                            // propremove <guid>: delete a placed prop (no player needed, no refund).
                            Props.DevRemove(ulong.Parse(parts[1]));
                            break;
                        case "proptint":
                        {
                            // proptint <guid> [alias]: set or clear a placed prop's tint (no player needed).
                            string[] a = parts[1].Split(' ');
                            Props.DevSetTint(ulong.Parse(a[0]), a.Length > 1 ? a[1] : "");
                            break;
                        }
                        case "propdelete":
                            // propdelete <guid>: delete one of the player's placed props (cleans up after tests).
                            foreach (WorldSession session in targets)
                                Props.PickUp(session, ulong.Parse(parts[1]), true);
                            break;
                        case "propenable":
                        {
                            // propenable <guid hex> <0|1>: switch a placed prop's light/effect as if from the prop menu.
                            string[] a = parts[1].Split(' ');
                            foreach (WorldSession session in targets)
                                Props.SetEnabled(session, Convert.ToUInt64(a[0], 16), a.Length < 2 || a[1] != "0");
                            break;
                        }
                        case "durabilityof":
                            // durabilityof <item def>: wear and condition of the player's items of that kind.
                            foreach (WorldSession session in targets)
                                foreach (var item in session.Inventory.Items.Where(i => i.ItemDefinitionId == uint.Parse(parts[1])))
                                    log.Info($"{session.CharacterName}: item {item.ItemDefinitionId} wear {item.Wear:0} condition {Durability.Condition(item)}%");
                            break;
                        case "durability":
                        {
                            // durability <item def> <value>: sends a carried item's durability field (test).
                            string[] a = parts[1].Split(' ');
                            foreach (WorldSession session in targets)
                                foreach (var item in session.Inventory.Items.Where(i => i.ItemDefinitionId == uint.Parse(a[0])))
                                    session.EnqueueMessage(new Network.Message.Model.ClientUpdateItemUpdate
                                    {
                                        Guid = item.Id, Count = item.Count, Slot = item.Slot, Container = item.Container,
                                        Durability = uint.Parse(a[1])
                                    });
                            break;
                        }
                        case "resettitles":
                            // resettitles: forget every title the player owns or shows (test).
                            foreach (WorldSession session in targets)
                                Titles.DevReset(session);
                            break;
                        case "veins":
                        {
                            // veins <radius>: shows every vein within the radius (the Holoseeker's overlay); veins 0 clears it.
                            foreach (WorldSession session in targets)
                                if (float.Parse(parts[1]) > 0)
                                    log.Info($"{Terrain.VeinOverlay.Send(session, Terrain.VeinOverlay.Holoseeker, float.Parse(parts[1]))} vein cells shown.");
                                else
                                    Terrain.VeinOverlay.End(session, Terrain.VeinOverlay.Holoseeker);
                            break;
                        }
                        case "calcstats":
                            // calcstats: logs the calculated stats the server sends the player (non-default ones).
                            foreach (WorldSession session in targets)
                                log.Info($"{session.CharacterName} stats: " + string.Join(" ", Abilities.CalculatedStats(session)
                                    .Where(s => s.Value != 0).Select(s => $"{s.StatId}={s.Value:0.###}")));
                            break;
                        case "npcmode":
                            // npcmode <n>: creature spawn control mode for later spawns (0 = client ignores positions).
                            Creatures.ControlMode = sbyte.Parse(parts[1]);
                            log.Info($"Creature control mode {Creatures.ControlMode}.");
                            break;
                        case "exportdesign":
                            // exportdesign <design guid>: as the Designs window's export to item (TemplateBase 0x1B).
                            foreach (WorldSession session in targets)
                                DesignItems.Export(session, ulong.Parse(parts[1]));
                            break;
                        case "regendelay":
                            // regendelay <seconds>: terrain regeneration delay (default 1200), checked again now.
                            Terrain.TerrainEdits.SetRegenerationDelay(TimeSpan.FromSeconds(double.Parse(parts[1])));
                            break;
                        case "caveair":
                        {
                            // caveair <gx gy gz> (global dm): is the client's generated world cave air there?
                            string[] c = parts[1].Split(' ');
                            log.Info($"caveair {parts[1]}: {Terrain.CavePieces.IsAir(double.Parse(c[0]), double.Parse(c[1]), double.Parse(c[2]))}");
                            break;
                        }
                        case "veintest":
                        {
                            // veintest: the vein port against fixed inputs (compare with the Python reference).
                            var defs = Terrain.VeinGenerator.LoadDefinitions();
                            foreach (uint vid in new uint[] { 233, 239, 246 })
                            {
                                var d = defs[vid];
                                var pts = Terrain.VeinGenerator.Walk(d, (25584000.0 + 1234.0, 30000.0 + 77.0, 25564500.0 + 555.0), (0.2, -0.9, 0.1), 987654321u);
                                var cells = Terrain.VeinGenerator.CellsOf(d, pts);
                                long vox = 0, chk = 0;
                                foreach (var c in cells)
                                    foreach (var ((x, y, z), m) in Terrain.VeinGenerator.ApplyCell(d, pts, c, (_, _, _) => true))
                                    {
                                        vox++;
                                        chk += x * 7 + y * 13 + z * 31 + m;
                                    }
                                var last = pts[^1];
                                log.Info($"veintest {vid} {d.Name} pts {pts.Count} last ({last.X:F3}, {last.Y:F3}, {last.Z:F3}, {last.Width:F3}) {last.Seed} cells {cells.Count} vox {vox} chk {chk}");
                            }
                            var g = new Terrain.VeinGenerator.SpawnRandom(12345);
                            log.Info($"veintest rng {g.Next():F12} {g.Next():F12} {g.Next():F12}");
                            break;
                        }
                        case "nearvein":
                            // nearvein [radius]: the nearest ore veins (kind, nearest point, distance); "nearvein go" stands
                            // the player 2 m from the nearest one.
                            foreach (WorldSession session in targets)
                            {
                                var near = Terrain.OreVeins.Near(session.Position, parts.Length > 1 && parts[1] == "go" ? 400f : 80f, session.ZoneId);
                                foreach (var (vein, at, distance) in near.Take(6))
                                    log.Info($"vein {vein.Info.Definition.Name} ({vein.Points.Count} points, {vein.Cells.Count} cells) at {at} {distance:F1} m");
                                if (parts.Length > 1 && parts[1] == "go" && near.FirstOrDefault(n => !n.Vein.Cave) is { Vein: not null } surface)
                                {
                                    // Stand on the ground 3 m from where the vein comes out of it.
                                    var p0 = surface.Vein.Points[0];
                                    var origin = Terrain.TerrainStore.ZoneOrigin;
                                    var start = new System.Numerics.Vector3((float)((p0.X - origin.X) / 10), (float)((p0.Y - origin.Y) / 10), (float)((p0.Z - origin.Z) / 10));
                                    Teleport.To(session, start + new System.Numerics.Vector3(0, 2f, 3f));
                                    log.Info($"nearvein go: {surface.Vein.Info.Definition.Name} starts at {start}");
                                }
                            }
                            break;
                        case "lapsesite":
                            // lapsesite: the player's build site lapses now (upkeep run out): reverted, props packed.
                            foreach (WorldSession session in targets)
                                Claims.DevLapse(session);
                            break;
                        case "fxat":
                            // fxat <effect> [metres] [ms]: plays a composite effect on the ground in front of the player (TargetLocation).
                            foreach (WorldSession session in targets)
                            {
                                string[] f = parts[1].Split(' ');
                                float dist = f.Length > 1 ? float.Parse(f[1]) : 6f;
                                var fwd = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitZ, session.Facing);
                                fwd.Y = 0;
                                fwd = fwd.LengthSquared() > 0.01f ? System.Numerics.Vector3.Normalize(fwd) : System.Numerics.Vector3.UnitZ;
                                var at = session.Position + fwd * dist;
                                HitEffects.PlayAt(at, uint.Parse(f[0]), new[] { session }, f.Length > 2 ? uint.Parse(f[2]) : 0);
                                log.Info($"{session.CharacterName} fxat {f[0]} at {at}.");
                            }
                            break;
                        case "sortbag":
                            // sortbag: reverses the bag's order the way the window's sort sends it (RearrangeItemSlots).
                            foreach (WorldSession session in targets)
                            {
                                var bag = session.Inventory.Items.Where(i => i.Container == Inventory.Inventory.BagContainer).OrderBy(i => i.Slot).ToList();
                                var order = bag.Select((item, k) => ((uint)(bag.Count - 1 - k), item.Id)).ToList();
                                var changed = session.Inventory.Rearrange(Inventory.Inventory.BagContainer, order, session.Inventory.BagSize);
                                foreach (var item in changed)
                                    session.SendItem(item, replace: true);
                                log.Info($"{session.CharacterName} sortbag: {bag.Count} items, {changed.Count} moved; first now {session.Inventory.At(0, 0)?.ItemDefinitionId}.");
                            }
                            break;
                        case "guildof":
                            // guildof: the player's guild id (tests pass it to the guild window's Lua calls).
                            foreach (WorldSession session in targets)
                                log.Info($"{session.CharacterName} guild {Guilds.GuildIdOf(session.PlayerGuid)}");
                            break;
                        case "vaultnear":
                            // vaultnear: uses the nearest vault prop within 6 m (as clicking it does).
                            foreach (WorldSession session in targets)
                            {
                                var near = Props.PlacedNear(session, 6f).FirstOrDefault(pp => Vaults.IsVaultProp(pp.PropId));
                                if (near != null)
                                    Vaults.Open(session, near.Spawn.Guid);
                                else
                                    log.Info($"{session.CharacterName}: no vault prop near.");
                            }
                            break;
                        case "vault":
                            // vault [variant]: opens the vault window (Command 0x1E sub 0x50 InteractionVault {…, u64 entity}).
                            foreach (WorldSession session in targets)
                            {
                                var output = new List<byte>();
                                var w = new LandmarkEmulator.Shared.Network.GamePacketWriter(output);
                                w.Write((ushort)0x50);
                                if (parts.Length < 2 || parts[1] != "a")
                                    w.Write((ushort)0);
                                w.Write(session.PlayerGuid);
                                session.EnqueueMessage(Network.Message.ZoneMessageOpcode.CommandBase, output.ToArray());
                            }
                            break;
                        case "revive":
                            // revive: a dead player is alive again with full health where they stand (tests).
                            foreach (WorldSession session in targets)
                                Health.DevRevive(session);
                            break;
                        case "reviveby":
                            // reviveby <dead player>: as if the player cast the revive (246) at them (the 0.589 UI
                            // can't reach that cast; see Health.ReviveBy).
                            if (parts.Length >= 2)
                                foreach (WorldSession session in targets)
                                    Health.ReviveBy(session, PlayerManager.Snapshot().FirstOrDefault(p =>
                                        string.Equals(p.CharacterName, parts[1], StringComparison.OrdinalIgnoreCase)));
                            break;
                        case "god":
                            // god on|off: the player takes no damage (tests with hostile creatures).
                            foreach (WorldSession session in targets)
                                lock (Health.Invulnerable)
                                    if (parts.Length > 1 && parts[1] == "off") Health.Invulnerable.Remove(session);
                                    else Health.Invulnerable.Add(session);
                            break;
                        case "wield":
                            // wield <item>: gives the item if missing and equips it (weapons: main hand).
                            foreach (WorldSession session in targets)
                            {
                                uint id = uint.Parse(parts[1]);
                                if (session.Inventory.CountOf(id) == 0)
                                    session.GiveItem(id, 1);
                                var item = session.Inventory.Items.FirstOrDefault(i => i.ItemDefinitionId == id);
                                if (item == null || item.Container == Inventory.Inventory.EquippedContainer)
                                    continue;
                                var before = session.VisibleEquipment();
                                var held = Inventory.ItemDefinitionManager.Get(id) is { } def && Inventory.Inventory.EquippedSlotFor(def.EquipSlotId) is uint slot
                                    ? session.Inventory.At(Inventory.Inventory.EquippedContainer, slot) : null;
                                var moved = new List<LandmarkEmulator.Database.Character.Model.CharacterItemModel>();
                                if (held != null)
                                    moved.AddRange(session.Inventory.ToggleEquip(held));
                                moved.AddRange(session.Inventory.ToggleEquip(item));
                                foreach (var changed in moved)
                                    session.SendItem(changed, replace: true);
                                session.SendEquipmentChanges(before);
                                log.Info($"{session.CharacterName} wields {id}.");
                            }
                            break;
                        case "weaponattack":
                            // weaponattack basic|power [times]: the held weapon's attack at the nearest creature (Combat.WeaponCombat).
                            foreach (WorldSession session in targets)
                            {
                                string[] w = parts[1].Split(' ');
                                int times = w.Length > 1 ? int.Parse(w[1]) : 1;
                                for (int i = 0; i < times; i++)
                                    Combat.WeaponCombat.DevAttack(session, w[0] == "power");
                            }
                            break;
                        case "states":
                            // states: combat states on the player and the creatures within 25 m.
                            foreach (WorldSession session in targets)
                                log.Info($"{session.CharacterName} states: {Combat.WeaponCombat.DevStates(session)}");
                            break;
                        case "combatgear":
                            // combatgear: CombatGear.txt — every weapon / combat item with its abilities (id, type, input, category).
                            {
                                var gearLines = new List<string>();
                                foreach (var d in Inventory.ItemDefinitionManager.All.Where(d => d.EquipSlotId is 2 or 3 or 8
                                    && !d.Abilities.Any(a => Abilities.IsHarvestAbility(a.AbilityId))).OrderBy(d => d.Id))
                                    gearLines.Add($"{d.Id}	{d.NameId}	{d.DescriptionId}	{d.EquipSlotId}	{d.Quality}	"
                                        + $"{LandmarkEmulator.Shared.GameTable.Text.TextManager.Instance.GetTextForId(d.NameId)}	"
                                        + string.Join(" ", d.Abilities.Select(a => $"{a.AbilityId}/{a.Type}/{a.Input}/{a.Category}"))
                                        + $"	{d.ModelFile}	{WeaponAttacks.ClassOf(d)}	{string.Join(",", d.Categories)}");
                                File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "CombatGear.txt"), gearLines);
                                log.Info($"Combat gear: {gearLines.Count} items.");
                            }
                            break;
                        case "itemaudit":
                            // itemaudit: ItemAudit.txt (what each item does on this server; unhandled abilities first).
                            log.Info("Item audit: " + ItemAudit.Run(Path.Combine(AppContext.BaseDirectory, "ItemAudit.txt")));
                            break;
                        case "placedesign":
                            // placedesign <design guid>: places the design 2 m in front of the player (test of design paste).
                            foreach (WorldSession session in targets)
                                log.Info($"{session.CharacterName} placedesign {parts[1]}: " +
                                    (Building.PlaceDesign(session, ulong.Parse(parts[1]), session.Position + new System.Numerics.Vector3(0, 0, 2)) ? "placed" : "refused"));
                            break;
                        case "clearunlocks":
                            // clearunlocks <kind>…: forget the player's unlocks of those kinds (Unlocks.*; test).
                            foreach (WorldSession session in targets)
                                Unlocks.Clear(session, parts[1].Split(' ').Select(uint.Parse).ToArray());
                            break;
                        case "propcost":
                            // propcost <prop id>: what placing the prop costs this player.
                            foreach (WorldSession session in targets)
                                log.Info(Props.DescribeCost(session, uint.Parse(parts[1])));
                            break;
                        case "propmove":
                        {
                            // propmove <guid hex> dx dy dz: opens an edit session on a placed prop (PropAccess) and saves
                            // it moved by the offset, as the placement gizmo does (tests prop-move undo).
                            string[] a = parts[1].Split(' ');
                            ulong guid = Convert.ToUInt64(a[0], 16);
                            foreach (WorldSession session in targets)
                            {
                                Props.PlacedProp prop = Props.Find(guid);
                                if (prop == null)
                                    break;
                                Props.Access(session, guid);
                                var body = new List<byte>();
                                var w = new LandmarkEmulator.Shared.Network.GamePacketWriter(body);
                                w.Write(0u);
                                w.Write((byte)0);
                                w.Write(guid);
                                w.Write(0u);
                                var to = prop.Spawn.Position + new System.Numerics.Vector4(float.Parse(a[1]), float.Parse(a[2]), float.Parse(a[3]), 0f);
                                foreach (var v in new[] { to, prop.Spawn.Rotation, prop.Spawn.Scale, prop.Spawn.Position, prop.Spawn.Rotation, prop.Spawn.Scale })
                                {
                                    w.Write(v.X);
                                    w.Write(v.Y);
                                    w.Write(v.Z);
                                    w.Write(v.W);
                                }
                                w.Write((byte)0);
                                w.Write(0ul);
                                w.Write((byte)0);
                                Props.Save(session, body.ToArray());
                            }
                            break;
                        }
                        case "giveprop":
                            // giveprop <prop id>: one item that places that prop.
                            foreach (WorldSession session in targets)
                                if (Inventory.ItemDefinitionManager.ItemsForProp(uint.Parse(parts[1])).FirstOrDefault() is { } item)
                                {
                                    session.GiveItem(item.Id, 1);
                                    log.Info($"Gave {session.CharacterName} item {item.Id} for prop {parts[1]}.");
                                }
                            break;
                        case "copyhere":
                            // copyhere <voxels>: copies the props in a box of that many voxels around the player (Building.DevCopy).
                            foreach (WorldSession session in targets)
                                log.Info($"{session.CharacterName}'s clipboard holds {Building.DevCopy(session, uint.Parse(parts[1]))} prop(s).");
                            break;
                        case "pasteat":
                        {
                            // pasteat dx dy dz: pastes the clipboard with its corner at that offset from the player.
                            string[] a = parts[1].Split(' ');
                            foreach (WorldSession session in targets)
                                Building.DevPaste(session, session.Position + new System.Numerics.Vector3(float.Parse(a[0]), float.Parse(a[1]), float.Parse(a[2])));
                            break;
                        }
                        case "undo":
                        case "redo":
                            // undo / redo: as Ctrl+Z / Ctrl+Y (VoxelConstruction sub 3).
                            foreach (WorldSession session in targets)
                                Building.Handle(session, Building.UndoRedoSub, BitConverter.GetBytes(parts[0] == "redo" ? 1u : 0u));
                            break;
                        case "prop":
                            string[] p = parts[1].Split(' ');
                            var offset = p.Length >= 4
                                ? new System.Numerics.Vector3(float.Parse(p[1]), float.Parse(p[2]), float.Parse(p[3]))
                                : new System.Numerics.Vector3(2f, 0f, 0f);
                            foreach (WorldSession session in targets)
                                log.Info($"dev prop {p[0]} placed as 0x{Props.PlaceNear(session, uint.Parse(p[0]), offset):X}.");
                            break;
                        default:
                            log.Warn($"Unknown dev command: {line}");
                            continue;
                    }
                    log.Info($"Dev command run for {targets.Count} player(s): {line}");
                }
                catch (Exception exception)
                {
                    log.Error(exception, $"Dev command failed: {line}");
                }
            }
        }
    }
}
