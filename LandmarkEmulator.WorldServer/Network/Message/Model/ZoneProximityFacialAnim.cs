using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using System;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// S→C <c>0xF2</c> ZoneProximityFacialAnim: applies a face-channel frame to a character entity on the client.
    /// Wire header (Game reader): SOE-compressed character id, queue weight, length-prefixed <b>codec</b> payload.
    /// Uncompressed leaf is 33×u8 channels + 3×u16 LE head rot (<c>(deg + 90) * 360</c>).
    /// With default cvar <c>livedriver_compression_type == 3</c>, the payload must be a delta-codec blob
    /// (keyframe <c>[1]+39</c> or masked delta) — not a bare 39-byte leaf. See
    /// <c>research/notes/FacialAnimDeltaContract.md</c>.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ZoneProximityFacialAnim)]
    public class ZoneProximityFacialAnim : IWritable
    {
        public const int ChannelCount = 33;
        public const int UncompressedPayloadLength = ChannelCount + 6;
        public const int Mode3KeyframePayloadLength = 1 + UncompressedPayloadLength;

        /// <summary>
        /// Entity id on the wire (SOE compressed int). Live RE names this characterId; for zone entities the client
        /// resolves the same way as other compressed ids — we send the puppet/player <c>TransientId</c> (not the u64 guid).
        /// </summary>
        public uint CharacterId { get; set; }

        /// <summary>Playback budget byte on the client queue.</summary>
        public byte QueueWeight { get; set; } = 1;

        /// <summary>Delta-codec payload (mode 3 keyframe 40 B, or sparse delta) — not a bare 39 B leaf.</summary>
        public byte[] Payload { get; set; } = Array.Empty<byte>();

        public void Write(GamePacketWriter writer)
        {
            writer.WriteUIntWith2BitLength((int)(uint)CharacterId);
            writer.Write(QueueWeight);
            writer.Write((uint)(Payload?.Length ?? 0));
            if (Payload != null && Payload.Length > 0)
                writer.WriteBytes(Payload);
        }

        /// <summary>
        /// Builds the uncompressed 39-byte leaf (channels + head). For on-wire mode 3, wrap with
        /// <see cref="EncodeMode3Keyframe"/> — receivers will not apply a bare leaf.
        /// </summary>
        public static byte[] EncodeUncompressed(ReadOnlySpan<float> channels, float headXDeg, float headYDeg, float headZDeg)
        {
            var payload = new byte[UncompressedPayloadLength];
            for (int i = 0; i < ChannelCount; i++)
            {
                float v = i < channels.Length ? channels[i] : 0f;
                if (v < 0f) v = 0f;
                if (v > 1f) v = 1f;
                payload[i] = (byte)MathF.Round(v * 255f);
            }
            WriteHeadRot(payload.AsSpan(ChannelCount), headXDeg);
            WriteHeadRot(payload.AsSpan(ChannelCount + 2), headYDeg);
            WriteHeadRot(payload.AsSpan(ChannelCount + 4), headZDeg);
            return payload;
        }

        /// <summary>
        /// Mode-3 keyframe: <c>payload[0]=1</c> plus a full 39-byte uncompressed frame (wire length 40).
        /// Reseeds the client's per-character prev buffer; safe for every probe frame (deltas optional later).
        /// </summary>
        public static byte[] EncodeMode3Keyframe(ReadOnlySpan<float> channels, float headXDeg, float headYDeg, float headZDeg)
        {
            byte[] leaf = EncodeUncompressed(channels, headXDeg, headYDeg, headZDeg);
            var payload = new byte[Mode3KeyframePayloadLength];
            payload[0] = 1;
            Buffer.BlockCopy(leaf, 0, payload, 1, leaf.Length);
            return payload;
        }

        /// <summary>
        /// Mode-3 keyframe from an already-built 39-byte leaf (e.g. raw u8 probe path).
        /// </summary>
        public static byte[] WrapMode3Keyframe(ReadOnlySpan<byte> uncompressed39)
        {
            if (uncompressed39.Length != UncompressedPayloadLength)
                throw new ArgumentException($"Need {UncompressedPayloadLength} uncompressed bytes.", nameof(uncompressed39));
            var payload = new byte[Mode3KeyframePayloadLength];
            payload[0] = 1;
            uncompressed39.CopyTo(payload.AsSpan(1));
            return payload;
        }

        private static void WriteHeadRot(Span<byte> dest, float deg)
        {
            int q = (int)MathF.Round((deg + 90f) * 360f);
            if (q < 0) q = 0;
            if (q > 65535) q = 65535;
            dest[0] = (byte)q;
            dest[1] = (byte)(q >> 8);
        }
    }
}
