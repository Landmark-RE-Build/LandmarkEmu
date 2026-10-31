using System.Globalization;
using LandmarkEmulator.WorldServer.Game.WorldChannel;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Developer commands read from a text file next to the server (dev_commands.txt), so packets and items can be tried
    /// on live clients without restarting. Each line is run once against every player in the world, then the file is
    /// emptied. Local file only; nothing is exposed on the network (the admin page queues the same commands for admins,
    /// <see cref="Enqueue"/>). Every command has a usage line (DevCommands.Usage.cs): a missing or unreadable argument
    /// is answered "usage: …" in the log (and to the caller, when known) instead of failing.
    /// <list type="bullet">
    /// <item><c>give &lt;itemDefinitionId&gt; &lt;count&gt;</c></item>
    /// <item><c>send &lt;hex bytes&gt;</c>: raw zone packet, opcode bytes included (e.g. <c>9A 00 05 ...</c>)</item>
    /// <item><c>prop &lt;propId&gt; [dx dy dz]</c>: places that prop next to each player</item>
    /// <item><c>station &lt;stationId&gt;</c>: opens that station's crafting window, wherever the player is</item>
    /// </list>
    /// </summary>
    public static partial class DevCommands
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        /// <summary>
        /// Internal test switch, not an owner setting: no trees, plants or creatures are streamed (environment
        /// LANDMARK_NOSTREAM=1, read once at start; terrain-only client tests). Documented as internal in the wiki generator.
        /// </summary>
        public static readonly bool NoStreaming = System.Environment.GetEnvironmentVariable("LANDMARK_NOSTREAM") == "1";

        private static readonly string path = Path.Combine(AppContext.BaseDirectory, "dev_commands.txt");
        private static DateTime lastCheck = DateTime.MinValue;

        /// <summary>
        /// Where a command line came from, for the replies and the log: <see cref="Name"/> is the channel
        /// ("dev_commands.txt", "admin page"); <see cref="Caller"/> the player who ran it, when known (the admin page's
        /// admin, once <see cref="Admin.Run"/> passes it to <see cref="Enqueue"/>). With a caller, replies (usage lines,
        /// refusals) also go to that player as a system line; they always go to the log, the channel every command
        /// answers on.
        /// </summary>
        public sealed record Source(string Name, WorldSession Caller = null)
        {
            public override string ToString() => Caller != null ? $"{Caller.CharacterName} ({Name})" : Name;
        }

        /// <summary>The local file: written by the server owner or the test harness on the server PC, no caller.</summary>
        public static readonly Source FileSource = new("dev_commands.txt");

        /// <summary>Commands from the admin page (<see cref="Admin"/>), run with the file's on the next tick.</summary>
        private static readonly System.Collections.Concurrent.ConcurrentQueue<(string Line, Source From)> queued = new();

        /// <summary>
        /// Queues one command line ("@Name command" for one player, else everyone). <paramref name="caller"/>: the admin who
        /// ran it (replies go to them; the client-code commands may always target them).
        /// </summary>
        public static void Enqueue(string line, WorldSession caller = null, string source = "admin page")
            => queued.Enqueue((line, new Source(source, caller)));

        /// <summary>
        /// Runs the commands written to dev_commands.txt (checked at most once a second, then emptied) and the queued ones
        /// for every player online. One command per line; lines starting with # are ignored.
        /// </summary>
        public static void Update(IEnumerable<WorldSession> players)
        {
            var batch = new List<(string Line, Source From)>();
            while (queued.TryDequeue(out var q))
                batch.Add(q);
            if (batch.Count == 0 && (DateTime.UtcNow - lastCheck).TotalSeconds < 1)
                return;
            if ((DateTime.UtcNow - lastCheck).TotalSeconds >= 1)
            {
                lastCheck = DateTime.UtcNow;
                if (File.Exists(path))
                {
                    try
                    {
                        string[] lines = File.ReadAllLines(path);
                        if (lines.Length > 0)
                            File.WriteAllText(path, "");
                        batch.InsertRange(0, lines.Select(l => (l, FileSource)));
                    }
                    catch (IOException)
                    {
                    }
                }
            }
            if (batch.Count > 0)
                Run(batch, players.ToList());
        }

        /// <summary>
        /// Runs command lines for the players in <paramref name="everyone"/> and returns the replies sent back (usage lines
        /// and refusals; also logged, and sent to the caller when known). Before a command runs: a missing argument is
        /// answered with its usage line (<see cref="UsageOf"/>, <see cref="RequiredArguments"/>), and the client-code
        /// commands are limited to allowed targets (<see cref="ClientCodeTargets"/>). An argument that doesn't parse
        /// (FormatException and the like) is answered with the usage line too, instead of an error.
        /// </summary>
        internal static List<string> Run(IReadOnlyList<(string Line, Source From)> lines, List<WorldSession> everyone)
        {
            var replies = new List<string>();
            foreach ((string raw, Source commandSource) in lines)
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
                if (MissingArguments(parts) && UsageOf(parts[0]) is string usage)
                {
                    Reply(commandSource, replies, $"usage: {usage}");
                    continue;
                }
                if (ClientCodeCommands.Contains(parts[0]))
                {
                    targets = ClientCodeTargets(parts[0], parts.Length > 1 ? parts[1] : "", targets, commandSource, replies);
                    if (targets.Count == 0)
                        continue;
                }
                try
                {
                    switch (parts[0])
                    {
                        case "give":
                            string[] args = parts[1].Split(' ');
                            foreach (WorldSession session in targets)
                                session.GiveItem(uint.Parse(args[0]), uint.Parse(args[1]));
                            break;
                        case "giveoverflow":
                        {
                            // giveoverflow <item> <count> [minutes]: puts items straight into overflow (tests of "Retrieve
                            // all"); minutes: they expire that soon instead of after OverflowExpiryHours (OverflowExpiry).
                            string[] a = parts[1].Split(' ');
                            foreach (WorldSession session in targets)
                                foreach ((var item, bool isNew) in session.Inventory.AddToOverflow(uint.Parse(a[0]), uint.Parse(a[1])))
                                {
                                    if (a.Length > 2 && isNew)
                                        session.Inventory.SetExpiresAt(item, DateTime.UtcNow.AddMinutes(double.Parse(a[2],
                                            System.Globalization.CultureInfo.InvariantCulture)));
                                    session.SendItem(item, replace: !isNew);
                                }
                            break;
                        }
                        case "send":
                            // send <hex bytes>: a raw zone packet, opcode bytes included (e.g. 9A 00 05 ...). Raw packets can
                            // run client Lua too (FirstTimeEventBase Script): the caller / admins only (ClientCodeTargets).
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
                                Terrain.ZoneTerrain.SendManifestUpdate(session, id);
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
                        case "clock":
                        {
                            // clock: logs the world clock and tells the targets in chat, to compare with the client's
                            // GameData.Time data source (SceneTime / IslandTime; harness HXT.dd("GameData.Time")).
                            string text = $"World clock {WorldClock.Describe()}.";
                            foreach (WorldSession session in targets)
                                Chat.System(session, text);
                            log.Info(text);
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
                            // digmode <n>: how digs shape holes (TerrainEdits.OffsetMode): 8 the client's own surface (default),
                            // 5 the old solid shell, 0 no vertices. For side-by-side checks.
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
                            var (cells, voxels) = Terrain.EnvironmentServer.Instance.Sphere(at, double.Parse(a[2], CultureInfo.InvariantCulture), 0, session.ZoneId);
                            Terrain.ZoneTerrain.Broadcast(cells, session.ZoneId);
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
                            var (cells, voxels) = Terrain.EnvironmentServer.Instance.Sphere(new System.Numerics.Vector3(x, y, z),
                                double.Parse(a[2], CultureInfo.InvariantCulture), 0, session.ZoneId);
                            Terrain.ZoneTerrain.Broadcast(cells, session.ZoneId);
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
                            // cave [zone]: the same as Travel -> An Underground Adventure (a random cavern graveyard; with a
                            // zone, one of that landscape's, outposts placed or not; a cavern while its graveyards aren't ready).
                            foreach (WorldSession session in targets)
                                Caves.TravelRandom(session, parts.Length > 1 ? uint.Parse(parts[1].Trim()) : null);
                            break;
                        case "cavetrip":
                            // cavetrip <crystal|adventure> [client world id] [zone]: a Lost Crystal / Underground Adventure
                            // trip to another world of the gateway now (L131), whatever the roll and CavernTripsOtherWorlds
                            // say (default: the first reachable world, a random Chaos Cavern landscape). That world picks the
                            // cavern as for a real trip.
                            {
                                string[] a = parts.Length > 1 ? parts[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
                                uint? world = a.Length > 1 ? uint.Parse(a[1]) : null;
                                uint? zone = a.Length > 2 ? uint.Parse(a[2]) : null;
                                foreach (WorldSession session in targets)
                                    log.Info($"cavetrip {session.CharacterName}: " + CrossWorld.DevCavernTrip(session, a.Length > 0 ? a[0] : "", world, zone));
                            }
                            break;
                        case "cavelands":
                            // cavelands [zone]: every landscape with Chaos Caverns and where its outposts stand (Outposts,
                            // audit F2); with a zone, its outposts are queued for placement now (background, one at a time).
                            if (parts.Length > 1)
                                Outposts.Retry(uint.Parse(parts[1].Trim()));
                            log.Info("cavelands: " + Outposts.LandscapeReport());
                            break;
                        case "outpostrotate":
                            // outpostrotate [zone|here] [count]: one rotation of a landscape's outposts now (Outposts.Rotation,
                            // audit F8), whatever OutpostRotationHours says: count outposts (default the configured share) get
                            // another ruin in their own cavern, in the background; outposts a player is near are skipped.
                            // Writes terrain edits and the landscape's outpost marker (back up server-data first).
                            {
                                string[] a = parts.Length > 1 ? parts[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
                                uint zone = a.Length > 0 && a[0] != "here" ? uint.Parse(a[0]) : targets.FirstOrDefault()?.ZoneId ?? 0;
                                int count = a.Length > 1 ? int.Parse(a[1]) : 0;
                                log.Info("outpostrotate: " + Outposts.QueueRotation(zone, count));
                            }
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
                        case "studiotake":
                            // studiotake <product id>: the Showcase's Player Studio "Get" without the browser (tests).
                            foreach (WorldSession session in targets)
                            {
                                string product = parts[1];
                                WorldChannelServices.Commerce.TakeDesign(session, ulong.Parse(product))
                                    .ContinueWith(t => log.Info($"studiotake {product}: {t.Result.Message}"), TaskContinuationOptions.OnlyOnRanToCompletion);
                            }
                            break;
                        case "pioneerslab":
                        {
                            // pioneerslab [again] [force]: stamps the slate slab under each target's own site on the Pioneer land
                            // they stand on, as a new Pioneer site gets it, in the current PioneerSlab shape (default "site": the
                            // full 210 m square, 2 m proud, the ground cut or filled to it; the Starting Location goes onto it).
                            // Name the owner (@Name): without it every online player's site there is stamped. Only a surface
                            // site with nothing built on it: a cavern site never gets a slab, and a site with any building in
                            // its box is skipped, because the slab shows wherever the build has no voxel of its own (its
                            // ground-floor rooms and doorways fill with up to 2 m of slate, ground higher than the slab is cut
                            // away from under it); "force" stamps such a site anyway. Once per site: a site that has a slab is
                            // left alone, unless "again" stamps it once more (how an owner gives an old, unbuilt Pioneer site the
                            // full slab; props on its old ground stay where they are). L180 / L260.
                            bool again = parts.Skip(1).Any(p => p.Trim().Equals("again", StringComparison.OrdinalIgnoreCase));
                            bool force = parts.Skip(1).Any(p => p.Trim().Equals("force", StringComparison.OrdinalIgnoreCase));
                            foreach (WorldSession session in targets)
                            {
                                Database.Character.Model.ClaimModel site = Claims.All().FirstOrDefault(c => c.OwnerGuid == session.PlayerGuid && c.ZoneId == session.ZoneId);
                                if (site == null || Landscapes.Get(site.ZoneId) is not { Pioneer: true })
                                    log.Info($"pioneerslab {session.CharacterName}: no site of theirs on this landscape, or not a Pioneer land.");
                                else if (!Pioneer.IsSurfaceSite(site))
                                    log.Info($"pioneerslab {session.CharacterName}: site {site.Id} is not a surface site (a cavern site: its "
                                        + "middle lies under the ground); cavern sites get no slab.");
                                else
                                    log.Info($"pioneerslab {session.CharacterName}: site {site.Id} "
                                        + (Pioneer.QueueSlab(site, again, force)
                                            ? (force ? "queued (forced: stamped over any building)" : "queued (skipped when the stamp runs if anything is built in it)")
                                            : $"not queued (slab {Pioneer.SlabState(site.Id)}"
                                            + $"{(again ? "" : "; \"pioneerslab again\" stamps it once more")}; PioneerSlab "
                                            + $"{Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config.PioneerSlab})") + ".");
                            }
                            break;
                        }
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
                                Social.SocialServices.Service.Guild(session, Convert.ToUInt16(a[0], 16), Convert.FromHexString(a.Length > 1 ? a[1] : ""));
                            break;
                        }
                        case "sysmail":
                            // sysmail <subject>: a system mail to the player (new-mail notification test, L67).
                            foreach (WorldSession session in targets)
                                Social.SocialServices.Service.SendSystemMail(session.PlayerGuid, "Test", parts.Length > 1 ? parts[1].Trim() : "Test mail",
                                    "A test mail from the dev command.", 0, Array.Empty<(uint, uint)>());
                            break;
                        case "mailpacket":
                        {
                            // mailpacket <sub hex> <body hex>: handle a MailBase request as if the player's client sent it
                            // (tests compose, whose window object client Lua can't build).
                            string[] a = parts[1].Split(' ');
                            byte[] mailBytes = BitConverter.GetBytes(Convert.ToUInt32(a[0], 16))
                                .Concat(Convert.FromHexString(a.Length > 1 ? a[1] : "")).ToArray();
                            foreach (WorldSession session in targets)
                            {
                                var request = new Network.Message.Model.MailRequest();
                                request.Read(new global::LandmarkEmulator.Shared.Network.GamePacketReader(mailBytes));
                                Social.SocialServices.Service.Mail(session, request);
                            }
                            break;
                        }
                        case "lua":
                        {
                            // lua <Table:Function or Table.Function> [a b c]: run a Lua function in the client
                            // (InteractionRunClientLua) with up to three numbers. Client code: the caller / admins only
                            // (ClientCodeTargets, setting DevClientCodeOnOthers).
                            string[] a = parts[1].Split(' ');
                            float N(int i) => a.Length > i ? float.Parse(a[i], System.Globalization.CultureInfo.InvariantCulture) : 0f;
                            float n1 = N(1), n2 = N(2), n3 = N(3);
                            foreach (WorldSession session in targets)
                                ClientLua.DevRun(session, a[0], n1, n2, n3);
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
                        case "treepools":
                        {
                            // treepools: the min / max pools of the player's landscape (most short of their minimum first).
                            // treepools fell <n>: fell n standing trees of the nearest tree's pool (no drops) to push it below
                            // its minimum; treepools now: make the landscape's queued respawns due (Gathering.Respawn.cs).
                            string[] a = parts.Length > 1 ? parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
                            foreach (WorldSession session in targets)
                                log.Info(a.Length > 0 && a[0] == "fell"
                                    ? Gathering.DevFell(session, a.Length > 1 && int.TryParse(a[1], out int n) ? n : 1)
                                    : a.Length > 0 && a[0] == "now" ? Gathering.DevRespawnNow(session.ZoneId)
                                    : Gathering.RespawnReport(session.ZoneId));
                            break;
                        }
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
                        case "unwield":
                            // unwield [equipment slot]: puts the item in that slot back in the bag (default 2, the main hand).
                            foreach (WorldSession session in targets)
                                Network.Handlers.InventoryHandler.Unequip(session, parts.Length > 1 ? uint.Parse(parts[1].Trim()) : 2u);
                            break;
                        case "weaponkeywords":
                            Combat.WeaponCombat.SendKeywords = parts.Length < 2 || parts[1].Trim() != "off";
                            log.Info($"weapon keywords {(Combat.WeaponCombat.SendKeywords ? "on" : "off")}");
                            break;
                        case "movementeffects":
                            // movementeffects on|off: sprint / boot caster-info replies with or without their client-run
                            // movement effects (MovementEffects, L160). The client keeps replies it has until relog.
                            MovementEffects.Enabled = parts.Length < 2 || parts[1].Trim() != "off";
                            log.Info($"movement effects {(MovementEffects.Enabled ? "on" : "off")}");
                            break;
                        case "meleetargeters":
                            // meleetargeters on|off: melee weapon replies with or without their client Sweep targeter
                            // (WeaponCombat.Targeters, L170); the targeted players get their attack info again.
                            Combat.WeaponCombat.MeleeTargeters = parts.Length < 2 || parts[1].Trim() != "off";
                            foreach (WorldSession session in targets)
                                Combat.WeaponCombat.SendAttackInfo(session);
                            log.Info($"melee targeters {(Combat.WeaponCombat.MeleeTargeters ? "on" : "off")}");
                            break;
                        case "blockvisible":
                            // blockvisible on|off: blocking stances shown to the other clients or not (BlockStanceVisible,
                            // Combat.BlockStance, L170) until the restart; stances already shown still end normally.
                            Combat.BlockStance.Visible = parts.Length < 2 || parts[1].Trim() != "off";
                            log.Info($"block stances {(Combat.BlockStance.Visible ? "shown" : "not shown")} to other clients");
                            break;
                        case "propstream":
                        {
                            // propstream [<metres>|off]: with a value, the placed-prop stream range (setting PropStreamRange;
                            // off / 0 = the whole landscape) until the restart, applied on each player's next stream tick;
                            // then (or alone) each targeted player's stream in the log (Props.Stream.cs).
                            string arg = parts.Length > 1 ? parts[1].Trim() : "";
                            if (arg == "off")
                                Props.StreamRange = 0f;
                            else if (arg.Length > 0)
                                Props.StreamRange = float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
                            foreach (WorldSession session in targets)
                                log.Info($"{session.CharacterName} prop stream: {Props.StreamState(session)}");
                            break;
                        }
                        case "treestream":
                        {
                            // treestream [<metres>]: with a value, the tree / plant stream range (setting TreeStreamRange,
                            // 64-1000) until the restart, applied on each player's next stream tick; then (or alone) each
                            // targeted player's tree stream in the log (Gathering.Stream.cs).
                            string arg = parts.Length > 1 ? parts[1].Trim() : "";
                            if (arg.Length > 0)
                                Gathering.StreamRange = float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
                            foreach (WorldSession session in targets)
                                log.Info($"{session.CharacterName} tree stream: {Gathering.StreamState(session)}");
                            break;
                        }
                        case "safeeject":
                        {
                            // safeeject [clear|on|off]: the launches reported (SafeEject 0x26/0x4C) and not ended yet, in the
                            // log; clear = end the targeted players' launches now (bit 26 cleared); on / off = the setting
                            // SafeEjectReset until the restart (Game/SafeEject).
                            string arg = parts.Length > 1 ? parts[1].Trim() : "";
                            if (arg == "on" || arg == "off")
                                SafeEject.Reset = arg == "on";
                            if (arg == "clear")
                                foreach (WorldSession session in targets)
                                    if (!SafeEject.End(session, "dev command"))
                                        session.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateUpdateCharacterStateDeltaWire, SafeEject.ClearBody(session.PlayerGuid));
                            log.Info($"safe-eject launches (reset {(SafeEject.Reset ? "on" : "off")}): {SafeEject.DevState()}");
                            break;
                        }
                        case "blockstances":
                            // blockstances: the blocking stances up now (guid, arc, end, clients told), in the log.
                            log.Info($"block stances: {Combat.BlockStance.DevState()}");
                            break;
                        case "movement":
                            // movement: the player's open channel (sprint, Immediate Delivery, Endless Breeze), charging leap
                            // and movement-ability cooldowns (MovementEffects, L168); Energy is in "states".
                            foreach (WorldSession session in targets)
                                log.Info($"{session.CharacterName} movement: {MovementEffects.DevState(session)}; "
                                    + $"Energy {WeaponAttacks.EnergyOf(session)}");
                            break;
                        case "charstats":
                            // charstats [on|off]: the live character stats (CharacterStats, L162); "on" also sends them to
                            // the targeted players now.
                            CharacterStats.Enabled = parts.Length < 2 || parts[1].Trim() != "off";
                            foreach (WorldSession session in targets)
                                CharacterStats.SendOwn(session);
                            log.Info($"character stats {(CharacterStats.Enabled ? "on" : "off")}");
                            break;
                        case "fire":
                            // fire [power]: the held weapon's Basic / Power attack straight ahead (no input; WeaponCombat).
                            foreach (WorldSession session in targets)
                                log.Info($"fire: {session.CharacterName} hit {Combat.WeaponCombat.DevFire(session, parts.Length > 1 && parts[1].Trim() == "power")}");
                            break;
                        case "projectile":
                        {
                            // projectile <definition> [distance] [speed]: projectiles fly at the player from N / S / E / W
                            // (Projectiles family 0xEE, L138); Delete at arrival. speed 0 = the definition's own.
                            string[] a = parts[1].Split(' ');
                            uint def = uint.Parse(a[0]);
                            float dist = a.Length > 1 ? float.Parse(a[1], CultureInfo.InvariantCulture) : 20f;
                            float speed = a.Length > 2 ? float.Parse(a[2], CultureInfo.InvariantCulture) : 4f;
                            foreach (WorldSession session in targets)
                            {
                                // a 4th argument "r": positions in the remote frame (local + zone origin / 10) instead of the local one.
                                var frame = a.Length > 3 && a[3] == "r" ? WorldSession.RemoteFrameOffset : System.Numerics.Vector3.Zero;
                                var aim = session.Position + frame + new System.Numerics.Vector3(0f, 1.2f, 0f);
                                var viewers = new[] { session };
                                // From the four compass points at once, so one is in the camera's view.
                                foreach (var side in new[] { System.Numerics.Vector3.UnitX, -System.Numerics.Vector3.UnitX, System.Numerics.Vector3.UnitZ, -System.Numerics.Vector3.UnitZ })
                                {
                                    var origin = aim + side * dist + new System.Numerics.Vector3(0f, 2f, 0f);
                                    uint id = Projectiles.NewId();
                                    Projectiles.Create(viewers, id, def, origin, aim - origin, 0, aim, session.PlayerGuid, speed);
                                    float seconds = (speed > 0f ? dist / speed : dist / 25f) + 0.1f;
                                    log.Info($"projectile {id}: definition {def}, from {origin} at {speed} m/s, delete in {seconds:F1} s");
                                    _ = Task.Delay(System.TimeSpan.FromSeconds(seconds)).ContinueWith(_ => Projectiles.Delete(viewers, id, aim, true, session.PlayerGuid));
                                }
                            }
                            break;
                        }
                        case "itemslot":
                        {
                            // itemslot <equipment slot id>: up to 30 item definitions worn in that slot (8 = chest armour).
                            // itemslot <slot> [model text]: optionally only models containing the text (e.g. _Bow_).
                            string[] q = parts[1].Trim().Split(' ');
                            uint want = uint.Parse(q[0]);
                            var hits = Inventory.ItemDefinitionManager.All.Where(d => d.EquipSlotId == want
                                && (q.Length < 2 || (d.ModelFile ?? "").Contains(q[1]))).OrderBy(d => d.Id).ToList();
                            log.Info($"itemslot {want}: {hits.Count} item(s): " + string.Join(", ", hits.Take(30).Select(d => $"{d.Id} '{d.ModelFile}'")));
                            break;
                        }
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
                        case "struck":
                            // struck [damage] [count]: the nearest creature (30 m) hits this player [count] times through the
                            // normal path (blocks, Harden, shields, the armour's defensive passives: Crashing Waves, Frost
                            // Lord's Guard, Harden explosions …; each passive that fires logs "<name> passive: …").
                            {
                                string[] a = parts.Length > 1 ? parts[1].Split(' ') : Array.Empty<string>();
                                uint dmg = a.Length > 0 ? uint.Parse(a[0]) : 20u;
                                int count = a.Length > 1 ? Math.Clamp(int.Parse(a[1]), 1, 50) : 1;
                                foreach (WorldSession session in targets)
                                {
                                    int landed = 0;
                                    for (int i = 0; i < count && Creatures.DevStruckByNearest(session, dmg, 30f); i++)
                                        landed++;
                                    log.Info($"{session.CharacterName}: struck {landed} time(s) for {dmg} by the nearest creature{(landed == 0 ? " (none within 30 m)" : "")}.");
                                }
                            }
                            break;
                        case "loot":
                        case "content":
                            // loot reload | content reload: reload ContentTables (baked + /data/content) without a world restart.
                            {
                                string sub = parts.Length > 1 ? parts[1].Trim() : "";
                                if (!string.Equals(sub, "reload", StringComparison.OrdinalIgnoreCase))
                                {
                                    Reply(commandSource, replies, "usage: " + UsageOf(parts[0]));
                                    break;
                                }
                                string err = ContentTables.Reload();
                                string msg = err == null
                                    ? $"ContentTables reloaded (override {ContentTables.OverrideRoot()})."
                                    : $"ContentTables reload failed — kept previous tables: {err}";
                                Reply(commandSource, replies, msg);
                                log.Info(msg);
                            }
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
                            // npc reload | goto <id> | reset | flag <id> <key> [value]: world NPC scripting helpers.
                            string[] a = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (a.Length > 0 && a[0] is "reload" or "goto" or "talk" or "reset" or "flag")
                            {
                                foreach (WorldSession session in targets)
                                    log.Info($"npc: {Npc.WorldNpcs.DevCommand(session, a)}");
                                break;
                            }
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
                        case "pos":
                            // pos: the player's position (local metres), landscape and the surface height there (harness checks).
                            foreach (WorldSession session in targets)
                            {
                                var here = session.Position;
                                float? ground = Terrain.TerrainHeights.HeightAt(here.X, here.Z, session.ZoneId);
                                log.Info($"pos {session.CharacterName}: {here.X:F1} {here.Y:F1} {here.Z:F1} landscape {session.ZoneId}, surface {(ground is float g ? g.ToString("F1") : "?")}");
                            }
                            break;
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
                                // Raw packets go out as given: the 0.695 header, u16 0x26 + u32 sub (docs/protocol.md §1.6).
                                var body = new List<byte> { 0x26, 0x00, 0x2D, 0x00, 0x00, 0x00 };
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
                            string needle = parts.Length > 1 ? parts[1].Trim() : "";
                            // Cap matches: an empty needle used to match every name (Contains("")) and flood the world tick
                            // with log lines, which froze the game and the /admin page (docs/bugs.md 2026-10-18).
                            if (needle.Length == 0)
                            {
                                Reply(commandSource, replies, "usage: finditem [text|#id] (type a name fragment or #id)");
                                break;
                            }
                            const int findItemCap = 40;
                            int matched = 0, shown = 0;
                            bool byId = needle.StartsWith('#');
                            foreach (var d in Inventory.ItemDefinitionManager.All)
                            {
                                string name = LandmarkEmulator.Shared.GameTable.Text.TextManager.Instance.GetTextForId(d.NameId) ?? "";
                                // "#38" looks an item up by id.
                                if (!(byId ? needle[1..] == d.Id.ToString() : name.Contains(needle, StringComparison.OrdinalIgnoreCase)))
                                    continue;
                                matched++;
                                if (shown >= findItemCap)
                                    continue;
                                shown++;
                                log.Info($"item {d.Id}: {name} (max stack {d.MaxStack}, categories {string.Join(",", d.Categories)}{(d.PropId != 0 ? $", prop {d.PropId}" : "")}){(d.Abilities.Count > 0 ? " abilities " + string.Join(",", d.Abilities.Select(x => x.AbilityId)) : "")}"
                                    + $" — {LandmarkEmulator.Shared.GameTable.Text.TextManager.Instance.GetTextForId(d.DescriptionId)}");
                            }
                            Reply(commandSource, replies, matched == 0
                                ? $"finditem: no items matching \"{needle}\"."
                                : matched > shown
                                    ? $"finditem: {matched} matches for \"{needle}\" (first {shown} in the server log)."
                                    : $"finditem: {matched} match(es) for \"{needle}\" (server log).");
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
                        case "broadcast":
                        {
                            // broadcast <text>: a System Broadcast (BaseBroadcastPacket 0x4F, Game.Broadcasts) to the targets
                            // ("System Broadcast: <text>" on screen and in chat). "broadcast raw <text>" sends kind 0 (the text
                            // alone); "broadcast from <Name> <text>" a player broadcast ("Broadcast from <Name>: <text>").
                            string text = parts.Length > 1 ? parts[1].Trim() : "";
                            short kind = Broadcasts.SystemKind;
                            string sender = "";
                            if (text.StartsWith("raw ", StringComparison.OrdinalIgnoreCase))
                            {
                                kind = Broadcasts.Plain;
                                text = text[4..].Trim();
                            }
                            else if (text.StartsWith("from ", StringComparison.OrdinalIgnoreCase) && text[5..].Trim().Split(' ', 2) is { Length: 2 } from)
                            {
                                sender = from[0];
                                text = from[1].Trim();
                            }
                            if (text.Length > 0)
                                Broadcasts.Send(targets, text, kind, sender);
                            break;
                        }
                        case "kick":
                        {
                            // @Name kick [seconds] [reason] | @Name kick cancel: removes that player (Game.Kicks): warnings
                            // (PendingKickFromServer 0x65 + a readable line) during the countdown, then KickedFromServer 0x53
                            // and the connection closes. Needs "@Name": a bare "kick" would remove everyone.
                            if (!raw.TrimStart().StartsWith("@"))
                            {
                                log.Warn($"Dev command \"{line}\": kick needs a player (\"@Name kick …\"); not run.");
                                continue;
                            }
                            string[] a = parts.Length > 1 ? parts[1].Trim().Split(' ', 2) : Array.Empty<string>();
                            if (a.Length > 0 && a[0] == "cancel")
                            {
                                foreach (WorldSession session in targets)
                                    Kicks.Cancel(session);
                                break;
                            }
                            int seconds = 0;
                            string reason = parts.Length > 1 ? parts[1].Trim() : "";
                            if (a.Length > 0 && int.TryParse(a[0], out seconds))
                                reason = a.Length > 1 ? a[1] : "";
                            foreach (WorldSession session in targets)
                                Kicks.Kick(session, seconds, reason, "dev command");
                            break;
                        }
                        case "psoffer":
                            // psoffer: sends the product-for-review reply now (test tooling; the client asks at login).
                            foreach (WorldSession session in targets)
                                WorldChannelServices.Commerce.OfferReview(session);
                            break;
                        case "pstake":
                        {
                            // pstake <product id>: buys a listed Player Studio design as the Showcase's Buy / Get does (its price in
                            // Lumens unless FreeStore; the creator is mailed their share). Test tooling for the page's POST /api/studio/take.
                            ulong product = ulong.Parse(parts[1].Trim());
                            foreach (WorldSession session in targets)
                            {
                                string who = session.CharacterName;
                                uint before = session.Coins;
                                WorldChannelServices.Commerce.TakeDesign(session, product).ContinueWith(t => log.Info(
                                    $"pstake {product} for {who}: {(t.Result.Ok ? "ok" : "refused")}: {t.Result.Message} (Lumens before: {before})."),
                                    TaskContinuationOptions.OnlyOnRanToCompletion);
                            }
                            break;
                        }
                        case "psfee":
                        {
                            // psfee [price]: re-sends Player Studio's CommerceConfiguration 0x2E and the wallet, and logs the listing
                            // fee for that price (default 25), the wallet balance sent and the player's own designs' minimum prices
                            // (TemplateValue). Test tooling for the listing fee (Game.Commerce.SendConfiguration).
                            uint price = parts.Length > 1 && uint.TryParse(parts[1].Trim(), out uint asked) ? asked : 25;
                            foreach (WorldSession session in targets)
                            {
                                Commerce.SendConfiguration(session);
                                Commerce.SendWalletInfo(session);
                                List<string> minimums;
                                using (var context = Shared.Database.DatabaseManager.Instance.CharacterDatabase.NewContext())
                                    minimums = context.Template.Where(t => t.OwnerGuid == session.PlayerGuid).OrderBy(t => t.Id).Take(20).ToList()
                                        .Select(t => $"{t.Id} '{t.Name}' {Templates.MaterialCount(t.Materials)} voxels → min {Internal.Commerce.StudioRules.MinimumPrice(Templates.MaterialCount(t.Materials))}")
                                        .ToList();
                                log.Info($"psfee {price} for {session.CharacterName}: charged {Commerce.ListingFeeCharged}, minimum fee {Commerce.MinimumListingFee}, "
                                    + $"percent {Commerce.ListingFeePercent}, fee {Commerce.ListingFee(price)} Lumens; wallet sent "
                                    + $"{(Commerce.WalletHoldsLumens ? session.Coins : 0)}; designs: {(minimums.Count == 0 ? "none" : string.Join("; ", minimums))}.");
                            }
                            break;
                        }
                        case "storespent":
                            // storespent [lumens]: shows (or sets) the Lumens this character spent on the store and Player Studio
                            // (Unlocks kind 33), and whether the account may peer-review (Commerce.MayReview, live's 1000 SC rule).
                            foreach (WorldSession session in targets)
                            {
                                if (parts.Length > 1 && uint.TryParse(parts[1].Trim(), out uint spent))
                                    Unlocks.Set(session, Unlocks.StoreSpent, spent);
                                uint own = Unlocks.Values(session.PlayerGuid, Unlocks.StoreSpent).FirstOrDefault();
                                WorldSession who = session;
                                Commerce.AccountSpent(session, total => log.Info($"{who.CharacterName}: store spend {own} Lumens, "
                                    + $"account {total} of {Commerce.ReviewerMinLumens} needed: may review {total >= Commerce.ReviewerMinLumens}."));
                            }
                            break;
                        case "psreview":
                        {
                            // psreview <product id> approve|reject: decides a Player Studio product (Game.Commerce).
                            string[] a = parts[1].Split(' ');
                            string what = parts[1];
                            WorldChannelServices.Commerce.AdminDecide(ulong.Parse(a[0]), a.Length > 1 && a[1] == "approve")
                                .ContinueWith(t => log.Info($"psreview {what}: {(t.Result ? "done" : "no pending product")}"), TaskContinuationOptions.OnlyOnRanToCompletion);
                            break;
                        }
                        case "followdigest":
                        {
                            // followdigest [dry] [hours]: runs the Showcase following digest now (Game.ShowcaseFollow): the mails for
                            // what was listed since the last run (or the last <hours>); "dry" composes them only (nothing sent, no run
                            // recorded) and shows each one to the players named. Runs whether or not SHOWCASE_FOLLOW_DIGEST is on.
                            string[] a = parts.Length > 1 ? parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
                            bool dry = a.Contains("dry", StringComparer.OrdinalIgnoreCase);
                            double? hours = a.Select(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out double h) ? h : (double?)null)
                                .FirstOrDefault(h => h != null);
                            // With COMMERCE_ADDRESS set it asks the commerce service to run it for this world's realm (FollowService).
                            var told = targets.ToList();
                            WorldChannelServices.Commerce.RunFollowDigest(told.FirstOrDefault(), dry, hours).ContinueWith(t =>
                            {
                                if (t.IsFaulted)
                                {
                                    log.Error(t.Exception, "followdigest failed.");
                                    return;
                                }
                                // Chat.System only queues a packet to the session (thread-safe), so it may run where the answer arrives.
                                // The first line is the summary for everyone; a dry run's own mail goes to its recipient only.
                                foreach (WorldSession session in told)
                                    for (int i = 0; i < t.Result.Count; i++)
                                        if (i == 0 || session == told[0])
                                            Chat.System(session, t.Result[i]);
                                log.Info(t.Result.FirstOrDefault());
                            });
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
                            // landscape <zone id>: travel there (same as choosing it in the Landscapes window; also a
                            // closed event island 460-462, e.g. to bake its heights).
                            foreach (WorldSession session in targets)
                                Landscapes.TravelTo(session, uint.Parse(parts[1]), anyEventIsland: true);
                            break;
                        case "eventexpire":
                        {
                            // eventexpire: dry run of the event island expiry (Landscapes.EventExpiry.cs): the setting, the
                            // island open now and at the last run, and every site on 460-462 with what happens to it (log;
                            // a summary in chat). eventexpire confirm <zone>: expires a closed island's sites now (design
                            // saved, terrain reverted, contents mailed; refused for the open island).
                            string[] a = parts.Length > 1 ? parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
                            List<string> report;
                            if (a.Length == 2 && a[0] == "confirm" && uint.TryParse(a[1], out uint closedZone))
                                report = new List<string> { Landscapes.ExpireIslandByHand(closedZone) };
                            else
                                report = Landscapes.EventExpiryReport();
                            foreach (string l in report)
                                log.Info($"eventexpire: {l}");
                            foreach (WorldSession session in targets)
                                foreach (string l in report.Where(l => !l.StartsWith("  ")))
                                    Chat.System(session, l);
                            break;
                        }
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
                        case "expiresites":
                        {
                            // expiresites all | <site id> …: expires those build sites as upkeep expiry does (Claims.Expire:
                            // removed for everyone, design saved, terrain reverted, props and resources mailed to the owner).
                            // Test clean-up on the one server (data loss is fine there, user 2026-10-18).
                            string[] a = parts.Length > 1 ? parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
                            var ids = a.Where(x => x != "all").Select(ulong.Parse).ToHashSet();
                            var sites = Claims.All().Where(c => a.Contains("all") || ids.Contains(c.Id)).ToList();
                            Claims.Expire(sites, "dev clean-up");
                            log.Info($"expiresites: {sites.Count} site(s) expired: {string.Join(", ", sites.Select(c => c.Id))}.");
                            break;
                        }
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
                        case "cellcompare":
                            // cellcompare on|off: log how the stored cell compares with each edit's cell, and whether a selection Delete / Cut's client layer versions name the server's result (Building.CompareWithClient).
                            Building.CompareWithClient = parts.Length > 1 && parts[1].Trim() == "on";
                            log.Info($"Cell compare after edits: {(Building.CompareWithClient ? "on" : "off")}.");
                            break;
                        case "vertexonly":
                            // vertexonly on|off: keep the vertex-only voxels of client build diffs (round surfaces).
                            Terrain.BlockData.KeepVertexOnly = parts.Length > 1 && parts[1].Trim() == "on";
                            log.Info($"Vertex-only voxels from build diffs: {(Terrain.BlockData.KeepVertexOnly ? "kept" : "dropped")}.");
                            break;
                        case "veinsweep":
                            // veinsweep: moves every vein that touches a build site out of it (sites placed before
                            // OreVeins.ClearSite existed, or veins generated before the site rule).
                            foreach (var claim in Claims.All())
                                Terrain.ZoneTerrain.ClearVeinsInSite(claim.ZoneId, (claim.MinX, claim.MinY, claim.MinZ), (claim.MaxX, claim.MaxY, claim.MaxZ));
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
                                Terrain.EnvironmentServer.Instance.RefreshLodCopies(session.ZoneId);
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
                                    log.Info($"{session.CharacterName} to build site {siteId} at {ground}.");
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
                                WorldChannelServices.Commerce.PortraitCrc(session, Convert.ToUInt32(parts[1].Trim(), 16));
                            break;
                        case "portraittest":
                        {
                            // portraittest <character> <Headshot|Portrait> <image set id>: store an exported icon as that
                            // character's image and send it to the player (tests the client's portrait display).
                            string[] a = parts[1].Split(' ');
                            byte[] png = File.ReadAllBytes(GameDataFiles.PathOf(Path.Combine("icons", a[2] + ".png")));
                            WorldChannelServices.Commerce.PortraitStore(ulong.Parse(a[0]), a[1], png);
                            foreach (WorldSession session in targets)
                                log.Info($"portraittest {a[0]} {a[1]}: sent {WorldChannelServices.Commerce.PortraitSend(session, ulong.Parse(a[0]), a[1])}");
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
                                log.Info($"storetoken {session.CharacterName} {WorldChannelServices.Commerce.ShowcaseToken(session)}");
                            break;
                        case "luacode":
                            // luacode <Lua code>: run a line of Lua in the client (ClientLua.RunCode). Client code: the caller
                            // / admins only (ClientCodeTargets, setting DevClientCodeOnOthers).
                            foreach (WorldSession session in targets)
                                ClientLua.DevRunCode(session, parts[1]);
                            break;
                        case "luas":
                            // luas <Table.Method> [arg arg ...]: call a client Lua function with string arguments. Client
                            // code: the caller / admins only.
                            foreach (WorldSession session in targets)
                                ClientLua.DevRunWithArgs(session, parts[1].Split(' ')[0], parts[1].Split(' ').Skip(1).ToArray());
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
                        case "resupply":
                            // resupply: as using the Spire's Resupply Chest (missing starter tools, then Founder's Pack restore).
                            foreach (WorldSession session in targets)
                                Spire.UseResupplyChest(session);
                            break;
                        case "founderchest":
                            // founderchest: Founder's Pack restore only (same path the Spire Resupply Chest runs after tools; prints idle replies).
                            foreach (WorldSession session in targets)
                                Entitlements.RestoreFounderItems(session);
                            break;
                        case "activate":
                            // activate <item id>: uses one held item as the bag's Use does (InventoryHandler.HandleActivate:
                            // recipe items, storage expansions, unlockers, prop placement) — harness tests of item uses.
                            foreach (WorldSession session in targets)
                            {
                                var held = session.Inventory.Items.FirstOrDefault(i => i.ItemDefinitionId == uint.Parse(parts[1]));
                                if (held == null)
                                    log.Info($"activate: {session.CharacterName} holds no item {parts[1]}.");
                                else
                                    Network.Handlers.InventoryHandler.HandleActivate(session, held.Id);
                            }
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
                                // With a world-channel service linked the vote goes there, where the client's own likes
                                // are counted (RatingClientRequest, build step 6); otherwise the zone counts it.
                                if (WorldChannelLink.IsLinked)
                                    WorldChannelLink.Send(new LandmarkEmulator.Internal.WorldChannel.RatingClientRequest
                                    {
                                        ConnectionId = session.Link.ConnectionId, Character = session.PlayerGuid,
                                        Packet = new byte[] { 0xA7, 0x00 }.Concat(body).ToArray()
                                    });
                                else
                                    WorldChannelServices.WorldChannel.ClaimVote(session, body.ToArray());
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
                        case "finderwaiting":
                            // finderwaiting <table guid hex> [off] | off: this world publishes that game to the shared Game
                            // Finder as if someone were waiting in it (a cross-world match without a second client, L31).
                            log.Info(InstancedGames.DevFinderWaiting(parts.Length > 1 ? parts[1] : ""));
                            break;
                        case "spirefull":
                            Spire.SendFullAtOnce = parts.Length < 2 || parts[1] != "off";
                            break;
                        case "gamelabels":
                            InstancedGames.ControlLabels = parts.Length < 2 || parts[1] != "off";
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
                        case "propread":
                            // propread <prop hex>: as using a sign / book / scroll with text (Props.Read).
                            foreach (WorldSession session in targets)
                                Props.Read(session, Convert.ToUInt64(parts[1], 16));
                            break;
                        case "npctext":
                        {
                            // npctext <mode> <text>: DisplayPropText spoken by the nearest creature (L96 bubble test).
                            string[] a = parts[1].Split(' ', 2);
                            foreach (WorldSession session in targets)
                                log.Info($"npctext: speaker {Creatures.DevTextNearest(session, uint.Parse(a[0]), a.Length > 1 ? a[1] : "", 40f):X}");
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
                            StoryTools.SettingChanged(Convert.ToUInt64(a[0], 16), a[1]);   // counter disc in Edit Mode (2.19)
                            break;
                        }
                        case "storyfaction":
                        {
                            // storyfaction <creature guid hex> <0 enemy | 1 neutral | 2 ally>: a story creature's faction.
                            string[] f = parts[1].Split(' ');
                            Creatures.StoryFaction(ulong.Parse(f[0], System.Globalization.NumberStyles.HexNumber), int.Parse(f[1]));
                            break;
                        }
                        case "storyweapons":
                        {
                            // storyweapons <creature guid hex> <1 equipped | 0 sheathed>: a story creature's weapon visuals
                            // (AnimationSelection 352 / 353, Creatures.StoryWeapons); logs whether anything changed.
                            string[] f = parts[1].Split(' ');
                            bool changed = Creatures.StoryWeapons(ulong.Parse(f[0], System.Globalization.NumberStyles.HexNumber), f.Length > 1 && f[1] != "0");
                            log.Info($"storyweapons {parts[1]}: {(changed ? "changed" : "no change (already so, dead or not a story creature)")}");
                            break;
                        }
                        case "creatureweapon":
                        {
                            // creatureweapon <creature guid hex | near> <Wield_*.adr | off> [tint alias]: puts a weapon actor
                            // in a creature's main hand (EquipmentSetCharacterEquipmentSlot slot 2 to the players that have
                            // it as a full entity; CreatureWeapons, OPEN-LEADS L95), whatever CreatureWeapons is set to.
                            // "near" = the creature nearest the command's player (30 m).
                            string[] f = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            var guids = new List<ulong>();
                            if (f[0].Equals("near", StringComparison.OrdinalIgnoreCase))
                                foreach (WorldSession session in targets)
                                    guids.Add(Creatures.NearestGuid(session, 30f));
                            else
                                guids.Add(ulong.Parse(f[0], System.Globalization.NumberStyles.HexNumber));
                            string model = f.Length > 1 && !f[1].Equals("off", StringComparison.OrdinalIgnoreCase) ? f[1] : null;
                            foreach (ulong guid in guids)
                                log.Info($"creatureweapon {parts[1]}: {(guid == 0 ? null : Creatures.DevWeapon(guid, model, f.Length > 2 ? f[2] : "")) ?? "no such creature"}");
                            break;
                        }
                        case "storylink":
                        {
                            // storylink <source hex> <event> <target hex> <action>: adds a Link as the player would (logs the result code).
                            string[] a = parts[1].Split(' ');
                            foreach (WorldSession session in targets)
                                log.Info($"storylink result {StoryTools.Add(session, Convert.ToUInt64(a[0], 16), uint.Parse(a[1]), Convert.ToUInt64(a[2], 16), uint.Parse(a[3]))}");
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
                                WorldChannelServices.Commerce.ContestSetPhase(uint.Parse(a[0]), a[1])
                                    .ContinueWith(t => log.Info($"contestphase: {t.Result}"), TaskContinuationOptions.OnlyOnRanToCompletion);
                            }
                            break;
                        case "contests":
                            foreach (WorldSession session in targets)
                            {
                                WorldSession player = session;
                                // Chat.System only queues a packet to the session (thread-safe), so it may run where the answer arrives.
                                WorldChannelServices.Commerce.ContestDescribe().ContinueWith(t =>
                                {
                                    Chat.System(player, t.Result);
                                    log.Info($"contests: {t.Result}");
                                }, TaskContinuationOptions.OnlyOnRanToCompletion);
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
                                    WorldChannelServices.Commerce.ContestSubmit(session, body.ToArray());
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
                        case "closecraft":
                            // closecraft: the server closes the target's crafting / repair window (RecipePacketZoneCraftingWindow
                            // 0x42/0x24 with bool 0, Crafting.CloseWindow), as walking away from the station does.
                            foreach (WorldSession session in targets)
                                Crafting.CloseWindow(session);
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
                            // beginprop #<item id>: as if placing that prop item from the bag (the player needs one; opens
                            // build mode as the bag does, BuildModeHolster.OpenForPlacement).
                            foreach (WorldSession session in targets)
                                if (parts[1].StartsWith('#'))
                                {
                                    var item = session.Inventory.Items.FirstOrDefault(i => i.ItemDefinitionId == uint.Parse(parts[1][1..]));
                                    uint prop = Inventory.ItemDefinitionManager.Get(uint.Parse(parts[1][1..]))?.PropId ?? 0;
                                    if (item != null && prop != 0)
                                        BuildModeHolster.OpenForPlacement(session, Props.BeginPlacement(session, prop, item.ItemDefinitionId, item.Id));
                                }
                                else
                                    Props.BeginPlacement(session, uint.Parse(parts[1]));
                            break;
                        case "propslide":
                        {
                            // propslide <guid hex> dx dy dz [seconds]: L135 probe — sends a movement (PlayerUpdate Position,
                            // as creatures move) for the prop's transient id towards its spot + offset, to see whether the
                            // client moves a placed prop that way (Magical Platform movement). The server's record is unchanged.
                            string[] a = parts[1].Split(' ');
                            ulong slideGuid = Convert.ToUInt64(a[0].Replace("0x", ""), 16);
                            var slide = new System.Numerics.Vector3(float.Parse(a[1]), float.Parse(a[2]), float.Parse(a[3]));
                            if (Props.Find(slideGuid) is Props.PlacedProp slid)
                            {
                                var from = new System.Numerics.Vector3(slid.Spawn.Position.X, slid.Spawn.Position.Y, slid.Spawn.Position.Z);
                                float seconds = a.Length > 4 ? float.Parse(a[4]) : 0f;
                                var velocity = seconds > 0f ? slide / seconds : System.Numerics.Vector3.Zero;
                                byte[] movement = Creatures.MovementBody(from + slide, 0f, velocity);
                                foreach (WorldSession viewer in PlayerManager.InZone(slid.Zone))
                                    Network.MovementFanout.Queue(viewer, slid.Spawn.TransientId, movement);
                                log.Info($"propslide: 0x{slideGuid:X} (transient {slid.Spawn.TransientId}) → {from + slide}.");
                            }
                            break;
                        }
                        case "placeat":
                        {
                            // placeat x y z [yaw]: finishes the pending placement there through the normal Save (harness
                            // placements; Props.DevSavePending). "placeat here [yaw]": at the player's feet.
                            string[] a = parts[1].Split(' ');
                            foreach (WorldSession session in targets)
                            {
                                bool here = a[0] == "here";
                                var at = here ? session.Position : new System.Numerics.Vector3(float.Parse(a[0]), float.Parse(a[1]), float.Parse(a[2]));
                                float yaw = a.Length > (here ? 1 : 3) ? float.Parse(a[here ? 1 : 3]) : 0f;
                                if (!Props.DevSavePending(session, at, yaw))
                                    log.Info($"placeat: {session.CharacterName} has no pending placement.");
                            }
                            break;
                        }
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
                            // outpost <n>: go to outpost n (Chaos Caverns), next to its middle. Home continent 0-80; another
                            // landscape 128 × (zone - 390) + k (cavelands lists them), re-zoning there.
                            int n = int.Parse(parts[1]);
                            if (Outposts.ByIndex(n) is Outposts.Outpost o)
                                foreach (WorldSession session in targets)
                                {
                                    var at = o.Floor + new System.Numerics.Vector3(0f, 1f, 0f);
                                    if (o.Zone == session.ZoneId)
                                        Teleport.To(session, at, snapToGround: false, loadingScreen: true);
                                    else
                                        Teleport.ToZone(session, at, o.Zone);
                                    log.Info($"outpost {n}: {o.Name} (landscape {o.Zone}, cavern {o.Cavern}, {o.Spawners.Count} spawners, {o.ChestSpots.Count} chest spots) at {o.Floor}");
                                }
                            else
                                log.Info($"outpost {n}: no such outpost (yet).");
                            break;
                        }
                        case "sight":
                            // sight: line-of-sight check of the creatures near the player (Game/Sight.cs), logged.
                            foreach (WorldSession session in targets)
                                foreach (string sightLine in Creatures.DevSight(session))
                                    log.Info($"sight {session.CharacterName}: {sightLine}");
                            break;
                        case "pathing":
                        {
                            // pathing [on|off]: creature navigation (Game/Pathing, TASKS 6.61) — the walkable floors at the
                            // player's spot and each creature within 40 m (nav kind, straight line to the player, path), logged;
                            // on / off switches it until the restart (setting CreaturePathing).
                            string arg = parts.Length > 1 ? parts[1].Trim().ToLowerInvariant() : "";
                            if (arg is "on" or "off")
                            {
                                Pathing.Enabled = arg == "on";
                                log.Info($"pathing: creature navigation {arg} until the restart.");
                            }
                            foreach (WorldSession session in targets)
                                foreach (string pathLine in Creatures.DevPathing(session))
                                    log.Info($"pathing {session.CharacterName}: {pathLine}");
                            break;
                        }
                        case "sitepathing":
                        {
                            // sitepathing [on|off]: story creatures' build-site navigation (Pathing site grids, OPEN-LEADS L133)
                            // — every site / dug outpost grid held, logged; on / off switches it until the restart (setting
                            // SitePathing).
                            string arg = parts.Length > 1 ? parts[1].Trim().ToLowerInvariant() : "";
                            if (arg is "on" or "off")
                            {
                                Pathing.SitesEnabled = arg == "on";
                                log.Info($"sitepathing: site navigation {arg} until the restart.");
                            }
                            List<string> siteLines = Pathing.ExplainSites();
                            if (siteLines.Count == 0)
                                log.Info($"sitepathing: no site grids held (site navigation {(Pathing.SitesEnabled ? "on" : "off")}).");
                            foreach (string siteLine in siteLines)
                                log.Info($"sitepathing: {siteLine}");
                            break;
                        }
                        case "wall":
                        {
                            // wall <dx> <dz> <length m> <height m> <x|z> [door m] [material]: a wall of the player's own voxels
                            // (the paste path, like ownblock: overlay + ownership, sent to everyone in range), 0.8 m thick,
                            // starting at that offset from the player and running along +x or +z from the ground up (each
                            // column's generated ground), with a doorway of that width (2.4 m tall) in its middle. Material 0
                            // takes the same wall away again (its voxels cleared: the generated ground shows). For the
                            // site-pathing test (story creatures must go through the doorway / around the wall).
                            string[] a = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            float F(int i) => float.Parse(a[i], CultureInfo.InvariantCulture);
                            float wdx = F(0), wdz = F(1), length = F(2), height = F(3);
                            bool alongX = a[4].Equals("x", StringComparison.OrdinalIgnoreCase);
                            float door = a.Length > 5 ? F(5) : 0f;
                            ushort material = a.Length > 6 ? ushort.Parse(a[6]) : (ushort)79;
                            const float thick = 0.8f, doorHeight = 2.4f;
                            double size = Terrain.TerrainEdits.VoxelSize;
                            const int S = Terrain.BlockData.Size;
                            foreach (WorldSession session in targets)
                            {
                                var origin = session.Position + new System.Numerics.Vector3(wdx, 0f, wdz);
                                var byCell = new Dictionary<ulong, List<Terrain.TerrainEdits.VoxelValue>>();
                                byte c = Terrain.BlockData.CentreOffset;
                                for (float u = 0f; u < length; u += 0.4f)
                                    for (float w = 0f; w < thick; w += 0.4f)
                                    {
                                        var column = origin + (alongX ? new System.Numerics.Vector3(u, 0f, w) : new System.Numerics.Vector3(w, 0f, u));
                                        float ground = Terrain.TerrainHeights.HeightAt(column.X, column.Z, session.ZoneId) ?? session.Position.Y;
                                        bool inDoor = door > 0f && MathF.Abs(u - length / 2f) < door / 2f;
                                        for (float h = inDoor ? doorHeight : -0.4f; h < height; h += 0.4f)
                                        {
                                            (double gx, double gy, double gz) = Terrain.TerrainEdits.ToGlobal(column with { Y = ground + h });
                                            long vx = (long)Math.Floor(gx / size), vy = (long)Math.Floor(gy / size), vz = (long)Math.Floor(gz / size);
                                            ulong cell = Terrain.TerrainStore.CellId(Terrain.TerrainEdits.Level, (uint)(vx / S), (uint)(vy / S), (uint)(vz / S));
                                            int index = Terrain.TerrainEdits.VoxelIndex((int)(vx % S), (int)(vy % S), (int)(vz % S));
                                            if (!byCell.TryGetValue(cell, out var list))
                                                byCell[cell] = list = new List<Terrain.TerrainEdits.VoxelValue>();
                                            if (list.All(v => v.Index != index))
                                                list.Add(material == 0 ? new Terrain.TerrainEdits.VoxelValue(index, 0, 0, 0, 0, 0)
                                                    : new Terrain.TerrainEdits.VoxelValue(index, 3, material, c, c, c));
                                        }
                                    }
                                foreach ((ulong cell, var list) in byCell)
                                    Terrain.EnvironmentServer.Instance.Write(cell, list, session.ZoneId, session.PlayerGuid);
                                Terrain.ZoneTerrain.Broadcast(byCell.Keys.ToList(), session.ZoneId);
                                log.Info($"wall: {session.CharacterName} {(material == 0 ? "cleared" : "built")} a {length:0.#} m × {height:0.#} m wall along {(alongX ? "+x" : "+z")} from "
                                    + $"{origin} ({byCell.Values.Sum(l => l.Count)} voxels in {byCell.Count} cell(s)){(door > 0 ? $", doorway {door:0.#} m wide in the middle" : "")}.");
                            }
                            break;
                        }
                        case "outpostwake":
                            // outpostwake <n>: outpost n's crystal wakes, as when its Elite dies.
                            Outposts.EliteDied(Outposts.ByIndex(int.Parse(parts[1])).Cavern, null);
                            break;
                        case "outpostuse":
                        {
                            // outpostuse <n> chest|crystal: as if the player clicked that outpost's Lost Chest / Lost Crystal.
                            string[] a = parts[1].Split(' ');
                            var o = Outposts.ByIndex(int.Parse(a[0]));
                            foreach (WorldSession session in targets)
                                if (a[1] == "chest")
                                    Outposts.OpenChest(session, o.Chest.Guid);
                                else
                                    Outposts.UseCrystal(session, o.Crystal.Guid);
                            break;
                        }
                        case "barrenrecipes":
                        {
                            // barrenrecipes [drop]: logs the twelve "Recipe: Barren …" items (L44) — built or not, prop, palette
                            // knowledge — for each player; "drop" leaves a loot bag at their feet with one they don't know,
                            // picked as a Barren kill picks it (BarrenRecipes.RandomUnknown, the chance roll skipped).
                            bool drop = parts.Length > 1 && parts[1].Trim() == "drop";
                            foreach (WorldSession session in targets)
                            {
                                foreach (BarrenRecipes.Recipe r in BarrenRecipes.All)
                                    log.Info($"barrenrecipes {session.CharacterName}: {r.ItemId} {r.Name} → prop {r.PropId}: "
                                        + $"built {Inventory.ItemDefinitionManager.Get(r.ItemId) != null}, taught {RecipeBook.Teaches(r.ItemId)}, "
                                        + $"known {RecipeBook.KnowsProp(session, r.PropId)}, in bag {session.Inventory.CountOf(r.ItemId)}");
                                if (drop)
                                {
                                    uint item = BarrenRecipes.RandomUnknown(session, new Random());
                                    if (item != 0)
                                        LootPinatas.SpawnBag(session, session.Position, new[] { (item, 1u) });
                                    log.Info($"barrenrecipes {session.CharacterName}: drop {(item != 0 ? item.ToString() : "none (all known)")}.");
                                }
                            }
                            break;
                        }
                        case "gc":
                            // gc: full compacting garbage collection (memory measurements), then the heap size.
                            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                            GC.Collect(2, GCCollectionMode.Forced, true, true);
                            log.Info($"gc: heap {GC.GetTotalMemory(false) / (1024 * 1024)} MB, {Terrain.EnvironmentServer.Instance.MemoryReport()}");
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
                        case "lootfree":
                        {
                            // lootfree <item> [count]: one OWNERLESS loot bag (L141 claim test) 3 m ahead of the first
                            // target, shown to every player on that landscape within 50 m; the first to come within 5 m
                            // claims it (NotifyOwnerlessLootClaimed to all), and only the claimer's client picks it up.
                            string[] a = parts[1].Split(' ');
                            if (targets.FirstOrDefault() is WorldSession first)
                            {
                                var at = first.Position + new System.Numerics.Vector3(3f, 0.5f, 0f);
                                var viewers = PlayerManager.InZone(first.ZoneId)
                                    .Where(v => System.Numerics.Vector3.Distance(v.Position, at) <= 50f).ToList();
                                LootPinatas.SpawnOwnerless(viewers, at, new[] { (uint.Parse(a[0]), a.Length > 1 ? uint.Parse(a[1]) : 1u) });
                                log.Info($"lootfree: ownerless bag at {at} for {viewers.Count} player(s).");
                            }
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
                        case "graveyard":
                        {
                            // graveyard [go] [cavern|surface]: the landscape's graveyards (counts, the player's layer, whether
                            // they stand in a buffer zone) and the nearest one of their layer (or the named one); "go" moves
                            // the player to its arrival point (Graveyards.Landscape.cs, FEATURES #47).
                            string[] a = parts.Length > 1 ? parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
                            bool? cavern = a.Contains("cavern") ? true : a.Contains("surface") ? false : null;
                            foreach (WorldSession session in targets)
                            {
                                (string report, System.Numerics.Vector3? arrival, bool underground) = Graveyards.Describe(session, cavern);
                                log.Info($"graveyard ({session.CharacterName}): {report}");
                                Chat.System(session, report);
                                if (a.Contains("go") && arrival is System.Numerics.Vector3 to)
                                    Teleport.To(session, to, snapToGround: !underground, loadingScreen: true);
                            }
                            break;
                        }
                        case "terrainmem":
                            // terrainmem: edited terrain cells held, how many expanded, compact bytes.
                            log.Info(Terrain.EnvironmentServer.Instance.MemoryReport());
                            break;
                        case "terminal":
                        {
                            // terminal <hub | c<index> | hex node> [open 0/1]: TeleportBase ActivateTerminal, as if
                            // the player used that portal crystal (hub and c<index>: of the player's own landscape).
                            string[] a = parts[1].Split(' ');
                            foreach (WorldSession session in targets)
                            {
                                ulong node = a[0] == "hub" ? WarpNetwork.HubNodeOn(session.ZoneId)
                                    : a[0].StartsWith('c') ? WarpNetwork.CrystalNodeOn(session.ZoneId, int.Parse(a[0][1..]))
                                    : Convert.ToUInt64(a[0], 16);
                                WarpNetwork.ActivateTerminal(session, node, a.Length > 1 && a[1] == "1");
                                log.Info($"terminal {a[0]} = node {node:X} (landscape {session.ZoneId})");
                            }
                            break;
                        }
                        case "grantaccess":
                        {
                            // grantaccess <player> <hex mask>: Special Access for a player on this player's own site
                            // (0 = No Touching, 80000000 removes it as Site Management's delete does; 11F = Ultimate).
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
                                    Terrain.EnvironmentServer.Instance.Write(cell, list, session.ZoneId, session.PlayerGuid);
                                Terrain.ZoneTerrain.Broadcast(byCell.Keys.ToList(), session.ZoneId);
                                foreach (ulong cell in byCell.Keys)
                                    log.Info($"{session.CharacterName}: ownblock cell {cell:X16}: " + string.Join(", ",
                                        Terrain.EnvironmentServer.Instance.OwnershipCensus(session.ZoneId, cell).Select(kv => $"{kv.Key} owns {kv.Value}")));
                            }
                            break;
                        }
                        case "cavecheck":
                        {
                            // cavecheck [zone|all]: does every cavern entry point of the landscape have a floor (Caves.CheckFloors), and
                            // where do its cavern graveyards' arrivals stand: on the plinth, the client's own floor under them, their
                            // depth below the ground, how many random trips may use (Graveyards.CheckReport). all = every landscape
                            // with cavern graveyards, in the background.
                            string which = parts.Length > 1 ? parts[1].Trim() : "";
                            if (which.Equals("all", StringComparison.OrdinalIgnoreCase))
                            {
                                List<uint> lands = Caves.GraveyardLandscapes.ToList();
                                System.Threading.Tasks.Task.Run(() =>
                                {
                                    foreach (uint land in lands)
                                        log.Info("cavecheck: " + Graveyards.CheckReport(land));
                                });
                                log.Info($"cavecheck: checking the cavern graveyards of {lands.Count} landscape(s) in the background.");
                                break;
                            }
                            uint zone = which.Length > 0 ? uint.Parse(which) : 0;
                            log.Info("cavecheck: " + Caves.CheckFloors(zone));
                            log.Info("cavecheck: " + Graveyards.CheckReport(zone == 0 ? Terrain.TerrainStore.HomeContinent : zone));
                            break;
                        }
                        case "cellstats":
                        {
                            // cellstats [x y z]: what the stored layers of the cell at the player (or a local point) hold.
                            string[] a = parts.Length > 1 ? parts[1].Split(' ') : Array.Empty<string>();
                            foreach (WorldSession session in targets)
                            {
                                var at = a.Length >= 3 ? new System.Numerics.Vector3(float.Parse(a[0]), float.Parse(a[1]), float.Parse(a[2])) : session.Position;
                                log.Info($"cellstats {at}: {Terrain.EnvironmentServer.Instance.Describe(at, session.ZoneId)}");
                            }
                            break;
                        }
                        case "clearcell":
                            // clearcell <hex cell id>: drop every override in one terrain cell on the player's landscape.
                            foreach (WorldSession session in targets)
                            {
                                ulong cell = Convert.ToUInt64(parts[1], 16);
                                Terrain.EnvironmentServer.Instance.ClearCell(cell, session.ZoneId);
                                Terrain.ZoneTerrain.Broadcast(new[] { cell }, session.ZoneId);
                            }
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
                        case "weartint":
                        {
                            // weartint <alias>: re-send the player's own worn outfit / armour slots with another tint
                            // alias (this client only, nothing saved) — to see which ArtTints alias gives which colour
                            // (OPEN-LEADS L103). "weartint" alone re-sends the items' own tints.
                            string alias = parts.Length > 1 ? parts[1].Trim() : null;
                            foreach (WorldSession session in targets)
                                foreach ((uint slot, var item) in session.VisibleEquipment())
                                {
                                    var slotMessage = session.BuildEquipmentSlot(slot, item);
                                    string own = slotMessage.TintAlias;
                                    if (alias != null && !string.IsNullOrEmpty(own))
                                        slotMessage.TintAlias = alias;
                                    session.EnqueueMessage(slotMessage);
                                    log.Info($"weartint: slot {slot} item {item.ItemDefinitionId} model '{slotMessage.ModelName}' tint '{own}' -> '{slotMessage.TintAlias}'");
                                }
                            break;
                        }
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
                        case "propmenu":
                        {
                            // propmenu <guid hex|near>: runs the server side of a right-click on a prop (RadialMenuRequest
                            // {guid, 1}) — the prop menu answer and, in build mode, the prop's name over it
                            // (Props.MenuName.cs). The client shows no menu (its pending right-click target doesn't match,
                            // FUN_14372e1c0), but the label shows and goes after 10 s or the next prop request. "near" =
                            // the nearest placed prop within 20 m.
                            string arg = parts.Length > 1 ? parts[1].Trim() : "near";
                            foreach (WorldSession session in targets)
                            {
                                ulong guid = arg == "near" ? Props.NearestPlaced(session) : Convert.ToUInt64(arg, 16);
                                var body = new byte[12];
                                BitConverter.GetBytes(guid).CopyTo(body, 0);
                                BitConverter.GetBytes(1u).CopyTo(body, 8);
                                RightClickMenus.Handle(session, 1, body);
                                log.Info($"{session.CharacterName}: propmenu 0x{guid:X} (build mode {BuildModeHolster.InBuildMode(session)}).");
                            }
                            break;
                        }
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
                        case "psinstances":
                            // psinstances [clear]: the placed Player Studio designs recorded on the player's current site
                            // (visitors can't copy over them, TemplateInstances); clear drops those records.
                            foreach (WorldSession session in targets)
                            {
                                if (Claims.ClaimAt(session.ZoneId, session.Position) is not ulong here)
                                {
                                    log.Info($"{session.CharacterName}: not on a build site.");
                                    continue;
                                }
                                var rows = TemplateInstances.OnSite(here);
                                foreach (var r in rows)
                                    log.Info($"Site {here}: design {r.TemplateGuid} by 0x{r.PlacedBy:X} at ({r.MinX:0},{r.MinY:0},{r.MinZ:0})-({r.MaxX:0},{r.MaxY:0},{r.MaxZ:0}) {r.PlacedAt:u}");
                                if (parts.Length > 1 && parts[1].Trim() == "clear")
                                    TemplateInstances.Forget(rows);
                                log.Info($"Site {here}: {rows.Count} placed Player Studio design(s){(parts.Length > 1 && parts[1].Trim() == "clear" ? " cleared" : "")}.");
                            }
                            break;
                        case "setwear":
                        {
                            // setwear <item def> <wear points>: wear the player's carried items of that kind (saved and shown;
                            // tests that wear survives trade / mail / chest, audit-wiki-gaps #1). 0 = as new.
                            string[] a = parts[1].Split(' ');
                            foreach (WorldSession session in targets)
                                foreach (var item in session.Inventory.Items.Where(i => i.ItemDefinitionId == uint.Parse(a[0])).ToList())
                                {
                                    item.Wear = Math.Max(0f, float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture));
                                    session.Inventory.SaveWear(item);
                                    session.SendItem(item, replace: true);
                                    log.Info($"{session.CharacterName}: item 0x{item.Id:X} ({item.ItemDefinitionId}) wear {item.Wear:0} condition {Durability.Condition(item)}%");
                                }
                            break;
                        }
                        case "autoresupply":
                        {
                            // autoresupply <item def> [on|off]: the auto-resupply switch of the player's first stack of that
                            // kind, as the client's CoinStoreScreen.TurnOn/OffAutoResupply(def) picks it (Inventory 0x2D sub
                            // 0x0D), without the client's definition check, which no live definition passes. Saved; the
                            // client sees it in the item record at the next login (ClientItem +0xdc). Without on / off it
                            // only logs the switch of every stack of that kind.
                            string[] a = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            uint definition = uint.Parse(a[0]);
                            foreach (WorldSession session in targets)
                            {
                                if (a.Length > 1 && session.Inventory.Items.FirstOrDefault(i => i.ItemDefinitionId == definition) is { } first)
                                    session.Inventory.SetAutoResupply(first, a[1] == "on");
                                foreach (var item in session.Inventory.Items.Where(i => i.ItemDefinitionId == definition))
                                    log.Info($"{session.CharacterName}: item 0x{item.Id:X} ({item.ItemDefinitionId}) auto-resupply {(item.AutoResupply ? "on" : "off")}"
                                        + (Inventory.Inventory.AutoResupplyKept ? "" : " (ItemAutoResupply is off: sent as off)"));
                            }
                            break;
                        }
                        case "durability":
                        {
                            // durability <item def> <value>: sends a carried item's ItemUpdate +0x24 field (test). The
                            // client names it ConsumedCount (item +0xd4, clamped to the count), not durability.
                            string[] a = parts[1].Split(' ');
                            foreach (WorldSession session in targets)
                                foreach (var item in session.Inventory.Items.Where(i => i.ItemDefinitionId == uint.Parse(a[0])))
                                    session.EnqueueMessage(new Network.Message.Model.ClientUpdateItemUpdate
                                    {
                                        Guid = item.Id, Count = item.Count, Slot = item.Slot, Container = item.Container,
                                        ConsumedCount = uint.Parse(a[1])
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
                        case "emotelead":
                            // emotelead <ms>: how long after putting a held tool away an emote is played (EmoteHolster.PutAwayLead).
                            EmoteHolster.PutAwayLead = TimeSpan.FromMilliseconds(double.Parse(parts[1]));
                            log.Info($"Emote lead after a put-away: {EmoteHolster.PutAwayLead.TotalMilliseconds} ms.");
                            break;
                        case "regendelay":
                            // regendelay <seconds>: terrain regeneration delay (default 1200), checked again now.
                            Terrain.EnvironmentServer.Instance.SetRegenerationDelay(TimeSpan.FromSeconds(double.Parse(parts[1])));
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
                                var near = Terrain.EnvironmentServer.Instance.VeinsNear(session.Position, parts.Length > 1 && parts[1] == "go" ? 400f : 80f, session.ZoneId);
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
                                log.Info($"{session.CharacterName} guild {Social.SocialServices.GuildCache.GuildIdOf(session.PlayerGuid)}");
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
                        case "maxplayers":
                            // maxplayers <n> | full | config: the world's limit at run time, reported to the login server's
                            // queue with the next heartbeat (0 = no limit; "full" queues every login, for single-client tests).
                            LoginQueue.Override = parts.Length > 1 && parts[1].Trim() == "full" ? LoginQueue.Full
                                : parts.Length > 1 && int.TryParse(parts[1].Trim(), out int maxPlayers) ? maxPlayers : null;
                            log.Info($"Login queue limit: {LoginQueue.MaxPlayers} (0 = none).");
                            break;
                        case "friendtpcooldown":
                            // friendtpcooldown <seconds>: Teleport to Friend's cooldown at run time (0 = none, as live since
                            // 5 May 2015; 1800 = the 2014 alpha's 30 minutes). Setting FriendTeleportCooldownSeconds.
                            if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out int friendCooldown)
                                && Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config is { } friendConfig)
                                friendConfig.FriendTeleportCooldownSeconds = Math.Max(0, friendCooldown);
                            // The client checks it itself from 187's caster info (L182): everyone gets the new value.
                            foreach (WorldSession session in everyone)
                                Network.Handlers.MiscHandler.SendFriendTeleportCasterInfo(session);
                            log.Info($"Teleport to Friend cooldown: {Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config?.FriendTeleportCooldownSeconds ?? 0} s.");
                            break;
                        case "landscapecap":
                            // landscapecap <n> | full | config | text <id>: the travel queue's per-landscape limit at run
                            // time (0 = none; "full" queues every Travel-window arrival, for single-client tests), or the
                            // text of its "place free" Game Action Queue item (TravelQueue, L60). Prints the queue.
                            if (parts.Length > 1 && parts[1].Trim().StartsWith("text", StringComparison.OrdinalIgnoreCase))
                            {
                                if (uint.TryParse(parts[1].Trim().Substring(4).Trim(), out uint readyText))
                                    TravelQueue.ReadyText = readyText;
                            }
                            else if (parts.Length > 1)
                                TravelQueue.Override = parts[1].Trim() == "full" ? TravelQueue.Full
                                    : int.TryParse(parts[1].Trim(), out int landscapeCap) ? landscapeCap : null;
                            log.Info(TravelQueue.Describe() + $" Ready text {TravelQueue.ReadyText}.");
                            break;
                        case "grief":
                            // grief level <n> | points <n> | damage <n> | award <damage> | show | reset (Grief.cs, L144):
                            // level / points / damage set the state (GriefBase 2 / 1), award <damage> counts that much
                            // damage to an ally as a friendly-fire hit would (even with GRIEF_AWARDS off), show makes the
                            // client print the points it holds (GriefBase 1 with -1, -1), reset clears everything.
                            foreach (WorldSession session in targets)
                            {
                                string[] g = parts.Length > 1 ? parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
                                int value = g.Length > 1 && int.TryParse(g[1], out int v) ? v : 0;
                                switch (g.Length > 0 ? g[0] : "reset")
                                {
                                    case "level": Grief.SetLevel(session, value); break;
                                    case "points": Grief.SetPoints(session, value, -1); break;
                                    case "damage": Grief.SetPoints(session, -1, value); break;
                                    case "award": Grief.AllyDamage(session, null, (uint)Math.Max(0, value), force: true); break;
                                    case "show": Grief.Show(session); break;
                                    default: Grief.Reset(session); break;
                                }
                            }
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
                            // weaponattack basic|charged|power|special [times]: the held weapon's attack at the nearest
                            // creature (Combat.WeaponCombat); "charged" is the basic attack fully charged (held), as the
                            // client's charged cast ends; "special" is an E cast (the kit's Special, else its Power attack).
                            foreach (WorldSession session in targets)
                            {
                                string[] w = parts[1].Split(' ');
                                int times = w.Length > 1 ? int.Parse(w[1]) : 1;
                                for (int i = 0; i < times; i++)
                                    Combat.WeaponCombat.DevAttack(session, w[0] == "power", w[0] == "charged", w[0] == "special");
                            }
                            break;
                        case "status":
                            // status <name> [seconds]: puts a combat status on the player (Combat.Status, e.g. Eruption so
                            // the next fire hits erupt, which is otherwise a 15 % roll).
                            foreach (WorldSession session in targets)
                            {
                                string[] a = parts.Length > 1 ? parts[1].Split(' ') : Array.Empty<string>();
                                if (a.Length > 0 && Enum.TryParse(a[0], true, out Combat.Status status))
                                {
                                    bool on = Combat.Statuses.Apply(session.PlayerGuid, status, session, seconds: a.Length > 1 ? float.Parse(a[1]) : 0f);
                                    log.Info($"{session.CharacterName} status {status}: {(on ? "applied" : "not applied")}.");
                                }
                                else
                                    log.Info($"status: unknown status '{(a.Length > 0 ? a[0] : "")}'.");
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
                        case "offgridpaste":
                        {
                            // offgridpaste <yaw degrees> [design guid]: the off-grid paste (VoxelConstruction 0xF4 sub 5) of the
                            // clipboard (or a design) with its corner 2 m in front of the player, turned by that yaw (Building.DevOffGridPaste).
                            string[] a = parts.Length > 1 ? parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
                            float yaw = a.Length > 0 && float.TryParse(a[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y) ? y : 0f;
                            ulong design = a.Length > 1 && ulong.TryParse(a[1], out ulong d) ? d : 0;
                            foreach (WorldSession session in targets)
                                log.Info($"{session.CharacterName} offgridpaste yaw {yaw} design {design}: " +
                                    (Building.DevOffGridPaste(session, design, session.Position + new System.Numerics.Vector3(0, 0, 2), yaw) ? "placed" : "refused"));
                            break;
                        }
                        case "pathquery":
                        {
                            // pathquery dx dy dz: the PathFindVisualize tool's DebugPathQueryRequest (0x1E / 0x4A7) from the player
                            // to that offset, answered in chat (AdminCommands.DebugPathQuery; admins only, as from the client).
                            string[] a = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            foreach (WorldSession session in targets)
                            {
                                var to = session.Position + new System.Numerics.Vector3(float.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture),
                                    float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture));
                                var body = new List<byte>();
                                foreach (float f in new[] { session.Position.X, session.Position.Y, session.Position.Z, 1f, to.X, to.Y, to.Z, 1f })
                                    body.AddRange(BitConverter.GetBytes(f));
                                body.Add(0);
                                AdminCommands.Handle(session, AdminCommands.DebugPathQuerySub, body.ToArray());
                            }
                            break;
                        }
                        case "undo":
                        case "redo":
                            // undo / redo: as Ctrl+Z / Ctrl+Y (VoxelConstruction sub 3).
                            foreach (WorldSession session in targets)
                                Building.Handle(session, Building.UndoRedoSub, BitConverter.GetBytes(parts[0] == "redo" ? 1u : 0u));
                            break;
                        case "designoffer":
                            // designoffer <design guid hex>: as "Get Resources Now" (TemplateBase 0x1D): the missing materials offer.
                            foreach (WorldSession session in targets)
                                DesignMaterials.SendOffer(session, Convert.ToUInt64(parts[1], 16));
                            break;
                        case "stampdry":
                        {
                            // stampdry [n]: counts outpost n's template voxels as the stamp sees them (air / cave air), writes nothing.
                            var o = Outposts.ByIndex(parts.Length > 1 ? int.Parse(parts[1]) : 0);
                            var t = Terrain.VoxelTemplate.Load(System.IO.Path.Combine(Terrain.VoxelTemplate.Folder, o.Ruin + ".otd"));
                            long changedCount = 0, airCount = 0, caveAir = 0;
                            string sample = "";
                            var hist = new Dictionary<(byte, ushort), long>();
                            foreach (var cell in t.Cells)
                                for (int i = 0; i < Terrain.BlockData.Count; i += 7)
                                {
                                    if ((cell.Changed[i >> 3] & (0x80 >> (i & 7))) == 0)
                                        continue;
                                    changedCount++;
                                    byte f = cell.Blocks.Flags[i];
                                    ushort m = cell.Blocks.Materials[i];
                                    hist[(f, m)] = hist.GetValueOrDefault((f, m)) + 1;
                                    bool air = (f & 1) != 0 && (m == 0 || m == 0xFFFC);
                                    if (!air)
                                        continue;
                                    airCount++;
                                    int x = i % 40, y = i / 40 % 40, z = i / 1600;
                                    long gx = o.Corner.X + cell.X * 40L + x, gy = o.Corner.Y + cell.Y * 40L + y, gz = o.Corner.Z + cell.Z * 40L + z;
                                    if (Terrain.CavePieces.IsAir((gx + 0.5) * 4, (gy + 0.5) * 4, (gz + 0.5) * 4, o.Zone))
                                        caveAir++;
                                    else if (sample.Length == 0 && y > 30)
                                        sample = $"rock at voxel ({gx},{gy},{gz}) flags {f} mat {m}";
                                }
                            // The column through the template's middle: per 1 m (2-3 voxels), T = template solid, a = template air,
                            // f = interior air, v = vertex-only, . = not written; then C = cave air / R = cave rock.
                            {
                                var byCell = t.Cells.ToDictionary(c => (c.X, c.Y, c.Z));
                                long mx = t.Size.X / 2, mz = t.Size.Z / 2;
                                var col = new System.Text.StringBuilder();
                                var cave = new System.Text.StringBuilder();
                                for (long vy = 0; vy < Math.Min(t.Size.Y, 120); vy += 3)
                                {
                                    char ch = '.';
                                    if (byCell.TryGetValue(((int)(mx / 40), (int)(vy / 40), (int)(mz / 40)), out var cc))
                                    {
                                        int i = Terrain.BlockData.Index((int)(mx % 40), (int)(vy % 40), (int)(mz % 40));
                                        if ((cc.Changed[i >> 3] & (0x80 >> (i & 7))) != 0)
                                        {
                                            byte f = cc.Blocks.Flags[i]; ushort m = cc.Blocks.Materials[i];
                                            ch = (f & 1) == 0 ? 'v' : m == 0 ? 'a' : m == 0xFFFC ? 'f' : 'T';
                                        }
                                    }
                                    col.Append(ch);
                                    cave.Append(Terrain.CavePieces.IsAir((o.Corner.X + mx + 0.5) * 4, (o.Corner.Y + vy + 0.5) * 4, (o.Corner.Z + mz + 0.5) * 4, o.Zone) ? 'C' : 'R');
                                }
                                log.Info($"stampdry column (bottom up, 1.2 m steps): template {col} / cave {cave}");
                            }
                            log.Info($"stampdry {o.Name}: every 7th voxel: {changedCount} changed, {airCount} air, {caveAir} on cave air; corner {o.Corner}; {sample}; top (flags, material): " + string.Join(", ", hist.OrderByDescending(h => h.Value).Take(6).Select(h => $"{h.Key}={h.Value}")));
                            break;
                        }
                        case "isair":
                        {
                            // isair [n]: cave air (CavePieces.IsAir) on a vertical line through outpost n's floor middle, every 4 m.
                            var o = Outposts.ByIndex(parts.Length > 1 ? int.Parse(parts[1]) : 0);
                            var sb = new System.Text.StringBuilder($"outpost {o.Name} floor {o.Floor}:");
                            for (int dy = -8; dy <= 40; dy += 4)
                            {
                                var g = Terrain.TerrainEdits.ToGlobal(o.Floor + new System.Numerics.Vector3(0, dy, 0));
                                sb.Append($" {dy}:{(Terrain.CavePieces.IsAir(g.Item1, g.Item2, g.Item3, o.Zone) ? "air" : "rock")}");
                            }
                            log.Info(sb.ToString());
                            break;
                        }
                        case "launch":
                            // launch <prop hex>: launches a Fireworks Emitter for the players in its zone (as its Link action).
                            if (Props.Find(Convert.ToUInt64(parts[1], 16)) is Props.PlacedProp emitter)
                                PropEffects.Launch(targets, emitter.PropId, Convert.ToUInt64(parts[1], 16));
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
                catch (Exception exception) when (IsBadArgument(exception) && UsageOf(parts[0]) is string usageLine)
                {
                    // An argument that is not a number / hex / id where one is needed: the usage line, not an error.
                    Reply(commandSource, replies, $"usage: {usageLine} ({exception.GetType().Name}: {exception.Message})");
                }
                catch (Exception exception)
                {
                    log.Error(exception, $"Dev command failed ({commandSource}): {line}");
                }
            }
            return replies;
        }
    }
}
