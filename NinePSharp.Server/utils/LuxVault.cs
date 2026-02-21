using System;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace NinePSharp.Server.Utils
{
    public static class LuxVault
    {
        public const string VaultDirectory = "vaults";
        private const int KeySize = 32;

        public static string GetVaultPath(string filename)
        {
            if (!Directory.Exists(VaultDirectory)) Directory.CreateDirectory(VaultDirectory);
            return Path.Combine(VaultDirectory, filename);
        }

        public static void CleanupVaults()
        {
            if (Directory.Exists(VaultDirectory))
            {
                try { Directory.Delete(VaultDirectory, true); } catch { /* Ignore */ }
            }
        }

        private const int NonceSize = 24; 
        private const int MacSize = 16;
        private const int SaltSize = 16;
        private const int Iterations = 600000;

        // Session-bound key. If not set, operations will still work but won't be ephemeral.
        private static byte[]? _sessionKey;

        public static void InitializeSessionKey(ReadOnlySpan<byte> sessionKey)
        {
            if (_sessionKey != null) return;
            _sessionKey = GC.AllocateArray<byte>(sessionKey.Length, pinned: true);
            sessionKey.CopyTo(_sessionKey);
        }

        public static string GenerateHiddenId(byte[] seed)
        {
            if (seed.Length != 32) throw new ArgumentException("Seed must be exactly 32 bytes.");
            byte[] seedCopy = (byte[])seed.Clone();
            byte[] hidden = new byte[32];
            byte[] secretKey = new byte[32];
            try {
                MonocypherNative.crypto_elligator_key_pair(hidden, secretKey, seedCopy);
                return Convert.ToHexString(hidden).ToLowerInvariant();
            }
            finally {
                Array.Clear(seedCopy);
                Array.Clear(secretKey);
            }
        }

        private static T WithSecureString<T>(SecureString secureString, Func<byte[], T> action)
        {
            IntPtr ptr = Marshal.SecureStringToGlobalAllocUnicode(secureString);
            try
            {
                unsafe {
                    int length = secureString.Length;
                    char* chars = (char*)ptr.ToPointer();
                    int byteCount = Encoding.UTF8.GetByteCount(chars, length);
                    byte[] bytes = GC.AllocateArray<byte>(byteCount, pinned: true);
                    fixed (byte* pBytes = bytes)
                    {
                        Encoding.UTF8.GetBytes(chars, length, pBytes, byteCount);
                    }
                    try { return action(bytes); }
                    finally { Array.Clear(bytes); }
                }
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocUnicode(ptr);
            }
        }

        private static byte[] MixSessionKey(byte[] input)
        {
            if (_sessionKey == null) return input;
            
            // HMAC or Blake2b mix: input + sessionKey
            byte[] mixed = new byte[32];
            byte[] buffer = new byte[input.Length + _sessionKey.Length];
            Buffer.BlockCopy(input, 0, buffer, 0, input.Length);
            Buffer.BlockCopy(_sessionKey, 0, buffer, input.Length, _sessionKey.Length);
            
            MonocypherNative.crypto_blake2b(mixed, (nuint)mixed.Length, buffer, (nuint)buffer.Length);
            Array.Clear(buffer);
            return mixed;
        }

        public static byte[] DeriveSeed(string password, byte[] nonce)
        {
            if (password == null) throw new ArgumentNullException(nameof(password));
            var rawSeed = Rfc2898DeriveBytes.Pbkdf2(password, nonce, Iterations, HashAlgorithmName.SHA256, KeySize);
            // Mix with session key to ensure ephemeral behavior
            var mixed = MixSessionKey(rawSeed);
            Array.Clear(rawSeed);
            return mixed;
        }

        public static byte[] DeriveSeed(SecureString password, byte[] nonce)
        {
            return WithSecureString(password, bytes => {
                var rawSeed = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetString(bytes), nonce, Iterations, HashAlgorithmName.SHA256, KeySize);
                var mixed = MixSessionKey(rawSeed);
                Array.Clear(rawSeed);
                return mixed;
            });
        }

        private static void DeriveKeyFromPassword(string password, ReadOnlySpan<byte> salt, byte[] outKey)
        {
            var key = Rfc2898DeriveBytes.Pbkdf2(password, salt.ToArray(), Iterations, HashAlgorithmName.SHA256, KeySize);
            // Mix with session key
            var mixed = MixSessionKey(key);
            mixed.CopyTo(outKey, 0);
            Array.Clear(key);
            Array.Clear(mixed);
        }

        private static void DeriveKeyFromSecureString(SecureString password, ReadOnlySpan<byte> salt, byte[] outKey)
        {
            byte[] saltArray = salt.ToArray();
            WithSecureString(password, bytes => {
                DeriveKeyFromPassword(Encoding.UTF8.GetString(bytes), saltArray, outKey);
                return true;
            });
        }

        private static void DeriveKeyFromBytes(ReadOnlySpan<byte> keyMaterial, ReadOnlySpan<byte> salt, byte[] outKey)
        {
            byte[] mixed = new byte[keyMaterial.Length + salt.Length];
            keyMaterial.CopyTo(mixed);
            salt.CopyTo(mixed.AsSpan(keyMaterial.Length));
            
            byte[] rawKey = new byte[32];
            MonocypherNative.crypto_blake2b(rawKey, (nuint)rawKey.Length, mixed, (nuint)mixed.Length);
            
            var finalKey = MixSessionKey(rawKey);
            finalKey.CopyTo(outKey, 0);
            
            Array.Clear(mixed);
            Array.Clear(rawKey);
            Array.Clear(finalKey);
        }

        public static byte[] Encrypt(byte[] plaintextBytes, string password)
        {
            if (plaintextBytes == null) throw new ArgumentNullException(nameof(plaintextBytes));
            if (password == null) throw new ArgumentNullException(nameof(password));
            
            byte[] salt = new byte[SaltSize];
            RandomNumberGenerator.Fill(salt);
            byte[] key = GC.AllocateArray<byte>(KeySize, pinned: true);
            try {
                DeriveKeyFromPassword(password, salt, key);
                return EncryptInternal(plaintextBytes, key, salt);
            }
            finally {
                Array.Clear(key);
            }
        }

        public static byte[] Encrypt(byte[] plaintextBytes, SecureString password)
        {
            if (plaintextBytes == null) throw new ArgumentNullException(nameof(plaintextBytes));
            
            byte[] salt = new byte[SaltSize];
            RandomNumberGenerator.Fill(salt);
            byte[] key = GC.AllocateArray<byte>(KeySize, pinned: true);
            try {
                DeriveKeyFromSecureString(password, salt, key);
                return EncryptInternal(plaintextBytes, key, salt);
            }
            finally {
                Array.Clear(key);
            }
        }

        public static byte[] Encrypt(byte[] plaintextBytes, ReadOnlySpan<byte> keyMaterial)
        {
            if (plaintextBytes == null) throw new ArgumentNullException(nameof(plaintextBytes));
            
            byte[] salt = new byte[SaltSize];
            RandomNumberGenerator.Fill(salt);
            byte[] key = GC.AllocateArray<byte>(KeySize, pinned: true);
            try {
                DeriveKeyFromBytes(keyMaterial, salt, key);
                return EncryptInternal(plaintextBytes, key, salt);
            }
            finally {
                Array.Clear(key);
            }
        }

        private static byte[] EncryptInternal(byte[] plaintextBytes, byte[] key, byte[] salt)
        {
            byte[] ciphertext = new byte[plaintextBytes.Length];
            byte[] mac = new byte[MacSize];
            byte[] nonce = new byte[NonceSize];
            RandomNumberGenerator.Fill(nonce);
            
            MonocypherNative.crypto_aead_lock(ciphertext, mac, key, nonce, null, 0, plaintextBytes, (nuint)plaintextBytes.Length);

            byte[] finalPayload = new byte[SaltSize + NonceSize + MacSize + ciphertext.Length];
            salt.CopyTo(finalPayload.AsSpan(0, SaltSize));
            nonce.CopyTo(finalPayload.AsSpan(SaltSize, NonceSize));
            mac.CopyTo(finalPayload.AsSpan(SaltSize + NonceSize, MacSize));
            Buffer.BlockCopy(ciphertext, 0, finalPayload, SaltSize + NonceSize + MacSize, ciphertext.Length);

            Array.Clear(mac);
            Array.Clear(nonce);
            return finalPayload;
        }

        public static byte[]? DecryptToBytes(byte[] payload, string password)
        {
            if (payload == null || payload.Length < SaltSize + NonceSize + MacSize) return null;
            byte[] key = GC.AllocateArray<byte>(KeySize, pinned: true);
            try {
                DeriveKeyFromPassword(password, payload.AsSpan(0, SaltSize), key);
                return DecryptInternal(payload, key);
            }
            finally {
                Array.Clear(key);
            }
        }

        public static byte[]? DecryptToBytes(byte[] payload, SecureString password)
        {
            if (payload == null || payload.Length < SaltSize + NonceSize + MacSize) return null;
            byte[] key = GC.AllocateArray<byte>(KeySize, pinned: true);
            try {
                DeriveKeyFromSecureString(password, payload.AsSpan(0, SaltSize), key);
                return DecryptInternal(payload, key);
            }
            finally {
                Array.Clear(key);
            }
        }

        public static byte[]? DecryptToBytes(byte[] payload, ReadOnlySpan<byte> keyMaterial)
        {
            if (payload == null || payload.Length < SaltSize + NonceSize + MacSize) return null;
            byte[] key = GC.AllocateArray<byte>(KeySize, pinned: true);
            try {
                DeriveKeyFromBytes(keyMaterial, payload.AsSpan(0, SaltSize), key);
                return DecryptInternal(payload, key);
            }
            finally {
                Array.Clear(key);
            }
        }

        private static byte[]? DecryptInternal(byte[] payload, byte[] key)
        {
            try
            {
                byte[] nonce = new byte[NonceSize];
                payload.AsSpan(SaltSize, NonceSize).CopyTo(nonce);
                
                byte[] mac = new byte[MacSize];
                payload.AsSpan(SaltSize + NonceSize, MacSize).CopyTo(mac);
                
                byte[] ciphertext = new byte[payload.Length - (SaltSize + NonceSize + MacSize)];
                payload.AsSpan(SaltSize + NonceSize + MacSize).CopyTo(ciphertext);

                byte[] plaintextBytes = GC.AllocateArray<byte>(ciphertext.Length, pinned: true);
                int result = MonocypherNative.crypto_aead_unlock(plaintextBytes, mac, key, nonce, null, 0, ciphertext, (nuint)ciphertext.Length);
                
                Array.Clear(nonce);
                Array.Clear(mac);
                Array.Clear(ciphertext);

                if (result == 0) return plaintextBytes;
                
                Array.Clear(plaintextBytes);
                return null;
            }
            catch { return null; }
        }

        public static string? Decrypt(byte[] payload, string password)
        {
            var bytes = DecryptToBytes(payload, password);
            if (bytes == null) return null;
            try { return Encoding.UTF8.GetString(bytes); }
            finally { Array.Clear(bytes); }
        }

        public static string? Decrypt(byte[] payload, SecureString password)
        {
            var bytes = DecryptToBytes(payload, password);
            if (bytes == null) return null;
            try { return Encoding.UTF8.GetString(bytes); }
            finally { Array.Clear(bytes); }
        }

        public static string? Decrypt(byte[] payload, ReadOnlySpan<byte> keyMaterial)
        {
            var bytes = DecryptToBytes(payload, keyMaterial);
            if (bytes == null) return null;
            try { return Encoding.UTF8.GetString(bytes); }
            finally { Array.Clear(bytes); }
        }

        public static string ProtectConfig(string plainText, ReadOnlySpan<byte> masterKey)
        {
            var ciphertext = Encrypt(Encoding.UTF8.GetBytes(plainText), masterKey);
            return "secret://" + Convert.ToBase64String(ciphertext);
        }

        public static string? UnprotectConfig(string secretUri, ReadOnlySpan<byte> masterKey)
        {
            if (!secretUri.StartsWith("secret://")) return secretUri;
            var base64 = secretUri.Substring("secret://".Length);
            var ciphertext = Convert.FromBase64String(base64);
            return Decrypt(ciphertext, masterKey);
        }
    }
}
