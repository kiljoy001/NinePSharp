using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("NinePSharp.Tests")]

namespace NinePSharp.Server.Utils
{
    /// <summary>
    /// Provides zero-exposure secret management using Monocypher (XChaCha20-Poly1305) 
    /// and sharded RAM-locked pinned memory for high-performance parallel execution.
    /// </summary>
    public static class LuxVault
    {
        public static readonly string VaultDirectory = Path.Combine(AppContext.BaseDirectory, "vaults");
        private const int KeySize = 32;
        private const int NonceSize = 24; 
        private const int MacSize = 16;
        private const int SaltSize = 16;

        // ─── Parallel Optimization & Sharded Arenas ─────────────────────────
        
        private static readonly int MaxParallelOps = (int)Math.Max(1, Environment.ProcessorCount * 0.8);
        private static readonly SemaphoreSlim ConcurrencyGovernor = new(MaxParallelOps, MaxParallelOps);
        private static int _concurrencyGovernorEnabled = 1;

        internal static bool IsConcurrencyGovernorEnabled => Volatile.Read(ref _concurrencyGovernorEnabled) == 1;
        internal static void SetConcurrencyGovernorEnabled(bool enabled) => Volatile.Write(ref _concurrencyGovernorEnabled, enabled ? 1 : 0);

        private static readonly ICpuMonitor? CpuMonitor;
        private static readonly Timer? CpuMonitorTimer;
        private static int _currentLimit = MaxParallelOps;
        private static DateTime _lastAdjustment = DateTime.MinValue;

        static LuxVault()
        {
            // Select platform-appropriate monitor
            if (OperatingSystem.IsWindows())
            {
                CpuMonitor = new WindowsCpuMonitor();
            }
            else if (OperatingSystem.IsLinux())
            {
                CpuMonitor = new LinuxCpuMonitor();
            }

            if (CpuMonitor != null)
            {
                // Start adaptive CPU monitoring - checks every 2 seconds
                CpuMonitorTimer = new Timer(_ => AdjustConcurrencyBasedOnCpu(), null,
                    TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
            }
        }

        private static void AdjustConcurrencyBasedOnCpu()
        {
            if (CpuMonitor == null) return;

            // Rate limit adjustments to once per 5 seconds
            if ((DateTime.UtcNow - _lastAdjustment).TotalSeconds < 5) return;

            try
            {
                float currentCpu = CpuMonitor.GetUsage();
                int targetLimit = _currentLimit;

                // If system CPU > 80%, reduce our concurrency
                if (currentCpu > 80f)
                {
                    targetLimit = Math.Max(1, _currentLimit - 1);
                }
                // If system CPU < 60%, we can increase (up to MaxParallelOps)
                else if (currentCpu < 60f && _currentLimit < MaxParallelOps)
                {
                    targetLimit = Math.Min(MaxParallelOps, _currentLimit + 1);
                }

                if (targetLimit != _currentLimit)
                {
                    // Adjust semaphore by releasing or waiting
                    int delta = targetLimit - _currentLimit;
                    if (delta > 0)
                    {
                        ConcurrencyGovernor.Release(delta);
                    }
                    else if (delta < 0)
                    {
                        for (int i = 0; i < Math.Abs(delta); i++)
                        {
                            ConcurrencyGovernor.Wait(0); 
                        }
                    }

                    _currentLimit = targetLimit;
                    _lastAdjustment = DateTime.UtcNow;
                }
            }
            catch { /* Monitoring should never crash the vault */ }
        }

        private interface ICpuMonitor { float GetUsage(); }

        [SupportedOSPlatform("windows")]
        private class WindowsCpuMonitor : ICpuMonitor
        {
            private readonly PerformanceCounter? _counter;
            public WindowsCpuMonitor()
            {
                try {
                    _counter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    _counter.NextValue();
                } catch { _counter = null; }
            }
            public float GetUsage() => _counter?.NextValue() ?? 0f;
        }

        private class LinuxCpuMonitor : ICpuMonitor
        {
            public float GetUsage()
            {
                try {
                    // Use loadavg as a proxy for CPU usage on Linux
                    // Format: "0.10 0.05 0.01 1/123 12345"
                    string loadStr = File.ReadAllText("/proc/loadavg").Split(' ')[0];
                    float load = float.Parse(loadStr);
                    // Convert load to a percentage based on core count
                    return (load / Environment.ProcessorCount) * 100f;
                } catch { return 0f; }
            }
        }

        public static readonly SecureMemoryArena[] Arenas = Enumerable.Range(0, MaxParallelOps)
            .Select(_ => new SecureMemoryArena(1024 * 1024)) // 1MB per shard
            .ToArray();

        /// <summary>
        /// Selects an arena shard based on the current thread ID to ensure lock-free scaling.
        /// </summary>
        internal static SecureMemoryArena GetLocalArena()
        {
            int index = Math.Abs(Thread.CurrentThread.ManagedThreadId) % Arenas.Length;
            return Arenas[index];
        }

        private static ConcurrencyGovernorGuard AcquireConcurrencyGovernor()
        {
            var governor = Volatile.Read(ref _concurrencyGovernorEnabled) == 1 ? ConcurrencyGovernor : null;
            return new ConcurrencyGovernorGuard(governor);
        }

        private readonly struct ConcurrencyGovernorGuard : IDisposable
        {
            private readonly SemaphoreSlim? _governor;

            public ConcurrencyGovernorGuard(SemaphoreSlim? governor)
            {
                _governor = governor;
                _governor?.Wait();
            }

            public void Dispose()
            {
                _governor?.Release();
            }
        }

        // ────────────────────────────────────────────────────────────────────

        internal static int Iterations = 600000;
        private static byte[]? _sessionKey;
        private static readonly object SessionKeyInitLock = new();

        public static void InitializeSessionKey(ReadOnlySpan<byte> sessionKey)
        {
            if (Volatile.Read(ref _sessionKey) != null) return;

            lock (SessionKeyInitLock)
            {
                if (_sessionKey != null) return;

                var pinnedKey = GC.AllocateArray<byte>(sessionKey.Length, pinned: true);
                sessionKey.CopyTo(pinnedKey);
                unsafe {
                    fixed (byte* pKey = pinnedKey) {
                        MemoryLock.Lock((IntPtr)pKey, (nuint)pinnedKey.Length);
                    }
                }
                Volatile.Write(ref _sessionKey, pinnedKey);
            }
        }

        public static string GetVaultPath(string filename)
        {
            Directory.CreateDirectory(VaultDirectory);
            return Path.Combine(VaultDirectory, filename);
        }

        public static void CleanupVaults()
        {
            if (Directory.Exists(VaultDirectory))
            {
                try { Directory.Delete(VaultDirectory, true); } catch { /* Ignore */ }
            }
            try { Directory.CreateDirectory(VaultDirectory); } catch { /* Ignore */ }
        }

        public static string GenerateHiddenId(ReadOnlySpan<byte> seed)
        {
            if (seed.Length != 32) throw new ArgumentException("Seed must be exactly 32 bytes.");

            using var gate = AcquireConcurrencyGovernor();

            var arena = GetLocalArena();
            using var hidden = new SecureBuffer(32, arena);
            using var secretKey = new SecureBuffer(32, arena);
            Span<byte> seedCopy = stackalloc byte[32];
            seed.CopyTo(seedCopy);

            unsafe
            {
                fixed (byte* pSeed = seedCopy, pHidden = hidden.Span, pSecret = secretKey.Span)
                {
                    MonocypherNative.crypto_elligator_key_pair(pHidden, pSecret, pSeed);
                }
            }

            return Convert.ToHexString(hidden.Span).ToLowerInvariant();
        }

        private delegate T ReadOnlySpanAction<T>(ReadOnlySpan<byte> span);

        private static T WithSecureString<T>(SecureString secureString, ReadOnlySpanAction<T> action)
        {
            IntPtr ptr = Marshal.SecureStringToGlobalAllocUnicode(secureString);
            nuint unmanagedLen = (nuint)(secureString.Length * 2);
            MemoryLock.Lock(ptr, unmanagedLen);
            
            try
            {
                unsafe {
                    int length = secureString.Length;
                    char* chars = (char*)ptr.ToPointer();
                    int byteCount = Encoding.UTF8.GetByteCount(chars, length);
                    if (byteCount == 0) return action(ReadOnlySpan<byte>.Empty);
                    
                    using (var secureBuffer = new SecureBuffer(byteCount, GetLocalArena()))
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
            var sessionKey = Volatile.Read(ref _sessionKey);
            if (sessionKey == null)
            {
                input.CopyTo(output);
                return;
            }
            
            using (var buffer = new SecureBuffer(input.Length + sessionKey.Length, GetLocalArena()))
            {
                input.CopyTo(buffer.Span);
                sessionKey.CopyTo(buffer.Span.Slice(input.Length));
                unsafe {
                    fixed (byte* pMixed = output, pBuffer = buffer.Span) {
                        MonocypherNative.crypto_blake2b_ptr(pMixed, (nuint)output.Length, pBuffer, (nuint)buffer.Length);
                    }
                }
            }
        }

        public static void DeriveSeed(string password, ReadOnlySpan<byte> nonce, Span<byte> output)
        {
            if (password == null) throw new ArgumentNullException(nameof(password));
            if (nonce.Length < 8) throw new ArgumentException("Nonce must be at least 8 bytes.");

            using (var rawSeed = new SecureBuffer(KeySize, GetLocalArena()))
            {
                Rfc2898DeriveBytes.Pbkdf2(password, nonce, rawSeed.Span, Iterations, HashAlgorithmName.SHA256);
                MixSessionKey(rawSeed.Span, output);
            }
        }

        public static unsafe void DeriveSeed(SecureString password, ReadOnlySpan<byte> nonce, Span<byte> output)
        {
            if (nonce.Length < 8) throw new ArgumentException("Nonce must be at least 8 bytes.");
            
            byte[] localNonce = nonce.ToArray();
            fixed (byte* pOutput = output)
            {
                byte* pOutputLocal = pOutput;
                WithSecureString(password, bytes => {
                    using (var rawSeed = new SecureBuffer(KeySize, GetLocalArena()))
                    {
                        Rfc2898DeriveBytes.Pbkdf2(bytes, localNonce, rawSeed.Span, Iterations, HashAlgorithmName.SHA256);
                        MixSessionKey(rawSeed.Span, new Span<byte>(pOutputLocal, KeySize));
                        return true;
                    }
                });
            }
        }

        public static void DeriveSeed(ReadOnlySpan<byte> passwordBytes, ReadOnlySpan<byte> nonce, Span<byte> output)
        {
            if (nonce.Length < 8) throw new ArgumentException("Nonce must be at least 8 bytes.");

            using (var rawSeed = new SecureBuffer(KeySize, GetLocalArena()))
            {
                Rfc2898DeriveBytes.Pbkdf2(passwordBytes, nonce, rawSeed.Span, Iterations, HashAlgorithmName.SHA256);
                MixSessionKey(rawSeed.Span, output);
            }
        }

        public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, string password)
        {
            if (password == null) throw new ArgumentNullException(nameof(password));
            using var gate = AcquireConcurrencyGovernor();

            Span<byte> salt = stackalloc byte[SaltSize];
            RandomNumberGenerator.Fill(salt);
            using (var key = new SecureBuffer(KeySize, GetLocalArena()))
            {
                DeriveSeed(password, salt, key.Span);
                return EncryptInternal(plaintext, key.Span, salt);
            }
        }

        public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, SecureString password)
        {
            if (password == null) throw new ArgumentNullException(nameof(password));
            using var gate = AcquireConcurrencyGovernor();

            Span<byte> salt = stackalloc byte[SaltSize];
            RandomNumberGenerator.Fill(salt);
            using (var key = new SecureBuffer(KeySize, GetLocalArena()))
            {
                DeriveSeed(password, salt, key.Span);
                return EncryptInternal(plaintext, key.Span, salt);
            }
        }

        public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> keyMaterial)
        {
            using var gate = AcquireConcurrencyGovernor();

            Span<byte> salt = stackalloc byte[SaltSize];
            RandomNumberGenerator.Fill(salt);
            using (var key = new SecureBuffer(KeySize, GetLocalArena()))
            {
                DeriveSeed(keyMaterial, salt, key.Span);
                return EncryptInternal(plaintext, key.Span, salt);
            }
        }

        private static byte[] EncryptInternal(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> salt)
        {
            byte[] finalPayload = new byte[SaltSize + NonceSize + MacSize + plaintext.Length];
            salt.CopyTo(finalPayload.AsSpan(0, SaltSize));
            
            using var nonce = new SecureBuffer(NonceSize, GetLocalArena());
            RandomNumberGenerator.Fill(nonce.Span);
            nonce.Span.CopyTo(finalPayload.AsSpan(SaltSize, NonceSize));
            
            using var mac = new SecureBuffer(MacSize, GetLocalArena());
            
            unsafe {
                fixed (byte* pPayload = finalPayload, pMac = mac.Span, pKey = key, pNonce = nonce.Span, pPlain = plaintext) 
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
            using var gate = AcquireConcurrencyGovernor();

            using (var key = new SecureBuffer(KeySize, GetLocalArena()))
            {
                DeriveSeed(password, payload.AsSpan(0, SaltSize), key.Span);
                return DecryptInternal(payload, key.Span);
            }
        }

        public static SecureSecret? DecryptToBytes(byte[] payload, SecureString password)
        {
            if (payload == null || payload.Length < SaltSize + NonceSize + MacSize) return null;
            using var gate = AcquireConcurrencyGovernor();

            using (var key = new SecureBuffer(KeySize, GetLocalArena()))
            {
                DeriveSeed(password, payload.AsSpan(0, SaltSize), key.Span);
                return DecryptInternal(payload, key.Span);
            }
        }

        public static SecureSecret? DecryptToBytes(byte[] payload, ReadOnlySpan<byte> keyMaterial)
        {
            if (payload == null || payload.Length < SaltSize + NonceSize + MacSize) return null;
            using var gate = AcquireConcurrencyGovernor();

            using (var key = new SecureBuffer(KeySize, GetLocalArena()))
            {
                DeriveSeed(keyMaterial, payload.AsSpan(0, SaltSize), key.Span);
                return DecryptInternal(payload, key.Span);
            }
        }

        public static SecureSecret? DecryptToBytesWithPasswordBytes(byte[] payload, ReadOnlySpan<byte> passwordBytes)
        {
            if (payload == null || payload.Length < SaltSize + NonceSize + MacSize) return null;
            using var gate = AcquireConcurrencyGovernor();

            using (var key = new SecureBuffer(KeySize, GetLocalArena()))
            {
                DeriveSeed(passwordBytes, payload.AsSpan(0, SaltSize), key.Span);
                return DecryptInternal(payload, key.Span);
            }
        }

        public static void StoreSecret(string name, byte[] plaintext, SecureString password)
        {
            var encrypted = Encrypt(plaintext, password);
            using var seed = new SecureBuffer(KeySize, GetLocalArena());
            DeriveSeed(password, Encoding.UTF8.GetBytes(name), seed.Span);
            var hiddenId = GenerateHiddenId(seed.Span);
            var path = GetVaultPath($"secret_{hiddenId}.vlt");
            File.WriteAllBytes(path, encrypted);
        }

        public static SecureSecret? LoadSecret(string name, SecureString password)
        {
            using var seed = new SecureBuffer(KeySize, GetLocalArena());
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

        public static SecureSecret? LoadSecret(string name, ReadOnlySpan<byte> passwordBytes)
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(name);
            using var seed = new SecureBuffer(KeySize, GetLocalArena());
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
                var arena = GetLocalArena();
                using var nonce = new SecureBuffer(NonceSize, arena);
                payload.AsSpan(SaltSize, NonceSize).CopyTo(nonce.Span);
                
                using var mac = new SecureBuffer(MacSize, arena);
                payload.AsSpan(SaltSize + NonceSize, MacSize).CopyTo(mac.Span);
                
                int ciphertextLen = payload.Length - (SaltSize + NonceSize + MacSize);
                using (var plaintextBuffer = new SecureBuffer(ciphertextLen, arena))
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
            return secret == null ? null : Encoding.UTF8.GetString(secret.Span);
        }

        [Obsolete("Use DecryptToBytes to avoid leaking secrets into the managed string pool.")]
        public static string? Decrypt(byte[] payload, SecureString password)
        {
            using var secret = DecryptToBytes(payload, password);
            return secret == null ? null : Encoding.UTF8.GetString(secret.Span);
        }

        [Obsolete("Use DecryptToBytes to avoid leaking secrets into the managed string pool.")]
        public static string? Decrypt(byte[] payload, ReadOnlySpan<byte> keyMaterial)
        {
            using var secret = DecryptToBytes(payload, keyMaterial);
            return secret == null ? null : Encoding.UTF8.GetString(secret.Span);
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
            using var secret = DecryptToBytes(ciphertext, masterKey);
            return secret == null ? null : Encoding.UTF8.GetString(secret.Span);
        }
    }
}
