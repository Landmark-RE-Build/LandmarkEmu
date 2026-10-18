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

        /// <summary>
        /// Movement state bit 0x2000000: the character is sitting [P]. The writer FUN_142ca4ce0 copies it from the
        /// character's flag +0x1461 bit 0x4, which the client's own /sit sets (FUN_1433b4e90 → FUN_142ec31e0; the client
        /// then sits in movement mode 6, FUN_142ecd1d0 → FUN_142eadb50 → FUN_142ea4a70, AnimationSelection 351). An
        /// observer applies it in FUN_142ea8bb0: set → the same flag, so that client sits the character too; cleared while
        /// set → FUN_142e93b20 stands it up (selection 478) and clears the flag. The bit is in the full-body mask
        /// 0x201f810, so each change goes out as a type-0 body (kept as the player's last full movement for newcomers).
        /// The flag is only cleared on the sitting client by moving / jumping (FUN_142e93b20) or by
        /// RequestAnimationSelection's clear-sit bool (FUN_142ec9840): a server animation alone leaves it set.
        /// </summary>
        public const uint SittingState = 0x2000000;

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
        /// The velocity (m/s) of a movement body that carries one: type 0 after position and rotation, type 2 after the
        /// position (layouts above). Used for "is the player in the air" checks (Consumables, TASKS 6.19).
        /// </summary>
        public static bool TryReadVelocity(byte[] data, out Vector3 velocity)
        {
            velocity = default;
            if (data.Length <= FieldsOffset || (data[0] != 0x00 && data[0] != 0x02))
                return false;
            int offset = FieldsOffset;
            if (!TryReadVector3(data, ref offset, 2, out _))
                return false;
            if (data[0] == 0x00 && !TryReadQuaternion(data, ref offset, out _))
                return false;
            return TryReadVector3(data, ref offset, 5, out velocity);
        }

        /// <summary>
        /// The movement state word (2-bit-length uint: bits 0-1 the extra byte count, the value shifted left 2): type 0
        /// after its 20 packed values, type 1 alone (docs/protocol.md 4.3; types 2-5 carry none). Bits (client writer
        /// FUN_142ca4ce0, trace/0695/agent-leads2-b.md L121) [P]: 0x1 always, 0x2 diving, 0x4 sprint, 0x10 jumping, 0x20
        /// falling, 0x40 not moving, 0x400 landed / on the ground, 0x800 double jump, 0x1000 gliding, 0x2000 strafing,
        /// 0x4000 movement input held, 0x10000 vaulting, 0x40000 / 0x80000 / 0x100000 grapple deploy / fire / grappling,
        /// 0x200000 ledge grab, 0x800000 flung, 0x1000000 fell off, 0x2000000 sitting (<see cref="SittingState"/>),
        /// 0x10000000 standing on a supporting entity (the body then carries 3 × p2 offset from it and a signed 2-bit int,
        /// its id), 0x20000000 the trailing uint carries a value.
        /// </summary>
        public static bool TryReadState(byte[] data, out uint state)
        {
            state = 0;
            if (data.Length <= FieldsOffset || (data[0] != 0x00 && data[0] != 0x01))
                return false;
            int offset = FieldsOffset;
            if (data[0] == 0x00)
                for (int i = 0; i < 20; i++)
                    if (!TryReadSignedInt(data, ref offset, out _))
                        return false;
            int extra = data[offset] & 3;
            if (offset + extra >= data.Length)
                return false;
            uint raw = 0;
            for (int i = 0; i <= extra; i++)
                raw |= (uint)data[offset + i] << (8 * i);
            state = raw >> 2;
            return true;
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
