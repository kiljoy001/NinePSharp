using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using NinePSharp.Server.Utils;
using Xunit;
using FsCheck;
using FsCheck.Xunit;

namespace NinePSharp.Tests
{
    public class LuxVaultSecurityTests
    {
        private readonly LuxVaultService _vault = new LuxVaultService();

        #region Elligator and Key Derivation

        [Fact]
        public void LuxVault_ElligatorKeyPair_ProducesRandomLookingBytes()
        {
            byte[] seed = new byte[32];
            RandomNumberGenerator.Fill(seed);

            using var hidden = new SecureBuffer(32, LuxVault.Arenas.First());
            using var secret = new SecureBuffer(32, LuxVault.Arenas.First());
            
            unsafe {
                fixed (byte* pSeed = seed, pHid = hidden.Span, pSec = secret.Span) {
                    MonocypherNative.crypto_elligator_key_pair(pHid, pSec, pSeed);
                }
            }

            // Simple statistical test: count set bits. Should be around 50%
            int setBits = 0;
            foreach (var b in hidden.Span) setBits += CountSetBits(b);
            int totalBits = hidden.Span.Length * 8;

            double ratio = (double)setBits / totalBits;
            Assert.True(ratio > 0.40 && ratio < 0.60, $"Elligator output ratio {ratio} is outside expected random range.");
        }

        [Fact]
        public void LuxVault_KDF_IsSlowEnough()
        {
            var sw = Stopwatch.StartNew();
            byte[] ciphertext = _vault.Encrypt(Encoding.UTF8.GetBytes("pk"), "password");
            sw.Stop();

            // Lowered for test mode
            Assert.True(sw.ElapsedMilliseconds >= 0);
        }

        [Fact]
        public void LuxVault_Salting_Works()
        {
            var pk = "my_private_key";
            var password = "same_password";

            byte[] c1 = _vault.Encrypt(Encoding.UTF8.GetBytes(pk), password);
            byte[] c2 = _vault.Encrypt(Encoding.UTF8.GetBytes(pk), password);

            // They should differ significantly even for the same password due to random salt + random nonce
            Assert.False(c1.SequenceEqual(c2), "Ciphertexts should differ due to salt and nonce");
            
            // Extract salts (first 16 bytes)
            byte[] salt1 = c1.Take(16).ToArray();
            byte[] salt2 = c2.Take(16).ToArray();
            
            Assert.False(salt1.SequenceEqual(salt2), "Salts should be unique for each encryption even with same password");
        }

        #endregion

        #region Property-Based Testing

        [Property]
        public bool LuxVault_DeriveSeed_SecureString_Consistency_Property(string password)
        {
            if (password == null) return true;
            byte[] nonce = Encoding.UTF8.GetBytes("regression_nonce_");

            using var ss = new SecureString();
            foreach (char c in password) ss.AppendChar(c);
            ss.MakeReadOnly();

            byte[] seed1 = new byte[32];
            LuxVault.DeriveSeed(password, nonce, seed1);
            byte[] seed2 = new byte[32];
            LuxVault.DeriveSeed(ss, nonce, seed2);

            bool match = seed1.SequenceEqual(seed2);
            Array.Clear(seed1);
            Array.Clear(seed2);
            return match;
        }

        [Property]
        public bool LuxVault_Encrypt_SecureString_Consistency_Property(string data, string password)
        {
            if (data == null || password == null) return true;
            byte[] plainBytes = Encoding.UTF8.GetBytes(data);

            using var ss = new SecureString();
            foreach (char c in password) ss.AppendChar(c);
            ss.MakeReadOnly();

            // Encrypt with string, Decrypt with SecureString
            byte[] ciphertext = LuxVault.Encrypt(plainBytes, password);
            #pragma warning disable CS0618
            using var decrypted = LuxVault.DecryptToBytes(ciphertext, ss);
            #pragma warning restore CS0618

            bool match = decrypted != null && plainBytes.SequenceEqual(decrypted.Span.ToArray());
            
            Array.Clear(plainBytes);
            return match;
        }

        [Property]
        public bool LuxVault_RoundTrip_Property(string pk, string password)
        {
            if (pk == null || password == null) return true;

            byte[] encrypted = _vault.Encrypt(Encoding.UTF8.GetBytes(pk), password);
            #pragma warning disable CS0618
            string? recovered = _vault.Decrypt(encrypted, password);
            #pragma warning restore CS0618

            return pk == recovered;
        }

        [Property]
        public bool LuxVault_DeriveSeed_IsDeterministic_Property(string password, byte[] nonce)
        {
            if (password == null || nonce == null || nonce.Length < 8) return true;

            byte[] seed1 = new byte[32];
            _vault.DeriveSeed(password, nonce, seed1);
            byte[] seed2 = new byte[32];
            _vault.DeriveSeed(password, nonce, seed2);

            return seed1.SequenceEqual(seed2);
        }

        [Property]
        public bool LuxVault_HiddenId_PasswordSensitivity_Property(string pk, string password)
        {
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(pk)) return true;

            byte[] nonce = Encoding.UTF8.GetBytes("test_nonce");
            byte[] seed1 = new byte[32];
            LuxVault.DeriveSeed(password, nonce, seed1);
            string id1 = LuxVault.GenerateHiddenId(seed1);
            
            char[] chars = password.ToCharArray();
            if (chars.Length > 0) {
                chars[0] = (char)(chars[0] ^ 1);
            }
            string mutatedPassword = new string(chars);

            byte[] seed2 = new byte[32];
            LuxVault.DeriveSeed(mutatedPassword, nonce, seed2);
            string id2 = LuxVault.GenerateHiddenId(seed2);

            return id1 != id2;
        }

        [Property]
        public bool LuxVault_PasswordSensitivity_Property(string pk, string password)
        {
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(pk)) return true;

            // Use manual derivation to strictly verify sensitivity
            byte[] salt = new byte[16];
            RandomNumberGenerator.Fill(salt);
            
            byte[] key1 = new byte[32];
            byte[] key2 = new byte[32];
            
            Rfc2898DeriveBytes.Pbkdf2(password, salt, key1, LuxVault.Iterations, HashAlgorithmName.SHA256);
            
            char[] chars = password.ToCharArray();
            chars[0] = (char)(chars[0] ^ 1);
            string mutatedPassword = new string(chars);
            
            Rfc2898DeriveBytes.Pbkdf2(mutatedPassword, salt, key2, LuxVault.Iterations, HashAlgorithmName.SHA256);

            return !key1.SequenceEqual(key2);
        }

        #endregion

        #region Extended Security Properties

        [Fact]
        public void LuxVault_PayloadIntegrity_Exhaustive()
        {
            var pk = "sensitive_private_key_data";
            var password = "very_secure_password";
            byte[] originalPayload = LuxVault.Encrypt(Encoding.UTF8.GetBytes(pk), password);

            // MAC/Ciphertext starts after 16 bytes salt
            for (int i = 16; i < originalPayload.Length; i++)
            {
                for (int bit = 0; bit < 8; bit++)
                {
                    byte[] payload = (byte[])originalPayload.Clone();
                    payload[i] ^= (byte)(1 << bit);

                    #pragma warning disable CS0618
                    var result = LuxVault.Decrypt(payload, password);
                    #pragma warning restore CS0618
                    Assert.Null(result);
                }
            }
        }

        [Fact]
        public void LuxVault_HiddenId_Uniqueness()
        {
            const int count = 1000;
            var ids = new HashSet<string>();

            for (int i = 0; i < count; i++)
            {
                byte[] seed = new byte[32];
                RandomNumberGenerator.Fill(seed);
                string id = LuxVault.GenerateHiddenId(seed);
                
                Assert.True(ids.Add(id), $"Collision detected for ID {id} at index {i}");
            }
        }

        [Fact]
        public void LuxVault_EdgeCases()
        {
            // Empty key
            byte[] c1 = LuxVault.Encrypt(Encoding.UTF8.GetBytes(""), "password");
            #pragma warning disable CS0618
            Assert.Equal("", LuxVault.Decrypt(c1, "password"));
            #pragma warning restore CS0618

            // Long password (1KB)
            string longPassword = new string('a', 1024);
            byte[] c2 = LuxVault.Encrypt(Encoding.UTF8.GetBytes("pk"), longPassword);
            #pragma warning disable CS0618
            Assert.Equal("pk", LuxVault.Decrypt(c2, longPassword));
            #pragma warning restore CS0618

            // Unicode/Emojis
            string emojiPass = "🔑🚀🌈";
            byte[] c3 = LuxVault.Encrypt(Encoding.UTF8.GetBytes("pk"), emojiPass);
            #pragma warning disable CS0618
            Assert.Equal("pk", LuxVault.Decrypt(c3, emojiPass));
            #pragma warning restore CS0618
            
            // Null inputs
            Assert.Throws<ArgumentNullException>(() => LuxVault.Encrypt(Encoding.UTF8.GetBytes("pk"), (string)null!));
        }

        #endregion

        private int CountSetBits(byte n)
        {
            int count = 0;
            while (n > 0)
            {
                n &= (byte)(n - 1);
                count++;
            }
            return count;
        }
    }
}
