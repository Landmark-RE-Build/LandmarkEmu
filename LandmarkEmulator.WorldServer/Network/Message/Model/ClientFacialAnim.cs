using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using System;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// C→S <c>0xF1</c> ClientFacialAnim: the client's LiveDriver / SOEmote face frame for proximity relay.
    /// Wire (inverse of <see cref="ZoneProximityFacialAnim"/> without the compressed character id): queue weight,
    /// length-prefixed mode-3 codec payload. Relayed opaquely as S→C <c>0xF2</c> to nearby peers.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ClientFacialAnim)]
    public class ClientFacialAnim : IReadable
    {
        /// <summary>Playback budget byte (same role as on <see cref="ZoneProximityFacialAnim"/>).</summary>
        public byte QueueWeight { get; set; } = 1;

        /// <summary>Delta-codec payload (keyframe or masked delta) — forwarded unchanged into <c>0xF2</c>.</summary>
        public byte[] Payload { get; set; } = Array.Empty<byte>();

        public void Read(GamePacketReader reader)
        {
            QueueWeight = reader.BytesRemaining > 0 ? reader.ReadByte() : (byte)1;
            if (reader.BytesRemaining < 4)
            {
                Payload = Array.Empty<byte>();
                return;
            }
            uint length = reader.ReadUInt();
            if (length == 0 || length > reader.BytesRemaining)
            {
                Payload = Array.Empty<byte>();
                return;
            }
            Payload = reader.ReadBytes(length);
        }
    }
}
