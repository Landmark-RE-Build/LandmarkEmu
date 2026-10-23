using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Proximity facial-anim relay (<c>0xF1</c>→<c>0xF2</c>) and receiver preference. Opaque payload fan-out within
    /// <see cref="ViewRange"/>; hold deltas until each viewer has seen a natural keyframe (no synthesized catch-up).
    /// See <c>research/notes/FacialAnimDeltaContract.md</c>.
    /// </summary>
    public static class FacialAnim
    {
        /// <summary>How far other players receive face frames, metres (same band as <see cref="Emotes.ViewRange"/>).</summary>
        public const float ViewRange = 80f;

        private static readonly object seedSync = new();
        /// <summary>speaker TransientId → viewer CharacterIds that have received a keyframe this proximity visit.</summary>
        private static readonly Dictionary<uint, HashSet<ulong>> seededBySpeaker = new();

        /// <summary>World config master enable (<c>FACIAL_ANIM_RECEIVER</c>). When false, SendSelf and all F2 are off.</summary>
        public static bool MasterEnabled
            => ConfigurationManager<WorldServerConfiguration>.Instance.Config.FacialAnimReceiver;

        /// <summary>Mode-3 keyframe when <c>payload[0] &amp; 1 != 0</c> (delta contract).</summary>
        public static bool IsMode3Keyframe(ReadOnlySpan<byte> payload)
            => payload.Length > 0 && (payload[0] & 1) != 0;

        /// <summary>
        /// Whether this viewer should get this frame, and whether to mark them seeded afterward.
        /// Unseeded viewers only receive keyframes; seeded viewers receive every opaque frame.
        /// </summary>
        public static (bool Send, bool MarkSeeded) RelayDecision(bool viewerSeeded, bool payloadIsKeyframe)
        {
            if (viewerSeeded)
                return (true, false);
            if (payloadIsKeyframe)
                return (true, true);
            return (false, false);
        }

        /// <summary>Effective receive preference: master enable AND the character's stored opt-in.</summary>
        public static bool WantsReceive(WorldSession session)
            => MasterEnabled && session != null && !session.Disconnected && session.FacialAnimReceiver;

        /// <summary>
        /// Opaque F1→F2 fan-out from <paramref name="speaker"/> to nearby peers that want faces. Does not echo to the speaker.
        /// </summary>
        public static int RelayFromClient(WorldSession speaker, byte queueWeight, byte[] payload)
        {
            if (speaker == null || speaker.Disconnected || payload == null || payload.Length == 0 || !MasterEnabled)
                return 0;
            return Fanout(speaker.ZoneId, speaker.Position, speaker.TransientId, queueWeight, payload, exclude: speaker);
        }

        /// <summary>
        /// S→C F2 to nearby receivers for a speaker that is not a WorldSession (e.g. a probe entity). Origin is that speaker's position.
        /// </summary>
        public static int Fanout(uint zoneId, Vector3 origin, uint speakerTransientId, byte queueWeight, byte[] payload,
            WorldSession exclude = null)
        {
            if (payload == null || payload.Length == 0 || !MasterEnabled)
                return 0;

            bool isKeyframe = IsMode3Keyframe(payload);
            var packet = new ZoneProximityFacialAnim
            {
                CharacterId = speakerTransientId,
                QueueWeight = queueWeight,
                Payload = payload,
            };

            int sent = 0;
            foreach (WorldSession viewer in PlayerManager.InZone(zoneId))
            {
                if (viewer == exclude || viewer.Disconnected)
                    continue;

                float dist = Vector3.Distance(viewer.Position, origin);
                if (dist > ViewRange)
                {
                    ClearSeeded(speakerTransientId, viewer.CharacterId);
                    continue;
                }
                if (!WantsReceive(viewer))
                {
                    ClearSeeded(speakerTransientId, viewer.CharacterId);
                    continue;
                }

                bool seeded = IsSeeded(speakerTransientId, viewer.CharacterId);
                (bool send, bool markSeeded) = RelayDecision(seeded, isKeyframe);
                if (!send)
                    continue;

                viewer.EnqueueMessage(packet);
                if (markSeeded)
                    MarkSeeded(speakerTransientId, viewer.CharacterId);
                sent++;
            }
            return sent;
        }

        /// <summary>Drop all seed state for a speaker (logout / despawn).</summary>
        public static void ClearSpeaker(uint speakerTransientId)
        {
            lock (seedSync)
                seededBySpeaker.Remove(speakerTransientId);
        }

        private static bool IsSeeded(uint speakerTransientId, ulong viewerCharacterId)
        {
            if (viewerCharacterId == 0)
                return false;
            lock (seedSync)
                return seededBySpeaker.TryGetValue(speakerTransientId, out HashSet<ulong> set)
                    && set.Contains(viewerCharacterId);
        }

        private static void MarkSeeded(uint speakerTransientId, ulong viewerCharacterId)
        {
            if (viewerCharacterId == 0)
                return;
            lock (seedSync)
            {
                if (!seededBySpeaker.TryGetValue(speakerTransientId, out HashSet<ulong> set))
                {
                    set = new HashSet<ulong>();
                    seededBySpeaker[speakerTransientId] = set;
                }
                set.Add(viewerCharacterId);
            }
        }

        private static void ClearSeeded(uint speakerTransientId, ulong viewerCharacterId)
        {
            if (viewerCharacterId == 0)
                return;
            lock (seedSync)
            {
                if (!seededBySpeaker.TryGetValue(speakerTransientId, out HashSet<ulong> set))
                    return;
                set.Remove(viewerCharacterId);
                if (set.Count == 0)
                    seededBySpeaker.Remove(speakerTransientId);
            }
        }
    }
}
