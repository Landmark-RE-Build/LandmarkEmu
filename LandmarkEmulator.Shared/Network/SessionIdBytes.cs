using System;

namespace LandmarkEmulator.Shared.Network
{
    public static class SessionIdBytes
    {
        public static byte[] ToBytes(Guid sessionId) => sessionId.ToByteArray();

        public static Guid FromBytes(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length != 16)
                throw new ArgumentException("session_id must be 16 bytes (GUID).", nameof(bytes));

            return new Guid(bytes);
        }
    }
}
