using LandmarkEmulator.Shared.GameTable;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Game.Terrain;
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
    /// Outposts of the Chaos Caverns (wiki "Outposts", "Lost Chests", "Lost Crystals", "Elite Monsters", launch 2016):
    /// player-built ruins (the "Cavern Ruins" contest winners) placed by the developers in the largest caverns. Each has
    /// monsters and one Elite, one Lost Chest (more loot than a common chest, often half buried) and one Lost Crystal,
    /// which wakes ("a spire of light") once the Elite is dead; using it channels a few seconds (not in combat; a hit
    /// cancels it), gives loot and offers a trip to another cavern ("Eureka!").
    /// <para>
    /// Built from the client's own data: the ruins are its templates CommonData/Voxel/templates/cavern_ruins_*.otd
    /// (version 2; stamped into the cavern like the Spire's base, cells kept from regrowth); their garrisons are the
    /// "Spawner: Ruins: &lt;faction&gt; Patroller / Melee / Ranged / Caster / BOSS" props (models and names from the prop
    /// dump; the BOSS is the Elite, shown with the boss display); the Lost Chest is a "Lost Treasure Chest" prop
    /// (2930), the crystal the "Lost Crystal" prop (2654). Effects: chest glow 11279 / open 12830, crystal light 10621,
    /// discovery fanfare 11674. The trip is TeleportBase NextUndergroundOffer (0xFC sub 0x0C {u64}, client dialog
    /// Teleport.LeylineTeleportDialogTitle "Eureka!") answered by NextUndergroundReply (sub 0x0D {u64}).
    /// Where live put each outpost is not known: here the largest open caverns, spread out, one ruin each.
    /// </para>
    /// </summary>
    public static class Outposts
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>The complete ruins (parts like "(1 of 2 claims)" and the empty "cavern_ruin_template" left out).</summary>
        /// <summary>
        /// The complete ruins (parts like "(1 of 2 claims)" and the empty "cavern_ruin_template" left out). Stamped cells
        /// are kept compact in memory (TerrainEdits), so even the large ones (shipwreck, the portal: up to 245 cells)
        /// cost only a few KB each.
        /// </summary>
        private static readonly string[] Ruins =
        {
            "cavern_ruins_frozen_in_time", "cavern_ruins_scorpion_altar", "cavern_ruins_abandoned_foundry",
            "cavern_ruins_team-up_winner_-_frodrick's_life_orb_containment_pod", "cavern_ruins_team-up_winner_-_grimstone_gate",
            "cavern_ruins_team-up_winner_-_grimstone_henge", "cavern_ruins_team-up_winner_-_kjac's_ogre_ruin",
            "cavern_ruins_team-up_winner_-_pantera's_alien_idol_ruin", "cavern_ruins_team-up_winner_-_temple_of_terror",
            "cavern_ruins_team-up_winner_-_titan", "cavern_ruins_lost_in_time",
            "cavern_ruins_team-up_winner_-__ancient_cathedral_of_the_bat", "cavern_ruins_team-up_winner_-_amberwood_shrine_complete",
            "cavern_ruins_team-up_winner_-_desire's_end", "cavern_ruins_team-up_winner_-_grand_orrery",
            "cavern_ruins_team-up_winner_-_last_of_the_sun_magi", "cavern_ruins_team-up_winner_-_lovedmadly's_teamup_astroid_cavern_ruin",
            "cavern_ruins_team-up_winner_-_ogresnots_statue_of_statues", "cavern_ruins_team-up_winner_-_persistant_peek",
            "cavern_ruins_team-up_winner_-_shipwreck_combined", "cavern_ruins_team-up_winner_-_team_up_ruins_(_combined_)",
            "cavern_ruins_team-up_winner_-_the_portal_complete", "cavern_ruins_the_lost_terminal"
        };

        /// <summary>The ruin spawner sets: patroller, melee, ranged, caster, boss (Elite).</summary>
        private static readonly (string Faction, uint[] Spawners)[] Garrisons =
        {
            ("Barren", new uint[] { 2702, 2703, 2704, 2705, 2706 }),
            ("Scalewind", new uint[] { 2707, 2708, 2709, 2710, 2711 }),
            ("Highfall", new uint[] { 2712, 2713, 2714, 2715, 2716 }),
            ("NovaTech", new uint[] { 2717, 2718, 2719, 2719, 2720 }),
            ("Ebonguard", new uint[] { 2721, 2722, 2723, 2724, 2725 }),
            ("Stoneskin", new uint[] { 2726, 2727, 2728, 2729, 2730 })
        };

        private const uint LostChestProp = 2930, LostCrystalProp = 2654;
        private const uint ChestGlow = 11279, ChestOpen = 12830, CrystalLight = 10621, DiscoveryFanfare = 11674,
            ChannelEffect = 11673;   // PFX_Teleport_LeylineCrystal_Cave on the channelling player
        private const ulong GuidBase = 0x5450_0000_0000_0000;
        private const string Marker = "outposts.txt";
        /// <summary>Everyone this close to an opened Lost Chest gets their own loot (wiki "Chests": 50 m).</summary>
        private const float ChestShareRange = 50f;
        private static readonly TimeSpan ChestRespawn = TimeSpan.FromMinutes(20), ChannelTime = TimeSpan.FromSeconds(5),
            InCombatFor = TimeSpan.FromSeconds(6);
        private const float ViewRange = 160f, DropRange = 220f, NameRange = 60f;
        private const ulong NextCaveToken = 0x4C43;   // the offer's u64; echoed by the reply

        public sealed class Outpost
        {
            public int Index;
            public int Cavern;
            public string Ruin;
            public string Name;
            public int Garrison;
            public Vector3 Floor;
            public (uint X, uint Y, uint Z) Corner;
            public VoxelTemplate Template;
            public List<PlayerUpdateAddLightweightProp> Props = new();
            public PlayerUpdateAddLightweightProp Chest, Crystal;
            public bool ChestReady = true;
            public DateTime ChestBack;
            public bool EliteDead;
        }

        private static readonly List<Outpost> outposts = new();
        private static readonly Dictionary<int, Outpost> byCavern = new();
        private static readonly object sync = new();
        private static readonly Dictionary<WorldSession, HashSet<int>> sent = new();
        private static readonly Dictionary<WorldSession, HashSet<int>> named = new();
        private static readonly Dictionary<WorldSession, (Outpost Outpost, DateTime Until)> channels = new();
        private static readonly Random random = new();
        private static DateTime nextStream;

        public static IReadOnlyList<Outpost> All => outposts;

        /// <summary>
        /// Picks the caverns and stamps the ruins once (remembered in TerrainEdits/outposts.txt: cavern, ruin, corner
        /// cell), then builds each outpost's props.
        /// </summary>
        public static void Load(string editsFolder)
        {
            if (Caves.Caverns.Count == 0 || !Directory.Exists(VoxelTemplate.Folder))
                return;
            string marker = Path.Combine(editsFolder, Marker);
            var saved = new List<(int Cavern, string Ruin, (uint, uint, uint) Corner)>();
            if (File.Exists(marker))
                foreach (string line in File.ReadAllLines(marker))
                {
                    string[] f = line.Split('\t');
                    if (f.Length == 5)
                        saved.Add((int.Parse(f[0]), f[1], (uint.Parse(f[2]), uint.Parse(f[3]), uint.Parse(f[4]))));
                }
            // Ruins not placed yet (a first start, or ruins added to the list later) get new caverns.
            int stampedBefore = saved.Count;
            string[] missing = Ruins.Where(r => saved.All(x => x.Ruin != r)).ToArray();
            if (missing.Length > 0)
                foreach ((int cavern, string ruin) in Choose(missing, saved.Select(x => x.Cavern).ToList()))
                    saved.Add((cavern, ruin, default));
            bool stampedAny = false;
            var lines = new List<string>();
            for (int n = 0; n < saved.Count; n++)
            {
                (int cavern, string ruin, (uint, uint, uint) savedCorner) = saved[n];
                bool stamp = n >= stampedBefore;
                (uint, uint, uint) corner = savedCorner;
                VoxelTemplate template;
                try
                {
                    template = VoxelTemplate.Load(Path.Combine(VoxelTemplate.Folder, ruin + ".otd"));
                }
                catch (Exception e)
                {
                    log.Warn($"Outpost ruin {ruin} unreadable: {e.Message}");
                    continue;
                }
                Vector3 floor = Caves.Floor(Caves.Caverns[cavern].At);
                var o = new Outpost
                {
                    Index = outposts.Count,
                    Cavern = cavern,
                    Ruin = ruin,
                    Name = Title(ruin),
                    Garrison = (int)((uint)HashCode.Combine(cavern, 0x4f75) % (uint)Garrisons.Length),
                    Floor = floor,
                    Template = template
                };
                if (stamp)
                {
                    // The ruin's footprint centred on the cavern floor, its base a little into the ground.
                    (double gx, double gy, double gz) = TerrainEdits.ToGlobal(floor);
                    double cell = TerrainEdits.CellSize, voxel = TerrainEdits.VoxelSize;
                    corner = ((uint)Math.Round((gx - template.Size.X * voxel / 2) / cell), (uint)Math.Floor((gy - 10) / cell),
                        (uint)Math.Round((gz - template.Size.Z * voxel / 2) / cell));
                    template.Stamp(corner.Item1, corner.Item2, corner.Item3, TerrainStore.HomeContinent);
                    // Saved and packed at once, so stamping every ruin never holds them all expanded.
                    TerrainEdits.Flush();
                    TerrainEdits.CompactNow(true);
                    stampedAny = true;
                }
                o.Corner = corner;
                TerrainEdits.MarkPermanent(template.Cells.Select(c => TerrainStore.CellId(TerrainEdits.Level,
                    (uint)(corner.Item1 + c.X), (uint)(corner.Item2 + c.Y), (uint)(corner.Item3 + c.Z))), TerrainStore.HomeContinent);
                BuildProps(o);
                o.Template = null;      // only the props are kept: the cells live in the terrain overlays now
                outposts.Add(o);
                byCavern[cavern] = o;
                lines.Add($"{cavern}\t{ruin}\t{corner.Item1}\t{corner.Item2}\t{corner.Item3}");
            }
            if (stampedAny)
            {
                Directory.CreateDirectory(editsFolder);
                File.WriteAllLines(marker, lines);
            }
            log.Info($"Outposts: {outposts.Count} in the Chaos Caverns ({saved.Count - stampedBefore} stamped now). {TerrainEdits.MemoryReport()}");
        }

        /// <summary>
        /// The largest open caverns, at least 400 m apart: open floor all round at 25 m and 40 m from the middle.
        /// </summary>
        private static List<(int Cavern, string Ruin)> Choose(string[] ruins, List<int> taken)
        {
            var scored = new List<(int Index, int Space)>();
            for (int i = 0; i < Caves.Caverns.Count; i++)
            {
                Vector3 floor = Caves.Floor(Caves.Caverns[i].At);
                int space = 0;
                foreach (float r in new[] { 25f, 40f })
                    for (int k = 0; k < 12; k++)
                    {
                        double a = k * Math.PI / 6;
                        if (Caves.FloorNear(floor + new Vector3((float)(Math.Cos(a) * r), 3f, (float)(Math.Sin(a) * r)), 8f) is Vector3 p
                            && MathF.Abs(p.Y - floor.Y) < 8f)
                            space++;
                    }
                scored.Add((i, space));
            }
            var chosen = new List<int>();
            foreach ((int index, int space) in scored.OrderByDescending(s => s.Space).ThenBy(s => s.Index))
            {
                if (chosen.Count >= ruins.Length || space < 16)
                    break;
                Vector3 at = Caves.Caverns[index].At;
                if (chosen.Concat(taken).All(c => Vector3.Distance(Caves.Caverns[c].At, at) >= 400f))
                    chosen.Add(index);
            }
            return chosen.Select((c, n) => (c, ruins[n])).ToList();
        }

        /// <summary>"cavern_ruins_team-up_winner_-_kjac's_ogre_ruin" → "Kjac's Ogre Ruin".</summary>
        private static string Title(string ruin)
        {
            string s = ruin.Replace("cavern_ruins_team-up_winner_-_", "").Replace("cavern_ruins_", "").Replace('_', ' ').Trim();
            return string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Length > 1 && char.IsLetter(w[0]) ? char.ToUpperInvariant(w[0]) + w[1..] : w));
        }

        /// <summary>The ruin's own props where it puts them, plus the Lost Chest and the Lost Crystal.</summary>
        private static void BuildProps(Outpost o)
        {
            Vector3 corner = CornerLocal(o);
            ulong next = 0;
            foreach (VoxelTemplate.Prop p in o.Template.Props.Where(p => p.ModelId != 0))
                o.Props.Add(Prop(o, next++, p.ModelId, corner + p.Position, p.Rotation, p.Scale, 0));
            // The chest half buried a little way from the middle, the crystal by the middle (wiki: chests "often hidden
            // and partially buried", crystals in the outpost).
            Vector3 chestAt = Caves.FloorNear(o.Floor + new Vector3(9f, 2f, 7f), 10f) ?? o.Floor;
            o.Chest = Prop(o, 1000, Props.ModelOf(LostChestProp), chestAt - new Vector3(0f, 0.35f, 0f), Quaternion.Identity, Vector3.One, LostChestProp);
            Vector3 crystalAt = Caves.FloorNear(o.Floor + new Vector3(-6f, 2f, -4f), 10f) ?? o.Floor;
            o.Crystal = Prop(o, 1001, Props.ModelOf(LostCrystalProp), crystalAt, Quaternion.Identity, Vector3.One, LostCrystalProp);
        }

        private static Vector3 CornerLocal(Outpost o)
        {
            var origin = TerrainStore.ZoneOrigin;
            double cell = TerrainEdits.CellSize;
            return new Vector3((float)((o.Corner.X * cell - origin.X) / 10), (float)((o.Corner.Y * cell - origin.Y) / 10),
                (float)((o.Corner.Z * cell - origin.Z) / 10));
        }

        private static PlayerUpdateAddLightweightProp Prop(Outpost o, ulong n, uint model, Vector3 at, Quaternion rotation, Vector3 scale, uint propId) => new()
        {
            Guid = GuidBase | ((ulong)o.Index << 16) | n,
            TransientId = PlayerManager.AllocateTransientId(),
            ModelId = model,
            Position = new Vector4(at, 1f),
            Rotation = new Vector4(rotation.X, rotation.Y, rotation.Z, rotation.W),
            Scale = new Vector4(scale, 1f),
            NameId = propId == 0 ? 0 : GameTableManager.Instance.PropDefs.GetEntry(propId)?.NameStringId ?? 0
        };

        public static string GarrisonName(Outpost o) => Garrisons[o.Garrison].Faction;

        // ---- Garrisons (Creatures puts them into the cavern's group) --------------------------------------------------

        /// <summary>The outpost's garrison as (spawner prop, its name, boss) — two patrollers, two melee, ranged, caster, Elite.</summary>
        public static IEnumerable<(uint Spawner, string Name, bool Boss, Vector3 Near)> GarrisonFor(int cavern)
        {
            if (!byCavern.TryGetValue(cavern, out Outpost o))
                yield break;
            uint[] s = Garrisons[o.Garrison].Spawners;
            foreach ((int slot, int n) in new[] { (0, 2), (1, 2), (2, 1), (3, 1), (4, 1) })
                for (int i = 0; i < n; i++)
                    yield return (s[slot], Creatures.PropName(s[slot]), slot == 4, o.Floor);
        }

        /// <summary>The Elite fell: the crystal wakes (its light), for everyone who has the outpost on screen.</summary>
        public static void EliteDied(int cavern, WorldSession killer)
        {
            if (!byCavern.TryGetValue(cavern, out Outpost o))
                return;
            o.EliteDead = true;
            HitEffects.PlayOn(o.Crystal.Guid, CrystalLight, Viewers(o));
            if (killer != null)
                Chat.System(killer, "The Lost Crystal stirs: a spire of light shoots from it.");
        }

        /// <summary>The Elite is back: the crystal sleeps again.</summary>
        public static void EliteBack(int cavern)
        {
            if (byCavern.TryGetValue(cavern, out Outpost o))
                o.EliteDead = false;
        }

        // ---- Streaming ---------------------------------------------------------------------------------------------

        public static void Update()
        {
            DateTime now = DateTime.UtcNow;
            CompleteChannels(now);
            if (now < nextStream || outposts.Count == 0)
                return;
            nextStream = now.AddSeconds(1);
            foreach (WorldSession session in PlayerManager.Snapshot())
                Stream(session, now);
            foreach (Outpost o in outposts.Where(o => !o.ChestReady && now >= o.ChestBack))
            {
                o.ChestReady = true;
                foreach (WorldSession s in Viewers(o))
                    SendChest(s, o);
            }
        }

        private static void Stream(WorldSession session, DateTime now)
        {
            if (session.StreamsOnHold)
                return;
            HashSet<int> have, seen;
            lock (sync)
            {
                if (!sent.TryGetValue(session, out have))
                    sent[session] = have = new HashSet<int>();
                if (!named.TryGetValue(session, out seen))
                    named[session] = seen = new HashSet<int>();
            }
            bool home = session.OnHomeContinent;
            foreach (Outpost o in outposts)
            {
                float d = Vector3.Distance(o.Floor, session.Position);
                if (home && d <= ViewRange && have.Add(o.Index))
                {
                    foreach (PlayerUpdateAddLightweightProp p in o.Props)
                        session.EnqueueMessage(p);
                    session.EnqueueMessage(o.Crystal);
                    if (o.ChestReady)
                        SendChest(session, o);
                    var flagging = new DynamicInteractionFlagging();
                    flagging.Guids.Add(o.Crystal.Guid);
                    flagging.Guids.Add(o.Chest.Guid);
                    session.EnqueueMessage(flagging);
                    if (o.EliteDead)
                        HitEffects.PlayOn(o.Crystal.Guid, CrystalLight, new[] { session });
                }
                else if ((!home || d > DropRange) && have.Remove(o.Index))
                {
                    var remove = o.Props.Select(p => p.Guid).Append(o.Crystal.Guid).Append(o.Chest.Guid);
                    foreach (ulong guid in remove)
                        session.EnqueueMessage(new PlayerUpdateRemovePlayer { Guid = guid });
                }
                // The outpost's name as players come in (wiki: "The name of the Outpost appears in white text").
                if (home && d <= NameRange && seen.Add(o.Index))
                    Chat.System(session, $"Outpost: {o.Name}");
            }
        }

        private static void SendChest(WorldSession session, Outpost o)
        {
            session.EnqueueMessage(o.Chest);
            HitEffects.PlayOn(o.Chest.Guid, ChestGlow, new[] { session });
        }

        private static List<WorldSession> Viewers(Outpost o)
        {
            lock (sync)
                return sent.Where(s => s.Value.Contains(o.Index)).Select(s => s.Key).ToList();
        }

        public static void Forget(WorldSession session)
        {
            lock (sync)
            {
                sent.Remove(session);
                named.Remove(session);
                channels.Remove(session);
            }
        }

        // ---- Interaction ---------------------------------------------------------------------------------------------

        private static Outpost ByGuid(ulong guid) =>
            (guid & 0xFFFF_0000_0000_0000) == GuidBase ? outposts.ElementAtOrDefault((int)((guid >> 16) & 0xFFFF)) : null;

        public static bool IsChest(ulong guid) => ByGuid(guid) is Outpost o && o.Chest.Guid == guid;
        public static bool IsCrystal(ulong guid) => ByGuid(guid) is Outpost o && o.Crystal.Guid == guid;
        public static bool Usable(ulong guid) => IsChest(guid) || IsCrystal(guid);

        public static bool SendFull(WorldSession session, ulong guid)
        {
            if (ByGuid(guid) is not Outpost o)
                return false;
            PlayerUpdateAddLightweightProp p = o.Chest.Guid == guid ? o.Chest : o.Crystal.Guid == guid ? o.Crystal
                : o.Props.FirstOrDefault(x => x.Guid == guid);
            if (p == null)
                return false;
            session.EnqueueMessage(new PlayerUpdateLightweightToFullProp { TransientId = p.TransientId });
            return true;
        }

        /// <summary>
        /// A Lost Chest opened: everyone within 50 m gets their own loot (wiki "Chests": "anyone within 50m of the chest
        /// will get the loot … each person … different items") — plenty of resources, a chance at a recipe and at High
        /// Yield Dynamite; the chest goes and comes back later.
        /// </summary>
        public static void OpenChest(WorldSession session, ulong guid)
        {
            if (ByGuid(guid) is not Outpost o || !o.ChestReady || Health.IsDead(session))
                return;
            o.ChestReady = false;
            o.ChestBack = DateTime.UtcNow + ChestRespawn;
            List<WorldSession> viewers = Viewers(o);
            HitEffects.PlayOn(o.Chest.Guid, ChestOpen, viewers);
            Vector3 at = new(o.Chest.Position.X, o.Chest.Position.Y, o.Chest.Position.Z);
            foreach (WorldSession p in PlayerManager.Snapshot().Where(p => p.OnHomeContinent && Vector3.Distance(p.Position, at) <= ChestShareRange))
            {
                var loot = new List<(uint, uint)>();
                uint[] resources = { 34, 35, 36, 37, 39, 51, 56, 57, 58, 2000, 2001, 2002, 46 };
                for (int i = 0; i < 4; i++)
                    loot.Add((resources[random.Next(resources.Length)], (uint)random.Next(10, 31)));
                if (random.NextDouble() < 0.3 && RecipeBook.RandomUnknownRecipeItem(p, random) is uint recipe and not 0)
                    loot.Add((recipe, 1));
                if (random.NextDouble() < 0.25)
                    loot.Add((511, 1));          // High Yield Dynamite
                loot.Add((Gathering.AdventureEssence, (uint)random.Next(1, 3)));
                LootPinatas.SpawnBag(p, at + new Vector3(0f, 0.6f, 0f), loot);
            }
            foreach (WorldSession s in viewers)
                s.EnqueueMessage(new PlayerUpdateRemovePlayer { Guid = o.Chest.Guid });
            log.Info($"{session.CharacterName} opened the Lost Chest of {o.Name}.");
        }

        /// <summary>
        /// Using a Lost Crystal: only once the outpost's Elite is dead, never in combat; a 5 s channel that a hit cancels.
        /// </summary>
        public static void UseCrystal(WorldSession session, ulong guid)
        {
            if (ByGuid(guid) is not Outpost o || Health.IsDead(session))
                return;
            if (!o.EliteDead)
            {
                Chat.System(session, "The Lost Crystal is dormant. Defeat the outpost's Elite to wake it.");
                return;
            }
            if (Health.HurtWithin(session, InCombatFor))
            {
                Chat.System(session, "You can't use the Lost Crystal while in combat.");
                return;
            }
            lock (sync)
                channels[session] = (o, DateTime.UtcNow + ChannelTime);
            HitEffects.PlayOn(session.PlayerGuid, ChannelEffect, Viewers(o));
            Chat.System(session, "You channel the Lost Crystal...");
        }

        /// <summary>A hit cancels a crystal channel (wiki: "If you are attacked, the channeling will cancel").</summary>
        public static void Interrupt(WorldSession session)
        {
            bool had;
            lock (sync)
                had = channels.Remove(session);
            if (had)
                Chat.System(session, "Your channel was interrupted.");
        }

        private static void CompleteChannels(DateTime now)
        {
            List<(WorldSession Session, Outpost Outpost)> done;
            lock (sync)
            {
                done = channels.Where(c => now >= c.Value.Until).Select(c => (c.Key, c.Value.Outpost)).ToList();
                foreach ((WorldSession s, _) in done)
                    channels.Remove(s);
            }
            foreach ((WorldSession session, Outpost o) in done)
            {
                // Loot (wiki "Lost Crystals": "a good chance to drop 1 or 2 recipes, as well as Cozy Campfires,
                // essences, and Lumens"), the discovery fanfare, then the offer of another cavern.
                var loot = new List<(uint, uint)>();
                int recipes = random.NextDouble() < 0.6 ? 1 + (random.NextDouble() < 0.3 ? 1 : 0) : 0;
                for (int i = 0; i < recipes; i++)
                    if (RecipeBook.RandomUnknownRecipeItem(session, random) is uint recipe and not 0)
                        loot.Add((recipe, 1));
                if (random.NextDouble() < 0.5)
                    loot.Add((3894, 1));            // Cozy Campfire
                uint[] essences = { 1587, 1588, 1589, 1590, 1591, 1592, 1593 };
                loot.Add((essences[random.Next(essences.Length)], (uint)random.Next(1, 4)));
                loot.Add((Gathering.AdventureEssence, (uint)random.Next(2, 5)));
                loot.Add((1579, (uint)random.Next(2, 6)));   // Lumens
                Vector3 at = new(o.Crystal.Position.X, o.Crystal.Position.Y, o.Crystal.Position.Z);
                LootPinatas.SpawnBag(session, at + new Vector3(0f, 1f, 0f), loot);
                HitEffects.PlayOn(session.PlayerGuid, DiscoveryFanfare, Viewers(o));
                SendNextCaveOffer(session);
                DailyGoals.CrystalReached(session);
                log.Info($"{session.CharacterName} used the Lost Crystal of {o.Name}.");
            }
        }

        /// <summary>NextUndergroundOffer (0xFC sub 0x0C {u64}): the client's "Eureka!" dialog with the trip button.</summary>
        private static void SendNextCaveOffer(WorldSession session)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write((byte)0x0C);
            w.Write(NextCaveToken);
            session.EnqueueMessage(ZoneMessageOpcode.TeleportBase, output.ToArray());
        }

        /// <summary>NextUndergroundReply (0xFC sub 0x0D {u64}): off to another outpost's cavern.</summary>
        public static void NextCave(WorldSession session)
        {
            Outpost here = outposts.OrderBy(o => Vector3.Distance(o.Floor, session.Position)).FirstOrDefault();
            List<Outpost> others = outposts.Where(o => o != here).ToList();
            if (others.Count == 0)
            {
                Caves.TravelRandom(session);
                return;
            }
            Outpost to = others[random.Next(others.Count)];
            Teleport.To(session, Caves.Floor(Caves.Caverns[to.Cavern].At) + new Vector3(0f, 1f, 0f), snapToGround: false, loadingScreen: true);
            log.Info($"{session.CharacterName} follows the leyline to {to.Name}.");
        }
    }
}
