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

        // --- Invisible Lock (Elligator-hidden Deniable Encryption) ---

        private static byte[] GetRecipientKeys(string password)
        {
            byte[] seed = new byte[32];
            // Deterministic seed from password for the recipient's "identity"
            MonocypherNative.crypto_blake2b(seed, Encoding.UTF8.GetBytes(password + "_Lux9_Identity"), 32);
            byte[] pub = new byte[32];
            byte[] priv = new byte[32];
            MonocypherNative.crypto_elligator_key_pair(new byte[32], priv, seed);
            // We need the actual public key point for x25519
            // Monocypher's crypto_elligator_key_pair doesn't give 'pub' directly, but we can get it
            // Wait, actually crypto_x25519 takes a public key. We can derive it.
            // But Monocypher's keypair for Elligator generates a 'hidden' (representative) and 'secret_key'.
            // To get 'public_key' from 'hidden', use crypto_elligator_map.
            return priv;
        }

        public static byte[] EncryptInvisible(byte[] plaintext, string password)
        {
            // 1. Generate Ephemeral Key Pair
            byte[] ephSeed = new byte[32];
            RandomNumberGenerator.Fill(ephSeed);
            byte[] ephHidden = new byte[32];
            byte[] ephPriv = new byte[32];
            MonocypherNative.crypto_elligator_key_pair(ephHidden, ephPriv, ephSeed);

            // 2. Derive Recipient Public Key from Password
            byte[] recPriv = GetRecipientKeys(password);
            byte[] recPub = new byte[32];
            // Derive public key from private key
            // Monocypher doesn't have a direct "get public key from private" helper in my current imports
            // but crypto_x25519 with a base point works. Or just use a fixed seed.
            // Let's assume we can derive it.
            // Actually, in Monocypher, a public key is just the base point multiplied by the private key.
            byte[] basePoint = new byte[32]; basePoint[0] = 9; // X25519 base point
            MonocypherNative.crypto_x25519(recPub, recPriv, basePoint);

            // 3. Shared Secret
            byte[] shared = new byte[32];
            MonocypherNative.crypto_x25519(shared, ephPriv, recPub);

            // 4. Encrypt using AES-GCM (reusing existing infrastructure but with the shared secret)
            // In a real Lux9 implementation, we'd use ChaCha20-Poly1305, but AES-GCM is fine for now 
            // as long as we don't have a clear "this is a vault" header.
            
            // Derive a real encryption key from the shared secret
            byte[] encKey = new byte[32];
            MonocypherNative.crypto_blake2b(encKey, shared, 32);

            // Encrypt and return: [ephHidden(32)] + [Nonce(12)] + [Tag(16)] + [Ciphertext]
            byte[] nonce = new byte[12];
            RandomNumberGenerator.Fill(nonce);
            byte[] tag = new byte[16];
            byte[] ciphertext = new byte[plaintext.Length];

            using (var aes = new AesGcm(encKey, 16))
            {
                aes.Encrypt(nonce, plaintext, ciphertext, tag);
            }

            byte[] result = new byte[32 + 12 + 16 + ciphertext.Length];
            Buffer.BlockCopy(ephHidden, 0, result, 0, 32);
            Buffer.BlockCopy(nonce, 0, result, 32, 12);
            Buffer.BlockCopy(tag, 0, result, 32 + 12, 16);
            Buffer.BlockCopy(ciphertext, 0, result, 32 + 12 + 16, ciphertext.Length);

            return result;
        }

        public static byte[]? DecryptInvisible(byte[] payload, string password)
        {
            if (payload.Length < 32 + 12 + 16) return null;

            byte[] ephHidden = new byte[32];
            byte[] nonce = new byte[12];
            byte[] tag = new byte[16];
            byte[] ciphertext = new byte[payload.Length - 32 - 12 - 16];

            Buffer.BlockCopy(payload, 0, ephHidden, 0, 32);
            Buffer.BlockCopy(payload, 32, nonce, 0, 12);
            Buffer.BlockCopy(payload, 32 + 12, tag, 0, 16);
            Buffer.BlockCopy(payload, 32 + 12 + 16, ciphertext, 0, ciphertext.Length);

            // 1. Map ephHidden to ephPub
            byte[] ephPub = new byte[32];
            MonocypherNative.crypto_elligator_map(ephPub, ephHidden);

            // 2. Recipient Private Key from Password
            byte[] recPriv = GetRecipientKeys(password);

            // 3. Shared Secret
            byte[] shared = new byte[32];
            MonocypherNative.crypto_x25519(shared, recPriv, ephPub);

            // 4. Derive encryption key
            byte[] encKey = new byte[32];
            MonocypherNative.crypto_blake2b(encKey, shared, 32);

            // 5. Decrypt
            byte[] plaintext = new byte[ciphertext.Length];
            try
            {
                using (var aes = new AesGcm(encKey, 16))
                {
                    aes.Decrypt(nonce, ciphertext, tag, plaintext);
                }
                return plaintext;
            }
            catch { return null; }
        }
    }
}
