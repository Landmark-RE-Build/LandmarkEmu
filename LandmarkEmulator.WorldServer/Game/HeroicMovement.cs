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

        private sealed class Swimmer
        {
            /// <summary>The newest SwimmingUpdate body not relayed yet (null = none).</summary>
            public byte[] Pending;
            /// <summary>The last body relayed (starts as the zero state every receiver assumes).</summary>
            public byte[] LastSent = { 0, 0, 0, 0, 0 };
            public DateTime LastSentAt;
        }

        private static readonly object sync = new();
        private static readonly Dictionary<WorldSession, Swimmer> swimmers = new();

        [ZoneMessageHandler(ZoneMessageOpcode.HeroicMovement)]
        public static void Handle(WorldSession session, HeroicMovementRequest request)
        {
            switch (request.SubOpcode)
            {
                case FireGrapplingHook:
                    Achievements.HeroicMovement(session, request.SubOpcode);
                    Relay(session, request.SubOpcode, request.Body);
                    break;
                case SetHeroicMovement:
                case TerminateGrappleHookEffects:
                    Relay(session, request.SubOpcode, request.Body);
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
                if (!swimmers.TryGetValue(session, out Swimmer s))
                    swimmers[session] = s = new Swimmer();
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

        /// <summary>Server tick: sends each swimmer's pending state once its interval has passed.</summary>
        public static void Update()
        {
            List<WorldSession> due;
            lock (sync)
                due = swimmers.Where(x => x.Value.Pending != null).Select(x => x.Key).ToList();
            DateTime now = DateTime.UtcNow;
            foreach (WorldSession session in due)
                Flush(session, now);
        }

        private static void Flush(WorldSession session, DateTime now)
        {
            byte[] body;
            lock (sync)
            {
                if (!swimmers.TryGetValue(session, out Swimmer s) || s.Pending == null || now - s.LastSentAt < SwimInterval)
                    return;
                body = s.Pending;
                s.Pending = null;
                if (body.AsSpan().SequenceEqual(s.LastSent))
                    return;
                s.LastSent = body;
                s.LastSentAt = now;
            }
            Relay(session, SwimmingUpdate, body);
        }

        /// <summary>
        /// Sends a HeroicMovement packet about the sender to the other players on its landscape within
        /// <see cref="RelayRange"/>: {u16 0x105, u8 sub, u64 the sender's guid, body}.
        /// </summary>
        private static void Relay(WorldSession session, byte sub, byte[] body)
        {
            if (AdminCommands.IsHidden(session) || session.Disconnected)
                return;
            byte[] packet = new byte[HeaderLength + body.Length];
            packet[0] = 0x05;
            packet[1] = 0x01;
            packet[2] = sub;
            BitConverter.GetBytes(session.PlayerGuid).CopyTo(packet, 3);
            body.CopyTo(packet, HeaderLength);
            Vector3 at = session.Position;
            foreach (WorldSession other in PlayerManager.InZone(session.ZoneId))
                if (other != session && !other.Disconnected && Vector3.Distance(other.Position, at) <= RelayRange
                    && (other.RezoneTarget == null || Teleport.IsShortMovePending(other)))   // mid re-zone: no entity yet
                    other.SendRaw(packet);
        }

        /// <summary>
        /// Shows a swimmer's current state to a player who has just been shown that character (PlayerManager.Introduce,
        /// after AddLightweightPc in the same queue, so the receiver knows the entity). Nothing is sent for a player out of
        /// the water (the zero state is what the receiver assumes) or out of <see cref="RelayRange"/>.
        /// </summary>
        public static void IntroduceSwimmer(WorldSession viewer, WorldSession subject)
        {
            byte[] state;
            lock (sync)
            {
                if (!swimmers.TryGetValue(subject, out Swimmer s))
                    return;
                state = s.Pending ?? s.LastSent;
            }
            if (state.AsSpan().IndexOfAnyExcept((byte)0) < 0)
                return;
            if (Vector3.Distance(viewer.Position, subject.Position) > RelayRange)
                return;
            byte[] body = new byte[HeaderLength - 2 + state.Length];
            body[0] = SwimmingUpdate;
            BitConverter.GetBytes(subject.PlayerGuid).CopyTo(body, 1);
            state.CopyTo(body, HeaderLength - 2);
            if ((body[HeaderLength - 2] & 0x40) != 0)
                body[HeaderLength - 2] |= DiveFlag;
            viewer.EnqueueMessage(ZoneMessageOpcode.HeroicMovement, body);
        }

        /// <summary>A player left the world: their pending swimming state goes.</summary>
        public static void Forget(WorldSession session)
        {
            lock (sync)
                swimmers.Remove(session);
        }
    }
}
