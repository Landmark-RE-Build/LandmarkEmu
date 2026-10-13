using LandmarkEmulator.Shared.Game;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using System;
using System.Security.Cryptography;

namespace LandmarkEmulator.Shared.Network.Cryptography
{
    /// <summary>
    /// Account passwords. The stored verifier is an SRP-6a verifier g^x mod N (<see cref="Srp"/>, 512 hex digits) with
    /// x = PBKDF2-HMAC-SHA256(password, salt, 10000, 32 bytes). Accounts made before SRP stored x itself (64 hex digits,
    /// "legacy"); those still work and the web API upgrades them to a verifier the first time they are used.
    /// </summary>
    public static class PasswordProvider
    {
        /// <summary>x = PBKDF2-HMAC-SHA256(password, salt) as hex (the SRP private value).</summary>
        public static string KeyHex(string password, byte[] salt)
            => KeyDerivation.Pbkdf2(password, salt, KeyDerivationPrf.HMACSHA256, 10000, 256 / 8).ToHexString();

        /// <summary>
        /// Returns a random salt and the SRP verifier for a plaintext password (accounts made on the server itself:
        /// the AuthServer "account create" command, the local web API).
        /// </summary>
        public static (string salt, string verifier) GenerateSaltAndVerifier(string password, byte[] s = null)
        {
            s ??= RandomProvider.GetBytes(16u);
            return (s.ToHexString(), Srp.VerifierFromKey(KeyHex(password, s)));
        }

        /// <summary>Does the stored verifier hold an old-style PBKDF2 value instead of an SRP verifier?</summary>
        public static bool IsLegacy(string v) => v != null && v.Length == 64;

        /// <summary>The SRP verifier for a stored value (converted when it is a legacy PBKDF2 value).</summary>
        public static string SrpVerifier(string v) => IsLegacy(v) ? Srp.VerifierFromKey(v) : v;

        /// <summary>Verifies a plaintext password against a salt and stored verifier (either format).</summary>
        public static bool VerifyPassword(string s, string v, string password)
        {
            byte[] salt;
            try
            {
                salt = Convert.FromHexString(s ?? "");
            }
            catch (FormatException)
            {
                return false;
            }
            if (salt.Length == 0 || string.IsNullOrEmpty(v))
                return false;
            string key = KeyHex(password, salt);
            string expected = IsLegacy(v) ? key : Srp.VerifierFromKey(key);
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(v));
        }
    }
}
