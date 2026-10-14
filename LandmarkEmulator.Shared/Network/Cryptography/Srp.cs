using System;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace LandmarkEmulator.Shared.Network.Cryptography
{
    /// <summary>
    /// SRP-6a (RFC 5054 2048-bit group, g = 2, SHA-256) for the account / login service, so a password never crosses the
    /// network: the player's PC derives x = PBKDF2-HMAC-SHA256(password, salt, 10000 iterations, 32 bytes) (the same
    /// derivation <see cref="PasswordProvider"/> always used) and the server keeps only the verifier v = g^x mod N.
    /// <para>
    /// Login: the server sends salt and B = k·v + g^b; the client sends A = g^a and
    /// M1 = H(A | B | S) with S = (B − k·g^x)^(a + u·x), u = H(A | B); the server checks M1 against S = (A·v^u)^b and
    /// answers M2 = H(A | M1 | S) and the session key XOR H(S | "session"). All numbers are big-endian, padded to 256
    /// bytes inside hashes. LaunchPad (SrpClient.cs) and the account website (srp.js) are the client side.
    /// </para>
    /// </summary>
    public static class Srp
    {
        private const string PrimeHex =
            "AC6BDB41324A9A9BF166DE5E1389582FAF72B6651987EE07FC3192943DB56050A37329CBB4A099ED8193E0757767A13DD52312AB4B03310D" +
            "CD7F48A9DA04FD50E8083969EDB767B0CF6095179A163AB3661A05FBD5FAAAE82918A9962F0B93B855F97993EC975EEAA80D740ADBF4FF74" +
            "7359D041D5C33EA71D281E446B14773BCA97B43A23FB801676BD207A436C6481F1D2B9078717461A5B9D32E688F87748544523B524B0D57D" +
            "5EA77A2775D2ECFA032CFBDBF52FB3786160279004E57AE6AF874E7303CE53299CCC041C7BC308D82A5698F3A8D0C38271AE35F8E9DBFBB6" +
            "94B5C803D89F7AE435DE236D525F54759B65E372FCD68EF20FA7111F9E4AFF73";

        public const int Length = 256;
        public static readonly BigInteger N = new(Convert.FromHexString(PrimeHex), isUnsigned: true, isBigEndian: true);
        public static readonly BigInteger G = 2;
        public static readonly BigInteger K = Number(Hash(Pad(N), Pad(G)));

        public static byte[] Pad(BigInteger value)
        {
            byte[] bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
            if (bytes.Length >= Length)
                return bytes;
            byte[] padded = new byte[Length];
            Buffer.BlockCopy(bytes, 0, padded, Length - bytes.Length, bytes.Length);
            return padded;
        }

        public static BigInteger Number(byte[] bigEndian) => new(bigEndian, isUnsigned: true, isBigEndian: true);
        public static BigInteger Number(string hex) => Number(Convert.FromHexString(hex));
        public static string Hex(BigInteger value) => Convert.ToHexString(Pad(value));

        public static byte[] Hash(params byte[][] parts)
        {
            using var sha = SHA256.Create();
            foreach (byte[] part in parts)
                sha.TransformBlock(part, 0, part.Length, null, 0);
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return sha.Hash;
        }

        /// <summary>v = g^x mod N for a stored PBKDF2 value x (hex).</summary>
        public static string VerifierFromKey(string keyHex) => Hex(BigInteger.ModPow(G, Number(keyHex), N));

        /// <summary>Is this a valid verifier (an SRP group element, 512 hex digits)?</summary>
        public static bool IsVerifier(string hex)
        {
            if (hex == null || hex.Length != Length * 2)
                return false;
            try
            {
                BigInteger v = Number(hex);
                return v > 1 && v < N - 1;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        /// <summary>The server's half of a login: b and B = k·v + g^b mod N.</summary>
        public static (BigInteger b, BigInteger B) ServerStart(BigInteger v)
        {
            BigInteger b = Number(RandomNumberGenerator.GetBytes(32));
            BigInteger B = (K * v + BigInteger.ModPow(G, b, N)) % N;
            return (b, B);
        }

        /// <summary>
        /// Checks the client's proof; on success returns S (else null). A must not be 0 mod N, nor u = 0.
        /// </summary>
        public static BigInteger? ServerVerify(BigInteger v, BigInteger b, BigInteger B, BigInteger A, byte[] m1)
        {
            if (A % N == 0)
                return null;
            BigInteger u = Number(Hash(Pad(A), Pad(B)));
            if (u == 0)
                return null;
            BigInteger S = BigInteger.ModPow(A * BigInteger.ModPow(v, u, N) % N, b, N);
            byte[] expected = Hash(Pad(A), Pad(B), Pad(S));
            return CryptographicOperations.FixedTimeEquals(expected, m1) ? S : null;
        }

        /// <summary>M2 = H(A | M1 | S): proves to the client that the server knows v.</summary>
        public static byte[] ServerProof(BigInteger A, byte[] m1, BigInteger S) => Hash(Pad(A), m1, Pad(S));

        /// <summary>The session key encrypted for the client: XOR with H(S | "session") (keys up to 32 bytes).</summary>
        public static string SealSessionKey(string sessionKey, BigInteger S)
        {
            byte[] mask = Hash(Pad(S), Encoding.ASCII.GetBytes("session"));
            byte[] data = Encoding.ASCII.GetBytes(sessionKey);
            if (data.Length > mask.Length)
                throw new ArgumentException("Session key too long.");
            for (int i = 0; i < data.Length; i++)
                data[i] ^= mask[i];
            return Convert.ToHexString(data);
        }
    }
}
