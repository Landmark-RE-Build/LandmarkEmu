using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace LandmarkEmulator.LaunchPad
{
    /// <summary>
    /// SRP-6a, the player's side (the server side is LandmarkEmulator.Shared/Network/Cryptography/Srp.cs; the same as
    /// the account website's srp.js): RFC 5054 2048-bit group, g = 2, SHA-256,
    /// x = PBKDF2-HMAC-SHA256(password, salt, 10000, 32). The password never leaves this PC.
    /// </summary>
    public static class SrpClient
    {
        private const int Length = 256;
        private static readonly BigInteger N = Number(Convert.FromHexString(
            "AC6BDB41324A9A9BF166DE5E1389582FAF72B6651987EE07FC3192943DB56050A37329CBB4A099ED8193E0757767A13DD52312AB4B03310D" +
            "CD7F48A9DA04FD50E8083969EDB767B0CF6095179A163AB3661A05FBD5FAAAE82918A9962F0B93B855F97993EC975EEAA80D740ADBF4FF74" +
            "7359D041D5C33EA71D281E446B14773BCA97B43A23FB801676BD207A436C6481F1D2B9078717461A5B9D32E688F87748544523B524B0D57D" +
            "5EA77A2775D2ECFA032CFBDBF52FB3786160279004E57AE6AF874E7303CE53299CCC041C7BC308D82A5698F3A8D0C38271AE35F8E9DBFBB6" +
            "94B5C803D89F7AE435DE236D525F54759B65E372FCD68EF20FA7111F9E4AFF73"));
        private static readonly BigInteger G = 2;
        private static readonly BigInteger K = Number(Hash(Pad(N), Pad(G)));

        private static BigInteger Number(byte[] bigEndian) => new(bigEndian, isUnsigned: true, isBigEndian: true);

        private static byte[] Pad(BigInteger value)
        {
            byte[] bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
            if (bytes.Length >= Length)
                return bytes;
            byte[] padded = new byte[Length];
            Buffer.BlockCopy(bytes, 0, padded, Length - bytes.Length, bytes.Length);
            return padded;
        }

        private static byte[] Hash(params byte[][] parts)
        {
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (byte[] part in parts)
                sha.AppendData(part);
            return sha.GetHashAndReset();
        }

        private static BigInteger Mod(BigInteger value) => ((value % N) + N) % N;

        /// <summary>x for a password and a salt (hex), as hex: what "remember me" keeps instead of the password.</summary>
        public static string Key(string password, string saltHex)
            => Convert.ToHexString(Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), Convert.FromHexString(saltHex), 10000,
                HashAlgorithmName.SHA256, 32));

        /// <summary>A new account's salt and verifier (what /srp/register takes).</summary>
        public static (string Salt, string Verifier, string Key) NewAccount(string password)
        {
            string salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            string key = Key(password, salt);
            BigInteger v = BigInteger.ModPow(G, Number(Convert.FromHexString(key)), N);
            return (salt, Convert.ToHexString(Pad(v)), key);
        }

        /// <summary>The client's half of a login: A and the proof M1 for the server's salt and B, and what checks M2.</summary>
        public sealed class Login
        {
            public string A = "", M1 = "";
            internal byte[] PadA = Array.Empty<byte>(), M1Bytes = Array.Empty<byte>(), PadS = Array.Empty<byte>();

            /// <summary>Does the server's M2 prove it knows the verifier (it is the real server)?</summary>
            public bool ServerProofOk(string m2) => Convert.ToHexString(Hash(PadA, M1Bytes, PadS)).Equals(m2, StringComparison.OrdinalIgnoreCase);

            /// <summary>The session key: the server's sealed key XOR H(S | "session").</summary>
            public string OpenSessionKey(string sealedHex)
            {
                byte[] mask = Hash(PadS, Encoding.ASCII.GetBytes("session"));
                byte[] sealedKey = Convert.FromHexString(sealedHex);
                for (int i = 0; i < sealedKey.Length; i++)
                    sealedKey[i] ^= mask[i];
                return Encoding.ASCII.GetString(sealedKey);
            }
        }

        public static Login Start(string keyHex, string serverBHex)
        {
            BigInteger x = Number(Convert.FromHexString(keyHex));
            BigInteger publicB = Number(Convert.FromHexString(serverBHex));
            BigInteger secretA = Number(RandomNumberGenerator.GetBytes(32));
            BigInteger publicA = BigInteger.ModPow(G, secretA, N);
            byte[] padA = Pad(publicA), padB = Pad(publicB);
            BigInteger u = Number(Hash(padA, padB));
            BigInteger gx = BigInteger.ModPow(G, x, N);
            BigInteger baseValue = Mod(publicB - Mod(K * gx));
            BigInteger s = BigInteger.ModPow(baseValue, secretA + u * x, N);
            byte[] padS = Pad(s);
            byte[] m1 = Hash(padA, padB, padS);
            return new Login { A = Convert.ToHexString(padA), M1 = Convert.ToHexString(m1), PadA = padA, M1Bytes = m1, PadS = padS };
        }
    }
}
