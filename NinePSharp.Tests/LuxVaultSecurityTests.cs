using NinePSharp.Server.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests
{
    public class LuxVaultSecurityTests
    {
        private readonly ILuxVaultService _vault = new LuxVaultService();

        #region Baseline Security Tests

        [Fact]
        public void LuxVault_ElligatorOutput_LooksRandom()
        {
            const int sampleSize = 100;
            int totalBits = sampleSize * 32 * 8;
            int setBits = 0;

            for (int i = 0; i < sampleSize; i++)
            {
                byte[] seed = new byte[32];
                RandomNumberGenerator.Fill(seed);
                string hex = _vault.GenerateHiddenId(seed);
                byte[] hidden = Convert.FromHexString(hex);

                foreach (byte b in hidden)
                {
                    setBits += CountSetBits(b);
                }
            }

            double ratio = (double)setBits / totalBits;
            Assert.True(ratio > 0.45 && ratio < 0.55, $"Elligator output ratio {ratio} is outside expected random range.");
        }

        [Fact]
        public void LuxVault_KDF_IsSlowEnough()
        {
            var sw = Stopwatch.StartNew();
            byte[] ciphertext = _vault.Encrypt("pk", "password");
            sw.Stop();

            // After upgrade, PBKDF2 with 100k iterations should take > 50ms
            Assert.True(sw.ElapsedMilliseconds > 50, $"KDF is too fast: {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public void LuxVault_Salting_Works()
        {
            var pk = "my_private_key";
            var password = "same_password";

            byte[] c1 = _vault.Encrypt(pk, password);
            byte[] c2 = _vault.Encrypt(pk, password);

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
        public bool LuxVault_RoundTrip_Property(string pk, string password)
        {
            if (pk == null || password == null) return true;

            byte[] encrypted = _vault.Encrypt(pk, password);
            string recovered = _vault.Decrypt(encrypted, password);

            return pk == recovered;
        }

        [Property]
        public bool LuxVault_DeriveSeed_IsDeterministic_Property(string password, byte[] nonce)
        {
            if (password == null || nonce == null) return true;

            byte[] seed1 = _vault.DeriveSeed(password, nonce);
            byte[] seed2 = _vault.DeriveSeed(password, nonce);

            return seed1.SequenceEqual(seed2);
        }

        [Property]
        public bool LuxVault_HiddenId_PasswordSensitivity_Property(string pk, string password)
        {
            if (string.IsNullOrEmpty(password) || pk == null) return true;

            byte[] nonce = Encoding.UTF8.GetBytes("test_nonce");
            byte[] seed1 = LuxVault.DeriveSeed(password, nonce);
            string id1 = LuxVault.GenerateHiddenId(seed1);
            
            char[] chars = password.ToCharArray();
            chars[0] = (char)(chars[0] ^ 1);
            string mutatedPassword = new string(chars);

            byte[] seed2 = LuxVault.DeriveSeed(mutatedPassword, nonce);
            string id2 = LuxVault.GenerateHiddenId(seed2);

            return id1 != id2;
        }

        [Property]
        public bool LuxVault_PasswordSensitivity_Property(string pk, string password)
        {
            if (string.IsNullOrEmpty(password) || pk == null) return true;

            byte[] encrypted = LuxVault.Encrypt(pk, password);
            
            char[] chars = password.ToCharArray();
            chars[0] = (char)(chars[0] ^ 1);
            string mutatedPassword = new string(chars);

            string recovered = LuxVault.Decrypt(encrypted, mutatedPassword);

            return recovered == null;
        }

        #endregion

        #region Extended Security Properties

        [Fact]
        public void LuxVault_PayloadIntegrity_Exhaustive()
        {
            var pk = "sensitive_private_key_data";
            var password = "very_secure_password";
            byte[] originalPayload = LuxVault.Encrypt(pk, password);

            for (int i = 0; i < originalPayload.Length; i++)
            {
                for (int bit = 0; bit < 8; bit++)
                {
                    byte[] payload = (byte[])originalPayload.Clone();
                    payload[i] ^= (byte)(1 << bit);

                    var result = LuxVault.Decrypt(payload, password);
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
            byte[] c1 = LuxVault.Encrypt("", "password");
            Assert.Equal("", LuxVault.Decrypt(c1, "password"));

            // Long password (1KB)
            string longPassword = new string('a', 1024);
            byte[] c2 = LuxVault.Encrypt("pk", longPassword);
            Assert.Equal("pk", LuxVault.Decrypt(c2, longPassword));

            // Unicode/Emojis
            string emojiPass = "🔑🚀🌈";
            byte[] c3 = LuxVault.Encrypt("pk", emojiPass);
            Assert.Equal("pk", LuxVault.Decrypt(c3, emojiPass));
            
            // Null inputs
            Assert.Throws<ArgumentNullException>(() => LuxVault.Encrypt((string)null!, "pass"));
            Assert.Throws<ArgumentNullException>(() => LuxVault.Encrypt("pk", (string)null!));
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
