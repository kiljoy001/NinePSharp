using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;

[assembly: InternalsVisibleTo("NinePSharp.Tests")]

namespace NinePSharp.Server.Utils
{
    /// <summary>
    /// Provides zero-exposure secret management using Monocypher (XChaCha20-Poly1305) 
    /// and RAM-locked pinned memory.
    /// </summary>
    public static class LuxVault
    {
        /// <summary>
        /// The directory where encrypted vault files are stored.
        /// </summary>
        public static readonly string VaultDirectory = Path.Combine(AppContext.BaseDirectory, "vaults");
        private const int KeySize = 32;

        internal static readonly SecureMemoryArena Arena = new(1024 * 1024); // 1MB Secure Arena
        private static readonly object _directoryLock = new object();

        public static string GetVaultPath(string filename)
        {
            lock (_directoryLock)
            {
                if (!Directory.Exists(VaultDirectory)) Directory.CreateDirectory(VaultDirectory);
                return Path.Combine(VaultDirectory, filename);
            }
        }

        public static void CleanupVaults()
        {
            if (Directory.Exists(VaultDirectory))
            {
                try { Directory.Delete(VaultDirectory, true); } catch { /* Ignore */ }
            }
            try { Directory.CreateDirectory(VaultDirectory); } catch { /* Ignore */ }
        }

        private const int NonceSize = 24; 
        private const int MacSize = 16;
        private const int SaltSize = 16;
        
        // High iterations for production security (can be lowered by tests for speed)
        internal static int Iterations = 600000;

        // Session-bound key. If not set, operations will still work but won't be ephemeral.
        private static byte[]? _sessionKey;
        private static readonly object _sessionKeyLock = new object();

        public static void InitializeSessionKey(ReadOnlySpan<byte> sessionKey)
        {
            lock (_sessionKeyLock)
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
        }

        public static string GenerateHiddenId(ReadOnlySpan<byte> seed)
        {
            if (seed.Length != 32) throw new ArgumentException("Seed must be exactly 32 bytes.");
            
            using var hidden = new SecureBuffer(32, Arena);
            using var secretKey = new SecureBuffer(32, Arena);
            
            unsafe {
                fixed (byte* pSeed = seed, pHidden = hidden.Span, pSecret = secretKey.Span) {
                    MonocypherNative.crypto_elligator_key_pair(pHidden, pSecret, pSeed);
                }
            }
            
            return Convert.ToHexString(hidden.Span).ToLowerInvariant();
        }

        private delegate T ReadOnlySpanAction<T>(ReadOnlySpan<byte> span);

        private static T WithSecureString<T>(SecureString secureString, ReadOnlySpanAction<T> action)
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
                    if (byteCount == 0) return action(ReadOnlySpan<byte>.Empty);
                    
                    using (var secureBuffer = new SecureBuffer(byteCount, Arena))
                    {
                        fixed (byte* pBytes = secureBuffer.Span)
                        {
                            Encoding.UTF8.GetBytes(chars, length, pBytes, byteCount);
                        }
                        
                        return action(secureBuffer.Span);
                    }
                }
            }
            finally
            {
                MemoryLock.Unlock(ptr, unmanagedLen);
                Marshal.ZeroFreeGlobalAllocUnicode(ptr);
            }
        }

        private static void MixSessionKey(ReadOnlySpan<byte> input, Span<byte> output)
        {
            lock (_sessionKeyLock)
            {
                if (_sessionKey == null)
                {
                    input.CopyTo(output);
                    return;
                }

                // HMAC or Blake2b mix: input + sessionKey
                using (var buffer = new SecureBuffer(input.Length + _sessionKey.Length, Arena))
                {
                    input.CopyTo(buffer.Span);
                    _sessionKey.CopyTo(buffer.Span.Slice(input.Length));

                    unsafe {
                        fixed (byte* pMixed = output, pBuffer = buffer.Span) {
                            MonocypherNative.crypto_blake2b_ptr(pMixed, (nuint)output.Length, pBuffer, (nuint)buffer.Length);
                        }
                    }
                }
            }
        }

        public static void DeriveSeed(string password, ReadOnlySpan<byte> nonce, Span<byte> output)
        {
            if (password == null) throw new ArgumentNullException(nameof(password));
            using (var rawSeed = new SecureBuffer(KeySize, Arena))
            {
                Rfc2898DeriveBytes.Pbkdf2(password, nonce, rawSeed.Span, Iterations, HashAlgorithmName.SHA256);
                MixSessionKey(rawSeed.Span, output);
            }
        }

        public static unsafe void DeriveSeed(SecureString password, ReadOnlySpan<byte> nonce, Span<byte> output)
        {
            fixed (byte* pOutput = output, pNonce = nonce)
            {
                byte* pOutputLocal = pOutput;
                byte* pNonceLocal = pNonce;
                int nonceLen = nonce.Length;
                WithSecureString(password, bytes => {
                    using (var rawSeed = new SecureBuffer(KeySize, Arena))
                    {
                        Rfc2898DeriveBytes.Pbkdf2(bytes, new ReadOnlySpan<byte>(pNonceLocal, nonceLen), rawSeed.Span, Iterations, HashAlgorithmName.SHA256);
                        MixSessionKey(rawSeed.Span, new Span<byte>(pOutputLocal, KeySize));
                        return true;
                    }
                });
            }
        }

        public static void DeriveSeed(ReadOnlySpan<byte> passwordBytes, ReadOnlySpan<byte> nonce, Span<byte> output)
        {
            using (var rawSeed = new SecureBuffer(KeySize, Arena))
            {
                Rfc2898DeriveBytes.Pbkdf2(passwordBytes, nonce, rawSeed.Span, Iterations, HashAlgorithmName.SHA256);
                MixSessionKey(rawSeed.Span, output);
            }
        }


        public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, string password)
        {
            if (password == null) throw new ArgumentNullException(nameof(password));
            
            Span<byte> salt = stackalloc byte[SaltSize];
            RandomNumberGenerator.Fill(salt);
            using (var key = new SecureBuffer(KeySize, Arena))
            {
                DeriveSeed(password, salt, key.Span);
                return EncryptInternal(plaintext, key.Span, salt);
            }
        }

        public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, SecureString password)
        {
            if (password == null) throw new ArgumentNullException(nameof(password));
            
            Span<byte> salt = stackalloc byte[SaltSize];
            RandomNumberGenerator.Fill(salt);
            using (var key = new SecureBuffer(KeySize, Arena))
            {
                DeriveSeed(password, salt, key.Span);
                return EncryptInternal(plaintext, key.Span, salt);
            }
        }

        public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> keyMaterial)
        {
            Span<byte> salt = stackalloc byte[SaltSize];
            RandomNumberGenerator.Fill(salt);
            using (var key = new SecureBuffer(KeySize, Arena))
            {
                DeriveSeed(keyMaterial, salt, key.Span);
                return EncryptInternal(plaintext, key.Span, salt);
            }
        }

        private static byte[] EncryptInternal(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> salt)
        {
            byte[] finalPayload = new byte[SaltSize + NonceSize + MacSize + plaintext.Length];
            salt.CopyTo(finalPayload.AsSpan(0, SaltSize));
            
            using var nonce = new SecureBuffer(NonceSize, Arena);
            RandomNumberGenerator.Fill(nonce.Span);
            nonce.Span.CopyTo(finalPayload.AsSpan(SaltSize, NonceSize));
            
            using var mac = new SecureBuffer(MacSize, Arena);
            
            unsafe {
                fixed (byte* pPayload = finalPayload, 
                             pMac = mac.Span, 
                             pKey = key, 
                             pNonce = nonce.Span, 
                             pPlain = plaintext) 
                {
                    byte* pCipher = pPayload + SaltSize + NonceSize + MacSize;
                    MonocypherNative.crypto_aead_lock(pCipher, pMac, pKey, pNonce, null, 0, pPlain, (nuint)plaintext.Length);
                }
            }

            mac.Span.CopyTo(finalPayload.AsSpan(SaltSize + NonceSize, MacSize));
            return finalPayload;
        }

        public static SecureSecret? DecryptToBytes(byte[] payload, string password)
        {
            if (payload == null || payload.Length < SaltSize + NonceSize + MacSize) return null;
            using (var key = new SecureBuffer(KeySize, Arena))
            {
                DeriveSeed(password, payload.AsSpan(0, SaltSize), key.Span);
                return DecryptInternal(payload, key.Span);
            }
        }

        public static SecureSecret? DecryptToBytes(byte[] payload, SecureString password)
        {
            if (payload == null || payload.Length < SaltSize + NonceSize + MacSize) return null;
            using (var key = new SecureBuffer(KeySize, Arena))
            {
                DeriveSeed(password, payload.AsSpan(0, SaltSize), key.Span);
                return DecryptInternal(payload, key.Span);
            }
        }

        public static SecureSecret? DecryptToBytes(byte[] payload, ReadOnlySpan<byte> keyMaterial)
        {
            if (payload == null || payload.Length < SaltSize + NonceSize + MacSize) return null;
            using (var key = new SecureBuffer(KeySize, Arena))
            {
                DeriveSeed(keyMaterial, payload.AsSpan(0, SaltSize), key.Span);
                return DecryptInternal(payload, key.Span);
            }
        }

        public static SecureSecret? DecryptToBytesWithPasswordBytes(byte[] payload, ReadOnlySpan<byte> passwordBytes)
        {
            if (payload == null || payload.Length < SaltSize + NonceSize + MacSize) return null;
            using (var key = new SecureBuffer(KeySize, Arena))
            {
                DeriveSeed(passwordBytes, payload.AsSpan(0, SaltSize), key.Span);
                return DecryptInternal(payload, key.Span);
            }
        }

        /// <summary>
        /// Encrypts and stores a secret to the physical vault directory.
        /// </summary>
        /// <param name="name">The unique name/alias for the secret.</param>
        /// <param name="plaintext">The cleartext payload to encrypt.</param>
        /// <param name="password">The password used to derive the encryption key.</param>
        public static void StoreSecret(string name, byte[] plaintext, SecureString password)
        {
            var encrypted = Encrypt(plaintext, password);
            using var seed = new SecureBuffer(KeySize, Arena);
            DeriveSeed(password, Encoding.UTF8.GetBytes(name), seed.Span);
            var hiddenId = GenerateHiddenId(seed.Span);
            var path = GetVaultPath($"secret_{hiddenId}.vlt");
            File.WriteAllBytes(path, encrypted);
        }

        /// <summary>
        /// Decrypts and loads a secret from the physical vault.
        /// </summary>
        /// <param name="name">The name/alias of the secret to load.</param>
        /// <param name="password">The password used to decrypt the secret.</param>
        /// <returns>A <see cref="SecureSecret"/> containing the decrypted data, or null if loading fails.</returns>
        public static SecureSecret? LoadSecret(string name, SecureString password)
        {
            using var seed = new SecureBuffer(KeySize, Arena);
            DeriveSeed(password, Encoding.UTF8.GetBytes(name), seed.Span);
            try
            {
                var hiddenId = GenerateHiddenId(seed.Span);
                var path = GetVaultPath($"secret_{hiddenId}.vlt");
                if (!File.Exists(path)) return null;

                var encrypted = File.ReadAllBytes(path);
                return DecryptToBytes(encrypted, password);
            }
            finally
            {
            }
        }

        /// <summary>
        /// Decrypts and loads a secret from the physical vault using a raw password span.
        /// </summary>
        /// <param name="name">The name/alias of the secret to load.</param>
        /// <param name="passwordBytes">The raw password bytes.</param>
        /// <returns>A <see cref="SecureSecret"/> containing the decrypted data, or null if loading fails.</returns>
        public static SecureSecret? LoadSecret(string name, ReadOnlySpan<byte> passwordBytes)
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(name);
            using var seed = new SecureBuffer(KeySize, Arena);
            DeriveSeed(passwordBytes, nameBytes, seed.Span);
            try
            {
                var hiddenId = GenerateHiddenId(seed.Span);
                var path = GetVaultPath($"secret_{hiddenId}.vlt");
                if (!File.Exists(path)) return null;

                var encrypted = File.ReadAllBytes(path);
                return DecryptToBytesWithPasswordBytes(encrypted, passwordBytes);
            }
            finally
            {
                Array.Clear(nameBytes);
            }
        }

        private static SecureSecret? DecryptInternal(byte[] payload, ReadOnlySpan<byte> key)
        {
            try
            {
                using var nonce = new SecureBuffer(NonceSize, Arena);
                payload.AsSpan(SaltSize, NonceSize).CopyTo(nonce.Span);
                
                using var mac = new SecureBuffer(MacSize, Arena);
                payload.AsSpan(SaltSize + NonceSize, MacSize).CopyTo(mac.Span);
                
                int ciphertextLen = payload.Length - (SaltSize + NonceSize + MacSize);
                using (var plaintextBuffer = new SecureBuffer(ciphertextLen, Arena))
                {
                    int result;
                    unsafe {
                        fixed (byte* pPayload = payload, pKey = key) {
                            byte* pCiphertext = pPayload + SaltSize + NonceSize + MacSize;
                            fixed (byte* pPlain = plaintextBuffer.Span, pMac = mac.Span, pNonce = nonce.Span) {
                                result = MonocypherNative.crypto_aead_unlock(pPlain, pMac, pKey, pNonce, null, 0, pCiphertext, (nuint)ciphertextLen);
                            }
                        }
                    }
                    
                    if (result == 0)
                    {
                        // Allocate pinned + locked result, wrapped in SecureSecret for guaranteed cleanup
                        byte[] resultBytes = GC.AllocateArray<byte>(plaintextBuffer.Length, pinned: true);
                        plaintextBuffer.Span.CopyTo(resultBytes);
                        
                        unsafe {
                            fixed (byte* pResult = resultBytes) {
                                MemoryLock.Lock((IntPtr)pResult, (nuint)resultBytes.Length);
                            }
                        }
                        return new SecureSecret(resultBytes);
                    }
                }
                return null;
            }
            catch (Exception ex) { throw new Exception("DECRYPT TO BYTES FAILED", ex); }
        }

        [Obsolete("Use DecryptToBytes to avoid leaking secrets into the managed string pool.")]
        public static string? Decrypt(byte[] payload, string password)
        {
            using var secret = DecryptToBytes(payload, password);
            if (secret == null) return null;
            return Encoding.UTF8.GetString(secret.Span);
        }

        [Obsolete("Use DecryptToBytes to avoid leaking secrets into the managed string pool.")]
        public static string? Decrypt(byte[] payload, SecureString password)
        {
            using var secret = DecryptToBytes(payload, password);
            if (secret == null) return null;
            return Encoding.UTF8.GetString(secret.Span);
        }

        [Obsolete("Use DecryptToBytes to avoid leaking secrets into the managed string pool.")]
        public static string? Decrypt(byte[] payload, ReadOnlySpan<byte> keyMaterial)
        {
            using var secret = DecryptToBytes(payload, keyMaterial);
            if (secret == null) return null;
            return Encoding.UTF8.GetString(secret.Span);
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
