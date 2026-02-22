using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;

[assembly: InternalsVisibleTo("NinePSharp.Tests")]

namespace NinePSharp.Server.Utils
{
    public static class LuxVault
    {
        public const string VaultDirectory = "vaults";
        private const int KeySize = 32;

        private static readonly SecureMemoryArena Arena = new(1024 * 1024); // 1MB Secure Arena

        /// <summary>
        /// A scope-bound buffer allocated from the RAM-locked SecureMemoryArena.
        /// </summary>
        private ref struct SecureBuffer
        {
            public Span<byte> Span { get; }
            public int Length => Span.Length;

            public SecureBuffer(int size)
            {
                Span = Arena.Allocate(size);
            }

            public void Dispose()
            {
                Arena.Free(Span);
            }

            public static implicit operator Span<byte>(SecureBuffer b) => b.Span;
            public static implicit operator ReadOnlySpan<byte>(SecureBuffer b) => b.Span;
        }

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
        
        // High iterations for production security (can be lowered by tests for speed)
        internal static int Iterations = 600000;

        // Session-bound key. If not set, operations will still work but won't be ephemeral.
        private static byte[]? _sessionKey;

        public static void InitializeSessionKey(ReadOnlySpan<byte> sessionKey)
        {
            if (_sessionKey != null) return;
            _sessionKey = GC.AllocateArray<byte>(sessionKey.Length, pinned: true);
            sessionKey.CopyTo(_sessionKey);
            
            unsafe {
                fixed (byte* pKey = _sessionKey) {
                    MemoryLock.Lock((IntPtr)pKey, (nuint)_sessionKey.Length);
                }
            }
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
            nuint unmanagedLen = (nuint)(secureString.Length * 2); // Unicode
            MemoryLock.Lock(ptr, unmanagedLen);
            
            try
            {
                unsafe {
                    int length = secureString.Length;
                    char* chars = (char*)ptr.ToPointer();
                    int byteCount = Encoding.UTF8.GetByteCount(chars, length);
                    if (byteCount == 0) return action(Array.Empty<byte>());
                    
                    using (var secureBuffer = new SecureBuffer(byteCount))
                    {
                        fixed (byte* pBytes = secureBuffer.Span)
                        {
                            Encoding.UTF8.GetBytes(chars, length, pBytes, byteCount);
                        }
                        
                        // We still need to return a byte[] for legacy compatibility with action(byte[])
                        // but we ensure it is pinned and copied from the secure arena.
                        byte[] legacyBytes = GC.AllocateArray<byte>(byteCount, pinned: true);
                        try {
                            secureBuffer.Span.CopyTo(legacyBytes);
                            return action(legacyBytes); 
                        }
                        finally { 
                            Array.Clear(legacyBytes); 
                        }
                    }
                }
            }
            finally
            {
                MemoryLock.Unlock(ptr, unmanagedLen);
                Marshal.ZeroFreeGlobalAllocUnicode(ptr);
            }
        }

        private static byte[] MixSessionKey(ReadOnlySpan<byte> input)
        {
            if (_sessionKey == null) return input.ToArray();
            
            // HMAC or Blake2b mix: input + sessionKey
            byte[] mixed = new byte[32];
            // Use SecureBuffer from the contiguous Arena
            using (var buffer = new SecureBuffer(input.Length + _sessionKey.Length))
            {
                input.CopyTo(buffer.Span);
                _sessionKey.CopyTo(buffer.Span.Slice(input.Length));
                
                unsafe {
                    fixed (byte* pMixed = mixed, pBuffer = buffer.Span) {
                        MonocypherNative.crypto_blake2b_ptr(pMixed, (nuint)mixed.Length, pBuffer, (nuint)buffer.Length);
                    }
                }
            }
            return mixed;
        }

        public static byte[] DeriveSeed(string password, byte[] nonce)
        {
            if (password == null) throw new ArgumentNullException(nameof(password));
            using (var rawSeed = new SecureBuffer(KeySize))
            {
                Rfc2898DeriveBytes.Pbkdf2(password, nonce, rawSeed.Span, Iterations, HashAlgorithmName.SHA256);
                return MixSessionKey(rawSeed.Span);
            }
        }

        public static byte[] DeriveSeed(SecureString password, byte[] nonce)
        {
            return WithSecureString(password, bytes => {
                using (var rawSeed = new SecureBuffer(KeySize))
                {
                    Rfc2898DeriveBytes.Pbkdf2(bytes, nonce, rawSeed.Span, Iterations, HashAlgorithmName.SHA256);
                    return MixSessionKey(rawSeed.Span);
                }
            });
        }

        private static void DeriveKeyFromPassword(string password, ReadOnlySpan<byte> salt, byte[] outKey)
        {
            using (var key = new SecureBuffer(KeySize))
            {
                Rfc2898DeriveBytes.Pbkdf2(password, salt, key.Span, Iterations, HashAlgorithmName.SHA256);
                
                var mixed = MixSessionKey(key.Span);
                mixed.CopyTo(outKey, 0);
                Array.Clear(mixed);
            }
        }

        private static void DeriveKeyFromSecureString(SecureString password, ReadOnlySpan<byte> salt, byte[] outKey)
        {
            byte[] saltArray = salt.ToArray();
            WithSecureString(password, bytes => {
                using (var key = new SecureBuffer(KeySize))
                {
                    Rfc2898DeriveBytes.Pbkdf2(bytes, saltArray, key.Span, Iterations, HashAlgorithmName.SHA256);
                    
                    var mixed = MixSessionKey(key.Span);
                    mixed.CopyTo(outKey, 0);
                    Array.Clear(mixed);
                    return true;
                }
            });
        }

        private static void DeriveKeyFromBytes(ReadOnlySpan<byte> keyMaterial, ReadOnlySpan<byte> salt, byte[] outKey)
        {
            using (var mixed = new SecureBuffer(keyMaterial.Length + salt.Length))
            {
                keyMaterial.CopyTo(mixed.Span);
                salt.CopyTo(mixed.Span.Slice(keyMaterial.Length));
                
                using (var rawKey = new SecureBuffer(32))
                {
                    unsafe {
                        fixed (byte* pRaw = rawKey.Span, pMixed = mixed.Span) {
                            MonocypherNative.crypto_blake2b_ptr(pRaw, (nuint)rawKey.Length, pMixed, (nuint)mixed.Length);
                        }
                    }
                    
                    var finalKey = MixSessionKey(rawKey.Span);
                    finalKey.CopyTo(outKey, 0);
                    
                    Array.Clear(finalKey);
                }
            }
        }

        public static byte[] Encrypt(byte[] plaintextBytes, string password)
        {
            if (plaintextBytes == null) throw new ArgumentNullException(nameof(plaintextBytes));
            if (password == null) throw new ArgumentNullException(nameof(password));
            
            byte[] salt = new byte[SaltSize];
            RandomNumberGenerator.Fill(salt);
            using (var key = new SecureBuffer(KeySize))
            {
                DeriveKeyFromPassword(password, salt, key.Span.ToArray());
                return EncryptInternal(plaintextBytes, key.Span, salt);
            }
        }

        public static byte[] Encrypt(byte[] plaintextBytes, SecureString password)
        {
            if (plaintextBytes == null) throw new ArgumentNullException(nameof(plaintextBytes));
            
            byte[] salt = new byte[SaltSize];
            RandomNumberGenerator.Fill(salt);
            using (var key = new SecureBuffer(KeySize))
            {
                DeriveKeyFromSecureString(password, salt, key.Span.ToArray());
                return EncryptInternal(plaintextBytes, key.Span, salt);
            }
        }

        public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> keyMaterial)
        {
            byte[] salt = new byte[SaltSize];
            RandomNumberGenerator.Fill(salt);
            using (var key = new SecureBuffer(KeySize))
            {
                DeriveKeyFromBytes(keyMaterial, salt, key.Span.ToArray());
                return EncryptInternal(plaintext, key.Span, salt);
            }
        }

        private static byte[] EncryptInternal(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, byte[] salt)
        {
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] mac = new byte[MacSize];
            byte[] nonce = new byte[NonceSize];
            RandomNumberGenerator.Fill(nonce);
            
            unsafe {
                fixed (byte* pCipher = ciphertext, pMac = mac, pKey = key, pNonce = nonce, pPlain = plaintext) {
                    MonocypherNative.crypto_aead_lock(pCipher, pMac, pKey, pNonce, null, 0, pPlain, (nuint)plaintext.Length);
                }
            }

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
            using (var key = new SecureBuffer(KeySize))
            {
                DeriveKeyFromPassword(password, payload.AsSpan(0, SaltSize), key.Span.ToArray());
                return DecryptInternal(payload, key.Span);
            }
        }

        public static byte[]? DecryptToBytes(byte[] payload, SecureString password)
        {
            if (payload == null || payload.Length < SaltSize + NonceSize + MacSize) return null;
            using (var key = new SecureBuffer(KeySize))
            {
                DeriveKeyFromSecureString(password, payload.AsSpan(0, SaltSize), key.Span.ToArray());
                return DecryptInternal(payload, key.Span);
            }
        }

        public static byte[]? DecryptToBytes(byte[] payload, ReadOnlySpan<byte> keyMaterial)
        {
            if (payload == null || payload.Length < SaltSize + NonceSize + MacSize) return null;
            using (var key = new SecureBuffer(KeySize))
            {
                DeriveKeyFromBytes(keyMaterial, payload.AsSpan(0, SaltSize), key.Span.ToArray());
                return DecryptInternal(payload, key.Span);
            }
        }

        private static byte[]? DecryptInternal(byte[] payload, ReadOnlySpan<byte> key)
        {
            try
            {
                byte[] nonce = new byte[NonceSize];
                payload.AsSpan(SaltSize, NonceSize).CopyTo(nonce);
                
                byte[] mac = new byte[MacSize];
                payload.AsSpan(SaltSize + NonceSize, MacSize).CopyTo(mac);
                
                byte[] ciphertext = new byte[payload.Length - (SaltSize + NonceSize + MacSize)];
                payload.AsSpan(SaltSize + NonceSize + MacSize).CopyTo(ciphertext);

                using (var plaintextBuffer = new SecureBuffer(ciphertext.Length))
                {
                    int result;
                    unsafe {
                        fixed (byte* pPlain = plaintextBuffer.Span, pMac = mac, pKey = key, pNonce = nonce, pCipher = ciphertext) {
                            result = MonocypherNative.crypto_aead_unlock(pPlain, pMac, pKey, pNonce, null, 0, pCipher, (nuint)ciphertext.Length);
                        }
                    }
                    
                    Array.Clear(nonce);
                    Array.Clear(mac);
                    Array.Clear(ciphertext);

                    if (result == 0)
                    {
                                            // Return a pinned/locked array cloned from the arena
                                            byte[] resultBytes = GC.AllocateArray<byte>(plaintextBuffer.Length, pinned: true);
                                            plaintextBuffer.Span.CopyTo(resultBytes);
                                            
                                            unsafe {
                                                fixed (byte* pResult = resultBytes) {
                                                    MemoryLock.Lock((IntPtr)pResult, (nuint)resultBytes.Length);
                                                }
                                            }
                                            return resultBytes;                    }
                }
                return null;
            }
            catch { return null; }
        }

        [Obsolete("Use DecryptToBytes to avoid leaking secrets into the managed string pool.")]
        public static string? Decrypt(byte[] payload, string password)
        {
            var bytes = DecryptToBytes(payload, password);
            if (bytes == null) return null;
            try { return Encoding.UTF8.GetString(bytes); }
            finally { Array.Clear(bytes); }
        }

        [Obsolete("Use DecryptToBytes to avoid leaking secrets into the managed string pool.")]
        public static string? Decrypt(byte[] payload, SecureString password)
        {
            var bytes = DecryptToBytes(payload, password);
            if (bytes == null) return null;
            try { return Encoding.UTF8.GetString(bytes); }
            finally { Array.Clear(bytes); }
        }

        [Obsolete("Use DecryptToBytes to avoid leaking secrets into the managed string pool.")]
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
