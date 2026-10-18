using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LandmarkEmulator.Tests.Support
{
    /// <summary>Byte helpers: write a model, read one back, and build expected layouts field by field.</summary>
    public static class Wire
    {
        public static byte[] Write(IWritable message)
        {
            var data = new List<byte>();
            message.Write(new GamePacketWriter(data));
            return data.ToArray();
        }

        public static byte[] Write(Action<GamePacketWriter> write)
        {
            var data = new List<byte>();
            write(new GamePacketWriter(data));
            return data.ToArray();
        }

        /// <summary>Reads <paramref name="data"/> into a new <typeparamref name="T"/>; returns it and the bytes left.</summary>
        public static (T Message, uint Left) Read<T>(byte[] data) where T : IReadable, new()
        {
            var message = new T();
            var reader = new GamePacketReader(data);
            message.Read(reader);
            return (message, reader.BytesRemaining);
        }

        /// <summary>"26 00 58 00 00 00" / "2600580000 00" → bytes (spaces ignored).</summary>
        public static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace(" ", "").Replace("-", ""));

        public static string Show(byte[] data) => BitConverter.ToString(data).Replace('-', ' ');
    }

    /// <summary>Builds an expected little-endian layout: <c>new Layout().U32(1).U64(2).Str("x")</c>.</summary>
    public sealed class Layout
    {
        private readonly List<byte> bytes = new();

        public Layout U8(byte v) { bytes.Add(v); return this; }
        public Layout Bool(bool v) { bytes.Add(v ? (byte)1 : (byte)0); return this; }
        public Layout U16(ushort v) { bytes.AddRange(BitConverter.GetBytes(v)); return this; }
        public Layout I16(short v) { bytes.AddRange(BitConverter.GetBytes(v)); return this; }
        public Layout U32(uint v) { bytes.AddRange(BitConverter.GetBytes(v)); return this; }
        public Layout I32(int v) { bytes.AddRange(BitConverter.GetBytes(v)); return this; }
        public Layout U64(ulong v) { bytes.AddRange(BitConverter.GetBytes(v)); return this; }
        public Layout F32(float v) { bytes.AddRange(BitConverter.GetBytes(v)); return this; }
        public Layout F64(double v) { bytes.AddRange(BitConverter.GetBytes(v)); return this; }
        public Layout Vec4(float x, float y, float z, float w) => F32(x).F32(y).F32(z).F32(w);

        /// <summary>A u32-length-prefixed UTF-8 string (GamePacketWriter.Write(string)).</summary>
        public Layout Str(string v)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(v);
            U32((uint)utf8.Length);
            bytes.AddRange(utf8);
            return this;
        }

        public Layout Raw(params byte[] v) { bytes.AddRange(v); return this; }
        public Layout Raw(string hex) { bytes.AddRange(Wire.Hex(hex)); return this; }

        /// <summary>The client's 2-bit-length unsigned int: value &lt;&lt; 2 | extra byte count, little endian.</summary>
        public Layout Packed(uint v)
        {
            uint shifted = v << 2;
            int extra = shifted > 0xFFFFFF ? 3 : shifted > 0xFFFF ? 2 : shifted > 0xFF ? 1 : 0;
            shifted |= (uint)extra;
            bytes.AddRange(BitConverter.GetBytes(shifted).Take(extra + 1));
            return this;
        }

        public byte[] ToArray() => bytes.ToArray();
        public int Length => bytes.Count;
        public override string ToString() => Wire.Show(ToArray());
    }
}
