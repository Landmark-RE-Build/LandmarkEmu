using NLog;
using System;
using System.Buffers.Binary;
using System.Text;

namespace LandmarkEmulator.Shared.Network
{
    public class GamePacketReader
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public uint TotalBytes => (uint)stream.Length;
        public uint BytesRemaining => (uint)(stream.Length - currentBytePosition);

        private int currentBytePosition;
        private byte[] stream;

        public GamePacketReader(byte[] input)
        {
            stream = input;
            ResetBits();
        }

        public void ReverseBytePosition(int count)
        {
            currentBytePosition -= count;
        }

        public void ResetBits()
        {
            if (currentBytePosition > 7)
                return;

            currentBytePosition = 0;
        }

        public bool ReadBool(uint bits = 8u)
        {
            if (bits > sizeof(byte) * 8)
                throw new ArgumentException();

            return Convert.ToBoolean(GetDataBits(bits)[0]);
        }

        public byte ReadByte(uint bits = 8u)
        {
            if (bits > sizeof(byte) * 8)
                throw new ArgumentException();

            return GetDataBits(bits)[0];
        }

        public ushort ReadUShort(uint bits = 16u)
        {
            if (bits > sizeof(ushort) * 8)
                throw new ArgumentException();

            return BinaryPrimitives.ReadUInt16LittleEndian(GetDataBits(bits));
        }

        /// <summary>
        /// The client's 2-bit-length uint (GamePacketWriter.WriteUIntWith2BitLength): little endian, the low 2 bits of the
        /// first byte are the number of bytes that follow, the value is the whole thing shifted right by 2. The first
        /// byte's upper 6 bits are part of the value: they used to be dropped, so every id below 64 read as 0 and larger
        /// ones lost their low 6 bits (SendSelfToClient's transient id, the capture parser).
        /// </summary>
        public uint ReadUInt2BitLength()
        {
            uint first = GetDataBits(8u)[0];
            uint length = first & 3;
            uint value = first;

            for (int i = 0; i < length; i++)
            {
                value |= (uint)GetDataBits(8u)[0] << ((i + 1) * 8);
            }

            return value >> 2;
        }

        public uint ReadUInt(uint bits = 32u)
        {
            if (bits > sizeof(uint) * 8)
                throw new ArgumentException();

            return BinaryPrimitives.ReadUInt32LittleEndian(GetDataBits(bits));
        }

        public int ReadInt(uint bits = 32u)
        {
            if (bits > sizeof(uint) * 8)
                throw new ArgumentException();

            return BinaryPrimitives.ReadInt32LittleEndian(GetDataBits(bits));
        }

        public unsafe float ReadSingle(uint bits = 32u)
        {
            if (bits > sizeof(float) * 8)
                throw new ArgumentException();

            return BinaryPrimitives.ReadSingleLittleEndian(GetDataBits(bits));
        }

        public ulong ReadULong(uint bits = 64u)
        {
            if (bits > sizeof(ulong) * 8)
                throw new ArgumentException();

            return BinaryPrimitives.ReadUInt64LittleEndian(GetDataBits(bits));
        }

        public long ReadLong(uint bits = 64u)
        {
            if (bits > sizeof(ulong) * 8)
                throw new ArgumentException();

            return BinaryPrimitives.ReadInt64LittleEndian(GetDataBits(bits));
        }

        public unsafe double ReadDouble(uint bits = 64u)
        {
            if (bits > sizeof(double) * 8)
                throw new ArgumentException();

            return BinaryPrimitives.ReadDoubleLittleEndian(GetDataBits(bits));
        }

        public T ReadEnum<T>(uint bits = 64u) where T : Enum
        {
            if (bits > sizeof(ulong) * 8)
                throw new ArgumentException();

            return (T)Enum.ToObject(typeof(T), 0ul); // ReadBits(bits));
        }

        /// <summary>
        /// Reads <paramref name="length"/> bytes. The length usually comes from the packet itself (string and array
        /// prefixes), so it is checked against what is left before anything is allocated: a forged length of ~4 GB
        /// would otherwise make the server try to allocate it.
        /// </summary>
        public byte[] ReadBytes(uint length)
        {
            if (length > BytesRemaining)
                throw new System.IO.EndOfStreamException($"{length} bytes wanted, {BytesRemaining} left.");
            byte[] data = new byte[length];
            Buffer.BlockCopy(stream, currentBytePosition, data, 0, (int)length);
            currentBytePosition += (int)length;
            return data;
        }

        public string ReadShortLengthString()
        {
            var length = ReadUShort();
            var value = ReadBytes(length);

            if (length > 0)
                return Encoding.UTF8.GetString(value);
            else
                return "";
        }

        public string ReadString()
        {
            var length = ReadUInt();
            var value = ReadBytes(length);

            if (length > 0)
                return Encoding.UTF8.GetString(value);
            else
                return "";
        }

        public string ReadNullTerminatedString()
        {
            var data = new byte[BytesRemaining];
            Buffer.BlockCopy(stream, currentBytePosition, data, 0, data.Length);

            int nullterm = 0;
            while (nullterm < BytesRemaining && data[nullterm] != 0)
            {
                nullterm = nullterm + 1; // Don't include the null terminator as part of parse
            }

            string s = Encoding.UTF8.GetString(data, 0, nullterm);
            currentBytePosition += nullterm + 1; // Adjust byte position to nullterm + 1, so that bytePosition is at the byte after the null terminator
            return s;
        }

        public ReadOnlySpan<byte> GetDataBits(uint bits)
        {
            byte[] data = new byte[bits / 8];
            Buffer.BlockCopy(stream, currentBytePosition, data, 0, data.Length);

            currentBytePosition += (byte)bits / 8;
            return new ReadOnlySpan<byte>(data);
        }

        public byte[] GetRemainingData()
        {
            byte[] data = new byte[BytesRemaining];
            Buffer.BlockCopy(stream, currentBytePosition, data, 0, data.Length);
            return data;
        }
    }
}
