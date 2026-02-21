using System;
using System.Security.Cryptography;
using System.Text;

namespace NinePSharp.Server.Utils
{
    public static class LuxVault
    {
        // 32-byte AesGcm key and 12-byte nonce
        private const int KeySize = 32;
        private const int NonceSize = 12;

        private const int SaltSize = 16;
        private const int Iterations = 600000;

        // Use Monocypher's Elligator to derive a hidden ID from a 32-byte seed
        public static string GenerateHiddenId(byte[] seed)
        {
            if (seed.Length != 32) throw new ArgumentException("Seed must be exactly 32 bytes.");

            byte[] seedCopy = new byte[32];
            Array.Copy(seed, seedCopy, 32);

            byte[] hidden = new byte[32];
            byte[] secret_key = new byte[32];

            MonocypherNative.crypto_elligator_key_pair(hidden, secret_key, seedCopy);

            return Convert.ToHexString(hidden).ToLowerInvariant();
        }

        // Derives a deterministic 32-byte seed from a password and a nonce
        // This allows creating "Secret Pointers" for vaults.
        public static byte[] DeriveSeed(string password, byte[] nonce)
        {
            return Rfc2898DeriveBytes.Pbkdf2(
                password,
                nonce,
                Iterations,
                HashAlgorithmName.SHA256,
                KeySize);
        }

        private static byte[] DeriveKeyFromPassword(string password, byte[] salt)
        {
            return Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                KeySize);
        }

        public static byte[] Encrypt(byte[] plaintextBytes, string password)
        {
            byte[] salt = new byte[SaltSize];
            RandomNumberGenerator.Fill(salt);

            byte[] key = DeriveKeyFromPassword(password, salt);
            
            byte[] ciphertext = new byte[plaintextBytes.Length];
            byte[] tag = new byte[16]; // AesGcm Auth Tag

            // Prepend a random nonce to the payload
            byte[] nonce = new byte[NonceSize];
            RandomNumberGenerator.Fill(nonce);
            
            using (var aes = new AesGcm(key, tag.Length))
            {
                aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);
            }

            // Output payload: [Salt (16)] + [Nonce (12)] + [Tag (16)] + [Ciphertext]
            byte[] finalPayload = new byte[SaltSize + NonceSize + tag.Length + ciphertext.Length];
            Buffer.BlockCopy(salt, 0, finalPayload, 0, SaltSize);
            Buffer.BlockCopy(nonce, 0, finalPayload, SaltSize, NonceSize);
            Buffer.BlockCopy(tag, 0, finalPayload, SaltSize + NonceSize, tag.Length);
            Buffer.BlockCopy(ciphertext, 0, finalPayload, SaltSize + NonceSize + tag.Length, ciphertext.Length);

            return finalPayload;
        }

        public static byte[] Encrypt(string text, string password) => Encrypt(Encoding.UTF8.GetBytes(text), password);

        public static byte[]? DecryptToBytes(byte[] payload, string password)
        {
            if (payload.Length < SaltSize + NonceSize + 16) return null;

            try
            {
                byte[] salt = new byte[SaltSize];
                byte[] nonce = new byte[NonceSize];
                byte[] tag = new byte[16];
                byte[] ciphertext = new byte[payload.Length - SaltSize - NonceSize - tag.Length];

                Buffer.BlockCopy(payload, 0, salt, 0, SaltSize);
                Buffer.BlockCopy(payload, SaltSize, nonce, 0, NonceSize);
                Buffer.BlockCopy(payload, SaltSize + NonceSize, tag, 0, tag.Length);
                Buffer.BlockCopy(payload, SaltSize + NonceSize + tag.Length, ciphertext, 0, ciphertext.Length);

                byte[] key = DeriveKeyFromPassword(password, salt);
                byte[] plaintextBytes = new byte[ciphertext.Length];

                using (var aes = new AesGcm(key, tag.Length))
                {
                    aes.Decrypt(nonce, ciphertext, tag, plaintextBytes);
                }

                return plaintextBytes;
            }
            catch { return null; }
        }

        public static string? Decrypt(byte[] payload, string password)
        {
            var bytes = DecryptToBytes(payload, password);
            return bytes != null ? Encoding.UTF8.GetString(bytes) : null;
        }

        // Configuration protection (uses Master Secret)
        public static string ProtectConfig(string plainText, string masterKey)
        {
            var ciphertext = Encrypt(plainText, masterKey);
            return "secret://" + Convert.ToBase64String(ciphertext);
        }

        public static string? UnprotectConfig(string secretUri, string masterKey)
        {
            if (!secretUri.StartsWith("secret://")) return secretUri;
            var base64 = secretUri.Substring("secret://".Length);
            var ciphertext = Convert.FromBase64String(base64);
            return Decrypt(ciphertext, masterKey);
        }
    }
}
