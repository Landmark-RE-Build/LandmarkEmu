using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Game.Terrain;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Hostile creatures. The client ships their models (Models.txt Char_Biped_Kobold_*, wolves, golems) but no
    /// creature definitions (those were server data), so the server defines them, spawns them as lightweight NPCs
    /// (PlayerUpdate 0x59), moves them (Position 0x7F with a full type-0 movement body in the remote frame), and runs
    /// their AI and combat:
    /// - as at launch (wiki "Monsters (PvE)": "only Fiery Mushrooms appear on the surface; all other monsters are
    ///   cave-only"), the surface gets only Fiery Fungus, seeded per <see cref="RegionSize"/> region of land; the Chaos
    ///   Caverns (the continent's underground caverns, <see cref="Caves.Caverns"/>) each hold a group of the cave
    ///   biome's Chomper and Slaug variants, an Abomination and Fiery Fungus, streamed by 3D distance;
    /// - behaviours: Chompers are stationary (spore cloud in reach), Abominations keep their distance and spit, Fiery
    ///   Fungus inflates when a player comes close and explodes in fire, the rest chase and hit;
    /// - players are sent only creatures within <see cref="ViewRange"/>; movement goes only to players in range, at
    ///   most <see cref="AiHz"/> times a second and only while the creature moves;
    /// - a creature attacks players who come within <see cref="AggroRange"/>, chases, hits in melee, and returns home
    ///   (healed) past <see cref="LeashRange"/>;
    /// - weapon swings (casts not tied to a tool) hit the creature at the aim point; hit points show through
    ///   PlayerUpdate 0x03 UpdateHitpoints; at 0 it despawns, drops loot to the killer and respawns later.
    /// </summary>
    public static class Creatures
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private const float RegionSize = 128f;
        private const float ViewRange = 120f, DropRange = 160f;
        private const float AggroRange = 12f, LeashRange = 40f, AttackRange = 2.5f;
        /// <summary>Passive animals (no damage, e.g. deer) bolt when a player comes this close or hits them, and run
        /// until <see cref="SafeRange"/> away.</summary>
        private const float SkittishRange = 8f, SafeRange = 22f;
        /// <summary>Most height difference a melee hit reaches across.</summary>
        private const float MaxReachHeight = 3f;
        private const float RunSpeed = 5f, WanderSpeed = 1.5f;
        private const int AiHz = 5;
        private static readonly TimeSpan AttackInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan RespawnTime = TimeSpan.FromMinutes(3);
        private const ulong GuidBase = 0x5000_0000_0000_0000;

        private enum Behaviour { Melee, Stationary, Ranged, Exploder }

        private sealed record Kind(string Name, uint Model, uint MaxHealth, uint Damage, (uint Item, uint Min, uint Max)[] Loot,
            Behaviour Style = Behaviour.Melee);

        // ---- Chaos Caverns and surface monsters (launch, wiki "Monsters (PvE)"). Models come from the creature props
        // (prop dump), names from the props' own biome variants; health, damage and loot are ours (server data on live).
        private const uint MonsterEssence = 3682, AfflictionEssence = 1588, FireEssence = 1589, Dirt = 44, Stone = 46;

        private static uint ModelOf(uint prop, uint fallback) => Props.ModelOf(prop) is uint m and not 0 ? m : fallback;

        private static Kind fieryFungus, abomination;

        /// <summary>Fiery Fungus ("boom shroom"): inflates and explodes in fire; leaves rock or dirt behind.</summary>
        private static Kind FieryFungus => fieryFungus ??= new("Fiery Fungus", ModelOf(869, ModelOf(1950, 0)), 150, 180,
            new[] { (Dirt, 2u, 6u), (Stone, 1u, 4u), (FireEssence, 0u, 1u) }, Behaviour.Exploder);

        /// <summary>Abomination: slithers close, then spits poison goo from a distance (Affliction).</summary>
        private static Kind Abomination => abomination ??= new("Abomination", ModelOf(1438, 1337), 600, 60,
            new[] { (MonsterEssence, 1u, 2u), (AfflictionEssence, 0u, 1u), (Gathering.AdventureEssence, 0u, 1u) }, Behaviour.Ranged);

        /// <summary>
        /// The cave biome's Chomper and Slaug (VoxelBiomes caverns → the props' biome variants: 41 Ancient Forest →
        /// Forest Chomper / Wood Slaug, 62 Deciduous → Putrid / Rotten, 63 Volcanic → Scorched / Magma, 45 Crystal →
        /// Frozen / Pale, others Mossy / Creeper).
        /// </summary>
        private static (Kind Chomper, Kind Slaug) CavernKinds(uint biome)
        {
            lock (cavernKinds)
            {
                if (cavernKinds.TryGetValue(biome, out var kinds))
                    return kinds;
                (uint chomper, string chomperName, uint slaug, string slaugName) = biome switch
                {
                    41 => (490u, "Forest Chomper", 1711u, "Wood Slaug"),
                    62 => (1523u, "Putrid Chomper", 1716u, "Rotten Slaug"),
                    63 => (1520u, "Scorched Chomper", 1696u, "Magma Slaug"),
                    45 => (1522u, "Frozen Chomper", 1706u, "Pale Slaug"),
                    _ => (1521u, "Mossy Chomper", 1701u, "Creeper Slaug")
                };
                kinds = (new Kind(chomperName, ModelOf(chomper, 461), 900, 90,
                        new[] { (MonsterEssence, 1u, 3u), (AfflictionEssence, 0u, 1u), (Gathering.AdventureEssence, 0u, 1u) }, Behaviour.Stationary),
                    new Kind(slaugName, ModelOf(slaug, 1241), 650, 70,
                        new[] { (MonsterEssence, 1u, 2u), (Gathering.AdventureEssence, 0u, 1u) }));
                cavernKinds[biome] = kinds;
                return kinds;
            }
        }
        private static readonly Dictionary<uint, (Kind, Kind)> cavernKinds = new();

        /// <summary>Reach of the stationary spore cloud, the ranged spit, and the fungus fuse / blast.</summary>
        private const float SporeReach = 7f, SpitRange = 16f, SpitKeepAway = 6f, FuseRange = 3.5f, BlastRadius = 5f;
        private static readonly TimeSpan FuseTime = TimeSpan.FromSeconds(1.5), BlastLinger = TimeSpan.FromSeconds(1.2);
        /// <summary>Composite effects (ActorCompositeEffectDefinitions): the monsters' own attacks.</summary>
        private const uint FungusExplosion = 10918, FungusImpact = 10922, ChomperSporeRelease = 11676, ChomperSporeImpact = 11611,
            AbominationSpitRelease = 11627, AbominationSpitImpact = 11625;

        // Kobolds of the 0.589 model set; loot is raw resources until loot tables are known.
        private static readonly Kind KoboldRunt = new("Kobold Runt", 1573, 300, 25, new[] { (34u, 1u, 3u), (35u, 1u, 3u) });
        private static readonly Kind KoboldMelee = new("Kobold", 1569, 500, 40, new[] { (36u, 1u, 3u), (34u, 1u, 2u) });
        private static readonly Kind KoboldCaster = new("Kobold Shaman", 1571, 400, 50, new[] { (37u, 1u, 2u), (51u, 0u, 1u) });
        // Other bipeds that share the player animation network (their .adr files name PlayerAnimationNetwork, like the
        // kobolds), so they walk and attack with the same animation selections.
        private static readonly Kind Skeleton = new("Skeleton", 1457, 450, 45, new[] { (46u, 1u, 3u), (37u, 0u, 1u) });
        private static readonly Kind Slaug = new("Slaug", 1241, 550, 55, new[] { (35u, 1u, 3u), (51u, 0u, 1u) });
        private static readonly Kind Ogre = new("Ogre", 1456, 1200, 90, new[] { (36u, 2u, 5u), (39u, 1u, 2u) });

        // Animals on the NPC animation network (Char_Wolf_SpiritWolf / Char_Deer_Deer .adr): tried via dev "creature".
        private static readonly Kind SpiritWolf = new("Spirit Wolf", 1463, 400, 35, new[] { (34u, 0u, 1u) });
        private static readonly Kind Deer = new("Deer", 1462, 200, 0, new[] { (34u, 0u, 1u) });

        /// <summary>Creature kinds by dev name (dev command "creature").</summary>
        private static readonly Dictionary<string, Kind> kindsByName = new(StringComparer.OrdinalIgnoreCase)
        {
            ["kobold"] = KoboldMelee, ["runt"] = KoboldRunt, ["shaman"] = KoboldCaster,
            ["skeleton"] = Skeleton, ["slaug"] = Slaug, ["ogre"] = Ogre, ["wolf"] = SpiritWolf, ["deer"] = Deer
        };

        /// <summary>A prop's internal name (spawner props name their creature: "Spawner: Ruins: Barren Melee …").</summary>
        public static string PropName(uint propId) => Props.InternalName(propId) ?? "";

        /// <summary>Dev "creature" kinds that are built on first use (their models come from the prop dump).</summary>
        private static Kind DevKind(string name) => name?.ToLowerInvariant() switch
        {
            "fungus" => FieryFungus,
            "abomination" => Abomination,
            "chomper" => CavernKinds(41).Chomper,
            "woodslaug" => CavernKinds(41).Slaug,
            _ => null
        };

        private enum State { Idle, Chase, Return, Dead, Flee }

        private sealed class Creature
        {
            public ulong Guid;
            /// <summary>The landscape the creature lives on.</summary>
            public uint Zone;
            public uint TransientId;
            public Kind Kind;
            public Vector3 Home;
            public Vector3 Position;
            public float Yaw;
            public uint Health;
            public State State;
            public WorldSession Target;
            public DateTime NextAttack;
            public DateTime RespawnAt;
            public Vector3? WanderTo;
            public DateTime NextWander;
            public bool Moved;
            /// <summary>Metres per second of the last step; sent so the client plays walk/run instead of sliding.</summary>
            public Vector3 Velocity;
            /// <summary>Moved on the previous step: a creature that stops gets one zero-velocity update (idle pose).</summary>
            public bool WasMoving;
            /// <summary>Spawned by a story-tool creature spawner: removed when it dies (StoryTools respawns it).</summary>
            public bool Story;
            /// <summary>A story spawner's boss ("... BOSS ..." spawners): shown with the client's boss display.</summary>
            public bool Boss;
            /// <summary>Story faction: 0 enemy (attacks), 1 neutral, 2 ally (neither attacks players).</summary>
            public int Faction;
            /// <summary>Walking to a Travel Point (story); <see cref="WalkTag"/> is the point, told on arrival.</summary>
            public Vector3? WalkTo;
            public ulong WalkTag;
            /// <summary>Looking at a point (Focus Node): it stands still facing it.</summary>
            public Vector3? FocusAt;
            /// <summary>The Chaos Cavern it lives in (index into <see cref="Caves.Caverns"/>), −1 on the surface.</summary>
            public int Cavern = -1;
            /// <summary>A Fiery Fungus's fuse: it explodes at this time.</summary>
            public DateTime FuseAt;
            /// <summary>An exploded fungus stays visible until this time (its blast plays on it), then goes.</summary>
            public DateTime RemoveAt;
        }

        /// <summary>The Chaos Caverns' groups by cavern index, made on first approach.</summary>
        private static readonly Dictionary<int, Creature[]> cavernGroups = new();
        /// <summary>The caverns whose group each player has on screen.</summary>
        private static readonly Dictionary<WorldSession, HashSet<int>> cavernsSent = new();
        private const ulong CavernGuidBase = 0x5100_0000_0000_0000;
        /// <summary>A cavern's group is shown within this distance (3D) and dropped beyond the second.</summary>
        private const float CavernViewRange = 150f, CavernDropRange = 200f;

        /// <summary>Distance a creature reacts over: 3D in the caverns (players above must not wake them), else flat.</summary>
        private static float Dist(Creature c, Vector3 p) => c.Cavern >= 0 ? Vector3.Distance(c.Position, p) : Flat(c.Position, p);

        private static readonly Dictionary<int, Vector3> cavernFloors = new();

        /// <summary>The floor under a cavern's middle (cached): where its group stands, and what streaming measures to.</summary>
        private static Vector3 CavernFloor(int index)
        {
            if (!cavernFloors.TryGetValue(index, out Vector3 floor))
                cavernFloors[index] = floor = Caves.Floor(Caves.Caverns[index].At);
            return floor;
        }

        /// <summary>A cavern's monsters: a Chomper (two at times), two Slaugs, an Abomination and two Fiery Fungus.</summary>
        private static Creature[] CavernGroup(int index)
        {
            if (cavernGroups.TryGetValue(index, out Creature[] group))
                return group;
            (Vector3 inAir, uint biome) = Caves.Caverns[index];
            // The cavern's middle is open air from about 76 m below to 75 m above the piece's origin: its floor can be
            // far below the arrival point, so the group stands around the floor under it.
            Vector3 centre = CavernFloor(index);
            var rng = new Random(index * 7919 + 17);
            (Kind chomper, Kind slaug) = CavernKinds(biome);
            var plan = new List<Kind> { chomper, slaug, slaug, Abomination, FieryFungus, FieryFungus };
            if (rng.NextDouble() < 0.5)
                plan.Add(chomper);
            var list = new List<Creature>();
            for (int i = 0; i < plan.Count; i++)
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    double a = rng.NextDouble() * Math.PI * 2, r = 6 + rng.NextDouble() * 22;
                    if (Caves.FloorNear(centre + new Vector3((float)(Math.Cos(a) * r), 2f, (float)(Math.Sin(a) * r)), 10f) is not Vector3 at)
                        continue;
                    list.Add(new Creature
                    {
                        Guid = CavernGuidBase | ((ulong)index << 8) | (uint)i,
                        Zone = Home,
                        TransientId = PlayerManager.AllocateTransientId(),
                        Kind = plan[i],
                        Cavern = index,
                        Home = at,
                        Position = at,
                        Yaw = (float)(rng.NextDouble() * Math.PI * 2),
                        Health = plan[i].MaxHealth,
                        NextWander = DateTime.UtcNow.AddSeconds(rng.NextDouble() * 10)
                    });
                    break;
                }
            // An outpost's garrison: its faction's ruin spawners, the BOSS being the Elite (boss display).
            int g = 0;
            foreach ((uint spawner, string name, bool boss, Vector3 near) in Outposts.GarrisonFor(index))
            {
                Kind kind = SpawnerKind(name, spawner);
                if (kind == null)
                    continue;
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    double a = rng.NextDouble() * Math.PI * 2, r = boss ? 4 + rng.NextDouble() * 4 : 8 + rng.NextDouble() * 18;
                    if (Caves.FloorNear(near + new Vector3((float)(Math.Cos(a) * r), 2f, (float)(Math.Sin(a) * r)), 10f) is not Vector3 at)
                        continue;
                    list.Add(new Creature
                    {
                        Guid = CavernGuidBase | ((ulong)index << 8) | (uint)(100 + g++),
                        Zone = Home,
                        TransientId = PlayerManager.AllocateTransientId(),
                        Kind = boss ? kind with { Name = "Elite " + kind.Name, MaxHealth = kind.MaxHealth * 2 } : kind,
                        Cavern = index,
                        Boss = boss,
                        Home = at,
                        Position = at,
                        Yaw = (float)(rng.NextDouble() * Math.PI * 2),
                        Health = boss ? kind.MaxHealth * 2 : kind.MaxHealth,
                        NextWander = DateTime.UtcNow.AddSeconds(rng.NextDouble() * 10)
                    });
                    break;
                }
            }
            group = list.ToArray();
            cavernGroups[index] = group;
            return group;
        }

        private static readonly object sync = new();
        private static readonly Dictionary<long, Creature[]> regions = new();
        private static readonly Dictionary<WorldSession, HashSet<long>> sent = new();
        private static readonly Random random = new();
        private static DateTime nextTick = DateTime.MinValue;
        private static DateTime nextStream = DateTime.MinValue;

        /// <summary>
        /// Drops what was sent to a player who left, and any creature chasing them gives up.
        /// </summary>
        public static void Forget(WorldSession session)
        {
            ForgetBosses(session);
            lock (sync)
            {
                sent.Remove(session);
                cavernsSent.Remove(session);
                foreach (Creature c in regions.Values.SelectMany(r => r).Concat(cavernGroups.Values.SelectMany(g => g)))
                    if (c.Target == session)
                        c.Target = null;
            }
        }

        /// <summary>Server tick: streaming once a second, AI at <see cref="AiHz"/>.</summary>
        public static void Update()
        {
            if (!TerrainHeights.Loaded)
                return;
            DateTime now = DateTime.UtcNow;
            List<WorldSession> players = PlayerManager.Snapshot();
            if (now >= nextStream)
            {
                nextStream = now.AddSeconds(1);
                foreach (WorldSession session in players)
                    Stream(session);
            }
            if (now < nextTick)
                return;
            float dt = 1f / AiHz;
            nextTick = now.AddSeconds(dt);
            List<Creature> active;
            lock (sync)
                active = sent.Values.SelectMany(h => h).Distinct().Where(regions.ContainsKey)
                    .SelectMany(k => regions[k])
                    .Concat(cavernsSent.Values.SelectMany(h => h).Distinct().Where(cavernGroups.ContainsKey).SelectMany(i => cavernGroups[i]))
                    .Distinct().ToList();
            foreach (Creature c in active)
                Think(c, players, now, dt);
        }

        /// <summary>
        /// One AI step for a creature. Idle: wander around its home, or start chasing the nearest living player within
        /// <see cref="AggroRange"/>. Chase: run to the target and hit it every <see cref="AttackInterval"/> once in reach; give
        /// up when the target dies, leaves or the creature is <see cref="LeashRange"/> from home. Return: run home and heal.
        /// Dead: respawn after <see cref="RespawnTime"/>. Movement is only sent when the creature actually moved.
        /// </summary>
        private static void Think(Creature c, List<WorldSession> players, DateTime now, float dt)
        {
            if (c.State == State.Dead)
            {
                if (c.RemoveAt != default && now >= c.RemoveAt)
                {
                    c.RemoveAt = default;
                    foreach (WorldSession s in ViewersOf(c))
                        RemoveFor(s, c.Guid);
                }
                if (now >= c.RespawnAt)
                    Respawn(c);
                return;
            }
            if (c.Kind.Style == Behaviour.Exploder)
            {
                ThinkFungus(c, players, now);
                return;
            }
            c.Moved = false;
            Vector3 before = c.Position;
            switch (c.State)
            {
                case State.Idle:
                {
                    if (c.WalkTo is Vector3 dest)
                    {
                        // A story walk comes first (scripted, it ignores players on the way): the Travel Point becomes its new home, and the point hears it arrived.
                        if (Step(c, dest, WanderSpeed * 1.5f * dt))
                        {
                            c.WalkTo = null;
                            c.Home = c.Position;
                            StoryTools.CreatureArrived(c.Guid, c.WalkTag);
                        }
                        break;
                    }
                    WorldSession near = players.Where(p => p.ZoneId == c.Zone && !Health.IsDead(p) && Dist(c, p.Position) <= AggroRange)
                        .OrderBy(p => Dist(c, p.Position)).FirstOrDefault();
                    if (near != null && c.Faction == 0 && (c.Kind.Damage > 0 || Dist(c, near.Position) <= SkittishRange))
                    {
                        c.State = c.Kind.Damage > 0 ? State.Chase : State.Flee;
                        c.Target = near;
                        if (c.Story && c.State == State.Chase)
                            StoryTools.CreatureCombat(c.Guid, true);
                        break;
                    }
                    if (c.FocusAt is Vector3 focus)
                    {
                        Face(c, focus);
                        break;
                    }
                    if (c.Kind.Style == Behaviour.Stationary)
                        break;
                    if (c.WanderTo is Vector3 w)
                    {
                        if (Step(c, w, WanderSpeed * dt))
                            c.WanderTo = null;
                    }
                    else if (now >= c.NextWander)
                    {
                        c.NextWander = now.AddSeconds(5 + random.NextDouble() * 10);
                        float a = (float)(random.NextDouble() * Math.PI * 2), r = 3f + (float)random.NextDouble() * 6f;
                        c.WanderTo = c.Home + new Vector3(MathF.Cos(a) * r, 0f, MathF.Sin(a) * r);
                    }
                    break;
                }
                case State.Chase:
                {
                    WorldSession t = c.Target;
                    if (t == null || Health.IsDead(t) || !players.Contains(t) || t.ZoneId != c.Zone || Flat(c.Position, c.Home) > LeashRange
                        || c.Faction != 0)
                    {
                        c.State = State.Return;
                        c.Target = null;
                        if (c.Story)
                            StoryTools.CreatureCombat(c.Guid, false);
                        break;
                    }
                    float d = Dist(c, t.Position);
                    if (c.Kind.Style == Behaviour.Stationary)
                    {
                        // A Chomper doesn't move: it turns and spews its spore cloud on anyone in reach, and calms down
                        // once they're gone.
                        Face(c, t.Position);
                        if (d > SporeReach * 1.6f)
                        {
                            c.State = State.Idle;
                            c.Target = null;
                        }
                        else if (d <= SporeReach && now >= c.NextAttack && Combat.Statuses.CanAct(c.Guid))
                        {
                            c.NextAttack = now + AttackInterval;
                            PlayAnim(c, AnimAttack, t.Position);
                            HitEffects.PlayOn(c.Guid, ChomperSporeRelease, ViewersOf(c));
                            HitEffects.PlayOn(t.PlayerGuid, ChomperSporeImpact, ViewersOf(c));
                            HurtPlayer(c, t, c.Kind.Damage);
                        }
                        break;
                    }
                    if (c.Kind.Style == Behaviour.Ranged)
                    {
                        // An Abomination closes in to spitting range, backs off from anyone too close, and spits.
                        if (d > SpitRange)
                            Step(c, t.Position, RunSpeed * 0.8f * dt, stopShort: SpitRange * 0.8f);
                        else if (d < SpitKeepAway)
                        {
                            Vector3 away = new(c.Position.X - t.Position.X, 0f, c.Position.Z - t.Position.Z);
                            if (away.LengthSquared() > 0.01f)
                                Step(c, c.Position + Vector3.Normalize(away) * 4f, RunSpeed * 0.6f * dt);
                        }
                        else
                        {
                            Face(c, t.Position);
                            if (now >= c.NextAttack && Combat.Statuses.CanAct(c.Guid))
                            {
                                c.NextAttack = now + AttackInterval;
                                PlayAnim(c, AnimAttack, t.Position);
                                HitEffects.PlayOn(c.Guid, AbominationSpitRelease, ViewersOf(c));
                                HitEffects.PlayOn(t.PlayerGuid, AbominationSpitImpact, ViewersOf(c));
                                HurtPlayer(c, t, c.Kind.Damage);
                            }
                        }
                        break;
                    }
                    if (d > AttackRange)
                        Step(c, t.Position, RunSpeed * dt, stopShort: AttackRange * 0.8f);
                    else if (MathF.Abs(t.Position.Y - c.Position.Y) > MaxReachHeight)
                    {
                        // Right above or below (the creature walks the generated ground; the player may be in a cave,
                        // on rocks or on a build): it can't reach, so it waits there, facing the player.
                        Face(c, t.Position);
                    }
                    else
                    {
                        Face(c, t.Position);
                        if (now >= c.NextAttack && Combat.Statuses.CanAct(c.Guid))
                        {
                            c.NextAttack = now + AttackInterval;
                            PlayAnim(c, AnimAttack, t.Position);
                            HurtPlayer(c, t, c.Kind.Damage);
                        }
                    }
                    break;
                }
                case State.Flee:
                {
                    // Run straight away from the player; calm down once safe (or too far from home: head back).
                    WorldSession t = c.Target;
                    if (t == null || !players.Contains(t) || t.ZoneId != c.Zone || Flat(t.Position, c.Position) >= SafeRange)
                    {
                        c.State = State.Idle;
                        c.Target = null;
                        c.WanderTo = null;
                        c.NextWander = now.AddSeconds(3);
                        break;
                    }
                    if (Flat(c.Position, c.Home) > LeashRange)
                    {
                        c.State = State.Return;
                        c.Target = null;
                        break;
                    }
                    Vector3 away = new(c.Position.X - t.Position.X, 0f, c.Position.Z - t.Position.Z);
                    if (away.LengthSquared() < 0.01f)
                        away = new Vector3(MathF.Sin(c.Yaw), 0f, MathF.Cos(c.Yaw));
                    Step(c, c.Position + Vector3.Normalize(away) * 5f, RunSpeed * 1.3f * dt);
                    break;
                }
                case State.Return:
                    if (Step(c, c.Home, RunSpeed * dt))
                    {
                        c.State = State.Idle;
                        c.Health = c.Kind.MaxHealth;
                        SendHitpoints(c, 0);
                    }
                    break;
            }
            bool moving = c.Position != before;
            c.Velocity = moving && dt > 0 ? new Vector3(c.Position.X - before.X, c.Position.Y - before.Y, c.Position.Z - before.Z) / dt : Vector3.Zero;
            if (c.Moved || (c.WasMoving && !moving))
                SendMovement(c);
            c.WasMoving = moving;
        }

        /// <summary>Moves toward a point on the ground; true once there.</summary>
        private static bool Step(Creature c, Vector3 to, float distance, float stopShort = 0f)
        {
            // Frozen, Electrocuted, Crushed or Condemned creatures stay put; Crippled ones move at half speed.
            if (!Combat.Statuses.CanMove(c.Guid))
                return false;
            distance *= Combat.Statuses.Speed(c.Guid);
            Vector3 flat = new(to.X - c.Position.X, 0f, to.Z - c.Position.Z);
            float length = flat.Length();
            if (length <= stopShort + 0.05f)
                return true;
            float move = MathF.Min(distance, length - stopShort);
            Vector3 next = c.Position + flat / length * move;
            if (c.Cavern >= 0)
            {
                // Cavern creatures walk the cave floor (the client's own cave shapes); a wall or drop stops them.
                if (Caves.FloorNear(next with { Y = c.Position.Y }, 3f) is not Vector3 floor)
                    return true;
                next.Y = floor.Y;
            }
            else
                next.Y = TerrainHeights.HeightAt(next.X, next.Z, c.Zone) ?? c.Position.Y;
            c.Position = next;
            c.Yaw = MathF.Atan2(flat.X, flat.Z);
            c.Moved = true;
            return length - stopShort <= distance;
        }

        /// <summary>
        /// Turns a creature towards a point (only when the change is noticeable, to save movement packets).
        /// </summary>
        private static void Face(Creature c, Vector3 at)
        {
            float yaw = MathF.Atan2(at.X - c.Position.X, at.Z - c.Position.Z);
            if (MathF.Abs(yaw - c.Yaw) > 0.2f)
            {
                c.Yaw = yaw;
                c.Moved = true;
            }
        }

        // ---- Weapon combat (Combat.WeaponCombat) ---------------------------------------------------------------------

        /// <summary>
        /// A creature's attack lands on a player: its damage (less while it Rots or is Weakened) goes through the
        /// player's defences (<see cref="Combat.WeaponCombat.Incoming"/>: blocks, Harden, shields, armour), then hurts.
        /// </summary>
        private static void HurtPlayer(Creature c, WorldSession t, uint damage)
        {
            uint amount = (uint)Math.Round(damage * Combat.Statuses.DealtFactor(c.Guid));
            amount = Combat.WeaponCombat.Incoming(t, amount, c.Guid, c.Position);
            if (amount == 0)
                return;
            Health.Damage(t, amount, sourceGuid: c.Guid);
            SendCombatEvent(t, c.Guid, t.PlayerGuid, amount);
        }

        private static Dictionary<ulong, Creature> byGuid = new();

        /// <summary>Any creature by guid (regions, caverns, story creatures); the index is rebuilt on a miss.</summary>
        private static Creature Find(ulong guid)
        {
            lock (sync)
            {
                if (byGuid.TryGetValue(guid, out Creature c) && Alive(c))
                    return c;
                // A miss or a dead entry: dev / story spawns can reuse a guid, so the living creature wins.
                byGuid = regions.Values.SelectMany(r => r).Concat(cavernGroups.Values.SelectMany(g => g))
                    .GroupBy(x => x.Guid).ToDictionary(g => g.Key, g => g.OrderByDescending(Alive).First());
                return byGuid.GetValueOrDefault(guid);
            }
        }

        private static bool Alive(Creature c) => c.State != State.Dead;

        /// <summary>Living creatures within <paramref name="radius"/> m of a point (height allowance radius + 3 m).</summary>
        public static List<ulong> InRadius(uint zone, Vector3 at, float radius)
        {
            lock (sync)
                return Nearby(zone, at, radius).Where(c => Alive(c) && MathF.Abs(c.Position.Y - at.Y) <= radius + 3f)
                    .OrderBy(c => Vector3.Distance(c.Position, at)).Select(c => c.Guid).Distinct().ToList();
        }

        /// <summary>Living creatures in front: within <paramref name="range"/> m and ±<paramref name="halfAngle"/>° of the flat direction.</summary>
        public static List<ulong> InCone(uint zone, Vector3 from, Vector3 forward, float range, float halfAngle)
        {
            float cos = MathF.Cos(halfAngle * MathF.PI / 180f);
            lock (sync)
                return Nearby(zone, from, range + 1f).Where(c => Alive(c) && MathF.Abs(c.Position.Y - from.Y) <= 4f + range * 0.3f)
                    .Where(c =>
                    {
                        Vector3 to = new(c.Position.X - from.X, 0f, c.Position.Z - from.Z);
                        float len = to.Length();
                        return len < 1.5f || Vector3.Dot(to / len, forward) >= cos;
                    })
                    .OrderBy(c => Flat(c.Position, from)).Select(c => c.Guid).Distinct().ToList();
        }

        /// <summary>Living creatures whose centre (1 m up) passes within <paramref name="width"/> m of a ray, nearest first.</summary>
        public static List<ulong> AlongRay(uint zone, Vector3 origin, Vector3 direction, float range, float width)
        {
            var hits = new List<(float T, ulong Guid)>();
            lock (sync)
                foreach (Creature c in Nearby(zone, origin, range + 10f).Where(Alive))
                {
                    Vector3 centre = c.Position + new Vector3(0f, 1f, 0f);
                    float t = Vector3.Dot(centre - origin, direction);
                    if (t <= 0 || t > range)
                        continue;
                    if (Vector3.Distance(origin + direction * t, centre) <= width)
                        hits.Add((t, c.Guid));
                }
            return hits.OrderBy(h => h.T).Select(h => h.Guid).Distinct().ToList();
        }

        public static Vector3? PositionOfGuid(ulong guid) => Find(guid) is Creature c && Alive(c) ? c.Position : null;

        /// <summary>A creature's health as a fraction of its maximum (1 when unknown).</summary>
        public static float HealthFraction(ulong guid) => Find(guid) is Creature c && c.Kind.MaxHealth > 0 ? (float)c.Health / c.Kind.MaxHealth : 1f;

        public static IEnumerable<WorldSession> ViewersOfGuid(ulong guid) => Find(guid) is Creature c ? ViewersOf(c) : Enumerable.Empty<WorldSession>();

        /// <summary>
        /// Weapon damage on a creature by guid: the usual hit (Torment applies, aggro, reaction, death, loot), with or
        /// without the weapon's impact effect. Returns the damage dealt and whether it died.
        /// </summary>
        public static (uint Dealt, bool Killed) Damage(WorldSession attacker, ulong guid, uint amount, bool ranged, bool impact = true)
        {
            if (Find(guid) is not Creature c || !Alive(c) || amount == 0)
                return (0, false);
            if (c.Kind.Style == Behaviour.Exploder)
            {
                Hit(attacker, c, amount, ranged);
                return (amount, true);
            }
            uint before = c.Health;
            if (impact)
                Hit(attacker, c, amount, ranged);
            else
                Wound(attacker, c, (uint)(amount * Combat.Statuses.TakenFactor(c.Guid)));
            uint dealt = before > c.Health ? before - c.Health : (c.State == State.Dead ? before : 0);
            return (dealt, c.State == State.Dead);
        }

        /// <summary>Damage over time from a state (Burn, Rot, Condemn with the Hauberk): numbers, no impact or reaction.</summary>
        public static void StatusDamage(WorldSession source, ulong guid, uint amount)
        {
            if (Find(guid) is Creature c && Alive(c) && c.Kind.Style != Behaviour.Exploder)
                Wound(source, c, amount);
        }

        /// <summary>An armour "instantly kill" effect on a creature low on life (not on an Elite).</summary>
        public static void Execute(WorldSession attacker, ulong guid)
        {
            if (Find(guid) is Creature c && Alive(c) && !c.Boss)
                Wound(attacker, c, c.Health);
        }

        /// <summary>Takes health without the impact effect or hit reaction (states, splash, revenge).</summary>
        private static void Wound(WorldSession attacker, Creature c, uint amount)
        {
            bool died;
            lock (sync)
            {
                if (c.State == State.Dead)
                    return;
                c.Health = amount >= c.Health ? 0 : c.Health - amount;
                died = c.Health == 0;
                if (!died && c.State != State.Chase && c.State != State.Flee && attacker != null)
                {
                    c.State = c.Kind.Damage > 0 ? State.Chase : State.Flee;
                    c.Target = attacker;
                }
            }
            SendHitpoints(c, amount);
            if (attacker != null)
                SendCombatEvent(attacker, attacker.PlayerGuid, c.Guid, amount);
            if (died)
                Kill(c, attacker);
        }

        /// <summary>
        /// A weapon swing (any cast that isn't a tool's): the creature nearest the aim point within reach takes the
        /// player's damage. Returns true if something was hit.
        /// </summary>
        public static bool Swing(WorldSession attacker, Vector3 aim, uint damage)
        {
            Creature hit;
            lock (sync)
                hit = Nearby(attacker.ZoneId, attacker.Position, 6f).Where(c => c.State != State.Dead)
                    .Where(c => Flat(c.Position, attacker.Position) <= 4f || Flat(c.Position, aim) <= 2.5f)
                    .OrderBy(c => Flat(c.Position, aim)).FirstOrDefault();
            return hit != null && Hit(attacker, hit, damage);
        }

        /// <summary>
        /// A targeted attack (gear abilities, ranged weapons): the creature with this guid takes the damage if it is alive,
        /// on the attacker's landscape and within <paramref name="range"/> metres. Returns true if it was hit.
        /// </summary>
        public static bool Strike(WorldSession attacker, ulong guid, uint damage, float range)
        {
            Creature hit;
            lock (sync)
                hit = Nearby(attacker.ZoneId, attacker.Position, range).FirstOrDefault(c => c.Guid == guid && c.State != State.Dead);
            return hit != null && Hit(attacker, hit, damage);
        }

        /// <summary>
        /// A ranged shot along the player's aim (camera origin + unit direction from the cast): the first living creature
        /// whose centre (1 m above its feet) passes within 1.5 m of the ray, up to <paramref name="range"/> metres.
        /// </summary>
        public static bool Shoot(WorldSession attacker, Vector3 origin, Vector3 direction, uint damage, float range)
        {
            Creature hit = null;
            float best = float.MaxValue;
            lock (sync)
                foreach (Creature c in Nearby(attacker.ZoneId, attacker.Position, range + 10f).Where(c => c.State != State.Dead))
                {
                    Vector3 centre = c.Position + new Vector3(0f, 1f, 0f);
                    float t = Vector3.Dot(centre - origin, direction);
                    if (t <= 0 || t > range + 10f)
                        continue;
                    float miss = Vector3.Distance(origin + direction * t, centre);
                    if (miss <= 1.5f && t < best)
                    {
                        best = t;
                        hit = c;
                    }
                }
            return hit != null && Hit(attacker, hit, damage, ranged: true);
        }

        /// <summary>Applies one attack to a creature: damage (±15 %), aggro, hit reaction or death and loot.</summary>
        private static bool Hit(WorldSession attacker, Creature hit, uint damage, bool ranged = false)
        {
            if (hit.Kind.Style == Behaviour.Exploder)
            {
                // Shooting or hitting a Fiery Fungus sets it off (wiki: "take a shot at these to set them off safely"):
                // it blows up on the next AI step; the loot goes to whoever triggered it.
                lock (sync)
                    if (hit.FuseAt == default || hit.FuseAt > DateTime.UtcNow)
                    {
                        hit.FuseAt = DateTime.UtcNow;
                        hit.Target = attacker;
                    }
                SendCombatEvent(attacker, attacker.PlayerGuid, hit.Guid, damage);
                Health.HitConfirmed(attacker);
                HitEffects.Play(attacker, hit.Guid, ViewersOf(hit), ranged);
                return true;
            }
            uint dealt = (uint)(damage * (0.85 + random.NextDouble() * 0.3) * Combat.Statuses.TakenFactor(hit.Guid));
            bool died;
            lock (sync)
            {
                hit.Health = dealt >= hit.Health ? 0 : hit.Health - dealt;
                died = hit.Health == 0;
                if (!died && hit.State != State.Chase && hit.State != State.Flee)
                {
                    hit.State = hit.Kind.Damage > 0 ? State.Chase : State.Flee;
                    hit.Target = attacker;
                }
            }
            SendHitpoints(hit, dealt);
            SendCombatEvent(attacker, attacker.PlayerGuid, hit.Guid, dealt);
            Health.HitConfirmed(attacker);
            HitEffects.Play(attacker, hit.Guid, ViewersOf(hit), ranged);
            if (!died)
                PlayAnim(hit, AnimOuch, attacker.Position);
            if (died)
                Kill(hit, attacker);
            return true;
        }

        /// <summary>
        /// A creature reached 0 hit points: it stops, is removed from its viewers, the killer gets its loot, and it waits to
        /// respawn.
        /// </summary>
        private static void Kill(Creature c, WorldSession killer)
        {
            Combat.Statuses.Clear(c.Guid);
            lock (sync)
            {
                c.State = State.Dead;
                c.Target = null;
                c.RespawnAt = DateTime.UtcNow + RespawnTime;
            }
            foreach (WorldSession s in ViewersOf(c))
                RemoveFor(s, c.Guid);
            if (c.Story)
            {
                RemoveFromRegion(c);
                StoryTools.CreatureDied(c.Guid, killer);
            }
            if (c.Boss && c.Cavern >= 0)
                Outposts.EliteDied(c.Cavern, killer);
            if (killer == null)
            {
                log.Info($"A {c.Kind.Name} died.");
                return;
            }
            // The loot drops as a bag where the creature fell; the killer collects it by walking over it.
            LootPinatas.SpawnBag(killer, c.Position + new Vector3(0f, 0.5f, 0f), RollLoot(c.Kind));
            Achievements.Killed(killer);
            DailyGoals.Killed(killer, c.Kind.Name);
            InstancedGames.CreatureKilled(killer);
            log.Info($"{killer.CharacterName} killed a {c.Kind.Name}.");
        }

        /// <summary>
        /// Brings a dead creature back at home with full health and a new transient id, and shows it to its region's viewers.
        /// </summary>
        private static void Respawn(Creature c)
        {
            lock (sync)
            {
                c.State = State.Idle;
                c.Health = c.Kind.MaxHealth;
                c.Position = c.Home;
                c.TransientId = PlayerManager.AllocateTransientId();
            }
            if (c.Boss && c.Cavern >= 0)
                Outposts.EliteBack(c.Cavern);
            foreach (WorldSession s in ViewersOf(c))
                SendSpawn(s, c);
        }

        /// <summary>Depth below the ground (m) at which surface objects are dropped, and above which they return.</summary>
        private const float UndergroundDropDepth = 60f, UndergroundShowDepth = 40f;

        /// <summary>
        /// Interest management: sends a player the creatures of regions within <see cref="ViewRange"/> and removes those of
        /// regions beyond <see cref="DropRange"/> (the gap stops regions flickering at the edge).
        /// </summary>
        private static void Stream(WorldSession session)
        {
            uint zone = session.ZoneId;
            if (DevCommands.NoStreaming || !TerrainHeights.Has(zone))
                return;   // no baked heights for this landscape: creatures can't stand on its ground
            if (session.IsArriving)
                return;
            Vector3 p = session.Position;
            var spawns = new List<Creature>();
            var removals = new List<ulong>();
            lock (sync)
            {
                if (!sent.TryGetValue(session, out var have))
                    sent[session] = have = new HashSet<long>();
                // Surface objects aren't sent to players deep underground (caves), and are dropped once they get
                // there; the two depths differ so a player near the threshold doesn't flicker between them.
                float depth = Terrain.TerrainHeights.DepthBelowGround(p, zone);
                bool hideSurface = depth > UndergroundDropDepth, showSurface = depth < UndergroundShowDepth;
                int r0x = (int)MathF.Floor((p.X - ViewRange) / RegionSize), r1x = (int)MathF.Floor((p.X + ViewRange) / RegionSize);
                int r0z = (int)MathF.Floor((p.Z - ViewRange) / RegionSize), r1z = (int)MathF.Floor((p.Z + ViewRange) / RegionSize);
                for (int rz = r0z; rz <= r1z; rz++)
                    for (int rx = r0x; rx <= r1x; rx++)
                    {
                        long key = Pack(zone, rx, rz);
                        if (!showSurface || have.Contains(key) || RegionDistance(rx, rz, p) > ViewRange)
                            continue;
                        have.Add(key);
                        spawns.AddRange(Region(zone, rx, rz).Where(c => c.State != State.Dead));
                    }
                foreach (long key in have.ToList())
                {
                    (uint keyZone, int rx, int rz) = Unpack(key);
                    if (!hideSurface && RegionDistance(rx, rz, p) <= DropRange)
                        continue;
                    have.Remove(key);
                    removals.AddRange(Region(keyZone, rx, rz).Where(c => c.State != State.Dead).Select(c => c.Guid));
                }
                // The Chaos Caverns (home continent): a cavern's group is shown by 3D distance, so players on the
                // surface above don't get it.
                if (!cavernsSent.TryGetValue(session, out var caverns))
                    cavernsSent[session] = caverns = new HashSet<int>();
                bool home = session.OnHomeContinent;
                for (int i = 0; i < Caves.Caverns.Count; i++)
                {
                    float d = Vector3.Distance(CavernFloor(i), p);
                    if (home && d <= CavernViewRange && caverns.Add(i))
                        spawns.AddRange(CavernGroup(i).Where(c => c.State != State.Dead));
                    else if ((!home || d > CavernDropRange) && caverns.Remove(i))
                        removals.AddRange(CavernGroup(i).Where(c => c.State != State.Dead).Select(c => c.Guid));
                }
            }
            foreach (ulong guid in removals)
                RemoveFor(session, guid);
            foreach (Creature c in spawns)
                SendSpawn(session, c);
        }

        /// <summary>
        /// A Fiery Fungus: still until a living player comes within <see cref="FuseRange"/>; then it inflates (its attack
        /// animation) and after <see cref="FuseTime"/> explodes: every player within <see cref="BlastRadius"/> takes its
        /// damage, the blast plays on it, it drops its rock / dirt for the player who set it off and is gone a moment later.
        /// A hit kills it (Hit) like any creature.
        /// </summary>
        private static void ThinkFungus(Creature c, List<WorldSession> players, DateTime now)
        {
            if (c.FuseAt == default)
            {
                WorldSession near = players.FirstOrDefault(p => p.ZoneId == c.Zone && !Health.IsDead(p) && Dist(c, p.Position) <= FuseRange);
                if (near == null)
                    return;
                c.FuseAt = now + FuseTime;
                c.Target = near;
                PlayAnim(c, AnimAttack, near.Position);
                return;
            }
            if (now < c.FuseAt)
                return;
            c.FuseAt = default;
            List<WorldSession> viewers = ViewersOf(c);
            HitEffects.PlayOn(c.Guid, FungusExplosion, viewers);
            foreach (WorldSession p in players.Where(p => p.ZoneId == c.Zone && !Health.IsDead(p) && Dist(c, p.Position) <= BlastRadius))
            {
                HitEffects.PlayOn(p.PlayerGuid, FungusImpact, viewers);
                HurtPlayer(c, p, c.Kind.Damage);
            }
            WorldSession trigger = c.Target;
            lock (sync)
            {
                c.State = State.Dead;
                c.Target = null;
                c.RespawnAt = now + RespawnTime;
                c.RemoveAt = now + BlastLinger;
            }
            if (trigger != null && players.Contains(trigger))
                LootPinatas.SpawnBag(trigger, c.Position + new Vector3(0f, 0.5f, 0f), RollLoot(c.Kind));
            log.Debug($"A {c.Kind.Name} exploded at {c.Position}.");
        }

        private static List<(uint, uint)> RollLoot(Kind kind)
        {
            var loot = new List<(uint, uint)>();
            foreach ((uint item, uint min, uint max) in kind.Loot)
            {
                uint count = (uint)random.Next((int)min, (int)max + 1);
                if (count > 0)
                    loot.Add((item, count));
            }
            return loot;
        }

        /// <summary>A region's surface creatures, seeded from its coordinates (land only, above the sea).</summary>
        private static Creature[] Region(uint zone, int rx, int rz)
        {
            long key = Pack(zone, rx, rz);
            if (regions.TryGetValue(key, out Creature[] cached))
                return cached;
            // Seeded per landscape (the home continent keeps its original seed).
            var rng = new Random(HashCode.Combine(rx * 15485863, rz * 32452843, 0x6b6f ^ (int)(zone == Home ? 0 : zone)));
            var list = new List<Creature>();
            float cx = (rx + 0.5f) * RegionSize, cz = (rz + 0.5f) * RegionSize;
            int biome = TerrainMaterials.BiomeAt(new Vector3(cx, 0f, cz), zone);
            // Launch: only Fiery Fungus on the surface (wiki "Monsters (PvE)"), here and there on land of any biome
            // (VoxelBiomes 47-52); never in the ocean (54).
            if (biome >= 47 && biome <= 52 && rng.NextDouble() < 0.2)
            {
                int count = 1 + rng.Next(2);
                for (int i = 0; i < count; i++)
                {
                    float x = cx + (float)(rng.NextDouble() - 0.5) * 60f, z = cz + (float)(rng.NextDouble() - 0.5) * 60f;
                    if (TerrainHeights.HeightAt(x, z, zone) is not float y || y < 1002f || TerrainHeights.Slope(x, z, 2f, zone) > 0.7f)
                        continue;
                    var global = TerrainEdits.ToGlobal(new Vector3(x, y, z));
                    if (!Claims.MayEdit(0, global.X, global.Y, global.Z, zone))
                        continue;
                    Kind kind = FieryFungus;
                    var home = new Vector3(x, y, z);
                    list.Add(new Creature
                    {
                        Guid = GuidBase | ((ulong)key << 8) | (uint)i,
                        Zone = zone,
                        TransientId = PlayerManager.AllocateTransientId(),
                        Kind = kind,
                        Home = home,
                        Position = home,
                        Yaw = (float)(rng.NextDouble() * Math.PI * 2),
                        Health = kind.MaxHealth,
                        NextWander = DateTime.UtcNow.AddSeconds(rng.NextDouble() * 10)
                    });
                }
            }
            Creature[] result = list.ToArray();
            regions[key] = result;
            return result;
        }

        /// <summary>Spawn control mode for creatures (see <see cref="PlayerUpdateAddLightweightNpc.ControlMode"/>): 2 =
        /// follow the server's positions like a remote player. Dev "npcmode" changes it for later spawns.</summary>
        public static sbyte ControlMode { get; set; } = 2;

        /// <summary>
        /// AddLightweightNpc for a creature: guid, transient id, name, model, position and facing.
        /// </summary>
        private static PlayerUpdateAddLightweightNpc BuildSpawn(Creature c) => new()
        {
            ControlMode = ControlMode,
            Guid = c.Guid,
            TransientId = c.TransientId,
            Name = c.Kind.Name,
            ModelId = c.Kind.Model,
            Position = new Vector4(c.Position, 1f),
            Rotation = new Vector4(0f, MathF.Sin(c.Yaw / 2), 0f, MathF.Cos(c.Yaw / 2))
        };

        /// <summary>
        /// Shows a creature to a player. (Name/health indicator: not shown yet. It needs requirement 21 "NPC Combatant";
        /// sending max health as stat 1 (PlayerUpdate 0x53 {u64 guid, list {u32 stat, i8 kind, u32 base, u32 mod}}) and
        /// the hit points with the spawn made no visible difference, so the failing term is elsewhere — see MODLOG.)
        /// </summary>
        private static void SendSpawn(WorldSession session, Creature c)
        {
            session.EnqueueMessage(BuildSpawn(c));
            SendLife(session, c);
            // Story creatures can be clicked ("When a Luminary clicks this creature..."): flagged interactable, the
            // client asks for their interaction info (Props.SendInteractionInfo) and sends InteractRequest on use.
            if (c.Boss)
            {
                lock (bossesShown)
                {
                    if (!bossesShown.TryGetValue(session, out HashSet<ulong> shown))
                        bossesShown[session] = shown = new HashSet<ulong>();
                    shown.Add(c.Guid);
                }
                SendBossDisplay(session, c.Guid, true);
            }
            if (c.Story)
            {
                var flagging = new Network.Message.Model.DynamicInteractionFlagging();
                flagging.Guids.Add(c.Guid);
                session.EnqueueMessage(flagging);
            }
        }

        /// <summary>
        /// Combat EnableBossDisplay (CombatBase 0x24 → bytes 24 00, i16 sub 0x0A; reader FUN_14331c160, handler
        /// FUN_14331e620): {u64 entity, bool on}. On sets the entity's boss flag (+0x1454 bit 0x2) and lists it in the
        /// entity manager's boss list (+0x281e0). The client's entity removal (FUN_142ee4d70) only unlists flag-0x8
        /// entities, so a boss must be switched off before it leaves a player's view and before that player re-zones,
        /// or the list keeps a dangling entity and the client crashes (seen 2026-10-11 at a re-zone).
        /// </summary>
        private static void SendBossDisplay(WorldSession session, ulong guid, bool on)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write((ushort)0x0A);
            w.Write(guid);
            w.Write(on);
            session.EnqueueMessage(ZoneMessageOpcode.CombatBase, output.ToArray());
        }

        /// <summary>The bosses each player has been shown with the boss display (to switch off before removal).</summary>
        private static readonly Dictionary<WorldSession, HashSet<ulong>> bossesShown = new();

        /// <summary>Takes a creature out of one player's view: the boss display off first when it was on.</summary>
        private static void RemoveFor(WorldSession session, ulong guid)
        {
            bool boss;
            lock (bossesShown)
                boss = bossesShown.TryGetValue(session, out HashSet<ulong> shown) && shown.Remove(guid);
            if (boss)
                SendBossDisplay(session, guid, false);
            session.EnqueueMessage(new PlayerUpdateRemovePlayer { Guid = guid });
        }

        /// <summary>
        /// Before a re-zone: switches off every boss display the player has (the re-zone tears the entities down).
        /// Returns whether there was one, so the zoning can wait a moment.
        /// </summary>
        public static bool ClearBossDisplays(WorldSession session)
        {
            HashSet<ulong> shown;
            lock (bossesShown)
                if (!bossesShown.Remove(session, out shown) || shown.Count == 0)
                    return false;
            foreach (ulong guid in shown)
                SendBossDisplay(session, guid, false);
            return true;
        }

        /// <summary>The player left the world.</summary>
        public static void ForgetBosses(WorldSession session)
        {
            lock (bossesShown)
                bossesShown.Remove(session);
        }

        /// <summary>Is this a story-tool creature (spawned by a site's spawner in Play Mode)?</summary>
        public static bool IsStory(ulong guid) => FindStory(guid) != null;

        /// <summary>
        /// The creature's Life resource on one player's client. The client gives an NPC its name and health indicator
        /// (HUDIndicatorSettings type 13 → indicator 22 "Npc") only when requirement 21 "NPC Combatant" holds; checked
        /// live (2026-10-11): NotPlayer, NotProp and TargetNotPropProfile pass, TargetCanBeHarmed fails until the entity
        /// has a Health resource: ClientRequirementTargetMaxHealth (FUN_143656820) reads resource type 1's maximum from
        /// the entity's resource container (+0x4268). A resource's maximum comes from Resources.txt, so the Life row
        /// whose maximum is closest above the creature's (<see cref="LifeRow"/>) is used and the value scaled to it.
        /// </summary>
        private static void SendLife(WorldSession session, Creature c)
        {
            (uint row, uint max) = LifeRow(c.Kind.MaxHealth);
            if (row == 0)
                return;
            uint value = (uint)Math.Round((double)c.Health * max / Math.Max(1u, c.Kind.MaxHealth));
            CharacterResources.SendFor(session, c.Guid, row, CharacterResources.LifeType, value);
        }

        private static List<(uint Row, uint Max)> lifeRows;

        /// <summary>Resources.txt Life rows (type 1, name "Life"): the one with the smallest maximum ≥ <paramref name="max"/>.</summary>
        private static (uint Row, uint Max) LifeRow(uint max)
        {
            lifeRows ??= GameDataFiles.Rows("Resources.txt")
                .Where(r => r.U(1) == CharacterResources.LifeType && r.U(2) == 558897 && r.U(11) > 0 && r.U(11) < 100000)
                .Select(r => (r.U(0), r.U(11))).OrderBy(r => r.Item2).ToList();
            return lifeRows.FirstOrDefault(r => r.Max >= max) is var hit && hit.Row != 0 ? hit
                : lifeRows.LastOrDefault();
        }

        /// <summary>PlayerUpdate 0x03 UpdateHitpoints {u64 guid, u32 current, u32 max, u32 damage}.</summary>
        private static byte[] HitpointsBody(Creature c, uint damage)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(c.Guid);
            writer.Write(c.Health);
            writer.Write(c.Kind.MaxHealth);
            writer.Write(damage);
            return output.ToArray();
        }

        /// <summary>
        /// PlayerUpdate 0x67 CombatResourceEvent (reader FUN_142b8e060; the PlayerUpdate dispatcher switches on sub - 1, so it is its case 0x66): u64
        /// source, u64 target, u64, i32 change (negative = damage), u32 resource type (1 health, 11 armour), u32, u32,
        /// u32, bool, u32. The client shows a floating number when the local player is the source or the target (it
        /// draws them only with its floating combat text on: console togglefloatingcombattext). Sent only to the player
        /// involved.
        /// </summary>
        public static void SendCombatEvent(WorldSession session, ulong source, ulong target, uint damage)
        {
            if (damage == 0)
                return;
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(source);
            writer.Write(target);
            writer.Write(0ul);
            writer.Write(-(int)damage);
            writer.Write(1u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(false);
            writer.Write(0u);
            session.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateCombatResourceEventWire, output.ToArray());
        }

        /// <summary>UpdateHitpoints to every viewer.</summary>
        private static void SendHitpoints(Creature c, uint damage)
        {
            byte[] body = HitpointsBody(c, damage);
            foreach (WorldSession s in ViewersOf(c))
            {
                s.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateUpdateHitpointsWire, body);
                SendLife(s, c);
            }
        }

        /// <summary>AnimationSelection.txt messages: 1 "Attack", 4 "Ouch" (hit reaction).</summary>
        private const uint AnimAttack = 1, AnimOuch = 4;

        /// <summary>
        /// AbilityPlayAnim (Abilities 0x27 sub 0x12, reader FUN_142d650a0, handler FUN_142d7da00): u64 entity, u32
        /// AnimationSelection id, then a point (4 floats) the entity turns to face before playing it.
        /// </summary>
        private static void PlayAnim(Creature c, uint animation, Vector3 towards)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            // Ability header after the opcode (FUN_142d65900): u16 sub again, u8 flag, u32 time.
            writer.Write((byte)0x12);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write(0u);
            writer.Write(c.Guid);
            writer.Write(animation);
            writer.Write(towards.X);
            writer.Write(towards.Y);
            writer.Write(towards.Z);
            writer.Write(1f);
            byte[] body = output.ToArray();
            foreach (WorldSession s in ViewersOf(c))
                s.EnqueueMessage(ZoneMessageOpcode.AbilityPlayAnim, body);
        }

        /// <summary>
        /// Position 0x7F with a full movement body (type 0, server clock, version 1; position in the remote frame,
        /// i.e. local + zone origin / 10, rotation, zero velocity and extras, then the flag tail every client sends).
        /// </summary>
        private static void SendMovement(Creature c)
        {
            byte[] movement = MovementBody(c.Position, c.Yaw, c.Velocity);
            foreach (WorldSession s in ViewersOf(c))
                s.QueueMovement(c.TransientId, movement);
        }

        /// <summary>A full type-0 movement body for a position, heading and velocity (see <see cref="SendMovement"/>).</summary>
        public static byte[] MovementBody(Vector3 position, float yaw, Vector3 velocity)
        {
            var body = new List<byte> { 0x00 };
            body.AddRange(BitConverter.GetBytes((uint)ServerClock.Milliseconds));
            body.Add(0x01);
            void Put(float v, int precision) => WriteSigned(body, (int)MathF.Round(v * MathF.Pow(10, precision)));
            // Local frame, as clients send and receive player movement (adding the zone origin put the target millions
            // of metres away and the client ignored the update: kobolds never moved on screen).
            Put(position.X, 2);
            Put(position.Y, 2);
            Put(position.Z, 2);
            Put(0f, 5);
            Put(MathF.Sin(yaw / 2), 5);
            Put(0f, 5);
            Put(MathF.Cos(yaw / 2), 5);
            // Velocity (m/s): the client picks walk/run/idle from it.
            Put(velocity.X, 5);
            Put(velocity.Y, 5);
            Put(velocity.Z, 5);
            for (int i = 0; i < 8; i++)
                Put(0f, 2);
            Put(0f, 1);
            Put(0f, 5);
            // Movement state (2-bit-length uint), as live clients send it: 0x401 moving, 0x441 standing still (bit 0x40);
            // then the empty extra block.
            uint state = velocity == Vector3.Zero ? 0x441u : 0x401u;
            uint packed = (state << 2) | 1;                 // 2 bytes
            body.Add((byte)(packed & 0xFF));
            body.Add((byte)(packed >> 8));
            body.Add(0x00);
            return body.ToArray();
        }

        /// <summary>
        /// Writes a signed integer in the movement body's packed form: |value| shifted left 3, bit 0 = sign, bits 1-2 = extra
        /// byte count (0-3), little-endian.
        /// </summary>
        private static void WriteSigned(List<byte> output, int value)
        {
            uint raw = (uint)Math.Abs(value) << 3;
            int extra = raw > 0xFFFFFF ? 3 : raw > 0xFFFF ? 2 : raw > 0xFF ? 1 : 0;
            raw |= (uint)(extra << 1) | (value < 0 ? 1u : 0u);
            for (int i = 0; i <= extra; i++)
                output.Add((byte)(raw >> (8 * i)));
        }

        /// <summary>
        /// Players who have this creature's region (and so the creature) on screen.
        /// </summary>
        private static List<WorldSession> ViewersOf(Creature c)
        {
            lock (sync)
            {
                if (c.Cavern >= 0)
                    return cavernsSent.Where(s => s.Value.Contains(c.Cavern)).Select(s => s.Key).ToList();
                long key = Pack(c.Zone, (int)MathF.Floor(c.Home.X / RegionSize), (int)MathF.Floor(c.Home.Z / RegionSize));
                return sent.Where(s => s.Value.Contains(key)).Select(s => s.Key).ToList();
            }
        }

        /// <summary>
        /// Creatures within <paramref name="range"/> metres (horizontal) of a point.
        /// </summary>
        private static IEnumerable<Creature> Nearby(uint zone, Vector3 point, float range)
        {
            int r0x = (int)MathF.Floor((point.X - range - 30f) / RegionSize), r1x = (int)MathF.Floor((point.X + range + 30f) / RegionSize);
            int r0z = (int)MathF.Floor((point.Z - range - 30f) / RegionSize), r1z = (int)MathF.Floor((point.Z + range + 30f) / RegionSize);
            for (int rz = r0z; rz <= r1z; rz++)
                for (int rx = r0x; rx <= r1x; rx++)
                    foreach (Creature c in Region(zone, rx, rz))
                        if (Flat(c.Position, point) <= range)
                            yield return c;
            if (zone == Home || zone == 0)
                foreach (Creature[] group in cavernGroups.Values)
                    foreach (Creature c in group)
                        if (Vector3.Distance(c.Position, point) <= range)
                            yield return c;
        }

        /// <summary>Dev: describes creatures near a player.</summary>
        public static string Describe(WorldSession session)
        {
            lock (sync)
                return string.Join("; ", Nearby(session.ZoneId, session.Position, 40f).Select(c =>
                    $"{c.Kind.Name} {c.Guid:X} {c.State} hp {c.Health} at {c.Position} d {Flat(c.Position, session.Position):F1}"))
                    + $" | regions sent {(sent.TryGetValue(session, out var h) ? h.Count : 0)}"
                    + $" | caverns sent {(cavernsSent.TryGetValue(session, out var cs) ? cs.Count : 0)}, nearest cavern "
                    + $"{(Caves.Caverns.Count == 0 ? -1 : Enumerable.Range(0, Caves.Caverns.Count).Min(i => Vector3.Distance(CavernFloor(i), session.Position))):F0} m"
                    + $" | at {session.Position} arriving {session.IsArriving} home {session.OnHomeContinent}";
        }

        /// <summary>Dev/testing: kills every creature within a radius of a player (no loot). Returns how many.</summary>
        /// <summary>Dev/testing: the nearest living creature within a radius dies as if the player killed it (loot bag).</summary>
        public static bool DevSlayNearest(WorldSession session, float radius)
        {
            Creature c;
            lock (sync)
                c = Nearby(session.ZoneId, session.Position, radius).Where(x => x.State != State.Dead)
                    .OrderBy(x => Vector3.Distance(x.Position, session.Position)).FirstOrDefault();
            if (c == null)
                return false;
            Kill(c, session);
            return true;
        }

        /// <summary>Dev "hitnear": the nearest creature takes a hit from this player (aggro, hit reaction, health).</summary>
        public static bool DevHitNearest(WorldSession session, uint damage, float radius)
        {
            Creature c;
            lock (sync)
                c = Nearby(session.ZoneId, session.Position, radius).Where(x => x.State != State.Dead)
                    .OrderBy(x => Vector3.Distance(x.Position, session.Position)).FirstOrDefault();
            if (c == null)
                return false;
            Hit(session, c, damage);
            return true;
        }

        public static int DevKillNear(WorldSession session, float radius)
        {
            List<Creature> near;
            lock (sync)
                near = Nearby(session.ZoneId, session.Position, radius).Where(c => c.State != State.Dead || (c.Guid & 0xFF) >= 200).ToList();
            foreach (Creature c in near)
            {
                lock (sync)
                {
                    c.State = State.Dead;
                    c.RespawnAt = DateTime.UtcNow + RespawnTime;
                    c.Target = null;
                    // Dev-spawned ones (camp index 200+, see SpawnNear) are removed for good, not respawned.
                    if ((c.Guid & 0xFF) >= 200)
                    {
                        long key = Pack(c.Zone, (int)MathF.Floor(c.Home.X / RegionSize), (int)MathF.Floor(c.Home.Z / RegionSize));
                        if (regions.TryGetValue(key, out Creature[] members))
                            regions[key] = members.Where(m => m != c).ToArray();
                    }
                }
                foreach (WorldSession s in ViewersOf(c))
                    RemoveFor(s, c.Guid);
            }
            return near.Count;
        }

        private static void RemoveFromRegion(Creature c)
        {
            lock (sync)
            {
                long key = Pack(c.Zone, (int)MathF.Floor(c.Home.X / RegionSize), (int)MathF.Floor(c.Home.Z / RegionSize));
                if (regions.TryGetValue(key, out Creature[] members))
                    regions[key] = members.Where(m => m != c).ToArray();
            }
        }

        private static Creature FindStory(ulong guid)
        {
            lock (sync)
                return regions.Values.SelectMany(r => r).FirstOrDefault(c => c.Guid == guid && c.Story);
        }

        /// <summary>
        /// A story-tool creature spawner's creature (StoryTools): the kind comes from the spawner's name (Slaug, Skeleton,
        /// Ogre / BOSS, Wolf, Deer / Stag, Caster, Patroller; kobold otherwise). It is not respawned by the camp clock.
        /// Returns its guid.
        /// </summary>
        /// <summary>
        /// The creature a story spawner makes. Every creature prop ("Spawner: Ruins: Barren Melee (100 Limit)" and the
        /// like — the same spawners live's player-built Outposts used) shows its creature's own model (prop dump: e.g.
        /// 1899 Abomination → model 1337, 1904 Chomper → 461, 2711 Scalewind BOSS → 1575, 2730 Stoneskin BOSS GIANTS →
        /// 1490), so the creature uses that model and the spawner's name; its strength follows the role in the name
        /// (Patroller, Melee, Ranged, Caster, BOSS). Live's creature stats were server data; these are ours.
        /// </summary>
        private static Kind SpawnerKind(string spawnerName, uint propId)
        {
            uint model = Props.ModelOf(propId);
            if (model == 0)
                return null;
            string n = spawnerName ?? "";
            string name = System.Text.RegularExpressions.Regex.Replace(n, @"^(prop: internal: )?Spawner:\s*(Ruins:\s*)?", "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            name = System.Text.RegularExpressions.Regex.Replace(name, @"\s*\(\d+ Limit\)\s*$", "").Trim();
            bool boss = n.Contains("BOSS", StringComparison.Ordinal);
            (uint health, uint damage) = boss ? (2400u, 110u)
                : n.Contains("Caster", StringComparison.OrdinalIgnoreCase) ? (420u, 55u)
                : n.Contains("Ranged", StringComparison.OrdinalIgnoreCase) ? (450u, 50u)
                : n.Contains("Patroller", StringComparison.OrdinalIgnoreCase) ? (380u, 35u)
                : n.Contains("Fungus", StringComparison.OrdinalIgnoreCase) ? (150u, 60u)
                : n.Contains("Chomper", StringComparison.OrdinalIgnoreCase) ? (700u, 70u)
                : n.Contains("Stag", StringComparison.OrdinalIgnoreCase) || n.Contains("Deer", StringComparison.OrdinalIgnoreCase) ? (200u, 0u)
                : (550u, 50u);
            name = boss ? name.Replace("BOSS", "").Replace("GIANTS", "Giant").Trim() : name;
            return new Kind(name.Length > 0 ? name : "Creature", model, health, damage,
                new[] { (34u, 1u, 3u), (Gathering.AdventureEssence, 0u, 1u) });
        }

        public static ulong SpawnStory(uint zone, Vector3 at, string spawnerName, uint propId = 0)
        {
            string n = spawnerName ?? "";
            Kind kind = SpawnerKind(n, propId) ?? (n.Contains("Slaug", StringComparison.OrdinalIgnoreCase) ? Slaug
                : n.Contains("Skeleton", StringComparison.OrdinalIgnoreCase) ? Skeleton
                : n.Contains("BOSS", StringComparison.Ordinal) || n.Contains("Ogre", StringComparison.OrdinalIgnoreCase)
                    || n.Contains("Giant", StringComparison.OrdinalIgnoreCase) ? Ogre
                : n.Contains("Wolf", StringComparison.OrdinalIgnoreCase) || n.Contains("Hound", StringComparison.OrdinalIgnoreCase) ? SpiritWolf
                : n.Contains("Deer", StringComparison.OrdinalIgnoreCase) || n.Contains("Stag", StringComparison.OrdinalIgnoreCase) ? Deer
                : n.Contains("Caster", StringComparison.OrdinalIgnoreCase) ? KoboldCaster
                : n.Contains("Patroller", StringComparison.OrdinalIgnoreCase) ? KoboldRunt
                : KoboldMelee);
            at.Y = TerrainHeights.HeightAt(at.X, at.Z, zone) is float ground && ground > at.Y - 2f && ground < at.Y + 2f ? ground : at.Y;
            int rx = (int)MathF.Floor(at.X / RegionSize), rz = (int)MathF.Floor(at.Z / RegionSize);
            long key = Pack(zone, rx, rz);
            Creature c;
            lock (sync)
            {
                Creature[] existing = Region(zone, rx, rz);
                byte index = (byte)Enumerable.Range(200, 56).First(i => existing.All(m => (m.Guid & 0xFF) != (ulong)i));
                c = new Creature
                {
                    Guid = GuidBase | ((ulong)key << 8) | index,
                    Zone = zone,
                    TransientId = PlayerManager.AllocateTransientId(),
                    Kind = kind,
                    Home = at,
                    Position = at,
                    Health = kind.MaxHealth,
                    NextWander = DateTime.UtcNow.AddSeconds(5),
                    Story = true,
                    Boss = n.Contains("BOSS", StringComparison.Ordinal)
                };
                regions[key] = existing.Append(c).ToArray();
            }
            foreach (WorldSession s in ViewersOf(c))
                SendSpawn(s, c);
            return c.Guid;
        }

        /// <summary>Removes a story creature (despawn action, Play Mode off).</summary>
        public static void DespawnStory(ulong guid)
        {
            Creature c = FindStory(guid);
            if (c == null)
                return;
            foreach (WorldSession s in ViewersOf(c))
                RemoveFor(s, c.Guid);
            lock (sync)
                c.State = State.Dead;
            RemoveFromRegion(c);
        }

        /// <summary>A story creature takes a share of its maximum health as damage (Damage Node); it may die of it.</summary>
        public static void DamageStory(ulong guid, float percent)
        {
            Creature c = FindStory(guid);
            if (c == null || c.State == State.Dead)
                return;
            uint damage = (uint)Math.Ceiling(c.Kind.MaxHealth * percent / 100f);
            bool dies;
            lock (sync)
            {
                c.Health = damage >= c.Health ? 0 : c.Health - damage;
                dies = c.Health == 0;
            }
            if (dies)
                Kill(c, null);
        }

        /// <summary>A story creature plays an AnimationSelection (Story Node emote or stance), facing where it looks.</summary>
        public static void PlayStoryAnimation(ulong guid, uint animation)
        {
            Creature c = FindStory(guid);
            if (c != null && c.State != State.Dead)
                PlayAnim(c, animation, c.Position + new Vector3(MathF.Sin(c.Yaw), 0f, MathF.Cos(c.Yaw)));
        }

        /// <summary>A story creature walks to a point (Travel Point); StoryTools.CreatureArrived(tag) when it gets there.</summary>
        public static void StoryWalk(ulong guid, Vector3 to, ulong tag)
        {
            Creature c = FindStory(guid);
            if (c == null || c.State == State.Dead)
                return;
            lock (sync)
            {
                c.WalkTo = to;
                c.WalkTag = tag;
                c.FocusAt = null;
                c.WanderTo = null;
                if (c.State != State.Chase)
                    c.State = State.Idle;
            }
        }

        /// <summary>A story creature's faction: 0 enemy, 1 neutral, 2 ally (the last two leave players alone).</summary>
        public static void StoryFaction(ulong guid, int faction)
        {
            Creature c = FindStory(guid);
            if (c != null)
                lock (sync)
                    c.Faction = faction;
        }

        /// <summary>A story creature looks at a point and stands still (null: back to wandering).</summary>
        public static void StoryFocus(ulong guid, Vector3? at)
        {
            Creature c = FindStory(guid);
            if (c == null)
                return;
            lock (sync)
            {
                c.FocusAt = at;
                c.WanderTo = null;
            }
            if (at is Vector3 p)
            {
                Face(c, p);
                SendMovement(c);
            }
        }

        /// <summary>A story creature's name ("Slaug"), or null when it's gone.</summary>
        public static string StoryName(ulong guid) => FindStory(guid)?.Kind.Name;

        /// <summary>Where a story creature is (null when gone).</summary>
        public static Vector3? StoryPosition(ulong guid) => FindStory(guid)?.Position;

        /// <summary>Puts a creature next to a player (dev/testing): a one-off camp member at the player's position.</summary>
        public static void SpawnNear(WorldSession session, float dx, float dz, string kindName = "kobold")
        {
            if (!kindsByName.TryGetValue(kindName ?? "kobold", out Kind kind))
                kind = DevKind(kindName) ?? KoboldMelee;
            Vector3 at = session.Position + new Vector3(dx, 0f, dz);
            uint zone = session.ZoneId;
            at.Y = TerrainHeights.HeightAt(at.X, at.Z, zone) ?? at.Y;
            long key = Pack(zone, (int)MathF.Floor(at.X / RegionSize), (int)MathF.Floor(at.Z / RegionSize));
            Creature c;
            lock (sync)
            {
                Creature[] existing = Region(zone, (int)MathF.Floor(at.X / RegionSize), (int)MathF.Floor(at.Z / RegionSize));
                c = new Creature
                {
                    Guid = GuidBase | ((ulong)key << 8) | (uint)(200 + existing.Length),
                    Zone = zone,
                    TransientId = PlayerManager.AllocateTransientId(),
                    Kind = kind,
                    Home = at,
                    Position = at,
                    Health = kind.MaxHealth,
                    NextWander = DateTime.UtcNow.AddSeconds(5)
                };
                regions[key] = existing.Append(c).ToArray();
            }
            foreach (WorldSession s in ViewersOf(c))
                SendSpawn(s, c);
        }

        private static float Flat(Vector3 a, Vector3 b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

        /// <summary>
        /// Horizontal distance from a point to the nearest edge of a region (0 inside it).
        /// </summary>
        private static float RegionDistance(int rx, int rz, Vector3 p)
        {
            float cx = Math.Clamp(p.X, rx * RegionSize, (rx + 1) * RegionSize);
            float cz = Math.Clamp(p.Z, rz * RegionSize, (rz + 1) * RegionSize);
            return MathF.Sqrt((p.X - cx) * (p.X - cx) + (p.Z - cz) * (p.Z - cz));
        }

        private static uint Home => TerrainStore.HomeContinent;

        // Region key: the landscape in bits 32+ (0 = the home continent), region x and z in 16 bits each.
        private static long Pack(uint zone, int rx, int rz) =>
            ((long)(zone == Home ? 0 : zone) << 32) | ((long)(ushort)(short)rx << 16) | (ushort)(short)rz;

        private static (uint, int, int) Unpack(long key)
        {
            uint code = (uint)(key >> 32);
            return (code == 0 ? Home : code, (short)(ushort)(key >> 16), (short)(ushort)key);
        }
    }
}
