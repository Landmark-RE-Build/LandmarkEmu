using System;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Network
{
    /// <summary>
    /// Reads the player's position and rotation from packets on the gateway's PositionUpdate channel.
    /// </summary>
    /// <remarks>
    /// Layout per the 0.589 client's reader: type byte, uint32, byte, then values packed as
    /// "signed int with 2-bit length" (bit 0 of the first byte is the sign, bits 1-2 the number of extra bytes, the
    /// little-endian value shifted right by 3 is the magnitude), each divided by 10^precision:
    /// <list type="bullet">
    /// <item>0x00: position (3, p2), rotation (4, p5), velocity (3, p5), 4 (p2), 4 (p2), 1 (p1), 1 (p5), flags.</item>
    /// <item>0x02: position (3, p2), velocity (3, p5).</item>
    /// <item>0x03: rotation quaternion x, y, z, w (4, p5), 4 (p2).</item>
    /// </list>
    /// </remarks>
    public static class PositionUpdate
    {
        private const int FieldsOffset = 6;

        public static bool TryRead(byte[] data, out Vector3? position, out Quaternion? rotation)
        {
            position = null;
            rotation = null;
            if (data.Length <= FieldsOffset)
                return false;

            int offset = FieldsOffset;
            switch (data[0])
            {
                case 0x00:
                    if (!TryReadVector3(data, ref offset, 2, out Vector3 fullPosition)
                        || !TryReadQuaternion(data, ref offset, out Quaternion fullRotation))
                        return false;
                    position = fullPosition;
                    rotation = fullRotation;
                    return true;
                case 0x02:
                    if (!TryReadVector3(data, ref offset, 2, out Vector3 movedPosition))
                        return false;
                    position = movedPosition;
                    return true;
                case 0x03:
                    if (!TryReadQuaternion(data, ref offset, out Quaternion turnedRotation))
                        return false;
                    rotation = turnedRotation;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Converts a movement body from the sender's local map frame to the frame other clients expect.
        /// </summary>
        /// <remarks>
        /// A client subtracts the zone origin / 10 from positions it receives for other characters (0.589 client,
        /// confirmed live: relaying local positions put characters exactly origin/10 away), while its own updates
        /// are already local. Only the position (the first 3 values of types 0 and 2) changes; the rest is copied.
        /// </remarks>
        public static byte[] ToRemoteFrame(byte[] data, Vector3 offset)
        {
            if (data.Length <= FieldsOffset || (data[0] != 0x00 && data[0] != 0x02))
                return data;

            int offset0 = FieldsOffset;
            int end = offset0;
            var values = new int[3];
            for (int i = 0; i < 3; i++)
                if (!TryReadSignedInt(data, ref end, out values[i]))
                    return data;

            var result = new System.Collections.Generic.List<byte>(data.Length + 6);
            result.AddRange(data.AsSpan(0, offset0).ToArray());
            WriteSignedInt(result, values[0] + (int)MathF.Round(offset.X * 100f));
            WriteSignedInt(result, values[1] + (int)MathF.Round(offset.Y * 100f));
            WriteSignedInt(result, values[2] + (int)MathF.Round(offset.Z * 100f));
            result.AddRange(data.AsSpan(end).ToArray());
            return result.ToArray();
        }

        /// <summary>
        /// Writes a signed int with 2-bit length: bit 0 sign, bits 1-2 extra byte count, magnitude shifted left by 3.
        /// </summary>
        private static void WriteSignedInt(System.Collections.Generic.List<byte> output, int value)
        {
            uint magnitude = (uint)Math.Abs(value);
            uint raw = magnitude << 3;
            int extra = raw > 0xFFFFFF ? 3 : raw > 0xFFFF ? 2 : raw > 0xFF ? 1 : 0;
            raw |= (uint)(extra << 1) | (value < 0 ? 1u : 0u);
            for (int i = 0; i <= extra; i++)
                output.Add((byte)(raw >> (8 * i)));
        }

        private static bool TryReadVector3(byte[] data, ref int offset, int precision, out Vector3 value)
        {
            value = default;
            if (!TryReadFloat(data, ref offset, precision, out float x)
                || !TryReadFloat(data, ref offset, precision, out float y)
                || !TryReadFloat(data, ref offset, precision, out float z))
                return false;

            value = new Vector3(x, y, z);
            return true;
        }

        private static bool TryReadQuaternion(byte[] data, ref int offset, out Quaternion value)
        {
            value = default;
            if (!TryReadFloat(data, ref offset, 5, out float x)
                || !TryReadFloat(data, ref offset, 5, out float y)
                || !TryReadFloat(data, ref offset, 5, out float z)
                || !TryReadFloat(data, ref offset, 5, out float w))
                return false;

            value = new Quaternion(x, y, z, w);
            return true;
        }

        private static bool TryReadFloat(byte[] data, ref int offset, int precision, out float value)
        {
            value = 0f;
            if (!TryReadSignedInt(data, ref offset, out int raw))
                return false;

            value = raw / MathF.Pow(10f, precision);
            return true;
        }

        private static bool TryReadSignedInt(byte[] data, ref int offset, out int value)
        {
            value = 0;
            if (offset >= data.Length)
                return false;

            int first = data[offset];
            int extra = (first >> 1) & 3;
            if (offset + extra >= data.Length)
                return false;

            uint raw = (uint)first;
            for (int i = 0; i < extra; i++)
                raw |= (uint)data[offset + 1 + i] << (8 * (i + 1));

            value = (int)(raw >> 3);
            if ((first & 1) != 0)
                value = -value;

            offset += extra + 1;
            return true;
        }
    }
}
