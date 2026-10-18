using LandmarkEmulator.WorldServer.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Temporary voxel destruction on build sites (0.695; TASKS 2.1 bit 4, 2.6, C.6 / functions-695.md B7).
    /// <para>
    /// The rules, from the patch notes (design/patchnotes-after-0589.md; P-numbers trace/0695/patchnotes.tsv):
    /// <list type="bullet">
    /// <item>Voxel destruction means only temporary things like dynamite, while building with tools stays under Site
    /// Permissions (P5867): the destruction tools are the temporary ones — High Yield Dynamite. Picks and other
    /// gathering tools stay refused on every site: harvesting on a site was still impossible (30 Jun 2016 thread,
    /// beside P6012).</item>
    /// <item>In Play Mode, visitors may by default use grappling hooks, boot abilities and destruction tools such as
    /// dynamite on a site, and the Play Mode Rules can take any of them away (P5969): allowed in Play Mode unless the
    /// site's Play Mode Rules set bit 4 (<see cref="Claims.AdventureBlockVoxelDestruction"/>).</item>
    /// <item>Dynamite destruction stays temporary and heals after a short while (P6006), the same way as anywhere else,
    /// 100 seconds for dynamite at the time (P6037); each effect has its own duration, which site owners can't change
    /// (P5878 thread): <see cref="DynamiteHeal"/>, everywhere.</item>
    /// <item>Linking Areas: an area can set destruction apart from the site's setting, following it by default or
    /// allowing or blocking destruction inside itself (P6183, row 102 <see cref="PropSettings.AreaVoxelPermission"/>);
    /// a Link can target a Linking Area to destroy (or heal) inside it (P6185; <see cref="AreaAction"/>).</item>
    /// </list>
    /// </para>
    /// <para>
    /// The 0.695 client does not check bit 4 itself (unlike grapple / boot / camera bits): a refused destruction is
    /// reported by the server as Abilities EffectEnd with result <see cref="ResultNotAllowed"/> 9, which only the 0.695
    /// handler knows (FUN_142e18c70 case 9 → "ClaimDestruction.Fail.NotAllowed"; 0.589 FUN_142d7aca0 knew 2-8)
    /// [P: trace/0695/functions-695.md §4 item 7, B7]. It is sent with <see cref="Gathering.HarvestFail"/>, the same
    /// EffectEnd body (+0x38 = the local player, fraction 0, reason at +0x50).
    /// </para>
    /// <para>
    /// <b>Inferred</b> (not stated anywhere; TASKS 2.6 / 2.1 open questions): sites in Edit Mode refuse destruction (the
    /// rule is a Play Mode rule) with the pre-0.695 answer 8 (Harvest.Fail.NotAllowed); where Linking Areas overlap,
    /// Blocked wins over Allowed; dynamite on a site collects nothing (no harvesting on sites).
    /// </para>
    /// </summary>
    public static class VoxelDestruction
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>Abilities EffectEnd result 9 "ClaimDestruction.Fail.NotAllowed" (0.695 FUN_142e18c70).</summary>
        public const uint ResultNotAllowed = 9;

        /// <summary>Dynamite's destruction heals after 100 seconds (P6037), on sites and everywhere else.</summary>
        public static readonly TimeSpan DynamiteHeal = TimeSpan.FromSeconds(100);

        /// <summary>
        /// WorldEditingEffect composites (ActorCompositeEffectDefinitions; StringHashToValue, same in 0.589 / 0.695):
        /// Volume_Delete 10541 (particle 10685 + sound 10340) for a destroy, Volume_Heal 10532 (particle 10677) for a heal.
        /// <b>Inferred</b> pair: live's ids are not captured (trace/0695/leads-L33-L35-L36.md L36).
        /// </summary>
        public const uint VolumeDeleteEffect = 10541, VolumeHealEffect = 10532;

        /// <summary>Who sees a WorldEditingPlayEffect: 0.695 players within this distance of the area (as build cursors, L36).</summary>
        private const float EffectRange = 160f;

        /// <summary>At most one effect per area and kind this often (L36 recommendation: coalesce, bandwidth).</summary>
        private static readonly TimeSpan EffectInterval = TimeSpan.FromSeconds(1);

        private static readonly object sync = new();
        private static readonly Dictionary<(ulong Area, uint Effect), DateTime> lastEffect = new();

        /// <summary>What may happen to a voxel at a point.</summary>
        public enum Verdict
        {
            /// <summary>Outside every build site: destruction works as it always did.</summary>
            Outside,
            /// <summary>On a Play Mode site where destruction is allowed (site rule or a Linking Area set to Allowed).</summary>
            Allowed,
            /// <summary>On a site in Edit Mode (inferred: refused, answer 8).</summary>
            EditMode,
            /// <summary>On a Play Mode site where the Play Mode Rules or a Linking Area block destruction (answer 9).</summary>
            Blocked
        }

        /// <summary>A Linking Area with its own row 102 choice, in global dm (the InArea cylinder).</summary>
        private readonly record struct Area(double X, double Y, double Z, double Radius, double HalfHeight, uint Rule)
        {
            public bool Holds(double x, double y, double z)
                => (x - X) * (x - X) + (z - Z) * (z - Z) <= Radius * Radius && Math.Abs(y - Y) <= HalfHeight;
        }

        /// <summary>The sites touching a volume with their Linking Areas, read once before a destruction.</summary>
        private sealed class Rules
        {
            public readonly List<(Claims.DestructionSite Site, List<Area> Areas)> Sites = new();

            public Verdict At(double x, double y, double z)
            {
                foreach ((Claims.DestructionSite site, List<Area> areas) in Sites)
                {
                    if (!site.Contains(x, y, z))
                        continue;
                    if (!site.Playing)
                        return Verdict.EditMode;
                    bool allowed = !site.RuleBlocks;
                    bool inAllowed = false;
                    foreach (Area a in areas)
                        if (a.Holds(x, y, z))
                        {
                            if (a.Rule == PropSettings.AreaDestructionBlocked)
                                return Verdict.Blocked;   // inferred: Blocked wins where areas overlap
                            inAllowed |= a.Rule == PropSettings.AreaDestructionAllowed;
                        }
                    return allowed || inAllowed ? Verdict.Allowed : Verdict.Blocked;
                }
                return Verdict.Outside;
            }
        }

        /// <summary>
        /// Reads the sites touching a global dm box (Claims) and the Linking Areas of the playing ones (StoryTools). Done
        /// outside the terrain store's lock, so the predicates built from it take no locks.
        /// </summary>
        private static Rules Read(uint zone, (double X, double Y, double Z) min, (double X, double Y, double Z) max)
        {
            var rules = new Rules();
            foreach (Claims.DestructionSite site in Claims.DestructionSitesIn(zone, min, max))
            {
                var areas = new List<Area>();
                if (site.Playing)
                    foreach ((Vector3 centre, float radius, float halfHeight, uint rule) in StoryTools.DestructionAreas(site.Id))
                    {
                        var g = Terrain.TerrainEdits.ToGlobal(centre);
                        areas.Add(new Area(g.X, g.Y, g.Z, radius * 10.0, halfHeight * 10.0, rule));
                    }
                rules.Sites.Add((site, areas));
            }
            return rules;
        }

        /// <summary>The crater of a story Fiery Fungus / Proximity Mine (decimetres; ours, a bit smaller than dynamite's).</summary>
        public const double CreatureBlastRadius = 20;

        /// <summary>
        /// A story Fiery Fungus or Proximity Mine went off on a build site: Fiery Fungi and Proximity Mines damage the
        /// terrain on build sites (29 Aug 2016; TASKS 2.15) when its spawner's "Damages Terrain" (row 105
        /// npc_abilities_damage_terrain, default on) is set. Temporary like dynamite (heals after <see cref="DynamiteHeal"/>,
        /// inferred) and only where the site's Play Mode Rules and Linking Areas allow destruction; nothing is collected.
        /// Returns the voxels destroyed.
        /// </summary>
        public static int CreatureBlast(uint zone, Vector3 at)
        {
            var g = Terrain.TerrainEdits.ToGlobal(at);
            if (At(zone, g.X, g.Y, g.Z) != Verdict.Allowed)
                return 0;
            double r = CreatureBlastRadius * 1.1;
            Terrain.VoxelFilter allowed = MaskFor(zone, (g.X - r, g.Y - r, g.Z - r), (g.X + r, g.Y + r, g.Z + r));
            (List<ulong> cells, int voxels) = Terrain.EnvironmentServer.Instance.Sphere(at, CreatureBlastRadius, 0, zone,
                allowed: allowed, carveBuilds: true, healDelay: DynamiteHeal);
            if (cells.Count > 0)
                Terrain.ZoneTerrain.Broadcast(cells, zone);
            return voxels;
        }

        /// <summary>What may happen at a global dm point (one lookup; <see cref="Abilities.Excavate"/>'s target check).</summary>
        public static Verdict At(uint zone, double x, double y, double z)
            => Read(zone, (x, y, z), (x, y, z)).At(x, y, z);

        /// <summary>
        /// A per-voxel filter (global dm voxel centre → may it be destroyed?) for a temporary destruction (dynamite)
        /// touching a global dm box: true outside sites and where <see cref="Verdict.Allowed"/>. Null when no site touches
        /// the box (no filter needed). Built as data (<see cref="Terrain.VoxelFilter"/>, so it can travel to a remote
        /// Environment Server; terrain-service.md §5.5): per site its box, allowed = Play Mode and not blocked by the rules,
        /// and its Linking Areas set to Allowed / Blocked — <see cref="Rules.At"/>'s verdicts as a yes / no.
        /// </summary>
        public static Terrain.VoxelFilter MaskFor(uint zone, (double X, double Y, double Z) min, (double X, double Y, double Z) max)
        {
            Rules rules = Read(zone, min, max);
            if (rules.Sites.Count == 0)
                return null;
            var filter = new Terrain.VoxelFilter();
            foreach ((Claims.DestructionSite site, List<Area> areas) in rules.Sites)
                filter.Regions.Add(new Terrain.VoxelFilter.Region(site.MinX, site.MinY, site.MinZ, site.MaxX, site.MaxY, site.MaxZ,
                    site.Playing && !site.RuleBlocks,
                    !site.Playing ? new List<Terrain.VoxelFilter.Area>()
                        : areas.Where(a => a.Rule == PropSettings.AreaDestructionBlocked || a.Rule == PropSettings.AreaDestructionAllowed)
                            .Select(a => new Terrain.VoxelFilter.Area(a.X, a.Y, a.Z, a.Radius, a.HalfHeight, a.Rule == PropSettings.AreaDestructionAllowed))
                            .ToList()));
            return filter;
        }

        /// <summary>
        /// A per-voxel filter for gathering tools (picks, pulverizers): true only outside every site, so a swing at a
        /// site's edge does not dig into it (wiki Gathering: nothing can be gathered inside any build site's boundary).
        /// Null when no site touches the box.
        /// </summary>
        public static Terrain.VoxelFilter OutsideSites(uint zone, (double X, double Y, double Z) min, (double X, double Y, double Z) max)
        {
            List<Claims.DestructionSite> sites = Claims.DestructionSitesIn(zone, min, max);
            if (sites.Count == 0)
                return null;
            var filter = new Terrain.VoxelFilter();
            foreach (Claims.DestructionSite s in sites)
                filter.Regions.Add(new Terrain.VoxelFilter.Region(s.MinX, s.MinY, s.MinZ, s.MaxX, s.MaxY, s.MaxZ, false,
                    Array.Empty<Terrain.VoxelFilter.Area>()));
            return filter;
        }

        /// <summary>
        /// Refuses a destruction the client's way: Abilities EffectEnd with the result for <paramref name="verdict"/> —
        /// 9 "ClaimDestruction.Fail.NotAllowed" for <see cref="Verdict.Blocked"/> (0.695 only), 8 "Harvest.Fail.NotAllowed"
        /// for <see cref="Verdict.EditMode"/> (inferred, the pre-0.695 answer).
        /// </summary>
        public static void Refuse(WorldSession session, Verdict verdict)
        {
            uint result = verdict == Verdict.Blocked ? ResultNotAllowed : Gathering.HarvestNotAllowed;
            Gathering.HarvestFail(session, result);
            log.Info($"{session.CharacterName}: voxel destruction refused ({verdict}, EffectEnd result {result}).");
        }

        /// <summary>
        /// A Linking Area's "...Destroy Voxels in this area." (577548) or "...Heal Voxels in this area." (577549) on its
        /// cylinder (<paramref name="centre"/> local metres, radius and half height in metres: StoryTools.InArea).
        /// <para>
        /// Destroy writes air over the building voxels in the destruction layer, healing after 10000 s (P6203; see
        /// Terrain.TerrainEdits.DestroyArea), only where destruction is allowed — the area's own row 102, else the site's
        /// Play Mode Rules (<b>inferred</b> from trace/0695/storytools-695.md §6.5 "honouring row 102" and L36; the patch
        /// notes do not say whether a Link-made destruction obeys the rules meant for players' tools). Heal clears the
        /// destruction layer in the area (TerrainEdits.HealArea) and is never refused.
        /// </para>
        /// <para>
        /// Changed cells go to the players in range as usual (ZoneTerrain.Broadcast); when voxels changed, the 0.695 players
        /// near the area get a WorldEditingPlayEffect (<see cref="PlayEffect"/>, Volume_Delete / Volume_Heal, L36).
        /// </para>
        /// </summary>
        public static void AreaAction(ulong claimId, uint zone, ulong areaGuid, Vector3 centre, float radius, float halfHeight, bool destroy)
        {
            var g = Terrain.TerrainEdits.ToGlobal(centre);
            double r = radius * 10.0, h = halfHeight * 10.0;
            Terrain.IEnvironmentServer env = Terrain.EnvironmentServer.Instance;
            List<ulong> cells;
            int voxels;
            if (destroy)
            {
                Terrain.VoxelFilter mask = MaskFor(zone, (g.X - r, g.Y - h, g.Z - r), (g.X + r, g.Y + h, g.Z + r));
                (cells, voxels) = env.DestroyArea(zone, g, r, h, mask);
            }
            else
                (cells, voxels) = env.HealArea(zone, g, r, h);
            log.Info($"Linking Area 0x{areaGuid:X} on site {claimId}: {(destroy ? "destroyed" : "healed")} {voxels} voxel(s) in {cells.Count} cell(s).");
            if (cells.Count == 0)
                return;
            Terrain.ZoneTerrain.Broadcast(cells, zone);
            var extent = new Vector3(radius, halfHeight, radius);
            PlayEffect(zone, areaGuid, centre - extent, centre + extent, destroy ? VolumeDeleteEffect : VolumeHealEffect);
        }

        /// <summary>
        /// WorldEditingPlayEffectPacket 0x10C / 8 (S→C) [P: trace/0695/leads-L33-L35-L36.md L36, packets-695.md §11]:
        /// u16 0x010C, u32 sub 8, u32 header value (the client fills it with its synced clock in its own 0x10C packets; no
        /// S→C reader uses it: the server game clock ms is sent), u32 composite effect id, vec4 min, vec4 max (local
        /// metres, w = 1) — 46 bytes. The 0.695 client (reader FUN_14307c4d0) plays the composite once at the box centre
        /// ((min + max) / 2), unscaled, unless the viewer turned off "Show Previews of Others' Build Actions" (UserOptions
        /// DemoModeOthers, dispatcher gate +0x153b; the server cannot see that option, sending is harmless).
        /// Sent raw (opcode included) to players within <see cref="EffectRange"/> of the centre,
        /// not mid re-zone, at most once a second per area and effect.
        /// </summary>
        public static void PlayEffect(uint zone, ulong areaGuid, Vector3 min, Vector3 max, uint effect)
        {
            DateTime now = DateTime.UtcNow;
            lock (sync)
            {
                if (lastEffect.TryGetValue((areaGuid, effect), out DateTime last) && now - last < EffectInterval)
                    return;
                lastEffect[(areaGuid, effect)] = now;
                if (lastEffect.Count > 1024)
                    foreach (var old in lastEffect.Where(e => now - e.Value > EffectInterval).Select(e => e.Key).ToList())
                        lastEffect.Remove(old);
            }
            byte[] packet = new byte[46];
            int o = 0;
            void U16(ushort v) { BitConverter.GetBytes(v).CopyTo(packet, o); o += 2; }
            void U32(uint v) { BitConverter.GetBytes(v).CopyTo(packet, o); o += 4; }
            void F32(float v) { BitConverter.GetBytes(v).CopyTo(packet, o); o += 4; }
            U16(0x010C);
            U32(8);
            U32((uint)ServerClock.Milliseconds);
            U32(effect);
            F32(min.X); F32(min.Y); F32(min.Z); F32(1f);
            F32(max.X); F32(max.Y); F32(max.Z); F32(1f);
            Vector3 centre = (min + max) * 0.5f;
            int sent = 0;
            foreach (WorldSession viewer in PlayerManager.InZone(zone))
                if (viewer.RezoneTarget == null && !viewer.Disconnected
                    && Vector3.Distance(viewer.Position, centre) <= EffectRange)
                {
                    viewer.SendRaw(packet);
                    sent++;
                }
            log.Debug($"WorldEditingPlayEffect {effect} at {centre} to {sent} player(s).");
        }
    }
}
