using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using System;
using Xunit;

namespace LandmarkEmulator.Tests.Packets
{
    public class FacialAnimRelayTests
    {
        [Fact]
        public void IsMode3Keyframe_UsesLowBitOfFirstByte()
        {
            Assert.False(FacialAnim.IsMode3Keyframe(ReadOnlySpan<byte>.Empty));
            Assert.False(FacialAnim.IsMode3Keyframe(new byte[] { 0 }));
            Assert.True(FacialAnim.IsMode3Keyframe(new byte[] { 1 }));
            Assert.True(FacialAnim.IsMode3Keyframe(new byte[] { 3 }));
            Assert.False(FacialAnim.IsMode3Keyframe(new byte[] { 2 }));
        }

        [Theory]
        [InlineData(false, false, false, false)] // unseeded delta → skip
        [InlineData(false, true, true, true)]    // unseeded keyframe → send + seed
        [InlineData(true, false, true, false)]   // seeded delta → send
        [InlineData(true, true, true, false)]    // seeded keyframe → send
        public void RelayDecision_WaitForNaturalKeyframe(bool seeded, bool keyframe, bool expectSend, bool expectMark)
        {
            (bool send, bool mark) = FacialAnim.RelayDecision(seeded, keyframe);
            Assert.Equal(expectSend, send);
            Assert.Equal(expectMark, mark);
        }

        [Fact]
        public void ClientFacialAnimSettings_ReadsReceiverByte()
        {
            var on = new ClientFacialAnimSettings();
            on.Read(new GamePacketReader(new byte[] { 1 }));
            Assert.True(on.Receiver);

            var off = new ClientFacialAnimSettings();
            off.Read(new GamePacketReader(new byte[] { 0 }));
            Assert.False(off.Receiver);
        }

        [Fact]
        public void ClientFacialAnim_ReadsWeightAndLengthPrefixedPayload()
        {
            var body = new byte[1 + 4 + 3];
            body[0] = 7; // weight
            body[1] = 3; // length LE
            body[2] = 0;
            body[3] = 0;
            body[4] = 0;
            body[5] = 0xAA;
            body[6] = 0xBB;
            body[7] = 0xCC;

            var frame = new ClientFacialAnim();
            frame.Read(new GamePacketReader(body));
            Assert.Equal(7, frame.QueueWeight);
            Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC }, frame.Payload);
        }

        [Fact]
        public void ZoneProximityFacialAnim_Write_IncludesCompressedIdWeightAndPayload()
        {
            var packet = new ZoneProximityFacialAnim
            {
                CharacterId = 1,
                QueueWeight = 2,
                Payload = new byte[] { 1, 2, 3 },
            };
            var bytes = new System.Collections.Generic.List<byte>();
            packet.Write(new GamePacketWriter(bytes));
            // Transient 1 → 2-bit-length encoding: (1 << 2) | 0 = 4 in one byte
            Assert.Equal(4, bytes[0]);
            Assert.Equal(2, bytes[1]);
            Assert.Equal(3, bytes[2]); // length LE first byte
            Assert.Equal(0, bytes[3]);
            Assert.Equal(0, bytes[4]);
            Assert.Equal(0, bytes[5]);
            Assert.Equal(new byte[] { 1, 2, 3 }, bytes.GetRange(6, 3).ToArray());
        }
    }
}
