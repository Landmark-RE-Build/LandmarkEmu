using LandmarkEmulator.WorldServer.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Interest-range streaming of placed props (bandwidth audit 2026-10-18 R8; design note in
    /// trace/0695/audit-bandwidth-2026-10-18.md §R8). Until 2026-10-18 every placed prop of a landscape (≈250 B each:
    /// AddLightweightProp, its name, effect, story state and interaction flag) went to a player at login and again after
    /// <b>every</b> re-zone (travel, caves, the Spire, any long teleport): 1,000 props ≈ 250 KB per re-zone. Now a player
    /// is sent the props of the 64 m regions within <see cref="StreamRange"/> (setting PropStreamRange, Docker
    /// PROP_STREAM_RANGE, default 400 m), nearest region first, and the regions they walk into on the 1 s stream tick (at
    /// most <see cref="StreamBudget"/> props per tick); regions beyond the range + <see cref="StreamDropMargin"/> (480 m)
    /// are dropped again. 0 = the whole landscape, as before.
    /// <para>
    /// <b>Range [P for the client side, our choice for the number].</b> The client has no prop draw distance of its own:
    /// the far clip is the camera's far plane, min(the renderer's far plane, the user's Render Distance), FUN_142e5c4f0 →
    /// camera +0x284; the options slider maps 0-100 % to 120 + 2.81 × % metres (120 m at 0 %, ~400 m at 99 %) and 100 %
    /// (and the default -1) to 99999, i.e. no limit (SetRenderDistanceInPercent FUN_1435cfd10). 400 m is the slider's
    /// last bounded value; a player on "unlimited" sees props appear at 400 m, as trees and plants appear at 160 m
    /// (<see cref="Gathering"/>). Live's own server range is not known (no capture, no developer post found).
    /// </para>
    /// <para>
    /// <b>Dropping and re-sending [P].</b> A dropped prop gets the same removal as a picked-up one (<see cref="SendRemove"/>:
    /// its effect off, then PlayerUpdate RemovePlayer 0x26/1 with version 0: the 0.695 dispatcher FUN_142cdfa60 case 0
    /// looks the guid up in the entity table FUN_142f85390, which holds props, and removes the entity). Sent again later,
    /// AddLightweightProp → SpawnProxiedProp FUN_142f8a800 finds no entity for the guid and builds a new one (an entity
    /// still marked for deletion, +0x1457 bit 0x80, is flushed first: FUN_142d20da0 "Scan for stale Proxied Chars"). Trees
    /// and plants (<see cref="Gathering"/>) and Play Mode's hidden story tools (<see cref="PlayModeVisibilityChanged"/>)
    /// have used the same remove / re-spawn on the same handlers since 2026-10.
    /// </para>
    /// <para>
    /// <b>Bookkeeping.</b> Each landscape's placed props are listed by the 64 m region of their origin
    /// (<see cref="PlacedProp.StreamRegions"/>); a mover pack's parts are listed in the regions of <b>all</b> its parts, so
    /// a player who has one part has the whole pack (a Flinger throws the player at its target part, a platform rides to
    /// docks that may stand in the next region). Per player the server keeps the regions they hold and the props it has
    /// streamed to them (<see cref="PropStream.Streamed"/>: the interest set, Play Mode's hidden tools included). Every
    /// prop broadcast — placement, settled moves, pick-up, tint, name, on / off, sign text, Play Mode visibility, the
    /// Respawn Point's look, moving-platform movement — goes only to the players streaming that prop
    /// (<see cref="ViewersOf"/>); a placement to those holding its region.
    /// </para>
    /// <para>
    /// <b>Re-zones and moves.</b> ClientBeginZoning deletes every entity but the player's (zone dispatcher FUN_142d1ccf0
    /// case 8 → FUN_142d60340) [P], so the stream is forgotten when a re-zone goes out (<see cref="Teleport"/>.RezoneNow)
    /// and nothing is streamed to the loading client; an arrival over props sends the props near it early
    /// (<see cref="SendEarly"/>, MiscHandler.ClientIsReady) and <see cref="StreamIn"/> (PlayerManager.Reintroduce, after
    /// the zone-done flags) sends the rest of the range at once, nearest region first. A short move (plain
    /// TeleportToLocation) deletes nothing: the stream goes on and picks up the new regions once the arrival settles
    /// (<see cref="WorldSession.StreamsOnHold"/>, as trees and creatures).
    /// </para>
    /// <para>
    /// <b>Not changed.</b> The Spire's props and outposts are not placed props (their own streams). The landscape's
    /// trigger volumes (AreaDefinition SetDefinitions, ~135 B each) still go landscape-wide: a Linking Area carries no
    /// client area property (the Area Tool's Blank type 19 is left out, <see cref="AreaDefinitions"/>), its trigger is the
    /// server's (<see cref="StoryTools"/> via AreaDefinitions.BoxesOf), and the client needs its prop entity only to
    /// select it with the Area Tool (FUN_143739860: prop +0x240 area guid and a known area with one box), which a player
    /// does standing there. Zone-control indicators (InstancedGame PropState) live in the game manager's map by prop guid
    /// and are looked up when an entity is drawn (FUN_1434ec6b0 from FUN_143444620) [P], so a control point streamed in
    /// later still shows its label.
    /// </para>
    /// </summary>
    public static partial class Props
    {
        /// <summary>Region side (metres), as trees and plants (<see cref="Gathering"/>).</summary>
        private const float StreamRegionSize = 64f;

        /// <summary>Regions are dropped only this far beyond the send range (hysteresis: 400 → 480 m).</summary>
        private const float StreamDropMargin = 80f;

        /// <summary>
        /// Most props sent to one player per stream tick when they walk into new regions (whole regions, nearest first, so
        /// one region may go over it). Keeps a dense build area from becoming one burst on the reliable channel. The
        /// zone-in send (<see cref="StreamIn"/>) has no budget: it was the whole landscape at once before.
        /// </summary>
        private const int StreamBudget = 200;

        private static readonly TimeSpan StreamInterval = TimeSpan.FromSeconds(1);

        private static float? streamRangeOverride;

        /// <summary>
        /// Metres around a player whose regions' props are sent: setting PropStreamRange (default 400; 0 = every placed prop
        /// of the landscape, as before 2026-10-18), or the dev command "propstream" until the restart.
        /// </summary>
        public static float StreamRange
        {
            get => streamRangeOverride
                ?? Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config?.PropStreamRange ?? 400f;
            set => streamRangeOverride = Math.Max(0f, value);
        }

        /// <summary>One player's prop stream.</summary>
        private sealed class PropStream
        {
            /// <summary>The landscape it streams.</summary>
            public uint Zone;
            /// <summary>Regions held (their props are streamed); empty in whole-landscape mode.</summary>
            public readonly HashSet<long> Regions = new();
            /// <summary>Props streamed to this player (the interest set; Play Mode's hidden tools included).</summary>
            public readonly HashSet<ulong> Streamed = new();
            /// <summary>Whole-landscape mode (<see cref="StreamRange"/> 0): every placed prop of <see cref="Zone"/>.</summary>
            public bool All;
            /// <summary>
            /// <see cref="StreamIn"/> ran for this zone-in: the tick runs and new placements arrive. False in an arrival hold,
            /// where only <see cref="SendEarly"/>'s props are held.
            /// </summary>
            public bool Started;
            public DateTime LastTick;
        }

        private static readonly Dictionary<WorldSession, PropStream> streams = new();

        /// <summary>Region key → the placed props listed in it (<see cref="PlacedProp.StreamRegions"/>).</summary>
        private static readonly Dictionary<long, List<PlacedProp>> byRegion = new();

        /// <summary>Every prop by guid (placed or not), for <see cref="Find"/> and the stream's lookups.</summary>
        private static readonly Dictionary<ulong, PlacedProp> byGuid = new();

        // Region key: the landscape in the high 32 bits, region x and z in 16 bits each (±2,097 km at 64 m).
        private static long StreamKey(uint zone, int rx, int rz) => ((long)zone << 32) | ((long)(ushort)(short)rx << 16) | (ushort)(short)rz;

        private static (uint Zone, int Rx, int Rz) StreamKeyParts(long key) => ((uint)(key >> 32), (short)(ushort)(key >> 16), (short)(ushort)key);

        private static long OwnRegion(PlacedProp p)
            => StreamKey(p.Zone, (int)MathF.Floor(p.Spawn.Position.X / StreamRegionSize), (int)MathF.Floor(p.Spawn.Position.Z / StreamRegionSize));

        /// <summary>Horizontal distance from a point to the nearest edge of a region (0 inside it).</summary>
        private static float StreamRegionDistance(int rx, int rz, Vector3 p)
        {
            float cx = Math.Clamp(p.X, rx * StreamRegionSize, (rx + 1) * StreamRegionSize);
            float cz = Math.Clamp(p.Z, rz * StreamRegionSize, (rz + 1) * StreamRegionSize);
            return MathF.Sqrt((p.X - cx) * (p.X - cx) + (p.Z - cz) * (p.Z - cz));
        }

        /// <summary>Streamed at all: placed on a landscape (not still on its owner's cursor, not stowed).</summary>
        private static bool Streamable(PlacedProp p) => p.Placed && p.Zone != StowedZone;

        /// <summary>Lists a prop in its regions (none when <paramref name="keys"/> is null). Caller holds <see cref="sync"/>.</summary>
        private static void SetRegionsLocked(PlacedProp p, long[] keys)
        {
            if (p.StreamRegions != null)
                foreach (long key in p.StreamRegions)
                    if (byRegion.TryGetValue(key, out List<PlacedProp> list))
                    {
                        list.Remove(p);
                        if (list.Count == 0)
                            byRegion.Remove(key);
                    }
            p.StreamRegions = keys;
            if (keys != null)
                foreach (long key in keys)
                {
                    if (!byRegion.TryGetValue(key, out List<PlacedProp> list))
                        byRegion[key] = list = new List<PlacedProp>();
                    list.Add(p);
                }
        }

        /// <summary>
        /// (Re)lists a prop by where it stands now; a pack part takes its whole pack with it (each part is listed in the
        /// regions of every part). Caller holds <see cref="sync"/>.
        /// </summary>
        private static void IndexLocked(PlacedProp p)
        {
            if (p.PackGroup == 0)
            {
                SetRegionsLocked(p, Streamable(p) ? new[] { OwnRegion(p) } : null);
                return;
            }
            List<PlacedProp> mates = props.Where(m => m.PackGroup == p.PackGroup && Streamable(m)).ToList();
            long[] union = mates.Select(OwnRegion).Distinct().ToArray();
            foreach (PlacedProp m in mates)
                SetRegionsLocked(m, union);
            if (!Streamable(p))
                SetRegionsLocked(p, null);
        }

        /// <summary>Takes a prop out of the region lists (its pack mates are listed again without it). Caller holds <see cref="sync"/>.</summary>
        private static void UnindexLocked(PlacedProp p)
        {
            SetRegionsLocked(p, null);
            if (p.PackGroup != 0 && props.FirstOrDefault(m => m.PackGroup == p.PackGroup && m != p && Streamable(m)) is PlacedProp mate)
                IndexLocked(mate);
        }

        /// <summary>Lists every loaded prop (<see cref="LoadWorld"/>). Caller holds <see cref="sync"/>.</summary>
        private static void IndexAllLocked()
        {
            byRegion.Clear();
            foreach (PlacedProp p in props)
                p.StreamRegions = null;
            foreach (PlacedProp p in props.Where(p => p.PackGroup == 0))
                SetRegionsLocked(p, Streamable(p) ? new[] { OwnRegion(p) } : null);
            foreach (IGrouping<ulong, PlacedProp> pack in props.Where(p => p.PackGroup != 0).GroupBy(p => p.PackGroup))
            {
                long[] union = pack.Where(Streamable).Select(OwnRegion).Distinct().ToArray();
                foreach (PlacedProp m in pack)
                    SetRegionsLocked(m, Streamable(m) ? union : null);
            }
        }

        /// <summary>Whether a stream should hold a prop (its region is held, or the whole landscape). Caller holds <see cref="sync"/>.</summary>
        private static bool Holds(PropStream st, PlacedProp p)
        {
            if (p.StreamRegions == null)
                return false;
            if (st.All)
                return p.Zone == st.Zone;
            foreach (long key in p.StreamRegions)
                if (st.Regions.Contains(key))
                    return true;
            return false;
        }

        /// <summary>Whether the player's client has been streamed this prop (it may be hidden by Play Mode all the same).</summary>
        public static bool Streams(WorldSession session, ulong guid)
        {
            lock (sync)
                return streams.TryGetValue(session, out PropStream st) && st.Streamed.Contains(guid);
        }

        /// <summary>
        /// The players whose client has a prop: those it was streamed to; for a prop still on its owner's cursor, its owner
        /// (the only one it was spawned for, <see cref="BeginPlacement"/>).
        /// </summary>
        public static List<WorldSession> ViewersOf(PlacedProp prop)
        {
            if (!prop.Placed)
                return PlayerManager.InZone(prop.Zone).Where(s => s.PlayerGuid == prop.OwnerGuid).ToList();
            ulong guid = prop.Spawn.Guid;
            lock (sync)
                return streams.Where(kv => kv.Value.Streamed.Contains(guid)).Select(kv => kv.Key).ToList();
        }

        /// <summary>The players streaming a prop, by guid (empty for an unknown guid).</summary>
        public static List<WorldSession> ViewersOf(ulong guid) => Find(guid) is PlacedProp p ? ViewersOf(p) : new List<WorldSession>();

        /// <summary>Records that a player's client has a prop already (its owner, who placed it), without sending it.</summary>
        private static void MarkStreamed(WorldSession session, PlacedProp prop)
        {
            lock (sync)
                if (streams.TryGetValue(session, out PropStream st) && st.Zone == prop.Zone)
                    st.Streamed.Add(prop.Spawn.Guid);
        }

        /// <summary>
        /// A prop newly placed (already listed, <see cref="IndexLocked"/>): spawned for every player holding its region, but
        /// <paramref name="except"/> (who has it already).
        /// </summary>
        private static void Show(PlacedProp prop, WorldSession except = null)
        {
            var to = new List<WorldSession>();
            lock (sync)
                foreach ((WorldSession s, PropStream st) in streams)
                    if (s != except && st.Started && Holds(st, prop) && st.Streamed.Add(prop.Spawn.Guid))
                        to.Add(s);
            foreach (WorldSession s in to)
                SendSpawns(s, new[] { prop });
        }

        /// <summary>
        /// A placed prop changed place (a settled move, a Linking Area's box, a pack part moved): listed again, then every
        /// player is brought in line — removed for those who no longer hold it, spawned for those who now do, and with
        /// <paramref name="respawn"/> removed and spawned again (in its new place) for those who keep it, but
        /// <paramref name="mover"/> (whose own client moved it).
        /// </summary>
        private static void Reconcile(PlacedProp prop, bool respawn, WorldSession mover = null)
        {
            var again = new List<WorldSession>();
            var hide = new List<WorldSession>();
            var show = new List<WorldSession>();
            ulong guid = prop.Spawn.Guid;
            lock (sync)
            {
                IndexLocked(prop);
                foreach ((WorldSession s, PropStream st) in streams)
                {
                    if (st.Zone != prop.Zone)
                        continue;
                    bool had = st.Streamed.Contains(guid), should = st.Started && Holds(st, prop);
                    if (had && should)
                    {
                        if (respawn && s != mover)
                            again.Add(s);
                    }
                    else if (had)
                    {
                        st.Streamed.Remove(guid);
                        hide.Add(s);
                    }
                    else if (should)
                    {
                        st.Streamed.Add(guid);
                        show.Add(s);
                    }
                }
            }
            foreach (WorldSession s in hide)
                SendRemove(s, prop);
            foreach (WorldSession s in again)
                SendRemove(s, prop);
            foreach (WorldSession s in again.Concat(show))
                SendSpawns(s, new[] { prop });
        }

        /// <summary>A prop left the world (or its landscape): removed for every player streaming it, and unlisted.</summary>
        private static void Hide(PlacedProp prop)
        {
            List<WorldSession> to;
            ulong guid = prop.Spawn.Guid;
            lock (sync)
            {
                to = streams.Where(kv => kv.Value.Streamed.Remove(guid)).Select(kv => kv.Key).ToList();
                UnindexLocked(prop);
            }
            if (!prop.Placed)
                to = to.Union(ViewersOf(prop)).ToList();
            foreach (WorldSession s in to)
                SendRemove(s, prop);
        }

        /// <summary>Removes a prop for one player and spawns it again (a placed prop going back on their cursor).</summary>
        private static void Respawn(WorldSession session, PlacedProp prop)
        {
            SendRemove(session, prop);
            SendSpawns(session, new[] { prop });
            MarkStreamed(session, prop);
        }

        /// <summary>
        /// Zone-in (login, PlayerManager.Enter) and after every re-zone (PlayerManager.Reintroduce): every prop within
        /// <see cref="StreamRange"/> of the player that their client doesn't have yet (an arrival hold may have sent some,
        /// <see cref="SendEarly"/>), nearest region first, in one go as the whole landscape was before; then the stream tick
        /// takes over. Range 0: every placed prop of the landscape.
        /// </summary>
        public static void StreamIn(WorldSession session)
        {
            var spawns = new List<PlacedProp>();
            uint zone = session.ZoneId;
            Vector3 at = session.Position;
            float range = StreamRange;
            int regions = 0;
            lock (sync)
            {
                PropStream st = StreamOf(session);
                st.Started = true;
                st.LastTick = DateTime.UtcNow;
                if (range <= 0f)
                {
                    st.All = true;
                    st.Regions.Clear();
                    foreach (PlacedProp p in props)
                        if (p.Zone == zone && p.StreamRegions != null && st.Streamed.Add(p.Spawn.Guid))
                            spawns.Add(p);
                }
                else
                {
                    st.All = false;
                    foreach ((long key, float _) in RegionsWithin(zone, at, range).OrderBy(r => r.Distance))
                    {
                        st.Regions.Add(key);
                        regions++;
                        if (byRegion.TryGetValue(key, out List<PlacedProp> list))
                            foreach (PlacedProp p in list)
                                if (st.Streamed.Add(p.Spawn.Guid))
                                    spawns.Add(p);
                    }
                }
            }
            SendSpawns(session, spawns);
            log.Debug($"{session.CharacterName}: {spawns.Count} prop(s) streamed in ({(range <= 0f ? "whole landscape" : $"{regions} region(s) within {range:0} m")}).");
        }

        /// <summary>
        /// The arrival hold (MiscHandler.ClientIsReady, an arrival in the air over props) and the loading-screen stream hold
        /// (<see cref="SendArrival"/>): these props now, ahead of the rest; <see cref="StreamIn"/> later skips them. Returns
        /// how many went out.
        /// </summary>
        public static int SendEarly(WorldSession session, IEnumerable<PlacedProp> near)
        {
            var spawns = new List<PlacedProp>();
            lock (sync)
            {
                PropStream st = StreamOf(session);
                foreach (PlacedProp p in near)
                    if (p.StreamRegions != null && p.Zone == st.Zone && st.Streamed.Add(p.Spawn.Guid))
                        spawns.Add(p);
            }
            SendSpawns(session, spawns);
            return spawns.Count;
        }

        /// <summary>
        /// Whether the player's prop stream is running (<see cref="StreamIn"/> ran for this zone-in), else only the early
        /// props are held (<see cref="SendEarly"/>) or there is no stream.
        /// </summary>
        public static bool StreamStarted(WorldSession session)
        {
            lock (sync)
                return streams.TryGetValue(session, out PropStream st) && st.Started && st.Zone == session.ZoneId;
        }

        /// <summary>Sideways reach (m) of the props sent ahead of the rest around an arrival (64 m down, 16 m up)
        /// [I ours: the structure the player lands on, with margin].</summary>
        public const float ArrivalReach = 48f;

        /// <summary>
        /// The placed props under and around where the player stands (<see cref="ArrivalReach"/> sideways, 64 m down, 16 m
        /// up), ahead of the rest: an arrival over props (MiscHandler.ClientIsReady) and the loading-screen stream hold
        /// (PlayerManager "Stream hold"), so the player has collision under them. <see cref="StreamIn"/> later skips them.
        /// Returns how many went out.
        /// </summary>
        public static int SendArrival(WorldSession session)
        {
            Vector3 at = session.Position;
            List<PlacedProp> near = PlacedInBox(session.ZoneId, at - new Vector3(ArrivalReach, 64f, ArrivalReach),
                at + new Vector3(ArrivalReach, 16f, ArrivalReach));
            return SendEarly(session, near);
        }

        /// <summary>
        /// The player's client has no prop any more (a re-zone went out: ClientBeginZoning deletes every entity but the
        /// player's) or the player left: their stream starts again from nothing.
        /// </summary>
        public static void Forget(WorldSession session)
        {
            lock (sync)
                streams.Remove(session);
        }

        /// <summary>The player's stream, made fresh when there is none or it is for another landscape. Caller holds <see cref="sync"/>.</summary>
        private static PropStream StreamOf(WorldSession session)
        {
            uint zone = session.ZoneId;
            if (!streams.TryGetValue(session, out PropStream st) || st.Zone != zone)
                streams[session] = st = new PropStream { Zone = zone };
            return st;
        }

        /// <summary>The region keys whose nearest point lies within <paramref name="range"/> metres of a point, with that distance.</summary>
        private static IEnumerable<(long Key, float Distance)> RegionsWithin(uint zone, Vector3 at, float range)
        {
            int r0x = (int)MathF.Floor((at.X - range) / StreamRegionSize), r1x = (int)MathF.Floor((at.X + range) / StreamRegionSize);
            int r0z = (int)MathF.Floor((at.Z - range) / StreamRegionSize), r1z = (int)MathF.Floor((at.Z + range) / StreamRegionSize);
            for (int rz = r0z; rz <= r1z; rz++)
                for (int rx = r0x; rx <= r1x; rx++)
                {
                    float d = StreamRegionDistance(rx, rz, at);
                    if (d <= range)
                        yield return (StreamKey(zone, rx, rz), d);
                }
        }

        /// <summary>The stream tick (<see cref="Update"/>): each started stream at most every <see cref="StreamInterval"/>.</summary>
        private static void StreamUpdate()
        {
            if (DevCommands.NoStreaming)
                return;
            DateTime now = DateTime.UtcNow;
            List<(WorldSession Session, PropStream Stream)> due;
            lock (sync)
            {
                if (streams.Count == 0)
                    return;
                due = new List<(WorldSession, PropStream)>();
                foreach ((WorldSession s, PropStream st) in streams)
                    if (st.Started && now - st.LastTick >= StreamInterval)
                    {
                        st.LastTick = now;
                        due.Add((s, st));
                    }
            }
            foreach ((WorldSession session, PropStream st) in due)
            {
                // Not while the client is loading or arriving (as trees and creatures; WorldSession.StreamsOnHold), and not
                // for a stream of another landscape (a re-zone forgets it; this is a guard).
                if (session.Disconnected || session.StreamsOnHold || session.ZoneId != st.Zone)
                    continue;
                StreamTick(session, st);
            }
        }

        /// <summary>
        /// One player's tick: drops regions beyond the range + <see cref="StreamDropMargin"/>, adds the regions within the
        /// range, nearest first, up to <see cref="StreamBudget"/> props, and removes any streamed prop it no longer holds
        /// (dropped regions, props moved away, a pack that shrank).
        /// </summary>
        private static void StreamTick(WorldSession session, PropStream st)
        {
            var spawns = new List<PlacedProp>();
            var removals = new List<PlacedProp>();
            Vector3 at = session.Position;
            float range = StreamRange;
            lock (sync)
            {
                if (range <= 0f)
                {
                    // Whole landscape: new placements reach the player through Show; only a switch from a range needs work.
                    if (st.All)
                        return;
                    st.All = true;
                    st.Regions.Clear();
                    foreach (PlacedProp p in props)
                        if (p.Zone == st.Zone && p.StreamRegions != null && st.Streamed.Add(p.Spawn.Guid))
                            spawns.Add(p);
                }
                else
                {
                    // Coming from whole-landscape mode (dev "propstream"), every prop is streamed already: the regions in
                    // range are all taken at once, so the check below doesn't drop and re-send the ones past the budget.
                    int budget = st.All ? int.MaxValue : StreamBudget;
                    st.All = false;
                    float drop = range + StreamDropMargin;
                    st.Regions.RemoveWhere(key =>
                    {
                        (uint zone, int rx, int rz) = StreamKeyParts(key);
                        return zone != st.Zone || StreamRegionDistance(rx, rz, at) > drop;
                    });
                    int sent = 0;
                    foreach ((long key, float _) in RegionsWithin(st.Zone, at, range).Where(r => !st.Regions.Contains(r.Key)).OrderBy(r => r.Distance).ToList())
                    {
                        if (sent >= budget)
                            break;
                        st.Regions.Add(key);
                        if (byRegion.TryGetValue(key, out List<PlacedProp> list))
                            foreach (PlacedProp p in list)
                                if (st.Streamed.Add(p.Spawn.Guid))
                                {
                                    spawns.Add(p);
                                    sent++;
                                }
                    }
                }
                foreach (ulong guid in st.Streamed.ToList())
                {
                    if (!byGuid.TryGetValue(guid, out PlacedProp p) || p.StreamRegions == null)
                    {
                        st.Streamed.Remove(guid);   // gone or stowed: its removal went out then (Hide)
                        continue;
                    }
                    if (Holds(st, p))
                        continue;
                    st.Streamed.Remove(guid);
                    removals.Add(p);
                }
            }
            foreach (PlacedProp p in removals)
                SendRemove(session, p);
            if (spawns.Count > 0)
                SendSpawns(session, spawns);
            if (spawns.Count > 0 || removals.Count > 0)
                log.Debug($"{session.CharacterName}: props streamed {spawns.Count} in, {removals.Count} out.");
        }

        /// <summary>Dev "propstream": a player's stream (range, regions held, props streamed) for the log.</summary>
        public static string StreamState(WorldSession session)
        {
            lock (sync)
            {
                int onLandscape = props.Count(p => p.Zone == session.ZoneId && p.StreamRegions != null);
                if (!streams.TryGetValue(session, out PropStream st))
                    return $"range {StreamRange:0} m, no stream (re-zoning or not in the world), {onLandscape} placed prop(s) on landscape {session.ZoneId}";
                return $"range {StreamRange:0} m{(st.All ? " (whole landscape)" : "")}, {(st.Started ? "started" : "arrival hold")}, "
                    + $"{st.Regions.Count} region(s) held, {st.Streamed.Count} of {onLandscape} placed prop(s) on landscape {st.Zone} streamed";
            }
        }
    }
}
