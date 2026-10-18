using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Tests.Support;
using System;
using System.Collections.Generic;
using Xunit;

namespace LandmarkEmulator.Tests.Packets
{
    /// <summary>GamePacketWriter / GamePacketReader primitives: widths, endianness, strings, the 2-bit-length uint.</summary>
    public class PacketPrimitiveTests
    {
        [Fact]
        public void Widths_AreTheClients()
        {
            byte[] bytes = Wire.Write(w =>
            {
                w.Write((byte)0x11);
                w.Write((ushort)0x2233);
                w.Write(0x44556677u);
                w.Write(0x8899AABBCCDDEEFFul);
                w.Write(true);
                w.Write(1.5f);
                w.Write(2.5);
                w.Write("ab");
            });
            Assert.Equal(new Layout().U8(0x11).U16(0x2233).U32(0x44556677).U64(0x8899AABBCCDDEEFF).Bool(true).F32(1.5f).F64(2.5)
                .Str("ab").ToArray(), bytes);
        }

        [Fact]
        public void Reader_ReadsWhatTheWriterWrote()
        {
            byte[] bytes = new Layout().U8(7).U16(0xBEEF).U32(0xDEADBEEF).I32(-5).U64(ulong.MaxValue - 1).F32(-0.25f).F64(1e300)
                .Str("Landmark ü").Bool(true).ToArray();
            var r = new GamePacketReader(bytes);
            Assert.Equal((byte)7, r.ReadByte());
            Assert.Equal((ushort)0xBEEF, r.ReadUShort());
            Assert.Equal(0xDEADBEEFu, r.ReadUInt());
            Assert.Equal(-5, r.ReadInt());
            Assert.Equal(ulong.MaxValue - 1, r.ReadULong());
            Assert.Equal(-0.25f, r.ReadSingle());
            Assert.Equal(1e300, r.ReadDouble());
            Assert.Equal("Landmark ü", r.ReadString());
            Assert.True(r.ReadBool());
            Assert.Equal(0u, r.BytesRemaining);
        }

        public static IEnumerable<object[]> PackedValues => new[]
        {
            new object[] { 0u, "00" },
            new object[] { 1u, "04" },
            new object[] { 63u, "FC" },
            new object[] { 64u, "01 01" },
            new object[] { 0x3FFFu, "FD FF" },
            new object[] { 0x4000u, "02 00 01" },
            new object[] { 0x3FFFFFu, "FE FF FF" },
            new object[] { 0x400000u, "03 00 00 01" },
            new object[] { 0x123456u, "5A D1 48" },
        };

        /// <summary>The 2-bit-length uint (transient ids; docs/protocol.md §4.3): value &lt;&lt; 2 | extra bytes, LE.</summary>
        [Theory]
        [MemberData(nameof(PackedValues))]
        public void PackedUInt_Layout(uint value, string hex)
        {
            Assert.Equal(Wire.Hex(hex), Wire.Write(w => w.WriteUIntWith2BitLength((int)value)));
            Assert.Equal(Wire.Hex(hex), new Layout().Packed(value).ToArray());
        }

        [Theory]
        [MemberData(nameof(PackedValues))]
        public void PackedUInt_ReadsBack(uint value, string hex)
        {
            var reader = new GamePacketReader(Wire.Hex(hex + " AA"));
            Assert.Equal(value, reader.ReadUInt2BitLength());
            Assert.Equal(1u, reader.BytesRemaining);
        }

        [Fact]
        public void ReadBytes_RefusesAForgedLength()
        {
            var reader = new GamePacketReader(new Layout().U32(0xFFFFFFF0).ToArray());
            Assert.Throws<System.IO.EndOfStreamException>(() => reader.ReadString());
        }

        [Fact]
        public void Writer_U16OverloadWritesTwoBytes()
        {
            ushort sub = 0x0102;
            Assert.Equal(2, Wire.Write(w => w.Write(sub)).Length);
        }
    }
}
