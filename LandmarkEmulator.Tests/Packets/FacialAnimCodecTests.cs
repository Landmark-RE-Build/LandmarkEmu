using LandmarkEmulator.WorldServer.Network.Message.Model;
using System;
using Xunit;

namespace LandmarkEmulator.Tests.Packets
{
    public class FacialAnimCodecTests
    {
        [Fact]
        public void EncodeMode3Keyframe_IsOnePlusUncompressedLeaf()
        {
            var channels = new float[ZoneProximityFacialAnim.ChannelCount];
            channels[0] = 1f;
            channels[6] = 0.5f;

            byte[] leaf = ZoneProximityFacialAnim.EncodeUncompressed(channels, 0f, 0f, 0f);
            byte[] keyed = ZoneProximityFacialAnim.EncodeMode3Keyframe(channels, 0f, 0f, 0f);

            Assert.Equal(39, leaf.Length);
            Assert.Equal(40, keyed.Length);
            Assert.Equal(1, keyed[0]);
            Assert.Equal(leaf, keyed.AsSpan(1).ToArray());
            Assert.Equal(255, keyed[1]); // ch0 = 1.0 → u8 255
            Assert.Equal(128, keyed[7]); // ch6 = 0.5 → u8 128
            // Neutral head 0° → u16 32400 = 0x7E90 LE at leaf offs 33..38 → keyed 34..39
            Assert.Equal(0x90, keyed[34]);
            Assert.Equal(0x7E, keyed[35]);
        }

        [Fact]
        public void WrapMode3Keyframe_RejectsWrongLength()
        {
            Assert.Throws<ArgumentException>(() =>
                ZoneProximityFacialAnim.WrapMode3Keyframe(new byte[38]));
        }
    }
}
