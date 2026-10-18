using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LandmarkEmulator.Internal
{
    /// <summary>Writes an internal packet: little endian like the game, strings as u32 length + UTF-8.</summary>
    public sealed class InternalWriter
    {
        private readonly List<byte> bytes = new();

        public int Length => bytes.Count;

        public InternalWriter U8(byte v) { bytes.Add(v); return this; }
        public InternalWriter Bool(bool v) => U8(v ? (byte)1 : (byte)0);
        public InternalWriter U16(ushort v) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, v); bytes.AddRange(b.ToArray()); return this; }
        public InternalWriter U32(uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); bytes.AddRange(b.ToArray()); return this; }
        public InternalWriter U64(ulong v) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(b, v); bytes.AddRange(b.ToArray()); return this; }
        public InternalWriter F32(float v) => U32(BitConverter.SingleToUInt32Bits(v));
        public InternalWriter Bytes(ReadOnlySpan<byte> v) { bytes.AddRange(v.ToArray()); return this; }

        public InternalWriter Str(string v)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(v ?? "");
            U32((uint)utf8.Length);
            return Bytes(utf8);
        }

        public byte[] ToArray() => bytes.ToArray();
    }

    /// <summary>Reads an internal packet; throws <see cref="InvalidDataException"/> when it runs short.</summary>
    public sealed class InternalReader
    {
        private readonly byte[] data;
        private int position;

        public InternalReader(byte[] data, int start = 0)
        {
            this.data = data;
            position = start;
        }

        public int Remaining => data.Length - position;

        private ReadOnlySpan<byte> Take(int n)
        {
            if (n < 0 || position + n > data.Length)
                throw new InvalidDataException($"Internal packet too short: wanted {n} byte(s) at {position} of {data.Length}.");
            var span = new ReadOnlySpan<byte>(data, position, n);
            position += n;
            return span;
        }

        public byte U8() => Take(1)[0];
        public bool Bool() => U8() != 0;
        public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        public ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
        public float F32() => BitConverter.UInt32BitsToSingle(U32());
        public byte[] Bytes(int n) => Take(n).ToArray();
        public byte[] Rest() => Take(Remaining).ToArray();

        public string Str()
        {
            uint n = U32();
            if (n > 1 << 20)
                throw new InvalidDataException($"Internal string of {n} bytes refused.");
            return Encoding.UTF8.GetString(Take((int)n));
        }
    }
}
