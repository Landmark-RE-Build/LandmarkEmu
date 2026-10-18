using LandmarkEmulator.WorldServer.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Moving platforms (TASKS E.1, OPEN-LEADS L135; research trace/0695/agent-e-leads.md §1): the Magical Platform
    /// (pack 5: platform 1193 + two docks 1192), the Three dock moving platform (pack 6: 1193 + three 1192) and the Glass
    /// Platform (pack 8: 1421 + two docks 1422) travel between their docks, rest at each, and go on.
    ///
    /// Why the server moves them [P]: the platform's and the docks' area properties are AreaPropertyDataMovingObject
    /// (type 28, vtable 0x14496bd18) and AreaPropertyDataMovingObjectWaypoint (type 27, 0x14496bcb8). The client has those
    /// data classes but no ClientArea… class for either (the factory FUN_1431ae4f0), so it loads the settings and never
    /// runs a moving object. The 0.695 client has no platform-relative position system either: 0xB2
    /// PlayerUpdatePositionOnPlatform is only a registry name (no class, no builder; the zone dispatcher FUN_142d1ccf0
    /// drops it), and Rails.txt / RailPoints.txt are empty.
    ///
    /// The wire [P]: the ordinary <b>PlayerUpdatePosition 0x7F</b> for the platform's transient id. Its handler PlayClient
    /// vfunc +0x208 = FUN_142ce6340 finds the entity by transient id (FUN_142f85340), applies the movement (vfunc +0xD0),
    /// and when the entity is a placed prop (vfunc +0x108 AsProxiedProp: FUN_1430a8d90 returns the prop) sets
    /// ProxiedProp +0x4F00 = 1 ("moved"). The ProxiedProp update FUN_1430ab000 then re-registers the prop's collision
    /// (FUN_1430ac390 → FUN_1430a82a0) when that flag is set <b>and</b> its last refresh (+0x4F08) is none or more than
    /// 1000 ms ago. So the model moves as smoothly as a creature, but <b>its collision follows at most once a second</b> —
    /// whether a player standing on it rides along is the runtime question of L135 (see the report's test steps).
    ///
    /// Settings [P]: the platform (area 14: AreaProperties id 48, type 28) and each dock (area 15: id 45, type 27, plus
    /// id 50 type 29 MovingObjectParamEditProxy, the dock's settings window) carry two EditableProperties:
    /// <c>moving_platform_speed</c> (row 8, "Speed Adjustment" 50121, help 564600 "The speed at which the platform will
    /// travel."; default 160, 50-1000 step 5) and <c>moving_platform_pause_delay</c> (row 7, "Delay Adjustment" 564551,
    /// help 564599 "The time in seconds that the platform will rest once it reaches a dock."; default 2, 0-30). The data
    /// class's setting slot FUN_143db4060 stores key 0x321630f0 as value × 0.01 (float DAT_144c4103c) and key 0x30725695
    /// as an int; the keys are the one-at-a-time hashes of those names (<see cref="PropSettings.KeyHash"/>; L134). So the
    /// speed is 160 × 0.01 = <b>1.6 m/s</b> by default (0.5-10 m/s) [P for the factor; I that the unit is m/s] and the
    /// pause <b>2 whole seconds</b> [P: the help text]. The values are the props' saved settings (world_prop_setting,
    /// <see cref="PropSettings"/>): a dock's own saved value governs the pause at that dock and the leg that leaves it
    /// [I: why else would each waypoint carry both], else the platform's, else the default.
    ///
    /// The path [I]: the platform rests at each dock "flush" with it (P1898 fixed "a gap between the Moving Platform and
    /// their docks"): <see cref="DockStopDistance"/> in front of the dock, where the front is the dock's own +X turned by
    /// its rotation (the docks of each pack face the pack's middle: headings 0 / 180 / 90 at offsets −5 / +5 / z +5,
    /// PlacementPackProps.txt). Its turn at a dock is the dock's rotation without the dock's pack heading, and it turns
    /// smoothly between docks ("you can even rotate the ends in any axis, which in turn rotates the pad as it moves!",
    /// P1537). Two docks: there and back; three: in PROP_INDEX order and back (1, 2, 3, 2, 1 …).
    ///
    /// Bandwidth (as every server feature): motion is a pure function of the platform's running clock, so nothing is
    /// kept or sent while nobody is near. Updates go only to players on its landscape whose client has the platform
    /// (prop streaming, Props.Stream.cs: the pack is streamed as one) within <see cref="PlatformViewRange"/> (kept until <see cref="PlatformLeaveRange"/>, so walking along the edge doesn't
    /// re-send the newcomer pair) and not mid re-zone: one at each leg start / stop, every
    /// <see cref="PlatformStraightSendInterval"/> (a leg without a turn) or <see cref="PlatformSendInterval"/> (a turning
    /// leg) while it moves, and the current spot to a player who just came near (twice:
    /// the client drops a position for a prop it has not spawned yet — the second copy is skipped when a regular
    /// moving update has reached them in between). A viewer who leaves range while the platform moves gets a stop
    /// record (v = 0) first: the client rebuilds a prop's collision every frame while its motion state's |v|² ≥ 1e-5
    /// (ProxiedProp::Update FUN_1430ab000 → FUN_1430ac390; trace/0695/agent-platform-collision.md §2.5) [P], so a
    /// viewer left holding a moving record did so for as long as it kept the prop (bandwidth audit 2026-10-18 R7 a-c).
    /// The prop record (and the database) keeps the placed position; late viewers are spawned there and moved by that
    /// first update.
    /// <para>
    /// The rate (audit R7 d, OPEN-LEADS L186; trace/0695/agent-leadsB.md §1) [P]: the client's GetStateAtTime
    /// FUN_1435a53e0 extrapolates past the newest record — the blend FUN_143b043f0 with a fraction above 1 calls
    /// FUN_143b04100(newest, …, extrapolate position = 1, rotation = 1), which adds record velocity (+0x40) × time and
    /// turns by the angular velocity (+0x50) — clamped to K seconds past it. K is the StringHashToValue double with hash
    /// 0xCC862F8A = "Network.ExtrapolationLimit", which the 0.695 table sets to 0, so the fallback DAT_145471a98 is used;
    /// its only writer, the static initializer at 0x141ad9c30, copies the engine's float +infinity from 0x1452B7990
    /// (0x7F800000). So the extrapolation limit is +infinity: between records the pad keeps moving along the newest
    /// record's velocity, and a history gap over 150 ms is bridged on that same line (FUN_1435a4430). On a straight
    /// constant-speed leg no mid-leg record is needed for smooth motion; the client needs the leg-start record and the
    /// stop record (v = 0, sent at the first 100 ms tick after arrival, which the 0.15 s render delay keeps behind the
    /// dock). So a leg whose two stops have the same turn (|Quaternion.Dot| &gt; 0.99999, float-safe equality) is sent
    /// every <see cref="PlatformStraightSendInterval"/> (1000 ms: half the bytes, ≈63 B/s per viewer while moving); a
    /// turning leg keeps <see cref="PlatformSendInterval"/> (500 ms), since Creatures.MovementBody sends angular velocity
    /// 0, so the client holds the turn between records and then slerps. The records go Reliable through the gateway,
    /// and a newcomer still gets its two copies (its client holds a lone record until a second arrives).
    /// </para>
    ///
    /// On / off: switched off (PropBase SetPropEnable; in Play Mode the Links' state, <see cref="StoryTools.PlayModeOn"/>
    /// — P2668 linking and triggering work with the Moving Platform) the platform stops where it is and
    /// its clock waits.
    /// </summary>
    public static partial class Props
    {
        /// <summary>Placement packs that are moving platforms (PlacementPacks.txt 5 Magical, 6 Three dock, 8 Glass).</summary>
        private static readonly HashSet<uint> platformPacks = new() { 5, 6, 8 };

        /// <summary>The moving part (Magical Platform 1193, Glass Platform 1421) and the docks (1192, 1422).</summary>
        private static readonly HashSet<uint> platformParts = new() { 1193, 1421 }, platformDocks = new() { 1192, 1422 };

        /// <summary>EditableProperties keys of the platform settings (rows 8 and 7).</summary>
        public const string PlatformSpeedSetting = "moving_platform_speed", PlatformPauseSetting = "moving_platform_pause_delay";

        /// <summary>
        /// The client's keys for them, oaat("moving_platform_speed") = 0x321630f0 and oaat("moving_platform_pause_delay")
        /// = 0x30725695, as AreaPropertyDataMovingObject's setting slot FUN_143db4060 reads them [P].
        /// </summary>
        public const int PlatformSpeedKey = 0x321630f0, PlatformPauseKey = 0x30725695;

        /// <summary>Editor value → m/s (DAT_144c4103c = 0.01 in FUN_143db4060) [P factor].</summary>
        private const float PlatformSpeedUnit = 0.01f;

        /// <summary>EditableProperties defaults and limits (rows 8 / 7) [P].</summary>
        private const float DefaultPlatformSpeed = 160f, MinPlatformSpeed = 50f, MaxPlatformSpeed = 1000f;
        private const int DefaultPlatformPause = 2, MaxPlatformPause = 30;

        /// <summary>
        /// How far in front of a dock's centre the platform stops: half the platform's area box (±1.8 m, area 14) plus
        /// half the dock's depth (1 m box, area 15) [I from the GlobalAreaShapes boxes; tune in game].
        /// </summary>
        private const float DockStopDistance = 1.8f + 0.5f;

        /// <summary>Players this close to the platform are sent its movement [I, the creatures' view range].</summary>
        private const float PlatformViewRange = 120f;

        /// <summary>A viewer already sent the platform keeps getting it until this far (hysteresis, audit R7 b; ours).</summary>
        private const float PlatformLeaveRange = PlatformViewRange + 10f;

        /// <summary>Movement updates while it travels a turning leg [I: a modest rate; the client holds the turn between records].</summary>
        private static readonly TimeSpan PlatformSendInterval = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// Movement updates while it travels a leg without a turn: the client extrapolates along the record's velocity with
        /// no limit (Network.ExtrapolationLimit → +infinity, L186), so a straight constant-speed leg plays smoothly between
        /// records [P]. 1000 ms, as agent-leadsB.md §1.5 recommends (2000 ms would also be safe statically).
        /// </summary>
        private static readonly TimeSpan PlatformStraightSendInterval = TimeSpan.FromMilliseconds(1000);

        /// <summary>A new viewer's second copy of the current spot (in case its first came before the prop's spawn).</summary>
        private static readonly TimeSpan PlatformRepeat = TimeSpan.FromSeconds(1);

        private static readonly TimeSpan PlatformTick = TimeSpan.FromMilliseconds(100);

        /// <summary>One moving platform: its pack group, its running clock and who has been sent where it is.</summary>
        private sealed class MovingPlatform
        {
            public ulong Group;
            /// <summary>Seconds it has run (only while switched on): the motion is a function of this.</summary>
            public double Clock;
            /// <summary>The motion segment last sent (stop / leg index, −1 = stopped by a switch; null = none yet).</summary>
            public int? Segment;
            public DateTime LastSend;
            /// <summary>Viewers sent its spot, with when to send it once more (null = done).</summary>
            public readonly Dictionary<WorldSession, DateTime?> Viewers = new();
        }

        private static readonly Dictionary<ulong, MovingPlatform> platforms = new();
        private static DateTime lastPlatformTick = DateTime.MinValue;

        /// <summary>
        /// Every client takes local management of every prop it spawns (SpawnProxiedProp FUN_142f8a800 → FUN_1430aafd0 →
        /// FUN_142c98c00, unconditionally: entity +0xF4 = 1, guid in the managed set +0x32F40) [P], and then drops the
        /// server's position updates for it (0x7F handler FUN_142ce6340) and skips its playback (FUN_142eb1950) — the
        /// moving pad never moved (Frida round 87: all 68 records took the "managed" branch). So right after each spawn
        /// of a moving part, that viewer gets PlayerUpdate ManagedObject 0x4D {u64 object, u64 old manager = the viewer's
        /// own player guid, u64 new manager = 0} (reader FUN_142c29910, exactly 30 bytes; handler FUN_142cdd450 →
        /// FUN_142d3b580 removes it and clears +0xF4) [P] (trace/0695/agent-platform-collision.md §9). Every spawn needs
        /// its own release (a re-zone destroys and re-adds the entity); packets are handled in order, so the same burst
        /// is right.
        /// </summary>
        private static void ReleaseIfServerMoved(WorldSession session, PlacedProp prop)
        {
            if (!IsPlatform(prop))
                return;
            var body = new byte[24];
            BitConverter.GetBytes(prop.Spawn.Guid).CopyTo(body, 0);
            BitConverter.GetBytes(session.PlayerGuid).CopyTo(body, 8);
            // new manager 0: nobody — the server drives it
            session.EnqueueMessage(Network.Message.ZoneMessageOpcode.PlayerUpdateManagedObject, body);
        }

        /// <summary>Is this placed part the moving piece of a platform pack?</summary>
        private static bool IsPlatform(PlacedProp part)
            => part.PackGroup != 0 && platformParts.Contains(part.PropId) && platformPacks.Contains(PackOf(part.PackSpawner));

        /// <summary>Registers a platform pack (from <see cref="BindMover"/>: pack expansion and the world load).</summary>
        private static void BindPlatform(PlacedProp part)
        {
            if (!IsPlatform(part))
                return;
            // The owner's own client drops server position updates for the pad otherwise: AddLightweightProp
            // (FUN_142cde1c0) puts a prop whose spawn owner (+0x2A0) is the local player into its locally managed set
            // (+0x32F40, FUN_142c98c00), and the 0x7F handler FUN_142ce6340 returns before applying anything to a guid
            // in that set [P] (trace/0695/agent-platform-collision.md §7; round 76: the pad never moved on its owner's
            // screen). The spawn's owner is used for nothing else there, so the moving part goes out without one; the
            // server's own record (PlacedProp.OwnerGuid: permissions, database) keeps the owner.
            part.Spawn.OwnerGuid = 0;
            // And a control mode, without which the client queues the pad's position updates and never plays them
            // (FUN_142eb1950 / FUN_142e93340 need entity +0x41A0 ≠ 0) [P]; 2 as creatures (round 79: the pad stayed put).
            part.Spawn.ControlMode = 2;
            lock (platforms)
                if (!platforms.ContainsKey(part.PackGroup))
                    platforms[part.PackGroup] = new MovingPlatform { Group = part.PackGroup };
        }

        /// <summary>
        /// Some props were spawned again for the other players (<see cref="Update"/>: a part moved and settled): a
        /// platform among them is back at its placed spot on their screens, so its viewers are sent its spot afresh.
        /// </summary>
        private static void PlatformsRespawned(IEnumerable<PlacedProp> respawned)
        {
            lock (platforms)
                foreach (PlacedProp p in respawned)
                    if (p.PackGroup != 0 && platforms.TryGetValue(p.PackGroup, out MovingPlatform mp))
                        mp.Viewers.Clear();
        }

        /// <summary>One stop of the route: where the platform rests, its turn there, the pause and the speed leaving it.</summary>
        private readonly record struct PlatformStop(Vector3 At, Quaternion Turn, float Pause, float Speed);

        /// <summary>Where a platform is at a point of its clock.</summary>
        private readonly record struct PlatformPose(Vector3 At, Quaternion Turn, Vector3 Velocity, int Segment);

        /// <summary>Moves every platform someone can see (called from <see cref="Update"/>, at most every 100 ms).</summary>
        private static void UpdatePlatforms()
        {
            DateTime now = DateTime.UtcNow;
            if (now - lastPlatformTick < PlatformTick)
                return;
            double dt = lastPlatformTick == DateTime.MinValue ? 0 : Math.Min((now - lastPlatformTick).TotalSeconds, 1.0);
            lastPlatformTick = now;
            List<MovingPlatform> all;
            lock (platforms)
            {
                if (platforms.Count == 0)
                    return;
                all = platforms.Values.ToList();
            }
            foreach (MovingPlatform mp in all)
                StepPlatform(mp, now, dt);
        }

        private static void StepPlatform(MovingPlatform mp, DateTime now, double dt)
        {
            List<PlacedProp> parts = PackParts(mp.Group);
            PlacedProp platform = parts.FirstOrDefault(p => platformParts.Contains(p.PropId));
            List<PlacedProp> docks = parts.Where(p => platformDocks.Contains(p.PropId)).OrderBy(p => p.Spawn.Guid).ToList();
            if (platform == null || docks.Count < 2)
            {
                // Picked up / deleted (the pack goes as a whole): forget it.
                if (platform == null || parts.Count == 0)
                    lock (platforms)
                        platforms.Remove(mp.Group);
                return;
            }
            // Still being placed or moved by its owner: their cursor owns it; leave it alone until it settles.
            if (parts.Any(p => !p.Placed || p.MovedAt != null || p.ReplacingBy != 0))
                return;
            bool on = StoryTools.PlayModeOn(platform.Spawn.Guid) ?? !platform.Disabled;
            if (on)
                mp.Clock += dt;
            List<PlatformStop> stops = Route(platform, docks);
            PlatformPose pose = PoseAt(stops, mp.Clock);
            if (!on)
                pose = pose with { Velocity = Vector3.Zero, Segment = -1 };

            List<WorldSession> viewers, stopFor = new();
            List<WorldSession> sendTo;
            // Only players whose client has the platform (prop streaming, Props.Stream.cs): the client drops a 0x7F for a
            // transient id it has no entity for. Read before the platforms lock (the stream lock is taken first elsewhere).
            var streaming = ViewersOf(platform).ToHashSet();
            lock (platforms)
            {
                viewers = PlayerManager.InZone(platform.Zone)
                    .Where(v => v.RezoneTarget == null && streaming.Contains(v) && Vector3.Distance(v.Position, pose.At)
                        <= (mp.Viewers.ContainsKey(v) ? PlatformLeaveRange : PlatformViewRange)).ToList();
                // Their client may still hold a moving record: the platform moves now, or the last segment sent was a leg
                // (2k + 1) whose stop they'd miss by leaving this tick.
                bool heldMoving = pose.Velocity != Vector3.Zero || mp.Segment is int last && last > 0 && last % 2 == 1;
                // Gone out of range (or mid re-zone): forgotten, so they get the spot again when they come back. One left
                // with the platform moving gets a stop record where it is now (v = 0), unless their client dropped the
                // prop anyway (mid re-zone, another landscape, disconnected).
                foreach (WorldSession gone in mp.Viewers.Keys.Where(v => !viewers.Contains(v)).ToList())
                {
                    mp.Viewers.Remove(gone);
                    if (heldMoving && gone.RezoneTarget == null && gone.ZoneId == platform.Zone && !gone.Disconnected
                        && streaming.Contains(gone))
                        stopFor.Add(gone);
                }
                if (viewers.Count == 0)
                    mp.Segment = null;
            }
            if (stopFor.Count > 0)
            {
                byte[] stop = Creatures.MovementBody(pose.At, pose.Turn, Vector3.Zero);
                foreach (WorldSession v in stopFor)
                    MovementFanout.Queue(v, platform.Spawn.TransientId, stop);
            }
            if (viewers.Count == 0)
                return;
            lock (platforms)
            {
                bool all = mp.Segment != pose.Segment || (pose.Velocity != Vector3.Zero && now - mp.LastSend >= SendIntervalOf(stops, pose.Segment));
                sendTo = new List<WorldSession>();
                foreach (WorldSession v in viewers)
                {
                    if (!mp.Viewers.TryGetValue(v, out DateTime? repeatAt))
                    {
                        mp.Viewers[v] = now + PlatformRepeat;
                        sendTo.Add(v);
                    }
                    else if (all)
                    {
                        sendTo.Add(v);
                        // A moving update reached them after their first copy: that is the second chance, and more follow
                        // every PlatformSendInterval, so the newcomer repeat is not needed (audit R7 c). At rest it stays
                        // (nothing else would follow a dropped stop record).
                        if (pose.Velocity != Vector3.Zero)
                            mp.Viewers[v] = null;
                    }
                    else if (repeatAt is DateTime due && now >= due)
                    {
                        mp.Viewers[v] = null;
                        sendTo.Add(v);
                    }
                }
                if (all)
                {
                    if (mp.Segment != pose.Segment)
                        log.Debug($"Platform pack {mp.Group:X}: leg {pose.Segment} at {pose.At} (speed {pose.Velocity.Length():0.00} m/s, {viewers.Count} viewer(s)).");
                    mp.Segment = pose.Segment;
                    mp.LastSend = now;
                }
            }
            if (sendTo.Count == 0)
                return;
            byte[] movement = Creatures.MovementBody(pose.At, pose.Turn, pose.Velocity);
            foreach (WorldSession v in sendTo)
                MovementFanout.Queue(v, platform.Spawn.TransientId, movement);
        }

        /// <summary>
        /// How often a moving update goes out on a segment (2k + 1 = travelling from stop k to stop k + 1):
        /// <see cref="PlatformStraightSendInterval"/> when the leg's two stops have the same turn (no rotation on the way;
        /// q and −q are the same turn, hence |dot|), else <see cref="PlatformSendInterval"/> (see the class notes, L186).
        /// </summary>
        private static TimeSpan SendIntervalOf(List<PlatformStop> stops, int segment)
        {
            if (segment < 0 || segment % 2 != 1 || stops.Count == 0)
                return PlatformSendInterval;
            PlatformStop from = stops[segment / 2 % stops.Count], to = stops[(segment / 2 + 1) % stops.Count];
            return MathF.Abs(Quaternion.Dot(from.Turn, to.Turn)) > 0.99999f ? PlatformStraightSendInterval : PlatformSendInterval;
        }

        /// <summary>The send interval of a leg between two turns (tests: <see cref="SendIntervalOf"/> on a two-stop route).</summary>
        public static TimeSpan PlatformSendIntervalBetween(Quaternion fromTurn, Quaternion toTurn)
            => SendIntervalOf(new List<PlatformStop> { new(Vector3.Zero, fromTurn, 0f, 1f), new(Vector3.UnitX, toTurn, 0f, 1f) }, 1);

        /// <summary>
        /// The platform's route: one stop per dock in PROP_INDEX order (the docks' guids follow it: <see cref="ExpandPack"/>
        /// creates the parts in index order), and back through the middle ones when there are more than two.
        /// </summary>
        private static List<PlatformStop> Route(PlacedProp platform, List<PlacedProp> docks)
        {
            List<PackPart> packParts = packs.Value.Parts.GetValueOrDefault(PackOf(platform.PackSpawner)) ?? new List<PackPart>();
            List<PackPart> dockParts = packParts.Where(p => platformDocks.Contains(p.PropId)).ToList();
            float platformSpeed = Math.Clamp(PropSettings.Get(platform.Spawn.Guid, PlatformSpeedSetting), MinPlatformSpeed, MaxPlatformSpeed);
            float platformPause = Math.Clamp(PropSettings.Get(platform.Spawn.Guid, PlatformPauseSetting), 0f, MaxPlatformPause);
            var stops = new List<PlatformStop>();
            for (int i = 0; i < docks.Count; i++)
            {
                PlacedProp dock = docks[i];
                Vector4 r = dock.Spawn.Rotation;
                Quaternion dockTurn = Quaternion.Normalize(new Quaternion(r.X, r.Y, r.Z, r.W));
                float heading = i < dockParts.Count ? dockParts[i].Heading : 0f;
                Quaternion turn = Quaternion.Normalize(dockTurn * Quaternion.Inverse(Quaternion.CreateFromAxisAngle(Vector3.UnitY, heading * MathF.PI / 180f)));
                Vector3 at = new Vector3(dock.Spawn.Position.X, dock.Spawn.Position.Y, dock.Spawn.Position.Z)
                    + Vector3.Transform(Vector3.UnitX, dockTurn) * DockStopDistance;
                float speed = PropSettings.TryGetSaved(dock.Spawn.Guid, PlatformSpeedSetting, out float s)
                    ? Math.Clamp(s, MinPlatformSpeed, MaxPlatformSpeed) : platformSpeed;
                // Whole seconds (the data class keeps the pause as an int).
                float pause = MathF.Floor(PropSettings.TryGetSaved(dock.Spawn.Guid, PlatformPauseSetting, out float p)
                    ? Math.Clamp(p, 0f, MaxPlatformPause) : platformPause);
                stops.Add(new PlatformStop(at, turn, pause, speed * PlatformSpeedUnit));
            }
            for (int i = docks.Count - 2; i >= 1; i--)
                stops.Add(stops[i]);
            return stops;
        }

        /// <summary>
        /// Where the platform is <paramref name="clock"/> seconds into its run: the cycle is pause at stop 0, travel to
        /// stop 1, pause, … back to stop 0. Segments: 2k = resting at stop k, 2k + 1 = travelling from stop k.
        /// </summary>
        private static PlatformPose PoseAt(List<PlatformStop> stops, double clock)
        {
            int n = stops.Count;
            var legTimes = new double[n];
            double cycle = 0;
            for (int k = 0; k < n; k++)
            {
                PlatformStop from = stops[k], to = stops[(k + 1) % n];
                legTimes[k] = Math.Max(0.1, Vector3.Distance(from.At, to.At) / Math.Max(0.01f, from.Speed));
                cycle += from.Pause + legTimes[k];
            }
            double t = clock % cycle;
            for (int k = 0; k < n; k++)
            {
                PlatformStop from = stops[k], to = stops[(k + 1) % n];
                if (t < from.Pause)
                    return new PlatformPose(from.At, from.Turn, Vector3.Zero, 2 * k);
                t -= from.Pause;
                if (t < legTimes[k])
                {
                    float f = (float)(t / legTimes[k]);
                    Vector3 leg = to.At - from.At;
                    Vector3 velocity = leg.LengthSquared() > 0f ? Vector3.Normalize(leg) * from.Speed : Vector3.Zero;
                    return new PlatformPose(Vector3.Lerp(from.At, to.At, f), Quaternion.Slerp(from.Turn, to.Turn, f), velocity, 2 * k + 1);
                }
                t -= legTimes[k];
            }
            return new PlatformPose(stops[0].At, stops[0].Turn, Vector3.Zero, 0);
        }
    }
}
