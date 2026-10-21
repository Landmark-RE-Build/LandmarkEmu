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
    /// HeroicMovement 0x105 (wire 05 01): the grappling hook and swimming. The movement itself is client physics (the
    /// pull uses calculated stats 18-21); the client tells the server what it does so the server can show it to the
    /// players nearby, whose clients draw the rope, the pull and the swim pose for that character. Traced on the 0.695
    /// client; the 0.589 classes are the same (trace/0695/coverage-695.md "0x105").
    /// <para>
    /// Every packet starts with {u16 0x105, u8 sub, u64 entity} (reader FUN_142c1fd70, writer FUN_142e5eaf0); the entity
    /// is the sender's own guid (the writers read it from the character's +0x278 getter, the same one the receiving
    /// side compares). Subs [P layouts]:
    /// <list type="bullet">
    /// <item>1 FireGrapplingHook: {f32 × 4, f32 × 4, u32, u32, bool} (reader FUN_142c1fe00); others play it through
    /// FUN_142ea4a10. Also the Getting Around objective 1309 (<see cref="Achievements.HeroicMovement"/>).</item>
    /// <item>2 SetHeroicMovement: {bool} (reader FUN_142c49910) → FUN_142ec2740 on the remote character.</item>
    /// <item>3 SwimmingUpdate: {u8 flags, f32 depth, [flags &amp; 0x40: f32, f32]} (writer FUN_14358bc70, reader
    /// FUN_142c1ffa0), built by the swimming controller's update FUN_14358d760 and sent only when it differs from the
    /// last one sent (compare FUN_14358c410) — or once at the start: the controller's constructor (FUN_14358c210,
    /// 0.589 FUN_1434dc750) sets its "send" byte (+0xc4), so every client sends one all-zero SwimmingUpdate around
    /// zone-in (OPEN-LEADS L166: body 03 + its guid + 00 + 00 00 00 00). The remote side (FUN_14358cf30) stores depth
    /// and the 0x40 pair and, on flag 0x80 while the character is at the surface (state 1), switches it to under water
    /// (state 2). Meaning [I, from FUN_14358d760]: depth = how deep the character stands in water as a share of its
    /// height (a water query FUN_143158e90 over its height +0x8c; 0 = out of water); 0x40 = under the surface, with the
    /// surface height and a second height (0 when the voxel check FUN_143fb8080 fails); 0x80 = a dive into the water
    /// (falling fast as it enters).</item>
    /// <item>4 TerminateGrappleHookEffects: no body → FUN_142ea4f30 ends the rope effects.</item>
    /// <item>5 HeroicMovement: {u32} (writer FUN_142e5ef80), sent with 1 when a grapple pull arrives (FUN_142e62f50).
    /// No client reads it: the receiving switch FUN_142cdc570 handles 1-4 only, so it is not relayed.</item>
    /// </list>
    /// Nothing is answered: the client waits for no reply to any sub.
    /// </para>
    /// <para>
    /// Relay rules (bandwidth): to players on the landscape within <see cref="RelayRange"/>, the entity set to the
    /// sender's guid, not while the sender is hidden (/hideme), and not to a viewer in the middle of a re-zone (its
    /// client deleted the swimmer at ClientBeginZoning; <see cref="IntroduceSwimmer"/> gives it the state after).
    /// SwimmingUpdates are coalesced per swimmer: at most one every <see cref="SwimInterval"/>, the newest state (a
    /// pending dive flag 0x80 is kept, since the receiver acts on it once), and a state equal to the last one relayed is
    /// not sent again (the zone-in zero state is what every receiver already assumes).
    /// </para>
    /// <para>
    /// States are followed per viewer (audit 2026-10-21, docs/observer-sync.md): heroic movement, the swim state and a
    /// fired hook stay on the receiver's copy of the character until changed, so their end also goes to every viewer
    /// shown the start (wherever it is now), and once a second a player's current state goes to viewers who came into
    /// range since (<see cref="CatchUp"/>). Before, a viewer out of range at the end kept the heroic run or the swim
    /// pose, and one who came near during it saw neither.
    /// </para>
    /// <para>
    /// Quantised (audit-bandwidth-2026-10-18 R3): the client sends a SwimmingUpdate on any exact float change of depth
    /// or the two heights (compare FUN_14358c410 uses ==) and has no rate limit of its own (FUN_14358d760) [P], so while
    /// diving or bobbing it sends nearly every frame. Before the compare the server rounds depth to 1/64 of the
    /// character's height (the receiver draws the water line at height × depth, FUN_14358d760 remote branch [P]: ≈3 cm
    /// on a 2 m character) and the two heights to 0.05 m [I: steps that small are not visible]; a non-zero value never
    /// rounds to 0, which the receiver reads as "out of water" (depth) or "no second height" (+0xa0) [P, FUN_14358d760].
    /// Together with the 250 ms interval this takes a swimmer from ≈160-240 B/s to ≲60 B/s per viewer.
    /// </para>
    /// <para>
    /// A player who meets a swimmer later (PlayerManager.Introduce: zone-in, re-zone, coming into view)
    /// gets that swimmer's current state once, queued after the character itself (<see cref="IntroduceSwimmer"/>): the
    /// receiver keeps the last state per character and nothing else repeats it, so without this a swimmer met in the
    /// water stood upright until their state next changed (OPEN-LEADS L167). Under the surface (0x40) the dive flag 0x80
    /// is set on that copy, so the receiver's surface state switches to under water as it would have on the dive [I:
    /// FUN_14358cf30 acts on 0x80 only from state 1; a receiver not in state 1 ignores it].
    /// </para>
    /// </summary>
    public static class HeroicMovement
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const byte FireGrapplingHook = 1, SetHeroicMovement = 2, SwimmingUpdate = 3,
            TerminateGrappleHookEffects = 4, HeroicMovementSub = 5;

        /// <summary>Range (m) within which others see a player's grappling hook and swimming.</summary>
        private const float RelayRange = 200f;

        /// <summary>Shortest time between two relayed SwimmingUpdates of one swimmer [I ours; 100 ms until the 2026-10-18
        /// bandwidth audit R3].</summary>
        private static readonly TimeSpan SwimInterval = TimeSpan.FromMilliseconds(250);

        /// <summary>Relayed depth step: a share of the character's height (R3).</summary>
        private const float DepthStep = 1f / 64f;

        /// <summary>Relayed step of the two heights under the surface (flag 0x40), metres (R3).</summary>
        private const float HeightStep = 0.05f;

        /// <summary>SwimmingUpdate flag: a dive into the water (the receiver switches surface → under water).</summary>
        private const byte DiveFlag = 0x80;

        /// <summary>Wire header length: u16 base, u8 sub, u64 entity.</summary>
        private const int HeaderLength = 11;

        /// <summary>The zero SwimmingUpdate body: out of the water, what a receiver assumes for a character it has just made.</summary>
        private static readonly byte[] ZeroSwim = { 0, 0, 0, 0, 0 };

        /// <summary>How often a player's heroic movement / swimming state is checked against who should see it now.</summary>
        internal static TimeSpan ScanInterval = TimeSpan.FromSeconds(1);

        /// <summary>
        /// One player's heroic movement and swimming state and what each viewer's client was told of it. These are states
        /// the receiver keeps on its copy of the character until told otherwise (SetHeroicMovement: entity +0x1451 bit 0x20
        /// and the animation network's HeroicMovementActivated / Deactivated, FUN_142ec2740; the swim state, FUN_14358cf30;
        /// the rope until TerminateGrappleHookEffects, FUN_142ea4f30) [P], so a range-limited relay alone left stale looks
        /// (audit 2026-10-21): a viewer out of range when the state ended kept it (a heroic run, a swimmer on dry land)
        /// until the next change, and a viewer who came into range during it saw the default. Hence the per-viewer record:
        /// a change goes to the viewers in range and its end also to every viewer who was shown the start, and a scan
        /// (<see cref="ScanInterval"/>) shows the current state to viewers who came into range since.
        /// </summary>
        private sealed class Mover
        {
            /// <summary>The newest SwimmingUpdate body not relayed yet (null = none).</summary>
            public byte[] Pending;
            /// <summary>The last body relayed (starts as the zero state every receiver assumes).</summary>
            public byte[] LastSent = ZeroSwim;
            public DateTime LastSentAt;
            /// <summary>The swim state each viewer was last sent (a viewer not listed has the zero state).</summary>
            public readonly Dictionary<WorldSession, byte[]> SwimKnown = new();
            /// <summary>Heroic movement on (the client's last SetHeroicMovement), and the viewers shown it on.</summary>
            public bool Heroic;
            public readonly HashSet<WorldSession> HeroicTold = new();
            /// <summary>Viewers shown a FireGrapplingHook whose TerminateGrappleHookEffects has not come yet.</summary>
            public readonly HashSet<WorldSession> GrappleTold = new();
            public DateTime NextScan;
        }

        private static readonly object sync = new();
        private static readonly Dictionary<WorldSession, Mover> movers = new();

        private static Mover Of(WorldSession session)
        {
            if (!movers.TryGetValue(session, out Mover m))
                movers[session] = m = new Mover();
            return m;
        }

        [ZoneMessageHandler(ZoneMessageOpcode.HeroicMovement)]
        public static void Handle(WorldSession session, HeroicMovementRequest request)
        {
            switch (request.SubOpcode)
            {
                case FireGrapplingHook:
                    Achievements.HeroicMovement(session, request.SubOpcode);
                    Grapple(session, request.Body);
                    break;
                case TerminateGrappleHookEffects:
                    EndGrapple(session, request.Body);
                    break;
                case SetHeroicMovement:
                    Heroic(session, request.Body);
                    break;
                case SwimmingUpdate:
                    Swim(session, request.Body);
                    break;
                case HeroicMovementSub:
                    // A grapple pull arrived; informational (no client reads 0x105 sub 5, so nothing is relayed).
                    log.Trace($"{session.CharacterName}: grapple pull arrived ({BitConverter.ToString(request.Body)}).");
                    break;
                default:
                    Network.OpcodeStats.Unanswered("HeroicMovement 0x105", request.SubOpcode);
                    log.Debug($"{session.CharacterName}: HeroicMovement sub {request.SubOpcode}: {BitConverter.ToString(request.Body)}");
                    break;
            }
        }

        /// <summary>FireGrapplingHook: to the viewers in range, who are remembered for its terminate.</summary>
        private static void Grapple(WorldSession session, byte[] body)
        {
            List<WorldSession> to = Viewers(session, inRangeOnly: true);
            lock (sync)
                Of(session).GrappleTold.UnionWith(to);
            Send(session, FireGrapplingHook, body, to);
        }

        /// <summary>TerminateGrappleHookEffects: to the viewers in range and every viewer shown the hook, wherever now.</summary>
        private static void EndGrapple(WorldSession session, byte[] body)
        {
            List<WorldSession> zone = Viewers(session, inRangeOnly: false);
            var to = new List<WorldSession>();
            lock (sync)
            {
                Mover m = Of(session);
                foreach (WorldSession v in zone)
                    if (m.GrappleTold.Contains(v) || InRange(v, session))
                        to.Add(v);
                m.GrappleTold.Clear();
            }
            Send(session, TerminateGrappleHookEffects, body, to);
        }

        /// <summary>
        /// SetHeroicMovement {bool}: on goes to the viewers in range (remembered); off to them and to every viewer shown
        /// it on (an off the receiver already has does nothing there: FUN_142ec2740 returns when the bit is unchanged).
        /// </summary>
        private static void Heroic(WorldSession session, byte[] body)
        {
            if (body.Length < 1)
                return;
            bool on = body[0] != 0;
            List<WorldSession> zone = Viewers(session, inRangeOnly: false);
            var to = new List<WorldSession>();
            lock (sync)
            {
                Mover m = Of(session);
                m.Heroic = on;
                foreach (WorldSession v in zone)
                    if (InRange(v, session) || (!on && m.HeroicTold.Contains(v)))
                        to.Add(v);
                if (on)
                    m.HeroicTold.UnionWith(to);
                else
                    m.HeroicTold.Clear();
            }
            Send(session, SetHeroicMovement, new[] { on ? (byte)1 : (byte)0 }, to);
        }

        /// <summary>A SwimmingUpdate: kept as the swimmer's pending state; sent now when the interval has passed.</summary>
        private static void Swim(WorldSession session, byte[] body)
        {
            if (body.Length < 5)
            {
                log.Debug($"{session.CharacterName}: short SwimmingUpdate {BitConverter.ToString(body)}");
                return;
            }
            lock (sync)
            {
                Mover s = Of(session);
                byte[] next = Quantise(body);
                if (s.Pending != null && (s.Pending[0] & DiveFlag) != 0)
                    next[0] |= DiveFlag;
                s.Pending = next;
            }
            Flush(session, DateTime.UtcNow);
        }

        /// <summary>
        /// A copy of a SwimmingUpdate body {u8 flags, f32 depth, [flags &amp; 0x40: f32, f32]} with depth rounded to
        /// <see cref="DepthStep"/> and the two heights to <see cref="HeightStep"/> (flags and any extra bytes unchanged).
        /// </summary>
        private static byte[] Quantise(byte[] body)
        {
            byte[] next = (byte[])body.Clone();
            Round(next, 1, DepthStep);
            if ((next[0] & 0x40) != 0 && next.Length >= 13)
            {
                Round(next, 5, HeightStep);
                Round(next, 9, HeightStep);
            }
            return next;
        }

        /// <summary>Rounds the f32 at <paramref name="offset"/> to a multiple of <paramref name="step"/>; exact 0 and
        /// non-finite values stay as they are, and a non-zero value never becomes 0.</summary>
        private static void Round(byte[] body, int offset, float step)
        {
            float value = BitConverter.ToSingle(body, offset);
            if (value == 0f || !float.IsFinite(value))
                return;
            float rounded = MathF.Round(value / step) * step;
            if (rounded == 0f)
                rounded = MathF.CopySign(step, value);
            BitConverter.GetBytes(rounded).CopyTo(body, offset);
        }

        /// <summary>
        /// Server tick: sends each swimmer's pending state once its interval has passed, and once a
        /// <see cref="ScanInterval"/> per player shows their current heroic movement / swim state to viewers who came
        /// into <see cref="RelayRange"/> since (they were too far when it began or last changed).
        /// </summary>
        public static void Update()
        {
            List<WorldSession> due, scan;
            DateTime now = DateTime.UtcNow;
            lock (sync)
            {
                due = movers.Where(x => x.Value.Pending != null).Select(x => x.Key).ToList();
                scan = new List<WorldSession>();
                foreach ((WorldSession subject, Mover m) in movers)
                    if (now >= m.NextScan && (m.Heroic || !IsZero(m.LastSent)))
                    {
                        m.NextScan = now + ScanInterval;
                        scan.Add(subject);
                    }
            }
            foreach (WorldSession session in due)
                Flush(session, now);
            foreach (WorldSession subject in scan)
                CatchUp(subject);
        }

        /// <summary>The current state to the viewers in range whose client has another (<see cref="Update"/>).</summary>
        internal static void CatchUp(WorldSession subject)
        {
            List<WorldSession> near = Viewers(subject, inRangeOnly: true);
            var heroic = new List<WorldSession>();
            var swim = new List<(WorldSession Viewer, byte[] Body)>();
            lock (sync)
            {
                if (!movers.TryGetValue(subject, out Mover m))
                    return;
                foreach (WorldSession v in near)
                {
                    if (m.Heroic && m.HeroicTold.Add(v))
                        heroic.Add(v);
                    byte[] known = m.SwimKnown.GetValueOrDefault(v) ?? ZeroSwim;
                    if (!known.AsSpan().SequenceEqual(m.LastSent))
                    {
                        swim.Add((v, ForNewcomer(m.LastSent)));
                        if (IsZero(m.LastSent))
                            m.SwimKnown.Remove(v);
                        else
                            m.SwimKnown[v] = m.LastSent;
                    }
                }
            }
            Send(subject, SetHeroicMovement, new byte[] { 1 }, heroic);
            foreach ((WorldSession viewer, byte[] body) in swim)
                Send(subject, SwimmingUpdate, body, new[] { viewer });
        }

        private static void Flush(WorldSession session, DateTime now)
        {
            List<WorldSession> zone = Viewers(session, inRangeOnly: false);
            var to = new List<WorldSession>();
            byte[] body;
            lock (sync)
            {
                if (!movers.TryGetValue(session, out Mover s) || s.Pending == null || now - s.LastSentAt < SwimInterval)
                    return;
                body = s.Pending;
                s.Pending = null;
                if (body.AsSpan().SequenceEqual(s.LastSent))
                    return;
                s.LastSent = body;
                s.LastSentAt = now;
                // In range: the new state. Out of range: only the way out of the water, to a viewer still holding a swim
                // state (else that viewer saw a swimmer on dry land when next near); the rest catch up when near (CatchUp).
                bool zero = IsZero(body);
                foreach (WorldSession v in zone)
                {
                    byte[] known = s.SwimKnown.GetValueOrDefault(v) ?? ZeroSwim;
                    if ((InRange(v, session) || (zero && !IsZero(known))) && !known.AsSpan().SequenceEqual(body))
                    {
                        to.Add(v);
                        if (zero)
                            s.SwimKnown.Remove(v);
                        else
                            s.SwimKnown[v] = body;
                    }
                }
            }
            Send(session, SwimmingUpdate, body, to);
        }

        /// <summary>
        /// The other players on the sender's landscape who can be sent a HeroicMovement packet about it (within
        /// <see cref="RelayRange"/> when <paramref name="inRangeOnly"/>): none while the sender is hidden (/hideme) or
        /// gone, and no viewer in the middle of a re-zone (its client deleted the character at ClientBeginZoning; it gets
        /// the state with the next introduction, <see cref="IntroduceSwimmer"/>).
        /// </summary>
        private static List<WorldSession> Viewers(WorldSession session, bool inRangeOnly)
        {
            if (AdminCommands.IsHidden(session) || session.Disconnected)
                return new List<WorldSession>();
            return PlayerManager.InZone(session.ZoneId)
                .Where(other => other != session && !other.Disconnected
                    && (!inRangeOnly || InRange(other, session))
                    && (other.RezoneTarget == null || Teleport.IsShortMovePending(other)))   // mid re-zone: no entity yet
                .ToList();
        }

        private static bool InRange(WorldSession viewer, WorldSession subject)
            => Vector3.Distance(viewer.Position, subject.Position) <= RelayRange;

        private static bool IsZero(byte[] body) => body.AsSpan().IndexOfAnyExcept((byte)0) < 0;

        /// <summary>
        /// A swim state for a receiver that has not followed this swimmer: under the surface (0x40) the dive flag 0x80 is
        /// set, so the receiver's surface state switches to under water as it would have on the dive [I: FUN_14358cf30 acts
        /// on 0x80 only from state 1; a receiver not in state 1 ignores it].
        /// </summary>
        private static byte[] ForNewcomer(byte[] state)
        {
            byte[] body = (byte[])state.Clone();
            if ((body[0] & 0x40) != 0)
                body[0] |= DiveFlag;
            return body;
        }

        /// <summary>
        /// Sends a HeroicMovement packet about <paramref name="subject"/> to these viewers: {u16 0x105, u8 sub, u64 the
        /// subject's guid, body}, queued in order with the viewers' other packets.
        /// </summary>
        private static void Send(WorldSession subject, byte sub, byte[] body, IEnumerable<WorldSession> viewers)
        {
            byte[] packet = null;
            foreach (WorldSession viewer in viewers)
            {
                if (packet == null)
                {
                    packet = new byte[HeaderLength - 2 + body.Length];
                    packet[0] = sub;
                    BitConverter.GetBytes(subject.PlayerGuid).CopyTo(packet, 1);
                    body.CopyTo(packet, HeaderLength - 2);
                }
                viewer.EnqueueMessage(ZoneMessageOpcode.HeroicMovement, packet);
            }
        }

        /// <summary>
        /// A viewer has just been shown <paramref name="subject"/> (PlayerManager.Introduce, after AddLightweightPc in the
        /// same queue): its client made a fresh character — not swimming, no heroic movement, no rope — so what it was told
        /// before is forgotten, and within <see cref="RelayRange"/> it gets the current swim state (out of the water: the
        /// zero state it assumes, nothing sent) and heroic movement if on. Beyond it, <see cref="CatchUp"/> shows them
        /// when the viewer comes near.
        /// </summary>
        public static void IntroduceSwimmer(WorldSession viewer, WorldSession subject)
        {
            byte[] swim = null;
            bool heroic = false;
            bool near = InRange(viewer, subject);
            lock (sync)
            {
                if (!movers.TryGetValue(subject, out Mover m))
                    return;
                m.SwimKnown.Remove(viewer);
                m.HeroicTold.Remove(viewer);
                m.GrappleTold.Remove(viewer);
                if (!near)
                    return;
                byte[] state = m.Pending ?? m.LastSent;
                if (!IsZero(state))
                {
                    swim = ForNewcomer(state);
                    m.SwimKnown[viewer] = state;
                }
                if (m.Heroic)
                {
                    heroic = true;
                    m.HeroicTold.Add(viewer);
                }
            }
            if (swim != null)
                Send(subject, SwimmingUpdate, swim, new[] { viewer });
            if (heroic)
                Send(subject, SetHeroicMovement, new byte[] { 1 }, new[] { viewer });
        }

        /// <summary>A player left the world: their state goes, and they are no longer anyone's viewer.</summary>
        public static void Forget(WorldSession session)
        {
            lock (sync)
            {
                movers.Remove(session);
                foreach (Mover m in movers.Values)
                {
                    m.SwimKnown.Remove(session);
                    m.HeroicTold.Remove(session);
                    m.GrappleTold.Remove(session);
                }
            }
        }
    }
}
