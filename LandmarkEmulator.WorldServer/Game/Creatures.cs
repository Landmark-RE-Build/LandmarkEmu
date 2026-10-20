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
    /// - as at launch (wiki "Monsters (PvE)": on the surface only Fiery Mushrooms, every other monster in caves), the
    /// surface gets only Fiery Fungus, seeded per <see cref="RegionSize"/> region of land; the Chaos
    ///   Caverns (every cave landscape's underground caverns, <see cref="Caves.Caverns"/>) each hold a group of the cave
    ///   biome's Chomper and Slaug variants, an Abomination and Fiery Fungus, streamed by 3D distance;
    /// - behaviours: Chompers are stationary (spore cloud in reach), Abominations keep their distance and spit, Fiery
    ///   Fungus inflates when a player comes close and explodes in fire, the rest chase and hit;
    /// - players are sent only creatures within <see cref="ViewRange"/>; movement goes only to players in range, at
    ///   most <see cref="AiHz"/> times a second and only while the creature moves;
    /// - a creature attacks players who come within <see cref="AggroRange"/>, chases, hits in melee, and returns home
    ///   (healed) past <see cref="LeashRange"/>;
    /// - cavern and outpost creatures walk around cave rock and ruin walls, surface ones (off build sites) around cliffs
    ///   (<see cref="Walk"/>, <see cref="Pathing"/>, TASKS 6.61): straight when the line is clear, else along a path;
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

        /// <summary>
        /// How a kind fights. <see cref="Exploder"/>: Fiery Fungus and the Proximity Mines (<see cref="Kind.Mine"/>), set off
        /// by approach or a hit (<see cref="ThinkFungus"/>). <see cref="Dummy"/>: the Target Dummy, which only stands and
        /// swings (<see cref="ThinkDummy"/>).
        /// </summary>
        /// <see cref="Siege"/>: the Highfall Catapult, a war machine that never moves or turns and lobs rocks at distant
        /// targets in front of it (<see cref="ThinkSiege"/>, TASKS 3.13).
        private enum Behaviour { Melee, Stationary, Ranged, Exploder, Dummy, Siege }

        /// <summary>
        /// A creature kind. <paramref name="Mine"/>: an Exploder that is a Proximity Mine (or the Vine Trap, which the prop
        /// dump files as "hazard: proximity mine"): its own warning / blast effects, a knock-down, no loot, no fungus goals.
        /// <paramref name="Healer"/>: heals hurt creatures of its own side between attacks and otherwise fights as a ranged
        /// caster that follows its target (TASKS 6.21, P4696; <see cref="TryHeal"/>). <paramref name="Prop"/>: the creature
        /// prop (spawner) it was made from, 0 for the world's own kinds: its weapon (<see cref="CreatureWeapons"/>) is per prop.
        /// </summary>
        private sealed record Kind(string Name, uint Model, uint MaxHealth, uint Damage, (uint Item, uint Min, uint Max)[] Loot,
            Behaviour Style = Behaviour.Melee, bool Mine = false, bool Healer = false, uint Projectile = 0, uint Prop = 0);

        // ---- Chaos Caverns and surface monsters (launch, wiki "Monsters (PvE)"). Models come from the creature props
        // (prop dump), names from the props' own biome variants; health, damage and loot are ours (server data on live).
        private const uint MonsterEssence = 3682, AfflictionEssence = 1588, FireEssence = 1589, Dirt = 44, Stone = 46;

        private static uint ModelOf(uint prop, uint fallback) => Props.ModelOf(prop) is uint m and not 0 ? m : fallback;

        private static Kind fieryFungus, abomination;

        /// <summary>
        /// Fiery Fungus ("boom shroom"): inflates and explodes in fire. Loot: "tree loot on death, with a low chance to
        /// occasionally drop rare loot (it continues to also drop destroyed terrain loot if it explodes)" (P3522): Tree
        /// Fiber and Sap, plus the dirt / rock of the blast (amounts ours); the rare roll is in <see cref="ExplodeLoot"/>.
        /// </summary>
        private static Kind FieryFungus => fieryFungus ??= new("Fiery Fungus", ModelOf(869, ModelOf(1950, 0)), 150, 180,
            new[] { (921u, 1u, 3u), (922u, 0u, 1u), (Dirt, 2u, 6u), (Stone, 1u, 4u), (FireEssence, 0u, 1u) }, Behaviour.Exploder);

        /// <summary>A Fiery Fungus's chance of rare loot (an unknown recipe), ours.</summary>
        private const double FungusRareChance = 0.03;

        /// <summary>An exploded fungus's bag for the player who set it off: its loot and, rarely, a recipe (P3522).</summary>
        private static List<(uint, uint)> ExplodeLoot(Creature c, WorldSession trigger)
        {
            var loot = RollLoot(c.Kind);
            if (random.NextDouble() < FungusRareChance && RecipeBook.RandomUnknownRecipeItem(trigger, random) is uint recipe and not 0)
                loot.Add((recipe, 1u));
            // Its Fire Essence on the session curve like every essence (P4776, TASKS 6.17).
            return Pacing.PaceEssences(loot, trigger, random);
        }

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
        /// <summary>
        /// Fiery Fungus starts Agro Sequence1 (Stand_Break → AttackIdle swell) at this range so AttackIdle is already
        /// playing when the <see cref="FuseRange"/> fuse + 10918 boom begin. Sequence Breaks alone eat the whole 1.5 s fuse.
        /// </summary>
        private const float FungusSwellRange = 10f;
        /// <summary>
        /// Fuse matches <see cref="FungusExplosion"/>'s boom trigger (1.5 s). Linger covers that composite's
        /// material tail (ends at 3.0 s from arm = 1.5 s after detonate).
        /// </summary>
        private static readonly TimeSpan FuseTime = TimeSpan.FromSeconds(1.5), BlastLinger = TimeSpan.FromSeconds(1.5);
        /// <summary>Grow-splode Dud composite: buildup then fizzle (no boom); longer than <see cref="FuseTime"/>.</summary>
        private static readonly TimeSpan DudFuseTime = TimeSpan.FromSeconds(2.0);
        /// <summary>Composite effects (ActorCompositeEffectDefinitions): the monsters' own attacks.</summary>
        private const uint FungusExplosion = 10918, FungusImpact = 10922, FungusDud = 11629,
            ChomperSporeRelease = 11676, ChomperSporeImpact = 11611,
            AbominationSpitRelease = 11627, AbominationSpitImpact = 11625;

        /// <summary>
        /// Proximity Mine composites (0.695 ActorCompositeEffectDefinitions): 11380
        /// PFX_Combat_Misc_Explosives_ProximityMine_Warning1 when it arms, 11342 PFX_Combat_Fire_Generic_Explosion_Landmine
        /// when it goes off. (Warning2 / Warning3, 11381 / 11382, are unused: when live escalated them is unknown.)
        /// </summary>
        private const uint MineWarning = 11380, MineExplosion = 11342;

        /// <summary>A creature's melee hit landing on another creature: the generic slash impact (HitEffects "Generic").</summary>
        private const uint CreatureMeleeImpact = 11670;

        // ---- Combat hardware (PropCategory 121 "Combat Hardware" under 98 "Creatures": 2890, 1220, 2941) --------------

        /// <summary>
        /// Target Dummy 2890 ("landmark_v2: spawner: default: invuln: hazard: target dummy", model 1708 mannequin) and its
        /// ruin-template twin 2993. Hotfix 11 May 2016 (P5275 / P5433): it stands in place and swings its sword now and
        /// then, deals no damage and is ignored by enemy NPCs; 13 May (P5445): it can be attacked and "killed".
        /// </summary>
        public const uint TargetDummyProp = 2890, RuinTargetDummyProp = 2993;

        /// <summary>
        /// Proximity Mine 1220 ("landmark_v2: spawner: promo: hazard: proximity mine", model 1036) and its ruin twin 2991;
        /// the Vine Trap 2932 / 2994, whose dump description is "spawner: promo: small: hazard: proximity mine: fire".
        /// Hotfix 11 May 2016 (P5269): on a build site, mines count as creatures, with the faction and respawn settings
        /// of other creatures that don't path, such as fiery fungi; item text: it "detonates when
        /// unsuspecting individuals approach it, dealing damage and knocking them down".
        /// </summary>
        public const uint ProximityMineProp = 1220, RuinProximityMineProp = 2991, VineTrapProp = 2932, RuinVineTrapProp = 2994;

        /// <summary>The Target Dummy's hit points, and the quiet time after which it mends to full (ours).</summary>
        private const uint DummyHealth = 2000;
        private static readonly TimeSpan DummyMend = TimeSpan.FromSeconds(10);

        /// <summary>A mine's hit points (any hit sets it off) and blast damage (ours; a Fiery Fungus does 180).</summary>
        private const uint MineHealth = 100, MineDamage = 150;

        /// <summary>How often an idle story creature looks for a creature of the opposing faction (6.20; ours).</summary>
        private static readonly TimeSpan FoeScanInterval = TimeSpan.FromSeconds(1);

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

        private static Kind catapult, healer;

        /// <summary>Dev "creature" kinds that are built on first use (their models come from the prop dump).</summary>
        private static Kind DevKind(string name) => name?.ToLowerInvariant() switch
        {
            "fungus" => FieryFungus,
            "abomination" => Abomination,
            "chomper" => CavernKinds(41).Chomper,
            "woodslaug" => CavernKinds(41).Slaug,
            "catapult" => catapult ??= SpawnerKind("Highfall Catapult", HighfallCatapultProp),
            // A healer (radiance bolts; TASKS 6.21): the first spawner prop named as one that has a model.
            "healer" => healer ??= LandmarkEmulator.Shared.GameTable.GameTableManager.Instance.PropDefs.Entries
                .Select(e => (Id: e.Id, Name: PropName(e.Id))).Where(e => IsHealerName(e.Name) && Props.ModelOf(e.Id) != 0)
                .Select(e => SpawnerKind(e.Name, e.Id)).FirstOrDefault(k => k != null && k.Healer),
            // p<prop id>: what that spawner prop spawns (e.g. p2704 Barren Ranged).
            string p when p.Length > 1 && p[0] == 'p' && uint.TryParse(p[1..], out uint pid) => SpawnerKind(PropName(pid), pid),
            _ => null
        };

        private enum State { Idle, Chase, Return, Dead, Flee }

        private sealed class Creature
        {
            public ulong Guid;
            /// <summary>
            /// The surface region whose array holds it (<see cref="regions"/>; unused for cavern creatures): fixed at creation.
            /// <see cref="Home"/> can move (a story walk's Travel Point becomes its home), so the region is never worked out
            /// from it again: <see cref="ViewersOf"/> and <see cref="RemoveFromRegion"/> use this key (bughunt-combat.md B4).
            /// </summary>
            public long RegionKey;
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
            /// <summary>
            /// A story creature's shown name when its spawner was renamed (TASKS 6.30; P4227: right-clicking a creature
            /// gives more options, its name among them); null = the kind's name. Display only: kill goals and logs
            /// still use <see cref="Kind"/>.
            /// </summary>
            public string DisplayName;
            /// <summary>Story faction: 0 enemy (attacks), 1 neutral, 2 ally (neither attacks players).</summary>
            public int Faction;
            /// <summary>
            /// A story spawner's Combat Tactics (row 31): <see cref="TacticsStandard"/>, <see cref="TacticsStationary"/>
            /// (fights where it stands) or <see cref="TacticsNothing"/> (never fights).
            /// </summary>
            public int Tactics;
            /// <summary>Damage taken per player since it last calmed down: it attacks the highest (P3651).</summary>
            public readonly Dictionary<WorldSession, uint> Threat = new();
            /// <summary>
            /// A story creature on a site deep underground (a cave build site): surface regions aren't streamed down there,
            /// so it is shown by 3D distance like a cavern group (<see cref="deepStorySent"/>).
            /// </summary>
            public bool Deep;
            /// <summary>
            /// A story creature's build site (claim id; 0 = none): its navigation walks on the site's grid of player builds and
            /// props (Pathing.SiteReady, OPEN-LEADS L133).
            /// </summary>
            public ulong Site;
            /// <summary>
            /// A story exploder's spawner "Damages Terrain" (row 105 npc_abilities_damage_terrain, default 1; set by
            /// <see cref="StoryDamagesTerrain"/>).
            /// </summary>
            public bool DamagesTerrain = true;
            /// <summary>
            /// A story creature's weapon visuals: true = Equipped (weapon-ready idle, AnimationSelection
            /// <see cref="AnimNpcWeaponsEquipped"/>), false = Sheathed (the network's default; <see cref="StoryWeapons"/>).
            /// </summary>
            public bool WeaponsEquipped;
            /// <summary>
            /// A story creature's mood and stance (the Story Node selections it last played: mood 218-221 sets the actor's
            /// Emote_Emotion, stance 223 / 224 / 246 / 248 / 250 / 251 / 351 / 480 its Stance_Index, AnimationSelectionVariables
            /// [P]); 0 = the network's default (Calm = Emotion 0, Standing = Stance 0). Kept so a player who sees the creature
            /// later gets them too (<see cref="SendPose"/>; 7 Jun 2016: creature moods and stances are kept between
            /// sessions and site visits).
            /// </summary>
            public uint StoryMood, StoryStance;
            /// <summary>
            /// Dev "creatureweapon": a weapon set on this creature by hand (<see cref="WeaponOverridden"/>; null = none),
            /// in place of its kind's (<see cref="CreatureWeapons.ForModel"/>).
            /// </summary>
            public CreatureWeapons.Weapon WeaponOverride;
            public bool WeaponOverridden;
            /// <summary>
            /// A story creature whose spawner changed its health or damage (TASKS 2.14): it drops no loot (P6429: so
            /// nobody farms crowds of weakened enemies); kills still count.
            /// </summary>
            public bool StrengthChanged;
            /// <summary>Walking to a Travel Point (story); <see cref="WalkTag"/> is the point, told on arrival.</summary>
            public Vector3? WalkTo;
            public ulong WalkTag;
            /// <summary>Looking at a point (Focus Node): it stands still facing it.</summary>
            public Vector3? FocusAt;
            /// <summary>The Chaos Cavern it lives in (index into <see cref="Caves.Caverns"/>), −1 on the surface.</summary>
            public int Cavern = -1;
            /// <summary>
            /// Homemade melee combo stage (0–2): picks NPC Enter/Release AttackIndex 0 / 1 / 2 (357/360, 356/361, 358/362).
            /// </summary>
            public byte AttackCombo;
            /// <summary>A Fiery Fungus's fuse: it explodes at this time.</summary>
            public DateTime FuseAt;
            /// <summary>Fiery Fungus already got WeaponIdle 1 (Agro Sequence1) for an approaching player.</summary>
            public bool FungusAgro;
            /// <summary>An exploded fungus stays visible until this time (its blast plays on it), then goes.</summary>
            public DateTime RemoveAt;
            /// <summary>
            /// The creature it fights (story creatures only, 6.20): an ally's enemy or an enemy's ally. While set it wins
            /// over <see cref="Target"/>.
            /// </summary>
            public Creature Foe;
            /// <summary>Where the current fight started from: the leash is measured from here (home, or the spot a
            /// scripted walk was interrupted at). Null out of combat (then <see cref="Home"/>).</summary>
            public Vector3? Anchor;
            /// <summary>When an idle story creature next looks for a foe (<see cref="FoeScanInterval"/>).</summary>
            public DateTime NextFoeScan;
            /// <summary>Target Dummy: its next swing; when it was last hurt (it mends after <see cref="DummyMend"/>).</summary>
            public DateTime NextSwing, LastHurt;
            /// <summary>
            /// The heading it had when it appeared at <see cref="Home"/> (taken on its first AI step, before it can move, or
            /// when a story walk makes a new home): a creature back home faces this way again (P4715) and respawns facing it.
            /// A siege engine's is its placed prop's rotation (<see cref="PlacedHeading"/>).
            /// </summary>
            public float? HomeYaw;
            /// <summary>Siege engine: where its rock in flight will land and when (default: none in flight).</summary>
            public Vector3 ShotAt;
            public DateTime ShotLandsAt;
            /// <summary>The client projectile of the rock in flight (Projectiles 0xEE), 0 = none.</summary>
            public uint ShotProjectile;
            /// <summary>Healer: when it may heal next (<see cref="HealInterval"/>).</summary>
            public DateTime NextHeal;
            /// <summary>
            /// The ability a Combat Node asked for ("perform Ability N", <see cref="StoryUseAbility"/>): its next attack is
            /// this one (name in the combat spam, damage by rank) instead of its basic attack. Null: the basic one.
            /// </summary>
            public Combat.CreatureAbilities.Ability Pending;
            /// <summary>A shield block (<see cref="Combat.CreatureAbilities.Role.Block"/>): no damage is taken until then.</summary>
            public DateTime ShieldUntil;
            /// <summary>A fungus told to use its "Grow-splode Dud": the next blast harms nobody (ThinkFungus).</summary>
            public bool Dud;
            /// <summary>Siege engine: it stays in combat until then (renewed while it has a target, <see cref="SiegeCalm"/>).</summary>
            public DateTime BusyUntil;
            /// <summary>
            /// Its way around obstacles (<see cref="Walk"/>, TASKS 6.61): the waypoints (<see cref="PathIndex"/> is the next),
            /// the goal they were searched for, whether they end short of it (unreachable: <see cref="PathPartial"/>), the
            /// search in flight and when it may search again. Null while it walks straight.
            /// </summary>
            public List<Vector3> Path;
            public int PathIndex;
            public Vector3 PathGoal;
            public bool PathPartial;
            public Pathing.PathRequest PathQuery;
            public DateTime NextPathQuery;
            /// <summary>A story creature's next "path around" log line (at most one per <see cref="PathLogInterval"/>).</summary>
            public DateTime NextPathLog;
            /// <summary>Spits so far: every other one is the triple spread (<see cref="Spit"/>).</summary>
            public int Spits;
        }

        /// <summary>The Chaos Caverns' groups by cavern index, made on first approach.</summary>
        private static readonly Dictionary<int, Creature[]> cavernGroups = new();
        /// <summary>The caverns whose group each player has on screen.</summary>
        private static readonly Dictionary<WorldSession, HashSet<int>> cavernsSent = new();
        /// <summary>The deep story creatures (<see cref="Creature.Deep"/>) each player has on screen.</summary>
        private static readonly Dictionary<WorldSession, HashSet<ulong>> deepStorySent = new();
        private const ulong CavernGuidBase = 0x5100_0000_0000_0000;
        /// <summary>A cavern's group is shown within this distance (3D) and dropped beyond the second.</summary>
        private const float CavernViewRange = 150f, CavernDropRange = 200f;

        /// <summary>Distance a creature reacts over: 3D in the caverns (players above must not wake them), else flat.</summary>
        private static float Dist(Creature c, Vector3 p) => c.Cavern >= 0 ? Vector3.Distance(c.Position, p) : Flat(c.Position, p);

        /// <summary>
        /// The floor under a cavern's middle (cached in <see cref="Caves.FloorOf"/>, on its own landscape's cave pieces): where
        /// its group stands, and what streaming measures to.
        /// </summary>
        private static Vector3 CavernFloor(int index) => Caves.FloorOf(index);

        /// <summary>A cavern's monsters: a Chomper (two at times), two Slaugs, an Abomination and two Fiery Fungus.</summary>
        private static Creature[] CavernGroup(int index)
        {
            if (cavernGroups.TryGetValue(index, out Creature[] group))
                return group;
            (Vector3 inAir, uint biome) = Caves.Caverns[index];
            // Its landscape (the home continent's caverns come first; F2: every landscape with caves).
            uint zone = Caves.ZoneOf(index);
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
                    if (Caves.FloorNear(centre + new Vector3((float)(Math.Cos(a) * r), 2f, (float)(Math.Sin(a) * r)), 10f, zone) is not Vector3 at)
                        continue;
                    // Not inside a cavern graveyard's area (its "Prevent Spawning" property, Graveyards.PreventsSpawning).
                    if (Graveyards.PreventsSpawning(zone, at))
                        continue;
                    list.Add(new Creature
                    {
                        Guid = CavernGuidBase | ((ulong)index << 8) | (uint)i,
                        Zone = zone,
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
            // An outpost's garrison: the ruin template's own spawners, each creature at its spawner's spot (the builder put
            // them on the ruin's floors: no random placement, so none ends up inside a wall, P5924); the BOSS is the Elite
            // (boss display). Cave monster spawners in the ruin give the cavern's own kinds (the fungus explodes).
            int g = 0;
            foreach ((uint spawner, string name, bool boss, Vector3 spot) in Outposts.GarrisonFor(index))
            {
                Kind kind = name.Contains("Fung", StringComparison.OrdinalIgnoreCase) ? FieryFungus
                    : name.Contains("Abomination", StringComparison.OrdinalIgnoreCase) ? Abomination
                    : name.Contains("Chomper", StringComparison.OrdinalIgnoreCase) ? chomper
                    : name.Contains("Slaug", StringComparison.OrdinalIgnoreCase) ? slaug
                    : SpawnerKind(name, spawner);
                if (kind == null)
                    continue;
                {
                    Vector3 at = spot;
                    list.Add(new Creature
                    {
                        Guid = CavernGuidBase | ((ulong)index << 8) | (uint)(100 + g++),
                        Zone = zone,
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
                }
            }
            group = list.ToArray();
            cavernGroups[index] = group;
            return group;
        }

        /// <summary>
        /// A cavern got its outpost after its group was made (another landscape's outposts are placed in the background,
        /// Outposts.TakeArrivals): the group is dropped from every player who has it and made again, with the garrison, when
        /// they stream it next (a second later).
        /// </summary>
        public static void CavernChanged(int index)
        {
            var drops = new List<(WorldSession Session, ulong Guid)>();
            lock (sync)
            {
                if (!cavernGroups.Remove(index, out Creature[] old))
                    return;
                // The new group reuses the guids: the lookup must not find the old creatures any more.
                byGuid = new Dictionary<ulong, Creature>();
                foreach ((WorldSession session, HashSet<int> caverns) in cavernsSent)
                    if (caverns.Remove(index))
                        drops.AddRange(old.Where(c => c.State != State.Dead).Select(c => (session, c.Guid)));
            }
            foreach ((WorldSession session, ulong guid) in drops)
                RemoveFor(session, guid);
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
                deepStorySent.Remove(session);
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
            ResolveShots(now);
            if (now < nextTick)
                return;
            float dt = 1f / AiHz;
            nextTick = now.AddSeconds(dt);
            List<Creature> active;
            lock (sync)
                active = sent.Values.SelectMany(h => h).Distinct().Where(regions.ContainsKey)
                    .SelectMany(k => regions[k])
                    .Concat(cavernsSent.Values.SelectMany(h => h).Distinct().Where(cavernGroups.ContainsKey).SelectMany(i => cavernGroups[i]))
                    .Concat(DeepStoryInView())
                    .Distinct().ToList();
            // One creature's step that throws must not stop every other creature's this tick (and every tick after, while
            // it keeps throwing): the step runs story Links synchronously (StoryTools.CreatureCombat / CreatureArrived /
            // CreatureDied through a kill), any of whose actions can fail (bughunt-combat.md B10).
            foreach (Creature c in active)
            {
                try
                {
                    Think(c, players, now, dt);
                }
                catch (Exception e)
                {
                    thinkFailures++;
                    if (now >= nextThinkFailureLog)
                    {
                        nextThinkFailureLog = now.AddSeconds(30);
                        log.Error(e, $"AI step of {c.Kind?.Name} 0x{c.Guid:X} failed ({thinkFailures} failed step(s) so far); the other creatures go on.");
                    }
                }
            }
        }

        private static long thinkFailures;
        private static DateTime nextThinkFailureLog;

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
                    // An exploded story fungus / mine: gone for good (its spawner makes a new one, StoryTools.CreatureDied).
                    if (c.Story)
                    {
                        RemoveFromRegion(c);
                        return;
                    }
                }
                if (now >= c.RespawnAt && !c.Story)
                    Respawn(c);
                return;
            }
            if (c.Kind.Style == Behaviour.Exploder)
            {
                ThinkFungus(c, players, now);
                return;
            }
            if (c.Kind.Style == Behaviour.Dummy)
            {
                ThinkDummy(c, now);
                return;
            }
            // A siege engine never moves (it takes its heading from its placed prop on its first step).
            if (c.Kind.Style == Behaviour.Siege && c.State is State.Idle or State.Chase)
            {
                ThinkSiege(c, players, now);
                return;
            }
            // The spawn heading (P4715): taken on the first step, before the creature can have moved or turned (new
            // creatures start Idle with their first wander seconds away; an engage this step only sets the state).
            c.HomeYaw ??= c.Yaw;
            c.Moved = false;
            Vector3 before = c.Position;
            switch (c.State)
            {
                case State.Idle:
                {
                    // Combat comes before a scripted walk: combat AI outranks everything else on every creature (dev, 4
                    // May 2016, "[Feedback] Prop Linking", P5225). A walk that combat interrupts goes on afterwards
                    // (EndCombat).
                    if (Engage(c, players, now))
                        break;
                    if (c.WalkTo is Vector3 dest)
                    {
                        // A story walk: the Travel Point becomes its new home, and the point hears it arrived.
                        if (Walk(c, dest, WanderSpeed * 1.5f * dt))
                        {
                            c.WalkTo = null;
                            c.Home = c.Position;
                            // Its new home's heading: the way it arrived (P4715 faces it this way after a fight).
                            c.HomeYaw = c.Yaw;
                            StoryTools.CreatureArrived(c.Guid, c.WalkTag);
                        }
                        break;
                    }
                    if (c.FocusAt is Vector3 focus)
                    {
                        Face(c, focus);
                        break;
                    }
                    // Chompers stand; so do creatures on Combat Tactics "Stationary" (in place until visitors fight
                    // them) and "Do Nothing" (in place, doing nothing) — wiki "Creature Prop", General Settings.
                    if (c.Kind.Style == Behaviour.Stationary || c.Tactics != TacticsStandard)
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
                        // With navigation: only a spot it can stroll to in a straight line (none into a wall or off a
                        // ledge); otherwise it stays put this time.
                        if (!DirectOk(c, c.WanderTo.Value))
                            c.WanderTo = null;
                    }
                    break;
                }
                case State.Chase:
                {
                    // The fight's other side: a creature foe (story creatures, 6.20) wins over a player target.
                    WorldSession t = c.Target;
                    Creature f = c.Foe;
                    // A player who reaches a graveyard's buffer zone is let go (wiki Graveyard: roaming monsters don't
                    // attack there; Graveyards.InSafeZone).
                    bool Valid(WorldSession p) => p != null && !Health.IsDead(p) && players.Contains(p) && p.ZoneId == c.Zone
                        && MayFight(c, p) && !Graveyards.InSafeZone(p.ZoneId, p.Position);
                    bool leashed = Flat(c.Position, c.Anchor ?? c.Home) > LeashRange;
                    if (f != null && !FoeValid(c, f))
                        f = null;
                    if (f == null && !Valid(t) && !leashed)
                    {
                        // Its target is gone (dead, left): the next highest damage dealer, if any (P3651); else a story
                        // creature takes on the next foe near it.
                        lock (sync)
                        {
                            foreach (WorldSession gone in c.Threat.Keys.Where(p => !Valid(p)).ToList())
                                c.Threat.Remove(gone);
                            t = c.Faction != FactionAlly && c.Threat.Count > 0 ? c.Threat.MaxBy(k => k.Value).Key : null;
                            c.Target = t;
                        }
                        if (t == null && c.Story)
                            f = NearestFoe(c, AggroRange);
                    }
                    c.Foe = f;
                    if (leashed || (f == null && !Valid(t)))
                    {
                        EndCombat(c, toIdle: false);
                        break;
                    }
                    Vector3 tp = f != null ? f.Position : t.Position;
                    ulong tg = f != null ? f.Guid : t.PlayerGuid;
                    // One attack on whichever it fights: the swing, the kind's release / impact effects, then the damage
                    // (a player's defences, or a creature's own health with hit visuals: HurtCreature).
                    void Attack(uint release, uint impact)
                    {
                        c.NextAttack = now + AttackInterval;
                        // Which of its abilities this is (Combat.CreatureAbilities): the one a Combat Node asked for, else
                        // its basic attack. Its name goes to the combat spam; ranks and specials change the damage.
                        Combat.CreatureAbilities.Ability ability = c.Pending ?? Combat.CreatureAbilities.Basic(c.Kind.Name);
                        c.Pending = null;
                        uint damage = ability == null ? c.Kind.Damage : (uint)MathF.Round(c.Kind.Damage * ability.DamageFactor);
                        PlayNpcSwing(c, tp);
                        if (release == AbominationSpitRelease && c.Kind.Projectile != 0)
                        {
                            Shoot(c, f, t, tp, now, ability);
                            return;
                        }
                        if (release == AbominationSpitRelease)
                        {
                            // A projectile that can be dodged: the impact and the damage come when it arrives (Spit).
                            HitEffects.PlayOn(c.Guid, release, ViewersOf(c));
                            Spit(c, f, t, tp, now, ability);
                            return;
                        }
                        if (release != 0 || impact != 0)
                        {
                            List<WorldSession> viewers = ViewersOf(c);
                            if (release != 0)
                                HitEffects.PlayOn(c.Guid, release, viewers);
                            if (release == HealerBoltRelease)
                                ShowBolt(c, tp + new Vector3(0f, 1f, 0f), tg, viewers);
                            if (impact != 0)
                                HitEffects.PlayOn(tg, impact, viewers);
                        }
                        if (f != null)
                            HurtCreature(c, f, damage);
                        else
                            HurtPlayer(c, t, damage, ability);
                    }
                    uint meleeImpact = f != null ? CreatureMeleeImpact : 0u;
                    float d = Dist(c, tp);
                    // A healer first looks after its side: a heal takes this step (it is "busy healing", P4696); when nobody
                    // needs one it fights on below as a ranged caster and follows a target that leaves its range.
                    if (c.Kind.Healer && TryHeal(c, now))
                        break;
                    if (c.Tactics == TacticsStationary && c.Kind.Style != Behaviour.Stationary)
                    {
                        // Combat Tactics "Stationary": it turns and strikes whoever is in reach (spits when ranged), but
                        // never moves; out of reach for long it calms down (as a Chomper).
                        Face(c, tp);
                        float reach = c.Kind.Style == Behaviour.Ranged ? SpitRange : AttackRange;
                        if (d > Math.Max(reach * 1.6f, AggroRange))
                            EndCombat(c, toIdle: true);
                        else if (d <= reach && MathF.Abs(tp.Y - c.Position.Y) <= MaxReachHeight && now >= c.NextAttack
                            && CanAct(c) && (c.Kind.Style != Behaviour.Ranged || Sees(c, tp)))
                            Attack(0, meleeImpact);
                        break;
                    }
                    if (c.Kind.Style == Behaviour.Stationary)
                    {
                        // A Chomper doesn't move: it turns and spews its spore cloud on anyone in reach, and calms down
                        // once they're gone.
                        Face(c, tp);
                        // (Calms only beyond AggroRange, so an idle Chomper doesn't re-engage the same target at once and
                        // flap its spawner's enters / exits combat Links.)
                        if (d > Math.Max(SporeReach * 1.6f, AggroRange + 1f))
                            EndCombat(c, toIdle: true);
                        else if (d <= SporeReach && now >= c.NextAttack && CanAct(c))
                            Attack(ChomperSporeRelease, ChomperSporeImpact);
                        break;
                    }
                    if (c.Kind.Style == Behaviour.Ranged)
                    {
                        // An Abomination closes in to spitting range, backs off from anyone too close, and spits. A
                        // healer does the same with radiance bolts: healers with nobody to heal now chase enemies who
                        // leave their range instead of standing still (26 Apr 2016, P4696) [P] — the
                        // step towards a target beyond SpitRange is that pursuit.
                        if (d > SpitRange)
                            Walk(c, tp, RunSpeed * 0.8f * dt, stopShort: SpitRange * 0.8f, chase: true);
                        else if (d < SpitKeepAway)
                        {
                            Vector3 away = new(c.Position.X - tp.X, 0f, c.Position.Z - tp.Z);
                            if (away.LengthSquared() > 0.01f)
                                Step(c, c.Position + Vector3.Normalize(away) * 4f, RunSpeed * 0.6f * dt);
                        }
                        else
                        {
                            Face(c, tp);
                            // No shot through rock or an outpost wall (P5727): it moves in until it sees the target.
                            if (now >= c.NextAttack && CanAct(c) && !Sees(c, tp))
                                Walk(c, tp, RunSpeed * 0.8f * dt, stopShort: 2f, chase: true);
                            else if (now >= c.NextAttack && CanAct(c))
                            {
                                if (c.Kind.Healer)
                                    Attack(HealerBoltRelease, HealerBoltImpact);
                                else
                                    Attack(AbominationSpitRelease, AbominationSpitImpact);
                            }
                        }
                        break;
                    }
                    if (d > AttackRange)
                        Walk(c, tp, RunSpeed * dt, stopShort: AttackRange * 0.8f, chase: true);
                    else if (MathF.Abs(tp.Y - c.Position.Y) > MaxReachHeight)
                    {
                        // Right above or below (the creature walks the generated ground; the player may be in a cave,
                        // on rocks or on a build): it can't reach, so it waits there, facing the player.
                        Face(c, tp);
                    }
                    else
                    {
                        Face(c, tp);
                        if (now >= c.NextAttack && CanAct(c))
                            Attack(0, meleeImpact);
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
                    if (Walk(c, c.Home, RunSpeed * dt))
                    {
                        DropPath(c);
                        c.State = State.Idle;
                        c.Health = c.Kind.MaxHealth;
                        SendHitpoints(c, 0);
                        // A creature back at its spawn point faces the right way again (26 Apr 2016, P4715) [P]: the
                        // heading it spawned with. Moved → one movement update (the arrival step
                        // sends one anyway, so this costs no extra packet).
                        if (c.HomeYaw is float homeYaw && MathF.Abs(homeYaw - c.Yaw) > 0.01f)
                        {
                            c.Yaw = homeYaw;
                            c.Moved = true;
                        }
                        // Its next wander waits a little, so it is seen standing at its post facing the right way.
                        c.NextWander = now.AddSeconds(5 + random.NextDouble() * 10);
                    }
                    break;
            }
            bool moving = c.Position != before;
            c.Velocity = moving && dt > 0 ? new Vector3(c.Position.X - before.X, c.Position.Y - before.Y, c.Position.Z - before.Z) / dt : Vector3.Zero;
            if (c.Moved || (c.WasMoving && !moving))
                SendMovement(c);
            c.WasMoving = moving;
        }

        /// <summary>
        /// Moves toward a point on the ground in a straight line; true once there — or blocked (a wall or drop: callers
        /// such as wandering then give up the point).
        /// </summary>
        private static bool Step(Creature c, Vector3 to, float distance, float stopShort = 0f)
        {
            // Frozen, Electrocuted, Crushed or Condemned creatures stay put; Crippled ones move at half speed.
            if (!CanMove(c))
                return false;
            bool arrived = StepRaw(c, to, distance * MoveFactor(c), stopShort, null, out bool blocked);
            return arrived || blocked;
        }

        /// <summary>A height correction from a client (<see cref="CorrectHeights"/>) up to this far off the grid's floor is kept.</summary>
        private const float MaxHeightOffset = 1.5f;

        /// <summary>
        /// One straight move of at most <paramref name="distance"/> metres (speed factors already applied). Returns true once
        /// within <paramref name="stopShort"/>; <paramref name="blocked"/> when the ground ahead can't be walked onto.
        /// The height: for cavern creatures with navigation (<see cref="NavMode"/>) the floor the grid gives for the step
        /// (cave rock and outpost ruin floors, which the cave-only floor search below never saw; a client's height correction
        /// is kept as an offset); else as before 6.61 — the cave floor of the client's cave shapes for cavern creatures, the
        /// generated ground on the surface (where the grid only refuses cliffs). <paramref name="waypoint"/>: following a
        /// found path, a step the grid refuses (rounding between the path's columns and this step's) is taken anyway, its
        /// height eased towards the waypoint's.
        /// </summary>
        private static bool StepRaw(Creature c, Vector3 to, float distance, float stopShort, Vector3? waypoint, out bool blocked)
        {
            blocked = false;
            Vector3 flat = new(to.X - c.Position.X, 0f, to.Z - c.Position.Z);
            float length = flat.Length();
            if (length <= stopShort + 0.05f)
                return true;
            float move = MathF.Min(distance, length - stopShort);
            Vector3 next = c.Position + flat / length * move;
            Pathing.Probe probe = Pathing.Probe.Unknown;
            float here = 0f, there = 0f;
            if (NavMode(c) is Pathing.Mode mode)
                probe = Pathing.Step(c.Zone, mode, c.Position, next, out here, out there);
            if (probe == Pathing.Probe.None)
            {
                if (waypoint is not Vector3 w)
                {
                    blocked = true;
                    return false;
                }
                next.Y = c.Position.Y + (w.Y - c.Position.Y) * MathF.Min(1f, move / length);
            }
            else if (probe == Pathing.Probe.Found && (c.Cavern >= 0 || c.Story))
            {
                // Cavern creatures, and story creatures on their site's grid (player floors, stairs, dug holes; L133).
                float offset = c.Position.Y - here;
                next.Y = there + (MathF.Abs(offset) <= MaxHeightOffset ? offset : 0f);
            }
            else if (c.Cavern >= 0)
            {
                // Cavern creatures walk the cave floor (the client's own cave shapes, of their landscape); a wall or drop
                // stops them.
                if (Caves.FloorNear(next with { Y = c.Position.Y }, 3f, c.Zone) is not Vector3 floor)
                {
                    blocked = true;
                    return false;
                }
                next.Y = floor.Y;
            }
            else if (c.Deep)
                // A story creature on a cave build site: the cave floor when there is one near its height, else it keeps
                // its height (a built floor; the client's CorrectNpcHeights reports fix the rest). Never the surface.
                next.Y = Caves.FloorNear(next with { Y = c.Position.Y }, 3f, c.Zone) is Vector3 caveFloor ? caveFloor.Y : c.Position.Y;
            else
                next.Y = TerrainHeights.HeightAt(next.X, next.Z, c.Zone) ?? c.Position.Y;
            c.Position = next;
            c.Yaw = MathF.Atan2(flat.X, flat.Z);
            c.Moved = true;
            return length - stopShort <= distance;
        }

        // ---- Navigation (Pathing, TASKS 6.61) -------------------------------------------------------------------------

        /// <summary>A goal must move this far (m), or a quarter of the way to it, before its path is searched again.</summary>
        private const float RepathDistance = 2f;
        /// <summary>At most one path search per creature this often; a search not answered in time is given up.</summary>
        private static readonly TimeSpan RepathInterval = TimeSpan.FromSeconds(1), PathQueryTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Which navigation a creature walks with (<see cref="Pathing"/>), or null for the plain straight step: cavern and
        /// outpost creatures on the cave / ruin grid; surface creatures off build sites on the generated ground; story
        /// creatures on their build site's grid of player builds and props (OPEN-LEADS L133: live's creatures pathed around
        /// player walls and props, P5781 / forum 56713) — the surface one, or the cave one on a cave build site — once that
        /// grid is built (Pathing.SiteReady; until then, with setting SitePathing off, or a creature without a site, the
        /// straight step as before).
        /// </summary>
        private static Pathing.Mode? NavMode(Creature c)
        {
            if (!Pathing.Enabled)
                return null;
            if (c.Cavern >= 0)
                return Pathing.Mode.Cave;
            if (c.Story)
            {
                if (!Pathing.SiteReady(c.Zone, c.Site))
                    return null;
                if (c.Deep)
                    return CavePieces.HasData(c.Zone) ? Pathing.Mode.Cave : null;
                return TerrainHeights.Has(c.Zone) ? Pathing.Mode.Surface : null;
            }
            if (c.Deep || !TerrainHeights.Has(c.Zone))
                return null;
            return Pathing.Mode.Surface;
        }

        /// <summary>A story creature logs its path around obstacles at most this often (test evidence, L133).</summary>
        private static readonly TimeSpan PathLogInterval = TimeSpan.FromSeconds(5);

        /// <summary>Can it walk straight to a point? (No navigation, or tiles still building: yes, as before.)</summary>
        private static bool DirectOk(Creature c, Vector3 to)
            => NavMode(c) is not Pathing.Mode mode
                || Pathing.Line(c.Zone, mode, c.Position, to) is Pathing.LineResult.Clear or Pathing.LineResult.Unknown;

        /// <summary>Forgets a creature's path and search.</summary>
        private static void DropPath(Creature c)
        {
            c.Path = null;
            c.PathQuery = null;
            c.PathPartial = false;
        }

        /// <summary>
        /// Moves toward a goal around obstacles (TASKS 6.61): straight when the line is clear — or when the navigation can't
        /// tell (tiles still being built, or the creature or its goal stands where the grid has rock: the data doesn't match
        /// the world there, so it fails open as Sight does) — else along a path from the background search (one search per
        /// creature at a time, again when the goal has moved). While the first search runs the creature waits, a chaser
        /// facing its target. A chase whose path ends short of the target (out of reach: a ledge, a gap) stands at the
        /// nearest point facing it, like a creature below a player it can't reach; walking home or to a Travel Point with no
        /// way found steps straight, as before, so nobody is stranded. True once within <paramref name="stopShort"/>.
        /// </summary>
        private static bool Walk(Creature c, Vector3 to, float distance, float stopShort = 0f, bool chase = false)
        {
            if (NavMode(c) is not Pathing.Mode mode)
                return Step(c, to, distance, stopShort);
            float remaining = Flat(c.Position, to);
            if (remaining <= stopShort + 0.05f)
            {
                DropPath(c);
                return true;
            }
            if (Pathing.Line(c.Zone, mode, c.Position, to) != Pathing.LineResult.Blocked)
            {
                DropPath(c);
                return Step(c, to, distance, stopShort);
            }
            DateTime now = DateTime.UtcNow;
            if (c.PathQuery is Pathing.PathRequest query)
            {
                if (query.Done)
                {
                    c.PathQuery = null;
                    c.Path = query.Points ?? new List<Vector3>();
                    c.PathIndex = 0;
                    c.PathGoal = query.To;
                    c.PathPartial = !query.Reached;
                    if (c.Story && now >= c.NextPathLog)
                    {
                        c.NextPathLog = now + PathLogInterval;
                        log.Info($"Site pathing: story {c.Kind.Name} 0x{c.Guid:X} on site {c.Site}: straight line to {query.To} blocked, path of "
                            + $"{c.Path.Count} waypoint(s) {(query.Reached ? "reaching the goal" : "ending short (out of reach)")} ({query.Nodes} nodes): "
                            + string.Join(" → ", c.Path.Take(12).Select(p => $"({p.X:0.0}, {p.Y:0.0}, {p.Z:0.0})")));
                    }
                }
                else if (now - query.Created > PathQueryTimeout)
                    c.PathQuery = null;
            }
            float moved = c.Path == null ? float.MaxValue : Vector3.Distance(c.PathGoal, to);
            if (moved > Math.Max(RepathDistance, 0.25f * remaining) && c.PathQuery == null && now >= c.NextPathQuery)
            {
                c.PathQuery = Pathing.RequestPath(c.Zone, mode, c.Position, to);
                c.NextPathQuery = now + RepathInterval;
            }
            // A path towards a goal that has since moved far away is no use while the new one is searched.
            if (c.Path != null && moved > Math.Max(8f, 0.5f * remaining))
                c.Path = null;
            if (c.Path == null)
            {
                if (chase)
                    Face(c, to);
                return false;
            }
            if (c.PathIndex >= c.Path.Count)
            {
                // The end of the path: out of reach (or no way at all) — a chaser stands facing its target; anyone else, or
                // the last bit of a complete path, steps straight as before.
                if (chase && (c.PathPartial || c.Path.Count == 0))
                {
                    Face(c, to);
                    return false;
                }
                return Step(c, to, distance, stopShort);
            }
            if (!CanMove(c))
                return false;
            float budget = distance * MoveFactor(c);
            for (int guard = 0; guard < 4 && budget > 0.01f && c.PathIndex < c.Path.Count; guard++)
            {
                Vector3 w = c.Path[c.PathIndex];
                float d = Flat(c.Position, w);
                if (!StepRaw(c, w, budget, 0f, w, out _))
                    break;
                budget -= d;
                c.PathIndex++;
            }
            return false;
        }

        /// <summary>Admin monitor (page 32 "Characters", ours): creatures with a path or a path search right now.</summary>
        public static int PathingCharacters()
        {
            lock (sync)
                return regions.Values.SelectMany(r => r).Concat(cavernGroups.Values.SelectMany(g => g))
                    .Count(c => c.State != State.Dead && (c.Path != null || c.PathQuery != null));
        }

        /// <summary>Dev command "pathing": the grid at the player's spot and each creature within 40 m with its walk state.</summary>
        public static List<string> DevPathing(WorldSession session)
        {
            uint zone = session.ZoneId;
            Pathing.Mode here = Caves.HasCaverns(zone) && TerrainHeights.DepthBelowGround(session.Position, zone) > UndergroundShowDepth
                ? Pathing.Mode.Cave : Pathing.Mode.Surface;
            var lines = new List<string> { Pathing.Explain(zone, here, session.Position) };
            lines.AddRange(Pathing.ExplainSites());
            lock (sync)
                foreach (Creature c in Nearby(zone, session.Position, 40f).Where(Alive))
                {
                    Pathing.Mode? mode = NavMode(c);
                    string line = mode is Pathing.Mode m ? Pathing.Line(c.Zone, m, c.Position, session.Position).ToString() : "-";
                    lines.Add($"{c.Kind.Name} {c.Guid:X} {c.State} at {c.Position} ({Vector3.Distance(c.Position, session.Position):0.0} m): "
                        + $"nav {(mode?.ToString() ?? "off (straight steps)")}{(c.Story ? $" [{Pathing.SiteState(c.Zone, c.Site)}]" : "")}, line to you {line}, "
                        + (c.Path != null ? $"path {c.PathIndex}/{c.Path.Count}{(c.PathPartial ? " partial" : "")} to {c.PathGoal}" : "no path")
                        + (c.PathQuery != null ? ", searching" : ""));
                }
            return lines;
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
        /// <param name="ability">The creature ability that hit (Combat.CreatureAbilities): its name is the combat spam's
        /// [*Ability*] (CombatResourceEvent +0x40; rank 2 / 3 names only to 0.695 clients). Null: no name.</param>
        private static void HurtPlayer(Creature c, WorldSession t, uint damage, Combat.CreatureAbilities.Ability ability = null)
        {
            uint amount = (uint)Math.Round(damage * Combat.Statuses.DealtFactor(c.Guid));
            amount = Combat.WeaponCombat.Incoming(t, amount, c.Guid, c.Position);
            if (amount == 0)
                return;
            Health.Damage(t, amount, sourceGuid: c.Guid);
            SendCombatEvent(t, c.Guid, t.PlayerGuid, amount, ability?.Name ?? 0);
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

        /// <summary>Admin monitor (/admin/monitor): creatures alive per landscape, in generated regions and caverns.</summary>
        public static Dictionary<uint, int> AliveByZone()
        {
            var result = new Dictionary<uint, int>();
            lock (sync)
                foreach (Creature c in regions.Values.Concat(cavernGroups.Values).SelectMany(g => g))
                    if (c != null && Alive(c))
                        result[c.Zone] = result.GetValueOrDefault(c.Zone) + 1;
            return result;
        }

        /// <summary>Living creatures within <paramref name="radius"/> m of a point (height allowance radius + 3 m).</summary>
        public static List<ulong> InRadius(uint zone, Vector3 at, float radius)
        {
            lock (sync)
                return Nearby(zone, at, radius).Where(c => Alive(c) && MathF.Abs(c.Position.Y - at.Y) <= radius + 3f)
                    .OrderBy(c => Vector3.Distance(c.Position, at)).Select(c => c.Guid).Distinct().ToList();
        }

        /// <summary>
        /// <see cref="InRadius"/> for an area attack: a cavern creature behind rock or an outpost wall from the blast's
        /// centre is not hit (P5726, 7 Jun 2016: "Area of effect abilities on weapons should no longer pass through blocks
        /// and attract the attention of creatures on the other side of walls"; <see cref="Sight"/>).
        /// </summary>
        public static List<ulong> InSight(uint zone, Vector3 at, float radius)
        {
            List<ulong> all = InRadius(zone, at, radius);
            Vector3 from = at + new Vector3(0f, 1f, 0f);
            return all.Where(g => Find(g) is not Creature c || c.Cavern < 0 || Sight.Clear(zone, from, Eye(c))).ToList();
        }

        /// <summary>Where a creature sees from (and is seen at): 1.2 m above its feet.</summary>
        private static Vector3 Eye(Creature c) => c.Position + new Vector3(0f, 1.2f, 0f);

        // ---- Abomination spit as a projectile ----------------------------------------------------------------------------

        /// <summary>
        /// The Abomination's "poisonous lumps of goo" travel: wiki Monsters (PvE) — it alternates one projectile with a
        /// spreading volley of three, and strafing or terrain cover are the only ways to avoid them [P]. Speed and size
        /// are ProjectileDefinitions row 54
        /// (SPEED 25, RADIUS 1, COLLISIONEFFECT_ID 11625 = <see cref="AbominationSpitImpact"/>, bone Tongue_05) [P: matched
        /// by the impact effect]. The spread angle of the triple is ours [I]. Each lump flies as a Projectiles 0xEE shot
        /// (<see cref="Projectiles"/>, row 54: the client starts it at the creature's Tongue_05 bone when the creature is a
        /// full entity there, and draws the skull model and trail; tested 2026-10-17, L138 / L156); Delete lands it where
        /// the server resolved it. (PlayerUpdate LaunchProjectile 0x31 is an empty handler in both builds, L139.) The release
        /// and impact composites stay as before [I: whether Delete plays COLLISIONEFFECT_ID itself is open, L156].
        /// </summary>
        private const float SpitSpeed = 25f, SpitRadius = 1f, SpitSpreadDegrees = 12f, BodyHalfWidth = 0.5f;

        /// <summary>ProjectileDefinitions row of the Abomination's skull toss (model FX_Projectiles_…_Affliction_Skull_01).</summary>
        private const uint SpitProjectile = 54;

        /// <summary>A spit in flight: who threw it, at whom, from where to where, and when it lands.</summary>
        private sealed class Shot
        {
            public Creature From;
            public WorldSession Player;
            public Creature Foe;
            public Vector3 Origin, Aim;
            public DateTime Arrive;
            /// <summary>The client projectile drawing it (Projectiles 0xEE).</summary>
            public uint ProjectileId;
            /// <summary>The impact composite where it lands (the Abomination's spit, else the projectile row's own).</summary>
            public uint Impact = AbominationSpitImpact;
            /// <summary>The ability thrown (name and damage by rank; null = the plain spit).</summary>
            public Combat.CreatureAbilities.Ability Ability;
        }

        private static readonly List<Shot> shots = new();

        /// <summary>Draws a healer's radiance bolt from the creature to <paramref name="to"/> (Projectiles 0xEE, row 112).</summary>
        private static void ShowBolt(Creature c, Vector3 to, ulong target, List<WorldSession> viewers)
        {
            Vector3 from = Eye(c);
            uint id = Projectiles.NewId();
            Projectiles.Create(viewers, id, HealerBoltProjectile, from, to - from, c.Guid, to, target);
            int ms = (int)(Vector3.Distance(from, to) / HealerBoltSpeed * 1000f) + 50;
            _ = System.Threading.Tasks.Task.Delay(ms).ContinueWith(_ => Projectiles.Delete(viewers, id, to, true, target));
        }

        /// <summary>
        /// Projectile rows of the outpost archers and casters, by affinity (the 2016 affinity arrows and bolts, rows named by
        /// their trail effects; trace/0695/agent-l138-c1.md, OPEN-LEADS L157): speed and impact (COLLISIONEFFECT_ID, which
        /// the client doesn't play itself, L156) of each [P rows]. The creature's affinity comes from its name (Fireshot,
        /// Frostshot, Flamemage, Stormmancer, Lightbringer ...) [I].
        /// </summary>
        private static readonly Dictionary<uint, (float Speed, uint Impact)> creatureShots = new()
        {
            [85] = (80f, 11660), [86] = (80f, 11707), [87] = (80f, 11411), [88] = (80f, 11700), [89] = (80f, 11704),
            [90] = (80f, 11964), [91] = (80f, 11702), [92] = (80f, 11698),
            [94] = (40f, 11694), [95] = (40f, 11649), [96] = (40f, 11701), [97] = (40f, 10114), [98] = (40f, 11681),
            [100] = (40f, 11693), [109] = (40f, 11285)
        };

        /// <summary>The projectile row an outpost ranged creature shoots (0 = none), from its name [I].</summary>
        private static uint RangedProjectileFor(string name)
        {
            bool Has(params string[] words) => words.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase));
            bool archer = Has("Ranged", "Archer", "shot", "Bowman");
            bool caster = Has("Caster", "mancer", "Lightbringer") || (Has("mage") && !Has("Damage"));
            if (!archer && !caster)
                return 0;
            if (Has("Fire", "Flame")) return archer ? 87u : 94u;
            if (Has("Frost", "Cold", "Ice")) return archer ? 90u : 97u;
            if (Has("Storm", "Shock", "Lightning")) return archer ? 88u : 96u;
            if (Has("Shadow", "Dark")) return archer ? 89u : 98u;
            if (Has("Flash", "Light", "Radian")) return archer ? 91u : 95u;
            if (Has("Tox", "Plague", "Blight")) return archer ? 86u : 100u;
            return archer ? 85u : 109u;
        }

        /// <summary>
        /// An outpost archer's or caster's shot: one projectile at where the target is now, landing (hit or miss) when it
        /// arrives, like the Abomination's spit (P5727: outpost NPCs shoot; the caller checks sight).
        /// </summary>
        private static void Shoot(Creature c, Creature foe, WorldSession player, Vector3 at, DateTime now,
            Combat.CreatureAbilities.Ability ability)
        {
            uint row = c.Kind.Projectile;
            (float speed, uint impact) = creatureShots.TryGetValue(row, out var known) ? known : (40f, 0u);
            Vector3 origin = Eye(c), aim = at + new Vector3(0f, 1f, 0f);
            List<WorldSession> viewers = ViewersOf(c);
            ulong target = foe?.Guid ?? player?.PlayerGuid ?? 0;
            uint id = Projectiles.NewId();
            Projectiles.Create(viewers, id, row, origin, aim - origin, c.Guid, aim, target);
            lock (shots)
                shots.Add(new Shot
                {
                    From = c, Player = player, Foe = foe, Origin = origin, Aim = aim, Ability = ability, ProjectileId = id,
                    Impact = impact, Arrive = now + TimeSpan.FromSeconds(Vector3.Distance(origin, aim) / speed)
                });
        }

        /// <summary>Throws one spit (or, every other time, three spread ones) at where the target is now.</summary>
        private static void Spit(Creature c, Creature foe, WorldSession player, Vector3 at, DateTime now,
            Combat.CreatureAbilities.Ability ability = null)
        {
            c.Spits++;
            Vector3 origin = Eye(c), aim = at + new Vector3(0f, 1f, 0f);
            var aims = new List<Vector3> { aim };
            if (c.Spits % 2 == 0)
                foreach (float degrees in new[] { -SpitSpreadDegrees, SpitSpreadDegrees })
                    aims.Add(origin + Vector3.Transform(aim - origin, Quaternion.CreateFromAxisAngle(Vector3.UnitY, degrees * MathF.PI / 180f)));
            List<WorldSession> viewers = ViewersOf(c);
            ulong target = foe?.Guid ?? player?.PlayerGuid ?? 0;
            lock (shots)
                foreach (Vector3 a in aims)
                {
                    uint id = Projectiles.NewId();
                    Projectiles.Create(viewers, id, SpitProjectile, origin, a - origin, c.Guid, a, target);
                    shots.Add(new Shot
                    {
                        From = c, Player = player, Foe = foe, Origin = origin, Aim = a, Ability = ability, ProjectileId = id,
                        Arrive = now + TimeSpan.FromSeconds(Vector3.Distance(origin, a) / SpitSpeed)
                    });
                }
        }

        /// <summary>
        /// Lands the spits that have arrived: a hit when the target is still within the projectile's radius of the aim point
        /// (plus half a body) and no rock or ruin wall stood in the way (cavern creatures, <see cref="Sight"/>); a miss
        /// splashes where it was aimed.
        /// </summary>
        private static void ResolveShots(DateTime now)
        {
            List<Shot> due;
            lock (shots)
            {
                if (shots.Count == 0)
                    return;
                due = shots.Where(s => s.Arrive <= now).ToList();
                shots.RemoveAll(s => s.Arrive <= now);
            }
            foreach (Shot shot in due)
            {
                Creature c = shot.From;
                List<WorldSession> viewers = ViewersOf(c);
                bool blocked = c.Cavern >= 0 && !Sight.Clear(c.Zone, shot.Origin, shot.Aim);
                bool hit = false;
                if (!blocked && shot.Foe != null && Alive(shot.Foe))
                    hit = Vector3.Distance(shot.Foe.Position + new Vector3(0f, 1f, 0f), shot.Aim) <= SpitRadius + BodyHalfWidth;
                else if (!blocked && shot.Player != null && !Health.IsDead(shot.Player) && shot.Player.ZoneId == c.Zone
                    && PlayerManager.FindByGuid(shot.Player.PlayerGuid) == shot.Player)
                    hit = Vector3.Distance(shot.Player.Position + new Vector3(0f, 1f, 0f), shot.Aim) <= SpitRadius + BodyHalfWidth;
                Projectiles.Delete(viewers, shot.ProjectileId, shot.Aim, hit, hit ? shot.Foe?.Guid ?? shot.Player?.PlayerGuid ?? 0 : 0);
                if (!hit)
                {
                    HitEffects.PlayAt(shot.Aim, shot.Impact, viewers);
                    continue;
                }
                uint damage = shot.Ability == null ? c.Kind.Damage : (uint)MathF.Round(c.Kind.Damage * shot.Ability.DamageFactor);
                if (shot.Foe != null)
                    HurtCreature(c, shot.Foe, damage, shot.Impact);
                else
                {
                    HitEffects.PlayOn(shot.Player.PlayerGuid, shot.Impact, viewers);
                    HurtPlayer(c, shot.Player, damage, shot.Ability);
                }
            }
        }

        /// <summary>
        /// A cavern creature sees this player (P5727: outpost NPCs stop shooting through walls and ceilings; <see
        /// cref="Sight"/>). Surface creatures always do (no terrain test there).
        /// </summary>
        private static bool Sees(Creature c, Vector3 player)
            => c.Cavern < 0 || Sight.Clear(c.Zone, Eye(c), player + new Vector3(0f, 1.5f, 0f));

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

        /// <summary>
        /// The server check of one opponent a client Sweep targeter reported (FindTarget 0x27/0x24; WeaponCombat.Targeters,
        /// OPEN-LEADS L170): a living creature on this landscape, within <paramref name="range"/> m (flat) of
        /// <paramref name="from"/>, inside ±<paramref name="halfAngle"/>° of one of <paramref name="forwards"/> (or closer
        /// than 1.5 m), at most the cone's height off (<see cref="InCone"/>'s rule), and for a cavern creature in sight of
        /// the player's eye (<see cref="Sight"/>, as <see cref="InSight"/>).
        /// </summary>
        public static bool InSweep(uint zone, ulong guid, Vector3 from, IEnumerable<Vector3> forwards, float range, float halfAngle)
        {
            if (Find(guid) is not Creature c || !Alive(c) || c.Zone != zone)
                return false;
            if (!InSweepShape(from, c.Position, forwards, range, halfAngle))
                return false;
            return c.Cavern < 0 || Sight.Clear(zone, from + new Vector3(0f, 1.5f, 0f), Eye(c));
        }

        /// <summary>
        /// <see cref="InSweep"/>'s geometry: <paramref name="at"/> within <paramref name="range"/> m of <paramref name="from"/>
        /// on the flat, at most 4 m + 0.3 × range off in height, and inside ±<paramref name="halfAngle"/>° of one of the flat
        /// unit <paramref name="forwards"/> (or closer than 1.5 m, where any direction counts).
        /// </summary>
        internal static bool InSweepShape(Vector3 from, Vector3 at, IEnumerable<Vector3> forwards, float range, float halfAngle)
        {
            Vector3 to = new(at.X - from.X, 0f, at.Z - from.Z);
            float len = to.Length();
            if (len > range || MathF.Abs(at.Y - from.Y) > 4f + range * 0.3f)
                return false;
            float cos = MathF.Cos(halfAngle * MathF.PI / 180f);
            return len < 1.5f || forwards.Any(f => Vector3.Dot(to / len, f) >= cos);
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

        /// <summary>
        /// FullCharacterDataRequest for a creature: LightweightToFullNpc (0x26/0x5C) by its transient id, so the client turns
        /// the lightweight entity into a full one. False when the guid isn't one of ours.
        /// <para>
        /// The packet carries the creature's disposition (+0x90 → entity +0x147c, <see cref="DispositionOf"/>), which decides
        /// its nameplate colour and whether the client treats it as hostile (OPEN-LEADS L98, trace/0695/full-npc-695.md).
        /// The client asks once per entity, for the nearest ones (FUN_142ee8540: at most 50 full entities, 10 requests a
        /// frame), so this costs nothing extra: the value rides in a packet that is sent anyway.
        /// </para>
        /// </summary>
        public static bool SendFull(WorldSession session, ulong guid)
        {
            if (Find(guid) is not Creature c)
                return false;
            session.EnqueueMessage(new PlayerUpdateLightweightToFullNpc { TransientId = c.TransientId, Disposition = DispositionOf(c) });
            session.FullNpcs[guid] = c.TransientId;
            // [I] Weapon / NPC anim state once more after the full data: the selection only takes when the client's actor
            // has its animation network (FUN_142dfc010 asks the actor for it, vtable +0x468), which a just-spawned entity
            // may not have yet; the client asks for full data a frame or two later (FUN_142ee8540). Live re-sends 502 then
            // too (5Iewo: often two RequestAnimationSelection per guid).
            SendNpcAnimState(session, c);
            // [I] Its mood and stance once more for the same reason.
            SendPose(session, c);
            // Its weapon in hand (CreatureWeapons; on by default): only now, as the slot packet finds full entities only.
            if (WeaponShown(c) is CreatureWeapons.Weapon weapon)
                session.EnqueueMessage(CreatureWeapons.Message(c.Guid, weapon));
            return true;
        }

        /// <summary>
        /// The creature's weapon (dev override, else its kind's while CreatureWeapons is on: its creature prop's row, else its
        /// model's default); null = none.
        /// </summary>
        private static CreatureWeapons.Weapon WeaponOf(Creature c)
            => c.WeaponOverridden ? c.WeaponOverride : CreatureWeapons.For(c.Kind.Prop, c.Kind.Model);

        /// <summary>
        /// The weapon to show in the creature's hand: a story creature's only while its weapons are Equipped (Sheathed =
        /// the hand empty [I]: the Story Node's "Weapon Visuals" pair, live's look not recorded); others always.
        /// </summary>
        private static CreatureWeapons.Weapon WeaponShown(Creature c)
            => !c.Story || c.WeaponsEquipped ? WeaponOf(c) : null;

        /// <summary>
        /// Sends the creature's hand (its shown weapon, or empty) to every player that has it as a full entity
        /// (<see cref="CreatureWeapons"/>: the slot packet reaches full entities only; the others get it with their full
        /// data). Nothing when the creature has no weapon at all.
        /// </summary>
        private static void SendWeapon(Creature c, bool always = false)
        {
            if (!always && WeaponOf(c) == null)
                return;
            var message = CreatureWeapons.Message(c.Guid, WeaponShown(c));
            foreach (WorldSession s in ViewersOf(c))
                if (IsFullFor(s, c.Guid))
                    s.EnqueueMessage(message);
        }

        /// <summary>
        /// Dev "creatureweapon": puts <paramref name="model"/> (a Wield_*.adr; null / "" = none) in the creature's main hand,
        /// in place of its kind's weapon, and sends it to the players that have it as a full entity. Returns a line for the
        /// log, or null when there is no such creature.
        /// </summary>
        public static string DevWeapon(ulong guid, string model, string tint)
        {
            Creature c = Find(guid);
            if (c == null)
                return null;
            lock (sync)
            {
                c.WeaponOverridden = true;
                c.WeaponOverride = string.IsNullOrEmpty(model) ? null : new CreatureWeapons.Weapon(model, tint ?? "", "dev");
            }
            SendWeapon(c, always: true);
            int full = ViewersOf(c).Count(s => IsFullFor(s, c.Guid));
            return $"creature 0x{c.Guid:X} ({c.Kind.Name}, model {c.Kind.Model}): hand {(c.WeaponOverride?.Model ?? "empty")}"
                + $"{(WeaponShown(c) == null && c.WeaponOverride != null ? " (story creature Sheathed: shown when Equipped)" : "")}; sent to {full} full viewer(s)";
        }

        /// <summary>The nearest living creature to the player within <paramref name="radius"/> metres; 0 when none.</summary>
        public static ulong NearestGuid(WorldSession session, float radius)
        {
            lock (sync)
                return Nearby(session.ZoneId, session.Position, radius).Where(x => x.State != State.Dead)
                    .OrderBy(x => Vector3.Distance(x.Position, session.Position)).Select(x => x.Guid).FirstOrDefault();
        }

        /// <summary>
        /// A creature's disposition for the client (<see cref="NpcDisposition"/>): the story faction maps one to one (Enemy 0 →
        /// Hostile, Neutral 1 → Neutral, Ally 2 → Friendly); creatures that aren't story creatures (camps, caverns, the
        /// open world) are always hostile.
        /// </summary>
        private static NpcDisposition DispositionOf(Creature c) => !c.Story ? NpcDisposition.Hostile : c.Faction switch
        {
            FactionNeutral => NpcDisposition.Neutral,
            FactionAlly => NpcDisposition.Friendly,
            _ => NpcDisposition.Hostile
        };

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
            Health.EnteredCombat(attacker);
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
            // A Burn / Rot whose player has left still ticks, but as nobody's: no aggro on, combat text to, kill credit,
            // goals or loot bag for a session that is gone (bughunt-combat.md B8; lingering areas and Bleed skip them too).
            if (source != null && source.Disconnected)
                source = null;
            if (Find(guid) is Creature c && Alive(c) && c.Kind.Style != Behaviour.Exploder)
            {
                Health.EnteredCombat(source);
                Wound(source, c, amount);
            }
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
            bool died, engaged = false;
            lock (sync)
            {
                if (c.State == State.Dead || Shielded(c))
                    return;
                c.Health = amount >= c.Health ? 0 : c.Health - amount;
                c.LastHurt = DateTime.UtcNow;
                died = c.Health == 0;
                if (!died && attacker != null)
                    engaged = Provoked(c, attacker);
                if (!died)
                    NoteDamage(c, attacker, amount);
            }
            SendHitpoints(c, amount);
            if (attacker != null)
                SendCombatEvent(attacker, attacker.PlayerGuid, c.Guid, amount);
            if (died)
                Kill(c, attacker);
            else if (engaged && c.Story && c.State == State.Chase)
                StoryTools.CreatureCombat(c.Guid, true);
        }

        /// <summary>
        /// A player hurt a creature that wasn't fighting: it turns on them (or bolts, if harmless). Not on Combat Tactics
        /// "Do Nothing", not a Target Dummy (it only stands and swings), not an ally (allies fight for players, P4223);
        /// a neutral does (it fights only when provoked, wiki "Creature Prop"). Call inside the lock; true when it
        /// started a fight.
        /// </summary>
        private static bool Provoked(Creature c, WorldSession attacker)
        {
            if (c.State is State.Chase or State.Flee || c.Tactics == TacticsNothing || c.Kind.Style == Behaviour.Dummy
                || c.Faction == FactionAlly)
                return false;
            c.State = c.Kind.Damage > 0 ? State.Chase : State.Flee;
            c.Target = attacker;
            c.Foe = null;
            c.Anchor = AnchorOf(c);
            return true;
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
                // Shooting or hitting a Fiery Fungus sets it off (wiki: shoot them to set them off from a safe
                // distance). Same arming as approach: swell composite then boom; loot goes to the attacker.
                bool arm = false;
                lock (sync)
                    if (hit.FuseAt == default)
                    {
                        hit.Target = attacker;
                        arm = true;
                    }
                    else if (hit.FuseAt > DateTime.UtcNow)
                        hit.Target = attacker;
                if (arm)
                    ArmExploder(hit, attacker, attacker.Position, DateTime.UtcNow);
                SendCombatEvent(attacker, attacker.PlayerGuid, hit.Guid, damage);
                Health.HitConfirmed(attacker);
                HitEffects.Play(attacker, hit.Guid, ViewersOf(hit), ranged);
                return true;
            }
            // A shield block (Combat Node "Secondary Ability", Combat.CreatureAbilities.Role.Block): the blow lands (effect,
            // reaction, aggro) but deals nothing.
            uint dealt = Shielded(hit) ? 0u : (uint)(damage * (0.85 + random.NextDouble() * 0.3) * Combat.Statuses.TakenFactor(hit.Guid));
            bool died, engaged = false;
            lock (sync)
            {
                hit.Health = dealt >= hit.Health ? 0 : hit.Health - dealt;
                hit.LastHurt = DateTime.UtcNow;
                died = hit.Health == 0;
                if (!died)
                    engaged = Provoked(hit, attacker);
                if (!died)
                    NoteDamage(hit, attacker, dealt);
            }
            if (engaged && hit.Story && hit.State == State.Chase)
                StoryTools.CreatureCombat(hit.Guid, true);
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
            Combat.BlockStance.Forget(c.Guid);   // removed from its viewers below, with the stance
            lock (sync)
            {
                c.State = State.Dead;
                c.Target = null;
                c.RespawnAt = DateTime.UtcNow + RespawnDelay(c);
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
                // No player made the kill (another creature, a hazard's blast, a Damage Node): nothing drops (P4719).
                log.Info($"A {c.Kind.Name} died.");
                return;
            }
            if (c.Kind.Style == Behaviour.Dummy || c.Kind.Mine)
            {
                // A practice dummy (or a mine) is no monster: no loot, no kill goals (ours: "killed" in quotes, P5445).
                InstancedGames.CreatureKilled(killer);
                log.Info($"{killer.CharacterName} \"killed\" a {c.Kind.Name}.");
                return;
            }
            // The loot drops as a bag where the creature fell; the killer collects it by walking over it.
            var loot = RollLoot(c.Kind);
            // NPCs may drop campfires (P3583); the rate is ours.
            if (!c.Story && random.NextDouble() < CampfireChance)
                loot.Add((CozyCampfire, 1u));
            if (!c.Story && RareDrops.Beacon(random) is uint beacon and not 0)
                loot.Add((beacon, 1u));
            // Every activity that gives loot, harvesting or combat, may also give Lumens and recipes (21 Mar 2016,
            // P4169; recipes from fighting, P4166 / P4224), on the session curve (Pacing).
            // Rates ours, as gathering's.
            if (!c.Story)
            {
                if (random.NextDouble() < KillLumenChance * Pacing.Factor(killer))
                    loot.Add((Health.LumenItem, (uint)random.Next(1, 4)));
                if (random.NextDouble() < KillRecipeChance && RecipeBook.RandomUnknownRecipeItem(killer, random) is uint recipe and not 0)
                    loot.Add((recipe, 1u));
                // A Barren also rolls for one of the twelve "Recipe: Barren …" items (L44, user 2026-10-17: the non-elite
                // Barren props are unlocked by drops; more unlock through fighting, P4224): 1 % a kill, 30 % from a
                // Barren
                // Outpost Elite [I rates, BarrenRecipes].
                if (BarrenRecipes.RollFor(killer, c.Kind.Name, c.Boss && c.Cavern >= 0, random) is uint barren and not 0)
                    loot.Add((barren, 1u));
            }
            // An Outpost Elite drops "significantly more loot" (25 May 2016, P5628; recipes up, P5014): its kind's loot
            // three times over, a recipe at 30 %, essences, Adventure Essence and Lumens (amounts ours, like the Lost
            // Crystal's).
            if (c.Boss && c.Cavern >= 0)
            {
                for (int i = 0; i < 2; i++)
                    loot.AddRange(RollLoot(c.Kind));
                if (random.NextDouble() < 0.3 && RecipeBook.RandomUnknownRecipeItem(killer, random) is uint eliteRecipe and not 0)
                    loot.Add((eliteRecipe, 1u));
                loot.Add((MonsterEssence, (uint)random.Next(2, 5)));
                loot.Add((Gathering.AdventureEssence, (uint)random.Next(2, 5)));
                loot.Add((Health.LumenItem, (uint)random.Next(3, 8)));
            }
            // An Outpost elite (a boss in a Chaos Cavern): the rare Lop Ear Bunny figurine (P6143).
            if (c.Boss && c.Cavern >= 0 && random.NextDouble() < RareDrops.LopEarChance)
            {
                loot = loot.Append((Inventory.ItemDefinitionBuilder.LopEarBunnyFigurine, 1u)).ToList();
                log.Info($"{killer.CharacterName}: the Lop Ear Bunny dropped from {c.Kind.Name}.");
            }
            // Chaos Cavern monsters (the cavern group and the outpost garrison) drop a random resource, sometimes a rare one
            // (TASKS 6.16): one roll per kill, three for an Elite (its "significantly more loot", P5628).
            if (c.Cavern >= 0)
                for (int i = c.Boss ? 3 : 1; i > 0; i--)
                    loot.Add(CavernResource());
            // Essences on the play-session curve (26 Apr 2016, P4776, TASKS 6.17): fewer early in a session, more later.
            Pacing.PaceEssences(loot, killer, random);
            // A story creature with changed health / damage drops nothing (P6429, TASKS 2.14).
            if (c.StrengthChanged)
                loot.Clear();
            if (loot.Count > 0)
                LootPinatas.SpawnBag(killer, c.Position + new Vector3(0f, 0.5f, 0f), loot);
            if (Achievements.IsFungus(c.Kind.Name))
                FungusDiedNear(c, ViewersOf(c));
            else
            {
                Achievements.Killed(killer, c.Kind.Name);
                DailyGoals.Killed(killer, c.Kind.Name);
                // 13 Apr 2016 (P4559): combat dailies count for everyone fighting the creature (TASKS 6.55).
                // Everyone fighting = every player in its damage table (Threat: everyone who hurt it since it last calmed
                // down, the same table that picks its target, P3651), still connected and on its landscape; each counts
                // the kill with their own held weapon's affinity (DailyGoals.Killed). Only the dailies: the note names
                // no other goal, so achievements' kill objectives stay with the killer.
                foreach (WorldSession fighter in FightersOf(c, killer))
                    DailyGoals.Killed(fighter, c.Kind.Name);
            }
            InstancedGames.CreatureKilled(killer);
            log.Info($"{killer.CharacterName} killed a {c.Kind.Name}.");
        }

        /// <summary>
        /// The players besides <paramref name="killer"/> who fought a creature that just died: its damage table (cleared
        /// only when it calms down or respawns, so it still holds this fight), connected and on its landscape. Kill
        /// credit for P4559 (TASKS 6.55).
        /// </summary>
        private static List<WorldSession> FightersOf(Creature c, WorldSession killer)
        {
            List<WorldSession> fighters;
            lock (sync)
                fighters = c.Threat.Keys.ToList();
            return fighters.Where(p => p != null && p != killer && !p.Disconnected && p.ZoneId == c.Zone).ToList();
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
                // Back at its spawn point, facing its spawn heading (P4715), as the spawn packet shows it.
                c.Yaw = c.HomeYaw ?? c.Yaw;
                c.TransientId = PlayerManager.AllocateTransientId();
                c.Threat.Clear();
                DropPath(c);
                // A fuse lit while it went off (a revenge-armour hit on the exploding fungus re-armed it: Hit sets FuseAt
                // when it is clear) or while it died would otherwise blow it up on its first step back, with nobody near
                // and loot for whoever lit it (bughunt-combat.md B5).
                c.FuseAt = default;
                c.Dud = false;
                c.FungusAgro = false;
                c.Pending = null;
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
            if (DevCommands.NoStreaming || session.StreamsOnHold)
                return;
            // No baked heights for this landscape: surface creatures can't stand on its ground (its Chaos Caverns stand on
            // the client's cave pieces and are streamed all the same).
            bool heights = TerrainHeights.Has(zone);
            if (!heights && !Caves.HasCaverns(zone))
                return;
            Vector3 p = session.Position;
            var spawns = new List<Creature>();
            var removals = new List<ulong>();
            lock (sync)
            {
                StreamCaverns(session, zone, p, spawns, removals);
                if (heights)
                    StreamSurface(session, zone, p, spawns, removals);
            }
            foreach (ulong guid in removals)
                RemoveFor(session, guid);
            foreach (Creature c in spawns)
                SendSpawn(session, c);
        }

        /// <summary>
        /// The surface regions around the player and the story creatures on cave build sites (a landscape with baked
        /// heights). Call inside the lock.
        /// </summary>
        private static void StreamSurface(WorldSession session, uint zone, Vector3 p, List<Creature> spawns, List<ulong> removals)
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
                    spawns.AddRange(Region(zone, rx, rz).Where(c => c.State != State.Dead && !c.Deep));
                }
            foreach (long key in have.ToList())
            {
                (uint keyZone, int rx, int rz) = Unpack(key);
                if (!hideSurface && RegionDistance(rx, rz, p) <= DropRange)
                    continue;
                have.Remove(key);
                removals.AddRange(Region(keyZone, rx, rz).Where(c => c.State != State.Dead && !c.Deep).Select(c => c.Guid));
            }
            // Story creatures on cave build sites: shown by 3D distance, as the cavern groups.
            if (!deepStorySent.TryGetValue(session, out var deep))
                deepStorySent[session] = deep = new HashSet<ulong>();
            var here = regions.Values.SelectMany(r => r).Where(c => c.Story && c.Deep && c.Zone == zone).ToDictionary(c => c.Guid);
            foreach (ulong guid in deep.ToList())
                if (!here.TryGetValue(guid, out Creature c) || c.State == State.Dead || Vector3.Distance(c.Position, p) > CavernDropRange)
                {
                    deep.Remove(guid);
                    if (c != null && c.State != State.Dead)
                        removals.Add(guid);
                }
            foreach (Creature c in here.Values)
                if (c.State != State.Dead && Vector3.Distance(c.Position, p) <= CavernViewRange && deep.Add(c.Guid))
                    spawns.Add(c);
        }

        /// <summary>
        /// The Chaos Caverns of the player's landscape (every landscape with caves, F2): a cavern's group is shown by 3D
        /// distance, so players on the surface above don't get it; groups of another landscape are dropped. Call inside
        /// the lock.
        /// </summary>
        private static void StreamCaverns(WorldSession session, uint zone, Vector3 p, List<Creature> spawns, List<ulong> removals)
        {
            if (!cavernsSent.TryGetValue(session, out var caverns))
                cavernsSent[session] = caverns = new HashSet<int>();
            foreach (int i in caverns.Where(i => Caves.ZoneOf(i) != zone).ToList())
            {
                caverns.Remove(i);
                removals.AddRange(CavernGroup(i).Where(c => c.State != State.Dead).Select(c => c.Guid));
            }
            foreach (int i in Caves.In(zone))
            {
                float d = Vector3.Distance(CavernFloor(i), p);
                if (d <= CavernViewRange && caverns.Add(i))
                    spawns.AddRange(CavernGroup(i).Where(c => c.State != State.Dead));
                else if (d > CavernDropRange && caverns.Remove(i))
                    removals.AddRange(CavernGroup(i).Where(c => c.State != State.Dead).Select(c => c.Guid));
            }
        }

        /// <summary>
        /// A Fiery Fungus or a Proximity Mine (<see cref="Behaviour.Exploder"/>): still until something it would go off for
        /// comes within <see cref="FuseRange"/>; then it arms — fungus: <see cref="FungusExplosion"/> (buildup for
        /// <see cref="FuseTime"/> then boom, plus WeaponIdle 1 for mesh swell); mine: warning — and after the fuse
        /// detonates: everyone in <see cref="BlastRadius"/> it would hurt takes its damage (a mine also knocks them down:
        /// Crush), it drops its loot for the player who set it off (fungi only) and is gone a moment later. A hit arms
        /// the same way (<see cref="Hit"/>).
        ///
        /// Factions (story fungi and mines: "the same faction and respawn settings as other non-pathing creatures like
        /// fiery fungi", P5269; P4223 for what each faction does): world fungi and Enemy ones go off for players and for
        /// ally creatures, Ally ones for enemy creatures only and never hurt players, Neutral ones for nobody (they still
        /// go off when hit, and that blast hurts players). Combat Tactics "Do Nothing" makes one decoration (never goes
        /// off by itself). A story one that went off is a death for its spawner ("dies" Links, its respawn time), the
        /// player who set it off as the triggering player.
        /// </summary>
        private static void ThinkFungus(Creature c, List<WorldSession> players, DateTime now)
        {
            if (c.FuseAt == default)
            {
                if (c.Tactics == TacticsNothing)
                    return;
                WorldSession near = c.Faction != FactionEnemy ? null
                    : players.FirstOrDefault(p => p.ZoneId == c.Zone && !Health.IsDead(p) && Dist(c, p.Position) <= FuseRange);
                Vector3 at;
                if (near != null)
                    at = near.Position;
                else if (NearestFoe(c, FuseRange) is Creature foe)
                    at = foe.Position;
                else
                {
                    // Start Stand_Break → AttackIdle before the fuse so swell is on when 10918 / damage fire.
                    if (!c.Kind.Mine && !c.FungusAgro && c.Faction == FactionEnemy)
                    {
                        WorldSession swell = players.FirstOrDefault(p =>
                            p.ZoneId == c.Zone && !Health.IsDead(p) && Dist(c, p.Position) <= FungusSwellRange);
                        if (swell != null)
                        {
                            c.FungusAgro = true;
                            foreach (WorldSession s in ViewersOf(c))
                                if (!s.FullNpcs.ContainsKey(c.Guid))
                                    SendFull(s, c.Guid);
                            PlayFungusInflate(c, swell.Position);
                        }
                    }
                    return;
                }
                c.Target = near;
                ArmExploder(c, near, at, now);
                return;
            }
            if (now < c.FuseAt)
                return;
            c.FuseAt = default;
            // A Combat Node's "Grow-splode Dud" (Combat.CreatureAbilities.Role.Fizzle): it goes off, but harms nobody [I].
            bool dud = c.Dud;
            c.Dud = false;
            List<WorldSession> viewers = ViewersOf(c);
            // Mines: blast composite here. Fungus mesh Explode/Dud was started at arm (with 10918); damage/loot only here.
            if (c.Kind.Mine)
                HitEffects.PlayOn(c.Guid, MineExplosion, viewers);
            if (c.Faction != FactionAlly && !dud)
                foreach (WorldSession p in players.Where(p => p.ZoneId == c.Zone && !Health.IsDead(p) && Dist(c, p.Position) <= BlastRadius))
                {
                    if (!c.Kind.Mine)
                        HitEffects.PlayOn(p.PlayerGuid, FungusImpact, viewers);
                    HurtPlayer(c, p, c.Kind.Damage);
                    if (c.Kind.Mine && !Health.IsDead(p))
                        Combat.Statuses.Apply(p.PlayerGuid, Combat.Status.Crush, null, viewers: viewers);
                }
            // A story fungus / mine on a site in Play Mode damages its terrain (2.15) when its spawner allows it.
            if (c.Story && c.DamagesTerrain && !dud)
                VoxelDestruction.CreatureBlast(c.Zone, c.Position);
            if (c.Story && !dud)
            {
                // Creatures of the other side caught in the blast (6.20): hurt as by any creature, no killer.
                List<Creature> caught;
                lock (sync)
                    caught = Nearby(c.Zone, c.Position, BlastRadius)
                        .Where(x => Hostile(c, x) && MathF.Abs(x.Position.Y - c.Position.Y) <= BlastRadius).ToList();
                foreach (Creature x in caught)
                {
                    HurtCreature(c, x, c.Kind.Damage, c.Kind.Mine ? 0 : FungusImpact);
                    if (c.Kind.Mine && Alive(x))
                        Combat.Statuses.Apply(x.Guid, Combat.Status.Crush, null, viewers: ViewersOf(x));
                }
            }
            WorldSession trigger = c.Target;
            if (!c.Kind.Mine)
                FungusDiedNear(c, viewers);
            lock (sync)
            {
                c.State = State.Dead;
                c.Target = null;
                c.RespawnAt = now + RespawnDelay(c);
                c.RemoveAt = now + BlastLinger;
            }
            bool byPlayer = trigger != null && players.Contains(trigger);
            // The blast's destroyed-terrain loot (P4533) is the one drop of the 0.695 era no player earned: it spawns
            // ownerless for everyone near, and the first to reach it takes it (LootPinatas.SpawnOwnerless; the rule is
            // inferred from the client and patch notes, trace/0695/agent-loot-rule.md, OPEN-LEADS L141).
            if (byPlayer && !c.Kind.Mine && !c.StrengthChanged)
                LootPinatas.SpawnOwnerless(viewers, c.Position + new Vector3(0f, 0.5f, 0f), ExplodeLoot(c, trigger));
            if (c.Story)
                StoryTools.CreatureDied(c.Guid, byPlayer ? trigger : null);
            log.Debug($"A {c.Kind.Name} exploded at {c.Position}.");
        }

        /// <summary>
        /// Lights an exploder's fuse and starts its arming visuals. Fiery Fungus: <see cref="FungusExplosion"/> (or
        /// <see cref="FungusDud"/>) for particles, plus MESSAGE Release (<see cref="AnimFungusExplode"/> /
        /// <see cref="AnimFungusDud"/>) at arm so Explode_01 / Dud_01 grow-then-pop runs during the fuse with 10918 —
        /// not after loot (FINDINGS.md). Approach pre-swell may already have started Agro; Release leaves Stand for
        /// Explode. Mines: warning only; blast on detonate.
        /// </summary>
        private static void ArmExploder(Creature c, WorldSession trigger, Vector3 face, DateTime now)
        {
            bool dud = c.Dud;
            // Fuse before Full: SendFull → SendNpcAnimState must send Explode/Dud (not idle 353).
            c.FuseAt = now + (dud && !c.Kind.Mine ? DudFuseTime : FuseTime);
            if (trigger != null)
                c.Target = trigger;
            List<WorldSession> viewers = ViewersOf(c);
            if (c.Kind.Mine)
                HitEffects.PlayOn(c.Guid, MineWarning, viewers);
            else
            {
                // Buildup particles (11188–11191, 11112) attach to FieryFungi bones (Top, B, D1, G, Spine05). On a
                // lightweight-only entity those bones are missing → glow/light only, no expanding cloud. Full data first.
                foreach (WorldSession s in viewers)
                    if (!s.FullNpcs.ContainsKey(c.Guid))
                        SendFull(s, c.Guid);
                uint fx = dud ? FungusDud : FungusExplosion;
                HitEffects.PlayOn(c.Guid, fx, viewers);
                HitEffects.PlayAt(c.Position + new Vector3(0f, 0.8f, 0f), fx, viewers);
                PlayFungusBurst(c, face, dud);
                c.FungusAgro = true;
                // Full attach is async; re-assert Release after the network exists (live often doubles selections around Full).
                ulong guid = c.Guid;
                System.Threading.Tasks.Task.Delay(300).ContinueWith(_ =>
                {
                    if (Find(guid) is not Creature still || still.FuseAt == default || still.State == State.Dead)
                        return;
                    PlaySelection(still, still.Dud ? AnimFungusDud : AnimFungusExplode);
                });
            }
        }

        /// <summary>
        /// A Fiery Fungus died (exploded or killed): "You Can Kill Those Things" counts for every living player near it.
        /// "Near" isn't given by any source: ours, the players who can see it.
        /// </summary>
        private static void FungusDiedNear(Creature c, List<WorldSession> viewers)
        {
            foreach (WorldSession p in viewers)
                if (!Health.IsDead(p))
                    Achievements.FungusDied(p);
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
            var rng = new Random(Shared.StableHash.Combine(rx * 15485863, rz * 32452843, 0x6b6f ^ (int)(zone == Home ? 0 : zone)));
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
                    if (!Claims.MayEdit(0, global.X, global.Y, global.Z, zone) || Graveyards.PreventsSpawning(zone, new Vector3(x, y, z)))
                        continue;
                    Kind kind = FieryFungus;
                    var home = new Vector3(x, y, z);
                    list.Add(new Creature
                    {
                        Guid = GuidBase | ((ulong)key << 8) | (uint)i,
                        RegionKey = key,
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
            Name = c.DisplayName ?? c.Kind.Name,
            ModelId = c.Kind.Model,
            Scale = new Vector4(1f, 1f, 1f, 1f),
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
            // NPC anim network state (WeaponIdle / NPC flag): live sends RequestAnimationSelection 502 on almost every
            // combat NPC at spawn (5IewoKPQE2BqKg6Q: 102× 502, 3× 353). Story Equipped still uses 352 (adds Class 1).
            SendNpcAnimState(session, c);
            // A shield block still up: the newcomer's sweeps stop on it too (Combat.BlockStance). The Delta's lookup
            // (FUN_142f85390) is the guid table every character is in; the full data later upgrades the same entity [I].
            Combat.BlockStance.Introduce(session, c.Guid);
            // Its mood and stance the same way (actor variables too; Calm / Standing are the defaults and need nothing).
            SendPose(session, c);
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
            if (c.Story && Interactable(c))
            {
                var flagging = new Network.Message.Model.DynamicInteractionFlagging();
                flagging.Guids.Add(c.Guid);
                session.EnqueueMessage(flagging);
            }
        }

        /// <summary>
        /// A story creature shows the interact prompt only with a "When a Luminary clicks this creature..." Link and when
        /// it isn't an enemy (11 May 2016, P5304: only creatures with an active interaction Link show it; P5305:
        /// enemy-faction creatures can't be interacted with).
        /// </summary>
        private static bool Interactable(Creature c) => c.Story && c.Faction != 0 && StoryTools.CreatureHasClickLink(c.Guid);

        /// <summary>Whether a story creature can be clicked now (<see cref="Interactable(Creature)"/>).</summary>
        public static bool IsInteractable(ulong guid) => FindStory(guid) is Creature c && Interactable(c);

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
        /// <param name="abilityName">
        /// Text id of the ability that hit, the combat spam's [*Ability*] ("#Owns([*SourcePlayer*]) [*Ability*] hit YOU for
        /// [*Value*] damage.", CombatSpam row 1): the body u32 at +0x40 of the client's record (reader FUN_142c278c0), which
        /// the damage path FUN_14308e060 → FUN_14308de50 → FUN_142fe2240 hands to the spam builder FUN_142fe2840 as the name
        /// (Combat.CreatureAbilities). 0 = no name.
        /// </param>
        public static void SendCombatEvent(WorldSession session, ulong source, ulong target, uint damage, uint abilityName = 0)
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
            writer.Write(abilityName);
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

        /// <summary>
        /// AnimationSelection ids used on creatures:
        /// <list type="bullet">
        /// <item>4 "Ouch" — hit reaction MESSAGE.</item>
        /// <item>353 — fungus spawn idle (WeaponIdle 0); live 5nepi fungus guids. Same row as sheathed bipeds.</item>
        /// <item>356–358 Enter + 360–362 Release — NPC Class 1 / WeaponIdle melee (AttackIndex 0–2). An Enter alone leaves
        /// the actor in the wind-up pose (WeaponAttacks / Emotes.ShowSwing); Release must follow. Live biped combat is
        /// AbilityEffectStart-driven (5Iewo); our homemade path uses these pairs. Fungus arm reuses 360/361 (Release +
        /// AttackIndex 0/1) so Explode/Dud mesh runs during the fuse with 10918 — not at detonate after loot.</item>
        /// <item>502 — biped spawn weapon-ready (WeaponIdle 1); fungus pre-swell uses it for Agro Sequence1 on approach.</item>
        /// </list>
        /// </summary>
        private const uint AnimOuch = 4, AnimNpcReady = 502;
        /// <summary>Fiery Fungus MRN path (FINDINGS.md): spawn Stand, approach Agro, arm Explode/Dud Release.</summary>
        private const uint AnimFungusIdle = 353, AnimFungusArm = 502, AnimFungusExplode = 360, AnimFungusDud = 361;
        /// <summary>PlayBackSpeed 2.5 — rushes Agro Sequence1 Breaks on approach pre-swell.</summary>
        private const uint AnimFungusPlayFast = 838;
        /// <summary>NPC Class 1 Enter/Release by AttackIndex 0, 1, 2 (see AnimationSelectionVariables).</summary>
        private static readonly (uint Enter, uint Release)[] NpcMeleeSwings =
        {
            (357, 360), (356, 361), (358, 362)
        };
        private const int NpcMeleeReleaseMs = 350;

        /// <summary>
        /// An NPC's weapon visuals (Story Node row 41 "Weapon Visuals" 577234: Equipped 577235 / Sheathed 577236; spawner
        /// row 40 "Spawn With Weapons Equipped" 577229; Links 577074 / 577075 "...Have this creature equip / unequip their
        /// weapon." and 577237 "...Set this creature's weapon visuals."): the only two AnimationSelection rows that set the
        /// network's <c>NPC</c> flag apart from 502, and a pair (AnimationSelectionVariables, same in 0.589 and 0.695):
        /// <list type="bullet">
        /// <item>352 (MESSAGE "WeaponEnd", a request the player network has): WeaponIdle 1, Class 1, NPC 1 — Equipped.
        /// Class 1 with WeaponIdle 1 is what the NPC attack selections 356-359 / 364 / 781 set too.</item>
        /// <item>353 (no message): WeaponIdle 0, Class 0, NPC 1 — Sheathed, the variables' resting values.</item>
        /// <item>502 (no message): WeaponIdle 1, NPC 1, OnGround 0 — live spawn default for combat NPCs (see above).</item>
        /// </list>
        /// [P] the client side: AbilityPlayAnim's handler (0.695 FUN_142e1b9d0) plays the selection on the entity
        /// (FUN_142dfc010 → AnimationSelectionManagerBase::PlayAnim FUN_143d95d40, which sets each of the row's variables
        /// on the actor, then sends its message); in PlayerAnimationNetwork64.mrn the switch StandPoses|Idle_Stand_2 (node
        /// 10167) reads ControlParameters|WeaponIdle and picks Stand (0) or Weapon1 (1), the weapon-ready idle (decoded
        /// with the NodeDef layout of trace/systems/social.md). The same variable is what a held weapon's actor sets on a
        /// player (Wield_*.adr AnimationEnumeration hash −1452685539 = one-at-a-time "WeaponIdle" = 1).
        /// [I, strong] that live's Equipped / Sheathed were exactly these two rows (no client code names them; they are the
        /// only NPC weapon pair). The weapon model itself is not sent: OPEN-LEADS L95. Two carriers would attach one to a
        /// creature [P]: the full NPC data's attachment list (LightweightToFullNpc +0x28, CharacterAttachmentData reader
        /// FUN_142c185c0 = model / texture alias / tint alias / decal strings, u32 tint, u32 composite effect, u32 slot,
        /// u32, bool; applied by FUN_142e9a050) and EquipmentSetCharacterEquipmentSlot (0xCB, handler FUN_142e42da0: the
        /// entity by guid through FUN_142cd1cb0 → FUN_142f85390, the table full NPCs are in, profile id must be set);
        /// both end in the per-slot attach FUN_142ebd620. What the client does not have is which weapon each creature
        /// held: the creature actors carry none (PCNPC / Skeleton / Slaug / Kobold .adr: empty EquipType and
        /// ChildAttachSlotsEx, no Wield_* reference), and no table ties a creature or its model to a Wield_*.adr
        /// (ArtSourceActors lists the weapon actors only; LootPinata's Wield_* rows are dropped items). Live's per-creature
        /// weapon was server data (the wiki's creature pages name some: Barren Flameblade "Uses a flaming greatsword").
        /// </summary>
        public const uint AnimNpcWeaponsEquipped = 352, AnimNpcWeaponsSheathed = 353;

        /// <summary>
        /// Puts the creature's actor into the live NPC anim baseline for one viewer: story Equipped → 352, else 502;
        /// Fiery Fungus → 353 idle, 502 if approach-agro'd, or 360/361 while the fuse is lit (FINDINGS.md); mines skip.
        /// RequestAnimationSelection so the client queues until the actor is ready (5Iewo; AbilityPlayAnim was never used
        /// for this). Called from <see cref="SendSpawn"/> and again from <see cref="SendFull"/> — on arm, Full must not
        /// re-send idle 353 over the Explode Release.
        /// </summary>
        private static void SendNpcAnimState(WorldSession session, Creature c)
        {
            if (c.Kind.Style == Behaviour.Exploder)
            {
                if (c.Kind.Mine)
                    return;
                uint fungus = c.FuseAt != default
                    ? (c.Dud ? AnimFungusDud : AnimFungusExplode)
                    : (c.FungusAgro ? AnimFungusArm : AnimFungusIdle);
                session.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateRequestAnimationSelectionWire,
                    Emotes.SelectionBody(c.Guid, fungus, clearSit: false));
                return;
            }
            uint selection = c.Story && c.WeaponsEquipped ? AnimNpcWeaponsEquipped : AnimNpcReady;
            session.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateRequestAnimationSelectionWire,
                Emotes.SelectionBody(c.Guid, selection, clearSit: false));
        }

        /// <summary>
        /// Faces <paramref name="towards"/> on the server (yaw + movement) so observers see the turn without relying on
        /// AbilityPlayAnim's heading rewrite.
        /// </summary>
        private static void FaceTowards(Creature c, Vector3 towards)
        {
            if (c.Kind.Style == Behaviour.Siege)
                return;
            float dx = towards.X - c.Position.X, dz = towards.Z - c.Position.Z;
            if (dx * dx + dz * dz > 0.25f)
            {
                c.Yaw = MathF.Atan2(dx, dz);
                SendMovement(c);
            }
        }

        /// <summary>
        /// Homemade NPC melee: Class 1 Enter then its Release (Emotes.ShowSwing / WeaponAttacks). Cycles AttackIndex 0–2.
        /// </summary>
        private static void PlayNpcSwing(Creature c, Vector3 towards)
        {
            FaceTowards(c, towards);
            (uint enter, uint release) = NpcMeleeSwings[c.AttackCombo % NpcMeleeSwings.Length];
            c.AttackCombo = (byte)((c.AttackCombo + 1) % NpcMeleeSwings.Length);
            PlaySelection(c, enter);
            ulong guid = c.Guid;
            System.Threading.Tasks.Task.Delay(NpcMeleeReleaseMs).ContinueWith(_ =>
            {
                if (Find(guid) is not Creature still || still.State == State.Dead)
                    return;
                PlaySelection(still, release);
            });
        }

        /// <summary>
        /// Fiery Fungus approach Agro: WeaponIdle 1 → Stand Switch1 → Sequence1 (Stand_Break → AttackIdle), sped up.
        /// The fuse's mesh grow-then-pop is <see cref="PlayFungusBurst"/> (Release → Explode/Dud), not this path.
        /// </summary>
        private static void PlayFungusInflate(Creature c, Vector3 towards)
        {
            FaceTowards(c, towards);
            PlaySelection(c, AnimFungusArm);
            PlaySelection(c, AnimFungusPlayFast);
        }

        /// <summary>
        /// Fiery Fungus arm burst: MESSAGE Release + AttackIndex 0/1 → Explode_01 / Dud_01 (grow-then-pop during fuse).
        /// </summary>
        private static void PlayFungusBurst(Creature c, Vector3 towards, bool dud)
        {
            FaceTowards(c, towards);
            PlaySelection(c, dud ? AnimFungusDud : AnimFungusExplode);
        }

        /// <summary>RequestAnimationSelection for one selection (no facing); used by swings, Ouch, story poses.</summary>
        private static void PlaySelection(Creature c, uint animation)
        {
            byte[] body = Emotes.SelectionBody(c.Guid, animation, clearSit: false);
            foreach (WorldSession s in ViewersOf(c))
                s.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateRequestAnimationSelectionWire, body);
        }

        /// <summary>
        /// Plays an AnimationSelection on a creature for its viewers. Uses
        /// <see cref="ZoneMessageOpcode.PlayerUpdateRequestAnimationSelectionWire"/> (0x26/0x65), not AbilityPlayAnim:
        /// the latter always turns the actor toward a point and drops the play when the client actor is not ready yet
        /// (FUN_142dfc010 fails with no queue). RequestAnimationSelection plays the same selection (FUN_142dfc010) and
        /// queues until the actor is ready (case 100 → FUN_142e977b0) — the path player emotes use (Emotes.Play,
        /// docs/protocol.md). Facing is applied on the server (yaw + movement) so observers still see the creature turn.
        /// </summary>
        private static void PlayAnim(Creature c, uint animation, Vector3 towards)
        {
            if (c.Kind.Style == Behaviour.Siege)
                towards = Ahead(c);
            else
                FaceTowards(c, towards);
            PlaySelection(c, animation);
        }

        /// <summary>The AbilityPlayAnim body for one creature (see <see cref="PlayAnim"/>).</summary>
        private static byte[] PlayAnimBody(Creature c, uint animation, Vector3 towards)
        {
            // A siege engine faces one way only (P5728): whatever it plays (fire, Ouch), it faces ahead.
            if (c.Kind.Style == Behaviour.Siege)
                towards = Ahead(c);
            return AnimationBody(c.Guid, animation, towards);
        }

        /// <summary>
        /// An AbilityPlayAnim body (0x27 sub 0x12; handler FUN_142e1b9d0) for any proxied entity, creature or prop (the
        /// client finds both by guid, FUN_142f85390): it turns the entity to face <paramref name="towards"/> (heading
        /// only, FUN_142ec4e60) and plays the AnimationSelection (FUN_142dfc010).
        /// </summary>
        public static byte[] AnimationBody(ulong guid, uint animation, Vector3 towards)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            // Ability header after the opcode (FUN_142d65900): u16 sub again, u8 flag, u32 time.
            writer.Write((byte)0x12);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write(0u);
            writer.Write(guid);
            writer.Write(animation);
            writer.Write(towards.X);
            writer.Write(towards.Y);
            writer.Write(towards.Z);
            writer.Write(1f);
            return output.ToArray();
        }

        /// <summary>
        /// Position 0x7F with a full movement body (type 0, server clock, version 1; position in the remote frame,
        /// i.e. local + zone origin / 10, rotation, zero velocity and extras, then the flag tail every client sends).
        /// </summary>
        private static void SendMovement(Creature c)
        {
            byte[] movement = MovementBody(c.Position, c.Yaw, c.Velocity);
            foreach (WorldSession s in ViewersOf(c))
                Network.MovementFanout.Queue(s, c.TransientId, movement);
        }

        /// <summary>A full type-0 movement body for a position, heading and velocity (see <see cref="SendMovement"/>).</summary>
        public static byte[] MovementBody(Vector3 position, float yaw, Vector3 velocity)
            => MovementBody(position, new Quaternion(0f, MathF.Sin(yaw / 2), 0f, MathF.Cos(yaw / 2)), velocity);

        /// <summary>
        /// A full type-0 movement body with a whole rotation (x, y, z, w at precision 5, the order the yaw form fills):
        /// for entities that turn about any axis — a moving platform turned by its docks (Props.Platforms, P1537).
        /// </summary>
        public static byte[] MovementBody(Vector3 position, Quaternion rotation, Vector3 velocity)
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
            Put(rotation.X, 5);
            Put(rotation.Y, 5);
            Put(rotation.Z, 5);
            Put(rotation.W, 5);
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

        /// <summary>Largest height change one client report may make (metres): a ground snap, not a teleport.</summary>
        private const float MaxHeightCorrection = 12f;

        /// <summary>A client's report counts only for creatures within this distance of the reporter (its query radius is smaller).</summary>
        private const float HeightReportRange = 120f;

        /// <summary>
        /// PacketCorrectNpcHeights from a client (0x114). The local player runs a proximity query for NPC actors (entity
        /// type 0x10) around itself (FUN_14332cf10: physics query, listener at player +0x4b50); for each NPC whose
        /// movement component flags a pending height correction (component +0x278, vtable +0x30) and that rides nothing,
        /// the result callback FUN_1433278c0 sends the NPC's current client-side height (its transform +0x34 = y), i.e.
        /// where the client's own ground snap put it. Live used it to learn heights the server didn't know (caves,
        /// edited terrain; P5669 fixed outpost NPCs spawning inside the terrain). We adopt the height for creatures the
        /// reporter can see, near it, and only within <see cref="MaxHeightCorrection"/>; nothing is sent back (the next
        /// movement update carries it, and other viewers' clients snap the same way). Ours: the limits.
        /// </summary>
        public static void CorrectHeights(WorldSession session, List<ulong> guids, List<float> heights)
        {
            for (int i = 0; i < guids.Count && i < heights.Count; i++)
            {
                Creature c = Find(guids[i]);
                float y = heights[i];
                if (c == null || c.State == State.Dead || c.Zone != session.ZoneId || !float.IsFinite(y))
                    continue;
                float dy = y - c.Position.Y;
                bool take = Math.Abs(dy) <= MaxHeightCorrection && Math.Abs(dy) > 0.05f
                    && Vector3.Distance(c.Position, session.Position) <= HeightReportRange && ViewersOf(c).Contains(session);
                log.Debug($"NPC height from {session.CharacterName}: 0x{c.Guid:X} {c.Kind.Name} ours {c.Position.Y:F2}, client {y:F2}"
                    + (take ? " (taken)" : ""));
                if (!take)
                    continue;
                lock (sync)
                    c.Position = c.Position with { Y = y };
            }
        }

        /// <summary>
        /// Elites (bosses) and very large creatures (Giants / Ogres) shrug off crowd control: hotfix 11 May 2016,
        /// P5266: elites and very large creatures are immune to stun, root, freeze, snare and silence (burn), while the
        /// attacks' effects still play. The statuses still land (their visuals and damage over time run);
        /// only their hold on the creature is ignored. "Very large" = the Ogre kind (ours: the only giant body here).
        /// </summary>
        /// The Highfall Catapult is an Elite (wiki "Highfall Catapult": "Elite: Yes") and a war machine, so it shrugs them
        /// off too.
        private static bool IgnoresControl(Creature c) => c.Boss || c.Kind == Ogre || c.Kind.Style == Behaviour.Siege;

        // ---- Chaos Cavern resources (TASKS 6.16) ------------------------------------------------------------------------
        // Chaos Cavern enemies drop random resources (1 Apr 2016, P4353), and their rare resource rate was retuned (13
        // Apr 2016, P4536) [P]. Which resources: the underground ones of the
        // wiki "Resources" table (Location "Underground" or "Surface and Underground") [P]: Iron 36, Silver 37, Gold 39,
        // Obsidian 49, Alabaster 50 (uncommon metals / stone) and the gemstones Ruby 58, Emerald 56, Diamond 59, taken as
        // the "rare resources" [I: the notes don't name them; gems are the scarcest underground finds]. Common Stone / Dirt
        // are left out (the fungus blast already drops them). Amounts and rates are ours (live's were server data,
        // OPEN-LEADS L120).

        /// <summary>The uncommon underground resources a cavern monster may drop (1-3 of one).</summary>
        private static readonly uint[] CavernUncommon = { 36, 37, 39, 49, 50 };
        /// <summary>The rare ones (1 of one, at <see cref="CavernRareChance"/>).</summary>
        private static readonly uint[] CavernRare = { 56, 58, 59 };
        /// <summary>Chance a cavern resource roll gives a gemstone instead (ours).</summary>
        private const double CavernRareChance = 0.08;

        /// <summary>One random cavern resource reward: a gemstone at <see cref="CavernRareChance"/>, else 1-3 of an uncommon one.</summary>
        private static (uint, uint) CavernResource()
            => random.NextDouble() < CavernRareChance
                ? (CavernRare[random.Next(CavernRare.Length)], 1u)
                : (CavernUncommon[random.Next(CavernUncommon.Length)], (uint)random.Next(1, 4));

        // ---- Healers (TASKS 6.21) ---------------------------------------------------------------------------------------
        // P4696 (26 Apr 2016): healers with nobody to heal chase enemies who leave their range instead of standing
        // still [P]: live had creature healers that healed and, between heals, attacked. Which
        // creatures: the 0.695 prop dump's radiance minions whose names are healer roles — 2465 / 2777 Scalewind Shaman,
        // 2521 / 2817 Ebonguard Priestess, 2606 / 2850 Stoneskin Riftmender ("landmark_v2: spawner: …: minion: scalewind
        // shaman: radiance"; the wiki "Affinity" page: Radiance is the "healer/support" archetype) [I: the role is read from
        // the name; live's NPC ability sets were server data, OPEN-LEADS L19 / L120]. The heal: <see cref="HealShare"/> of
        // the patient's life, every <see cref="HealInterval"/>, to the most hurt creature of its side within
        // <see cref="HealRange"/> (itself included) below <see cref="HealBelow"/> of its life — all numbers ours. Visuals:
        // composite 12572 PFX_Combat_Radiance_Heal_Release_01 on the healer, 12571 PFX_Combat_Radiance_Heal_Impact_01 on
        // the patient [P that they exist, ActorCompositeEffectDefinitions; I that live's NPC heal used them]. Its attack is
        // a radiance bolt: 11574 PFX_Combat_Radiance_Wand_Ability01_Release / 11649 PFX_Combat_Misc_Impact_Radiance_NonDirectional
        // (ours, the radiance wand's).

        private const uint HealRelease = 12572, HealImpact = 12571, HealerBoltRelease = 11574, HealerBoltImpact = 11649;

        /// <summary>
        /// The healer's radiance bolt in flight: ProjectileDefinitions row 112 (model Radiant_01_Small, trail 11662
        /// PFX_Combat_Radiance_Wand_Ability01_Projectile_03, collision 11649 = <see cref="HealerBoltImpact"/>, 120 m/s,
        /// bone R_Weapon) — the same Radiance Wand Ability01 family as <see cref="HealerBoltRelease"/> [P: matched by both
        /// effects]. Visual only: at 120 m/s it arrives within a frame or two of the hit.
        /// </summary>
        private const uint HealerBoltProjectile = 112;
        private const float HealerBoltSpeed = 120f;
        private const float HealRange = 15f, HealBelow = 0.75f, HealShare = 0.15f;
        private static readonly TimeSpan HealInterval = TimeSpan.FromSeconds(4);

        /// <summary>Is <paramref name="x"/> on healer <paramref name="c"/>'s side? World / cavern creatures: every other
        /// world creature of the same cavern (or the surface); story creatures: the same faction.</summary>
        private static bool SameSide(Creature c, Creature x)
            => x.State != State.Dead && x.Zone == c.Zone && x.Kind.Style is not (Behaviour.Dummy or Behaviour.Exploder)
               && (c.Story ? x.Story && x.Faction == c.Faction : !x.Story && x.Cavern == c.Cavern);

        /// <summary>
        /// A healer's heal, if it is due and someone of its side (itself included) is hurt enough: the patient gains
        /// <see cref="HealShare"/> of its life (UpdateHitpoints to its viewers), the heal plays, and the healer's next
        /// attack waits an attack interval. True when it healed (the step is spent).
        /// </summary>
        private static bool TryHeal(Creature c, DateTime now)
        {
            if (now < c.NextHeal || !CanAct(c))
                return false;
            Creature patient;
            lock (sync)
                patient = Nearby(c.Zone, c.Position, HealRange).Where(x => x == c || SameSide(c, x))
                    .Where(x => x.Health < x.Kind.MaxHealth * HealBelow && MathF.Abs(x.Position.Y - c.Position.Y) <= HealRange)
                    .OrderBy(x => (float)x.Health / Math.Max(1u, x.Kind.MaxHealth)).FirstOrDefault();
            if (patient == null)
                return false;
            uint amount = (uint)Math.Ceiling(patient.Kind.MaxHealth * HealShare);
            lock (sync)
            {
                if (patient.State == State.Dead)
                    return false;
                patient.Health = Math.Min(patient.Kind.MaxHealth, patient.Health + amount);
            }
            c.NextHeal = now + HealInterval;
            c.NextAttack = now + AttackInterval;
            PlayNpcSwing(c, patient.Position);
            List<WorldSession> viewers = ViewersOf(c);
            HitEffects.PlayOn(c.Guid, HealRelease, viewers);
            HitEffects.PlayOn(patient.Guid, HealImpact, viewers);
            SendHitpoints(patient, 0);
            return true;
        }

        /// <summary>A healer by its creature's name (see the notes above): Shaman, Priestess, Riftmender.</summary>
        private static bool IsHealerName(string name)
            => name.Contains("Shaman", StringComparison.OrdinalIgnoreCase) || name.Contains("Priestess", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Riftmender", StringComparison.OrdinalIgnoreCase);

        // ---- Highfall Catapult (TASKS 3.13) -----------------------------------------------------------------------------
        // 7 Jun 2016 (P5728): the catapult faces one way and only hits distant enemies, never close ones [P]. Wiki
        // "Highfall Catapult": Elite, Fire Essence affinity, throws burning rocks in a straight line at a target about
        // 60 blocks away, and they hurt and knock down players in an area where they land [P]. Client data
        // (audit-outposts-695.md fact 5) [P]: prop 2941 (the only catapult prop), model
        // Char_WarMachine_Catapult_Fantasy01 on WarMachineAnimationNetwork.mrn, whose messages are only Activate_01,
        // Ouch and Death (no walk, no turn, no "Attack": strings of the .mrn); composites 12825
        // PFX_Combat_Fire_Catapult_Release_01 and 12826 PFX_Combat_Fire_Catapult_Explosion_Impact_01.
        //
        // So it never moves and never turns: its heading is the placed prop's rotation (PlacedHeading). It shoots only at
        // players (and, for story factions, creatures of the other side) inside a cone in front of it between
        // SiegeMinRange and SiegeMaxRange; the rock flies in a straight line to the point it aimed at (it does not home)
        // and on landing hurts and knocks down (Crush, as the Proximity Mine's "knocking them down") everyone of the
        // other side within SiegeBlastRadius. A "block" is one 0.4 m voxel (docs/terrain.md), so 60 blocks ≈ 24 m [I].
        // Cone, ranges, interval, flight speed, radius, health and damage are ours (OPEN-LEADS L119).

        private const float SiegeMinRange = 10f, SiegeMaxRange = 28f, SiegeHalfAngle = 30f, SiegeMaxHeight = 15f;
        private const float SiegeBlastRadius = 4f, SiegeRockSpeed = 16f;

        /// <summary>
        /// ProjectileDefinitions row of the catapult's rock: 41 FX_Projectiles_Rocks_CatapultRock_Firey, leaving from the
        /// Projectile1 bone [P row]. Sent with <see cref="SiegeRockSpeed"/> as the velocity (the row's own 75 m/s would
        /// arrive long before the server lands it) [I].
        /// </summary>
        private const uint CatapultRockProjectile = 41;
        private static readonly TimeSpan SiegeInterval = TimeSpan.FromSeconds(5);
        /// <summary>A siege engine stays "in combat" this long after its last hit or target (no flapping exits / enters).</summary>
        private static readonly TimeSpan SiegeCalm = TimeSpan.FromSeconds(8);
        private const uint CatapultRelease = 12825, CatapultImpact = 12826;
        /// <summary>
        /// AnimationSelection 272 "Activate_01" (the first of the five rows with that MESSAGE: 272, 283, 346, 404, 410): the
        /// war machine network's fire animation [P that the network takes the message; I that any row with it plays it].
        /// </summary>
        private const uint AnimActivate = 272;
        public const uint HighfallCatapultProp = 2941;

        /// <summary>
        /// The heading of the placed prop a story creature stands on (its spawner, within 2 m of its home): the prop's
        /// rotation quaternion turns +Z (the creatures' forward, as <see cref="MovementBody"/> writes yaw) into its facing.
        /// Null when no such prop is found.
        /// </summary>
        private static float? PlacedHeading(Creature c, uint propId)
        {
            var spot = Props.PlacedOf(c.Zone, new HashSet<uint> { propId })
                .Where(p => Flat(p.Position, c.Home) <= 2f).OrderBy(p => Flat(p.Position, c.Home)).Select(p => p.Guid).FirstOrDefault();
            if (spot == 0 || Props.Find(spot) is not { } prop)
                return null;
            var q = new Quaternion(prop.Spawn.Rotation.X, prop.Spawn.Rotation.Y, prop.Spawn.Rotation.Z, prop.Spawn.Rotation.W);
            if (q.LengthSquared() < 1e-6f)
                return null;
            Vector3 forward = Vector3.Transform(Vector3.UnitZ, Quaternion.Normalize(q));
            return new Vector2(forward.X, forward.Z).LengthSquared() < 1e-6f ? null : MathF.Atan2(forward.X, forward.Z);
        }

        /// <summary>A point a little ahead of a creature along its heading (what a siege engine "faces" when it plays an animation).</summary>
        private static Vector3 Ahead(Creature c) => c.Position + new Vector3(MathF.Sin(c.Yaw), 0f, MathF.Cos(c.Yaw)) * 5f;

        /// <summary>Is a point inside a siege engine's field of fire (cone in front, between the ranges, not far above / below)?</summary>
        private static bool InFieldOfFire(Creature c, Vector3 p)
        {
            Vector3 to = new(p.X - c.Position.X, 0f, p.Z - c.Position.Z);
            float d = to.Length();
            if (d < SiegeMinRange || d > SiegeMaxRange || MathF.Abs(p.Y - c.Position.Y) > SiegeMaxHeight)
                return false;
            Vector3 forward = new(MathF.Sin(c.Yaw), 0f, MathF.Cos(c.Yaw));
            return Vector3.Dot(to / d, forward) >= MathF.Cos(SiegeHalfAngle * MathF.PI / 180f);
        }

        /// <summary>
        /// One AI step of a siege engine (Idle or Chase; see the notes above). It takes its heading once, lands a rock in
        /// flight when due, keeps its current target while that is still in its field of fire (else the highest damage
        /// dealer there, P3651, else the nearest), fires every <see cref="SiegeInterval"/>, and calms down
        /// <see cref="SiegeCalm"/> after it last had a target or was hurt. Nothing moves, so no movement packets are sent
        /// except the one that turns it to its heading.
        /// </summary>
        private static void ThinkSiege(Creature c, List<WorldSession> players, DateTime now)
        {
            if (c.HomeYaw == null)
            {
                // First step: face the way the builder placed it (once).
                float yaw = PlacedHeading(c, HighfallCatapultProp) ?? c.Yaw;
                c.HomeYaw = yaw;
                if (MathF.Abs(yaw - c.Yaw) > 0.01f)
                {
                    c.Yaw = yaw;
                    c.Velocity = Vector3.Zero;
                    SendMovement(c);
                }
                c.NextAttack = now;
            }
            if (c.ShotLandsAt != default && now >= c.ShotLandsAt)
                LandRock(c);
            if (c.Tactics == TacticsNothing)
                return;
            // Its target: kept while still valid; else the highest damage dealer in its field (P3651), else the nearest
            // player or (story) creature of the other side there.
            bool PlayerOk(WorldSession p) => p != null && players.Contains(p) && p.ZoneId == c.Zone && !Health.IsDead(p)
                && !Health.Unnoticed(p) && MayFight(c, p) && InFieldOfFire(c, p.Position);
            WorldSession t = PlayerOk(c.Target) ? c.Target : null;
            Creature f = t == null && c.Foe != null && Hostile(c, c.Foe) && InFieldOfFire(c, c.Foe.Position) ? c.Foe : null;
            if (t == null && f == null)
            {
                lock (sync)
                    t = c.Threat.Where(k => PlayerOk(k.Key)).OrderByDescending(k => k.Value).Select(k => k.Key).FirstOrDefault();
                t ??= c.Faction == FactionEnemy || c.Faction == FactionNeutral
                    ? players.Where(PlayerOk).OrderBy(p => Flat(p.Position, c.Position)).FirstOrDefault() : null;
                if (t == null && c.Story && c.Faction != FactionNeutral)
                    lock (sync)
                        f = Nearby(c.Zone, c.Position, SiegeMaxRange).Where(x => Hostile(c, x) && InFieldOfFire(c, x.Position))
                            .OrderBy(x => Flat(x.Position, c.Position)).FirstOrDefault();
            }
            bool engaged = false;
            lock (sync)
            {
                c.Target = t;
                c.Foe = f;
                if ((t != null || f != null) && c.State != State.Chase)
                {
                    c.State = State.Chase;
                    c.Anchor = c.Home;
                    engaged = true;
                }
                if (t != null || f != null)
                    c.BusyUntil = now + SiegeCalm;
            }
            if (engaged && c.Story)
                StoryTools.CreatureCombat(c.Guid, true);
            if (t == null && f == null)
            {
                // Nobody in its field: it calms down once a while has passed since its last target or hit (a close attacker
                // it can't shoot keeps it "in combat" only while it keeps hitting).
                if (c.State == State.Chase && now >= c.BusyUntil && now - c.LastHurt >= SiegeCalm)
                    EndCombat(c, toIdle: true);
                return;
            }
            if (now < c.NextAttack || c.ShotLandsAt != default || !CanAct(c))
                return;
            // Fire: the throw animation and release on the catapult, the rock lands where the target stood.
            Vector3 aim = t != null ? t.Position : f.Position;
            c.NextAttack = now + SiegeInterval;
            c.ShotAt = aim;
            c.ShotLandsAt = now + TimeSpan.FromSeconds(Flat(aim, c.Position) / SiegeRockSpeed);
            PlayAnim(c, AnimActivate, Ahead(c));
            List<WorldSession> viewers = ViewersOf(c);
            HitEffects.PlayOn(c.Guid, CatapultRelease, viewers);
            // The rock in flight (the client starts it at the catapult's Projectile1 bone when the catapult is full there).
            Vector3 from = c.Position + new Vector3(0f, 3f, 0f), to = aim + new Vector3(0f, 0.5f, 0f);
            c.ShotProjectile = Projectiles.NewId();
            Projectiles.Create(viewers, c.ShotProjectile, CatapultRockProjectile, from, to - from, c.Guid, to, t?.PlayerGuid ?? 0,
                speed: Vector3.Distance(from, to) / Math.Max(0.1f, Flat(aim, c.Position) / SiegeRockSpeed));
        }

        /// <summary>
        /// A siege rock lands at <see cref="Creature.ShotAt"/>: the impact plays there, and every player (unless the engine
        /// is an ally) and every creature of the other side within <see cref="SiegeBlastRadius"/> takes its damage and is
        /// knocked down (Crush).
        /// </summary>
        private static void LandRock(Creature c)
        {
            Vector3 at = c.ShotAt;
            c.ShotLandsAt = default;
            List<WorldSession> viewers = ViewersOf(c);
            if (c.ShotProjectile != 0)
            {
                Projectiles.Delete(viewers, c.ShotProjectile, at, true, 0);
                c.ShotProjectile = 0;
            }
            HitEffects.PlayAt(at, CatapultImpact, viewers);
            if (c.Faction != FactionAlly)
                foreach (WorldSession p in PlayerManager.Snapshot().Where(p => p.ZoneId == c.Zone && !Health.IsDead(p)
                             && Vector3.Distance(p.Position, at) <= SiegeBlastRadius))
                {
                    HurtPlayer(c, p, c.Kind.Damage);
                    if (!Health.IsDead(p))
                        Combat.Statuses.Apply(p.PlayerGuid, Combat.Status.Crush, null, viewers: viewers);
                }
            if (!c.Story)
                return;
            List<Creature> caught;
            lock (sync)
                caught = Nearby(c.Zone, at, SiegeBlastRadius)
                    .Where(x => Hostile(c, x) && MathF.Abs(x.Position.Y - at.Y) <= SiegeBlastRadius).ToList();
            foreach (Creature x in caught)
            {
                HurtCreature(c, x, c.Kind.Damage);
                if (Alive(x))
                    Combat.Statuses.Apply(x.Guid, Combat.Status.Crush, null, viewers: ViewersOf(x));
            }
        }

        /// <summary>Cozy Campfire item 3894 and the chance a world creature drops one (ours).</summary>
        private const uint CozyCampfire = 3894;
        private const double CampfireChance = 0.05;

        /// <summary>A kill's chance of 1-3 Lumens (scaled by <see cref="Pacing"/>) and of an unknown recipe (ours).</summary>
        private const double KillLumenChance = 0.15, KillRecipeChance = 0.01;

        /// <summary>An undamaged creature that notices a player picks its target at random within this distance (P3651).</summary>
        private const float RandomTargetRange = 40f;

        /// <summary>
        /// Counts a player's damage and keeps the creature on the highest damage dealer (26 Apr 2016, P3651: creatures
        /// go for whoever deals the most damage, which replaces their first pick once they are hurt; P3652: they still
        /// switch when the highest changes, never between targets otherwise). Call inside the lock.
        /// </summary>
        private static void NoteDamage(Creature c, WorldSession attacker, uint amount)
        {
            if (attacker == null || amount == 0)
                return;
            c.Threat[attacker] = c.Threat.GetValueOrDefault(attacker) + amount;
            if (c.State == State.Chase && c.Tactics != TacticsNothing && c.Faction != FactionAlly)
                c.Target = c.Threat.MaxBy(k => k.Value).Key;
        }

        // ---- Factions: creatures against creatures (TASKS 6.20) --------------------------------------------------------
        // 21 Mar 2016 (P4223): allies help players against enemies, neutrals just watch; wiki "Creature Prop"
        // (Reaction): an enemy attacks anyone who comes near, a neutral fights only when provoked, an ally helps
        // visitors fight the site's other creatures; dev, 4 May 2016 (P5225): combat AI outranks everything else on
        // every creature. So, among story creatures (sites in
        // Play Mode): an ally takes on enemy creatures near it, an enemy takes on allies as well as players (the nearest
        // first), a neutral fights only whoever hit it, and none of it waits for a scripted walk. Kills by a creature drop
        // nothing and give the spawner's "dies" event no killer (P4719, 26 Apr 2016: no loot when an ally or neutral makes
        // the kill). The checks run at the AI tick, only for story creatures, within AggroRange, at most once a second
        // per idle creature (FoeScanInterval).

        /// <summary>Story factions (spawner Reaction row 17 / the faction Links): 0 enemy, 1 neutral, 2 ally.</summary>
        private const int FactionEnemy = 0, FactionNeutral = 1, FactionAlly = 2;

        /// <summary>
        /// May this creature fight this player? Enemies anyone; a neutral only someone who hurt it (its threat list);
        /// allies never.
        /// </summary>
        private static bool MayFight(Creature c, WorldSession p)
        {
            if (c.Faction == FactionEnemy)
                return true;
            if (c.Faction != FactionNeutral)
                return false;
            lock (sync)
                return c.Threat.ContainsKey(p);
        }

        /// <summary>
        /// Would <paramref name="a"/> fight creature <paramref name="b"/>? Both story creatures on one landscape, one an enemy
        /// and the other an ally. Target Dummies take no part (enemy NPCs ignore them, P5275) and
        /// hazards (fungi, mines) are never picked as targets (they go off by themselves, <see cref="ThinkFungus"/>).
        /// </summary>
        private static bool Hostile(Creature a, Creature b)
            => a != b && a.Story && b.Story && b.State != State.Dead && a.Zone == b.Zone
                && a.Kind.Style != Behaviour.Dummy && b.Kind.Style is not (Behaviour.Dummy or Behaviour.Exploder)
                && ((a.Faction == FactionEnemy && b.Faction == FactionAlly) || (a.Faction == FactionAlly && b.Faction == FactionEnemy));

        /// <summary>A foe it may go on fighting: still hostile and within <see cref="LeashRange"/>.</summary>
        private static bool FoeValid(Creature c, Creature f) => Hostile(c, f) && Dist(c, f.Position) <= LeashRange;

        /// <summary>The nearest creature <paramref name="c"/> would fight, within <paramref name="range"/> (null when none).</summary>
        private static Creature NearestFoe(Creature c, float range)
        {
            if (!c.Story || c.Faction == FactionNeutral)
                return null;
            lock (sync)
                return Nearby(c.Zone, c.Position, range)
                    .Where(x => Hostile(c, x) && MathF.Abs(x.Position.Y - c.Position.Y) <= Math.Max(range, MaxReachHeight * 2))
                    .OrderBy(x => Dist(c, x.Position)).FirstOrDefault();
        }

        /// <summary>Where a fight starting now is leashed to: the spot a scripted walk stopped at, else home.</summary>
        private static Vector3 AnchorOf(Creature c) => c.WalkTo != null ? c.Position : c.Home;

        /// <summary>
        /// An idle creature looks for a fight (Combat Tactics "Do Nothing" never does): enemies the nearest living
        /// player within <see cref="AggroRange"/> (not one inside a graveyard's buffer zone, Graveyards.InSafeZone), story
        /// enemies / allies also the nearest creature of the other side; the nearer of the two wins. A player target is
        /// then picked as P3651 says. True when it engaged.
        /// </summary>
        private static bool Engage(Creature c, List<WorldSession> players, DateTime now)
        {
            if (c.Tactics == TacticsNothing)
                return false;
            WorldSession near = c.Faction != FactionEnemy ? null
                : players.Where(p => p.ZoneId == c.Zone && !Health.IsDead(p) && !Health.Unnoticed(p) && Dist(c, p.Position) <= AggroRange
                        && !Graveyards.InSafeZone(p.ZoneId, p.Position))
                    .OrderBy(p => Dist(c, p.Position)).FirstOrDefault(p => Sees(c, p.Position));
            Creature foe = null;
            if (c.Story && c.Kind.Damage > 0 && c.Faction != FactionNeutral && now >= c.NextFoeScan)
            {
                c.NextFoeScan = now + FoeScanInterval;
                foe = NearestFoe(c, AggroRange);
            }
            if (foe != null && (near == null || Dist(c, foe.Position) <= Dist(c, near.Position)))
            {
                lock (sync)
                {
                    c.State = State.Chase;
                    c.Foe = foe;
                    c.Target = null;
                    c.Anchor = AnchorOf(c);
                }
                StoryTools.CreatureCombat(c.Guid, true);
                return true;
            }
            if (near == null || !(c.Kind.Damage > 0 || Dist(c, near.Position) <= SkittishRange))
                return false;
            c.State = c.Kind.Damage > 0 ? State.Chase : State.Flee;
            // Undamaged: a random target within 40 m (26 Apr 2016, P3651); a skittish creature flees
            // from the one that startled it.
            if (c.State == State.Chase)
            {
                var pool = players.Where(p => p.ZoneId == c.Zone && !Health.IsDead(p) && !Health.Unnoticed(p)
                    && Dist(c, p.Position) <= RandomTargetRange).ToList();
                near = pool.Count > 0 ? pool[random.Next(pool.Count)] : near;
                c.Anchor = AnchorOf(c);
            }
            c.Target = near;
            if (c.Story && c.State == State.Chase)
                StoryTools.CreatureCombat(c.Guid, true);
            return true;
        }

        /// <summary>
        /// The fight is over (target gone, leash, out of reach): back to Idle (<paramref name="toIdle"/>, or when a
        /// scripted walk was interrupted: the walk goes on), else home to heal (Return). The spawner's "exits combat"
        /// Links run.
        /// </summary>
        private static void EndCombat(Creature c, bool toIdle)
        {
            bool was;
            lock (sync)
            {
                was = c.State == State.Chase;
                c.State = toIdle || c.WalkTo != null ? State.Idle : State.Return;
                c.Target = null;
                c.Foe = null;
                c.Anchor = null;
                c.Threat.Clear();
            }
            if (c.Story && was)
                StoryTools.CreatureCombat(c.Guid, false);
        }

        /// <summary>
        /// A creature's attack (or a hazard's blast) lands on another creature: the damage (±15 %, less while the
        /// attacker Rots or is Weakened, more on a Tormented victim), the same hit points update (UpdateHitpoints 0x03 and
        /// the Life resource) and hit reaction ("Ouch") as a player's hit, an <paramref name="impact"/> effect when given.
        /// An idle victim fights back. At 0 it dies with no killer: no loot (P4719) and its spawner's "dies" Links run
        /// with nobody as the triggering player.
        /// </summary>
        private static void HurtCreature(Creature attacker, Creature victim, uint damage, uint impact = 0)
        {
            if (victim.Kind.Style is Behaviour.Dummy or Behaviour.Exploder || Shielded(victim))
                return;
            uint dealt = (uint)(damage * (0.85 + random.NextDouble() * 0.3) * Combat.Statuses.DealtFactor(attacker.Guid)
                * Combat.Statuses.TakenFactor(victim.Guid));
            if (dealt == 0)
                return;
            bool died, engaged = false;
            lock (sync)
            {
                if (victim.State == State.Dead)
                    return;
                victim.Health = dealt >= victim.Health ? 0 : victim.Health - dealt;
                died = victim.Health == 0;
                if (!died && victim.State != State.Chase && victim.State != State.Flee && victim.Tactics != TacticsNothing
                    && victim.Kind.Damage > 0 && attacker.Kind.Style != Behaviour.Exploder && Hostile(victim, attacker))
                {
                    victim.State = State.Chase;
                    victim.Foe = attacker;
                    victim.Target = null;
                    victim.Anchor = AnchorOf(victim);
                    engaged = true;
                }
            }
            if (impact != 0)
                HitEffects.PlayOn(victim.Guid, impact, ViewersOf(victim));
            SendHitpoints(victim, dealt);
            if (died)
            {
                Kill(victim, null);
                return;
            }
            PlayAnim(victim, AnimOuch, attacker.Position);
            if (engaged && victim.Story)
                StoryTools.CreatureCombat(victim.Guid, true);
        }

        /// <summary>
        /// The Target Dummy (TASKS 6.28; P5275 / P5433 / P5445): stands where it was placed and every few seconds swings
        /// (AnimationSelection "Attack", ours: 4-8 s), hurting nobody; it never moves, never targets anyone, and no other
        /// creature attacks it (<see cref="Hostile"/>). Players can hit and kill it; left alone for
        /// <see cref="DummyMend"/> it mends to full (ours: it has no Return home to heal on).
        /// </summary>
        private static void ThinkDummy(Creature c, DateTime now)
        {
            if (c.Health < c.Kind.MaxHealth && now - c.LastHurt >= DummyMend)
            {
                lock (sync)
                    c.Health = c.Kind.MaxHealth;
                SendHitpoints(c, 0);
            }
            if (now < c.NextSwing)
                return;
            c.NextSwing = now.AddSeconds(4 + random.NextDouble() * 4);
            if (CanAct(c))
                PlayNpcSwing(c, c.Position + new Vector3(MathF.Sin(c.Yaw), 0f, MathF.Cos(c.Yaw)));
        }

        /// <summary>
        /// How long a creature stays dead. Outpost creatures (a cavern with an Outpost): 8-10 minutes at random, its Elite
        /// 15 (dev, 4 May 2016, "[Feedback] Chaos Caverns + Outposts", P5193 / P5195 / P5197: 8-10 minutes for every
        /// non-elite outpost creature, 15 for elites); everyone else
        /// <see cref="RespawnTime"/>.
        /// </summary>
        private static TimeSpan RespawnDelay(Creature c)
        {
            if (c.Cavern < 0 || !Outposts.HasOutpost(c.Cavern))
                return RespawnTime;
            return c.Boss ? TimeSpan.FromMinutes(15) : TimeSpan.FromSeconds(480 + random.Next(121));
        }

        /// <summary>Can it attack (Burn, Freeze, Electrocute, Crush stop it, except on <see cref="IgnoresControl"/>)?</summary>
        private static bool CanAct(Creature c) => IgnoresControl(c) || Combat.Statuses.CanAct(c.Guid);

        /// <summary>Can it move (Freeze, Electrocute, Crush, Condemn stop it, except on <see cref="IgnoresControl"/>)?</summary>
        private static bool CanMove(Creature c) => IgnoresControl(c) || Combat.Statuses.CanMove(c.Guid);

        /// <summary>Its speed factor (Cripple halves it, except on <see cref="IgnoresControl"/>).</summary>
        private static float MoveFactor(Creature c) => IgnoresControl(c) ? 1f : Combat.Statuses.Speed(c.Guid);

        /// <summary>Deep story creatures some player has on screen (they think only then). Call inside the lock.</summary>
        private static IEnumerable<Creature> DeepStoryInView()
        {
            var seen = deepStorySent.Values.SelectMany(h => h).ToHashSet();
            return seen.Count == 0 ? Enumerable.Empty<Creature>()
                : regions.Values.SelectMany(r => r).Where(c => c.Story && c.Deep && seen.Contains(c.Guid));
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
                if (c.Deep)
                    return deepStorySent.Where(s => s.Value.Contains(c.Guid)).Select(s => s.Key).ToList();
                return sent.Where(s => s.Value.Contains(c.RegionKey)).Select(s => s.Key).ToList();
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
            uint land = zone == 0 ? Home : zone;
            foreach ((int index, Creature[] group) in cavernGroups)
                if (Caves.ZoneOf(index) == land)
                    foreach (Creature c in group)
                        if (Vector3.Distance(c.Position, point) <= range)
                            yield return c;
        }

        /// <summary>Dev: describes creatures near a player.</summary>
        public static string Describe(WorldSession session)
        {
            lock (sync)
                return string.Join("; ", Nearby(session.ZoneId, session.Position, 40f).Select(c =>
                    $"{c.Kind.Name} {c.Guid:X} {c.State} hp {c.Health} at {c.Position} d {Flat(c.Position, session.Position):F1}"
                    + (c.Story ? $" faction {c.Faction} tactics {c.Tactics}{(c.Foe != null ? $" foe {c.Foe.Guid:X}" : "")}" : "")))
                    + $" | regions sent {(sent.TryGetValue(session, out var h) ? h.Count : 0)}"
                    + $" | caverns sent {(cavernsSent.TryGetValue(session, out var cs) ? cs.Count : 0)}, nearest cavern "
                    + $"{(!Caves.HasCaverns(session.ZoneId) ? -1 : Caves.In(session.ZoneId).Min(i => Vector3.Distance(CavernFloor(i), session.Position))):F0} m"
                    + $" | at {session.Position} arriving {session.IsArriving} home {session.OnHomeContinent}";
        }

        /// <summary>Dev/testing: kills every creature within a radius of a player (no loot). Returns how many.</summary>
        /// <summary>Dev/testing: the nearest living creature within a radius dies as if the player killed it (loot bag).</summary>
        /// <summary>Dev command "sight": each living creature within 40 m, and whether it would see the player (F4).</summary>
        public static List<string> DevSight(WorldSession session)
        {
            uint zone = session.ZoneId;
            var lines = new List<string> { $"player spot solid: {Sight.SolidAt(session.Position + new Vector3(0f, 1.5f, 0f), zone)} at {session.Position}" };
            for (float dy = -2f; dy <= 4f; dy += 0.5f)
                lines.Add($"  column {dy:+0.0;-0.0} m: {Sight.Explain(session.Position + new Vector3(0f, dy, 0f), zone)}");
            foreach (ulong g in InRadius(session.ZoneId, session.Position, 40f))
                if (Find(g) is Creature c)
                    lines.Add($"{c.Kind.Name} cavern {c.Cavern} at {Vector3.Distance(c.Position, session.Position):0.0} m: "
                        + $"sees {Sees(c, session.Position)}, eye solid {Sight.SolidAt(Eye(c), c.Zone)}");
            return lines;
        }

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

        /// <summary>
        /// Is the creature a full entity on this client (it asked for and got LightweightToFullNpc for the creature's
        /// current spawn)? Then the client finds it in its entity table and it can speak bubbles itself (L96).
        /// </summary>
        public static bool IsFullFor(WorldSession session, ulong guid)
            => guid != 0 && Find(guid) is Creature c && session.FullNpcs.TryGetValue(guid, out uint transient) && transient == c.TransientId;

        /// <summary>
        /// Dev "npctext": DisplayPropText from the nearest creature through the story route (OPEN-LEADS L96): over the
        /// creature when this client has it full, else over the player. Returns the creature's guid, 0 when none is near.
        /// </summary>
        public static ulong DevTextNearest(WorldSession session, uint mode, string text, float radius)
        {
            Creature c;
            lock (sync)
                c = Nearby(session.ZoneId, session.Position, radius).Where(x => x.State != State.Dead)
                    .OrderBy(x => Vector3.Distance(x.Position, session.Position)).FirstOrDefault();
            if (c == null)
                return 0;
            // The player is the fallback speaker: the bubble shows over the creature only when this client has it as a
            // full entity (IsFullFor, the story route), else over the player.
            Props.DisplayTextBy(session.PlayerGuid, c.Zone, c.Position, text, mode, 8, creature: c.Guid);
            return c.Guid;
        }

        /// <summary>Dev "hitnear": the nearest creature takes a hit from this player (aggro, hit reaction, health).</summary>
        /// <summary>
        /// Dev: the nearest living creature's attack lands on the player through the normal path (<see cref="HurtPlayer"/>:
        /// blocks, Harden, shields, the armour's defensive passives, then the damage). False when none is near.
        /// </summary>
        public static bool DevStruckByNearest(WorldSession session, uint damage, float radius)
        {
            Creature c;
            lock (sync)
                c = Nearby(session.ZoneId, session.Position, radius).Where(x => x.State != State.Dead)
                    .OrderBy(x => Vector3.Distance(x.Position, session.Position)).FirstOrDefault();
            if (c == null)
                return false;
            HurtPlayer(c, session, damage);
            return true;
        }

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
                        if (regions.TryGetValue(c.RegionKey, out Creature[] members))
                            regions[c.RegionKey] = members.Where(m => m != c).ToArray();
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
                if (regions.TryGetValue(c.RegionKey, out Creature[] members))
                    regions[c.RegionKey] = members.Where(m => m != c).ToArray();
            }
        }

        private static Creature FindStory(ulong guid)
        {
            lock (sync)
                return regions.Values.SelectMany(r => r).FirstOrDefault(c => c.Guid == guid && c.Story);
        }

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
            var noLoot = Array.Empty<(uint, uint, uint)>();
            // Combat hardware (PropCategory 121): the Target Dummy stands and swings (ThinkDummy); mines and the Vine Trap
            // are hazards that go off like a Fiery Fungus (ThinkFungus). Neither drops loot.
            if (propId is TargetDummyProp or RuinTargetDummyProp)
                return new Kind(name.Length > 0 ? name : "Target Dummy", model, DummyHealth, 0, noLoot, Behaviour.Dummy, Prop: propId);
            if (propId is ProximityMineProp or RuinProximityMineProp or VineTrapProp or RuinVineTrapProp)
                return new Kind(name.Length > 0 ? name : "Proximity Mine", model, MineHealth, MineDamage, noLoot, Behaviour.Exploder, Mine: true);
            // A story Fiery Fungus (creature prop 2613, ruin spawner 1909) explodes as the world's do: P5269 calls fungi
            // "non-pathing creatures". Its loot and blast are the world fungus's (P3522).
            if (n.Contains("Fung", StringComparison.OrdinalIgnoreCase))
                return new Kind(name.Length > 0 ? name : "Fiery Fungus", model, FieryFungus.MaxHealth, FieryFungus.Damage,
                    FieryFungus.Loot, Behaviour.Exploder);
            // The Highfall Catapult (2941, combat hardware): a siege engine (ThinkSiege, TASKS 3.13). An Elite with Fire
            // affinity (wiki): Elite-like health and damage per rock, plus a chance of Fire Essence (numbers ours).
            if (propId == HighfallCatapultProp || n.Contains("Catapult", StringComparison.OrdinalIgnoreCase))
                return new Kind(name.Length > 0 ? name : "Highfall Catapult", model, 1600, 120,
                    new[] { (34u, 1u, 3u), (Gathering.AdventureEssence, 0u, 1u), (FireEssence, 0u, 1u) }, Behaviour.Siege);
            // Healers (Shaman / Priestess / Riftmender, TASKS 6.21): ranged casters that heal their side (TryHeal).
            bool healer = IsHealerName(n);
            // Outpost archers and casters shoot (P5727: outpost NPCs stop shooting through walls [P];
            // audit-outposts-695.md F4): Ranged / Archer / Caster spawners get the ranged behaviour and a projectile.
            uint projectile = healer ? 0 : RangedProjectileFor(n);
            Behaviour style = healer || projectile != 0 ? Behaviour.Ranged : Behaviour.Melee;
            (uint health, uint damage) = boss ? (2400u, 110u)
                : healer ? (420u, 40u)
                : n.Contains("Caster", StringComparison.OrdinalIgnoreCase) ? (420u, 55u)
                : n.Contains("Ranged", StringComparison.OrdinalIgnoreCase) ? (450u, 50u)
                : n.Contains("Patroller", StringComparison.OrdinalIgnoreCase) ? (380u, 35u)
                : n.Contains("Chomper", StringComparison.OrdinalIgnoreCase) ? (700u, 70u)
                : n.Contains("Stag", StringComparison.OrdinalIgnoreCase) || n.Contains("Deer", StringComparison.OrdinalIgnoreCase) ? (200u, 0u)
                : (550u, 50u);
            name = boss ? name.Replace("BOSS", "").Replace("GIANTS", "Giant").Trim() : name;
            return new Kind(name.Length > 0 ? name : "Creature", model, health, damage,
                new[] { (34u, 1u, 3u), (Gathering.AdventureEssence, 0u, 1u) }, style, Healer: healer, Projectile: projectile, Prop: propId);
        }

        /// <summary>
        /// The kind of creature a story spawner makes, before its health / damage % (<see cref="SpawnStory"/>): from the
        /// spawner's prop and name (<see cref="SpawnerKind"/>; else Slaug, Skeleton, Ogre / BOSS, Wolf, Deer / Stag, Caster,
        /// Patroller; kobold otherwise). Needs no live creature, so a Combat Node's settings can list the abilities of its
        /// linked spawner's creature in Edit Mode (<see cref="StoryAbilityNames"/>).
        /// </summary>
        private static Kind StoryKind(string spawnerName, uint propId)
        {
            string n = spawnerName ?? "";
            return SpawnerKind(n, propId) ?? (n.Contains("Slaug", StringComparison.OrdinalIgnoreCase) ? Slaug
                : n.Contains("Skeleton", StringComparison.OrdinalIgnoreCase) ? Skeleton
                : n.Contains("BOSS", StringComparison.Ordinal) || n.Contains("Ogre", StringComparison.OrdinalIgnoreCase)
                    || n.Contains("Giant", StringComparison.OrdinalIgnoreCase) ? Ogre
                : n.Contains("Wolf", StringComparison.OrdinalIgnoreCase) || n.Contains("Hound", StringComparison.OrdinalIgnoreCase) ? SpiritWolf
                : n.Contains("Deer", StringComparison.OrdinalIgnoreCase) || n.Contains("Stag", StringComparison.OrdinalIgnoreCase) ? Deer
                : n.Contains("Caster", StringComparison.OrdinalIgnoreCase) ? KoboldCaster
                : n.Contains("Patroller", StringComparison.OrdinalIgnoreCase) ? KoboldRunt
                : KoboldMelee);
        }

        /// <summary>
        /// A story-tool creature spawner's creature (StoryTools): the kind comes from the spawner's prop and name
        /// (<see cref="StoryKind"/>). It is not respawned by the camp clock. Returns its guid.
        /// </summary>
        /// <param name="healthPercent">The spawner's Health % Multiplier (2.14; only up: below 100 counts as 100).</param>
        /// <param name="damagePercent">The spawner's Damage % Multiplier (2.14; up or down).</param>
        /// <param name="weaponsEquipped">The spawner's "Spawn With Weapons Equipped" (row 40): it appears with its weapons
        /// equipped (<see cref="AnimNpcWeaponsEquipped"/>, sent with the spawn), else sheathed.</param>
        /// <param name="site">The build site (claim id) it belongs to: its navigation uses the site's grid (OPEN-LEADS L133).</param>
        public static ulong SpawnStory(uint zone, Vector3 at, string spawnerName, uint propId = 0, string displayName = null,
            float healthPercent = 100f, float damagePercent = 100f, bool weaponsEquipped = false, ulong site = 0)
        {
            string n = spawnerName ?? "";
            Kind kind = StoryKind(n, propId);
            // Health / damage % from the spawner (2.14): health never below the kind's, damage scaled either way.
            healthPercent = Math.Clamp(healthPercent, 100f, 500f);
            damagePercent = Math.Clamp(damagePercent, 25f, 300f);
            bool strengthChanged = healthPercent != 100f || damagePercent != 100f;
            if (strengthChanged)
                kind = kind with
                {
                    MaxHealth = (uint)Math.Round(kind.MaxHealth * healthPercent / 100f),
                    Damage = (uint)Math.Round(kind.Damage * damagePercent / 100f)
                };
            at.Y = TerrainHeights.HeightAt(at.X, at.Z, zone) is float ground && ground > at.Y - 2f && ground < at.Y + 2f ? ground : at.Y;
            int rx = (int)MathF.Floor(at.X / RegionSize), rz = (int)MathF.Floor(at.Z / RegionSize);
            long key = Pack(zone, rx, rz);
            Creature c;
            lock (sync)
            {
                Creature[] existing = Region(zone, rx, rz);
                // A free guid index (200-255) in the region; none left (56 story creatures in one 128 m region): no spawn,
                // its spawner keeps no creature (StoryTools checks for 0). .First() threw here, and the exception broke off
                // the spawner's Play Mode start or respawn (bughunt-combat.md B4).
                int free = Enumerable.Range(200, 56).FirstOrDefault(i => existing.All(m => (m.Guid & 0xFF) != (ulong)i));
                if (free == 0)
                {
                    log.Warn($"Story creature {spawnerName} at {at} on landscape {zone}: its region already holds 56 story creatures; not spawned.");
                    return 0;
                }
                byte index = (byte)free;
                c = new Creature
                {
                    Guid = GuidBase | ((ulong)key << 8) | index,
                    RegionKey = key,
                    Zone = zone,
                    TransientId = PlayerManager.AllocateTransientId(),
                    Kind = kind,
                    Home = at,
                    Position = at,
                    Health = kind.MaxHealth,
                    NextWander = DateTime.UtcNow.AddSeconds(5),
                    Story = true,
                    Site = site,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName,
                    StrengthChanged = strengthChanged,
                    WeaponsEquipped = weaponsEquipped,
                    Boss = n.Contains("BOSS", StringComparison.Ordinal),
                    // A catapult can't walk: it fights where it stands unless its spawner says Do Nothing (StoryTactics).
                    Tactics = n.Contains("Catapult", StringComparison.OrdinalIgnoreCase) ? TacticsStationary : TacticsStandard,
                    // The dummy's first swing comes a moment after it appears.
                    NextSwing = DateTime.UtcNow.AddSeconds(2 + random.NextDouble() * 3),
                    Deep = TerrainHeights.DepthBelowGround(at, zone) > UndergroundDropDepth
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
            Combat.BlockStance.Forget(c.Guid);
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

        /// <summary>A story creature heals a share of its maximum health (a Damage Node with "Heal Instead"), up to its maximum.</summary>
        public static void HealStory(ulong guid, float percent)
        {
            Creature c = FindStory(guid);
            if (c == null || c.State == State.Dead)
                return;
            uint amount = (uint)Math.Ceiling(c.Kind.MaxHealth * percent / 100f);
            lock (sync)
                c.Health = (uint)Math.Min(c.Kind.MaxHealth, (long)c.Health + amount);
        }

        /// <summary>A story creature plays an AnimationSelection (Story Node emote or stance), facing where it looks.</summary>
        public static void PlayStoryAnimation(ulong guid, uint animation)
        {
            Creature c = FindStory(guid);
            if (c != null && c.State != State.Dead)
                PlayAnim(c, animation, c.Position + new Vector3(MathF.Sin(c.Yaw), 0f, MathF.Cos(c.Yaw)));
        }

        /// <summary>The network defaults: Calm (Emote_Emotion 0) and Standing (Stance_Index 0).</summary>
        public const uint MoodCalm = 218, StanceStanding = 223;

        /// <summary>
        /// Keeps a story creature's mood and / or stance (null = unchanged) for players who see it later
        /// (<see cref="SendPose"/>); with <paramref name="play"/> the changed ones are also played now to those who see it
        /// (a respawn taking its spawner's saved pose; a Story Node plays them itself through PlayStoryAnimation).
        /// </summary>
        public static void StoryPose(ulong guid, uint? mood, uint? stance, bool play = false)
        {
            Creature c = FindStory(guid);
            if (c == null || c.State == State.Dead)
                return;
            lock (sync)
            {
                if (mood is uint m)
                    c.StoryMood = m == MoodCalm ? 0 : m;
                if (stance is uint s)
                    c.StoryStance = s == StanceStanding ? 0 : s;
            }
            if (!play)
                return;
            Vector3 ahead = c.Position + new Vector3(MathF.Sin(c.Yaw), 0f, MathF.Cos(c.Yaw));
            if (mood is uint playMood && playMood != MoodCalm && playMood != 0)
                PlayAnim(c, playMood, ahead);
            if (stance is uint playStance && playStance != StanceStanding && playStance != 0)
                PlayAnim(c, playStance, ahead);
        }

        /// <summary>
        /// A story creature's kept mood and stance to one player who newly sees it (spawn, full data): the selections set
        /// actor variables that live only in each client (as WeaponIdle, <see cref="SendSpawn"/>), so a later viewer would
        /// otherwise see the defaults. Mood first: Emote_Emotion colours the stance's idle (as StoryTools.Perform plays
        /// them). Nothing for a creature at the defaults (40 bytes each otherwise).
        /// </summary>
        private static void SendPose(WorldSession session, Creature c)
        {
            if (!c.Story)
                return;
            if (c.StoryMood != 0)
                session.EnqueueMessage(ZoneMessageOpcode.AbilityPlayAnim, PlayAnimBody(c, c.StoryMood, Ahead(c)));
            if (c.StoryStance != 0)
                session.EnqueueMessage(ZoneMessageOpcode.AbilityPlayAnim, PlayAnimBody(c, c.StoryStance, Ahead(c)));
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

        /// <summary>
        /// Combat Tactics (EditableProperties row 31 `npc_spawner_ai_capability`, 0-2, text 577073; the three option
        /// texts 577230 Standard, 577231 Stationary, 577232 Do Nothing; value order inferred from the text order).
        /// </summary>
        public const int TacticsStandard = 0, TacticsStationary = 1, TacticsNothing = 2;

        /// <summary>A story creature's Combat Tactics (see <see cref="TacticsStandard"/>).</summary>
        public static void StoryTactics(ulong guid, int tactics)
        {
            Creature c = FindStory(guid);
            if (c != null)
                lock (sync)
                {
                    c.Tactics = tactics;
                    if (tactics == TacticsNothing && c.State == State.Chase)
                    {
                        c.State = State.Return;
                        c.Target = null;
                        c.Foe = null;
                        c.Anchor = null;
                    }
                }
        }

        /// <summary>
        /// A story creature equips (draws) or sheathes its weapons (<see cref="AnimNpcWeaponsEquipped"/> /
        /// <see cref="AnimNpcWeaponsSheathed"/>), facing where it looks. Only a real change goes out: one AbilityPlayAnim
        /// (40 bytes) to the players who see it. False when nothing changed (or no such living creature).
        /// </summary>
        public static bool StoryWeapons(ulong guid, bool equipped)
        {
            Creature c = FindStory(guid);
            if (c == null || c.State == State.Dead)
                return false;
            lock (sync)
            {
                if (c.WeaponsEquipped == equipped)
                    return false;
                c.WeaponsEquipped = equipped;
            }
            PlayAnim(c, equipped ? AnimNpcWeaponsEquipped : AnimNpcWeaponsSheathed, c.Position + new Vector3(MathF.Sin(c.Yaw), 0f, MathF.Cos(c.Yaw)));
            // The weapon model too, when it has one (CreatureWeapons): in the hand when Equipped, gone when Sheathed.
            SendWeapon(c);
            return true;
        }

        /// <summary>Whether a story exploder damages terrain (its spawner's row 105).</summary>
        public static void StoryDamagesTerrain(ulong guid, bool on)
        {
            Creature c = FindStory(guid);
            if (c != null)
                lock (sync)
                    c.DamagesTerrain = on;
        }

        /// <summary>
        /// How many abilities a story creature has for a Combat Node's "perform Ability N" (P6152: a list of the
        /// creature's abilities): the creature prop's list (<see cref="Combat.CreatureAbilities.Sets"/>, from the
        /// wiki's creature pages
        /// and the client's texts, e.g. Barren Grunt 4: Slashing Blade 1-3 and Blade Lunge), at most the six actions the
        /// node has (577213-577216, 577243, 577244). A creature with no known list (spawner-name kinds: "Barren Melee"...)
        /// has its one attack (melee swing, spit, spore cloud or the blast), a Target Dummy none (L19).
        /// </summary>
        public static int StoryAbilityCount(ulong guid)
            => FindStory(guid) is Creature c ? AbilityCount(c.Kind) : 0;

        /// <summary>The <see cref="StoryAbilityCount"/> rule for a kind.</summary>
        private static int AbilityCount(Kind kind)
        {
            if (kind == null || kind.Style == Behaviour.Dummy || kind.Damage == 0 && kind.Style != Behaviour.Exploder)
                return 0;
            return Combat.CreatureAbilities.Of(kind.Name) is { } set ? Math.Min(set.Length, StoryTools.CombatAbilityActions.Length) : 1;
        }

        /// <summary>"Basic Attack" (565568): the one ability of a creature with no known list (as the player weapons' basic attack).</summary>
        public const uint BasicAttackText = 565568;

        /// <summary>
        /// The abilities of the creature a story spawner (prop <paramref name="propId"/>, PropDefs name
        /// <paramref name="spawnerName"/>) makes, in Combat Node order ("Ability 1" first), as name text ids, from the kind
        /// alone (no live creature needed: Edit Mode). Same count as <see cref="StoryAbilityCount"/>: the kind's list
        /// (<see cref="Combat.CreatureAbilities.Sets"/>, e.g. Barren Grunt 572981 / 577262 / 577263 / 572982 = Slashing
        /// Blade 1-3, Blade Lunge), else one <see cref="BasicAttackText"/>; none for a Target Dummy or a harmless kind. A 0
        /// entry is the nameless shield block (<see cref="Combat.CreatureAbilities.Role.Block"/>).
        /// </summary>
        public static uint[] StoryAbilityNames(uint propId, string spawnerName)
        {
            Kind kind = StoryKind(spawnerName, propId);
            int count = AbilityCount(kind);
            Combat.CreatureAbilities.Ability[] set = Combat.CreatureAbilities.Of(kind.Name);
            return Enumerable.Range(0, count).Select(i => set != null ? set[i].Name : BasicAttackText).ToArray();
        }

        /// <summary>
        /// How many abilities the creature of a story spawner prop has (<see cref="StoryAbilityNames"/>; the Combat Node's
        /// "perform Ability N" actions in Edit Mode).
        /// </summary>
        public static int StoryAbilityCount(uint propId, string spawnerName) => AbilityCount(StoryKind(spawnerName, propId));

        /// <summary>A shield block is up (<see cref="Combat.CreatureAbilities.Role.Block"/>): no damage gets through.</summary>
        private static bool Shielded(Creature c) => c.ShieldUntil != default && DateTime.UtcNow < c.ShieldUntil;

        /// <summary>
        /// A Combat Node made a story creature use ability <paramref name="n"/> now (TASKS 2.7, audit #55a). The ability is
        /// the creature's Nth (<see cref="Combat.CreatureAbilities"/>):
        /// <list type="bullet">
        /// <item>an attack (ranked or special): it turns on <paramref name="target"/> (the triggering player) or keeps its
        ///   current target, and that ability is its next attack, on its next AI step without waiting for its attack
        ///   interval (range, Combat Tactics movement and the ally rules still apply);</item>
        /// <item>Rejuvenate: a heal for its side now (the healers' heal, TryHeal), when someone is hurt;</item>
        /// <item>the shield block: immune to damage for <see cref="Combat.CreatureAbilities.BlockTime"/> [I];</item>
        /// <item>an exploder goes off (Grow-splode Dud: harmlessly [I]).</item>
        /// </list>
        /// A creature with no known list has one ability, its attack. False when it can't (no such ability, dead, nobody to
        /// attack, nobody to heal).
        /// </summary>
        public static bool StoryUseAbility(ulong guid, int n, WorldSession target)
        {
            Creature c = FindStory(guid);
            if (c == null || n < 1 || n > StoryAbilityCount(guid))
                return false;
            Combat.CreatureAbilities.Ability ability = Combat.CreatureAbilities.Get(c.Kind.Name, n);
            if (ability?.Role == Combat.CreatureAbilities.Role.Heal)
            {
                if (!Alive(c))
                    return false;
                c.NextHeal = default;
                return TryHeal(c, DateTime.UtcNow);
            }
            bool engaged = false;
            lock (sync)
            {
                if (!Alive(c))
                    return false;
                if (c.Kind.Style == Behaviour.Exploder)
                {
                    c.Dud = ability?.Role == Combat.CreatureAbilities.Role.Fizzle;
                    // Already armed or detonating: leave the fuse; Dud applies on detonate if set before arm.
                    if (c.FuseAt != default)
                        return true;
                    ArmExploder(c, null, Ahead(c), DateTime.UtcNow);
                    return true;
                }
                if (ability?.Role == Combat.CreatureAbilities.Role.Block)
                    c.ShieldUntil = DateTime.UtcNow + Combat.CreatureAbilities.BlockTime;
            }
            if (ability?.Role == Combat.CreatureAbilities.Role.Block)
            {
                // The other clients see the shield (entity state bit 45 + a 360° Block effect): their sweeps stop on it,
                // as the server lets no damage through (Combat.BlockStance).
                Combat.BlockStance.CreatureBlocked(c.Guid, Combat.CreatureAbilities.BlockTime);
                return true;
            }
            lock (sync)
            {
                if (!Alive(c))
                    return false;
                if (target != null && target.ZoneId == c.Zone && c.Faction != FactionAlly)
                {
                    engaged = c.State != State.Chase;
                    c.State = State.Chase;
                    c.Target = target;
                    c.Foe = null;
                    c.Anchor ??= AnchorOf(c);
                }
                else if (c.State != State.Chase)
                    return false;
                c.Pending = ability;
                c.NextAttack = DateTime.UtcNow;
            }
            if (engaged)
                StoryTools.CreatureCombat(c.Guid, true);
            return true;
        }

        /// <summary>A story creature's current faction (0 enemy, 1 neutral, 2 ally), or -1 when it isn't a live story creature.</summary>
        public static int StoryFactionOf(ulong guid)
        {
            Creature c = guid == 0 ? null : FindStory(guid);
            if (c == null)
                return -1;
            lock (sync)
                return c.Faction;
        }

        /// <summary>A story creature's faction: 0 enemy, 1 neutral, 2 ally (the last two leave players alone).</summary>
        public static void StoryFaction(ulong guid, int faction)
        {
            Creature c = FindStory(guid);
            if (c == null)
                return;
            bool was, changed;
            lock (sync)
            {
                was = Interactable(c);
                // A new side: old grudges go (a neutral forgets who provoked it); its current fight is re-checked on the
                // next AI step (MayFight / FoeValid) and ends if the new faction wouldn't have it.
                if (c.Faction != faction)
                    c.Threat.Clear();
                changed = c.Faction != faction;
                c.Faction = faction;
            }
            // Its viewers' clients learn the new side (nameplate colour, hostility; OPEN-LEADS L98). The full-data reply
            // (SendFull) can't carry it again — the client applies that only once per entity — so this is SetDisposition
            // (18 bytes), sent only on a real change and only to the players who have the creature. A viewer that hasn't
            // asked for the full data yet gets the new value in that reply too.
            if (changed)
            {
                var disposition = new PlayerUpdateSetDisposition { Guid = c.Guid, Disposition = DispositionOf(c) };
                foreach (WorldSession s in ViewersOf(c))
                    s.EnqueueMessage(disposition);
            }
            // Turned friendly with a click Link: its viewers may now interact (flagged; they ask for its info).
            if (!was && Interactable(c))
            {
                var flagging = new Network.Message.Model.DynamicInteractionFlagging();
                flagging.Guids.Add(c.Guid);
                foreach (WorldSession s in ViewersOf(c))
                    s.EnqueueMessage(flagging);
            }
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
        public static string StoryName(ulong guid) => FindStory(guid) is Creature c ? c.DisplayName ?? c.Kind.Name : null;

        /// <summary>Where a story creature is (null when gone).</summary>
        public static Vector3? StoryPosition(ulong guid) => FindStory(guid)?.Position;

        /// <summary>Puts a creature next to a player (dev/testing): a one-off camp member at the player's position.</summary>
        public static void SpawnNear(WorldSession session, float dx, float dz, string kindName = "kobold")
        {
            if (!kindsByName.TryGetValue(kindName ?? "kobold", out Kind kind))
                kind = DevKind(kindName) ?? KoboldMelee;
            Vector3 at = session.Position + new Vector3(dx, 0f, dz);
            uint zone = session.ZoneId;
            // On the surface: the ground there. Underground (well below it): the player's own height.
            if (TerrainHeights.HeightAt(at.X, at.Z, zone) is float ground && session.Position.Y > ground - 20f)
                at.Y = ground;
            long key = Pack(zone, (int)MathF.Floor(at.X / RegionSize), (int)MathF.Floor(at.Z / RegionSize));
            Creature c;
            lock (sync)
            {
                Creature[] existing = Region(zone, (int)MathF.Floor(at.X / RegionSize), (int)MathF.Floor(at.Z / RegionSize));
                c = new Creature
                {
                    Guid = GuidBase | ((ulong)key << 8) | (uint)(200 + existing.Length),
                    RegionKey = key,
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
